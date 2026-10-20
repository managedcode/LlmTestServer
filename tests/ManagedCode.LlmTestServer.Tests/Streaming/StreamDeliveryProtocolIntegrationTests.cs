using System.ClientModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Control;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Tests.Streaming;

public sealed partial class StreamDeliveryIntegrationTests
{
    private static async Task AssertProtocolDeliveryAsync(string model, string path, string marker,
        LlmTestServerStreamDeliveryMode mode, bool cancel,
        Func<HttpClient, Action, CancellationToken, Action<IDisposable>, Task<ObservedProtocolStream>> read,
        Func<HttpClient, CancellationToken, Action<IDisposable>, Task<string>> recover, bool afterAllText = false)
    {
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await StartTcpHostAsync(config => config.AddModel(model, LlmTestServerModelKind.Chat)
            .AddChatScenario("protocol-delivery", scenario => scenario.ForModel(model)
                .Responds("first second", "", "first ", "", "second").WithStreamDelivery(new()
                {
                    Mode = mode,
                    AfterContentFrames = afterAllText ? 2 : 1,
                    DelayMilliseconds = mode == LlmTestServerStreamDeliveryMode.DelayThenContinue ? cancel ? 30000 : 160 : 0,
                }).Responds("protocol recovered")), firstRead.Task, path, marker);
        using var http = TcpClient(app);
        var sdkOwners = new List<IDisposable>();
        try
        {
            var control = new LlmTestServerClient(http);
            using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var afterFirst = new Stopwatch();
            var sawFirst = false;
            void FirstContent()
            {
                if (sawFirst) { return; }
                sawFirst = true;
                afterFirst.Start();
                if (cancel) { watchdog.CancelAfter(TimeSpan.FromMilliseconds(180)); }
                firstRead.TrySetResult();
            }
            ObservedProtocolStream observed;
            try { observed = await read(http, FirstContent, watchdog.Token, sdkOwners.Add); }
            finally { firstRead.TrySetResult(); }
            var full = mode == LlmTestServerStreamDeliveryMode.DelayThenContinue && !cancel;
            var allText = full || afterAllText;
            await Assert.That(sawFirst).IsTrue();
            await Assert.That(observed.Text).IsEqualTo(allText ? "first second" : "first ");
            await Assert.That(observed.Terminal).IsEqualTo(full);
            if (full || mode == LlmTestServerStreamDeliveryMode.EarlyCleanEnd)
            { await Assert.That(observed.Failure).IsNull(); }
            else { await Assert.That(observed.Failure is not null).IsTrue(); }
            if (cancel)
            {
                await Assert.That(watchdog.IsCancellationRequested).IsTrue();
                await Assert.That(afterFirst.Elapsed < TimeSpan.FromSeconds(5)).IsTrue();
            }
            else { await Assert.That(watchdog.IsCancellationRequested).IsFalse(); }
            // The clock starts before acknowledging the server flush; allow only 5 ms for timer resolution.
            if (full) { await Assert.That(afterFirst.ElapsedMilliseconds).IsGreaterThanOrEqualTo(160 - 5); }
            var trace = await CompletedTraceAsync(control, 1);
            await Assert.That(trace.Path).IsEqualTo(path);
            await Assert.That(trace.StatusCode).IsEqualTo(200);
            await Assert.That(trace.StreamDelivery!.Mode).IsEqualTo(mode);
            await Assert.That(trace.StreamDeliveryApplied).IsTrue();
            await Assert.That(trace.EmittedContentFrames).IsEqualTo(allText ? 2 : 1);
            await Assert.That(trace.TerminalMarkerObserved == true).IsEqualTo(full);
            if (cancel)
            { await Assert.That(trace.TransportState is LlmTestServerProviderHttpTraceTransportState.Cancelled or LlmTestServerProviderHttpTraceTransportState.Aborted).IsTrue(); }
            else if (mode == LlmTestServerStreamDeliveryMode.AbortConnection)
            { await Assert.That(trace.TransportState == LlmTestServerProviderHttpTraceTransportState.Completed).IsFalse(); }
            else { await Assert.That(trace.TransportState).IsEqualTo(LlmTestServerProviderHttpTraceTransportState.Completed); }
            var verification = await control.VerifyAsync(new()
            { Requests = [new() { Query = new() { RequestId = trace.RequestId }, RequireStreamingTerminalMarker = true }] });
            await Assert.That(verification.Passed).IsEqualTo(full);
            if (!full)
            { await Assert.That(verification.Failures.Any(failure => failure.Code == "stream.terminal" && failure.RequestIds.Contains(trace.RequestId))).IsTrue(); }
            await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(1);
            using var healthy = await http.GetAsync("/health");
            await Assert.That(healthy.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var recoveryWatchdog = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await Assert.That(await recover(http, recoveryWatchdog.Token, sdkOwners.Add)).IsEqualTo("protocol recovered");
            await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
            await Assert.That((await control.GetRequestsAsync()).TotalObserved).IsEqualTo(2);
        }
        finally
        {
            foreach (var sdk in sdkOwners) { sdk.Dispose(); }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Configure_EmptyOrUnreachableNonemptyTextBoundaryIsAtomicAsync(bool empty)
    {
        await using var app = await StartTcpHostAsync(config => config.AddChatScenario("preserved", scenario => scenario.Responds("still available")));
        using var http = TcpClient(app);
        var control = new LlmTestServerClient(http);
        var validBase = new LlmTestServerConfigurationBuilder().Build();
        using var rejected = await http.PostAsJsonAsync(LlmTestServerControlRoutes.Configure, validBase with
        {
            ChatScenarios = [new() { Id = "unreachable", Responses = [new()
            {
                Content = empty ? string.Empty : "first second",
                StreamChunks = empty ? ["", ""] : ["first ", "", "second"],
                StreamDelivery = new() { Mode = LlmTestServerStreamDeliveryMode.EarlyCleanEnd, AfterContentFrames = empty ? 1 : 3 }
            }] }]
        });
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        var response = (await Sdk(http).CompleteChatAsync([new global::OpenAI.Chat.UserChatMessage("original")])).Value;
        await Assert.That(response.Content.Single().Text).IsEqualTo("still available");
    }

    private static bool ProtocolStreamException(Exception exception)
    {
        return exception is IOException or HttpRequestException
        or ClientResultException or OperationCanceledException or global::Anthropic.Exceptions.AnthropicException or Google.GenAI.ApiException;
    }

    private sealed record ObservedProtocolStream(string Text, bool Terminal, Exception? Failure);
}
