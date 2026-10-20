# Stream delivery faults

Status: **E6 text SSE delivery faults qualified through 42/42 running-server integration cases** across
OpenAI Chat Completions, OpenAI Responses, Anthropic Messages and Gemini. Delivery and cancellation cases
use the official OpenAI 2.14.0, Anthropic 12.58.0 (strict response validation) and Google.GenAI 1.26.1 clients
over real Kestrel/TCP. Replay identity also uses official SDKs against hosted ASP.NET Core HTTP routes;
raw HTTP supplements transport boundaries, admission and invalid-configuration checks. A fresh installed-package Aspire consumer
passed **9/9** cases. The SDK-built container also passed acceptance using the README's SDK publish command.
The combined code-only integration gate passed **1,359/1,359**, with **92.52%** production line coverage.
Canonical formatting and the final Release build passed with no warnings or errors.

## Configure an explicit delivery plan

`LlmTestServerScenarioResponse.StreamDelivery` accepts a `LlmTestServerStreamDelivery` from the
`ManagedCode.LlmTestServer.Scenarios` namespace. Scenario and conversation-step builders expose
`WithStreamDelivery(...)`, applied to the preceding response:

```csharp
config.UseChatModel(LlmTestServerKnownModelIds.Gpt55)
    .UseChatScenario("partial-text", scenario => scenario
        .ForModel(LlmTestServerKnownModelIds.Gpt55)
        .Responds("First second.", "First ", "second.")
        .WithStreamDelivery(new()
        {
            Mode = LlmTestServerStreamDeliveryMode.EarlyCleanEnd,
            AfterContentFrames = 1,
        })
        .Responds("Explicit next attempt."));
```

The plan belongs to the fixture, not to an upstream request option. It does not introduce a provider
header, environment toggle or alternate endpoint.

| Mode | Behavior after the selected content frame has been written and flushed |
| --- | --- |
| `EarlyCleanEnd` | End the HTTP response normally, omitting remaining content and the selected protocol's terminal sequence |
| `DelayThenContinue` | Pause once for `DelayMilliseconds`, then resume the remaining content and ordinary terminal sequence if the request remains active |
| `AbortConnection` | Abort the server request transport, omitting remaining content and protocol-terminal frames |

`AfterContentFrames` defaults to 1. It must be 1–128 and cannot exceed the configured content-frame
count of nonempty text chunks. Empty chunks do not count; an empty response or unreachable boundary
is rejected at configuration. A nonempty response without explicit chunks has one frame. Delay mode requires 1–60,000 milliseconds;
other modes require zero delay. These are bounded local fixture policies. Explicit chunks must
concatenate exactly to the response content. Invalid plans fail configuration atomically.

A plan applies only to plain-text responses. Scripted provider errors, ordinary function calls and
ordered/native output items cannot share that response. The qualified contract admits these exact routes:

| Surface | Request | Counted text frame | Ordinary successful protocol ending |
| --- | --- | --- | --- |
| OpenAI Chat Completions | `POST /openai/v1/chat/completions`, `stream: true` | Nonempty content chunk | Finish-reason chunk, requested usage chunk, `[DONE]` |
| OpenAI Responses | `POST /openai/v1/responses`, `stream: true` | Nonempty `response.output_text.delta` | Text/content/output done events, then `response.completed` |
| Anthropic Messages | `POST /anthropic/v1/messages`, `stream: true` | Nonempty `content_block_delta` text delta | Content-block stop, message delta, `message_stop` |
| Gemini | `POST /gemini/v1beta/models/{model}:streamGenerateContent?alt=sse` | Nonempty text candidate payload | Final candidate `finishReason: STOP` and usage |

