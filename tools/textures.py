#!/usr/bin/env python3
# /// script
# requires-python = ">=3.11"
# dependencies = ["pillow>=10"]
# ///
"""Seraph Horizons texture tool: content-addressed game textures for the recipe browser.

The site's eidolon fitting sheet dresses its model in the game's own textures. CI's game
is the dedicated server, which has no textures, so the recipe export carries only their
paths (`domain:path` under textures/, no extension), and the textures themselves are
imported here by hand from a game client install, like the icons (docs/recipe-browser/icons.md,
"Game textures"). They are stored as textures/<first two hex digits>/<sha256>.png, tracked
by Git LFS; textures/index.json maps texture paths to hashes. The site build
(site/scripts/prepare-data.ts) puts the ones a version's export names into its data.
Subcommands:

  import      Read each texture a recipe export names (or the paths given) from a game
              client's assets and the pack's own mods' assets, store each distinct image
              once and update the index. Needs Pillow: `uv run tools/textures.py import ...`.
  prune       Delete image files the index no longer references (dry run by default).
  drift       Compare the textures a recipe export names with the index; warns, never
              fails, on missing textures.
  verify      Every index entry has a file and every file's content matches its name.

Only `import` needs Pillow; the rest is stdlib only (Python 3.11+). The store shares its
file layout and checks with tools/icons.py.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import io
import json
import os
import re
from pathlib import Path

# The store's layout and checks are the icon store's. Loaded by file: `import icons` would find
# the repository's icons/ folder as a namespace package when run from the root.
_spec = importlib.util.spec_from_file_location("seraph_icons_tool", Path(__file__).resolve().parent / "icons.py")
icons = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(icons)
HASH, annotation_escape, die, image_path = icons.HASH, icons.annotation_escape, icons.die, icons.image_path
lfs_pointer_oid, rel, sha256_file, stored_files = icons.lfs_pointer_oid, icons.rel, icons.sha256_file, icons.stored_files

ROOT = icons.ROOT
TEXTURES = ROOT / "textures"
# domain:path under textures/, no extension.
PATH = re.compile(r"^[a-z0-9_-]+:[a-z0-9_./-]+$")
# The game's own domain spreads over three asset folders of a client or server install.
GAME_FOLDERS = ("survival", "game", "creative")


# ------------------------------------------------------------------------ index


def load_index(store: Path, *, create: bool = False) -> dict:
    path = store / "index.json"
    if not path.exists():
        if create:
            return {"schemaVersion": 1, "textures": {}}
        die(f"{rel(path)} is missing")
    try:
        index = json.loads(path.read_text())
    except (OSError, ValueError) as e:
        die(f"{rel(path)}: cannot read: {e}")
    if not isinstance(index, dict):
        die(f"{rel(path)}: not a JSON object")
    problems = []
    if index.get("schemaVersion") != 1:
        problems.append(f"schemaVersion is {index.get('schemaVersion')!r}, expected 1")
    textures = index.get("textures")
    if not isinstance(textures, dict):
        problems.append("textures is not an object")
    else:
        for texture, digest in textures.items():
            if not PATH.match(texture):
                problems.append(f"{texture!r} is not a domain:path texture path")
            if not isinstance(digest, str) or not HASH.match(digest):
                problems.append(f"{texture}: {digest!r} is not a sha256 hex digest")
    if problems:
        die(f"{rel(path)} is corrupt:\n  " + "\n  ".join(problems[:20]))
    return index


def write_index(store: Path, index: dict) -> bool:
    """Write the index in its one canonical form; returns whether it changed."""
    text = json.dumps({"schemaVersion": 1, "textures": dict(sorted(index["textures"].items()))},
                      indent=2, ensure_ascii=False) + "\n"
    path = store / "index.json"
    if path.exists() and path.read_text() == text:
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text)
    return True


# ------------------------------------------------------------- export, assets


def export_paths(export: dict) -> list[str]:
    """Every texture path a recipe export's eidolon section names: its layers' materials'
    and its shape's own. The same set site/src/lib/prepare.ts resolves."""
    e = export.get("eidolon")
    if not isinstance(e, dict):
        return []
    paths = {m["texture"] for l in e.get("layers", []) for m in l.get("materials", []) if m.get("texture")}
    paths.update(p for p in (e.get("shapeTextures") or {}).values() if p)
    return sorted(paths)


