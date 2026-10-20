using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Decisions;

public sealed class DecisionRuntimeTests
{
    private const string _model = "jev-latest";
    private static readonly JsonSerializerOptions _configurationJson = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    [Test]
    [Arguments("unknown", 404)]
    [Arguments("unmatched", 404)]
    [Arguments("error", 503)]
    [Arguments("mismatch", 409)]
    [Arguments("score-key", 409)]
    [Arguments("score-high", 409)]
    [Arguments("wrong-input", 404)]
    public async Task DecisionFailures_AreObservableAsync(string kind, int status)
    {
        var builder = new LlmTestServerConfigurationBuilder();
        if (kind != "unknown") { builder.AddModel(_model, LlmTestServerModelKind.Decision); }
        var scenario = DecisionEndpointTests.Scenario(_model);
        if (kind == "error") { scenario = scenario with { Error = new() { StatusCode = status, Code = "configured", Message = "configured outage" } }; }
        if (kind == "mismatch") { scenario.Answers.Remove("binary"); }
        if (kind == "score-key") { scenario.Answers["score"] = scenario.Answers["score"] with { Probabilities = new() { ["invalid"] = 0.5 } }; }
        if (kind == "score-high") { scenario.Answers["score"] = scenario.Answers["score"] with { Score = 100 }; }
        if (kind == "wrong-input") { scenario = scenario with { ExpectedInput = "other" }; }
        if (kind != "unmatched") { builder.AddDecisionScenario(scenario); }
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        using var configured = await http.PostAsJsonAsync("/admin-api/configure", builder.Build());
        configured.EnsureSuccessStatusCode();
        using var response = await http.PostAsJsonAsync(DecisionEndpointTests.PathFor(_model), DecisionEndpointTests.Body(_model));
        await Assert.That((int)response.StatusCode).IsEqualTo(status);
        var summary = await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions");
        await Assert.That(summary!.TotalEvents).IsEqualTo(1);
    }

    [Test]
    public async Task DecisionEndpoint_PreservesFixturesAcrossRequestsAndResetAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddJevLatest()
            .AddDecisionScenario(DecisionEndpointTests.Scenario(_model)));
        using var http = host.GetTestClient();
        using var first = await http.PostAsJsonAsync(DecisionEndpointTests.PathFor(_model), DecisionEndpointTests.Body(_model));
        first.EnsureSuccessStatusCode();
        var original = await first.Content.ReadAsStringAsync();
        using var reset = await http.PostAsync("/admin-api/reset", null);
        reset.EnsureSuccessStatusCode();
        using var second = await http.PostAsJsonAsync(DecisionEndpointTests.PathFor(_model), DecisionEndpointTests.Body(_model));
        second.EnsureSuccessStatusCode();
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("answers").GetProperty("choice").GetProperty("probabilities").GetProperty("opaque-id").GetDouble()).IsEqualTo(0.95);
        await Assert.That(original).Contains("opaque-id");
    }

    [Test]
    [Arguments(-0.1)]
    [Arguments(1.1)]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    public async Task DecisionFixtureValidation_RejectsInvalidProbabilityAsync(double value)
    {
        var scenario = DecisionEndpointTests.Scenario(_model); scenario.Answers["binary"] = new() { Probability = value };
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var configuration = new LlmTestServerConfiguration { DecisionScenarios = [scenario] };
        var json = JsonSerializer.Serialize(configuration, _configurationJson);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("/admin-api/configure", content);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    [Arguments("score")]
    [Arguments("choice")]
    [Arguments("kind")]
    public async Task DecisionFixtureValidation_RejectsMissingValuesAsync(string kind)
    {
        var scenario = DecisionEndpointTests.Scenario(_model);
        scenario.Answers["binary"] = new() { Kind = kind switch { "score" => LlmTestServerDecisionKind.Score, "choice" => LlmTestServerDecisionKind.Choice, _ => (LlmTestServerDecisionKind)100 } };
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var configuration = new LlmTestServerConfiguration { DecisionScenarios = [scenario] };
        var json = JsonSerializer.Serialize(configuration, _configurationJson);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("/admin-api/configure", content);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task OpenAiDecisions_EmitNativeRefusalAnswersAsync()
    {
        var scenario = DecisionEndpointTests.Scenario("gpt-6-luna"); scenario.Answers["0"] = new() { Refused = true };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddModel("gpt-6-luna", LlmTestServerModelKind.Decision).AddDecisionScenario(scenario));
        using var client = host.GetTestClient();
        using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("gpt-6-luna"), DecisionEndpointTests.Body("gpt-6-luna"));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("answers")[0].GetProperty("type").GetString()).IsEqualTo("refusal");
        await Assert.That(payload.GetProperty("answers")[0].TryGetProperty("probability", out _)).IsFalse();
    }

}
