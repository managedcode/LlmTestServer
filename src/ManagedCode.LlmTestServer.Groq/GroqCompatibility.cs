using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.Groq;

public static class GroqCompatibility
{
    public const string ProviderId = LlmTestServerCompatibilityTags.Groq;

    public static LlmTestServerProviderProfile Profile { get; } =
        new()
        {
            Id = ProviderId,
            DisplayName = "Groq",
            Protocol = LlmTestServerProtocolFamily.Groq,
            DefaultEndpointPath = "/groq/openai/v1/chat/completions",
            Capabilities =
            [
                LlmTestServerProviderCapability.Models,
                LlmTestServerProviderCapability.Chat,
                LlmTestServerProviderCapability.StreamingChat,
                LlmTestServerProviderCapability.Tools,
                LlmTestServerProviderCapability.StructuredOutput,
                LlmTestServerProviderCapability.Audio,
            ],
            CompatibilityTags = [ProviderId, LlmTestServerCompatibilityTags.OpenAICompatible],
            ApiContract = new()
            {
                DocumentationUrl = "https://console.groq.com/docs/api-reference",
                DocumentationRetrievedOn = "2026-09-07",
                DocumentationVersion = "OpenAI-compatible v1",
                Operations =
                [
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Groq.ChatCompletionsCreate,
                        Method = "POST",
                        Path = "/groq/openai/v1/chat/completions",
                        DocumentationUrl = "https://console.groq.com/docs/api-reference",
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
                        Id = LlmTestServerProviderOperationIds.Groq.ResponsesCreate,
                        Method = "POST",
                        Path = "/groq/openai/v1/responses",
                        DocumentationUrl = "https://console.groq.com/docs/api-reference",
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
                        Id = LlmTestServerProviderOperationIds.Groq.AudioSpeechCreate,
                        Method = "POST",
                        Path = "/groq/openai/v1/audio/speech",
                        DocumentationUrl = "https://console.groq.com/docs/api-reference",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Audio],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Groq.AudioTranscriptionsCreate,
                        Method = "POST",
                        Path = "/groq/openai/v1/audio/transcriptions",
                        DocumentationUrl = "https://console.groq.com/docs/api-reference",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Audio],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Groq.AudioTranslationsCreate,
                        Method = "POST",
                        Path = "/groq/openai/v1/audio/translations",
                        DocumentationUrl = "https://console.groq.com/docs/api-reference",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Audio],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Groq.ModelsList,
                        Method = "GET",
                        Path = "/groq/openai/v1/models",
                        DocumentationUrl = "https://console.groq.com/docs/models",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Models],
                    },
                ],
            },
        };
}
