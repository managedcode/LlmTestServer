using System.Net;
using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Cloudflare;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Decisions;

public sealed class CloudflareDecisionErrorTests
{
    [Test]
    [Arguments(400, "5004")]
    [Arguments(408, "3007")]
    [Arguments(408, "3008")]
    [Arguments(429, "3036")]
    [Arguments(429, "3040")]
    public async Task ConfiguredFault_PreservesNativeNumericErrorCodeAndHttpStatusAsync(int status, string code)
    {
        const string nativeDetail = "Explicit Cloudflare native failure.";
        var scenario = CloudflareDecisionTestFixtures.Scenario("clef") with
        { Error = new() { StatusCode = status, Code = code, Message = nativeDetail } };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddClef().AddDecisionScenario(scenario));
        using var client = host.GetTestClient();
        using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("clef"), CloudflareDecisionTestFixtures.Body("clef"));
        await Assert.That((int)response.StatusCode).IsEqualTo(status);
        var payload = await response.Content.ReadFromJsonAsync<CloudflareDecisionResponse>();
        await Assert.That(payload!.Success).IsFalse();
        await Assert.That(payload.Errors.Single().Code).IsEqualTo(int.Parse(code, System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(payload.Errors.Single().Message).IsEqualTo(nativeDetail);
        await Assert.That(payload.Messages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GenericRateLimit_UsesNativeCapacityCodeRatherThanHttpStatusAsCodeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddClef()
            .AddDecisionScenario(CloudflareDecisionTestFixtures.Scenario("clef")).SimulateRateLimitAfter(0));
        using var client = host.GetTestClient();
        using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("clef"), CloudflareDecisionTestFixtures.Body("clef"));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        var payload = await response.Content.ReadFromJsonAsync<CloudflareDecisionResponse>();
        await Assert.That(payload!.Errors.Single().Code).IsEqualTo(CloudflareDecisionErrors.Capacity);
    }

    [Test]
    [Arguments(400, CloudflareDecisionErrors.InvalidData)]
    [Arguments(401, CloudflareDecisionErrors.Authentication)]
    [Arguments(403, CloudflareDecisionErrors.AccountBlocked)]
    [Arguments(404, CloudflareDecisionErrors.InvalidModel)]
    [Arguments(408, CloudflareDecisionErrors.Timeout)]
    [Arguments(413, CloudflareDecisionErrors.RequestTooLarge)]
    [Arguments(429, CloudflareDecisionErrors.Capacity)]
    public async Task DefaultFaultCodes_MatchDocumentedNativeWorkersAiMappingAsync(int status, int code)
    {
        var scenario = CloudflareDecisionTestFixtures.Scenario("clef") with
        {
            Error = new() { StatusCode = status, Code = "configured_fault", Message = "fault" },
        };
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddClef().AddDecisionScenario(scenario));
        using var http = host.GetTestClient();
        using var response = await http.PostAsJsonAsync(DecisionEndpointTests.PathFor("clef"), CloudflareDecisionTestFixtures.Body("clef"));
        await Assert.That((int)response.StatusCode).IsEqualTo(status);
        var payload = await response.Content.ReadFromJsonAsync<CloudflareDecisionResponse>();
        await Assert.That(payload!.Errors.Single().Code).IsEqualTo(code);
    }
}
