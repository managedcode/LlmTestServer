using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ManagedCode.LlmTestServer.Control;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Hosting;

public sealed class AdminPanelTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task AdminPanel_ServesStyledHtmlShellAtRootAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var client = host.GetTestClient();

        var response = await client.GetAsync(LlmTestServerControlRoutes.Admin);
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/html");
        await Assert.That(LlmTestServerControlRoutes.Admin).IsEqualTo("/");
        await Assert.That(body).Contains("<!doctype html>");
        await Assert.That(body).Contains("""<meta name="generator" content="Blazor SSR">""");
        await Assert.That(body).Contains("""<body data-renderer="blazor-ssr">""");
        await Assert.That(body).Contains("LLM Test Server request dashboard");
        await Assert.That(body).Contains(LlmTestServerControlRoutes.Models);
        await Assert.That(body).Contains(LlmTestServerControlRoutes.Assertions);
        await Assert.That(body).Contains("Fixtures / models");
        await Assert.That(body).Contains("Runtime actions");
        await Assert.That(body).Contains("Provider APIs");
        await Assert.That(body).Contains("Provider requests");
        await Assert.That(body).Contains("Runtime setup & diagnostics");
        await Assert.That(body).Contains("Request summary");
        await Assert.That(body).Contains("Cache read");
        await Assert.That(body).Contains("Cache write");
        await Assert.That(body).DoesNotContain("Calls / requests");
        await Assert.That(body).Contains("blazor.web.js");
        await Assert.That(body).DoesNotContain("setInterval(refresh, 4000)");
        await Assert.That(body).DoesNotContain("setTimeout(function ()");
        await Assert.That(body).DoesNotContain("autoCountdown");
        await Assert.That(body).Contains("Total tokens");
        await Assert.That(body).Contains("/openai");
        await Assert.That(body).Contains("/azure-openai");
        await Assert.That(body).Contains("/anthropic");
    }

    [Test]
    public async Task AdminPanel_StaysReachableWhenBearerTokenIsRequiredAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options.RequireBearerToken("test-key"));
        using var client = host.GetTestClient();

        var page = await client.GetAsync(LlmTestServerControlRoutes.Admin);
        var protectedModels = await client.GetAsync(LlmTestServerControlRoutes.Models);

        // The shell renders without a token so operators can enter one; the data
        // endpoints it calls remain protected.
        page.EnsureSuccessStatusCode();
        await Assert.That(protectedModels.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task RuntimeEvents_CaptureIncomingRequestAndModelResponseAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options.AddChatScenario(
                "capture-scenario",
                scenario => scenario
                    .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                    .WhenUserContains("color")
                    .Responds("blue whale")
            ));
        using var client = host.GetTestClient();

        var chat = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new
            {
                model = LlmTestServerKnownModelIds.Gpt41Mini,
                messages = new[] { new { role = "user", content = "what color is the whale" } },
            },
            _jsonOptions
        );
        chat.EnsureSuccessStatusCode();

        var summary = await client.GetFromJsonAsync<JsonElement>(
            LlmTestServerControlRoutes.Assertions,
            _jsonOptions
        );
        var events = summary.GetProperty("events");
        var lastEvent = events[events.GetArrayLength() - 1];

        // Operators must be able to see exactly what was asked and what the model answered.
        await Assert.That(lastEvent.GetProperty("request").GetString())
            .Contains("user: what color is the whale");
        await Assert.That(lastEvent.GetProperty("response").GetString()).IsEqualTo("blue whale");
        await Assert.That(summary.GetProperty("inputTokens").GetInt32()).IsEqualTo(5);
        await Assert.That(summary.GetProperty("outputTokens").GetInt32()).IsEqualTo(2);
        await Assert.That(summary.GetProperty("reasoningTokens").GetInt32()).IsEqualTo(0);
        await Assert.That(summary.GetProperty("totalTokens").GetInt32()).IsEqualTo(7);
        await Assert.That(lastEvent.GetProperty("usage").GetProperty("inputTokens").GetInt32())
            .IsEqualTo(5);
        await Assert.That(lastEvent.GetProperty("usage").GetProperty("outputTokens").GetInt32())
            .IsEqualTo(2);
        await Assert.That(lastEvent.GetProperty("usage").GetProperty("reasoningTokens").GetInt32())
            .IsEqualTo(0);
        await Assert.That(lastEvent.GetProperty("usage").GetProperty("totalTokens").GetInt32())
            .IsEqualTo(7);
    }

    [Test]
    public async Task ProviderRequestTrace_CapturesProviderMultiTurnPayloadAndRuntimeUsageAsync()
    {
        var longSystemMessage = $"{new string('x', 900)} END_OF_LONG_PROMPT";
        using var host = await LlmTestServerTestHost.StartAsync(options => options.AddChatScenario(
                "trace-multi-turn",
                scenario => scenario
                    .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                    .WhenUserContains("color")
                    .Responds("blue whale")
            ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new
            {
                model = LlmTestServerKnownModelIds.Gpt41Mini,
                messages = new[]
                {
                    new { role = "system", content = longSystemMessage },
                    new { role = "user", content = "What did you answer before?" },
                    new { role = "assistant", content = "A blue whale." },
                    new { role = "user", content = "What color is it now?" },
                },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var trace = (await GetTracesAsync(client)).Single();
        var runtimeEvent = (await client.GetFromJsonAsync<LlmTestServerAssertionSummary>(LlmTestServerControlRoutes.Assertions))!.Events.Single();

        await Assert.That(trace.ProviderId).IsEqualTo(LlmTestServerCompatibilityTags.OpenAI);
        await Assert.That(trace.Method).IsEqualTo(HttpMethod.Post.Method);
        await Assert.That(trace.Path).IsEqualTo("/openai/v1/chat/completions");
        await Assert.That(trace.OperationId)
            .IsEqualTo(LlmTestServerProviderOperationIds.OpenAI.ChatCompletionsCreate);
        await Assert.That(trace.ModelId).IsEqualTo(LlmTestServerKnownModelIds.Gpt41Mini);
        await Assert.That(trace.StatusCode).IsEqualTo((int)HttpStatusCode.OK);
        await Assert.That(trace.DurationMilliseconds).IsNotNull();
        await Assert.That(trace.DurationMilliseconds!.Value).IsGreaterThanOrEqualTo(0d);
        await Assert.That(trace.RequestPreview).Contains("END_OF_LONG_PROMPT");
        await Assert.That(trace.ResponsePreview).Contains("blue whale");
        await Assert.That(trace.RuntimeResponse).IsEqualTo("blue whale");
        await Assert.That(trace.Messages.Count).IsEqualTo(4);
        await Assert.That(trace.Messages[0].Role).IsEqualTo("system");
        await Assert.That(trace.Messages[0].Content).IsEqualTo(longSystemMessage);
        await Assert.That(trace.Messages[1].Role).IsEqualTo("user");
        await Assert.That(trace.Messages[1].Content).IsEqualTo("What did you answer before?");
        await Assert.That(trace.Messages[2].Role).IsEqualTo("assistant");
        await Assert.That(trace.Messages[2].Content).IsEqualTo("A blue whale.");
        await Assert.That(trace.Messages[3].Role).IsEqualTo("user");
        await Assert.That(trace.Messages[3].Content).IsEqualTo("What color is it now?");
        await Assert.That(trace.Usage).IsNotNull();
        await Assert.That(trace.Usage!.TotalTokens).IsGreaterThan(0);
        await Assert.That(runtimeEvent.RequestId).IsEqualTo(trace.RequestId);
        await Assert.That(runtimeEvent.Messages.Count).IsEqualTo(4);
        await Assert.That(response.Headers.GetValues(LlmTestServerProviderHttpTraceOptions.DefaultRequestIdHeaderName).Single())
            .IsEqualTo(trace.RequestId);
    }

    [Test]
    public async Task ProviderRequestTrace_CorrelatesMultipartAudioModelAndOperationAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .WithDefaultTranscriptionText("deterministic transcript"));
        using var client = host.GetTestClient();
        using var content = CreateTranscriptionContent(LlmTestServerKnownModelIds.Gpt4OMiniTts);

        var response = await client.PostAsync("/openai/v1/audio/transcriptions", content);

        response.EnsureSuccessStatusCode();
        var trace = (await GetTracesAsync(client))
            .Single();

        await Assert.That(trace.ProviderId).IsEqualTo(LlmTestServerCompatibilityTags.OpenAI);
        await Assert.That(trace.ModelId).IsEqualTo(LlmTestServerKnownModelIds.Gpt4OMiniTts);
        await Assert.That(trace.OperationId)
            .IsEqualTo(LlmTestServerProviderOperationIds.OpenAI.AudioTranscriptionsCreate);
        await Assert.That(trace.RuntimeEventKind).IsEqualTo(LlmTestServerEventKind.Matched);
        await Assert.That(trace.RequestPreview).Contains("[multipart body summary;");
        await Assert.That(trace.RequestPreview)
            .Contains($"field model: {LlmTestServerKnownModelIds.Gpt4OMiniTts}");
        await Assert.That(trace.ResponsePreview).Contains("deterministic transcript");
    }

    [Test]
    public async Task ProviderRequestTrace_CapturesStreamingChunksAndRendersInspectorAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options.AddChatScenario(
                "trace-streaming",
                scenario => scenario
                    .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                    .WhenUserContains("stream response")
                    .Responds("blue whale", "blue ", "whale")
            ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new
            {
                model = LlmTestServerKnownModelIds.Gpt41Mini,
                stream = true,
                messages = new[] { new { role = "user", content = "stream response" } },
            },
            _jsonOptions
        );
        var responseBody = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        var trace = (await GetTracesAsync(client))
            .Single();
        var page = await client.GetStringAsync(LlmTestServerControlRoutes.Admin);
        var chunkSectionIndex = page.IndexOf("Stream chunks", StringComparison.Ordinal);
        var firstChunkIndex = page.IndexOf("blue ", chunkSectionIndex, StringComparison.Ordinal);
        var secondChunkIndex = page.IndexOf("whale", firstChunkIndex + "blue ".Length, StringComparison.Ordinal);

        await Assert.That(responseBody).Contains("data: ");
        await Assert.That(responseBody).Contains("data: [DONE]");
        await Assert.That(trace.IsStreaming).IsTrue();
        await Assert.That(trace.OperationId)
            .IsEqualTo(LlmTestServerProviderOperationIds.OpenAI.ChatCompletionsCreate);
        await Assert.That(trace.StreamChunks.Count).IsEqualTo(2);
        await Assert.That(trace.StreamChunks[0]).IsEqualTo("blue ");
        await Assert.That(trace.StreamChunks[1]).IsEqualTo("whale");
        await Assert.That(trace.ResponsePreview).Contains("data: ");
        await Assert.That(trace.ResponsePreview).Contains("[DONE]");
        await Assert.That(page).Contains("2 ordered chunks");
        await Assert.That(CountOccurrences(page, "class=\"chunk\"")).IsEqualTo(2);
        await Assert.That(chunkSectionIndex).IsGreaterThan(0);
        await Assert.That(firstChunkIndex).IsGreaterThan(chunkSectionIndex);
        await Assert.That(secondChunkIndex).IsGreaterThan(firstChunkIndex);
    }

    [Test]
    public async Task ProviderRequestTrace_RedactsNestedCredentialsAndPreservesUsageFieldsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options.AddChatScenario(
                "trace-redaction",
                scenario => scenario
                    .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                    .WhenUserContains("redact credentials")
                    .Responds("safe response")
            ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new
            {
                model = LlmTestServerKnownModelIds.Gpt41Mini,
                max_tokens = 321,
                refresh_token = "refresh-secret-value",
                metadata = new
                {
                    github_token = "github-secret-value",
                    private_key = "private-secret-value",
                    nested = new { session_token = "session-secret-value" },
                },
                messages = new[] { new { role = "user", content = "redact credentials" } },
            },
            _jsonOptions
        );

        response.EnsureSuccessStatusCode();
        var trace = (await GetTracesAsync(client))
            .Single();
        var preview = trace.RequestPreview
            ?? throw new InvalidOperationException("Expected a captured JSON request preview.");
        using var document = JsonDocument.Parse(preview);
        var root = document.RootElement;
        var metadata = root.GetProperty("metadata");

        await Assert.That(trace.RequestPreviewTruncated).IsFalse();
        await Assert.That(root.GetProperty("max_tokens").GetInt32()).IsEqualTo(321);
        await Assert.That(root.GetProperty("refresh_token").GetString()).IsEqualTo("[redacted]");
        await Assert.That(metadata.GetProperty("github_token").GetString()).IsEqualTo("[redacted]");
        await Assert.That(metadata.GetProperty("private_key").GetString()).IsEqualTo("[redacted]");
        await Assert.That(metadata.GetProperty("nested").GetProperty("session_token").GetString())
            .IsEqualTo("[redacted]");
        await Assert.That(preview).DoesNotContain("refresh-secret-value");
        await Assert.That(preview).DoesNotContain("github-secret-value");
        await Assert.That(preview).DoesNotContain("private-secret-value");
        await Assert.That(preview).DoesNotContain("session-secret-value");
    }

    [Test]
    public async Task ProviderRequestTrace_CapturesHttpOnlyAndValidationFailureRequestsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var client = host.GetTestClient();

        var models = await client.GetAsync("/openai/v1/models");
        var invalid = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new { model = LlmTestServerKnownModelIds.Gpt41Mini },
            _jsonOptions
        );

        models.EnsureSuccessStatusCode();
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        var traces = (await GetTracesAsync(client));
        var modelsTrace = traces.Single(trace => trace.Path.EndsWith("/models", StringComparison.Ordinal));
        var invalidTrace = traces.Single(trace => trace.Path.EndsWith("/chat/completions", StringComparison.Ordinal));

        await Assert.That(traces.Count).IsEqualTo(2);
        await Assert.That(modelsTrace.StatusCode).IsEqualTo((int)HttpStatusCode.OK);
        await Assert.That(modelsTrace.RuntimeEventKind).IsNull();
        await Assert.That(invalidTrace.StatusCode).IsEqualTo((int)HttpStatusCode.BadRequest);
        await Assert.That(invalidTrace.RuntimeEventKind).IsNull();
        await Assert.That(invalidTrace.ResponsePreview).Contains("invalid_request");
    }

    [Test]
    public async Task ProviderFallbacks_TraceUnknownAndWrongMethodRequestsAndRenderBothRowsAsync()
    {
        const string FallbackModel = "fallback-diagnostic-model";
        const string RequestPayload = "wrong method fallback payload";
        using var host = await LlmTestServerTestHost.StartAsync();
        using var client = host.GetTestClient();

        var missing = await client.GetAsync("/openai/v1/diagnostics/not-supported");
        var wrongMethod = await client.PostAsJsonAsync(
            "/openai/v1/models",
            new { model = FallbackModel, request = RequestPayload },
            _jsonOptions
        );

        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(wrongMethod.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
        await Assert.That(wrongMethod.Content.Headers.Allow.Single()).IsEqualTo("GET");

        var traces = (await GetTracesAsync(client));
        var missingTrace = traces.Single(trace =>
            trace.Path.EndsWith("/diagnostics/not-supported", StringComparison.Ordinal)
        );
        var wrongMethodTrace = traces.Single(trace =>
            trace.Path.EndsWith("/v1/models", StringComparison.Ordinal)
        );
        var preview = wrongMethodTrace.RequestPreview
            ?? throw new InvalidOperationException("Expected the fallback request payload to be captured.");
        using var previewDocument = JsonDocument.Parse(preview);
        var previewRoot = previewDocument.RootElement;

        await Assert.That(traces.Count).IsEqualTo(2);
        await Assert.That(missingTrace.ProviderId).IsEqualTo(LlmTestServerCompatibilityTags.OpenAI);
        await Assert.That(missingTrace.StatusCode).IsEqualTo((int)HttpStatusCode.NotFound);
        await Assert.That(missingTrace.TransportState)
            .IsEqualTo(LlmTestServerProviderHttpTraceTransportState.Completed);
        await Assert.That(missingTrace.OperationId).IsNull();
        await Assert.That(wrongMethodTrace.ProviderId).IsEqualTo(LlmTestServerCompatibilityTags.OpenAI);
        await Assert.That(wrongMethodTrace.Method).IsEqualTo(HttpMethod.Post.Method);
        await Assert.That(wrongMethodTrace.StatusCode)
            .IsEqualTo((int)HttpStatusCode.MethodNotAllowed);
        await Assert.That(wrongMethodTrace.TransportState)
            .IsEqualTo(LlmTestServerProviderHttpTraceTransportState.Completed);
        await Assert.That(wrongMethodTrace.OperationId).IsNull();
        await Assert.That(wrongMethodTrace.ModelId).IsEqualTo(FallbackModel);
        await Assert.That(previewRoot.GetProperty("model").GetString()).IsEqualTo(FallbackModel);
        await Assert.That(previewRoot.GetProperty("request").GetString()).IsEqualTo(RequestPayload);

        var page = await client.GetStringAsync(LlmTestServerControlRoutes.Admin);
        await Assert.That(CountOccurrences(page, "data-testid=\"request-row\"")).IsEqualTo(2);
        await Assert.That(page).Contains($"data-request-id=\"{missingTrace.RequestId}\"");
        await Assert.That(page).Contains($"data-request-id=\"{wrongMethodTrace.RequestId}\"");
        await Assert.That(page).Contains(FallbackModel);
        await Assert.That(page).Contains(RequestPayload);
    }

    [Test]
    public async Task AdminPanel_RendersScriptedErrorResponseAsProviderErrorAsync()
    {
        const string ErrorMessage = "Scripted provider refusal.";
        using var host = await LlmTestServerTestHost.StartAsync(options => options.AddChatScenario(
                "provider-error-label",
                scenario => scenario
                    .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                    .WhenUserContains("trigger provider error")
                    .Fails(400, "content_filter", ErrorMessage)
            ));
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new
            {
                model = LlmTestServerKnownModelIds.Gpt41Mini,
                messages = new[] { new { role = "user", content = "trigger provider error" } },
            },
            _jsonOptions
        );

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var trace = (await GetTracesAsync(client))
            .Single();
        await Assert.That(trace.RuntimeEventKind).IsEqualTo(LlmTestServerEventKind.ErrorReturned);
        await Assert.That(trace.RuntimeResponse).IsEqualTo(ErrorMessage);

        var page = await client.GetStringAsync(LlmTestServerControlRoutes.Admin);
        await Assert.That(page).Contains("<article class=\"turn error\"");
        await Assert.That(page).Contains("<div class=\"turn-role\">provider error</div>");
        await Assert.That(page).Contains(ErrorMessage);
    }

    [Test]
    public async Task ProviderRequestTrace_CorrelatesCacheWriteThenCacheReadPerRequestAsync()
    {
        var cacheablePrefix = string.Join(' ', Enumerable.Repeat("stable-prefix", 1200));
        using var host = await LlmTestServerTestHost.StartAsync(options => options.AddChatScenario(
                "trace-cache",
                scenario => scenario
                    .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                    .WhenUserContains("color")
                    .Responds("first response")
                    .Responds("second response")
            ));
        using var client = host.GetTestClient();
        var request = new
        {
            model = LlmTestServerKnownModelIds.Gpt41Mini,
            messages = new[]
            {
                new { role = "system", content = cacheablePrefix },
                new { role = "user", content = "What color is the whale?" },
            },
        };

        (await client.PostAsJsonAsync("/openai/v1/chat/completions", request, _jsonOptions))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/openai/v1/chat/completions", request, _jsonOptions))
            .EnsureSuccessStatusCode();

        var traces = (await GetTracesAsync(client));
        var second = traces[0];
        var first = traces[1];
        var page = await client.GetStringAsync(LlmTestServerControlRoutes.Admin);

        await Assert.That(traces.Count).IsEqualTo(2);
        await Assert.That(first.Usage).IsNotNull();
        await Assert.That(second.Usage).IsNotNull();
        await Assert.That(first.Usage!.CachedInputTokens).IsEqualTo(0);
        await Assert.That(first.Usage.CacheCreationInputTokens).IsGreaterThanOrEqualTo(1024);
        await Assert.That(second.Usage!.CachedInputTokens).IsGreaterThanOrEqualTo(1024);
        await Assert.That(second.Usage.CacheCreationInputTokens).IsEqualTo(0);
        await Assert.That(second.RequestId).IsNotEqualTo(first.RequestId);
        await Assert.That(CountOccurrences(page, "data-testid=\"request-row\"")).IsEqualTo(2);
        await Assert.That(page).Contains($"data-request-id=\"{first.RequestId}\"");
        await Assert.That(page).Contains($"data-request-id=\"{second.RequestId}\"");
        await Assert.That(first.RuntimeResponse).IsEqualTo("first response");
        await Assert.That(second.RuntimeResponse).IsEqualTo("second response");
    }

    [Test]
    public async Task AdminPanel_RendersOneRequestRowWithFullConversationAndExchangeAsync()
    {
        var longSystemMessage = $"{new string('z', 900)} END_OF_LONG_PROMPT";
        using var host = await LlmTestServerTestHost.StartAsync(options => options.AddChatScenario(
                "dashboard-multi-turn",
                scenario => scenario
                    .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
                    .WhenUserContains("color")
                    .Responds("blue whale")
            ));
        using var client = host.GetTestClient();
        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new
            {
                model = LlmTestServerKnownModelIds.Gpt41Mini,
                messages = new[]
                {
                    new { role = "system", content = longSystemMessage },
                    new { role = "user", content = "Previous question" },
                    new { role = "assistant", content = "Previous answer" },
                    new { role = "user", content = "What color is the whale?" },
                },
            },
            _jsonOptions
        );
        response.EnsureSuccessStatusCode();

        var page = await client.GetStringAsync(LlmTestServerControlRoutes.Admin);
        var conversationIndex = page.IndexOf("data-testid=\"conversation\"", StringComparison.Ordinal);
        var responseIndex = page.IndexOf("data-response-card=\"true\"", StringComparison.Ordinal);
        var collapsedSystemIndex = page.IndexOf(
            "data-collapsed-by-default=\"true\"",
            StringComparison.Ordinal
        );

        await Assert.That(CountOccurrences(page, "data-testid=\"request-row\"")).IsEqualTo(1);
        await Assert.That(CountOccurrences(page, "data-testid=\"conversation-turn\"")).IsEqualTo(5);
        await Assert.That(page).Contains("OpenAI");
        await Assert.That(page).Contains(LlmTestServerKnownModelIds.Gpt41Mini);
        await Assert.That(page).Contains("Request metadata");
        await Assert.That(page).Contains("Tokens & cache");
        await Assert.That(page).Contains("Wire payloads");
        await Assert.That(page).Contains("END_OF_LONG_PROMPT");
        await Assert.That(page).Contains("Previous answer");
        await Assert.That(page).Contains("blue whale");
        await Assert.That(page).Contains("aria-selected=\"true\"");
        await Assert.That(page).Contains("aria-selected=\"false\"");
        await Assert.That(page).Contains("aria-pressed=\"true\"");
        await Assert.That(page).Contains("aria-pressed=\"false\"");
        await Assert.That(page).Contains("role=\"tabpanel\"");
        await Assert.That(conversationIndex).IsGreaterThan(0);
        await Assert.That(responseIndex).IsGreaterThan(conversationIndex);
        await Assert.That(collapsedSystemIndex).IsGreaterThan(responseIndex);
        await Assert.That(page.IndexOf("END_OF_LONG_PROMPT", conversationIndex, StringComparison.Ordinal))
            .IsGreaterThan(conversationIndex);
    }

    [Test]
    public async Task AdminPanel_PrettyPrintsJsonWithoutUnicodeEscapeNoiseAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new
            {
                model = "missing-model",
                messages = new[] { new { role = "user", content = "Show the error payload" } },
            },
            _jsonOptions
        );

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var page = await client.GetStringAsync(LlmTestServerControlRoutes.Admin);

        await Assert.That(page).Contains("data-testid=\"overview-response-json\"");
        await Assert.That(page).Contains("&quot;error&quot;");
        await Assert.That(page).Contains("missing-model");
        await Assert.That(page).Contains("provider error");
        await Assert.That(page).DoesNotContain("\\u0027");
    }

    [Test]
    public async Task AdminPanel_LabelsNonChatJsonAsProviderResponseAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var client = host.GetTestClient();

        (await client.GetAsync("/openai/v1/models")).EnsureSuccessStatusCode();
        var page = await client.GetStringAsync(LlmTestServerControlRoutes.Admin);

        await Assert.That(page).Contains("provider response");
        await Assert.That(page).DoesNotContain(">assistant</span>");
    }

    [Test]
    public async Task ResetControlRoute_ClearsProviderRequestJournalAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var client = host.GetTestClient();

        (await client.GetAsync("/openai/v1/models")).EnsureSuccessStatusCode();
        await Assert.That((await GetTracesAsync(client)).Count).IsEqualTo(1);

        (await client.PostAsync(LlmTestServerControlRoutes.Reset, content: null)).EnsureSuccessStatusCode();
        await Assert.That((await GetTracesAsync(client))).IsEmpty();
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private static MultipartFormDataContent CreateTranscriptionContent(string model)
    {
        var content = new MultipartFormDataContent();
        var audio = new ByteArrayContent(Encoding.ASCII.GetBytes("RIFF....WAVE"));
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audio, "file", "fixture.wav");
        content.Add(new StringContent(model), "model");
        content.Add(new StringContent("json"), "response_format");
        return content;
    }

    private static async Task<List<LlmTestServerProviderHttpTrace>> GetTracesAsync(HttpClient client)
    {
        return await client.GetFromJsonAsync<List<LlmTestServerProviderHttpTrace>>(LlmTestServerControlRoutes.Requests) ?? [];
    }
}
