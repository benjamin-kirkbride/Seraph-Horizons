#!/usr/bin/env python3
"""Seraph Horizons pack tool: resolve, verify, fetch, smoke-test and assemble.

Stdlib only (Python 3.11+). Subcommands:

  lock        Resolve pack.toml pins against the ModDB and (re)write pack/lock.json.
  check       Offline: fail if lock.json is out of sync with pack.toml.
  fetch       Download locked mod files into a cache, verify sha256, stage them.
  smoke       Boot a headless dedicated server with the staged mods and scan logs.
  outdated    Report mods with a newer release compatible with the pinned game version.
  assemble    Build release artifacts (meta-mod, Cairn pack, mod list, server bundle).
"""

from __future__ import annotations

import argparse
import hashlib
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
        except (urllib.error.URLError, TimeoutError) as e:
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
    """ModDB API v2: resolve `modid` or `modid@version` to a downloadable file."""
    query = urllib.parse.urlencode({"ids": ",".join(ids), "gv": game_version}, safe=",@")
    return http_json(f"{MODDB}/api/v2/mods/install-information?{query}")["data"]


def release_info(modid: str, version: str) -> dict:
    """ModDB API v1: release metadata (release id, declared game versions)."""
    mod = http_json(f"{MODDB}/api/mod/{urllib.parse.quote(modid)}").get("mod")
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
        # by the api"); v2's fileUrl is a relative redirect to the same file.
        file_url = rel.get("mainfile") or urllib.parse.urljoin(MODDB, entry["fileUrl"])
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
    # Override on the command line rather than writing serverconfig.json: a partial
    # config file lacks the default player groups and the server refuses to start.
    # Fixed seed + standard worldgen so structure mods actually generate.
    overrides = json.dumps({"WorldConfig": {"Seed": str(args.seed), "WorldName": "ci",
                                            "WorldType": "standard", "PlayStyle": "surviveandbuild"}})
    cmd = ["dotnet", str(dll), "--dataPath", str(data), "--port", str(args.port),
           f"--withconfig={overrides}"]
    print("+ " + " ".join(cmd), flush=True)
    proc = subprocess.Popen(cmd, cwd=server, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, text=True, bufsize=1)
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
    for line in lines:
        if any(a.search(line) for a in ALLOWED):
            continue
        for pat, why in FATAL_PATTERNS:
            if pat.search(line):
                failures.append(f"{why}: {line.strip()}")
                break

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
    ]
    report("Server smoke test", summary, failures)
    if failures:
        sys.exit(1)


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


def cmd_outdated(args) -> None:
    lock = load_lock()
    gv = args.game_version or lock["pack"]["game_version"]
    info = install_information([f"{m['id']}@{m['version']}" for m in lock["mods"]], gv)
    rows = []
    for m in lock["mods"]:
        entry = info.get(m["id"], {})
        if "errorCode" in entry:
            code = entry["errorCode"]
            rows.append((m["id"], m["version"], "-",
                         f"ModDB error {code}: {entry.get('retractionReason', MODDB_ERRORS.get(code, '?'))}"))
        elif entry.get("recommendedUpgrade") and entry["recommendedUpgrade"] != m["version"]:
            rows.append((m["id"], m["version"], entry["recommendedUpgrade"], "update available"))
    if args.json:
        print(json.dumps([dict(zip(("id", "locked", "latest", "note"), r)) for r in rows], indent=2))
    elif not rows:
        print(f"all {len(lock['mods'])} mod(s) are current for game {gv}")
    else:
        print(f"| mod | locked | latest for {gv} | note |\n|---|---|---|---|")
        for r in rows:
            print("| " + " | ".join(r) + " |")
    if any(r[3].startswith("ModDB error") for r in rows):
        sys.exit(2)


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
    write_json(out / f"{tag}.cairn", cairn_bundle(meta, lock))

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
