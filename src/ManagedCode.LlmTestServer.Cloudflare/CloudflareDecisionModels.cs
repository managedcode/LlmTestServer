using System.Text.Json.Serialization;
using ManagedCode.LlmTestServer.SystemOne;

namespace ManagedCode.LlmTestServer.Cloudflare;

public sealed record CloudflareDecisionResponse
{
    [JsonPropertyName("result")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SystemOneDecisionResponse? Result { get; init; }
    [JsonPropertyName("success")] public bool Success { get; init; }
    [JsonPropertyName("errors")] public List<CloudflareDecisionError> Errors { get; init; } = [];
    [JsonPropertyName("messages")] public List<string> Messages { get; init; } = [];
}

public sealed record CloudflareDecisionError
{
    [JsonPropertyName("code")] public int Code { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
}
