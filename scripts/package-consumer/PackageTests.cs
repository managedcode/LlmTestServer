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
        var resource = builder.AddLlmTestServer("package-tck").WithApiKey("test-key");
        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("package-tck", cancellationToken);
        using var http = app.CreateHttpClient("package-tck", "http");
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
        await control.ConfigureAsync(config => config.RequireBearerToken("test-key").UseGpt41Mini()
            .UseChatScenario("package-conversation", scenario => scenario.WhenUserContains("Paris")
                .Responds("Paris: 18 °C and sunny. The next train leaves at 14:30. Your itinerary is ready.")), cancellationToken);
        var sdk = new OpenAI.Chat.ChatClient("gpt-4.1-mini", new System.ClientModel.ApiKeyCredential("test-key"),
            new OpenAI.OpenAIClientOptions { Endpoint = new Uri(http.BaseAddress!, "openai/v1") });
        List<OpenAI.Chat.ChatMessage> history =
        [
            new OpenAI.Chat.SystemChatMessage(string.Concat(Enumerable.Repeat("Help the traveller plan a deterministic fixture itinerary. Keep supplied facts intact. ", 30))),
            new OpenAI.Chat.UserChatMessage("Plan a visit to Paris."),
            new OpenAI.Chat.AssistantChatMessage("Which day and transport do you prefer?"),
            new OpenAI.Chat.UserChatMessage("Tomorrow by train; include weather and a walking route."),
            new OpenAI.Chat.AssistantChatMessage("I will use the supplied timetable and weather fixtures."),
            new OpenAI.Chat.UserChatMessage("Use the afternoon train and show the final Paris itinerary."),
        ];
        var reply = await sdk.CompleteChatAsync(history, cancellationToken: cancellationToken);
        if (!reply.Value.Content[0].Text.Contains("18 °C", StringComparison.Ordinal))
            throw new InvalidOperationException("The installed client and provider SDK lost the Unicode response fixture.");
        var summary = await control.GetAssertionsAsync(cancellationToken);
        if (summary.Matched != 1 || summary.InputTokens <= 0 || summary.OutputTokens <= 0)
            throw new InvalidOperationException("The installed control client did not observe the SDK request and usage.");
        await using (var session = await control.CreateSessionAsync(config => config.RequireBearerToken("test-key")
            .UseChatScenario("package-session", scenario => scenario.Responds("isolated package response")), cancellationToken))
        {
            using var sessionChat = session.Control.CreateChatClient();
            var sessionReply = await sessionChat.GetResponseAsync(
                [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "session request")], cancellationToken: cancellationToken);
            if (sessionReply.Text != "isolated package response" || (await session.Control.GetAssertionsAsync(cancellationToken)).Matched != 1
                || (await control.GetAssertionsAsync(cancellationToken)).Matched != 1)
                throw new InvalidOperationException("Installed package sessions must isolate requests and assertions from the dashboard runtime.");
        }
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(http.BaseAddress!.ToString());
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
        await page.SetViewportSizeAsync(1440, 1000);
        await Expect(page.GetByRole(AriaRole.Alertdialog, new() { Name = "Runtime state" })).Not.ToBeVisibleAsync();
        var headerBounds = await page.Locator(".topbar").BoundingBoxAsync();
        var workspaceBounds = await page.Locator(".mail-workspace").BoundingBoxAsync();
        if (headerBounds is null || workspaceBounds is null || Math.Abs(workspaceBounds.Y - headerBounds.Y - headerBounds.Height) > 2)
            throw new InvalidOperationException("Closed runtime dialog must not leave empty space above the request workspace.");
        await page.GetByTestId("request-row").First.ClickAsync();
        await Expect(page.GetByTestId("request-details")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new() { Path = "package-dashboard-overview.jpg", FullPage = true, Quality = 85 });
        await page.ScreenshotAsync(new() { Path = "package-dashboard-token-usage.png", FullPage = true });
        await page.GetByRole(AriaRole.Tab, new() { Name = "Conversation", Exact = false }).ClickAsync();
        await page.ScreenshotAsync(new() { Path = "package-dashboard-conversation.jpg", FullPage = true, Quality = 85 });
        await runtimeTrigger.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alertdialog, new() { Name = "Runtime state" })).ToBeVisibleAsync();
        await page.ScreenshotAsync(new() { Path = "package-dashboard-runtime.jpg", FullPage = true, Quality = 85 });
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
    }
}
