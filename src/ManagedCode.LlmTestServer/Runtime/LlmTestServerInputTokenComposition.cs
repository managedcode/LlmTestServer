namespace ManagedCode.LlmTestServer.Runtime;

public sealed record LlmTestServerInputTokenComposition
{
    public int SystemTokens { get; init; }

    public int UserTokens { get; init; }

    public int AssistantTokens { get; init; }

    public int ToolCallTokens { get; init; }

    public int ToolResultTokens { get; init; }

    public int ToolDefinitionTokens { get; init; }

    public int OtherTokens { get; init; }

    public int TotalTokens => SystemTokens + UserTokens + AssistantTokens + ToolCallTokens + ToolResultTokens
        + ToolDefinitionTokens + OtherTokens;
}
