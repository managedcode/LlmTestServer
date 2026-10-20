using ManagedCode.LlmTck.Providers;
namespace ManagedCode.LlmTck.Cloudflare;

public static class CloudflareCompatibility
{
    public const string ProviderId = LlmTckCompatibilityTags.Cloudflare;
    public static LlmTckProviderProfile Profile { get; } = new()
    {
        Id = ProviderId,
        DisplayName = "Cloudflare Workers AI (Clef)",
        Protocol = LlmTckProtocolFamily.Cloudflare,
        DefaultEndpointPath = "/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef",
        Capabilities = [LlmTckProviderCapability.Decisions],
        CompatibilityTags = [ProviderId],
        ApiContract = new()
        {
            DocumentationUrl = "https://developers.cloudflare.com/workers-ai/models/clef/",
            DocumentationRetrievedOn = "2026-10-20",
            DocumentationVersion = "native Clef/Clef-flash decisions and embedded PNG/JPEG/WebP images",
            Operations = [ new() { Id = LlmTckProviderOperationIds.Cloudflare.ClefDecisionsCreate, Method = "POST", Path = "/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef", DocumentationUrl = "https://developers.cloudflare.com/workers-ai/models/clef/", ImplementedByHosting = true, Capabilities = [LlmTckProviderCapability.Decisions] },
new() { Id = LlmTckProviderOperationIds.Cloudflare.ClefFlashDecisionsCreate, Method = "POST", Path = "/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef-flash", DocumentationUrl = "https://developers.cloudflare.com/workers-ai/models/clef/", ImplementedByHosting = true, Capabilities = [LlmTckProviderCapability.Decisions] } ]
        },
    };
}
