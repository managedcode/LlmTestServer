namespace ManagedCode.LlmTestServer.Scenarios;

public sealed record LlmTestServerRequestMatch
{
    public LlmTestServerMatchMode Mode { get; init; } = LlmTestServerMatchMode.Contains;

    public List<LlmTestServerMessage> Messages { get; init; } = [];
}
