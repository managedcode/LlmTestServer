using System.Security.Cryptography;
using System.Text.Json;
using ManagedCode.LlmTestServer.Conversations;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Runtime;

public sealed partial class LlmTestServerRuntime
{
    public LlmTestServerMatchExplanation Explain(LlmTestServerChatRequest request, string providerNamespace,
        string? conversationId = null, string? bearerToken = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Messages);
        ArgumentNullException.ThrowIfNull(request.Tools);
        foreach (var tool in request.Tools)
        {
            ArgumentNullException.ThrowIfNull(tool);
            ArgumentException.ThrowIfNullOrWhiteSpace(tool.Name);
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(providerNamespace);
        foreach (var message in request.Messages)
        {
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(message.Content);
            ArgumentNullException.ThrowIfNull(message.ToolCalls);
            foreach (var call in message.ToolCalls)
            {
                ArgumentNullException.ThrowIfNull(call);
                ArgumentException.ThrowIfNullOrWhiteSpace(call.Id);
                ArgumentException.ThrowIfNullOrWhiteSpace(call.Name);
                ArgumentNullException.ThrowIfNull(call.ArgumentsJson);
            }
        }
        lock (_gate)
        {
            var key = new ConversationKey(providerNamespace, conversationId ?? "default", TokenHash(bearerToken));
            var historyError = RestoreChatResponse(ref request, bearerToken);
            if (historyError is not null)
            {
                return new() { ConversationId = key.ConversationId, TurnIndex = LogicalTurn(request.Messages), ErrorCode = historyError.ErrorCode };
            }
            var selected = SelectChatScenario(request, bearerToken, key);
            var candidates = new List<LlmTestServerMatchCandidate>();
            var selectedScenario = selected?.Id;
            string? selectedStep = null;
            foreach (var scenario in GetChatScenarios(_configuration))
            {
                var baseFailures = new List<string>();
                if (!string.Equals(scenario.ModelId, request.ModelId, StringComparison.OrdinalIgnoreCase)) { baseFailures.Add("model"); }
                else if (!ScenarioMatches(scenario, request)) { baseFailures.Add("history.messages"); }
                if (!HasRequiredToken(scenario.RequiredBearerToken, bearerToken)) { baseFailures.Add("authorization"); }
                if (scenario.Conversation is not { } graph)
                {
                    var matches = baseFailures.Count == 0;
                    candidates.Add(new()
                    {
                        ScenarioId = scenario.Id,
                        IsMatch = matches,
                        IsSelected = ReferenceEquals(selected, scenario),
                        IsShadowed = matches && !ReferenceEquals(selected, scenario),
                        Mismatches = baseFailures
                    });
                    continue;
                }
                var state = FindConversation(scenario, key);
                var fingerprint = ConversationFingerprint(request);
                var replay = state?.LastResult is { IsSuccess: true } &&
                    (graph.ReplaySuccessfulRequests && state.Fingerprint == fingerprint
                    || state.Terminal && graph.Exhaustion == LlmTestServerConversationExhaustion.RepeatLastResponse);
                var restart = state?.Terminal == true && graph.Exhaustion == LlmTestServerConversationExhaustion.Restart && !replay;
                var stepIds = replay ? new[] { state!.LastStep!.Id }
                    : state is null || restart ? new[] { graph.StartStepId } : state.NextSteps.Count == 0 && state.LastStep is not null ? new[] { state.LastStep.Id } : state.NextSteps;
                var selectedTargetId = replay ? state!.LastStep!.Id : stepIds.FirstOrDefault(id =>
                    MatchStep(graph.Steps.First(step => step.Id == id).Match, request).Count == 0);
                foreach (var id in stepIds)
                {
                    var step = graph.Steps.First(item => item.Id == id);
                    var failures = new List<string>(baseFailures);
                    if (state?.Busy == true) { failures.Add("state.busy"); }
                    if (!replay)
                    {
                        failures.AddRange(MatchStep(step.Match, request));
                        if (!restart && state?.Terminal == true) { failures.Add("state.completed"); }
                        if (!restart && state is not null && state.Executions >= graph.MaxSteps) { failures.Add("state.executionLimit"); }
                        if (!restart && state?.LastResult is { IsSuccess: true } && LogicalTurn(request.Messages) <= state.LastTurn)
                        { failures.Add("turn.notAdvanced"); }
                    }
                    if (!request.PopulateCacheOnly && ValidateFixture(request, step.Response) is not null) { failures.Add("fixture.mismatch"); }
                    var matches = failures.Count == 0;
                    var select = ReferenceEquals(selected, scenario) && id == selectedTargetId && matches;
                    candidates.Add(new()
                    {
                        ScenarioId = scenario.Id,
                        StepId = id,
                        IsMatch = matches,
                        IsSelected = select,
                        IsShadowed = matches && !select,
                        Mismatches = failures
                    });
                    if (select) { selectedScenario = scenario.Id; selectedStep = id; }
                }
            }
            return new()
            {
                ConversationId = key.ConversationId,
                TurnIndex = LogicalTurn(request.Messages),
                SelectedScenarioId = selectedScenario,
                SelectedStepId = selectedStep,
                Candidates = candidates
            };
        }
    }

    public IReadOnlyList<LlmTestServerConversationStatus> GetConversations()
    {
        lock (_gate)
        {
            return _conversations.SelectMany(pair => pair.Value.Select(entry => new LlmTestServerConversationStatus
            {
                ScenarioId = pair.Key.Id,
                ProviderNamespace = entry.Key.Provider,
                ConversationId = entry.Key.ConversationId,
                IsBusy = entry.Value.Busy,
                IsTerminal = entry.Value.Terminal,
                ExecutedSteps = entry.Value.Executions,
                LastTurnIndex = entry.Value.LastTurn,
                NextSteps = [.. entry.Value.NextSteps],
                VisitedSteps = pair.Key.Conversation!.Steps.Where(step => entry.Value.Visited.Contains(step.Id)).Select(step => step.Id).ToList(),
                UnvisitedSteps = pair.Key.Conversation!.Steps.Where(step => !entry.Value.Visited.Contains(step.Id)).Select(step => step.Id).ToList(),
            })).ToArray();
        }
    }

    private static string ConversationFingerprint(LlmTestServerChatRequest request)
    {
        return Convert.ToHexString(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request with { RequestId = null, Stream = false, PreviousResponseId = null })));
    }
}
