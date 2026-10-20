using System.Net;
using System.Net.Http.Headers;
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;

namespace ManagedCode.LlmTestServer.Tests.AspireIntegration;

public sealed class AspireAuthorizationIntegrationTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("test-server-key")]
    [Timeout(120_000)]
    public async Task PackagedServer_EnforcesCanonicalApiKeyConfigurationAsync(
        string? apiKey, CancellationToken cancellationToken)
    {
        var builder = DistributedApplicationTestingBuilder.Create([]);
        var resource = builder.AddLlmTestServer("authorization");
        if (!string.IsNullOrEmpty(apiKey))
        {
            resource.WithApiKey(apiKey);
        }
        else if (apiKey is not null)
        {
            resource.WithEnvironment("LlmTestServer__RequiredBearerToken", apiKey);
        }

        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("authorization", cancellationToken);
        using var http = app.CreateHttpClient("authorization", "http");
        foreach (var path in new[] { "/admin-api/models", "/openai/v1/models", "/groq/openai/v1/models", "/openrouter/api/v1/models", "/deepseek/models" })
        {
            http.DefaultRequestHeaders.Authorization = null;
            using var missing = await http.GetAsync(path, cancellationToken);
            await Assert.That(missing.StatusCode).IsEqualTo(string.IsNullOrEmpty(apiKey)
                ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
            using var rejected = await http.GetAsync(path, cancellationToken);
            await Assert.That(rejected.StatusCode).IsEqualTo(string.IsNullOrEmpty(apiKey)
                ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);

            http.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(apiKey)
                ? null : new AuthenticationHeaderValue("Bearer", apiKey);
            using var accepted = await http.GetAsync(path, cancellationToken);
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await accepted.Content.ReadAsStringAsync(cancellationToken)).Contains("gpt-4.1-mini");
        }
    }
}
