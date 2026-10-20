using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.AzureOpenAI;

public static class AzureOpenAiCompatibility
{
    public const string ProviderId = LlmTestServerCompatibilityTags.AzureOpenAI;

    public static LlmTestServerProviderProfile Profile { get; } =
        new()
        {
            Id = ProviderId,
            DisplayName = "Azure OpenAI",
            Protocol = LlmTestServerProtocolFamily.AzureOpenAI,
            DefaultEndpointPath = "/azure-openai/openai/deployments/{deployment}/chat/completions",
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
                LlmTestServerProviderCapability.StreamingAudio,
            ],
            CompatibilityTags =
            [
                ProviderId,
                LlmTestServerCompatibilityTags.OpenAICompatible,
                LlmTestServerCompatibilityTags.Foundry,
            ],
            ApiContract = new()
            {
                DocumentationUrl = "https://learn.microsoft.com/en-us/azure/foundry/openai/reference",
                DocumentationRetrievedOn = "2026-10-04",
                DocumentationVersion = "SDK-selected dated deployment API (2024-10-21 reference baseline); v1 GA inference; v1 preview video",
                Operations =
                [
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.V1AudioTranscriptionsCreate,
                        Method = "POST",
                        Path = "/azure-openai/openai/v1/audio/transcriptions",
                        DocumentationUrl = "https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest",
                        ApiVersion = "v1 (default) or preview",
                        SupportsStreaming = true,
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Audio, LlmTestServerProviderCapability.StreamingAudio],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.V1ChatCompletionsCreate,
                        Method = "POST",
                        Path = "/azure-openai/openai/v1/chat/completions",
                        DocumentationUrl = "https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle",
                        ApiVersion = "v1",
                        SupportsStreaming = true,
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Chat, LlmTestServerProviderCapability.StreamingChat, LlmTestServerProviderCapability.Tools, LlmTestServerProviderCapability.StructuredOutput],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.V1ResponsesCreate,
                        Method = "POST",
                        Path = "/azure-openai/openai/v1/responses",
                        DocumentationUrl = "https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle",
                        ApiVersion = "v1",
                        SupportsStreaming = true,
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Chat, LlmTestServerProviderCapability.StreamingChat, LlmTestServerProviderCapability.Tools, LlmTestServerProviderCapability.StructuredOutput],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.V1EmbeddingsCreate,
                        Method = "POST",
                        Path = "/azure-openai/openai/v1/embeddings",
                        DocumentationUrl = "https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle",
                        ApiVersion = "v1",
                        SupportsStreaming = false,
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Embeddings],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.V1ImagesCreate,
                        Method = "POST",
                        Path = "/azure-openai/openai/v1/images/generations",
                        DocumentationUrl = "https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest",
                        ApiVersion = "v1 (default) or preview",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Images],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.ChatCompletionsCreate,
                        Method = "POST",
                        Path = "/azure-openai/openai/deployments/{deployment}/chat/completions",
                        DocumentationUrl =
                            "https://learn.microsoft.com/en-us/azure/foundry/openai/reference",
                        ApiVersion = "2024-10-21",
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
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.EmbeddingsCreate,
                        Method = "POST",
                        Path = "/azure-openai/openai/deployments/{deployment}/embeddings",
                        DocumentationUrl =
                            "https://learn.microsoft.com/en-us/azure/foundry/openai/reference",
                        ApiVersion = "2024-10-21",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Embeddings],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.ImagesCreate,
                        Method = "POST",
                        Path = "/azure-openai/openai/deployments/{deployment}/images/generations",
                        DocumentationUrl = "https://learn.microsoft.com/en-us/azure/foundry/openai/reference",
                        ApiVersion = "2024-10-21",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Images],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.AudioSpeechCreate,
                        Method = "POST",
                        Path = "/azure-openai/openai/deployments/{deployment}/audio/speech",
                        DocumentationUrl = "https://learn.microsoft.com/en-us/azure/foundry/openai/reference",
                        ApiVersion = "2024-10-21",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Audio],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.AudioTranscriptionsCreate,
                        Method = "POST",
                        Path = "/azure-openai/openai/deployments/{deployment}/audio/transcriptions",
                        DocumentationUrl = "https://learn.microsoft.com/en-us/azure/foundry/openai/reference",
                        ApiVersion = "2024-10-21",
                        RequiredHeader = "api-key",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Audio],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.AudioTranslationsCreate,
                        Method = "POST",
                        Path = "/azure-openai/openai/deployments/{deployment}/audio/translations",
                        DocumentationUrl = "https://learn.microsoft.com/en-us/azure/foundry/openai/reference",
                        ApiVersion = "2024-10-21",
                        RequiredHeader = "api-key",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Audio],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.VideoGenerationJobsCreate,
                        Method = "POST",
                        Path = "/azure-openai/openai/v1/video/generations/jobs",
                        DocumentationUrl =
                            "https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest",
                        ApiVersion = "v1 preview",
                        RequiredHeader = "api-key",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.VideoGenerationJobsList,
                        Method = "GET",
                        Path = "/azure-openai/openai/v1/video/generations/jobs",
                        DocumentationUrl =
                            "https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest",
                        ApiVersion = "v1 preview",
                        RequiredHeader = "api-key",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.VideoGenerationJobsRetrieve,
                        Method = "GET",
                        Path = "/azure-openai/openai/v1/video/generations/jobs/{jobId}",
                        DocumentationUrl =
                            "https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest",
                        ApiVersion = "v1 preview",
                        RequiredHeader = "api-key",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.VideoGenerationJobsDelete,
                        Method = "DELETE",
                        Path = "/azure-openai/openai/v1/video/generations/jobs/{jobId}",
                        DocumentationUrl =
                            "https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest",
                        ApiVersion = "v1 preview",
                        RequiredHeader = "api-key",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.VideoGenerationsRetrieve,
                        Method = "GET",
                        Path = "/azure-openai/openai/v1/video/generations/{generationId}",
                        DocumentationUrl =
                            "https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest",
                        ApiVersion = "v1 preview",
                        RequiredHeader = "api-key",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.VideoGenerationsThumbnailRetrieve,
                        Method = "GET",
                        Path = "/azure-openai/openai/v1/video/generations/{generationId}/content/thumbnail",
                        DocumentationUrl =
                            "https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest",
                        ApiVersion = "v1 preview",
                        RequiredHeader = "api-key",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.VideoGenerationsContentRetrieve,
                        Method = "GET",
                        Path = "/azure-openai/openai/v1/video/generations/{generationId}/content/video",
                        DocumentationUrl =
                            "https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest",
                        ApiVersion = "v1 preview",
                        RequiredHeader = "api-key",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.AzureOpenAI.VideoGenerationsContentHead,
                        Method = "HEAD",
                        Path = "/azure-openai/openai/v1/video/generations/{generationId}/content/video",
                        DocumentationUrl =
                            "https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest",
                        ApiVersion = "v1 preview",
                        RequiredHeader = "api-key",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                ],
            },
        };
}
