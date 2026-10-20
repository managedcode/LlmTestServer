using System.Text.Json;
using ManagedCode.LlmTestServer.Control;
using ManagedCode.LlmTestServer.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ManagedCode.LlmTestServer.Hosting;

public static partial class LlmTestServerEndpointRouteBuilderExtensions
{
    private static void MapSessionEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(LlmTestServerControlRoutes.Sessions, async (HttpContext context, LlmTestServerSessionRegistry registry, CancellationToken cancellationToken) =>
        {
            var unauthorized = AuthorizeControlRequest(context, registry.Default.Runtime);
            if (unauthorized is not null) { return unauthorized; }
            if (context.Request.Headers.ContainsKey(LlmTestServerSessionHeaders.Id)
                || context.Request.Headers.ContainsKey(LlmTestServerSessionHeaders.AccessKey))
            { return InvalidRequest("Create sessions through the default management client."); }
            var read = await ReadJsonAsync<LlmTestServerSessionCreateRequest>(context, cancellationToken).ConfigureAwait(false);
            if (read.Error is not null) { return read.Error; }
            if (read.Value is null) { return InvalidRequest("Missing session body."); }
            try
            {
                var session = registry.Create(read.Value.Configuration);
                context.Response.Headers.CacheControl = "no-store";
                return session is null ? SessionCapacityExceeded()
                    : Results.Created(LlmTestServerControlRoutes.Sessions + "/" + session.Id, session);
            }
            catch (ArgumentException) { return InvalidRequest("Invalid session configuration."); }
            catch (JsonException) { return InvalidRequest("Invalid JSON fixture in session configuration."); }
        });
        endpoints.MapDelete(LlmTestServerControlRoutes.Sessions + "/{id}", async (string id, HttpContext context, LlmTestServerSessionRegistry registry) =>
        {
            var state = registry.Resolve(context);
            if (state is null || ReferenceEquals(state, registry.Default)) { return SessionNotFound(); }
            var removed = registry.DeleteAsync(id, state);
            if (removed is null) { return SessionNotFound(); }
            await removed.ConfigureAwait(false);
            return Results.NoContent();
        });
    }

    private static IResult SessionNotFound()
    {
        return Results.Json(
        new { error = new { code = "session_not_found", message = "The test session is unavailable with the supplied session credentials." } }, statusCode: 404);
    }

    private static IResult SessionCapacityExceeded()
    {
        return Results.Json(
        new { error = new { code = "session_capacity_exceeded", message = "The configured test session capacity is exhausted." } }, statusCode: 409);
    }
}
