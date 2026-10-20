# LLM Test Server

[![CI](https://github.com/managedcode/LlmTestServer/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/managedcode/LlmTestServer/actions/workflows/ci.yml)
[![Release](https://github.com/managedcode/LlmTestServer/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/managedcode/LlmTestServer/actions/workflows/release.yml)
[![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer)
[![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer)
[![License](https://img.shields.io/github/license/managedcode/LlmTestServer.svg?style=flat-square)](LICENSE)

**Deterministic LLM API mocking for .NET and Aspire.**

`ManagedCode.LlmTestServer` is a local mock server for applications that call LLM APIs.
It gives tests a local provider-compatible server that can be hosted by Aspire,
scripted with explicit scenarios, and called through regular HTTP, official SDKs,
or `Microsoft.Extensions.AI`.

The hosted surface is provider-namespaced. OpenAI-compatible routes live under
`/openai`, Azure OpenAI under `/azure-openai`, Microsoft Foundry under
`/microsoft-foundry`, Anthropic Messages under `/anthropic`, and every other
provider family uses its own namespace. Provider packages carry doc-backed API
contracts, and `ManagedCode.LlmTestServer.Hosting` only claims routes that are mapped by
`MapLlmTestServer()` and covered by tests.

Use it when a test needs to prove what an application sends to an LLM provider,
script deterministic provider responses, exercise retry/error/model-routing paths,
or assert that no unexpected LLM calls occurred. When a bearer token is configured,
provider endpoints and runtime configuration/assertion control endpoints require it.
Test-session lifecycle uses the credentials described in the [session guide](docs/Features/TestSessions.md).
Branching tool workflows use the [typed conversation DSL](docs/Features/ConversationDSL.md).

## First passing test

In a .NET 10 TUnit test project, add `ManagedCode.LlmTestServer.Aspire`, `ManagedCode.LlmTestServer.Client`
and `Aspire.Hosting.Testing`. This starts the packaged server without Docker, configures a response, calls it
through `IChatClient` and verifies the recorded request:

```csharp
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;
using ManagedCode.LlmTestServer.Client;
using Microsoft.Extensions.AI;

public sealed class AiIntegrationTests
{
    [Test]
    public async Task ApplicationReceivesScriptedResponse(CancellationToken cancellationToken)
    {
        var builder = DistributedApplicationTestingBuilder.Create([]);
        builder.AddLlmTestServer("llm-test-server").WithApiKey("test-key");
        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("llm-test-server", cancellationToken);

        using var http = app.CreateHttpClient("llm-test-server", "http");
        var control = new LlmTestServerClient(http, "test-key");
        await control.ConfigureAsync(config => config.RequireBearerToken("test-key").UseGpt41Mini()
            .UseChatScenario("greeting", scenario => scenario.WhenUserContains("hello").Responds("Hello from your test!")),
            cancellationToken);
        using var chat = control.CreateChatClient();
        var response = await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: cancellationToken);

        await Assert.That(response.Text).IsEqualTo("Hello from your test!");
        await Assert.That((await control.GetAssertionsAsync(cancellationToken)).Matched).IsEqualTo(1);
    }
}
```

See [provider operation coverage](docs/Features/ProviderApiContractCoverage.md) for the exact supported contracts.

## Parallel tests on a shared server

Create a separate session for each test. Each one owns its configuration, response queues, history, prompt cache,
fault counters, video jobs and request journal. Disposal removes its server state; the shared transport stays usable.

```csharp
await using var session = await control.CreateSessionAsync(config => config
    .RequireBearerToken("test-key")
    .UseChatScenario("isolated", scenario => scenario.Responds("This test's response")));
using var chat = session.Control.CreateChatClient();
var response = await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);
```

Use `session.HttpClient` as the transport for official provider SDKs. See [test sessions](docs/Features/TestSessions.md)
for credentials, capacity limits, configure/reset semantics and HTTP routes.

## Jev / TypeSafe System One

Jev is supported through its native System One API, including Jev 1.14 image assets
and bounding boxes. The emulator accepts JSON base64 and multipart PNG/JPEG uploads,
structured instructions and criteria, mixed `noul`/`choice`/`score`/`box` batches,
native confidence, usage and provider-resolved model versions. Image and box requests
require `TypeSafe-Danger-Zone-Images: true`; long-context requests can explicitly send
`TypeSafe-Danger-Zone-Long-Context: true`. It uses deterministic fixtures and never
calls a production model or estimates image tokens.

- Native test-server route: `POST /systemone/v1/systemone`.
- Contracts: `ManagedCode.LlmTestServer.SystemOne`.
- Typed client: `LlmTestServerClient.CreateSystemOneClient()` from `ManagedCode.LlmTestServer.Client`.
- Register `jev-1.14.0` or `jev-preview` with `AddDecisionModel(id, LlmTestServerDecisionProvider.TypeSafe)`.
- [Jev 1.14 guide and limits](docs/Features/Jev114.md), [native decision fixtures](#native-decision-api-fixtures),
  [official images API](https://docs.typesafe.ai/images).

## Request Dashboard

The Blazor dashboard at `/` is an operator-focused request inbox. It keeps provider,
model, status, latency, token/cache usage, multi-turn context, sanitized wire payloads,
stream chunks, and the deterministic runtime result together for each request.

![LLM Test Server provider request dashboard showing a selected multi-turn OpenAI request](https://raw.githubusercontent.com/managedcode/LlmTestServer/main/docs/images/admin-dashboard-overview.jpg)

| Conversation timeline | Runtime diagnostics |
| --- | --- |
| ![Chronological multi-turn request and provider response](https://raw.githubusercontent.com/managedcode/LlmTestServer/main/docs/images/admin-dashboard-conversation.jpg) | ![Fluent UI runtime diagnostics dialog with fixtures, provider APIs, assertions, and events](https://raw.githubusercontent.com/managedcode/LlmTestServer/main/docs/images/admin-dashboard-runtime.jpg) |

## Packages

Package links open the latest stable release on NuGet; badges update automatically.

| Package | Latest version | Downloads | Description |
| --- | --- | --- | --- |
| [ManagedCode.LlmTestServer](https://www.nuget.org/packages/ManagedCode.LlmTestServer) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer) | Provider-neutral scenario runtime and assertion state. |
| [ManagedCode.LlmTestServer.OpenAI](https://www.nuget.org/packages/ManagedCode.LlmTestServer.OpenAI) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.OpenAI.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.OpenAI) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.OpenAI.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.OpenAI) | OpenAI-compatible wire contracts. |
| [ManagedCode.LlmTestServer.AzureOpenAI](https://www.nuget.org/packages/ManagedCode.LlmTestServer.AzureOpenAI) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.AzureOpenAI.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.AzureOpenAI) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.AzureOpenAI.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.AzureOpenAI) | Azure OpenAI compatibility profile and wire contracts. |
| [ManagedCode.LlmTestServer.Foundry](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Foundry) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Foundry.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Foundry) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Foundry.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Foundry) | Microsoft Foundry compatibility profile and wire contracts. |
| [ManagedCode.LlmTestServer.Anthropic](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Anthropic) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Anthropic.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Anthropic) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Anthropic.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Anthropic) | Anthropic Messages compatibility profile and wire contracts. |
| [ManagedCode.LlmTestServer.Gemini](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Gemini) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Gemini.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Gemini) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Gemini.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Gemini) | Gemini compatibility profile and wire contracts. |
| [ManagedCode.LlmTestServer.Groq](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Groq) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Groq.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Groq) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Groq.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Groq) | Groq compatibility profile and wire contracts. |
| [ManagedCode.LlmTestServer.Mistral](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Mistral) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Mistral.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Mistral) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Mistral.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Mistral) | Mistral compatibility profile and wire contracts. |
| [ManagedCode.LlmTestServer.Ollama](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Ollama) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Ollama.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Ollama) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Ollama.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Ollama) | Ollama compatibility profile and wire contracts. |
| [ManagedCode.LlmTestServer.Cohere](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cohere) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Cohere.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cohere) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Cohere.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cohere) | Cohere compatibility profile and wire contracts. |
| [ManagedCode.LlmTestServer.Bedrock](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Bedrock) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Bedrock.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Bedrock) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Bedrock.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Bedrock) | Amazon Bedrock compatibility profile and wire contracts. |
| [ManagedCode.LlmTestServer.OpenRouter](https://www.nuget.org/packages/ManagedCode.LlmTestServer.OpenRouter) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.OpenRouter.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.OpenRouter) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.OpenRouter.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.OpenRouter) | OpenRouter compatibility profile and wire contracts. |
| [ManagedCode.LlmTestServer.DeepSeek](https://www.nuget.org/packages/ManagedCode.LlmTestServer.DeepSeek) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.DeepSeek.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.DeepSeek) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.DeepSeek.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.DeepSeek) | DeepSeek compatibility profile and wire contracts. |
| [ManagedCode.LlmTestServer.Perplexity](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Perplexity) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Perplexity.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Perplexity) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Perplexity.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Perplexity) | Perplexity compatibility profile and wire contracts. |
| [ManagedCode.LlmTestServer.SystemOne](https://www.nuget.org/packages/ManagedCode.LlmTestServer.SystemOne) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.SystemOne.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.SystemOne) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.SystemOne.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.SystemOne) | Native Jev/Kev SystemOne decision contracts. |
| [ManagedCode.LlmTestServer.Cloudflare](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cloudflare) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Cloudflare.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cloudflare) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Cloudflare.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cloudflare) | Cloudflare Workers AI Clef/flash decision envelopes and validation. |
| [ManagedCode.LlmTestServer.Hosting](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Hosting) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Hosting.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Hosting) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Hosting.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Hosting) | ASP.NET Core endpoint mapping. |
| [ManagedCode.LlmTestServer.Client](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Client) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Client.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Client) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Client.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Client) | Control client plus `IChatClient`, `IEmbeddingGenerator<string, Embedding<float>>`, and `IImageGenerator` implementations. |
| [ManagedCode.LlmTestServer.Aspire](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Aspire) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Aspire.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Aspire) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Aspire.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Aspire) | Aspire AppHost extension methods. |

