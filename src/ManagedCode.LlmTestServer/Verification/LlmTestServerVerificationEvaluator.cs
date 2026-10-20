using System.Globalization;
using System.Text.Json;
using ManagedCode.LlmTestServer.Conversations;
using ManagedCode.LlmTestServer.Diagnostics;

namespace ManagedCode.LlmTestServer.Verification;

public static class LlmTestServerVerificationEvaluator
{
    public static LlmTestServerVerificationResult Verify(
        LlmTestServerVerificationRequest verification,
        LlmTestServerRequestSnapshot snapshot,
        IReadOnlyList<LlmTestServerConversationStatus> conversations)
    {
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(conversations);
        Validate(verification);
        var scope = LlmTestServerRequestQueries.Apply(snapshot, verification.Scope);
        var failures = new List<LlmTestServerVerificationFailure>();
        var matched = new HashSet<string>(StringComparer.Ordinal);
        var complete = snapshot.EvictedCount == 0 && !snapshot.RetentionTruncated
            && scope.TotalMatched == scope.Requests.Count
            && scope.Requests.All(trace => trace.IsCompleted && !trace.RetentionTruncated && !trace.RuntimePayloadTruncated);
        void Fail(string code, string expectation, string expected, string actual, IEnumerable<string>? ids = null)
        {
            failures.Add(new() { Code = code, Expectation = expectation, Expected = expected, Actual = actual, RequestIds = ids?.ToList() ?? [] });
        }

        if (!complete)
        {
            Fail("evidence.incomplete", "journal", "A complete retained journal with completed requests",
                $"evicted={snapshot.EvictedCount}; truncated={snapshot.RetentionTruncated}; selected={scope.Requests.Count}/{scope.TotalMatched}");
        }
        long? previousLastSequence = null;
        foreach (var expectation in verification.Requests)
        {
            var selected = LlmTestServerRequestQueries.Apply(scope, expectation.Query);
            var requests = selected.Requests.OrderBy(trace => trace.Sequence).ToArray();
            var ids = requests.Select(trace => trace.RequestId).ToArray();
            matched.UnionWith(ids);
            if (selected.TotalMatched != requests.Length)
            { complete = false; Fail("evidence.page", expectation.Name, "All matching requests retained in the verification page", selected.TotalMatched.ToString(CultureInfo.InvariantCulture), ids); }
            if (!InRange(requests.Length, expectation.MinimumCount, expectation.MaximumCount))
            { Fail("request.count", expectation.Name, Range(expectation.MinimumCount, expectation.MaximumCount), requests.Length.ToString(CultureInfo.InvariantCulture), ids); }
            if (verification.InOrder && requests.Length > 0)
            {
                if (previousLastSequence is { } previous && requests[0].Sequence <= previous)
                { Fail("request.order", expectation.Name, $"Sequence greater than {previous}", requests[0].Sequence.ToString(CultureInfo.InvariantCulture), ids); }
                previousLastSequence = requests[^1].Sequence;
            }
            foreach (var trace in requests)
            {
                if (expectation.RequireStreamingTerminalMarker && (!trace.IsStreaming || trace.TerminalMarkerObserved != true))
                { Fail("stream.terminal", expectation.Name, "A streaming response with its terminal marker observed", $"streaming={trace.IsStreaming}; terminal={trace.TerminalMarkerObserved}", [trace.RequestId]); }
                foreach (var tool in expectation.Tools)
                {
                    var count = Tools(trace, tool.Kind).Count(evidence => ToolMatches(evidence, tool));
                    if (!InRange(count, tool.MinimumCount, tool.MaximumCount))
                    { Fail("tool.count", expectation.Name, $"{tool.Kind} {tool.Name ?? "*"}/{tool.CallId ?? "*"}: {Range(tool.MinimumCount, tool.MaximumCount)}", count.ToString(CultureInfo.InvariantCulture), [trace.RequestId]); }
                }
            }
        }
        foreach (var expectation in verification.Conversations)
        {
            var states = conversations.Where(state => state.ScenarioId == expectation.ScenarioId
                && (expectation.ConversationId is null || state.ConversationId == expectation.ConversationId)
                && (expectation.ProviderNamespace is null || state.ProviderNamespace == expectation.ProviderNamespace)).ToArray();
            if (states.Length == 0)
            { Fail("conversation.missing", expectation.ScenarioId, "At least one matching conversation", "0"); continue; }
            foreach (var state in states)
            {
                var missing = expectation.VisitedSteps.Except(state.VisitedSteps, StringComparer.Ordinal).ToArray();
                if (missing.Length > 0)
                { Fail("conversation.steps", expectation.ScenarioId, string.Join(",", expectation.VisitedSteps), $"Missing: {string.Join(",", missing)}"); }
                if (expectation.RequireTerminal && !state.IsTerminal)
                { Fail("conversation.terminal", expectation.ScenarioId, "Terminal", state.IsBusy ? "Busy" : "Active"); }
            }
        }
        foreach (var retry in verification.Retries)
        {
            var selected = LlmTestServerRequestQueries.Apply(scope, retry.Query);
            var attempts = selected.Requests.Where(trace => trace.RetryGroupId == retry.GroupId).OrderBy(trace => trace.Sequence).ToArray();
            var ids = attempts.Select(trace => trace.RequestId).ToArray();
            matched.UnionWith(ids);
            if (selected.TotalMatched != selected.Requests.Count)
            { complete = false; Fail("evidence.page", retry.GroupId, "All matching retry requests retained", selected.TotalMatched.ToString(CultureInfo.InvariantCulture), ids); }
            if (!InRange(attempts.Length, retry.MinimumAttempts, retry.MaximumAttempts))
            { Fail("retry.attempts", retry.GroupId, Range(retry.MinimumAttempts, retry.MaximumAttempts), attempts.Length.ToString(CultureInfo.InvariantCulture), ids); }
            if (retry.StatusSequence.Count > 0 && !attempts.Select(trace => trace.StatusCode).SequenceEqual(retry.StatusSequence.Select(status => (int?)status)))
            { Fail("retry.statuses", retry.GroupId, string.Join(",", retry.StatusSequence), string.Join(",", attempts.Select(trace => trace.StatusCode)), ids); }
            for (var index = 0; index < attempts.Length; index++)
            {
                var attempt = attempts[index];
                if (attempt.StatusCode is not (429 or 503)) { continue; }
                var delay = RetryAfterSeconds(attempt);
                if (retry.MinimumRetryAfterSeconds is { } minimum && (delay is null || delay < minimum))
                { Fail("retry.after", retry.GroupId, $"Retry-After >= {minimum.ToString(CultureInfo.InvariantCulture)}s", attempt.RetryAfter ?? "missing", [attempt.RequestId]); }
                if (retry.RespectRetryAfter && index + 1 < attempts.Length)
                {
                    var next = attempts[index + 1];
                    if (!TryGetMonotonicBounds(attempt, out _, out var completed)
                        || !TryGetMonotonicBounds(next, out var started, out _))
                    {
                        complete = false;
                        Fail("retry.timing", retry.GroupId, "Finite monotonic start and completion timestamps in chronological order",
                            $"previous={attempt.StartedMonotonicSeconds}/{attempt.CompletedMonotonicSeconds}; next={next.StartedMonotonicSeconds}/{next.CompletedMonotonicSeconds}", [attempt.RequestId, next.RequestId]);
                        continue;
                    }
                    var elapsed = started - completed;
                    if (delay is null || elapsed < delay)
                    { Fail("retry.delay", retry.GroupId, delay is null ? "A valid Retry-After header" : $"At least {delay.Value.ToString(CultureInfo.InvariantCulture)}s", elapsed.ToString(CultureInfo.InvariantCulture), [attempt.RequestId, next.RequestId]); }
                }
            }
        }
        // Retry expectations also account for their calls when asserting that nothing unexpected happened.
        if (verification.NoUnexpectedCalls)
        {
            var unexpected = scope.Requests.Where(trace => !matched.Contains(trace.RequestId)).Select(trace => trace.RequestId).ToArray();
            if (unexpected.Length > 0) { Fail("request.unexpected", "journal", "No unexpected provider requests", unexpected.Length.ToString(CultureInfo.InvariantCulture), unexpected); }
        }
        return new()
        {
            Passed = complete && failures.Count == 0,
            EvidenceComplete = complete,
            Failures = failures,
            MatchedRequestIds = scope.Requests.OrderBy(trace => trace.Sequence).Where(trace => matched.Contains(trace.RequestId)).Select(trace => trace.RequestId).ToList()
        };
    }

