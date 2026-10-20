using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.AI;

namespace ManagedCode.LlmTestServer.Tests.Regression;

public sealed class ReviewHttpRegressionTests
{
    private const string _chat = """{"model":"gpt-4.1-mini","messages":[{"role":"user","content":"whale"}]}""";
    private static StringContent Json(string body)
    {
        return new(body, Encoding.UTF8, "application/json");
    }

    [Test]
    public async Task OfficialOpenAiSdk_ReceivesFinalStreamingUsageAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddReasoningChatModel(LlmTestServerKnownModelIds.Gpt41Mini, 17)
            .AddChatScenario("queue", scenario => scenario.Responds("blue whale", "blue ", "whale")));
        using var http = host.GetTestClient();
        var sdk = new global::OpenAI.Chat.ChatClient(LlmTestServerKnownModelIds.Gpt41Mini,
            new System.ClientModel.ApiKeyCredential("test-key"), new global::OpenAI.OpenAIClientOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/openai/v1"),
                Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(http),
            });
        var updates = new List<global::OpenAI.Chat.StreamingChatCompletionUpdate>();
        await foreach (var update in sdk.CompleteChatStreamingAsync([new global::OpenAI.Chat.UserChatMessage("whale")]))
        {
            updates.Add(update);
        }
        var usage = updates.Single(update => update.Usage is not null).Usage;
        await Assert.That(usage.InputTokenCount).IsGreaterThan(0);
        await Assert.That(usage.OutputTokenDetails.ReasoningTokenCount).IsEqualTo(17);
        await Assert.That(usage.TotalTokenCount).IsEqualTo(usage.InputTokenCount + usage.OutputTokenCount);
        await Assert.That(updates.Count(update => update.FinishReason == global::OpenAI.Chat.ChatFinishReason.Stop)).IsEqualTo(1);
    }

    [Test]
    [Arguments("models")]
    [Arguments("datasets")]
    [Arguments("chatScenarios")]
    [Arguments("faultSimulation")]
    [Arguments("defaultAudioBytes")]
    public async Task InvalidConfigure_PreservesConfigurationAndTracesAsync(string property)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("queue", s => s.Responds("first").Responds("second")));
        using var client = host.GetTestClient();
        using var first = await client.PostAsync("/openai/v1/chat/completions", Json(_chat));
        first.EnsureSuccessStatusCode();
        var before = (await client.GetFromJsonAsync<List<LlmTestServerProviderHttpTrace>>("/admin-api/requests"))!.Select(trace => trace.RequestId).ToArray();
        using var invalid = await client.PostAsync("/admin-api/configure", Json($"{{\"{property}\":null}}"));
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await client.GetFromJsonAsync<List<LlmTestServerProviderHttpTrace>>("/admin-api/requests"))!.Select(trace => trace.RequestId).ToArray()).IsEquivalentTo(before);
        using var second = await client.PostAsync("/openai/v1/chat/completions", Json(_chat));
        second.EnsureSuccessStatusCode();
        await Assert.That(await second.Content.ReadAsStringAsync()).Contains("second");
    }

    [Test]
    [Arguments("/gemini/v1beta/models/gpt-4.1-mini:generateContent", "{\"contents\":[{\"parts\":null}]}")]
    [Arguments("/gemini/v1beta/models/gpt-4.1-mini:generateContent", "{\"contents\":[{\"parts\":[null]}]}")]
    [Arguments("/gemini/v1beta/models/gpt-4.1-mini:generateContent", "{\"contents\":null}")]
    [Arguments("/gemini/v1beta/models/text-embedding-3-small:embedContent", "{\"content\":null}")]
    [Arguments("/anthropic/v1/messages", "{\"model\":\"gpt-4.1-mini\",\"max_tokens\":8,\"messages\":null}")]
    [Arguments("/ollama/api/chat", "{\"model\":\"gpt-4.1-mini\",\"messages\":null}")]
    [Arguments("/cohere/v2/chat", "{\"model\":\"gpt-4.1-mini\",\"messages\":null}")]
    [Arguments("/openai/v1/chat/completions", "{\"model\":\"gpt-4.1-mini\",\"messages\":null}")]
    public async Task NestedNullPayloads_ReturnBadRequestAsync(string path, string body)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        using var response = await client.PostAsync(path, Json(body));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await client.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!.TotalEvents).IsEqualTo(0);
    }

    [Test]
    [Arguments("")]
    [Arguments("?api-version=")]
    [Arguments("?api-version=not-a-version")]
    [Arguments("?api-version=2024-10-21&api-version=other")]
    [Arguments("?api-version=2024-10-21&api-version=2024-10-21")]
    [Arguments("?api-version=2025-02-30")]
    [Arguments("?api-version=2025-04-01-preview-preview")]
    [Arguments("?api-version=2025-04-01-beta")]
    [Arguments("?api-version=2025-4-1")]
    [Arguments("?api-version=2030-01-01")]
    [Arguments("?api-version=2030-01-01-preview")]
    [Arguments("?api-version=2025-04-02-preview")]
    public async Task AzureLegacyVersion_IsValidatedBeforeQueueConsumptionAsync(string query)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("queue", s => s.Responds("first")));
        using var client = host.GetTestClient();
        const string path = "/azure-openai/openai/deployments/gpt-4.1-mini/chat/completions";
        using var invalid = await client.PostAsync(path + query, Json(_chat));
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var valid = await client.PostAsync(path + "?api-version=2024-10-21", Json(_chat));
        valid.EnsureSuccessStatusCode();
        await Assert.That(await valid.Content.ReadAsStringAsync()).Contains("first");
    }

    [Test]
    [Arguments("2024-10-21")]
    [Arguments("2025-04-01-preview")]
    public async Task AzureLegacyVersion_AcceptsDocumentedStableAndPreviewVersionsAsync(string version)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("queue", s => s.Responds("first")));
        using var client = host.GetTestClient();
        using var response = await client.PostAsync(
            "/azure-openai/openai/deployments/gpt-4.1-mini/chat/completions?api-version=" + version, Json(_chat));

        response.EnsureSuccessStatusCode();
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("first");
    }

    [Test]
    public async Task ChatClient_PreservesUsageAndTerminalUpdate_EmbeddingsPreserveUsageAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddReasoningChatModel(LlmTestServerKnownModelIds.Gpt41Mini, 17)
            .AddChatScenario("queue", s => s.Responds("blue whale").Responds("blue whale", "blue ", "whale")));
        using var http = host.GetTestClient();
        using var client = new LlmTestServerChatClient(http);
        var response = await client.GetResponseAsync([new(ChatRole.User, "whale")]);
        await Assert.That(response.Usage!.ReasoningTokenCount).IsEqualTo(17);
        await Assert.That(response.Usage.InputTokenCount.GetValueOrDefault()).IsGreaterThan(0);
        await Assert.That(response.Usage.TotalTokenCount).IsEqualTo(response.Usage.InputTokenCount + response.Usage.OutputTokenCount);
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new(ChatRole.User, "whale")]))
        {
            updates.Add(update);
        }

        await Assert.That(string.Concat(updates.Select(update => update.Text))).IsEqualTo("blue whale");
        await Assert.That(updates.Count(update => update.FinishReason == ChatFinishReason.Stop)).IsEqualTo(1);
        var usage = updates.SelectMany(update => update.Contents).OfType<UsageContent>().Single().Details;
        await Assert.That(usage.TotalTokenCount).IsEqualTo(response.Usage.TotalTokenCount);
        await Assert.That(usage.ReasoningTokenCount).IsEqualTo(17);
        using var embeddings = new LlmTestServerEmbeddingGenerator(http);
        var generated = await embeddings.GenerateAsync(["blue whale"]);
        await Assert.That(generated.Usage!.InputTokenCount.GetValueOrDefault()).IsGreaterThan(0);
        await Assert.That(generated.Usage.TotalTokenCount).IsEqualTo(generated.Usage.InputTokenCount);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task StreamingUsage_IsOptInAndFollowsTerminalChoiceAsync(bool includeUsage)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("queue", s => s.Responds("blue whale", "blue ", "whale")));
        using var http = host.GetTestClient();
        using var response = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
        {
            model = LlmTestServerKnownModelIds.Gpt41Mini,
            messages = new[] { new { role = "user", content = "whale" } },
            stream = true,
            stream_options = new { include_usage = includeUsage },
        });
        response.EnsureSuccessStatusCode();
        var chunks = (await response.Content.ReadAsStringAsync()).Split('\n')
            .Where(line => line.StartsWith("data: {", StringComparison.Ordinal)).Select(line => JsonSerializer.Deserialize<JsonElement>(line[6..])).ToList();
        var usageChunks = chunks.Where(chunk => chunk.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object).ToList();
        await Assert.That(usageChunks.Count).IsEqualTo(includeUsage ? 1 : 0);
        if (includeUsage)
        {
            await Assert.That(chunks[^1].GetProperty("choices").GetArrayLength()).IsEqualTo(0);
            await Assert.That(chunks[^2].GetProperty("choices")[0].GetProperty("finish_reason").GetString()).IsEqualTo("stop");
            await Assert.That(chunks[^1].GetProperty("usage").GetProperty("total_tokens").GetInt32()).IsGreaterThan(0);
        }
    }
    [Test]
    [Arguments("/microsoft-foundry/chat/completions")]
    [Arguments("/microsoft-foundry/models/chat/completions")]
    [Arguments("/microsoft-foundry/embeddings")]
    [Arguments("/microsoft-foundry/models/embeddings")]
    public async Task FoundryInference_RequiresDocumentedVersionWithoutConsumingQueueAsync(string path)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("queue", s => s.Responds("first")));
        using var http = host.GetTestClient();
        foreach (var query in new[] { "", "?api-version=wrong", "?api-version=2024-05-01-preview&api-version=wrong" })
        {
            using var response = await http.PostAsync(path + query, Json(_chat));
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        var summary = (await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!;
        await Assert.That(summary.TotalEvents).IsEqualTo(0);
        using var success = await http.PostAsync("/microsoft-foundry/chat/completions?api-version=2024-05-01-preview", Json(_chat));
        await Assert.That(await success.Content.ReadAsStringAsync()).Contains("first");
    }

}
