using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Decisions;

public enum LlmTestServerDecisionProvider { Unspecified, TypeSafe, Kev, Cloudflare, OpenAI }

public enum LlmTestServerDecisionKind { Predicate, Choice, Score, Box }

public sealed record LlmTestServerDecisionQuestion
{
    public string Name { get; init; } = string.Empty;
    public LlmTestServerDecisionKind Kind { get; init; }
    public List<string> Options { get; init; } = [];
}

public sealed record LlmTestServerDecisionRequest
{
    public LlmTestServerDecisionProvider Provider { get; init; }
    public string ModelId { get; init; } = string.Empty;
    public string Input { get; init; } = string.Empty;
    public List<LlmTestServerDecisionQuestion> Questions { get; init; } = [];
}

public sealed record LlmTestServerDecisionAnswer
{
    public LlmTestServerDecisionKind Kind { get; init; }
    public bool Refused { get; init; }
    public double? Probability { get; init; }
    public string? Choice { get; init; }
    public double? Score { get; init; }
    public double? Confidence { get; init; }
    public Dictionary<string, double> Probabilities { get; init; } = [];
    public LlmTestServerBoundingBox? Box { get; init; }
}

public sealed record LlmTestServerBoundingBox
{
    public required double Top { get; init; }
    public required double Bottom { get; init; }
    public required double Left { get; init; }
    public required double Right { get; init; }

    public bool IsValid => new[] { Top, Bottom, Left, Right }.All(value => double.IsFinite(value) && value >= 0 && value <= 1)
        && Top < Bottom && Left < Right;
}

public sealed record LlmTestServerDecisionScenario
{
    public string Id { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string? ResolvedModelId { get; init; }
    public string? ExpectedInput { get; init; }
    public Dictionary<string, LlmTestServerDecisionAnswer> Answers { get; init; } = [];
    public LlmTestServerTokenUsage Usage { get; init; } = new();
    public LlmTestServerScenarioError? Error { get; init; }
    public bool AllowMalformedResponse { get; init; }
    public LlmTestServerDecisionMetadata Metadata { get; init; } = new();
}

public sealed record LlmTestServerDecisionResult
{
    public bool IsSuccess { get; init; }
    public string ModelId { get; init; } = string.Empty;
    public string? ScenarioId { get; init; }
    public Dictionary<string, LlmTestServerDecisionAnswer> Answers { get; init; } = [];
    public LlmTestServerTokenUsage Usage { get; init; } = new();
    public int StatusCode { get; init; } = 200;
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public LlmTestServerDecisionMetadata Metadata { get; init; } = new();
}

public sealed record LlmTestServerDecisionMetadata
{
    public double LatencyMilliseconds { get; init; }
    public bool? Truncated { get; init; }
    public int? StateTokens { get; init; }
    public int? StateTokensUsed { get; init; }
}
