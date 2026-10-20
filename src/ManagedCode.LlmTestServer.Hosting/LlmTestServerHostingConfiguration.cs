using Microsoft.Extensions.Configuration;

namespace ManagedCode.LlmTestServer.Hosting;

/// <summary>Configuration names shared by packaged hosts and consumer applications.</summary>
public static class LlmTestServerHostingConfiguration
{
    public const string RequiredBearerTokenKey = "LlmTestServer:RequiredBearerToken";

    /// <summary>Reads the canonical host authentication setting.</summary>
    public static string? GetRequiredBearerToken(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration[RequiredBearerTokenKey];
    }
}