## Current Functionality

This is the currently implemented behavior, not future intent:

| Surface | What works now |
| --- | --- |
| Hosting | `builder.Services.AddLlmTestServer(...)` registers one deterministic runtime; `app.MapLlmTestServer()` maps the Blazor server-side browser panel at `/`, JSON control endpoints under `/admin-api`, and provider-namespaced HTTP routes. |
| Runtime configuration | Configure at host startup, through `LlmTestServerClient.ConfigureAsync(...)`, or through `POST /admin-api/configure`. Configuration replaces the current runtime state and resets assertions, scenario positions, fault counters, and prompt-cache entries. |
| Reset | `ResetAsync()` and `POST /admin-api/reset` keep the current models/scenarios/fixtures but clear assertion events, scenario response positions, rate-limit counters, and prompt-cache entries. |
| Models | Default models use official fixture IDs for chat, embeddings, images, audio, and video. Typed helpers register broader OpenAI model families, and `AddModel(...)` supports custom deployment or provider IDs. Model kind is enforced on every route. |
| Chat scenarios | Contains matching, exact prompt matching, queued responses, streaming chunks, scripted errors, delayed responses, cancellation-safe queues, and scenario-specific bearer tokens are implemented. |
| Modalities | Chat, embeddings, image generation/edit/variation, audio speech/transcription/translation, OpenAI/Azure video, Gemini long-running video/file output, and Bedrock text/embedding/image invoke shapes return deterministic fixtures. |
| Provider matrix | OpenAI, Azure OpenAI, Microsoft Foundry, Anthropic, Gemini, Groq, Mistral, Ollama, Cohere, Amazon Bedrock, OpenRouter, DeepSeek, and Perplexity are exposed under explicit provider namespaces. Each hosted operation is represented in a provider `ApiContract` and behavior evidence tests. |
| SDK clients | Official OpenAI, Azure OpenAI, Azure AI Inference / Foundry, Amazon Bedrock, and `Microsoft.Extensions.AI` clients can call the hosted provider routes. The client package also creates control, chat, embedding, image, and audio clients with the same bearer token. |
| Auth | A global bearer token protects provider and `/admin-api/*` endpoints. Anthropic `x-api-key` and Azure `api-key` headers are accepted as provider-native ways to pass the same token. Scenarios can add their own bearer-token requirement. |
| Fault simulation | Provider-neutral rate-limit and content-filter simulation flow through every provider family and return provider-shaped errors. Scripted per-scenario failures remain available for exact test paths. |
| Assertions | `/admin-api/assertions` returns counters plus every runtime event with request preview, response/error text, model id, scenario id, event kind, and deterministic usage. |
| Usage accounting | Input, output, reasoning, total, cached-input, and cache-write token values are computed deterministically and projected into provider-specific response envelopes when that provider surface documents usage fields. |
| Browser panel | `/` shows models/fixtures, actions, control and provider endpoint groups, grouped runtime events, captured requests, captured responses, counters, and per-event usage. It auto-refreshes every 15 seconds using Blazor component state and `StateHasChanged()`. |
| Aspire | `builder.AddLlmTestServer()` starts the packaged .NET service without Docker by default; `WithApiKey(...)` wires auth, `GetHttpEndpoint()` and provider-specific endpoint helpers expose URLs, and `AddLlmTestServerContainer()` is an explicit container opt-in. |

