# Admin API

Configure fixtures, reset a test run and verify the requests your application sent. The API is available from
any language over HTTP; the .NET `LlmTestServerClient` calls the same endpoints.
Back to the [README](../../README.md).

All paths below are relative to the server URL, for example `http://localhost:8080` for the
[Docker quick start](../../README.md#1-start-the-server). Provider SDKs use their own
[namespaced API roots](ProvidersAndRoutes.md); the browser dashboard is at `/`.

## Authentication and scope

- Send `Authorization: Bearer test-key` when the server requires that key.
- Send JSON bodies with `Content-Type: application/json`. Property names use camel case;
  configuration enums accept names such as `"chat"` and `"contains"`.
- Without session headers, requests operate on the default runtime.
- For an isolated session, send both `X-Llm-Test-Server-Session` and `X-Llm-Test-Server-Session-Key`
  on provider and control calls. Bearer authentication still uses that runtime's configured key.

`configure` authenticates with the **current** key. If its body changes `requiredBearerToken`, use the new
key on subsequent requests. Keep `requiredBearerToken` in the replacement configuration to retain authentication.

## Configure a chat

After starting the server, run this once before a language example in the README. It registers a model ID and
two queued replies. No .NET installation is needed to configure the Docker server.

```sh
curl --fail-with-body http://localhost:8080/admin-api/configure \
  -H 'Authorization: Bearer test-key' \
  -H 'Content-Type: application/json' \
  --data-binary '{
    "requiredBearerToken": "test-key",
    "models": [{ "id": "gpt-6.1-sol", "kind": "chat" }],
    "chatScenarios": [{
      "id": "delivery",
      "modelId": "gpt-6.1-sol",
      "responses": [
        { "content": "Your order arrives on Friday." },
        { "content": "Delivery is free." }
      ]
    }]
  }'
```

Success: HTTP `200`, `{"status":"configured"}`.

Point your official SDK at `http://localhost:8080/openai/v1`, with model `gpt-6.1-sol` and key `test-key`.
The first matching request gets `Your order arrives on Friday.`; the second gets `Delivery is free.`.
Normal and streaming requests consume the same queue. A third returns `409` with
`llm_test_server_scenario_exhausted`. Reset before rerunning the example.

The scenario above matches any prompt for that model. To restrict it to a user message, add:

```json
"match": {
  "mode": "contains",
  "messages": [{ "role": "user", "content": "order" }]
}
```

Every configured match message must occur in the request. For a complete prompt contract, use `"mode":"exact"`.
The server sends the scripted replies in order; it does not derive answers from the prompt.

For explicit streaming chunks, a response can be
`{"content":"Delivery is free.","streamChunks":["Delivery ","is free."]}`.
`content` is the normal reply; `streamChunks` controls delivery when the SDK requests streaming.

### Configuration body

`POST /admin-api/configure` accepts
[`LlmTestServerConfiguration`](../../src/ManagedCode.LlmTestServer/Configuration/LlmTestServerConfiguration.cs).
It **replaces** the selected runtime's configuration. Include every model, scenario and fixture needed by the test;
omitted properties use the DTO defaults. Successful configuration clears previous request evidence, assertions,
scenario positions, conversations, stored response history, cache state, fault counters and video jobs.

| JSON property | Purpose and detailed contract |
| --- | --- |
| `requiredBearerToken` | Key required by provider and control calls; omitted/null means no global key requirement |
| `models` | Registered model IDs and kinds: `chat`, `embedding`, `image`, `audio`, `video`, `decision` |
| `chatScenarios` | Model, prompt matcher and ordered replies, tool calls or errors; [scenario configuration](Configuration.md#chat-scenarios) |
| `datasets` | Groups of scenarios; [dataset configuration](Configuration.md#configure-before-a-test) |
| `decisionScenarios` | Native TypeSafe, Kev and Cloudflare decision fixtures; [decision models](../Features/DecisionModels.md) |
| `faultSimulation` | Provider-neutral rate limits and content filters; [fault simulation](Configuration.md#fault-simulation) |
| `defaultEmbeddingVector`, `defaultImageDataUri` | Deterministic embedding and image output; [modalities](Configuration.md#modalities) |
| `defaultAudioBytes`, `defaultAudioMediaType` | Base64 audio bytes and MIME type for speech |
| `defaultTranscriptionText`, `defaultTranscriptionUsage`, `defaultTranslationText` | Transcription/translation output; null transcription usage simulates omitted usage |
| `defaultVideoBytes`, `defaultVideoMediaType` | Base64 video bytes and MIME type |
| `defaultVideoId`, `defaultVideoGenerationId`, `defaultVideoCreatedAtUnixTime`, `defaultVideoSize`, `defaultVideoSeconds` | Video fixture metadata; [modalities](Configuration.md#modalities) |
| `determinism` | Seed and fixed wire clock; [wire determinism](../Features/RequestVerificationAndDeterminism.md#seeded-wire-metadata) |
| `maxConversations`, `maxConversationBytes` | Conversation state limits; [conversation DSL](../Features/ConversationDSL.md) |
| `maxPromptCacheEntries` | Retained cache prefix limit; zero disables caching |
| `maxStoredChatResponses`, `maxStoredChatResponseBytes` | Stored response history limits; zero responses disables storage |
| `maxVideoJobs`, `maxVideoBytes` | Video job and payload limits |

A model has `id`, `kind`, optional `ownedBy`, `reasoningTokens` and `decisionProvider`.
`reasoningTokens` configures deterministic reasoning usage when needed; it is optional for chat setup.
A chat scenario has `id`, `modelId`, optional `requiredBearerToken`, `match`, `responses` and an optional
[`conversation` graph](../Features/ConversationDSL.md).
Each response can carry `content`, `streamChunks`, `toolCalls`, `outputItems`, `error`, `delayMilliseconds`,
[`streamDelivery`](../Features/StreamDeliveryFaults.md) and
[`outputDisposition`](../Features/OutputPolicyFixtures.md).
See [tools and structured output](../Features/ToolAndStructuredFixtures.md) for tool fixtures.

## Configure a branching conversation

The [support flow in the README](../../README.md#test-conversations-that-branch) is also plain JSON.
Send this body to `POST /admin-api/configure` with the same headers as the chat setup above:

```json
{
  "requiredBearerToken": "test-key",
  "models": [{ "id": "gpt-6.1-sol", "kind": "chat" }],
  "chatScenarios": [{
    "id": "support",
    "modelId": "gpt-6.1-sol",
    "conversation": {
      "startStepId": "welcome",
      "maxSteps": 12,
      "steps": [
        {
          "id": "welcome",
          "match": { "latestRole": "user", "textEquals": "help" },
          "response": { "content": "Do you need a refund or delivery tracking?" },
          "nextSteps": ["refund", "delivery", "clarify"]
        },
        {
          "id": "refund",
          "match": { "latestRole": "user", "textContains": "refund" },
          "response": { "content": "Reply confirm or cancel." },
          "nextSteps": ["refunded", "cancelled"]
        },
        {
          "id": "refunded",
          "match": { "latestRole": "user", "textEquals": "confirm" },
          "response": { "content": "Your refund is complete." },
          "terminal": true
        },
        {
          "id": "cancelled",
          "match": { "latestRole": "user", "textEquals": "cancel" },
          "response": { "content": "Your order is unchanged." },
          "terminal": true
        },
        {
          "id": "delivery",
          "match": { "latestRole": "user", "textContains": "delivery" },
          "response": { "content": "Your order arrives on Friday." },
          "terminal": true
        },
        {
          "id": "clarify",
          "match": { "latestRole": "user" },
          "response": { "content": "Please choose refund or delivery." },
          "nextSteps": ["refund", "delivery", "clarify"]
        }
      ]
    }
  }]
}
```

Use `conversation` for the graph and leave the scenario's linear `responses` queue empty.
Each step has exactly one `response` plus either `nextSteps` or `terminal: true`.
The runtime examines the targets of the current step in order and chooses the first whose predicates all match.
The `clarify` catch-all comes last. Its self-transition lets a later message choose again; `maxSteps` bounds loops.

Set `X-Llm-Test-Server-Conversation` to a distinct ID for each dialogue. Send full user/assistant history through
the provider SDK on each turn. Both ordinary and streaming calls use the graph. To try another path, use a new
conversation ID or reset the runtime. `GET /admin-api/conversations` shows visited, next and unvisited steps.

Available step match properties are `latestRole`, `textEquals`, `textContains`, `turnIndex`, `toolName`,
`toolCallId`, `toolArgumentsJson` and `jsonFields` (`path` plus JSON-encoded `equalsJson`).
Graphs also support `replaySuccessfulRequests` and `exhaustion`: `reject` (default), `repeatLastResponse` or
`restart`. See [all predicate, replay and terminal rules](../Features/ConversationDSL.md#rules-and-predicates).

### Run the conversation

After applying the graph above, this official Python SDK example sends a complete dialogue with its own ID:

```python
from openai import OpenAI

client = OpenAI(
    base_url="http://localhost:8080/openai/v1",
    api_key="test-key",
    default_headers={"X-Llm-Test-Server-Conversation": "refund-example"},
)
messages = []
for text in ["help", "refund", "confirm"]:
    messages.append({"role": "user", "content": text})
    reply = client.chat.completions.create(model="gpt-6.1-sol", messages=messages)
    answer = reply.choices[0].message.content
    print(answer)
    messages.append({"role": "assistant", "content": answer})
```

Expected output:

```text
Do you need a refund or delivery tracking?
Reply confirm or cancel.
Your refund is complete.
```

Try `["help", "refund", "cancel"]` or `["help", "something else", "delivery"]` with a new conversation ID.
In .NET, `control.ForConversation("refund-example")` provides an `HttpClient` that adds the conversation header;
use it as the official SDK's transport. See [client and state ownership](../Features/ConversationDSL.md#configuration-and-client-flow).

## All endpoints

| Method and path | Request | Success response | .NET control method |
| --- | --- | --- | --- |
| `POST /admin-api/configure` | Configuration object | `200`, `{"status":"configured"}` | `ConfigureAsync(...)` |
| `POST /admin-api/reset` | No body | `200`, `{"status":"reset"}` | `ResetAsync()` |
| `GET /admin-api/models` | No body | `200`, array of registered model definitions | HTTP |
| `GET /admin-api/assertions` | No body | `200`, counters and retained runtime events | `GetAssertionsAsync()` |
| `GET /admin-api/requests` | Optional query filters below | `200`, array of request traces plus retention headers | `GetRequestsAsync(...)` |
| `GET /admin-api/requests/export` | Same filters; optional `format=json` or `ndjson` | `200`, export envelope or NDJSON | `ExportRequestsAsync(...)`, `ExportRequestsNdjsonAsync(...)` |
| `POST /admin-api/verify` | Verification expectations | `200`, verification result, including failed expectations | `VerifyAsync(...)` |
| `GET /admin-api/conversations` | Optional conversation selector header | `200`, array of graph conversation states | `GetConversationsAsync()` |
| `POST /admin-api/match/explain` | Normalized chat request and provider namespace | `200`, candidates, mismatches and selected scenario/step | `ExplainMatchAsync(...)` |
| `POST /admin-api/sessions` | `{}` or `{"configuration":{...}}` | `201`, ID, access key and reserved state bytes; `Location`, `Cache-Control: no-store` | `CreateSessionAsync(...)` |
| `DELETE /admin-api/sessions/{id}` | Both session headers | `204`, no body | Session `DisposeAsync()` |

## Reset and inspect

Reset keeps configured models and fixtures, rewinds their queues, and clears the selected runtime's test state
and request journal. It does not remove isolated sessions.

```sh
curl --fail-with-body -X POST http://localhost:8080/admin-api/reset \
  -H 'Authorization: Bearer test-key'
```

After making provider calls, inspect the result:

```sh
curl --fail-with-body http://localhost:8080/admin-api/assertions \
  -H 'Authorization: Bearer test-key'

curl --fail-with-body 'http://localhost:8080/admin-api/requests?provider=openai&order=asc' \
  -H 'Authorization: Bearer test-key'
```

Assertions include `matched`, `unmatched`, `authFailed`, `modelNotFound`, `scenarioExhausted`, `errorsReturned`
and retained `events`. Request traces contain captured request/response details, HTTP status, timing and usage.

### Request filters and exports

| Query parameter | Values |
| --- | --- |
| `provider`, `model`, `requestId`, `operation`, `scenario`, `conversation`, `step`, `errorCode` | Exact string match |
| `status` | HTTP status 100–599 |
| `streaming`, `completed`, `success` | `true` or `false` |
| `from`, `to` | Inclusive request-start timestamps in ISO 8601; `from` must not follow `to` |
| `order` | `desc` (default) or `asc`, by journal sequence |
| `offset`, `limit` | Offset ≥ 0; limit 1–500 (default 500) |
| `format` (export only) | `json` (default) or `ndjson` |

The list returns `X-Total-Count` (filtered count before pagination), `X-Total-Observed`, `X-Evicted-Count`,
`X-Retained-Byte-Count` and `X-Retention-Truncated`. The export returns `schemaVersion`, `query` and `evidence`;
NDJSON starts with a metadata line followed by request lines.
The journal is bounded: default retention is 500 entries, 16 MiB total and 64 KiB payload previews.
Use retention metadata when deciding whether evidence is complete.
[Full query/export contract](../Features/RequestVerificationAndDeterminism.md#query-and-export).

## Verify calls

After the two README requests, verify that both succeeded and no other provider calls occurred:

```sh
curl --fail-with-body http://localhost:8080/admin-api/verify \
  -H 'Authorization: Bearer test-key' \
  -H 'Content-Type: application/json' \
  --data-binary '{
    "requests": [{
      "name": "delivery replies",
      "query": { "provider": "openai", "model": "gpt-6.1-sol", "success": true },
      "minimumCount": 2,
      "maximumCount": 2
    }],
    "noUnexpectedCalls": true
  }'
```

Read `passed`, `evidenceComplete`, `failures` and `matchedRequestIds` in the response.
HTTP `200` means the expectations were evaluated; the test must check `passed`.
Incomplete retained evidence cannot pass verification.

The request also supports `scope`, `inOrder`, `conversations` and `retries`.
See the [verification contract](../Features/RequestVerificationAndDeterminism.md#verification) for tool predicates,
visited steps, retry order and terminal streaming markers, or the
[request/response DTOs](../../src/ManagedCode.LlmTestServer/Verification/LlmTestServerVerification.cs).

## Conversations and match explanations

`GET /admin-api/conversations` reports graph states: scenario/provider/conversation IDs, busy/terminal state,
executed steps, last turn, next steps, visited and unvisited steps.
`POST /admin-api/match/explain` accepts a provider-neutral request:

```json
{
  "providerNamespace": "/openai",
  "request": {
    "modelId": "gpt-6.1-sol",
    "messages": [{ "role": "user", "content": "When will my order arrive?" }]
  }
}
```

It returns `errorCode`, `conversationId`, `turnIndex`, `selectedScenarioId`, `selectedStepId` and `candidates`
with predicate mismatches. It does not consume a reply or advance conversation state.
`X-Llm-Test-Server-Conversation` scopes graph status, match explanations and verification to that conversation;
for the request journal, use the `conversation` query filter instead.
See [conversation configuration and matching](../Features/ConversationDSL.md).

## Isolated sessions

Create through the default runtime with its bearer key and **without** session headers:

```sh
curl --fail-with-body http://localhost:8080/admin-api/sessions \
  -H 'Authorization: Bearer test-key' \
  -H 'Content-Type: application/json' \
  --data-binary '{}'
```

`{}` clones the startup configuration. To supply another fixture, send `{"configuration":{...}}` with a full
configuration object. Read `id` and `accessKey` from the `201` response, then send both session headers on calls
to that session. Configure/reset affect only its state. Delete it using its own ID and key headers;
deletion releases its capacity and returns `204`.
Sessions have no automatic expiry. See [session lifecycle, limits and examples](../Features/TestSessions.md).

## Errors

Control errors use `{"error":{"code":"...","message":"..."}}`.

| HTTP status | Meaning |
| --- | --- |
| `400` | Invalid JSON, configuration, query, correlation headers or verification expectations |
| `401` | Missing or incorrect bearer token when required |
| `404`, `session_not_found` | Unknown session or missing/incorrect session credentials; also deleting an absent session |
| `409`, `session_capacity_exceeded` | Session creation or reconfiguration exceeds the configured admission budget |

Provider failures such as an exhausted response queue belong to the provider request, not the configure call.
Script those through [scenarios and faults](Configuration.md), then inspect them through this API.
