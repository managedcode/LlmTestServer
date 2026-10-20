using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI;
using OpenAI.Chat;
using TUnit.Core;

public sealed class DeepSeekOutputPolicyPackageTests
{
    private const string _key = "deepseek-policy-package-key";
    private const string _model = "deepseek-flash";
    private const string _path = "/deepseek/v1/chat/completions";
    private const string _text = "Explicit installed DeepSeek prefix";
    private const string _recovery = "Installed DeepSeek recovery";

    [Test]
    public async Task InstalledPackages_DeepSeekFilteredStreamRetainsUsageAndExplicitJsonRecoveryAsync(CancellationToken cancellationToken)
    {
        const string ResourceName = "deepseek-policy-package";
        var builder = DistributedApplicationTestingBuilder.Create([]);
        builder.AddLlmTestServer(ResourceName).WithApiKey(_key);
        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync(ResourceName, cancellationToken);
        using var http = app.CreateHttpClient(ResourceName, LlmTestServerResource.HttpEndpointName);
        var control = new LlmTestServerClient(http, _key);
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseChatModel(_model)
            .UseChatScenario("installed-deepseek-policy", scenario => scenario.ForModel(_model)
                .Responds(_text, "Explicit installed ", "DeepSeek prefix")
                .WithOutputDisposition(new() { Kind = LlmTestServerOutputDispositionKind.Filtered })
                .Responds(_recovery)), cancellationToken);
        var sdk = new ChatClient(_model, new ApiKeyCredential(_key), new OpenAIClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/deepseek/v1/"),
            Transport = new HttpClientPipelineTransport(http),
            RetryPolicy = new ClientRetryPolicy(0),
        });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var text = new StringBuilder();
        ChatFinishReason? finish = null;
        ChatTokenUsage? usage = null;
        var usageUpdates = 0;
        await foreach (var update in sdk.CompleteChatStreamingAsync([new UserChatMessage("policy")], cancellationToken: deadline.Token))
        {
            foreach (var part in update.ContentUpdate) { text.Append(part.Text); }
            finish = update.FinishReason ?? finish;
            Require(update.RefusalUpdate is null, "DeepSeek Filtered must not become a model-refusal field.");
            if (update.Usage is not null) { usage = update.Usage; usageUpdates++; }
        }
        Require(text.ToString() == _text && finish == ChatFinishReason.ContentFilter && usage is not null && usageUpdates == 1,
            "Installed DeepSeek SDK streaming must retain the configured prefix, native ContentFilter finish and one final usage update.");
        var sdkUsage = usage ?? throw new InvalidOperationException("Installed DeepSeek SSE omitted final usage.");
        var first = await CompletedTraceAsync(control, 1, deadline.Token);
        Require(first.Path == _path && first.ModelId == _model && first.IsStreaming && first.StatusCode == 200
            && first.TransportState == LlmTestServerProviderHttpTraceTransportState.Completed
            && first.OutputDisposition?.Kind == LlmTestServerOutputDispositionKind.Filtered
            && first.TerminalMarkerObserved == true && first.StreamDelivery is null && !first.StreamDeliveryApplied,
            "Installed journal must distinguish a completed native DeepSeek policy stream from HTTP or delivery faults.");
        Require(first.Usage is not null && sdkUsage.InputTokenCount == first.Usage.InputTokens
            && sdkUsage.OutputTokenCount == first.Usage.OutputTokens && sdkUsage.TotalTokenCount == first.Usage.TotalTokens
            && sdkUsage.InputTokenCount > 0 && sdkUsage.OutputTokenCount > 0,
            "Installed SDK usage must reconcile with the recorded inference usage, including generated policy-prefix tokens.");
        var verified = await control.VerifyAsync(new()
        {
            Requests = [new() { Query = new() { RequestId = first.RequestId }, RequireStreamingTerminalMarker = true }],
        }, deadline.Token);
        Require(verified.Passed, "Installed DeepSeek native policy SSE must satisfy actual protocol-terminal verification.");
        var summary = await control.GetAssertionsAsync(deadline.Token);
        Require(summary.Matched == 1 && summary.ErrorsReturned == 0
            && summary.Events.Single().OutputDisposition?.Kind == LlmTestServerOutputDispositionKind.Filtered,
            "The filtered outcome must commit once without becoming a provider HTTP error.");

        var recovered = (await sdk.CompleteChatAsync([new UserChatMessage("next")], cancellationToken: deadline.Token)).Value;
        Require(recovered.Content.Single().Text == _recovery && recovered.FinishReason == ChatFinishReason.Stop && recovered.Refusal is null,
            "The explicit next installed SDK JSON call must consume the queued recovery with a normal stop.");
        var second = await CompletedTraceAsync(control, 2, deadline.Token);
        Require(second.Path == _path && !second.IsStreaming && second.StatusCode == 200 && second.OutputDisposition is null
            && second.TransportState == LlmTestServerProviderHttpTraceTransportState.Completed,
            "Installed JSON recovery must remain on the DeepSeek route and not inherit the prior policy outcome.");
        var requests = await control.GetRequestsAsync(cancellationToken: deadline.Token);
        summary = await control.GetAssertionsAsync(deadline.Token);
        Require(requests.TotalObserved == 2 && summary.Matched == 2 && summary.ErrorsReturned == 0,
            "Installed DeepSeek SDK must make exactly two matched requests with retries disabled.");
    }

    private static async Task<LlmTestServerProviderHttpTrace> CompletedTraceAsync(LlmTestServerClient control, int count, CancellationToken cancellationToken)
    {
        while (true)
        {
            var evidence = await control.GetRequestsAsync(cancellationToken: cancellationToken);
            if (evidence.Requests.Count == count && evidence.Requests.All(trace => trace.IsCompleted))
                return evidence.Requests.OrderBy(trace => trace.Sequence).Last();
            await Task.Delay(20, cancellationToken);
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException(message); } }
}
