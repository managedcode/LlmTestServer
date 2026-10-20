namespace ManagedCode.LlmTestServer.Providers;

public sealed record LlmTestServerProviderProfile
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required LlmTestServerProtocolFamily Protocol { get; init; }

    public string DefaultEndpointPath { get; init; } = "/";

    public IReadOnlyList<LlmTestServerProviderCapability> Capabilities { get; init; } = [];

    public IReadOnlyList<string> CompatibilityTags { get; init; } = [];

    public required LlmTestServerProviderApiContract ApiContract { get; init; }
}
