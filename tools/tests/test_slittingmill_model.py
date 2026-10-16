"""The slitting mill's generated model files (mods-src/seraphhorizons/SlittingMill/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared
rig maths and uses the `requires` vocabulary of the model, its reference poses are its own maths, the cells are
rebuilt from the shipped shape, the work is knife strokes with the knife at the bottom at every whole stroke, the
rolls step the plate a nail's length while the knife is up and stand while it is down, whichever way the axle turns,
p easing at the start and the end turns nothing, the nails go down with the knife's edge and fall, the push bar goes
with the push shaft's pinion, which turns on only while the push sector turns and back only after the push, the strips
and nails are taken off at the hand-out, and the anchors are where the model puts them. Run with
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
# the fitted parts: the slitters, the pinions, the drive, the cut-off shear, the push bar; the work is a plate
REQUIRES = {"rolls", "pinions", "drive", "shear", "pusher", "plate", None}
WORK_PARTS = {"plate", "rodsl", "rodsu", "nail1l", "nail1u", "nail2l", "nail2u"}
STEPPED = (("rollbottom", 1), ("pinionbottom", 1), ("rolltop", -1), ("piniontop", -1), ("wheel", -1))


def inputs(pose):
    return {k: pose[k] for k in ("theta", "travel", "work", "size", "presence")}


def matrix(pid, **ins):
    return rigmath.part_matrix(RIG["parts"], pid, ins, RIG["work"])


def at(W, back=False, p=1.0):
    th = 2 * math.pi * W * RIG["slit"]["turnsPerStroke"]
    return {"theta": -th if back else th, "travel": th, "work": W, "size": 1, "presence": p}


def turn_x(m):
    return math.atan2(m[2][1], m[1][1])


def close(a, b, tol=1e-4):
    return all(abs(a[i][j] - b[i][j]) < tol for i in range(3) for j in range(4))


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
        self.assertEqual(slit["handOut"], 5.0)
        self.assertEqual(slit["turnsPerStroke"], 1)
        self.assertEqual(slit["cuts"], [3.0, 4.0])
        self.assertEqual(slit["steps"], [0.5, 1.5, 2.5, 3.5, 4.5])
        self.assertEqual((slit["plate"], slit["output"]), ("game:metalplate-{metal}", "game:metalnailsandstrips-{metal}"))

    def test_the_knife_is_at_the_bottom_at_every_whole_stroke_and_up_for_the_steps(self):
        bottom = matrix("knife", work=0.0)[1][3]
        self.assertAlmostEqual(bottom * 16, -make_shape.R_CRANK, places=4)
        for W in RIG["slit"]["cuts"] + [0.0, 1.0, 2.0, 5.0]:
            for back in (False, True):
                self.assertAlmostEqual(matrix("knife", **at(W, back))[1][3], bottom, places=5)
        for W in RIG["slit"]["steps"]:
            self.assertAlmostEqual(matrix("knife", **at(W))[1][3] * 16, make_shape.R_CRANK, places=3)
        # the knife, its crank and the clutch's cone follow the work, not the axle: turning the axle alone moves none
        for pid in ("knife", "crank", "crankshaft", "cone"):
            self.assertTrue(close(matrix(pid, theta=1.3, travel=1.3, work=0.7, size=1, presence=1.0),
                                  matrix(pid, theta=-4.0, travel=9.0, work=0.7, size=1, presence=1.0), 1e-12), pid)

    def test_the_rolls_step_while_the_knife_is_up_and_stand_while_it_is_down(self):
        steps, half = RIG["slit"]["steps"], RIG["slit"]["stepWidth"] / 2
        for back in (False, True):
            for m, s in enumerate(steps):
                a, b = at(s - half, back), at(s + half, back)
                c = at(steps[m + 1] - half, back) if m + 1 < len(steps) else at(RIG["slit"]["handOut"], back)
                for pid, sign in STEPPED:
                    step = math.remainder(turn_x(matrix(pid, **b)) - turn_x(matrix(pid, **a)), math.tau)
                    self.assertAlmostEqual(step * sign, make_shape.STEP_ANGLE, places=4, msg=f"{pid} step {m}")
                    # standing from the end of one step to the start of the next, the knife's cut between
                    stand = math.remainder(turn_x(matrix(pid, **c)) - turn_x(matrix(pid, **b)), math.tau)
                    self.assertAlmostEqual(stand, 0.0, places=4, msg=f"{pid} after step {m}")
        # a step carries the plate a nail's length (2 voxels) at the cutters' rim
        self.assertAlmostEqual(make_shape.STEP_ANGLE * make_shape.R_CUT, make_shape.L_NAIL, places=6)

    def test_the_drive_stands_as_p_eases_at_the_start_and_the_end(self):
        for W in (0.0, RIG["slit"]["handOut"]):
            for pid in [p for p, _ in STEPPED] + ["crankshaft", "knife"]:
                full = matrix(pid, **at(W))
                for p in (0.0, 0.4):
                    self.assertTrue(close(full, matrix(pid, **at(W, p=p))), f"{pid} at W {W}, p {p}")

    def test_the_plate_goes_in_the_nails_are_parted_and_all_is_taken_off(self):
        # the plate shrinks into the nip a quarter a step (scale along z) and is gone after the fourth
        self.assertAlmostEqual(matrix("plate", **at(0.0))[2][2], 1.0, places=9)
        self.assertAlmostEqual(matrix("plate", **at(2.0))[2][2], 0.5, places=5)
        self.assertAlmostEqual(matrix("plate", **at(2.3))[2][2], 0.5, places=5)
        self.assertEqual(matrix("plate", **at(4.0))[2][2], 0.0)
        # the rods grow to the cut in two steps; the strips left of them are pushed out past the cut, fall into the box,
        # and only then are taken off
        for lay in ("l", "u"):
            pid = f"rods{lay}"
            self.assertAlmostEqual(matrix(pid, **at(3.0))[2][2] * make_shape.EPS_ROD, make_shape.D_KNIFE, places=3)
            before = matrix(pid, **at(RIG["slit"]["push"][0]))
            pushed = matrix(pid, **at(RIG["slit"]["push"][1]))
            self.assertAlmostEqual((pushed[2][3] - before[2][3]) * 16, make_shape.D_KNIFE, places=3)
            self.assertAlmostEqual(pushed[1][3], before[1][3], places=6)
            fallen = matrix(pid, **at(make_shape.STRIP_FALL[1]))
            self.assertAlmostEqual((fallen[1][3] - pushed[1][3]) * 16, -make_shape.strip_drop(lay), places=3)
            self.assertGreater(matrix(pid, **at(5.0 - make_shape.TAKE_OFF))[2][2], 0.0)
            self.assertEqual(matrix(pid, **at(5.0))[2][2], 0.0)
        # each nail stands until the knife's edge comes down on it, goes down with it its thickness, and falls once through
        for j in (1, 2):
            for lay in ("l", "u"):
                marks = make_shape.cut_marks(lay, j)
                pid = f"nail{j}{lay}"
                self.assertAlmostEqual(matrix(pid, **at(marks[0] - 0.01))[1][3], 0.0, places=9)
                self.assertAlmostEqual(matrix(pid, **at(marks[-1]))[1][3] * 16, -1.0, places=3)
                self.assertLess(matrix(pid, **at(marks[-1] + make_shape.FALL))[1][3] * 16, -6.0)
                self.assertEqual(matrix(pid, **at(5.0))[2][2], 0.0)
        # nothing of the work shows at presence 0
        for pid in WORK_PARTS - {"plate"}:
            m = matrix(pid, **at(0.0, p=0.0))
            self.assertEqual([m[i][i] for i in range(3)], [1.0, 1.0, 1.0])


class PushBar(unittest.TestCase):
    def test_the_sector_drives_it_out_and_the_weight_brings_it_back(self):
        slit = RIG["slit"]
        z = lambda W: matrix("pusher", **at(W))[2][3] * 16    # noqa: E731
        self.assertEqual(z(0.0), 0.0)
        # it stands while the first steps feed the plate, and goes the plate's length in the catch-up: to the nip, as the
        # plate's tail goes in
        self.assertAlmostEqual(z(slit["catchUp"][0]), 0.0, places=9)
        self.assertAlmostEqual(make_shape.PUSH_REST + z(slit["catchUp"][1]), make_shape.Z_N, places=3)
        self.assertAlmostEqual(z(slit["push"][0]), make_shape.PLATE_L, places=3)
        # it goes with the push pinion, the weight with the drum; the shaft turns on only while the sector turns
        q = lambda W: turn_x(matrix("pushshaft", **at(W)))    # noqa: E731
        s = lambda W: turn_x(matrix("pushsector", **at(W)))    # noqa: E731
        y = lambda W: matrix("pushweight", **at(W))[1][3] * 16    # noqa: E731
        for i in range(200):
            W = 3.0 + i * 0.01
            dq = math.remainder(q(W + 0.01) - q(W), math.tau)
            self.assertAlmostEqual(z(W + 0.01) - z(W), make_shape.R_PP * dq, places=4)
            self.assertAlmostEqual(y(W + 0.01) - y(W), make_shape.R_DRUM * dq, places=4)
            if dq > 1e-9:
                self.assertGreater(abs(math.remainder(s(W + 0.01) - s(W), math.tau)), 1e-6)
            if dq < -1e-9:
                self.assertGreaterEqual(W, slit["pushBack"][0] - 0.01)
        # the strips' length more while the knife is up, the fingers' front then at the cut, never past it
        self.assertAlmostEqual(z(slit["push"][1]), make_shape.PLATE_L + make_shape.D_KNIFE, places=3)
        self.assertAlmostEqual(make_shape.PUSH_REST + z(slit["push"][1]), make_shape.Z_K, places=3)
        for W in (slit["push"][0], slit["push"][1]):
            self.assertGreater(matrix("knife", **at(W))[1][3] * 16, 0.4)
        # drawn back by the end: as at the start, and p easing there moves nothing
        self.assertAlmostEqual(z(slit["pushBack"][1]), 0.0, places=3)
        self.assertTrue(close(matrix("pusher", **at(5.0)), matrix("pusher", **at(5.0, p=0.3))))


class Anchors(unittest.TestCase):
    def test_cells_sides_and_anchors(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        self.assertEqual(cells, {(x, y, z) for x in (0, 1) for y in (0, 1) for z in (0, 1)})
        self.assertFalse(any(c.get("hollow") for c in RIG["cells"]))
        self.assertEqual(checks.lid_gaps(RIG["cells"]), [])
        self.assertEqual((RIG["powerCell"], RIG["powerFace"]), ([1, 1, 0], "east"))
        self.assertEqual((RIG["infeedSide"], RIG["outputSide"]), ("north", "south"))
        for key in ("output", "plate", "nip", "knife"):
            self.assertEqual(len(RIG[key]["pos"]), 3)
        self.assertAlmostEqual(RIG["nip"]["pos"][2] * 16, make_shape.Z_N, places=3)
        self.assertAlmostEqual(RIG["knife"]["pos"][2] * 16, make_shape.Z_K, places=3)


if __name__ == "__main__":
    unittest.main()
