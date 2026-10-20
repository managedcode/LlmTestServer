using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Providers;

public sealed class ProviderApiDriftTests
{
    [Test]
    [Arguments("")]
    [Arguments("?alt=json")]
    [Arguments("?alt=sse")]
    public async Task GeminiStreaming_PreservesResponseIdAndReasoningUsageAsync(string query)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
            .AddReasoningChatModel("gemini-2.5-flash", 17)
            .AddChatScenario("gemini-reasoning", scenario => scenario.ForModel("gemini-2.5-flash")
                .WhenUserContains("whale").Responds("blue whale", "blue ", "whale")));
        using var client = host.GetTestClient();
        using var response = await client.PostAsJsonAsync("/gemini/v1beta/models/gemini-2.5-flash:streamGenerateContent" + query,
            new { contents = new[] { new { role = "user", parts = new[] { new { text = "whale" } } } } });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var sse = query == "?alt=sse";
        var chunks = sse
            ? body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
                .Select(line => JsonSerializer.Deserialize<JsonElement>(line[6..])).ToArray()
            : JsonSerializer.Deserialize<JsonElement[]>(body)!;
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo(sse ? "text/event-stream" : "application/json");
        await Assert.That(chunks.Length).IsEqualTo(3);
        await Assert.That(string.Concat(chunks.Select(chunk => chunk.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString())))
            .IsEqualTo("blue whale");
        await Assert.That(chunks.Select(chunk => chunk.GetProperty("responseId").GetString()).Distinct().Count()).IsEqualTo(1);
        var usage = chunks[^1].GetProperty("usageMetadata");
        await Assert.That(usage.GetProperty("thoughtsTokenCount").GetInt32()).IsEqualTo(17);
        await Assert.That(usage.GetProperty("candidatesTokenCount").GetInt32()).IsEqualTo(2);
        await Assert.That(usage.GetProperty("totalTokenCount").GetInt32()).IsEqualTo(usage.GetProperty("promptTokenCount").GetInt32() + 19);
    }

    [Test]
    [Arguments("/openai/v1/responses")]
    [Arguments("/groq/openai/v1/responses")]
    [Arguments("/openrouter/api/v1/responses")]
    public async Task ResponsesStreaming_UsesDocumentedEventsAndStableItemIdentityAsync(string path)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
            .AddModel("gpt-4.1-mini", LlmTestServerModelKind.Chat)
            .AddChatScenario("response-events", scenario => scenario.ForModel("gpt-4.1-mini")
                .WhenUserContains("whale").Responds("blue whale", "blue ", "whale")));
        using var client = host.GetTestClient();
        using var response = await client.PostAsJsonAsync(path, new { model = "gpt-4.1-mini", input = "whale", stream = true });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var events = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line[6..])).ToArray();
        await Assert.That(events.Select(item => item.GetProperty("type").GetString()!).SequenceEqual(new[]
        {
            "response.created", "response.in_progress", "response.output_item.added", "response.content_part.added",
            "response.output_text.delta", "response.output_text.delta", "response.output_text.done", "response.content_part.done",
            "response.output_item.done", "response.completed",
        })).IsTrue();
        for (var index = 0; index < events.Length; index++)
        {
            await Assert.That(events[index].GetProperty("sequence_number").GetInt32()).IsEqualTo(index);
        }

        var id = events[2].GetProperty("item").GetProperty("id").GetString();
        foreach (var item in events.Skip(3).Take(5))
        {
            await Assert.That(item.GetProperty("item_id").GetString()).IsEqualTo(id);
        }

        await Assert.That(events[8].GetProperty("item").GetProperty("id").GetString()).IsEqualTo(id);
        await Assert.That(events[9].GetProperty("response").GetProperty("output")[0].GetProperty("id").GetString()).IsEqualTo(id);
        await Assert.That(events[6].GetProperty("text").GetString()).IsEqualTo("blue whale");
    }

    [Test]
    [Arguments(true, null)]
    [Arguments(false, "resp_previous")]
    [Arguments(null, "")]
    public async Task OpenRouterResponses_RejectsStateBeforeConsumingScenarioAsync(bool? store, string? previousResponseId)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
            .AddModel("gpt-4.1-mini", LlmTestServerModelKind.Chat)
            .AddChatScenario("stateless", scenario => scenario.ForModel("gpt-4.1-mini")
                .WhenUserContains("whale").Responds("blue whale")));
        using var client = host.GetTestClient();
        using var invalid = await client.PostAsJsonAsync("/openrouter/api/v1/responses", new
        {
            model = "gpt-4.1-mini",
            input = "whale",
            store,
            previous_response_id = previousResponseId,
        });
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var error = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(error.GetProperty("error").GetProperty("message").GetString()).Contains("stateless");
        using var valid = await client.PostAsJsonAsync("/openrouter/api/v1/responses", new
        {
            model = "gpt-4.1-mini",
            input = "whale",
            store = false,
            previous_response_id = (string?)null,
        });
        valid.EnsureSuccessStatusCode();
        var payload = await valid.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString())
            .IsEqualTo("blue whale");
    }

    [Test]
    public async Task OllamaChat_ReportsCacheReadsInFinalStreamingChunkAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
            .AddModel("llama3.2", LlmTestServerModelKind.Chat)
            .AddChatScenario("ollama-cache", scenario => scenario.ForModel("llama3.2")
                .WhenUserContains("whale").Responds("blue whale").Responds("blue whale", "blue ", "whale")));
        using var client = host.GetTestClient();
        var messages = new[] { new { role = "user", content = "Tell me about a whale" } };
        using var first = await client.PostAsJsonAsync("/ollama/api/chat", new { model = "llama3.2", messages, stream = false });
        first.EnsureSuccessStatusCode();
        var cold = await first.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(cold.GetProperty("prompt_eval_cached_count").GetInt32()).IsEqualTo(0);
        using var second = await client.PostAsJsonAsync("/ollama/api/chat", new { model = "llama3.2", messages, stream = true });
        second.EnsureSuccessStatusCode();
        var body = await second.Content.ReadAsStringAsync();
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        await Assert.That(lines.Length).IsEqualTo(3);
        await Assert.That(lines[0].GetProperty("prompt_eval_cached_count").GetInt32()).IsEqualTo(0);
        await Assert.That(lines[^1].GetProperty("done").GetBoolean()).IsTrue();
        await Assert.That(lines[^1].GetProperty("prompt_eval_cached_count").GetInt32())
            .IsEqualTo(cold.GetProperty("prompt_eval_count").GetInt32());
        await Assert.That(lines[^1].GetProperty("prompt_eval_cached_count").GetInt32()).IsGreaterThan(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AnthropicZeroMaxTokens_WarmsCacheWithoutConsumingResponseAsync(bool stream)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
            .AddModel(LlmTestServerKnownModelIds.ClaudeSonnet55, LlmTestServerModelKind.Chat)
            .AddChatScenario("cache-only", scenario => scenario.ForModel(LlmTestServerKnownModelIds.ClaudeSonnet55)
                .WhenUserContains("whale").Responds("blue whale")));
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var messages = new[] { new { role = "user", content = "whale " + string.Join(' ', Enumerable.Repeat("ocean", 1600)) } };
        var cacheControl = new { type = "ephemeral" };
        using var warm = await client.PostAsJsonAsync("/anthropic/v1/messages", new
        {
            model = LlmTestServerKnownModelIds.ClaudeSonnet55,
            messages,
            max_tokens = 0,
            cache_control = cacheControl,
            stream,
        });
        warm.EnsureSuccessStatusCode();
        var warmBody = await warm.Content.ReadAsStringAsync();
        await Assert.That(warmBody).Contains("\"output_tokens\":0");
        await Assert.That(warmBody).Contains("\"stop_reason\":\"max_tokens\"");
        await Assert.That(warmBody).DoesNotContain("blue whale");
        await Assert.That(warmBody).DoesNotContain("content_block_delta");
        using var response = await client.PostAsJsonAsync("/anthropic/v1/messages", new
        {
            model = LlmTestServerKnownModelIds.ClaudeSonnet55,
            messages,
            max_tokens = 64,
            cache_control = cacheControl,
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("content")[0].GetProperty("text").GetString()).IsEqualTo("blue whale");
        await Assert.That(payload.GetProperty("usage").GetProperty("cache_read_input_tokens").GetInt32()).IsGreaterThan(0);
    }
}