Start/progress/empty frames do not advance the delivery boundary. Gemini requires exactly one `alt=sse`;
its JSON-array streaming transport is excluded. Non-streaming requests, other provider roots, media
streaming, tool declarations/native includes/tool-bearing history and structured-output requests are rejected
as fixture mismatches before the selected response is consumed. Native capability metadata alone does not
mean a request declared a tool. Do not infer support for another route because it shares a mapper.
Anthropic cache-population-only requests (`max_tokens: 0`) return before response selection: they warm
prompt-cache state without consuming a queued response or executing a graph step. Their planless response
emits no text and applies no delivery fault. A subsequent generation still selects the configured fault fixture.

## Inference commit and delivery are different boundaries

The runtime computes and commits the complete deterministic inference result before the HTTP writer
starts delivering it. The selected queue entry or conversation step, retained response and successful
prompt-cache effects are therefore already committed when the delivery plan fires. A delivery failure
can leave the client with only a prefix while the runtime retains the full logical answer.

An explicit retry against a flat response queue selects its next response. The server does not retry or
roll back the committed answer because a terminal marker was lost. A repeated request that qualifies
for existing conversation replay returns the retained result and reapplies its delivery plan without
executing the step again. Replay therefore reproduces the fault rather than repairing the first
transport. A new valid conversation turn advances according to the configured graph.

Request ID, streaming transport flag, delivery-surface marker and previous-response lookup ID are excluded
from the logical inference fingerprint. Changing only JSON/SSE delivery therefore does not create a new
logical turn; replay still validates the selected fixture against the requested transport. A delivery fault
cannot be replayed through an unsupported JSON/array transport to bypass its SSE applicability rule.

Conversation ownership includes the provider namespace, conversation label and credential hash inside the
owning test session. Keep the same SDK credential when comparing SDK and raw HTTP requests or replaying a
turn, even when the host does not require authentication. A different supplied credential selects a different
conversation owner; the conversation label alone is not authorization.

This differs from pre-inference `.DelaysBy(...)`: cancellation before the existing runtime commit can
release a reservation. Cancellation during the new delivery pause happens after inference commit.
The delivery delay uses real elapsed time even when provider wire metadata uses a fixed fixture clock.

## Transport completion and protocol completion

