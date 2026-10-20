using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Control;
using ManagedCode.LlmTestServer.Sessions;

namespace ManagedCode.LlmTestServer.Client;

public sealed partial class LlmTestServerClient
{
    public async Task<LlmTestServerSessionClient> CreateSessionAsync(
        Action<LlmTestServerClientConfigurationBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        LlmTestServerClientConfigurationBuilder? configuration = null;
        if (configure is not null) { configuration = new(); configure(configuration); }
        var configured = configuration?.Build();
        using var request = CreateJsonRequest(HttpMethod.Post, LlmTestServerControlRoutes.Sessions,
            new LlmTestServerSessionCreateRequest { Configuration = configured });
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<LlmTestServerSession>(_jsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException("The server returned an empty session response.");
        return new LlmTestServerSessionClient(httpClient, session, configured?.RequiredBearerToken ?? bearerToken);
    }
}
