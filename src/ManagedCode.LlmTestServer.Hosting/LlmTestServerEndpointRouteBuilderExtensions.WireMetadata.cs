using ManagedCode.LlmTestServer.Determinism;
using ManagedCode.LlmTestServer.Runtime;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.LlmTestServer.Hosting;

public static partial class LlmTestServerEndpointRouteBuilderExtensions
{
    private static LlmTestServerResponseMetadata WireMetadata(HttpContext context)
    {
        return context.RequestServices.GetRequiredService<ILlmTestServerRuntime>() is ILlmTestServerWireMetadataSource source
            ? source.GetResponseMetadata() : LlmTestServerResponseMetadata.Live();
    }
}
