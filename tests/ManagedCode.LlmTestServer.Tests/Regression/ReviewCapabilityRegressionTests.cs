using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Regression;

public sealed class ReviewCapabilityRegressionTests
{
    [Test]
    [Arguments("/openai/v1/chat/completions")]
    [Arguments("/azure-openai/openai/deployments/gpt-4.1-mini/chat/completions?api-version=2024-10-21")]
    [Arguments("/azure-openai/openai/v1/chat/completions")]
    [Arguments("/microsoft-foundry/chat/completions?api-version=2024-05-01-preview")]
    [Arguments("/microsoft-foundry/openai/v1/chat/completions")]
    [Arguments("/groq/openai/v1/chat/completions")]
    [Arguments("/mistral/v1/chat/completions")]
    [Arguments("/openrouter/api/v1/chat/completions")]
    [Arguments("/deepseek/v1/chat/completions")]
    [Arguments("/perplexity/v1/sonar")]
    [Arguments("/anthropic/v1/messages")]
    [Arguments("/ollama/api/chat")]
    [Arguments("/cohere/v2/chat")]
    [Arguments("/gemini/v1beta/models/gpt-4.1-mini:generateContent")]
    [Arguments("/bedrock/model/gpt-4.1-mini/converse")]
    [Arguments("/bedrock/model/gpt-4.1-mini/invoke")]
    [Arguments("/openai/v1/responses")]
    public async Task LegacyFunctions_ReturnExplicitErrorWithoutConsumingFixtureAsync(string path)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("queue", s => s.Responds("blue whale")));
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var body = JsonNode.Parse("""{"model":"gpt-4.1-mini","max_tokens":10,"messages":[{"role":"user","content":"whale"}],"input":"whale","contents":[{"parts":[{"text":"whale"}]}]}""")!.AsObject();
        body["functions"] = JsonNode.Parse("""[{"name":"lookup_whale"}]""");
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(path, content);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var summary = (await client.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!;
        await Assert.That(summary.TotalEvents).IsEqualTo(0);
    }

    [Test]
    [Arguments("{\"tools\":[]}", false)]
    [Arguments("{\"tool_choice\":\"none\"}", false)]
    [Arguments("{\"tool_choice\":\"required\"}", true)]
    [Arguments("{\"response_format\":{\"type\":\"text\"}}", false)]
    [Arguments("{\"response_format\":{\"type\":7}}", true)]
    [Arguments("{\"toolConfig\":{}}", true)]
    [Arguments("{\"generationConfig\":{\"responseJsonSchema\":{}}}", true)]
    public async Task CapabilityPolicy_DistinguishesTextDefaultsFromUnsupportedFeaturesAsync(string json, bool unsupported)
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddChatScenario("policy", scenario => scenario.Responds("accepted")));
        using var http = host.GetTestClient();
        var body = JsonNode.Parse(json)!.AsObject();
        body["inputText"] = "policy";
        using var response = await http.PostAsJsonAsync("/bedrock/model/gpt-4.1-mini/invoke", body);
        await Assert.That(response.StatusCode).IsEqualTo(unsupported ? HttpStatusCode.BadRequest : HttpStatusCode.OK);
        if (!unsupported)
        {
            await Assert.That(await response.Content.ReadAsStringAsync()).Contains("accepted");
        }
    }
}
