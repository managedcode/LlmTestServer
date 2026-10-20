using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using ManagedCode.LlmTestServer.Tools.WellKnown;
using ManagedCode.LlmTestServer.Verification;

namespace ManagedCode.LlmTestServer.Tests.Tools;

public sealed partial class WellKnownToolIntegrationTests
{
    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    [Arguments(4, false)]
    [Arguments(5, false)]
    [Arguments(6, false)]
    [Arguments(7, false)]
    [Arguments(8, false)]
    [Arguments(9, false)]
    [Arguments(10, false)]
    [Arguments(11, false)]
    [Arguments(0, true)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    [Arguments(3, true)]
    [Arguments(4, true)]
    [Arguments(5, true)]
    [Arguments(6, true)]
    [Arguments(7, true)]
    [Arguments(8, true)]
    [Arguments(9, true)]
    [Arguments(10, true)]
    [Arguments(11, true)]
    public async Task WellKnownPreset_UsesTypedScriptedCallAndExplicitSuccessOrErrorResultAsync(int kind, bool error)
    {
        var fixture = CreateFixture(kind, error);
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseConversation("tool-preset", graph => graph
            .Step("call", step => step.WhenLatestUserEquals("run fixture").CallsTools(fixture.Call).DelaysBy(fixture.DelayMilliseconds).Then("result"))
            .Step("result", step => step.WhenToolResult(fixture.Call.Name, fixture.Call.Id).WithToolArgumentsJson(fixture.Call.ArgumentsJson)
                .WhenLatestJson("$", fixture.Result.Content).RespondsJson(fixture.Result.Content).Complete())));
        using var conversation = control.ForConversation("typed-preset");
        var tools = Tools(fixture);
        using var first = await conversation.HttpClient.PostAsJsonAsync("/openai/v1/chat/completions", new
        { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "run fixture" } }, tools });
        first.EnsureSuccessStatusCode();
        var message = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("choices")[0].GetProperty("message");
        var call = message.GetProperty("tool_calls")[0];
        await Assert.That(call.GetProperty("id").GetString()).IsEqualTo(fixture.Result.ToolCallId);
        await Assert.That(call.GetProperty("function").GetProperty("name").GetString()).IsEqualTo(fixture.Definition.Name);
        using var returnedArguments = JsonDocument.Parse(call.GetProperty("function").GetProperty("arguments").GetString()!);
        using var expectedArguments = JsonDocument.Parse(fixture.Call.ArgumentsJson);
        await Assert.That(JsonElement.DeepEquals(returnedArguments.RootElement, expectedArguments.RootElement)).IsTrue();
        object[] history =
        [
            new { role = "user", content = "run fixture" }, message,
            new { role = fixture.Result.Role, tool_call_id = fixture.Result.ToolCallId, content = fixture.Result.Content },
        ];
        var descriptor = LlmTestServerWellKnownTools.All.Single(tool => tool.Name == fixture.Definition.Name);
        using var second = await conversation.HttpClient.PostAsJsonAsync("/openai/v1/chat/completions", new
        {
            model = "gpt-4.1-mini",
            messages = history,
            tools,
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "fixture_result",
                    schema =
                JsonSerializer.Deserialize<JsonElement>(error ? descriptor.ErrorSchemaJson : descriptor.ResultSchemaJson)
                }
            },
        });
        second.EnsureSuccessStatusCode();
        var answer = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        using var returnedResult = JsonDocument.Parse(answer!);
        using var expectedResult = JsonDocument.Parse(fixture.Result.Content);
        await Assert.That(JsonElement.DeepEquals(returnedResult.RootElement, expectedResult.RootElement)).IsTrue();
        var verification = await conversation.Control.VerifyAsync(new()
        {
            NoUnexpectedCalls = true,
            Requests =
            [
                new() { Query = new() { Step = "call" }, Tools = [new() { Name = fixture.Definition.Name, CallId = fixture.Call.Id, ArgumentsJson = fixture.Call.ArgumentsJson }] },
                new() { Query = new() { Step = "result" }, Tools = [new() { Kind = LlmTestServerToolEvidenceKind.Result, Name = fixture.Definition.Name,
                    CallId = fixture.Call.Id, ResultJson = fixture.Result.Content }] },
            ],
            Conversations = [new() { ScenarioId = "tool-preset", VisitedSteps = ["call", "result"], RequireTerminal = true }],
        });
        await Assert.That(verification.Passed).IsTrue();
        var status = (await conversation.Control.GetConversationsAsync()).Single();
        await Assert.That(status.ExecutedSteps).IsEqualTo(2);
        await Assert.That(status.LastTurnIndex).IsEqualTo(2);
    }

    private static object[] Tools(LlmTestServerWellKnownToolFixture fixture)
    {
        return [new { type = "function", function = new { name = fixture.Definition.Name, description = fixture.Definition.Description,
        parameters = JsonSerializer.Deserialize<JsonElement>(fixture.Definition.ParametersJson) } }];
    }

    private static LlmTestServerWellKnownToolFixture CreateFixture(int kind, bool error = false)
    {
        LlmTestServerWellKnownToolFixture Fixture<TArguments, TResult>(LlmTestServerWellKnownTool<TArguments, TResult> tool, TArguments arguments, TResult result)
            where TArguments : class where TResult : class
        {
            return error
                ? tool.Failure(arguments, new() { Code = "fixture_unavailable", Message = "Scripted tool failure", Retryable = true })
                : tool.Success(arguments, result);
        }

        return kind switch
        {
            0 => Fixture(LlmTestServerWellKnownTools.Calculator, new() { Expression = "6 * 7" }, new() { Value = 42m }),
            1 => Fixture(LlmTestServerWellKnownTools.CurrentTime, new(), new() { CurrentTime = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero) }),
            2 => Fixture(LlmTestServerWellKnownTools.Weather, new() { Location = "Paris", Unit = "celsius" }, new() { Location = "Paris", Temperature = 21m, Unit = "celsius", Condition = "sunny" }),
            3 => Fixture(LlmTestServerWellKnownTools.WebSearch, new() { Query = "fixture search", Limit = 3 }, new() { Results = [new() { Title = "Fixture", Url = "https://example.com/fixture", Snippet = "scripted search hit" }] }),
            4 => Fixture(LlmTestServerWellKnownTools.FetchUrl, new() { Url = "https://example.com/fixture" }, new() { Url = "https://example.com/fixture", ContentType = "text/plain", Content = "scripted page", StatusCode = 200 }),
            5 => Fixture(LlmTestServerWellKnownTools.FileSearch, new() { Query = "fixture", Limit = 2 }, new() { Results = [new() { FileId = "file_fixture", Path = "docs/fixture.txt", Snippet = "scripted file hit" }] }),
            6 => Fixture(LlmTestServerWellKnownTools.ReadFile, new() { Path = "docs/fixture.txt", Offset = 0, Length = 7 }, new() { Path = "docs/fixture.txt", Content = "fixture", Truncated = true }),
            7 => Fixture(LlmTestServerWellKnownTools.WriteFile, new() { Path = "docs/fixture.txt", Content = "fixture", Append = true }, new() { Path = "docs/fixture.txt", BytesWritten = 7 }),
            8 => Fixture(LlmTestServerWellKnownTools.ListFiles, new() { Path = "docs", Recursive = true }, new() { Entries = [new() { Path = "docs/fixture.txt", IsDirectory = false, Bytes = 7 }] }),
            9 => Fixture(LlmTestServerWellKnownTools.RunCode, new() { Language = "python", Code = "print(42)" }, new() { Stdout = "42\n", Stderr = "", ExitCode = 0 }),
            10 => Fixture(LlmTestServerWellKnownTools.Shell, new() { Command = "echo fixture", WorkingDirectory = "workspace" }, new() { Stdout = "fixture\n", Stderr = "", ExitCode = 0 }),
            11 => Fixture(LlmTestServerWellKnownTools.ApplyPatch, new() { Patch = "*** Begin Patch\n*** Add File: fixture.txt\n+fixture\n*** End Patch" }, new() { UpdatedPaths = ["fixture.txt"] }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}
