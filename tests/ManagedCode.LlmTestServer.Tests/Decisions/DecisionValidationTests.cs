using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Cloudflare;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.OpenAI;
using ManagedCode.LlmTestServer.SystemOne;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Decisions;

public sealed class DecisionValidationTests
{
    [Test]
    [Arguments("state")]
    [Arguments("empty")]
    [Arguments("type")]
    [Arguments("instructions")]
    [Arguments("criteria")]
    [Arguments("duplicates")]
    public async Task SystemOneValidation_RejectsInvalidNativeShapeAsync(string kind)
    {
        var body = DecisionEndpointTests.Body("jev-latest");
        switch (kind)
        {
            case "state": body["state"] = 42; break;
            case "empty": body["questions"] = new JsonObject(); break;
            case "type": body["questions"]!["binary"]!["type"] = "unsupported"; break;
            case "instructions": body["questions"]!["binary"]!["instructions"] = false; break;
            case "criteria": body["questions"]!["choice"]!["criteria"] = new JsonArray(); break;
            default:
                var duplicate = """{"model":"jev-latest","state":"text","questions":{"x":{"type":"noul"},"x":{"type":"noul"}}}""";
                await AssertRejectedAsync("/systemone/v1/systemone", duplicate, HttpStatusCode.UnprocessableEntity); return;
        }
        await AssertRejectedAsync("/systemone/v1/systemone", body.ToJsonString(), HttpStatusCode.UnprocessableEntity);
    }

    [Test]
    [Arguments("type")]
    [Arguments("null-name")]
    [Arguments("options")]
    [Arguments("description")]
    [Arguments("label")]
    public async Task OpenAiValidation_RejectsInvalidNativeShapeAsync(string kind)
    {
        var body = DecisionEndpointTests.Body("gpt-6-luna");
        if (kind == "type") { body["questions"]![0]!["type"] = "unsupported"; }
        if (kind == "null-name") { body["questions"]![1]!["name"] = null; }
        if (kind == "options") { body["questions"]![1]!["choices"] = new JsonArray(); }
        if (kind == "label") { body["questions"]![2]!["levels"]![0]!["label"] = true; }
        if (kind == "description") { body["questions"]![2]!["levels"]![0]!["description"] = false; }
        await AssertRejectedAsync("/openai/v1/decisions", body.ToJsonString(), HttpStatusCode.BadRequest);
    }

