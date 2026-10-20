using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Json;
using System.Text.Json;
using Azure;
using Azure.AI.Inference;
using Azure.Core.Pipeline;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using FoundryChatClient = Azure.AI.Inference.ChatCompletionsClient;
using FoundryEmbeddingClient = Azure.AI.Inference.EmbeddingsClient;
using OpenAiUserChatMessage = OpenAI.Chat.UserChatMessage;
using ProviderRoutes = ManagedCode.LlmTestServer.Providers.LlmTestServerProviderRouteNamespaces;

namespace ManagedCode.LlmTestServer.Tests.Azure;

public sealed class AzureSdkCompatibilityTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task AzureOpenAiClient_CanUseDeploymentChatAndEmbeddingsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .RequireBearerToken("test-key")
                .AddModel("azure-chat", LlmTestServerModelKind.Chat)
                .AddModel("azure-embedding", LlmTestServerModelKind.Embedding)
                .WithDefaultEmbeddingVector(0.25f, 0.5f)
                .AddChatScenario(
                    "azure-sdk-blue-whale",
                    scenario => scenario
                        .ForModel("azure-chat")
                        .WhenUserContains("azure sdk")
                        .Responds("azure blue whale")
                        .Responds("azure blue whale")
                ));
        using var httpClient = host.GetTestClient();
        var azureClient = new AzureOpenAIClient(
            ProviderEndpoint(httpClient, ProviderRoutes.AzureOpenAI, includeTrailingSlash: true),
            new ApiKeyCredential("test-key"),
            new AzureOpenAIClientOptions
            {
                Transport = new HttpClientPipelineTransport(httpClient),
            }
        );

        var chatClient = azureClient.GetChatClient("azure-chat");
        var embeddingClient = azureClient.GetEmbeddingClient("azure-embedding");

        var chat = await chatClient.CompleteChatAsync(
            [new OpenAiUserChatMessage("hello from azure sdk")],
            cancellationToken: CancellationToken.None
        );
        var streamedContent = new List<string>();
        await foreach (
            var update in chatClient.CompleteChatStreamingAsync(
                    [new OpenAiUserChatMessage("stream from azure sdk")],
                    cancellationToken: CancellationToken.None
                )
        )
        {
            streamedContent.AddRange(update.ContentUpdate.Select(part => part.Text));
        }

        var embedding = await embeddingClient.GenerateEmbeddingAsync(
            "embedding input",
            cancellationToken: CancellationToken.None
        );
        var embeddingValues = embedding.Value.ToFloats().ToArray();

        await Assert.That(chat.Value.Content[0].Text).IsEqualTo("azure blue whale");
        await Assert.That(string.Concat(streamedContent)).IsEqualTo("azure blue whale");
        await Assert.That(embeddingValues).IsEquivalentTo(new[] { 0.25f, 0.5f });
    }

    [Test]
    public async Task AzureAiInferenceClients_CanUseFoundryChatAndEmbeddingsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .RequireBearerToken("test-key")
                .AddModel("foundry-chat", LlmTestServerModelKind.Chat)
                .AddModel("foundry-embedding", LlmTestServerModelKind.Embedding)
                .WithDefaultEmbeddingVector(0.75f, 0.875f)
                .AddChatScenario(
                    "foundry-sdk-blue-whale",
                    scenario => scenario
                        .ForModel("foundry-chat")
                        .WhenUserContains("foundry sdk")
                        .Responds("foundry blue whale")
                ));
        using var httpClient = host.GetTestClient();
        var options = new AzureAIInferenceClientOptions
        {
            Transport = new HttpClientTransport(httpClient),
        };
        var credential = new AzureKeyCredential("test-key");
        var foundryEndpoint = ProviderEndpoint(
            httpClient,
            ProviderRoutes.MicrosoftFoundry,
            includeTrailingSlash: false
        );
        var chatClient = new FoundryChatClient(foundryEndpoint, credential, options);
        var embeddingClient = new FoundryEmbeddingClient(foundryEndpoint, credential, options);

        var chat = await chatClient.CompleteAsync(
            new ChatCompletionsOptions([new ChatRequestUserMessage("hello from foundry sdk")])
            {
                Model = "foundry-chat",
            },
            CancellationToken.None
        );
        var embeddings = await embeddingClient.EmbedAsync(
            new EmbeddingsOptions(["first", "second"])
            {
                Model = "foundry-embedding",
            },
            CancellationToken.None
        );
        var foundryEmbedding = embeddings.Value.Data[0].Embedding.ToObjectFromJson<float[]>() ?? [];

        await Assert.That(chat.Value.Content).IsEqualTo("foundry blue whale");
        await Assert.That(embeddings.Value.Data).Count().IsEqualTo(2);
        await Assert.That(foundryEmbedding).IsEquivalentTo(new[] { 0.75f, 0.875f });
    }

    [Test]
    public async Task AzureOpenAiDeploymentRoutes_UseApiKeyAndDeploymentModelForModalitiesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .RequireBearerToken("test-key")
                .AddModel("azure-embedding", LlmTestServerModelKind.Embedding)
                .AddModel("azure-image", LlmTestServerModelKind.Image)
                .AddModel("azure-audio", LlmTestServerModelKind.Audio)
                .WithDefaultEmbeddingVector(0.125f, 0.25f));
        using var httpClient = host.GetTestClient();
        httpClient.DefaultRequestHeaders.Add("api-key", "test-key");

        var embedding = await httpClient.PostAsJsonAsync(
            "/azure-openai/openai/deployments/azure-embedding/embeddings?api-version=2024-10-21",
            new { input = "invoice", encoding_format = "base64" },
            _jsonOptions
        );
        var image = await httpClient.PostAsJsonAsync(
            "/azure-openai/openai/deployments/azure-image/images/generations?api-version=2024-10-21",
            new { prompt = "fixture image" },
            _jsonOptions
        );
        var audio = await httpClient.PostAsJsonAsync(
            "/azure-openai/openai/deployments/azure-audio/audio/speech?api-version=2024-10-21",
            new { input = "fixture audio", voice = "alloy" },
            _jsonOptions
        );

        embedding.EnsureSuccessStatusCode();
        image.EnsureSuccessStatusCode();
        audio.EnsureSuccessStatusCode();

        var embeddingJson = await embedding.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var imageJson = await image.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var audioBytes = await audio.Content.ReadAsByteArrayAsync();

        await Assert.That(embeddingJson.GetProperty("model").GetString()).IsEqualTo("azure-embedding");
        await Assert.That(
                embeddingJson.GetProperty("data")[0].GetProperty("embedding").GetString()
            )
            .IsNotNull()
            .And
            .IsNotEmpty();
        await Assert.That(imageJson.GetProperty("data")[0].GetProperty("b64_json").GetString())
            .IsNotNull()
            .And
            .IsNotEmpty();
        await Assert.That(audio.Content.Headers.ContentType?.MediaType).IsEqualTo("audio/wav");
        await Assert.That(audioBytes.Length).IsGreaterThan(0);
    }

    private static Uri ProviderEndpoint(
        HttpClient httpClient,
        string providerNamespace,
        bool includeTrailingSlash
    )
    {
        var path = providerNamespace.TrimStart('/');
        if (includeTrailingSlash)
        {
            path += "/";
        }

        return new Uri(httpClient.BaseAddress!, path);
    }
}
