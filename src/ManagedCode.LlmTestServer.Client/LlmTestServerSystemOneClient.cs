using System.Net.Http.Headers;
using System.Net.Http.Json;
using ManagedCode.LlmTestServer.SystemOne;

namespace ManagedCode.LlmTestServer.Client;

/// <summary>Typed native Jev/Kev client for a test-server host. The caller owns the shared HttpClient.</summary>
public sealed class LlmTestServerSystemOneClient(HttpClient httpClient, string? bearerToken = null)
{
    private const string _path = "systemone/v1/systemone";
    private const string _requestPart = "request";

    public Task<SystemOneDecisionResponse> EvaluateAsync(SystemOneDecisionRequest request,
        bool enableImages = false, bool enableLongContext = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(JsonContent.Create(request), enableImages, enableLongContext, cancellationToken);
    }

    public Task<SystemOneDecisionResponse> EvaluateMultipartAsync(SystemOneDecisionRequest request,
        IReadOnlyList<SystemOneImageUpload> images, bool enableImages = false, bool enableLongContext = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(images);
        var content = new MultipartFormDataContent();
        try
        {
            content.Add(JsonContent.Create(request), _requestPart);
            foreach (var image in images)
            {
                var file = new ReadOnlyMemoryContent(image.Data);
                if (image.ContentType is { } media) { file.Headers.ContentType = new MediaTypeHeaderValue(media); }
                content.Add(file, image.PartName, image.FileName ?? image.PartName);
            }
            return SendAsync(content, enableImages, enableLongContext, cancellationToken);
        }
        catch { content.Dispose(); throw; }
    }

    private async Task<SystemOneDecisionResponse> SendAsync(HttpContent content, bool images, bool longContext,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, _path) { Content = content };
        if (bearerToken is not null) { message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken); }
        if (images) { message.Headers.Add(TypeSafeRequestValidation.ImagesHeader, TypeSafeRequestValidation.Enabled); }
        if (longContext) { message.Headers.Add(TypeSafeRequestValidation.LongContextHeader, TypeSafeRequestValidation.Enabled); }
        using var response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SystemOneDecisionResponse>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("SystemOne returned an empty native response.");
    }
}

public sealed record SystemOneImageUpload
{
    public required string PartName { get; init; }
    public required ReadOnlyMemory<byte> Data { get; init; }
    public string? ContentType { get; init; }
    public string? FileName { get; init; }
}
