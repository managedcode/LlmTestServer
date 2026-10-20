using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.OpenAI;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Decisions;

public sealed class DecisionQuestionTests
{
    [Test]
    public async Task OpenAiQuestions_PreserveUnnamedDuplicateAndEmptyNamesByPositionAsync()
    {
        var scenario = DecisionEndpointTests.Scenario("gpt-6-luna"); scenario.Answers.Clear();
        scenario.Answers[OpenAiDecisionFixtureIds.ForQuestionIndex(0)] = new() { Refused = true };
        for (var index = 1; index < 4; index++) { scenario.Answers[OpenAiDecisionFixtureIds.ForQuestionIndex(index)] = new() { Probability = 0.1 * index }; }
        var body = DecisionEndpointTests.Body("gpt-6-luna");
        body["questions"] = JsonNode.Parse("""[{"type":"predicate","instructions":""},{"type":"predicate","name":"same","instructions":" "},{"type":"predicate","name":"same","instructions":""},{"type":"predicate","name":"","instructions":""}]""");
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddOpenAiDecisionModel("gpt-6-luna").AddDecisionScenario(scenario));
        using var client = host.GetTestClient(); using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("gpt-6-luna"), body);
        response.EnsureSuccessStatusCode(); var answers = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("answers");
        await Assert.That(answers[0].GetProperty("name").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(answers[0].GetProperty("type").GetString()).IsEqualTo("refusal");
        await Assert.That(answers[1].GetProperty("name").GetString()).IsEqualTo("same");
        await Assert.That(answers[2].GetProperty("probability").GetDouble()).IsEqualTo(0.2);
        await Assert.That(answers[3].GetProperty("name").GetString()).IsEqualTo("");
    }

    [Test]
    public async Task OpenAiQuestions_PreserveEmptyChoiceAndRepeatedScoreLabelsWithoutDescriptionsAsync()
    {
        var scenario = DecisionEndpointTests.Scenario("gpt-6-luna"); scenario.Answers.Remove("0");
        scenario.Answers["0"] = new() { Kind = LlmTestServerDecisionKind.Choice, Choice = OpenAiDecisionFixtureIds.ForChoiceValue(""), Confidence = 0.9, Probabilities = new() { [OpenAiDecisionFixtureIds.ForChoiceValue("")] = 0.9, [OpenAiDecisionFixtureIds.ForChoiceValue(" ")] = 0.1 } };
        scenario.Answers["1"] = scenario.Answers["2"]; scenario.Answers.Remove("2");
        var body = DecisionEndpointTests.Body("gpt-6-luna");
        body["questions"] = JsonNode.Parse("""[{"type":"choice","instructions":"","choices":[{"value":""},{"value":" "}]},{"type":"score","instructions":"","levels":[{"label":""},{"label":"same"},{"label":"same"}]}]""");
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddOpenAiDecisionModel("gpt-6-luna").AddDecisionScenario(scenario));
        using var client = host.GetTestClient(); using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("gpt-6-luna"), body);
        response.EnsureSuccessStatusCode(); var answers = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("answers");
        await Assert.That(answers[0].GetProperty("choice").GetString()).IsEqualTo("");
        await Assert.That(answers[1].GetProperty("probabilities")[0].GetProperty("label").GetString()).IsEqualTo("");
        await Assert.That(answers[1].GetProperty("probabilities")[1].GetProperty("label").GetString()).IsEqualTo("same");
        await Assert.That(answers[1].GetProperty("probabilities")[2].GetProperty("label").GetString()).IsEqualTo("same");
    }

    [Test]
    [Arguments("duplicate-choice")]
    [Arguments("too-many-choices")]
    [Arguments("null-description")]
    [Arguments("long-name")]
    [Arguments("long-model")]
    [Arguments("safety-identifier")]
    public async Task OpenAiQuestions_EnforceDocumentedNativeFieldConstraintsAsync(string kind)
    {
        var body = DecisionEndpointTests.Body("gpt-6-luna");
        if (kind == "duplicate-choice") { body["questions"]![1]!["choices"]![1]!["value"] = "opaque-id"; }
        if (kind == "too-many-choices") { body["questions"]![1]!["choices"] = new JsonArray(Enumerable.Range(0, OpenAiDecisionMapper.MaxChoices + 1).Select(index => (JsonNode)new JsonObject { ["value"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ToArray()); }
        if (kind == "null-description") { body["questions"]![1]!["choices"]![0]!["description"] = null; }
        if (kind == "long-name") { body["questions"]![0]!["name"] = new string('a', OpenAiDecisionMapper.MaxQuestionTextLength + 1); }
        if (kind == "long-model") { body["model"] = new string('a', OpenAiDecisionMapper.MaxQuestionTextLength + 1); }
        if (kind == "safety-identifier") { body["safety_identifier"] = false; }
        using var host = await LlmTestServerTestHost.StartAsync(); using var client = host.GetTestClient();
        using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("gpt-6-luna"), body);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task OpenAiQuestions_LeaveUndocumentedCountToFixtureMatchingAsync()
    {
        var scenario = DecisionEndpointTests.Scenario("gpt-6-luna"); scenario.Answers.Clear();
        var body = DecisionEndpointTests.Body("gpt-6-luna"); body["questions"] = new JsonArray();
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddOpenAiDecisionModel("gpt-6-luna").AddDecisionScenario(scenario));
        using var client = host.GetTestClient(); using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("gpt-6-luna"), body);
        response.EnsureSuccessStatusCode();
        await Assert.That((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("answers").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task OpenAiQuestions_PreserveChoiceValuesWithoutUndocumentedTextLimitAsync()
    {
        var value = new string('a', OpenAiDecisionMapper.MaxQuestionTextLength + 1);
        var choiceId = OpenAiDecisionFixtureIds.ForChoiceValue(value);
        var otherId = OpenAiDecisionFixtureIds.ForChoiceValue(false);
        var scenario = DecisionEndpointTests.Scenario("gpt-6-luna"); scenario.Answers.Clear();
        scenario.Answers[OpenAiDecisionFixtureIds.ForQuestionIndex(0)] = new()
        {
            Kind = LlmTestServerDecisionKind.Choice,
            Choice = choiceId,
            Confidence = 0.9,
            Probabilities = new() { [choiceId] = 0.9, [otherId] = 0.1 },
        };
        var body = DecisionEndpointTests.Body("gpt-6-luna");
        body["questions"] = new JsonArray(new JsonObject
        {
            ["type"] = OpenAiDecisionTypes.Choice,
            ["instructions"] = "",
            ["choices"] = new JsonArray(new JsonObject { ["value"] = value }, new JsonObject { ["value"] = false }),
        });
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddOpenAiDecisionModel("gpt-6-luna").AddDecisionScenario(scenario));
        using var client = host.GetTestClient(); using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("gpt-6-luna"), body);
        response.EnsureSuccessStatusCode(); var answer = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("answers")[0];
        await Assert.That(answer.GetProperty("choice").ValueEquals(value)).IsTrue();
        await Assert.That(answer.GetProperty("probabilities")[0].GetProperty("value").ValueEquals(value)).IsTrue();
    }
}
