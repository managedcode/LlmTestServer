# OpenAI Responses native web-search fixtures

The first native-tool slice implements a **scripted completed search** for `gpt-4.1-mini` on
`POST /openai/v1/responses`. Its focused suite passes **99 running-server HTTP and official SDK cases** with a Release build containing
**zero warnings and zero errors**. Local qualification and the independent code-only checkpoint are recorded below and in the [delivery plan](LlmTestServerDeliveryPlan.md).

The server returns native `web_search_call` items, assistant text, URL citations and native streaming events.
Search queries, source URLs and answer text come from configuration. No URL is fetched and no search is executed.
The provider-neutral [function-tool presets](WellKnownFunctionTools.md) remain a separate fixture surface.

## Configure through the control client

```csharp
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Scenarios;

await control.ConfigureAsync(config => config.UseGpt41Mini()
    .UseConversation("research", graph => graph
        .Step("search", step => step.RespondsWithItems(
            new LlmTestServerResponseWebSearchItem
            {
                Id = "ws_fixture",
                Queries = ["Paris fixture facts"],
                Sources = [new() { Url = "https://example.com/source" }],
            },
            new LlmTestServerResponseMessageItem
            {
                Id = "msg_fixture",
                Content = "Paris fixture.",
                StreamChunks = ["Paris ", "fixture."],
                Citations = [new()
                {
                    StartIndex = 0,
                    EndIndex = 5,
                    Url = "https://example.com/source",
                    Title = "Fixture source",
                }],
            }).Complete())));
```

`RespondsWithItems(...)` also works on a queued chat scenario. Its item order is the response order;
`LlmTestServerResponseFunctionCallItem` permits mixed client function calls, native searches and messages.
A function item in a mixed native plan requires its own explicit `Id`, distinct from its tool-call correlation ID
and from every other output item ID.
Existing delays, conversation transitions, replay, reset and capacity settings apply to the same engine.
An error or legacy response fields cannot be combined with ordered output items in the same fixture.

Fixture configuration validates item identities, HTTP(S) source/citation URLs, query counts, ASCII citation ranges,
message chunk concatenation and bounded collections. A malformed HTTP configuration returns 400 and leaves the
previous configuration active. The initial citation contract accepts ASCII output text when citations are present.

## Send through the official .NET SDK

Use the installed `OpenAI` 2.14.0 Responses client with the HTTP endpoint allocated by the host:

```csharp
using OpenAI.Responses;
using System.ClientModel;
using System.ClientModel.Primitives;

using var conversation = control.ForConversation("research-example");
var sdk = new ResponsesClient(new ApiKeyCredential("test-key"), new ResponsesClientOptions
{
    Endpoint = new Uri(conversation.HttpClient.BaseAddress!, "/openai/v1/"),
    Transport = new HttpClientPipelineTransport(conversation.HttpClient),
});
var options = new CreateResponseOptions
{
    Model = "gpt-4.1-mini",
    InputItems = { ResponseItem.CreateUserMessageItem("Search the configured fixture") },
    Tools = { ResponseTool.CreateWebSearchTool(null, WebSearchToolContextSize.Low, null) },
    ToolChoice = ResponseToolChoice.CreateRequiredChoice(),
    IncludedProperties = { IncludedResponseProperty.WebSearchCallActionSources },
};
var response = (await sdk.CreateResponseAsync(options)).Value;
var search = response.OutputItems.OfType<WebSearchCallResponseItem>().Single();
```

The installed SDK's `CreateWebSearchChoice()` factory emits the distinct preview tool choice, so this
non-preview example uses `CreateRequiredChoice()`; named `web_search` selection is exercised through HTTP.

The native call parses as `WebSearchCallResponseItem` with a `WebSearchSearchAction`. Included sources parse as
`WebSearchActionUriSource`; output-text citations parse as `UriCitationMessageAnnotation`. Native calls do not
become function calls. `CreateResponseStreamingAsync(options)` emits typed native progress updates, annotation
updates and a final completed response containing the same ordered items.

## Supported request subset

| Field | Accepted contract |
| --- | --- |
| Model and route | `gpt-4.1-mini`, `/openai/v1/responses` |
| Tool declaration | One active scripted `type: web_search` contract; function tools may accompany it |
| `search_context_size` | Omitted, `low`, `medium` or `high`; explicit null is invalid |
| `user_location` | Optional/null approximate-location object; optional city, region, country and timezone fields; nullable fields follow the provider schema |
| Country/timezone | Known two-letter country code, case insensitive; a supplied timezone must identify a valid IANA timezone |
| `filters.allowed_domains` | Optional/null list, up to 100 bare DNS hostnames; empty list is unrestricted; URLs, paths, IP addresses and wildcards are invalid |
| `tool_choice` | `auto`, `none`, `required`, named web search or existing named function choice |
| `include` | `web_search_call.action.sources`; repeated entries are permitted |
| Continuation | Explicit prior native output items in an input array, or `previous_response_id` |

