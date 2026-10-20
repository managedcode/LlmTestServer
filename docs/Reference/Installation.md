# Installation and hosting

How to install the packages, run the standalone container, embed the server in ASP.NET Core or Aspire, and build from source. Back to the [README](../../README.md).

## Choose how to run

| Setup | Install / start | Best fit |
| --- | --- | --- |
| **.NET + Aspire** | `ManagedCode.LlmTestServer.Aspire` + `ManagedCode.LlmTestServer.Client` | Start an isolated .NET server from your AppHost or integration test; Docker is optional. |
| **Standalone Docker** | `managedcode/llm-test-server:local`, built with the .NET SDK | Run the dashboard, management API and provider routes together for applications in any language. |
| **ASP.NET Core library** | `ManagedCode.LlmTestServer.Hosting` | Embed the server in your own host with `AddLlmTestServer(...)` and `MapLlmTestServer()`. |

The [v1.0.0 release](https://github.com/managedcode/LlmTestServer/releases/tag/v1.0.0)
contains all 19 packages accepted by NuGet. Install the packages in your consumer project:

```sh
dotnet add package ManagedCode.LlmTestServer.Aspire --version 1.0.0
dotnet add package ManagedCode.LlmTestServer.Client --version 1.0.0
```

For an existing ASP.NET Core application, install `ManagedCode.LlmTestServer.Hosting` from NuGet
and follow [ASP.NET Core startup](#aspnet-core-startup). The core package owns scenarios and model configuration;
provider packages own their wire contracts, and the client package owns management and typed client APIs.

### Standalone container and management API

Build a local image from the checkout with the .NET SDK's `PublishContainer` target, then run it with Docker:

```sh
dotnet publish samples/ManagedCode.LlmTestServer.Service/ManagedCode.LlmTestServer.Service.csproj \
  --configuration Release --runtime linux-arm64 --self-contained false \
  /t:PublishContainer -p:ContainerRuntimeIdentifiers=linux-arm64 \
  -p:ContainerImageTag=local -p:UseAppHost=false
docker run --rm --name llm-test-server -p 8080:8080 \
  -e LlmTestServer__RequiredBearerToken=local-test-key \
  managedcode/llm-test-server:local
```

Use `linux-x64` in both RID arguments on an amd64 machine. Open **http://localhost:8080/** for the dashboard.
The host port is explicitly mapped to container port 8080. Configure a model and response through the running
server, keeping the same test API key in the replacement configuration:

```sh
curl --fail-with-body http://localhost:8080/admin-api/configure \
  -H 'Authorization: Bearer local-test-key' -H 'Content-Type: application/json' \
  --data '{"requiredBearerToken":"local-test-key","models":[{"id":"gpt-4.1-mini","kind":"chat"}],"chatScenarios":[{"id":"hello","modelId":"gpt-4.1-mini","responses":[{"content":"Hello from your fixture"}]}]}'

curl --fail-with-body http://localhost:8080/openai/v1/chat/completions \
  -H 'Authorization: Bearer local-test-key' -H 'Content-Type: application/json' \
  --data '{"model":"gpt-4.1-mini","messages":[{"role":"user","content":"Hello"}]}'
```

The API key is a local fixture credential; production provider credentials are unnecessary. Provider routes
and `/admin-api/*` use it. The intended public release image is
`ghcr.io/managedcode/llm-test-server:1.0.0`; the multi-platform image has been pushed, but public pulling remains pending GHCR visibility
configuration and anonymous-pull verification.
See [Docker distribution](../Features/DockerDistribution.md) for both architectures, authentication,
health checks and release requirements.

## ASP.NET Core Startup

The simplest host config is one chat scenario:

```csharp
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLlmTestServer(options =>
{
    options.AddChatScenario(
        "blue-whale",
        scenario => scenario
            .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
            .WhenUserContains("largest animal")
            .Responds("blue whale", "blue ", "whale"));
});

var app = builder.Build();
app.MapLlmTestServer();
app.Run();
```

A fuller config can advertise your application's real model names while still serving deterministic fixtures:

```csharp
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLlmTestServer(options =>
{
    options.RequireBearerToken("test-key");

    options.AddDefaultOpenAiModels();

    options.WithDefaultEmbeddingVector(0.125f, 0.25f, 0.5f, 1.0f);
    options.WithDefaultAudio(File.ReadAllBytes("fixtures/ok.wav"), "audio/wav");

    options.AddChatScenario(
        "invoice-total",
        scenario => scenario
            .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
            .WhenUserContains("invoice total")
            .Responds(
                "{\"total\":42.50}",
                "{\"total\":",
                "42.50",
                "}"));

    options.AddChatScenario(
        "provider-rate-limit",
        scenario => scenario
            .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
            .WhenUserContains("rate limit")
            .Fails(429, "rate_limit_exceeded", "Scripted rate limit from LLM Test Server."));

    options.AddChatScenario(
        "slow-response",
        scenario => scenario
            .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
            .WhenUserContains("slow")
            .Responds("done")
            .DelaysBy(250));
});

var app = builder.Build();
app.MapLlmTestServer();
app.Run();
```

## Aspire AppHost

Install `ManagedCode.LlmTestServer.Aspire` in the AppHost using the versioned
[NuGet installation commands](#choose-how-to-run) above.

Then add the package-owned Test Server resource directly:

```csharp
using ManagedCode.LlmTestServer.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

var llmTestServer = builder
    .AddLlmTestServer()
    .WithApiKey("test-key");

builder
    .AddProject<Projects.Consumer>("consumer")
    .WithReference(llmTestServer)
    .WithEnvironment("LLM_PROVIDER_ENDPOINT", llmTestServer.GetHttpEndpoint())
    .WithEnvironment("OPENAI_ENDPOINT", llmTestServer.GetOpenAiEndpoint())
    .WithEnvironment("OPENAI_API_KEY", "test-key")
    .WaitFor(llmTestServer);

builder.Build().Run();
```

`AddLlmTestServer()` creates a `LlmTestServerResource` backed by the packaged .NET LLM Test Server service executable and exposes its `http` endpoint. It does not require a consumer service project reference, generated `Projects.*` metadata type, project path, Docker, or a container runtime. Consumer resources should reference the Test Server resource, wait for it, and use `llmTestServer.GetHttpEndpoint()` when they need the provider-compatible base URL. `.WithApiKey("test-key")` sets `LlmTestServer:RequiredBearerToken` so both provider endpoints and `/admin-api/*` control endpoints require the same bearer token.

Use `AddLlmTestServerContainer()` only when you explicitly want a container-backed resource, for example for a deployment or container-runtime smoke test. The container mode uses a `ghcr.io/managedcode/llm-test-server` image tagged with the same release version as the installed `ManagedCode.LlmTestServer.Aspire` package; it is not the default local Aspire path.

## Build And Test

```bash
dotnet restore ManagedCode.LlmTestServer.slnx
dotnet build ManagedCode.LlmTestServer.slnx --configuration Release --no-restore
dotnet test --project tests/ManagedCode.LlmTestServer.Tests/ManagedCode.LlmTestServer.Tests.csproj --configuration Release --no-build --verbosity normal
for project in src/*/*.csproj; do dotnet pack "$project" --configuration Release --no-build --output artifacts/packages; done
```

`global.json` opts `dotnet test` into `Microsoft.Testing.Platform` for .NET 10.

After restore/build, run `bash scripts/coverage.sh` for the production 90% coverage gate. After packing, run `python3 scripts/verify-package.py` for an isolated NuGet consumer, test-owned Aspire host and Chromium dashboard test; this requires Python 3 and PowerShell (`pwsh`). CI installs Chromium dependencies and saves coverage/browser evidence. Release delivery is marked complete only after NuGet push and GitHub artifact attachment succeed.
