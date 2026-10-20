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
        if (!body.TryGetProperty(field, out var messages)) { return; }
        if (messages.ValueKind == JsonValueKind.Object) { ValidateRefusalHistory(messages, responses); return; }
        if (messages.ValueKind != JsonValueKind.Array) { return; }
        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object) { continue; }
            ValidateRefusalHistory(message, responses);
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

    private static void ValidateRefusalHistory(JsonElement message, bool responses)
    {
        var hasField = message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind != JsonValueKind.Null;
        var hasContent = message.TryGetProperty("content", out var content);
        if (hasContent && content.ValueKind == JsonValueKind.Object
            && content.TryGetProperty("type", out var objectType) && objectType.ValueKind == JsonValueKind.String
            && objectType.GetString() == "refusal")
        { throw new JsonException("Refusal content requires an array of typed refusal parts."); }
        var hasParts = hasContent && content.ValueKind == JsonValueKind.Array
            && content.EnumerateArray().Any(part => part.ValueKind == JsonValueKind.Object
                && part.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "refusal");
        if (!hasField && !hasParts) { return; }
        if (!message.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String || role.GetString() != "assistant")
        { throw new JsonException("Refusal history requires an assistant message."); }
        if (!responses)
        {
            if (hasParts)
            {
                if (hasField) { throw new JsonException("Refusal history cannot mix the refusal field and refusal content parts."); }
                foreach (var part in content.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object
                        || part.EnumerateObject().Any(property => property.Name is not ("type" or "refusal")))
                    { throw new JsonException("This Chat history fixture requires pure refusal content parts."); }
                    Choice(part.GetProperty("type"), "refusal");
                    String(part.GetProperty("refusal"));
                }
                return;
            }
            String(refusal);
            if (message.TryGetProperty("content", out content) && content.ValueKind != JsonValueKind.Null
                && !string.IsNullOrEmpty(OpenAiWireMapper.ReadResponseInputContent(content)))
            { throw new JsonException("This refusal history fixture cannot mix refusal with ordinary content."); }
            return;
        }
        if (hasField || content.GetArrayLength() != 1)
        { throw new JsonException("Responses refusal history requires one typed refusal content part."); }
        var responsePart = content[0];
        if (responsePart.EnumerateObject().Any(property => property.Name is not ("type" or "refusal")))
        { throw new JsonException("Unsupported refusal history content fields."); }
        String(responsePart.GetProperty("refusal"));
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
