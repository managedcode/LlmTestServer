namespace ManagedCode.LlmTestServer.Runtime;

public sealed record LlmTestServerAssertionSummary
{
    public int TotalEvents { get; init; }

    public int Matched { get; init; }

    public int Unmatched { get; init; }

    public int ModelNotFound { get; init; }

    public int AuthFailed { get; init; }

    public int ScenarioExhausted { get; init; }

    public int ErrorsReturned { get; init; }

    public int InputTokens { get; init; }

    public int CachedInputTokens { get; init; }

    public int CacheCreationInputTokens { get; init; }

    public int OutputTokens { get; init; }

    public int ReasoningTokens { get; init; }

    public int TotalTokens { get; init; }

    public List<LlmTestServerRuntimeEvent> Events { get; init; } = [];
}
