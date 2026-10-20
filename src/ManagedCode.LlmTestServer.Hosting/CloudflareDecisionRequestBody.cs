using System.Buffers;
using ManagedCode.LlmTestServer.Cloudflare;
using Microsoft.AspNetCore.Http;

namespace ManagedCode.LlmTestServer.Hosting;

internal static class CloudflareDecisionRequestBody
{
    private const int _bufferSize = 32 * 1024;

    public static async Task<bool> FitsLimitAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > CloudflareDecisionImageValidation.MaxRequestBytes) { return false; }
        request.EnableBuffering(_bufferSize, CloudflareDecisionImageValidation.MaxRequestBytes);
        var buffer = ArrayPool<byte>.Shared.Rent(_bufferSize);
        var start = request.Body.Position;
        try
        {
            long bytes = 0;
            int read;
            while ((read = await request.Body.ReadAsync(buffer.AsMemory(0, _bufferSize), cancellationToken).ConfigureAwait(false)) != 0)
            {
                bytes += read;
                if (bytes > CloudflareDecisionImageValidation.MaxRequestBytes) { return false; }
            }
            return true;
        }
        catch (IOException) { return false; }
        finally
        {
            request.Body.Position = start;
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
