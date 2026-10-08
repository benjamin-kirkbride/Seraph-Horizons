#!/usr/bin/env python3
"""Config defaults snapshots: what a fresh server of the pack writes into ModConfig.

docs/config-defaults.md has the design. Each pack version (pack.toml's [pack] version) has a
snapshot in the pack's own mod, so the mod carries the defaults' history and can move a setting a
player never changed to the new default when a mod (or the pack) changes it:

    mods-src/seraphhorizons/assets/seraphhorizons/config/configdefaults/<version>/
        index.json      the mods and versions, each file's format and owners, the pack's own
                        values (pack/config/ModConfig) and the renames (pack/config-defaults.toml)
        files/<name>    each ModConfig file byte for byte, as the mods write it

`packtool smoke --config-defaults write` boots a fresh server with every locked mod and this
tree's build of the pack's own mod in capture mode (it changes nothing) and writes the current
version's snapshot from its ModConfig; `--config-defaults check` (CI's smoke job) fails when the
committed snapshot differs. A released version's snapshot (tag v<version>) is frozen. Stdlib only.
"""

from __future__ import annotations

import difflib
import json
import re
import shutil
import subprocess
import tomllib
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SNAPSHOTS = ROOT / "mods-src" / "seraphhorizons" / "assets" / "seraphhorizons" / "config" / "configdefaults"
SETTINGS = ROOT / "pack" / "config-defaults.toml"
PACK_VALUES = ROOT / "pack" / "config" / "ModConfig"
PACK_TOML = ROOT / "pack" / "pack.toml"
LOCK_JSON = ROOT / "pack" / "lock.json"
PACK_MOD_INFO = ROOT / "mods-src" / "seraphhorizons" / "modinfo.json"
PACK_MOD_ID = "seraphhorizons"

CAPTURE_VARIABLE = "SERAPH_CONFIG_DEFAULTS"  # ConfigDefaultsSystem.CaptureVariable
CAPTURE_REPORT = "configdefaults-capture.json"  # ConfigDefaultsSystem.CaptureReport
INDEX_FORMAT = 1  # SnapshotIndex.FormatVersion
REGENERATE = "VINTAGE_STORY=<server> python3 tools/packtool.py smoke --config-defaults write"


class ConfigDefaultsError(Exception):
    pass


# --------------------------------------------------------------------- inputs


def pack_version() -> str:
    with PACK_TOML.open("rb") as f:
        return tomllib.load(f)["pack"]["version"]


def parse_modinfo(raw: str) -> dict:
    """modinfo.json as Newtonsoft reads it: keys in any case, comments, trailing commas."""
    raw = re.sub(r"^\s*//.*$", "", raw, flags=re.M)
    raw = re.sub(r",(\s*[}\]])", r"\1", raw)
    return {k.lower(): v for k, v in json.loads(raw).items()}


def pack_mods() -> dict[str, str]:
    """modid -> version: the lock's mods and the pack's own mod at this tree's version."""
    lock = json.loads(LOCK_JSON.read_text())
    mods = {m["id"]: m["version"] for m in lock["mods"]}
    mods[PACK_MOD_ID] = parse_modinfo(PACK_MOD_INFO.read_text(encoding="utf-8-sig"))["version"]
    return dict(sorted(mods.items()))


