#pragma warning disable OPENAI001 // Official Responses client adapter is still marked experimental.

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.Azure;

public sealed class ResponsesCallerManagedHistoryTests
{
    private const string _key = "caller-history-test-key";
    private const string _prompt = "review-caller-owned-history";
    private const string _evidence = "document-evidence-417";
    private const string _tool = "read_document";
    private static readonly string[] _includes =
    [
        "file_search_call.results", "web_search_call.results", "web_search_call.action.sources",
        "message.input_image.image_url", "computer_call_output.output.image_url",
        "code_interpreter_call.outputs", "reasoning.encrypted_content", "message.output_text.logprobs"
    ];

    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.OpenAI, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.OpenAI, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, true)]
    public async Task OfficialSdk_StatelessToolAndConversationFlowPreservesExplicitHistoryAsync(string provider, bool streaming)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options.RequireBearerToken(_key)
            .AddModel(LlmTestServerKnownModelIds.Gpt41Mini, LlmTestServerModelKind.Chat)
            .AddChatScenario(_prompt, scenario => scenario.ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                .WhenUserContains(_prompt).CallsTool(_tool, "{}", "call_document")
                .Responds("reviewed").Responds("continued")));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", _key);
        var sdk = new ResponsesClient(new ApiKeyCredential(_key), new ResponsesClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, ResponsePath(provider)),
            Transport = new HttpClientPipelineTransport(http)
        });
        using var client = sdk.AsIChatClient(LlmTestServerKnownModelIds.Gpt41Mini).AsBuilder()
            .ConfigureOptions(options => options.RawRepresentationFactory = _ => new CreateResponseOptions
            {
                StoredOutputEnabled = false,
                IncludedProperties = { IncludedResponseProperty.ReasoningEncryptedContent }
            }).UseFunctionInvocation().Build();
        var chatOptions = new ChatOptions { Tools = [AIFunctionFactory.Create(() => _evidence, _tool)] };
        List<ChatMessage> history = [new(ChatRole.User, _prompt)];
        var first = streaming
            ? await client.GetStreamingResponseAsync(history, chatOptions).ToChatResponseAsync()
            : await client.GetResponseAsync(history, chatOptions);
        await Assert.That(first.Text).IsEqualTo("reviewed");
        history.AddRange(first.Messages);
        history.Add(new(ChatRole.User, "continue using the retained document evidence"));
        var second = streaming
            ? await client.GetStreamingResponseAsync(history, chatOptions).ToChatResponseAsync()
            : await client.GetResponseAsync(history, chatOptions);
        await Assert.That(second.Text).IsEqualTo("continued");
        var events = (await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!.Events;
        await Assert.That(events.Count).IsEqualTo(3);
        await Assert.That(events.All(item => item.Kind == LlmTestServerEventKind.Matched)).IsTrue();
        var retained = events.Last().Messages;
        await Assert.That(retained.Any(message => message.Role == "user" && message.Content == _prompt)).IsTrue();
        await Assert.That(retained.Single(message => message.Role == "tool").Content).IsEqualTo(JsonSerializer.Serialize(_evidence));
        await Assert.That(retained.Any(message => message.Role == "assistant" && message.Content == "reviewed")).IsTrue();
    }

    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.OpenAI)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry)]
    public async Task HttpSelectors_RejectMalformedThenCompleteAndContinueWithDocumentedIncludesAsync(string provider)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
            .AddModel(LlmTestServerKnownModelIds.Gpt41Mini, LlmTestServerModelKind.Chat)
            .AddChatScenario(_prompt, scenario => scenario.ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                .WhenUserContains(_prompt).Responds("first reply").Responds("second reply")));
        using var http = host.GetTestClient();
        var path = ResponsePath(provider) + "responses";
        foreach (var malformed in new[] { "[\"reasoning.unsupported\"]", "[42]", "\"reasoning.encrypted_content\"", "null" })
        {
            using var include = JsonDocument.Parse(malformed);
            using var rejected = await http.PostAsJsonAsync(path, new
            { model = LlmTestServerKnownModelIds.Gpt41Mini, input = _prompt, store = false, include = include.RootElement });
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            var failure = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            await Assert.That(failure.GetProperty("error").GetProperty("code").GetString()).IsEqualTo("invalid_request");
        }
        using var first = await http.PostAsJsonAsync(path, new
        { model = LlmTestServerKnownModelIds.Gpt41Mini, input = _prompt, store = false, include = _includes });
        first.EnsureSuccessStatusCode();
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var assistant = firstBody.GetProperty("output")[0];
        await Assert.That(assistant.GetProperty("content")[0].GetProperty("text").GetString()).IsEqualTo("first reply");
        await Assert.That(firstBody.GetProperty("output").GetArrayLength()).IsEqualTo(1);
        using var unavailable = await http.PostAsJsonAsync(path, new
        {
            model = LlmTestServerKnownModelIds.Gpt41Mini,
            input = "continue",
            previous_response_id = firstBody.GetProperty("id").GetString(),
            include = _includes
        });
        await Assert.That(unavailable.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var unavailableBody = await unavailable.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(unavailableBody.GetProperty("error").GetProperty("code").GetString()).IsEqualTo("response_not_found");
        object[] history = [new { role = "user", content = _prompt }, assistant, new { role = "user", content = "continue" }];
        using var second = await http.PostAsJsonAsync(path, new
        { model = LlmTestServerKnownModelIds.Gpt41Mini, input = history, store = false, include = _includes });
        second.EnsureSuccessStatusCode();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(secondBody.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString()).IsEqualTo("second reply");
        var events = (await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!.Events;
        await Assert.That(events.Count).IsEqualTo(3);
        await Assert.That(events.Count(item => item.Kind == LlmTestServerEventKind.Matched)).IsEqualTo(2);
        await Assert.That(events.Count(item => item.Kind == LlmTestServerEventKind.ErrorReturned)).IsEqualTo(1);
        await Assert.That(events.Last().Messages.Any(message => message.Role == "assistant" && message.Content == "first reply")).IsTrue();
    }

    private static string ResponsePath(string provider)
    {
        return provider.TrimStart('/') + (provider == LlmTestServerProviderRouteNamespaces.OpenAI ? "/v1/" : "/openai/v1/");
    }
}
