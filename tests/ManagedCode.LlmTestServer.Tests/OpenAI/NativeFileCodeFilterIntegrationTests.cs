using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeFileCodeIntegrationTests
{
    [Test]
    [Arguments("""{"type":"eq","key":"category","value":"fixture"}""")]
    [Arguments("""{"type":"ne","key":"category","value":"other"}""")]
    [Arguments("""{"type":"gt","key":"year","value":2025}""")]
    [Arguments("""{"type":"gte","key":"year","value":2026}""")]
    [Arguments("""{"type":"lt","key":"year","value":2027}""")]
    [Arguments("""{"type":"lte","key":"year","value":2026}""")]
    [Arguments("""{"type":"in","key":"category","value":["fixture","other"]}""")]
    [Arguments("""{"type":"nin","key":"category","value":["other"]}""")]
    [Arguments("""{"type":"in","key":"year","value":[2026,2027]}""")]
    [Arguments("""{"type":"eq","key":"approved","value":true}""")]
    [Arguments("""{"type":"and","filters":[{"type":"eq","key":"category","value":"fixture"},{"type":"gte","key":"year","value":2026}]}""")]
    [Arguments("""{"type":"or","filters":[{"type":"eq","key":"absent","value":1},{"type":"eq","key":"approved","value":true}]}""")]
    public async Task FileFilters_AdmitOnlyMatchingConfiguredResultsAsync(string filter)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration(false));
        var body = Request(false); body["tools"]![0]!["filters"] = JsonNode.Parse(filter);
        var result = await CompleteAsync(http, false, body);
        await Assert.That(result.GetProperty("output")[0].GetProperty("results")[0].GetProperty("file_id").GetString()).IsEqualTo(_fileId);
    }

    [Test]
    [Arguments("""{"type":"eq","key":"category","value":"other"}""")]
    [Arguments("""{"type":"gt","key":"year","value":2026}""")]
    [Arguments("""{"type":"eq","key":"year","value":"2026"}""")]
    [Arguments("""{"type":"ne","key":"absent","value":1}""")]
    [Arguments("""{"type":"nin","key":"absent","value":[1]}""")]
    [Arguments("""{"type":"eq","key":"absent","value":1}""")]
    [Arguments("""{"type":"gt","key":"absent","value":1}""")]
    [Arguments("""{"type":"and","filters":[{"type":"eq","key":"approved","value":true},{"type":"eq","key":"category","value":"other"}]}""")]
    public async Task NonmatchingFileFilters_Return409WithoutConsumingFixtureAsync(string filter)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration(false));
        var body = Request(false); body["tools"]![0]!["filters"] = JsonNode.Parse(filter);
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var result = await CompleteAsync(http, false);
        await Assert.That(result.GetProperty("output")[0].GetProperty("id").GetString()).IsEqualTo(ItemId(false));
    }

    [Test]
    [Arguments(1, "auto", 0d)]
    [Arguments(50, "default-2024-11-15", 0.9d)]
    public async Task FileRankingAndResultCount_RespectAcceptedBoundariesAsync(int maximum, string ranker, double threshold)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration(false));
        var body = Request(false);
        body["tools"]![0]!["max_num_results"] = maximum;
        body["tools"]![0]!["ranking_options"] = new JsonObject { ["ranker"] = ranker, ["score_threshold"] = threshold };
        await CompleteAsync(http, false, body);
    }

    [Test]
    [Arguments("score")]
    [Arguments("maximum")]
    [Arguments("store")]
    [Arguments("emptyStore")]
    public async Task FileFixtureConstraints_Return409BeforeQueueConsumptionAsync(string mismatch)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var configuration = Configuration(false);
        if (mismatch == "maximum")
        {
            var item = (LlmTestServerResponseFileSearchItem)Items(false)[0];
            configuration.ChatScenarios[0].Responses[0] = configuration.ChatScenarios[0].Responses[0] with
            { OutputItems = [item with { Results = [item.Results![0], item.Results[0] with { FileId = "file_second" }] }, Items(false)[1]] };
        }
        await new LlmTestServerClient(http).ConfigureAsync(configuration);
        var body = Request(false);
        if (mismatch == "score") { body["tools"]![0]!["ranking_options"] = new JsonObject { ["score_threshold"] = 1d }; }
        if (mismatch == "maximum") { body["tools"]![0]!["max_num_results"] = 1; }
        if (mismatch == "store") { body["tools"]![0]!["vector_store_ids"] = new JsonArray("vs_foreign"); }
        if (mismatch == "emptyStore") { body["tools"]![0]!["vector_store_ids"] = new JsonArray(); }
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await CompleteAsync(http, false);
    }

    [Test]
    [Arguments(8, 128)]
    public async Task FileFilterTree_AdmitsDepthAndNodeBoundaryAsync(int depth, int nodes)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration(false, 2));
        JsonNode nested = Comparison();
        for (var index = 1; index < depth; index++) { nested = new JsonObject { ["type"] = "and", ["filters"] = new JsonArray(nested) }; }
        var body = Request(false); body["tools"]![0]!["filters"] = nested;
        await CompleteAsync(http, false, body);
        var leaves = new JsonArray();
        for (var index = 1; index < nodes; index++) { leaves.Add(Comparison()); }
        body = Request(false); body["tools"]![0]!["filters"] = new JsonObject { ["type"] = "and", ["filters"] = leaves };
        await CompleteAsync(http, false, body);
    }

    private static JsonObject Comparison()
    {
        return new() { ["type"] = "eq", ["key"] = "category", ["value"] = "fixture" };
    }
}
