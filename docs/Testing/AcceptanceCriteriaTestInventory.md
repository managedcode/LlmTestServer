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
| Aspire starts packaged service and exercises official SDKs through allocated endpoint | `AspireIntegrationTests` |
| New/legacy configuration key precedence enforced by running Aspire server | `AspireAuthorizationIntegrationTests` |
| Installed NuGet assets, typed configuration, official SDK and interactive Chromium dashboard | `scripts/package-consumer/PackageTests.cs`, run by `scripts/verify-package.py` with isolated build configuration/cache |
| Actual public GitHub release and all NuGet bytes | `scripts/verify-release.py`, executed after publication |
| Clean public-feed installed consumer | `scripts/verify-package.py --public-feed`, executed after publication |

## Migration qualification still in progress

Removing old internal tests does not prove equivalent public API coverage. The remaining migration obligations
are tracked in `artifacts/llm-test-server-migration/integration-parity.json` and the implementation ledger in
[the migration plan](../Features/LlmTestServerMigrationPlan.md). Remaining work includes final coverage qualification, installed-package browser flows and container
execution; the configuration/history/cancellation integrations passed in the 629/629 integration-only suite (90.86% production line coverage). Add real server flows for uncovered behavior; do not restore internal tests or lower the 90% coverage gate.

Each implemented operation must be exercised with every supported parameter, positive and invalid/boundary
values, supported streaming modes, auth/version handling and stateful continuation where applicable. A passing
subset or this inventory alone does not establish full parameter coverage. New provider claims require official
method-level contracts plus passing HTTP/SDK behavior evidence.
