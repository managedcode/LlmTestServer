using System.ClientModel;
using System.Net;
using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Control;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI.Chat;

namespace ManagedCode.LlmTestServer.Tests.Streaming;

public sealed partial class StreamDeliveryIntegrationTests
{
    [Test]
    public async Task OfficialSdk_GraphReplayRepeatsBrokenDeliveryWithoutAdvancingAndSessionsRemainIsolatedAsync()
    {
        await using var app = await StartTcpHostAsync(_ => { });
        using var http = TcpClient(app);
        var control = new LlmTestServerClient(http);
        static void Configure(LlmTestServerClientConfigurationBuilder config)
        {
            config.UseConversation("stream-graph", graph => graph
            .Step("broken", step => step.WhenLatestUserEquals("go").Responds("first second", "first ", "second")
                .WithStreamDelivery(new() { Mode = LlmTestServerStreamDeliveryMode.EarlyCleanEnd, AfterContentFrames = 1 }).Then("recovery"))
            .Step("recovery", step => step.WhenLatestUserEquals("next").Responds("graph recovered").Complete()));
        }

        await using var firstSession = await control.CreateSessionAsync(Configure);
        await using var secondSession = await control.CreateSessionAsync(Configure);
        using var firstConversation = firstSession.Control.ForConversation("same-label");
        using var secondConversation = secondSession.Control.ForConversation("same-label");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var first = await ReadSdkAsync(Sdk(firstConversation.HttpClient), [new UserChatMessage("go")], null, deadline.Token);
        await Assert.That(first.Text).IsEqualTo("first ");
        await Assert.That(first.Finish).IsNull();
        await Assert.That((await secondSession.Control.GetConversationsAsync()).Count).IsEqualTo(0);
        var replay = await ReadSdkAsync(Sdk(firstConversation.HttpClient), [new UserChatMessage("go")], null, deadline.Token);
        await Assert.That(replay.Text).IsEqualTo(first.Text);
        await Assert.That(replay.Finish).IsNull();
        var firstState = (await firstSession.Control.GetConversationsAsync()).Single();
        await Assert.That(firstState.ExecutedSteps).IsEqualTo(1);
        await Assert.That(firstState.NextSteps.Single()).IsEqualTo("recovery");
        var firstEvidence = await firstSession.Control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(firstEvidence.Requests.Count).IsEqualTo(2);
        await Assert.That(firstEvidence.Requests[0].IsReplay).IsFalse();
        await Assert.That(firstEvidence.Requests[1].IsReplay).IsTrue();
        await Assert.That(firstEvidence.Requests.All(trace => trace.StreamDeliveryApplied && trace.EmittedContentFrames == 1 && trace.TerminalMarkerObserved != true)).IsTrue();
        var second = await ReadSdkAsync(Sdk(secondConversation.HttpClient), [new UserChatMessage("go")], null, deadline.Token);
        await Assert.That(second.Text).IsEqualTo("first ");
        await Assert.That((await secondSession.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        var recovery = (await Sdk(firstConversation.HttpClient).CompleteChatAsync([
            new UserChatMessage("go"), new AssistantChatMessage("first second"), new UserChatMessage("next")])).Value;
        await Assert.That(recovery.Content.Single().Text).IsEqualTo("graph recovered");
        await Assert.That((await firstSession.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
        await Assert.That((await firstSession.Control.GetConversationsAsync()).Single().IsTerminal).IsTrue();
        await Assert.That((await secondSession.Control.GetConversationsAsync()).Single().IsTerminal).IsFalse();
        await Assert.That((await secondSession.Control.GetRequestsAsync()).Requests.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("json")]
    [Arguments("provider")]
    [Arguments("tools")]
    [Arguments("schema")]
    public async Task OfficialSdk_UnsupportedRequestRejectsDeliveryPlanBeforeConsumingFixtureAsync(string kind)
    {
        await using var app = await StartTcpHostAsync(config => config.AddChatScenario("delivery-admission", scenario => scenario
            .Responds("first second", "first ", "second").WithStreamDelivery(new()
            { Mode = LlmTestServerStreamDeliveryMode.EarlyCleanEnd, AfterContentFrames = 1 }).Responds("after actual delivery")));
        using var http = TcpClient(app);
        var control = new LlmTestServerClient(http);
        var sdk = Sdk(http, kind == "provider" ? "/groq/openai/v1" : "/openai/v1");
        var options = new ChatCompletionOptions();
        if (kind == "tools") { options.Tools.Add(ChatTool.CreateFunctionTool("weather")); }
        if (kind == "schema") { options.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(); }
        ClientResultException? failure = null;
        try
        {
            if (kind == "json") { await sdk.CompleteChatAsync([new UserChatMessage("go")], options); }
            else { await foreach (var update in sdk.CompleteChatStreamingAsync([new UserChatMessage("go")], options)) { _ = update; } }
        }
        catch (ClientResultException exception) { failure = exception; }
        await Assert.That(failure?.Status).IsEqualTo(409);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        var rejected = (await control.GetRequestsAsync()).Requests.Single();
        await Assert.That(rejected.StatusCode).IsEqualTo(409);
        await Assert.That(rejected.StreamDeliveryApplied).IsFalse();
        await Assert.That(rejected.EmittedContentFrames).IsEqualTo(0);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var actual = await ReadSdkAsync(Sdk(http), [new UserChatMessage("go")], null, deadline.Token);
        await Assert.That(actual.Text).IsEqualTo("first ");
        await Assert.That(actual.Finish).IsNull();
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(1);
        var recovery = (await Sdk(http).CompleteChatAsync([new UserChatMessage("next")])).Value;
        await Assert.That(recovery.Content.Single().Text).IsEqualTo("after actual delivery");
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    public async Task Configure_InvalidDeliveryPlanIsAtomicAndLeavesPreviousFixtureUsableAsync(int kind)
    {
        await using var app = await StartTcpHostAsync(config => config.AddChatScenario("preserved", scenario => scenario.Responds("still available")));
        using var http = TcpClient(app);
        var control = new LlmTestServerClient(http);
        var delivery = new LlmTestServerStreamDelivery
        { Mode = LlmTestServerStreamDeliveryMode.EarlyCleanEnd, AfterContentFrames = 1 };
        var response = new LlmTestServerScenarioResponse { Content = "first second", StreamChunks = ["first ", "second"], StreamDelivery = delivery };
        switch (kind)
        {
            case 0: delivery = delivery with { Mode = (LlmTestServerStreamDeliveryMode)0 }; break;
            case 1: delivery = delivery with { AfterContentFrames = 0 }; break;
            case 2: delivery = delivery with { AfterContentFrames = 3 }; break;
            case 3: delivery = delivery with { Mode = LlmTestServerStreamDeliveryMode.DelayThenContinue }; break;
            case 4: delivery = delivery with { DelayMilliseconds = 1 }; break;
            case 5: delivery = delivery with { Mode = LlmTestServerStreamDeliveryMode.DelayThenContinue, DelayMilliseconds = 60001 }; break;
            case 6: response = response with { StreamChunks = ["different content"] }; break;
            case 7: response = response with { ToolCalls = [new() { Id = "tool", Name = "weather", ArgumentsJson = "{}" }] }; break;
            case 8: response = response with { Error = new() { StatusCode = 500, Code = "scripted", Message = "failure" } }; break;
            default:
                response = response with
                {
                    Content = string.Empty,
                    StreamChunks = [],
                    OutputItems = [new LlmTestServerResponseWebSearchItem
                { Id = "native", Queries = ["fixture query"] }]
                };
                break;
        }
        response = response with { StreamDelivery = delivery };
        var validBase = new LlmTestServerConfigurationBuilder().Build();
        using var rejected = await http.PostAsJsonAsync(LlmTestServerControlRoutes.Configure,
            validBase with { ChatScenarios = [new() { Id = "invalid", Responses = [response] }] });
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        var preserved = (await Sdk(http).CompleteChatAsync([new UserChatMessage("unchanged")])).Value;
        await Assert.That(preserved.Content.Single().Text).IsEqualTo("still available");
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(1);
    }
}
