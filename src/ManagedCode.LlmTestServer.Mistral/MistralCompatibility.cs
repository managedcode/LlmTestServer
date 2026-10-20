using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.Mistral;

public static class MistralCompatibility
{
    public const string ProviderId = LlmTestServerCompatibilityTags.Mistral;

    public static LlmTestServerProviderProfile Profile { get; } =
        new()
        {
            Id = ProviderId,
            DisplayName = "Mistral",
            Protocol = LlmTestServerProtocolFamily.Mistral,
            DefaultEndpointPath = "/mistral/v1/chat/completions",
            Capabilities =
            [
                LlmTestServerProviderCapability.Chat,
                LlmTestServerProviderCapability.StreamingChat,
                LlmTestServerProviderCapability.Tools,
                LlmTestServerProviderCapability.StructuredOutput,
                LlmTestServerProviderCapability.Embeddings,
            ],
            CompatibilityTags = [ProviderId, LlmTestServerCompatibilityTags.OpenAICompatible],
            ApiContract = new()
            {
                DocumentationUrl = "https://docs.mistral.ai/api/endpoint/chat",
                DocumentationRetrievedOn = "2026-09-07",
                DocumentationVersion = "v1",
                Operations =
                [
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Mistral.ChatComplete,
                        Method = "POST",
                        Path = "/mistral/v1/chat/completions",
                        DocumentationUrl = "https://docs.mistral.ai/api/endpoint/chat",
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
                        Id = LlmTestServerProviderOperationIds.Mistral.EmbeddingsCreate,
                        Method = "POST",
                        Path = "/mistral/v1/embeddings",
                        DocumentationUrl = "https://docs.mistral.ai/api/endpoint/embeddings",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Embeddings],
                    },
                ],
            },
        };
}
