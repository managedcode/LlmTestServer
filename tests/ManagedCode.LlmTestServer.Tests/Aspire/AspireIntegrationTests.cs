using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure;
using Azure.AI.Inference;
using Azure.Core.Pipeline;
using global::Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Control;
using Microsoft.Extensions.AI;
using OpenAI;
using ExtensionsChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ExtensionsChatRole = Microsoft.Extensions.AI.ChatRole;
using FoundryChatClient = Azure.AI.Inference.ChatCompletionsClient;
using FoundryEmbeddingClient = Azure.AI.Inference.EmbeddingsClient;
using OpenAiChatClient = OpenAI.Chat.ChatClient;
using OpenAiUserChatMessage = OpenAI.Chat.UserChatMessage;
using ProviderRoutes = ManagedCode.LlmTestServer.Providers.LlmTestServerProviderRouteNamespaces;

namespace ManagedCode.LlmTestServer.Tests.AspireIntegration;

public sealed class AspireIntegrationTests
{
    private const string _resourceName = "llm-test-server";
    private const string _apiKey = "test-key";
    private const string _chatModel = "gpt-4.1-mini";
    private const string _embeddingModel = "text-embedding-3-small";
    private const string _imageModel = "gpt-image-1";
    private const string _audioModel = "gpt-4o-mini-tts";
    private const int _parallelResourceCount = 10;