## Provider Packages

Provider packages keep vendor-specific protocol and compatibility metadata out of the provider-neutral runtime. The package surface exists now so applications can select a provider family explicitly; provider-specific DTOs and endpoint mappings are added inside each package as support grows.

| Package | Provider ID | Protocol family | Primary hosted route |
| --- | --- | --- | --- |
| `ManagedCode.LlmTestServer.OpenAI` | `openai` | OpenAI | `/openai/v1` |
| `ManagedCode.LlmTestServer.AzureOpenAI` | `azure-openai` | Azure OpenAI | `/azure-openai/openai/deployments/{deployment}/chat/completions` |
| `ManagedCode.LlmTestServer.Foundry` | `microsoft-foundry` | Microsoft Foundry | `/microsoft-foundry/models/chat/completions` |
| `ManagedCode.LlmTestServer.Anthropic` | `anthropic` | Anthropic Messages | `/anthropic/v1/messages` |
| `ManagedCode.LlmTestServer.Gemini` | `gemini` | Gemini | `/gemini/v1beta/models/{model}:generateContent` |
| `ManagedCode.LlmTestServer.Groq` | `groq` | Groq | `/groq/openai/v1/chat/completions` |
| `ManagedCode.LlmTestServer.Mistral` | `mistral` | Mistral | `/mistral/v1/chat/completions` |
| `ManagedCode.LlmTestServer.Ollama` | `ollama` | Ollama | `/ollama/api/chat` |
| `ManagedCode.LlmTestServer.Cohere` | `cohere` | Cohere | `/cohere/v2/chat` |
| `ManagedCode.LlmTestServer.Bedrock` | `bedrock` | Amazon Bedrock | `/bedrock/model/{modelId}/converse` |
| `ManagedCode.LlmTestServer.OpenRouter` | `openrouter` | OpenRouter | `/openrouter/api/v1/chat/completions` |
| `ManagedCode.LlmTestServer.DeepSeek` | `deepseek` | DeepSeek | `/deepseek/v1/chat/completions` |
| `ManagedCode.LlmTestServer.Perplexity` | `perplexity` | Perplexity | `/perplexity/v1/sonar` |
| `ManagedCode.LlmTestServer.SystemOne` | `systemone` | Jev / TypeSafe and Kev native decisions | `/systemone/v1/systemone` |
| `ManagedCode.LlmTestServer.Cloudflare` | `cloudflare` | Clef / Clef Flash native decisions | `/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef` |

Each provider profile also carries a doc-backed `ApiContract` with official documentation links, retrieval date, API version or header requirements, documented operations, streaming support, and whether `ManagedCode.LlmTestServer.Hosting` currently maps the operation.

Use a provider profile when code needs a stable package-level declaration without starting a host:

```csharp
using ManagedCode.LlmTestServer.AzureOpenAI;

var profile = AzureOpenAiCompatibility.Profile;
Console.WriteLine(profile.Id); // azure-openai
```

## OpenAI SDK

The official OpenAI .NET SDK expects its `Endpoint` to be the API root. Use
`/openai/v1` for this Test Server, or `llmTestServer.GetOpenAiEndpoint()` from the Aspire
package when wiring an AppHost resource.

```csharp
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

var chat = new ChatClient(
    "gpt-4.1-mini",
    new ApiKeyCredential("test-key"),
    new OpenAIClientOptions
    {
        Endpoint = new Uri("http://localhost:5000/openai/v1"),
    });

await foreach (var update in chat.CompleteChatStreamingAsync(
                   [new UserChatMessage("What is the invoice total?")]))
{
    Console.Write(update.ContentUpdate.Count > 0 ? update.ContentUpdate[0].Text : string.Empty);
}
```

## Azure OpenAI And Foundry SDKs

The OpenAI v1 API is available at `/azure-openai/openai/v1/` and `/microsoft-foundry/openai/v1/`, with `chat/completions`, `responses` and `embeddings`. Point the official OpenAI client endpoint at the corresponding base URL and supply the configured API key. These v1 routes accept bearer or `api-key` authentication and need no dated `api-version` query parameter. Responses supports ordinary JSON and documented SSE events. See the [September 2026 API review](docs/Features/ProviderApiReview20260907.md) for the verified SDK versions and provider contracts.

Azure OpenAI deployment routes use the deployment name as the Test Server model id. API keys sent through the official SDK's `api-key` header are accepted anywhere a bearer token would be accepted.

