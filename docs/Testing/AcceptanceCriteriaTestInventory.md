# Server integration acceptance inventory

Only integration tests are accepted: start the server, call its public HTTP endpoints or real provider SDKs,
and inspect public responses/control APIs. Test setup may build explicit fixtures, but assertions must prove
observable server behavior. Source/document scans, fake clients, reflection guards and direct runtime unit tests
have been removed. This inventory is documentation; tests do not read it to assert that method names appear here.

## Existing executable flows

| Contract | Integration evidence |
| --- | --- |
| Default models, chat, embeddings, images, audio | `OpenAiEndpointTests.OpenAiEndpoints_ExposeModelsAndAllModalitiesAsync` |
| JSON and native SSE streaming, usage, terminal events | `OpenAiEndpointTests`, `OpenAiSdkCompatibilityTests`, `OpenAiCompatibleProviderRouteTests` |
| Fixed output tokenizer counts exposed on response and journal | `LlmTestServerTokenCounterTests.ChatResponse_ReportsConfiguredOutputTokenUsageThroughHttpAsync` |
| Reasoning and prompt-cache usage | `OpenAiEndpointTests`, `AnthropicEndpointTests`, `GeminiEndpointTests`, `BedrockEndpointTests`, `ProviderApiDriftTests` |
| Auth, malformed bodies, unknown models, required parameters | `OpenAiEndpointTests`, `ProviderValidationBoundaryTests`, `ReviewHttpRegressionTests` |
| Configure/reset/assertions through typed control client | `OpenAiEndpointTests.ControlClient_ConfiguresReadsAssertionsAndResetsRuntimeAsync` |
| Invalid configuration preserves the active runtime after HTTP 400 | `ConfigurationBoundaryIntegrationTests` |
| Per-modality auth, wrong model kind, content filter, rate limit and reset | `ModalityFailureIntegrationTests` |
| Stored response credential/provider isolation, count/byte capacity, store=false and reset/configure | `ResponseHistoryIntegrationTests` |
| Exact conversation order, scenario credentials, exhaustion and cancellation/retry | `ScenarioFlowIntegrationTests` |
| Parallel session queues, reset/configure, credentials and admission/disposal | `SessionIsolationIntegrationTests`, `SessionBoundaryIntegrationTests` |
| Session response history, video/fault state, cancellation and in-flight deletion | `SessionStateIntegrationTests` |
| Session provider matrix, official SDK JSON/SSE, native decisions, cache and journal limits | `SessionProviderIntegrationTests` |
| Conversation branches, turns, JSON predicates, tools, replay, exhaustion and invalid graph/configuration | `ConversationHttpIntegrationTests` |
| Conversation official SDK tool loops, Responses history/replay and streaming | `ConversationSdkIntegrationTests` |
| Conversation provider/credential/session isolation, cancellation and in-flight mutation | `ConversationProviderIntegrationTests` |
| Client model presets actually reach the server | `LlmTestServerClientConfigurationTests` |
| MEAI chat, embedding and image clients call the server | `MicrosoftExtensionsAiClientTests.MicrosoftExtensionsAiClients_InvokeLlmTestServerThroughOpenAiCompatibilityEndpointsAsync` |
| Official OpenAI SDK chat, Responses, tools, embeddings and modalities | `OpenAiSdkCompatibilityTests`, `ToolFixtureClientTests` |
| Azure OpenAI deployment endpoints through official SDK | `AzureSdkCompatibilityTests` |
| Azure/Foundry v1 endpoints through official clients | `AzureV1SdkCompatibilityTests`, `V1AudioTranscriptionCompatibilityTests` |
| Foundry inference version and auth requirements | `AzureSdkCompatibilityTests`, `ReviewHttpRegressionTests.FoundryInference_RequiresDocumentedVersionWithoutConsumingQueueAsync` |
| Anthropic Messages, SSE, tool history, cache | `AnthropicEndpointTests`, `ToolFixtureEndpointTests` |
| Gemini generation, stream, embedding, video, schema options | `GeminiEndpointTests`, `FollowUpEndpointTests`, `ProviderApiDriftTests` |
| Cohere/Ollama native chat and embedding | `CohereEndpointTests`, `OllamaEndpointTests` |
| Bedrock Converse/Invoke and native binary event streams | `BedrockEndpointTests`, `BedrockSdkCompatibilityTests` |
| OpenAI-compatible provider namespaces and documented operations | `OpenAiCompatibleProviderRouteTests` |
| Provider-neutral rate limit and content filter errors | `ProviderFaultSimulationTests` |
| Tool/schema operation variants, JSON and streaming, negative then successful recovery | `ProviderCapabilityEvidenceTests.ClaimedToolAndSchemaCases_ExecutePositiveAndNegativeHttpEvidenceAsync` |
| Tool selection, local references, invalid arguments, structured stream concatenation | `ToolFixtureValidationTests` |
| Parallel-tool request limits and malformed-schema/nullable/nested constraints | `FollowUpEndpointTests`, `ToolFixtureClientTests` |
| Official SDK/MEAI function invocation, tool-result continuation and model changes | `ToolFixtureClientTests`, `LlmTestServerResponsesContinuationTests` |
| Video CRUD/content/delete/reset/provider isolation | `ReviewVideoRegressionTests`, `OpenAiEndpointTests`, `AzureSdkCompatibilityTests` |
| Native Jev/Kev/OpenAI/Clef responses, errors, images, bounds and parameter variants | `DecisionEndpointTests`, `DecisionRuntimeTests`, `DecisionValidationTests`, `DecisionQuestionTests`, `DecisionInputTests`, `DecisionFixtureContractTests` |
| Jev multipart/image/box/long-context contracts | `Jev114EndpointTests`, `Jev114MultipartTests`, `TypeSafeVisionContractTests` |
| Native Clef image/scalar/error envelopes and typed client | `CloudflareDecisionContractTests`, `CloudflareDecisionErrorTests`, `CloudflareDecisionImageTests`, `CloudflareNativeClientTests` |
| Native Kev formatting and required/optional request fields over HTTP | `SystemOneNativeContractTests` |
| Dashboard root HTML and request/response presentation | `AdminPanelTests` |
| Public request journal auth, all filter/paging parameters, validation, reset | `RequestJournalIntegrationTests` |
| Bounded request/response diagnostics preserve full HTTP response bytes | `RequestJournalIntegrationTests.Journal_BoundsRetentionWithoutTruncatingActualProviderResponsesAsync` |
| Request query filters, order, JSON/NDJSON export, retention/eviction/reset and active-request evidence | `RequestQueryExportIntegrationTests` |
| Counts/order/no-unexpected-calls, structured tool/result predicates and graph expectations | `VerificationIntegrationTests` |
| Retry group/status/count, advertised Retry-After and real monotonic wait compliance | `VerificationIntegrationTests` |
| Incomplete evidence after eviction/truncation/paging/pending requests cannot pass | `VerificationIntegrationTests` |
| Seed/fixed clock/reset/replay, provider-local ordinals, session isolation and real observations | `DeterminismIntegrationTests` |
| Native Anthropic/Gemini/Cohere/Ollama JSON/stream identity, embedding/image/Groq audio metadata | `NativeDeterminismIntegrationTests` |
| Aspire starts packaged service and exercises official SDKs through allocated endpoint | `AspireIntegrationTests` |
| Configuration key enforced by running Aspire server | `AspireAuthorizationIntegrationTests` |
| Installed NuGet assets, typed configuration, official SDK and interactive Chromium dashboard | `scripts/package-consumer/PackageTests.cs`, run by `scripts/verify-package.py` with isolated build configuration/cache |
| Actual public GitHub release and all NuGet bytes | `scripts/verify-release.py`, executed after publication |
| Clean public-feed installed consumer | `scripts/verify-package.py --public-feed`, executed after publication |