    private static bool InRange(int count, int minimum, int? maximum)
    {
        return count >= minimum && (maximum is null || count <= maximum);
    }

    private static string Range(int minimum, int? maximum)
    {
        return maximum == minimum
            ? minimum.ToString(CultureInfo.InvariantCulture) : $"{minimum}..{maximum?.ToString(CultureInfo.InvariantCulture) ?? "unbounded"}";
    }

    private static IEnumerable<ToolEvidence> Tools(LlmTestServerProviderHttpTrace trace, LlmTestServerToolEvidenceKind kind)
    {
        if (kind == LlmTestServerToolEvidenceKind.ResponseCall)
        { return trace.ResponseToolCalls.Select(call => new ToolEvidence(call.Name, call.Id, call.ArgumentsJson, null)); }
        if (kind == LlmTestServerToolEvidenceKind.RequestCall)
        { return trace.Messages.SelectMany(message => message.ToolCalls).Select(call => new ToolEvidence(call.Name, call.Id, call.ArgumentsJson, null)); }
        return trace.Messages.Select((message, index) => (message, index)).Where(item => item.message.Role == "tool").Select(item =>
        {
            var call = trace.Messages.Take(item.index).SelectMany(message => message.ToolCalls).LastOrDefault(candidate => candidate.Id == item.message.ToolCallId);
            return new ToolEvidence(call?.Name, item.message.ToolCallId, call?.ArgumentsJson, item.message.Content);
        });
    }

