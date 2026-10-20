#!/usr/bin/env bash
set -euo pipefail
case "${1:?Expected resolve, publish or finalize}" in
  resolve)
    version=$(dotnet msbuild src/ManagedCode.LlmTestServer/ManagedCode.LlmTestServer.csproj -nologo -v:q -getProperty:Version)
    tag="v$version"
    echo "version=$version" >> "$GITHUB_OUTPUT"
    echo "tag=$tag" >> "$GITHUB_OUTPUT"
    should_release=true
    if [ "${FORCE_RELEASE:-false}" != true ] && release=$(gh release view "$tag" --json assets,isDraft 2>/dev/null); then
      if python3 - "$version" "$release" <<'PYTHON'
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
version, payload = sys.argv[1:]
release = json.loads(payload)
package_ids = [ET.parse(project).findtext(".//PackageId") for project in sorted(Path("src").glob("*/*.csproj"))]
if not package_ids or any(not package for package in package_ids):
    raise SystemExit("Every release project must declare a PackageId.")
expected = {f"{package}.{version}.nupkg" for package in package_ids} | {"nuget-published.txt"}
actual = {asset["name"] for asset in release["assets"]}
raise SystemExit(0 if not release["isDraft"] and expected <= actual else 1)
PYTHON
      then
        should_release=false
      fi
    fi
    echo "should_release=$should_release" >> "$GITHUB_OUTPUT"
    ;;
  publish)
    test -n "${NUGET_API_KEY:-}" || { echo 'NUGET_API_KEY is required.' >&2; exit 1; }
    release_commit="${RELEASE_COMMIT:-${GITHUB_SHA:?RELEASE_COMMIT or GITHUB_SHA is required}}"
    [[ "$release_commit" =~ ^[0-9a-fA-F]{40}$ ]] || { echo 'Release commit must be the full tested commit SHA.' >&2; exit 1; }
    mkdir -p artifacts/packages
    python3 - "${TAG_NAME:?TAG_NAME is required}" > artifacts/packages/release-manifest.txt <<'PY'
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
version = sys.argv[1].removeprefix("v")
package_ids = [ET.parse(project).findtext(".//PackageId") for project in sorted(Path("src").glob("*/*.csproj"))]
if not package_ids or any(not package for package in package_ids):
    raise SystemExit("Every release project must declare a PackageId.")
paths = [Path("artifacts/packages") / f"{package}.{version}.nupkg" for package in package_ids]
missing = [str(path) for path in paths if not path.is_file()]
if missing:
    raise SystemExit("Missing release artifacts: " + ", ".join(missing))
print("\n".join(map(str, paths)))
PY
    packages=()
    while IFS= read -r package; do packages+=("$package"); done < artifacts/packages/release-manifest.txt
    if git show-ref --verify --quiet "refs/tags/$TAG_NAME"; then
      tag_commit=$(git rev-parse --verify "refs/tags/$TAG_NAME^{commit}")
      [ "$tag_commit" = "$release_commit" ] || { echo "Version tag $TAG_NAME belongs to another commit; increment the version before publishing different code." >&2; exit 1; }
    fi
    python3 scripts/verify-release.py --existing-assets
    existing_release=false
    if release=$(gh release view "$TAG_NAME" --json assets,isDraft,targetCommitish 2>/dev/null); then
      existing_release=true
      python3 - "$release" "$release_commit" "${packages[@]}" > artifacts/packages/missing-release-assets.txt <<'PYTHON'
import json
from pathlib import Path
import sys
release = json.loads(sys.argv[1])
if release["isDraft"] and release["targetCommitish"] != sys.argv[2]:
    raise SystemExit("Existing draft targets another commit; increment the version before publishing different code.")
existing = {asset["name"] for asset in release["assets"]}
for package in sys.argv[3:]:
    if Path(package).name not in existing:
        print(package)
PYTHON
    fi
    for package in "${packages[@]}"; do
      dotnet nuget push "$package" --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY" --skip-duplicate
    done
    if [ "$existing_release" = true ]; then
      while IFS= read -r package; do
        gh release upload "$TAG_NAME" "$package"
      done < artifacts/packages/missing-release-assets.txt
    else
      gh release create "$TAG_NAME" "${packages[@]}" --draft --title "$TAG_NAME" --generate-notes --target "$release_commit"
    fi
    ;;
  finalize)
    # Complete the receipt after every NuGet push was accepted and the GitHub assets
    # were verified. Public NuGet indexing is asynchronous and is not a release gate.
    test -f "artifacts/release-verification/${TAG_NAME#v}/release.json" || { echo 'GitHub release verification is required before finalization.' >&2; exit 1; }
    release=$(gh release view "$TAG_NAME" --json assets,isDraft)
    if ! python3 - "$release" <<'PYTHON'
import json
import sys
raise SystemExit(0 if any(asset["name"] == "nuget-published.txt" for asset in json.loads(sys.argv[1])["assets"]) else 1)
PYTHON
    then
      printf '%s\n' "$TAG_NAME" > artifacts/packages/nuget-published.txt
      gh release upload "$TAG_NAME" artifacts/packages/nuget-published.txt
    fi
    if [ "$(python3 -c 'import json, sys; print(str(json.loads(sys.argv[1])["isDraft"]).lower())' "$release")" = true ]; then
      gh release edit "$TAG_NAME" --draft=false
    fi
    ;;
  *) echo 'Expected resolve, publish or finalize' >&2; exit 2 ;;
esac
