using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.OpenAI;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Decisions;

public sealed class DecisionEndpointTests
{
    private const string _systemOnePath = "/systemone/v1/systemone";
    private const string _cloudflarePath = "/cloudflare/client/v4/accounts/test-account/ai/run/@cf/cloudflare/";
    private const string _openAiPath = "/openai/v1/decisions";
    private const string _key = "decision-test-key";

    [Test]
    [Arguments("jev-latest")]
    [Arguments("kev-latest")]
    [Arguments("clef")]
    [Arguments("clef-flash")]
    [Arguments("gpt-6-luna")]
    public async Task NativeDecisionEndpoints_PreserveAnswersAndUsageAsync(string model)
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddModel(model, LlmTestServerModelKind.Decision).AddDecisionScenario(Scenario(model)));
        using var client = host.GetTestClient();
        using var response = await client.PostAsJsonAsync(PathFor(model), Body(model));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (model.StartsWith("clef", StringComparison.Ordinal))
        { await Assert.That(payload.GetProperty("success").GetBoolean()).IsTrue(); payload = payload.GetProperty("result"); }
        var openAi = model == "gpt-6-luna";
        var answers = payload.GetProperty("answers");
        var predicate = openAi ? answers[0] : answers.GetProperty("binary");
        var choice = openAi ? answers[1] : answers.GetProperty("choice");
        var score = openAi ? answers[2] : answers.GetProperty("score");
        await Assert.That(predicate.GetProperty(openAi ? "probability" : "noul").GetDouble()).IsEqualTo(0.95);
        await Assert.That(choice.GetProperty("choice").GetString()).IsEqualTo("opaque-id");
        await Assert.That(score.GetProperty("score").GetDouble()).IsEqualTo(1.75);
        await Assert.That(score.GetProperty("confidence").GetDouble()).IsEqualTo(model is "jev-latest" or "kev-latest" ? 0.625 : 0.8);
        await Assert.That(payload.GetProperty("usage").GetProperty("input_tokens").GetInt32()).IsEqualTo(100);
        if (openAi)
        {
            await Assert.That(score.GetProperty("probabilities")[2].GetProperty("label").GetString()).IsEqualTo("high-id");
            await Assert.That(payload.GetProperty("usage").GetProperty("input_tokens_details").GetProperty("cached_tokens").GetInt32()).IsEqualTo(4);
            await Assert.That(payload.GetProperty("usage").GetProperty("input_tokens_details").GetProperty("cache_write_tokens").GetInt32()).IsEqualTo(0);
            await Assert.That(payload.GetProperty("usage").GetProperty("output_tokens_details").GetProperty("reasoning_tokens").GetInt32()).IsEqualTo(0);
        }
        else { await Assert.That(score.GetProperty("legend").GetProperty("2").GetString()).IsEqualTo("high"); }
        var summary = (await client.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!;
        await Assert.That(summary.Matched).IsEqualTo(1);
        await Assert.That(summary.Events.Single().ScenarioId).IsEqualTo("fixture-" + model);
        var trace = (await client.GetFromJsonAsync<List<LlmTestServerProviderHttpTrace>>("/admin-api/requests"))!.Single();
        await Assert.That(trace.ModelId).IsEqualTo(model);
    }

    [Test]
    [Arguments("jev-latest")]
    [Arguments("kev-latest")]
    [Arguments("clef")]
    [Arguments("clef-flash")]
    [Arguments("gpt-6-luna")]
    [Arguments("jev-latest", 429)]
    [Arguments("clef", 429)]
    [Arguments("gpt-6-luna", 429)]
    [Arguments("jev-latest", 400)]
    [Arguments("clef", 400)]
    [Arguments("gpt-6-luna", 400)]
    public async Task NativeDecisionEndpoints_EnforceAuthAndFaultsAsync(string model, int status = 401)
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder =>
        {
            builder.AddModel(model, LlmTestServerModelKind.Decision).AddDecisionScenario(Scenario(model));
            if (status == 401) { builder.RequireBearerToken(_key); }
            if (status == 429) { builder.SimulateRateLimitAfter(0); }
            if (status == 400) { builder.SimulateContentFilter("evidence"); }
        });
        using var client = host.GetTestClient();
        using var response = await client.PostAsJsonAsync(PathFor(model), Body(model));
        await Assert.That((int)response.StatusCode).IsEqualTo(status);
        if (status == 401)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", _key);
            using var accepted = await client.PostAsJsonAsync(PathFor(model), Body(model));
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
    }

    [Test]
    [Arguments("jev-latest", "model")]
    [Arguments("jev-latest", "state")]
    [Arguments("jev-latest", "questions")]
    [Arguments("gpt-6-luna", "model")]
    [Arguments("gpt-6-luna", "input")]
    [Arguments("gpt-6-luna", "questions")]
    public async Task NativeDecisionEndpoints_RejectMissingRequiredFieldsAsync(string model, string property)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var client = host.GetTestClient();
        var body = Body(model); body.Remove(property);
        using var response = await client.PostAsJsonAsync(PathFor(model), body);
        await Assert.That(response.StatusCode).IsEqualTo(model == "jev-latest" ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DecisionControlClient_ConfiguresFixturesAndMismatchIsObservableAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var client = host.GetTestClient();
        var control = new LlmTestServerClient(client);
        await control.ConfigureAsync(builder => builder.UseModel("jev-latest", LlmTestServerModelKind.Decision).UseDecisionScenario(Scenario("jev-latest")));
        var body = Body("jev-latest"); body["questions"]!["choice"]!["criteria"] = new JsonObject { ["another-id"] = "first", ["last-id"] = "last" };
        using var mismatch = await client.PostAsJsonAsync(_systemOnePath, body);
        await Assert.That(mismatch.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var good = await client.PostAsJsonAsync(_systemOnePath, Body("jev-latest"));
        await Assert.That(good.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var summary = await control.GetAssertionsAsync();
        await Assert.That(summary.ErrorsReturned).IsEqualTo(1);
    }

    internal static LlmTestServerDecisionScenario Scenario(string model)
    {
        var firstChoice = model == "gpt-6-luna" ? OpenAiDecisionFixtureIds.ForChoiceValue("opaque-id") : "opaque-id";
        var lastChoice = model == "gpt-6-luna" ? OpenAiDecisionFixtureIds.ForChoiceValue("other-id") : "other-id";
        return new()
        {
            Id = "fixture-" + model,
            ModelId = model,
            Usage = new() { InputTokens = 100, CachedInputTokens = 4, OutputTokens = 10, TotalTokens = 110 },
            Answers = new()
            {
                [model == "gpt-6-luna" ? OpenAiDecisionFixtureIds.ForQuestionIndex(0) : "binary"] = new() { Kind = LlmTestServerDecisionKind.Predicate, Probability = 0.95 },
                [model == "gpt-6-luna" ? OpenAiDecisionFixtureIds.ForQuestionIndex(1) : "choice"] = new() { Kind = LlmTestServerDecisionKind.Choice, Choice = firstChoice, Confidence = model.StartsWith("clef", StringComparison.Ordinal) ? 0.95 : 0.9, Probabilities = new() { [firstChoice] = 0.95, [lastChoice] = 0.05 } },
                [model == "gpt-6-luna" ? OpenAiDecisionFixtureIds.ForQuestionIndex(2) : "score"] = new() { Kind = LlmTestServerDecisionKind.Score, Score = 1.75, Confidence = model is "jev-latest" or "kev-latest" ? 0.625 : 0.8, Probabilities = new() { ["0"] = 0.05, ["1"] = 0.15, ["2"] = 0.8 } },
            },
        };
    }

    internal static string PathFor(string model)
    {
        return model == "gpt-6-luna" ? _openAiPath : model.StartsWith("clef", StringComparison.Ordinal) ? _cloudflarePath + model : _systemOnePath;
    }

    internal static JsonObject Body(string model)
    {
        const string systemOne = """{"state":{"evidence":"evidence"},"questions":{"binary":{"type":"noul","instructions":"binary?","criteria":{"true":"yes","false":"no"}},"choice":{"type":"choice","instructions":"choice?","criteria":{"opaque-id":"first","other-id":"last"}},"score":{"type":"score","instructions":"score?","criteria":["low","middle","high"]}}}""";
        const string openAi = """{"input":"evidence","questions":[{"type":"predicate","name":"binary","instructions":"binary?"},{"type":"choice","name":"choice","instructions":"choice?","choices":[{"value":"opaque-id","description":"first"},{"value":"other-id","description":"last"}]},{"type":"score","name":"score","instructions":"score?","levels":[{"label":"low-id","description":"low"},{"label":"middle-id","description":"middle"},{"label":"high-id","description":"high"}]}]}""";
        var body = JsonNode.Parse(model == "gpt-6-luna" ? openAi : systemOne)!.AsObject(); body["model"] = model; return body;
    }
}
