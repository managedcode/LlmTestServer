using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Conversations;

public sealed partial class ConversationHttpIntegrationTests
{
    [Test]
    [Arguments("$.forecast[0].temperature", "21", "{\"forecast\":[{\"temperature\":21}]}")]
    [Arguments("/a~1b/~0value/0", "\"sunny\"", "{\"a/b\":{\"~value\":[\"sunny\"]}}")]
    [Arguments("$", "{\"ok\":true}", "{\"ok\":true}")]
    public async Task ToolResult_RequiresMatchingCallNameIdArgumentsAndJsonFieldsAsync(string path, string expectedJson, string validResult)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversation("weather", graph => graph
            .Step("ask", step => step.WhenLatestUserEquals("weather").CallsTool("weather", "{\"city\":\"Paris\",\"units\":\"C\"}", "call_paris").Then("answer"))
            .Step("answer", step => step.WhenToolResult("weather", "call_paris").WithToolArgumentsJson("{\"units\":\"C\",\"city\":\"Paris\"}")
                .WhenLatestJson(path, expectedJson).WhenTurn(2).Responds("Paris forecast accepted").Complete())));
        using var conversation = root.ForConversation("weather-42");
        object[] tools = [new { type = "function", function = new { name = "weather", parameters = new { type = "object" } } }];
        using var start = await SendChatAsync(conversation.HttpClient, [User("weather")], tools: tools);
        start.EnsureSuccessStatusCode();
        var wire = await start.Content.ReadFromJsonAsync<JsonElement>();
        var call = wire.GetProperty("choices")[0].GetProperty("message").GetProperty("tool_calls")[0];
        await Assert.That(call.GetProperty("id").GetString()).IsEqualTo("call_paris");
        await Assert.That(call.GetProperty("function").GetProperty("name").GetString()).IsEqualTo("weather");
        object[] History(string name, string id, string arguments, string result, string resultId)
        {
            return [
            User("weather"),
            new { role = "assistant", content = "", tool_calls = new[] { new { id, type = "function", function = new { name, arguments } } } },
            new { role = "tool", content = result, tool_call_id = resultId },
        ];
        }

        await AssertErrorAsync(conversation.HttpClient, History("other", "call_paris", "{\"city\":\"Paris\",\"units\":\"C\"}", validResult, "call_paris"), "conversation_step_mismatch", tools: tools);
        await AssertErrorAsync(conversation.HttpClient, History("weather", "call_other", "{\"city\":\"Paris\",\"units\":\"C\"}", validResult, "call_other"), "conversation_step_mismatch", tools: tools);
        await AssertErrorAsync(conversation.HttpClient, History("weather", "call_paris", "{\"city\":\"London\",\"units\":\"C\"}", validResult, "call_paris"), "conversation_step_mismatch", tools: tools);
        await AssertErrorAsync(conversation.HttpClient, History("weather", "call_paris", "{\"city\":\"Paris\",\"units\":\"C\"}", "{}", "call_paris"), "conversation_step_mismatch", tools: tools);
        await Assert.That((await root.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        await AssertChatAsync(conversation.HttpClient, History("weather", "call_paris", "{\"units\":\"C\",\"city\":\"Paris\"}", validResult, "call_paris"), "Paris forecast accepted", tools);
        var completed = (await root.GetConversationsAsync()).Single();
        await Assert.That(completed.IsTerminal).IsTrue();
        await Assert.That(completed.ExecutedSteps).IsEqualTo(2);
    }

    [Test]
    public async Task InvalidStructuredFixture_DoesNotCreateOrAdvanceConversationAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await root.ConfigureAsync(config => config.UseConversation("plain", graph => graph
            .Step("plain", step => step.WhenLatestUserEquals("hello").Responds("plain text").Complete())));
        using var conversation = root.ForConversation("schema-retry");
        using var invalid = await SendChatAsync(conversation.HttpClient, [User("hello")], responseFormat: new { type = "json_object" });
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(await invalid.Content.ReadAsStringAsync()).Contains("llm_test_server_fixture_mismatch");
        await Assert.That((await root.GetConversationsAsync()).Count).IsEqualTo(0);
        await AssertChatAsync(conversation.HttpClient, [User("hello")], "plain text");
        await Assert.That((await root.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        using var invalidReplay = await SendChatAsync(conversation.HttpClient, [User("hello")], responseFormat: new { type = "json_object" });
        await Assert.That(invalidReplay.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await AssertChatAsync(conversation.HttpClient, [User("hello")], "plain text");
        await Assert.That((await root.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
    }
}
