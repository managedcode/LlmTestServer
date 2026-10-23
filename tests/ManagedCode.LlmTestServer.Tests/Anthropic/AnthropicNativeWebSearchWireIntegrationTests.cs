using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.LlmTestServer.Tests.Anthropic;

public sealed partial class AnthropicNativeWebSearchIntegrationTests
{
    [Test]
    public async Task OrderedNativeJson_PreservesOpaqueSourcesCitationsAndPublicEvidenceAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var control = new LlmTestServerClient(http); await control.ConfigureAsync(Configuration());
        var response = await CompleteAsync(http); await AssertContentAsync(response.GetProperty("content"));
        await Assert.That(response.GetProperty("stop_reason").GetString()).IsEqualTo("end_turn");
        await Assert.That(response.GetProperty("usage").GetProperty("server_tool_use").GetProperty("web_search_requests").GetInt32()).IsEqualTo(1);
        var exported = await control.ExportRequestsAsync(); var trace = exported.Evidence.Requests.Single();
        await Assert.That(trace.ResponseToolCalls.Count).IsEqualTo(0);
        await Assert.That(trace.NativeTools.Count).IsEqualTo(1);
        await Assert.That(trace.ResponseOutputItems.Count).IsEqualTo(3);
        await Assert.That(((LlmTestServerWebSearchResults)((LlmTestServerResponseServerToolResultItem)trace.ResponseOutputItems[1]).Result).Results[0].EncryptedContent).IsEqualTo(_opaque);
        await Assert.That(((LlmTestServerAnthropicWebSearchCitation)((LlmTestServerResponseMessageItem)trace.ResponseOutputItems[2]).Citations.Single()).EncryptedIndex).IsEqualTo(_index);
        var lines = (await control.ExportRequestsNdjsonAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using var record = JsonDocument.Parse(lines[1]);
        await Assert.That(record.RootElement.GetProperty("request").GetProperty("responseOutputItems")[0].GetProperty("kind").GetString()).IsEqualTo("server_tool_use");
    }

