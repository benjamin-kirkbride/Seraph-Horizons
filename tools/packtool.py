#!/usr/bin/env python3
"""Seraph Horizons pack tool: resolve, verify, fetch, smoke-test and assemble.

Stdlib only (Python 3.11+). Subcommands:

  lock        Resolve pack.toml pins against the ModDB and (re)write pack/lock.json.
  check       Offline: fail if lock.json is out of sync with pack.toml.
  fetch       Download locked mod files into a cache, verify sha256, stage them.
  smoke       Boot a headless dedicated server with the staged mods and scan logs
              (--export PATH: also load tools/recipe-export and check its export).
  outdated    Report mods with a newer release compatible with the pinned game version.
  assemble    Build release artifacts (meta-mod, Cairn pack, mod list, server bundle).
"""

from __future__ import annotations

import argparse
import hashlib
import html
import http.client
import json
import os
import re
import shutil
import subprocess
import sys
import threading
import time
import tomllib
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PACK_TOML = ROOT / "pack" / "pack.toml"
LOCK_JSON = ROOT / "pack" / "lock.json"
MODDB = "https://mods.vintagestory.at"
USER_AGENT = "seraph-horizons-packtool (+https://github.com/benjamin-kirkbride/Seraph-Horizons)"

# ModDB API v2 install-information error codes we know about. Anything else is
# reported verbatim; every error is fatal for a pinned pack.
MODDB_ERRORS = {
    4041: "mod or release not found",
    4101: "release retracted",
    4102: "release retracted",
}


# --------------------------------------------------------------------------- io


def load_pack() -> dict:
    with PACK_TOML.open("rb") as f:
        pack = tomllib.load(f)
    ids = [m["id"] for m in pack.get("mod", [])]
    dupes = {i for i in ids if ids.count(i) > 1}
    if dupes:
        die(f"duplicate mod ids in pack.toml: {', '.join(sorted(dupes))}")
    return pack


def load_lock() -> dict:
    if not LOCK_JSON.exists():
        die("pack/lock.json is missing; run `tools/packtool.py lock`")
    return json.loads(LOCK_JSON.read_text())


def write_json(path: Path, data) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")


def die(msg: str) -> None:
    print(f"error: {msg}", file=sys.stderr)
    sys.exit(1)


def http_get(url: str, *, retries: int = 3) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(req, timeout=60) as resp:
                return resp.read()
        except (urllib.error.URLError, http.client.HTTPException, ConnectionError, TimeoutError) as e:
            if attempt == retries - 1:
                raise RuntimeError(f"GET {url} failed: {e}") from e
            time.sleep(2**attempt)
    raise AssertionError("unreachable")


def http_json(url: str):
    return json.loads(http_get(url))


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


# ------------------------------------------------------------------------ moddb


def install_information(ids: list[str], game_version: str) -> dict:
    """ModDB API v2: resolve `modid` or `modid@version` to a downloadable file.

    Batched: the server answers 414 somewhere between ~6 and ~11 KB of URL.
    """
    data, batch = {}, []
    for i in [*ids, None]:
        if batch and (i is None or len(",".join([*batch, i])) > 2000):
            query = urllib.parse.urlencode({"ids": ",".join(batch), "gv": game_version}, safe=",@")
            data |= http_json(f"{MODDB}/api/v2/mods/install-information?{query}")["data"]
            batch = []
        if i is not None:
            batch.append(i)
    return data


def mod_info(modid: str) -> dict | None:
    """ModDB API v1: the mod with all its releases (newest first)."""
    return http_json(f"{MODDB}/api/mod/{urllib.parse.quote(modid)}").get("mod")


def release_info(modid: str, version: str) -> dict:
    """ModDB API v1: release metadata (release id, declared game versions)."""
    mod = mod_info(modid)
    if not mod:
        die(f"{modid}: not found on the ModDB")
    for rel in mod["releases"]:
        if rel["modversion"] == version:
            return {"mod": mod, "release": rel}
    die(f"{modid}: no release {version} on the ModDB")
    raise AssertionError("unreachable")


def modinfo_from_zip(path: Path) -> dict:
    with zipfile.ZipFile(path) as z:
        names = {n.lower(): n for n in z.namelist()}
        if "modinfo.json" not in names:
            return {}
        raw = z.read(names["modinfo.json"]).decode("utf-8-sig")
    # modinfo.json is parsed by Newtonsoft, which tolerates keys in any case,
    # comments and trailing commas; normalise the common cases.
    raw = re.sub(r"^\s*//.*$", "", raw, flags=re.M)
    raw = re.sub(r",(\s*[}\]])", r"\1", raw)
    return {k.lower(): v for k, v in json.loads(raw).items()}


