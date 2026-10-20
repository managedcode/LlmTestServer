using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.Bedrock;

public static class BedrockCompatibility
{
    public const string ProviderId = LlmTestServerCompatibilityTags.Bedrock;

    public static LlmTestServerProviderProfile Profile { get; } =
        new()
        {
            Id = ProviderId,
            DisplayName = "Amazon Bedrock",
            Protocol = LlmTestServerProtocolFamily.Bedrock,
            DefaultEndpointPath = "/bedrock/model/{modelId}/converse",
            Capabilities =
            [
                LlmTestServerProviderCapability.Chat,
                LlmTestServerProviderCapability.StreamingChat,
                LlmTestServerProviderCapability.Tools,
                LlmTestServerProviderCapability.StructuredOutput,
                LlmTestServerProviderCapability.Embeddings,
                LlmTestServerProviderCapability.Images,
            ],
            CompatibilityTags =
            [
                LlmTestServerCompatibilityTags.AmazonBedrock,
                LlmTestServerCompatibilityTags.AwsBedrock,
                ProviderId,
            ],
            ApiContract = new()
            {
                DocumentationUrl =
                    "https://docs.aws.amazon.com/bedrock/latest/APIReference/welcome.html",
                DocumentationRetrievedOn = "2026-09-07",
                DocumentationVersion = "bedrock-runtime",
                Operations =
                [
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Bedrock.Converse,
                        Method = "POST",
                        Path = "/bedrock/model/{modelId}/converse",
                        DocumentationUrl =
                            "https://docs.aws.amazon.com/bedrock/latest/APIReference/API_runtime_Converse.html",
                        ImplementedByHosting = true,
                        Capabilities =
                        [
                            LlmTestServerProviderCapability.Chat,
                            LlmTestServerProviderCapability.Tools,
                            LlmTestServerProviderCapability.StructuredOutput,
                        ],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Bedrock.ConverseStream,
                        Method = "POST",
                        Path = "/bedrock/model/{modelId}/converse-stream",
                        DocumentationUrl =
                            "https://docs.aws.amazon.com/bedrock/latest/APIReference/API_runtime_ConverseStream.html",
                        SupportsStreaming = true,
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.StreamingChat, LlmTestServerProviderCapability.Tools, LlmTestServerProviderCapability.StructuredOutput],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Bedrock.InvokeModel,
                        Method = "POST",
                        Path = "/bedrock/model/{modelId}/invoke",
                        DocumentationUrl =
                            "https://docs.aws.amazon.com/bedrock/latest/APIReference/API_runtime_InvokeModel.html",
                        ImplementedByHosting = true,
                        Capabilities =
                        [
                            LlmTestServerProviderCapability.Embeddings,
                            LlmTestServerProviderCapability.Images,
                        ],
                    },
                    new()
                    {
                        Id = LlmTestServerProviderOperationIds.Bedrock.InvokeModelWithResponseStream,
                        Method = "POST",
                        Path = "/bedrock/model/{modelId}/invoke-with-response-stream",
                        DocumentationUrl =
                            "https://docs.aws.amazon.com/bedrock/latest/APIReference/API_runtime_InvokeModelWithResponseStream.html",
                        SupportsStreaming = true,
                        ImplementedByHosting = true,
                        Capabilities = [LlmTestServerProviderCapability.StreamingChat],
                    },
                ],
            },
        };
}
