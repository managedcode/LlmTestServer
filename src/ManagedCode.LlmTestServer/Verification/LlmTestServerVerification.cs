using ManagedCode.LlmTestServer.Conversations;
using ManagedCode.LlmTestServer.Diagnostics;

namespace ManagedCode.LlmTestServer.Verification;

public sealed record LlmTestServerVerificationRequest
{
    public LlmTestServerRequestQuery Scope { get; init; } = new();
    public List<LlmTestServerRequestExpectation> Requests { get; init; } = [];
    public bool InOrder { get; init; }
    public bool NoUnexpectedCalls { get; init; }
    public List<LlmTestServerConversationExpectation> Conversations { get; init; } = [];
    public List<LlmTestServerRetryExpectation> Retries { get; init; } = [];
}

public sealed record LlmTestServerRequestExpectation
{
    public string Name { get; init; } = "request";
    public LlmTestServerRequestQuery Query { get; init; } = new();
    public int MinimumCount { get; init; } = 1;
    public int? MaximumCount { get; init; } = 1;
    public bool RequireStreamingTerminalMarker { get; init; }
    public List<LlmTestServerToolExpectation> Tools { get; init; } = [];
}

public enum LlmTestServerToolEvidenceKind
{
    ResponseCall,
    RequestCall,
    Result,
}

public sealed record LlmTestServerToolExpectation
{
    public LlmTestServerToolEvidenceKind Kind { get; init; }
    public string? Name { get; init; }
    public string? CallId { get; init; }
    public string? ArgumentsJson { get; init; }
    public string? ResultJson { get; init; }
    public List<LlmTestServerJsonFieldMatch> ResultFields { get; init; } = [];
    public int MinimumCount { get; init; } = 1;
    public int? MaximumCount { get; init; } = 1;
}

public sealed record LlmTestServerConversationExpectation
{
    public string ScenarioId { get; init; } = string.Empty;
    public string? ConversationId { get; init; }
    public string? ProviderNamespace { get; init; }
    public List<string> VisitedSteps { get; init; } = [];
    public bool RequireTerminal { get; init; }
}

public sealed record LlmTestServerRetryExpectation
{
    public string GroupId { get; init; } = string.Empty;
    public LlmTestServerRequestQuery Query { get; init; } = new();
    public int MinimumAttempts { get; init; } = 1;
    public int MaximumAttempts { get; init; } = 3;
    public List<int> StatusSequence { get; init; } = [];
    public double? MinimumRetryAfterSeconds { get; init; }
    public bool RespectRetryAfter { get; init; }
}

public sealed record LlmTestServerVerificationResult
{
    public bool Passed { get; init; }
    public bool EvidenceComplete { get; init; }
    public List<LlmTestServerVerificationFailure> Failures { get; init; } = [];
    public List<string> MatchedRequestIds { get; init; } = [];
}

public sealed record LlmTestServerVerificationFailure
{
    public string Code { get; init; } = string.Empty;
    public string Expectation { get; init; } = string.Empty;
    public string Expected { get; init; } = string.Empty;
    public string Actual { get; init; } = string.Empty;
    public List<string> RequestIds { get; init; } = [];
}
