# Configuration and scenarios

Models, chat scenarios, modalities, fault simulation, runtime reconfiguration and assertions. Back to the [README](../../README.md).

## Configuration Model

LLM Test Server is configured with one `LlmTestServerConfiguration`.

You can apply that configuration in two places:

- at host startup through `builder.Services.AddLlmTestServer(options => ...)`
- at test runtime through `LlmTestServerClient.ConfigureAsync(...)` or `POST /admin-api/configure`

Runtime configuration is a replacement, not a merge. Applying a new configuration resets scenario positions and assertion events. The runtime snapshots the configuration, so later mutations to your builder/list objects do not change the running provider.

The default configuration advertises official OpenAI fixture model IDs:

| Model ID | Kind |
| --- | --- |
| `gpt-4.1-mini` | Chat |
| `text-embedding-3-small` | Embedding |
| `gpt-image-1` | Image |
| `gpt-4o-mini-tts` | Audio |
| `sora-2` | Video |

Model IDs and model kinds are enforced. A chat request to an embedding model, or an embedding request to an unknown model, returns `404` with `llm_test_server_unknown_model`.

Use typed helpers for broader OpenAI model registration when tests need more than the defaults:

```csharp
options.AddKnownOpenAiModels(reasoningTokens: 128);

// Or register only one modality family.
options.AddCurrentOpenAiChatModels(reasoningTokens: 128);
options.AddOpenAiEmbeddingModels();
options.AddOpenAiImageModels();
options.AddOpenAiAudioModels();
options.AddOpenAiVideoModels();
```

Individual helpers are also available, including `AddGpt55(...)`, `AddGpt54Mini(...)`, `AddGpt5Nano(...)`, `AddGpt41()`, `AddGpt4OMini()`, `AddTextEmbedding3Large()`, `AddGptImage2()`, `AddGptImage15()`, `AddGptImage1Mini()`, `AddTts1()`, `AddTts1Hd()`, and `AddSora2Pro()`. Keep using `AddModel(...)` for custom deployment names or provider-specific IDs.

### Fault Simulation

Fault simulation is configured on the provider-neutral runtime and applies through every provider namespace. Use it when a test needs retry, moderation, or provider-error handling without scripting each provider route separately.

```csharp
builder.Services.AddLlmTestServer(options =>
{
    // Allow two accepted runtime requests, then return 429 too_many_requests.
    options.SimulateRateLimitAfter(2);

    // Return 400 content_filter whenever a request text contains either term.
    options.SimulateContentFilter("blocked phrase", "unsafe fixture");
});
```

`SimulateRateLimitAfter(0)` makes the first valid runtime request return `429` with `too_many_requests`. `SimulateContentFilter(...)` checks chat messages, embedding inputs, image prompts, audio speech input, audio transcription/translation prompts, and video prompts. `ConfigureAsync(...)` and `ResetAsync()` both reset the rate-limit counter so tests stay deterministic.

## Chat Scenarios

### Contains Match

`WhenUserContains(...)` adds a user-message contains matcher. All configured match messages must be present somewhere in the actual request.

```csharp
options.AddChatScenario(
    "support-refund",
    scenario => scenario
        .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
        .WhenUserContains("refund")
        .Responds("I can help with a refund."));
```

### Exact Match

Use exact matching when a test must prove the complete prompt contract:

```csharp
using ManagedCode.LlmTestServer.Scenarios;

options.AddChatScenario(
    "exact-system-contract",
    scenario => scenario
        .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
        .WithExactMatch(
            new LlmTestServerMessage { Role = "system", Content = "Return JSON only." },
            new LlmTestServerMessage { Role = "user", Content = "Give me the invoice total." })
        .Responds("{\"total\":42.50}"));
```

### Queued Responses

Each matched request consumes the next response. This makes multi-turn flows deterministic:

```csharp
options.AddChatScenario(
    "two-turn-plan",
    scenario => scenario
        .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
        .WhenUserContains("make a plan")
        .Responds("First draft")
        .Responds("Revised draft"));
```

