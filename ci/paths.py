#!/usr/bin/env python3
"""Which paths CI reads, in one place: the green-tree memo's no-op paths and the
models-only fast path's allow-list (docs/merge-queue.md).

  python3 ci/paths.py noop-regex             print the no-op pattern (ci/code-tree.sh reads it)
  python3 ci/paths.py classify <base> <head> exit 0 if every path changed from <base> to <head>
                                             is models-only, 1 if not (reasons on stdout)

No-op paths: nothing in CI reads them, so ci/code-tree.sh leaves them out of the
tree id. Anything not matched counts as an input, including this file and the
workflows, so the list can only ever be widened deliberately.

Models-only paths: what a machine model PR changes (a generator, the shape and rig
it writes, the rig tests and reference poses, the site's model viewer) and nothing
else. A PR whose every changed path is a no-op or models-only path runs only the
`tools` and `units` jobs (.github/workflows/ci.yml). A shape or rig config ships in
the mod zip, so one is models-only only while no shipped code, block, patch, Atlas
scenario or exporter names its machine: `referenced` looks, in the tree under
test, and the shipped machines' rigs and shapes (the bucking sawmill's
buckingmill-rig.json, ...) never qualify. tools/tests/test_ci_paths.py holds both
lists to that.

Stdlib only; needs git.
"""

from __future__ import annotations

import re
import subprocess
import sys

# Matched at the start of a repository path (ci/code-tree.sh anchors it after
# ls-tree's tab). Must stay a POSIX ERE as well as a Python regex: grep -E reads it.
NOOP = r"(\.claude/|\.gitignore$|\.mergify\.yml$|docs/|LICENSE$|[^/]*\.md$|.*/[^/]*\.md$)"

_MOD = "mods-src/seraphhorizons/"
_ASSETS = _MOD + "assets/seraphhorizons/"

# A machine's model assets, which ship in the mod zip: models-only only while
# nothing shipped names the machine (`referenced`). `id` is the machine's asset
# name (amalgampan), its shapes shapes/block/<id>.json and <id>_<part>.json.
MODEL_ASSETS = [
    re.compile(re.escape(_ASSETS) + r"shapes/block/(?P<id>[a-z0-9]+)(?:_[a-z0-9_-]+)?\.json"),
    re.compile(re.escape(_ASSETS) + r"config/(?P<id>[a-z0-9]+)-rig\.json"),
]

# Never shipped and never read by smoke, Atlas, cairn, the exporter or the site's
# end-to-end tests: each is tested by the tools job (Python and site unit tests)
# or the units job (dotnet test mods-src/seraphhorizons/tests).
MODEL_SOURCES = [
    # A machine's generator and validator (<Machine>/tools/, Machines/tools/ the
    # package they share). Python only: the mod's csproj compiles every .cs
    # outside tests/, and its zip holds assets/ alone.
    re.compile(re.escape(_MOD) + r"(?!assets/|tests/)[A-Za-z0-9]+/tools/[A-Za-z0-9_./-]+\.py"),
    # The rig tests and their reference poses, one folder per feature, and the
    # project file that links the shipped rig into them.
    re.compile(re.escape(_MOD) + r"tests/[A-Za-z0-9]+/.+"),
    re.compile(re.escape(_MOD) + r"tests/SeraphHorizons\.Tests\.csproj"),
    re.compile(r"tools/tests/test_[a-z0-9_]+_model\.py"),
    # The site's model viewer: svelte-check and its unit tests (tools job). The
    # end-to-end models page is tested by the merge queue's full run.
    re.compile(r"site/models\.json"),
    re.compile(r"site/src/lib/model-[a-z0-9-]+\.ts"),
    re.compile(r"site/test/.+"),
]

# Where a reference to a machine makes its model assets live: everything that
# ships, loads or exports the mods, less what only the fast path's own jobs read.
REFERENCE_ROOTS = ["mods-src", "tests/PackTests", "tools/recipe-export", "pack"]
REFERENCE_EXCLUDES = [
    ":(glob,exclude)mods-src/*/tests/**",
    ":(glob,exclude)mods-src/*/*/tools/**/*.py",
    ":(glob,exclude)**/*.md",
]
# Assets that define what the game loads: a block, item or entity coded after
# the machine, or a patch naming it, may load its shape or rig by that code.
_DEFINING = re.compile(r"mods-src/[^/]+/assets/[^/]+/(?:blocktypes|itemtypes|entities|patches)/.+")


