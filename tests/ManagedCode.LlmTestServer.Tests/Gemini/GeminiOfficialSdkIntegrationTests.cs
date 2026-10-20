using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Google.GenAI;
using Google.GenAI.Types;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.Scenarios;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using GenAiType = Google.GenAI.Types.Type;

namespace ManagedCode.LlmTestServer.Tests.Gemini;

public sealed class GeminiOfficialSdkIntegrationTests
{
    // Current stable model codes: https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash
    // and https://ai.google.dev/gemini-api/docs/models/gemini-embedding-2 (verified 2026-10-20).
    private const string _chatModel = "gemini-3.8-flash";
    private const string _embeddingModel = "gemini-embedding-2";
    private const string _signature = "Z29vZ2xlLWZpeHR1cmUtc2lnbmF0dXJl";
    private const string _key = "gemini-sdk-fixture-key";

    [Test]
    public async Task GenerateContent_ParsesTypedJsonAndPreservesSystemInstructionAndUsageAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http, _key);
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseChatModel(_chatModel)
            .UseChatScenario("google-json", scenario => scenario.ForModel(_chatModel).RespondsJson("{\"answer\":\"fixture\"}")));
        using var sdk = Sdk(http);
        var response = await sdk.Models.GenerateContentAsync(_chatModel, "answer with JSON", new GenerateContentConfig
        {
            SystemInstruction = new() { Parts = [Part.FromText("Use the scripted response")] },
            ResponseMimeType = "application/json",
            ResponseSchema = new() { Type = GenAiType.Object, Properties = new() { ["answer"] = new() { Type = GenAiType.String } }, Required = ["answer"] },
        });
        await Assert.That(response.Text).IsEqualTo("{\"answer\":\"fixture\"}");
        await Assert.That(response.ModelVersion).IsEqualTo(_chatModel);
        await Assert.That(response.Candidates!.Single().FinishReason!.Value).IsEqualTo(FinishReason.Stop);
        var trace = (await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending })).Requests.Single();
        await Assert.That(trace.OperationId).IsEqualTo(LlmTestServerProviderOperationIds.Gemini.ModelsGenerateContent);
        await Assert.That(trace.Messages[0].Content).IsEqualTo("Use the scripted response");
        await Assert.That(response.UsageMetadata!.PromptTokenCount).IsEqualTo(trace.Usage!.InputTokens);
        await Assert.That(response.UsageMetadata.TotalTokenCount).IsEqualTo(trace.Usage.TotalTokens);
    }

    [Test]
    public async Task GenerateContentStream_ConsumesActualSseAndKeepsStableIdentityAndTerminalUsageAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http, _key);
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseChatModel(_chatModel)
            .UseChatScenario("google-stream", scenario => scenario.ForModel(_chatModel).Responds("blue whale", "blue ", "whale")));
        using var sdk = Sdk(http);
        var chunks = new List<GenerateContentResponse>();
        await foreach (var chunk in sdk.Models.GenerateContentStreamAsync(_chatModel, "stream fixture")) { chunks.Add(chunk); }
        await Assert.That(string.Concat(chunks.Select(chunk => chunk.Text))).IsEqualTo("blue whale");
        await Assert.That(chunks.Select(chunk => chunk.ResponseId).Distinct().Count()).IsEqualTo(1);
        await Assert.That(chunks[^1].Candidates!.Single().FinishReason!.Value).IsEqualTo(FinishReason.Stop);
        var trace = (await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending })).Requests.Single();
        await Assert.That(trace.OperationId).IsEqualTo(LlmTestServerProviderOperationIds.Gemini.ModelsStreamGenerateContent);
        await Assert.That(trace.ResponseContentType).Contains("text/event-stream");
        await Assert.That(trace.TerminalMarkerObserved).IsTrue();
        await Assert.That(chunks[^1].UsageMetadata!.TotalTokenCount).IsEqualTo(trace.Usage!.TotalTokens);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FunctionRound_OfficialSdkPreservesCallIdentityArgumentsAndToolResultAsync(bool streaming)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http, _key);
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseChatModel(_chatModel)
            .UseConversation("google-function", graph => graph.ForModel(_chatModel)
                .Step("call", step => step.WhenLatestUserEquals("weather").CallsTools(new LlmTestServerToolCall { Name = "weather", ArgumentsJson = "{\"city\":\"Paris\"}", Id = "call_google_weather", ContinuationToken = _signature }).Then("answer"))
                .Step("answer", step => step.WhenToolResult("weather", "call_google_weather")
                    .WithToolArgumentsJson("{\"city\":\"Paris\"}").WhenLatestJsonValue("$.temperature", 22)
                    .Responds("Paris is 22 C", "Paris is ", "22 C").Complete())));
        using var conversation = control.ForConversation("google-function");
        using var sdk = Sdk(conversation.HttpClient);
        var options = new GenerateContentConfig
        {
            Tools = [new() { FunctionDeclarations = [new() { Name = "weather", Parameters = new()
            { Type = GenAiType.Object, Properties = new() { ["city"] = new() { Type = GenAiType.String } }, Required = ["city"] } }] }],
            ToolConfig = new() { FunctionCallingConfig = new() { Mode = FunctionCallingConfigMode.Any, AllowedFunctionNames = ["weather"] } },
        };
        List<Content> messages = [Text("user", "weather")];
        var first = await ResponsesAsync(sdk, messages, options, streaming);
        var call = first.SelectMany(response => response.Candidates!).SelectMany(candidate => candidate.Content!.Parts!)
            .Select(part => part.FunctionCall).Single(value => value is not null)!;
        await Assert.That(call.Name).IsEqualTo("weather");
        await Assert.That(call.Id).IsEqualTo("call_google_weather");
        await Assert.That(JsonSerializer.SerializeToElement(call.Args!).GetProperty("city").GetString()).IsEqualTo("Paris");
        var signedContent = first.SelectMany(response => response.Candidates!).Select(candidate => candidate.Content!).Single(content => content.Parts!.Any(part => part.FunctionCall is not null));
        await Assert.That(Convert.ToBase64String(signedContent.Parts!.Single(part => part.FunctionCall is not null).ThoughtSignature!)).IsEqualTo(_signature);
        messages.Add(signedContent);
        messages.Add(new()
        {
            Role = "user",
            Parts = [new() { FunctionResponse = new()
        { Id = call.Id, Name = call.Name, Response = new() { ["temperature"] = 22 } } }]
        });
        options.ToolConfig = new() { FunctionCallingConfig = new() { Mode = FunctionCallingConfigMode.Auto } };
        var completed = await ResponsesAsync(sdk, messages, options, streaming);
        await Assert.That(string.Concat(completed.Select(response => response.Text))).IsEqualTo("Paris is 22 C");
        var trace = (await conversation.Control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending })).Requests[^1];
        await Assert.That(trace.Messages.Single(message => message.Role == "tool").ToolCallId).IsEqualTo(call.Id);
        await Assert.That(trace.Messages.Single(message => message.Role == "tool").Content).IsEqualTo("{\"temperature\":22}");
        var state = (await conversation.Control.GetConversationsAsync()).Single();
        await Assert.That(state.ExecutedSteps).IsEqualTo(2);
        await Assert.That(state.IsTerminal).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EmbedContent_OfficialSdkUsesBatchRouteForSingleAndMultipleInputsAsync(bool multiple)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http, _key);
        var vector = Enumerable.Range(0, 256).Select(index => (float)index / 256).ToArray();
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseEmbeddingModel(_embeddingModel).UseEmbeddingVector(vector));
        using var sdk = Sdk(http);
        List<Content> inputs = [Text("user", "first embedding")];
        if (multiple) { inputs.Add(Text("user", "second embedding")); }
        var response = await sdk.Models.EmbedContentAsync(_embeddingModel, inputs, new() { OutputDimensionality = 128 });
        await Assert.That(response.Embeddings!.Count).IsEqualTo(inputs.Count);
        foreach (var embedding in response.Embeddings)
        { await Assert.That(embedding.Values!).IsEquivalentTo(vector.Take(128).Select(value => (double)value)); }
        var trace = (await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending })).Requests.Single();
        await Assert.That(trace.OperationId).IsEqualTo(LlmTestServerProviderOperationIds.Gemini.ModelsBatchEmbedContents);
        await Assert.That(trace.StatusCode).IsEqualTo(200);
        await Assert.That(trace.Path).Contains(":batchEmbedContents");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EmbedContent_RejectsDimensionsBeyondFixtureAtomicallyAndAllowsExplicitRetryAsync(bool multiple)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http, _key);
        var vector = Enumerable.Range(0, 128).Select(index => (float)index / 128).ToArray();
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseEmbeddingModel(_embeddingModel).UseEmbeddingVector(vector));
        using var sdk = Sdk(http);
        List<Content> inputs = [Text("user", "first embedding")];
        if (multiple) { inputs.Add(Text("user", "second embedding")); }

        ClientError? failure = null;
        try { await sdk.Models.EmbedContentAsync(_embeddingModel, inputs, new() { OutputDimensionality = 256 }); }
        catch (ClientError exception) { failure = exception; }
        await Assert.That(failure?.StatusCode).IsEqualTo(409);
        var rejectedSummary = await control.GetAssertionsAsync();
        await Assert.That(rejectedSummary.Matched).IsEqualTo(0);
        await Assert.That(rejectedSummary.ErrorsReturned).IsEqualTo(1);
        await Assert.That(rejectedSummary.Events.Single().Message).Contains("fewer coordinates");
        var rejected = (await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending })).Requests.Single();
        await Assert.That(rejected.StatusCode).IsEqualTo(409);
        await Assert.That(rejected.ErrorCode).IsEqualTo("409");
        await Assert.That(rejected.OperationId).IsEqualTo(LlmTestServerProviderOperationIds.Gemini.ModelsBatchEmbedContents);

        var response = await sdk.Models.EmbedContentAsync(_embeddingModel, inputs, new() { OutputDimensionality = 128 });
        await Assert.That(response.Embeddings!.Count).IsEqualTo(inputs.Count);
        foreach (var embedding in response.Embeddings)
        {
            await Assert.That(embedding.Values!.Count).IsEqualTo(128);
            await Assert.That(embedding.Values.SequenceEqual(vector.Select(value => (double)value))).IsTrue();
        }
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(1);
        var evidence = await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(evidence.Requests.Count).IsEqualTo(2);
        await Assert.That(evidence.Requests[^1].StatusCode).IsEqualTo(200);
        await Assert.That(evidence.Requests[^1].OperationId).IsEqualTo(LlmTestServerProviderOperationIds.Gemini.ModelsBatchEmbedContents);
    }

    [Test]
    [Arguments(400, false)]
    [Arguments(400, true)]
    [Arguments(429, false)]
    [Arguments(429, true)]
    [Arguments(500, false)]
    [Arguments(500, true)]
    public async Task OfficialSdk_ReportsScriptedProviderErrorsOnceThenExplicitRetryRecoversAsync(int status, bool streaming)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http, _key);
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseChatModel(_chatModel)
            .UseChatScenario("google-error", scenario => scenario.ForModel(_chatModel)
                .Fails(status, "fixture_provider_failure", "Scripted Google SDK failure").Responds("recovered")));
        using var sdk = Sdk(http);
        ApiException? failure = null;
        try { await ResponsesAsync(sdk, [Text("user", "error")], null, streaming); }
        catch (ApiException exception) { failure = exception; }
        await Assert.That(failure?.StatusCode).IsEqualTo(status);
        await Assert.That(failure!.Message).Contains("Scripted Google SDK failure");
        await Assert.That((await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending })).Requests.Count).IsEqualTo(1);
        var response = await ResponsesAsync(sdk, [Text("user", "error")], null, streaming);
        await Assert.That(string.Concat(response.Select(chunk => chunk.Text))).IsEqualTo("recovered");
        await Assert.That((await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending })).Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task AuthenticationAndInputFilter_DoNotConsumeSuccessfulFixtureAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http, _key);
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseChatModel(_chatModel)
            .SimulateContentFilter("blocked_sdk_input")
            .UseChatScenario("google-protected", scenario => scenario.ForModel(_chatModel).Responds("protected response")));
        using var wrong = Sdk(http, "wrong-key");
        ApiException? unauthorized = null;
        try { await wrong.Models.GenerateContentAsync(_chatModel, "hello"); }
        catch (ApiException exception) { unauthorized = exception; }
        await Assert.That(unauthorized?.StatusCode).IsEqualTo(401);
        using var sdk = Sdk(http);
        ApiException? filtered = null;
        try { await sdk.Models.GenerateContentAsync(_chatModel, "blocked_sdk_input"); }
        catch (ApiException exception) { filtered = exception; }
        await Assert.That(filtered?.StatusCode).IsEqualTo(400);
        var response = await sdk.Models.GenerateContentAsync(_chatModel, "clean input");
        await Assert.That(response.Text).IsEqualTo("protected response");
        var evidence = await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(evidence.Requests.Count).IsEqualTo(3);
        await Assert.That(evidence.Requests[^1].StatusCode).IsEqualTo(200);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    public async Task BatchEmbedding_InvalidShapeRejectsBeforeRuntimeThenOfficialSdkStillSucceedsAsync(int kind)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http, _key);
        var vector = Enumerable.Range(0, 128).Select(index => (float)index / 128).ToArray();
        await control.ConfigureAsync(config => config.UseEmbeddingModel(_embeddingModel).UseEmbeddingVector(vector));
        var entry = new Dictionary<string, object?>
        {
            ["model"] = "models/" + _embeddingModel,
            ["content"] = new { parts = new[] { new { text = "valid text" } } },
        };
        object request;
        switch (kind)
        {
            case 0: request = new { requests = Array.Empty<object>() }; break;
            case 1: request = new { requests = Enumerable.Repeat(entry, 129).ToArray() }; break;
            case 2: entry["model"] = "models/wrong-model"; request = new { requests = new[] { entry } }; break;
            case 3: entry["content"] = new { parts = new[] { new { inlineData = new { mimeType = "image/png", data = "AA==" } } } }; request = new { requests = new[] { entry } }; break;
            case 4: entry["outputDimensionality"] = 0; request = new { requests = new[] { entry } }; break;
            case 5: entry["outputDimensionality"] = 3073; request = new { requests = new[] { entry } }; break;
            case 6: entry["taskType"] = "RETRIEVAL_QUERY"; request = new { requests = new[] { entry } }; break;
            case 7: entry["title"] = "unsupported title"; request = new { requests = new[] { entry } }; break;
            default: entry["embedContentConfig"] = new { documentOcr = true }; request = new { requests = new[] { entry } }; break;
        }
        using var rejected = await http.PostAsJsonAsync("/gemini/v1beta/models/" + _embeddingModel + ":batchEmbedContents", request);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        using var sdk = Sdk(http);
        var response = await sdk.Models.EmbedContentAsync(_embeddingModel, "valid text");
        await Assert.That(response.Embeddings!.Single().Values!).IsEquivalentTo(vector.Select(value => (double)value));
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(1);
    }

    [Test]
    public async Task OfficialSdk_UnknownModelsExpose404ForGenerationAndEmbeddingAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http, _key);
        await control.ConfigureAsync(config => config.UseChatModel(_chatModel).UseEmbeddingModel(_embeddingModel)
            .UseEmbeddingVector(Enumerable.Repeat(0.25f, 128).ToArray()).UseChatScenario("known-google-model", scenario => scenario.ForModel(_chatModel).Responds("known model response")));
        using var sdk = Sdk(http);
        ApiException? generation = null;
        try { await sdk.Models.GenerateContentAsync("unconfigured-google-sdk-model", "unknown"); }
        catch (ApiException exception) { generation = exception; }
        await Assert.That(generation?.StatusCode).IsEqualTo(404);
        ApiException? embedding = null;
        try { await sdk.Models.EmbedContentAsync("unconfigured-google-sdk-model", "unknown"); }
        catch (ApiException exception) { embedding = exception; }
        await Assert.That(embedding?.StatusCode).IsEqualTo(404);
        await Assert.That((await sdk.Models.GenerateContentAsync(_chatModel, "known")).Text).IsEqualTo("known model response");
        await Assert.That((await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending })).Requests.Count).IsEqualTo(3);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task FunctionContinuation_RejectsMissingOrChangedSignatureWithoutAdvancingAndRecoversAsync(bool streaming, bool tampered)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http, _key);
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseChatModel(_chatModel)
            .UseConversation("signed-recovery", graph => graph.ForModel(_chatModel)
                .Step("call", step => step.WhenLatestUserEquals("weather").CallsTools(SignedCall("call_signed")).Then("answer"))
                .Step("answer", step => step.WhenToolResult("weather", "call_signed").Responds("signed recovery").Complete())));
        using var conversation = control.ForConversation("signed-recovery");
        using var sdk = Sdk(conversation.HttpClient);
        var options = WeatherOptions();
        options.SystemInstruction = new() { Parts = [Part.FromText(string.Join(" ", Enumerable.Repeat("shared reference", 4096)))] };
        var first = await ResponsesAsync(sdk, [Text("user", "weather")], options, streaming);
        var signed = first.SelectMany(response => response.Candidates!).Select(candidate => candidate.Content!)
            .Single(content => content.Parts!.Any(part => part.FunctionCall is not null));
        var signedPart = signed.Parts!.Single(part => part.FunctionCall is not null);
        var changed = signed with { Parts = [signedPart with { ThoughtSignature = tampered ? new byte[] { 1, 2, 3 } : null }] };
        List<Content> invalid = [Text("user", "weather"), changed, FunctionResult("call_signed")];
        options.ToolConfig = new() { FunctionCallingConfig = new() { Mode = FunctionCallingConfigMode.Auto } };
        ClientError? failure = null;
        try { await ResponsesAsync(sdk, invalid, options, streaming); }
        catch (ClientError exception) { failure = exception; }
        await Assert.That(failure?.StatusCode).IsEqualTo(tampered ? 409 : 400);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(1);
        var beforeRecovery = (await control.GetConversationsAsync()).Single();
        await Assert.That(beforeRecovery.ExecutedSteps).IsEqualTo(1);
        await Assert.That(beforeRecovery.NextSteps.Single()).IsEqualTo("answer");
        var rejected = (await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending })).Requests[^1];
        await Assert.That(rejected.StatusCode).IsEqualTo(tampered ? 409 : 400);
        var recovered = await ResponsesAsync(sdk, [Text("user", "weather"), signed, FunctionResult("call_signed")], options, streaming);
        await Assert.That(string.Concat(recovered.Select(response => response.Text))).IsEqualTo("signed recovery");
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
        var traces = (await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending })).Requests;
        await Assert.That(traces.Count).IsEqualTo(3);
        await Assert.That(traces[^1].Messages.Single(message => message.ToolCalls.Count > 0).ToolCalls.Single().ContinuationToken).IsEqualTo(_signature);
        await Assert.That(traces[^1].Usage!.CachedInputTokens).IsGreaterThan(0);
        await Assert.That((await control.GetConversationsAsync()).Single().IsTerminal).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FunctionContinuation_PreservesParallelAndSequentialSignaturesAndIgnoresPreviousTurnAsync(bool streaming)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http, _key);
        const string secondSignature = "c2Vjb25kLXN0ZXAtc2lnbmF0dXJl";
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseChatModel(_chatModel)
            .UseConversation("signed-steps", graph => graph.ForModel(_chatModel)
                .Step("parallel", step => step.WhenLatestUserEquals("weather").CallsTools(SignedCall("parallel_first"),
                    new LlmTestServerToolCall { Id = "parallel_second", Name = "weather", ArgumentsJson = "{\"city\":\"Paris\"}" }).Then("sequential"))
                .Step("sequential", step => step.WhenToolResult("weather", "parallel_second")
                    .CallsTools(SignedCall("sequential", secondSignature)).Then("answer"))
                .Step("answer", step => step.WhenToolResult("weather", "sequential").Responds("all signatures preserved").Complete())));
        using var conversation = control.ForConversation("signed-steps");
        using var sdk = Sdk(conversation.HttpClient);
        // Unsigned function history precedes a new ordinary user turn and is intentionally valid.
        List<Content> messages = [Text("user", "old turn"), new() { Role = "model", Parts = [new() { FunctionCall = new() { Id = "old_call", Name = "weather", Args = new() { ["city"] = "Paris" } } }] },
            FunctionResult("old_call"), Text("user", "weather")];
        var options = WeatherOptions();
        var first = await ResponsesAsync(sdk, messages, options, streaming);
        var parallel = first.SelectMany(response => response.Candidates!).Select(candidate => candidate.Content!)
            .Single(content => content.Parts!.Any(part => part.FunctionCall is not null));
        var parts = parallel.Parts!.Where(part => part.FunctionCall is not null).ToList();
        await Assert.That(parts.Count).IsEqualTo(2);
        await Assert.That(Convert.ToBase64String(parts[0].ThoughtSignature!)).IsEqualTo(_signature);
        await Assert.That(parts[1].ThoughtSignature).IsNull();
        messages.Add(parallel);
        messages.Add(FunctionResult("parallel_first"));
        messages.Add(FunctionResult("parallel_second"));
        var next = await ResponsesAsync(sdk, messages, options, streaming);
        var sequential = next.SelectMany(response => response.Candidates!).Select(candidate => candidate.Content!)
            .Single(content => content.Parts!.Any(part => part.FunctionCall is not null));
        await Assert.That(Convert.ToBase64String(sequential.Parts!.Single(part => part.FunctionCall is not null).ThoughtSignature!)).IsEqualTo(secondSignature);
        messages.Add(sequential);
        messages.Add(FunctionResult("sequential"));
        options.ToolConfig = new() { FunctionCallingConfig = new() { Mode = FunctionCallingConfigMode.Auto } };
        var missingEarlier = messages.Select(content => ReferenceEquals(content, parallel)
            ? content with { Parts = parts.Select((part, index) => index == 0 ? part with { ThoughtSignature = null } : part).ToList() } : content).ToList();
        ClientError? missing = null;
        try { await ResponsesAsync(sdk, missingEarlier, options, streaming); }
        catch (ClientError exception) { missing = exception; }
        await Assert.That(missing?.StatusCode).IsEqualTo(400);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
        var final = await ResponsesAsync(sdk, messages, options, streaming);
        await Assert.That(string.Concat(final.Select(response => response.Text))).IsEqualTo("all signatures preserved");
        await Assert.That((await control.GetConversationsAsync()).Single().ExecutedSteps).IsEqualTo(3);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(3);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FunctionFixture_RequiresSignatureAndCountsItsRetentionBytesBeforeAdmissionAsync(bool oversized)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http, _key);
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseChatModel(_chatModel).UseConversationCapacity(8, 4096)
            .UseConversation("signed-budget", graph => graph.ForModel(_chatModel)
                .Step("call", step => step.WhenLatestUserEquals("weather").CallsTools(SignedCall("budget", oversized ? Convert.ToBase64String(new byte[6144]) : null)).Complete())));
        using var conversation = control.ForConversation("signed-budget");
        using var sdk = Sdk(conversation.HttpClient);
        ClientError? rejected = null;
        try { await sdk.Models.GenerateContentAsync(_chatModel, "weather", WeatherOptions()); }
        catch (ClientError exception) { rejected = exception; }
        await Assert.That(rejected?.StatusCode).IsEqualTo(409);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        await Assert.That((await control.GetConversationsAsync()).Count).IsEqualTo(0);
        var failureTrace = (await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending })).Requests.Single();
        await Assert.That(failureTrace.ErrorCode).IsEqualTo("409");
        await Assert.That(failureTrace.RuntimeMessage).Contains(oversized ? "conversation state capacity" : "continuation metadata");
        await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseChatModel(_chatModel).UseConversationCapacity(8, 4096)
            .UseConversation("signed-budget", graph => graph.ForModel(_chatModel)
                .Step("call", step => step.WhenLatestUserEquals("weather").CallsTools(SignedCall("budget")).Complete())));
        var valid = await sdk.Models.GenerateContentAsync(_chatModel, "weather", WeatherOptions());
        await Assert.That(Convert.ToBase64String(valid.Candidates!.Single().Content!.Parts!.Single().ThoughtSignature!)).IsEqualTo(_signature);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(1);
        if (oversized)
        {
            var smallUsage = (await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending })).Requests[^1].Usage!;
            var largeSignature = Convert.ToBase64String(new byte[6144]);
            await control.ConfigureAsync(config => config.RequireBearerToken(_key).UseChatModel(_chatModel).UseConversationCapacity(8, 32768)
                .UseConversation("signed-budget", graph => graph.ForModel(_chatModel)
                    .Step("call", step => step.WhenLatestUserEquals("weather").CallsTools(SignedCall("budget", largeSignature)).Complete())));
            var large = await sdk.Models.GenerateContentAsync(_chatModel, "weather", WeatherOptions());
            await Assert.That(Convert.ToBase64String(large.Candidates!.Single().Content!.Parts!.Single().ThoughtSignature!)).IsEqualTo(largeSignature);
            var largeUsage = (await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending })).Requests[^1].Usage!;
            await Assert.That(largeUsage.InputTokens).IsEqualTo(smallUsage.InputTokens);
            await Assert.That(largeUsage.OutputTokens).IsEqualTo(smallUsage.OutputTokens);
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task FunctionContinuation_RejectsMalformedWireSignatureBeforeConsumingFixtureAsync(int kind)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http, _key);
        await control.ConfigureAsync(config => config.UseChatModel(_chatModel)
            .UseChatScenario("signature-shape", scenario => scenario.ForModel(_chatModel).Responds("not consumed")));
        var signature = kind switch { 0 => string.Empty, 1 => "not base64!", _ => Convert.ToBase64String(new byte[12289]) };
        using var rejected = await http.PostAsJsonAsync("/gemini/v1beta/models/" + _chatModel + ":generateContent", new
        {
            contents = new object[] { new { role = "user", parts = new[] { new { text = "weather" } } },
                new { role = "model", parts = new[] { new { functionCall = new { name = "weather", args = new { city = "Paris" } }, thoughtSignature = signature } } } }
        });
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(0);
        using var sdk = Sdk(http);
        await Assert.That((await sdk.Models.GenerateContentAsync(_chatModel, "clean input")).Text).IsEqualTo("not consumed");
    }

    private static LlmTestServerToolCall SignedCall(string id, string? signature = _signature)
    {
        return new() { Id = id, Name = "weather", ArgumentsJson = "{\"city\":\"Paris\"}", ContinuationToken = signature };
    }

    private static GenerateContentConfig WeatherOptions()
    {
        return new()
        {
            Tools = [new() { FunctionDeclarations = [new() { Name = "weather", Parameters = new()
        { Type = GenAiType.Object, Properties = new() { ["city"] = new() { Type = GenAiType.String } }, Required = ["city"] } }] }],
            ToolConfig = new() { FunctionCallingConfig = new() { Mode = FunctionCallingConfigMode.Any } }
        };
    }

    private static Content FunctionResult(string id)
    {
        return new()
        {
            Role = "user",
            Parts = [new() { FunctionResponse = new()
        { Id = id, Name = "weather", Response = new() { ["temperature"] = 22 } } }]
        };
    }

    private static Google.GenAI.Client Sdk(HttpClient http, string apiKey = _key)
    {
        return new(apiKey: apiKey, enterprise: false,
            httpOptions: new() { BaseUrl = new Uri(http.BaseAddress!, "/gemini").ToString(), ApiVersion = "v1beta", RetryOptions = new() { Attempts = 1 } },
            clientOptions: new() { HttpClientFactory = () => http });
    }

    private static Content Text(string role, string value)
    {
        return new() { Role = role, Parts = [Part.FromText(value)] };
    }

    private static async Task<List<GenerateContentResponse>> ResponsesAsync(Google.GenAI.Client sdk,
        List<Content> messages, GenerateContentConfig? options, bool streaming)
    {
        if (!streaming) { return [await sdk.Models.GenerateContentAsync(_chatModel, messages, options)]; }
        var responses = new List<GenerateContentResponse>();
        await foreach (var response in sdk.Models.GenerateContentStreamAsync(_chatModel, messages, options)) { responses.Add(response); }
        return responses;
    }
}
