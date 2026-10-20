# Native decision contracts

Official native decision/vision contracts re-audited 2026-10-20; see [vision audit](DecisionVisionAudit.md). Provider DTOs live in their owning SystemOne, Cloudflare and OpenAI packages; deterministic fixtures live in core and routes in Hosting.

Jev, Kev, Cloudflare Clef/Clef Flash and OpenAI Decisions use explicit decision fixtures. The TCK hosts their native JSON shapes under provider namespaces and does not contact production providers. Declare the provider profile explicitly; model names never select an API implicitly.

| Provider | TCK POST route | Production POST endpoint | Model helper | Official contract |
| --- | --- | --- | --- | --- |
| TypeSafe Jev | `/systemone/v1/systemone` | `https://api.typesafe.ai/v1/systemone` | `AddJevLatest()` | [OpenAPI](https://api.typesafe.ai/openapi.json), [primitives](https://docs.typesafe.ai/primitives/choice) |
| Kev | `/systemone/v1/systemone` | `{kev-server}/v1/systemone` | `AddKevLatest()` | [native request models](https://github.com/jaredpalmer/kev/blob/main/kev/api.py), [server/auth](https://github.com/jaredpalmer/kev/blob/main/kev/serve.py) |
| Cloudflare Clef | `/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef` | `https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef` | `AddClef()` | [Clef API](https://developers.cloudflare.com/workers-ai/models/clef/), [REST envelope/auth](https://developers.cloudflare.com/workers-ai/get-started/rest-api/) |
| Cloudflare Clef Flash | `/cloudflare/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef-flash` | `https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef-flash` | `AddClefFlash()` | [Clef Flash API](https://developers.cloudflare.com/workers-ai/models/clef-flash/) |
| OpenAI Decisions | `/openai/v1/decisions` | `https://api.openai.com/v1/decisions` | `AddOpenAiDecisionModel("gpt-6-luna")` | [create API](https://developers.openai.com/api/reference/resources/decisions/methods/create/) |

Production APIs use `Authorization: Bearer <provider-token>`; Kev checks `KEV_API_KEY` only when configured and otherwise permits unauthenticated calls. For the TCK, `RequireBearerToken` controls the expected test token and protects both provider routes and `/admin-api/*`. Tests do not need production credentials. Resolve the hosted base URL from the Aspire resource (`GetEndpoint("http")` / `CreateHttpClient(...)`), then append the TCK route.

### SystemOne: Jev and Kev

Jev 1.14's JSON image assets and `box` questions are covered by the
[Jev 1.14 compatibility profile](Jev114.md). Register `jev-1.14.0` or `jev-preview`
with `AddDecisionModel(..., LlmTckDecisionProvider.TypeSafe)` and opt each image/box
request in through `TypeSafe-Danger-Zone-Images: true`. Structured score legends
and the optional fixture `ResolvedModelId` retain native values and version attribution.
Long-context headers pass through; deterministic scenarios define usage and scripted
context-limit failures. The emulator does not tokenize images or estimate model context.

Jev requires `model`, string/object/array `state`, and a nonempty question map. Kev accepts any JSON `state`, including numbers, booleans and null; instructions and criterion descriptions also accept arbitrary JSON. Kev defaults an omitted `model` to `kev-latest`; the TCK enables that default only when `AddKevLatest()` explicitly registers the Kev profile. Binary questions use `type: "noul"`; optional `criteria` maps the native `true` and `false` descriptions. Choices use an opaque ID-to-description map. Scores use an ordered criteria array.

```json
{
  "model": "kev-latest",
  "state": { "message": "Please help quickly" },
  "questions": {
    "urgent": { "type": "noul", "instructions": "Is urgent?", "criteria": { "true": "needs prompt help", "false": "can wait" } },
    "route": { "type": "choice", "instructions": "Choose the queue", "criteria": { "fast": "urgent", "regular": "routine" } },
    "severity": { "type": "score", "instructions": "Assess severity", "criteria": ["low", "medium", "high"] }
  }
}
```

Use `model: "jev-latest"` for Jev. Native responses contain keyed `answers`: `noul`, or `choice`/`score` with `confidence` and `probabilities`; score `legend` keys are zero-based numeric strings. `usage` contains `input_tokens` and `output_tokens`. Scores are native ordinal values and can exceed one.

```csharp
using ManagedCode.LlmTck.Decisions;

options.AddKevLatest().AddDecisionScenario(new LlmTckDecisionScenario
{
    Id = "priority", ModelId = "kev-latest",
    Answers = new()
    {
        ["urgent"] = new() { Kind = LlmTckDecisionKind.Predicate, Probability = 0.95 },
        ["route"] = new() { Kind = LlmTckDecisionKind.Choice, Choice = "fast", Confidence = 0.9,
            Probabilities = new() { ["fast"] = 0.95, ["regular"] = 0.05 } },
        ["severity"] = new() { Kind = LlmTckDecisionKind.Score, Score = 1.75, Confidence = 0.625,
            Probabilities = new() { ["0"] = 0.05, ["1"] = 0.15, ["2"] = 0.8 } },
    },
    Usage = new() { InputTokens = 100, OutputTokens = 10, TotalTokens = 110 },
});
```

Kev score legends contain rendered strings, while Jev retains string/object/array criteria. Kev responses include `latency_ms`; `LlmTckDecisionScenario.Metadata` supplies deterministic latency and optional `Truncated`, `StateTokens`, `StateTokensUsed` fields. The route echoes or generates `x-typesafe-request-id`, including on errors. Invalid SystemOne request shapes return HTTP 422 with a native `detail` validation array; runtime/admission faults retain `detail` strings. Kev bearer failures include `WWW-Authenticate: Bearer`. This compatibility surface covers `/v1/systemone`; Kev demo `/permute` and `/separate` routes are outside its scope.

### Cloudflare Workers AI: Clef and Clef Flash

Send the same SystemOne question shape to the account/model route with body `model: "clef"` or `"clef-flash"` matching the route. Every question requires instructions. Binary `criteria.true` and `criteria.false` accept arbitrary JSON, including numbers, booleans and null; `criteria` itself must remain an object. State and instructions retain their documented string/object/array types, choice descriptions accept string/object/array/null, and score descriptions accept string/object/array. Responses wrap the native answer object in the Workers AI envelope:

```json
{
  "result": {
    "model": "clef",
    "answers": { "urgent": { "type": "noul", "noul": 0.95 } },
    "usage": { "input_tokens": 100, "output_tokens": 0 }
  },
  "success": true,
  "errors": [],
  "messages": []
}
```

Configure `AddClef()` / `AddClefFlash()` and fixtures with the matching short model ID; native surrounding model whitespace is accepted. Runtime/auth failures remain non-success HTTP responses and use `success: false` with native numeric `errors[].code`. Explicit numeric scenario error codes are preserved, including 5004/400 (invalid input), 3007 or 3008/408 (timeout), 3036 or 3040/429 (capacity/rate limit). See [native errors](https://developers.cloudflare.com/workers-ai/platform/errors/). For native error cases, configure the explicit numeric `LlmTckScenarioError.Code`. Unmapped TCK-only errors such as fixture mismatch use the HTTP status as a synthetic code; that fallback is not a claimed Cloudflare native error code.

Optional `images` is an array of inline data URLs or `{ "content_type": "image/png", "base64": "..." }` objects. PNG/JPEG/WebP are supported; remote URLs are rejected. Limits: four images, 4 MiB decoded file bytes and 16 megapixels per image, 8 MiB combined decoded bytes, and 13 MiB for the complete request body. Image dimensions and file type are read as metadata; compressed pixel streams are not decoded, so full raster validity is outside this deterministic mock validation. The TCK retains supplied images in the matched input and trace. See [input schema](https://developers.cloudflare.com/workers-ai/models/clef/schema-input.json), [output schema](https://developers.cloudflare.com/workers-ai/models/clef/schema-output.json).

The typed client is available through
`new LlmTckClient(httpClient, bearerToken).CreateCloudflareDecisionClient(accountId)`.
Pass `CloudflareDecisionRequest` with either `CloudflareDecisionImage.DataUrl` or
`Embedded = new CloudflareBase64Image { ContentType = ..., Base64 = ... }`.
`EvaluateAsync` selects the native route from the explicit `clef`/`clef-flash` model,
returns the complete Cloudflare envelope (including native non-2xx errors), and
leaves the shared HTTP client owned by its caller. It does not download remote URLs.

### OpenAI Decisions

OpenAI uses `input` and an ordered question array. Predicate questions return one probability; boolean choices can carry separate true/false descriptions. Choice values are typed strings or booleans. Score `levels` use ordered string labels and optional descriptions; responses retain the zero-based level index and original label.

```json
{
  "model": "gpt-6-luna",
  "input": "Please help quickly",
  "questions": [
    { "type": "predicate", "name": "urgent", "instructions": "Is urgent?" },
    { "type": "choice", "name": "route", "instructions": "Choose the queue", "choices": [
      { "value": "fast", "description": "urgent" }, { "value": false, "description": "defer" }
    ] },
    { "type": "score", "name": "severity", "instructions": "Assess severity", "levels": [
      { "label": "low", "description": "low" }, { "label": "medium", "description": "medium" }, { "label": "high", "description": "high" }
    ] }
  ]
}
```

OpenAI fixture question IDs use `OpenAiDecisionFixtureIds.ForQuestionIndex(index)` for the zero-based position, regardless of optional native names. Choice fixture IDs use `ForChoiceValue(string/bool)` canonical JSON literals so string `"true"` differs from boolean `true`. Responses preserve names (including `null`), duplicate names and answer order.

```csharp
using ManagedCode.LlmTck.Decisions;
using ManagedCode.LlmTck.OpenAI;

var fast = OpenAiDecisionFixtureIds.ForChoiceValue("fast");
var defer = OpenAiDecisionFixtureIds.ForChoiceValue(false);
options.AddOpenAiDecisionModel("gpt-6-luna").AddDecisionScenario(new LlmTckDecisionScenario
{
    Id = "priority-openai", ModelId = "gpt-6-luna",
    Answers = new()
    {
        [OpenAiDecisionFixtureIds.ForQuestionIndex(0)] = new() { Probability = 0.95 },
        [OpenAiDecisionFixtureIds.ForQuestionIndex(1)] = new() { Kind = LlmTckDecisionKind.Choice, Choice = fast,
            Confidence = 0.9, Probabilities = new() { [fast] = 0.9, [defer] = 0.1 } },
        [OpenAiDecisionFixtureIds.ForQuestionIndex(2)] = new() { Kind = LlmTckDecisionKind.Score, Score = 1.75,
            Confidence = 0.8, Probabilities = new() { ["0"] = 0.05, ["1"] = 0.15, ["2"] = 0.8 } },
    },
    Usage = new() { InputTokens = 100, CachedInputTokens = 4, OutputTokens = 0, TotalTokens = 100 },
});
```

`input` can also be an array of user messages: string content or `input_text` / `input_image` parts. Images use embedded base64 data URLs; external URLs, file IDs, audio, tools and non-user roles are rejected. Usage retains input/output/total tokens, cached/cache-write input details and reasoning output details, including zero defaults. Set `Refused = true` on an answer fixture to emit a native `{"type":"refusal","name":...}` answer in a successful HTTP response.

### Limits, fixture matching and failures

| Profile | Choice criteria | Score criteria | Other native constraints |
| --- | --- | --- | --- |
| TypeSafe Jev | Up to 255 | 1..10 | Optional instructions and binary criteria; state is string/object/array, including JSON descriptions and nullable optional instructions. |
| Kev | 1..255 | 1..255 | Any JSON state/instructions/descriptions, empty opaque IDs; probabilities/native scores rounded to four decimals. |
| Cloudflare Clef/Flash | 2..255 | 2..10 | 1..64 questions; ASCII letter/digit/underscore/dot/hyphen IDs, max 100 characters; required nonempty instructions; four-decimal probabilities/native scores. |
| OpenAI | 2..255 distinct typed values | Ordered labels; no undocumented count cap | Optional names/descriptions, empty strings and repeated score labels preserved; max 128 embedded images; model/names/instructions/labels/descriptions max 1,048,576 characters; no invented choice-value length cap. |

`AddDecisionModel(id, LlmTckDecisionProvider...)` supports explicit custom model IDs; the control client mirrors it with `UseDecisionModel` and `UseDecisionScenario`. Fixtures are repeatable snapshots. Optional `ExpectedInput` matches the provider input exactly; SystemOne JSON state and OpenAI message arrays use serialized JSON. Cloudflare requests with supplied images match serialized `{state,images}`; text-only requests retain the state-only input. Question IDs, kinds and requested value domains must fit the fixture. Unknown models, unmatched inputs, mismatched fixtures, authentication, configured rate limits/content filtering and scripted errors produce observable non-success HTTP responses. Normal native choice/score fixtures require confidence and a complete probability domain, near-unit probability mass. SystemOne and Cloudflare choices must have maximal probability; OpenAI preserves any selected value in the supplied domain. Cloudflare confidence must equal the maximum probability within native rounding tolerance. Configure profile-correct explicit values: the Kev/Jev example above has score confidence 0.625; Clef uses the maximum probability 0.8. Values are validated and preserved without normalization or recomputation. Four-decimal distributions may total 0.9999 and native scores are not recomputed from rounded probabilities. For deliberate malformed-response regressions, set `AllowMalformedResponse = true` on the scenario; this explicitly bypasses native response conformance while keeping question-kind/domain mapping safe.

See [complete native contracts and fixture details](../../README.md#native-decision-api-fixtures) and [behavior-test evidence](../Testing/AcceptanceCriteriaTestInventory.md). Provider-specific limits and optional-field rules above are reconciled against the linked primary contracts.
