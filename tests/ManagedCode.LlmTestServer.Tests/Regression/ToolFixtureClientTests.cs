using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.AI;

namespace ManagedCode.LlmTestServer.Tests.Regression;

// Responses remains experimental in the official SDK.
#pragma warning disable OPENAI001
public sealed class ToolFixtureClientTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExtensionsAi_RespectsSingleToolOptionWithoutConsumingFixtureAsync(bool stream)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("parallel", s => s.CallsTools(
            new LlmTestServerToolCall { Id = "first", Name = "weather" }, new LlmTestServerToolCall { Id = "second", Name = "weather" })));
        using var http = host.GetTestClient();
        using var client = new LlmTestServerChatClient(http);
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(() => "sunny", "weather")], AllowMultipleToolCalls = false };
        async Task<ChatResponse> SendAsync()
        {
            return stream
            ? await client.GetStreamingResponseAsync("weather", options).ToChatResponseAsync()
            : await client.GetResponseAsync("weather", options);
        }

        await Assert.That(async () => await SendAsync()).Throws<HttpRequestException>();
        options.AllowMultipleToolCalls = true;
        var accepted = await SendAsync();
        await Assert.That(accepted.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Select(call => call.CallId))
            .IsEquivalentTo(new[] { "first", "second" });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExtensionsAi_InvokesToolAndReturnsValidatedJsonAsync(bool stream)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("weather", s => s.CallsTool("weather", """{"city":"Paris"}""").RespondsJson("""{"forecast":"sunny"}""")));
        using var http = host.GetTestClient();
        using var client = new ChatClientBuilder(new LlmTestServerChatClient(http)).UseFunctionInvocation().Build();
        var calls = new List<string>();
        var tool = AIFunctionFactory.Create((string city) => { calls.Add(city); return "sunny"; }, "weather");
        var options = new ChatOptions
        {
            Tools = [tool],
            ResponseFormat = new ChatResponseFormatJson(JsonSerializer.Deserialize<JsonElement>("""{"type":"object","required":["forecast"],"properties":{"forecast":{"const":"sunny"}}}""")),
        };
        var response = stream ? await client.GetStreamingResponseAsync("weather", options).ToChatResponseAsync() : await client.GetResponseAsync("weather", options);
        await Assert.That(response.Text).IsEqualTo("""{"forecast":"sunny"}""");
        await Assert.That(calls).IsEquivalentTo(new[] { "Paris" });
        await Assert.That(response.Usage!.InputTokenCount!.Value).IsGreaterThan(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OfficialOpenAiSdk_ParsesToolCallAndStreamingArgumentsAsync(bool stream)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("weather", s => s.CallsTool("weather", """{"city":"Paris"}""")));
        using var http = host.GetTestClient();
        var client = new global::OpenAI.Chat.ChatClient(LlmTestServerKnownModelIds.Gpt41Mini,
            new System.ClientModel.ApiKeyCredential("test-key"), new global::OpenAI.OpenAIClientOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/openai/v1"),
                Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(http),
            });
        var options = new global::OpenAI.Chat.ChatCompletionOptions
        {
            Tools = { global::OpenAI.Chat.ChatTool.CreateFunctionTool("weather", functionParameters: BinaryData.FromString("""{"type":"object","properties":{"city":{"type":"string"}}}""")) },
            ToolChoice = global::OpenAI.Chat.ChatToolChoice.CreateFunctionChoice("weather"),
        };
        if (stream)
        {
            var arguments = new System.Text.StringBuilder();
            var names = new List<string>();
            global::OpenAI.Chat.ChatFinishReason? finish = null;
            await foreach (var update in client.CompleteChatStreamingAsync([new global::OpenAI.Chat.UserChatMessage("weather")], options))
            {
                foreach (var call in update.ToolCallUpdates)
                {
                    arguments.Append(call.FunctionArgumentsUpdate); if (call.FunctionName is not null)
                    {
                        names.Add(call.FunctionName);
                    }
                }
                finish = update.FinishReason ?? finish;
            }
            await Assert.That(arguments.ToString()).IsEqualTo("""{"city":"Paris"}""");
            await Assert.That(names).IsEquivalentTo(new[] { "weather" });
            await Assert.That(finish).IsEqualTo(global::OpenAI.Chat.ChatFinishReason.ToolCalls);
        }
        else
        {
            var response = (await client.CompleteChatAsync([new global::OpenAI.Chat.UserChatMessage("weather")], options)).Value;
            await Assert.That(response.ToolCalls.Single().FunctionName).IsEqualTo("weather");
            await Assert.That(response.ToolCalls.Single().FunctionArguments.ToString()).IsEqualTo("""{"city":"Paris"}""");
            await Assert.That(response.FinishReason).IsEqualTo(global::OpenAI.Chat.ChatFinishReason.ToolCalls);
        }
    }
    [Test]
    [Arguments("/openai/v1")]
    [Arguments("/azure-openai/openai/v1")]
    [Arguments("/microsoft-foundry/openai/v1")]
    public async Task OfficialResponsesSdk_ParsesFunctionCallsAndStreamAsync(string endpoint)
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("weather", s => s.CallsTool("weather", "{}").CallsTool("weather", "{}")));
        using var http = host.GetTestClient();
        var client = new global::OpenAI.Responses.ResponsesClient(new System.ClientModel.ApiKeyCredential("test-key"), new global::OpenAI.Responses.ResponsesClientOptions
        { Endpoint = new Uri(http.BaseAddress!, endpoint), Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(http) });
        var options = new global::OpenAI.Responses.CreateResponseOptions
        {
            Model = LlmTestServerKnownModelIds.Gpt41Mini,
            InputItems = { global::OpenAI.Responses.ResponseItem.CreateUserMessageItem("weather") },
            Tools = { global::OpenAI.Responses.ResponseTool.CreateFunctionTool("weather", BinaryData.FromString("{\"type\":\"object\"}"), null, null) },
        };
        var response = (await client.CreateResponseAsync(options)).Value;
        var call = response.OutputItems.OfType<global::OpenAI.Responses.FunctionCallResponseItem>().Single();
        await Assert.That(call.FunctionName).IsEqualTo("weather");
        await Assert.That(call.CallId).IsEqualTo("call_fixture");
        await Assert.That(call.FunctionArguments.ToString()).IsEqualTo("{}");
        options.StreamingEnabled = true;
        var arguments = new System.Text.StringBuilder();
        await foreach (var update in client.CreateResponseStreamingAsync(options))
        {
            if (update is global::OpenAI.Responses.StreamingResponseFunctionCallArgumentsDeltaUpdate delta)
            {
                arguments.Append(delta.Delta);
            }
        }

        await Assert.That(arguments.ToString()).IsEqualTo("{}");
    }

    [Test]
    public async Task OfficialAzureSdk_ParsesFunctionToolCallsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(b => b.AddChatScenario("weather", s => s.CallsTool("weather", "{}")));
        using var http = host.GetTestClient();
        var azure = new global::Azure.AI.OpenAI.AzureOpenAIClient(new Uri(http.BaseAddress!, "/azure-openai"), new global::Azure.AzureKeyCredential("test-key"),
            new global::Azure.AI.OpenAI.AzureOpenAIClientOptions
            { Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(http) });
        var response = (await azure.GetChatClient(LlmTestServerKnownModelIds.Gpt41Mini).CompleteChatAsync([new global::OpenAI.Chat.UserChatMessage("weather")], new()
        { Tools = { global::OpenAI.Chat.ChatTool.CreateFunctionTool("weather", functionParameters: BinaryData.FromString("{\"type\":\"object\"}")) } })).Value;
        await Assert.That(response.ToolCalls.Single().FunctionName).IsEqualTo("weather");
    }

}