# ------------------------------------------------------------------------- lock


def cmd_lock(args) -> None:
    pack = load_pack()
    gv = pack["pack"]["game_version"]
    mods = pack.get("mod", [])
    info = install_information([f"{m['id']}@{m['version']}" for m in mods], gv) if mods else {}
    cache = Path(args.cache)

    locked, problems = [], []
    for m in mods:
        entry = info.get(m["id"], {})
        if "errorCode" in entry:
            code = entry["errorCode"]
            reason = entry.get("retractionReason", MODDB_ERRORS.get(code, "unknown error"))
            problems.append(f"{m['id']}@{m['version']}: ModDB error {code} ({reason})")
            continue
        rel = release_info(m["id"], m["version"])["release"]
        # Prefer the release's full CDN URI ("always respect the full uris returned
        # by the api"); v2's fileUrl is a relative redirect to the same file. The
        # ModDB returns some with raw spaces (`?dl=Foo 1.0.zip`), which urllib and
        # curl both reject, so percent-encode whatever isn't already.
        file_url = urllib.parse.quote(rel.get("mainfile") or urllib.parse.urljoin(MODDB, entry["fileUrl"]),
                                      safe=":/?#[]@!$&'()*+,;=%")
        path = download(file_url, cache / entry["fileName"])
        mi = modinfo_from_zip(path)
        if mi.get("modid", "").lower() != m["id"]:
            problems.append(f"{m['id']}: zip modinfo.json declares modid {mi.get('modid')!r}")
        if mi.get("version") != m["version"]:
            problems.append(f"{m['id']}: zip modinfo.json declares version {mi.get('version')!r}")
        declared = sorted(rel.get("tags", []))
        if gv not in declared:
            print(f"warning: {m['id']}@{m['version']} does not declare {gv} "
                  f"(declares {', '.join(declared) or 'nothing'}); the boot test is the arbiter",
                  file=sys.stderr)
        locked.append({
            "id": m["id"],
            "version": m["version"],
            "releaseId": rel["releaseid"],
            "fileId": rel["fileid"],
            "fileName": entry["fileName"],
            "fileUrl": file_url,
            "sha256": sha256_file(path),
            "size": path.stat().st_size,
            "side": m.get("side", "universal"),
            "license": m.get("license", "unknown"),
            "redistribute": bool(m.get("redistribute", False)),
            "compatibleGameVersions": declared,
            "dependencies": mi.get("dependencies", {}),
        })

    if problems:
        die("cannot lock:\n  " + "\n  ".join(problems))

    write_json(LOCK_JSON, {
        "lockVersion": 1,
        "pack": {k: pack["pack"][k] for k in ("id", "version", "game_version", "dotnet")},
        "mods": locked,
    })
    print(f"locked {len(locked)} mod(s) for game {gv} -> {LOCK_JSON.relative_to(ROOT)}")


def download(url: str, dest: Path) -> Path:
    if dest.exists():
        return dest
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(dest.suffix + ".part")
    tmp.write_bytes(http_get(url))
    tmp.replace(dest)
    return dest


def cmd_check(args) -> None:
    pack, lock = load_pack(), load_lock()
    errors = []
    for key in ("id", "version", "game_version", "dotnet"):
        if lock["pack"].get(key) != pack["pack"][key]:
            errors.append(f"pack.{key}: pack.toml={pack['pack'][key]!r} lock={lock['pack'].get(key)!r}")
    want = {m["id"]: m for m in pack.get("mod", [])}
    have = {m["id"]: m for m in lock["mods"]}
    for mid in sorted(want.keys() - have.keys()):
        errors.append(f"{mid}: in pack.toml but not locked")
    for mid in sorted(have.keys() - want.keys()):
        errors.append(f"{mid}: locked but not in pack.toml")
    for mid in sorted(want.keys() & have.keys()):
        w, h = want[mid], have[mid]
        if w["version"] != h["version"]:
            errors.append(f"{mid}: pack.toml pins {w['version']}, lock has {h['version']}")
        for key in ("side", "license", "redistribute"):
            if w.get(key, h[key]) != h[key]:
                errors.append(f"{mid}: {key} differs between pack.toml and lock")
    # Every hard dependency declared by a locked mod must itself be locked.
    for m in lock["mods"]:
        for dep in m.get("dependencies", {}):
            if dep.lower() not in ("game", "survival", "creative") and dep.lower() not in have:
                errors.append(f"{m['id']}: depends on {dep!r}, which is not in the pack")
    if errors:
        die("lock.json is out of date (run `tools/packtool.py lock`):\n  " + "\n  ".join(errors))
    print(f"lock.json matches pack.toml ({len(have)} mod(s))")


