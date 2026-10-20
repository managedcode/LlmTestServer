#pragma warning disable OPENAI001 // Exercise official Responses SDK continuation through HTTP.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeFileCodeIntegrationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task OfficialSdk_MixedFunctionRoundPreservesNativeHistoryAndResourceIdsAsync(bool code, bool stored)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var call = new LlmTestServerToolCall { Id = "call_file_code", Name = "client_fixture", ArgumentsJson = "{}" };
        await control.ConfigureAsync(config => config.UseChatModel(Model(code)).UseConversation("file-code-history", graph => graph.ForModel(Model(code))
            .Step("native", step => step.WhenLatestUserEquals("start").WhenTurn(1).RespondsWithItems(
                new LlmTestServerResponseFunctionCallItem { Id = "fc_file_code", Call = call }, Items(code)[0], Items(code)[1]).Then("result"))
            .Step("result", step => step.WhenToolResult(call.Name, call.Id).WhenTurn(2).WhenLatestTextContains("scripted client result")
                .Responds("complete native client round").Complete())));
        using var conversation = control.ForConversation("file-code-history");
        var sdk = Sdk(conversation.HttpClient);
        var initial = Options(code); initial.ToolChoice = ResponseToolChoice.CreateAutoChoice(); initial.StoredOutputEnabled = stored;
        initial.Tools.Add(ResponseTool.CreateFunctionTool(call.Name, BinaryData.FromString("{\"type\":\"object\"}"), false, "Client fixture"));
        var first = (await sdk.CreateResponseAsync(initial)).Value;
        await Assert.That(((FunctionCallResponseItem)first.OutputItems[0]).Id).IsEqualTo("fc_file_code");
        await Assert.That(first.OutputItems[1].Id).IsEqualTo(ItemId(code));
        var next = Options(code); next.ToolChoice = ResponseToolChoice.CreateAutoChoice(); next.InputItems.Clear();
        next.Tools.Add(ResponseTool.CreateFunctionTool(call.Name, BinaryData.FromString("{\"type\":\"object\"}"), false, "Client fixture"));
        if (stored) { next.PreviousResponseId = first.Id; }
        else
        {
            next.InputItems.Add(ResponseItem.CreateUserMessageItem("start"));
            foreach (var item in first.OutputItems) { next.InputItems.Add(item); }
        }
        next.InputItems.Add(ResponseItem.CreateFunctionCallOutputItem(call.Id, "scripted client result"));
        var final = (await sdk.CreateResponseAsync(next)).Value;
        await Assert.That(((MessageResponseItem)final.OutputItems.Single()).Content.Single().Text).IsEqualTo("complete native client round");
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
        var trace = (await conversation.Control.GetRequestsAsync()).Requests[0];
        var historical = trace.Messages.Where(message => message.Role == "assistant").SelectMany(message => message.OutputItems).ToArray();
        await Assert.That(historical.Length).IsEqualTo(3);
        await Assert.That(((LlmTestServerResponseFunctionCallItem)historical[0]).Id).IsEqualTo("fc_file_code");
        await Assert.That(historical[1] is LlmTestServerResponseCodeInterpreterItem).IsEqualTo(code);
        await Assert.That(trace.Messages.Count(message => message.Role == "user")).IsEqualTo(1);
        await Assert.That(trace.Messages.Single(message => message.Role == "tool").ToolCallId).IsEqualTo(call.Id);
    }

    [Test]
    [Arguments(false, "credential")]
    [Arguments(false, "provider")]
    [Arguments(false, "session")]
    [Arguments(false, "reset")]
    [Arguments(true, "credential")]
    [Arguments(true, "provider")]
    [Arguments(true, "session")]
    [Arguments(true, "reset")]
    public async Task NativeHistory_RespectsCredentialProviderSessionAndResetBoundariesAsync(bool code, string boundary)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(Configuration(code, 2));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "first-key");
        var first = await CompleteAsync(http, code);
        var follow = new JsonObject { ["model"] = Model(code), ["input"] = "follow", ["previous_response_id"] = first.GetProperty("id").GetString() };
        if (boundary == "credential") { http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "second-key"); }
        if (boundary == "reset") { await control.ResetAsync(); }
        var path = boundary == "provider" ? "/azure-openai/openai/v1/responses" : "/openai/v1/responses";
        if (boundary == "session")
        {
            await using var session = await control.CreateSessionAsync(config => config.UseChatModel(Model(code))
                .UseChatScenario("session-native-file-code", scenario => scenario.ForModel(Model(code)).RespondsWithItems(Items(code))));
            using var rejected = await session.HttpClient.PostAsJsonAsync(path, follow);
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await CompleteAsync(session.HttpClient, code);
        }
        else
        {
            using var rejected = await http.PostAsJsonAsync(path, follow);
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That((await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString()).IsEqualTo("response_not_found");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "first-key");
            await CompleteAsync(http, code);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeGraphReplay_PreservesResourceAndCitationIdentityAsync(bool code)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseChatModel(Model(code)).UseConversation("file-code-replay", graph => graph.ForModel(Model(code))
            .Step("native", step => step.RespondsWithItems(Items(code)).Complete())));
        var first = await CompleteAsync(http, code);
        var second = await CompleteAsync(http, code);
        await Assert.That(first.GetProperty("output").GetRawText()).IsEqualTo(second.GetProperty("output").GetRawText());
        await Assert.That(first.GetProperty("id").GetString()).IsEqualTo(second.GetProperty("id").GetString());
        await Assert.That((await control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        await Assert.That((await control.GetRequestsAsync()).Requests.Count(trace => trace.IsReplay)).IsEqualTo(1);
    }
}
#pragma warning restore OPENAI001
