using System.Text.Json;
using ManagedCode.LlmTestServer.Determinism;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Ollama;

public static class OllamaWireMapper
{
    public static LlmTestServerChatRequest ToRuntimeRequest(OllamaChatRequest request)
    {
        return new()
        {
            Tools = request.Tools.Select(tool => new LlmTestServerToolDefinition { Name = tool.Function.Name, Description = tool.Function.Description, ParametersJson = tool.Function.Parameters.GetRawText() }).ToList(),
            RequireJson = request.Format.ValueKind == JsonValueKind.Object || request.Format.ValueKind == JsonValueKind.String && request.Format.GetString() == "json",
            ResponseSchemaJson = request.Format.ValueKind == JsonValueKind.Object ? request.Format.GetRawText() : null,
            ModelId = request.Model,
            Stream = request.Stream != false,
            PromptCachePolicy = LlmTestServerPromptCachePolicy.Ollama,
            Messages = request
                .Messages
                .Select(message => new LlmTestServerMessage
                {
                    Role = message.Role,
                    Content = message.TextContent,
                    ToolCallId = message.ToolName,
                    ToolCalls = message.ToolCalls?.Select((call, index) => new LlmTestServerToolCall { Id = call.Function.Name + "_" + index, Name = call.Function.Name, ArgumentsJson = call.Function.Arguments.GetRawText() }).ToList() ?? [],
                })
                .ToList(),
        };
    }

    public static OllamaChatResponse ToChatResponse(LlmTestServerChatResult result)
    {
        return new()
        {
            Model = result.ModelId,
            CreatedAt = (result.WireMetadata ?? LlmTestServerResponseMetadata.Live()).CreatedAt,
            Message = new OllamaChatMessageResponse { Content = result.Content, ToolCalls = MapTools(result) },
            Done = true,
            DoneReason = "stop",
            PromptEvalCount = result.Usage.InputTokens,
            PromptEvalCachedCount = result.Usage.CachedInputTokens,
            EvalCount = result.Usage.OutputTokens,
        };
    }

    public static OllamaChatResponse ToChatChunk(
        LlmTestServerChatResult result,
        string content,
        bool done
    )
    {
        return new()
        {
            Model = result.ModelId,
            CreatedAt = (result.WireMetadata ?? LlmTestServerResponseMetadata.Live()).CreatedAt,
            Message = new OllamaChatMessageResponse { Content = content, ToolCalls = done ? MapTools(result) : null },
            Done = done,
            DoneReason = done ? "stop" : null,
            PromptEvalCount = done ? result.Usage.InputTokens : 0,
            PromptEvalCachedCount = done ? result.Usage.CachedInputTokens : 0,
            EvalCount = done ? result.Usage.OutputTokens : 0,
        };
    }

    private static List<OllamaToolCall>? MapTools(LlmTestServerChatResult result)
    {
        return result.ToolCalls.Count == 0 ? null
        : result.ToolCalls.Select(call => new OllamaToolCall { Function = new() { Name = call.Name, Arguments = JsonSerializer.Deserialize<JsonElement>(call.ArgumentsJson) } }).ToList();
    }

    public static OllamaEmbedResponse ToEmbedResponse(
        string model,
        IReadOnlyList<IReadOnlyList<float>> vectors
    )
    {
        return new()
        {
            Model = model,
            Embeddings = vectors,
            PromptEvalCount = vectors.Count,
        };
    }

    public static OllamaErrorResponse ToError(string message)
    {
        return new() { Error = message };
    }

    public static List<string> ReadEmbeddingInputs(OllamaEmbedRequest request)
    {
        return request.Input.ValueKind == System.Text.Json.JsonValueKind.Array
            ? request
                .Input
                .EnumerateArray()
                .Select(item => item.ValueKind == System.Text.Json.JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString())
                .ToList()
            : [request.Input.ValueKind == System.Text.Json.JsonValueKind.String ? request.Input.GetString() ?? string.Empty : request.Input.ToString()];
    }

}
