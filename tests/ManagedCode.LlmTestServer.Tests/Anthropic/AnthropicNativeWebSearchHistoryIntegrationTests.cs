using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Anthropic;

public sealed partial class AnthropicNativeWebSearchIntegrationTests
{
    [Test]
    public async Task MixedClientFunctionRound_ExplicitNativeHistoryPreservesPairOpaqueDataAndLogicalTurnsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var control = new LlmTestServerClient(http);
        var call = new LlmTestServerToolCall { Id = "toolu_client", Name = "client_fixture", ArgumentsJson = "{}" };
        await control.ConfigureAsync(config => config.UseChatModel(_model).UseConversation("anthropic-round", flow => flow.ForModel(_model)
            .Step("search", step => step.WhenTurn(1).WhenLatestUserEquals("start").RespondsWithItems(
                new LlmTestServerResponseFunctionCallItem { Id = "function_fixture", Call = call }, Items()[0], Items()[1], Items()[2]).Then("final"))
            .Step("final", step => step.WhenTurn(2).WhenToolResult(call.Name, call.Id).WhenLatestTextContains("client result").Responds("Complete client round.").Complete())));
        using var scoped = control.ForConversation("anthropic-round");
        var body = Request(); ((JsonArray)body["tools"]!).Add(new JsonObject { ["name"] = call.Name, ["input_schema"] = new JsonObject { ["type"] = "object" } });
        var first = await CompleteAsync(scoped.HttpClient, body);
        await Assert.That(first.GetProperty("content")[0].GetProperty("type").GetString()).IsEqualTo("tool_use");
        await Assert.That(first.GetProperty("content")[0].GetProperty("id").GetString()).IsEqualTo(call.Id);
        var messages = (JsonArray)body["messages"]!;
        messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = JsonNode.Parse(first.GetProperty("content").GetRawText()) });
        messages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject
            { ["type"] = "tool_result", ["tool_use_id"] = call.Id, ["content"] = "client result" })
        });
        var final = await CompleteAsync(scoped.HttpClient, body);
        await Assert.That(final.GetProperty("content")[0].GetProperty("text").GetString()).IsEqualTo("Complete client round.");
        await Assert.That((await scoped.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
        var trace = (await scoped.Control.GetRequestsAsync()).Requests[0];
        var history = trace.Messages.Where(message => message.Role == "assistant").SelectMany(message => message.OutputItems).ToArray();
        await Assert.That(history.Length).IsEqualTo(4);
        await Assert.That(((LlmTestServerResponseServerToolUseItem)history[1]).Id).IsEqualTo(_callId);
        await Assert.That(((LlmTestServerWebSearchResults)((LlmTestServerResponseServerToolResultItem)history[2]).Result).Results.Single().EncryptedContent).IsEqualTo(_opaque);
        await Assert.That(trace.Messages.Single(message => message.Role == "tool").ToolCallId).IsEqualTo(call.Id);
    }

    [Test]
    public async Task OpaqueResultsAndCitationMetadata_DoNotInflateResponseOrHistoricalInputTokensAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var config = Configuration(4); var changed = Items();
        var url = "https://example.com/" + new string('a', 1500);
        changed[1] = ((LlmTestServerResponseServerToolResultItem)changed[1]) with
        {
            Result = new LlmTestServerWebSearchResults
            { Results = [new() { Url = url, Title = new string('t', 1500), EncryptedContent = new string('e', 10000), PageAge = new string('p', 1500) }] }
        };
        changed[2] = ((LlmTestServerResponseMessageItem)changed[2]) with
        {
            Citations = [new LlmTestServerAnthropicWebSearchCitation
        { Url = url, Title = new string('t', 1500), CitedText = new string('c', 1500), EncryptedIndex = new string('i', 10000) }]
        };
        config.ChatScenarios[0].Responses[1] = config.ChatScenarios[0].Responses[1] with { OutputItems = [.. changed] };
        var control = new LlmTestServerClient(http); await control.ConfigureAsync(config);
        var first = await CompleteAsync(http); var second = await CompleteAsync(http);
        await Assert.That(second.GetProperty("usage").GetProperty("output_tokens").GetInt32()).IsEqualTo(first.GetProperty("usage").GetProperty("output_tokens").GetInt32());
        var inputs = new List<int>();
        foreach (var previous in new[] { first, second })
        {
            var body = Request(); ((JsonArray)body["messages"]!).Add(new JsonObject { ["role"] = "assistant", ["content"] = JsonNode.Parse(previous.GetProperty("content").GetRawText()) });
            ((JsonArray)body["messages"]!).Add(new JsonObject { ["role"] = "user", ["content"] = "follow" });
            var result = await CompleteAsync(http, body); var usage = result.GetProperty("usage");
            inputs.Add(usage.GetProperty("input_tokens").GetInt32() + (usage.TryGetProperty("cache_read_input_tokens", out var read) ? read.GetInt32() : 0) + (usage.TryGetProperty("cache_creation_input_tokens", out var write) ? write.GetInt32() : 0));
        }
        await Assert.That(inputs[1]).IsEqualTo(inputs[0]);
        var historical = (await control.GetRequestsAsync()).Requests[0].Messages.Where(message => message.Role == "assistant").SelectMany(message => message.OutputItems).ToArray();
        await Assert.That(((LlmTestServerWebSearchResults)((LlmTestServerResponseServerToolResultItem)historical[1]).Result).Results.Single().EncryptedContent.Length).IsEqualTo(10000);
    }

    [Test]
    public async Task CompletedNativeGraph_ReplaysExactContentWithoutAdvancingAgainAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseChatModel(_model).UseConversation("anthropic-replay", flow => flow.ForModel(_model)
            .Step("search", step => step.RespondsWithItems(Items()).Complete())));
        var first = await CompleteAsync(http); var replay = await CompleteAsync(http);
        await Assert.That(replay.GetProperty("content").GetRawText()).IsEqualTo(first.GetProperty("content").GetRawText());
        await Assert.That((await control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        await Assert.That((await control.GetRequestsAsync()).Requests.Count(trace => trace.IsReplay)).IsEqualTo(1);
    }
}
