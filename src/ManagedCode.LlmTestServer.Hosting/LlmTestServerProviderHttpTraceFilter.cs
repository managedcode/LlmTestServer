using System.Diagnostics;
using System.Text.Json;
using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.Runtime;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagedCode.LlmTestServer.Hosting;

public sealed class LlmTestServerProviderHttpTraceFilter(
    ILlmTestServerProviderHttpTraceStore traceStore,
    ILlmTestServerRuntime runtime,
    IOptions<LlmTestServerProviderHttpTraceOptions> options,
    ILogger<LlmTestServerProviderHttpTraceFilter> logger
) : IEndpointFilter
{
    private static readonly ProviderRoute[] _providerRoutes =
    [
        new(LlmTestServerProviderRouteNamespaces.SystemOne, LlmTestServerCompatibilityTags.SystemOne),
        new(LlmTestServerProviderRouteNamespaces.Cloudflare, LlmTestServerCompatibilityTags.Cloudflare),
        new(LlmTestServerProviderRouteNamespaces.Anthropic, LlmTestServerCompatibilityTags.Anthropic),
        new(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerCompatibilityTags.AzureOpenAI),
        new(LlmTestServerProviderRouteNamespaces.Bedrock, LlmTestServerCompatibilityTags.Bedrock),
        new(LlmTestServerProviderRouteNamespaces.Cohere, LlmTestServerCompatibilityTags.Cohere),
        new(LlmTestServerProviderRouteNamespaces.DeepSeek, LlmTestServerCompatibilityTags.DeepSeek),
        new(LlmTestServerProviderRouteNamespaces.Gemini, LlmTestServerCompatibilityTags.Gemini),
        new(LlmTestServerProviderRouteNamespaces.Groq, LlmTestServerCompatibilityTags.Groq),
        new(
            LlmTestServerProviderRouteNamespaces.MicrosoftFoundry,
            LlmTestServerCompatibilityTags.MicrosoftFoundry
        ),
        new(LlmTestServerProviderRouteNamespaces.Mistral, LlmTestServerCompatibilityTags.Mistral),
        new(LlmTestServerProviderRouteNamespaces.Ollama, LlmTestServerCompatibilityTags.Ollama),
        new(LlmTestServerProviderRouteNamespaces.OpenAI, LlmTestServerCompatibilityTags.OpenAI),
        new(LlmTestServerProviderRouteNamespaces.OpenRouter, LlmTestServerCompatibilityTags.OpenRouter),
        new(LlmTestServerProviderRouteNamespaces.Perplexity, LlmTestServerCompatibilityTags.Perplexity),
    ];

    private readonly LlmTestServerProviderHttpTraceOptions _options = options.Value;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        var provider = FindProvider(httpContext.Request.Path);
        if (provider is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        var session = TryStartSession(httpContext, provider.Value);
        if (session is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        var runtimeScope = TryBeginRuntimeScope(session.RequestId);
        try
        {
            object? result;
            try
            {
                result = await next(context).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                session.CaptureRequestBestEffort();
                await session.CompleteBestEffortAsync(exception).ConfigureAwait(false);
                throw;
            }

            session.CaptureRequestBestEffort();
            if (result is IResult httpResult)
            {
                return new LlmTestServerProviderHttpTraceResult(httpResult, session);
            }

            session.RegisterCompletionFallback();
            return result;
        }
        finally
        {
            DisposeRuntimeScopeBestEffort(runtimeScope, session.RequestId);
        }
    }

    private TraceSession? TryStartSession(HttpContext context, ProviderRoute provider)
    {
        var requestId = context.TraceIdentifier;
        var requestContentType = LlmTestServerProviderHttpTracePayloads.NormalizeContentType(
            context.Request.ContentType
        );
        var requestCaptureLimit = LlmTestServerProviderHttpTracePayloads.ShouldCaptureBody(
            requestContentType
        )
            ? _options.MaxPreviewBytes
            : 0;
        var originalRequestBody = context.Request.Body;
        var originalResponseBody = context.Response.Body;
        var requestTee = new LlmTestServerBoundedReadTeeStream(
            originalRequestBody,
            requestCaptureLimit
        );
        var responseTee = new LlmTestServerBoundedWriteTeeStream(
            originalResponseBody,
            _options.MaxPreviewBytes
        );
        var endpoint = context.GetEndpoint();
        var route = (endpoint as RouteEndpoint)?.RoutePattern.RawText;
        var operationId = endpoint
            ?.Metadata
            .GetMetadata<LlmTestServerProviderOperationMetadata>()
            ?.OperationId;

        try
        {
            context.Request.Body = requestTee;
            context.Response.Body = responseTee;
            var session = new TraceSession(
                context,
                traceStore,
                runtime,
                logger,
                requestId,
                requestContentType,
                _options.MaxPreviewBytes,
                route,
                operationId,
                originalRequestBody,
                originalResponseBody,
                requestTee,
                responseTee
            );
            LlmTestServerProviderHttpTraceExtensions.SetLlmTestServerProviderHttpTraceId(context, requestId);
            traceStore.Start(
                new LlmTestServerProviderHttpTrace
                {
                    RequestId = requestId,
                    SessionId = context.Request.Headers[ManagedCode.LlmTestServer.Sessions.LlmTestServerSessionHeaders.Id].ToString() is { Length: > 0 } sessionId ? sessionId : null,
                    ProviderId = provider.ProviderId,
                    ProviderNamespace = provider.Namespace,
                    Method = context.Request.Method,
                    Path = context.Request.PathBase.Add(context.Request.Path).Value ?? string.Empty,
                    Route = route,
                    OperationId = operationId,
                    StartedAt = session.StartedAt,
                    TransportState = LlmTestServerProviderHttpTraceTransportState.InProgress,
                    RequestContentType = requestContentType,
                    RequestByteCount = context.Request.ContentLength,
                    ModelId = ReadRouteModelId(context),
                }
            );
            TrySetRequestIdHeader(context, requestId);
            return session;
        }
        catch (Exception exception)
        {
            TryRestoreBody(context, originalRequestBody, originalResponseBody);
            TryDispose(requestTee);
            TryDispose(responseTee);
            LogFailure(exception, "start", requestId);
            return null;
        }
    }

    private IDisposable? TryBeginRuntimeScope(string requestId)
    {
        if (runtime is not ILlmTestServerRuntimeRequestScope runtimeRequestScope)
        {
            return null;
        }

        try
        {
            return runtimeRequestScope.BeginRequest(requestId);
        }
        catch (Exception exception)
        {
            LogFailure(exception, "runtime correlation start", requestId);
            return null;
        }
    }

    private void DisposeRuntimeScopeBestEffort(IDisposable? runtimeScope, string requestId)
    {
        if (runtimeScope is null)
        {
            return;
        }

        try
        {
            runtimeScope.Dispose();
        }
        catch (Exception exception)
        {
            LogFailure(exception, "runtime correlation disposal", requestId);
        }
    }

    private void TrySetRequestIdHeader(HttpContext context, string requestId)
    {
        try
        {
            context.Response.Headers[_options.RequestIdHeaderName] = requestId;
        }
        catch (Exception exception)
        {
            LogFailure(exception, "request id response header", requestId);
        }
    }

    private void LogFailure(Exception exception, string phase, string requestId)
    {
        logger.LogWarning(
            exception,
            "LLM Test Server provider HTTP tracing failed during {Phase} for request {RequestId}; provider behavior continues unchanged.",
            phase,
            requestId
        );
    }

    private static void TryRestoreBody(
        HttpContext context,
        Stream requestBody,
        Stream responseBody
    )
    {
        try
        {
            context.Request.Body = requestBody;
        }
        catch (Exception)
        {
        }

        try
        {
            context.Response.Body = responseBody;
        }
        catch (Exception)
        {
        }
    }

    private static void TryDispose(IDisposable disposable)
    {
        try
        {
            disposable.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private static ProviderRoute? FindProvider(PathString path)
    {
        foreach (var provider in _providerRoutes)
        {
            if (
                path.Equals(provider.Namespace, StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments(provider.Namespace, StringComparison.OrdinalIgnoreCase)
            )
            {
                return provider;
            }
        }

        return null;
    }

    private static string? ReadRouteModelId(HttpContext context)
    {
        return ReadRouteValue(context, "model")
            ?? ReadRouteValue(context, "modelId")
            ?? ReadRouteValue(context, "deployment");
    }

    private static string? ReadRouteValue(HttpContext context, string name)
    {
        return context.Request.RouteValues.TryGetValue(name, out var value)
            && !string.IsNullOrWhiteSpace(value?.ToString())
            ? value.ToString()
            : null;
    }

    private sealed class TraceSession(
        HttpContext context,
        ILlmTestServerProviderHttpTraceStore traceStore,
        ILlmTestServerRuntime runtime,
        ILogger logger,
        string requestId,
        string? requestContentType,
        int maxPreviewBytes,
        string? route,
        string? operationId,
        Stream originalRequestBody,
        Stream originalResponseBody,
        LlmTestServerBoundedReadTeeStream requestTee,
        LlmTestServerBoundedWriteTeeStream responseTee
    )
    {
        private readonly long _startedTimestamp = Stopwatch.GetTimestamp();
        private int _requestCaptured;
        private int _completed;

        public string RequestId => requestId;

        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

        public void CaptureRequestBestEffort()
        {
            if (Interlocked.Exchange(ref _requestCaptured, 1) != 0)
            {
                return;
            }

            RestoreRequestBodyBestEffort();
            try
            {
                var observedBytes = requestTee.BytesObserved;
                var expectedBytes = context.Request.ContentLength;
                var incomplete = expectedBytes is > 0 && observedBytes < expectedBytes;
                var capturedBytes = requestTee.GetCapturedBytes();
                var preview = TryCreateMultipartPreview(expectedBytes ?? observedBytes)
                    ?? LlmTestServerProviderHttpTracePayloads.CreateRequestPreview(
                        capturedBytes,
                        requestContentType,
                        expectedBytes ?? observedBytes,
                        requestTee.IsTruncated || incomplete
                    );
                traceStore.TryCaptureRequest(
                    requestId,
                    new LlmTestServerProviderHttpTraceRequestCapture
                    {
                        ByteCount = preview.ByteCount,
                        Preview = preview.Preview,
                        PreviewTruncated = preview.IsTruncated,
                        ModelId = preview.ModelId,
                    }
                );

                if (requestTee.CaptureFailed)
                {
                    LogCaptureFailure("request body capture");
                }
            }
            catch (Exception exception)
            {
                LogFailure(exception, "request capture");
            }
            finally
            {
                DisposeBestEffort(requestTee, "request capture disposal");
            }
        }

        private LlmTestServerProviderHttpPayloadPreview? TryCreateMultipartPreview(long? byteCount)
        {
            if (
                requestContentType?.StartsWith(
                    "multipart/",
                    StringComparison.OrdinalIgnoreCase
                ) != true
            )
            {
                return null;
            }

            try
            {
                var form = context.Features.Get<IFormFeature>()?.Form;
                return form is null
                    ? null
                    : LlmTestServerProviderHttpTracePayloads.CreateMultipartRequestPreview(
                        form,
                        requestContentType,
                        byteCount,
                        maxPreviewBytes
                    );
            }
            catch (Exception exception)
            {
                LogFailure(exception, "cached multipart form summary");
                return null;
            }
        }

        public void RegisterCompletionFallback()
        {
            try
            {
                context.Response.OnCompleted(
                    static state => ((TraceSession)state).CompleteBestEffortAsync(null),
                    this
                );
            }
            catch (Exception exception)
            {
                LogFailure(exception, "completion callback registration");
                _ = CompleteBestEffortAsync(null);
            }
        }

        public Task CompleteBestEffortAsync(Exception? exception)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return Task.CompletedTask;
            }

            CaptureRequestBestEffort();
            RestoreResponseBodyBestEffort();

            var transportState = ClassifyTransportState(context, exception);
            int? statusCode = transportState == LlmTestServerProviderHttpTraceTransportState.Completed
                || context.Response.HasStarted
                ? context.Response.StatusCode
                : null;
            EnrichFromRuntimeEventBestEffort();

            try
            {
                var responseContentType = LlmTestServerProviderHttpTracePayloads.NormalizeContentType(
                    context.Response.ContentType
                );
                var responseBytes = responseTee.GetCapturedBytes();
                var responsePreview = LlmTestServerProviderHttpTracePayloads.CreateResponsePreview(
                    responseBytes, responseContentType, responseTee.BytesObserved, responseTee.IsTruncated);
                if (statusCode >= 400)
                {
                    EnrichFromErrorPayloadBestEffort(responsePreview);
                }
                traceStore.TryComplete(
                    requestId,
                    new LlmTestServerProviderHttpTraceCompletion
                    {
                        CompletedAt = DateTimeOffset.UtcNow,
                        DurationMilliseconds = Stopwatch
                            .GetElapsedTime(_startedTimestamp)
                            .TotalMilliseconds,
                        StatusCode = statusCode,
                        TransportState = transportState,
                        Route = route,
                        OperationId = operationId,
                        IsStreaming = LlmTestServerProviderHttpTracePayloads.IsStreamingResponse(
                            responseContentType,
                            route,
                            responseTee.SuccessfulFlushCount
                        ),
                        ResponseContentType = responseContentType,
                        ResponseByteCount = responseTee.BytesObserved,
                        ResponsePreview = responsePreview,
                        ResponsePreviewTruncated = responseTee.IsTruncated,
                        TransportError = exception?.GetType().Name
                            ?? (transportState == LlmTestServerProviderHttpTraceTransportState.Aborted
                                ? "RequestAborted"
                                : null),
                    }
                );

                if (responseTee.CaptureFailed)
                {
                    LogCaptureFailure("response body capture");
                }
            }
            catch (Exception finalizationException)
            {
                LogFailure(finalizationException, "finalization");
                TryCompleteWithoutPayload(transportState, statusCode, exception);
            }
            finally
            {
                DisposeBestEffort(responseTee, "response capture disposal");
            }

            return Task.CompletedTask;
        }

        private void EnrichFromErrorPayloadBestEffort(string? preview)
        {
            if (string.IsNullOrWhiteSpace(preview))
            {
                return;
            }
            try
            {
                using var document = JsonDocument.Parse(preview);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return;
                }
                var error = root;
                if (root.TryGetProperty("error", out var native) && native.ValueKind == JsonValueKind.Object)
                {
                    error = native;
                }
                else if (root.TryGetProperty("errors", out var errors)
                    && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
                    && errors[0].ValueKind == JsonValueKind.Object)
                {
                    error = errors[0];
                }
                var code = error.TryGetProperty("code", out var value) ? value
                    : error.TryGetProperty("type", out value) ? value : default;
                traceStore.TryEnrich(requestId, new LlmTestServerProviderHttpTraceEnrichment
                {
                    ErrorCode = code.ValueKind == JsonValueKind.String ? code.GetString()
                        : code.ValueKind == JsonValueKind.Number ? code.GetRawText() : null,
                    ErrorMessage = error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                        ? message.GetString() : null,
                });
            }
            catch (JsonException)
            {
                // A truncated or non-JSON diagnostic preview must not affect the actual provider response.
            }
        }

        private void EnrichFromRuntimeEventBestEffort()
        {
            try
            {
                if (runtime is not ILlmTestServerRuntimeEventLookup lookup
                    || !lookup.TryGetEvent(requestId, out var runtimeEvent)
                    || runtimeEvent is null)
                {
                    return;
                }

                traceStore.TryEnrich(
                    requestId,
                    new LlmTestServerProviderHttpTraceEnrichment
                    {
                        ModelId = runtimeEvent.ModelId,
                        ScenarioId = runtimeEvent.ScenarioId,
                        RuntimeEventKind = runtimeEvent.Kind,
                        RuntimeMessage = runtimeEvent.Message,
                        RuntimeResponse = runtimeEvent.Response,
                        Usage = runtimeEvent.Usage,
                        Messages = runtimeEvent.Messages,
                        StreamChunks = runtimeEvent.StreamChunks,
                        IsStreaming = runtimeEvent.IsStreaming,
                        RuntimePayloadTruncated = runtimeEvent.PayloadTruncated,
                    }
                );
            }
            catch (Exception exception)
            {
                LogFailure(exception, "runtime event enrichment");
            }
        }

        private void TryCompleteWithoutPayload(
            LlmTestServerProviderHttpTraceTransportState transportState,
            int? statusCode,
            Exception? exception
        )
        {
            try
            {
                traceStore.TryComplete(
                    requestId,
                    new LlmTestServerProviderHttpTraceCompletion
                    {
                        CompletedAt = DateTimeOffset.UtcNow,
                        DurationMilliseconds = Stopwatch
                            .GetElapsedTime(_startedTimestamp)
                            .TotalMilliseconds,
                        StatusCode = statusCode,
                        TransportState = transportState,
                        Route = route,
                        OperationId = operationId,
                        ResponseContentType = LlmTestServerProviderHttpTracePayloads.NormalizeContentType(
                            context.Response.ContentType
                        ),
                        ResponseByteCount = responseTee.BytesObserved,
                        ResponsePreviewTruncated = responseTee.IsTruncated,
                        TransportError = exception?.GetType().Name,
                    }
                );
            }
            catch (Exception fallbackException)
            {
                LogFailure(fallbackException, "fallback finalization");
            }
        }

        private void RestoreRequestBodyBestEffort()
        {
            try
            {
                context.Request.Body = originalRequestBody;
            }
            catch (Exception exception)
            {
                LogFailure(exception, "request stream restoration");
            }
        }

        private void RestoreResponseBodyBestEffort()
        {
            try
            {
                context.Response.Body = originalResponseBody;
            }
            catch (Exception exception)
            {
                LogFailure(exception, "response stream restoration");
            }
        }

        private void DisposeBestEffort(IDisposable disposable, string phase)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                LogFailure(exception, phase);
            }
        }

        private void LogCaptureFailure(string phase)
        {
            logger.LogWarning(
                "LLM Test Server provider HTTP tracing could not retain all bytes during {Phase} for request {RequestId}; provider behavior was not affected.",
                phase,
                requestId
            );
        }

        private void LogFailure(Exception exception, string phase)
        {
            logger.LogWarning(
                exception,
                "LLM Test Server provider HTTP tracing failed during {Phase} for request {RequestId}; provider behavior continues unchanged.",
                phase,
                requestId
            );
        }

        private static LlmTestServerProviderHttpTraceTransportState ClassifyTransportState(
            HttpContext context,
            Exception? exception
        )
        {
            if (
                context.RequestAborted.IsCancellationRequested
                || exception is ConnectionAbortedException
            )
            {
                return LlmTestServerProviderHttpTraceTransportState.Aborted;
            }

            if (exception is OperationCanceledException)
            {
                return LlmTestServerProviderHttpTraceTransportState.Cancelled;
            }

            return exception is null
                ? LlmTestServerProviderHttpTraceTransportState.Completed
                : LlmTestServerProviderHttpTraceTransportState.Faulted;
        }
    }

    private sealed class LlmTestServerProviderHttpTraceResult(
        IResult result,
        TraceSession session
    ) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            Exception? exception = null;
            try
            {
                await result.ExecuteAsync(httpContext).ConfigureAwait(false);
            }
            catch (Exception caught)
            {
                exception = caught;
                throw;
            }
            finally
            {
                await session.CompleteBestEffortAsync(exception).ConfigureAwait(false);
            }
        }
    }

    private readonly record struct ProviderRoute(string Namespace, string ProviderId);
}