    [Test]
    [Arguments("invalid_tool_input")]
    [Arguments("unavailable")]
    [Arguments("max_uses_exceeded")]
    [Arguments("too_many_requests")]
    [Arguments("query_too_long")]
    [Arguments("request_too_large")]
    [Arguments("")]
    public async Task NativeSearchErrorsAndEmptyResults_AreSuccessfulTypedMessagesAsync(string code)
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        await new LlmTestServerClient(http).ConfigureAsync(Configuration(error: code.Length == 0 ? null : code, empty: code.Length == 0));
        var body = Request(); if (code == "max_uses_exceeded") { body["tools"]![0]!["max_uses"] = 0; }
        var response = await CompleteAsync(http, body); var result = response.GetProperty("content")[1].GetProperty("content");
        if (code.Length == 0) { await Assert.That(result.GetArrayLength()).IsEqualTo(0); }
        else
        {
            await Assert.That(result.GetProperty("type").GetString()).IsEqualTo("web_search_tool_result_error");
            await Assert.That(result.GetProperty("error_code").GetString()).IsEqualTo(code);
        }
        await Assert.That(response.GetProperty("content")[2].GetProperty("text").GetString()).IsEqualTo(_answer);
        await Assert.That(response.GetProperty("usage").GetProperty("server_tool_use").GetProperty("web_search_requests").GetInt32()).IsEqualTo(code == "max_uses_exceeded" ? 0 : 1);
    }

    [Test]
    public async Task NativeSse_ReconstructsTheSameOrderedContentAndTerminalUsageAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var config = Configuration(2);
        var control = new LlmTestServerClient(http); await control.ConfigureAsync(config);
        var baseline = await CompleteAsync(http);
        using var response = await http.PostAsJsonAsync("/anthropic/v1/messages", Request(true)); response.EnsureSuccessStatusCode();
        var blocks = new JsonArray(); var input = ""; var stopped = new List<int>(); var messageStopped = false; var usageCount = -1;
        var chunks = new List<string>(); var citationDeltas = 0;
        foreach (var line in (await response.Content.ReadAsStringAsync()).Split('\n'))
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) { continue; }
            var frame = JsonNode.Parse(line[6..])!; var type = frame["type"]!.GetValue<string>();
            if (type is "content_block_start" or "content_block_delta" or "content_block_stop")
            {
                var expectedFields = type switch
                {
                    "content_block_start" => "type,index,content_block",
                    "content_block_delta" => "type,index,delta",
                    _ => "type,index",
                };
                await Assert.That(string.Join(',', frame.AsObject().Select(property => property.Key))).IsEqualTo(expectedFields);
            }
            if (type == "content_block_start")
            {
                await Assert.That(frame["index"]!.GetValue<int>()).IsEqualTo(blocks.Count);
                var block = frame["content_block"]!.AsObject();
                if (blocks.Count == 0)
                {
                    await Assert.That(block["input"]!.AsObject().Count).IsEqualTo(0);
                    await Assert.That(block.ContainsKey("text") || block.ContainsKey("citations") || block.ContainsKey("content")).IsFalse();
                }
                if (blocks.Count == 1)
                {
                    await Assert.That(block.ContainsKey("input") || block.ContainsKey("text") || block.ContainsKey("citations")).IsFalse();
                }
                if (blocks.Count == 2)
                {
                    await Assert.That(block["text"]!.GetValue<string>()).IsEqualTo(string.Empty);
                    await Assert.That(block["citations"]!.AsArray().Count).IsEqualTo(0);
                    await Assert.That(block.ContainsKey("input") || block.ContainsKey("content")).IsFalse();
                }
                blocks.Add(block.DeepClone());
            }
            if (type == "content_block_delta")
            {
                var index = frame["index"]!.GetValue<int>(); var delta = frame["delta"]!;
                switch (delta["type"]!.GetValue<string>())
                {
                    case "input_json_delta":
                        await Assert.That(string.Join(',', delta.AsObject().Select(property => property.Key))).IsEqualTo("type,partial_json");
                        input += delta["partial_json"]!.GetValue<string>(); break;
                    case "text_delta":
                        await Assert.That(string.Join(',', delta.AsObject().Select(property => property.Key))).IsEqualTo("type,text");
                        chunks.Add(delta["text"]!.GetValue<string>());
                        blocks[index]!["text"] = (blocks[index]!["text"]?.GetValue<string>() ?? "") + chunks[^1]; break;
                    case "citations_delta":
                        await Assert.That(string.Join(',', delta.AsObject().Select(property => property.Key))).IsEqualTo("type,citation");
                        citationDeltas++;
                        blocks[index]!["citations"] ??= new JsonArray();
                        ((JsonArray)blocks[index]!["citations"]!).Add(delta["citation"]!.DeepClone()); break;
                }
            }
            if (type == "content_block_stop") { stopped.Add(frame["index"]!.GetValue<int>()); }
            if (type == "message_delta")
            {
                await Assert.That(frame["delta"]!["stop_reason"]!.GetValue<string>()).IsEqualTo("end_turn");
                await Assert.That(string.Join(',', frame["delta"]!.AsObject().Select(property => property.Key))).IsEqualTo("stop_reason,stop_sequence");
                await Assert.That(frame["delta"]!["stop_sequence"] is null).IsTrue();
                usageCount = frame["usage"]!["server_tool_use"]!["web_search_requests"]!.GetValue<int>();
            }
            if (type == "message_stop") { messageStopped = true; }
        }
        await Assert.That(input).IsEqualTo(baseline.GetProperty("content")[0].GetProperty("input").GetRawText());
        await Assert.That(chunks.SequenceEqual(new[] { "Paris ", "fixture." })).IsTrue();
        await Assert.That(citationDeltas).IsEqualTo(1);
        blocks[0]!["input"] = JsonNode.Parse(input);
        await AssertContentAsync(JsonSerializer.SerializeToElement(blocks));
        await Assert.That(JsonNode.DeepEquals(blocks, JsonNode.Parse(baseline.GetProperty("content").GetRawText()))).IsTrue();
        await Assert.That(string.Join(',', stopped)).IsEqualTo("0,1,2");
        await Assert.That(messageStopped).IsTrue(); await Assert.That(usageCount).IsEqualTo(1);
        await Assert.That((await control.GetRequestsAsync()).Requests[0].TerminalMarkerObserved).IsTrue();
    }
    [Test]
    public async Task TruncatedNativePair_PublicSnapshotsRetainAnHonestCallPrefixWithoutThrowingAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(configureServices: services =>
            services.Configure<LlmTestServerProviderHttpTraceOptions>(options => options.MaxPreviewBytes = 1024));
        using var http = host.GetTestClient(); Headers(http);
        var config = Configuration(); var items = Items();
        items[1] = ((LlmTestServerResponseServerToolResultItem)items[1]) with
        { Result = new LlmTestServerWebSearchResults { Results = [new() { Url = _url, Title = _title, EncryptedContent = new string('x', 10000) }] } };
        config.ChatScenarios[0].Responses[0] = config.ChatScenarios[0].Responses[0] with { OutputItems = [.. items] };
        var control = new LlmTestServerClient(http); await control.ConfigureAsync(config);
        var response = await CompleteAsync(http);
        await Assert.That(response.GetProperty("content")[1].GetProperty("content")[0].GetProperty("encrypted_content").GetString()!.Length).IsEqualTo(10000);
        var snapshot = await control.GetRequestsAsync(); var trace = snapshot.Requests.Single();
        await Assert.That(trace.RetentionTruncated).IsTrue();
        await Assert.That(trace.ResponseOutputItems.Count).IsEqualTo(1);
        await Assert.That(((LlmTestServerResponseServerToolUseItem)trace.ResponseOutputItems.Single()).Id).IsEqualTo(_callId);
        var exported = await control.ExportRequestsAsync();
        await Assert.That(exported.Evidence.Requests.Single().ResponseOutputItems.Count).IsEqualTo(1);
        await Assert.That(exported.Evidence.RetentionTruncated).IsTrue();
        var lines = (await control.ExportRequestsNdjsonAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using var record = JsonDocument.Parse(lines[1]);
        await Assert.That(record.RootElement.GetProperty("request").GetProperty("responseOutputItems").GetArrayLength()).IsEqualTo(1);
    }

}
