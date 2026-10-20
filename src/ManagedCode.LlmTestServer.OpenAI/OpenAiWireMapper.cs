using System.Text.Json;
using ManagedCode.LlmTestServer.Determinism;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.OpenAI;

public enum OpenAiCacheUsageShape
{
    None,
    PromptTokensDetails,
    PromptTokensDetailsWithCacheWriteTokens,
    DeepSeekPromptCache,
}

public static partial class OpenAiWireMapper
{
    public static LlmTestServerChatRequest ToRuntimeRequest(
        OpenAiChatCompletionRequest request,
        LlmTestServerPromptCachePolicy promptCachePolicy = LlmTestServerPromptCachePolicy.None
    )
    {
        return OpenAiToolMapper.Apply(new()
        {
            ModelId = request.Model,
            AllowParallelToolCalls = request.ParallelToolCalls,
            Stream = request.Stream,
            PromptCachePolicy = promptCachePolicy,
            PromptCacheKey = ReadPromptCacheKey(
                request.PromptCacheKey,
                request.SessionId,
                promptCachePolicy
            ),
            Messages = request
                .Messages
                .Select(message => new LlmTestServerMessage
                {
                    Role = message.Role,
                    Content = message.RefusalTextContent ?? message.TextContent,
                    OutputDisposition = message.RefusalTextContent is null ? null : new() { Kind = LlmTestServerOutputDispositionKind.Refused },
                    ToolCallId = message.ToolCallId,
                    ToolCalls = message.ToolCalls?.Select(OpenAiToolMapper.ToRuntime).ToList() ?? [],
                })
                .ToList(),
        }, request.Tools, request.ToolChoice, request.ResponseFormat);
    }

    public static LlmTestServerChatRequest ToRuntimeRequest(
        OpenAiResponseRequest request,
        LlmTestServerPromptCachePolicy promptCachePolicy = LlmTestServerPromptCachePolicy.None,
        bool allowNativeTools = false
    )
    {
        var messages = ReadResponseInputMessages(request.Input);
        if (!string.IsNullOrWhiteSpace(request.Instructions))
        {
            messages.Insert(
                0,
                new LlmTestServerMessage
                {
                    Role = "system",
                    Content = request.Instructions,
                }
            );
        }

        var normalized = OpenAiToolMapper.Apply(new()
        {
            ModelId = request.Model,
            AllowParallelToolCalls = request.ParallelToolCalls,
            Stream = request.Stream,
            PromptCachePolicy = promptCachePolicy,
            PromptCacheKey = ReadPromptCacheKey(
                request.PromptCacheKey,
                request.SessionId,
                promptCachePolicy
            ),
            Messages = messages,
        }, request.Tools.Where(tool => tool.Type == "function").Select(tool => new OpenAiTool { Type = tool.Type, Function = new() { Name = tool.Name ?? string.Empty, Description = tool.Description, Parameters = tool.Parameters.ValueKind == JsonValueKind.Undefined ? JsonSerializer.SerializeToElement(new { type = "object" }) : tool.Parameters } }).ToList(),
            FunctionResponseChoice(request.ToolChoice),
            request.Text?.Format is { } format ? new() { Type = format.Type, JsonSchema = format.Type == "json_schema" ? new() { Schema = format.Schema } : null } : null);
        return ApplyNativeResponseRequest(request, normalized, allowNativeTools);
    }

    public static OpenAiChatCompletionResponse ToChatResponse(
        LlmTestServerChatResult result,
        OpenAiCacheUsageShape cacheUsageShape = OpenAiCacheUsageShape.None
    )
    {
        var now = (result.WireMetadata ?? LlmTestServerResponseMetadata.Live()).CreatedAt.ToUnixTimeSeconds();

        return new OpenAiChatCompletionResponse
        {
            Id = CreateResponseId(result.WireMetadata),
            Created = now,
            Model = result.ModelId,
            Choices =
            [
                new OpenAiChatChoice
                {
                    Index = 0,
                    Message = result.OutputDisposition?.Kind == LlmTestServerOutputDispositionKind.Refused
                        ? new OpenAiChatMessage { Role = "assistant", Content = JsonSerializer.SerializeToElement<object?>(null), Refusal = result.Content }
                        : OpenAiChatMessage.FromText("assistant", result.Content) with { ToolCalls = result.ToolCalls.Count > 0 ? result.ToolCalls.Select(call => OpenAiToolMapper.ToWire(call)).ToList() : null },
                    FinishReason = result.OutputDisposition?.Kind == LlmTestServerOutputDispositionKind.Filtered ? "content_filter" : result.ToolCalls.Count > 0 ? "tool_calls" : "stop",
                },
            ],
            Usage = CreateUsage(result.Usage, cacheUsageShape),
        };
    }

