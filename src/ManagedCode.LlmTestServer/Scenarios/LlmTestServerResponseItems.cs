using System.Text.Json;

namespace ManagedCode.LlmTestServer.Scenarios;

internal static partial class LlmTestServerResponseItems
{
    internal static List<LlmTestServerResponseItem> Snapshot(IEnumerable<LlmTestServerResponseItem> items, bool validateChunks = false)
    {
        ArgumentNullException.ThrowIfNull(items);
        var sourceItems = items.ToList();
        var nativePlan = sourceItems.Any(IsNative);
        var result = new List<LlmTestServerResponseItem>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in sourceItems)
        {
            if (result.Count >= 128) { throw new ArgumentException("At most 128 response output items are supported.", nameof(items)); }
            ArgumentNullException.ThrowIfNull(item);
            string id;
            switch (item)
            {
                case LlmTestServerResponseMessageItem message:
                    ArgumentNullException.ThrowIfNull(message.Id);
                    ArgumentNullException.ThrowIfNull(message.Content);
                    ArgumentNullException.ThrowIfNull(message.StreamChunks);
                    ArgumentNullException.ThrowIfNull(message.Citations);
                    foreach (var chunk in message.StreamChunks) { ArgumentNullException.ThrowIfNull(chunk); }
                    if (validateChunks && message.StreamChunks.Count > 0 && string.Concat(message.StreamChunks) != message.Content)
                    { throw new ArgumentException("Message chunks must concatenate to the output text.", nameof(items)); }
                    if (message.Citations.Count > 128) { throw new ArgumentException("Too many URL citations.", nameof(items)); }
                    var citations = message.Citations.Select(citation => SnapshotCitation(citation, message.Content)).ToList();
                    result.Add(message with { StreamChunks = [.. message.StreamChunks], Citations = citations });
                    id = message.Id;
                    break;
                case LlmTestServerResponseFunctionCallItem function:
                    ArgumentNullException.ThrowIfNull(function.Id);
                    if (nativePlan) { ArgumentException.ThrowIfNullOrWhiteSpace(function.Id); }
                    ArgumentNullException.ThrowIfNull(function.Call);
                    ArgumentException.ThrowIfNullOrWhiteSpace(function.Call.Id);
                    ArgumentException.ThrowIfNullOrWhiteSpace(function.Call.Name);
                    LlmTestServerJsonSchema.ValidateJson(function.Call.ArgumentsJson);
                    result.Add(function with { Call = function.Call with { } });
                    id = function.Id.Length > 0 ? function.Id : function.Call.Id;
                    break;
                case LlmTestServerResponseWebSearchItem web:
                    ArgumentException.ThrowIfNullOrWhiteSpace(web.Id);
                    ArgumentNullException.ThrowIfNull(web.Queries);
                    ArgumentNullException.ThrowIfNull(web.Sources);
                    if (web.Contract != LlmTestServerNativeToolContract.OpenAiResponsesWebSearch || web.Queries.Count is < 1 or > 32 || web.Sources.Count > 128)
                    { throw new ArgumentException("Invalid native search fixture.", nameof(items)); }
                    foreach (var query in web.Queries) { ArgumentException.ThrowIfNullOrWhiteSpace(query); }
                    foreach (var source in web.Sources) { ArgumentNullException.ThrowIfNull(source); ValidateUrl(source.Url); }
                    result.Add(web with { Queries = [.. web.Queries], Sources = [.. web.Sources.Select(source => source with { })] });
                    id = web.Id;
                    break;
                case LlmTestServerResponseFileSearchItem file:
                    result.Add(SnapshotFileSearch(file, validateChunks));
                    id = file.Id;
                    break;
                case LlmTestServerResponseCodeInterpreterItem code:
                    result.Add(SnapshotCode(code));
                    id = code.Id;
                    break;
                case LlmTestServerResponseServerToolUseItem serverCall:
                    result.Add(SnapshotServerToolUse(serverCall));
                    id = serverCall.Id;
                    break;
                case LlmTestServerResponseServerToolResultItem serverResult:
                    result.Add(SnapshotServerToolResult(serverResult));
                    id = string.Empty;
                    break;
                default:
                    throw new ArgumentException("Unknown response output item.", nameof(items));
            }
            var legacyFunctionId = item is LlmTestServerResponseFunctionCallItem { Id.Length: 0 };
            if (!legacyFunctionId && id.Length > 128 || id.Length > 0 && (string.IsNullOrWhiteSpace(id) || !ids.Add(id))) { throw new ArgumentException("Output item IDs must be distinct and bounded.", nameof(items)); }
        }
        if (validateChunks) { ValidateCompletedServerTools(result); }
        return result;
    }

    internal static LlmTestServerScenarioResponse Project(LlmTestServerScenarioResponse response)
    {
        if (response.OutputItems.Count == 0) { return response; }
        var items = Snapshot(response.OutputItems);
        return response with
        {
            OutputItems = items,
            Content = string.Concat(items.OfType<LlmTestServerResponseMessageItem>().Select(message => message.Content)),
            StreamChunks = items.OfType<LlmTestServerResponseMessageItem>().SelectMany(message => message.StreamChunks.Count > 0 ? message.StreamChunks : [message.Content]).ToList(),
            ToolCalls = items.OfType<LlmTestServerResponseFunctionCallItem>().Select(function => function.Call with { }).ToList(),
        };
    }

    internal static List<LlmTestServerNativeToolDeclaration> SnapshotDeclarations(IEnumerable<LlmTestServerNativeToolDeclaration> declarations, bool validate = false)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        return validate
            ? declarations.Select(SnapshotDeclaration).ToList()
            : declarations.Where(declaration => declaration is not null).Select(CloneDeclaration).ToList();
    }

    internal static bool AllowsUrl(LlmTestServerWebSearchOptions? options, string url)
    {
        if (options is null || options.AllowedDomains.Count == 0) { return true; }
        var host = new Uri(url).IdnHost;
        return options.AllowedDomains.Any(domain =>
        {
            var allowed = new Uri("https://" + domain).IdnHost;
            return host.Equals(allowed, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase);
        });
    }

    internal static string NativeTokenText(IEnumerable<LlmTestServerResponseItem> items)
    {
        return string.Concat(items.Where(IsNative).Select(item => item switch
        {
            LlmTestServerResponseServerToolUseItem call => ServerToolInputTokenText(call.Input),
            LlmTestServerResponseServerToolResultItem => string.Empty,
            _ => JsonSerializer.Serialize<LlmTestServerResponseItem>(item),
        }));
    }

    internal static long Bytes(IEnumerable<LlmTestServerResponseItem> items)
    {
        return items.Sum(item => 128L + JsonSerializer.SerializeToUtf8Bytes<LlmTestServerResponseItem>(item).LongLength);
    }

    private static void ValidateUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        { throw new ArgumentException("Native fixture URLs must be absolute HTTP or HTTPS URLs.", nameof(value)); }
    }
}