After the queue is exhausted, the provider returns `409` with `llm_test_server_scenario_exhausted`.

### Streaming Chunks

The first `Responds` argument is the non-streaming response. The remaining arguments are the streaming chunks:

```csharp
options.AddChatScenario(
    "streaming-answer",
    scenario => scenario
        .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
        .WhenUserContains("stream this")
        .Responds("blue whale", "blue ", "whale"));
```

For `stream: true`, LLM Test Server emits SSE `data:` frames and a final `data: [DONE]` marker. All chunks in one response share the same response id.

### Scripted Errors

Use `Fails(...)` to test client error handling without waiting for a real provider to fail:

```csharp
options.AddChatScenario(
    "content-policy-error",
    scenario => scenario
        .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
        .WhenUserContains("blocked fixture")
        .Fails(400, "content_filter", "The scripted request was rejected."));
```

### Delays And Cancellation

Use `DelaysBy(...)` to test timeout and cancellation behavior:

```csharp
options.AddChatScenario(
    "timeout-path",
    scenario => scenario
        .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
        .WhenUserContains("slow path")
        .Responds("eventual answer")
        .DelaysBy(1_500));
```

If the caller cancels during the delay, the queued response is not consumed.

### Scenario-Specific Token

A scenario can require its own bearer token:

```csharp
options.AddChatScenario(
    "tenant-a-only",
    scenario => scenario
        .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
        .RequireBearerToken("tenant-a-key")
        .WhenUserContains("tenant secret")
        .Responds("tenant-a response"));
```

Use either a global token or per-scenario tokens. If both are configured for a scenario, the same request must satisfy both checks, so different global and scenario tokens intentionally make that scenario unreachable.

## Modalities

### Embeddings

Configure a fixed embedding vector. Every input value receives the same deterministic vector:

```csharp
options.AddTextEmbedding3Small();
options.WithDefaultEmbeddingVector(0.01f, 0.02f, 0.03f, 0.04f);
```

```csharp
using ManagedCode.LlmTestServer.Client;
using Microsoft.Extensions.AI;

using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
using IEmbeddingGenerator<string, Embedding<float>> embeddings =
    new LlmTestServerEmbeddingGenerator(httpClient, "text-embedding-3-small");

var result = await embeddings.GenerateAsync(["alpha", "beta"]);
```

### Images

Image generation returns a deterministic base64 PNG by default:

```csharp
options.AddGptImage1();
```

```csharp
using ManagedCode.LlmTestServer.Client;
using Microsoft.Extensions.AI;

using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
using IImageGenerator images = new LlmTestServerImageGenerator(httpClient, "gpt-image-1");

var image = await images.GenerateAsync(
    new ImageGenerationRequest { Prompt = "compatibility marker" });
```

### Audio

Audio speech returns deterministic bytes with a matching media type. The default fixture is a minimal WAV payload:

```csharp
options.AddGpt4OMiniTts();
options.WithDefaultAudio(File.ReadAllBytes("fixtures/speech.wav"), "audio/wav");
```

```csharp
var audio = await httpClient.PostAsJsonAsync(
    "/openai/v1/audio/speech",
    new { model = "gpt-4o-mini-tts", input = "hello", voice = "alloy" });

audio.EnsureSuccessStatusCode();
var bytes = await audio.Content.ReadAsByteArrayAsync();
```

## Runtime Reconfiguration

Use runtime reconfiguration when each test needs a different provider script.

### Configure Before A Test

Use `ManagedCode.LlmTestServer.Client` as the universal test control surface. A test can start or discover the hosted Test Server, create one `LlmTestServerClient`, load all required models, datasets, scripted responses, errors, embeddings, images, and audio fixtures, and then call the provider through regular clients.

