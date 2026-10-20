using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.Conversations;

#pragma warning disable OPENAI001 // Exercise the official experimental Responses SDK through HTTP.
public sealed class ConversationSdkIntegrationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task OfficialChatSdk_CompletesToolResultGraphAsync(bool azure, bool streaming)
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddConversation("weather", graph => graph
            .Step("lookup", step => step.WhenLatestUserEquals("weather").WhenTurn(1)
                .CallsTool("weather", """{"city":"Paris"}""", "call_weather").Then("result"))
            .Step("result", step => step.WhenToolResult("weather", "call_weather").WhenTurn(2)
                .WithToolArgumentsJson("""{"city":"Paris"}""").WhenLatestJsonValue("$.temperature", 22)
                .Responds("Paris is 22 degrees", "Paris is ", "22 degrees").Complete())));
        using var http = host.GetTestClient();
        using var conversation = new LlmTestServerClient(http).ForConversation("sdk-weather");
        var transport = new HttpClientPipelineTransport(conversation.HttpClient);
        var sdk = azure
            ? new global::Azure.AI.OpenAI.AzureOpenAIClient(new Uri(http.BaseAddress!, "/azure-openai"),
                new global::Azure.AzureKeyCredential("provider-key"), new() { Transport = transport }).GetChatClient(LlmTestServerKnownModelIds.Gpt41Mini)
            : new global::OpenAI.Chat.ChatClient(LlmTestServerKnownModelIds.Gpt41Mini, new ApiKeyCredential("provider-key"),
                new OpenAIClientOptions { Endpoint = new Uri(http.BaseAddress!, "/openai/v1"), Transport = transport });
        var options = new ChatCompletionOptions
        {
            Tools = { ChatTool.CreateFunctionTool("weather", functionParameters: BinaryData.FromString("""{"type":"object","properties":{"city":{"type":"string"}}}""")) },
        };
        List<global::OpenAI.Chat.ChatMessage> messages = [new UserChatMessage("weather")];
        var first = (await sdk.CompleteChatAsync(messages, options)).Value;
        var call = first.ToolCalls.Single();
        await Assert.That(call.Id).IsEqualTo("call_weather");
        await Assert.That(call.FunctionArguments.ToString()).IsEqualTo("""{"city":"Paris"}""");
        messages.Add(new AssistantChatMessage(first));
        messages.Add(new ToolChatMessage(call.Id, """{"temperature":22}"""));
        string result;
        if (streaming)
        {
            result = string.Empty;
            await foreach (var update in sdk.CompleteChatStreamingAsync(messages, options))
            { result += string.Concat(update.ContentUpdate.Select(part => part.Text)); }
        }
        else { result = (await sdk.CompleteChatAsync(messages, options)).Value.Content[0].Text; }
        await Assert.That(result).IsEqualTo("Paris is 22 degrees");
        var state = (await conversation.Control.GetConversationsAsync()).Single();
        await Assert.That(state.ExecutedSteps).IsEqualTo(2);
        await Assert.That(state.IsTerminal).IsTrue();
        var events = (await conversation.Control.GetAssertionsAsync()).Events;
        await Assert.That(events.Select(item => item.StepId)).IsEquivalentTo(new string?[] { "lookup", "result" });
        await Assert.That(events.Select(item => item.TurnIndex)).IsEquivalentTo(new int?[] { 1, 2 });
    }

    [Test]
    [Arguments("/openai/v1/", false)]
    [Arguments("/openai/v1/", true)]
    [Arguments("/azure-openai/openai/v1/", false)]
    [Arguments("/azure-openai/openai/v1/", true)]
    [Arguments("/microsoft-foundry/openai/v1/", false)]
    [Arguments("/microsoft-foundry/openai/v1/", true)]
    public async Task OfficialResponsesSdk_RestoresToolHistoryBeforeGraphMatchingAsync(string endpoint, bool streaming)
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddConversation("document", graph => graph
            .Step("read", step => step.WhenLatestUserEquals("review document").WhenTurn(1)
                .CallsTool("read_document", "{}", "call_document").Then("review"))
            .Step("review", step => step.WhenToolResult("read_document", "call_document").WhenTurn(2)
                .WhenLatestTextContains("verified evidence").Responds("review complete", "review ", "complete").Complete())));
        using var http = host.GetTestClient();
        using var conversation = new LlmTestServerClient(http).ForConversation("responses-tool");
        var sdk = new ResponsesClient(new ApiKeyCredential("provider-key"), new ResponsesClientOptions
        { Endpoint = new Uri(http.BaseAddress!, endpoint), Transport = new HttpClientPipelineTransport(conversation.HttpClient) });
        using var client = sdk.AsIChatClient(LlmTestServerKnownModelIds.Gpt41Mini).AsBuilder().UseFunctionInvocation().Build();
        var invocations = 0;
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(() => { invocations++; return "verified evidence"; }, "read_document")] };
        var response = streaming
            ? await client.GetStreamingResponseAsync("review document", options).ToChatResponseAsync()
            : await client.GetResponseAsync("review document", options);
        await Assert.That(response.Text).IsEqualTo("review complete");
        await Assert.That(invocations).IsEqualTo(1);
        var states = await conversation.Control.GetConversationsAsync();
        await Assert.That(states.Single().ExecutedSteps).IsEqualTo(2);
        var events = (await conversation.Control.GetAssertionsAsync()).Events;
        await Assert.That(events.Last().Messages.Any(message => message.Role == "user" && message.Content == "review document")).IsTrue();
        await Assert.That(events.Last().Messages.Single(message => message.Role == "tool").Content).Contains("verified evidence");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ResponsesContinuation_ReplaysLatestLogicalTurnAcrossJsonAndSseAsync(bool replayStreaming)
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddConversation("continuation", graph => graph
            .Step("first", step => step.WhenLatestUserEquals("start").WhenTurn(1).Responds("first answer").Then("second"))
            .Step("second", step => step.WhenLatestUserEquals("continue").WhenTurn(2).Responds("second answer", "second ", "answer").Complete())));
        using var http = host.GetTestClient();
        using var conversation = new LlmTestServerClient(http).ForConversation("continuation");
        using var first = await conversation.HttpClient.PostAsJsonAsync("/openai/v1/responses", new { model = LlmTestServerKnownModelIds.Gpt41Mini, input = "start" });
        first.EnsureSuccessStatusCode();
        var firstId = (await first.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetString();
        var body = new { model = LlmTestServerKnownModelIds.Gpt41Mini, input = "continue", previous_response_id = firstId };
        using var second = await conversation.HttpClient.PostAsJsonAsync("/openai/v1/responses", body);
        second.EnsureSuccessStatusCode();
        var secondId = (await second.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetString();
        using var replay = await conversation.HttpClient.PostAsJsonAsync("/openai/v1/responses", new
        { model = LlmTestServerKnownModelIds.Gpt41Mini, input = "continue", previous_response_id = firstId, stream = replayStreaming });
        replay.EnsureSuccessStatusCode();
        var replayText = await replay.Content.ReadAsStringAsync();
        await Assert.That(replayText).Contains(secondId!);
        await Assert.That(replayText).Contains(replayStreaming ? "response.completed" : "second answer");
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
        await Assert.That((await conversation.Control.GetAssertionsAsync()).Events.Last().IsReplay).IsTrue();
    }
}
