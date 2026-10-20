namespace ManagedCode.LlmTestServer.Runtime;

public enum LlmTestServerEventKind
{
    Matched,
    Unmatched,
    ModelNotFound,
    AuthFailed,
    ScenarioExhausted,
    ErrorReturned,
}