Without the sources include, search sources are omitted from the wire response; text citations remain present.
A configured source or citation outside a declared domain allowlist produces a 409 fixture mismatch. Missing, repeated or
disabled declarations also produce 409 before the queued response or graph step is consumed. Changing native
declaration options changes the normalized request and prompt-cache prefix identity.

Stored history retains ordered native outputs as one assistant response. Provider namespace and credential
ownership apply within the session runtime. A conversation header selects graph/correlation scope; it does not
create an additional stored-response access boundary. Reset invalidates stored response IDs; `store: false`
creates no stored parent. Native payloads contribute to existing history and conversation byte budgets.

Public request queries and JSON/NDJSON export expose typed `NativeTools`, `ResponseOutputItems` and retained
message output items. Function-only `ResponseToolCalls` continue to contain client function calls. Diagnostic
retention remains bounded and explicitly marks omitted payload evidence.

## Remaining E5 scope

This slice makes no support claim for `web_search_preview`, open/find actions, native tool failures,
non-ASCII citation-offset semantics, external-web-access/return-token-budget controls, file search,
code interpreter, resource lifecycle or native tools on other provider namespaces. Those remain in the
[native contract inventory](NativeBuiltInToolContractInventory.md) and delivery plan.

## Contract sources and integration evidence

Official references: [web-search guide](https://developers.openai.com/api/docs/guides/tools-web-search),
[Responses create contract](https://developers.openai.com/api/reference/resources/responses/methods/create),
[streaming events](https://developers.openai.com/api/reference/resources/responses/streaming-events).
The web-search guide explicitly lists `gpt-4.1-mini`; the implemented subset is narrower than the full upstream API.

Running-server coverage:

- [JSON, SDK, mixed output and continuation](../../tests/ManagedCode.LlmTestServer.Tests/OpenAI/NativeWebSearchJsonIntegrationTests.cs)
- [SDK and raw SSE lifecycle](../../tests/ManagedCode.LlmTestServer.Tests/OpenAI/NativeWebSearchStreamIntegrationTests.cs)
- [Request options, schema boundaries and namespace guards](../../tests/ManagedCode.LlmTestServer.Tests/OpenAI/NativeWebSearchValidationIntegrationTests.cs)
- [Atomic configuration and native payload budgets](../../tests/ManagedCode.LlmTestServer.Tests/OpenAI/NativeWebSearchConfigurationIntegrationTests.cs)
- [Cancellation, reset, replay, ownership and domain mismatch](../../tests/ManagedCode.LlmTestServer.Tests/OpenAI/NativeWebSearchLifecycleIntegrationTests.cs)
- [Native declaration options and real prompt-cache usage](../../tests/ManagedCode.LlmTestServer.Tests/OpenAI/NativeWebSearchCacheIntegrationTests.cs)

## Local qualification

- Native HTTP/official SDK suite: 99/99.
- Existing provider behavior/contract namespace: 85/85.
- Full current working-tree integration snapshot: 966/966, production line coverage 91.94%.
- Canonical format verification and final Release build: passed, 0 warnings/errors.
- All 19 packages packed; independent clean NuGet/Aspire/SDK/Chromium consumer: 2/2, including the separate native JSON/SSE and mixed-function round.
- .NET SDK `PublishContainer` image: actual native JSON/source gating/citations/SSE/terminal evidence smoke passed on linux/arm64.

Evidence logs are repository-owned under `artifacts/delivery/native-web-*`; container evidence is in
`artifacts/container-verification/2cd0409b3d183025`. The full working-tree count includes parallel owner analytics
work; a code-only checkpoint and its exact-head CI are tracked separately in the delivery plan. No NuGet/GHCR
release or main merge is asserted by these local results.

The independent code-only checkout passed 965/965 server integrations after the ordinary-response regression fix, with 91.96% production line coverage, 77.38% branch coverage and 94.38% method coverage. Canonical formatting and Release build passed with 0 warnings/errors. These results exclude the parallel owner analytics/visual work. Evidence: `artifacts/delivery/native-isolated-final-{format,build,coverage}.log`.

The same code-only checkout packed all 19 packages; the fresh installed NuGet/Aspire/official SDK/Chromium consumer passed 2/2. Its .NET SDK-built linux/arm64 image passed actual HTTP native JSON/SSE and terminal-evidence acceptance. Evidence: `artifacts/delivery/native-isolated-{pack,consumer,container-publish,container-smoke}.log`; container evidence `artifacts/native-code-validation/artifacts/container-verification/220fcd85c2a76ed5`. No public package/image publication is claimed.
