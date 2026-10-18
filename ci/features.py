"""The feature map's reading, for the planner's feature rules (ci/paths.py; docs/merge-queue.md,
"Sliced PR runs"; ci/atlas-shards.md, "Feature slicing").

ci/features.json maps each feature of the pack's own mod to what is its own: its code (folders and
top-level files under mods-src/seraphhorizons/), its assets (by asset code: a path segment under
assets/seraphhorizons/ that is the code or starts with it and `-`, `_` or `.`, plus globs), its lang
keys (the same codes against a key's stem, plus globs), its exporter files under
tools/recipe-export/, its ModConfig switches, and the Atlas test classes (names or globs) that
exercise it without naming its code. Paths the map gives no feature (`global`, the shared files
below, anything unlisted) are never claimed: they run everything.

What a change to a feature runs is partly worked out, not listed: its code's dependents, file by
file. A C# file reaches another only through the names of its top-level types (or an extension
method), and only where that file can see them: in the same namespace or one inside it, through a
using, or by a qualified name (`CodeFile.sees`). So a changed code file reaches every file that
names one of its types where it can see it, one step (`Code.dependents`: transitively, a type
every feature names, a handbook guide or the machines' shared parts, takes every change
everywhere), and every scenario file using a scenario file reached, transitively. A changed
asset, lang key, switch or exporter file reaches the scenario files naming its feature's types.
What runs is the always-on shards, the test classes declared in the scenario files reached, and
every class the map lists for the features changed and for those whose code a change reaches.

Four files every feature edits are read hunk by hunk (`attribute_shared`): lang/en.json and the
config defaults snapshot's seraphhorizons.json by the keys whose value changed, SeraphHorizons.cs
and SwitchRegistry.cs by the statement each changed line belongs to. A file is attributed only
when every change in it is: one key or statement the map cannot place leaves it to run
everything.

Stdlib only; needs git.
"""

from __future__ import annotations

import fnmatch
import json
import re
import subprocess
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MAP_FILE = ROOT / "ci" / "features.json"

MOD = "mods-src/seraphhorizons/"
ASSETS = MOD + "assets/seraphhorizons/"
EXPORT = "tools/recipe-export/"
PACKTESTS = "tests/PackTests/"
LANG = ASSETS + "lang/en.json"
SNAPSHOTS = ASSETS + "config/configdefaults/"
SNAPSHOT_FILE = re.compile(re.escape(SNAPSHOTS) + r"[^/]+/files/seraphhorizons\.json")
# The C# files every feature adds a line to: the switches and their dispatch, which switch owns
# what, and the exporter's list of sections.
SHARED_CS = (MOD + "SeraphHorizons.cs", MOD + "SwitchRegistry.cs", MOD + "Core/SwitchOwnership.cs",
             EXPORT + "RecipeSection.cs")
# A mod file under these is not the mod's code: tests/ and benchmarks/ are projects of their own, never
# in the mod, a machine's tools/ its Python generator.
_NOT_CODE = re.compile(re.escape(MOD) + r"(?:tests/|assets/|benchmarks/|[^/]+/tools/)")
# The leading parts of a lang key that say what kind of text it is, not whose.
_LANG_KINDS = {"block", "blockhelp", "blockdesc", "item", "itemdesc", "heldhelp", "creature", "dead",
               "prefixandcreature"}
_FEATURE_KEYS = {"code", "codes", "assets", "lang", "export", "switches", "classes"}


