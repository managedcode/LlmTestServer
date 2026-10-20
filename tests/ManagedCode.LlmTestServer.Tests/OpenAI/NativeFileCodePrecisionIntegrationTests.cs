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
    [Arguments("1e-29", "2e-29")]
    [Arguments("9007199254740993", "9007199254740992")]
    public async Task FileNumericFilters_DoNotRoundDistinctJsonNumbersIntoFalseMatchesAsync(string configured, string different)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var configuration = Configuration(false);
        var native = (LlmTestServerResponseFileSearchItem)Items(false)[0];
        var result = native.Results[0] with
        { Attributes = new() { ["precision"] = JsonSerializer.Deserialize<JsonElement>(configured) } };
        configuration.ChatScenarios[0].Responses[0] = configuration.ChatScenarios[0].Responses[0] with
        { OutputItems = [native with { Results = [result] }, Items(false)[1]] };
        await new LlmTestServerClient(http).ConfigureAsync(configuration);
        var body = Request(false);
        body["tools"]![0]!["filters"] = new JsonObject
        { ["type"] = "eq", ["key"] = "precision", ["value"] = JsonNode.Parse(different) };
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        body["tools"]![0]!["filters"]!["value"] = JsonNode.Parse(configured);
        var accepted = await CompleteAsync(http, false, body);
        await Assert.That(accepted.GetProperty("output")[0].GetProperty("results")[0].GetProperty("file_id").GetString()).IsEqualTo(_fileId);
    }
    [Test]
    [Arguments("1", "1.0", "eq", true)]
    [Arguments("1", "1e0", "eq", true)]
    [Arguments("0", "-0", "eq", true)]
    [Arguments("-0", "0.000e200", "eq", true)]
    [Arguments("1e200", "10e199", "eq", true)]
    [Arguments("1e-200", "10e-201", "eq", true)]
    [Arguments("1e-1000000", "2e-1000000", "eq", false)]
    [Arguments("1e-1000000", "10e-1000001", "eq", true)]
    [Arguments("12345678901234567890123456789012345678901234567890", "12345678901234567890123456789012345678901234567891", "eq", false)]
    [Arguments("1e-29", "2e-29", "ne", true)]
    [Arguments("2e-29", "1e-29", "gt", true)]
    [Arguments("1e-29", "1e-29", "gte", true)]
    [Arguments("-2e-29", "-1e-29", "lt", true)]
    [Arguments("-2e-29", "-2e-29", "lte", true)]
    [Arguments("-2e-29", "-1e-29", "gt", false)]
    [Arguments("-0", "0", "gt", false)]
    [Arguments("1e-29", "[2e-29,3e-29]", "in", false)]
    [Arguments("1e-29", "[2e-29,3e-29]", "nin", true)]
    [Arguments("1e-29", "[0.00000000000000000000000000001]", "in", true)]
    [Arguments("1e-29", "[1e-29]", "nin", false)]
    [Arguments("1", "[1.0]", "nin", false)]
    [Arguments("1", "[1e0]", "in", true)]
    public async Task FileNumericFilterOperators_PreserveExactJsonValueOrderingAsync(string configured, string compared, string operation, bool matches)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var configuration = Configuration(false);
        var native = (LlmTestServerResponseFileSearchItem)Items(false)[0];
        var result = native.Results[0] with
        { Attributes = new() { ["precision"] = JsonSerializer.Deserialize<JsonElement>(configured) } };
        configuration.ChatScenarios[0].Responses[0] = configuration.ChatScenarios[0].Responses[0] with
        { OutputItems = [native with { Results = [result] }, Items(false)[1]] };
        await new LlmTestServerClient(http).ConfigureAsync(configuration);
        var body = Request(false);
        body["tools"]![0]!["filters"] = new JsonObject
        { ["type"] = operation, ["key"] = "precision", ["value"] = JsonNode.Parse(compared) };
        using var response = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(response.StatusCode).IsEqualTo(matches ? HttpStatusCode.OK : HttpStatusCode.Conflict);
        if (!matches)
        {
            body["tools"]![0]!["filters"]!["type"] = "eq";
            body["tools"]![0]!["filters"]!["value"] = JsonNode.Parse(configured);
            var preserved = await CompleteAsync(http, false, body);
            await Assert.That(preserved.GetProperty("output")[0].GetProperty("results")[0].GetProperty("file_id").GetString()).IsEqualTo(_fileId);
        }
        else
        {
            var accepted = await response.Content.ReadFromJsonAsync<JsonElement>();
            await Assert.That(accepted.GetProperty("output")[0].GetProperty("results")[0].GetProperty("file_id").GetString()).IsEqualTo(_fileId);
        }
    }
}
