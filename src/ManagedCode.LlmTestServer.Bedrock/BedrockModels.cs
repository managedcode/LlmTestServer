using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.Bedrock;

public sealed record BedrockConverseRequest
{
    [JsonPropertyName("toolConfig")] public BedrockToolConfig? ToolConfig { get; init; }
    [JsonPropertyName("outputConfig")] public BedrockOutputConfig? OutputConfig { get; init; }

    [JsonPropertyName("messages")]
    public List<BedrockMessage> Messages { get; init; } = [];

    [JsonPropertyName("system")]
    public List<BedrockContentBlock> System { get; init; } = [];
}

public sealed record BedrockMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public List<BedrockContentBlock> Content { get; init; } = [];
}

public sealed record BedrockContentBlock
{
    [JsonPropertyName("toolUse")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public BedrockToolUse? ToolUse { get; init; }
    [JsonPropertyName("toolResult")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public BedrockToolResult? ToolResult { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonPropertyName("image")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Image { get; init; }

    [JsonPropertyName("document")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Document { get; init; }

    [JsonPropertyName("cachePoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? CachePoint { get; init; }

    [JsonPropertyName("cache_control")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? CacheControl { get; init; }
}

public sealed record BedrockConverseResponse
{
    [JsonPropertyName("output")]
    public BedrockConverseOutput Output { get; init; } = new();

    [JsonPropertyName("stopReason")]
    public string StopReason { get; init; } = "end_turn";

    [JsonPropertyName("usage")]
    public BedrockUsage Usage { get; init; } = new();

    [JsonPropertyName("metrics")]
    public BedrockMetrics Metrics { get; init; } = new();
}

public sealed record BedrockConverseOutput
{
    [JsonPropertyName("message")]
    public BedrockMessage Message { get; init; } = new();
}

public sealed record BedrockUsage
{
    [JsonPropertyName("inputTokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("outputTokens")]
    public int OutputTokens { get; init; }

    [JsonPropertyName("totalTokens")]
    public int TotalTokens { get; init; }

    [JsonPropertyName("cacheReadInputTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int CacheReadInputTokens { get; init; }

    [JsonPropertyName("cacheWriteInputTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int CacheWriteInputTokens { get; init; }
}

public sealed record BedrockMetrics
{
    [JsonPropertyName("latencyMs")]
    public int LatencyMs { get; init; }
}

public sealed record BedrockTitanTextResponse
{
    [JsonPropertyName("inputTextTokenCount")]
    public int InputTextTokenCount { get; init; }

    [JsonPropertyName("results")]
    public IReadOnlyList<BedrockTitanTextResult> Results { get; init; } = [];
}

public sealed record BedrockTitanTextResult
{
    [JsonPropertyName("tokenCount")]
    public int TokenCount { get; init; }

    [JsonPropertyName("outputText")]
    public string OutputText { get; init; } = string.Empty;

    [JsonPropertyName("completionReason")]
    public string CompletionReason { get; init; } = "FINISHED";
}

public sealed record BedrockTitanEmbeddingResponse
{
    [JsonPropertyName("embedding")]
    public IReadOnlyList<float> Embedding { get; init; } = [];

    [JsonPropertyName("inputTextTokenCount")]
    public int InputTextTokenCount { get; init; }

    [JsonPropertyName("embeddingsByType")]
    public IReadOnlyDictionary<string, IReadOnlyList<float>> EmbeddingsByType { get; init; } =
        new Dictionary<string, IReadOnlyList<float>>(StringComparer.Ordinal);
}

public sealed record BedrockImageResponse
{
    [JsonPropertyName("images")]
    public IReadOnlyList<string> Images { get; init; } = [];

    [JsonPropertyName("finish_reasons")]
    public IReadOnlyList<string?> FinishReasons { get; init; } = [];
}

public sealed record BedrockErrorResponse
{
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed record BedrockToolConfig
{
    [JsonPropertyName("tools")] public List<BedrockTool> Tools { get; init; } = [];
    [JsonPropertyName("toolChoice")] public BedrockToolChoice? ToolChoice { get; init; }
}
public sealed record BedrockToolChoice
{
    [JsonPropertyName("any")] public JsonElement? Any { get; init; }
    [JsonPropertyName("tool")] public BedrockNamedTool? Tool { get; init; }
}
public sealed record BedrockNamedTool
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
}
public sealed record BedrockTool
{
    [JsonPropertyName("toolSpec")] public BedrockToolSpec ToolSpec { get; init; } = new();
}
public sealed record BedrockToolSpec
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("inputSchema")] public BedrockInputSchema InputSchema { get; init; } = new();
}
public sealed record BedrockInputSchema
{
    [JsonPropertyName("json")] public JsonElement Json { get; init; } = JsonSerializer.SerializeToElement(new { type = "object" });
}
public sealed record BedrockToolUse
{
    [JsonPropertyName("toolUseId")] public string ToolUseId { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("input")] public JsonElement Input { get; init; }
}
public sealed record BedrockToolResult
{
    [JsonPropertyName("toolUseId")] public string ToolUseId { get; init; } = string.Empty;
    [JsonPropertyName("content")] public List<JsonElement> Content { get; init; } = [];
}
public sealed record BedrockOutputConfig
{
    [JsonPropertyName("textFormat")] public BedrockOutputFormat? TextFormat { get; init; }
}
public sealed record BedrockOutputFormat
{
    [JsonPropertyName("type")] public string Type { get; init; } = "json_schema";
    [JsonPropertyName("structure")] public BedrockOutputStructure? Structure { get; init; }
}
public sealed record BedrockOutputStructure
{
    [JsonPropertyName("jsonSchema")] public BedrockJsonSchema? JsonSchema { get; init; }
}
public sealed record BedrockJsonSchema
{
    [JsonPropertyName("schema")] public string Schema { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string? Name { get; init; }
}
