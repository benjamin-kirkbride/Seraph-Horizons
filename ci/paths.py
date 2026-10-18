#!/usr/bin/env python3
"""Which paths CI reads, in one place: the green-tree memo's no-op paths, and the
planner that decides what a PR run must test (docs/merge-queue.md, "Sliced PR runs").

  python3 ci/paths.py noop-regex             print the no-op pattern (ci/code-tree.sh reads it)
  python3 ci/paths.py plan <base> <head>     print, as JSON, what a PR changing <base> to <head>
                                             needs run (the planner, below); never fails: an
                                             error is a full plan with it as its reason
  python3 ci/paths.py outputs <plan.json|full>
                                             the plan as the `tree` job's GitHub outputs: smoke,
                                             cairn, site, atlas (true/false), sliced, and
                                             atlas_matrix (ci/atlas-shards.json, filtered)
  python3 ci/paths.py classify <base> <head> exit 0 if every path changed is models-only, 1 if
                                             not (reasons on stdout)

No-op paths: nothing in CI reads them, so ci/code-tree.sh leaves them out of the
tree id. Anything not matched counts as an input, including this file and the
workflows, so the list can only ever be widened deliberately.

The planner. Every changed path must be claimed by a rule (RULES), and each claim says
what that path needs run: a `Need`, the Atlas shards (a set, or ALL) and whether smoke
(and with it export), cairn and site must run. The plan is the union of the claims'
needs. A path no rule claims needs everything, and so does a rule that cannot decide
(a parse it does not trust, a git error): the planner only ever narrows a run on
evidence. tools, units and lock always run. A plan that leaves anything out is
`sliced`, and a sliced run records no green tree, so the merge queue, which never
plans, runs the full suite before anything lands. The rules:

- noop: the no-op paths need nothing.
- units: mods-src/seraphhorizons/tests/, a project of its own only the units job builds.
- models: a machine model PR's paths (a generator, the shape and rig it writes, the rig
  tests and reference poses, the site's model viewer) need nothing beyond tools and
  units. A shape or rig config ships in the mod zip, so one qualifies only while no
  shipped code, block, patch, Atlas scenario or exporter names its machine:
  `referenced` looks, in the tree under test, and the shipped machines' rigs and
  shapes (the bucking sawmill's buckingmill-rig.json, ...) never qualify. A PR whose
  every path is claimed by these two is the former models-only fast path (`classify`).
- atlas-classes: a scenario file directly under tests/PackTests needs the shards whose
  filter selects the test classes it declares, at base and at head (a renamed or
  deleted class counts), and those of every type that, transitively, names one it
  declares (scenario_classes). Any other file there (a helper, a base class, the csproj,
  a fixture) needs every shard. Smoke, cairn, the export and the site read nothing
  under tests/PackTests, so it needs none of them.
- site: site/ needs only the site's end-to-end tests, on main's latest recipe export
  when nothing else plans smoke (`site_main_export`): nothing but tools and the site job
  reads it, and nothing in it changes what the export holds.
- tooling: tooling only the tools job and one other job read (TOOLING): Python tool
  tests and the icon store need nothing more, the local-only icon export and ore survey
  mods and the rusty gear check the `rest` shard, and the tools the export job runs on
  smoke's export (icons.py, textures.py, item-values, site-data) smoke. Not packtool.py,
  configdefaults.py, recipe-export, schema/ or textures/: smoke, cairn or Atlas read them.

A new rule is a function (ctx, paths) -> list[Claim] appended to RULES: it is offered
only the paths no earlier rule claimed, claims those it understands, and leaves the
rest. Shards come from ci/atlas-shards.json, the one list the workflow reads too
(ci/atlas-shards.md says why each is what it is).

Stdlib only; needs git.
"""

from __future__ import annotations

import json
import re
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SHARDS_FILE = ROOT / "ci" / "atlas-shards.json"

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


