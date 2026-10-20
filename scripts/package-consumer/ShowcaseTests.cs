using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;
using Microsoft.Playwright;
using TUnit.Core;
using static Microsoft.Playwright.Assertions;

// Drives a realistic multi-provider session through the installed package and captures the showcase images used by the
// README, landing page and launch material. The evidence captures in PackageTests stay independent of this traffic.
public sealed class ShowcaseTests
{
    private const string Conversation = "trip-planner";

    [Test]
    public async Task Showcase_CapturesTokenAnalyticsInsightsAndOverviewFromRichTraffic(CancellationToken cancellationToken)
    {
        var builder = DistributedApplicationTestingBuilder.Create([]);
        builder.AddLlmTestServer("showcase-server").WithApiKey("test-key");
        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("showcase-server", cancellationToken);
        using var http = app.CreateHttpClient("showcase-server", "http");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-key");
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        await PostAsync(http, "/admin-api/configure", Configuration(), cancellationToken);
        await DriveTripPlannerAsync(http, cancellationToken);
        await DriveSideTrafficAsync(http, cancellationToken);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 1040 },
            DeviceScaleFactor = 2,
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(http.BaseAddress!.ToString());
        await page.GetByRole(AriaRole.Button, new() { Name = "Runtime settings", Exact = true }).ClickAsync();
        await page.GetByLabel("Bearer token", new() { Exact = true }).FillAsync("test-key");
        await page.GetByRole(AriaRole.Button, new() { Name = "Close runtime settings", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh runtime state" }).ClickAsync();
        await Expect(page.GetByTestId("request-row")).ToHaveCountAsync(15);
        var tripRows = page.Locator("[data-testid='request-row']", new() { HasTextString = "10 turns" });

        await tripRows.First.ClickAsync();
        await page.GetByTestId("analytics-tab").ClickAsync();
        await page.GetByTestId("analytics-scope-conversation").ClickAsync();
        await Expect(page.GetByTestId("analytics-flow")).ToBeVisibleAsync();
        await page.Locator(".sankey-band").First.HoverAsync();
        await Expect(page.Locator("#viz-tip")).ToBeVisibleAsync();
        await CaptureAsync(page, "package-showcase-token-flow.jpg");

        await page.GetByTestId("insights-open").ClickAsync();
        await Expect(page.GetByTestId("insights-panel")).ToBeVisibleAsync();
        var providerArc = page.Locator("[data-testid='insight-providers'] .donut-arc").First;
        var arcBox = await providerArc.BoundingBoxAsync() ?? throw new InvalidOperationException("Provider donut is not rendered.");
        await providerArc.HoverAsync(new() { Position = new() { X = arcBox.Width / 2 + 4, Y = 4 }, Force = true });
        await Expect(page.Locator("#viz-tip")).ToBeVisibleAsync();
        await CaptureAsync(page, "package-showcase-insights.jpg");

        var toolCallRow = page.Locator("[data-testid='request-row']", new() { HasTextString = "2 turns" })
            .Filter(new() { HasTextString = "gpt-5-mini" }).First;
        await toolCallRow.ClickAsync();
        await Expect(page.GetByTestId("overview-tool-calls")).ToBeVisibleAsync();
        await page.GetByTestId("conversation-strip").First.Locator(".convo-strip-bar > span").First.HoverAsync();
        await Expect(page.Locator("#viz-tip")).ToBeVisibleAsync();
        await CaptureAsync(page, "package-showcase-overview.jpg");

        await tripRows.First.ClickAsync();
        await page.GetByRole(AriaRole.Tab, new() { Name = "Conversation", Exact = false }).ClickAsync();
        await page.Locator("[data-testid='conversation'] .msg-meter").Nth(1).HoverAsync();
        await Expect(page.Locator("#viz-tip")).ToBeVisibleAsync();
        await CaptureAsync(page, "package-showcase-conversation.jpg");
    }

    private static async Task CaptureAsync(IPage page, string path)
    {
        await page.WaitForTimeoutAsync(250);
        await page.ScreenshotAsync(new() { Path = path, Type = ScreenshotType.Jpeg, Quality = 86 });
    }

    private static async Task DriveTripPlannerAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var system = "You are a careful travel assistant for a rail and dining concierge. Use only the supplied tools and fixtures, "
            + "explain practical trade-offs, keep Unicode place names, and never invent timetables.\n"
            + string.Join("\n", Enumerable.Range(1, 60).Select(index =>
                $"Policy {index:D3}: prefer step-free routes, leave twenty minutes before departure, confirm bookings with a reference, and keep answers under 80 words."));
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = system },
            new JsonObject { ["role"] = "user", ["content"] = "Plan a relaxed afternoon trip from Paris to Fontainebleau today, with weather and trains." },
        };
        var results = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["call_weather"] = """{"city":"Paris","temperature":18,"unit":"celsius","condition":"sunny","wind_kph":9}""",
            ["call_trains"] = """{"departures":[{"time":"14:30","duration_min":38,"platform":"L"},{"time":"15:30","duration_min":41}],"return":[{"time":"18:12"},{"time":"19:05"}]}""",
            ["call_table"] = """{"restaurant":"Le Train Bleu","time":"19:00","guests":2,"reference":"TB-4821"}""",
        };
        for (var turn = 0; turn < 5; turn++)
        {
            if (turn == 3)
            {
                messages.Add(new JsonObject { ["role"] = "user", ["content"] = "Great. Book a table for two near the station for when we get back." });
            }

            var body = new JsonObject { ["model"] = "gpt-5-mini", ["messages"] = messages.DeepClone(), ["tools"] = Tools() };
            using var request = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/chat/completions") { Content = JsonContent.Create(body) };
            request.Headers.Add("X-Llm-Test-Server-Conversation", Conversation);
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var message = (await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))!["choices"]![0]!["message"]!.AsObject();
            var assistant = new JsonObject { ["role"] = "assistant", ["content"] = message["content"]?.DeepClone() };
            if (message["tool_calls"] is JsonArray calls)
            {
                assistant["tool_calls"] = calls.DeepClone();
            }

            messages.Add(assistant);
            foreach (var call in (message["tool_calls"] as JsonArray) ?? [])
            {
                var id = call!["id"]!.GetValue<string>();
                messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = results[id] });
            }
        }
    }

    private static async Task DriveSideTrafficAsync(HttpClient http, CancellationToken cancellationToken)
    {
        await PostAsync(http, "/openai/v1/chat/completions", new JsonObject
        {
            ["model"] = "gpt-4.1-mini",
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = "Are the fixtures healthy?" } },
        }, cancellationToken);
        using (var stream = await http.PostAsJsonAsync("/openai/v1/chat/completions", new JsonObject
        {
            ["model"] = "gpt-4.1-mini",
            ["stream"] = true,
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = "Does streaming work?" } },
        }, cancellationToken))
        {
            stream.EnsureSuccessStatusCode();
            _ = await stream.Content.ReadAsStringAsync(cancellationToken);
        }

        var review = new JsonObject
        {
            ["model"] = "claude-sonnet-4-5",
            ["max_tokens"] = 256,
            ["system"] = "You review travel plans for realism. Answer in one sentence.",
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = "Review: 14:30 train, château, 18:12 back, 19:00 dinner." } },
        };
        await PostAsync(http, "/anthropic/v1/messages", review, cancellationToken);
        await PostAsync(http, "/anthropic/v1/messages", review, cancellationToken);
        await PostAsync(http, "/gemini/v1beta/models/gemini-2.5-flash:generateContent", new JsonObject
        {
            ["contents"] = new JsonArray { new JsonObject { ["role"] = "user", ["parts"] = new JsonArray { new JsonObject { ["text"] = "Summarize the Fontainebleau plan in one line." } } } },
        }, cancellationToken);
        await PostAsync(http, "/mistral/v1/chat/completions", new JsonObject
        {
            ["model"] = "mistral-small-latest",
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = "Translate the plan into French." } },
        }, cancellationToken);
        await PostAsync(http, "/groq/openai/v1/chat/completions", new JsonObject
        {
            ["model"] = "llama-3.3-70b-versatile",
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = "Give me a packing list." } },
        }, cancellationToken);
        await PostAsync(http, "/openai/v1/embeddings", new JsonObject { ["model"] = "text-embedding-3-small", ["input"] = "station café near Gare de Lyon" }, cancellationToken);
        (await http.GetAsync("/openai/v1/models", cancellationToken)).EnsureSuccessStatusCode();
        using var missing = await http.PostAsJsonAsync("/openai/v1/chat/completions", new JsonObject
        {
            ["model"] = "gpt-missing",
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = "hello" } },
        }, cancellationToken);
        if (missing.StatusCode != System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("The showcase needs one visible client error.");
    }

    private static async Task PostAsync(HttpClient http, string path, JsonNode body, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(path, body, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{path} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
    }

    private static JsonArray Tools()
    {
        static JsonObject Tool(string name, string description, JsonObject properties, params string[] required) => new()
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray(required.Select(item => (JsonNode)item).ToArray()) },
            },
        };

        return
        [
            Tool("get_weather", "Current weather and short forecast for a city.", new JsonObject { ["city"] = new JsonObject { ["type"] = "string" }, ["unit"] = new JsonObject { ["type"] = "string" } }, "city"),
            Tool("search_trains", "Regional and intercity departures between two stations after a time.", new JsonObject { ["from"] = new JsonObject { ["type"] = "string" }, ["to"] = new JsonObject { ["type"] = "string" }, ["after"] = new JsonObject { ["type"] = "string" } }, "from", "to"),
            Tool("reserve_table", "Reserve a restaurant table near a landmark or station.", new JsonObject { ["area"] = new JsonObject { ["type"] = "string" }, ["time"] = new JsonObject { ["type"] = "string" }, ["guests"] = new JsonObject { ["type"] = "integer" } }, "area", "time", "guests"),
        ];
    }

    private static JsonObject Configuration()
    {
        static JsonObject Call(string id, string name, object arguments) => new()
        {
            ["toolCalls"] = new JsonArray { new JsonObject { ["id"] = id, ["name"] = name, ["argumentsJson"] = JsonSerializer.Serialize(arguments) } },
        };
        static JsonObject Text(string content) => new() { ["content"] = content };

        return new JsonObject
        {
            ["requiredBearerToken"] = "test-key",
            ["models"] = new JsonArray
            {
                new JsonObject { ["id"] = "gpt-5-mini", ["kind"] = "chat", ["reasoningTokens"] = 23 },
                new JsonObject { ["id"] = "gpt-4.1-mini", ["kind"] = "chat" },
                new JsonObject { ["id"] = "claude-sonnet-4-5", ["kind"] = "chat", ["reasoningTokens"] = 12 },
                new JsonObject { ["id"] = "gemini-2.5-flash", ["kind"] = "chat" },
                new JsonObject { ["id"] = "mistral-small-latest", ["kind"] = "chat" },
                new JsonObject { ["id"] = "llama-3.3-70b-versatile", ["kind"] = "chat" },
                new JsonObject { ["id"] = "text-embedding-3-small", ["kind"] = "embedding" },
            },
            ["chatScenarios"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "trip-planner",
                    ["modelId"] = "gpt-5-mini",
                    ["responses"] = new JsonArray
                    {
                        Call("call_weather", "get_weather", new { city = "Paris", unit = "celsius" }),
                        Call("call_trains", "search_trains", new { from = "Paris Gare de Lyon", to = "Fontainebleau", after = "14:00" }),
                        Text("Paris is 18 °C and sunny. Take the 14:30 regional train from Gare de Lyon (38 min), walk the château gardens, and be back on the 18:12 service."),
                        Call("call_table", "reserve_table", new { area = "Gare de Lyon", time = "19:00", guests = 2 }),
                        Text("Booked: a table for two at 19:00 at Le Train Bleu, five minutes from your return platform. Confirmation TB-4821."),
                    },
                },
                new JsonObject { ["id"] = "quick-check", ["modelId"] = "gpt-4.1-mini", ["responses"] = new JsonArray { Text("All fixtures are healthy."), new JsonObject { ["content"] = "Streaming works.", ["streamChunks"] = new JsonArray { "Streaming ", "works." } } } },
                new JsonObject { ["id"] = "claude-review", ["modelId"] = "claude-sonnet-4-5", ["responses"] = new JsonArray { Text("Realistic; add ten minutes for the château queue."), Text("Looks good.") } },
                new JsonObject { ["id"] = "gemini-summary", ["modelId"] = "gemini-2.5-flash", ["responses"] = new JsonArray { Text("Fontainebleau day trip: 14:30 out, 18:12 back, dinner at 19:00.") } },
                new JsonObject { ["id"] = "mistral-translate", ["modelId"] = "mistral-small-latest", ["responses"] = new JsonArray { Text("Départ 14h30, retour 18h12, dîner à 19h00.") } },
                new JsonObject { ["id"] = "groq-packing", ["modelId"] = "llama-3.3-70b-versatile", ["responses"] = new JsonArray { Text("Comfortable shoes, a light jacket, water and your rail pass.") } },
            },
        };
    }
}
