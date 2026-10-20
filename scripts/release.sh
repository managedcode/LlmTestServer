#!/usr/bin/env bash
set -euo pipefail
case "${1:?Expected resolve or publish}" in
  resolve)
    version=$(dotnet msbuild src/ManagedCode.LlmTestServer/ManagedCode.LlmTestServer.csproj -nologo -v:q -getProperty:Version)
    tag="v$version"
    echo "version=$version" >> "$GITHUB_OUTPUT"
    echo "tag=$tag" >> "$GITHUB_OUTPUT"
    if gh release view "$tag" --json assets --jq '.assets[].name' 2>/dev/null | grep -qx 'nuget-published.txt' && [ "${FORCE_RELEASE:-false}" != true ]; then
      echo 'should_release=false' >> "$GITHUB_OUTPUT"
    else
      echo 'should_release=true' >> "$GITHUB_OUTPUT"
    fi
    ;;
  publish)
    test -n "${NUGET_API_KEY:-}" || { echo 'NUGET_API_KEY is required.' >&2; exit 1; }
    mkdir -p artifacts/packages
    python3 - "${TAG_NAME:?TAG_NAME is required}" > artifacts/packages/release-manifest.txt <<'PY'
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
version = sys.argv[1].removeprefix("v")
paths = [Path("artifacts/packages") / f"{ET.parse(project).findtext('.//PackageId')}.{version}.nupkg"
         for project in sorted(Path("src").glob("*/*.csproj"))]
missing = [str(path) for path in paths if not path.is_file()]
if missing:
    raise SystemExit("Missing release artifacts: " + ", ".join(missing))
print("\n".join(map(str, paths)))
PY
    packages=()
    while IFS= read -r package; do packages+=("$package"); done < artifacts/packages/release-manifest.txt
    for package in "${packages[@]}"; do
      dotnet nuget push "$package" --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY" --skip-duplicate
    done
    if gh release view "$TAG_NAME" > /dev/null 2>&1; then
      gh release upload "$TAG_NAME" "${packages[@]}" --clobber
    else
      gh release create "$TAG_NAME" "${packages[@]}" --title "$TAG_NAME" --generate-notes --target "$GITHUB_SHA"
    fi
    ;;
  finalize)
    # Only the verified public-feed consumer completes delivery. A failed verification
    # leaves this marker absent, so a rerun resumes the same version's publication.
    printf '%s\n' "$TAG_NAME" > artifacts/packages/nuget-published.txt
    gh release upload "$TAG_NAME" artifacts/packages/nuget-published.txt --clobber
    ;;
  *) echo 'Expected resolve, publish or finalize' >&2; exit 2 ;;
esac
