using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.OpenAI;

public sealed record OpenAiResponseRequest
{
    [JsonPropertyName("include")]
    public List<string> Include { get; init; } = [];

    [JsonPropertyName("parallel_tool_calls")]
    public bool ParallelToolCalls { get; init; } = true;

    [JsonPropertyName("tools")] public List<OpenAiResponseTool> Tools { get; init; } = [];
    [JsonPropertyName("tool_choice")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement ToolChoice { get; init; }
    [JsonPropertyName("text")] public OpenAiResponseTextOptions? Text { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("input")]
    public JsonElement Input { get; init; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    [JsonPropertyName("store")]
    public bool? Store { get; init; }

    [JsonPropertyName("previous_response_id")]
    public string? PreviousResponseId { get; init; }

    [JsonPropertyName("prompt_cache_key")]
    public string? PromptCacheKey { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }
}

public sealed record OpenAiResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "response";

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("output")]
    public List<OpenAiResponseOutputItem> Output { get; init; } = [];

    [JsonPropertyName("usage")]
    public OpenAiResponseUsage Usage { get; init; } = new();

    [JsonPropertyName("status")]
    public string Status { get; init; } = "completed";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OpenAiResponseOutputMessage), "message")]
[JsonDerivedType(typeof(OpenAiResponseFunctionCall), "function_call")]
[JsonDerivedType(typeof(OpenAiResponseWebSearchCall), "web_search_call")]
[JsonDerivedType(typeof(OpenAiResponseFileSearchCall), "file_search_call")]
[JsonDerivedType(typeof(OpenAiResponseCodeInterpreterCall), "code_interpreter_call")]
public abstract record OpenAiResponseOutputItem
{
    [JsonIgnore] public abstract string Type { get; }
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = "completed";
}

public sealed record OpenAiResponseOutputMessage : OpenAiResponseOutputItem
{
    [JsonIgnore] public override string Type => "message";
    [JsonPropertyName("role")] public string Role { get; init; } = "assistant";
    [JsonPropertyName("content")] public List<OpenAiResponseOutputText> Content { get; init; } = [];
}

public sealed record OpenAiResponseFunctionCall : OpenAiResponseOutputItem
{
    [JsonIgnore] public override string Type => "function_call";
    [JsonPropertyName("call_id")] public string CallId { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("arguments")] public string Arguments { get; init; } = "{}";
}

public sealed record OpenAiResponseOutputText
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "output_text";

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("annotations")]
    public List<object> Annotations { get; init; } = [];
}

public sealed record OpenAiResponseUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }

    [JsonPropertyName("input_tokens_details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAiInputTokensDetails? InputTokensDetails { get; init; }

    [JsonPropertyName("output_tokens_details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAiOutputTokensDetails? OutputTokensDetails { get; init; }
}

public sealed record OpenAiInputTokensDetails
{
    [JsonPropertyName("cached_tokens")]
    public int CachedTokens { get; init; }

    [JsonPropertyName("cache_write_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CacheWriteTokens { get; init; }
}

public sealed record OpenAiOutputTokensDetails
{
    [JsonPropertyName("reasoning_tokens")]
    public int ReasoningTokens { get; init; }
}

public sealed record OpenAiResponseTool
{
    [JsonPropertyName("search_context_size")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SearchContextSize { get; init; }
    [JsonPropertyName("user_location")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OpenAiWebSearchLocation? UserLocation { get; init; }
    [JsonPropertyName("filters")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JsonElement? Filters { get; init; }
    [JsonIgnore]
    public OpenAiWebSearchFilters? WebSearchFilters => Type == "web_search" && Filters is { ValueKind: JsonValueKind.Object } filters
        ? filters.Deserialize<OpenAiWebSearchFilters>() : null;
    [JsonPropertyName("vector_store_ids")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? VectorStoreIds { get; init; }
    [JsonPropertyName("max_num_results")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? MaxNumResults { get; init; }
    [JsonPropertyName("ranking_options")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OpenAiFileSearchRankingOptions? RankingOptions { get; init; }
    [JsonPropertyName("container")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement Container { get; init; }
    [JsonPropertyName("type")] public string Type { get; init; } = "function";
    [JsonPropertyName("name")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; init; }
    [JsonPropertyName("description")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; init; }
    [JsonPropertyName("parameters")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement Parameters { get; init; }
}
public sealed record OpenAiResponseTextOptions
{
    [JsonPropertyName("format")] public OpenAiResponseTextFormat? Format { get; init; }
}
public sealed record OpenAiResponseTextFormat
{
    [JsonPropertyName("type")] public string Type { get; init; } = "text";
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("schema")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement Schema { get; init; }
    [JsonPropertyName("strict")] public bool? Strict { get; init; }
}
