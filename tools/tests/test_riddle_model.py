"""The riddle's generated model files (mods-src/seraphhorizons/Riddle/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the
shared rig maths and uses the model's `requires` vocabulary, its reference poses are its own maths, the
cell is rebuilt from the shipped shape, the work is one charge's riddling, the clock shakes the riddle only
while a charge is riddled, the fines end in the box and the oversize in the riddle, the model stays one
readable block, and the anchors are where the model puts them. Run with
`python3 -m unittest discover -s tools/tests -p test_riddle_model.py`.
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
sys.path.insert(0, str(MOD / "Riddle" / "tools"))

from machinegen import checks, rigmath  # noqa: E402
from machinegen.geometry import aabb_of, flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("riddle_make_shape", MOD / "Riddle" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)


def load(path):
    return json.loads((MOD / path).read_text())


RIG = load("assets/seraphhorizons/config/riddle-rig.json")
SHAPE = load("assets/seraphhorizons/shapes/block/riddle.json")
FRAME = load("assets/seraphhorizons/shapes/block/riddle_frame.json")
REFERENCE = load("tests/Riddle/rig-reference.json")
REQUIRES = {"riddle", "chargesmall", "chargefull", None}


def inputs(pose):
    return {k: pose[k] for k in ("theta", "work", "size", "presence")}


def matrix(pid, **ins):
    return rigmath.part_matrix(RIG["parts"], pid, ins, RIG["work"])


def point(pid, p, **ins):
    return [v * 16 for v in rigmath.apply(matrix(pid, **ins), [c / 16 for c in p])]


def elements(part):
    return [e for e in flatten(SHAPE["elements"], textures={}) if rigmath.part_of(RIG["parts"], e.name) == part]


def posed(part, **ins):
    m = matrix(part, **ins)
    return [rigmath.posed(e, m) for e in elements(part)]


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        parts = RIG["parts"]
        ids = [p["id"] for p in parts]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in parts:
            if p.get("ride"):
                self.assertIn(p["ride"], ids)
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({p["requires"] for p in parts}, REQUIRES)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        parts = RIG["parts"]
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        self.assertEqual({rigmath.part_of(parts, n) for n in names}, {p["id"] for p in parts})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(parts, n) == "frame"})

    def test_the_charge_wears_the_ore_texture_code(self):
        # the renderer sets "ore" to the crushed ore's texture
        parts = RIG["parts"]
        for e in SHAPE["elements"]:
            pid = rigmath.part_of(parts, e["name"])
            codes = {f["texture"] for f in e["faces"].values()}
            if pid.startswith(("c1", "c2")):
                self.assertEqual(codes, {"#ore"}, e["name"])
            else:
                self.assertNotIn("#ore", codes, e["name"])
        self.assertIn("ore", SHAPE["textures"])

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

    def test_the_cell_is_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"], RIG["work"]), RIG["cells"])
        self.assertEqual([c["pos"] for c in RIG["cells"]], [[0, 0, 0]])
        self.assertEqual(checks.lid_gaps(RIG["cells"]), [])
        for b in RIG["cells"][0]["boxes"]:
            self.assertTrue(all(0.0 <= b[i] < b[i + 3] <= 1.0 for i in range(3)), b)


class Readable(unittest.TestCase):
    def test_one_block_few_elements_nothing_thin(self):
        # readable at block scale: a low element count, and no part of the riddle or the frame under 0.75
        # voxels (the charge's layers are steps of a heap)
        self.assertLessEqual(len(SHAPE["elements"]), 36)
        for e in flatten(SHAPE["elements"], textures={}):
            lo, hi = e.aabb()
            self.assertTrue(all(-0.01 < lo[i] and hi[i] < 16.01 for i in range(3)), e.name)
            if not e.name.startswith(("c1", "c2")):
                self.assertGreaterEqual(min(abs(s) for s in e.size), 0.75 - 1e-6, e.name)

    def test_the_riddle_is_square_and_shallow_like_the_pan_with_a_woven_iron_mesh(self):
        riddle = elements("riddle")
        lo, hi = aabb_of(riddle)
        self.assertAlmostEqual(hi[0] - lo[0], hi[2] - lo[2], places=6)
        self.assertLessEqual(hi[1] - lo[1], 2.0 + 1e-6)
        wires = [e for e in riddle if "_wire" in e.name]
        self.assertEqual(len([e for e in wires if "wirex" in e.name]), len([e for e in wires if "wirez" in e.name]))
        self.assertTrue(all(f["texture"] == "#iron" for e in SHAPE["elements"] if "_wire" in e["name"] for f in e["faces"].values()))


class Riddling(unittest.TestCase):
    def test_work_is_one_charges_riddling(self):
        self.assertNotIn("trunkPath", RIG)
        w = rigmath.progress_of(RIG)
        self.assertEqual((w["name"], w["unit"]), ("riddling", "charges"))
        self.assertEqual(w["end"], {"thin": 1.0, "thick": 1.0})
        pace = RIG["riddling"]["shakesPerCharge"]
        self.assertGreater(pace["thick"], pace["thin"])

    def test_the_clock_shakes_the_riddle_only_while_a_charge_is_riddled(self):
        pids = [p["id"] for p in RIG["parts"]]
        for w in (0.0, 0.9, 1.0):
            for pid in pids:
                a = matrix(pid, theta=0.0, work=w, size=2, presence=1.0)
                b = matrix(pid, theta=2.2, work=w, size=2, presence=1.0)
                self.assertEqual([[round(v, 12) for v in r] for r in a], [[round(v, 12) for v in r] for r in b], pid)
        a = matrix("riddle", theta=0.0, work=0.5, size=0, presence=0.0)
        b = matrix("riddle", theta=1.6, work=0.5, size=0, presence=0.0)
        self.assertEqual(a, b)
        c = (make_shape.CX, make_shape.Y0 + 1, make_shape.CZ)
        rest = point("riddle", c, theta=0.0, work=0.4, size=2, presence=1.0)
        moved = point("riddle", c, theta=math.pi / 2, work=0.4, size=2, presence=1.0)
        self.assertGreater(abs(moved[2] - rest[2]), 1.0)

    def test_the_fines_end_in_the_box_and_the_oversize_in_the_riddle(self):
        for k, sunk, fines in ((1, ("c1top",), ("c1fines",)), (2, ("c2mid", "c2top"), ("c2fines1", "c2fines2"))):
            done = dict(theta=0.0, work=1.0, size=k, presence=1.0)
            bed = aabb_of([e for e in posed(f"c{k}base", **done) if e.name.endswith("_bed")])
            for pid in sunk:
                lo, hi = aabb_of(posed(pid, **done))
                for i in range(3):
                    self.assertGreater(lo[i], bed[0][i], pid)
                    self.assertLess(hi[i], bed[1][i], pid)
            hidden = aabb_of(posed(fines[0], theta=0.0, work=0.0, size=k, presence=1.0))
            self.assertLess(hidden[1][1], make_shape.BOX_BOTTOM, f"{fines[0]} at W 0")
            low = aabb_of(posed(fines[0], **done))[0][1]
            self.assertAlmostEqual(low, make_shape.BOX_BOTTOM, places=3)
            # the oversize, the bed and its lumps, left on the mesh of the riddle set down on its bearers
            self.assertAlmostEqual(bed[0][1], make_shape.Y0 + make_shape.MESH_TOP, places=3)
            lumps = [e for e in posed(f"c{k}base", **done) if "_lump" in e.name]
            self.assertGreaterEqual(len(lumps), 2)
            self.assertTrue(all(e.aabb()[1][1] > bed[1][1] + 0.5 for e in lumps))


class Anchors(unittest.TestCase):
    def test_sides_and_anchors(self):
        self.assertNotIn("powerCell", RIG)
        self.assertNotIn("powerFace", RIG)
        self.assertEqual((RIG["operatorSide"], RIG["infeedSide"], RIG["outputSide"]), ("north", "east", "south"))
        for key in ("output", "charge"):
            self.assertEqual(len(RIG[key]["pos"]), 3)
            self.assertTrue(all(0 < v < 1 for v in RIG[key]["pos"]), key)
        # the output on the fines in the box, under the charge in the riddle
        self.assertLess(RIG["output"]["pos"][1] * 16, make_shape.BOX_H)
        self.assertGreater(RIG["charge"]["pos"][1], RIG["output"]["pos"][1])


if __name__ == "__main__":
    unittest.main()
