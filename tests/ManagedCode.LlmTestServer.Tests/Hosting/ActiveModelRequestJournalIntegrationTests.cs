using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Chat;

namespace ManagedCode.LlmTestServer.Tests.Hosting;

public sealed class ActiveModelRequestJournalIntegrationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Journal_FindsActiveModelBeforeDelayedSdkResponseAsync(bool streaming, bool boundedPreview)
    {
        const string model = "active-journal-model";
        const string reply = "delayed SDK response";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = timeout.Token;
        using var host = await LlmTestServerTestHost.StartAsync(options => options
            .AddModel(model, LlmTestServerModelKind.Chat)
            .AddChatScenario("active-journal", scenario => scenario.ForModel(model)
                .Responds(reply).DelaysBy(2000)),
            configureServices: services => services.Configure<LlmTestServerProviderHttpTraceOptions>(options =>
                options.MaxPreviewBytes = boundedPreview ? 128 : 65536), cancellationToken: cancellationToken);
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var sdk = new ChatClient(model, new ApiKeyCredential("test-key"), new OpenAIClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/openai/v1"),
            Transport = new HttpClientPipelineTransport(http),
            RetryPolicy = new ClientRetryPolicy(0),
        });
        var pending = CompleteAsync();
        LlmTestServerProviderHttpTrace? active = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        while (!pending.IsCompleted)
        {
            var snapshot = await control.GetRequestsAsync(new() { Model = model, Completed = false }, cancellationToken);
            active = snapshot.Requests.SingleOrDefault();
            if (active is not null) { break; }
            if (await Task.WhenAny(pending, timer.WaitForNextTickAsync(cancellationToken).AsTask()) == pending) { break; }
        }
        var observedBeforeCompletion = !pending.IsCompleted;
        var content = await pending;
        await Assert.That(content).IsEqualTo(reply);
        await Assert.That(active).IsNotNull().Because("Model filtering must expose the actual pending SDK request before the scripted delay ends.");
        await Assert.That(observedBeforeCompletion).IsTrue();
        await Assert.That(active!.IsCompleted).IsFalse();
        await Assert.That(active.ModelId).IsEqualTo(model);
        var completed = await control.GetRequestsAsync(new() { RequestId = active.RequestId, Completed = true }, cancellationToken);
        await Assert.That(completed.TotalObserved).IsEqualTo(1);
        await Assert.That(completed.Requests.Single().ModelId).IsEqualTo(model);
        await Assert.That(completed.Requests.Single().StatusCode).IsEqualTo(200);
        await Assert.That(completed.Requests.Single().RequestPreviewTruncated).IsEqualTo(boundedPreview);
        await Assert.That((await control.GetRequestsAsync(new() { Model = model, Completed = false }, cancellationToken)).Requests).IsEmpty();
        var assertions = await control.GetAssertionsAsync(cancellationToken);
        await Assert.That(assertions.TotalEvents).IsEqualTo(1);
        await Assert.That(assertions.Matched).IsEqualTo(1);
        await Assert.That(assertions.Events.Count).IsEqualTo(1);

        async Task<string> CompleteAsync()
        {
            ChatMessage[] messages = [new UserChatMessage(boundedPreview ? new string('p', 16000) : "journal")];
            if (!streaming)
            {
                return (await sdk.CompleteChatAsync(messages, cancellationToken: cancellationToken)).Value.Content[0].Text;
            }
            var text = new StringBuilder();
            await foreach (var update in sdk.CompleteChatStreamingAsync(messages, cancellationToken: cancellationToken))
            {
                foreach (var part in update.ContentUpdate) { text.Append(part.Text); }
            }
            return text.ToString();
        }
    }
}