# ----------------------------------------------------------------- the Atlas shards


def load_shards(path: Path = SHARDS_FILE) -> list[dict]:
    """ci/atlas-shards.json: [{"shard": name, "filter": dotnet test filter}, ...], in the
    order the workflow's matrix runs them (ci/atlas-shards.md says why each is what it is)."""
    shards = json.loads(path.read_text())
    names = [s["shard"] for s in shards]
    if not shards or len(set(names)) != len(names) or any(set(s) != {"shard", "filter"} for s in shards):
        raise ValueError(f"{path}: want a non-empty list of distinct {{shard, filter}} objects")
    for s in shards:
        parse_filter(s["filter"])
    return shards


class FilterError(ValueError):
    pass


def parse_filter(text: str):
    """A predicate over a test's fully qualified name for a `dotnet test --filter`
    expression, as VSTest reads one: `Prop~V` (contains), `Prop!~V`, `Prop=V`, `Prop!=V`,
    each case-insensitive, `&` binding tighter than `|`, parentheses, `\\` escaping the
    next character. Only FullyQualifiedName is a property here: a shard filtering on
    anything else (a trait, a display name) is beyond what the planner can evaluate, and
    raises, which makes every plan full."""
    pos = 0

    def peek() -> str:
        return text[pos] if pos < len(text) else ""

    def expr():
        nonlocal pos
        terms = [conj()]
        while peek() == "|":
            pos += 1
            terms.append(conj())
        return lambda name: any(t(name) for t in terms)

    def conj():
        nonlocal pos
        factors = [factor()]
        while peek() == "&":
            pos += 1
            factors.append(factor())
        return lambda name: all(f(name) for f in factors)

    def factor():
        nonlocal pos
        if peek() == "(":
            pos += 1
            inner = expr()
            if peek() != ")":
                raise FilterError(f"unbalanced parenthesis at {pos} in {text!r}")
            pos += 1
            return inner
        m = re.compile(r"\s*([A-Za-z]+)\s*(!~|!=|~|=)").match(text, pos)
        if not m:
            raise FilterError(f"expected a condition at {pos} in {text!r}")
        prop, op = m.group(1), m.group(2)
        if prop != "FullyQualifiedName":
            raise FilterError(f"{prop}: only FullyQualifiedName conditions are understood ({text!r})")
        pos = m.end()
        value = []
        while pos < len(text) and text[pos] not in "&|()":
            if text[pos] == "\\" and pos + 1 < len(text):
                pos += 1
            value.append(text[pos])
            pos += 1
        v = "".join(value).strip().lower()
        if not v:
            raise FilterError(f"empty value at {pos} in {text!r}")
        return {
            "~": lambda name: v in name.lower(),
            "!~": lambda name: v not in name.lower(),
            "=": lambda name: name.lower() == v,
            "!=": lambda name: name.lower() != v,
        }[op]

    result = expr()
    if pos != len(text):
        raise FilterError(f"unexpected {text[pos]!r} at {pos} in {text!r}")
    return result


def shards_selecting(fqn: str, shards: list[dict]) -> list[str]:
    return [s["shard"] for s in shards if parse_filter(s["filter"])(fqn)]


# ----------------------------------------------------------------- C# scenario files

PACKTESTS = "tests/PackTests/"
# Test methods: Atlas' own attributes and xunit's, alone or in a list ([ReadsBootLog, AtlasScenario]).
TEST_ATTRIBUTES = ("AtlasScenario", "AtlasTheory", "Fact", "Theory")
_TEST_ATTR = re.compile(r"^\s*\[(?:[^\]]*[\s,(])?(?:" + "|".join(TEST_ATTRIBUTES) + r")\b(?!\s*[.=])")
_MODIFIERS = r"(?:(?:public|internal|private|protected|file|sealed|abstract|static|partial|readonly|unsafe|new|ref)\s+)*"
_TYPE_DECL = re.compile(r"^(?P<mods>" + _MODIFIERS + r")(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)"
                        r"\s+(?P<name>[A-Za-z_]\w*)")
