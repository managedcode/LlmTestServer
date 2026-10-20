using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeFileCodeIntegrationTests
{
    [Test]
    [Arguments(false, "nullResults")]
    [Arguments(false, "nullResult")]
    [Arguments(false, "negativeScore")]
    [Arguments(false, "largeScore")]
    [Arguments(false, "objectAttribute")]
    [Arguments(false, "tooManyAttributes")]
    [Arguments(false, "longAttributeKey")]
    [Arguments(false, "longAttributeValue")]
    [Arguments(false, "foreignFileCitation")]
    [Arguments(false, "wrongFilename")]
    [Arguments(false, "negativeIndex")]
    [Arguments(false, "oversizedIndex")]
    [Arguments(false, "tooManyResults")]
    [Arguments(false, "noStores")]
    [Arguments(true, "blankContainer")]
    [Arguments(true, "invalidChunks")]
    [Arguments(true, "unknownOutput")]
    [Arguments(true, "foreignFileCitation")]
    [Arguments(true, "foreignContainerCitation")]
    [Arguments(true, "reversedRange")]
    [Arguments(true, "oversizedRange")]
    [Arguments(true, "tooManyOutputs")]
    [Arguments(true, "tooManyInputFiles")]
    public async Task InvalidFileCodeConfiguration_Returns400AndPreservesOldFixturesAsync(bool code, string defect)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration(code));
        var configuration = JsonSerializer.SerializeToNode(Configuration(code), _configurationJson)!;
        var items = configuration["chatScenarios"]![0]!["responses"]![0]!["outputItems"]!;
        var native = items[0]!; var message = items[1]!;
        switch (defect)
        {
            case "nullResults": native["results"] = null; break;
            case "nullResult": native["results"] = new JsonArray((JsonNode?)null); break;
            case "negativeScore": native["results"]![0]!["score"] = -0.1; break;
            case "largeScore": native["results"]![0]!["score"] = 1.1; break;
            case "objectAttribute": native["results"]![0]!["attributes"]!["invalid"] = new JsonObject(); break;
            case "tooManyAttributes":
                var attributes = new JsonObject(); for (var index = 0; index < 17; index++) { attributes[$"key{index}"] = "value"; }
                native["results"]![0]!["attributes"] = attributes; break;
            case "longAttributeKey": native["results"]![0]!["attributes"] = new JsonObject { [new string('x', 65)] = "value" }; break;
            case "longAttributeValue": native["results"]![0]!["attributes"]!["category"] = new string('x', 513); break;
            case "foreignFileCitation": message["citations"]![0]!["fileId"] = "file_foreign"; break;
            case "wrongFilename": message["citations"]![0]!["filename"] = "wrong.txt"; break;
            case "negativeIndex": message["citations"]![0]!["index"] = -1; break;
            case "oversizedIndex": message["citations"]![0]!["index"] = 1000; break;
            case "tooManyResults": native["results"] = new JsonArray(Enumerable.Range(0, 129).Select(_ => native["results"]![0]!.DeepClone()).ToArray()); break;
            case "noStores": native["vectorStoreIds"] = new JsonArray(); break;
            case "blankContainer": native["containerId"] = ""; break;
            case "invalidChunks": native["codeChunks"] = new JsonArray("other code"); break;
            case "unknownOutput": native["outputs"]![0]!["kind"] = "other"; break;
            case "foreignContainerCitation": message["citations"]![0]!["containerId"] = "cntr_foreign"; break;
            case "reversedRange": message["citations"]![0]!["startIndex"] = 2; message["citations"]![0]!["endIndex"] = 1; break;
            case "oversizedRange": message["citations"]![0]!["endIndex"] = 1000; break;
            case "tooManyOutputs": native["outputs"] = new JsonArray(Enumerable.Range(0, 129).Select(_ => native["outputs"]![0]!.DeepClone()).ToArray()); break;
            case "tooManyInputFiles": native["inputFileIds"] = new JsonArray(Enumerable.Range(0, 129).Select(index => JsonValue.Create($"file_{index}")).ToArray()); break;
        }
        using var rejected = await http.PostAsJsonAsync("/admin-api/configure", configuration);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var result = await CompleteAsync(http, code);
        await Assert.That(result.GetProperty("output")[0].GetProperty("id").GetString()).IsEqualTo(ItemId(code));
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, false, true)]
    [Arguments(false, true, false)]
    [Arguments(false, true, true)]
    [Arguments(true, false, false)]
    [Arguments(true, false, true)]
    [Arguments(true, true, false)]
    [Arguments(true, true, true)]
    public async Task DisabledOrAbsentTool_Returns409WithoutConsumingQueueOrGraphAsync(bool code, bool graph, bool absent)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        if (graph)
        {
            await control.ConfigureAsync(config => config.UseChatModel(Model(code)).UseConversation("native-mismatch", flow => flow.ForModel(Model(code))
                .Step("native", step => step.RespondsWithItems(Items(code)).Complete())));
        }
        else { await control.ConfigureAsync(Configuration(code)); }
        var body = Request(code); body["tool_choice"] = absent ? "auto" : "none";
        if (absent) { body["tools"] = new JsonArray(); }
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That((await control.GetConversationsAsync()).Count).IsEqualTo(0);
        await CompleteAsync(http, code);
        if (graph) { await Assert.That((await control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1); }
    }
}
