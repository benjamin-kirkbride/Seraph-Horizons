"""The mandrel forging station's generated model files (mods-src/seraphhorizons/MandrelStation/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with
the shared rig maths and uses the `requires` vocabulary of the contract, its reference poses are its own
maths, the cells are rebuilt from the shipped shape, the work is one hollow's forging, theta moves
nothing, the box closes from 8 to 6 across onto the mandrel, the two sections come off the tip, and the
anchors are where the contract puts them. Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import re
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(MOD / "MandrelStation" / "tools"))

from machinegen import checks, rigmath  # noqa: E402
from machinegen.geometry import aabb_of, flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("mandrelstation_make_shape", MOD / "MandrelStation" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "mandrelstation-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "mandrelstation.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "mandrelstation_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "MandrelStation" / "rig-reference.json").read_text())
# the build order: the frame (the block), the mandrel; the work is a lead or a copper hollow
REQUIRES = {"mandrel", "hollowlead", "hollowcopper", None}


def inputs(pose):
    return {k: pose[k] for k in ("theta", "work", "size", "presence")}


def matrix(pid, **ins):
    return rigmath.part_matrix(RIG["parts"], pid, ins, RIG["work"])


def posed_group(pre, rings, **ins):
    """The shipped shape's elements of metal `pre`'s rings (numbered from 1 at the shoulder), posed (voxels)."""
    out = []
    for el in flatten(SHAPE["elements"], textures={}):
        pid = rigmath.part_of(RIG["parts"], el.name)
        m = re.fullmatch(r"([lc])(\d+)[udew]+", pid)
        if m and m.group(1) == pre and int(m.group(2)) in rings:
            out.append(rigmath.posed(el, matrix(pid, **ins)))
    return out


ALL, NEAR, FAR = range(1, 9), range(1, 5), range(5, 9)


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

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_the_mandrel_and_the_work_wear_their_own_texture_codes(self):
        # the renderer sets "mandrel" to the fitted rod's metal
        for req, code in (("mandrel", "#mandrel"), ("hollowlead", "#lead"), ("hollowcopper", "#copper")):
            pids = {p["id"] for p in RIG["parts"] if p["requires"] == req}
            els = [e for e in SHAPE["elements"] if rigmath.part_of(RIG["parts"], e["name"]) in pids]
            self.assertTrue(els, req)
            self.assertEqual({f["texture"] for e in els for f in e["faces"].values()}, {code}, req)
        self.assertIn("mandrel", SHAPE["textures"])

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


class Forge(unittest.TestCase):
    def test_work_is_one_hollows_forging(self):
        self.assertNotIn("trunkPath", RIG)
        w = rigmath.progress_of(RIG)
        self.assertEqual((w["name"], w["unit"]), ("blows", "hollows"))
        self.assertEqual(w["end"], {"thin": 1.0, "thick": 1.0})
        forge = RIG["forge"]
        self.assertEqual(forge["hollows"], {"thin": "game:chutesection-lead", "thick": "game:chutesection-copper"})
        self.assertEqual(forge["sections"], {"thin": "seraphhorizons:pipesection-lead", "thick": "seraphhorizons:pipesection-copper"})
        self.assertEqual(forge["sectionsPerHollow"], 2)
        self.assertEqual(forge["blowsPerHollow"], {"thin": 6.0, "thick": 9.0})

    def test_theta_moves_nothing(self):
        for pid in (p["id"] for p in RIG["parts"]):
            a = matrix(pid, theta=0.0, work=0.3, size=2, presence=1.0)
            b = matrix(pid, theta=4.4, work=0.3, size=2, presence=1.0)
            self.assertEqual([[round(v, 12) for v in r] for r in a], [[round(v, 12) for v in r] for r in b], pid)

    def test_the_hollow_closes_and_stretches_evenly_together(self):
        # one motion: at every tenth of the forging the hollow is (8 - 2e) across and (8 + 8e) long
        t0, t1 = make_shape.T_FORGE
        for k, pre in ((1, "l"), (2, "c")):
            for i in range(11):
                e = i / 10
                lo, hi = aabb_of(posed_group(pre, ALL, work=t0 + (t1 - t0) * e, size=k, presence=1.0))
                self.assertAlmostEqual(hi[0] - lo[0], 8.0 - 2.0 * e, delta=0.15)
                self.assertAlmostEqual(hi[1] - lo[1], 8.0 - 2.0 * e, delta=0.15)
                self.assertAlmostEqual(hi[2] - lo[2], 8.0 + 8.0 * e, delta=0.15)
                # hung on the bar, not centred: the centre rises from HANG0 below the axis to the axis, and the
                # top wall (the bore's ceiling) bears on the bar's top throughout
                self.assertAlmostEqual((lo[1] + hi[1]) / 2, make_shape.YM - make_shape.HANG0 + make_shape.LIFT * e, delta=0.05)
                self.assertAlmostEqual(hi[1] - 1.0, make_shape.YM + make_shape.MH, delta=0.05)
                self.assertAlmostEqual(lo[2], make_shape.Z0, delta=0.05)

    def test_the_tube_stays_on_the_mandrel_at_the_end(self):
        # nothing slides off: at W 1 the 16-long tube is on the mandrel, its two sections end to end
        for k, pre in ((1, "l"), (2, "c")):
            whole = aabb_of(posed_group(pre, ALL, work=1.0, size=k, presence=1.0))
            self.assertAlmostEqual(whole[0][2], make_shape.Z0, delta=0.05)
            self.assertLessEqual(whole[1][2], make_shape.TIP + 1e-3)
            near = aabb_of(posed_group(pre, NEAR, work=1.0, size=k, presence=1.0))
            far = aabb_of(posed_group(pre, FAR, work=1.0, size=k, presence=1.0))
            for lo, hi in (near, far):
                self.assertAlmostEqual(hi[2] - lo[2], 8.0, delta=0.15)
                self.assertAlmostEqual((lo[1] + hi[1]) / 2, make_shape.YM, delta=0.05)
            self.assertAlmostEqual(far[0][2], near[1][2], delta=0.15)
        # every driver is the forging's: no part moves beyond the closing and the stretch
        for p in RIG["parts"]:
            for d in p["drivers"]:
                if d["type"] == "gauge":
                    self.assertEqual([(w["from"], w["ease"]) for w in d["windows"]], [(0.0, 1.0)], p["id"])


class Anchors(unittest.TestCase):
    def test_cells_sides_and_anchors(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        self.assertEqual(cells, {(0, 0, 0), (0, 0, 1)})
        # a hand station is not walked on: no lids
        self.assertFalse([c for c in RIG["cells"] if "lid" in c])
        self.assertTrue(all(c.get("boxes") for c in RIG["cells"]))
        self.assertNotIn("powerCell", RIG)
        self.assertNotIn("powerFace", RIG)
        self.assertEqual((RIG["infeedSide"], RIG["outputSide"]), ("west", "south"))
        for key in ("output", "strike"):
            self.assertEqual(len(RIG[key]["pos"]), 3)
        # the strike on the box's top over the stump's cell; the output (where gameplay drops the sections)
        # beyond the tip in the far cell
        self.assertLess(RIG["strike"]["pos"][2], 1.0)
        self.assertGreater(RIG["output"]["pos"][2], make_shape.TIP / 16)


if __name__ == "__main__":
    unittest.main()
