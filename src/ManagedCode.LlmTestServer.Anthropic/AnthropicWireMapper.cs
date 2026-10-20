using System.Text.Json;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Anthropic;

public static class AnthropicWireMapper
{
    public const string VersionHeaderName = "anthropic-version";
    public const string SupportedVersion = "2023-06-01";

    public static LlmTestServerChatRequest ToRuntimeRequest(AnthropicMessagesRequest request)
    {
        var messages = new List<LlmTestServerMessage>();
        var system = AnthropicContentReader.ReadTextContent(request.System);
        if (!string.IsNullOrWhiteSpace(system))
        {
            messages.Add(new LlmTestServerMessage { Role = "system", Content = system });
        }

        messages.AddRange(request.Messages.SelectMany(ReadMessages));

        return new()
        {
            Tools = request.Tools.Select(tool => new LlmTestServerToolDefinition { Name = tool.Name, Description = tool.Description, ParametersJson = tool.InputSchema.GetRawText() }).ToList(),
            ToolChoice = request.ToolChoice?.Type switch { "any" or "tool" => LlmTestServerToolChoice.Required, "none" => LlmTestServerToolChoice.None, _ => LlmTestServerToolChoice.Auto },
            RequiredToolName = request.ToolChoice?.Name,
            AllowParallelToolCalls = request.ToolChoice?.DisableParallelToolUse != true,
            RequireJson = request.OutputConfig?.Format is not null,
            ResponseSchemaJson = request.OutputConfig?.Format?.Schema.GetRawText(),
            ModelId = request.Model,
            Stream = request.Stream,
            PopulateCacheOnly = request.MaxTokens == 0,
            PromptCachePolicy = HasPromptCacheControl(request)
                ? LlmTestServerPromptCachePolicy.Anthropic
                : LlmTestServerPromptCachePolicy.None,
            Messages = messages,
        };
    }

    public static AnthropicMessageResponse ToMessageResponse(LlmTestServerChatResult result, bool cacheOnly = false)
    {
        return new()
        {
            Id = CreateMessageId(),
            Model = result.ModelId,
            Content = cacheOnly ? [] : result.ToolCalls.Count > 0 ? (result.Content.Length > 0 ? new[] { new AnthropicContentBlock { Text = result.Content } } : []).Concat(result.ToolCalls.Select(ToToolBlock)).ToList() : [new AnthropicContentBlock { Text = result.Content }],
            StopReason = cacheOnly ? "max_tokens" : result.ToolCalls.Count > 0 ? "tool_use" : "end_turn",
            Usage = CreateUsage(result.Usage),
        };
    }

    public static AnthropicErrorResponse ToError(string type, string message)
    {
        return new()
        {
            Error = new AnthropicError
            {
                Type = type,
                Message = message,
            },
            RequestId = CreateRequestId(),
        };
    }

    public static object ToMessageStartEvent(LlmTestServerChatResult result)
    {
        return new
        {
            type = "message_start",
            message = new AnthropicMessageResponse
            {
                Id = CreateMessageId(),
                Model = result.ModelId,
                StopReason = null,
                Content = [],
                Usage = CreateUsage(
                    result.Usage,
                    result.Usage.OutputTokens > 0 ? 1 : 0
                ),
            },
        };
    }

    public static object ToContentBlockStartEvent()
    {
        return new
        {
            type = "content_block_start",
            index = 0,
            content_block = new AnthropicContentBlock(),
        };
    }

    public static object ToContentBlockDeltaEvent(string text)
    {
        return new
        {
            type = "content_block_delta",
            index = 0,
            delta = new
            {
                type = "text_delta",
                text,
            },
        };
    }

    public static object ToContentBlockStopEvent()
    {
        return new
        {
            type = "content_block_stop",
            index = 0,
        };
    }

