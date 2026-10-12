"""The parting furnace's generated model files (mods-src/seraphhorizons/PartingFurnace/tools/make_shape.py).

These hold what the generator wrote to its own rules: it reproduces the committed files; the shipped rig parses
with the shared rig maths, reads theta alone, and its `requires` are one per fitted set, each in one level and
listed level by level (the rig's `levels`), the frame needing none; its reference poses are its own maths; the
cells are rebuilt from the shipped shape, the frame's boxes apart from each fitted set's; the anchors are the
generator's cells and faces, with the chutes' ends and the water pipe on them; the stirrer turns with the shaft
through the mitre pair and the yoke rises with the crank; and the site's scenario states are the rig's levels,
cumulative. Run with `python3 -m unittest discover -s tools/tests`.
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
LEVELS = {"1": ["blast", "cupel", "liquation", "acid"], "2": ["kettle", "stirrer", "chutes"]}
PIPES = ["pipecopper", "pipelead"]          # the water pipe, one part per metal, fitted with the acid set
REQUIRES = LEVELS["1"] + PIPES + LEVELS["2"]


def matrix(pid, theta):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta})


def angle_about(m, axis):
    """A pure rotation's angle about y or z, from its matrix."""
    if axis == "z":
        return math.atan2(m[1][0], m[0][0])
    return math.atan2(m[0][2], m[0][0])


class Generator(unittest.TestCase):
    def test_the_generator_reproduces_the_files(self):
        with tempfile.TemporaryDirectory() as d:
            run = subprocess.run([sys.executable, str(TOOLS / "make_shape.py"), "--out", d], capture_output=True, text=True)
            self.assertEqual(run.returncode, 0, run.stdout[-3000:] + run.stderr[-3000:])
            for path in (SHAPE_PATH, FRAME_PATH, RIG_PATH, REFERENCE_PATH):
                self.assertEqual((Path(d) / path.name).read_text(), path.read_text(), f"{path.name} is not what the generator writes: regenerate it")


class Rig(unittest.TestCase):
    def test_parts_parse_and_requires_are_the_levels_sets(self):
        self.assertEqual(RIG["levels"], LEVELS)
        self.assertEqual(RIG["waterPipes"], PIPES)
        self.assertNotIn("tiers", RIG)
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
                self.assertEqual(sum(r in vs for vs in LEVELS.values()) + (r in PIPES), 1, r)
                if r not in order:
                    order.append(r)
        # listed level by level, the water pipe's after the acid set's, every set used
        self.assertEqual(order, REQUIRES)

    def test_theta_is_the_only_input(self):
        self.assertNotIn("work", RIG)
        self.assertNotIn("trunkPath", RIG)
        for p in RIG["parts"]:
            for d in p["drivers"]:
                self.assertIn(d["type"], ("rotate", "slide"))
                self.assertEqual(rigmath.driver_input(d), "theta")
        moving = {p["id"] for p in RIG["parts"] if p["drivers"]}
        self.assertEqual(moving, {"shaft", "yoke", "stirrer", "bevelpinion"})

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


class Cells(unittest.TestCase):
    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        self.assertFalse(any(c.get("hollow") or "lid" in c for c in RIG["cells"]))

    def test_frame_boxes_and_fitted_boxes(self):
        """A cell's boxes are the frame's; its fitted boxes are keyed by requires values in the levels' order, each a
        box inside the cell; and the footprint is compact: 4 x 2 by 2 high, the stack's column 2 higher."""
        cells = RIG["cells"]
        self.assertEqual(len(cells), 18)
        for c in cells:
            keys = list(c.get("fitted", {}))
            self.assertEqual(keys, [r for r in REQUIRES if r in keys], c["pos"])
            for b in c.get("boxes", []) + [b for bs in c.get("fitted", {}).values() for b in bs]:
                self.assertEqual(len(b), 6)
                for k in range(3):
                    self.assertTrue(0 <= b[k] < b[k + 3] <= 1, (c["pos"], b))
        # the frame alone (the floor and the stack) leaves the kettle's upper cell empty, and level 1 has only the
        # engine shaft's end in it, where the pinion goes: it collides with what is fitted
        kettle_top = next(c for c in cells if c["pos"] == RIG["inputCell"])
        self.assertNotIn("boxes", kettle_top)
        self.assertEqual(set(kettle_top["fitted"]) & set(LEVELS["1"]), {"blast"})
        (shaft_end,) = kettle_top["fitted"]["blast"]
        self.assertLess(math.prod(shaft_end[k + 3] - shaft_end[k] for k in range(3)), 0.01)

    def test_every_ground_cell_has_the_floor(self):
        for c in RIG["cells"]:
            if c["pos"][1] == 0:
                self.assertIn([0.0, 0.0, 0.0, 1.0, 0.125, 1.0], c["boxes"], c["pos"])


