using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using ManagedCode.LlmTestServer.Tools.WellKnown;
using Microsoft.Extensions.AI;

namespace ManagedCode.LlmTestServer.Tests.Tools;

public sealed partial class WellKnownToolFaultIntegrationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task HostedSdkFunctionLoop_ReturnsExplicitNullThroughTypedPresetAsync(bool streaming)
    {
        var fixture = LlmTestServerWellKnownTools.WebSearch.NullResult(new() { Query = "scripted" }, "call_null_loop");
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddConversation("null-loop", graph => graph
            .Step("call", step => step.CallsTools(fixture.Call).Then("result"))
            .Step("result", step => step.WhenToolResult(fixture.Call.Name, fixture.Call.Id)
                .WhenLatestJson("$", fixture.Result.Content).Responds("null observed").Complete())));
        using var http = host.GetTestClient();
        using var conversation = new LlmTestServerClient(http).ForConversation("null-loop");
        using var client = new FunctionInvokingChatClient(CreateSdk(conversation.HttpClient).AsIChatClient());
        var invocations = 0;
        string? observedQuery = null;
        var observedLimit = 0;
        object? Search(string query, int limit)
        {
            invocations++;
            observedQuery = query;
            observedLimit = limit;
            return null;
        }
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(Search, fixture.Definition.Name)] };
        var answer = streaming
            ? await client.GetStreamingResponseAsync("lookup", options).ToChatResponseAsync()
            : await client.GetResponseAsync("lookup", options);
        await Assert.That(answer.Text).IsEqualTo("null observed");
        await Assert.That(invocations).IsEqualTo(1);
        await Assert.That(observedQuery).IsEqualTo("scripted");
        await Assert.That(observedLimit).IsEqualTo(5);
        var evidence = await conversation.Control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(evidence.Requests.Count).IsEqualTo(2);
        await Assert.That(evidence.Requests[^1].Messages.Single(message => message.Role == "tool").Content).IsEqualTo("null");
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(0, true)]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(2, false)]
    [Arguments(2, true)]
    public async Task HostedSdkFunctionLoop_RecoversFromActualControlledExceptionsWithoutProviderHttpErrorsAsync(int kind, bool streaming)
    {
        var marker = kind switch { 0 => "fixture_tool_timeout", 1 => "fixture_tool_internal_crash", _ => "fixture_tool_content_filtered" };
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddConversation("throwing-tool", graph => graph
            .Step("call", step => step.CallsTool("controlled_tool", "{}", "call_throwing_tool").Then("recover"))
            .Step("recover", step => step.WhenToolResult("controlled_tool", "call_throwing_tool")
                .WhenLatestTextContains(marker).Responds("caller failure recovered").Complete())));
        using var http = host.GetTestClient();
        using var conversation = new LlmTestServerClient(http).ForConversation("throwing-tool");
        using var client = new FunctionInvokingChatClient(CreateSdk(conversation.HttpClient).AsIChatClient())
        { IncludeDetailedErrors = true };
        var invocations = 0;
        string Invoke()
        {
            invocations++;
            throw kind switch
            {
                0 => new TimeoutException(marker),
                1 => new InvalidOperationException(marker),
                _ => new UnauthorizedAccessException(marker),
            };
        }
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(Invoke, "controlled_tool")] };
        var answer = streaming
            ? await client.GetStreamingResponseAsync("run controlled tool", options).ToChatResponseAsync()
            : await client.GetResponseAsync("run controlled tool", options);
        await Assert.That(answer.Text).IsEqualTo("caller failure recovered");
        await Assert.That(invocations).IsEqualTo(1);
        var evidence = await conversation.Control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(evidence.Requests.Count).IsEqualTo(2);
        await Assert.That(evidence.Requests.All(request => request.StatusCode == 200 && request.ErrorCode is null)).IsTrue();
        var observed = evidence.Requests[^1].Messages.Single(message => message.Role == "tool");
        await Assert.That(observed.ToolCallId).IsEqualTo("call_throwing_tool");
        await Assert.That(observed.Content).Contains(marker);
        var state = (await conversation.Control.GetConversationsAsync()).Single();
        await Assert.That(state.ExecutedSteps).IsEqualTo(2);
        await Assert.That(state.IsTerminal).IsTrue();
    }

    [Test]
    public async Task HostedSdkFunctionCancellation_OmitsResultAndAcceptsExplicitLaterRecoveryAsync()
    {
        const string callId = "call_cancellable_tool";
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddConversation("cancellable-tool", graph => graph
            .Step("call", step => step.CallsTool("cancellable_tool", "{}", callId).Then("recover"))
            .Step("recover", step => step.WhenToolResult("cancellable_tool", callId)
                .WhenLatestJson("$.error.code", "\"fixture_tool_timeout\"").Responds("timeout recovered").Complete())));
        using var http = host.GetTestClient();
        using var conversation = new LlmTestServerClient(http).ForConversation("cancellable-tool");
        conversation.HttpClient.DefaultRequestHeaders.Authorization = new("Bearer", "tool-fault-key");
        using var client = new FunctionInvokingChatClient(CreateSdk(conversation.HttpClient).AsIChatClient());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;
        async Task<string> Invoke(CancellationToken cancellationToken)
        {
            invocations++;
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "unreachable";
        }
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(Invoke, "cancellable_tool")] };
        using var cancellation = new CancellationTokenSource();
        var pending = client.GetResponseAsync("run cancellable tool", options, cancellation.Token);
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { cancellation.Cancel(); }
        var cancelled = false;
        try { await pending; }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelled = true; }
        await Assert.That(cancelled).IsTrue();
        await Assert.That(invocations).IsEqualTo(1);
        var before = await conversation.Control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(before.Requests.Count).IsEqualTo(1);
        await Assert.That(before.Requests.Single().Messages.Any(message => message.Role == "tool")).IsFalse();
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        using var responseBody = JsonDocument.Parse(before.Requests.Single().ResponsePreview!);
        var assistant = responseBody.RootElement.GetProperty("choices")[0].GetProperty("message").Clone();
        var toolError = LlmTestServerWellKnownTools.Shell.CreateError(new()
        { Code = "fixture_tool_timeout", Message = "Controlled invocation was cancelled", Retryable = true }, callId);
        using var recovered = await conversation.HttpClient.PostAsJsonAsync("/openai/v1/chat/completions", new
        {
            model = LlmTestServerKnownModelIds.Gpt41Mini,
            tools = new[] { new { type = "function", function = new { name = "cancellable_tool", parameters = new { type = "object" } } } },
            messages = new object[] { new { role = "user", content = "run cancellable tool" }, assistant,
                new { role = "tool", tool_call_id = callId, content = toolError.Content } },
        });
        await Assert.That(recovered.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var payload = await recovered.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()).IsEqualTo("timeout recovered");
        var after = await conversation.Control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(after.Requests[^1].Messages.Single(message => message.Role == "tool").Content).IsEqualTo(toolError.Content);
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
    }
}
