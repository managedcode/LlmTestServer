using System.Text;
using ManagedCode.LlmTestServer.Conversations;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Runtime;

public sealed partial class LlmTestServerRuntime : ILlmTestServerConversationDiagnostics
{
    private readonly Dictionary<LlmTestServerScenario, Dictionary<ConversationKey, ConversationState>> _conversations = new(ReferenceEqualityComparer.Instance);
    private int _conversationCount;
    private long _conversationBytes;

    private ConversationKey ConversationIdentity(string? token)
    {
        return new(
        _activeRequest.Value?.ProviderNamespace ?? string.Empty,
        _activeRequest.Value?.ConversationId ?? "default", TokenHash(token));
    }

    private ConversationState? FindConversation(LlmTestServerScenario scenario, ConversationKey key)
    {
        return _conversations.TryGetValue(scenario, out var states) && states.TryGetValue(key, out var state) ? state : null;
    }

    private LlmTestServerScenario? SelectChatScenario(LlmTestServerChatRequest request, string? token, ConversationKey? explicitKey = null)
    {
        var key = explicitKey ?? ConversationIdentity(token);
        return GetChatScenarios(_configuration).FirstOrDefault(scenario => ScenarioMatches(scenario, request)
            && (scenario.Conversation is null || FindConversation(scenario, key) is not null
                || MatchStep(scenario.Conversation.Steps.First(step => step.Id == scenario.Conversation.StartStepId).Match, request).Count == 0));
    }

    private LlmTestServerChatResult? PrepareConversation(LlmTestServerScenario scenario, LlmTestServerChatRequest request,
        string? token, out ConversationLease? lease)
    {
        lease = null;
        var graph = scenario.Conversation!;
        var key = ConversationIdentity(token);
        var state = FindConversation(scenario, key);
        var turn = LogicalTurn(request.Messages);
        var fingerprint = ConversationFingerprint(request);
        LlmTestServerChatResult Failure(string code, string message, string? stepId = null)
        {
            var usage = CreateChatUsage(request);
            AddEvent(LlmTestServerEventKind.ErrorReturned, scenario.Id, request.ModelId, message, FormatChatRequest(request), message,
                usage, chatRequest: request, stepId: stepId, logicalTurnIndex: turn);
            return LlmTestServerChatResult.Failure(request.ModelId, 409, code, message, scenario.Id, usage);
        }
        LlmTestServerChatResult Replay()
        {
            var error = ValidateFixture(request, state!.LastStep!.Response);
            if (error is not null) { return Failure("llm_test_server_fixture_mismatch", error, state.LastStep.Id); }
            var result = CopyConversationResult(state.LastResult!);
            AddEvent(LlmTestServerEventKind.Matched, scenario.Id, request.ModelId, "Conversation response replayed.",
                FormatChatRequest(request), result.Content, result.Usage, request, request.Stream ? result.StreamChunks : null,
                state.LastStep.Id, turn, true, result.ToolCalls, wireMetadata: result.WireMetadata);
            return result;
        }
        if (state?.Busy == true) { return Failure("conversation_busy", "A conversation step is already in flight."); }
        if (state?.LastResult is { IsSuccess: true } && graph.ReplaySuccessfulRequests && state.Fingerprint == fingerprint)
        { return Replay(); }
        var restart = false;
        if (state?.Terminal == true)
        {
            if (graph.Exhaustion == LlmTestServerConversationExhaustion.RepeatLastResponse && state.LastResult is { IsSuccess: true }) { return Replay(); }
            if (graph.Exhaustion != LlmTestServerConversationExhaustion.Restart)
            { return Failure("conversation_completed", "The conversation is complete.", state.LastStep?.Id); }
            restart = true;
        }
        if (!restart && state is not null && state.Executions >= graph.MaxSteps)
        { return Failure("conversation_exhausted", "The conversation execution limit was reached."); }
        if (!restart && state?.LastResult is { IsSuccess: true } && turn <= state.LastTurn)
        { return Failure("conversation_turn_not_advanced", "Changed or stale input cannot advance an already successful logical turn."); }
        var next = state is null || restart ? new[] { graph.StartStepId } : state.NextSteps;
        var step = next.Select(id => graph.Steps.First(candidate => candidate.Id == id)).FirstOrDefault(candidate => MatchStep(candidate.Match, request).Count == 0);
        if (step is null) { return Failure("conversation_step_mismatch", "No active conversation step matched the latest input."); }
        if (request.PopulateCacheOnly)
        {
            var cached = CreateChatUsage(request, updatePromptCache: true);
            var usage = cached with { OutputTokens = 0, ReasoningTokens = 0, TotalTokens = cached.InputTokens };
            AddEvent(LlmTestServerEventKind.Matched, scenario.Id, request.ModelId, "Conversation prompt cache populated.", FormatChatRequest(request),
                string.Empty, usage, request, stepId: step.Id, logicalTurnIndex: turn);
            return LlmTestServerChatResult.Success(request.ModelId, scenario.Id, string.Empty, [], usage);
        }
        var fixtureError = ValidateFixture(request, step.Response);
        if (fixtureError is not null) { return Failure("llm_test_server_fixture_mismatch", fixtureError, step.Id); }
        var newState = state is null;
        var baseBytes = state?.BaseBytes ?? 1024L + Bytes(key.Provider) + Bytes(key.ConversationId) + graph.Steps.Count * 128L;
        var resultBytes = ResponseStateBytes(scenario, step.Response);
        var previousBytes = state?.Bytes ?? 0;
        var reservation = baseBytes + Math.Max(state?.ResultBytes ?? 0, resultBytes);
        if (newState && _conversationCount >= _configuration.MaxConversations
            || reservation - previousBytes > _configuration.MaxConversationBytes - _conversationBytes)
        { return Failure("conversation_capacity_exceeded", "The conversation state capacity was exceeded.", step.Id); }
        if (state is null)
        {
            state = new() { BaseBytes = baseBytes };
            if (!_conversations.TryGetValue(scenario, out var states)) { states = []; _conversations.Add(scenario, states); }
            states.Add(key, state);
            _conversationCount++;
        }
        _conversationBytes += reservation - previousBytes;
        state.Bytes = reservation;
        state.Busy = true;
        lease = new(scenario, key, state, step, fingerprint, turn, newState, restart, previousBytes, resultBytes);
        return null;
    }

