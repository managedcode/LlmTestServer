using ManagedCode.LlmTestServer.Control;
using ManagedCode.LlmTestServer.Sessions;

namespace ManagedCode.LlmTestServer.Client;

public sealed class LlmTestServerSessionClient : IAsyncDisposable
{
    private bool _disposed;

    internal LlmTestServerSessionClient(HttpClient transport, LlmTestServerSession session, string? bearerToken)
    {
        Session = session;
        HttpClient = new HttpClient(new SessionHandler(transport, session)) { BaseAddress = transport.BaseAddress };
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

    private sealed class SessionHandler(HttpClient transport, LlmTestServerSession session) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version,
                VersionPolicy = request.VersionPolicy,
                Content = request.Content,
            };
            foreach (var header in request.Headers)
            { forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value); }
            foreach (var option in request.Options)
            { forwarded.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value); }
            forwarded.Headers.Remove(LlmTestServerSessionHeaders.Id);
            forwarded.Headers.Remove(LlmTestServerSessionHeaders.AccessKey);
            forwarded.Headers.Add(LlmTestServerSessionHeaders.Id, session.Id);
            forwarded.Headers.Add(LlmTestServerSessionHeaders.AccessKey, session.AccessKey);
            try
            {
                return await transport.SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            finally { forwarded.Content = null; }
        }
    }
}