    public static object ToMessageDeltaEvent(LlmTestServerChatResult result, bool cacheOnly = false)
    {
        return new
        {
            type = "message_delta",
            delta = new
            {
                stop_reason = cacheOnly ? "max_tokens" : result.ToolCalls.Count > 0 ? "tool_use" : "end_turn",
                stop_sequence = (string?)null,
            },
            usage = new { output_tokens = result.Usage.OutputTokens },
        };
    }

    public static object ToMessageStopEvent()
    {
        return new
        {
            type = "message_stop",
        };
    }

    public static AnthropicContentBlock ToToolBlock(LlmTestServerToolCall call)
    {
        return new()
        { Type = "tool_use", Id = call.Id, Name = call.Name, Input = JsonSerializer.Deserialize<JsonElement>(call.ArgumentsJson), Text = null };
    }

    public static IEnumerable<object> ToToolStreamEvents(LlmTestServerChatResult result, int offset = 0)
    {
        for (var position = 0; position < result.ToolCalls.Count; position++)
        {
            var index = position + offset;
            var call = result.ToolCalls[position];
            yield return new { type = "content_block_start", index, content_block = ToToolBlock(call) with { Input = JsonSerializer.SerializeToElement(new { }) } };
            yield return new { type = "content_block_delta", index, delta = new { type = "input_json_delta", partial_json = call.ArgumentsJson } };
            yield return new { type = "content_block_stop", index };
        }
    }

    private static IEnumerable<LlmTestServerMessage> ReadMessages(AnthropicInputMessage message)
    {
        if (message.Content.ValueKind != JsonValueKind.Array)
        { yield return new() { Role = message.Role, Content = message.TextContent }; yield break; }
        var blocks = message.Content.EnumerateArray().ToArray();
        var calls = blocks.Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "tool_use")
            .Select(block => new LlmTestServerToolCall { Id = block.GetProperty("id").GetString()!, Name = block.GetProperty("name").GetString()!, ArgumentsJson = block.GetProperty("input").GetRawText() }).ToList();
        var text = string.Concat(blocks.Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "text").Select(block => block.GetProperty("text").GetString()));
        if (calls.Count > 0 || text.Length > 0)
        {
            yield return new() { Role = message.Role, Content = text, ToolCalls = calls };
        }

        foreach (var block in blocks.Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "tool_result"))
        {
            yield return new() { Role = "tool", ToolCallId = block.GetProperty("tool_use_id").GetString(), Content = AnthropicContentReader.ReadTextContent(block.GetProperty("content")) };
        }
    }

    private static string CreateMessageId()
    {
        return $"msg_llmtestserver_{Guid.NewGuid():N}";
    }

    private static AnthropicUsage CreateUsage(
        LlmTestServerTokenUsage usage,
        int? outputTokens = null
    )
    {
        var hasCacheBreakdown = usage.CachedInputTokens > 0
            || usage.CacheCreationInputTokens > 0;
        return new()
        {
            InputTokens = hasCacheBreakdown
                ? Math.Max(
                    0,
                    usage.InputTokens
                        - usage.CachedInputTokens
                        - usage.CacheCreationInputTokens
                )
                : usage.InputTokens,
            OutputTokens = outputTokens ?? usage.OutputTokens,
            CacheCreationInputTokens = hasCacheBreakdown
                ? usage.CacheCreationInputTokens
                : null,
            CacheReadInputTokens = hasCacheBreakdown ? usage.CachedInputTokens : null,
        };
    }

    private static bool HasPromptCacheControl(AnthropicMessagesRequest request)
    {
        return HasCacheControl(request.CacheControl)
            || AnthropicContentReader.HasCacheControl(request.System)
            || request.Messages.Any(message =>
                AnthropicContentReader.HasCacheControl(message.Content)
            );
    }

    private static bool HasCacheControl(JsonElement? cacheControl)
    {
        return cacheControl is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null };
    }

    private static string CreateRequestId()
    {
        return $"req_llmtestserver_{Guid.NewGuid():N}";
    }

}
