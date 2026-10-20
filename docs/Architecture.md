# LLM Test Server Architecture

## Purpose

LLM Test Server provides deterministic provider emulation for integration tests. Test suites can host a local compatibility server, script exact responses and failures, call it through normal client abstractions, and inspect assertion summaries after execution.

## Package Boundaries

- `ManagedCode.LlmTestServer` owns provider-neutral runtime state: models, scenarios, conversation step graphs, match rules, deterministic modality fixtures, auth checks, and assertion events.
- `ManagedCode.LlmTestServer.OpenAI` owns OpenAI-compatible DTOs and mapping from runtime results to wire responses.
- `ManagedCode.LlmTestServer.AzureOpenAI`, `ManagedCode.LlmTestServer.Foundry`, `ManagedCode.LlmTestServer.Anthropic`, `ManagedCode.LlmTestServer.Gemini`, `ManagedCode.LlmTestServer.Groq`, `ManagedCode.LlmTestServer.Mistral`, `ManagedCode.LlmTestServer.Ollama`, `ManagedCode.LlmTestServer.Cohere`, `ManagedCode.LlmTestServer.Bedrock`, `ManagedCode.LlmTestServer.OpenRouter`, `ManagedCode.LlmTestServer.DeepSeek`, `ManagedCode.LlmTestServer.Perplexity`, `ManagedCode.LlmTestServer.SystemOne`, and `ManagedCode.LlmTestServer.Cloudflare` own provider compatibility profiles and provider-specific wire contracts as they are implemented.
- `ManagedCode.LlmTestServer.Hosting` exposes the runtime through ASP.NET Core endpoints and serves the browser admin panel as Blazor server-side rendered components.
- `ManagedCode.LlmTestServer.Client` exposes a control client and `Microsoft.Extensions.AI` clients.
- `ManagedCode.LlmTestServer.Aspire` adds a package-owned Aspire resource with `builder.AddLlmTestServer()`.
- `samples/ManagedCode.LlmTestServer.Service` is the runnable HTTP provider emulator.
- `samples/ManagedCode.LlmTestServer.AppHost` shows the consumer AppHost shape for the Aspire package.

## Runtime Flow

1. Tests configure the runtime through `AddLlmTestServer(...)` at host startup, through `LlmTestServerClient.ConfigureAsync(config => ...)`, or through `POST /admin-api/configure`.
2. Provider requests arrive through explicit namespaces such as `/openai/v1/*`, `/anthropic/v1/*` and `/gemini/*`.
3. Hosting maps provider requests into provider-neutral runtime requests.
4. The runtime checks bearer-token requirements, configured model IDs, model modality kind, and scenario match rules.
5. The provider adapter maps success or failure to the expected wire shape.
6. Tests call `/admin-api/assertions` to inspect matched, unmatched, unknown-model, auth-failed, exhausted, and error-returned events.

Control endpoints use the same bearer-token requirement when one is configured, so tests can expose the service without leaving runtime reset or reconfiguration open to unauthenticated callers.

## Isolated test sessions

Hosting owns a bounded registry of independent core runtimes and request journals. Request-scoped DI selects a
session using its ID/key headers before provider/control execution; no header selects the default runtime.
Session credentials are independent of provider credentials. Reset/configure/delete affect only their owning
session. See [Test sessions](Features/TestSessions.md) for the HTTP/client contract and
[ADR-0003](ADR/ADR-0003-isolated-test-sessions.md) for bounds, ownership and concurrency decisions.

## Request evidence and fixture clocks

Hosting retains bounded HTTP evidence in the session's existing trace journal. Core `Diagnostics` supplies the
shared query/export DTOs, and core `Verification` evaluates serializable count/order/tool/conversation/retry
expectations. Retention loss and incomplete pages prevent successful complete verification. Runtime-owned,
provider-local seeded metadata controls generated wire IDs/time while real event/trace and monotonic timing
remain observational. See [Request verification and determinism](Features/RequestVerificationAndDeterminism.md)
and [ADR-0005](ADR/ADR-0005-request-evidence-and-wire-determinism.md). E4 is locally qualified; branch CI and
public delivery remain pending.

## Stored response continuation

The runtime owns bounded provider/model/credential-isolated chat-response snapshots. Provider adapters map
Responses storage/parent fields into this neutral state and reuse its response identity for streaming and normal
output. See [Responses continuation](Features/ResponsesContinuation.md) for limits, reset semantics and regressions.

## Conversation step graphs

