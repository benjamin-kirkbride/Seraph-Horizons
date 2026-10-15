"""The slitting mill's generated model files (mods-src/seraphhorizons/SlittingMill/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared
rig maths and uses the `requires` vocabulary of the model, its reference poses are its own maths, the cells are
rebuilt from the shipped shape, the work is knife strokes with the knife at the bottom at every whole turn, the rolls
feed the plate at its pace whichever way the axle turns, the nails are parted at the cuts and the strips and nails
are taken off at the hand-out, and the anchors are where the model puts them. Run with
`python3 -m unittest discover -s tools/tests`.
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
sys.path.insert(0, str(MOD / "SlittingMill" / "tools"))

from machinegen import checks, rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("slittingmill_make_shape", MOD / "SlittingMill" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "slittingmill-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "slittingmill.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "slittingmill_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "SlittingMill" / "rig-reference.json").read_text())
# the fitted parts: the slitters, the pinions, the drive, the cut-off shear; the work is a plate
REQUIRES = {"rolls", "pinions", "drive", "shear", "plate", None}
WORK_PARTS = {"plate", "rods", "nail1l", "nail1u", "nail2l", "nail2u"}


def inputs(pose):
    return {k: pose[k] for k in ("theta", "travel", "work", "size", "presence")}


def matrix(pid, **ins):
    return rigmath.part_matrix(RIG["parts"], pid, ins, RIG["work"])


def at(W, back=False, p=1.0):
    th = 2 * math.pi * W * RIG["slit"]["turnsPerStroke"]
    return {"theta": -th if back else th, "travel": th, "work": W, "size": 1, "presence": p}


def turn_x(m):
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
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        self.assertEqual({p["id"] for p in RIG["parts"] if p["requires"] == "plate"}, WORK_PARTS)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_the_work_wears_the_metal_code(self):
        # the renderer sets "metal" to the plate's metal
        els = [e for e in SHAPE["elements"] if rigmath.part_of(RIG["parts"], e["name"]) in WORK_PARTS]
        self.assertTrue(els)
        self.assertEqual({f["texture"] for e in els for f in e["faces"].values()}, {"#metal"})
        others = [e for e in SHAPE["elements"] if rigmath.part_of(RIG["parts"], e["name"]) not in WORK_PARTS]
        self.assertNotIn("#metal", {f["texture"] for e in others for f in e["faces"].values()})

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreater(len(poses), 30)
        self.assertEqual({p["size"] for p in poses}, {0, 1})
        for pose in poses[::2]:
            for pid, want in pose["matrices"].items():
                got = rigmath.part_matrix(RIG["parts"], pid, inputs(pose), RIG["work"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {inputs(pose)}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        cells = make_shape.shipped_cells(SHAPE, RIG["parts"], RIG["work"])
        self.assertEqual(cells, RIG["cells"])


class Slitting(unittest.TestCase):
    def test_work_is_knife_strokes_to_the_hand_out(self):
        self.assertNotIn("trunkPath", RIG)
        w = rigmath.progress_of(RIG)
        self.assertEqual((w["name"], w["unit"]), ("knife strokes", "strokes"))
        slit = RIG["slit"]
        self.assertEqual(w["end"], {"thin": slit["handOut"]})
        self.assertEqual(slit["turnsPerStroke"], 1)
        self.assertEqual(slit["cuts"], [3.0, 4.0])
        self.assertEqual((slit["plate"], slit["output"]), ("game:metalplate-{metal}", "game:metalnailsandstrips-{metal}"))

    def test_the_knife_is_at_the_bottom_at_every_whole_turn_either_way(self):
        bottom = matrix("knife", theta=0.0)[1][3]
        self.assertAlmostEqual(bottom * 16, -make_shape.R_CRANK, places=4)
        for W in RIG["slit"]["cuts"] + [0.0, 1.0, 2.0]:
            for back in (False, True):
                self.assertAlmostEqual(matrix("knife", **at(W, back))[1][3], bottom, places=9)
        self.assertAlmostEqual(matrix("knife", theta=math.pi)[1][3] * 16, make_shape.R_CRANK, places=4)

    def test_the_rolls_turn_the_feed_way_whichever_way_the_axle_turns(self):
        for back in (False, True):
            a, b = at(1.0, back), at(1.01, back)
            for pid in ("rollbottom", "pinionbottom"):
                self.assertGreater(math.remainder(turn_x(matrix(pid, **b)) - turn_x(matrix(pid, **a)), math.tau), 0)
            for pid in ("rolltop", "piniontop", "wheel"):
                self.assertLess(math.remainder(turn_x(matrix(pid, **b)) - turn_x(matrix(pid, **a)), math.tau), 0)
        # the cutters' rim moves a nail's length (2 voxels) a turn of the axle: the plate's pace
        ratio = next(d for p in RIG["parts"] if p["id"] == "rollbottom" for d in p["drivers"])["ratio"]
        self.assertAlmostEqual(ratio * make_shape.R_CUT * math.tau, make_shape.L_NAIL, places=4)

    def test_the_plate_goes_in_the_nails_are_parted_and_all_is_taken_off(self):
        # the plate shrinks into the nip (scale along z) and is gone when the rods' tail leaves it
        self.assertAlmostEqual(matrix("plate", **at(0.0))[2][2], 1.0, places=9)
        self.assertAlmostEqual(matrix("plate", **at(2.0))[2][2], 0.5, places=9)
        self.assertEqual(matrix("plate", **at(4.0))[2][2], 0.0)
        # the rods grow to the knife's face and are taken off at the end
        self.assertAlmostEqual(matrix("rods", **at(3.0))[2][2] * make_shape.EPS_ROD, make_shape.D_KNIFE, places=4)
        self.assertEqual(matrix("rods", **at(4.5))[2][2], 0.0)
        # each nail falls after its cut
        for j, w_cut in enumerate(RIG["slit"]["cuts"], 1):
            for lay in ("l", "u"):
                before = matrix(f"nail{j}{lay}", **at(w_cut))[1][3]
                after = matrix(f"nail{j}{lay}", **at(w_cut + 0.4))[1][3]
                self.assertAlmostEqual(before, 0.0, places=9)
                self.assertLess(after * 16, -6.0)
                self.assertEqual(matrix(f"nail{j}{lay}", **at(4.5))[2][2], 0.0)
        # nothing of the work shows at presence 0
        for pid in WORK_PARTS - {"plate"}:
            m = matrix(pid, **at(0.0, p=0.0))
            self.assertEqual([m[i][i] for i in range(3)], [1.0, 1.0, 1.0])


class Anchors(unittest.TestCase):
    def test_cells_sides_and_anchors(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        self.assertEqual(cells, {(x, y, z) for x in (0, 1) for y in (0, 1) for z in (0, 1)})
        self.assertFalse(any(c.get("hollow") for c in RIG["cells"]))
        self.assertEqual(checks.lid_gaps(RIG["cells"]), [])
        self.assertEqual((RIG["powerCell"], RIG["powerFace"]), ([1, 1, 1], "east"))
        self.assertEqual((RIG["infeedSide"], RIG["outputSide"]), ("north", "south"))
        for key in ("output", "plate", "nip", "knife"):
            self.assertEqual(len(RIG[key]["pos"]), 3)
        self.assertAlmostEqual(RIG["nip"]["pos"][2] * 16, make_shape.Z_N, places=3)
        self.assertAlmostEqual(RIG["knife"]["pos"][2] * 16, make_shape.Z_K, places=3)


if __name__ == "__main__":
    unittest.main()
