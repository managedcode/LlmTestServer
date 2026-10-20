namespace ManagedCode.LlmTestServer.Models;

public sealed record LlmTestServerModel
{
    public string Id { get; init; } = LlmTestServerKnownModelIds.Gpt41Mini;

    public LlmTestServerModelKind Kind { get; init; } = LlmTestServerModelKind.Chat;

    public string OwnedBy { get; init; } = "llm-test-server";

    public ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionProvider DecisionProvider { get; init; }

    public int ReasoningTokens { get; init; }
}
