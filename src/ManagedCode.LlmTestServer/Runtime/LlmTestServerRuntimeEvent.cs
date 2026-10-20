using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Runtime;

public sealed record LlmTestServerRuntimeEvent
{
    public DateTimeOffset Timestamp { get; init; }

    public LlmTestServerEventKind Kind { get; init; }

    /// <summary>
    /// Optional caller-provided identifier used to correlate this event with its provider request.
    /// </summary>
    public string? RequestId { get; init; }

    public string? ConversationId { get; init; }
    public string? StepId { get; init; }
    public int? TurnIndex { get; init; }
    public bool IsReplay { get; init; }

    public string? ScenarioId { get; init; }

    public string ModelId { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Readable representation of the incoming request that produced this event, when one is
    /// available. Chat messages are also retained as ordered structured data below.
    /// </summary>
    public string? Request { get; init; }

    /// <summary>
    /// Independent, ordered copy of the chat conversation that produced this event. See
    /// <see cref="PayloadTruncated"/> for the exceptional journal-budget case.
    /// </summary>
    public IReadOnlyList<LlmTestServerMessage> Messages { get; init; } = [];

    /// <summary>Independent tool definitions offered in the originating chat request, within the journal payload budget.</summary>
    public IReadOnlyList<LlmTestServerToolDefinition> Tools { get; init; } = [];

    /// <summary>
    /// The response the model returned for this event (matched content or the error text),
    /// when one is available. Lets operators see exactly what the model answered.
    /// </summary>
    public string? Response { get; init; }

    /// <summary>
    /// Whether the originating chat request asked for a streaming response.
    /// </summary>
    public bool IsStreaming { get; init; }

    /// <summary>
    /// Independent, ordered response chunks retained for a successful streaming chat request.
    /// See <see cref="PayloadTruncated"/> for the exceptional journal-budget case.
    /// </summary>
    public IReadOnlyList<string> StreamChunks { get; init; } = [];

    /// <summary>
    /// Indicates that an exceptionally large diagnostic payload was shortened to keep the
    /// in-memory event journal within its retention budget.
    /// </summary>
    public bool PayloadTruncated { get; init; }

    /// <summary>
    /// Provider-neutral prompt-cache behavior selected for the originating chat request.
    /// </summary>
    public LlmTestServerPromptCachePolicy PromptCachePolicy { get; init; }

    /// <summary>
    /// Optional caller-provided prompt-cache key used by the originating chat request.
    /// </summary>
    public string? PromptCacheKey { get; init; }

    /// <summary>
    /// Deterministic token usage observed for this event, when the runtime can derive it.
    /// </summary>
    public LlmTestServerTokenUsage? Usage { get; init; }
}