def _reference_forms(machine: str, path: str) -> list[re.Pattern]:
    """How a file at `path` would name `machine`'s model assets, case ignored:
    its rig (<id>-rig), a shape path (block/<id>, block/<id>_frame), the
    machine itself in a defining asset, and in C# a string literal that starts
    with it ("<id>", "<id>-frame-north", "<id>/..."). Prose and identifiers
    (the ore code's enum Concentrator.Sluice, a config key) are not loads."""
    m = re.escape(machine)
    forms = [rf"(?<![a-z0-9]){m}-rig(?![a-z0-9])", rf"block/{m}(?![a-z0-9])"]
    if _DEFINING.fullmatch(path):
        forms.append(rf"(?<![a-z0-9]){m}(?![a-z0-9])")
    if path.endswith(".cs"):
        forms.append(rf"\"{m}(?=[\"/_.{{-])")
    return [re.compile(f, re.IGNORECASE) for f in forms]


def is_noop(path: str) -> bool:
    return re.match(NOOP, path) is not None


def model_asset_id(path: str) -> str | None:
    """The machine a model asset belongs to, or None if the path is not one."""
    for pattern in MODEL_ASSETS:
        m = pattern.fullmatch(path)
        if m:
            return m.group("id")
    return None


def is_model_source(path: str) -> bool:
    return any(p.fullmatch(path) for p in MODEL_SOURCES)


def _git(*args: str, cwd: str | None = None) -> subprocess.CompletedProcess:
    return subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True)


def referenced(machine: str, rev: str, cwd: str | None = None) -> list[str]:
    """Files at `rev` that ship, load or export the mods and name `machine`'s
    model assets (`_reference_forms`), other than those assets themselves.
    Erring wide is safe: a false hit only costs the full suite."""
    r = _git("grep", "-I", "-i", "-l", "-F", "-e", machine, rev, "--",
             *REFERENCE_ROOTS, *REFERENCE_EXCLUDES, cwd=cwd)
    if r.returncode not in (0, 1):
        raise RuntimeError(f"git grep failed: {r.stderr.strip()}")
    hits = []
    for line in r.stdout.splitlines():
        path = line[len(rev) + 1:] if line.startswith(rev + ":") else line
        if model_asset_id(path) == machine:
            continue
        shown = _git("show", f"{rev}:{path}", cwd=cwd)
        if shown.returncode != 0:
            raise RuntimeError(f"git show {rev}:{path} failed: {shown.stderr.strip()}")
        if any(f.search(shown.stdout) for f in _reference_forms(machine, path)):
            hits.append(path)
    return hits


def classify(paths: list[str], rev: str, cwd: str | None = None) -> tuple[bool, list[str]]:
    """(models_only, reasons): true only when `paths` is non-empty and each is a
    no-op path, a model source, or a model asset whose machine nothing at `rev`
    references."""
    if not paths:
        return False, ["no changed paths"]
    reasons, ids = [], {}
    for path in paths:
        if is_noop(path) or is_model_source(path):
            continue
        machine = model_asset_id(path)
        if machine is None:
            reasons.append(f"{path}: not a models-only path")
        else:
            ids.setdefault(machine, []).append(path)
    for machine, owned in sorted(ids.items()):
        hits = referenced(machine, rev, cwd)
        if hits:
            reasons.append(f"{', '.join(owned)}: {machine} is named by {', '.join(hits[:5])}"
                           + (f" and {len(hits) - 5} more" if len(hits) > 5 else ""))
    return not reasons, reasons


def changed(base: str, head: str, cwd: str | None = None) -> list[str]:
    r = _git("diff", "--name-only", "--no-renames", "-z", base, head, cwd=cwd)
    if r.returncode != 0:
        raise RuntimeError(f"git diff failed: {r.stderr.strip()}")
    return [p for p in r.stdout.split("\0") if p]


def main(argv: list[str]) -> int:
    if argv[:1] == ["noop-regex"] and len(argv) == 1:
        print(NOOP)
        return 0
    if argv[:1] == ["classify"] and len(argv) == 3:
        base, head = argv[1], argv[2]
        try:
            paths = changed(base, head)
            ok, reasons = classify(paths, head)
        except RuntimeError as e:
            print(f"cannot classify: {e}")
            return 2
        print(f"{len(paths)} changed path(s) from {base} to {head}")
        if ok:
            print("models-only: every changed path is a no-op or models-only path")
            return 0
        for reason in reasons:
            print(f"not models-only: {reason}")
        return 1
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
