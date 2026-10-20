using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed class ScenarioFlowIntegrationTests
{
    [Test]
    public async Task ExactConversation_RequiresScenarioCredentialAndOrderedHistoryAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("exact", scenario => scenario
            .RequireBearerToken("scenario-key").WithExactMatch(
                new LlmTestServerMessage { Role = "system", Content = "Follow the script" },
                new LlmTestServerMessage { Role = "user", Content = "hello" })
            .Responds("first").DelaysBy(10).Responds("second")));
        using var http = host.GetTestClient();
        var body = new
        {
            model = "gpt-4.1-mini",
            messages = new[] {
            new { role = "system", content = "Follow the script" }, new { role = "user", content = "hello" } }
        };
        using var unauthenticated = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        await Assert.That(unauthenticated.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "scenario-key");
        using var reordered = await http.PostAsJsonAsync("/openai/v1/chat/completions", new { body.model, messages = body.messages.Reverse().ToArray() });
        await Assert.That(reordered.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await reordered.Content.ReadAsStringAsync()).Contains("llm_test_server_unmatched_request");
        foreach (var expected in new[] { "first", "second" })
        {
            using var response = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
            response.EnsureSuccessStatusCode();
            await Assert.That(await response.Content.ReadAsStringAsync()).Contains(expected);
        }
        using var exhausted = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        await Assert.That(exhausted.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var summary = (await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!;
        await Assert.That(summary.AuthFailed).IsEqualTo(1);
        await Assert.That(summary.Unmatched).IsEqualTo(1);
        await Assert.That(summary.ScenarioExhausted).IsEqualTo(1);
        await Assert.That(summary.Matched).IsEqualTo(2);
    }

    [Test]
    public async Task CancelledDelayedResponse_DoesNotConsumeScenarioAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("cancel", scenario => scenario.Responds("retry succeeds").DelaysBy(1000)));
        using var http = host.GetTestClient();
        var body = new { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "hello" } } };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var cancelled = false;
        try
        {
            using var unexpected = await http.PostAsJsonAsync("/openai/v1/chat/completions", body, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            cancelled = true;
        }
        await Assert.That(cancelled).IsTrue();
        using var retry = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        retry.EnsureSuccessStatusCode();
        await Assert.That(await retry.Content.ReadAsStringAsync()).Contains("retry succeeds");
        var summary = (await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!;
        await Assert.That(summary.Matched).IsEqualTo(1);
    }
}
