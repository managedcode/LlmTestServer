using ManagedCode.LlmTestServer.Conversations;
using ManagedCode.LlmTestServer.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace ManagedCode.LlmTestServer.Hosting;

public static partial class LlmTestServerEndpointRouteBuilderExtensions
{
    private static IResult? ValidateCorrelationHeaders(HttpContext context)
    {
        foreach (var name in new[] { LlmTestServerConversationHeaders.Id, LlmTestServerRequestHeaders.RetryGroup })
        {
            var values = context.Request.Headers[name];
            if (context.Request.Headers.ContainsKey(name)
                && (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]) || values[0]!.Length > 128))
            { return InvalidRequest("Supply one nonempty correlation ID of at most 128 characters per header."); }
        }
        return null;
    }
}
