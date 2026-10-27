using System.Text.Json;
using System.Text.Json.Nodes;
using global::Anthropic;
using global::Anthropic.Core;
using global::Anthropic.Exceptions;
using global::Anthropic.Models.Messages;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Anthropic;

public sealed class AnthropicNativeWebSearchSdkIntegrationTests
{
    private static readonly string _model = JsonSerializer.SerializeToElement(Model.ClaudeSonnet5_5).GetString()!;
    private const string _key = "anthropic-sdk-key";
    private const string _url = "https://example.com/sdk-source";
    private const string _call = "srvtoolu_sdk";

    [Test]
    public async Task OfficialSdk_ParsesNativeJsonSseAndAssistantHistoryAsync(CancellationToken cancellationToken)
    {
        using var host = await LlmTestServerTestHost.StartAsync(cancellationToken: cancellationToken);
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseChatModel(_model)
            .UseChatScenario("anthropic-sdk", scenario => scenario.ForModel(_model)
                .RespondsWithItems(Items()).RespondsWithItems(Items()).Responds("SDK history complete.")), cancellationToken);
        using var sdk = new AnthropicClient(new ClientOptions
        { HttpClient = http, BaseUrl = new Uri(http.BaseAddress!, "/anthropic").ToString(), ApiKey = _key, MaxRetries = 0, ResponseValidation = true });
        var options = Options();
        var first = await sdk.Messages.Create(options, cancellationToken);
        AssertContent(first.Content);
        await Assert.That(first.Usage.ServerToolUse!.WebSearchRequests).IsEqualTo(1L);

        var blocks = new JsonArray(); var input = string.Empty; var stopped = new List<long>();
        var messageStopped = false; var citationDeltas = 0; var searchCount = -1L;
        await foreach (var update in sdk.Messages.CreateStreaming(options, cancellationToken))
        {
            switch (update.Value)
            {
                case RawContentBlockStartEvent start:
                    await Assert.That(start.Index).IsEqualTo((long)blocks.Count);
                    blocks.Add(JsonNode.Parse(start.ContentBlock.Json.GetRawText()));
                    break;
                case RawContentBlockDeltaEvent delta:
                    switch (delta.Delta.Value)
                    {
                        case InputJsonDelta json: input += json.PartialJson; break;
                        case TextDelta text:
                            var index = checked((int)delta.Index);
                            blocks[index]!["text"] = (blocks[index]!["text"]?.GetValue<string>() ?? string.Empty) + text.Text;
                            break;
                        case CitationsDelta citation:
                            citationDeltas++;
                            var citationIndex = checked((int)delta.Index);
                            blocks[citationIndex]!["citations"] ??= new JsonArray();
                            ((JsonArray)blocks[citationIndex]!["citations"]!).Add(JsonNode.Parse(citation.Citation.Json.GetRawText()));
                            break;
                    }
                    break;
                case RawContentBlockStopEvent stop: stopped.Add(stop.Index); break;
                case RawMessageDeltaEvent delta:
                    searchCount = delta.Usage.ServerToolUse!.WebSearchRequests;
                    break;
                case RawMessageStopEvent: messageStopped = true; break;
            }
        }
        blocks[0]!["input"] = JsonNode.Parse(input);
        await Assert.That(messageStopped && stopped.SequenceEqual(new long[] { 0, 1, 2 }) && citationDeltas == 1 && searchCount == 1).IsTrue();
        var streamed = JsonSerializer.Deserialize<List<ContentBlock>>(blocks.ToJsonString())!;
        AssertContent(streamed);
        await Assert.That(JsonNode.DeepEquals(blocks, JsonNode.Parse(JsonSerializer.Serialize(first.Content)))).IsTrue();

