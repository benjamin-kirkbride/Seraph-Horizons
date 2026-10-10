"""The riddle's generated model files (mods-src/seraphhorizons/Riddle/tools/make_shape.py): the hand riddle and
the riddle on its stand.

These hold what the generator wrote to its own rules, without running it: both shipped rigs parse with the
shared rig maths and use the `requires` vocabulary of the model, their reference poses are their own maths,
the cells are rebuilt from the shipped shapes (and are the footprint's, boxed inside it, though the stand's
lever rises above it), the work is one charge's riddling, the clock shakes the riddle only while a charge is
riddled, the stand's riddle is the hand riddle's moved, hangs on its pins, rides its roller and tips the
oversize into the second block, and the anchors are where the model puts them. Run with
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


MODELS = {
    "hand": {"rig": load("assets/seraphhorizons/config/riddle-rig.json"),
             "shape": load("assets/seraphhorizons/shapes/block/riddle.json"),
             "frame": load("assets/seraphhorizons/shapes/block/riddle_frame.json"),
             "reference": load("tests/Riddle/rig-reference.json"),
             "requires": {"riddle", "chargesmall", "chargefull", None},
             "cells": [(0, 0, 0)]},
    "stand": {"rig": load("assets/seraphhorizons/config/riddlestand-rig.json"),
              "shape": load("assets/seraphhorizons/shapes/block/riddlestand.json"),
              "frame": load("assets/seraphhorizons/shapes/block/riddlestand_frame.json"),
              "reference": load("tests/Riddle/stand-rig-reference.json"),
              "requires": {"boxes", "riddle", "hangers", "lever", "chargesmall", "chargefull", None},
              "cells": [(0, 0, 0), (0, 0, 1)]},
}


def inputs(pose):
    return {k: pose[k] for k in ("theta", "work", "size", "presence")}


def matrix(model, pid, **ins):
    rig = MODELS[model]["rig"]
    return rigmath.part_matrix(rig["parts"], pid, ins, rig["work"])


def point(model, pid, p, **ins):
    return [v * 16 for v in rigmath.apply(matrix(model, pid, **ins), [c / 16 for c in p])]


def elements(model, part):
    rig, shape = MODELS[model]["rig"], MODELS[model]["shape"]
    return [e for e in flatten(shape["elements"], textures={}) if rigmath.part_of(rig["parts"], e.name) == part]


def posed(model, part, **ins):
    m = matrix(model, part, **ins)
    return [rigmath.posed(e, m) for e in elements(model, part)]


class Rigs(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        for name, m in MODELS.items():
            parts = m["rig"]["parts"]
            ids = [p["id"] for p in parts]
            self.assertEqual(len(ids), len(set(ids)), name)
            self.assertEqual(ids[-1], "frame", name)
            for p in parts:
                if p.get("ride"):
                    self.assertIn(p["ride"], ids)
                for d in p["drivers"]:
                    rigmath.validate_driver(d)
            self.assertEqual({p["requires"] for p in parts}, m["requires"], name)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        for name, m in MODELS.items():
            parts = m["rig"]["parts"]
            names = [e["name"] for e in m["shape"]["elements"]]
            self.assertEqual(len(names), len(set(names)), name)
            self.assertEqual({rigmath.part_of(parts, n) for n in names}, {p["id"] for p in parts}, name)
            frame = {e["name"] for e in m["frame"]["elements"]}
            self.assertEqual(frame, {n for n in names if rigmath.part_of(parts, n) == "frame"}, name)

    def test_the_charge_wears_the_ore_texture_code(self):
        # the renderer sets "ore" to the crushed ore's texture
        for name, m in MODELS.items():
            parts = m["rig"]["parts"]
            for e in m["shape"]["elements"]:
                pid = rigmath.part_of(parts, e["name"])
                codes = {f["texture"] for f in e["faces"].values()}
                if pid.startswith(("c1", "c2")):
                    self.assertEqual(codes, {"#ore"}, e["name"])
                else:
                    self.assertNotIn("#ore", codes, e["name"])
            self.assertIn("ore", m["shape"]["textures"])

    def test_reference_poses_are_the_rigs_own_maths(self):
        for name, m in MODELS.items():
            rig, poses = m["rig"], m["reference"]["poses"]
            self.assertGreater(len(poses), 40)
            self.assertEqual({p["size"] for p in poses}, {0, 1, 2})
            for pose in poses[::2]:
                for pid, want in pose["matrices"].items():
                    got = rigmath.part_matrix(rig["parts"], pid, inputs(pose), rig["work"])
                    for r in range(3):
                        for c in range(4):
                            self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{name} {pid} at {inputs(pose)}")

    def test_cells_are_the_footprint_rebuilt_from_the_shipped_shapes(self):
        for name, m in MODELS.items():
            cells = make_shape.shipped_cells(m["shape"], m["rig"]["parts"], m["rig"]["work"], (1, 1, len(m["cells"])))
            self.assertEqual(cells, m["rig"]["cells"], name)
            self.assertEqual([tuple(c["pos"]) for c in m["rig"]["cells"]], m["cells"], name)
            self.assertEqual(checks.lid_gaps(m["rig"]["cells"]), [])
            for c in m["rig"]["cells"]:
                for b in c["boxes"]:
                    self.assertTrue(all(0.0 <= b[i] < b[i + 3] <= 1.0 for i in range(3)), (name, c["pos"], b))


class Riddling(unittest.TestCase):
    def test_work_is_one_charges_riddling(self):
        for name, m in MODELS.items():
            rig = m["rig"]
            self.assertNotIn("trunkPath", rig)
            w = rigmath.progress_of(rig)
            self.assertEqual((w["name"], w["unit"]), ("riddling", "charges"))
            self.assertEqual(w["end"], {"thin": 1.0, "thick": 1.0})
            pace = rig["riddling"]["shakesPerCharge"]
            self.assertGreater(pace["thick"], pace["thin"], name)

    def test_the_clock_shakes_the_riddle_only_while_a_charge_is_riddled(self):
        for name in MODELS:
            pids = [p["id"] for p in MODELS[name]["rig"]["parts"]]
            for w in (0.0, 0.9, 1.0) + ((0.7,) if name == "stand" else ()):
                for pid in pids:
                    a = matrix(name, pid, theta=0.0, work=w, size=2, presence=1.0)
                    b = matrix(name, pid, theta=2.2, work=w, size=2, presence=1.0)
                    self.assertEqual([[round(v, 12) for v in r] for r in a], [[round(v, 12) for v in r] for r in b], f"{name} {pid}")
            a = matrix(name, "riddle", theta=0.0, work=0.5, size=0, presence=0.0)
            b = matrix(name, "riddle", theta=1.6, work=0.5, size=0, presence=0.0)
            self.assertEqual(a, b)
            c = make_shape.HAND_C if name == "hand" else make_shape.STAND_C
            rest = point(name, "riddle", [c[0], 8, c[1]], theta=0.0, work=0.4, size=2, presence=1.0)
            moved = point(name, "riddle", [c[0], 8, c[1]], theta=math.pi / 2, work=0.4, size=2, presence=1.0)
            self.assertGreater(abs(moved[2] - rest[2]), 1.0, name)

    def test_the_charge_settles_into_its_bed_and_the_fines_end_in_the_box(self):
        for name in MODELS:
            for k, sunk, fines in ((1, ("c1top",), ("c1fines",)), (2, ("c2mid", "c2top"), ("c2fines1", "c2fines2"))):
                bed = aabb_of(posed(name, f"c{k}base", theta=0.0, work=1.0, size=k, presence=1.0))
                for pid in sunk:
                    lo, hi = aabb_of(posed(name, pid, theta=0.0, work=1.0, size=k, presence=1.0))
                    for i in range(3):
                        self.assertGreater(lo[i], bed[0][i], f"{name} {pid}")
                        self.assertLess(hi[i], bed[1][i], f"{name} {pid}")
                hidden = aabb_of(posed(name, fines[0], theta=0.0, work=0.0, size=k, presence=1.0))
                self.assertLess(hidden[1][1], make_shape.BOX_BOTTOM, f"{name} {fines[0]} at W 0")
                low = aabb_of(posed(name, fines[0], theta=0.0, work=1.0, size=k, presence=1.0))[0][1]
                self.assertAlmostEqual(low, make_shape.BOX_BOTTOM, places=3)


class Stand(unittest.TestCase):
    def test_the_stand_riddle_is_the_hand_riddle_moved(self):
        hand, stand = elements("hand", "riddle"), elements("stand", "riddle")
        self.assertEqual([e.name for e in hand], [e.name for e in stand])
        d = (make_shape.STAND_C[0] - make_shape.HAND_C[0], make_shape.STAND_Y0 - make_shape.HAND_Y0, make_shape.STAND_C[1] - make_shape.HAND_C[1])
        for a, b in zip(hand, stand):
            for i in range(3):
                self.assertAlmostEqual(a.size[i], b.size[i], places=4)
                self.assertAlmostEqual(a.c[i] + d[i], b.c[i], places=4)

    def test_the_riddle_is_square_and_shallow_like_the_pan(self):
        lo, hi = aabb_of(elements("hand", "riddle"))
        self.assertAlmostEqual(hi[0] - lo[0], hi[2] - lo[2], places=6)
        self.assertLessEqual(hi[1] - lo[1], 2.0 + 1e-6)

    def test_it_hangs_on_its_pins_and_the_lever_works_it_through_a_parallelogram(self):
        parts = {p["id"]: p for p in MODELS["stand"]["rig"]["parts"]}
        self.assertEqual(parts["riddle"]["ride"], "hangers")
        self.assertEqual(parts["link"]["ride"], "hangers")
        self.assertEqual(parts["lugs"]["ride"], "riddle")
        for th in (0.0, 0.7, 1.6, 4.7):
            for w in (0.06, 0.3, 0.5, 0.7, 0.75, 0.8, 0.9, 0.95):
                ins = dict(theta=th, work=w, size=2, presence=1.0)
                n = [2.25, make_shape.HANG_Y, make_shape.HANG_Z]
                a, b = point("stand", "lugs", n, **ins), point("stand", "hangers", n, **ins)
                self.assertLess(max(abs(a[i] - b[i]) for i in range(3)), 1e-5)
                for z, other in ((make_shape.HANG_Z, "hangers"), (make_shape.LEVER_Z, "lever")):
                    q = [2.0, make_shape.LINK_PIN_Y, z]
                    a, b = point("stand", "link", q, **ins), point("stand", other, q, **ins)
                    self.assertLess(max(abs(a[i] - b[i]) for i in range(3)), 1e-5)

    def test_pulled_back_it_tips_the_oversize_into_the_second_block_and_comes_back(self):
        full = dict(theta=0.0, work=0.8, size=2, presence=1.0)
        m = matrix("stand", "riddle", **full)
        self.assertGreater(math.degrees(math.atan2(m[2][1], m[1][1])), 35.0)
        self.assertGreater(aabb_of(posed("stand", "riddle", **full))[1][2], 17.0)
        for pid in ("hangers", "riddle", "lever", "link"):
            m = matrix("stand", pid, theta=0.0, work=1.0, size=2, presence=1.0)
            self.assertEqual([[round(v, 9) for v in r[:4]] for r in m[:3]], [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0]], pid)
        for k in (1, 2):
            lo, hi = aabb_of(posed("stand", f"c{k}base", theta=0.0, work=1.0, size=k, presence=1.0))
            self.assertAlmostEqual(lo[1], make_shape.BOX_BOTTOM, places=3)
            self.assertGreater(lo[2], 16.0)

    def test_only_the_lever_rises_above_the_block(self):
        rig = MODELS["stand"]["rig"]
        above = set()
        for w in (0.0, 0.3, 0.74, 0.8, 0.85, 0.95):
            for p in rig["parts"]:
                for e in posed("stand", p["id"], theta=1.0, work=w, size=2, presence=1.0):
                    lo, hi = e.aabb()
                    self.assertGreater(lo[1], -0.01, e.name)
                    self.assertTrue(-0.01 < lo[0] and hi[0] < 16.01 and -0.01 < lo[2] and hi[2] < 32.01, e.name)
                    if hi[1] > 16.01:
                        above.add(p["id"])
        self.assertEqual(above, {"lever"})


class Anchors(unittest.TestCase):
    def test_sides_and_anchors(self):
        for name, m in MODELS.items():
            rig = m["rig"]
            self.assertNotIn("powerCell", rig)
            self.assertNotIn("powerFace", rig)
            self.assertEqual(rig["infeedSide"], "east")
            for key in ("output", "charge"):
                self.assertEqual(len(rig[key]["pos"]), 3)
            # the output on the fines in the box, under the charge in the riddle, in the controller's cell
            self.assertLess(rig["output"]["pos"][1] * 16, make_shape.FINES_H)
            self.assertGreater(rig["charge"]["pos"][1], rig["output"]["pos"][1])
            self.assertLess(rig["output"]["pos"][2], 1.0)
        self.assertEqual(MODELS["hand"]["rig"]["outputSide"], "south")
        stand = MODELS["stand"]["rig"]
        self.assertEqual((stand["outputSide"], stand["oversizeSide"]), ("west", "south"))
        self.assertGreater(stand["oversize"]["pos"][2], 1.0)
        self.assertNotIn("oversize", MODELS["hand"]["rig"])


if __name__ == "__main__":
    unittest.main()
