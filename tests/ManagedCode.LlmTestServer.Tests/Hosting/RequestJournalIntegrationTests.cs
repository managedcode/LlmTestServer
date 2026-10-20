using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.LlmTestServer.Tests.Hosting;

public sealed class RequestJournalIntegrationTests
{
    [Test]
    public async Task Journal_FiltersAndPagesActualProviderRequestsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options.AddChatScenario("requests",
            scenario => scenario.Responds("first").Responds("second").Fails(429, "too_many_requests", "retry later")));
        using var http = host.GetTestClient();
        foreach (var path in new[] { "/openai/v1/chat/completions", "/mistral/v1/chat/completions", "/openai/v1/chat/completions" })
        {
            using var response = await http.PostAsJsonAsync(path, new
            {
                model = "gpt-4.1-mini",
                messages = new[] { new { role = "user", content = "journal" } },
            });
        }
        using var page = await http.GetAsync("/admin-api/requests?provider=openai&model=gpt-4.1-mini&offset=0&limit=1");
        page.EnsureSuccessStatusCode();
        await Assert.That(page.Headers.GetValues("X-Total-Count").Single()).IsEqualTo("2");
        var entries = (await page.Content.ReadFromJsonAsync<List<LlmTestServerProviderHttpTrace>>())!;
        await Assert.That(entries.Count).IsEqualTo(1);
        var entry = entries.Single();
        await Assert.That(entry.StatusCode).IsEqualTo(429);
        await Assert.That(entry.ProviderId).IsEqualTo(LlmTestServerCompatibilityTags.OpenAI);
        await Assert.That(entry.ErrorCode).IsEqualTo("too_many_requests");

        var matching = (await http.GetFromJsonAsync<List<LlmTestServerProviderHttpTrace>>(
            $"/admin-api/requests?requestId={entry.RequestId}&status=429"))!;
        await Assert.That(matching.Single().RequestId).IsEqualTo(entry.RequestId);
        var next = (await http.GetFromJsonAsync<List<LlmTestServerProviderHttpTrace>>(
            "/admin-api/requests?provider=openai&status=200&offset=0&limit=1"))!;
        await Assert.That(next.Single().RuntimeResponse).IsEqualTo("first");
        var empty = (await http.GetFromJsonAsync<List<LlmTestServerProviderHttpTrace>>(
            "/admin-api/requests?model=unknown&offset=10&limit=1"))!;
        await Assert.That(empty).IsEmpty();
        using var reset = await http.PostAsync("/admin-api/reset", null);
        reset.EnsureSuccessStatusCode();
        await Assert.That(await http.GetFromJsonAsync<List<LlmTestServerProviderHttpTrace>>("/admin-api/requests")).IsEmpty();
    }

    [Test]
    [Arguments("offset=-1")]
    [Arguments("limit=0")]
    [Arguments("limit=501")]
    [Arguments("status=99")]
    [Arguments("status=600")]
    [Arguments("offset=invalid")]
    public async Task Journal_RejectsInvalidQueryParametersAsync(string query)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        using var response = await http.GetAsync("/admin-api/requests?" + query);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Journal_RequiresTheConfiguredControlCredentialAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options.RequireBearerToken("journal-key"));
        using var http = host.GetTestClient();
        using var rejected = await http.GetAsync("/admin-api/requests");
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "journal-key");
        using var accepted = await http.GetAsync("/admin-api/requests");
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Journal_BoundsRetentionWithoutTruncatingActualProviderResponsesAsync()
    {
        var text = new string('x', 16_000);
        using var host = await LlmTestServerTestHost.StartAsync(options => options
            .AddChatScenario("large", scenario => scenario.Responds(text).Responds(text).Responds(text)),
            configureServices: services => services.Configure<LlmTestServerProviderHttpTraceOptions>(options =>
            {
                options.MaxEntries = 2;
                options.MaxPreviewBytes = 128;
                options.MaxRetainedBytes = 8192;
            }));
        using var http = host.GetTestClient();
        for (var index = 0; index < 3; index++)
        {
            using var response = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
            {
                model = "gpt-4.1-mini",
                messages = new[] { new { role = "user", content = text } },
            });
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            await Assert.That(body.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString())
                .IsEqualTo(text);
        }
        var entries = (await http.GetFromJsonAsync<List<LlmTestServerProviderHttpTrace>>("/admin-api/requests"))!;
        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries.Sum(entry => entry.RetainedByteCount)).IsLessThanOrEqualTo(8192);
        await Assert.That(entries.All(entry => entry.RequestPreviewTruncated && entry.ResponsePreviewTruncated)).IsTrue();
        await Assert.That(entries.All(entry => entry.RetentionTruncated)).IsTrue();
    }
}
