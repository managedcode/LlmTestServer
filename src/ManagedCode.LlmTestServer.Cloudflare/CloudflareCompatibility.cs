using ManagedCode.LlmTestServer.Providers;
namespace ManagedCode.LlmTestServer.Cloudflare;

public static class CloudflareCompatibility
{
    public const string ProviderId = LlmTestServerCompatibilityTags.Cloudflare;
    public static LlmTestServerProviderProfile Profile { get; } = new()
    {
        Id = ProviderId,
        DisplayName = "Cloudflare Workers AI (Clef)",
        Protocol = LlmTestServerProtocolFamily.Cloudflare,
        DefaultEndpointPath = "/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef",
        Capabilities = [LlmTestServerProviderCapability.Decisions],
        CompatibilityTags = [ProviderId],
        ApiContract = new()
        {
            DocumentationUrl = "https://developers.cloudflare.com/workers-ai/models/clef/",
            DocumentationRetrievedOn = "2026-10-20",
            DocumentationVersion = "native Clef/Clef-flash decisions and embedded PNG/JPEG/WebP images",
            Operations = [ new() { Id = LlmTestServerProviderOperationIds.Cloudflare.ClefDecisionsCreate, Method = "POST", Path = "/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef", DocumentationUrl = "https://developers.cloudflare.com/workers-ai/models/clef/", ImplementedByHosting = true, Capabilities = [LlmTestServerProviderCapability.Decisions] },
new() { Id = LlmTestServerProviderOperationIds.Cloudflare.ClefFlashDecisionsCreate, Method = "POST", Path = "/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef-flash", DocumentationUrl = "https://developers.cloudflare.com/workers-ai/models/clef/", ImplementedByHosting = true, Capabilities = [LlmTestServerProviderCapability.Decisions] } ]
        },
    };
}
