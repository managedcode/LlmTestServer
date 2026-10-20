using System.Text.Json;

namespace ManagedCode.LlmTestServer.Scenarios;

internal static partial class LlmTestServerResponseItems
{
    internal static bool IsNative(LlmTestServerResponseItem item)
    {
        return item is LlmTestServerResponseWebSearchItem or LlmTestServerResponseFileSearchItem or LlmTestServerResponseCodeInterpreterItem
            or LlmTestServerResponseServerToolUseItem or LlmTestServerResponseServerToolResultItem;
    }

    internal static bool IsNativeCall(LlmTestServerResponseItem item)
    {
        return IsNative(item) && item is not LlmTestServerResponseServerToolResultItem;
    }

    internal static LlmTestServerNativeToolContract? Contract(LlmTestServerResponseItem item)
    {
        return item switch
        {
            LlmTestServerResponseWebSearchItem web => web.Contract,
            LlmTestServerResponseFileSearchItem file => file.Contract,
            LlmTestServerResponseCodeInterpreterItem code => code.Contract,
            LlmTestServerResponseServerToolUseItem call => call.Contract,
            LlmTestServerResponseServerToolResultItem result => result.Contract,
            _ => null,
        };
    }

    private static LlmTestServerResponseCitation SnapshotCitation(LlmTestServerResponseCitation citation, string content)
    {
        ArgumentNullException.ThrowIfNull(citation);
        if (citation is LlmTestServerAnthropicWebSearchCitation anthropic) { return SnapshotAnthropicCitation(anthropic); }
        if (content.Any(character => character > 127)) { throw new ArgumentException("Citation text must be ASCII.", nameof(citation)); }
        switch (citation)
        {
            case LlmTestServerUrlCitation url:
                ArgumentNullException.ThrowIfNull(url.Title);
                ValidateUrl(url.Url);
                Range(url.StartIndex, url.EndIndex, content.Length, false);
                return url with { };
            case LlmTestServerFileCitation file:
                Identifier(file.FileId);
                ArgumentException.ThrowIfNullOrWhiteSpace(file.Filename);
                if (file.Index < 0 || file.Index > content.Length) { throw new ArgumentException("File citation index exceeds output text.", nameof(citation)); }
                return file with { };
            case LlmTestServerContainerFileCitation container:
                Identifier(container.ContainerId);
                Identifier(container.FileId);
                ArgumentException.ThrowIfNullOrWhiteSpace(container.Filename);
                Range(container.StartIndex, container.EndIndex, content.Length, true);
                return container with { };
            default:
                throw new ArgumentException("Unknown citation kind.", nameof(citation));
        }
    }

    private static void Range(int start, int end, int length, bool allowEmpty)
    {
        if (start < 0 || end < start || !allowEmpty && end == start || end > length)
        { throw new ArgumentException("Citation range exceeds output text."); }
    }

