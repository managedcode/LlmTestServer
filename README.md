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
2. **Choose the answer** — try the built-in demo, or script your own with the typed .NET client or management API.
3. **Call it with your SDK** — point the base URL at `http://localhost:8080/openai/v1`, `/anthropic/v1`,
   `/gemini/v1beta` or another [provider namespace](#providers).

Then open `http://localhost:8080` to see the request, the response and the token flow.

## Quick start

[![Docker](https://img.shields.io/badge/Docker-2496ED?style=flat-square&logo=docker&logoColor=white)](#quick-start)
[![Python](https://img.shields.io/badge/Python-3776AB?style=flat-square&logo=python&logoColor=white)](#python)
[![TypeScript](https://img.shields.io/badge/TypeScript-3178C6?style=flat-square&logo=typescript&logoColor=white)](#typescript--javascript)
[![C# / .NET](https://img.shields.io/badge/C%23%20%2F%20.NET-512BD4?style=flat-square&logo=dotnet&logoColor=white)](#c)
[![.NET Aspire](https://img.shields.io/badge/.NET%20Aspire-512BD4?style=flat-square&logo=dotnet&logoColor=white)](#net-and-aspire)

Start the server:

```sh
docker run --rm --name llm-test-server -p 8080:8080 \
  -e LlmTestServer__RequiredBearerToken=test-key \
  ghcr.io/managedcode/llm-test-server:1.0.0
```

Configure two replies for [GPT-6.1 Sol](https://developers.openai.com/api/docs/models/gpt-6.1-sol)
with the typed .NET control SDK. Run this setup before each language example to reload its response queue:

```sh
dotnet add package ManagedCode.LlmTestServer.Client
```

```csharp
using ManagedCode.LlmTestServer.Client;

using var http = new HttpClient { BaseAddress = new Uri("http://localhost:8080") };
var control = new LlmTestServerClient(http, "test-key");
await control.ConfigureAsync(config => config
    .RequireBearerToken("test-key")
    .UseReasoningChatModel("gpt-6.1-sol", reasoningTokens: 8)
    .UseChatScenario("hello", scenario => scenario
        .ForModel("gpt-6.1-sol")
        .Responds("blue whale", "blue ", "whale")
        .Responds("blue whale", "blue ", "whale")));
```

The fixture returns `blue whale` for both a normal call and a streaming call.
For Python- or JavaScript-only test suites, the same setup is available through the
[management API](docs/Reference/Configuration.md).

These are standard calls to the official OpenAI SDKs for [C#](https://github.com/openai/openai-dotnet),
[Python](https://github.com/openai/openai-python) and [TypeScript/JavaScript](https://github.com/openai/openai-node).
Your application changes only the endpoint and test API key. The server supplies the scripted response,
stream chunks and token usage; it never calls a real model.

### C\#

```sh
dotnet add package OpenAI
```

Create the client and request a complete response:

```csharp
using System.ClientModel;
using OpenAI;
using OpenAI.Chat;

var chat = new ChatClient(
    "gpt-6.1-sol",
    new ApiKeyCredential("test-key"),
    new OpenAIClientOptions
    {
        Endpoint = new Uri("http://localhost:8080/openai/v1"),
    });

ChatCompletion reply = await chat.CompleteChatAsync("What color is the largest animal?");
Console.WriteLine(reply.Content[0].Text); // blue whale
```

Stream the next response with the same client:

```csharp
await foreach (var update in chat.CompleteChatStreamingAsync("What color is the largest animal?"))
{
    foreach (var part in update.ContentUpdate)
    {
        Console.Write(part.Text);
    }
}
// blue whale
```

### Python

```sh
pip install openai
```

Create the client and request a complete response:

```python
from openai import OpenAI

client = OpenAI(
    base_url="http://localhost:8080/openai/v1",
    api_key="test-key",
)
reply = client.chat.completions.create(
    model="gpt-6.1-sol",
    messages=[{"role": "user", "content": "What color is the largest animal?"}],
)
print(reply.choices[0].message.content)  # blue whale
```

Stream the next response with the same client:

```python
stream = client.chat.completions.create(
    model="gpt-6.1-sol",
    messages=[{"role": "user", "content": "What color is the largest animal?"}],
    stream=True,
)
for chunk in stream:
    if chunk.choices:
        print(chunk.choices[0].delta.content or "", end="", flush=True)
# blue whale
```

### TypeScript / JavaScript

```sh
npm install openai
```

The same code works in TypeScript or a JavaScript ES module (`.mjs`).
Create the client and request a complete response:

```ts
import OpenAI from "openai";

const client = new OpenAI({
  baseURL: "http://localhost:8080/openai/v1",
  apiKey: "test-key",
});
const reply = await client.chat.completions.create({
  model: "gpt-6.1-sol",
  messages: [{ role: "user", content: "What color is the largest animal?" }],
});
console.log(reply.choices[0].message.content); // blue whale
```

Stream the next response with the same client:

```ts
const stream = await client.chat.completions.create({
  model: "gpt-6.1-sol",
  messages: [{ role: "user", content: "What color is the largest animal?" }],
  stream: true,
});
for await (const chunk of stream) {
  process.stdout.write(chunk.choices[0]?.delta.content ?? "");
}
// blue whale
```

Open `http://localhost:8080` to inspect both requests, their responses and token usage.
To script your own answers, use the [typed .NET client](#script-your-own-answers) or the
[management API](docs/Reference/Configuration.md).
For other providers, use their official SDK with the corresponding
[provider namespace](docs/Reference/ProvidersAndRoutes.md).

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

### Script your own answers

This complete C# integration test starts its own server, loads a two-reply scenario and runs a dialogue
through the official OpenAI SDK. Aspire chooses the URL; the test needs no manually started server or Docker.

In a .NET 10 TUnit test project, use `<Project Sdk="Aspire.AppHost.Sdk/13.6.2">` and install:

```sh
dotnet add package ManagedCode.LlmTestServer.Aspire
dotnet add package ManagedCode.LlmTestServer.Client
dotnet add package Aspire.Hosting.Testing
dotnet add package OpenAI
```

```csharp
using System.ClientModel;
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;
using ManagedCode.LlmTestServer.Client;
using OpenAI;
using OpenAI.Chat;

public sealed class ChatDialogueTests
{
    [Test]
    public async Task ClientReceivesScriptedDialogue(CancellationToken cancellationToken)
    {
        // 1. Start the server. Aspire allocates the address and port.
        var builder = DistributedApplicationTestingBuilder.Create([]);
        builder.AddLlmTestServer("llm").WithApiKey("test-key");
        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("llm", cancellationToken);

        // 2. Script the replies in the order the client will receive them.
        using var http = app.CreateHttpClient("llm", "http");
        var control = new LlmTestServerClient(http, "test-key");
        await control.ConfigureAsync(config => config
            .RequireBearerToken("test-key")
            .UseReasoningChatModel("gpt-6.1-sol", reasoningTokens: 8)
            .UseChatScenario("delivery", scenario => scenario
                .ForModel("gpt-6.1-sol")
                .Responds("Your order arrives on Friday.")
                .Responds("Yes, delivery is free.")), cancellationToken);

        // 3. Call the server through the official OpenAI SDK.
        var chat = new ChatClient("gpt-6.1-sol", new ApiKeyCredential("test-key"),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "openai/v1"),
            });
        List<ChatMessage> messages = [new UserChatMessage("When will my order arrive?")];
        ChatCompletion first = await chat.CompleteChatAsync(messages, cancellationToken: cancellationToken);

        // 4. Continue the dialogue, including the previous answer in the history.
        messages.Add(new AssistantChatMessage(first));
        messages.Add(new UserChatMessage("Is delivery free?"));
        ChatCompletion second = await chat.CompleteChatAsync(messages, cancellationToken: cancellationToken);

        // 5. Verify the answers and that both requests matched the scenario.
        await Assert.That(first.Content[0].Text).IsEqualTo("Your order arrives on Friday.");
        await Assert.That(second.Content[0].Text).IsEqualTo("Yes, delivery is free.");
        var recorded = await control.GetAssertionsAsync(cancellationToken);
        await Assert.That(recorded.Matched).IsEqualTo(2);
    }
}
```

The scenario queues exact replies; it does not infer answers from the prompts. The second SDK call sends
the full dialogue so your application exercises the same conversation path it uses with a real provider.
At the end of the test, disposing `app` stops the server.

[Runnable test source](scripts/package-consumer/ReadmeDialoguePackageTests.cs).

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
| [ManagedCode.LlmTestServer.OpenAI](https://www.nuget.org/packages/ManagedCode.LlmTestServer.OpenAI) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.OpenAI.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.OpenAI) | use OpenAI wire contracts and compatibility adapters |
| [ManagedCode.LlmTestServer.AzureOpenAI](https://www.nuget.org/packages/ManagedCode.LlmTestServer.AzureOpenAI) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.AzureOpenAI.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.AzureOpenAI) | use Azure OpenAI compatibility contracts and adapters |
| [ManagedCode.LlmTestServer.Foundry](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Foundry) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Foundry.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Foundry) | use Microsoft Foundry compatibility contracts and adapters |
| [ManagedCode.LlmTestServer.Anthropic](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Anthropic) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Anthropic.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Anthropic) | use Anthropic Messages contracts and adapters |
| [ManagedCode.LlmTestServer.Gemini](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Gemini) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Gemini.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Gemini) | use Google Gemini compatibility contracts and adapters |
| [ManagedCode.LlmTestServer.Groq](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Groq) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Groq.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Groq) | use Groq compatibility contracts and adapters |
| [ManagedCode.LlmTestServer.Mistral](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Mistral) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Mistral.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Mistral) | use Mistral compatibility contracts and adapters |
| [ManagedCode.LlmTestServer.Ollama](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Ollama) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Ollama.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Ollama) | use Ollama compatibility contracts and adapters |
| [ManagedCode.LlmTestServer.Cohere](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cohere) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Cohere.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cohere) | use Cohere compatibility contracts and adapters |
| [ManagedCode.LlmTestServer.Bedrock](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Bedrock) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Bedrock.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Bedrock) | use Amazon Bedrock compatibility contracts and adapters |
| [ManagedCode.LlmTestServer.OpenRouter](https://www.nuget.org/packages/ManagedCode.LlmTestServer.OpenRouter) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.OpenRouter.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.OpenRouter) | use OpenRouter compatibility contracts and adapters |
| [ManagedCode.LlmTestServer.DeepSeek](https://www.nuget.org/packages/ManagedCode.LlmTestServer.DeepSeek) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.DeepSeek.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.DeepSeek) | use DeepSeek compatibility contracts and adapters |
| [ManagedCode.LlmTestServer.Perplexity](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Perplexity) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Perplexity.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Perplexity) | use Perplexity compatibility contracts and adapters |
| [ManagedCode.LlmTestServer.SystemOne](https://www.nuget.org/packages/ManagedCode.LlmTestServer.SystemOne) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.SystemOne.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.SystemOne) | use TypeSafe/Jev native System One decision contracts |
| [ManagedCode.LlmTestServer.Cloudflare](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cloudflare) | [![NuGet version](https://img.shields.io/nuget/v/ManagedCode.LlmTestServer.Cloudflare.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/ManagedCode.LlmTestServer.Cloudflare) | use native Cloudflare Workers AI decision contracts |

## Build your next test

| Run the server | Shape the response | Connect your app |
| --- | --- | --- |
| [**Installation & hosting →**](docs/Reference/Installation.md) | [**Scenarios & fixtures →**](docs/Reference/Configuration.md) | [**Clients & SDKs →**](docs/Reference/Clients.md) |
| Choose Docker, Aspire or your own ASP.NET Core host. | Script a dialogue, stream tokens or trigger a failure. | Call the server with the SDK your app already uses. |

Looking for a specific API? Check [providers & routes](docs/Reference/ProvidersAndRoutes.md).
Contributing? Start with the [architecture](docs/Architecture.md).

---

<p align="center">
  <strong>Predictable answers. Real SDKs. Tests you can trust.</strong><br>
  Built by <a href="https://managed-code.com">Managed Code</a><br>
  <a href="https://llmtest.managed-code.com">Website</a> ·
  <a href="https://github.com/managedcode/LlmTestServer">GitHub</a> ·
  <a href="LICENSE">MIT License</a><br>
  <sub>Copyright © 2026 Managed Code</sub>
</p>
