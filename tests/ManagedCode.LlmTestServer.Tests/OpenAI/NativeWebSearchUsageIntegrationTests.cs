#pragma warning disable OPENAI001 // Verify native SDK extension preservation through HTTP.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Control;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeWebSearchIntegrationTests
{
    [Test]
    [Arguments(-1, false)]
    [Arguments(0, false)]
    [Arguments(3, false)]
    [Arguments(-1, true)]
    [Arguments(0, true)]
    [Arguments(3, true)]
    public async Task AzureResponses_ReportsExplicitBillableRequestsIndependentOfCallsAsync(int count, bool streaming)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseGpt41Mini().UseChatScenario("azure-meter",
            scenario => scenario.RespondsWithItems(Items()).WithWebSearchRequests(count < 0 ? null : count)));
        var request = Request(stream: streaming);
        using var response = await http.PostAsJsonAsync("/azure-openai/openai/v1/responses", request);
        response.EnsureSuccessStatusCode();
        JsonElement terminal;
        if (streaming)
        {
            var frames = (await response.Content.ReadAsStringAsync()).Split('\n')
                .Where(line => line.StartsWith("data: ", StringComparison.Ordinal) && line != "data: [DONE]")
                .Select(line => JsonDocument.Parse(line[6..]).RootElement.Clone()).ToArray();
            foreach (var frame in frames.Where(frame => frame.GetProperty("type").GetString() is "response.created" or "response.in_progress"))
            {
                await Assert.That(frame.GetProperty("response").TryGetProperty("tool_usage", out _)).IsFalse();
            }
            terminal = frames.Single(frame => frame.GetProperty("type").GetString() == "response.completed").GetProperty("response");
        }
        else { terminal = await response.Content.ReadFromJsonAsync<JsonElement>(); }
        await AssertWireAsync(terminal);
        await Assert.That(terminal.GetProperty("output").EnumerateArray().Count(item => item.GetProperty("type").GetString() == "web_search_call")).IsEqualTo(1);
        await Assert.That(terminal.TryGetProperty("tool_usage", out var usage)).IsEqualTo(count >= 0);
        if (count >= 0) { await Assert.That(usage.GetProperty("web_search").GetProperty("num_requests").GetInt32()).IsEqualTo(count); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AzureResponses_OfficialSdkRetainsExactMeterInTerminalResponseAsync(bool streaming)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(config => config.UseGpt41Mini().UseChatScenario("sdk-meter",
            scenario => scenario.RespondsWithItems(Items()).WithWebSearchRequests(3)));
        var sdk = new ResponsesClient(new ApiKeyCredential("provider-key"), new ResponsesClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/azure-openai/openai/v1/"),
            Transport = new HttpClientPipelineTransport(http),
        });
        var options = Options();
        options.StreamingEnabled = streaming;
        ResponseResult? result = null;
        if (streaming)
        {
            await foreach (var update in sdk.CreateResponseStreamingAsync(options))
            {
                if (update is StreamingResponseCompletedUpdate completed) { result = completed.Response; }
            }
        }
        else { result = (await sdk.CreateResponseAsync(options)).Value; }
        await Assert.That(result).IsNotNull();
        using var json = JsonDocument.Parse(ModelReaderWriter.Write(result!, ModelReaderWriterOptions.Json).ToMemory());
        await Assert.That(json.RootElement.GetProperty("tool_usage").GetProperty("web_search").GetProperty("num_requests").GetInt32()).IsEqualTo(3);
    }

    [Test]
    [Arguments(-1, false)]
    [Arguments(0, false)]
    [Arguments(3, false)]
    [Arguments(-1, true)]
    [Arguments(0, true)]
    [Arguments(3, true)]
    public async Task AzureResponses_MeaiNativeSearchAcceptsConfiguredDeploymentAliasAsync(int count, bool streaming)
    {
        const string deployment = "configured-hosted-model";
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(config => config.UseChatModel(deployment).UseChatScenario("azure-meai-meter",
            scenario => scenario.ForModel(deployment).RespondsWithItems(Items()).WithWebSearchRequests(count < 0 ? null : count)));
        using var client = new ResponsesClient(new ApiKeyCredential("provider-key"), new ResponsesClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/azure-openai/openai/v1/"),
            Transport = new HttpClientPipelineTransport(http),
        }).AsIChatClient(deployment);
        var options = new ChatOptions
        {
            Tools = [new HostedWebSearchTool()],
            MaxOutputTokens = 128,
            RawRepresentationFactory = _ => new CreateResponseOptions { MaxToolCallCount = 1 },
        };
        ResponseResult? result = null;
        if (streaming)
        {
            await foreach (var update in client.GetStreamingResponseAsync([new(ChatRole.User, "start")], options))
            {
                if (update.RawRepresentation is StreamingResponseCompletedUpdate completed) { result = completed.Response; }
            }
        }
        else { result = (await client.GetResponseAsync([new(ChatRole.User, "start")], options)).RawRepresentation as ResponseResult; }
        await Assert.That(result).IsNotNull();
        using var json = JsonDocument.Parse(ModelReaderWriter.Write(result!, ModelReaderWriterOptions.Json).ToMemory());
        var wire = json.RootElement;
        await Assert.That(wire.GetProperty("output").EnumerateArray().Count(item => item.GetProperty("type").GetString() == "web_search_call")).IsEqualTo(1);
        await Assert.That(wire.TryGetProperty("tool_usage", out var usage)).IsEqualTo(count >= 0);
        if (count >= 0) { await Assert.That(usage.GetProperty("web_search").GetProperty("num_requests").GetInt32()).IsEqualTo(count); }
    }

    [Test]
    public async Task OpenAiResponses_DoesNotEmitAzureSpecificMeterAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(config => config.UseGpt41Mini().UseChatScenario("openai-meter",
            scenario => scenario.RespondsWithItems(Items()).WithWebSearchRequests(3)));
        var response = await CompleteAsync(http);
        await Assert.That(response.TryGetProperty("tool_usage", out _)).IsFalse();
    }

    [Test]
    public async Task ControlApi_RejectsNegativeBillableRequestFixtureAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var json = JsonSerializer.SerializeToNode(Configuration(), _configurationJson)!;
        json["chatScenarios"]![0]!["responses"]![0]!["webSearchRequests"] = -1;
        using var rejected = await http.PostAsJsonAsync(LlmTestServerControlRoutes.Configure, json);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }
}
