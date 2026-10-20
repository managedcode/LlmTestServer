using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeWebSearchIntegrationTests
{
    [Test]
    [Arguments("duplicateFunctionOutputId")]
    [Arguments("missingFunctionOutputId")]
    [Arguments("zeroLengthCitation")]
    [Arguments("reversedCitation")]
    [Arguments("unexpectedStatus")]
    [Arguments("unexpectedAction")]
    [Arguments("nullCitation")]
    [Arguments("nullItem")]
    [Arguments("unknownKind")]
    [Arguments("nullQueries")]
    [Arguments("emptyQueries")]
    [Arguments("blankQuery")]
    [Arguments("nullSource")]
    [Arguments("invalidSourceUrl")]
    [Arguments("foreignContract")]
    [Arguments("duplicateIds")]
    [Arguments("negativeCitation")]
    [Arguments("oversizedCitation")]
    [Arguments("invalidCitationUrl")]
    [Arguments("nonAsciiCitationText")]
    [Arguments("inconsistentChunks")]
    [Arguments("tooManyQueries")]
    [Arguments("tooManySources")]
    [Arguments("tooManyItems")]
    public async Task InvalidNativeConfiguration_IsAtomicAndLeavesOldFixtureUsableAsync(string defect)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration());
        var configuration = JsonSerializer.SerializeToNode(Configuration(), _configurationJson)!;
        var output = configuration["chatScenarios"]![0]!["responses"]![0]!["outputItems"]!.AsArray();
        var search = output[0]!;
        var message = output[1]!;
        switch (defect)
        {
            case "duplicateFunctionOutputId":
            case "missingFunctionOutputId":
                output.Add(new JsonObject
                {
                    ["kind"] = "function_call",
                    ["id"] = defect == "duplicateFunctionOutputId" ? _searchId : "",
                    ["call"] = new JsonObject { ["id"] = "call_collision_fixture", ["name"] = "weather", ["argumentsJson"] = "{}" },
                });
                break;
            case "zeroLengthCitation": message["citations"]![0]!["endIndex"] = 0; break;
            case "reversedCitation": message["citations"]![0]!["startIndex"] = 4; message["citations"]![0]!["endIndex"] = 3; break;
            case "unexpectedStatus": search["status"] = "failed"; break;
            case "unexpectedAction": search["action"] = new JsonObject { ["type"] = "open_page" }; break;
            case "nullCitation": message["citations"] = new JsonArray((JsonNode?)null); break;
            case "nullItem": output[0] = null; break;
            case "unknownKind": search["kind"] = "file_search"; break;
            case "nullQueries": search["queries"] = null; break;
            case "emptyQueries": search["queries"] = new JsonArray(); break;
            case "blankQuery": search["queries"] = new JsonArray(" "); break;
            case "nullSource": search["sources"] = new JsonArray((JsonNode?)null); break;
            case "invalidSourceUrl": search["sources"]![0]!["url"] = "file:///fixture"; break;
            case "foreignContract": search["contract"] = "unavailableContract"; break;
            case "duplicateIds": message["id"] = _searchId; break;
            case "negativeCitation": message["citations"]![0]!["startIndex"] = -1; break;
            case "oversizedCitation": message["citations"]![0]!["endIndex"] = 999; break;
            case "invalidCitationUrl": message["citations"]![0]!["url"] = "relative-source"; break;
            case "nonAsciiCitationText": message["content"] = "Paris café."; message["streamChunks"] = new JsonArray(); break;
            case "inconsistentChunks": message["streamChunks"] = new JsonArray("different answer"); break;
            case "tooManyQueries": search["queries"] = new JsonArray(Enumerable.Range(0, 33).Select(index => JsonValue.Create($"query {index}")).ToArray()); break;
            case "tooManySources": search["sources"] = new JsonArray(Enumerable.Range(0, 129).Select(_ => (JsonNode)new JsonObject { ["url"] = _sourceUrl }).ToArray()); break;
            case "tooManyItems":
                while (output.Count < 129) { output.Add(new JsonObject { ["kind"] = "message", ["content"] = "extra" }); }
                break;
        }
        using var rejected = await http.PostAsJsonAsync("/admin-api/configure", configuration);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertWireAsync(await CompleteAsync(http));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativePayload_UsesExistingHistoryAndConversationByteBudgetsAsync(bool graph)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var search = (LlmTestServerResponseWebSearchItem)Items()[0];
        search = search with
        {
            Sources = Enumerable.Range(0, 100).Select(index => new LlmTestServerWebSearchSource
            { Url = $"https://example.com/{index}/" + new string('x', 100) }).ToList()
        };
        var builder = new LlmTestServerClientConfigurationBuilder().UseGpt41Mini();
        if (graph)
        {
            builder.UseConversationCapacity(4, 3000).UseConversation("bounded-native", flow => flow
                .Step("search", step => step.RespondsWithItems(search, Items()[1]).Complete()));
        }
        else
        {
            builder.UseChatResponseCapacity(8, 3000).UseChatScenario("bounded-native", scenario => scenario.RespondsWithItems(search, Items()[1]));
        }
        await control.ConfigureAsync(builder.Build());
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", Request());
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var error = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(error.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo(graph ? "conversation_capacity_exceeded" : "llm_test_server_response_capacity_exceeded");
        await Assert.That((await control.GetConversationsAsync()).Count).IsEqualTo(0);
        if (!graph)
        {
            var transient = Request(); transient["store"] = false;
            var accepted = await CompleteAsync(http, transient);
            await Assert.That(accepted.GetProperty("output")[0].GetProperty("action").GetProperty("sources").GetArrayLength()).IsEqualTo(100);
        }
        else
        {
            await control.ConfigureAsync(Configuration());
            await AssertWireAsync(await CompleteAsync(http));
        }
    }
}