_IDENT = re.compile(r"[A-Za-z_]\w*")
_EXTENSION = re.compile(r"\bstatic\b[^;{}=()]*?\b([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(\s*this\s")
# Effects beyond the file's own types: on the whole test assembly.
_GLOBAL = re.compile(r"^\s*(?:\[\s*assembly\s*:|global\s+using\b|\[\s*ModuleInitializer\b)", re.M)
_SCENARIO_ROOT = "AtlasScenarioBase"


def _code_tokens(line: str) -> set[str]:
    """The identifiers on a line, less what is certainly a comment: a line that starts
    with `//` (doc comments with their <see cref>s included), or the tail from a `//` with
    no quote before it on the line. Anything less certain is kept: a name read in a
    comment only costs a shard, a name dropped from code would miss one."""
    s = line.lstrip()
    if s.startswith("//"):
        return set()
    cut = line.find("//")
    if cut >= 0 and '"' not in line[:cut] and "'" not in line[:cut]:
        line = line[:cut]
    return set(_IDENT.findall(line))


@dataclass
class CsType:
    """One top-level declaration (a part, for a partial class)."""
    name: str
    static: bool
    abstract: bool
    base: str | None
    tests: list[str] = field(default_factory=list)       # test methods declared in it
    tokens: set[str] = field(default_factory=set)        # identifiers its code names
    extensions: set[str] = field(default_factory=set)    # extension methods it declares


@dataclass
class CsFile:
    """What the planner reads of one .cs file: its file-scoped namespace, its top-level
    types, whether it reaches the whole test assembly (an assembly attribute, a global
    using, a module initializer), and `problem`, set when the file is laid out in a way
    this reading does not trust."""
    namespace: str | None = None
    types: list[CsType] = field(default_factory=list)
    global_effect: bool = False
    problem: str | None = None

    @property
    def names(self) -> set[str]:
        return {t.name for t in self.types}


def scan_cs(text: str) -> CsFile:
    """Read a C# file laid out as tests/PackTests' are: a file-scoped namespace, top-level
    types declared at column 0 and their members indented. A type's code runs from its
    declaration (and the attributes above it) to the next one; the file's usings count
    as every type's. Anything else at column 0 (a block namespace's contents, a raw
    string's lines) makes the file a `problem`, as does a test attribute with no method
    after it."""
    f = CsFile(global_effect=bool(_GLOBAL.search(text)))
    lines = text.splitlines()
    shared: set[str] = set()          # usings: every type's
    pending: set[str] = set()         # column-0 attributes: the next type's
    current: CsType | None = None
    pending_test = None
    in_block_comment = False
    for i, line in enumerate(lines):
        if in_block_comment:
            in_block_comment = "*/" not in line
            continue
        stripped = line.strip()
        if pending_test is not None and stripped and not stripped.startswith(("[", "//")):
            m = re.search(r"\b([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(", stripped)
            if not m or current is None:
                break
            current.tests.append(m.group(1))
            pending_test = None
        if _TEST_ATTR.match(line):
            pending_test = i
        if not line or line[0] in " \t" or stripped.startswith("//"):
            if current is not None:
                current.tokens |= _code_tokens(line)
            continue
        if stripped.startswith("/*"):
            in_block_comment = "*/" not in stripped
            continue
        if stripped in ("{", "}"):
            continue
        if stripped.startswith("using "):
            shared |= _code_tokens(line)
            continue
        if stripped.startswith("["):
            pending |= _code_tokens(line)
            continue
        m = re.fullmatch(r"namespace\s+([\w.]+)\s*;", stripped)
        if m:
            if f.namespace is not None:
                f.problem = "more than one namespace"
                return f
            f.namespace = m.group(1)
            continue
        m = _TYPE_DECL.match(line)
        if m:
            header = []
            for rest in lines[i:i + 20]:
                header.append(rest)
                if "{" in rest or rest.rstrip().endswith(";"):
                    break
            head = " ".join(header).split("{", 1)[0]
            while re.search(r"\([^()]*\)", head):
                head = re.sub(r"\([^()]*\)", "", head)
            head = re.sub(r"<[^<>]*>", "", head)
            base = re.search(r":\s*([A-Za-z_][\w.]*)", head.split(" where ", 1)[0])
            mods = m.group("mods").split()
            current = CsType(m.group("name"), "static" in mods, "abstract" in mods,
                             base.group(1).rsplit(".", 1)[-1] if base else None,
                             tokens=pending | _code_tokens(line))
            pending = set()
            f.types.append(current)
            continue
        f.problem = f"line {i + 1}: {stripped[:60]!r} at column 0 is not a declaration this reads"
        return f
    if pending_test is not None:
        f.problem = f"line {pending_test + 1}: a test attribute with no method after it"
    elif f.namespace is None:
        f.problem = "no file-scoped namespace"
    # Split by declaration: a type's extension methods are those in its own lines.
    for t in f.types:
        t.tokens |= shared
    for t, (start, end) in zip(f.types, _type_spans(lines)):
        t.extensions = set(_EXTENSION.findall("\n".join(lines[start:end])))
    return f


