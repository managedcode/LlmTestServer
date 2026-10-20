using System.Globalization;
using System.Text.Json;
using static ManagedCode.LlmTestServer.ProviderValidation.LlmTestServerJsonValidation;

namespace ManagedCode.LlmTestServer.Anthropic;

internal static class AnthropicNativeWebSearchValidation
{
    private const int _maxItems = 128;

    internal static bool ValidateTool(JsonElement tool)
    {
        Object(tool);
        if (!tool.TryGetProperty("type", out var type) || type.ValueKind == JsonValueKind.Null) { return false; }
        String(type);
        if (type.GetString() == "custom") { return false; }
        Fields(tool, "type", "name", "max_uses", "allowed_domains", "blocked_domains", "user_location");
        Choice(type, AnthropicNativeWebSearchContract.ToolType);
        Choice(tool.GetProperty("name"), AnthropicNativeWebSearchContract.ToolName);
        if (tool.TryGetProperty("max_uses", out var max) && max.ValueKind != JsonValueKind.Null
            && (!max.TryGetInt32(out var value) || value is < 0 or > _maxItems))
        { throw new JsonException("Native max_uses must be zero to 128 in this fixture contract."); }
        if (tool.TryGetProperty("allowed_domains", out var allowed) && allowed.ValueKind != JsonValueKind.Null
            && tool.TryGetProperty("blocked_domains", out var blocked) && blocked.ValueKind != JsonValueKind.Null)
        { throw new JsonException("Allowed and blocked domains are mutually exclusive."); }
        foreach (var name in new[] { "allowed_domains", "blocked_domains" })
        {
            if (!tool.TryGetProperty(name, out var domains) || domains.ValueKind == JsonValueKind.Null) { continue; }
            BoundedArray(domains);
            foreach (var domain in domains.EnumerateArray()) { ValidateDomain(domain); }
        }
        if (tool.TryGetProperty("user_location", out var location) && location.ValueKind != JsonValueKind.Null)
        { ValidateLocation(location); }
        return true;
    }

