using System.Text.Json;
using Json.Schema;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Runtime;

public sealed partial class LlmTestServerRuntime
{
    private static string? ValidateFixture(LlmTestServerChatRequest request, LlmTestServerScenarioResponse response)
    {
        if (response.Error is not null)
        {
            return null;
        }

        try
        {
            response = LlmTestServerResponseItems.Project(response);
            var nativeError = ValidateNativeFixture(request, response);
            if (nativeError is not null) { return nativeError; }
            var callCount = response.ToolCalls.Count + response.OutputItems.OfType<LlmTestServerResponseWebSearchItem>().Count();
            if (request.Tools is null || request.Tools.Any(tool => tool is null || string.IsNullOrWhiteSpace(tool.Name)))
            {
                return "Tool declarations require names.";
            }

            if (request.Tools.Select(tool => tool.Name).Distinct(StringComparer.Ordinal).Count() != request.Tools.Count)
            {
                return "Tool names must be unique.";
            }

            foreach (var tool in request.Tools)
            {
                _ = LlmTestServerJsonSchema.Matches("{}", tool.ParametersJson);
            }

            if (request.ResponseSchemaJson is { } declaredSchema)
            {
                _ = LlmTestServerJsonSchema.Matches("{}", declaredSchema);
            }

            if (!request.AllowParallelToolCalls && callCount > 1)
            {
                return "The selected fixture contains parallel tool calls, which the request disabled.";
            }

            if (request.ToolChoice == LlmTestServerToolChoice.Required && callCount == 0)
            {
                return "The selected fixture must contain a tool call.";
            }

            if (request.ToolChoice == LlmTestServerToolChoice.None && callCount > 0)
            {
                return "The selected fixture contains a tool call but tool choice is none.";
            }

            foreach (var call in response.ToolCalls)
            {
                var definition = request.Tools.FirstOrDefault(tool => tool.Name == call.Name);
                if (definition is null || request.RequiredToolName is { } required && required != call.Name)
                {
                    return "The fixture calls a tool that was not selected or declared in the request.";
                }

                if (!LlmTestServerJsonSchema.Matches(call.ArgumentsJson, definition.ParametersJson))
                {
                    return "Fixture tool arguments do not match the declared parameter schema.";
                }
            }
            if (response.ToolCalls.Select(call => call.Id).Distinct(StringComparer.Ordinal).Count() != response.ToolCalls.Count)
            {
                return "Fixture tool call IDs must be unique.";
            }

            if (response.ToolCalls.Count == 0 && (request.RequireJson || request.ResponseSchemaJson is not null))
            {
                LlmTestServerJsonSchema.ValidateJson(response.Content);
                if (request.ResponseSchemaJson is { } schema && !LlmTestServerJsonSchema.Matches(response.Content, schema))
                {
                    return "The selected fixture does not match the requested JSON schema.";
                }

                if (response.StreamChunks.Count > 0 && string.Concat(response.StreamChunks) != response.Content)
                {
                    return "Structured-output stream chunks must concatenate to the validated JSON fixture.";
                }
            }
            return null;
        }
        catch (Exception exception) when (exception is JsonException or JsonSchemaException or ArgumentException)
        {
            return $"Invalid JSON or schema in request/fixture: {exception.Message}";
        }
    }
    private static string? ValidateNativeFixture(LlmTestServerChatRequest request, LlmTestServerScenarioResponse response)
    {
        ArgumentNullException.ThrowIfNull(request.NativeTools);
        ArgumentNullException.ThrowIfNull(request.NativeIncludes);
        _ = LlmTestServerResponseItems.SnapshotDeclarations(request.NativeTools);
        foreach (var message in request.Messages)
        {
            ArgumentNullException.ThrowIfNull(message);
            _ = LlmTestServerResponseItems.Snapshot(message.OutputItems);
        }
        var calls = response.OutputItems.OfType<LlmTestServerResponseWebSearchItem>().ToList();
        var citations = response.OutputItems.OfType<LlmTestServerResponseMessageItem>().Any(message => message.Citations.Count > 0);
        var nativeHistory = request.Messages.Any(message => message.OutputItems.OfType<LlmTestServerResponseWebSearchItem>().Any()
            || message.OutputItems.OfType<LlmTestServerResponseMessageItem>().Any(item => item.Citations.Count > 0));
        if (calls.Count == 0 && !citations && !nativeHistory && request.NativeTools.Count == 0 && request.RequiredNativeTool is null) { return null; }
        if (request.NativeToolContract != LlmTestServerNativeToolContract.OpenAiResponsesWebSearch
            || !string.Equals(request.ModelId, Models.LlmTestServerKnownModelIds.Gpt41Mini, StringComparison.Ordinal))
        { return "Native web search fixtures require the qualified OpenAI Responses surface and gpt-4.1-mini."; }
        if (request.NativeTools.Any(tool => tool is null || tool.Contract != LlmTestServerNativeToolContract.OpenAiResponsesWebSearch)
            || request.NativeTools.Select(tool => tool.Contract).Distinct().Count() != request.NativeTools.Count
            || request.NativeIncludes.Any(include => include != LlmTestServerNativeToolInclude.WebSearchSources))
        { return "Native tool declarations and includes must use supported, distinct contracts."; }
        if (calls.Any(call => !request.NativeTools.Any(tool => tool.Contract == call.Contract)))
        { return "The fixture calls a native tool that was not declared."; }
        if (request.RequiredToolName is not null && calls.Count > 0)
        { return "A named function choice cannot select a native tool."; }
        if (request.RequiredNativeTool is { } selected
            && (calls.Count == 0 || response.ToolCalls.Count > 0 || calls.Any(call => call.Contract != selected)))
        { return "The selected native tool must match every fixture call."; }
        if (citations && !request.NativeTools.Any(tool => tool.Contract == LlmTestServerNativeToolContract.OpenAiResponsesWebSearch))
        { return "URL citations require a declared native web search tool."; }
        var options = request.NativeTools.FirstOrDefault(tool => tool.Contract == LlmTestServerNativeToolContract.OpenAiResponsesWebSearch)?.WebSearchOptions;
        var urls = calls.SelectMany(call => call.Sources.Select(source => source.Url))
            .Concat(response.OutputItems.OfType<LlmTestServerResponseMessageItem>().SelectMany(message => message.Citations.Select(citation => citation.Url)));
        if (urls.Any(url => !LlmTestServerResponseItems.AllowsUrl(options, url)))
        { return "Fixture sources and citations must respect the declared allowed domains."; }
        return null;
    }

}
