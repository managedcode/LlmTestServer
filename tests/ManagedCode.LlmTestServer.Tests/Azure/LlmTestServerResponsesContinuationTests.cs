using System.Net.Http.Json;
#pragma warning disable OPENAI001 // Official Responses client adapter is still marked experimental.

using System.ClientModel;
using System.ClientModel.Primitives;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.Azure;

public sealed class LlmTestServerResponsesContinuationTests
{
    private const string _key = "continuation-test-key";
    private const string _prompt = "review-file-continuation";
    private const string _evidence = "private-document-evidence-417";
    private const string _toolName = "read_document";

    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.OpenAI, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.OpenAI, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, true)]
    public async Task OfficialMeaiToolLoop_RestoresPreviousResponseContextAsync(string provider, bool streaming)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options.RequireBearerToken(_key)
            .AddModel(LlmTestServerKnownModelIds.Gpt41Mini, LlmTestServerModelKind.Chat)
            .AddChatScenario(_prompt, scenario => scenario.ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                .WhenUserContains(_prompt).CallsTool(_toolName, "{}", "call_document").Responds("reviewed")));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _key);
        var apiRoot = provider == LlmTestServerProviderRouteNamespaces.OpenAI ? "/v1/" : "/openai/v1/";
        var sdk = new ResponsesClient(new ApiKeyCredential(_key), new ResponsesClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, provider.TrimStart('/') + apiRoot),
            Transport = new HttpClientPipelineTransport(http)
        });
        using var client = sdk.AsIChatClient(LlmTestServerKnownModelIds.Gpt41Mini).AsBuilder().UseFunctionInvocation().Build();
        var chatOptions = new ChatOptions { Tools = [AIFunctionFactory.Create(() => _evidence, _toolName)] };
        var picture = new LlmTestServerConfiguration().DefaultImageDataUri;
        ChatMessage[] input = [new(ChatRole.User, [new TextContent(_prompt),
            new DataContent(Convert.FromBase64String(picture[(picture.IndexOf(',') + 1)..]), "image/png")])];
        if (streaming)
        {
            var text = string.Empty;
            await foreach (var update in client.GetStreamingResponseAsync(input, chatOptions)) { text += update.Text; }
            await Assert.That(text).IsEqualTo("reviewed");
        }
        else
        {
            await Assert.That((await client.GetResponseAsync(input, chatOptions)).Text).IsEqualTo("reviewed");
        }
        var events = (await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!.Events;
        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(events.All(item => item.Kind == LlmTestServerEventKind.Matched)).IsTrue();
        await Assert.That(events.Last().Messages.Any(message => message.Role == "user" && message.Content.Contains(_prompt, StringComparison.Ordinal) && message.Content.Contains(picture, StringComparison.Ordinal))).IsTrue();
        await Assert.That(events.Last().Messages.Single(message => message.Role == "tool").Content).Contains(_evidence);
    }

    [Test]
    [Arguments(LlmTestServerProviderRouteNamespaces.OpenAI, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.OpenAI, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.AzureOpenAI, true)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, false)]
    [Arguments(LlmTestServerProviderRouteNamespaces.MicrosoftFoundry, true)]
    public async Task OfficialMeaiContinuation_AllowsModelChangeAndPreservesHistoryAsync(string provider, bool streaming)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options.RequireBearerToken(_key)
            .AddModel(LlmTestServerKnownModelIds.Gpt41Mini, LlmTestServerModelKind.Chat)
            .AddModel(LlmTestServerKnownModelIds.Gpt41, LlmTestServerModelKind.Chat)
            .AddChatScenario("initial-model", scenario => scenario.ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                .WhenUserContains(_prompt).Responds("initial reply"))
            .AddChatScenario("updated-model", scenario => scenario.ForModel(LlmTestServerKnownModelIds.Gpt41)
                .WhenUserContains(_prompt).Responds("updated reply")));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _key);
        var apiRoot = provider == LlmTestServerProviderRouteNamespaces.OpenAI ? "/v1/" : "/openai/v1/";
        var sdk = new ResponsesClient(new ApiKeyCredential(_key), new ResponsesClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, provider.TrimStart('/') + apiRoot),
            Transport = new HttpClientPipelineTransport(http)
        });
        using var client = sdk.AsIChatClient(LlmTestServerKnownModelIds.Gpt41Mini);
        var first = await client.GetResponseAsync(_prompt);
        await Assert.That(first.ConversationId).IsNotNull();
        var options = new ChatOptions { ModelId = LlmTestServerKnownModelIds.Gpt41, ConversationId = first.ConversationId };
        var result = streaming
            ? await client.GetStreamingResponseAsync("continue on the updated model", options).ToChatResponseAsync()
            : await client.GetResponseAsync("continue on the updated model", options);

        await Assert.That(result.Text).IsEqualTo("updated reply");
        await Assert.That(result.ModelId).IsEqualTo(LlmTestServerKnownModelIds.Gpt41);
        var events = (await http.GetFromJsonAsync<LlmTestServerAssertionSummary>("/admin-api/assertions"))!.Events;
        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(events.All(item => item.Kind == LlmTestServerEventKind.Matched)).IsTrue();
        await Assert.That(events.Last().Messages.Any(message => message.Role == "user" && message.Content == _prompt)).IsTrue();
        await Assert.That(events.Last().Messages.Any(message => message.Role == "assistant" && message.Content == "initial reply")).IsTrue();
    }
}
