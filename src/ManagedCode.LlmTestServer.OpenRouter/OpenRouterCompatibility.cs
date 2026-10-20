using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.OpenRouter;

public static class OpenRouterCompatibility
{
    public const string ProviderId = LlmTestServerCompatibilityTags.OpenRouter;

    public static LlmTestServerProviderProfile Profile { get; } =
        new()
        {
            Id = ProviderId,
            DisplayName = "OpenRouter",
            Protocol = LlmTestServerProtocolFamily.OpenRouter,
            DefaultEndpointPath = "/openrouter/api/v1/chat/completions",
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
                DocumentationUrl = "https://openrouter.ai/docs/api/reference/overview",
                DocumentationRetrievedOn = "2026-09-07",
                DocumentationVersion = "OpenAI-compatible /api/v1",
                Operations =
                [
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.OpenRouter.ChatCompletionsCreate,
                        Method = "POST",
                        Path = "/openrouter/api/v1/chat/completions",
                        DocumentationUrl = "https://openrouter.ai/docs/api/reference/overview",
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
                        Id = LlmTestServerProviderOperationIds.OpenRouter.ResponsesCreate,
                        Method = "POST",
                        Path = "/openrouter/api/v1/responses",
                        DocumentationUrl =
                            "https://openrouter.ai/docs/api/reference/responses/overview",
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
                        Id = LlmTestServerProviderOperationIds.OpenRouter.ModelsList,
                        Method = "GET",
                        Path = "/openrouter/api/v1/models",
                        DocumentationUrl =
                            "https://openrouter.ai/docs/api/api-reference/models/list-all-models-and-their-properties",
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.Models],
                    },
                ],
            },
        };
}
