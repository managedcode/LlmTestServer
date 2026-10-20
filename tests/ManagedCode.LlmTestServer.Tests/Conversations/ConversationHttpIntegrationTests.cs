using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Conversations;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Conversations;

public sealed partial class ConversationHttpIntegrationTests
{
    [Test]
    [Arguments("approve", "approved", "cancel")]
    [Arguments("cancel", "cancelled", "approve")]
    public async Task Branches_InspectLatestUserInsteadOfHistoricalApprovalAsync(string action, string answer, string unvisited)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversation("order", graph => graph
            .Step("review", step => step.WhenLatestUserEquals("review").WhenTurn(1).Responds("choose").Then("approve", "cancel"))
            .Step("approve", step => step.WhenLatestUserEquals("approve").WhenTurn(2).Responds("approved").Complete())
            .Step("cancel", step => step.WhenLatestUserEquals("cancel").WhenTurn(2).Responds("cancelled").Complete())));
        using var conversation = root.ForConversation("order-42");
        var initial = new object[] { User("approve from a historical message"), User("review") };
        await AssertChatAsync(conversation.HttpClient, initial, "choose");
        var messages = new object[] { User("approve"), User("review"), Assistant("choose"), User(action) };
        var explanation = await conversation.Control.ExplainMatchAsync(Explain(messages));
        await Assert.That(explanation.SelectedStepId).IsEqualTo(action);
        await Assert.That(explanation.TurnIndex).IsEqualTo(2);
        await Assert.That((await root.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        await AssertChatAsync(conversation.HttpClient, messages, answer);
        var state = (await root.GetConversationsAsync()).Single();
        await Assert.That(state.ConversationId).IsEqualTo("order-42");
        await Assert.That(state.IsTerminal).IsTrue();
        await Assert.That(state.ExecutedSteps).IsEqualTo(2);
        await Assert.That(state.UnvisitedSteps).IsEquivalentTo(new[] { unvisited });
        await Assert.That(state.NextSteps.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SuccessfulRetry_ReplaysWithoutAdvancingAndRejectsChangedStaleTurnAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversation("retry", graph => graph
            .Step("start", step => step.WhenLatestUserEquals("begin").Responds("ready", "rea", "dy").Then("finish"))
            .Step("finish", step => step.WhenLatestUserEquals("finish").Responds("done").Complete())));
        using var conversation = root.ForConversation("retry-safe");
        var first = new object[] { User("begin") };
        await AssertChatAsync(conversation.HttpClient, first, "ready");
        using var replay = await SendChatAsync(conversation.HttpClient, first, stream: true);
        replay.EnsureSuccessStatusCode();
        var events = await replay.Content.ReadAsStringAsync();
        await Assert.That(events).Contains("rea");
        await Assert.That(events).Contains("dy");
        await Assert.That(events).Contains("[DONE]");
        await Assert.That((await root.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        await AssertErrorAsync(conversation.HttpClient, [User("finish")], "conversation_turn_not_advanced");
        await AssertChatAsync(conversation.HttpClient, [User("begin"), Assistant("ready"), User("finish")], "done");
        await Assert.That((await root.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
    }

    [Test]
    public async Task ScriptedFailure_TransitionsToRecoveryWithoutRequiringAssistantTurnAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversation("recover", graph => graph
            .Step("unavailable", step => step.WhenLatestUserEquals("hello").WhenTurn(1)
                .Fails(503, "fixture_unavailable", "Retry the same request").Then("recovered"))
            .Step("recovered", step => step.WhenLatestUserEquals("hello").WhenTurn(1).Responds("available").Complete())));
        using var conversation = root.ForConversation("recover-42");
        await AssertErrorAsync(conversation.HttpClient, [User("hello")], "fixture_unavailable", HttpStatusCode.ServiceUnavailable);
        var failed = (await root.GetConversationsAsync()).Single();
        await Assert.That(failed.ExecutedSteps).IsEqualTo(1);
        await Assert.That(failed.LastTurnIndex).IsEqualTo(1);
        await Assert.That(failed.NextSteps).IsEquivalentTo(new[] { "recovered" });
        await AssertChatAsync(conversation.HttpClient, [User("hello")], "available");
        var recovered = (await root.GetConversationsAsync()).Single();
        await Assert.That(recovered.ExecutedSteps).IsEqualTo(2);
        await Assert.That(recovered.LastTurnIndex).IsEqualTo(1);
    }

    [Test]
    [Arguments(LlmTestServerConversationExhaustion.Reject)]
    [Arguments(LlmTestServerConversationExhaustion.RepeatLastResponse)]
    [Arguments(LlmTestServerConversationExhaustion.Restart)]
    public async Task TerminalPolicy_ControlsRequestsAfterCompletionAsync(LlmTestServerConversationExhaustion policy)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversation("terminal", graph => graph
            .ReplaySuccessfulRequests(false).OnExhaustion(policy)
            .Step("only", step => step.WhenLatestUserEquals("begin").Responds("completed").Complete())));
        using var conversation = root.ForConversation("terminal-42");
        await AssertChatAsync(conversation.HttpClient, [User("begin")], "completed");
        object[] next = [User("begin"), Assistant("completed"), User(policy == LlmTestServerConversationExhaustion.Restart ? "begin" : "different")];
        if (policy == LlmTestServerConversationExhaustion.Reject)
        { await AssertErrorAsync(conversation.HttpClient, next, "conversation_completed"); }
        else
        { await AssertChatAsync(conversation.HttpClient, next, "completed"); }
        var state = (await root.GetConversationsAsync()).Single();
        await Assert.That(state.ExecutedSteps).IsEqualTo(1);
        await Assert.That(state.IsTerminal).IsTrue();
        await Assert.That(state.LastTurnIndex).IsEqualTo(policy == LlmTestServerConversationExhaustion.Restart ? 2 : 1);
    }

    [Test]
    public async Task CyclicConversation_StopsAtConfiguredExecutionBudgetWithoutConsumingRejectedTurnAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversation("cycle", graph => graph.WithMaxSteps(2)
            .Step("loop", step => step.WhenLatestUserContains("next").Responds("again").Then("loop"))));
        using var conversation = root.ForConversation("cycle-42");
        await AssertChatAsync(conversation.HttpClient, [User("next one")], "again");
        await AssertChatAsync(conversation.HttpClient, [User("next one"), Assistant("again"), User("next two")], "again");
        object[] exhausted = [User("next one"), Assistant("again"), User("next two"), Assistant("again"), User("next three")];
        await AssertErrorAsync(conversation.HttpClient, exhausted, "conversation_exhausted");
        await AssertErrorAsync(conversation.HttpClient, exhausted, "conversation_exhausted");
        await Assert.That((await root.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
        await root.ResetAsync();
        await Assert.That((await root.GetConversationsAsync()).Count).IsEqualTo(0);
        await AssertChatAsync(conversation.HttpClient, [User("next fresh")], "again");
    }

    private static object User(string content)
    {
        return new { role = "user", content };
    }

    private static object Assistant(string content)
    {
        return new { role = "assistant", content };
    }

    private static Task<HttpResponseMessage> SendChatAsync(HttpClient http, object[] messages, bool stream = false, object[]? tools = null,
        object? responseFormat = null)
    {
        var body = new Dictionary<string, object?> { ["model"] = "gpt-4.1-mini", ["messages"] = messages, ["stream"] = stream };
        if (tools is not null) { body["tools"] = tools; }
        if (responseFormat is not null) { body["response_format"] = responseFormat; }
        return http.PostAsJsonAsync("/openai/v1/chat/completions", body);
    }

    private static async Task AssertChatAsync(HttpClient http, object[] messages, string content, object[]? tools = null)
    {
        using var response = await SendChatAsync(http, messages, tools: tools);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()).IsEqualTo(content);
    }

    private static async Task AssertErrorAsync(HttpClient http, object[] messages, string code,
        HttpStatusCode status = HttpStatusCode.Conflict, object[]? tools = null)
    {
        using var response = await SendChatAsync(http, messages, tools: tools);
        await Assert.That(response.StatusCode).IsEqualTo(status);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(json.GetProperty("error").GetProperty("code").GetString()).IsEqualTo(code);
    }

    private static LlmTestServerMatchExplanationRequest Explain(object[] messages)
    {
        return new()
        {
            Request = new LlmTestServerChatRequest
            {
                Messages = messages.Select(message =>
                {
                    var value = JsonSerializer.SerializeToElement(message);
                    return new LlmTestServerMessage { Role = value.GetProperty("role").GetString()!, Content = value.GetProperty("content").GetString()! };
                }).ToList(),
            },
        };
    }
}