def read_export(path: str) -> dict:
    try:
        export = json.loads(Path(path).read_text())
        if not isinstance(export, dict):
            raise TypeError("not a JSON object")
        return export
    except (OSError, ValueError, TypeError) as e:
        die(f"{path}: cannot read the export: {e}")


def asset_roots(game: str | None, assets: list[str]) -> list[Path]:
    """Folders holding <domain>/textures/...: the pack's own mods' assets (mods-src/*/assets),
    any given, and a game install's assets/ (its game domain resolved by asset_file)."""
    roots = [Path(a) for a in assets]
    roots += sorted(p for p in (ROOT / "mods-src").glob("*/assets") if p.is_dir())
    if game:
        g = Path(game).expanduser()
        if not (g / "assets").is_dir():
            die(f"{g}: no assets/ folder; give the game's install folder (with assets/survival)")
        roots.append(g / "assets")
    return roots


def asset_file(roots: list[Path], path: str) -> Path | None:
    """The PNG a texture path names, from the first root that has it."""
    domain, _, rest = path.partition(":")
    folders = GAME_FOLDERS if domain == "game" else (domain,)
    for root in roots:
        for folder in folders:
            f = root / folder / "textures" / f"{rest}.png"
            if f.is_file():
                return f
    return None


def normalise(path: Path) -> bytes:
    """The texture as an RGBA PNG with no metadata chunks and the colour under fully
    transparent pixels zeroed, so the bytes depend only on what it looks like."""
    from PIL import Image

    with Image.open(path) as im:
        im = im.convert("RGBA")
    raw = bytearray(im.tobytes())
    for i in range(3, len(raw), 4):
        if raw[i] == 0:
            raw[i - 3:i] = b"\0\0\0"
    buf = io.BytesIO()
    Image.frombytes("RGBA", im.size, bytes(raw)).save(buf, "PNG", optimize=True)
    return buf.getvalue()


# ---------------------------------------------------------------------- import


def cmd_import(args) -> None:
    try:
        import PIL  # noqa: F401
    except ImportError:
        die("import needs Pillow: run `uv run tools/textures.py import ...` (or `pip install pillow`)")
    store = Path(args.textures)
    paths = set(args.paths)
    if args.export:
        paths.update(export_paths(read_export(args.export)))
    bad = sorted(p for p in paths if not PATH.match(p))
    if bad:
        die("not domain:path texture paths: " + ", ".join(bad))
    if not paths:
        die("nothing to import: give --export or texture paths")
    roots = asset_roots(args.game, args.assets)
    index = load_index(store, create=True)
    added = changed = same = 0
    missing = []
    for path in sorted(paths):
        src = asset_file(roots, path)
        if src is None:
            missing.append(path)
            continue
        data = normalise(src)
        digest = hashlib.sha256(data).hexdigest()
        target = image_path(store, digest)
        if not target.exists() or lfs_pointer_oid(target) is not None:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        old = index["textures"].get(path)
        added += old is None
        changed += old is not None and old != digest
        same += old == digest
        index["textures"][path] = digest
    write_index(store, index)
    print(f"{added} added, {changed} changed, {same} unchanged, {len(missing)} not found -> {rel(store)}")
    if missing:
        die("textures not found in " + ", ".join(rel(r) for r in roots) + ":\n  " + "\n  ".join(missing))


# ----------------------------------------------------------------------- prune


def cmd_prune(args) -> None:
    store = Path(args.textures)
    referenced = set(load_index(store)["textures"].values())
    stale = [p for p in stored_files(store) if p.suffix == ".png" and HASH.match(p.stem) and p.stem not in referenced]
    for p in stale:
        if args.delete:
            p.unlink()
            if not any(p.parent.iterdir()):
                p.parent.rmdir()
        print(f"{'deleted' if args.delete else 'would delete'} {rel(p)}")
    if not stale:
        print("nothing to prune")
    elif not args.delete:
        print(f"{len(stale)} unreferenced file(s); run with --delete to remove them")


