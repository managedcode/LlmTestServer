using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Amazon.Runtime.Documents;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Bedrock;

public sealed class BedrockOfficialSdkIntegrationTests
{
    private const string _model = "amazon.nova-lite-v1:0";
    private const string _titan = "amazon.titan-text-express-v1";

    [Test]
    public async Task Converse_OfficialSdkDeserializesMessageStopReasonAndUsageAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options.AddModel(_model, LlmTestServerModelKind.Chat)
            .AddChatScenario("sdk-converse", scenario => scenario.ForModel(_model).WhenUserContains("whale").Responds("blue whale")));
        using var http = Transport(host.GetTestServer().CreateHandler(), host.GetTestServer().BaseAddress);
        using var sdk = Sdk(http);
        var response = await sdk.ConverseAsync(new ConverseRequest
        {
            ModelId = _model,
            Messages = [User("whale")],
            System = [new SystemContentBlock { Text = "Answer briefly." }],
            InferenceConfig = new InferenceConfiguration { MaxTokens = 64, Temperature = 0, TopP = 1, StopSequences = ["END"] },
        });
        await Assert.That(response.HttpStatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Output.Message.Role).IsEqualTo(ConversationRole.Assistant);
        await Assert.That(response.Output.Message.Content.Single().Text).IsEqualTo("blue whale");
        await Assert.That(response.StopReason.Value).IsEqualTo("end_turn");
        await Assert.That(response.Usage.InputTokens ?? 0).IsGreaterThan(0);
        await Assert.That(response.Usage.OutputTokens).IsEqualTo(2);
        await Assert.That(response.Usage.TotalTokens).IsEqualTo(response.Usage.InputTokens + response.Usage.OutputTokens);
        var trace = (await new LlmTestServerClient(http).GetRequestsAsync()).Requests.Single();
        await Assert.That(trace.OperationId).IsEqualTo(LlmTestServerProviderOperationIds.Bedrock.Converse);
        await Assert.That(trace.Messages.Single(message => message.Role == "system").Content).IsEqualTo("Answer briefly.");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InvokeModel_OfficialSdkReadsTitanJsonAndAmazonEventStreamAsync(bool streaming)
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options.AddModel(_titan, LlmTestServerModelKind.Chat)
            .AddChatScenario("sdk-invoke", scenario => scenario.ForModel(_titan).WhenUserContains("whale").Responds("blue whale", "blue ", "whale")));
        using var http = Transport(host.GetTestServer().CreateHandler(), host.GetTestServer().BaseAddress);
        using var sdk = Sdk(http);
        if (streaming)
        {
            using var response = await sdk.InvokeModelWithResponseStreamAsync(new InvokeModelWithResponseStreamRequest
            { ModelId = _titan, ContentType = "application/json", Accept = "application/json", Body = InvokeBody() });
            var chunks = new List<string>();
            response.Body.ChunkReceived += (_, args) =>
            {
                using var payload = JsonDocument.Parse(args.EventStreamEvent.Bytes.ToArray());
                chunks.Add(payload.RootElement.GetProperty("outputText").GetString()!);
            };
            await response.Body.StartProcessingAsync();
            await Assert.That(response.HttpStatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(response.ContentType).IsEqualTo("application/json");
            await Assert.That(string.Join('|', chunks)).IsEqualTo("blue |whale");
        }
        else
        {
            var response = await sdk.InvokeModelAsync(new InvokeModelRequest
            { ModelId = _titan, ContentType = "application/json", Accept = "application/json", Body = InvokeBody() });
            using var payload = await JsonDocument.ParseAsync(response.Body);
            await Assert.That(response.HttpStatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(payload.RootElement.GetProperty("inputTextTokenCount").GetInt32()).IsGreaterThan(0);
            var result = payload.RootElement.GetProperty("results")[0];
            await Assert.That(result.GetProperty("outputText").GetString()).IsEqualTo("blue whale");
            await Assert.That(result.GetProperty("tokenCount").GetInt32()).IsEqualTo(2);
            await Assert.That(result.GetProperty("completionReason").GetString()).IsEqualTo("FINISHED");
        }
        var trace = (await new LlmTestServerClient(http).GetRequestsAsync()).Requests.Single();
        await Assert.That(trace.OperationId).IsEqualTo(streaming ? LlmTestServerProviderOperationIds.Bedrock.InvokeModelWithResponseStream : LlmTestServerProviderOperationIds.Bedrock.InvokeModel);
        await Assert.That(trace.IsStreaming).IsEqualTo(streaming);
        await Assert.That(trace.StatusCode).IsEqualTo(200);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task OfficialSdk_ClientToolRoundUsesExactCallIdArgumentsAndGraphTurnAsync(bool streaming, bool textBeforeTool)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = Transport(host.GetTestServer().CreateHandler(), host.GetTestServer().BaseAddress);
        var control = new LlmTestServerClient(http);
        var configuration = new LlmTestServerClientConfigurationBuilder().UseChatModel(_model).UseConversation("bedrock-sdk-round", graph => graph.ForModel(_model)
            .Step("tool", step => step.WhenLatestUserEquals("weather").WhenTurn(1).CallsTool("weather", "{\"city\":\"Paris\"}", "toolu_weather").Then("final"))
            .Step("final", step => step.WhenTurn(2).WhenToolResult("weather", "toolu_weather").WhenLatestTextContains("21C").Responds("Paris is 21C.").Complete())).Build();
        if (textBeforeTool)
        {
            var steps = configuration.ChatScenarios.Single(scenario => scenario.Id == "bedrock-sdk-round").Conversation!.Steps;
            steps[0] = steps[0] with
            {
                Response = steps[0].Response with
                {
                    Content = "Checking weather.",
                    StreamChunks = ["Checking ", "weather."]
                }
            };
        }
        await control.ConfigureAsync(configuration);
        using var scoped = control.ForConversation("bedrock-sdk-round"); using var sdk = Sdk(scoped.HttpClient);
        var tools = new ToolConfiguration
        {
            Tools = [new Tool { ToolSpec = new ToolSpecification
            { Name = "weather", Description = "Scripted weather", InputSchema = new ToolInputSchema { Json = new Document(new Dictionary<string, Document>
                { ["type"] = "object", ["properties"] = new Document(new Dictionary<string, Document> { ["city"] = new Document(new Dictionary<string, Document> { ["type"] = "string" }) }), ["required"] = new Document(new List<Document> { "city" }) }) } } }],
        };
        ToolUseBlock call;
        if (streaming)
        {
            using var first = await sdk.ConverseStreamAsync(new ConverseStreamRequest { ModelId = _model, Messages = [User("weather")], ToolConfig = tools });
            var id = ""; var name = ""; var arguments = ""; var stop = "";
            var frames = new List<(string Kind, int? Index)>();
            var text = "";
            first.Stream.ContentBlockStartReceived += (_, args) =>
            {
                frames.Add(("start", args.EventStreamEvent.ContentBlockIndex));
                if (args.EventStreamEvent.Start.ToolUse is { } tool)
                {
                    id = tool.ToolUseId;
                    name = tool.Name;
                }
            };
            first.Stream.ContentBlockDeltaReceived += (_, args) =>
            {
                frames.Add(("delta", args.EventStreamEvent.ContentBlockIndex));
                if (args.EventStreamEvent.Delta.ToolUse is { } tool)
                {
                    arguments += tool.Input;
                }
                else
                {
                    text += args.EventStreamEvent.Delta.Text;
                }
            };
            first.Stream.ContentBlockStopReceived += (_, args) => frames.Add(("stop", args.EventStreamEvent.ContentBlockIndex));
            first.Stream.MessageStopReceived += (_, args) => stop = args.EventStreamEvent.StopReason;
            await first.Stream.StartProcessingAsync();
            await Assert.That(stop).IsEqualTo("tool_use");
            await Assert.That(string.Join("|", frames.Select(frame => $"{frame.Kind}:{frame.Index}"))).IsEqualTo(textBeforeTool
                ? "start:0|delta:0|delta:0|stop:0|start:1|delta:1|stop:1"
                : "start:0|delta:0|stop:0");
            await Assert.That(text).IsEqualTo(textBeforeTool ? "Checking weather." : "");
            await Assert.That(arguments).IsEqualTo("{\"city\":\"Paris\"}");
            await Assert.That(JsonDocument.Parse(arguments).RootElement.GetProperty("city").GetString()).IsEqualTo("Paris");
            call = new ToolUseBlock { ToolUseId = id, Name = name, Input = new Document(new Dictionary<string, Document> { ["city"] = "Paris" }) };
        }
        else
        {
            var first = await sdk.ConverseAsync(new ConverseRequest { ModelId = _model, Messages = [User("weather")], ToolConfig = tools });
            await Assert.That(first.StopReason.Value).IsEqualTo("tool_use");
            call = first.Output.Message.Content.Single().ToolUse;
            await Assert.That(call.Input.AsDictionary()["city"].AsString()).IsEqualTo("Paris");
        }
        await Assert.That(call.ToolUseId).IsEqualTo("toolu_weather"); await Assert.That(call.Name).IsEqualTo("weather");
        var final = await sdk.ConverseAsync(new ConverseRequest
        {
            ModelId = _model,
            ToolConfig = tools,
            Messages = [User("weather"),
                new Message { Role = ConversationRole.Assistant, Content = textBeforeTool
                    ? [new ContentBlock { Text = "Checking weather." }, new ContentBlock { ToolUse = call }]
                    : [new ContentBlock { ToolUse = call }] },
                new Message { Role = ConversationRole.User, Content = [new ContentBlock { ToolResult = new ToolResultBlock
                { ToolUseId = call.ToolUseId, Status = ToolResultStatus.Success, Content = [new ToolResultContentBlock { Text = "21C" }] } }] }],
        });
        await Assert.That(final.Output.Message.Content.Single().Text).IsEqualTo("Paris is 21C.");
        await Assert.That(final.StopReason.Value).IsEqualTo("end_turn");
        var conversation = (await scoped.Control.GetConversationsAsync()).Single();
        await Assert.That(conversation.ExecutedSteps).IsEqualTo(2);
        await Assert.That(conversation.IsTerminal).IsTrue();
        var traces = (await scoped.Control.GetRequestsAsync()).Requests;
        await Assert.That(traces.Count).IsEqualTo(2);
        await Assert.That(traces[0].Messages.Single(message => message.Role == "tool").ToolCallId).IsEqualTo(call.ToolUseId);
        await Assert.That(traces[1].ResponseToolCalls.Single().ArgumentsJson).IsEqualTo("{\"city\":\"Paris\"}");
    }

    [Test]
    [Arguments("converse")]
    [Arguments("invoke")]
    [Arguments("stream")]
    public async Task OfficialSdk_ReceivesScripted429AndRecoversOnExplicitNextAttemptAsync(string operation)
    {
        var model = operation == "converse" ? _model : _titan;
        using var host = await LlmTestServerTestHost.StartAsync(options => options.AddModel(model, LlmTestServerModelKind.Chat)
            .AddChatScenario("sdk-error", scenario => scenario.ForModel(model).Fails(429, "too_many_requests", "Retry the fixture.").Responds("blue whale")));
        using var http = Transport(host.GetTestServer().CreateHandler(), host.GetTestServer().BaseAddress); using var sdk = Sdk(http);
        AmazonServiceException? observed = null;
        try { await SendAsync(); } catch (AmazonServiceException exception) { observed = exception; }
        await Assert.That(observed).IsNotNull();
        await Assert.That(observed!.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(observed.Message).Contains("Retry the fixture.");
        await SendAsync();
        var traces = (await new LlmTestServerClient(http).GetRequestsAsync()).Requests;
        await Assert.That(traces.Count).IsEqualTo(2);
        await Assert.That(traces[1].StatusCode).IsEqualTo(429); await Assert.That(traces[0].StatusCode).IsEqualTo(200);
        async Task SendAsync()
        {
            if (operation == "converse")
            {
                var response = await sdk.ConverseAsync(new ConverseRequest { ModelId = model, Messages = [User("whale")] });
                await Assert.That(response.Output.Message.Content.Single().Text).IsEqualTo("blue whale");
            }
            else if (operation == "invoke")
            {
                var response = await sdk.InvokeModelAsync(new InvokeModelRequest { ModelId = model, ContentType = "application/json", Body = InvokeBody() });
                using var payload = await JsonDocument.ParseAsync(response.Body);
                await Assert.That(payload.RootElement.GetProperty("results")[0].GetProperty("outputText").GetString()).IsEqualTo("blue whale");
            }
            else
            {
                using var response = await sdk.InvokeModelWithResponseStreamAsync(new InvokeModelWithResponseStreamRequest { ModelId = model, ContentType = "application/json", Body = InvokeBody() });
                var chunks = new List<string>(); response.Body.ChunkReceived += (_, args) =>
                { using var payload = JsonDocument.Parse(args.EventStreamEvent.Bytes.ToArray()); chunks.Add(payload.RootElement.GetProperty("outputText").GetString()!); };
                await response.Body.StartProcessingAsync(); await Assert.That(string.Concat(chunks)).IsEqualTo("blue whale");
            }
        }
    }

    private static Message User(string text)
    {
        return new() { Role = ConversationRole.User, Content = [new ContentBlock { Text = text }] };
    }

    private static MemoryStream InvokeBody()
    {
        return new(Encoding.UTF8.GetBytes("{\"inputText\":\"whale\",\"textGenerationConfig\":{\"maxTokenCount\":64,\"temperature\":0,\"topP\":1}}"));
    }

    private static HttpClient Transport(HttpMessageHandler handler, Uri address)
    {
        return new(new CanonicalUriHandler(handler)) { BaseAddress = address };
    }

    private static AmazonBedrockRuntimeClient Sdk(HttpClient http)
    {
        return new(new BasicAWSCredentials("test-access-key", "test-secret-key"),
            new AmazonBedrockRuntimeConfig { ServiceURL = new Uri(http.BaseAddress!, LlmTestServerProviderRouteNamespaces.Bedrock).ToString(), AuthenticationRegion = "us-east-1", HttpClientFactory = new TestHttpClientFactory(http), MaxErrorRetry = 0 });
    }

    // ASP.NET TestHost requires a canonical Uri when AWS disables URI canonicalization for signing.
    private sealed class CanonicalUriHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { request.RequestUri = new Uri(request.RequestUri!.AbsoluteUri); return base.SendAsync(request, cancellationToken); }
    }
    private sealed class TestHttpClientFactory(HttpClient client) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig)
        {
            return client;
        }
    }
}
