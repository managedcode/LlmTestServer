using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.Perplexity;

public static class PerplexityCompatibility
{
    public const string ProviderId = LlmTestServerCompatibilityTags.Perplexity;

    public static LlmTestServerProviderProfile Profile { get; } =
        new()
        {
            Id = ProviderId,
            DisplayName = "Perplexity",
            Protocol = LlmTestServerProtocolFamily.Perplexity,
            DefaultEndpointPath = "/perplexity/v1/sonar",
            Capabilities =
            [
                LlmTestServerProviderCapability.Chat,
                LlmTestServerProviderCapability.StreamingChat,
                LlmTestServerProviderCapability.StructuredOutput,
            ],
            CompatibilityTags = [ProviderId, LlmTestServerCompatibilityTags.OpenAICompatible],
            ApiContract = new()
            {
                DocumentationUrl = "https://docs.perplexity.ai/api-reference/sonar-post",
                DocumentationRetrievedOn = "2026-09-07",
                DocumentationVersion = "Sonar API v1",
                Operations =
                [
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Perplexity.SonarCreate,
                        Method = "POST",
                        Path = "/perplexity/v1/sonar",
                        DocumentationUrl = "https://docs.perplexity.ai/api-reference/sonar-post",
                        SupportsStreaming = true,
                        ImplementedByHosting = true,
                        Capabilities =
                        [
                            LlmTestServerProviderCapability.Chat,
                            LlmTestServerProviderCapability.StreamingChat,
                LlmTestServerProviderCapability.StructuredOutput,
                        ],
                    },
                ],
            },
        };
}
