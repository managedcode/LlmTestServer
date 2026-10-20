using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Hosting;

public sealed class PromptCacheCapacityIntegrationTests
{
    [Test]
    [Arguments(1)]
    [Arguments(0)]
    public async Task RepeatedMultiPrefixRequest_ReadsRetainedLongestPrefixBeforeCapacityEvictionAsync(int maximumEntries)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var configuration = new LlmTestServerClientConfigurationBuilder().UseGpt41Mini()
            .UseChatScenario("prefix-capacity", scenario => scenario
                .Responds("first intact response")
                .Responds("second intact response")
                .Responds("third intact response"))
            .Build() with
        { MaxPromptCacheEntries = maximumEntries };
        await control.ConfigureAsync(configuration);
        var request = new
        {
            model = LlmTestServerKnownModelIds.Gpt41Mini,
            messages = new[]
            {
                new { role = "system", content = string.Join(' ', Enumerable.Repeat("System reference", 1200)) },
                new { role = "user", content = string.Join(' ', Enumerable.Repeat("User details", 1200)) },
            },
        };
        var responses = new List<JsonElement>();
        foreach (var expectedText in new[] { "first intact response", "second intact response", "third intact response" })
        {
            using var response = await http.PostAsJsonAsync("/openai/v1/chat/completions", request);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            await Assert.That(body.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()).IsEqualTo(expectedText);
            responses.Add(body);
        }

        var inputTokens = responses[0].GetProperty("usage").GetProperty("prompt_tokens").GetInt32();
        await Assert.That(inputTokens).IsGreaterThanOrEqualTo(2048);
        await Assert.That(CachedTokens(responses[0])).IsEqualTo(0);
        var expectedCachedTokens = maximumEntries == 0 ? 0 : inputTokens / 128 * 128;
        await Assert.That(CachedTokens(responses[1])).IsEqualTo(expectedCachedTokens);
        await Assert.That(CachedTokens(responses[2])).IsEqualTo(expectedCachedTokens);
        var evidence = await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(evidence.Requests.Count).IsEqualTo(3);
        for (var index = 0; index < responses.Count; index++)
        {
            var usage = evidence.Requests[index].Usage!;
            await Assert.That(evidence.Requests[index].StatusCode).IsEqualTo(200);
            await Assert.That(usage.InputTokens).IsEqualTo(inputTokens);
            await Assert.That(usage.CachedInputTokens).IsEqualTo(CachedTokens(responses[index]));
            await Assert.That(usage.CacheCreationInputTokens).IsEqualTo(index == 0 ? expectedCachedTokens : 0);
            await Assert.That(usage.TotalTokens).IsEqualTo(usage.InputTokens + usage.OutputTokens);
        }
    }

    private static int CachedTokens(JsonElement response)
    {
        return response.GetProperty("usage").GetProperty("prompt_tokens_details").GetProperty("cached_tokens").GetInt32();
    }
}
