using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using ManagedCode.LlmTestServer.Verification;

namespace ManagedCode.LlmTestServer.Tests.Verification;

public sealed partial class VerificationIntegrationTests
{
    [Test]
    public async Task Verify_InspectsResponseCallsRequestCallsToolResultsAndVisitedGraphStepsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseConversation("weather", graph => graph
            .Step("lookup", step => step.WhenLatestUserEquals("weather").CallsTool("weather", "{\"city\":\"Paris\"}", "call_paris").Then("answer"))
            .Step("answer", step => step.WhenToolResult("weather", "call_paris").Responds("21 C").Complete())));
        using var conversation = control.ForConversation("weather-42");
        var tools = new[] { new { type = "function", function = new { name = "weather", parameters = new { type = "object" } } } };
        using var first = await conversation.HttpClient.PostAsJsonAsync("/openai/v1/chat/completions", new
        { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "weather" } }, tools });
        first.EnsureSuccessStatusCode();
        var initial = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("choices")[0].GetProperty("message");
        object[] messages =
        [
            new { role = "user", content = "weather" }, initial,
            new { role = "tool", tool_call_id = "call_paris", content = "{\"temperature\":21,\"forecast\":[\"sunny\"]}" },
        ];
        using var second = await conversation.HttpClient.PostAsJsonAsync("/openai/v1/chat/completions", new { model = "gpt-4.1-mini", messages, tools });
        second.EnsureSuccessStatusCode();
        var expected = new LlmTestServerVerificationRequest
        {
            NoUnexpectedCalls = true,
            InOrder = true,
            Requests =
            [
                new() { Name = "lookup", Query = new() { Step = "lookup" }, Tools =
                    [new() { Kind = LlmTestServerToolEvidenceKind.ResponseCall, Name = "weather", CallId = "call_paris", ArgumentsJson = "{\"city\":\"Paris\"}" }] },
                new() { Name = "answer", Query = new() { Step = "answer" }, Tools =
                    [new() { Kind = LlmTestServerToolEvidenceKind.RequestCall, Name = "weather", CallId = "call_paris", ArgumentsJson = "{\"city\":\"Paris\"}" },
                     new() { Kind = LlmTestServerToolEvidenceKind.Result, Name = "weather", CallId = "call_paris", ResultJson = "{\"forecast\":[\"sunny\"],\"temperature\":21}",
                         ResultFields = [new() { Path = "$.forecast[0]", EqualsJson = "\"sunny\"" }, new() { Path = "/temperature", EqualsJson = "21" }] }] },
            ],
            Conversations = [new() { ScenarioId = "weather", ConversationId = "weather-42", ProviderNamespace = "/openai", VisitedSteps = ["lookup", "answer"], RequireTerminal = true }],
        };
        var result = await conversation.Control.VerifyAsync(expected);
        await Assert.That(result.Passed).IsTrue();
        await Assert.That(result.MatchedRequestIds.Count).IsEqualTo(2);
        var badTool = await conversation.Control.VerifyAsync(expected with
        { Requests = [expected.Requests[0] with { Tools = [new() { Name = "different", Kind = LlmTestServerToolEvidenceKind.ResponseCall }] }], NoUnexpectedCalls = false });
        await Assert.That(badTool.Passed).IsFalse();
        await Assert.That(badTool.Failures.Any(failure => failure.Code == "tool.count")).IsTrue();
        var missingStep = await conversation.Control.VerifyAsync(new()
        { Conversations = [new() { ScenarioId = "weather", VisitedSteps = ["nonexistent"] }] });
        await Assert.That(missingStep.Failures.Single().Code).IsEqualTo("conversation.steps");
        var missingGraph = await conversation.Control.VerifyAsync(new() { Conversations = [new() { ScenarioId = "missing" }] });
        await Assert.That(missingGraph.Failures.Single().Code).IsEqualTo("conversation.missing");
    }

    [Test]
    public async Task Verify_RequiresVisitedStepsAndTerminalStateBeforeConversationCompletesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseConversation("active", graph => graph
            .Step("start", step => step.Responds("ready").Then("finish"))
            .Step("finish", step => step.Responds("done").Complete())));
        using var first = await ChatAsync(http, "/openai/v1/chat/completions");
        first.EnsureSuccessStatusCode();
        var result = await control.VerifyAsync(new()
        { Conversations = [new() { ScenarioId = "active", VisitedSteps = ["start", "finish"], RequireTerminal = true }] });
        await Assert.That(result.Passed).IsFalse();
        await Assert.That(result.Failures.Select(failure => failure.Code)).IsEquivalentTo(new[] { "conversation.steps", "conversation.terminal" });
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Verify_StreamingTerminalMarkerUsesObservedTransportEvidenceAsync(bool stream)
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("stream", scenario => scenario.Responds("hello", "hel", "lo")));
        using var http = host.GetTestClient();
        using var response = await ChatAsync(http, "/openai/v1/chat/completions", stream);
        response.EnsureSuccessStatusCode();
        var wire = await response.Content.ReadAsStringAsync();
        if (stream) { await Assert.That(wire).Contains("[DONE]"); }
        var result = await new LlmTestServerClient(http).VerifyAsync(new()
        { Requests = [new() { RequireStreamingTerminalMarker = true }] });
        await Assert.That(result.Passed).IsEqualTo(stream);
        await Assert.That(result.Failures.Any(failure => failure.Code == "stream.terminal")).IsEqualTo(!stream);
    }

    [Test]
    public async Task Verify_BoundedRetriesInspectStatusesRetryAfterAndAttemptCountAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("retries", scenario => scenario
            .Fails(429, "too_many_requests", "wait").RetryAfter(0).Fails(503, "unavailable", "wait").RetryAfter(0).Responds("recovered")));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("X-Llm-Test-Server-Retry-Group", "retry-group");
        using var first = await ChatAsync(http, "/openai/v1/chat/completions");
        using var second = await ChatAsync(http, "/openai/v1/chat/completions");
        using var third = await ChatAsync(http, "/openai/v1/chat/completions");
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        third.EnsureSuccessStatusCode();
        var control = new LlmTestServerClient(http);
        var retry = new LlmTestServerRetryExpectation
        { GroupId = "retry-group", MinimumAttempts = 3, MaximumAttempts = 3, StatusSequence = [429, 503, 200], MinimumRetryAfterSeconds = 0, RespectRetryAfter = true };
        var result = await control.VerifyAsync(new() { NoUnexpectedCalls = true, Retries = [retry] });
        await Assert.That(result.Passed).IsTrue();
        await Assert.That(result.MatchedRequestIds.Count).IsEqualTo(3);
        var excess = await control.VerifyAsync(new() { Retries = [retry with { MinimumAttempts = 1, MaximumAttempts = 2 }] });
        await Assert.That(excess.Failures.Any(failure => failure.Code == "retry.attempts")).IsTrue();
        var wrongStatuses = await control.VerifyAsync(new() { Retries = [retry with { StatusSequence = [429, 200] }] });
        await Assert.That(wrongStatuses.Failures.Any(failure => failure.Code == "retry.statuses")).IsTrue();
        var insufficient = await control.VerifyAsync(new() { Retries = [retry with { MinimumRetryAfterSeconds = 1 }] });
        await Assert.That(insufficient.Failures.Count(failure => failure.Code == "retry.after")).IsEqualTo(2);
        var missing = await control.VerifyAsync(new() { Retries = [retry with { GroupId = "not-observed" }] });
        await Assert.That(missing.Failures.Any(failure => failure.Code == "retry.attempts")).IsTrue();
    }

    [Test]
    public async Task Verify_RetryAfterComplianceAcceptsObservedWaitBeforeRecoveryAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("waited", scenario => scenario
            .Fails(429, "too_many_requests", "wait").RetryAfter(1).Responds("recovered")));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("X-Llm-Test-Server-Retry-Group", "waited-retry");
        using var first = await ChatAsync(http, "/openai/v1/chat/completions");
        await Assert.That(first.Headers.RetryAfter!.Delta).IsEqualTo(TimeSpan.FromSeconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        using var second = await ChatAsync(http, "/openai/v1/chat/completions");
        second.EnsureSuccessStatusCode();
        var traces = (await http.GetFromJsonAsync<List<LlmTestServerProviderHttpTrace>>("/admin-api/requests"))!
            .OrderBy(trace => trace.Sequence).ToArray();
        await Assert.That(traces.Length).IsEqualTo(2);
        foreach (var trace in traces)
        {
            await Assert.That(trace.StartedMonotonicSeconds.HasValue).IsTrue();
            await Assert.That(trace.CompletedMonotonicSeconds.HasValue).IsTrue();
            await Assert.That(double.IsFinite(trace.StartedMonotonicSeconds!.Value)).IsTrue();
            await Assert.That(double.IsFinite(trace.CompletedMonotonicSeconds!.Value)).IsTrue();
            await Assert.That(trace.CompletedMonotonicSeconds.Value).IsGreaterThanOrEqualTo(trace.StartedMonotonicSeconds.Value);
        }
        await Assert.That(traces[1].StartedMonotonicSeconds!.Value - traces[0].CompletedMonotonicSeconds!.Value).IsGreaterThanOrEqualTo(1);
        var result = await new LlmTestServerClient(http).VerifyAsync(new()
        {
            NoUnexpectedCalls = true,
            Retries = [new() { GroupId = "waited-retry", MinimumAttempts = 2, MaximumAttempts = 2,
                StatusSequence = [429, 200], MinimumRetryAfterSeconds = 1, RespectRetryAfter = true }],
        });
        await Assert.That(result.Passed).IsTrue();
        await Assert.That(result.Failures).IsEmpty();
    }

    [Test]
    public async Task Verify_RetryAfterComplianceRejectsPrematureRetryAndMissingHeaderAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("early", scenario => scenario
            .Fails(429, "too_many_requests", "wait").RetryAfter(60).Responds("too early")));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("X-Llm-Test-Server-Retry-Group", "early-retry");
        using var first = await ChatAsync(http, "/openai/v1/chat/completions");
        using var second = await ChatAsync(http, "/openai/v1/chat/completions");
        var control = new LlmTestServerClient(http);
        var expected = new LlmTestServerVerificationRequest
        { Retries = [new() { GroupId = "early-retry", MinimumAttempts = 2, MaximumAttempts = 2, RespectRetryAfter = true }] };
        var result = await control.VerifyAsync(expected);
        await Assert.That(result.Passed).IsFalse();
        await Assert.That(result.Failures.Single().Code).IsEqualTo("retry.delay");
        await control.ConfigureAsync(config => config.UseChatScenario("missing-header", scenario => scenario.Fails(503, "unavailable", "wait").Responds("retry")));
        using var missingHeader = await ChatAsync(http, "/openai/v1/chat/completions");
        using var retry = await ChatAsync(http, "/openai/v1/chat/completions");
        var missing = await control.VerifyAsync(expected with { Retries = [expected.Retries[0] with { MinimumRetryAfterSeconds = 0 }] });
        await Assert.That(missing.Failures.Select(failure => failure.Code)).IsEquivalentTo(new[] { "retry.after", "retry.delay" });
    }
}
