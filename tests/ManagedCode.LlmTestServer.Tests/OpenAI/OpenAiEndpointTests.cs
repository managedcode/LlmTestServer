using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Control;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed class OpenAiEndpointTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task OpenAiEndpoints_ExposeModelsAndAllModalitiesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .RequireBearerToken("test-key")
                .AddModel("chat-model", LlmTestServerModelKind.Chat)
                .AddModel("embedding-model", LlmTestServerModelKind.Embedding)
                .AddModel("image-model", LlmTestServerModelKind.Image)
                .AddModel("audio-model", LlmTestServerModelKind.Audio)
                .AddModel("video-model", LlmTestServerModelKind.Video)
                .WithDefaultEmbeddingVector(0.25f, 0.5f)
                .WithDefaultTranscriptionText("audio transcript")
                .AddChatScenario(
                    "endpoint-blue-whale",
                    scenario => scenario
                        .ForModel("chat-model")
                        .WhenUserContains("largest animal")
                        .Responds("blue whale", "blue ", "whale")
                ));
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-key");

        var models = await client.GetFromJsonAsync<JsonElement>("/openai/v1/models", _jsonOptions);
        var chat = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new
            {
                model = "chat-model",
                messages = new[] { new { role = "user", content = "largest animal?" } },
            },
            _jsonOptions
        );
        var embedding = await client.PostAsJsonAsync(
            "/openai/v1/embeddings",
            new { model = "embedding-model", input = new[] { "alpha", "beta" } },
            _jsonOptions
        );
        var image = await client.PostAsJsonAsync(
            "/openai/v1/images/generations",
            new { model = "image-model", prompt = "draw a blue compatibility marker" },
            _jsonOptions
        );
        var audio = await client.PostAsJsonAsync(
            "/openai/v1/audio/speech",
            new { model = "audio-model", input = "speak this fixture", voice = "alloy" },
            _jsonOptions
        );
        using var transcriptionContent = CreateTranscriptionContent("audio-model");
        var transcription = await client.PostAsync(
            "/openai/v1/audio/transcriptions",
            transcriptionContent
        );
        using var videoContent = CreateVideoContent(
            "video-model",
            "generate a blue compatibility marker"
        );
        var video = await client.PostAsync("/openai/v1/videos", videoContent);

        chat.EnsureSuccessStatusCode();
        embedding.EnsureSuccessStatusCode();
        image.EnsureSuccessStatusCode();
        audio.EnsureSuccessStatusCode();
        transcription.EnsureSuccessStatusCode();
        video.EnsureSuccessStatusCode();

        var chatJson = await chat.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var embeddingJson = await embedding.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var imageJson = await image.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var audioBytes = await audio.Content.ReadAsByteArrayAsync();
        var transcriptionJson = await transcription.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var videoJson = await video.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(models.GetProperty("data").GetArrayLength()).IsGreaterThanOrEqualTo(5);
        await Assert.That(
                chatJson
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()
            )
            .IsEqualTo("blue whale");
        await Assert.That(embeddingJson.GetProperty("data").GetArrayLength()).IsEqualTo(2);
        await Assert.That(
                embeddingJson.GetProperty("data")[0].GetProperty("embedding").GetArrayLength()
            )
            .IsEqualTo(2);
        await Assert.That(
                imageJson.GetProperty("data")[0].GetProperty("b64_json").GetString()
            )
            .IsNotNull()
            .And
            .IsNotEmpty();
        await Assert.That(audio.Content.Headers.ContentType?.MediaType).IsEqualTo("audio/wav");
        await Assert.That(audioBytes.Length).IsGreaterThan(0);
        await Assert.That(transcriptionJson.GetProperty("text").GetString())
            .IsEqualTo("audio transcript");
        await Assert.That(videoJson.GetProperty("object").GetString()).IsEqualTo("video");
        await Assert.That(videoJson.GetProperty("model").GetString()).IsEqualTo("video-model");
        await Assert.That(videoJson.GetProperty("prompt").GetString())
            .IsEqualTo("generate a blue compatibility marker");

        var assertions = await client.GetFromJsonAsync<JsonElement>(
            LlmTestServerControlRoutes.Assertions,
            _jsonOptions
        );
        await Assert.That(assertions.GetProperty("matched").GetInt32()).IsEqualTo(6);
    }

    [Test]
    public async Task StreamingChatCompletion_ReturnsServerSentChunksAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options.AddChatScenario(
                "streaming-blue-whale",
                scenario => scenario
                    .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                    .WhenUserContains("stream")
                    .Responds("blue whale", "blue ", "whale")
            ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new
            {
                model = LlmTestServerKnownModelIds.Gpt41Mini,
                stream = true,
                messages = new[] { new { role = "user", content = "stream response" } },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(body).Contains("data: ");
        await Assert.That(body).Contains("blue ");
        await Assert.That(body).Contains("whale");
        await Assert.That(body).Contains("[DONE]");

        var ids = body
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("data: {", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line["data: ".Length..]).RootElement.GetProperty("id").GetString())
            .Distinct()
            .ToList();
        await Assert.That(ids).Count().IsEqualTo(1);
    }

    [Test]
    public async Task OpenAiUsage_IncludesReasoningTokenDetailsForReasoningModelsAsync()
    {
        const string reasoningModel = "gpt-5-nano";
        const int reasoningTokens = 19;
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddReasoningChatModel(reasoningModel, reasoningTokens)
                .AddChatScenario(
                    "reasoning-usage",
                    scenario => scenario
                        .ForModel(reasoningModel)
                        .WhenUserContains("reasoning usage")
                        .Responds("reasoned response")
                        .Responds("reasoned response")
                ));
        using var client = host.GetTestClient();

        var chat = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new
            {
                model = reasoningModel,
                messages = new[] { new { role = "user", content = "reasoning usage" } },
            },
            _jsonOptions
        );
        var responses = await client.PostAsJsonAsync(
            "/openai/v1/responses",
            new
            {
                model = reasoningModel,
                input = new[]
                {
                    new
                    {
                        role = "user",
                        content = "reasoning usage",
                    },
                },
            },
            _jsonOptions
        );

        chat.EnsureSuccessStatusCode();
        responses.EnsureSuccessStatusCode();

        var chatPayload = await chat.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var responsePayload = await responses.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var chatUsage = chatPayload.GetProperty("usage");
        var responseUsage = responsePayload.GetProperty("usage");

        await Assert.That(
                chatUsage
                    .GetProperty("completion_tokens_details")
                    .GetProperty("reasoning_tokens")
                    .GetInt32()
            )
            .IsEqualTo(reasoningTokens);
        await Assert.That(
                responseUsage
                    .GetProperty("output_tokens_details")
                    .GetProperty("reasoning_tokens")
                    .GetInt32()
            )
            .IsEqualTo(reasoningTokens);
        await Assert.That(chatUsage.GetProperty("completion_tokens").GetInt32())
            .IsGreaterThan(reasoningTokens);
        await Assert.That(responseUsage.GetProperty("output_tokens").GetInt32())
            .IsGreaterThan(reasoningTokens);
    }

    [Test]
    public async Task OpenAiUsage_IncludesPromptCacheDetailsForChatAndResponsesAsync()
    {
        var chatPrompt = CreatePromptWithAtLeastTokens(1100);
        var responsesPrompt = CreatePromptWithAtLeastTokens(1100) + " responses";
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("cache-chat", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "openai-chat-cache",
                    scenario => scenario
                        .ForModel("cache-chat")
                        .WhenUserContains("cache turn")
                        .Responds("first")
                        .Responds("second")
                )
                .AddChatScenario(
                    "openai-responses-cache",
                    scenario => scenario
                        .ForModel("cache-chat")
                        .WhenUserContains("responses cached prompt")
                        .Responds("first")
                        .Responds("second")
                ));
        using var client = host.GetTestClient();

        var firstChat = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new
            {
                model = "cache-chat",
                messages = new[]
                {
                    new { role = "system", content = chatPrompt },
                    new { role = "user", content = "cache turn one" },
                },
            },
            _jsonOptions
        );
        var secondChat = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new
            {
                model = "cache-chat",
                messages = new[]
                {
                    new { role = "system", content = chatPrompt },
                    new { role = "user", content = "cache turn two" },
                },
            },
            _jsonOptions
        );
        var firstResponse = await client.PostAsJsonAsync(
            "/openai/v1/responses",
            new
            {
                model = "cache-chat",
                instructions = responsesPrompt,
                input = "responses cached prompt one",
            },
            _jsonOptions
        );
        var secondResponse = await client.PostAsJsonAsync(
            "/openai/v1/responses",
            new
            {
                model = "cache-chat",
                instructions = responsesPrompt,
                input = "responses cached prompt two",
            },
            _jsonOptions
        );

        firstChat.EnsureSuccessStatusCode();
        secondChat.EnsureSuccessStatusCode();
        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();

        var firstChatUsage = (await firstChat.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions))
            .GetProperty("usage");
        var secondChatUsage = (await secondChat.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions))
            .GetProperty("usage");
        var firstResponseUsage = (await firstResponse.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions))
            .GetProperty("usage");
        var secondResponseUsage = (await secondResponse.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions))
            .GetProperty("usage");

        await Assert.That(
                firstChatUsage
                    .GetProperty("prompt_tokens_details")
                    .GetProperty("cached_tokens")
                    .GetInt32()
            )
            .IsEqualTo(0);
        await Assert.That(
                secondChatUsage
                    .GetProperty("prompt_tokens_details")
                    .GetProperty("cached_tokens")
                    .GetInt32()
            )
            .IsGreaterThanOrEqualTo(1024);
        await Assert.That(secondChatUsage.GetProperty("prompt_tokens").GetInt32())
            .IsGreaterThan(
                secondChatUsage
                    .GetProperty("prompt_tokens_details")
                    .GetProperty("cached_tokens")
                    .GetInt32()
            );

        await Assert.That(
                firstResponseUsage
                    .GetProperty("input_tokens_details")
                    .GetProperty("cached_tokens")
                    .GetInt32()
            )
            .IsEqualTo(0);
        await Assert.That(
                secondResponseUsage
                    .GetProperty("input_tokens_details")
                    .GetProperty("cached_tokens")
                    .GetInt32()
            )
            .IsGreaterThanOrEqualTo(1024);
        await Assert.That(secondResponseUsage.GetProperty("input_tokens").GetInt32())
            .IsGreaterThan(
                secondResponseUsage
                    .GetProperty("input_tokens_details")
                    .GetProperty("cached_tokens")
                    .GetInt32()
            );
    }

    [Test]
    public async Task BearerTokenRequirement_ReturnsOpenAiErrorShapeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options.RequireBearerToken("test-key"));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/openai/v1/embeddings",
            new { model = LlmTestServerKnownModelIds.TextEmbedding3Small, input = "hello" },
            _jsonOptions
        );

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        await Assert.That(payload.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_api_key");
    }

    [Test]
    public async Task ControlEndpoints_RequireConfiguredBearerTokenAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options.RequireBearerToken("test-key"));
        using var client = host.GetTestClient();

        var assertions = await client.GetAsync(LlmTestServerControlRoutes.Assertions);
        var reset = await client.PostAsync(LlmTestServerControlRoutes.Reset, content: null);
        var configure = await client.PostAsJsonAsync(
            LlmTestServerControlRoutes.Configure,
            new { models = Array.Empty<object>(), chatScenarios = Array.Empty<object>() },
            _jsonOptions
        );

        await Assert.That(assertions.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(reset.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(configure.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-key");
        var authorizedAssertions = await client.GetAsync(LlmTestServerControlRoutes.Assertions);

        authorizedAssertions.EnsureSuccessStatusCode();
    }

    [Test]
    public async Task ProviderEndpoints_RejectUnknownModelsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/openai/v1/embeddings",
            new { model = "missing-embedding-model", input = "hello" },
            _jsonOptions
        );

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        await Assert.That(payload.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("llm_test_server_unknown_model");
    }

    [Test]
    public async Task MalformedChatCompletion_ReturnsOpenAiBadRequestAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var client = host.GetTestClient();

        var malformed = await client.PostAsync(
            "/openai/v1/chat/completions",
            new StringContent("{", Encoding.UTF8, "application/json")
        );
        var missingMessages = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new { model = LlmTestServerKnownModelIds.Gpt41Mini, messages = (object?)null },
            _jsonOptions
        );

        await Assert.That(malformed.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(missingMessages.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        var payload = await malformed.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        await Assert.That(payload.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_request");
    }

    [Test]
    public async Task ConfigureEndpoint_AcceptsReadableJsonEnumsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var client = host.GetTestClient();

        var configure = await client.PostAsJsonAsync(
            LlmTestServerControlRoutes.Configure,
            new
            {
                models = new[]
                {
                    new { id = "docs-chat", kind = "chat" },
                    new { id = "docs-embedding", kind = "embedding" },
                    new { id = "docs-image", kind = "image" },
                    new { id = "docs-audio", kind = "audio" },
                },
                chatScenarios = new[]
                {
                    new
                    {
                        id = "docs-blue-whale",
                        modelId = "docs-chat",
                        match = new
                        {
                            mode = "contains",
                            messages = new[] { new { role = "user", content = "largest animal" } },
                        },
                        responses = new[]
                        {
                            new
                            {
                                content = "blue whale",
                                streamChunks = new[] { "blue ", "whale" },
                            },
                        },
                    },
                },
                defaultEmbeddingVector = new[] { 0.125f, 0.25f },
            },
            _jsonOptions
        );

        configure.EnsureSuccessStatusCode();

        var chat = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new
            {
                model = "docs-chat",
                messages = new[] { new { role = "user", content = "What is the largest animal?" } },
            },
            _jsonOptions
        );

        chat.EnsureSuccessStatusCode();
        var payload = await chat.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        await Assert.That(
                payload
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()
            )
            .IsEqualTo("blue whale");
    }

    [Test]
    public async Task ControlClient_ConfiguresReadsAssertionsAndResetsRuntimeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var client = host.GetTestClient();
        var controlClient = new LlmTestServerClient(client);

        await controlClient.ConfigureAsync(
            new LlmTestServerConfigurationBuilder()
                .AddChatScenario(
                    "control-client-blue-whale",
                    scenario => scenario
                        .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                        .WhenUserContains("largest animal")
                        .Responds("blue whale")
                )
                .Build()
        );
        var chat = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new
            {
                model = LlmTestServerKnownModelIds.Gpt41Mini,
                messages = new[] { new { role = "user", content = "largest animal" } },
            },
            _jsonOptions
        );

        chat.EnsureSuccessStatusCode();
        var beforeReset = await controlClient.GetAssertionsAsync();

        await controlClient.ResetAsync();

        var afterReset = await controlClient.GetAssertionsAsync();

        await Assert.That(beforeReset.Matched).IsEqualTo(1);
        await Assert.That(afterReset.TotalEvents).IsEqualTo(0);
    }

    [Test]
    public async Task ProviderEndpoints_ReturnBadRequestForMissingRequiredFieldsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var client = host.GetTestClient();

        var missingEmbeddingModel = await client.PostAsJsonAsync(
            "/openai/v1/embeddings",
            new { model = "", input = "hello" },
            _jsonOptions
        );
        var missingEmbeddingInput = await client.PostAsJsonAsync(
            "/openai/v1/embeddings",
            new { model = LlmTestServerKnownModelIds.TextEmbedding3Small, input = (object?)null },
            _jsonOptions
        );
        var missingImageModel = await client.PostAsJsonAsync(
            "/openai/v1/images/generations",
            new { model = "", prompt = "draw this" },
            _jsonOptions
        );
        var missingImagePrompt = await client.PostAsJsonAsync(
            "/openai/v1/images/generations",
            new { model = LlmTestServerKnownModelIds.GptImage1, prompt = "" },
            _jsonOptions
        );
        var missingAudioModel = await client.PostAsJsonAsync(
            "/openai/v1/audio/speech",
            new { model = "", input = "speak this", voice = "alloy" },
            _jsonOptions
        );
        var missingAudioInput = await client.PostAsJsonAsync(
            "/openai/v1/audio/speech",
            new { model = LlmTestServerKnownModelIds.Gpt4OMiniTts, input = "", voice = "alloy" },
            _jsonOptions
        );
        var missingAudioVoice = await client.PostAsJsonAsync(
            "/openai/v1/audio/speech",
            new { model = LlmTestServerKnownModelIds.Gpt4OMiniTts, input = "speak this", voice = "" },
            _jsonOptions
        );
        var controlModels = await client.GetAsync(LlmTestServerControlRoutes.Models);

        await Assert.That(missingEmbeddingModel.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(missingEmbeddingInput.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(missingImageModel.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(missingImagePrompt.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(missingAudioModel.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(missingAudioInput.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(missingAudioVoice.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        controlModels.EnsureSuccessStatusCode();
    }

    [Test]
    public async Task AudioSpeech_ReturnsTruthfulWavFixtureAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/openai/v1/audio/speech",
            new { model = LlmTestServerKnownModelIds.Gpt4OMiniTts, input = "audio fixture", voice = "alloy" },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();

        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("audio/wav");
        await Assert.That(Encoding.ASCII.GetString(bytes, 0, 4)).IsEqualTo("RIFF");
        await Assert.That(Encoding.ASCII.GetString(bytes, 8, 4)).IsEqualTo("WAVE");
    }

    [Test]
    public async Task ImageRoutes_ReturnDocumentedEditVariationAndStreamingShapesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("gpt-image-1.5", LlmTestServerModelKind.Image)
                .AddModel("dall-e-2", LlmTestServerModelKind.Image));
        using var client = host.GetTestClient();

        var stream = await client.PostAsJsonAsync(
            "/openai/v1/images/generations",
            new
            {
                model = "gpt-image-1.5",
                prompt = "stream a fixture image",
                stream = true,
                partial_images = 1,
                size = "1024x1024",
                quality = "high",
                background = "auto",
                output_format = "png",
            },
            _jsonOptions
        );
        using var editContent = CreateImageEditContent(
            "gpt-image-1.5",
            "edit a fixture image"
        );
        var edit = await client.PostAsync("/openai/v1/images/edits", editContent);
        using var editStreamContent = CreateImageEditContent(
            "gpt-image-1.5",
            "stream an edited fixture image",
            stream: true
        );
        var editStream = await client.PostAsync("/openai/v1/images/edits", editStreamContent);
        using var variationContent = CreateImageVariationContent("dall-e-2");
        var variation = await client.PostAsync("/openai/v1/images/variations", variationContent);

        var invalidGeneration = await client.PostAsJsonAsync(
            "/openai/v1/images/generations",
            new
            {
                model = "gpt-image-1.5",
                prompt = "invalid image",
                stream = true,
                partial_images = 4,
            },
            _jsonOptions
        );
        var invalidEdit = await client.PostAsJsonAsync(
            "/openai/v1/images/edits",
            new { model = "gpt-image-1.5", prompt = "missing image" },
            _jsonOptions
        );
        using var invalidVariationContent = new MultipartFormDataContent
        {
            { new StringContent("dall-e-2"), "model" },
        };
        var invalidVariation = await client.PostAsync(
            "/openai/v1/images/variations",
            invalidVariationContent
        );

        stream.EnsureSuccessStatusCode();
        edit.EnsureSuccessStatusCode();
        editStream.EnsureSuccessStatusCode();
        variation.EnsureSuccessStatusCode();

        var streamBody = await stream.Content.ReadAsStringAsync();
        var editPayload = await edit.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var editStreamBody = await editStream.Content.ReadAsStringAsync();
        var variationPayload = await variation.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(stream.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("text/event-stream");
        await Assert.That(streamBody).Contains("\"type\":\"image_generation.partial_image\"");
        await Assert.That(streamBody).Contains("\"type\":\"image_generation.completed\"");
        await Assert.That(editPayload.GetProperty("data")[0].GetProperty("b64_json").GetString())
            .IsNotNull()
            .And
            .IsNotEmpty();
        await Assert.That(editStream.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("text/event-stream");
        await Assert.That(editStreamBody).Contains("\"type\":\"image_edit.partial_image\"");
        await Assert.That(editStreamBody).Contains("\"type\":\"image_edit.completed\"");
        await Assert.That(variationPayload.GetProperty("data")[0].GetProperty("b64_json").GetString())
            .IsNotNull()
            .And
            .IsNotEmpty();
        await Assert.That(invalidGeneration.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(invalidEdit.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(invalidVariation.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AudioTranscription_ReturnsJsonTextAndStreamingShapesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .WithDefaultTranscriptionText("deterministic transcript"));
        using var client = host.GetTestClient();

        using var jsonContent = CreateTranscriptionContent(LlmTestServerKnownModelIds.Gpt4OMiniTts);
        var json = await client.PostAsync("/openai/v1/audio/transcriptions", jsonContent);
        using var textContent = CreateTranscriptionContent(
            LlmTestServerKnownModelIds.Gpt4OMiniTts,
            responseFormat: "text"
        );
        var text = await client.PostAsync("/openai/v1/audio/transcriptions", textContent);
        using var streamContent = CreateTranscriptionContent(
            LlmTestServerKnownModelIds.Gpt4OMiniTts,
            stream: true
        );
        var stream = await client.PostAsync("/openai/v1/audio/transcriptions", streamContent);

        json.EnsureSuccessStatusCode();
        text.EnsureSuccessStatusCode();
        stream.EnsureSuccessStatusCode();

        var jsonPayload = await json.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var textPayload = await text.Content.ReadAsStringAsync();
        var streamPayload = await stream.Content.ReadAsStringAsync();

        await Assert.That(jsonPayload.GetProperty("text").GetString())
            .IsEqualTo("deterministic transcript");
        await Assert.That(text.Content.Headers.ContentType?.MediaType).IsEqualTo("text/plain");
        await Assert.That(textPayload).IsEqualTo("deterministic transcript");
        await Assert.That(stream.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("text/event-stream");
        await Assert.That(streamPayload).Contains("\"type\":\"transcript.text.delta\"");
        await Assert.That(streamPayload).Contains("\"delta\":\"deterministic transcript\"");
        await Assert.That(streamPayload).Contains("\"type\":\"transcript.text.done\"");
        await Assert.That(streamPayload).Contains("\"text\":\"deterministic transcript\"");
    }

    [Test]
    public async Task AudioTranslation_ReturnsJsonTextAndRejectsStreamingAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .WithDefaultTranslationText("deterministic translation"));
        using var client = host.GetTestClient();

        using var jsonContent = CreateTranscriptionContent(LlmTestServerKnownModelIds.Gpt4OMiniTts);
        var json = await client.PostAsync("/openai/v1/audio/translations", jsonContent);
        using var textContent = CreateTranscriptionContent(
            LlmTestServerKnownModelIds.Gpt4OMiniTts,
            responseFormat: "text"
        );
        var text = await client.PostAsync("/openai/v1/audio/translations", textContent);
        using var streamContent = CreateTranscriptionContent(
            LlmTestServerKnownModelIds.Gpt4OMiniTts,
            stream: true
        );
        var stream = await client.PostAsync("/openai/v1/audio/translations", streamContent);
        using var diarizedContent = CreateTranscriptionContent(
            LlmTestServerKnownModelIds.Gpt4OMiniTts,
            responseFormat: "diarized_json"
        );
        var diarized = await client.PostAsync("/openai/v1/audio/translations", diarizedContent);

        json.EnsureSuccessStatusCode();
        text.EnsureSuccessStatusCode();

        var jsonPayload = await json.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var textPayload = await text.Content.ReadAsStringAsync();
        var streamPayload = await stream.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var diarizedPayload = await diarized.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(jsonPayload.GetProperty("text").GetString())
            .IsEqualTo("deterministic translation");
        await Assert.That(text.Content.Headers.ContentType?.MediaType).IsEqualTo("text/plain");
        await Assert.That(textPayload).IsEqualTo("deterministic translation");
        await Assert.That(stream.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(streamPayload.GetProperty("error").GetProperty("message").GetString())
            .Contains("Streaming translation");
        await Assert.That(diarized.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(diarizedPayload.GetProperty("error").GetProperty("message").GetString())
            .Contains("Unsupported translation response_format");
    }

    [Test]
    public async Task VideoRoutes_ReturnDocumentedOpenAiShapesAndValidateEnumsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel(LlmTestServerKnownModelIds.Sora2, LlmTestServerModelKind.Video));
        using var client = host.GetTestClient();

        using var createContent = CreateVideoContent(
            LlmTestServerKnownModelIds.Sora2,
            "generate a deterministic clip",
            seconds: "8",
            size: "1280x720"
        );
        var create = await client.PostAsync("/openai/v1/videos", createContent);
        create.EnsureSuccessStatusCode();
        var createPayload = await create.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var createdId = createPayload.GetProperty("id").GetString();
        var list = await client.GetAsync("/openai/v1/videos?limit=1&order=desc");
        var retrieve = await client.GetAsync($"/openai/v1/videos/{createdId}");
        var content = await client.GetAsync($"/openai/v1/videos/{createdId}/content");
        var thumbnail = await client.GetAsync($"/openai/v1/videos/{createdId}/content?variant=thumbnail");
        var edit = await client.PostAsJsonAsync(
            "/openai/v1/videos/edits",
            new { prompt = "edit the clip", video = new { id = createdId } },
            _jsonOptions
        );
        var extension = await client.PostAsJsonAsync(
            "/openai/v1/videos/extensions",
            new { prompt = "extend the clip", seconds = "12", video = new { id = createdId } },
            _jsonOptions
        );
        var remix = await client.PostAsJsonAsync(
             $"/openai/v1/videos/{createdId}/remix",
            new { prompt = "remix the clip" },
            _jsonOptions
        );
        using var characterContent = CreateVideoCharacterContent();
        var character = await client.PostAsync("/openai/v1/videos/characters", characterContent);
        var getCharacter = await client.GetAsync("/openai/v1/videos/characters/char_custom");
        var delete = await client.DeleteAsync($"/openai/v1/videos/{createdId}");

        using var invalidSecondsContent = CreateVideoContent(
            LlmTestServerKnownModelIds.Sora2,
            "generate a deterministic clip",
            seconds: "16"
        );
        var invalidSeconds = await client.PostAsync("/openai/v1/videos", invalidSecondsContent);
        using var invalidSizeContent = CreateVideoContent(
            LlmTestServerKnownModelIds.Sora2,
            "generate a deterministic clip",
            size: "640x480"
        );
        var invalidSize = await client.PostAsync("/openai/v1/videos", invalidSizeContent);
        var invalidList = await client.GetAsync("/openai/v1/videos?limit=101");

        create.EnsureSuccessStatusCode();
        list.EnsureSuccessStatusCode();
        retrieve.EnsureSuccessStatusCode();
        content.EnsureSuccessStatusCode();
        thumbnail.EnsureSuccessStatusCode();
        edit.EnsureSuccessStatusCode();
        extension.EnsureSuccessStatusCode();
        remix.EnsureSuccessStatusCode();
        character.EnsureSuccessStatusCode();
        getCharacter.EnsureSuccessStatusCode();
        delete.EnsureSuccessStatusCode();

        var listPayload = await list.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var retrievePayload = await retrieve.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var contentBytes = await content.Content.ReadAsByteArrayAsync();
        var editPayload = await edit.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var extensionPayload = await extension.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var remixPayload = await remix.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var characterPayload = await character.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var getCharacterPayload = await getCharacter.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var deletePayload = await delete.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(createPayload.GetProperty("object").GetString()).IsEqualTo("video");
        await Assert.That(createPayload.GetProperty("model").GetString()).IsEqualTo(LlmTestServerKnownModelIds.Sora2);
        await Assert.That(createPayload.GetProperty("seconds").GetString()).IsEqualTo("8");
        await Assert.That(createPayload.GetProperty("size").GetString()).IsEqualTo("1280x720");
        await Assert.That(listPayload.GetProperty("object").GetString()).IsEqualTo("list");
        await Assert.That(listPayload.GetProperty("data").GetArrayLength()).IsEqualTo(1);
        await Assert.That(retrievePayload.GetProperty("id").GetString()).IsEqualTo(createdId);
        await Assert.That(content.Content.Headers.ContentType?.MediaType).IsEqualTo("video/mp4");
        await Assert.That(Encoding.ASCII.GetString(contentBytes, 4, 4)).IsEqualTo("ftyp");
        await Assert.That(thumbnail.Content.Headers.ContentType?.MediaType).IsEqualTo("image/jpeg");
        await Assert.That(editPayload.GetProperty("remixed_from_video_id").GetString())
            .IsEqualTo(createdId);
        await Assert.That(extensionPayload.GetProperty("seconds").GetString()).IsEqualTo("12");
        await Assert.That(remixPayload.GetProperty("remixed_from_video_id").GetString())
            .IsEqualTo(createdId);
        await Assert.That(characterPayload.GetProperty("name").GetString()).IsEqualTo("Fixture Character");
        await Assert.That(getCharacterPayload.GetProperty("id").GetString()).IsEqualTo("char_custom");
        await Assert.That(deletePayload.GetProperty("object").GetString()).IsEqualTo("video.deleted");
        await Assert.That(deletePayload.GetProperty("deleted").GetBoolean()).IsTrue();
        await Assert.That(invalidSeconds.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(invalidSize.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(invalidList.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    private static MultipartFormDataContent CreateTranscriptionContent(
        string model,
        string responseFormat = "json",
        bool stream = false
    )
    {
        var content = new MultipartFormDataContent();
        var audio = new ByteArrayContent(Encoding.ASCII.GetBytes("RIFF....WAVE"));
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audio, "file", "fixture.wav");
        content.Add(new StringContent(model), "model");
        content.Add(new StringContent(responseFormat), "response_format");
        if (stream)
        {
            content.Add(new StringContent("true"), "stream");
        }

        return content;
    }

    private static MultipartFormDataContent CreateVideoContent(
        string model,
        string prompt,
        string? seconds = null,
        string? size = null
    )
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(model), "model" },
            { new StringContent(prompt), "prompt" },
        };
        if (!string.IsNullOrWhiteSpace(seconds))
        {
            content.Add(new StringContent(seconds), "seconds");
        }

        if (!string.IsNullOrWhiteSpace(size))
        {
            content.Add(new StringContent(size), "size");
        }

        return content;
    }

    private static string CreatePromptWithAtLeastTokens(int minimumTokens)
    {
        var builder = new StringBuilder("cacheable fixture");
        while (LlmTestServerTokenCounter.CountTextTokens(builder.ToString()) < minimumTokens)
        {
            builder.Append(" stable-prefix");
        }

        return builder.ToString();
    }

    private static MultipartFormDataContent CreateVideoCharacterContent()
    {
        var content = new MultipartFormDataContent();
        var video = new ByteArrayContent([0, 0, 0, 24, 102, 116, 121, 112]);
        video.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        content.Add(new StringContent("Fixture Character"), "name");
        content.Add(video, "video", "fixture.mp4");
        return content;
    }

    private static MultipartFormDataContent CreateImageEditContent(
        string model,
        string prompt,
        bool stream = false
    )
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(model), "model" },
            { new StringContent(prompt), "prompt" },
            { new StringContent("1024x1024"), "size" },
        };
        if (stream)
        {
            content.Add(new StringContent("true"), "stream");
        }

        var image = new ByteArrayContent([137, 80, 78, 71]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(image, "image[]", "fixture.png");
        return content;
    }

    private static MultipartFormDataContent CreateImageVariationContent(string model)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(model), "model" },
            { new StringContent("1"), "n" },
            { new StringContent("b64_json"), "response_format" },
            { new StringContent("512x512"), "size" },
        };
        var image = new ByteArrayContent([137, 80, 78, 71]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(image, "image", "fixture.png");
        return content;
    }
}
