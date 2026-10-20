namespace ManagedCode.LlmTestServer.Hosting;

public sealed class LlmTestServerSessionOptions
{
    public int MaxSessions { get; set; } = 32;
    public long MaxReservedStateBytes { get; set; } = 512L * 1024 * 1024;
    public int MaxConfigurationBytes { get; set; } = 1024 * 1024;
}