# ------------------------------------------------------------------------ fetch


def cmd_fetch(args) -> None:
    lock = load_lock()
    cache, dest = Path(args.cache), Path(args.dest)
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True)
    for m in lock["mods"]:
        path = cache / m["fileName"]
        if not path.exists() or sha256_file(path) != m["sha256"]:
            path.unlink(missing_ok=True)
            download(m["fileUrl"], path)
        got = sha256_file(path)
        if got != m["sha256"]:
            die(f"{m['id']}@{m['version']}: sha256 mismatch (lock {m['sha256']}, got {got}); "
                "the file on the ModDB changed under a pinned version")
        shutil.copy2(path, dest / m["fileName"])
        print(f"staged {m['id']}@{m['version']}")
    # Mods authored in this repo (mods/<name>/ with a modinfo.json) are staged as folders.
    for local in sorted((ROOT / "mods").glob("*/modinfo.json")) if (ROOT / "mods").exists() else []:
        shutil.copytree(local.parent, dest / local.parent.name)
        print(f"staged local mod {local.parent.name}")


# ------------------------------------------------------------------------ smoke

READY = re.compile(r"Dedicated Server now running")
FATAL_PATTERNS = [
    (re.compile(r"\[Server (Error|Fatal)\]"), "error logged"),
    (re.compile(r"JsonPatch Loader: .*had errors on"), "json patch errors"),
    (re.compile(r"Patch \d+ \(target: .*\) in .* failed"), "json patch failed"),
    (re.compile(r"[Uu]nresolved dependenc|[Mm]issing dependenc|dependency .* not found"), "dependency problem"),
    (re.compile(r"[A-Za-z.]+Exception\b"), "exception"),
]
# Lines that match a fatal pattern but are expected in CI. Keep this list short
# and justified; prefer fixing the cause.
ALLOWED = [
    # The server polls the ModDB for its blocklist; harmless if CI egress is filtered.
    re.compile(r"Could not get blocked mods from api"),
]
# Understood cross-mod errors, each tied to an issue (shared with the Atlas scenarios).
KNOWN_ERRORS_JSON = ROOT / "pack" / "known-errors.json"
PATCH_FAILED = FATAL_PATTERNS[2][0]
PATCH_SUMMARY = re.compile(r"JsonPatch Loader: .*had errors on (\d+) patches")


def known_errors() -> list[tuple[re.Pattern, int]]:
    if not KNOWN_ERRORS_JSON.exists():
        return []
    return [(re.compile(e["pattern"]), e["issue"])
            for e in json.loads(KNOWN_ERRORS_JSON.read_text())["errors"]]


