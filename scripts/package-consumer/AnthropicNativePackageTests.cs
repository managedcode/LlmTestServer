using System.Text.Json;
using System.Text.Json.Nodes;
using global::Anthropic;
using global::Anthropic.Core;
using global::Anthropic.Models.Messages;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;
using TUnit.Core;

public sealed class AnthropicNativePackageTests
{
    private static readonly string _model = JsonSerializer.SerializeToElement(Model.ClaudeSonnet5_5).GetString()!;
    private const string _key = "anthropic-sdk-key";
    private const string _url = "https://example.com/sdk-source";
    private const string _call = "srvtoolu_sdk";

    [Test]
    public async Task InstalledPackages_OfficialSdkParsesNativeJsonSseErrorsAndHistoryAsync(CancellationToken cancellationToken)
    {
        var builder = DistributedApplicationTestingBuilder.Create([]);
        builder.AddLlmTestServer("anthropic-native-package-server").WithApiKey(_key);
        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("anthropic-native-package-server", cancellationToken);
        using var http = app.CreateHttpClient("anthropic-native-package-server", LlmTestServerResource.HttpEndpointName);
        var control = new LlmTestServerClient(http, _key);
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseModel(_model, LlmTestServerModelKind.Chat)
            .UseChatScenario("anthropic-sdk", scenario => scenario.ForModel(_model)
                .RespondsWithItems(Items()).RespondsWithItems(Items()).RespondsWithItems(Items("unavailable")).Responds("SDK history complete.")), cancellationToken);
        using var sdk = new AnthropicClient(new ClientOptions
        { HttpClient = http, BaseUrl = new Uri(http.BaseAddress!, "/anthropic").ToString(), ApiKey = _key, MaxRetries = 0, ResponseValidation = true });
        var options = Options();
        var first = await sdk.Messages.Create(options, cancellationToken);
        AssertContent(first.Content);
        Require(first.Usage.ServerToolUse!.WebSearchRequests == 1, "Installed native JSON must report one search.");

        var blocks = new JsonArray(); var input = string.Empty; var stopped = new List<long>();
        var messageStopped = false; var citationDeltas = 0; var searchCount = -1L;
        await foreach (var update in sdk.Messages.CreateStreaming(options, cancellationToken))
        {
            switch (update.Value)
            {
                case RawContentBlockStartEvent start:
                    Require(start.Index == blocks.Count, "Installed SDK stream block indexes must be contiguous.");
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
        Require(messageStopped && stopped.SequenceEqual(new long[] { 0, 1, 2 }) && citationDeltas == 1 && searchCount == 1, "Installed native SDK SSE must complete blocks, citations and usage.");
        var streamed = JsonSerializer.Deserialize<List<ContentBlock>>(blocks.ToJsonString())!;
        AssertContent(streamed);
        Require(JsonNode.DeepEquals(blocks, JsonNode.Parse(JsonSerializer.Serialize(first.Content))), "Installed SDK SSE must reconstruct JSON content exactly.");

        var failed = await sdk.Messages.Create(options, cancellationToken);
        var errorBlock = (WebSearchToolResultBlock)failed.Content[1].Value!;
        Require(errorBlock.Content.TryPickError(out var nativeError) && nativeError.ErrorCode.Json.GetString() == "unavailable",
            "Official installed SDK must parse typed native errors inside successful Messages.");
        var history = JsonSerializer.Deserialize<List<ContentBlockParam>>(JsonSerializer.Serialize(first.Content))!;
        var follow = options with { Messages = [.. options.Messages, new AssistantMessageParam { Content = history }, new UserMessageParam { Content = "SDK follow" }] };
        var continued = await sdk.Messages.Create(follow, cancellationToken);
        Require(((TextBlock)continued.Content.Single().Value!).Text == "SDK history complete.", "Installed SDK assistant history must roundtrip.");
        var traces = (await control.ExportRequestsAsync(cancellationToken: cancellationToken)).Evidence.Requests;
        Require(traces.Count == 4, "Installed journal must retain every SDK request.");
        var stream = traces.Single(trace => trace.IsStreaming);
        Require(stream.TerminalMarkerObserved == true && stream.NativeTools.Count == 1 && stream.ResponseToolCalls.Count == 0, "Installed journal must retain terminal native evidence separately from functions.");
        var restored = traces.Single(trace => trace.Messages.Any(message => message.Content == "SDK follow"));
        Require(restored.Messages.Single(message => message.Role == "assistant").OutputItems.Count == 3, "Installed typed history must retain ordered native items.");
    }

    private static MessageCreateParams Options(long maxUses = 1)
    {
        return new()
        {
            Model = Model.ClaudeSonnet5_5, MaxTokens = 256, ToolChoice = new ToolChoiceAuto(),
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
    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
