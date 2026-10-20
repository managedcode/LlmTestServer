namespace ManagedCode.LlmTestServer.Runtime;

public sealed record LlmTestServerTokenUsage
{
    public int InputTokens { get; init; }

    public int CachedInputTokens { get; init; }

    public int CacheCreationInputTokens { get; init; }

    public int OutputTokens { get; init; }

    public int ReasoningTokens { get; init; }

    public int TotalTokens { get; init; }

    public LlmTestServerInputTokenComposition? InputComposition { get; init; }
}
