using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeFileCodeIntegrationTests
{
    [Test]
    [Arguments("filter")]
    [Arguments("store")]
    [Arguments("ranker")]
    [Arguments("score")]
    [Arguments("container")]
    [Arguments("files")]
    public async Task NativeNestedOptions_PartitionPromptCacheAndSurvivePublicExportAsync(string option)
    {
        var code = option is "container" or "files";
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var configuration = Configuration(code, 3);
        if (option is "store" or "container" or "files")
        {
            for (var index = 1; index < 3; index++)
            {
                var items = Items(code);
                if (option == "store") { items[0] = ((LlmTestServerResponseFileSearchItem)items[0]) with { VectorStoreIds = ["vs_changed"] }; }
                if (option == "files") { items[0] = ((LlmTestServerResponseCodeInterpreterItem)items[0]) with { InputFileIds = ["file_changed"] }; }
                if (option == "container")
                {
                    items[0] = ((LlmTestServerResponseCodeInterpreterItem)items[0]) with { ContainerId = "cntr_changed" };
                    items[1] = ((LlmTestServerResponseMessageItem)items[1]) with
                    { Citations = [new LlmTestServerContainerFileCitation { ContainerId = "cntr_changed", FileId = _generatedFile, Filename = "result.txt", StartIndex = 0, EndIndex = 0 }] };
                }
                configuration.ChatScenarios[0].Responses[index] = configuration.ChatScenarios[0].Responses[index] with { OutputItems = [.. items] };
            }
        }
        await control.ConfigureAsync(configuration);
        var body = Request(code, auto: option == "files", input: string.Join(' ', Enumerable.Repeat("stable native option cache reference", 1000)));
        var tool = body["tools"]![0]!;
        if (option is "ranker" or "score") { tool["ranking_options"] = new JsonObject { ["ranker"] = "auto", ["score_threshold"] = 0.5 }; }
        var first = await CompleteAsync(http, code, body);
        await Assert.That(first.GetProperty("usage").GetProperty("input_tokens").GetInt32()).IsGreaterThanOrEqualTo(1024);
        await Assert.That(first.GetProperty("usage").GetProperty("input_tokens_details").GetProperty("cached_tokens").GetInt32()).IsEqualTo(0);
        switch (option)
        {
            case "filter": tool["filters"] = Comparison(); break;
            case "store": tool["vector_store_ids"] = new JsonArray("vs_changed"); break;
            case "ranker": tool["ranking_options"]!["ranker"] = "default-2024-11-15"; break;
            case "score": tool["ranking_options"]!["score_threshold"] = 0.75; break;
            case "container": tool["container"] = "cntr_changed"; break;
            case "files": tool["container"]!["file_ids"] = new JsonArray("file_changed"); break;
        }
        var changed = await CompleteAsync(http, code, body);
        await Assert.That(changed.GetProperty("usage").GetProperty("input_tokens_details").GetProperty("cached_tokens").GetInt32()).IsEqualTo(0);
        var repeated = await CompleteAsync(http, code, body);
        var cached = repeated.GetProperty("usage").GetProperty("input_tokens_details").GetProperty("cached_tokens").GetInt32();
        await Assert.That(cached).IsGreaterThanOrEqualTo(1024);
        var exported = await control.ExportRequestsAsync();
        var trace = exported.Evidence.Requests[0];
        await Assert.That(trace.Usage!.CachedInputTokens).IsEqualTo(cached);
        await Assert.That(trace.ResponseToolCalls.Count).IsEqualTo(0);
        await Assert.That(trace.NativeTools.Count).IsEqualTo(1);
        await Assert.That(trace.ResponseOutputItems.Count).IsEqualTo(2);
        var normalized = JsonSerializer.SerializeToElement(trace.NativeTools.Single(), _configurationJson);
        var options = normalized.GetProperty(code ? "codeInterpreterOptions" : "fileSearchOptions");
        switch (option)
        {
            case "filter": await Assert.That(options.GetProperty("filters").GetProperty("key").GetString()).IsEqualTo("category"); break;
            case "store": await Assert.That(options.GetProperty("vectorStoreIds")[0].GetString()).IsEqualTo("vs_changed"); break;
            case "ranker": await Assert.That(options.GetProperty("ranker").GetString()).IsEqualTo("default-2024-11-15"); break;
            case "score": await Assert.That(options.GetProperty("scoreThreshold").GetDouble()).IsEqualTo(0.75); break;
            case "container": await Assert.That(options.GetProperty("containerId").GetString()).IsEqualTo("cntr_changed"); break;
            case "files": await Assert.That(options.GetProperty("fileIds")[0].GetString()).IsEqualTo("file_changed"); break;
        }
        await Assert.That(((LlmTestServerResponseMessageItem)trace.ResponseOutputItems[1]).Citations.Count).IsEqualTo(1);
        var lines = (await control.ExportRequestsNdjsonAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using var record = JsonDocument.Parse(lines[1]);
        await Assert.That(record.RootElement.GetProperty("request").GetProperty("responseOutputItems")[0].GetProperty("kind").GetString())
            .IsEqualTo(code ? "code_interpreter" : "file_search");
    }
}
