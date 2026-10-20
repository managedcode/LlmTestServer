using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Configuration;

public sealed partial class LlmTestServerConfigurationBuilder
{
    private LlmTestServerConfiguration _configuration = LlmTestServerConfiguration.CreateDefault();

    public LlmTestServerConfigurationBuilder AddModel(
        string id,
        LlmTestServerModelKind kind,
        int reasoningTokens = 0
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (reasoningTokens < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reasoningTokens),
                "Reasoning token count must be zero or greater."
            );
        }

        _configuration.Models.RemoveAll(model =>
            string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase)
        );
        _configuration.Models.Add(
            new LlmTestServerModel
            {
                Id = id,
                Kind = kind,
                ReasoningTokens = kind == LlmTestServerModelKind.Chat ? reasoningTokens : 0,
            }
        );

        return this;
    }

    public LlmTestServerConfigurationBuilder AddReasoningChatModel(string id, int reasoningTokens)
    {
        if (reasoningTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reasoningTokens),
                "Reasoning chat models must declare at least one reasoning token."
            );
        }

        return AddModel(id, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerConfigurationBuilder AddGpt55(int reasoningTokens = 0)
    {
        return AddModel(LlmTestServerKnownModelIds.Gpt55, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerConfigurationBuilder AddGpt55Pro(int reasoningTokens = 0)
    {
        return AddModel(LlmTestServerKnownModelIds.Gpt55Pro, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerConfigurationBuilder AddGpt54(int reasoningTokens = 0)
    {
        return AddModel(LlmTestServerKnownModelIds.Gpt54, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerConfigurationBuilder AddGpt54Pro(int reasoningTokens = 0)
    {
        return AddModel(LlmTestServerKnownModelIds.Gpt54Pro, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerConfigurationBuilder AddGpt54Mini(int reasoningTokens = 0)
    {
        return AddModel(LlmTestServerKnownModelIds.Gpt54Mini, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerConfigurationBuilder AddGpt54Nano(int reasoningTokens = 0)
    {
        return AddModel(LlmTestServerKnownModelIds.Gpt54Nano, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerConfigurationBuilder AddGpt5(int reasoningTokens = 0)
    {
        return AddModel(LlmTestServerKnownModelIds.Gpt5, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerConfigurationBuilder AddGpt5Pro(int reasoningTokens = 0)
    {
        return AddModel(LlmTestServerKnownModelIds.Gpt5Pro, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerConfigurationBuilder AddGpt5Mini(int reasoningTokens = 0)
    {
        return AddModel(LlmTestServerKnownModelIds.Gpt5Mini, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerConfigurationBuilder AddGpt5Nano(int reasoningTokens = 0)
    {
        return AddModel(LlmTestServerKnownModelIds.Gpt5Nano, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerConfigurationBuilder AddGpt41()
    {
        return AddModel(LlmTestServerKnownModelIds.Gpt41, LlmTestServerModelKind.Chat);
    }

    public LlmTestServerConfigurationBuilder AddGpt41Mini()
    {
        return AddModel(LlmTestServerKnownModelIds.Gpt41Mini, LlmTestServerModelKind.Chat);
    }

    public LlmTestServerConfigurationBuilder AddGpt4OMini()
    {
        return AddModel(LlmTestServerKnownModelIds.Gpt4OMini, LlmTestServerModelKind.Chat);
    }

    public LlmTestServerConfigurationBuilder AddTextEmbedding3Small()
    {
        return AddModel(LlmTestServerKnownModelIds.TextEmbedding3Small, LlmTestServerModelKind.Embedding);
    }

    public LlmTestServerConfigurationBuilder AddTextEmbedding3Large()
    {
        return AddModel(LlmTestServerKnownModelIds.TextEmbedding3Large, LlmTestServerModelKind.Embedding);
    }

    public LlmTestServerConfigurationBuilder AddTextEmbeddingAda002()
    {
        return AddModel(LlmTestServerKnownModelIds.TextEmbeddingAda002, LlmTestServerModelKind.Embedding);
    }

    public LlmTestServerConfigurationBuilder AddGptImage2()
    {
        return AddModel(LlmTestServerKnownModelIds.GptImage2, LlmTestServerModelKind.Image);
    }

    public LlmTestServerConfigurationBuilder AddGptImage15()
    {
        return AddModel(LlmTestServerKnownModelIds.GptImage15, LlmTestServerModelKind.Image);
    }

    public LlmTestServerConfigurationBuilder AddGptImage1()
    {
        return AddModel(LlmTestServerKnownModelIds.GptImage1, LlmTestServerModelKind.Image);
    }

    public LlmTestServerConfigurationBuilder AddGptImage1Mini()
    {
        return AddModel(LlmTestServerKnownModelIds.GptImage1Mini, LlmTestServerModelKind.Image);
    }

    public LlmTestServerConfigurationBuilder AddGpt4OMiniTts()
    {
        return AddModel(LlmTestServerKnownModelIds.Gpt4OMiniTts, LlmTestServerModelKind.Audio);
    }

    public LlmTestServerConfigurationBuilder AddTts1()
    {
        return AddModel(LlmTestServerKnownModelIds.Tts1, LlmTestServerModelKind.Audio);
    }

    public LlmTestServerConfigurationBuilder AddTts1Hd()
    {
        return AddModel(LlmTestServerKnownModelIds.Tts1Hd, LlmTestServerModelKind.Audio);
    }

    public LlmTestServerConfigurationBuilder AddSora2()
    {
        return AddModel(LlmTestServerKnownModelIds.Sora2, LlmTestServerModelKind.Video);
    }

    public LlmTestServerConfigurationBuilder AddSora2Pro()
    {
        return AddModel(LlmTestServerKnownModelIds.Sora2Pro, LlmTestServerModelKind.Video);
    }

    public LlmTestServerConfigurationBuilder AddDefaultOpenAiModels()
    {
        return AddGpt41Mini()
            .AddTextEmbedding3Small()
            .AddGptImage1()
            .AddGpt4OMiniTts()
            .AddSora2();
    }

    public LlmTestServerConfigurationBuilder AddCurrentOpenAiChatModels(int reasoningTokens = 0)
    {
        return AddGpt55(reasoningTokens)
            .AddGpt55Pro(reasoningTokens)
            .AddGpt54(reasoningTokens)
            .AddGpt54Pro(reasoningTokens)
            .AddGpt54Mini(reasoningTokens)
            .AddGpt54Nano(reasoningTokens)
            .AddGpt5(reasoningTokens)
            .AddGpt5Pro(reasoningTokens)
            .AddGpt5Mini(reasoningTokens)
            .AddGpt5Nano(reasoningTokens)
            .AddGpt41()
            .AddGpt41Mini()
            .AddGpt4OMini();
    }

    public LlmTestServerConfigurationBuilder AddOpenAiEmbeddingModels()
    {
        return AddTextEmbedding3Small()
            .AddTextEmbedding3Large()
            .AddTextEmbeddingAda002();
    }

    public LlmTestServerConfigurationBuilder AddOpenAiImageModels()
    {
        return AddGptImage2()
            .AddGptImage15()
            .AddGptImage1()
            .AddGptImage1Mini();
    }

    public LlmTestServerConfigurationBuilder AddOpenAiAudioModels()
    {
        return AddGpt4OMiniTts()
            .AddTts1()
            .AddTts1Hd();
    }

    public LlmTestServerConfigurationBuilder AddOpenAiVideoModels()
    {
        return AddSora2()
            .AddSora2Pro();
    }

    public LlmTestServerConfigurationBuilder AddKnownOpenAiModels(int reasoningTokens = 0)
    {
        return AddCurrentOpenAiChatModels(reasoningTokens)
            .AddOpenAiEmbeddingModels()
            .AddOpenAiImageModels()
            .AddOpenAiAudioModels()
            .AddOpenAiVideoModels();
    }

    public LlmTestServerConfigurationBuilder RequireBearerToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        _configuration = _configuration with { RequiredBearerToken = token };
        return this;
    }

    public LlmTestServerConfigurationBuilder SimulateRateLimitAfter(int allowedRequests)
    {
        if (allowedRequests < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(allowedRequests),
                "Allowed request count must be zero or greater."
            );
        }

        _configuration = _configuration with
        {
            FaultSimulation = _configuration.FaultSimulation with
            {
                MaxRequestsBeforeRateLimit = allowedRequests,
            },
        };
        return this;
    }

    public LlmTestServerConfigurationBuilder SimulateContentFilter(params string[] blockedTerms)
    {
        ArgumentNullException.ThrowIfNull(blockedTerms);
        if (blockedTerms.Length == 0)
        {
            throw new ArgumentException(
                "At least one content filter term is required.",
                nameof(blockedTerms)
            );
        }

        var terms = blockedTerms
            .Select(term =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(term);
                return term.Trim();
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _configuration = _configuration with
        {
            FaultSimulation = _configuration.FaultSimulation with
            {
                ContentFilterTerms = terms,
            },
        };
        return this;
    }

    public LlmTestServerConfigurationBuilder AddChatScenario(
        string id,
        Action<LlmTestServerScenarioBuilder> configure
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new LlmTestServerScenarioBuilder(id);
        configure(builder);
        var scenario = builder.Build();

        _configuration.ChatScenarios.RemoveAll(existing =>
            string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase)
        );
        _configuration.ChatScenarios.Add(scenario);

        return this;
    }

    public LlmTestServerConfigurationBuilder AddDecisionModel(string id, ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionProvider provider)
    {
        if (!Enum.IsDefined(provider)) { throw new ArgumentOutOfRangeException(nameof(provider)); }
        AddModel(id, LlmTestServerModelKind.Decision);
        var index = _configuration.Models.FindIndex(model => model.Id == id);
        _configuration.Models[index] = _configuration.Models[index] with { DecisionProvider = provider };
        return this;
    }

    public LlmTestServerConfigurationBuilder AddJevLatest()
    {
        return AddDecisionModel(ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionModelIds.TypeSafeJev, ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionProvider.TypeSafe);
    }

    public LlmTestServerConfigurationBuilder AddKevLatest()
    {
        return AddDecisionModel(ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionModelIds.Kev, ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionProvider.Kev);
    }

    public LlmTestServerConfigurationBuilder AddClef()
    {
        return AddDecisionModel(ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionModelIds.CloudflareClef, ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionProvider.Cloudflare);
    }

    public LlmTestServerConfigurationBuilder AddClefFlash()
    {
        return AddDecisionModel(ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionModelIds.CloudflareClefFlash, ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionProvider.Cloudflare);
    }

    public LlmTestServerConfigurationBuilder AddOpenAiDecisionModel(string id)
    {
        return AddDecisionModel(id, ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionProvider.OpenAI);
    }

    public LlmTestServerConfigurationBuilder AddDecisionScenario(ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionScenario scenario)
    {
        var snapshot = SnapshotDecisionScenario(scenario);
        _configuration.DecisionScenarios.RemoveAll(existing => existing.Id == snapshot.Id);
        _configuration.DecisionScenarios.Add(snapshot);
        return this;
    }

    public LlmTestServerConfigurationBuilder AddDataset(
        string id,
        Action<LlmTestServerScenarioDatasetBuilder> configure
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new LlmTestServerScenarioDatasetBuilder(id);
        configure(builder);
        var dataset = builder.Build();

        _configuration.Datasets.RemoveAll(existing =>
            string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase)
        );
        _configuration.Datasets.Add(dataset);

        return this;
    }

    public LlmTestServerConfigurationBuilder WithDefaultEmbeddingVector(params float[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
        {
            throw new ArgumentException("At least one vector value is required.", nameof(values));
        }

        _configuration.DefaultEmbeddingVector.Clear();
        _configuration.DefaultEmbeddingVector.AddRange(values);

        return this;
    }

    public LlmTestServerConfigurationBuilder WithDefaultImageDataUri(string dataUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataUri);
        if (!dataUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Image fixture must be a data URI.", nameof(dataUri));
        }

        _configuration = _configuration with { DefaultImageDataUri = dataUri };
        return this;
    }

    public LlmTestServerConfigurationBuilder WithDefaultAudio(byte[] bytes, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        if (bytes.Length == 0)
        {
            throw new ArgumentException("At least one audio byte is required.", nameof(bytes));
        }

        _configuration = _configuration with
        {
            DefaultAudioBytes = [.. bytes],
            DefaultAudioMediaType = mediaType,
        };

        return this;
    }

    public LlmTestServerConfigurationBuilder WithDefaultTranscriptionText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        _configuration = _configuration with { DefaultTranscriptionText = text };
        return this;
    }

    public LlmTestServerConfigurationBuilder WithDefaultTranscriptionUsage(LlmTestServerTranscriptionUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        usage.Validate();
        _configuration = _configuration with { DefaultTranscriptionUsage = usage };
        return this;
    }

    public LlmTestServerConfigurationBuilder WithoutDefaultTranscriptionUsage()
    {
        _configuration = _configuration with { DefaultTranscriptionUsage = null };
        return this;
    }

    public LlmTestServerConfigurationBuilder WithDefaultTranslationText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        _configuration = _configuration with { DefaultTranslationText = text };
        return this;
    }

    public LlmTestServerConfigurationBuilder WithDefaultVideo(byte[] bytes, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        if (bytes.Length == 0)
        {
            throw new ArgumentException("At least one video byte is required.", nameof(bytes));
        }

        _configuration = _configuration with
        {
            DefaultVideoBytes = [.. bytes],
            DefaultVideoMediaType = mediaType,
        };

        return this;
    }

    public LlmTestServerConfigurationBuilder WithPromptCacheCapacity(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _configuration = _configuration with { MaxPromptCacheEntries = capacity };
        return this;
    }

    public LlmTestServerConfigurationBuilder WithVideoCapacity(int maxJobs, long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxJobs);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        _configuration = _configuration with { MaxVideoJobs = maxJobs, MaxVideoBytes = maxBytes };
        return this;
    }

    public LlmTestServerConfigurationBuilder WithChatResponseCapacity(int maxResponses, long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxResponses);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        _configuration = _configuration with { MaxStoredChatResponses = maxResponses, MaxStoredChatResponseBytes = maxBytes };
        return this;
    }

    public LlmTestServerConfiguration Build()
    {
        return Snapshot(_configuration);
    }

    internal static LlmTestServerConfiguration Snapshot(LlmTestServerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Determinism is { } determinism)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(determinism.Seed);
            if (determinism.Seed.Length > 256) { throw new ArgumentException("Deterministic seeds are limited to 256 characters.", nameof(configuration)); }
        }
        ArgumentOutOfRangeException.ThrowIfNegative(configuration.MaxPromptCacheEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(configuration.MaxConversations);
        ArgumentOutOfRangeException.ThrowIfNegative(configuration.MaxConversationBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(configuration.MaxVideoJobs);
        ArgumentOutOfRangeException.ThrowIfNegative(configuration.MaxVideoBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(configuration.MaxStoredChatResponses);
        ArgumentOutOfRangeException.ThrowIfNegative(configuration.MaxStoredChatResponseBytes);
        ArgumentNullException.ThrowIfNull(configuration.Models);
        ArgumentNullException.ThrowIfNull(configuration.ChatScenarios);
        ArgumentNullException.ThrowIfNull(configuration.DecisionScenarios);
        ArgumentNullException.ThrowIfNull(configuration.Datasets);
        ArgumentNullException.ThrowIfNull(configuration.DefaultEmbeddingVector);
        ArgumentNullException.ThrowIfNull(configuration.DefaultAudioBytes);
        ArgumentNullException.ThrowIfNull(configuration.DefaultVideoBytes);
        configuration.DefaultTranscriptionUsage?.Validate();

        return configuration with
        {
            Models = [.. configuration.Models.Select(SnapshotModel)],
            ChatScenarios = [.. configuration.ChatScenarios.Select(SnapshotScenario)],
            DecisionScenarios = [.. configuration.DecisionScenarios.Select(SnapshotDecisionScenario)],
            Datasets = [.. configuration.Datasets.Select(SnapshotDataset)],
            FaultSimulation = SnapshotFaultSimulation(configuration.FaultSimulation),
            DefaultEmbeddingVector = [.. configuration.DefaultEmbeddingVector],
            DefaultAudioBytes = [.. configuration.DefaultAudioBytes],
            DefaultVideoBytes = [.. configuration.DefaultVideoBytes],
        };
    }

    private static ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionScenario SnapshotDecisionScenario(ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario.ModelId);
        if (scenario.ResolvedModelId is not null) { ArgumentException.ThrowIfNullOrWhiteSpace(scenario.ResolvedModelId); }
        ArgumentNullException.ThrowIfNull(scenario.Answers);
        foreach (var (name, answer) in scenario.Answers)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(answer);
            var values = answer.Probabilities.Values.Concat(new[] { answer.Probability, answer.Confidence }.OfType<double>());
            if (answer.Score is { } score && (!double.IsFinite(score) || score < 0)) { throw new ArgumentException("Decision scores must be finite and nonnegative.", nameof(scenario)); }
            if (!Enum.IsDefined(answer.Kind) || values.Any(value => !double.IsFinite(value) || value < 0 || value > 1))
            {
                throw new ArgumentException("Decision probabilities and scores must be finite values in [0, 1].", nameof(scenario));
            }
            var valid = answer.Kind switch
            {
                ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionKind.Predicate => answer.Probability.HasValue,
                ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionKind.Choice => answer.Choice is not null,
                ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionKind.Score => answer.Score.HasValue,
                ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionKind.Box => answer.Box is not null
                    && (scenario.AllowMalformedResponse || answer.Box.IsValid),
                _ => false,
            };
            if (!valid && !answer.Refused) { throw new ArgumentException("The fixture must provide the value for its decision kind.", nameof(scenario)); }
        }
        return scenario with
        {
            Answers = scenario.Answers.ToDictionary(entry => entry.Key,
            entry => entry.Value with { Probabilities = new(entry.Value.Probabilities) }, StringComparer.Ordinal)
        };
    }

    private static LlmTestServerModel SnapshotModel(LlmTestServerModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(model.Id);
        if (model.ReasoningTokens < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(model),
                "Model reasoning token count must be zero or greater."
            );
        }

        return model with
        {
            ReasoningTokens = model.Kind == LlmTestServerModelKind.Chat ? model.ReasoningTokens : 0,
        };
    }

    internal static LlmTestServerScenario SnapshotScenario(LlmTestServerScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(scenario.Match);
        ArgumentNullException.ThrowIfNull(scenario.Match.Messages);
        ArgumentNullException.ThrowIfNull(scenario.Responses);

        return scenario with
        {
            Conversation = SnapshotConversation(scenario),
            Match = scenario.Match with { Messages = scenario.Match.Messages.Select(SnapshotMessage).ToList() },
            Responses =
            [
                .. scenario.Responses.Select(SnapshotResponse),
            ],
        };
    }

    private static LlmTestServerMessage SnapshotMessage(LlmTestServerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(message.ToolCalls);
        if (message.OutputDisposition is { } disposition) { LlmTestServerOutputDisposition.Validate(disposition); }
        foreach (var call in message.ToolCalls)
        {
            ArgumentNullException.ThrowIfNull(call);
            ArgumentException.ThrowIfNullOrWhiteSpace(call.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(call.Name);
            LlmTestServerJsonSchema.ValidateJson(call.ArgumentsJson);
        }
        return message with { OutputDisposition = LlmTestServerOutputDisposition.ForHistory(message.OutputDisposition), ToolCalls = [.. message.ToolCalls], OutputItems = LlmTestServerResponseItems.Snapshot(message.OutputItems) };
    }

    private static LlmTestServerScenarioResponse SnapshotResponse(LlmTestServerScenarioResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(response.Content);
        ArgumentNullException.ThrowIfNull(response.StreamChunks);
        foreach (var chunk in response.StreamChunks) { ArgumentNullException.ThrowIfNull(chunk); }
        if (response.Error is { } error)
        {
            if (error.StatusCode is < 400 or > 599 || error.RetryAfterSeconds is < 0 or > 86400) { throw new ArgumentOutOfRangeException(nameof(response)); }
            ArgumentException.ThrowIfNullOrWhiteSpace(error.Code);
            ArgumentNullException.ThrowIfNull(error.Message);
        }
        if (response.DelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(response));
        }

        ArgumentNullException.ThrowIfNull(response.ToolCalls);
        foreach (var call in response.ToolCalls)
        {
            ArgumentNullException.ThrowIfNull(call);
            ArgumentException.ThrowIfNullOrWhiteSpace(call.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(call.Name);
            LlmTestServerJsonSchema.ValidateJson(call.ArgumentsJson);
        }
        ArgumentNullException.ThrowIfNull(response.OutputItems);
        if (response.OutputItems.Count > 0 && (response.Content.Length > 0 || response.ToolCalls.Count > 0 || response.StreamChunks.Count > 0 || response.Error is not null))
        { throw new ArgumentException("An ordered output plan cannot also specify legacy output fields or an error.", nameof(response)); }
        return response with { OutputDisposition = LlmTestServerOutputDisposition.Snapshot(response), StreamDelivery = LlmTestServerStreamDelivery.Snapshot(response), StreamChunks = [.. response.StreamChunks], ToolCalls = [.. response.ToolCalls], OutputItems = LlmTestServerResponseItems.Snapshot(response.OutputItems, validateChunks: true) };
    }

    private static LlmTestServerScenarioDataset SnapshotDataset(LlmTestServerScenarioDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(dataset.ChatScenarios);

        return dataset with
        {
            ChatScenarios = [.. dataset.ChatScenarios.Select(SnapshotScenario)],
        };
    }

    private static LlmTestServerFaultSimulation SnapshotFaultSimulation(
        LlmTestServerFaultSimulation faultSimulation
    )
    {
        ArgumentNullException.ThrowIfNull(faultSimulation);
        ArgumentNullException.ThrowIfNull(faultSimulation.ContentFilterTerms);

        return faultSimulation with
        {
            ContentFilterTerms =
            [
                .. faultSimulation
                    .ContentFilterTerms
                    .Where(term => !string.IsNullOrWhiteSpace(term))
                    .Select(term => term.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase),
            ],
        };
    }
}
