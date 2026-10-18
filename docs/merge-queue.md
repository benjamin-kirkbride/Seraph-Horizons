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
`needs:` list and give each a `check <job> "$RESULT" ...` line, naming after the result
what may skip it: `"$TESTED"` if the green-tree memo below may, a `NO_<JOB>` flag from the
plan if a sliced run (below) may, nothing if neither may. A skip nothing on that line
accounts for fails it.

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
- The `tree` job looks the id up first. On record → `smoke`, `atlas`, `cairn`, `tools`
  and `units` (and `export` and `site`, which need `smoke`) are skipped and `ci-ok`
  passes on the earlier result.

The queue's temporary PR holds `main` + queued-ahead + PR. For a PR built on the current
`main` and queued with nothing ahead of it, that is exactly the tree the PR's own run
tested: same id, no second run. A docs-only commit on `main` changes nothing the id sees,
so it invalidates nobody. A PR queued behind others, or one whose `main` has moved by a
real change, has a new tree and gets the full run, which is the run that matters.

What the memo does not cover:

- `lock` always runs. It is cheap, and `packtool outdated` asks the ModDB whether a locked
  release was retracted, which no earlier pass can answer.
- A push to `main` uses the memo only for the test-only jobs: `atlas`, `tools`, `units`
  and `site` are skipped for a tree on record, but `smoke`, `cairn` and `export` still run.
  `next.yml` publishes the `recipe-export` artifact `smoke` dumps and the `dist` artifact
  `cairn` builds from that push run (with the `seraphhorizons-mod` zip, also `cairn`'s),
  and `export` validates what is about to ship.
- Tags and manual runs always run everything.
- Fork PRs can use a record but not write one (read-only token).
- A sliced run (below) never writes one.

`prune-green-trees.yml` deletes refs whose tested commit is older than 60 days, monthly.
Inspect them with `git ls-remote origin 'refs/green-trees/*'`; delete one to force a full
run for that tree.

## Sliced PR runs

Most PRs need the whole suite, but some change only what a few jobs, or a few Atlas
shards, can find anything in: a machine's model (epic #684's model branches and the
machines after them), a flaky scenario's fix. Without help each of their pushes would run
everything. So `tree` asks a planner, for a `pull_request` run whose tree is not on record:
`ci/paths.py plan HEAD^1 HEAD` diffs the PR's merge commit against the `main` it merges
into and prints a plan as JSON:

```json
{"sliced": true, "atlas": ["rest"], "smoke": false, "cairn": false, "site": false,
 "reasons": ["atlas_classes: 1 scenario file(s): classes SharedWorldScenarios -> shards rest"]}
```

`atlas` is `"all"` or the shards to run (names from `ci/atlas-shards.json`, the matrix's one
list); `smoke` covers `export` too, which validates what smoke writes. `tools`, `units` and
`lock` always run. The plan reaches the jobs as `tree`'s outputs (`ci/paths.py outputs`):
`smoke`, `cairn`, `site` and `atlas` (each `true` or `false`), `sliced`, and `atlas_matrix`,
the `atlas` job's `strategy.matrix`, the file's list filtered to the plan. Then:

- each job the plan left out is skipped, and the `atlas` job runs one job per planned shard
  (none at all when it planned none: the matrix is then the full list, because GitHub rejects
  an empty one even for a skipped job);
- `ci-ok` accepts exactly those skips, job by job, from the plan's flags, as it accepts the
  memo's. A skip neither accounts for still fails it, and so does a skipped `tools` or `units`
  unless the tree is on record. A sliced `atlas` passes when every shard it ran did;
- nothing is recorded under `refs/green-trees/`: the run did not test the tree.

**How it decides.** Every changed path must be claimed by one of the planner's rules
(`RULES` in `ci/paths.py`), each saying what that path needs. A path no rule claims needs
everything, and so does anything a rule cannot account for (a file it does not trust its
reading of, a git error): the planner only narrows a run on evidence, and on an error it
prints the full plan. The rules today:

