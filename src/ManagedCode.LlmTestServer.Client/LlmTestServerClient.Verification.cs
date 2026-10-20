using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Control;
using ManagedCode.LlmTestServer.Verification;

namespace ManagedCode.LlmTestServer.Client;

public sealed partial class LlmTestServerClient
{
    public async Task<LlmTestServerVerificationResult> VerifyAsync(LlmTestServerVerificationRequest verification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verification);
        using var request = CreateJsonRequest(HttpMethod.Post, LlmTestServerControlRoutes.Verify, verification);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LlmTestServerVerificationResult>(_jsonOptions, cancellationToken).ConfigureAwait(false))!;
    }
}
