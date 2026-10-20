using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.SystemOne;
using OpenAI;
using OpenAI.Chat;
using TUnit.Core;

public sealed class TypeSafeAndDeliveryPackageTests
{
    private const string _key = "test-key";

    [Test]
    public async Task InstalledPackages_ReadmeTypeSafeJev114ExampleReturnsNativePredicateAsync(CancellationToken cancellationToken)
    {
        var builder = DistributedApplicationTestingBuilder.Create([]);
        builder.AddLlmTestServer("typesafe-readme-package-server").WithApiKey(_key);
        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("typesafe-readme-package-server", cancellationToken);
        using var http = app.CreateHttpClient("typesafe-readme-package-server", LlmTestServerResource.HttpEndpointName);
        var control = new LlmTestServerClient(http, _key);
        await control.ConfigureAsync(config => config
            .RequireBearerToken("test-key")
            .UseDecisionModel(TypeSafeRequestValidation.Jev114Model, LlmTestServerDecisionProvider.TypeSafe)
            .UseDecisionScenario(new LlmTestServerDecisionScenario
            {
                Id = "typesafe-priority",
                ModelId = TypeSafeRequestValidation.Jev114Model,
                Answers = new()
                {
                    ["urgent"] = new() { Kind = LlmTestServerDecisionKind.Predicate, Probability = 0.95 },
                },
            }), cancellationToken);

        var jev = control.CreateSystemOneClient();
        var decision = await jev.EvaluateAsync(new SystemOneDecisionRequest
        {
            Model = TypeSafeRequestValidation.Jev114Model,
            State = JsonSerializer.SerializeToElement("Please help quickly"),
            Questions = new()
            {
                ["urgent"] = new()
                {
                    Type = SystemOneDecisionTypes.Predicate,
                    Instructions = JsonSerializer.SerializeToElement("Does this need urgent attention?"),
                },
            },
        }, cancellationToken: cancellationToken);
        if (decision.Model != TypeSafeRequestValidation.Jev114Model || decision.Answers.Count != 1
            || decision.Answers["urgent"].Type != SystemOneDecisionTypes.Predicate || decision.Answers["urgent"].Probability != 0.95)
            throw new InvalidOperationException("Installed packages did not preserve the README TypeSafe/Jev 1.14 native predicate example.");
        var summary = await control.GetAssertionsAsync(cancellationToken);
        if (summary.Matched != 1 || summary.ErrorsReturned != 0)
            throw new InvalidOperationException("The installed TypeSafe/Jev fixture must produce exactly one successful matched decision.");
        var trace = (await control.GetRequestsAsync(cancellationToken: cancellationToken)).Requests.Single();
        if (trace.Path != "/systemone/v1/systemone" || trace.OperationId != ManagedCode.LlmTestServer.Providers.LlmTestServerProviderOperationIds.SystemOne.DecisionsCreate
            || trace.ModelId != TypeSafeRequestValidation.Jev114Model || trace.StatusCode != 200 || trace.ScenarioId != "typesafe-priority")
            throw new InvalidOperationException("The installed README native request is missing its provider/model/scenario trace evidence.");
    }

    [Test]
    public async Task InstalledPackages_PublicStreamDeliveryPlanProducesPartialSdkStreamThenExplicitRecoveryAsync(CancellationToken cancellationToken)
    {
        var builder = DistributedApplicationTestingBuilder.Create([]);
        builder.AddLlmTestServer("stream-delivery-package-server").WithApiKey(_key);
        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("stream-delivery-package-server", cancellationToken);
        using var http = app.CreateHttpClient("stream-delivery-package-server", LlmTestServerResource.HttpEndpointName);
        var control = new LlmTestServerClient(http, _key);
        await control.ConfigureAsync(config => config.RequireBearerToken(_key)
            .UseChatScenario("package-stream-delivery", scenario => scenario
                .Responds("first second", "first ", "second")
                .WithStreamDelivery(new() { Mode = LlmTestServerStreamDeliveryMode.EarlyCleanEnd, AfterContentFrames = 1 })
                .Responds("installed package recovery")), cancellationToken);
        var sdk = new ChatClient(LlmTestServerKnownModelIds.Gpt41Mini, new ApiKeyCredential(_key), new OpenAIClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/openai/v1"),
            Transport = new HttpClientPipelineTransport(http),
            RetryPolicy = new ClientRetryPolicy(0),
        });
        var text = new StringBuilder();
        ChatFinishReason? finish = null;
        await foreach (var update in sdk.CompleteChatStreamingAsync([new UserChatMessage("stream")], cancellationToken: cancellationToken))
        {
            foreach (var part in update.ContentUpdate) { text.Append(part.Text); }
            if (update.FinishReason is { } reason) { finish = reason; }
        }
        if (text.ToString() != "first " || finish is not null)
            throw new InvalidOperationException("The installed delivery plan must end after the first real SDK content update without a finish frame.");
        var trace = await CompletedTraceAsync(control, cancellationToken);
        if (trace.StatusCode != 200 || trace.TerminalMarkerObserved == true
            || trace.StreamDelivery?.Mode != LlmTestServerStreamDeliveryMode.EarlyCleanEnd
            || !trace.StreamDeliveryApplied || trace.EmittedContentFrames != 1
            || trace.ResponsePreview?.Contains("[DONE]", StringComparison.Ordinal) != false)
            throw new InvalidOperationException("Installed package delivery diagnostics must distinguish the incomplete protocol from HTTP 200.");
        var verification = await control.VerifyAsync(new()
        {
            Requests = [new() { Query = new() { RequestId = trace.RequestId }, RequireStreamingTerminalMarker = true }],
        }, cancellationToken);
        if (verification.Passed || !verification.Failures.Any(failure => failure.Code == "stream.terminal" && failure.RequestIds.Contains(trace.RequestId)))
            throw new InvalidOperationException("Installed package verification must reject a stream without its terminal marker.");
        if ((await control.GetAssertionsAsync(cancellationToken)).Matched != 1)
            throw new InvalidOperationException("Post-header delivery failure must retain its one committed inference without SDK retries.");
        var recovered = (await sdk.CompleteChatAsync([new UserChatMessage("explicit retry")], cancellationToken: cancellationToken)).Value;
        if (recovered.Content.Single().Text != "installed package recovery" || (await control.GetAssertionsAsync(cancellationToken)).Matched != 2
            || (await control.GetRequestsAsync(cancellationToken: cancellationToken)).TotalObserved != 2)
            throw new InvalidOperationException("The explicit SDK retry must consume the next installed fixture exactly once.");
    }

    private static async Task<LlmTestServerProviderHttpTrace> CompletedTraceAsync(LlmTestServerClient control, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            var evidence = await control.GetRequestsAsync(cancellationToken: deadline.Token);
            if (evidence.Requests.Count == 1 && evidence.Requests[0].IsCompleted) { return evidence.Requests[0]; }
            deadline.Token.ThrowIfCancellationRequested();
        }
    }
}
