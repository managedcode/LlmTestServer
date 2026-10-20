using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Sessions;
using ManagedCode.LlmTestServer.Tests.Providers;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Conversations;

public sealed class ConversationProviderIntegrationTests
{
    [Test]
    public async Task EveryChatProvider_MaintainsItsOwnGraphWithinOneConversationAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddConversation("provider-isolation", graph => graph
            .ReplaySuccessfulRequests(false).Step("first", step => step.WhenLatestUserEquals("same input").Responds("provider start").Complete())));
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        using var conversation = root.ForConversation("shared-id");
        foreach (var provider in ProviderFaultSimulationTests.GetProviderFaultCases())
        {
            using var first = await provider.SendAsync(conversation.HttpClient, "same input");
            first.EnsureSuccessStatusCode();
            await Assert.That(await first.Content.ReadAsStringAsync()).Contains("provider start");
            using var exhausted = await provider.SendAsync(conversation.HttpClient, "same input");
            await Assert.That(exhausted.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        }
        var states = await conversation.Control.GetConversationsAsync();
        await Assert.That(states.Count).IsEqualTo(13);
        await Assert.That(states.Select(state => state.ProviderNamespace).Distinct().Count()).IsEqualTo(13);
        await Assert.That(states.All(state => state.IsTerminal && state.ExecutedSteps == 1)).IsTrue();
        using var independent = root.ForConversation("another-id");
        using var fresh = await ProviderFaultSimulationTests.GetProviderFaultCases()[0].SendAsync(independent.HttpClient, "same input");
        fresh.EnsureSuccessStatusCode();
        await Assert.That((await independent.Control.GetConversationsAsync()).Single().ConversationId).IsEqualTo("another-id");
    }

    [Test]
    public async Task IdenticalConversationIds_AreIsolatedByProviderCredentialAndSessionAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var first = await root.CreateSessionAsync(config => config.UseConversation("isolated", graph => graph
            .ReplaySuccessfulRequests(false).Step("one", step => step.Responds("first session").Complete())));
        await using var second = await root.CreateSessionAsync(config => config.UseConversation("isolated", graph => graph
            .ReplaySuccessfulRequests(false).Step("one", step => step.Responds("second session").Complete())));
        using var firstConversation = first.Control.ForConversation("same-id");
        using var secondConversation = second.Control.ForConversation("same-id");
        foreach (var credential in new[] { "alpha-key", "beta-key" })
        {
            using var accepted = await SendAsync(firstConversation.HttpClient, credential);
            accepted.EnsureSuccessStatusCode();
            await Assert.That(await accepted.Content.ReadAsStringAsync()).Contains("first session");
            using var exhausted = await SendAsync(firstConversation.HttpClient, credential);
            await Assert.That(exhausted.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        }
        using var anotherSession = await SendAsync(secondConversation.HttpClient, "alpha-key");
        anotherSession.EnsureSuccessStatusCode();
        await Assert.That(await anotherSession.Content.ReadAsStringAsync()).Contains("second session");
        await Assert.That((await first.Control.GetConversationsAsync()).Count).IsEqualTo(2);
        await Assert.That((await second.Control.GetConversationsAsync()).Count).IsEqualTo(1);
        await Assert.That((await root.GetConversationsAsync()).Count).IsEqualTo(0);
        firstConversation.Dispose();
        using var retained = first.Control.ForConversation("same-id");
        using var stillExhausted = await SendAsync(retained.HttpClient, "alpha-key");
        await Assert.That(stillExhausted.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await first.Control.ResetAsync();
        using var reset = await SendAsync(retained.HttpClient, "alpha-key");
        reset.EnsureSuccessStatusCode();
        using var secondStillExhausted = await SendAsync(secondConversation.HttpClient, "alpha-key");
        await Assert.That(secondStillExhausted.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task CancelledStep_RollsBackBusyReservationAndPreservesOtherConversationsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddConversation("cancel", graph => graph
            .ReplaySuccessfulRequests(false).Step("delayed", step => step.Responds("preserved").DelaysBy(500).Complete())));
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        using var first = root.ForConversation("cancelled");
        using var other = root.ForConversation("active");
        using var cancellation = new CancellationTokenSource();
        var pending = SendAsync(first.HttpClient, cancellationToken: cancellation.Token);
        await WaitUntilBusyAsync(first.Control);
        using var concurrent = await SendAsync(first.HttpClient);
        await Assert.That(concurrent.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(await concurrent.Content.ReadAsStringAsync()).Contains("conversation_busy");
        cancellation.Cancel();
        var cancelled = false;
        try { using var response = await pending; }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelled = true; }
        await Assert.That(cancelled).IsTrue();
        var replies = await Task.WhenAll(SendAsync(first.HttpClient), SendAsync(other.HttpClient));
        foreach (var response in replies)
        {
            using (response)
            {
                response.EnsureSuccessStatusCode();
                await Assert.That(await response.Content.ReadAsStringAsync()).Contains("preserved");
            }
        }
        var states = await root.GetConversationsAsync();
        await Assert.That(states.Count).IsEqualTo(2);
        await Assert.That(states.All(state => state.ExecutedSteps == 1 && !state.IsBusy)).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task SessionMutation_InvalidatesPendingGraphAndClearsItsStateAsync(int mutation)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var session = await root.CreateSessionAsync(config => config.UseConversation("pending", graph => graph
            .Step("one", step => step.Responds("old graph").DelaysBy(500).Complete())));
        using var conversation = session.Control.ForConversation("pending-id");
        var pending = SendAsync(conversation.HttpClient);
        await WaitUntilBusyAsync(conversation.Control);
        if (mutation == 0) { await session.Control.ResetAsync(); }
        else if (mutation == 1)
        {
            await session.Control.ConfigureAsync(config => config.UseConversation("replacement", graph => graph
                .Step("one", step => step.Responds("new graph").Complete())));
        }
        else
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, "/admin-api/sessions/" + session.Session.Id);
            request.Headers.Add(LlmTestServerSessionHeaders.Id, session.Session.Id);
            request.Headers.Add(LlmTestServerSessionHeaders.AccessKey, session.Session.AccessKey);
            using var deleted = await http.SendAsync(request);
            await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        }
        using var superseded = await pending;
        await Assert.That(superseded.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(await superseded.Content.ReadAsStringAsync()).Contains("llm_test_server_request_superseded");
        if (mutation != 2)
        {
            await Assert.That((await conversation.Control.GetConversationsAsync()).Count).IsEqualTo(0);
            using var next = await SendAsync(conversation.HttpClient);
            next.EnsureSuccessStatusCode();
            await Assert.That(await next.Content.ReadAsStringAsync()).Contains(mutation == 1 ? "new graph" : "old graph");
            await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        }
        else
        {
            using var absent = await SendAsync(conversation.HttpClient);
            await Assert.That(absent.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        await Assert.That((await root.GetConversationsAsync()).Count).IsEqualTo(0);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, string? credential = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/chat/completions")
        {
            Content = JsonContent.Create(new { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "same input" } } }),
        };
        if (credential is not null) { request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential); }
        return await http.SendAsync(request, cancellationToken);
    }

    private static async Task WaitUntilBusyAsync(LlmTestServerClient control)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!(await control.GetConversationsAsync()).Any(state => state.IsBusy))
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(5)) { throw new TimeoutException("The server did not reserve the conversation step."); }
            await Task.Delay(10);
        }
    }
}