    private void CommitConversation(ConversationLease lease, LlmTestServerChatResult result)
    {
        var state = lease.State;
        if (lease.Restart) { state.Executions = 0; state.Visited.Clear(); }
        state.Executions++;
        state.Visited.Add(lease.Step.Id);
        state.LastStep = lease.Step;
        state.LastTurn = lease.Turn;
        state.LastResult = CopyConversationResult(result);
        state.Fingerprint = lease.Fingerprint;
        state.NextSteps = lease.Step.NextSteps;
        state.Terminal = lease.Step.Terminal;
        state.Busy = false;
        _conversationBytes += state.BaseBytes + lease.ResultBytes - state.Bytes;
        state.ResultBytes = lease.ResultBytes;
        state.Bytes = state.BaseBytes + lease.ResultBytes;
    }

    private void RollBackResponse(LlmTestServerScenario scenario, int position, long generation, ConversationLease? lease)
    {
        if (lease is null) { RollBackReservedResponse(scenario, position, generation); return; }
        lock (_gate)
        {
            if (_retired || generation != _generation) { return; }
            _conversationBytes += lease.PreviousBytes - lease.State.Bytes;
            if (lease.NewState) { _conversations[scenario].Remove(lease.Key); _conversationCount--; }
            lease.State.Bytes = lease.PreviousBytes;
            lease.State.Busy = false;
        }
    }

    private void ClearConversations() { _conversations.Clear(); _conversationBytes = 0; _conversationCount = 0; }
    private static long Bytes(string value)
    {
        return Encoding.UTF8.GetByteCount(value);
    }

