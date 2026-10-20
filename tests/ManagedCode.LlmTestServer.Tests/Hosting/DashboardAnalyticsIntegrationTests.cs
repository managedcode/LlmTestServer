using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Conversations;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Hosting;

public sealed class DashboardAnalyticsIntegrationTests
{
    [Test]
    public async Task Analytics_ObservesCacheMissAndHitWithExplicitConversationCorrelationAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddReasoningChatModel(LlmTestServerKnownModelIds.Gpt5Mini, 17).AddChatScenario("analytics-cache", scenario => scenario
            .ForModel(LlmTestServerKnownModelIds.Gpt5Mini)
            .Responds("first answer").Responds("second answer").Responds("uncorrelated answer")));
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var body = new
        {
            model = LlmTestServerKnownModelIds.Gpt5Mini,
            messages = new[]
            {
                new { role = "system", content = string.Join(' ', Enumerable.Repeat("stable analytical prefix", 1200)) },
                new { role = "user", content = "explain the fixture" },
            },
        };
        http.DefaultRequestHeaders.Add(LlmTestServerConversationHeaders.Id, "analytics-cache-conversation");
        using var firstResponse = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        firstResponse.EnsureSuccessStatusCode();
        using var secondResponse = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        secondResponse.EnsureSuccessStatusCode();
        var wireUsage = (await secondResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("usage");
        var snapshot = await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        var first = snapshot.Requests[0];
        var second = snapshot.Requests[1];
        await Assert.That(snapshot.Requests.Count).IsEqualTo(2);
        await Assert.That(first.ConversationIdWasProvided).IsTrue();
        await Assert.That(second.ConversationIdWasProvided).IsTrue();
        await Assert.That(second.ConversationId).IsEqualTo("analytics-cache-conversation");
        await Assert.That(first.Usage).IsNotNull();
        await Assert.That(second.Usage).IsNotNull();
        await Assert.That(first.Usage!.CachedInputTokens).IsEqualTo(0);
        await Assert.That(first.Usage.CacheCreationInputTokens).IsGreaterThanOrEqualTo(1024);
        await Assert.That(second.Usage!.CachedInputTokens).IsGreaterThanOrEqualTo(1024);
        await Assert.That(second.Usage.CacheCreationInputTokens).IsEqualTo(0);
        await Assert.That(second.Usage.InputTokens).IsEqualTo(wireUsage.GetProperty("prompt_tokens").GetInt32());
        await Assert.That(second.Usage.CachedInputTokens).IsEqualTo(wireUsage.GetProperty("prompt_tokens_details").GetProperty("cached_tokens").GetInt32());
        await Assert.That(second.Usage.InputTokens >= second.Usage.CachedInputTokens + second.Usage.CacheCreationInputTokens).IsTrue();
        await Assert.That(second.Usage.ReasoningTokens).IsEqualTo(17);
        await Assert.That(second.Usage.OutputTokens).IsEqualTo(wireUsage.GetProperty("completion_tokens").GetInt32());
        await Assert.That(second.Usage.OutputTokens > second.Usage.ReasoningTokens).IsTrue();
        await Assert.That(second.Usage.TotalTokens).IsEqualTo(second.Usage.InputTokens + second.Usage.OutputTokens);
        await Assert.That(first.ResponseToolCalls).IsEmpty();
        await Assert.That(second.ResponseToolCalls).IsEmpty();
        var page = await http.GetStringAsync("/");
        await Assert.That(page).Contains(second.Usage.TotalTokens.ToString("N0", CultureInfo.InvariantCulture));
        await Assert.That(page).Contains(second.Usage.CachedInputTokens.ToString("N0", CultureInfo.InvariantCulture));
        await Assert.That(page).Contains("analytics-cache-conversation");
        await AssertMetricAsync(page, "input", second.Usage.InputTokens);
        await AssertMetricAsync(page, "output", second.Usage.OutputTokens);
        await AssertMetricAsync(page, "total", second.Usage.TotalTokens);
        await AssertMetricAsync(page, "reasoning", 17);
        await AssertMetricAsync(page, "cache-read", second.Usage.CachedInputTokens);
        await AssertMetricAsync(page, "cache-write", 0);
        await AssertMetricAsync(page, "requests", 1);
        await AssertMetricAsync(page, "tool-calls", 0);
        await AssertMetricAsync(page, "tool-results", 0);

        http.DefaultRequestHeaders.Remove(LlmTestServerConversationHeaders.Id);
        using var uncorrelated = await http.PostAsJsonAsync("/openai/v1/chat/completions", body);
        uncorrelated.EnsureSuccessStatusCode();
        var last = (await control.GetRequestsAsync()).Requests[0];
        await Assert.That(last.ConversationIdWasProvided).IsFalse();
    }

    [Test]
    public async Task Analytics_DistinguishesReturnedToolCallsFromObservedToolResultsAsync()
    {
        var call = new LlmTestServerToolCall { Id = "call_analytics", Name = "read_fixture", ArgumentsJson = "{}" };
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddConversation("analytics-tools", graph => graph
            .Step("call", step => step.WhenLatestUserEquals("read fixture").CallsTools(call).Then("result"))
            .Step("result", step => step.WhenToolResult(call.Name, call.Id).Responds("fixture read").Complete())));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add(LlmTestServerConversationHeaders.Id, "analytics-tool-conversation");
        var tools = new[] { new { type = "function", function = new { name = call.Name, parameters = new { type = "object" } } } };
        using var first = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
        {
            model = LlmTestServerKnownModelIds.Gpt41Mini,
            messages = new[] { new { role = "user", content = "read fixture" } },
            tools,
        });
        first.EnsureSuccessStatusCode();
        var assistant = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("choices")[0].GetProperty("message");
        using var second = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
        {
            model = LlmTestServerKnownModelIds.Gpt41Mini,
            messages = new object[]
            {
                new { role = "user", content = "read fixture" },
                assistant,
                new { role = "tool", tool_call_id = call.Id, content = "fixture contents" },
            },
            tools,
        });
        second.EnsureSuccessStatusCode();
        var snapshot = await new LlmTestServerClient(http).GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(snapshot.Requests.Count).IsEqualTo(2);
        await Assert.That(snapshot.Requests[0].ResponseToolCalls.Single().Id).IsEqualTo(call.Id);
        await Assert.That(snapshot.Requests[0].Messages.Count(message => message.ToolCallId is not null)).IsEqualTo(0);
        await Assert.That(snapshot.Requests[1].ResponseToolCalls).IsEmpty();
        var result = snapshot.Requests[1].Messages.Single(message => message.ToolCallId is not null);
        await Assert.That(result.ToolCallId).IsEqualTo(call.Id);
        await Assert.That(result.Content).IsEqualTo("fixture contents");
        await Assert.That(snapshot.Requests[1].Messages.SelectMany(message => message.ToolCalls).Single().Id).IsEqualTo(call.Id);
        await Assert.That(snapshot.Requests.All(trace => trace.ConversationIdWasProvided)).IsTrue();
        await Assert.That(snapshot.Requests.Select(trace => trace.ConversationId).Distinct().Single()).IsEqualTo("analytics-tool-conversation");
        await Assert.That(snapshot.Requests.Sum(trace => trace.ResponseToolCalls.Count)).IsEqualTo(1);
        await Assert.That(snapshot.Requests.Sum(trace => trace.Messages.Count(message => message.ToolCallId is not null))).IsEqualTo(1);
        var page = await http.GetStringAsync("/");
        await AssertMetricAsync(page, "tool-calls", 0);
        await AssertMetricAsync(page, "tool-results", 1);
        await AssertMetricAsync(page, "tool-result-ids", 1);
    }

    [Test]
    public async Task Analytics_ModelDiscoveryKeepsUnknownUsageVisibleAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add(LlmTestServerConversationHeaders.Id, "analytics-discovery");
        using var response = await http.GetAsync("/openai/v1/models");
        response.EnsureSuccessStatusCode();
        var models = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(models.GetProperty("data").GetArrayLength()).IsGreaterThan(0);
        var trace = (await new LlmTestServerClient(http).GetRequestsAsync()).Requests.Single();
        await Assert.That(trace.ConversationIdWasProvided).IsTrue();
        await Assert.That(trace.ConversationId).IsEqualTo("analytics-discovery");
        await Assert.That(trace.Usage).IsNull();
        var page = await http.GetStringAsync("/");
        await AssertMetricAsync(page, "requests", 1);
        await AssertMetricAsync(page, "usage-requests", 0);
        foreach (var metric in new[] { "input", "output", "total", "reasoning", "cache-read", "cache-write", "cache-share", "cache-hit-rate" })
        {
            var element = Regex.Match(WebUtility.HtmlDecode(page), "<[^>]*data-testid=\"analytics-" + metric + "\"[^>]*>[^<]*</span>").Value;
            await Assert.That(element).Contains("data-value=\"\"");
            await Assert.That(element.Contains('—') || element.Contains("N/A", StringComparison.Ordinal)).IsTrue();
        }
        var retention = Regex.Match(page, "<[^>]*data-testid=\"analytics-retention\"[^>]*>").Value;
        await Assert.That(retention).Contains("data-incomplete=\"true\"");
        await Assert.That(page).Contains("data-testid=\"analytics-flow-empty\"");
        await Assert.That(page).DoesNotContain("data-testid=\"analytics-flow\"");
    }

    [Test]
    public async Task Analytics_ItemizesOpenAiInputByRoleAndReconcilesWithWireUsageAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddChatScenario("analytics-mix", scenario => scenario
            .ForModel(LlmTestServerKnownModelIds.Gpt41Mini).Responds("Take the 14:30 train.")));
        using var http = host.GetTestClient();
        using var response = await http.PostAsJsonAsync("/openai/v1/chat/completions", new
        {
            model = LlmTestServerKnownModelIds.Gpt41Mini,
            messages = new object[]
            {
                new { role = "system", content = "You are a rail concierge. Use only the supplied tools and never invent timetables." },
                new { role = "user", content = "Find an afternoon train from Paris to Fontainebleau." },
                new
                {
                    role = "assistant",
                    content = "Checking departures.",
                    tool_calls = new[] { new { id = "call_trains", type = "function", function = new { name = "search_trains", arguments = "{\"from\":\"Paris Gare de Lyon\",\"to\":\"Fontainebleau\"}" } } },
                },
                new { role = "tool", tool_call_id = "call_trains", content = "{\"departures\":[{\"time\":\"14:30\",\"platform\":\"L\"}]}" },
                new { role = "user", content = "Which one should I take?" },
            },
            tools = new[]
            {
                new { type = "function", function = new { name = "search_trains", description = "Search departures between two stations.", parameters = new { type = "object", properties = new { from = new { type = "string" }, to = new { type = "string" } } } } },
            },
        });
        response.EnsureSuccessStatusCode();
        var promptTokens = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("usage").GetProperty("prompt_tokens").GetInt32();
        var usage = (await new LlmTestServerClient(http).GetRequestsAsync()).Requests.Single().Usage!;
        var composition = usage.InputComposition!;
        await AssertItemizedAsync(composition, usage.InputTokens, promptTokens);

        var page = await http.GetStringAsync("/");
        await AssertMetricAsync(page, "mix-system", composition.SystemTokens);
        await AssertMetricAsync(page, "mix-user", composition.UserTokens);
        await AssertMetricAsync(page, "mix-assistant", composition.AssistantTokens);
        await AssertMetricAsync(page, "mix-tool-calls", composition.ToolCallTokens);
        await AssertMetricAsync(page, "mix-tool-results", composition.ToolResultTokens);
        await AssertMetricAsync(page, "mix-tool-definitions", composition.ToolDefinitionTokens);
        await Assert.That(page).DoesNotContain("data-testid=\"analytics-mix-not-itemized\"");
        foreach (var key in new[] { "system", "tool-definitions", "user", "assistant", "tool-calls", "tool-results", "output" })
        {
            await Assert.That(page).Contains("data-testid=\"analytics-flow-source\" data-key=\"" + key + "\"");
        }
        foreach (var key in new[] { "uncached", "response" })
        {
            await Assert.That(page).Contains("data-testid=\"analytics-flow-target\" data-key=\"" + key + "\"");
        }
    }

    [Test]
    public async Task Analytics_ItemizesAnthropicSystemToolUseAndToolResultBlocksAsync()
    {
        const string model = "claude-sonnet-4-5";
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddModel(model, LlmTestServerModelKind.Chat)
            .AddChatScenario("analytics-anthropic-mix", scenario => scenario.ForModel(model).Responds("Paris is 18 °C and sunny.")));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        using var response = await http.PostAsJsonAsync("/anthropic/v1/messages", new
        {
            model,
            max_tokens = 256,
            system = "You are a weather assistant. Answer in one sentence.",
            tools = new[] { new { name = "get_weather", description = "Current weather for a city.", input_schema = new { type = "object", properties = new { city = new { type = "string" } } } } },
            messages = new object[]
            {
                new { role = "user", content = "What is the weather in Paris?" },
                new { role = "assistant", content = new object[] { new { type = "text", text = "Let me check." }, new { type = "tool_use", id = "toolu_weather", name = "get_weather", input = new { city = "Paris" } } } },
                new { role = "user", content = new object[] { new { type = "tool_result", tool_use_id = "toolu_weather", content = "18 °C, sunny" } } },
            },
        });
        response.EnsureSuccessStatusCode();
        var wireInput = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("usage").GetProperty("input_tokens").GetInt32();
        var usage = (await new LlmTestServerClient(http).GetRequestsAsync()).Requests.Single().Usage!;
        await Assert.That(usage.CachedInputTokens + usage.CacheCreationInputTokens).IsEqualTo(0);
        await AssertItemizedAsync(usage.InputComposition!, usage.InputTokens, wireInput);
        var page = await http.GetStringAsync("/");
        await AssertMetricAsync(page, "mix-system", usage.InputComposition!.SystemTokens);
        await AssertMetricAsync(page, "mix-tool-results", usage.InputComposition.ToolResultTokens);
    }

    [Test]
    public async Task Insights_ChartRetainedTrafficByProviderModelOutcomeDeliveryAndContextAsync()
    {
        const string claude = "claude-sonnet-4-5";
        using var host = await LlmTestServerTestHost.StartAsync(config => config.AddModel(claude, LlmTestServerModelKind.Chat)
            .AddChatScenario("insights-openai", scenario => scenario.ForModel(LlmTestServerKnownModelIds.Gpt41Mini).Responds("one").Responds("two"))
            .AddChatScenario("insights-anthropic", scenario => scenario.ForModel(claude).Responds("three")));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var openAiMessages = new object[]
        {
            new { role = "system", content = "You answer in one word." },
            new { role = "user", content = "Say one." },
        };
        using (var single = await http.PostAsJsonAsync("/openai/v1/chat/completions", new { model = LlmTestServerKnownModelIds.Gpt41Mini, messages = openAiMessages }))
        {
            single.EnsureSuccessStatusCode();
        }
        using (var streamed = await http.PostAsJsonAsync("/openai/v1/chat/completions", new { model = LlmTestServerKnownModelIds.Gpt41Mini, stream = true, messages = openAiMessages }))
        {
            streamed.EnsureSuccessStatusCode();
            await Assert.That(await streamed.Content.ReadAsStringAsync()).Contains("[DONE]");
        }
        using (var anthropic = await http.PostAsJsonAsync("/anthropic/v1/messages", new
        {
            model = claude,
            max_tokens = 64,
            system = "You answer in one word.",
            messages = new[] { new { role = "user", content = "Say three." } },
        }))
        {
            anthropic.EnsureSuccessStatusCode();
        }
        using (var missing = await http.PostAsJsonAsync("/openai/v1/chat/completions", new { model = "gpt-missing", messages = openAiMessages }))
        {
            await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }

        var journal = (await new LlmTestServerClient(http).GetRequestsAsync()).Requests;
        await Assert.That(journal.Count).IsEqualTo(4);
        var page = await http.GetStringAsync("/?view=insights");
        await Assert.That(page).Contains("data-testid=\"insights-panel\"");
        foreach (var provider in journal.GroupBy(trace => trace.ProviderId))
        {
            await AssertInsightAsync(page, "providers", provider.Key, provider.Count());
        }
        foreach (var model in journal.GroupBy(trace => trace.ModelId!))
        {
            await AssertInsightAsync(page, "models", model.Key, model.Count());
        }
        await AssertInsightAsync(page, "outcomes", "success", journal.Count(trace => trace.StatusCode < 400));
        await AssertInsightAsync(page, "outcomes", "client-error", journal.Count(trace => trace.StatusCode is >= 400 and < 500));
        await AssertInsightAsync(page, "delivery", "stream", journal.Count(trace => trace.IsStreaming));
        await AssertInsightAsync(page, "delivery", "single", journal.Count(trace => !trace.IsStreaming));
        var itemized = journal.Select(trace => trace.Usage).Where(usage => usage?.InputComposition?.TotalTokens == usage?.InputTokens && usage is not null)
            .Select(usage => usage!.InputComposition!).ToArray();
        await Assert.That(itemized.Length).IsGreaterThanOrEqualTo(3);
        await AssertInsightAsync(page, "context-mix", "system", itemized.Sum(composition => composition.SystemTokens));
        await AssertInsightAsync(page, "context-mix", "user", itemized.Sum(composition => composition.UserTokens));
        await Assert.That(await http.GetStringAsync("/")).DoesNotContain("data-testid=\"insights-panel\"");
    }

    private static async Task AssertInsightAsync(string page, string chart, string key, int expected)
    {
        var tag = Regex.Match(page, "<[^>]*data-testid=\"insight-" + chart + "-value\" data-key=\"" + Regex.Escape(WebUtility.HtmlEncode(key)) + "\"[^>]*>").Value;
        await Assert.That(tag).Contains("data-value=\"" + expected.ToString(CultureInfo.InvariantCulture) + "\"");
    }

    private static async Task AssertItemizedAsync(LlmTestServerInputTokenComposition composition, int inputTokens, int wireInputTokens)
    {
        await Assert.That(composition.SystemTokens).IsGreaterThan(0);
        await Assert.That(composition.UserTokens).IsGreaterThan(0);
        await Assert.That(composition.AssistantTokens).IsGreaterThan(0);
        await Assert.That(composition.ToolCallTokens).IsGreaterThan(0);
        await Assert.That(composition.ToolResultTokens).IsGreaterThan(0);
        await Assert.That(composition.ToolDefinitionTokens).IsGreaterThan(0);
        await Assert.That(composition.OtherTokens).IsEqualTo(0);
        await Assert.That(composition.TotalTokens).IsEqualTo(inputTokens);
        await Assert.That(inputTokens).IsEqualTo(wireInputTokens);
    }

    private static async Task AssertMetricAsync(string page, string metric, int expected)
    {
        var tag = Regex.Match(page, "<[^>]*data-testid=\"analytics-" + metric + "\"[^>]*>").Value;
        await Assert.That(tag).Contains("data-value=\"" + expected.ToString(CultureInfo.InvariantCulture) + "\"");
    }
}
