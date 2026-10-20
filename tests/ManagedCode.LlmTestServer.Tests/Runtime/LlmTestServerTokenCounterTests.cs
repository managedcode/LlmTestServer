using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Runtime;

public sealed class LlmTestServerTokenCounterTests
{
    [Test]
    [Arguments("hello,world", 3)]
    [Arguments("Text tokenization is the process of splitting a string into a list of tokens.", 16)]
    [Arguments("antidisestablishmentarianism", 6)]
    public async Task ChatResponse_ReportsConfiguredOutputTokenUsageThroughHttpAsync(
        string text,
        int expectedTokens
    )
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
            .AddChatScenario("usage", scenario => scenario.Responds(text)));
        using var http = host.GetTestClient();
        using var response = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
        {
            model = "gpt-4.1-mini",
            messages = new[] { new { role = "user", content = "Count the output tokens." } },
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString())
            .IsEqualTo(text);
        await Assert.That(body.GetProperty("usage").GetProperty("completion_tokens").GetInt32())
            .IsEqualTo(expectedTokens);
        var summary = await http.GetFromJsonAsync<JsonElement>("/admin-api/assertions");
        await Assert.That(summary.GetProperty("outputTokens").GetInt32()).IsEqualTo(expectedTokens);
    }
}
