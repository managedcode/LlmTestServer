using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Conversations;

public sealed partial class ConversationHttpIntegrationTests
{
    [Test]
    [Arguments(0, 1000000)]
    [Arguments(4, 0)]
    [Arguments(4, 1024)]
    public async Task ConversationCapacity_RejectsWithoutCreatingPartialStateAsync(int count, long bytes)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversationCapacity(count, bytes).UseConversation("capacity", graph => graph
            .Step("only", step => step.Responds("accepted").Complete())));
        using var conversation = root.ForConversation("capacity-42");
        await AssertErrorAsync(conversation.HttpClient, [User("hello")], "conversation_capacity_exceeded");
        await AssertErrorAsync(conversation.HttpClient, [User("hello")], "conversation_capacity_exceeded");
        await Assert.That((await root.GetConversationsAsync()).Count).IsEqualTo(0);
        await root.ConfigureAsync(config => config.UseConversationCapacity(4, 1000000).UseConversation("capacity", graph => graph
            .Step("only", step => step.Responds("accepted").Complete())));
        await AssertChatAsync(conversation.HttpClient, [User("hello")], "accepted");
    }

    [Test]
    public async Task ConversationCountLimit_PreservesExistingStateAndConfigureReleasesCapacityAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversationCapacity(1, 1000000).UseConversation("bounded", graph => graph
            .Step("first", step => step.Responds("first answer").Then("second"))
            .Step("second", step => step.Responds("second answer").Complete())));
        using var first = root.ForConversation("first");
        using var second = root.ForConversation("second");
        await AssertChatAsync(first.HttpClient, [User("hello")], "first answer");
        await AssertErrorAsync(second.HttpClient, [User("hello")], "conversation_capacity_exceeded");
        await AssertChatAsync(first.HttpClient, [User("hello"), Assistant("first answer"), User("continue")], "second answer");
        await Assert.That((await root.GetConversationsAsync()).Single().ConversationId).IsEqualTo("first");
        await root.ConfigureAsync(config => config.UseConversationCapacity(1, 1000000).UseConversation("bounded", graph => graph
            .Step("only", step => step.Responds("configured again").Complete())));
        await Assert.That((await root.GetConversationsAsync()).Count).IsEqualTo(0);
        await AssertChatAsync(second.HttpClient, [User("hello")], "configured again");
    }

    [Test]
    [Arguments("missingStart")]
    [Arguments("duplicateStep")]
    [Arguments("unknownNext")]
    [Arguments("duplicateNext")]
    [Arguments("terminalAndNext")]
    [Arguments("nonterminalNoNext")]
    [Arguments("emptySteps")]
    [Arguments("negativeSteps")]
    [Arguments("tooManyExecutions")]
    [Arguments("unknownExhaustion")]
    [Arguments("invalidRole")]
    [Arguments("zeroTurn")]
    [Arguments("invalidArguments")]
    [Arguments("invalidPointer")]
    [Arguments("invalidPath")]
    [Arguments("invalidExpectedJson")]
    [Arguments("mixedResponseQueue")]
    [Arguments("longScenarioId")]
    [Arguments("longStepId")]
    [Arguments("emptyStepId")]
    [Arguments("negativeCapacity")]
    [Arguments("negativeBytes")]
    public async Task MalformedConversation_RejectsConfigurationAndPreservesServingFixtureAsync(string defect)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversation("preserved", graph => graph
            .Step("only", step => step.Responds("preserved response").Complete())));
        var config = JsonNode.Parse("""
            {"models":[{"id":"gpt-4.1-mini","kind":0}],"chatScenarios":[
              {"id":"invalid","modelId":"gpt-4.1-mini","conversation":{"startStepId":"only","maxSteps":64,"steps":[
                {"id":"only","terminal":true,"response":{"content":"invalid replacement"},"match":{},"nextSteps":[]}
              ]}}
            ]}
            """)!.AsObject();
        var scenario = config["chatScenarios"]![0]!;
        var graph = scenario["conversation"]!;
        var step = graph["steps"]![0]!;
        switch (defect)
        {
            case "missingStart": graph["startStepId"] = "missing"; break;
            case "duplicateStep": graph["steps"]!.AsArray().Add(step.DeepClone()); break;
            case "unknownNext": step["terminal"] = false; step["nextSteps"] = new JsonArray("missing"); break;
            case "duplicateNext": step["terminal"] = false; step["nextSteps"] = new JsonArray("only", "only"); break;
            case "terminalAndNext": step["nextSteps"] = new JsonArray("only"); break;
            case "nonterminalNoNext": step["terminal"] = false; break;
            case "emptySteps": graph["steps"] = new JsonArray(); break;
            case "negativeSteps": graph["maxSteps"] = -1; break;
            case "tooManyExecutions": graph["maxSteps"] = 4097; break;
            case "unknownExhaustion": graph["exhaustion"] = 99; break;
            case "invalidRole": step["match"]!["latestRole"] = "banana"; break;
            case "zeroTurn": step["match"]!["turnIndex"] = 0; break;
            case "invalidArguments": step["match"]!["toolArgumentsJson"] = "{"; break;
            case "invalidPointer": step["match"]!["jsonFields"] = JsonNode.Parse("""[{"path":"/bad~2escape","equalsJson":"null"}]"""); break;
            case "invalidPath": step["match"]!["jsonFields"] = JsonNode.Parse("""[{"path":"$..unsupported","equalsJson":"null"}]"""); break;
            case "invalidExpectedJson": step["match"]!["jsonFields"] = JsonNode.Parse("""[{"path":"$.value","equalsJson":"{"}]"""); break;
            case "mixedResponseQueue": scenario["responses"] = JsonNode.Parse("""[{"content":"mixed"}]"""); break;
            case "longScenarioId": scenario["id"] = new string('s', 129); break;
            case "longStepId": step["id"] = new string('s', 129); break;
            case "emptyStepId": step["id"] = ""; break;
            case "negativeCapacity": config["maxConversations"] = -1; break;
            case "negativeBytes": config["maxConversationBytes"] = -1; break;
        }
        using var rejected = await http.PostAsJsonAsync("/admin-api/configure", config);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertChatAsync(http, [User("still available")], "preserved response");
        await Assert.That((await root.GetConversationsAsync()).Single().ScenarioId).IsEqualTo("preserved");
    }
}
