"""The parting furnace's generated model files (mods-src/seraphhorizons/PartingFurnace/tools/make_shape.py).

These hold what the generator wrote to its own rules: it reproduces the committed files; the shipped rig parses
with the shared rig maths, reads theta alone, and its `requires` are one per fitted set, named for its tier and
listed tier by tier (the rig's `tiers`), the frame needing none; its reference poses are its own maths; the cells
are rebuilt from the shipped shape; the anchors are the generator's cells and faces, with the chutes' ends on
them; the stirrer turns at the bevel pair's ratio and the yoke rises with the crank; and the site's scenario
states are the rig's tiers, cumulative. Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import math
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
TOOLS = MOD / "PartingFurnace" / "tools"
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(TOOLS))

from machinegen import rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("partingfurnace_make_shape", TOOLS / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

SHAPE_PATH = MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "partingfurnace.json"
FRAME_PATH = MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "partingfurnace_frame.json"
RIG_PATH = MOD / "assets" / "seraphhorizons" / "config" / "partingfurnace-rig.json"
REFERENCE_PATH = MOD / "tests" / "PartingFurnace" / "rig-reference.json"
SHAPE = json.loads(SHAPE_PATH.read_text())
FRAME = json.loads(FRAME_PATH.read_text())
RIG = json.loads(RIG_PATH.read_text())
REFERENCE = json.loads(REFERENCE_PATH.read_text())
MANIFEST = json.loads((ROOT / "site" / "models.json").read_text())
TIERS = {"2": ["t2blast", "t2cupel", "t2liquation"], "3": ["t3acid", "t3precip"], "4": ["t4kettle", "t4stirrer", "t4chutes"]}


def matrix(pid, theta):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta})


def angle_about(m, axis):
    """A pure rotation's angle about x or y, from its matrix."""
    if axis == "x":
        return math.atan2(m[2][1], m[1][1])
    return math.atan2(m[0][2], m[0][0])


class Generator(unittest.TestCase):
    def test_the_generator_reproduces_the_files(self):
        with tempfile.TemporaryDirectory() as d:
            run = subprocess.run([sys.executable, str(TOOLS / "make_shape.py"), "--out", d], capture_output=True, text=True)
            self.assertEqual(run.returncode, 0, run.stdout[-3000:] + run.stderr[-3000:])
            for path in (SHAPE_PATH, FRAME_PATH, RIG_PATH, REFERENCE_PATH):
                self.assertEqual((Path(d) / path.name).read_text(), path.read_text(), f"{path.name} is not what the generator writes: regenerate it")


