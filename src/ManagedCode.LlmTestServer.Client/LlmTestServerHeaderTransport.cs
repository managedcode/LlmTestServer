namespace ManagedCode.LlmTestServer.Client;

internal sealed class LlmTestServerHeaderTransport(HttpClient transport, IReadOnlyDictionary<string, string> headers) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri)
        { Version = request.Version, VersionPolicy = request.VersionPolicy, Content = request.Content };
        foreach (var header in request.Headers) { forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value); }
        foreach (var option in request.Options) { forwarded.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value); }
        foreach (var header in headers)
        { forwarded.Headers.Remove(header.Key); forwarded.Headers.Add(header.Key, header.Value); }
        try { return await transport.SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false); }
        finally { forwarded.Content = null; }
    }
}
