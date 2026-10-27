#pragma warning disable OPENAI001 // Qualify the installed package with the pinned official Responses SDK.
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
using OpenAI.Responses;
using TUnit.Core;
using AnthropicMessages = global::Anthropic.Models.Messages;

public sealed class ProtocolDeliveryPackageTests
{
    private const string _key = "protocol-delivery-package-key";

    [Test]
    [Arguments("responses")]
    [Arguments("anthropic")]
    [Arguments("gemini")]
    public async Task InstalledPackages_OfficialSdkDetectsMissingStreamTerminalAndExplicitRetryRecoversAsync(string provider, CancellationToken cancellationToken)
    {
        var resourceName = "protocol-package-" + provider;
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
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseChatModel(model)
            .UseChatScenario("protocol-package-delivery", scenario => scenario.ForModel(model)
                .Responds("first second", "", "first ", "", "second")
                .WithStreamDelivery(new() { Mode = LlmTestServerStreamDeliveryMode.EarlyCleanEnd, AfterContentFrames = 1 })
                .Responds("installed protocol recovery")), cancellationToken);

        var responses = provider == "responses" ? new ResponsesClient(new ApiKeyCredential(_key), new ResponsesClientOptions
        { Endpoint = new Uri(http.BaseAddress!, "/openai/v1/"), Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) }) : null;
        using var anthropic = provider == "anthropic" ? new global::Anthropic.AnthropicClient(new global::Anthropic.Core.ClientOptions
        { HttpClient = http, BaseUrl = new Uri(http.BaseAddress!, "/anthropic").ToString(), ApiKey = _key, MaxRetries = 0, ResponseValidation = true }) : null;
        using var google = provider == "gemini" ? new Google.GenAI.Client(apiKey: _key, enterprise: false,
            httpOptions: new() { BaseUrl = new Uri(http.BaseAddress!, "/gemini").ToString(), ApiVersion = "v1beta", RetryOptions = new() { Attempts = 1 } },
            clientOptions: new() { HttpClientFactory = () => http }) : null;
        var text = new StringBuilder();
        var terminal = false;
        if (responses is not null)
        {
            await foreach (var update in responses.CreateResponseStreamingAsync(new CreateResponseOptions
            { Model = model, StreamingEnabled = true, StoredOutputEnabled = false, InputItems = { ResponseItem.CreateUserMessageItem("stream") } }, cancellationToken))
            {
                if (update is StreamingResponseOutputTextDeltaUpdate delta) { text.Append(delta.Delta); }
                if (update is StreamingResponseCompletedUpdate) { terminal = true; }
            }
        }
        else if (anthropic is not null)
        {
            await foreach (var update in anthropic.Messages.CreateStreaming(AnthropicOptions(), cancellationToken))
            {
                if (update.Value is AnthropicMessages.RawContentBlockDeltaEvent block && block.Delta.Value is AnthropicMessages.TextDelta delta) { text.Append(delta.Text); }
                if (update.Value is AnthropicMessages.RawMessageStopEvent) { terminal = true; }
            }
        }
        else
        {
            await foreach (var update in google!.Models.GenerateContentStreamAsync(model, "stream",
                new Google.GenAI.Types.GenerateContentConfig { AutomaticContinuation = false }, cancellationToken: cancellationToken))
            {
                text.Append(update.Text);
                if (update.Candidates?.Any(candidate => candidate.FinishReason is not null) == true) { terminal = true; }
            }
        }
        if (text.ToString() != "first " || terminal)
            throw new InvalidOperationException($"Installed {provider} SSE must expose the first nonempty text delta without a protocol completion.");
        var trace = await CompletedTraceAsync(control, cancellationToken);
        var expectedPath = provider switch
        {
            "responses" => "/openai/v1/responses",
            "anthropic" => "/anthropic/v1/messages",
            _ => "/gemini/v1beta/models/" + model + ":streamGenerateContent",
        };
        if (trace.Path != expectedPath || trace.StatusCode != 200 || trace.TerminalMarkerObserved == true
            || trace.TransportState != LlmTestServerProviderHttpTraceTransportState.Completed
            || trace.StreamDelivery?.Mode != LlmTestServerStreamDeliveryMode.EarlyCleanEnd
            || !trace.StreamDeliveryApplied || trace.EmittedContentFrames != 1)
            throw new InvalidOperationException($"Installed {provider} diagnostics lost the applied delivery boundary or reported a false terminal marker.");
        var verification = await control.VerifyAsync(new()
        { Requests = [new() { Query = new() { RequestId = trace.RequestId }, RequireStreamingTerminalMarker = true }] }, cancellationToken);
        if (verification.Passed || !verification.Failures.Any(failure => failure.Code == "stream.terminal" && failure.RequestIds.Contains(trace.RequestId)))
            throw new InvalidOperationException($"Installed {provider} verification must reject missing terminal evidence.");
        if ((await control.GetAssertionsAsync(cancellationToken)).Matched != 1)
            throw new InvalidOperationException($"Installed {provider} SDK must issue exactly one attempt and retain its committed inference.");

        string recovered;
        if (responses is not null)
            recovered = (await responses.CreateResponseAsync(new CreateResponseOptions
            { Model = model, StoredOutputEnabled = false, InputItems = { ResponseItem.CreateUserMessageItem("explicit retry") } }, cancellationToken)).Value.GetOutputText();
        else if (anthropic is not null)
            recovered = ((AnthropicMessages.TextBlock)(await anthropic.Messages.Create(AnthropicOptions(), cancellationToken)).Content.Single().Value!).Text;
        else
            recovered = (await google!.Models.GenerateContentAsync(model, "explicit retry",
                new Google.GenAI.Types.GenerateContentConfig { AutomaticContinuation = false }, cancellationToken: cancellationToken)).Text!;
        if (recovered != "installed protocol recovery" || (await control.GetAssertionsAsync(cancellationToken)).Matched != 2
            || (await control.GetRequestsAsync(cancellationToken: cancellationToken)).TotalObserved != 2)
            throw new InvalidOperationException($"Installed {provider} explicit JSON retry must consume the next fixture exactly once.");
    }

    private static AnthropicMessages.MessageCreateParams AnthropicOptions() => new()
    { Model = AnthropicMessages.Model.ClaudeSonnet5_5, MaxTokens = 256, Messages = [new AnthropicMessages.UserMessageParam { Content = "stream" }] };

    private static async Task<LlmTestServerProviderHttpTrace> CompletedTraceAsync(LlmTestServerClient control, CancellationToken cancellationToken)
    {
        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        watchdog.CancelAfter(TimeSpan.FromSeconds(60));
        while (true)
        {
            var evidence = await control.GetRequestsAsync(cancellationToken: watchdog.Token);
            if (evidence.Requests.Count == 1 && evidence.Requests[0].IsCompleted) { return evidence.Requests[0]; }
            watchdog.Token.ThrowIfCancellationRequested();
        }
    }
}
