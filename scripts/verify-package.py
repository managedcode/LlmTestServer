#!/usr/bin/env python3
"""Run a real NuGet consumer with no project references and an isolated package cache."""
import os
import base64
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import shutil
import sys
import xml.etree.ElementTree as ET
import argparse

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--public-feed", action="store_true", help="Restore exclusively from public NuGet, after publication.")
parser.add_argument("--refresh-screenshots", action="store_true", help="Update checked-in dashboard images after browser verification.")
parser.add_argument("--packages", type=Path, help="Local package directory; defaults to artifacts/packages.")
args = parser.parse_args()

root = Path(__file__).resolve().parent.parent
packages = (args.packages or root / "artifacts/packages").resolve()
versions = {n.attrib["Include"]: n.attrib["Version"] for n in ET.parse(root / "Directory.Packages.props").iter("PackageVersion")}
version = ET.parse(root / "Directory.Build.props").findtext(".//Version")
evidence = root / "artifacts/package-consumer"
evidence.mkdir(parents=True, exist_ok=True)
consumer = Path(tempfile.mkdtemp(prefix="consumer-", dir=evidence)).resolve()
package_cache = Path(tempfile.mkdtemp(prefix="cache-", dir=evidence)).resolve()
print(f"Consumer: {consumer}", flush=True)
project = (root / "scripts/package-consumer/Consumer.csproj.template").read_text()
for key, value in {"__VERSION__": version, "__ASPIRE_VERSION__": versions["Aspire.Hosting.Testing"], "__TUNIT_VERSION__": versions["TUnit"], "__PLAYWRIGHT_VERSION__": versions["Microsoft.Playwright"], "__OPENAI_VERSION__": versions["OpenAI"]}.items():
    project = project.replace(key, value)
(consumer / "Consumer.csproj").write_text(project)
(consumer / "PackageTests.cs").write_text((root / "scripts/package-consumer/PackageTests.cs").read_text())
(consumer / "global.json").write_text((root / "global.json").read_text())
# Keep evidence repository-owned without inheriting the source repository's CPM,
# analyzers or project references into this otherwise independent consumer.
for name in ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"]:
    (consumer / name).write_text("<Project />\n")
env = dict(os.environ, NUGET_PACKAGES=str(package_cache))
def run(*args):
    subprocess.run(args, cwd=consumer, env=env, check=True)
sources = [] if args.public_feed else ["--source", str(packages)]
run("dotnet", "restore", "Consumer.csproj", *sources, "--source", "https://api.nuget.org/v3/index.json")
metadata = json.loads((package_cache / "managedcode.llmtestserver.aspire" / version / ".nupkg.metadata").read_text())
expected_hash = base64.b64encode(hashlib.sha512((packages / f"ManagedCode.LlmTestServer.Aspire.{version}.nupkg").read_bytes()).digest()).decode()
if metadata.get("contentHash") != expected_hash:
    raise RuntimeError("Restored Aspire package differs from the local artifact being verified.")
print("Verified restored package SHA-512 matches the local nupkg.", flush=True)
run("dotnet", "build", "Consumer.csproj", "--configuration", "Release", "--no-restore")
run("pwsh", "bin/Release/net10.0/playwright.ps1", "install", "chromium", *(["--with-deps"] if sys.platform.startswith("linux") else []))
try:
    run("dotnet", "test", "--project", "Consumer.csproj", "--configuration", "Release", "--no-build")
finally:
    for image in consumer.rglob("package-dashboard*.*"):
        if image.suffix in {".png", ".jpg"}:
            shutil.copy2(image, evidence / image.name)
    if (consumer / "TestResults").exists():
        shutil.copytree(consumer / "TestResults", evidence / "TestResults", dirs_exist_ok=True)
if args.refresh_screenshots:
    for name in ["overview.jpg", "conversation.jpg", "runtime.jpg", "mobile-request-detail.png", "token-usage.png"]:
        shutil.copy2(evidence / f"package-dashboard-{name}", root / "docs/images" / f"admin-dashboard-{name}")
    shutil.copy2(evidence / "package-dashboard-overview.jpg", root / "site/assets/admin-dashboard-overview.jpg")
