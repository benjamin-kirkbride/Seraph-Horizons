"""The riddle's generated model files, both forms (mods-src/seraphhorizons/Riddle/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: each shipped rig parses with the shared rig
maths and uses its form's `requires` vocabulary, its reference poses are its own maths, its cell is rebuilt from its
shipped shape, the work is one charge's riddling, the clock moves the riddle only while a charge is riddled, the heap
of fines shrinks into the mesh and the fines grow in the box while the oversize stays on the mesh, the stand's riddle
is the hand riddle's and hangs level on its straps, each form stays one readable block, and the anchors are where the
model puts them. Run with `python3 -m unittest discover -s tools/tests -p test_riddle_model.py`.
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


class Form:
    """One form's shipped files."""

    def __init__(self, key, name, ref, requires):
        self.key = key
        self.rig = load(f"assets/seraphhorizons/config/{name}-rig.json")
        self.shape = load(f"assets/seraphhorizons/shapes/block/{name}.json")
        self.frame = load(f"assets/seraphhorizons/shapes/block/{name}_frame.json")
        self.reference = load(f"tests/Riddle/{ref}")
        self.requires = requires

    def matrix(self, pid, **ins):
        return rigmath.part_matrix(self.rig["parts"], pid, ins, self.rig["work"])

    def point(self, pid, p, **ins):
        return [v * 16 for v in rigmath.apply(self.matrix(pid, **ins), [c / 16 for c in p])]

    def elements(self, part):
        return [e for e in flatten(self.shape["elements"], textures={}) if rigmath.part_of(self.rig["parts"], e.name) == part]

    def posed(self, part, **ins):
        m = self.matrix(part, **ins)
        return [rigmath.posed(e, m) for e in self.elements(part)]


CHARGE = {"chargesmall", "chargefull"}
HAND = Form("hand", "riddle", "rig-reference.json", {"riddle", None} | CHARGE)
STAND = Form("stand", "riddlestand", "stand-rig-reference.json", {"riddle", "hangers", None} | CHARGE)
FORMS = (HAND, STAND)


def inputs(pose):
    return {k: pose[k] for k in ("theta", "work", "size", "presence")}


def at(k, w, theta=0.0):
    return dict(theta=theta, work=w, size=k, presence=1.0)


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        for f in FORMS:
            parts = f.rig["parts"]
            ids = [p["id"] for p in parts]
            self.assertEqual(len(ids), len(set(ids)), f.key)
            self.assertEqual(ids[-1], "frame")
            for p in parts:
                if p.get("ride"):
                    self.assertIn(p["ride"], ids)
                for d in p["drivers"]:
                    rigmath.validate_driver(d)
            self.assertEqual({p["requires"] for p in parts}, f.requires, f.key)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        for f in FORMS:
            parts = f.rig["parts"]
            names = [e["name"] for e in f.shape["elements"]]
            self.assertEqual(len(names), len(set(names)))
            self.assertEqual({rigmath.part_of(parts, n) for n in names}, {p["id"] for p in parts}, f.key)
            frame = {e["name"] for e in f.frame["elements"]}
            self.assertEqual(frame, {n for n in names if rigmath.part_of(parts, n) == "frame"}, f.key)

    def test_the_charge_wears_the_ore_texture_code(self):
        # the renderer sets "ore" to the crushed ore's texture (its ore's nugget texture)
        for f in FORMS:
            parts = f.rig["parts"]
            for e in f.shape["elements"]:
                pid = rigmath.part_of(parts, e["name"])
                codes = {x["texture"] for x in e["faces"].values()}
                if pid.startswith(("c1", "c2")):
                    self.assertEqual(codes, {"#ore"}, e["name"])
                else:
                    self.assertNotIn("#ore", codes, e["name"])
            self.assertTrue(f.shape["textures"]["ore"].startswith("game:item/resource/nugget/"))

    def test_reference_poses_are_the_rigs_own_maths(self):
        for f in FORMS:
            poses = f.reference["poses"]
            self.assertGreater(len(poses), 40)
            self.assertEqual({p["size"] for p in poses}, {0, 1, 2})
            for pose in poses[::2]:
                for pid, want in pose["matrices"].items():
                    got = f.matrix(pid, **inputs(pose))
                    for r in range(3):
                        for c in range(4):
                            self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{f.key} {pid} at {inputs(pose)}")

    def test_the_cell_is_rebuilt_from_the_shipped_shape(self):
        for f in FORMS:
            self.assertEqual(make_shape.shipped_cells(f.shape, f.rig["parts"], f.rig["work"]), f.rig["cells"], f.key)
            self.assertEqual([c["pos"] for c in f.rig["cells"]], [[0, 0, 0]])
            self.assertEqual(checks.lid_gaps(f.rig["cells"]), [])
            for b in f.rig["cells"][0]["boxes"]:
                self.assertTrue(all(0.0 <= b[i] < b[i + 3] <= 1.0 for i in range(3)), b)


