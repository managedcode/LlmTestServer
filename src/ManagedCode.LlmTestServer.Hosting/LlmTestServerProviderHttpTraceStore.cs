using System.Text;
using System.Text.Json;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Scenarios;
using Microsoft.Extensions.Options;

namespace ManagedCode.LlmTestServer.Hosting;

public sealed class LlmTestServerProviderHttpTraceStore : ILlmTestServerProviderHttpTraceStore
{
    private const int _traceOverheadBytes = 512;
    private const int _messageOverheadBytes = 64;
    private const int _chunkOverheadBytes = 32;
    private const int _maxMetadataBytes = 4 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<string, LlmTestServerProviderHttpTrace> _traces = new(
        StringComparer.Ordinal
    );
    private readonly LinkedList<string> _order = [];
    private readonly int _maxEntries;
    private readonly int _maxPreviewBytes;
    private readonly int _maxRetainedBytes;
    private long _retainedByteCount;
    private long _totalObserved;
    private long _evictedCount;
    private bool _retentionTruncated;

    public LlmTestServerProviderHttpTraceStore(IOptions<LlmTestServerProviderHttpTraceOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _maxEntries = options.Value.MaxEntries;
        _maxPreviewBytes = options.Value.MaxPreviewBytes;
        _maxRetainedBytes = options.Value.MaxRetainedBytes;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_maxPreviewBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(_maxRetainedBytes, _traceOverheadBytes);
    }

    public long RetainedByteCount
    {
        get
        {
            lock (_gate)
            {
                return _retainedByteCount;
            }
        }
    }

    public IReadOnlyList<LlmTestServerProviderHttpTrace> GetSnapshot()
    {
        lock (_gate)
        {
            var snapshot = new LlmTestServerProviderHttpTrace[_order.Count];
            var index = 0;
            for (var node = _order.Last; node is not null; node = node.Previous)
            {
                var trace = _traces[node.Value];
                snapshot[index++] = trace with
                {
                    NativeTools = Array.AsReadOnly(trace.NativeTools.Select(CloneNativeTool).ToArray()),
                    ResponseOutputItems = Array.AsReadOnly(trace.ResponseOutputItems.Select(CloneOutputItem).ToArray()),
                    Messages = Array.AsReadOnly(trace.Messages.Select(message => message with
                    { OutputItems = message.OutputItems.Select(CloneOutputItem).ToList() }).ToArray()),
                };
            }

            return snapshot;
        }
    }

    public LlmTestServerRequestSnapshot GetEvidenceSnapshot()
    {
        lock (_gate)
        {
            var entries = GetSnapshot();
            return new()
            {
                Requests = entries,
                TotalMatched = entries.Count,
                TotalObserved = _totalObserved,
                EvictedCount = _evictedCount,
                RetentionTruncated = _retentionTruncated,
                RetainedByteCount = _retainedByteCount,
                FirstSequence = entries.Count == 0 ? null : entries.Min(entry => entry.Sequence),
                LastSequence = entries.Count == 0 ? null : entries.Max(entry => entry.Sequence)
            };
        }
    }

    public void Start(LlmTestServerProviderHttpTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentException.ThrowIfNullOrWhiteSpace(trace.RequestId);

        lock (_gate)
        {
            if (_traces.TryGetValue(trace.RequestId, out var existing))
            {
                _retainedByteCount -= GetEntryRetainedByteCount(trace.RequestId, existing);
                _order.Remove(trace.RequestId);
                _evictedCount++;
            }

            var retained = BoundTrace(trace with { Sequence = ++_totalObserved });
            _retentionTruncated |= retained.RetentionTruncated || retained.RequestPreviewTruncated || retained.ResponsePreviewTruncated || retained.RuntimePayloadTruncated;
            _traces[trace.RequestId] = retained;
            _order.AddLast(trace.RequestId);
            _retainedByteCount += GetEntryRetainedByteCount(trace.RequestId, retained);
            TrimOldestEntries();
        }
    }

