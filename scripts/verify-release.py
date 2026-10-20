#!/usr/bin/env python3
"""Verify a real GitHub release and every published NuGet package; never simulate publishing."""
import hashlib
import json
from pathlib import Path
import subprocess
import time
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent.parent
version = ET.parse(root / "Directory.Build.props").findtext(".//Version")
package_ids = sorted(ET.parse(project).findtext(".//PackageId") for project in (root / "src").glob("*/*.csproj"))
release = json.loads(subprocess.check_output(
    ["gh", "release", "view", "v" + version, "--json", "tagName,isDraft,assets,url"], cwd=root, text=True))
if release["isDraft"] or release["tagName"] != "v" + version:
    raise RuntimeError("The matching public GitHub release is not available.")
asset_names = {asset["name"] for asset in release["assets"]}
expected_assets = {f"{package}.{version}.nupkg" for package in package_ids}
if expected_assets - asset_names:
    raise RuntimeError("Missing GitHub release assets: " + ", ".join(sorted(expected_assets - asset_names)))
evidence = root / "artifacts/release-verification" / version
evidence.mkdir(parents=True, exist_ok=True)
for package in package_ids:
    filename = f"{package}.{version}.nupkg"
    url = f"https://api.nuget.org/v3-flatcontainer/{package.lower()}/{version}/{filename.lower()}"
    target = evidence / filename
    for attempt in range(20):
        result = subprocess.run(["curl", "--fail", "--silent", "--show-error", "--max-time", "30",
                                 "--output", str(target), url], capture_output=True, text=True)
        if result.returncode == 0:
            break
        if attempt == 19:
            raise RuntimeError(f"Published package remains unavailable: {package} {version}: {result.stderr}")
        print(f"Waiting for NuGet: {package} {version} ({attempt + 1}/20)", flush=True)
        time.sleep(15)
    local = root / "artifacts/packages" / filename
    if not local.exists() or hashlib.sha512(local.read_bytes()).digest() != hashlib.sha512(target.read_bytes()).digest():
        raise RuntimeError("Published package bytes differ from the release artifact: " + filename)
    print("Verified public package: " + filename, flush=True)
(evidence / "release.json").write_text(json.dumps(release, indent=2) + "\n")
print(f"Verified {len(package_ids)} published packages and GitHub release {release['url']}", flush=True)
