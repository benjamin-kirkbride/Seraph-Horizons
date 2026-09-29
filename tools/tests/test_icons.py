"""Tests for tools/icons.py.

Run from the repository root:

  uv run --with pillow python -m unittest discover -s tools/tests    # everything
  python3 -m unittest discover -s tools/tests                        # import tests skip without Pillow

Input PNGs are written by the small encoder below, not by the tool, and expected
values are worked out by hand. Outputs are decoded with Pillow.
"""

from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import struct
import subprocess
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

TOOL = Path(__file__).resolve().parents[1] / "icons.py"
_spec = importlib.util.spec_from_file_location("icons_tool", TOOL)
icons = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(icons)

try:
    from PIL import Image
except ImportError:
    Image = None
needs_pillow = unittest.skipUnless(Image, "Pillow is not installed")

RED, CLEAR = (255, 0, 0, 255), (0, 0, 0, 0)


def png(rows, *, level=6, sub_filter=False, extra=()):
    """RGBA PNG bytes from rows of (r, g, b, a). `sub_filter` and `level` vary the
    encoding; `extra` adds ancillary chunks (tEXt, tIME) before IDAT."""
    height, width = len(rows), len(rows[0])
    data = b""
    for row in rows:
        line = bytes(v for px in row for v in px)
        if sub_filter:
            line = bytes((line[i] - (line[i - 4] if i >= 4 else 0)) & 0xFF for i in range(len(line)))
        data += (b"\1" if sub_filter else b"\0") + line

    def chunk(tag, body):
        return struct.pack(">I", len(body)) + tag + body + struct.pack(">I", zlib.crc32(tag + body))

    out = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    for tag, body in extra:
        out += chunk(tag, body)
    return out + chunk(b"IDAT", zlib.compress(data, level)) + chunk(b"IEND", b"")


def solid(w, h, px):
    return [[px] * w for _ in range(h)]


def pointer(oid, size=1234):
    return f"version https://git-lfs.github.com/spec/v1\noid sha256:{oid}\nsize {size}\n"


def snapshot(root: Path) -> dict:
    return {p.relative_to(root).as_posix(): p.read_bytes() for p in sorted(root.rglob("*")) if p.is_file()}


class ToolCase(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.icons = self.tmp / "store"
        self.export = self.tmp / "export"

    def tearDown(self):
        self._tmp.cleanup()

    def run_tool(self, *args, env=None):
        return subprocess.run([sys.executable, str(TOOL), "--icons", str(self.icons), *args],
                              capture_output=True, text=True, env={**os.environ, **(env or {})})

    def put(self, relpath, data):
        p = self.export / relpath
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)

    def index(self):
        return json.loads((self.icons / "index.json").read_text())

    def write_index(self, mapping, size=64):
        self.icons.mkdir(parents=True, exist_ok=True)
        (self.icons / "index.json").write_text(json.dumps({"schemaVersion": 1, "size": size, "icons": mapping}))

    def stored(self):
        return sorted(p for p in self.icons.rglob("*.png"))

    def pixels(self, digest):
        with Image.open(self.icons / digest[:2] / f"{digest}.png") as im:
            return im.convert("RGBA")