An HTTP 200 and clean response-body EOF do not prove a complete chat stream. OpenAI Chat Completions
uses data-only SSE; a normal stream has its final finish-reason chunk and `data: [DONE]`. Requested
usage appears in a separate final chunk before `[DONE]`; interrupted streams can omit it.
[Official streaming guide](https://developers.openai.com/api/docs/guides/streaming-responses),
[Chat Completions streaming contract](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create).

The pinned OpenAI .NET 2.14.0 API exposes `CompleteChatStreamingAsync` and typed
`StreamingChatCompletionUpdate.ContentUpdate`, `FinishReason` and `Usage`. Its SSE enumerator can
finish on either `[DONE]` or clean EOF, without itself requiring a finish reason. Thus the expected
clean-end test observes a prefix and missing terminal evidence; it must not assume an SDK exception.
Running-server tests confirm clean EOF produces a partial result without a finish reason, while the
real Kestrel abort interrupts the official client. Tests forward unchanged bytes and coordinate the
first actual SDK content update before releasing the server flush, avoiding a scheduling race.
[Package-pinned SDK parser](https://github.com/openai/openai-dotnet/blob/2e77b08828145f658ec04e49aec87abb1543c553/OpenAI/src/Utility/AsyncSseUpdateCollection.cs),
[pinned chat client](https://github.com/openai/openai-dotnet/blob/2e77b08828145f658ec04e49aec87abb1543c553/OpenAI/src/Custom/Chat/ChatClient.cs).

### Qualified SDK parser boundaries

The official OpenAI 2.14 Responses client also uses the common SSE collection. A frame-aligned clean
EOF can therefore end typed update enumeration without `response.completed`; assert the typed text prefix
and missing completion, rather than assuming an exception or requiring a Chat-style `[DONE]`.
[Responses client source](https://github.com/openai/openai-dotnet/blob/2e77b08828145f658ec04e49aec87abb1543c553/OpenAI.Responses/src/Custom/ResponsesClient.cs).

Anthropic 12.58 strict response validation validates each event, but raw typed stream enumeration does
not require `message_stop` before EOF. Its separate official `.Aggregate()` helper requires a stop event
and throws for a partial message. Normal message completion ends with `message_stop`.
[Streaming contract](https://platform.claude.com/docs/en/build-with-claude/streaming),
[SDK event reader](https://github.com/anthropics/anthropic-sdk-csharp/blob/Anthropic-v12.58.0/src/Anthropic/Core/Sse.cs),
[SDK aggregator](https://github.com/anthropics/anthropic-sdk-csharp/blob/Anthropic-v12.58.0/src/Anthropic/Helpers/MessageContentAggregator.cs).

Google.GenAI 1.26.1 accepts EOF with no buffered partial SSE frame and does not enforce a finish reason.
A remaining incomplete JSON segment instead causes a parser error. A missing `finishReason` means the
model has not stopped generating; for this text fixture, successful continuation ends with `STOP`.
These faults flush whole SSE frames, so clean early end differs from byte truncation.
[Gemini contract](https://ai.google.dev/api/generate-content#method:-models.streamgeneratecontent),
[pinned HTTP parser](https://github.com/googleapis/dotnet-genai/blob/c071e7d18f779bf166a66c0a7ff715ab24035273/Google.GenAI/HttpApiClient.cs).

The focused integrations confirm clean EOF returns the observed text without a terminal event on all four
routes, real connection abort interrupts the clients, and delay continuation/cancellation preserves the
recorded prefix and transport outcome. Tests disable ordinary SDK retries and Google's
`GenerateContentConfig.AutomaticContinuation` to prove individual attempts. They also cover the boundary
after **all text** has arrived: complete text alone still does not imply protocol completion when the final
finish/completion/stop event is missing, and `stream.terminal` verification fails.

## Public evidence and qualification boundary

Request diagnostics expose `StreamDelivery`, `StreamDeliveryApplied` and `EmittedContentFrames`
alongside the existing transport state, status, response preview, timing and
`TerminalMarkerObserved`. A configured plan and a reached trigger are distinct observations.
An ordinary response EOF can have completed transport with no protocol terminal; an aborted transport
must not acquire a terminal marker. Query/export preserves this structured evidence under its existing
retention bounds. Runtime events describe committed inference; transport traces describe actual delivery.
Neither record alone proves the client received the full answer.

The verification API's `stream.terminal` check can detect missing protocol completion. Consumer retry
policies should be disabled when proving individual attempts. Test the same configured plan through
the applicable official provider client over a running Kestrel server, verify the actual emitted prefix and
finish/usage behavior, inspect request evidence, exercise explicit retry and graph replay, and confirm
that a subsequent health/provider request still works. Delay/cancellation tests must coordinate actual
first-frame delivery rather than rely on a scheduling race.

Malformed SSE/JSON, byte-level truncation, tool/native/media streams, output-stage content filtering,
provider families outside the four listed routes and resource/process termination remain separate unqualified delivery-fault
contracts. This bounded text slice does not claim all E6 capabilities.

The cache regression confirms that a partial stream retains its already committed prompt-cache entry:
the explicit next request with identical input reports positive cached tokens in both the official SDK
and public request trace. Test hosts warm health/endpoint metadata over real HTTP before starting the
stream watchdog. Cancellation starts after the first real SDK text update, and tests drain completion
traces before cleanup/next assertions. Health warmup and trace draining synchronize test lifecycle; they
neither retry inference nor change provider behavior, fault boundaries or committed state.

The 42-case focused evidence is recorded in `artifacts/delivery/e6-multi-stable-sdk.log`. All 19 NuGet
packages were packed, and `artifacts/delivery/e6-multi-installed-consumer.log` records the fresh Aspire
consumer's 9/9 cases, including delivery through the official clients and the TypeSafe/Jev example.
Standalone acceptance used an image built by the .NET SDK's `PublishContainer` target with the README's
single-platform command. These are local qualification results; public package/image publication remains
part of the owner-merged release gate.