def cmd_smoke(args) -> None:
    server = Path(args.server).resolve()
    dll = server / "VintagestoryServer.dll"
    if not dll.exists():
        die(f"{dll} not found (set --server to an extracted vs_server_linux-x64 archive)")
    data = Path(args.data).resolve()
    if data.exists():
        shutil.rmtree(data)
    (data / "Mods").mkdir(parents=True)
    for item in Path(args.mods).iterdir():
        target = data / "Mods" / item.name
        (shutil.copytree if item.is_dir() else shutil.copy2)(item, target)

    lock = load_lock()
    env = None
    export = Path(args.export).resolve() if args.export else None
    if export:
        # The export mod goes into this run's Mods only, never into build/mods, so it is
        # not part of the pack, the lock or `assemble`.
        stage_export_mod(server, data)
        export.unlink(missing_ok=True)
        env = {**os.environ, "SERAPH_EXPORT_PATH": str(export),
               "SERAPH_PACK_ID": lock["pack"]["id"], "SERAPH_PACK_VERSION": lock["pack"]["version"]}
    # Override on the command line rather than writing serverconfig.json: a partial
    # config file lacks the default player groups and the server refuses to start.
    # Fixed seed + standard worldgen so structure mods actually generate.
    overrides = json.dumps({"WorldConfig": {"Seed": str(args.seed), "WorldName": "ci",
                                            "WorldType": "standard", "PlayStyle": "surviveandbuild"}})
    cmd = ["dotnet", str(dll), "--dataPath", str(data), "--port", str(args.port),
           f"--withconfig={overrides}"]
    print("+ " + " ".join(cmd), flush=True)
    proc = subprocess.Popen(cmd, cwd=server, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, text=True, bufsize=1, env=env)
    assert proc.stdin and proc.stdout
    stdin, stdout = proc.stdin, proc.stdout
    lines: list[str] = []
    ready, fatal = threading.Event(), threading.Event()

    def pump():
        for line in stdout:
            lines.append(line.rstrip("\n"))
            if args.verbose:
                print(line, end="", flush=True)
            if READY.search(line):
                ready.set()
            if "[Server Fatal]" in line:
                fatal.set()

    t = threading.Thread(target=pump, daemon=True)
    t.start()
    deadline = time.monotonic() + args.timeout
    while not (ready.is_set() or fatal.is_set()) and proc.poll() is None and time.monotonic() < deadline:
        time.sleep(0.5)
    booted = ready.is_set() and not fatal.is_set()
    if booted:
        time.sleep(args.settle)  # let a few ticks and the first chunk columns run
        stdin.write("/stop\n")
        stdin.flush()
    try:
        code = proc.wait(timeout=120)
    except subprocess.TimeoutExpired:
        proc.kill()
        code = proc.wait()
    t.join(timeout=5)

    log = "\n".join(lines)
    (data / "smoke-stdout.log").write_text(log + "\n")
    failures = []
    if fatal.is_set():
        failures.append("server logged a fatal error")
    elif not booted:
        failures.append(f"server did not report ready within {args.timeout}s")
    if booted and code != 0:
        failures.append(f"server exited with code {code}")
    known = known_errors()
    tolerated: dict[int, int] = {}  # issue -> lines tolerated
    tolerated_patches = 0
    summaries: list[str] = []
    for line in lines:
        if any(a.search(line) for a in ALLOWED):
            continue
        issue = next((i for pat, i in known if pat.search(line)), None)
        if issue is not None:
            tolerated[issue] = tolerated.get(issue, 0) + 1
            tolerated_patches += bool(PATCH_FAILED.search(line))
            continue
        if PATCH_SUMMARY.search(line):
            summaries.append(line)
            continue
        for pat, why in FATAL_PATTERNS:
            if pat.search(line):
                failures.append(f"{why}: {line.strip()}")
                break
    # The loader's summary counts every failed patch; it is fine only if all of
    # them were known ones.
    for line in summaries:
        if int(PATCH_SUMMARY.search(line).group(1)) != tolerated_patches:
            failures.append(f"json patch errors: {line.strip()}")

    m = re.search(r"Mods, sorted by dependency: (.*)", log)
    loaded = {s.strip() for s in m.group(1).split(",")} if m else set()
    for mod in lock["mods"]:
        if mod["id"] not in loaded:
            failures.append(f"locked mod {mod['id']} was not loaded")
    patches = re.search(r"JsonPatch Loader: .*", log)

    summary = [
        f"game {lock['pack']['game_version']}, seed {args.seed}",
        f"loaded mods: {', '.join(sorted(loaded)) or '(none)'}",
        patches.group(0) if patches else "no JsonPatch summary line found",
        f"warnings: {sum('[Server Warning]' in l for l in lines)}",
        "known errors tolerated: " + (", ".join(f"#{i} x{n}" for i, n in sorted(tolerated.items())) or "none"),
    ]
    if export:
        export_lines, export_failures = check_export(export, lines)
        summary += export_lines
        failures += export_failures
    report("Server smoke test", summary, failures)
    if failures:
        sys.exit(1)


EXPORT_PROJECT = ROOT / "tools" / "recipe-export"
EXPORT_LOG = re.compile(r"\[seraphexport\]")


def stage_export_mod(server: Path, data: Path) -> None:
    """Build tools/recipe-export against this server and stage it as a folder mod."""
    out = data / "seraphexport-build"
    cmd = ["dotnet", "build", str(EXPORT_PROJECT), "-c", "Release", "-o", str(out), "--nologo", "-v", "q"]
    print("+ " + " ".join(cmd), flush=True)
    result = subprocess.run(cmd, env={**os.environ, "VINTAGE_STORY": str(server)})
    if result.returncode != 0:
        die("building the export mod failed")
    dest = data / "Mods" / "seraphexport"
    dest.mkdir()
    for name in ("SeraphExport.dll", "SeraphExport.pdb", "modinfo.json"):
        if (out / name).exists():
            shutil.copy2(out / name, dest / name)
    shutil.rmtree(out)