class NameMapping(unittest.TestCase):
    ITEMS = {
        "game:plank-oak": {"kind": "item", "mod": "game"},
        "game:ladder-wood-north": {"kind": "block", "mod": "game"},
        "game:metalplate/copper": {"kind": "item", "mod": "game"},
        "game:widget": {"kind": "item", "mod": "game"},
        "examplemod:widget": {"kind": "item", "mod": "examplemod"},
        "examplemod:rack-oak-east": {"kind": "block", "mod": "examplemod"},
    }

    def test_split_file_name(self):
        self.assertEqual(icons.split_export_file("item/plank-oak.png"), ("item", "plank-oak"))
        self.assertEqual(icons.split_export_file("block/Ladder-Wood-North.png"), ("block", "ladder-wood-north"))
        self.assertEqual(icons.split_export_file("block/sub/thing.png"), ("block", "sub/thing"))

    def test_domain_given(self):
        r = icons.Resolver("examplemod", None)
        self.assertEqual(r.resolve("block", "rack-oak-east"), ("examplemod:rack-oak-east", ""))
        self.assertEqual(r.resolve("item", "widget"), ("examplemod:widget", ""))
        self.assertEqual(icons.Resolver("game", None).resolve("block", "sub/thing"), ("game:sub/thing", ""))

    def test_lookup_in_export(self):
        r = icons.Resolver(None, self.ITEMS)
        self.assertEqual(r.resolve("item", "plank-oak"), ("game:plank-oak", ""))
        self.assertEqual(r.resolve("block", "ladder-wood-north"), ("game:ladder-wood-north", ""))
        self.assertEqual(r.resolve("block", "rack-oak-east"), ("examplemod:rack-oak-east", ""))
        # The bulk export writes item "metalplate/copper" as metalplate-copper.png.
        self.assertEqual(r.resolve("item", "metalplate-copper"), ("game:metalplate/copper", ""))
        # `.exponepng hand` keeps the slash and may save an item into block/.
        self.assertEqual(r.resolve("block", "metalplate/copper"), ("game:metalplate/copper", ""))

    def test_same_path_in_two_domains_is_ambiguous(self):
        code, why = icons.Resolver(None, self.ITEMS).resolve("item", "widget")
        self.assertIsNone(code)
        self.assertEqual(why, "ambiguous: examplemod:widget, game:widget")
        self.assertEqual(icons.Resolver("examplemod", self.ITEMS).resolve("item", "widget"),
                         ("examplemod:widget", ""))

    def test_unknown_name(self):
        self.assertEqual(icons.Resolver(None, self.ITEMS).resolve("item", "nothing")[0], None)

    def test_client_names(self):
        self.assertEqual(icons.file_name("game:metalplate/copper", "item"), "metalplate-copper")
        self.assertEqual(icons.file_name("game:sub/thing", "block"), "sub/thing")


