using System.Net;
using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Sessions;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.LlmTestServer.Tests.Sessions;

public sealed class SessionBoundaryIntegrationTests
{
    [Test]
    public async Task InvalidSessionCreate_DoesNotReserveCountOrChangeDefaultRuntimeAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(configureServices: services => services.Configure<LlmTestServerSessionOptions>(options =>
        { options.MaxSessions = 1; options.MaxConfigurationBytes = 4096; }));
        using var http = host.GetTestClient();
        foreach (var configuration in new[]
        {
            LlmTestServerConfiguration.CreateDefault() with { DefaultTranslationText = new string('x', 8192) },
            LlmTestServerConfiguration.CreateDefault() with { MaxStoredChatResponseBytes = long.MaxValue },
            LlmTestServerConfiguration.CreateDefault() with { MaxPromptCacheEntries = -1 },
        })
        {
            using var invalid = await http.PostAsJsonAsync("/admin-api/sessions", new LlmTestServerSessionCreateRequest { Configuration = configuration });
            await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        var invalidFixture = new
        {
            chatScenarios = new[] { new { id = "invalid", responses = new[]
                { new { toolCalls = new[] { new { id = "call", name = "weather", argumentsJson = "{" } } } } } }
        };
        using var badFixture = await http.PostAsJsonAsync("/admin-api/sessions", new { configuration = invalidFixture });
        await Assert.That(badFixture.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await using var session = await new LlmTestServerClient(http).CreateSessionAsync();
        using var badConfigure = await session.HttpClient.PostAsJsonAsync("/admin-api/configure", invalidFixture);
        await Assert.That(badConfigure.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var nested = await session.HttpClient.PostAsJsonAsync("/admin-api/sessions", new { });
        await Assert.That(nested.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await session.Control.GetAssertionsAsync()).TotalEvents).IsEqualTo(0);
    }

    [Test]
    public async Task ShrinkingSessionCapacity_AllowsAnotherSessionWithinAggregateBudgetAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(configureServices: services => services.Configure<LlmTestServerSessionOptions>(options => options.MaxReservedStateBytes = 180L * 1024 * 1024));
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var first = await root.CreateSessionAsync();
        using var full = await http.PostAsJsonAsync("/admin-api/sessions", new { });
        await Assert.That(full.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await first.Control.ConfigureAsync(LlmTestServerConfiguration.CreateDefault() with { MaxVideoBytes = 0, MaxStoredChatResponseBytes = 0 });
        await using var second = await root.CreateSessionAsync();
        await Assert.That((await second.Control.GetAssertionsAsync()).TotalEvents).IsEqualTo(0);
    }

    [Test]
    public async Task InvalidSessionHeaders_DoNotExecuteOrJournalDefaultProviderRequestAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("default", scenario => scenario.Responds("default preserved")));
        using var http = host.GetTestClient();
        using var invalid = new HttpRequestMessage(HttpMethod.Get, "/openai/v1/models");
        invalid.Headers.Add(LlmTestServerSessionHeaders.AccessKey, "key-without-session-id");
        using var rejected = await http.SendAsync(invalid);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await rejected.Content.ReadAsStringAsync()).Contains("session_not_found");
        await Assert.That(await http.GetStringAsync("/admin-api/requests")).IsEqualTo("[]");
        using var chat = new LlmTestServerClient(http).CreateChatClient();
        await Assert.That((await chat.GetResponseAsync("hello")).Text).IsEqualTo("default preserved");
    }

    [Test]
    public async Task DefaultConfigure_DoesNotChangeSessionStartupTemplateOrExistingQueuesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("startup", scenario => scenario.Responds("startup response")));
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var first = await root.CreateSessionAsync();
        await root.ConfigureAsync(config => config.UseChatScenario("changed", scenario => scenario.Responds("default changed")));
        await using var second = await root.CreateSessionAsync();
        using var a = first.Control.CreateChatClient();
        using var b = second.Control.CreateChatClient();
        using var defaultChat = root.CreateChatClient();
        await Assert.That((await a.GetResponseAsync("hello")).Text).IsEqualTo("startup response");
        await Assert.That((await b.GetResponseAsync("hello")).Text).IsEqualTo("startup response");
        await Assert.That((await defaultChat.GetResponseAsync("hello")).Text).IsEqualTo("default changed");
    }
}
