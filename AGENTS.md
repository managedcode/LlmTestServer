# LLM Test Server Contributor Guide

## Scope

This repository builds `ManagedCode.LlmTestServer`, a deterministic LLM API mock server for LLM APIs. Treat the runtime, protocol adapters, clients, Aspire integration, docs, and samples as separate package surfaces.

Follows [MCAF](https://mcaf.managed-code.com/).

## Conversations (Self-Learning)

Learn the user's stable habits, preferences, and corrections. Record durable rules here instead of relying on chat history.

Before doing any non-trivial task, evaluate the latest user message.
If it contains a durable rule, correction, preference, or workflow change, update `AGENTS.md` first.
If it is only task-local scope, do not turn it into a lasting rule.

Update this file when the user gives:

- a repeated correction
- a permanent requirement
- a lasting preference
- a workflow change
- a high-signal frustration that indicates a rule was missed

Extract rules aggressively when the user says things equivalent to:

- "never", "don't", "stop", "avoid"
- "always", "must", "make sure", "should"
- "remember", "keep in mind", "note that"
- "from now on", "going forward"
- "the workflow is", "we do it like this"

Preferences belong in `## Preferences`:

- positive preferences go under `Likes`
- negative preferences go under `Dislikes`
- comparisons should become explicit rules or preferences

Corrections should update an existing rule when possible instead of creating duplicates.

Treat these as strong signals and record them immediately:

- anger, swearing, sarcasm, or explicit frustration
- ALL CAPS, repeated punctuation, or "don't do this again"
- the same mistake happening twice
- the user manually undoing or rejecting a recurring pattern

Do not record:

- one-off instructions for the current task
- temporary exceptions
- requirements that are already captured elsewhere without change

Rule format:

- one instruction per bullet
- place it in the right section
- capture the why, not only the literal wording
- remove obsolete rules when a better one replaces them

## Commands

- Restore: `dotnet restore ManagedCode.LlmTestServer.slnx`
- Build: `dotnet build ManagedCode.LlmTestServer.slnx --configuration Release --no-restore`
- Test: `dotnet test --project tests/ManagedCode.LlmTestServer.Tests/ManagedCode.LlmTestServer.Tests.csproj --configuration Release --no-build --verbosity normal`
- Pack: `for project in src/*/*.csproj; do dotnet pack "$project" --configuration Release --no-build --output artifacts/packages; done`
- Release: pushing to `main` must start the release workflow; it reads the package version from `Directory.Build.props`, creates the matching `vX.Y.Z` tag and GitHub Release, attaches package artifacts, and publishes NuGet packages only when that version is not already released.
- Format check: `dotnet format ManagedCode.LlmTestServer.slnx --verify-no-changes`
- Tool restore: `dotnet tool restore`
- Coverage gate: production code line coverage must stay at or above 90%; measure with coverlet/reportgenerator when coverage expectations change.
- Coverage: `bash scripts/coverage.sh` (maps deterministic CI source paths before collection and enforces 90% total production line coverage).
- Quality gate: `dotnet format ManagedCode.LlmTestServer.slnx --verify-no-changes --no-restore && dotnet build ManagedCode.LlmTestServer.slnx --configuration Release --no-restore && dotnet test --project tests/ManagedCode.LlmTestServer.Tests/ManagedCode.LlmTestServer.Tests.csproj --configuration Release --no-build --verbosity normal && git diff --check`

`global.json` opts `dotnet test` into `Microsoft.Testing.Platform`; do not add VSTest-specific logger arguments to normal test commands.

## Preferences

### Likes

- In marketing avatar videos, keep the presenter addressing the camera in frontal shots without side profiles or sustained off-camera gaze. Preserve natural blinking, small eye movements and relaxed facial/head motion; camera contact must feel conversational rather than frozen or tense.
- Match the avatar video's background, lighting and wardrobe colours to the actual website and product screenshots so all launch assets form one coherent visual style when edited together.
- Ship one complete v1.0.0 launch batch on a separate branch; the owner performs the merge. Do not merge, publish intermediate versions, or trigger releases before the complete product and launch preparation are ready.
- Publish the product landing page through GitHub Pages using the dashboard visual language and shared styles/components where practical; substantiate product sections with real server/browser screenshots and demo recordings.
- Keep README.md product-first: what LLM Test Server is, why to use it, what to do with it, and where to go next, in plain language. Move deep technical detail into docs/ and link to it. Every code sample must use real public APIs and be compile-checked against the current packages before it ships; never invent APIs or pad the README with technical noise.
- Lead README, landing and launch material with visually interesting captures (token flow, insights charts), not plain list or table screenshots.
- Keep the landing page polished, clear and fully responsive: showcase every major capability (provider matrix, request inspector, token analytics, tools/conversations, streaming, faults, Aspire/Docker setup) with crafted visual elements, not plain text blocks. Review it at desktop, tablet and mobile widths after every change.
- Make the dashboard visually rich with donut/pie charts across many dimensions of recorded traffic (providers, models, outcomes, operations, latency, tokens, cache, tool usage), each with a value-and-share legend; stable entity colors across filters.
- Use one distinctive, symbol-only product mark (no letter monogram) consistently for the launch thumbnail, favicons, dashboard header and OG/social images. It must not resemble the owner's other brands: no brackets around a square or token (that is the Prostir.build mark), and check prostir.build and managed-code.com marks before proposing one.
- The product website lives at https://llmtest.managed-code.com; use that canonical URL in site metadata, README, launch listings and social posts.
- Position the product as .NET-first but usable from any stack: the prebuilt Docker image plus the management API work for teams outside .NET, so launch copy and video must say so.
- Keep launch marketing drafts, generated images, videos, screenshots and production files under marketing/ and leave that directory uncommitted until the owner authorizes publication.
- Deliver both NuGet packages and a publicly pullable prebuilt Docker image, configurable through the management API.
- Build and publish container images through the .NET SDK container targets (`dotnet publish /t:PublishContainer`); do not use Dockerfile/docker build as the distribution build mechanism.

- Keep checked-in UI screenshots synchronized with the current rendered product; after visible dashboard changes, refresh and verify the affected images in a real browser before commit and push.
- When token usage is requested, count response tokens with `tiktoken` and include the measured usage in replies; if `tiktoken` is unavailable, report that blocker instead of guessing token counts.
- Token usage workflow changes need executable code and tests, not only contributor-guide documentation, so callers can verify usage accounting behavior directly.

### Dislikes

- Treat ManagedCode.LlmTestServer as a completely new brand: remove all old project/package/type/configuration names, legacy aliases, and backward-compatibility paths. Do not provide a migration layer or preserve old branded diagnostic codes, fixture IDs, package identities, or release/deprecation machinery.

- Do not ship template-looking or AI-generic UI: no colored left-border cards or chat bubbles, no unbalanced card compositions with lopsided visuals, no gradient-glow icon tiles, no walls of numbers where a diagram would explain more. Study high-quality references (for example Dribbble analytics dashboards) before visual work, keep every card's visual balanced and centered on one idea, and keep copy short and clear.
- Do not mention competing products in README, documentation, source, tests, or other repository artifacts; describe our own contracts and capabilities directly.
- Keep only integration tests that start the server and verify real HTTP/provider SDK behavior. Do not add or retain unit tests, fake clients, source/document scans, reflection-based test guards, or assertions about implementation fragments. Exercise every supported API and parameter, streaming mode, error boundary, and complete tool/conversation flow through the running server.
- Do not introduce real-LLM toggles, external project environment variable names, or WA.Storied-specific constants into this repository; LlmTestServer tests and samples must remain deterministic, repo-owned, and free of cross-project naming leaks.

## Code Quality Skills

- Repository-local skills live in `.codex/skills`; keep them checked in with the repo.
- MCAF governance skills remain the `mcaf-*` set installed from the canonical MCAF tutorial.
- Code-quality skills are installed from the current Managed Code `dotnet-skills` catalog for `.NET Quality`, `Frontend Quality`, `Diagnostics & Metrics`, `Testing`, and `Testing Research`.
- Use `analyzer-config`, `code-analysis`, `format`, `quality-ci`, `roslynator`, `meziantou-analyzer`, `stylecop-analyzers`, `complexity`, and `resharper-clt` when changing analyzer, naming, formatting, or warning policy.
- Use `run-tests`, `tunit`, `coverage-analysis`, `coverlet`, `reportgenerator`, `stryker`, `assertion-quality`, `test-anti-patterns`, `test-gap-analysis`, and `test-smell-detection` when changing test behavior or coverage expectations.
- Use `biome`, `eslint`, `htmlhint`, `stylelint`, `webhint`, and `sonarjs` for browser-facing files under `site` or future frontend assets.
- Use `codeql`, `quickdup`, `cloc`, `analyzing-dotnet-performance`, `microbenchmarking`, and `profiling` for deeper static, duplication, size, or performance analysis.
- Root `.editorconfig` is the source of truth for formatting, naming, and C# style. Prefer changing it once over suppressing analyzer noise in individual files.

## Design Rules

- Identify TypeSafe as the provider and Jev as its model family in user-facing documentation; name System One as the native API/protocol and make its package, route and typed client discoverable instead of hiding TypeSafe behind the protocol name.

- Tool fixtures must explicitly simulate distinct errors, timeouts, internal failures or connection crashes, literal null, empty/no result, and content-filter refusals. Keep tool-result failures distinct from provider/transport failures, expose the observed outcome in diagnostics, and verify recovery plus conversation/cache state through real server integrations; expected faults must not be rejected as invalid mock configuration.

- Before claiming that all review findings are fixed, recheck every finding against final behavior and tests; report removed capability claims and temporary mitigations separately from implemented functionality.

- Prefer official .NET packages and abstractions before writing custom protocol code.
- Provider compatibility tests must use the official provider SDK clients against the running server wherever an official client exists. Raw HTTP supplements malformed-request, exact wire-format and unsupported-boundary checks; it is not sufficient proof for supported success, streaming, tool or conversation flows. Do not substitute fake `IChatClient` implementations or direct runtime/builder calls for integration behavior.
- Keep provider wire formats in provider packages such as `ManagedCode.LlmTestServer.OpenAI`; keep deterministic scenario behavior in `ManagedCode.LlmTestServer`.
- Keep the provider package matrix explicit. Do not ship the Test Server as only OpenAI-compatible; maintain package surfaces for OpenAI, Azure OpenAI, Microsoft Foundry, Anthropic, Gemini, Groq, Mistral, Ollama, Cohere, Amazon Bedrock, OpenRouter, DeepSeek, and Perplexity.
- Azure OpenAI and Microsoft Foundry compatibility must be proven with official Azure SDK clients, not only raw HTTP requests.
- Azure SDK clients must select their own API version. Do not pin a dated version in consumers or SDK compatibility tests to satisfy the emulator; deployment routes must enforce their documented supported Azure versions and reject unknown versions, including syntactically valid future dates. Do not weaken server validation to make client tests pass.
- Source current built-in model IDs and capability defaults from the original official provider SDK model definitions and current official model documentation. Prefer SDK model identifiers in SDK tests; do not invent models or treat stale hand-maintained string constants as an authoritative catalogue. Keep custom fixture models explicitly configurable.
- Keep custom `AddModel(...)` support, but expose typed convenience APIs for common official fixture models so callers do not have to hand-type model IDs for standard chat, embedding, image, and audio setup.
- Reasoning-capable models must carry deterministic reasoning-token usage through runtime summaries and provider response usage details; do not flatten reasoning usage into ordinary visible output tokens only.
- Prompt-cache accounting is provider-specific: expose cache read/write usage for every provider surface that has a documented cache contract, and report no cache usage for providers or operations without one.
- Compatibility tag values in C# code must come from named constants everywhere they are assigned or asserted, so `CompatibilityTags` cannot drift through inline string literals.
- Provider `ApiContract` operation id values and behavior-evidence operation ids in C# must come from `LlmTestServerProviderOperationIds`; do not type raw operation-id strings such as `videos.content.retrieve`.
- The client package must expose a universal pre-test configuration API so tests can spawn a client, reset or configure the hosted Test Server, load models, auth, datasets, scenarios, scripted errors, embeddings, images, and audio fixtures without hand-authoring raw DTOs.
- The browser admin panel must be served from `/`, and runtime control APIs must use `/admin-api/...` paths; do not add hidden or aliased admin/control routes for reset, configure, models, assertions, or operator UI.
- The browser admin panel must be implemented as Blazor server-side components in `ManagedCode.LlmTestServer.Hosting`; do not serve it from a static HTML string endpoint, and use component state plus `StateHasChanged()` for refresh behavior instead of client-side polling fetch code.
- The browser admin panel is a full-viewport debugging surface, not a narrow landing page; show models/fixtures, actions, calls/requests, responses, provider/control API endpoints, assertions, and runtime events as separate grouped, inspectable areas so users can inspect each part without digging through one collapsed feed.
- The browser admin dashboard must be request-centric: use a scannable table with one entry per provider request, show provider and model at a glance, and let operators inspect the full request, response, multi-turn conversation, cache accounting, token usage, timing, status, and related metadata without reconstructing a call from separate feeds; keep retention explicitly bounded and visibly mark the exceptional payload that exceeds that diagnostic budget.
- Show request and explicitly scoped conversation analytics from recorded traffic as visual token composition, not text tables and footnotes: split input tokens by system prompt, user, assistant, tool calls, tool results and tool schemas with the runtime tokenizer so the parts reconcile exactly with reported input, then show cache read/write/uncached and output/reasoning through flow (Sankey) and donut charts with value-and-share legends, plus per-request context growth for conversations. Keep reasoning/cache subsets out of double-counted totals, distinguish missing usage from zero, and disclose retention limits briefly instead of in walls of caveat text.
- Keep the request inbox visually primary and easy to scan; do not compress it beside a narrow, endlessly tall inspector. Constrain long conversations and payloads to independent scroll regions, collapse oversized system/tool content by default while keeping it expandable, and validate the resulting hierarchy with realistic long multi-turn data at desktop, tablet, and mobile sizes.
- Keep the dashboard's useful horizontal master-detail structure, but use an Apple-inspired content-first visual language rather than Material or Fluent styling: quiet system typography, generous clean surfaces, restrained separators and status color, sidebar depth only where it clarifies hierarchy, and no grid of badges or boxed metrics. Keep the filter/navigation rail visible at ordinary laptop widths and the selected response visible in the first reading-pane viewport; on narrow mobile screens use an explicit list-to-detail transition with a Back action.
- Every chart mark, bar, segment and dot in the dashboard needs a styled hover tooltip with the label, value and share; do not rely on slow native title tooltips.
- Render JSON payloads as properly indented, syntax-highlighted code (keys, strings, numbers, literals) with an SF Mono-style font, and preserve readable Unicode characters instead of exposing serializer escape sequences such as `\u0027`.
- Use Microsoft Fluent UI Blazor primitives for dashboard overlays and dismissible controls, but theme them to the dashboard's Apple-inspired visual language instead of adopting stock Fluent styling. Popovers must close on outside click, Escape, and completed actions. Blend Prostir.build's restrained glass, lime selection rail, and technical polish with managed-code.com's warm canvas, ink typography, and tiny coral-to-magenta accents without copying either site's branding or marketing-page layout.
- The browser admin panel must auto-refresh by default every 15 seconds, but do not add visible countdown UI unless the user explicitly asks for it.
- Provider APIs must be explicitly namespaced by provider, such as `/openai`, `/anthropic`, `/gemini`, or `/azure-openai`; do not expose generic root `/v1/*` provider routes that make the Test Server look like only the OpenAI API.
- Aspire examples must show endpoint retrieval from the Aspire resource (`GetEndpoint("http")` or `CreateHttpClient(...)`) and API key wiring so users see the complete integration path.
- Aspire sample services must use `ManagedCode.LlmTestServer.Hosting` service-defaults methods such as `AddServiceDefaults()` and `MapDefaultEndpoints()` for health/liveness wiring; do not create a separate ServiceDefaults sample project, and do not hand-roll `app.MapGet("/health", ...)` or placeholder root status endpoints in sample `Program.cs` files.
- Aspire integration must expose a package-owned `builder.AddLlmTestServer()` entry point so consumers can install the Aspire NuGet package and add the Test Server without caller-supplied project paths or `Projects.*` metadata types.
- `builder.AddLlmTestServer()` must be .NET/Aspire-first and must not require Docker or a container runtime by default; container-backed hosting may exist only as an explicit opt-in API.
- Aspire integration must start an Aspire AppHost in tests before a change is considered covered, with the AppHost model built directly in test code or a test fixture.
- Aspire test coverage should build the AppHost directly inside the test code or fixture; do not add a standalone `Obhost`/test AppHost file just to host Aspire for tests.
- Aspire tests and samples must resolve service/provider URLs from the Aspire resource endpoint allocated by the AppHost; do not hard-code `127.0.0.1`, `localhost`, ports, or invented provider endpoints in configuration.
- Do not add an Aspire provider-endpoint setter to `ManagedCode.LlmTestServer.Aspire`; consumers must use the endpoint exposed by the `LlmTestServerResource`.
- Do not add Aspire compatibility environment flags, sample-service config echoes, or fluent compatibility methods unless they drive real route/runtime behavior; prove compatibility through actual provider clients instead.
- Provider API compatibility must be doc-backed: every claimed provider method, route, streaming mode, modality, and API-version constraint must be represented in a checked-in contract with official documentation links and covered by tests before the provider package claims support.
- Provider API verification must cover the full claimed provider matrix: every implemented `ApiContract` operation for every provider needs behavior-test evidence, and fixes that touch provider compatibility must rerun the provider contract/evidence tests plus the full quality gate before closeout.
- Provider fault simulation must be provider-neutral and always available across the full claimed provider matrix: configurable rate-limit (`too_many_requests`/429) and content-filter (`content_filter`) behavior must flow through runtime configuration and be proven on all provider families, not only OpenAI-compatible routes.
- Do not hide nondeterminism behind retries. Model responses, stream chunks, errors, auth requirements, embeddings, images, and audio fixtures should be explicit.

## Ownership Map

- `src/ManagedCode.LlmTestServer`: provider-neutral runtime, scenarios, assertions, and model catalog.
- `src/ManagedCode.LlmTestServer.SystemOne`: Jev/Kev native SystemOne decision contracts and compatibility profile.
- `src/ManagedCode.LlmTestServer.Cloudflare`: Workers AI Clef/flash native decision envelopes, validation and compatibility profile.
- `src/ManagedCode.LlmTestServer.OpenAI`: OpenAI-compatible request/response shapes and mapping.
- `src/ManagedCode.LlmTestServer.AzureOpenAI`: Azure OpenAI compatibility profile and future Azure OpenAI wire contracts.
- `src/ManagedCode.LlmTestServer.Foundry`: Microsoft Foundry compatibility profile and future Foundry wire contracts.
- `src/ManagedCode.LlmTestServer.Anthropic`: Anthropic Messages compatibility profile and future Anthropic wire contracts.
- `src/ManagedCode.LlmTestServer.Gemini`: Gemini compatibility profile and future Gemini wire contracts.
- `src/ManagedCode.LlmTestServer.Groq`: Groq compatibility profile and future Groq wire contracts.
- `src/ManagedCode.LlmTestServer.Mistral`: Mistral compatibility profile and future Mistral wire contracts.
- `src/ManagedCode.LlmTestServer.Ollama`: Ollama compatibility profile and future Ollama wire contracts.
- `src/ManagedCode.LlmTestServer.Cohere`: Cohere compatibility profile and future Cohere wire contracts.
- `src/ManagedCode.LlmTestServer.Bedrock`: Amazon Bedrock compatibility profile and future Bedrock wire contracts.
- `src/ManagedCode.LlmTestServer.OpenRouter`: OpenRouter compatibility profile and future OpenRouter wire contracts.
- `src/ManagedCode.LlmTestServer.DeepSeek`: DeepSeek compatibility profile and future DeepSeek wire contracts.
- `src/ManagedCode.LlmTestServer.Perplexity`: Perplexity compatibility profile and future Perplexity wire contracts.
- `src/ManagedCode.LlmTestServer.Hosting`: ASP.NET Core provider and control endpoints.
- `src/ManagedCode.LlmTestServer.Client`: control client and `Microsoft.Extensions.AI` clients.
- `src/ManagedCode.LlmTestServer.Aspire`: Aspire AppHost extension methods.
- `samples`: runnable AppHost and service sample.
- `tests`: TUnit server API, client SDK, and Aspire integration coverage.
- `docs`: architecture, feature specs, ADRs, and test inventory.
- `site`: static public landing page.
