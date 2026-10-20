#!/usr/bin/env bash
set -euo pipefail
mkdir -p artifacts/coverage/raw
# CI normalizes PDB paths. Resolve them back to this checkout without disabling
# source filtering or the 90% gate (Coverlet console does not create this map).
dotnet msbuild tests/ManagedCode.LlmTestServer.Tests/ManagedCode.LlmTestServer.Tests.csproj \
  -nologo -target:InitializeSourceRootMappedPaths -getItem:SourceRoot \
  -p:Configuration=Release -p:ContinuousIntegrationBuild=true > artifacts/coverage/source-roots.json
python3 - <<'PYMAP'
import json
from pathlib import Path
roots = json.loads(Path("artifacts/coverage/source-roots.json").read_text())["Items"]["SourceRoot"]
project = Path("tests/ManagedCode.LlmTestServer.Tests/ManagedCode.LlmTestServer.Tests.csproj").resolve()
Path("artifacts/coverage/source-roots.map").write_text("".join(
    f"{project}|{root['Identity']}={root['MappedPath']}\n"
    for root in roots if root.get("MappedPath")
))
PYMAP
dotnet tool run coverlet tests/ManagedCode.LlmTestServer.Tests/bin/Release/net10.0/ManagedCode.LlmTestServer.Tests.dll \
  --target dotnet \
  --targetargs "test --project tests/ManagedCode.LlmTestServer.Tests/ManagedCode.LlmTestServer.Tests.csproj --configuration Release --no-build --verbosity normal" \
  --source-mapping-file artifacts/coverage/source-roots.map \
  --format cobertura --format json --output artifacts/coverage/raw/coverage \
  --include '[ManagedCode.LlmTestServer]*' --include '[ManagedCode.LlmTestServer.*]*' \
  --exclude '[ManagedCode.LlmTestServer.Tests]*' --exclude '[ManagedCode.LlmTestServer.Service]*' --exclude '[ManagedCode.LlmTestServer.AppHost]*' \
  --threshold 90 --threshold-type line --threshold-stat Total
dotnet tool run reportgenerator -reports:artifacts/coverage/raw/coverage.cobertura.xml \
  -targetdir:artifacts/coverage/report '-reporttypes:HtmlSummary;TextSummary'
