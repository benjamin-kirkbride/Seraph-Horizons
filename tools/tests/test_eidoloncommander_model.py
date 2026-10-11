"""The eidolon command tool's generated shape (mods-src/seraphhorizons/Eidolon/tools/make_commander.py).

The committed file is checked as it is: strict JSON, every texture the game's own, every face naming
a texture of the map, the two Jonas parts present under their prefixes, and the model about the
block's middle. With a game install ($VINTAGE_STORY) the generator is run too and its output must
equal the committed file. Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import os
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT / "mods-src" / "seraphhorizons" / "Eidolon" / "tools"

_spec = importlib.util.spec_from_file_location("eidolon_make_commander", TOOLS / "make_commander.py")
make_commander = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_commander)


def strict_load(path):
    def no_constants(name):
        raise ValueError(f"{path}: {name} is not JSON")

    return json.loads(Path(path).read_text(encoding="utf-8"), parse_constant=no_constants)


SHAPE = strict_load(make_commander.SHAPE_OUT)


def walk(elements):
    for el in elements:
        yield el
        yield from walk(el.get("children", []))


class CommanderShapeTests(unittest.TestCase):
    def test_textures_are_the_games_and_every_face_names_one(self):
        textures = SHAPE["textures"]
        for key, path in textures.items():
            self.assertTrue(path.startswith("game:"), f"{key}: {path}")
        for el in walk(SHAPE["elements"]):
            for face, f in el.get("faces", {}).items():
                self.assertIn(f["texture"].lstrip("#"), textures, f"{el['name']} {face}")

    def test_both_jonas_parts_and_the_casing_are_there(self):
        names = [el["name"] for el in walk(SHAPE["elements"])]
        self.assertEqual(len(names), len(set(names)), "element names repeat")
        self.assertIn("casing", names)
        self.assertTrue(any(n.startswith("cylinder-") for n in names))
        self.assertTrue(any(n.startswith("connector-") for n in names))

    def test_the_casing_sits_within_the_block(self):
        casing = SHAPE["elements"][0]
        for i in range(3):
            self.assertGreaterEqual(casing["from"][i], 0)
            self.assertLessEqual(casing["to"][i], 16)

    @unittest.skipUnless(os.environ.get("VINTAGE_STORY"), "needs a game install ($VINTAGE_STORY)")
    def test_the_generator_writes_the_committed_file(self):
        self.assertEqual(make_commander.main(["--check"]), 0)


if __name__ == "__main__":
    unittest.main()
