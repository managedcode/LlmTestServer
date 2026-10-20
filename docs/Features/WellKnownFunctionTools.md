# Well-known function-tool fixtures

`ManagedCode.LlmTestServer.Tools.WellKnown` provides twelve provider-neutral, typed function-tool presets.
They supply scripted calls and results to the existing scenario/conversation engine. They never evaluate an
expression, read a clock, fetch a URL, access files or execute code, commands or patches. This is the function-tool
portion of E5; provider-native built-in tools and dashboard discovery remain separate, unfinished work.

## Catalogue and contracts

`LlmTestServerWellKnownTools.All` is a read-only descriptor catalogue. Each descriptor exposes `Name`,
`Description`, `ParametersJson`, `ResultSchemaJson` and `ErrorSchemaJson`. Strongly typed properties on the same
class expose `LlmTestServerWellKnownTool<TArguments,TResult>`; names are constants in
`LlmTestServerWellKnownToolNames`. Argument/result types follow `LlmTestServer{Preset}Arguments` /
`LlmTestServer{Preset}Result`.

| Preset | Function name | Scripted arguments → result |
| --- | --- | --- |
| Calculator | `calculator` | Expression → decimal value |
| CurrentTime | `current_time` | Time zone → explicit `DateTimeOffset` and time zone |
| Weather | `weather` | Location, celsius/fahrenheit → temperature and condition |
| WebSearch | `web_search` | Query, limit → title/URL/snippet hits |
| FetchUrl | `fetch_url` | URL → status, content type and content |
| FileSearch | `file_search` | Query, limit → file ID/path/snippet hits |
| ReadFile | `read_file` | Path, offset, optional length → content and truncation flag |
| WriteFile | `write_file` | Path, content, append → bytes-written receipt |
| ListFiles | `list_files` | Path, recursive → file/directory entries |
| RunCode | `run_code` | Language, code → stdout, stderr and exit code |
| Shell | `shell` | Command, optional working directory → stdout, stderr and exit code |
| ApplyPatch | `apply_patch` | Patch text → updated-path receipt |

Search limits are 1–100. Read offsets/lengths and write/file byte counts are nonnegative. Weather units are
`celsius` or `fahrenheit`. Schemas reject unknown properties. Nullable optional fields are omitted during
serialization. `CurrentTime` requires the caller to supply its timestamp; use the same explicit timestamp as
your test's fixed fixture clock when that is the intended contract.

`CreateCall(arguments, callId)`, `CreateResult(result, callId)` and `CreateError(error, callId)` return existing
`LlmTestServerToolCall` / tool-role `LlmTestServerMessage` records. `Definition` returns the existing
`LlmTestServerToolDefinition`. Arguments and result payloads are checked against their catalogue schemas.
The default call ID is `call_` followed by the function name; supply distinct IDs for multiple simultaneous calls.
Explicit IDs must be nonblank and at most 128 characters.

## Typed fixture and tool round

`Success(...)` combines a definition, call and result into a fixture. Its delay is metadata: apply it through
`DelaysBy` when configuring the existing response or step. This example uses the typed control client already
connected to a running server:

```csharp
using ManagedCode.LlmTestServer.Tools.WellKnown;

var weather = LlmTestServerWellKnownTools.Weather.Success(
    new() { Location = "Paris", Unit = "celsius" },
    new() { Location = "Paris", Temperature = 21m, Unit = "celsius", Condition = "sunny" },
    callId: "call_paris", delayMilliseconds: 20);

await control.ConfigureAsync(config => config.UseConversation("weather", graph => graph
    .Step("call", step => step.WhenLatestUserEquals("weather")
        .CallsTools(weather.Call).DelaysBy(weather.DelayMilliseconds).Then("answer"))
    .Step("answer", step => step.WhenToolResult(weather.Call.Name, weather.Call.Id)
        .WithToolArgumentsJson(weather.Call.ArgumentsJson)
        .WhenLatestJson("$", weather.Result.Content)
        .Responds("Paris is sunny, 21 C.").Complete())));

using var conversation = control.ForConversation("weather-test");
```

Offer `weather.Definition` through the provider's function-tool wire shape. For OpenAI Chat Completions, map
`Name`, `Description` and parsed `ParametersJson` to `tools[].function`. Send the initial user message, retain the
returned assistant/tool call, and append a provider tool-result message with `tool_call_id = weather.Result.ToolCallId`
and `content = weather.Result.Content`. Use `conversation.HttpClient` for those requests or as the transport of a
real provider SDK. Normalized core records require the provider adapter's field names; they are not OpenAI wire DTOs.
The HTTP tests linked below show the complete request/response round.

Arguments/results match structurally as JSON. A changed argument or result rejects the next graph step without
consuming it. Linear queues retain their normal exhaustion behavior; graphs retain their configured
`Reject`, `RepeatLastResponse`, `Restart`, step limits and cancellation semantics. Presets introduce no additional
cursor, repeat policy or executor. Existing `VerifyAsync` tool expectations inspect names, call IDs, argument JSON
and result JSON; see [request verification](RequestVerificationAndDeterminism.md).

## Explicit result shapes

Success results retain their typed root. A calculator success is `{"value":42}`, and a weather success is
`{"location":"Paris","temperature":21,"unit":"celsius","condition":"sunny"}`. There is no implicit success envelope.

`Failure(arguments, error, callId, delayMilliseconds)` explicitly selects a tool error fixture:

```csharp
var unavailable = LlmTestServerWellKnownTools.Weather.Failure(
    new() { Location = "Paris" },
    new() { Code = "fixture_unavailable", Message = "Scripted weather failure", Retryable = true });
```

Its result content is `{"error":{"code":"fixture_unavailable","message":"Scripted weather failure","retryable":true}}`.
This is the documented `LlmTestServerToolFixtureErrorResult` envelope, described by `ErrorSchemaJson`.
It is tool-result content, not a provider HTTP error or automatic retry instruction. Configure the next graph step
and the application's handling explicitly. `IsError` identifies the selected fixture kind.

## Server evidence

The combined server suite passed **852/852** with **91.79%** production line coverage; Release build passed
with zero warnings/errors and all 19 packages packed. The preset-focused HTTP cases passed **40/40**.
Evidence: `artifacts/delivery/presets-coverage.log`. A clean installed NuGet/Aspire/official SDK/Chromium consumer passed 1/1, including the typed Calculator tool round (`artifacts/delivery/presets-consumer.log`);
these results do not establish public-feed publication or completion of the remaining E5 work.

- [WellKnownToolIntegrationTests](../../tests/ManagedCode.LlmTestServer.Tests/Tools/WellKnownToolIntegrationTests.cs): all twelve presets, success/error tool rounds, offered input schemas, typed output schemas and request verification.
- [WellKnownToolBoundaryTests](../../tests/ManagedCode.LlmTestServer.Tests/Tools/WellKnownToolBoundaryTests.cs): catalogue-driven HTTP calls, linear exhaustion, incompatible schemas without consumption, exact argument/result matching.
- [WellKnownToolLifecycleTests](../../tests/ManagedCode.LlmTestServer.Tests/Tools/WellKnownToolLifecycleTests.cs): existing repeat policy, stable call identity, optional delay and cancellation without consumption.

These tests start the server and use real HTTP. The current preset qualification uses OpenAI function-tool wire
requests; it does not establish support for provider-native search, file-search or code-execution built-ins.
