# Official provider SDK integration

Supported success, streaming, tool and conversation flows must be exercised through the official
provider SDK against a running Test Server. Raw HTTP supplements malformed input and exact wire
assertions. A custom HTTP transport still sends real requests to the hosted server; it does not
replace SDK serialization, parsing, streaming or error handling.

| Client | Pinned package | Added running-server evidence |
| --- | --- | --- |
| Anthropic | `Anthropic` 12.60.0 | Native web search JSON/SSE, citations, assistant history and all six native search errors |
| Google Gemini | `Google.GenAI` 1.26.1 | GenerateContent JSON/SSE, signed function rounds, batch embeddings, dimensions, auth, filters and error recovery (36 cases) |
| Amazon Bedrock | `AWSSDK.BedrockRuntime` 4.0.101.12 | Converse, ConverseStream, InvokeModel, InvokeModelWithResponseStream, tool rounds and rate-limit recovery |
| OpenAI-compatible routes | `OpenAI` 2.14.0 | Groq, OpenRouter, Mistral and DeepSeek function rounds, JSON/SSE, provider errors and explicit recovery |

The last row proves compatibility with the official **OpenAI client** at those route roots. It does
not claim that the OpenAI package is a provider-owned SDK for those providers. Existing OpenAI,
Azure OpenAI and Microsoft Foundry SDK integration suites remain part of the full gate. Other
provider SDK families and all remaining roadmap capabilities still require their own qualification.

## Current model provenance

Anthropic fixtures use the original SDK identifier `Anthropic.Models.Messages.Model.ClaudeSonnet5_5`.
The provider-neutral contract uses the corresponding documented wire ID `claude-sonnet-5-5`.
Sonnet 5.5 accepts automatic tool choice and rejects forced `any`/named tool choice. Sources:
[official SDK](https://github.com/anthropics/anthropic-sdk-csharp),
[Sonnet 5.5](https://platform.claude.com/docs/en/models/sonnet-5-5/overview).

Google's SDK accepts a model string and does not expose a closed model enum. Its current fixtures use
[`gemini-3.8-flash`](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash) and
[`gemini-embedding-2`](https://ai.google.dev/gemini-api/docs/models/gemini-embedding-2), taken from the
current official model pages. Do not invent SDK constants that do not exist.

The added OpenAI-client compatibility fixtures explicitly configure the documented
[`gpt-5.5`](https://developers.openai.com/api/docs/models/gpt-5.5) fixture ID. A configured model is
local deterministic test data; this is not a claim that every compatible remote provider hosts it.

Native decision wire identifiers live separately in `LlmTestServerDecisionModelIds`, with original
provider/source links. They are opt-in decision profiles and are not registered as default chat
models. Account-specific live model availability still belongs to each provider's authenticated
Models API; the test server does not pretend to discover a live account catalogue.
