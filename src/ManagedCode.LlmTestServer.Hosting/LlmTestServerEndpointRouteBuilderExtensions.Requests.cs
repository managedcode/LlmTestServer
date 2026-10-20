using System.Globalization;
using System.Text.Json;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Runtime;
using Microsoft.AspNetCore.Http;

namespace ManagedCode.LlmTestServer.Hosting;

public static partial class LlmTestServerEndpointRouteBuilderExtensions
{
    private static IResult GetRequests(HttpContext context, ILlmTestServerRuntime runtime, ILlmTestServerProviderHttpTraceStore traceStore)
    {
        return QueryRequests(context, runtime, traceStore, export: false);
    }

    private static IResult ExportRequests(HttpContext context, ILlmTestServerRuntime runtime, ILlmTestServerProviderHttpTraceStore traceStore)
    {
        return QueryRequests(context, runtime, traceStore, export: true);
    }

    private static IResult QueryRequests(HttpContext context, ILlmTestServerRuntime runtime, ILlmTestServerProviderHttpTraceStore traceStore, bool export)
    {
        var unauthorized = AuthorizeControlRequest(context, runtime);
        if (unauthorized is not null) { return unauthorized; }
        try
        {
            var query = ReadRequestQuery(context.Request);
            var evidence = LlmTestServerRequestQueries.Apply(traceStore.GetEvidenceSnapshot(), query);
            context.Response.Headers["X-Total-Count"] = evidence.TotalMatched.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-Total-Observed"] = evidence.TotalObserved.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-Evicted-Count"] = evidence.EvictedCount.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-Retained-Byte-Count"] = evidence.RetainedByteCount.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-Retention-Truncated"] = evidence.RetentionTruncated ? "true" : "false";
            if (!export) { return Results.Json(evidence.Requests); }
            var format = ReadSingle(context.Request, "format") ?? "json";
            var envelope = new LlmTestServerRequestExport { Query = query, Evidence = evidence };
            if (format == "json") { return Results.Json(envelope); }
            if (format != "ndjson") { return InvalidRequest("format must be json or ndjson."); }
            return new RequestNdjsonResult(envelope);
        }
        catch (ArgumentException) { return InvalidRequest("Invalid request query parameters."); }
    }

    private static LlmTestServerRequestQuery ReadRequestQuery(HttpRequest request)
    {
        int? Number(string key)
        {
            return ReadSingle(request, key) is { } value ? int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) : null;
        }

        bool? Boolean(string key)
        {
            return ReadSingle(request, key) is { } value ? bool.Parse(value) : null;
        }

        DateTimeOffset? Date(string key)
        {
            return ReadSingle(request, key) is { } value ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : null;
        }

        try
        {
            var order = ReadSingle(request, "order") ?? "desc";
            return new()
            {
                Provider = ReadSingle(request, "provider"),
                Model = ReadSingle(request, "model"),
                RequestId = ReadSingle(request, "requestId"),
                Status = Number("status"),
                Offset = Number("offset") ?? 0,
                Limit = Number("limit") ?? 500,
                Operation = ReadSingle(request, "operation"),
                Scenario = ReadSingle(request, "scenario"),
                Conversation = ReadSingle(request, "conversation"),
                Step = ReadSingle(request, "step"),
                Streaming = Boolean("streaming"),
                Completed = Boolean("completed"),
                Success = Boolean("success"),
                ErrorCode = ReadSingle(request, "errorCode"),
                From = Date("from"),
                To = Date("to"),
                Order = order switch { "asc" => LlmTestServerRequestOrder.Ascending, "desc" => LlmTestServerRequestOrder.Descending, _ => throw new ArgumentException("Invalid order.") }
            };
        }
        catch (FormatException exception) { throw new ArgumentException("Invalid request query format.", exception); }
        catch (OverflowException exception) { throw new ArgumentException("Invalid request query number.", exception); }
    }

    private static string? ReadSingle(HttpRequest request, string name)
    {
        var values = request.Query[name];
        if (values.Count > 1 || values.Count == 1 && string.IsNullOrWhiteSpace(values[0])) { throw new ArgumentException("Query values must occur once and be nonblank.", nameof(name)); }
        return values.Count == 0 ? null : values[0];
    }

    private sealed class RequestNdjsonResult(LlmTestServerRequestExport export) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            context.Response.ContentType = "application/x-ndjson; charset=utf-8";
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                type = "metadata",
                export.SchemaVersion,
                export.Query,
                evidence = export.Evidence with { Requests = [] }
            }, options) + "\n", context.RequestAborted).ConfigureAwait(false);
            foreach (var entry in export.Evidence.Requests)
            {
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { type = "request", request = entry }, options) + "\n", context.RequestAborted).ConfigureAwait(false);
            }
        }
    }
}
