using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Images;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.Azure;

// The official stable SDK still marks its Responses surface experimental.
#pragma warning disable OPENAI001
public sealed class AzureV1SdkCompatibilityTests
{
    [Test]
    public async Task AzureOpenAiV1Images_UseOfficialOpenAiSdkAndReturnImageBytesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
            .RequireBearerToken("test-key")
            .AddModel("gpt-image-1", LlmTestServerModelKind.Image));
        using var http = host.GetTestClient();
        var client = new OpenAIClient(new ApiKeyCredential("test-key"), new OpenAIClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, LlmTestServerProviderRouteNamespaces.AzureOpenAI + "/openai/v1/"),
            Transport = new HttpClientPipelineTransport(http),
        });
        var imageClient = client.GetImageClient("gpt-image-1");
        var result = await imageClient.GenerateImageAsync(
            "draw a blue compatibility marker",
            new ImageGenerationOptions
            {
                ResponseFormat = GeneratedImageFormat.Bytes,
                Size = GeneratedImageSize.W1024xH1024,
                Quality = new GeneratedImageQuality("medium"),
            });

        await Assert.That(result.Value.ImageBytes.ToArray().Length).IsGreaterThan(0);
    }

    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry)]
    public async Task V1ChatAndEmbeddings_UseOfficialSdkWithoutApiVersionAsync(string provider)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
            .RequireBearerToken("test-key")
            .AddModel("gpt-4.1-mini", LlmTestServerModelKind.Chat)
            .AddModel("text-embedding-3-small", LlmTestServerModelKind.Embedding)
            .WithDefaultEmbeddingVector(0.25f, 0.5f)
            .AddChatScenario("v1-chat", scenario => scenario
                .ForModel("gpt-4.1-mini").WhenUserContains("whale")
                .Responds("blue whale").Responds("blue whale", "blue ", "whale")));
        using var http = host.GetTestClient();
        var sdk = new OpenAIClient(new ApiKeyCredential("test-key"), new OpenAIClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, provider.TrimStart('/') + "/openai/v1/"),
            Transport = new HttpClientPipelineTransport(http),
        });
        var chat = sdk.GetChatClient("gpt-4.1-mini");
        var completion = await chat.CompleteChatAsync([new UserChatMessage("whale")]);
        await Assert.That(completion.Value.Content[0].Text).IsEqualTo("blue whale");
        var chunks = new List<string>();
        await foreach (var update in chat.CompleteChatStreamingAsync([new UserChatMessage("whale")]))
        {
            chunks.AddRange(update.ContentUpdate.Select(part => part.Text));
        }

        await Assert.That(string.Concat(chunks)).IsEqualTo("blue whale");
        var embedding = await sdk.GetEmbeddingClient("text-embedding-3-small").GenerateEmbeddingAsync("blue whale");
        await Assert.That(embedding.Value.ToFloats().ToArray()).IsEquivalentTo(new[] { 0.25f, 0.5f });
    }

    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry)]
    [Arguments(LlmTestServerProviderRouteNamespaces.OpenAI)]
    public async Task V1Responses_UseOfficialSdkAndStreamAsync(string provider)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
            .RequireBearerToken("test-key")
            .AddModel("gpt-4.1-mini", LlmTestServerModelKind.Chat)
            .AddChatScenario("v1-response", scenario => scenario
                .ForModel("gpt-4.1-mini").WhenUserContains("whale")
                .Responds("blue whale").Responds("blue whale", "blue ", "whale")));
        using var http = host.GetTestClient();
        var apiRoot = provider == LlmTestServerProviderRouteNamespaces.OpenAI ? "/v1/" : "/openai/v1/";
        var client = new ResponsesClient(new ApiKeyCredential("test-key"), new ResponsesClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, provider.TrimStart('/') + apiRoot),
            Transport = new HttpClientPipelineTransport(http),
        });
        var options = new CreateResponseOptions
        {
            Model = "gpt-4.1-mini",
            InputItems = { ResponseItem.CreateUserMessageItem("whale") },
        };
        var response = await client.CreateResponseAsync(options);
        await Assert.That(response.Value.GetOutputText()).IsEqualTo("blue whale");
        var chunks = new List<string>();
        options.StreamingEnabled = true;
        await foreach (var update in client.CreateResponseStreamingAsync(options))
        {
            if (update is StreamingResponseOutputTextDeltaUpdate text)
            {
                chunks.Add(text.Delta);
            }
        }

        await Assert.That(string.Concat(chunks)).IsEqualTo("blue whale");
    }

    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry)]
    public async Task V1Responses_AcceptsApiKeyAndRejectsMissingCredentialsAsync(string provider)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
            .RequireBearerToken("test-key")
            .AddModel("gpt-4.1-mini", LlmTestServerModelKind.Chat)
            .AddChatScenario("v1-auth", scenario => scenario.ForModel("gpt-4.1-mini")
                .WhenUserContains("whale").Responds("blue whale")));
        using var client = host.GetTestClient();
        var path = provider + "/openai/v1/responses";
        var request = new { model = "gpt-4.1-mini", input = "whale" };
        using var unauthorized = await client.PostAsJsonAsync(path, request);
        await Assert.That(unauthorized.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Add("api-key", "test-key");
        using var response = await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString())
            .IsEqualTo("blue whale");
    }
}
