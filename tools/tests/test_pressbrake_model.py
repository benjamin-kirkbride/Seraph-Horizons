"""The press brake's generated model files (mods-src/seraphhorizons/PressBrake/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with
the shared rig maths and uses the `requires` vocabulary of the contract, its reference poses are its own
maths, the cells are rebuilt from the shipped shape, the work is one half plate's fold cycle, theta moves
nothing, the fold reaches the metal's throw and sets at 90 degrees, and the anchors are where the contract
puts them. Run with `python3 -m unittest discover -s tools/tests`.
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
sys.path.insert(0, str(MOD / "PressBrake" / "tools"))

from machinegen import checks, rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("pressbrake_make_shape", MOD / "PressBrake" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "pressbrake-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "pressbrake.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "pressbrake_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "PressBrake" / "rig-reference.json").read_text())
# the build order: the frame, screws, edge; the work is a lead or a copper half plate, folded once into an angle
REQUIRES = {"screws", "edge", "platelead", "platecopper", None}


def inputs(pose):
    return {k: pose[k] for k in ("theta", "work", "size", "presence")}


def matrix(pid, **ins):
    return rigmath.part_matrix(RIG["parts"], pid, ins, RIG["work"])


def turn_x(m):
    return math.degrees(math.atan2(m[2][1], m[1][1]))


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
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_the_fitted_metals_wear_their_own_texture_codes(self):
        # the renderer sets "edge" to the fitted plate's metal; the screws, made from metal parts, are
        # always cupronickel
        for req, code in (("edge", "#edge"), ("screws", "#cupronickel"), ("platelead", "#lead"), ("platecopper", "#copper")):
            pids = {p["id"] for p in RIG["parts"] if p["requires"] == req}
            els = [e for e in SHAPE["elements"] if rigmath.part_of(RIG["parts"], e["name"]) in pids]
            self.assertTrue(els, req)
            self.assertEqual({f["texture"] for e in els for f in e["faces"].values()}, {code}, req)
        self.assertTrue({"edge", "cupronickel"} <= set(SHAPE["textures"]))
        self.assertNotIn("screw", SHAPE["textures"])

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreater(len(poses), 40)
        self.assertEqual({p["size"] for p in poses}, {0, 1, 2})
        for pose in poses[::2]:
            for pid, want in pose["matrices"].items():
                got = rigmath.part_matrix(RIG["parts"], pid, inputs(pose), RIG["work"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {inputs(pose)}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        cells = make_shape.shipped_cells(SHAPE, RIG["parts"], RIG["work"])
        self.assertEqual(cells, RIG["cells"])


class Fold(unittest.TestCase):
    def test_work_is_one_plates_fold_cycle(self):
        self.assertNotIn("trunkPath", RIG)
        w = rigmath.progress_of(RIG)
        self.assertEqual((w["name"], w["unit"]), ("fold", "plates"))
        self.assertEqual(w["end"], {"thin": 1.0, "thick": 1.0})
        fold = RIG["fold"]
        self.assertEqual(fold["plates"], {"thin": "seraphhorizons:halfplate-lead", "thick": "seraphhorizons:halfplate-copper"})
        self.assertEqual(fold["leverTurnsPerPlate"], {"thin": 1.5, "thick": 2.25})
        self.assertEqual(fold["angles"], {"thin": "seraphhorizons:angle-lead", "thick": "seraphhorizons:angle-copper"})
        self.assertEqual(fold["anglesPerPlate"], 1)
        for old in ("sections", "sectionsPerPlate"):
            self.assertNotIn(old, fold)
        # copper takes more lever: more turns a plate, a longer throw
        self.assertGreater(fold["leverTurnsPerPlate"]["thick"], fold["leverTurnsPerPlate"]["thin"])
        self.assertGreater(fold["throwDegrees"]["thick"], fold["throwDegrees"]["thin"])

    def test_theta_moves_nothing(self):
        for pid in (p["id"] for p in RIG["parts"]):
            a = matrix(pid, theta=0.0, work=0.3, size=2, presence=1.0)
            b = matrix(pid, theta=4.4, work=0.3, size=2, presence=1.0)
            self.assertEqual([[round(v, 12) for v in r] for r in a], [[round(v, 12) for v in r] for r in b], pid)

    def test_the_leaf_throws_to_the_metals_angle_and_comes_back(self):
        for k, cls in ((1, "thin"), (2, "thick")):
            for t in (make_shape.T_FOLD1,):
                self.assertAlmostEqual(turn_x(matrix("leaf", work=t[1], size=k, presence=1.0)), RIG["fold"]["throwDegrees"][cls], places=3)
                self.assertAlmostEqual(turn_x(matrix("leaf", work=t[2], size=k, presence=1.0)), 0.0, places=6)

    def test_the_flange_sets_at_ninety_and_the_angle_is_delivered(self):
        # one fold a half plate: leg A sets at 90 degrees, leg M stays flat and both come north a leg onto the leaf
        for k, pre in ((1, "l"), (2, "c")):
            self.assertAlmostEqual(turn_x(matrix(f"{pre}a", work=0.7, size=k, presence=1.0)), 90.0, places=3)
            self.assertAlmostEqual(turn_x(matrix(f"{pre}m", work=1.0, size=k, presence=1.0)), 0.0, places=6)
            self.assertAlmostEqual(matrix(f"{pre}m", work=1.0, size=k, presence=1.0)[2][3] * 16, -make_shape.S, places=4)
        self.assertEqual({p["id"] for p in RIG["parts"] if p["requires"] == "platelead"}, {"la", "lm"})

    def test_the_half_plate_lies_whole_across_the_edge_and_folds_into_a_4_by_4_L(self):
        # no spread: leg A is the half plate's own sheet over the leaf from the moment it goes on, and
        # the two legs together are the 8 x 4 half plate, folded across its middle
        legs = {}
        for e in SHAPE["elements"]:
            pid = rigmath.part_of(RIG["parts"], e["name"])
            if pid in ("la", "lm"):
                legs[pid] = [[v * 1.0 for v in e["from"]], [v * 1.0 for v in e["to"]]]
        (a0, a1), (m0, m1) = legs["la"], legs["lm"]
        # (the z-fighting fix trims a sheet's edges by hundredths where the two legs meet)
        for lo, hi in ((a0, a1), (m0, m1)):
            for got, want in zip([hi[i] - lo[i] for i in range(3)], [4.0, make_shape.T, 4.0]):
                self.assertAlmostEqual(got, want, delta=0.05)
        self.assertEqual((a0[1], m0[1]), (make_shape.YB, make_shape.YB))
        self.assertAlmostEqual(a0[2], make_shape.EZ - 4.0, delta=0.05)
        self.assertAlmostEqual(m1[2], make_shape.EZ + 4.0, delta=0.05)
        self.assertAlmostEqual(a1[2], make_shape.EZ, delta=0.05)
        self.assertAlmostEqual(m0[2], make_shape.EZ, delta=0.05)
        for k in (1, 2):
            pre = "lc"[k - 1]
            for w in (0.0, 0.1, make_shape.T_FOLD1[0]):
                m = matrix(f"{pre}a", work=w, size=k, presence=1.0)
                self.assertEqual([[round(v, 9) for v in r] for r in m[:3]], [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0]])

    def test_the_bar_lies_on_the_sheet_while_the_leaf_moves(self):
        for k in (1, 2):
            for w in (make_shape.T_FOLD1[1],):
                self.assertAlmostEqual(matrix("bar", work=w, size=k, presence=1.0)[1][3] * 16, make_shape.T, places=4)
            self.assertAlmostEqual(matrix("bar", work=make_shape.T_OFF[1], size=k, presence=1.0)[1][3] * 16, make_shape.T + make_shape.LIFT, places=4)
        self.assertEqual(matrix("bar")[1][3], 0.0)


class Anchors(unittest.TestCase):
    def test_cells_sides_and_anchors(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        self.assertEqual(cells, {(0, 0, 0), (0, 0, 1)})
        self.assertEqual(checks.lid_gaps(RIG["cells"]), [])
        self.assertNotIn("powerCell", RIG)
        self.assertNotIn("powerFace", RIG)
        self.assertEqual((RIG["infeedSide"], RIG["outputSide"]), ("south", "north"))
        for key in ("output", "plate", "edge"):
            self.assertEqual(len(RIG[key]["pos"]), 3)
        # the edge anchor is on the hinge axis; the output over the leaf at the near end
        _, y, z = (c * 16 for c in RIG["edge"]["pos"])
        self.assertAlmostEqual(y, make_shape.YB, places=2)
        self.assertAlmostEqual(z, make_shape.EZ, places=2)
        self.assertLess(RIG["output"]["pos"][2], 0.5)


if __name__ == "__main__":
    unittest.main()
