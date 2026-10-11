"""Tests for tools/textures.py, the eidolon fitting sheet's game texture store.

Run from the repository root:

  python3 -m unittest discover -s tools/tests -p test_textures.py   # import tests skip without Pillow

Input PNGs come from test_icons' small encoder, not from the tool.
"""

from __future__ import annotations

import contextlib
import hashlib
import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
TOOL = HERE.parent / "textures.py"
_spec = importlib.util.spec_from_file_location("textures_tool", TOOL)
textures = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(textures)
_spec = importlib.util.spec_from_file_location("test_icons_helpers", HERE / "test_icons.py")
helpers = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(helpers)
png, needs_pillow = helpers.png, helpers.needs_pillow

RED, CLEAR = (255, 0, 0, 255), (0, 0, 0, 0)


def export_with(*material_paths, shape=None):
    return {"eidolon": {
        "layers": [{"id": "plates", "materials": [{"id": f"m{i}", "texture": p} for i, p in enumerate(material_paths)]},
                   {"id": "sash", "materials": [{"id": "none", "texture": None}]}],
        "shapeTextures": shape or {},
    }}


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.store = self.tmp / "textures"
        self.game = self.tmp / "game"
        self.assets = self.tmp / "assets"

    def put(self, root: Path, rel: str, data: bytes) -> Path:
        path = root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    def run_tool(self, *argv) -> str:
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            textures.main(["--textures", str(self.store), *argv])
        return out.getvalue()

    def fails(self, *argv) -> str:
        err = io.StringIO()
        with contextlib.redirect_stderr(err), contextlib.redirect_stdout(io.StringIO()), self.assertRaises(SystemExit):
            textures.main(["--textures", str(self.store), *argv])
        return err.getvalue()

    def index(self) -> dict:
        return json.loads((self.store / "index.json").read_text())["textures"]


class PathsTest(Base):
    def test_export_paths_are_the_materials_and_the_shape_once(self):
        e = export_with("game:block/metal/plate/iron", "game:block/metal/plate/steel",
                        shape={"plates": "game:block/metal/plate/steel", "lattice": "game:block/machine/lattice"})
        self.assertEqual(textures.export_paths(e), ["game:block/machine/lattice", "game:block/metal/plate/iron",
                                                   "game:block/metal/plate/steel"])
        self.assertEqual(textures.export_paths({"items": {}}), [])

    def test_game_domain_is_read_from_the_installs_three_folders(self):
        roots = [self.game / "assets"]
        f = self.put(self.game, "assets/survival/textures/block/wood/debarked/oak.png", b"x")
        g = self.put(self.game, "assets/creative/textures/block/glow.png", b"x")
        self.assertEqual(textures.asset_file(roots, "game:block/wood/debarked/oak"), f)
        self.assertEqual(textures.asset_file(roots, "game:block/glow"), g)
        self.assertIsNone(textures.asset_file(roots, "game:block/none"))

    def test_other_domains_are_read_from_their_own_folder(self):
        f = self.put(self.assets, "testmod/textures/block/chain.png", b"x")
        self.assertEqual(textures.asset_file([self.assets], "testmod:block/chain"), f)
        self.assertIsNone(textures.asset_file([self.assets], "game:block/chain"))

    def test_the_packs_own_mods_assets_are_always_read(self):
        roots = textures.asset_roots(None, [])
        self.assertIn(textures.ROOT / "mods-src" / "seraphhorizons" / "assets", roots)