@dataclass
class Feature:
    name: str
    code: list[str]
    codes: list[str] = field(default_factory=list)
    assets: list[str] = field(default_factory=list)
    lang: list[str] = field(default_factory=list)
    export: list[str] = field(default_factory=list)
    switches: list[str] = field(default_factory=list)
    classes: list[str] = field(default_factory=list)

    def owns_code(self, rel: str) -> bool:
        """`rel`, a path under mods-src/seraphhorizons/, is this feature's code."""
        return any(rel.startswith(c) if c.endswith("/") else rel == c for c in self.code)

    def owns_asset(self, rel: str) -> bool:
        """`rel`, a path under assets/seraphhorizons/, is this feature's asset."""
        if any(fnmatch.fnmatchcase(rel, g) for g in self.assets):
            return True
        return any(_code_matches(c, re.sub(r"\.[a-z0-9]+$", "", seg))
                   for seg in rel.split("/")[1:] for c in self.codes)

    def owns_lang(self, key: str) -> bool:
        key = key.split(":", 1)[1] if re.match(r"[a-z0-9]+:", key) else key
        if any(fnmatch.fnmatchcase(key, g) for g in self.lang):
            return True
        parts = key.split("-")
        while len(parts) > 1 and parts[0] in _LANG_KINDS:
            parts = parts[1:]
        stem = "-".join(parts)
        return any(_code_matches(c, stem) for c in self.codes)


def _code_matches(code: str, stem: str) -> bool:
    return stem == code or (stem.startswith(code) and stem[len(code)] in "-_.")


@dataclass
class FeatureMap:
    features: dict[str, Feature]
    always: list[str]          # shards every feature-sliced plan runs (ci/atlas-shards.md)
    global_: list[str]         # code under mods-src/seraphhorizons/ that is every feature's
    global_switches: list[str]  # ModConfig settings every feature reads (no feature's: they run everything)
    unmapped: list[str]        # top-level entries knowingly in no feature (no C#: they run everything)

    def owners(self, path: str) -> set[str] | None:
        """The features `path` belongs to, or None if it is not a feature's path (global code, the
        shared files, anything the map does not list)."""
        if path.startswith(ASSETS):
            rel = path[len(ASSETS):]
            if path == LANG or path.startswith(SNAPSHOTS):
                return None
            hits = {f.name for f in self.features.values() if f.owns_asset(rel)}
        elif path.startswith(MOD):
            if path in SHARED_CS or _NOT_CODE.match(path) or self.is_global(path):
                return None
            rel = path[len(MOD):]
            hits = {f.name for f in self.features.values() if f.owns_code(rel)}
        elif path.startswith(EXPORT):
            rel = path[len(EXPORT):]
            hits = {f.name for f in self.features.values() if rel in f.export}
        else:
            return None
        return hits or None

    def is_global(self, path: str) -> bool:
        """`path` is global code: under a `global` entry, and not a file a feature names (Core/
        holds the game-independent rules of several small features beside what is every
        feature's)."""
        rel = path[len(MOD):] if path.startswith(MOD) else None
        if rel is None or any(rel in f.code for f in self.features.values()):
            return False
        return any(rel.startswith(g) if g.endswith("/") else rel == g for g in self.global_)

    def code_owner(self, path: str) -> str | None:
        """The one feature whose code `path` (a .cs file under mods-src/seraphhorizons/) is."""
        rel = path[len(MOD):]
        hits = [f.name for f in self.features.values() if f.owns_code(rel)]
        return hits[0] if len(hits) == 1 else None

    def switch_owners(self, key: str) -> set[str]:
        return {f.name for f in self.features.values() if key in f.switches}

    def lang_owners(self, key: str) -> set[str]:
        return {f.name for f in self.features.values() if f.owns_lang(key)}


def load_map(path: Path | None = None) -> FeatureMap:
    path = path or MAP_FILE
    data = json.loads(path.read_text())
    if set(data) != {"always", "global", "global_switches", "unmapped", "features"}:
        raise ValueError(f"{path}: want always, global, global_switches, unmapped and features, got {sorted(data)}")
    features = {}
    for name, spec in data["features"].items():
        if not set(spec) <= _FEATURE_KEYS or "code" not in spec:
            raise ValueError(f"{path}: {name}: keys {sorted(set(spec) - _FEATURE_KEYS)} unknown, or no code")
        features[name] = Feature(name, **spec)
    return FeatureMap(features, data["always"], data["global"], data["global_switches"], data["unmapped"])