def _type_spans(lines: list[str]) -> list[tuple[int, int]]:
    starts = [i for i, line in enumerate(lines) if _TYPE_DECL.match(line)]
    return list(zip(starts, starts[1:] + [len(lines)]))


@dataclass
class ScenarioTree:
    """The .cs files directly under tests/PackTests at one revision, read."""
    files: dict[str, CsFile]

    def parts(self) -> dict[str, list[CsType]]:
        out: dict[str, list[CsType]] = {}
        for f in self.files.values():
            for t in f.types:
                out.setdefault(t.name, []).append(t)
        return out

    def test_classes(self) -> dict[str, set[str]]:
        """Test class -> its test methods' fully qualified names (inherited ones included,
        as xunit names them under the derived class). A test class is a top-level class,
        neither static nor abstract, that derives from AtlasScenarioBase (through bases
        declared here) or has a part with a test method."""
        parts = self.parts()
        namespaces = {t.name: f.namespace for f in self.files.values() for t in f.types}

        def chain(name: str) -> list[str]:
            seen = [name]
            while (b := next((t.base for t in parts.get(seen[-1], []) if t.base), None)) and b not in seen:
                seen.append(b)
            return seen

        out = {}
        for name, ps in parts.items():
            if any(t.static or t.abstract for t in ps):
                continue
            lineage = chain(name)
            if _SCENARIO_ROOT not in lineage and not any(t.tests for t in ps):
                continue
            methods = {m for cls in lineage for t in parts.get(cls, []) for m in t.tests}
            out[name] = {f"{namespaces[name]}.{name}.{m}" for m in methods} or {f"{namespaces[name]}.{name}"}
        return out

    def dependents(self, names: set[str]) -> set[str]:
        """`names` and every type that, transitively, names one of them in its code (a
        member, a base class, a `using static`) or calls an extension method one declares:
        the only ways C# code reaches another type's. Parts of a partial class are one type."""
        parts = self.parts()
        tokens = {n: set().union(*(t.tokens for t in ps)) - {n} for n, ps in parts.items()}
        exts = {n: set().union(*(t.extensions for t in ps)) for n, ps in parts.items()}
        out, frontier = set(names), list(names)
        while frontier:
            n = frontier.pop()
            reach = {n} | exts.get(n, set())
            for m, toks in tokens.items():
                if m not in out and toks & reach:
                    out.add(m)
                    frontier.append(m)
        return out


