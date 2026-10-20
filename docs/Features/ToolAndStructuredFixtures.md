# Deterministic tool and structured-output fixtures

The runtime stores explicit tool calls and JSON responses. It does not invent tool arguments or generate JSON from a schema. The selected fixture must satisfy the request; otherwise the operation returns `409 / llm_tck_fixture_mismatch` without consuming the response. Malformed wire options return a provider-shaped 400 before runtime execution.

```csharp
builder.AddChatScenario("weather", scenario => scenario
    .CallsTool("weather", """{"city":"Paris"}""", "weather_1")
    .RespondsJson("""{"forecast":"sunny"}"""));
```

`CallsTools(...)` configures multiple calls in one response. Each call carries its ID, function name and JSON arguments. Tool declarations and arguments flow through provider adapters into typed provider-neutral records. Assistant tool calls and tool results remain in conversation history, exact scenario matching, diagnostic retention, token accounting and prompt-cache identity. `LlmTestServerChatClient` maps `AIFunctionDeclaration`, tool modes, `FunctionCallContent`, `FunctionResultContent`, and `ChatResponseFormatJson`; it works with the standard `UseFunctionInvocation()` middleware.

## Supported contracts

| Provider operation | Tool declaration / result | Structured output | Official reference |
| --- | --- | --- | --- |
| OpenAI Chat; Azure deployment and v1 Chat; Foundry Chat; Groq, Mistral, OpenRouter, DeepSeek Chat | `tools[].function`, `tool_calls`, `tool_call_id`; auto/none/required/named choice | `response_format`: json_object or json_schema | [OpenAI Chat](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create/), [Azure](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/structured-outputs) |
| OpenAI, Azure v1, Foundry v1, Groq and OpenRouter Responses | flat function tools; function_call and function_call_output items | `text.format` | [OpenAI Responses](https://developers.openai.com/api/reference/resources/responses/methods/create/), [OpenRouter](https://openrouter.ai/docs/api/reference/responses/overview) |
| Anthropic Messages | tools/input_schema; tool_use and tool_result blocks; auto/none/any/tool choice | `output_config.format.schema` | [Tool use](https://platform.claude.com/docs/en/agents-and-tools/tool-use/implement-tool-use), [Structured outputs](https://platform.claude.com/docs/en/build-with-claude/structured-outputs) |
| Gemini generateContent / streamGenerateContent | functionDeclarations, functionCall/functionResponse; functionCallingConfig | responseJsonSchema or responseSchema, application/json | [Functions](https://ai.google.dev/gemini-api/docs/function-calling), [Structured output](https://ai.google.dev/gemini-api/docs/structured-output) |
| Ollama chat | tools[].function; object-valued function arguments; tool_name results | format: json or schema object | [Chat](https://docs.ollama.com/api/chat) |
| Cohere v2 chat | tools[].function, tool_calls/tool_call_id; REQUIRED/NONE choice | response_format: json_object, optional schema | [Chat](https://docs.cohere.com/v2/reference/chat), [Tool streaming](https://docs.cohere.com/v2/docs/tool-use-streaming) |
| Bedrock Converse / ConverseStream | toolConfig, toolUse/toolResult blocks | outputConfig.textFormat.structure.jsonSchema.schema | [Converse](https://docs.aws.amazon.com/bedrock/latest/APIReference/API_runtime_Converse.html), [Schema](https://docs.aws.amazon.com/bedrock/latest/APIReference/API_runtime_JsonSchemaDefinition.html) |
| Perplexity Sonar | Function tools are not claimed and are rejected | response_format: json_schema | [Structured output](https://docs.perplexity.ai/docs/sonar/features) |

Legacy Azure deployment routes accept the SDK-selected `api-version` for the documented `2024-10-21` GA and `2025-04-01-preview` contracts and reject unknown versions; Foundry inference routes require `api-version=2024-05-01-preview`; OpenAI v1 routes do not require a dated version. The operation contracts retain these constraints. Provider/model availability restrictions in the upstream services still apply; the Test Server proves the listed fixture wire contracts, not every provider-hosted tool or model feature. Bedrock InvokeModel retains its existing model-specific fixture contract and rejects tool/schema options; tool support is claimed on Converse only. Legacy `functions` / `function_call` request options and server-hosted tools are explicitly rejected.

## Schema and streaming behavior

JSON Schema validation uses an isolated Draft 2020-12 registry, including local `$ref` references. External references are rejected so tests cannot depend on a remote schema fetch. Gemini's uppercase OpenAPI schema type names are normalized at its adapter boundary. Tool arguments must satisfy the selected function's parameter schema; required/none/named choices and unique tool IDs are enforced. JSON stream chunks must concatenate to the validated fixture content.

Streams carry provider-native tool events: OpenAI indexed tool-call deltas and terminal tool_calls; Responses function argument deltas; Anthropic input_json_delta; Gemini functionCall parts; Ollama tool_calls; Cohere tool-call events; Bedrock binary event-stream toolUse events. Multiple calls retain distinct indices, and text plus tool blocks remain available in the same response.

## Verification

- `ToolFixtureEndpointTests`: positive full conversation and schema mismatch recovery for each claimed tool operation; native streaming events; mapped tool-result history.
- `ToolFixtureClientTests`: OpenAI Chat and Responses SDK parsing, Azure SDK, Microsoft.Extensions.AI automatic function invocation in ordinary and streaming modes.
- `ToolFixtureValidationTests`: malformed wire options, tool selection mismatch, argument validation, local/external schema references and inconsistent JSON chunks.
- HTTP/SDK state flows must cover parallel calls, mixed text/tool output, configuration isolation, exact history matching and prompt-cache identity; replacement of removed internal state tests is in progress.
- `ProviderCapabilityEvidenceTests`: operation variants execute through HTTP with success, rejection and queue recovery; operation contracts retain official documentation references.

## Request constraints and diagnostics

OpenAI Chat requires an explicit wire `model`; only Azure deployment routes derive it from the URL. Convenience clients continue to supply their configured default model. OpenAI Chat and Responses honor `parallel_tool_calls=false`; Anthropic honors `tool_choice.disable_parallel_tool_use=true` ([parallel tool contract](https://platform.claude.com/docs/en/agents-and-tools/tool-use/parallel-tool-use)). `ChatOptions.AllowMultipleToolCalls` is forwarded by the Microsoft.Extensions.AI client. The neutral runtime exposes `AllowParallelToolCalls` (default true). Incompatible fixtures return 409 without consuming the response, including on streaming requests.

Gemini OpenAPI `responseSchema` and function `parameters` normalize schema types, `nullable`, and string int64 bounds (`minItems`, `maxItems`, `minProperties`, `maxProperties`, `minLength`, `maxLength`) according to the [Schema reference](https://ai.google.dev/api/generate-content?hl=en#Schema). Conversion traverses properties, items, and anyOf schemas; it leaves example/default/enum values unchanged. `responseJsonSchema` and `parametersJsonSchema` retain their JSON Schema representation.

Every fixture mismatch produces an `ErrorReturned` runtime event with request/scenario correlation, error text, and input token usage. It increments `ErrorsReturned`; it does not warm the prompt cache or consume a response. Returned assertion summaries detach nested message/tool-call and chunk collections from the retained journal.

`WithExactMatch` now compares tool-call IDs and the complete ordered tool-call collection, including null/empty values. Callers that used empty expected tool metadata as a wildcard must select Contains matching instead. Contains retains the existing optional tool metadata behavior.

Wire tool validation lives in the corresponding provider package, returning `LlmTestServerRequestValidationResult`. Hosting selects a provider validator and maps invalid results to the provider's HTTP error shape. Provider-independent JSON primitives are linked from `src/Shared`; the neutral runtime has no provider DTO dependencies.

`ProviderCapabilityEvidenceTests` executes a positive and negative fixture for each `(provider, operation, Tools/StructuredOutput, JSON/stream)` case supported by the checked-in contracts. Its guard also requires executable provider arguments and named nullable/limit/anyOf and parallel-option regression cases. This supplements operation-level evidence; it is not a claim of exhaustive upstream API coverage.

## Bounded video jobs

`WithVideoCapacity(maxJobs, maxBytes)` configures a shared runtime budget across stored provider video jobs. Defaults are 256 jobs and 64 MiB of retained payload bytes. Zero jobs disables storage. New jobs exceeding either bound return 409 / `llm_tck_video_capacity_exceeded`; existing jobs remain available and failed stores do not advance IDs. Delete releases capacity; reset/configure clear jobs and byte accounting. OpenAI and Azure video job endpoints propagate the failure. This budget applies to stored jobs, separately from configured modality fixture bytes and the event-journal budget.

Schema validation uses the MIT-licensed JsonSchema.Net 8.0.5 package so installed Test Server binaries do not introduce the separate binary maintenance terms from later releases. Keep fixture validation and external-reference rejection covered when changing this dependency.
