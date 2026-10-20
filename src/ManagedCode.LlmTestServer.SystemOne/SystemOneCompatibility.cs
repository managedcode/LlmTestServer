using ManagedCode.LlmTestServer.Providers;
namespace ManagedCode.LlmTestServer.SystemOne;

public static class SystemOneCompatibility
{
    public const string ProviderId = LlmTestServerCompatibilityTags.SystemOne;
    public static LlmTestServerProviderProfile Profile { get; } = new()
    {
        Id = ProviderId,
        DisplayName = "SystemOne (Jev and Kev)",
        Protocol = LlmTestServerProtocolFamily.SystemOne,
        DefaultEndpointPath = "/systemone/v1/systemone",
        Capabilities = [LlmTestServerProviderCapability.Decisions],
        CompatibilityTags = [ProviderId],
        ApiContract = new()
        {
            DocumentationUrl = "https://docs.typesafe.ai/api",
            DocumentationRetrievedOn = "2026-10-20",
            DocumentationVersion = "Jev 1.14 JSON/multipart decisions, images and box; Kev native decisions",
            Operations = [new() { Id = LlmTestServerProviderOperationIds.SystemOne.DecisionsCreate, Method = "POST", Path = "/systemone/v1/systemone", DocumentationUrl = "https://docs.typesafe.ai/api", ImplementedByHosting = true, Capabilities = [LlmTestServerProviderCapability.Decisions] }]
        },
    };
}
