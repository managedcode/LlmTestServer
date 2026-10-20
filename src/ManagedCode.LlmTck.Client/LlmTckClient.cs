using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTck.Configuration;
using ManagedCode.LlmTck.Control;
using ManagedCode.LlmTck.Models;
using ManagedCode.LlmTck.OpenAI;
using ManagedCode.LlmTck.Runtime;
using Microsoft.Extensions.AI;
using ProviderRoutes = ManagedCode.LlmTck.Providers.LlmTckProviderRouteNamespaces;

namespace ManagedCode.LlmTck.Client;

public sealed class LlmTckClient(HttpClient httpClient, string? bearerToken = null)
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public static LlmTckClient Create(HttpClient httpClient, string? bearerToken = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        return new LlmTckClient(httpClient, bearerToken);
    }

    public LlmTckClient WithBearerToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new LlmTckClient(httpClient, token);
    }

    public Task ConfigureAsync(
        Action<LlmTckClientConfigurationBuilder> configure,
        CancellationToken cancellationToken = default
    )
    {
        return ConfigureAsync(configure, resetFirst: false, cancellationToken);
    }

    public async Task ConfigureAsync(
        Action<LlmTckClientConfigurationBuilder> configure,
        bool resetFirst,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (resetFirst)
        {
            await ResetAsync(cancellationToken).ConfigureAwait(false);
        }

        var builder = new LlmTckClientConfigurationBuilder();
        configure(builder);
        await ConfigureAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
    }

    public async Task ConfigureAsync(
        LlmTckConfiguration configuration,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);

        using var request = CreateJsonRequest(HttpMethod.Post, LlmTckControlRoutes.Configure, configuration);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, LlmTckControlRoutes.Reset);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<LlmTckAssertionSummary> GetAssertionsAsync(
        CancellationToken cancellationToken = default
    )
    {
        using var request = CreateRequest(HttpMethod.Get, LlmTckControlRoutes.Assertions);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response
            .Content
            .ReadFromJsonAsync<LlmTckAssertionSummary>(_jsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? new LlmTckAssertionSummary();
    }

    public IChatClient CreateChatClient(string defaultModelId = LlmTckKnownModelIds.Gpt41Mini)
    {
        return new LlmTckChatClient(httpClient, defaultModelId, bearerToken);
    }

    public LlmTckCloudflareDecisionClient CreateCloudflareDecisionClient(string accountId)
    {
        return new(httpClient, accountId, bearerToken);
    }

    public LlmTckSystemOneClient CreateSystemOneClient()
    {
        return new(httpClient, bearerToken);
    }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        string defaultModelId = LlmTckKnownModelIds.TextEmbedding3Small
    )
    {
        return new LlmTckEmbeddingGenerator(httpClient, defaultModelId, bearerToken);
    }

    public IImageGenerator CreateImageGenerator(string defaultModelId = LlmTckKnownModelIds.GptImage1)
    {
        return new LlmTckImageGenerator(httpClient, defaultModelId, bearerToken);
    }

    public Task<LlmTckAudioContent> GenerateAudioAsync(
        string modelId,
        string input,
        CancellationToken cancellationToken
    )
    {
        return GenerateAudioAsync(modelId, input, voice: "alloy", cancellationToken);
    }

    public async Task<LlmTckAudioContent> GenerateAudioAsync(
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

        return new LlmTckAudioContent(
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
