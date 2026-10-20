using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed class ModalityFailureIntegrationTests
{
    [Test]
    [Arguments("images/generations", "gpt-image-1")]
    [Arguments("audio/speech", "gpt-4o-mini-tts")]
    [Arguments("audio/transcriptions", "gpt-4o-mini-tts")]
    [Arguments("audio/translations", "gpt-4o-mini-tts")]
    [Arguments("videos", "sora-2")]
    [Arguments("embeddings", "text-embedding-3-small")]
    public async Task Modality_RejectsCredentialsAndWrongModelWithoutConsumingSuccessAsync(string operation, string model)
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.RequireBearerToken("fixture-key"));
        using var http = host.GetTestClient();
        using var rejected = await SendAsync(http, operation, model, "hello");
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(await rejected.Content.ReadAsStringAsync()).Contains("invalid_api_key");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fixture-key");
        foreach (var invalidModel in new[] { "missing-model", "gpt-4.1-mini" })
        {
            using var unknown = await SendAsync(http, operation, invalidModel, "hello");
            await Assert.That(unknown.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(await unknown.Content.ReadAsStringAsync()).Contains("llm_tck_unknown_model");
        }
        using var success = await SendAsync(http, operation, model, "hello");
        success.EnsureSuccessStatusCode();
        var summary = (await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!;
        await Assert.That(summary.AuthFailed).IsEqualTo(1);
        await Assert.That(summary.ModelNotFound).IsEqualTo(2);
        await Assert.That(summary.Matched).IsEqualTo(1);
    }

    [Test]
    [Arguments("images/generations", "gpt-image-1")]
    [Arguments("audio/speech", "gpt-4o-mini-tts")]
    [Arguments("audio/transcriptions", "gpt-4o-mini-tts")]
    [Arguments("audio/translations", "gpt-4o-mini-tts")]
    [Arguments("videos", "sora-2")]
    [Arguments("embeddings", "text-embedding-3-small")]
    public async Task Modality_AppliesContentFilterAndRateLimitAndRestoresBudgetOnResetAsync(string operation, string model)
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config
            .SimulateContentFilter("blocked-fixture").SimulateRateLimitAfter(2));
        using var http = host.GetTestClient();
        using var filtered = await SendAsync(http, operation, model, "blocked-fixture");
        await Assert.That(filtered.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await filtered.Content.ReadAsStringAsync()).Contains("content_filter");
        using var success = await SendAsync(http, operation, model, "allowed");
        success.EnsureSuccessStatusCode();
        using var limited = await SendAsync(http, operation, model, "allowed");
        await Assert.That(limited.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(await limited.Content.ReadAsStringAsync()).Contains("too_many_requests");
        using var reset = await http.PostAsync("/admin-api/reset", null);
        reset.EnsureSuccessStatusCode();
        using var afterReset = await SendAsync(http, operation, model, "allowed");
        afterReset.EnsureSuccessStatusCode();
        var summary = (await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!;
        await Assert.That(summary.Matched).IsEqualTo(1);
        await Assert.That(summary.ErrorsReturned).IsEqualTo(0);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, string operation, string model, string text)
    {
        if (operation is "audio/transcriptions" or "audio/translations")
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(model), "model");
            form.Add(new StringContent(text), "prompt");
            form.Add(new ByteArrayContent([82, 73, 70, 70]), "file", "fixture.wav");
            return await http.PostAsync("/openai/v1/" + operation, form);
        }
        return await http.PostAsJsonAsync("/openai/v1/" + operation,
            new { model, prompt = text, input = text, voice = "alloy" });
    }
}
