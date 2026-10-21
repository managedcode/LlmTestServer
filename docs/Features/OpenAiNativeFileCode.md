# OpenAI Responses native file-search and code fixtures

Status: implemented bounded contracts. **191 running-server HTTP/official SDK cases passed**, including exact numeric-filter and normalization regressions; the web-search regression suite passed **105/105**. Release build has zero warnings/errors. Full-suite/package/container qualification is recorded below; this is not a full E5 completion or publication claim.

These fixtures serve `POST /openai/v1/responses`. They return configured native output items, citations and SSE events without retrieving files, executing code, fetching image URLs or starting containers. File/container/store IDs are explicit fixture references. They do not imply working resource upload, download, creation or deletion APIs.

## Independent model contracts

| Native declaration | Current fixture model | Primary-source pairing |
| --- | --- | --- |
| `web_search` | `gpt-4.1-mini` | [Existing web contract](OpenAiNativeWebSearch.md) |
| `file_search` | `gpt-4o-mini` | [Official file-search cookbook](https://developers.openai.com/cookbook/examples/file_search_responses) |
| `code_interpreter` | `gpt-4.1` | [Official tool cookbook](https://developers.openai.com/cookbook/examples/mcp/mcp_tool_guide) |

The pairings are separate. A current request cannot declare multiple native families whose supported model sets have no common member. Raw model checks apply to current declarations and named choices; completed prior tool outputs and include selectors alone do not re-qualify a past tool against the current model. The core independently checks the qualified native surface and selected fixture. Azure OpenAI Responses also accepts these bounded native declarations at `/azure-openai/openai/v1/responses`; other provider namespaces and Chat Completions reject them. Azure code results retain the exact fixture container identity and do not invent session counts or durations.

## Configure a file-search fixture

The following uses a control client connected to the running server:

```csharp
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;

await control.ConfigureAsync(config => config.UseGpt4OMini()
    .UseChatScenario("file-search", scenario => scenario
        .ForModel(LlmTestServerKnownModelIds.Gpt4OMini)
        .RespondsWithItems(
            new LlmTestServerResponseFileSearchItem
            {
                Id = "fs_fixture",
                VectorStoreIds = ["vs_fixture"],
                Queries = ["Paris fixture facts"],
                Results = [new()
                {
                    FileId = "file_source", Filename = "source.txt",
                    Text = "Scripted Paris source", Score = 0.9,
                    Attributes = new()
                    {
                        ["category"] = JsonSerializer.SerializeToElement("fixture"),
                        ["year"] = JsonSerializer.SerializeToElement(2026),
                    },
                }],
            },
            new LlmTestServerResponseMessageItem
            {
                Id = "msg_file", Content = "Paris fixture.",
                Citations = [new LlmTestServerFileCitation
                { FileId = "file_source", Filename = "source.txt", Index = 5 }],
            })));
```

Every fixture store must appear in the request's `vector_store_ids`. The request may declare additional stores. Configured resource-reference lists are distinct. Duplicate declaration IDs are accepted and preserved; matching treats resource references as sets. An empty declaration array is syntactically valid but cannot satisfy a fixture requiring stores. Missing/foreign stores produce a 409 fixture mismatch without consuming the queued response or graph step.

`max_num_results` is an integer from 1 to 50. The configured result count must already fit that limit; the engine does not silently cut the fixture down. A supplied `score_threshold` must be finite and between zero and one; every configured result then needs a score meeting it. Each result must also satisfy the requested attribute filter. `ranker` accepts `auto` and `default-2024-11-15`, and is retained in normalized declarations and request identity; it does not run a ranking algorithm. These request/result fields follow the [Responses schema](https://developers.openai.com/api/reference/resources/responses/methods/create) and [file-search guide](https://developers.openai.com/api/docs/guides/tools-file-search).

## Configure a code-interpreter fixture

```csharp
await control.ConfigureAsync(config => config.UseGpt41()
    .UseChatScenario("code", scenario => scenario
        .ForModel(LlmTestServerKnownModelIds.Gpt41)
        .RespondsWithItems(
            new LlmTestServerResponseCodeInterpreterItem
            {
                Id = "ci_fixture", ContainerId = "cntr_fixture",
                InputFileIds = ["file_input"],
                GeneratedFileIds = ["file_plot"],
                Code = "print(42)\n", CodeChunks = ["print(", "42)\n"],
                Outputs = [
                    new LlmTestServerCodeInterpreterLogsOutput { Logs = "42\n" },
                    new LlmTestServerCodeInterpreterImageOutput
                    { Url = "https://example.com/fixture.png" },
                ],
            },
            new LlmTestServerResponseMessageItem
            {
                Id = "msg_code", Content = "42 fixture.",
                Citations = [new LlmTestServerContainerFileCitation
                {
                    ContainerId = "cntr_fixture", FileId = "file_plot",
                    Filename = "plot.png", StartIndex = 0, EndIndex = 0,
                }],
            })));
```

An explicit request container must equal the configured `ContainerId`. In automatic mode, the distinct requested `file_ids` set must equal the fixture's `InputFileIds`; declaration order/duplicates remain preserved in normalized request data. Auto mode returns the configured container identity. It does not provision a container.

Container-file citations must reference that exact container and one of its configured `InputFileIds` or `GeneratedFileIds`. Generated references are explicit ownership metadata, not a side effect of evaluating `Code`. A mismatch returns 409 before fixture consumption. Nullable `Code` and nullable/empty `Outputs` remain distinct scripted states; configured chunks must concatenate exactly to non-null code. Only logs and explicit HTTP(S) image URLs are implemented. The [code-interpreter guide](https://developers.openai.com/api/docs/guides/tools-code-interpreter) documents the native container/code/output and generated-file citation contracts; lifecycle/download behavior remains outside this slice.

## Official .NET SDK options

Use `OpenAI` 2.14.0 `ResponsesClient` with its endpoint resolved from the running server's `/openai/v1/` surface and its real HTTP transport. For the file fixture above:

```csharp
using OpenAI.Responses;

var fileOptions = new CreateResponseOptions
{
    Model = "gpt-4o-mini",
    InputItems = { ResponseItem.CreateUserMessageItem("Use the file fixture") },
    Tools = { ResponseTool.CreateFileSearchTool(
        ["vs_fixture"], 2,
        new FileSearchToolRankingOptions
        { Ranker = FileSearchToolRanker.Auto, ScoreThreshold = 0.5f },
        BinaryData.FromString("""{"type":"eq","key":"category","value":"fixture"}""")) },
    ToolChoice = ResponseToolChoice.CreateFileSearchChoice(),
    IncludedProperties = { IncludedResponseProperty.FileSearchCallResults },
};
var fileResponse = (await sdk.CreateResponseAsync(fileOptions)).Value;
var search = fileResponse.OutputItems.OfType<FileSearchCallResponseItem>().Single();
```

For the independently configured code fixture:

```csharp
var container = new CodeInterpreterToolContainer(
    CodeInterpreterToolContainerConfiguration.CreateAutomaticContainerConfiguration(["file_input"]));
// An explicit container uses: new CodeInterpreterToolContainer("cntr_fixture").
var codeOptions = new CreateResponseOptions
{
    Model = "gpt-4.1",
    InputItems = { ResponseItem.CreateUserMessageItem("Use the code fixture") },
    Tools = { ResponseTool.CreateCodeInterpreterTool(container) },
    ToolChoice = ResponseToolChoice.CreateRequiredChoice(),
    IncludedProperties = { IncludedResponseProperty.CodeInterpreterCallOutputs },
};
var codeResponse = (await sdk.CreateResponseAsync(codeOptions)).Value;
var execution = codeResponse.OutputItems.OfType<CodeInterpreterCallResponseItem>().Single();
```

Raw HTTP additionally supports named `{"type":"code_interpreter"}` choice. The installed SDK's typed surface has no corresponding named-code factory; use its required choice for this example. Existing `auto`, `none`, `required`, named native and named function choices are checked against the actual ordered fixture. Native items do not become client function calls.

## Filters and bounded input

The supported filter operators are `eq`, `ne`, `gt`, `gte`, `lt`, `lte`, `in`, `nin`, `and`, `or`. This is an explicit bounded fixture contract:

- `eq`/`ne` compare matching scalar kinds: strings, finite JSON numbers or booleans.
- Ordering compares strings ordinally or numbers numerically. Boolean ordering is rejected with 400.
- `in`/`nin` require a nonempty homogeneous string or number array. Boolean/mixed/null arrays are rejected.
- Missing attributes and mismatched kinds do not match, including `ne` and `nin`.
- Compound filters evaluate all/any child matches. Root depth is one; maximum depth is eight and the entire tree has at most 128 nodes. Comparison/member arrays and compound child arrays have at most 128 entries.
- Attribute keys are nonblank and at most 64 characters. Results have at most 16 scalar attributes; attribute string values have at most 512 characters.

Numeric comparisons use normalized decimal digits/exponents rather than rounding both operands to a binary floating-point value. JSON values must still satisfy finite-number admission. The HTTP suite verifies distinct tiny and large numbers, numeric ordering, membership, signed zero and equivalent number representations. A pre-fix HTTP regression demonstrated a false match for `1e-29` versus `2e-29`; the exact comparator fixes it without expanding exponent powers.

Native item/resource IDs are bounded to 128 characters. File queries, results, store and input/generated file references and code outputs/chunks have at most 128 entries; file queries are nonempty. A file-search fixture owns at least one store; request store arrays may be empty. Native assistant groups contain at most 128 ordered items, with distinct effective nonempty output IDs. Citation lists are also bounded to 128. These collection/identity limits belong to our fixture contract, rather than being attributed to arbitrary upstream limits.

`filters` may be omitted/null; an actual filter object must match its comparison/compound shape. `ranking_options` may be omitted or `{}` but not null. Optional ranker/threshold/max-result fields reject explicit null. Code `container` is required: a nonempty ID or `{ "type":"auto", "file_ids":[...] }`; absent `file_ids` and `[]` are valid, while null is not. Unknown tool/filter/result fields are rejected. Hybrid ranking, auto-container memory/network controls and additional native tool options remain pending and return 400 instead of being accepted and ignored.

## Ordered control data and provider output

Control configuration uses `kind` for output items and annotations; the wire API uses provider `type` values:

| Control `kind` | API representation |
| --- | --- |
| `file_search` | `file_search_call` with ID/status/queries and optional `results` |
| `code_interpreter` | `code_interpreter_call` with container ID, nullable code and nullable outputs |
| Message citation `file` | `file_citation`: file_id, filename, insertion index |
| Message citation `container_file` | `container_file_citation`: container/file IDs, filename, start/end |
| Code output `logs` / `image` | `type: logs` with logs / `type: image` with url |

One ordered citation union retains URL, file and container-file annotation order. A file citation references a configured file-search result and its filename when that result supplies one. Citation text is ASCII in this initial subset. File insertion indexes are between zero and text length. Container ranges allow `start == end`, matching documented zero-range examples; URL ranges retain the existing positive-length boundary. Uncited text retains Unicode support.

Results are included by `file_search_call.results`; without it, the optional field is omitted. Code outputs use `code_interpreter_call.outputs`; excluded or unavailable outputs are null, while an included scripted empty output list remains `[]`. Include projection does not remove citations or mutate stored fixture data. File result fields retain their optionality, including absent metadata; the field is `results`, with no alternative spelling.

SSE retains output-item added/done and final response completion. File calls emit in_progress/searching/completed events. Code calls emit in_progress, configured code deltas and code.done when code is non-null, interpreting and completed. Logs/images are completed output data, with no invented logs/image delta events. The existing annotation-added event carries each ordered citation. The final JSON output agrees with nonstreaming output; terminal evidence is recorded only after final completion is written/flushed. See the [official streaming schemas](https://developers.openai.com/api/reference/resources/responses/streaming-events).

## History, evidence and remaining qualification

Explicit prior native output items and `previous_response_id` use the existing history/replay/capacity engine. Past native calls retain their kinds; they are not synthetic user JSON. Provider namespace and credential ownership apply inside the session runtime. A conversation header labels graph/correlation scope and does not create another history authorization boundary. Reset/configure invalidates existing retained history through generation ownership.

Request queries and JSON/NDJSON exports retain typed declarations, results, citations and assistant output groups within the existing diagnostic budgets. Nested data is copied; omitted evidence is marked truncated. Function `ResponseToolCalls` stays separate. Native declaration/filter/resource options remain preserved in fingerprints, provider-scoped prompt-cache identity and normalized usage. Cache reads/writes and reasoning remain subsets of input/output totals.

The local qualification below covers the focused suites, full server/coverage gate, clean installed-package Aspire/SDK consumer and actual container smoke. Public delivery remains pending. Native failed/incomplete results, unqualified model pairings, other providers and resource lifecycle remain in [the contract inventory](NativeBuiltInToolContractInventory.md). Related design: [ADR-0006](../ADR/ADR-0006-ordered-native-tool-output.md).

## Code-only qualification

- Focused file/code HTTP/official SDK: 191/191, including 24 exact-number and 17 normalization cases.
- Existing web-search regression: 105/105.
- Full isolated server suite: 1158/1158; production line coverage 92.21%, branch coverage 78.21%, method coverage 94.55%.
- Canonical format verification and final Release build passed with 0 warnings/errors.
- All 19 packages packed; a fresh installed NuGet/Aspire/official SDK/Chromium consumer passed 3/3, including independent file/code JSON/SSE and stored continuation. A .NET SDK `PublishContainer` linux/arm64 image passed actual web/file/code JSON/SSE, includes, citations, code deltas, management/auth/reset and terminal-evidence smoke.

Evidence: `artifacts/delivery/native-file-code-*`; container record: `artifacts/native-code-validation/artifacts/container-verification/bb0d14644caef9d4`. This checkout excludes the concurrent owner analytics/visual changes. Public package/image publication, owner merge and remaining E5–E13 work are not asserted by these local results.
