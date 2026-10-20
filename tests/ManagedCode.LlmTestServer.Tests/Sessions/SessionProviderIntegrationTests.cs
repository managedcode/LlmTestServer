using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Tests.Decisions;
using ManagedCode.LlmTestServer.Tests.Providers;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Chat;

namespace ManagedCode.LlmTestServer.Tests.Sessions;

public sealed class SessionProviderIntegrationTests
{
    [Test]
    [Arguments("jev-latest", LlmTestServerDecisionProvider.TypeSafe)]
    [Arguments("kev-latest", LlmTestServerDecisionProvider.Kev)]
    [Arguments("clef", LlmTestServerDecisionProvider.Cloudflare)]
    [Arguments("clef-flash", LlmTestServerDecisionProvider.Cloudflare)]
    [Arguments("gpt-6-luna", LlmTestServerDecisionProvider.OpenAI)]
    public async Task NativeDecisionProviders_KeepSessionFixturesAndErrorsSeparateAsync(string model, LlmTestServerDecisionProvider provider)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var first = await root.CreateSessionAsync(config => config.UseDecisionModel(model, provider)
            .UseDecisionScenario(DecisionEndpointTests.Scenario(model)));
        await using var second = await root.CreateSessionAsync(config => config.UseDecisionModel(model, provider)
            .UseDecisionScenario(DecisionEndpointTests.Scenario(model) with { Error = new() { StatusCode = 503, Code = "capacity", Message = "session capacity fixture" } }));
        using var success = await first.HttpClient.PostAsJsonAsync(DecisionEndpointTests.PathFor(model), DecisionEndpointTests.Body(model));
        success.EnsureSuccessStatusCode();
        using var failure = await second.HttpClient.PostAsJsonAsync(DecisionEndpointTests.PathFor(model), DecisionEndpointTests.Body(model));
        await Assert.That(failure.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That((await first.Control.GetAssertionsAsync()).Matched).IsEqualTo(1);
        await Assert.That((await second.Control.GetAssertionsAsync()).ErrorsReturned).IsEqualTo(1);
        await Assert.That((await root.GetAssertionsAsync()).TotalEvents).IsEqualTo(0);
    }

    [Test]
    public async Task EveryChatProvider_UsesItsSelectedSessionFaultBudgetAndQueueAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var limited = await root.CreateSessionAsync(config => config.SimulateRateLimitAfter(0));
        await using var active = await root.CreateSessionAsync(config => config.UseChatScenario("providers", scenario =>
        {
            foreach (var provider in ProviderFaultSimulationTests.GetProviderFaultCases()) { scenario.Responds("independent response"); }
        }));
        foreach (var provider in ProviderFaultSimulationTests.GetProviderFaultCases())
        {
            using var rejected = await provider.SendAsync(limited.HttpClient, "same prompt");
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            using var accepted = await provider.SendAsync(active.HttpClient, "same prompt");
            accepted.EnsureSuccessStatusCode();
            await Assert.That(await accepted.Content.ReadAsStringAsync()).Contains("independent response");
        }
        await Assert.That((await limited.Control.GetAssertionsAsync()).ErrorsReturned).IsEqualTo(13);
        await Assert.That((await active.Control.GetAssertionsAsync()).Matched).IsEqualTo(13);
        await Assert.That((await root.GetAssertionsAsync()).TotalEvents).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OfficialOpenAiSdk_UsesSessionTransportAndIndependentProviderCredentialAsync(bool stream)
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.RequireBearerToken("management-key"));
        using var http = host.GetTestClient();
        await using var session = await new LlmTestServerClient(http, "management-key").CreateSessionAsync(config => config
            .RequireBearerToken("provider-key").UseChatScenario("sdk", scenario => scenario.Responds("SDK session reply", "SDK ", "session reply")));
        var sdk = new ChatClient("gpt-4.1-mini", new ApiKeyCredential("provider-key"), new OpenAIClientOptions
        {
            Endpoint = new Uri(session.HttpClient.BaseAddress!, "openai/v1"),
            Transport = new HttpClientPipelineTransport(session.HttpClient),
        });
        var result = string.Empty;
        if (stream)
        {
            await foreach (var chunk in sdk.CompleteChatStreamingAsync([new UserChatMessage("same prompt")]))
            { result += string.Concat(chunk.ContentUpdate.Select(part => part.Text)); }
        }
        else
        { result = (await sdk.CompleteChatAsync([new UserChatMessage("same prompt")])).Value.Content[0].Text; }
        await Assert.That(result).IsEqualTo("SDK session reply");
        await Assert.That((await session.Control.GetAssertionsAsync()).Matched).IsEqualTo(1);
    }

    [Test]
    public async Task PromptCaches_AreIndependentAcrossSessionsAndLocalResetAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var first = await root.CreateSessionAsync(config => config.UseChatScenario("cache", scenario => scenario.Responds("reply").Responds("reply").Responds("reply")));
        await using var second = await root.CreateSessionAsync(config => config.UseChatScenario("cache", scenario => scenario.Responds("reply").Responds("reply")));
        var prompt = string.Concat(Enumerable.Repeat("A stable sequence of words for provider prompt cache. ", 250));
        async Task<int> CachedAsync(LlmTestServerSessionClient session)
        {
            using var response = await session.HttpClient.PostAsJsonAsync("/openai/v1/chat/completions", new { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = prompt } } });
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("usage").GetProperty("prompt_tokens_details").GetProperty("cached_tokens").GetInt32();
        }
        await Assert.That(await CachedAsync(first)).IsEqualTo(0);
        await Assert.That(await CachedAsync(first)).IsGreaterThan(0);
        await Assert.That(await CachedAsync(second)).IsEqualTo(0);
        await first.Control.ResetAsync();
        await Assert.That(await CachedAsync(first)).IsEqualTo(0);
        await Assert.That(await CachedAsync(second)).IsGreaterThan(0);
    }

    [Test]
    public async Task RequestJournalRetention_IsBoundedIndependentlyInEachSessionAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(configureServices: services => services.Configure<LlmTestServerProviderHttpTraceOptions>(options => options.MaxEntries = 1));
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var first = await root.CreateSessionAsync();
        await using var second = await root.CreateSessionAsync();
        foreach (var session in new[] { first, second })
        {
            for (var index = 0; index < 2; index++)
            {
                using var response = await session.HttpClient.PostAsJsonAsync("/openai/v1/embeddings", new { model = "text-embedding-3-small", input = "bounded" });
                response.EnsureSuccessStatusCode();
            }
            var journal = (await session.HttpClient.GetFromJsonAsync<LlmTestServerProviderHttpTrace[]>("/admin-api/requests"))!;
            await Assert.That(journal.Length).IsEqualTo(1);
            await Assert.That(journal[0].SessionId).IsEqualTo(session.Session.Id);
            await Assert.That((await session.Control.GetAssertionsAsync()).Matched).IsEqualTo(2);
        }
    }
}
