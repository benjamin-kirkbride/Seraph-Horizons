"""The amalgam pan's generated model files (mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with
the shared rig maths and uses its `requires` vocabulary (the muller, its shoes, and the two contents),
its reference poses are its own maths, the cell is rebuilt from the shipped shape, theta (the crank)
turns the muller and its shoes about the pan's axis and nothing else, the contents lie on the pan's
floor, every texture is declared and exists, and the provisional mercury texture is the generator's.
Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import math
import os
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
ASSETS = MOD / "assets" / "seraphhorizons"
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(MOD / "AmalgamPan" / "tools"))

from machinegen import rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("amalgampan_make_shape", MOD / "AmalgamPan" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)
import mercury_texture  # noqa: E402

RIG = json.loads((ASSETS / "config" / "amalgampan-rig.json").read_text())
SHAPE = json.loads((ASSETS / "shapes" / "block" / "amalgampan.json").read_text())
FRAME = json.loads((ASSETS / "shapes" / "block" / "amalgampan_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "AmalgamPan" / "rig-reference.json").read_text())
MERCURY = ASSETS / "textures" / "block" / "liquid" / "mercury.png"
# the fitted parts: the muller (spindle, hub, arms, crank) and its shoes; the contents: a mercury pool or amalgam
REQUIRES = {"muller", "shoes", "mercury", "amalgam", None}


def matrix(pid, theta):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta}, None)


def turn_y(m):
    return math.atan2(-m[2][0], m[0][0])


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(ids, ["muller", "shoes", "mercury", "amalgam", "frame"])
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            if p.get("ride"):
                self.assertIn(p["ride"], ids)
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        self.assertEqual(rigmath.progress_of(RIG), None)          # no work: nothing is posed by one
        for key in ("powerCell", "powerFace", "work", "trunkPath"):
            self.assertNotIn(key, RIG)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreaterEqual(len(poses), 10)
        for pose in poses:
            self.assertEqual(set(pose["matrices"]), {p["id"] for p in RIG["parts"]})
            for pid, want in pose["matrices"].items():
                got = matrix(pid, pose["theta"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at theta {pose['theta']}")

    def test_cell_is_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        (cell,) = RIG["cells"]
        self.assertEqual(cell["pos"], [0, 0, 0])
        self.assertGreater(cell["lid"], 0.9)

    def test_anchors(self):
        self.assertEqual(RIG["operatorSide"], "north")
        self.assertEqual(RIG["pan"]["pos"], [0.5, make_shape.FLOOR_TOP / 16, 0.5])


class Turning(unittest.TestCase):
    def test_the_crank_turns_the_muller_and_its_shoes_once_a_turn_about_the_pans_axis(self):
        for theta in (0.3, 1.7, -2.2, 5.0, 11.0):
            m = matrix("muller", theta)
            self.assertAlmostEqual(math.remainder(turn_y(m) - theta, 2 * math.pi), 0.0, places=9)
            axis = rigmath.apply(m, [0.5, 0.7, 0.5])
            for got, want in zip(axis, [0.5, 0.7, 0.5]):
                self.assertAlmostEqual(got, want, places=12)
            self.assertEqual(matrix("shoes", theta), m)

    def test_nothing_else_moves(self):
        ident = rigmath.m4()
        for pid in ("mercury", "amalgam", "frame"):
            for theta in (0.0, 2.5, -7.0):
                m = matrix(pid, theta)
                self.assertEqual([[round(v, 12) + 0.0 for v in row] for row in m], ident, pid)

    def test_the_crank_points_at_the_operator_at_rest(self):
        # the handle's elements lie north of the axis (towards the operator) in the authored pose
        handle = [e for e in SHAPE["elements"] if e["name"].startswith("muller_handle_")]
        self.assertTrue(handle)
        for e in handle:
            self.assertLess(max(e["from"][2], e["to"][2]), 8.0)


class Contents(unittest.TestCase):
    def layer(self, pid):
        els = [e for e in SHAPE["elements"] if rigmath.part_of(RIG["parts"], e["name"]) == pid]
        self.assertEqual(len(els), 8, pid)
        return min(min(e["from"][1], e["to"][1]) for e in els), max(max(e["from"][1], e["to"][1]) for e in els)

    def test_the_contents_are_flat_layers_on_the_floor_the_mercury_under_the_paste(self):
        floor = make_shape.FLOOR_TOP
        m0, m1 = self.layer("mercury")
        a0, a1 = self.layer("amalgam")
        self.assertLess(m0, floor)
        self.assertLess(a0, floor)
        self.assertAlmostEqual(m1, make_shape.MERCURY_TOP, places=4)
        self.assertAlmostEqual(a1, make_shape.AMALGAM_TOP, places=4)
        self.assertLess(floor, m1)
        self.assertLess(m1, a1)
        # the shoes stand out of the paste
        shoes = [e for e in SHAPE["elements"] if e["name"].startswith("shoe_")]
        self.assertGreater(min(max(e["from"][1], e["to"][1]) for e in shoes), a1)

    def test_each_content_is_its_own_requires_and_texture(self):
        for pid in ("mercury", "amalgam"):
            (part,) = [p for p in RIG["parts"] if p["id"] == pid]
            self.assertEqual(part["requires"], pid)
            self.assertEqual((part["drivers"], part["ride"]), ([], None))
            els = [e for e in SHAPE["elements"] if rigmath.part_of(RIG["parts"], e["name"]) == pid]
            self.assertEqual({f["texture"] for e in els for f in e["faces"].values()}, {"#" + pid})


class Textures(unittest.TestCase):
    def test_every_face_uses_a_declared_code_and_the_frame_matches(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertEqual(used, set(shape["textures"]))
        for code, path in FRAME["textures"].items():
            self.assertEqual(SHAPE["textures"][code], path, code)

    def test_the_shoes_have_their_own_code(self):
        shoes = [e for e in SHAPE["elements"] if e["name"].startswith("shoe_")]
        self.assertEqual({f["texture"] for e in shoes for f in e["faces"].values()}, {"#shoe"})

    def test_the_mods_own_textures_exist(self):
        for code, path in SHAPE["textures"].items():
            domain, rel = path.split(":", 1)
            if domain == "seraphhorizons":
                self.assertTrue((ASSETS / "textures" / f"{rel}.png").is_file(), f"{code}: {path}")
        self.assertEqual(SHAPE["textures"]["mercury"], "seraphhorizons:block/liquid/mercury")

    def test_the_mercury_texture_is_the_generators(self):
        rows = mercury_texture.decode(MERCURY.read_bytes())
        self.assertEqual((len(rows), len(rows[0])), (32, 32))
        self.assertEqual(rows, [list(r) for r in mercury_texture.pixels()])

    def test_every_game_texture_exists_in_the_game(self):
        install = os.environ.get("VINTAGE_STORY")
        if not install:
            raise unittest.SkipTest("VINTAGE_STORY is not set to a game or server install")
        assets = Path(install) / "assets"
        for code, path in SHAPE["textures"].items():
            domain, rel = path.split(":", 1)
            if domain == "game":
                self.assertTrue((assets / "survival" / "textures" / f"{rel}.png").is_file(), f"{code}: {path}")


if __name__ == "__main__":
    unittest.main()
