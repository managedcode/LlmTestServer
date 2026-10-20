using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace ManagedCode.LlmTestServer.Aspire;

public sealed class LlmTestServerResource(string name, string command, string workingDirectory)
    : ExecutableResource(name, command, workingDirectory),
        IResourceWithServiceDiscovery
{
    public const string DefaultName = "llm-test-server";

    public const string HttpEndpointName = "http";
}

public sealed class LlmTestServerContainerResource(string name)
    : ContainerResource(name),
        IResourceWithServiceDiscovery
{
    public const int HttpPort = 8080;
}