@needs_pillow
class Import(ToolCase):
    def test_identical_pixels_share_one_file(self):
        self.put("item/a.png", png(solid(4, 4, RED)))
        self.put("item/b.png", png(solid(4, 4, RED)))
        r = self.run_tool("import", str(self.export), "--domain", "game", "--size", "4")
        self.assertEqual(r.returncode, 0, r.stderr)
        idx = self.index()["icons"]
        self.assertEqual(sorted(idx), ["game:a", "game:b"])
        self.assertEqual(idx["game:a"], idx["game:b"])
        self.assertEqual(len(self.stored()), 1)
        self.assertIn("imported 2, unchanged 0, distinct images 1", r.stdout)

    def test_one_pixel_difference_gives_two_files(self):
        rows = solid(4, 4, RED)
        self.put("item/a.png", png(rows))
        rows[2][1] = (254, 0, 0, 255)
        self.put("item/b.png", png(rows))
        self.run_tool("import", str(self.export), "--domain", "game", "--size", "4")
        idx = self.index()["icons"]
        self.assertNotEqual(idx["game:a"], idx["game:b"])
        self.assertEqual(len(self.stored()), 2)

    def test_file_name_is_sha256_of_its_bytes(self):
        # Not grey: one flat grey would count as untextured (see Untextured below).
        self.put("block/stone.png", png(solid(8, 8, (90, 80, 70, 255))))
        self.run_tool("import", str(self.export), "--domain", "game")
        [f] = self.stored()
        digest = hashlib.sha256(f.read_bytes()).hexdigest()
        self.assertEqual(f.name, f"{digest}.png")
        self.assertEqual(f.parent.name, digest[:2])
        self.assertEqual(self.index()["icons"], {"game:stone": digest})

    def test_output_size(self):
        self.put("item/big.png", png(solid(100, 100, RED)))
        self.run_tool("import", str(self.export), "--domain", "game")
        idx = self.index()
        self.assertEqual(idx["size"], 64)
        self.assertEqual(self.pixels(idx["icons"]["game:big"]).size, (64, 64))

    def test_wide_image_is_centred_vertically(self):
        # 20x10 into 8x8: scale 0.4 -> 8x4, pasted at y = (8 - 4) // 2 = 2.
        self.put("item/wide.png", png(solid(20, 10, RED)))
        self.run_tool("import", str(self.export), "--domain", "game", "--size", "8")
        im = self.pixels(self.index()["icons"]["game:wide"])
        self.assertEqual(im.size, (8, 8))
        for y in (0, 1, 6, 7):
            self.assertEqual(im.getpixel((4, y)), CLEAR, f"row {y}")
        for y in (2, 3, 4, 5):
            self.assertEqual(im.getpixel((4, y)), RED, f"row {y}")
            self.assertEqual(im.getpixel((0, y))[3], 255, f"row {y}")

    def test_tall_image_is_centred_horizontally(self):
        # 10x20 into 8x8: 4x8, pasted at x = 2.
        self.put("item/tall.png", png(solid(10, 20, RED)))
        self.run_tool("import", str(self.export), "--domain", "game", "--size", "8")
        im = self.pixels(self.index()["icons"]["game:tall"])
        self.assertEqual([im.getpixel((x, 4))[3] for x in range(8)], [0, 0, 255, 255, 255, 255, 0, 0])

    def test_transparency_survives(self):
        rows = solid(4, 4, RED)
        rows[0][0] = (200, 100, 50, 0)     # invisible colour is dropped
        rows[0][3] = (10, 20, 30, 128)
        self.put("item/glass.png", png(rows))
        self.run_tool("import", str(self.export), "--domain", "game", "--size", "4")
        im = self.pixels(self.index()["icons"]["game:glass"])
        self.assertEqual(im.getpixel((0, 0)), CLEAR)
        self.assertEqual(im.getpixel((3, 0)), (10, 20, 30, 128))
        self.assertEqual(im.getpixel((3, 3)), RED)

    def test_import_twice_changes_nothing(self):
        rows = solid(6, 6, RED)
        rows[1][1] = (0, 0, 255, 100)
        self.put("item/a.png", png(rows))
        self.put("block/b.png", png(solid(6, 6, (0, 255, 0, 255))))
        self.run_tool("import", str(self.export), "--domain", "game")
        before = snapshot(self.icons)
        r = self.run_tool("import", str(self.export), "--domain", "game")
        self.assertEqual(snapshot(self.icons), before)
        self.assertIn("imported 0, unchanged 2, distinct images 2 (0 new file(s))", r.stdout)

    def test_encoding_and_metadata_do_not_change_the_hash(self):
        rows = solid(5, 3, (12, 34, 56, 255))
        rows[1][2] = (0, 0, 0, 0)
        other = [list(r) for r in rows]
        other[1][2] = (77, 77, 77, 0)      # different colour under full transparency
        self.put("item/plain.png", png(rows, level=9))
        self.put("item/fancy.png", png(other, level=0, sub_filter=True, extra=[
            (b"tEXt", b"Software\0something"),
            (b"tIME", struct.pack(">HBBBBB", 2026, 9, 28, 12, 0, 0)),
        ]))
        self.run_tool("import", str(self.export), "--domain", "game", "--size", "8")
        idx = self.index()["icons"]
        self.assertEqual(idx["game:plain"], idx["game:fancy"])

    def test_stored_png_has_no_metadata(self):
        self.put("item/a.png", png(solid(3, 3, RED), extra=[(b"tEXt", b"Comment\0hello"), (b"gAMA", struct.pack(">I", 45455))]))
        self.run_tool("import", str(self.export), "--domain", "game", "--size", "3")
        [f] = self.stored()
        data, pos, tags = f.read_bytes(), 8, []
        while pos < len(data):
            (length,) = struct.unpack(">I", data[pos:pos + 4])
            tags.append(data[pos + 4:pos + 8].decode())
            pos += 12 + length
        self.assertEqual(tags[0], "IHDR")
        self.assertEqual(tags[-1], "IEND")
        self.assertEqual(set(tags[1:-1]), {"IDAT"})

    def test_invalid_code_is_not_imported(self):
        self.put("item/blue morpho.png", png(solid(2, 2, RED)))
        r = self.run_tool("import", str(self.export), "--domain", "game", "--size", "2")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(self.index()["icons"], {})
        self.assertIn("unmapped item/blue morpho.png: 'game:blue morpho' is not a valid item code", r.stdout)
        self.assertEqual(self.run_tool("verify").returncode, 0)

    def test_new_icon_replaces_mapping(self):
        self.put("item/a.png", png(solid(4, 4, RED)))
        self.run_tool("import", str(self.export), "--domain", "game", "--size", "4")
        first = self.index()["icons"]["game:a"]
        self.put("item/a.png", png(solid(4, 4, (0, 0, 255, 255))))
        r = self.run_tool("import", str(self.export), "--domain", "game")
        second = self.index()["icons"]["game:a"]
        self.assertNotEqual(first, second)
        self.assertIn("imported 1, unchanged 0", r.stdout)
        self.assertEqual(self.pixels(second).getpixel((0, 0)), (0, 0, 255, 255))

    def test_index_is_sorted_and_stable(self):
        # Separate runs, so the codes arrive out of order.
        for name in ("zeta", "alpha", "mid"):
            self.put(f"{name}/item/{name}.png", png(solid(2, 2, RED)))
            self.run_tool("import", str(self.export / name), "--domain", "game", "--size", "2")
        text = (self.icons / "index.json").read_text()
        self.assertTrue(text.startswith('{\n  "schemaVersion": 1,\n  "size": 2,\n  "icons": {\n    "game:alpha"'), text)
        self.assertLess(text.index("game:mid"), text.index("game:zeta"))

    def test_mapping_through_export_items(self):
        items = {"items": {"game:metalplate/copper": {"kind": "item", "mod": "game"},
                           "game:widget": {"kind": "item", "mod": "game"},
                           "examplemod:widget": {"kind": "item", "mod": "examplemod"}}}
        (self.tmp / "recipes.json").write_text(json.dumps(items))
        self.put("item/metalplate-copper.png", png(solid(2, 2, RED)))
        self.put("item/widget.png", png(solid(2, 2, RED)))
        self.put("item/unknown.png", png(solid(2, 2, RED)))
        r = self.run_tool("import", str(self.export), "--items", str(self.tmp / "recipes.json"), "--size", "2")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(list(self.index()["icons"]), ["game:metalplate/copper"])
        self.assertIn("unmapped 2 (1 not in the export)", r.stdout)
        # Ambiguous first, although "unknown" sorts before "widget".
        self.assertIn("  unmapped item/widget.png: ambiguous: examplemod:widget, game:widget\n"
                      "  unmapped item/unknown.png: no item in the export has this name\n", r.stdout)

    def test_needs_domain_or_items(self):
        self.put("item/a.png", png(solid(2, 2, RED)))
        r = self.run_tool("import", str(self.export))
        self.assertEqual(r.returncode, 1)
        self.assertIn("--domain", r.stderr)


