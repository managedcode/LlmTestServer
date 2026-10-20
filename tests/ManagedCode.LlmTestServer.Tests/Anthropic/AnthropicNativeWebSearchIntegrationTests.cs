using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Tests.Anthropic;

public sealed partial class AnthropicNativeWebSearchIntegrationTests
{
    private const string _model = LlmTestServerKnownModelIds.ClaudeSonnet55;
    private const string _callId = "srvtoolu_fixture";
    private const string _query = "Paris fixture facts";
    private const string _answer = "Paris fixture.";
    private const string _url = "https://example.com/source";
    private const string _title = "Fixture source";
    private const string _opaque = "opaque-source-content";
    private const string _index = "opaque-citation-index";
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private static LlmTestServerResponseItem[] Items(string? error = null, bool empty = false)
    {
        return [new LlmTestServerResponseServerToolUseItem
        { Id = _callId, Input = new LlmTestServerWebSearchInput { Query = _query } },
        new LlmTestServerResponseServerToolResultItem
        { ToolUseId = _callId, Result = error is null ? new LlmTestServerWebSearchResults
          { Results = empty ? [] : [new() { Url = _url, Title = _title, EncryptedContent = _opaque, PageAge = "2 days ago" }] }
          : new LlmTestServerWebSearchErrorResult { ErrorCode = error } },
        new LlmTestServerResponseMessageItem
        { Content = _answer, StreamChunks = ["Paris ", "fixture."], Citations = error is not null || empty ? [] :
          [new LlmTestServerAnthropicWebSearchCitation { Url = _url, Title = _title, EncryptedIndex = _index, CitedText = "Scripted source excerpt" }] }];
    }

    private static LlmTestServerConfiguration Configuration(int responses = 1, string? error = null, bool empty = false)
    {
        return new LlmTestServerClientConfigurationBuilder().UseChatModel(_model).UseChatScenario("anthropic-native", scenario =>
        { scenario.ForModel(_model); for (var index = 0; index < responses; index++) { scenario.RespondsWithItems(Items(error, empty)); } }).Build();
    }

    private static JsonObject Request(bool stream = false, string input = "start")
    {
        return new JsonObject
        {
            ["model"] = _model,
            ["max_tokens"] = 256,
            ["tool_choice"] = new JsonObject { ["type"] = "auto" },
            ["stream"] = stream,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = input }),
            ["tools"] = new JsonArray(new JsonObject { ["type"] = "web_search_20250305", ["name"] = "web_search" }),
        };
    }

    private static void Headers(HttpClient http)
    {
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        http.DefaultRequestHeaders.Add("x-api-key", "provider-key");
    }

    private static async Task<JsonElement> CompleteAsync(HttpClient http, JsonObject? body = null)
    {
        using var response = await http.PostAsJsonAsync("/anthropic/v1/messages", body ?? Request());
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task AssertContentAsync(JsonElement content)
    {
        await Assert.That(content.GetArrayLength()).IsEqualTo(3);
        await Assert.That(content[0].GetProperty("type").GetString()).IsEqualTo("server_tool_use");
        await Assert.That(content[0].GetProperty("id").GetString()).IsEqualTo(_callId);
        await Assert.That(content[0].GetProperty("name").GetString()).IsEqualTo("web_search");
        await Assert.That(content[0].GetProperty("input").GetProperty("query").GetString()).IsEqualTo(_query);
        await Assert.That(content[1].GetProperty("type").GetString()).IsEqualTo("web_search_tool_result");
        await Assert.That(content[1].GetProperty("tool_use_id").GetString()).IsEqualTo(_callId);
        var source = content[1].GetProperty("content")[0];
        await Assert.That(source.GetProperty("type").GetString()).IsEqualTo("web_search_result");
        await Assert.That(source.GetProperty("url").GetString()).IsEqualTo(_url);
        await Assert.That(source.GetProperty("title").GetString()).IsEqualTo(_title);
        await Assert.That(source.GetProperty("encrypted_content").GetString()).IsEqualTo(_opaque);
        await Assert.That(source.GetProperty("page_age").GetString()).IsEqualTo("2 days ago");
        await Assert.That(content[2].GetProperty("text").GetString()).IsEqualTo(_answer);
        var citation = content[2].GetProperty("citations")[0];
        await Assert.That(citation.GetProperty("type").GetString()).IsEqualTo("web_search_result_location");
        await Assert.That(citation.GetProperty("url").GetString()).IsEqualTo(_url);
        await Assert.That(citation.GetProperty("encrypted_index").GetString()).IsEqualTo(_index);
        await Assert.That(citation.GetProperty("cited_text").GetString()).IsEqualTo("Scripted source excerpt");
    }
}
