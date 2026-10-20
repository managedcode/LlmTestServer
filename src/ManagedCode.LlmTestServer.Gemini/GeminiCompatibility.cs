using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.Gemini;

public static class GeminiCompatibility
{
    public const string ProviderId = LlmTestServerCompatibilityTags.Gemini;

    public static LlmTestServerProviderProfile Profile { get; } =
        new()
        {
            Id = ProviderId,
            DisplayName = "Gemini",
            Protocol = LlmTestServerProtocolFamily.Gemini,
            DefaultEndpointPath = "/gemini/v1beta/models/{model}:generateContent",
            Capabilities =
            [
                LlmTestServerProviderCapability.Chat,
                LlmTestServerProviderCapability.StreamingChat,
                LlmTestServerProviderCapability.Tools,
                LlmTestServerProviderCapability.StructuredOutput,
                LlmTestServerProviderCapability.Embeddings,
                LlmTestServerProviderCapability.Images,
                LlmTestServerProviderCapability.Video,
                LlmTestServerProviderCapability.Audio,
            ],
            CompatibilityTags =
            [
                ProviderId,
                LlmTestServerCompatibilityTags.GoogleAI,
                LlmTestServerCompatibilityTags.OpenAICompatible,
            ],
            ApiContract = new()
            {
                DocumentationUrl = "https://ai.google.dev/api",
                DocumentationRetrievedOn = "2026-09-07",
                DocumentationVersion = "v1beta Generative Language API",
                Operations =
                [
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Gemini.ModelsGenerateContent,
                        Method = "POST",
                        Path = "/gemini/v1beta/models/{model}:generateContent",
                        DocumentationUrl = "https://ai.google.dev/api/generate-content",
                        ImplementedByHosting = true,
                        Capabilities =
                        [
                            LlmTestServerProviderCapability.Chat,
                            LlmTestServerProviderCapability.Tools,
                            LlmTestServerProviderCapability.StructuredOutput,
                            LlmTestServerProviderCapability.Images,
                            LlmTestServerProviderCapability.Audio,
                        ],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Gemini.ModelsStreamGenerateContent,
                        Method = "POST",
                        Path = "/gemini/v1beta/models/{model}:streamGenerateContent",
                        DocumentationUrl = "https://ai.google.dev/api/generate-content",
                        SupportsStreaming = true,
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.StreamingChat, LlmTestServerProviderCapability.Tools, LlmTestServerProviderCapability.StructuredOutput],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Gemini.ModelsEmbedContent,
                        Method = "POST",
                        Path = "/gemini/v1beta/models/{model}:embedContent",
                        DocumentationUrl = "https://ai.google.dev/api/embeddings",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Embeddings],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Gemini.ModelsPredictLongRunningVideo,
                        Method = "POST",
                        Path = "/gemini/v1beta/models/{model}:predictLongRunning",
                        DocumentationUrl = "https://ai.google.dev/gemini-api/docs/video",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Gemini.ModelsOperationsGetVideo,
                        Method = "GET",
                        Path = "/gemini/v1beta/models/{model}/operations/{operationId}",
                        DocumentationUrl = "https://ai.google.dev/gemini-api/docs/video",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Gemini.FilesGetGeneratedVideo,
                        Method = "GET",
                        Path = "/gemini/v1beta/files/{fileId}",
                        DocumentationUrl = "https://ai.google.dev/api/files",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                ],
            },
        };
}
