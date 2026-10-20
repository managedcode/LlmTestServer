using System.Globalization;
using System.Text.Json;
using ManagedCode.LlmTestServer.Models;
using static ManagedCode.LlmTestServer.ProviderValidation.LlmTestServerJsonValidation;

namespace ManagedCode.LlmTestServer.OpenAI;

internal static class OpenAiNativeWebSearchValidation
{
    internal const string _toolType = "web_search";
    internal const string _sourcesInclude = "web_search_call.action.sources";

    internal static void ValidateTool(JsonElement tool)
    {
        Fields(tool, "type", "search_context_size", "user_location", "filters");
        Choice(tool.GetProperty("type"), _toolType);
        if (tool.TryGetProperty("search_context_size", out var context)) { Choice(context, "low", "medium", "high"); }
        if (tool.TryGetProperty("user_location", out var location) && location.ValueKind != JsonValueKind.Null) { ValidateLocation(location); }
        if (tool.TryGetProperty("filters", out var filters) && filters.ValueKind != JsonValueKind.Null)
        {
            Fields(filters, "allowed_domains");
            if (!filters.TryGetProperty("allowed_domains", out var domains) || domains.ValueKind == JsonValueKind.Null) { return; }
            Array(domains);
            if (domains.GetArrayLength() > 100) { throw new JsonException("A bounded domain allowlist is required."); }
            foreach (var entry in domains.EnumerateArray())
            {
                NonEmpty(entry);
                var domain = entry.GetString()!;
                if (domain.Length > 253 || Uri.CheckHostName(domain) != UriHostNameType.Dns
                    || domain.Split('.').Any(label => label.Length is < 1 or > 63
                        || label[0] == '-' || label[^1] == '-'
                        || label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
                { throw new JsonException("A bare domain name is required."); }
            }
        }
    }

    internal static void ValidateResponseOptions(JsonElement body, bool enabled)
    {
        var tools = OptionalArray(body, "tools").ToArray();
        var webTools = tools.Count(tool => tool.GetProperty("type").GetString() == _toolType);
        var nativeUsed = webTools > 0;
        if (body.TryGetProperty("include", out var include))
        {
            Array(include);
            foreach (var entry in include.EnumerateArray())
            {
                Choice(entry, _sourcesInclude);
                if (!enabled) { throw new JsonException("Unsupported include."); }
                nativeUsed = true;
            }
        }
        if (body.TryGetProperty("tool_choice", out var choice) && choice.ValueKind == JsonValueKind.Object
            && choice.GetProperty("type").GetString() == _toolType)
        {
            Fields(choice, "type");
            if (!enabled || webTools == 0) { throw new JsonException("Web search choice requires its declaration."); }
            nativeUsed = true;
        }
        if (body.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object)
        {
            if ((input.TryGetProperty("type", out var objectType)
                && objectType.GetString() is "web_search_call" or "file_search_call" or "code_interpreter_call")
                || ValidateMessageCitations(input, enabled))
            { throw new JsonException("Native history must use an input array."); }
        }
        if (input.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in input.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) { continue; }
                var itemType = item.TryGetProperty("type", out var type) ? type.GetString() : null;
                if (itemType == "web_search_call")
                {
                    if (!enabled) { throw new JsonException("Native history is unsupported on this provider."); }
                    ValidateHistoryCall(item);
                    nativeUsed = true;
                }
                if (itemType is "file_search_call" or "code_interpreter_call")
                { throw new JsonException("Unsupported native history item."); }
                if (HasCitations(item) && itemType is not (null or "message"))
                { throw new JsonException("Unsupported annotated native message type."); }
                if ((itemType is null or "message") && ValidateMessageCitations(item, enabled)) { nativeUsed = true; }
            }
        }
        if (input.ValueKind == JsonValueKind.Array) { ValidateNativeGroups(input); }
        if (nativeUsed && (!enabled || body.GetProperty("model").GetString() != LlmTestServerKnownModelIds.Gpt41Mini))
        { throw new JsonException("Native web search requires a supported Responses model."); }
    }

    private static void ValidateHistoryCall(JsonElement item)
    {
        Fields(item, "type", "id", "status", "action");
        NonEmpty(item.GetProperty("id"));
        ValidateItemId(item.GetProperty("id").GetString()!);
        Choice(item.GetProperty("status"), "completed");
        var action = item.GetProperty("action");
        Fields(action, "type", "queries", "sources");
        Choice(action.GetProperty("type"), "search");
        var queries = action.GetProperty("queries");
        Array(queries);
        if (queries.GetArrayLength() is < 1 or > 32) { throw new JsonException("Search history requires one to 32 queries."); }
        foreach (var query in queries.EnumerateArray()) { NonEmpty(query); }
        if (action.TryGetProperty("sources", out var sources))
        {
            Array(sources);
            if (sources.GetArrayLength() > 128) { throw new JsonException("Too many search sources."); }
            foreach (var source in sources.EnumerateArray())
            {
                Fields(source, "type", "url");
                Choice(source.GetProperty("type"), "url");
                Url(source.GetProperty("url"));
            }
        }
    }

