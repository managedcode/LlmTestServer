using System.Net;
using ManagedCode.LlmTestServer.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace ManagedCode.LlmTestServer.Tests.Hosting;

public sealed class ServiceHealthIntegrationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ServiceDefaults_ServeHealthAndLivenessOverHttpAsync(bool healthChecksOnly)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        if (healthChecksOnly) { builder.AddDefaultHealthChecks(); }
        else { builder.AddServiceDefaults(); }
        await using var app = builder.Build();
        app.MapDefaultEndpoints();
        await app.StartAsync();
        using var http = app.GetTestClient();
        foreach (var path in new[] { "/health", "/alive" })
        {
            using var response = await http.GetAsync(path);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("Healthy");
        }
    }
}
