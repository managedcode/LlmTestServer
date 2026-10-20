using ManagedCode.LlmTestServer.Configuration;

namespace ManagedCode.LlmTestServer.Sessions;

public static class LlmTestServerSessionHeaders
{
    public const string Id = "X-Llm-Test-Server-Session";
    public const string AccessKey = "X-Llm-Test-Server-Session-Key";
}

public sealed class LlmTestServerSession
{
    public required string Id { get; init; }
    public required string AccessKey { get; init; }
    public long ReservedStateBytes { get; init; }
}

public sealed class LlmTestServerSessionCreateRequest
{
    public LlmTestServerConfiguration? Configuration { get; init; }
}
