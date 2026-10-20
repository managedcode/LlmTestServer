using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Hosting;

public sealed record LlmTestServerProviderHttpTraceCompletion
{
    public string? RetryAfter { get; init; }

    public bool? TerminalMarkerObserved { get; init; }

    public double? CompletedMonotonicSeconds { get; init; }

    public DateTimeOffset CompletedAt { get; init; }

    public double DurationMilliseconds { get; init; }

    public int? StatusCode { get; init; }

    public LlmTestServerProviderHttpTraceTransportState TransportState { get; init; }

    public string? Route { get; init; }

    public string? OperationId { get; init; }

    public bool IsStreaming { get; init; }

    public string? ResponseContentType { get; init; }

    public long ResponseByteCount { get; init; }

    public string? ResponsePreview { get; init; }

    public bool ResponsePreviewTruncated { get; init; }

    public string? TransportError { get; init; }
}

public sealed record LlmTestServerProviderHttpTraceRequestCapture
{
    public long? ByteCount { get; init; }

    public string? Preview { get; init; }

    public bool PreviewTruncated { get; init; }

    public string? ModelId { get; init; }
}

public sealed record LlmTestServerProviderHttpTraceEnrichment
{
    public string? WireClockMode { get; init; }
    public DateTimeOffset? WireCreatedAt { get; init; }
    public long? WireSequence { get; init; }
    public IReadOnlyList<LlmTestServerToolCall>? ResponseToolCalls { get; init; }
    public string? ConversationId { get; init; }
    public string? StepId { get; init; }
    public int? TurnIndex { get; init; }
    public bool? IsReplay { get; init; }

    public string? OperationId { get; init; }

    public string? ModelId { get; init; }

    public string? ScenarioId { get; init; }

    public LlmTestServerEventKind? RuntimeEventKind { get; init; }

    public string? RuntimeMessage { get; init; }

    public string? RuntimeResponse { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public LlmTestServerTokenUsage? Usage { get; init; }

    public IReadOnlyList<LlmTestServerMessage>? Messages { get; init; }

    public IReadOnlyList<string>? StreamChunks { get; init; }

    public bool? IsStreaming { get; init; }

    public bool? RuntimePayloadTruncated { get; init; }
}

public sealed class LlmTestServerProviderHttpTraceOptions
{
    public const int DefaultMaxEntries = 500;
    public const int DefaultMaxPreviewBytes = 64 * 1024;
    public const int DefaultMaxRetainedBytes = 16 * 1024 * 1024;
    public const string DefaultRequestIdHeaderName = "x-llm-test-server-request-id";

    public int MaxEntries { get; set; } = DefaultMaxEntries;

    public int MaxPreviewBytes { get; set; } = DefaultMaxPreviewBytes;

    public int MaxRetainedBytes { get; set; } = DefaultMaxRetainedBytes;

    public string RequestIdHeaderName { get; set; } = DefaultRequestIdHeaderName;
}

public interface ILlmTestServerProviderHttpTraceStore
{
    long RetainedByteCount { get; }

    IReadOnlyList<LlmTestServerProviderHttpTrace> GetSnapshot();

    LlmTestServerRequestSnapshot GetEvidenceSnapshot();

    void Start(LlmTestServerProviderHttpTrace trace);

    bool TryCaptureRequest(string requestId, LlmTestServerProviderHttpTraceRequestCapture capture);

    bool TryComplete(string requestId, LlmTestServerProviderHttpTraceCompletion completion);

    bool TryEnrich(string requestId, LlmTestServerProviderHttpTraceEnrichment enrichment);

    void Reset();
}
