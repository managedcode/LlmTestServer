using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Providers;

public sealed class OpenAiCompatibleProviderRouteTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments("/openai/v1/chat/completions")]
    [Arguments("/groq/openai/v1/chat/completions")]
    [Arguments("/openrouter/api/v1/chat/completions")]
    [Arguments("/mistral/v1/chat/completions")]
    [Arguments("/deepseek/v1/chat/completions")]
    [Arguments("/perplexity/v1/sonar")]
    [Arguments("/microsoft-foundry/chat/completions?api-version=2024-05-01-preview")]
    [Arguments("/microsoft-foundry/models/chat/completions?api-version=2024-05-01-preview")]
    public async Task OpenAiCompatibleChatRoutes_ReturnChatCompletionShapeAsync(string path)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("compat-chat", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "compat-route-blue-whale",
                    scenario => scenario
                        .ForModel("compat-chat")
                        .WhenUserContains("compat route")
                        .Responds("blue whale", "blue ", "whale")
                ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            path,
            new
            {
                model = "compat-chat",
                messages = new[] { new { role = "user", content = "compat route" } },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(
                payload
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()
            )
            .IsEqualTo("blue whale");
        var usage = payload.GetProperty("usage");
        var promptTokens = usage.GetProperty("prompt_tokens").GetInt32();

        await Assert.That(promptTokens).IsGreaterThan(0);
        await Assert.That(usage.GetProperty("completion_tokens").GetInt32()).IsEqualTo(2);
        await Assert.That(usage.GetProperty("total_tokens").GetInt32())
            .IsEqualTo(promptTokens + 2);
    }

    [Test]
    [Arguments("/groq/openai/v1/chat/completions", false, false)]
    [Arguments("/openrouter/api/v1/chat/completions", true, false)]
    [Arguments("/mistral/v1/chat/completions", false, false)]
    [Arguments("/deepseek/v1/chat/completions", false, true)]
    [Arguments("/microsoft-foundry/chat/completions?api-version=2024-05-01-preview", false, false)]
    [Arguments("/microsoft-foundry/models/chat/completions?api-version=2024-05-01-preview", false, false)]
    public async Task OpenAiCompatibleChatRoutes_WithProviderPromptCaching_ReportCacheUsageAsync(
        string path,
        bool includesCacheWriteTokens,
        bool usesDeepSeekUsageFields
    )
    {
        var cacheablePrompt = CreatePromptWithAtLeastTokens(1100);
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("provider-cache-chat", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "provider-cache",
                    scenario => scenario
                        .ForModel("provider-cache-chat")
                        .WhenUserContains("provider cache turn")
                        .Responds("first")
                        .Responds("second")
                ));
        using var client = host.GetTestClient();

        var first = await PostProviderCacheRequestAsync(
            client,
            path,
            cacheablePrompt,
            "provider cache turn one"
        );
        var second = await PostProviderCacheRequestAsync(
            client,
            path,
            cacheablePrompt,
            "provider cache turn two"
        );

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        var firstUsage = (await first.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions))
            .GetProperty("usage");
        var secondUsage = (await second.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions))
            .GetProperty("usage");

        if (usesDeepSeekUsageFields)
        {
            await Assert.That(firstUsage.GetProperty("prompt_cache_hit_tokens").GetInt32())
                .IsEqualTo(0);
            await Assert.That(secondUsage.GetProperty("prompt_cache_hit_tokens").GetInt32())
                .IsGreaterThanOrEqualTo(1024);
            await Assert.That(secondUsage.GetProperty("prompt_cache_miss_tokens").GetInt32())
                .IsGreaterThan(0);
            await Assert.That(secondUsage.TryGetProperty("prompt_tokens_details", out _))
                .IsFalse();
            return;
        }

        var firstDetails = firstUsage.GetProperty("prompt_tokens_details");
        var secondDetails = secondUsage.GetProperty("prompt_tokens_details");
        await Assert.That(firstDetails.GetProperty("cached_tokens").GetInt32()).IsEqualTo(0);
        await Assert.That(secondDetails.GetProperty("cached_tokens").GetInt32())
            .IsGreaterThanOrEqualTo(1024);

        if (includesCacheWriteTokens)
        {
            await Assert.That(firstDetails.GetProperty("cache_write_tokens").GetInt32())
                .IsGreaterThanOrEqualTo(1024);
            await Assert.That(secondDetails.GetProperty("cache_write_tokens").GetInt32())
                .IsEqualTo(0);
        }
    }

    [Test]
    public async Task OpenAiCompatibleChatRoutes_WithoutProviderPromptCaching_DoNotReportCacheUsageAsync()
    {
        var cacheablePrompt = CreatePromptWithAtLeastTokens(1100);
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("perplexity-cache-chat", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "perplexity-cache",
                    scenario => scenario
                        .ForModel("perplexity-cache-chat")
                        .WhenUserContains("perplexity cache turn")
                        .Responds("first")
                        .Responds("second")
                ));
        using var client = host.GetTestClient();

        var first = await PostProviderCacheRequestAsync(
            client,
            "/perplexity/v1/sonar",
            cacheablePrompt,
            "perplexity cache turn one",
            "perplexity-cache-chat"
        );
        var second = await PostProviderCacheRequestAsync(
            client,
            "/perplexity/v1/sonar",
            cacheablePrompt,
            "perplexity cache turn two",
            "perplexity-cache-chat"
        );

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        var usage = (await second.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions))
            .GetProperty("usage");

        await Assert.That(usage.TryGetProperty("prompt_tokens_details", out _)).IsFalse();
        await Assert.That(usage.TryGetProperty("prompt_cache_hit_tokens", out _)).IsFalse();
        await Assert.That(usage.TryGetProperty("prompt_cache_miss_tokens", out _)).IsFalse();
    }

    [Test]
    [Arguments("/openai/v1/chat/completions")]
    [Arguments("/groq/openai/v1/chat/completions")]
    [Arguments("/openrouter/api/v1/chat/completions")]
    [Arguments("/mistral/v1/chat/completions")]
    [Arguments("/deepseek/v1/chat/completions")]
    [Arguments("/perplexity/v1/sonar")]
    [Arguments("/microsoft-foundry/chat/completions?api-version=2024-05-01-preview")]
    [Arguments("/microsoft-foundry/models/chat/completions?api-version=2024-05-01-preview")]
    public async Task OpenAiCompatibleChatRoutes_StreamServerSentChunksAsync(string path)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("compat-stream-chat", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "compat-route-stream",
                    scenario => scenario
                        .ForModel("compat-stream-chat")
                        .WhenUserContains("stream compat route")
                        .Responds("blue whale", "blue ", "whale")
                ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            path,
            new
            {
                model = "compat-stream-chat",
                stream = true,
                messages = new[] { new { role = "user", content = "stream compat route" } },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("text/event-stream");
        await Assert.That(body).Contains("data: ");
        await Assert.That(body).Contains("blue ");
        await Assert.That(body).Contains("whale");
        await Assert.That(body).Contains("[DONE]");
    }

    [Test]
    [Arguments("/openai/v1/models")]
    [Arguments("/groq/openai/v1/models")]
    [Arguments("/openrouter/api/v1/models")]
    [Arguments("/deepseek/models")]
    public async Task OpenAiCompatibleModelRoutes_ReturnModelListShapeAsync(string path)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options =>
            options.AddModel("compat-chat", LlmTestServerModelKind.Chat)
        );
        using var client = host.GetTestClient();

        var payload = await client.GetFromJsonAsync<JsonElement>(path, _jsonOptions);

        await Assert.That(payload.GetProperty("object").GetString()).IsEqualTo("list");
        await Assert.That(payload.GetProperty("data").GetArrayLength()).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    [Arguments("GET", "/models")]
    [Arguments("GET", "/v1/models")]
    [Arguments("POST", "/v1/chat/completions")]
    [Arguments("POST", "/v1/messages")]
    [Arguments("POST", "/api/chat")]
    [Arguments("POST", "/api/embed")]
    [Arguments("POST", "/api/v1/chat/completions")]
    [Arguments("POST", "/v2/chat")]
    [Arguments("POST", "/v2/embed")]
    [Arguments("POST", "/model/compat-chat/converse")]
    [Arguments("POST", "/model/compat-chat/invoke")]
    [Arguments("POST", "/openai/deployments/compat-chat/chat/completions")]
    public async Task GenericRootProviderRoutes_AreNotMappedAsync(string method, string path)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options =>
            options.AddModel("compat-chat", LlmTestServerModelKind.Chat)
        );
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    [Arguments("/openai/v1/responses")]
    [Arguments("/groq/openai/v1/responses")]
    [Arguments("/openrouter/api/v1/responses")]
    public async Task OpenAiCompatibleResponsesRoutes_ReturnResponseShapeAsync(string path)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("compat-responses", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "compat-responses",
                    scenario => scenario
                        .ForModel("compat-responses")
                        .WhenUserContains("responses route")
                        .Responds("response text", "response ", "text")
                ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            path,
            new
            {
                model = "compat-responses",
                input = new object[]
                {
                    new
                    {
                        type = "message",
                        role = "user",
                        content = new object[]
                        {
                            new { type = "input_text", text = "responses route" },
                            new
                            {
                                type = "input_image",
                                image_url = "data:image/png;base64,AA==",
                            },
                        },
                    },
                },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var output = payload.GetProperty("output")[0];

        await Assert.That(payload.GetProperty("object").GetString()).IsEqualTo("response");
        await Assert.That(payload.GetProperty("status").GetString()).IsEqualTo("completed");
        await Assert.That(output.GetProperty("type").GetString()).IsEqualTo("message");
        await Assert.That(output.GetProperty("status").GetString()).IsEqualTo("completed");
        await Assert.That(output.GetProperty("content")[0].GetProperty("type").GetString())
            .IsEqualTo("output_text");
        await Assert.That(output.GetProperty("content")[0].GetProperty("text").GetString())
            .IsEqualTo("response text");
        var usage = payload.GetProperty("usage");
        var inputTokens = usage.GetProperty("input_tokens").GetInt32();

        await Assert.That(inputTokens).IsGreaterThan(0);
        await Assert.That(payload.GetProperty("usage").GetProperty("output_tokens").GetInt32())
            .IsEqualTo(2);
        await Assert.That(payload.GetProperty("usage").GetProperty("total_tokens").GetInt32())
            .IsEqualTo(inputTokens + 2);
    }

    [Test]
    [Arguments("/openai/v1/responses")]
    [Arguments("/groq/openai/v1/responses")]
    [Arguments("/openrouter/api/v1/responses")]
    public async Task OpenAiCompatibleResponsesRoutes_StreamResponseServerSentEventsAsync(string path)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("compat-responses", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "compat-responses-stream",
                    scenario => scenario
                        .ForModel("compat-responses")
                        .WhenUserContains("stream responses")
                        .Responds("response text", "response ", "text")
                ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            path,
            new
            {
                model = "compat-responses",
                input = "stream responses",
                stream = true,
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("text/event-stream");
        await Assert.That(body).Contains("\"type\":\"response.created\"");
        await Assert.That(body).Contains("\"type\":\"response.output_item.added\"");
        await Assert.That(body).Contains("\"type\":\"response.content_part.added\"");
        await Assert.That(body).Contains("\"type\":\"response.output_text.delta\"");
        await Assert.That(body).Contains("\"delta\":\"response \"");
        await Assert.That(body).Contains("\"delta\":\"text\"");
        await Assert.That(body).Contains("\"type\":\"response.output_item.done\"");
        await Assert.That(body).Contains("\"type\":\"response.completed\"");
        await Assert.That(body).Contains("\"input_tokens\":");
        await Assert.That(body).Contains("\"output_tokens\":2");
    }

    [Test]
    [Arguments("/microsoft-foundry/embeddings?api-version=2024-05-01-preview")]
    [Arguments("/microsoft-foundry/models/embeddings?api-version=2024-05-01-preview")]
    public async Task FoundryEmbeddingRoutes_ReturnEmbeddingShapeAsync(string path)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("foundry-embedding", LlmTestServerModelKind.Embedding)
                .WithDefaultEmbeddingVector(0.25f, 0.5f));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            path,
            new { model = "foundry-embedding", input = new[] { "alpha", "beta" } },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("data").GetArrayLength()).IsEqualTo(2);
        await Assert.That(payload.GetProperty("data")[0].GetProperty("embedding").GetArrayLength())
            .IsEqualTo(2);
    }

    [Test]
    public async Task MistralEmbeddingRoute_ReturnsEmbeddingShapeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("mistral-embedding", LlmTestServerModelKind.Embedding)
                .WithDefaultEmbeddingVector(0.25f, 0.5f));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/mistral/v1/embeddings",
            new { model = "mistral-embedding", input = new[] { "alpha", "beta" } },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("data").GetArrayLength()).IsEqualTo(2);
        await Assert.That(payload.GetProperty("data")[0].GetProperty("embedding").GetArrayLength())
            .IsEqualTo(2);
    }

    [Test]
    public async Task GroqAudioSpeechRoute_ReturnsAudioAndValidatesDocumentedRequestAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("playai-tts", LlmTestServerModelKind.Audio));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/groq/openai/v1/audio/speech",
            new
            {
                model = "playai-tts",
                input = "fixture audio",
                voice = "Fritz-PlayAI",
                response_format = "wav",
            },
            _jsonOptions
        );
        var missingVoice = await client.PostAsJsonAsync(
            "/groq/openai/v1/audio/speech",
            new { model = "playai-tts", input = "fixture audio", response_format = "wav" },
            _jsonOptions
        );
        var unsupportedFormat = await client.PostAsJsonAsync(
            "/groq/openai/v1/audio/speech",
            new
            {
                model = "playai-tts",
                input = "fixture audio",
                voice = "Fritz-PlayAI",
                response_format = "opus",
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var missingVoicePayload = await missingVoice.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var unsupportedFormatPayload = await unsupportedFormat
            .Content
            .ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("audio/wav");
        await Assert.That(Encoding.ASCII.GetString(bytes, 0, 4)).IsEqualTo("RIFF");
        await Assert.That(missingVoice.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(missingVoicePayload.GetProperty("error").GetProperty("message").GetString())
            .Contains("Missing audio voice");
        await Assert.That(unsupportedFormat.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(unsupportedFormatPayload.GetProperty("error").GetProperty("message").GetString())
            .Contains("Unsupported audio response_format");
    }

    [Test]
    public async Task GroqAudioTranscriptionRoute_ReturnsGroqTranscriptionShapeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("whisper-large-v3", LlmTestServerModelKind.Audio)
                .WithDefaultTranscriptionText("groq transcript"));
        using var client = host.GetTestClient();
        using var content = CreateTranscriptionContent("whisper-large-v3");

        var response = await client.PostAsync("/groq/openai/v1/audio/transcriptions", content);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("text").GetString()).IsEqualTo("groq transcript");
        await Assert.That(payload.GetProperty("x_groq").GetProperty("id").GetString())
            .StartsWith("req_");
    }

    [Test]
    public async Task GroqAudioTranslationRoute_ReturnsGroqTranslationShapeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("whisper-large-v3", LlmTestServerModelKind.Audio)
                .WithDefaultTranslationText("groq translation"));
        using var client = host.GetTestClient();
        using var content = CreateTranscriptionContent("whisper-large-v3");

        var response = await client.PostAsync("/groq/openai/v1/audio/translations", content);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("text").GetString()).IsEqualTo("groq translation");
        await Assert.That(payload.GetProperty("x_groq").GetProperty("id").GetString())
            .StartsWith("req_");
    }

    [Test]
    public async Task AzureOpenAiAudioTranscriptionRoute_UsesDeploymentModelAndApiKeyAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .RequireBearerToken("azure-key")
                .AddModel("whisper-deployment", LlmTestServerModelKind.Audio)
                .WithDefaultTranscriptionText("azure transcript"));
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("api-key", "azure-key");
        using var content = CreateTranscriptionContent(
            model: "ignored-body-model",
            responseFormat: "verbose_json"
        );

        var response = await client.PostAsync(
            "/azure-openai/openai/deployments/whisper-deployment/audio/transcriptions?api-version=2024-10-21",
            content
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("text").GetString()).IsEqualTo("azure transcript");
        await Assert.That(payload.GetProperty("language").GetString()).IsEqualTo("en");
        await Assert.That(payload.GetProperty("segments").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task AzureOpenAiAudioTranslationRoute_UsesDeploymentModelAndApiKeyAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .RequireBearerToken("azure-key")
                .AddModel("whisper-deployment", LlmTestServerModelKind.Audio)
                .WithDefaultTranslationText("azure translation"));
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("api-key", "azure-key");
        using var content = CreateTranscriptionContent(
            model: "ignored-body-model",
            responseFormat: "verbose_json"
        );

        var response = await client.PostAsync(
            "/azure-openai/openai/deployments/whisper-deployment/audio/translations?api-version=2024-10-21",
            content
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("text").GetString()).IsEqualTo("azure translation");
        await Assert.That(payload.GetProperty("language").GetString()).IsEqualTo("en");
        await Assert.That(payload.GetProperty("segments").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task AzureOpenAiVideoRoutes_FollowPreviewJobAndContentShapesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .RequireBearerToken("azure-key")
                .AddModel("sora-deployment", LlmTestServerModelKind.Video));
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("api-key", "azure-key");

        var create = await client.PostAsJsonAsync(
            "/azure-openai/openai/v1/video/generations/jobs?api-version=preview",
            new
            {
                model = "sora-deployment",
                prompt = "azure preview video",
                width = 1280,
                height = 720,
                n_seconds = 5,
                n_variants = 1,
            },
            _jsonOptions
        );

        create.EnsureSuccessStatusCode();
        var createPayload = await create.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var jobId = createPayload.GetProperty("id").GetString();
        var generationId = createPayload.GetProperty("generations")[0].GetProperty("id").GetString();

        var list = await client.GetAsync(
            "/azure-openai/openai/v1/video/generations/jobs?api-version=preview&limit=1"
        );
        var job = await client.GetAsync(
            $"/azure-openai/openai/v1/video/generations/jobs/{jobId}?api-version=preview"
        );
        var generation = await client.GetAsync(
            $"/azure-openai/openai/v1/video/generations/{generationId}?api-version=preview"
        );
        var thumbnail = await client.GetAsync(
            $"/azure-openai/openai/v1/video/generations/{generationId}/content/thumbnail?api-version=preview"
        );
        var video = await client.GetAsync(
            $"/azure-openai/openai/v1/video/generations/{generationId}/content/video?api-version=preview"
        );
        using var headRequest = new HttpRequestMessage(
            HttpMethod.Head,
            $"/azure-openai/openai/v1/video/generations/{generationId}/content/video?api-version=preview"
        );
        var head = await client.SendAsync(headRequest);
        var delete = await client.DeleteAsync(
            $"/azure-openai/openai/v1/video/generations/jobs/{jobId}?api-version=preview"
        );
        var invalid = await client.PostAsJsonAsync(
            "/azure-openai/openai/v1/video/generations/jobs?api-version=preview",
            new
            {
                model = "sora-deployment",
                prompt = "azure preview video",
                width = 1280,
                height = 720,
                n_seconds = 21,
                n_variants = 1,
            },
            _jsonOptions
        );

        list.EnsureSuccessStatusCode();
        job.EnsureSuccessStatusCode();
        generation.EnsureSuccessStatusCode();
        thumbnail.EnsureSuccessStatusCode();
        video.EnsureSuccessStatusCode();
        head.EnsureSuccessStatusCode();

        var listPayload = await list.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var jobPayload = await job.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var generationPayload = await generation.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var videoBytes = await video.Content.ReadAsByteArrayAsync();

        await Assert.That(createPayload.GetProperty("object").GetString())
            .IsEqualTo("video.generation.job");
        await Assert.That(createPayload.GetProperty("model").GetString()).IsEqualTo("sora-deployment");
        await Assert.That(createPayload.GetProperty("n_seconds").GetInt32()).IsEqualTo(5);
        await Assert.That(createPayload.GetProperty("width").GetInt32()).IsEqualTo(1280);
        await Assert.That(createPayload.GetProperty("height").GetInt32()).IsEqualTo(720);
        await Assert.That(listPayload.GetProperty("object").GetString()).IsEqualTo("list");
        await Assert.That(listPayload.GetProperty("data").GetArrayLength()).IsEqualTo(1);
        await Assert.That(jobPayload.GetProperty("id").GetString()).IsEqualTo(jobId);
        await Assert.That(generationPayload.GetProperty("id").GetString()).IsEqualTo(generationId);
        await Assert.That(generationPayload.GetProperty("job_id").GetString()).IsEqualTo("video_llm_tck");
        await Assert.That(thumbnail.Content.Headers.ContentType?.MediaType).IsEqualTo("image/jpg");
        await Assert.That(video.Content.Headers.ContentType?.MediaType).IsEqualTo("video/mp4");
        await Assert.That(Encoding.ASCII.GetString(videoBytes, 4, 4)).IsEqualTo("ftyp");
        await Assert.That(head.Content.Headers.ContentType?.MediaType).IsEqualTo("video/mp4");
        await Assert.That(delete.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    [Arguments("/groq/openai/v1/audio/transcriptions", "transcription")]
    [Arguments("/groq/openai/v1/audio/translations", "translation")]
    public async Task GroqAudioRoutes_RejectOpenAiOnlyResponseFormatsAsync(
        string path,
        string operationName
    )
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("whisper-large-v3", LlmTestServerModelKind.Audio));
        using var client = host.GetTestClient();
        using var content = CreateTranscriptionContent("whisper-large-v3", responseFormat: "vtt");

        var response = await client.PostAsync(path, content);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        await Assert.That(payload.GetProperty("error").GetProperty("message").GetString())
            .Contains($"Unsupported {operationName} response_format");
    }

    private static MultipartFormDataContent CreateTranscriptionContent(
        string model,
        string responseFormat = "json"
    )
    {
        var content = new MultipartFormDataContent();
        var audio = new ByteArrayContent(Encoding.ASCII.GetBytes("RIFF....WAVE"));
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audio, "file", "fixture.wav");
        content.Add(new StringContent(model), "model");
        content.Add(new StringContent(responseFormat), "response_format");
        return content;
    }

    private static Task<HttpResponseMessage> PostProviderCacheRequestAsync(
        HttpClient client,
        string path,
        string systemPrompt,
        string userPrompt,
        string model = "provider-cache-chat"
    )
    {
        return client.PostAsJsonAsync(
            path,
            new
            {
                model,
                prompt_cache_key = "provider-cache-session",
                session_id = "provider-cache-session",
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt },
                },
            },
            _jsonOptions
        );
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
}