def shaded_white(size=8):
    """The broken export's look: a white cube in three flat shades on transparency."""
    rows = []
    for y in range(size):
        row = []
        for x in range(size):
            if x in (0, size - 1) or y in (0, size - 1):
                row.append(CLEAR)
            elif y < size // 3:
                row.append((255, 255, 255, 255))
            elif x < size // 2:
                row.append((224, 224, 224, 255))
            else:
                row.append((184, 184, 184, 255))
        rows.append(row)
    return rows


class IconPath(unittest.TestCase):
    # Written out by hand from the rule in tools/icon-export/Core/IconPaths.cs; the C# tests
    # check the same vectors against the mod's own implementation.
    def test_paths(self):
        cases = [
            ("game:crate", "block", "game/block/crate.png"),
            ("materialneeds:crate", "block", "materialneeds/block/crate.png"),
            ("game:clutter-art/bottle", "block", "game/block/clutter-art/bottle.png"),
            ("tankardsandgoblets:t&g-winebottle-blue", "item", "tankardsandgoblets/item/t%26g-winebottle-blue.png"),
            ("tankardsandgoblets:tankard-woodtype-acacia.-bismuth", "item",
             "tankardsandgoblets/item/tankard-woodtype-acacia%2E-bismuth.png"),
            ("game:Foo", "item", "game/item/%46oo.png"),
            ("game:con", "item", "game/item/%63on.png"),
            ("game:a//b", "item", "game/item/a%2F%2Fb.png"),
            ("game:ümlaut", "item", "game/item/%C3%BCmlaut.png"),
        ]
        for code, kind, path in cases:
            with self.subTest(code=code):
                self.assertEqual(icons.icon_path(code, kind), path)


