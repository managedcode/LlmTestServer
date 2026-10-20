# Standalone Docker distribution

Status: Distribution code and verification are being prepared for the complete v1.0.0 release. Public GHCR
availability must be established by the release workflow; these commands do not assert that a release is already published.
Canonical image: `ghcr.io/managedcode/llm-test-server`.

The image runs the complete HTTP backend: provider routes, authenticated management API, Blazor dashboard,
health and liveness. It includes the published service and browser assets; it does not require a .NET SDK or
NuGet restore on the consuming machine. The release builds Linux amd64 and arm64 images. The process runs
as the .NET runtime image's non-root app user and listens on container port 8080.

## Run and configure

After the verified v1.0.0 release is public:

```sh
docker pull ghcr.io/managedcode/llm-test-server:1.0.0
docker run --rm --name llm-test-server \
  -p 8080:8080 \
  -e LlmTestServer__RequiredBearerToken=local-test-key \
  ghcr.io/managedcode/llm-test-server:1.0.0
```

Open `http://localhost:8080/` for the dashboard. `GET /health` and `GET /alive` expose readiness/liveness.
The configured bearer key protects provider and `/admin-api/*` routes. The dashboard page is available at `/`;
provider URLs remain namespaced, such as `/openai/v1` and `/anthropic/v1`.

Configure deterministic fixtures through the same management API as the .NET client:

```sh
curl --fail-with-body http://localhost:8080/admin-api/configure \
  -H 'Authorization: Bearer local-test-key' \
  -H 'Content-Type: application/json' \
  --data '{
    "requiredBearerToken": "local-test-key",
    "models": [{"id": "gpt-4.1-mini", "kind": "chat"}],
    "chatScenarios": [{
      "id": "greeting",
      "modelId": "gpt-4.1-mini",
      "responses": [{"content": "Hello from LLM Test Server", "streamChunks": ["Hello from ", "LLM Test Server"]}]
    }]
  }'

curl --fail-with-body http://localhost:8080/openai/v1/chat/completions \
  -H 'Authorization: Bearer local-test-key' \
  -H 'Content-Type: application/json' \
  --data '{"model":"gpt-4.1-mini","messages":[{"role":"user","content":"Hello"}]}'
```

Configure replaces the selected runtime configuration. Include the bearer requirement in the replacement when
retaining authentication. Test sessions provide independent configurations inside one container;
[the session API](TestSessions.md) and [typed client](RequestVerificationAndDeterminism.md) use the same HTTP
transport. Runtime state is in memory; container replacement starts from the built-in startup configuration.
Map a different host port as needed. Prefer a version tag or verified digest for reproducible consumers.

## Build and exercise locally

From the repository root:

```sh
dotnet publish samples/ManagedCode.LlmTestServer.Service/ManagedCode.LlmTestServer.Service.csproj \
  --configuration Release --runtime linux-arm64 --self-contained false \
  /t:PublishContainer -p:ContainerRuntimeIdentifier=linux-arm64 \
  -p:ContainerImageTag=local -p:UseAppHost=false
python3 scripts/verify-container.py --image managedcode/llm-test-server:local
```

Use `linux-x64` in both RID arguments on an amd64 machine. The .NET SDK builds the image from published
service output using its built-in `PublishContainer` target. The service project declares the canonical
repository, ASP.NET 10 base image, port/environment, OCI labels, non-root user and both supported runtime IDs.
It does not require a separate container-builder toolchain. Docker is used only to load/run/inspect/pull images.
Published runtime output includes the dashboard assets; source, repository metadata and marketing files are not
added as a build context. Provider credentials are supplied only at container startup/configuration.

To generate the multi-platform OCI image index locally, omit the single RID arguments:

```sh
dotnet publish samples/ManagedCode.LlmTestServer.Service/ManagedCode.LlmTestServer.Service.csproj \
  --configuration Release --self-contained false /t:PublishContainer \
  -p:ContainerImageTag=local-multi -p:UseAppHost=false
```

The project's `RuntimeIdentifiers` and `ContainerRuntimeIdentifiers` both specify `linux-x64;linux-arm64`.
The SDK publishes each RID and combines them into one OCI index. An optional
`-p:ContainerArchiveOutputPath=/absolute/repository/artifacts/container-sdk/image.tar.gz` exports an archive
instead of loading a daemon; `docker load --input ...` can import it. See Microsoft's
[SDK publish guide](https://learn.microsoft.com/en-us/dotnet/core/containers/sdk-publish) and
[container configuration reference](https://learn.microsoft.com/en-us/dotnet/core/containers/publish-configuration).

The verifier launches a real container on an allocated loopback host port, waits for health, loads dashboard
HTML and Blazor assets, tests missing-credential rejection, configures fixtures through HTTP, verifies JSON/SSE,
unknown-model validation, actual assertion counters and reset. It checks the image uses a non-root account.
It stops only the container it created and stores sanitized logs/evidence under `artifacts/container-verification`.
This is API/container verification; it does not establish interactive browser qualification.
On 2026-10-20 the installed .NET SDK 10.0.401 generated and loaded a combined Linux amd64/arm64 OCI index
with `PublishContainer`; both architecture variants passed the listed HTTP checks (amd64 through Docker Desktop
emulation). SDK publish logs are under `artifacts/container-sdk`; runtime evidence is under
`artifacts/container-verification`. Public pull and registry publication remain pending owner-merged release.

## CI, publication and public access

PR CI builds and runs the image without pushing it. The release workflow is restricted to `main`, follows the
owner's merge, and uses the canonical package version. Its order is existing quality/package gates → local
SDK container HTTP acceptance → version-tagged multi-platform SDK publish → anonymous pull and HTTP acceptance → NuGet
publication and public-consumer verification → delivery marker. Publication does not run from this branch.
A failed gate keeps delivery incomplete; retry the same version rather than creating an intermediate release.

The release job grants `packages: write`, authenticates with `GITHUB_TOKEN`, and calls `dotnet publish
/t:PublishContainer -p:ContainerRegistry=ghcr.io` with both configured RIDs. It links the image to its source
repository with the OCI source label. **Those settings do not make a new GHCR package public.** GitHub documents
new container packages as private by default. A package administrator must open the managedcode organization's
`llm-test-server` container package, choose **Package settings → Change visibility → Public**, and confirm the
change. Organization policies may restrict that permission. After the first push creates the package, change
visibility and rerun the same release if its anonymous check failed.

The supported visibility setup is documented by GitHub's
[container registry guide](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry)
and [package access/visibility guide](https://docs.github.com/en/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility).
The workflow does not invoke an undocumented visibility mutation API.

The public-access gate runs `verify-container.py --anonymous-pull` with a newly created Docker configuration
containing no registry credentials. It must pull the published version and pass the same HTTP checks.
The published manifest must also include Linux amd64 and arm64. A push succeeding under the workflow token is insufficient. Public delivery remains blocked by missing GHCR
permissions, organization visibility policy, unavailable registry access or failed anonymous acceptance until
that specific condition is resolved. No public-availability claim should precede this gate.
