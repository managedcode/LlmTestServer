#!/usr/bin/env python3
"""Publish each container version once, then verify the existing public image.

The GitHub token is used only for package metadata. Registry reads use public pull;
this script never logs in, pushes, retags, or changes package visibility.
"""
import json
import os
from pathlib import Path
import re
import urllib.error
import urllib.request


OWNER = "managedcode"
PACKAGE = "llm-test-server"


def tag_exists(tag, token):
    page = 1
    while True:
        request = urllib.request.Request(
            f"https://api.github.com/orgs/{OWNER}/packages/container/{PACKAGE}/versions?per_page=100&page={page}",
            headers={
                "Accept": "application/vnd.github+json",
                "Authorization": "Bearer " + token,
                "X-GitHub-Api-Version": "2022-11-28",
                "User-Agent": "ManagedCode-LlmTestServer-container-release",
            },
        )
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                versions = json.load(response)
        except urllib.error.HTTPError as error:
            if error.code == 404 and page == 1:
                print("Container package is not present; first publication is permitted.")
                return False
            raise RuntimeError(f"Container package metadata lookup failed with HTTP {error.code}; publication denied.") from error
        if not isinstance(versions, list):
            raise RuntimeError("Container package metadata was not a version list; publication denied.")
        for version in versions:
            tags = version["metadata"]["container"]["tags"]
            if not isinstance(tags, list):
                raise RuntimeError("Container version tags were not a list; publication denied.")
            if tag in tags:
                return True
        if len(versions) < 100:
            return False
        page += 1


def main():
    tag = os.environ["IMAGE_TAG"]
    revision = os.environ["RELEASE_COMMIT"]
    token = os.environ["GH_TOKEN"]
    if not token:
        raise RuntimeError("GH_TOKEN is required for container package metadata lookup.")
    if not re.fullmatch(r"[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}", tag):
        raise RuntimeError("IMAGE_TAG must be a valid explicit container tag.")
    if not re.fullmatch(r"[0-9a-fA-F]{40}", revision):
        raise RuntimeError("RELEASE_COMMIT must be the full tested commit SHA.")
    publish = not tag_exists(tag, token)
    if not publish:
        print(f"Container version {tag} is already published; preserve it and verify the public image.")
    else:
        print(f"Container version {tag} is not present; publication of tested commit {revision} is permitted.")
    with Path(os.environ["GITHUB_OUTPUT"]).open("a") as output:
        output.write(f"publish={str(publish).lower()}\n")


if __name__ == "__main__":
    main()
