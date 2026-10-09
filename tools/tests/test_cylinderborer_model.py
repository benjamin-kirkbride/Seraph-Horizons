"""The cylinder borer's generated model files (mods-src/seraphhorizons/CylinderBorer/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with
the shared rig maths and uses its `requires` vocabulary, its reference poses are its own maths, the cells
are rebuilt from the shipped shape, the work is the cutter head's travel in blocks, the head and the feed
travel together and come back with the cylinder, the bar turns a quarter of the axle, both cylinders lie
concentric with the bar, and the oil cups follow the oil. Run with `python3 -m unittest discover -s tools/tests`.
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
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(MOD / "CylinderBorer" / "tools"))

from machinegen import checks, rigmath  # noqa: E402
from machinegen.geometry import flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("cylinderborer_make_shape", MOD / "CylinderBorer" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "cylinderborer-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "cylinderborer.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "cylinderborer_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "CylinderBorer" / "rig-reference.json").read_text())
# provisional fitted parts (the stages are still to be settled): the bar's bearings, the bar with its great wheel,
# the weighted feed, the cutter head (the wearing kit); the work is a small or a large cylinder
REQUIRES = {"bearings", "bar", "feed", "cutterhead", "blanksmall", "blanklarge", None}


def inputs(pose):
    return {k: pose[k] for k in ("theta", "travel", "work", "size", "presence", "oil")}


def matrix(pid, **ins):
    return rigmath.part_matrix(RIG["parts"], pid, ins, RIG["work"])


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            if p.get("ride"):
                self.assertIn(p["ride"], ids)
            for d in p["drivers"]:
                rigmath.validate_driver(d)
            # the renderer draws a part with a requires, a ride or a driver; anything else must be the frame
            if p["id"] != "frame":
                self.assertTrue(p["requires"] or p.get("ride") or p["drivers"], p["id"])
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_every_face_uses_a_declared_code(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertLessEqual(used, set(shape["textures"]))
            for code, path in shape["textures"].items():
                self.assertTrue(path.startswith("game:"), code)

    def test_every_texture_exists_in_the_game(self):
        install = os.environ.get("VINTAGE_STORY")
        if not install:
            raise unittest.SkipTest("VINTAGE_STORY is not set to a game or server install")
        assets = Path(install) / "assets"
        for code, path in SHAPE["textures"].items():
            domain, rel = path.split(":", 1)
            self.assertEqual(domain, "game", code)
            self.assertTrue((assets / "survival" / "textures" / f"{rel}.png").is_file(), f"{code}: {path}")

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreater(len(poses), 40)
        self.assertEqual({p["size"] for p in poses}, {0, 1, 2})
        for pose in poses[::3]:
            for pid, want in pose["matrices"].items():
                got = rigmath.part_matrix(RIG["parts"], pid, inputs(pose), RIG["work"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {inputs(pose)}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        cells = make_shape.shipped_cells(SHAPE, RIG["parts"], RIG["work"])
        self.assertEqual(cells, RIG["cells"])


class Bore(unittest.TestCase):
    def test_work_is_the_heads_travel(self):
        self.assertNotIn("trunkPath", RIG)
        w = rigmath.progress_of(RIG)
        self.assertEqual((w["name"], w["unit"]), ("cutter travel", "blocks"))
        self.assertEqual(w["end"], {"thin": 17 / 16, "thick": 21 / 16})
        self.assertGreater(RIG["bore"]["turnsPerBlock"], 0)
        for key in ("blanks", "cylinders", "box"):
            self.assertEqual(set(RIG["bore"][key]), {"thin", "thick"})

    def test_the_head_and_the_feed_travel_together_and_come_back_with_the_cylinder(self):
        for k, end in ((1, 17 / 16), (2, 21 / 16)):
            for w in (0.0, 0.3, end / 2, end):
                for pid in ("head", "feed"):
                    self.assertAlmostEqual(matrix(pid, work=w, size=k, presence=1.0)[2][3], -w, places=6, msg=f"{pid} {k} {w}")
            # the cylinder taken away at its end: the head is pushed back as p eases out
            for p in (0.5, 0.0):
                self.assertAlmostEqual(matrix("head", work=end, size=k, presence=p)[2][3], -end * p, places=6)

    def test_the_drum_and_the_weight_follow_the_head(self):
        for k, end in ((1, 17 / 16), (2, 21 / 16)):
            m = matrix("drum", work=end, size=k, presence=1.0)
            turned = -math.atan2(m[2][1], m[1][1])
            self.assertAlmostEqual(math.remainder(turned * make_shape.R_DRUM - end * 16, 2 * math.pi * make_shape.R_DRUM), 0.0, places=3)
            fall = -matrix("weight", work=end, size=k, presence=1.0)[1][3] * 16
            self.assertAlmostEqual(fall, end * 16 * make_shape.R_WDRUM / make_shape.R_DRUM, places=3)

    def test_the_bar_turns_a_quarter_of_the_axle_and_the_head_with_it(self):
        for th in (0.7, -2.1):
            for pid in ("bar", "head"):
                m = matrix(pid, theta=th)
                self.assertAlmostEqual(math.atan2(m[1][0], m[0][0]), math.remainder(-th / 4, 2 * math.pi), places=5, msg=pid)
        self.assertEqual((make_shape.PINION_T, make_shape.WHEEL_T), (8, 32))
        d = math.hypot(make_shape.BAR[0] - make_shape.ENTRY[0], make_shape.BAR[1] - make_shape.ENTRY[1])
        self.assertAlmostEqual(d, make_shape.R_PINION + make_shape.R_WHEEL)

    def test_every_skin_slice_is_cut_by_the_end(self):
        # each quarter of the cast skin has sunk into the wall once the cutters are past it, for either cylinder
        for k, t, end in ((1, "s", 17 / 16), (2, "l", 21 / 16)):
            for j in range(1, make_shape.SKIN_SLICES + 1):
                for q in "tlbr":
                    pid = f"s{t}{j}{q}"
                    rest = matrix(pid, work=0.0, size=k, presence=1.0)
                    done = matrix(pid, work=end, size=k, presence=1.0)
                    self.assertAlmostEqual(max(abs(rest[r][3]) for r in range(3)), 0.0, places=9, msg=pid)
                    self.assertAlmostEqual(max(abs(done[r][3]) for r in range(3)) * 16, make_shape.SKIN_SINK, places=4, msg=pid)

    def test_both_cylinders_lie_concentric_with_the_bar(self):
        els = flatten(SHAPE["elements"], textures={})
        for size in ("small", "large"):
            walls = [e for e in els if e.name.startswith(f"bl{size[0]}_wall")]
            xs = [p[0] for e in walls for p in e.corners()]
            ys = [p[1] for e in walls for p in e.corners()]
            self.assertAlmostEqual((min(xs) + max(xs)) / 2, make_shape.BAR[0], places=3)
            self.assertAlmostEqual((min(ys) + max(ys)) / 2, make_shape.BAR[1], places=3)
        # the large cylinder winds the V-blocks down and sets the cutters out
        self.assertAlmostEqual(matrix("saddles", size=2, presence=1.0)[1][3] * 16, -make_shape.V_DROP, places=4)
        self.assertAlmostEqual(matrix("saddles", size=1, presence=1.0)[1][3], 0.0)
        self.assertAlmostEqual(matrix("cutter1", size=2, presence=1.0)[1][3] * 16, make_shape.CUTTER_SET, places=4)


class Anchors(unittest.TestCase):
    def test_cells_and_faces(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        self.assertEqual(cells, {(x, y, z) for x in range(2) for y in range(2) for z in range(4)})
        self.assertEqual(checks.lid_gaps(RIG["cells"]), [])
        self.assertEqual(tuple(RIG["powerCell"]), (0, 1, 3))
        self.assertEqual(RIG["powerFace"], "south")
        self.assertEqual((RIG["infeedSide"], RIG["outputSide"]), ("east", "west"))
        for key in ("output", "chips", "drip"):
            self.assertEqual(len(RIG[key]["pos"]), 3)
        self.assertNotIn("oil", RIG)        # the oil is MachineOil's tank: no fill anchor

    def test_the_oil_level_follows_the_oil_input(self):
        (lvl,) = [p for p in RIG["parts"] if p["id"] == "oillevel"]
        self.assertEqual([d["input"] for d in lvl["drivers"]], ["oil"])
        heights = [matrix("oillevel", oil=oil)[1][1] * lvl["drivers"][0]["length"] * 16 for oil in (0.0, 0.5, 1.0)]
        self.assertAlmostEqual(heights[0], make_shape.OIL_EMPTY, places=5)
        self.assertAlmostEqual(heights[2], make_shape.OIL_FULL, places=5)
        self.assertAlmostEqual(heights[1], (heights[0] + heights[2]) / 2, places=5)


if __name__ == "__main__":
    unittest.main()
