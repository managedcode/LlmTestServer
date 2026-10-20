using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.OpenAI;

public sealed record OpenAiWebSearchLocation
{
    [JsonPropertyName("type")] public string Type { get; init; } = "approximate";
    [JsonPropertyName("city")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? City { get; init; }
    [JsonPropertyName("region")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Region { get; init; }
    [JsonPropertyName("country")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Country { get; init; }
    [JsonPropertyName("timezone")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Timezone { get; init; }
}

public sealed record OpenAiWebSearchFilters
{
    [JsonPropertyName("allowed_domains")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? AllowedDomains { get; init; }
}

public sealed record OpenAiResponseWebSearchCall : OpenAiResponseOutputItem
{
    [JsonIgnore] public override string Type => "web_search_call";
    [JsonPropertyName("action")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OpenAiWebSearchAction? Action { get; init; }
}

public sealed record OpenAiWebSearchAction
{
    [JsonPropertyName("type")] public string Type { get; init; } = "search";
    [JsonPropertyName("query")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Query { get; init; }
    [JsonPropertyName("queries")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Queries { get; init; }
    [JsonPropertyName("url")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Url { get; init; }
    [JsonPropertyName("pattern")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Pattern { get; init; }
    [JsonPropertyName("sources")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<OpenAiWebSearchSource>? Sources { get; init; }
}

public sealed record OpenAiWebSearchSource
{
    [JsonPropertyName("type")] public string Type { get; init; } = "url";
    [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;
}

public sealed record OpenAiUrlCitation
{
    [JsonPropertyName("type")] public string Type { get; init; } = "url_citation";
    [JsonPropertyName("start_index")] public int StartIndex { get; init; }
    [JsonPropertyName("end_index")] public int EndIndex { get; init; }
    [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
}
