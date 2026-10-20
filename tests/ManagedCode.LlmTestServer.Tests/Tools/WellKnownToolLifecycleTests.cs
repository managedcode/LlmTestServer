using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Conversations;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using ManagedCode.LlmTestServer.Tools.WellKnown;

namespace ManagedCode.LlmTestServer.Tests.Tools;

public sealed partial class WellKnownToolIntegrationTests
{
    [Test]
    public async Task Preset_RepeatsThroughExistingConversationPolicyWithStableCallIdentityAsync()
    {
        var fixture = LlmTestServerWellKnownTools.CurrentTime.Success(new(),
            new() { CurrentTime = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero) }, "call_scripted_time");
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddConversation("repeat-preset", graph => graph
            .OnExhaustion(LlmTestServerConversationExhaustion.RepeatLastResponse)
            .Step("call", step => step.CallsTools(fixture.Call).Complete())));
        using var http = host.GetTestClient();
        var tools = Tools(fixture);
        using var first = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
        { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "time" } }, tools });
        first.EnsureSuccessStatusCode();
        var message = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("choices")[0].GetProperty("message");
        object[] messages = [new { role = "user", content = "time" }, message, new { role = "user", content = "time again" }];
        using var repeated = await http.PostAsJsonAsync("/openai/v1/chat/completions", new { model = "gpt-4.1-mini", messages, tools });
        repeated.EnsureSuccessStatusCode();
        var call = (await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("choices")[0].GetProperty("message").GetProperty("tool_calls")[0];
        await Assert.That(call.GetProperty("id").GetString()).IsEqualTo("call_scripted_time");
        await Assert.That(call.GetProperty("function").GetProperty("arguments").GetString()).IsEqualTo(fixture.Call.ArgumentsJson);
        var control = new LlmTestServerClient(http);
        await Assert.That((await control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        var journal = await control.GetRequestsAsync();
        await Assert.That(journal.Requests.Single(trace => trace.IsReplay).ResponseToolCalls.Single().Id).IsEqualTo("call_scripted_time");
    }

    [Test]
    public async Task Preset_OptionalDelayUsesExistingCancellationAndDoesNotConsumeTheCallAsync()
    {
        var fixture = LlmTestServerWellKnownTools.Shell.Success(new() { Command = "scripted-command" },
            new() { Stdout = "scripted output", ExitCode = 0 }, "call_delayed_shell", delayMilliseconds: 300);
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddConversation("delay-preset", graph => graph
            .Step("call", step => step.CallsTools(fixture.Call).DelaysBy(fixture.DelayMilliseconds).Complete())));
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        using var cancellation = new CancellationTokenSource();
        var tools = Tools(fixture);
        var pending = http.PostAsJsonAsync("/openai/v1/chat/completions", new
        { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "shell fixture" } }, tools }, cancellation.Token);
        var observedBusy = false;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await control.GetConversationsAsync()).Any(state => state.IsBusy)) { observedBusy = true; break; }
            if (pending.IsCompleted) { break; }
            await Task.Delay(1);
        }
        await Assert.That(observedBusy).IsTrue();
        cancellation.Cancel();
        var wasCancelled = false;
        try { using var response = await pending; }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { wasCancelled = true; }
        await Assert.That(wasCancelled).IsTrue();
        await Assert.That((await control.GetConversationsAsync()).Count).IsEqualTo(0);
        using var retry = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
        { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "shell fixture" } }, tools });
        retry.EnsureSuccessStatusCode();
        var call = (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("choices")[0].GetProperty("message").GetProperty("tool_calls")[0];
        await Assert.That(call.GetProperty("id").GetString()).IsEqualTo("call_delayed_shell");
        await Assert.That((await control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
    }
}
