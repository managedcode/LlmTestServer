#!/usr/bin/env python3
"""Verify GitHub release assets against local packages without waiting for NuGet indexing."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--existing-assets", action="store_true", help="Before publication, verify any existing matching assets and allow an incomplete or absent release.")
parser.add_argument("--allow-draft", action="store_true", help="Verify complete draft assets before final publication.")
args = parser.parse_args()
root = Path(__file__).resolve().parent.parent
version = ET.parse(root / "Directory.Build.props").findtext(".//Version")
package_ids = [ET.parse(project).findtext(".//PackageId") for project in (root / "src").glob("*/*.csproj")]
if not version or not package_ids or any(not package for package in package_ids):
    raise RuntimeError("The release version and every project PackageId are required.")
package_ids.sort()
view = subprocess.run(
    ["gh", "release", "view", "v" + version, "--json", "tagName,isDraft,assets,url"], cwd=root, capture_output=True, text=True)
if view.returncode:
    if args.existing_assets and "release not found" in view.stderr.lower():
        print("No existing GitHub release assets to verify.", flush=True)
        raise SystemExit(0)
    raise RuntimeError("Could not inspect the GitHub release: " + view.stderr)
release = json.loads(view.stdout)
if release["tagName"] != "v" + version or release["isDraft"] and not (args.existing_assets or args.allow_draft):
    raise RuntimeError("The matching public GitHub release is not available.")
assets = {asset["name"]: asset for asset in release["assets"]}
expected_assets = {f"{package}.{version}.nupkg" for package in package_ids}
if not args.existing_assets and expected_assets - assets.keys():
    raise RuntimeError("Missing GitHub release assets: " + ", ".join(sorted(expected_assets - assets.keys())))
local_paths = {filename: root / "artifacts/packages" / filename for filename in expected_assets}
missing = [filename for filename, path in local_paths.items() if not path.is_file()]
if missing:
    raise RuntimeError("Missing local release artifacts: " + ", ".join(sorted(missing)))
evidence = root / "artifacts/release-verification" / version
evidence.mkdir(parents=True, exist_ok=True)
checksums = {}
for filename, local in sorted(local_paths.items()):
    if args.existing_assets and filename not in assets:
        continue
    checksum = hashlib.sha256(local.read_bytes()).hexdigest()
    remote_digest = assets[filename].get("digest")
    if remote_digest and remote_digest.startswith("sha256:"):
        remote_checksum = remote_digest.removeprefix("sha256:")
    else:
        subprocess.run(["gh", "release", "download", "v" + version, "--pattern", filename,
                        "--dir", str(evidence), "--clobber"], cwd=root, check=True)
        remote_checksum = hashlib.sha256((evidence / filename).read_bytes()).hexdigest()
    if checksum != remote_checksum:
        raise RuntimeError("GitHub release package bytes differ from the selected CI artifact; increment the version before publishing: " + filename)
    checksums[filename] = "sha256:" + checksum
    print("Verified GitHub package: " + filename, flush=True)
if args.existing_assets:
    (evidence / "existing-packages-sha256.json").write_text(json.dumps(checksums, indent=2) + "\n")
    print(f"Verified {len(checksums)} existing package assets before publication.", flush=True)
else:
    (evidence / "packages-sha256.json").write_text(json.dumps(checksums, indent=2) + "\n")
    (evidence / "release.json").write_text(json.dumps(release, indent=2) + "\n")
    print(f"Verified {len(package_ids)} GitHub package assets and release {release['url']}; NuGet indexing is not awaited.", flush=True)
