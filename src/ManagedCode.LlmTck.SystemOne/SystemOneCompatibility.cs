using ManagedCode.LlmTck.Providers;
namespace ManagedCode.LlmTck.SystemOne;

public static class SystemOneCompatibility
{
    public const string ProviderId = LlmTckCompatibilityTags.SystemOne;
    public static LlmTckProviderProfile Profile { get; } = new()
    {
        Id = ProviderId,
        DisplayName = "SystemOne (Jev and Kev)",
        Protocol = LlmTckProtocolFamily.SystemOne,
        DefaultEndpointPath = "/systemone/v1/systemone",
        Capabilities = [LlmTckProviderCapability.Decisions],
        CompatibilityTags = [ProviderId],
        ApiContract = new()
        {
            DocumentationUrl = "https://docs.typesafe.ai/api",
            DocumentationRetrievedOn = "2026-10-20",
            DocumentationVersion = "Jev 1.14 JSON/multipart decisions, images and box; Kev native decisions",
            Operations = [new() { Id = LlmTckProviderOperationIds.SystemOne.DecisionsCreate, Method = "POST", Path = "/systemone/v1/systemone", DocumentationUrl = "https://docs.typesafe.ai/api", ImplementedByHosting = true, Capabilities = [LlmTckProviderCapability.Decisions] }]
        },
    };
}