```csharp
using Azure.AI.OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using System.ClientModel;

var azure = new AzureOpenAIClient(
    new Uri("http://localhost:5000/azure-openai/"),
    new ApiKeyCredential("test-key"));

ChatClient chat = azure.GetChatClient("gpt-4.1-mini");
EmbeddingClient embeddings = azure.GetEmbeddingClient("text-embedding-3-small");

var answer = await chat.CompleteChatAsync(
[
    new UserChatMessage("What is the invoice total?"),
]);

var vector = await embeddings.GenerateEmbeddingAsync("invoice");
```

Microsoft Foundry / Azure AI Inference clients call the `/microsoft-foundry/chat/completions` and `/microsoft-foundry/embeddings` routes. The inference routes require `api-version=2024-05-01-preview`, which the official SDK supplies. Set `Model` to the model id configured in LLM Test Server.

```csharp
using Azure;
using Azure.AI.Inference;

var endpoint = new Uri("http://localhost:5000/microsoft-foundry/");
var credential = new AzureKeyCredential("test-key");

var chat = new ChatCompletionsClient(endpoint, credential);
var embeddings = new EmbeddingsClient(endpoint, credential);

var completion = await chat.CompleteAsync(
    new ChatCompletionsOptions(
    [
        new ChatRequestUserMessage("What is the invoice total?"),
    ])
    {
        Model = "gpt-4.1-mini",
    });

var embedding = await embeddings.EmbedAsync(
    new EmbeddingsOptions(["invoice"])
    {
        Model = "text-embedding-3-small",
    });
```

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

## ASP.NET Core Startup

The simplest host config is one chat scenario:

```csharp
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLlmTestServer(options =>
{
    options.AddChatScenario(
        "blue-whale",
        scenario => scenario
            .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
            .WhenUserContains("largest animal")
            .Responds("blue whale", "blue ", "whale"));
});

var app = builder.Build();
app.MapLlmTestServer();
app.Run();
```

A fuller config can advertise your application's real model names while still serving deterministic fixtures:

```csharp
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLlmTestServer(options =>
{
    options.RequireBearerToken("test-key");

    options.AddDefaultOpenAiModels();

    options.WithDefaultEmbeddingVector(0.125f, 0.25f, 0.5f, 1.0f);
    options.WithDefaultAudio(File.ReadAllBytes("fixtures/ok.wav"), "audio/wav");

    options.AddChatScenario(
        "invoice-total",
        scenario => scenario
            .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
            .WhenUserContains("invoice total")
            .Responds(
                "{\"total\":42.50}",
                "{\"total\":",
                "42.50",
                "}"));

    options.AddChatScenario(
        "provider-rate-limit",
        scenario => scenario
            .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
            .WhenUserContains("rate limit")
            .Fails(429, "rate_limit_exceeded", "Scripted rate limit from LLM Test Server."));

    options.AddChatScenario(
        "slow-response",
        scenario => scenario
            .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
            .WhenUserContains("slow")
            .Responds("done")
            .DelaysBy(250));
});

var app = builder.Build();
app.MapLlmTestServer();
app.Run();
```

## Aspire AppHost

Install the Aspire integration package in the AppHost:

```bash
dotnet add package ManagedCode.LlmTestServer.Aspire
```

Then add the package-owned Test Server resource directly:

```csharp
using ManagedCode.LlmTestServer.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

var llmTestServer = builder
    .AddLlmTestServer()
    .WithApiKey("test-key");

builder
    .AddProject<Projects.Consumer>("consumer")
    .WithReference(llmTestServer)
    .WithEnvironment("LLM_PROVIDER_ENDPOINT", llmTestServer.GetHttpEndpoint())
    .WithEnvironment("OPENAI_ENDPOINT", llmTestServer.GetOpenAiEndpoint())
    .WaitFor(llmTestServer);

builder.Build().Run();
```

`AddLlmTestServer()` creates a `LlmTestServerResource` backed by the packaged .NET LLM Test Server service executable and exposes its `http` endpoint. It does not require a consumer service project reference, generated `Projects.*` metadata type, project path, Docker, or a container runtime. Consumer resources should reference the Test Server resource, wait for it, and use `llmTestServer.GetHttpEndpoint()` when they need the provider-compatible base URL. `.WithApiKey("test-key")` sets `LlmTestServer:RequiredBearerToken` so both provider endpoints and `/admin-api/*` control endpoints require the same bearer token.

Use `AddLlmTestServerContainer()` only when you explicitly want a container-backed resource, for example for a deployment or container-runtime smoke test. The container mode uses a `ghcr.io/managedcode/llm-test-server` image tagged with the same release version as the installed `ManagedCode.LlmTestServer.Aspire` package; it is not the default local Aspire path.

## Control Panel And Token Usage

Open the Test Server resource endpoint from the Aspire dashboard to inspect the running configuration at `/`. The browser admin panel is rendered by Blazor server-side rendering from `ManagedCode.LlmTestServer.Hosting`. If the resource was configured with `.WithApiKey("test-key")` or `RequireBearerToken("test-key")`, enter the same token in the control panel before refreshing.

The panel reads `/admin-api/models` and `/admin-api/assertions`. It shows advertised models, actions, control endpoint groups, provider endpoint groups, grouped runtime events, captured requests, captured responses, assertion counters, and deterministic token usage. Auto-refresh is enabled by default and runs every 15 seconds on the server-side Blazor component; there is no client-side polling script and no visible countdown.

Token usage is counted with the repo-owned tiktoken-compatible counter and reported both as summary totals and per runtime event with `inputTokens`, `cachedInputTokens`, `cacheCreationInputTokens`, `outputTokens`, `reasoningTokens`, and `totalTokens`. Provider response envelopes also receive the same deterministic usage values: OpenAI-compatible chat and Responses usage including reasoning-token and prompt-cache details when configured, Anthropic sync messages and streaming `message_start`/`message_delta`, Gemini `usageMetadata` including long-running video operation results, Cohere chat usage, Ollama prompt/eval counts, and Bedrock Converse usage.

