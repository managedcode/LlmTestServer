using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Net;
using System.Text;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using OpenAI.Chat;

namespace ManagedCode.LlmTestServer.Tests.Streaming;

public sealed partial class StreamDeliveryIntegrationTests
{
    [Test]
    [Arguments(LlmTestServerStreamDeliveryMode.EarlyCleanEnd)]
    [Arguments(LlmTestServerStreamDeliveryMode.AbortConnection)]
    public async Task OfficialSdk_PartialDeliveryHasNoFinishAndFailsTerminalVerificationThenExplicitRetryRecoversAsync(LlmTestServerStreamDeliveryMode mode)
    {
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await StartTcpHostAsync(config => config.AddChatScenario("broken-stream", scenario => scenario
            .Responds("first second", "first ", "second").WithStreamDelivery(new() { Mode = mode, AfterContentFrames = 1 })
            .Responds("explicit recovery")), firstRead.Task);
        using var http = TcpClient(app);
        var control = new LlmTestServerClient(http);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var partial = await ReadSdkAsync(Sdk(http), [new UserChatMessage("stream")], firstRead, deadline.Token);
        await Assert.That(partial.Text).IsEqualTo("first ");
        await Assert.That(partial.Finish).IsNull();
        await Assert.That(firstRead.Task.IsCompletedSuccessfully).IsTrue();
        await Assert.That(deadline.IsCancellationRequested).IsFalse();
        if (mode == LlmTestServerStreamDeliveryMode.AbortConnection)
        { await Assert.That(partial.Failure is IOException or HttpRequestException or ClientResultException).IsTrue(); }
        else { await Assert.That(partial.Failure).IsNull(); }
        var trace = await CompletedTraceAsync(control, 1);
        await Assert.That(trace.StatusCode).IsEqualTo(200);
        await Assert.That(trace.TerminalMarkerObserved == true).IsFalse();
        await Assert.That(trace.StreamDelivery!.Mode).IsEqualTo(mode);
        await Assert.That(trace.StreamDeliveryApplied).IsTrue();
        await Assert.That(trace.EmittedContentFrames).IsEqualTo(1);
        await Assert.That(trace.ResponsePreview).Contains("first ");
        await Assert.That(trace.ResponsePreview!.Contains("[DONE]", StringComparison.Ordinal)).IsFalse();
        if (mode == LlmTestServerStreamDeliveryMode.EarlyCleanEnd)
        { await Assert.That(trace.TransportState).IsEqualTo(LlmTestServerProviderHttpTraceTransportState.Completed); }
        else
        { await Assert.That(trace.TransportState == LlmTestServerProviderHttpTraceTransportState.Completed).IsFalse(); }
        var verification = await control.VerifyAsync(new()
        { Requests = [new() { Query = new() { RequestId = trace.RequestId }, RequireStreamingTerminalMarker = true }] });
        await Assert.That(verification.Passed).IsFalse();
        await Assert.That(verification.Failures.Any(failure => failure.Code == "stream.terminal" && failure.RequestIds.Contains(trace.RequestId))).IsTrue();
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(1);
        using var healthy = await http.GetAsync("/health");
        await Assert.That(healthy.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var recovered = (await Sdk(http).CompleteChatAsync([new UserChatMessage("explicit retry")])).Value;
        await Assert.That(recovered.Content.Single().Text).IsEqualTo("explicit recovery");
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
        await Assert.That((await control.GetRequestsAsync()).TotalObserved).IsEqualTo(2);
    }

    [Test]
    public async Task OfficialSdk_PostFrameDelayContinuesWithFullTextAndTerminalAsync()
    {
        const int delayMilliseconds = 160;
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await StartTcpHostAsync(config => config.AddChatScenario("delayed-stream", scenario => scenario
            .Responds("first second", "first ", "second").WithStreamDelivery(new()
            { Mode = LlmTestServerStreamDeliveryMode.DelayThenContinue, AfterContentFrames = 1, DelayMilliseconds = delayMilliseconds })), firstRead.Task);
        using var http = TcpClient(app);
        var control = new LlmTestServerClient(http);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var afterFirst = new Stopwatch();
        var completed = await ReadSdkAsync(Sdk(http), [new UserChatMessage("stream")], firstRead, deadline.Token, afterFirst.Start);
        await Assert.That(completed.Failure).IsNull();
        await Assert.That(completed.Text).IsEqualTo("first second");
        await Assert.That(completed.Finish).IsEqualTo(ChatFinishReason.Stop);
        await Assert.That(afterFirst.ElapsedMilliseconds).IsGreaterThanOrEqualTo(delayMilliseconds / 2);
        var trace = await CompletedTraceAsync(control, 1);
        await Assert.That(trace.TerminalMarkerObserved).IsTrue();
        await Assert.That(trace.EmittedContentFrames).IsEqualTo(2);
        await Assert.That(trace.StreamDeliveryApplied).IsTrue();
        await Assert.That(trace.TransportState).IsEqualTo(LlmTestServerProviderHttpTraceTransportState.Completed);
        await Assert.That((await control.VerifyAsync(new() { Requests = [new() { RequireStreamingTerminalMarker = true }] })).Passed).IsTrue();
    }

    [Test]
    public async Task OfficialSdk_PostFrameStallCancellationKeepsCommittedInferenceThenExplicitRetryRecoversAsync()
    {
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await StartTcpHostAsync(config => config.AddChatScenario("stalled-stream", scenario => scenario
            .Responds("first second", "first ", "second").WithStreamDelivery(new()
            { Mode = LlmTestServerStreamDeliveryMode.DelayThenContinue, AfterContentFrames = 1, DelayMilliseconds = 30000 })
            .Responds("after cancellation")), firstRead.Task);
        using var http = TcpClient(app);
        var control = new LlmTestServerClient(http);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var afterFirst = new Stopwatch();
        var partial = await ReadSdkAsync(Sdk(http), [new UserChatMessage("stall")], firstRead, deadline.Token,
            () => { afterFirst.Start(); deadline.CancelAfter(TimeSpan.FromMilliseconds(180)); });
        await Assert.That(partial.Text).IsEqualTo("first ");
        await Assert.That(partial.Finish).IsNull();
        await Assert.That(partial.Failure is OperationCanceledException || partial.Failure is ClientResultException && deadline.IsCancellationRequested).IsTrue();
        await Assert.That(deadline.IsCancellationRequested).IsTrue();
        await Assert.That(afterFirst.Elapsed < TimeSpan.FromSeconds(5)).IsTrue();
        var trace = await CompletedTraceAsync(control, 1);
        await Assert.That(trace.TerminalMarkerObserved == true).IsFalse();
        await Assert.That(trace.EmittedContentFrames).IsEqualTo(1);
        await Assert.That(trace.TransportState is LlmTestServerProviderHttpTraceTransportState.Cancelled or LlmTestServerProviderHttpTraceTransportState.Aborted).IsTrue();
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(1);
        using var healthy = await http.GetAsync("/health");
        await Assert.That(healthy.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var recovered = (await Sdk(http).CompleteChatAsync([new UserChatMessage("retry")])).Value;
        await Assert.That(recovered.Content.Single().Text).IsEqualTo("after cancellation");
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
    }

    private static ChatClient Sdk(HttpClient http, string provider = "/openai/v1")
    {
        return new(LlmTestServerKnownModelIds.Gpt41Mini,
        new ApiKeyCredential("stream-delivery-key"), new OpenAIClientOptions
        { Endpoint = new Uri(http.BaseAddress!, provider), Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) });
    }

    private static async Task<ObservedStream> ReadSdkAsync(ChatClient sdk, IEnumerable<ChatMessage> messages,
        TaskCompletionSource? firstRead, CancellationToken cancellationToken, Action? onFirst = null)
    {
        var text = new StringBuilder();
        ChatFinishReason? finish = null;
        Exception? failure = null;
        var observed = false;
        try
        {
            await foreach (var update in sdk.CompleteChatStreamingAsync(messages, cancellationToken: cancellationToken))
            {
                foreach (var part in update.ContentUpdate)
                {
                    text.Append(part.Text);
                    if (!observed && !string.IsNullOrEmpty(part.Text))
                    {
                        observed = true;
                        onFirst?.Invoke();
                        firstRead?.TrySetResult();
                    }
                }
                if (update.FinishReason is { } reason) { finish = reason; }
            }
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or ClientResultException or OperationCanceledException)
        { failure = exception; }
        finally { firstRead?.TrySetResult(); }
        return new(text.ToString(), finish, failure);
    }

    private static async Task<LlmTestServerProviderHttpTrace> CompletedTraceAsync(LlmTestServerClient control, int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var snapshot = await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending }, deadline.Token);
            if (snapshot.Requests.Count == count && snapshot.Requests[^1].IsCompleted) { return snapshot.Requests[^1]; }
            deadline.Token.ThrowIfCancellationRequested();
        }
    }

    private static async Task<WebApplication> StartTcpHostAsync(Action<LlmTestServerConfigurationBuilder> configure, Task? firstRead = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddLlmTestServer(configure);
        builder.AddDefaultHealthChecks();
        var app = builder.Build();
        if (firstRead is not null)
        {
            var gated = 0;
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/openai/v1/chat/completions" && Interlocked.CompareExchange(ref gated, 1, 0) == 0)
                {
                    var original = context.Response.Body;
                    await using var barrier = new FirstFlushGateStream(original, firstRead);
                    context.Response.Body = barrier;
                    try { await next(context); }
                    finally { context.Response.Body = original; }
                }
                else { await next(context); }
            });
        }
        app.MapLlmTestServer();
        app.MapDefaultEndpoints();
        await app.StartAsync();
        return app;
    }

    private static HttpClient TcpClient(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(15) };
    }

    private sealed record ObservedStream(string Text, ChatFinishReason? Finish, Exception? Failure);

    // Real TCP backpressure: bytes are forwarded unchanged, and only the first flush waits for the SDK's acknowledgement.
    private sealed class FirstFlushGateStream(Stream inner, Task firstRead) : Stream
    {
        private int _gated;
        private long _written;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush()
        {
            inner.Flush();
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await inner.FlushAsync(cancellationToken);
            if (Interlocked.Read(ref _written) > 0 && Interlocked.CompareExchange(ref _gated, 1, 0) == 0)
            { await firstRead.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken); }
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            return inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        { inner.Write(buffer, offset, count); Interlocked.Add(ref _written, count); }
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        { await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken); Interlocked.Add(ref _written, count); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { await inner.WriteAsync(buffer, cancellationToken); Interlocked.Add(ref _written, buffer.Length); }
    }
}
