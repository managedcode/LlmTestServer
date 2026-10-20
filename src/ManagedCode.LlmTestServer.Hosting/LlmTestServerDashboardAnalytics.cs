using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Runtime;

namespace ManagedCode.LlmTestServer.Hosting;

internal sealed record LlmTestServerDashboardAnalytics
{
    public int RequestCount { get; init; }
    public int UsageRequestCount { get; init; }
    public int ItemizedRequestCount { get; init; }
    public int PendingCount { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long TotalTokens { get; init; }
    public long ReasoningTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheWriteTokens { get; init; }
    public long SystemTokens { get; init; }
    public long UserTokens { get; init; }
    public long AssistantTokens { get; init; }
    public long ToolCallTokens { get; init; }
    public long ToolResultTokens { get; init; }
    public long ToolDefinitionTokens { get; init; }
    public long OtherRoleTokens { get; init; }
    public long NotItemizedTokens { get; init; }
    public int CacheHitCount { get; init; }
    public long VisibleOutputTokens => Math.Max(0, OutputTokens - ReasoningTokens);
    public long UncachedInputTokens => Math.Max(0, InputTokens - CacheReadTokens - CacheWriteTokens);
    public decimal? CacheShare => InputTokens > 0 ? (decimal)CacheReadTokens / InputTokens : null;
    public decimal? CacheHitRate => UsageRequestCount > 0 ? (decimal)CacheHitCount / UsageRequestCount : null;
    public int CallsReturned => Tools.Sum(tool => tool.CallsReturned);
    public int ResultsObserved => Tools.Sum(tool => tool.ResultsObserved);
    public int DistinctResultIds { get; init; }
    public bool PayloadTruncated { get; init; }
    public IReadOnlyList<LlmTestServerDashboardToolAnalytics> Tools { get; init; } = [];
    public IReadOnlyList<LlmTestServerDashboardRequestPoint> Requests { get; init; } = [];

    public IReadOnlyList<LlmTestServerDashboardSegment> InputMix =>
    [
        new("system", "System prompt", SystemTokens, "var(--mix-system)"),
        new("tool-definitions", "Tool schemas", ToolDefinitionTokens, "var(--mix-tool-definitions)"),
        new("user", "User", UserTokens, "var(--mix-user)"),
        new("assistant", "Assistant", AssistantTokens, "var(--mix-assistant)"),
        new("tool-calls", "Tool calls", ToolCallTokens, "var(--mix-tool-calls)"),
        new("tool-results", "Tool results", ToolResultTokens, "var(--mix-tool-results)"),
        new("other", "Other roles", OtherRoleTokens, "var(--mix-other)"),
        new("not-itemized", "Not itemized", NotItemizedTokens, "var(--mix-not-itemized)"),
    ];

    public IReadOnlyList<LlmTestServerDashboardSegment> CacheMix =>
    [
        new("cache-read", "Cache read", CacheReadTokens, "var(--cache-read)"),
        new("cache-write", "Cache write", CacheWriteTokens, "var(--cache-write)"),
        new("uncached", "Uncached input", UncachedInputTokens, "var(--cache-none)"),
    ];

    public IReadOnlyList<LlmTestServerDashboardSegment> OutputMix =>
    [
        new("response", "Response", VisibleOutputTokens, "var(--output-visible)"),
        new("reasoning", "Reasoning", ReasoningTokens, "var(--output-reasoning)"),
    ];

    public IReadOnlyList<LlmTestServerDashboardSegment> ToolMix =>
    [
        new("tool-definitions", "Schemas", ToolDefinitionTokens, "var(--mix-tool-definitions)"),
        new("tool-calls", "Call arguments", ToolCallTokens, "var(--mix-tool-calls)"),
        new("tool-results", "Results", ToolResultTokens, "var(--mix-tool-results)"),
    ];

    public IReadOnlyList<LlmTestServerDashboardSegment> FlowSources =>
        [.. InputMix, new("output", "Generated output", OutputTokens, "var(--output-source)")];

    public IReadOnlyList<LlmTestServerDashboardSegment> FlowTargets => [.. CacheMix, .. OutputMix];

    public static LlmTestServerDashboardAnalytics Create(LlmTestServerRequestSnapshot snapshot,
        LlmTestServerProviderHttpTrace selected, bool conversation)
    {
        var rows = conversation && selected.ConversationIdWasProvided && !string.IsNullOrWhiteSpace(selected.ConversationId)
            ? snapshot.Requests.Where(trace => trace.ConversationIdWasProvided
                && trace.SessionId == selected.SessionId && trace.ProviderNamespace == selected.ProviderNamespace
                && trace.ConversationId == selected.ConversationId).ToArray()
            : [selected];
        return FromRows(rows);
    }

    public static LlmTestServerDashboardAnalytics FromRows(IReadOnlyList<LlmTestServerProviderHttpTrace> rows)
    {
        var usage = rows.Where(trace => trace.Usage is not null).Select(trace => trace.Usage!).ToArray();
        // A composition is shown only when it reconciles with the reported input; anything else stays visibly unitemized.
        var itemized = usage.Where(item => item.InputComposition?.TotalTokens == item.InputTokens)
            .Select(item => item.InputComposition!).ToArray();
        var names = new Dictionary<string, ToolAccumulator>(StringComparer.Ordinal);
        var resultIds = new HashSet<string>(StringComparer.Ordinal);
        ToolAccumulator Tool(string name)
        {
            if (!names.TryGetValue(name, out var tool)) { tool = new(); names.Add(name, tool); }
            return tool;
        }
        foreach (var trace in rows)
        {
            foreach (var call in trace.ResponseToolCalls) { Tool(call.Name).CallsReturned++; }
            var priorCallsById = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var message in trace.Messages)
            {
                if (message.Role == "tool" && !string.IsNullOrEmpty(message.ToolCallId))
                {
                    var tool = Tool(priorCallsById.GetValueOrDefault(message.ToolCallId)
                        ?? "Unknown tool (context unavailable)");
                    tool.ResultsObserved++;
                    tool.ResultIds.Add(message.ToolCallId);
                    resultIds.Add(message.ToolCallId);
                }
                foreach (var call in message.ToolCalls) { priorCallsById[call.Id] = call.Name; }
            }
        }
        return new()
        {
            RequestCount = rows.Count,
            UsageRequestCount = usage.Length,
            ItemizedRequestCount = itemized.Length,
            PendingCount = rows.Count(trace => !trace.IsCompleted),
            InputTokens = usage.Sum(item => (long)item.InputTokens),
            OutputTokens = usage.Sum(item => (long)item.OutputTokens),
            TotalTokens = usage.Sum(item => (long)item.TotalTokens),
            ReasoningTokens = usage.Sum(item => (long)item.ReasoningTokens),
            CacheReadTokens = usage.Sum(item => (long)item.CachedInputTokens),
            CacheWriteTokens = usage.Sum(item => (long)item.CacheCreationInputTokens),
            SystemTokens = itemized.Sum(item => (long)item.SystemTokens),
            UserTokens = itemized.Sum(item => (long)item.UserTokens),
            AssistantTokens = itemized.Sum(item => (long)item.AssistantTokens),
            ToolCallTokens = itemized.Sum(item => (long)item.ToolCallTokens),
            ToolResultTokens = itemized.Sum(item => (long)item.ToolResultTokens),
            ToolDefinitionTokens = itemized.Sum(item => (long)item.ToolDefinitionTokens),
            OtherRoleTokens = itemized.Sum(item => (long)item.OtherTokens),
            NotItemizedTokens = usage.Where(item => item.InputComposition?.TotalTokens != item.InputTokens)
                .Sum(item => (long)item.InputTokens),
            CacheHitCount = usage.Count(item => item.CachedInputTokens > 0),
            DistinctResultIds = resultIds.Count,
            PayloadTruncated = rows.Any(trace => trace.RequestPreviewTruncated || trace.ResponsePreviewTruncated
                || trace.RetentionTruncated || trace.RuntimePayloadTruncated),
            Tools = names.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new LlmTestServerDashboardToolAnalytics
            {
                Name = pair.Key,
                CallsReturned = pair.Value.CallsReturned,
                ResultsObserved = pair.Value.ResultsObserved,
                ResultIds = pair.Value.ResultIds.Order(StringComparer.Ordinal).ToArray()
            }).ToArray(),
            Requests = rows.OrderBy(trace => trace.Sequence).Select(trace => new LlmTestServerDashboardRequestPoint(
                trace.RequestId, trace.ModelId, trace.StartedAt, trace.Usage)).ToArray()
        };
    }

    private sealed class ToolAccumulator
    {
        public int CallsReturned { get; set; }
        public int ResultsObserved { get; set; }
        public HashSet<string> ResultIds { get; } = new(StringComparer.Ordinal);
    }
}

internal sealed record LlmTestServerDashboardToolAnalytics
{
    public string Name { get; init; } = string.Empty;
    public int CallsReturned { get; init; }
    public int ResultsObserved { get; init; }
    public IReadOnlyList<string> ResultIds { get; init; } = [];
}

internal sealed record LlmTestServerDashboardSegment(string Key, string Label, long Tokens, string Color);

internal sealed record LlmTestServerDashboardRequestPoint(string RequestId, string? ModelId, DateTimeOffset StartedAt,
    LlmTestServerTokenUsage? Usage);
