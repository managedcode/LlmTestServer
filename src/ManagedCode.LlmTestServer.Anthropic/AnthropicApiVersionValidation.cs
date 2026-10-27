using System.Text.Json;

namespace ManagedCode.LlmTestServer.Anthropic;

internal static class AnthropicApiVersionValidation
{
    internal static string? Validate(JsonElement body, string version)
    {
        if (version != AnthropicWireMapper.CurrentVersion) { return null; }
        foreach (var field in new[] { "temperature", "top_p", "top_k" })
        {
            if (body.TryGetProperty(field, out _))
            { return $"{field} is not supported in API {AnthropicWireMapper.CurrentVersion}."; }
        }
        if (!body.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array) { return null; }
        foreach (var message in messages.EnumerateArray())
        {
            if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) { continue; }
            var role = message.TryGetProperty("role", out var roleValue) ? roleValue.GetString() : null;
            foreach (var block in content.EnumerateArray())
            {
                if (ValidateBlock(block, role) is { } error) { return error; }
            }
        }
        return null;
    }

    private static string? ValidateBlock(JsonElement block, string? role)
    {
        if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("type", out var type)) { return null; }
        if (role == "assistant" && type.GetString() == "document")
        { return $"A document block cannot appear directly in an assistant message in API {AnthropicWireMapper.CurrentVersion}."; }
        if (role == "user" && type.GetString() == "text" && HasCitations(block)) { return CitationError; }
        if (type.GetString() == "search_result" && block.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("type", out var partType) && partType.GetString() == "text" && HasCitations(part))
                { return CitationError; }
            }
        }
        return null;
    }

    private static bool HasCitations(JsonElement block)
    {
        return block.TryGetProperty("citations", out var citations) && citations.ValueKind == JsonValueKind.Array && citations.GetArrayLength() > 0;
    }

    private static string CitationError =>
        $"Non-empty text citations are not supported in user messages or search_result blocks in API {AnthropicWireMapper.CurrentVersion}.";
}