def read_scenario_tree(rev: str, cwd: str | None = None) -> ScenarioTree:
    ls = _git("ls-tree", "--name-only", rev, "--", PACKTESTS, cwd=cwd)
    if ls.returncode != 0:
        raise RuntimeError(f"git ls-tree {rev} failed: {ls.stderr.strip()}")
    names = [p for p in ls.stdout.splitlines() if p.endswith(".cs")]
    if not names:
        return ScenarioTree({})
    r = subprocess.run(["git", "cat-file", "--batch"], cwd=cwd, capture_output=True,
                       input="".join(f"{rev}:{p}\n" for p in names).encode())
    if r.returncode != 0:
        raise RuntimeError(f"git cat-file failed: {r.stderr.decode(errors='replace').strip()}")
    out, data, pos = {}, r.stdout, 0
    for p in names:
        nl = data.index(b"\n", pos)
        header = data[pos:nl].decode().split()
        if len(header) != 3 or header[1] != "blob":
            raise RuntimeError(f"git cat-file: {rev}:{p}: {' '.join(header)}")
        size = int(header[2])
        out[p] = scan_cs(data[nl + 1:nl + 1 + size].decode("utf-8", errors="replace"))
        pos = nl + 1 + size + 1
    return ScenarioTree(out)


def scenario_classes(tree: ScenarioTree, changed_files: list[str]) -> tuple[set[str], list[str]]:
    """(test classes, problems): the test classes that changing `changed_files` (scenario
    files, read at this revision) may change the behaviour of: those of the types the
    files declare, and of every type depending on one (ScenarioTree.dependents). A
    problem means the classes cannot be trusted. Changed files absent at this revision
    are skipped (the other revision has them)."""
    present = [p for p in changed_files if p in tree.files]
    problems = [f"{p}: {tree.files[p].problem}" for p in present if tree.files[p].problem]
    problems += [f"{p}: has an assembly attribute, a global using or a module initializer, which reach every class"
                 for p in present if tree.files[p].global_effect]
    tests = tree.test_classes()
    for p in present:
        if not tree.files[p].names & tests.keys():
            problems.append(f"{p}: declares no test class (a helper or base class every shard may use)")
    if problems:
        return set(), problems
    affected = tree.dependents({n for p in present for n in tree.files[p].names})
    problems = [f"{q} (uses a changed type): {g.problem}" for q, g in tree.files.items()
                if g.problem and g.names & affected]
    return affected & tests.keys(), problems


# ----------------------------------------------------------------- the planner

ALL = "all"


@dataclass(frozen=True)
class Need:
    """What a changed path needs run. tools, units and lock always run, so they are not here."""
    atlas: frozenset | str = frozenset()   # shard names, or ALL
    smoke: bool = False                    # with it the export job, which validates its output
    cairn: bool = False
    site: bool = False                     # the site's end-to-end tests, on this tree's export (keeps smoke)
    # The site's end-to-end tests, on any export of the current schema: main's latest when
    # nothing else plans smoke (the site job's fallback). Only for a path that cannot change
    # what the export holds or how it is read before the site's own code (prepare-data
    # included) gets it.
    site_main_export: bool = False

    def __or__(self, other: "Need") -> "Need":
        atlas = ALL if ALL in (self.atlas, other.atlas) else self.atlas | other.atlas
        return Need(atlas, self.smoke or other.smoke, self.cairn or other.cairn, self.site or other.site,
                    self.site_main_export or other.site_main_export)


NOTHING = Need()
EVERYTHING = Need(ALL, True, True, True)


@dataclass
class Claim:
    paths: list[str]
    need: Need
    reason: str


@dataclass
class Context:
    base: str
    head: str
    cwd: str | None
    shards: list[dict]
    _trees: dict = field(default_factory=dict)

    def scenario_tree(self, rev: str) -> ScenarioTree:
        if rev not in self._trees:
            self._trees[rev] = read_scenario_tree(rev, self.cwd)
        return self._trees[rev]


