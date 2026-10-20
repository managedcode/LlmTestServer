using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace ManagedCode.LlmTestServer.Aspire;

public static class LlmTestServerAspireExtensions
{
    public static IResourceBuilder<LlmTestServerResource> AddLlmTestServer(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = LlmTestServerResource.DefaultName
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var serviceDirectory = LlmTestServerServiceLocator.GetServiceDirectory();
        var resource = new LlmTestServerResource(name, "dotnet", serviceDirectory);

        return builder.AddResource(resource)
            .WithArgs("ManagedCode.LlmTestServer.Service.dll")
            .WithHttpEndpoint(name: LlmTestServerResource.HttpEndpointName, env: "PORT")
            .WithHttpHealthCheck("/health");
    }

    public static IResourceBuilder<LlmTestServerContainerResource> AddLlmTestServerContainer(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = LlmTestServerResource.DefaultName
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var resource = new LlmTestServerContainerResource(name);

        return builder.AddResource(resource)
            .WithImage(LlmTestServerContainerImageTags.Image, LlmTestServerContainerImageTags.Tag)
            .WithImageRegistry(LlmTestServerContainerImageTags.Registry)
            .WithHttpEndpoint(
                targetPort: LlmTestServerContainerResource.HttpPort,
                name: LlmTestServerResource.HttpEndpointName
            )
            .WithHttpHealthCheck("/health");
    }

    public static EndpointReference GetHttpEndpoint(
        this IResourceBuilder<LlmTestServerResource> builder
    )
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.GetEndpoint(LlmTestServerResource.HttpEndpointName);
    }

    public static ReferenceExpression GetOpenAiEndpoint(
        this IResourceBuilder<LlmTestServerResource> builder
    )
    {
        ArgumentNullException.ThrowIfNull(builder);

        return ReferenceExpression.Create($"{builder.GetHttpEndpoint()}/openai/v1");
    }

    public static EndpointReference GetHttpEndpoint(
        this IResourceBuilder<LlmTestServerContainerResource> builder
    )
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.GetEndpoint(LlmTestServerResource.HttpEndpointName);
    }

    public static ReferenceExpression GetOpenAiEndpoint(
        this IResourceBuilder<LlmTestServerContainerResource> builder
    )
    {
        ArgumentNullException.ThrowIfNull(builder);

        return ReferenceExpression.Create($"{builder.GetHttpEndpoint()}/openai/v1");
    }

    public static IResourceBuilder<TResource> WithApiKey<TResource>(
        this IResourceBuilder<TResource> builder,
        string apiKey
    )
        where TResource : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        return builder.WithEnvironment("LlmTestServer__RequiredBearerToken", apiKey);
    }

}
