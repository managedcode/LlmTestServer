using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.SystemOne;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Decisions;

public sealed class Jev114EndpointTests
{
    private const string _model = TypeSafeRequestValidation.Jev114Model;
    private const string _path = "/systemone/v1/systemone";
    // Valid 280x280 grayscale PNG, including pixel data and CRCs.
    private const string _png = "iVBORw0KGgoAAAANSUhEUgAAARgAAAEYAQAAAACv9dQhAAAAN0lEQVR42u3KMQEAAAwCIPuX1ggLMLhJb3Ecx3Ecx3Ecx3Ecx3Ecx3Ecx3Ecx3Ecx3Ecx3GcZ2f9LSPzwqoynQAAAABJRU5ErkJggg==";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Jev114_JsonImagesAndBoxPreserveNativeAnswersAsync(bool boxOnly)
    {
        var scenario = Scenario(boxOnly);
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddDecisionModel(_model, LlmTestServerDecisionProvider.TypeSafe).AddDecisionScenario(scenario));
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(TypeSafeRequestValidation.ImagesHeader, TypeSafeRequestValidation.Enabled);
        client.DefaultRequestHeaders.Add(TypeSafeRequestValidation.LongContextHeader, TypeSafeRequestValidation.Enabled);
        using var response = await client.PostAsJsonAsync(_path, Body(boxOnly));
        Console.WriteLine(await response.Content.ReadAsStringAsync());
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var box = payload.GetProperty("answers").GetProperty("target");
        await Assert.That(box.GetProperty("type").GetString()).IsEqualTo(SystemOneDecisionTypes.Box);
        await Assert.That(box.GetProperty("box").GetProperty("left").GetDouble()).IsEqualTo(0.44);
        await Assert.That(box.GetProperty("confidence").GetDouble()).IsEqualTo(0.92);
        await Assert.That(box.TryGetProperty("probabilities", out _)).IsFalse();
        await Assert.That(payload.GetProperty("usage").GetProperty("input_tokens").GetInt32()).IsEqualTo(290);
        await Assert.That(response.Headers.Contains("x-typesafe-request-id")).IsTrue();
        if (!boxOnly)
        {
            var legend = payload.GetProperty("answers").GetProperty("rating").GetProperty("legend");
            await Assert.That(legend.GetProperty("0").GetProperty("example").GetString()).IsEqualTo("photo-reference");
        }
        var trace = (await client.GetFromJsonAsync<List<LlmTestServerProviderHttpTrace>>("/admin-api/requests"))!.Single();
        await Assert.That(trace.ModelId).IsEqualTo(_model);
    }

    [Test]
    [Arguments("missing-header", 400)]
    [Arguments("no-image", 400)]
    [Arguments("old-model", 400)]
    [Arguments("unreferenced", 400)]
    [Arguments("key-only-reference", 400)]
    [Arguments("invalid-base64", 400)]
    [Arguments("whitespace-base64", 400)]
    [Arguments("invalid-format", 400)]
    [Arguments("mismatched-format", 400)]
    [Arguments("empty-image", 400)]
    [Arguments("invalid-type", 400)]
    [Arguments("part", 400)]
    [Arguments("animated", 400)]
    [Arguments("truncated", 400)]
    [Arguments("reference-size", 413)]
    [Arguments("box-criteria", 422)]
    public async Task Jev114_RejectsInvalidImageAndBoxRequestsAsync(string defect, int status)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var client = host.GetTestClient();
        if (defect != "missing-header") { client.DefaultRequestHeaders.Add(TypeSafeRequestValidation.ImagesHeader, TypeSafeRequestValidation.Enabled); }
        var body = Body(true);
        var asset = body["assets"]!["photo-reference"]!.AsObject();
        switch (defect)
        {
            case "no-image": body.Remove("assets"); break;
            case "old-model": body["model"] = "jev-1.13.0"; break;
            case "unreferenced": body["state"] = "plain text"; break;
            case "key-only-reference": body["state"] = new JsonObject { ["photo-reference"] = "plain text" }; break;
            case "invalid-base64": asset["data_base64"] = "!invalid"; break;
            case "whitespace-base64": asset["data_base64"] = _png + "\n"; break;
            case "invalid-format": asset["format"] = "webp"; break;
            case "mismatched-format": asset["format"] = "jpg"; break;
            case "empty-image": asset["data_base64"] = ""; break;
            case "invalid-type": asset["type"] = "audio"; break;
            case "part": asset["part"] = "photo"; break;
            case "animated":
                var bytes = Convert.FromBase64String(_png).ToList();
                bytes.InsertRange(33, new byte[] { 0, 0, 0, 0, (byte)'a', (byte)'c', (byte)'T', (byte)'L', 0, 0, 0, 0 });
                asset["data_base64"] = Convert.ToBase64String([.. bytes]); break;
            case "truncated": asset["data_base64"] = Convert.ToBase64String(Convert.FromBase64String(_png)[..^12]); break;
            case "reference-size":
                var reference = new string('a', 1025);
                body["assets"] = new JsonObject { [reference] = asset.DeepClone() };
                body["state"] = reference; break;
            case "box-criteria": body["questions"]!["target"]!["criteria"] = new JsonObject(); break;
        }
        using var response = await client.PostAsJsonAsync(_path, body);
        await Assert.That((int)response.StatusCode).IsEqualTo(status);
        await Assert.That((await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("detail", out _)).IsTrue();
    }

    [Test]
    public async Task Jev114_AliasReportsResolvedModelAsync()
    {
        var scenario = Scenario(true) with { ModelId = TypeSafeRequestValidation.PreviewModel, ResolvedModelId = _model };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddDecisionModel(TypeSafeRequestValidation.PreviewModel, LlmTestServerDecisionProvider.TypeSafe).AddDecisionScenario(scenario));
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(TypeSafeRequestValidation.ImagesHeader, TypeSafeRequestValidation.Enabled);
        var body = Body(true); body["model"] = TypeSafeRequestValidation.PreviewModel;
        using var response = await client.PostAsJsonAsync(_path, body);
        Console.WriteLine(await response.Content.ReadAsStringAsync());
        response.EnsureSuccessStatusCode();
        await Assert.That((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("model").GetString()).IsEqualTo(_model);
    }

    internal static JsonObject Body(bool boxOnly)
    {
        var body = JsonNode.Parse("""{"state":{"photo":"photo-reference"},"questions":{"target":{"type":"box","instructions":"the target in photo"}}}""")!.AsObject();
        body["model"] = _model;
        body["assets"] = new JsonObject { ["photo-reference"] = new JsonObject { ["type"] = "image", ["format"] = "png", ["data_base64"] = _png } };
        if (!boxOnly)
        {
            body["questions"]!["present"] = JsonNode.Parse("""{"type":"noul","instructions":{"question":"Is the target present?","example":"photo-reference"}}""");
            body["questions"]!["rating"] = JsonNode.Parse("""{"type":"score","instructions":"Rate match","criteria":[{"example":"photo-reference"},["clear","match"]]}""");
        }
        return body;
    }

    [Test]
    public async Task Jev114_ExplicitMalformedBoxFixtureSupportsConsumerProtocolTestsAsync()
    {
        var scenario = Scenario(true) with { AllowMalformedResponse = true };
        scenario.Answers["target"] = scenario.Answers["target"] with { Box = new() { Top = 0.8, Bottom = 0.2, Left = 0.1, Right = 0.9 } };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddDecisionModel(_model, LlmTestServerDecisionProvider.TypeSafe).AddDecisionScenario(scenario));
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(TypeSafeRequestValidation.ImagesHeader, TypeSafeRequestValidation.Enabled);
        using var response = await client.PostAsJsonAsync(_path, Body(true));
        response.EnsureSuccessStatusCode();
        var box = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("answers").GetProperty("target").GetProperty("box");
        await Assert.That(box.GetProperty("top").GetDouble()).IsEqualTo(0.8);
    }

    internal static LlmTestServerDecisionScenario Scenario(bool boxOnly)
    {
        var scenario = new LlmTestServerDecisionScenario
        {
            Id = "jev114-images",
            ModelId = _model,
            Usage = new() { InputTokens = 290, OutputTokens = 21, TotalTokens = 311 },
            Answers = new() { ["target"] = new() { Kind = LlmTestServerDecisionKind.Box, Box = new() { Top = 0.25, Bottom = 0.82, Left = 0.44, Right = 0.84 }, Confidence = 0.92 } },
        };
        if (!boxOnly)
        {
            scenario.Answers["present"] = new() { Probability = 0.95 };
            scenario.Answers["rating"] = new() { Kind = LlmTestServerDecisionKind.Score, Score = 0.75, Confidence = 0.6, Probabilities = new() { ["0"] = 0.25, ["1"] = 0.75 } };
        }
        return scenario;
    }
}