@needs_pillow
class ManifestLayout(ToolCase):
    def put_icon(self, relpath, rows, code, kind):
        self.put(relpath, png(rows))
        self.entries[relpath] = {"code": code, "kind": kind, "size": 4, "check": "ok"}

    def setUp(self):
        super().setUp()
        self.entries = {}

    def write_manifest(self, **extra):
        self.export.mkdir(parents=True, exist_ok=True)
        (self.export / "manifest.json").write_text(json.dumps(
            {"schemaVersion": 1, "generator": "test", "icons": self.entries, "failed": [], **extra}))

    def test_codes_come_from_the_manifest(self):
        blue = (0, 0, 255, 255)
        self.put_icon("game/block/crate.png", solid(4, 4, RED), "game:crate", "block")
        self.put_icon("materialneeds/block/crate.png", solid(4, 4, blue), "materialneeds:crate", "block")
        self.put_icon("game/block/clutter-art/bottle.png", solid(4, 4, (0, 255, 0, 255)), "game:clutter-art/bottle", "block")
        self.put_icon("tankardsandgoblets/item/t%26g-winebottle-blue.png", solid(4, 4, (9, 99, 199, 255)),
                      "tankardsandgoblets:t&g-winebottle-blue", "item")
        self.write_manifest()
        r = self.run_tool("import", str(self.export), "--size", "4")
        self.assertEqual(r.returncode, 0, r.stderr)
        idx = self.index()["icons"]
        self.assertEqual(sorted(idx), ["game:clutter-art/bottle", "game:crate", "materialneeds:crate",
                                       "tankardsandgoblets:t&g-winebottle-blue"])
        self.assertEqual(self.pixels(idx["game:crate"]).getpixel((0, 0)), RED)
        self.assertEqual(self.pixels(idx["materialneeds:crate"]).getpixel((0, 0)), blue)
        self.assertIn("imported 4, unchanged 0, distinct images 4 (4 new file(s)), unmapped 0", r.stdout)

    def test_domain_does_not_apply(self):
        self.put_icon("game/item/a.png", solid(2, 2, RED), "game:a", "item")
        self.write_manifest()
        r = self.run_tool("import", str(self.export), "--domain", "game")
        self.assertEqual(r.returncode, 1)
        self.assertIn("--domain does not apply", r.stderr)

    def test_manifest_and_path_must_agree(self):
        self.put_icon("game/item/stick.png", solid(2, 2, RED), "game:flint", "item")
        self.put_icon("game/item/ok.png", solid(2, 2, RED), "game:ok", "block")
        self.write_manifest()
        r = self.run_tool("import", str(self.export), "--size", "2")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertNotIn("game:flint", self.index()["icons"])
        self.assertNotIn("game:ok", self.index()["icons"])
        self.assertIn("unmapped game/item/stick.png: the manifest says item game:flint, "
                      "whose file would be game/item/flint.png", r.stdout)
        self.assertIn("unmapped game/item/ok.png: the manifest says block game:ok, "
                      "whose file would be game/block/ok.png", r.stdout)

    def test_missing_and_escaping_files_are_not_imported(self):
        self.entries["game/item/gone.png"] = {"code": "game:gone", "kind": "item"}
        self.put("../outside.png", png(solid(2, 2, RED)))
        self.entries["../outside.png"] = {"code": "game:outside", "kind": "item"}
        self.write_manifest()
        r = self.run_tool("import", str(self.export), "--size", "2")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(self.index()["icons"], {})
        self.assertIn("unmapped game/item/gone.png: listed in the manifest, but the file is missing", r.stdout)
        self.assertIn("unmapped 2", r.stdout)

    def test_files_not_in_the_manifest_are_ignored(self):
        self.put_icon("game/item/a.png", solid(2, 2, RED), "game:a", "item")
        self.put("game/item/b.png", png(solid(2, 2, RED)))
        self.write_manifest()
        r = self.run_tool("import", str(self.export), "--size", "2")
        self.assertEqual(list(self.index()["icons"]), ["game:a"])
        self.assertIn("note: 1 PNG file(s)", r.stdout)

    def test_block_and_item_with_one_code(self):
        self.put_icon("game/item/torch.png", solid(2, 2, RED), "game:torch", "item")
        self.put_icon("game/block/torch.png", solid(2, 2, (0, 0, 255, 255)), "game:torch", "block")
        self.write_manifest()
        r = self.run_tool("import", str(self.export), "--size", "2")
        self.assertEqual(self.index()["icons"], {})
        self.assertIn("game:torch has icons as both a block and an item", r.stdout)
        # The recipe export says which one the site shows.
        (self.tmp / "recipes.json").write_text(json.dumps({"items": {"game:torch": {"kind": "block"}}}))
        self.run_tool("import", str(self.export), "--items", str(self.tmp / "recipes.json"))
        self.assertEqual(self.pixels(self.index()["icons"]["game:torch"]).getpixel((0, 0)), (0, 0, 255, 255))

    def test_not_a_manifest(self):
        self.export.mkdir(parents=True)
        (self.export / "manifest.json").write_text(json.dumps({"schemaVersion": 7, "icons": {}}))
        r = self.run_tool("import", str(self.export))
        self.assertEqual(r.returncode, 1)
        self.assertIn("not a schemaVersion 1 icon manifest", r.stderr)


