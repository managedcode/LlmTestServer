using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Conversations;

public sealed partial class ConversationHttpIntegrationTests
{
    [Test]
    public async Task ExplainNullToolCall_ReturnsBadRequestWithoutConsumingConversationAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversation("validation", graph => graph
            .Step("only", step => step.Responds("preserved answer").Complete())));
        var malformed = JsonNode.Parse("""
            {"providerNamespace":"/openai","request":{"modelId":"gpt-4.1-mini","messages":[
              {"role":"assistant","content":"","toolCalls":[null]}
            ]}}
            """);
        using var rejected = await http.PostAsJsonAsync("/admin-api/match/explain", malformed);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await root.GetConversationsAsync()).Count).IsEqualTo(0);
        await AssertChatAsync(http, [User("hello")], "preserved answer");
        await Assert.That((await root.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
    }

    [Test]
    public async Task ExplainFreshToolFixture_ReportsMissingToolsAndMatchesActualRejectionAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversation("tool-validation", graph => graph
            .Step("lookup", step => step.WhenLatestUserEquals("weather").CallsTool("weather", "{}", "call_weather").Complete())));
        using var conversation = root.ForConversation("fresh-tool-validation");
        var explanation = await conversation.Control.ExplainMatchAsync(Explain([User("weather")]));
        await Assert.That(explanation.SelectedStepId).IsNull();
        var candidate = explanation.Candidates.Single(item => item.ScenarioId == "tool-validation" && item.StepId == "lookup");
        await Assert.That(candidate.Mismatches).Contains("fixture.mismatch");
        await Assert.That(candidate.IsMatch).IsFalse();
        await Assert.That(candidate.IsSelected).IsFalse();
        await Assert.That((await root.GetConversationsAsync()).Count).IsEqualTo(0);
        await AssertErrorAsync(conversation.HttpClient, [User("weather")], "llm_test_server_fixture_mismatch");
        await Assert.That((await root.GetConversationsAsync()).Count).IsEqualTo(0);
        object[] tools = [new { type = "function", function = new { name = "weather", parameters = new { type = "object" } } }];
        using var accepted = await SendChatAsync(conversation.HttpClient, [User("weather")], tools: tools);
        accepted.EnsureSuccessStatusCode();
        var result = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(result.GetProperty("choices")[0].GetProperty("message").GetProperty("tool_calls")[0].GetProperty("id").GetString())
            .IsEqualTo("call_weather");
        await Assert.That((await root.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
    }

    [Test]
    public async Task ExplainOrderedBranches_DoesNotSkipFirstPredicateMatchWhenItsFixtureIsInvalidAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversation("ordered-validation", graph => graph
            .Step("start", step => step.WhenLatestUserEquals("begin").Responds("ready").Then("invalid", "valid"))
            .Step("invalid", step => step.WhenLatestUserEquals("continue").WhenTurn(2).CallsTool("weather", "{}").Complete())
            .Step("valid", step => step.WhenLatestUserEquals("continue").WhenTurn(2).Responds("must not skip ahead").Complete())));
        using var conversation = root.ForConversation("ordered-validation-42");
        await AssertChatAsync(conversation.HttpClient, [User("begin")], "ready");
        object[] messages = [User("begin"), Assistant("ready"), User("continue")];
        var explanation = await conversation.Control.ExplainMatchAsync(Explain(messages));
        await Assert.That(explanation.SelectedStepId).IsNull();
        var invalid = explanation.Candidates.Single(candidate => candidate.StepId == "invalid");
        await Assert.That(invalid.Mismatches).Contains("fixture.mismatch");
        await Assert.That(invalid.IsSelected).IsFalse();
        var shadowed = explanation.Candidates.Single(candidate => candidate.StepId == "valid");
        await Assert.That(shadowed.IsSelected).IsFalse();
        await Assert.That(shadowed.IsShadowed).IsTrue();
        await AssertErrorAsync(conversation.HttpClient, messages, "llm_test_server_fixture_mismatch");
        var state = (await root.GetConversationsAsync()).Single();
        await Assert.That(state.ExecutedSteps).IsEqualTo(1);
        await Assert.That(state.NextSteps).IsEquivalentTo(new[] { "invalid", "valid" });
    }

    [Test]
    [Arguments("nullContent")]
    [Arguments("nullChunk")]
    [Arguments("nullErrorCode")]
    [Arguments("nullErrorMessage")]
    [Arguments("zeroErrorStatus")]
    [Arguments("successfulErrorStatus")]
    [Arguments("outOfRangeErrorStatus")]
    public async Task InvalidStepResponse_RejectsConfigurationAndPreservesOldServerBehaviorAsync(string defect)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversation("valid", graph => graph
            .Step("only", step => step.Responds("valid response").Complete())));
        var config = JsonNode.Parse("""
            {"models":[{"id":"gpt-4.1-mini","kind":0}],"chatScenarios":[
              {"id":"invalid","modelId":"gpt-4.1-mini","conversation":{"startStepId":"only","steps":[
                {"id":"only","terminal":true,"response":{"content":"replacement"}}
              ]}}
            ]}
            """)!;
        var response = config["chatScenarios"]![0]!["conversation"]!["steps"]![0]!["response"]!;
        switch (defect)
        {
            case "nullContent": response["content"] = null; break;
            case "nullChunk": response["streamChunks"] = JsonNode.Parse("[null]"); break;
            case "nullErrorCode": response["error"] = JsonNode.Parse("""{"statusCode":503,"code":null,"message":"unavailable"}"""); break;
            case "nullErrorMessage": response["error"] = JsonNode.Parse("""{"statusCode":503,"code":"unavailable","message":null}"""); break;
            case "zeroErrorStatus": response["error"] = JsonNode.Parse("""{"statusCode":0,"code":"unavailable","message":"unavailable"}"""); break;
            case "successfulErrorStatus": response["error"] = JsonNode.Parse("""{"statusCode":200,"code":"unavailable","message":"unavailable"}"""); break;
            case "outOfRangeErrorStatus": response["error"] = JsonNode.Parse("""{"statusCode":600,"code":"unavailable","message":"unavailable"}"""); break;
        }
        using var rejected = await http.PostAsJsonAsync("/admin-api/configure", config);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await root.GetConversationsAsync()).Count).IsEqualTo(0);
        await AssertChatAsync(http, [User("still serving")], "valid response");
        await Assert.That((await root.GetConversationsAsync()).Single().ScenarioId).IsEqualTo("valid");
    }
}
