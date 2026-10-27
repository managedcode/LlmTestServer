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
                DocumentationRetrievedOn = "2026-10-27",
                DocumentationVersion = $"anthropic-version: {AnthropicWireMapper.CurrentVersion}; also supports {AnthropicWireMapper.SupportedVersion}",
                Operations =
                [
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Anthropic.MessagesCreate,
                        Method = "POST",
                        Path = "/anthropic/v1/messages",
                        DocumentationUrl = "https://platform.claude.com/docs/en/api/messages/create",
                        RequiredHeader = "anthropic-version",
                        ApiVersion = AnthropicWireMapper.CurrentVersion,
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
