using ManagedCode.LlmTestServer.Control;
using ManagedCode.LlmTestServer.Sessions;

namespace ManagedCode.LlmTestServer.Client;

public sealed class LlmTestServerSessionClient : IAsyncDisposable
{
    private bool _disposed;

    internal LlmTestServerSessionClient(HttpClient transport, LlmTestServerSession session, string? bearerToken)
    {
        Session = session;
        HttpClient = new HttpClient(new LlmTestServerHeaderTransport(transport, new Dictionary<string, string>
        { [LlmTestServerSessionHeaders.Id] = session.Id, [LlmTestServerSessionHeaders.AccessKey] = session.AccessKey }))
        { BaseAddress = transport.BaseAddress };
        Control = new LlmTestServerClient(HttpClient, bearerToken);
    }

    public LlmTestServerSession Session { get; }
    public HttpClient HttpClient { get; }
    public LlmTestServerClient Control { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) { return; }
        using var response = await HttpClient.DeleteAsync(LlmTestServerControlRoutes.Sessions + "/" + Session.Id).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound) { response.EnsureSuccessStatusCode(); }
        _disposed = true;
        HttpClient.Dispose();
    }

}
