using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Anthropic;

public sealed partial class AnthropicNativeWebSearchIntegrationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeCancellationOrReset_RollsBackGraphReservationAsync(bool reset)
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseChatModel(_model).UseConversation("anthropic-cancel", flow => flow.ForModel(_model)
            .Step("search", step => step.RespondsWithItems(Items()).DelaysBy(800).Complete())));
        using var cancellation = new CancellationTokenSource();
        var pending = http.PostAsJsonAsync("/anthropic/v1/messages", Request(), cancellation.Token);
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
            await control.ResetAsync(); using var superseded = await pending;
            await Assert.That(superseded.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        }
        else
        {
            cancellation.Cancel(); var cancelled = false;
            try { using var response = await pending; } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelled = true; }
            await Assert.That(cancelled).IsTrue();
        }
        await Assert.That((await control.GetConversationsAsync()).Count).IsEqualTo(0);
        await AssertContentAsync((await CompleteAsync(http)).GetProperty("content"));
        await Assert.That((await control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
    }

    [Test]
    public async Task NativeOpaquePayload_ConsumesConversationByteBudgetEvenThoughItCostsNoTokensAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var items = Items(); items[1] = ((LlmTestServerResponseServerToolResultItem)items[1]) with
        { Result = new LlmTestServerWebSearchResults { Results = [new() { Url = _url, Title = _title, EncryptedContent = new string('x', 10000) }] } };
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseChatModel(_model).UseConversationCapacity(4, 3000)
            .UseConversation("anthropic-budget", flow => flow.ForModel(_model).Step("search", step => step.RespondsWithItems(items).Complete())));
        using var rejected = await http.PostAsJsonAsync("/anthropic/v1/messages", Request());
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That((await control.GetConversationsAsync()).Count).IsEqualTo(0);
        await control.ConfigureAsync(Configuration()); await CompleteAsync(http);
    }

    [Test]
    [Arguments("max")]
    [Arguments("allowed")]
    [Arguments("blocked")]
    [Arguments("location")]
    public async Task NativeOptions_PartitionPromptCacheAndSurviveTypedExportAsync(string option)
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var control = new LlmTestServerClient(http); await control.ConfigureAsync(Configuration(3));
        var body = Request(input: string.Join(' ', Enumerable.Repeat("stable native Anthropic cache reference", 1600)));
        body["system"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = string.Join(' ', Enumerable.Repeat("long shared fixture prefix", 1600)), ["cache_control"] = new JsonObject { ["type"] = "ephemeral" } });
        var first = await CompleteAsync(http, body);
        await Assert.That(first.GetProperty("usage").TryGetProperty("cache_read_input_tokens", out var read) ? read.GetInt32() : 0).IsEqualTo(0);
        var tool = body["tools"]![0]!;
        switch (option)
        {
            case "max": tool["max_uses"] = 2; break;
            case "allowed": tool["allowed_domains"] = new JsonArray("example.com/source*"); break;
            case "blocked": tool["blocked_domains"] = new JsonArray("blocked.example"); break;
            case "location": tool["user_location"] = new JsonObject { ["type"] = "approximate", ["city"] = "Paris" }; break;
        }
        var changed = await CompleteAsync(http, body);
        await Assert.That(changed.GetProperty("usage").TryGetProperty("cache_read_input_tokens", out read) ? read.GetInt32() : 0).IsEqualTo(0);
        var repeated = await CompleteAsync(http, body);
        await Assert.That(repeated.GetProperty("usage").GetProperty("cache_read_input_tokens").GetInt32()).IsGreaterThanOrEqualTo(1024);
        var trace = (await control.ExportRequestsAsync()).Evidence.Requests[0];
        await Assert.That(trace.Usage!.CachedInputTokens).IsGreaterThanOrEqualTo(1024);
        var options = trace.NativeTools.Single().AnthropicWebSearchOptions!;
        switch (option)
        {
            case "max": await Assert.That(options.MaxUses).IsEqualTo(2); break;
            case "allowed": await Assert.That(options.AllowedDomains!.Single()).IsEqualTo("example.com/source*"); break;
            case "blocked": await Assert.That(options.BlockedDomains!.Single()).IsEqualTo("blocked.example"); break;
            case "location": await Assert.That(options.UserLocation!.City).IsEqualTo("Paris"); break;
        }
    }

    [Test]
    public async Task SessionsAndNamedConversations_IsolateNativeGraphExecutionAndResetAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var control = new LlmTestServerClient(http);
        await using var first = await control.CreateSessionAsync(config => config.UseChatModel(_model).UseChatScenario("session-native", scenario => scenario.ForModel(_model).RespondsWithItems(Items())));
        await using var second = await control.CreateSessionAsync(config => config.UseChatModel(_model).UseChatScenario("session-native", scenario => scenario.ForModel(_model).RespondsWithItems(Items())));
        await CompleteAsync(first.HttpClient); await CompleteAsync(second.HttpClient);
        await Assert.That((await first.Control.GetRequestsAsync()).Requests.Count).IsEqualTo(1);
        await Assert.That((await second.Control.GetRequestsAsync()).Requests.Count).IsEqualTo(1);
        await first.Control.ResetAsync();
        await Assert.That((await first.Control.GetRequestsAsync()).Requests.Count).IsEqualTo(0);
        await Assert.That((await second.Control.GetRequestsAsync()).Requests.Count).IsEqualTo(1);
    }
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CacheOnlyRequest_DoesNotExecuteNativeGraphBeforeOrdinaryCompletionAsync(bool streaming)
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseChatModel(_model).UseConversation("anthropic-cache-only", flow => flow.ForModel(_model)
            .Step("native", step => step.WhenTurn(1).RespondsWithItems(Items()).Complete())));
        using var conversation = control.ForConversation("anthropic-cache-only");
        var body = Request(streaming); body["max_tokens"] = 0;
        using var response = await conversation.HttpClient.PostAsJsonAsync("/anthropic/v1/messages", body);
        response.EnsureSuccessStatusCode();
        if (!streaming)
        {
            var message = await response.Content.ReadFromJsonAsync<JsonElement>();
            await Assert.That(message.GetProperty("content").GetArrayLength()).IsEqualTo(0);
            await Assert.That(message.GetProperty("stop_reason").GetString()).IsEqualTo("max_tokens");
            var usage = message.GetProperty("usage");
            await Assert.That(usage.GetProperty("output_tokens").GetInt32()).IsEqualTo(0);
            await Assert.That(usage.TryGetProperty("server_tool_use", out var server) ? server.GetProperty("web_search_requests").GetInt32() : 0).IsEqualTo(0);
        }
        else
        {
            var frames = new List<JsonElement>();
            foreach (var line in (await response.Content.ReadAsStringAsync()).Split('\n'))
            {
                if (line.StartsWith("data: ", StringComparison.Ordinal)) { frames.Add(JsonSerializer.Deserialize<JsonElement>(line[6..])); }
            }
            await Assert.That(frames.Count(frame => frame.GetProperty("type").GetString() == "content_block_start")).IsEqualTo(0);
            var start = frames.Single(frame => frame.GetProperty("type").GetString() == "message_start").GetProperty("message");
            await Assert.That(start.GetProperty("content").GetArrayLength()).IsEqualTo(0);
            var delta = frames.Single(frame => frame.GetProperty("type").GetString() == "message_delta");
            await Assert.That(delta.GetProperty("delta").GetProperty("stop_reason").GetString()).IsEqualTo("max_tokens");
            await Assert.That(delta.GetProperty("usage").GetProperty("output_tokens").GetInt32()).IsEqualTo(0);
            foreach (var usage in new[] { start.GetProperty("usage"), delta.GetProperty("usage") })
            {
                await Assert.That(usage.TryGetProperty("server_tool_use", out var server) ? server.GetProperty("web_search_requests").GetInt32() : 0).IsEqualTo(0);
            }
            await Assert.That(frames.Count(frame => frame.GetProperty("type").GetString() == "message_stop")).IsEqualTo(1);
        }
        await Assert.That((await conversation.Control.GetConversationsAsync()).Sum(state => state.ExecutedSteps)).IsEqualTo(0);
        var completed = await CompleteAsync(conversation.HttpClient);
        await AssertContentAsync(completed.GetProperty("content"));
        await Assert.That(completed.GetProperty("usage").GetProperty("server_tool_use").GetProperty("web_search_requests").GetInt32()).IsEqualTo(1);
        var state = (await conversation.Control.GetConversationsAsync()).Single();
        await Assert.That(state.ExecutedSteps).IsEqualTo(1);
        await Assert.That(state.IsTerminal).IsTrue();
    }

}