    private static LlmTestServerResponseFileSearchItem SnapshotFileSearch(LlmTestServerResponseFileSearchItem file, bool fixture)
    {
        Identifier(file.Id);
        if (file.Contract != LlmTestServerNativeToolContract.OpenAiResponsesFileSearch) { throw new ArgumentException("Invalid file search contract."); }
        Strings(file.VectorStoreIds, fixture ? 1 : 0, true);
        Strings(file.Queries, 1, false);
        ArgumentNullException.ThrowIfNull(file.Results);
        if (file.Results.Count > 128) { throw new ArgumentException("Too many file search results."); }
        var results = file.Results.Select(result =>
        {
            ArgumentNullException.ThrowIfNull(result);
            if (result.FileId is not null) { Identifier(result.FileId); }
            if (result.Filename is not null) { ArgumentException.ThrowIfNullOrWhiteSpace(result.Filename); }
            Score(result.Score);
            if (result.Attributes is { } attributes)
            {
                if (attributes.Count > 16) { throw new ArgumentException("At most 16 file attributes are supported."); }
                foreach (var pair in attributes)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 64) { throw new ArgumentException("Invalid file attribute key."); }
                    Scalar(pair.Value);
                    if (pair.Value.ValueKind == JsonValueKind.String && pair.Value.GetString()!.Length > 512)
                    { throw new ArgumentException("File attribute text exceeds 512 characters."); }
                }
            }
            return result with { Attributes = result.Attributes?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal) };
        }).ToList();
        return file with { VectorStoreIds = [.. file.VectorStoreIds], Queries = [.. file.Queries], Results = results };
    }

    private static LlmTestServerResponseCodeInterpreterItem SnapshotCode(LlmTestServerResponseCodeInterpreterItem code)
    {
        Identifier(code.Id);
        Identifier(code.ContainerId);
        if (code.Contract != LlmTestServerNativeToolContract.OpenAiResponsesCodeInterpreter) { throw new ArgumentException("Invalid code interpreter contract."); }
        Strings(code.InputFileIds, 0, true);
        Strings(code.GeneratedFileIds, 0, true);
        ArgumentNullException.ThrowIfNull(code.CodeChunks);
        if (code.CodeChunks.Count > 128) { throw new ArgumentException("Too many code chunks."); }
        foreach (var chunk in code.CodeChunks) { ArgumentNullException.ThrowIfNull(chunk); }
        if (code.CodeChunks.Count > 0 && (code.Code is null || string.Concat(code.CodeChunks) != code.Code))
        { throw new ArgumentException("Code chunks must concatenate to the configured code."); }
        if (code.Outputs?.Count > 128) { throw new ArgumentException("Too many code outputs."); }
        return code with
        {
            InputFileIds = [.. code.InputFileIds],
            GeneratedFileIds = [.. code.GeneratedFileIds],
            CodeChunks = [.. code.CodeChunks],
            Outputs = code.Outputs?.Select(SnapshotCodeOutput).ToList(),
        };
    }

    private static LlmTestServerCodeInterpreterOutput SnapshotCodeOutput(LlmTestServerCodeInterpreterOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        switch (output)
        {
            case LlmTestServerCodeInterpreterLogsOutput logs:
                ArgumentNullException.ThrowIfNull(logs.Logs);
                return logs with { };
            case LlmTestServerCodeInterpreterImageOutput image:
                ValidateUrl(image.Url);
                return image with { };
            default:
                throw new ArgumentException("Unknown code output kind.", nameof(output));
        }
    }

    private static LlmTestServerNativeToolDeclaration CloneDeclaration(LlmTestServerNativeToolDeclaration declaration)
    {
        var web = declaration.WebSearchOptions;
        var file = declaration.FileSearchOptions;
        var code = declaration.CodeInterpreterOptions;
        return declaration with
        {
            WebSearchOptions = web is null ? null : web with
            {
                AllowedDomains = web.AllowedDomains is null ? [] : [.. web.AllowedDomains],
                UserLocation = web.UserLocation is null ? null : web.UserLocation with { },
            },
            FileSearchOptions = file is null ? null : file with
            {
                VectorStoreIds = file.VectorStoreIds is null ? [] : [.. file.VectorStoreIds],
                Filters = file.Filters is { ValueKind: not JsonValueKind.Undefined } filter ? filter.Clone() : file.Filters,
            },
            CodeInterpreterOptions = code is null ? null : code with { FileIds = code.FileIds is null ? [] : [.. code.FileIds] },
            AnthropicWebSearchOptions = CloneAnthropicOptions(declaration.AnthropicWebSearchOptions),
        };
    }

    private static LlmTestServerNativeToolDeclaration SnapshotDeclaration(LlmTestServerNativeToolDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (!Enum.IsDefined(declaration.Contract)) { throw new ArgumentException("Unknown native tool contract."); }
        if (declaration.WebSearchOptions is not null && declaration.Contract != LlmTestServerNativeToolContract.OpenAiResponsesWebSearch
            || declaration.FileSearchOptions is not null && declaration.Contract != LlmTestServerNativeToolContract.OpenAiResponsesFileSearch
            || declaration.CodeInterpreterOptions is not null && declaration.Contract != LlmTestServerNativeToolContract.OpenAiResponsesCodeInterpreter
            || declaration.AnthropicWebSearchOptions is not null && declaration.Contract != LlmTestServerNativeToolContract.AnthropicMessagesWebSearch20250305)
        { throw new ArgumentException("Native declaration options must match their contract."); }
        var web = declaration.WebSearchOptions;
        if (web is not null)
        {
            ArgumentNullException.ThrowIfNull(web.AllowedDomains);
            if (web.AllowedDomains.Count > 100 || web.SearchContextSize is not (null or "low" or "medium" or "high") || web.UserLocation is { Type: not "approximate" })
            { throw new ArgumentException("Invalid web search options."); }
            foreach (var domain in web.AllowedDomains)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(domain);
                if (Uri.CheckHostName(domain) != UriHostNameType.Dns || domain.Contains(':') || domain.Contains('/')) { throw new ArgumentException("A plain DNS domain is required."); }
            }
            web = web with { AllowedDomains = [.. web.AllowedDomains], UserLocation = web.UserLocation is null ? null : web.UserLocation with { } };
        }
        var file = declaration.FileSearchOptions;
        if (file is not null)
        {
            Strings(file.VectorStoreIds, 0, true, unique: false);
            if (file.MaxNumResults is < 1 or > 50 || file.Ranker is not (null or "auto" or "default-2024-11-15")) { throw new ArgumentException("Invalid file search options."); }
            Score(file.ScoreThreshold);
            if (file.Filters is { ValueKind: not JsonValueKind.Null } filter) { ValidateFilter(filter); }
            file = file with { VectorStoreIds = [.. file.VectorStoreIds], Filters = file.Filters?.Clone() };
        }
        if (declaration.Contract == LlmTestServerNativeToolContract.OpenAiResponsesFileSearch && file is null) { throw new ArgumentException("File search options are required."); }
        var code = declaration.CodeInterpreterOptions;
        if (code is not null)
        {
            Strings(code.FileIds, 0, true, unique: false);
            if (code.AutomaticContainer)
            {
                if (code.ContainerId is not null) { throw new ArgumentException("Automatic containers cannot also specify a container ID."); }
            }
            else
            {
                Identifier(code.ContainerId!);
                if (code.FileIds.Count > 0) { throw new ArgumentException("Explicit containers cannot specify auto file IDs."); }
            }
            code = code with { FileIds = [.. code.FileIds] };
        }
        if (declaration.Contract == LlmTestServerNativeToolContract.OpenAiResponsesCodeInterpreter && code is null) { throw new ArgumentException("Code interpreter options are required."); }
        return declaration with
        {
            WebSearchOptions = web,
            FileSearchOptions = file,
            CodeInterpreterOptions = code,
            AnthropicWebSearchOptions = SnapshotAnthropicOptions(declaration.AnthropicWebSearchOptions)
        };
    }

    private static void Identifier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 128) { throw new ArgumentException("Fixture identifiers are bounded to 128 characters.", nameof(value)); }
    }

    private static void Strings(List<string> values, int minimum, bool identifiers, bool unique = true)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < minimum || values.Count > 128) { throw new ArgumentException("Fixture collections must stay within their bounded count."); }
        foreach (var value in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (identifiers) { Identifier(value); }
        }
        if (identifiers && unique && values.Distinct(StringComparer.Ordinal).Count() != values.Count) { throw new ArgumentException("Resource identifiers must be distinct."); }
    }

    private static void Score(double? score)
    {
        if (score is { } value && (!double.IsFinite(value) || value is < 0 or > 1)) { throw new ArgumentException("Scores must be finite values between zero and one."); }
    }

    private static void Scalar(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.String or JsonValueKind.True or JsonValueKind.False) { return; }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)) { return; }
        throw new ArgumentException("A finite JSON scalar is required.");
    }
}