def load_settings(path: Path = SETTINGS) -> dict:
    """pack/config-defaults.toml: ignored files, owners by hand, renames."""
    if not path.exists():
        return {"ignore": {}, "owners": {}, "renames": []}
    with path.open("rb") as f:
        raw = tomllib.load(f)
    ignore = {}
    for entry in raw.get("ignore", []):
        if not entry.get("file") or not entry.get("why"):
            raise ConfigDefaultsError(f"{path.name}: every [[ignore]] needs a file and a why")
        ignore[entry["file"]] = entry["why"]
    owners = {}
    for name, ids in raw.get("owners", {}).items():
        if not isinstance(ids, list) or not ids or not all(isinstance(i, str) for i in ids):
            raise ConfigDefaultsError(f"{path.name}: owners of {name} must be a list of modids")
        owners[name] = ids
    renames = []
    for entry in raw.get("rename", []):
        frm, to = entry.get("from"), entry.get("to")
        if not entry.get("file") or not isinstance(frm, list) or not frm or not isinstance(to, str) or not to:
            raise ConfigDefaultsError(f"{path.name}: every [[rename]] needs file, from (a list of keys) and to (a key)")
        if not entry.get("why"):
            raise ConfigDefaultsError(f"{path.name}: [[rename]] of {entry['file']} {'.'.join(frm)} needs a why")
        renames.append({"file": entry["file"], "from": frm, "to": to})
    return {"ignore": ignore, "owners": owners, "renames": renames}


YAML_SCALAR = re.compile(r"^([A-Za-z0-9_-]+):\s+(.+?)\s*$")


def flat_yaml(path: Path) -> dict:
    """Top-level `key: scalar` lines only, the one shape ConfigKit's files have: key -> value text.

    Not a YAML parser: anything else is an error, so a value never ships as something other than
    what the file says.
    """
    out: dict = {}
    for n, line in enumerate(path.read_text().splitlines(), 1):
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        m = YAML_SCALAR.match(line)
        if not m:
            raise ConfigDefaultsError(f"{path}:{n}: expected `key: value`")
        key, raw = m.groups()
        if key in out:
            raise ConfigDefaultsError(f"{path}:{n}: {key} is set twice")
        # The mod's own marker of its file's shape (ConfigKit starts the file over when it differs).
        if key.lower() == "version":
            raise ConfigDefaultsError(f"{path}:{n}: don't set version; it is the mod's")
        if not (raw in ("true", "false") or re.fullmatch(r"-?\d+(\.\d+)?", raw)
                or re.fullmatch(r'"[^"\\]*"', raw)):
            raise ConfigDefaultsError(f"{path}:{n}: {key} must be true, false, a number or a \"quoted string\"")
        out[key] = raw
    return out


def yaml_value(raw: str):
    """A flat_yaml value's text as the value it stands for."""
    if raw in ("true", "false"):
        return raw == "true"
    if re.fullmatch(r"-?\d+", raw):
        return int(raw)
    if re.fullmatch(r"-?\d+\.\d+", raw):
        return float(raw)
    return raw[1:-1]


def _leaves(obj, path=()):
    if isinstance(obj, dict) and obj:
        for k, v in obj.items():
            yield from _leaves(v, path + (k,))
    else:
        yield path, obj


def pack_values(base: Path = PACK_VALUES) -> list[dict]:
    """pack/config/ModConfig/**/*.{json,yaml}: the values the pack sets instead of a mod's own
    default, each {file, path, value} with the value's text as the file writes it. A .json file
    sets each leaf of its object; a .yaml file each `key: scalar` line."""
    if not base.exists():
        return []
    out = []
    for f in sorted(p for p in base.rglob("*") if p.is_file()):
        name = f.relative_to(base).as_posix()
        if f.suffix in (".yaml", ".yml"):
            for key, raw in flat_yaml(f).items():
                out.append({"file": name, "path": [key], "value": raw})
        elif f.suffix == ".json":
            doc = json.loads(f.read_text())
            if not isinstance(doc, dict):
                raise ConfigDefaultsError(f"{f}: must be an object of the settings to set")
            for path, value in _leaves(doc):
                if path and path[0].lower() == "version" and len(path) == 1:
                    raise ConfigDefaultsError(f"{f}: don't set version; it is the mod's")
                out.append({"file": name, "path": list(path), "value": json.dumps(value)})
        else:
            raise ConfigDefaultsError(f"{f}: only .json and .yaml files hold pack values")
    return out


# --------------------------------------------------------------------- owners


