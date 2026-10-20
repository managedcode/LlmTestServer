using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Runtime;

public sealed partial class LlmTestServerRuntime
{
    private static string? ValidateAnthropicNativeFixture(LlmTestServerChatRequest request, LlmTestServerScenarioResponse response,
        IReadOnlyList<LlmTestServerNativeToolDeclaration> declarations, IReadOnlyList<LlmTestServerResponseItem> calls,
        IReadOnlyList<LlmTestServerResponseCitation> citations)
    {
        const LlmTestServerNativeToolContract contract = LlmTestServerNativeToolContract.AnthropicMessagesWebSearch20250305;
        if (declarations.Count > 1 || declarations.Any(declaration => declaration.Contract != contract)
            || request.NativeIncludes.Count > 0)
        { return "The Anthropic Messages surface requires its supported native web search declaration and no Responses includes."; }
        if (declarations.Count > 0 && !string.Equals(request.ModelId, LlmTestServerKnownModelIds.ClaudeSonnet55, StringComparison.Ordinal))
        { return "Native Anthropic web search is unsupported for this fixture model."; }
        var allItems = response.OutputItems.Concat(request.Messages.SelectMany(message => message.OutputItems)).ToList();
        if (allItems.Any(item => LlmTestServerResponseItems.IsNative(item) && LlmTestServerResponseItems.Contract(item) != contract)
            || allItems.OfType<LlmTestServerResponseMessageItem>().SelectMany(message => message.Citations)
                .Any(citation => citation is not LlmTestServerAnthropicWebSearchCitation))
        { return "Native outputs, history and citations must match the Anthropic Messages surface."; }
        if (calls.Count > 0 && declarations.Count == 0)
        { return "The fixture calls a native tool that was not declared."; }
        if (request.RequiredToolName is not null && calls.Count > 0)
        { return "A named function choice cannot select a native tool."; }
        if (request.RequiredNativeTool is { } required
            && (required != contract || calls.Count == 0 || response.ToolCalls.Count > 0))
        { return "The selected native tool must match every fixture call."; }
        var options = declarations.Count > 0 ? declarations[0].AnthropicWebSearchOptions : null;
        var attempts = 0;
        var sources = new List<LlmTestServerAnthropicWebSearchResult>();
        var results = response.OutputItems.OfType<LlmTestServerResponseServerToolResultItem>()
            .ToDictionary(result => result.ToolUseId, result => result.Result, StringComparer.Ordinal);
        foreach (var call in calls.OfType<LlmTestServerResponseServerToolUseItem>())
        {
            var result = results[call.Id];
            if (result is LlmTestServerWebSearchErrorResult { ErrorCode: "max_uses_exceeded" })
            {
                if (options?.MaxUses is not { } limit || attempts < limit)
                { return "A max_uses_exceeded fixture requires an exhausted explicit search budget."; }
                continue;
            }
            if (options?.MaxUses is { } maximum && attempts >= maximum)
            { return "The fixture exceeds the declared native search budget."; }
            attempts++;
            if (result is LlmTestServerWebSearchResults web) { sources.AddRange(web.Results); }
        }
        if (sources.Any(source => !LlmTestServerResponseItems.AllowsAnthropicUrl(options, source.Url)))
        { return "Native web search results must respect the declared domain filters."; }
        foreach (var citation in citations)
        {
            if (citation is not LlmTestServerAnthropicWebSearchCitation web || declarations.Count == 0
                || !sources.Any(source => string.Equals(source.Url, web.Url, StringComparison.Ordinal))
                || !LlmTestServerResponseItems.AllowsAnthropicUrl(options, web.Url))
            { return "Anthropic web citations must reference a configured result under the declared domain filters."; }
        }
        return null;
    }
}
