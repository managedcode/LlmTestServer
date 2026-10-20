using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.Anthropic;

public static class AnthropicCompatibility
{
    public const string ProviderId = LlmTestServerCompatibilityTags.Anthropic;

    public static LlmTestServerProviderProfile Profile { get; } =
        new()
        {
            Id = ProviderId,
            DisplayName = "Anthropic",
            Protocol = LlmTestServerProtocolFamily.AnthropicMessages,
            DefaultEndpointPath = "/anthropic/v1/messages",
            Capabilities =
            [
                LlmTestServerProviderCapability.Chat,
                LlmTestServerProviderCapability.StreamingChat,
                LlmTestServerProviderCapability.Tools,
                LlmTestServerProviderCapability.StructuredOutput,
            ],
            CompatibilityTags =
            [
                ProviderId,
                LlmTestServerCompatibilityTags.Claude,
                LlmTestServerCompatibilityTags.MessagesApi,
            ],
            ApiContract = new()
            {
                DocumentationUrl = "https://platform.claude.com/docs/en/api/messages/create",
                DocumentationRetrievedOn = "2026-09-07",
                DocumentationVersion = "anthropic-version: 2023-06-01",
                Operations =
                [
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Anthropic.MessagesCreate,
                        Method = "POST",
                        Path = "/anthropic/v1/messages",
                        DocumentationUrl = "https://platform.claude.com/docs/en/api/messages/create",
                        RequiredHeader = "anthropic-version",
                        ApiVersion = "2023-06-01",
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
                ],
            },
        };
}
