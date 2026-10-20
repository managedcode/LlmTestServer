using System.ClientModel;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI.Chat;

namespace ManagedCode.LlmTestServer.Tests.Policy;

public sealed partial class OutputDispositionIntegrationTests
{
    // Current primary contract: https://api-docs.deepseek.com/api/create-chat-completion/
    private const string _deepSeekModel = "deepseek-flash";
    private const string _deepSeekPath = "/deepseek/v1/chat/completions";

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task DeepSeek_OfficialOpenAiSdkParsesFilteredJsonAndSseWithUsageThenRecoversAsync(bool streaming, bool empty)
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, _deepSeekModel, LlmTestServerOutputDispositionKind.Filtered, empty));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        var sdk = ChatSdk(http, "deepseek", _deepSeekModel);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var text = new StringBuilder(); ChatFinishReason? finish = null; ChatTokenUsage? usage = null; var usageUpdates = 0;
        if (streaming)
        {
            await foreach (var update in sdk.CompleteChatStreamingAsync([new UserChatMessage("policy")], cancellationToken: deadline.Token))
            {
                foreach (var part in update.ContentUpdate) { text.Append(part.Text); }
                finish = update.FinishReason ?? finish;
                await Assert.That(update.RefusalUpdate).IsNull();
                if (update.Usage is not null) { usage = update.Usage; usageUpdates++; }
            }
            await Assert.That(usageUpdates).IsEqualTo(1);
        }
        else
        {
            var result = (await sdk.CompleteChatAsync([new UserChatMessage("policy")], cancellationToken: deadline.Token)).Value;
            foreach (var part in result.Content) { text.Append(part.Text); }
            finish = result.FinishReason; usage = result.Usage;
            await Assert.That(result.Refusal).IsNull();
        }
        await Assert.That(text.ToString()).IsEqualTo(empty ? string.Empty : _text);
        await Assert.That(finish).IsEqualTo(ChatFinishReason.ContentFilter);
        await Assert.That(usage).IsNotNull();
        await EvidenceAsync(control, LlmTestServerOutputDispositionKind.Filtered, streaming);
        var first = await CompletedAsync(control, 1);
        await Assert.That(first.Path).IsEqualTo(_deepSeekPath);
        await Assert.That(first.ModelId).IsEqualTo(_deepSeekModel);
        await Assert.That(usage!.InputTokenCount).IsEqualTo(first.Usage!.InputTokens);
        await Assert.That(usage.OutputTokenCount).IsEqualTo(first.Usage.OutputTokens);
        await Assert.That(usage.TotalTokenCount).IsEqualTo(first.Usage.TotalTokens);
        var next = (await sdk.CompleteChatAsync([new UserChatMessage("next")], cancellationToken: deadline.Token)).Value;
        await Assert.That(next.Content.Single().Text).IsEqualTo(_recovery);
        await Assert.That(next.FinishReason).IsEqualTo(ChatFinishReason.Stop);
        await RecoveredAsync(control);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(false, null)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    [Arguments(true, null)]
    public async Task DeepSeek_NativeSseFinalChoiceCarriesUsageForAllOptionsAndPolicyOutcomesAsync(bool filtered, bool? includeUsage)
    {
        await using var host = await StartAsync(config => config.AddModel(_deepSeekModel, LlmTestServerModelKind.Chat)
            .AddChatScenario("deepseek-native-stream", scenario =>
            {
                scenario.ForModel(_deepSeekModel).Responds(_text, "Visible ", "fixture text");
                if (filtered) { scenario.WithOutputDisposition(new() { Kind = LlmTestServerOutputDispositionKind.Filtered }); }
                scenario.Responds(_recovery);
            }));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        // Exact native frame placement supplements the original SDK success coverage above.
        var body = new JsonObject
        {
            ["model"] = _deepSeekModel,
            ["stream"] = true,
            ["messages"] = JsonSerializer.SerializeToNode(new[] { new { role = "user", content = "policy" } }),
        };
        if (includeUsage is { } requestedUsage) { body["stream_options"] = new JsonObject { ["include_usage"] = requestedUsage }; }
        using var response = await http.PostAsJsonAsync(_deepSeekPath, body);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("text/event-stream");
        var records = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal)).Select(line => line[5..].Trim()).ToArray();
        await Assert.That(records[^1]).IsEqualTo("[DONE]");
        var frames = records[..^1].Select(record => JsonSerializer.Deserialize<JsonElement>(record)).ToArray();
        await Assert.That(frames.Length).IsEqualTo(3);
        await Assert.That(frames.All(frame => frame.GetProperty("choices").GetArrayLength() == 1)).IsTrue();
        foreach (var frame in frames[..^1])
        {
            await Assert.That(frame.GetProperty("choices")[0].GetProperty("finish_reason").ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(frame.TryGetProperty("usage", out var interimUsage)).IsEqualTo(includeUsage == true);
            if (includeUsage == true) { await Assert.That(interimUsage.ValueKind).IsEqualTo(JsonValueKind.Null); }
        }
        var final = frames[^1]; var finalChoice = final.GetProperty("choices")[0];
        await Assert.That(finalChoice.GetProperty("finish_reason").GetString()).IsEqualTo(filtered ? "content_filter" : "stop");
        await Assert.That(finalChoice.GetProperty("delta").GetProperty("content").GetString()).IsEqualTo(string.Empty);
        var usage = final.GetProperty("usage");
        await Assert.That(usage.ValueKind).IsEqualTo(JsonValueKind.Object);
        await Assert.That(frames.Count(frame => frame.TryGetProperty("usage", out var value) && value.ValueKind == JsonValueKind.Object)).IsEqualTo(1);
        var trace = await CompletedAsync(control, 1);
        await Assert.That(trace.TerminalMarkerObserved).IsTrue();
        await Assert.That(trace.OutputDisposition?.Kind == LlmTestServerOutputDispositionKind.Filtered).IsEqualTo(filtered);
        await Assert.That(usage.GetProperty("prompt_tokens").GetInt32()).IsEqualTo(trace.Usage!.InputTokens);
        await Assert.That(usage.GetProperty("completion_tokens").GetInt32()).IsEqualTo(trace.Usage.OutputTokens);
        await Assert.That(usage.GetProperty("total_tokens").GetInt32()).IsEqualTo(trace.Usage.TotalTokens);
        await Assert.That(usage.GetProperty("prompt_cache_hit_tokens").GetInt32()).IsEqualTo(trace.Usage.CachedInputTokens);
        await Assert.That(usage.GetProperty("prompt_cache_miss_tokens").GetInt32()).IsEqualTo(trace.Usage.InputTokens - trace.Usage.CachedInputTokens);
        await Assert.That((await control.VerifyAsync(new()
        { Requests = [new() { RequireStreamingTerminalMarker = true }] })).Passed).IsTrue();
        var recovered = (await ChatSdk(http, "deepseek", _deepSeekModel).CompleteChatAsync([new UserChatMessage("next")])).Value;
        await Assert.That(recovered.Content.Single().Text).IsEqualTo(_recovery);
        await RecoveredAsync(control);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DeepSeek_RefusedIsRejectedBeforeConsumptionThenQualifiedOpenAiRouteUsesSameFixtureAsync(bool streaming)
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, _deepSeekModel, LlmTestServerOutputDispositionKind.Refused));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        var sdk = ChatSdk(http, "deepseek", _deepSeekModel);
        ClientResultException? failure = null;
        try
        {
            if (streaming) { await foreach (var update in sdk.CompleteChatStreamingAsync([new UserChatMessage("policy")])) { _ = update; } }
            else { await sdk.CompleteChatAsync([new UserChatMessage("policy")]); }
        }
        catch (ClientResultException exception) { failure = exception; }
        await Assert.That(failure?.Status).IsEqualTo(409);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        var qualified = (await ChatSdk(http, model: _deepSeekModel).CompleteChatAsync([new UserChatMessage("policy")])).Value;
        await Assert.That(qualified.Refusal).IsEqualTo(_text);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(1);
        var recovered = (await sdk.CompleteChatAsync([new UserChatMessage("next")])).Value;
        await Assert.That(recovered.Content.Single().Text).IsEqualTo(_recovery);
        await Assert.That((await CompletedAsync(control, 3)).OutputDisposition).IsNull();
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
    }

    [Test]
    [Arguments(false, "true")]
    [Arguments(false, "false")]
    [Arguments(false, "empty")]
    [Arguments(null, "true")]
    [Arguments(null, "false")]
    [Arguments(null, "empty")]
    public async Task DeepSeek_NonStreamingUsageOptionsAre400AndLeaveOriginalPolicyFixtureAvailableAsync(bool? streaming, string usageOption)
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, _deepSeekModel, LlmTestServerOutputDispositionKind.Filtered));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        var options = new JsonObject();
        if (usageOption != "empty") { options["include_usage"] = usageOption == "true"; }
        var body = new JsonObject
        {
            ["model"] = _deepSeekModel,
            ["stream_options"] = options,
            ["messages"] = JsonSerializer.SerializeToNode(new[] { new { role = "user", content = "policy" } }),
        };
        if (streaming is { } stream) { body["stream"] = stream; }
        using var rejected = await http.PostAsJsonAsync(_deepSeekPath, body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        var sdk = ChatSdk(http, "deepseek", _deepSeekModel);
        var first = (await sdk.CompleteChatAsync([new UserChatMessage("policy")])).Value;
        await Assert.That(first.FinishReason).IsEqualTo(ChatFinishReason.ContentFilter);
        await Assert.That(first.Content.Single().Text).IsEqualTo(_text);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(1);
        var generated = await CompletedAsync(control, 2);
        await Assert.That(generated.OutputDisposition!.Kind).IsEqualTo(LlmTestServerOutputDispositionKind.Filtered);
        var next = (await sdk.CompleteChatAsync([new UserChatMessage("next")])).Value;
        await Assert.That(next.Content.Single().Text).IsEqualTo(_recovery);
        await Assert.That((await CompletedAsync(control, 3)).OutputDisposition).IsNull();
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
        await Assert.That((await control.GetRequestsAsync()).TotalObserved).IsEqualTo(3);
    }

    [Test]
    public async Task DeepSeek_FilteredGraphReplaysAcrossJsonAndSseAndCommittedCacheIsRetainedAsync()
    {
        await using var host = await StartAsync(config => config.AddModel(_deepSeekModel, LlmTestServerModelKind.Chat)
            .AddConversation("deepseek-policy-replay", graph => graph.ForModel(_deepSeekModel)
                .Step("filtered", step => step.WhenLatestUserEquals("go").Responds(_text, "Visible ", "fixture text")
                    .WithOutputDisposition(new() { Kind = LlmTestServerOutputDispositionKind.Filtered }).Then("next"))
                .Step("next", step => step.WhenLatestUserEquals("next").Responds(_recovery).Complete())));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        using var conversation = control.ForConversation("deepseek-policy-replay");
        var sdk = ChatSdk(conversation.HttpClient, "deepseek", _deepSeekModel);
        global::OpenAI.Chat.ChatMessage[] prompt =
        [new SystemChatMessage(string.Join(" ", Enumerable.Repeat("DeepSeek cache fixture", 1200))), new UserChatMessage("go")];
        var first = (await sdk.CompleteChatAsync(prompt)).Value;
        await Assert.That(first.FinishReason).IsEqualTo(ChatFinishReason.ContentFilter);
        var replay = new StringBuilder(); ChatFinishReason? reason = null; ChatTokenUsage? replayUsage = null;
        await foreach (var update in sdk.CompleteChatStreamingAsync(prompt))
        {
            foreach (var part in update.ContentUpdate) { replay.Append(part.Text); }
            reason = update.FinishReason ?? reason;
            replayUsage = update.Usage ?? replayUsage;
        }
        await Assert.That(replay.ToString()).IsEqualTo(_text);
        await Assert.That(reason).IsEqualTo(ChatFinishReason.ContentFilter);
        await Assert.That(replayUsage!.TotalTokenCount).IsEqualTo(first.Usage.TotalTokenCount);
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        var replayTrace = await CompletedAsync(conversation.Control, 2);
        await Assert.That(replayTrace.IsReplay).IsTrue();
        await Assert.That(replayTrace.TerminalMarkerObserved).IsTrue();
        await Assert.That(replayTrace.OutputDisposition!.Kind).IsEqualTo(LlmTestServerOutputDispositionKind.Filtered);
        var recovered = (await sdk.CompleteChatAsync([.. prompt, new AssistantChatMessage(first), new UserChatMessage("next")])).Value;
        await Assert.That(recovered.Content.Single().Text).IsEqualTo(_recovery);
        var recoveryTrace = await CompletedAsync(conversation.Control, 3);
        await Assert.That(recoveryTrace.Usage!.CachedInputTokens).IsGreaterThan(0);
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().IsTerminal).IsTrue();
    }
}
