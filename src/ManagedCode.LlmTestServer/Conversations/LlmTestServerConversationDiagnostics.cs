using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Conversations;

public sealed record LlmTestServerMatchExplanationRequest
{
    public LlmTestServerChatRequest Request { get; init; } = new();
    public string ProviderNamespace { get; init; } = LlmTestServerProviderRouteNamespaces.OpenAI;
}

public sealed record LlmTestServerMatchExplanation
{
    public string? ErrorCode { get; init; }
    public string ConversationId { get; init; } = "default";
    public int TurnIndex { get; init; }
    public string? SelectedScenarioId { get; init; }
    public string? SelectedStepId { get; init; }
    public List<LlmTestServerMatchCandidate> Candidates { get; init; } = [];
}

public sealed record LlmTestServerMatchCandidate
{
    public string ScenarioId { get; init; } = string.Empty;
    public string? StepId { get; init; }
    public bool IsMatch { get; init; }
    public bool IsSelected { get; init; }
    public bool IsShadowed { get; init; }
    public List<string> Mismatches { get; init; } = [];
}

public sealed record LlmTestServerConversationStatus
{
    public string ScenarioId { get; init; } = string.Empty;
    public string ProviderNamespace { get; init; } = string.Empty;
    public string ConversationId { get; init; } = "default";
    public bool IsBusy { get; init; }
    public bool IsTerminal { get; init; }
    public int ExecutedSteps { get; init; }
    public int LastTurnIndex { get; init; }
    public List<string> NextSteps { get; init; } = [];
    public List<string> VisitedSteps { get; init; } = [];
    public List<string> UnvisitedSteps { get; init; } = [];
}

public interface ILlmTestServerConversationDiagnostics
{
    LlmTestServerMatchExplanation Explain(LlmTestServerChatRequest request, string providerNamespace,
        string? conversationId = null, string? bearerToken = null);
    IReadOnlyList<LlmTestServerConversationStatus> GetConversations();
}
