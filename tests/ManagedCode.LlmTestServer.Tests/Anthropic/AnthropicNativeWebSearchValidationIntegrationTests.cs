using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Anthropic;

public sealed partial class AnthropicNativeWebSearchIntegrationTests
{
    [Test]
    [Arguments("negativeMax")]
    [Arguments("largeMax")]
    [Arguments("fractionMax")]
    [Arguments("bothDomains")]
    [Arguments("scheme")]
    [Arguments("wildcardHost")]
    [Arguments("queryDomain")]
    [Arguments("emptyLocation")]
    [Arguments("wrongCountry")]
    [Arguments("wrongTimezone")]
    [Arguments("cache_control")]
    [Arguments("defer_loading")]
    [Arguments("strict")]
    [Arguments("allowed_callers")]
    [Arguments("newVersion")]
    [Arguments("wrongName")]
    [Arguments("model")]
    public async Task InvalidNativeDeclaration_Returns400WithoutConsumingFixtureAsync(string defect)
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        await new LlmTestServerClient(http).ConfigureAsync(Configuration());
        var body = Request(); var tool = body["tools"]![0]!;
        switch (defect)
        {
            case "negativeMax": tool["max_uses"] = -1; break;
            case "largeMax": tool["max_uses"] = 129; break;
            case "fractionMax": tool["max_uses"] = 1.5; break;
            case "bothDomains": tool["allowed_domains"] = new JsonArray(); tool["blocked_domains"] = new JsonArray(); break;
            case "scheme": tool["allowed_domains"] = new JsonArray("https://example.com"); break;
            case "wildcardHost": tool["allowed_domains"] = new JsonArray("*.example.com"); break;
            case "queryDomain": tool["allowed_domains"] = new JsonArray("example.com?query=1"); break;
            case "emptyLocation": tool["user_location"] = new JsonObject { ["type"] = "approximate" }; break;
            case "wrongCountry": tool["user_location"] = new JsonObject { ["type"] = "approximate", ["country"] = "ZZ" }; break;
            case "wrongTimezone": tool["user_location"] = new JsonObject { ["type"] = "approximate", ["timezone"] = "Mars/Fixture" }; break;
            case "cache_control": tool[defect] = new JsonObject { ["type"] = "ephemeral" }; break;
            case "defer_loading": case "strict": tool[defect] = true; break;
            case "allowed_callers": tool[defect] = new JsonArray("direct"); break;
            case "newVersion": tool["type"] = "web_search_20260209"; break;
            case "wrongName": tool["name"] = "other"; break;
            case "model": body["model"] = "gpt-4.1-mini"; break;
        }
        using var rejected = await http.PostAsJsonAsync("/anthropic/v1/messages", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertContentAsync((await CompleteAsync(http)).GetProperty("content"));
    }

    [Test]
    [Arguments(false, "missing")]
    [Arguments(false, "none")]
    [Arguments(false, "allowed")]
    [Arguments(false, "blocked")]
    [Arguments(false, "budget")]
    [Arguments(true, "missing")]
    [Arguments(true, "none")]
    [Arguments(true, "allowed")]
    [Arguments(true, "blocked")]
    [Arguments(true, "budget")]
    public async Task FixtureMismatch_DoesNotConsumeQueueOrGraphAsync(bool graph, string mismatch)
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var control = new LlmTestServerClient(http);
        if (graph)
        {
            await control.ConfigureAsync(config => config.UseChatModel(_model).UseConversation("anthropic-native", flow => flow.ForModel(_model)
                .Step("search", step => step.RespondsWithItems(Items()).Complete())));
        }
        else { await control.ConfigureAsync(Configuration()); }
        var body = Request(); var tool = body["tools"]![0]!;
        switch (mismatch)
        {
            case "missing": body["tools"] = new JsonArray(); break;
            case "none": body["tool_choice"] = new JsonObject { ["type"] = "none" }; break;
            case "allowed": tool["allowed_domains"] = new JsonArray("other.example"); break;
            case "blocked": tool["blocked_domains"] = new JsonArray("example.com/source*"); break;
            case "budget": tool["max_uses"] = 0; break;
        }
        using var rejected = await http.PostAsJsonAsync("/anthropic/v1/messages", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That((await control.GetConversationsAsync()).Count).IsEqualTo(0);
        await AssertContentAsync((await CompleteAsync(http)).GetProperty("content"));
        if (graph) { await Assert.That((await control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1); }
    }

    [Test]
    [Arguments("orphan")]
    [Arguments("pending")]
    [Arguments("duplicateResult")]
    [Arguments("wrongPair")]
    [Arguments("invalidError")]
    [Arguments("emptyOpaque")]
    [Arguments("foreignCitation")]
    [Arguments("nullInput")]
    public async Task InvalidConfiguration_IsAtomicAndLeavesOldFixtureUsableAsync(string defect)
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        await new LlmTestServerClient(http).ConfigureAsync(Configuration());
        var config = JsonSerializer.SerializeToNode(Configuration(), _json)!;
        var items = (JsonArray)config["chatScenarios"]![0]!["responses"]![0]!["outputItems"]!;
        switch (defect)
        {
            case "orphan": items.RemoveAt(0); break;
            case "pending": items.RemoveAt(1); break;
            case "duplicateResult": items.Insert(2, items[1]!.DeepClone()); break;
            case "wrongPair": items[1]!["toolUseId"] = "srvtoolu_other"; break;
            case "invalidError": items[1]!["result"] = new JsonObject { ["kind"] = "web_search_error", ["errorCode"] = "invented" }; break;
            case "emptyOpaque": items[1]!["result"]!["results"]![0]!["encryptedContent"] = ""; break;
            case "foreignCitation": items[2]!["citations"]![0]!["url"] = "https://other.example/source"; break;
            case "nullInput": items[0]!["input"] = null; break;
        }
        using var rejected = await http.PostAsJsonAsync("/admin-api/configure", config);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertContentAsync((await CompleteAsync(http)).GetProperty("content"));
    }