    public bool TryComplete(string requestId, LlmTestServerProviderHttpTraceCompletion completion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(completion);

        lock (_gate)
        {
            if (!_traces.TryGetValue(requestId, out var trace))
            {
                return false;
            }

            ReplaceTrace(
                requestId,
                trace with
                {
                    CompletedAt = completion.CompletedAt,
                    CompletedMonotonicSeconds = completion.CompletedMonotonicSeconds,
                    RetryAfter = completion.RetryAfter,
                    TerminalMarkerObserved = completion.TerminalMarkerObserved,
                    DurationMilliseconds = completion.DurationMilliseconds,
                    StatusCode = completion.StatusCode,
                    IsCompleted = true,
                    TransportState = completion.TransportState,
                    Route = completion.Route ?? trace.Route,
                    OperationId = completion.OperationId ?? trace.OperationId,
                    IsStreaming = completion.IsStreaming || trace.IsStreaming,
                    ResponseContentType = completion.ResponseContentType,
                    ResponseByteCount = completion.ResponseByteCount,
                    ResponsePreview = completion.ResponsePreview,
                    ResponsePreviewTruncated = completion.ResponsePreviewTruncated,
                    TransportError = completion.TransportError,
                }
            );
            return true;
        }
    }

    public bool TryCaptureRequest(
        string requestId,
        LlmTestServerProviderHttpTraceRequestCapture capture
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(capture);

        lock (_gate)
        {
            if (!_traces.TryGetValue(requestId, out var trace))
            {
                return false;
            }

            ReplaceTrace(
                requestId,
                trace with
                {
                    RequestByteCount = capture.ByteCount,
                    RequestPreview = capture.Preview,
                    RequestPreviewTruncated = capture.PreviewTruncated,
                    ModelId = capture.ModelId ?? trace.ModelId,
                }
            );
            return true;
        }
    }

    public bool TryEnrich(string requestId, LlmTestServerProviderHttpTraceEnrichment enrichment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(enrichment);

        lock (_gate)
        {
            if (!_traces.TryGetValue(requestId, out var trace))
            {
                return false;
            }

            ReplaceTrace(
                requestId,
                trace with
                {
                    WireClockMode = enrichment.WireClockMode ?? trace.WireClockMode,
                    WireCreatedAt = enrichment.WireCreatedAt ?? trace.WireCreatedAt,
                    WireSequence = enrichment.WireSequence ?? trace.WireSequence,
                    OperationId = enrichment.OperationId ?? trace.OperationId,
                    ModelId = enrichment.ModelId ?? trace.ModelId,
                    ScenarioId = enrichment.ScenarioId ?? trace.ScenarioId,
                    ConversationId = enrichment.ConversationId ?? trace.ConversationId,
                    StepId = enrichment.StepId ?? trace.StepId,
                    TurnIndex = enrichment.TurnIndex ?? trace.TurnIndex,
                    IsReplay = enrichment.IsReplay ?? trace.IsReplay,
                    RuntimeEventKind = enrichment.RuntimeEventKind ?? trace.RuntimeEventKind,
                    RuntimeMessage = enrichment.RuntimeMessage ?? trace.RuntimeMessage,
                    RuntimeResponse = enrichment.RuntimeResponse ?? trace.RuntimeResponse,
                    ErrorCode = enrichment.ErrorCode ?? trace.ErrorCode,
                    ErrorMessage = enrichment.ErrorMessage ?? trace.ErrorMessage,
                    Usage = enrichment.Usage ?? trace.Usage,
                    Messages = enrichment.Messages ?? trace.Messages,
                    ResponseToolCalls = enrichment.ResponseToolCalls ?? trace.ResponseToolCalls,
                    NativeTools = enrichment.NativeTools ?? trace.NativeTools,
                    ResponseOutputItems = enrichment.ResponseOutputItems ?? trace.ResponseOutputItems,
                    StreamChunks = enrichment.StreamChunks ?? trace.StreamChunks,
                    IsStreaming = enrichment.IsStreaming ?? trace.IsStreaming,
                    RuntimePayloadTruncated =
                        enrichment.RuntimePayloadTruncated ?? trace.RuntimePayloadTruncated,
                }
            );
            return true;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _traces.Clear();
            _order.Clear();
            _retainedByteCount = 0;
            _totalObserved = 0;
            _evictedCount = 0;
            _retentionTruncated = false;
        }
    }

