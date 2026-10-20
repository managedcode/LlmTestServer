#pragma warning disable OPENAI001 // Verify official SDK nullable contracts through HTTP.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeFileCodeIntegrationTests
{
    [Test]
    public async Task FileResultOptionalFields_RemainNullWithoutInventedResourceValuesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var configuration = Configuration(false, 2);
        var native = (LlmTestServerResponseFileSearchItem)Items(false)[0];
        var message = (LlmTestServerResponseMessageItem)Items(false)[1];
        for (var index = 0; index < 2; index++)
        {
            configuration.ChatScenarios[0].Responses[index] = configuration.ChatScenarios[0].Responses[index] with
            { OutputItems = [native with { Results = [new LlmTestServerFileSearchResult()] }, message with { Citations = [] }] };
        }
        await new LlmTestServerClient(http).ConfigureAsync(configuration);
        var wire = await CompleteAsync(http, false);
        var result = wire.GetProperty("output")[0].GetProperty("results")[0];
        foreach (var member in new[] { "file_id", "filename", "text", "score", "attributes" })
        { await Assert.That(!result.TryGetProperty(member, out var value) || value.ValueKind == JsonValueKind.Null).IsTrue(); }
        var options = Options(false);
        options.Tools.Clear(); options.Tools.Add(ResponseTool.CreateFileSearchTool([_storeId], null, null, null));
        var parsed = ((FileSearchCallResponseItem)(await Sdk(http).CreateResponseAsync(options)).Value.OutputItems[0]).Results.Single();
        await Assert.That(parsed.FileId).IsNull();
        await Assert.That(parsed.Filename).IsNull();
        await Assert.That(parsed.Text).IsNull();
        await Assert.That(parsed.Score).IsNull();
        await Assert.That(parsed.Attributes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CodeNullableBodyAndOutputs_RemainNullThroughHttpAndSdkAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var configuration = Configuration(true, 2);
        var item = (LlmTestServerResponseCodeInterpreterItem)Items(true)[0];
        for (var index = 0; index < 2; index++)
        {
            configuration.ChatScenarios[0].Responses[index] = configuration.ChatScenarios[0].Responses[index] with
            { OutputItems = [item with { Code = null, CodeChunks = [], Outputs = null }, Items(true)[1]] };
        }
        await new LlmTestServerClient(http).ConfigureAsync(configuration);
        var wire = (await CompleteAsync(http, true)).GetProperty("output")[0];
        await Assert.That(wire.GetProperty("code").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(wire.GetProperty("outputs").ValueKind).IsEqualTo(JsonValueKind.Null);
        var parsed = (CodeInterpreterCallResponseItem)(await Sdk(http).CreateResponseAsync(Options(true))).Value.OutputItems[0];
        await Assert.That(parsed.Code).IsNull();
        await Assert.That(parsed.Outputs?.Count ?? 0).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CancellationOrReset_LeavesNativeGraphAvailableForRetryAsync(bool code, bool reset)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseChatModel(Model(code)).UseConversation("file-code-cancel", graph => graph.ForModel(Model(code))
            .Step("native", step => step.RespondsWithItems(Items(code)).DelaysBy(800).Complete())));
        using var cancellation = new CancellationTokenSource();
        var pending = http.PostAsJsonAsync("/openai/v1/responses", Request(code), cancellation.Token);
        var busy = false;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if ((await control.GetConversationsAsync()).Any(state => state.IsBusy)) { busy = true; break; }
            if (pending.IsCompleted) { break; }
            await Task.Delay(2);
        }
        await Assert.That(busy).IsTrue();
        if (reset)
        {
            await control.ResetAsync();
            using var superseded = await pending;
            await Assert.That(superseded.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        }
        else
        {
            cancellation.Cancel(); var cancelled = false;
            try { using var response = await pending; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelled = true; }
            await Assert.That(cancelled).IsTrue();
        }
        await Assert.That((await control.GetConversationsAsync()).Count).IsEqualTo(0);
        await CompleteAsync(http, code);
        await Assert.That((await control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task NativePayloads_AreIncludedInHistoryAndGraphCapacityAccountingAsync(bool code, bool graph)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var items = Items(code);
        items[0] = code
            ? ((LlmTestServerResponseCodeInterpreterItem)items[0]) with { Outputs = [new LlmTestServerCodeInterpreterLogsOutput { Logs = new string('x', 10000) }] }
            : ((LlmTestServerResponseFileSearchItem)items[0]) with
            {
                Results = [new LlmTestServerFileSearchResult
            { FileId = _fileId, Filename = _fileName, Text = new string('x', 10000) }]
            };
        var builder = new LlmTestServerClientConfigurationBuilder().UseChatModel(Model(code));
        if (graph) { builder.UseConversationCapacity(4, 3000).UseConversation("file-code-budget", flow => flow.ForModel(Model(code)).Step("native", step => step.RespondsWithItems(items).Complete())); }
        else { builder.UseChatResponseCapacity(8, 3000).UseChatScenario("file-code-budget", scenario => scenario.ForModel(Model(code)).RespondsWithItems(items)); }
        var control = new LlmTestServerClient(http); await control.ConfigureAsync(builder.Build());
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", Request(code));
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That((await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo(graph ? "conversation_capacity_exceeded" : "llm_test_server_response_capacity_exceeded");
        await Assert.That((await control.GetConversationsAsync()).Count).IsEqualTo(0);
        if (!graph) { var transient = Request(code); transient["store"] = false; await CompleteAsync(http, code, transient); }
    }
}
#pragma warning restore OPENAI001
