# Providers, packages and routes

Provider namespaces, NuGet packages, hosted routes, current functionality and native decision APIs. Back to the [README](../../README.md).

## Packages

Package links identify the NuGet package surfaces; badges update when publication is available.
The complete v1.0.0 launch remains pending. Use the [local package feed](Installation.md#choose-how-to-run) before public release.

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
| [ManagedCode.LlmTestServer.SystemOne](https://www.nuget.org/packages/ManagedCode.LlmTestServer.SystemOne) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.SystemOne.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.SystemOne) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.SystemOne.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.SystemOne) | **TypeSafe / Jev** and Kev native System One decision contracts. |
| [ManagedCode.LlmTestServer.Cloudflare](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cloudflare) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Cloudflare.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cloudflare) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Cloudflare.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cloudflare) | Cloudflare Workers AI Clef/flash decision envelopes and validation. |
| [ManagedCode.LlmTestServer.Hosting](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Hosting) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Hosting.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Hosting) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Hosting.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Hosting) | ASP.NET Core endpoint mapping. |
| [ManagedCode.LlmTestServer.Client](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Client) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Client.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Client) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Client.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Client) | Control client plus `IChatClient`, `IEmbeddingGenerator<string, Embedding<float>>`, and `IImageGenerator` implementations. |
| [ManagedCode.LlmTestServer.Aspire](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Aspire) | [![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Aspire.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Aspire) | [![Downloads](https://img.shields.io/nuget/dt/ManagedCode.LlmTestServer.Aspire.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Aspire) | Aspire AppHost extension methods. |

## Current Functionality

This is the currently implemented behavior, not future intent:

| Surface | What works now |
| --- | --- |
| Hosting | `builder.Services.AddLlmTestServer(...)` registers the default runtime and isolated test-session registry; `app.MapLlmTestServer()` maps the Blazor server-side browser panel at `/`, JSON control endpoints under `/admin-api`, and provider-namespaced HTTP routes. |
| Runtime configuration | Configure at host startup, through `LlmTestServerClient.ConfigureAsync(...)`, or through `POST /admin-api/configure`. Configuration replaces the current runtime state and resets assertions, scenario positions, fault counters, and prompt-cache entries. |
| Reset | `ResetAsync()` and `POST /admin-api/reset` keep the current models/scenarios/fixtures but clear assertion events, scenario response positions, rate-limit counters, and prompt-cache entries. |
| Models | Default models use official fixture IDs for chat, embeddings, images, audio, and video. Typed helpers register broader OpenAI model families, and `AddModel(...)` supports custom deployment or provider IDs. Model kind is enforced on every route. |
| Chat scenarios | Contains matching, exact prompt matching, queued responses, streaming chunks, scripted errors, delayed responses, cancellation-safe queues, and scenario-specific bearer tokens are implemented. |
| Modalities | Chat, embeddings, image generation/edit/variation, audio speech/transcription/translation, OpenAI/Azure video, Gemini long-running video/file output, and Bedrock text/embedding/image invoke shapes return deterministic fixtures. |
| Provider matrix | OpenAI, Azure OpenAI, Microsoft Foundry, Anthropic, Gemini, Groq, Mistral, Ollama, Cohere, Amazon Bedrock, OpenRouter, DeepSeek, and Perplexity are exposed under explicit provider namespaces. Each hosted operation is represented in a provider `ApiContract` and behavior evidence tests. |
| SDK clients | Official OpenAI, Azure OpenAI, Azure AI Inference / Foundry, Anthropic and Google Gen AI SDKs, Amazon Bedrock, and `Microsoft.Extensions.AI` clients call the qualified hosted routes. See the [SDK compatibility matrix](../Features/ProviderSdkCompatibility.md) for operation-level evidence and boundaries. The client package also creates control, chat, embedding, image, and audio clients with the same bearer token. |
| Auth | A global bearer token protects provider and `/admin-api/*` endpoints. Anthropic `x-api-key` and Azure `api-key` headers are accepted as provider-native ways to pass the same token. Scenarios can add their own bearer-token requirement. |
| Fault simulation | Provider-neutral rate-limit and content-filter simulation flow through every provider family and return provider-shaped errors. Scripted per-scenario failures remain available for exact test paths. |
| Assertions | `/admin-api/assertions` returns counters plus retained runtime events with request preview, response/error text, model id, scenario id, event kind, and deterministic usage. |
| Test sessions | Typed `CreateSessionAsync(...)` and session headers isolate configuration, queues, history, faults, cache and journal; reset/disposal affect the owning session. |
| Conversation graphs | `UseConversation(...)` configures typed step predicates, branches, tool-result checks, turn ordering, terminal policies and successful-request replay; match explanations and conversation status are available through control APIs. |
| Request evidence | `GetRequestsAsync(...)` filters/pages the bounded journal; `ExportRequestsAsync(...)` and `ExportRequestsNdjsonAsync(...)` include versioned retention/eviction evidence. |
| Verification | `VerifyAsync(...)` checks counts/order, unexpected calls, structured tool arguments/results, visited graph steps, terminal markers and retry policy. Incomplete retained evidence cannot pass. |
| Wire identity and time | `UseDeterminism(seed, fixedUtc)` fixes generated provider IDs/time with provider-local counters; reset reproduces them and graph replay retains metadata. Real diagnostic clocks and retry timing stay real. |
| Usage accounting | Input, output, reasoning, total, cached-input, and cache-write token values are computed deterministically and projected into provider-specific response envelopes when that provider surface documents usage fields. |
| Browser panel | `/` shows models/fixtures, actions, control and provider endpoint groups, grouped runtime events, captured requests, captured responses, counters, and per-event usage. It auto-refreshes every 15 seconds using Blazor component state and `StateHasChanged()`. |
| Aspire | `builder.AddLlmTestServer()` starts the packaged .NET service without Docker by default; `WithApiKey(...)` wires auth, `GetHttpEndpoint()` and provider-specific endpoint helpers expose URLs, and `AddLlmTestServerContainer()` is an explicit container opt-in. |

See [conversation graphs](../Features/ConversationDSL.md) and
[request verification and determinism](../Features/RequestVerificationAndDeterminism.md) for typed APIs,
HTTP contracts, retention limits and terminal-marker applicability. Explicit fixture timestamps retain their
configured values.

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
| **`ManagedCode.LlmTestServer.SystemOne`** | `systemone` | **TypeSafe / Jev** and Kev native System One decisions | `/systemone/v1/systemone` |
| `ManagedCode.LlmTestServer.Cloudflare` | `cloudflare` | Clef / Clef Flash native decisions | `/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef` |

Each provider profile also carries a doc-backed `ApiContract` with official documentation links, retrieval date, API version or header requirements, documented operations, streaming support, and whether `ManagedCode.LlmTestServer.Hosting` currently maps the operation.

Use a provider profile when code needs a stable package-level declaration without starting a host:

```csharp
using ManagedCode.LlmTestServer.AzureOpenAI;

var profile = AzureOpenAiCompatibility.Profile;
Console.WriteLine(profile.Id); // azure-openai
```

## Hosted Endpoint Families

Provider endpoints are explicitly namespaced. The table below shows the route
families and representative operations; the full method/path contract lives in
each provider profile's `ApiContract` and is guarded by
`ProviderApiContractTests`. See
[`docs/Features/ProviderApiContractCoverage.md`](../Features/ProviderApiContractCoverage.md)
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
| TypeSafe / Jev and Kev (System One) | `/systemone` | `/systemone/v1/systemone` |
| Cloudflare (Clef/flash) | `/cloudflare` | `/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef`, `clef-flash` |

The Blazor server-side rendered browser control panel lives at `/`. JSON control endpoints live under
`/admin-api`; see the [complete admin API reference](AdminApi.md) for configuration, reset, models,
assertions, request queries/exports, verification, conversation diagnostics and isolated sessions.

Audio speech returns a deterministic WAV fixture by default. When
`requiredBearerToken` is configured, all provider and control endpoints require
`Authorization: Bearer <token>`.

## Native decision API fixtures

Native typed clients are available through `LlmTestServerClient.CreateSystemOneClient()`
and `LlmTestServerClient.CreateCloudflareDecisionClient(accountId)`. Clef accepts embedded
PNG/JPEG/WebP data URLs or base64 objects; Jev accepts PNG/JPEG reference assets as
JSON or multipart, with explicit image/long-context opt-ins. See the
[official-contract audit and supported limits](../Features/DecisionVisionAudit.md).

Jev, Kev, Cloudflare Clef/Clef Flash and OpenAI Decisions use explicit decision fixtures. The Test Server hosts their native JSON shapes under provider namespaces and does not contact production providers. Declare the provider profile explicitly; model names never select an API implicitly.

| Provider | Test Server POST route | Production POST endpoint | Model helper | Official contract |
| --- | --- | --- | --- | --- |
| TypeSafe Jev | `/systemone/v1/systemone` | `https://api.typesafe.ai/v1/systemone` | `AddJevLatest()` | [OpenAPI](https://api.typesafe.ai/openapi.json), [primitives](https://docs.typesafe.ai/primitives/choice) |
| Kev | `/systemone/v1/systemone` | `{kev-server}/v1/systemone` | `AddKevLatest()` | [native request models](https://github.com/jaredpalmer/kev/blob/main/kev/api.py), [server/auth](https://github.com/jaredpalmer/kev/blob/main/kev/serve.py) |
| Cloudflare Clef | `/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef` | `https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef` | `AddClef()` | [Clef API](https://developers.cloudflare.com/workers-ai/models/clef/), [REST envelope/auth](https://developers.cloudflare.com/workers-ai/get-started/rest-api/) |
| Cloudflare Clef Flash | `/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef-flash` | `https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef-flash` | `AddClefFlash()` | [Clef Flash API](https://developers.cloudflare.com/workers-ai/models/clef-flash/) |
| OpenAI Decisions | `/openai/v1/decisions` | `https://api.openai.com/v1/decisions` | `AddOpenAiDecisionModel("gpt-6-luna")` | [create API](https://developers.openai.com/api/reference/resources/decisions/methods/create/) |

Production APIs use `Authorization: Bearer <provider-token>`; Kev checks `KEV_API_KEY` only when configured and otherwise permits unauthenticated calls. For the Test Server, `RequireBearerToken` controls the expected test token and protects both provider routes and `/admin-api/*`. Tests do not need production credentials. Resolve the hosted base URL from the Aspire resource (`GetEndpoint("http")` / `CreateHttpClient(...)`), then append the Test Server route.

Complete native request/response contracts, examples and limits for SystemOne (Jev, Kev), Cloudflare Clef/Clef Flash and OpenAI Decisions are in [native decision contracts](../Features/DecisionModels.md); behavior-test evidence is in the [test inventory](../Testing/AcceptanceCriteriaTestInventory.md).
