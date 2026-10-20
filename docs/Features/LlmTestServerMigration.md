# Migrating from LlmTck to LLM Test Server

The package family is now `ManagedCode.LlmTestServer.*`. This is a source migration: replace package references,
namespaces and public type names together. The same package boundaries and provider routes are retained.
The coordinated release version is selected in `Directory.Build.props` when the approved batch is ready.

## Package mapping

| Previous package | Replacement |
| --- | --- |
| `ManagedCode.LlmTck` | `ManagedCode.LlmTestServer` |
| `ManagedCode.LlmTck.Hosting` | `ManagedCode.LlmTestServer.Hosting` |
| `ManagedCode.LlmTck.Client` | `ManagedCode.LlmTestServer.Client` |
| `ManagedCode.LlmTck.Aspire` | `ManagedCode.LlmTestServer.Aspire` |
| `ManagedCode.LlmTck.OpenAI` | `ManagedCode.LlmTestServer.OpenAI` |
| `ManagedCode.LlmTck.AzureOpenAI` | `ManagedCode.LlmTestServer.AzureOpenAI` |
| `ManagedCode.LlmTck.Foundry` | `ManagedCode.LlmTestServer.Foundry` |
| `ManagedCode.LlmTck.Anthropic` | `ManagedCode.LlmTestServer.Anthropic` |
| `ManagedCode.LlmTck.Gemini` | `ManagedCode.LlmTestServer.Gemini` |
| `ManagedCode.LlmTck.Groq` | `ManagedCode.LlmTestServer.Groq` |
| `ManagedCode.LlmTck.Mistral` | `ManagedCode.LlmTestServer.Mistral` |
| `ManagedCode.LlmTck.Ollama` | `ManagedCode.LlmTestServer.Ollama` |
| `ManagedCode.LlmTck.Cohere` | `ManagedCode.LlmTestServer.Cohere` |
| `ManagedCode.LlmTck.Bedrock` | `ManagedCode.LlmTestServer.Bedrock` |
| `ManagedCode.LlmTck.OpenRouter` | `ManagedCode.LlmTestServer.OpenRouter` |
| `ManagedCode.LlmTck.DeepSeek` | `ManagedCode.LlmTestServer.DeepSeek` |
| `ManagedCode.LlmTck.Perplexity` | `ManagedCode.LlmTestServer.Perplexity` |
| `ManagedCode.LlmTck.SystemOne` | `ManagedCode.LlmTestServer.SystemOne` |
| `ManagedCode.LlmTck.Cloudflare` | `ManagedCode.LlmTestServer.Cloudflare` |

Replace the `LlmTck` prefix in types and extension methods with `LlmTestServer`:

```csharp
using ManagedCode.LlmTestServer.Aspire;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Hosting;

builder.AddLlmTestServer();                  // Aspire, packaged .NET service
builder.AddLlmTestServerContainer();         // Explicit container option
services.AddLlmTestServer(options => options.AddDefaultOpenAiModels());
app.MapLlmTestServer();

var control = new LlmTestServerClient(httpClient, bearerToken);
```

These snippets illustrate separate Aspire and ASP.NET Core entry points; use the appropriate builder for each.
`ILlmTckRuntime` becomes `ILlmTestServerRuntime`, `LlmTckResource` becomes `LlmTestServerResource`, and builders,
model IDs, provider-operation constants and compatibility-tag constants follow the same prefix mapping.
There are no binary-compatibility forwarding assemblies or dependency-only facade packages.

## Configuration and protocols

The packaged service reads `LlmTestServer:RequiredBearerToken`, or `LlmTestServer__RequiredBearerToken` as an
environment variable. `LlmTck:RequiredBearerToken` remains a fallback during migration. The new key wins when
both are present, including an explicitly empty new value. `WithApiKey(...)` writes only the new key.
Custom ASP.NET Core hosts can use `LlmTestServerHostingConfiguration.GetRequiredBearerToken(configuration)`.

The dashboard remains at `/`; control APIs remain at `/admin-api/*`. Provider endpoints keep their explicit
namespaces, for example `/openai/v1/chat/completions`, `/anthropic/v1/messages` and `/azure-openai/...`.
The existing `llm_tck_*` diagnostic error codes and configured video fixture identities are retained, so existing
protocol assertions remain meaningful. Generated response/request IDs are opaque; their branding prefix can change.

The new container identity is `ghcr.io/managedcode/llm-test-server`. Aspire still starts the packaged .NET service
without Docker by default; its DLL, packaged directory and build-transitive targets use the new identity.
Resolve URLs with `GetEndpoint("http")` or `CreateHttpClient(...)` from the running Aspire resource.

## Repository and installed-package verification

The intended canonical repository is `https://github.com/managedcode/LlmTestServer`. Update the remote after the
repository rename. Published old package versions stay available; replacement/deprecation metadata is applied
only after every new package is published and verified. See the [implementation ledger](LlmTestServerMigrationPlan.md)
for actual delivery status rather than assuming the migration has already been released.

```bash
git remote set-url origin https://github.com/managedcode/LlmTestServer.git
dotnet restore ManagedCode.LlmTestServer.slnx
dotnet build ManagedCode.LlmTestServer.slnx --configuration Release --no-restore
dotnet test --project tests/ManagedCode.LlmTestServer.Tests/ManagedCode.LlmTestServer.Tests.csproj --configuration Release --no-build --verbosity normal
python3 scripts/verify-package.py
```

The package verifier restores a consumer with an independent NuGet cache, starts its installed Aspire service,
configures it through the installed control client, calls the official OpenAI SDK, verifies usage and native
decision routes, and exercises the interactive dashboard in Chromium. After publication run
`python3 scripts/verify-package.py --public-feed` to verify the same package bytes from public NuGet.
Pass `--refresh-screenshots` only when deliberately updating the checked-in dashboard images after UI changes.