For reasoning-capable fixture models, configure a deterministic reasoning-token count on the model. LLM Test Server includes those tokens in `outputTokens` and exposes the split in `reasoningTokens`; OpenAI-compatible responses also include `completion_tokens_details.reasoning_tokens` or `output_tokens_details.reasoning_tokens`.

```csharp
options.AddGpt5Nano(reasoningTokens: 128);
```

## Prompt Cache Accounting

Prompt cache accounting is deterministic runtime state for testing provider usage handling. LLM Test Server does not call a real cache service and does not hide prompt text; it calculates repeated prompt-prefix usage from the requests that reach the runtime.

Cache entries are created only for successful chat or Responses requests on provider surfaces with a cache policy. A failed, unmatched, unauthorized, cancelled, unknown-model, or scripted-error request does not create a cache entry. `ConfigureAsync(...)` and `ResetAsync()` both clear prompt-cache entries so each test can start from a known cache state.

The cache key includes provider cache policy, model id, an optional provider cache key or session id, the rounded cacheable token count, and the cumulative message prefix. The first successful request with a cacheable prefix reports cache-write tokens and zero cache-read tokens. Ollama exposes cache reads only and does not report cache-write tokens. A later successful request with the same cache key reports cache-read tokens and zero cache-write tokens for the already cached prefix.

Provider cache thresholds follow the current runtime policy:

| Policy | Provider routes | Minimum cacheable tokens | Increment |
| --- | --- | ---: | ---: |
| OpenAI-compatible | OpenAI, Groq, Azure OpenAI, Microsoft Foundry, Perplexity, Responses API routes that use the OpenAI-compatible shape | 1024 | 128 |
| OpenRouter | OpenRouter chat and Responses routes; `session_id` or `x-session-id` can become the cache key when no explicit prompt cache key is sent | 1024 | 128 |
| DeepSeek | DeepSeek chat routes | 1024 | 128 |
| Anthropic | Anthropic Messages requests that include `cache_control` markers | 1024 | 128 |
| Mistral | Mistral chat routes | 64 | 64 |
| Gemini | Gemini generateContent routes | 2048 | 128 |
| Ollama | Ollama chat routes; deterministic cache reads only | 1 | 1 |
| Bedrock | Bedrock Converse requests that include `cachePoint` or `cache_control` markers | 1024 | 128 |

Cache accounting is a breakdown of input usage, not an extra billable token bucket. `totalTokens` remains `inputTokens + outputTokens`; `cachedInputTokens` and `cacheCreationInputTokens` explain how much of the input was read from, or written to, the deterministic prompt cache.

Provider envelopes expose the same runtime usage through provider-native field names:

| Provider surface | Cache fields returned |
| --- | --- |
| OpenAI, Groq, Azure OpenAI, Microsoft Foundry, Mistral, Perplexity chat | `usage.prompt_tokens_details.cached_tokens` |
| OpenAI, Groq, Azure OpenAI, Microsoft Foundry, Perplexity Responses | `usage.input_tokens_details.cached_tokens` |
| OpenRouter chat and Responses | `cached_tokens` plus `cache_write_tokens` in the matching prompt/input token details object |
| DeepSeek chat | `usage.prompt_cache_hit_tokens` and `usage.prompt_cache_miss_tokens` |
| Anthropic Messages | `usage.cache_creation_input_tokens` and `usage.cache_read_input_tokens` when cache usage exists |
| Gemini generateContent | `usageMetadata.cachedContentTokenCount` |
| Ollama chat | `prompt_eval_cached_count`, reported on the final NDJSON chunk for streaming |
| Bedrock Converse and ConverseStream metadata | `usage.cacheReadInputTokens` and `usage.cacheWriteInputTokens` |

![LLM Test Server request dashboard showing per-request token and cache usage](docs/images/admin-dashboard-token-usage.png)

![LLM Test Server mobile request detail showing token, cache, conversation, and response data](docs/images/admin-dashboard-mobile-request-detail.png)

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

## Microsoft.Extensions.AI

Use the client package when you want tests to call the fake provider through normal `Microsoft.Extensions.AI` abstractions:

```csharp
using ManagedCode.LlmTestServer.Client;
using Microsoft.Extensions.AI;

using var httpClient = new HttpClient
{
    BaseAddress = new Uri("http://localhost:5000"),
};
httpClient.DefaultRequestHeaders.Authorization =
    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-key");

using IChatClient chatClient = new LlmTestServerChatClient(httpClient);

var response = await chatClient.GetResponseAsync(
[
    new ChatMessage(ChatRole.User, "What color is the largest animal?"),
]);

Console.WriteLine(response.Text);
```

When a bearer token is required, prefer creating the modality clients from the control client so the same token is applied consistently:

```csharp
var server = LlmTestServerClient.Create(httpClient, bearerToken: "test-key");

using IChatClient chatClient = server.CreateChatClient("docs-chat");
using IEmbeddingGenerator<string, Embedding<float>> embeddings =
    server.CreateEmbeddingGenerator("docs-embedding");
using IImageGenerator images = server.CreateImageGenerator("docs-image");
```

Streaming uses the same client:

