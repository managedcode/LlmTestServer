using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeFileCodeIntegrationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DuplicateDeclaredResources_ArePreservedAndMatchedAsSetsAsync(bool code)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(Configuration(code));
        var body = Request(code, auto: code);
        if (code) { body["tools"]![0]!["container"]!["file_ids"] = new JsonArray(_inputFile, _inputFile); }
        else { body["tools"]![0]!["vector_store_ids"] = new JsonArray(_storeId, _storeId, "vs_extra"); }
        await CompleteAsync(http, code, body);
        var trace = (await control.GetRequestsAsync()).Requests.Single();
        await Assert.That(trace.NativeTools.Count).IsEqualTo(1);
        var count = code ? trace.NativeTools[0].CodeInterpreterOptions!.FileIds.Count
            : trace.NativeTools[0].FileSearchOptions!.VectorStoreIds.Count;
        await Assert.That(count).IsEqualTo(code ? 2 : 3);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EveryFileResult_MustRespectTheDeclaredFilterAndScoreAsync(bool score)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var configuration = Configuration(false);
        var item = (LlmTestServerResponseFileSearchItem)Items(false)[0];
        var other = item.Results[0] with
        {
            FileId = "file_second",
            Score = score ? 0.1 : 0.9,
            Attributes = new() { ["category"] = JsonSerializer.SerializeToElement("other") },
        };
        configuration.ChatScenarios[0].Responses[0] = configuration.ChatScenarios[0].Responses[0] with
        { OutputItems = [item with { Results = [item.Results[0], other] }, Items(false)[1]] };
        await new LlmTestServerClient(http).ConfigureAsync(configuration);
        var body = Request(false);
        if (score) { body["tools"]![0]!["ranking_options"] = new JsonObject { ["score_threshold"] = 0.5 }; }
        else { body["tools"]![0]!["filters"] = Comparison(); }
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var accepted = await CompleteAsync(http, false);
        await Assert.That(accepted.GetProperty("output")[0].GetProperty("results").GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    [Arguments(0d)]
    [Arguments(1d)]
    public async Task FileAttributeAndScoreBoundaries_ArePreservedAsync(double score)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var configuration = Configuration(false);
        var item = (LlmTestServerResponseFileSearchItem)Items(false)[0];
        var attributes = new Dictionary<string, JsonElement> { [new string('k', 64)] = JsonSerializer.SerializeToElement(new string('v', 512)) };
        for (var index = 1; index < 16; index++) { attributes[$"key{index}"] = JsonSerializer.SerializeToElement(index); }
        configuration.ChatScenarios[0].Responses[0] = configuration.ChatScenarios[0].Responses[0] with
        { OutputItems = [item with { Results = [item.Results[0] with { Score = score, Attributes = attributes }] }, Items(false)[1]] };
        await new LlmTestServerClient(http).ConfigureAsync(configuration);
        var body = Request(false); body["tools"]![0]!["ranking_options"] = new JsonObject { ["score_threshold"] = score };
        var result = (await CompleteAsync(http, false, body)).GetProperty("output")[0].GetProperty("results")[0];
        await Assert.That(result.GetProperty("score").GetDouble()).IsEqualTo(score);
        await Assert.That(result.GetProperty("attributes").EnumerateObject().Count()).IsEqualTo(16);
        await Assert.That(result.GetProperty("attributes").GetProperty(new string('k', 64)).GetString()).IsEqualTo(new string('v', 512));
    }
}
