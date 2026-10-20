using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Control;
using ManagedCode.LlmTestServer.Conversations;

namespace ManagedCode.LlmTestServer.Client;

public sealed partial class LlmTestServerClient
{
    public LlmTestServerConversationClient ForConversation(string id)
    {
        return new(httpClient, id, bearerToken);
    }

    public async Task<LlmTestServerMatchExplanation> ExplainMatchAsync(LlmTestServerMatchExplanationRequest match,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateJsonRequest(HttpMethod.Post, LlmTestServerControlRoutes.ExplainMatch, match);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LlmTestServerMatchExplanation>(_jsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<IReadOnlyList<LlmTestServerConversationStatus>> GetConversationsAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, LlmTestServerControlRoutes.Conversations);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<LlmTestServerConversationStatus>>(_jsonOptions, cancellationToken).ConfigureAwait(false))!;
    }
}
