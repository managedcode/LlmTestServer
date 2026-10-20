using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Control;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.OpenAI;
using ManagedCode.LlmTestServer.Runtime;
using Microsoft.Extensions.AI;
using ProviderRoutes = ManagedCode.LlmTestServer.Providers.LlmTestServerProviderRouteNamespaces;

namespace ManagedCode.LlmTestServer.Client;

public sealed partial class LlmTestServerClient(HttpClient httpClient, string? bearerToken = null)
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public static LlmTestServerClient Create(HttpClient httpClient, string? bearerToken = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        return new LlmTestServerClient(httpClient, bearerToken);
    }

    public LlmTestServerClient WithBearerToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new LlmTestServerClient(httpClient, token);
    }

    public Task ConfigureAsync(
        Action<LlmTestServerClientConfigurationBuilder> configure,
        CancellationToken cancellationToken = default
    )
    {
        return ConfigureAsync(configure, resetFirst: false, cancellationToken);
    }

    public async Task ConfigureAsync(
        Action<LlmTestServerClientConfigurationBuilder> configure,
        bool resetFirst,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (resetFirst)
        {
            await ResetAsync(cancellationToken).ConfigureAwait(false);
        }

        var builder = new LlmTestServerClientConfigurationBuilder();
        configure(builder);
        await ConfigureAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
    }

    public async Task ConfigureAsync(
        LlmTestServerConfiguration configuration,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);

        using var request = CreateJsonRequest(HttpMethod.Post, LlmTestServerControlRoutes.Configure, configuration);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, LlmTestServerControlRoutes.Reset);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<LlmTestServerAssertionSummary> GetAssertionsAsync(
        CancellationToken cancellationToken = default
    )
    {
        using var request = CreateRequest(HttpMethod.Get, LlmTestServerControlRoutes.Assertions);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response
            .Content
            .ReadFromJsonAsync<LlmTestServerAssertionSummary>(_jsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? new LlmTestServerAssertionSummary();
    }

    public IChatClient CreateChatClient(string defaultModelId = LlmTestServerKnownModelIds.Gpt41Mini)
    {
        return new LlmTestServerChatClient(httpClient, defaultModelId, bearerToken);
    }

    public LlmTestServerCloudflareDecisionClient CreateCloudflareDecisionClient(string accountId)
    {
        return new(httpClient, accountId, bearerToken);
    }

    public LlmTestServerSystemOneClient CreateSystemOneClient()
    {
        return new(httpClient, bearerToken);
    }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        string defaultModelId = LlmTestServerKnownModelIds.TextEmbedding3Small
    )
    {
        return new LlmTestServerEmbeddingGenerator(httpClient, defaultModelId, bearerToken);
    }

    public IImageGenerator CreateImageGenerator(string defaultModelId = LlmTestServerKnownModelIds.GptImage1)
    {
        return new LlmTestServerImageGenerator(httpClient, defaultModelId, bearerToken);
    }

    public Task<LlmTestServerAudioContent> GenerateAudioAsync(
        string modelId,
        string input,
        CancellationToken cancellationToken
    )
    {
        return GenerateAudioAsync(modelId, input, voice: "alloy", cancellationToken);
    }

    public async Task<LlmTestServerAudioContent> GenerateAudioAsync(
        string modelId,
        string input,
        string voice = "alloy",
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(voice);

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            ProviderRoutes.ForProvider(ProviderRoutes.OpenAI, "/v1/audio/speech"),
            new { model = modelId, input, voice }
        );
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return new LlmTestServerAudioContent(
            await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false),
            response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream"
        );
    }

    public async Task<string> GenerateVideoAsync(
        string modelId,
        string prompt,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(prompt);

        using var request = CreateRequest(
            HttpMethod.Post,
            ProviderRoutes.ForProvider(ProviderRoutes.OpenAI, "/v1/videos")
        );
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(modelId), "model");
        form.Add(new StringContent(prompt), "prompt");
        request.Content = form;

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var video = await response
            .Content
            .ReadFromJsonAsync<OpenAiVideoResponse>(_jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return video?.Id ?? string.Empty;
    }

    public async Task<string> TranscribeAudioAsync(
        string modelId,
        byte[] bytes,
        string fileName = "audio.wav",
        string mediaType = "audio/wav",
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        using var request = CreateRequest(
            HttpMethod.Post,
            ProviderRoutes.ForProvider(ProviderRoutes.OpenAI, "/v1/audio/transcriptions")
        );
        using var form = new MultipartFormDataContent();
        using var audio = new ByteArrayContent(bytes);
        audio.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(audio, "file", fileName);
        form.Add(new StringContent(modelId), "model");
        request.Content = form;

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var transcription = await response
            .Content
            .ReadFromJsonAsync<OpenAiAudioTranscriptionResponse>(_jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return transcription?.Text ?? string.Empty;
    }

    public async Task<string> TranslateAudioAsync(
        string modelId,
        byte[] bytes,
        string fileName = "audio.wav",
        string mediaType = "audio/wav",
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        using var request = CreateRequest(
            HttpMethod.Post,
            ProviderRoutes.ForProvider(ProviderRoutes.OpenAI, "/v1/audio/translations")
        );
        using var form = new MultipartFormDataContent();
        using var audio = new ByteArrayContent(bytes);
        audio.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(audio, "file", fileName);
        form.Add(new StringContent(modelId), "model");
        request.Content = form;

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var translation = await response
            .Content
            .ReadFromJsonAsync<OpenAiAudioTranscriptionResponse>(_jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return translation?.Text ?? string.Empty;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string requestUri)
    {
        var request = new HttpRequestMessage(method, requestUri);
        ApplyBearerToken(request);
        return request;
    }

    private HttpRequestMessage CreateJsonRequest<TPayload>(
        HttpMethod method,
        string requestUri,
        TPayload payload
    )
    {
        var request = CreateRequest(method, requestUri);
        request.Content = JsonContent.Create(payload, options: _jsonOptions);
        return request;
    }

    private void ApplyBearerToken(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
    }
}