    public static OpenAiChatCompletionChunk ToChatChunk(
        LlmTestServerChatResult result,
        string content,
        string responseId,
        long created,
        string? finishReason = null
    )
    {
        return new()
        {
            Id = responseId,
            Created = created,
            Model = result.ModelId,
            Choices =
            [
                new OpenAiChatChunkChoice
                {
                    Index = 0,
                    Delta = OpenAiChatMessage.FromText("assistant", content) with
                    {
                        Content = result.OutputDisposition?.Kind == LlmTestServerOutputDispositionKind.Refused
                            ? JsonSerializer.SerializeToElement<object?>(null) : JsonSerializer.SerializeToElement(content),
                        Refusal = result.OutputDisposition?.Kind == LlmTestServerOutputDispositionKind.Refused && finishReason is null ? content : null,
                        ToolCalls = finishReason is null && result.ToolCalls.Count > 0
                            ? result.ToolCalls.Select((call, index) => OpenAiToolMapper.ToWire(call, index)).ToList() : null,
                    },
                    FinishReason = finishReason is not null && result.OutputDisposition?.Kind == LlmTestServerOutputDispositionKind.Filtered ? "content_filter" : finishReason,
                },
            ],
        };
    }

    public static OpenAiResponse ToResponse(
        LlmTestServerChatResult result,
        string responseId,
        long created,
        OpenAiCacheUsageShape cacheUsageShape = OpenAiCacheUsageShape.None,
        IReadOnlyCollection<string>? includedProperties = null
    )
    {
        return new()
        {
            Id = responseId,
            CreatedAt = created,
            Model = result.ModelId,
            Output = CreateResponseOutput(result, includedProperties),
            Status = result.OutputDisposition?.Kind == LlmTestServerOutputDispositionKind.Filtered ? "incomplete" : "completed",
            IncompleteDetails = result.OutputDisposition?.Kind == LlmTestServerOutputDispositionKind.Filtered ? new() : null,
            Usage = new OpenAiResponseUsage
            {
                InputTokens = result.Usage.InputTokens,
                OutputTokens = result.Usage.OutputTokens,
                TotalTokens = result.Usage.TotalTokens,
                InputTokensDetails = CreateInputTokensDetails(
                    result.Usage,
                    cacheUsageShape
                ),
                OutputTokensDetails = result.Usage.ReasoningTokens > 0
                    ? new OpenAiOutputTokensDetails
                    {
                        ReasoningTokens = result.Usage.ReasoningTokens,
                    }
                    : null,
            },
        };
    }

    public static object ToResponseCreatedEvent(
        LlmTestServerChatResult result,
        string responseId,
        long created
    )
    {
        return new
        {
            type = "response.created",
            response = new
            {
                id = responseId,
                @object = "response",
                created_at = created,
                model = result.ModelId,
                status = "in_progress",
            },
        };
    }

    public static object ToResponseOutputItemAddedEvent(string responseId, LlmTestServerResponseMetadata? metadata = null)
    {
        return new
        {
            type = "response.output_item.added",
            response_id = responseId,
            output_index = 0,
            item = (OpenAiResponseOutputItem)CreateOutputMessage(string.Empty, completed: false, metadata),
        };
    }

    public static object ToResponseContentPartAddedEvent(string responseId)
    {
        return new
        {
            type = "response.content_part.added",
            response_id = responseId,
            output_index = 0,
            content_index = 0,
            part = (OpenAiResponseContentPart)new OpenAiResponseOutputText(),
        };
    }

    public static object ToResponseContentPartDeltaEvent(string responseId, string delta)
    {
        return new
        {
            type = "response.output_text.delta",
            response_id = responseId,
            output_index = 0,
            content_index = 0,
            delta,
        };
    }

