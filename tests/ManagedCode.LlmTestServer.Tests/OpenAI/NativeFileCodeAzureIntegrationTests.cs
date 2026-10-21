using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeFileCodeIntegrationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AzureResponses_PreservesNativeFileCodeAndContainerIdentityAsync(bool code, bool streaming)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration(code));
        var request = Request(code);
        request["stream"] = streaming;
        using var response = await http.PostAsJsonAsync("/azure-openai/openai/v1/responses", request);
        response.EnsureSuccessStatusCode();
        JsonElement terminal;
        if (streaming)
        {
            terminal = (await response.Content.ReadAsStringAsync()).Split('\n')
                .Where(line => line.StartsWith("data: ", StringComparison.Ordinal) && line != "data: [DONE]")
                .Select(line => JsonDocument.Parse(line[6..]).RootElement.Clone())
                .Single(frame => frame.GetProperty("type").GetString() == "response.completed").GetProperty("response");
        }
        else { terminal = await response.Content.ReadFromJsonAsync<JsonElement>(); }
        await Assert.That(terminal.GetProperty("output")[0].GetProperty("type").GetString())
            .IsEqualTo(code ? "code_interpreter_call" : "file_search_call");
        if (code) { await Assert.That(terminal.GetProperty("output")[0].GetProperty("container_id").GetString()).IsEqualTo(_containerId); }
        await Assert.That(terminal.TryGetProperty("tool_usage", out _)).IsFalse();
    }
}