    internal static void ValidateBody(JsonElement body, bool hasNative)
    {
        if (body.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String
            && model.GetString() == AnthropicNativeWebSearchContract.ModelId
            && body.TryGetProperty("tool_choice", out var currentChoice) && currentChoice.ValueKind == JsonValueKind.Object
            && currentChoice.TryGetProperty("type", out var currentType) && currentType.ValueKind == JsonValueKind.String
            && currentType.GetString() is "any" or "tool")
        { throw new JsonException("Claude Sonnet 5.5 does not support forced tool choice."); }
        if (hasNative && body.GetProperty("model").GetString() != AnthropicNativeWebSearchContract.ModelId)
        { throw new JsonException("Native web search requires the qualified fixture model."); }
        if (body.TryGetProperty("tool_choice", out var choice) && choice.ValueKind == JsonValueKind.Object
            && choice.TryGetProperty("name", out var choiceName) && choiceName.ValueKind == JsonValueKind.String
            && choiceName.GetString() == AnthropicNativeWebSearchContract.ToolName && hasNative)
        {
            Fields(choice, "type", "name", "disable_parallel_tool_use");
            Choice(choice.GetProperty("type"), "tool");
        }
        foreach (var message in OptionalArray(body, "messages"))
        {
            if (!message.TryGetProperty("content", out var content)) { continue; }
            if (content.ValueKind == JsonValueKind.Object && content.TryGetProperty("type", out var objectType)
                && objectType.ValueKind == JsonValueKind.String
                && objectType.GetString() is "server_tool_use" or "web_search_tool_result")
            { throw new JsonException("Native history must be a content array."); }
            if (content.ValueKind != JsonValueKind.Array) { continue; }
            var native = false;
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("type", out var type)) { continue; }
                String(type);
                switch (type.GetString())
                {
                    case "server_tool_use":
                        Fields(part, "type", "id", "name", "input", "caller");
                        ValidateCaller(part);
                        Id(part.GetProperty("id"));
                        Choice(part.GetProperty("name"), AnthropicNativeWebSearchContract.ToolName);
                        var input = part.GetProperty("input");
                        Fields(input, "query");
                        NonEmpty(input.GetProperty("query"));
                        native = true;
                        break;
                    case "web_search_tool_result":
                        Fields(part, "type", "tool_use_id", "content", "caller");
                        ValidateCaller(part);
                        Id(part.GetProperty("tool_use_id"));
                        ValidateResults(part.GetProperty("content"));
                        native = true;
                        break;
                    case "text" when part.TryGetProperty("citations", out var citations):
                        if (citations.ValueKind == JsonValueKind.Null) { break; }
                        Array(citations);
                        if (citations.GetArrayLength() > 0)
                        {
                            Fields(part, "type", "text", "citations", "cache_control");
                            String(part.GetProperty("text"));
                            BoundedArray(citations);
                            foreach (var citation in citations.EnumerateArray()) { ValidateCitation(citation); }
                            native = true;
                        }
                        break;
                    default:
                        var name = type.GetString()!;
                        if (name.EndsWith("_tool_result", StringComparison.Ordinal)
                            || name.StartsWith("web_search", StringComparison.Ordinal)
                            || name.StartsWith("code_execution", StringComparison.Ordinal))
                        { throw new JsonException("Unsupported native history block."); }
                        break;
                }
            }
            if (!native) { continue; }
            Choice(message.GetProperty("role"), "assistant");
            BoundedArray(content);
            foreach (var part in content.EnumerateArray())
            {
                Object(part);
                Choice(part.GetProperty("type"), "text", "tool_use", "server_tool_use", "web_search_tool_result");
                if (part.GetProperty("type").GetString() == "tool_use") { Id(part.GetProperty("id")); ValidateCaller(part); }
                if (part.GetProperty("type").GetString() == "text")
                {
                    Fields(part, "type", "text", "citations", "cache_control");
                    String(part.GetProperty("text"));
                }
            }
        }
    }

    internal static void ValidateCaller(JsonElement block)
    {
        if (!block.TryGetProperty("caller", out var caller) || caller.ValueKind == JsonValueKind.Null) { return; }
        Fields(caller, "type");
        Choice(caller.GetProperty("type"), "direct");
    }

    private static void ValidateResults(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.Object)
        {
            Fields(content, "type", "error_code");
            Choice(content.GetProperty("type"), "web_search_tool_result_error");
            Choice(content.GetProperty("error_code"), "invalid_tool_input", "unavailable", "max_uses_exceeded", "too_many_requests", "query_too_long", "request_too_large");
            return;
        }
        BoundedArray(content);
        foreach (var result in content.EnumerateArray())
        {
            Fields(result, "type", "url", "title", "encrypted_content", "page_age");
            Choice(result.GetProperty("type"), "web_search_result");
            Url(result.GetProperty("url"));
            String(result.GetProperty("title"));
            NonEmpty(result.GetProperty("encrypted_content"));
            OptionalString(result, "page_age");
        }
    }

    private static void ValidateCitation(JsonElement citation)
    {
        Fields(citation, "type", "url", "encrypted_index", "cited_text", "title");
        Choice(citation.GetProperty("type"), "web_search_result_location");
        Url(citation.GetProperty("url"));
        NonEmpty(citation.GetProperty("encrypted_index"));
        String(citation.GetProperty("cited_text"));
        OptionalString(citation, "title");
    }

    private static void ValidateLocation(JsonElement location)
    {
        Fields(location, "type", "city", "region", "country", "timezone");
        Choice(location.GetProperty("type"), "approximate");
        var populated = false;
        foreach (var name in new[] { "city", "region", "country", "timezone" })
        {
            if (!location.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) { continue; }
            NonEmpty(value);
            populated = true;
            if (name == "country")
            {
                var country = value.GetString()!;
                if (country.Length != 2 || !country.All(char.IsAsciiLetter)
                    || !CultureInfo.GetCultures(CultureTypes.SpecificCultures).Any(culture =>
                        string.Equals(new RegionInfo(culture.Name).TwoLetterISORegionName, country, StringComparison.OrdinalIgnoreCase)))
                { throw new JsonException("An ISO country code is required."); }
            }
            if (name == "timezone" && (!TimeZoneInfo.TryFindSystemTimeZoneById(value.GetString()!, out var zone)
                || !zone.HasIanaId))
            { throw new JsonException("An IANA timezone is required."); }
        }
        if (!populated) { throw new JsonException("At least one location field is required."); }
    }

    private static void ValidateDomain(JsonElement value)
    {
        NonEmpty(value);
        var domain = value.GetString()!;
        var host = domain.Split('/', 2)[0];
        if (domain.Contains("://", StringComparison.Ordinal) || domain.Contains('\\')
            || domain.Contains('?') || domain.Contains('#') || domain.Any(char.IsWhiteSpace)
            || host.Length > 253 || Uri.CheckHostName(host) != UriHostNameType.Dns
            || host.Split('.').Any(label => label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-'
                || label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
        { throw new JsonException("A scheme-less domain with optional path is required."); }
    }

    private static void Url(JsonElement value)
    {
        NonEmpty(value);
        if (!Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        { throw new JsonException("An HTTP URL is required."); }
    }

    private static void Id(JsonElement value)
    {
        NonEmpty(value);
        if (value.GetString()!.Length > _maxItems) { throw new JsonException("Native IDs are bounded to 128 characters."); }
    }

    private static void OptionalString(JsonElement value, string name)
    {
        if (value.TryGetProperty(name, out var member) && member.ValueKind != JsonValueKind.Null) { String(member); }
    }

    private static void BoundedArray(JsonElement value)
    {
        Array(value);
        if (value.GetArrayLength() > _maxItems) { throw new JsonException("Native arrays are bounded to 128 items."); }
    }

    private static void Fields(JsonElement value, params string[] names)
    {
        Object(value);
        if (value.EnumerateObject().Any(property => !names.Contains(property.Name, StringComparer.Ordinal)))
        { throw new JsonException("Unsupported native field."); }
    }
}
