using ManagedCode.LlmTestServer.Control;
using ManagedCode.LlmTestServer.Conversations;
using ManagedCode.LlmTestServer.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ManagedCode.LlmTestServer.Hosting;

public static partial class LlmTestServerEndpointRouteBuilderExtensions
{
    private static void MapConversationEndpoints(RouteGroupBuilder endpoints)
    {
        endpoints.MapGet(LlmTestServerControlRoutes.Conversations, (HttpContext context, ILlmTestServerRuntime runtime) =>
        {
            var unauthorized = AuthorizeControlRequest(context, runtime);
            if (unauthorized is not null) { return unauthorized; }
            var selected = context.Request.Headers[LlmTestServerConversationHeaders.Id].FirstOrDefault();
            var states = ((ILlmTestServerConversationDiagnostics)runtime).GetConversations();
            return Results.Json(selected is null ? states : states.Where(state => state.ConversationId == selected));
        });
        endpoints.MapPost(LlmTestServerControlRoutes.ExplainMatch, async (HttpContext context, ILlmTestServerRuntime runtime, CancellationToken cancellationToken) =>
        {
            var unauthorized = AuthorizeControlRequest(context, runtime);
            if (unauthorized is not null) { return unauthorized; }
            var read = await ReadJsonAsync<LlmTestServerMatchExplanationRequest>(context, cancellationToken).ConfigureAwait(false);
            if (read.Error is not null) { return read.Error; }
            if (read.Value is null) { return InvalidRequest("Missing match explanation request."); }
            try
            {
                return Results.Json(((ILlmTestServerConversationDiagnostics)runtime).Explain(read.Value.Request,
                    read.Value.ProviderNamespace, context.Request.Headers[LlmTestServerConversationHeaders.Id].FirstOrDefault(), ReadAccessToken(context)));
            }
            catch (ArgumentException) { return InvalidRequest("Invalid match explanation request."); }
        });
    }
}
