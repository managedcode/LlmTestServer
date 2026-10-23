using System.Text;
using System.Text.Json;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Bedrock;

public static class BedrockWireMapper
{
    public static LlmTestServerChatRequest ToRuntimeRequest(
        string modelId,
        BedrockConverseRequest request,
        bool stream
    )
    {
        var messages = request
            .Messages
            .SelectMany(ReadMessages)
            .ToList();

        var system = ReadText(request.System);
        if (!string.IsNullOrWhiteSpace(system))
        {
            messages.Insert(0, new LlmTestServerMessage { Role = "system", Content = system });
        }

        return new()
        {
            Tools = request.ToolConfig?.Tools.Select(tool => new LlmTestServerToolDefinition { Name = tool.ToolSpec.Name, Description = tool.ToolSpec.Description, ParametersJson = tool.ToolSpec.InputSchema.Json.GetRawText() }).ToList() ?? [],
            ToolChoice = request.ToolConfig?.ToolChoice is { } choice && (choice.Any is not null || choice.Tool is not null) ? LlmTestServerToolChoice.Required : LlmTestServerToolChoice.Auto,
            RequiredToolName = request.ToolConfig?.ToolChoice?.Tool?.Name,
            RequireJson = request.OutputConfig?.TextFormat is not null,
            ResponseSchemaJson = request.OutputConfig?.TextFormat?.Structure?.JsonSchema?.Schema,
            ModelId = modelId,
            Stream = stream,
            PromptCachePolicy = HasPromptCacheMarker(request)
                ? LlmTestServerPromptCachePolicy.Bedrock
                : LlmTestServerPromptCachePolicy.None,
            Messages = messages,
        };
    }

    public static LlmTestServerChatRequest ToRuntimeRequest(
        string modelId,
        JsonElement request,
        bool stream = false
    )
    {
        return new()
        {
            ModelId = modelId,
            Stream = stream,
            PromptCachePolicy = HasPromptCacheMarker(request)
                ? LlmTestServerPromptCachePolicy.Bedrock
                : LlmTestServerPromptCachePolicy.None,
            Messages =
            [
                new LlmTestServerMessage
                {
                    Role = "user",
                    Content = ReadInvokeInput(request),
                },
            ],
        };
    }

    public static BedrockConverseResponse ToConverseResponse(LlmTestServerChatResult result)
    {
        return new()
        {
            StopReason = result.ToolCalls.Count > 0 ? "tool_use" : "end_turn",
            Output = new BedrockConverseOutput
            {
                Message = new BedrockMessage
                {
                    Role = "assistant",
                    Content = result.ToolCalls.Count > 0 ? (result.Content.Length > 0 ? new[] { new BedrockContentBlock { Text = result.Content } } : []).Concat(result.ToolCalls.Select(call => new BedrockContentBlock { ToolUse = new() { ToolUseId = call.Id, Name = call.Name, Input = JsonSerializer.Deserialize<JsonElement>(call.ArgumentsJson) } })).ToList() : [new BedrockContentBlock { Text = result.Content }],
                },
            },
            Usage = new BedrockUsage
            {
                InputTokens = result.Usage.InputTokens,
                OutputTokens = result.Usage.OutputTokens,
                TotalTokens = result.Usage.TotalTokens,
                CacheReadInputTokens = result.Usage.CachedInputTokens,
                CacheWriteInputTokens = result.Usage.CacheCreationInputTokens,
            },
        };
    }

    private static IEnumerable<LlmTestServerMessage> ReadMessages(BedrockMessage message)
    {
        var calls = message.Content.Where(block => block.ToolUse is not null).Select(block => new LlmTestServerToolCall { Id = block.ToolUse!.ToolUseId, Name = block.ToolUse.Name, ArgumentsJson = block.ToolUse.Input.GetRawText() }).ToList();
        if (calls.Count > 0 || message.Content.Any(block => block.ToolResult is null))
        {
            yield return new() { Role = message.Role, Content = ReadText(message.Content), ToolCalls = calls };
        }

        foreach (var block in message.Content.Where(block => block.ToolResult is not null))
        {
            yield return new() { Role = "tool", ToolCallId = block.ToolResult!.ToolUseId, Content = string.Concat(block.ToolResult.Content.Select(value => value.TryGetProperty("text", out var text) ? text.GetString() : value.GetRawText())) };
        }
    }
    public static IEnumerable<JsonElement> ToToolStreamEvents(LlmTestServerChatResult result, int offset = 0)
    {
        for (var position = 0; position < result.ToolCalls.Count; position++)
        {
            var contentBlockIndex = position + offset;
            var call = result.ToolCalls[position];
            yield return JsonSerializer.SerializeToElement(new { contentBlockStart = new { contentBlockIndex, start = new { toolUse = new { toolUseId = call.Id, name = call.Name } } } });
            yield return JsonSerializer.SerializeToElement(new { contentBlockDelta = new { contentBlockIndex, delta = new { toolUse = new { input = call.ArgumentsJson } } } });
            yield return JsonSerializer.SerializeToElement(new { contentBlockStop = new { contentBlockIndex } });
        }
    }

