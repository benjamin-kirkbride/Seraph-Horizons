"""The vanner's generated model files (mods-src/seraphhorizons/Vanner/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with
the shared rig maths and uses its `requires` vocabulary, its reference poses are its own maths, the cells
are rebuilt from the shipped shape, theta turns the countershaft and shakes the frame across (its uprights
leaning with it) the same either way, the travel creeps the belt to the head whichever way theta went, the
anchors sit on their cells' faces (the axle's cross, the water inlet, three vanilla chute sections), and
every texture is declared (and exists in the game, when one is given).
Run with `python3 -m unittest discover -s tools/tests -p test_vanner_model.py`.
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
sys.path.insert(0, str(MOD / "Pipes" / "tools"))
sys.path.insert(0, str(MOD / "Vanner" / "tools"))

from machinegen import rigmath  # noqa: E402
from machinegen.geometry import flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("vanner_make_shape", MOD / "Vanner" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((ASSETS / "config" / "vanner-rig.json").read_text())
SHAPE = json.loads((ASSETS / "shapes" / "block" / "vanner.json").read_text())
FRAME = json.loads((ASSETS / "shapes" / "block" / "vanner_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "Vanner" / "rig-reference.json").read_text())
REQUIRES = {"countershaft", "shaker", "rollers", "belt", "creep", "feedbox", "pipecopper", "pipelead", None}


def matrix(pid, theta, travel=None):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta, "travel": abs(theta) if travel is None else travel})


def elements(prefix):
    return [e for e in SHAPE["elements"] if e["name"].startswith(prefix)]


def aabb(els):
    lo = [min(min(e["from"][k], e["to"][k]) for e in els) for k in range(3)]
    hi = [max(max(e["from"][k], e["to"][k]) for e in els) for k in range(3)]
    return lo, hi


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(ids, ["countershaft", "shaker", "uprightw", "uprighte", "belt", "head", "foot", "worm", "pawl", "feedbox",
                               "pipecopper", "pipelead", "frame"])
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            if p.get("ride"):
                self.assertIn(p["ride"], ids)
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        self.assertIsNone(rigmath.progress_of(RIG))          # no work: nothing is posed by one
        for key in ("work", "trunkPath"):
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
        self.assertGreaterEqual(len(poses), 20)
        for pose in poses:
            self.assertEqual(set(pose["matrices"]), {p["id"] for p in RIG["parts"]})
            for pid, want in pose["matrices"].items():
                got = matrix(pid, pose["theta"], pose["travel"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {pose['theta']}, {pose['travel']}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        self.assertEqual(sorted(tuple(c["pos"]) for c in RIG["cells"]), [(x, 0, z) for x in range(2) for z in range(3)])
        for c in RIG["cells"]:
            self.assertGreater(c["lid"], 0.9)
            self.assertNotIn("hollow", c)


class Motion(unittest.TestCase):
    def test_the_countershaft_turns_with_the_axle_and_the_frame_shakes_across(self):
        e = make_shape.E / 16
        for theta in (0.3, -1.7, math.pi / 2, 5.0):
            self.assertAlmostEqual(matrix("shaker", theta)[0][3], e * math.sin(theta), places=12)
            m = matrix("countershaft", theta)
            self.assertAlmostEqual(math.atan2(m[1][0], m[0][0]), math.remainder(theta, 2 * math.pi), places=9)
            axis = rigmath.apply(m, [1.5, 0.5, 2.0])
            for got, want in zip(axis, [1.5, 0.5, 2.0]):
                self.assertAlmostEqual(got, want, places=12)

    def test_the_uprights_carry_the_frame(self):
        drop = make_shape.UP_LEN * (1 - math.cos(make_shape.UP_SWING)) / 16
        for k, side in enumerate("we"):
            cx = sum(make_shape.RAILS_X[k]) / 2 / 16
            head = [cx, make_shape.UP_PIVOTS_Y[1] / 16, 1.0]
            for theta in (0.4, math.pi / 2, -math.pi / 2, 2.9):
                a = rigmath.apply(matrix(f"upright{side}", theta), head)
                b = rigmath.apply(matrix("shaker", theta), head)
                self.assertLessEqual(math.dist(a, b), drop + 1e-9)

    def test_the_belt_creeps_to_the_head_whichever_way_the_axle_turns(self):
        top = [0.6, (make_shape.HEAD[0] + make_shape.ROLL_R) / 16, make_shape.HEAD[1] / 16]
        for theta, travel in ((0.5, 0.5), (-0.5, 0.5), (6.0, 6.0), (-6.0, 6.0), (1.0, 50.0)):
            moved = rigmath.apply(matrix("head", theta, travel), top)
            self.assertLess(moved[2], top[2])
            for pid in ("head", "foot", "worm"):          # their turn is the travel's: the same for theta either way
                a, b = matrix(pid, theta, travel), matrix(pid, -theta, travel)
                self.assertEqual([row[:3] for row in a[:3]], [row[:3] for row in b[:3]], pid)
        # the ratchet a tooth a turn, the wheel a tooth a worm turn
        worm = {d["ratio"] for p in RIG["parts"] if p["id"] == "worm" for d in p["drivers"]}
        head = {d["ratio"] for p in RIG["parts"] if p["id"] == "head" for d in p["drivers"]}
        self.assertEqual(worm, {round(-1 / 12, 6)})
        self.assertEqual(head, {round(-1 / 144, 6)})

    def test_nothing_else_moves(self):
        ident = rigmath.m4()
        for pid in ("pawl", "feedbox", "pipecopper", "pipelead", "frame"):
            for theta in (0.0, 2.5, -7.0):
                m = matrix(pid, theta, 30.0)
                self.assertEqual([[round(v, 12) + 0.0 for v in row] for row in m], ident, pid)


class Anchors(unittest.TestCase):
    def test_the_power_face_meets_the_axles_cross(self):
        self.assertEqual((RIG["powerCell"], RIG["powerFace"]), ([1, 0, 2], "south"))
        lo, hi = aabb(elements("cs_entry"))
        self.assertEqual((hi[2], (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, hi[0] - lo[0], hi[1] - lo[1]), (48.0, 24.0, 8.0, 4.0, 4.0))

    def test_the_water_inlet_is_open_on_its_face(self):
        self.assertEqual((RIG["waterCell"], RIG["waterFace"]), ([1, 0, 0], "up"))
        for metal in ("copper", "lead"):
            walls = [e for e in elements(f"pipe{metal}_inlet_") if max(e["from"][1], e["to"][1]) == 16.0]
            lo, hi = aabb(walls)
            self.assertEqual((lo[0], hi[0], lo[2], hi[2]), (21.0, 27.0, 5.0, 11.0))

    def test_each_chute_section_is_vanillas_on_its_face(self):
        ring = {(a, b) for a in range(4, 12) for b in range(4, 12) if not (5 <= a < 11 and 5 <= b < 11)}
        for prefix, key, axis, flush in (("fr_chute_conc_", "concentrate", 2, 0.0), ("fr_chute_tail_", "tailings", 2, 48.0),
                                         ("fb_chute_", "feed", 1, 16.0)):
            els = elements(prefix)
            self.assertEqual(len(els), 4, prefix)
            cell = RIG[f"{key}Cell"]
            across = [k for k in range(3) if k != axis]
            cover = set()
            for e in els:
                lo = [min(e["from"][k], e["to"][k]) for k in range(3)]
                hi = [max(e["from"][k], e["to"][k]) for k in range(3)]
                self.assertIn(flush, (lo[axis], hi[axis]), prefix)
                for a in range(int(lo[across[0]] - cell[across[0]] * 16), int(hi[across[0]] - cell[across[0]] * 16)):
                    for b in range(int(lo[across[1]] - cell[across[1]] * 16), int(hi[across[1]] - cell[across[1]] * 16)):
                        cover.add((a, b))
                self.assertEqual({f["texture"] for f in e["faces"].values()}, {"#chute"})
            self.assertEqual(cover, ring, prefix)
        self.assertEqual(SHAPE["textures"]["chute"], "game:block/metal/sheet/copper1")
        self.assertEqual((RIG["concentrateFace"], RIG["tailingsFace"], RIG["feedFace"]), ("north", "south", "up"))


class Textures(unittest.TestCase):
    def test_every_face_uses_a_declared_code_and_the_frame_matches(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertEqual(used, set(shape["textures"]))
        for code, path in FRAME["textures"].items():
            self.assertEqual(SHAPE["textures"][code], path, code)

    def test_the_water_lines_metals_are_the_same_elements(self):
        def flat(metal):
            return sorted((e.name.split("_", 1)[1], [round(v, 4) for v in e.c], [round(v, 4) for v in e.size])
                          for e in flatten(elements(f"pipe{metal}_"), textures={}))
        self.assertEqual(flat("copper"), flat("lead"))

    def test_every_game_texture_exists_in_the_game(self):
        install = os.environ.get("VINTAGE_STORY")
        if not install:
            raise unittest.SkipTest("VINTAGE_STORY is not set to a game or server install")
        assets = Path(install) / "assets"
        for code, path in SHAPE["textures"].items():
            domain, rel = path.split(":", 1)
            self.assertEqual(domain, "game", code)
            self.assertTrue((assets / "survival" / "textures" / f"{rel}.png").is_file(), f"{code}: {path}")


if __name__ == "__main__":
    unittest.main()
