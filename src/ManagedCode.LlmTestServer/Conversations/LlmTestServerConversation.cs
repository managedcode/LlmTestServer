using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Conversations;

public static class LlmTestServerConversationHeaders
{
    public const string Id = "X-Llm-Test-Server-Conversation";
}

public enum LlmTestServerConversationExhaustion { Reject, RepeatLastResponse, Restart }

public sealed record LlmTestServerConversation
{
    public string StartStepId { get; init; } = string.Empty;
    public int MaxSteps { get; init; } = 64;
    public bool ReplaySuccessfulRequests { get; init; } = true;
    public LlmTestServerConversationExhaustion Exhaustion { get; init; }
    public List<LlmTestServerConversationStep> Steps { get; init; } = [];
}

public sealed record LlmTestServerConversationStep
{
    public string Id { get; init; } = string.Empty;
    public LlmTestServerConversationMatch Match { get; init; } = new();
    public LlmTestServerScenarioResponse Response { get; init; } = new();
    public List<string> NextSteps { get; init; } = [];
    public bool Terminal { get; init; }
}

public sealed record LlmTestServerConversationMatch
{
    public string? LatestRole { get; init; }
    public string? TextEquals { get; init; }
    public string? TextContains { get; init; }
    public int? TurnIndex { get; init; }
    public string? ToolName { get; init; }
    public string? ToolCallId { get; init; }
    public string? ToolArgumentsJson { get; init; }
    public List<LlmTestServerJsonFieldMatch> JsonFields { get; init; } = [];
}

public sealed record LlmTestServerJsonFieldMatch
{
    public string Path { get; init; } = "$";
    public string EqualsJson { get; init; } = "null";
}