    private static bool ValidateMessageCitations(JsonElement item, bool enabled)
    {
        if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) { return false; }
        var annotated = false;
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("annotations", out var annotations)) { continue; }
            Array(annotations);
            if (annotations.GetArrayLength() == 0) { continue; }
            if (!enabled || content.GetArrayLength() != 1) { throw new JsonException("Unsupported annotated native message."); }
            if (annotations.GetArrayLength() > 128) { throw new JsonException("Too many URL citations."); }
            if (item.TryGetProperty("id", out var messageId))
            {
                String(messageId);
                ValidateItemId(messageId.GetString()!);
            }
            Choice(item.GetProperty("role"), "assistant");
            Fields(part, "type", "text", "annotations");
            Choice(part.GetProperty("type"), "output_text");
            String(part.GetProperty("text"));
            var text = part.GetProperty("text").GetString()!;
            if (text.Any(character => character > 127)) { throw new JsonException("Native citation text must be ASCII in this contract."); }
            foreach (var citation in annotations.EnumerateArray())
            {
                Fields(citation, "type", "start_index", "end_index", "url", "title");
                Choice(citation.GetProperty("type"), "url_citation");
                if (!citation.GetProperty("start_index").TryGetInt32(out var start)
                    || !citation.GetProperty("end_index").TryGetInt32(out var end)
                    || start < 0 || end <= start || end > text.Length)
                { throw new JsonException("Invalid native citation range."); }
                Url(citation.GetProperty("url"));
                String(citation.GetProperty("title"));
            }
            annotated = true;
        }
        return annotated;
    }

    private static void ValidateNativeGroups(JsonElement input)
    {
        var group = new List<JsonElement>();
        var count = 0;
        var native = false;
        void FinishGroup()
        {
            if (native)
            {
                if (count > 128) { throw new JsonException("Too many native assistant output items."); }
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in group)
                {
                    var type = item.TryGetProperty("type", out var discriminator) ? discriminator.GetString() : null;
                    string? id = null;
                    if (type == "function_call")
                    {
                        id = item.TryGetProperty("id", out var functionId)
                            ? functionId.GetString() : "fc_" + item.GetProperty("call_id").GetString();
                        if (string.IsNullOrWhiteSpace(id)) { throw new JsonException("A native function output ID is required."); }
                    }
                    else if (type == "web_search_call" || HasCitations(item) || item.TryGetProperty("id", out _))
                    {
                        if (item.TryGetProperty("id", out var outputId))
                        {
                            String(outputId);
                            id = outputId.GetString();
                        }
                        else { id = string.Empty; }
                    }
                    if (id is null) { continue; }
                    ValidateItemId(id);
                    if (id.Length > 0 && !ids.Add(id)) { throw new JsonException("Duplicate native assistant output ID."); }
                }
            }
            group.Clear();
            count = 0;
            native = false;
        }
        foreach (var item in input.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                if (!string.IsNullOrWhiteSpace(OpenAiWireMapper.ReadResponseInputContent(item))) { FinishGroup(); }
                continue;
            }
            var type = item.TryGetProperty("type", out var discriminator) ? discriminator.GetString() : null;
            var assistant = type is "web_search_call" or "function_call"
                || type != "function_call_output" && item.TryGetProperty("role", out var role) && role.GetString() == "assistant";
            var citations = HasCitations(item);
            var retained = type is "web_search_call" or "function_call" or "function_call_output" || citations
                || assistant && item.TryGetProperty("id", out _)
                || !string.IsNullOrWhiteSpace(item.TryGetProperty("content", out var content) ? OpenAiWireMapper.ReadResponseInputContent(content)
                    : item.TryGetProperty("text", out var text) ? OpenAiWireMapper.ReadResponseInputContent(text) : item.ToString());
            if (!retained) { continue; }
            if (!assistant) { FinishGroup(); continue; }
            count++;
            native |= type == "web_search_call" || citations;
            if (group.Count < 129) { group.Add(item); }
            if (native && count > 128) { throw new JsonException("Too many native assistant output items."); }
        }
        FinishGroup();
    }

    private static bool HasCitations(JsonElement item)
    {
        return item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            && content.EnumerateArray().Any(part => part.ValueKind == JsonValueKind.Object
                && part.TryGetProperty("annotations", out var annotations) && annotations.ValueKind == JsonValueKind.Array
                && annotations.GetArrayLength() > 0);
    }

    private static void ValidateItemId(string id)
    {
        if (id.Length > 128 || id.Length > 0 && string.IsNullOrWhiteSpace(id))
        { throw new JsonException("Native output IDs must be bounded nonblank labels."); }
    }

    private static void Url(JsonElement value)
    {
        NonEmpty(value);
        if (value.GetString()!.Any(char.IsWhiteSpace) || !Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        { throw new JsonException("An absolute HTTP URL is required."); }
    }

    private static void ValidateLocation(JsonElement location)
    {
        Fields(location, "type", "city", "region", "country", "timezone");
        if (location.TryGetProperty("type", out var approximation)) { Choice(approximation, "approximate"); }
        foreach (var field in new[] { "city", "region", "country", "timezone" })
        {
            if (!location.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) { continue; }
            String(value);
            var text = value.GetString()!;
            if (field == "country")
            {
                if (text.Length != 2 || text.Any(character => !char.IsAsciiLetter(character)))
                { throw new JsonException("An ISO country code is required."); }
                try
                {
                    if (!string.Equals(new RegionInfo(text).TwoLetterISORegionName, text, StringComparison.OrdinalIgnoreCase)) { throw new JsonException("Unknown country code."); }
                }
                catch (ArgumentException) { throw new JsonException("Unknown country code."); }
            }
            if (field == "timezone" && (text.Any(char.IsWhiteSpace) || !TimeZoneInfo.TryFindSystemTimeZoneById(text, out _)))
            { throw new JsonException("An IANA timezone is required."); }
        }
    }

    private static void Fields(JsonElement value, params string[] allowed)
    {
        Object(value);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
            { throw new JsonException("Unsupported or duplicate native tool field."); }
        }
    }
}
