using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Audio;

namespace ManagedCode.LlmTestServer.Tests.Azure;

#pragma warning disable OPENAI001 // Official transcription usage types are experimental.
#pragma warning disable MEAI001 // Official speech usage modality counters are experimental.
public sealed class V1AudioTranscriptionCompatibilityTests
{
    private const string _whisperModel = "whisper-1";
    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.OpenAI, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.OpenAI, true)]
    public async Task OfficialSdk_PreservesTranscriptionUsageAsync(string provider, bool tokens)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = LlmTestServerClient.Create(http);
        await control.ConfigureAsync(configuration => configuration.RequireBearerToken("test-key")
            .UseAudioModel(_whisperModel).UseTranscriptionText("blue whale")
            .UseTranscriptionUsage(tokens ? LlmTestServerTranscriptionUsage.ForTokens(12, 3, 4)
                : LlmTestServerTranscriptionUsage.ForDuration(0.25)));
        var sdk = new OpenAIClient(new ApiKeyCredential("test-key"), new OpenAIClientOptions
        {
            Endpoint = Endpoint(http, provider),
            Transport = new HttpClientPipelineTransport(http),
        }).GetAudioClient(_whisperModel);
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var audio = new MemoryStream([82, 73, 70, 70]);
        var response = await sdk.TranscribeAudioAsync(audio, "fixture.wav", cancellationToken: cancellationToken);
        await Assert.That(response.Value.Text).IsEqualTo("blue whale");
        var wire = JsonDocument.Parse(response.GetRawResponse().Content.ToString());
        using (wire)
        {
            var usage = wire.RootElement.GetProperty("usage");
            await Assert.That(usage.GetProperty("type").GetString()).IsEqualTo(tokens ? "tokens" : "duration");
            if (tokens)
            {
                await Assert.That(usage.GetProperty("input_tokens").GetInt64()).IsEqualTo(15);
                await Assert.That(usage.GetProperty("output_tokens").GetInt64()).IsEqualTo(4);
                await Assert.That(usage.GetProperty("total_tokens").GetInt64()).IsEqualTo(19);
            }
            else
            {
                await Assert.That(((AudioTranscriptionDurationUsage)response.Value.Usage).Duration)
                    .IsEqualTo(TimeSpan.FromSeconds(0.25));
            }
        }
        using var speech = sdk.AsISpeechToTextClient();
        using var nextAudio = new MemoryStream([82, 73, 70, 70]);
        var adapted = await speech.GetTextAsync(nextAudio, cancellationToken: cancellationToken);
        await Assert.That(adapted.Text).IsEqualTo("blue whale");
        if (tokens)
        {
            await Assert.That(adapted.Usage!.InputAudioTokenCount).IsEqualTo(12);
            await Assert.That(adapted.Usage.InputTextTokenCount).IsEqualTo(3);
            await Assert.That(adapted.Usage.OutputTokenCount).IsEqualTo(4);
        }
        else
        {
            var raw = (AudioTranscription)adapted.RawRepresentation!;
            await Assert.That(((AudioTranscriptionDurationUsage)raw.Usage).Duration)
                .IsEqualTo(TimeSpan.FromSeconds(0.25));
        }
    }

    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry)]
    [Arguments(LlmTestServerProviderRouteNamespaces.OpenAI)]
    public async Task OfficialSdk_PreservesMissingTranscriptionUsageAsync(string provider)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = LlmTestServerClient.Create(http);
        await control.ConfigureAsync(configuration => configuration
            .RequireBearerToken("test-key")
            .UseAudioModel(_whisperModel)
            .UseTranscriptionText("blue whale")
            .WithoutTranscriptionUsage());
        var sdk = new OpenAIClient(new ApiKeyCredential("test-key"), new OpenAIClientOptions
        {
            Endpoint = Endpoint(http, provider),
            Transport = new HttpClientPipelineTransport(http),
        }).GetAudioClient(_whisperModel);
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var audio = new MemoryStream([82, 73, 70, 70]);
        var response = await sdk.TranscribeAudioAsync(audio, "fixture.wav", cancellationToken: cancellationToken);
        await Assert.That(response.Value.Text).IsEqualTo("blue whale");
        await Assert.That(response.Value.Usage).IsNull();
        var wire = JsonDocument.Parse(response.GetRawResponse().Content.ToString());
        using (wire)
        {
            await Assert.That(wire.RootElement.TryGetProperty("usage", out _)).IsFalse();
        }

        using var speech = sdk.AsISpeechToTextClient();
        using var nextAudio = new MemoryStream([82, 73, 70, 70]);
        var adapted = await speech.GetTextAsync(nextAudio, cancellationToken: cancellationToken);
        await Assert.That(adapted.Text).IsEqualTo("blue whale");
        await Assert.That(adapted.Usage).IsNull();
        var raw = (AudioTranscription)adapted.RawRepresentation!;
        await Assert.That(raw.Usage).IsNull();
    }

    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry)]
    [Arguments(LlmTestServerProviderRouteNamespaces.OpenAI)]
    public async Task Streaming_PreservesFinalUsageAsync(string provider)
    {
        using var host = await LlmTestServerTestHost.StartAsync(configuration => configuration
            .RequireBearerToken("test-key").AddModel("gpt-4o-transcribe", LlmTestServerModelKind.Audio)
            .WithDefaultTranscriptionUsage(LlmTestServerTranscriptionUsage.ForTokens(12, 0, 4)));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-key");
        using var form = AudioForm("gpt-4o-transcribe");
        form.Add(new StringContent("true"), "stream");
        using var response = await http.PostAsync(new Uri(Endpoint(http, provider), "audio/transcriptions"), form);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var done = body.Split('\n').Single(line => line.StartsWith("data: ", StringComparison.Ordinal)
            && line.Contains("transcript.text.done", StringComparison.Ordinal));
        using var payload = JsonDocument.Parse(done[6..]);
        await Assert.That(payload.RootElement.GetProperty("usage").GetProperty("total_tokens").GetInt64())
            .IsEqualTo(16);
    }

    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry)]
    public async Task V1Audio_EnforcesAuthAndVersionAndSupportsVerboseJsonAsync(string provider)
    {
        using var host = await LlmTestServerTestHost.StartAsync(configuration => configuration.RequireBearerToken("test-key")
            .AddModel(_whisperModel, LlmTestServerModelKind.Audio)
            .WithDefaultTranscriptionUsage(LlmTestServerTranscriptionUsage.ForDuration(0.25)));
        using var http = host.GetTestClient();
        var route = new Uri(Endpoint(http, provider), "audio/transcriptions");
        using (var form = AudioForm(_whisperModel))
        using (var response = await http.PostAsync(route, form))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        http.DefaultRequestHeaders.Add("api-key", "test-key");
        foreach (var query in new[] { "?api-version=unknown", "?api-version=2029-01-01", "?api-version=v1&api-version=v1" })
        {
            using var form = AudioForm(_whisperModel);
            using var response = await http.PostAsync(route + query, form);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        foreach (var query in new[] { "", "?api-version=v1", "?api-version=preview" })
        {
            using var form = AudioForm(_whisperModel);
            form.Add(new StringContent("verbose_json"), "response_format");
            using var response = await http.PostAsync(route + query, form);
            response.EnsureSuccessStatusCode();
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await Assert.That(payload.RootElement.GetProperty("duration").GetDouble()).IsEqualTo(0.25);
            await Assert.That(payload.RootElement.GetProperty("usage").GetProperty("seconds").GetDouble()).IsEqualTo(0.25);
        }
        using var empty = new MultipartFormDataContent();
        empty.Add(new StringContent(_whisperModel), "model");
        using var invalid = await http.PostAsync(route, empty);
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    private static Uri Endpoint(HttpClient http, string provider)
    {
        return new(http.BaseAddress!, provider.TrimStart('/') + (provider == LlmTestServerProviderRouteNamespaces.OpenAI ? "/v1/" : "/openai/v1/"));
    }

    private static MultipartFormDataContent AudioForm(string model)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(model), "model" },
            { new ByteArrayContent([82, 73, 70, 70]), "file", "fixture.wav" }
        };
        return form;
    }
}
