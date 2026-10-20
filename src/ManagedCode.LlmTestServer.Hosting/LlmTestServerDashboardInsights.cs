using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Runtime;

namespace ManagedCode.LlmTestServer.Hosting;

internal enum LlmTestServerInsightForm
{
    Donut,
    Bars,
    Columns,
    Dots,
}

internal sealed record LlmTestServerInsightChart(
    string Key,
    string Title,
    string Subtitle,
    string CenterValue,
    string CenterLabel,
    IReadOnlyList<LlmTestServerDashboardSegment> Segments,
    string? FilterKind = null,
    LlmTestServerInsightForm Form = LlmTestServerInsightForm.Donut);

internal sealed record LlmTestServerInsightRequest(string RequestId, long Tokens, string Color, string Outcome, string Title);

internal sealed record LlmTestServerDashboardInsights(
    int RequestCount,
    IReadOnlyList<LlmTestServerInsightChart> Charts,
    IReadOnlyList<LlmTestServerInsightRequest> Recent)
{
    public const string ProviderFilter = "provider";
    public const string ModelFilter = "model";
    public const string OutcomeFilter = "outcome";
    public const string SearchFilter = "search";
    public const string OtherKey = "other";

    // Categorical slots in validated adjacent-pair order; entities past the last slot fold into "Other".
    private static readonly string[] _categorical = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6"];
    private const string _otherColor = "#a8a29e";

    public static LlmTestServerDashboardInsights Create(
        IReadOnlyList<LlmTestServerProviderHttpTrace> visible,
        IReadOnlyList<LlmTestServerProviderHttpTrace> all,
        Func<string, string> providerName,
        Func<LlmTestServerProviderHttpTrace, string> modelName,
        Func<LlmTestServerProviderHttpTrace, LlmTestServerTokenUsage?> usageFor,
        Func<LlmTestServerProviderHttpTrace, int> messageCount)
    {
        var analytics = LlmTestServerDashboardAnalytics.FromRows(visible);
        var withUsage = visible.Where(trace => usageFor(trace) is not null).ToArray();
        var completed = visible.Where(trace => trace.DurationMilliseconds is not null).ToArray();
        var outcomes = Outcomes(visible);
        var succeeded = outcomes.FirstOrDefault(segment => segment.Key == "success")?.Tokens ?? 0;
        var streamed = visible.Count(trace => trace.IsStreaming);
        var latencies = completed.Select(trace => trace.DurationMilliseconds!.Value).Order().ToArray();
        var models = Categorical(visible, all, modelName, model => model == "—" ? "No model" : model, _ => 1);
        var operations = Categorical(visible, all, Operation, operation => operation, _ => 1);
        var totalTokens = withUsage.Sum(trace => (long)usageFor(trace)!.TotalTokens);

        LlmTestServerInsightChart[] built =
        [
            new("providers", "Providers", "Requests per provider surface", Count(visible.Count), "requests",
                Categorical(visible, all, trace => trace.ProviderId, providerName, _ => 1), ProviderFilter),
            new("models", "Models", "Requests per model", Count(models.Count(segment => segment.Key != OtherKey)),
                models.Count == 1 ? "model" : "models", models, ModelFilter, LlmTestServerInsightForm.Bars),
            new("outcomes", "Outcomes", "Status of each request",
                visible.Count == 0 ? "—" : LlmTestServerDashboardCharts.Percent((double)succeeded / visible.Count), "succeeded",
                outcomes, OutcomeFilter),
            new("operations", "Operations", "Which provider APIs were called", Count(operations.Count), operations.Count == 1 ? "operation" : "operations",
                operations, SearchFilter, LlmTestServerInsightForm.Bars),
            new("model-tokens", "Tokens by model", "Where the token volume went",
                withUsage.Length == 0 ? "—" : LlmTestServerDashboardCharts.Tokens(totalTokens), "tokens",
                Categorical(withUsage, all, modelName, model => model == "—" ? "No model" : model,
                    trace => usageFor(trace)!.TotalTokens), ModelFilter),
            new("context-mix", "Context mix", "What all prompts are made of",
                analytics.UsageRequestCount == 0 ? "—" : LlmTestServerDashboardCharts.Tokens(analytics.InputTokens), "input",
                analytics.InputMix),
            new("prompt-cache", "Prompt cache", "How input tokens were served",
                analytics.CacheShare is { } share ? LlmTestServerDashboardCharts.Percent((double)share) : "—", "cache read",
                analytics.CacheMix),
            new("latency", "Latency", "Completed requests by duration",
                latencies.Length == 0 ? "—" : FormatLatency(latencies[latencies.Length / 2]), "median",
                Latency(visible), Form: LlmTestServerInsightForm.Columns),
            new("delivery", "Delivery", "Each dot is 1% of requests",
                visible.Count == 0 ? "—" : LlmTestServerDashboardCharts.Percent((double)streamed / visible.Count), "streamed",
                [
                    new("stream", "Streaming", streamed, "#6fae32"),
                    new("single", "Single response", visible.Count - streamed, "#e2dbd1"),
                ], OutcomeFilter, LlmTestServerInsightForm.Dots),
        ];
        var depth = visible.Select(messageCount).Where(count => count > 0).ToArray();
        var deepest = depth.DefaultIfEmpty(0).Max();
        var charts = new List<LlmTestServerInsightChart>(built.Length + 1);
        // Order fills a 3-, 2- or 1-column bento without holes after the double-width recent-requests card.
        foreach (var key in new[] { "delivery", "providers", "outcomes", "prompt-cache", "models", "operations", "latency", "model-tokens", "context-mix" })
        {
            charts.Add(built.Single(chart => chart.Key == key));
        }

        charts.Add(new("depth", "Conversation depth", "Messages sent per request", Count(deepest), "deepest",
        [
            new("single", "1", depth.Count(count => count == 1), "#cfe8a9"),
            new("short", "2–4", depth.Count(count => count is >= 2 and <= 4), "#a4d36a"),
            new("medium", "5–9", depth.Count(count => count is >= 5 and <= 9), "#6fae32"),
            new("long", "10+", depth.Count(count => count >= 10), "#3f7a17"),
        ], Form: LlmTestServerInsightForm.Columns));
        var recent = visible.Take(48).Reverse().Select(trace => new LlmTestServerInsightRequest(trace.RequestId,
            usageFor(trace)?.TotalTokens ?? 0, OutcomeColor(trace), OutcomeLabel(trace),
            $"{modelName(trace)} · {providerName(trace.ProviderId)}\n{(usageFor(trace) is { } usage ? LlmTestServerDashboardCharts.Tokens(usage.TotalTokens) + " tokens" : "No usage reported")} · {OutcomeLabel(trace)}\n{trace.StartedAt.ToLocalTime():HH:mm:ss}"))
            .ToArray();
        return new(visible.Count, charts, recent);
    }

    public static IReadOnlyDictionary<string, string> CategoricalColors(IReadOnlyList<LlmTestServerProviderHttpTrace> all,
        Func<LlmTestServerProviderHttpTrace, string> key)
    {
        return Categorical(all, all, key, value => value, _ => 1)
            .Where(segment => segment.Key != OtherKey)
            .ToDictionary(segment => segment.Key, segment => segment.Color, StringComparer.Ordinal);
    }

    private static IReadOnlyList<LlmTestServerDashboardSegment> Categorical(
        IReadOnlyList<LlmTestServerProviderHttpTrace> visible,
        IReadOnlyList<LlmTestServerProviderHttpTrace> all,
        Func<LlmTestServerProviderHttpTrace, string> key,
        Func<string, string> label,
        Func<LlmTestServerProviderHttpTrace, long> weight)
    {
        // Slots are ranked over the whole journal, so a filter never repaints the entities that remain visible.
        var slots = all.GroupBy(key, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(_categorical.Length)
            .Select((group, index) => (group.Key, Index: index))
            .ToDictionary(slot => slot.Key, slot => slot.Index, StringComparer.Ordinal);
        var totals = visible.GroupBy(key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(weight), StringComparer.Ordinal);
        var segments = slots.OrderBy(slot => slot.Value)
            .Where(slot => totals.ContainsKey(slot.Key))
            .Select(slot => new LlmTestServerDashboardSegment(slot.Key, label(slot.Key), totals[slot.Key], _categorical[slot.Value]))
            .ToList();
        var other = totals.Where(total => !slots.ContainsKey(total.Key)).Sum(total => total.Value);
        if (other > 0)
        {
            segments.Add(new(OtherKey, "Other", other, _otherColor));
        }

        return segments;
    }

    private static readonly (string Key, string Label, string Color)[] _outcomes =
    [
        ("success", "Succeeded", "#3f8d55"),
        ("client-error", "Client errors (4xx)", "#e8b445"),
        ("server-error", "Server errors (5xx)", "#c94f3d"),
        ("transport", "Transport failures", "#ed732e"),
        ("running", "In flight", _otherColor),
    ];

    private static IReadOnlyList<LlmTestServerDashboardSegment> Outcomes(IReadOnlyList<LlmTestServerProviderHttpTrace> visible)
    {
        return _outcomes.Select(outcome => new LlmTestServerDashboardSegment(outcome.Key, outcome.Label,
            visible.Count(trace => OutcomeKey(trace) == outcome.Key), outcome.Color)).ToArray();
    }

    private static string OutcomeKey(LlmTestServerProviderHttpTrace trace)
    {
        if (!trace.IsCompleted)
        {
            return "running";
        }

        if (trace.TransportState != LlmTestServerProviderHttpTraceTransportState.Completed || trace.StatusCode is null)
        {
            return "transport";
        }

        return trace.StatusCode switch
        {
            < 400 => "success",
            < 500 => "client-error",
            _ => "server-error",
        };
    }

    private static string OutcomeColor(LlmTestServerProviderHttpTrace trace)
    {
        var key = OutcomeKey(trace);
        return _outcomes.First(outcome => outcome.Key == key).Color;
    }

    private static string OutcomeLabel(LlmTestServerProviderHttpTrace trace)
    {
        var key = OutcomeKey(trace);
        return _outcomes.First(outcome => outcome.Key == key).Label;
    }

    private static IReadOnlyList<LlmTestServerDashboardSegment> Latency(IReadOnlyList<LlmTestServerProviderHttpTrace> visible)
    {
        // An ordinal ramp: lighter is faster.
        var durations = visible.Select(trace => trace.DurationMilliseconds).ToArray();
        return
        [
            new("fast", "<10 ms", durations.Count(value => value < 10), "#86b6ef"),
            new("quick", "10–100", durations.Count(value => value is >= 10 and < 100), "#3987e5"),
            new("slow", "0.1–1 s", durations.Count(value => value is >= 100 and < 1000), "#256abf"),
            new("slowest", "≥1 s", durations.Count(value => value >= 1000), "#104281"),
            new("running", "In flight", durations.Count(value => value is null), _otherColor),
        ];
    }

    private static string Operation(LlmTestServerProviderHttpTrace trace)
    {
        return string.IsNullOrWhiteSpace(trace.OperationId) ? trace.Path : trace.OperationId;
    }

    private static string Count(int value)
    {
        return LlmTestServerDashboardCharts.Tokens(value);
    }

    private static string FormatLatency(double milliseconds)
    {
        return milliseconds < 1 ? "<1 ms" : $"{LlmTestServerDashboardCharts.Tokens((long)Math.Round(milliseconds))} ms";
    }
}

internal sealed record LlmTestServerInsightCardView(
    LlmTestServerInsightChart Chart,
    string TestId,
    string? SelectedKey,
    IReadOnlyList<LlmTestServerDashboardSegment> Visible,
    IReadOnlyList<LlmTestServerDonutArc> Arcs,
    IReadOnlyList<string> Dots,
    long Max,
    long Total)
{
    private const int _dotCount = 100;

    public string AriaSummary => $"{Chart.Title}: " + string.Join(", ", Visible.Select(segment =>
        $"{segment.Label} {LlmTestServerDashboardCharts.Tokens(segment.Tokens)}"));

    public static LlmTestServerInsightCardView Create(LlmTestServerInsightChart chart, string testId, string? selectedKey)
    {
        var visible = chart.Segments.Where(segment => segment.Tokens > 0).ToArray();
        var total = visible.Sum(segment => segment.Tokens);
        return new(chart, testId, selectedKey, visible,
            chart.Form == LlmTestServerInsightForm.Donut ? LlmTestServerDashboardCharts.Donut(visible) : [],
            chart.Form == LlmTestServerInsightForm.Dots ? DotColors(visible, total) : [],
            Math.Max(1, chart.Segments.Select(segment => segment.Tokens).DefaultIfEmpty(0).Max()), total);
    }

    private static IReadOnlyList<string> DotColors(IReadOnlyList<LlmTestServerDashboardSegment> visible, long total)
    {
        if (total <= 0)
        {
            return [];
        }

        // Largest-remainder rounding keeps the dot count exact.
        var exact = visible.Select(segment => (Segment: segment, Value: segment.Tokens * (double)_dotCount / total)).ToArray();
        var counts = exact.Select(item => (int)Math.Floor(item.Value)).ToArray();
        foreach (var index in exact.Select((item, index) => (Remainder: item.Value - Math.Floor(item.Value), Index: index))
                     .OrderByDescending(item => item.Remainder).Take(_dotCount - counts.Sum()).Select(item => item.Index))
        {
            counts[index]++;
        }

        return exact.SelectMany((item, index) => Enumerable.Repeat(item.Segment.Color, counts[index])).ToArray();
    }
}
