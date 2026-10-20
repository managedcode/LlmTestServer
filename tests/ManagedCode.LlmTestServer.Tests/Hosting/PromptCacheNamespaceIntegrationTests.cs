using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Hosting;

public sealed class PromptCacheNamespaceIntegrationTests
{
    private static readonly string[] _paths =
    [
        "/openai/v1/chat/completions",
        "/groq/openai/v1/chat/completions",
        "/azure-openai/openai/deployments/gpt-4.1-mini/chat/completions?api-version=2024-10-21",
    ];

    [Test]
    public async Task PromptCache_SeparatesProviderNamespacesBeforeWarmingEachIndependentlyAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("namespace-cache", scenario => scenario
            .Responds("one").Responds("two").Responds("three").Responds("four").Responds("five").Responds("six")));
        using var http = host.GetTestClient();
        foreach (var path in _paths)
        {
            await Assert.That(await CachedAsync(http, path)).IsEqualTo(0);
        }
        foreach (var path in _paths)
        {
            await Assert.That(await CachedAsync(http, path)).IsGreaterThanOrEqualTo(1024);
        }
        var evidence = await new LlmTestServerClient(http).GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(evidence.Requests.Count).IsEqualTo(6);
        await Assert.That(evidence.Requests.Select(trace => trace.ProviderNamespace).Distinct().Count()).IsEqualTo(3);
        foreach (var trace in evidence.Requests.Take(3))
        {
            await Assert.That(trace.Usage!.CachedInputTokens).IsEqualTo(0);
            await Assert.That(trace.Usage.CacheCreationInputTokens).IsGreaterThanOrEqualTo(1024);
        }
        foreach (var trace in evidence.Requests.Skip(3))
        {
            await Assert.That(trace.Usage!.CachedInputTokens).IsGreaterThanOrEqualTo(1024);
            await Assert.That(trace.Usage.CacheCreationInputTokens).IsEqualTo(0);
        }
    }

    [Test]
    public async Task PromptCache_SeparatesSessionsAndProvidersWithIdenticalKeysAndPrefixesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var first = await root.CreateSessionAsync(config => config.UseChatScenario("namespace-cache", scenario => scenario
            .Responds("one").Responds("two").Responds("three").Responds("four")));
        await using var second = await root.CreateSessionAsync(config => config.UseChatScenario("namespace-cache", scenario => scenario
            .Responds("one").Responds("two").Responds("three").Responds("four")));
        foreach (var session in new[] { first, second })
        {
            await Assert.That(await CachedAsync(session.HttpClient, _paths[0])).IsEqualTo(0);
            await Assert.That(await CachedAsync(session.HttpClient, _paths[1])).IsEqualTo(0);
        }
        foreach (var session in new[] { first, second })
        {
            await Assert.That(await CachedAsync(session.HttpClient, _paths[0])).IsGreaterThanOrEqualTo(1024);
            await Assert.That(await CachedAsync(session.HttpClient, _paths[1])).IsGreaterThanOrEqualTo(1024);
        }
        await Assert.That((await root.GetRequestsAsync()).Requests).IsEmpty();
        var firstEvidence = await first.Control.GetRequestsAsync();
        var secondEvidence = await second.Control.GetRequestsAsync();
        await Assert.That(firstEvidence.Requests.Count).IsEqualTo(4);
        await Assert.That(secondEvidence.Requests.Count).IsEqualTo(4);
        await Assert.That(firstEvidence.Requests.Select(trace => trace.SessionId).Distinct().Single())
            .IsNotEqualTo(secondEvidence.Requests.Select(trace => trace.SessionId).Distinct().Single());
    }

    private static async Task<int> CachedAsync(HttpClient http, string path)
    {
        using var response = await http.PostAsJsonAsync(path, new
        {
            model = LlmTestServerKnownModelIds.Gpt41Mini,
            prompt_cache_key = "same-cache-key",
            messages = new[] { new { role = "user", content = string.Concat(Enumerable.Repeat("A stable shared provider prefix. ", 400)) } },
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("usage").GetProperty("prompt_tokens_details").GetProperty("cached_tokens").GetInt32();
    }
}
