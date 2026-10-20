using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Hosting;

public sealed class ConfigurationBoundaryIntegrationTests
{
    [Test]
    [Arguments("maxPromptCacheEntries", "-1")]
    [Arguments("maxVideoJobs", "-1")]
    [Arguments("maxVideoBytes", "-1")]
    [Arguments("maxStoredChatResponses", "-1")]
    [Arguments("maxStoredChatResponseBytes", "-1")]
    [Arguments("models", "null")]
    [Arguments("models", "[null]")]
    [Arguments("models", "[{\"id\":\"\",\"kind\":0}]")]
    [Arguments("models", "[{\"id\":\"gpt-4.1-mini\",\"kind\":0,\"reasoningTokens\":-1}]")]
    [Arguments("chatScenarios", "null")]
    [Arguments("chatScenarios", "[null]")]
    [Arguments("chatScenarios", "[{\"id\":\"invalid\",\"match\":null}]")]
    [Arguments("chatScenarios", "[{\"id\":\"invalid\",\"match\":{\"messages\":null}}]")]
    [Arguments("chatScenarios", "[{\"id\":\"invalid\",\"responses\":null}]")]
    [Arguments("chatScenarios", "[{\"id\":\"invalid\",\"responses\":[{\"delayMilliseconds\":-1}]}]")]
    [Arguments("chatScenarios", "[{\"id\":\"invalid\",\"responses\":[{\"streamChunks\":null}]}]")]
    [Arguments("chatScenarios", "[{\"id\":\"invalid\",\"responses\":[{\"toolCalls\":null}]}]")]
    [Arguments("datasets", "null")]
    [Arguments("datasets", "[null]")]
    [Arguments("datasets", "[{\"id\":\"invalid\",\"chatScenarios\":null}]")]
    [Arguments("decisionScenarios", "null")]
    [Arguments("defaultEmbeddingVector", "null")]
    [Arguments("defaultAudioBytes", "null")]
    [Arguments("defaultVideoBytes", "null")]
    [Arguments("faultSimulation", "null")]
    [Arguments("faultSimulation", "{\"contentFilterTerms\":null}")]
    [Arguments("chatScenarios", "[{\"id\":\"invalid\",\"responses\":[{\"toolCalls\":[{\"id\":\"call\",\"name\":\"weather\",\"argumentsJson\":\"{\"}]}]}]")]
    public async Task InvalidConfiguration_Returns400AndPreservesActiveScenarioAsync(string field, string invalidJson)
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config
            .AddChatScenario("preserved", scenario => scenario.Responds("still active")));
        using var http = host.GetTestClient();
        using var invalid = await http.PostAsJsonAsync("/admin-api/configure", new JsonObject { [field] = JsonNode.Parse(invalidJson) });
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var chat = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
        {
            model = "gpt-4.1-mini",
            messages = new[] { new { role = "user", content = "verify current configuration" } }
        });
        chat.EnsureSuccessStatusCode();
        await Assert.That(await chat.Content.ReadAsStringAsync()).Contains("still active");
    }
}
