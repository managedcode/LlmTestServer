using System.Reflection;

namespace ManagedCode.LlmTestServer.Aspire;

public static class LlmTestServerContainerImageTags
{
    private const string _fallbackTag = "0.1.1";

    public const string Registry = "ghcr.io";

    public const string Image = "managedcode/llm-test-server";

    public static string Tag { get; } = GetPackageVersion();

    private static string GetPackageVersion()
    {
        var version = typeof(LlmTestServerContainerImageTags)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            return _fallbackTag;
        }

        var metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        return metadataIndex > 0 ? version[..metadataIndex] : version;
    }
}