- **noop**: the no-op paths above need nothing.
- **units**: `mods-src/seraphhorizons/tests/**`, a project of its own the mod never
  compiles, needs only `units`.
- **models**: a machine model's paths need only `tools` and `units` (below). A PR whose
  every path is a no-op or a model path is the former models-only fast path.
- **atlas_classes**: an Atlas scenario file needs the shards that run the classes it
  declares (below), and no smoke, cairn, export or site: none of those reads anything under
  `tests/PackTests` (the exporter is referenced by the scenarios, not the other way round).
- **site**: `site/**` needs the site job, on main's recipe export when nothing else plans
  smoke (below).
- **tooling**: tooling that only `tools` and one other job read needs that job alone
  (below).

A new rule is a function `(ctx, paths) -> list[Claim]` added to `RULES`: it sees the paths no
earlier rule claimed, claims those it understands with a `Need` (shards or `ALL`, and the
smoke, cairn and site flags), and leaves the rest. The planner unions the needs. `rest` also
builds and tests the local-only icon export and ore survey mods and runs the rusty gear
check, so a sliced plan whose change touches their inputs keeps `rest` (`REST_STEP_INPUTS`).

### Machine models

A model PR gives smoke, Atlas, cairn, the export and the site's end-to-end tests nothing to
find. Its paths, in `ci/paths.py` beside the memo's no-op pattern (`ci/code-tree.sh` reads
that pattern from there, so the two can't drift):

- `mods-src/seraphhorizons/<Folder>/tools/**/*.py`, a generator and its validator
  (`Machines/tools/` too). Python only: the mod compiles every `.cs` outside `tests/`, and its
  zip holds `assets/` alone;
- `mods-src/seraphhorizons/tests/<Folder>/**` and the tests' csproj, which only `units` reads;
- `tools/tests/test_*_model.py`, `site/models.json`, `site/src/lib/model-*.ts`, `site/test/**`;
- a machine's shapes and rig, `assets/seraphhorizons/shapes/block/<id>.json`,
  `shapes/block/<id>_<part>.json` and `config/<id>-rig.json`, **only while nothing that
  ships, loads or exports the mods names that machine's assets**. These ship in the mod zip,
  so a rig or shape a shipped machine loads (the bucking sawmill's `buckingmill-rig.json`,
  the rosser's, the draw bench's, ...) must never be sliced away. The planner looks
  in the tree under test, in every `mods-src/` mod (outside `tests/`, the generators and
  `*.md`), `tests/PackTests`, `tools/recipe-export` and `pack/`, for `<id>-rig`, a shape path
  `block/<id>`, the id in a blocktype, itemtype, entity or patch, or a C# string literal
  starting with it (`"<id>"`, `"<id>-frame-north"`). Any hit, and the PR runs everything.

