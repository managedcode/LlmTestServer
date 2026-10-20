using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using global::Azure.AI.Inference;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI.Chat;

namespace ManagedCode.LlmTestServer.Tests.Policy;

public sealed partial class OutputDispositionIntegrationTests
{
    private static ChatClient AzurePolicySdk(HttpClient http)
    {
        var client = new global::Azure.AI.OpenAI.AzureOpenAIClient(
            new Uri(http.BaseAddress!, "/azure-openai/"), new ApiKeyCredential(_key),
            new global::Azure.AI.OpenAI.AzureOpenAIClientOptions
            {
                Transport = new HttpClientPipelineTransport(http),
                RetryPolicy = new ClientRetryPolicy(0),
            });
        return client.GetChatClient(LlmTestServerKnownModelIds.Gpt41Mini);
    }

    private static ChatCompletionsClient FoundryPolicySdk(HttpClient http, string endpoint)
    {
        var options = new AzureAIInferenceClientOptions
        {
            Transport = new global::Azure.Core.Pipeline.HttpClientTransport(http),
        };
        options.Retry.MaxRetries = 0;
        return new(new Uri(http.BaseAddress!, endpoint), new global::Azure.AzureKeyCredential(_key), options);
    }

    // Pass-through observation of the original SDK request; no version, headers, or content are rewritten.
    private sealed class AzurePolicyObserver : DelegatingHandler
    {
        public ConcurrentQueue<(Uri Uri, HttpStatusCode Status)> Observed { get; } = new();