    public static BedrockTitanTextResponse ToTitanTextResponse(
        LlmTestServerChatResult result,
        string inputText
    )
    {
        return new()
        {
            InputTextTokenCount = LlmTestServerTokenCounter.CountTextTokens(inputText),
            Results =
            [
                new BedrockTitanTextResult
                {
                    TokenCount = result.Usage.OutputTokens,
                    OutputText = result.Content,
                },
            ],
        };
    }

    public static BedrockTitanEmbeddingResponse ToTitanEmbeddingResponse(
        IReadOnlyList<float> vector,
        string inputText
    )
    {
        return new()
        {
            Embedding = vector,
            InputTextTokenCount = LlmTestServerTokenCounter.CountTextTokens(inputText),
            EmbeddingsByType = new Dictionary<string, IReadOnlyList<float>>(StringComparer.Ordinal)
            {
                ["float"] = vector,
            },
        };
    }

    public static BedrockImageResponse ToImageResponse(string dataUri)
    {
        return new()
        {
            Images = [StripDataUriPrefix(dataUri)],
            FinishReasons = [null],
        };
    }

    public static JsonElement ToConverseMessageStartEvent()
    {
        return JsonSerializer.SerializeToElement(new { messageStart = new { role = "assistant" } });
    }

    public static JsonElement ToConverseContentBlockStartEvent()
    {
        return JsonSerializer.SerializeToElement(new
        {
            contentBlockStart = new { contentBlockIndex = 0, start = new { } },
        });
    }

    public static JsonElement ToConverseContentBlockDeltaEvent(string text)
    {
        return JsonSerializer.SerializeToElement(new
        {
            contentBlockDelta = new
            {
                contentBlockIndex = 0,
                delta = new { text },
            },
        });
    }

    public static JsonElement ToConverseContentBlockStopEvent()
    {
        return JsonSerializer.SerializeToElement(new { contentBlockStop = new { contentBlockIndex = 0 } });
    }

    public static JsonElement ToConverseMessageStopEvent(bool toolUse = false)
    {
        return JsonSerializer.SerializeToElement(new { messageStop = new { stopReason = toolUse ? "tool_use" : "end_turn" } });
    }

    public static JsonElement ToConverseMetadataEvent(LlmTestServerChatResult result)
    {
        return JsonSerializer.SerializeToElement(new
        {
            metadata = new
            {
                usage = new
                BedrockUsage
                {
                    InputTokens = result.Usage.InputTokens,
                    OutputTokens = result.Usage.OutputTokens,
                    TotalTokens = result.Usage.TotalTokens,
                    CacheReadInputTokens = result.Usage.CachedInputTokens,
                    CacheWriteInputTokens = result.Usage.CacheCreationInputTokens,
                },
                metrics = new { latencyMs = 0 },
            },
        });
    }

    public static JsonElement ToInvokeStreamChunk(string outputText)
    {
        var payload = JsonSerializer.Serialize(new { outputText });
        return JsonSerializer.SerializeToElement(new
        {
            chunk = new
            {
                bytes = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)),
            },
        });
    }

    public static BedrockErrorResponse ToError(string message)
    {
        return new() { Message = message };
    }

    public static string ReadInvokeInput(JsonElement request)
    {
        if (request.ValueKind == JsonValueKind.String)
        {
            return request.GetString() ?? string.Empty;
        }

        if (request.ValueKind != JsonValueKind.Object)
        {
            return request.ToString();
        }

        foreach (var propertyName in new[] { "inputText", "prompt", "text" })
        {
            if (request.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? string.Empty;
            }
        }

        if (request.TryGetProperty("messages", out var messages)
            && messages.ValueKind == JsonValueKind.Array)
        {
            return string.Concat(messages.EnumerateArray().Select(ReadMessageText));
        }

        return request.ToString();
    }

    public static string ReadText(IReadOnlyList<BedrockContentBlock> content)
    {
        return string.Concat(content.Select(block => block.Text).Where(text => text is not null));
    }

    private static bool HasPromptCacheMarker(BedrockConverseRequest request)
    {
        return request.System.Any(HasPromptCacheMarker)
            || request.Messages.SelectMany(message => message.Content).Any(HasPromptCacheMarker);
    }

    private static bool HasPromptCacheMarker(BedrockContentBlock block)
    {
        return block.CachePoint is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null }
            || block.CacheControl is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null };
    }

    private static bool HasPromptCacheMarker(JsonElement request)
    {
        return request.ValueKind switch
        {
            JsonValueKind.Object => request.TryGetProperty("cachePoint", out _)
                || request.TryGetProperty("cache_control", out _)
                || request.EnumerateObject().Any(property => HasPromptCacheMarker(property.Value)),
            JsonValueKind.Array => request.EnumerateArray().Any(HasPromptCacheMarker),
            _ => false,
        };
    }

    private static string ReadMessageText(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
        {
            return message.ToString();
        }

        if (message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.Array)
        {
            return string.Concat(
                content
                    .EnumerateArray()
                    .Select(block =>
                        block.ValueKind == JsonValueKind.Object
                        && block.TryGetProperty("text", out var text)
                            ? text.GetString()
                            : null
                    )
                    .Where(text => text is not null)
            );
        }

        return message.ToString();
    }

    private static string StripDataUriPrefix(string dataUri)
    {
        var commaIndex = dataUri.IndexOf(',', StringComparison.Ordinal);
        return commaIndex < 0 ? dataUri : dataUri[(commaIndex + 1)..];
    }

}