# ----------------------------------------------------------------- reading the code

_COMMENT_BLOCK = re.compile(r"/\*.*?\*/", re.S)
_IDENT = re.compile(r"[A-Za-z_]\w*")
_NAMESPACE = re.compile(r"^namespace\s+([\w.]+)\s*;", re.M)
_USING = re.compile(r"^\s*(?:global\s+)?using\s+(?:static\s+)?(?:\w+\s*=\s*)?([\w.]+)\s*;", re.M)
_TOP_TYPE = re.compile(r"^(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|file|sealed|abstract|static|partial"
                       r"|readonly|unsafe|new|ref)\s+)*(?:class|struct|interface|enum|delegate\s+[\w<>\[\],.?\s]+?"
                       r"|record(?:\s+(?:class|struct))?)\s+([A-Za-z_]\w*)", re.M)
_EXTENSION = re.compile(r"\bstatic\b[^;{}=()]*?\b([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(\s*this\s")


def strip_comments(text: str) -> str:
    """The code of a C# file less what is certainly a comment: block comments, lines that start
    with `//`, and the tail from a `//` with no quote before it on its line."""
    text = _COMMENT_BLOCK.sub(" ", text)
    out = []
    for line in text.splitlines():
        if line.lstrip().startswith("//"):
            out.append("")
            continue
        cut = line.find("//")
        if cut >= 0 and '"' not in line[:cut] and "'" not in line[:cut]:
            line = line[:cut]
        out.append(line)
    return "\n".join(out)


@dataclass
class CodeFile:
    namespace: str
    types: set[str]             # top-level types (a file-scoped namespace's types are at column 0)
    extensions: set[str]        # extension methods it declares
    usings: set[str]            # namespaces (or namespace.Type, for using static and aliases) it uses
    tokens: set[str]            # identifiers its code names
    text: str                   # its code, comments stripped

    def sees(self, namespace: str, name: str) -> bool:
        """Whether this file can name the top-level type (or extension method) `name` declared in
        `namespace`: from that namespace or one inside it, through a using, or qualified."""
        if name not in self.tokens:
            return False
        if self.namespace == namespace or self.namespace.startswith(namespace + "."):
            return True
        if namespace in self.usings or f"{namespace}.{name}" in self.usings:
            return True
        segs = namespace.split(".")
        suffixes = "|".join(re.escape(".".join(segs[i:])) for i in range(len(segs)))
        return re.search(rf"(?<![\w])(?:{suffixes})\.{re.escape(name)}\b", self.text) is not None


def read_code(text: str) -> CodeFile:
    code = strip_comments(text)
    ns = _NAMESPACE.search(code)
    return CodeFile(ns.group(1) if ns else "", set(_TOP_TYPE.findall(code)), set(_EXTENSION.findall(code)),
                    set(_USING.findall(code)), set(_IDENT.findall(code)), code)


def _git(*args: str, cwd: str | None = None, input: bytes | None = None) -> subprocess.CompletedProcess:
    return subprocess.run(["git", *args], cwd=cwd, capture_output=True, input=input)


def show(rev: str, path: str, cwd: str | None = None) -> str | None:
    """`path` at `rev`, or None if it is not there."""
    r = _git("cat-file", "-e", f"{rev}:{path}", cwd=cwd)
    if r.returncode != 0:
        return None
    r = _git("show", f"{rev}:{path}", cwd=cwd)
    if r.returncode != 0:
        raise RuntimeError(f"git show {rev}:{path} failed: {r.stderr.decode(errors='replace').strip()}")
    return r.stdout.decode("utf-8", errors="replace")


def is_code(path: str) -> bool:
    """A C# file the dependency reading covers: the pack's own mod's code, a scenario file, or the
    exporter."""
    if not path.endswith(".cs"):
        return False
    if path.startswith(PACKTESTS):
        return "/" not in path[len(PACKTESTS):]
    return path.startswith(EXPORT) or (path.startswith(MOD) and not _NOT_CODE.match(path))


