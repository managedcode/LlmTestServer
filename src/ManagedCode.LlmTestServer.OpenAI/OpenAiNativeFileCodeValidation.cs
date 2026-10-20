using System.Text.Json;
using static ManagedCode.LlmTestServer.OpenAI.OpenAiNativeWebSearchValidation;
using static ManagedCode.LlmTestServer.ProviderValidation.LlmTestServerJsonValidation;

namespace ManagedCode.LlmTestServer.OpenAI;

internal static class OpenAiNativeFileCodeValidation
{
    internal const string _fileType = "file_search";
    internal const string _codeType = "code_interpreter";
    internal const string _resultsInclude = "file_search_call.results";
    internal const string _outputsInclude = "code_interpreter_call.outputs";

    internal static void ValidateTool(JsonElement tool)
    {
        if (tool.GetProperty("type").GetString() == _fileType)
        {
            Fields(tool, "type", "vector_store_ids", "max_num_results", "ranking_options", "filters");
            IdArray(tool.GetProperty("vector_store_ids"));
            if (tool.TryGetProperty("max_num_results", out var maximum)
                && (!maximum.TryGetInt32(out var count) || count is < 1 or > 50))
            { throw new JsonException("File search result limit must be an integer from one to 50."); }
            if (tool.TryGetProperty("ranking_options", out var ranking))
            {
                Fields(ranking, "ranker", "score_threshold");
                if (ranking.TryGetProperty("ranker", out var ranker)) { Choice(ranker, "auto", "default-2024-11-15"); }
                if (ranking.TryGetProperty("score_threshold", out var score)) { Score(score); }
            }
            if (tool.TryGetProperty("filters", out var filters) && filters.ValueKind != JsonValueKind.Null)
            {
                var nodes = 0;
                ValidateFilter(filters, 1, ref nodes);
            }
            return;
        }
        Fields(tool, "type", "container");
        var container = tool.GetProperty("container");
        if (container.ValueKind == JsonValueKind.String) { Id(container); return; }
        Fields(container, "type", "file_ids");
        Choice(container.GetProperty("type"), "auto");
        if (container.TryGetProperty("file_ids", out var files)) { IdArray(files); }
    }

    internal static void ValidateHistory(JsonElement item)
    {
        var file = item.GetProperty("type").GetString() == "file_search_call";
        if (file) { Fields(item, "type", "id", "status", "queries", "results"); }
        else { Fields(item, "type", "id", "status", "container_id", "code", "outputs"); }
        Id(item.GetProperty("id"));
        Choice(item.GetProperty("status"), "completed");
        if (file)
        {
            var queries = item.GetProperty("queries");
            Array(queries);
            if (queries.GetArrayLength() is < 1 or > 128) { throw new JsonException("File queries exceed the fixture budget."); }
            foreach (var query in queries.EnumerateArray()) { NonEmpty(query); }
            if (item.TryGetProperty("results", out var results) && results.ValueKind != JsonValueKind.Null)
            {
                Array(results);
                if (results.GetArrayLength() > 128) { throw new JsonException("Too many file results."); }
                foreach (var result in results.EnumerateArray()) { ValidateResult(result); }
            }
            return;
        }
        Id(item.GetProperty("container_id"));
        if (item.TryGetProperty("code", out var code) && code.ValueKind != JsonValueKind.Null) { String(code); }
        if (item.TryGetProperty("outputs", out var outputs) && outputs.ValueKind != JsonValueKind.Null)
        {
            Array(outputs);
            if (outputs.GetArrayLength() > 128) { throw new JsonException("Too many code outputs."); }
            foreach (var output in outputs.EnumerateArray())
            {
                Choice(output.GetProperty("type"), "logs", "image");
                if (output.GetProperty("type").GetString() == "logs")
                { Fields(output, "type", "logs"); String(output.GetProperty("logs")); }
                else { Fields(output, "type", "url"); Url(output.GetProperty("url")); }
            }
        }
    }

