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
    public async Task ExplainUnauthorizedScenario_DoesNotSelectFallbackOrConsumeResponseAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config
            .UseConversation("protected", graph => graph.RequireBearerToken("scenario-key")
                .Step("only", step => step.WhenLatestUserEquals("hello").Responds("protected answer").Complete()))
            .UseChatScenario("fallback", scenario => scenario.Responds("fallback answer")));
        using var conversation = root.ForConversation("protected-42");
        var explanation = await conversation.Control.ExplainMatchAsync(Explain([User("hello")]));
        await Assert.That(explanation.SelectedScenarioId).IsEqualTo("protected");
        await Assert.That(explanation.SelectedStepId).IsNull();
        await Assert.That(explanation.Candidates.Single(candidate => candidate.ScenarioId == "protected").Mismatches)
            .Contains("authorization");
        await Assert.That(explanation.Candidates.Single(candidate => candidate.ScenarioId == "fallback").IsSelected).IsFalse();
        await Assert.That((await root.GetConversationsAsync()).Count).IsEqualTo(0);
        await AssertErrorAsync(conversation.HttpClient, [User("hello")], "invalid_api_key", HttpStatusCode.Unauthorized);
        conversation.HttpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "scenario-key");
        await AssertChatAsync(conversation.HttpClient, [User("hello")], "protected answer");
    }

    [Test]
    public async Task ExplainCompletedConversation_ReportsCompletedStateAndDoesNotRestartAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversation("completed", graph => graph.ReplaySuccessfulRequests(false)
            .Step("only", step => step.Responds("finished").Complete())));
        using var conversation = root.ForConversation("completed-42");
        await AssertChatAsync(conversation.HttpClient, [User("hello")], "finished");
        var next = new object[] { User("hello"), Assistant("finished"), User("continue") };
        var explanation = await conversation.Control.ExplainMatchAsync(Explain(next));
        await Assert.That(explanation.SelectedStepId).IsNull();
        await Assert.That(explanation.Candidates.Any(candidate => candidate.Mismatches.Contains("state.completed", StringComparer.Ordinal))).IsTrue();
        await Assert.That((await root.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        await AssertErrorAsync(conversation.HttpClient, next, "conversation_completed");
    }

    [Test]
    public async Task ExplainStoredContinuation_RestoresHistoryWithoutAdvancingConversationAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversation("continued", graph => graph
            .Step("first", step => step.WhenLatestUserEquals("begin").WhenTurn(1).Responds("ready").Then("last"))
            .Step("last", step => step.WhenLatestUserEquals("finish").WhenTurn(2).Responds("done").Complete())));
        using var conversation = root.ForConversation("response-chain");
        using var initial = await conversation.HttpClient.PostAsJsonAsync("/openai/v1/responses", new { model = "gpt-4.1-mini", input = "begin" });
        initial.EnsureSuccessStatusCode();
        var id = (await initial.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var request = new LlmTestServerMatchExplanationRequest
        {
            Request = new LlmTestServerChatRequest
            {
                PreviousResponseId = id,
                HistoryNamespace = "openai",
                Messages = [new LlmTestServerMessage { Role = "user", Content = "finish" }],
            },
        };
        var explanation = await conversation.Control.ExplainMatchAsync(request);
        await Assert.That(explanation.TurnIndex).IsEqualTo(2);
        await Assert.That(explanation.SelectedStepId).IsEqualTo("last");
        await Assert.That((await root.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        var repeated = await conversation.Control.ExplainMatchAsync(request);
        await Assert.That(repeated.SelectedStepId).IsEqualTo("last");
        using var final = await conversation.HttpClient.PostAsJsonAsync("/openai/v1/responses", new
        { model = "gpt-4.1-mini", input = "finish", previous_response_id = id });
        final.EnsureSuccessStatusCode();
        await Assert.That(await final.Content.ReadAsStringAsync()).Contains("done");
        await Assert.That((await root.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
    }
}
