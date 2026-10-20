using Microsoft.Extensions.Configuration;

namespace ManagedCode.LlmTestServer.Hosting;

/// <summary>Configuration names shared by packaged hosts and consumer applications.</summary>
public static class LlmTestServerHostingConfiguration
{
    public const string RequiredBearerTokenKey = "LlmTestServer:RequiredBearerToken";
    public const string LegacyRequiredBearerTokenKey = "LlmTck:RequiredBearerToken";

    /// <summary>The new key takes precedence, including an explicitly empty value.</summary>
    public static string? GetRequiredBearerToken(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration[RequiredBearerTokenKey] ?? configuration[LegacyRequiredBearerTokenKey];
    }
}