    private void ReplaceTrace(string requestId, LlmTestServerProviderHttpTrace trace)
    {
        var previous = _traces[requestId];
        _retainedByteCount -= GetEntryRetainedByteCount(requestId, previous);
        var retained = BoundTrace(trace);
        _retentionTruncated |= retained.RetentionTruncated || retained.RequestPreviewTruncated || retained.ResponsePreviewTruncated || retained.RuntimePayloadTruncated;
        _traces[requestId] = retained;
        _retainedByteCount += GetEntryRetainedByteCount(requestId, retained);
        TrimOldestEntries();
    }

    private LlmTestServerProviderHttpTrace BoundTrace(LlmTestServerProviderHttpTrace trace)
    {
        var remaining = _maxRetainedBytes - _traceOverheadBytes;
        var retentionTruncated = trace.RetentionTruncated;
        var requestId = RetainRequiredText(
            trace.RequestId,
            _maxMetadataBytes,
            ref remaining,
            ref retentionTruncated
        );
        var providerId = RetainRequiredText(
            trace.ProviderId,
            _maxMetadataBytes,
            ref remaining,
            ref retentionTruncated
        );
        var providerNamespace = RetainRequiredText(
            trace.ProviderNamespace,
            _maxMetadataBytes,
            ref remaining,
            ref retentionTruncated
        );
        var method = RetainRequiredText(
            trace.Method,
            _maxMetadataBytes,
            ref remaining,
            ref retentionTruncated
        );
        var path = RetainRequiredText(
            trace.Path,
            _maxMetadataBytes,
            ref remaining,
            ref retentionTruncated
        );
        var route = RetainText(
            trace.Route,
            _maxMetadataBytes,
            ref remaining,
            ref retentionTruncated
        );
        var operationId = RetainText(
            trace.OperationId,
            _maxMetadataBytes,
            ref remaining,
            ref retentionTruncated
        );
        var requestContentType = RetainText(
            trace.RequestContentType,
            _maxMetadataBytes,
            ref remaining,
            ref retentionTruncated
        );
        var responseContentType = RetainText(
            trace.ResponseContentType,
            _maxMetadataBytes,
            ref remaining,
            ref retentionTruncated
        );
        var modelId = RetainText(
            trace.ModelId,
            _maxMetadataBytes,
            ref remaining,
            ref retentionTruncated
        );
        var sessionId = RetainText(trace.SessionId, _maxMetadataBytes, ref remaining, ref retentionTruncated);
        var conversationId = RetainText(trace.ConversationId, _maxMetadataBytes, ref remaining, ref retentionTruncated);
        var stepId = RetainText(trace.StepId, _maxMetadataBytes, ref remaining, ref retentionTruncated);
        var scenarioId = RetainText(
            trace.ScenarioId,
            _maxMetadataBytes,
            ref remaining,
            ref retentionTruncated
        );
        var errorCode = RetainText(
            trace.ErrorCode,
            _maxMetadataBytes,
            ref remaining,
            ref retentionTruncated
        );
        var transportError = RetainText(
            trace.TransportError,
            _maxMetadataBytes,
            ref remaining,
            ref retentionTruncated
        );
        var responsePreview = RetainText(
            trace.ResponsePreview,
            _maxPreviewBytes,
            ref remaining,
            ref retentionTruncated,
            out var responsePreviewTruncated
        );
        var runtimeMessage = RetainText(
            trace.RuntimeMessage,
            _maxMetadataBytes,
            ref remaining,
            ref retentionTruncated
        );
        var runtimeResponse = RetainText(
            trace.RuntimeResponse,
            _maxPreviewBytes,
            ref remaining,
            ref retentionTruncated
        );
        var errorMessage = RetainText(
            trace.ErrorMessage,
            _maxPreviewBytes,
            ref remaining,
            ref retentionTruncated
        );
        var streamChunks = RetainChunks(
            trace.StreamChunks,
            ref remaining,
            ref retentionTruncated
        );
        var requestPreview = RetainText(
            trace.RequestPreview,
            _maxPreviewBytes,
            ref remaining,
            ref retentionTruncated,
            out var requestPreviewTruncated
        );
        var wireClockMode = RetainText(trace.WireClockMode, _maxMetadataBytes, ref remaining, ref retentionTruncated);
        var retryGroupId = RetainText(trace.RetryGroupId, _maxMetadataBytes, ref remaining, ref retentionTruncated);
        var retryAfter = RetainText(trace.RetryAfter, _maxMetadataBytes, ref remaining, ref retentionTruncated);
        var responseToolCalls = RetainToolCalls(trace.ResponseToolCalls, ref remaining, ref retentionTruncated);
        var nativeTools = RetainNativeTools(trace.NativeTools, ref remaining, ref retentionTruncated);
        var responseOutputItems = RetainOutputItems(trace.ResponseOutputItems, ref remaining, ref retentionTruncated);
        var messages = RetainMessages(
            trace.Messages,
            ref remaining,
            ref retentionTruncated
        );

        return trace with
        {
            RequestId = requestId,
            ProviderId = providerId,
            ProviderNamespace = providerNamespace,
            Method = method,
            Path = path,
            Route = route,
            OperationId = operationId,
            RequestContentType = requestContentType,
            RequestPreview = requestPreview,
            RequestPreviewTruncated =
                trace.RequestPreviewTruncated || requestPreviewTruncated,
            ResponseContentType = responseContentType,
            ResponsePreview = responsePreview,
            ResponsePreviewTruncated =
                trace.ResponsePreviewTruncated || responsePreviewTruncated,
            ModelId = modelId,
            ScenarioId = scenarioId,
            SessionId = sessionId,
            ConversationId = conversationId,
            StepId = stepId,
            RuntimeMessage = runtimeMessage,
            RuntimeResponse = runtimeResponse,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            TransportError = transportError,
            Messages = messages,
            ResponseToolCalls = responseToolCalls,
            NativeTools = nativeTools,
            ResponseOutputItems = responseOutputItems,
            RetryGroupId = retryGroupId,
            WireClockMode = wireClockMode,
            RetryAfter = retryAfter,
            StreamChunks = streamChunks,
            RetentionTruncated = retentionTruncated,
            RetainedByteCount = _maxRetainedBytes - remaining,
        };
    }

