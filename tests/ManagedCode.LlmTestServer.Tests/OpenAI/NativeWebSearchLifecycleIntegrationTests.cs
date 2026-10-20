using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeWebSearchIntegrationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InFlightNativeResponse_CancellationAndResetReleaseGraphReservationAsync(bool reset)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseGpt41Mini().UseConversation("native-inflight", graph => graph
            .Step("search", step => step.RespondsWithItems(Items()).DelaysBy(800).Complete())));
        using var cancellation = new CancellationTokenSource();
        var pending = http.PostAsJsonAsync("/openai/v1/responses", Request(), cancellation.Token);
        var busy = false;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if ((await control.GetConversationsAsync()).Any(state => state.IsBusy)) { busy = true; break; }
            if (pending.IsCompleted) { break; }
            await Task.Delay(2);
        }
        await Assert.That(busy).IsTrue();
        if (reset)
        {
            await control.ResetAsync();
            using var superseded = await pending;
            await Assert.That(superseded.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        }
        else
        {
            cancellation.Cancel();
            var cancelled = false;
            try { using var response = await pending; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelled = true; }
            await Assert.That(cancelled).IsTrue();
        }
        await Assert.That((await control.GetConversationsAsync()).Count).IsEqualTo(0);
        await AssertWireAsync(await CompleteAsync(http));
        await Assert.That((await control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
    }

    [Test]
    public async Task GraphReplay_PreservesOrderedNativeItemsAndDoesNotAdvanceTwiceAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseGpt41Mini().UseDeterminism("native-replay", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero))
            .UseConversation("native-replay", graph => graph.Step("search", step => step.RespondsWithItems(Items()).Complete())));
        var first = await CompleteAsync(http);
        var replay = await CompleteAsync(http);
        await AssertWireAsync(replay);
        await Assert.That(replay.GetProperty("id").GetString()).IsEqualTo(first.GetProperty("id").GetString());
        await Assert.That(replay.GetProperty("output").GetRawText()).IsEqualTo(first.GetProperty("output").GetRawText());
        await Assert.That((await control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        var evidence = await control.GetRequestsAsync();
        await Assert.That(evidence.Requests.Count(trace => trace.IsReplay)).IsEqualTo(1);
        await control.ResetAsync();
        var reset = await CompleteAsync(http);
        await Assert.That(reset.GetProperty("output").GetRawText()).IsEqualTo(first.GetProperty("output").GetRawText());
        await Assert.That(reset.GetProperty("created_at").GetInt64()).IsEqualTo(first.GetProperty("created_at").GetInt64());
    }

    [Test]
    [Arguments("credential")]
    [Arguments("provider")]
    [Arguments("session")]
    [Arguments("reset")]
    [Arguments("storeFalse")]
    public async Task StoredNativeHistory_PreservesExistingOwnershipBoundariesAsync(string boundary)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(Configuration(2));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "first-key");
        var initial = Request();
        if (boundary == "storeFalse") { initial["store"] = false; }
        var first = await CompleteAsync(http, initial);
        await AssertWireAsync(first);
        var follow = new JsonObject
        {
            ["model"] = "gpt-4.1-mini",
            ["input"] = "follow",
            ["previous_response_id"] = first.GetProperty("id").GetString(),
        };
        if (boundary == "credential") { http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "second-key"); }
        if (boundary == "reset") { await control.ResetAsync(); }
        var path = boundary == "provider" ? "/azure-openai/openai/v1/responses" : "/openai/v1/responses";
        if (boundary == "session")
        {
            await using var session = await control.CreateSessionAsync(config => config.UseGpt41Mini()
                .UseChatScenario("session-native", scenario => scenario.RespondsWithItems(Items())));
            using var rejected = await session.HttpClient.PostAsJsonAsync(path, follow);
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That((await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString()).IsEqualTo("response_not_found");
            await AssertWireAsync(await CompleteAsync(session.HttpClient));
        }
        else
        {
            using var rejected = await http.PostAsJsonAsync(path, follow);
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That((await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString()).IsEqualTo("response_not_found");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "first-key");
            await AssertWireAsync(await CompleteAsync(http));
        }
    }

    [Test]
    public async Task NativeFixtureSourcesAndCitations_RespectAllowedDomainsWithoutConsumingResponseAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration());
        var body = Request();
        body["tools"]![0]!["filters"] = new JsonObject { ["allowed_domains"] = new JsonArray("unrelated.example.org") };
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That((await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString()).IsEqualTo("llm_test_server_fixture_mismatch");
        await AssertWireAsync(await CompleteAsync(http));
    }
}
