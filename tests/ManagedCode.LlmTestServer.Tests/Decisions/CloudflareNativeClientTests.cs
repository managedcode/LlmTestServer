using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Cloudflare;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Decisions;

public sealed class CloudflareNativeClientTests
{
    private const string _credential = "native-decision-client-test-token";
    private const string _account = "native-client-account";

    [Test]
    [Arguments("clef", "image/png", false)]
    [Arguments("clef", "image/jpeg", true)]
    [Arguments("clef-flash", "image/webp", false)]
    [Arguments("clef-flash", "image/png", true)]
    public async Task TypedClientPreservesEmbeddedImagesProbabilitiesUsageAndCallerHttpLifetimeAsync(string model, string contentType, bool embedded)
    {
        var body = CloudflareDecisionTestFixtures.Body(model);
        body["images"] = new System.Text.Json.Nodes.JsonArray(CloudflareDecisionTestFixtures.Image(contentType, embedded));
        var request = body.Deserialize<CloudflareDecisionRequest>()!;
        var scenario = CloudflareDecisionTestFixtures.Scenario(model) with { ExpectedInput = CloudflareDecisionMapper.ToRequest(request).Input };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.RequireBearerToken(_credential)
            .AddDecisionModel(model, LlmTestServerDecisionProvider.Cloudflare).AddDecisionScenario(scenario));
        using var http = host.GetTestClient();
        var client = new LlmTestServerClient(http, _credential).CreateCloudflareDecisionClient(_account);

        var response = await client.EvaluateAsync(request);
        var second = await client.EvaluateAsync(request);

        await Assert.That(response.Success && second.Success).IsTrue();
        await Assert.That(response.Result!.Answers["binary"].Probability).IsEqualTo(0.75);
        await Assert.That(response.Result.Usage.InputTokens).IsEqualTo(100);
        await Assert.That(response.Result.Model).IsEqualTo(model);
    }

    [Test]
    [Arguments(401, CloudflareDecisionErrors.Authentication)]
    [Arguments(429, CloudflareDecisionErrors.Capacity)]
    [Arguments(413, CloudflareDecisionErrors.RequestTooLarge)]
    public async Task TypedClientPreservesNativeFailureEnvelopesAsync(int status, int code)
    {
        const string model = "clef";
        var scenario = CloudflareDecisionTestFixtures.Scenario(model) with
        { Error = new LlmTestServerScenarioError { StatusCode = status, Code = code.ToString(System.Globalization.CultureInfo.InvariantCulture), Message = "native failure" } };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddDecisionModel(model, LlmTestServerDecisionProvider.Cloudflare).AddDecisionScenario(scenario));
        using var http = host.GetTestClient();
        var client = new LlmTestServerClient(http).CreateCloudflareDecisionClient(_account);

        var response = await client.EvaluateAsync(CloudflareDecisionTestFixtures.Body(model).Deserialize<CloudflareDecisionRequest>()!);

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Errors.Single().Code).IsEqualTo(code);
        await Assert.That(response.Errors.Single().Message).IsEqualTo("native failure");
    }

    [Test]
    public async Task TypedClientRejectsRemoteImageUrlsUsingNativeErrorEnvelopeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var request = CloudflareDecisionTestFixtures.Body("clef").Deserialize<CloudflareDecisionRequest>()!
            with
        { Images = [new() { DataUrl = "https://example.org/clipping.png" }] };
        var response = await new LlmTestServerClient(http).CreateCloudflareDecisionClient(_account).EvaluateAsync(request);
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Errors.Single().Code).IsEqualTo(CloudflareDecisionErrors.InvalidData);
    }
}
