using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Hosting;

public sealed class StoredResponseAdmissionIntegrationTests
{
    [Test]
    [Arguments(0, 1048576L)]
    [Arguments(8, 1L)]
    public async Task FailedStorageAdmission_DoesNotWarmPromptCacheOrConsumeFixtureAsync(int maximumResponses, long maximumBytes)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseGpt41Mini()
            .UseChatResponseCapacity(maximumResponses, maximumBytes)
            .UseChatScenario("admission", scenario => scenario.Responds("first queued answer").Responds("second queued answer")));
        var prompt = CacheablePrefix("Admission reference");

        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", Request(prompt, store: true));
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var error = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(error.GetProperty("error").GetProperty("code").GetString()).IsEqualTo("llm_test_server_response_capacity_exceeded");

        var admitted = await CompleteAsync(http, prompt);
        await Assert.That(Text(admitted)).IsEqualTo("first queued answer");
        await Assert.That(CachedTokens(admitted)).IsEqualTo(0);
        await Assert.That(admitted.GetProperty("usage").GetProperty("input_tokens").GetInt32()).IsGreaterThanOrEqualTo(1024);
        var repeated = await CompleteAsync(http, prompt);
        await Assert.That(Text(repeated)).IsEqualTo("second queued answer");
        await Assert.That(CachedTokens(repeated)).IsGreaterThanOrEqualTo(1024);

        var evidence = await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(evidence.Requests.Count).IsEqualTo(3);
        await Assert.That(evidence.Requests[0].StatusCode).IsEqualTo(409);
        await Assert.That(evidence.Requests[1].StatusCode).IsEqualTo(200);
        await Assert.That(evidence.Requests[2].StatusCode).IsEqualTo(200);
        await Assert.That(evidence.Requests[1].Usage!.CachedInputTokens).IsEqualTo(0);
        await Assert.That(evidence.Requests[1].Usage!.CacheCreationInputTokens).IsGreaterThanOrEqualTo(1024);
        await Assert.That(evidence.Requests[2].Usage!.CachedInputTokens).IsEqualTo(CachedTokens(repeated));
    }

    [Test]
    [Arguments(0, 1048576L)]
    [Arguments(8, 1L)]
    public async Task FailedStorageAdmission_DoesNotEvictPreviouslyWarmedPrefixAsync(int maximumResponses, long maximumBytes)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var configuration = new LlmTestServerClientConfigurationBuilder().UseGpt41Mini()
            .UseChatResponseCapacity(maximumResponses, maximumBytes)
            .UseChatScenario("admission-eviction", scenario => scenario.Responds("warm prefix A").Responds("preserved queued answer"))
            .Build() with
        { MaxPromptCacheEntries = 1 };
        await control.ConfigureAsync(configuration);
        var firstPrefix = CacheablePrefix("Alpha retained reference");
        var rejectedPrefix = CacheablePrefix("Zulu rejected reference");

        var warm = await CompleteAsync(http, firstPrefix);
        await Assert.That(Text(warm)).IsEqualTo("warm prefix A");
        await Assert.That(CachedTokens(warm)).IsEqualTo(0);
        await Assert.That(warm.GetProperty("usage").GetProperty("input_tokens").GetInt32()).IsGreaterThanOrEqualTo(1024);
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", Request(rejectedPrefix, store: true));
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var error = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(error.GetProperty("error").GetProperty("code").GetString()).IsEqualTo("llm_test_server_response_capacity_exceeded");

        var preserved = await CompleteAsync(http, firstPrefix);
        await Assert.That(Text(preserved)).IsEqualTo("preserved queued answer");
        await Assert.That(CachedTokens(preserved)).IsGreaterThanOrEqualTo(1024);
        var evidence = await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(evidence.Requests.Count).IsEqualTo(3);
        await Assert.That(evidence.Requests[0].Usage!.CacheCreationInputTokens).IsGreaterThanOrEqualTo(1024);
        await Assert.That(evidence.Requests[2].Usage!.CachedInputTokens).IsEqualTo(CachedTokens(preserved));
        await Assert.That(evidence.Requests[2].Usage!.CacheCreationInputTokens).IsEqualTo(0);
    }

    [Test]
    [Arguments(0, 1048576L)]
    [Arguments(8, 1L)]
    public async Task FailedStorageAdmission_RollsBackConversationWithoutWarmingCacheAsync(int maximumResponses, long maximumBytes)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseGpt41Mini()
            .UseChatResponseCapacity(maximumResponses, maximumBytes)
            .UseConversation("admission-graph", graph => graph
                .Step("answer", step => step.WhenTurn(1).Responds("unconsumed graph answer").Complete())));
        var prefix = CacheablePrefix("Conversation admission reference");
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", Request(prefix, store: true));
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var error = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(error.GetProperty("error").GetProperty("code").GetString()).IsEqualTo("llm_test_server_response_capacity_exceeded");
        await Assert.That((await control.GetConversationsAsync()).Count).IsEqualTo(0);

        var admitted = await CompleteAsync(http, prefix);
        await Assert.That(Text(admitted)).IsEqualTo("unconsumed graph answer");
        await Assert.That(CachedTokens(admitted)).IsEqualTo(0);
        var state = (await control.GetConversationsAsync()).Single();
        await Assert.That(state.ExecutedSteps).IsEqualTo(1);
        var evidence = await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(evidence.Requests.Count).IsEqualTo(2);
        await Assert.That(evidence.Requests[1].Usage!.CacheCreationInputTokens).IsGreaterThanOrEqualTo(1024);
        await Assert.That(evidence.Requests[1].StepId).IsEqualTo("answer");
    }

    private static string CacheablePrefix(string reference)
    {
        return string.Join(' ', Enumerable.Repeat(reference, 1200));
    }

    private static object Request(string prefix, bool store)
    {
        return new
        {
            model = LlmTestServerKnownModelIds.Gpt41Mini,
            instructions = prefix,
            input = "Return the queued answer.",
            store,
        };
    }

    private static async Task<JsonElement> CompleteAsync(HttpClient http, string prefix)
    {
        using var response = await http.PostAsJsonAsync("/openai/v1/responses", Request(prefix, store: false));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static int CachedTokens(JsonElement response)
    {
        return response.GetProperty("usage")
        .GetProperty("input_tokens_details").GetProperty("cached_tokens").GetInt32();
    }

    private static string? Text(JsonElement response)
    {
        return response.GetProperty("output")[0]
        .GetProperty("content")[0].GetProperty("text").GetString();
    }
}
