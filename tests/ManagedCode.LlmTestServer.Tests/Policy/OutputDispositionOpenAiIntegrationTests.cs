#pragma warning disable OPENAI001 // Qualify the pinned official Responses SDK against native policy outcomes.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI.Chat;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.Policy;

public sealed partial class OutputDispositionIntegrationTests
{
    [Test]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered, false, false)]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered, true, false)]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered, false, true)]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered, true, true)]
    [Arguments(LlmTestServerOutputDispositionKind.Refused, false, false)]
    [Arguments(LlmTestServerOutputDispositionKind.Refused, true, false)]
    [Arguments(LlmTestServerOutputDispositionKind.Refused, false, true)]
    [Arguments(LlmTestServerOutputDispositionKind.Refused, true, true)]
    public async Task OfficialOpenAiChat_ReturnsNativePolicyOutcomeThenQueuedRecoveryAsync(
        LlmTestServerOutputDispositionKind kind, bool streaming, bool empty)
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, LlmTestServerKnownModelIds.Gpt41Mini, kind, empty));
        using var http = host.Client();
        var control = new LlmTestServerClient(http);
        var sdk = ChatSdk(http);
        var content = new StringBuilder(); var refusal = new StringBuilder(); ChatFinishReason? finish = null; var sawRefusal = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        if (streaming)
        {
            await foreach (var update in sdk.CompleteChatStreamingAsync([new UserChatMessage("policy")], cancellationToken: deadline.Token))
            {
                foreach (var part in update.ContentUpdate) { content.Append(part.Text); }
                refusal.Append(update.RefusalUpdate);
                sawRefusal |= update.RefusalUpdate is not null;
                finish = update.FinishReason ?? finish;
            }
        }
        else
        {
            var response = (await sdk.CompleteChatAsync([new UserChatMessage("policy")], cancellationToken: deadline.Token)).Value;
            foreach (var part in response.Content) { content.Append(part.Text); }
            refusal.Append(response.Refusal);
            sawRefusal = response.Refusal is not null;
            finish = response.FinishReason;
        }
        var expected = empty ? string.Empty : _text;
        await Assert.That(sawRefusal).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Refused);
        await Assert.That(finish).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Filtered ? ChatFinishReason.ContentFilter : ChatFinishReason.Stop);
        await Assert.That(content.ToString()).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Filtered ? expected : string.Empty);
        await Assert.That(refusal.ToString()).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Refused ? expected : string.Empty);
        await EvidenceAsync(control, kind, streaming);
        var recovered = (await sdk.CompleteChatAsync([new UserChatMessage("next")], cancellationToken: deadline.Token)).Value;
        await Assert.That(recovered.Content.Single().Text).IsEqualTo(_recovery);
        await Assert.That(recovered.FinishReason).IsEqualTo(ChatFinishReason.Stop);
        await RecoveredAsync(control);
    }

    [Test]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered, false, false)]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered, true, false)]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered, false, true)]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered, true, true)]
    [Arguments(LlmTestServerOutputDispositionKind.Refused, false, false)]
    [Arguments(LlmTestServerOutputDispositionKind.Refused, true, false)]
    [Arguments(LlmTestServerOutputDispositionKind.Refused, false, true)]
    [Arguments(LlmTestServerOutputDispositionKind.Refused, true, true)]
    public async Task OfficialResponses_ReturnsTypedIncompleteOrRefusalThenQueuedRecoveryAsync(
        LlmTestServerOutputDispositionKind kind, bool streaming, bool empty)
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, LlmTestServerKnownModelIds.Gpt41Mini, kind, empty));
        using var http = host.Client();
        var control = new LlmTestServerClient(http);
        var sdk = ResponsesSdk(http);
        var options = new CreateResponseOptions
        { Model = LlmTestServerKnownModelIds.Gpt41Mini, StoredOutputEnabled = false, StreamingEnabled = streaming, InputItems = { ResponseItem.CreateUserMessageItem("policy") } };
        ResponseResult? result = null; var deltas = new StringBuilder(); var refusals = new StringBuilder(); var incomplete = false; var completed = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        if (streaming)
        {
            await foreach (var update in sdk.CreateResponseStreamingAsync(options, deadline.Token))
            {
                if (update is StreamingResponseOutputTextDeltaUpdate text) { deltas.Append(text.Delta); }
                if (update is StreamingResponseRefusalDeltaUpdate refusal) { refusals.Append(refusal.Delta); }
                if (update is StreamingResponseIncompleteUpdate incompleteEnding) { incomplete = true; result = incompleteEnding.Response; }
                if (update is StreamingResponseCompletedUpdate completedEnding) { completed = true; result = completedEnding.Response; }
                await Assert.That(update is StreamingResponseFailedUpdate or StreamingResponseErrorUpdate).IsFalse();
            }
            await Assert.That(incomplete).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Filtered);
            await Assert.That(completed).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Refused);
            await Assert.That(deltas.ToString()).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Filtered && !empty ? _text : string.Empty);
            await Assert.That(refusals.ToString()).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Refused && !empty ? _text : string.Empty);
        }
        else { result = (await sdk.CreateResponseAsync(options, deadline.Token)).Value; }
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Status).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Filtered ? ResponseStatus.Incomplete : ResponseStatus.Completed);
        if (kind == LlmTestServerOutputDispositionKind.Filtered)
        {
            await Assert.That(result.IncompleteStatusDetails.Reason).IsEqualTo(ResponseIncompleteStatusReason.ContentFilter);
            await Assert.That(result.GetOutputText()).IsEqualTo(empty ? string.Empty : _text);
        }
        else
        {
            var message = (MessageResponseItem)result.OutputItems.Single();
            await Assert.That(message.Content.Single().Kind).IsEqualTo(ResponseContentPartKind.Refusal);
            await Assert.That(message.Content.Single().Refusal).IsEqualTo(empty ? string.Empty : _text);
            await Assert.That(result.IncompleteStatusDetails).IsNull();
        }
        await EvidenceAsync(control, kind, streaming);
        var next = (await sdk.CreateResponseAsync(new CreateResponseOptions
        { Model = LlmTestServerKnownModelIds.Gpt41Mini, StoredOutputEnabled = false, InputItems = { ResponseItem.CreateUserMessageItem("next") } }, deadline.Token)).Value;
        await Assert.That(next.Status).IsEqualTo(ResponseStatus.Completed);
        await Assert.That(next.GetOutputText()).IsEqualTo(_recovery);
        await RecoveredAsync(control);
    }

    private static ResponsesClient ResponsesSdk(HttpClient http)
    {
        return new(new ApiKeyCredential(_key), new ResponsesClientOptions
        { Endpoint = new Uri(http.BaseAddress!, "/openai/v1/"), Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) });
    }
}
