using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.OpenAI;

public sealed record OpenAiModelListResponse
{
    [JsonPropertyName("object")] public string ObjectType { get; init; } = "list";
    [JsonPropertyName("data")] public IEnumerable<OpenAiModelDescriptor> Data { get; init; } = [];
}

public sealed record OpenAiModelDescriptor
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("object")] public string ObjectType { get; init; } = "model";
    [JsonPropertyName("owned_by")] public string OwnedBy { get; init; } = string.Empty;
    [JsonPropertyName("metadata")] public OpenAiModelDescriptorMetadata Metadata { get; init; } = new();
}

public sealed record OpenAiModelDescriptorMetadata
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
}