class Readable(unittest.TestCase):
    def test_one_block_few_elements_nothing_thin(self):
        # readable at block scale: a low element count, and no part of the riddle, the stand or the frame under 0.75
        # voxels (the charge's lumps and layers are a heap)
        for f, most in ((HAND, 40), (STAND, 64)):
            self.assertLessEqual(len(f.shape["elements"]), most)
            for e in flatten(f.shape["elements"], textures={}):
                lo, hi = e.aabb()
                self.assertTrue(all(-0.01 < lo[i] and hi[i] < 16.01 for i in range(3)), e.name)
                if not e.name.startswith(("c1", "c2")):
                    self.assertGreaterEqual(min(abs(s) for s in e.size), 0.75 - 1e-6, e.name)

    def test_the_riddle_is_square_and_shallow_like_the_pan_with_a_woven_iron_mesh(self):
        riddle = [e for e in HAND.elements("riddle")]
        lo, hi = aabb_of(riddle)
        self.assertAlmostEqual(hi[0] - lo[0], hi[2] - lo[2], places=6)
        self.assertLessEqual(hi[1] - lo[1], 2.0 + 1e-6)
        wires = [e for e in riddle if "_wire" in e.name]
        self.assertEqual(len([e for e in wires if "wirex" in e.name]), len([e for e in wires if "wirez" in e.name]))
        self.assertTrue(all(x["texture"] == "#iron" for e in HAND.shape["elements"] if "_wire" in e["name"] for x in e["faces"].values()))

    def test_the_stands_riddle_is_the_hand_riddles(self):
        # the same riddle, element for element (up to the z-fighting fix's insets); the stand adds its grip and pins
        hand = {e.name: e for e in HAND.elements("riddle")}
        stand = {e.name: e for e in STAND.elements("riddle")}
        for name, e in hand.items():
            self.assertIn(name, stand)
            s = stand[name]
            self.assertLess(max(abs(a - b) for a, b in zip(e.c + e.size, s.c + s.size)), 0.05, name)
        self.assertTrue(all(n.startswith(("riddle_grip_", "riddle_pin_")) for n in set(stand) - set(hand)))


