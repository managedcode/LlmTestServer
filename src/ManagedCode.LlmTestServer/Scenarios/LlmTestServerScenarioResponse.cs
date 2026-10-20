namespace ManagedCode.LlmTestServer.Scenarios;

public sealed record LlmTestServerScenarioResponse
{
    public List<LlmTestServerToolCall> ToolCalls { get; init; } = [];

    public string Content { get; init; } = string.Empty;

    public List<string> StreamChunks { get; init; } = [];

    public LlmTestServerScenarioError? Error { get; init; }

    public int DelayMilliseconds { get; init; }
}
