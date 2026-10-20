using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using ManagedCode.LlmTestServer.Tools.WellKnown;

namespace ManagedCode.LlmTestServer.Tests.Tools;

public sealed partial class WellKnownToolIntegrationTests
{
    [Test]
    public async Task Catalogue_DefinitionsDriveTwelveActualToolCallsAndKeepLinearExhaustionAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseChatScenario("catalogue", scenario =>
        {
            for (var kind = 0; kind < 12; kind++) { scenario.CallsTools(CreateFixture(kind).Call); }
        }));
        var tools = LlmTestServerWellKnownTools.All.Select(descriptor => new
        {
            type = "function",
            function = new
            {
                name = descriptor.Name,
                description = descriptor.Description,
                parameters = JsonSerializer.Deserialize<JsonElement>(descriptor.ParametersJson)
            }
        }).ToArray();
        var returnedNames = new List<string>();
        for (var index = 0; index < 12; index++)
        {
            using var response = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
            { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "catalogue" } }, tools, tool_choice = "required" });
            response.EnsureSuccessStatusCode();
            var call = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("choices")[0].GetProperty("message").GetProperty("tool_calls")[0];
            returnedNames.Add(call.GetProperty("function").GetProperty("name").GetString()!);
        }
        await Assert.That(returnedNames.Distinct().Count()).IsEqualTo(12);
        using var exhausted = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
        { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "catalogue" } }, tools });
        await Assert.That(exhausted.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(await exhausted.Content.ReadAsStringAsync()).Contains("llm_test_server_scenario_exhausted");
        var verification = await control.VerifyAsync(new()
        {
            NoUnexpectedCalls = true,
            Requests = [new() { Query = new() { Success = true }, MinimumCount = 12, MaximumCount = 12 },
                new() { Query = new() { Status = 409 } }],
        });
        await Assert.That(verification.Passed).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    public async Task Preset_RejectsIncompatibleOfferedSchemaWithoutConsumingTheCallAsync(int kind)
    {
        var fixture = CreateFixture(kind);
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("schema", scenario => scenario.CallsTools(fixture.Call)));
        using var http = host.GetTestClient();
        var incompatible = new[] { new { type = "function", function = new { name = fixture.Definition.Name,
            parameters = new { type = "object", required = new[] { "absent_fixture_field" } } } } };
        using var rejected = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
        { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "schema" } }, tools = incompatible });
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(await rejected.Content.ReadAsStringAsync()).Contains("llm_test_server_fixture_mismatch");
        using var accepted = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
        { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "schema" } }, tools = Tools(fixture) });
        accepted.EnsureSuccessStatusCode();
        var call = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("choices")[0].GetProperty("message").GetProperty("tool_calls")[0];
        await Assert.That(call.GetProperty("id").GetString()).IsEqualTo(fixture.Call.Id);
    }

    [Test]
    public async Task Preset_ToolResultRequiresExactArgumentsAndResultBeforeGraphCanAdvanceAsync()
    {
        var fixture = LlmTestServerWellKnownTools.Calculator.Success(new() { Expression = "6 * 7" }, new() { Value = 42m }, "call_exact_calculator");
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddConversation("exact", graph => graph
            .Step("call", step => step.CallsTools(fixture.Call).Then("result"))
            .Step("result", step => step.WhenToolResult(fixture.Call.Name, fixture.Call.Id).WithToolArgumentsJson(fixture.Call.ArgumentsJson)
                .WhenLatestJson("$", fixture.Result.Content).Responds("accepted").Complete())));
        using var http = host.GetTestClient();
        var tools = Tools(fixture);
        using var initial = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
        { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "calculate" } }, tools });
        initial.EnsureSuccessStatusCode();
        object[] History(string arguments, string result)
        {
            return [
            new { role = "user", content = "calculate" },
            new { role = "assistant", content = "", tool_calls = new[] { new { id = fixture.Call.Id, type = "function", function = new { name = fixture.Call.Name, arguments } } } },
            new { role = "tool", tool_call_id = fixture.Call.Id, content = result },
        ];
        }

        foreach (var history in new[] { History("{\"expression\":\"7 * 7\"}", fixture.Result.Content), History(fixture.Call.ArgumentsJson, "{\"value\":49}") })
        {
            using var rejected = await http.PostAsJsonAsync("/openai/v1/chat/completions", new { model = "gpt-4.1-mini", messages = history, tools });
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
            await Assert.That(await rejected.Content.ReadAsStringAsync()).Contains("conversation_step_mismatch");
        }
        await Assert.That((await new LlmTestServerClient(http).GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        using var accepted = await http.PostAsJsonAsync("/openai/v1/chat/completions", new { model = "gpt-4.1-mini", messages = History(fixture.Call.ArgumentsJson, fixture.Result.Content), tools });
        accepted.EnsureSuccessStatusCode();
        await Assert.That(await accepted.Content.ReadAsStringAsync()).Contains("accepted");
    }
}
