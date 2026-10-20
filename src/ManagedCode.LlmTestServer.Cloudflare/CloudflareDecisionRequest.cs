using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedCode.LlmTestServer.SystemOne;

namespace ManagedCode.LlmTestServer.Cloudflare;

public sealed record CloudflareDecisionRequest
{
    [JsonPropertyName("model")] public required string Model { get; init; }
    [JsonPropertyName("state")] public required JsonElement State { get; init; }
    [JsonPropertyName("questions")] public required Dictionary<string, SystemOneDecisionQuestion> Questions { get; init; }
    [JsonPropertyName("images")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<CloudflareDecisionImage>? Images { get; init; }
}

[JsonConverter(typeof(CloudflareDecisionImageConverter))]
public sealed record CloudflareDecisionImage
{
    public string? DataUrl { get; init; }
    public CloudflareBase64Image? Embedded { get; init; }
}

public sealed record CloudflareBase64Image
{
    [JsonPropertyName("content_type")] public required string ContentType { get; init; }
    [JsonPropertyName("base64")] public required string Base64 { get; init; }
}
