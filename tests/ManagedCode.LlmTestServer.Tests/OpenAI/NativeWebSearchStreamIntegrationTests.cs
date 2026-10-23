#pragma warning disable OPENAI001 // Exercise the official experimental Responses SDK through HTTP.
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeWebSearchIntegrationTests
{
    [Test]
    public async Task OfficialSdkStream_ParsesNativeProgressCitationAndCompletedOutputAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(Configuration());
        var updates = new List<StreamingResponseUpdate>();
        var text = new StringBuilder();
        var options = Options();
        options.StreamingEnabled = true;
        await foreach (var update in Sdk(http).CreateResponseStreamingAsync(options))
        {
            updates.Add(update);
            if (update is StreamingResponseOutputTextDeltaUpdate delta) { text.Append(delta.Delta); }
        }
        await Assert.That(text.ToString()).IsEqualTo(_answer);
        await Assert.That(updates.Select(update => update.SequenceNumber).SequenceEqual(Enumerable.Range(0, updates.Count))).IsTrue();
        await Assert.That(updates.OfType<StreamingResponseWebSearchCallInProgressUpdate>().Single().ItemId).IsEqualTo(_searchId);
        await Assert.That(updates.OfType<StreamingResponseWebSearchCallSearchingUpdate>().Single().OutputIndex).IsEqualTo(0);
        await Assert.That(updates.OfType<StreamingResponseWebSearchCallCompletedUpdate>().Single().ItemId).IsEqualTo(_searchId);
        var citation = updates.OfType<StreamingResponseOutputTextAnnotationAddedUpdate>().Single();
        await Assert.That(citation.ItemId).IsEqualTo(_messageId);
        await Assert.That(citation.OutputIndex).IsEqualTo(1);
        await Assert.That(citation.ContentIndex).IsEqualTo(0);
        await Assert.That(citation.AnnotationIndex).IsEqualTo(0);
        await Assert.That(((UriCitationMessageAnnotation)citation.Annotation).Uri.AbsoluteUri).IsEqualTo(_sourceUrl);
        await Assert.That(updates.OfType<StreamingResponseFunctionCallArgumentsDeltaUpdate>().Count()).IsEqualTo(0);
        await AssertSdkAsync(updates.OfType<StreamingResponseCompletedUpdate>().Single().Response);
        var trace = (await control.GetRequestsAsync()).Requests.Single();
        await Assert.That(trace.IsStreaming).IsTrue();
        await Assert.That(trace.TerminalMarkerObserved).IsTrue();
        await Assert.That(trace.ResponseToolCalls.Count).IsEqualTo(0);
        var retainedMessage = (LlmTestServerResponseMessageItem)trace.ResponseOutputItems[1];
        await Assert.That(retainedMessage.Content).IsEqualTo(text.ToString());
        var retainedCitation = (LlmTestServerUrlCitation)retainedMessage.Citations.Single();
        await Assert.That(retainedCitation.Url).IsEqualTo(_sourceUrl);
        await Assert.That(retainedCitation.StartIndex).IsEqualTo(0);
        await Assert.That(retainedCitation.EndIndex).IsEqualTo(5);
    }

    [Test]
    public async Task RawStream_FinalOutputMatchesJsonAndNativeLifecycleIsOrderedAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(Configuration(2));
        var json = await CompleteAsync(http);
        using var response = await http.PostAsJsonAsync("/openai/v1/responses", Request(stream: true));
        response.EnsureSuccessStatusCode();
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("text/event-stream");
        var frames = (await response.Content.ReadAsStringAsync()).Split('\n')
            .Where(line => line.StartsWith("data: ", StringComparison.Ordinal) && line != "data: [DONE]")
            .Select(line => JsonDocument.Parse(line[6..]).RootElement.Clone()).ToArray();
        await Assert.That(frames.Select(frame => frame.GetProperty("sequence_number").GetInt32()).SequenceEqual(Enumerable.Range(0, frames.Length))).IsTrue();
        var native = frames.Where(frame => frame.GetProperty("type").GetString()!.StartsWith("response.web_search_call.", StringComparison.Ordinal)).ToArray();
        await Assert.That(string.Join(',', native.Select(frame => frame.GetProperty("type").GetString())))
            .IsEqualTo("response.web_search_call.in_progress,response.web_search_call.searching,response.web_search_call.completed");
        await Assert.That(native.All(frame => frame.GetProperty("item_id").GetString() == _searchId && frame.GetProperty("output_index").GetInt32() == 0)).IsTrue();
        var names = frames.Select(frame => frame.GetProperty("type").GetString()).ToArray();
        await Assert.That(Array.IndexOf(names, "response.web_search_call.completed") < Array.IndexOf(names, "response.output_text.delta")).IsTrue();
        await Assert.That(names.Count(name => name == "response.output_text.annotation.added")).IsEqualTo(1);
        await Assert.That(names.Any(name => name!.Contains("function_call_arguments", StringComparison.Ordinal))).IsFalse();
        var completed = frames.Single(frame => frame.GetProperty("type").GetString() == "response.completed").GetProperty("response");
        await AssertWireAsync(completed);
        await Assert.That(completed.GetProperty("output").GetRawText()).IsEqualTo(json.GetProperty("output").GetRawText());
    }
}

#pragma warning restore OPENAI001
