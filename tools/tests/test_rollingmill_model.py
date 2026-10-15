"""The rolling mill's generated model files (mods-src/seraphhorizons/RollingMill/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the
shared rig maths and uses its `requires` vocabulary, its reference poses are its own maths, the cells are
rebuilt from the shipped shape, the work is three passes geared to the axle (the tumbling shaft once a pass),
the rolls turn a third of the axle in opposite directions, the last pass is the press brake's angle, the bar
grows out of each pass and the angle is handed out, and the oil cups follow the oil. Run with
`python3 -m unittest discover -s tools/tests -p test_rollingmill_model.py`.
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
sys.path.insert(0, str(MOD / "RollingMill" / "tools"))

from machinegen import checks, rigmath  # noqa: E402
from machinegen.geometry import flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("rollingmill_make_shape", MOD / "RollingMill" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "rollingmill-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "rollingmill.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "rollingmill_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "RollingMill" / "rig-reference.json").read_text())
ANGLE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "item" / "angle.json").read_text())
# the fitted parts (provisional: the build stages are the owner's to settle) and the work, a hot bar of iron or steel
REQUIRES = {"pinions", "spindles", "rolls", "catcher", "tray", "bariron", "barsteel", None}


def inputs(pose):
    return {k: pose[k] for k in ("theta", "travel", "work", "size", "presence", "oil")}


def matrix(pid, **ins):
    return rigmath.part_matrix(RIG["parts"], pid, ins, RIG["work"])


def x_angle(m):
    return math.atan2(m[2][1], m[1][1])


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            for d in p["drivers"]:
                rigmath.validate_driver(d)
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
            rel = path.split(":", 1)[1]
            self.assertTrue((assets / "survival" / "textures" / f"{rel}.png").is_file(), f"{code}: {path}")

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreater(len(poses), 60)
        self.assertEqual({p["size"] for p in poses}, {0, 1, 2})
        for pose in poses[::3]:
            for pid, want in pose["matrices"].items():
                got = rigmath.part_matrix(RIG["parts"], pid, inputs(pose), RIG["work"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {inputs(pose)}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"], RIG["work"]), RIG["cells"])


class Mill(unittest.TestCase):
    def test_the_work_is_three_passes_geared_to_the_axle(self):
        self.assertNotIn("trunkPath", RIG)
        w = rigmath.progress_of(RIG)
        self.assertEqual((w["name"], w["unit"], w["end"]), ("passes", "passes", {"thin": 3.0, "thick": 3.0}))
        mill = RIG["mill"]
        self.assertEqual(mill["passes"], 3)
        self.assertEqual(mill["turnsPerPass"], make_shape.WORM_TEETH / make_shape.WORM_STARTS)
        # the tumbling shaft turns once a pass at the drawn pace: with the axle at turnsPerPass turns a pass
        for W in (0.3, 1.7, 2.45):
            th = 2 * math.pi * mill["turnsPerPass"] * W
            got = x_angle(matrix("tumble", theta=th, work=W, size=1, presence=1.0))
            self.assertAlmostEqual(math.remainder(got - th / mill["turnsPerPass"], 2 * math.pi), 0.0, places=5)

    def test_the_rolls_turn_a_third_of_the_axle_in_opposite_ways(self):
        for th in (0.7, -2.1):
            for pid, sign in (("rollbottom", 1), ("pinionbottom", 1), ("crown", 1), ("rolltop", -1), ("piniontop", -1)):
                self.assertAlmostEqual(x_angle(matrix(pid, theta=th)), math.remainder(sign * th / 3, 2 * math.pi), places=5, msg=pid)

    def test_the_last_pass_is_the_press_brakes_angle(self):
        last = RIG["mill"]["sections"][-1]
        self.assertEqual(last, {"angle": 90.0, "leg": 4.0, "thick": 1.0})
        # the angle item: a flange 4 wide and 1 thick, a web standing 3 more on it, 1 thick: legs 4 outside
        els = {e["name"]: e for e in ANGLE["elements"]}
        flange, web = els["Flange"], els["Web"]
        size = lambda e: [e["to"][i] - e["from"][i] for i in range(3)]  # noqa: E731
        self.assertEqual(sorted(size(flange)), [1.0, 4.0, 4.0])
        self.assertEqual(sorted(size(web)), [1.0, 3.0, 4.0])

    def test_each_bar_grows_out_of_its_pass_and_the_angle_is_handed_out(self):
        els = flatten(SHAPE["elements"], textures={})
        for t, metal, k in (("i", "iron", 1), ("s", "steel", 2)):
            for j, length in enumerate(RIG["mill"]["lengths"], 1):
                mine = [e for e in els if e.name.startswith(f"{t}bar{j}_")]
                zs = [c[2] for e in mine for c in e.corners()]
                self.assertAlmostEqual(max(zs) - min(zs), length, places=1)
            hand = RIG["mill"]["handOut"]
            m = matrix(f"bar3{metal}", work=hand + 0.01, size=k, presence=1.0)
            self.assertEqual([m[r][c] for r in range(3) for c in range(3)].count(0.0), 9)      # drawn to nothing
            # each metal's bar moves for its own class only
            other = matrix(f"bar1{metal}", work=1.5, size=3 - k, presence=1.0)
            self.assertEqual([round(other[r][c], 9) for r in range(3) for c in range(4)], [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0])

    def test_three_grooves_in_lanes_a_transfer_stroke_apart(self):
        lanes = RIG["mill"]["laneX"]
        self.assertEqual(len(lanes), 3)
        self.assertTrue(all(abs((b - a) * 16 - make_shape.LANE_PITCH) < 1e-6 for a, b in zip(lanes, lanes[1:])))


class Anchors(unittest.TestCase):
    def test_cells_and_faces(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        ox = make_shape.ORIGIN_CELL[0]
        self.assertEqual(cells, {(x - ox, y, z) for x in range(4) for y in range(2) for z in range(3)})
        self.assertIn((0, 0, 0), cells)
        self.assertEqual(tuple(RIG["powerCell"]), (-ox, 0, 2))
        self.assertEqual(RIG["powerFace"], "south")
        self.assertEqual((RIG["infeedSide"], RIG["outputSide"]), ("north", "east"))
        for key in ("output", "bite", "drip"):
            self.assertEqual(len(RIG[key]["pos"]), 3)
        solid = {}
        for c in RIG["cells"]:
            x, y, z = c["pos"]
            if (x, z) not in solid or y > solid[(x, z)]["pos"][1]:
                solid[(x, z)] = c
        self.assertEqual(checks.lid_gaps(RIG["cells"], columns=lambda x, z: not solid[(x, z)].get("hollow")), [])

    def test_the_oil_level_follows_the_oil_input(self):
        (lvl,) = [p for p in RIG["parts"] if p["id"] == "oillevel"]
        self.assertEqual([d["input"] for d in lvl["drivers"]], ["oil"])
        heights = [matrix("oillevel", oil=oil)[1][1] * lvl["drivers"][0]["length"] * 16 for oil in (0.0, 0.5, 1.0)]
        self.assertAlmostEqual(heights[0], make_shape.OIL_EMPTY, places=5)
        self.assertAlmostEqual(heights[2], make_shape.OIL_FULL, places=5)
        self.assertAlmostEqual(heights[1], (heights[0] + heights[2]) / 2, places=5)


if __name__ == "__main__":
    unittest.main()
