using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.SystemOne;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Decisions;

public sealed class TypeSafeVisionContractTests
{
    private const string _path = "/systemone/v1/systemone";

    [Test]
    [Arguments(256, 400, HttpStatusCode.BadRequest)]
    [Arguments(279, 400, HttpStatusCode.BadRequest)]
    [Arguments(280, 279, HttpStatusCode.BadRequest)]
    [Arguments(280, 280, HttpStatusCode.OK)]
    [Arguments(1568, 280, HttpStatusCode.OK)]
    [Arguments(1569, 280, HttpStatusCode.RequestEntityTooLarge)]
    public async Task ImageSidesMatchDocumentedInclusiveLimitsAsync(int width, int height, HttpStatusCode status)
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder
            .AddDecisionModel(TypeSafeRequestValidation.Jev114Model, LlmTestServerDecisionProvider.TypeSafe)
            .AddDecisionScenario(Jev114EndpointTests.Scenario(true)));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add(TypeSafeRequestValidation.ImagesHeader, TypeSafeRequestValidation.Enabled);
        var body = Jev114EndpointTests.Body(true);
        body["assets"]!["photo-reference"]!["data_base64"] = TypeSafeImageBoundaryFixtures.Png(width, height);

        using var response = await http.PostAsJsonAsync(_path, body);

        await Assert.That(response.StatusCode).IsEqualTo(status);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BoxAllowsNullableConfidenceAndOptionalInstructionsFromOpenApiAsync(bool omitInstructions)
    {
        var scenario = Jev114EndpointTests.Scenario(true);
        scenario.Answers["target"] = scenario.Answers["target"] with { Confidence = null };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder
            .AddDecisionModel(TypeSafeRequestValidation.Jev114Model, LlmTestServerDecisionProvider.TypeSafe).AddDecisionScenario(scenario));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add(TypeSafeRequestValidation.ImagesHeader, TypeSafeRequestValidation.Enabled);
        var body = Jev114EndpointTests.Body(true);
        if (omitInstructions) { body["questions"]!["target"]!.AsObject().Remove("instructions"); }
        else { body["questions"]!["target"]!["instructions"] = null; }

        using var response = await http.PostAsJsonAsync(_path, body);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var answer = result.GetProperty("answers").GetProperty("target");
        await Assert.That(answer.GetProperty("confidence").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(answer.GetProperty("box").GetProperty("left").GetDouble()).IsEqualTo(0.44);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PhysicalBodyLimitIncludesWhitespaceWithOrWithoutContentLengthAsync(bool unknownLength)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var body = Jev114EndpointTests.Body(true).ToJsonString();
        var padded = Encoding.UTF8.GetBytes(new string(' ', TypeSafeRequestValidation.MaxRequestBytes) + body);
        using HttpContent content = unknownLength ? new StreamContent(new UnknownLengthStream(padded)) : new ByteArrayContent(padded);
        content.Headers.ContentType = new("application/json");
        using var response = await http.PostAsync(_path, content);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
    }

    [Test]
    public async Task NativeSerializationDoesNotAddConfidenceToNoulAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddKevLatest()
            .AddDecisionScenario(DecisionEndpointTests.Scenario("kev-latest")));
        using var http = host.GetTestClient();
        using var response = await http.PostAsJsonAsync(_path, DecisionEndpointTests.Body("kev-latest"));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("answers").GetProperty("binary").TryGetProperty("confidence", out _)).IsFalse();
    }

    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
