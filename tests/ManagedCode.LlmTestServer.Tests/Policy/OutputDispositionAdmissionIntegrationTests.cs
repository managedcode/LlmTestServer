using System.ClientModel;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI.Chat;

namespace ManagedCode.LlmTestServer.Tests.Policy;

public sealed partial class OutputDispositionIntegrationTests
{
    [Test]
    [Arguments("kind-zero")]
    [Arguments("kind-unknown")]
    [Arguments("category")]
    [Arguments("explanation")]
    [Arguments("error")]
    [Arguments("delivery")]
    [Arguments("tools")]
    [Arguments("items")]
    [Arguments("chunks")]
    public async Task InvalidPolicyConfigurationIsAtomicAndPriorFixtureStillRunsThroughOfficialSdkAsync(string defect)
    {
        await using var host = await StartAsync(config => config.AddChatScenario("preserved", scenario => scenario.Responds(_recovery)));
        using var http = host.Client();
        var response = JsonNode.Parse("""{"content":"visible","outputDisposition":{"kind":"filtered"}}""")!.AsObject();
        var disposition = response["outputDisposition"]!.AsObject();
        switch (defect)
        {
            case "kind-zero": disposition["kind"] = 0; break;
            case "kind-unknown": disposition["kind"] = 999; break;
            case "category": disposition["category"] = new string('x', 257); break;
            case "explanation": disposition["explanation"] = new string('x', 4097); break;
            case "error": response["error"] = JsonNode.Parse("""{"statusCode":500,"code":"fixture-error","message":"explicit"}"""); break;
            case "delivery": response["streamDelivery"] = JsonNode.Parse("""{"mode":"earlyCleanEnd"}"""); break;
            case "tools": response["toolCalls"] = JsonNode.Parse("""[{"id":"call_policy","name":"weather","argumentsJson":"{}"}]"""); break;
            case "items": response["content"] = ""; response["outputItems"] = JsonNode.Parse("""[{"kind":"message","id":"msg_policy","content":"visible"}]"""); break;
            case "chunks": response["streamChunks"] = JsonNode.Parse("""["different"]"""); break;
        }
        var body = new JsonObject
        {
            ["models"] = JsonNode.Parse("""[{"id":"gpt-4.1-mini","kind":"chat"}]"""),
            ["chatScenarios"] = new JsonArray(new JsonObject { ["id"] = "invalid", ["responses"] = new JsonArray(response) }),
        };
        using var rejected = await http.PostAsJsonAsync("/admin-api/configure", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var recovered = (await ChatSdk(http).CompleteChatAsync([new UserChatMessage("preserved")])).Value;
        await Assert.That(recovered.Content.Single().Text).IsEqualTo(_recovery);
        await Assert.That((await new LlmTestServerClient(http).GetAssertionsAsync()).Matched).IsEqualTo(1);
    }

    [Test]
    [Arguments("provider")]
    [Arguments("tools")]
    [Arguments("schema")]
    public async Task UnsupportedPolicyRequestFailsBeforeConsumptionAndNativePlainRequestRecoversAsync(string defect)
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, LlmTestServerKnownModelIds.Gpt41Mini, LlmTestServerOutputDispositionKind.Filtered));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        var options = new ChatCompletionOptions();
        if (defect == "tools") { options.Tools.Add(ChatTool.CreateFunctionTool("weather")); }
        if (defect == "schema") { options.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(); }
        ClientResultException? rejected = null;
        try { await ChatSdk(http, defect == "provider" ? "groq/openai" : "openai").CompleteChatAsync([new UserChatMessage("policy")], options); }
        catch (ClientResultException exception) { rejected = exception; }
        await Assert.That(rejected?.Status).IsEqualTo(409);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        var first = (await ChatSdk(http).CompleteChatAsync([new UserChatMessage("policy")])).Value;
        await Assert.That(first.FinishReason).IsEqualTo(ChatFinishReason.ContentFilter);
        await Assert.That(first.Content.Single().Text).IsEqualTo(_text);
        var next = (await ChatSdk(http).CompleteChatAsync([new UserChatMessage("next")])).Value;
        await Assert.That(next.Content.Single().Text).IsEqualTo(_recovery);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
    }

    [Test]
    [Arguments("anthropic")]
    [Arguments("gemini")]
    public async Task UnsupportedNativePolicyPairIs409AndDoesNotConsumeSelectedFixtureAsync(string provider)
    {
        var model = provider == "anthropic"
            ? JsonSerializer.SerializeToElement(global::Anthropic.Models.Messages.Model.ClaudeSonnet5_5).GetString()! : _geminiModel;
        var kind = provider == "anthropic" ? LlmTestServerOutputDispositionKind.Filtered : LlmTestServerOutputDispositionKind.Refused;
        await using var host = await StartAsync(config => ConfigureQueue(config, model, kind));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        // This malformed pairing is checked over HTTP; the supported recovery uses the original official SDK.
        var path = provider == "anthropic" ? "/anthropic/v1/messages" : "/gemini/v1beta/models/" + model + ":generateContent";
        object body = provider == "anthropic"
            ? new { model, max_tokens = 256, messages = new[] { new { role = "user", content = "policy" } } }
            : new { contents = new[] { new { role = "user", parts = new[] { new { text = "policy" } } } } };
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (provider == "anthropic")
        { request.Headers.Add(ManagedCode.LlmTestServer.Anthropic.AnthropicWireMapper.VersionHeaderName, ManagedCode.LlmTestServer.Anthropic.AnthropicWireMapper.SupportedVersion); }
        using var rejected = await http.SendAsync(request);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        var supported = (await ChatSdk(http, model: model).CompleteChatAsync([new UserChatMessage("policy")])).Value;
        await Assert.That(supported.FinishReason).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Filtered ? ChatFinishReason.ContentFilter : ChatFinishReason.Stop);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(1);
        var recovery = (await ChatSdk(http, model: model).CompleteChatAsync([new UserChatMessage("next")])).Value;
        await Assert.That(recovery.Content.Single().Text).IsEqualTo(_recovery);
    }
}