    private IReadOnlyList<LlmTestServerMessage> RetainMessages(
        IReadOnlyList<LlmTestServerMessage> source,
        ref int remaining,
        ref bool truncated
    )
    {
        if (source.Count == 0)
        {
            return [];
        }

        var maximumItemCount = remaining / _messageOverheadBytes;
        var retained = new List<LlmTestServerMessage>(Math.Min(source.Count, maximumItemCount));
        for (var index = 0; index < source.Count; index++)
        {
            if (remaining < _messageOverheadBytes)
            {
                truncated = true;
                break;
            }

            remaining -= _messageOverheadBytes;
            var message = source[index];
            var role = RetainRequiredText(
                message.Role,
                _maxMetadataBytes,
                ref remaining,
                ref truncated
            );
            var content = RetainRequiredText(
                message.Content,
                _maxPreviewBytes,
                ref remaining,
                ref truncated
            );
            var callId = RetainText(message.ToolCallId, _maxMetadataBytes, ref remaining, ref truncated);
            var calls = RetainToolCalls(message.ToolCalls, ref remaining, ref truncated);
            var outputItems = RetainOutputItems(message.OutputItems, ref remaining, ref truncated);
            retained.Add(new LlmTestServerMessage { Role = role, Content = content, ToolCallId = callId, ToolCalls = [.. calls], OutputItems = [.. outputItems] });
        }

        if (retained.Count < source.Count)
        {
            truncated = true;
        }

        return retained.Count == 0 ? [] : Array.AsReadOnly(retained.ToArray());
    }

