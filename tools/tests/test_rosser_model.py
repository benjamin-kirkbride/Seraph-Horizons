"""The rosser's generated model files (mods-src/seraphhorizons/Rosser/tools/make_shape.py).

The generator itself needs the fetched IW and Logging Expanded zips, so CI does not run it; these
tests hold what it wrote to its own rules without them: the shipped rig parses, its reference poses
are its own maths, its anchors and feed constants agree with the generator's constants, and its
parts cover the shape. Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import math
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
sys.path.insert(0, str(MOD / "Machines" / "tools"))

from machinegen import rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("rosser_make_shape", MOD / "Rosser" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "rosser-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "rosser.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "rosser_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "Rosser" / "rig-reference.json").read_text())
REQUIRES = {"shaft", "ring", "tyres", "rollsin", "rollsout", "breaker", "levers", "heads", None}


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

    def test_globs_tell_the_traps_apart(self):
        parts = RIG["parts"]
        self.assertEqual(rigmath.part_of(parts, "ring_body1"), "ring")
        self.assertEqual(rigmath.part_of(parts, "ringtyre1_1"), "ringtyre")
        self.assertEqual(rigmath.part_of(parts, "ringpinion_iwtooth1"), "main")
        self.assertEqual(rigmath.part_of(parts, "rock_shaft"), "rock")
        self.assertEqual(rigmath.part_of(parts, "rocker_shaft"), "rocker")
        self.assertEqual(rigmath.part_of(parts, "toproll_in_body_1"), "toproll_in")
        self.assertEqual(rigmath.part_of(parts, "toparm_in_tie"), "toparm_in")
        self.assertEqual(rigmath.part_of(parts, "fr_bearing_rock58"), "frame")

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_reference_poses_are_the_rigs_own_maths(self):
        path = RIG["trunkPath"]
        poses = REFERENCE["poses"]
        self.assertGreater(len(poses), 100)
        self.assertEqual({p["size"] for p in poses}, {0, 1, 2})
        for pose in poses[::5]:
            ins = {k: pose[k] for k in ("theta", "travel", "feed", "trunk", "size", "presence")}
            for pid, want in pose["matrices"].items():
                got = rigmath.part_matrix(RIG["parts"], pid, ins, path)
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {ins}")


    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        cells = make_shape.shipped_cells(SHAPE, RIG["parts"], RIG["trunkPath"], RIG["cells"])
        self.assertEqual(cells, RIG["cells"])


class Anchors(unittest.TestCase):
    def test_cells_and_faces(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        self.assertIn((0, 0, 0), cells)
        for c in RIG["cells"]:
            self.assertFalse(c.get("hollow") and c.get("boxes"), c["pos"])
            self.assertTrue(c.get("hollow") or c.get("boxes"), c["pos"])     # (a cell with neither would be a full cube)
        power, water = tuple(RIG["powerCell"]), tuple(RIG["waterCell"])
        self.assertIn(power, cells)
        self.assertIn(water, cells)
        self.assertGreater(power[1], 0)
        self.assertEqual((RIG["powerFace"], RIG["waterFace"], RIG["chute"]["side"], RIG["chuteSide"]), ("north", "south", "south", "south"))
        self.assertNotIn((power[0], power[1], power[2] - 1), cells)
        self.assertNotIn((water[0], water[1], water[2] + 1), cells)
        # the east end leaves a mill in line its power feed cell (the mill's [-6,3,0] is our [0,3,0])
        self.assertNotIn((0, 3, 0), cells)

    def test_trunk_path_matches_the_generator(self):
        m = make_shape
        p = RIG["trunkPath"]
        dx = -m.ORIGIN_CELL[0]
        self.assertAlmostEqual(p["nose0"], m.NOSE0 / 16 + dx, places=6)
        self.assertAlmostEqual(p["tailStop"], m.TAIL_STOP / 16 + dx, places=6)
        self.assertEqual(p["lengths"], {"thin": 4, "thick": 5})
        self.assertAlmostEqual(p["origin"][1], m.H / 16, places=6)
        self.assertAlmostEqual(p["origin"][2], m.TZ / 16 - m.ORIGIN_CELL[2], places=6)
        st = p["stations"]
        self.assertLess(p["nose0"], st["breaker"])
        self.assertLess(st["breaker"], st["ring"])
        self.assertLessEqual(st["ring"], p["tailStop"])
        # a delivered thick trunk stays inside the machine
        self.assertLessEqual(p["tailStop"] + 5, 1.0)

    def test_feed_constants_are_the_drawn_gears(self):
        m = make_shape
        feed = RIG["feed"]
        self.assertAlmostEqual(feed["blocksPerRadian"], m.ROLL_RHO / 16 * m.BANJO[0] / m.BANJO[1], places=6)
        lay = m.G / m.WHEEL_TEETH
        self.assertAlmostEqual(feed["gear"]["thin"], lay * m.FAST[0] / m.FAST[1], places=5)
        self.assertAlmostEqual(feed["gear"]["thick"], lay * m.SLOW[0] / m.SLOW[1], places=5)
        self.assertLess(feed["gear"]["thick"], feed["gear"]["thin"])
        # the cross shafts turn once per feed radian, the selectors keyed to them
        for st in ("in", "out"):
            cross = next(p for p in RIG["parts"] if p["id"] == f"cross_{st}")["drivers"][0]
            self.assertEqual((cross["input"], abs(cross["ratio"])), ("feed", 1.0))

    def test_change_gears_are_whole_teeth_at_one_pitch_and_one_centre_distance(self):
        m = make_shape
        self.assertEqual(sum(m.FAST), sum(m.SLOW))
        self.assertAlmostEqual(m.CHANGE_DX, sum(m.FAST) * m.CHANGE_MODULE / 2)
        self.assertAlmostEqual(m.WORM_LEAD, 2 * math.pi * m.WHEEL_PITCH_R / m.WHEEL_TEETH)
        self.assertAlmostEqual(m.MAIN_Y - m.SHAFT_Y, m.WORM_PITCH_R + m.WHEEL_PITCH_R)
        self.assertAlmostEqual(2 * math.pi * m.RING_PITCH_R / m.RING_TEETH, 2 * math.pi * m.PINION_R / m.PINION_TEETH)
        dist = math.hypot(m.MAIN_Y - m.H, m.MAIN_Z - m.TZ)
        self.assertAlmostEqual(dist, m.RING_PITCH_R + m.PINION_R, delta=0.05)


if __name__ == "__main__":
    unittest.main()
