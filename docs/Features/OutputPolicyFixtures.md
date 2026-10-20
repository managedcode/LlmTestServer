# Output policy fixtures

Status: the initial E6 text output-policy slice passed **57/57 focused running-server SDK integration cases**.
This checkpoint covers the mappings below with OpenAI 2.14.0, Anthropic 12.58.0 with strict response
validation, Google.GenAI 1.26.1 and the package's `Microsoft.Extensions.AI` client. The combined full run passed **1,416/1,416 integration cases** with **92.57% production line
coverage**. These results do not close the remaining E6 catalogue or qualify other provider surfaces.

## Configure an explicit output outcome

`LlmTestServerScenarioResponse.OutputDisposition` accepts an immutable
`LlmTestServerOutputDisposition` from `ManagedCode.LlmTestServer.Scenarios`. `Kind` is
`LlmTestServerOutputDispositionKind.Filtered` or `.Refused`. Optional `Category` and `Explanation`
carry bounded outcome metadata. A null disposition preserves the ordinary completion behavior.

Scenario and conversation-step builders expose `WithOutputDisposition(...)`, applied after a response:

```csharp
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;

await control.ConfigureAsync(config => config
    .UseGpt55()
    .UseChatScenario("policy-and-recovery", scenario => scenario
        .ForModel(LlmTestServerKnownModelIds.Gpt55)
        .Responds("I cannot provide that response.", "I cannot ", "provide that response.")
        .WithOutputDisposition(new()
        {
            Kind = LlmTestServerOutputDispositionKind.Refused,
            Category = "general_harms",
            Explanation = "Explicit deterministic refusal fixture.",
        })
        .Responds("Explicit next attempt.")));
```

Use `.Filtered` instead for a filtering fixture and configure an explicit generated prefix or empty
content. `Content` and `StreamChunks` supply deterministic fixture output, including refusal text;
the disposition selects its native representation. The server does not generate a refusal explanation,
execute a policy service or call an upstream model. Existing configure/reset APIs remain the control surface.

`WithOutputDisposition` requires a preceding response. JSON configuration accepts the same typed shape:

```json
{
  "content": "Explicit refusal text.",
  "streamChunks": ["Explicit ", "refusal text."],
  "outputDisposition": {
    "kind": "Refused",
    "category": "general_harms",
    "explanation": "Explicit fixture metadata."
  }
}
```

This is a response entry inside the existing configuration, not a complete configuration document.

## Bounds and admission

- `Kind` must be one of the two named values; the default/unknown enum value is invalid.
- `Category` is optional and limited to 256 C# string characters; `Explanation` is optional and limited
  to 4,096. These are local fixture bounds, not advertised upstream limits.
- Explicit chunks must concatenate exactly to `Content`. Empty output is supported.
- A policy response cannot also contain a scripted `Error`, `StreamDelivery`, ordinary `ToolCalls`
  or ordered/native `OutputItems`.
- Invalid configuration is rejected atomically with HTTP 400, leaving the previous configuration usable.
- A valid fixture selected by an unsupported route/kind pair, tool declaration or tool-bearing/native
  history, native includes or structured-output request is rejected with a fixture-mismatch HTTP 409
  before queue/graph consumption and cache population.

