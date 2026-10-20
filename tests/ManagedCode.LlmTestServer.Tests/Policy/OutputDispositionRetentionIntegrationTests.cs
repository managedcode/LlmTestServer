using System.Text.Json;
using global::Anthropic.Models.Messages;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Tests.Policy;

public sealed partial class OutputDispositionIntegrationTests
{
    [Test]
    [Arguments(2048)]
    [Arguments(4096)]
    public async Task NativePolicyMetadataRemainsValidWhilePublicDiagnosticBudgetMarksTruncationAsync(int retainedBytes)
    {
        var model = JsonSerializer.SerializeToElement(Model.ClaudeSonnet5_5).GetString()!;
        var explanation = new string('x', 4096);
        await using var host = await StartAsync(config => config.AddModel(model, LlmTestServerModelKind.Chat)
            .AddChatScenario("policy-retention", scenario => scenario.ForModel(model).Responds(_text)
                .WithOutputDisposition(new() { Kind = LlmTestServerOutputDispositionKind.Refused, Category = "cyber", Explanation = explanation })
                .Responds(_recovery)), traceBudgetBytes: retainedBytes);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var http = host.Client();
        var control = new LlmTestServerClient(http);
        using var sdk = AnthropicSdk(http);
        var native = await sdk.Messages.Create(AnthropicOptions(), deadline.Token);
        await Assert.That(native.StopReason?.Json.GetString()).IsEqualTo("refusal");
        await Assert.That(native.StopDetails!.Explanation).IsEqualTo(explanation);
        await Assert.That(((TextBlock)native.Content.Single().Value!).Text).IsEqualTo(_text);
        var trace = await CompletedAsync(control, 1);
        await Assert.That(trace.RetentionTruncated).IsTrue();
        await Assert.That(trace.RetainedByteCount <= retainedBytes).IsTrue();
        await Assert.That(trace.ResponsePreviewTruncated || trace.OutputDisposition?.Explanation != explanation).IsTrue();
        await Assert.That(trace.Usage!.InputTokens).IsEqualTo((int)native.Usage.InputTokens);
        await Assert.That(trace.Usage.OutputTokens).IsEqualTo((int)native.Usage.OutputTokens);
        await Assert.That(trace.OutputDisposition?.Explanation == explanation).IsFalse();
        var exported = await control.ExportRequestsAsync(cancellationToken: deadline.Token);
        await Assert.That(exported.Evidence.RetentionTruncated).IsTrue();
        await Assert.That(exported.Evidence.RetainedByteCount <= retainedBytes).IsTrue();
        await Assert.That(exported.Evidence.Requests.Single().RetentionTruncated).IsTrue();
        // Runtime evidence has its own existing budget; a smaller wire journal does not mutate the inference result.
        var events = (await control.GetAssertionsAsync(deadline.Token)).Events;
        await Assert.That(events.Single().OutputDisposition!.Explanation).IsEqualTo(explanation);
        var recovered = await sdk.Messages.Create(AnthropicOptions(), deadline.Token);
        await Assert.That(recovered.StopReason?.Json.GetString()).IsEqualTo("end_turn");
        await Assert.That(((TextBlock)recovered.Content.Single().Value!).Text).IsEqualTo(_recovery);
        await Assert.That((await control.GetRequestsAsync(cancellationToken: deadline.Token)).TotalObserved).IsEqualTo(2);
    }
}
