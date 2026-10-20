# Stream delivery faults

Status: **E6 first slice qualified through 20 running-server integration cases.** This contract is limited
to text SSE on `POST /openai/v1/chat/completions` with `stream: true`. Tests use the official OpenAI SDK
over real Kestrel/TCP; raw HTTP supplements invalid configuration. The full code-only integration gate
passed 1,337 cases with 92.51% production line coverage. Other streaming families remain outside this slice.

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
| `EarlyCleanEnd` | End the HTTP response normally, omitting remaining content, finish-reason/usage frames and `[DONE]` |
| `DelayThenContinue` | Pause once for `DelayMilliseconds`, then resume the remaining content and ordinary terminal sequence if the request remains active |
| `AbortConnection` | Abort the server request transport, omitting remaining content and protocol-terminal frames |

`AfterContentFrames` defaults to 1. It must be 1–128 and cannot exceed the configured content-frame
count. A response without explicit chunks has one frame. Delay mode requires 1–60,000 milliseconds;
other modes require zero delay. These are bounded local fixture policies. Explicit chunks must
concatenate exactly to the response content. Invalid plans fail configuration atomically.

A plan applies only to plain-text responses. Scripted provider errors, ordinary function calls and
ordered/native output items cannot share that response. Non-streaming requests, other provider roots,
Responses/media streaming, tool-bearing requests/history and structured-output requests are rejected
as fixture mismatches before the selected response is consumed. Do not infer support for another route
because it also uses an OpenAI-compatible mapper.

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
the official OpenAI client over a running Kestrel server, verify the actual emitted prefix and
finish/usage behavior, inspect request evidence, exercise explicit retry and graph replay, and confirm
that a subsequent health/provider request still works. Delay/cancellation tests must coordinate actual
first-frame delivery rather than rely on a scheduling race.

Malformed SSE/JSON, byte-level truncation, tool/native/media streams, output-stage content filtering,
other provider families and resource/process termination remain separate unqualified delivery-fault
contracts. This first slice does not claim all E6 capabilities.

The cache regression confirms that a partial stream retains its already committed prompt-cache entry:
the explicit next request with identical input reports positive cached tokens in both the official SDK
and public request trace. Startup watchdogs are separate from the actual cancellation deadline.

All 19 NuGet packages were packed. A fresh installed-package Aspire consumer passed six cases,
including the public delivery-plan SDK flow and the TypeSafe/Jev README example. The SDK-built
linux/arm64 container also passed the standalone HTTP acceptance suite.
