using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.DeepSeek;

public static class DeepSeekCompatibility
{
    public const string ProviderId = LlmTestServerCompatibilityTags.DeepSeek;

    public static LlmTestServerProviderProfile Profile { get; } =
        new()
        {
            Id = ProviderId,
            DisplayName = "DeepSeek",
            Protocol = LlmTestServerProtocolFamily.DeepSeek,
            DefaultEndpointPath = "/deepseek/v1/chat/completions",
            Capabilities =
            [
                LlmTestServerProviderCapability.Models,
                LlmTestServerProviderCapability.Chat,
                LlmTestServerProviderCapability.StreamingChat,
                LlmTestServerProviderCapability.Tools,
                LlmTestServerProviderCapability.StructuredOutput,
            ],
            CompatibilityTags = [ProviderId, LlmTestServerCompatibilityTags.OpenAICompatible],
            ApiContract = new()
            {
                DocumentationUrl = "https://api-docs.deepseek.com/",
                DocumentationRetrievedOn = "2026-09-07",
                DocumentationVersion = "OpenAI-compatible v1",
                Operations =
                [
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.DeepSeek.ChatCompletionsCreate,
                        Method = "POST",
                        Path = "/deepseek/v1/chat/completions",
                        DocumentationUrl =
                            "https://api-docs.deepseek.com/api/create-chat-completion",
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
                        Id = LlmTestServerProviderOperationIds.DeepSeek.ModelsList,
                        Method = "GET",
                        Path = "/deepseek/models",
                        DocumentationUrl = "https://api-docs.deepseek.com/api/list-models",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Models],
                    },
                ],
            },
        };
}
