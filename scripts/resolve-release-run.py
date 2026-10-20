"""Resolve artifacts from a successful Build and Test run for current main."""
import json
import os
from pathlib import Path
import subprocess


def github(*args):
    return json.loads(subprocess.check_output(["gh", *args], text=True))


repository = os.environ["GITHUB_REPOSITORY"]
event_name = os.environ["GITHUB_EVENT_NAME"]
event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
main_sha = github("api", f"repos/{repository}/git/ref/heads/main")["object"]["sha"]
if event_name == "workflow_run":
    run = event["workflow_run"]
    permitted = (run["conclusion"] == "success" and run["event"] in ("push", "workflow_dispatch")
                 and run["head_branch"] == "main" and run["head_repository"]["full_name"] == repository
                 and run["name"] == "Build and Test" and run["head_sha"] == main_sha)
    if not permitted:
        with open(os.environ["GITHUB_OUTPUT"], "a") as output:
            output.write("ready=false\n")
        print("No publication: this is not the successful validation of current main.")
        raise SystemExit(0)
elif event_name == "workflow_dispatch":
    if os.environ["GITHUB_REF"] != "refs/heads/main":
        raise SystemExit("Publication can only be dispatched on main.")
    runs = github("api", f"repos/{repository}/actions/workflows/ci.yml/runs?branch=main&status=success&per_page=100")["workflow_runs"]
    run = next((item for item in runs if item["head_sha"] == main_sha
                and item["event"] in ("push", "workflow_dispatch")), None)
    if run is None:
        raise SystemExit("Run Build and Test successfully for current main before dispatching publication.")
else:
    raise SystemExit("Unsupported publication trigger.")

with open(os.environ["GITHUB_OUTPUT"], "a") as output:
    output.write(f"ready=true\nsha={run['head_sha']}\nrun_id={run['id']}\n")
print(f"Using successful Build and Test run {run['id']} for {run['head_sha']}")
