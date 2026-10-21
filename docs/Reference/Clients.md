# Clients and SDKs

Calling LLM Test Server from tests: the control client, official provider SDKs, `Microsoft.Extensions.AI`, and the native TypeSafe client. Back to the [README](../../README.md).

## First passing test

In a .NET 10 TUnit test project, add `ManagedCode.LlmTestServer.Aspire`, `ManagedCode.LlmTestServer.Client`
and `Aspire.Hosting.Testing`. This starts the packaged server without Docker, configures a response, calls it
through `IChatClient` and verifies the recorded request:

```csharp
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;
using ManagedCode.LlmTestServer.Client;
using Microsoft.Extensions.AI;

public sealed class AiIntegrationTests
{
    [Test]
    public async Task ApplicationReceivesScriptedResponse(CancellationToken cancellationToken)
    {
        var builder = DistributedApplicationTestingBuilder.Create([]);
        builder.AddLlmTestServer("llm-test-server").WithApiKey("test-key");
        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("llm-test-server", cancellationToken);

        using var http = app.CreateHttpClient("llm-test-server", "http");
        var control = new LlmTestServerClient(http, "test-key");
        await control.ConfigureAsync(config => config.RequireBearerToken("test-key").UseGpt41Mini()
            .UseChatScenario("greeting", scenario => scenario.WhenUserContains("hello").Responds("Hello from your test!")),
            cancellationToken);
        using var chat = control.CreateChatClient();
        var response = await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: cancellationToken);

        await Assert.That(response.Text).IsEqualTo("Hello from your test!");
        await Assert.That((await control.GetAssertionsAsync(cancellationToken)).Matched).IsEqualTo(1);
    }
}
```

See [provider operation coverage](../Features/ProviderApiContractCoverage.md) for the exact supported contracts.

## Complete Aspire dialogue test

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

[Runnable test source](../../scripts/package-consumer/ReadmeDialoguePackageTests.cs).

## Parallel tests on a shared server

Create a separate session for each test. Each one owns its configuration, response queues, history, prompt cache,
fault counters, video jobs and request journal. Disposal removes its server state; the shared transport stays usable.

```csharp
await using var session = await control.CreateSessionAsync(config => config
    .RequireBearerToken("test-key")
    .UseChatScenario("isolated", scenario => scenario.Responds("This test's response")));
using var chat = session.Control.CreateChatClient();
var response = await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);
```

Use `session.HttpClient` as the transport for official provider SDKs. See [test sessions](../Features/TestSessions.md)
for credentials, capacity limits, configure/reset semantics and HTTP routes.

## TypeSafe: Jev through System One

**TypeSafe is the provider, Jev is its model family, and System One is the native API/protocol.**
`ManagedCode.LlmTestServer.SystemOne` contains the native DTOs; `ManagedCode.LlmTestServer.Client`
provides `LlmTestServerSystemOneClient`. The package name remains `SystemOne` because it also hosts the Kev profile.

Jev is supported through its native System One API, including Jev 1.14 image assets
and bounding boxes. The emulator accepts JSON base64 and multipart PNG/JPEG uploads,
structured instructions and criteria, mixed `noul`/`choice`/`score`/`box` batches,
native confidence, usage and provider-resolved model versions. Image and box requests
require `TypeSafe-Danger-Zone-Images: true`; long-context requests can explicitly send
`TypeSafe-Danger-Zone-Long-Context: true`. It uses deterministic fixtures and never
calls a production model or estimates image tokens.

