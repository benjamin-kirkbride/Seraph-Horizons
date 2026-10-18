#!/usr/bin/env python3
"""Watch a PR in the Mergify merge queue until it merges or stops moving.

Stdlib only (Python 3.11+); needs an authenticated `gh`. Usage:

  python3 tools/prwatch.py <pr> [--delay 45] [--interval 30] [--timeout 10800] [--repo OWNER/NAME]

Polls `gh pr view <pr> --json state,mergeable,labels,statusCheckRollup,comments` and prints
one line when the PR stops moving (docs/merge-queue.md):

  STATE <MERGED|CLOSED>   the PR is no longer open
  FAILED [<check>, ...]   a check failed, was cancelled or timed out (the latest run of each name)
  CONFLICT                main moved and the PR now conflicts with it
  DEQUEUED                Mergify took it out of the queue (its `dequeued` label, without `queued`)
  NOTQUEUED <why>         no `@mergifyio queue` comment, Mergify cancelled or removed the request,
                          or Mergify has not answered the last one after 180 s
  timeout                 none of these before --timeout

It exits 0 only for STATE MERGED. Wait --delay seconds first after a push, so the new run's
checks have registered. A failing `gh` call is retried at the next poll. `gh` runs with this
process's environment, so XDG_CACHE_HOME and GH_* apply to it.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FIELDS = "state,mergeable,labels,statusCheckRollup,comments"
MERGIFY = "mergify"
FAILED_CONCLUSIONS = {"FAILURE", "CANCELLED", "TIMED_OUT", "STARTUP_FAILURE", "ERROR"}
QUEUE_COMMAND = re.compile(r"@mergifyio\s+(?:re)?queue\b", re.IGNORECASE)
REPLY_GRACE = 180  # seconds Mergify gets to answer a queue command


def _time(stamp: str | None) -> dt.datetime:
    if not stamp:
        return dt.datetime.min.replace(tzinfo=dt.timezone.utc)
    return dt.datetime.fromisoformat(stamp.replace("Z", "+00:00"))


def latest_checks(rollup: list[dict]) -> dict[str, dict]:
    """The latest entry per check name. A rerun replaces a check run in GitHub's rollup today,
    but an older attempt must never decide the outcome if it is ever listed beside its rerun.
    Status contexts carry `context`/`state` where check runs carry `name`/`conclusion`."""
    latest: dict[str, dict] = {}
    for c in rollup:
        name = c.get("name") or c.get("context") or "?"
        if name not in latest or _time(c.get("startedAt")) >= _time(latest[name].get("startedAt")):
            latest[name] = c
    return latest


def failed_checks(rollup: list[dict]) -> list[str]:
    return [name for name, c in latest_checks(rollup).items()
            if (c.get("conclusion") or c.get("state") or "").upper() in FAILED_CONCLUSIONS]


def decide(pr: dict, now: dt.datetime) -> str | None:
    """The result line for `pr` (gh's JSON for FIELDS) at `now`, or None while it is moving."""
    if pr["state"] != "OPEN":
        return f"STATE {pr['state']}"
    fails = failed_checks(pr.get("statusCheckRollup") or [])
    if fails:
        return f"FAILED {fails}"
    if pr.get("mergeable") == "CONFLICTING":
        return "CONFLICT"
    # A requeue leaves the earlier `dequeued` label beside the new `queued` one.
    labels = {label["name"] for label in pr.get("labels") or []}
    if "dequeued" in labels and "queued" not in labels:
        return "DEQUEUED"
    comments = pr.get("comments") or []
    asks = [c for c in comments
            if c["author"]["login"] != MERGIFY and QUEUE_COMMAND.match(c["body"].strip())]
    if not asks:
        return "NOTQUEUED no queue command"
    since = _time(asks[-1]["createdAt"])
    # Mergify answers each command in a new comment and edits it as the PR moves; its
    # comments on earlier requests (one now says "Left the queue ... Requeued") don't count.
    replies = [c["body"].lower() for c in comments
               if c["author"]["login"] == MERGIFY and _time(c["createdAt"]) > since]
    if any("cancelled" in b or "has been removed" in b for b in replies):
        return "NOTQUEUED cancelled"
    if not replies and (now - since).total_seconds() > REPLY_GRACE:
        return "NOTQUEUED no reply from Mergify"
    return None


def fetch(pr: str, repo: str | None) -> dict | None:
    cmd = ["gh", "pr", "view", pr, "--json", FIELDS]
    if repo:
        cmd += ["--repo", repo]
    r = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True)
    if r.returncode != 0:
        print(f"prwatch: gh failed ({r.returncode}): {r.stderr.strip()}", file=sys.stderr)
        return None
    try:
        return json.loads(r.stdout)
    except json.JSONDecodeError as e:
        print(f"prwatch: unreadable gh output: {e}", file=sys.stderr)
        return None


def watch(pr: str, *, delay: float, interval: float, timeout: float, repo: str | None) -> str:
    deadline = time.monotonic() + timeout
    time.sleep(delay)
    while True:
        data = fetch(pr, repo)
        if data is not None:
            result = decide(data, dt.datetime.now(dt.timezone.utc))
            if result:
                return result
        if time.monotonic() + interval > deadline:
            return "timeout"
        time.sleep(interval)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("pr", help="PR number, URL or branch")
    ap.add_argument("--delay", type=float, default=45, help="seconds before the first poll (default 45)")
    ap.add_argument("--interval", type=float, default=30, help="seconds between polls (default 30)")
    ap.add_argument("--timeout", type=float, default=3 * 3600,
                    help="give up after this many seconds (default 10800: a queue run can take over an hour)")
    ap.add_argument("--repo", help="OWNER/NAME (default: the repository this file is in)")
    args = ap.parse_args(argv)
    result = watch(args.pr, delay=args.delay, interval=args.interval, timeout=args.timeout, repo=args.repo)
    print(result, flush=True)
    return 0 if result == "STATE MERGED" else 1


if __name__ == "__main__":
    sys.exit(main())
