#pragma warning disable OPENAI001 // Exercise the official experimental Responses SDK through HTTP.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeWebSearchIntegrationTests
{
    private const string _sourceUrl = "https://example.com/source";
    private const string _searchId = "ws_native_fixture";
    private const string _messageId = "msg_native_fixture";
    private const string _answer = "Paris fixture.";
    private const string _query = "Paris fixture facts";
    private static readonly JsonSerializerOptions _configurationJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static LlmTestServerResponseItem[] Items()
    {
        return [
        new LlmTestServerResponseWebSearchItem
        {
            Id = _searchId, Queries = [_query], Sources = [new() { Url = _sourceUrl }],
        },
        new LlmTestServerResponseMessageItem
        {
            Id = _messageId, Content = _answer, StreamChunks = ["Paris ", "fixture."],
            Citations = [new LlmTestServerUrlCitation() { StartIndex = 0, EndIndex = 5, Url = _sourceUrl, Title = "Fixture source" }],
        },
    ];
    }

    private static LlmTestServerConfiguration Configuration(int responses = 1)
    {
        return new LlmTestServerClientConfigurationBuilder().UseGpt41Mini().UseChatScenario("native-web", scenario =>
        {
            for (var index = 0; index < responses; index++) { scenario.RespondsWithItems(Items()); }
        }).Build();
    }

    private static JsonObject Request(bool includeSources = true, bool stream = false, string input = "start")
    {
        var body = new JsonObject
        {
            ["model"] = LlmTestServerKnownModelIds.Gpt41Mini,
            ["input"] = input,
            ["tools"] = new JsonArray(new JsonObject { ["type"] = "web_search" }),
            ["tool_choice"] = new JsonObject { ["type"] = "web_search" },
            ["stream"] = stream,
        };
        if (includeSources) { body["include"] = new JsonArray("web_search_call.action.sources"); }
        return body;
    }

    private static ResponsesClient Sdk(HttpClient http)
    {
        return new(new ApiKeyCredential("provider-key"), new ResponsesClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/openai/v1/"),
            Transport = new HttpClientPipelineTransport(http),
        });
    }

    private static CreateResponseOptions Options(bool includeSources = true, string context = "low", string input = "start")
    {
        var options = new CreateResponseOptions
        {
            Model = LlmTestServerKnownModelIds.Gpt41Mini,
            InputItems = { ResponseItem.CreateUserMessageItem(input) },
            Tools = { ResponseTool.CreateWebSearchTool(new WebSearchToolApproximateLocation
            { Country = "FR", Region = "Ile-de-France", City = "Paris", Timezone = "Europe/Paris" },
                new WebSearchToolContextSize(context), new WebSearchToolFilters { AllowedDomains = { "example.com" } }) },
            ToolChoice = ResponseToolChoice.CreateRequiredChoice(),
        };
        if (includeSources) { options.IncludedProperties.Add(IncludedResponseProperty.WebSearchCallActionSources); }
        return options;
    }

    private static async Task<JsonElement> CompleteAsync(HttpClient http, JsonObject? request = null)
    {
        using var response = await http.PostAsJsonAsync("/openai/v1/responses", request ?? Request());
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task AssertWireAsync(JsonElement result, bool sources = true)
    {
        var output = result.GetProperty("output");
        await Assert.That(output.GetArrayLength()).IsEqualTo(2);
        var search = output[0];
        await Assert.That(search.GetProperty("type").GetString()).IsEqualTo("web_search_call");
        await Assert.That(search.GetProperty("id").GetString()).IsEqualTo(_searchId);
        await Assert.That(search.GetProperty("status").GetString()).IsEqualTo("completed");
        var action = search.GetProperty("action");
        await Assert.That(action.GetProperty("type").GetString()).IsEqualTo("search");
        await Assert.That(action.GetProperty("queries")[0].GetString()).IsEqualTo(_query);
        if (sources)
        {
            await Assert.That(action.GetProperty("sources").GetArrayLength()).IsEqualTo(1);
            await Assert.That(action.GetProperty("sources")[0].GetProperty("type").GetString()).IsEqualTo("url");
            await Assert.That(action.GetProperty("sources")[0].GetProperty("url").GetString()).IsEqualTo(_sourceUrl);
        }
        else { await Assert.That(!action.TryGetProperty("sources", out var omitted) || omitted.ValueKind == JsonValueKind.Null).IsTrue(); }
        var message = output[1];
        await Assert.That(message.GetProperty("type").GetString()).IsEqualTo("message");
        await Assert.That(message.GetProperty("id").GetString()).IsEqualTo(_messageId);
        var text = message.GetProperty("content")[0];
        await Assert.That(text.GetProperty("text").GetString()).IsEqualTo(_answer);
        var citation = text.GetProperty("annotations")[0];
        await Assert.That(text.GetProperty("annotations").GetArrayLength()).IsEqualTo(1);
        await Assert.That(citation.GetProperty("type").GetString()).IsEqualTo("url_citation");
        await Assert.That(citation.GetProperty("start_index").GetInt32()).IsEqualTo(0);
        await Assert.That(citation.GetProperty("end_index").GetInt32()).IsEqualTo(5);
        await Assert.That(citation.GetProperty("url").GetString()).IsEqualTo(_sourceUrl);
        await Assert.That(citation.GetProperty("title").GetString()).IsEqualTo("Fixture source");
    }

    private static async Task AssertSdkAsync(ResponseResult result, bool sources = true)
    {
        await Assert.That(result.OutputItems.Count).IsEqualTo(2);
        await Assert.That(result.OutputItems.OfType<FunctionCallResponseItem>().Count()).IsEqualTo(0);
        var search = (WebSearchCallResponseItem)result.OutputItems[0];
        await Assert.That(search.Id).IsEqualTo(_searchId);
        await Assert.That(search.Status).IsEqualTo(WebSearchCallStatus.Completed);
        var action = (WebSearchSearchAction)search.Action!;
        await Assert.That(action.Queries.Single()).IsEqualTo(_query);
        await Assert.That(action.Sources.Count).IsEqualTo(sources ? 1 : 0);
        if (sources) { await Assert.That(((WebSearchActionUriSource)action.Sources.Single()).Uri.AbsoluteUri).IsEqualTo(_sourceUrl); }
        var message = (MessageResponseItem)result.OutputItems[1];
        await Assert.That(message.Id).IsEqualTo(_messageId);
        await Assert.That(message.Content.Single().Text).IsEqualTo(_answer);
        var citation = (UriCitationMessageAnnotation)message.Content.Single().OutputTextAnnotations.Single();
        await Assert.That(citation.Uri.AbsoluteUri).IsEqualTo(_sourceUrl);
        await Assert.That(citation.StartIndex).IsEqualTo(0);
        await Assert.That(citation.EndIndex).IsEqualTo(5);
        await Assert.That(citation.Title).IsEqualTo("Fixture source");
    }
}

#pragma warning restore OPENAI001