@needs_pillow
class Untextured(ToolCase):
    def test_image_check(self):
        def check(rows):
            p = self.tmp / "x.png"
            p.write_bytes(png(rows))
            return icons.image_check(p)

        self.assertEqual(check(shaded_white()), "untextured")
        coloured = shaded_white()
        coloured[3][3] = (200, 120, 80, 255)
        self.assertEqual(check(coloured), "ok")
        self.assertEqual(check(solid(4, 4, CLEAR)), "transparent")
        # Colour under zero alpha is invisible.
        self.assertEqual(check(solid(4, 4, (255, 0, 0, 0))), "transparent")
        # A grey texture with more shades than the broken export draws.
        self.assertEqual(check([[(v, v, v, 255) for v in range(60, 100)]]), "ok")
        self.assertEqual(check([[(v, v, v, 255) for v in range(100, 116)]]), "untextured")
        self.assertEqual(check([[(v, v, v, 255) for v in range(100, 117)]]), "ok")
        self.assertEqual(check([[(100, 101, 102, 255)]]), "untextured")
        self.assertEqual(check([[(100, 100, 103, 255)]]), "ok")

    def test_mostly_untextured_export_is_refused(self):
        for name in ("a", "b", "c"):
            self.put(f"item/{name}.png", png(shaded_white()))
        self.put("item/d.png", png(solid(8, 8, RED)))
        r = self.run_tool("import", str(self.export), "--domain", "game")
        self.assertEqual(r.returncode, 1)
        self.assertIn("3 of 4 images look untextured", r.stderr)
        self.assertFalse((self.icons / "index.json").exists())
        self.assertEqual(self.stored(), [])

    def test_refusal_can_be_overridden(self):
        for name in ("a", "b", "c"):
            self.put(f"item/{name}.png", png(shaded_white()))
        r = self.run_tool("import", str(self.export), "--domain", "game", "--allow-untextured")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(sorted(self.index()["icons"]), ["game:a", "game:b", "game:c"])
        self.assertIn("warning: 3 of 3 images look untextured", r.stdout)

    def test_half_is_not_most(self):
        self.put("item/a.png", png(shaded_white()))
        self.put("item/b.png", png(solid(8, 8, RED)))
        r = self.run_tool("import", str(self.export), "--domain", "game")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(sorted(self.index()["icons"]), ["game:a", "game:b"])
        self.assertIn("warning: 1 of 2 images look untextured: game:a", r.stdout)

    def test_manifest_layout_is_checked_too(self):
        self.put("game/item/a.png", png(shaded_white()))
        self.export.mkdir(parents=True, exist_ok=True)
        (self.export / "manifest.json").write_text(json.dumps({"schemaVersion": 1, "icons": {
            "game/item/a.png": {"code": "game:a", "kind": "item"}}}))
        r = self.run_tool("import", str(self.export))
        self.assertEqual(r.returncode, 1)
        self.assertIn("1 of 1 images look untextured", r.stderr)

    def test_transparent_images_are_not_imported(self):
        self.put("item/blank.png", png(solid(4, 4, CLEAR)))
        self.put("item/red.png", png(solid(4, 4, RED)))
        r = self.run_tool("import", str(self.export), "--domain", "game", "--size", "4")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(list(self.index()["icons"]), ["game:red"])
        self.assertIn("unmapped item/blank.png: fully transparent: nothing was drawn", r.stdout)


