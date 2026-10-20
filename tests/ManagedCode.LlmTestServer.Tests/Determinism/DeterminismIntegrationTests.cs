using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using OpenAI;
using OpenAI.Chat;

namespace ManagedCode.LlmTestServer.Tests.Determinism;

public sealed class DeterminismIntegrationTests
{
    private static readonly DateTimeOffset _fixedUtc = new(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);

    [Test]
    public async Task OfficialSdk_UsesFixedWireTimeAndResettableSeededIdsWithRealDiagnosticClocksAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.WithDeterminism("sdk-seed", _fixedUtc)
            .AddChatScenario("clock", scenario => scenario.Responds("first").Responds("second")));
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var sdk = new global::OpenAI.Chat.ChatClient("gpt-4.1-mini", new ApiKeyCredential("provider-key"), new OpenAIClientOptions
        { Endpoint = new Uri(http.BaseAddress!, "/openai/v1"), Transport = new HttpClientPipelineTransport(http) });
        var started = DateTimeOffset.UtcNow.AddSeconds(-1);
        var first = (await sdk.CompleteChatAsync([new UserChatMessage("clock")])).Value;
        var second = (await sdk.CompleteChatAsync([new UserChatMessage("clock")])).Value;
        await Assert.That(first.CreatedAt).IsEqualTo(_fixedUtc);
        await Assert.That(second.CreatedAt).IsEqualTo(_fixedUtc);
        await Assert.That(first.Id).IsNotEqualTo(second.Id);
        var finished = DateTimeOffset.UtcNow.AddSeconds(1);
        var events = (await control.GetAssertionsAsync()).Events;
        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(events.All(item => item.Timestamp >= started && item.Timestamp <= finished)).IsTrue();
        var traces = (await http.GetFromJsonAsync<LlmTestServerProviderHttpTrace[]>("/admin-api/requests"))!;
        await Assert.That(traces.Length).IsEqualTo(2);
        await Assert.That(traces.All(trace => trace.StartedAt >= started && trace.CompletedAt <= finished && trace.DurationMilliseconds >= 0)).IsTrue();
        await control.ResetAsync();
        var afterReset = (await sdk.CompleteChatAsync([new UserChatMessage("clock")])).Value;
        await Assert.That(afterReset.Id).IsEqualTo(first.Id);
        await Assert.That(afterReset.CreatedAt).IsEqualTo(_fixedUtc);
        await control.ConfigureAsync(config => config.UseDeterminism("sdk-seed", _fixedUtc)
            .UseChatScenario("clock", scenario => scenario.Responds("first").Responds("second")));
        var afterConfigure = (await sdk.CompleteChatAsync([new UserChatMessage("clock")])).Value;
        await Assert.That(afterConfigure.Id).IsEqualTo(first.Id);
        await Assert.That(afterConfigure.Content[0].Text).IsEqualTo("first");
        using var meai = control.CreateChatClient("gpt-4.1-mini");
        var meaiResult = await meai.GetResponseAsync([new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "clock")]);
        await Assert.That(meaiResult.CreatedAt).IsEqualTo(_fixedUtc);
        await control.ResetAsync();
        await foreach (var update in meai.GetStreamingResponseAsync([new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "clock")]))
        { await Assert.That(update.CreatedAt).IsEqualTo(_fixedUtc); }
    }

    [Test]
    [Arguments("/openai/v1/chat/completions")]
    [Arguments("/groq/openai/v1/chat/completions")]
    [Arguments("/mistral/v1/chat/completions")]
    [Arguments("/openrouter/api/v1/chat/completions")]
    [Arguments("/deepseek/v1/chat/completions")]
    public async Task JsonAndSse_ShareSeededFixtureIdentityAndFixedTimestampAsync(string route)
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.WithDeterminism("transport-seed", _fixedUtc)
            .AddChatScenario("transport", scenario => scenario.Responds("stable reply", "stable ", "reply")));
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var json = await ChatAsync(http, route);
        var id = json.GetProperty("id").GetString();
        await Assert.That(json.GetProperty("created").GetInt64()).IsEqualTo(_fixedUtc.ToUnixTimeSeconds());
        await control.ResetAsync();
        using var stream = await http.PostAsJsonAsync(route, ChatBody(true));
        stream.EnsureSuccessStatusCode();
        var chunks = ReadSse(await stream.Content.ReadAsStringAsync());
        await Assert.That(chunks.Count).IsGreaterThan(1);
        await Assert.That(chunks.All(chunk => chunk.GetProperty("id").GetString() == id
            && chunk.GetProperty("created").GetInt64() == _fixedUtc.ToUnixTimeSeconds())).IsTrue();
        var text = string.Concat(chunks.SelectMany(chunk => chunk.GetProperty("choices").EnumerateArray())
            .Select(choice => choice.GetProperty("delta").TryGetProperty("content", out var content) ? content.GetString() : null));
        await Assert.That(text).IsEqualTo("stable reply");
    }

    [Test]
    [Arguments("/openai/v1/chat/completions")]
    [Arguments("/openai/v1/responses")]
    public async Task ConversationReplay_PreservesWireIdentityAcrossJsonAndSseAsync(string route)
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.WithDeterminism("replay-seed", _fixedUtc)
            .AddConversation("replay", graph => graph.Step("answer", step => step.Responds("stable reply", "stable ", "reply").Complete())));
        using var http = host.GetTestClient();
        using var conversation = new LlmTestServerClient(http).ForConversation("fixed-replay");
        var responses = route.EndsWith("responses", StringComparison.Ordinal);
        object Body(bool stream)
        {
            return responses ? new { model = "gpt-4.1-mini", input = "clock", stream } : ChatBody(stream);
        }

        using var first = await conversation.HttpClient.PostAsJsonAsync(route, Body(false));
        first.EnsureSuccessStatusCode();
        var firstJson = await first.Content.ReadFromJsonAsync<JsonElement>();
        var id = firstJson.GetProperty("id").GetString();
        await Assert.That(firstJson.GetProperty(responses ? "created_at" : "created").GetInt64()).IsEqualTo(_fixedUtc.ToUnixTimeSeconds());
        using var retry = await conversation.HttpClient.PostAsJsonAsync(route, Body(true));
        retry.EnsureSuccessStatusCode();
        var chunks = ReadSse(await retry.Content.ReadAsStringAsync());
        if (responses)
        {
            var completed = chunks.Single(chunk => chunk.GetProperty("type").GetString() == "response.completed").GetProperty("response");
            await Assert.That(completed.GetProperty("id").GetString()).IsEqualTo(id);
            await Assert.That(completed.GetProperty("created_at").GetInt64()).IsEqualTo(_fixedUtc.ToUnixTimeSeconds());
        }
        else
        {
            await Assert.That(chunks.All(chunk => chunk.GetProperty("id").GetString() == id
                && chunk.GetProperty("created").GetInt64() == _fixedUtc.ToUnixTimeSeconds())).IsTrue();
        }
        await Assert.That((await conversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        await Assert.That((await conversation.Control.GetAssertionsAsync()).Events.Last().IsReplay).IsTrue();
    }

    [Test]
    public async Task Sessions_ReproduceSameSeedWithoutSharingResponseCountersOrConfigurationAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var root = new LlmTestServerClient(http);
        await using var first = await root.CreateSessionAsync(config => config.UseDeterminism("session-seed", _fixedUtc)
            .UseChatScenario("seed", scenario => scenario.Responds("one").Responds("two")));
        await using var second = await root.CreateSessionAsync(config => config.UseDeterminism("session-seed", _fixedUtc)
            .UseChatScenario("seed", scenario => scenario.Responds("one").Responds("two")));
        await using var anotherSeed = await root.CreateSessionAsync(config => config.UseDeterminism("different-seed", _fixedUtc)
            .UseChatScenario("seed", scenario => scenario.Responds("one")));
        var a1 = await ChatAsync(first.HttpClient);
        var a2 = await ChatAsync(first.HttpClient);
        var b1 = await ChatAsync(second.HttpClient);
        var b2 = await ChatAsync(second.HttpClient);
        var different = await ChatAsync(anotherSeed.HttpClient);
        await Assert.That(a1.GetProperty("id").GetString()).IsEqualTo(b1.GetProperty("id").GetString());
        await Assert.That(a2.GetProperty("id").GetString()).IsEqualTo(b2.GetProperty("id").GetString());
        await Assert.That(a1.GetProperty("id").GetString()).IsNotEqualTo(different.GetProperty("id").GetString());
        await first.Control.ResetAsync();
        var reset = await ChatAsync(first.HttpClient);
        await Assert.That(reset.GetProperty("id").GetString()).IsEqualTo(a1.GetProperty("id").GetString());
        await Assert.That((await second.Control.GetAssertionsAsync()).Matched).IsEqualTo(2);
        await Assert.That((await root.GetAssertionsAsync()).Matched).IsEqualTo(0);
    }

    [Test]
    public async Task ProviderNamespaces_AvoidWireIdCollisionsAndReproduceAfterResetAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.WithDeterminism("provider-seed", _fixedUtc)
            .AddChatScenario("providers", scenario => scenario.Responds("one").Responds("two")));
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var openAi = await ChatAsync(http, "/openai/v1/chat/completions");
        var groq = await ChatAsync(http, "/groq/openai/v1/chat/completions");
        await Assert.That(openAi.GetProperty("id").GetString()).IsNotEqualTo(groq.GetProperty("id").GetString());
        await control.ResetAsync();
        var repeatOpenAi = await ChatAsync(http, "/openai/v1/chat/completions");
        var repeatGroq = await ChatAsync(http, "/groq/openai/v1/chat/completions");
        await Assert.That(repeatOpenAi.GetProperty("id").GetString()).IsEqualTo(openAi.GetProperty("id").GetString());
        await Assert.That(repeatGroq.GetProperty("id").GetString()).IsEqualTo(groq.GetProperty("id").GetString());
    }

    private static object ChatBody(bool stream = false)
    {
        return new
        { model = "gpt-4.1-mini", stream, messages = new[] { new { role = "user", content = "clock" } } };
    }

    private static async Task<JsonElement> ChatAsync(HttpClient http, string route = "/openai/v1/chat/completions")
    {
        using var response = await http.PostAsJsonAsync(route, ChatBody());
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<JsonElement> ReadSse(string stream)
    {
        return stream.Split('\n')
        .Where(line => line.StartsWith("data: ", StringComparison.Ordinal) && line != "data: [DONE]")
        .Select(line => JsonSerializer.Deserialize<JsonElement>(line[6..])).ToList();
    }
}