def rule_noop(ctx: Context, paths: list[str]) -> list[Claim]:
    hits = [p for p in paths if is_noop(p)]
    return [Claim(hits, NOTHING, f"{len(hits)} no-op path(s)")] if hits else []


# The pack's own mod's unit tests: its csproj compiles nothing under tests/, which is a
# project of its own that only the units job builds (and the site's unit tests, in tools,
# read its rig fixtures).
UNIT_TESTS = re.compile(re.escape(_MOD) + r"tests/.+")


def rule_units(ctx: Context, paths: list[str]) -> list[Claim]:
    hits = [p for p in paths if UNIT_TESTS.fullmatch(p)]
    return [Claim(hits, NOTHING, f"{len(hits)} unit test path(s): the units job runs them")] if hits else []


def rule_models(ctx: Context, paths: list[str]) -> list[Claim]:
    claims, ids = [], {}
    sources = [p for p in paths if is_model_source(p)]
    if sources:
        claims.append(Claim(sources, NOTHING, f"{len(sources)} model source(s): tools and units test them"))
    for p in paths:
        machine = model_asset_id(p)
        if machine is not None:
            ids.setdefault(machine, []).append(p)
    for machine, owned in sorted(ids.items()):
        hits = referenced(machine, ctx.head, ctx.cwd)
        if hits:
            claims.append(Claim(owned, EVERYTHING, f"{', '.join(owned)}: {machine} is named by "
                                + ", ".join(hits[:5]) + (f" and {len(hits) - 5} more" if len(hits) > 5 else "")))
        else:
            claims.append(Claim(owned, NOTHING, f"{', '.join(owned)}: {machine}'s model, which nothing shipped names"))
    return claims


def rule_atlas_classes(ctx: Context, paths: list[str]) -> list[Claim]:
    mine = [p for p in paths if p.startswith(PACKTESTS)]
    if not mine:
        return []
    scenario = [p for p in mine if "/" not in p[len(PACKTESTS):] and p.endswith(".cs")]
    claims = [Claim([p], EVERYTHING, f"{p}: not a scenario file, which every shard may read")
              for p in mine if p not in scenario]
    if not scenario:
        return claims
    classes, problems, methods = set(), [], {}
    for rev in (ctx.base, ctx.head):
        tree = ctx.scenario_tree(rev)
        got, bad = scenario_classes(tree, scenario)
        classes |= got
        problems += [f"at {rev}: {b}" for b in bad]
        for cls, fqns in tree.test_classes().items():
            if cls in got:
                methods.setdefault(cls, set()).update(fqns)
    if problems:
        return claims + [Claim(scenario, EVERYTHING, "; ".join(problems))]
    shards, unmatched = set(), []
    for cls in sorted(classes):
        for fqn in sorted(methods[cls]):
            hit = shards_selecting(fqn, ctx.shards)
            if not hit:
                unmatched.append(fqn)
            shards |= set(hit)
    if unmatched:
        return claims + [Claim(scenario, EVERYTHING, "no shard's filter selects "
                               + ", ".join(unmatched[:5]) + " (ci/atlas-shards.json is missing a class)")]
    return claims + [Claim(scenario, Need(frozenset(shards)),
                           f"{len(scenario)} scenario file(s): classes {', '.join(sorted(classes))}"
                           f" -> shards {', '.join(sorted(shards))}")]


# The recipe browser (site/): tools runs svelte-check and its unit tests, and the site job
# its end-to-end tests. Nothing else reads it (smoke, Atlas, cairn and the export job read
# nothing under site/; pages.yml deploys it after merge), and it reads the export the way
# its own code says, so any export of the current schema tests it: main's, when nothing else
# in the change plans smoke. What could change the export or its schema (the exporter,
# schema/, tools/site-data, the mods, pack/) is not site/, and plans smoke on its own claim
# (or everything), so the site then reads this run's export. The model viewer's paths the
# models rule claimed first.
SITE = re.compile(r"site/.+")


