using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Determinism;

public sealed class DeterminismBoundaryIntegrationTests
{
    [Test]
    [Arguments("null")]
    [Arguments("empty")]
    [Arguments("blank")]
    [Arguments("oversized")]
    public async Task InvalidSeedConfiguration_PreservesActiveFixtureAndClockAsync(string defect)
    {
        var fixedUtc = new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);
        using var host = await LlmTestServerTestHost.StartAsync(config => config.WithDeterminism("original", fixedUtc)
            .AddChatScenario("preserved", scenario => scenario.Responds("preserved")));
        using var http = host.GetTestClient();
        var configuration = JsonNode.Parse("""{"determinism":{"seed":"valid","fixedUtc":"2001-02-03T04:05:06Z"}}""")!;
        configuration["determinism"]!["seed"] = defect switch
        { "null" => null, "empty" => "", "blank" => " ", _ => new string('x', 257) };
        using var invalid = await http.PostAsJsonAsync("/admin-api/configure", configuration);
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var actual = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
        { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "hello" } } });
        actual.EnsureSuccessStatusCode();
        var result = await actual.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(result.GetProperty("created").GetInt64()).IsEqualTo(fixedUtc.ToUnixTimeSeconds());
        await Assert.That(result.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()).IsEqualTo("preserved");
    }

    [Test]
    public async Task MaximumSeedAndOffsetClock_ProduceReproducibleSdkFixtureTimeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var clock = new DateTimeOffset(2001, 2, 3, 6, 5, 6, TimeSpan.FromHours(2));
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseDeterminism(new string('x', 256), clock)
            .UseChatScenario("clock", scenario => scenario.Responds("stable")));
        async Task<JsonElement> CallAsync()
        {
            using var response = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
            { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "hello" } } });
            response.EnsureSuccessStatusCode();
            await Assert.That(response.Headers.Date).IsEqualTo(clock.ToUniversalTime());
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        var first = await CallAsync();
        await control.ResetAsync();
        var repeated = await CallAsync();
        await Assert.That(first.GetProperty("id").GetString()).IsEqualTo(repeated.GetProperty("id").GetString());
        await Assert.That(repeated.GetProperty("created").GetInt64()).IsEqualTo(clock.ToUnixTimeSeconds());
    }
}