    [Test]
    [Arguments("string")]
    [Arguments("array")]
    [Arguments("true-only")]
    [Arguments("false-only")]
    [Arguments("object-instructions")]
    public async Task SystemOneValidation_AcceptsOptionalPredicateCriteriaAndJsonContentAsync(string kind)
    {
        var body = DecisionEndpointTests.Body("jev-latest");
        body["questions"]!["binary"]!.AsObject().Remove("criteria");
        if (kind == "string") { body["state"] = "content"; }
        if (kind == "array") { body["state"] = new JsonArray("content"); }
        if (kind == "true-only") { body["questions"]!["binary"]!["criteria"] = new JsonObject { ["true"] = "yes" }; }
        if (kind == "false-only") { body["questions"]!["binary"]!["criteria"] = new JsonObject { ["false"] = new JsonArray("no") }; }
        if (kind == "object-instructions") { body["questions"]!["binary"]!["instructions"] = new JsonObject { ["task"] = "classify" }; }
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddJevLatest().AddDecisionScenario(DecisionEndpointTests.Scenario("jev-latest")));
        using var client = host.GetTestClient();
        using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("jev-latest"), body);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    [Arguments("id")]
    [Arguments("length")]
    [Arguments("count")]
    [Arguments("model")]
    [Arguments("instructions")]
    [Arguments("choice")]
    [Arguments("score")]
    public async Task CloudflareValidation_EnforcesNativeQuestionAndModelLimitsAsync(string kind)
    {
        var body = DecisionEndpointTests.Body("clef");
        if (kind == "id") { body["questions"]!["не-ascii"] = new JsonObject { ["type"] = "noul", ["instructions"] = "question?" }; }
        if (kind == "length") { body["questions"]![new string('a', CloudflareDecisionValidation.MaxQuestionIdLength + 1)] = new JsonObject { ["type"] = "noul", ["instructions"] = "question?" }; }
        if (kind == "count") { for (var index = 0; index < CloudflareDecisionValidation.MaxQuestions; index++) { body["questions"]!["q" + index] = new JsonObject { ["type"] = "noul", ["instructions"] = "question?" }; } }
        if (kind == "instructions") { body["questions"]!["binary"]!.AsObject().Remove("instructions"); }
        if (kind == "choice") { body["questions"]!["choice"]!["criteria"] = new JsonObject { ["only"] = "one" }; }
        if (kind == "score") { body["questions"]!["score"]!["criteria"] = new JsonArray("one"); }
        if (kind == "model") { body["model"] = "clef-flash"; }
        using var host = await LlmTestServerTestHost.StartAsync(); using var client = host.GetTestClient();
        using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("clef"), body);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task DecisionProfiles_EnforceJevScoreLimitAndKevNativeRoundingAsync()
    {
        var fixture = DecisionEndpointTests.Scenario("kev-latest"); fixture.Answers["binary"] = fixture.Answers["binary"] with { Probability = 0.12345678 };
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddJevLatest().AddKevLatest().AddDecisionScenario(fixture)); using var client = host.GetTestClient();
        var body = DecisionEndpointTests.Body("jev-latest"); body["questions"]!["score"]!["criteria"] = new JsonArray(Enumerable.Range(0, SystemOneDecisionMapper.JevMaxScoreLevels + 1).Select(index => JsonValue.Create(index.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray<JsonNode?>());
        using var invalid = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("jev-latest"), body);
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
        using var rounded = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("kev-latest"), DecisionEndpointTests.Body("kev-latest"));
        rounded.EnsureSuccessStatusCode();
        var payload = await rounded.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("answers").GetProperty("binary").GetProperty("noul").GetDouble()).IsEqualTo(0.1235);
    }
    [Test]
    public async Task OpenAiDecisions_PreserveBooleanChoicesAndUserMessageArrayAsync()
    {
        var scenario = DecisionEndpointTests.Scenario("gpt-6-luna");
        scenario.Answers.Clear();
        scenario.Answers[OpenAiDecisionFixtureIds.ForQuestionIndex(0)] = new() { Kind = LlmTestServerDecisionKind.Choice, Choice = "true", Confidence = 0.9, Probabilities = new() { ["true"] = 0.9, ["false"] = 0.1 } };
        var body = DecisionEndpointTests.Body("gpt-6-luna");
        body["input"] = JsonNode.Parse("""[{"role":"user","content":"evidence"}]""");
        body["questions"] = JsonNode.Parse("""[{"type":"choice","name":"choice","instructions":"is urgent?","choices":[{"value":true,"description":"urgent"},{"value":false,"description":"routine"}]}]""");
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddOpenAiDecisionModel("gpt-6-luna").AddDecisionScenario(scenario));
        using var client = host.GetTestClient(); using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("gpt-6-luna"), body);
        response.EnsureSuccessStatusCode(); var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var answer = payload.GetProperty("answers")[0];
        await Assert.That(answer.GetProperty("choice").GetBoolean()).IsTrue();
        await Assert.That(answer.GetProperty("probabilities")[0].GetProperty("value").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task SystemOneEndpoint_AcceptsOmittedOptionalPredicatePropertiesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddKevLatest()
            .AddDecisionScenario(DecisionEndpointTests.Scenario("kev-latest")));
        using var http = host.GetTestClient();
        var body = DecisionEndpointTests.Body("kev-latest");
        body["questions"]!["binary"] = new JsonObject { ["type"] = SystemOneDecisionTypes.Predicate };
        using var response = await http.PostAsJsonAsync("/systemone/v1/systemone", body);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("answers").GetProperty("binary").GetProperty("noul").GetDouble()).IsEqualTo(0.95);
    }

    [Test]
    public async Task OpenAiDecisions_PreserveDistinctBooleanAndStringChoiceValuesAsync()
    {
        var scenario = DecisionEndpointTests.Scenario("gpt-6-luna");
        scenario.Answers.Clear();
        var booleanId = OpenAiDecisionFixtureIds.ForChoiceValue(true);
        var stringId = OpenAiDecisionFixtureIds.ForChoiceValue("true");
        scenario.Answers[OpenAiDecisionFixtureIds.ForQuestionIndex(0)] = new() { Kind = LlmTestServerDecisionKind.Choice, Choice = booleanId, Confidence = 0.9, Probabilities = new() { [booleanId] = 0.9, [stringId] = 0.1 } };
        var body = DecisionEndpointTests.Body("gpt-6-luna");
        body["questions"] = JsonNode.Parse("""[{"type":"choice","name":"choice","instructions":"which?","choices":[{"value":true,"description":"boolean"},{"value":"true","description":"text"}]}]""");
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddOpenAiDecisionModel("gpt-6-luna").AddDecisionScenario(scenario));
        using var client = host.GetTestClient(); using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("gpt-6-luna"), body);
        response.EnsureSuccessStatusCode(); var answer = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("answers")[0];
        await Assert.That(answer.GetProperty("choice").GetBoolean()).IsTrue();
        await Assert.That(answer.GetProperty("probabilities")[0].GetProperty("value").ValueKind).IsEqualTo(JsonValueKind.True);
        await Assert.That(answer.GetProperty("probabilities")[1].GetProperty("value").GetString()).IsEqualTo("true");
    }
    private static async Task AssertRejectedAsync(string path, string body, HttpStatusCode status)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(path, content);
        await Assert.That(response.StatusCode).IsEqualTo(status);
    }
}
