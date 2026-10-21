# Isolated test sessions

Status: Implemented and locally qualified; public-package release pending.
Architecture: [ADR-0003](../ADR/ADR-0003-isolated-test-sessions.md).

A shared server can run independent tests concurrently. Each session owns its configuration, scenario queues,
response history, prompt cache, fault counters, video jobs, assertions and bounded request journal. A reset or
configure call changes the selected session only. Calls without session headers use the existing default runtime.

```csharp
var server = new LlmTestServerClient(httpClient, "management-key");
await using var session = await server.CreateSessionAsync(config => config
    .RequireBearerToken("provider-key")
    .UseGpt41Mini()
    .UseChatScenario("greeting", scenario => scenario.Responds("Hello from this test")));

using var chat = session.Control.CreateChatClient();
var reply = await chat.GetResponseAsync("Hello");
var assertions = await session.Control.GetAssertionsAsync();
```

The supplied `httpClient` remains caller-owned and usable after session disposal. Different session clients
share its transport without changing its default headers. Use `session.HttpClient` as the transport for official
SDKs; configure their endpoint from the server's actual allocated endpoint and supply the provider API key as
usual. The transport adds session ID/key headers to every request, including streaming and multipart operations.

## HTTP contract

| Request | Result |
| --- | --- |
| `POST /admin-api/sessions` with `{}` | Clone startup configuration; return 201, Location, Cache-Control: no-store, ID/key/reservation |
| `POST /admin-api/sessions` with `configuration` | Create an independent runtime with that configuration |
| Existing provider/control routes with both session headers | Operate on the selected runtime and journal |
| `DELETE /admin-api/sessions/{id}` with that session's headers | Release capacity, retire the runtime and return 204 |
| Missing/unknown/foreign/duplicate session ID or key | 404 `session_not_found`, without invoking or journaling the default runtime |
| Session count/state budget exhausted | 409 `session_capacity_exceeded` |
| Invalid/oversized configuration | 400; preserve the previous configuration, counters and reservation |

Headers are `X-Llm-Test-Server-Session` and `X-Llm-Test-Server-Session-Key`. They select and authorize the session;
provider bearer/API-key authentication remains independent. The session access key is returned only at creation
and is redacted from provider diagnostics. To change the provider key through configure, use
`session.Control.WithBearerToken(newKey)` for subsequent control calls. Session credentials remain unchanged.

Creation uses the default runtime's management bearer requirement and rejects session selector headers with 400; create through the root management client. Deletion uses the session's own ID/key, so
changing the default provider token does not prevent cleanup. Deleting a session invalidates in-flight delayed
work with 409 `llm_test_server_request_superseded`; subsequent calls return 404. Cancelling a request keeps the existing
queue rollback contract. Deleting an already absent session returns 404; typed disposal accepts that response.

## Bounds

Configure hosting with `services.Configure<LlmTestServerSessionOptions>(...)`. Defaults are maximum 32 sessions,
512 MiB aggregate reserved state and 1 MiB serialized configuration per session. The reservation uses configured
history/video capacities, fixed event/diagnostic retention and bounded cache/job metadata; capacity may be
exhausted before the session-count limit. It is an admission budget, not a managed-heap measurement. Configure
can shrink or grow the reservation within that budget; deletion releases it. Explicit `await using` disposal is
the lifetime boundary. There is no timer expiration or persistence.

The default browser dashboard continues to inspect the default runtime. A session selector and session/step
visualization are not covered by this contract.

## Verification

`SessionIsolationIntegrationTests`, `SessionStateIntegrationTests`, `SessionBoundaryIntegrationTests` and `SessionProviderIntegrationTests` start a
server and exercise HTTP, MEAI and official OpenAI SDK calls. They cover parallel identical prompts, credentials,
queue/reset/configure/dispose, admission limits, history/video/fault/cache isolation, journal retention, streaming
and deletion during an active delayed response. Full-suite and coverage results are recorded in the delivery
implementation ledger; this document alone is not test evidence.