def mod_blobs(mods_dir: Path) -> dict[str, bytes]:
    """modid (lower case) -> the bytes of every .dll the staged mod has (zip or folder)."""
    blobs: dict[str, bytes] = {}
    for item in sorted(mods_dir.iterdir()) if mods_dir.exists() else []:
        try:
            if item.is_dir():
                info_file = item / "modinfo.json"
                if not info_file.exists():
                    continue
                info = parse_modinfo(info_file.read_text(encoding="utf-8-sig"))
                data = b"".join(p.read_bytes() for p in sorted(item.rglob("*.dll")))
            elif item.suffix.lower() == ".zip":
                with zipfile.ZipFile(item) as z:
                    names = z.namelist()
                    info_name = next((n for n in names if n.lower() == "modinfo.json"), None)
                    if info_name is None:
                        continue
                    info = parse_modinfo(z.read(info_name).decode("utf-8-sig"))
                    data = b"".join(z.read(n) for n in sorted(names) if n.lower().endswith(".dll"))
            else:
                continue
        except (OSError, ValueError, zipfile.BadZipFile):
            continue
        modid = info.get("modid")
        if isinstance(modid, str):
            blobs[modid.lower()] = blobs.get(modid.lower(), b"") + data
    return blobs


def _norm(s: str) -> str:
    return re.sub(r"[^a-z0-9]", "", s.lower())


def guess_owner(name: str, modids: list[str]) -> str | None:
    """By name: the mod whose id the file's (or its top folder's) name starts with (the longest),
    or else the one mod whose id starts with the name."""
    head = name.split("/", 1)[0]
    stem = _norm(head.rsplit(".", 1)[0] if "/" not in name else head)
    if not stem:
        return None
    prefixes = [m for m in modids if _norm(m) and stem.startswith(_norm(m))]
    if prefixes:
        return max(prefixes, key=lambda m: len(_norm(m)))
    longer = [m for m in modids if _norm(m).startswith(stem)]
    return longer[0] if len(longer) == 1 else None


def find_owners(files: list[str], blobs: dict[str, bytes], mods: dict[str, str],
                by_hand: dict[str, list[str]]) -> tuple[dict[str, list[str]], list[str]]:
    """Each file's owners: the mods whose versions decide what it holds.

    In order: pack/config-defaults.toml's [owners]; a ConfigKit .yaml file (named after the
    content mod whose declared settings it holds) is that mod's and ConfigKit's; the mods whose
    code has the file's name in it (its path, its own name, or for a file in a folder the folder's
    name; as a .NET string literal), narrowed to the mod the name points to when that is one of
    them; the mod the name points to (guess_owner). A file none of these places is an error, to
    settle by hand in [owners].
    """
    modids = sorted(mods)
    owners: dict[str, list[str]] = {}
    unplaced = []
    for name in files:
        if name in by_hand:
            owners[name] = by_hand[name]
            continue
        stem = name.rsplit(".", 1)[0]
        if name.endswith(".yaml") and "/" not in name and stem in mods and "configkit" in mods:
            owners[name] = [stem, "configkit"]
            continue
        guess = guess_owner(name, modids)
        candidates = [name, name.rsplit("/", 1)[-1]]
        if "/" in name:
            candidates.append(name.split("/", 1)[0])
        found = None
        for c in dict.fromkeys(candidates):
            needle = c.encode("utf-16-le")
            hits = [m for m in modids if needle in blobs.get(m.lower(), b"")]
            if hits:
                # Several mods with the name in their code: the one it is named after, or else all.
                found = [guess] if guess in hits else hits
                break
        found = found or ([guess] if guess else None)
        if found is None:
            unplaced.append(name)
        else:
            owners[name] = found
    return owners, unplaced


# --------------------------------------------------------------------- snapshot


def captured_files(modconfig: Path, ignore: dict[str, str]) -> dict[str, bytes]:
    out = {}
    for f in sorted(p for p in modconfig.rglob("*") if p.is_file()) if modconfig.exists() else []:
        name = f.relative_to(modconfig).as_posix()
        if name not in ignore:
            out[name] = f.read_bytes()
    return out


