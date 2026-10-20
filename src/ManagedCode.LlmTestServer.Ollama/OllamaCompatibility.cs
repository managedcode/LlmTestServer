using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.Ollama;

public static class OllamaCompatibility
{
    public const string ProviderId = LlmTestServerCompatibilityTags.Ollama;

    public static LlmTestServerProviderProfile Profile { get; } =
        new()
        {
            Id = ProviderId,
            DisplayName = "Ollama",
            Protocol = LlmTestServerProtocolFamily.Ollama,
            DefaultEndpointPath = "/ollama/api/chat",
            Capabilities =
            [
                LlmTestServerProviderCapability.Chat,
                LlmTestServerProviderCapability.StreamingChat,
                LlmTestServerProviderCapability.Tools,
                LlmTestServerProviderCapability.StructuredOutput,
                LlmTestServerProviderCapability.Embeddings,
            ],
            CompatibilityTags =
            [
                ProviderId,
                LlmTestServerCompatibilityTags.LocalLlm,
                LlmTestServerCompatibilityTags.OpenAICompatible,
            ],
            ApiContract = new()
            {
                DocumentationUrl = "https://docs.ollama.com/api/chat",
                DocumentationRetrievedOn = "2026-09-07",
                DocumentationVersion = "local Ollama HTTP API",
                Operations =
                [
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Ollama.ChatCreate,
                        Method = "POST",
                        Path = "/ollama/api/chat",
                        DocumentationUrl = "https://docs.ollama.com/api/chat",
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
                        Id = LlmTestServerProviderOperationIds.Ollama.EmbeddingsCreate,
                        Method = "POST",
                        Path = "/ollama/api/embed",
                        DocumentationUrl = "https://docs.ollama.com/api/embed",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Embeddings],
                    },
                ],
            },
        };
}
