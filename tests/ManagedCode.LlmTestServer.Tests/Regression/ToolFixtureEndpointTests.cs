using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Regression;

public sealed class ToolFixtureEndpointTests
{
    private const string _schema = """{"type":"object","properties":{"city":{"type":"string","enum":["Paris"]}},"required":["city"],"additionalProperties":false}""";
    private const string _answer = """{"city":"Paris"}""";

    [Test]
    [Arguments("/openai/v1/chat/completions", 0)]
    [Arguments("/azure-openai/openai/deployments/gpt-4.1-mini/chat/completions?api-version=2024-10-21", 0)]
    [Arguments("/azure-openai/openai/v1/chat/completions", 0)]
    [Arguments("/microsoft-foundry/chat/completions?api-version=2024-05-01-preview", 0)]
    [Arguments("/microsoft-foundry/models/chat/completions?api-version=2024-05-01-preview", 0)]
    [Arguments("/microsoft-foundry/openai/v1/chat/completions", 0)]
    [Arguments("/groq/openai/v1/chat/completions", 0)]
    [Arguments("/mistral/v1/chat/completions", 0)]
    [Arguments("/openrouter/api/v1/chat/completions", 0)]
    [Arguments("/deepseek/v1/chat/completions", 0)]
    [Arguments("/openai/v1/responses", 1)]
    [Arguments("/groq/openai/v1/responses", 1)]
    [Arguments("/openrouter/api/v1/responses", 1)]
    [Arguments("/azure-openai/openai/v1/responses", 1)]
    [Arguments("/microsoft-foundry/openai/v1/responses", 1)]
    [Arguments("/anthropic/v1/messages", 2)]
    [Arguments("/gemini/v1beta/models/gpt-4.1-mini:generateContent", 3)]
    [Arguments("/ollama/api/chat", 4)]
    [Arguments("/cohere/v2/chat", 5)]
    [Arguments("/bedrock/model/gpt-4.1-mini/converse", 6)]
    public async Task ToolsAndSchema_RoundTripAndRejectMismatchedFixturesAsync(string path, int format)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("tools", s => s.CallsTool("weather", _answer).RespondsJson(_answer)));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var request = Request(format, tools: true);
        using var first = await PostAsync(http, path, request);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var payload = JsonNode.Parse(await first.Content.ReadAsStringAsync())!;
        var call = format switch
        {
            1 => payload["output"]![0]!,
            2 => payload["content"]![0]!,
            3 => payload["candidates"]![0]!["content"]!["parts"]![0]!["functionCall"]!,
            4 or 5 => payload["message"]!["tool_calls"]![0]!,
            6 => payload["output"]!["message"]!["content"]![0]!["toolUse"]!,
            _ => payload["choices"]![0]!["message"]!["tool_calls"]![0]!,
        };
        var function = format is 0 or 4 or 5 ? call["function"]! : call;
        await Assert.That(function["name"]!.GetValue<string>()).IsEqualTo("weather");
        var arguments = format switch { 2 or 6 => function["input"]!, 3 => function["args"]!, _ => function["arguments"]! };
        var argumentObject = arguments is JsonValue ? JsonNode.Parse(arguments.GetValue<string>())! : arguments;
        await Assert.That(argumentObject["city"]!.GetValue<string>()).IsEqualTo("Paris");

        var second = Request(format, tools: false);
        AddHistory(second, payload, format);
        var invalid = second.DeepClone();
        // Same structural schema, incompatible enum: must not consume the second response.
        var invalidText = invalid.ToJsonString().Replace("Paris", "London", StringComparison.Ordinal);
        using var mismatch = await http.PostAsync(path, new StringContent(invalidText, Encoding.UTF8, "application/json"));
        await Assert.That(mismatch.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var final = await PostAsync(http, path, second);
        await Assert.That(final.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await final.Content.ReadAsStringAsync()).Contains("Paris");
        var events = (await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!.Events;
        await Assert.That(events.Any(entry => entry.Messages.Any(message => message.Role == "tool" && message.Content.Contains("sunny", StringComparison.Ordinal)))).IsTrue();
    }

    [Test]
    [Arguments("/openai/v1/chat/completions", 0, "tool_calls")]
    [Arguments("/openai/v1/responses", 1, "response.function_call_arguments.delta")]
    [Arguments("/anthropic/v1/messages", 2, "input_json_delta")]
    [Arguments("/gemini/v1beta/models/gpt-4.1-mini:streamGenerateContent?alt=sse", 3, "functionCall")]
    [Arguments("/ollama/api/chat", 4, "tool_calls")]
    [Arguments("/cohere/v2/chat", 5, "tool-call-delta")]
    [Arguments("/bedrock/model/gpt-4.1-mini/converse-stream", 6, "contentBlockDelta")]
    public async Task StreamingTools_IncludeArgumentsAndProviderEventsAsync(string path, int format, string eventType)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("tools", s => s.CallsTool("weather", _answer)));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var request = Request(format, tools: true);
        request["stream"] = true;
        using var response = await PostAsync(http, path, request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        await Assert.That(text).Contains(eventType);
        await Assert.That(text).Contains("Paris");
        await Assert.That(text.Split("weather", StringSplitOptions.None).Length - 1).IsGreaterThan(0);
    }

    [Test]
    public async Task Perplexity_ValidatesJsonSchemaAndRejectsUnclaimedFunctionToolsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("json", s => s.RespondsJson(_answer)));
        using var http = host.GetTestClient();
        using var rejected = await PostAsync(http, "/perplexity/v1/sonar", Request(0, tools: true));
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var response = await PostAsync(http, "/perplexity/v1/sonar", Request(0, tools: false));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("Paris");
    }

    internal static JsonObject Request(int format, bool tools)
    {
        var body = JsonNode.Parse("""{"model":"gpt-4.1-mini","max_tokens":100,"stream":false,"messages":[{"role":"user","content":"weather"}]}""")!.AsObject();
        var schema = JsonNode.Parse(_schema)!;
        if (format == 1)
        {
            body["input"] = "weather";
        }

        if (format == 3)
        {
            body["contents"] = JsonNode.Parse("""[{"role":"user","parts":[{"text":"weather"}]}]""");
        }

        if (format == 6)
        {
            body["messages"] = JsonNode.Parse("""[{"role":"user","content":[{"text":"weather"}]}]""");
        }

        if (tools)
        {
            body["tools"] = format switch
            {
                1 => new JsonArray(new JsonObject { ["type"] = "function", ["name"] = "weather", ["parameters"] = schema }),
                2 => new JsonArray(new JsonObject { ["name"] = "weather", ["input_schema"] = schema }),
                3 => new JsonArray(new JsonObject { ["functionDeclarations"] = new JsonArray(new JsonObject { ["name"] = "weather", ["parametersJsonSchema"] = schema }) }),
                _ => new JsonArray(new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = "weather", ["parameters"] = schema } }),
            };
            if (format == 6)
            {
                body.Remove("tools");
                body["toolConfig"] = new JsonObject { ["tools"] = new JsonArray(new JsonObject { ["toolSpec"] = new JsonObject { ["name"] = "weather", ["inputSchema"] = new JsonObject { ["json"] = schema.DeepClone() } } }) };
            }
        }
        else
        {
            switch (format)
            {
                case 1: body["text"] = new JsonObject { ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = schema } }; break;
                case 2: body["output_config"] = new JsonObject { ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = schema } }; break;
                case 3: body["generationConfig"] = new JsonObject { ["responseMimeType"] = "application/json", ["responseJsonSchema"] = schema }; break;
                case 4: body["format"] = schema; break;
                case 5: body["response_format"] = new JsonObject { ["type"] = "json_object", ["schema"] = schema }; break;
                case 6: body["outputConfig"] = new JsonObject { ["textFormat"] = new JsonObject { ["type"] = "json_schema", ["structure"] = new JsonObject { ["jsonSchema"] = new JsonObject { ["schema"] = schema.ToJsonString() } } } }; break;
                default: body["response_format"] = new JsonObject { ["type"] = "json_schema", ["json_schema"] = new JsonObject { ["name"] = "weather", ["schema"] = schema } }; break;
            }
        }
        return body;
    }

    private static void AddHistory(JsonObject body, JsonNode response, int format)
    {
        var result = JsonNode.Parse("""{"role":"tool","content":"sunny","tool_call_id":"call_fixture"}""")!;
        switch (format)
        {
            case 1: body["input"] = new JsonArray(response["output"]![0]!.DeepClone(), new JsonObject { ["type"] = "function_call_output", ["call_id"] = "call_fixture", ["output"] = "sunny" }); break;
            case 2: body["messages"]!.AsArray().Add(new JsonObject { ["role"] = "assistant", ["content"] = response["content"]!.DeepClone() }); body["messages"]!.AsArray().Add(JsonNode.Parse("""{"role":"user","content":[{"type":"tool_result","tool_use_id":"call_fixture","content":"sunny"}]}""")); break;
            case 3: body["contents"]!.AsArray().Add(response["candidates"]![0]!["content"]!.DeepClone()); body["contents"]!.AsArray().Add(JsonNode.Parse("""{"role":"user","parts":[{"functionResponse":{"id":"call_fixture","name":"weather","response":{"text":"sunny"}}}]}""")); break;
            case 6: body["messages"]!.AsArray().Add(response["output"]!["message"]!.DeepClone()); body["messages"]!.AsArray().Add(JsonNode.Parse("""{"role":"user","content":[{"toolResult":{"toolUseId":"call_fixture","content":[{"text":"sunny"}]}}]}""")); break;
            default: body["messages"]!.AsArray().Add((format is 4 or 5 ? response["message"]! : response["choices"]![0]!["message"]!).DeepClone()); if (format == 4) { result["tool_name"] = "weather"; } body["messages"]!.AsArray().Add(result); break;
        }
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, string path, JsonNode body)
    {
        return http.PostAsync(path, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
    }
}