class Drift(ToolCase):
    EXPORT = {"items": {
        "game:stick": {"kind": "item", "mod": "game"},
        "game:flint": {"kind": "item", "mod": "game"},
        "examplemod:widget": {"kind": "item", "mod": "examplemod"},
        "examplemod:gear": {"kind": "item", "mod": "examplemod"},
    }}
    H1, H2 = "1" * 64, "2" * 64

    def setUp(self):
        super().setUp()
        self.write_index({"game:stick": self.H1, "game:oldthing": self.H2})
        for h in (self.H1, self.H2):
            (self.icons / h[:2]).mkdir(exist_ok=True)
            (self.icons / h[:2] / f"{h}.png").write_text(pointer(h))
        self.export_file = self.tmp / "recipes.json"
        self.export_file.write_text(json.dumps(self.EXPORT))

    def test_lists(self):
        missing, orphaned = icons.drift(self.EXPORT["items"], self.index())
        self.assertEqual(missing, ["examplemod:gear", "examplemod:widget", "game:flint"])
        self.assertEqual(orphaned, ["game:oldthing"])
        self.assertEqual(icons.by_mod(missing, self.EXPORT["items"]),
                         {"examplemod": ["examplemod:gear", "examplemod:widget"], "game": ["game:flint"]})

    def test_missing_icons_warn_but_pass(self):
        summary = self.tmp / "summary.md"
        r = self.run_tool("drift", "--export", str(self.export_file), env={"GITHUB_STEP_SUMMARY": str(summary)})
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertIn("items 4, with icon 1, missing 3, icons for codes not in the export 1", r.stdout)
        self.assertIn("missing in examplemod (2): examplemod:gear, examplemod:widget", r.stdout)
        self.assertIn("::warning title=Icons missing::3 of 4 items have no icon (examplemod 2, game 1). "
                      "The site shows a placeholder for them.\n", r.stdout)
        text = summary.read_text()
        self.assertIn("### Icon drift", text)
        self.assertIn("| examplemod | 2 | `examplemod:gear`, `examplemod:widget` |", text)

    def test_limit_caps_the_list(self):
        r = self.run_tool("drift", "--export", str(self.export_file), "--limit", "1")
        self.assertIn("missing in examplemod (2): examplemod:gear and 1 more", r.stdout)

    def test_no_warning_when_complete(self):
        self.write_index({c: self.H1 for c in self.EXPORT["items"]})
        r = self.run_tool("drift", "--export", str(self.export_file))
        self.assertEqual(r.returncode, 0)
        self.assertNotIn("::warning", r.stdout)

    def test_corrupt_index_fails(self):
        (self.icons / "index.json").write_text('{"schemaVersion": 1, "size": 64, "icons": {')
        r = self.run_tool("drift", "--export", str(self.export_file))
        self.assertEqual(r.returncode, 1)
        self.assertIn("index.json", r.stderr)

    def test_bad_hash_in_index_fails(self):
        self.write_index({"game:stick": "not-a-hash"})
        self.assertEqual(self.run_tool("drift", "--export", str(self.export_file)).returncode, 1)

    def test_unreadable_export_fails(self):
        self.export_file.write_text("[1, 2")
        self.assertEqual(self.run_tool("drift", "--export", str(self.export_file)).returncode, 1)

    def test_drift_and_verify_run_without_pillow(self):
        # A PIL package that fails to import stands in for a machine without Pillow (CI's
        # drift step installs nothing).
        fake = self.tmp / "nopil" / "PIL"
        fake.mkdir(parents=True)
        (fake / "__init__.py").write_text("raise ImportError('Pillow is not installed')\n")
        env = {"PYTHONPATH": str(fake.parent)}
        r = self.run_tool("drift", "--export", str(self.export_file), env=env)
        self.assertEqual(r.returncode, 0, r.stderr)
        r = self.run_tool("verify", env=env)
        self.assertEqual(r.returncode, 0, r.stderr)
        # The stand-in works: import does notice.
        r = self.run_tool("import", str(self.tmp), "--domain", "game", env=env)
        self.assertIn("import needs Pillow", r.stderr)

    def test_absent_image_fails(self):
        (self.icons / self.H2[:2] / f"{self.H2}.png").unlink()
        r = self.run_tool("drift", "--export", str(self.export_file))
        self.assertEqual(r.returncode, 1)
        self.assertIn("game:oldthing", r.stderr)


