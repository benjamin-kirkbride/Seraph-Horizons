# Merge queue

PRs land on `main` through the [Mergify merge queue](https://docs.mergify.com/merge-queue/),
configured in `.mergify.yml`. The merge button is deliberately out of use. This is the same
setup as The Decay Factor's.

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

`ci-ok` (last job in `.github/workflows/ci.yml`) is the only CI check branch protection
requires. It depends on every other job and passes only if all of them succeeded. It runs
`if: always()` because a plain dependent job is *skipped* when a dependency fails, and
GitHub and Mergify both read a skipped required check as passing. Add new CI jobs to its
`needs:` list.

## Branch protection

- **Strict status checks are off.** The queue's speculative checks need it: a temporary PR
  for `main+A+B` is tested before `A` has landed, so B's own branch is never "up to date"
  at the moment the queue merges it.
- **`Mergify Merge Protections` should be required** alongside `ci-ok`. With strict off, the
  merge button could otherwise land a PR tested only against an older `main`. That check
  only passes while the queue itself is handling the PR.
- **Admins are included** (`enforce_admins`), and GitHub's native auto-merge is off: it
  fires as soon as the required checks pass, which would race the queue.

## Setup (one-time, after this config is on `main`)

Mergify reads `.mergify.yml` from the default branch, so this lands via a normal merge
first. Then:

```sh
# 1. Protect main: require ci-ok (app 15368 = GitHub Actions), strict off, admins included.
gh api -X PUT repos/{owner}/{repo}/branches/main/protection --input - <<'JSON'
{"required_status_checks": {"strict": false,
                            "checks": [{"context": "ci-ok", "app_id": 15368}]},
 "enforce_admins": true,
 "required_pull_request_reviews": null,
 "restrictions": null}
JSON

# 2. GitHub's native auto-merge would race the queue. Keep it off.
gh api -X PATCH repos/{owner}/{repo} -F allow_auto_merge=false
```

Then require the merge-protection check. It is only ever posted if the **Merge Protections**
product is enabled for the repo in the Mergify dashboard (Integrations > GitHub > Configure).
Until it is, do not make it required: nothing, the queue included, could merge without it.
Once the check appears on a PR:

```sh
gh api -X PATCH repos/{owner}/{repo}/branches/main/protection/required_status_checks --input - <<'JSON'
{"strict": false,
 "checks": [{"context": "ci-ok", "app_id": 15368},
            {"context": "Mergify Merge Protections", "app_id": -1}]}
JSON
```

`app_id=-1` means "any app" (it reads back as `null`). Use Mergify's app id once it has
posted the check (`gh api repos/{owner}/{repo}/commits/<sha>/check-runs --jq '.check_runs[] | [.name, .app.id]'`).

## Not copied (yet)

The Decay Factor also keeps a green-tree memo (`refs/green-trees/*`) that skips the second
CI run on the queue's temporary PR when its tree matches one that already passed. Here that
means each PR runs CI twice: once on the PR, once in the queue. Worth adding if CI minutes
or queue latency start to hurt.