# ----------------------------------------------------------------------- drift


def cmd_drift(args) -> None:
    store = Path(args.textures)
    index = load_index(store)
    named = export_paths(read_export(args.export))
    missing = [p for p in named if p not in index["textures"]]
    # A pointer file counts as present: CI checks out without LFS.
    absent = sorted(p for p, d in index["textures"].items() if not image_path(store, d).exists())
    print(f"textures named {len(named)}, stored {len(named) - len(missing)}, missing {len(missing)}")
    if missing:
        print("  missing: " + ", ".join(missing))
        msg = (f"{len(missing)} of {len(named)} textures the eidolon section names are not in textures/ "
               f"({', '.join(missing[:5])}{' and more' if len(missing) > 5 else ''}). The site draws them as colours.")
        print(f"::warning title=Textures missing::{annotation_escape(msg)}")
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_path and (missing or absent):
        with open(summary_path, "a") as fh:
            fh.write("### Texture drift\n\n")
            if missing:
                fh.write(f"{len(missing)} texture(s) not in textures/: " + ", ".join(f"`{p}`" for p in missing)
                         + "\n\nSee docs/recipe-browser/icons.md, \"Game textures\", to add them.\n\n")
            if absent:
                fh.write(f":x: {len(absent)} index entr(y/ies) point at a missing image file\n\n")
    if absent:
        die("index entries whose image file is not in the tree:\n  "
            + "\n  ".join(f"{p} -> {rel(image_path(store, index['textures'][p]))}" for p in absent))


# ---------------------------------------------------------------------- verify


def cmd_verify(args) -> None:
    store = Path(args.textures)
    index = load_index(store)
    problems = [f"{p}: {rel(image_path(store, d))} is missing"
                for p, d in sorted(index["textures"].items()) if not image_path(store, d).exists()]
    referenced = set(index["textures"].values())
    checked = pointers = unreferenced = 0
    for p in stored_files(store):
        if p.suffix != ".png" or not HASH.match(p.stem) or p.parent.name != p.stem[:2]:
            problems.append(f"{rel(p)}: not a <sha256>.png in its shard directory")
            continue
        oid = lfs_pointer_oid(p)
        if oid is not None:
            pointers += 1
            if oid != p.stem:
                problems.append(f"{rel(p)}: LFS pointer for {oid or 'nothing readable'}, not its name")
        else:
            checked += 1
            got = sha256_file(p)
            if got != p.stem:
                problems.append(f"{rel(p)}: content hashes to {got}")
        unreferenced += p.stem not in referenced
    print(f"{len(index['textures'])} index entr(y/ies), {checked} file(s) hashed, "
          f"{pointers} LFS pointer(s) checked by oid, {unreferenced} unreferenced (see prune)")
    if problems:
        die(f"{len(problems)} problem(s):\n  " + "\n  ".join(problems))
    print("textures verified")


# ------------------------------------------------------------------------ main


def main(argv: list[str] | None = None) -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--textures", default=str(TEXTURES), help="texture store (default: textures/)")
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("import", help="import textures from a game client and the pack's mods")
    s.add_argument("paths", nargs="*", help="texture paths (domain:path under textures/, no extension)")
    s.add_argument("--export", help="recipes.json: import every texture its eidolon section names")
    s.add_argument("--game", help="a game client (or server) install folder, with assets/survival")
    s.add_argument("--assets", action="append", default=[],
                   help="another folder of <domain>/textures/... (repeatable); mods-src/*/assets is always read")
    s.set_defaults(func=cmd_import)

    s = sub.add_parser("prune", help="delete unreferenced image files")
    s.add_argument("--delete", action="store_true", help="delete (default: only list)")
    s.set_defaults(func=cmd_prune)

    s = sub.add_parser("drift", help="compare a recipe export's texture paths with the index")
    s.add_argument("--export", required=True, help="recipes.json from the export mod")
    s.set_defaults(func=cmd_drift)

    sub.add_parser("verify", help="check index entries and file hashes").set_defaults(func=cmd_verify)

    args = p.parse_args(argv)
    args.func(args)


if __name__ == "__main__":
    main()
