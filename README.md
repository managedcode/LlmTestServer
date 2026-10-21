# LLM Test Server

[![CI](https://github.com/managedcode/LlmTestServer/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/managedcode/LlmTestServer/actions/workflows/ci.yml)
[![Release](https://github.com/managedcode/LlmTestServer/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/managedcode/LlmTestServer/actions/workflows/release.yml)
[![NuGet](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ManagedCode.LlmTestServer)
[![Docker](https://img.shields.io/badge/Docker-ghcr.io%2Fmanagedcode%2Fllm--test--server-2496ED?style=flat-square&logo=docker&logoColor=white)](https://github.com/orgs/managedcode/packages/container/package/llm-test-server)
[![License](https://img.shields.io/github/license/managedcode/LlmTestServer.svg?style=flat-square)](LICENSE)

**Deterministic LLM APIs for your tests — in .NET, Python, TypeScript or any other stack.
See exactly what your app sent and where every token went.**

[llmtest.managed-code.com](https://llmtest.managed-code.com)

[![Where the tokens of a five-request conversation go: system prompt, tool schemas and history flowing into prompt-cache reads, writes, response and reasoning](docs/images/showcase-token-flow.jpg)](docs/images/showcase-token-flow.jpg)

| Every request, in context | All traffic at a glance |
| --- | --- |
| [![Request inspector with the conversation, tool call and highlighted payloads](docs/images/showcase-overview.jpg)](docs/images/showcase-overview.jpg) | [![Traffic insights: providers, outcomes and prompt cache as charts](docs/images/showcase-insights.jpg)](docs/images/showcase-insights.jpg) |

## What it is

LLM Test Server is a local server that answers like an LLM provider — OpenAI, Anthropic, Gemini and ten more — but
returns exactly what your test scripted. Your app keeps its official SDK and only changes the base URL.

- **The same answer on every run.** Script replies, streams, tool calls, rate limits and content filters.
- **Your real code path.** Requests travel through the provider SDKs and wire formats you use in production.
- **Every token visible.** The dashboard splits each prompt into system prompt, tool schemas, history and tool results,
  and shows how much the prompt cache served.
- **Any stack.** NuGet packages and an Aspire integration for .NET; one Docker image for everything else.

## How it works

1. **Start the server** — one `docker run`, or one line in your Aspire AppHost.
2. **Script the answer** — over the management API, or with the typed .NET client.
3. **Call it with your SDK** — point the base URL at `http://localhost:8080/openai/v1`, `/anthropic/v1`,
   `/gemini/v1beta` or another [provider namespace](#providers).

Then open `http://localhost:8080` to see the request, the response and the token flow.

## Quick start

[![Docker](https://img.shields.io/badge/Docker-2496ED?style=flat-square&logo=docker&logoColor=white)](#quick-start)
[![Python](https://img.shields.io/badge/Python-3776AB?style=flat-square&logo=python&logoColor=white)](#python)
[![TypeScript](https://img.shields.io/badge/TypeScript-3178C6?style=flat-square&logo=typescript&logoColor=white)](#typescript)
[![C# / .NET](https://img.shields.io/badge/C%23%20%2F%20.NET-512BD4?style=flat-square&logo=dotnet&logoColor=white)](#c)
[![.NET Aspire](https://img.shields.io/badge/.NET%20Aspire-512BD4?style=flat-square&logo=dotnet&logoColor=white)](#net-and-aspire)

Start the server:

```sh
docker run -p 8080:8080 -e LlmTestServer__RequiredBearerToken=test-key \
  ghcr.io/managedcode/llm-test-server:1.0.0
```

Script an answer:

```sh
curl localhost:8080/admin-api/configure -H "Authorization: Bearer test-key" --json '{
  "requiredBearerToken": "test-key",
  "models": [{ "id": "gpt-4.1-mini", "kind": "chat" }],
  "chatScenarios": [{ "id": "hello", "modelId": "gpt-4.1-mini",
                      "responses": [{ "content": "Hello from your test!" }] }]
}'
```

Call it from your language. Each scripted reply is used once, so configure again before the next call.

### Python

`pip install openai`

```python
from openai import OpenAI

client = OpenAI(base_url="http://localhost:8080/openai/v1", api_key="test-key")
reply = client.chat.completions.create(
    model="gpt-4.1-mini",
    messages=[{"role": "user", "content": "Hi"}],
)
print(reply.choices[0].message.content)  # Hello from your test!
```

### TypeScript

`npm install openai`

```ts
import OpenAI from "openai";

const client = new OpenAI({ baseURL: "http://localhost:8080/openai/v1", apiKey: "test-key" });
const reply = await client.chat.completions.create({
  model: "gpt-4.1-mini",
  messages: [{ role: "user", content: "Hi" }],
});
console.log(reply.choices[0].message.content); // Hello from your test!
```

### C\#

`dotnet add package OpenAI`

```csharp
using System.ClientModel;
using OpenAI;
using OpenAI.Chat;

var chat = new ChatClient("gpt-4.1-mini", new ApiKeyCredential("test-key"),
    new OpenAIClientOptions { Endpoint = new Uri("http://localhost:8080/openai/v1") });
ChatCompletion reply = await chat.CompleteChatAsync("Hi");
Console.WriteLine(reply.Content[0].Text); // Hello from your test!
```

Another provider? Use its SDK with its [namespace](docs/Reference/ProvidersAndRoutes.md). More on running the image:
[Docker distribution](docs/Features/DockerDistribution.md).

## .NET and Aspire

```sh
dotnet add package ManagedCode.LlmTestServer.Aspire
dotnet add package ManagedCode.LlmTestServer.Client
```

In your AppHost — the server runs as a .NET process, no Docker needed:

```csharp
using ManagedCode.LlmTestServer.Aspire;

var builder = DistributedApplication.CreateBuilder(args);
var llm = builder.AddLlmTestServer().WithApiKey("test-key");

builder.AddProject<Projects.Api>("api")
    .WithEnvironment("OPENAI_ENDPOINT", llm.GetOpenAiEndpoint())
    .WithEnvironment("OPENAI_API_KEY", "test-key")
    .WaitFor(llm);

builder.Build().Run();
```

In a test, script the answer with the typed client. Add `Aspire.Hosting.Testing` and give the test project the
Aspire SDK (`<Project Sdk="Aspire.AppHost.Sdk/13.6.2">`) so it can start the server:

```csharp
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;
using ManagedCode.LlmTestServer.Client;
using Microsoft.Extensions.AI;

var builder = DistributedApplicationTestingBuilder.Create([]);
builder.AddLlmTestServer("llm").WithApiKey("test-key");
await using var app = await builder.BuildAsync();
await app.StartAsync();
await app.ResourceNotifications.WaitForResourceHealthyAsync("llm");

var control = new LlmTestServerClient(app.CreateHttpClient("llm", "http"), "test-key");
await control.ConfigureAsync(config => config
    .RequireBearerToken("test-key")
    .UseGpt41Mini()
    .UseChatScenario("hello", scenario => scenario.Responds("Hello from your test!")));

var reply = await control.CreateChatClient().GetResponseAsync("Hi");
Console.WriteLine(reply.Text); // Hello from your test!
```

To host the server and dashboard inside your own ASP.NET Core app, see
[installation and hosting](docs/Reference/Installation.md).

## Token usage and prompt-cache hits

Every chat request gets deterministic usage from the server's tokenizer, returned in each provider's own usage fields.

- **What the input is made of** — system prompt, tool schemas, user, assistant, tool calls and tool results; the parts
  add up exactly to the reported input.
- **What the prompt cache served** — cache reads and writes follow each provider's rules.
- **What the model produced** — output tokens, with reasoning tokens counted on their own.

The dashboard's **Tokens** tab draws it for one request or a whole conversation, and **Traffic insights** charts all
recorded calls. Tests read the same numbers from the request journal. Details:
[token usage and prompt cache](docs/Reference/TokenUsageAndPromptCache.md) ·
[dashboard analytics](docs/Features/DashboardAnalytics.md).

## What you can do

| Capability | Guide |
| --- | --- |
| Chat scenarios: prompt matching, queued replies, streaming chunks, delays, scripted errors | [Configuration](docs/Reference/Configuration.md) |
| Tool calls and structured output, including failing tools | [Tools](docs/Features/ToolAndStructuredFixtures.md) · [Tool faults](docs/Features/ToolFaultFixtures.md) |
| Multi-turn conversations as typed step graphs | [Conversation DSL](docs/Features/ConversationDSL.md) |
| Rate limits (429) and content filters on every provider | [Fault simulation](docs/Reference/Configuration.md#fault-simulation) |
| Embeddings, images, speech, transcription and video | [Modalities](docs/Reference/Configuration.md#modalities) |
| Parallel tests with isolated sessions | [Test sessions](docs/Features/TestSessions.md) |
| Verify what was sent: journal queries, counts, order, tool arguments | [Verification](docs/Features/RequestVerificationAndDeterminism.md) |

The server never calls a real model, and nothing is hidden behind retries or randomness.

## Providers

| Provider | Namespace | Provider | Namespace |
| --- | --- | --- | --- |
| OpenAI | `/openai` | Mistral | `/mistral` |
| Azure OpenAI | `/azure-openai` | Ollama | `/ollama` |
| Microsoft Foundry | `/microsoft-foundry` | Cohere | `/cohere` |
| Anthropic | `/anthropic` | Amazon Bedrock | `/bedrock` |
| Gemini | `/gemini` | OpenRouter | `/openrouter` |
| Groq | `/groq` | DeepSeek | `/deepseek` |
| Perplexity | `/perplexity` | TypeSafe Jev / Kev | `/systemone` |
| Cloudflare Clef | `/cloudflare` | | |

Supported operations differ by provider — check the [operation coverage](docs/Features/ProviderApiContractCoverage.md).

**TypeSafe** is the provider and **Jev** its model family; System One is the native API at
`POST /systemone/v1/systemone`. Use `ManagedCode.LlmTestServer.SystemOne` for its contracts and
`control.CreateSystemOneClient()` from the Client package —
[TypeSafe/Jev example](docs/Reference/Clients.md#typesafe-jev-through-system-one).

## Packages

| Package | NuGet | Use it to |
| --- | --- | --- |
| [ManagedCode.LlmTestServer.Aspire](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Aspire) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Aspire.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Aspire) | start the server from an Aspire AppHost or test |
| [ManagedCode.LlmTestServer.Client](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Client) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Client.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Client) | script fixtures and verify recorded requests |
| [ManagedCode.LlmTestServer.Hosting](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Hosting) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Hosting.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Hosting) | host the server and dashboard in your ASP.NET Core app |
| [ManagedCode.LlmTestServer](https://www.nuget.org/packages/ManagedCode.LlmTestServer) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer) | build on the provider-neutral runtime |

Each provider also has its own contracts package, such as `ManagedCode.LlmTestServer.OpenAI` or
`ManagedCode.LlmTestServer.Anthropic` — [all packages on NuGet](https://www.nuget.org/packages?q=ManagedCode.LlmTestServer).

## Learn more

- [Installation and hosting](docs/Reference/Installation.md)
- [Configuration and scenarios](docs/Reference/Configuration.md)
- [Clients and SDKs](docs/Reference/Clients.md)
- [Providers and routes](docs/Reference/ProvidersAndRoutes.md)
- [Architecture](docs/Architecture.md)

## License

[MIT](LICENSE)