    private IReadOnlyList<LlmTestServerToolCall> RetainToolCalls(IReadOnlyList<LlmTestServerToolCall> source, ref int remaining, ref bool truncated)
    {
        var retained = new List<LlmTestServerToolCall>();
        foreach (var call in source)
        {
            if (remaining < _messageOverheadBytes) { truncated = true; break; }
            remaining -= _messageOverheadBytes;
            var id = RetainRequiredText(call.Id, _maxMetadataBytes, ref remaining, ref truncated);
            var name = RetainRequiredText(call.Name, _maxMetadataBytes, ref remaining, ref truncated);
            var arguments = RetainRequiredText(call.ArgumentsJson, _maxPreviewBytes, ref remaining, ref truncated);
            retained.Add(new() { Id = id, Name = name, ArgumentsJson = arguments });
        }
        return Array.AsReadOnly(retained.ToArray());
    }

    private IReadOnlyList<LlmTestServerNativeToolDeclaration> RetainNativeTools(
        IReadOnlyList<LlmTestServerNativeToolDeclaration> source, ref int remaining, ref bool truncated)
    {
        var retained = new List<LlmTestServerNativeToolDeclaration>();
        foreach (var tool in source)
        {
            var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(tool).LongLength;
            var nestedOverhead = tool.WebSearchOptions is { } options
                ? (long)options.AllowedDomains.Count * _chunkOverheadBytes
                    + (options.UserLocation is null ? 0 : _messageOverheadBytes)
                : 0;
            var cost = _messageOverheadBytes + payloadBytes + nestedOverhead;
            if (payloadBytes > _maxPreviewBytes || cost > remaining) { truncated = true; break; }
            remaining -= (int)cost;
            retained.Add(CloneNativeTool(tool));
        }
        return Array.AsReadOnly(retained.ToArray());
    }

    private IReadOnlyList<LlmTestServerResponseItem> RetainOutputItems(
        IReadOnlyList<LlmTestServerResponseItem> source, ref int remaining, ref bool truncated)
    {
        var retained = new List<LlmTestServerResponseItem>();
        foreach (var item in source)
        {
            var payloadBytes = JsonSerializer.SerializeToUtf8Bytes<LlmTestServerResponseItem>(item).LongLength;
            var nestedOverhead = item switch
            {
                LlmTestServerResponseMessageItem message => (long)message.StreamChunks.Count * _chunkOverheadBytes
                    + (long)message.Citations.Count * _messageOverheadBytes,
                LlmTestServerResponseWebSearchItem web => (long)web.Queries.Count * _chunkOverheadBytes
                    + (long)web.Sources.Count * _messageOverheadBytes,
                LlmTestServerResponseFunctionCallItem => _messageOverheadBytes,
                _ => 0,
            };
            var cost = _messageOverheadBytes + payloadBytes + nestedOverhead;
            if (payloadBytes > _maxPreviewBytes || cost > remaining) { truncated = true; break; }
            remaining -= (int)cost;
            retained.Add(CloneOutputItem(item));
        }
        return Array.AsReadOnly(retained.ToArray());
    }

    private static LlmTestServerNativeToolDeclaration CloneNativeTool(LlmTestServerNativeToolDeclaration tool)
    {
        return tool with
        {
            WebSearchOptions = tool.WebSearchOptions is { } options ? options with
            {
                AllowedDomains = [.. options.AllowedDomains],
                UserLocation = options.UserLocation is { } location ? location with { } : null,
            } : null,
        };
    }

    private static LlmTestServerResponseItem CloneOutputItem(LlmTestServerResponseItem item)
    {
        return item switch
        {
            LlmTestServerResponseMessageItem message => message with
            { StreamChunks = [.. message.StreamChunks], Citations = message.Citations.Select(citation => citation with { }).ToList() },
            LlmTestServerResponseFunctionCallItem function => function with { Call = function.Call with { } },
            LlmTestServerResponseWebSearchItem web => web with
            { Queries = [.. web.Queries], Sources = web.Sources.Select(source => source with { }).ToList() },
            _ => throw new InvalidOperationException("Unsupported structured response item."),
        };
    }

    private IReadOnlyList<string> RetainChunks(
        IReadOnlyList<string> source,
        ref int remaining,
        ref bool truncated
    )
    {
        if (source.Count == 0)
        {
            return [];
        }

        var maximumItemCount = remaining / _chunkOverheadBytes;
        var retained = new List<string>(Math.Min(source.Count, maximumItemCount));
        for (var index = 0; index < source.Count; index++)
        {
            if (remaining < _chunkOverheadBytes)
            {
                truncated = true;
                break;
            }

            remaining -= _chunkOverheadBytes;
            retained.Add(
                RetainRequiredText(
                    source[index],
                    _maxPreviewBytes,
                    ref remaining,
                    ref truncated
                )
            );
        }

        if (retained.Count < source.Count)
        {
            truncated = true;
        }

        return retained.Count == 0 ? [] : Array.AsReadOnly(retained.ToArray());
    }

