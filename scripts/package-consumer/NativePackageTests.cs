#pragma warning disable OPENAI001 // Qualify the official experimental Responses SDK against installed packages.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI.Responses;
using TUnit.Core;

public sealed class NativePackageTests
{
    private const string _key = "native-package-key";
    private const string _source = "https://example.com/installed-native";
    private const string _searchId = "ws_installed_native";
    private const string _messageId = "msg_installed_native";
    private const string _functionId = "fc_installed_native";
    private const string _callId = "call_installed_native";

    [Test]
    public async Task InstalledPackages_ServeNativeSearchJsonStreamAndCompleteMixedFunctionRound(CancellationToken cancellationToken)
    {
        var builder = DistributedApplicationTestingBuilder.Create([]);
        builder.AddLlmTestServer("native-package-server").WithApiKey(_key);
        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("native-package-server", cancellationToken);
        using var http = app.CreateHttpClient("native-package-server", "http");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        var control = new LlmTestServerClient(http, _key);
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseGpt41Mini()
            .UseConversation("installed-native", graph => graph
                .Step("search", step => step.WhenLatestUserEquals("native-start").WhenTurn(1).RespondsWithItems(NativeItems()).Then("follow"))
                .Step("follow", step => step.WhenLatestUserEquals("native-follow").WhenTurn(2).Responds("stored native history complete").Complete()))
            .UseConversation("installed-mixed", graph => graph
                .Step("search", step => step.WhenLatestUserEquals("mixed-start").WhenTurn(1).RespondsWithItems(
                    new LlmTestServerResponseFunctionCallItem
                    { Id = _functionId, Call = new() { Id = _callId, Name = "weather", ArgumentsJson = "{}" } },
                    NativeItems()[0], NativeItems()[1]).Then("result"))
                .Step("result", step => step.WhenToolResult("weather", _callId).WhenTurn(2)
                    .WhenLatestTextContains("18 degrees").Responds("mixed native round complete").Complete())), cancellationToken);
        using (var unauthorizedRequest = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/responses"))
        {
            unauthorizedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-native-key");
            unauthorizedRequest.Content = JsonContent.Create(new
            { model = LlmTestServerKnownModelIds.Gpt41Mini, input = "native-start", tools = new[] { new { type = "web_search" } } });
            using var denied = await http.SendAsync(unauthorizedRequest, cancellationToken);
            Require(denied.StatusCode == HttpStatusCode.Unauthorized, "The packaged native endpoint must enforce its configured key.");
        }
        Require((await control.GetConversationsAsync(cancellationToken: cancellationToken)).Count == 0,
            "Unauthorized native requests must not create conversation state.");

        using var storedConversation = control.ForConversation("installed-native-stored");
        var storedSdk = Sdk(storedConversation.HttpClient);
        var first = (await storedSdk.CreateResponseAsync(Options("native-start"), cancellationToken: cancellationToken)).Value;
        AssertNative(first, offset: 0);
        Require(first.OutputItems.Count == 2 && !first.OutputItems.OfType<FunctionCallResponseItem>().Any(),
            "Native web search must be a native item rather than a function call.");
        var follow = Options("native-follow");
        follow.PreviousResponseId = first.Id;
        follow.ToolChoice = ResponseToolChoice.CreateAutoChoice();
        var storedFinal = (await storedSdk.CreateResponseAsync(follow, cancellationToken: cancellationToken)).Value;
        Require(((MessageResponseItem)storedFinal.OutputItems.Single()).Content.Single().Text == "stored native history complete",
            "The installed server must restore native previous_response_id history before turn-two matching.");

        using var mixedConversation = control.ForConversation("installed-native-explicit");
        var mixedSdk = Sdk(mixedConversation.HttpClient);
        var streamingOptions = Options("mixed-start", mixed: true);
        streamingOptions.StoredOutputEnabled = false;
        streamingOptions.StreamingEnabled = true;
        var updates = new List<StreamingResponseUpdate>();
        await foreach (var update in mixedSdk.CreateResponseStreamingAsync(streamingOptions, cancellationToken: cancellationToken))
        { updates.Add(update); }
        Require(updates.Select(update => update.SequenceNumber).SequenceEqual(Enumerable.Range(0, updates.Count)),
            "Native stream sequence numbers must be contiguous.");
        Require(updates.OfType<StreamingResponseWebSearchCallInProgressUpdate>().Single().ItemId == _searchId
            && updates.OfType<StreamingResponseWebSearchCallSearchingUpdate>().Single().OutputIndex == 1
            && updates.OfType<StreamingResponseWebSearchCallCompletedUpdate>().Single().ItemId == _searchId,
            "The installed package must emit typed native progress updates at the mixed output index.");
        var annotation = updates.OfType<StreamingResponseOutputTextAnnotationAddedUpdate>().Single();
        Require(annotation.OutputIndex == 2 && annotation.ItemId == _messageId
            && ((UriCitationMessageAnnotation)annotation.Annotation).Uri.AbsoluteUri == _source,
            "The installed stream must retain the native URL citation.");
        var mixedFirst = updates.OfType<StreamingResponseCompletedUpdate>().Single().Response;
        Require(mixedFirst.OutputItems.Count == 3, "Mixed output order must include function, native search and text.");
        var function = (FunctionCallResponseItem)mixedFirst.OutputItems[0];
        Require(function.Id == _functionId && function.CallId == _callId && function.FunctionName == "weather"
            && function.FunctionArguments.ToString() == "{}", "Mixed function output and correlation IDs must remain distinct.");
        AssertNative(mixedFirst, offset: 1);
        var explicitFollow = Options("unused", mixed: true);
        explicitFollow.InputItems.Clear();
        explicitFollow.InputItems.Add(ResponseItem.CreateUserMessageItem("mixed-start"));
        foreach (var item in mixedFirst.OutputItems) { explicitFollow.InputItems.Add(item); }
        explicitFollow.InputItems.Add(ResponseItem.CreateFunctionCallOutputItem(_callId, "18 degrees"));
        var mixedFinal = (await mixedSdk.CreateResponseAsync(explicitFollow, cancellationToken: cancellationToken)).Value;
        Require(((MessageResponseItem)mixedFinal.OutputItems.Single()).Content.Single().Text == "mixed native round complete",
            "The installed server must complete a real SDK function/native round from explicit native history.");

        var states = await control.GetConversationsAsync(cancellationToken: cancellationToken);
        Require(states.Count == 2 && states.All(state => state.ExecutedSteps == 2),
            "Both independently correlated packaged conversations must advance exactly two logical turns.");
        var evidence = await control.GetRequestsAsync(cancellationToken: cancellationToken);
        Require(evidence.Requests.Count == 5 && evidence.Requests.Count(trace => trace.StatusCode == 401) == 1
            && evidence.Requests.Count(trace => trace.IsSuccess == true) == 4, "Public traces must expose all real packaged provider requests.");
        var storedTrace = evidence.Requests.Single(trace => trace.ConversationId == storedConversation.Id && trace.StepId == "follow");
        var storedSearch = storedTrace.Messages.Where(message => message.Role == "assistant").SelectMany(message => message.OutputItems)
            .OfType<LlmTestServerResponseWebSearchItem>().Single();
        Require(storedSearch.Id == _searchId && storedSearch.Sources.Single().Url == _source,
            "Public packaged trace serialization must retain typed native stored history.");
        var mixedTrace = evidence.Requests.Single(trace => trace.ConversationId == mixedConversation.Id && trace.StepId == "result");
        var history = mixedTrace.Messages.Where(message => message.Role == "assistant").SelectMany(message => message.OutputItems).ToArray();
        Require(history.Length == 3 && ((LlmTestServerResponseFunctionCallItem)history[0]).Id == _functionId
            && ((LlmTestServerResponseWebSearchItem)history[1]).Id == _searchId
            && ((LlmTestServerResponseMessageItem)history[2]).Id == _messageId,
            "Public packaged trace serialization must preserve ordered mixed output identities.");
        var streamedTrace = evidence.Requests.Single(trace => trace.ConversationId == mixedConversation.Id && trace.StepId == "search");
        Require(streamedTrace.IsStreaming && streamedTrace.TerminalMarkerObserved == true
            && streamedTrace.ResponseOutputItems.Count == 3 && streamedTrace.ResponseToolCalls.Single().Id == _callId
            && streamedTrace.NativeTools.Single().Contract == LlmTestServerNativeToolContract.OpenAiResponsesWebSearch,
            "Packaged native streaming evidence must include completion, ordered outputs and a separate native declaration.");
    }

