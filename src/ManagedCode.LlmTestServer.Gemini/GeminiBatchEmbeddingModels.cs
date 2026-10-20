using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.Gemini;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GeminiBatchEmbedContentsRequest
{
    [JsonPropertyName("requests")]
    public List<GeminiBatchEmbedContentRequest> Requests { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GeminiBatchEmbedContentRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public GeminiContent Content { get; init; } = new();

    [JsonPropertyName("outputDimensionality")]
    public int? OutputDimensionality { get; init; }
}

public sealed record GeminiBatchEmbedContentsResponse
{
    [JsonPropertyName("embeddings")]
    public List<GeminiContentEmbedding> Embeddings { get; init; } = [];

    [JsonPropertyName("usageMetadata")]
    public GeminiEmbeddingUsageMetadata UsageMetadata { get; init; } = new();
}

public sealed record GeminiEmbeddingUsageMetadata
{
    [JsonPropertyName("promptTokenCount")]
    public int PromptTokenCount { get; init; }
}