```csharp
using ManagedCode.LlmTestServer.Client;
using Microsoft.Extensions.AI;

using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
var server = LlmTestServerClient.Create(httpClient, bearerToken: "test-key");

await server.ConfigureAsync(
    config => config
        .RequireBearerToken("test-key")
        .UseChatModel("test-chat")
        .UseEmbeddingModel("test-embedding")
        .UseImageModel("test-image")
        .UseAudioModel("test-audio")
        .UseEmbeddingVector(0.9f, 0.8f)
        .UseImageDataUri("data:image/png;base64,Zm9v")
        .UseAudio([1, 2, 3, 4], "audio/test")
        .UseDataset(
            "checkout-flow",
            dataset => dataset
                .AddChatScenario(
                    "invoice-total",
                    scenario => scenario
                        .ForModel("test-chat")
                        .WhenUserContains("invoice total")
                        .Responds("{\"total\":42.50}", "{\"total\":", "42.50", "}"))
                .AddChatScenario(
                    "provider-rate-limit",
                    scenario => scenario
                        .ForModel("test-chat")
                        .WhenUserContains("rate limit")
                        .Fails(429, "rate_limit_exceeded", "Scripted rate limit."))),
    resetFirst: true);

using IChatClient chat = server.CreateChatClient("test-chat");
using IEmbeddingGenerator<string, Embedding<float>> embeddings =
    server.CreateEmbeddingGenerator("test-embedding");
using IImageGenerator images = server.CreateImageGenerator("test-image");

var response = await chat.GetResponseAsync(
[
    new ChatMessage(ChatRole.User, "What is the invoice total?"),
]);

var vector = await embeddings.GenerateAsync(["invoice"]);
var image = await images.GenerateAsync(new ImageGenerationRequest { Prompt = "fixture" });
var audio = await server.GenerateAudioAsync("test-audio", "speak this");
var assertions = await server.GetAssertionsAsync();
```

`resetFirst: true` clears previous assertion events and scenario positions before the new configuration is applied. Named datasets are active after configuration; they group scenarios so a test can load a whole conversation set without flattening everything by hand.

### Configure With `LlmTestServerClient`

```csharp
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Models;

using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
httpClient.DefaultRequestHeaders.Authorization =
    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-key");

var control = new LlmTestServerClient(httpClient);

var configuration = new LlmTestServerConfigurationBuilder()
    .RequireBearerToken("test-key")
    .AddModel("docs-chat", LlmTestServerModelKind.Chat)
    .AddModel("docs-embedding", LlmTestServerModelKind.Embedding)
    .AddChatScenario(
        "docs-blue-whale",
        scenario => scenario
            .ForModel("docs-chat")
            .WhenUserContains("largest animal")
            .Responds("blue whale", "blue ", "whale"))
    .WithDefaultEmbeddingVector(0.125f, 0.25f)
    .Build();

await control.ConfigureAsync(configuration);
```

If the current runtime already has a bearer token, the configure request must use that current token. If the new configuration sets a different token, future provider and control requests must use the new token.

### Configure With Raw JSON

`POST /admin-api/configure` accepts readable enum values such as `"chat"` and `"contains"`:

```bash
curl -X POST http://localhost:5000/admin-api/configure \
  -H "content-type: application/json" \
  -H "authorization: Bearer test-key" \
  -d '{
    "requiredBearerToken": "test-key",
    "models": [
      { "id": "docs-chat", "kind": "chat" },
      { "id": "docs-embedding", "kind": "embedding" },
      { "id": "docs-image", "kind": "image" },
      { "id": "docs-audio", "kind": "audio" }
    ],
    "chatScenarios": [
      {
        "id": "docs-blue-whale",
        "modelId": "docs-chat",
        "match": {
          "mode": "contains",
          "messages": [
            { "role": "user", "content": "largest animal" }
          ]
        },
        "responses": [
          {
            "content": "blue whale",
            "streamChunks": [ "blue ", "whale" ]
          }
        ]
      }
    ],
    "defaultEmbeddingVector": [ 0.125, 0.25 ],
    "defaultAudioMediaType": "audio/wav"
  }'
```

