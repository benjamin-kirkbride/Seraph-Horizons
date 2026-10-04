# Merge queue

PRs land on `main` through the [Mergify merge queue](https://docs.mergify.com/merge-queue/),
configured in `.mergify.yml`. The merge button is deliberately out of use.

## Day to day

- Get a PR in: comment `@mergifyio queue` (the comment, not the `queue` label). Do it as soon as
  the PR is opened; there is no need to wait for CI. A green `ci-ok` is a queue condition,
  and Mergify re-evaluates it continuously, so a PR queued early enters the queue on its own
  the moment the check passes.
- Mergify opens a temporary draft PR per queued PR on a `mergify/merge-queue/*` branch
  holding `main` + everything queued ahead + the PR, runs CI on it, and merges the
  original PR (merge commit) when that passes. Queued PRs are tested speculatively, up to
  `max_parallel_checks` at once, so a queue of three costs one CI wall-clock, not three.
  If one fails it is dequeued and the ones behind it are re-tested without it.
- Stacked PRs (B based on A's branch) go through the queue as a unit: put
  `Depends-On: #<A>` in B's body and queue the top PR. The command propagates down,
  predecessors merge first, and every rung is queued against `main`.
- `main` moving under a PR does not invalidate its CI run: the queue re-tests against
  the current `main` anyway. There is no need to rebase just to merge.

## The required check

`ci-ok` (last job in `.github/workflows/ci.yml`) is the only CI check the ruleset
requires. It depends on every other job and passes only if all of them succeeded. It runs
`if: always()` because a plain dependent job is *skipped* when a dependency fails, and
GitHub and Mergify both read a skipped required check as passing. Add new CI jobs to its
`needs:` list, and to its `MEMOISED` list if the green-tree memo below may skip them.

## Protecting `main`

`main` is protected by a repository ruleset, not classic branch protection, and not by a
Mergify merge protection.

- **Restrict updates, with Mergify as an `exempt` bypass actor.** Nobody else can push to
  `main` or merge into it, admins included, so the merge button stays unusable. This
  matters because strict status checks are off: the button could otherwise land a PR that
  was only tested against an older `main`. Mergify's app is the only bypass actor.
  Don't add yourself or the admin role, or the button works again.
- **`ci-ok` is required**, with strict status checks off. The queue's speculative checks
  need strict off: a temporary PR for `main+A+B` is tested before `A` has landed, so B's
  own branch is never "up to date" at the moment the queue merges it.
- **Force pushes and deletion are blocked.**
- **The ruleset targets `main` only.** The queue pushes its temporary
  `mergify/merge-queue/*` branches, and these must not match it.
- **GitHub's native merge queue rule and auto-merge are off.** Both would race Mergify.

### Why not a Mergify merge protection

A `merge_protections` rule such as `queue-position >= 0`, made required in GitHub, looks
like it keeps the button grey until the queue picks the PR up. It deadlocks instead.
Mergify won't merge *or queue* a PR until every active merge protection passes, and it
adds the `Mergify Merge Protections` check to every queue's requirements whatever
`branch_protection_injection_mode` says. The PR can't enter the queue until it is in
the queue, so it never enters (see #156). Merge protections are fine for conditions a PR
can meet on its own, like freeze windows or labels. Blocking the merge button is the
ruleset's job.

## Setup (one-time, after this config is on `main`)

Mergify reads `.mergify.yml` from the default branch, so this lands via a normal merge
first. Then:

```sh
# 1. Protect main with a ruleset. 10562 = the Mergify app, 15368 = GitHub Actions.
gh api -X POST repos/{owner}/{repo}/rulesets --input - <<'JSON'
{"name": "main", "target": "branch", "enforcement": "active",
 "conditions": {"ref_name": {"include": ["~DEFAULT_BRANCH"], "exclude": []}},
 "bypass_actors": [{"actor_id": 10562, "actor_type": "Integration", "bypass_mode": "exempt"}],
 "rules": [
   {"type": "update"},
   {"type": "deletion"},
   {"type": "non_fast_forward"},
   {"type": "required_status_checks",
    "parameters": {"strict_required_status_checks_policy": false,
                   "required_status_checks": [{"context": "ci-ok", "integration_id": 15368}]}}]}
JSON

# 2. Drop classic branch protection now that the ruleset covers main.
gh api -X DELETE repos/{owner}/{repo}/branches/main/protection

# 3. GitHub's native auto-merge would race the queue. Keep it off.
gh api -X PATCH repos/{owner}/{repo} -F allow_auto_merge=false
```

To check it, open any PR: the merge button should say you aren't allowed to merge
into `main`, while `@mergifyio queue` still lands it. The ruleset lives under
Settings > Rules > Rulesets.

## Cost, and the green-tree memo

Without help, each PR would run the full CI twice before it lands: once on the PR, once
on its temporary queue PR. CI skips the second run when it is provably redundant:

- `ci/code-tree.sh` gives a commit an id over what CI reads: its tree minus docs,
  `.mergify.yml` and the other no-op paths listed in that script.
- A `pull_request` run in which every job passed records `refs/green-trees/<id>` → the
  merge commit it tested (a ref in the repo's ref database, like `refs/pull/*`; not a
  branch, not a file). `ci-ok` writes it.
- The `tree` job looks the id up first. On record → `smoke`, `atlas`, `cairn` and
  `tools` (and `export` and `site`, which need `smoke`) are skipped and `ci-ok` passes
  on the earlier result.

The queue's temporary PR holds `main` + queued-ahead + PR. For a PR built on the current
`main` and queued with nothing ahead of it, that is exactly the tree the PR's own run
tested: same id, no second run. A docs-only commit on `main` changes nothing the id sees,
so it invalidates nobody. A PR queued behind others, or one whose `main` has moved by a
real change, has a new tree and gets the full run, which is the run that matters.

What the memo does not cover:

- `lock` always runs. It is cheap, and `packtool outdated` asks the ModDB whether a locked
  release was retracted, which no earlier pass can answer.
- A push to `main` uses the memo only for the test-only jobs: `atlas`, `tools` and
  `site` are skipped for a tree on record, but `smoke`, `cairn` and `export` still run.
  `next.yml` publishes the `recipe-export` artifact `smoke` dumps and the `dist` artifact
  `cairn` builds from that push run (with the `seraphhorizons-mod` zip, also `cairn`'s),
  and `export` validates what is about to ship.
- Tags and manual runs always run everything.
- Fork PRs can use a record but not write one (read-only token).

`prune-green-trees.yml` deletes refs whose tested commit is older than 60 days, monthly.
Inspect them with `git ls-remote origin 'refs/green-trees/*'`; delete one to force a full
run for that tree.
