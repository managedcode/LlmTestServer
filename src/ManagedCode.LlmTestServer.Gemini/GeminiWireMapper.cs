using System.Text.Json;
using ManagedCode.LlmTestServer.Determinism;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Gemini;

public static class GeminiWireMapper
{
    public static LlmTestServerChatRequest ToRuntimeRequest(
        string model,
        GeminiGenerateContentRequest request,
        bool stream
    )
    {
        var messages = request
            .Contents
            .SelectMany(ReadMessages)
            .ToList();

        if (request.SystemInstruction is not null)
        {
            messages.Insert(
                0,
                new LlmTestServerMessage
                {
                    Role = "system",
                    Content = ReadText(request.SystemInstruction),
                }
            );
        }

        return new()
        {
            Tools = request.Tools.SelectMany(tool => tool.FunctionDeclarations).Where(tool => request.ToolConfig?.FunctionCallingConfig?.AllowedFunctionNames is not { Count: > 0 } allowed || allowed.Contains(tool.Name))
                .Select(tool => new LlmTestServerToolDefinition { Name = tool.Name, Description = tool.Description, ParametersJson = tool.ParametersJsonSchema?.GetRawText() ?? GeminiSchemaMapper.ToJsonSchema(tool.Parameters) ?? "{\"type\":\"object\"}" }).ToList(),
            ToolChoice = request.ToolConfig?.FunctionCallingConfig?.Mode switch { "ANY" => LlmTestServerToolChoice.Required, "NONE" => LlmTestServerToolChoice.None, _ => LlmTestServerToolChoice.Auto },
            RequireJson = request.GenerationConfig?.ResponseMimeType == "application/json",
            ResponseSchemaJson = request.GenerationConfig?.ResponseJsonSchema?.GetRawText() ?? GeminiSchemaMapper.ToJsonSchema(request.GenerationConfig?.ResponseSchema),
            RequireToolContinuationTokens = RequiresToolContinuationTokens(model),
            ModelId = model,
            Stream = stream,
            PromptCachePolicy = LlmTestServerPromptCachePolicy.Gemini,
            Messages = messages,
        };
    }

    public static GeminiGenerateContentResponse ToGenerateContentResponse(
        LlmTestServerChatResult result,
        string content,
        string? finishReason = "STOP",
        string? responseId = null
    )
    {
        var outputTokens = finishReason is not null
            ? result.Usage.OutputTokens - result.Usage.ReasoningTokens
            : LlmTestServerTokenCounter.CountTextTokens(content);
        return new()
        {
            Candidates =
            [
                new GeminiCandidate
                {
                    Content = new GeminiContent
                    {
                        Role = "model",
                        Parts = result.ToolCalls.Count > 0 && finishReason is not null ? (content.Length > 0 ? new[] { new GeminiPart { Text = content } } : []).Concat(result.ToolCalls.Select(call => new GeminiPart { ThoughtSignature = call.ContinuationToken, FunctionCall = new() { Id = call.Id, Name = call.Name, Args = JsonSerializer.Deserialize<JsonElement>(call.ArgumentsJson) } })).ToList() : [new GeminiPart { Text = content }],
                    },
                    FinishReason = finishReason,
                    Index = 0,
                },
            ],
            UsageMetadata = new GeminiUsageMetadata
            {
                PromptTokenCount = result.Usage.InputTokens,
                CachedContentTokenCount = result.Usage.CachedInputTokens,
                CandidatesTokenCount = outputTokens,
                ThoughtsTokenCount = finishReason is not null && result.Usage.ReasoningTokens > 0 ? result.Usage.ReasoningTokens : null,
                TotalTokenCount = result.Usage.InputTokens + outputTokens + (finishReason is not null ? result.Usage.ReasoningTokens : 0),
            },
            ModelVersion = result.ModelId,
            ResponseId = responseId ?? CreateResponseId(result.WireMetadata),
        };
    }

    public static bool RequiresToolContinuationTokens(string model)
    {
        return model == "gemini-3.8-flash";
    }

    private static IEnumerable<LlmTestServerMessage> ReadMessages(GeminiContent content)
    {
        var calls = content.Parts.Where(part => part.FunctionCall is not null).Select(part => new LlmTestServerToolCall { Id = part.FunctionCall!.Id ?? part.FunctionCall.Name, Name = part.FunctionCall.Name, ArgumentsJson = part.FunctionCall.Args.GetRawText(), ContinuationToken = part.ThoughtSignature }).ToList();
        if (calls.Count > 0 || content.Parts.Any(part => part.Text is not null || part.InlineData is not null || part.FileData is not null))
        {
            yield return new() { Role = NormalizeRole(content.Role), Content = ReadText(content), ToolCalls = calls };
        }

        foreach (var part in content.Parts.Where(part => part.FunctionResponse is not null))
        {
            yield return new() { Role = "tool", ToolCallId = part.FunctionResponse!.Id ?? part.FunctionResponse.Name, Content = part.FunctionResponse.Response.GetRawText() };
        }
    }

