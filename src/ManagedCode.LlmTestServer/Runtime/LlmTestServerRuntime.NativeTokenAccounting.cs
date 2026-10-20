using System.Text.Json;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Runtime;

public sealed partial class LlmTestServerRuntime
{
    private static string NativeDeclarationTokenText(LlmTestServerChatRequest request)
    {
        return request.NativeTools.Count == 0 ? string.Empty : JsonSerializer.Serialize(request.NativeTools);
    }
}