class Riddling(unittest.TestCase):
    def test_work_is_one_charges_riddling(self):
        for f in FORMS:
            self.assertNotIn("trunkPath", f.rig)
            w = rigmath.progress_of(f.rig)
            self.assertEqual((w["name"], w["unit"]), ("riddling", "charges"))
            self.assertEqual(w["end"], {"thin": 1.0, "thick": 1.0})
            pace = f.rig["riddling"]["shakesPerCharge"]
            self.assertGreater(pace["thick"], pace["thin"])

    def test_the_clock_moves_the_riddle_only_while_a_charge_is_riddled(self):
        for f in FORMS:
            pids = [p["id"] for p in f.rig["parts"]]
            for w in (0.0, 0.9, 1.0):
                for pid in pids:
                    a = f.matrix(pid, **at(2, w))
                    b = f.matrix(pid, **at(2, w, 2.2))
                    self.assertEqual([[round(v, 12) for v in r] for r in a], [[round(v, 12) for v in r] for r in b], pid)
            a = f.matrix("riddle", theta=0.0, work=0.5, size=0, presence=0.0)
            b = f.matrix("riddle", theta=1.6, work=0.5, size=0, presence=0.0)
            self.assertEqual(a, b)
            c = (make_shape.CX, make_shape.Y0 + 1, make_shape.CZ)
            # a quarter turn into the riddling the riddle's middle has moved more than a voxel to or fro
            rest = f.point("riddle", c, **at(2, 0.4))
            moved = f.point("riddle", c, **at(2, 0.4, math.pi / 2))
            self.assertGreater(abs(moved[2] - rest[2]), 1.0, f.key)

    def test_the_stands_riddle_hangs_level_on_its_straps(self):
        # it never turns, and each strap's pin into it moves with it
        for theta in (0.0, 0.7, math.pi / 2, 2.9, 4.4):
            ins = at(2, 0.5, theta)
            r = STAND.matrix("riddle", **ins)
            self.assertTrue(all(abs(r[i][j] - (1.0 if i == j else 0.0)) < 1e-9 for i in range(3) for j in range(3)))
            for part, z in (("hangernorth", make_shape.HANG_Z[0]), ("hangersouth", make_shape.HANG_Z[1])):
                pin = (make_shape.HANGER_X[0], make_shape.PIN_Y, z)
                a, b = STAND.point(part, pin, **ins), STAND.point("riddle", pin, **ins)
                self.assertLess(max(abs(a[i] - b[i]) for i in range(3)), 1e-6, part)
                top = (make_shape.HANGER_X[0], make_shape.PIVOT_Y, z)
                self.assertLess(max(abs(x - y) for x, y in zip(STAND.point(part, top, **ins), top)), 1e-9)

    def test_the_fines_end_in_the_box_and_the_oversize_on_the_mesh(self):
        for f in FORMS:
            for k, heaps, fines in ((1, ("c1heap",), ("c1fines",)), (2, ("c2heap", "c2top"), ("c2fines1", "c2fines2"))):
                start, done = at(k, 0.0), at(k, 1.0)
                for pid in heaps:
                    self.assertFalse(any(e.vanished for e in f.posed(pid, **start)), pid)
                    self.assertTrue(all(e.vanished for e in f.posed(pid, **done)), pid)
                for pid in fines:
                    self.assertTrue(all(e.vanished for e in f.posed(pid, **start)), pid)
                    self.assertFalse(any(e.vanished for e in f.posed(pid, **done)), pid)
                low = aabb_of(f.posed(fines[0], **done))[0][1]
                self.assertAlmostEqual(low, make_shape.BOX_BOTTOM, places=3)
                # the oversize left on the mesh of the riddle at rest
                lumps = f.posed(f"c{k}over", **done)
                self.assertGreaterEqual(len(lumps), 3)
                for e in lumps:
                    self.assertAlmostEqual(e.aabb()[0][1], make_shape.Y0 + make_shape.MESH_TOP, places=3)


class Anchors(unittest.TestCase):
    def test_a_hand_station_takes_no_automated_input_or_output(self):
        # the player loads and unloads it by hand: no power, and no side, face, cell or point for a chute or a
        # hopper; only the side the operator stands on
        for f in FORMS:
            self.assertNotIn("powerCell", f.rig)
            self.assertNotIn("powerFace", f.rig)
            self.assertEqual([k for k in f.rig if k.endswith(("Side", "Face", "Cell"))], ["operatorSide"])
            self.assertEqual(f.rig["operatorSide"], "north")
            for key in make_shape.NO_TRANSFER:
                self.assertNotIn(key, f.rig)

    def test_the_fines_and_the_charge_are_where_the_model_puts_them(self):
        for f in FORMS:
            for key in ("fines", "charge"):
                self.assertEqual(len(f.rig[key]["pos"]), 3)
                self.assertTrue(all(0 < v < 1 for v in f.rig[key]["pos"]), key)
            # the fines in the box, under the charge in the riddle
            self.assertLess(f.rig["fines"]["pos"][1] * 16, make_shape.BOX_H)
            self.assertGreater(f.rig["charge"]["pos"][1], f.rig["fines"]["pos"][1])


if __name__ == "__main__":
    unittest.main()
