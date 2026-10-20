namespace ManagedCode.LlmTestServer.Diagnostics;

public enum LlmTestServerRequestOrder { Descending, Ascending }

public sealed record LlmTestServerRequestQuery
{
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? RequestId { get; init; }
    public int? Status { get; init; }
    public string? Operation { get; init; }
    public string? Scenario { get; init; }
    public string? Conversation { get; init; }
    public string? Step { get; init; }
    public bool? Streaming { get; init; }
    public bool? Completed { get; init; }
    public bool? Success { get; init; }
    public string? ErrorCode { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public LlmTestServerRequestOrder Order { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; } = 500;
}

public sealed record LlmTestServerRequestSnapshot
{
    public IReadOnlyList<LlmTestServerProviderHttpTrace> Requests { get; init; } = [];
    public int TotalMatched { get; init; }
    public long TotalObserved { get; init; }
    public long EvictedCount { get; init; }
    public bool RetentionTruncated { get; init; }
    public long RetainedByteCount { get; init; }
    public long? FirstSequence { get; init; }
    public long? LastSequence { get; init; }
}

public sealed record LlmTestServerRequestExport
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public LlmTestServerRequestQuery Query { get; init; } = new();
    public LlmTestServerRequestSnapshot Evidence { get; init; } = new();
}

public static class LlmTestServerRequestQueries
{
    public static LlmTestServerRequestSnapshot Apply(LlmTestServerRequestSnapshot snapshot, LlmTestServerRequestQuery query)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Offset < 0 || query.Limit is < 1 or > 500 || query.Status is < 100 or > 599
            || !Enum.IsDefined(query.Order) || query.From > query.To)
        { throw new ArgumentException("Invalid request query range, order or pagination.", nameof(query)); }
        var matches = snapshot.Requests.Where(entry =>
            (query.Provider is null || entry.ProviderId == query.Provider)
            && (query.Model is null || entry.ModelId == query.Model)
            && (query.RequestId is null || entry.RequestId == query.RequestId)
            && (query.Status is null || entry.StatusCode == query.Status)
            && (query.Operation is null || entry.OperationId == query.Operation)
            && (query.Scenario is null || entry.ScenarioId == query.Scenario)
            && (query.Conversation is null || entry.ConversationId == query.Conversation)
            && (query.Step is null || entry.StepId == query.Step)
            && (query.Streaming is null || entry.IsStreaming == query.Streaming)
            && (query.Completed is null || entry.IsCompleted == query.Completed)
            && (query.Success is null || entry.IsSuccess == query.Success)
            && (query.ErrorCode is null || entry.ErrorCode == query.ErrorCode)
            && (query.From is null || entry.StartedAt >= query.From)
            && (query.To is null || entry.StartedAt <= query.To));
        var ordered = (query.Order == LlmTestServerRequestOrder.Ascending
            ? matches.OrderBy(entry => entry.Sequence) : matches.OrderByDescending(entry => entry.Sequence)).ToArray();
        return snapshot with { Requests = Array.AsReadOnly(ordered.Skip(query.Offset).Take(query.Limit).ToArray()), TotalMatched = ordered.Length };
    }
}
