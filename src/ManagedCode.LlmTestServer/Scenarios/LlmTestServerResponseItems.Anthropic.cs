using System.Text.Json;

namespace ManagedCode.LlmTestServer.Scenarios;

internal static partial class LlmTestServerResponseItems
{
    private static LlmTestServerResponseServerToolUseItem SnapshotServerToolUse(LlmTestServerResponseServerToolUseItem call)
    {
        Identifier(call.Id);
        if (call.Contract != LlmTestServerNativeToolContract.AnthropicMessagesWebSearch20250305 || call.Input is not LlmTestServerWebSearchInput input)
        { throw new ArgumentException("Unknown server tool call contract or input."); }
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Query);
        return call with { Input = input with { } };
    }

    private static LlmTestServerResponseServerToolResultItem SnapshotServerToolResult(LlmTestServerResponseServerToolResultItem item)
    {
        Identifier(item.ToolUseId);
        if (item.Contract != LlmTestServerNativeToolContract.AnthropicMessagesWebSearch20250305)
        { throw new ArgumentException("Unknown server tool result contract."); }
        LlmTestServerServerToolResult result;
        switch (item.Result)
        {
            case LlmTestServerWebSearchResults web:
                ArgumentNullException.ThrowIfNull(web.Results);
                if (web.Results.Count > 128) { throw new ArgumentException("Too many server search results."); }
                result = web with
                {
                    Results = web.Results.Select(source =>
                    {
                        ArgumentNullException.ThrowIfNull(source);
                        ValidateUrl(source.Url);
                        ArgumentNullException.ThrowIfNull(source.Title);
                        ArgumentException.ThrowIfNullOrWhiteSpace(source.EncryptedContent);
                        return source with { };
                    }).ToList(),
                };
                break;
            case LlmTestServerWebSearchErrorResult error:
                if (error.ErrorCode is not ("invalid_tool_input" or "unavailable" or "max_uses_exceeded" or "too_many_requests" or "query_too_long" or "request_too_large"))
                { throw new ArgumentException("Unknown server web search error code."); }
                result = error with { };
                break;
            default:
                throw new ArgumentException("Unknown server tool result.");
        }
        return item with { Result = result };
    }

    private static LlmTestServerAnthropicWebSearchCitation SnapshotAnthropicCitation(LlmTestServerAnthropicWebSearchCitation citation)
    {
        ValidateUrl(citation.Url);
        ArgumentException.ThrowIfNullOrWhiteSpace(citation.EncryptedIndex);
        ArgumentNullException.ThrowIfNull(citation.CitedText);
        return citation with { };
    }

    internal static void ValidateCompletedServerTools(IReadOnlyList<LlmTestServerResponseItem> items)
    {
        var pending = new Dictionary<string, LlmTestServerNativeToolContract>(StringComparer.Ordinal);
        var callIds = new HashSet<string>(StringComparer.Ordinal);
        var functionIds = items.OfType<LlmTestServerResponseFunctionCallItem>().Select(item => item.Call.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is LlmTestServerResponseServerToolUseItem call)
            {
                if (!callIds.Add(call.Id) || functionIds.Contains(call.Id)) { throw new ArgumentException("Server and function tool call IDs must be distinct."); }
                pending.Add(call.Id, call.Contract);
            }
            else if (item is LlmTestServerResponseServerToolResultItem result)
            {
                if (!pending.Remove(result.ToolUseId, out var contract) || contract != result.Contract)
                { throw new ArgumentException("Each server tool result must follow its matching call exactly once."); }
            }
        }
        if (pending.Count > 0) { throw new ArgumentException("This fixture contract requires completed server tool call/result pairs."); }
    }

    private static LlmTestServerAnthropicWebSearchOptions? SnapshotAnthropicOptions(LlmTestServerAnthropicWebSearchOptions? options)
    {
        if (options is null) { return null; }
        if (options.MaxUses is < 0 or > 128 || options.AllowedDomains is not null && options.BlockedDomains is not null
            || options.UserLocation is { Type: not "approximate" })
        { throw new ArgumentException("Invalid Anthropic web search options."); }
        foreach (var patterns in new[] { options.AllowedDomains, options.BlockedDomains })
        {
            if (patterns is null) { continue; }
            if (patterns.Count > 128) { throw new ArgumentException("Too many web domain filters."); }
            foreach (var pattern in patterns) { ValidateDomainPattern(pattern); }
        }
        return CloneAnthropicOptions(options);
    }

    private static LlmTestServerAnthropicWebSearchOptions? CloneAnthropicOptions(LlmTestServerAnthropicWebSearchOptions? options)
    {
        return options is null ? null : options with
        {
            AllowedDomains = options.AllowedDomains is null ? null : [.. options.AllowedDomains],
            BlockedDomains = options.BlockedDomains is null ? null : [.. options.BlockedDomains],
            UserLocation = options.UserLocation is null ? null : options.UserLocation with { },
        };
    }

    private static void ValidateDomainPattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var slash = pattern.IndexOf('/');
        var host = slash < 0 ? pattern : pattern[..slash];
        if (pattern.Contains("://", StringComparison.Ordinal) || pattern.IndexOfAny(['\\', '?', '#']) >= 0
            || pattern.Any(char.IsWhiteSpace) || host.Length > 253 || Uri.CheckHostName(host) != UriHostNameType.Dns
            || host.Split('.').Any(label => label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-'
                || label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
        { throw new ArgumentException("Domain filters require an ASCII DNS host and optional path; wildcards are allowed only in the path."); }
    }

    internal static bool AllowsAnthropicUrl(LlmTestServerAnthropicWebSearchOptions? options, string url)
    {
        var uri = new Uri(url);
        return (options?.AllowedDomains is null || options.AllowedDomains.Any(pattern => MatchesDomainPattern(uri, pattern)))
            && (options?.BlockedDomains is null || !options.BlockedDomains.Any(pattern => MatchesDomainPattern(uri, pattern)));
    }

    private static bool MatchesDomainPattern(Uri uri, string pattern)
    {
        var slash = pattern.IndexOf('/');
        var host = slash < 0 ? pattern : pattern[..slash];
        if (!uri.IdnHost.Equals(host, StringComparison.OrdinalIgnoreCase)
            && !uri.IdnHost.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)) { return false; }
        if (slash < 0) { return true; }
        var path = new Uri("https://" + pattern).AbsolutePath;
        return GlobPrefix(uri.AbsolutePath, path);
    }

    private static bool GlobPrefix(string text, string pattern)
    {
        var position = 0;
        var index = 0;
        var star = -1;
        var restart = 0;
        while (position < text.Length)
        {
            if (index == pattern.Length) { return true; }
            if (pattern[index] == '*') { star = index++; restart = position; }
            else if (pattern[index] == text[position]) { index++; position++; }
            else if (star >= 0) { index = star + 1; position = ++restart; }
            else { return false; }
        }
        while (index < pattern.Length && pattern[index] == '*') { index++; }
        return index == pattern.Length;
    }

    private static string ServerToolInputTokenText(LlmTestServerServerToolInput input)
    {
        return input switch
        {
            LlmTestServerWebSearchInput web => JsonSerializer.Serialize(new { query = web.Query }),
            _ => string.Empty,
        };
    }
}
