using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Decisions;

public sealed class DecisionFixtureContractTests
{
    [Test]
    [Arguments("jev-latest", "confidence")]
    [Arguments("kev-latest", "domain")]
    [Arguments("clef", "confidence")]
    [Arguments("clef-flash", "domain")]
    [Arguments("gpt-6-luna", "confidence")]
    [Arguments("gpt-6-luna", "mass")]
    [Arguments("clef", "argmax")]
    [Arguments("clef", "max-confidence")]
    public async Task NativeFixtures_RejectIncompleteOrInvalidNormalResponsesAsync(string model, string defect)
    {
        var scenario = DecisionEndpointTests.Scenario(model); var key = model == "gpt-6-luna" ? "1" : "choice"; var answer = scenario.Answers[key];
        if (defect == "confidence") { scenario.Answers[key] = answer with { Confidence = null }; }
        if (defect == "domain") { answer.Probabilities.Remove(answer.Probabilities.Keys.Last()); }
        if (defect == "mass") { answer.Probabilities[answer.Probabilities.Keys.Last()] = 0.95; }
        if (defect == "argmax") { scenario.Answers[key] = answer with { Choice = answer.Probabilities.Keys.Last() }; }
        if (defect == "max-confidence") { scenario.Answers[key] = answer with { Confidence = 0.5 }; }
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddDecisionModel(model, Provider(model)).AddDecisionScenario(scenario));
        using var client = host.GetTestClient(); using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor(model), DecisionEndpointTests.Body(model));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task MalformedResponseFixture_ExplicitlyPreservesBadNativeProbabilityMassAsync()
    {
        var scenario = DecisionEndpointTests.Scenario("gpt-6-luna") with { AllowMalformedResponse = true };
        foreach (var key in scenario.Answers["1"].Probabilities.Keys) { scenario.Answers["1"].Probabilities[key] = 0.95; }
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddOpenAiDecisionModel("gpt-6-luna").AddDecisionScenario(scenario));
        using var client = host.GetTestClient(); using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("gpt-6-luna"), DecisionEndpointTests.Body("gpt-6-luna")); response.EnsureSuccessStatusCode();
        var probabilities = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("answers")[1].GetProperty("probabilities");
        await Assert.That(probabilities.EnumerateArray().Sum(value => value.GetProperty("probability").GetDouble())).IsEqualTo(1.9);
    }

    [Test]
    [Arguments("kev-latest")]
    [Arguments("clef")]
    [Arguments("gpt-6-luna")]
    public async Task NativeFixtures_AcceptRoundedMassAndPreserveExplicitScoreAsync(string model)
    {
        var scenario = DecisionEndpointTests.Scenario(model); var key = model == "gpt-6-luna" ? "2" : "score";
        scenario.Answers[key] = scenario.Answers[key] with
        {
            Score = 1,
            Confidence = model == "clef" ? 0.3333 : 0,
            Probabilities = new() { ["0"] = 0.3333, ["1"] = 0.3333, ["2"] = model == "gpt-6-luna" ? 0.3333995 : 0.3333 }
        };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddDecisionModel(model, Provider(model)).AddDecisionScenario(scenario));
        using var client = host.GetTestClient(); using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor(model), DecisionEndpointTests.Body(model)); response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(); if (model == "clef") { payload = payload.GetProperty("result"); }
        var score = model == "gpt-6-luna" ? payload.GetProperty("answers")[2] : payload.GetProperty("answers").GetProperty("score");
        await Assert.That(score.GetProperty("score").GetDouble()).IsEqualTo(1);
    }

    [Test]
    [Arguments("\"other-id\"")]
    [Arguments("true")]
    [Arguments("false")]
    public async Task OpenAiFixtures_PreserveSelectedValueAndIndependentConfidenceAsync(string selectedValueJson)
    {
        var selected = JsonSerializer.Deserialize<JsonElement>(selectedValueJson);
        var selectedId = selected.ValueKind == JsonValueKind.String
            ? ManagedCode.LlmTestServer.OpenAI.OpenAiDecisionFixtureIds.ForChoiceValue(selected.GetString()!)
            : ManagedCode.LlmTestServer.OpenAI.OpenAiDecisionFixtureIds.ForChoiceValue(selected.GetBoolean());
        var otherId = ManagedCode.LlmTestServer.OpenAI.OpenAiDecisionFixtureIds.ForChoiceValue("opaque-id");
        var scenario = DecisionEndpointTests.Scenario("gpt-6-luna");
        scenario.Answers["1"] = scenario.Answers["1"] with
        {
            Choice = selectedId,
            Confidence = 0.4,
            Probabilities = new() { [selectedId] = 0.95, [otherId] = 0.05 },
        };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddOpenAiDecisionModel("gpt-6-luna").AddDecisionScenario(scenario));
        using var client = host.GetTestClient();
        var body = DecisionEndpointTests.Body("gpt-6-luna");
        body["questions"]![1]!["choices"] = new JsonArray(
            new JsonObject { ["value"] = JsonNode.Parse(selectedValueJson), ["description"] = "selected" },
            new JsonObject { ["value"] = "opaque-id", ["description"] = "other" });
        using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("gpt-6-luna"), body);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var answer = payload.GetProperty("answers")[1];
        await Assert.That(answer.GetProperty("choice").GetRawText()).IsEqualTo(selected.GetRawText());
        var probabilities = answer.GetProperty("probabilities").EnumerateArray().ToArray();
        await Assert.That(probabilities.Single(entry => entry.GetProperty("probability").GetDouble() == 0.95).GetProperty("value").GetRawText()).IsEqualTo(selected.GetRawText());
        await Assert.That(answer.GetProperty("confidence").GetDouble()).IsEqualTo(0.4);
        await Assert.That(payload.GetProperty("answers")[2].GetProperty("probabilities")[0].GetProperty("value").ValueKind).IsEqualTo(JsonValueKind.Number);
    }

    [Test]
    public async Task OpenAiFixtures_PredicateDoesNotLeakOtherKindFieldsAsync()
    {
        var scenario = DecisionEndpointTests.Scenario("gpt-6-luna");
        scenario.Answers["0"] = scenario.Answers["0"] with { Score = 1, Confidence = 0.9, Choice = "not-a-request-choice" };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddOpenAiDecisionModel("gpt-6-luna").AddDecisionScenario(scenario));
        using var client = host.GetTestClient(); using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("gpt-6-luna"), DecisionEndpointTests.Body("gpt-6-luna")); response.EnsureSuccessStatusCode();
        var answer = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("answers")[0];
        await Assert.That(answer.EnumerateObject().Select(item => item.Name)).IsEquivalentTo(new[] { "type", "name", "probability" });
    }

    [Test]
    public async Task KevFixtures_RequireMaximalChoiceEvenForWideProbabilityDomainsAsync()
    {
        var scenario = DecisionEndpointTests.Scenario("kev-latest");
        var body = DecisionEndpointTests.Body("kev-latest"); var criteria = new JsonObject(); var probabilities = new Dictionary<string, double>();
        for (var index = 0; index < ManagedCode.LlmTestServer.SystemOne.SystemOneDecisionMapper.MaxChoices; index++)
        { var key = index.ToString(System.Globalization.CultureInfo.InvariantCulture); criteria[key] = key; probabilities[key] = 0; }
        probabilities["0"] = 0.495; probabilities["1"] = 0.505;
        body["questions"]!["choice"]!["criteria"] = criteria;
        scenario.Answers["choice"] = scenario.Answers["choice"] with { Choice = "0", Probabilities = probabilities };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddKevLatest().AddDecisionScenario(scenario));
        using var client = host.GetTestClient(); using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("kev-latest"), body);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task CloudflareEndpoint_RejectsMismatchedNativeConfidenceAsync()
    {
        var scenario = DecisionEndpointTests.Scenario("clef");
        scenario.Answers["choice"] = scenario.Answers["choice"] with { Confidence = 0.9 };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddClef().AddDecisionScenario(scenario));
        using var http = host.GetTestClient();
        using var response = await http.PostAsJsonAsync(DecisionEndpointTests.PathFor("clef"), DecisionEndpointTests.Body("clef"));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean()).IsFalse();
    }

    private static LlmTestServerDecisionProvider Provider(string model)
    {
        return model switch
        { "kev-latest" => LlmTestServerDecisionProvider.Kev, "jev-latest" => LlmTestServerDecisionProvider.TypeSafe, "gpt-6-luna" => LlmTestServerDecisionProvider.OpenAI, _ => LlmTestServerDecisionProvider.Cloudflare };
    }
}
