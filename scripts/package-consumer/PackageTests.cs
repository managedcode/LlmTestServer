using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;
using ManagedCode.LlmTestServer.Client;
using Microsoft.Playwright;
using TUnit.Core;
using static Microsoft.Playwright.Assertions;

public sealed class PackageTests
{
    [Test]
    public async Task InstalledPackage_ServesAssetsAndInteractiveDashboard(CancellationToken cancellationToken)
    {
        var builder = DistributedApplicationTestingBuilder.Create([]);
        var resource = builder.AddLlmTestServer("package-test-server").WithApiKey("test-key");
        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("package-test-server", cancellationToken);
        using var http = app.CreateHttpClient("package-test-server", "http");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-key");
        foreach (var path in new[] { "/health", "/admin-api/models", "/_framework/blazor.web.js", "/_content/Microsoft.FluentUI.AspNetCore.Components/Microsoft.FluentUI.AspNetCore.Components.lib.module.js" })
        {
            using var response = await http.GetAsync(path, cancellationToken);
            response.EnsureSuccessStatusCode();
            var length = (await response.Content.ReadAsByteArrayAsync(cancellationToken)).Length;
            Console.WriteLine($"{path}: {(int)response.StatusCode}, {length} bytes");
            if (path.EndsWith(".js", StringComparison.Ordinal) && length < 100)
                throw new InvalidOperationException($"Missing JavaScript asset: {path}");
        }
        using (var configuration = new StringContent("""
            {"requiredBearerToken":"test-key","models":[{"id":"gpt-4.1-mini","kind":"chat"}],
             "chatScenarios":[{"id":"package-tools","responses":[{"toolCalls":[{"id":"call_package","name":"weather","argumentsJson":"{\"city\":\"Paris\"}"}]}]}]}
            """, Encoding.UTF8, "application/json"))
        using (var configured = await http.PostAsync("/admin-api/configure", configuration, cancellationToken))
            configured.EnsureSuccessStatusCode();
        using (var request = new StringContent("""
            {"model":"gpt-4.1-mini","messages":[{"role":"user","content":"weather"}],
             "tools":[{"type":"function","function":{"name":"weather","parameters":{"type":"object","required":["city"],"properties":{"city":{"const":"Paris"}}}}}],"tool_choice":"required"}
            """, Encoding.UTF8, "application/json"))
        using (var response = await http.PostAsync("/openai/v1/chat/completions", request, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var function = payload.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("tool_calls")[0].GetProperty("function");
            if (function.GetProperty("name").GetString() != "weather" || function.GetProperty("arguments").GetString() != "{\"city\":\"Paris\"}")
                throw new InvalidOperationException("Installed package lost the schema-validated tool fixture.");
        }
        // Verify the published Aspire service actually includes every native decision adapter.
        using (var configuration = new StringContent("""
            {"requiredBearerToken":"test-key","models":[{"id":"kev-latest","kind":"decision","decisionProvider":"kev"},{"id":"clef","kind":"decision","decisionProvider":"cloudflare"},{"id":"gpt-6-luna","kind":"decision","decisionProvider":"openAI"}],
             "decisionScenarios":[{"id":"kev","modelId":"kev-latest","answers":{"binary":{"kind":"predicate","probability":0.95}}},
              {"id":"clef","modelId":"clef","answers":{"binary":{"kind":"predicate","probability":0.95}}},
              {"id":"openai","modelId":"gpt-6-luna","answers":{"0":{"kind":"predicate","probability":0.95}}}]}
            """, Encoding.UTF8, "application/json"))
        using (var configured = await http.PostAsync("/admin-api/configure", configuration, cancellationToken))
            configured.EnsureSuccessStatusCode();
        foreach (var (path, model) in new[] { ("/systemone/v1/systemone", "kev-latest"), ("/cloudflare/client/v4/accounts/test/ai/run/@cf/cloudflare/clef", "clef"), ("/openai/v1/decisions", "gpt-6-luna") })
        {
            var body = model == "gpt-6-luna"
                ? """{"model":"gpt-6-luna","input":"evidence","questions":[{"type":"predicate","name":"binary","instructions":"binary?"}]}"""
                : """{"model":"MODEL","state":"evidence","questions":{"binary":{"type":"noul","instructions":"binary?"}}}""".Replace("MODEL", model, StringComparison.Ordinal);
            if (model == "clef")
            {
                const string image = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGNgYGAAAAAEAAH2FzhVAAAAAElFTkSuQmCC";
                var native = JsonNode.Parse(body)!.AsObject(); native["images"] = new JsonArray(image); body = native.ToJsonString();
            }
            using var request = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(path, request, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (!(await response.Content.ReadAsStringAsync(cancellationToken)).Contains("0.95", StringComparison.Ordinal))
                throw new InvalidOperationException("Installed package lost the native decision fixture: " + model);
        }
        var control = new LlmTestServerClient(http, "test-key");
        var dashboardDemo = await CreateDashboardCacheDemoAsync(control, http, cancellationToken);
        var dashboardEvidence = dashboardDemo.NamedConversation;
        const int defaultMatchedRequests = 4;
        await using (var session = await control.CreateSessionAsync(config => config.RequireBearerToken("test-key")
            .UseChatScenario("package-session", scenario => scenario.Responds("isolated package response")), cancellationToken))
        {
            using var sessionChat = session.Control.CreateChatClient();
            var sessionReply = await sessionChat.GetResponseAsync(
                [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "session request")], cancellationToken: cancellationToken);
            if (sessionReply.Text != "isolated package response" || (await session.Control.GetAssertionsAsync(cancellationToken)).Matched != 1
                || (await control.GetAssertionsAsync(cancellationToken)).Matched != defaultMatchedRequests)
                throw new InvalidOperationException("Installed package sessions must isolate requests and assertions from the dashboard runtime.");
        }
        await VerifyInstalledConversationGraphAsync(control, defaultMatchedRequests, cancellationToken);
        await VerifyInstalledToolPresetAsync(control, cancellationToken);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var recordingDirectory = Environment.GetEnvironmentVariable("LLM_TEST_SERVER_DEMO_RECORDING_DIRECTORY");
        await using var browserContext = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 1000 },
            RecordVideoDir = recordingDirectory,
            RecordVideoSize = recordingDirectory is null ? null : new() { Width = 1440, Height = 1000 },
        });
        var recordedScenes = new List<object>();
        var sceneLabels = new[] { "Authorization", "Request inbox", "Overview", "Cache miss", "Cache hit", "Conversation analytics", "Unlabeled request", "Conversation history", "Runtime", "Mobile detail" };
        var recordingClock = recordingDirectory is null ? null : System.Diagnostics.Stopwatch.StartNew();
        var page = await browserContext.NewPageAsync();
        async Task HoldDemoFrameAsync(int seconds)
        {
            if (recordingDirectory is not null)
            {
                var index = recordedScenes.Count + 1;
                recordedScenes.Add(new { Index = index, Label = sceneLabels[index - 1], StartSeconds = recordingClock!.Elapsed.TotalSeconds, DurationSeconds = seconds });
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
            }
        }
        await page.GotoAsync(http.BaseAddress!.ToString());
        await HoldDemoFrameAsync(7);
        var runtimeTrigger = page.GetByRole(AriaRole.Button, new() { Name = "Runtime settings", Exact = true });
        await runtimeTrigger.ClickAsync();
        await Expect(page.GetByLabel("Bearer token", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(runtimeTrigger).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(page.GetByRole(AriaRole.Alertdialog, new() { Name = "Runtime state" })).ToBeVisibleAsync();
        await page.ScreenshotAsync(new() { Path = "package-dashboard.png", FullPage = true });
        await page.GetByLabel("Bearer token", new() { Exact = true }).FillAsync("test-key");
        await page.GetByRole(AriaRole.Button, new() { Name = "Close runtime settings", Exact = true }).ClickAsync();
        await Expect(page.GetByLabel("Bearer token", new() { Exact = true })).Not.ToBeVisibleAsync();
        await Expect(runtimeTrigger).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(runtimeTrigger).ToBeFocusedAsync();
        await runtimeTrigger.ClickAsync();
        await Expect(page.GetByLabel("Bearer token", new() { Exact = true })).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.GetByLabel("Bearer token", new() { Exact = true })).Not.ToBeVisibleAsync();
        await Expect(runtimeTrigger).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(runtimeTrigger).ToBeFocusedAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh runtime state" }).ClickAsync();
        await Expect(page.GetByText("Unauthorized", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("request-row")).ToHaveCountAsync(4);
        await page.SetViewportSizeAsync(1440, 1000);
        await HoldDemoFrameAsync(7);
        await Expect(page.GetByRole(AriaRole.Alertdialog, new() { Name = "Runtime state" })).Not.ToBeVisibleAsync();
        var headerBounds = await page.Locator(".topbar").BoundingBoxAsync();
        var workspaceBounds = await page.Locator(".mail-workspace").BoundingBoxAsync();
        if (headerBounds is null || workspaceBounds is null || Math.Abs(workspaceBounds.Y - headerBounds.Y - headerBounds.Height) > 2)
            throw new InvalidOperationException("Closed runtime dialog must not leave empty space above the request workspace.");
        var selectedDemoRequest = dashboardEvidence.Requests[^1];
        await page.Locator($"[data-testid='request-row'][data-request-id='{selectedDemoRequest.RequestId}']").ClickAsync();
        await Expect(page.GetByTestId("request-details")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("selected-request-id")).ToHaveTextAsync(selectedDemoRequest.RequestId);
        await page.ScreenshotAsync(new() { Path = "package-dashboard-overview.jpg", FullPage = true, Quality = 85 });
        await HoldDemoFrameAsync(6);
        var uncachedDemoRequest = dashboardEvidence.Requests[0];
        await page.Locator($"[data-testid='request-row'][data-request-id='{uncachedDemoRequest.RequestId}']").ClickAsync();
        await page.GetByTestId("analytics-tab").ClickAsync();
        await Expect(page.GetByTestId("analytics-panel")).ToBeVisibleAsync();
        await page.GetByTestId("analytics-scope-request").ClickAsync();
        await AssertDashboardAnalyticsAsync(page, [uncachedDemoRequest], dashboardEvidence);
        await page.ScreenshotAsync(new() { Path = "package-dashboard-cache-miss.jpg", FullPage = true, Quality = 85 });
        await HoldDemoFrameAsync(10);
        await page.Locator($"[data-testid='request-row'][data-request-id='{selectedDemoRequest.RequestId}']").ClickAsync();
        await page.GetByTestId("analytics-tab").ClickAsync();
        await Expect(page.GetByTestId("analytics-panel")).ToBeVisibleAsync();
        await page.GetByTestId("analytics-scope-request").ClickAsync();
        await Expect(page.GetByTestId("analytics-scope-request")).ToHaveAttributeAsync("aria-pressed", "true");
        await AssertDashboardAnalyticsAsync(page, [selectedDemoRequest], dashboardEvidence);
        await page.ScreenshotAsync(new() { Path = "package-dashboard-token-usage.png", FullPage = true });
        await page.ScreenshotAsync(new() { Path = "package-dashboard-cache-request.jpg", FullPage = true, Quality = 85 });
        await HoldDemoFrameAsync(10);
        await page.GetByTestId("analytics-scope-conversation").ClickAsync();
        await Expect(page.GetByTestId("analytics-scope-conversation")).ToHaveAttributeAsync("aria-pressed", "true");
        await AssertDashboardAnalyticsAsync(page, dashboardEvidence.Requests, dashboardEvidence, conversationScope: true);
        var analyticsLayout = new Dictionary<string, object?>();
        foreach (var (label, locator) in new[]
        {
            ("cache-share", page.GetByTestId("analytics-cache-share")),
            ("token-flow", page.GetByTestId("analytics-flow")),
            ("weather-row", page.Locator("[data-testid='analytics-tool-row'][data-tool-name='weather']")),
            ("analytics-panel", page.GetByTestId("analytics-panel")),
        })
        {
            var bounds = await locator.BoundingBoxAsync();
            analyticsLayout[label] = bounds;
            // The headline figures and the top of the token flow must be on screen; detail cards such as tools sit below the fold.
            if ((label is "cache-share" && (bounds is null || bounds.X < 0 || bounds.Y < 0 || bounds.X + bounds.Width > 1440 || bounds.Y + bounds.Height > 1000))
                || (label is "token-flow" && (bounds is null || bounds.X < 0 || bounds.Y < 0 || bounds.X + bounds.Width > 1440 || bounds.Y > 1000 - 160)))
                throw new InvalidOperationException($"Dashboard analytics {label} must be visible in the first desktop viewport: {JsonSerializer.Serialize(bounds)}.");
        }
        await File.WriteAllTextAsync("package-dashboard-analytics-layout.json",
            JsonSerializer.Serialize(new { ViewportWidth = 1440, ViewportHeight = 1000, Bounds = analyticsLayout },
                new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        await page.ScreenshotAsync(new() { Path = "package-dashboard-cache-conversation.jpg", FullPage = true, Quality = 85 });
        await HoldDemoFrameAsync(12);
        await page.Locator($"[data-testid='request-row'][data-request-id='{dashboardDemo.UnlabeledRequest.RequestId}']").ClickAsync();
        await page.GetByTestId("analytics-tab").ClickAsync();
        await Expect(page.GetByTestId("analytics-scope-conversation")).ToBeDisabledAsync();
        await Expect(page.GetByTestId("analytics-scope-request")).ToHaveAttributeAsync("aria-pressed", "true");
        await AssertDashboardAnalyticsAsync(page, [dashboardDemo.UnlabeledRequest], dashboardEvidence);
        await HoldDemoFrameAsync(8);
        await page.Locator($"[data-testid='request-row'][data-request-id='{selectedDemoRequest.RequestId}']").ClickAsync();
        await page.GetByRole(AriaRole.Tab, new() { Name = "Conversation", Exact = false }).ClickAsync();
        await page.ScreenshotAsync(new() { Path = "package-dashboard-conversation.jpg", FullPage = true, Quality = 85 });
        await HoldDemoFrameAsync(12);
        var insightsTrigger = page.GetByTestId("insights-open");
        await insightsTrigger.ClickAsync();
        await Expect(page.GetByTestId("insights-panel")).ToBeVisibleAsync();
        await Expect(insightsTrigger).ToHaveAttributeAsync("aria-pressed", "true");
        var journalNow = await control.GetRequestsAsync(cancellationToken: cancellationToken);
        foreach (var provider in journalNow.Requests.GroupBy(trace => trace.ProviderId))
            await Expect(page.Locator($"[data-testid='insight-providers-value'][data-key='{provider.Key}']"))
                .ToHaveAttributeAsync("data-value", provider.Count().ToString(CultureInfo.InvariantCulture));
        await page.ScreenshotAsync(new() { Path = "package-dashboard-insights.jpg", FullPage = true, Quality = 85 });
        await insightsTrigger.ClickAsync();
        await Expect(page.GetByTestId("insights-panel")).ToHaveCountAsync(0);
        await runtimeTrigger.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alertdialog, new() { Name = "Runtime state" })).ToBeVisibleAsync();
        await page.ScreenshotAsync(new() { Path = "package-dashboard-runtime.jpg", FullPage = true, Quality = 85 });
        await HoldDemoFrameAsync(8);
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.GetByRole(AriaRole.Alertdialog, new() { Name = "Runtime state" })).Not.ToBeVisibleAsync();
        await page.ScreenshotAsync(new() { Path = "package-dashboard-authorized.png", FullPage = true });
        await page.SetViewportSizeAsync(390, 844);
        await runtimeTrigger.ClickAsync();
        await Expect(page.GetByLabel("Bearer token", new() { Exact = true })).ToBeVisibleAsync();
        var dialogBounds = await page.GetByRole(AriaRole.Alertdialog, new() { Name = "Runtime state" }).BoundingBoxAsync();
        if (dialogBounds is null || dialogBounds.X < 0 || dialogBounds.Y < 0 || dialogBounds.X + dialogBounds.Width > 390 || dialogBounds.Y + dialogBounds.Height > 844)
            throw new InvalidOperationException("Runtime settings must fit the mobile viewport.");
        await page.ScreenshotAsync(new() { Path = "package-dashboard-runtime-mobile.png", FullPage = true });
        await page.GetByRole(AriaRole.Button, new() { Name = "Close runtime settings", Exact = true }).ClickAsync();
        await Expect(runtimeTrigger).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(page.GetByRole(AriaRole.Alertdialog, new() { Name = "Runtime state" })).Not.ToBeVisibleAsync();
        await page.ScreenshotAsync(new() { Path = "package-dashboard-authorized-mobile.png", FullPage = true });
        await page.ScreenshotAsync(new() { Path = "package-dashboard-mobile-request-detail.png", FullPage = true });
        await HoldDemoFrameAsync(8);
        if (recordingDirectory is not null)
        {
            await browserContext.CloseAsync();
            var videoPath = page.Video is null ? null : await page.Video.PathAsync();
            var markerPath = Path.Combine(recordingDirectory, "demo-scenes-" + Guid.NewGuid().ToString("N") + ".json");
            await File.WriteAllTextAsync(markerPath,
                JsonSerializer.Serialize(new { VideoPath = videoPath, Clock = "Stopwatch started immediately before NewPageAsync", Scenes = recordedScenes },
                    new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        }
    }

    private static async Task AssertDashboardAnalyticsAsync(IPage page,
        IReadOnlyList<ManagedCode.LlmTestServer.Diagnostics.LlmTestServerProviderHttpTrace> requests,
        ManagedCode.LlmTestServer.Diagnostics.LlmTestServerRequestSnapshot evidence, bool conversationScope = false)
    {
        var usage = requests.Where(trace => trace.Usage is not null).Select(trace => trace.Usage!).ToArray();
        var toolResults = requests.SelectMany(trace => trace.Messages).Where(message => message.Role == "tool").ToArray();
        var expected = new Dictionary<string, long>
        {
            ["requests"] = requests.Count,
            ["usage-requests"] = usage.Length,
            ["input"] = usage.Sum(item => (long)item.InputTokens),
            ["output"] = usage.Sum(item => (long)item.OutputTokens),
            ["total"] = usage.Sum(item => (long)item.TotalTokens),
            ["reasoning"] = usage.Sum(item => (long)item.ReasoningTokens),
            ["cache-read"] = usage.Sum(item => (long)item.CachedInputTokens),
            ["cache-write"] = usage.Sum(item => (long)item.CacheCreationInputTokens),
            ["cache-hits"] = usage.Count(item => item.CachedInputTokens > 0),
            ["tool-calls"] = requests.Sum(trace => (long)trace.ResponseToolCalls.Count),
            ["tool-results"] = toolResults.Length,
            ["tool-result-ids"] = toolResults.Select(message => message.ToolCallId).Where(id => id is not null).Distinct(StringComparer.Ordinal).Count(),
        };
        foreach (var metric in expected)
            await Expect(page.GetByTestId("analytics-" + metric.Key)).ToHaveAttributeAsync("data-value", metric.Value.ToString(CultureInfo.InvariantCulture));
        var expectedShare = expected["input"] > 0 ? (double)expected["cache-read"] / expected["input"] : 0;
        var expectedHitRate = expected["usage-requests"] > 0 ? (double)expected["cache-hits"] / expected["usage-requests"] : 0;
        foreach (var (name, value) in new[] { ("cache-share", expectedShare), ("cache-hit-rate", expectedHitRate) })
        {
            var raw = await page.GetByTestId("analytics-" + name).GetAttributeAsync("data-value");
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var actual) || Math.Abs(actual - value) > 0.0000001)
                throw new InvalidOperationException($"Dashboard analytics {name} differs from the observed journal: expected {value}, actual {raw}.");
        }
        await Expect(page.GetByTestId("analytics-retention")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("analytics-retention")).ToHaveAttributeAsync("data-incomplete",
            requests.Any(trace => !trace.IsCompleted || trace.Usage is null) || conversationScope && evidence.EvictedCount > 0 ? "true" : "false");
        var payloadShortened = evidence.RetentionTruncated || requests.Any(trace => trace.RequestPreviewTruncated
            || trace.ResponsePreviewTruncated || trace.RetentionTruncated || trace.RuntimePayloadTruncated);
        await Expect(page.GetByTestId("analytics-payload-retention")).ToHaveCountAsync(payloadShortened ? 1 : 0);
        var weather = page.Locator("[data-testid='analytics-tool-row'][data-tool-name='weather']");
        if (!requests.Any(trace => trace.ResponseToolCalls.Any(call => call.Name == "weather")
            || trace.Messages.Any(message => message.ToolCalls.Any(call => call.Name == "weather"))))
        {
            await Expect(weather).ToHaveCountAsync(0);
            return;
        }
        await Expect(weather.GetByTestId("analytics-tool-name")).ToHaveTextAsync("weather");
        await Expect(weather.GetByTestId("analytics-tool-calls-returned")).ToHaveAttributeAsync("data-value", expected["tool-calls"].ToString(CultureInfo.InvariantCulture));
        await Expect(weather.GetByTestId("analytics-tool-results-observed")).ToHaveAttributeAsync("data-value", expected["tool-results"].ToString(CultureInfo.InvariantCulture));
        await Expect(weather.GetByTestId("analytics-result-ids")).ToHaveAttributeAsync("data-value", expected["tool-result-ids"].ToString(CultureInfo.InvariantCulture));
    }

    private sealed record DashboardDemoEvidence(
        ManagedCode.LlmTestServer.Diagnostics.LlmTestServerRequestSnapshot NamedConversation,
        ManagedCode.LlmTestServer.Diagnostics.LlmTestServerProviderHttpTrace UnlabeledRequest);

    private static async Task<DashboardDemoEvidence> CreateDashboardCacheDemoAsync(
        LlmTestServerClient control, HttpClient http, CancellationToken cancellationToken)
    {
        const string model = "gpt-5-mini";
        var weather = ManagedCode.LlmTestServer.Tools.WellKnown.LlmTestServerWellKnownTools.Weather.Success(
            new() { Location = "Paris", Unit = "celsius" },
            new() { Location = "Paris", Temperature = 18m, Unit = "celsius", Condition = "sunny" },
            "call_dashboard_weather");
        const string finalAnswer = "Paris is 18 °C and sunny. Take the 14:30 train, walk along the Seine, and leave time for a café near the station.";
        await control.ConfigureAsync(config => config.RequireBearerToken("test-key").UseGpt5Mini(reasoningTokens: 23)
            .UseConversation("dashboard-cache", graph => graph.ForModel(model)
                .Step("weather", step => step.WhenLatestUserContains("Paris").WhenTurn(1).CallsTools(weather.Call).Then("itinerary"))
                .Step("itinerary", step => step.WhenToolResult(weather.Call.Name, weather.Call.Id).WhenTurn(2)
                    .WithToolArgumentsJson(weather.Call.ArgumentsJson).WhenLatestJson("$", weather.Result.Content)
                    .Responds(finalAnswer, "Paris is 18 °C and sunny. ", "Take the 14:30 train, walk along the Seine, ",
                        "and leave time for a café near the station.").Complete()))
            .UseChatScenario("unlabeled-dashboard", scenario => scenario.ForModel(model).WhenUserContains("unlabeled fixture")
                .Responds("This independent request has no conversation selector.")), cancellationToken);
        using var conversation = control.ForConversation("dashboard-cache-demo");
        var sdk = new OpenAI.Chat.ChatClient(model, new System.ClientModel.ApiKeyCredential("test-key"),
            new OpenAI.OpenAIClientOptions
            {
                Endpoint = new Uri(conversation.HttpClient.BaseAddress!, "openai/v1"),
                Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(conversation.HttpClient),
            });
        var options = new OpenAI.Chat.ChatCompletionOptions
        {
            Tools = { OpenAI.Chat.ChatTool.CreateFunctionTool(weather.Definition.Name, weather.Definition.Description,
                BinaryData.FromString(weather.Definition.ParametersJson)) },
        };
        var reference = "You are a careful travel assistant. Use only the supplied timetable and weather fixtures, explain practical trade-offs, and preserve Unicode place names.\n"
            + string.Join("\n", Enumerable.Range(1, 100).Select(index =>
                $"Travel reference {index:D3}: Reserve a relaxed walking interval, allow twenty minutes before departure, prefer step-free routes, and keep the supplied afternoon timetable unchanged."));
        List<OpenAI.Chat.ChatMessage> messages =
        [new OpenAI.Chat.SystemChatMessage(reference), new OpenAI.Chat.UserChatMessage("Plan a relaxed afternoon visit to Paris with weather and the train itinerary.")];
        var first = (await sdk.CompleteChatAsync(messages, options, cancellationToken)).Value;
        if (first.ToolCalls.Single().FunctionName != weather.Call.Name || first.ToolCalls.Single().Id != weather.Call.Id
            || first.Usage.InputTokenCount < 1024 || first.Usage.InputTokenDetails.CachedTokenCount != 0)
            throw new InvalidOperationException("The dashboard demo must start with a real SDK tool call and an uncached eligible prompt.");
        messages.Add(new OpenAI.Chat.AssistantChatMessage(first));
        messages.Add(new OpenAI.Chat.ToolChatMessage(weather.Result.ToolCallId, weather.Result.Content));
        var answer = (await sdk.CompleteChatAsync(messages, options, cancellationToken)).Value;
        if (answer.Content[0].Text != finalAnswer || answer.Usage.InputTokenDetails.CachedTokenCount <= 0
            || answer.Usage.OutputTokenDetails.ReasoningTokenCount != 23)
            throw new InvalidOperationException("The second SDK request must complete the tool round with real cached-input and reasoning usage.");
        var streamed = new StringBuilder();
        OpenAI.Chat.ChatTokenUsage? streamingUsage = null;
        await foreach (var update in sdk.CompleteChatStreamingAsync(messages, options, cancellationToken))
        {
            foreach (var part in update.ContentUpdate) streamed.Append(part.Text);
            if (update.Usage is not null) streamingUsage = update.Usage;
        }
        if (streamed.ToString() != finalAnswer || streamingUsage?.InputTokenDetails.CachedTokenCount is not > 0)
            throw new InvalidOperationException("The repeated SDK answer must stream the real cache-hit usage.");
        var independentSdk = new OpenAI.Chat.ChatClient(model, new System.ClientModel.ApiKeyCredential("test-key"),
            new OpenAI.OpenAIClientOptions { Endpoint = new Uri(http.BaseAddress!, "openai/v1"),
                Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(http) });
        var independent = (await independentSdk.CompleteChatAsync([new OpenAI.Chat.UserChatMessage("An unlabeled fixture request")],
            cancellationToken: cancellationToken)).Value;
        if (independent.Content[0].Text != "This independent request has no conversation selector.")
            throw new InvalidOperationException("The unlabeled dashboard fixture did not reach its separate scenario.");
        var evidence = await control.GetRequestsAsync(new()
        { Conversation = conversation.Id, Order = ManagedCode.LlmTestServer.Diagnostics.LlmTestServerRequestOrder.Ascending }, cancellationToken);
        var traces = evidence.Requests;
        if (traces.Count != 3 || evidence.TotalObserved != 4 || evidence.EvictedCount != 0
            || traces.Any(trace => trace.ConversationId != conversation.Id || !trace.ConversationIdWasProvided || !trace.IsCompleted || trace.IsSuccess != true)
            || traces[0].Usage?.CachedInputTokens != 0 || traces[1].Usage?.CachedInputTokens is not > 0 || traces[2].Usage?.CachedInputTokens is not > 0
            || traces[0].ResponseToolCalls.Single().Name != weather.Call.Name || traces[2].TerminalMarkerObserved != true)
            throw new InvalidOperationException("Dashboard cache analytics must use three complete, explicitly correlated provider requests.");
        if (traces[0].Messages.Count != 2 || traces[0].ResponseToolCalls.Single().ArgumentsJson != weather.Call.ArgumentsJson
            || traces.Skip(1).Any(trace => trace.Messages.Count != 4
                || trace.Messages.SelectMany(message => message.ToolCalls).Count() != 1
                || !trace.Messages.SelectMany(message => message.ToolCalls).Any(call => call.Id == weather.Call.Id
                    && call.Name == weather.Call.Name && call.ArgumentsJson == weather.Call.ArgumentsJson)
                || !trace.Messages.Any(message => message.Role == "tool" && message.ToolCallId == weather.Result.ToolCallId
                    && message.Content == weather.Result.Content)))
            throw new InvalidOperationException("Preview shortening must retain the real weather call arguments, result content, and correlation IDs.");
        if (traces[0].Usage!.InputTokens != first.Usage.InputTokenCount
            || traces[1].Usage!.CachedInputTokens != answer.Usage.InputTokenDetails.CachedTokenCount
            || traces[2].Usage!.CachedInputTokens != streamingUsage!.InputTokenDetails.CachedTokenCount)
            throw new InvalidOperationException("Dashboard trace cache accounting differs from the actual SDK usage.");
        var allEvidence = await control.GetRequestsAsync(cancellationToken: cancellationToken);
        var unlabeled = allEvidence.Requests.Single(trace => !trace.ConversationIdWasProvided);
        if (allEvidence.Requests.Count != 4 || unlabeled.ScenarioId != "unlabeled-dashboard")
            throw new InvalidOperationException("Default request grouping must preserve the independent unlabeled request.");
        var summary = await control.GetAssertionsAsync(cancellationToken);
        if (summary.Matched != 4 || summary.InputTokens != allEvidence.Requests.Sum(trace => trace.Usage!.InputTokens)
            || summary.CachedInputTokens != allEvidence.Requests.Sum(trace => trace.Usage!.CachedInputTokens)
            || summary.ReasoningTokens != allEvidence.Requests.Sum(trace => trace.Usage!.ReasoningTokens))
            throw new InvalidOperationException("Default runtime assertions must agree with the dashboard demo's observed request usage.");
        return new(evidence, unlabeled);
    }

    private static async Task VerifyInstalledToolPresetAsync(LlmTestServerClient control, CancellationToken cancellationToken)
    {
        var fixture = ManagedCode.LlmTestServer.Tools.WellKnown.LlmTestServerWellKnownTools.Calculator.Success(
            new() { Expression = "6 * 7" }, new() { Value = 42m }, "call_installed_calculator");
        await using var session = await control.CreateSessionAsync(config => config.RequireBearerToken("test-key").UseGpt41Mini()
            .UseConversation("installed-preset", graph => graph
                .Step("call", step => step.WhenTurn(1).CallsTools(fixture.Call).Then("result"))
                .Step("result", step => step.WhenToolResult(fixture.Call.Name, fixture.Call.Id)
                    .WithToolArgumentsJson(fixture.Call.ArgumentsJson).WhenLatestJson("$", fixture.Result.Content)
                    .Responds("42").Complete())), cancellationToken);
        var sdk = new OpenAI.Chat.ChatClient("gpt-4.1-mini", new System.ClientModel.ApiKeyCredential("test-key"),
            new OpenAI.OpenAIClientOptions
            {
                Endpoint = new Uri(session.HttpClient.BaseAddress!, "openai/v1"),
                Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(session.HttpClient),
            });
        var options = new OpenAI.Chat.ChatCompletionOptions
        {
            Tools = { OpenAI.Chat.ChatTool.CreateFunctionTool(fixture.Definition.Name, fixture.Definition.Description,
                BinaryData.FromString(fixture.Definition.ParametersJson)) },
        };
        List<OpenAI.Chat.ChatMessage> messages = [new OpenAI.Chat.UserChatMessage("calculate")];
        var first = (await sdk.CompleteChatAsync(messages, options, cancellationToken)).Value;
        if (first.ToolCalls.Single().FunctionName != fixture.Call.Name || first.ToolCalls.Single().Id != fixture.Call.Id)
            throw new InvalidOperationException("Installed well-known preset lost its typed function call.");
        messages.Add(new OpenAI.Chat.AssistantChatMessage(first));
        messages.Add(new OpenAI.Chat.ToolChatMessage(fixture.Result.ToolCallId, fixture.Result.Content));
        var answer = (await sdk.CompleteChatAsync(messages, options, cancellationToken)).Value;
        var state = (await session.Control.GetConversationsAsync(cancellationToken)).Single();
        if (answer.Content[0].Text != "42" || !state.IsTerminal || state.ExecutedSteps != 2)
            throw new InvalidOperationException("Installed well-known preset did not complete its real SDK tool round.");
    }

    private static async Task VerifyInstalledConversationGraphAsync(LlmTestServerClient control, int defaultMatchedRequests, CancellationToken cancellationToken)
    {
        await using var session = await control.CreateSessionAsync(config => config.RequireBearerToken("test-key").UseGpt41Mini()
            .UseDeterminism("installed-seed", new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero))
            .UseConversation("package-graph", graph => graph
                .Step("lookup", step => step.WhenLatestUserEquals("weather in Paris").WhenTurn(1)
                    .CallsTool("weather", """{"city":"Paris"}""", "call_package_graph").Then("answer"))
                .Step("answer", step => step.WhenToolResult("weather", "call_package_graph").WhenTurn(2)
                    .WithToolArgumentsJson("""{"city":"Paris"}""").WhenLatestJsonValue("$.temperature", 18)
                    .Responds("Paris is 18 °C", "Paris is ", "18 °C").Complete())), cancellationToken);
        using var conversation = session.Control.ForConversation("installed-sdk-graph");
        var sdk = new OpenAI.Chat.ChatClient("gpt-4.1-mini", new System.ClientModel.ApiKeyCredential("test-key"),
            new OpenAI.OpenAIClientOptions
            {
                Endpoint = new Uri(conversation.HttpClient.BaseAddress!, "openai/v1"),
                Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(conversation.HttpClient),
            });
        var options = new OpenAI.Chat.ChatCompletionOptions
        {
            Tools = { OpenAI.Chat.ChatTool.CreateFunctionTool("weather", functionParameters:
                BinaryData.FromString("""{"type":"object","required":["city"],"properties":{"city":{"const":"Paris"}}}""")) },
        };
        List<OpenAI.Chat.ChatMessage> messages = [new OpenAI.Chat.UserChatMessage("weather in Paris")];
        var first = (await sdk.CompleteChatAsync(messages, options, cancellationToken)).Value;
        if (first.CreatedAt != new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero))
            throw new InvalidOperationException("Installed fixed-clock SDK fixture time was lost.");
        var call = first.ToolCalls.Single();
        if (call.Id != "call_package_graph" || call.FunctionName != "weather"
            || call.FunctionArguments.ToString() != """{"city":"Paris"}""")
            throw new InvalidOperationException("Installed conversation graph lost its SDK tool-call fixture.");
        messages.Add(new OpenAI.Chat.AssistantChatMessage(first));
        messages.Add(new OpenAI.Chat.ToolChatMessage(call.Id, """{"temperature":18}"""));
        var explanation = await conversation.Control.ExplainMatchAsync(new()
        {
            Request = new()
            {
                Messages =
                [
                    new() { Role = "user", Content = "weather in Paris" },
                    new() { Role = "assistant", ToolCalls = [new() { Id = call.Id, Name = "weather", ArgumentsJson = """{"city":"Paris"}""" }] },
                    new() { Role = "tool", ToolCallId = call.Id, Content = """{"temperature":18}""" },
                ],
                Tools = [new() { Name = "weather", ParametersJson = """{"type":"object","required":["city"],"properties":{"city":{"const":"Paris"}}}""" }],
            },
        }, cancellationToken);
        if (explanation.ConversationId != conversation.Id || explanation.SelectedScenarioId != "package-graph"
            || explanation.SelectedStepId != "answer" || explanation.TurnIndex != 2
            || (await conversation.Control.GetConversationsAsync(cancellationToken)).Single().ExecutedSteps != 1)
            throw new InvalidOperationException("Installed match explanation must identify the active step without consuming it.");
        var answer = (await sdk.CompleteChatAsync(messages, options, cancellationToken)).Value;
        if (answer.Content[0].Text != "Paris is 18 °C")
            throw new InvalidOperationException("Installed SDK conversation did not match its JSON tool result.");
        var replay = new StringBuilder();
        await foreach (var update in sdk.CompleteChatStreamingAsync(messages, options, cancellationToken))
            foreach (var part in update.ContentUpdate) replay.Append(part.Text);
        var state = (await conversation.Control.GetConversationsAsync(cancellationToken)).Single();
        var events = (await session.Control.GetAssertionsAsync(cancellationToken)).Events;
        if (replay.ToString() != "Paris is 18 °C" || state.ConversationId != conversation.Id || state.ExecutedSteps != 2
            || !state.IsTerminal || !events.Last().IsReplay || events.Last().StepId != "answer"
            || events.Any(item => item.ConversationId != conversation.Id)
            || (await control.GetAssertionsAsync(cancellationToken)).Matched != defaultMatchedRequests)
            throw new InvalidOperationException("Installed conversation replay must preserve graph state, header scope and default runtime isolation.");
        var evidence = await conversation.Control.GetRequestsAsync(new() { Order = ManagedCode.LlmTestServer.Diagnostics.LlmTestServerRequestOrder.Ascending }, cancellationToken);
        var exported = await conversation.Control.ExportRequestsAsync(cancellationToken: cancellationToken);
        var ndjson = await conversation.Control.ExportRequestsNdjsonAsync(cancellationToken: cancellationToken);
        var verified = await conversation.Control.VerifyAsync(new()
        {
            NoUnexpectedCalls = true,
            Requests =
            [
                new() { Name = "all graph calls", MinimumCount = 3, MaximumCount = 3 },
                new() { Name = "stream completion", Query = new() { Streaming = true }, RequireStreamingTerminalMarker = true },
                new() { Name = "tool fixture", Query = new() { Step = "lookup" }, Tools = [new() { Name = "weather", CallId = "call_package_graph", ArgumentsJson = """{"city":"Paris"}""" }] },
            ],
            Conversations = [new() { ScenarioId = "package-graph", ConversationId = conversation.Id, VisitedSteps = ["lookup", "answer"], RequireTerminal = true }],
        }, cancellationToken);
        if (evidence.Requests.Count != 3 || evidence.EvictedCount != 0 || evidence.RetentionTruncated
            || evidence.Requests.Any(trace => trace.WireClockMode != "fixed") || evidence.Requests[^1].WireSequence != 2
            || exported.SchemaVersion != 1 || exported.Evidence.Requests.Count != 3
            || ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length != 4 || !verified.Passed || !verified.EvidenceComplete)
            throw new InvalidOperationException("Installed request query/export/verification lost complete fixture evidence.");
        await using var retrySession = await control.CreateSessionAsync(config => config.RequireBearerToken("test-key")
            .UseChatScenario("sdk-retry", scenario => scenario.Fails(429, "too_many_requests", "try again").RetryAfter(0).Responds("retried")), cancellationToken);
        retrySession.HttpClient.DefaultRequestHeaders.Add(ManagedCode.LlmTestServer.Diagnostics.LlmTestServerRequestHeaders.RetryGroup, "installed-sdk-retry");
        var retrySdk = new OpenAI.Chat.ChatClient("gpt-4.1-mini", new System.ClientModel.ApiKeyCredential("test-key"),
            new OpenAI.OpenAIClientOptions { Endpoint = new Uri(retrySession.HttpClient.BaseAddress!, "openai/v1"),
                Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(retrySession.HttpClient) });
        var retryAnswer = (await retrySdk.CompleteChatAsync([new OpenAI.Chat.UserChatMessage("retry")], cancellationToken: cancellationToken)).Value;
        var retryEvidence = await retrySession.Control.VerifyAsync(new()
        {
            NoUnexpectedCalls = true,
            Retries = [new() { GroupId = "installed-sdk-retry", MinimumAttempts = 2, MaximumAttempts = 2,
                StatusSequence = [429, 200], MinimumRetryAfterSeconds = 0, RespectRetryAfter = true }],
        }, cancellationToken);
        if (retryAnswer.Content[0].Text != "retried" || !retryEvidence.Passed || !retryEvidence.EvidenceComplete)
            throw new InvalidOperationException("Installed official SDK retries must be proven by actual provider requests.");

    }

}
