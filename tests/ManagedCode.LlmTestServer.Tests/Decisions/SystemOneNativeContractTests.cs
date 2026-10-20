using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.SystemOne;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Decisions;

public sealed class SystemOneNativeContractTests
{
    [Test]
    public async Task Kev_DoesNotInheritTypeSafeImageUploadBodyLimitAsync()
    {
        var scenario = new LlmTestServerDecisionScenario
        {
            Id = "kev-large-text",
            ModelId = SystemOneDecisionMapper.KevDefaultModel,
            Answers = new() { ["present"] = new() { Probability = 0.75 } },
        };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddKevLatest().AddDecisionScenario(scenario));
        using var client = host.GetTestClient();
        using var content = new StringContent(JsonSerializer.Serialize(new
        {
            model = SystemOneDecisionMapper.KevDefaultModel,
            state = new string('a', TypeSafeRequestValidation.MaxRequestBytes),
            questions = new { present = new { type = SystemOneDecisionTypes.Predicate, instructions = "Is content present?" } },
        }), System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(DecisionEndpointTests.PathFor(SystemOneDecisionMapper.KevDefaultModel), content);
        response.EnsureSuccessStatusCode();
        await Assert.That((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("answers").GetProperty("present").GetProperty("noul").GetDouble()).IsEqualTo(0.75);
    }

    [Test]
    [Arguments("-0", "0")]
    [Arguments("-0.0", "-0.0")]
    [Arguments("1e1", "10.0")]
    [Arguments("1.1", "1.1")]
    [Arguments("1e-5", "1e-05")]
    [Arguments("1e-4", "0.0001")]
    [Arguments("1e16", "1e+16")]
    public async Task Kev_RenderedNumbersMatchNativePythonContentAsync(string json, string expected)
    {
        var scenario = DecisionEndpointTests.Scenario("kev-latest");
        scenario.Answers.Clear();
        scenario.Answers["score"] = new()
        {
            Kind = LlmTestServerDecisionKind.Score,
            Score = 0,
            Confidence = 1,
            Probabilities = new() { ["0"] = 1, ["1"] = 0 },
        };
        var body = DecisionEndpointTests.Body("kev-latest");
        body["questions"] = new JsonObject
        {
            ["score"] = new JsonObject
            {
                ["type"] = "score",
                ["criteria"] = new JsonArray(JsonNode.Parse(json), "other"),
            },
        };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddKevLatest().AddDecisionScenario(scenario));
        using var http = host.GetTestClient();
        using var response = await http.PostAsJsonAsync("/systemone/v1/systemone", body);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("answers").GetProperty("score").GetProperty("legend").GetProperty("0").GetString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("null")]
    [Arguments("true")]
    [Arguments("42")]
    [Arguments("1.25")]
    public async Task Kev_AcceptsArbitraryJsonAndExplicitConfiguredDefaultAsync(string state)
    {
        var body = DecisionEndpointTests.Body("kev-latest"); body.Remove("model"); body["state"] = JsonNode.Parse(state);
        body["questions"]!["binary"]!["instructions"] = 7;
        body["questions"]!["binary"]!["criteria"] = new JsonObject { ["ignored"] = false, ["true"] = true, ["false"] = null };
        body["questions"]!["choice"]!["criteria"]!["opaque-id"] = false;
        body["questions"]!["score"]!["criteria"] = new JsonArray(null, true, new JsonObject { ["list"] = new JsonArray(1, "x") });
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddKevLatest().AddDecisionScenario(DecisionEndpointTests.Scenario("kev-latest")));
        using var client = host.GetTestClient(); client.DefaultRequestHeaders.Add("x-typesafe-request-id", "native-request-id");
        using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("kev-latest"), body); response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(); var legend = payload.GetProperty("answers").GetProperty("score").GetProperty("legend");
        await Assert.That(legend.GetProperty("0").GetString()).IsEqualTo("");
        await Assert.That(legend.GetProperty("1").GetString()).IsEqualTo("True");
        await Assert.That(legend.GetProperty("2").GetString()).IsEqualTo("list:\n  - 1\n  - x");
        await Assert.That(payload.GetProperty("latency_ms").GetDouble()).IsEqualTo(0);
        await Assert.That(response.Headers.GetValues("x-typesafe-request-id").Single()).IsEqualTo("native-request-id");
        await Assert.That(response.Headers.GetValues("server-timing").Single()).IsEqualTo("app;dur=0.0");
    }

    [Test]
    public async Task Kev_PreservesEmptyOpaqueNamesChoicesAndConfiguredTruncationMetadataAsync()
    {
        var scenario = DecisionEndpointTests.Scenario("kev-latest"); scenario.Answers.Clear();
        scenario = scenario with { Metadata = new() { LatencyMilliseconds = 12.34, Truncated = true, StateTokens = 80, StateTokensUsed = 40 } };
        scenario.Answers[""] = new() { Kind = LlmTestServerDecisionKind.Choice, Choice = "", Confidence = 0.8, Probabilities = new() { [""] = 0.9, [" "] = 0.1 } };
        var body = DecisionEndpointTests.Body("kev-latest"); body["questions"] = JsonNode.Parse("""{"":{"type":"choice","criteria":{"":null," ":42}}}""");
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddKevLatest().AddDecisionScenario(scenario));
        using var client = host.GetTestClient(); using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("kev-latest"), body); response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("answers").GetProperty("").GetProperty("choice").GetString()).IsEqualTo("");
        await Assert.That(payload.GetProperty("truncated").GetBoolean()).IsTrue();
        await Assert.That(payload.GetProperty("latency_ms").GetDouble()).IsEqualTo(12.3);
        await Assert.That(payload.GetProperty("usage").GetProperty("state_tokens").GetInt32()).IsEqualTo(80);
        await Assert.That(payload.GetProperty("usage").GetProperty("state_tokens_used").GetInt32()).IsEqualTo(40);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SystemOne_ReturnsNativeValidationEnvelopeAndRequestIdAsync(bool configuredKev)
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder => { if (configuredKev) { builder.AddKevLatest(); } });
        using var client = host.GetTestClient();
        var body = DecisionEndpointTests.Body(configuredKev ? "kev-latest" : "jev-latest"); body.Remove("state");
        using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("jev-latest"), body);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
        var detail = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail")[0];
        await Assert.That(detail.GetProperty("loc")[0].GetString()).IsEqualTo("body");
        await Assert.That(detail.GetProperty("msg").GetString()).IsNotNull();
        await Assert.That(detail.GetProperty("type").GetString()).IsEqualTo("value_error");
        await Assert.That(response.Headers.GetValues("x-typesafe-request-id").Single().Length).IsEqualTo(32);
    }

    [Test]
    public async Task Kev_AuthFailureCarriesBearerChallengeAndRequestIdAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddKevLatest().RequireBearerToken("test-token"));
        using var client = host.GetTestClient(); using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("kev-latest"), DecisionEndpointTests.Body("kev-latest"));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.WwwAuthenticate.Single().Scheme).IsEqualTo("Bearer");
        await Assert.That(response.Headers.Contains("x-typesafe-request-id")).IsTrue();
    }

    [Test]
    public async Task SystemOne_PredicateOutputContainsOnlyNativeKindFieldsAsync()
    {
        var scenario = DecisionEndpointTests.Scenario("jev-latest");
        scenario.Answers["binary"] = scenario.Answers["binary"] with { Confidence = 0.8, Choice = "unused", Score = 1 };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddJevLatest().AddDecisionScenario(scenario));
        using var client = host.GetTestClient(); using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("jev-latest"), DecisionEndpointTests.Body("jev-latest")); response.EnsureSuccessStatusCode();
        var binary = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("answers").GetProperty("binary");
        await Assert.That(binary.EnumerateObject().Select(item => item.Name)).IsEquivalentTo(new[] { "type", "noul" });
    }
}
