using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeFileCodeIntegrationTests
{
    [Test]
    [Arguments(false, """{"type":"file_search"}""")]
    [Arguments(false, """{"type":"file_search","vector_store_ids":null}""")]
    [Arguments(false, """{"type":"file_search","vector_store_ids":[""]}""")]
    [Arguments(false, """{"type":"file_search","vector_store_ids":["vs_fixture"],"max_num_results":0}""")]
    [Arguments(false, """{"type":"file_search","vector_store_ids":["vs_fixture"],"max_num_results":51}""")]
    [Arguments(false, """{"type":"file_search","vector_store_ids":["vs_fixture"],"max_num_results":1.5}""")]
    [Arguments(false, """{"type":"file_search","vector_store_ids":["vs_fixture"],"max_num_results":null}""")]
    [Arguments(false, """{"type":"file_search","vector_store_ids":["vs_fixture"],"ranking_options":null}""")]
    [Arguments(false, """{"type":"file_search","vector_store_ids":["vs_fixture"],"ranking_options":{"ranker":"unknown"}}""")]
    [Arguments(false, """{"type":"file_search","vector_store_ids":["vs_fixture"],"ranking_options":{"ranker":null}}""")]
    [Arguments(false, """{"type":"file_search","vector_store_ids":["vs_fixture"],"ranking_options":{"score_threshold":-0.1}}""")]
    [Arguments(false, """{"type":"file_search","vector_store_ids":["vs_fixture"],"ranking_options":{"score_threshold":1.1}}""")]
    [Arguments(false, """{"type":"file_search","vector_store_ids":["vs_fixture"],"ranking_options":{"score_threshold":null}}""")]
    [Arguments(false, """{"type":"file_search","vector_store_ids":["vs_fixture"],"ranking_options":{"hybrid_search":{"embedding_weight":1}}}""")]
    [Arguments(true, """{"type":"code_interpreter"}""")]
    [Arguments(true, """{"type":"code_interpreter","container":null}""")]
    [Arguments(true, """{"type":"code_interpreter","container":""}""")]
    [Arguments(true, """{"type":"code_interpreter","container":{"type":"other"}}""")]
    [Arguments(true, """{"type":"code_interpreter","container":{"type":"auto","file_ids":null}}""")]
    [Arguments(true, """{"type":"code_interpreter","container":{"type":"auto","file_ids":[""]}}""")]
    [Arguments(true, """{"type":"code_interpreter","container":{"type":"auto","memory_limit":"1g"}}""")]
    [Arguments(true, """{"type":"code_interpreter","container":{"type":"auto","network_policy":{"allow":[]}}}""")]
    [Arguments(true, """{"type":"code_interpreter","container":"cntr_fixture","allowed_callers":[]}""")]
    public async Task InvalidNativeDeclaration_Returns400BeforeFixtureConsumptionAsync(bool code, string declaration)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration(code));
        var body = Request(code); body["tools"] = new JsonArray(JsonNode.Parse(declaration));
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await CompleteAsync(http, code);
    }

    [Test]
    [Arguments("""{"type":"unknown","key":"category","value":"fixture"}""")]
    [Arguments("""{"type":"eq","key":"category","value":null}""")]
    [Arguments("""{"type":"eq","key":"category","value":{}}""")]
    [Arguments("""{"type":"eq","key":"category","value":["fixture"]}""")]
    [Arguments("""{"type":"eq","value":"fixture"}""")]
    [Arguments("""{"type":"in","key":"category","value":[]}""")]
    [Arguments("""{"type":"in","key":"category","value":["fixture",1]}""")]
    [Arguments("""{"type":"nin","key":"approved","value":[true]}""")]
    [Arguments("""{"type":"and","filters":[]}""")]
    [Arguments("""{"type":"or","filters":null}""")]
    [Arguments("""{"type":"eq","key":"category","value":"fixture","ignored":true}""")]
    public async Task InvalidFileFilters_Return400AndPreserveQueuedResponseAsync(string filter)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration(false));
        var body = Request(false); body["tools"]![0]!["filters"] = JsonNode.Parse(filter);
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await CompleteAsync(http, false);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task OversizedFilterTree_Return400WithoutConsumptionAsync(bool depth)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration(false));
        JsonNode filter = Comparison();
        if (depth)
        {
            for (var index = 1; index < 9; index++) { filter = new JsonObject { ["type"] = "and", ["filters"] = new JsonArray(filter) }; }
        }
        else
        {
            var children = new JsonArray(); for (var index = 0; index < 128; index++) { children.Add(Comparison()); }
            filter = new JsonObject { ["type"] = "or", ["filters"] = children };
        }
        var body = Request(false); body["tools"]![0]!["filters"] = filter;
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await CompleteAsync(http, false);
    }

    [Test]
    [Arguments("container")]
    [Arguments("foreignFile")]
    [Arguments("missingFile")]
    public async Task CodeResourceMismatch_Returns409WithoutConsumptionAsync(string mismatch)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration(true));
        var body = Request(true, auto: mismatch != "container");
        body["tools"]![0]!["container"] = mismatch switch
        {
            "container" => JsonValue.Create("cntr_foreign"),
            "foreignFile" => new JsonObject { ["type"] = "auto", ["file_ids"] = new JsonArray("file_foreign") },
            _ => new JsonObject { ["type"] = "auto" },
        };
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await CompleteAsync(http, true);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AutomaticContainer_InputFilesAreAnExactSetIncludingEmptyAsync(bool empty)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var configuration = Configuration(true);
        var code = (LlmTestServerResponseCodeInterpreterItem)Items(true)[0];
        configuration.ChatScenarios[0].Responses[0] = configuration.ChatScenarios[0].Responses[0] with
        { OutputItems = [code with { InputFileIds = empty ? [] : [_inputFile, "file_second"] }, Items(true)[1]] };
        await new LlmTestServerClient(http).ConfigureAsync(configuration);
        var body = Request(true, auto: true);
        body["tools"]![0]!["container"]!["file_ids"] = empty ? new JsonArray() : new JsonArray("file_second", _inputFile);
        await CompleteAsync(http, true, body);
    }

    [Test]
    [Arguments(false, "/groq/openai/v1/responses")]
    [Arguments(true, "/microsoft-foundry/openai/v1/responses")]
    [Arguments(true, "/openrouter/api/v1/responses")]
    [Arguments(false, "/openai/v1/chat/completions")]
    [Arguments(true, "/openai/v1/chat/completions")]
    public async Task NativeFileAndCode_DoNotLeakIntoOtherNamespacesAsync(bool code, string path)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration(code));
        var body = Request(code);
        if (path.EndsWith("chat/completions", StringComparison.Ordinal))
        {
            body.Remove("input"); body.Remove("include");
            body["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "start" });
        }
        using var rejected = await http.PostAsJsonAsync(path, body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await CompleteAsync(http, code);
    }
}
