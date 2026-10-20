namespace ManagedCode.LlmTestServer.Runtime;

/// <summary>
/// Optional provider-neutral correlation scope used by hosts to associate runtime events with an
/// external request without changing the operation methods on <see cref="ILlmTestServerRuntime"/>.
/// </summary>
public interface ILlmTestServerRuntimeRequestScope
{
    IDisposable BeginRequest(string requestId);
}
