#pragma warning disable OPENAI001 // Exercise the pinned official Responses SDK against installed packages.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Azure;
using Azure.AI.Inference;
using Azure.AI.OpenAI;
using Azure.Core.Pipeline;
using ManagedCode.LlmTestServer.Aspire;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using TUnit.Core;

public sealed class AzureOutputPolicyPackageTests
{
    private const string _key = "azure-policy-package-key";
    private const string _model = LlmTestServerKnownModelIds.Gpt41Mini;
    private const string _text = "Explicit installed Azure policy text";
    private const string _recovery = "Installed Azure policy recovery";

    [Test]
    [Arguments("deployment")]
    [Arguments("inference")]
    [Arguments("inference-models")]
    [Arguments("azure-chat")]
    [Arguments("foundry-chat")]
    [Arguments("azure-responses")]
    [Arguments("foundry-responses")]
    public async Task InstalledPackages_AzurePolicyStreamsThroughOriginalSdkAndExplicitJsonRecoveryAsync(string route, CancellationToken cancellationToken)
    {
        var resourceName = "azure-policy-" + route;
        var builder = DistributedApplicationTestingBuilder.Create([]);
        builder.AddLlmTestServer(resourceName).WithApiKey(_key);
        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync(resourceName, cancellationToken);
        using var http = app.CreateHttpClient(resourceName, LlmTestServerResource.HttpEndpointName);
        var control = new LlmTestServerClient(http, _key);
        var inference = route is "inference" or "inference-models";
        var responses = route.EndsWith("responses", StringComparison.Ordinal);
        var kind = inference || responses ? LlmTestServerOutputDispositionKind.Filtered : LlmTestServerOutputDispositionKind.Refused;
        var prefix = route.StartsWith("foundry-", StringComparison.Ordinal) ? "/microsoft-foundry" : "/azure-openai";
        var expectedPath = route switch
        {
            "deployment" => "/azure-openai/openai/deployments/" + _model + "/chat/completions",
            "inference" => "/microsoft-foundry/chat/completions",
            "inference-models" => "/microsoft-foundry/models/chat/completions",
            _ => prefix + "/openai/v1/" + (responses ? "responses" : "chat/completions"),
        };
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseChatModel(_model)
            .UseChatScenario("installed-azure-policy", scenario => scenario.ForModel(_model)
                .Responds(_text, "Explicit installed ", "Azure policy text")
                .WithOutputDisposition(new() { Kind = kind })
                .Responds(_recovery)), cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var text = new StringBuilder();
        var refusal = new StringBuilder();
        long inputTokens = 0, outputTokens = 0, totalTokens = 0;
        var sawUsage = false;
        Func<CancellationToken, Task<string>> recover;
        if (inference)
        {
            var options = new AzureAIInferenceClientOptions { Transport = new HttpClientTransport(http) };
            options.Retry.MaxRetries = 0;
            var endpoint = route == "inference-models" ? "/microsoft-foundry/models" : "/microsoft-foundry";
            var sdk = new ChatCompletionsClient(new Uri(http.BaseAddress!, endpoint), new AzureKeyCredential(_key), options);
            var request = new ChatCompletionsOptions([new ChatRequestUserMessage("policy")]) { Model = _model };
            CompletionsFinishReason? finish = null;
            using (var stream = await sdk.CompleteStreamingAsync(request, deadline.Token))
            {
                await foreach (var update in stream)
                {
                    text.Append(update.ContentUpdate);
                    finish = update.FinishReason ?? finish;
                    if (update.Usage is { } usage)
                    {
                        sawUsage = true; inputTokens = usage.PromptTokens; outputTokens = usage.CompletionTokens; totalTokens = usage.TotalTokens;
                    }
                }
            }
            Require(finish == CompletionsFinishReason.ContentFiltered, "Installed Model Inference SDK must parse typed ContentFiltered.");
            recover = async token =>
            {
                var next = (await sdk.CompleteAsync(new ChatCompletionsOptions([new ChatRequestUserMessage("next")]) { Model = _model }, token)).Value;
                Require(next.FinishReason == CompletionsFinishReason.Stopped, "Installed inference recovery must have a normal stop.");
                return next.Content;
            };
        }
        else if (responses)
        {
            var sdk = new ResponsesClient(new ApiKeyCredential(_key), new ResponsesClientOptions
            { Endpoint = new Uri(http.BaseAddress!, prefix + "/openai/v1/"), Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) });
            ResponseResult? final = null;
            await foreach (var update in sdk.CreateResponseStreamingAsync(new CreateResponseOptions
            { Model = _model, StreamingEnabled = true, StoredOutputEnabled = false, InputItems = { ResponseItem.CreateUserMessageItem("policy") } }, deadline.Token))
            {
                if (update is StreamingResponseOutputTextDeltaUpdate delta) { text.Append(delta.Delta); }
                if (update is StreamingResponseIncompleteUpdate incomplete) { final = incomplete.Response; }
                Require(update is not StreamingResponseCompletedUpdate and not StreamingResponseFailedUpdate and not StreamingResponseErrorUpdate,
                    "Installed filtered Azure Responses must end incomplete, not completed, failed or API error.");
            }
            Require(final is not null && final.Status == ResponseStatus.Incomplete
                && final.IncompleteStatusDetails.Reason == ResponseIncompleteStatusReason.ContentFilter,
                "Installed Azure Responses SDK must parse native content_filter incomplete completion.");
            var usage = final!.Usage;
            sawUsage = usage is not null;
            if (usage is not null) { inputTokens = usage.InputTokenCount; outputTokens = usage.OutputTokenCount; totalTokens = usage.TotalTokenCount; }
            recover = async token =>
            {
                var next = (await sdk.CreateResponseAsync(new CreateResponseOptions
                { Model = _model, StoredOutputEnabled = false, InputItems = { ResponseItem.CreateUserMessageItem("next") } }, token)).Value;
                Require(next.Status == ResponseStatus.Completed && next.IncompleteStatusDetails is null, "Installed Azure Responses recovery must complete normally.");
                return next.GetOutputText();
            };
        }
        else
        {
            var transport = new HttpClientPipelineTransport(http);
            var sdk = route == "deployment"
                ? new AzureOpenAIClient(new Uri(http.BaseAddress!, "/azure-openai/"), new ApiKeyCredential(_key),
                    new AzureOpenAIClientOptions { Transport = transport, RetryPolicy = new ClientRetryPolicy(0) }).GetChatClient(_model)
                : new ChatClient(_model, new ApiKeyCredential(_key), new OpenAIClientOptions
                { Endpoint = new Uri(http.BaseAddress!, prefix + "/openai/v1/"), Transport = transport, RetryPolicy = new ClientRetryPolicy(0) });
            ChatFinishReason? finish = null;
            await foreach (var update in sdk.CompleteChatStreamingAsync([new UserChatMessage("policy")], cancellationToken: deadline.Token))
            {
                foreach (var part in update.ContentUpdate) { text.Append(part.Text); }
                refusal.Append(update.RefusalUpdate);
                finish = update.FinishReason ?? finish;
                if (update.Usage is { } usage)
                { sawUsage = true; inputTokens = usage.InputTokenCount; outputTokens = usage.OutputTokenCount; totalTokens = usage.TotalTokenCount; }
            }
            Require(finish == ChatFinishReason.Stop && text.Length == 0 && refusal.ToString() == _text,
                "Installed Azure Chat SDK must expose native refusal deltas and normal stop without duplicate ordinary text.");
            recover = async token =>
            {
                var next = (await sdk.CompleteChatAsync([new UserChatMessage("next")], cancellationToken: token)).Value;
                Require(next.FinishReason == ChatFinishReason.Stop && next.Refusal is null, "Installed Azure Chat recovery must not inherit refusal.");
                return next.Content.Single().Text;
            };
        }
        Require((kind == LlmTestServerOutputDispositionKind.Refused ? refusal.ToString() : text.ToString()) == _text,
            "Installed original Azure SDK must retain explicit policy text.");
        var first = await CompletedTraceAsync(control, 1, deadline.Token);
        Require(first.Path == expectedPath && first.ModelId == _model && first.IsStreaming && first.StatusCode == 200
            && first.TransportState == LlmTestServerProviderHttpTraceTransportState.Completed && first.TerminalMarkerObserved == true
            && first.OutputDisposition?.Kind == kind && first.StreamDelivery is null && !first.StreamDeliveryApplied,
            "Installed journal must retain the exact Azure route, native200 policy and actual stream terminal.");
        Require(sawUsage, $"Installed original {route} SDK stream omitted typed usage; journal input/output/total={first.Usage?.InputTokens}/{first.Usage?.OutputTokens}/{first.Usage?.TotalTokens}.");
        Require(first.Usage is { } recorded && recorded.InputTokens == inputTokens && recorded.OutputTokens == outputTokens
            && recorded.TotalTokens == totalTokens && inputTokens > 0 && outputTokens > 0,
            "Installed SDK stream usage must reconcile with recorded generated/refusal tokens.");
        var verified = await control.VerifyAsync(new()
        { Requests = [new() { Query = new() { RequestId = first.RequestId }, RequireStreamingTerminalMarker = true }] }, deadline.Token);
        Require(verified.Passed, "Installed native Azure policy stream must satisfy protocol-terminal verification.");
        Require(await recover(deadline.Token) == _recovery, "Explicit installed SDK JSON retry must consume the next recovery fixture.");
        var second = await CompletedTraceAsync(control, 2, deadline.Token);
        var summary = await control.GetAssertionsAsync(deadline.Token);
        Require(second.Path == expectedPath && !second.IsStreaming && second.StatusCode == 200 && second.OutputDisposition is null
            && second.TransportState == LlmTestServerProviderHttpTraceTransportState.Completed
            && summary.Matched == 2 && summary.ErrorsReturned == 0
            && summary.Events.Count(item => item.OutputDisposition?.Kind == kind) == 1
            && (await control.GetRequestsAsync(cancellationToken: deadline.Token)).TotalObserved == 2,
            "Exactly two original SDK calls must be observed, with one committed policy followed by ordinary JSON recovery.");
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
