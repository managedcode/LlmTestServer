using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Cohere;

public sealed class CohereEndpointTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task ChatEndpoint_ReturnsCohereChatShapeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("command-test", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "cohere-blue-whale",
                    scenario => scenario
                        .ForModel("command-test")
                        .WhenUserContains("largest animal")
                        .Responds("blue whale", "blue ", "whale")
                ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/cohere/v2/chat",
            new
            {
                model = "command-test",
                messages = new[] { new { role = "user", content = "largest animal?" } },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("finish_reason").GetString()).IsEqualTo("COMPLETE");
        await Assert.That(payload.GetProperty("message").GetProperty("role").GetString())
            .IsEqualTo("assistant");
        await Assert.That(
                payload
                    .GetProperty("message")
                    .GetProperty("content")[0]
                    .GetProperty("type")
                    .GetString()
            )
            .IsEqualTo("text");
        await Assert.That(
                payload
                    .GetProperty("message")
                    .GetProperty("content")[0]
                    .GetProperty("text")
                    .GetString()
            )
            .IsEqualTo("blue whale");
        var tokens = payload.GetProperty("usage").GetProperty("tokens");
        var billedUnits = payload.GetProperty("usage").GetProperty("billed_units");

        await Assert.That(tokens.GetProperty("input_tokens").GetInt32()).IsGreaterThan(0);
        await Assert.That(tokens.GetProperty("output_tokens").GetInt32()).IsEqualTo(2);
        await Assert.That(billedUnits.GetProperty("input_tokens").GetInt32())
            .IsEqualTo(tokens.GetProperty("input_tokens").GetInt32());
        await Assert.That(billedUnits.GetProperty("output_tokens").GetInt32()).IsEqualTo(2);
    }

    [Test]
    public async Task ChatEndpoint_StreamsCohereServerSentEventsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("command-test", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "cohere-stream",
                    scenario => scenario
                        .ForModel("command-test")
                        .WhenUserContains("stream")
                        .Responds("blue whale", "blue ", "whale")
                ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/cohere/v2/chat",
            new
            {
                model = "command-test",
                stream = true,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[] { new { type = "text", text = "stream response" } },
                    },
                },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("text/event-stream");
        await Assert.That(body).Contains("event: message-start");
        await Assert.That(body).Contains("event: content-start");
        await Assert.That(body).Contains("event: content-delta");
        await Assert.That(body).Contains("event: content-end");
        await Assert.That(body).Contains("event: message-end");
        await Assert.That(body).Contains("\"input_tokens\":");
        await Assert.That(body).Contains("\"output_tokens\":2");
        await Assert.That(body).Contains("blue ");
        await Assert.That(body).Contains("whale");
    }

    [Test]
    public async Task EmbedEndpoint_ReturnsCohereEmbeddingShapeForTextsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("embed-v4.0", LlmTestServerModelKind.Embedding)
                .WithDefaultEmbeddingVector(0.25f, 0.5f));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/cohere/v2/embed",
            new
            {
                model = "embed-v4.0",
                input_type = "classification",
                embedding_types = new[] { "float" },
                texts = new[] { "alpha", "beta" },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("embeddings").GetProperty("float").GetArrayLength())
            .IsEqualTo(2);
        await Assert.That(payload.GetProperty("embeddings").GetProperty("float")[0].GetArrayLength())
            .IsEqualTo(2);
    }

    [Test]
    public async Task EmbedEndpoint_ReturnsCohereEmbeddingShapeForInputsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("embed-v4.0", LlmTestServerModelKind.Embedding)
                .WithDefaultEmbeddingVector(0.125f, 0.25f));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/cohere/v2/embed",
            new
            {
                model = "embed-v4.0",
                input_type = "classification",
                embedding_types = new[] { "float" },
                inputs = new[]
                {
                    new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "alpha" },
                            new { type = "text", text = "beta" },
                        },
                    },
                },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("embeddings").GetProperty("float").GetArrayLength())
            .IsEqualTo(1);
        await Assert.That(payload.GetProperty("embeddings").GetProperty("float")[0].GetArrayLength())
            .IsEqualTo(2);
    }
}
