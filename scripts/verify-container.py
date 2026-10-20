#!/usr/bin/env python3
"""Run a real Docker image and verify its standalone HTTP configuration/provider/dashboard flows."""
import argparse
import json
from pathlib import Path
import secrets
import subprocess
import time
import urllib.error
import urllib.request


def command(*arguments, **kwargs):
    return subprocess.check_output(arguments, text=True, **kwargs).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", required=True)
    parser.add_argument("--platform", choices=["linux/amd64", "linux/arm64"])
    parser.add_argument("--anonymous-pull", action="store_true")
    parser.add_argument("--require-platform", action="append", default=[])
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    evidence = root / "artifacts" / "container-verification" / secrets.token_hex(8)
    evidence.mkdir(parents=True)
    published_platforms = []
    if args.require_platform and not args.anonymous_pull:
        parser.error("--require-platform requires --anonymous-pull")
    if args.anonymous_pull:
        clean_config = evidence / "anonymous-docker-config"
        clean_config.mkdir()
        (clean_config / "config.json").write_text('{"auths":{}}\n')
        print(f"Pulling {args.image} with an empty Docker configuration", flush=True)
        pull_arguments = ["docker", "--config", str(clean_config), "pull"]
        if args.platform:
            pull_arguments += ["--platform", args.platform]
        command(*pull_arguments, args.image)
        if args.require_platform:
            manifest = json.loads(command("docker", "--config", str(clean_config), "manifest", "inspect", args.image))
            published_platforms = sorted({item["platform"]["os"] + "/" + item["platform"]["architecture"]
                                          for item in manifest.get("manifests", [])})
            missing = set(args.require_platform) - set(published_platforms)
            if missing:
                raise RuntimeError("Published image is missing platforms: " + ", ".join(sorted(missing)))
    inspect_arguments = ["docker", "image", "inspect"]
    if args.platform:
        inspect_arguments += ["--platform", args.platform]
    image = json.loads(command(*inspect_arguments, args.image))[0]
    if not image["Config"].get("User") or image["Config"]["User"] in ("root", "0"):
        raise RuntimeError("The distribution image must run as a non-root user.")
    token = secrets.token_urlsafe(24)
    container = command("docker", "run", "--detach", "--platform", image["Os"] + "/" + image["Architecture"], "--publish", "127.0.0.1::8080",
                        "--env", "LlmTestServer__RequiredBearerToken=" + token, args.image)
    results = []
    try:
        state = json.loads(command("docker", "inspect", container))[0]
        bindings = state["NetworkSettings"].get("Ports", {}).get("8080/tcp")
        if not bindings:
            raise RuntimeError(f"Container did not allocate its HTTP port; status={state['State']['Status']}, exitCode={state['State']['ExitCode']}")
        port = bindings[0]["HostPort"]
        base = "http://127.0.0.1:" + port

        def request(path, *, body=None, authorized=True, method=None):
            headers = {}
            if authorized:
                headers["Authorization"] = "Bearer " + token
            data = None if body is None else json.dumps(body).encode()
            if data is not None:
                headers["Content-Type"] = "application/json"
            req = urllib.request.Request(base + path, data=data, headers=headers, method=method)
            try:
                with urllib.request.urlopen(req, timeout=15) as response:
                    return response.status, response.read(), dict(response.headers)
            except urllib.error.HTTPError as error:
                return error.code, error.read(), dict(error.headers)

        deadline = time.monotonic() + 120
        while True:
            try:
                status, _, _ = request("/health", authorized=False)
                if status == 200:
                    break
            except (urllib.error.URLError, TimeoutError, ConnectionError):
                pass
            if time.monotonic() >= deadline:
                raise RuntimeError("Container failed to become healthy within 120 seconds.")
            if command("docker", "inspect", "--format", "{{.State.Running}}", container) != "true":
                raise RuntimeError("Container exited before becoming healthy.")
            time.sleep(1)
        assert request("/alive", authorized=False)[0] == 200
        results.append("health and liveness")
        status, dashboard, _ = request("/", authorized=False)
        assert status == 200 and b"LLM Test Server" in dashboard and b"blazor.web.js" in dashboard
        assert request("/_framework/blazor.web.js", authorized=False)[0] == 200
        results.append("dashboard HTML and Blazor assets")
        assert request("/admin-api/models", authorized=False)[0] == 401
        assert request("/openai/v1/models", authorized=False)[0] == 401
        results.append("management and provider authorization")
        configuration = {
            "requiredBearerToken": token,
            "models": [{"id": "gpt-4.1-mini", "kind": "chat"}],
            "chatScenarios": [{"id": "container-smoke", "modelId": "gpt-4.1-mini",
                               "responses": [{"content": "container reply", "streamChunks": ["container ", "reply"]},
                                             {"content": "stream reply", "streamChunks": ["stream ", "reply"]}]}],
        }
        assert request("/admin-api/configure", body=configuration)[0] == 200
        body = {"model": "gpt-4.1-mini", "messages": [{"role": "user", "content": "test"}]}
        assert request("/openai/v1/chat/completions", body=body, authorized=False)[0] == 401
        status, response, _ = request("/openai/v1/chat/completions", body=body)
        assert status == 200 and json.loads(response)["choices"][0]["message"]["content"] == "container reply"
        status, response, _ = request("/openai/v1/chat/completions", body=body | {"stream": True})
        assert status == 200 and b"data: [DONE]" in response and b"stream " in response
        results.append("management configure, provider JSON and SSE")
        invalid = body | {"model": "not-configured"}
        assert request("/openai/v1/chat/completions", body=invalid)[0] == 404
        assertions = json.loads(request("/admin-api/assertions")[1])
        assert assertions["matched"] == 2 and assertions["modelNotFound"] == 1
        results.append("unknown-model validation and real runtime assertions")
        assert request("/admin-api/reset", method="POST")[0] == 200
        assert json.loads(request("/admin-api/assertions")[1])["totalEvents"] == 0
        results.append("management reset")
        (evidence / "result.json").write_text(json.dumps({"image": args.image, "imageId": image["Id"],
            "repoDigests": image.get("RepoDigests", []), "architecture": image["Architecture"],
            "anonymousPull": args.anonymous_pull, "publishedPlatforms": published_platforms, "checks": results}, indent=2) + "\n")
        print("Container acceptance passed: " + "; ".join(results), flush=True)
        print("Evidence: " + str(evidence), flush=True)
    finally:
        logs = subprocess.run(["docker", "logs", container], text=True, capture_output=True)
        (evidence / "container.log").write_text((logs.stdout + logs.stderr).replace(token, "[redacted]"))
        subprocess.run(["docker", "rm", "--force", container], check=True, stdout=subprocess.DEVNULL)


if __name__ == "__main__":
    main()