@needs_pillow
class ImportTest(Base):
    def setUp(self):
        super().setUp()
        self.put(self.game, "assets/survival/textures/block/metal/plate/iron.png", png([[RED, CLEAR]]))
        # the same pixels, encoded differently and with a text chunk, under another name
        self.put(self.game, "assets/survival/textures/block/metal/plate/steel.png",
                 png([[RED, (9, 9, 9, 0)]], level=1, sub_filter=True, extra=[(b"tEXt", b"Comment\0hi")]))
        self.put(self.assets, "testmod/textures/block/chain.png", png([[(0, 0, 255, 255)] * 3] * 2))

    def test_imports_each_path_once_by_its_normalised_content(self):
        out = self.run_tool("import", "--game", str(self.game), "--assets", str(self.assets),
                            "game:block/metal/plate/iron", "game:block/metal/plate/steel", "testmod:block/chain")
        self.assertIn("3 added", out)
        index = self.index()
        self.assertEqual(sorted(index), ["game:block/metal/plate/iron", "game:block/metal/plate/steel", "testmod:block/chain"])
        # the colour under a transparent pixel and the metadata do not change the image
        self.assertEqual(index["game:block/metal/plate/iron"], index["game:block/metal/plate/steel"])
        for digest in index.values():
            f = self.store / digest[:2] / f"{digest}.png"
            self.assertEqual(hashlib.sha256(f.read_bytes()).hexdigest(), digest)
        from PIL import Image
        with Image.open(self.store / index["testmod:block/chain"][:2] / f"{index['testmod:block/chain']}.png") as im:
            self.assertEqual((im.size, im.mode), ((3, 2), "RGBA"))   # kept at its own size
        self.assertIn("textures verified", self.run_tool("verify"))
        self.assertIn("0 added, 0 changed, 3 unchanged", self.run_tool(
            "import", "--game", str(self.game), "--assets", str(self.assets),
            "game:block/metal/plate/iron", "game:block/metal/plate/steel", "testmod:block/chain"))

    def test_imports_the_paths_an_export_names(self):
        e = self.tmp / "recipes.json"
        e.write_text(json.dumps(export_with("game:block/metal/plate/iron", shape={"plates": "testmod:block/chain"})))
        self.run_tool("import", "--game", str(self.game), "--assets", str(self.assets), "--export", str(e))
        self.assertEqual(sorted(self.index()), ["game:block/metal/plate/iron", "testmod:block/chain"])

    def test_a_path_with_no_file_fails_after_storing_the_rest(self):
        err = self.fails("import", "--game", str(self.game), "game:block/metal/plate/iron", "game:block/metal/plate/gold")
        self.assertIn("game:block/metal/plate/gold", err)
        self.assertEqual(list(self.index()), ["game:block/metal/plate/iron"])

    def test_refuses_a_malformed_path_and_nothing_to_import(self):
        self.assertIn("not domain:path", self.fails("import", "block/metal/plate/iron"))
        self.assertIn("nothing to import", self.fails("import"))


class StoreTest(Base):
    def store_file(self, data: bytes, name: str | None = None) -> str:
        digest = name or hashlib.sha256(data).hexdigest()
        self.put(self.store, f"{digest[:2]}/{digest}.png", data)
        return digest

    def write_index(self, entries: dict):
        self.store.mkdir(parents=True, exist_ok=True)
        (self.store / "index.json").write_text(json.dumps({"schemaVersion": 1, "textures": entries}))

    def test_verify_accepts_lfs_pointers_by_their_oid(self):
        real = hashlib.sha256(b"image").hexdigest()
        pointer = f"version https://git-lfs.github.com/spec/v1\noid sha256:{real}\nsize 5\n".encode()
        self.store_file(pointer, real)
        self.write_index({"game:block/a": real})
        self.assertIn("1 LFS pointer(s)", self.run_tool("verify"))

    def test_verify_fails_on_content_that_is_not_its_name_and_a_missing_file(self):
        wrong = self.store_file(b"one", hashlib.sha256(b"two").hexdigest())
        self.write_index({"game:block/a": wrong, "game:block/b": "f" * 64})
        err = self.fails("verify")
        self.assertIn("content hashes to", err)
        self.assertIn("game:block/b", err)

    def test_a_corrupt_index_fails(self):
        self.write_index({"block/a": "nothex"})
        err = self.fails("verify")
        self.assertIn("not a domain:path", err)
        self.assertIn("not a sha256", err)

    def test_drift_warns_about_missing_paths_but_passes(self):
        self.write_index({"game:block/a": self.store_file(b"a")})
        e = self.tmp / "recipes.json"
        e.write_text(json.dumps(export_with("game:block/a", "game:block/b")))
        out = self.run_tool("drift", "--export", str(e))
        self.assertIn("missing 1", out)
        self.assertIn("::warning title=Textures missing::", out)
        self.assertIn("game:block/b", out)

    def test_drift_fails_on_an_entry_without_a_file(self):
        self.write_index({"game:block/a": "a" * 64})
        e = self.tmp / "recipes.json"
        e.write_text(json.dumps(export_with("game:block/a")))
        self.assertIn("not in the tree", self.fails("drift", "--export", str(e)))

    def test_prune_lists_then_deletes_unreferenced_files(self):
        keep = self.store_file(b"keep")
        stale = self.store_file(b"stale")
        self.write_index({"game:block/a": keep})
        self.assertIn("would delete", self.run_tool("prune"))
        self.assertTrue((self.store / stale[:2] / f"{stale}.png").exists())
        self.run_tool("prune", "--delete")
        self.assertFalse((self.store / stale[:2] / f"{stale}.png").exists())
        self.assertTrue((self.store / keep[:2] / f"{keep}.png").exists())


class RepositoryStoreTest(unittest.TestCase):
    def test_the_store_is_tracked_by_lfs(self):
        attributes = (textures.ROOT / ".gitattributes").read_text()
        self.assertIn("textures/*/*.png filter=lfs diff=lfs merge=lfs -text", attributes)

    def test_the_stores_index_is_well_formed(self):
        with contextlib.redirect_stdout(io.StringIO()):
            index = textures.load_index(textures.TEXTURES)
        self.assertIn("seraphhorizons:block/metal/armor-generic/chain-stainlesssteel", index["textures"])


if __name__ == "__main__":
    unittest.main()
