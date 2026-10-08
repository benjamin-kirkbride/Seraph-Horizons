"""The squaring shear's generated model files (mods-src/seraphhorizons/SquaringShear/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with
the shared rig maths and uses the `requires` vocabulary of the contract, its reference poses are its own
maths, the cells are rebuilt from the shipped shape, the work is one plate's cut cycle, theta moves
nothing, the blade comes down through the plate and the treadle with it, the two halves are delivered
onto the table, and the anchors are where the contract puts them. Run with
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
sys.path.insert(0, str(MOD / "SquaringShear" / "tools"))

from machinegen import checks, rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("squaringshear_make_shape", MOD / "SquaringShear" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "squaringshear-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "squaringshear.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "squaringshear_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "SquaringShear" / "rig-reference.json").read_text())
# the build order: the frame, blade, gauge; the work is a lead or a copper plate, cut once into two half plates
REQUIRES = {"blade", "gauge", "platelead", "platecopper", None}


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
        # the renderer sets "blade" to the fitted plate's metal and "gauge" to the rods'
        for req, code in (("blade", "#blade"), ("gauge", "#gauge"), ("platelead", "#lead"), ("platecopper", "#copper")):
            pids = {p["id"] for p in RIG["parts"] if p["requires"] == req}
            els = [e for e in SHAPE["elements"] if rigmath.part_of(RIG["parts"], e["name"]) in pids]
            self.assertTrue(els, req)
            self.assertEqual({f["texture"] for e in els for f in e["faces"].values()}, {code}, req)
        self.assertTrue({"blade", "gauge"} <= set(SHAPE["textures"]))

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


class Cut(unittest.TestCase):
    def test_work_is_one_plates_cut_cycle(self):
        self.assertNotIn("trunkPath", RIG)
        w = rigmath.progress_of(RIG)
        self.assertEqual((w["name"], w["unit"]), ("cut", "plates"))
        self.assertEqual(w["end"], {"thin": 1.0, "thick": 1.0})
        cut = RIG["cut"]
        self.assertEqual(cut["plates"], {"thin": "game:metalplate-lead", "thick": "game:metalplate-copper"})
        self.assertEqual(cut["halfPlates"], {"thin": "seraphhorizons:halfplate-lead", "thick": "seraphhorizons:halfplate-copper"})
        self.assertEqual(cut["halfPlatesPerPlate"], 2)
        # copper takes more treadle: half as many strokes again
        self.assertEqual(cut["strokesPerPlate"], {"thin": 1.0, "thick": 1.5})

    def test_theta_moves_nothing(self):
        for pid in (p["id"] for p in RIG["parts"]):
            a = matrix(pid, theta=0.0, work=0.3, size=2, presence=1.0)
            b = matrix(pid, theta=4.4, work=0.3, size=2, presence=1.0)
            self.assertEqual([[round(v, 12) for v in r] for r in a], [[round(v, 12) for v in r] for r in b], pid)

    def test_the_blade_comes_down_through_the_plate_with_the_treadle_and_goes_back_up(self):
        t = make_shape.T_CUT
        for k in (1, 2):
            down = matrix("crosshead", work=t[1], size=k, presence=1.0)[1][3] * 16
            self.assertAlmostEqual(down, -make_shape.DROP, places=4)
            self.assertLess(make_shape.REST_EDGE + down, make_shape.YB)
            self.assertAlmostEqual(matrix("crosshead", work=t[2], size=k, presence=1.0)[1][3], 0.0, places=9)
            # the treadle's foot goes down while the blade does, and the links ride the crosshead
            self.assertLess(turn_x(matrix("treadle", work=t[1], size=k, presence=1.0)), -5.0)
            self.assertAlmostEqual(turn_x(matrix("treadle", work=t[2], size=k, presence=1.0)), 0.0, places=6)
        self.assertEqual({p["id"] for p in RIG["parts"] if p.get("ride") == "crosshead"}, {"upperblade", "linkw", "linke"})

    def test_the_hold_down_lies_on_the_sheet_through_the_stroke(self):
        for k in (1, 2):
            for w in make_shape.T_CUT:
                self.assertAlmostEqual(matrix("holddown", work=w, size=k, presence=1.0)[1][3] * 16, make_shape.T, places=4)
            self.assertAlmostEqual(matrix("holddown", work=make_shape.T_OFF[1], size=k, presence=1.0)[1][3] * 16,
                                   make_shape.T + make_shape.LIFT, places=4)
        self.assertEqual(matrix("holddown")[1][3], 0.0)

    def test_the_two_halves_are_drawn_onto_the_table(self):
        for k, pre in ((1, "l"), (2, "c")):
            for half in ("f", "b"):
                self.assertAlmostEqual(matrix(f"{pre}{half}", work=make_shape.T_OFF[0], size=k, presence=1.0)[2][3], 0.0, places=9)
                self.assertAlmostEqual(matrix(f"{pre}{half}", work=1.0, size=k, presence=1.0)[2][3] * 16, -make_shape.DRAW, places=4)
        self.assertEqual({p["id"] for p in RIG["parts"] if p["requires"] == "platelead"}, {"lf", "lb"})
        # each half is a half plate: 8 along the blade, 4 across it, 1 thick
        for e in SHAPE["elements"]:
            if e["name"].startswith(("lf_", "lb_", "cf_", "cb_")):
                size = sorted(round(e["to"][i] - e["from"][i], 4) for i in range(3))
                self.assertEqual(size, [1.0, 4.0, 8.0], e["name"])


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
        # the edge anchor is on the cut; the output over the table in the controller's cell
        _, y, z = (c * 16 for c in RIG["edge"]["pos"])
        self.assertAlmostEqual(y, make_shape.YB, places=2)
        self.assertAlmostEqual(z, make_shape.ZB, places=2)
        self.assertLess(RIG["output"]["pos"][2], 1.0)


if __name__ == "__main__":
    unittest.main()
