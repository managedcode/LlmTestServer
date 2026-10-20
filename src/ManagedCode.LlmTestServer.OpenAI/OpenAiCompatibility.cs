using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.OpenAI;

public static class OpenAiCompatibility
{
    public const string ProviderId = LlmTestServerCompatibilityTags.OpenAI;

    public static LlmTestServerProviderProfile Profile { get; } =
        new()
        {
            Id = ProviderId,
            DisplayName = "OpenAI",
            Protocol = LlmTestServerProtocolFamily.OpenAI,
            DefaultEndpointPath = "/openai/v1",
            Capabilities =
            [
                LlmTestServerProviderCapability.Decisions,
                LlmTestServerProviderCapability.Models,
                LlmTestServerProviderCapability.Chat,
                LlmTestServerProviderCapability.StreamingChat,
                LlmTestServerProviderCapability.Tools,
                LlmTestServerProviderCapability.StructuredOutput,
                LlmTestServerProviderCapability.Embeddings,
                LlmTestServerProviderCapability.Images,
                LlmTestServerProviderCapability.StreamingImages,
                LlmTestServerProviderCapability.Video,
                LlmTestServerProviderCapability.Audio,
                LlmTestServerProviderCapability.StreamingAudio,
            ],
            CompatibilityTags = [ProviderId, LlmTestServerCompatibilityTags.OpenAICompatible],
            ApiContract = new()
            {
                DocumentationUrl = "https://developers.openai.com/api/reference/",
                DocumentationRetrievedOn = "2026-09-07",
                DocumentationVersion = "v1",
                Operations =
                [
                    new() { Id = LlmTestServerProviderOperationIds.OpenAI.DecisionsCreate, Method = "POST", Path = "/openai/v1/decisions",
                        DocumentationUrl = "https://developers.openai.com/api/reference/resources/decisions/methods/create/",
                        ImplementedByHosting = true, Capabilities = [LlmTestServerProviderCapability.Decisions] },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.ModelsList,
                        Method = "GET",
                        Path = "/openai/v1/models",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/models/methods/list/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Models],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.ChatCompletionsCreate,
                        Method = "POST",
                        Path = "/openai/v1/chat/completions",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create/",
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
                        Id = LlmTestServerProviderOperationIds.OpenAI.ResponsesCreate,
                        Method = "POST",
                        Path = "/openai/v1/responses",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/responses/methods/create/",
                        SupportsStreaming = true,
                        ImplementedByHosting = true,
                        Capabilities =
                        [
                            LlmTestServerProviderCapability.Chat,
                            LlmTestServerProviderCapability.StreamingChat,
                LlmTestServerProviderCapability.Tools,
                LlmTestServerProviderCapability.StructuredOutput,
                            LlmTestServerProviderCapability.Images,
                        ],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.EmbeddingsCreate,
                        Method = "POST",
                        Path = "/openai/v1/embeddings",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/embeddings/methods/create/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Embeddings],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.ImagesCreate,
                        Method = "POST",
                        Path = "/openai/v1/images/generations",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/images/methods/generate/",
                        SupportsStreaming = true,
                        ImplementedByHosting = true,
                        Capabilities =
                        [
                            LlmTestServerProviderCapability.Images,
                            LlmTestServerProviderCapability.StreamingImages,
                        ],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.ImagesEditsCreate,
                        Method = "POST",
                        Path = "/openai/v1/images/edits",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/images/methods/edit/",
                        SupportsStreaming = true,
                        ImplementedByHosting = true,
                        Capabilities =
                        [
                            LlmTestServerProviderCapability.Images,
                            LlmTestServerProviderCapability.StreamingImages,
                        ],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.ImagesVariationsCreate,
                        Method = "POST",
                        Path = "/openai/v1/images/variations",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/images/methods/create_variation/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Images],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.AudioSpeechCreate,
                        Method = "POST",
                        Path = "/openai/v1/audio/speech",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/audio/subresources/speech/methods/create/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Audio],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.AudioTranscriptionsCreate,
                        Method = "POST",
                        Path = "/openai/v1/audio/transcriptions",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/audio/subresources/transcriptions/methods/create/",
                        SupportsStreaming = true,
                        ImplementedByHosting = true,
                        Capabilities =
                        [
                            LlmTestServerProviderCapability.Audio,
                            LlmTestServerProviderCapability.StreamingAudio,
                        ],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.AudioTranslationsCreate,
                        Method = "POST",
                        Path = "/openai/v1/audio/translations",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/audio/subresources/translations/methods/create/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Audio],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.VideosCreate,
                        Method = "POST",
                        Path = "/openai/v1/videos",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/videos/methods/create/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.VideosList,
                        Method = "GET",
                        Path = "/openai/v1/videos",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/videos/methods/list/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.VideosRetrieve,
                        Method = "GET",
                        Path = "/openai/v1/videos/{videoId}",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/videos/methods/retrieve/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.VideosDelete,
                        Method = "DELETE",
                        Path = "/openai/v1/videos/{videoId}",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/videos/methods/delete/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.VideosContentRetrieve,
                        Method = "GET",
                        Path = "/openai/v1/videos/{videoId}/content",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/videos/methods/download_content/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.VideosEditsCreate,
                        Method = "POST",
                        Path = "/openai/v1/videos/edits",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/videos/methods/edit/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.VideosExtensionsCreate,
                        Method = "POST",
                        Path = "/openai/v1/videos/extensions",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/videos/methods/extend/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.VideosRemix,
                        Method = "POST",
                        Path = "/openai/v1/videos/{videoId}/remix",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/videos/methods/remix/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.VideosCharactersCreate,
                        Method = "POST",
                        Path = "/openai/v1/videos/characters",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/videos/methods/create_character/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenAI.VideosCharactersRetrieve,
                        Method = "GET",
                        Path = "/openai/v1/videos/characters/{characterId}",
                        DocumentationUrl =
                            "https://developers.openai.com/api/reference/resources/videos/methods/get_character/",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Video],
                    },
                ],
            },
        };
}
