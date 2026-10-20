using System.Text;
using System.Text.Json;
using global::Anthropic;
using global::Anthropic.Core;
using global::Anthropic.Models.Messages;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Tests.Policy;

public sealed partial class OutputDispositionIntegrationTests
{
    [Test]
    [Arguments(false, false, false)]
    [Arguments(true, false, false)]
    [Arguments(false, true, false)]
    [Arguments(true, true, false)]
    [Arguments(false, false, true)]
    [Arguments(true, false, true)]
    public async Task OfficialAnthropic_StrictRefusalHasNativeStopDetailsAndRecoveryAsync(bool streaming, bool empty, bool metadata)
    {
        var model = JsonSerializer.SerializeToElement(Model.ClaudeSonnet5_5).GetString()!;
        await using var host = await StartAsync(config => ConfigureQueue(config, model, LlmTestServerOutputDispositionKind.Refused, empty, metadata));
        using var http = host.Client();
        var control = new LlmTestServerClient(http);
        using var sdk = AnthropicSdk(http);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var text = new StringBuilder(); RefusalStopDetails? details = null; string? reason = null; var stopped = false;
        if (streaming)
        {
            await foreach (var update in sdk.Messages.CreateStreaming(AnthropicOptions(), deadline.Token))
            {
                if (update.Value is RawMessageStartEvent start)
                { await Assert.That(start.Message.StopReason).IsNull(); await Assert.That(start.Message.StopDetails).IsNull(); }
                if (update.Value is RawContentBlockDeltaEvent block && block.Delta.Value is TextDelta delta) { text.Append(delta.Text); }
                if (update.Value is RawMessageDeltaEvent ending) { reason = ending.Delta.StopReason?.Json.GetString(); details = ending.Delta.StopDetails; }
                if (update.Value is RawMessageStopEvent) { stopped = true; }
            }
            await Assert.That(stopped).IsTrue();
        }
        else
        {
            var response = await sdk.Messages.Create(AnthropicOptions(), deadline.Token);
            foreach (var block in response.Content) { text.Append(((TextBlock)block.Value!).Text); }
            reason = response.StopReason?.Json.GetString(); details = response.StopDetails;
        }
        await Assert.That(text.ToString()).IsEqualTo(empty ? string.Empty : _text);
        await Assert.That(reason).IsEqualTo("refusal");
        await Assert.That(details).IsNotNull();
        await Assert.That(details!.Type.GetString()).IsEqualTo("refusal");
        await Assert.That(details.Category?.Json.GetString()).IsEqualTo(metadata ? "cyber" : null);
        await Assert.That(details.Explanation).IsEqualTo(metadata ? "Explicit fixture policy" : null);
        await EvidenceAsync(control, LlmTestServerOutputDispositionKind.Refused, streaming, metadata);
        var recovered = await sdk.Messages.Create(AnthropicOptions(), deadline.Token);
        await Assert.That(((TextBlock)recovered.Content.Single().Value!).Text).IsEqualTo(_recovery);
        await Assert.That(recovered.StopReason?.Json.GetString()).IsEqualTo("end_turn");
        await Assert.That(recovered.StopDetails).IsNull();
        await RecoveredAsync(control);
    }

    [Test]
    public async Task OfficialAnthropic_PrefillLeavesPolicyFixtureUnconsumedAndWarmsCacheAsync()
    {
        var model = JsonSerializer.SerializeToElement(Model.ClaudeSonnet5_5).GetString()!;
        await using var host = await StartAsync(config => ConfigureQueue(config, model, LlmTestServerOutputDispositionKind.Refused));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        using var sdk = AnthropicSdk(http);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var options = AnthropicOptions() with
        { MaxTokens = 0, CacheControl = new(), System = string.Join(" ", Enumerable.Repeat("shared reference", 1800)) };
        var prefill = await sdk.Messages.Create(options, deadline.Token);
        await Assert.That(prefill.Content).IsEmpty();
        await Assert.That(prefill.StopReason?.Json.GetString()).IsEqualTo("max_tokens");
        await Assert.That(prefill.Usage.CacheCreationInputTokens > 0).IsTrue();
        var trace = await CompletedAsync(control, 1);
        await Assert.That(trace.OutputDisposition).IsNull();
        await Assert.That((await control.GetConversationsAsync()).Count).IsEqualTo(0);
        var refused = await sdk.Messages.Create(options with { MaxTokens = 256 }, deadline.Token);
        await Assert.That(refused.StopReason?.Json.GetString()).IsEqualTo("refusal");
        await Assert.That(refused.Usage.CacheReadInputTokens > 0).IsTrue();
        await Assert.That((await CompletedAsync(control, 2)).OutputDisposition!.Kind).IsEqualTo(LlmTestServerOutputDispositionKind.Refused);
        var next = await sdk.Messages.Create(options with { MaxTokens = 256 }, deadline.Token);
        await Assert.That(((TextBlock)next.Content.Single().Value!).Text).IsEqualTo(_recovery);
    }

    private static AnthropicClient AnthropicSdk(HttpClient http)
    {
        return new(new ClientOptions
        { HttpClient = http, BaseUrl = new Uri(http.BaseAddress!, "/anthropic").ToString(), ApiKey = _key, MaxRetries = 0, ResponseValidation = true });
    }

    private static MessageCreateParams AnthropicOptions()
    {
        return new() { Model = Model.ClaudeSonnet5_5, MaxTokens = 256, Messages = [new() { Role = Role.User, Content = "policy" }] };
    }
}
