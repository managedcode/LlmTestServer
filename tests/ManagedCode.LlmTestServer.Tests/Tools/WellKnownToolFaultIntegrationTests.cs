using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using ManagedCode.LlmTestServer.Tools.WellKnown;
using OpenAI;
using OpenAI.Chat;

namespace ManagedCode.LlmTestServer.Tests.Tools;

public sealed partial class WellKnownToolFaultIntegrationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NullAndEmptyCollection_RejectMissingAndWrongResultsThenRecoverWithCacheAndHistoryAsync(bool emptyCollection)
    {
        var preset = LlmTestServerWellKnownTools.WebSearch;
        var arguments = new LlmTestServerWebSearchArguments { Query = "fixture evidence" };
        var fixture = emptyCollection
            ? preset.Success(arguments, new() { Results = [] }, "call_search_outcome")
            : preset.NullResult(arguments, "call_search_outcome");
        var expectedResult = fixture.Result.Content;
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddConversation("outcome", graph => graph
            .Step("call", step => step.WhenLatestUserEquals("lookup").CallsTools(fixture.Call).Then("result"))
            .Step("result", step => step.WhenToolResult(fixture.Call.Name, fixture.Call.Id)
                .WithToolArgumentsJson(fixture.Call.ArgumentsJson).WhenLatestJson("$", expectedResult)
                .Responds("outcome accepted").Complete())));
        using var http = host.GetTestClient();
        using var conversation = new LlmTestServerClient(http).ForConversation("outcome-isolation");
        conversation.HttpClient.DefaultRequestHeaders.Authorization = new("Bearer", "tool-fault-key");
        var sdk = CreateSdk(conversation.HttpClient);
        var options = new ChatCompletionOptions
        {
            Tools = { ChatTool.CreateFunctionTool(fixture.Definition.Name, fixture.Definition.Description,
                BinaryData.FromString(fixture.Definition.ParametersJson)) },
        };
        var system = string.Join(' ', Enumerable.Repeat("Retained system reference", 1200));
        List<ChatMessage> messages = [new SystemChatMessage(system), new UserChatMessage("lookup")];
        var first = (await sdk.CompleteChatAsync(messages, options)).Value;
        await Assert.That(first.ToolCalls.Single().Id).IsEqualTo(fixture.Call.Id);
        messages.Add(new AssistantChatMessage(first));
        var tools = new[] { new { type = "function", function = new { name = fixture.Definition.Name,
            parameters = JsonSerializer.Deserialize<JsonElement>(fixture.Definition.ParametersJson) } } };
        var assistant = JsonSerializer.SerializeToElement(new
        {
            role = "assistant",
            tool_calls = new[] { new
        { id = first.ToolCalls.Single().Id, type = "function", function = new { name = first.ToolCalls.Single().FunctionName,
            arguments = first.ToolCalls.Single().FunctionArguments.ToString() } } }
        });
        object[] prefix = [new { role = "system", content = system }, new { role = "user", content = "lookup" }, assistant];
        using var missing = await conversation.HttpClient.PostAsJsonAsync("/openai/v1/chat/completions", new
        { model = LlmTestServerKnownModelIds.Gpt41Mini, tools, messages = prefix.Append(new { role = "user", content = "continue without a result" }).ToArray() });
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        var missingEvidence = await conversation.Control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(missingEvidence.Requests[^1].Messages.Count(message => message.ToolCallId == fixture.Call.Id)).IsEqualTo(0);
        var wrongResult = emptyCollection ? "null" : "{\"results\":[]}";
        using var wrong = await conversation.HttpClient.PostAsJsonAsync("/openai/v1/chat/completions", new
        { model = LlmTestServerKnownModelIds.Gpt41Mini, tools, messages = prefix.Append(new { role = "tool", tool_call_id = fixture.Call.Id, content = wrongResult }).ToArray() });
        await Assert.That(wrong.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        messages.Add(new ToolChatMessage(fixture.Result.ToolCallId, expectedResult));
        var accepted = (await sdk.CompleteChatAsync(messages, options)).Value;
        await Assert.That(accepted.Content.Single().Text).IsEqualTo("outcome accepted");
        var replay = (await sdk.CompleteChatAsync(messages, options)).Value;
        await Assert.That(replay.Id).IsEqualTo(accepted.Id);
        var evidence = await conversation.Control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(evidence.Requests.Select(request => request.StatusCode)).IsEquivalentTo(new int?[] { 200, 409, 409, 200, 200 });
        await Assert.That(evidence.Requests[0].Usage!.CachedInputTokens).IsEqualTo(0);
        await Assert.That(evidence.Requests[0].Usage!.CacheCreationInputTokens).IsGreaterThan(0);
        await Assert.That(evidence.Requests.Where(request => request.StatusCode == 409).All(request => request.Usage!.CacheCreationInputTokens == 0)).IsTrue();
        await Assert.That(evidence.Requests[3].Usage!.CachedInputTokens).IsGreaterThan(0);
        await Assert.That(evidence.Requests[3].Messages.Single(message => message.Role == "tool").Content).IsEqualTo(expectedResult);
        await Assert.That(evidence.Requests[^1].IsReplay).IsTrue();
        var state = (await conversation.Control.GetConversationsAsync()).Single();
        await Assert.That(state.ExecutedSteps).IsEqualTo(2);
        await Assert.That(state.IsTerminal).IsTrue();
    }

    [Test]
    public async Task EmptyToolText_IsPresentAndDistinctFromLiteralNullAsync()
    {
        var preset = LlmTestServerWellKnownTools.ReadFile;
        var call = preset.CreateCall(new() { Path = "fixture.txt" }, "call_empty_text");
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddConversation("empty-text", graph => graph
            .Step("call", step => step.CallsTools(call).Then("result"))
            .Step("result", step => step.WhenToolResult(call.Name, call.Id).Responds("empty text observed").Complete())));
        using var http = host.GetTestClient();
        using var conversation = new LlmTestServerClient(http).ForConversation("empty-text");
        var sdk = CreateSdk(conversation.HttpClient);
        var options = new ChatCompletionOptions
        {
            Tools = { ChatTool.CreateFunctionTool(preset.Definition.Name, preset.Definition.Description,
                BinaryData.FromString(preset.Definition.ParametersJson)) },
        };
        List<ChatMessage> messages = [new UserChatMessage("read fixture")];
        var first = (await sdk.CompleteChatAsync(messages, options)).Value;
        messages.Add(new AssistantChatMessage(first));
        messages.Add(new ToolChatMessage(call.Id, string.Empty));
        var accepted = (await sdk.CompleteChatAsync(messages, options)).Value;
        await Assert.That(accepted.Content.Single().Text).IsEqualTo("empty text observed");
        var evidence = await conversation.Control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        var result = evidence.Requests[^1].Messages.Single(message => message.Role == "tool");
        await Assert.That(result.ToolCallId).IsEqualTo(call.Id);
        await Assert.That(result.Content).IsEqualTo(string.Empty);
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
    }

    private static global::OpenAI.Chat.ChatClient CreateSdk(HttpClient http)
    {
        return new(LlmTestServerKnownModelIds.Gpt41Mini, new ApiKeyCredential("tool-fault-key"), new OpenAIClientOptions
        { Endpoint = new Uri(http.BaseAddress!, "/openai/v1"), Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) });
    }
}
