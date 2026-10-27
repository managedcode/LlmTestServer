# Anthropic native web search

The basic `web_search_20250305` contract is implemented for `claude-sonnet-5-5` on
`POST /anthropic/v1/messages`, with a [documented Anthropic API version](AnthropicApiVersions.md) and the configured
`x-api-key`. The current model and wire subset passed **76 real HTTP/SSE integration cases**
(`artifacts/delivery/anthropic-official-sdk-strict-wire-regression.log`). Official `Anthropic` 12.58.0
passed **8 real SDK integration cases with `ResponseValidation: true`**, covering JSON, typed SSE,
assistant history, all six native error variants and rejection of a programmatic caller without consuming
the next queued response (`artifacts/delivery/anthropic-official-sdk-strict-focused-batch.log`).
The isolated Release build completed with zero warnings/errors. The combined final quality gate and
installed-package consumer remain separate checks; this page does not claim the full E5 scope.

Responses are deterministic fixtures. No search, network retrieval, code execution or external service runs.

## Request contract

```json
{
  "model": "claude-sonnet-5-5",
  "max_tokens": 1024,
  "tools": [{
    "type": "web_search_20250305",
    "name": "web_search",
    "max_uses": 2,
    "allowed_domains": ["example.com/articles"],
    "user_location": {"type": "approximate", "country": "US"}
  }],
  "tool_choice": {"type": "auto"},
  "messages": [{"role": "user", "content": "Find the configured release notes."}]
}
```

Supported declaration options are nullable `max_uses`, `allowed_domains`, `blocked_domains` and
`user_location`. Both non-null domain lists are invalid, including two empty lists. Domains are scheme-less
DNS names with optional paths; subdomains, path prefixes and path wildcards participate in result matching.
Approximate location needs at least one non-empty city, region, supported ISO country or IANA timezone.
Ordinary function declarations/calls remain separate from native tools. For Claude Sonnet 5.5,
`auto` and `none` are accepted; forced `any` and named `tool` choices return 400 for both ordinary
and native tools, following the model-specific contract. `disable_parallel_tool_use` remains available.

Local fixture bounds are `max_uses` 0–128, up to 128 domain entries, result entries, citations or content
blocks per native assistant message, and non-empty IDs up to 128 characters. These are Test Server bounds,
not a claim about upstream maxima. Invalid raw shapes/options return 400. An incompatible configured
fixture, missing declaration, exhausted budget or domain mismatch returns 409 before scenario consumption.
An explicitly configured `max_uses_exceeded` result is the supported way to represent budget exhaustion.

## Ordered response and continuation

A configured response preserves the order of text, ordinary function calls, native calls and native results:

- `server_tool_use`: `id`, `name: "web_search"`, `input: {"query": "..."}`.
- `web_search_tool_result`: matching `tool_use_id` and `content`, either a results array or typed error.
- Results: `type: "web_search_result"`, `url`, `title`, `encrypted_content`, optional nullable `page_age`.
- Text citations: `type: "web_search_result_location"`, `url`, `encrypted_index`, `cited_text`, optional nullable `title`.

Native calls/results carry `caller: {"type":"direct"}`; ordinary function calls also emit their direct caller.
An omitted or null history caller normalizes to direct invocation. Programmatic/nested callers are rejected.
Opaque encrypted strings are retained unchanged. Citations reference configured result URLs; no local
cryptographic verification or decryption is performed. Successful searches can return an empty result list.
Each native call must have exactly one following matching result in the same assistant message.
Completed mixed function/native messages can be sent back as assistant history; they remain one ordered
message and retain native identity, result content and citations. Provider, session and credential scopes
continue to apply. A conversation ID groups requests and does not grant authorization.

## Errors, usage and streaming

A native tool error is still an HTTP 200 Messages response. Its result content is
`{"type":"web_search_tool_result_error","error_code":"unavailable"}`.
The supported codes are `invalid_tool_input`, `unavailable`, `max_uses_exceeded`, `too_many_requests`,
`query_too_long` and `request_too_large`. These differ from top-level HTTP/provider failures.

`usage.server_tool_use.web_search_requests` counts the configured completed call/result pairs, excluding
`max_uses_exceeded` pairs that did not execute a search. Result count does not multiply call count.
The required `web_fetch_requests` field is zero because this subset performs no web-fetch calls.
This deterministic accounting does not claim upstream billing parity and adds no code-execution counter.
Native calls remain separate from ordinary function-call analytics. Token usage is normalized fixture
accounting: opaque encrypted result content, encrypted citation indexes and result/citation metadata are
retained for inspection without being tokenized as retrieved plaintext. The server cannot recover the
underlying plaintext from these opaque values, so it does not claim upstream retrieved-content input
usage or billing parity.

With `stream: true`, standard Anthropic SSE emits `message_start`, indexed block starts/deltas/stops,
`message_delta` and `message_stop`. Server calls begin with empty input followed by `input_json_delta`;
native result content arrives whole in its block start. Text uses `text_delta`, citations use
`citations_delta`, and final usage is cumulative. JSON and the reconstructed stream preserve the same
ordered content. There is no synthetic `[DONE]` marker.

Typed native declarations, output items and history are inspectable through bounded request diagnostics,
query/export and replay. Retention limits and truncation remain explicit. Options contribute to request
identity and cache behavior; wire determinism does not replace real trace observation timestamps.

## Deferred boundaries

Pending calls, result-only continuation, `pause_turn`, programmatic/nested invocation, newer web-search
versions, web fetch, code execution and native file-search claims are outside this subset.
Declaration `allowed_callers`, `cache_control`, `defer_loading`, `strict` and other unsupported options are
rejected explicitly. Existing system/message cache controls remain available. Official SDK tests use `Anthropic.Models.Messages.Model.ClaudeSonnet5_5` directly, including SDK
streaming unions and assistant-history types, with response schema validation enabled.

Sources: [official web-search guide](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool),
[server-tool mechanics and domains](https://platform.claude.com/docs/en/agents-and-tools/tool-use/server-tools),
[streaming](https://platform.claude.com/docs/en/build-with-claude/streaming),
[versioned declaration](https://github.com/anthropics/anthropic-sdk-python/blob/main/src/anthropic/types/web_search_tool_20250305_param.py),
[result schema](https://github.com/anthropics/anthropic-sdk-python/blob/main/src/anthropic/types/web_search_result_block.py),
[citation schema](https://github.com/anthropics/anthropic-sdk-python/blob/main/src/anthropic/types/citations_web_search_result_location.py).

Current model references: [Sonnet 5.5 official model contract](https://platform.claude.com/docs/en/models/sonnet-5-5/overview),
[official SDK pinned model enum](https://github.com/anthropics/anthropic-sdk-csharp/blob/Anthropic-v12.58.0/src/Anthropic/Models/Messages/Model.cs).