        var history = JsonSerializer.Deserialize<List<ContentBlockParam>>(JsonSerializer.Serialize(first.Content))!;
        var follow = options with { Messages = [.. options.Messages, new AssistantMessageParam { Content = history }, new UserMessageParam { Content = "SDK follow" }] };
        var continued = await sdk.Messages.Create(follow, cancellationToken);
        await Assert.That(((TextBlock)continued.Content.Single().Value!).Text).IsEqualTo("SDK history complete.");
        var traces = (await control.ExportRequestsAsync(cancellationToken: cancellationToken)).Evidence.Requests;
        await Assert.That(traces.Count).IsEqualTo(3);
        var stream = traces.Single(trace => trace.IsStreaming);
        await Assert.That(stream.TerminalMarkerObserved == true && stream.NativeTools.Count == 1 && stream.ResponseToolCalls.Count == 0).IsTrue();
        var restored = traces.Single(trace => trace.Messages.Any(message => message.Content == "SDK follow"));
        await Assert.That(restored.Messages.Single(message => message.Role == "assistant").OutputItems.Count).IsEqualTo(3);
    }

    [Test]
    [Arguments("invalid_tool_input")]
    [Arguments("unavailable")]
    [Arguments("max_uses_exceeded")]
    [Arguments("too_many_requests")]
    [Arguments("query_too_long")]
    [Arguments("request_too_large")]
    public async Task OfficialSdk_ParsesTypedNativeErrorsAndNextQueuedSuccessAsync(string code, CancellationToken cancellationToken)
    {
        using var host = await LlmTestServerTestHost.StartAsync(cancellationToken: cancellationToken);
        using var http = host.GetTestClient(); var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseChatModel(_model)
            .UseChatScenario("anthropic-sdk-errors", scenario => scenario.ForModel(_model)
                .RespondsWithItems(Items(code)).RespondsWithItems(Items())), cancellationToken);
        using var sdk = new AnthropicClient(new ClientOptions
        { HttpClient = http, BaseUrl = new Uri(http.BaseAddress!, "/anthropic").ToString(), ApiKey = _key, MaxRetries = 0, ResponseValidation = true });
        var request = Options(code == "max_uses_exceeded" ? 0 : 1);
        var result = await sdk.Messages.Create(request, cancellationToken);
        var returned = (WebSearchToolResultBlock)result.Content[1].Value!;
        await Assert.That(returned.Content.TryPickError(out var error)).IsTrue();
        await Assert.That(error!.ErrorCode.Json.GetString()).IsEqualTo(code);
        await Assert.That(result.Usage.ServerToolUse!.WebSearchRequests).IsEqualTo(code == "max_uses_exceeded" ? 0L : 1L);
        AssertContent((await sdk.Messages.Create(Options(), cancellationToken)).Content);
        await Assert.That((await control.GetRequestsAsync(cancellationToken: cancellationToken)).Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task OfficialSdk_RejectsProgrammaticCallerAndPreservesQueuedResponseAsync(CancellationToken cancellationToken)
    {
        using var host = await LlmTestServerTestHost.StartAsync(cancellationToken: cancellationToken);
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseChatModel(_model)
            .UseChatScenario("anthropic-sdk-caller", scenario => scenario.ForModel(_model)
                .RespondsWithItems(Items()).RespondsWithItems(Items())), cancellationToken);
        using var sdk = new AnthropicClient(new ClientOptions
        { HttpClient = http, BaseUrl = new Uri(http.BaseAddress!, "/anthropic").ToString(), ApiKey = _key, MaxRetries = 0, ResponseValidation = true });
        var options = Options();
        var first = await sdk.Messages.Create(options, cancellationToken);
        var history = JsonSerializer.Deserialize<List<ContentBlockParam>>(JsonSerializer.Serialize(first.Content))!;
        var call = (ServerToolUseBlockParam)history[0].Value!;
        history[0] = call with { Caller = new ServerToolCaller { ToolID = "toolu_programmatic" } };
        var unsupported = options with
        { Messages = [.. options.Messages, new AssistantMessageParam { Content = history }, new UserMessageParam { Content = "unsupported caller" }] };
        var rejected = false;
        try { _ = await sdk.Messages.Create(unsupported, cancellationToken); }
        catch (AnthropicBadRequestException) { rejected = true; }
        await Assert.That(rejected).IsTrue();
        AssertContent((await sdk.Messages.Create(options, cancellationToken)).Content);
        var traces = (await control.GetRequestsAsync(cancellationToken: cancellationToken)).Requests;
        await Assert.That(traces.Count).IsEqualTo(3);
        await Assert.That(traces.Count(trace => trace.StatusCode == 400)).IsEqualTo(1);
    }

    private static MessageCreateParams Options(long maxUses = 1)
    {
        return new()
        {
            Model = Model.ClaudeSonnet5_5,
            MaxTokens = 256,
            ToolChoice = new ToolChoiceAuto(),
            Messages = [new UserMessageParam { Content = "SDK fixture" }],
            Tools = [new WebSearchTool20250305
            { MaxUses = maxUses, AllowedDomains = ["example.com/sdk*"], UserLocation = new() { Country = "US", Timezone = "America/New_York" } }],
        };
    }

    private static LlmTestServerResponseItem[] Items(string? error = null)
    {
        return
        [new LlmTestServerResponseServerToolUseItem { Id = _call, Input = new LlmTestServerWebSearchInput { Query = "SDK fixture query" } },
        new LlmTestServerResponseServerToolResultItem { ToolUseId = _call,
            Result = error is null ? new LlmTestServerWebSearchResults { Results = [new() { Url = _url, Title = "SDK source", EncryptedContent = "opaque-sdk-source" }] }
                : new LlmTestServerWebSearchErrorResult { ErrorCode = error } },
        new LlmTestServerResponseMessageItem { Content = "SDK answer.", StreamChunks = ["SDK ", "answer."],
            Citations = error is null ? [new LlmTestServerAnthropicWebSearchCitation { Url = _url, EncryptedIndex = "opaque-sdk-index", CitedText = "SDK source excerpt", Title = "SDK source" }] : [] }];
    }

    private static void AssertContent(IReadOnlyList<ContentBlock> content)
    {
        if (content.Count != 3 || content[0].Value is not ServerToolUseBlock call || call.ID != _call
            || call.Input["query"].GetString() != "SDK fixture query" || content[1].Value is not WebSearchToolResultBlock returned
            || returned.ToolUseID != _call || !returned.Content.TryPickWebSearchResultBlocks(out var results)
            || results.Single().EncryptedContent != "opaque-sdk-source" || content[2].Value is not TextBlock text || text.Text != "SDK answer."
            || text.Citations!.Single().Value is not CitationsWebSearchResultLocation citation || citation.EncryptedIndex != "opaque-sdk-index")
        { throw new InvalidOperationException("Official Anthropic SDK must parse typed native calls, results and citations."); }
    }
}