## Qualification and remaining delivery

E4 local qualification passed **812/812** running-server integrations, **91.66%** production line coverage and a
final canonical Release build with **0 warnings / 0 errors**. All **19 packages** packed successfully. The
independent clean installed NuGet consumer ran the packaged Aspire service, official SDKs, E4 typed
query/export/verification/fixed metadata and interactive Chromium acceptance (**1/1**).
Evidence logs: `artifacts/delivery/e4-build-final.log`, `e4-coverage-final.log`, `e4-pack.log`, `e4-consumer.log`.

E3 [CI 39446187097](https://github.com/managedcode/LlmTestServer/actions/runs/39446187097) passed at `3aae4db`.
E4 has not yet been pushed to branch CI. Local NuGet acceptance does not prove public-feed publication.
The [delivery plan](../Features/LlmTestServerDeliveryPlan.md) retains E5–E13, public NuGet/GHCR and launch
acceptance as unfinished. Add actual server flows for new behavior and preserve the 90% coverage gate.

Each implemented operation must be exercised with every supported parameter, positive and invalid/boundary
values, supported streaming modes, auth/version handling and stateful continuation where applicable. A passing
subset or this inventory alone does not establish full parameter coverage. New provider claims require official
method-level contracts plus passing HTTP/SDK behavior evidence.

## Native OpenAI Responses web search

The `OpenAI/NativeWebSearch*IntegrationTests.cs` suites exercise the running server through HTTP and the official Responses SDK: typed ordered outputs, citations, native SSE lifecycle, options and schema rejection, mixed function rounds, explicit/stored continuation, cache accounting, ownership, reset/cancellation, replay and bounded diagnostics. Initial qualification: 99/99 native cases and 85/85 existing provider evidence cases. The installed-package consumer adds an independent `NativePackageTests` flow; container verification covers native JSON/SSE and terminal request evidence. See [the exact supported subset and qualification](../Features/OpenAiNativeWebSearch.md). Ordinary function-only responses retain their existing output limits and are covered separately by the large-response regression.

## Native OpenAI file-search and code fixtures

The eleven `OpenAI/NativeFileCode*IntegrationTests.cs` files run 191 HTTP/official SDK cases for the independent gpt-4o-mini file-search and gpt-4.1 code-interpreter contracts: JSON/SSE, typed ordered citations, options and every supported filter operator, exact numeric comparisons, resource matching, include projection, continuation/replay, isolation, invalid-ID normalization, atomic configuration, cache read/miss and request export. Expanded web regression coverage is 105/105, including valid unused include selectors. The independent code-only full server gate passed 1158/1158 with 92.21% line coverage; see [the bounded contract and delivery evidence](../Features/OpenAiNativeFileCode.md).