def rule_site(ctx: Context, paths: list[str]) -> list[Claim]:
    hits = [p for p in paths if SITE.fullmatch(p)]
    return [Claim(hits, Need(site_main_export=True),
                  f"{len(hits)} site path(s): tools checks them, and the end-to-end tests need any current export")
            ] if hits else []


# Tooling read by the tools job and by nothing smoke, Atlas, cairn or the site loads
# (each traced: ci.yml, .github/actions/setup-pack, tests/PackTests' csproj and scenarios,
# packtool.py and what it imports), each with what else reads it:
TOOLING: list[tuple[re.Pattern, Need, str]] = [
    # The rusty gear check also runs in the `rest` shard (it needs the game and the mods).
    (re.compile(r"tools/tests/test_gear_consumers\.py"), Need(frozenset({"rest"})),
     "the rusty gear check, which the rest shard runs too"),
    # Python tool tests: the tools job runs them all, and nothing imports them.
    (re.compile(r"tools/tests/[^/]+\.py"), NOTHING, "Python tool tests, which the tools job runs"),
    # Local-only mods, never shipped: the rest shard builds and tests them (their Python
    # halves' tests are the tools job's), and no other job, csproj or packtool step reads them.
    (re.compile(r"tools/(?:icon-export|ore-survey)/.+"), Need(frozenset({"rest"})),
     "a local-only mod, which the rest shard builds and tests"),
    # The export job's drift reports run these on smoke's export (and pages.yml after merge).
    (re.compile(r"tools/(?:icons|textures)\.py"), Need(smoke=True),
     "an icon or texture store tool, which the export job runs on smoke's export"),
    # The export job checks the shipped item value table against a rebuild from smoke's export.
    (re.compile(r"tools/item-values/.+"), Need(smoke=True),
     "the item value builder, which the export job runs on smoke's export"),
    # The export job validates smoke's export with it (pages.yml migrates and assembles with
    # it after merge); the site's code never imports it, and Atlas validates with the schema.
    (re.compile(r"tools/site-data/.+"), Need(smoke=True),
     "site-data, which the export job validates smoke's export with"),
    # The icon store: the tools job verifies its index (on LFS pointers), the export job's
    # drift only warns, and pages.yml deploys it after merge. Not textures/: Atlas reads its
    # index (RecipeExportEidolonFittingsScenarios) and prepare-data its images.
    (re.compile(r"icons/.+"), NOTHING, "the icon store, which the tools job verifies"),
]


def rule_tooling(ctx: Context, paths: list[str]) -> list[Claim]:
    claims, left = [], list(paths)
    for pattern, need, why in TOOLING:
        hits = [p for p in left if pattern.fullmatch(p)]
        if hits:
            claims.append(Claim(hits, need, f"{len(hits)} path(s), {why}"))
            left = [p for p in left if p not in hits]
    return claims


RULES = [rule_noop, rule_units, rule_models, rule_atlas_classes, rule_site, rule_tooling]

# The `rest` shard also builds and tests the local-only icon export and ore survey mods,
# and runs the rusty gear check, none of which any other job does. rule_tooling claims the
# mods and the check with `rest`; whatever else changes their inputs (the lock, a gear
# consumers patch) keeps `rest` in a sliced plan through this.
REST_STEP_INPUTS = re.compile(r"tools/icon-export/|tools/ore-survey/|tools/tests/test_gear_consumers\.py$"
                              r"|pack/lock\.json$|" + re.escape(_ASSETS) + r"patches/gearconsumers-")


