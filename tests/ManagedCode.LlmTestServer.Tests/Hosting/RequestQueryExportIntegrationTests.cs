using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.LlmTestServer.Tests.Hosting;

public sealed class RequestQueryExportIntegrationTests
{
    [Test]
    public async Task QueryExport_FiltersOrdersAndExportsTheSameRetainedRequestsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("query", scenario => scenario.Responds("one").Fails(429, "retry", "later")));
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        using var conversation = control.ForConversation("query-conversation");
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        using var first = await conversation.HttpClient.PostAsJsonAsync("/openai/v1/chat/completions", new { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "hello" } } });
        first.EnsureSuccessStatusCode();
        using var second = await conversation.HttpClient.PostAsJsonAsync("/openai/v1/chat/completions", new { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "hello" } } });
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        var all = await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(all.TotalObserved).IsEqualTo(2L);
        await Assert.That(all.Requests.Select(entry => entry.Sequence).ToArray()).IsEquivalentTo(new long[] { 1, 2 });
        var query = new LlmTestServerRequestQuery
        {
            Provider = "openai",
            Model = "gpt-4.1-mini",
            Scenario = "query",
            Conversation = "query-conversation",
            Operation = all.Requests[0].OperationId,
            Completed = true,
            Streaming = false,
            Success = false,
            ErrorCode = "retry",
            Status = 429,
            From = before,
            To = DateTimeOffset.UtcNow.AddSeconds(1)
        };
        var selected = await control.GetRequestsAsync(query);
        await Assert.That(selected.Requests.Single().Sequence).IsEqualTo(2L);
        var completed = selected.Requests.Single();
        await Assert.That(completed.StartedMonotonicSeconds).IsNotNull();
        await Assert.That(completed.CompletedMonotonicSeconds).IsNotNull();
        await Assert.That(completed.CompletedMonotonicSeconds!.Value >= completed.StartedMonotonicSeconds!.Value).IsTrue();
        await Assert.That(completed.StartedMonotonicSeconds.Value >= all.Requests[0].CompletedMonotonicSeconds!.Value).IsTrue();
        var exported = await control.ExportRequestsAsync(query);
        await Assert.That(exported.SchemaVersion).IsEqualTo(1);
        await Assert.That(exported.Evidence.Requests.Single().RequestId).IsEqualTo(selected.Requests.Single().RequestId);
        var lines = (await control.ExportRequestsNdjsonAsync(query)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        await Assert.That(lines.Length).IsEqualTo(2);
        using var metadata = JsonDocument.Parse(lines[0]);
        using var record = JsonDocument.Parse(lines[1]);
        await Assert.That(metadata.RootElement.GetProperty("type").GetString()).IsEqualTo("metadata");
        await Assert.That(record.RootElement.GetProperty("request").GetProperty("sequence").GetInt64()).IsEqualTo(2L);
        await Assert.That((await control.GetRequestsAsync(query with { Step = "absent" })).Requests).IsEmpty();
        await Assert.That((await control.GetRequestsAsync(new() { Offset = 1, Limit = 1, Order = LlmTestServerRequestOrder.Ascending })).Requests.Single().Sequence).IsEqualTo(2L);
    }

    [Test]
    public async Task QueryExport_ReportsEvictionAndResetsEvidenceCountersAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("bounded", scenario => scenario.Responds("one").Responds("two")),
            configureServices: services => services.Configure<LlmTestServerProviderHttpTraceOptions>(options => { options.MaxEntries = 1; options.MaxPreviewBytes = 64; }));
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        for (var index = 0; index < 2; index++)
        {
            using var response = await http.PostAsJsonAsync("/openai/v1/chat/completions", new { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "hello" } } });
            response.EnsureSuccessStatusCode();
        }
        var export = await control.ExportRequestsAsync();
        await Assert.That(export.Evidence.TotalObserved).IsEqualTo(2L);
        await Assert.That(export.Evidence.EvictedCount).IsEqualTo(1L);
        await Assert.That(export.Evidence.RetentionTruncated).IsTrue();
        await Assert.That(export.Evidence.FirstSequence).IsEqualTo(2L);
        await control.ResetAsync();
        var reset = await control.ExportRequestsAsync();
        await Assert.That(reset.Evidence.TotalObserved).IsEqualTo(0L);
        await Assert.That(reset.Evidence.EvictedCount).IsEqualTo(0L);
        await Assert.That(reset.Evidence.Requests).IsEmpty();
    }

    [Test]
    [Arguments("order=invalid")]
    [Arguments("completed=invalid")]
    [Arguments("streaming=invalid")]
    [Arguments("success=invalid")]
    [Arguments("from=invalid")]
    [Arguments("to=invalid")]
    [Arguments("from=2026-10-21T00:00:00Z&to=2026-10-20T00:00:00Z")]
    [Arguments("limit=1&limit=2")]
    [Arguments("provider=")]
    [Arguments("format=xml")]
    public async Task QueryExport_RejectsInvalidBoundariesThroughHttpAsync(string query)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        using var response = await http.GetAsync("/admin-api/requests/export?" + query);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task QueryExport_SelectsActiveRequestsAndConversationStepsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddConversation("step-query", graph => graph
            .Step("finish", step => step.WhenLatestUserEquals("start").Responds("done").DelaysBy(350).Complete())));
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        using var conversation = control.ForConversation("step-query-id");
        var pending = conversation.HttpClient.PostAsJsonAsync("/openai/v1/chat/completions", new { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "start" } } });
        LlmTestServerRequestSnapshot active = new();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            active = await control.GetRequestsAsync(new() { Completed = false });
            if (active.Requests.Count > 0) { break; }
            await Task.Delay(10);
        }
        await Assert.That(active.Requests.Count).IsEqualTo(1);
        await Assert.That(active.Requests.Single().IsSuccess).IsNull();
        await Assert.That(active.Requests.Single().StartedMonotonicSeconds).IsNotNull();
        await Assert.That(active.Requests.Single().CompletedMonotonicSeconds).IsNull();
        using var response = await pending;
        response.EnsureSuccessStatusCode();
        var finished = await control.GetRequestsAsync(new() { Scenario = "step-query", Conversation = "step-query-id", Step = "finish", Completed = true, Success = true });
        await Assert.That(finished.Requests.Count).IsEqualTo(1);
        await Assert.That((await control.GetRequestsAsync(new() { Completed = false })).Requests).IsEmpty();
    }

    [Test]
    public async Task QueryExport_RequiresControlAuthorizationAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.RequireBearerToken("export-key"));
        using var http = host.GetTestClient();
        using var rejected = await http.GetAsync("/admin-api/requests/export");
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "export-key");
        var exported = await new LlmTestServerClient(http, "export-key").ExportRequestsAsync();
        await Assert.That(exported.Evidence.Requests).IsEmpty();
    }
}
