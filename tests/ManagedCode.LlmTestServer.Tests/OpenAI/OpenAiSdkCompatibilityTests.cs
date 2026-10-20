using System.ClientModel;
using System.ClientModel.Primitives;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using OpenAI;
using OpenAiChatClient = OpenAI.Chat.ChatClient;
using OpenAiUserChatMessage = OpenAI.Chat.UserChatMessage;
using ProviderRoutes = ManagedCode.LlmTestServer.Providers.LlmTestServerProviderRouteNamespaces;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed class OpenAiSdkCompatibilityTests
{
    [Test]
    public async Task OpenAiChatClient_CanUseNamespacedChatStreamingAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .RequireBearerToken("test-key")
                .AddModel("openai-chat", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "openai-sdk-streaming-blue-whale",
                    scenario => scenario
                        .ForModel("openai-chat")
                        .WhenUserContains("openai sdk")
                        .Responds("openai blue whale", "openai ", "blue ", "whale")
                ));
        using var httpClient = host.GetTestClient();
        var chatClient = new OpenAiChatClient(
            "openai-chat",
            new ApiKeyCredential("test-key"),
            new OpenAIClientOptions
            {
                Endpoint = ProviderEndpoint(httpClient, ProviderRoutes.OpenAI, "/v1"),
                Transport = new HttpClientPipelineTransport(httpClient),
            }
        );

        var streamedContent = new List<string>();
        await foreach (
            var update in chatClient.CompleteChatStreamingAsync(
                    [new OpenAiUserChatMessage("stream from openai sdk")],
                    cancellationToken: CancellationToken.None
                )
        )
        {
            streamedContent.AddRange(update.ContentUpdate.Select(part => part.Text));
        }

        await Assert.That(string.Concat(streamedContent)).IsEqualTo("openai blue whale");
    }

    private static Uri ProviderEndpoint(
        HttpClient httpClient,
        string providerNamespace,
        string providerApiRoot
    )
    {
        var path = providerNamespace.TrimStart('/') + providerApiRoot;
        return new Uri(httpClient.BaseAddress!, path);
    }
}