class Rig(unittest.TestCase):
    def test_parts_parse_and_requires_are_the_tiers_sets(self):
        self.assertEqual(RIG["tiers"], TIERS)
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        self.assertIsNone(RIG["parts"][-1]["requires"])
        order = []
        for p in RIG["parts"]:
            for d in p["drivers"]:
                rigmath.validate_driver(d)
            self.assertIsNone(p.get("ride"))
            r = p["requires"]
            if r is not None:
                tier = next(k for k, vs in TIERS.items() if r in vs)
                self.assertTrue(r.startswith(f"t{tier}"), r)
                if r not in order:
                    order.append(r)
        # listed tier by tier, every set used
        self.assertEqual(order, [v for k in sorted(TIERS) for v in TIERS[k]])

    def test_theta_is_the_only_input(self):
        self.assertNotIn("work", RIG)
        self.assertNotIn("trunkPath", RIG)
        for p in RIG["parts"]:
            for d in p["drivers"]:
                self.assertIn(d["type"], ("rotate", "slide"))
                self.assertEqual(rigmath.driver_input(d), "theta")
        moving = {p["id"] for p in RIG["parts"] if p["drivers"]}
        self.assertEqual(moving, {"lineshaft", "yoke", "stirrer", "bevelpinion"})

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_textures_are_declared(self):
        used = {f["texture"].lstrip("#") for e in SHAPE["elements"] for f in e["faces"].values()}
        self.assertEqual(used, set(SHAPE["textures"]))
        for code, path in SHAPE["textures"].items():
            self.assertTrue(path.startswith("game:block/"), code)

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreaterEqual(len(poses), 8)
        self.assertTrue(any(p["theta"] < 0 for p in poses))
        self.assertTrue(any(abs(p["theta"]) > 2 * math.pi for p in poses))
        for pose in poses:
            self.assertEqual(set(pose["matrices"]), {p["id"] for p in RIG["parts"]})
            for pid, want in pose["matrices"].items():
                got = rigmath.part_matrix(RIG["parts"], pid, {"theta": pose["theta"], "travel": pose["travel"]})
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at theta {pose['theta']}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        self.assertFalse(any(c.get("hollow") or "lid" in c for c in RIG["cells"]))

    def test_anchors_are_the_generators(self):
        o = make_shape.ORIGIN_CELL
        for key, cell in (("powerCell", make_shape.POWER_CELL), ("inputCell", make_shape.INPUT_CELL), ("outputCell", make_shape.OUTPUT_CELL)):
            self.assertEqual(RIG[key], [cell[k] - o[k] for k in range(3)], key)
            self.assertIn(RIG[key], [c["pos"] for c in RIG["cells"]], key)
        self.assertEqual((RIG["powerFace"], RIG["inputFace"], RIG["outputFace"]), ("east", "north", "south"))
        self.assertIn([0, 0, 0], [c["pos"] for c in RIG["cells"]])
        # each chute's anchor point is on its cell's face
        for key, face, k, side in (("input", "inputCell", 2, 0), ("output", "outputCell", 2, 1)):
            pos, cell = RIG[key]["pos"], RIG[face]
            self.assertAlmostEqual(pos[k], cell[k] + side, places=6, msg=key)
            for q in (0, 1):
                self.assertTrue(cell[q] <= pos[q] <= cell[q] + 1, key)


class Mechanism(unittest.TestCase):
    def test_the_stirrer_turns_at_the_bevel_pairs_ratio(self):
        self.assertEqual(make_shape.PINION_TEETH * 2, make_shape.WHEEL_TEETH)
        for th in (0.3, 1.2, -0.8):
            shaft = angle_about(matrix("lineshaft", th), "x")
            pinion = angle_about(matrix("bevelpinion", th), "x")
            stirrer = angle_about(matrix("stirrer", th), "y")
            self.assertAlmostEqual(pinion, shaft, places=9)
            self.assertAlmostEqual(stirrer, th * make_shape.PINION_TEETH / make_shape.WHEEL_TEETH, places=9)

    def test_the_yoke_rises_with_the_crank(self):
        # the crank pin is north of the shaft at theta 0: its height is the shaft's plus r sin(theta), and the
        # yoke's slot rides it
        r = make_shape.CRANK_R / 16
        o = make_shape.ORIGIN_CELL               # the shipped rig is the build frame moved by the controller cell
        axis = (make_shape.LS[0] / 16 - o[1], make_shape.LS[1] / 16 - o[2])
        for th in (0.0, 0.7, 1.5708, 3.0, -2.2):
            self.assertAlmostEqual(matrix("yoke", th)[1][3], r * math.sin(th), places=9)
            pin = rigmath.apply(matrix("lineshaft", th), [0.5 - o[0], axis[0], axis[1] - r])
            self.assertAlmostEqual(pin[1] - axis[0], r * math.sin(th), places=9)


class Scenario(unittest.TestCase):
    def test_the_viewers_states_are_the_tiers(self):
        model = next(m for m in MANIFEST["models"] if m["id"] == "parting-furnace")
        self.assertEqual(model["rig"], str(RIG_PATH.relative_to(ROOT)))
        self.assertEqual(model["shape"], str(SHAPE_PATH.relative_to(ROOT)))
        states = model["scenario"]["states"]
        fitted, want = [], [[]]
        for k in sorted(TIERS):
            fitted = fitted + TIERS[k]
            want.append(fitted)
        self.assertEqual([o["fitted"] for o in states["options"]], want)
        self.assertEqual(states["default"], states["options"][-1]["id"])
        self.assertEqual(set(model["scenario"]["requires"]), {v for vs in TIERS.values() for v in vs})


if __name__ == "__main__":
    unittest.main()