def check_export(path: Path, log: list[str]) -> tuple[list[str], list[str]]:
    """Summary lines and failures for the export the server was asked to write."""
    notes = [l.strip() for l in log if EXPORT_LOG.search(l)]
    if not path.exists():
        return [], [f"recipe export not written to {path}"] + [f"exporter: {n}" for n in notes]
    try:
        doc = json.loads(path.read_text())
    except (json.JSONDecodeError, UnicodeDecodeError) as e:
        return [], [f"recipe export {path} is not valid JSON: {e}"]
    counts, problems = export_counts(doc)
    size = path.stat().st_size
    lines = [f"recipe export: {sum(counts.values())} recipe(s) in {len(counts)} type(s), "
             f"{size / 1e6:.1f} MB -> {path}"]
    lines += [f"  {t}: {n}" for t, n in sorted(counts.items())]
    return lines, [f"recipe export: {p}" for p in problems]


def export_counts(doc) -> tuple[dict[str, int], list[str]]:
    """Recipes per type, checked against recipeTypes; problems that make the export unusable."""
    if not isinstance(doc, dict) or not isinstance(doc.get("recipes"), list) \
            or not isinstance(doc.get("recipeTypes"), dict):
        return {}, ["no recipes list or recipeTypes object"]
    problems = []
    counts = {t: 0 for t in doc["recipeTypes"]}
    ids = set()
    for r in doc["recipes"]:
        t = r.get("type") if isinstance(r, dict) else None
        if t not in counts:
            problems.append(f"recipe {r.get('id') if isinstance(r, dict) else r!r} has type {t!r}, "
                            "which is not in recipeTypes")
            continue
        counts[t] += 1
        if r.get("id") in ids:
            problems.append(f"duplicate recipe id {r.get('id')}")
        ids.add(r.get("id"))
    for t, entry in doc["recipeTypes"].items():
        if entry.get("count") != counts[t]:
            problems.append(f"recipeTypes[{t!r}].count is {entry.get('count')}, but {counts[t]} recipe(s) have it")
    if not doc["recipes"]:
        problems.append("no recipes exported")
    return counts, problems


def report(title: str, summary: list[str], failures: list[str]) -> None:
    status = "FAILED" if failures else "passed"
    print(f"\n{title}: {status}")
    for s in summary:
        print(f"  {s}")
    for f in failures:
        print(f"  FAIL {f}")
    step_summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if step_summary:
        with open(step_summary, "a") as fh:
            fh.write(f"### {title}: {status}\n\n")
            fh.writelines(f"- {s}\n" for s in summary)
            fh.writelines(f"- :x: `{f}`\n" for f in failures)
            fh.write("\n")


# --------------------------------------------------------------------- outdated


REPORT_MAX = 60_000  # GitHub caps issue bodies at 65536 chars; leave room for the wrapper
CHANGELOG_MAX = 1_500


def version_key(v: str) -> tuple:
    """Order game version tags: 1.22.0-pre.1 < 1.22.0 < 1.22.1."""
    core, _, pre = v.partition("-")
    return tuple(int(x) if x.isdigit() else 0 for x in core.split(".")), pre == "", pre


def html_text(s: str) -> str:
    """ModDB changelogs are HTML; flatten to plain text lines."""
    s = re.sub(r"(?i)\s*<li\b[^>]*>\s*", "\n- ", re.sub(r"(?i)</li>", "", s))
    s = re.sub(r"(?i)<br\s*/?>|</(p|div|h\d|ul|ol|pre)>", "\n", s)
    s = html.unescape(re.sub(r"<[^>]*>", "", s)).replace("\xa0", " ")
    return re.sub(r"\n{3,}", "\n\n", "\n".join(l.rstrip() for l in s.splitlines())).strip()


