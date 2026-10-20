using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Diagnostics;

public sealed record LlmTestServerProviderHttpTrace
{
    public string? WireClockMode { get; init; }
    public DateTimeOffset? WireCreatedAt { get; init; }
    public long? WireSequence { get; init; }
    public long Sequence { get; init; }
    public string? RetryGroupId { get; init; }
    public string? RetryAfter { get; init; }
    public bool? TerminalMarkerObserved { get; init; }
    public IReadOnlyList<LlmTestServerToolCall> ResponseToolCalls { get; init; } = [];
    public string? ConversationId { get; init; }
    public string? StepId { get; init; }
    public int? TurnIndex { get; init; }
    public bool IsReplay { get; init; }

    public string? SessionId { get; init; }

    public string RequestId { get; init; } = string.Empty;

    public string ProviderId { get; init; } = string.Empty;

    public string ProviderNamespace { get; init; } = string.Empty;

    public string Method { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string? Route { get; init; }

    public string? OperationId { get; init; }

    public double? StartedMonotonicSeconds { get; init; }

    public double? CompletedMonotonicSeconds { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public double? DurationMilliseconds { get; init; }

    public int? StatusCode { get; init; }

    public bool IsCompleted { get; init; }

    public LlmTestServerProviderHttpTraceTransportState TransportState { get; init; }

    public bool? IsSuccess => !IsCompleted
        ? null
        : TransportState != LlmTestServerProviderHttpTraceTransportState.Completed
            ? false
            : StatusCode is null
                ? null
                : StatusCode < 400;

    public bool IsStreaming { get; init; }

    public string? RequestContentType { get; init; }

    public long? RequestByteCount { get; init; }

    public string? RequestPreview { get; init; }

    public bool RequestPreviewTruncated { get; init; }

    public string? ResponseContentType { get; init; }

    public long ResponseByteCount { get; init; }

    public string? ResponsePreview { get; init; }

    public bool ResponsePreviewTruncated { get; init; }

    public string? ModelId { get; init; }

    public string? ScenarioId { get; init; }

    public LlmTestServerEventKind? RuntimeEventKind { get; init; }

    public string? RuntimeMessage { get; init; }

    public string? RuntimeResponse { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public string? TransportError { get; init; }

    public LlmTestServerTokenUsage? Usage { get; init; }

    public IReadOnlyList<LlmTestServerMessage> Messages { get; init; } = [];

    public IReadOnlyList<string> StreamChunks { get; init; } = [];

    public bool RuntimePayloadTruncated { get; init; }

    public bool RetentionTruncated { get; init; }

    public int RetainedByteCount { get; init; }
}

public enum LlmTestServerProviderHttpTraceTransportState
{
    InProgress,
    Completed,
    Cancelled,
    Aborted,
    Faulted,
}
