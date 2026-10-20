using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Google.GenAI.Types;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Tests.Policy;

public sealed partial class OutputDispositionIntegrationTests
{
    private const string _geminiModel = "gemini-3.8-flash";

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task OfficialGoogle_OutputSafetyReturnsCandidateRatherThanPromptBlockAndRecoversAsync(bool streaming, bool empty)
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, _geminiModel, LlmTestServerOutputDispositionKind.Filtered, empty));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        using var sdk = GoogleSdk(http);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var text = new StringBuilder(); GenerateContentResponse? final = null;
        var options = new GenerateContentConfig { AutomaticContinuation = false };
        if (streaming)
        {
            await foreach (var chunk in sdk.Models.GenerateContentStreamAsync(_geminiModel, "policy", options, cancellationToken: deadline.Token))
            {
                text.Append(chunk.Text);
                await Assert.That(chunk.PromptFeedback).IsNull();
                if (chunk.Candidates?.Any(candidate => candidate.FinishReason is not null) == true) { final = chunk; }
            }
        }
        else
        {
            final = await sdk.Models.GenerateContentAsync(_geminiModel, "policy", options, cancellationToken: deadline.Token);
            text.Append(final.Text);
        }
        await Assert.That(final).IsNotNull();
        await Assert.That(final!.Candidates!.Single().FinishReason == FinishReason.Safety).IsTrue();
        await Assert.That(final.PromptFeedback).IsNull();
        await Assert.That(text.ToString()).IsEqualTo(empty || !streaming ? string.Empty : _text);
        if (streaming) { await Assert.That(final.Text ?? string.Empty).IsEqualTo(string.Empty); }
        await EvidenceAsync(control, LlmTestServerOutputDispositionKind.Filtered, streaming);
        var recovered = await sdk.Models.GenerateContentAsync(_geminiModel, "next", options, cancellationToken: deadline.Token);
        await Assert.That(recovered.Text).IsEqualTo(_recovery);
        await Assert.That(recovered.Candidates!.Single().FinishReason == FinishReason.Stop).IsTrue();
        await RecoveredAsync(control);
    }

    [Test]
    public async Task GeminiJsonArrayStreamCarriesPrefixAndSafetyTerminalThenOfficialSdkRecoveryAsync()
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, _geminiModel, LlmTestServerOutputDispositionKind.Filtered));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        // The official SDK uses SSE; raw HTTP supplements this distinct native JSON-array transport.
        using var response = await http.PostAsJsonAsync("/gemini/v1beta/models/" + _geminiModel + ":streamGenerateContent", new
        { contents = new[] { new { role = "user", parts = new[] { new { text = "policy" } } } } });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/json");
        var frames = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(frames.ValueKind).IsEqualTo(JsonValueKind.Array);
        var text = new StringBuilder();
        foreach (var frame in frames.EnumerateArray())
        {
            var candidate = frame.GetProperty("candidates")[0];
            if (candidate.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object
                && content.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                { if (part.TryGetProperty("text", out var value)) { text.Append(value.GetString()); } }
            }
        }
        await Assert.That(text.ToString()).IsEqualTo(_text);
        var final = frames[frames.GetArrayLength() - 1].GetProperty("candidates")[0];
        await Assert.That(final.GetProperty("finishReason").GetString()).IsEqualTo("SAFETY");
        await Assert.That(final.TryGetProperty("content", out var finalContent)
            && finalContent.ValueKind == JsonValueKind.Object && finalContent.TryGetProperty("parts", out var finalParts)
            && finalParts.EnumerateArray().Any(part => part.TryGetProperty("text", out var value) && !string.IsNullOrEmpty(value.GetString()))).IsFalse();
        await EvidenceAsync(control, LlmTestServerOutputDispositionKind.Filtered, true);
        using var sdk = GoogleSdk(http);
        var recovered = await sdk.Models.GenerateContentAsync(_geminiModel, "next", new GenerateContentConfig { AutomaticContinuation = false });
        await Assert.That(recovered.Text).IsEqualTo(_recovery);
        await RecoveredAsync(control);
    }

    private static Google.GenAI.Client GoogleSdk(HttpClient http)
    {
        return new(apiKey: _key, enterprise: false,
            httpOptions: new() { BaseUrl = new Uri(http.BaseAddress!, "/gemini").ToString(), ApiVersion = "v1beta", RetryOptions = new() { Attempts = 1 } },
            clientOptions: new() { HttpClientFactory = () => http });
    }
}
