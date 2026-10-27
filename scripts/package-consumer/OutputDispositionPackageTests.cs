#pragma warning disable OPENAI001 // Exercise the pinned official Responses client using installed NuGet packages.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using TUnit.Core;
using AnthropicMessages = global::Anthropic.Models.Messages;

public sealed class OutputDispositionPackageTests
{
    private const string _key = "output-policy-package-key";
    private const string _text = "Explicit installed policy text";
    private const string _recovery = "Installed policy recovery";

    [Test]
    [Arguments("chat")]
    [Arguments("responses")]
    [Arguments("anthropic")]
    [Arguments("gemini")]
    [Arguments("meai-json")]
    [Arguments("meai-stream")]
    public async Task InstalledPackages_NativePolicyOutcomeIsTypedAndFollowingInferenceRecoversAsync(string provider, CancellationToken cancellationToken)
    {
        var resourceName = "output-policy-" + provider;
        var builder = DistributedApplicationTestingBuilder.Create([]);
        builder.AddLlmTestServer(resourceName).WithApiKey(_key);
        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync(resourceName, cancellationToken);
        using var http = app.CreateHttpClient(resourceName, LlmTestServerResource.HttpEndpointName);
        var control = new LlmTestServerClient(http, _key);
        var model = provider switch
        {
            "anthropic" => JsonSerializer.SerializeToElement(AnthropicMessages.Model.ClaudeSonnet5_5).GetString()!,
            "gemini" => "gemini-3.8-flash",
            _ => LlmTestServerKnownModelIds.Gpt41Mini,
        };
        var kind = provider is "responses" or "gemini" ? LlmTestServerOutputDispositionKind.Filtered : LlmTestServerOutputDispositionKind.Refused;
        var metadata = kind == LlmTestServerOutputDispositionKind.Refused;
        var streaming = provider is "responses" or "gemini" or "meai-stream";
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseChatModel(model)
            .UseChatScenario("installed-policy", scenario => scenario.ForModel(model)
                .Responds(_text, "Explicit installed ", "policy text")
                .WithOutputDisposition(new() { Kind = kind, Category = metadata ? "cyber" : null, Explanation = metadata ? "Installed fixture policy" : null })
                .Responds(_recovery)), cancellationToken);

