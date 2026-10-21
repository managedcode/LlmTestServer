using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.OpenAI;

/// <summary>Azure Responses billable tool usage, distinct from output tool calls.</summary>
public sealed record OpenAiResponseToolUsage
{
    [JsonPropertyName("web_search")]
    public OpenAiResponseWebSearchUsage WebSearch { get; init; } = new();
}

public sealed record OpenAiResponseWebSearchUsage
{
    [JsonPropertyName("num_requests")]
    public int NumberOfRequests { get; init; }
}
