using System.ClientModel;
using System.Net;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Tools.WellKnown;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using OpenAI.Chat;

namespace ManagedCode.LlmTestServer.Tests.Tools;

public sealed partial class WellKnownToolFaultIntegrationTests
{
    [Test]
    public async Task Provider500_IsOneScriptedAttemptThenExplicitRetryReturnsToolWithoutWarmingErrorCacheAsync()
    {
        await using var app = await StartHealthyFaultHostAsync(config => config.AddConversation("provider-failure", graph => graph
            .Step("failure", step => step.WhenLatestUserEquals("run tool")
                .Fails(500, "fixture_provider_internal_error", "Scripted provider failure").Then("recover"))
            .Step("recover", step => step.WhenLatestUserEquals("run tool")
                .CallsTool("weather", "{}", "call_after_provider_error").Complete())));
        using var http = app.GetTestClient();
        using var conversation = new LlmTestServerClient(http).ForConversation("provider-failure");
        var sdk = CreateSdk(conversation.HttpClient);
        var options = new ChatCompletionOptions { Tools = { ChatTool.CreateFunctionTool("weather") } };
        List<ChatMessage> messages = [new SystemChatMessage(LongCachePrefix()), new UserChatMessage("run tool")];
        ClientResultException? failure = null;
        try { await sdk.CompleteChatAsync(messages, options); }
        catch (ClientResultException exception) { failure = exception; }
        await Assert.That(failure?.Status).IsEqualTo(500);
        var failed = await conversation.Control.GetRequestsAsync();
        await Assert.That(failed.Requests.Count).IsEqualTo(1);
        await Assert.That(failed.Requests.Single().Usage!.CacheCreationInputTokens).IsEqualTo(0);
        await Assert.That(failed.Requests.Single().ResponseToolCalls.Count).IsEqualTo(0);
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        using var healthy = await http.GetAsync("/health");
        await Assert.That(healthy.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var recovered = (await sdk.CompleteChatAsync(messages, options)).Value;
        await Assert.That(recovered.ToolCalls.Single().Id).IsEqualTo("call_after_provider_error");
        var evidence = await conversation.Control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(evidence.Requests.Select(request => request.StatusCode)).IsEquivalentTo(new int?[] { 500, 200 });
        await Assert.That(evidence.Requests[^1].Usage!.CachedInputTokens).IsEqualTo(0);
        await Assert.That(evidence.Requests[^1].Usage!.CacheCreationInputTokens).IsGreaterThan(0);
        var state = (await conversation.Control.GetConversationsAsync()).Single();
        await Assert.That(state.ExecutedSteps).IsEqualTo(2);
        await Assert.That(state.IsTerminal).IsTrue();
    }

    [Test]
    public async Task ProviderDelay_CancellationRollsBackQueuedToolAndCacheBeforeExplicitRetryAsync()
    {
        await using var app = await StartHealthyFaultHostAsync(config => config
            .AddConversation("delayed-tool", graph => graph
                .Step("call", step => step.WhenLatestUserEquals("run tool").CallsTool("weather", "{}", "call_delayed_fault")
                    .DelaysBy(10000).Complete()))
            .AddConversation("startup", graph => graph
                .Step("ready", step => step.WhenLatestUserEquals("startup").Responds("provider ready").Complete())));
        using var http = app.GetTestClient();
        var root = new LlmTestServerClient(http);
        // Initialize real routing, tokenization and public state serialization before measuring the pending reservation.
        // This unrelated short prompt cannot populate the target's long system-prefix cache.
        using (var startup = root.ForConversation("startup"))
        {
            using var startupWatchdog = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var ready = (await CreateSdk(startup.HttpClient).CompleteChatAsync([new UserChatMessage("startup")], cancellationToken: startupWatchdog.Token)).Value;
            await Assert.That(ready.Content.Single().Text).IsEqualTo("provider ready");
            await Assert.That((await startup.Control.GetConversationsAsync(startupWatchdog.Token)).Single().ExecutedSteps).IsEqualTo(1);
        }
        using var conversation = root.ForConversation("delayed-tool");
        var sdk = CreateSdk(conversation.HttpClient);
        var options = new ChatCompletionOptions { Tools = { ChatTool.CreateFunctionTool("weather") } };
        List<ChatMessage> messages = [new SystemChatMessage(LongCachePrefix()), new UserChatMessage("run tool")];
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(watchdog.Token);
        var pending = sdk.CompleteChatAsync(messages, options, cancellation.Token);
        try
        {
            while (true)
            {
                var observed = conversation.Control.GetConversationsAsync(watchdog.Token);
                if (await Task.WhenAny(pending, observed) == pending)
                {
                    await observed;
                    await pending;
                    throw new InvalidOperationException("The delayed SDK request completed before its busy reservation was observed.");
                }
                if ((await observed).Any(state => state.IsBusy)) { break; }
                await Task.Delay(10, watchdog.Token);
            }
            cancellation.Cancel();
            var cancelled = false;
            try { await pending; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelled = true; }
            await Assert.That(cancelled).IsTrue();
            while ((await conversation.Control.GetConversationsAsync(watchdog.Token)).Any(state => state.IsBusy))
            { await Task.Delay(10, watchdog.Token); }
            await Assert.That((await conversation.Control.GetConversationsAsync(watchdog.Token)).Count).IsEqualTo(0);
            // Retry the original input against the SAME graph and cache; no configure/reset can mask rollback.
            var recovered = (await sdk.CompleteChatAsync(messages, options, watchdog.Token)).Value;
            await Assert.That(recovered.ToolCalls.Single().Id).IsEqualTo("call_delayed_fault");
            var evidence = await conversation.Control.GetRequestsAsync(new() { Conversation = conversation.Id, Order = LlmTestServerRequestOrder.Ascending }, watchdog.Token);
            await Assert.That(evidence.Requests.Count).IsEqualTo(2);
            await Assert.That((await root.GetRequestsAsync(cancellationToken: watchdog.Token)).TotalObserved).IsEqualTo(3);
            await Assert.That(evidence.Requests[0].TransportState is LlmTestServerProviderHttpTraceTransportState.Cancelled
                or LlmTestServerProviderHttpTraceTransportState.Aborted).IsTrue();
            await Assert.That(evidence.Requests[0].ResponseToolCalls.Count).IsEqualTo(0);
            await Assert.That(evidence.Requests[^1].Usage!.CachedInputTokens).IsEqualTo(0);
            await Assert.That(evidence.Requests[^1].Usage!.CacheCreationInputTokens).IsGreaterThan(0);
            await Assert.That((await conversation.Control.GetConversationsAsync(watchdog.Token)).Single().ExecutedSteps).IsEqualTo(1);
            using var healthy = await http.GetAsync("/health", watchdog.Token);
            await Assert.That(healthy.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        finally
        {
            cancellation.Cancel();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(60)); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
    }

    [Test]
    public async Task InputFilter_RejectsToolResultWithoutAdvancingThenAcceptsCleanNullResultAsync()
    {
        var fixture = LlmTestServerWellKnownTools.Weather.NullResult(new() { Location = "Paris" }, "call_filtered_tool");
        await using var app = await StartHealthyFaultHostAsync(config => config.SimulateContentFilter("blocked_tool_result")
            .AddConversation("filtered-tool", graph => graph
                .Step("call", step => step.CallsTools(fixture.Call).Then("result"))
                .Step("result", step => step.WhenToolResult(fixture.Call.Name, fixture.Call.Id)
                    .WhenLatestJson("$", fixture.Result.Content).Responds("clean result accepted").Complete())));
        using var http = app.GetTestClient();
        using var conversation = new LlmTestServerClient(http).ForConversation("filtered-tool");
        var sdk = CreateSdk(conversation.HttpClient);
        var options = new ChatCompletionOptions
        {
            Tools = { ChatTool.CreateFunctionTool(fixture.Definition.Name, fixture.Definition.Description,
                BinaryData.FromString(fixture.Definition.ParametersJson)) },
        };
        List<ChatMessage> messages = [new SystemChatMessage(LongCachePrefix()), new UserChatMessage("weather")];
        var call = (await sdk.CompleteChatAsync(messages, options)).Value;
        messages.Add(new AssistantChatMessage(call));
        messages.Add(new ToolChatMessage(fixture.Call.Id, "blocked_tool_result"));
        ClientResultException? failure = null;
        try { await sdk.CompleteChatAsync(messages, options); }
        catch (ClientResultException exception) { failure = exception; }
        await Assert.That(failure?.Status).IsEqualTo(400);
        var filtered = await conversation.Control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(filtered.Requests[^1].ErrorCode).IsEqualTo("content_filter");
        await Assert.That(filtered.Requests[^1].Usage!.CacheCreationInputTokens).IsEqualTo(0);
        await Assert.That(filtered.Requests[^1].Messages.Single(message => message.Role == "tool").Content).IsEqualTo("blocked_tool_result");
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        messages[^1] = new ToolChatMessage(fixture.Call.Id, fixture.Result.Content);
        var accepted = (await sdk.CompleteChatAsync(messages, options)).Value;
        await Assert.That(accepted.Content.Single().Text).IsEqualTo("clean result accepted");
        var evidence = await conversation.Control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(evidence.Requests.Select(request => request.StatusCode)).IsEquivalentTo(new int?[] { 200, 400, 200 });
        await Assert.That(evidence.Requests[^1].Usage!.CachedInputTokens).IsGreaterThan(0);
        await Assert.That(evidence.Requests[^1].Messages.Single(message => message.Role == "tool").Content).IsEqualTo("null");
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
        using var healthy = await http.GetAsync("/health");
        await Assert.That(healthy.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static async Task<WebApplication> StartHealthyFaultHostAsync(Action<LlmTestServerConfigurationBuilder> configure)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddLlmTestServer(configure);
        builder.AddDefaultHealthChecks();
        var app = builder.Build();
        app.MapLlmTestServer();
        app.MapDefaultEndpoints();
        await app.StartAsync();
        return app;
    }

    private static string LongCachePrefix()
    {
        return string.Join(' ', Enumerable.Repeat("Retained system reference", 1200));
    }
}
