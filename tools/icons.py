#!/usr/bin/env python3
# /// script
# requires-python = ">=3.11"
# dependencies = ["pillow>=10"]
# ///
"""Seraph Horizons icon tool: content-addressed item icons for the recipe browser.

Icons are rendered in the game client with `.blockitempngexport` (see
docs/recipe-browser/icons.md) and stored under icons/ as
icons/<first two hex digits>/<sha256>.png, tracked by Git LFS. icons/index.json
maps item codes to hashes. Subcommands:

  import      Map a client export (icons/block, icons/item) to item codes, resize,
              store each distinct image once and update the index. Needs Pillow:
              `uv run tools/icons.py import ...`.
  prune       Delete image files the index no longer references (dry run by default).
  drift       Compare a recipe export's items with the index; warns, never fails,
              on missing icons.
  verify      Every index entry has a file and every file's content matches its name.

Only `import` needs Pillow; the rest is stdlib only (Python 3.11+).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import struct
import sys
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ICONS = ROOT / "icons"
DEFAULT_SIZE = 64
HASH = re.compile(r"^[0-9a-f]{64}$")
CODE = re.compile(r"^[a-z0-9_-]+:[^\s:]+$")
# The client writes one folder per collectible class; icons/lod2 is a different
# export (block LOD textures) and is ignored.
KINDS = ("block", "item")
LFS_POINTER = b"version https://git-lfs.github.com/spec/v1\n"


def die(msg: str) -> None:
    print(f"error: {msg}", file=sys.stderr)
    sys.exit(1)


def rel(path: Path) -> str:
    return str(path.relative_to(ROOT)) if path.is_relative_to(ROOT) else str(path)


# ------------------------------------------------------------------------ index


def image_path(icons: Path, digest: str) -> Path:
    return icons / digest[:2] / f"{digest}.png"


def load_index(icons: Path, *, create: bool = False) -> dict:
    path = icons / "index.json"
    if not path.exists():
        if create:
            return {"schemaVersion": 1, "size": DEFAULT_SIZE, "icons": {}}
        die(f"{rel(path)} is missing")
    try:
        index = json.loads(path.read_text())
    except (OSError, ValueError) as e:
        die(f"{rel(path)}: cannot read: {e}")
    problems = []
    if not isinstance(index, dict):
        die(f"{rel(path)}: not a JSON object")
    if index.get("schemaVersion") != 1:
        problems.append(f"schemaVersion is {index.get('schemaVersion')!r}, expected 1")
    size = index.get("size")
    if not isinstance(size, int) or isinstance(size, bool) or size < 1:
        problems.append(f"size is {size!r}, expected a positive integer")
    icons_map = index.get("icons")
    if not isinstance(icons_map, dict):
        problems.append("icons is not an object")
    else:
        for code, digest in icons_map.items():
            if not CODE.match(code):
                problems.append(f"{code!r} is not a full item code")
            if not isinstance(digest, str) or not HASH.match(digest):
                problems.append(f"{code}: {digest!r} is not a sha256 hex digest")
    if problems:
        die(f"{rel(path)} is corrupt:\n  " + "\n  ".join(problems[:20]))
    return index


def write_index(icons: Path, index: dict) -> bool:
    """Write the index in its one canonical form; returns whether it changed."""
    text = json.dumps({
        "schemaVersion": 1,
        "size": index["size"],
        "icons": dict(sorted(index["icons"].items())),
    }, indent=2, ensure_ascii=False) + "\n"
    path = icons / "index.json"
    if path.exists() and path.read_text() == text:
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text)
    return True


def lfs_pointer_oid(path: Path) -> str | None:
    """The sha256 a Git LFS pointer file stands for, or None if it is not a pointer.

    Without `git lfs pull` (CI's default checkout) every image is a pointer; the
    pointer's oid is the sha256 of the real content, so it can still be checked.
    """
    with path.open("rb") as f:
        head = f.read(1024)
    if not head.startswith(LFS_POINTER):
        return None
    m = re.search(rb"^oid sha256:([0-9a-f]{64})$", head, re.M)
    return m.group(1).decode() if m else ""


def stored_files(icons: Path) -> list[Path]:
    """Everything in the two-hex-digit shard directories."""
    if not icons.exists():
        return []
    return sorted(p for d in icons.iterdir() if d.is_dir() and re.fullmatch(r"[0-9a-f]{2}", d.name)
                  for p in d.iterdir())


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


# ---------------------------------------------------------------- name mapping
#
# The client saves icons/block/<code path>.png and icons/item/<code path>.png.
# The bulk export replaces "/" in item paths with "-"; `.exponepng` does not,
# and `.exponepng hand` saves into block/ unless the last `.exponepng code` was
# for an item. The domain is not in the name, so
# game:foo and somemod:foo overwrite each other in one export. A file name is
# therefore mapped to a code with the domain given on the command line
# (`.blockitempngexport all <size> <domain>` exports one domain) or looked up in
# a recipe export's items.


def file_name(code: str, kind: str) -> str:
    """The name (without .png) the client gives the icon of `code`."""
    path = code.split(":", 1)[1]
    return path.replace("/", "-") if kind == "item" else path


def split_export_file(relpath: str) -> tuple[str, str]:
    """'item/plank-oak.png' -> ('item', 'plank-oak')."""
    kind, _, name = relpath.partition("/")
    return kind, name.removesuffix(".png").lower()


class Resolver:
    """Maps export file names to item codes."""

    def __init__(self, domain: str | None, items: dict | None):
        self.domain = domain
        self.by_name: dict[str, list[tuple[str, str]]] = {}
        for code, item in (items or {}).items():
            if domain and not code.startswith(domain + ":"):
                continue
            kind = item.get("kind", "item") if isinstance(item, dict) else "item"
            # `.exponepng` keeps the "/" that the bulk export replaces.
            for name in {file_name(code, kind), code.split(":", 1)[1]}:
                self.by_name.setdefault(name, []).append((code, kind))
        self.items = items

    def resolve(self, kind: str, name: str) -> tuple[str | None, str]:
        """(code, "") or (None, reason)."""
        if self.items is None:
            return f"{self.domain}:{name}", ""
        found = self.by_name.get(name, [])
        # `.exponepng hand` saves into block/ even for items, so the folder only
        # breaks ties.
        if len(found) > 1:
            found = [c for c in found if c[1] == kind] or found
        if len(found) == 1:
            return found[0][0], ""
        if not found:
            return None, "no item in the export has this name"
        return None, "ambiguous: " + ", ".join(sorted(c for c, _ in found))

    def kind_of(self, code: str) -> str | None:
        if self.items is None or not isinstance(self.items.get(code), dict):
            return None
        return self.items[code].get("kind", "item")


# ---------------------------------------------------------------------- images


def normalise(path: Path, size: int) -> bytes:
    """Raw RGBA pixels, size x size: the image scaled to fit, centred, on transparency.

    Colour under fully transparent pixels is zeroed so that images that look the
    same are the same.
    """
    from PIL import Image

    with Image.open(path) as im:
        im = im.convert("RGBA")
    w, h = im.size
    scale = min(size / w, size / h)
    nw, nh = max(1, round(w * scale)), max(1, round(h * scale))
    if (nw, nh) != (w, h):
        # Pillow resamples RGBA premultiplied, so transparent edges do not bleed colour.
        im = im.resize((nw, nh), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(im, ((size - nw) // 2, (size - nh) // 2))
    raw = bytearray(canvas.tobytes())
    for i in range(3, len(raw), 4):
        if raw[i] == 0:
            raw[i - 3:i] = b"\0\0\0"
    return bytes(raw)


def encode_png(raw: bytes, size: int) -> bytes:
    """A minimal RGBA PNG: IHDR, one IDAT, IEND. No metadata, so the bytes depend
    only on the pixels (and zlib)."""
    stride = size * 4
    scanlines = b"".join(b"\0" + raw[y * stride:(y + 1) * stride] for y in range(size))

    def chunk(tag: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data))

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(scanlines, 9))
            + chunk(b"IEND", b""))


# ---------------------------------------------------------------------- import


def cmd_import(args) -> None:
    try:
        import PIL  # noqa: F401
    except ImportError:
        die("import needs Pillow: run `uv run tools/icons.py import ...` (or `pip install pillow`)")
    if not args.domain and not args.items:
        die("say which mod the files belong to: --domain <modid> for a one-domain export, "
            "or --items <recipes.json> to look the names up in a recipe export")
    src = Path(args.export_dir)
    if not any((src / k).is_dir() for k in KINDS):
        die(f"{src} has neither block/ nor item/ (point it at the client's icons/ folder)")
    items = None
    if args.items:
        try:
            items = json.loads(Path(args.items).read_text())["items"]
        except (OSError, ValueError, KeyError, TypeError) as e:
            die(f"{args.items}: cannot read the export's items: {e}")
    icons = Path(args.icons)
    index = load_index(icons, create=True)
    if args.size and args.size != index["size"]:
        if index["icons"]:
            die(f"the index holds {index['size']}px icons; re-importing at another size "
                "means starting a new index")
        index["size"] = args.size
    size = index["size"]
    resolver = Resolver(args.domain, items)

    # file -> code, then drop codes that more than one file claims.
    claims: dict[str, list[tuple[str, Path]]] = {}
    unmapped: list[tuple[Path, str]] = []
    for kind in KINDS:
        base = src / kind
        for f in sorted(base.rglob("*.png")) if base.is_dir() else []:
            code, why = resolver.resolve(*split_export_file(f"{kind}/{f.relative_to(base).as_posix()}"))
            if code:
                claims.setdefault(code, []).append((kind, f))
            else:
                unmapped.append((f, why))
    chosen: dict[str, Path] = {}
    for code, files in sorted(claims.items()):
        if len(files) > 1:
            known = resolver.kind_of(code)
            files = [x for x in files if x[0] == known] or files
        if len(files) == 1:
            chosen[code] = files[0][1]
        else:
            for _, f in files:
                unmapped.append((f, f"{code} has icons in both block/ and item/"))

    # Existing images by pixel content, so an unchanged icon keeps its hash even
    # if this machine's zlib would encode it differently.
    by_pixels: dict[str, str] = {}
    for digest in sorted(set(index["icons"].values())):
        p = image_path(icons, digest)
        if not p.exists():
            continue
        if lfs_pointer_oid(p) is not None:
            die(f"{rel(p)} is a Git LFS pointer; run `git lfs pull` before importing")
        by_pixels[hashlib.sha256(normalise(p, size)).hexdigest()] = digest

    imported = unchanged = written = 0
    used: set[str] = set()
    for code, f in chosen.items():
        try:
            raw = normalise(f, size)
        except (OSError, ValueError) as e:
            unmapped.append((f, f"not a readable image: {e}"))
            continue
        key = hashlib.sha256(raw).hexdigest()
        digest = by_pixels.get(key)
        if digest is None:
            png = encode_png(raw, size)
            digest = hashlib.sha256(png).hexdigest()
            dest = image_path(icons, digest)
            if not dest.exists():
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(png)
                written += 1
            by_pixels[key] = digest
        used.add(digest)
        if index["icons"].get(code) == digest:
            unchanged += 1
        else:
            index["icons"][code] = digest
            imported += 1
    write_index(icons, index)

    print(f"imported {imported}, unchanged {unchanged}, distinct images {len(used)} "
          f"({written} new file(s)), unmapped {len(unmapped)}")
    for f, why in unmapped[:args.limit]:
        print(f"  unmapped {f.relative_to(src).as_posix()}: {why}")
    if len(unmapped) > args.limit:
        print(f"  ... and {len(unmapped) - args.limit} more")
    if imported:
        print(f"index now maps {len(index['icons'])} code(s); "
              "run `tools/icons.py prune` if icons were replaced")


# ----------------------------------------------------------------------- prune


def cmd_prune(args) -> None:
    icons = Path(args.icons)
    index = load_index(icons)
    referenced = set(index["icons"].values())
    stale = [p for p in stored_files(icons)
             if p.suffix == ".png" and HASH.match(p.stem) and p.stem not in referenced]
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


def drift(items: dict, index: dict) -> tuple[list[str], list[str]]:
    """(codes without an icon, icon codes that are not in the export), sorted."""
    have = set(index["icons"])
    return sorted(set(items) - have), sorted(have - set(items))


def by_mod(codes: list[str], items: dict) -> dict[str, list[str]]:
    groups: dict[str, list[str]] = {}
    for code in codes:
        item = items.get(code)
        mod = item.get("mod") if isinstance(item, dict) and item.get("mod") else code.split(":", 1)[0]
        groups.setdefault(mod, []).append(code)
    # Biggest gap first; the name breaks ties.
    return dict(sorted(groups.items(), key=lambda kv: (-len(kv[1]), kv[0])))


def annotation_escape(s: str) -> str:
    return s.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def cmd_drift(args) -> None:
    icons = Path(args.icons)
    index = load_index(icons)
    try:
        items = json.loads(Path(args.export).read_text())["items"]
        if not isinstance(items, dict):
            raise TypeError("items is not an object")
    except (OSError, ValueError, KeyError, TypeError) as e:
        die(f"{args.export}: cannot read the export's items: {e}")
    missing, orphaned = drift(items, index)
    groups = by_mod(missing, items)
    # A pointer file counts as present: CI checks out without LFS.
    absent = sorted(c for c, d in index["icons"].items() if not image_path(icons, d).exists())

    print(f"items {len(items)}, with icon {len(items) - len(missing)}, missing {len(missing)}, "
          f"icons for codes not in the export {len(orphaned)}")
    for mod, codes in groups.items():
        shown = ", ".join(codes[:args.limit])
        more = f" and {len(codes) - args.limit} more" if len(codes) > args.limit else ""
        print(f"  missing in {mod} ({len(codes)}): {shown}{more}")
    if orphaned:
        # The index is shared by every pack version, so these may still be in use.
        print(f"  not in this export: {', '.join(orphaned[:args.limit])}"
              + (f" and {len(orphaned) - args.limit} more" if len(orphaned) > args.limit else ""))

    if missing:
        top = ", ".join(f"{m} {len(c)}" for m, c in list(groups.items())[:5])
        if len(groups) > 5:
            top += f", {len(groups) - 5} more mod(s)"
        msg = (f"{len(missing)} of {len(items)} items have no icon ({top}). "
               "The site shows a placeholder for them.")
        print(f"::warning title=Icons missing::{annotation_escape(msg)}")

    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_path:
        with open(summary_path, "a") as fh:
            fh.write("### Icon drift\n\n")
            fh.write(f"- items: {len(items)}\n- with an icon: {len(items) - len(missing)}\n"
                     f"- missing: {len(missing)}\n- icons for codes not in this export: {len(orphaned)}\n\n")
            if groups:
                fh.write("| mod | missing | codes |\n|---|---|---|\n")
                for mod, codes in groups.items():
                    more = f" and {len(codes) - args.limit} more" if len(codes) > args.limit else ""
                    fh.write(f"| {mod} | {len(codes)} | {', '.join(f'`{c}`' for c in codes[:args.limit])}{more} |\n")
                fh.write("\nSee docs/recipe-browser/icons.md to add them.\n\n")
            if absent:
                fh.write(f":x: {len(absent)} index entr(y/ies) point at a missing image file\n\n")

    if absent:
        die("index entries whose image file is not in the tree:\n  "
            + "\n  ".join(f"{c} -> {rel(image_path(icons, index['icons'][c]))}" for c in absent[:50]))


# ---------------------------------------------------------------------- verify


def cmd_verify(args) -> None:
    icons = Path(args.icons)
    index = load_index(icons)
    problems = []
    for code, digest in sorted(index["icons"].items()):
        if not image_path(icons, digest).exists():
            problems.append(f"{code}: {rel(image_path(icons, digest))} is missing")
    referenced = set(index["icons"].values())
    checked = pointers = unreferenced = 0
    for p in stored_files(icons):
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
    print(f"{len(index['icons'])} index entr(y/ies), {checked} file(s) hashed, "
          f"{pointers} LFS pointer(s) checked by oid, {unreferenced} unreferenced (see prune)")
    if problems:
        die(f"{len(problems)} problem(s):\n  " + "\n  ".join(problems))
    print("icons verified")


# ------------------------------------------------------------------------ main


def main(argv: list[str] | None = None) -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--icons", default=str(ICONS), help="icon store (default: icons/)")
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("import", help="import a client icon export")
    s.add_argument("export_dir", help="the client's icons/ folder (with block/ and item/)")
    s.add_argument("--domain", help="mod domain of every file (for `.blockitempngexport all <size> <domain>`)")
    s.add_argument("--items", help="recipes.json whose items resolve file names to codes")
    s.add_argument("--size", type=int, help=f"icon size for a new index (default {DEFAULT_SIZE})")
    s.add_argument("--limit", type=int, default=20, help="unmapped files to list")
    s.set_defaults(func=cmd_import)

    s = sub.add_parser("prune", help="delete unreferenced image files")
    s.add_argument("--delete", action="store_true", help="delete (default: only list)")
    s.set_defaults(func=cmd_prune)

    s = sub.add_parser("drift", help="compare a recipe export with the index")
    s.add_argument("--export", required=True, help="recipes.json from the export mod")
    s.add_argument("--limit", type=int, default=10, help="codes to list per mod")
    s.set_defaults(func=cmd_drift)

    sub.add_parser("verify", help="check index entries and file hashes").set_defaults(func=cmd_verify)

    args = p.parse_args(argv)
    args.func(args)


if __name__ == "__main__":
    main()