def plan(base: str, head: str, cwd: str | None = None, shards: list[dict] | None = None,
         paths: list[str] | None = None) -> dict:
    """The plan for a PR whose change is `base`..`head` (the module docstring)."""
    ctx = Context(base, head, cwd, shards if shards is not None else load_shards())
    paths = changed(base, head, cwd) if paths is None else paths
    if not paths:
        return full_plan(["no changed paths"])
    need, reasons, left = NOTHING, [], list(paths)
    for rule in RULES:
        if not left:
            break
        for claim in rule(ctx, left):
            need |= claim.need
            reasons.append(f"{rule.__name__[5:]}: {claim.reason}")
            left = [p for p in left if p not in claim.paths]
    for p in left:
        need |= EVERYTHING
        reasons.append(f"unclaimed: {p}: no rule knows what reads it")
    if need.atlas != ALL and any(REST_STEP_INPUTS.match(p) for p in paths):
        need |= Need(frozenset({"rest"}))
        reasons.append("rest: the icon export, ore survey or rusty gear inputs changed")
    return as_plan(need, reasons, ctx.shards)


def as_plan(need: Need, reasons: list[str], shards: list[dict]) -> dict:
    names = [s["shard"] for s in shards]
    atlas = names if need.atlas == ALL else [n for n in names if n in need.atlas]
    unknown = set() if need.atlas == ALL else set(need.atlas) - set(names)
    if unknown:
        raise ValueError(f"shards {sorted(unknown)} are not in {SHARDS_FILE.name}")
    # The site's end-to-end tests read the export smoke writes, or, when nothing plans smoke
    # and they need no fresh one (site_main_export), main's latest (the site job's fallback).
    smoke = need.smoke or need.site
    site = need.site or need.site_main_export
    if site and not smoke:
        reasons = [*reasons, "site: no smoke planned, so the end-to-end tests read main's latest recipe export"]
    sliced = len(atlas) < len(names) or not (smoke and need.cairn and site)
    return {"sliced": sliced, "atlas": ALL if len(atlas) == len(names) else atlas,
            "smoke": smoke, "cairn": need.cairn, "site": site, "reasons": reasons}


def full_plan(reasons: list[str], shards: list[dict] | None = None) -> dict:
    return as_plan(EVERYTHING, reasons, shards if shards is not None else load_shards())


def outputs(p: dict, shards: list[dict] | None = None) -> dict[str, str]:
    """The `tree` job's outputs for plan `p`. atlas_matrix is never empty (GitHub Actions
    rejects an empty matrix even for a job that will be skipped): with no shard planned it
    is the full list, and `atlas` is false."""
    shards = shards if shards is not None else load_shards()
    want = [s["shard"] for s in shards] if p["atlas"] == ALL else p["atlas"]
    unknown = set(want) - {s["shard"] for s in shards}
    if unknown:
        raise ValueError(f"plan names shards {sorted(unknown)} that {SHARDS_FILE.name} lacks")
    chosen = [s for s in shards if s["shard"] in want] or shards
    flag = lambda b: "true" if b else "false"  # noqa: E731
    return {"sliced": flag(p["sliced"]), "smoke": flag(p["smoke"]), "cairn": flag(p["cairn"]),
            "site": flag(p["site"]), "atlas": flag(bool(want)),
            "atlas_matrix": json.dumps({"include": chosen}, separators=(",", ":"))}


def main(argv: list[str]) -> int:
    if argv[:1] == ["noop-regex"] and len(argv) == 1:
        print(NOOP)
        return 0
    if argv[:1] == ["plan"] and len(argv) == 3:
        try:
            p = plan(argv[1], argv[2])
        except Exception as e:  # noqa: BLE001 - any failure plans the full suite
            p = full_plan([f"error: {type(e).__name__}: {e}"])
        print(json.dumps(p, indent=2))
        return 0
    if argv[:1] == ["outputs"] and len(argv) == 2:
        p = full_plan(["full suite"]) if argv[1] == "full" else json.loads(Path(argv[1]).read_text())
        for k, v in outputs(p).items():
            print(f"{k}={v}")
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
