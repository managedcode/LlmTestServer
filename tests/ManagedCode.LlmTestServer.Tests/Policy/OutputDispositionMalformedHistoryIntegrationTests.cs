#pragma warning disable OPENAI001 // Validate native Responses admission with the original SDK recovery path.
using System.Net;
using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI.Chat;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.Policy;

public sealed partial class OutputDispositionIntegrationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RefusalHistoryObjectIsRejectedBeforeFlatteningAndOriginalSdkStillReceivesFirstPolicyAsync(bool responses)
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, LlmTestServerKnownModelIds.Gpt41Mini, LlmTestServerOutputDispositionKind.Filtered));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        object[] history =
        [
            new { role = "assistant", content = new { type = "refusal", refusal = "x" } },
            new { role = "user", content = "policy" },
        ];
        object body = responses
            ? new { model = LlmTestServerKnownModelIds.Gpt41Mini, input = history }
            : new { model = LlmTestServerKnownModelIds.Gpt41Mini, messages = history };
        using var rejected = await http.PostAsJsonAsync(responses ? "/openai/v1/responses" : "/openai/v1/chat/completions", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        if (responses)
        {
            var sdk = ResponsesSdk(http);
            var first = (await sdk.CreateResponseAsync(new CreateResponseOptions
            { Model = LlmTestServerKnownModelIds.Gpt41Mini, StoredOutputEnabled = false, InputItems = { ResponseItem.CreateUserMessageItem("policy") } })).Value;
            await Assert.That(first.Status).IsEqualTo(ResponseStatus.Incomplete);
            await Assert.That(first.IncompleteStatusDetails.Reason).IsEqualTo(ResponseIncompleteStatusReason.ContentFilter);
            await Assert.That(first.GetOutputText()).IsEqualTo(_text);
            await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(1);
            var next = (await sdk.CreateResponseAsync(new CreateResponseOptions
            { Model = LlmTestServerKnownModelIds.Gpt41Mini, StoredOutputEnabled = false, InputItems = { ResponseItem.CreateUserMessageItem("next") } })).Value;
            await Assert.That(next.GetOutputText()).IsEqualTo(_recovery);
        }
        else
        {
            var sdk = ChatSdk(http);
            var first = (await sdk.CompleteChatAsync([new UserChatMessage("policy")])).Value;
            await Assert.That(first.FinishReason).IsEqualTo(ChatFinishReason.ContentFilter);
            await Assert.That(first.Content.Single().Text).IsEqualTo(_text);
            await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(1);
            var next = (await sdk.CompleteChatAsync([new UserChatMessage("next")])).Value;
            await Assert.That(next.Content.Single().Text).IsEqualTo(_recovery);
        }
        var recovery = await CompletedAsync(control, 3);
        await Assert.That(recovery.OutputDisposition).IsNull();
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
        await Assert.That((await control.GetRequestsAsync()).TotalObserved).IsEqualTo(3);
    }
}
