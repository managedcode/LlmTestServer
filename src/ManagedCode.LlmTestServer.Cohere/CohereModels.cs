using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.Cohere;

public sealed record CohereChatRequest
{
    [JsonPropertyName("tools")] public List<CohereTool> Tools { get; init; } = [];
    [JsonPropertyName("tool_choice")] public string? ToolChoice { get; init; }
    [JsonPropertyName("response_format")] public CohereResponseFormat? ResponseFormat { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<CohereChatMessage> Messages { get; init; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }
}

public sealed record CohereChatMessage
{
    [JsonPropertyName("tool_call_id")] public string? ToolCallId { get; init; }

    [JsonPropertyName("tool_calls")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<CohereToolCall>? ToolCalls { get; init; }

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public JsonElement Content { get; init; }

    [JsonIgnore]
    public string TextContent => CohereContentReader.ReadTextContent(Content);
}

public sealed record CohereChatResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("finish_reason")]
    public string FinishReason { get; init; } = "COMPLETE";

    [JsonPropertyName("message")]
    public CohereAssistantMessage Message { get; init; } = new();

    [JsonPropertyName("usage")]
    public CohereUsage Usage { get; init; } = new();
}

public sealed record CohereAssistantMessage
{
    [JsonPropertyName("tool_calls")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<CohereToolCall>? ToolCalls { get; init; }

    [JsonPropertyName("role")]
    public string Role { get; init; } = "assistant";

    [JsonPropertyName("content")]
    public List<CohereContentBlock> Content { get; init; } = [];
}

public sealed record CohereContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

public sealed record CohereUsage
{
    [JsonPropertyName("billed_units")]
    public CohereTokenUsage BilledUnits { get; init; } = new();

    [JsonPropertyName("tokens")]
    public CohereTokenUsage Tokens { get; init; } = new();
}

public sealed record CohereTokenUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }
}

public sealed record CohereEmbedRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("input_type")]
    public string InputType { get; init; } = string.Empty;

    [JsonPropertyName("texts")]
    public List<string>? Texts { get; init; }

    [JsonPropertyName("inputs")]
    public JsonElement Inputs { get; init; }

    [JsonPropertyName("embedding_types")]
    public List<string> EmbeddingTypes { get; init; } = [];
}

public sealed record CohereEmbedResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("embeddings")]
    public CohereEmbeddings Embeddings { get; init; } = new();
}

public sealed record CohereEmbeddings
{
    [JsonPropertyName("float")]
    public IReadOnlyList<IReadOnlyList<float>> FloatValues { get; init; } = [];
}

public sealed record CohereErrorResponse
{
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

internal static class CohereContentReader
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

        if (block.TryGetProperty("content", out var content))
        {
            return ReadTextContent(content);
        }

        return block.ToString();
    }
}

public sealed record CohereTool
{
    [JsonPropertyName("type")] public string Type { get; init; } = "function";
    [JsonPropertyName("function")] public CohereFunction Function { get; init; } = new();
}
public sealed record CohereFunction
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("parameters")] public JsonElement Parameters { get; init; } = JsonSerializer.SerializeToElement(new { type = "object" });
}
public sealed record CohereToolCall
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; init; } = "function";
    [JsonPropertyName("function")] public CohereFunctionCall Function { get; init; } = new();
}
public sealed record CohereFunctionCall
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("arguments")] public string Arguments { get; init; } = "{}";
}
public sealed record CohereResponseFormat
{
    [JsonPropertyName("type")] public string Type { get; init; } = "text";
    [JsonPropertyName("schema")] public JsonElement? Schema { get; init; }
}