`tools/tests/test_ci_paths.py` guards it: every rig and shape the mod loads today (by an
independent check of the asset paths in its C# and blocktypes) is refused, each reference
form refuses a scratch machine, no shipped code loads `shapes/` or `config/` wholesale
(which would load a machine's assets without naming it), and `code-tree.sh` hashes exactly
what `paths.py` calls a no-op. Anything else in the diff (a texture, a lang entry, a
machine's `Core/` or `Game/` code) is not a model path: once a machine is wired into the
game, its PRs run the full suite.

### Atlas scenario classes

A PR that changes only scenarios (a flake's fix, a new check) runs only the shards that
run them. The planner reads the `.cs` files directly under `tests/PackTests` at both the
base and the head, so a renamed or deleted class runs the shard its old name was in too:

- a changed file's test classes are the top-level classes it declares that derive from
  `AtlasScenarioBase` or hold a test method. A partial class (SharedWorldScenarios, one file
  per feature, few of them named after it) is one class over all its files;
- whatever else uses a type the file declares runs too: a helper in a scenario file, a
  static member, a base class or an extension method reaches another class only through its
  name (or the extension method's), so every type naming one, transitively, is affected.
  Comments are left out of that reading, string literals are not;
- each affected class's test methods (`Namespace.Class.Method`, inherited ones included) are
  run through each shard's filter, with a small evaluator of VSTest's filter syntax
  (`FullyQualifiedName` with `~`, `!~`, `=`, `!=`, `&` over `|`, parentheses). Method names
  matter: `~.TidyVariants` matches a SharedWorldScenarios method named `TidyVariants_...`.

It plans everything for a file that declares no test class (a helper or base class:
`PlayerJoin.cs`, `VirtualClock.cs`, `ExportUnderTest.cs`, `WoodworkingScenarioBase.cs`, ...),
any other file there (`PackTests.csproj`, `fixtures/`), a file with an assembly attribute, a
global using or a module initializer, a file laid out in a way it does not trust (anything
at column 0 that is not a using, a namespace, an attribute or a type: a block namespace, a
raw string's lines), and a test method no shard selects. A method two shards select runs in
both. `test_ci_paths.py` holds the matrix to every test method at HEAD being selected by
exactly one shard (this replaced checking by eye that `rest` is the complement of the
others), and every file there to read cleanly.

### The site and tooling

The recipe browser's code is read by `tools` (svelte-check, its unit tests) and the `site`
job (Playwright against a real export), and by nothing else CI runs: smoke, Atlas, cairn and
the export job read nothing under `site/`, and `pages.yml` deploys it only after merge. A
PR changing only `site/` (`prepare-data.ts` and the e2e tests included) runs `site` and no
smoke: the `Need` flag `site_main_export` says the site's tests want *an* export of the
current schema, not this tree's. The `site` job then takes the `recipe-export` artifact of
main's push run for the commit the PR merges into (`HEAD^1`), or main's newest passing one
when that run has not passed yet (`gh run download`, with `actions: read`, which a fork's
read-only token has on a public repository). It is safe because nothing under `site/` can
change what the export holds or its schema: what can (`schema/`, `tools/site-data`,
`tools/recipe-export`, the mods, `pack/`, `packtool.py`) plans smoke on its own claim, or
everything, and a plan with smoke gives the site this run's export. The plain `site` flag
(this tree's export) still forces smoke, as before. The job runs `!cancelled()` when smoke
succeeded or the plan left it out (and it was skipped), never after a planned smoke failed.

Tooling, each path traced through `ci.yml`, `setup-pack`, `tests/PackTests` and `packtool.py`
(`TOOLING` in `ci/paths.py`):

| Paths | Read by, besides `tools` | Plans |
|---|---|---|
| `tools/tests/*.py` | nothing (`test_gear_consumers.py` also `rest`) | nothing (`rest`) |
| `tools/icon-export/**`, `tools/ore-survey/**` | the `rest` shard's build and tests | `rest` |
| `tools/icons.py`, `tools/textures.py` | the export job's drift reports | smoke |
| `tools/item-values/**` | the export job's item values check | smoke |
| `tools/site-data/**` | the export job's validation (`pages.yml` after merge) | smoke |
| `icons/**` | the export job's drift, which only warns | nothing |

Left to the full plan: `packtool.py` and `configdefaults.py` (smoke, cairn, lock),
`tools/recipe-export/` (smoke, Atlas), `schema/` (Atlas copies it to validate the export,
and site-data), `textures/` (Atlas reads its index, `prepare-data` its images), and any
other path under `tools/tests/` than a module (a fixture's readers are not traced).

**The merge queue never slices.** Its temporary PR is a `pull_request` run too, with the
same diff against `main`, but `tree` never plans on a `mergify/merge-queue/*` branch: the
queue runs the full suite (and records the tree when it passes) before anything lands on
`main`. A sliced PR therefore pays one full run, at queue time, instead of one per push.
This leans on Mergify testing queued PRs on its own draft PRs (`max_parallel_checks` above 1
in `.mergify.yml`); were it to switch to in-place checks on the PR's own branch, the queue's
run would plan like the PR's and this needs revisiting. A push to `main`, a tag, a manual
run and a `workflow_call` never plan: `next.yml` and `release.yml` need the full artifacts.