class Anchors(unittest.TestCase):
    def test_anchors_are_the_generators(self):
        o = make_shape.ORIGIN_CELL
        for key, cell in (("powerCell", make_shape.POWER_CELL), ("waterCell", make_shape.WATER_CELL),
                          ("inputCell", make_shape.INPUT_CELL), ("outputCell", make_shape.OUTPUT_CELL)):
            self.assertEqual(RIG[key], [cell[k] - o[k] for k in range(3)], key)
            self.assertIn(RIG[key], [c["pos"] for c in RIG["cells"]], key)
        self.assertEqual((RIG["powerFace"], RIG["waterFace"], RIG["inputFace"], RIG["outputFace"]), ("north", "south", "east", "south"))
        self.assertIn([0, 0, 0], [c["pos"] for c in RIG["cells"]])
        # each chute's anchor point is on its cell's face
        for key, face, k, side in (("input", "inputCell", 0, 1), ("output", "outputCell", 2, 1)):
            pos, cell = RIG[key]["pos"], RIG[face]
            self.assertAlmostEqual(pos[k], cell[k] + side, places=6, msg=key)
            for q in range(3):
                if q != k:
                    self.assertTrue(cell[q] <= pos[q] <= cell[q] + 1, key)

    def test_the_water_pipe_meets_the_water_face_in_ppexs_section(self):
        cell = RIG["waterCell"]
        pipe = [e for e in SHAPE["elements"] if e["name"].startswith("pipecopper_inlet")]
        lo = [min(e["from"][k] for e in pipe) for k in range(3)]
        hi = [max(e["to"][k] for e in pipe) for k in range(3)]
        self.assertAlmostEqual(hi[2], (cell[2] + 1) * 16, places=4)
        self.assertAlmostEqual(hi[0] - lo[0], 6.0, places=4)
        self.assertAlmostEqual(hi[1] - lo[1], 6.0, places=4)
        self.assertAlmostEqual((lo[0] + hi[0]) / 2, cell[0] * 16 + 8, places=4)
        self.assertAlmostEqual((lo[1] + hi[1]) / 2, cell[1] * 16 + 8, places=4)


class Infeed(unittest.TestCase):
    def test_the_infeed_is_the_games_chute_at_the_input_face(self):
        """Its walls at the face are the game's straight chute's section in the cell, in the chute's texture."""
        cell = RIG["inputCell"]
        walls = [e for e in SHAPE["elements"] if e["name"].startswith("ch_in_")]
        self.assertEqual(len(walls), 4)
        got = []
        for e in walls:
            self.assertNotIn("rotationX", e)
            self.assertAlmostEqual(e["to"][0], (cell[0] + 1) * 16, places=4)
            got.append(tuple(round(v, 4) for v in (e["from"][1] - cell[1] * 16, e["to"][1] - cell[1] * 16,
                                                     e["from"][2] - cell[2] * 16, e["to"][2] - cell[2] * 16)))
            self.assertEqual({f["texture"] for f in e["faces"].values()}, {"#chute"})
        self.assertEqual(sorted(got), sorted(make_shape.VANILLA_CHUTE))
        self.assertEqual(SHAPE["textures"]["chute"], "game:block/metal/sheet/copper1")
        self.assertEqual(RIG["input"]["pos"], [cell[0] + 1, cell[1] + 0.5, cell[2] + 0.5])

    def test_the_profile_is_the_games_when_the_game_is_installed(self):
        """VANILLA_CHUTE against the game's own shape (shapes/block/metal/chute/straight.json, along x as chute-straight-we
        places it): skipped without a game install (CI)."""
        import os
        import re
        roots = [os.environ.get("VINTAGE_STORY", ""), str(Path.home() / "Games" / "vintagestory")]
        path = next((Path(r) / "assets" / "survival" / "shapes" / "block" / "metal" / "chute" / "straight.json" for r in roots
                     if r and (Path(r) / "assets" / "survival" / "shapes" / "block" / "metal" / "chute" / "straight.json").exists()), None)
        if path is None:
            self.skipTest("no game install")
        text = re.sub(r",(\s*[}\]])", r"\1", re.sub(r"//[^\n]*", "", path.read_text()))
        walls = sorted((e["from"][1], e["to"][1], e["from"][2], e["to"][2]) for e in json.loads(text)["elements"])
        for e in json.loads(text)["elements"]:
            self.assertEqual((e["from"][0], e["to"][0]), (0.0, 16.0))
        self.assertEqual(walls, sorted(make_shape.VANILLA_CHUTE))


