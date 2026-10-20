namespace ManagedCode.LlmTestServer.Scenarios;

public sealed record LlmTestServerMessage
{
    public string? ToolCallId { get; init; }

    public string Role { get; init; } = "user";

    public List<LlmTestServerToolCall> ToolCalls { get; init; } = [];

    public string Content { get; init; } = string.Empty;
}
