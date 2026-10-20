using System.Net;
using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using ManagedCode.LlmTestServer.Verification;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.LlmTestServer.Tests.Verification;

public sealed partial class VerificationIntegrationTests
{
    [Test]
    public async Task Verify_CountsFiltersOrderAndUnexpectedCalls_FromActualProviderRequestsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("verify",
            scenario => scenario.Responds("first").Responds("second").Fails(429, "too_many_requests", "retry later")));
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        using var first = await ChatAsync(http, "/openai/v1/chat/completions");
        using var second = await ChatAsync(http, "/mistral/v1/chat/completions");
        using var third = await ChatAsync(http, "/openai/v1/chat/completions");
        await Assert.That(third.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        var expected = new LlmTestServerVerificationRequest
        {
            InOrder = true,
            NoUnexpectedCalls = true,
            Requests =
            [
                new() { Name = "first", Query = new() { Provider = "openai", Scenario = "verify", Status = 200 } },
                new() { Name = "second", Query = new() { Provider = "mistral", Model = "gpt-4.1-mini", Success = true } },
                new() { Name = "third", Query = new() { Provider = "openai", Status = 429, ErrorCode = "too_many_requests" } },
            ],
        };
        var result = await control.VerifyAsync(expected);
        await Assert.That(result.Passed).IsTrue();
        await Assert.That(result.EvidenceComplete).IsTrue();
        await Assert.That(result.Failures).IsEmpty();
        await Assert.That(result.MatchedRequestIds.Count).IsEqualTo(3);
        var reversed = await control.VerifyAsync(expected with { Requests = expected.Requests.AsEnumerable().Reverse().ToList() });
        await Assert.That(reversed.Passed).IsFalse();
        await Assert.That(reversed.Failures.Any(failure => failure.Code == "request.order")).IsTrue();
        var missing = await control.VerifyAsync(expected with { Requests = [expected.Requests[0]] });
        await Assert.That(missing.Passed).IsFalse();
        await Assert.That(missing.Failures.Single().Code).IsEqualTo("request.unexpected");
        await Assert.That(missing.Failures.Single().RequestIds.Count).IsEqualTo(2);
        var wrongCount = await control.VerifyAsync(new()
        { Requests = [new() { Name = "all", MinimumCount = 4, MaximumCount = 4 }] });
        await Assert.That(wrongCount.Failures.Single().Code).IsEqualTo("request.count");
        await Assert.That(wrongCount.Failures.Single().Expected).IsEqualTo("4");
        await Assert.That(wrongCount.Failures.Single().Actual).IsEqualTo("3");
    }

    [Test]
    public async Task Verify_ExplicitZeroAndRequestIdScopes_DoNotIncludeOtherRequestsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("verify", scenario => scenario.Responds("one").Responds("two")));
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var empty = await control.VerifyAsync(new() { NoUnexpectedCalls = true });
        await Assert.That(empty.Passed).IsTrue();
        using var first = await ChatAsync(http, "/openai/v1/chat/completions");
        using var second = await ChatAsync(http, "/mistral/v1/chat/completions");
        var requests = (await http.GetFromJsonAsync<List<LlmTestServerProviderHttpTrace>>("/admin-api/requests"))!;
        var selected = requests.Single(trace => trace.ProviderId == "openai");
        var result = await control.VerifyAsync(new()
        {
            Scope = new() { RequestId = selected.RequestId, From = selected.StartedAt, To = selected.StartedAt },
            NoUnexpectedCalls = true,
            Requests = [new() { Query = new() { Completed = true, Streaming = false } },
                new() { Query = new() { Provider = "gemini" }, MinimumCount = 0, MaximumCount = 0 }],
        });
        await Assert.That(result.Passed).IsTrue();
        await Assert.That(result.MatchedRequestIds).IsEquivalentTo(new[] { selected.RequestId });
        var page = await control.VerifyAsync(new() { Scope = new() { Limit = 1 }, Requests = [new()] });
        await Assert.That(page.Passed).IsFalse();
        await Assert.That(page.EvidenceComplete).IsFalse();
        await Assert.That(page.Failures.Any(failure => failure.Code == "evidence.incomplete")).IsTrue();
        var expectationPage = await control.VerifyAsync(new() { Requests = [new() { Query = new() { Limit = 1 } }] });
        await Assert.That(expectationPage.EvidenceComplete).IsFalse();
        await Assert.That(expectationPage.Failures.Any(failure => failure.Code == "evidence.page")).IsTrue();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Verify_IncompleteJournalNeverProvesNoUnexpectedCallsAsync(bool evict)
    {
        var content = evict ? "answer" : new string('x', 4096);
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("bounded", scenario => scenario.Responds(content).Responds(content)),
            configureServices: services => services.Configure<LlmTestServerProviderHttpTraceOptions>(options =>
            {
                options.MaxEntries = evict ? 1 : 10;
                options.MaxPreviewBytes = evict ? 65536 : 64;
                options.MaxRetainedBytes = 65536;
            }));
        using var http = host.GetTestClient();
        using var first = await ChatAsync(http, "/openai/v1/chat/completions");
        using var second = await ChatAsync(http, "/openai/v1/chat/completions");
        var result = await new LlmTestServerClient(http).VerifyAsync(new()
        { NoUnexpectedCalls = true, Requests = [new() { MinimumCount = 0, MaximumCount = null }] });
        await Assert.That(result.Passed).IsFalse();
        await Assert.That(result.EvidenceComplete).IsFalse();
        await Assert.That(result.Failures.Any(failure => failure.Code == "evidence.incomplete")).IsTrue();
    }

    [Test]
    public async Task Verify_IsolatedSessionsAndConversationClient_RestrictEvidenceAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var a = await root.CreateSessionAsync(config => config.UseConversation("flow", graph => graph.Step("only", step => step.Responds("A").Complete())));
        await using var b = await root.CreateSessionAsync(config => config.UseConversation("flow", graph => graph.Step("only", step => step.Responds("B").Complete())));
        using var aFirst = a.Control.ForConversation("first");
        using var aSecond = a.Control.ForConversation("second");
        using var first = await ChatAsync(aFirst.HttpClient, "/openai/v1/chat/completions");
        using var second = await ChatAsync(aSecond.HttpClient, "/openai/v1/chat/completions");
        using var other = await ChatAsync(b.HttpClient, "/openai/v1/chat/completions");
        var expected = new LlmTestServerVerificationRequest { NoUnexpectedCalls = true, Requests = [new()] };
        await Assert.That((await aFirst.Control.VerifyAsync(expected)).Passed).IsTrue();
        await Assert.That((await b.Control.VerifyAsync(expected)).Passed).IsTrue();
        await Assert.That((await a.Control.VerifyAsync(expected)).Passed).IsFalse();
        await Assert.That((await root.VerifyAsync(new() { NoUnexpectedCalls = true })).Passed).IsTrue();
    }

    private static Task<HttpResponseMessage> ChatAsync(HttpClient http, string path, bool stream = false)
    {
        return http.PostAsJsonAsync(path, new { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "verify" } }, stream });
    }
}