- Native test-server route: `POST /systemone/v1/systemone`.
- Contracts: `ManagedCode.LlmTestServer.SystemOne`.
- Typed client: `LlmTestServerClient.CreateSystemOneClient()` from `ManagedCode.LlmTestServer.Client`.
- Register `jev-1.14.0` or `jev-preview` with `AddDecisionModel(id, LlmTestServerDecisionProvider.TypeSafe)`.
- [Jev 1.14 guide and limits](../Features/Jev114.md), [native decision fixtures](ProvidersAndRoutes.md#native-decision-api-fixtures),
  [official images API](https://docs.typesafe.ai/images).

Using the `control` client and Aspire-allocated HTTP transport from the first test above, configure a TypeSafe
fixture and call the native typed client:

```csharp
using System.Text.Json;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.SystemOne;

await control.ConfigureAsync(config => config
    .RequireBearerToken("test-key")
    .UseDecisionModel(TypeSafeRequestValidation.Jev114Model, LlmTestServerDecisionProvider.TypeSafe)
    .UseDecisionScenario(new LlmTestServerDecisionScenario
    {
        Id = "typesafe-priority",
        ModelId = TypeSafeRequestValidation.Jev114Model,
        Answers = new()
        {
            ["urgent"] = new() { Kind = LlmTestServerDecisionKind.Predicate, Probability = 0.95 },
        },
    }));

var jev = control.CreateSystemOneClient();
var decision = await jev.EvaluateAsync(new SystemOneDecisionRequest
{
    Model = TypeSafeRequestValidation.Jev114Model,
    State = JsonSerializer.SerializeToElement("Please help quickly"),
    Questions = new()
    {
        ["urgent"] = new()
        {
            Type = SystemOneDecisionTypes.Predicate,
            Instructions = JsonSerializer.SerializeToElement("Does this need urgent attention?"),
        },
    },
});
Console.WriteLine(decision.Answers["urgent"].Probability); // 0.95
```

The client sends `POST /systemone/v1/systemone` using the control client's transport and configured test key.
For image/box fixtures, use `EvaluateAsync(..., enableImages: true)` or `EvaluateMultipartAsync(...)`;
see the [Jev 1.14 guide](../Features/Jev114.md) for explicit assets and native limits.

## OpenAI SDK

The official OpenAI .NET SDK expects its `Endpoint` to be the API root. Use
`/openai/v1` for this Test Server, or `llmTestServer.GetOpenAiEndpoint()` from the Aspire
package when wiring an AppHost resource.

```csharp
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

var chat = new ChatClient(
    "gpt-4.1-mini",
    new ApiKeyCredential("test-key"),
    new OpenAIClientOptions
    {
        Endpoint = new Uri("http://localhost:5000/openai/v1"),
    });

await foreach (var update in chat.CompleteChatStreamingAsync(
                   [new UserChatMessage("What is the invoice total?")]))
{
    Console.Write(update.ContentUpdate.Count > 0 ? update.ContentUpdate[0].Text : string.Empty);
}
```

## Azure OpenAI And Foundry SDKs

The OpenAI v1 API is available at `/azure-openai/openai/v1/` and `/microsoft-foundry/openai/v1/`, with `chat/completions`, `responses` and `embeddings`. Point the official OpenAI client endpoint at the corresponding base URL and supply the configured API key. These v1 routes accept bearer or `api-key` authentication and need no dated `api-version` query parameter. Responses supports ordinary JSON and documented SSE events. See the [September 2026 API review](../Features/ProviderApiReview20260907.md) for the verified SDK versions and provider contracts.

Azure OpenAI deployment routes use the deployment name as the Test Server model id. API keys sent through the official SDK's `api-key` header are accepted anywhere a bearer token would be accepted.

```csharp
using Azure.AI.OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using System.ClientModel;

var azure = new AzureOpenAIClient(
    new Uri("http://localhost:5000/azure-openai/"),
    new ApiKeyCredential("test-key"));

ChatClient chat = azure.GetChatClient("gpt-4.1-mini");
EmbeddingClient embeddings = azure.GetEmbeddingClient("text-embedding-3-small");

var answer = await chat.CompleteChatAsync(
[
    new UserChatMessage("What is the invoice total?"),
]);

var vector = await embeddings.GenerateEmbeddingAsync("invoice");
```

Microsoft Foundry / Azure AI Inference clients call the `/microsoft-foundry/chat/completions` and `/microsoft-foundry/embeddings` routes. The inference routes require `api-version=2024-05-01-preview`, which the official SDK supplies. Set `Model` to the model id configured in LLM Test Server.

```csharp
using Azure;
using Azure.AI.Inference;

var endpoint = new Uri("http://localhost:5000/microsoft-foundry/");
var credential = new AzureKeyCredential("test-key");

var chat = new ChatCompletionsClient(endpoint, credential);
var embeddings = new EmbeddingsClient(endpoint, credential);

var completion = await chat.CompleteAsync(
    new ChatCompletionsOptions(
    [
        new ChatRequestUserMessage("What is the invoice total?"),
    ])
    {
        Model = "gpt-4.1-mini",
    });

var embedding = await embeddings.EmbedAsync(
    new EmbeddingsOptions(["invoice"])
    {
        Model = "text-embedding-3-small",
    });
```

## Microsoft.Extensions.AI

Use the client package when you want tests to call the fake provider through normal `Microsoft.Extensions.AI` abstractions:

```csharp
using ManagedCode.LlmTestServer.Client;
using Microsoft.Extensions.AI;

using var httpClient = new HttpClient
{
    BaseAddress = new Uri("http://localhost:5000"),
};
httpClient.DefaultRequestHeaders.Authorization =
    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-key");

using IChatClient chatClient = new LlmTestServerChatClient(httpClient);

var response = await chatClient.GetResponseAsync(
[
    new ChatMessage(ChatRole.User, "What color is the largest animal?"),
]);

Console.WriteLine(response.Text);
```

When a bearer token is required, prefer creating the modality clients from the control client so the same token is applied consistently:

```csharp
var server = LlmTestServerClient.Create(httpClient, bearerToken: "test-key");

using IChatClient chatClient = server.CreateChatClient("docs-chat");
using IEmbeddingGenerator<string, Embedding<float>> embeddings =
    server.CreateEmbeddingGenerator("docs-embedding");
using IImageGenerator images = server.CreateImageGenerator("docs-image");
```

Streaming uses the same client:

```csharp
await foreach (var update in chatClient.GetStreamingResponseAsync(
    [new ChatMessage(ChatRole.User, "stream this")]))
{
    Console.Write(update.Text);
}
```
