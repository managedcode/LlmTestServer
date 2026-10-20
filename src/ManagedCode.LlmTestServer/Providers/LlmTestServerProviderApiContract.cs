namespace ManagedCode.LlmTestServer.Providers;

public sealed record LlmTestServerProviderApiContract
{
    public required string DocumentationUrl { get; init; }

    public required string DocumentationRetrievedOn { get; init; }

    public string? DocumentationVersion { get; init; }

    public IReadOnlyList<LlmTestServerProviderOperation> Operations { get; init; } = [];
}
