using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Determinism;

public sealed class NativeDeterminismIntegrationTests
{
    private static readonly DateTimeOffset _fixedUtc = new(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);

    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    public async Task NativeJsonResetAndGraphStreamingReplay_PreserveProviderIdentityAsync(int format, bool geminiArray)
    {
        var model = format switch { 0 => LlmTestServerKnownModelIds.ClaudeSonnet55, 1 => "gemini-2.5-flash", 2 => "command-r", _ => "llama3.2" };
        using var host = await LlmTestServerTestHost.StartAsync(config => config.WithDeterminism("native-seed", _fixedUtc)
            .AddModel(model, LlmTestServerModelKind.Chat)
            .AddConversation("native-clock", graph => graph.ForModel(model).Step("answer", step => step.Responds("stable reply", "stable ", "reply").Complete())));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var control = new LlmTestServerClient(http);
        using var conversation = control.ForConversation("native-clock-id");
        string Route(bool stream)
        {
            return format switch
            {
                0 => "/anthropic/v1/messages",
                1 => $"/gemini/v1beta/models/{model}:" + (stream ? "streamGenerateContent" + (geminiArray ? "" : "?alt=sse") : "generateContent"),
                2 => "/cohere/v2/chat",
                _ => "/ollama/api/chat",
            };
        }

        object Body(bool stream)
        {
            return format == 1
                    ? new { contents = new[] { new { role = "user", parts = new[] { new { text = "clock" } } } } }
                    : new { model, max_tokens = 128, stream, messages = new[] { new { role = "user", content = "clock" } } };
        }

        async Task<JsonElement> JsonAsync()
        {
            using var response = await conversation.HttpClient.PostAsJsonAsync(Route(false), Body(false));
            response.EnsureSuccessStatusCode();
            await Assert.That(response.Headers.Date).IsEqualTo(_fixedUtc);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        string? Identity(JsonElement value)
        {
            return format switch
            { 1 => value.GetProperty("responseId").GetString(), 3 => value.GetProperty("created_at").GetString(), _ => value.GetProperty("id").GetString() };
        }

        var first = await JsonAsync();
        var id = Identity(first);
        await Assert.That(id).IsNotNull();
        if (format == 3) { await Assert.That(first.GetProperty("created_at").GetDateTimeOffset()).IsEqualTo(_fixedUtc); }
        await control.ResetAsync();
        var repeated = await JsonAsync();
        await Assert.That(Identity(repeated)).IsEqualTo(id);
        using var streamed = await conversation.HttpClient.PostAsJsonAsync(Route(true), Body(true));
        streamed.EnsureSuccessStatusCode();
        var text = await streamed.Content.ReadAsStringAsync();
        var events = format == 3 ? ReadLines(text) : format == 1 && geminiArray ? ReadArray(text) : ReadSse(text);
        await Assert.That(events.Count).IsGreaterThan(1);
        if (format == 0)
        {
            var start = events.Single(item => item.GetProperty("type").GetString() == "message_start");
            await Assert.That(start.GetProperty("message").GetProperty("id").GetString()).IsEqualTo(id);
            await Assert.That(events.Last().GetProperty("type").GetString()).IsEqualTo("message_stop");
        }
        else if (format == 2)
        {
            await Assert.That(events.First().GetProperty("id").GetString()).IsEqualTo(id);
            await Assert.That(events.Last().GetProperty("type").GetString()).IsEqualTo("message-end");
        }
        else
        {
            await Assert.That(events.All(item => Identity(item) == id)).IsTrue();
            if (format == 3)
            {
                await Assert.That(events.All(item => item.GetProperty("created_at").GetDateTimeOffset() == _fixedUtc)).IsTrue();
                await Assert.That(events.Last().GetProperty("done").GetBoolean()).IsTrue();
            }
            else { await Assert.That(events.Last().GetProperty("candidates")[0].GetProperty("finishReason").GetString()).IsEqualTo("STOP"); }
        }
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        await Assert.That((await conversation.Control.GetAssertionsAsync()).Events.Last().IsReplay).IsTrue();
        await Assert.That((await conversation.Control.GetRequestsAsync()).Requests[0].TerminalMarkerObserved == true).IsTrue();
    }

    [Test]
    public async Task EmbeddingAndImageFixtures_ResetSeededIdsAndUseFixedImageTimestampsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.WithDeterminism("media-seed", _fixedUtc)
            .AddTextEmbedding3Small().AddGptImage1());
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        async Task<JsonElement> PostAsync(string route, object body)
        {
            using var response = await http.PostAsJsonAsync(route, body);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        var embeddingBody = new { model = "text-embedding-3-small", input = "clock" };
        var embedding = await PostAsync("/openai/v1/embeddings", embeddingBody);
        await control.ResetAsync();
        var repeatedEmbedding = await PostAsync("/openai/v1/embeddings", embeddingBody);
        await Assert.That(repeatedEmbedding.GetProperty("id").GetString()).IsEqualTo(embedding.GetProperty("id").GetString());
        var imageBody = new { model = "gpt-image-1", prompt = "fixture" };
        var image = await PostAsync("/openai/v1/images/generations", imageBody);
        await Assert.That(image.GetProperty("created").GetInt64()).IsEqualTo(_fixedUtc.ToUnixTimeSeconds());
        await control.ResetAsync();
        var repeatedImage = await PostAsync("/openai/v1/images/generations", imageBody);
        await Assert.That(repeatedImage.GetProperty("created").GetInt64()).IsEqualTo(_fixedUtc.ToUnixTimeSeconds());
        await Assert.That(repeatedImage.GetProperty("data")[0].GetProperty("b64_json").GetString()).IsEqualTo(image.GetProperty("data")[0].GetProperty("b64_json").GetString());
        using var stream = await http.PostAsJsonAsync("/openai/v1/images/generations", new { model = "gpt-image-1", prompt = "fixture", stream = true, partial_images = 1 });
        stream.EnsureSuccessStatusCode();
        var events = ReadSse(await stream.Content.ReadAsStringAsync());
        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(events.All(item => item.GetProperty("created_at").GetInt64() == _fixedUtc.ToUnixTimeSeconds())).IsTrue();
        await Assert.That(events.Last().GetProperty("type").GetString()).IsEqualTo("image_generation.completed");
    }

