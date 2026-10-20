using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.OpenAI;
using Microsoft.Extensions.AI;
using ProviderRoutes = ManagedCode.LlmTestServer.Providers.LlmTestServerProviderRouteNamespaces;

namespace ManagedCode.LlmTestServer.Client;

public sealed class LlmTestServerImageGenerator(
    HttpClient httpClient,
    string defaultModelId = LlmTestServerKnownModelIds.GptImage1,
    string? bearerToken = null
) : IImageGenerator
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var httpRequest = new OpenAiImageGenerationRequest
        {
            Model = options?.ModelId ?? defaultModelId,
            Prompt = request.Prompt ?? string.Empty,
        };

        using var requestMessage = new HttpRequestMessage(
            HttpMethod.Post,
            ProviderRoutes.ForProvider(ProviderRoutes.OpenAI, "/v1/images/generations")
        )
        {
            Content = JsonContent.Create(httpRequest, options: _jsonOptions),
        };
        ApplyBearerToken(requestMessage);

        using var response = await httpClient
            .SendAsync(requestMessage, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response
            .Content
            .ReadFromJsonAsync<OpenAiImageGenerationResponse>(_jsonOptions, cancellationToken)
            .ConfigureAwait(false);

        var image = payload?.Data.FirstOrDefault();
        var contents = new List<AIContent>();

        if (!string.IsNullOrEmpty(image?.Base64Json))
        {
            contents.Add(new DataContent($"data:image/png;base64,{image.Base64Json}"));
        }
        else if (!string.IsNullOrEmpty(image?.Url))
        {
            contents.Add(new UriContent(image.Url, "image/png"));
        }

        return new ImageGenerationResponse(contents);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }

    private void ApplyBearerToken(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
    }
}
