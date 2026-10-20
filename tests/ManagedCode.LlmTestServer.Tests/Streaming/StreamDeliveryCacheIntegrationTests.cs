using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI.Chat;

namespace ManagedCode.LlmTestServer.Tests.Streaming;

public sealed partial class StreamDeliveryIntegrationTests
{
    [Test]
    public async Task OfficialSdk_PostHeaderEarlyEndCommitsCacheAndExplicitNextAttemptReportsRealCachedUsageAsync()
    {
        await using var app = await StartTcpHostAsync(config => config.AddChatScenario("stream-cache", scenario => scenario
            .Responds("first second", "first ", "second").WithStreamDelivery(new()
            { Mode = LlmTestServerStreamDeliveryMode.EarlyCleanEnd, AfterContentFrames = 1 }).Responds("cached recovery")));
        using var http = TcpClient(app);
        var control = new LlmTestServerClient(http);
        List<ChatMessage> messages = [new SystemChatMessage(string.Join(" ", Enumerable.Repeat("shared cache reference", 1600))), new UserChatMessage("same request")];
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var partial = await ReadSdkAsync(Sdk(http), messages, null, deadline.Token);
        await Assert.That(partial.Text).IsEqualTo("first ");
        await Assert.That(partial.Finish).IsNull();
        await Assert.That(partial.Failure).IsNull();
        var initial = await CompletedTraceAsync(control, 1);
        await Assert.That(initial.Usage!.InputTokens).IsGreaterThanOrEqualTo(1600);
        await Assert.That(initial.Usage.CachedInputTokens).IsEqualTo(0);
        await Assert.That(initial.StreamDeliveryApplied).IsTrue();
        await Assert.That(initial.TerminalMarkerObserved == true).IsFalse();
        var recovered = (await Sdk(http).CompleteChatAsync(messages)).Value;
        await Assert.That(recovered.Content.Single().Text).IsEqualTo("cached recovery");
        await Assert.That(recovered.Usage.InputTokenDetails.CachedTokenCount).IsGreaterThan(0);
        var evidence = await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(evidence.Requests.Count).IsEqualTo(2);
        await Assert.That(evidence.Requests[^1].Usage!.CachedInputTokens).IsGreaterThan(0);
        await Assert.That(recovered.Usage.InputTokenDetails.CachedTokenCount).IsEqualTo(evidence.Requests[^1].Usage!.CachedInputTokens);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
    }
}
