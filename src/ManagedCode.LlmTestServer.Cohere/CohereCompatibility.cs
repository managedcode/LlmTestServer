using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.Cohere;

public static class CohereCompatibility
{
    public const string ProviderId = LlmTestServerCompatibilityTags.Cohere;

    public static LlmTestServerProviderProfile Profile { get; } =
        new()
        {
            Id = ProviderId,
            DisplayName = "Cohere",
            Protocol = LlmTestServerProtocolFamily.Cohere,
            DefaultEndpointPath = "/cohere/v2/chat",
            Capabilities =
            [
                LlmTestServerProviderCapability.Chat,
                LlmTestServerProviderCapability.StreamingChat,
                LlmTestServerProviderCapability.Tools,
                LlmTestServerProviderCapability.StructuredOutput,
                LlmTestServerProviderCapability.Embeddings,
            ],
            CompatibilityTags = [ProviderId],
            ApiContract = new()
            {
                DocumentationUrl = "https://docs.cohere.com/reference/chat",
                DocumentationRetrievedOn = "2026-09-07",
                DocumentationVersion = "v2",
                Operations =
                [
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Cohere.ChatCreate,
                        Method = "POST",
                        Path = "/cohere/v2/chat",
                        DocumentationUrl = "https://docs.cohere.com/reference/chat",
                        SupportsStreaming = true,
                        ImplementedByHosting = true,
                        Capabilities =
                        [
                            LlmTestServerProviderCapability.Chat,
                            LlmTestServerProviderCapability.StreamingChat,
                LlmTestServerProviderCapability.Tools,
                LlmTestServerProviderCapability.StructuredOutput,
                        ],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Cohere.EmbedCreate,
                        Method = "POST",
                        Path = "/cohere/v2/embed",
                        DocumentationUrl = "https://docs.cohere.com/reference/embed",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Embeddings],
                    },
                ],
            },
        };
}