@dataclass
class Code:
    """Every C# file the dependency reading covers, at one revision, read."""
    files: dict[str, CodeFile]

    def __post_init__(self):
        self._index: dict[str, list[str]] = {}
        for p, f in self.files.items():
            for t in f.tokens:
                self._index.setdefault(t, []).append(p)

    def dependents(self, seeds: dict[str, CodeFile], leaves: set[str] = frozenset()) -> set[str]:
        """The files that changing `seeds` (path -> the file as read, at either revision) may
        change the behaviour of: the seeds, every file that names one of their top-level types or
        extension methods where it can see it, and, transitively, the scenario files that use a
        scenario file reached (a helper, a base class). The mod's own code is followed one step
        only: a type every feature names (a handbook guide, the machines' shared parts) would
        otherwise take every change everywhere. The names in `leaves` are not followed (the test
        classes: one part of a partial class names the class, and no other class uses a test
        class)."""
        out = set(seeds)
        frontier = list(seeds.items())
        while frontier:
            path, f = frontier.pop()
            for name in (f.types | f.extensions) - leaves:
                for q in self._index.get(name, ()):
                    if q not in out and self.files[q].sees(f.namespace, name):
                        out.add(q)
                        if q.startswith(PACKTESTS):
                            frontier.append((q, self.files[q]))
        return out


def read_tree(rev: str, cwd: str | None = None) -> Code:
    ls = _git("ls-tree", "-r", "--name-only", "-z", rev, "--", MOD, PACKTESTS, EXPORT, cwd=cwd)
    if ls.returncode != 0:
        raise RuntimeError(f"git ls-tree {rev} failed: {ls.stderr.decode(errors='replace').strip()}")
    names = [p for p in ls.stdout.decode().split("\0") if p and is_code(p)]
    if not names:
        return Code({})
    r = _git("cat-file", "--batch", cwd=cwd, input="".join(f"{rev}:{p}\n" for p in names).encode())
    if r.returncode != 0:
        raise RuntimeError(f"git cat-file failed: {r.stderr.decode(errors='replace').strip()}")
    out, data, pos = {}, r.stdout, 0
    for p in names:
        nl = data.index(b"\n", pos)
        header = data[pos:nl].decode().split()
        if len(header) != 3 or header[1] != "blob":
            raise RuntimeError(f"git cat-file: {rev}:{p}: {' '.join(header)}")
        size = int(header[2])
        out[p] = read_code(data[nl + 1:nl + 1 + size].decode("utf-8", errors="replace"))
        pos = nl + 1 + size + 1
    return Code(out)


# ----------------------------------------------------------------- the shared files, hunk by hunk


def _json_pairs(text: str) -> dict:
    """A JSON object's members, refusing a key given twice (which of the two counts is the
    reader's choice, not this one's)."""
    def pairs(items):
        keys = [k for k, _ in items]
        if len(set(keys)) != len(keys):
            raise ValueError("a key given twice")
        return dict(items)
    data = json.loads(text, object_pairs_hook=pairs)
    if not isinstance(data, dict):
        raise ValueError("not a JSON object")
    return data


def changed_keys(old: str | None, new: str | None) -> set[str]:
    """The top-level keys added, removed or given another value between two JSON objects."""
    a = _json_pairs(old) if old is not None else {}
    b = _json_pairs(new) if new is not None else {}
    return {k for k in a.keys() | b.keys() if a.get(k, ...) != b.get(k, ...)}


_HUNK = re.compile(r"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")


def changed_lines(diff: str) -> tuple[list[int], list[int]]:
    """(removed, added): the 1-based line numbers a `git diff -U0` removes from the old file and
    adds to the new."""
    removed, added = [], []
    for line in diff.splitlines():
        m = _HUNK.match(line)
        if m:
            o, oc, n, nc = int(m.group(1)), int(m.group(2) or 1), int(m.group(3)), int(m.group(4) or 1)
            removed += range(o, o + oc)
            added += range(n, n + nc)
    return removed, added


