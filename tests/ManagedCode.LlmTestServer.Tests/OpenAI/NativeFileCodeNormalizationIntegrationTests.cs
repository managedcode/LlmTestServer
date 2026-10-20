using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeFileCodeIntegrationTests
{
    [Test]
    [Arguments(false, "\"ordinary part\"", "ordinary part")]
    [Arguments(false, "null", "")]
    [Arguments(false, "123", "123")]
    [Arguments(true, "\"ordinary part\"", "ordinary part")]
    [Arguments(true, "null", "")]
    [Arguments(true, "123", "123")]
    public async Task NativeHistory_OrdinaryAssistantScalarPartsPreserveTextAndOneLogicalTurnAsync(bool code, string part, string expected)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseChatModel(Model(code)).UseConversation("native-normalization", graph => graph.ForModel(Model(code))
            .Step("native", step => step.WhenLatestUserEquals("start").WhenTurn(1).RespondsWithItems(Items(code)).Then("follow"))
            .Step("follow", step => step.WhenLatestUserEquals("follow").WhenTurn(2).Responds("normalized follow-up").Complete())));
        var first = await CompleteAsync(http, code);
        var input = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "start" });
        foreach (var item in first.GetProperty("output").EnumerateArray()) { input.Add(JsonNode.Parse(item.GetRawText())); }
        input.Add(new JsonObject { ["role"] = "assistant", ["content"] = new JsonArray(JsonNode.Parse(part)) });
        input.Add(new JsonObject { ["role"] = "user", ["content"] = "follow" });
        var body = Request(code); body["input"] = input; body["tool_choice"] = "auto";
        var final = await CompleteAsync(http, code, body);
        await Assert.That(final.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString()).IsEqualTo("normalized follow-up");
        await Assert.That((await control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
        var trace = (await control.GetRequestsAsync()).Requests[0];
        await Assert.That(trace.Messages.Single(message => message.Role == "assistant").Content).IsEqualTo(_answer + expected);
    }

    [Test]
    [Arguments(false, "123")]
    [Arguments(false, "{}")]
    [Arguments(false, "true")]
    [Arguments(false, "[]")]
    [Arguments(true, "123")]
    [Arguments(true, "{}")]
    [Arguments(true, "true")]
    [Arguments(true, "[]")]
    public async Task OrdinaryAssistantOrFunctionNonStringIds_Return400WithoutConsumingFixtureAsync(bool function, string id)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration(false));
        var item = function ? OrdinaryFunction(JsonNode.Parse(id)) : new JsonObject
        { ["role"] = "assistant", ["id"] = JsonNode.Parse(id), ["content"] = "ordinary assistant" };
        var body = Request(false); body["input"] = new JsonArray(item, new JsonObject { ["role"] = "user", ["content"] = "follow" });
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await CompleteAsync(http, false);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryLongStringIds_StayOutsideNativeGroupSizeRestrictionsAsync(bool function)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(Configuration(false));
        var id = new string('x', 300);
        var item = function ? OrdinaryFunction(JsonValue.Create(id)) : new JsonObject
        { ["role"] = "assistant", ["id"] = id, ["content"] = "ordinary assistant" };
        var body = Request(false); body["input"] = new JsonArray(item, new JsonObject { ["role"] = "user", ["content"] = "follow" });
        await CompleteAsync(http, false, body);
        var trace = (await control.GetRequestsAsync()).Requests.Single();
        if (function) { await Assert.That(trace.Messages.Single(message => message.Role == "assistant").ToolCalls.Single().Id).IsEqualTo("call_ordinary"); }
        else { await Assert.That(trace.Messages.Single(message => message.Role == "assistant").Content).IsEqualTo("ordinary assistant"); }
    }

    [Test]
    public async Task OrdinaryFunctionNullId_UsesExistingFunctionCorrelationWithoutNativeGroupRestrictionAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(Configuration(false));
        var body = Request(false); body["input"] = new JsonArray(OrdinaryFunction(null), new JsonObject { ["role"] = "user", ["content"] = "follow" });
        await CompleteAsync(http, false, body);
        var trace = (await control.GetRequestsAsync()).Requests.Single();
        await Assert.That(trace.Messages.Single(message => message.Role == "assistant").ToolCalls.Single().Id).IsEqualTo("call_ordinary");
    }

    private static JsonObject OrdinaryFunction(JsonNode? id)
    {
        return new()
        { ["type"] = "function_call", ["id"] = id, ["call_id"] = "call_ordinary", ["name"] = "client_fixture", ["arguments"] = "{}" };
    }
}
