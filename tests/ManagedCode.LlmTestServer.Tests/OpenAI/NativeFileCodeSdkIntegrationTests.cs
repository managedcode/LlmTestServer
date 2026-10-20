#pragma warning disable OPENAI001 // Exercise official Responses SDK contracts through HTTP.
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
    [Arguments(false, false, false)]
    [Arguments(false, true, false)]
    [Arguments(true, false, false)]
    [Arguments(true, true, false)]
    [Arguments(true, true, true)]
    public async Task JsonAndSdk_PreserveNativeItemsNullableIncludesAndTypedCitationsAsync(bool code, bool include, bool auto)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(Configuration(code, 2));
        var wire = await CompleteAsync(http, code, Request(code, include, auto));
        var item = wire.GetProperty("output")[0];
        await Assert.That(item.GetProperty("type").GetString()).IsEqualTo(code ? "code_interpreter_call" : "file_search_call");
        await Assert.That(item.GetProperty("id").GetString()).IsEqualTo(ItemId(code));
        var member = code ? "outputs" : "results";
        if (include) { await Assert.That(item.GetProperty(member).GetArrayLength()).IsEqualTo(code ? 2 : 1); }
        else { await Assert.That(!item.TryGetProperty(member, out var omitted) || omitted.ValueKind == JsonValueKind.Null).IsTrue(); }
        await Assert.That(item.TryGetProperty("search_results", out _)).IsFalse();
        await AssertSdkAsync((await Sdk(http).CreateResponseAsync(Options(code, include, auto))).Value, code, include);
        var trace = (await control.GetRequestsAsync()).Requests[0];
        await Assert.That(trace.NativeTools.Count).IsEqualTo(1);
        await Assert.That(trace.ResponseOutputItems.Count).IsEqualTo(2);
        await Assert.That(trace.ResponseToolCalls.Count).IsEqualTo(0);
        var exported = await control.ExportRequestsAsync();
        await Assert.That(exported.Evidence.Requests[0].ResponseOutputItems[0] is LlmTestServerResponseCodeInterpreterItem).IsEqualTo(code);
        await Assert.That(exported.Evidence.Requests[0].ResponseOutputItems[0] is LlmTestServerResponseFileSearchItem).IsEqualTo(!code);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OfficialSdkStream_ParsesLifecycleNativeCodeChunksAndAnnotationsAsync(bool code)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(Configuration(code));
        var options = Options(code); options.StreamingEnabled = true;
        var updates = new List<StreamingResponseUpdate>();
        await foreach (var update in Sdk(http).CreateResponseStreamingAsync(options)) { updates.Add(update); }
        await Assert.That(updates.Select(update => update.SequenceNumber).SequenceEqual(Enumerable.Range(0, updates.Count))).IsTrue();
        if (code)
        {
            await Assert.That(updates.OfType<StreamingResponseCodeInterpreterCallInProgressUpdate>().Single().ItemId).IsEqualTo(ItemId(true));
            await Assert.That(updates.OfType<StreamingResponseCodeInterpreterCallInterpretingUpdate>().Single().OutputIndex).IsEqualTo(0);
            await Assert.That(updates.OfType<StreamingResponseCodeInterpreterCallCompletedUpdate>().Single().ItemId).IsEqualTo(ItemId(true));
            await Assert.That(string.Concat(updates.OfType<StreamingResponseCodeInterpreterCallCodeDeltaUpdate>().Select(update => update.Delta))).IsEqualTo(_code);
            await Assert.That(updates.OfType<StreamingResponseCodeInterpreterCallCodeDoneUpdate>().Single().Code).IsEqualTo(_code);
        }
        else
        {
            await Assert.That(updates.OfType<StreamingResponseFileSearchCallInProgressUpdate>().Single().ItemId).IsEqualTo(ItemId(false));
            await Assert.That(updates.OfType<StreamingResponseFileSearchCallSearchingUpdate>().Single().OutputIndex).IsEqualTo(0);
            await Assert.That(updates.OfType<StreamingResponseFileSearchCallCompletedUpdate>().Single().ItemId).IsEqualTo(ItemId(false));
        }
        var annotation = updates.OfType<StreamingResponseOutputTextAnnotationAddedUpdate>().Single();
        await Assert.That(annotation.OutputIndex).IsEqualTo(1);
        await Assert.That(annotation.ContentIndex).IsEqualTo(0);
        await Assert.That(annotation.AnnotationIndex).IsEqualTo(0);
        await Assert.That(annotation.Annotation is ContainerFileCitationMessageAnnotation).IsEqualTo(code);
        await Assert.That(annotation.Annotation is FileCitationMessageAnnotation).IsEqualTo(!code);
        await Assert.That(updates.OfType<StreamingResponseFunctionCallArgumentsDeltaUpdate>().Count()).IsEqualTo(0);
        await AssertSdkAsync(updates.OfType<StreamingResponseCompletedUpdate>().Single().Response, code);
        var trace = (await control.GetRequestsAsync()).Requests.Single();
        await Assert.That(trace.TerminalMarkerObserved).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RawSse_FinalOrderedOutputMatchesJsonAsync(bool code)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration(code, 2));
        var json = await CompleteAsync(http, code);
        using var response = await http.PostAsJsonAsync("/openai/v1/responses", Request(code, stream: true));
        response.EnsureSuccessStatusCode();
        var frames = (await response.Content.ReadAsStringAsync()).Split('\n').Where(line => line.StartsWith("data: ", StringComparison.Ordinal) && line != "data: [DONE]")
            .Select(line => JsonDocument.Parse(line[6..]).RootElement.Clone()).ToArray();
        await Assert.That(frames.Select(frame => frame.GetProperty("sequence_number").GetInt32()).SequenceEqual(Enumerable.Range(0, frames.Length))).IsTrue();
        var prefix = code ? "response.code_interpreter_call." : "response.file_search_call.";
        var lifecycle = frames.Where(frame => frame.GetProperty("type").GetString()!.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        await Assert.That(string.Join(',', lifecycle.Select(frame => frame.GetProperty("type").GetString())))
            .IsEqualTo(prefix + "in_progress," + prefix + (code ? "interpreting," : "searching,") + prefix + "completed");
        await Assert.That(lifecycle.All(frame => frame.GetProperty("item_id").GetString() == ItemId(code) && frame.GetProperty("output_index").GetInt32() == 0)).IsTrue();
        var final = frames.Single(frame => frame.GetProperty("type").GetString() == "response.completed").GetProperty("response");
        await Assert.That(final.GetProperty("output").GetRawText()).IsEqualTo(json.GetProperty("output").GetRawText());
    }
}
#pragma warning restore OPENAI001
