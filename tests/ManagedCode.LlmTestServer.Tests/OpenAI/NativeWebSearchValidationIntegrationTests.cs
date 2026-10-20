using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeWebSearchIntegrationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task MissingOrDisabledNativeDeclaration_DoesNotConsumeQueueOrGraphAsync(bool graph, bool none)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        if (graph)
        {
            await control.ConfigureAsync(config => config.UseGpt41Mini().UseConversation("native-mismatch", flow => flow
                .Step("search", step => step.RespondsWithItems(Items()).Complete())));
        }
        else { await control.ConfigureAsync(Configuration()); }
        var invalid = Request();
        invalid["tool_choice"] = none ? "none" : "auto";
        if (!none) { invalid["tools"] = new JsonArray(); }
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", invalid);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var failure = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(failure.GetProperty("error").GetProperty("code").GetString()).IsEqualTo("llm_test_server_fixture_mismatch");
        await Assert.That((await control.GetConversationsAsync()).Count).IsEqualTo(0);
        await AssertWireAsync(await CompleteAsync(http));
        if (graph) { await Assert.That((await control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(1); }
    }

    [Test]
    [Arguments("/azure-openai/openai/v1/responses")]
    [Arguments("/microsoft-foundry/openai/v1/responses")]
    [Arguments("/groq/openai/v1/responses")]
    [Arguments("/openrouter/api/v1/responses")]
    [Arguments("/openai/v1/chat/completions")]
    public async Task NativeDeclaration_StaysWithinOpenAiResponsesNamespaceAsync(string path)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration());
        var body = Request();
        if (path.EndsWith("chat/completions", StringComparison.Ordinal))
        {
            body.Remove("input"); body.Remove("include");
            body["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "start" });
        }
        using var rejected = await http.PostAsJsonAsync(path, body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertWireAsync(await CompleteAsync(http));
    }

    [Test]
    [Arguments("{\"type\":\"web_search\",\"search_context_size\":\"huge\"}")]
    [Arguments("{\"type\":\"web_search\",\"search_context_size\":null}")]
    [Arguments("{\"type\":\"web_search\",\"external_web_access\":true}")]
    [Arguments("{\"type\":\"web_search\",\"return_token_budget\":\"unlimited\"}")]
    [Arguments("{\"type\":\"web_search\",\"user_location\":{\"type\":\"exact\",\"city\":\"Paris\"}}")]
    [Arguments("{\"type\":\"web_search\",\"user_location\":{\"type\":\"approximate\",\"country\":\"ZZ\"}}")]
    [Arguments("{\"type\":\"web_search\",\"user_location\":{\"type\":\"approximate\",\"timezone\":\"Invalid/Fixture\"}}")]
    [Arguments("{\"type\":\"web_search\",\"filters\":{\"allowed_domains\":[\"https://example.com\"]}}")]
    [Arguments("{\"type\":\"web_search\",\"filters\":{\"allowed_domains\":[\"*.example.com\"]}}")]
    [Arguments("{\"type\":\"web_search\",\"filters\":{\"allowed_domains\":[\"127.0.0.1\"]}}")]
    [Arguments("{\"type\":\"web_search\",\"filters\":{\"allowed_domains\":[\"example.com/path\"]}}")]
    [Arguments("{\"type\":\"web_search\",\"filters\":{\"blocked_domains\":[\"example.com\"]}}")]
    public async Task InvalidNativeOptions_Return400AndLeaveFixtureAvailableAsync(string declaration)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration());
        var body = Request(); body["tools"] = new JsonArray(JsonNode.Parse(declaration));
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertWireAsync(await CompleteAsync(http));
    }

    [Test]
    [Arguments("foreignInclude")]
    [Arguments("missingNamedDeclaration")]
    [Arguments("unsupportedModel")]
    [Arguments("tooManyDomains")]
    public async Task InvalidNativeRequestBoundaries_DoNotConsumeFixtureAsync(string defect)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration());
        var body = Request();
        switch (defect)
        {
            case "foreignInclude": body["include"] = new JsonArray("file_search_call.unsupported"); break;
            case "missingNamedDeclaration": body["tools"] = new JsonArray(); break;
            case "unsupportedModel": body["model"] = "gpt-5-mini"; break;
            case "tooManyDomains":
                body["tools"]![0]!["filters"] = new JsonObject
                { ["allowed_domains"] = new JsonArray(Enumerable.Range(0, 101).Select(index => JsonValue.Create($"d{index}.example.com")).ToArray()) }; break;
        }
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertWireAsync(await CompleteAsync(http));
    }

    [Test]
    [Arguments("file_search_call.results")]
    [Arguments("code_interpreter_call.outputs")]
    public async Task SupportedUnusedIncludes_DoNotInventOtherNativeOutputAsync(string include)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration());
        var body = Request();
        body["include"] = new JsonArray(include);
        var payload = await CompleteAsync(http, body);
        await AssertWireAsync(payload, sources: false);
        await Assert.That(payload.GetProperty("output").GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task RepeatedNativeDeclaration_RejectsUnsupportedFixtureWithoutConsumptionAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration());
        var body = Request();
        body["tools"]!.AsArray().Add(new JsonObject { ["type"] = "web_search" });
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That((await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString()).IsEqualTo("llm_test_server_fixture_mismatch");
        await AssertWireAsync(await CompleteAsync(http));
    }

    [Test]
    public async Task DuplicateNativeField_IsRejectedBeforeDeserializationAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration());
        using var body = new StringContent("""
            {"model":"gpt-4.1-mini","input":"start","tools":[{"type":"web_search","search_context_size":"low","search_context_size":"high"}]}
            """, Encoding.UTF8, "application/json");
        using var rejected = await http.PostAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertWireAsync(await CompleteAsync(http));
    }

    [Test]
    [Arguments("{\"type\":\"web_search\",\"user_location\":null}")]
    [Arguments("{\"type\":\"web_search\",\"user_location\":{}}")]
    [Arguments("{\"type\":\"web_search\",\"user_location\":{\"type\":\"approximate\"}}")]
    [Arguments("{\"type\":\"web_search\",\"user_location\":{\"country\":\"fr\",\"city\":\"\",\"region\":\"\"}}")]
    [Arguments("{\"type\":\"web_search\",\"user_location\":{\"country\":null,\"city\":null,\"region\":null,\"timezone\":null}}")]
    [Arguments("{\"type\":\"web_search\",\"filters\":null}")]
    [Arguments("{\"type\":\"web_search\",\"filters\":{}}")]
    [Arguments("{\"type\":\"web_search\",\"filters\":{\"allowed_domains\":null}}")]
    [Arguments("{\"type\":\"web_search\",\"filters\":{\"allowed_domains\":[]}}")]
    [Arguments("{\"type\":\"web_search\",\"filters\":{\"allowed_domains\":[\"example.com\",\"EXAMPLE.COM\"]}}")]
    public async Task OptionalNativeValues_FollowProviderSchemaAsync(string declaration)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration());
        var body = Request();
        body["tools"] = new JsonArray(JsonNode.Parse(declaration));
        body["include"]!.AsArray().Add("web_search_call.action.sources");
        await AssertWireAsync(await CompleteAsync(http, body));
    }

    [Test]
    public async Task AllowedDomainUpperBoundary_IsAcceptedAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration());
        var body = Request();
        var domains = new JsonArray("example.com");
        for (var index = 1; index < 100; index++) { domains.Add($"d{index}.example.com"); }
        body["tools"]![0]!["filters"] = new JsonObject { ["allowed_domains"] = domains };
        await AssertWireAsync(await CompleteAsync(http, body));
    }
}