    [Test]
    [Arguments("example.com/source")]
    [Arguments("example.com/sour*")]
    [Arguments("example.com")]
    public async Task AllowedDomainPathsAndNullBlockedDomains_ArePreservedAsync(string domain)
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var control = new LlmTestServerClient(http); await control.ConfigureAsync(Configuration());
        var body = Request(); var tool = body["tools"]![0]!;
        tool["allowed_domains"] = new JsonArray(domain, domain); tool["blocked_domains"] = null; tool["max_uses"] = 128;
        tool["user_location"] = new JsonObject { ["type"] = "approximate", ["city"] = "Paris", ["country"] = "FR", ["timezone"] = "Europe/Paris" };
        await CompleteAsync(http, body);
        var options = (await control.GetRequestsAsync()).Requests.Single().NativeTools.Single().AnthropicWebSearchOptions!;
        await Assert.That(options.AllowedDomains!.Count).IsEqualTo(2);
        await Assert.That(options.AllowedDomains[0]).IsEqualTo(domain);
        await Assert.That(options.UserLocation!.City).IsEqualTo("Paris");
    }
    [Test]
    public async Task ForcedNativeChoices_Return400WithoutConsumingPlainTextOrNativeFixturesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseChatModel(_model).UseChatScenario("forced-native", scenario =>
            scenario.ForModel(_model).Responds("queued plain text").RespondsWithItems(Items())));
        foreach (var choice in new[] { new JsonObject { ["type"] = "any" }, new JsonObject { ["type"] = "tool", ["name"] = "web_search" } })
        {
            var forced = Request(); forced["tool_choice"] = choice;
            using var rejected = await http.PostAsJsonAsync("/anthropic/v1/messages", forced);
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        var plain = await CompleteAsync(http);
        await Assert.That(plain.GetProperty("content")[0].GetProperty("text").GetString()).IsEqualTo("queued plain text");
        await AssertContentAsync((await CompleteAsync(http)).GetProperty("content"));
        var traces = (await control.GetRequestsAsync()).Requests;
        await Assert.That(traces.Count).IsEqualTo(4);
        await Assert.That(traces.Count(trace => trace.StatusCode == 400)).IsEqualTo(2);
        await Assert.That(traces.Count(trace => trace.StatusCode == 200)).IsEqualTo(2);
    }

    [Test]
    public async Task MaxUses_RequiresExhaustionAtTheExactOrderedErrorAndCountsOnlyPerformedSearchesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(); using var http = host.GetTestClient(); Headers(http);
        var first = Items(); var second = Items(); var exhausted = Items("max_uses_exceeded");
        second[0] = ((LlmTestServerResponseServerToolUseItem)second[0]) with { Id = "srvtoolu_second" };
        second[1] = ((LlmTestServerResponseServerToolResultItem)second[1]) with { ToolUseId = "srvtoolu_second" };
        exhausted[0] = ((LlmTestServerResponseServerToolUseItem)exhausted[0]) with { Id = "srvtoolu_exhausted" };
        exhausted[1] = ((LlmTestServerResponseServerToolResultItem)exhausted[1]) with { ToolUseId = "srvtoolu_exhausted" };
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseChatModel(_model).UseChatScenario("ordered-max-uses", scenario => scenario.ForModel(_model)
            .RespondsWithItems(first[0], first[1], second[0], second[1], exhausted[0], exhausted[1], first[2])));
        foreach (var invalidBudget in new[] { 1, 3 })
        {
            var invalid = Request(); invalid["tools"]![0]!["max_uses"] = invalidBudget;
            using var rejected = await http.PostAsJsonAsync("/anthropic/v1/messages", invalid);
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        }
        var body = Request(); body["tools"]![0]!["max_uses"] = 2;
        var response = await CompleteAsync(http, body);
        await Assert.That(response.GetProperty("content").GetArrayLength()).IsEqualTo(7);
        await Assert.That(response.GetProperty("content")[5].GetProperty("content").GetProperty("error_code").GetString()).IsEqualTo("max_uses_exceeded");
        await Assert.That(response.GetProperty("usage").GetProperty("server_tool_use").GetProperty("web_search_requests").GetInt32()).IsEqualTo(2);
    }

}