    internal static string ValidateCitation(JsonElement citation, int textLength)
    {
        if (citation.GetProperty("type").GetString() == "file_citation")
        {
            Fields(citation, "type", "file_id", "filename", "index");
            Id(citation.GetProperty("file_id"));
            NonEmpty(citation.GetProperty("filename"));
            if (!citation.GetProperty("index").TryGetInt32(out var index) || index < 0 || index > textLength)
            { throw new JsonException("Invalid file citation insertion index."); }
            return _fileType;
        }
        Fields(citation, "type", "container_id", "file_id", "filename", "start_index", "end_index");
        Choice(citation.GetProperty("type"), "container_file_citation");
        Id(citation.GetProperty("container_id"));
        Id(citation.GetProperty("file_id"));
        NonEmpty(citation.GetProperty("filename"));
        if (!citation.GetProperty("start_index").TryGetInt32(out var start)
            || !citation.GetProperty("end_index").TryGetInt32(out var end)
            || start < 0 || end < start || end > textLength)
        { throw new JsonException("Invalid container citation range."); }
        return _codeType;
    }

    private static void ValidateResult(JsonElement result)
    {
        Fields(result, "file_id", "filename", "text", "score", "attributes");
        if (result.TryGetProperty("file_id", out var file)) { Id(file); }
        if (result.TryGetProperty("filename", out var name)) { NonEmpty(name); }
        if (result.TryGetProperty("text", out var text)) { String(text); }
        if (result.TryGetProperty("score", out var score)) { Score(score); }
        if (result.TryGetProperty("attributes", out var attributes) && attributes.ValueKind != JsonValueKind.Null)
        {
            Object(attributes);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in attributes.EnumerateObject())
            {
                if (keys.Count >= 16 || !keys.Add(property.Name) || string.IsNullOrWhiteSpace(property.Name) || property.Name.Length > 64)
                { throw new JsonException("Invalid file attribute key budget."); }
                Scalar(property.Value);
                if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString()!.Length > 512)
                { throw new JsonException("File attribute text exceeds the fixture budget."); }
            }
        }
    }

    private static void ValidateFilter(JsonElement filter, int depth, ref int nodes)
    {
        if (depth > 8 || ++nodes > 128) { throw new JsonException("File filter exceeds the fixture depth or node budget."); }
        var type = filter.GetProperty("type");
        Choice(type, "eq", "ne", "gt", "gte", "lt", "lte", "in", "nin", "and", "or");
        if (type.GetString() is "and" or "or")
        {
            Fields(filter, "type", "filters");
            var children = filter.GetProperty("filters");
            Array(children);
            if (children.GetArrayLength() is < 1 or > 128) { throw new JsonException("Invalid compound filter size."); }
            foreach (var child in children.EnumerateArray()) { ValidateFilter(child, depth + 1, ref nodes); }
            return;
        }
        Fields(filter, "type", "key", "value");
        NonEmpty(filter.GetProperty("key"));
        if (filter.GetProperty("key").GetString()!.Length > 64) { throw new JsonException("Filter attribute key exceeds the fixture budget."); }
        var value = filter.GetProperty("value");
        if (type.GetString() is "in" or "nin")
        {
            Array(value);
            if (value.GetArrayLength() is < 1 or > 128) { throw new JsonException("Invalid filter comparison list size."); }
            var kind = value[0].ValueKind;
            if (kind is not (JsonValueKind.String or JsonValueKind.Number)) { throw new JsonException("Comparison arrays require strings or numbers."); }
            foreach (var entry in value.EnumerateArray())
            {
                if (entry.ValueKind != kind) { throw new JsonException("Comparison arrays must have one scalar type."); }
                Scalar(entry);
            }
            return;
        }
        Scalar(value);
        if (type.GetString() is not ("eq" or "ne") && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
        { throw new JsonException("Ordering comparisons require strings or numbers."); }
    }

    private static void Scalar(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.String or JsonValueKind.True or JsonValueKind.False) { return; }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)) { return; }
        throw new JsonException("A finite scalar attribute value is required.");
    }

    private static void Score(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var score) || !double.IsFinite(score) || score is < 0 or > 1)
        { throw new JsonException("A score from zero to one is required."); }
    }

    private static void IdArray(JsonElement value)
    {
        Array(value);
        if (value.GetArrayLength() > 128) { throw new JsonException("Resource IDs exceed the fixture budget."); }
        foreach (var id in value.EnumerateArray()) { Id(id); }
    }

    private static void Id(JsonElement value)
    {
        NonEmpty(value);
        ValidateItemId(value.GetString()!);
    }
}
