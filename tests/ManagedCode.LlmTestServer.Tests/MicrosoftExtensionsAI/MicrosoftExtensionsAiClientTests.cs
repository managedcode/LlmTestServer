using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.AI;

namespace ManagedCode.LlmTestServer.Tests.MicrosoftExtensionsAI;

public sealed class MicrosoftExtensionsAiClientTests
{
    [Test]
    public async Task MicrosoftExtensionsAiClients_InvokeLlmTestServerThroughOpenAiCompatibilityEndpointsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .WithDefaultEmbeddingVector(0.125f, 0.25f, 0.5f)
                .AddChatScenario(
                    "extensions-ai-blue-whale",
                    scenario => scenario
                        .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                        .WhenUserContains("largest animal")
                        .Responds("blue whale", "blue ", "whale")
                        .Responds("blue whale", "blue ", "whale")
                ));
        using var httpClient = host.GetTestClient();
        using IChatClient chatClient = new LlmTestServerChatClient(httpClient);
        using IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator =
            new LlmTestServerEmbeddingGenerator(httpClient);
        using IImageGenerator imageGenerator = new LlmTestServerImageGenerator(httpClient);

        var chatResponse = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "What is the largest animal?")]
        );
        var streamUpdates = new List<string>();
        await foreach (
            var update in chatClient.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "What is the largest animal?")]
            )
        )
        {
            streamUpdates.Add(update.Text);
        }

        var embeddings = await embeddingGenerator.GenerateAsync(["alpha", "beta"]);
        var image = await imageGenerator.GenerateAsync(
            new ImageGenerationRequest { Prompt = "blue compatibility marker" }
        );

        await Assert.That(chatResponse.Text).IsEqualTo("blue whale");
        await Assert.That(string.Concat(streamUpdates)).IsEqualTo("blue whale");
        await Assert.That(embeddings).Count().IsEqualTo(2);
        await Assert.That(embeddings[0].Vector.Length).IsEqualTo(3);
        await Assert.That(image.Contents).Count().IsEqualTo(1);
        await Assert.That(image.Contents[0]).IsTypeOf<DataContent>();
    }
}
