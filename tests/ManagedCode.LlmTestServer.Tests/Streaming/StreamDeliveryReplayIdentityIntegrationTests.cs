using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Google.GenAI;
using Google.GenAI.Types;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Streaming;

public sealed class StreamDeliveryReplayIdentityIntegrationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GeminiGraphReplay_SeparatesInferenceIdentityFromCurrentDeliveryApplicabilityAsync(bool deliveryFault)
    {
        const string model = "gemini-3.8-flash";
        const string root = "/gemini/v1beta/models/" + model;
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddModel(model, LlmTestServerModelKind.Chat)
            .AddConversation("gemini-replay-identity", graph => graph.ForModel(model).Step("answer", step =>
            {
                step.Responds("first second", "first ", "second").Complete();
                if (deliveryFault) { step.WithStreamDelivery(new() { Mode = LlmTestServerStreamDeliveryMode.EarlyCleanEnd }); }
            })));
        using var http = host.GetTestClient(); var control = new LlmTestServerClient(http);
        using var scoped = control.ForConversation("gemini-replay-identity");
        var body = new { contents = new[] { new { role = "user", parts = new[] { new { text = "same inference" } } } } };
        using var sdk = new Google.GenAI.Client(apiKey: "gemini-replay-key", enterprise: false,
            httpOptions: new() { BaseUrl = new Uri(scoped.HttpClient.BaseAddress!, "/gemini").ToString(), ApiVersion = "v1beta", RetryOptions = new() { Attempts = 1 } },
            clientOptions: new() { HttpClientFactory = () => scoped.HttpClient });
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        async Task<List<GenerateContentResponse>> StreamAsync()
        {
            var frames = new List<GenerateContentResponse>();
            await foreach (var frame in sdk.Models.GenerateContentStreamAsync(model, "same inference", new GenerateContentConfig { AutomaticContinuation = false }, cancellationToken: watchdog.Token))
            { frames.Add(frame); }
            return frames;
        }
        string? identity;
        if (deliveryFault)
        {
            var first = (await StreamAsync()).Single();
            identity = first.ResponseId;
            await Assert.That(first.Text).IsEqualTo("first ");
            ClientError? rejected = null;
            try { await sdk.Models.GenerateContentAsync(model, "same inference", new GenerateContentConfig { AutomaticContinuation = false }, cancellationToken: watchdog.Token); }
            catch (ClientError exception) { rejected = exception; }
            await Assert.That(rejected).IsNotNull();
            await Assert.That(rejected!.StatusCode).IsEqualTo(409);
            await Assert.That(rejected.Message).Contains("text SSE");
        }
        else
        {
            var initial = await sdk.Models.GenerateContentAsync(model, "same inference", new GenerateContentConfig { AutomaticContinuation = false }, cancellationToken: watchdog.Token);
            identity = initial.ResponseId;
            await Assert.That(initial.Text).IsEqualTo("first second");
        }
        await Assert.That(identity).IsNotNullOrEmpty();
        using var arrayRequest = new HttpRequestMessage(HttpMethod.Post, root + ":streamGenerateContent") { Content = JsonContent.Create(body) };
        // Match the official SDK credential so this wire-format check addresses the same graph owner.
        arrayRequest.Headers.Add("x-goog-api-key", "gemini-replay-key");
        using var array = await scoped.HttpClient.SendAsync(arrayRequest);
        await Assert.That(array.StatusCode).IsEqualTo(deliveryFault ? HttpStatusCode.Conflict : HttpStatusCode.OK);
        if (deliveryFault)
        {
            await Assert.That((await array.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("message").GetString()!).Contains("text SSE");
        }
        else
        {
            var frames = await array.Content.ReadFromJsonAsync<JsonElement>();
            await Assert.That(frames[0].GetProperty("responseId").GetString()).IsEqualTo(identity);
            await Assert.That(frames[frames.GetArrayLength() - 1].GetProperty("candidates")[0].GetProperty("finishReason").GetString()).IsEqualTo("STOP");
        }
        var events = await StreamAsync();
        await Assert.That(events.All(item => item.ResponseId == identity)).IsTrue();
        await Assert.That(string.Concat(events.Select(item => item.Text))).IsEqualTo(deliveryFault ? "first " : "first second");
        await Assert.That(events.Count).IsEqualTo(deliveryFault ? 1 : 3);
        await Assert.That(events.Any(item => item.Candidates?.Any(candidate => candidate.FinishReason is not null) == true)).IsEqualTo(!deliveryFault);
        var states = await scoped.Control.GetConversationsAsync();
        await Assert.That(states.Single().ExecutedSteps).IsEqualTo(1);
        var latest = (await scoped.Control.GetRequestsAsync()).Requests[0];
        await Assert.That(latest.IsReplay).IsTrue();
        await Assert.That(latest.StreamDeliveryApplied).IsEqualTo(deliveryFault);
        await Assert.That(latest.TerminalMarkerObserved == true).IsEqualTo(!deliveryFault);
    }

}
