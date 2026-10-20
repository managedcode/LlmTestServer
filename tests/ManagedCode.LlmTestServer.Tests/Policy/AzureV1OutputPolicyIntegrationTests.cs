#pragma warning disable OPENAI001 // Qualify the pinned official Responses SDK against native policy outcomes.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.Policy;

public sealed partial class OutputDispositionIntegrationTests
{
    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Filtered, false, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Filtered, false, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Filtered, true, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Filtered, true, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Filtered, false, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Filtered, false, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Filtered, true, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Filtered, true, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Refused, false, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Refused, false, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Refused, true, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Refused, true, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Refused, false, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Refused, false, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Refused, true, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Refused, true, true)]
    public async Task AzureV1Chat_OfficialSdkPolicyOutcomesAndRecoveryAsync(
        string provider, LlmTestServerOutputDispositionKind kind, bool streaming, bool empty)
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, LlmTestServerKnownModelIds.Gpt41Mini, kind, empty));
        using var http = host.Client();
        var control = new LlmTestServerClient(http);
        var sdk = AzurePolicyChatSdk(http, provider);
        var content = new StringBuilder(); var refusal = new StringBuilder(); ChatFinishReason? finish = null; var sawRefusal = false; ChatTokenUsage? sdkUsage = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        if (streaming)
        {
            await foreach (var update in sdk.CompleteChatStreamingAsync([new UserChatMessage("policy")], cancellationToken: deadline.Token))
            {
                foreach (var part in update.ContentUpdate) { content.Append(part.Text); }
                refusal.Append(update.RefusalUpdate);
                sawRefusal |= update.RefusalUpdate is not null;
                finish = update.FinishReason ?? finish;
                sdkUsage = update.Usage ?? sdkUsage;
            }
        }
        else
        {
            var response = (await sdk.CompleteChatAsync([new UserChatMessage("policy")], cancellationToken: deadline.Token)).Value;
            foreach (var part in response.Content) { content.Append(part.Text); }
            refusal.Append(response.Refusal);
            sawRefusal = response.Refusal is not null;
            finish = response.FinishReason;
            sdkUsage = response.Usage;
        }
        var expected = empty ? string.Empty : _text;
        await Assert.That(sawRefusal).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Refused);
        await Assert.That(finish).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Filtered ? ChatFinishReason.ContentFilter : ChatFinishReason.Stop);
        await Assert.That(content.ToString()).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Filtered ? expected : string.Empty);
        await Assert.That(refusal.ToString()).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Refused ? expected : string.Empty);
        await EvidenceAsync(control, kind, streaming);
        var trace = await CompletedAsync(control, 1);
        await Assert.That(trace.Path).IsEqualTo(provider + "/openai/v1/chat/completions");
        await Assert.That(trace.Usage!.InputTokens).IsGreaterThan(0);
        await Assert.That(sdkUsage!.TotalTokenCount).IsEqualTo(trace.Usage.TotalTokens);
        var recovered = (await sdk.CompleteChatAsync([new UserChatMessage("next")], cancellationToken: deadline.Token)).Value;
        await Assert.That(recovered.Content.Single().Text).IsEqualTo(_recovery);
        await Assert.That(recovered.FinishReason).IsEqualTo(ChatFinishReason.Stop);
        await RecoveredAsync(control);
    }

    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Filtered, false, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Filtered, false, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Filtered, true, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Filtered, true, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Filtered, false, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Filtered, false, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Filtered, true, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Filtered, true, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Refused, false, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Refused, false, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Refused, true, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Refused, true, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Refused, false, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Refused, false, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Refused, true, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Refused, true, true)]
    public async Task AzureV1Responses_OfficialSdkPolicyOutcomesAndRecoveryAsync(
        string provider, LlmTestServerOutputDispositionKind kind, bool streaming, bool empty)
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, LlmTestServerKnownModelIds.Gpt41Mini, kind, empty));
        using var http = host.Client();
        var control = new LlmTestServerClient(http);
        var sdk = AzurePolicyResponsesSdk(http, provider);
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
        var trace = await CompletedAsync(control, 1);
        await Assert.That(trace.Path).IsEqualTo(provider + "/openai/v1/responses");
        await Assert.That(trace.Usage!.InputTokens).IsGreaterThan(0);
        await Assert.That(result.Usage.TotalTokenCount).IsEqualTo(trace.Usage.TotalTokens);
        var next = (await sdk.CreateResponseAsync(new CreateResponseOptions
        { Model = LlmTestServerKnownModelIds.Gpt41Mini, StoredOutputEnabled = false, InputItems = { ResponseItem.CreateUserMessageItem("next") } }, deadline.Token)).Value;
        await Assert.That(next.Status).IsEqualTo(ResponseStatus.Completed);
        await Assert.That(next.GetOutputText()).IsEqualTo(_recovery);
        await RecoveredAsync(control);
    }

    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Filtered)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Filtered)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Refused)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Refused)]
    public async Task AzureV1Chat_PolicyHistoryReplayAndSessionsRemainIsolatedAsync(string provider, LlmTestServerOutputDispositionKind kind)
    {
        await using var host = await StartAsync(_ => { });
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        void Configure(LlmTestServerClientConfigurationBuilder config)
        {
            config.UseGpt41Mini().UseConversation("policy-graph", graph => graph.ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                .Step("policy", step => step.WhenLatestUserEquals("go").Responds(_text, "Visible ", "fixture text")
                    .WithOutputDisposition(new() { Kind = kind }).Then("next"))
                .Step("next", step => step.WhenLatestUserEquals("next").Responds(_recovery).Complete()));
        }
        await using var firstSession = await control.CreateSessionAsync(Configure);
        await using var secondSession = await control.CreateSessionAsync(Configure);
        using var firstConversation = firstSession.Control.ForConversation("same-label");
        using var secondConversation = secondSession.Control.ForConversation("same-label");
        var prompt = new SystemChatMessage(string.Join(" ", Enumerable.Repeat("stable policy context", 1400)));
        global::OpenAI.Chat.ChatMessage[] messages = [prompt, new UserChatMessage("go")];
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var firstSdk = AzurePolicyChatSdk(firstConversation.HttpClient, provider);
        var first = (await firstSdk.CompleteChatAsync(messages, cancellationToken: deadline.Token)).Value;
        await Assert.That(first.FinishReason).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Filtered ? ChatFinishReason.ContentFilter : ChatFinishReason.Stop);
        await Assert.That(first.Usage.InputTokenDetails.CachedTokenCount).IsEqualTo(0);
        var replayText = new StringBuilder(); var replayRefusal = new StringBuilder(); ChatFinishReason? replayReason = null;
        await foreach (var update in firstSdk.CompleteChatStreamingAsync(messages, cancellationToken: deadline.Token))
        {
            foreach (var part in update.ContentUpdate) { replayText.Append(part.Text); }
            replayRefusal.Append(update.RefusalUpdate);
            replayReason = update.FinishReason ?? replayReason;
        }
        await Assert.That(replayReason).IsEqualTo(first.FinishReason);
        await Assert.That(kind == LlmTestServerOutputDispositionKind.Filtered ? replayText.ToString() : replayRefusal.ToString()).IsEqualTo(_text);
        await Assert.That((await firstConversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        await Assert.That((await secondSession.Control.GetConversationsAsync()).Count).IsEqualTo(0);
        var replay = await CompletedAsync(firstConversation.Control, 2);
        await Assert.That(replay.IsReplay).IsTrue();
        await Assert.That(replay.OutputDisposition!.Kind).IsEqualTo(kind);
        await Assert.That(replay.TerminalMarkerObserved).IsTrue();
        var second = (await AzurePolicyChatSdk(secondConversation.HttpClient, provider).CompleteChatAsync(messages, cancellationToken: deadline.Token)).Value;
        await Assert.That(second.Usage.InputTokenDetails.CachedTokenCount).IsEqualTo(0);
        await Assert.That((await secondConversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        var recovered = (await firstSdk.CompleteChatAsync([.. messages, kind == LlmTestServerOutputDispositionKind.Refused ? new AssistantChatMessage(ChatMessageContentPart.CreateRefusalPart(first.Refusal)) : new AssistantChatMessage(first), new UserChatMessage("next")], cancellationToken: deadline.Token)).Value;
        await Assert.That(recovered.Content.Single().Text).IsEqualTo(_recovery);
        await Assert.That(recovered.Usage.InputTokenDetails.CachedTokenCount).IsGreaterThan(0);
        await Assert.That((await firstConversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
        await Assert.That((await firstConversation.Control.GetConversationsAsync()).Single().IsTerminal).IsTrue();
        await Assert.That((await secondConversation.Control.GetConversationsAsync()).Single().IsTerminal).IsFalse();
        var followTrace = await CompletedAsync(firstConversation.Control, 3);
        var assistant = followTrace.Messages.Single(message => message.Role == "assistant");
        await Assert.That(assistant.Content).IsEqualTo(_text);
        if (kind == LlmTestServerOutputDispositionKind.Refused)
        { await Assert.That(assistant.OutputDisposition!.Kind).IsEqualTo(kind); }
    }

    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Refused)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Refused)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, LlmTestServerOutputDispositionKind.Filtered)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, LlmTestServerOutputDispositionKind.Filtered)]
    public async Task AzureV1Responses_StoredAndManualHistoryReplaySameTurnWithCommittedCacheAsync(string provider, LlmTestServerOutputDispositionKind kind)
    {
        await using var host = await StartAsync(config => config.AddConversation("policy-history-projection", graph => graph
            .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
            .Step("policy", step => step.WhenLatestUserEquals("go").Responds(_text)
                .WithOutputDisposition(new()
                {
                    Kind = kind,
                    Category = kind == LlmTestServerOutputDispositionKind.Refused ? "cyber" : null,
                    Explanation = kind == LlmTestServerOutputDispositionKind.Refused ? "Full diagnostic metadata" : null,
                }).Then("next"))
            .Step("next", step => step.WhenLatestUserEquals("next").Responds(_recovery).Then("tail"))
            .Step("tail", step => step.WhenLatestUserEquals("tail").Responds("Remaining graph step").Complete())));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        using var conversation = control.ForConversation("canonical-policy-history");
        var sdk = AzurePolicyResponsesSdk(conversation.HttpClient, provider);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var prefix = string.Join(" ", Enumerable.Repeat("stable Azure v1 policy context", 1400));
        var first = (await sdk.CreateResponseAsync(new CreateResponseOptions
        {
            Model = LlmTestServerKnownModelIds.Gpt41Mini,
            Instructions = prefix,
            StoredOutputEnabled = true,
            InputItems = { ResponseItem.CreateUserMessageItem("go") },
        }, deadline.Token)).Value;
        await Assert.That(first.Status).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Filtered ? ResponseStatus.Incomplete : ResponseStatus.Completed);
        var automatic = (await sdk.CreateResponseAsync(new CreateResponseOptions
        {
            Model = LlmTestServerKnownModelIds.Gpt41Mini,
            Instructions = prefix,
            StoredOutputEnabled = false,
            PreviousResponseId = first.Id,
            InputItems = { ResponseItem.CreateUserMessageItem("next") },
        }, deadline.Token)).Value;
        await Assert.That(automatic.GetOutputText()).IsEqualTo(_recovery);
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
        var assistant = kind == LlmTestServerOutputDispositionKind.Refused
            ? ResponseItem.CreateAssistantMessageItem([ResponseContentPart.CreateRefusalPart(_text)])
            : ResponseItem.CreateAssistantMessageItem(_text);
        var canonical = (await sdk.CreateResponseAsync(new CreateResponseOptions
        {
            Model = LlmTestServerKnownModelIds.Gpt41Mini,
            Instructions = prefix,
            StoredOutputEnabled = false,
            InputItems = { ResponseItem.CreateUserMessageItem("go"), assistant, ResponseItem.CreateUserMessageItem("next") },
        }, deadline.Token)).Value;
        await Assert.That(canonical.Id).IsEqualTo(automatic.Id);
        await Assert.That(canonical.GetOutputText()).IsEqualTo(_recovery);
        await Assert.That(canonical.Usage.InputTokenCount).IsEqualTo(automatic.Usage.InputTokenCount);
        await Assert.That(canonical.Usage.OutputTokenCount).IsEqualTo(automatic.Usage.OutputTokenCount);
        var state = (await conversation.Control.GetConversationsAsync()).Single();
        await Assert.That(state.ExecutedSteps).IsEqualTo(2);
        await Assert.That(state.NextSteps.Single()).IsEqualTo("tail");
        await Assert.That(state.IsTerminal).IsFalse();
        var replay = await CompletedAsync(conversation.Control, 3);
        await Assert.That(replay.IsReplay).IsTrue();
        var history = replay.Messages.Single(message => message.Role == "assistant");
        await Assert.That(history.Content).IsEqualTo(_text);
        if (kind == LlmTestServerOutputDispositionKind.Refused)
        {
            await Assert.That(history.OutputDisposition!.Kind).IsEqualTo(kind);
            await Assert.That(history.OutputDisposition.Category).IsNull();
            await Assert.That(history.OutputDisposition.Explanation).IsNull();
        }
        else { await Assert.That(history.OutputDisposition).IsNull(); }
        var journal = await conversation.Control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(journal.Requests[0].OutputDisposition!.Kind).IsEqualTo(kind);
        await Assert.That(journal.Requests[0].OutputDisposition!.Explanation).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Refused ? "Full diagnostic metadata" : null);
        await Assert.That(journal.Requests[1].IsReplay).IsFalse();
        await Assert.That(journal.Requests[0].Usage!.CachedInputTokens).IsEqualTo(0);
        await Assert.That(journal.Requests[1].Usage!.CachedInputTokens).IsGreaterThan(0);
        await Assert.That(replay.Usage!.TotalTokens).IsEqualTo(journal.Requests[1].Usage!.TotalTokens);
    }

    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, false, LlmTestServerOutputDispositionKind.Filtered, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, false, LlmTestServerOutputDispositionKind.Filtered, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, false, LlmTestServerOutputDispositionKind.Refused, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, false, LlmTestServerOutputDispositionKind.Refused, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, true, LlmTestServerOutputDispositionKind.Filtered, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, true, LlmTestServerOutputDispositionKind.Filtered, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, true, LlmTestServerOutputDispositionKind.Refused, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, true, LlmTestServerOutputDispositionKind.Refused, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, false, LlmTestServerOutputDispositionKind.Filtered, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, false, LlmTestServerOutputDispositionKind.Filtered, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, false, LlmTestServerOutputDispositionKind.Refused, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, false, LlmTestServerOutputDispositionKind.Refused, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, true, LlmTestServerOutputDispositionKind.Filtered, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, true, LlmTestServerOutputDispositionKind.Filtered, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, true, LlmTestServerOutputDispositionKind.Refused, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, true, LlmTestServerOutputDispositionKind.Refused, true)]
    public async Task AzureV1_PolicyRejectsToolsAndStructuredOutputBeforeFixtureConsumptionAsync(
        string provider, bool responses, LlmTestServerOutputDispositionKind kind, bool schema)
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, LlmTestServerKnownModelIds.Gpt41Mini, kind));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        if (responses && schema)
        {
            // Malformed/unsupported wire configuration supplements genuine SDK admission below.
            using var rejected = await http.PostAsJsonAsync(provider + "/openai/v1/responses", new
            {
                model = LlmTestServerKnownModelIds.Gpt41Mini,
                input = "policy",
                text = new { format = new { type = "json_object" } },
            });
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        }
        else
        {
            ClientResultException? rejected = null;
            try
            {
                if (responses)
                {
                    var options = new CreateResponseOptions
                    { Model = LlmTestServerKnownModelIds.Gpt41Mini, StoredOutputEnabled = false, InputItems = { ResponseItem.CreateUserMessageItem("policy") } };
                    options.Tools.Add(ResponseTool.CreateFunctionTool("weather", BinaryData.FromString("{\"type\":\"object\"}"), false, "Scripted weather"));
                    await AzurePolicyResponsesSdk(http, provider).CreateResponseAsync(options);
                }
                else
                {
                    var options = new ChatCompletionOptions();
                    if (schema) { options.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(); }
                    else { options.Tools.Add(ChatTool.CreateFunctionTool("weather")); }
                    await AzurePolicyChatSdk(http, provider).CompleteChatAsync([new UserChatMessage("policy")], options);
                }
            }
            catch (ClientResultException exception) { rejected = exception; }
            await Assert.That(rejected?.Status).IsEqualTo(409);
        }
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        if (responses)
        {
            var sdk = AzurePolicyResponsesSdk(http, provider);
            var first = (await sdk.CreateResponseAsync(new CreateResponseOptions
            { Model = LlmTestServerKnownModelIds.Gpt41Mini, StoredOutputEnabled = false, InputItems = { ResponseItem.CreateUserMessageItem("policy") } })).Value;
            await Assert.That(first.Status).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Filtered ? ResponseStatus.Incomplete : ResponseStatus.Completed);
            if (kind == LlmTestServerOutputDispositionKind.Refused)
            { await Assert.That(((MessageResponseItem)first.OutputItems.Single()).Content.Single().Refusal).IsEqualTo(_text); }
            else { await Assert.That(first.GetOutputText()).IsEqualTo(_text); }
            var next = (await sdk.CreateResponseAsync(new CreateResponseOptions
            { Model = LlmTestServerKnownModelIds.Gpt41Mini, StoredOutputEnabled = false, InputItems = { ResponseItem.CreateUserMessageItem("next") } })).Value;
            await Assert.That(next.GetOutputText()).IsEqualTo(_recovery);
        }
        else
        {
            var sdk = AzurePolicyChatSdk(http, provider);
            var first = (await sdk.CompleteChatAsync([new UserChatMessage("policy")])).Value;
            await Assert.That(first.FinishReason).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Filtered ? ChatFinishReason.ContentFilter : ChatFinishReason.Stop);
            await Assert.That(kind == LlmTestServerOutputDispositionKind.Filtered ? first.Content.Single().Text : first.Refusal).IsEqualTo(_text);
            var next = (await sdk.CompleteChatAsync([new UserChatMessage("next")])).Value;
            await Assert.That(next.Content.Single().Text).IsEqualTo(_recovery);
        }
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
        var evidence = await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(evidence.TotalObserved).IsEqualTo(3);
        await Assert.That(evidence.Requests[0].StatusCode).IsEqualTo(409);
        await Assert.That(evidence.Requests[1].OutputDisposition!.Kind).IsEqualTo(kind);
        await Assert.That(evidence.Requests[2].OutputDisposition).IsNull();
    }

    [Test]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered)]
    [Arguments(LlmTestServerOutputDispositionKind.Refused)]
    public async Task AzureV1Responses_StoredPolicyAndGraphStateAreIsolatedByProviderAndSessionAsync(LlmTestServerOutputDispositionKind kind)
    {
        await using var host = await StartAsync(_ => { });
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        void Configure(LlmTestServerClientConfigurationBuilder config)
        {
            config.UseGpt41Mini()
            .UseConversation("isolated-policy", graph => graph.ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                .Step("policy", step => step.WhenLatestUserEquals("go").Responds(_text)
                    .WithOutputDisposition(new() { Kind = kind }).Then("next"))
                .Step("next", step => step.WhenLatestUserEquals("next").Responds(_recovery).Complete()));
        }

        await using var firstSession = await control.CreateSessionAsync(Configure);
        await using var secondSession = await control.CreateSessionAsync(Configure);
        using var firstConversation = firstSession.Control.ForConversation("shared-label");
        using var otherSessionConversation = secondSession.Control.ForConversation("shared-label");
        var azure = AzurePolicyResponsesSdk(firstConversation.HttpClient, LlmTestServerProviderRouteNamespaces.AzureOpenAI);
        var foundry = AzurePolicyResponsesSdk(firstConversation.HttpClient, LlmTestServerProviderRouteNamespaces.MicrosoftFoundry);
        var options = new CreateResponseOptions
        { Model = LlmTestServerKnownModelIds.Gpt41Mini, StoredOutputEnabled = true, InputItems = { ResponseItem.CreateUserMessageItem("go") } };
        var first = (await azure.CreateResponseAsync(options)).Value;
        await Assert.That(string.IsNullOrWhiteSpace(first.Id)).IsFalse();
        var continued = new CreateResponseOptions
        { Model = LlmTestServerKnownModelIds.Gpt41Mini, StoredOutputEnabled = false, PreviousResponseId = first.Id, InputItems = { ResponseItem.CreateUserMessageItem("next") } };
        foreach (var unauthorized in new[]
        {
            foundry,
            AzurePolicyResponsesSdk(otherSessionConversation.HttpClient, LlmTestServerProviderRouteNamespaces.AzureOpenAI),
        })
        {
            ClientResultException? rejected = null;
            try { await unauthorized.CreateResponseAsync(continued); }
            catch (ClientResultException exception) { rejected = exception; }
            await Assert.That(rejected?.Status).IsEqualTo(404);
        }
        await Assert.That((await secondSession.Control.GetConversationsAsync()).Count).IsEqualTo(0);
        await Assert.That((await firstSession.Control.GetAssertionsAsync()).Matched).IsEqualTo(1);
        var independent = (await foundry.CreateResponseAsync(options)).Value;
        await Assert.That(independent.Status).IsEqualTo(first.Status);
        var states = await firstSession.Control.GetConversationsAsync();
        await Assert.That(states.Count).IsEqualTo(2);
        await Assert.That(states.All(state => state.ExecutedSteps == 1 && !state.IsTerminal)).IsTrue();
        var recovered = (await azure.CreateResponseAsync(continued)).Value;
        await Assert.That(recovered.GetOutputText()).IsEqualTo(_recovery);
        states = await firstSession.Control.GetConversationsAsync();
        await Assert.That(states.Count(state => state.ExecutedSteps == 2 && state.IsTerminal)).IsEqualTo(1);
        await Assert.That(states.Count(state => state.ExecutedSteps == 1 && !state.IsTerminal)).IsEqualTo(1);
        var other = (await AzurePolicyResponsesSdk(otherSessionConversation.HttpClient,
            LlmTestServerProviderRouteNamespaces.AzureOpenAI).CreateResponseAsync(options)).Value;
        await Assert.That(other.Status).IsEqualTo(first.Status);
        await Assert.That((await secondSession.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        await Assert.That((await firstSession.Control.GetAssertionsAsync()).Matched).IsEqualTo(3);
    }

    private static Uri AzurePolicyEndpoint(HttpClient http, string provider)
    {
        return new(http.BaseAddress!, provider + "/openai/v1/");
    }

    private static ChatClient AzurePolicyChatSdk(HttpClient http, string provider)
    {
        return new(
        LlmTestServerKnownModelIds.Gpt41Mini, new ApiKeyCredential(_key), new OpenAIClientOptions
        { Endpoint = AzurePolicyEndpoint(http, provider), Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) });
    }

    private static ResponsesClient AzurePolicyResponsesSdk(HttpClient http, string provider)
    {
        return new(new ApiKeyCredential(_key), new ResponsesClientOptions
        { Endpoint = AzurePolicyEndpoint(http, provider), Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) });
    }
}
