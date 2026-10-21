using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.Foundry;

public static class MicrosoftFoundryCompatibility
{
    public const string ProviderId = LlmTestServerCompatibilityTags.MicrosoftFoundry;

    public static LlmTestServerProviderProfile Profile { get; } =
        new()
        {
            Id = ProviderId,
            DisplayName = "Microsoft Foundry",
            Protocol = LlmTestServerProtocolFamily.MicrosoftFoundry,
            DefaultEndpointPath = "/microsoft-foundry/models/chat/completions",
            Capabilities =
            [
                LlmTestServerProviderCapability.Chat,
                LlmTestServerProviderCapability.StreamingChat,
                LlmTestServerProviderCapability.Tools,
                LlmTestServerProviderCapability.StructuredOutput,
                LlmTestServerProviderCapability.Embeddings,
                LlmTestServerProviderCapability.Audio,
                LlmTestServerProviderCapability.StreamingAudio,
            ],
            CompatibilityTags =
            [
                ProviderId,
                LlmTestServerCompatibilityTags.AzureAIFoundry,
                LlmTestServerCompatibilityTags.OpenAICompatible,
            ],
            ApiContract = new()
            {
                DocumentationUrl =
                    "https://learn.microsoft.com/en-us/rest/api/microsoft-foundry/modelinference/",
                DocumentationRetrievedOn = "2026-10-21",
                DocumentationVersion = "Azure AI Model Inference; OpenAI v1 GA",
                Operations =
                [
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.MicrosoftFoundry.V1AudioTranscriptionsCreate,
                        Method = "POST",
                        Path = "/microsoft-foundry/openai/v1/audio/transcriptions",
                        DocumentationUrl = "https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest",
                        ApiVersion = "v1 (default) or preview",
                        SupportsStreaming = true,
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Audio, LlmTestServerProviderCapability.StreamingAudio],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.MicrosoftFoundry.V1ChatCompletionsCreate,
                        Method = "POST",
                        Path = "/microsoft-foundry/openai/v1/chat/completions",
                        DocumentationUrl = "https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle",
                        ApiVersion = "v1",
                        SupportsStreaming = true,
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Chat, LlmTestServerProviderCapability.StreamingChat, LlmTestServerProviderCapability.Tools, LlmTestServerProviderCapability.StructuredOutput],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.MicrosoftFoundry.V1ResponsesCreate,
                        Method = "POST",
                        Path = "/microsoft-foundry/openai/v1/responses",
                        DocumentationUrl = "https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/responses",
                        ApiVersion = "v1",
                        SupportsStreaming = true,
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Chat, LlmTestServerProviderCapability.StreamingChat, LlmTestServerProviderCapability.Tools, LlmTestServerProviderCapability.StructuredOutput],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.MicrosoftFoundry.V1EmbeddingsCreate,
                        Method = "POST",
                        Path = "/microsoft-foundry/openai/v1/embeddings",
                        DocumentationUrl = "https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle",
                        ApiVersion = "v1",
                        SupportsStreaming = false,
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Embeddings],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.MicrosoftFoundry.ChatCompletionsCreate,
                        ApiVersion = "2024-05-01-preview",
                        Method = "POST",
                        Path = "/microsoft-foundry/chat/completions",
                        DocumentationUrl =
                            "https://learn.microsoft.com/en-us/rest/api/microsoft-foundry/modelinference/",
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
                        Id = LlmTestServerProviderOperationIds.MicrosoftFoundry.EmbeddingsCreate,
                        ApiVersion = "2024-05-01-preview",
                        Method = "POST",
                        Path = "/microsoft-foundry/embeddings",
                        DocumentationUrl =
                            "https://learn.microsoft.com/en-us/rest/api/microsoft-foundry/modelinference/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Embeddings],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.MicrosoftFoundry.ModelsChatCompletionsCreate,
                        ApiVersion = "2024-05-01-preview",
                        Method = "POST",
                        Path = "/microsoft-foundry/models/chat/completions",
                        DocumentationUrl =
                            "https://learn.microsoft.com/en-us/rest/api/microsoft-foundry/modelinference/",
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
                        Id = LlmTestServerProviderOperationIds.MicrosoftFoundry.ModelsEmbeddingsCreate,
                        ApiVersion = "2024-05-01-preview",
                        Method = "POST",
                        Path = "/microsoft-foundry/models/embeddings",
                        DocumentationUrl =
                            "https://learn.microsoft.com/en-us/rest/api/microsoft-foundry/modelinference/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Embeddings],
                    },
                ],
            },
        };
}
