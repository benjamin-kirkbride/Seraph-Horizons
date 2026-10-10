"""The riddle's generated model files (mods-src/seraphhorizons/Riddle/tools/make_shape.py): the hand riddle and
the riddle on its stand.

These hold what the generator wrote to its own rules, without running it: both shipped rigs parse with the
shared rig maths and use the `requires` vocabulary of the model, their reference poses are their own maths,
the cells are rebuilt from the shipped shapes, the work is one charge's riddling, the clock shakes the riddle
only while a charge is riddled, the stand's riddle is the hand riddle's raised and hangs level, the fines
end in the tub and the oversize bed in the riddle, and the anchors are where the model puts them. Run with
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
from machinegen.geometry import flatten  # noqa: E402

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
             "requires": {"riddle", "chargesmall", "chargefull", None}},
    "stand": {"rig": load("assets/seraphhorizons/config/riddlestand-rig.json"),
              "shape": load("assets/seraphhorizons/shapes/block/riddlestand.json"),
              "frame": load("assets/seraphhorizons/shapes/block/riddlestand_frame.json"),
              "reference": load("tests/Riddle/stand-rig-reference.json"),
              "requires": {"tub", "riddle", "hangers", "lever", "chargesmall", "chargefull", None}},
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

    def test_cells_are_rebuilt_from_the_shipped_shapes(self):
        for name, m in MODELS.items():
            cells = make_shape.shipped_cells(m["shape"], m["rig"]["parts"], m["rig"]["work"])
            self.assertEqual(cells, m["rig"]["cells"], name)


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
            for w in (0.0, 0.9, 1.0):
                for pid in pids:
                    a = matrix(name, pid, theta=0.0, work=w, size=2, presence=1.0)
                    b = matrix(name, pid, theta=2.2, work=w, size=2, presence=1.0)
                    self.assertEqual([[round(v, 12) for v in r] for r in a], [[round(v, 12) for v in r] for r in b], f"{name} {pid}")
            # with no charge the riddle is still
            a = matrix(name, "riddle", theta=0.0, work=0.5, size=0, presence=0.0)
            b = matrix(name, "riddle", theta=1.6, work=0.5, size=0, presence=0.0)
            self.assertEqual(a, b)
            rest = point(name, "riddle", [8, 8, 8], theta=0.0, work=0.5, size=2, presence=1.0)
            moved = point(name, "riddle", [8, 8, 8], theta=math.pi / 2, work=0.5, size=2, presence=1.0)
            self.assertGreater(abs(moved[2] - rest[2]), 1.0, name)

    def test_the_charge_settles_into_its_bed_and_the_fines_end_in_the_tub(self):
        for name in MODELS:
            for k, sunk, fines in ((1, ("c1top",), ("c1fines",)), (2, ("c2mid", "c2top"), ("c2fines1", "c2fines2"))):
                pre = f"c{k}"
                base = point(name, f"{pre}base", [8, 8, 8], theta=0.0, work=1.0, size=k, presence=1.0)
                riddle = point(name, "riddle", [8, 8, 8], theta=0.0, work=1.0, size=k, presence=1.0)
                self.assertEqual([round(v, 9) for v in base], [round(v, 9) for v in riddle])
                top_bed = min(e.aabb()[1][1] for e in elements(name, f"{pre}base"))
                for pid in sunk:
                    els = [rigmath.posed(e, matrix(name, pid, theta=0.0, work=1.0, size=k, presence=1.0)) for e in elements(name, pid)]
                    self.assertLess(max(e.aabb()[1][1] for e in els), top_bed, f"{name} {pid}")
                for pid in fines:
                    els = [rigmath.posed(e, matrix(name, pid, theta=0.0, work=0.0, size=k, presence=1.0)) for e in elements(name, pid)]
                    self.assertLess(max(e.aabb()[1][1] for e in els), make_shape.TUB_BOTTOM[1], f"{name} {pid} at W 0")
                low = min(rigmath.posed(e, matrix(name, fines[0], theta=0.0, work=1.0, size=k, presence=1.0)).aabb()[0][1]
                          for e in elements(name, fines[0]))
                self.assertAlmostEqual(low, make_shape.TUB_BOTTOM[1], places=3)


class Stand(unittest.TestCase):
    def test_the_stand_riddle_is_the_hand_riddle_raised(self):
        hand, stand = elements("hand", "riddle"), elements("stand", "riddle")
        self.assertEqual([e.name for e in hand], [e.name for e in stand])
        dy = make_shape.STAND_Y0 - make_shape.HAND_Y0
        for a, b in zip(hand, stand):
            for i in range(3):
                self.assertAlmostEqual(a.size[i], b.size[i], places=4)
                self.assertAlmostEqual(a.c[i] + (dy if i == 1 else 0.0), b.c[i], places=4)

    def test_it_hangs_level_on_its_hangers_swung_by_the_lever_through_the_link(self):
        parts = {p["id"]: p for p in MODELS["stand"]["rig"]["parts"]}
        self.assertEqual(parts["riddle"]["ride"], "hangers")
        self.assertEqual(parts["link"]["ride"], "hangers")
        self.assertEqual(parts["lugs"]["ride"], "riddle")
        for th in (0.0, 0.7, 1.6, 3.3, 4.7):
            for w in (0.06, 0.3, 0.6):
                m = matrix("stand", "riddle", theta=th, work=w, size=2, presence=1.0)
                for i in range(3):
                    for j in range(3):
                        self.assertAlmostEqual(m[i][j], 1.0 if i == j else 0.0, places=9)
                # the trunnions stay in the hangers' eyes, the lever's pin in the link's slot
                t = [2.5, make_shape.TRUNNION_Y, 8.0]
                a, b = point("stand", "lugs", t, theta=th, work=w, size=2, presence=1.0), point("stand", "hangers", t, theta=th, work=w, size=2, presence=1.0)
                self.assertLess(max(abs(a[i] - b[i]) for i in range(3)), 1e-5)
                q = [2.0, make_shape.LINK_PIN_Y, make_shape.LEVER_PIVOT[1]]
                a, b = point("stand", "lever", q, theta=th, work=w, size=2, presence=1.0), point("stand", "link", q, theta=th, work=w, size=2, presence=1.0)
                self.assertLess(abs(a[1] - b[1]), 0.35)
                self.assertLess(abs(a[2] - b[2]), 0.03)


class Anchors(unittest.TestCase):
    def test_one_lidded_cell_sides_and_anchors(self):
        for name, m in MODELS.items():
            rig = m["rig"]
            self.assertEqual([tuple(c["pos"]) for c in rig["cells"]], [(0, 0, 0)], name)
            self.assertEqual(checks.lid_gaps(rig["cells"]), [])
            self.assertNotIn("powerCell", rig)
            self.assertNotIn("powerFace", rig)
            self.assertEqual((rig["infeedSide"], rig["outputSide"]), ("east", "south"))
            for key in ("output", "charge"):
                self.assertEqual(len(rig[key]["pos"]), 3)
            # the output on the fines in the tub, under the charge in the riddle
            self.assertLess(rig["output"]["pos"][1] * 16, make_shape.TUB_H)
            self.assertGreater(rig["charge"]["pos"][1], rig["output"]["pos"][1])


if __name__ == "__main__":
    unittest.main()
