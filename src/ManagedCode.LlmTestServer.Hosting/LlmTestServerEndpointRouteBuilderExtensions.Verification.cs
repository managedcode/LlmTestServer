using ManagedCode.LlmTestServer.Control;
using ManagedCode.LlmTestServer.Conversations;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Verification;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ManagedCode.LlmTestServer.Hosting;

public static partial class LlmTestServerEndpointRouteBuilderExtensions
{
    private static void MapVerificationEndpoints(RouteGroupBuilder endpoints)
    {
        endpoints.MapPost(LlmTestServerControlRoutes.Verify, async (HttpContext context,
            ILlmTestServerRuntime runtime, ILlmTestServerProviderHttpTraceStore traces, CancellationToken cancellationToken) =>
        {
            var unauthorized = AuthorizeControlRequest(context, runtime);
            if (unauthorized is not null) { return unauthorized; }
            var read = await ReadJsonAsync<LlmTestServerVerificationRequest>(context, cancellationToken).ConfigureAwait(false);
            if (read.Error is not null) { return read.Error; }
            if (read.Value is null) { return InvalidRequest("Missing verification expectations."); }
            try
            {
                var snapshot = traces.GetEvidenceSnapshot();
                var selectedConversation = context.Request.Headers[LlmTestServerConversationHeaders.Id].FirstOrDefault();
                if (selectedConversation is not null)
                {
                    var requests = snapshot.Requests.Where(trace => trace.ConversationId == selectedConversation).ToArray();
                    snapshot = snapshot with { Requests = requests, TotalMatched = requests.Length };
                }
                var conversations = ((ILlmTestServerConversationDiagnostics)runtime).GetConversations();
                if (selectedConversation is not null)
                { conversations = conversations.Where(conversation => conversation.ConversationId == selectedConversation).ToArray(); }
                return Results.Json(LlmTestServerVerificationEvaluator.Verify(read.Value, snapshot, conversations));
            }
            catch (ArgumentException) { return InvalidRequest("Invalid verification expectations."); }
        });
    }
}