def has_setting(data: bytes, fmt: str, path: list[str]) -> bool | None:
    """Whether the file has the setting; None when this light reader cannot tell (lenient JSON)."""
    text = data.decode("utf-8-sig", errors="replace")
    if fmt == "yaml":
        if len(path) != 1:
            return None
        return re.search(rf"^{re.escape(path[0])}:", text, re.M) is not None
    try:
        node = json.loads(text)
    except json.JSONDecodeError:
        return None
    for key in path:
        if not isinstance(node, dict):
            return False
        match = next((k for k in node if k == key), None) or next((k for k in node if k.lower() == key.lower()), None)
        if match is None:
            return False
        node = node[match]
    return True


def build(modconfig: Path, mods_dir: Path, version: str | None = None) -> tuple[dict, dict[str, bytes], list[str]]:
    """The snapshot of a captured ModConfig: (index, files, problems)."""
    settings = load_settings()
    mods = pack_mods()
    files = captured_files(modconfig, settings["ignore"])
    owners, unplaced = find_owners(list(files), mod_blobs(mods_dir), mods, settings["owners"])
    problems = [f"ModConfig/{n}: no mod found that writes it; name its owners in "
                f"pack/config-defaults.toml [owners]" for n in unplaced]
    for name, ids in owners.items():
        for i in ids:
            if i not in mods:
                problems.append(f"ModConfig/{name}: owner {i} is not one of the pack's mods")
    values = pack_values()
    for v in values:
        if v["file"] not in files:
            problems.append(f"pack/config/ModConfig/{v['file']}: a fresh server writes no ModConfig/{v['file']}")
        elif has_setting(files[v["file"]], fmt_of(v["file"]), v["path"]) is False:
            problems.append(f"pack/config/ModConfig/{v['file']}: ModConfig/{v['file']} has no {'.'.join(v['path'])}")
    for r in settings["renames"]:
        if r["file"] not in files:
            problems.append(f"pack/config-defaults.toml: rename in {r['file']}, which a fresh server does not write")
    index = {
        "format": INDEX_FORMAT,
        "packVersion": version or pack_version(),
        "gameVersion": json.loads(LOCK_JSON.read_text())["pack"]["game_version"],
        "generatedBy": "tools/packtool.py smoke --config-defaults write (docs/config-defaults.md)",
        "mods": mods,
        "files": {n: {"format": fmt_of(n), "owners": owners.get(n, [])} for n in files},
        "packValues": values,
        "renames": settings["renames"],
    }
    return index, files, problems


def fmt_of(name: str) -> str:
    return "yaml" if name.endswith((".yaml", ".yml")) else "json"


def index_text(index: dict) -> str:
    return json.dumps(index, indent=2) + "\n"


def read_snapshot(version: str, root: Path = SNAPSHOTS) -> tuple[dict, dict[str, bytes]] | None:
    d = root / version
    if not (d / "index.json").exists():
        return None
    index = json.loads((d / "index.json").read_text())
    base = d / "files"
    files = {p.relative_to(base).as_posix(): p.read_bytes()
             for p in sorted(base.rglob("*")) if p.is_file()} if base.exists() else {}
    return index, files


def write_snapshot(index: dict, files: dict[str, bytes], root: Path = SNAPSHOTS) -> Path:
    d = root / index["packVersion"]
    if d.exists():
        shutil.rmtree(d)
    (d / "files").mkdir(parents=True)
    (d / "index.json").write_text(index_text(index))
    for name, data in files.items():
        p = d / "files" / name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)
    return d


