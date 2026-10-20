using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Client;

public sealed class LlmTestServerClientConfigurationBuilder
{
    private readonly LlmTestServerConfigurationBuilder _builder = new();

    public LlmTestServerClientConfigurationBuilder RequireBearerToken(string token)
    {
        _builder.RequireBearerToken(token);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder SimulateRateLimitAfter(int allowedRequests)
    {
        _builder.SimulateRateLimitAfter(allowedRequests);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder SimulateContentFilter(params string[] blockedTerms)
    {
        _builder.SimulateContentFilter(blockedTerms);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseModel(
        string id,
        LlmTestServerModelKind kind,
        int reasoningTokens = 0
    )
    {
        _builder.AddModel(id, kind, reasoningTokens);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseReasoningChatModel(
        string id,
        int reasoningTokens
    )
    {
        _builder.AddReasoningChatModel(id, reasoningTokens);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseGpt55(int reasoningTokens = 0)
    {
        return UseModel(LlmTestServerKnownModelIds.Gpt55, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerClientConfigurationBuilder UseGpt55Pro(int reasoningTokens = 0)
    {
        return UseModel(LlmTestServerKnownModelIds.Gpt55Pro, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerClientConfigurationBuilder UseGpt54(int reasoningTokens = 0)
    {
        return UseModel(LlmTestServerKnownModelIds.Gpt54, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerClientConfigurationBuilder UseGpt54Pro(int reasoningTokens = 0)
    {
        return UseModel(LlmTestServerKnownModelIds.Gpt54Pro, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerClientConfigurationBuilder UseGpt54Mini(int reasoningTokens = 0)
    {
        return UseModel(LlmTestServerKnownModelIds.Gpt54Mini, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerClientConfigurationBuilder UseGpt54Nano(int reasoningTokens = 0)
    {
        return UseModel(LlmTestServerKnownModelIds.Gpt54Nano, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerClientConfigurationBuilder UseGpt5(int reasoningTokens = 0)
    {
        return UseModel(LlmTestServerKnownModelIds.Gpt5, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerClientConfigurationBuilder UseGpt5Pro(int reasoningTokens = 0)
    {
        return UseModel(LlmTestServerKnownModelIds.Gpt5Pro, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerClientConfigurationBuilder UseGpt5Mini(int reasoningTokens = 0)
    {
        return UseModel(LlmTestServerKnownModelIds.Gpt5Mini, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerClientConfigurationBuilder UseGpt5Nano(int reasoningTokens = 0)
    {
        return UseModel(LlmTestServerKnownModelIds.Gpt5Nano, LlmTestServerModelKind.Chat, reasoningTokens);
    }

    public LlmTestServerClientConfigurationBuilder UseGpt41()
    {
        return UseChatModel(LlmTestServerKnownModelIds.Gpt41);
    }

    public LlmTestServerClientConfigurationBuilder UseGpt41Mini()
    {
        return UseChatModel(LlmTestServerKnownModelIds.Gpt41Mini);
    }

    public LlmTestServerClientConfigurationBuilder UseGpt4OMini()
    {
        return UseChatModel(LlmTestServerKnownModelIds.Gpt4OMini);
    }

    public LlmTestServerClientConfigurationBuilder UseTextEmbedding3Small()
    {
        return UseEmbeddingModel(LlmTestServerKnownModelIds.TextEmbedding3Small);
    }

    public LlmTestServerClientConfigurationBuilder UseTextEmbedding3Large()
    {
        return UseEmbeddingModel(LlmTestServerKnownModelIds.TextEmbedding3Large);
    }

    public LlmTestServerClientConfigurationBuilder UseTextEmbeddingAda002()
    {
        return UseEmbeddingModel(LlmTestServerKnownModelIds.TextEmbeddingAda002);
    }

    public LlmTestServerClientConfigurationBuilder UseGptImage2()
    {
        return UseImageModel(LlmTestServerKnownModelIds.GptImage2);
    }

    public LlmTestServerClientConfigurationBuilder UseGptImage15()
    {
        return UseImageModel(LlmTestServerKnownModelIds.GptImage15);
    }

    public LlmTestServerClientConfigurationBuilder UseGptImage1()
    {
        return UseImageModel(LlmTestServerKnownModelIds.GptImage1);
    }

    public LlmTestServerClientConfigurationBuilder UseGptImage1Mini()
    {
        return UseImageModel(LlmTestServerKnownModelIds.GptImage1Mini);
    }

    public LlmTestServerClientConfigurationBuilder UseGpt4OMiniTts()
    {
        return UseAudioModel(LlmTestServerKnownModelIds.Gpt4OMiniTts);
    }

    public LlmTestServerClientConfigurationBuilder UseTts1()
    {
        return UseAudioModel(LlmTestServerKnownModelIds.Tts1);
    }

    public LlmTestServerClientConfigurationBuilder UseTts1Hd()
    {
        return UseAudioModel(LlmTestServerKnownModelIds.Tts1Hd);
    }

    public LlmTestServerClientConfigurationBuilder UseSora2()
    {
        return UseVideoModel(LlmTestServerKnownModelIds.Sora2);
    }

    public LlmTestServerClientConfigurationBuilder UseSora2Pro()
    {
        return UseVideoModel(LlmTestServerKnownModelIds.Sora2Pro);
    }

    public LlmTestServerClientConfigurationBuilder UseDefaultOpenAiModels()
    {
        _builder.AddDefaultOpenAiModels();
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseCurrentOpenAiChatModels(int reasoningTokens = 0)
    {
        _builder.AddCurrentOpenAiChatModels(reasoningTokens);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseOpenAiEmbeddingModels()
    {
        _builder.AddOpenAiEmbeddingModels();
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseOpenAiImageModels()
    {
        _builder.AddOpenAiImageModels();
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseOpenAiAudioModels()
    {
        _builder.AddOpenAiAudioModels();
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseOpenAiVideoModels()
    {
        _builder.AddOpenAiVideoModels();
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseKnownOpenAiModels(int reasoningTokens = 0)
    {
        _builder.AddKnownOpenAiModels(reasoningTokens);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseChatModel(string id)
    {
        return UseModel(id, LlmTestServerModelKind.Chat);
    }

    public LlmTestServerClientConfigurationBuilder UseEmbeddingModel(string id)
    {
        return UseModel(id, LlmTestServerModelKind.Embedding);
    }

    public LlmTestServerClientConfigurationBuilder UseImageModel(string id)
    {
        return UseModel(id, LlmTestServerModelKind.Image);
    }

    public LlmTestServerClientConfigurationBuilder UseAudioModel(string id)
    {
        return UseModel(id, LlmTestServerModelKind.Audio);
    }

    public LlmTestServerClientConfigurationBuilder UseVideoModel(string id)
    {
        return UseModel(id, LlmTestServerModelKind.Video);
    }

    public LlmTestServerClientConfigurationBuilder UseChatScenario(
        string id,
        Action<LlmTestServerScenarioBuilder> configure
    )
    {
        _builder.AddChatScenario(id, configure);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseDecisionModel(string id, ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionProvider provider)
    {
        _builder.AddDecisionModel(id, provider);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseDecisionScenario(ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionScenario scenario)
    {
        _builder.AddDecisionScenario(scenario);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseDataset(
        string id,
        Action<LlmTestServerScenarioDatasetBuilder> configure
    )
    {
        _builder.AddDataset(id, configure);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseEmbeddingVector(params float[] values)
    {
        _builder.WithDefaultEmbeddingVector(values);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseImageDataUri(string dataUri)
    {
        _builder.WithDefaultImageDataUri(dataUri);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseAudio(byte[] bytes, string mediaType)
    {
        _builder.WithDefaultAudio(bytes, mediaType);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseVideo(byte[] bytes, string mediaType)
    {
        _builder.WithDefaultVideo(bytes, mediaType);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseTranscriptionText(string text)
    {
        _builder.WithDefaultTranscriptionText(text);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseTranscriptionUsage(LlmTestServerTranscriptionUsage usage)
    {
        _builder.WithDefaultTranscriptionUsage(usage);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder WithoutTranscriptionUsage()
    {
        _builder.WithoutDefaultTranscriptionUsage();
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseTranslationText(string text)
    {
        _builder.WithDefaultTranslationText(text);
        return this;
    }

    public LlmTestServerClientConfigurationBuilder UseChatResponseCapacity(int maxResponses, long maxBytes)
    {
        _builder.WithChatResponseCapacity(maxResponses, maxBytes);
        return this;
    }

    public LlmTestServerConfiguration Build()
    {
        return _builder.Build();
    }
}
