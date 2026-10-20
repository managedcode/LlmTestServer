using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Regression;

public sealed class ToolFixtureValidationTests
{
    [Test]
    [Arguments("/openai/v1/chat/completions", "\"tools\":null")]
    [Arguments("/openai/v1/chat/completions", "\"tools\":[null]")]
    [Arguments("/openai/v1/chat/completions", "\"tools\":[{\"type\":\"function\",\"function\":null}]")]
    [Arguments("/openai/v1/chat/completions", "\"tools\":[{\"type\":\"web_search\"}]")]
    [Arguments("/openai/v1/chat/completions", "\"tool_choice\":\"maybe\"")]
    [Arguments("/openai/v1/chat/completions", "\"tool_choice\":{\"type\":\"function\",\"function\":{}}")]
    [Arguments("/openai/v1/chat/completions", "\"response_format\":{\"type\":\"json_schema\"}")]
    [Arguments("/openai/v1/chat/completions", "\"response_format\":{\"type\":\"xml\"}")]
    [Arguments("/openai/v1/responses", "\"text\":{\"format\":{\"type\":\"json_schema\"}}")]
    [Arguments("/anthropic/v1/messages", "\"tools\":[{\"name\":\"weather\"}]")]
    [Arguments("/anthropic/v1/messages", "\"tool_choice\":{\"type\":\"tool\"}")]
    [Arguments("/gemini/v1beta/models/gpt-4.1-mini:generateContent", "\"tools\":[{}]")]
    [Arguments("/gemini/v1beta/models/gpt-4.1-mini:generateContent", "\"toolConfig\":{\"functionCallingConfig\":{\"mode\":\"WRONG\"}}")]
    [Arguments("/ollama/api/chat", "\"format\":\"xml\"")]
    [Arguments("/cohere/v2/chat", "\"tool_choice\":\"MAYBE\"")]
    [Arguments("/bedrock/model/gpt-4.1-mini/converse", "\"toolConfig\":{\"tools\":[null]}")]
    [Arguments("/bedrock/model/gpt-4.1-mini/converse", "\"outputConfig\":{\"textFormat\":{\"type\":\"json_schema\"}}")]
    [Arguments("/openai/v1/chat/completions", "\"messages\":[{\"tool_calls\":[null]}]")]
    [Arguments("/openai/v1/chat/completions", "\"messages\":[{\"tool_calls\":[{\"id\":\"call\",\"function\":{\"name\":\"weather\",\"arguments\":null}}]}]")]
    [Arguments("/openai/v1/responses", "\"input\":[{\"type\":\"function_call\",\"name\":\"weather\"}]")]
    [Arguments("/anthropic/v1/messages", "\"messages\":[{\"content\":[{\"type\":\"tool_use\"}]}]")]
    [Arguments("/gemini/v1beta/models/gpt-4.1-mini:generateContent", "\"contents\":[{\"parts\":[{\"functionCall\":{}}]}]")]
    [Arguments("/bedrock/model/gpt-4.1-mini/converse", "\"messages\":[{\"content\":[{\"toolResult\":{\"toolUseId\":\"call\",\"content\":[null]}}]}]")]
    public async Task InvalidToolOptions_Return400BeforeRuntimeAsync(string path, string fields)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        using var response = await http.PostAsync(path, new StringContent("{" + fields + "}", Encoding.UTF8, "application/json"));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    [Arguments("{\"$ref\":\"https://example.com/schema\"}", HttpStatusCode.Conflict)]
    [Arguments("{\"type\":42}", HttpStatusCode.Conflict)]
    [Arguments("\"not json\"", HttpStatusCode.BadRequest)]
    public async Task InvalidSchema_RejectsWithoutConsumingResponseAsync(string schema, HttpStatusCode status)
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddChatScenario("json", scenario => scenario.RespondsJson("{}")));
        using var http = host.GetTestClient();
        var body = Request();
        body["response_format"] = JsonNode.Parse("{\"type\":\"json_schema\",\"json_schema\":{\"name\":\"fixture\",\"schema\":" + schema + "}}");
        using var rejected = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(status);
        body["response_format"]!["json_schema"]!["schema"] = new JsonObject { ["type"] = "object" };
        using var accepted = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        accepted.EnsureSuccessStatusCode();
        await Assert.That(await accepted.Content.ReadAsStringAsync()).Contains("{}");
    }

    [Test]
    public async Task RequiredAndNoneChoices_DoNotConsumeMismatchedToolFixturesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddChatScenario("tools", scenario => scenario.CallsTool("weather", "{}").Responds("done")));
        using var http = host.GetTestClient();
        var body = Request();
        body["tools"] = Tools(new JsonObject { ["type"] = "object" });
        body["tool_choice"] = "none";
        using var none = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        await Assert.That(none.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        body["tool_choice"] = new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = "other" } };
        using var named = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        await Assert.That(named.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        body.Remove("tools"); body.Remove("tool_choice");
        using var missing = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        body["tools"] = Tools(new JsonObject { ["type"] = "object" }); body["tool_choice"] = "required";
        using var tool = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        tool.EnsureSuccessStatusCode();
        var payload = await tool.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("choices")[0].GetProperty("message").GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString()).IsEqualTo("weather");
        using var required = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        await Assert.That(required.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        body["tool_choice"] = "none";
        using var final = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        final.EnsureSuccessStatusCode();
        await Assert.That(await final.Content.ReadAsStringAsync()).Contains("done");
    }

    [Test]
    public async Task LocalSchemaReferences_AreEvaluatedAndInvalidArgumentsDoNotConsumeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddChatScenario("tools", scenario => scenario.CallsTool("weather", "{\"city\":\"London\"}")));
        using var http = host.GetTestClient();
        var body = Request();
        body["tools"] = Tools(JsonNode.Parse("""{"$defs":{"city":{"const":"Paris"}},"type":"object","properties":{"city":{"$ref":"#/$defs/city"}},"required":["city"]}""")!);
        using var rejected = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        body["tools"]![0]!["function"]!["parameters"]!["$defs"]!["city"]!["const"] = "London";
        using var accepted = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        accepted.EnsureSuccessStatusCode();
        await Assert.That(await accepted.Content.ReadAsStringAsync()).Contains("London");
    }

    [Test]
    public async Task StructuredStream_ValidatesActualConcatenatedContentAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddChatScenario("json", scenario => scenario.Responds("{}", "not json")));
        using var http = host.GetTestClient();
        var body = Request(); body["stream"] = true; body["response_format"] = new JsonObject { ["type"] = "json_object" };
        using var rejected = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        body.Remove("response_format"); body["stream"] = false;
        using var accepted = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        accepted.EnsureSuccessStatusCode();
        var payload = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()).IsEqualTo("{}");
    }

    private static JsonObject Request()
    {
        return JsonNode.Parse("""{"model":"gpt-4.1-mini","messages":[{"role":"user","content":"weather"}]}""")!.AsObject();
    }

    private static JsonArray Tools(JsonNode schema)
    {
        return new JsonArray(new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = "weather", ["parameters"] = schema },
        });
    }
}
