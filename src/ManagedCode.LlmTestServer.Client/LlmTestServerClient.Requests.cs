using System.Globalization;
using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Control;
using ManagedCode.LlmTestServer.Diagnostics;

namespace ManagedCode.LlmTestServer.Client;

public sealed partial class LlmTestServerClient
{
    public async Task<LlmTestServerRequestSnapshot> GetRequestsAsync(LlmTestServerRequestQuery? query = null, CancellationToken cancellationToken = default)
    {
        query ??= new();
        using var request = CreateRequest(HttpMethod.Get, LlmTestServerControlRoutes.Requests + BuildRequestQuery(query));
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var entries = await response.Content.ReadFromJsonAsync<List<LlmTestServerProviderHttpTrace>>(_jsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        long Header(string name)
        {
            return response.Headers.TryGetValues(name, out var values) ? long.Parse(values.Single(), CultureInfo.InvariantCulture) : 0;
        }

        return new()
        {
            Requests = entries.AsReadOnly(),
            TotalMatched = checked((int)Header("X-Total-Count")),
            TotalObserved = Header("X-Total-Observed"),
            EvictedCount = Header("X-Evicted-Count"),
            RetainedByteCount = Header("X-Retained-Byte-Count"),
            RetentionTruncated = response.Headers.TryGetValues("X-Retention-Truncated", out var truncated) && truncated.Single() == "true",
            FirstSequence = entries.Count == 0 ? null : entries.Min(entry => entry.Sequence),
            LastSequence = entries.Count == 0 ? null : entries.Max(entry => entry.Sequence)
        };
    }

    public async Task<LlmTestServerRequestExport> ExportRequestsAsync(LlmTestServerRequestQuery? query = null, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, LlmTestServerControlRoutes.Requests + "/export" + BuildRequestQuery(query ?? new()));
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LlmTestServerRequestExport>(_jsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<string> ExportRequestsNdjsonAsync(LlmTestServerRequestQuery? query = null, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, LlmTestServerControlRoutes.Requests + "/export" + BuildRequestQuery(query ?? new()) + "&format=ndjson");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildRequestQuery(LlmTestServerRequestQuery query)
    {
        LlmTestServerRequestQueries.Apply(new(), query);
        var values = new Dictionary<string, string?>
        {
            ["provider"] = query.Provider,
            ["model"] = query.Model,
            ["requestId"] = query.RequestId,
            ["status"] = query.Status?.ToString(CultureInfo.InvariantCulture),
            ["operation"] = query.Operation,
            ["scenario"] = query.Scenario,
            ["conversation"] = query.Conversation,
            ["step"] = query.Step,
            ["streaming"] = query.Streaming?.ToString().ToLowerInvariant(),
            ["completed"] = query.Completed?.ToString().ToLowerInvariant(),
            ["success"] = query.Success?.ToString().ToLowerInvariant(),
            ["errorCode"] = query.ErrorCode,
            ["from"] = query.From?.ToString("O", CultureInfo.InvariantCulture),
            ["to"] = query.To?.ToString("O", CultureInfo.InvariantCulture),
            ["offset"] = query.Offset.ToString(CultureInfo.InvariantCulture),
            ["limit"] = query.Limit.ToString(CultureInfo.InvariantCulture),
            ["order"] = query.Order == LlmTestServerRequestOrder.Ascending ? "asc" : "desc",
        };
        return "?" + string.Join("&", values.Where(pair => pair.Value is not null).Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value!)));
    }
}
