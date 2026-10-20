using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Anthropic;

public sealed partial class AnthropicNativeWebSearchIntegrationTests
{
    [Test]
    [Arguments("numericId")]
    [Arguments("longId")]
    [Arguments("emptyQuery")]
    [Arguments("wrongResultShape")]
    [Arguments("unknownError")]
    [Arguments("nullOpaque")]
    [Arguments("nativeUser")]
    [Arguments("unknownCitation")]
    public async Task MalformedNativeHistory_Returns400AndPreservesQueueAsync(string defect)
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var control = new LlmTestServerClient(http); await control.ConfigureAsync(Configuration(2));
        var first = await CompleteAsync(http);
        var content = JsonNode.Parse(first.GetProperty("content").GetRawText())!;
        var role = "assistant";
        switch (defect)
        {
            case "numericId": content[0]!["id"] = 123; break;
            case "longId": content[0]!["id"] = new string('x', 129); break;
            case "emptyQuery": content[0]!["input"]!["query"] = ""; break;
            case "wrongResultShape": content[1]!["content"] = "not a result"; break;
            case "unknownError": content[1]!["content"] = new JsonObject { ["type"] = "web_search_tool_result_error", ["error_code"] = "invented" }; break;
            case "nullOpaque": content[1]!["content"]![0]!["encrypted_content"] = null; break;
            case "nativeUser": role = "user"; break;
            case "unknownCitation": content[2]!["citations"]![0]!["type"] = "invented"; break;
        }
        var body = Request(); ((JsonArray)body["messages"]!).Add(new JsonObject { ["role"] = role, ["content"] = content });
        ((JsonArray)body["messages"]!).Add(new JsonObject { ["role"] = "user", ["content"] = "follow" });
        using var rejected = await http.PostAsJsonAsync("/anthropic/v1/messages", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertContentAsync((await CompleteAsync(http)).GetProperty("content"));
    }

    [Test]
    [Arguments("/openai/v1/responses")]
    [Arguments("/openai/v1/chat/completions")]
    public async Task AnthropicNativeDeclarations_DoNotLeakIntoOpenAiNamespacesAsync(string path)
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        await new LlmTestServerClient(http).ConfigureAsync(Configuration());
        var body = Request(); body["input"] = "start";
        using var rejected = await http.PostAsJsonAsync(path, body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertContentAsync((await CompleteAsync(http)).GetProperty("content"));
    }

    [Test]
    public async Task ProviderAuthenticationFailure_DoesNotConsumeNativeFixtureAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var configuration = Configuration(); configuration = configuration with { RequiredBearerToken = "provider-key" };
        var control = new LlmTestServerClient(http, "provider-key"); await control.ConfigureAsync(configuration);
        http.DefaultRequestHeaders.Remove("x-api-key"); http.DefaultRequestHeaders.Add("x-api-key", "wrong-key");
        using var rejected = await http.PostAsJsonAsync("/anthropic/v1/messages", Request());
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        http.DefaultRequestHeaders.Remove("x-api-key"); http.DefaultRequestHeaders.Add("x-api-key", "provider-key");
        await AssertContentAsync((await CompleteAsync(http)).GetProperty("content"));
    }
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnicodeDomainPaths_AllowColonAndAtSignAndApplyBlockingWithoutConsumingFixtureAsync(bool blocked)
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        const string domainPath = "example.com/джерело:версія/@author";
        const string sourceUrl = "https://example.com/джерело:версія/@author/article";
        var items = Items();
        items[1] = ((LlmTestServerResponseServerToolResultItem)items[1]) with
        { Result = new LlmTestServerWebSearchResults { Results = [new() { Url = sourceUrl, Title = _title, EncryptedContent = _opaque }] } };
        items[2] = ((LlmTestServerResponseMessageItem)items[2]) with
        { Citations = [new LlmTestServerAnthropicWebSearchCitation { Url = sourceUrl, Title = _title, EncryptedIndex = _index, CitedText = "Джерело" }] };
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseChatModel(_model).UseChatScenario("unicode-domain-path", scenario =>
            scenario.ForModel(_model).RespondsWithItems(items)));
        var body = Request(); var tool = body["tools"]![0]!;
        tool[blocked ? "blocked_domains" : "allowed_domains"] = new JsonArray(domainPath);
        if (blocked)
        {
            using var rejected = await http.PostAsJsonAsync("/anthropic/v1/messages", body);
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
            tool["blocked_domains"] = new JsonArray("example.com/інше:значення/@author");
        }
        var response = await CompleteAsync(http, body);
        await Assert.That(response.GetProperty("content")[1].GetProperty("content")[0].GetProperty("url").GetString()).IsEqualTo(sourceUrl);
        await Assert.That(response.GetProperty("content")[2].GetProperty("citations")[0].GetProperty("url").GetString()).IsEqualTo(sourceUrl);
        await Assert.That(response.GetProperty("content")[2].GetProperty("citations")[0].GetProperty("cited_text").GetString()).IsEqualTo("Джерело");
        await Assert.That(response.GetProperty("usage").GetProperty("server_tool_use").GetProperty("web_search_requests").GetInt32()).IsEqualTo(1);
    }

}
