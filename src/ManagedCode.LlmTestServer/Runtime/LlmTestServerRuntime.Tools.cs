using System.Text.Json;
using Json.Schema;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Runtime;

public sealed partial class LlmTestServerRuntime
{
    private static string? ValidateFixture(LlmTestServerChatRequest request, LlmTestServerScenarioResponse response)
    {
        if (response.StreamDelivery is not null && (!request.Stream
            || request.StreamDeliverySurface is not (LlmTestServerStreamDeliverySurface.OpenAiChatCompletions
                or LlmTestServerStreamDeliverySurface.OpenAiResponses or LlmTestServerStreamDeliverySurface.AnthropicMessages
                or LlmTestServerStreamDeliverySurface.GeminiGenerateContent)
            || request.Tools is not { Count: 0 } || request.NativeTools is not { Count: 0 }
            || request.NativeIncludes is not { Count: 0 } || request.RequiredNativeTool is not null
            || request.RequireJson || request.ResponseSchemaJson is not null
            || request.Messages is null || request.Messages.Any(message => message is null || message.Role == "tool"
                || message.ToolCalls is not { Count: 0 } || message.OutputItems is not { Count: 0 })))
        { return "This stream delivery fixture requires a qualified OpenAI, Anthropic or Gemini text SSE route without tools or structured output."; }
        if (response.Error is not null)
        {
            return null;
        }

        try
        {
            response = LlmTestServerResponseItems.Project(response);
            _ = LlmTestServerOutputDisposition.Snapshot(response);
            foreach (var message in request.Messages)
            {
                if (message.OutputDisposition is not { } historyDisposition) { continue; }
                LlmTestServerOutputDisposition.Validate(historyDisposition);
                if (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)) { return "Output disposition history belongs to assistant messages."; }
            }
            if (response.OutputDisposition is { } disposition)
            {
                var supported = request.OutputDispositionSurface switch
                {
                    LlmTestServerOutputDispositionSurface.OpenAiChatCompletions or LlmTestServerOutputDispositionSurface.OpenAiResponses
                        or LlmTestServerOutputDispositionSurface.AzureOpenAiDeploymentChatCompletions
                        or LlmTestServerOutputDispositionSurface.AzureOpenAiV1ChatCompletions or LlmTestServerOutputDispositionSurface.AzureOpenAiV1Responses
                        or LlmTestServerOutputDispositionSurface.MicrosoftFoundryV1ChatCompletions or LlmTestServerOutputDispositionSurface.MicrosoftFoundryV1Responses
                        => disposition.Kind is LlmTestServerOutputDispositionKind.Filtered or LlmTestServerOutputDispositionKind.Refused,
                    LlmTestServerOutputDispositionSurface.AnthropicMessages => disposition.Kind == LlmTestServerOutputDispositionKind.Refused,
                    LlmTestServerOutputDispositionSurface.GeminiGenerateContent => disposition.Kind == LlmTestServerOutputDispositionKind.Filtered,
                    LlmTestServerOutputDispositionSurface.DeepSeekChatCompletions
                        or LlmTestServerOutputDispositionSurface.MicrosoftFoundryChatCompletions
                        or LlmTestServerOutputDispositionSurface.MicrosoftFoundryModelsChatCompletions
                        => disposition.Kind == LlmTestServerOutputDispositionKind.Filtered,
                    _ => false,
                };
                if (!supported || request.Tools is not { Count: 0 } || request.NativeTools is not { Count: 0 }
                    || request.NativeIncludes is not { Count: 0 } || request.RequiredNativeTool is not null
                    || request.RequireJson || request.ResponseSchemaJson is not null
                    || request.Messages.Any(message => message.Role == "tool" || message.ToolCalls is not { Count: 0 } || message.OutputItems is not { Count: 0 }))
                { return "This output disposition requires a qualified provider text route and supported policy kind without tools, native items or structured output."; }
                if (request.OutputDispositionSurface == LlmTestServerOutputDispositionSurface.AnthropicMessages
                    && disposition.Category is not (null or "cyber" or "bio" or "frontier_llm" or "reasoning_extraction" or "general_harms"))
                { return "The configured category is not a supported Anthropic refusal category."; }
            }
            var nativeError = ValidateNativeFixture(request, response);
            if (nativeError is not null) { return nativeError; }
            if (request.RequireToolContinuationTokens && response.ToolCalls.Count > 0
                && !ValidToolContinuationToken(response.ToolCalls[0].ContinuationToken))
            { return "The selected fixture requires valid continuation metadata on its first function call."; }
            if (response.ToolCalls.Any(call => call.ContinuationToken is not null && !ValidToolContinuationToken(call.ContinuationToken)))
            { return "Fixture tool continuation metadata must be bounded, nonempty base64."; }
            var callCount = response.ToolCalls.Count + response.OutputItems.Count(LlmTestServerResponseItems.IsNativeCall);
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
    // A repository retention bound, not a provider-advertised signature limit.
    private static bool ValidToolContinuationToken(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 16384 || value.Any(char.IsWhiteSpace)) { return false; }
        return Convert.TryFromBase64String(value, new byte[value.Length], out var written) && written > 0;
    }

    private static string? ValidateToolContinuationHistory(LlmTestServerChatRequest request, LlmTestServerChatResult priorResult)
    {
        if (!request.RequireToolContinuationTokens) { return null; }
        var start = request.Messages.FindLastIndex(message => message.Role == "user");
        var currentCalls = request.Messages.Skip(start + 1).SelectMany(message => message.ToolCalls).ToList();
        foreach (var prior in priorResult.ToolCalls.Where(call => call.ContinuationToken is not null))
        {
            var echoed = currentCalls.LastOrDefault(call => call.Id == prior.Id);
            if (echoed is not null && !string.Equals(echoed.ContinuationToken, prior.ContinuationToken, StringComparison.Ordinal))
            { return "Tool continuation metadata differs from the last response retained in this conversation."; }
        }
        return null;
    }

}
