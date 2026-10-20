using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Regression;

public sealed class FollowUpEndpointTests
{
    [Test]
    [Arguments("/openai/v1/chat/completions", 0, false)]
    [Arguments("/openai/v1/chat/completions", 0, true)]
    [Arguments("/openai/v1/responses", 1, false)]
    [Arguments("/openai/v1/responses", 1, true)]
    [Arguments("/anthropic/v1/messages", 2, false)]
    [Arguments("/anthropic/v1/messages", 2, true)]
    public async Task ParallelTools_RespectRequestLimitAndPreserveQueueAsync(string path, int format, bool stream)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("parallel", s => s.CallsTools(
            new LlmTestServerToolCall { Id = "first", Name = "weather", ArgumentsJson = "{\"city\":\"Paris\"}" },
            new LlmTestServerToolCall { Id = "second", Name = "weather", ArgumentsJson = "{\"city\":\"Paris\"}" })));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var body = ToolFixtureEndpointTests.Request(format, tools: true);
        body["stream"] = stream;
        if (format == 2)
        {
            body["tool_choice"] = new JsonObject { ["type"] = "auto", ["disable_parallel_tool_use"] = true };
        }
        else
        {
            body["parallel_tool_calls"] = false;
        }

        using var rejected = await PostAsync(http, path, body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(await rejected.Content.ReadAsStringAsync()).Contains("parallel tool calls");
        var summary = (await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!;
        await Assert.That(summary.ErrorsReturned).IsEqualTo(1);
        body.Remove(format == 2 ? "tool_choice" : "parallel_tool_calls");
        using var accepted = await PostAsync(http, path, body);
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = await accepted.Content.ReadAsStringAsync();
        await Assert.That(result).Contains("first");
        await Assert.That(result).Contains("second");
        using var exhausted = await PostAsync(http, path, body);
        await Assert.That(exhausted.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ChatModel_IsRequiredExceptForAzureDeploymentRoutesAsync(bool azureDeployment)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("model", s => s.Responds("fixture")));
        using var http = host.GetTestClient();
        var body = JsonNode.Parse("""{"messages":[{"role":"user","content":"hello"}]}""")!;
        const string chat = "/openai/v1/chat/completions";
        var path = azureDeployment ? "/azure-openai/openai/deployments/gpt-4.1-mini/chat/completions?api-version=2024-10-21" : chat;
        using var response = await PostAsync(http, path, body);
        await Assert.That(response.StatusCode).IsEqualTo(azureDeployment ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        if (!azureDeployment)
        {
            await Assert.That((await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!.TotalEvents).IsEqualTo(0);
            body["model"] = "gpt-4.1-mini";
            using var valid = await PostAsync(http, path, body);
            await Assert.That(valid.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await valid.Content.ReadAsStringAsync()).Contains("fixture");
        }
    }

    [Test]
    [Arguments("nullable", "{\"city\":null}", "{\"type\":\"OBJECT\",\"properties\":{\"city\":{\"type\":\"STRING\",\"nullable\":true}},\"required\":[\"city\"]}")]
    [Arguments("string-limits", "[\"Paris\"]", "{\"type\":\"ARRAY\",\"items\":{\"type\":\"STRING\",\"minLength\":\"5\",\"maxLength\":\"5\"},\"minItems\":\"1\",\"maxItems\":\"1\"}")]
    [Arguments("nested-anyof", "{\"city\":\"Paris\"}", "{\"type\":\"OBJECT\",\"minProperties\":\"1\",\"maxProperties\":\"1\",\"properties\":{\"city\":{\"anyOf\":[{\"type\":\"STRING\",\"enum\":[\"Paris\"]},{\"type\":\"INTEGER\",\"minimum\":1,\"maximum\":2}]}}}")]
    public async Task GeminiSchema_ValidatesNullableNestedAlternativesAndStringLimitsAsync(string caseId, string fixture, string schema)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario(caseId, s => s.RespondsJson(fixture)));
        using var http = host.GetTestClient();
        var body = JsonNode.Parse("""{"contents":[{"role":"user","parts":[{"text":"answer"}]}],"generationConfig":{"responseMimeType":"application/json"}}""")!;
        body["generationConfig"]!["responseSchema"] = JsonNode.Parse("""{"type":"BOOLEAN"}""");
        const string path = "/gemini/v1beta/models/gpt-4.1-mini:generateContent";
        using var mismatch = await PostAsync(http, path, body);
        await Assert.That(mismatch.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        body["generationConfig"]!["responseSchema"] = JsonNode.Parse(schema);
        using var valid = await PostAsync(http, path, body);
        await Assert.That(valid.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var response = await valid.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(response.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString()).IsEqualTo(fixture);
    }

    [Test]
    [Arguments("/openai/v1/videos")]
    [Arguments("/azure-openai/openai/v1/video/generations/jobs?api-version=preview")]
    public async Task VideoCapacity_ReturnsConflictThroughProviderEndpointAsync(string path)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.WithVideoCapacity(0, 0));
        using var http = host.GetTestClient();
        using var response = await http.PostAsJsonAsync(path, new { model = "sora-2", prompt = "whale" });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("llm_test_server_video_capacity_exceeded");
        using var list = await http.GetAsync(path);
        await Assert.That((await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetArrayLength()).IsEqualTo(0);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, string path, JsonNode body)
    {
        return http.PostAsync(path, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
    }
}
