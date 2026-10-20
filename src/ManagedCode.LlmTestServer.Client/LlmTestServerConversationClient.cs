using ManagedCode.LlmTestServer.Conversations;

namespace ManagedCode.LlmTestServer.Client;

public sealed class LlmTestServerConversationClient : IDisposable
{
    internal LlmTestServerConversationClient(HttpClient transport, string id, string? bearerToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 128) { throw new ArgumentException("Conversation IDs are limited to 128 characters.", nameof(id)); }
        Id = id;
        HttpClient = new HttpClient(new LlmTestServerHeaderTransport(transport,
            new Dictionary<string, string> { [LlmTestServerConversationHeaders.Id] = id }))
        { BaseAddress = transport.BaseAddress };
        Control = new LlmTestServerClient(HttpClient, bearerToken);
    }
    public string Id { get; }
    public HttpClient HttpClient { get; }
    public LlmTestServerClient Control { get; }
    public void Dispose() { HttpClient.Dispose(); }
}
