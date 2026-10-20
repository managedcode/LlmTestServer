using System.Globalization;
using ManagedCode.LlmTestServer.Runtime;
using Microsoft.AspNetCore.Http;

namespace ManagedCode.LlmTestServer.Hosting;

public static partial class LlmTestServerEndpointRouteBuilderExtensions
{
    private static IResult GetRequests(
        HttpContext context,
        ILlmTestServerRuntime runtime,
        ILlmTestServerProviderHttpTraceStore traceStore,
        string? provider, string? model, string? requestId, int? status, int? offset, int? limit)
    {
        var unauthorized = AuthorizeControlRequest(context, runtime);
        if (unauthorized is not null)
        {
            return unauthorized;
        }
        var start = offset ?? 0;
        var count = limit ?? 500;
        if (start < 0 || count is < 1 or > 500 || status is < 100 or > 599)
        {
            return InvalidRequest("offset must be nonnegative, limit must be 1–500, and status must be 100–599.");
        }

        var entries = traceStore.GetSnapshot().Where(entry =>
            (provider is null || entry.ProviderId == provider)
            && (model is null || entry.ModelId == model)
            && (requestId is null || entry.RequestId == requestId)
            && (status is null || entry.StatusCode == status)).ToArray();
        context.Response.Headers["X-Total-Count"] = entries.Length.ToString(CultureInfo.InvariantCulture);
        return Results.Json(entries.Skip(start).Take(count));
    }
}
