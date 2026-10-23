using System.Text.Json;
using ManagedCode.LlmTestServer.Determinism;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Cohere;

public static class CohereWireMapper
{
    public static LlmTestServerChatRequest ToRuntimeRequest(CohereChatRequest request)
    {
        return new()
        {
            Tools = request.Tools.Select(tool => new LlmTestServerToolDefinition { Name = tool.Function.Name, Description = tool.Function.Description, ParametersJson = tool.Function.Parameters.GetRawText() }).ToList(),
            ToolChoice = request.ToolChoice switch { "REQUIRED" => LlmTestServerToolChoice.Required, "NONE" => LlmTestServerToolChoice.None, _ => LlmTestServerToolChoice.Auto },
            RequireJson = request.ResponseFormat?.Type == "json_object",
            ResponseSchemaJson = request.ResponseFormat?.Schema?.GetRawText(),
            ModelId = request.Model,
            Stream = request.Stream,
            Messages = request
                .Messages
                .Select(message => new LlmTestServerMessage
                {
                    Role = message.Role,
                    Content = message.TextContent,
                    ToolCallId = message.ToolCallId,
                    ToolCalls = message.ToolCalls?.Select(call => new LlmTestServerToolCall { Id = call.Id, Name = call.Function.Name, ArgumentsJson = call.Function.Arguments }).ToList() ?? [],
                })
                .ToList(),
        };
    }

    public static CohereChatResponse ToChatResponse(LlmTestServerChatResult result)
    {
        return new()
        {
            Id = CreateResponseId(result.WireMetadata),
            FinishReason = result.ToolCalls.Count > 0 ? "TOOL_CALL" : "COMPLETE",
            Message = new CohereAssistantMessage
            {
                Content = [new CohereContentBlock { Text = result.Content }],
                ToolCalls = result.ToolCalls.Count == 0 ? null : result.ToolCalls.Select(ToToolCall).ToList(),
            },
            Usage = CreateUsage(result.Usage),
        };
    }

    public static JsonElement ToMessageStartEvent(LlmTestServerResponseMetadata? metadata = null)
    {
        return JsonSerializer.SerializeToElement(new
        {
            type = "message-start",
            id = CreateResponseId(metadata),
            delta = new
            {
                message = new { role = "assistant" },
            },
        });
    }

    public static JsonElement ToContentStartEvent()
    {
        return JsonSerializer.SerializeToElement(new
        {
            type = "content-start",
            index = 0,
            delta = new
            {
                message = new
                {
                    content = new CohereContentBlock(),
                },
            },
        });
    }

    public static JsonElement ToContentDeltaEvent(string text)
    {
        return JsonSerializer.SerializeToElement(new
        {
            type = "content-delta",
            index = 0,
            delta = new
            {
                message = new
                {
                    content = new { text },
                },
            },
        });
    }

    public static JsonElement ToContentEndEvent()
    {
        return JsonSerializer.SerializeToElement(new
        {
            type = "content-end",
            index = 0,
        });
    }

    public static JsonElement ToMessageEndEvent(LlmTestServerChatResult result)
    {
        return JsonSerializer.SerializeToElement(new
        {
            type = "message-end",
            delta = new
            {
                finish_reason = result.ToolCalls.Count > 0 ? "TOOL_CALL" : "COMPLETE",
                usage = CreateUsage(result.Usage),
            },
        });
    }

    private static CohereToolCall ToToolCall(LlmTestServerToolCall call)
    {
        return new() { Id = call.Id, Function = new() { Name = call.Name, Arguments = call.ArgumentsJson } };
    }

    public static IEnumerable<JsonElement> ToToolStreamEvents(LlmTestServerChatResult result)
    {
        for (var index = 0; index < result.ToolCalls.Count; index++)
        {
            var call = result.ToolCalls[index];
            yield return JsonSerializer.SerializeToElement(new { type = "tool-call-start", index, delta = new { message = new { tool_calls = ToToolCall(call) with { Function = new() { Name = call.Name, Arguments = "" } } } } });
            yield return JsonSerializer.SerializeToElement(new { type = "tool-call-delta", index, delta = new { message = new { tool_calls = new { function = new { arguments = call.ArgumentsJson } } } } });
            yield return JsonSerializer.SerializeToElement(new { type = "tool-call-end", index });
        }
    }

    public static CohereEmbedResponse ToEmbedResponse(IReadOnlyList<IReadOnlyList<float>> vectors, LlmTestServerResponseMetadata? metadata = null)
    {
        return new()
        {
            Id = CreateResponseId(metadata),
            Embeddings = new CohereEmbeddings { FloatValues = vectors },
        };
    }

    public static CohereErrorResponse ToError(string message)
    {
        return new() { Message = message };
    }

    public static List<string> ReadEmbeddingInputs(CohereEmbedRequest request)
    {
        if (request.Texts is { Count: > 0 })
        {
            return [.. request.Texts];
        }

        if (request.Inputs.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return [];
        }

        return request
            .Inputs
            .EnumerateArray()
            .Select(CohereContentReader.ReadTextContent)
            .Where(input => !string.IsNullOrWhiteSpace(input))
            .ToList();
    }

    private static CohereUsage CreateUsage(LlmTestServerTokenUsage usage)
    {
        var tokens = new CohereTokenUsage
        {
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
        };
        return new() { BilledUnits = tokens, Tokens = tokens };
    }

    private static string CreateResponseId(LlmTestServerResponseMetadata? metadata)
    {
        return new Guid((metadata ?? LlmTestServerResponseMetadata.Live()).IdFor("response")).ToString();
    }

}
