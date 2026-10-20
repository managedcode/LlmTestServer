namespace ManagedCode.LlmTestServer.Configuration;

public sealed record LlmTestServerFaultSimulation
{
    public int? MaxRequestsBeforeRateLimit { get; init; }

    public List<string> ContentFilterTerms { get; init; } = [];
}
