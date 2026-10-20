using System.Net;
using System.Net.Http.Json;
using System.Text;
using Google.GenAI.Types;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Tests.Streaming;

public sealed partial class StreamDeliveryIntegrationTests
{
    private const string _geminiDeliveryModel = "gemini-3.8-flash";

    [Test]
    [Arguments(LlmTestServerStreamDeliveryMode.EarlyCleanEnd, false, false)]
    [Arguments(LlmTestServerStreamDeliveryMode.EarlyCleanEnd, false, true)]
    [Arguments(LlmTestServerStreamDeliveryMode.AbortConnection, false, false)]
    [Arguments(LlmTestServerStreamDeliveryMode.DelayThenContinue, true, false)]
    [Arguments(LlmTestServerStreamDeliveryMode.DelayThenContinue, false, false)]
    public async Task OfficialGoogleSdk_TextSseDeliveryFaultsPreservePrefixAndRecoverAsync(LlmTestServerStreamDeliveryMode mode, bool cancel, bool afterAllText)
    {
        await AssertProtocolDeliveryAsync(_geminiDeliveryModel, "/gemini/v1beta/models/" + _geminiDeliveryModel + ":streamGenerateContent", "first ", mode, cancel,
            async (http, first, token, keepAlive) =>
            {
                var sdk = GoogleSdk(http);
                keepAlive(sdk);
                var text = new StringBuilder();
                var terminal = false;
                Exception? failure = null;
                try
                {
                    await foreach (var update in sdk.Models.GenerateContentStreamAsync(_geminiDeliveryModel, "stream", new GenerateContentConfig { AutomaticContinuation = false }, cancellationToken: token))
                    {
                        var delta = update.Text;
                        text.Append(delta);
                        if (!string.IsNullOrEmpty(delta)) { first(); }
                        if (update.Candidates?.Any(candidate => candidate.FinishReason is not null) == true) { terminal = true; }
                    }
                }
                catch (Exception exception) when (ProtocolStreamException(exception)) { failure = exception; }
                return new(text.ToString(), terminal, failure);
            },
            async (http, token, keepAlive) =>
            {
                var sdk = GoogleSdk(http);
                keepAlive(sdk);
                return (await sdk.Models.GenerateContentAsync(_geminiDeliveryModel, "explicit retry", new GenerateContentConfig { AutomaticContinuation = false }, cancellationToken: token)).Text!;
            }, afterAllText);
    }

    [Test]
    public async Task GoogleJsonArrayStreamRejectsDeliveryPlanBeforeConsumptionThenOfficialSseAndJsonRecoverAsync()
    {
        await using var app = await StartTcpHostAsync(config => config.AddModel(_geminiDeliveryModel, LlmTestServerModelKind.Chat)
            .AddChatScenario("gemini-delivery-admission", scenario => scenario.ForModel(_geminiDeliveryModel)
                .Responds("first second", "first ", "second").WithStreamDelivery(new()
                { Mode = LlmTestServerStreamDeliveryMode.EarlyCleanEnd }).Responds("google recovered")));
        using var http = TcpClient(app);
        var control = new LlmTestServerClient(http);
        using var rejected = await http.PostAsJsonAsync("/gemini/v1beta/models/" + _geminiDeliveryModel + ":streamGenerateContent", new
        { contents = new[] { new { role = "user", parts = new[] { new { text = "stream" } } } } });
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        using var sdk = GoogleSdk(http);
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var text = new StringBuilder();
        var finished = false;
        await foreach (var update in sdk.Models.GenerateContentStreamAsync(_geminiDeliveryModel, "stream", new GenerateContentConfig { AutomaticContinuation = false }, cancellationToken: watchdog.Token))
        {
            text.Append(update.Text);
            finished |= update.Candidates?.Any(candidate => candidate.FinishReason is not null) == true;
        }
        await Assert.That(text.ToString()).IsEqualTo("first ");
        await Assert.That(finished).IsFalse();
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(1);
        await Assert.That((await sdk.Models.GenerateContentAsync(_geminiDeliveryModel, "next", new GenerateContentConfig { AutomaticContinuation = false }, cancellationToken: watchdog.Token)).Text).IsEqualTo("google recovered");
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
    }

    private static Google.GenAI.Client GoogleSdk(HttpClient http)
    {
        return new(apiKey: "gemini-delivery-key", enterprise: false,
        httpOptions: new() { BaseUrl = new Uri(http.BaseAddress!, "/gemini").ToString(), ApiVersion = "v1beta", RetryOptions = new() { Attempts = 1 } },
        clientOptions: new() { HttpClientFactory = () => http });
    }
}