    public static object ToResponseOutputItemDoneEvent(string responseId, LlmTestServerChatResult result)
    {
        return new
        {
            type = "response.output_item.done",
            response_id = responseId,
            output_index = 0,
            item = (OpenAiResponseOutputItem)CreateOutputMessage(result.Content, completed: true, result.WireMetadata),
        };
    }

    public static object ToResponseDoneEvent(
        LlmTestServerChatResult result,
        string responseId,
        long created,
        OpenAiCacheUsageShape cacheUsageShape = OpenAiCacheUsageShape.None
    )
    {
        return new
        {
            type = result.OutputDisposition?.Kind == LlmTestServerOutputDispositionKind.Filtered ? "response.incomplete" : "response.completed",
            response = ToResponse(result, responseId, created, cacheUsageShape),
        };
    }

    public static object ToModelsResponse(IEnumerable<LlmTestServerModel> models)
    {
        return new
        {
            @object = "list",
            data = models.Select(model => new
            {
                id = model.Id,
                @object = "model",
                owned_by = model.OwnedBy,
                metadata = new { kind = model.Kind.ToString().ToLowerInvariant() },
            }),
        };
    }

    public static OpenAiEmbeddingResponse ToEmbeddingResponse(
        string model,
        IReadOnlyList<IReadOnlyList<float>> vectors,
        bool encodeAsBase64 = false,
        LlmTestServerResponseMetadata? metadata = null
    )
    {
        return new()
        {
            Id = CreateResponseId(metadata),
            Model = model,
            Data = vectors
                .Select((vector, index) => OpenAiEmbeddingData.FromVector(index, vector, encodeAsBase64))
                .ToList(),
            Usage = new OpenAiUsage
            {
                PromptTokens = vectors.Count,
                TotalTokens = vectors.Count,
            },
        };
    }

    public static OpenAiImageGenerationResponse ToImageResponse(
        string dataUri,
        string? background = null,
        string? outputFormat = null,
        string? quality = null,
        string? size = null,
        LlmTestServerResponseMetadata? metadata = null
    )
    {
        return new OpenAiImageGenerationResponse
        {
            Created = (metadata ?? LlmTestServerResponseMetadata.Live()).CreatedAt.ToUnixTimeSeconds(),
            Background = background,
            OutputFormat = outputFormat,
            Quality = quality,
            Size = size,
            Data = [new OpenAiImageData { Base64Json = ReadBase64Payload(dataUri) }],
        };
    }

