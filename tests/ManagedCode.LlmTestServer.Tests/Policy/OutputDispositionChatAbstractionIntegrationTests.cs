using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using Microsoft.Extensions.AI;

namespace ManagedCode.LlmTestServer.Tests.Policy;

public sealed partial class OutputDispositionIntegrationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MicrosoftChatAbstraction_RefusalIsErrorContentAndAssistantHistoryRoundTripsAsync(bool streaming)
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, LlmTestServerKnownModelIds.Gpt41Mini, LlmTestServerOutputDispositionKind.Refused));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        using var chat = control.CreateChatClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Microsoft.Extensions.AI.ChatMessage[] prompt = [new(ChatRole.User, "policy")];
        var response = streaming
            ? await chat.GetStreamingResponseAsync(prompt, cancellationToken: deadline.Token).ToChatResponseAsync(deadline.Token)
            : await chat.GetResponseAsync(prompt, cancellationToken: deadline.Token);
        var refusal = response.Messages.SelectMany(message => message.Contents).OfType<ErrorContent>().Single();
        await Assert.That(refusal.ErrorCode).IsEqualTo("Refusal");
        await Assert.That(refusal.Message).IsEqualTo(_text);
        await Assert.That(response.Messages.SelectMany(message => message.Contents).OfType<TextContent>()).IsEmpty();
        await Assert.That(response.Text).IsEqualTo(string.Empty);
        await EvidenceAsync(control, LlmTestServerOutputDispositionKind.Refused, streaming);
        var recovered = await chat.GetResponseAsync([.. prompt, .. response.Messages, new(ChatRole.User, "next")], cancellationToken: deadline.Token);
        await Assert.That(recovered.Text).IsEqualTo(_recovery);
        var trace = await CompletedAsync(control, 2);
        var assistant = trace.Messages.Single(message => message.Role == "assistant");
        await Assert.That(assistant.Content).IsEqualTo(_text);
        await Assert.That(assistant.OutputDisposition!.Kind).IsEqualTo(LlmTestServerOutputDispositionKind.Refused);
        await RecoveredAsync(control);
    }
}