    [Test]
    [Timeout(360_000)]
    public async Task AddLlmTestServer_BuildsAppHostInTestAndSupportsConfiguredClientsAsync(
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        var builder = DistributedApplicationTestingBuilder.Create([]);
        var llmTestServer = builder
            .AddLlmTestServer()
            .WithApiKey(_apiKey);
        var llmTestServerEndpointExpression = llmTestServer.GetHttpEndpoint().ToString();
        var openAiEndpointExpression = llmTestServer.GetOpenAiEndpoint();

        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync(_resourceName, timeout.Token);

        using var httpClient = app.CreateHttpClient(_resourceName, "http");
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        var controlClient = new LlmTestServerClient(httpClient, _apiKey);
        await controlClient.ConfigureAsync(
            options => options
                .RequireBearerToken(_apiKey)
                .UseChatModel(_chatModel)
                .UseEmbeddingModel(_embeddingModel)
                .UseImageModel(_imageModel)
                .UseAudioModel(_audioModel)
                .UseEmbeddingVector(0.25f, 0.5f)
                .UseChatScenario(
                    "aspire-sdk-blue-whale",
                    scenario => scenario
                        .ForModel(_chatModel)
                        .WhenUserContains("aspire sdk")
                        .Responds("aspire blue whale", "aspire ", "blue ", "whale")
                        .Responds("aspire blue whale", "aspire ", "blue ", "whale")
                        .Responds("aspire blue whale", "aspire ", "blue ", "whale")
                        .Responds("aspire blue whale", "aspire ", "blue ", "whale")
                        .Responds("aspire blue whale", "aspire ", "blue ", "whale")
                ),
            timeout.Token
        );

        using var chatClient = controlClient.CreateChatClient(_chatModel);
        using var embeddingGenerator = controlClient.CreateEmbeddingGenerator(_embeddingModel);
        using var imageGenerator = controlClient.CreateImageGenerator(_imageModel);

        var root = await httpClient.GetAsync("/", timeout.Token);
        var adminPage = await httpClient.GetStringAsync(LlmTestServerControlRoutes.Admin, timeout.Token);
        var models = await httpClient.GetFromJsonAsync<JsonElement>("/openai/v1/models", timeout.Token);
        var adminModels = await httpClient.GetFromJsonAsync<JsonElement>(
            LlmTestServerControlRoutes.Models,
            timeout.Token
        );
        var chat = await chatClient.GetResponseAsync(
            [new ExtensionsChatMessage(ExtensionsChatRole.User, "hello from aspire sdk")],
            cancellationToken: timeout.Token
        );
        var streamChunks = new List<string>();
        await foreach (
            var update in chatClient.GetStreamingResponseAsync(
                    [new ExtensionsChatMessage(ExtensionsChatRole.User, "stream from aspire sdk")],
                    cancellationToken: timeout.Token
                )
                .WithCancellation(timeout.Token)
        )
        {
            streamChunks.Add(update.Text);
        }

        var embeddings = await embeddingGenerator.GenerateAsync(
            ["first", "second"],
            cancellationToken: timeout.Token
        );
        var openAiEndpointValue =
            await openAiEndpointExpression.GetValueAsync(timeout.Token)
            ?? throw new InvalidOperationException("LLM Test Server OpenAI endpoint was not allocated.");
        var openAiEndpoint = new Uri(openAiEndpointValue);
        var openAiChatClient = new OpenAiChatClient(
            _chatModel,
            new ApiKeyCredential(_apiKey),
            new OpenAIClientOptions
            {
                Endpoint = openAiEndpoint,
            }
        );
        var openAiStreamChunks = new List<string>();
        await foreach (
            var update in openAiChatClient.CompleteChatStreamingAsync(
                    [new OpenAiUserChatMessage("stream from openai aspire sdk")],
                    cancellationToken: timeout.Token
                )
        )
        {
            openAiStreamChunks.AddRange(update.ContentUpdate.Select(part => part.Text));
        }

        var azureClient = new AzureOpenAIClient(
            ProviderEndpoint(httpClient, ProviderRoutes.AzureOpenAI, includeTrailingSlash: true),
            new ApiKeyCredential(_apiKey),
            new AzureOpenAIClientOptions
            {
                Transport = new HttpClientPipelineTransport(httpClient),
            }
        );
        var azureChatClient = azureClient.GetChatClient(_chatModel);
        var azureEmbeddingClient = azureClient.GetEmbeddingClient(_embeddingModel);
        var azureChat = await azureChatClient.CompleteChatAsync(
            [new OpenAiUserChatMessage("hello from azure aspire sdk")],
            cancellationToken: timeout.Token
        );
        var azureEmbedding = await azureEmbeddingClient.GenerateEmbeddingAsync(
            "azure embedding input",
            cancellationToken: timeout.Token
        );
        var azureEmbeddingValues = azureEmbedding.Value.ToFloats().ToArray();

        var foundryOptions = new AzureAIInferenceClientOptions
        {
            Transport = new HttpClientTransport(httpClient),
        };
        var foundryCredential = new AzureKeyCredential(_apiKey);
        var foundryEndpoint = ProviderEndpoint(
            httpClient,
            ProviderRoutes.MicrosoftFoundry,
            includeTrailingSlash: false
        );
        var foundryChatClient = new FoundryChatClient(
            foundryEndpoint,
            foundryCredential,
            foundryOptions
        );
        var foundryEmbeddingClient = new FoundryEmbeddingClient(
            foundryEndpoint,
            foundryCredential,
            foundryOptions
        );
        var foundryChat = await foundryChatClient.CompleteAsync(
            new ChatCompletionsOptions([new ChatRequestUserMessage("hello from foundry aspire sdk")])
            {
                Model = _chatModel,
            },
            timeout.Token
        );
        var foundryEmbeddings = await foundryEmbeddingClient.EmbedAsync(
            new EmbeddingsOptions(["first", "second"])
            {
                Model = _embeddingModel,
            },
            timeout.Token
        );
        var foundryEmbedding =
            foundryEmbeddings.Value.Data[0].Embedding.ToObjectFromJson<float[]>() ?? [];
        var image = await imageGenerator.GenerateAsync(
            new ImageGenerationRequest { Prompt = "compatibility marker" },
            cancellationToken: timeout.Token
        );
        var audio = await controlClient.GenerateAudioAsync(
            _audioModel,
            "audio fixture",
            timeout.Token
        );
        var assertions = await controlClient.GetAssertionsAsync(timeout.Token);
        var adminAssertions = await httpClient.GetFromJsonAsync<JsonElement>(
            LlmTestServerControlRoutes.Assertions,
            timeout.Token
        );

        await Assert.That(llmTestServerEndpointExpression).IsNotEmpty();
        await Assert.That(root.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(adminPage).Contains("LLM Test Server request dashboard");
        await Assert.That(adminPage).Contains(LlmTestServerControlRoutes.Models);
        await Assert.That(adminPage).Contains(LlmTestServerControlRoutes.Assertions);
        await Assert.That(adminPage).Contains("Total tokens");
        await Assert.That(models.GetProperty("data").GetArrayLength()).IsGreaterThanOrEqualTo(4);
        await Assert.That(adminModels.GetArrayLength()).IsGreaterThanOrEqualTo(4);
        await Assert.That(chat.Text).IsEqualTo("aspire blue whale");
        await Assert.That(string.Concat(streamChunks)).IsEqualTo("aspire blue whale");
        await Assert.That(embeddings).Count().IsEqualTo(2);
        await Assert.That(embeddings[0].Vector.ToArray()).IsEquivalentTo(new[] { 0.25f, 0.5f });
        await Assert.That(openAiEndpoint.AbsolutePath).IsEqualTo("/openai/v1");
        await Assert.That(string.Concat(openAiStreamChunks)).IsEqualTo("aspire blue whale");
        await Assert.That(azureChat.Value.Content[0].Text).IsEqualTo("aspire blue whale");
        await Assert.That(azureEmbeddingValues).IsEquivalentTo(new[] { 0.25f, 0.5f });
        await Assert.That(foundryChat.Value.Content).IsEqualTo("aspire blue whale");
        await Assert.That(foundryEmbeddings.Value.Data).Count().IsEqualTo(2);
        await Assert.That(foundryEmbedding).IsEquivalentTo(new[] { 0.25f, 0.5f });
        await Assert.That(image.Contents).Count().IsEqualTo(1);
        await Assert.That(audio.Bytes).Count().IsGreaterThan(0);
        await Assert.That(audio.MediaType).IsEqualTo("audio/wav");
        await Assert.That(assertions.Matched).IsGreaterThanOrEqualTo(9);
        await Assert.That(adminAssertions.GetProperty("matched").GetInt32()).IsGreaterThanOrEqualTo(9);
        await Assert.That(adminAssertions.GetProperty("totalTokens").GetInt32()).IsGreaterThan(0);
    }

    [Test]
    [Timeout(600_000)]
    public async Task AddLlmTestServer_StartsTenDistinctResourcesAndHealthEndpointsStayAliveAsync(
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(9));

        var builder = DistributedApplicationTestingBuilder.Create([]);
        var resourceNames = Enumerable
            .Range(1, _parallelResourceCount)
            .Select(index => $"{_resourceName}-{index:00}")
            .ToArray();

        foreach (var resourceName in resourceNames)
        {
            builder
                .AddLlmTestServer(resourceName)
                .WithApiKey($"{_apiKey}-{resourceName}");
        }

        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);