    private static LlmTestServerResponseItem[] NativeItems() =>
    [
        new LlmTestServerResponseWebSearchItem { Id = _searchId, Queries = ["installed native fixture"], Sources = [new() { Url = _source }] },
        new LlmTestServerResponseMessageItem
        {
            Id = _messageId, Content = "Paris fixture.", StreamChunks = ["Paris ", "fixture."],
            Citations = [new LlmTestServerUrlCitation() { StartIndex = 0, EndIndex = 5, Url = _source, Title = "Installed fixture source" }],
        },
    ];

    private static ResponsesClient Sdk(HttpClient http) => new(new ApiKeyCredential(_key), new ResponsesClientOptions
    { Endpoint = new Uri(http.BaseAddress!, "/openai/v1/"), Transport = new HttpClientPipelineTransport(http) });

    private static CreateResponseOptions Options(string input, bool mixed = false)
    {
        var options = new CreateResponseOptions
        {
            Model = LlmTestServerKnownModelIds.Gpt41Mini,
            InputItems = { ResponseItem.CreateUserMessageItem(input) },
            Tools = { ResponseTool.CreateWebSearchTool(null, WebSearchToolContextSize.Low, null) },
            ToolChoice = mixed ? ResponseToolChoice.CreateAutoChoice() : ResponseToolChoice.CreateRequiredChoice(),
            IncludedProperties = { IncludedResponseProperty.WebSearchCallActionSources },
        };
        if (mixed) { options.Tools.Add(ResponseTool.CreateFunctionTool("weather", BinaryData.FromString("{\"type\":\"object\"}"), false, "Scripted weather")); }
        return options;
    }

    private static void AssertNative(ResponseResult response, int offset)
    {
        var search = (WebSearchCallResponseItem)response.OutputItems[offset];
        var action = (WebSearchSearchAction)search.Action!;
        Require(search.Id == _searchId && search.Status == WebSearchCallStatus.Completed && action.Queries.Single() == "installed native fixture"
            && ((WebSearchActionUriSource)action.Sources.Single()).Uri.AbsoluteUri == _source,
            "The installed official SDK must parse native search identity, action and included sources.");
        var message = (MessageResponseItem)response.OutputItems[offset + 1];
        var citation = (UriCitationMessageAnnotation)message.Content.Single().OutputTextAnnotations.Single();
        Require(message.Id == _messageId && message.Content.Single().Text == "Paris fixture." && citation.StartIndex == 0
            && citation.EndIndex == 5 && citation.Uri.AbsoluteUri == _source && citation.Title == "Installed fixture source",
            "The installed official SDK must parse the exact configured message and URL citation.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
#pragma warning restore OPENAI001
