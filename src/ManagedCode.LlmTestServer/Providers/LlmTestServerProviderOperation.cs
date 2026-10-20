namespace ManagedCode.LlmTestServer.Providers;

public sealed record LlmTestServerProviderOperation
{
    public required string Id { get; init; }

    public required string Method { get; init; }

    public required string Path { get; init; }

    public required string DocumentationUrl { get; init; }

    public string? ApiVersion { get; init; }

    public string? RequiredHeader { get; init; }

    public bool SupportsStreaming { get; init; }

    public bool ImplementedByHosting { get; init; }

    public IReadOnlyList<LlmTestServerProviderCapability> Capabilities { get; init; } = [];
}