def clip(s: str, n: int) -> str:
    if len(s) <= n:
        return s
    cut = s.rfind("\n", 0, n)
    return s[:cut if cut > n // 2 else n].rstrip() + "\n[...]"


def cell(s: str) -> str:
    """Markdown table cell: one line, no pipes, no raw HTML."""
    return re.sub(r"\s+", " ", str(s)).strip().replace("|", "\\|").replace("<", "&lt;")


def span(s: str) -> str:
    """Inline code span for ModDB-supplied text (no @mentions or #refs)."""
    return "`" + cell(s).replace("`", "'") + "`"


def cmd_outdated(args) -> None:
    lock = load_lock()
    gv = args.game_version or lock["pack"]["game_version"]
    mods = lock["mods"]
    pins = {m["id"]: m["version"] for m in mods}
    info = install_information([f"{i}@{v}" for i, v in pins.items()], gv)
    errors, upgrades = [], {}  # upgrades: id -> recommended version
    for m in mods:
        entry = info.get(m["id"], {})
        if "errorCode" in entry:
            err = entry["errorCode"]
            reason = entry.get("retractionReason") or MODDB_ERRORS.get(err, "unknown error")
            errors.append({"id": m["id"], "locked": m["version"], "latest": "-",
                           "note": f"ModDB error {err}: {reason}", "errorCode": err, "reason": reason})
        elif entry.get("recommendedUpgrade") not in (None, m["version"]):
            upgrades[m["id"]] = entry["recommendedUpgrade"]
    failed = {e["id"] for e in errors}

    # v2 only recommends stable releases that declare `gv`. Pins whose release does
    # not declare it are also asked under the newest version they do declare (one
    # request per such version); prerelease pins are checked against v1 below.
    fallback: dict[str, list[str]] = {}
    for m in mods:
        cv = m["compatibleGameVersions"]
        if cv and gv not in cv and "-" not in m["version"] and m["id"] not in upgrades.keys() | failed:
            fallback.setdefault(max(cv, key=version_key), []).append(f"{m['id']}@{m['version']}")
    for alt, ids in sorted(fallback.items()):
        for mid, entry in install_information(ids, alt).items():
            if entry.get("recommendedUpgrade") not in (None, pins.get(mid)):
                upgrades[mid] = entry["recommendedUpgrade"]

    # Details (one v1 request per mod) only for mods with an update or a prerelease pin.
    updates = []
    for m in mods:
        if m["id"] in failed or not (m["id"] in upgrades or "-" in m["version"]):
            continue
        try:
            mod = mod_info(m["id"]) or {}
        except RuntimeError as e:
            print(f"warning: {e}", file=sys.stderr)
            mod = {}
        rels = mod.get("releases", [])
        pinned = next((r for r in rels if r["releaseid"] == m["releaseId"]), None)
        if m["id"] in upgrades:
            rel = next((r for r in rels if r["modversion"] == upgrades[m["id"]]), None)
        elif pinned:
            cv = m["compatibleGameVersions"]
            ok = {gv} if gv in cv or not cv else {gv, max(cv, key=version_key)}
            newer = [r for r in rels if r["created"] > pinned["created"] and ok & set(r.get("tags", []))]
            if not newer:
                continue
            rel = max(newer, key=lambda r: r["created"])
        else:
            print(f"warning: {m['id']}@{m['version']}: prerelease pin not found in the ModDB release list",
                  file=sys.stderr)
            continue
        rel = rel or {}
        latest = rel.get("modversion") or upgrades[m["id"]]
        tags = sorted(rel.get("tags", []), key=version_key)
        updates.append({
            "id": m["id"],
            "name": mod.get("name", m["id"]),
            "url": f"{MODDB}/show/mod/{mod['assetid']}" if mod.get("assetid") else f"{MODDB}/{m['id']}",
            "locked": m["version"],
            "latest": latest,
            "note": "update available",
            "released": rel.get("created", "")[:10] or None,
            "gameVersions": tags,
            "declaresGameVersion": gv in tags,
            "prerelease": "-" in latest,
            "changelog": clip(html_text(rel.get("changelog") or ""), CHANGELOG_MAX) or None,
        })

    if args.json:
        print(json.dumps(errors + updates, indent=2, ensure_ascii=False))
    elif not (errors or updates):
        print(f"all {len(mods)} mod(s) are current for game {gv}")
    else:
        print(outdated_markdown(gv, errors, updates))
    if errors:
        sys.exit(2)


def outdated_markdown(gv: str, errors: list[dict], updates: list[dict]) -> str:
    out = []
    if errors:
        out += [f"### Locked releases retracted or missing ({len(errors)})", "",
                "These break installs: pick another release now.", "",
                "| mod | locked | ModDB error |", "|---|---|---|"]
        out += [f"| {cell(e['id'])} | {cell(e['locked'])} | {e['errorCode']}: {span(e['reason'])} |"
                for e in errors]
        out.append("")
    if updates:
        out += [f"### Updates available ({len(updates)})", "",
                "| mod | locked | latest | released | game versions |", "|---|---|---|---|---|"]
        for u in updates:
            name = cell(u["name"]).replace("[", "\\[").replace("]", "\\]")
            latest = cell(u["latest"]) + (" (pre)" if u["prerelease"] else "")
            gvs = ", ".join(u["gameVersions"]) or "?"
            if not u["declaresGameVersion"]:
                gvs += f" (not {gv})"
            out.append(f"| [{name}]({u['url']}) `{u['id']}` | {cell(u['locked'])} | {latest} "
                       f"| {u['released'] or '?'} | {cell(gvs)} |")
        out.append("")
    body = "\n".join(out)
    notes, skipped = [], 0
    for u in updates:
        if not u["changelog"]:
            continue
        fence = "`" * max(3, 1 + max((len(r) for r in re.findall(r"`+", u["changelog"])), default=0))
        note = (f"<details><summary><code>{u['id']}</code> {cell(u['latest'])} changelog</summary>\n\n"
                f"{fence}text\n{u['changelog']}\n{fence}\n\n</details>\n")
        if len(body) + sum(map(len, notes)) + len(note) > REPORT_MAX - 200:
            skipped += 1
        else:
            notes.append(note)
    if notes:
        body += "\n### Changelogs\n\n" + "\n".join(notes)
    if skipped:
        body += f"\n_{skipped} more changelog(s) omitted to fit the issue size limit; see the ModDB pages._\n"
    if len(body) > REPORT_MAX:  # only if the tables alone are huge
        body = body[:body.rfind("\n", 0, REPORT_MAX - 100)] + "\n\n_[report truncated]_\n"
    return body.rstrip("\n")


# --------------------------------------------------------------------- assemble


def cmd_assemble(args) -> None:
    pack, lock = load_pack(), load_lock()
    meta = pack["pack"]
    out = Path(args.out)
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    tag = f"{meta['id']}_{meta['version']}"

    # (a) ModDB meta-mod: a content mod whose only payload is its dependency list.
    # The game treats these as minimum versions, so this is the "casual" install path.
    modinfo = {
        "type": "content",
        "modid": meta["id"],
        "name": meta["name"],
        "version": meta["version"],
        "authors": meta.get("authors", []),
        "description": meta.get("description", ""),
        "side": "Universal",
        "dependencies": {"game": meta["game_version"],
                         **{m["id"]: m["version"] for m in lock["mods"]}},
    }
    with zipfile.ZipFile(out / f"{tag}_metamod.zip", "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("modinfo.json", json.dumps(modinfo, indent=2) + "\n")

    # (b) Cairn pack file (manifest + lockfile): exact pins, sha256-verified downloads,
    # and Cairn installs the matching game and .NET. Open it with the Cairn launcher
    # or `cairn-server install <file>`.
    write_json(out / f"{tag}.cairn.json", cairn_bundle(meta, lock))

    # (c) modid@version list (Story Forge import string, ModDB v2 `ids` format).
    (out / f"{tag}_modlist.txt").write_text(
        ",".join(f"{m['id']}@{m['version']}" for m in lock["mods"]) + "\n")

    # (d) Plain server bundle (for hosts not using cairn-server): lockfile + fetch
    # script. ModConfig overrides are partial merges, so they ship via Cairn only. Mod zips are included
    # only when their license allows redistribution; the rest are fetched from
    # the ModDB CDN and checked against the locked sha256.
    with zipfile.ZipFile(out / f"{tag}_server.zip", "w", zipfile.ZIP_DEFLATED) as z:
        z.write(LOCK_JSON, "lock.json")
        z.writestr("fetch-mods.sh", server_fetch_script(lock))
        z.getinfo("fetch-mods.sh").external_attr = 0o755 << 16
        for m in lock["mods"]:
            if m["redistribute"]:
                src = Path(args.cache) / m["fileName"]
                if not src.exists() or sha256_file(src) != m["sha256"]:
                    die(f"{m['id']}: redistributable zip missing from cache; run fetch first")
                z.write(src, f"Mods/{m['fileName']}")

    (out / "SHA256SUMS").write_text("".join(
        f"{sha256_file(p)}  {p.name}\n" for p in sorted(out.iterdir()) if p.name != "SHA256SUMS"))
    for p in sorted(out.iterdir()):
        print(f"wrote {p.relative_to(ROOT) if p.is_relative_to(ROOT) else p}")


CAIRN_SIDES = {"universal": "both", "server": "server", "client": "client"}


def cairn_bundle(meta: dict, lock: dict) -> dict:
    """Cairn's PackBundle (formatVersion 1): {pack: PackManifest, lock: PackLock}.

    Field names follow cairn-app src/Cairn.Core/Packs/PackManifest.cs and PackBundle.cs.
    """
    manifest: dict = {
        "id": meta["id"],
        "name": meta["name"],
        "description": meta.get("description", "")[:280],
        "gameVersion": meta["game_version"],
        "mods": [{"modid": m["id"], "version": m["version"]} for m in lock["mods"]],
    }
    mod_config = collect_mod_config()
    if mod_config:
        manifest["modConfig"] = mod_config
    return {
        "formatVersion": 1,
        "pack": manifest,
        "lock": {
            "gameVersion": meta["game_version"],
            "mods": [{
                "modid": m["id"],
                "version": m["version"],
                "filename": m["fileName"],
                "url": m["fileUrl"],
                "releaseId": m["releaseId"],
                "fileId": m["fileId"],
                "sha256": m["sha256"],
                "side": CAIRN_SIDES[m["side"]],
            } for m in lock["mods"]],
        },
    }


def collect_mod_config() -> dict:
    """pack/config/ModConfig/**/*.json -> {relative/path.json: {keys to merge}}."""
    base = ROOT / "pack" / "config" / "ModConfig"
    if not base.exists():
        return {}
    return {f.relative_to(base).as_posix(): json.loads(f.read_text())
            for f in sorted(base.rglob("*.json"))}


def server_fetch_script(lock: dict) -> str:
    lines = [
        "#!/bin/sh",
        "# Download the locked mods into ./Mods and verify their sha256.",
        "# Usage: ./fetch-mods.sh [path/to/server/data]",
        "set -eu",
        'dest="${1:-.}/Mods"',
        'mkdir -p "$dest"',
    ]
    for m in lock["mods"]:
        if m["redistribute"]:
            continue
        f = f'"$dest/{m["fileName"]}"'
        lines += [
            f'[ -f {f} ] || curl -fsSL --proto "=https" -o {f} "{m["fileUrl"]}"',
            f'echo "{m["sha256"]}  $dest/{m["fileName"]}" | sha256sum -c -',
        ]
    return "\n".join(lines) + "\n"


# ------------------------------------------------------------------------- main


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--cache", default=os.environ.get("PACK_CACHE", str(ROOT / ".cache" / "mods")),
                   help="download cache for mod zips (default: .cache/mods)")
    sub = p.add_subparsers(dest="cmd", required=True)

    sub.add_parser("lock", help=cmd_lock.__doc__).set_defaults(func=cmd_lock)
    sub.add_parser("check").set_defaults(func=cmd_check)

    s = sub.add_parser("fetch")
    s.add_argument("--dest", default=str(ROOT / "build" / "mods"))
    s.set_defaults(func=cmd_fetch)

    s = sub.add_parser("smoke")
    s.add_argument("--server", default=os.environ.get("VINTAGE_STORY", ""),
                   help="extracted server directory (default: $VINTAGE_STORY)")
    s.add_argument("--mods", default=str(ROOT / "build" / "mods"))
    s.add_argument("--data", default=str(ROOT / "build" / "smoke-data"))
    s.add_argument("--seed", type=int, default=424242)
    s.add_argument("--port", type=int, default=42420)
    s.add_argument("--timeout", type=int, default=600, help="seconds to wait for the server to be ready")
    s.add_argument("--settle", type=int, default=20, help="seconds to keep running after ready")
    s.add_argument("-v", "--verbose", action="store_true")
    s.add_argument("--export", metavar="PATH",
                   help="also load tools/recipe-export and write the recipe export to PATH")
    s.set_defaults(func=cmd_smoke)

    s = sub.add_parser("outdated")
    s.add_argument("--game-version")
    s.add_argument("--json", action="store_true")
    s.set_defaults(func=cmd_outdated)

    s = sub.add_parser("assemble")
    s.add_argument("--out", default=str(ROOT / "dist"))
    s.set_defaults(func=cmd_assemble)

    args = p.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
