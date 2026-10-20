using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed class NativeWebSearchLegacyOutputIntegrationTests
{
    [Test]
    [Arguments(false, 128, true)]
    [Arguments(true, 128, true)]
    [Arguments(false, 129, false)]
    [Arguments(true, 129, false)]
    public async Task LegacyFunctionCalls_PreserveLargeResponseAndQueueOrderAsync(bool responses, int callCount, bool withContent)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var calls = Enumerable.Range(0, callCount).Select(index => new LlmTestServerToolCall
        {
            Id = $"call_legacy_{index}",
            Name = "legacy_function",
            ArgumentsJson = JsonSerializer.Serialize(new { index }),
        }).ToArray();
        var configuration = new LlmTestServerClientConfigurationBuilder().UseGpt41Mini()
            .UseChatScenario("legacy-many-calls", scenario => scenario.CallsTools(calls).Responds("queued answer after large fixture"))
            .Build();
        var content = withContent ? "Legacy text accompanying function calls." : string.Empty;
        var scenario = configuration.ChatScenarios.Single();
        scenario.Responses[0] = scenario.Responses[0] with { Content = content };
        await control.ConfigureAsync(configuration);
        var body = new JsonObject
        {
            ["model"] = LlmTestServerKnownModelIds.Gpt41Mini,
            ["tool_choice"] = "auto",
            ["parallel_tool_calls"] = true,
        };
        if (responses)
        {
            body["input"] = "legacy calls";
            body["tools"] = new JsonArray(new JsonObject
            { ["type"] = "function", ["name"] = "legacy_function", ["parameters"] = new JsonObject { ["type"] = "object" } });
        }
        else
        {
            body["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "legacy calls" });
            body["tools"] = new JsonArray(new JsonObject
            { ["type"] = "function", ["function"] = new JsonObject { ["name"] = "legacy_function", ["parameters"] = new JsonObject { ["type"] = "object" } } });
        }
        var path = responses ? "/openai/v1/responses" : "/openai/v1/chat/completions";
        using var first = await http.PostAsJsonAsync(path, body);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var firstPayload = await first.Content.ReadFromJsonAsync<JsonElement>();
        var returned = responses
            ? firstPayload.GetProperty("output").EnumerateArray().Where(item => item.GetProperty("type").GetString() == "function_call").ToArray()
            : firstPayload.GetProperty("choices")[0].GetProperty("message").GetProperty("tool_calls").EnumerateArray().ToArray();
        await Assert.That(returned.Length).IsEqualTo(callCount);
        for (var index = 0; index < callCount; index++)
        {
            await Assert.That(returned[index].GetProperty(responses ? "call_id" : "id").GetString()).IsEqualTo(calls[index].Id);
            var function = responses ? returned[index] : returned[index].GetProperty("function");
            await Assert.That(function.GetProperty("name").GetString()).IsEqualTo(calls[index].Name);
            await Assert.That(function.GetProperty("arguments").GetString()).IsEqualTo(calls[index].ArgumentsJson);
        }
        if (responses)
        {
            var messages = firstPayload.GetProperty("output").EnumerateArray().Where(item => item.GetProperty("type").GetString() == "message").ToArray();
            await Assert.That(messages.Length).IsEqualTo(withContent ? 1 : 0);
            if (withContent) { await Assert.That(messages.Single().GetProperty("content")[0].GetProperty("text").GetString()).IsEqualTo(content); }
        }
        else
        {
            await Assert.That(firstPayload.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()).IsEqualTo(content);
        }
        using var second = await http.PostAsJsonAsync(path, body);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var secondPayload = await second.Content.ReadFromJsonAsync<JsonElement>();
        var nextText = responses
            ? secondPayload.GetProperty("output").EnumerateArray().Single().GetProperty("content")[0].GetProperty("text").GetString()
            : secondPayload.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        await Assert.That(nextText).IsEqualTo("queued answer after large fixture");
        using var exhausted = await http.PostAsJsonAsync(path, body);
        await Assert.That(exhausted.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That((await exhausted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString()).IsEqualTo("llm_test_server_scenario_exhausted");
        var evidence = await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(evidence.Requests.Count).IsEqualTo(3);
        await Assert.That(evidence.Requests[0].ResponseToolCalls.Count).IsEqualTo(callCount);
        await Assert.That(evidence.Requests[1].ResponseToolCalls.Count).IsEqualTo(0);
    }
}