Then call the configured model:

```bash
curl -X POST http://localhost:5000/openai/v1/chat/completions \
  -H "content-type: application/json" \
  -H "authorization: Bearer test-key" \
  -d '{
    "model": "docs-chat",
    "messages": [
      { "role": "user", "content": "What is the largest animal?" }
    ]
  }'
```

## Assertions And Reset

The assertion summary is intentionally simple: it tells the test whether requests matched configured scenarios, missed, failed auth, used unknown models, exhausted a response queue, or returned scripted errors.

```csharp
var control = new LlmTestServerClient(httpClient);
var assertions = await control.GetAssertionsAsync();

if (assertions.Unmatched > 0 || assertions.ModelNotFound > 0)
{
    throw new InvalidOperationException("The application called an unexpected LLM path.");
}
```

Reset clears assertion events and scenario response positions without replacing the current configuration:

```csharp
await control.ResetAsync();
```

## Common Test Shapes

### Replace Provider Calls In Integration Tests

1. Start the LLM Test Server service in Aspire or an ASP.NET Core test host.
2. Configure the expected scenarios for the test.
3. Point the application under test at the LLM Test Server base URL.
4. Run the application flow.
5. Assert `GetAssertionsAsync()` has no unmatched, unknown-model, auth-failed, or exhausted events.

### Test Retry Logic

```csharp
options.AddChatScenario(
    "retry-then-success",
    scenario => scenario
        .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
        .WhenUserContains("retry")
        .Fails(429, "rate_limit_exceeded", "Try again.")
        .Responds("success after retry"));
```

### Test Prompt Contract Drift

```csharp
options.AddChatScenario(
    "exact-contract",
    scenario => scenario
        .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
        .WithExactMatch(
            new LlmTestServerMessage { Role = "system", Content = "Return JSON only." },
            new LlmTestServerMessage { Role = "user", Content = "Summarize invoice INV-42." })
        .Responds("{\"summary\":\"paid\"}"));
```

If the application changes the prompt, the request becomes unmatched and the test can fail on the assertion summary.

### Test Wrong Model Wiring

Configure only the models your application is allowed to call:

```csharp
options.AddModel("approved-chat-model", LlmTestServerModelKind.Chat);
```

If the app calls `old-chat-model` or calls `approved-chat-model` through the embeddings endpoint, LLM Test Server returns `llm_test_server_unknown_model`.

## Compatibility boundaries

Tool calls and structured output use explicit deterministic fixtures: `CallsTool(...)`, `CallsTools(...)` and `RespondsJson(...)`. Provider adapters preserve tool history and streaming events; `LlmTestServerChatClient` supports the standard Microsoft.Extensions.AI function-invocation loop. Fixtures must satisfy tool selection, argument schemas and response schemas; mismatches return 409 without consuming the response. See [supported contracts and tests](../Features/ToolAndStructuredFixtures.md).

Video create operations retain completed fixture jobs. Retrieve/content/delete require a previously created ID, and reset or reconfiguration clears jobs. OpenAI fixtures return `status=completed` and `progress=100`; IDs remain unique within a run. Dataset-local scenario IDs are isolated, including concurrent cancellation rollback.

Prompt-cache storage is bounded to 4096 prefixes by default. Use `WithPromptCacheCapacity(capacity)` to change the limit; zero disables caching. Eviction is deterministic FIFO, and reset/configure clears the cache.

Legacy Azure deployment APIs accept the dated `api-version` selected automatically by the Azure SDK. Use the default `AzureOpenAIClientOptions` constructor, or omit options entirely; do not pin a version for the emulator. These routes require exactly one supported deployment API version: the documented `2024-10-21` GA or `2025-04-01-preview` contract. Missing, malformed, duplicate, and unknown versions are rejected, including valid-looking future dates. Azure/Foundry `/openai/v1` endpoints require no dated query parameter.
