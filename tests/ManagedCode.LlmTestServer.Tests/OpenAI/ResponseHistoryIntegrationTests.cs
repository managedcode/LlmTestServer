using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed class ResponseHistoryIntegrationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StoredResponse_IsUnavailableAcrossCredentialsProvidersAndResetAsync(bool reconfigure)
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config
            .AddChatScenario("history", scenario => scenario.Responds("first").Responds("second")));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "first-key");
        using var first = await http.PostAsJsonAsync("/openai/v1/responses", new { model = "gpt-4.1-mini", input = "hello" });
        first.EnsureSuccessStatusCode();
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "second-key");
        using var otherCredential = await http.PostAsJsonAsync("/openai/v1/responses", new { model = "gpt-4.1-mini", input = "continue", previous_response_id = id });
        await Assert.That(otherCredential.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await otherCredential.Content.ReadAsStringAsync()).Contains("response_not_found");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "first-key");
        using var otherProvider = await http.PostAsJsonAsync("/azure-openai/openai/v1/responses", new { model = "gpt-4.1-mini", input = "continue", previous_response_id = id });
        await Assert.That(otherProvider.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var cleared = reconfigure
            ? await http.PostAsJsonAsync("/admin-api/configure", LlmTestServerConfiguration.CreateDefault())
            : await http.PostAsync("/admin-api/reset", null);
        cleared.EnsureSuccessStatusCode();
        using var after = await http.PostAsJsonAsync("/openai/v1/responses", new { model = "gpt-4.1-mini", input = "continue", previous_response_id = id });
        await Assert.That(after.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await after.Content.ReadAsStringAsync()).Contains("response_not_found");
    }

    [Test]
    [Arguments(0, 1048576)]
    [Arguments(5, 1)]
    [Arguments(1, 1048576)]
    public async Task ResponseCapacity_RejectsOverflowAndStoreFalseRemainsAvailableAsync(int maxResponses, long maxBytes)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(config => config.UseGpt41Mini()
            .UseChatResponseCapacity(maxResponses, maxBytes)
            .UseChatScenario("capacity", scenario => scenario.Responds("one").Responds("two").Responds("three")));
        if (maxResponses == 1)
        {
            using var first = await http.PostAsJsonAsync("/openai/v1/responses", new { model = "gpt-4.1-mini", input = "hello" });
            first.EnsureSuccessStatusCode();
        }
        using var overflow = await http.PostAsJsonAsync("/openai/v1/responses", new { model = "gpt-4.1-mini", input = "hello" });
        await Assert.That(overflow.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(await overflow.Content.ReadAsStringAsync()).Contains("llm_test_server_response_capacity_exceeded");
        using var transient = await http.PostAsJsonAsync("/openai/v1/responses", new { model = "gpt-4.1-mini", input = "hello", store = false });
        transient.EnsureSuccessStatusCode();
        var id = (await transient.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        using var continuation = await http.PostAsJsonAsync("/openai/v1/responses", new { model = "gpt-4.1-mini", input = "hello", previous_response_id = id });
        await Assert.That(continuation.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }
}
