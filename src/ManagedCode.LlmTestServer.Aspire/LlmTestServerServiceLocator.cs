namespace ManagedCode.LlmTestServer.Aspire;

internal static class LlmTestServerServiceLocator
{
    private const string _serviceDirectoryName = "llm-test-server-service";
    private const string _serviceAssemblyName = "ManagedCode.LlmTestServer.Service.dll";

    public static string GetServiceDirectory()
    {
        var assemblyDirectory = AppContext.BaseDirectory;
        var packageServiceDirectory = Path.Combine(assemblyDirectory, _serviceDirectoryName);
        if (ContainsServiceAssembly(packageServiceDirectory))
        {
            return packageServiceDirectory;
        }

        var sourceServiceDirectory = FindSourceServiceDirectory(assemblyDirectory);
        if (sourceServiceDirectory is not null)
        {
            return sourceServiceDirectory;
        }

        throw new InvalidOperationException(
            $"The packaged LLM Test Server service executable was not found at '{packageServiceDirectory}'. "
                + "Restore the ManagedCode.LlmTestServer.Aspire package or use AddLlmTestServerContainer() for the container-backed resource."
        );
    }

    private static bool ContainsServiceAssembly(string directory)
    {
        return File.Exists(Path.Combine(directory, _serviceAssemblyName));
    }

    private static string? FindSourceServiceDirectory(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            var solutionPath = Path.Combine(current.FullName, "ManagedCode.LlmTestServer.slnx");
            if (File.Exists(solutionPath))
            {
                foreach (var configuration in new[] { "Release", "Debug" })
                {
                    var candidate = Path.Combine(
                        current.FullName,
                        "samples",
                        "ManagedCode.LlmTestServer.Service",
                        "bin",
                        configuration,
                        "net10.0"
                    );
                    if (ContainsServiceAssembly(candidate))
                    {
                        return candidate;
                    }
                }
            }

            current = current.Parent;
        }

        return null;
    }
}