    public static GeminiEmbedContentResponse ToEmbedContentResponse(IReadOnlyList<float> vector)
    {
        return new()
        {
            Embedding = new GeminiContentEmbedding { Values = vector },
            UsageMetadata = new GeminiUsageMetadata
            {
                PromptTokenCount = 1,
                TotalTokenCount = 1,
            },
        };
    }

    public static GeminiOperationResponse ToVideoOperationResponse(
        string model,
        LlmTestServerVideoResult result,
        string videoUri,
        bool done
    )
    {
        return new()
        {
            Name = CreateVideoOperationName(model, result.GenerationId),
            Done = done,
            Response = done
                ? new GeminiVideoOperationResponse
                {
                    GenerateVideoResponse = new GeminiGenerateVideoResponse
                    {
                        UsageMetadata = ToUsageMetadata(result.Usage),
                        GeneratedSamples =
                        [
                            new GeminiGeneratedVideoSample
                            {
                                Video = new GeminiGeneratedVideo
                                {
                                    Uri = videoUri,
                                    MimeType = result.MediaType,
                                },
                            },
                        ],
                    },
                }
                : null,
        };
    }

    public static GeminiFileResponse ToFileResponse(
        string fileId,
        LlmTestServerVideoResult result,
        string fileUri,
        string downloadUri
    )
    {
        return new()
        {
            Name = CreateFileName(fileId),
            DisplayName = $"{fileId}.mp4",
            MimeType = result.MediaType,
            SizeBytes = result.Bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Uri = fileUri,
            DownloadUri = downloadUri,
            VideoMetadata = new GeminiVideoFileMetadata
            {
                VideoDuration = $"{result.Seconds}s",
            },
        };
    }

    public static GeminiErrorResponse ToError(int statusCode, string message)
    {
        return new()
        {
            Error = new GeminiError
            {
                Code = statusCode,
                Message = message,
                Status = ToGoogleStatus(statusCode),
            },
        };
    }

    public static string ReadText(GeminiContent content)
    {
        return string.Concat(
            content
                .Parts
                .Select(part => part.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
        );
    }

    public static string ReadPredictPrompt(GeminiPredictLongRunningRequest request)
    {
        return request
            .Instances
            .Select(ReadPrompt)
            .FirstOrDefault(prompt => !string.IsNullOrWhiteSpace(prompt)) ?? string.Empty;
    }

    public static string CreateVideoOperationName(string model, string operationId)
    {
        return $"models/{model}/operations/{operationId}";
    }

    public static string CreateFileName(string fileId)
    {
        return $"files/{fileId}";
    }

    private static GeminiUsageMetadata ToUsageMetadata(LlmTestServerTokenUsage usage)
    {
        return new()
        {
            PromptTokenCount = usage.InputTokens,
            CachedContentTokenCount = usage.CachedInputTokens,
            CandidatesTokenCount = usage.OutputTokens,
            TotalTokenCount = usage.TotalTokens,
        };
    }

    private static string ReadPrompt(JsonElement instance)
    {
        return instance.ValueKind == JsonValueKind.Object
            && instance.TryGetProperty("prompt", out var prompt)
            && prompt.ValueKind == JsonValueKind.String
            ? prompt.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string NormalizeRole(string role)
    {
        return string.Equals(role, "model", StringComparison.OrdinalIgnoreCase)
            ? "assistant"
            : role;
    }

    private static string ToGoogleStatus(int statusCode)
    {
        return statusCode switch
        {
            400 => "INVALID_ARGUMENT",
            401 => "UNAUTHENTICATED",
            403 => "PERMISSION_DENIED",
            404 => "NOT_FOUND",
            409 => "ABORTED",
            429 => "RESOURCE_EXHAUSTED",
            >= 500 => "INTERNAL",
            _ => "INVALID_ARGUMENT",
        };
    }

    private static string CreateResponseId(LlmTestServerResponseMetadata? metadata)
    {
        return "gemini-" + (metadata ?? LlmTestServerResponseMetadata.Live()).IdFor("response");
    }

}
