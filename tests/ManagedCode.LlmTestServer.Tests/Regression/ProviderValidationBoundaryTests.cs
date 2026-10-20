using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Regression;

public sealed class ProviderValidationBoundaryTests
{
    [Test]
    [Arguments("/openai/v1/chat/completions", 0)]
    [Arguments("/openai/v1/responses", 1)]
    [Arguments("/anthropic/v1/messages", 2)]
    [Arguments("/gemini/v1beta/models/gpt-4.1-mini:generateContent", 3)]
    [Arguments("/ollama/api/chat", 4)]
    [Arguments("/cohere/v2/chat", 5)]
    [Arguments("/bedrock/model/gpt-4.1-mini/converse", 6)]
    public async Task ProviderValidator_RejectsMalformedToolsBeforeMappingOrRuntimeAsync(string path, int format)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("tools", s => s.CallsTool("weather", "{\"city\":\"Paris\"}")));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var valid = ToolFixtureEndpointTests.Request(format, tools: true);
        foreach (var malformed in new JsonNode?[] { null, new JsonObject(), new JsonArray((JsonNode?)null) })
        {
            var invalid = valid.DeepClone();
            if (format == 6) { invalid["toolConfig"]!["tools"] = malformed; }
            else { invalid["tools"] = malformed; }
            using var response = await PostAsync(http, path, invalid);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        await Assert.That((await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!.TotalEvents).IsEqualTo(0);
        using var accepted = await PostAsync(http, path, valid);
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    [Arguments("/openai/v1/chat/completions", 0)]
    [Arguments("/openai/v1/responses", 1)]
    [Arguments("/anthropic/v1/messages", 2)]
    public async Task DisabledParallelTools_AllowsZeroOrOneCallAsync(string path, int format)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("single", s => s.Responds("plain").CallsTool("weather", "{\"city\":\"Paris\"}")));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var body = ToolFixtureEndpointTests.Request(format, tools: true);
        if (format == 2) { body["tool_choice"] = new JsonObject { ["type"] = "auto", ["disable_parallel_tool_use"] = true }; }
        else { body["parallel_tool_calls"] = false; }
        using var plain = await PostAsync(http, path, body);
        await Assert.That(plain.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await plain.Content.ReadAsStringAsync()).Contains("plain");
        using var single = await PostAsync(http, path, body);
        await Assert.That(single.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await single.Content.ReadAsStringAsync()).Contains("weather");
        await Assert.That((await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!.ErrorsReturned).IsEqualTo(0);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, string path, JsonNode body)
    {
        return http.PostAsync(path, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
    }
}
