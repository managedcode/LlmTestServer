using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Sessions;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.AI;

namespace ManagedCode.LlmTestServer.Tests.Sessions;

public sealed class SessionStateIntegrationTests
{
    [Test]
    public async Task CancellingOneSessionRequest_DoesNotConsumeItsQueueOrStopAnotherSessionAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var first = await root.CreateSessionAsync(config => config.UseChatScenario("cancel", scenario => scenario.Responds("A preserved").DelaysBy(500)));
        await using var second = await root.CreateSessionAsync(config => config.UseChatScenario("active", scenario => scenario.Responds("B active")));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var cancelled = false;
        try
        {
            using var response = await first.HttpClient.PostAsJsonAsync("/openai/v1/chat/completions", new
            { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "hello" } } }, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelled = true; }
        await Assert.That(cancelled).IsTrue();
        using var b = second.Control.CreateChatClient();
        await Assert.That((await b.GetResponseAsync("hello")).Text).IsEqualTo("B active");
        using var a = first.Control.CreateChatClient();
        await Assert.That((await a.GetResponseAsync("hello")).Text).IsEqualTo("A preserved");
        await Assert.That((await first.Control.GetAssertionsAsync()).Matched).IsEqualTo(1);
        await Assert.That((await second.Control.GetAssertionsAsync()).Matched).IsEqualTo(1);
    }

    [Test]
    public async Task SessionClients_StreamIndependentResponsesAndKeepDefaultHostUnchangedAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var first = await root.CreateSessionAsync(config => config.UseChatScenario("stream", scenario => scenario.Responds("alpha", "al", "pha")));
        await using var second = await root.CreateSessionAsync(config => config.UseChatScenario("stream", scenario => scenario.Responds("beta", "be", "ta")));
        async Task<string> ReadAsync(LlmTestServerSessionClient session)
        {
            using var chat = session.Control.CreateChatClient();
            var text = string.Empty;
            await foreach (var chunk in chat.GetStreamingResponseAsync("identical")) { text += chunk.Text; }
            return text;
        }
        var replies = await Task.WhenAll(ReadAsync(first), ReadAsync(second));
        await Assert.That(replies).IsEquivalentTo(new[] { "alpha", "beta" });
        await Assert.That((await first.Control.GetAssertionsAsync()).Matched).IsEqualTo(1);
        await Assert.That((await second.Control.GetAssertionsAsync()).Matched).IsEqualTo(1);
        await Assert.That((await root.GetAssertionsAsync()).Matched).IsEqualTo(0);
    }

    [Test]
    public async Task Sessions_IsolateStoredResponsesEvenWithIdenticalProviderCredentialsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var first = await root.CreateSessionAsync(config => config.UseChatScenario("history", scenario => scenario.Responds("first").Responds("continued")));
        await using var second = await root.CreateSessionAsync(config => config.UseChatScenario("history", scenario => scenario.Responds("second")));
        using var created = await first.HttpClient.PostAsJsonAsync("/openai/v1/responses", new { model = "gpt-4.1-mini", input = "hello" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        using var foreign = await second.HttpClient.PostAsJsonAsync("/openai/v1/responses", new { model = "gpt-4.1-mini", input = "continue", previous_response_id = id });
        await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await foreign.Content.ReadAsStringAsync()).Contains("response_not_found");
        using var continued = await first.HttpClient.PostAsJsonAsync("/openai/v1/responses", new { model = "gpt-4.1-mini", input = "continue", previous_response_id = id });
        continued.EnsureSuccessStatusCode();
        await Assert.That(await continued.Content.ReadAsStringAsync()).Contains("continued");
        using var untouched = await second.HttpClient.PostAsJsonAsync("/openai/v1/responses", new { model = "gpt-4.1-mini", input = "hello" });
        untouched.EnsureSuccessStatusCode();
        await Assert.That(await untouched.Content.ReadAsStringAsync()).Contains("second");
    }

    [Test]
    public async Task Sessions_IsolateVideoModelsFixturesAndFaultBudgetsAcrossConfigureAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var first = await root.CreateSessionAsync(config => config.UseVideo([1, 2, 3], "video/mp4").UseEmbeddingVector(1, 2)
            .UseTranscriptionText("A transcript").UseTranslationText("A translation"));
        await using var second = await root.CreateSessionAsync(config => config.UseVideo([4, 5, 6], "video/mp4"));
        await Assert.That(await first.Control.TranscribeAudioAsync("gpt-4o-mini-tts", [82, 73, 70, 70], "fixture.wav")).IsEqualTo("A transcript");
        await Assert.That(await second.Control.TranscribeAudioAsync("gpt-4o-mini-tts", [82, 73, 70, 70], "fixture.wav")).IsEqualTo("transcribed audio fixture");
        await Assert.That(await first.Control.TranslateAudioAsync("gpt-4o-mini-tts", [82, 73, 70, 70], "fixture.wav")).IsEqualTo("A translation");
        using var embeddings = first.Control.CreateEmbeddingGenerator();
        var vectors = await embeddings.GenerateAsync(["same input"]);
        await Assert.That(vectors[0].Vector.ToArray()).IsEquivalentTo(new float[] { 1, 2 });
        using var created = await first.HttpClient.PostAsJsonAsync("/openai/v1/videos", new { model = "sora-2", prompt = "A video" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        using var absent = await second.HttpClient.GetAsync("/openai/v1/videos/" + id);
        await Assert.That(absent.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var another = await second.HttpClient.PostAsJsonAsync("/openai/v1/videos", new { model = "sora-2", prompt = "B video" });
        another.EnsureSuccessStatusCode();
        var otherId = (await another.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        using var contentA = await first.HttpClient.GetAsync("/openai/v1/videos/" + id + "/content");
        using var contentB = await second.HttpClient.GetAsync("/openai/v1/videos/" + otherId + "/content");
        await Assert.That(await contentA.Content.ReadAsByteArrayAsync()).IsEquivalentTo((byte[])[1, 2, 3]);
        await Assert.That(await contentB.Content.ReadAsByteArrayAsync()).IsEquivalentTo((byte[])[4, 5, 6]);
        await first.Control.ConfigureAsync(config => config.UseChatModel("session-only-model").SimulateRateLimitAfter(0));
        using var throttled = await first.HttpClient.PostAsJsonAsync("/openai/v1/embeddings", new { model = "text-embedding-3-small", input = "hello" });
        await Assert.That(throttled.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        using var available = await second.HttpClient.PostAsJsonAsync("/openai/v1/embeddings", new { model = "text-embedding-3-small", input = "hello" });
        available.EnsureSuccessStatusCode();
        using var cleared = await first.HttpClient.GetAsync("/openai/v1/videos/" + id);
        await Assert.That(cleared.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var retained = await second.HttpClient.GetAsync("/openai/v1/videos/" + otherId);
        retained.EnsureSuccessStatusCode();
        var modelsA = await first.HttpClient.GetStringAsync("/admin-api/models");
        var modelsB = await second.HttpClient.GetStringAsync("/admin-api/models");
        await Assert.That(modelsA).Contains("session-only-model");
        await Assert.That(modelsB).DoesNotContain("session-only-model");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task SessionMutation_InvalidatesInFlightDelayedResponseWithoutAffectingOtherSessionsAsync(int mutation)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var session = await root.CreateSessionAsync(config => config.UseChatScenario("delayed", scenario => scenario.Responds("retired response").DelaysBy(1000)));
        var pending = session.HttpClient.PostAsJsonAsync("/openai/v1/chat/completions", new { model = "gpt-4.1-mini", messages = new[] { new { role = "user", content = "hello" } } });
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var entries = (await session.HttpClient.GetFromJsonAsync<LlmTestServerProviderHttpTrace[]>("/admin-api/requests"))!;
            if (entries.Any(entry => !entry.IsCompleted)) { break; }
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(5)) { throw new TimeoutException("The server did not observe the delayed request."); }
            await Task.Delay(10);
        }
        if (mutation == 0)
        {
            using var deletion = new HttpRequestMessage(HttpMethod.Delete, "/admin-api/sessions/" + session.Session.Id);
            deletion.Headers.Add(LlmTestServerSessionHeaders.Id, session.Session.Id);
            deletion.Headers.Add(LlmTestServerSessionHeaders.AccessKey, session.Session.AccessKey);
            using var deleted = await http.SendAsync(deletion);
            await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        }
        else if (mutation == 1)
        {
            await session.Control.ConfigureAsync(config => config.UseChatScenario("replacement", scenario => scenario.Responds("reconfigured")));
        }
        else { await session.Control.ResetAsync(); }
        using var superseded = await pending;
        await Assert.That(superseded.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(await superseded.Content.ReadAsStringAsync()).Contains("llm_test_server_request_superseded");
        if (mutation != 0)
        {
            await Assert.That((await session.Control.GetAssertionsAsync()).Matched).IsEqualTo(0);
            using var chat = session.Control.CreateChatClient();
            await Assert.That((await chat.GetResponseAsync("hello")).Text).IsEqualTo(mutation == 1 ? "reconfigured" : "retired response");
        }
        await using var replacement = await root.CreateSessionAsync();
        await Assert.That((await replacement.Control.GetAssertionsAsync()).TotalEvents).IsEqualTo(0);
    }
}
