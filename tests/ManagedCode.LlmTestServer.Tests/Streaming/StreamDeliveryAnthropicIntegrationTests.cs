using System.Text;
using System.Text.Json;
using global::Anthropic;
using global::Anthropic.Core;
using global::Anthropic.Models.Messages;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Tests.Streaming;

public sealed partial class StreamDeliveryIntegrationTests
{
    [Test]
    [Arguments(LlmTestServerStreamDeliveryMode.EarlyCleanEnd, false, false)]
    [Arguments(LlmTestServerStreamDeliveryMode.EarlyCleanEnd, false, true)]
    [Arguments(LlmTestServerStreamDeliveryMode.AbortConnection, false, false)]
    [Arguments(LlmTestServerStreamDeliveryMode.DelayThenContinue, true, false)]
    [Arguments(LlmTestServerStreamDeliveryMode.DelayThenContinue, false, false)]
    public async Task OfficialAnthropicSdk_StrictTypedTextEventsExposeDeliveryFaultsAndRecoverAsync(LlmTestServerStreamDeliveryMode mode, bool cancel, bool afterAllText)
    {
        var model = JsonSerializer.SerializeToElement(Model.ClaudeSonnet5_5).GetString()!;
        await AssertProtocolDeliveryAsync(model, "/anthropic/v1/messages", "first ", mode, cancel,
            async (http, first, token, keepAlive) =>
            {
                var sdk = AnthropicSdk(http);
                keepAlive(sdk);
                var text = new StringBuilder();
                var terminal = false;
                Exception? failure = null;
                try
                {
                    await foreach (var update in sdk.Messages.CreateStreaming(AnthropicTextOptions(), token))
                    {
                        if (update.Value is RawContentBlockDeltaEvent block && block.Delta.Value is TextDelta delta)
                        {
                            text.Append(delta.Text);
                            if (!string.IsNullOrEmpty(delta.Text)) { first(); }
                        }
                        if (update.Value is RawMessageStopEvent) { terminal = true; }
                    }
                }
                catch (Exception exception) when (ProtocolStreamException(exception)) { failure = exception; }
                return new(text.ToString(), terminal, failure);
            },
            async (http, token, keepAlive) =>
            {
                var sdk = AnthropicSdk(http);
                keepAlive(sdk);
                return ((TextBlock)(await sdk.Messages.Create(AnthropicTextOptions(), token)).Content.Single().Value!).Text;
            }, afterAllText);
    }

    [Test]
    public async Task OfficialAnthropicSdk_ZeroTokenPrefillWarmsCacheWithoutConsumingDeliveryFixtureAsync()
    {
        var model = JsonSerializer.SerializeToElement(Model.ClaudeSonnet5_5).GetString()!;
        await using var app = await StartTcpHostAsync(config => config.AddModel(model, LlmTestServerModelKind.Chat)
            .AddChatScenario("anthropic-prefill", scenario => scenario.ForModel(model)
                .Responds("first second", "first ", "second").WithStreamDelivery(new()
                { Mode = LlmTestServerStreamDeliveryMode.EarlyCleanEnd }).Responds("prefill recovery")));
        using var http = TcpClient(app);
        var control = new LlmTestServerClient(http);
        using var sdk = AnthropicSdk(http);
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var options = AnthropicTextOptions() with
        {
            MaxTokens = 0,
            CacheControl = new(),
            System = string.Join(" ", Enumerable.Repeat("shared prefill reference", 1600)),
        };
        var prefillStopped = false;
        var prefillText = new StringBuilder();
        long cacheWritten = 0;
        await foreach (var update in sdk.Messages.CreateStreaming(options, watchdog.Token))
        {
            if (update.Value is RawMessageStartEvent start) { cacheWritten = start.Message.Usage.CacheCreationInputTokens ?? 0; }
            if (update.Value is RawContentBlockDeltaEvent block && block.Delta.Value is TextDelta delta) { prefillText.Append(delta.Text); }
            if (update.Value is RawMessageStopEvent) { prefillStopped = true; }
        }
        await Assert.That(prefillStopped).IsTrue();
        await Assert.That(prefillText.ToString()).IsEqualTo(string.Empty);
        await Assert.That(cacheWritten).IsGreaterThan(0);
        var prefill = await CompletedTraceAsync(control, 1);
        await Assert.That(prefill.TerminalMarkerObserved).IsTrue();
        await Assert.That(prefill.StreamDelivery).IsNull();
        await Assert.That(prefill.StreamDeliveryApplied).IsFalse();
        await Assert.That(prefill.EmittedContentFrames).IsEqualTo(0);
        await Assert.That(prefill.Usage!.InputTokens).IsGreaterThanOrEqualTo(1600);
        await Assert.That((long)prefill.Usage.CacheCreationInputTokens).IsEqualTo(cacheWritten);
        await Assert.That((await control.GetConversationsAsync()).Count).IsEqualTo(0);
        var generated = new StringBuilder();
        var generationStopped = false;
        long cacheRead = 0;
        await foreach (var update in sdk.Messages.CreateStreaming(options with { MaxTokens = 256 }, watchdog.Token))
        {
            if (update.Value is RawMessageStartEvent start) { cacheRead = start.Message.Usage.CacheReadInputTokens ?? 0; }
            if (update.Value is RawContentBlockDeltaEvent block && block.Delta.Value is TextDelta delta) { generated.Append(delta.Text); }
            if (update.Value is RawMessageStopEvent) { generationStopped = true; }
        }
        await Assert.That(generated.ToString()).IsEqualTo("first ");
        await Assert.That(generationStopped).IsFalse();
        await Assert.That(cacheRead).IsGreaterThan(0);
        var fault = await CompletedTraceAsync(control, 2);
        await Assert.That(fault.StreamDeliveryApplied).IsTrue();
        await Assert.That(fault.StreamDelivery!.Mode).IsEqualTo(LlmTestServerStreamDeliveryMode.EarlyCleanEnd);
        await Assert.That(fault.EmittedContentFrames).IsEqualTo(1);
        await Assert.That(fault.TerminalMarkerObserved == true).IsFalse();
        var recovered = await sdk.Messages.Create(options with { MaxTokens = 256 }, watchdog.Token);
        await Assert.That(((TextBlock)recovered.Content.Single().Value!).Text).IsEqualTo("prefill recovery");
        await Assert.That((await control.GetRequestsAsync()).TotalObserved).IsEqualTo(3);
    }

    private static AnthropicClient AnthropicSdk(HttpClient http)
    {
        return new(new ClientOptions
        { HttpClient = http, BaseUrl = new Uri(http.BaseAddress!, "/anthropic").ToString(), ApiKey = "anthropic-delivery-key", MaxRetries = 0, ResponseValidation = true });
    }

    private static MessageCreateParams AnthropicTextOptions()
    {
        return new()
        { Model = Model.ClaudeSonnet5_5, MaxTokens = 256, Messages = [new() { Role = Role.User, Content = "stream" }] };
    }
}
