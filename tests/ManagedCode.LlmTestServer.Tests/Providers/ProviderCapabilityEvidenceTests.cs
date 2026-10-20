using System.Net;
using System.Net.Http.Json;
using System.Text;
using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Tests.Regression;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Providers;

public sealed class ProviderCapabilityEvidenceTests
{
    [Test]
    [Arguments(LlmTestServerCompatibilityTags.SystemOne)]
    [Arguments(LlmTestServerCompatibilityTags.Cloudflare)]
    [Arguments(LlmTestServerCompatibilityTags.OpenAI)]
    [Arguments(LlmTestServerCompatibilityTags.AzureOpenAI)]
    [Arguments(LlmTestServerCompatibilityTags.MicrosoftFoundry)]
    [Arguments(LlmTestServerCompatibilityTags.Anthropic)]
    [Arguments(LlmTestServerCompatibilityTags.Gemini)]
    [Arguments(LlmTestServerCompatibilityTags.Groq)]
    [Arguments(LlmTestServerCompatibilityTags.Mistral)]
    [Arguments(LlmTestServerCompatibilityTags.Ollama)]
    [Arguments(LlmTestServerCompatibilityTags.Cohere)]
    [Arguments(LlmTestServerCompatibilityTags.Bedrock)]
    [Arguments(LlmTestServerCompatibilityTags.OpenRouter)]
    [Arguments(LlmTestServerCompatibilityTags.DeepSeek)]
    [Arguments(LlmTestServerCompatibilityTags.Perplexity)]
    public async Task ClaimedToolAndSchemaCases_ExecutePositiveAndNegativeHttpEvidenceAsync(string provider)
    {
        var profile = LlmTestServerProviderFixtures.GetProfiles().Single(item => item.Id == provider);
        foreach (var operation in profile.ApiContract.Operations.Where(operation => operation.ImplementedByHosting))
        {
            foreach (var capability in operation.Capabilities.Where(capability => capability is LlmTestServerProviderCapability.Tools or LlmTestServerProviderCapability.StructuredOutput))
            {
                var streamingOnly = operation.Id is LlmTestServerProviderOperationIds.Gemini.ModelsStreamGenerateContent or LlmTestServerProviderOperationIds.Bedrock.ConverseStream;
                var modes = streamingOnly ? new[] { true } : operation.SupportsStreaming ? [false, true] : [false];
                foreach (var stream in modes)
                {
                    await VerifyCaseAsync(provider, operation, capability, stream);
                }
            }
        }
    }

    private static async Task VerifyCaseAsync(string provider, LlmTestServerProviderOperation operation, LlmTestServerProviderCapability capability, bool stream)
    {
        var caseId = $"{provider}/{operation.Id}/{capability}/{(stream ? "stream" : "json")}";
        var tools = capability == LlmTestServerProviderCapability.Tools;
        const string answer = "{\"city\":\"Paris\"}";
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario(caseId, s =>
        {
            if (tools) { s.CallsTool("weather", answer); }
            else { s.RespondsJson(answer); }
        }));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var format = provider switch
        {
            LlmTestServerCompatibilityTags.Anthropic => 2,
            LlmTestServerCompatibilityTags.Gemini => 3,
            LlmTestServerCompatibilityTags.Ollama => 4,
            LlmTestServerCompatibilityTags.Cohere => 5,
            LlmTestServerCompatibilityTags.Bedrock => 6,
            _ => operation.Id is LlmTestServerProviderOperationIds.OpenAI.ResponsesCreate or LlmTestServerProviderOperationIds.AzureOpenAI.V1ResponsesCreate ? 1 : 0,
        };
        var body = ToolFixtureEndpointTests.Request(format, tools);
        body["stream"] = stream;
        var path = operation.Path.Replace("{model}", "gpt-4.1-mini", StringComparison.Ordinal)
            .Replace("{modelId}", "gpt-4.1-mini", StringComparison.Ordinal)
            .Replace("{deployment}", "gpt-4.1-mini", StringComparison.Ordinal);
        if (operation.ApiVersion is { } version && version != "v1") { path += "?api-version=" + version; }
        if (provider == LlmTestServerCompatibilityTags.Gemini && stream) { path += "?alt=sse"; }
        var invalid = body.ToJsonString().Replace("Paris", "London", StringComparison.Ordinal);
        using var rejected = await http.PostAsync(path, new StringContent(invalid, Encoding.UTF8, "application/json"));
        await Assert.That((caseId + "/reject", rejected.StatusCode)).IsEqualTo((caseId + "/reject", HttpStatusCode.Conflict));
        using var accepted = await http.PostAsync(path, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        await Assert.That((caseId + "/accept", accepted.StatusCode)).IsEqualTo((caseId + "/accept", HttpStatusCode.OK));
        var content = await accepted.Content.ReadAsStringAsync();
        await Assert.That(content).Contains(tools ? "weather" : "Paris");
        if (stream)
        {
            var marker = format switch { 1 => "response.completed", 2 => "message_stop", 3 => "data:", 4 => "\"done\":true", 5 => "message-end", 6 => "messageStop", _ => "[DONE]" };
            await Assert.That(content).Contains(marker);
        }
        var summary = (await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!;
        await Assert.That(summary.ErrorsReturned).IsEqualTo(1);
        await Assert.That(summary.Matched).IsEqualTo(1);
        await Assert.That(summary.Events.All(entry => entry.ScenarioId == caseId)).IsTrue();
    }

}