    public static object ToImageStreamingEvent(
        string eventType,
        string dataUri,
        bool completed,
        string? background,
        string? outputFormat,
        string? quality,
        string? size,
        LlmTestServerResponseMetadata? metadata = null
    )
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = eventType,
            ["b64_json"] = ReadBase64Payload(dataUri),
            ["created_at"] = (metadata ?? LlmTestServerResponseMetadata.Live()).CreatedAt.ToUnixTimeSeconds(),
            ["size"] = size ?? "1024x1024",
            ["quality"] = quality ?? "auto",
            ["background"] = background ?? "auto",
            ["output_format"] = outputFormat ?? "png",
        };

        if (completed)
        {
            payload["usage"] = new
            {
                total_tokens = 1,
                input_tokens = 1,
                output_tokens = 0,
                input_tokens_details = new
                {
                    text_tokens = 1,
                    image_tokens = 0,
                },
            };
        }
        else
        {
            payload["partial_image_index"] = 0;
        }

        return payload;
    }

    public static OpenAiVideoResponse ToVideoResponse(LlmTestServerVideoResult result)
    {
        return new()
        {
            Id = result.VideoId,
            Status = "completed",
            Progress = 100,
            RemixedFromVideoId = result.RemixedFromVideoId,
            Model = result.ModelId,
            Prompt = result.Prompt,
            CreatedAt = result.CreatedAt,
            CompletedAt = result.CreatedAt,
            ExpiresAt = result.CreatedAt + 172800,
            Seconds = result.Seconds,
            Size = result.Size,
        };
    }

    public static OpenAiVideoListResponse ToVideoListResponse(LlmTestServerVideoResult result)
    {
        var video = ToVideoResponse(result);
        return new()
        {
            Data = [video],
            FirstId = video.Id,
            LastId = video.Id,
        };
    }

    public static OpenAiVideoDeleteResponse ToVideoDeleteResponse(string videoId)
    {
        return new() { Id = videoId };
    }

    public static OpenAiVideoCharacterResponse ToVideoCharacterResponse(
        string characterId,
        string name,
        long createdAt
    )
    {
        return new()
        {
            Id = characterId,
            CreatedAt = createdAt,
            Name = name,
        };
    }

    public static AzureVideoGenerationJobResponse ToAzureVideoGenerationJobResponse(
        LlmTestServerVideoResult result
    )
    {
        var (width, height) = ReadVideoSize(result.Size);
        var generation = ToAzureVideoGenerationResponse(result);
        return new()
        {
            Id = result.VideoId,
            CreatedAt = result.CreatedAt,
            FinishedAt = result.CreatedAt,
            ExpiresAt = result.CreatedAt + 172800,
            Generations = [generation],
            Prompt = result.Prompt,
            Model = result.ModelId,
            NSeconds = int.TryParse(result.Seconds, out var seconds) ? seconds : 4,
            Height = height,
            Width = width,
        };
    }

    public static AzureVideoGenerationResponse ToAzureVideoGenerationResponse(
        LlmTestServerVideoResult result
    )
    {
        var (width, height) = ReadVideoSize(result.Size);
        return new()
        {
            Id = result.GenerationId,
            JobId = result.VideoId,
            CreatedAt = result.CreatedAt,
            Width = width,
            Height = height,
            NSeconds = int.TryParse(result.Seconds, out var seconds) ? seconds : 4,
            Prompt = result.Prompt,
        };
    }

    public static AzureVideoGenerationJobListResponse ToAzureVideoGenerationJobListResponse(
        LlmTestServerVideoResult result
    )
    {
        var job = ToAzureVideoGenerationJobResponse(result);
        return new()
        {
            Data = [job],
            FirstId = job.Id,
            LastId = job.Id,
        };
    }

    public static OpenAiAudioTranscriptionResponse ToAudioTranscriptionResponse(
        LlmTestServerTranscriptionResult result
    )
    {
        return new() { Text = result.Text, Usage = ToAudioTranscriptionUsage(result.Usage) };
    }

    public static GroqAudioTranscriptionResponse ToGroqAudioTranscriptionResponse(
        LlmTestServerTranscriptionResult result
    )
    {
        return new()
        {
            Text = result.Text,
            XGroq = new GroqRequestMetadata { Id = "req_" + (result.WireMetadata ?? LlmTestServerResponseMetadata.Live()).IdFor("groq-request") },
        };
    }

    public static OpenAiVerboseAudioTranscriptionResponse ToVerboseAudioTranscriptionResponse(
        LlmTestServerTranscriptionResult result
    )
    {
        return new()
        {
            Text = result.Text,
            Duration = result.Usage?.DurationSeconds ?? 0,
            Usage = ToAudioTranscriptionUsage(result.Usage),
        };
    }

    private static OpenAiAudioTranscriptionUsage? ToAudioTranscriptionUsage(LlmTestServerTranscriptionUsage? usage)
    {
        if (usage is null)
        {
            return null;
        }
        if (usage.DurationSeconds is { } seconds)
        {
            return new() { Type = "duration", Seconds = seconds };
        }
        return new()
        {
            Type = "tokens",
            InputTokens = usage.InputAudioTokens + usage.InputTextTokens,
            OutputTokens = usage.OutputTokens,
            TotalTokens = usage.InputAudioTokens + usage.InputTextTokens + usage.OutputTokens,
            InputTokenDetails = new()
            {
                AudioTokens = usage.InputAudioTokens!.Value,
                TextTokens = usage.InputTextTokens!.Value,
            },
        };
    }

    public static object ToTranscriptTextDeltaEvent(string delta)
    {
        return new
        {
            type = "transcript.text.delta",
            delta,
        };
    }

    public static object ToTranscriptTextDoneEvent(LlmTestServerTranscriptionResult result)
    {
        return new
        {
            type = "transcript.text.done",
            text = result.Text,
            usage = ToAudioTranscriptionUsage(result.Usage),
        };
    }

    public static OpenAiErrorResponse ToError(string code, string message)
    {
        return new()
        {
            Error = new OpenAiError
            {
                Code = code,
                Message = message,
            },
        };
    }

    public static string CreateResponseId(LlmTestServerResponseMetadata? metadata = null)
    {
        return "llmtestserver-" + (metadata ?? LlmTestServerResponseMetadata.Live()).IdFor("chat");
    }

    private static OpenAiUsage CreateUsage(
        LlmTestServerTokenUsage usage,
        OpenAiCacheUsageShape cacheUsageShape = OpenAiCacheUsageShape.None
    )
    {
        return new()
        {
            PromptTokens = usage.InputTokens,
            CompletionTokens = usage.OutputTokens,
            TotalTokens = usage.TotalTokens,
            PromptTokensDetails = CreatePromptTokensDetails(usage, cacheUsageShape),
            CompletionTokensDetails = usage.ReasoningTokens > 0
                ? new OpenAiCompletionTokensDetails
                {
                    ReasoningTokens = usage.ReasoningTokens,
                }
                : null,
            PromptCacheHitTokens = cacheUsageShape == OpenAiCacheUsageShape.DeepSeekPromptCache
                ? usage.CachedInputTokens
                : null,
            PromptCacheMissTokens = cacheUsageShape == OpenAiCacheUsageShape.DeepSeekPromptCache
                ? Math.Max(0, usage.InputTokens - usage.CachedInputTokens)
                : null,
        };
    }

    private static OpenAiPromptTokensDetails? CreatePromptTokensDetails(
        LlmTestServerTokenUsage usage,
        OpenAiCacheUsageShape cacheUsageShape
    )
    {
        return cacheUsageShape switch
        {
            OpenAiCacheUsageShape.PromptTokensDetails => new OpenAiPromptTokensDetails
            {
                CachedTokens = usage.CachedInputTokens,
            },
            OpenAiCacheUsageShape.PromptTokensDetailsWithCacheWriteTokens => new OpenAiPromptTokensDetails
            {
                CachedTokens = usage.CachedInputTokens,
                CacheWriteTokens = usage.CacheCreationInputTokens,
            },
            _ => null,
        };
    }

    private static OpenAiInputTokensDetails? CreateInputTokensDetails(
        LlmTestServerTokenUsage usage,
        OpenAiCacheUsageShape cacheUsageShape
    )
    {
        return cacheUsageShape switch
        {
            OpenAiCacheUsageShape.PromptTokensDetails => new OpenAiInputTokensDetails
            {
                CachedTokens = usage.CachedInputTokens,
            },
            OpenAiCacheUsageShape.PromptTokensDetailsWithCacheWriteTokens => new OpenAiInputTokensDetails
            {
                CachedTokens = usage.CachedInputTokens,
                CacheWriteTokens = usage.CacheCreationInputTokens,
            },
            _ => null,
        };
    }

    private static string? ReadPromptCacheKey(
        string? promptCacheKey,
        string? sessionId,
        LlmTestServerPromptCachePolicy promptCachePolicy
    )
    {
        if (promptCachePolicy == LlmTestServerPromptCachePolicy.OpenRouter
            && !string.IsNullOrWhiteSpace(sessionId))
        {
            return sessionId;
        }

        return string.IsNullOrWhiteSpace(promptCacheKey) ? null : promptCacheKey;
    }

    private static (int Width, int Height) ReadVideoSize(string size)
    {
        var parts = size.Split('x', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && int.TryParse(parts[0], out var width)
            && int.TryParse(parts[1], out var height)
            ? (width, height)
            : (1280, 720);
    }

    private static string ReadBase64Payload(string dataUri)
    {
        var commaIndex = dataUri.IndexOf(',', StringComparison.Ordinal);
        return commaIndex >= 0 ? dataUri[(commaIndex + 1)..] : dataUri;
    }

    private static OpenAiResponseOutputMessage CreateOutputMessage(
        string content,
        bool completed,
        LlmTestServerResponseMetadata? metadata = null
    )
    {
        return new()
        {
            Id = "msg_" + (metadata ?? LlmTestServerResponseMetadata.Live()).IdFor("message"),
            Status = completed ? "completed" : "in_progress",
            Content = [new OpenAiResponseOutputText { Text = content }],
        };
    }

    private static List<LlmTestServerMessage> ReadResponseInputMessages(JsonElement input)
    {
        var messages = input.ValueKind switch
        {
            JsonValueKind.String =>
            [
                new LlmTestServerMessage
                {
                    Role = "user",
                    Content = input.GetString() ?? string.Empty,
                },
            ],
            JsonValueKind.Array => input
                .EnumerateArray()
                .Select(ReadResponseInputMessage)
                .Where(message => !string.IsNullOrWhiteSpace(message.Content) || message.ToolCalls.Count > 0 || message.ToolCallId is not null || message.OutputItems.Count > 0 || message.OutputDisposition is not null)
                .ToList(),
            JsonValueKind.Object =>
            [
                ReadResponseInputMessage(input),
            ],
            _ => [],
        };
        return CoalesceNativeAssistantGroups(messages);
    }

    private static LlmTestServerMessage ReadResponseInputMessage(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return new LlmTestServerMessage
            {
                Role = "user",
                Content = item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString(),
            };
        }

        if (item.TryGetProperty("type", out var itemType))
        {
            if (itemType.GetString() is "web_search_call" or "file_search_call" or "code_interpreter_call")
            {
                return new() { Role = "assistant", OutputItems = [ReadNativeToolItem(item)] };
            }
            if (itemType.GetString() == "function_call")
            {
                var call = new LlmTestServerToolCall
                {
                    Id = item.GetProperty("call_id").GetString()!,
                    Name = item.GetProperty("name").GetString()!,
                    ArgumentsJson = item.GetProperty("arguments").GetString()!
                };
                return new()
                {
                    Role = "assistant",
                    ToolCalls = [call],
                    OutputItems = [new LlmTestServerResponseFunctionCallItem
                    {
                        Id = item.TryGetProperty("id", out var outputId) && outputId.ValueKind == JsonValueKind.String
                            ? outputId.GetString()! : "fc_" + call.Id,
                        Call = call
                    }]
                };
            }

            if (itemType.GetString() == "function_call_output")
            {
                return new() { Role = "tool", ToolCallId = item.GetProperty("call_id").GetString(), Content = ReadResponseInputContent(item.GetProperty("output")) };
            }
        }
        var role = item.TryGetProperty("role", out var roleElement)
            ? roleElement.GetString() ?? "user"
            : "user";

        if (item.TryGetProperty("content", out var content))
        {
            return new LlmTestServerMessage
            {
                Role = role,
                Content = ReadResponseInputContent(content),
                OutputDisposition = HasRefusalPart(content) ? new() { Kind = LlmTestServerOutputDispositionKind.Refused } : null,
                OutputItems = role == "assistant" ? ReadNativeMessageItems(item, content) : [],
            };
        }

        if (item.TryGetProperty("text", out var text))
        {
            return new LlmTestServerMessage
            {
                Role = role,
                Content = ReadResponseInputContent(text),
            };
        }

        return new LlmTestServerMessage
        {
            Role = role,
            Content = item.ToString(),
        };
    }

    private static bool HasRefusalPart(JsonElement content)
    {
        return content.ValueKind == JsonValueKind.Array && content.EnumerateArray().Any(part => part.ValueKind == JsonValueKind.Object
            && part.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "refusal");
    }

    internal static string ReadResponseInputContent(JsonElement content)
    {
        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Concat(content.EnumerateArray().Select(ReadResponseInputContent)),
            JsonValueKind.Object when content.TryGetProperty("type", out var partType) && partType.ValueKind == JsonValueKind.String && partType.GetString() == "refusal"
                && content.TryGetProperty("refusal", out var refusal) => refusal.GetString() ?? string.Empty,
            JsonValueKind.Object when content.TryGetProperty("text", out var text) =>
                ReadResponseInputContent(text),
            JsonValueKind.Object when content.TryGetProperty("content", out var nested) =>
                ReadResponseInputContent(nested),
            JsonValueKind.Object when content.TryGetProperty("type", out var type)
                && type.GetString() is "input_text" or "output_text"
                && content.TryGetProperty("text", out var typedText) =>
                ReadResponseInputContent(typedText),
            JsonValueKind.Object when content.TryGetProperty("type", out var imageType)
                && imageType.GetString() == "input_image"
                && content.TryGetProperty("image_url", out var imageUrl) => ReadResponseInputContent(imageUrl),
            JsonValueKind.Object when content.TryGetProperty("type", out var mediaType)
                && mediaType.GetString() is "input_image" or "input_file" => content.GetRawText(),
            JsonValueKind.Object when content.TryGetProperty("type", out _) => string.Empty,
            JsonValueKind.Undefined or JsonValueKind.Null => string.Empty,
            _ => content.ToString(),
        };
    }

}