    [Test]
    public async Task GroqTranscription_ResetReproducesSeededRequestMetadataAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.WithDeterminism("groq-audio-seed", _fixedUtc)
            .AddModel("whisper-large-v3", LlmTestServerModelKind.Audio).WithDefaultTranscriptionText("clock transcript"));
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        async Task<JsonElement> TranscribeAsync()
        {
            using var form = new MultipartFormDataContent();
            using var audio = new ByteArrayContent(Encoding.ASCII.GetBytes("RIFF....WAVE"));
            audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(audio, "file", "fixture.wav");
            form.Add(new StringContent("whisper-large-v3"), "model");
            form.Add(new StringContent("json"), "response_format");
            using var response = await http.PostAsync("/groq/openai/v1/audio/transcriptions", form);
            response.EnsureSuccessStatusCode();
            await Assert.That(response.Headers.Date).IsEqualTo(_fixedUtc);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        var first = await TranscribeAsync();
        await control.ResetAsync();
        var repeated = await TranscribeAsync();
        await Assert.That(repeated.GetProperty("text").GetString()).IsEqualTo("clock transcript");
        await Assert.That(repeated.GetProperty("x_groq").GetProperty("id").GetString()).IsEqualTo(first.GetProperty("x_groq").GetProperty("id").GetString());
    }

    private static List<JsonElement> ReadSse(string stream)
    {
        return stream.Split('\n').Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
        .Select(line => JsonSerializer.Deserialize<JsonElement>(line[6..])).ToList();
    }

    private static List<JsonElement> ReadLines(string stream)
    {
        return stream.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToList();
    }

    private static List<JsonElement> ReadArray(string stream)
    {
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(item => item.Clone()).ToList();
    }
}
