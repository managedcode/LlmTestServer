using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Cohere;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Cohere;

public sealed class CohereEndpointTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task ChatEndpoint_ReturnsCohereChatShapeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("command-test", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "cohere-blue-whale",
                    scenario => scenario
                        .ForModel("command-test")
                        .WhenUserContains("largest animal")
                        .Responds("blue whale", "blue ", "whale")
                ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/cohere/v2/chat",
            new
            {
                model = "command-test",
                messages = new[] { new { role = "user", content = "largest animal?" } },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("finish_reason").GetString()).IsEqualTo("COMPLETE");
        await Assert.That(payload.GetProperty("message").GetProperty("role").GetString())
            .IsEqualTo("assistant");
        await Assert.That(
                payload
                    .GetProperty("message")
                    .GetProperty("content")[0]
                    .GetProperty("type")
                    .GetString()
            )
            .IsEqualTo("text");
        await Assert.That(
                payload
                    .GetProperty("message")
                    .GetProperty("content")[0]
                    .GetProperty("text")
                    .GetString()
            )
            .IsEqualTo("blue whale");
        var tokens = payload.GetProperty("usage").GetProperty("tokens");
        var billedUnits = payload.GetProperty("usage").GetProperty("billed_units");

        await Assert.That(tokens.GetProperty("input_tokens").GetInt32()).IsGreaterThan(0);
        await Assert.That(tokens.GetProperty("output_tokens").GetInt32()).IsEqualTo(2);
        await Assert.That(billedUnits.GetProperty("input_tokens").GetInt32())
            .IsEqualTo(tokens.GetProperty("input_tokens").GetInt32());
        await Assert.That(billedUnits.GetProperty("output_tokens").GetInt32()).IsEqualTo(2);
    }

    [Test]
    public async Task ChatEndpoint_StreamsCohereServerSentEventsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("command-test", LlmTestServerModelKind.Chat)
                .AddChatScenario(
                    "cohere-stream",
                    scenario => scenario
                        .ForModel("command-test")
                        .WhenUserContains("stream")
                        .Responds("blue whale", "blue ", "whale")
                ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/cohere/v2/chat",
            new
            {
                model = "command-test",
                stream = true,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[] { new { type = "text", text = "stream response" } },
                    },
                },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("text/event-stream");
        await Assert.That(body).Contains("event: message-start");
        await Assert.That(body).Contains("event: content-start");
        await Assert.That(body).Contains("event: content-delta");
        await Assert.That(body).Contains("event: content-end");
        await Assert.That(body).Contains("event: message-end");
        await Assert.That(body).Contains("\"input_tokens\":");
        await Assert.That(body).Contains("\"output_tokens\":2");
        await Assert.That(body).Contains("blue ");
        await Assert.That(body).Contains("whale");
    }

    [Test]
    public async Task ChatEndpoint_StreamsParallelToolFramesAndCompletesTheToolRoundAsync()
    {
        const string model = "command-test";
        LlmTestServerToolCall[] expectedCalls =
        [
            new() { Id = "call_paris", Name = "weather", ArgumentsJson = "{\"city\":\"Paris\",\"note\":null}" },
            new() { Id = "call_tokyo", Name = "weather", ArgumentsJson = "{\"city\":\"東京\",\"days\":[1,2]}" }
        ];
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseChatModel(model).UseConversation("cohere-parallel-round", graph => graph
            .ForModel(model)
            .Step("tools", step => step.WhenLatestUserEquals("weather").WhenTurn(1).CallsTools(expectedCalls).Then("answer"))
            .Step("answer", step => step.WhenTurn(2).WhenToolResult("weather", "call_tokyo")
                .WhenLatestTextContains("22C").Responds("Paris 21C; Tokyo 22C.").Complete())));
        using var conversation = control.ForConversation("cohere-parallel-round");
        var request = new CohereChatRequest
        {
            Model = model,
            ToolChoice = "REQUIRED",
            Stream = true,
            Messages = [new() { Role = "user", Content = JsonSerializer.SerializeToElement("weather") }],
            Tools = [new() { Function = new()
            {
                Name = "weather",
                Parameters = JsonSerializer.SerializeToElement(new
                {
                    type = "object", properties = new { city = new { type = "string" } }, required = new[] { "city" }
                })
            } }]
        };
        using var response = await conversation.HttpClient.PostAsJsonAsync("/cohere/v2/chat", request, _jsonOptions);
        response.EnsureSuccessStatusCode();
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/event-stream");
        var body = await response.Content.ReadAsStringAsync();
        var frames = body.Split('\n').Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line[6..])).ToArray();
        await Assert.That(string.Join(",", frames.Select(frame => frame.GetProperty("type").GetString())))
            .IsEqualTo("message-start,tool-call-start,tool-call-delta,tool-call-end,tool-call-start,tool-call-delta,tool-call-end,message-end");
        var delivered = new List<CohereToolCall>();
        for (var index = 0; index < expectedCalls.Length; index++)
        {
            var start = frames[1 + index * 3];
            var delta = frames[2 + index * 3];
            var end = frames[3 + index * 3];
            await Assert.That(string.Join(",", start.EnumerateObject().Select(property => property.Name))).IsEqualTo("type,index,delta");
            await Assert.That(string.Join(",", delta.EnumerateObject().Select(property => property.Name))).IsEqualTo("type,index,delta");
            await Assert.That(string.Join(",", end.EnumerateObject().Select(property => property.Name))).IsEqualTo("type,index");
            foreach (var frame in new[] { start, delta, end })
            {
                await Assert.That(frame.GetProperty("index").GetInt32()).IsEqualTo(index);
            }
            var call = start.GetProperty("delta").GetProperty("message").GetProperty("tool_calls").Deserialize<CohereToolCall>(_jsonOptions)!;
            await Assert.That(call.Id).IsEqualTo(expectedCalls[index].Id);
            await Assert.That(call.Type).IsEqualTo("function");
            await Assert.That(call.Function.Name).IsEqualTo(expectedCalls[index].Name);
            await Assert.That(call.Function.Arguments).IsEqualTo("");
            var function = delta.GetProperty("delta").GetProperty("message").GetProperty("tool_calls").GetProperty("function");
            await Assert.That(string.Join(",", function.EnumerateObject().Select(property => property.Name))).IsEqualTo("arguments");
            var arguments = function.GetProperty("arguments").GetString()!;
            await Assert.That(arguments).IsEqualTo(expectedCalls[index].ArgumentsJson);
            delivered.Add(call with { Function = call.Function with { Arguments = arguments } });
        }
        await Assert.That(frames[^1].GetProperty("delta").GetProperty("finish_reason").GetString()).IsEqualTo("TOOL_CALL");
        using var finalResponse = await conversation.HttpClient.PostAsJsonAsync("/cohere/v2/chat", request with
        {
            Stream = false,
            ToolChoice = "NONE",
            Messages = [.. request.Messages,
                new() { Role = "assistant", Content = JsonSerializer.SerializeToElement(""), ToolCalls = delivered },
                new() { Role = "tool", ToolCallId = delivered[0].Id, Content = JsonSerializer.SerializeToElement("21C") },
                new() { Role = "tool", ToolCallId = delivered[1].Id, Content = JsonSerializer.SerializeToElement("22C") }]
        }, _jsonOptions);
        finalResponse.EnsureSuccessStatusCode();
        var final = await finalResponse.Content.ReadFromJsonAsync<CohereChatResponse>(_jsonOptions)
            ?? throw new InvalidOperationException("The completed Cohere tool round must return its terminal response.");
        await Assert.That(final.FinishReason).IsEqualTo("COMPLETE");
        await Assert.That(final.Message.Content.Single().Text).IsEqualTo("Paris 21C; Tokyo 22C.");
        var state = (await conversation.Control.GetConversationsAsync()).Single();
        await Assert.That(state.ExecutedSteps).IsEqualTo(2);
        await Assert.That(state.IsTerminal).IsTrue();
        var traces = (await conversation.Control.GetRequestsAsync()).Requests;
        await Assert.That(traces.Count).IsEqualTo(2);
        await Assert.That(traces[0].Messages.Where(message => message.Role == "tool").Select(message => message.ToolCallId!))
            .IsEquivalentTo(expectedCalls.Select(call => call.Id));
        await Assert.That(traces[1].ResponseToolCalls.Select(call => call.ArgumentsJson))
            .IsEquivalentTo(expectedCalls.Select(call => call.ArgumentsJson));
    }

    [Test]
    public async Task EmbedEndpoint_ReturnsCohereEmbeddingShapeForTextsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("embed-v4.0", LlmTestServerModelKind.Embedding)
                .WithDefaultEmbeddingVector(0.25f, 0.5f));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/cohere/v2/embed",
            new
            {
                model = "embed-v4.0",
                input_type = "classification",
                embedding_types = new[] { "float" },
                texts = new[] { "alpha", "beta" },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("embeddings").GetProperty("float").GetArrayLength())
            .IsEqualTo(2);
        await Assert.That(payload.GetProperty("embeddings").GetProperty("float")[0].GetArrayLength())
            .IsEqualTo(2);
    }

    [Test]
    public async Task EmbedEndpoint_ReturnsCohereEmbeddingShapeForInputsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .AddModel("embed-v4.0", LlmTestServerModelKind.Embedding)
                .WithDefaultEmbeddingVector(0.125f, 0.25f));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/cohere/v2/embed",
            new
            {
                model = "embed-v4.0",
                input_type = "classification",
                embedding_types = new[] { "float" },
                inputs = new[]
                {
                    new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "alpha" },
                            new { type = "text", text = "beta" },
                        },
                    },
                },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        await Assert.That(payload.GetProperty("embeddings").GetProperty("float").GetArrayLength())
            .IsEqualTo(1);
        await Assert.That(payload.GetProperty("embeddings").GetProperty("float")[0].GetArrayLength())
            .IsEqualTo(2);
    }
}
