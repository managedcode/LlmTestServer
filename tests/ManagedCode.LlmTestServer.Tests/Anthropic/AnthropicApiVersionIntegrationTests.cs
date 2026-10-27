using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using global::Anthropic;
using global::Anthropic.Core;
using global::Anthropic.Models.Messages;
using ManagedCode.LlmTestServer.Anthropic;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;

namespace ManagedCode.LlmTestServer.Tests.Anthropic;

public sealed class AnthropicApiVersionIntegrationTests
{
    private const string _route = "/anthropic/v1/messages";
    private const string _prompt = "Versioned Messages request";
    private const string _answer = "Verified versioned reply";
    private const string _key = "version-fixture-key";
    private static string ModelId => JsonSerializer.SerializeToElement(Model.ClaudeSonnet5_5).GetString()!;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Current_official_sdk_selects_its_version_and_reads_buffered_and_streaming_repliesAsync(bool streaming)
    {
        using var host = await StartAsync();
        using var http = host.GetTestClient();
        using var sdk = new AnthropicClient(new ClientOptions
        {
            HttpClient = http,
            BaseUrl = new Uri(http.BaseAddress!, "/anthropic").ToString(),
            ApiKey = _key,
            MaxRetries = 0,
            ResponseValidation = true
        });
        using var client = sdk.AsIChatClient(ModelId, 64);
        ChatMessage[] messages = [new(ChatRole.User, _prompt)];
        var response = streaming
            ? await client.GetStreamingResponseAsync(messages).ToChatResponseAsync()
            : await client.GetResponseAsync(messages);
        await Assert.That(response.Text).IsEqualTo(_answer);
    }

    [Test]
    [Arguments(AnthropicWireMapper.SupportedVersion)]
    [Arguments(AnthropicWireMapper.CurrentVersion)]
    public async Task Both_documented_api_versions_complete_real_requestsAsync(string version)
    {
        using var host = await StartAsync();
        using var http = host.GetTestClient();
        Authorize(http, version);
        await AssertValidAsync(http);
    }

    [Test]
    [Arguments("")]
    [Arguments("2026-10-22")]
    [Arguments("2023-06-01,2026-10-21")]
    public async Task Missing_unknown_and_combined_api_versions_fail_before_consuming_the_fixtureAsync(string version)
    {
        using var host = await StartAsync();
        using var http = host.GetTestClient();
        Authorize(http, version);
        using var response = await http.PostAsJsonAsync(_route, Body());
        await AssertInvalidAsync(response, AnthropicWireMapper.VersionHeaderName);
        http.DefaultRequestHeaders.Remove(AnthropicWireMapper.VersionHeaderName);
        http.DefaultRequestHeaders.Add(AnthropicWireMapper.VersionHeaderName, AnthropicWireMapper.CurrentVersion);
        await AssertValidAsync(http);
    }

    [Test]
    [Arguments("temperature")]
    [Arguments("top_p")]
    [Arguments("top_k")]
    [Arguments("assistant-document")]
    [Arguments("user-citations")]
    [Arguments("search-result-citations")]
    public async Task Current_api_rejects_removed_controls_and_forbidden_message_content_before_executionAsync(string field)
    {
        using var host = await StartAsync();
        using var http = host.GetTestClient();
        Authorize(http, AnthropicWireMapper.CurrentVersion);
        var body = Body();
        var message = body["messages"]![0]!;
        if (field is "temperature" or "top_p" or "top_k") { body[field] = 1; }
        else if (field == "assistant-document")
        {
            message["role"] = "assistant";
            message["content"] = JsonNode.Parse("""[{"type":"document","source":{"type":"text","media_type":"text/plain","data":"fixture"}}]""");
        }
        else if (field == "user-citations")
        { message["content"] = JsonNode.Parse("""[{"type":"text","text":"fixture","citations":[{}]}]"""); }
        else
        { message["content"] = JsonNode.Parse("""[{"type":"search_result","title":"Fixture","source":"https://example.com/source","content":[{"type":"text","text":"fixture","citations":[{}]}]}]"""); }
        using var response = await http.PostAsJsonAsync(_route, body);
        await AssertInvalidAsync(response, field switch
        {
            "assistant-document" => "document",
            "user-citations" or "search-result-citations" => "citations",
            _ => field
        });
        await AssertValidAsync(http);
    }

    private static Task<IHost> StartAsync()
    {
        return LlmTestServerTestHost.StartAsync(options => options
        .RequireBearerToken(_key).AddModel(ModelId, LlmTestServerModelKind.Chat)
        .AddChatScenario("api-version", scenario => scenario.ForModel(ModelId).WhenUserContains(_prompt).Responds(_answer)));
    }

    private static JsonObject Body()
    {
        return new()
        {
            ["model"] = ModelId,
            ["max_tokens"] = 64,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = _prompt })
        };
    }

    private static void Authorize(HttpClient http, string version)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        if (version.Length > 0) { http.DefaultRequestHeaders.TryAddWithoutValidation(AnthropicWireMapper.VersionHeaderName, version); }
    }

    private static async Task AssertValidAsync(HttpClient http)
    {
        using var response = await http.PostAsJsonAsync(_route, Body());
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("content")[0].GetProperty("text").GetString()).IsEqualTo(_answer);
    }

    private static async Task AssertInvalidAsync(HttpResponseMessage response, string field)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("error").GetProperty("type").GetString()).IsEqualTo("invalid_request_error");
        await Assert.That(payload.GetProperty("error").GetProperty("message").GetString()).Contains(field);
    }
}
