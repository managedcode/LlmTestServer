# Tool outcomes and failures

A function call and its result belong to different requests. The server returns the scripted call;
the application invokes its function and sends a tool-result message on the next request. The
well-known presets construct data for that flow and never execute searches, files or processes.

```csharp
var weather = LlmTestServerWellKnownTools.Weather;
var arguments = new LlmTestServerWeatherArguments { Location = "Paris" };
var nullFixture = weather.NullResult(arguments, "call_weather");
// nullFixture.Result.Content is the JSON literal "null".
var failedFixture = weather.Failure(arguments, new()
{
    Code = "timeout",
    Message = "The application could not obtain the tool result in time.",
    Retryable = true,
}, "call_weather");
```

`CreateNullResult(callId)` creates just the correlated tool message. The explicit null factory
bypasses the successful result schema intentionally; `CreateResult` still requires a non-null,
schema-valid typed result. An empty list or empty string remains its own result. To simulate no
result, omit the tool-result message entirely. Do not replace absence with an empty message or null.
A graph can require the exact call ID and JSON root before advancing:

```csharp
config.AddConversation("weather", graph => graph
    .Step("call", step => step.CallsTools(nullFixture.Call).Then("result"))
    .Step("result", step => step.WhenToolResult(nullFixture.Call.Name, nullFixture.Call.Id)
        .WhenLatestJson("$", "null").Responds("No weather data available.").Complete()));
```

A `Failure` fixture returns the explicit `{ "error": { "code", "message", "retryable" } }`
data envelope. `IsError` labels that fixture; it does not throw, retry or set an HTTP status.
`DelayMilliseconds` is fixture metadata and must be applied explicitly to the selected scenario
or application invocation. The application can throw an exception, cancel a real invocation at
its deadline, or catch a failure and send the chosen envelope. A local exception that is never
sent back cannot appear as a server-observed tool result.

Provider request failures use the existing scenario engine. `.Fails(500, code, message)` consumes
one scripted attempt, allowing the next graph step to recover. `.DelaysBy(milliseconds)` delays
the response before runtime commit; cancellation releases that reservation so the same call can
be returned on retry. `SimulateContentFilter(term)` rejects matching request content, including
tool-result text, with a provider-shaped `content_filter` error before consuming the next step.
These failures do not warm the successful-response prompt cache. The host remains available.
Disable SDK automatic retries when testing individual observed attempts.

Native tools retain their provider-specific result contracts. Anthropic web search can return an
empty result list or a documented search error inside a successful Messages response; these do
not become application function calls or automatic provider errors.

Mid-stream stalls, socket aborts, malformed output, missing terminal frames and output-stage
content-filter events remain separate delivery-fault work in E6. A scripted HTTP 500 does not
terminate the process and does not prove a real socket abort.

The running-server regression suite is `WellKnownToolFaultIntegrationTests`: official OpenAI SDK
and Extensions.AI function loops, null/empty/missing result distinction, controlled exceptions and
cancellation, explicit recovery, provider 500, delayed response cancellation, filtered tool results,
cache accounting, retained history and health checks.
