using ManagedCode.LlmTck.Models;
using ManagedCode.LlmTck.Runtime;
using ManagedCode.LlmTck.Scenarios;

namespace ManagedCode.LlmTck.Configuration;

public sealed class LlmTckConfigurationBuilder
{
    private LlmTckConfiguration _configuration = LlmTckConfiguration.CreateDefault();

    public LlmTckConfigurationBuilder AddModel(
        string id,
        LlmTckModelKind kind,
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
            new LlmTckModel
            {
                Id = id,
                Kind = kind,
                ReasoningTokens = kind == LlmTckModelKind.Chat ? reasoningTokens : 0,
            }
        );

        return this;
    }

    public LlmTckConfigurationBuilder AddReasoningChatModel(string id, int reasoningTokens)
    {
        if (reasoningTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reasoningTokens),
                "Reasoning chat models must declare at least one reasoning token."
            );
        }

        return AddModel(id, LlmTckModelKind.Chat, reasoningTokens);
    }

    public LlmTckConfigurationBuilder AddGpt55(int reasoningTokens = 0)
    {
        return AddModel(LlmTckKnownModelIds.Gpt55, LlmTckModelKind.Chat, reasoningTokens);
    }

    public LlmTckConfigurationBuilder AddGpt55Pro(int reasoningTokens = 0)
    {
        return AddModel(LlmTckKnownModelIds.Gpt55Pro, LlmTckModelKind.Chat, reasoningTokens);
    }

    public LlmTckConfigurationBuilder AddGpt54(int reasoningTokens = 0)
    {
        return AddModel(LlmTckKnownModelIds.Gpt54, LlmTckModelKind.Chat, reasoningTokens);
    }

    public LlmTckConfigurationBuilder AddGpt54Pro(int reasoningTokens = 0)
    {
        return AddModel(LlmTckKnownModelIds.Gpt54Pro, LlmTckModelKind.Chat, reasoningTokens);
    }

    public LlmTckConfigurationBuilder AddGpt54Mini(int reasoningTokens = 0)
    {
        return AddModel(LlmTckKnownModelIds.Gpt54Mini, LlmTckModelKind.Chat, reasoningTokens);
    }

    public LlmTckConfigurationBuilder AddGpt54Nano(int reasoningTokens = 0)
    {
        return AddModel(LlmTckKnownModelIds.Gpt54Nano, LlmTckModelKind.Chat, reasoningTokens);
    }

    public LlmTckConfigurationBuilder AddGpt5(int reasoningTokens = 0)
    {
        return AddModel(LlmTckKnownModelIds.Gpt5, LlmTckModelKind.Chat, reasoningTokens);
    }

    public LlmTckConfigurationBuilder AddGpt5Pro(int reasoningTokens = 0)
    {
        return AddModel(LlmTckKnownModelIds.Gpt5Pro, LlmTckModelKind.Chat, reasoningTokens);
    }

    public LlmTckConfigurationBuilder AddGpt5Mini(int reasoningTokens = 0)
    {
        return AddModel(LlmTckKnownModelIds.Gpt5Mini, LlmTckModelKind.Chat, reasoningTokens);
    }

    public LlmTckConfigurationBuilder AddGpt5Nano(int reasoningTokens = 0)
    {
        return AddModel(LlmTckKnownModelIds.Gpt5Nano, LlmTckModelKind.Chat, reasoningTokens);
    }

    public LlmTckConfigurationBuilder AddGpt41()
    {
        return AddModel(LlmTckKnownModelIds.Gpt41, LlmTckModelKind.Chat);
    }

    public LlmTckConfigurationBuilder AddGpt41Mini()
    {
        return AddModel(LlmTckKnownModelIds.Gpt41Mini, LlmTckModelKind.Chat);
    }

    public LlmTckConfigurationBuilder AddGpt4OMini()
    {
        return AddModel(LlmTckKnownModelIds.Gpt4OMini, LlmTckModelKind.Chat);
    }

    public LlmTckConfigurationBuilder AddTextEmbedding3Small()
    {
        return AddModel(LlmTckKnownModelIds.TextEmbedding3Small, LlmTckModelKind.Embedding);
    }

    public LlmTckConfigurationBuilder AddTextEmbedding3Large()
    {
        return AddModel(LlmTckKnownModelIds.TextEmbedding3Large, LlmTckModelKind.Embedding);
    }

    public LlmTckConfigurationBuilder AddTextEmbeddingAda002()
    {
        return AddModel(LlmTckKnownModelIds.TextEmbeddingAda002, LlmTckModelKind.Embedding);
    }

    public LlmTckConfigurationBuilder AddGptImage2()
    {
        return AddModel(LlmTckKnownModelIds.GptImage2, LlmTckModelKind.Image);
    }

    public LlmTckConfigurationBuilder AddGptImage15()
    {
        return AddModel(LlmTckKnownModelIds.GptImage15, LlmTckModelKind.Image);
    }

    public LlmTckConfigurationBuilder AddGptImage1()
    {
        return AddModel(LlmTckKnownModelIds.GptImage1, LlmTckModelKind.Image);
    }

    public LlmTckConfigurationBuilder AddGptImage1Mini()
    {
        return AddModel(LlmTckKnownModelIds.GptImage1Mini, LlmTckModelKind.Image);
    }

    public LlmTckConfigurationBuilder AddGpt4OMiniTts()
    {
        return AddModel(LlmTckKnownModelIds.Gpt4OMiniTts, LlmTckModelKind.Audio);
    }

    public LlmTckConfigurationBuilder AddTts1()
    {
        return AddModel(LlmTckKnownModelIds.Tts1, LlmTckModelKind.Audio);
    }

    public LlmTckConfigurationBuilder AddTts1Hd()
    {
        return AddModel(LlmTckKnownModelIds.Tts1Hd, LlmTckModelKind.Audio);
    }

    public LlmTckConfigurationBuilder AddSora2()
    {
        return AddModel(LlmTckKnownModelIds.Sora2, LlmTckModelKind.Video);
    }

    public LlmTckConfigurationBuilder AddSora2Pro()
    {
        return AddModel(LlmTckKnownModelIds.Sora2Pro, LlmTckModelKind.Video);
    }

    public LlmTckConfigurationBuilder AddDefaultOpenAiModels()
    {
        return AddGpt41Mini()
            .AddTextEmbedding3Small()
            .AddGptImage1()
            .AddGpt4OMiniTts()
            .AddSora2();
    }

    public LlmTckConfigurationBuilder AddCurrentOpenAiChatModels(int reasoningTokens = 0)
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

    public LlmTckConfigurationBuilder AddOpenAiEmbeddingModels()
    {
        return AddTextEmbedding3Small()
            .AddTextEmbedding3Large()
            .AddTextEmbeddingAda002();
    }

    public LlmTckConfigurationBuilder AddOpenAiImageModels()
    {
        return AddGptImage2()
            .AddGptImage15()
            .AddGptImage1()
            .AddGptImage1Mini();
    }

    public LlmTckConfigurationBuilder AddOpenAiAudioModels()
    {
        return AddGpt4OMiniTts()
            .AddTts1()
            .AddTts1Hd();
    }

    public LlmTckConfigurationBuilder AddOpenAiVideoModels()
    {
        return AddSora2()
            .AddSora2Pro();
    }

    public LlmTckConfigurationBuilder AddKnownOpenAiModels(int reasoningTokens = 0)
    {
        return AddCurrentOpenAiChatModels(reasoningTokens)
            .AddOpenAiEmbeddingModels()
            .AddOpenAiImageModels()
            .AddOpenAiAudioModels()
            .AddOpenAiVideoModels();
    }

    public LlmTckConfigurationBuilder RequireBearerToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        _configuration = _configuration with { RequiredBearerToken = token };
        return this;
    }

    public LlmTckConfigurationBuilder SimulateRateLimitAfter(int allowedRequests)
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

    public LlmTckConfigurationBuilder SimulateContentFilter(params string[] blockedTerms)
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

    public LlmTckConfigurationBuilder AddChatScenario(
        string id,
        Action<LlmTckScenarioBuilder> configure
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new LlmTckScenarioBuilder(id);
        configure(builder);
        var scenario = builder.Build();

        _configuration.ChatScenarios.RemoveAll(existing =>
            string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase)
        );
        _configuration.ChatScenarios.Add(scenario);

        return this;
    }

    public LlmTckConfigurationBuilder AddDecisionModel(string id, ManagedCode.LlmTck.Decisions.LlmTckDecisionProvider provider)
    {
        if (!Enum.IsDefined(provider)) { throw new ArgumentOutOfRangeException(nameof(provider)); }
        AddModel(id, LlmTckModelKind.Decision);
        var index = _configuration.Models.FindIndex(model => model.Id == id);
        _configuration.Models[index] = _configuration.Models[index] with { DecisionProvider = provider };
        return this;
    }

    public LlmTckConfigurationBuilder AddJevLatest()
    {
        return AddDecisionModel(LlmTckKnownModelIds.JevLatest, ManagedCode.LlmTck.Decisions.LlmTckDecisionProvider.TypeSafe);
    }

    public LlmTckConfigurationBuilder AddKevLatest()
    {
        return AddDecisionModel(LlmTckKnownModelIds.KevLatest, ManagedCode.LlmTck.Decisions.LlmTckDecisionProvider.Kev);
    }

    public LlmTckConfigurationBuilder AddClef()
    {
        return AddDecisionModel(LlmTckKnownModelIds.Clef, ManagedCode.LlmTck.Decisions.LlmTckDecisionProvider.Cloudflare);
    }

    public LlmTckConfigurationBuilder AddClefFlash()
    {
        return AddDecisionModel(LlmTckKnownModelIds.ClefFlash, ManagedCode.LlmTck.Decisions.LlmTckDecisionProvider.Cloudflare);
    }

    public LlmTckConfigurationBuilder AddOpenAiDecisionModel(string id)
    {
        return AddDecisionModel(id, ManagedCode.LlmTck.Decisions.LlmTckDecisionProvider.OpenAI);
    }

    public LlmTckConfigurationBuilder AddDecisionScenario(ManagedCode.LlmTck.Decisions.LlmTckDecisionScenario scenario)
    {
        var snapshot = SnapshotDecisionScenario(scenario);
        _configuration.DecisionScenarios.RemoveAll(existing => existing.Id == snapshot.Id);
        _configuration.DecisionScenarios.Add(snapshot);
        return this;
    }

    public LlmTckConfigurationBuilder AddDataset(
        string id,
        Action<LlmTckScenarioDatasetBuilder> configure
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new LlmTckScenarioDatasetBuilder(id);
        configure(builder);
        var dataset = builder.Build();

        _configuration.Datasets.RemoveAll(existing =>
            string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase)
        );
        _configuration.Datasets.Add(dataset);

        return this;
    }

    public LlmTckConfigurationBuilder WithDefaultEmbeddingVector(params float[] values)
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

    public LlmTckConfigurationBuilder WithDefaultImageDataUri(string dataUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataUri);
        if (!dataUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Image fixture must be a data URI.", nameof(dataUri));
        }

        _configuration = _configuration with { DefaultImageDataUri = dataUri };
        return this;
    }

    public LlmTckConfigurationBuilder WithDefaultAudio(byte[] bytes, string mediaType)
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

    public LlmTckConfigurationBuilder WithDefaultTranscriptionText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        _configuration = _configuration with { DefaultTranscriptionText = text };
        return this;
    }

    public LlmTckConfigurationBuilder WithDefaultTranscriptionUsage(LlmTckTranscriptionUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        usage.Validate();
        _configuration = _configuration with { DefaultTranscriptionUsage = usage };
        return this;
    }

    public LlmTckConfigurationBuilder WithoutDefaultTranscriptionUsage()
    {
        _configuration = _configuration with { DefaultTranscriptionUsage = null };
        return this;
    }

    public LlmTckConfigurationBuilder WithDefaultTranslationText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        _configuration = _configuration with { DefaultTranslationText = text };
        return this;
    }

    public LlmTckConfigurationBuilder WithDefaultVideo(byte[] bytes, string mediaType)
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

    public LlmTckConfigurationBuilder WithPromptCacheCapacity(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _configuration = _configuration with { MaxPromptCacheEntries = capacity };
        return this;
    }

    public LlmTckConfigurationBuilder WithVideoCapacity(int maxJobs, long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxJobs);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        _configuration = _configuration with { MaxVideoJobs = maxJobs, MaxVideoBytes = maxBytes };
        return this;
    }

    public LlmTckConfigurationBuilder WithChatResponseCapacity(int maxResponses, long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxResponses);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        _configuration = _configuration with { MaxStoredChatResponses = maxResponses, MaxStoredChatResponseBytes = maxBytes };
        return this;
    }

    public LlmTckConfiguration Build()
    {
        return Snapshot(_configuration);
    }

    internal static LlmTckConfiguration Snapshot(LlmTckConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentOutOfRangeException.ThrowIfNegative(configuration.MaxPromptCacheEntries);
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

    private static ManagedCode.LlmTck.Decisions.LlmTckDecisionScenario SnapshotDecisionScenario(ManagedCode.LlmTck.Decisions.LlmTckDecisionScenario scenario)
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
                ManagedCode.LlmTck.Decisions.LlmTckDecisionKind.Predicate => answer.Probability.HasValue,
                ManagedCode.LlmTck.Decisions.LlmTckDecisionKind.Choice => answer.Choice is not null,
                ManagedCode.LlmTck.Decisions.LlmTckDecisionKind.Score => answer.Score.HasValue,
                ManagedCode.LlmTck.Decisions.LlmTckDecisionKind.Box => answer.Box is not null
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

    private static LlmTckModel SnapshotModel(LlmTckModel model)
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
            ReasoningTokens = model.Kind == LlmTckModelKind.Chat ? model.ReasoningTokens : 0,
        };
    }

    internal static LlmTckScenario SnapshotScenario(LlmTckScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(scenario.Match);
        ArgumentNullException.ThrowIfNull(scenario.Match.Messages);
        ArgumentNullException.ThrowIfNull(scenario.Responses);

        return scenario with
        {
            Match = scenario.Match with { Messages = scenario.Match.Messages.Select(SnapshotMessage).ToList() },
            Responses =
            [
                .. scenario.Responses.Select(SnapshotResponse),
            ],
        };
    }

    private static LlmTckMessage SnapshotMessage(LlmTckMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(message.ToolCalls);
        foreach (var call in message.ToolCalls)
        {
            ArgumentNullException.ThrowIfNull(call);
            ArgumentException.ThrowIfNullOrWhiteSpace(call.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(call.Name);
            LlmTckJsonSchema.ValidateJson(call.ArgumentsJson);
        }
        return message with { ToolCalls = [.. message.ToolCalls] };
    }

    private static LlmTckScenarioResponse SnapshotResponse(LlmTckScenarioResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(response.StreamChunks);
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
            LlmTckJsonSchema.ValidateJson(call.ArgumentsJson);
        }
        return response with { StreamChunks = [.. response.StreamChunks], ToolCalls = [.. response.ToolCalls] };
    }

    private static LlmTckScenarioDataset SnapshotDataset(LlmTckScenarioDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(dataset.ChatScenarios);

        return dataset with
        {
            ChatScenarios = [.. dataset.ChatScenarios.Select(SnapshotScenario)],
        };
    }

    private static LlmTckFaultSimulation SnapshotFaultSimulation(
        LlmTckFaultSimulation faultSimulation
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