For Anthropic refusal, `Category` must be null or `cyber`, `bio`, `frontier_llm`, `reasoning_extraction`
or `general_harms`. A different bounded generic category is rejected at Anthropic fixture admission;
the strict SDK's declared category contract is the reference. Other surfaces retain generic metadata
in diagnostics rather than inventing provider fields. See the official
[RefusalStopDetails type](https://github.com/anthropics/anthropic-sdk-csharp/blob/Anthropic-v12.58.0/src/Anthropic/Models/Messages/RefusalStopDetails.cs).

## Qualified native mappings

These are normal HTTP 200 output outcomes. Inspect their native fields; do not infer an exception
from the policy outcome alone.

| Exact provider route | `Filtered` | `Refused` |
| --- | --- | --- |
| `/openai/v1/chat/completions` | Final `finish_reason: content_filter`; explicit content/prefix | `message.refusal`, null ordinary content; streamed `delta.refusal`, then normal stop/terminal sequence |
| `/openai/v1/responses` | `status: incomplete`, `incomplete_details.reason: content_filter`; SSE ends with `response.incomplete` | Typed `refusal` content part; refusal delta/done events; completed response |
| `/anthropic/v1/messages` | Unsupported pairing: fixture mismatch | `stop_reason: refusal` and `stop_details` with type `refusal` and required nullable category/explanation; final `message_stop` |
| `/gemini/v1beta/models/{model}:generateContent` and `:streamGenerateContent` | Terminal candidate `finishReason: SAFETY`; terminal content omitted | Unsupported pairing: fixture mismatch |

The OpenAI mappings follow the official [Chat completion contract](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create)
and [Responses streaming events](https://developers.openai.com/api/reference/resources/responses/streaming-events).
Anthropic refusal is a returned outcome distinct from an SSE error event; see
[refusals and fallback](https://platform.claude.com/docs/en/build-with-claude/refusals-and-fallback)
and [streaming](https://platform.claude.com/docs/en/build-with-claude/streaming).
Gemini output `SAFETY` is separate from prompt blocking with `promptFeedback`; this fixture implements
only the candidate output case described by the [GenerateContent API](https://ai.google.dev/api/generate-content).

Both ordinary JSON responses and text SSE are qualified. Gemini's existing JSON-array streaming
transport also accepts this policy outcome; its separate post-header delivery-fault restriction to
`alt=sse` does not apply here. A filtered Gemini JSON/final candidate has no `content`, so its SDK
`Text` can be empty. Earlier SSE candidates can already have delivered the explicitly configured
prefix. Runtime usage describes the configured generated fixture text; omitted final JSON content
does not retroactively erase generated prefix tokens. This is deterministic fixture accounting,
not a claim of upstream billing or inference equivalence.

## Policy completion, input rejection and broken delivery

`SimulateContentFilter(...)` retains its existing request/history filtering behavior: a matching input
fails with HTTP 400 and `content_filter` before generation. `Fails(...)` remains an explicit provider
HTTP failure. Neither is a substitute for the native HTTP 200 output outcomes above.

A policy outcome has a valid protocol ending. OpenAI Chat still finishes its SSE sequence; Responses
uses `response.incomplete` for filtering or `response.completed` for refusal; Anthropic emits
`message_stop`; Gemini emits its terminal `SAFETY` candidate. Once the final sequence is actually
flushed, `TerminalMarkerObserved` is true and terminal-marker verification can pass. Responses
`incomplete` describes the logical output, not a missing transport terminal.

Use [stream delivery faults](StreamDeliveryFaults.md) for early EOF, post-frame delay or connection
abort. Combining a delivery plan with a policy outcome is rejected rather than assigning an
unqualified precedence. This slice does not simulate Responses `failed`, a provider SSE `error`
event, Gemini prompt blocking, or a native tool's own error result.

## Queue, graph, cache and history semantics

A native policy outcome uses the existing successful inference commit path. It consumes the selected
queued response or graph step, records actual explicit output usage, populates the normal qualified
prompt cache and retains an eligible stored response. No separate queue, executor, retry service or
history store is introduced. An explicit next attempt against a flat queue selects its next fixture.
Graph replay returns the committed policy outcome without executing another step; session/provider/
credential scope continues to use the existing owners.

Anthropic `max_tokens: 0` remains cache-population-only. It returns before response selection and
produces a new planless result: no output disposition is selected, no queued policy response is
consumed and no graph step executes. A subsequent nonzero generation still selects the configured
policy fixture.

History preserves the input representation available in the native protocol:

- An OpenAI refusal is retained as assistant `Content` plus
  `LlmTestServerMessage.OutputDisposition.Kind = Refused`. In history, category and explanation are
  stripped because native refusal input parts do not carry them.
- Filtered visible text is retained as ordinary assistant prefix input. The prior output-envelope
  filter status is not an input-part field, so history has no invented `Filtered` marker.
- The original result, runtime event and request trace retain the full configured outcome metadata.
  Input history is not a replacement for that output diagnostic evidence.

This canonical projection is shared by stored-response restoration, history snapshots, cache keys,
conversation fingerprints and message matching. Automatic `previous_response_id` continuation and
canonical manual SDK history therefore replay the same graph turn. Bounds/enums are validated before
projection. Metadata is excluded from model text tokens while retained objects/strings remain charged
to their graph/history/event/trace budgets; exceptional diagnostic truncation remains visibly marked.

OpenAI Chat accepts pure native refusal fields or pure typed refusal content parts, including empty
refusal text. Responses accepts its native refusal part. Mixed ordinary text/refusal history and
ambiguous simultaneous refusal representations are outside this qualified subset and are rejected;
do not flatten them into an unmarked assistant answer. Native ordered output items are unchanged.

## Microsoft.Extensions.AI client

The package-owned `LlmTestServerChatClient` returns refusal text as `ErrorContent` with
`ErrorCode = "Refusal"`, preserving it separately from ordinary `TextContent`. A streamed refusal is
accumulated into that typed content, and a follow-up request maps it back to native refusal history.
Filtering retains the native `content_filter` finish reason. HTTP 200 policy outcomes do not become
fabricated HTTP failures or automatic thrown exceptions in this wrapper.

`ErrorContent` is an official AI-content abstraction; this client uses it for the refusal convention above. See its
[upstream source](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI.Abstractions/Contents/ErrorContent.cs).
Applications can choose their own handling of that returned content. This wrapper convention does
not replace the official SDK's distinct native refusal and filtering types.

## Evidence and remaining E6 scope

Focused tests under `tests/ManagedCode.LlmTestServer.Tests/Policy` start Kestrel and exercise real
HTTP/SDK behavior: typed JSON/SSE outcomes, valid terminal evidence, empty/prefix text, strict refusal
details, explicit recovery, graph replay, session/cache isolation, manual/stored canonical history,
metadata bounds, unsupported combinations and the existing input-filter distinction. Raw HTTP
supplements configuration and Gemini array boundaries. SDK automatic retries are disabled; Google
automatic continuation is also disabled. The current focused result is 57/57, including malformed refusal-history admission and bounded
diagnostic retention. The combined run passed 1,416/1,416 with 92.57% line coverage; a fresh independent
NuGet consumer passed 15/15 cases, including six policy/recovery cases through installed packages,
Aspire and the original provider SDKs. All 19 packages were packed, and the .NET SDK-built linux/arm64
container passed standalone health, dashboard, authorization, management, JSON/SSE and native-tool checks.

The larger E6 objective remains open for additional provider-specific outcomes and transports,
including in-stream API error events and separately qualified semantics for other provider families.
This document does not claim output-policy support for Azure OpenAI, Foundry, Bedrock, Groq, Mistral,
Ollama, Cohere, OpenRouter, DeepSeek, Perplexity, System One decision profiles, Cloudflare decision
profiles or media/native-tool/structured-output combinations. Shared mapper code alone is not
qualification. See [the delivery plan](LlmTestServerDeliveryPlan.md) for the remaining work.
