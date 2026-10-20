using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ManagedCode.LlmTestServer.Tests.TestSupport;

internal static class LlmTestServerTestHost
{
    public static async Task<IHost> StartAsync(
        Action<LlmTestServerConfigurationBuilder>? configure = null,
        Action<IServiceCollection>? configureServices = null,
        CancellationToken cancellationToken = default
    )
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder => webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddLlmTestServer(configure);
                        configureServices?.Invoke(services);
                    })
                    .Configure(application =>
                    {
                        application.UseRouting();
                        application.UseEndpoints(endpoints => endpoints.MapLlmTestServer());
                    }))
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }
}