class Pipes(unittest.TestCase):
    def test_the_water_pipe_is_drawn_in_each_metal_as_soldered_ppex_pipe(self):
        by = {p: [e for e in SHAPE["elements"] if e["name"].startswith(p + "_")] for p in PIPES}
        first = by[PIPES[0]]
        self.assertEqual(len(first), 8)                 # four walls, and four of the wiped joint at the cock
        for p in PIPES:
            self.assertEqual([p + e["name"][len(PIPES[0]):] for e in first], [e["name"] for e in by[p]])
            for a, b in zip(first, by[p]):
                self.assertEqual((a["from"], a["to"]), (b["from"], b["to"]))
                want = "#solder" if "_joint" in b["name"] else f"#{p}"
                self.assertEqual({f["texture"] for f in b["faces"].values()}, {want}, b["name"])
        self.assertEqual(SHAPE["textures"]["pipecopper"], "game:block/metal/sheet-plain/copper4")
        self.assertEqual(SHAPE["textures"]["pipelead"], "game:block/metal/sheet-plain/lead4")
        self.assertEqual(SHAPE["textures"]["solder"], "game:block/metal/ingot/leadsolder")


class Mechanism(unittest.TestCase):
    def test_every_motion_works_either_way_round(self):
        # nothing reads the travel; backwards the yoke covers the same stroke, and the stirrer turns the other way
        for p in RIG["parts"]:
            for d in p["drivers"]:
                self.assertEqual(rigmath.driver_input(d), "theta")
                self.assertNotIn("rectified", d)
        fw = [matrix("yoke", 2 * math.pi * i / 36)[1][3] for i in range(37)]
        bw = [matrix("yoke", -2 * math.pi * i / 36)[1][3] for i in range(37)]
        self.assertAlmostEqual(min(fw), min(bw), places=9)
        self.assertAlmostEqual(max(fw), max(bw), places=9)


    def test_the_stirrer_turns_with_the_shaft_through_the_mitre_pair(self):
        self.assertEqual(make_shape.PINION_TEETH, make_shape.WHEEL_TEETH)
        for th in (0.3, 1.2, -0.8):
            shaft = angle_about(matrix("shaft", th), "z")
            pinion = angle_about(matrix("bevelpinion", th), "z")
            stirrer = angle_about(matrix("stirrer", th), "y")
            self.assertAlmostEqual(pinion, shaft, places=9)
            self.assertAlmostEqual(shaft, th, places=9)
            self.assertAlmostEqual(stirrer, -th, places=9)

    def test_the_yoke_rises_with_the_crank(self):
        # the crank pin is east of the shaft at theta 0: its height is the shaft's plus r sin(theta), and the yoke's
        # slot rides it
        r = make_shape.CRANK_R / 16
        o = make_shape.ORIGIN_CELL               # the shipped rig is the build frame moved by the controller cell
        axis = (make_shape.SHAFT[0] / 16 - o[0], make_shape.SHAFT[1] / 16 - o[1])
        for th in (0.0, 0.7, 1.5708, 3.0, -2.2):
            self.assertAlmostEqual(matrix("yoke", th)[1][3], r * math.sin(th), places=9)
            pin = rigmath.apply(matrix("shaft", th), [axis[0] + r, axis[1], 0.5 - o[2]])
            self.assertAlmostEqual(pin[1] - axis[1], r * math.sin(th), places=9)


class Scenario(unittest.TestCase):
    def test_the_viewers_states_are_the_levels(self):
        model = next(m for m in MANIFEST["models"] if m["id"] == "parting-furnace")
        self.assertEqual(model["rig"], str(RIG_PATH.relative_to(ROOT)))
        self.assertEqual(model["shape"], str(SHAPE_PATH.relative_to(ROOT)))
        groups = {g["id"]: g for g in model["scenario"]["states"]}
        self.assertEqual(list(groups), ["level", "pipes"])
        states = groups["level"]
        self.assertEqual(states["values"], LEVELS["1"] + LEVELS["2"])
        pipes = groups["pipes"]
        self.assertEqual((pipes["values"], pipes["default"], pipes["needs"]), (PIPES, "pipecopper", ["acid"]))
        self.assertEqual([o["fitted"] for o in pipes["options"]], [[p] for p in PIPES])
        self.assertEqual([o["label"] for o in pipes["options"]], ["Copper", "Lead"])
        fitted, want = [], [[]]
        for k in sorted(LEVELS):
            fitted = fitted + LEVELS[k]
            want.append(fitted)
        self.assertEqual([o["fitted"] for o in states["options"]], want)
        self.assertEqual(states["default"], states["options"][-1]["id"])
        self.assertEqual(set(model["scenario"]["requires"]), set(REQUIRES))
        for value, label in model["scenario"]["requires"].items():
            if value in PIPES:
                continue
            level = next(k for k, vs in LEVELS.items() if value in vs)
            self.assertTrue(label.startswith(f"Level {level}: "), label)


if __name__ == "__main__":
    unittest.main()
