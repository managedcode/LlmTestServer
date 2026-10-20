using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Cloudflare;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Decisions;

public sealed class CloudflareDecisionImageTests
{
    [Test]
    [Arguments("remote")]
    [Arguments("null")]
    [Arguments("nonarray")]
    [Arguments("missing-content-type")]
    [Arguments("missing-base64")]
    [Arguments("invalid-base64")]
    [Arguments("invalid-bytes")]
    [Arguments("mismatched-format")]
    [Arguments("unsupported-format")]
    [Arguments("count")]
    public async Task InvalidImages_ReturnNativeNonSuccessRatherThanSilentlyDroppingImagesAsync(string kind)
    {
        var body = CloudflareDecisionTestFixtures.Body("clef");
        body["images"] = InvalidImages(kind);
        using var host = await LlmTestServerTestHost.StartAsync(); using var client = host.GetTestClient();
        using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("clef"), body);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<CloudflareDecisionResponse>();
        await Assert.That(payload!.Success).IsFalse();
        await Assert.That(payload.Errors.Single().Code).IsEqualTo(CloudflareDecisionErrors.InvalidData);
    }

    [Test]
    [Arguments("per-image")]
    [Arguments("total")]
    [Arguments("pixels")]
    [Arguments("body")]
    public async Task NativeImageBudgets_ReturnHttp413WithRequestTooLargeCodeAsync(string kind)
    {
        var body = CloudflareDecisionTestFixtures.Body("clef");
        if (kind == "body") { body["state"] = new string('x', CloudflareDecisionImageValidation.MaxRequestBytes); }
        else { body["images"] = OversizedImages(kind); }
        using var host = await LlmTestServerTestHost.StartAsync(); using var client = host.GetTestClient();
        using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("clef"), body);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
        var payload = await response.Content.ReadFromJsonAsync<CloudflareDecisionResponse>();
        await Assert.That(payload!.Success).IsFalse();
        await Assert.That(payload.Errors.Single().Code).IsEqualTo(CloudflareDecisionErrors.RequestTooLarge);
    }

    [Test]
    public async Task NoImages_PreservesOriginalStructuredStateScenarioInputAsync()
    {
        var body = CloudflareDecisionTestFixtures.Body("clef");
        var scenario = CloudflareDecisionTestFixtures.Scenario("clef") with
        {
            ExpectedInput = JsonSerializer.Serialize(body["state"]),
        };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddClef().AddDecisionScenario(scenario));
        using var http = host.GetTestClient();
        using var response = await http.PostAsJsonAsync(DecisionEndpointTests.PathFor("clef"), body);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CloudflareDecisionResponse>();
        await Assert.That(payload!.Result!.Answers["binary"].Probability).IsEqualTo(0.75);
    }

    private static JsonNode? InvalidImages(string kind)
    {
        return kind switch
        {
            "remote" => new JsonArray("https://example.com/photo.png"),
            "null" => null,
            "nonarray" => new JsonObject(),
            "missing-content-type" => new JsonArray(new JsonObject { ["base64"] = CloudflareDecisionTestFixtures._png }),
            "missing-base64" => new JsonArray(new JsonObject { ["content_type"] = "image/png" }),
            "invalid-base64" => new JsonArray(new JsonObject { ["content_type"] = "image/png", ["base64"] = "%%%" }),
            "invalid-bytes" => new JsonArray(new JsonObject { ["content_type"] = "image/png", ["base64"] = "bm90IGFuIGltYWdl" }),
            "mismatched-format" => new JsonArray(new JsonObject { ["content_type"] = "image/jpeg", ["base64"] = CloudflareDecisionTestFixtures._png }),
            "unsupported-format" => new JsonArray(new JsonObject { ["content_type"] = "image/gif", ["base64"] = CloudflareDecisionTestFixtures._png }),
            _ => new JsonArray(Enumerable.Range(0, CloudflareDecisionImageValidation.MaxImages + 1)
                .Select(_ => CloudflareDecisionTestFixtures.Image("image/png", false)).ToArray()),
        };
    }

    private static JsonArray OversizedImages(string kind)
    {
        if (kind == "pixels") { return new JsonArray("data:image/png;base64," + CloudflareDecisionLargeImageFixture._png); }
        var count = kind == "total" ? 3 : 1;
        var length = kind == "total" ? CloudflareDecisionImageValidation.MaxTotalImageBytes / count + 1
            : CloudflareDecisionImageValidation.MaxImageBytes + 1;
        var bytes = new byte[length]; Convert.FromBase64String(CloudflareDecisionTestFixtures._png).CopyTo(bytes, 0);
        return new JsonArray(Enumerable.Range(0, count).Select(_ => (JsonNode)new JsonObject
        { ["content_type"] = "image/png", ["base64"] = Convert.ToBase64String(bytes) }).ToArray());
    }
}
