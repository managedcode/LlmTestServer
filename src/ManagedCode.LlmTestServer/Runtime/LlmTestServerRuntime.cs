using System.Security.Cryptography;
using System.Text;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Runtime;

public sealed partial class LlmTestServerRuntime
    : ILlmTestServerRuntime,
        ILlmTestServerRuntimeRequestScope,
        ILlmTestServerRuntimeEventLookup,
        ILlmTestServerVideoStore
{
    private const string _tooManyRequestsCode = "too_many_requests";
    private const string _contentFilterCode = "content_filter";
    private const string _requestSupersededCode = "llm_test_server_request_superseded";
    private const string _requestSupersededMessage =
        "The runtime was reset or reconfigured while the request was in flight.";
    private const int _openAiPromptCacheMinimumTokens = 1024;
    private const int _openAiPromptCacheIncrementTokens = 128;
    private const int _mistralPromptCacheMinimumTokens = 64;
    private const int _mistralPromptCacheIncrementTokens = 64;
    private const int _geminiPromptCacheMinimumTokens = 2048;
    private const int _geminiPromptCacheIncrementTokens = 128;
    private const int _maxRetainedEvents = 500;
    private const int _maxRetainedEventCharacters = 8 * 1024 * 1024;
    private const int _maxRetainedMetadataCharacters = 4096;
    private const int _retainedMessageCost = 256;
    private const int _retainedToolCost = 256;
    private const int _retainedChunkCost = 128;
    private const int _retainedOutputDispositionCost = 32;

    private readonly object _gate = new();
    private readonly AsyncLocal<RuntimeRequestContext?> _activeRequest = new();
    private readonly Dictionary<LlmTestServerScenario, HashSet<int>> _scenarioReservations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, int> _promptCacheEntries = new(StringComparer.Ordinal);
    private readonly Queue<string> _promptCacheOrder = [];
    private readonly Queue<LlmTestServerRuntimeEvent> _events = [];
    private readonly Dictionary<string, LlmTestServerRuntimeEvent> _eventsByRequestId = new(
        StringComparer.Ordinal
    );
    private readonly int[] _eventKindCounts = new int[Enum.GetValues<LlmTestServerEventKind>().Length];
    private LlmTestServerConfiguration _configuration = LlmTestServerConfiguration.CreateDefault();
    private int _requestCount;
    private int _totalEvents;
    private int _inputTokens;
    private int _cachedInputTokens;
    private int _cacheCreationInputTokens;
    private int _outputTokens;
    private int _reasoningTokens;
    private int _totalTokens;
    private int _retainedEventCharacters;
    private long _generation;
    private bool _retired;

    public LlmTestServerRuntime()
    {
    }

    public LlmTestServerRuntime(LlmTestServerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = LlmTestServerConfigurationBuilder.Snapshot(configuration);
    }

    public Task ConfigureAsync(
        LlmTestServerConfiguration configuration,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _configuration = LlmTestServerConfigurationBuilder.Snapshot(configuration);
            _scenarioReservations.Clear();
            ClearConversations();
            _wireSequences.Clear();
            _chatResponses.Clear();
            _chatResponseBytes = 0;
            _videos.Clear();
            _videoBytes = 0;
            _videoSequence.Clear();
            _promptCacheEntries.Clear();
            _promptCacheOrder.Clear();
            ResetEventJournal();
            _requestCount = 0;
            _generation++;
        }

        return Task.CompletedTask;
    }

    public Task RetireAsync()
    {
        lock (_gate)
        {
            _retired = true;
            return ResetAsync();
        }
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _scenarioReservations.Clear();
            ClearConversations();
            _wireSequences.Clear();
            _chatResponses.Clear();
            _chatResponseBytes = 0;
            _videos.Clear();
            _videoBytes = 0;
            _videoSequence.Clear();
            _promptCacheEntries.Clear();
            _promptCacheOrder.Clear();
            ResetEventJournal();
            _requestCount = 0;
            _generation++;
        }

        return Task.CompletedTask;
    }

    public bool IsBearerTokenAccepted(string? bearerToken)
    {
        lock (_gate)
        {
            return HasRequiredToken(_configuration.RequiredBearerToken, bearerToken);
        }
    }

    public IReadOnlyList<LlmTestServerModel> GetModels()
    {
        lock (_gate)
        {
            return [.. _configuration.Models];
        }
    }

    public IDisposable BeginRequest(string requestId, string? conversationId = null, string? providerNamespace = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var previousRequest = _activeRequest.Value;
        lock (_gate)
        {
            _activeRequest.Value = new RuntimeRequestContext(requestId, _generation, conversationId, providerNamespace, ReserveResponseMetadata(providerNamespace ?? string.Empty));
        }

        return new RuntimeRequestScope(_activeRequest, previousRequest);
    }

    public bool TryGetEvent(string requestId, out LlmTestServerRuntimeEvent? runtimeEvent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        lock (_gate)
        {
            var found = _eventsByRequestId.TryGetValue(requestId, out var retained);
            runtimeEvent = found ? SnapshotEvent(retained!) : null;
            return found;
        }
    }

    private async Task<LlmTestServerChatResult> CompleteChatCoreAsync(
        LlmTestServerChatRequest request,
        string? bearerToken = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        LlmTestServerScenarioResponse response;
        LlmTestServerScenario? scenario;
        var responsePosition = -1;
        ConversationLease? conversationLease = null;
        long generation;

        lock (_gate)
        {
            generation = _activeRequest.Value?.Generation ?? _generation;
            if (_retired || generation != _generation)
            {
                return CreateSupersededChatResult(request, scenarioId: null);
            }

            if (!HasRequiredToken(_configuration.RequiredBearerToken, bearerToken))
            {
                var usage = CreateChatUsage(request);
                AddEvent(
                    LlmTestServerEventKind.AuthFailed,
                    null,
                    request.ModelId,
                    "Global bearer token requirement failed.",
                    FormatChatRequest(request),
                    usage: usage,
                    chatRequest: request
                );
                return LlmTestServerChatResult.Failure(
                    request.ModelId,
                    401,
                    "invalid_api_key",
                    "The supplied bearer token did not match the configured LLM Test Server token.",
                    usage: usage
                );
            }

            if (!IsConfiguredModel(request.ModelId, LlmTestServerModelKind.Chat))
            {
                var usage = CreateChatUsage(request);
                AddModelNotFoundEvent(
                    request.ModelId,
                    LlmTestServerModelKind.Chat,
                    FormatChatRequest(request),
                    usage,
                    chatRequest: request
                );
                return UnknownModel(request.ModelId, LlmTestServerModelKind.Chat, usage);
            }

            var historyError = RestoreChatResponse(ref request, bearerToken);
            if (historyError is not null)
            {
                AddEvent(LlmTestServerEventKind.ErrorReturned, null, request.ModelId, historyError.ErrorMessage!,
                    FormatChatRequest(request), historyError.ErrorMessage, chatRequest: request);
                return historyError;
            }

            var fault = TryCreateFault(
                request.ModelId,
                request.Messages.Select(message => message.Content),
                FormatChatRequest(request),
                CreateChatUsage(request),
                chatRequest: request
            );
            if (fault is not null)
            {
                return LlmTestServerChatResult.Failure(
                    request.ModelId,
                    fault.StatusCode,
                    fault.Code,
                    fault.Message,
                    usage: fault.Usage
                );
            }

            scenario = SelectChatScenario(request, bearerToken);

            if (scenario is null)
            {
                var usage = CreateChatUsage(request);
                AddEvent(
                    LlmTestServerEventKind.Unmatched,
                    null,
                    request.ModelId,
                    "No configured scenario matched the request.",
                    FormatChatRequest(request),
                    usage: usage,
                    chatRequest: request
                );
                return LlmTestServerChatResult.Failure(
                    request.ModelId,
                    404,
                    "llm_test_server_unmatched_request",
                    "No configured LLM Test Server scenario matched the request.",
                    usage: usage
                );
            }

            if (!HasRequiredToken(scenario.RequiredBearerToken, bearerToken))
            {
                var usage = CreateChatUsage(request);
                AddEvent(
                    LlmTestServerEventKind.AuthFailed,
                    scenario.Id,
                    request.ModelId,
                    "Scenario bearer token requirement failed.",
                    FormatChatRequest(request),
                    usage: usage,
                    chatRequest: request
                );
                return LlmTestServerChatResult.Failure(
                    request.ModelId,
                    401,
                    "invalid_api_key",
                    "The supplied bearer token did not match the scenario token.",
                    scenario.Id,
                    usage
                );
            }

            if (request.PopulateCacheOnly)
            {
                var cacheUsage = CreateChatUsage(request, updatePromptCache: true);
                var usage = cacheUsage with
                {
                    OutputTokens = 0,
                    ReasoningTokens = 0,
                    TotalTokens = cacheUsage.InputTokens,
                };
                AddEvent(LlmTestServerEventKind.Matched, scenario.Id, request.ModelId,
                    "Prompt cache populated without generating a response.", FormatChatRequest(request),
                    string.Empty, usage, chatRequest: request);
                return LlmTestServerChatResult.Success(request.ModelId, scenario.Id, string.Empty, [], usage);
            }

            if (scenario.Conversation is not null)
            {
                var immediate = PrepareConversation(scenario, request, bearerToken, out conversationLease);
                if (immediate is not null) { return immediate; }
                response = conversationLease!.Step.Response;
            }
            else
            {
                if (!_scenarioReservations.TryGetValue(scenario, out var reservations))
                {
                    reservations = [];
                    _scenarioReservations.Add(scenario, reservations);
                }

                responsePosition = 0;
                while (reservations.Contains(responsePosition))
                {
                    responsePosition++;
                }
                if (responsePosition >= scenario.Responses.Count)
                {
                    var usage = CreateChatUsage(request);
                    AddEvent(
                        LlmTestServerEventKind.ScenarioExhausted,
                        scenario.Id,
                        request.ModelId,
                        "Scenario response queue is exhausted.",
                        FormatChatRequest(request),
                        usage: usage,
                        chatRequest: request
                    );
                    return LlmTestServerChatResult.Failure(
                        request.ModelId,
                        409,
                        "llm_test_server_scenario_exhausted",
                        "The matched scenario has no responses left.",
                        scenario.Id,
                        usage
                    );
                }

                response = scenario.Responses[responsePosition];
                var fixtureError = ValidateFixture(request, response);
                if (fixtureError is not null)
                {
                    var usage = CreateChatUsage(request);
                    AddEvent(LlmTestServerEventKind.ErrorReturned, scenario.Id, request.ModelId,
                        "The selected fixture does not satisfy the request.", FormatChatRequest(request),
                        fixtureError, usage, chatRequest: request);
                    return LlmTestServerChatResult.Failure(request.ModelId, 409, "llm_test_server_fixture_mismatch", fixtureError, scenario.Id, usage);
                }

                reservations.Add(responsePosition);
            }

        }

        response = LlmTestServerResponseItems.Project(response);
        try
        {
            if (response.DelayMilliseconds > 0)
            {
                await Task.Delay(response.DelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RollBackResponse(scenario, responsePosition, generation, conversationLease);
            throw;
        }

        lock (_gate)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                RollBackResponse(scenario, responsePosition, generation, conversationLease);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (_retired || generation != _generation)
            {
                return CreateSupersededChatResult(request, scenario.Id);
            }

            var responseText = response.Error?.Message ?? response.Content;
            var responseOutput = response.Error is null
                ? response.Content + string.Concat(response.ToolCalls.Select(call => call.Name + call.ArgumentsJson)) + LlmTestServerResponseItems.NativeTokenText(response.OutputItems)
                : null;
            IReadOnlyList<string> successfulStreamChunks = response.StreamChunks.Count > 0
                ? response.StreamChunks
                : [response.Content];
            LlmTestServerChatResult? completed = null;
            if (response.Error is null)
            {
                completed = RetainChatResponse(request, LlmTestServerChatResult.Success(request.ModelId, scenario.Id,
                    response.Content, successfulStreamChunks) with
                { OutputDisposition = response.OutputDisposition, StreamDelivery = response.StreamDelivery, ToolCalls = [.. response.ToolCalls], OutputItems = LlmTestServerResponseItems.Snapshot(response.OutputItems), WireMetadata = GetResponseMetadata() }, bearerToken);
                if (!completed.IsSuccess)
                {
                    RollBackResponse(scenario, responsePosition, generation, conversationLease);
                    var rejectedUsage = CreateChatUsage(request, responseOutput);
                    AddEvent(LlmTestServerEventKind.ErrorReturned, scenario.Id, request.ModelId, completed.ErrorMessage!,
                        FormatChatRequest(request), completed.ErrorMessage, rejectedUsage, chatRequest: request);
                    return completed;
                }
            }
            var usage = CreateChatUsage(request, responseOutput, updatePromptCache: response.Error is null);
            if (completed is not null) { completed = completed with { Usage = usage }; }
            var finalResult = response.Error is { } error
                ? LlmTestServerChatResult.Failure(request.ModelId, error.StatusCode, error.Code, error.Message, scenario.Id, usage)
                : completed!;
            finalResult = finalResult with { WireMetadata = finalResult.WireMetadata ?? GetResponseMetadata() };
            if (conversationLease is not null) { CommitConversation(conversationLease, finalResult); }
            AddEvent(
                response.Error is null ? LlmTestServerEventKind.Matched : LlmTestServerEventKind.ErrorReturned,
                scenario.Id,
                request.ModelId,
                response.Error?.Message ?? "Scenario matched.",
                FormatChatRequest(request),
                responseText,
                usage,
                chatRequest: request,
                streamChunks: response.Error is null && request.Stream ? successfulStreamChunks : null,
                stepId: conversationLease?.Step.Id,
                logicalTurnIndex: conversationLease?.Turn,
                responseToolCalls: response.Error is null ? response.ToolCalls : null,
                retryAfterSeconds: response.Error?.RetryAfterSeconds,
                wireMetadata: finalResult.WireMetadata,
                responseOutputItems: finalResult.OutputItems, streamDelivery: finalResult.StreamDelivery, outputDisposition: finalResult.OutputDisposition
            );

            if (response.Error is not null)
            {
                return LlmTestServerChatResult.Failure(
                    request.ModelId,
                    response.Error.StatusCode,
                    response.Error.Code,
                    response.Error.Message,
                    scenario.Id,
                    usage
                );
            }

            return completed!;
        }
    }

    private Task<LlmTestServerEmbeddingResult> CreateEmbeddingCoreAsync(
        string modelId,
        IReadOnlyList<string> inputs,
        string? bearerToken = null,
        IReadOnlyList<int?>? outputDimensions = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(inputs);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (IsActiveRequestSuperseded())
            {
                return Task.FromResult(CreateSupersededEmbeddingResult(modelId));
            }

            if (!HasRequiredToken(_configuration.RequiredBearerToken, bearerToken))
            {
                AddEvent(
                    LlmTestServerEventKind.AuthFailed,
                    null,
                    modelId,
                    "Embedding bearer token requirement failed."
                );
                return Task.FromResult(
                    new LlmTestServerEmbeddingResult
                    {
                        IsSuccess = false,
                        StatusCode = 401,
                        ModelId = modelId,
                        ErrorCode = "invalid_api_key",
                        ErrorMessage = "The supplied bearer token did not match the configured LLM Test Server token.",
                    }
                );
            }

            if (!IsConfiguredModel(modelId, LlmTestServerModelKind.Embedding))
            {
                AddModelNotFoundEvent(
                    modelId,
                    LlmTestServerModelKind.Embedding
                );
                return Task.FromResult(UnknownEmbeddingModel(modelId));
            }

            var dimensionError = ValidateEmbeddingDimensions(modelId, inputs.Count, outputDimensions);
            if (dimensionError is not null) { return Task.FromResult(dimensionError); }

            var fault = TryCreateFault(
                modelId,
                inputs,
                request: string.Join("\n", inputs)
            );
            if (fault is not null)
            {
                return Task.FromResult(
                    new LlmTestServerEmbeddingResult
                    {
                        IsSuccess = false,
                        StatusCode = fault.StatusCode,
                        ModelId = modelId,
                        ErrorCode = fault.Code,
                        ErrorMessage = fault.Message,
                    }
                );
            }

            AddEvent(
                LlmTestServerEventKind.Matched,
                null,
                modelId,
                $"Generated deterministic embeddings for {inputs.Count} input value(s)."
            );

            return Task.FromResult(
                new LlmTestServerEmbeddingResult
                {
                    IsSuccess = true,
                    ModelId = modelId,
                    Vectors = inputs.Select((_, index) => _configuration.DefaultEmbeddingVector
                        .Take(outputDimensions?[index] ?? _configuration.DefaultEmbeddingVector.Count).ToList()).ToList(),
                }
            );
        }
    }

    private Task<LlmTestServerImageResult> GenerateImageCoreAsync(
        string modelId,
        string prompt,
        string? bearerToken = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(prompt);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (IsActiveRequestSuperseded())
            {
                return Task.FromResult(CreateSupersededImageResult(modelId));
            }

            if (!HasRequiredToken(_configuration.RequiredBearerToken, bearerToken))
            {
                AddEvent(
                    LlmTestServerEventKind.AuthFailed,
                    null,
                    modelId,
                    "Image auth failed."
                );
                return Task.FromResult(
                    new LlmTestServerImageResult
                    {
                        IsSuccess = false,
                        StatusCode = 401,
                        ModelId = modelId,
                        ErrorCode = "invalid_api_key",
                        ErrorMessage = "The supplied bearer token did not match the configured LLM Test Server token.",
                    }
                );
            }

            if (!IsConfiguredModel(modelId, LlmTestServerModelKind.Image))
            {
                AddModelNotFoundEvent(modelId, LlmTestServerModelKind.Image);
                return Task.FromResult(UnknownImageModel(modelId));
            }

            var fault = TryCreateFault(
                modelId,
                [prompt],
                request: prompt
            );
            if (fault is not null)
            {
                return Task.FromResult(
                    new LlmTestServerImageResult
                    {
                        IsSuccess = false,
                        StatusCode = fault.StatusCode,
                        ModelId = modelId,
                        ErrorCode = fault.Code,
                        ErrorMessage = fault.Message,
                    }
                );
            }

            AddEvent(
                LlmTestServerEventKind.Matched,
                null,
                modelId,
                $"Generated image for '{prompt}'."
            );
            return Task.FromResult(
                new LlmTestServerImageResult
                {
                    IsSuccess = true,
                    ModelId = modelId,
                    DataUri = _configuration.DefaultImageDataUri,
                }
            );
        }
    }

    private Task<LlmTestServerAudioResult> GenerateAudioCoreAsync(
        string modelId,
        string input,
        string? bearerToken = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (IsActiveRequestSuperseded())
            {
                return Task.FromResult(CreateSupersededAudioResult(modelId));
            }

            if (!HasRequiredToken(_configuration.RequiredBearerToken, bearerToken))
            {
                AddEvent(
                    LlmTestServerEventKind.AuthFailed,
                    null,
                    modelId,
                    "Audio auth failed."
                );
                return Task.FromResult(
                    new LlmTestServerAudioResult
                    {
                        IsSuccess = false,
                        StatusCode = 401,
                        ModelId = modelId,
                        ErrorCode = "invalid_api_key",
                        ErrorMessage = "The supplied bearer token did not match the configured LLM Test Server token.",
                    }
                );
            }

            if (!IsConfiguredModel(modelId, LlmTestServerModelKind.Audio))
            {
                AddModelNotFoundEvent(modelId, LlmTestServerModelKind.Audio);
                return Task.FromResult(UnknownAudioModel(modelId));
            }

            var fault = TryCreateFault(
                modelId,
                [input],
                request: input
            );
            if (fault is not null)
            {
                return Task.FromResult(
                    new LlmTestServerAudioResult
                    {
                        IsSuccess = false,
                        StatusCode = fault.StatusCode,
                        ModelId = modelId,
                        ErrorCode = fault.Code,
                        ErrorMessage = fault.Message,
                    }
                );
            }

            AddEvent(
                LlmTestServerEventKind.Matched,
                null,
                modelId,
                $"Generated audio for '{input}'."
            );
            return Task.FromResult(
                new LlmTestServerAudioResult
                {
                    IsSuccess = true,
                    ModelId = modelId,
                    Bytes = [.. _configuration.DefaultAudioBytes],
                    MediaType = _configuration.DefaultAudioMediaType,
                }
            );
        }
    }

    private Task<LlmTestServerTranscriptionResult> TranscribeAudioCoreAsync(
        string modelId,
        string fileName,
        string? prompt = null,
        string? bearerToken = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (IsActiveRequestSuperseded())
            {
                return Task.FromResult(CreateSupersededTranscriptionResult(modelId));
            }

            if (!HasRequiredToken(_configuration.RequiredBearerToken, bearerToken))
            {
                AddEvent(
                    LlmTestServerEventKind.AuthFailed,
                    null,
                    modelId,
                    "Audio transcription auth failed."
                );
                return Task.FromResult(
                    new LlmTestServerTranscriptionResult
                    {
                        IsSuccess = false,
                        StatusCode = 401,
                        ModelId = modelId,
                        ErrorCode = "invalid_api_key",
                        ErrorMessage = "The supplied bearer token did not match the configured LLM Test Server token.",
                    }
                );
            }

            if (!IsConfiguredModel(modelId, LlmTestServerModelKind.Audio))
            {
                AddModelNotFoundEvent(modelId, LlmTestServerModelKind.Audio);
                return Task.FromResult(UnknownTranscriptionModel(modelId));
            }

            var fault = TryCreateFault(
                modelId,
                [fileName, prompt ?? string.Empty],
                request: FormatAudioRequest(fileName, prompt)
            );
            if (fault is not null)
            {
                return Task.FromResult(
                    new LlmTestServerTranscriptionResult
                    {
                        IsSuccess = false,
                        StatusCode = fault.StatusCode,
                        ModelId = modelId,
                        ErrorCode = fault.Code,
                        ErrorMessage = fault.Message,
                    }
                );
            }

            AddEvent(
                LlmTestServerEventKind.Matched,
                null,
                modelId,
                string.IsNullOrWhiteSpace(prompt)
                    ? $"Transcribed audio file '{fileName}'."
                    : $"Transcribed audio file '{fileName}' with prompt."
            );
            return Task.FromResult(
                new LlmTestServerTranscriptionResult
                {
                    IsSuccess = true,
                    ModelId = modelId,
                    Text = _configuration.DefaultTranscriptionText,
                    Usage = _configuration.DefaultTranscriptionUsage,
                }
            );
        }
    }

    private Task<LlmTestServerTranscriptionResult> TranslateAudioCoreAsync(
        string modelId,
        string fileName,
        string? prompt = null,
        string? bearerToken = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (IsActiveRequestSuperseded())
            {
                return Task.FromResult(CreateSupersededTranscriptionResult(modelId));
            }

            if (!HasRequiredToken(_configuration.RequiredBearerToken, bearerToken))
            {
                AddEvent(
                    LlmTestServerEventKind.AuthFailed,
                    null,
                    modelId,
                    "Audio translation auth failed."
                );
                return Task.FromResult(
                    new LlmTestServerTranscriptionResult
                    {
                        IsSuccess = false,
                        StatusCode = 401,
                        ModelId = modelId,
                        ErrorCode = "invalid_api_key",
                        ErrorMessage = "The supplied bearer token did not match the configured LLM Test Server token.",
                    }
                );
            }

            if (!IsConfiguredModel(modelId, LlmTestServerModelKind.Audio))
            {
                AddModelNotFoundEvent(modelId, LlmTestServerModelKind.Audio);
                return Task.FromResult(UnknownTranscriptionModel(modelId));
            }

            var fault = TryCreateFault(
                modelId,
                [fileName, prompt ?? string.Empty],
                request: FormatAudioRequest(fileName, prompt)
            );
            if (fault is not null)
            {
                return Task.FromResult(
                    new LlmTestServerTranscriptionResult
                    {
                        IsSuccess = false,
                        StatusCode = fault.StatusCode,
                        ModelId = modelId,
                        ErrorCode = fault.Code,
                        ErrorMessage = fault.Message,
                    }
                );
            }

            AddEvent(
                LlmTestServerEventKind.Matched,
                null,
                modelId,
                string.IsNullOrWhiteSpace(prompt)
                    ? $"Translated audio file '{fileName}'."
                    : $"Translated audio file '{fileName}' with prompt."
            );
            return Task.FromResult(
                new LlmTestServerTranscriptionResult
                {
                    IsSuccess = true,
                    ModelId = modelId,
                    Text = _configuration.DefaultTranslationText,
                }
            );
        }
    }

    private Task<LlmTestServerVideoResult> GenerateVideoCoreAsync(
        string modelId,
        string prompt,
        string? bearerToken = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(prompt);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (IsActiveRequestSuperseded())
            {
                return Task.FromResult(CreateSupersededVideoResult(modelId));
            }

            var usage = CreateVideoUsage(prompt);
            if (!HasRequiredToken(_configuration.RequiredBearerToken, bearerToken))
            {
                AddEvent(
                    LlmTestServerEventKind.AuthFailed,
                    null,
                    modelId,
                    "Video auth failed.",
                    usage: usage
                );
                return Task.FromResult(
                    new LlmTestServerVideoResult
                    {
                        IsSuccess = false,
                        StatusCode = 401,
                        ModelId = modelId,
                        ErrorCode = "invalid_api_key",
                        ErrorMessage = "The supplied bearer token did not match the configured LLM Test Server token.",
                        Usage = usage,
                    }
                );
            }

            if (!IsConfiguredModel(modelId, LlmTestServerModelKind.Video))
            {
                AddModelNotFoundEvent(
                    modelId,
                    LlmTestServerModelKind.Video,
                    usage: usage
                );
                return Task.FromResult(UnknownVideoModel(modelId, usage));
            }

            var fault = TryCreateFault(
                modelId,
                [prompt],
                request: prompt,
                usage: usage
            );
            if (fault is not null)
            {
                return Task.FromResult(
                    new LlmTestServerVideoResult
                    {
                        IsSuccess = false,
                        StatusCode = fault.StatusCode,
                        ModelId = modelId,
                        ErrorCode = fault.Code,
                        ErrorMessage = fault.Message,
                        Usage = fault.Usage ?? usage,
                    }
                );
            }

            AddEvent(
                LlmTestServerEventKind.Matched,
                null,
                modelId,
                $"Generated video for '{prompt}'.",
                usage: usage
            );
            return Task.FromResult(
                new LlmTestServerVideoResult
                {
                    IsSuccess = true,
                    ModelId = modelId,
                    Prompt = prompt,
                    VideoId = _configuration.DefaultVideoId,
                    GenerationId = _configuration.DefaultVideoGenerationId,
                    Bytes = [.. _configuration.DefaultVideoBytes],
                    MediaType = _configuration.DefaultVideoMediaType,
                    CreatedAt = _configuration.DefaultVideoCreatedAtUnixTime,
                    Size = _configuration.DefaultVideoSize,
                    Seconds = _configuration.DefaultVideoSeconds,
                    Usage = usage,
                }
            );
        }
    }

    public LlmTestServerAssertionSummary GetAssertionSummary()
    {
        lock (_gate)
        {
            return new LlmTestServerAssertionSummary
            {
                TotalEvents = _totalEvents,
                Matched = _eventKindCounts[(int)LlmTestServerEventKind.Matched],
                Unmatched = _eventKindCounts[(int)LlmTestServerEventKind.Unmatched],
                ModelNotFound = _eventKindCounts[(int)LlmTestServerEventKind.ModelNotFound],
                AuthFailed = _eventKindCounts[(int)LlmTestServerEventKind.AuthFailed],
                ScenarioExhausted = _eventKindCounts[(int)LlmTestServerEventKind.ScenarioExhausted],
                ErrorsReturned = _eventKindCounts[(int)LlmTestServerEventKind.ErrorReturned],
                InputTokens = _inputTokens,
                CachedInputTokens = _cachedInputTokens,
                CacheCreationInputTokens = _cacheCreationInputTokens,
                OutputTokens = _outputTokens,
                ReasoningTokens = _reasoningTokens,
                TotalTokens = _totalTokens,
                Events = _events.Select(SnapshotEvent).ToList(),
            };
        }
    }

    private static LlmTestServerRuntimeEvent SnapshotEvent(LlmTestServerRuntimeEvent item)
    {
        return item with
        {
            Messages = item.Messages.Select(message => message with { ToolCalls = [.. message.ToolCalls], OutputItems = LlmTestServerResponseItems.Snapshot(message.OutputItems) }).ToArray(),
            ResponseOutputItems = LlmTestServerResponseItems.Snapshot(item.ResponseOutputItems),
            NativeTools = LlmTestServerResponseItems.SnapshotDeclarations(item.NativeTools),
            StreamChunks = item.StreamChunks.ToArray(),
        };
    }

    private static bool HasRequiredToken(string? required, string? supplied)
    {
        return string.IsNullOrEmpty(required) || string.Equals(required, supplied, StringComparison.Ordinal);
    }

    private static IEnumerable<LlmTestServerScenario> GetChatScenarios(LlmTestServerConfiguration configuration)
    {
        return configuration.ChatScenarios.Concat(
            configuration.Datasets.SelectMany(dataset => dataset.ChatScenarios)
        );
    }

    private bool IsConfiguredModel(string modelId, LlmTestServerModelKind kind)
    {
        return _configuration.Models.Any(model =>
            model.Kind == kind && string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase)
        );
    }

    private int GetReasoningTokens(string modelId)
    {
        return _configuration
            .Models
            .FirstOrDefault(model =>
                model.Kind == LlmTestServerModelKind.Chat
                && string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase)
            )
            ?.ReasoningTokens ?? 0;
    }

    private LlmTestServerRuntimeFault? TryCreateFault(
        string modelId,
        IEnumerable<string?> requestContent,
        string? request = null,
        LlmTestServerTokenUsage? usage = null,
        LlmTestServerChatRequest? chatRequest = null
    )
    {
        var faultSimulation = _configuration.FaultSimulation;

        if (
            faultSimulation.MaxRequestsBeforeRateLimit is int allowedRequests
            && allowedRequests >= 0
            && _requestCount >= allowedRequests
        )
        {
            var message = $"{_tooManyRequestsCode}: configured LLM Test Server request limit was exceeded.";
            AddEvent(
                LlmTestServerEventKind.ErrorReturned,
                null,
                modelId,
                message,
                request,
                message,
                usage,
                chatRequest: chatRequest
            );
            return new LlmTestServerRuntimeFault(429, _tooManyRequestsCode, message, usage);
        }

        _requestCount++;

        var filteredTerm = FindContentFilterTerm(
            requestContent,
            faultSimulation.ContentFilterTerms
        );
        if (filteredTerm is null)
        {
            return null;
        }

        var filterMessage =
            $"{_contentFilterCode}: request matched configured LLM Test Server content filter term '{filteredTerm}'.";
        AddEvent(
            LlmTestServerEventKind.ErrorReturned,
            null,
            modelId,
            filterMessage,
            request,
            filterMessage,
            usage,
            chatRequest: chatRequest
        );
        return new LlmTestServerRuntimeFault(400, _contentFilterCode, filterMessage, usage);
    }

    private static string? FindContentFilterTerm(
        IEnumerable<string?> requestContent,
        IReadOnlyList<string> contentFilterTerms
    )
    {
        if (contentFilterTerms.Count == 0)
        {
            return null;
        }

        foreach (var value in requestContent)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var matchedTerm = contentFilterTerms.FirstOrDefault(term =>
                value.Contains(term, StringComparison.OrdinalIgnoreCase)
            );
            if (matchedTerm is not null)
            {
                return matchedTerm;
            }
        }

        return null;
    }

    private static LlmTestServerChatResult UnknownModel(
        string modelId,
        LlmTestServerModelKind kind,
        LlmTestServerTokenUsage? usage = null
    )
    {
        return LlmTestServerChatResult.Failure(
            modelId,
            404,
            "llm_test_server_unknown_model",
            CreateUnknownModelMessage(modelId, kind),
            usage: usage
        );
    }

    private static LlmTestServerEmbeddingResult UnknownEmbeddingModel(string modelId)
    {
        return new()
        {
            IsSuccess = false,
            StatusCode = 404,
            ModelId = modelId,
            ErrorCode = "llm_test_server_unknown_model",
            ErrorMessage = CreateUnknownModelMessage(modelId, LlmTestServerModelKind.Embedding),
        };
    }

    private static LlmTestServerImageResult UnknownImageModel(string modelId)
    {
        return new()
        {
            IsSuccess = false,
            StatusCode = 404,
            ModelId = modelId,
            ErrorCode = "llm_test_server_unknown_model",
            ErrorMessage = CreateUnknownModelMessage(modelId, LlmTestServerModelKind.Image),
        };
    }

    private static LlmTestServerAudioResult UnknownAudioModel(string modelId)
    {
        return new()
        {
            IsSuccess = false,
            StatusCode = 404,
            ModelId = modelId,
            ErrorCode = "llm_test_server_unknown_model",
            ErrorMessage = CreateUnknownModelMessage(modelId, LlmTestServerModelKind.Audio),
        };
    }

    private static LlmTestServerTranscriptionResult UnknownTranscriptionModel(string modelId)
    {
        return new()
        {
            IsSuccess = false,
            StatusCode = 404,
            ModelId = modelId,
            ErrorCode = "llm_test_server_unknown_model",
            ErrorMessage = CreateUnknownModelMessage(modelId, LlmTestServerModelKind.Audio),
        };
    }

    private static LlmTestServerVideoResult UnknownVideoModel(
        string modelId,
        LlmTestServerTokenUsage? usage = null
    )
    {
        return new()
        {
            IsSuccess = false,
            StatusCode = 404,
            ModelId = modelId,
            ErrorCode = "llm_test_server_unknown_model",
            ErrorMessage = CreateUnknownModelMessage(modelId, LlmTestServerModelKind.Video),
            Usage = usage ?? new LlmTestServerTokenUsage(),
        };
    }

    private static string CreateUnknownModelMessage(string modelId, LlmTestServerModelKind kind)
    {
        return $"Model '{modelId}' is not configured for {kind.ToString().ToLowerInvariant()} requests.";
    }

    private void AddModelNotFoundEvent(
        string modelId,
        LlmTestServerModelKind kind,
        string? request = null,
        LlmTestServerTokenUsage? usage = null,
        LlmTestServerChatRequest? chatRequest = null
    )
    {
        AddEvent(
            LlmTestServerEventKind.ModelNotFound,
            null,
            modelId,
            CreateUnknownModelMessage(modelId, kind),
            request,
            usage: usage,
            chatRequest: chatRequest
        );
    }

    private void RollBackReservedResponse(
        LlmTestServerScenario scenario,
        int reservedPosition,
        long generation
    )
    {
        lock (_gate)
        {
            if (_retired || generation != _generation)
            {
                return;
            }

            if (_scenarioReservations.TryGetValue(scenario, out var reservations))
            {
                reservations.Remove(reservedPosition);
            }
        }
    }

    private LlmTestServerChatResult CreateSupersededChatResult(
        LlmTestServerChatRequest request,
        string? scenarioId
    )
    {
        return LlmTestServerChatResult.Failure(
            request.ModelId,
            409,
            _requestSupersededCode,
            _requestSupersededMessage,
            scenarioId,
            CreateChatUsage(request)
        );
    }

    private bool IsActiveRequestSuperseded()
    {
        return _retired || _activeRequest.Value is { } request && request.Generation != _generation;
    }

    private static LlmTestServerEmbeddingResult CreateSupersededEmbeddingResult(string modelId)
    {
        return new LlmTestServerEmbeddingResult
        {
            IsSuccess = false,
            StatusCode = 409,
            ModelId = modelId,
            ErrorCode = _requestSupersededCode,
            ErrorMessage = _requestSupersededMessage,
        };
    }

    private static LlmTestServerImageResult CreateSupersededImageResult(string modelId)
    {
        return new LlmTestServerImageResult
        {
            IsSuccess = false,
            StatusCode = 409,
            ModelId = modelId,
            ErrorCode = _requestSupersededCode,
            ErrorMessage = _requestSupersededMessage,
        };
    }

    private static LlmTestServerAudioResult CreateSupersededAudioResult(string modelId)
    {
        return new LlmTestServerAudioResult
        {
            IsSuccess = false,
            StatusCode = 409,
            ModelId = modelId,
            ErrorCode = _requestSupersededCode,
            ErrorMessage = _requestSupersededMessage,
        };
    }

    private static LlmTestServerTranscriptionResult CreateSupersededTranscriptionResult(
        string modelId
    )
    {
        return new LlmTestServerTranscriptionResult
        {
            IsSuccess = false,
            StatusCode = 409,
            ModelId = modelId,
            ErrorCode = _requestSupersededCode,
            ErrorMessage = _requestSupersededMessage,
        };
    }

    private static LlmTestServerVideoResult CreateSupersededVideoResult(string modelId)
    {
        return new LlmTestServerVideoResult
        {
            IsSuccess = false,
            StatusCode = 409,
            ModelId = modelId,
            ErrorCode = _requestSupersededCode,
            ErrorMessage = _requestSupersededMessage,
        };
    }

    private static bool ScenarioMatches(LlmTestServerScenario scenario, LlmTestServerChatRequest request)
    {
        if (!string.Equals(scenario.ModelId, request.ModelId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return scenario.Match.Messages.Count == 0 || scenario.Match.Mode switch
        {
            LlmTestServerMatchMode.Exact => ExactMessagesMatch(scenario.Match.Messages, request.Messages),
            _ => ContainsMessagesMatch(scenario.Match.Messages, request.Messages),
        };
    }

    private static bool ExactMessagesMatch(
        IReadOnlyList<LlmTestServerMessage> expected,
        IReadOnlyList<LlmTestServerMessage> actual
    )
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            if (!MessageMatches(expected[i], actual[i], LlmTestServerMatchMode.Exact))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsMessagesMatch(
        IReadOnlyList<LlmTestServerMessage> expected,
        IReadOnlyList<LlmTestServerMessage> actual
    )
    {
        return expected.All(item =>
            actual.Any(candidate => MessageMatches(item, candidate, LlmTestServerMatchMode.Contains))
        );
    }

    private static bool MessageMatches(
        LlmTestServerMessage expected,
        LlmTestServerMessage actual,
        LlmTestServerMatchMode mode
    )
    {
        var roleMatches = string.IsNullOrWhiteSpace(expected.Role)
            || string.Equals(expected.Role, actual.Role, StringComparison.OrdinalIgnoreCase);
        var contentMatches = mode == LlmTestServerMatchMode.Exact
            ? string.Equals(expected.Content, actual.Content, StringComparison.Ordinal)
            : actual.Content.Contains(expected.Content, StringComparison.OrdinalIgnoreCase);

        var expectedDisposition = LlmTestServerOutputDisposition.ForHistory(expected.OutputDisposition);
        var actualDisposition = LlmTestServerOutputDisposition.ForHistory(actual.OutputDisposition);
        return roleMatches && contentMatches
            && ((mode != LlmTestServerMatchMode.Exact && expectedDisposition is null) || expectedDisposition == actualDisposition)
            && ((mode != LlmTestServerMatchMode.Exact && expected.ToolCallId is null) || expected.ToolCallId == actual.ToolCallId)
            && ((mode != LlmTestServerMatchMode.Exact && expected.ToolCalls.Count == 0) || expected.ToolCalls.SequenceEqual(actual.ToolCalls))
            && (expected.OutputItems.Count == 0 || System.Text.Json.JsonSerializer.Serialize(expected.OutputItems) == System.Text.Json.JsonSerializer.Serialize(actual.OutputItems));
    }

    private void AddEvent(
        LlmTestServerEventKind kind,
        string? scenarioId,
        string modelId,
        string message,
        string? request = null,
        string? response = null,
        LlmTestServerTokenUsage? usage = null,
        LlmTestServerChatRequest? chatRequest = null,
        IReadOnlyList<string>? streamChunks = null,
        string? stepId = null,
        int? logicalTurnIndex = null,
        bool isReplay = false,
        IReadOnlyList<LlmTestServerToolCall>? responseToolCalls = null,
        int? retryAfterSeconds = null,
        ManagedCode.LlmTestServer.Determinism.LlmTestServerResponseMetadata? wireMetadata = null,
        IReadOnlyList<LlmTestServerResponseItem>? responseOutputItems = null,
        LlmTestServerStreamDelivery? streamDelivery = null,
        LlmTestServerOutputDisposition? outputDisposition = null
    )
    {
        var activeRequest = _activeRequest.Value;
        if (activeRequest is { } && activeRequest.Value.Generation != _generation)
        {
            return;
        }

        var runtimeEvent = BoundEventPayload(new LlmTestServerRuntimeEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = kind,
            RequestId = activeRequest?.RequestId ?? chatRequest?.RequestId,
            ScenarioId = scenarioId,
            ConversationId = activeRequest?.ConversationId ?? (stepId is null ? null : "default"),
            StepId = stepId,
            TurnIndex = logicalTurnIndex,
            IsReplay = isReplay,
            ResponseToolCalls = responseToolCalls ?? [],
            ResponseOutputItems = responseOutputItems ?? [],
            StreamDelivery = streamDelivery,
            OutputDisposition = outputDisposition,
            RetryAfterSeconds = retryAfterSeconds,
            WireMetadata = wireMetadata ?? activeRequest?.WireMetadata,
            ModelId = modelId,
            Message = message,
            Request = request,
            Messages = chatRequest?.Messages ?? [],
            Tools = chatRequest?.Tools ?? [],
            NativeTools = chatRequest?.NativeTools ?? [],
            Response = response,
            IsStreaming = chatRequest?.Stream ?? false,
            StreamChunks = streamChunks ?? [],
            PromptCachePolicy = chatRequest?.PromptCachePolicy ?? LlmTestServerPromptCachePolicy.None,
            PromptCacheKey = chatRequest?.PromptCacheKey,
            Usage = usage,
        });
        RecordEventTotals(runtimeEvent);
        var retainedCharacters = CountRetainedCharacters(runtimeEvent);
        while (
            _events.Count >= _maxRetainedEvents
            || (_events.Count > 0
                && _retainedEventCharacters + retainedCharacters > _maxRetainedEventCharacters)
        )
        {
            RemoveOldestEvent();
        }

        _events.Enqueue(runtimeEvent);
        _retainedEventCharacters += retainedCharacters;
        if (!string.IsNullOrWhiteSpace(runtimeEvent.RequestId))
        {
            _eventsByRequestId[runtimeEvent.RequestId] = runtimeEvent;
        }
    }

    private void ResetEventJournal()
    {
        _events.Clear();
        _eventsByRequestId.Clear();
        Array.Clear(_eventKindCounts);
        _totalEvents = 0;
        _inputTokens = 0;
        _cachedInputTokens = 0;
        _cacheCreationInputTokens = 0;
        _outputTokens = 0;
        _reasoningTokens = 0;
        _totalTokens = 0;
        _retainedEventCharacters = 0;
    }

    private void RemoveOldestEvent()
    {
        var oldest = _events.Dequeue();
        _retainedEventCharacters -= CountRetainedCharacters(oldest);
        if (!string.IsNullOrWhiteSpace(oldest.RequestId)
            && _eventsByRequestId.TryGetValue(oldest.RequestId, out var indexed)
            && ReferenceEquals(indexed, oldest))
        {
            _eventsByRequestId.Remove(oldest.RequestId);
        }
    }

    private static LlmTestServerRuntimeEvent BoundEventPayload(LlmTestServerRuntimeEvent runtimeEvent)
    {
        var truncated = false;
        var requestId = TruncateMetadata(runtimeEvent.RequestId, ref truncated);
        var scenarioId = TruncateMetadata(runtimeEvent.ScenarioId, ref truncated);
        var conversationId = TruncateMetadata(runtimeEvent.ConversationId, ref truncated);
        var stepId = TruncateMetadata(runtimeEvent.StepId, ref truncated);
        var modelId = TruncateMetadata(runtimeEvent.ModelId, ref truncated) ?? string.Empty;
        var message = TruncateMetadata(runtimeEvent.Message, ref truncated) ?? string.Empty;
        var promptCacheKey = TruncateMetadata(runtimeEvent.PromptCacheKey, ref truncated);
        var remaining = Math.Max(
            0,
            _maxRetainedEventCharacters
                - CountCharacters(requestId)
                - CountCharacters(scenarioId)
                - CountCharacters(conversationId)
                - CountCharacters(stepId)
                - modelId.Length
                - message.Length
                - CountCharacters(promptCacheKey)
                - CountCharacters(runtimeEvent.WireMetadata?.Identity)
                - CountCharacters(runtimeEvent.WireMetadata?.ClockMode)
        );
        var response = RetainText(runtimeEvent.Response, ref remaining, ref truncated);
        var disposition = RetainOutputDisposition(runtimeEvent.OutputDisposition, ref remaining, ref truncated);
        var streamChunks = RetainChunks(runtimeEvent.StreamChunks, ref remaining, ref truncated);
        var messages = RetainMessages(runtimeEvent.Messages, ref remaining, ref truncated);
        var tools = RetainTools(runtimeEvent.Tools, ref remaining, ref truncated);
        var nativeTools = RetainNativeTools(runtimeEvent.NativeTools, ref remaining, ref truncated);
        var responseCalls = RetainResponseCalls(runtimeEvent.ResponseToolCalls, ref remaining, ref truncated);
        var responseItems = RetainOutputItems(runtimeEvent.ResponseOutputItems, ref remaining, ref truncated);
        var request = RetainText(runtimeEvent.Request, ref remaining, ref truncated);

        return runtimeEvent with
        {
            RequestId = requestId,
            ScenarioId = scenarioId,
            ConversationId = conversationId,
            StepId = stepId,
            ModelId = modelId,
            Message = message,
            PromptCacheKey = promptCacheKey,
            Messages = messages,
            Tools = tools,
            NativeTools = nativeTools,
            ResponseToolCalls = responseCalls,
            ResponseOutputItems = responseItems,
            Response = response,
            OutputDisposition = disposition,
            StreamChunks = streamChunks,
            Request = request,
            PayloadTruncated = truncated,
        };
    }

    private static IReadOnlyList<LlmTestServerMessage> RetainMessages(
        IReadOnlyList<LlmTestServerMessage> messages,
        ref int remaining,
        ref bool truncated
    )
    {
        if (messages.Count == 0)
        {
            return [];
        }

        var maximumItemCount = remaining / _retainedMessageCost;
        var retained = new List<LlmTestServerMessage>(Math.Min(messages.Count, maximumItemCount));
        foreach (var source in messages)
        {
            if (remaining < _retainedMessageCost)
            {
                truncated = true;
                break;
            }

            remaining -= _retainedMessageCost;
            var role = RetainRequiredText(source.Role, ref remaining, ref truncated);
            var content = RetainRequiredText(source.Content, ref remaining, ref truncated);
            var callId = source.ToolCallId is null ? null : RetainRequiredText(source.ToolCallId, ref remaining, ref truncated);
            var calls = new List<LlmTestServerToolCall>();
            foreach (var call in source.ToolCalls)
            {
                if (remaining < _retainedMessageCost) { truncated = true; break; }
                remaining -= _retainedMessageCost;
                calls.Add(new() { Id = RetainRequiredText(call.Id, ref remaining, ref truncated), Name = RetainRequiredText(call.Name, ref remaining, ref truncated), ArgumentsJson = RetainRequiredText(call.ArgumentsJson, ref remaining, ref truncated), ContinuationToken = RetainText(call.ContinuationToken, ref remaining, ref truncated) });
            }
            var outputItems = RetainOutputItems(source.OutputItems, ref remaining, ref truncated);
            var disposition = RetainOutputDisposition(source.OutputDisposition, ref remaining, ref truncated);
            retained.Add(new LlmTestServerMessage { Role = role, Content = content, ToolCallId = callId, ToolCalls = calls, OutputItems = outputItems, OutputDisposition = disposition });
        }

        return retained.Count == 0 ? [] : Array.AsReadOnly(retained.ToArray());
    }

    private static List<LlmTestServerNativeToolDeclaration> RetainNativeTools(IReadOnlyList<LlmTestServerNativeToolDeclaration> tools, ref int remaining, ref bool truncated)
    {
        var retained = new List<LlmTestServerNativeToolDeclaration>();
        foreach (var tool in tools)
        {
            var cost = NativeToolCharacters(tool);
            if (cost > remaining) { truncated = true; break; }
            remaining -= cost;
            retained.AddRange(LlmTestServerResponseItems.SnapshotDeclarations([tool]));
        }
        return retained;
    }

    private static int NativeToolCharacters(LlmTestServerNativeToolDeclaration tool)
    {
        return _retainedToolCost + System.Text.Json.JsonSerializer.Serialize(tool).Length;
    }

    private static List<LlmTestServerResponseItem> RetainOutputItems(IReadOnlyList<LlmTestServerResponseItem> items, ref int remaining, ref bool truncated)
    {
        var retained = new List<LlmTestServerResponseItem>();
        foreach (var item in items)
        {
            var cost = OutputItemCharacters(item);
            if (cost > remaining) { truncated = true; break; }
            remaining -= cost;
            retained.AddRange(LlmTestServerResponseItems.Snapshot([item]));
        }
        return retained;
    }

    private static int OutputItemCharacters(LlmTestServerResponseItem item)
    {
        return _retainedMessageCost + System.Text.Json.JsonSerializer.Serialize(item).Length;
    }

    private static IReadOnlyList<LlmTestServerToolCall> RetainResponseCalls(IReadOnlyList<LlmTestServerToolCall> calls, ref int remaining, ref bool truncated)
    {
        var retained = new List<LlmTestServerToolCall>();
        foreach (var call in calls)
        {
            if (remaining < _retainedMessageCost) { truncated = true; break; }
            remaining -= _retainedMessageCost;
            retained.Add(new() { Id = RetainRequiredText(call.Id, ref remaining, ref truncated), Name = RetainRequiredText(call.Name, ref remaining, ref truncated), ArgumentsJson = RetainRequiredText(call.ArgumentsJson, ref remaining, ref truncated), ContinuationToken = RetainText(call.ContinuationToken, ref remaining, ref truncated) });
        }
        return retained.ToArray();
    }

    private static IReadOnlyList<LlmTestServerToolDefinition> RetainTools(
        IReadOnlyList<LlmTestServerToolDefinition> tools, ref int remaining, ref bool truncated)
    {
        if (tools.Count == 0)
        {
            return [];
        }

        var retained = new List<LlmTestServerToolDefinition>(Math.Min(tools.Count, remaining / _retainedToolCost));
        foreach (var tool in tools)
        {
            if (remaining < _retainedToolCost) { truncated = true; break; }
            remaining -= _retainedToolCost;
            retained.Add(new LlmTestServerToolDefinition
            {
                Name = RetainRequiredText(tool.Name, ref remaining, ref truncated),
                Description = RetainText(tool.Description, ref remaining, ref truncated),
                ParametersJson = RetainRequiredText(tool.ParametersJson, ref remaining, ref truncated)
            });
        }
        return retained.Count == 0 ? [] : Array.AsReadOnly(retained.ToArray());
    }

    private static IReadOnlyList<string> RetainChunks(
        IReadOnlyList<string> chunks,
        ref int remaining,
        ref bool truncated
    )
    {
        if (chunks.Count == 0)
        {
            return [];
        }

        var maximumItemCount = remaining / _retainedChunkCost;
        var retained = new List<string>(Math.Min(chunks.Count, maximumItemCount));
        foreach (var chunk in chunks)
        {
            if (remaining < _retainedChunkCost)
            {
                truncated = true;
                break;
            }

            remaining -= _retainedChunkCost;
            retained.Add(RetainRequiredText(chunk, ref remaining, ref truncated));
        }

        return retained.Count == 0 ? [] : Array.AsReadOnly(retained.ToArray());
    }

    private static string? TruncateMetadata(string? value, ref bool truncated)
    {
        if (value is null || value.Length <= _maxRetainedMetadataCharacters)
        {
            return value;
        }

        truncated = true;
        return value[.._maxRetainedMetadataCharacters];
    }

    private static string? RetainText(string? value, ref int remaining, ref bool truncated)
    {
        if (value is null)
        {
            return null;
        }

        return RetainRequiredText(value, ref remaining, ref truncated);
    }

    private static string RetainRequiredText(
        string value,
        ref int remaining,
        ref bool truncated
    )
    {
        if (value.Length <= remaining)
        {
            remaining -= value.Length;
            return value;
        }

        truncated = true;
        var retained = remaining == 0 ? string.Empty : value[..remaining];
        remaining = 0;
        return retained;
    }

    private static LlmTestServerOutputDisposition? RetainOutputDisposition(LlmTestServerOutputDisposition? disposition, ref int remaining, ref bool truncated)
    {
        if (disposition is null) { return null; }
        if (remaining < _retainedOutputDispositionCost) { truncated = true; return null; }
        remaining -= _retainedOutputDispositionCost;
        return disposition with
        {
            Category = RetainText(disposition.Category, ref remaining, ref truncated),
            Explanation = RetainText(disposition.Explanation, ref remaining, ref truncated),
        };
    }

    private static int OutputDispositionCharacters(LlmTestServerOutputDisposition? disposition)
    {
        return disposition is null ? 0 : _retainedOutputDispositionCost + CountCharacters(disposition.Category) + CountCharacters(disposition.Explanation);
    }

    private static int CountRetainedCharacters(LlmTestServerRuntimeEvent runtimeEvent)
    {
        var total = CountCharacters(runtimeEvent.RequestId)
            + CountCharacters(runtimeEvent.ScenarioId)
            + CountCharacters(runtimeEvent.ConversationId)
            + CountCharacters(runtimeEvent.StepId)
            + runtimeEvent.ModelId.Length
            + runtimeEvent.Message.Length
            + CountCharacters(runtimeEvent.Request)
            + CountCharacters(runtimeEvent.Response)
            + CountCharacters(runtimeEvent.PromptCacheKey)
            + CountCharacters(runtimeEvent.WireMetadata?.Identity)
            + CountCharacters(runtimeEvent.WireMetadata?.ClockMode)
            + OutputDispositionCharacters(runtimeEvent.OutputDisposition);
        foreach (var item in runtimeEvent.Messages)
        {
            total += _retainedMessageCost + item.Role.Length + item.Content.Length + CountCharacters(item.ToolCallId)
                + OutputDispositionCharacters(item.OutputDisposition)
                + item.ToolCalls.Sum(call => _retainedMessageCost + call.Id.Length + call.Name.Length + call.ArgumentsJson.Length + (call.ContinuationToken?.Length ?? 0))
                + item.OutputItems.Sum(OutputItemCharacters);
        }

        total += runtimeEvent.ResponseOutputItems.Sum(OutputItemCharacters) + runtimeEvent.NativeTools.Sum(NativeToolCharacters);
        foreach (var call in runtimeEvent.ResponseToolCalls)
        { total += _retainedMessageCost + call.Id.Length + call.Name.Length + call.ArgumentsJson.Length + (call.ContinuationToken?.Length ?? 0); }
        foreach (var item in runtimeEvent.Tools)
        {
            total += _retainedToolCost + item.Name.Length + CountCharacters(item.Description) + item.ParametersJson.Length;
        }
        foreach (var item in runtimeEvent.StreamChunks)
        {
            total += _retainedChunkCost + item.Length;
        }

        return total;
    }

    private static int CountCharacters(string? value)
    {
        return value?.Length ?? 0;
    }

    private void RecordEventTotals(LlmTestServerRuntimeEvent runtimeEvent)
    {
        _totalEvents++;
        _eventKindCounts[(int)runtimeEvent.Kind]++;
        if (runtimeEvent.Usage is not { } usage)
        {
            return;
        }

        _inputTokens += usage.InputTokens;
        _cachedInputTokens += usage.CachedInputTokens;
        _cacheCreationInputTokens += usage.CacheCreationInputTokens;
        _outputTokens += usage.OutputTokens;
        _reasoningTokens += usage.ReasoningTokens;
        _totalTokens += usage.TotalTokens;
    }

    private static string MessageTokenText(LlmTestServerMessage message)
    {
        var text = message.ToolCalls.Count == 0 && message.ToolCallId is null ? message.Content
        : message.Content + " " + message.ToolCallId + " " + string.Join(" ", message.ToolCalls.Select(call => call.Id + " " + call.Name + " " + call.ArgumentsJson));
        return text + LlmTestServerResponseItems.NativeTokenText(message.OutputItems);
    }

    private LlmTestServerTokenUsage CreateChatUsage(
        LlmTestServerChatRequest request,
        string? response = null,
        bool updatePromptCache = false
    )
    {
        var composition = CreateInputComposition(request);
        if (request.NativeTools.Count > 0)
        {
            composition = composition with
            {
                ToolDefinitionTokens = composition.ToolDefinitionTokens
                    + LlmTestServerTokenCounter.CountTextTokens(NativeDeclarationTokenText(request)),
            };
        }
        var inputTokens = composition.TotalTokens;
        var (cachedInputTokens, cacheCreationInputTokens) = CalculatePromptCacheUsage(
            request,
            updatePromptCache
        );
        var visibleOutputTokens = LlmTestServerTokenCounter.CountTextTokens(response);
        var reasoningTokens = response is null ? 0 : GetReasoningTokens(request.ModelId);
        var outputTokens = visibleOutputTokens + reasoningTokens;
        return new LlmTestServerTokenUsage
        {
            InputTokens = inputTokens,
            CachedInputTokens = cachedInputTokens,
            CacheCreationInputTokens = cacheCreationInputTokens,
            OutputTokens = outputTokens,
            ReasoningTokens = reasoningTokens,
            TotalTokens = inputTokens + outputTokens,
            InputComposition = composition,
        };
    }

    private static LlmTestServerInputTokenComposition CreateInputComposition(LlmTestServerChatRequest request)
    {
        int system = 0, user = 0, assistant = 0, toolCalls = 0, toolResults = 0, other = 0;
        foreach (var message in request.Messages)
        {
            var tokens = LlmTestServerTokenCounter.CountTextTokens(MessageTokenText(message));
            if (message.ToolCallId is not null || IsRole(message, "tool") || IsRole(message, "function"))
            {
                toolResults += tokens;
                continue;
            }

            // Tool-call arguments are attributed separately; the remainder of the message stays with its role.
            var roleTokens = message.ToolCalls.Count == 0
                ? tokens
                : Math.Min(tokens, LlmTestServerTokenCounter.CountTextTokens(message.Content));
            toolCalls += tokens - roleTokens;
            if (IsRole(message, "system") || IsRole(message, "developer"))
            {
                system += roleTokens;
            }
            else if (IsRole(message, "user"))
            {
                user += roleTokens;
            }
            else if (IsRole(message, "assistant") || IsRole(message, "model") || IsRole(message, "chatbot"))
            {
                assistant += roleTokens;
            }
            else
            {
                other += roleTokens;
            }
        }

        return new LlmTestServerInputTokenComposition
        {
            SystemTokens = system,
            UserTokens = user,
            AssistantTokens = assistant,
            ToolCallTokens = toolCalls,
            ToolResultTokens = toolResults,
            ToolDefinitionTokens = request.Tools.Sum(tool =>
                LlmTestServerTokenCounter.CountTextTokens(tool.Name + " " + tool.Description + " " + tool.ParametersJson)),
            OtherTokens = other,
        };

        static bool IsRole(LlmTestServerMessage message, string role)
        {
            return string.Equals(message.Role, role, StringComparison.OrdinalIgnoreCase);
        }
    }

    private (int CachedInputTokens, int CacheCreationInputTokens) CalculatePromptCacheUsage(
        LlmTestServerChatRequest request,
        bool updatePromptCache
    )
    {
        if (!updatePromptCache || _configuration.MaxPromptCacheEntries == 0 || request.PromptCachePolicy == LlmTestServerPromptCachePolicy.None)
        {
            return (0, 0);
        }

        var candidates = CreatePromptCacheCandidates(request);
        if (candidates.Count == 0)
        {
            return (0, 0);
        }

        var cachedInputTokens = 0;
        var maxCacheableTokens = 0;

        foreach (var candidate in candidates)
        {
            if (_promptCacheEntries.TryGetValue(candidate.Key, out var cachedTokens))
            {
                cachedInputTokens = Math.Max(cachedInputTokens, cachedTokens);
            }

            maxCacheableTokens = Math.Max(maxCacheableTokens, candidate.Tokens);
        }

        foreach (var candidate in candidates)
        {
            if (!_promptCacheEntries.ContainsKey(candidate.Key))
            {
                while (_promptCacheEntries.Count >= _configuration.MaxPromptCacheEntries)
                {
                    _promptCacheEntries.Remove(_promptCacheOrder.Dequeue());
                }
                _promptCacheOrder.Enqueue(candidate.Key);
            }
            _promptCacheEntries[candidate.Key] = candidate.Tokens;
        }

        return (cachedInputTokens, request.PromptCachePolicy == LlmTestServerPromptCachePolicy.Ollama
            ? 0
            : Math.Max(0, maxCacheableTokens - cachedInputTokens));
    }

    private List<PromptCacheCandidate> CreatePromptCacheCandidates(
        LlmTestServerChatRequest request
    )
    {
        var candidates = new List<PromptCacheCandidate>();
        var cachePrefix = new StringBuilder();
        var toolText = string.Join("\n", request.Tools.Select(tool => tool.Name + " " + tool.Description + " " + tool.ParametersJson));
        var nativeText = NativeDeclarationTokenText(request);
        if (nativeText.Length > 0) { toolText += "\n" + nativeText; }
        cachePrefix.Append(toolText);
        var cumulativeTokens = request.Tools.Sum(tool => LlmTestServerTokenCounter.CountTextTokens(
            tool.Name + " " + tool.Description + " " + tool.ParametersJson))
            + LlmTestServerTokenCounter.CountTextTokens(nativeText);

        foreach (var message in request.Messages)
        {
            cumulativeTokens += LlmTestServerTokenCounter.CountTextTokens(MessageTokenText(message));
            cachePrefix
                .Append("role:")
                .Append(message.Role)
                .Append('\u001f')
                .Append("content:")
                .Append(MessageTokenText(message))
                .Append('\u001e');

            var historyDisposition = LlmTestServerOutputDisposition.ForHistory(message.OutputDisposition);
            if (historyDisposition is not null)
            {
                cachePrefix.Append("output-disposition:")
                    .Append(System.Text.Json.JsonSerializer.Serialize(historyDisposition)).Append('\u001e');
            }
            if (message.ToolCalls.Any(call => call.ContinuationToken is not null))
            {
                cachePrefix.Append("continuations:")
                    .Append(System.Text.Json.JsonSerializer.Serialize(message.ToolCalls
                        .Where(call => call.ContinuationToken is not null).Select(call => new { call.Id, call.ContinuationToken })))
                    .Append('\u001e');
            }

            var cacheableTokens = RoundPromptCacheTokens(
                request.PromptCachePolicy,
                cumulativeTokens
            );
            if (cacheableTokens <= 0)
            {
                continue;
            }

            candidates.Add(
                new PromptCacheCandidate(
                    CreatePromptCacheEntryKey(request, cachePrefix.ToString(), cacheableTokens),
                    cacheableTokens
                )
            );
        }

        return candidates;
    }

    private static int RoundPromptCacheTokens(
        LlmTestServerPromptCachePolicy policy,
        int tokenCount
    )
    {
        var minimumTokens = GetPromptCacheMinimumTokens(policy);
        if (tokenCount < minimumTokens)
        {
            return 0;
        }

        var incrementTokens = GetPromptCacheIncrementTokens(policy);
        return minimumTokens + ((tokenCount - minimumTokens) / incrementTokens * incrementTokens);
    }

    private static int GetPromptCacheMinimumTokens(LlmTestServerPromptCachePolicy policy)
    {
        return policy switch
        {
            LlmTestServerPromptCachePolicy.Mistral => _mistralPromptCacheMinimumTokens,
            LlmTestServerPromptCachePolicy.Gemini => _geminiPromptCacheMinimumTokens,
            LlmTestServerPromptCachePolicy.Ollama => 1,
            _ => _openAiPromptCacheMinimumTokens,
        };
    }

    private static int GetPromptCacheIncrementTokens(LlmTestServerPromptCachePolicy policy)
    {
        return policy switch
        {
            LlmTestServerPromptCachePolicy.Mistral => _mistralPromptCacheIncrementTokens,
            LlmTestServerPromptCachePolicy.Gemini => _geminiPromptCacheIncrementTokens,
            LlmTestServerPromptCachePolicy.Ollama => 1,
            _ => _openAiPromptCacheIncrementTokens,
        };
    }

    private string CreatePromptCacheEntryKey(
        LlmTestServerChatRequest request,
        string prefix,
        int cacheableTokens
    )
    {
        var keyMaterial = string.Join(
            '\u001d',
            _activeRequest.Value?.ProviderNamespace ?? string.Empty,
            request.PromptCachePolicy.ToString(),
            request.ModelId,
            request.PromptCacheKey ?? string.Empty,
            cacheableTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            prefix
        );
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial)));
    }

    private readonly record struct PromptCacheCandidate(string Key, int Tokens);

    private readonly record struct RuntimeRequestContext(string RequestId, long Generation, string? ConversationId, string? ProviderNamespace, ManagedCode.LlmTestServer.Determinism.LlmTestServerResponseMetadata WireMetadata);

    private static LlmTestServerTokenUsage CreateVideoUsage(string prompt)
    {
        var inputTokens = LlmTestServerTokenCounter.CountTextTokens(prompt);
        return new LlmTestServerTokenUsage
        {
            InputTokens = inputTokens,
            TotalTokens = inputTokens,
        };
    }

    private static string FormatChatRequest(LlmTestServerChatRequest request)
    {
        if (request.Messages.Count == 0)
        {
            return "(no messages)";
        }

        var builder = new StringBuilder();
        foreach (var message in request.Messages)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(message.Role).Append(": ").Append(Truncate(message.Content, 800));
        }

        return builder.ToString();
    }

    private static string FormatAudioRequest(string fileName, string? prompt)
    {
        return string.IsNullOrWhiteSpace(prompt)
            ? $"file: {fileName}"
            : $"file: {fileName}\nprompt: {Truncate(prompt, 800)}";
    }

    private static string Truncate(string value, int maxLength)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "…";
    }

    private sealed record LlmTestServerRuntimeFault(
        int StatusCode,
        string Code,
        string Message,
        LlmTestServerTokenUsage? Usage = null
    );

    private sealed class RuntimeRequestScope(
        AsyncLocal<RuntimeRequestContext?> activeRequest,
        RuntimeRequestContext? previousRequest
    ) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            activeRequest.Value = previousRequest;
            _disposed = true;
        }
    }
}