        await Task.WhenAll(
            resourceNames.Select(
                async resourceName =>
                    await app.ResourceNotifications.WaitForResourceHealthyAsync(
                        resourceName,
                        timeout.Token
                    )
            )
        );

        var healthResults = await Task.WhenAll(
            resourceNames.Select(resourceName => ProbeHealthAsync(resourceName))
        );
        var baseAddresses = healthResults
            .Select(result => result.BaseAddress)
            .ToArray();

        await Assert.That(resourceNames.Distinct()).Count().IsEqualTo(_parallelResourceCount);
        await Assert.That(healthResults).Count().IsEqualTo(_parallelResourceCount);
        await Assert.That(healthResults.Select(result => result.ResourceName)).IsEquivalentTo(resourceNames);
        await Assert.That(baseAddresses.Distinct()).Count().IsEqualTo(_parallelResourceCount);

        foreach (var result in healthResults)
        {
            await Assert.That(result.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(result.BaseAddress).IsNotEmpty();
        }

        async Task<HealthProbeResult> ProbeHealthAsync(string resourceName)
        {
            using var httpClient = app.CreateHttpClient(resourceName, LlmTestServerResource.HttpEndpointName);
            var health = await httpClient.GetAsync("/health", timeout.Token);

            return new HealthProbeResult(
                resourceName,
                httpClient.BaseAddress?.ToString() ?? string.Empty,
                health.StatusCode
            );
        }
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

    private sealed record HealthProbeResult(
        string ResourceName,
        string BaseAddress,
        HttpStatusCode StatusCode
    );
}
