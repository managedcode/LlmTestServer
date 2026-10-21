# LLM Test Server

[![CI](https://github.com/managedcode/LlmTestServer/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/managedcode/LlmTestServer/actions/workflows/ci.yml)
[![Release](https://github.com/managedcode/LlmTestServer/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/managedcode/LlmTestServer/actions/workflows/release.yml)
[![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer)
[![License](https://img.shields.io/github/license/managedcode/LlmTestServer.svg?style=flat-square)](LICENSE)

**A local, deterministic LLM provider for .NET tests, with a dashboard that shows where every token goes.**

**Website:** [llmtest.managed-code.com](https://llmtest.managed-code.com) · .NET packages and a Docker image for any other stack.

[![Token flow of a five-request conversation: system prompt, tool schemas, user, assistant, tool calls and tool results flowing into cache read, cache write, uncached input, response and reasoning](docs/images/showcase-token-flow.jpg)](docs/images/showcase-token-flow.jpg)

[What it is](#what-it-is) · [Token usage and cache hits](#token-usage-and-prompt-cache-hits) · [Quick start](#quick-start) · [Capabilities](#what-you-can-do) · [Providers](#providers) · [TypeSafe / Jev](#typesafe-and-jev) · [Docs](#learn-more)

## What it is

LLM Test Server is a small ASP.NET Core server that speaks the HTTP APIs of 13 LLM providers, each under its own
namespace: `/openai/v1`, `/anthropic/v1`, `/gemini/v1beta`, `/azure-openai`, and so on. Your application keeps its
official SDK or `Microsoft.Extensions.AI` client and only changes the base URL. Every answer, tool call, stream chunk
and failure comes from a fixture your test configures, so the same test produces the same result on every run — with
no external model calls and no production credentials. Run it from an Aspire AppHost, inside your own ASP.NET Core host,
or as a standalone container.

Native decision APIs are included too: **TypeSafe Jev**, Kev through System One, and Cloudflare Clef.
Use the typed client from `ManagedCode.LlmTestServer.Client` with the contracts in
`ManagedCode.LlmTestServer.SystemOne` — see the [TypeSafe/Jev example](docs/Reference/Clients.md#typesafe-jev-through-system-one).

## Why use it

- **Deterministic tests for LLM features.** Script answers, tool calls, multi-turn conversations, streams, rate limits
  and content filters instead of mocking SDK internals.
- **Your real integration code runs.** Requests travel through the same provider SDKs and wire formats your
  application uses in production.
- **Proof of what your application sent.** Every request is recorded with its messages, payloads, usage and outcome;
  tests can query, export and verify that journal.
- **Token and cache visibility.** See how much of each prompt is system text, tool schemas or tool results, and how
  much of it the prompt cache served.
- **Safe in parallel.** Isolated sessions give each test its own configuration, queues, cache and journal on one
  shared server.

## Token usage and prompt-cache hits

Every chat request gets deterministic token usage from the server's tokenizer, and the provider response carries it in
that provider's own usage fields. Three things are tracked for each request:

- **What the input is made of.** Input tokens are split by role — system prompt, tool schemas, user, assistant text,
  tool-call arguments and tool results. The parts add up exactly to the reported input and are recorded as
  `InputComposition` on the request's usage.
- **How the prompt cache served it.** A repeated prompt prefix reports cache reads; the first request that makes a
  prefix cacheable reports cache writes. Thresholds follow each provider (for example 1,024 tokens in 128-token steps on
  OpenAI-compatible routes, 2,048 on Gemini, and explicit cache markers on Anthropic and Bedrock).
- **What the model produced.** Output tokens, with reasoning tokens for reasoning-capable fixture models reported
  separately.

Read it from a test through the request journal (`GET /admin-api/requests`):

```csharp
var journal = await control.GetRequestsAsync();
foreach (var request in journal.Requests)
{
    if (request.Usage is not { InputComposition: { } parts } usage) continue;
    Console.WriteLine($"{request.ModelId}: {usage.InputTokens} input = {parts.SystemTokens} system + " +
        $"{parts.ToolDefinitionTokens} tool schemas + {parts.UserTokens} user + {parts.AssistantTokens} assistant + " +
        $"{parts.ToolCallTokens} tool calls + {parts.ToolResultTokens} tool results + {parts.OtherTokens} other; " +
        $"{usage.CachedInputTokens} read from cache, {usage.CacheCreationInputTokens} written");
}
```

Or open the dashboard: the **Tokens** tab draws the flow above for one request or a whole labeled conversation, with
context-mix and prompt-cache donuts and per-request context growth. **Traffic insights** (`/?view=insights`) charts
all recorded traffic by provider, model, outcome, latency, cache and context mix; clicking a slice filters the inbox.

[![Traffic insights: donuts, capsule bars and columns across providers, models, outcomes, cache and latency](docs/images/showcase-insights.jpg)](docs/images/showcase-insights.jpg)

Details: [dashboard analytics](docs/Features/DashboardAnalytics.md) ·
[token usage and prompt-cache accounting](docs/Reference/TokenUsageAndPromptCache.md).

## Quick start

| You use | Install |
| --- | --- |
| Any language or stack | `docker run -p 8080:8080 ghcr.io/managedcode/llm-test-server:1.0.0` |
| .NET with Aspire | `dotnet add package ManagedCode.LlmTestServer.Aspire` |
| .NET tests | `dotnet add package ManagedCode.LlmTestServer.Client` |

### With Docker, from any language

Start the server. The dashboard opens at `http://localhost:8080/`.

```sh
docker run --rm -p 8080:8080 \
  -e LlmTestServer__RequiredBearerToken=local-test-key \
  ghcr.io/managedcode/llm-test-server:1.0.0
```

Choose a quick start below. Each example configures its own fixture, calls the server through the official
OpenAI SDK and prints `Hello from your fixture`. The key is a local test value; no provider account is needed.

### .NET — official OpenAI SDK

In a .NET 10 console app:

```sh
dotnet add package ManagedCode.LlmTestServer.Client
dotnet add package OpenAI
```

Save as `Program.cs`, then run `dotnet run`:

```csharp
using System.ClientModel;
using ManagedCode.LlmTestServer.Client;
using OpenAI;
using OpenAI.Chat;

using var http = new HttpClient { BaseAddress = new Uri("http://localhost:8080/") };
var control = new LlmTestServerClient(http, "local-test-key");
await control.ConfigureAsync(config => config
    .RequireBearerToken("local-test-key")
    .UseGpt41Mini()
    .UseChatScenario("hello", scenario => scenario
        .WhenUserContains("Hello")
        .Responds("Hello from your fixture")));

var chat = new ChatClient("gpt-4.1-mini", new ApiKeyCredential("local-test-key"),
    new OpenAIClientOptions { Endpoint = new Uri(http.BaseAddress!, "openai/v1") });
ChatCompletion reply = await chat.CompleteChatAsync([new UserChatMessage("Hello")]);
Console.WriteLine(reply.Content[0].Text); // Hello from your fixture
```

### JavaScript — official OpenAI SDK

Requires Node.js 20 or later:

```sh
npm install openai
```

Save as `example.mjs`, then run `node example.mjs`:

```javascript
import OpenAI from "openai";

const baseURL = "http://localhost:8080";
const apiKey = "local-test-key";

// Configure the test fixture through the server's management API.
const configured = await fetch(`${baseURL}/admin-api/configure`, {
  method: "POST",
  headers: {
    Authorization: `Bearer ${apiKey}`,
    "Content-Type": "application/json",
  },
  body: JSON.stringify({
    requiredBearerToken: apiKey,
    models: [{ id: "gpt-4.1-mini", kind: "chat" }],
    chatScenarios: [{
      id: "hello",
      modelId: "gpt-4.1-mini",
      responses: [{ content: "Hello from your fixture" }],
    }],
  }),
});
if (!configured.ok) throw new Error(await configured.text());

const client = new OpenAI({ baseURL: `${baseURL}/openai/v1`, apiKey });
const reply = await client.chat.completions.create({
  model: "gpt-4.1-mini",
  messages: [{ role: "user", content: "Hello" }],
});
console.log(reply.choices[0].message.content); // Hello from your fixture
```

### Python — official OpenAI SDK

```sh
python -m pip install openai httpx
```

Save as `example.py`, then run `python example.py`:

```python
import httpx
from openai import OpenAI

base_url = "http://localhost:8080"
api_key = "local-test-key"

# Configure the test fixture through the server's management API.
configured = httpx.post(
    f"{base_url}/admin-api/configure",
    headers={"Authorization": f"Bearer {api_key}"},
    json={
        "requiredBearerToken": api_key,
        "models": [{"id": "gpt-4.1-mini", "kind": "chat"}],
        "chatScenarios": [{
            "id": "hello",
            "modelId": "gpt-4.1-mini",
            "responses": [{"content": "Hello from your fixture"}],
        }],
    },
)
configured.raise_for_status()

with OpenAI(base_url=f"{base_url}/openai/v1", api_key=api_key) as client:
    reply = client.chat.completions.create(
        model="gpt-4.1-mini",
        messages=[{"role": "user", "content": "Hello"}],
    )
    print(reply.choices[0].message.content)  # Hello from your fixture
```

Open [the dashboard](http://localhost:8080/) to inspect the request, response and token usage.
The management API configures fixtures; the official SDK sends the provider request.
For other providers, use their SDK and [provider endpoint](docs/Reference/ProvidersAndRoutes.md).

More: [Docker distribution](docs/Features/DockerDistribution.md) ·
[official JavaScript SDK](https://github.com/openai/openai-node) ·
[official Python SDK](https://github.com/openai/openai-python).

### With .NET and Aspire

```sh
dotnet add package ManagedCode.LlmTestServer.Aspire
dotnet add package ManagedCode.LlmTestServer.Client
```

#### In an Aspire AppHost

`AddLlmTestServer()` starts the packaged .NET service; Docker is not required.

```csharp
using ManagedCode.LlmTestServer.Aspire;

var builder = DistributedApplication.CreateBuilder(args);
var llm = builder.AddLlmTestServer().WithApiKey("test-key");

builder.AddProject<Projects.Api>("api")
    .WithReference(llm)
    .WithEnvironment("OPENAI_ENDPOINT", llm.GetOpenAiEndpoint())
    .WithEnvironment("OPENAI_API_KEY", "test-key")
    .WaitFor(llm);

builder.Build().Run();
```

#### In a test

Add `ManagedCode.LlmTestServer.Aspire`, `ManagedCode.LlmTestServer.Client` and `Aspire.Hosting.Testing`, then
configure a response and call it:

```csharp
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;
using ManagedCode.LlmTestServer.Client;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

var builder = DistributedApplicationTestingBuilder.Create([]);
builder.AddLlmTestServer("llm").WithApiKey("test-key");
await using var app = await builder.BuildAsync();
await app.StartAsync();
await app.ResourceNotifications.WaitForResourceHealthyAsync("llm");

var control = new LlmTestServerClient(app.CreateHttpClient("llm", "http"), "test-key");
await control.ConfigureAsync(config => config.RequireBearerToken("test-key").UseGpt41Mini()
    .UseChatScenario("greeting", scenario => scenario.WhenUserContains("hello").Responds("Hello from your test!")));
using var chat = control.CreateChatClient();
var reply = await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]); // "Hello from your test!"
```

Your application can use its official SDK against the same server. In the Aspire test above, use its allocated
endpoint and the same fixture key — for example with OpenAI:

```csharp
using System.ClientModel;
using OpenAI;
using OpenAI.Chat;

await control.ResetAsync(); // Reuse the configured fixture for this separate SDK example.
using var providerHttp = app.CreateHttpClient("llm", "http");
var openAiChat = new ChatClient("gpt-4.1-mini", new ApiKeyCredential("test-key"),
    new OpenAIClientOptions { Endpoint = new Uri(providerHttp.BaseAddress!, "openai/v1") });
ChatCompletion completion = await openAiChat.CompleteChatAsync([new UserChatMessage("hello")]);
Console.WriteLine($"{completion.Usage.InputTokenCount} input tokens, " +
    $"{completion.Usage.InputTokenDetails.CachedTokenCount} from cache");
```

You can also embed the server and dashboard in your own ASP.NET Core host — see
[hosting options](docs/Reference/Installation.md#aspnet-core-startup).

## What you can do

| Capability | What it covers | Guide |
| --- | --- | --- |
| Chat scenarios | Contains or exact prompt matching, queued replies, streaming chunks, delays, scripted errors, per-scenario keys | [Configuration](docs/Reference/Configuration.md) |
| Tool calls and structured output | Function calls validated against your schemas, JSON responses, well-known tools, tool failures | [Tools](docs/Features/ToolAndStructuredFixtures.md) · [Well-known tools](docs/Features/WellKnownFunctionTools.md) · [Tool faults](docs/Features/ToolFaultFixtures.md) |
| Multi-turn conversations | Typed step graphs with branches, tool-result checks and terminal states | [Conversation DSL](docs/Features/ConversationDSL.md) |
| Streaming | Provider-native SSE and NDJSON streams with usage where the provider reports it, plus delivery faults | [Stream delivery faults](docs/Features/StreamDeliveryFaults.md) |
| Failure paths | 429 rate limits and content filters on every provider family | [Fault simulation](docs/Reference/Configuration.md#fault-simulation) |
| More than text | Embeddings, images, speech, transcription, translation and video jobs | [Modalities](docs/Reference/Configuration.md#modalities) |
| Native provider tools | OpenAI web search, file search and code; Anthropic web search; Responses continuation | [OpenAI web search](docs/Features/OpenAiNativeWebSearch.md) · [File/code](docs/Features/OpenAiNativeFileCode.md) · [Anthropic](docs/Features/AnthropicNativeWebSearch.md) · [Continuation](docs/Features/ResponsesContinuation.md) |
| Parallel tests | Isolated sessions with their own configuration, queues, cache and journal | [Test sessions](docs/Features/TestSessions.md) |
| Verification | Query and export the request journal, verify counts, order and tool arguments, fix IDs and clocks | [Verification and determinism](docs/Features/RequestVerificationAndDeterminism.md) |
| Native decision APIs | TypeSafe Jev, Kev (System One), Cloudflare Clef, OpenAI Decisions | [Decision contracts](docs/Features/DecisionModels.md) · [Jev 1.14](docs/Features/Jev114.md) |
| Official SDKs | OpenAI, Azure OpenAI, Foundry, Anthropic, Google Gen AI, Bedrock and `Microsoft.Extensions.AI` | [Clients and SDKs](docs/Reference/Clients.md) · [SDK compatibility](docs/Features/ProviderSdkCompatibility.md) |

Everything is a fixture you choose: nothing is hidden behind retries or randomness, and the server never calls a real
model.

## Dashboard tour

The dashboard at `/` is a request inbox: one row per provider call, with the full request, response, conversation,
payloads, stream chunks, tokens and runtime result one click away.

| Request overview | Conversation | Runtime diagnostics |
| --- | --- | --- |
| [![Request overview with conversation strip, tool-call card and highlighted arguments](docs/images/showcase-overview.jpg)](docs/images/showcase-overview.jpg) | [![Conversation with per-message token bars](docs/images/showcase-conversation.jpg)](docs/images/showcase-conversation.jpg) | [![Runtime diagnostics with assertion and fixture charts](docs/images/admin-dashboard-runtime.jpg)](docs/images/admin-dashboard-runtime.jpg) |
| What was sent, what came back, and what it cost in tokens. | Each turn with its role and token share. | Fixtures, provider routes, assertions and the event timeline. |

## Providers

Each provider family lives under its own namespace and keeps its native request and response shapes.

| Provider | Namespace | Provider | Namespace |
| --- | --- | --- | --- |
| OpenAI | `/openai` | Mistral | `/mistral` |
| Azure OpenAI | `/azure-openai` | Ollama | `/ollama` |
| Microsoft Foundry | `/microsoft-foundry` | Cohere | `/cohere` |
| Anthropic | `/anthropic` | Amazon Bedrock | `/bedrock` |
| Gemini | `/gemini` | OpenRouter | `/openrouter` |
| Groq | `/groq` | DeepSeek | `/deepseek` |
| Perplexity | `/perplexity` | TypeSafe Jev / Kev (System One) | `/systemone` |
| Cloudflare Clef / Clef Flash | `/cloudflare` | | |

Operation coverage differs by provider and grows over time. A provider being listed does not mean every API of that
provider is implemented — check the [operation coverage](docs/Features/ProviderApiContractCoverage.md) and
[routes reference](docs/Reference/ProvidersAndRoutes.md) for the exact contracts.

## TypeSafe and Jev

**TypeSafe is the provider; Jev is its model family. System One is the native API.**
Use `ManagedCode.LlmTestServer.SystemOne` for its request and response contracts, and
`ManagedCode.LlmTestServer.Client` for `control.CreateSystemOneClient()`.
The native server route is `POST /systemone/v1/systemone`.

Configure deterministic answers for predicates, choices, scores and Jev 1.14 bounding boxes.
JSON image assets and multipart uploads are supported with explicit image opt-in.
Kev uses the same protocol package with its own compatibility profile.

[Configure and call Jev from a test](docs/Reference/Clients.md#typesafe-jev-through-system-one) ·
[Jev 1.14 images and limits](docs/Features/Jev114.md) ·
[Native decision fixtures](docs/Features/DecisionModels.md)

## Packages

Start with **Aspire** for orchestration, **Client** for test setup, or **Hosting** to embed the server.
Each badge shows the latest published NuGet version.

| Package | NuGet | Use it for |
| --- | --- | --- |
| [`ManagedCode.LlmTestServer.Aspire`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Aspire) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Aspire.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Aspire) | Start the server from an Aspire AppHost or integration test. |
| [`ManagedCode.LlmTestServer.Client`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Client) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Client.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Client) | Configure fixtures, reset state and verify recorded requests. |
| [`ManagedCode.LlmTestServer.Hosting`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Hosting) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Hosting.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Hosting) | Host the server and Blazor dashboard in your ASP.NET Core app. |
| [`ManagedCode.LlmTestServer`](https://www.nuget.org/packages/ManagedCode.LlmTestServer) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer) | Build on the provider-neutral runtime, scenarios and assertions. |

Provider packages contain protocol contracts and compatibility profiles. Choose the provider your app uses;
see [operation coverage](docs/Features/ProviderApiContractCoverage.md) for supported APIs.

| Provider package | NuGet | Provider |
| --- | --- | --- |
| [`ManagedCode.LlmTestServer.OpenAI`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.OpenAI) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.OpenAI.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.OpenAI) | OpenAI |
| [`ManagedCode.LlmTestServer.AzureOpenAI`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.AzureOpenAI) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.AzureOpenAI.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.AzureOpenAI) | Azure OpenAI |
| [`ManagedCode.LlmTestServer.Foundry`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Foundry) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Foundry.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Foundry) | Microsoft Foundry |
| [`ManagedCode.LlmTestServer.Anthropic`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Anthropic) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Anthropic.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Anthropic) | Anthropic |
| [`ManagedCode.LlmTestServer.Gemini`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Gemini) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Gemini.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Gemini) | Google Gemini |
| [`ManagedCode.LlmTestServer.Groq`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Groq) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Groq.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Groq) | Groq |
| [`ManagedCode.LlmTestServer.Mistral`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Mistral) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Mistral.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Mistral) | Mistral |
| [`ManagedCode.LlmTestServer.Ollama`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Ollama) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Ollama.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Ollama) | Ollama |
| [`ManagedCode.LlmTestServer.Cohere`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cohere) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Cohere.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cohere) | Cohere |
| [`ManagedCode.LlmTestServer.Bedrock`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Bedrock) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Bedrock.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Bedrock) | Amazon Bedrock |
| [`ManagedCode.LlmTestServer.OpenRouter`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.OpenRouter) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.OpenRouter.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.OpenRouter) | OpenRouter |
| [`ManagedCode.LlmTestServer.DeepSeek`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.DeepSeek) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.DeepSeek.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.DeepSeek) | DeepSeek |
| [`ManagedCode.LlmTestServer.Perplexity`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Perplexity) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Perplexity.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Perplexity) | Perplexity |
| [`ManagedCode.LlmTestServer.SystemOne`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.SystemOne) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.SystemOne.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.SystemOne) | TypeSafe Jev / Kev — native System One API |
| [`ManagedCode.LlmTestServer.Cloudflare`](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cloudflare) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Cloudflare.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cloudflare) | Cloudflare Clef / Clef Flash |

## Learn more

- [Installation and hosting](docs/Reference/Installation.md) — Docker, Aspire and ASP.NET Core hosting
- [Configuration and scenarios](docs/Reference/Configuration.md) — models, scenarios, modalities, faults, assertions
- [Clients and SDKs](docs/Reference/Clients.md) — control client, official SDKs, `Microsoft.Extensions.AI`, TypeSafe client
- [Token usage and prompt cache](docs/Reference/TokenUsageAndPromptCache.md) and [dashboard analytics](docs/Features/DashboardAnalytics.md)
- [Providers, packages and routes](docs/Reference/ProvidersAndRoutes.md) and [operation coverage](docs/Features/ProviderApiContractCoverage.md)
- [Architecture](docs/Architecture.md), [test inventory](docs/Testing/AcceptanceCriteriaTestInventory.md)

## License

[MIT](LICENSE)
