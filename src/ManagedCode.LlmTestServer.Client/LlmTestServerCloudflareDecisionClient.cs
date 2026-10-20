using System.Net.Http.Headers;
using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Cloudflare;

namespace ManagedCode.LlmTestServer.Client;

/// <summary>Typed native Clef/Clef-flash client for a test-server host. The caller owns the HttpClient.</summary>
public sealed class LlmTestServerCloudflareDecisionClient
{
    private readonly HttpClient _httpClient;
    private readonly string _accountId;
    private readonly string? _bearerToken;

    public LlmTestServerCloudflareDecisionClient(HttpClient httpClient, string accountId, string? bearerToken = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        _httpClient = httpClient;
        _accountId = Uri.EscapeDataString(accountId);
        _bearerToken = bearerToken;
    }

    public async Task<CloudflareDecisionResponse> EvaluateAsync(CloudflareDecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var model = request.Model.Trim();
        if (model is not ("clef" or "clef-flash"))
        { throw new ArgumentException("Select clef or clef-flash for the native Cloudflare endpoint.", nameof(request)); }
        var path = $"cloudflare/client/v4/accounts/{_accountId}/ai/run/@cf/cloudflare/{model}";
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(request) };
        if (_bearerToken is not null) { message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken); }
        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        // Cloudflare reports provider failures through its native envelope, including on non-2xx responses.
        return await response.Content.ReadFromJsonAsync<CloudflareDecisionResponse>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Cloudflare returned an empty native decision response.");
    }
}
