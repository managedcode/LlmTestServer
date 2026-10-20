using System.Text.Json;
using ManagedCode.LlmTestServer.Providers;
using static ManagedCode.LlmTestServer.ProviderValidation.LlmTestServerJsonValidation;

namespace ManagedCode.LlmTestServer.Anthropic;

public static class AnthropicRequestValidation
{
    public static LlmTestServerRequestValidationResult Validate(JsonElement body)
    {
        return Check(body, ValidateBody);
    }

    private static void ValidateBody(JsonElement body)
    {
        if (body.TryGetProperty("functions", out _) || body.TryGetProperty("function_call", out _))
        {
            throw new JsonException("Legacy function fields are unsupported.");
        }
        var hasNative = false;
        foreach (var tool in OptionalArray(body, "tools"))
        {
            if (AnthropicNativeWebSearchValidation.ValidateTool(tool)) { hasNative = true; }
            else { Name(tool); Schema(tool.GetProperty("input_schema")); }
        }
        AnthropicNativeWebSearchValidation.ValidateBody(body, hasNative);
        if (body.TryGetProperty("tool_choice", out var choice))
        {
            Choice(choice.GetProperty("type"), "auto", "none", "any", "tool");
            if (choice.GetProperty("type").GetString() == "tool") { Name(choice); }
            if (choice.TryGetProperty("disable_parallel_tool_use", out var disabled)) { _ = disabled.GetBoolean(); }
        }
        if (body.TryGetProperty("output_config", out var output) && output.ValueKind != JsonValueKind.Null && output.TryGetProperty("format", out var format))
        {
            Choice(format.GetProperty("type"), "json_schema"); Schema(format.GetProperty("schema"));
        }
        foreach (var message in OptionalArray(body, "messages"))
        {
            if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) { continue; }
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("type", out var type)) { continue; }
                if (type.GetString() == "tool_use") { AnthropicNativeWebSearchValidation.ValidateCaller(part); Name(part); NonEmpty(part.GetProperty("id")); Object(part.GetProperty("input")); }
                if (type.GetString() == "tool_result") { NonEmpty(part.GetProperty("tool_use_id")); _ = part.GetProperty("content"); }
            }
        }
    }
}
