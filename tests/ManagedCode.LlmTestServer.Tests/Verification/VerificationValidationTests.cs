using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.LlmTestServer.Tests.Verification;

public sealed partial class VerificationIntegrationTests
{
    [Test]
    [Arguments("{}")]
    [Arguments("{\"scope\":null,\"noUnexpectedCalls\":true}")]
    [Arguments("{\"requests\":null,\"noUnexpectedCalls\":true}")]
    [Arguments("{\"conversations\":null,\"noUnexpectedCalls\":true}")]
    [Arguments("{\"retries\":null,\"noUnexpectedCalls\":true}")]
    [Arguments("{\"requests\":[null]}")]
    [Arguments("{\"requests\":[{\"name\":\"\"}]}")]
    [Arguments("{\"requests\":[{\"query\":null}]}")]
    [Arguments("{\"requests\":[{\"minimumCount\":-1}]}")]
    [Arguments("{\"requests\":[{\"minimumCount\":2,\"maximumCount\":1}]}")]
    [Arguments("{\"requests\":[{\"query\":{\"status\":99}}]}")]
    [Arguments("{\"requests\":[{\"query\":{\"offset\":-1}}]}")]
    [Arguments("{\"requests\":[{\"query\":{\"limit\":501}}]}")]
    [Arguments("{\"requests\":[{\"query\":{\"order\":99}}]}")]
    [Arguments("{\"requests\":[{\"tools\":null}]}")]
    [Arguments("{\"requests\":[{\"tools\":[null]}]}")]
    [Arguments("{\"requests\":[{\"tools\":[{\"kind\":99}]}]}")]
    [Arguments("{\"requests\":[{\"tools\":[{\"argumentsJson\":\"{\"}]}]}")]
    [Arguments("{\"requests\":[{\"tools\":[{\"kind\":2,\"resultJson\":\"{\"}]}]}")]
    [Arguments("{\"requests\":[{\"tools\":[{\"resultFields\":null}]}]}")]
    [Arguments("{\"requests\":[{\"tools\":[{\"kind\":2,\"resultFields\":[null]}]}]}")]
    [Arguments("{\"requests\":[{\"tools\":[{\"kind\":2,\"resultFields\":[{\"path\":\"$..invalid\",\"equalsJson\":\"null\"}]}]}]}")]
    [Arguments("{\"requests\":[{\"tools\":[{\"resultJson\":\"{}\"}]}]}")]
    [Arguments("{\"conversations\":[null]}")]
    [Arguments("{\"conversations\":[{}]}")]
    [Arguments("{\"conversations\":[{\"scenarioId\":\"graph\",\"visitedSteps\":null}]}")]
    [Arguments("{\"conversations\":[{\"scenarioId\":\"graph\",\"visitedSteps\":[\"\"]}]}")]
    [Arguments("{\"retries\":[null]}")]
    [Arguments("{\"retries\":[{}]}")]
    [Arguments("{\"retries\":[{\"groupId\":\"retry\",\"query\":null}]}")]
    [Arguments("{\"retries\":[{\"groupId\":\"retry\",\"minimumAttempts\":4,\"maximumAttempts\":3}]}")]
    [Arguments("{\"retries\":[{\"groupId\":\"retry\",\"statusSequence\":null}]}")]
    [Arguments("{\"retries\":[{\"groupId\":\"retry\",\"statusSequence\":[600]}]}")]
    [Arguments("{\"retries\":[{\"groupId\":\"retry\",\"minimumRetryAfterSeconds\":-1}]}")]
    public async Task Verify_InvalidExpectationsReturnBadRequestAndDoNotConsumeProviderFixturesAsync(string json)
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("preserved", scenario => scenario.Responds("still available")));
        using var http = host.GetTestClient();
        using var invalid = await http.PostAsJsonAsync("/admin-api/verify", JsonNode.Parse(json));
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var actual = await ChatAsync(http, "/openai/v1/chat/completions");
        actual.EnsureSuccessStatusCode();
        await Assert.That(await actual.Content.ReadAsStringAsync()).Contains("still available");
    }

    [Test]
    public async Task Verify_RequiresControlAuthorizationAndTypedClientSendsCredentialAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.RequireBearerToken("verify-key"));
        using var http = host.GetTestClient();
        using var unauthorized = await http.PostAsJsonAsync("/admin-api/verify", new { noUnexpectedCalls = true });
        await Assert.That(unauthorized.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "wrong-key");
        using var wrong = await http.PostAsJsonAsync("/admin-api/verify", new { noUnexpectedCalls = true });
        await Assert.That(wrong.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        var valid = await new LlmTestServerClient(http, "verify-key").VerifyAsync(new() { NoUnexpectedCalls = true });
        await Assert.That(valid.Passed).IsTrue();
    }

    [Test]
    [Arguments("empty")]
    [Arguments("duplicate")]
    [Arguments("oversized")]
    public async Task InvalidRetryGroup_DoesNotConsumeProviderFixtureAsync(string defect)
    {
        if (defect == "empty") { await VerifyEmptyHeaderOverTcpAsync(ManagedCode.LlmTestServer.Diagnostics.LlmTestServerRequestHeaders.RetryGroup); return; }
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("preserved", scenario => scenario.Responds("still available")));
        using var http = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/chat/completions")
        { Content = System.Net.Http.Json.JsonContent.Create(new { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "hello" } } }) };
        request.Headers.TryAddWithoutValidation(ManagedCode.LlmTestServer.Diagnostics.LlmTestServerRequestHeaders.RetryGroup,
            defect == "empty" ? new[] { "" } : defect == "duplicate" ? new[] { "one", "two" } : new[] { new string('x', 129) });
        using var invalid = await http.SendAsync(request);
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var evidence = await new LlmTestServerClient(http).GetRequestsAsync(new() { Status = 400 });
        await Assert.That(evidence.TotalMatched).IsEqualTo(1);
        var unaccounted = await new LlmTestServerClient(http).VerifyAsync(new() { NoUnexpectedCalls = true });
        await Assert.That(unaccounted.Passed).IsFalse();
        using var actual = await ChatAsync(http, "/openai/v1/chat/completions");
        actual.EnsureSuccessStatusCode();
        await Assert.That(await actual.Content.ReadAsStringAsync()).Contains("still available");
    }

    [Test]
    public async Task EmptyConversationHeader_DoesNotConsumeProviderFixtureAsync()
    {
        await VerifyEmptyHeaderOverTcpAsync(ManagedCode.LlmTestServer.Conversations.LlmTestServerConversationHeaders.Id);
    }

    private static async Task VerifyEmptyHeaderOverTcpAsync(string headerName)
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        builder.Services.AddLlmTestServer(config => config.AddChatScenario("preserved", scenario => scenario.Responds("still available")));
        await using var app = builder.Build();
        app.MapLlmTestServer();
        await app.StartAsync();
        try
        {
            var server = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
            var address = new Uri(server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.Single());
            using var http = new HttpClient { BaseAddress = address };
            var control = new LlmTestServerClient(http);
            // Complete lazy endpoint initialization before measuring the raw provider exchange.
            var initialEvidence = await control.GetRequestsAsync(new());
            await Assert.That(initialEvidence.TotalObserved).IsEqualTo(0);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var socket = new System.Net.Sockets.TcpClient();
            await socket.ConnectAsync(address.Host, address.Port, cancellation.Token);
            const string body = """{"model":"gpt-4.1-mini","messages":[{"role":"user","content":"hello"}]}""";
            var wire = $"POST /openai/v1/chat/completions HTTP/1.1\r\nHost: {address.Authority}\r\nContent-Type: application/json\r\nContent-Length: {System.Text.Encoding.UTF8.GetByteCount(body)}\r\n{headerName}: \r\nConnection: close\r\n\r\n{body}";
            await socket.GetStream().WriteAsync(System.Text.Encoding.UTF8.GetBytes(wire), cancellation.Token);
            using var reader = new StreamReader(socket.GetStream());
            var response = await reader.ReadToEndAsync(cancellation.Token);
            await Assert.That(response.StartsWith("HTTP/1.1 400", StringComparison.Ordinal)).IsTrue();
            var rejectedTrace = await new LlmTestServerClient(http).GetRequestsAsync(new() { Status = 400 });
            await Assert.That(rejectedTrace.TotalMatched).IsEqualTo(1);
            var unaccounted = await new LlmTestServerClient(http).VerifyAsync(new() { NoUnexpectedCalls = true });
            await Assert.That(unaccounted.Passed).IsFalse();
            using var actual = await ChatAsync(http, "/openai/v1/chat/completions");
            actual.EnsureSuccessStatusCode();
            await Assert.That(await actual.Content.ReadAsStringAsync()).Contains("still available");
        }
        finally
        {
            // Drain active requests before the application service provider is disposed.
            await app.StopAsync();
        }
    }

    [Test]
    public async Task ConversationScopedModelDiscovery_IsIncludedInUnexpectedCallVerificationAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        using var conversation = new LlmTestServerClient(http).ForConversation("discovery");
        using var response = await conversation.HttpClient.GetAsync("/openai/v1/models");
        response.EnsureSuccessStatusCode();
        var evidence = await conversation.Control.GetRequestsAsync(new() { Conversation = "discovery" });
        await Assert.That(evidence.TotalMatched).IsEqualTo(1);
        var unaccounted = await conversation.Control.VerifyAsync(new() { NoUnexpectedCalls = true });
        await Assert.That(unaccounted.Passed).IsFalse();
        var expected = await conversation.Control.VerifyAsync(new()
        { NoUnexpectedCalls = true, Requests = [new() { Name = "model discovery" }] });
        await Assert.That(expected.Passed).IsTrue();
    }
}
