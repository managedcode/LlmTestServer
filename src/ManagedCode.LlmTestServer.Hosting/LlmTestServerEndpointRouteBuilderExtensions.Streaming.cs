using System.Text.Json;
using ManagedCode.LlmTestServer.Anthropic;
using ManagedCode.LlmTestServer.Bedrock;
using ManagedCode.LlmTestServer.Cohere;
using ManagedCode.LlmTestServer.Gemini;
using ManagedCode.LlmTestServer.Ollama;
using ManagedCode.LlmTestServer.OpenAI;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;
using Microsoft.AspNetCore.Http;

namespace ManagedCode.LlmTestServer.Hosting;

public static partial class LlmTestServerEndpointRouteBuilderExtensions
{
    private static async Task WriteStreamingChatAsync(
        HttpContext context,
        LlmTestServerChatResult result,
        bool includeUsage,
        OpenAiCacheUsageShape cacheUsageShape,
        CancellationToken cancellationToken
    )
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        var responseId = OpenAiWireMapper.CreateResponseId(result.WireMetadata);
        var created = (result.WireMetadata ?? WireMetadata(context)).CreatedAt.ToUnixTimeSeconds();

        var emittedContentFrames = 0;
        var deliveryApplied = false;
        if (result.StreamDelivery is { } configuredDelivery) { LlmTestServerStreamDeliveryEvidence.Record(context, configuredDelivery, 0, false); }
        foreach (var chunk in result.ToolCalls.Count > 0 ? new List<string> { result.Content } : result.StreamChunks)
        {
            var payload = JsonSerializer.Serialize(
                OpenAiWireMapper.ToChatChunk(result, chunk, responseId, created),
                _jsonOptions
            );
            await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken)
                .ConfigureAwait(false);
            await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            emittedContentFrames++;
            if (result.StreamDelivery is { } delivery)
            {
                var atBoundary = !deliveryApplied && emittedContentFrames == delivery.AfterContentFrames;
                deliveryApplied |= atBoundary;
                LlmTestServerStreamDeliveryEvidence.Record(context, delivery, emittedContentFrames, deliveryApplied);
                if (atBoundary)
                {
                    switch (delivery.Mode)
                    {
                        case LlmTestServerStreamDeliveryMode.EarlyCleanEnd:
                            return;
                        case LlmTestServerStreamDeliveryMode.DelayThenContinue:
                            await Task.Delay(delivery.DelayMilliseconds, cancellationToken).ConfigureAwait(false);
                            break;
                        case LlmTestServerStreamDeliveryMode.AbortConnection:
                            context.Abort();
                            throw new Microsoft.AspNetCore.Connections.ConnectionAbortedException("Configured stream delivery aborted the connection after a content frame.");
                    }
                }
            }
        }

        var finalPayload = JsonSerializer.Serialize(
            OpenAiWireMapper.ToChatChunk(result, string.Empty, responseId, created, result.ToolCalls.Count > 0 ? "tool_calls" : "stop"),
            _jsonOptions
        );
        await context.Response.WriteAsync($"data: {finalPayload}\n\n", cancellationToken)
            .ConfigureAwait(false);
        if (includeUsage)
        {
            var usageChunk = new OpenAiChatCompletionChunk
            {
                Id = responseId,
                Created = created,
                Model = result.ModelId,
                Usage = OpenAiWireMapper.ToChatResponse(result, cacheUsageShape).Usage,
            };
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(usageChunk, _jsonOptions)}\n\n", cancellationToken)
                .ConfigureAwait(false);
        }

        await context.Response.WriteAsync("data: [DONE]\n\n", cancellationToken).ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        context.MarkLlmTestServerTerminalMarker();
    }

    private static async Task WriteStreamingResponseAsync(
        HttpContext context,
        LlmTestServerChatResult result,
        string responseId,
        long created,
        OpenAiCacheUsageShape cacheUsageShape,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? includedProperties = null
    )
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";

        foreach (var streamEvent in OpenAiWireMapper.ToResponseStreamEvents(result, responseId, created, cacheUsageShape, includedProperties))
        {
            await WriteResponseSseDataAsync(context, streamEvent, cancellationToken).ConfigureAwait(false);
        }
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        context.MarkLlmTestServerTerminalMarker();
    }

    private static async Task WriteResponseSseDataAsync(
        HttpContext context,
        object data,
        CancellationToken cancellationToken
    )
    {
        var payload = JsonSerializer.Serialize(data, _jsonOptions);
        await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken)
            .ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteStreamingTranscriptionAsync(
        HttpContext context,
        LlmTestServerTranscriptionResult result,
        CancellationToken cancellationToken
    )
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";

        await WriteResponseSseDataAsync(
                context,
                OpenAiWireMapper.ToTranscriptTextDeltaEvent(result.Text),
                cancellationToken
            )
            .ConfigureAwait(false);
        await WriteResponseSseDataAsync(
                context,
                OpenAiWireMapper.ToTranscriptTextDoneEvent(result),
                cancellationToken
            )
            .ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        context.MarkLlmTestServerTerminalMarker();
    }

    private static async Task WriteImageStreamingAsync(
        HttpContext context,
        string dataUri,
        string eventPrefix,
        string? background,
        string? outputFormat,
        string? quality,
        string? size,
        CancellationToken cancellationToken
    )
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";

        await WriteResponseSseDataAsync(
                context,
                OpenAiWireMapper.ToImageStreamingEvent(
                    $"{eventPrefix}.partial_image",
                    dataUri,
                    completed: false,
                    background,
                    outputFormat,
                    quality,
                    size,
                    WireMetadata(context)
                ),
                cancellationToken
            )
            .ConfigureAwait(false);
        await WriteResponseSseDataAsync(
                context,
                OpenAiWireMapper.ToImageStreamingEvent(
                    $"{eventPrefix}.completed",
                    dataUri,
                    completed: true,
                    background,
                    outputFormat,
                    quality,
                    size,
                    WireMetadata(context)
                ),
                cancellationToken
            )
            .ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        context.MarkLlmTestServerTerminalMarker();
    }

    private static async Task WriteOllamaStreamingChatAsync(
        HttpContext context,
        LlmTestServerChatResult result,
        CancellationToken cancellationToken
    )
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/x-ndjson";

        foreach (var chunk in result.StreamChunks)
        {
            var payload = JsonSerializer.Serialize(
                OllamaWireMapper.ToChatChunk(result, chunk, done: false),
                _jsonOptions
            );
            await context.Response.WriteAsync($"{payload}\n", cancellationToken)
                .ConfigureAwait(false);
            await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var finalPayload = JsonSerializer.Serialize(
            OllamaWireMapper.ToChatChunk(result, string.Empty, done: true),
            _jsonOptions
        );
        await context.Response.WriteAsync($"{finalPayload}\n", cancellationToken)
            .ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        context.MarkLlmTestServerTerminalMarker();
    }

    private static async Task WriteAnthropicStreamingMessageAsync(
        HttpContext context,
        LlmTestServerChatResult result,
        CancellationToken cancellationToken,
        bool cacheOnly = false
    )
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";

        await WriteAnthropicSseEventAsync(
                context,
                "message_start",
                AnthropicWireMapper.ToMessageStartEvent(result),
                cancellationToken
            )
            .ConfigureAwait(false);
        if (!cacheOnly && result.OutputItems.Count > 0)
        {
            foreach (var streamEvent in AnthropicWireMapper.ToOrderedContentStreamEvents(result))
            {
                var element = JsonSerializer.SerializeToElement(streamEvent, _jsonOptions);
                await WriteAnthropicSseEventAsync(context, element.GetProperty("type").GetString()!, streamEvent, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            if (!cacheOnly && (result.ToolCalls.Count == 0 || result.Content.Length > 0))
            {
                await WriteAnthropicSseEventAsync(
                        context,
                        "content_block_start",
                        AnthropicWireMapper.ToContentBlockStartEvent(),
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                foreach (var chunk in result.StreamChunks)
                {
                    await WriteAnthropicSseEventAsync(
                            context,
                            "content_block_delta",
                            AnthropicWireMapper.ToContentBlockDeltaEvent(chunk),
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }

                await WriteAnthropicSseEventAsync(
                        context,
                        "content_block_stop",
                        AnthropicWireMapper.ToContentBlockStopEvent(),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (!cacheOnly && result.ToolCalls.Count > 0)
            {
                foreach (var streamEvent in AnthropicWireMapper.ToToolStreamEvents(result, result.Content.Length > 0 ? 1 : 0))
                {
                    var element = JsonSerializer.SerializeToElement(streamEvent, _jsonOptions);
                    await WriteAnthropicSseEventAsync(context, element.GetProperty("type").GetString()!, streamEvent, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        await WriteAnthropicSseEventAsync(
                context,
                "message_delta",
                AnthropicWireMapper.ToMessageDeltaEvent(result, cacheOnly),
                cancellationToken
            )
            .ConfigureAwait(false);
        await WriteAnthropicSseEventAsync(
                context,
                "message_stop",
                AnthropicWireMapper.ToMessageStopEvent(),
                cancellationToken
            )
            .ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        context.MarkLlmTestServerTerminalMarker();
    }

    private static async Task WriteAnthropicSseEventAsync(
        HttpContext context,
        string eventName,
        object data,
        CancellationToken cancellationToken
    )
    {
        var payload = JsonSerializer.Serialize(data, _jsonOptions);
        await context.Response.WriteAsync($"event: {eventName}\n", cancellationToken)
            .ConfigureAwait(false);
        await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken)
            .ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteCohereStreamingChatAsync(
        HttpContext context,
        LlmTestServerChatResult result,
        CancellationToken cancellationToken
    )
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";

        await WriteCohereSseEventAsync(
                context,
                "message-start",
                CohereWireMapper.ToMessageStartEvent(result.WireMetadata),
                cancellationToken
            )
            .ConfigureAwait(false);
        if (result.ToolCalls.Count == 0 || result.Content.Length > 0)
        {
            await WriteCohereSseEventAsync(
                    context,
                    "content-start",
                    CohereWireMapper.ToContentStartEvent(),
                    cancellationToken
                )
                .ConfigureAwait(false);

            foreach (var chunk in result.StreamChunks)
            {
                await WriteCohereSseEventAsync(
                        context,
                        "content-delta",
                        CohereWireMapper.ToContentDeltaEvent(chunk),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            await WriteCohereSseEventAsync(
                    context,
                    "content-end",
                    CohereWireMapper.ToContentEndEvent(),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        if (result.ToolCalls.Count > 0)
        {
            foreach (var streamEvent in CohereWireMapper.ToToolStreamEvents(result))
            {
                var element = JsonSerializer.SerializeToElement(streamEvent, _jsonOptions);
                await WriteCohereSseEventAsync(context, element.GetProperty("type").GetString()!, streamEvent, cancellationToken).ConfigureAwait(false);
            }
        }
        await WriteCohereSseEventAsync(
                context,
                "message-end",
                CohereWireMapper.ToMessageEndEvent(result),
                cancellationToken
            )
            .ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        context.MarkLlmTestServerTerminalMarker();
    }

    private static async Task WriteCohereSseEventAsync(
        HttpContext context,
        string eventName,
        object data,
        CancellationToken cancellationToken
    )
    {
        var payload = JsonSerializer.Serialize(data, _jsonOptions);
        await context.Response.WriteAsync($"event: {eventName}\n", cancellationToken)
            .ConfigureAwait(false);
        await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken)
            .ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteGeminiStreamingContentAsync(
        HttpContext context,
        LlmTestServerChatResult result,
        CancellationToken cancellationToken
    )
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        var sse = string.Equals(context.Request.Query["alt"], "sse", StringComparison.Ordinal);
        context.Response.ContentType = sse ? "text/event-stream" : "application/json";
        var responseId = "gemini-" + (result.WireMetadata ?? WireMetadata(context)).IdFor("response");
        if (!sse)
        {
            await context.Response.WriteAsync("[", cancellationToken).ConfigureAwait(false);
        }

        var first = true;
        foreach (var chunk in ReadStreamChunks(result))
        {
            var payload = JsonSerializer.Serialize(
                GeminiWireMapper.ToGenerateContentResponse(result, chunk, finishReason: null, responseId: responseId),
                _jsonOptions
            );
            await context.Response.WriteAsync(sse ? $"data: {payload}\n\n" : (first ? payload : $",{payload}"), cancellationToken).ConfigureAwait(false);
            await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            first = false;
        }

        var finalPayload = JsonSerializer.Serialize(
            GeminiWireMapper.ToGenerateContentResponse(result, string.Empty, responseId: responseId),
            _jsonOptions
        );
        await context.Response.WriteAsync(sse ? $"data: {finalPayload}\n\n" : $",{finalPayload}]", cancellationToken).ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        context.MarkLlmTestServerTerminalMarker();
    }

    private static async Task WriteBedrockConverseStreamAsync(
        HttpContext context,
        LlmTestServerChatResult result,
        CancellationToken cancellationToken
    )
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/vnd.amazon.eventstream";

        await WriteBedrockEventAsync(
                context,
                BedrockWireMapper.ToConverseMessageStartEvent(),
                cancellationToken
            )
            .ConfigureAwait(false);
        if (result.ToolCalls.Count == 0 || result.Content.Length > 0)
        {
            await WriteBedrockEventAsync(
                    context,
                    BedrockWireMapper.ToConverseContentBlockStartEvent(),
                    cancellationToken
                )
                .ConfigureAwait(false);

            foreach (var chunk in ReadStreamChunks(result))
            {
                await WriteBedrockEventAsync(
                        context,
                        BedrockWireMapper.ToConverseContentBlockDeltaEvent(chunk),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            await WriteBedrockEventAsync(
                    context,
                    BedrockWireMapper.ToConverseContentBlockStopEvent(),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        if (result.ToolCalls.Count > 0)
        {
            foreach (var streamEvent in BedrockWireMapper.ToToolStreamEvents(result, result.Content.Length > 0 ? 1 : 0))
            {
                await WriteBedrockEventAsync(context, streamEvent, cancellationToken).ConfigureAwait(false);
            }
        }
        await WriteBedrockEventAsync(
                context,
                BedrockWireMapper.ToConverseMessageStopEvent(result.ToolCalls.Count > 0),
                cancellationToken
            )
            .ConfigureAwait(false);
        await WriteBedrockEventAsync(
                context,
                BedrockWireMapper.ToConverseMetadataEvent(result),
                cancellationToken
            )
            .ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        context.MarkLlmTestServerTerminalMarker();
    }

    private static async Task WriteBedrockInvokeModelStreamAsync(
        HttpContext context,
        LlmTestServerChatResult result,
        CancellationToken cancellationToken
    )
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/vnd.amazon.eventstream";
        context.Response.Headers["x-amzn-bedrock-content-type"] = "application/json";

        foreach (var chunk in ReadStreamChunks(result))
        {
            await WriteBedrockEventAsync(
                    context,
                    BedrockWireMapper.ToInvokeStreamChunk(chunk),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    private static async Task WriteBedrockEventAsync(
        HttpContext context,
        object data,
        CancellationToken cancellationToken
    )
    {
        var payload = BedrockEventStreamEncoder.Encode(data, _jsonOptions);
        await context.Response.Body.WriteAsync(payload, cancellationToken)
            .ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> ReadStreamChunks(LlmTestServerChatResult result)
    {
        return result.StreamChunks.Count == 0 ? [result.Content] : result.StreamChunks;
    }

    private static string? ReadBearerToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string Prefix = "Bearer ";
        return header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? header[Prefix.Length..]
            : null;
    }

    private static LlmTestServerChatRequest CorrelateChatRequest(
        HttpContext context,
        LlmTestServerChatRequest request
    )
    {
        return request with
        {
            RequestId = context.GetLlmTestServerProviderHttpTraceId() ?? context.TraceIdentifier,
            StreamDeliverySurface = string.Equals(context.Request.Path.Value, "/openai/v1/chat/completions", StringComparison.Ordinal)
                ? LlmTestServerStreamDeliverySurface.OpenAiChatCompletions : null,
        };
    }

    private static string? ReadAccessToken(HttpContext context)
    {
        var bearerToken = ReadBearerToken(context);
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            return bearerToken;
        }

        var apiKey = context.Request.Headers["api-key"].ToString();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return apiKey;
        }

        var anthropicApiKey = context.Request.Headers["x-api-key"].ToString();
        if (!string.IsNullOrWhiteSpace(anthropicApiKey))
        {
            return anthropicApiKey;
        }

        var googleApiKey = context.Request.Headers["x-goog-api-key"].ToString();
        if (!string.IsNullOrWhiteSpace(googleApiKey))
        {
            return googleApiKey;
        }

        var queryKey = context.Request.Query["key"].ToString();
        return string.IsNullOrWhiteSpace(queryKey) ? null : queryKey;
    }

    private static List<string> ReadEmbeddingInputs(JsonElement input)
    {
        return input.ValueKind == JsonValueKind.Array
            ? input
                .EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString())
                .ToList()
            : [input.ValueKind == JsonValueKind.String ? input.GetString() ?? string.Empty : input.ToString()];
    }

}
