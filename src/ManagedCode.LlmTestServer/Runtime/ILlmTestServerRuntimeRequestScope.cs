namespace ManagedCode.LlmTestServer.Runtime;

/// <summary>
/// Optional provider-neutral correlation scope used by hosts to associate runtime events with an
/// external request and publish its parsed model before execution completes, without changing the operation methods on <see cref="ILlmTestServerRuntime"/>.
/// </summary>
public interface ILlmTestServerRuntimeRequestScope
{
    IDisposable BeginRequest(string requestId, string? conversationId = null, string? providerNamespace = null, Action<string>? modelObserved = null);
}