The core conversation slice adds serializable predicates and ordered transitions to existing scenarios. Hosting
passes the conversation selector and provider namespace into the runtime request scope; state remains owned
by the selected test session and is partitioned by scenario, provider and credential. A typed client adds the
selector without changing shared transport headers. Control APIs expose bounded conversation status and
side-effect-free match explanations. See [Conversation DSL](Features/ConversationDSL.md) for the contract and
[ADR-0004](ADR/ADR-0004-conversation-step-graphs.md) for identity, reservation, replay and lifecycle decisions.
This extension is locally qualified through server integrations and an installed-package consumer; public
delivery remains pending.

## Determinism

Linear chat scenarios consume queued responses. Conversation graphs select an active step and advance only
after its fixture commits; identical successful retries can replay the last result. Streaming responses use
explicit chunks. Datasets are named groups of active scenarios and graphs so tests can load conversation sets
before a run. Embeddings return configured vectors. Image, audio, and video endpoints return fixed fixture content.

Audio transcription fixtures carry explicitly configured duration or complete audio/text/output token usage
through runtime results and OpenAI-compatible JSON, verbose JSON and final streaming events. Configure it
with `WithDefaultTranscriptionUsage(LlmTestServerTranscriptionUsage.ForDuration(seconds))` at startup or
`UseTranscriptionUsage(LlmTestServerTranscriptionUsage.ForTokens(audio, text, output))` through the control client.
The default fixture duration is one second; it is deterministic fixture data, never inferred from input bytes.
Azure OpenAI and Microsoft Foundry expose `/openai/v1/audio/transcriptions` with no required query version;
explicit `v1` and `preview` are accepted and unknown or duplicate versions are rejected. Official SDK tests
exercise the multipart endpoint. See the [Microsoft audio reference](https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest)
and [OpenAI usage contract](https://developers.openai.com/api/reference/resources/audio/subresources/transcriptions/methods/create).

## Client Configuration Surface

`ManagedCode.LlmTestServer.Client` owns the universal pre-test configuration API. A test can create one `LlmTestServerClient`, optionally bind a bearer token, call `ConfigureAsync(config => ...)`, and then create `Microsoft.Extensions.AI` chat, embedding, and image clients from the same control client. Audio and video fixtures are reachable through `LlmTestServerClient.GenerateAudioAsync(...)` and `LlmTestServerClient.GenerateVideoAsync(...)` until first-party `Microsoft.Extensions.AI` abstractions exist.

The client builder wraps the provider-neutral configuration builder; it does not create a separate runtime model. This keeps startup configuration, control endpoint configuration, and raw JSON configuration on the same contract.

## Compatibility Strategy

OpenAI compatibility is the first implemented provider endpoint surface. The hosted surface also maps Azure OpenAI deployment routes, Microsoft Foundry / Azure AI Inference chat and embedding routes, and the native Anthropic Messages route. The provider matrix is explicit: OpenAI, Azure OpenAI, Microsoft Foundry, Anthropic, Gemini, Groq, Mistral, Ollama, Cohere, Amazon Bedrock, OpenRouter, DeepSeek, and Perplexity each have a package-level compatibility profile. Each profile carries a doc-backed `ApiContract` so claimed routes, methods, streaming modes, modalities, and version requirements can be checked against official provider documentation. Provider-specific DTOs and mapping belong in the matching provider package; provider-neutral scenario behavior stays in the core runtime.

Aspire integration adds the Test Server through `builder.AddLlmTestServer()` without requiring consumer AppHosts to reference a service project, generated `Projects.*` metadata type, project path, Docker, or a container runtime. The package-owned resource starts the packaged .NET LLM Test Server service executable and exposes its `http` endpoint. Consumers that explicitly want container hosting can opt in with `builder.AddLlmTestServerContainer()`, which uses the versioned `ghcr.io/managedcode/llm-test-server` image. Consumer resources reference the `LlmTestServerResource`, wait for it, and read its `http` endpoint through `GetHttpEndpoint()` or Aspire's `GetEndpoint("http")`.

Native decision fixtures live under the core `Decisions` slice; model profile metadata selects documented SystemOne limits and rounding. OpenAI wire contracts stay in OpenAI, SystemOne Jev/Kev contracts share their native protocol package, and Cloudflare owns Workers AI envelopes and native Clef validation. Hosting delegates the new native routes to the same authenticated/fault-injected runtime and request tracing.

The [Jev 1.14 JSON extension](Features/Jev114.md) adds TypeSafe image assets and
box answers to that same SystemOne route. Typed image validation belongs to
SystemOne; neutral bounding-box fixtures and resolved model attribution belong to core.
