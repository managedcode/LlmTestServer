using System.Text.Json;
using ManagedCode.LlmTestServer.Providers;
using static ManagedCode.LlmTestServer.ProviderValidation.LlmTestServerJsonValidation;

namespace ManagedCode.LlmTestServer.OpenAI;

public static class OpenAiRequestValidation
{
    public static LlmTestServerRequestValidationResult ValidateChat(JsonElement body)
    {
        return Check(body, value => Validate(value, false));
    }

    public static LlmTestServerRequestValidationResult ValidateResponse(JsonElement body)
    {
        return Check(body, value => Validate(value, true));
    }

    public static LlmTestServerRequestValidationResult ValidateNativeResponse(JsonElement body)
    {
        return Check(body, value => Validate(value, true, nativeTools: true));
    }

    private static void Validate(JsonElement body, bool responses, bool nativeTools = false)
    {
        if (body.TryGetProperty("functions", out _) || body.TryGetProperty("function_call", out _))
        {
            throw new JsonException("Use tools and tool_choice.");
        }
        foreach (var tool in OptionalArray(body, "tools"))
        {
            if (nativeTools && OpenAiNativeWebSearchValidation.IsNativeToolType(tool.GetProperty("type").GetString()))
            {
                if (tool.GetProperty("type").GetString() == OpenAiNativeWebSearchValidation._toolType)
                { OpenAiNativeWebSearchValidation.ValidateTool(tool); }
                else { OpenAiNativeFileCodeValidation.ValidateTool(tool); }
                continue;
            }
            Choice(tool.GetProperty("type"), "function");
            var function = responses ? tool : tool.GetProperty("function");
            Name(function); OptionalSchema(function, "parameters");
        }
        if (body.TryGetProperty("tool_choice", out var choice))
        {
            if (choice.ValueKind == JsonValueKind.Object)
            {
                if (nativeTools && OpenAiNativeWebSearchValidation.IsNativeToolType(choice.GetProperty("type").GetString()))
                {
                    // Exact choice fields and declaration applicability are validated below.
                }
                else
                {
                    Choice(choice.GetProperty("type"), "function");
                    Name(responses ? choice : choice.GetProperty("function"));
                }
            }
            else { Choice(choice, "auto", "none", "required"); }
        }
        if (body.TryGetProperty("parallel_tool_calls", out var parallel)) { _ = parallel.GetBoolean(); }
        if (body.TryGetProperty("response_format", out var format) && format.ValueKind != JsonValueKind.Null)
        {
            ValidateFormat(format, false);
        }
        if (responses && body.TryGetProperty("text", out var text) && text.ValueKind != JsonValueKind.Null && text.TryGetProperty("format", out format))
        {
            ValidateFormat(format, true);
        }
        if (responses) { OpenAiNativeWebSearchValidation.ValidateResponseOptions(body, nativeTools); }
        var field = responses ? "input" : "messages";
        if (!body.TryGetProperty(field, out var messages) || messages.ValueKind != JsonValueKind.Array) { return; }
        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object) { continue; }
            if (!responses)
            {
                if (message.TryGetProperty("type", out var nativeType)
                    && OpenAiNativeWebSearchValidation.IsNativeHistoryType(nativeType.GetString()))
                { throw new JsonException("Native history is unsupported on this operation."); }
                _ = OpenAiNativeWebSearchValidation.ValidateMessageCitations(message, enabled: false);
            }
            foreach (var call in OptionalArray(message, "tool_calls", allowNull: true))
            {
                Name(call.GetProperty("function")); NonEmpty(call.GetProperty("id")); String(call.GetProperty("function").GetProperty("arguments"));
            }
            if (responses && message.TryGetProperty("type", out var type))
            {
                if (type.GetString() == "function_call")
                {
                    Name(message); NonEmpty(message.GetProperty("call_id")); String(message.GetProperty("arguments"));
                    if (message.TryGetProperty("id", out var outputId)
                        && outputId.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    { throw new JsonException("A function output ID must be a string or null."); }
                }
                if (type.GetString() == "function_call_output") { NonEmpty(message.GetProperty("call_id")); _ = message.GetProperty("output"); }
            }
        }
    }

    private static void ValidateFormat(JsonElement format, bool responses)
    {
        Choice(format.GetProperty("type"), "text", "json_object", "json_schema");
        if (format.GetProperty("type").GetString() == "json_schema")
        {
            Schema(responses ? format.GetProperty("schema") : format.GetProperty("json_schema").GetProperty("schema"));
        }
        OptionalSchema(format, "schema");
    }
}