def _is_comment_or_attribute(s: str) -> bool:
    """A stripped line that is a comment, or an attribute (`[Fact]`, not a collection's `[A, B],`)."""
    return s.startswith(("//", "/*", "*")) or s.endswith("*/") or (s.startswith("[") and s.endswith("]"))


_BRACES = ("{", "}", "};", "},", "});", "]", "];", "],")


def statement(lines: list[str], i: int) -> list[str] | None:
    """The statement or declaration line `i` (0-based) belongs to, as its lines: an attribute
    belongs to the declaration below it; a statement runs back to the line after one that ends in
    `;`, `{`, `}` or `,` (an entry in a list, an argument on a line of its own) and on to the line
    that does. None for a blank line, a brace on its own, or an attribute with nothing below it."""
    j = i
    while j < len(lines) and (not lines[j].strip() or _is_comment_or_attribute(lines[j].strip())):
        j += 1
    if j == len(lines) or lines[j].strip() in _BRACES:
        return None
    start = j
    while start > 0:
        prev = lines[start - 1].strip()
        if not prev or _is_comment_or_attribute(prev) or prev.endswith((";", "{", "}", ",")):
            break
        start -= 1
    end = j
    while end < len(lines) - 1 and not lines[end].rstrip().endswith((";", "{", "}", ",")) and end - start < 30:
        end += 1
    return lines[start:end + 1]


def _code_above(lines: list[str], i: int) -> int | None:
    """The nearest line above `i` that is code (not blank, not a comment or an attribute)."""
    for j in range(i - 1, -1, -1):
        s = lines[j].strip()
        if s and not _is_comment_or_attribute(s):
            return j
    return None


def header(lines: list[str], i: int) -> list[str] | None:
    """The header of the block line `i` is in, or of the block a brace on line `i` opens or closes:
    the statement that opens it (an `if`, a `foreach`, a method, a list's declaration), or a
    braceless `if`'s condition."""
    s = lines[i].strip()
    if s.startswith(("}", "]")):
        depth = 0
        for j in range(i, -1, -1):
            code = strip_comments(lines[j])
            depth += code.count("}") + code.count("]") - code.count("{") - code.count("[")
            if depth <= 0 and j < i:
                return header_of_open(lines, j)
        return None
    if s.startswith(("{", "[")) and s.rstrip(",;") in ("{", "["):
        return header_of_open(lines, i)
    indent = len(lines[i]) - len(lines[i].lstrip())
    for j in range(i - 1, -1, -1):
        t = lines[j].strip()
        if not t or t.startswith(("//", "/*", "*")):
            continue
        if len(lines[j]) - len(lines[j].lstrip()) < indent:
            return header_of_open(lines, j) if t in ("{", "[") else statement(lines, j)
    return None


def header_of_open(lines: list[str], j: int) -> list[str] | None:
    """The statement a block opened on line `j` belongs to: that line, or the code above a lone brace."""
    if lines[j].strip() in ("{", "["):
        k = _code_above(lines, j)
        return statement(lines, k) if k is not None else None
    return statement(lines, j)


def _tokens(unit: list[str]) -> set[str]:
    return set(_IDENT.findall(strip_comments("\n".join(unit))))


@dataclass
class Names:
    """What a C# token says about features: a switch, a feature's top-level type or namespace
    segment (features), or global code's (blocks attribution)."""
    features: dict[str, set[str]]
    global_: set[str]

    def of(self, tokens: set[str]) -> set[str] | None:
        """The features `tokens` name, or None if they name global code."""
        if tokens & self.global_:
            return None
        return set().union(*(self.features.get(t, set()) for t in tokens))


_METHOD = re.compile(r"^\s*(?:(?:public|private|internal|protected|static|partial|async|override|virtual)\s+)+"
                     r"[\w<>\[\],.?]+(?:\s*<[^<>()]*>)?\s+([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(", re.M)


