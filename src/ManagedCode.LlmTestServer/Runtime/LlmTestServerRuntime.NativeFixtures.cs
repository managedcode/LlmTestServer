using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Runtime;

public sealed partial class LlmTestServerRuntime
{
    private static string? ValidateNativeFixture(LlmTestServerChatRequest request, LlmTestServerScenarioResponse response)
    {
        ArgumentNullException.ThrowIfNull(request.NativeTools);
        ArgumentNullException.ThrowIfNull(request.NativeIncludes);
        var declarations = LlmTestServerResponseItems.SnapshotDeclarations(request.NativeTools, validate: true);
        foreach (var message in request.Messages)
        {
            ArgumentNullException.ThrowIfNull(message);
            _ = LlmTestServerResponseItems.Snapshot(message.OutputItems);
        }
        var calls = response.OutputItems.Where(LlmTestServerResponseItems.IsNative).ToList();
        var citations = response.OutputItems.OfType<LlmTestServerResponseMessageItem>().SelectMany(message => message.Citations).ToList();
        var nativeHistory = request.Messages.Any(message => message.OutputItems.Any(LlmTestServerResponseItems.IsNative)
            || message.OutputItems.OfType<LlmTestServerResponseMessageItem>().Any(item => item.Citations.Count > 0));
        if (calls.Count == 0 && citations.Count == 0 && !nativeHistory && declarations.Count == 0 && request.RequiredNativeTool is null) { return null; }
        if (request.NativeToolSurface != LlmTestServerNativeToolSurface.OpenAiResponses) { return "Native fixtures require the qualified OpenAI Responses surface."; }
        if (declarations.Select(tool => tool.Contract).Distinct().Count() != declarations.Count
            || request.NativeIncludes.Any(include => !Enum.IsDefined(include))) { return "Native declarations and includes must use supported contracts."; }
        foreach (var declaration in declarations)
        {
            var model = declaration.Contract switch
            {
                LlmTestServerNativeToolContract.OpenAiResponsesWebSearch => LlmTestServerKnownModelIds.Gpt41Mini,
                LlmTestServerNativeToolContract.OpenAiResponsesFileSearch => LlmTestServerKnownModelIds.Gpt4OMini,
                LlmTestServerNativeToolContract.OpenAiResponsesCodeInterpreter => LlmTestServerKnownModelIds.Gpt41,
                _ => null,
            };
            if (!string.Equals(request.ModelId, model, StringComparison.Ordinal)) { return "Native tool declaration is unsupported for this fixture model."; }
        }
        if (calls.Any(call => !declarations.Any(tool => tool.Contract == LlmTestServerResponseItems.Contract(call))))
        { return "The fixture calls a native tool that was not declared."; }
        if (request.RequiredToolName is not null && calls.Count > 0) { return "A named function choice cannot select a native tool."; }
        if (request.RequiredNativeTool is { } selected
            && (calls.Count == 0 || response.ToolCalls.Count > 0 || calls.Any(call => LlmTestServerResponseItems.Contract(call) != selected)))
        { return "The selected native tool must match every fixture call."; }
        var webOptions = declarations.FirstOrDefault(tool => tool.Contract == LlmTestServerNativeToolContract.OpenAiResponsesWebSearch)?.WebSearchOptions;
        if (calls.OfType<LlmTestServerResponseWebSearchItem>().SelectMany(call => call.Sources).Any(source => !LlmTestServerResponseItems.AllowsUrl(webOptions, source.Url)))
        { return "Fixture sources must respect the declared allowed domains."; }
        foreach (var file in calls.OfType<LlmTestServerResponseFileSearchItem>())
        {
            var options = declarations.First(tool => tool.Contract == file.Contract).FileSearchOptions!;
            if (file.VectorStoreIds.Count == 0 || file.VectorStoreIds.Any(store => !options.VectorStoreIds.Contains(store, StringComparer.Ordinal)))
            { return "File search fixture stores must be declared by the request."; }
            if (options.MaxNumResults is { } maximum && file.Results.Count > maximum
                || file.Results.Any(result => options.ScoreThreshold is { } threshold && (result.Score is null || result.Score < threshold)
                    || !LlmTestServerResponseItems.MatchesFilter(options.Filters, result.Attributes)))
            { return "File search results must satisfy the requested count, score and attribute filter."; }
        }
        foreach (var code in calls.OfType<LlmTestServerResponseCodeInterpreterItem>())
        {
            var options = declarations.First(tool => tool.Contract == code.Contract).CodeInterpreterOptions!;
            if (options.AutomaticContainer
                ? !code.InputFileIds.Order(StringComparer.Ordinal).SequenceEqual(options.FileIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal)
                : !string.Equals(code.ContainerId, options.ContainerId, StringComparison.Ordinal))
            { return "Code fixture container or input file references do not match the request."; }
        }
        return ValidateNativeCitations(citations, calls, declarations, webOptions);
    }

    private static string? ValidateNativeCitations(IReadOnlyList<LlmTestServerResponseCitation> citations,
        IReadOnlyList<LlmTestServerResponseItem> calls, IReadOnlyList<LlmTestServerNativeToolDeclaration> declarations, LlmTestServerWebSearchOptions? webOptions)
    {
        foreach (var citation in citations)
        {
            switch (citation)
            {
                case LlmTestServerUrlCitation url:
                    if (!declarations.Any(tool => tool.Contract == LlmTestServerNativeToolContract.OpenAiResponsesWebSearch)
                        || !LlmTestServerResponseItems.AllowsUrl(webOptions, url.Url)) { return "URL citations require the declared web search domain contract."; }
                    break;
                case LlmTestServerFileCitation file:
                    if (!declarations.Any(tool => tool.Contract == LlmTestServerNativeToolContract.OpenAiResponsesFileSearch)
                        || !calls.OfType<LlmTestServerResponseFileSearchItem>().SelectMany(call => call.Results).Any(result => result.FileId == file.FileId
                            && (result.Filename is null || result.Filename == file.Filename))) { return "File citations must reference a configured search result."; }
                    break;
                case LlmTestServerContainerFileCitation container:
                    if (!declarations.Any(tool => tool.Contract == LlmTestServerNativeToolContract.OpenAiResponsesCodeInterpreter)
                        || !calls.OfType<LlmTestServerResponseCodeInterpreterItem>().Any(call => call.ContainerId == container.ContainerId
                            && (call.InputFileIds.Contains(container.FileId, StringComparer.Ordinal) || call.GeneratedFileIds.Contains(container.FileId, StringComparer.Ordinal))))
                    { return "Container citations must reference configured input or generated files in that container."; }
                    break;
            }
        }
        return null;
    }
}