        var chat = provider == "chat" ? new ChatClient(model, new ApiKeyCredential(_key), new OpenAIClientOptions
        { Endpoint = new Uri(http.BaseAddress!, "/openai/v1/"), Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) }) : null;
        var responses = provider == "responses" ? new ResponsesClient(new ApiKeyCredential(_key), new ResponsesClientOptions
        { Endpoint = new Uri(http.BaseAddress!, "/openai/v1/"), Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) }) : null;
        using var anthropic = provider == "anthropic" ? new global::Anthropic.AnthropicClient(new global::Anthropic.Core.ClientOptions
        { HttpClient = http, BaseUrl = new Uri(http.BaseAddress!, "/anthropic").ToString(), ApiKey = _key, MaxRetries = 0, ResponseValidation = true }) : null;
        using var google = provider == "gemini" ? new Google.GenAI.Client(apiKey: _key, enterprise: false,
            httpOptions: new() { BaseUrl = new Uri(http.BaseAddress!, "/gemini").ToString(), ApiVersion = "v1beta", RetryOptions = new() { Attempts = 1 } },
            clientOptions: new() { HttpClientFactory = () => http }) : null;
        using var abstraction = provider.StartsWith("meai-", StringComparison.Ordinal) ? control.CreateChatClient() : null;
        if (chat is not null)
        {
            var result = (await chat.CompleteChatAsync([new UserChatMessage("policy")], cancellationToken: cancellationToken)).Value;
            Require(result.Refusal == _text && result.Content.Count == 0 && result.FinishReason == OpenAI.Chat.ChatFinishReason.Stop,
                "Installed OpenAI chat refusal must be the typed refusal field with native stop, without ordinary text duplication.");
        }
        else if (responses is not null)
        {
            var text = new StringBuilder(); ResponseResult? final = null;
            await foreach (var update in responses.CreateResponseStreamingAsync(new CreateResponseOptions
            { Model = model, StreamingEnabled = true, StoredOutputEnabled = false, InputItems = { ResponseItem.CreateUserMessageItem("policy") } }, cancellationToken))
            {
                if (update is StreamingResponseOutputTextDeltaUpdate delta) { text.Append(delta.Delta); }
                if (update is StreamingResponseIncompleteUpdate incomplete) { final = incomplete.Response; }
                Require(update is not StreamingResponseCompletedUpdate and not StreamingResponseFailedUpdate and not StreamingResponseErrorUpdate,
                    "Installed filtered Responses SSE must end incomplete rather than completed, failed or API error.");
            }
            Require(text.ToString() == _text && final is not null && final.Status == ResponseStatus.Incomplete
                && final.IncompleteStatusDetails.Reason == ResponseIncompleteStatusReason.ContentFilter,
                "Installed Responses SDK must retain its explicit prefix and typed content_filter incomplete outcome.");
        }
        else if (anthropic is not null)
        {
            var result = await anthropic.Messages.Create(AnthropicOptions(), cancellationToken);
            Require(result.StopReason?.Json.GetString() == "refusal" && result.StopDetails is { } refusalDetails
                && refusalDetails.Category?.Json.GetString() == "cyber" && refusalDetails.Explanation == "Installed fixture policy"
                && string.Concat(result.Content.Select(block => ((AnthropicMessages.TextBlock)block.Value!).Text)) == _text,
                "Installed strict Anthropic SDK must parse native refusal stop/details and explicit text.");
        }
        else if (google is not null)
        {
            var text = new StringBuilder(); Google.GenAI.Types.GenerateContentResponse? final = null;
            await foreach (var update in google.Models.GenerateContentStreamAsync(model, "policy",
                new Google.GenAI.Types.GenerateContentConfig { AutomaticContinuation = false }, cancellationToken: cancellationToken))
            {
                text.Append(update.Text);
                if (update.Candidates?.Any(candidate => candidate.FinishReason is not null) == true) { final = update; }
            }
            Require(text.ToString() == _text && final is not null && final.Candidates?.Single().FinishReason == Google.GenAI.Types.FinishReason.Safety
                && string.IsNullOrEmpty(final.Text) && final.PromptFeedback is null,
                "Installed Gemini SDK must observe a prefix followed by candidate SAFETY with no repeated terminal content or input-block envelope.");
        }
        else
        {
            Microsoft.Extensions.AI.ChatMessage[] prompt = [new(ChatRole.User, "policy")];
            var result = streaming
                ? await abstraction!.GetStreamingResponseAsync(prompt, cancellationToken: cancellationToken).ToChatResponseAsync(cancellationToken)
                : await abstraction!.GetResponseAsync(prompt, cancellationToken: cancellationToken);
            var refusal = result.Messages.SelectMany(message => message.Contents).OfType<ErrorContent>().Single();
            Require(refusal.ErrorCode == "Refusal" && refusal.Message == _text && result.Text.Length == 0
                && !result.Messages.SelectMany(message => message.Contents).OfType<TextContent>().Any(),
                "Installed IChatClient must represent refusal as one canonical ErrorContent and no duplicate ordinary text.");
        }
        var first = await CompletedTraceAsync(control, 1, cancellationToken);
        Require(first.StatusCode == 200 && first.TransportState == LlmTestServerProviderHttpTraceTransportState.Completed
            && first.OutputDisposition is { } policy && policy.Kind == kind && policy.Category == (metadata ? "cyber" : null)
            && policy.Explanation == (metadata ? "Installed fixture policy" : null),
            "Installed request journal must preserve the native200 policy kind and full bounded metadata.");
        var summary = await control.GetAssertionsAsync(cancellationToken);
        Require(summary.Matched == 1 && summary.ErrorsReturned == 0 && summary.Events.Single().OutputDisposition?.Kind == kind
            && summary.Events.Single().OutputDisposition?.Explanation == (metadata ? "Installed fixture policy" : null),
            "Installed runtime evidence must describe committed native policy inference without reporting a generic HTTP error.");
        if (streaming)
        {
            Require(first.TerminalMarkerObserved == true, "Installed native policy SSE must have a real protocol terminal.");
            var verified = await control.VerifyAsync(new()
            { Requests = [new() { Query = new() { RequestId = first.RequestId }, RequireStreamingTerminalMarker = true }] }, cancellationToken);
            Require(verified.Passed, "Installed stream verification must accept actual native policy terminal evidence.");
        }
        string recovered;
        if (chat is not null) { recovered = (await chat.CompleteChatAsync([new UserChatMessage("next")], cancellationToken: cancellationToken)).Value.Content.Single().Text; }
        else if (responses is not null)
        {
            recovered = (await responses.CreateResponseAsync(new CreateResponseOptions
            { Model = model, StoredOutputEnabled = false, InputItems = { ResponseItem.CreateUserMessageItem("next") } }, cancellationToken)).Value.GetOutputText();
        }
        else if (anthropic is not null) { recovered = ((AnthropicMessages.TextBlock)(await anthropic.Messages.Create(AnthropicOptions(), cancellationToken)).Content.Single().Value!).Text; }
        else if (google is not null)
        {
            recovered = (await google.Models.GenerateContentAsync(model, "next", new Google.GenAI.Types.GenerateContentConfig
            { AutomaticContinuation = false }, cancellationToken: cancellationToken)).Text!;
        }
        else { recovered = (await abstraction!.GetResponseAsync([new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, "next")], cancellationToken: cancellationToken)).Text; }
        Require(recovered == _recovery, "The explicit next inference must select the queued recovery after the policy outcome.");
        var second = await CompletedTraceAsync(control, 2, cancellationToken);
        Require(second.OutputDisposition is null && second.StatusCode == 200, "Installed queued recovery must not inherit prior policy metadata.");
        Require((await control.GetRequestsAsync(cancellationToken: cancellationToken)).TotalObserved == 2
            && (await control.GetAssertionsAsync(cancellationToken)).Matched == 2,
            "The original SDK must make exactly two observed, matched attempts with no hidden retries.");
    }

    private static AnthropicMessages.MessageCreateParams AnthropicOptions() => new()
    { Model = AnthropicMessages.Model.ClaudeSonnet5_5, MaxTokens = 256, Messages = [new AnthropicMessages.UserMessageParam { Content = "policy" }] };

    private static async Task<LlmTestServerProviderHttpTrace> CompletedTraceAsync(LlmTestServerClient control, int count, CancellationToken cancellationToken)
    {
        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        watchdog.CancelAfter(TimeSpan.FromSeconds(60));
        while (true)
        {
            var evidence = await control.GetRequestsAsync(cancellationToken: watchdog.Token);
            if (evidence.Requests.Count == count && evidence.Requests.All(trace => trace.IsCompleted))
                return evidence.Requests.OrderBy(trace => trace.Sequence).Last();
            await Task.Delay(20, watchdog.Token);
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException(message); } }
}