def names(fmap: FeatureMap, code: Code) -> Names:
    feats: dict[str, set[str]] = {}
    for f in fmap.features.values():
        for s in f.switches:
            feats.setdefault(s, set()).add(f.name)
        # The exporter's sections are methods of one partial class (RecipeSection.cs calls FillJig).
        for rel in f.export:
            cf = code.files.get(EXPORT + rel)
            for n in (cf.types | set(_METHOD.findall(cf.text))) if cf else ():
                feats.setdefault(n, set()).add(f.name)
    glob = set()
    for p, cf in code.files.items():
        if not p.startswith(MOD) or p in SHARED_CS:
            continue
        if fmap.is_global(p):
            glob |= cf.types
            seg = cf.namespace.split(".")[2:3]
            glob |= {s for s in seg if s != "Core"}
            continue
        owner = fmap.code_owner(p)
        if owner is None:
            continue
        for t in cf.types:
            feats.setdefault(t, set()).add(owner)
        for seg in cf.namespace.split(".")[2:3]:
            if seg != "Core":
                feats.setdefault(seg, set()).add(owner)
    return Names(feats, glob)


def attribute_cs(old: str | None, new: str | None, diff: str, nm: Names) -> tuple[set[str] | None, str]:
    """(features, why): the features every changed line of a C# file belongs to, or None (and the
    line it could not place) if a line belongs to no feature or to global code. A line belongs to
    the features its statement names (`statement`); a line whose statement names none (a brace,
    `_harmony ??= new Harmony(HarmonyId);`) to those its block's header names (`header`). A blank
    line or a `//` comment changes nothing the game runs and belongs to nothing."""
    if old is None or new is None:
        return None, "added or deleted, not edited"
    removed, added = changed_lines(diff)
    found: set[str] = set()
    for text, numbers in ((old, removed), (new, added)):
        lines = text.splitlines()
        for n in numbers:
            if n - 1 >= len(lines):
                return None, f"line {n} is past the file's end"
            s = lines[n - 1].strip()
            if not s or s.startswith("//"):
                continue
            hit: set[str] | None = set()
            for unit in (statement(lines, n - 1), header(lines, n - 1)):
                if unit is not None:
                    hit = nm.of(_tokens(unit))
                    if hit is None or hit:
                        break
            if not hit:
                return None, (f"line {n} ({s[:60]!r}) names no feature"
                              + (" (it names global code)" if hit is None else ""))
            found |= hit
    return found, ""


def attribute_shared(path: str, base: str, head: str, fmap: FeatureMap, nm: Names,
                     cwd: str | None = None) -> tuple[set[str] | None, str]:
    """(features, why) for one of the shared files: the features its change belongs to, or None
    and why not."""
    old, new = show(base, path, cwd), show(head, path, cwd)
    if path == LANG or SNAPSHOT_FILE.fullmatch(path):
        if old is None or new is None:
            return None, "added or deleted, not edited"
        try:
            keys = changed_keys(old, new)
        except ValueError as e:
            return None, f"cannot read it as one JSON object ({e})"
        owners = fmap.lang_owners if path == LANG else fmap.switch_owners
        found = set()
        for k in sorted(keys):
            hit = owners(k)
            if not hit:
                return None, f"key {k!r} is no feature's"
            found |= hit
        return found, ""
    if path in SHARED_CS:
        r = _git("diff", "-U0", "--no-renames", base, head, "--", path, cwd=cwd)
        if r.returncode != 0:
            raise RuntimeError(f"git diff failed: {r.stderr.decode(errors='replace').strip()}")
        return attribute_cs(old, new, r.stdout.decode("utf-8", errors="replace"), nm)
    return None, "not a shared file"


def is_shared(path: str) -> bool:
    return path == LANG or path in SHARED_CS or SNAPSHOT_FILE.fullmatch(path) is not None