```csharp
await foreach (var update in chatClient.GetStreamingResponseAsync(
    [new ChatMessage(ChatRole.User, "stream this")]))
{
    Console.Write(update.Text);
}
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

## Hosted Endpoint Families

Provider endpoints are explicitly namespaced. The table below shows the route
families and representative operations; the full method/path contract lives in
each provider profile's `ApiContract` and is guarded by
`ProviderApiContractTests`. See
[`docs/Features/ProviderApiContractCoverage.md`](docs/Features/ProviderApiContractCoverage.md)
for the current coverage rules and behavior-test inventory.

| Provider | Namespace | Representative hosted operations |
| --- | --- | --- |
| OpenAI | `/openai` | `/openai/v1/models`, `/openai/v1/decisions`, `/openai/v1/chat/completions`, `/openai/v1/responses`, `/openai/v1/embeddings`, `/openai/v1/images/*`, `/openai/v1/audio/*`, `/openai/v1/videos*` |
| Azure OpenAI | `/azure-openai` | `/azure-openai/openai/v1/{chat/completions,responses,embeddings}`, deployment chat/embeddings/images/audio, and `/azure-openai/openai/v1/video/generations/*` |
| Microsoft Foundry | `/microsoft-foundry` | `/microsoft-foundry/openai/v1/{chat/completions,responses,embeddings}`, `/microsoft-foundry/chat/completions`, `/microsoft-foundry/embeddings`, and `/microsoft-foundry/models/*` aliases |
| Anthropic | `/anthropic` | `/anthropic/v1/messages` |
| Gemini | `/gemini` | `/gemini/v1beta/models/{model}:generateContent`, streaming content, embeddings, long-running video operations, and generated files |
| Groq | `/groq` | `/groq/openai/v1/models`, chat completions, Responses, speech, transcription, and translation routes |
| Mistral | `/mistral` | `/mistral/v1/chat/completions`, `/mistral/v1/embeddings` |
| Ollama | `/ollama` | `/ollama/api/chat`, `/ollama/api/embed` |
| Cohere | `/cohere` | `/cohere/v2/chat`, `/cohere/v2/embed` |
| Amazon Bedrock | `/bedrock` | `/bedrock/model/{modelId}/converse`, converse stream, invoke, and invoke-with-response-stream |
| OpenRouter | `/openrouter` | `/openrouter/api/v1/models`, chat completions, Responses |
| DeepSeek | `/deepseek` | `/deepseek/models`, `/deepseek/v1/chat/completions` |
| Perplexity | `/perplexity` | `/perplexity/v1/sonar` |
| SystemOne (Jev/Kev) | `/systemone` | `/systemone/v1/systemone` |
| Cloudflare (Clef/flash) | `/cloudflare` | `/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef`, `clef-flash` |

The Blazor server-side rendered browser control panel lives at `/`. JSON control endpoints live under
`/admin-api`: `GET /admin-api/models`, `GET /admin-api/assertions`,
`POST /admin-api/configure`, and `POST /admin-api/reset`.

Audio speech returns a deterministic WAV fixture by default. When
`requiredBearerToken` is configured, all provider and control endpoints require
`Authorization: Bearer <token>`.

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

## Build And Test

```bash
dotnet restore ManagedCode.LlmTestServer.slnx
dotnet build ManagedCode.LlmTestServer.slnx --configuration Release --no-restore
dotnet test --project tests/ManagedCode.LlmTestServer.Tests/ManagedCode.LlmTestServer.Tests.csproj --configuration Release --no-build --verbosity normal
for project in src/*/*.csproj; do dotnet pack "$project" --configuration Release --no-build --output artifacts/packages; done
```

`global.json` opts `dotnet test` into `Microsoft.Testing.Platform` for .NET 10.

## Compatibility boundaries and verification

Tool calls and structured output use explicit deterministic fixtures: `CallsTool(...)`, `CallsTools(...)` and `RespondsJson(...)`. Provider adapters preserve tool history and streaming events; `LlmTestServerChatClient` supports the standard Microsoft.Extensions.AI function-invocation loop. Fixtures must satisfy tool selection, argument schemas and response schemas; mismatches return 409 without consuming the response. See [supported contracts and tests](docs/Features/ToolAndStructuredFixtures.md).

OpenAI chat streaming supports `stream_options.include_usage`; the control library's `IChatClient` carries token usage and terminal updates, including reasoning and cache details. Authentication failures and scripted errors before generation do not accrue output/reasoning tokens.

Video create operations retain completed fixture jobs. Retrieve/content/delete require a previously created ID, and reset or reconfiguration clears jobs. OpenAI fixtures return `status=completed` and `progress=100`; IDs remain unique within a run. Dataset-local scenario IDs are isolated, including concurrent cancellation rollback.

Prompt-cache storage is bounded to 4096 prefixes by default. Use `WithPromptCacheCapacity(capacity)` to change the limit; zero disables caching. Eviction is deterministic FIFO, and reset/configure clears the cache.

Legacy Azure deployment APIs accept the dated `api-version` selected automatically by the Azure SDK. Use the default `AzureOpenAIClientOptions` constructor, or omit options entirely; do not pin a version for the emulator. These routes require exactly one supported deployment API version: the documented `2024-10-21` GA or `2025-04-01-preview` contract. Missing, malformed, duplicate, and unknown versions are rejected, including valid-looking future dates. Azure/Foundry `/openai/v1` endpoints require no dated query parameter.

After restore/build, run `bash scripts/coverage.sh` for the production 90% coverage gate. After packing, run `python3 scripts/verify-package.py` for an isolated NuGet consumer, test-owned Aspire host and Chromium dashboard test; this requires Python 3 and PowerShell (`pwsh`). CI installs Chromium dependencies and saves coverage/browser evidence. `python3 scripts/test-release.py` verifies partial-release recovery locally without external publication. Release delivery is marked complete only after NuGet push and GitHub artifact attachment succeed.

## Native decision API fixtures

Native typed clients are available through `LlmTestServerClient.CreateSystemOneClient()`
and `LlmTestServerClient.CreateCloudflareDecisionClient(accountId)`. Clef accepts embedded
PNG/JPEG/WebP data URLs or base64 objects; Jev accepts PNG/JPEG reference assets as
JSON or multipart, with explicit image/long-context opt-ins. See the
[official-contract audit and supported limits](docs/Features/DecisionVisionAudit.md).


Jev, Kev, Cloudflare Clef/Clef Flash and OpenAI Decisions use explicit decision fixtures. The Test Server hosts their native JSON shapes under provider namespaces and does not contact production providers. Declare the provider profile explicitly; model names never select an API implicitly.

| Provider | Test Server POST route | Production POST endpoint | Model helper | Official contract |
| --- | --- | --- | --- | --- |
| TypeSafe Jev | `/systemone/v1/systemone` | `https://api.typesafe.ai/v1/systemone` | `AddJevLatest()` | [OpenAPI](https://api.typesafe.ai/openapi.json), [primitives](https://docs.typesafe.ai/primitives/choice) |
| Kev | `/systemone/v1/systemone` | `{kev-server}/v1/systemone` | `AddKevLatest()` | [native request models](https://github.com/jaredpalmer/kev/blob/main/kev/api.py), [server/auth](https://github.com/jaredpalmer/kev/blob/main/kev/serve.py) |
| Cloudflare Clef | `/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef` | `https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef` | `AddClef()` | [Clef API](https://developers.cloudflare.com/workers-ai/models/clef/), [REST envelope/auth](https://developers.cloudflare.com/workers-ai/get-started/rest-api/) |
| Cloudflare Clef Flash | `/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef-flash` | `https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef-flash` | `AddClefFlash()` | [Clef Flash API](https://developers.cloudflare.com/workers-ai/models/clef-flash/) |
| OpenAI Decisions | `/openai/v1/decisions` | `https://api.openai.com/v1/decisions` | `AddOpenAiDecisionModel("gpt-6-luna")` | [create API](https://developers.openai.com/api/reference/resources/decisions/methods/create/) |

Production APIs use `Authorization: Bearer <provider-token>`; Kev checks `KEV_API_KEY` only when configured and otherwise permits unauthenticated calls. For the Test Server, `RequireBearerToken` controls the expected test token and protects both provider routes and `/admin-api/*`. Tests do not need production credentials. Resolve the hosted base URL from the Aspire resource (`GetEndpoint("http")` / `CreateHttpClient(...)`), then append the Test Server route.

### SystemOne: Jev and Kev

Jev requires `model`, string/object/array `state`, and a nonempty question map. Kev accepts any JSON `state`, including numbers, booleans and null; instructions and criterion descriptions also accept arbitrary JSON. Kev defaults an omitted `model` to `kev-latest`; the Test Server enables that default only when `AddKevLatest()` explicitly registers the Kev profile. Binary questions use `type: "noul"`; optional `criteria` maps the native `true` and `false` descriptions. Choices use an opaque ID-to-description map. Scores use an ordered criteria array.

```json
{
  "model": "kev-latest",
  "state": { "message": "Please help quickly" },
  "questions": {
    "urgent": { "type": "noul", "instructions": "Is urgent?", "criteria": { "true": "needs prompt help", "false": "can wait" } },
    "route": { "type": "choice", "instructions": "Choose the queue", "criteria": { "fast": "urgent", "regular": "routine" } },
    "severity": { "type": "score", "instructions": "Assess severity", "criteria": ["low", "medium", "high"] }
  }
}
```

Use `model: "jev-latest"` for Jev. Native responses contain keyed `answers`: `noul`, or `choice`/`score` with `confidence` and `probabilities`; score `legend` keys are zero-based numeric strings. `usage` contains `input_tokens` and `output_tokens`. Scores are native ordinal values and can exceed one.

```csharp
using ManagedCode.LlmTestServer.Decisions;

options.AddKevLatest().AddDecisionScenario(new LlmTestServerDecisionScenario
{
    Id = "priority", ModelId = "kev-latest",
    Answers = new()
    {
        ["urgent"] = new() { Kind = LlmTestServerDecisionKind.Predicate, Probability = 0.95 },
        ["route"] = new() { Kind = LlmTestServerDecisionKind.Choice, Choice = "fast", Confidence = 0.9,
            Probabilities = new() { ["fast"] = 0.95, ["regular"] = 0.05 } },
        ["severity"] = new() { Kind = LlmTestServerDecisionKind.Score, Score = 1.75, Confidence = 0.625,
            Probabilities = new() { ["0"] = 0.05, ["1"] = 0.15, ["2"] = 0.8 } },
    },
    Usage = new() { InputTokens = 100, OutputTokens = 10, TotalTokens = 110 },
});
```

Kev score legends contain rendered strings, while Jev retains string/object/array criteria. Kev responses include `latency_ms`; `LlmTestServerDecisionScenario.Metadata` supplies deterministic latency and optional `Truncated`, `StateTokens`, `StateTokensUsed` fields. The route echoes or generates `x-typesafe-request-id`, including on errors. Invalid SystemOne request shapes return HTTP 422 with a native `detail` validation array; runtime/admission faults retain `detail` strings. Kev bearer failures include `WWW-Authenticate: Bearer`. This compatibility surface covers `/v1/systemone`; Kev demo `/permute` and `/separate` routes are outside its scope.

### Cloudflare Workers AI: Clef and Clef Flash

Send the same SystemOne question shape to the account/model route with body `model: "clef"` or `"clef-flash"` matching the route. Every question requires instructions. Binary `criteria.true` and `criteria.false` accept arbitrary JSON, including numbers, booleans and null; `criteria` itself must remain an object. State and instructions retain their documented string/object/array types, choice descriptions accept string/object/array/null, and score descriptions accept string/object/array. Responses wrap the native answer object in the Workers AI envelope:

```json
{
  "result": {
    "model": "clef",
    "answers": { "urgent": { "type": "noul", "noul": 0.95 } },
    "usage": { "input_tokens": 100, "output_tokens": 0 }
  },
  "success": true,
  "errors": [],
  "messages": []
}
```

Configure `AddClef()` / `AddClefFlash()` and fixtures with the matching short model ID; native surrounding model whitespace is accepted. Runtime/auth failures remain non-success HTTP responses and use `success: false` with native numeric `errors[].code`. Explicit numeric scenario error codes are preserved, including 5004/400 (invalid input), 3007 or 3008/408 (timeout), 3036 or 3040/429 (capacity/rate limit). See [native errors](https://developers.cloudflare.com/workers-ai/platform/errors/). For native error cases, configure the explicit numeric `LlmTestServerScenarioError.Code`. Unmapped Test Server-only errors such as fixture mismatch use the HTTP status as a synthetic code; that fallback is not a claimed Cloudflare native error code.

Optional `images` is an array of inline data URLs or `{ "content_type": "image/png", "base64": "..." }` objects. PNG/JPEG/WebP are supported; remote URLs are rejected. Limits: four images, 4 MiB decoded file bytes and 16 megapixels per image, 8 MiB combined decoded bytes, and 13 MiB for the complete request body. Image dimensions and file type are read as metadata; compressed pixel streams are not decoded, so full raster validity is outside this deterministic mock validation. The Test Server retains supplied images in the matched input and trace. See [input schema](https://developers.cloudflare.com/workers-ai/models/clef/schema-input.json), [output schema](https://developers.cloudflare.com/workers-ai/models/clef/schema-output.json).

### OpenAI Decisions

OpenAI uses `input` and an ordered question array. Predicate questions return one probability; boolean choices can carry separate true/false descriptions. Choice values are typed strings or booleans. Score `levels` use ordered string labels and optional descriptions; responses retain the zero-based level index and original label.

```json
{
  "model": "gpt-6-luna",
  "input": "Please help quickly",
  "questions": [
    { "type": "predicate", "name": "urgent", "instructions": "Is urgent?" },
    { "type": "choice", "name": "route", "instructions": "Choose the queue", "choices": [
      { "value": "fast", "description": "urgent" }, { "value": false, "description": "defer" }
    ] },
    { "type": "score", "name": "severity", "instructions": "Assess severity", "levels": [
      { "label": "low", "description": "low" }, { "label": "medium", "description": "medium" }, { "label": "high", "description": "high" }
    ] }
  ]
}
```

OpenAI fixture question IDs use `OpenAiDecisionFixtureIds.ForQuestionIndex(index)` for the zero-based position, regardless of optional native names. Choice fixture IDs use `ForChoiceValue(string/bool)` canonical JSON literals so string `"true"` differs from boolean `true`. Responses preserve names (including `null`), duplicate names and answer order.

```csharp
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.OpenAI;

var fast = OpenAiDecisionFixtureIds.ForChoiceValue("fast");
var defer = OpenAiDecisionFixtureIds.ForChoiceValue(false);
options.AddOpenAiDecisionModel("gpt-6-luna").AddDecisionScenario(new LlmTestServerDecisionScenario
{
    Id = "priority-openai", ModelId = "gpt-6-luna",
    Answers = new()
    {
        [OpenAiDecisionFixtureIds.ForQuestionIndex(0)] = new() { Probability = 0.95 },
        [OpenAiDecisionFixtureIds.ForQuestionIndex(1)] = new() { Kind = LlmTestServerDecisionKind.Choice, Choice = fast,
            Confidence = 0.9, Probabilities = new() { [fast] = 0.9, [defer] = 0.1 } },
        [OpenAiDecisionFixtureIds.ForQuestionIndex(2)] = new() { Kind = LlmTestServerDecisionKind.Score, Score = 1.75,
            Confidence = 0.8, Probabilities = new() { ["0"] = 0.05, ["1"] = 0.15, ["2"] = 0.8 } },
    },
    Usage = new() { InputTokens = 100, CachedInputTokens = 4, OutputTokens = 0, TotalTokens = 100 },
});
```

`input` can also be an array of user messages: string content or `input_text` / `input_image` parts. Images use embedded base64 data URLs; external URLs, file IDs, audio, tools and non-user roles are rejected. Usage retains input/output/total tokens, cached/cache-write input details and reasoning output details, including zero defaults. Set `Refused = true` on an answer fixture to emit a native `{"type":"refusal","name":...}` answer in a successful HTTP response.

### Limits, fixture matching and failures

| Profile | Choice criteria | Score criteria | Other native constraints |
| --- | --- | --- | --- |
| TypeSafe Jev | Up to 255 | 1..10 | Optional instructions and binary criteria; state is string/object/array, including JSON descriptions and nullable optional instructions. |
| Kev | 1..255 | 1..255 | Any JSON state/instructions/descriptions, empty opaque IDs; probabilities/native scores rounded to four decimals. |
| Cloudflare Clef/Flash | 2..255 | 2..10 | 1..64 questions; ASCII letter/digit/underscore/dot/hyphen IDs, max 100 characters; required nonempty instructions; four-decimal probabilities/native scores. |
| OpenAI | 2..255 distinct typed values | Ordered labels; no undocumented count cap | Optional names/descriptions, empty strings and repeated score labels preserved; max 128 embedded images; model/names/instructions/labels/descriptions max 1,048,576 characters; no invented choice-value length cap. |

`AddDecisionModel(id, LlmTestServerDecisionProvider...)` supports explicit custom model IDs; the control client mirrors it with `UseDecisionModel` and `UseDecisionScenario`. Fixtures are repeatable snapshots. Optional `ExpectedInput` matches the provider input exactly; SystemOne JSON state and OpenAI message arrays use serialized JSON. Cloudflare requests with supplied images match serialized `{state,images}`; text-only requests retain the state-only input. Question IDs, kinds and requested value domains must fit the fixture. Unknown models, unmatched inputs, mismatched fixtures, authentication, configured rate limits/content filtering and scripted errors produce observable non-success HTTP responses. Normal native choice/score fixtures require confidence and a complete probability domain, near-unit probability mass. SystemOne and Cloudflare choices must have maximal probability; OpenAI preserves any selected value in the supplied domain. Cloudflare confidence must equal the maximum probability within native rounding tolerance. Configure profile-correct explicit values: the Kev/Jev example above has score confidence 0.625; Clef uses the maximum probability 0.8. Values are validated and preserved without normalization or recomputation. Four-decimal distributions may total 0.9999 and native scores are not recomputed from rounded probabilities. For deliberate malformed-response regressions, set `AllowMalformedResponse = true` on the scenario; this explicitly bypasses native response conformance while keeping question-kind/domain mapping safe.

See [complete native contracts and fixture details](docs/Features/DecisionModels.md) and [behavior-test evidence](docs/Testing/AcceptanceCriteriaTestInventory.md). Provider-specific limits and optional-field rules above are reconciled against the linked primary contracts.