        public AzurePolicyObserver() : base(new HttpClientHandler()) { }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            Observed.Enqueue((request.RequestUri!, response.StatusCode));
            return response;
        }
    }

    [Test]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered, false, false)]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered, true, false)]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered, false, true)]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered, true, true)]
    [Arguments(LlmTestServerOutputDispositionKind.Refused, false, false)]
    [Arguments(LlmTestServerOutputDispositionKind.Refused, true, false)]
    [Arguments(LlmTestServerOutputDispositionKind.Refused, false, true)]
    [Arguments(LlmTestServerOutputDispositionKind.Refused, true, true)]
    public async Task AzureDeployment_OfficialSdkDefaultVersionReturnsNativePolicyJsonAndSseThenRecoversAsync(
        LlmTestServerOutputDispositionKind kind, bool streaming, bool empty)
    {
        await using var host = await StartAsync(config =>
        {
            config.RequireBearerToken(_key);
            ConfigureQueue(config, LlmTestServerKnownModelIds.Gpt41Mini, kind, empty);
        });
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        using var observer = new AzurePolicyObserver();
        using var sdkHttp = new HttpClient(observer) { BaseAddress = http.BaseAddress };
        var sdk = AzurePolicySdk(sdkHttp);
        var text = new StringBuilder(); var refusal = new StringBuilder();
        ChatFinishReason? finish = null; ChatTokenUsage? usage = null; var hasRefusal = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        if (streaming)
        {
            await foreach (var update in sdk.CompleteChatStreamingAsync([new UserChatMessage("policy")], cancellationToken: deadline.Token))
            {
                foreach (var part in update.ContentUpdate) { text.Append(part.Text); }
                refusal.Append(update.RefusalUpdate); hasRefusal |= update.RefusalUpdate is not null;
                finish = update.FinishReason ?? finish; usage = update.Usage ?? usage;
            }
        }
        else
        {
            var result = (await sdk.CompleteChatAsync([new UserChatMessage("policy")], cancellationToken: deadline.Token)).Value;
            foreach (var part in result.Content) { text.Append(part.Text); }
            refusal.Append(result.Refusal); hasRefusal = result.Refusal is not null;
            finish = result.FinishReason; usage = result.Usage;
        }
        var expected = empty ? string.Empty : _text;
        await Assert.That(text.ToString()).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Filtered ? expected : string.Empty);
        await Assert.That(refusal.ToString()).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Refused ? expected : string.Empty);
        await Assert.That(hasRefusal).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Refused);
        await Assert.That(finish).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Filtered ? ChatFinishReason.ContentFilter : ChatFinishReason.Stop);
        await EvidenceAsync(control, kind, streaming);
        var trace = await CompletedAsync(control, 1);
        await Assert.That(trace.Path).IsEqualTo("/azure-openai/openai/deployments/" + LlmTestServerKnownModelIds.Gpt41Mini + "/chat/completions");
        await Assert.That(usage).IsNotNull();
        await Assert.That(usage!.InputTokenCount).IsEqualTo(trace.Usage!.InputTokens);
        await Assert.That(usage.OutputTokenCount).IsEqualTo(trace.Usage.OutputTokens);
        await Assert.That(usage.TotalTokenCount).IsEqualTo(trace.Usage.TotalTokens);
        var observed = observer.Observed.Single();
        await Assert.That(observed.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(observed.Uri.AbsolutePath).IsEqualTo(trace.Path);
        // This value is observed from the pinned official SDK default, never selected in options.
        await Assert.That(observed.Uri.Query).IsEqualTo("?api-version=2025-04-01-preview");
        var next = (await sdk.CompleteChatAsync([new UserChatMessage("next")], cancellationToken: deadline.Token)).Value;
        await Assert.That(next.Content.Single().Text).IsEqualTo(_recovery);
        await RecoveredAsync(control);
    }

    [Test]
    [Arguments("/microsoft-foundry", false, false)]
    [Arguments("/microsoft-foundry", true, false)]
    [Arguments("/microsoft-foundry", false, true)]
    [Arguments("/microsoft-foundry", true, true)]
    [Arguments("/microsoft-foundry/models", false, false)]
    [Arguments("/microsoft-foundry/models", true, false)]
    [Arguments("/microsoft-foundry/models", false, true)]
    [Arguments("/microsoft-foundry/models", true, true)]
    public async Task FoundryInference_OfficialSdkDefaultVersionReturnsContentFilteredThenRecoversAsync(string endpoint, bool streaming, bool empty)
    {
        await using var host = await StartAsync(config =>
        {
            config.RequireBearerToken(_key);
            ConfigureQueue(config, LlmTestServerKnownModelIds.Gpt41Mini, LlmTestServerOutputDispositionKind.Filtered, empty);
        });
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        using var observer = new AzurePolicyObserver();
        using var sdkHttp = new HttpClient(observer) { BaseAddress = http.BaseAddress };
        var sdk = FoundryPolicySdk(sdkHttp, endpoint);
        var options = new ChatCompletionsOptions([new ChatRequestUserMessage("policy")]) { Model = LlmTestServerKnownModelIds.Gpt41Mini };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var text = new StringBuilder(); CompletionsFinishReason? finish = null; CompletionsUsage? usage = null; var usageUpdates = 0;
        if (streaming)
        {
            using var response = await sdk.CompleteStreamingAsync(options, deadline.Token);
            await foreach (var update in response)
            {
                text.Append(update.ContentUpdate);
                finish = update.FinishReason ?? finish; usage = update.Usage ?? usage;
                if (update.Usage is not null) { usageUpdates++; }
            }
        }
        else
        {
            var response = (await sdk.CompleteAsync(options, deadline.Token)).Value;
            text.Append(response.Content); finish = response.FinishReason; usage = response.Usage;
        }
        await Assert.That(text.ToString()).IsEqualTo(empty ? string.Empty : _text);
        await Assert.That(finish).IsEqualTo(CompletionsFinishReason.ContentFiltered);
        await EvidenceAsync(control, LlmTestServerOutputDispositionKind.Filtered, streaming);
        var trace = await CompletedAsync(control, 1);
        await Assert.That(trace.Path).IsEqualTo(endpoint + "/chat/completions");
        await Assert.That(usage).IsNotNull();
        await Assert.That(usage!.PromptTokens).IsEqualTo(trace.Usage!.InputTokens);
        await Assert.That(usage.CompletionTokens).IsEqualTo(trace.Usage.OutputTokens);
        await Assert.That(usage.TotalTokens).IsEqualTo(trace.Usage.TotalTokens);
        if (streaming) { await Assert.That(usageUpdates).IsEqualTo(1); }
        var observed = observer.Observed.Single();
        await Assert.That(observed.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(observed.Uri.AbsolutePath).IsEqualTo(trace.Path);
        await Assert.That(observed.Uri.Query).IsEqualTo("?api-version=2024-05-01-preview");
        var next = (await sdk.CompleteAsync(new ChatCompletionsOptions([new ChatRequestUserMessage("next")])
        { Model = LlmTestServerKnownModelIds.Gpt41Mini }, deadline.Token)).Value;
        await Assert.That(next.Content).IsEqualTo(_recovery);
        await RecoveredAsync(control);
    }

    [Test]
    [Arguments("/microsoft-foundry", false)]
    [Arguments("/microsoft-foundry", true)]
    [Arguments("/microsoft-foundry/models", false)]
    [Arguments("/microsoft-foundry/models", true)]
    public async Task FoundryInference_RefusedIs409BeforeConsumptionAndAzureDeploymentCanUseTheOriginalFixtureAsync(string endpoint, bool streaming)
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, LlmTestServerKnownModelIds.Gpt41Mini, LlmTestServerOutputDispositionKind.Refused));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        var sdk = FoundryPolicySdk(http, endpoint);
        var options = new ChatCompletionsOptions([new ChatRequestUserMessage("policy")]) { Model = LlmTestServerKnownModelIds.Gpt41Mini };
        global::Azure.RequestFailedException? failure = null;
        try
        {
            if (streaming)
            {
                using var response = await sdk.CompleteStreamingAsync(options);
                await foreach (var update in response) { _ = update; }
            }
            else { await sdk.CompleteAsync(options); }
        }
        catch (global::Azure.RequestFailedException exception) { failure = exception; }
        await Assert.That(failure?.Status).IsEqualTo(409);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        var qualified = (await AzurePolicySdk(http).CompleteChatAsync([new UserChatMessage("policy")])).Value;
        await Assert.That(qualified.Refusal).IsEqualTo(_text);
        var next = (await sdk.CompleteAsync(new ChatCompletionsOptions([new ChatRequestUserMessage("next")])
        { Model = LlmTestServerKnownModelIds.Gpt41Mini })).Value;
        await Assert.That(next.Content).IsEqualTo(_recovery);
        await Assert.That((await CompletedAsync(control, 3)).OutputDisposition).IsNull();
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
    }

    [Test]
    [Arguments("deployment")]
    [Arguments("/microsoft-foundry")]
    [Arguments("/microsoft-foundry/models")]
    public async Task AzureNativeSdk_InputFilterIs400WhileOutputFilterIs200AndDoesNotConsumeQueueAsync(string surface)
    {
        await using var host = await StartAsync(config =>
        {
            config.SimulateContentFilter("blocked-input");
            ConfigureQueue(config, LlmTestServerKnownModelIds.Gpt41Mini, LlmTestServerOutputDispositionKind.Filtered);
        });
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        var status = 0;
        if (surface == "deployment")
        {
            try { await AzurePolicySdk(http).CompleteChatAsync([new UserChatMessage("blocked-input")]); }
            catch (ClientResultException exception) { status = exception.Status; }
        }
        else
        {
            try { await FoundryPolicySdk(http, surface).CompleteAsync(new ChatCompletionsOptions([new ChatRequestUserMessage("blocked-input")])
            { Model = LlmTestServerKnownModelIds.Gpt41Mini }); }
            catch (global::Azure.RequestFailedException exception) { status = exception.Status; }
        }
        await Assert.That(status).IsEqualTo(400);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        await Assert.That((await CompletedAsync(control, 1)).OutputDisposition).IsNull();
        if (surface == "deployment")
        {
            var sdk = AzurePolicySdk(http);
            await Assert.That((await sdk.CompleteChatAsync([new UserChatMessage("policy")])).Value.FinishReason).IsEqualTo(ChatFinishReason.ContentFilter);
            await Assert.That((await sdk.CompleteChatAsync([new UserChatMessage("next")])).Value.Content.Single().Text).IsEqualTo(_recovery);
        }
        else
        {
            var sdk = FoundryPolicySdk(http, surface);
            await Assert.That((await sdk.CompleteAsync(new ChatCompletionsOptions([new ChatRequestUserMessage("policy")])
            { Model = LlmTestServerKnownModelIds.Gpt41Mini })).Value.FinishReason).IsEqualTo(CompletionsFinishReason.ContentFiltered);
            await Assert.That((await sdk.CompleteAsync(new ChatCompletionsOptions([new ChatRequestUserMessage("next")])
            { Model = LlmTestServerKnownModelIds.Gpt41Mini })).Value.Content).IsEqualTo(_recovery);
        }
        await Assert.That((await CompletedAsync(control, 2)).OutputDisposition!.Kind).IsEqualTo(LlmTestServerOutputDispositionKind.Filtered);
        await Assert.That((await CompletedAsync(control, 3)).OutputDisposition).IsNull();
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
    }

    [Test]
    [Arguments("deployment", false)]
    [Arguments("deployment", true)]
    [Arguments("/microsoft-foundry", false)]
    [Arguments("/microsoft-foundry", true)]
    [Arguments("/microsoft-foundry/models", false)]
    [Arguments("/microsoft-foundry/models", true)]
    public async Task AzureNativeSdk_ToolsAndStructuredRequestsAre409BeforeConsumingFilteredFixtureAsync(string surface, bool schema)
    {
        await using var host = await StartAsync(config => ConfigureQueue(config, LlmTestServerKnownModelIds.Gpt41Mini, LlmTestServerOutputDispositionKind.Filtered));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        var status = 0;
        if (surface == "deployment")
        {
            var options = new ChatCompletionOptions();
            if (schema) { options.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(); }
            else { options.Tools.Add(ChatTool.CreateFunctionTool("weather", functionParameters: BinaryData.FromString("{\"type\":\"object\",\"properties\":{}}"))); }
            try { await AzurePolicySdk(http).CompleteChatAsync([new UserChatMessage("policy")], options); }
            catch (ClientResultException exception) { status = exception.Status; }
        }
        else
        {
            var options = new ChatCompletionsOptions([new ChatRequestUserMessage("policy")]) { Model = LlmTestServerKnownModelIds.Gpt41Mini };
            if (schema) { options.ResponseFormat = ChatCompletionsResponseFormat.CreateJsonFormat(); }
            else { options.Tools.Add(new ChatCompletionsToolDefinition(new FunctionDefinition("weather")
            { Parameters = BinaryData.FromString("{\"type\":\"object\",\"properties\":{}}") })); }
            try { await FoundryPolicySdk(http, surface).CompleteAsync(options); }
            catch (global::Azure.RequestFailedException exception) { status = exception.Status; }
        }
        await Assert.That(status).IsEqualTo(409);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        var first = (await AzurePolicySdk(http).CompleteChatAsync([new UserChatMessage("policy")])).Value;
        await Assert.That(first.FinishReason).IsEqualTo(ChatFinishReason.ContentFilter);
        await Assert.That(first.Content.Single().Text).IsEqualTo(_text);
        var next = (await AzurePolicySdk(http).CompleteChatAsync([new UserChatMessage("next")])).Value;
        await Assert.That(next.Content.Single().Text).IsEqualTo(_recovery);
        await Assert.That((await CompletedAsync(control, 3)).OutputDisposition).IsNull();
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
    }

    [Test]
    [Arguments(LlmTestServerOutputDispositionKind.Filtered)]
    [Arguments(LlmTestServerOutputDispositionKind.Refused)]
    public async Task AzureDeployment_PolicyGraphReplaysAcrossJsonSseAndCanonicalSdkHistoryWhileSessionsStayIsolatedAsync(LlmTestServerOutputDispositionKind kind)
    {
        await using var host = await StartAsync(_ => { });
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        void Configure(LlmTestServerClientConfigurationBuilder config)
        {
            config.UseGpt41Mini().UseConversation("azure-policy-graph", graph => graph.ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                .Step("policy", step => step.WhenLatestUserEquals("go").Responds(_text, "Visible ", "fixture text")
                    .WithOutputDisposition(new() { Kind = kind }).Then("next"))
                .Step("next", step => step.WhenLatestUserEquals("next").Responds(_recovery).Complete()));
        }
        await using var firstSession = await control.CreateSessionAsync(Configure);
        await using var secondSession = await control.CreateSessionAsync(Configure);
        using var firstConversation = firstSession.Control.ForConversation("same-azure-label");
        using var secondConversation = secondSession.Control.ForConversation("same-azure-label");
        global::OpenAI.Chat.ChatMessage[] input = [new SystemChatMessage(string.Join(" ", Enumerable.Repeat("stable policy context", 1400))), new UserChatMessage("go")];
        var firstSdk = AzurePolicySdk(firstConversation.HttpClient);
        var first = (await firstSdk.CompleteChatAsync(input)).Value;
        var expectedReason = kind == LlmTestServerOutputDispositionKind.Filtered ? ChatFinishReason.ContentFilter : ChatFinishReason.Stop;
        await Assert.That(first.FinishReason).IsEqualTo(expectedReason);
        await Assert.That(first.Usage.InputTokenDetails.CachedTokenCount).IsEqualTo(0);
        var replayText = new StringBuilder(); ChatFinishReason? replayFinish = null;
        await foreach (var update in firstSdk.CompleteChatStreamingAsync(input))
        {
            foreach (var part in update.ContentUpdate) { replayText.Append(part.Text); }
            replayText.Append(update.RefusalUpdate); replayFinish = update.FinishReason ?? replayFinish;
        }
        await Assert.That(replayText.ToString()).IsEqualTo(_text);
        await Assert.That(replayFinish).IsEqualTo(expectedReason);
        var replay = await CompletedAsync(firstConversation.Control, 2);
        await Assert.That(replay.IsReplay).IsTrue();
        await Assert.That(replay.TerminalMarkerObserved).IsTrue();
        await Assert.That((await firstConversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
        await Assert.That((await secondSession.Control.GetConversationsAsync()).Count).IsEqualTo(0);
        var isolated = (await AzurePolicySdk(secondConversation.HttpClient).CompleteChatAsync(input)).Value;
        await Assert.That(isolated.FinishReason).IsEqualTo(expectedReason);
        await Assert.That(isolated.Usage.InputTokenDetails.CachedTokenCount).IsEqualTo(0);
        var next = (await firstSdk.CompleteChatAsync([.. input, new AssistantChatMessage(first), new UserChatMessage("next")])).Value;
        await Assert.That(next.Content.Single().Text).IsEqualTo(_recovery);
        await Assert.That(next.Usage.InputTokenDetails.CachedTokenCount).IsGreaterThan(0);
        var trace = await CompletedAsync(firstConversation.Control, 3);
        var assistant = trace.Messages.Single(message => message.Role == "assistant");
        await Assert.That(assistant.Content).IsEqualTo(_text);
        await Assert.That(assistant.OutputDisposition?.Kind).IsEqualTo(kind == LlmTestServerOutputDispositionKind.Refused ? kind : (LlmTestServerOutputDispositionKind?)null);
        await Assert.That((await firstConversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(2);
        await Assert.That((await secondConversation.Control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1);
    }

    [Test]
    [Arguments("/microsoft-foundry", false)]
    [Arguments("/microsoft-foundry", true)]
    [Arguments("/microsoft-foundry/models", false)]
    [Arguments("/microsoft-foundry/models", true)]
    public async Task FoundryInference_NativeSseCarriesFinalUsageWithOneChoiceWithoutUsageOnlyFramesAsync(string endpoint, bool filtered)
    {
        await using var host = await StartAsync(config => config.AddModel(LlmTestServerKnownModelIds.Gpt41Mini, LlmTestServerModelKind.Chat)
            .AddChatScenario("foundry-native-stream", scenario =>
            {
                scenario.ForModel(LlmTestServerKnownModelIds.Gpt41Mini).Responds(_text, "Visible ", "fixture text");
                if (filtered) { scenario.WithOutputDisposition(new() { Kind = LlmTestServerOutputDispositionKind.Filtered }); }
                scenario.Responds(_recovery);
            }));
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        // Raw protocol evidence supplements the original SDK tests. No stream_options extension is sent.
        using var response = await http.PostAsJsonAsync(endpoint + "/chat/completions?api-version=2024-05-01-preview", new
        {
            model = LlmTestServerKnownModelIds.Gpt41Mini,
            stream = true,
            messages = new[] { new { role = "user", content = "policy" } },
        });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var records = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal)).Select(line => line[5..].Trim()).ToArray();
        await Assert.That(records[^1]).IsEqualTo("[DONE]");
        var frames = records[..^1].Select(record => JsonSerializer.Deserialize<JsonElement>(record)).ToArray();
        await Assert.That(frames.Length).IsGreaterThan(1);
        await Assert.That(frames.All(frame => frame.GetProperty("choices").GetArrayLength() == 1)).IsTrue();
        foreach (var frame in frames[..^1])
        {
            await Assert.That(frame.GetProperty("choices")[0].GetProperty("finish_reason").ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(!frame.TryGetProperty("usage", out var intermediateUsage) || intermediateUsage.ValueKind == JsonValueKind.Null).IsTrue();
        }
        var final = frames[^1];
        await Assert.That(final.GetProperty("choices")[0].GetProperty("finish_reason").GetString()).IsEqualTo(filtered ? "content_filter" : "stop");
        var usage = final.GetProperty("usage");
        await Assert.That(usage.ValueKind).IsEqualTo(JsonValueKind.Object);
        await Assert.That(frames.Count(frame => frame.TryGetProperty("usage", out var value) && value.ValueKind == JsonValueKind.Object)).IsEqualTo(1);
        var trace = await CompletedAsync(control, 1);
        await Assert.That(trace.TerminalMarkerObserved).IsTrue();
        await Assert.That(trace.OutputDisposition?.Kind == LlmTestServerOutputDispositionKind.Filtered).IsEqualTo(filtered);
        await Assert.That(usage.GetProperty("prompt_tokens").GetInt32()).IsEqualTo(trace.Usage!.InputTokens);
        await Assert.That(usage.GetProperty("completion_tokens").GetInt32()).IsEqualTo(trace.Usage.OutputTokens);
        await Assert.That(usage.GetProperty("total_tokens").GetInt32()).IsEqualTo(trace.Usage.TotalTokens);
        await Assert.That((await control.VerifyAsync(new()
        { Requests = [new() { RequireStreamingTerminalMarker = true }] })).Passed).IsTrue();
        var next = (await FoundryPolicySdk(http, endpoint).CompleteAsync(new ChatCompletionsOptions([new ChatRequestUserMessage("next")])
        { Model = LlmTestServerKnownModelIds.Gpt41Mini })).Value;
        await Assert.That(next.Content).IsEqualTo(_recovery);
        await RecoveredAsync(control);
    }

    [Test]
    [Arguments("2024-10-21", LlmTestServerOutputDispositionKind.Filtered, false)]
    [Arguments("2024-10-21", LlmTestServerOutputDispositionKind.Filtered, true)]
    [Arguments("2024-10-21", LlmTestServerOutputDispositionKind.Refused, false)]
    [Arguments("2024-10-21", LlmTestServerOutputDispositionKind.Refused, true)]
    [Arguments("2025-04-01-preview", LlmTestServerOutputDispositionKind.Filtered, false)]
    [Arguments("2025-04-01-preview", LlmTestServerOutputDispositionKind.Filtered, true)]
    [Arguments("2025-04-01-preview", LlmTestServerOutputDispositionKind.Refused, false)]
    [Arguments("2025-04-01-preview", LlmTestServerOutputDispositionKind.Refused, true)]
    public async Task AzureDeployment_BothDocumentedVersionsReturnNativePolicyJsonAndSseAsync(
        string apiVersion, LlmTestServerOutputDispositionKind kind, bool streaming)
    {
        await using var host = await StartAsync(config =>
        {
            config.RequireBearerToken(_key);
            ConfigureQueue(config, LlmTestServerKnownModelIds.Gpt41Mini, kind);
        });
        using var http = host.Client(); var control = new LlmTestServerClient(http);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "/azure-openai/openai/deployments/" + LlmTestServerKnownModelIds.Gpt41Mini + "/chat/completions?api-version=" + apiVersion)
        {
            Content = JsonContent.Create(new
            {
                stream = streaming,
                messages = new[] { new { role = "user", content = "policy" } },
            }),
        };
        request.Headers.Add("api-key", _key);
        using var response = await http.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var text = new StringBuilder(); var refusal = new StringBuilder(); string? finish;
        if (streaming)
        {
            var records = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("data:", StringComparison.Ordinal)).Select(line => line[5..].Trim()).ToArray();
            await Assert.That(records[^1]).IsEqualTo("[DONE]");
            var choices = records[..^1].Select(record => JsonSerializer.Deserialize<JsonElement>(record))
                .SelectMany(frame => frame.GetProperty("choices").EnumerateArray()).ToArray();
            foreach (var choice in choices)
            {
                var delta = choice.GetProperty("delta");
                if (delta.TryGetProperty("content", out var content)) { text.Append(content.GetString()); }
                if (delta.TryGetProperty("refusal", out var refusalPart)) { refusal.Append(refusalPart.GetString()); }
            }
            finish = choices.Last().GetProperty("finish_reason").GetString();
        }
        else
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var choice = body.GetProperty("choices")[0]; var message = choice.GetProperty("message");
            if (message.TryGetProperty("content", out var content)) { text.Append(content.GetString()); }
            if (message.TryGetProperty("refusal", out var refusalPart)) { refusal.Append(refusalPart.GetString()); }
            finish = choice.GetProperty("finish_reason").GetString();
        }
        var filtered = kind == LlmTestServerOutputDispositionKind.Filtered;
        await Assert.That(finish).IsEqualTo(filtered ? "content_filter" : "stop");
        await Assert.That(text.ToString()).IsEqualTo(filtered ? _text : string.Empty);
        await Assert.That(refusal.ToString()).IsEqualTo(filtered ? string.Empty : _text);
        await EvidenceAsync(control, kind, streaming);
        var recovered = (await AzurePolicySdk(http).CompleteChatAsync([new UserChatMessage("next")])).Value;
        await Assert.That(recovered.Content.Single().Text).IsEqualTo(_recovery);
        await RecoveredAsync(control);
    }
}
