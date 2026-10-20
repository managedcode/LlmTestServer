#pragma warning disable OPENAI001 // Qualify refusal history through the pinned official Responses SDK.
using System.Text;
using System.Text.Json;
using global::Anthropic.Models.Messages;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI.Chat;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.Policy;

public sealed partial class OutputDispositionIntegrationTests
{
    [Test]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered)]
    [Arguments(LlmTestServerOutputDispositionKind.Refused)]
    public async Task OfficialChat_PolicyGraphReplaysAndSessionsKeepIndependentStateAndCacheAsync(LlmTestServerOutputDispositionKind kind)
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
        var firstSdk = ChatSdk(firstConversation.HttpClient);
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
        var second = (await ChatSdk(secondConversation.HttpClient).CompleteChatAsync(messages, cancellationToken: deadline.Token)).Value;
        await Assert.That(second.Usage.InputTokenDetails.CachedTokenCount).IsEqualTo(0);
        await Assert.That((await secondConversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        var recovered = (await firstSdk.CompleteChatAsync([.. messages, new AssistantChatMessage(first), new UserChatMessage("next")], cancellationToken: deadline.Token)).Value;
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
    [Arguments(false)]
    [Arguments(true)]
    public async Task OfficialChat_PureRefusalContentPartsRoundTripIntoNormalizedAssistantHistoryAsync(bool multipleParts)
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, LlmTestServerKnownModelIds.Gpt41Mini, LlmTestServerOutputDispositionKind.Refused));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        var sdk = ChatSdk(http);
        var initial = (await sdk.CompleteChatAsync([new UserChatMessage("policy")])).Value;
        await Assert.That(initial.Refusal).IsEqualTo(_text);
        var assistant = multipleParts
            ? new AssistantChatMessage(ChatMessageContentPart.CreateRefusalPart("Visible "), ChatMessageContentPart.CreateRefusalPart("fixture text"))
            : new AssistantChatMessage(ChatMessageContentPart.CreateRefusalPart(initial.Refusal));
        var follow = (await sdk.CompleteChatAsync([new UserChatMessage("policy"), assistant, new UserChatMessage("next")])).Value;
        await Assert.That(follow.Content.Single().Text).IsEqualTo(_recovery);
        var trace = await CompletedAsync(control, 2);
        var history = trace.Messages.Single(message => message.Role == "assistant");
        await Assert.That(history.Content).IsEqualTo(_text);
        await Assert.That(history.OutputDisposition!.Kind).IsEqualTo(LlmTestServerOutputDispositionKind.Refused);
        await Assert.That(trace.OutputDisposition).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OfficialResponses_RefusalContinuationPreservesNativeHistoryForExplicitAndStoredInputAsync(bool stored)
    {
        await using var host = await StartAsync(config => config.AddChatScenario("refusal-history", scenario => scenario
            .Responds(_text).WithOutputDisposition(new() { Kind = LlmTestServerOutputDispositionKind.Refused }).Responds(_recovery)));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        var sdk = ResponsesSdk(http);
        var options = new CreateResponseOptions
        { Model = LlmTestServerKnownModelIds.Gpt41Mini, StoredOutputEnabled = stored, InputItems = { ResponseItem.CreateUserMessageItem("policy") } };
        var first = (await sdk.CreateResponseAsync(options)).Value;
        await Assert.That(((MessageResponseItem)first.OutputItems.Single()).Content.Single().Refusal).IsEqualTo(_text);
        var next = new CreateResponseOptions { Model = LlmTestServerKnownModelIds.Gpt41Mini, StoredOutputEnabled = false };
        if (stored) { next.PreviousResponseId = first.Id; }
        else
        {
            foreach (var item in options.InputItems) { next.InputItems.Add(item); }
            foreach (var item in first.OutputItems) { next.InputItems.Add(item); }
        }
        next.InputItems.Add(ResponseItem.CreateUserMessageItem("next"));
        var continued = (await sdk.CreateResponseAsync(next)).Value;
        await Assert.That(continued.GetOutputText()).IsEqualTo(_recovery);
        var trace = await CompletedAsync(control, 2);
        var history = trace.Messages.Single(message => message.Role == "assistant");
        await Assert.That(history.Content).IsEqualTo(_text);
        await Assert.That(history.OutputDisposition!.Kind).IsEqualTo(LlmTestServerOutputDispositionKind.Refused);
        await Assert.That(trace.OutputDisposition).IsNull();
    }

    [Test]
    [Arguments(LlmTestServerOutputDispositionKind.Refused)]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered)]
    public async Task OfficialResponses_StoredPolicyHistoryAndCanonicalManualHistoryReplayTheSameGraphTurnAsync(LlmTestServerOutputDispositionKind kind)
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
        var sdk = ResponsesSdk(conversation.HttpClient);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var first = (await sdk.CreateResponseAsync(new CreateResponseOptions
        {
            Model = LlmTestServerKnownModelIds.Gpt41Mini,
            StoredOutputEnabled = true,
            InputItems = { ResponseItem.CreateUserMessageItem("go") },
        }, deadline.Token)).Value;
        await Assert.That(first.Status).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Filtered ? ResponseStatus.Incomplete : ResponseStatus.Completed);
        var automatic = (await sdk.CreateResponseAsync(new CreateResponseOptions
        {
            Model = LlmTestServerKnownModelIds.Gpt41Mini,
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
    }

    [Test]
    public async Task OfficialAnthropic_StrictRefusalAssistantContentCanBeReturnedInNextTurnAsync()
    {
        var model = JsonSerializer.SerializeToElement(Model.ClaudeSonnet5_5).GetString()!;
        await using var host = await StartAsync(config => ConfigureQueue(config, model, LlmTestServerOutputDispositionKind.Refused));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        using var sdk = AnthropicSdk(http);
        var options = AnthropicOptions();
        var refused = await sdk.Messages.Create(options);
        var history = JsonSerializer.Deserialize<List<ContentBlockParam>>(JsonSerializer.Serialize(refused.Content))!;
        var continued = await sdk.Messages.Create(options with
        { Messages = [.. options.Messages, new() { Role = Role.Assistant, Content = history }, new() { Role = Role.User, Content = "next" }] });
        await Assert.That(((TextBlock)continued.Content.Single().Value!).Text).IsEqualTo(_recovery);
        var trace = await CompletedAsync(control, 2);
        await Assert.That(trace.Messages.Single(message => message.Role == "assistant").Content).IsEqualTo(_text);
        await Assert.That(trace.OutputDisposition).IsNull();
    }
}
