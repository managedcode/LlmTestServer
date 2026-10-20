#pragma warning disable OPENAI001 // Exercise the official experimental Responses SDK through HTTP.
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeWebSearchIntegrationTests
{
    [Test]
    [Arguments(false, "low")]
    [Arguments(true, "low")]
    [Arguments(true, "medium")]
    [Arguments(true, "high")]
    public async Task JsonAndOfficialSdk_PreserveNativeSearchSourcesAndTypedCitationAsync(bool sources, string context)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(Configuration(2));
        var request = Request(sources);
        request["tools"]![0]!["search_context_size"] = context;
        await AssertWireAsync(await CompleteAsync(http, request), sources);
        await AssertSdkAsync((await Sdk(http).CreateResponseAsync(Options(sources, context))).Value, sources);
        var evidence = await control.GetRequestsAsync();
        await Assert.That(evidence.Requests.Count).IsEqualTo(2);
        await Assert.That(evidence.Requests.All(trace => trace.IsSuccess == true)).IsTrue();
        await Assert.That(evidence.Requests.All(trace => trace.ResponseToolCalls.Count == 0)).IsTrue();
        await Assert.That(evidence.Requests.All(trace => trace.NativeTools.Count == 1 && trace.ResponseOutputItems.Count == 2)).IsTrue();
        var exported = await control.ExportRequestsAsync();
        var exportedSearch = (LlmTestServerResponseWebSearchItem)exported.Evidence.Requests[0].ResponseOutputItems[0];
        await Assert.That(exportedSearch.Id).IsEqualTo(_searchId);
        await Assert.That(exportedSearch.Sources.Single().Url).IsEqualTo(_sourceUrl);
        var lines = (await control.ExportRequestsNdjsonAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using var record = JsonDocument.Parse(lines[1]);
        await Assert.That(record.RootElement.GetProperty("request").GetProperty("responseOutputItems")[0].GetProperty("kind").GetString()).IsEqualTo("web_search");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OfficialSdk_PreservesMixedFunctionNativeAndMessageOrderAsync(bool streaming)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var call = new LlmTestServerToolCall { Id = "call_mixed_fixture", Name = "weather", ArgumentsJson = "{}" };
        await control.ConfigureAsync(config => config.UseGpt41Mini().UseChatScenario("mixed-native", scenario => scenario.RespondsWithItems(
            new LlmTestServerResponseFunctionCallItem { Id = "fc_mixed_fixture", Call = call }, Items()[0], Items()[1])));
        var options = Options();
        options.ToolChoice = ResponseToolChoice.CreateAutoChoice();
        options.Tools.Add(ResponseTool.CreateFunctionTool("weather", BinaryData.FromString("{\"type\":\"object\"}"), false, "Scripted weather"));
        ResponseResult result;
        if (streaming)
        {
            options.StreamingEnabled = true;
            var updates = new List<StreamingResponseUpdate>();
            await foreach (var update in Sdk(http).CreateResponseStreamingAsync(options)) { updates.Add(update); }
            result = updates.OfType<StreamingResponseCompletedUpdate>().Single().Response;
            await Assert.That(updates.OfType<StreamingResponseWebSearchCallCompletedUpdate>().Single().OutputIndex).IsEqualTo(1);
            await Assert.That(updates.OfType<StreamingResponseFunctionCallArgumentsDeltaUpdate>().Count()).IsEqualTo(1);
        }
        else { result = (await Sdk(http).CreateResponseAsync(options)).Value; }
        await Assert.That(result.OutputItems.Count).IsEqualTo(3);
        var function = (FunctionCallResponseItem)result.OutputItems[0];
        await Assert.That(function.Id).IsEqualTo("fc_mixed_fixture");
        await Assert.That(function.FunctionName).IsEqualTo(call.Name);
        await Assert.That(function.CallId).IsEqualTo(call.Id);
        await Assert.That(function.FunctionArguments.ToString()).IsEqualTo(call.ArgumentsJson);
        await Assert.That(((WebSearchCallResponseItem)result.OutputItems[1]).Id).IsEqualTo(_searchId);
        await Assert.That(((MessageResponseItem)result.OutputItems[2]).Content.Single().Text).IsEqualTo(_answer);
        var trace = (await control.GetRequestsAsync()).Requests.Single();
        await Assert.That(trace.ResponseToolCalls.Single().Id).IsEqualTo(call.Id);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OfficialSdk_ExplicitAndStoredNativeHistoryAdvanceOneLogicalTurnAsync(bool stored)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseGpt41Mini().UseConversation("native-continuation", graph => graph
            .Step("search", step => step.WhenLatestUserEquals("start").WhenTurn(1).RespondsWithItems(Items()).Then("follow"))
            .Step("follow", step => step.WhenLatestUserEquals("follow").WhenTurn(2).Responds("continued native history").Complete())));
        using var conversation = control.ForConversation("native-continuation");
        var sdk = Sdk(conversation.HttpClient);
        var firstOptions = Options();
        firstOptions.StoredOutputEnabled = stored;
        var first = (await sdk.CreateResponseAsync(firstOptions)).Value;
        await AssertSdkAsync(first);
        var next = Options(input: "follow");
        if (stored) { next.PreviousResponseId = first.Id; }
        else
        {
            next.InputItems.Clear();
            next.InputItems.Add(ResponseItem.CreateUserMessageItem("start"));
            foreach (var item in first.OutputItems) { next.InputItems.Add(item); }
            next.InputItems.Add(ResponseItem.CreateUserMessageItem("follow"));
        }
        next.ToolChoice = ResponseToolChoice.CreateAutoChoice();
        var continued = (await sdk.CreateResponseAsync(next)).Value;
        await Assert.That(((MessageResponseItem)continued.OutputItems.Single()).Content.Single().Text).IsEqualTo("continued native history");
        var state = (await conversation.Control.GetConversationsAsync()).Single();
        await Assert.That(state.ExecutedSteps).IsEqualTo(2);
        var trace = (await conversation.Control.GetRequestsAsync()).Requests[0];
        await Assert.That(trace.StepId).IsEqualTo("follow");
        await Assert.That(trace.Messages.Count(message => message.Role == "user")).IsEqualTo(2);
        var priorSearch = trace.Messages.Where(message => message.Role == "assistant").SelectMany(message => message.OutputItems)
            .OfType<LlmTestServerResponseWebSearchItem>().Single();
        await Assert.That(priorSearch.Id).IsEqualTo(_searchId);
        await Assert.That(priorSearch.Queries.Single()).IsEqualTo(_query);
        await Assert.That(trace.Messages.Any(message => message.Role == "user" && message.Content.Contains("web_search_call", StringComparison.Ordinal))).IsFalse();
    }
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OfficialSdk_MixedNativeFunctionRoundRetainsOutputIdsAcrossContinuationAsync(bool stored)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var call = new LlmTestServerToolCall { Id = "call_native_weather", Name = "weather", ArgumentsJson = "{}" };
        await control.ConfigureAsync(config => config.UseGpt41Mini().UseConversation("mixed-native-history", graph => graph
            .Step("search", step => step.WhenLatestUserEquals("start").WhenTurn(1).RespondsWithItems(
                new LlmTestServerResponseFunctionCallItem { Id = "fc_native_weather", Call = call }, Items()[0], Items()[1]).Then("result"))
            .Step("result", step => step.WhenToolResult(call.Name, call.Id).WhenTurn(2)
                .WhenLatestTextContains("18 degrees").Responds("native search and weather complete").Complete())));
        using var conversation = control.ForConversation("mixed-native-history");
        var sdk = Sdk(conversation.HttpClient);
        var firstOptions = Options();
        firstOptions.StoredOutputEnabled = stored;
        firstOptions.ToolChoice = ResponseToolChoice.CreateAutoChoice();
        firstOptions.Tools.Add(ResponseTool.CreateFunctionTool("weather", BinaryData.FromString("{\"type\":\"object\"}"), false, "Scripted weather"));
        var first = (await sdk.CreateResponseAsync(firstOptions)).Value;
        await Assert.That(first.OutputItems.Count).IsEqualTo(3);
        await Assert.That(((FunctionCallResponseItem)first.OutputItems[0]).Id).IsEqualTo("fc_native_weather");
        await Assert.That(((WebSearchCallResponseItem)first.OutputItems[1]).Id).IsEqualTo(_searchId);
        var next = Options();
        next.ToolChoice = ResponseToolChoice.CreateAutoChoice();
        next.Tools.Add(ResponseTool.CreateFunctionTool("weather", BinaryData.FromString("{\"type\":\"object\"}"), false, "Scripted weather"));
        next.InputItems.Clear();
        if (stored) { next.PreviousResponseId = first.Id; }
        else
        {
            next.InputItems.Add(ResponseItem.CreateUserMessageItem("start"));
            foreach (var item in first.OutputItems) { next.InputItems.Add(item); }
        }
        next.InputItems.Add(ResponseItem.CreateFunctionCallOutputItem(call.Id, "18 degrees"));
        var final = (await sdk.CreateResponseAsync(next)).Value;
        await Assert.That(((MessageResponseItem)final.OutputItems.Single()).Content.Single().Text).IsEqualTo("native search and weather complete");
        var state = (await conversation.Control.GetConversationsAsync()).Single();
        await Assert.That(state.ExecutedSteps).IsEqualTo(2);
        var trace = (await conversation.Control.GetRequestsAsync()).Requests[0];
        var historical = trace.Messages.Where(message => message.Role == "assistant").SelectMany(message => message.OutputItems).ToArray();
        await Assert.That(historical.Length).IsEqualTo(3);
        await Assert.That(((LlmTestServerResponseFunctionCallItem)historical[0]).Id).IsEqualTo("fc_native_weather");
        await Assert.That(((LlmTestServerResponseFunctionCallItem)historical[0]).Call.Id).IsEqualTo(call.Id);
        await Assert.That(((LlmTestServerResponseWebSearchItem)historical[1]).Id).IsEqualTo(_searchId);
        await Assert.That(((LlmTestServerResponseMessageItem)historical[2]).Id).IsEqualTo(_messageId);
        await Assert.That(trace.Messages.Single(message => message.Role == "tool").ToolCallId).IsEqualTo(call.Id);
    }
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OfficialSdk_ExplicitNativeHistoryPreservesUncitedAndEmptyMessageIdentityAsync(bool empty)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var text = empty ? "" : "Uncited fixture answer.";
        await control.ConfigureAsync(config => config.UseGpt41Mini().UseConversation("uncited-native-history", graph => graph
            .Step("search", step => step.WhenLatestUserEquals("start").WhenTurn(1).RespondsWithItems(Items()[0],
                new LlmTestServerResponseMessageItem { Id = _messageId, Content = text }).Then("follow"))
            .Step("follow", step => step.WhenLatestUserEquals("follow").WhenTurn(2).Responds("continued uncited history").Complete())));
        using var conversation = control.ForConversation("uncited-native-history");
        var sdk = Sdk(conversation.HttpClient);
        var first = (await sdk.CreateResponseAsync(Options())).Value;
        var firstMessage = (MessageResponseItem)first.OutputItems[1];
        await Assert.That(firstMessage.Id).IsEqualTo(_messageId);
        await Assert.That(firstMessage.Content.Single().Text).IsEqualTo(text);
        await Assert.That(firstMessage.Content.Single().OutputTextAnnotations.Count).IsEqualTo(0);
        var next = Options();
        next.ToolChoice = ResponseToolChoice.CreateAutoChoice();
        next.InputItems.Clear();
        next.InputItems.Add(ResponseItem.CreateUserMessageItem("start"));
        foreach (var item in first.OutputItems) { next.InputItems.Add(item); }
        next.InputItems.Add(ResponseItem.CreateUserMessageItem("follow"));
        var final = (await sdk.CreateResponseAsync(next)).Value;
        await Assert.That(((MessageResponseItem)final.OutputItems.Single()).Content.Single().Text).IsEqualTo("continued uncited history");
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
        var trace = (await conversation.Control.GetRequestsAsync()).Requests[0];
        await Assert.That(trace.Messages.Count(message => message.Role == "user")).IsEqualTo(2);
        var historical = trace.Messages.Where(message => message.Role == "assistant").SelectMany(message => message.OutputItems)
            .OfType<LlmTestServerResponseMessageItem>().Single();
        await Assert.That(historical.Id).IsEqualTo(_messageId);
        await Assert.That(historical.Content).IsEqualTo(text);
        await Assert.That(historical.Citations.Count).IsEqualTo(0);
    }
}

#pragma warning restore OPENAI001
