using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.Ollama;

public sealed record OllamaChatRequest
{
    [JsonPropertyName("tools")] public List<OllamaTool> Tools { get; init; } = [];
    [JsonPropertyName("format")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement Format { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OllamaChatMessage> Messages { get; init; } = [];

    [JsonPropertyName("stream")]
    public bool? Stream { get; init; }
}

public sealed record OllamaChatMessage
{
    [JsonPropertyName("tool_calls")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<OllamaToolCall>? ToolCalls { get; init; }
    [JsonPropertyName("tool_name")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ToolName { get; init; }

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public JsonElement Content { get; init; }

    [JsonIgnore]
    public string TextContent => OllamaContentReader.ReadTextContent(Content);
}

public sealed record OllamaChatResponse
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("message")]
    public OllamaChatMessageResponse Message { get; init; } = new();

    [JsonPropertyName("done")]
    public bool Done { get; init; }

    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; init; }

    [JsonPropertyName("total_duration")]
    public long TotalDuration { get; init; }

    [JsonPropertyName("load_duration")]
    public long LoadDuration { get; init; }

    [JsonPropertyName("prompt_eval_count")]
    public int PromptEvalCount { get; init; }

    [JsonPropertyName("prompt_eval_duration")]
    public long PromptEvalDuration { get; init; }

    [JsonPropertyName("prompt_eval_cached_count")]
    public int PromptEvalCachedCount { get; init; }

    [JsonPropertyName("eval_count")]
    public int EvalCount { get; init; }

    [JsonPropertyName("eval_duration")]
    public long EvalDuration { get; init; }
}

public sealed record OllamaChatMessageResponse
{
    [JsonPropertyName("tool_calls")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<OllamaToolCall>? ToolCalls { get; init; }
    [JsonPropertyName("tool_name")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ToolName { get; init; }

    [JsonPropertyName("role")]
    public string Role { get; init; } = "assistant";

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;
}

public sealed record OllamaEmbedRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("input")]
    public JsonElement Input { get; init; }
}

public sealed record OllamaEmbedResponse
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("embeddings")]
    public IReadOnlyList<IReadOnlyList<float>> Embeddings { get; init; } = [];

    [JsonPropertyName("total_duration")]
    public long TotalDuration { get; init; }

    [JsonPropertyName("load_duration")]
    public long LoadDuration { get; init; }

    [JsonPropertyName("prompt_eval_count")]
    public int PromptEvalCount { get; init; }
}

public sealed record OllamaErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;
}

internal static class OllamaContentReader
{
    public static string ReadTextContent(JsonElement content)
    {
        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Concat(content.EnumerateArray().Select(ReadTextContent)),
            JsonValueKind.Object when content.TryGetProperty("text", out var text) => ReadTextContent(text),
            JsonValueKind.Object when content.TryGetProperty("content", out var nested) => ReadTextContent(nested),
            JsonValueKind.Undefined or JsonValueKind.Null => string.Empty,
            _ => content.ToString(),
        };
    }
}

public sealed record OllamaTool
{
    [JsonPropertyName("type")] public string Type { get; init; } = "function";
    [JsonPropertyName("function")] public OllamaFunction Function { get; init; } = new();
}
public sealed record OllamaFunction
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("parameters")] public JsonElement Parameters { get; init; } = JsonSerializer.SerializeToElement(new { type = "object" });
}
public sealed record OllamaToolCall
{
    [JsonPropertyName("function")] public OllamaFunctionCall Function { get; init; } = new();
}
public sealed record OllamaFunctionCall
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("arguments")] public JsonElement Arguments { get; init; }
}