    private static bool ToolMatches(ToolEvidence evidence, LlmTestServerToolExpectation expectation)
    {
        return (expectation.Name is null || evidence.Name == expectation.Name)
                && (expectation.CallId is null || evidence.CallId == expectation.CallId)
                && (expectation.ArgumentsJson is null || evidence.ArgumentsJson is not null && LlmTestServerJsonFieldSelector.Matches(evidence.ArgumentsJson, "$", expectation.ArgumentsJson))
                && (expectation.ResultJson is null || evidence.ResultJson is not null && LlmTestServerJsonFieldSelector.Matches(evidence.ResultJson, "$", expectation.ResultJson))
                && expectation.ResultFields.All(field => evidence.ResultJson is not null && LlmTestServerJsonFieldSelector.Matches(evidence.ResultJson, field.Path, field.EqualsJson));
    }

    private static bool TryGetMonotonicBounds(LlmTestServerProviderHttpTrace trace, out double started, out double completed)
    {
        started = trace.StartedMonotonicSeconds ?? double.NaN;
        completed = trace.CompletedMonotonicSeconds ?? double.NaN;
        return double.IsFinite(started) && double.IsFinite(completed) && started >= 0 && completed >= started;
    }

    private static double? RetryAfterSeconds(LlmTestServerProviderHttpTrace trace)
    {
        if (trace.RetryAfter is null) { return null; }
        if (double.TryParse(trace.RetryAfter, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds >= 0)
        { return seconds; }
        if (DateTimeOffset.TryParse(trace.RetryAfter, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        { return Math.Max(0, (date - (trace.CompletedAt ?? trace.StartedAt)).TotalSeconds); }
        return null;
    }

    private static void Validate(LlmTestServerVerificationRequest verification)
    {
        ArgumentNullException.ThrowIfNull(verification.Scope);
        ArgumentNullException.ThrowIfNull(verification.Requests);
        ArgumentNullException.ThrowIfNull(verification.Conversations);
        ArgumentNullException.ThrowIfNull(verification.Retries);
        var expectations = verification.Requests.Count + verification.Conversations.Count + verification.Retries.Count;
        if (expectations > 256 || expectations == 0 && !verification.NoUnexpectedCalls)
        { throw new ArgumentException("Verification requires at most 256 expectations and a request, graph, retry, or no-unexpected-calls assertion.", nameof(verification)); }
        foreach (var expectation in verification.Requests)
        {
            ArgumentNullException.ThrowIfNull(expectation);
            ArgumentException.ThrowIfNullOrWhiteSpace(expectation.Name);
            ArgumentNullException.ThrowIfNull(expectation.Query);
            ValidateRange(expectation.MinimumCount, expectation.MaximumCount);
            ArgumentNullException.ThrowIfNull(expectation.Tools);
            if (expectation.Tools.Count > 64) { throw new ArgumentException("A request expectation supports at most 64 tool expectations.", nameof(verification)); }
            foreach (var tool in expectation.Tools)
            {
                ArgumentNullException.ThrowIfNull(tool);
                if (!Enum.IsDefined(tool.Kind)) { throw new ArgumentException("Unknown tool evidence kind.", nameof(verification)); }
                ValidateRange(tool.MinimumCount, tool.MaximumCount);
                ValidateJson(tool.ArgumentsJson);
                ValidateJson(tool.ResultJson);
                ArgumentNullException.ThrowIfNull(tool.ResultFields);
                if (tool.ResultFields.Count > 64) { throw new ArgumentException("A tool expectation supports at most 64 result fields.", nameof(verification)); }
                foreach (var field in tool.ResultFields)
                {
                    ArgumentNullException.ThrowIfNull(field);
                    LlmTestServerJsonFieldSelector.Parse(field.Path);
                    ArgumentNullException.ThrowIfNull(field.EqualsJson);
                    ValidateJson(field.EqualsJson);
                }
                if (tool.Kind != LlmTestServerToolEvidenceKind.Result && (tool.ResultJson is not null || tool.ResultFields.Count != 0))
                { throw new ArgumentException("Result predicates require tool-result evidence.", nameof(verification)); }
            }
        }
        foreach (var conversation in verification.Conversations)
        {
            ArgumentNullException.ThrowIfNull(conversation);
            ArgumentException.ThrowIfNullOrWhiteSpace(conversation.ScenarioId);
            ArgumentNullException.ThrowIfNull(conversation.VisitedSteps);
            if (conversation.VisitedSteps.Count > 256) { throw new ArgumentException("A graph supports at most 256 required steps.", nameof(verification)); }
            foreach (var step in conversation.VisitedSteps) { ArgumentException.ThrowIfNullOrWhiteSpace(step); }
        }
        foreach (var retry in verification.Retries)
        {
            ArgumentNullException.ThrowIfNull(retry);
            ArgumentException.ThrowIfNullOrWhiteSpace(retry.GroupId);
            ArgumentNullException.ThrowIfNull(retry.Query);
            ValidateRange(retry.MinimumAttempts, retry.MaximumAttempts);
            ArgumentNullException.ThrowIfNull(retry.StatusSequence);
            if (retry.StatusSequence.Count > 500 || retry.StatusSequence.Any(status => status is < 100 or > 599)
                || retry.MinimumRetryAfterSeconds is { } seconds && (!double.IsFinite(seconds) || seconds < 0))
            { throw new ArgumentException("Invalid retry verification limits or statuses.", nameof(verification)); }
        }
    }

    private static void ValidateRange(int minimum, int? maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimum);
        if (maximum < minimum) { throw new ArgumentException("The maximum count must be at least the minimum count.", nameof(maximum)); }
    }

    private static void ValidateJson(string? json)
    {
        if (json is null) { return; }
        try { using var parsed = JsonDocument.Parse(json); }
        catch (JsonException exception) { throw new ArgumentException("Verification JSON predicates must contain valid JSON.", nameof(json), exception); }
    }

    private sealed record ToolEvidence(string? Name, string? CallId, string? ArgumentsJson, string? ResultJson);
}