def differences(want: tuple[dict, dict[str, bytes]], have: tuple[dict, dict[str, bytes]] | None) -> list[str]:
    """How the committed snapshot (have) differs from the server's (want), for a human."""
    if have is None:
        return ["no snapshot is committed for this pack version"]
    out = []
    wi, wf = want
    hi, hf = have
    for name in sorted(wf.keys() - hf.keys()):
        out.append(f"ModConfig/{name}: new (not in the snapshot)")
    for name in sorted(hf.keys() - wf.keys()):
        out.append(f"ModConfig/{name}: in the snapshot, but the server no longer writes it")
    for name in sorted(wf.keys() & hf.keys()):
        if wf[name] != hf[name]:
            diff = list(difflib.unified_diff(
                hf[name].decode("utf-8", "replace").splitlines(), wf[name].decode("utf-8", "replace").splitlines(),
                "snapshot", "server", n=1, lineterm=""))
            out.append(f"ModConfig/{name}: differs\n    " + "\n    ".join(diff[:40] + (["..."] if len(diff) > 40 else [])))
    if index_text(wi) != index_text(hi):
        diff = difflib.unified_diff(index_text(hi).splitlines(), index_text(wi).splitlines(),
                                    "index.json (committed)", "index.json (server)", n=1, lineterm="")
        out.append("index.json differs\n    " + "\n    ".join(list(diff)[:60]))
    return out


def released(version: str, remote: bool = True) -> bool:
    """Whether v<version> is tagged: locally, or on origin (CI checks out without tags)."""
    tag = f"v{version}"
    cmds = [["git", "-C", str(ROOT), "tag", "-l", tag]]
    if remote:
        cmds.append(["git", "-C", str(ROOT), "ls-remote", "--tags", "origin", f"refs/tags/{tag}"])
    for cmd in cmds:
        try:
            out = subprocess.run(cmd, capture_output=True, text=True, timeout=30).stdout
        except (OSError, subprocess.TimeoutExpired):
            continue
        if out.strip():
            return True
    return False


def run(mode: str, data: Path, mods_dir: Path | None = None) -> tuple[list[str], list[str]]:
    """After a capture-mode smoke run: (summary lines, failures) for --config-defaults MODE."""
    version = pack_version()
    report = data / CAPTURE_REPORT
    if not report.exists():
        return [], [f"config defaults: the server did not run in capture mode (no {report.name}); "
                    "is the pack's own mod loaded?"]
    early = json.loads(report.read_text()).get("presentAtFirstStartPre", [])
    lines = []
    if early:
        lines.append("config defaults: written before the first StartPre, so read before the pack's "
                     f"mod can bring them up to date: {', '.join(early)}")
    try:
        index, files, problems = build(data / "ModConfig", mods_dir or data / "Mods", version)
    except ConfigDefaultsError as e:
        return lines, [f"config defaults: {e}"]
    failures = [f"config defaults: {p}" for p in problems]
    lines.append(f"config defaults: {len(files)} file(s) in ModConfig, "
                 f"{len(index['packValues'])} pack value(s), {len(index['renames'])} rename(s)")
    have = read_snapshot(version)
    diffs = differences((index, files), have)
    frozen = released(version) and have is not None
    if mode == "write":
        if failures:
            return lines, failures
        if not diffs:
            lines.append(f"config defaults: the snapshot for pack {version} is up to date")
        elif frozen:
            failures.append(f"config defaults: pack {version} is released (tag v{version}), so its snapshot "
                            "is frozen; bump [pack] version in pack/pack.toml first")
        else:
            d = write_snapshot(index, files)
            lines.append(f"config defaults: wrote {d.relative_to(ROOT)} ({len(diffs)} difference(s))")
        return lines, failures
    if not diffs:
        lines.append(f"config defaults: the snapshot for pack {version} matches")
    elif frozen:
        lines.append(f"config defaults: pack {version} is released, so its snapshot is frozen and this tree's "
                     f"{len(diffs)} difference(s) wait for the next version's (bumping [pack] version records it)")
    else:
        failures.append(f"config defaults: the snapshot for pack {version} is out of date; run `{REGENERATE}` "
                        "and commit:\n  " + "\n  ".join(diffs))
    return lines, failures