class Verify(ToolCase):
    def store(self, name, data: bytes):
        (self.icons / name[:2]).mkdir(parents=True, exist_ok=True)
        (self.icons / name[:2] / f"{name}.png").write_bytes(data)

    def test_good_file_and_pointer_pass(self):
        data = png(solid(2, 2, RED))
        good = hashlib.sha256(data).hexdigest()
        other = "ab" + "c" * 62
        self.store(good, data)
        self.store(other, pointer(other).encode())
        self.write_index({"game:a": good, "game:b": other})
        r = self.run_tool("verify")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertIn("1 file(s) hashed, 1 LFS pointer(s)", r.stdout)

    def test_content_that_does_not_match_its_name_fails(self):
        name = hashlib.sha256(b"something else").hexdigest()
        self.store(name, png(solid(2, 2, RED)))
        self.write_index({"game:a": name})
        r = self.run_tool("verify")
        self.assertEqual(r.returncode, 1)
        self.assertIn(f"{name}.png: content hashes to", r.stderr)

    def test_pointer_for_another_object_fails(self):
        name = "d" * 64
        self.store(name, pointer("e" * 64).encode())
        self.write_index({"game:a": name})
        self.assertEqual(self.run_tool("verify").returncode, 1)

    def test_missing_file_fails(self):
        self.write_index({"game:a": "f" * 64})
        r = self.run_tool("verify")
        self.assertEqual(r.returncode, 1)
        self.assertIn("game:a:", r.stderr)


class Prune(ToolCase):
    def setUp(self):
        super().setUp()
        self.keep, self.drop = "a1" + "0" * 62, "b2" + "0" * 62
        for h in (self.keep, self.drop):
            (self.icons / h[:2]).mkdir(parents=True)
            (self.icons / h[:2] / f"{h}.png").write_text(pointer(h))
        self.write_index({"game:a": self.keep})

    def test_dry_run_deletes_nothing(self):
        before = snapshot(self.icons)
        r = self.run_tool("prune")
        self.assertEqual(snapshot(self.icons), before)
        self.assertIn(f"would delete", r.stdout)
        self.assertIn(self.drop, r.stdout)
        self.assertNotIn(self.keep, r.stdout)

    def test_delete_removes_only_unreferenced(self):
        self.run_tool("prune", "--delete")
        self.assertEqual(sorted(snapshot(self.icons)), ["a1/" + self.keep + ".png", "index.json"])
        self.assertFalse((self.icons / "b2").exists())


if __name__ == "__main__":
    unittest.main()
