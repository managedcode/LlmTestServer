# ADR-0006: Ordered native tool fixtures on the existing conversation engine

Status: Accepted; implemented and locally qualified for the first web-search contract, public delivery pending
Date: 2026-10-20
Related: [native tool inventory](../Features/NativeBuiltInToolContractInventory.md),
[delivery plan](../Features/LlmTestServerDeliveryPlan.md),
[conversation graphs](ADR-0004-conversation-step-graphs.md),
[request evidence](ADR-0005-request-evidence-and-wire-determinism.md).

## Context and decision

A hosted provider tool returns provider-native call items, citations and lifecycle events. Treating it as a
function call loses its contract and can cause a client to attempt an external function invocation. Separate
text/function/native lists also lose the order of mixed response items and break continuation history.

Add a typed ordered output plan to the existing scenario/conversation response, chat result and retained
assistant history. Keep the current reservation, cancellation, graph progression, replay and session ownership.
The first qualified contract targets completed OpenAI Responses web searches; file retrieval, code execution,
other tool families and providers remain in the full E5/E10 roadmap and require their own adapters and evidence.

```mermaid
flowchart LR
    HTTP[Provider request] --> Adapter[Provider declaration validation]
    Adapter --> Request[Typed declarations and history]
    Request --> Engine[Existing scenario and conversation engine]
    Engine --> Validate[Fixture validation before reservation]
    Validate --> Plan[Ordered typed output plan]
    Plan --> History[Existing bounded history and replay]
    Plan --> Wire[Provider JSON and streaming mapping]
    Plan --> Evidence[Existing bounded runtime and HTTP journals]
```

## Boundaries and data

Core owns message/function/search fixture data, immutable snapshots, declaration options, semantic validation,
ordering, usage accounting and capacity admission. The provider package owns its request and output unions,
include projection, citation wire fields and streaming events. Hosting enables native contracts only on the
qualified route, reads the request, calls the runtime and writes the mapped response.

Explicit output plans and ordinary Content/ToolCalls/StreamChunks are mutually exclusive in configuration.
The engine projects a selected plan once into the existing text/function fields needed by matchers and clients,
while retaining its authoritative ordered items. Ordinary fixtures keep their existing result fields and provider fallback mapping. This avoids two
independently mutable representations in configuration JSON.

Native declarations remain distinct from function definitions. A native result requires a matching declaration
and qualified route/model contract. Named choices, none/required and parallel-call limits are checked against
the complete selected plan before queue or graph consumption. Search domain restrictions apply to scripted
sources and citations. Context/location options are retained for request identity and diagnostics; no live search
is performed. Newer unimplemented native options are rejected explicitly rather than silently discarded.

## History, identity and accounting

All native items and the final assistant message belong to one logical assistant response. Explicit prior output
items and stored-response continuation preserve their types, order, sources and citations. Native items never
become synthetic user JSON or client tool-result messages. Stored-response access retains the existing provider
namespace and credential boundary inside its session runtime. Conversation labels remain correlation/graph
identities; an explicit previous_response_id selects the parent history.

Deep-copy nested collections at configuration, history, replay and journal boundaries. Include native payloads
in existing conversation/history byte limits and journal character budgets. Capacity rejection must not advance
a response/step or mutate the prompt cache. Native declaration options participate in request fingerprints,
cache identity and normalized input usage; native history/result payloads participate in normalized token counts.
Cache and reasoning remain subsets of input/output totals.

Explicit fixture IDs and existing seeded wire metadata preserve deterministic response identities. Native
stream progress, output-item completion, citations and final completion must agree with ordinary JSON output.
The terminal marker is observed only after the final response event has been written and flushed.

## First contract and remaining breadth

The initial OpenAI contract uses gpt-4.1-mini, completed search actions, typed source URLs, URL citations and
JSON/SSE Responses. The web-search guide reserves open_page/find_in_page for reasoning models; they require
separate model-qualified implementation. The upstream citation schema does not establish an encoding convention
for non-ASCII indexes. The initial cited-text fixture subset is explicitly ASCII, preserving supplied indexes;
uncited text retains normal Unicode support. This boundary is visible in the feature/support inventory and does
not remove Unicode qualification or other native tools from the delivery plan.

No network search, code executor, file downloader, second work queue or unbounded resource registry is added.
Files, vector stores, containers and generated-artifact lifecycle will reuse the appropriate session-owned
bounded state when their E9 contracts are implemented.

## Alternatives and consequences

- A function-name shim would emit the wrong SDK item/event types and misrepresent hosted execution.
- Provider JSON embedded as arbitrary core strings would bypass typed validation, budgets and history semantics.
- A separate native scenario queue would split consumption, cancellation and replay ownership.
- Mapping validation after CompleteChatAsync would reject a fixture only after consuming its queue/graph step.

The chosen model adds explicit union/snapshot handling to existing state paths. Its cost is additional typed
mapping and budget coverage; it keeps one engine and permits later native contracts to reuse ordering and
ownership without pretending they share a provider wire format.

## Verification

Use running ASP.NET endpoints and the installed official Responses SDK. NativeWebSearch*IntegrationTests must
prove typed JSON and streaming, mixed output order, include gating, explicit/stored continuation, named choices,
invalid declarations and atomic configure failure, domain mismatch without consumption, route/model boundaries,
reset/cancellation/replay, credential/session isolation and capacity limits. Provider contract/evidence suites
and existing function/structured-output integrations remain mandatory regressions.

Run the repository format check, canonical Release build, complete integration suite and coverage gate; pack
all packages and qualify a clean installed-package consumer before claiming delivery. Qualification results are
recorded in the delivery ledger only after the actual commands pass. Public publication remains the single
owner-approved v1.0.0 launch batch.

## References

- [OpenAI web search](https://developers.openai.com/api/docs/guides/tools-web-search)
- [Responses create](https://developers.openai.com/api/reference/resources/responses/methods/create)
- [Responses streaming schema](https://developers.openai.com/api/reference/resources/responses/streaming-events)
