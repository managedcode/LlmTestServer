using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.OpenAI;

public sealed record OpenAiTool
{
    [JsonPropertyName("type")] public string Type { get; init; } = "function";
    [JsonPropertyName("function")] public OpenAiFunction Function { get; init; } = new();
}
public sealed record OpenAiFunction
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("parameters")] public JsonElement Parameters { get; init; } = JsonSerializer.SerializeToElement(new { type = "object" });
    [JsonPropertyName("strict")] public bool? Strict { get; init; }
}
public sealed record OpenAiToolCall
{
    [JsonPropertyName("index")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Index { get; init; }
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; init; } = "function";
    [JsonPropertyName("function")] public OpenAiFunctionCall Function { get; init; } = new();
}
public sealed record OpenAiFunctionCall
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("arguments")] public string Arguments { get; init; } = "{}";
}
public sealed record OpenAiResponseFormat
{
    [JsonPropertyName("type")] public string Type { get; init; } = "text";
    [JsonPropertyName("json_schema")] public OpenAiJsonSchema? JsonSchema { get; init; }
}
public sealed record OpenAiJsonSchema
{
    [JsonPropertyName("name")] public string Name { get; init; } = "response";
    [JsonPropertyName("strict")] public bool Strict { get; init; }
    [JsonPropertyName("schema")] public JsonElement Schema { get; init; }
}
public static class OpenAiToolMapper
{
    public static LlmTestServerChatRequest Apply(LlmTestServerChatRequest request, List<OpenAiTool>? tools, JsonElement choice, OpenAiResponseFormat? format)
    {
        var named = choice.ValueKind == JsonValueKind.Object && choice.TryGetProperty("function", out var function)
            ? function.GetProperty("name").GetString() : null;
        return request with
        {
            Tools = tools?.Select(tool => new LlmTestServerToolDefinition { Name = tool.Function.Name, Description = tool.Function.Description, ParametersJson = tool.Function.Parameters.GetRawText() }).ToList() ?? [],
            ToolChoice = named is not null || choice.ValueKind == JsonValueKind.String && choice.GetString() == "required"
                ? LlmTestServerToolChoice.Required : choice.ValueKind == JsonValueKind.String && choice.GetString() == "none" ? LlmTestServerToolChoice.None : LlmTestServerToolChoice.Auto,
            RequiredToolName = named,
            RequireJson = format?.Type is "json_object" or "json_schema",
            ResponseSchemaJson = format?.JsonSchema?.Schema.GetRawText(),
        };
    }
    public static OpenAiToolCall ToWire(LlmTestServerToolCall call, int? index = null)
    {
        return new()
        {
            Id = call.Id,
            Index = index,
            Function = new() { Name = call.Name, Arguments = call.ArgumentsJson },
        };
    }

    public static LlmTestServerToolCall ToRuntime(OpenAiToolCall call)
    {
        return new()
        {
            Id = call.Id,
            Name = call.Function.Name,
            ArgumentsJson = call.Function.Arguments,
        };
    }
}
