using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Bedrock;

public sealed class BedrockEndpointTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments("converse-stream")]
    [Arguments("invoke-with-response-stream")]
    public async Task StreamingEndpoints_RejectInvalidRequestsWithoutConsumingFixtureAsync(string operation)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
            .RequireBearerToken("test-key")
            .AddModel("amazon.nova-lite-v1:0", LlmTestServerModelKind.Chat)
            .AddChatScenario("stream-validation", scenario => scenario.ForModel("amazon.nova-lite-v1:0")
                .WhenUserContains("whale").Responds("blue whale")));
        using var client = host.GetTestClient();
        var path = $"/bedrock/model/amazon.nova-lite-v1:0/{operation}";
        foreach (var invalidBody in new[] { "{", "null", """{"inputText":""}""" })
        {
            using var body = new StringContent(invalidBody, Encoding.UTF8, "application/json");
            using var invalid = await client.PostAsync(path, body);
            await Assert.That(invalid.StatusCode).IsEqualTo(System.Net.HttpStatusCode.BadRequest);
            await Assert.That(invalid.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
            var error = await invalid.Content.ReadFromJsonAsync<JsonElement>();
            await Assert.That(error.GetProperty("message").GetString()).IsNotNullOrEmpty();
        }

        var validBody = operation == "converse-stream"
            ? """{"messages":[{"role":"user","content":[{"text":"whale"}]}]}"""
            : """{"inputText":"whale"}""";
        using var unauthorizedBody = new StringContent(validBody, Encoding.UTF8, "application/json");
        using var unauthorized = await client.PostAsync(path, unauthorizedBody);
        await Assert.That(unauthorized.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
        var unauthorizedError = await unauthorized.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(unauthorizedError.GetProperty("message").GetString()).IsNotNullOrEmpty();

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-key");
        using var validContent = new StringContent(validBody, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(path, validContent);
        response.EnsureSuccessStatusCode();
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/vnd.amazon.eventstream");
        var events = await ReadEventStreamAsync(response);
        if (operation == "converse-stream")
        {
            var delta = events.Single(item => item.TryGetProperty("contentBlockDelta", out _));
            await Assert.That(delta.GetProperty("contentBlockDelta").GetProperty("delta").GetProperty("text").GetString())
                .IsEqualTo("blue whale");
        }
        else
        {
            var bytes = events[0].GetProperty("chunk").GetProperty("bytes").GetBytesFromBase64();
            var chunk = JsonSerializer.Deserialize<JsonElement>(bytes);
            await Assert.That(chunk.GetProperty("outputText").GetString()).IsEqualTo("blue whale");
        }
    }

    [Test]
    public async Task ConverseEndpoint_ReturnsBedrockConverseShapeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("amazon.nova-lite-v1:0", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "bedrock-converse",
                    scenario => scenario
                        .ForModel("amazon.nova-lite-v1:0")
                        .WhenUserContains("largest animal")
                        .Responds("blue whale")
                ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bedrock/model/amazon.nova-lite-v1:0/converse",
            new
            {
                system = new[] { new { text = "Answer briefly." } },
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[] { new { text = "largest animal?" } },
                    },
                },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("stopReason").GetString()).IsEqualTo("end_turn");
        await Assert.That(
                payload
                    .GetProperty("output")
                    .GetProperty("message")
                    .GetProperty("role")
                    .GetString()
            )
            .IsEqualTo("assistant");
        await Assert.That(
                payload
                    .GetProperty("output")
                    .GetProperty("message")
                    .GetProperty("content")[0]
                    .GetProperty("text")
                    .GetString()
            )
            .IsEqualTo("blue whale");
        var usage = payload.GetProperty("usage");
        var inputTokens = usage.GetProperty("inputTokens").GetInt32();

        await Assert.That(inputTokens).IsGreaterThan(0);
        await Assert.That(payload.GetProperty("usage").GetProperty("outputTokens").GetInt32())
            .IsEqualTo(2);
        await Assert.That(payload.GetProperty("usage").GetProperty("totalTokens").GetInt32())
            .IsEqualTo(inputTokens + 2);
        await Assert.That(payload.GetProperty("metrics").GetProperty("latencyMs").GetInt32())
            .IsEqualTo(0);
    }

    [Test]
    public async Task ConverseEndpoint_WithCachePoint_ReturnsBedrockCacheUsageAsync()
    {
        var cacheableSystemPrompt = CreatePromptWithAtLeastTokens(1100);
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("amazon.nova-cache-v1:0", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "bedrock-cache",
                    scenario => scenario
                        .ForModel("amazon.nova-cache-v1:0")
                        .WhenUserContains("bedrock cache turn")
                        .Responds("first")
                        .Responds("second")
                ));
        using var client = host.GetTestClient();

        var first = await PostCachedBedrockConverseAsync(
            client,
            cacheableSystemPrompt,
            "bedrock cache turn one"
        );
        var second = await PostCachedBedrockConverseAsync(
            client,
            cacheableSystemPrompt,
            "bedrock cache turn two"
        );

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        var firstUsage = (await first.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions))
            .GetProperty("usage");
        var secondUsage = (await second.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions))
            .GetProperty("usage");

        await Assert.That(firstUsage.GetProperty("cacheWriteInputTokens").GetInt32())
            .IsGreaterThanOrEqualTo(1024);
        await Assert.That(firstUsage.TryGetProperty("cacheReadInputTokens", out _))
            .IsFalse();
        await Assert.That(secondUsage.GetProperty("cacheReadInputTokens").GetInt32())
            .IsGreaterThanOrEqualTo(1024);
        await Assert.That(secondUsage.TryGetProperty("cacheWriteInputTokens", out _))
            .IsFalse();
    }

    [Test]
    public async Task ConverseStreamEndpoint_ReturnsBedrockEventStreamShapeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("amazon.nova-lite-v1:0", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "bedrock-converse-stream",
                    scenario => scenario
                        .ForModel("amazon.nova-lite-v1:0")
                        .WhenUserContains("stream largest animal")
                        .Responds("blue whale", "blue ", "whale")
                ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bedrock/model/amazon.nova-lite-v1:0/converse-stream",
            new
            {
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[] { new { text = "stream largest animal" } },
                    },
                },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var lines = await ReadEventStreamAsync(response);

        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/vnd.amazon.eventstream");
        await Assert.That(lines[0].GetProperty("messageStart").GetProperty("role").GetString())
            .IsEqualTo("assistant");
        await Assert.That(lines[1].GetProperty("contentBlockStart").GetProperty("contentBlockIndex").GetInt32())
            .IsEqualTo(0);
        await Assert.That(
                lines[2]
                    .GetProperty("contentBlockDelta")
                    .GetProperty("delta")
                    .GetProperty("text")
                    .GetString()
            )
            .IsEqualTo("blue ");
        await Assert.That(
                lines[3]
                    .GetProperty("contentBlockDelta")
                    .GetProperty("delta")
                    .GetProperty("text")
                    .GetString()
            )
            .IsEqualTo("whale");
        await Assert.That(lines[4].GetProperty("contentBlockStop").GetProperty("contentBlockIndex").GetInt32())
            .IsEqualTo(0);
        await Assert.That(lines[5].GetProperty("messageStop").GetProperty("stopReason").GetString())
            .IsEqualTo("end_turn");
        var streamUsage = lines[6].GetProperty("metadata").GetProperty("usage");
        var streamInputTokens = streamUsage.GetProperty("inputTokens").GetInt32();

        await Assert.That(streamInputTokens).IsGreaterThan(0);
        await Assert.That(streamUsage.GetProperty("outputTokens").GetInt32()).IsEqualTo(2);
        await Assert.That(streamUsage.GetProperty("totalTokens").GetInt32())
            .IsEqualTo(streamInputTokens + 2);
    }

    [Test]
    public async Task InvokeEndpoint_ReturnsTitanTextShapeForChatModelAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("amazon.titan-text-premier-v1:0", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "bedrock-titan-text",
                    scenario => scenario
                        .ForModel("amazon.titan-text-premier-v1:0")
                        .WhenUserContains("hello world")
                        .Responds("prints a greeting")
                ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bedrock/model/amazon.titan-text-premier-v1:0/invoke",
            new
            {
                inputText = "Describe hello world",
                textGenerationConfig = new { maxTokenCount = 64 },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("inputTextTokenCount").GetInt32()).IsEqualTo(3);
        await Assert.That(payload.GetProperty("results").GetArrayLength()).IsEqualTo(1);
        await Assert.That(payload.GetProperty("results")[0].GetProperty("outputText").GetString())
            .IsEqualTo("prints a greeting");
        await Assert.That(payload.GetProperty("results")[0].GetProperty("completionReason").GetString())
            .IsEqualTo("FINISHED");
    }

    [Test]
    public async Task InvokeModelWithResponseStreamEndpoint_ReturnsChunkBytesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("amazon.titan-text-premier-v1:0", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "bedrock-titan-stream",
                    scenario => scenario
                        .ForModel("amazon.titan-text-premier-v1:0")
                        .WhenUserContains("stream hello")
                        .Responds("prints a greeting", "prints ", "a greeting")
                ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bedrock/model/amazon.titan-text-premier-v1:0/invoke-with-response-stream",
            new
            {
                inputText = "stream hello",
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var lines = await ReadEventStreamAsync(response);
        var firstChunk = ReadBedrockChunkPayload(lines[0]);
        var secondChunk = ReadBedrockChunkPayload(lines[1]);

        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/vnd.amazon.eventstream");
        await Assert.That(response.Headers.GetValues("x-amzn-bedrock-content-type").Single())
            .IsEqualTo("application/json");
        await Assert.That(firstChunk.GetProperty("outputText").GetString()).IsEqualTo("prints ");
        await Assert.That(secondChunk.GetProperty("outputText").GetString()).IsEqualTo("a greeting");
    }

    [Test]
    public async Task InvokeEndpoint_ReturnsTitanEmbeddingShapeForEmbeddingModelAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("amazon.titan-embed-text-v2:0", LlmTestServerModelKind.Embedding)
                .WithDefaultEmbeddingVector(0.125f, 0.25f, 0.5f));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bedrock/model/amazon.titan-embed-text-v2:0/invoke",
            new
            {
                inputText = "embed this text",
                embeddingTypes = new[] { "float" },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("inputTextTokenCount").GetInt32()).IsEqualTo(3);
        await Assert.That(payload.GetProperty("embedding").GetArrayLength()).IsEqualTo(3);
        await Assert.That(
                payload.GetProperty("embeddingsByType").GetProperty("float").GetArrayLength()
            )
            .IsEqualTo(3);
    }

    [Test]
    public async Task InvokeEndpoint_ReturnsImageShapeForImageModelAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("stability.stable-image-ultra-v1:1", LlmTestServerModelKind.Image)
                .WithDefaultImageDataUri("data:image/png;base64,ZmFrZS1wbmc="));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bedrock/model/stability.stable-image-ultra-v1:1/invoke",
            new
            {
                prompt = "A simple diagram",
                output_format = "png",
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("images").GetArrayLength()).IsEqualTo(1);
        await Assert.That(payload.GetProperty("images")[0].GetString()).IsEqualTo("ZmFrZS1wbmc=");
        await Assert.That(payload.GetProperty("finish_reasons").GetArrayLength()).IsEqualTo(1);
        await Assert.That(payload.GetProperty("finish_reasons")[0].ValueKind)
            .IsEqualTo(JsonValueKind.Null);
    }

    private static async Task<JsonElement[]> ReadEventStreamAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsByteArrayAsync();
        var events = new List<JsonElement>();
        using var decoder = new Amazon.Runtime.EventStreams.Internal.EventStreamDecoder();
        decoder.MessageReceived += (_, args) =>
        {
            var eventType = args.Message.Headers[":event-type"].AsString();
            using var document = JsonDocument.Parse(args.Message.Payload);
            events.Add(JsonSerializer.SerializeToElement(
                new Dictionary<string, JsonElement> { [eventType] = document.RootElement }
            ));
        };
        // Exercise arbitrary network fragmentation, including split preludes and checksums.
        for (var offset = 0; offset < body.Length; offset += 7)
        {
            var fragment = body.AsSpan(offset, Math.Min(7, body.Length - offset)).ToArray();
            decoder.ProcessData(fragment, 0, fragment.Length);
        }

        return events.ToArray();
    }

    private static JsonElement ReadBedrockChunkPayload(JsonElement streamEvent)
    {
        var bytes = streamEvent.GetProperty("chunk").GetProperty("bytes").GetString();
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(bytes!));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static Task<HttpResponseMessage> PostCachedBedrockConverseAsync(
        HttpClient client,
        string systemPrompt,
        string userPrompt
    )
    {
        return client.PostAsJsonAsync(
            "/bedrock/model/amazon.nova-cache-v1:0/converse",
            new
            {
                system = new[]
                {
                    new
                    {
                        text = systemPrompt,
                        cachePoint = new { type = "default" },
                    },
                },
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[] { new { text = userPrompt } },
                    },
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
