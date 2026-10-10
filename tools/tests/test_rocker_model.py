"""The rocker's generated model files (mods-src/seraphhorizons/Rocker/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the
shared rig maths and uses the `requires` vocabulary its README names, its reference poses are its own
maths, the cell is rebuilt from the shipped shape, theta is the only input and rocks the cradle (and
everything riding it) over and back once a turn on rockers that roll, the sills stand still, it takes water by
bucket only (no water cell, no spout, no stream), and the anchors are where the README puts them. Run with `python3 -m unittest discover -s tools/tests`.
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
sys.path.insert(0, str(MOD / "Rocker" / "tools"))

from machinegen import checks, rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("rocker_make_shape", MOD / "Rocker" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "rocker-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "rocker.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "rocker_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "Rocker" / "rig-reference.json").read_text())
# the fitted parts (build stages to be settled) and the states the renderer draws (water while worked, the load)
REQUIRES = {"riffles", "apron", "hopper", "riddle", "water", "charge", "concentrate", None}
ROCKING = {"cradle", "handle", "riffles", "apron", "hopper", "riddle", "water", "charge", "concentrate"}
FIXED = {"frame"}


def matrix(pid, theta):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta})


def turn_x(m):
    return math.degrees(math.atan2(m[2][1], m[1][1]))


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        self.assertEqual(set(ids), ROCKING | FIXED)
        for p in RIG["parts"]:
            if p["id"] in ROCKING - {"cradle"}:
                self.assertEqual((p["ride"], p["drivers"]), ("cradle", []), p["id"])
            if p["id"] in FIXED:
                self.assertEqual((p["ride"], p["drivers"]), (None, []), p["id"])

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_textures_by_part_and_the_water_in_the_transparent_pass(self):
        want = {"riddle": {"#riddle"}, "water": {"#water"}, "charge": {"#charge"}, "concentrate": {"#concentrate"},
                "riffles": {"#oak"}, "apron": {"#oak", "#canvas"}, "hopper": {"#oak"}, "cradle": {"#oak"},
                "handle": {"#oak", "#iron"}, "frame": {"#oak"}}
        got = {}
        for e in SHAPE["elements"]:
            pid = rigmath.part_of(RIG["parts"], e["name"])
            got.setdefault(pid, set()).update(f["texture"] for f in e["faces"].values())
            if pid == "water":
                self.assertEqual(e.get("renderPass"), 3, e["name"])
            else:
                self.assertNotIn("renderPass", e, e["name"])
        self.assertEqual(got, want)
        self.assertEqual(SHAPE["textures"]["water"], "game:block/liquid/water")

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreater(len(poses), 10)
        for pose in poses:
            self.assertEqual(set(pose), {"theta", "matrices"})
            for pid, want in pose["matrices"].items():
                got = matrix(pid, pose["theta"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at theta {pose['theta']}")

    def test_the_cell_is_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])


class Rock(unittest.TestCase):
    def test_theta_is_the_only_input(self):
        self.assertNotIn("work", RIG)
        self.assertNotIn("trunkPath", RIG)
        drivers = [d for p in RIG["parts"] for d in p["drivers"]]
        self.assertEqual([d["type"] for d in drivers], ["swing", "slide"])
        for d in drivers:
            self.assertNotIn("input", d)
            self.assertEqual(d["ratio"], 1.0)

    def test_one_rock_a_turn_over_to_the_south_and_back_over_to_the_north(self):
        deg = RIG["rock"]["degrees"]
        for theta, want in ((0.0, 0.0), (math.pi / 2, deg), (math.pi, 0.0), (3 * math.pi / 2, -deg), (2 * math.pi, 0.0)):
            self.assertAlmostEqual(turn_x(matrix("cradle", theta)), want, places=3)
        # leaning south (positive about x) tips its top towards +z
        self.assertGreater(matrix("cradle", math.pi / 2)[2][1], 0)

    def test_the_rockers_roll_without_slipping(self):
        # the running faces' centre stays at its height and moves along z by the radius times the angle
        r = RIG["rock"]["radius"]
        pivot = RIG["parts"][0]["drivers"][0]["pivot"]
        for theta in (0.3, 1.2, math.pi / 2, 2.5, 4.0, 3 * math.pi / 2, 5.9):
            a = math.radians(turn_x(matrix("cradle", theta)))
            c = rigmath.apply(matrix("cradle", theta), pivot)
            self.assertAlmostEqual(c[1], pivot[1], places=5)
            self.assertAlmostEqual(c[2], pivot[2] + r * a, places=5)

    def test_what_rides_rocks_with_the_cradle_and_the_rest_stands_still(self):
        for theta in (0.4, 2.0, 4.4):
            c = matrix("cradle", theta)
            for pid in ROCKING:
                self.assertEqual([[round(v, 9) for v in row] for row in matrix(pid, theta)], [[round(v, 9) for v in row] for row in c], pid)
            for pid in FIXED:
                self.assertEqual([[round(v, 12) for v in row] for row in matrix(pid, theta)[:3]],
                                 [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0]], pid)


class Anchors(unittest.TestCase):
    def test_one_cell_with_a_lid_and_no_power(self):
        self.assertEqual([c["pos"] for c in RIG["cells"]], [[0, 0, 0]])
        self.assertEqual(checks.lid_gaps(RIG["cells"]), [])
        self.assertNotIn("powerCell", RIG)
        self.assertNotIn("powerFace", RIG)

    def test_water_by_bucket_only(self):
        # no pipe connects: no water cell or face, no spout or stream, nothing fixed but the sills
        self.assertEqual([k for k in RIG if k.endswith(("Cell", "Face"))], [])
        for key in ("spout", "stream"):
            self.assertNotIn(key, RIG)
            self.assertNotIn(key, {p["id"] for p in RIG["parts"]})
        self.assertNotIn("pipe", SHAPE["textures"])
        sills = [e for e in SHAPE["elements"] if rigmath.part_of(RIG["parts"], e["name"]) == "frame"]
        self.assertEqual(max(e["to"][1] for e in sills), make_shape.SILL_TOP)

    def test_tailings_and_the_points_that_ride(self):
        self.assertEqual((RIG["tailings"]["pos"], RIG["tailingsSide"]), ([-0.5, 0.0, 0.5], "west"))
        ids = {p["id"] for p in RIG["parts"]}
        for key in ("hopper", "outflow", "concentrate"):
            self.assertEqual(RIG[key]["part"], "cradle", key)
            self.assertIn(RIG[key]["part"], ids)
        # the hopper's point is over the head half, where the bucket is poured; the outflow is the foot's lip, west
        self.assertGreater(RIG["hopper"]["pos"][0], 0.5)
        self.assertAlmostEqual(RIG["hopper"]["pos"][2], 0.5, places=4)
        self.assertLess(RIG["outflow"]["pos"][0], 0.1)


if __name__ == "__main__":
    unittest.main()
