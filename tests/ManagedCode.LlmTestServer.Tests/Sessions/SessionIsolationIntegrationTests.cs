using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Sessions;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.LlmTestServer.Tests.Sessions;

public sealed class SessionIsolationIntegrationTests
{
    [Test]
    public async Task ParallelSessions_IsolateIdenticalPromptsQueuesJournalsAndResetAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.RequireBearerToken("shared-key"));
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http, "shared-key");
        await using var first = await root.CreateSessionAsync(config => config.RequireBearerToken("shared-key")
            .UseChatScenario("same", scenario => scenario.Responds("A1").Responds("A2")));
        await using var second = await root.CreateSessionAsync(config => config.RequireBearerToken("shared-key")
            .UseChatScenario("same", scenario => scenario.Responds("B1").Responds("B2")));
        using var a = first.Control.CreateChatClient();
        using var b = second.Control.CreateChatClient();
        var replies = await Task.WhenAll(a.GetResponseAsync("same prompt"), b.GetResponseAsync("same prompt"));
        await Assert.That(replies[0].Text).IsEqualTo("A1");
        await Assert.That(replies[1].Text).IsEqualTo("B1");
        await first.Control.ResetAsync();
        await Assert.That((await a.GetResponseAsync("same prompt")).Text).IsEqualTo("A1");
        await Assert.That((await b.GetResponseAsync("same prompt")).Text).IsEqualTo("B2");
        var firstSummary = await first.Control.GetAssertionsAsync();
        var secondSummary = await second.Control.GetAssertionsAsync();
        await Assert.That(firstSummary.Matched).IsEqualTo(1);
        await Assert.That(secondSummary.Matched).IsEqualTo(2);
        await Assert.That((await root.GetAssertionsAsync()).TotalEvents).IsEqualTo(0);
        foreach (var session in new[] { first, second })
        {
            session.HttpClient.DefaultRequestHeaders.Authorization = new("Bearer", "shared-key");
            var journal = (await session.HttpClient.GetFromJsonAsync<LlmTestServerProviderHttpTrace[]>("/admin-api/requests"))!;
            await Assert.That(journal.All(entry => entry.SessionId == session.Session.Id)).IsTrue();
            await Assert.That(journal.Length).IsEqualTo(ReferenceEquals(session, first) ? 1 : 2);
            await Assert.That(JsonSerializer.Serialize(journal)).DoesNotContain(session.Session.AccessKey);
        }
        await Assert.That(http.DefaultRequestHeaders.Contains(LlmTestServerSessionHeaders.Id)).IsFalse();
        await root.ResetAsync();
        await Assert.That((await second.Control.GetAssertionsAsync()).Matched).IsEqualTo(2);
    }

    [Test]
    public async Task SessionCredentials_RejectUnknownMissingForeignAndDuplicateHeadersAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.RequireBearerToken("root-key"));
        using var http = host.GetTestClient();
        using var unauthorized = await http.PostAsJsonAsync("/admin-api/sessions", new { });
        await Assert.That(unauthorized.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        var root = new LlmTestServerClient(http, "root-key");
        await using var first = await root.CreateSessionAsync(config => config.RequireBearerToken("provider-key"));
        await using var second = await root.CreateSessionAsync();
        foreach (var pair in new[] { (first.Session.Id, "wrong"), ("missing", first.Session.AccessKey), (first.Session.Id, second.Session.AccessKey), (first.Session.Id, "") })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/admin-api/models");
            request.Headers.Authorization = new("Bearer", "root-key");
            request.Headers.Add(LlmTestServerSessionHeaders.Id, pair.Item1);
            if (pair.Item2.Length > 0) { request.Headers.Add(LlmTestServerSessionHeaders.AccessKey, pair.Item2); }
            using var response = await http.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        using var duplicate = new HttpRequestMessage(HttpMethod.Get, "/admin-api/models");
        duplicate.Headers.Add(LlmTestServerSessionHeaders.Id, new[] { first.Session.Id, second.Session.Id });
        duplicate.Headers.Add(LlmTestServerSessionHeaders.AccessKey, first.Session.AccessKey);
        using var duplicated = await http.SendAsync(duplicate);
        await Assert.That(duplicated.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var foreignDelete = await first.HttpClient.DeleteAsync("/admin-api/sessions/" + second.Session.Id);
        await Assert.That(foreignDelete.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await first.Control.GetAssertionsAsync()).TotalEvents).IsEqualTo(0);
        await Assert.That((await second.Control.GetAssertionsAsync()).TotalEvents).IsEqualTo(0);
        await first.Control.ConfigureAsync(config => config.RequireBearerToken("changed-provider-key").UseGpt41Mini());
        await Assert.That((await first.Control.WithBearerToken("changed-provider-key").GetAssertionsAsync()).TotalEvents).IsEqualTo(0);
    }

    [Test]
    public async Task SessionDeletion_ReleasesCountBudgetAndLeavesSharedTransportAliveAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(configureServices: services => services.Configure<LlmTestServerSessionOptions>(options => options.MaxSessions = 1));
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        var first = await root.CreateSessionAsync();
        using var full = await http.PostAsJsonAsync("/admin-api/sessions", new { });
        await Assert.That(full.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(await full.Content.ReadAsStringAsync()).Contains("session_capacity_exceeded");
        var id = first.Session.Id;
        var key = first.Session.AccessKey;
        await first.DisposeAsync();
        await first.DisposeAsync();
        using var stale = new HttpRequestMessage(HttpMethod.Get, "/admin-api/models");
        stale.Headers.Add(LlmTestServerSessionHeaders.Id, id);
        stale.Headers.Add(LlmTestServerSessionHeaders.AccessKey, key);
        using var removed = await http.SendAsync(stale);
        await Assert.That(removed.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await using var replacement = await root.CreateSessionAsync();
        await Assert.That(replacement.Session.Id).IsNotEqualTo(id);
        await Assert.That((await root.GetAssertionsAsync()).TotalEvents).IsEqualTo(0);
    }

    [Test]
    public async Task SessionBudget_RejectsAdmissionAndConfigureOverflowWithoutChangingStateAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(configureServices: services => services.Configure<LlmTestServerSessionOptions>(options => options.MaxReservedStateBytes = 60L * 1024 * 1024));
        using var http = host.GetTestClient();
        using var oversized = await http.PostAsJsonAsync("/admin-api/sessions", new { });
        await Assert.That(oversized.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var configuration = LlmTestServerConfiguration.CreateDefault() with { MaxVideoBytes = 0, MaxStoredChatResponseBytes = 0 };
        using var created = await http.PostAsJsonAsync("/admin-api/sessions", new LlmTestServerSessionCreateRequest { Configuration = configuration });
        created.EnsureSuccessStatusCode();
        var session = (await created.Content.ReadFromJsonAsync<LlmTestServerSession>())!;
        await Assert.That(created.Headers.CacheControl!.NoStore).IsTrue();
        http.DefaultRequestHeaders.Add(LlmTestServerSessionHeaders.Id, session.Id);
        http.DefaultRequestHeaders.Add(LlmTestServerSessionHeaders.AccessKey, session.AccessKey);
        using var tooLarge = await http.PostAsJsonAsync("/admin-api/configure", LlmTestServerConfiguration.CreateDefault());
        await Assert.That(tooLarge.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var invalid = await http.PostAsJsonAsync("/admin-api/configure", configuration with { MaxVideoBytes = -1 });
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var invalidOverBudget = await http.PostAsJsonAsync("/admin-api/configure", configuration with
        { Models = null!, MaxVideoBytes = 1024L * 1024 * 1024 });
        await Assert.That(invalidOverBudget.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var stillActive = await http.PostAsJsonAsync("/openai/v1/embeddings", new { model = "text-embedding-3-small", input = "active" });
        stillActive.EnsureSuccessStatusCode();
        using var deleted = await http.DeleteAsync("/admin-api/sessions/" + session.Id);
        await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }
}
