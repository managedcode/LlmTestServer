using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.Anthropic;

public sealed record AnthropicMessagesRequest
{
    [JsonPropertyName("tools")] public List<AnthropicTool> Tools { get; init; } = [];
    [JsonPropertyName("tool_choice")] public AnthropicToolChoice? ToolChoice { get; init; }
    [JsonPropertyName("output_config")] public AnthropicOutputConfig? OutputConfig { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    [JsonPropertyName("messages")]
    public List<AnthropicInputMessage> Messages { get; init; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    [JsonPropertyName("system")]
    public JsonElement System { get; init; }

    [JsonPropertyName("cache_control")]
    public JsonElement? CacheControl { get; init; }
}

public sealed record AnthropicInputMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public JsonElement Content { get; init; }

    [JsonIgnore]
    public string TextContent => AnthropicContentReader.ReadTextContent(Content);
}

public sealed record AnthropicMessageResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = "message";

    [JsonPropertyName("role")]
    public string Role { get; init; } = "assistant";

    [JsonPropertyName("content")]
    public List<AnthropicContentBlock> Content { get; init; } = [];

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; init; } = "end_turn";

    [JsonPropertyName("stop_details")]
    public AnthropicRefusalStopDetails? StopDetails { get; init; }

    [JsonPropertyName("stop_sequence")]
    public string? StopSequence { get; init; }

    [JsonPropertyName("usage")]
    public AnthropicUsage Usage { get; init; } = new();
}

public sealed record AnthropicContentBlock
{
    [JsonPropertyName("id")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Id { get; init; }
    [JsonPropertyName("name")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; init; }
    [JsonPropertyName("input")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JsonElement? Input { get; init; }

    [JsonPropertyName("caller")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnthropicDirectCaller? Caller { get; init; }
    [JsonPropertyName("tool_use_id")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ToolUseId { get; init; }
    [JsonPropertyName("content")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JsonElement? Content { get; init; }
    [JsonPropertyName("citations")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<AnthropicWebSearchCitation>? Citations { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; } = string.Empty;
}

public sealed record AnthropicUsage
{
    [JsonPropertyName("server_tool_use")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicServerToolUsage? ServerToolUse { get; init; }

    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }

    [JsonPropertyName("cache_creation_input_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CacheCreationInputTokens { get; init; }

    [JsonPropertyName("cache_read_input_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CacheReadInputTokens { get; init; }
}

public sealed record AnthropicErrorResponse
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "error";

    [JsonPropertyName("error")]
    public AnthropicError Error { get; init; } = new();

    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = string.Empty;
}

public sealed record AnthropicError
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "invalid_request_error";

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

internal static class AnthropicContentReader
{
    public static string ReadTextContent(JsonElement content)
    {
        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Concat(content.EnumerateArray().Select(ReadContentBlock)),
            JsonValueKind.Object => ReadContentBlock(content),
            JsonValueKind.Undefined or JsonValueKind.Null => string.Empty,
            _ => content.ToString(),
        };
    }

    public static bool HasCacheControl(JsonElement content)
    {
        return content.ValueKind switch
        {
            JsonValueKind.Object => content.TryGetProperty("cache_control", out _)
                || content.EnumerateObject().Any(property => HasCacheControl(property.Value)),
            JsonValueKind.Array => content.EnumerateArray().Any(HasCacheControl),
            _ => false,
        };
    }

    private static string ReadContentBlock(JsonElement block)
    {
        if (block.ValueKind != JsonValueKind.Object)
        {
            return ReadTextContent(block);
        }

        if (block.TryGetProperty("type", out var type)
            && string.Equals(type.GetString(), "text", StringComparison.Ordinal)
            && block.TryGetProperty("text", out var text))
        {
            return ReadTextContent(text);
        }

        return block.TryGetProperty("content", out var nested)
            ? ReadTextContent(nested)
            : block.ToString();
    }
}

public sealed record AnthropicTool
{
    private static readonly JsonElement _defaultInputSchema = JsonSerializer.SerializeToElement(new { type = "object" });

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; init; }
    [JsonPropertyName("input_schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement InputSchema
    {
        get => field.ValueKind == JsonValueKind.Undefined && Type is null or "custom" ? _defaultInputSchema : field;
        init;
    }
    [JsonPropertyName("type")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Type { get; init; }
    [JsonPropertyName("max_uses")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? MaxUses { get; init; }
    [JsonPropertyName("allowed_domains")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? AllowedDomains { get; init; }
    [JsonPropertyName("blocked_domains")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? BlockedDomains { get; init; }
    [JsonPropertyName("user_location")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnthropicWebSearchLocation? UserLocation { get; init; }
}
public sealed record AnthropicToolChoice
{
    [JsonPropertyName("disable_parallel_tool_use")] public bool DisableParallelToolUse { get; init; }
    [JsonPropertyName("type")] public string Type { get; init; } = "auto";
    [JsonPropertyName("name")] public string? Name { get; init; }
}
public sealed record AnthropicOutputConfig
{
    [JsonPropertyName("format")] public AnthropicOutputFormat? Format { get; init; }
}
public sealed record AnthropicOutputFormat
{
    [JsonPropertyName("type")] public string Type { get; init; } = "json_schema";
    [JsonPropertyName("schema")] public JsonElement Schema { get; init; }
}

public sealed record AnthropicRefusalStopDetails
{
    [JsonPropertyName("type")] public string Type { get; init; } = "refusal";
    [JsonPropertyName("category")] public string? Category { get; init; }
    [JsonPropertyName("explanation")] public string? Explanation { get; init; }
}
