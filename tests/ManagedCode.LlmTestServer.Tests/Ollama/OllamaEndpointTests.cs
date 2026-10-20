using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Ollama;

public sealed class OllamaEndpointTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task ChatEndpoint_WithStreamFalse_ReturnsOllamaChatShapeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("ollama-chat", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "ollama-blue-whale",
                    scenario => scenario
                        .ForModel("ollama-chat")
                        .WhenUserContains("largest animal")
                        .Responds("blue whale", "blue ", "whale")
                ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/ollama/api/chat",
            new
            {
                model = "ollama-chat",
                stream = false,
                messages = new[] { new { role = "user", content = "largest animal?" } },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("model").GetString()).IsEqualTo("ollama-chat");
        await Assert.That(payload.GetProperty("message").GetProperty("role").GetString())
            .IsEqualTo("assistant");
        await Assert.That(payload.GetProperty("message").GetProperty("content").GetString())
            .IsEqualTo("blue whale");
        await Assert.That(payload.GetProperty("done").GetBoolean()).IsTrue();
        await Assert.That(payload.GetProperty("done_reason").GetString()).IsEqualTo("stop");
        await Assert.That(payload.GetProperty("prompt_eval_count").GetInt32()).IsGreaterThan(0);
        await Assert.That(payload.GetProperty("eval_count").GetInt32()).IsEqualTo(2);
    }

    [Test]
    public async Task ChatEndpoint_DefaultStreamsOllamaJsonLinesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("ollama-chat", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "ollama-stream",
                    scenario => scenario
                        .ForModel("ollama-chat")
                        .WhenUserContains("stream")
                        .Responds("blue whale", "blue ", "whale")
                ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/ollama/api/chat",
            new
            {
                model = "ollama-chat",
                messages = new[] { new { role = "user", content = "stream response" } },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/x-ndjson");
        await Assert.That(lines).Count().IsEqualTo(3);
        await Assert.That(JsonDocument.Parse(lines[0]).RootElement.GetProperty("done").GetBoolean())
            .IsFalse();
        await Assert.That(
                JsonDocument
                    .Parse(lines[0])
                    .RootElement
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()
            )
            .IsEqualTo("blue ");
        await Assert.That(JsonDocument.Parse(lines[^1]).RootElement.GetProperty("done").GetBoolean())
            .IsTrue();
        await Assert.That(
                JsonDocument.Parse(lines[^1]).RootElement.GetProperty("prompt_eval_count").GetInt32()
            )
            .IsGreaterThan(0);
        await Assert.That(JsonDocument.Parse(lines[^1]).RootElement.GetProperty("eval_count").GetInt32())
            .IsEqualTo(2);
    }

    [Test]
    public async Task EmbedEndpoint_ReturnsOllamaEmbeddingShapeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("ollama-embed", LlmTestServerModelKind.Embedding)
                .WithDefaultEmbeddingVector(0.25f, 0.5f));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/ollama/api/embed",
            new { model = "ollama-embed", input = new[] { "alpha", "beta" } },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("model").GetString()).IsEqualTo("ollama-embed");
        await Assert.That(payload.GetProperty("embeddings").GetArrayLength()).IsEqualTo(2);
        await Assert.That(payload.GetProperty("embeddings")[0].GetArrayLength()).IsEqualTo(2);
        await Assert.That(payload.GetProperty("prompt_eval_count").GetInt32()).IsEqualTo(2);
    }
}
