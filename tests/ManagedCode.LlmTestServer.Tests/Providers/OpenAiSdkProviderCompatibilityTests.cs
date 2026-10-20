using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using OpenAI;
using OpenAI.Chat;

namespace ManagedCode.LlmTestServer.Tests.Providers;

public sealed class OpenAiSdkProviderCompatibilityTests
{
    private const string _key = "sdk-provider-key";

    [Test]
    [Arguments("/groq/openai/v1", false)]
    [Arguments("/groq/openai/v1", true)]
    [Arguments("/openrouter/api/v1", false)]
    [Arguments("/openrouter/api/v1", true)]
    [Arguments("/mistral/v1", false)]
    [Arguments("/mistral/v1", true)]
    [Arguments("/deepseek/v1", false)]
    [Arguments("/deepseek/v1", true)]
    public async Task OfficialOpenAiClient_CompletesCorrelatedFunctionRoundThroughCompatibleProviderAsync(string prefix, bool stream)
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.RequireBearerToken(_key)
            .AddModel(LlmTestServerKnownModelIds.Gpt55, LlmTestServerModelKind.Chat)
            .AddConversation("provider-sdk", graph => graph.ForModel(LlmTestServerKnownModelIds.Gpt55)
                .Step("call", step => step.WhenLatestUserEquals("weather")
                    .CallsTool("weather", "{\"city\":\"Paris\"}", "call_provider_sdk").Then("result"))
                .Step("result", step => step.WhenToolResult("weather", "call_provider_sdk")
                    .WhenLatestTextContains("18 degrees").Responds("Paris is 18 degrees.", "Paris is ", "18 degrees.").Complete())));
        using var http = host.GetTestClient();
        using var conversation = new LlmTestServerClient(http, _key).ForConversation("sdk-round");
        var sdk = CreateSdk(conversation.HttpClient, prefix);
        var options = new ChatCompletionOptions
        {
            Tools = { ChatTool.CreateFunctionTool("weather", "Weather fixture", BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}},\"required\":[\"city\"]}")) },
            Temperature = 0,
        };
        List<ChatMessage> messages = [new UserChatMessage("weather")];
        var called = (await sdk.CompleteChatAsync(messages, options)).Value;
        var call = called.ToolCalls.Single();
        await Assert.That(call.Id).IsEqualTo("call_provider_sdk");
        await Assert.That(call.FunctionName).IsEqualTo("weather");
        await Assert.That(call.FunctionArguments.ToString()).IsEqualTo("{\"city\":\"Paris\"}");
        messages.Add(new AssistantChatMessage(called));
        messages.Add(new ToolChatMessage(call.Id, "18 degrees"));
        var output = new StringBuilder();
        if (stream)
        {
            await foreach (var update in sdk.CompleteChatStreamingAsync(messages, options))
            {
                foreach (var part in update.ContentUpdate) { output.Append(part.Text); }
            }
        }
        else { output.Append((await sdk.CompleteChatAsync(messages, options)).Value.Content.Single().Text); }
        await Assert.That(output.ToString()).IsEqualTo("Paris is 18 degrees.");
        var state = (await conversation.Control.GetConversationsAsync()).Single();
        await Assert.That(state.ExecutedSteps).IsEqualTo(2);
        await Assert.That(state.IsTerminal).IsTrue();
        var evidence = (await conversation.Control.ExportRequestsAsync()).Evidence.Requests;
        await Assert.That(evidence.Count).IsEqualTo(2);
        await Assert.That(evidence.All(request => request.StatusCode == 200 && request.Path == prefix + "/chat/completions")).IsTrue();
        var result = evidence.Single(request => request.Messages.Any(message => message.Role == "tool"));
        await Assert.That(result.Messages.Single(message => message.Role == "tool").ToolCallId).IsEqualTo(call.Id);
        await Assert.That(result.Messages.Single(message => message.Role == "tool").Content).IsEqualTo("18 degrees");
        if (stream) { await Assert.That(result.TerminalMarkerObserved).IsTrue(); }
    }

    [Test]
    [Arguments("/groq/openai/v1", 429)]
    [Arguments("/groq/openai/v1", 500)]
    [Arguments("/openrouter/api/v1", 429)]
    [Arguments("/openrouter/api/v1", 500)]
    [Arguments("/mistral/v1", 429)]
    [Arguments("/mistral/v1", 500)]
    [Arguments("/deepseek/v1", 429)]
    [Arguments("/deepseek/v1", 500)]
    public async Task OfficialOpenAiClient_ObservesOneProviderErrorThenExplicitRecoveryAsync(string prefix, int status)
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.RequireBearerToken(_key)
            .AddModel(LlmTestServerKnownModelIds.Gpt55, LlmTestServerModelKind.Chat).AddChatScenario("sdk-error", scenario => scenario
                .ForModel(LlmTestServerKnownModelIds.Gpt55).Fails(status, "fixture_unavailable", "Scripted provider failure").Responds("SDK recovered")));
        using var http = host.GetTestClient();
        var sdk = CreateSdk(http, prefix);
        ClientResultException? failure = null;
        try { await sdk.CompleteChatAsync([new UserChatMessage("recover")]); }
        catch (ClientResultException exception) { failure = exception; }
        await Assert.That(failure?.Status).IsEqualTo(status);
        await Assert.That((await sdk.CompleteChatAsync([new UserChatMessage("recover")])).Value.Content.Single().Text).IsEqualTo("SDK recovered");
        var evidence = (await new LlmTestServerClient(http, _key).ExportRequestsAsync()).Evidence.Requests;
        await Assert.That(evidence.Count).IsEqualTo(2);
        await Assert.That(evidence.Select(request => request.StatusCode)).IsEquivalentTo(new int?[] { status, 200 });
    }

    private static ChatClient CreateSdk(HttpClient http, string prefix)
    {
        return new(LlmTestServerKnownModelIds.Gpt55, new ApiKeyCredential(_key), new OpenAIClientOptions
        { Endpoint = new Uri(http.BaseAddress!, prefix), Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) });
    }
}