    private static long ResponseStateBytes(LlmTestServerScenario scenario, LlmTestServerScenarioResponse response)
    {
        return 1024L
            + Bytes(scenario.Id) + Bytes(scenario.ModelId) + Bytes(response.Content) + response.StreamChunks.Sum(chunk => Bytes(chunk))
            + response.ToolCalls.Sum(call => Bytes(call.Id) + Bytes(call.Name) + Bytes(call.ArgumentsJson) + 128)
            + (response.Error is { } error ? Bytes(error.Code) + Bytes(error.Message) : 0);
    }

    private static LlmTestServerChatResult CopyConversationResult(LlmTestServerChatResult result)
    {
        return result with { ToolCalls = [.. result.ToolCalls], StreamChunks = [.. result.StreamChunks] };
    }

    private static int LogicalTurn(IEnumerable<LlmTestServerMessage> messages)
    {
        var turns = 1;
        string? previous = null;
        foreach (var message in messages)
        {
            if (message.Role is "system" or "developer") { continue; }
            if (message.Role == "assistant" && previous != "assistant") { turns++; }
            previous = message.Role;
        }
        return turns;
    }

    private static List<string> MatchStep(LlmTestServerConversationMatch match, LlmTestServerChatRequest request)
    {
        var failures = new List<string>();
        var latest = request.Messages.LastOrDefault();
        if (match.LatestRole is not null && latest?.Role != match.LatestRole) { failures.Add("latest.role"); }
        if (match.TextEquals is not null && latest?.Content != match.TextEquals) { failures.Add("latest.text.equals"); }
        if (match.TextContains is not null && (latest is null || !latest.Content.Contains(match.TextContains, StringComparison.Ordinal))) { failures.Add("latest.text.contains"); }
        if (match.TurnIndex is not null && match.TurnIndex != LogicalTurn(request.Messages)) { failures.Add("turn.index"); }
        var call = latest?.Role == "tool"
            ? request.Messages.SelectMany(message => message.ToolCalls).LastOrDefault(candidate => candidate.Id == latest.ToolCallId)
            : latest?.ToolCalls.LastOrDefault();
        if (match.ToolName is not null && call?.Name != match.ToolName) { failures.Add("tool.name"); }
        if (match.ToolCallId is not null && (latest?.Role == "tool" ? latest.ToolCallId : call?.Id) != match.ToolCallId) { failures.Add("tool.callId"); }
        if (match.ToolArgumentsJson is not null && (call is null || !LlmTestServerJsonFieldSelector.Matches(call.ArgumentsJson, "$", match.ToolArgumentsJson))) { failures.Add("tool.arguments"); }
        foreach (var field in match.JsonFields)
        { if (latest is null || !LlmTestServerJsonFieldSelector.Matches(latest.Content, field.Path, field.EqualsJson)) { failures.Add("latest.json:" + field.Path); } }
        return failures;
    }

    private readonly record struct ConversationKey(string Provider, string ConversationId, string CredentialHash);
    private sealed class ConversationState
    {
        internal long BaseBytes { get; init; }
        internal long Bytes { get; set; }
        internal long ResultBytes { get; set; }
        internal bool Busy { get; set; }
        internal bool Terminal { get; set; }
        internal int Executions { get; set; }
        internal int LastTurn { get; set; }
        internal string? Fingerprint { get; set; }
        internal LlmTestServerConversationStep? LastStep { get; set; }
        internal LlmTestServerChatResult? LastResult { get; set; }
        internal IReadOnlyList<string> NextSteps { get; set; } = [];
        internal HashSet<string> Visited { get; } = new(StringComparer.Ordinal);
    }
    private sealed record ConversationLease(LlmTestServerScenario Scenario, ConversationKey Key, ConversationState State,
        LlmTestServerConversationStep Step, string Fingerprint, int Turn, bool NewState, bool Restart, long PreviousBytes, long ResultBytes);
}
