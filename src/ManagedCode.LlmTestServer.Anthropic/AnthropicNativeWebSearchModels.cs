using System.Text.Json.Serialization;
using ManagedCode.LlmTestServer.Models;

namespace ManagedCode.LlmTestServer.Anthropic;

public static class AnthropicNativeWebSearchContract
{
    public const string ToolType = "web_search_20250305";
    public const string ToolName = "web_search";
    public const string ModelId = LlmTestServerKnownModelIds.ClaudeSonnet55;
}

public sealed record AnthropicWebSearchLocation
{
    [JsonPropertyName("type")] public string Type { get; init; } = "approximate";
    [JsonPropertyName("city")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? City { get; init; }
    [JsonPropertyName("region")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Region { get; init; }
    [JsonPropertyName("country")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Country { get; init; }
    [JsonPropertyName("timezone")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Timezone { get; init; }
}

public sealed record AnthropicWebSearchResult
{
    [JsonPropertyName("type")] public string Type { get; init; } = "web_search_result";
    [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
    [JsonPropertyName("encrypted_content")] public string EncryptedContent { get; init; } = string.Empty;
    [JsonPropertyName("page_age")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PageAge { get; init; }
}

public sealed record AnthropicWebSearchToolResultError
{
    [JsonPropertyName("type")] public string Type { get; init; } = "web_search_tool_result_error";
    [JsonPropertyName("error_code")] public string ErrorCode { get; init; } = string.Empty;
}

public sealed record AnthropicWebSearchCitation
{
    [JsonPropertyName("type")] public string Type { get; init; } = "web_search_result_location";
    [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;
    [JsonPropertyName("encrypted_index")] public string EncryptedIndex { get; init; } = string.Empty;
    [JsonPropertyName("cited_text")] public string CitedText { get; init; } = string.Empty;
    [JsonPropertyName("title")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Title { get; init; }
}

public sealed record AnthropicDirectCaller
{
    [JsonPropertyName("type")] public string Type { get; init; } = "direct";
}

public sealed record AnthropicServerToolUsage
{
    [JsonPropertyName("web_fetch_requests")] public int WebFetchRequests { get; init; }

    [JsonPropertyName("web_search_requests")] public int WebSearchRequests { get; init; }
}