    private static string? RetainText(
        string? value,
        int maximumBytes,
        ref int remaining,
        ref bool truncated
    )
    {
        return RetainText(value, maximumBytes, ref remaining, ref truncated, out _);
    }

    private static string? RetainText(
        string? value,
        int maximumBytes,
        ref int remaining,
        ref bool truncated,
        out bool valueTruncated
    )
    {
        if (value is null)
        {
            valueTruncated = false;
            return null;
        }

        return RetainRequiredText(
            value,
            maximumBytes,
            ref remaining,
            ref truncated,
            out valueTruncated
        );
    }

    private static string RetainRequiredText(
        string value,
        int maximumBytes,
        ref int remaining,
        ref bool truncated
    )
    {
        return RetainRequiredText(
            value,
            maximumBytes,
            ref remaining,
            ref truncated,
            out _
        );
    }

    private static string RetainRequiredText(
        string value,
        int maximumBytes,
        ref int remaining,
        ref bool truncated,
        out bool valueTruncated
    )
    {
        value ??= string.Empty;
        var allowedBytes = Math.Min(maximumBytes, remaining);
        var byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount <= allowedBytes)
        {
            remaining -= byteCount;
            valueTruncated = false;
            return value;
        }

        truncated = true;
        valueTruncated = true;
        if (allowedBytes == 0)
        {
            return string.Empty;
        }

        const int ellipsisByteCount = 3;
        var prefixBudget = allowedBytes >= ellipsisByteCount
            ? allowedBytes - ellipsisByteCount
            : allowedBytes;
        var prefixLength = FindUtf8PrefixLength(value, prefixBudget, out var prefixBytes);
        remaining -= prefixBytes;
        if (remaining < ellipsisByteCount || allowedBytes < ellipsisByteCount)
        {
            return prefixLength == 0 ? string.Empty : value[..prefixLength];
        }

        remaining -= ellipsisByteCount;
        return string.Concat(value.AsSpan(0, prefixLength), "…");
    }

    private static int FindUtf8PrefixLength(
        string value,
        int maximumBytes,
        out int byteCount
    )
    {
        var index = 0;
        byteCount = 0;
        while (index < value.Length)
        {
            var character = value[index];
            var characterLength = 1;
            var characterBytes = character switch
            {
                <= '\u007f' => 1,
                <= '\u07ff' => 2,
                _ when char.IsHighSurrogate(character)
                    && index + 1 < value.Length
                    && char.IsLowSurrogate(value[index + 1]) => 4,
                _ => 3,
            };
            if (characterBytes == 4)
            {
                characterLength = 2;
            }

            if (byteCount + characterBytes > maximumBytes)
            {
                break;
            }

            byteCount += characterBytes;
            index += characterLength;
        }

        return index;
    }

    private static long GetEntryRetainedByteCount(
        string storageKey,
        LlmTestServerProviderHttpTrace trace
    )
    {
        var storageKeyBytes = string.Equals(storageKey, trace.RequestId, StringComparison.Ordinal)
            ? 0
            : Encoding.UTF8.GetByteCount(storageKey);
        return trace.RetainedByteCount + storageKeyBytes;
    }

    private void TrimOldestEntries()
    {
        while (_order.Count > _maxEntries || _retainedByteCount > _maxRetainedBytes)
        {
            var oldest = _order.First;
            if (oldest is null)
            {
                _retainedByteCount = 0;
                return;
            }

            _order.RemoveFirst();
            if (_traces.Remove(oldest.Value, out var trace))
            {
                _retainedByteCount -= GetEntryRetainedByteCount(oldest.Value, trace);
                _evictedCount++;
            }
        }

        if (_retainedByteCount < 0)
        {
            _retainedByteCount = 0;
        }
    }
}
