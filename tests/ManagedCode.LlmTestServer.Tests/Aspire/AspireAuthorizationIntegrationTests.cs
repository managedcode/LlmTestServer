using System.Net;
using System.Net.Http.Headers;
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;

namespace ManagedCode.LlmTestServer.Tests.AspireIntegration;

public sealed class AspireAuthorizationIntegrationTests
{
    [Test]
    [Arguments(null, null, null)]
    [Arguments(null, "legacy-token", "legacy-token")]
    [Arguments("new-token", null, "new-token")]
    [Arguments("new-token", "legacy-token", "new-token")]
    [Arguments("", "legacy-token", "")]
    [Timeout(120_000)]
    public async Task PackagedServer_EnforcesConfiguredTokenAndMigrationPrecedenceAsync(
        string? current, string? legacy, string? expected, CancellationToken cancellationToken)
    {
        var builder = DistributedApplicationTestingBuilder.Create([]);
        var resource = builder.AddLlmTestServer("authorization");
        if (current is not null)
        {
            resource.WithEnvironment("LlmTestServer__RequiredBearerToken", current);
        }
        if (legacy is not null)
        {
            resource.WithEnvironment("LlmTck__RequiredBearerToken", legacy);
        }

        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("authorization", cancellationToken);
        using var http = app.CreateHttpClient("authorization", "http");
        foreach (var path in new[] { "/admin-api/models", "/openai/v1/models", "/groq/openai/v1/models", "/openrouter/api/v1/models", "/deepseek/models" })
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
            using var rejected = await http.GetAsync(path, cancellationToken);
            await Assert.That(rejected.StatusCode).IsEqualTo(string.IsNullOrEmpty(expected)
                ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);

            http.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(expected)
                ? null : new AuthenticationHeaderValue("Bearer", expected);
            using var accepted = await http.GetAsync(path, cancellationToken);
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await accepted.Content.ReadAsStringAsync(cancellationToken)).Contains("gpt-4.1-mini");
        }
    }
}
