using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Anthropic;

public sealed class AnthropicToolStreamWireIntegrationTests
{
    [Test]
    public async Task ToolSse_PreservesOffsetWireShapeAndArgumentsThroughCompletedToolRoundAsync()
    {
        const string model = LlmTestServerKnownModelIds.ClaudeSonnet55;
        const string callId = "toolu_wire_fixture";
        const string arguments = """{ "city": "Paris", "note": null }""";
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var control = new LlmTestServerClient(http);
        var config = new LlmTestServerClientConfigurationBuilder().UseChatModel(model)
            .UseChatScenario("anthropic-tool-wire", scenario => scenario.ForModel(model)
                .CallsTool("weather", arguments, callId).Responds("Tool round complete.")).Build();
        config.ChatScenarios[0].Responses[0] = config.ChatScenarios[0].Responses[0] with
        { Content = "Looking up.", StreamChunks = ["Looking ", "up."] };
        await control.ConfigureAsync(config);
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = 256,
            ["stream"] = true,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "weather" }),
            ["tools"] = new JsonArray(new JsonObject
            {
                ["name"] = "weather",
                ["input_schema"] = JsonNode.Parse("""{"type":"object","properties":{"city":{"type":"string"},"note":{"type":"null"}},"required":["city","note"],"additionalProperties":false}"""),
            }),
        };
        using var response = await http.PostAsJsonAsync("/anthropic/v1/messages", body);
        response.EnsureSuccessStatusCode();
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/event-stream");
        var blocks = new JsonArray();
        var partialJson = string.Empty;
        var textChunks = new List<string>();
        var stopped = new List<int>();
        var events = new List<string>();
        var messageStopped = false;
        var stopReason = string.Empty;
        foreach (var line in (await response.Content.ReadAsStringAsync()).Split('\n'))
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) { continue; }
            var frame = JsonNode.Parse(line[6..])!.AsObject();
            var type = frame["type"]!.GetValue<string>();
            events.Add(type);
            switch (type)
            {
                case "message_start":
                    await Assert.That(string.Join(',', frame.Select(property => property.Key))).IsEqualTo("type,message");
                    var message = frame["message"]!.AsObject();
                    await Assert.That(message["content"]!.AsArray().Count).IsEqualTo(0);
                    await Assert.That(message.ContainsKey("stop_reason") && message["stop_reason"] is null).IsTrue();
                    await Assert.That(message.ContainsKey("stop_sequence") && message["stop_sequence"] is null).IsTrue();
                    break;
                case "content_block_start":
                    await Assert.That(string.Join(',', frame.Select(property => property.Key))).IsEqualTo("type,index,content_block");
                    await Assert.That(frame["index"]!.GetValue<int>()).IsEqualTo(blocks.Count);
                    var block = frame["content_block"]!.AsObject();
                    if (blocks.Count == 1)
                    {
                        await Assert.That(block["type"]!.GetValue<string>()).IsEqualTo("tool_use");
                        await Assert.That(block["id"]!.GetValue<string>()).IsEqualTo(callId);
                        await Assert.That(block["name"]!.GetValue<string>()).IsEqualTo("weather");
                        await Assert.That(block["input"]!.AsObject().Count).IsEqualTo(0);
                        await Assert.That(block["caller"]!["type"]!.GetValue<string>()).IsEqualTo("direct");
                        await Assert.That(block.ContainsKey("text") || block.ContainsKey("content") || block.ContainsKey("citations")).IsFalse();
                    }
                    blocks.Add(block.DeepClone());
                    break;
                case "content_block_delta":
                    await Assert.That(string.Join(',', frame.Select(property => property.Key))).IsEqualTo("type,index,delta");
                    var delta = frame["delta"]!.AsObject();
                    if (delta["type"]!.GetValue<string>() == "input_json_delta")
                    {
                        await Assert.That(frame["index"]!.GetValue<int>()).IsEqualTo(1);
                        await Assert.That(string.Join(',', delta.Select(property => property.Key))).IsEqualTo("type,partial_json");
                        partialJson += delta["partial_json"]!.GetValue<string>();
                    }
                    else
                    {
                        await Assert.That(delta["type"]!.GetValue<string>()).IsEqualTo("text_delta");
                        await Assert.That(frame["index"]!.GetValue<int>()).IsEqualTo(0);
                        textChunks.Add(delta["text"]!.GetValue<string>());
                    }
                    break;
                case "content_block_stop":
                    await Assert.That(string.Join(',', frame.Select(property => property.Key))).IsEqualTo("type,index");
                    stopped.Add(frame["index"]!.GetValue<int>());
                    break;
                case "message_delta":
                    await Assert.That(string.Join(',', frame.Select(property => property.Key))).IsEqualTo("type,delta,usage");
                    var terminalDelta = frame["delta"]!.AsObject();
                    await Assert.That(string.Join(',', terminalDelta.Select(property => property.Key))).IsEqualTo("stop_reason,stop_sequence,stop_details");
                    await Assert.That(terminalDelta["stop_sequence"] is null && terminalDelta["stop_details"] is null).IsTrue();
                    stopReason = terminalDelta["stop_reason"]!.GetValue<string>();
                    break;
                case "message_stop":
                    await Assert.That(string.Join(',', frame.Select(property => property.Key))).IsEqualTo("type");
                    messageStopped = true;
                    break;
            }
        }
        await Assert.That(string.Join(',', events)).IsEqualTo("message_start,content_block_start,content_block_delta,content_block_delta,content_block_stop,content_block_start,content_block_delta,content_block_stop,message_delta,message_stop");
        await Assert.That(blocks.Count).IsEqualTo(2);
        await Assert.That(textChunks.SequenceEqual(new[] { "Looking ", "up." })).IsTrue();
        await Assert.That(partialJson).IsEqualTo(arguments);
        await Assert.That(stopped.SequenceEqual(new[] { 0, 1 })).IsTrue();
        await Assert.That(stopReason).IsEqualTo("tool_use");
        await Assert.That(messageStopped).IsTrue();
        blocks[0]!["text"] = string.Concat(textChunks);
        blocks[1]!["input"] = JsonNode.Parse(partialJson);
        body["stream"] = false;
        body["messages"]!.AsArray().Add(new JsonObject { ["role"] = "assistant", ["content"] = blocks });
        body["messages"]!.AsArray().Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = callId, ["content"] = "sunny" }),
        });
        using var completed = await http.PostAsJsonAsync("/anthropic/v1/messages", body);
        completed.EnsureSuccessStatusCode();
        var final = JsonNode.Parse(await completed.Content.ReadAsStringAsync())!;
        await Assert.That(final["stop_reason"]!.GetValue<string>()).IsEqualTo("end_turn");
        await Assert.That(final["content"]![0]!["text"]!.GetValue<string>()).IsEqualTo("Tool round complete.");
        var traces = (await control.GetRequestsAsync()).Requests;
        await Assert.That(traces.Count).IsEqualTo(2);
        var streamed = traces.Single(trace => trace.IsStreaming);
        await Assert.That(streamed.TerminalMarkerObserved).IsTrue();
        await Assert.That(streamed.ResponseToolCalls.Single().ArgumentsJson).IsEqualTo(arguments);
        var tool = traces.Single(trace => !trace.IsStreaming).Messages.Single(message => message.Role == "tool");
        await Assert.That(tool.ToolCallId).IsEqualTo(callId);
        await Assert.That(tool.Content).IsEqualTo("sunny");
    }
}
