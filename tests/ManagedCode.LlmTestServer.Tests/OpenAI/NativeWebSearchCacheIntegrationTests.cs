using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeWebSearchIntegrationTests
{
    [Test]
    [Arguments("context")]
    [Arguments("domains")]
    [Arguments("location")]
    public async Task NativeDeclarationOptions_PartitionActualPromptCachePrefixesAsync(string option)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(Configuration(3));
        var body = Request(input: string.Join(' ', Enumerable.Repeat("stable native search reference", 1200)));
        var initial = await CompleteAsync(http, body);
        await Assert.That(initial.GetProperty("usage").GetProperty("input_tokens").GetInt32()).IsGreaterThanOrEqualTo(1024);
        await Assert.That(initial.GetProperty("usage").GetProperty("input_tokens_details").GetProperty("cached_tokens").GetInt32()).IsEqualTo(0);
        var tool = body["tools"]![0]!;
        switch (option)
        {
            case "context": tool["search_context_size"] = "high"; break;
            case "domains": tool["filters"] = new JsonObject { ["allowed_domains"] = new JsonArray("example.com") }; break;
            case "location": tool["user_location"] = new JsonObject { ["type"] = "approximate", ["country"] = "FR" }; break;
        }
        var changed = await CompleteAsync(http, body);
        await Assert.That(changed.GetProperty("usage").GetProperty("input_tokens_details").GetProperty("cached_tokens").GetInt32()).IsEqualTo(0);
        var repeated = await CompleteAsync(http, body);
        var cached = repeated.GetProperty("usage").GetProperty("input_tokens_details").GetProperty("cached_tokens").GetInt32();
        await Assert.That(cached).IsGreaterThanOrEqualTo(1024);
        var evidence = await control.GetRequestsAsync();
        await Assert.That(evidence.Requests.Count).IsEqualTo(3);
        await Assert.That(evidence.Requests[0].Usage!.CachedInputTokens).IsEqualTo(cached);
    }
    [Test]
    public async Task MixedNativeDefinitions_CacheAccountingStaysWithinRealInputAtTokenBoundaryAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(Configuration(130));
        var prefix = string.Join(' ', Enumerable.Repeat("native cache boundary reference", 400));
        JsonObject? boundaryRequest = null;
        var boundaryInput = 0;
        var observed = 0;
        for (var padding = 0; padding < 128; padding++)
        {
            var body = Request(input: prefix + string.Concat(Enumerable.Repeat(" x", padding)));
            body["tools"]!.AsArray().Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = "cache_boundary",
                ["description"] = "Scripted boundary fixture",
                ["parameters"] = new JsonObject { ["type"] = "object" },
            });
            body["tool_choice"] = "auto";
            var result = await CompleteAsync(http, body);
            observed++;
            var usage = result.GetProperty("usage");
            var input = usage.GetProperty("input_tokens").GetInt32();
            var cached = usage.GetProperty("input_tokens_details").GetProperty("cached_tokens").GetInt32();
            await Assert.That(input).IsGreaterThanOrEqualTo(1024);
            await Assert.That(cached).IsLessThanOrEqualTo(input);
            if (input % 128 == 127) { boundaryRequest = body; boundaryInput = input; break; }
        }
        await Assert.That(boundaryRequest).IsNotNull();
        var repeated = await CompleteAsync(http, boundaryRequest!);
        var repeatedInput = repeated.GetProperty("usage").GetProperty("input_tokens").GetInt32();
        var repeatedCached = repeated.GetProperty("usage").GetProperty("input_tokens_details").GetProperty("cached_tokens").GetInt32();
        await Assert.That(repeatedInput).IsEqualTo(boundaryInput);
        await Assert.That(repeatedCached).IsEqualTo(boundaryInput / 128 * 128);
        var evidence = await control.GetRequestsAsync();
        await Assert.That(evidence.Requests.Count).IsEqualTo(observed + 1);
        await Assert.That(evidence.Requests.All(trace => trace.Usage is { } usage
            && usage.CachedInputTokens + usage.CacheCreationInputTokens <= usage.InputTokens)).IsTrue();
        await Assert.That((double)evidence.Requests[0].Usage!.CachedInputTokens / evidence.Requests[0].Usage!.InputTokens).IsLessThanOrEqualTo(1d);
    }
}
