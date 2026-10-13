"""The shaking table's generated model files (mods-src/seraphhorizons/ShakingTable/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the
shared rig maths and uses its `requires` vocabulary (the deck, the head, the rectifier, the chutes, a water
line in copper or lead), its reference poses are its own maths, the cells are rebuilt from the shipped shape,
the head shaft and the deck move by the axle's travel only (the rectifier), the deck's stroke is a slow push
south and a quick pull back, the chutes are the game's chute section flush with their faces, and every
texture is declared (and exists in the game, when $VINTAGE_STORY names one).
Run with `python3 -m unittest discover -s tools/tests -p test_shakingtable_model.py`.
"""

import importlib.util
import json
import math
import os
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
ASSETS = MOD / "assets" / "seraphhorizons"
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(MOD / "ShakingTable" / "tools"))

from machinegen import rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("shakingtable_make_shape", MOD / "ShakingTable" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((ASSETS / "config" / "shakingtable-rig.json").read_text())
SHAPE = json.loads((ASSETS / "shapes" / "block" / "shakingtable.json").read_text())
FRAME = json.loads((ASSETS / "shapes" / "block" / "shakingtable_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "ShakingTable" / "rig-reference.json").read_text())
REQUIRES = {"deck", "head", "rectifier", "chutes", "pipecopper", "pipelead", None}
TAU = 2 * math.pi


def matrix(pid, theta, travel=None):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta, "travel": travel}, None)


def turn_x(m):
    return math.atan2(m[2][1], m[1][1])


def deck_z(travel):
    return matrix("deck", travel, travel)[2][3]


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(ids, ["entry", "headshaft", "deck", "pullrod", "rectifier", "head", "chutes", "pipecopper", "pipelead", "frame"])
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            if p.get("ride"):
                self.assertIn(p["ride"], ids)
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        self.assertIsNone(rigmath.progress_of(RIG))

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreaterEqual(len(poses), 10)
        for pose in poses:
            self.assertEqual(set(pose["matrices"]), {p["id"] for p in RIG["parts"]})
            for pid, want in pose["matrices"].items():
                got = matrix(pid, pose["theta"], pose["travel"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at theta {pose['theta']}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        self.assertEqual(sorted(tuple(c["pos"]) for c in RIG["cells"]), [(x, 0, z) for x in range(2) for z in range(3)])
        for c in RIG["cells"]:
            self.assertGreater(c["lid"], 0.9)

    def test_anchors(self):
        self.assertEqual((RIG["powerCell"], RIG["powerFace"]), ([0, 0, 0], "west"))
        self.assertEqual((RIG["infeedCell"], RIG["infeedFace"]), ([0, 0, 1], "up"))
        self.assertEqual((RIG["waterCell"], RIG["waterFace"]), ([0, 0, 2], "up"))
        self.assertEqual((RIG["concentrateCell"], RIG["concentrateFace"]), ([0, 0, 2], "south"))
        self.assertEqual((RIG["middlingsCell"], RIG["middlingsFace"]), ([1, 0, 2], "south"))
        self.assertEqual((RIG["tailingsCell"], RIG["tailingsFace"]), ([1, 0, 0], "east"))
        for key in ("returnWaterCell", "work", "trunkPath"):
            self.assertNotIn(key, RIG)


class Drive(unittest.TestCase):
    def test_the_rectifier_turns_the_head_and_strokes_the_deck_the_same_either_way(self):
        for theta in (0.3, 1.7, 2.2, 5.0, 11.0):
            self.assertEqual(matrix("headshaft", theta), matrix("headshaft", -theta))
            self.assertEqual(matrix("deck", theta), matrix("deck", -theta))
            self.assertAlmostEqual(math.remainder(turn_x(matrix("headshaft", theta)) - RIG["stroke"]["strokesPerTurn"] * theta, TAU), 0.0, places=9)
            self.assertAlmostEqual(math.remainder(turn_x(matrix("entry", -theta)) + theta, TAU), 0.0, places=9)
            self.assertEqual(matrix("pullrod", theta), matrix("deck", theta))

    def test_the_stroke_is_a_slow_push_south_and_a_quick_pull_back(self):
        n = 720
        period = TAU / RIG["stroke"]["strokesPerTurn"]
        zs = [deck_z(period * i / n) for i in range(n + 1)]
        steps = [zs[i + 1] - zs[i] for i in range(n)]
        forward = sum(1 for s in steps if s > 0) / n
        changes = sum(1 for i in range(n) if (steps[i] > 0) != (steps[i - 1] > 0))
        self.assertAlmostEqual(zs[0], zs[-1], places=9)
        self.assertAlmostEqual(max(zs) - min(zs), RIG["stroke"]["length"], places=4)
        self.assertTrue(0.6 < forward < 0.8, forward)
        self.assertEqual(changes, 2)
        self.assertGreater(-min(steps), 2 * max(steps))
        self.assertEqual(RIG["stroke"]["forward"], "south")

    def test_nothing_else_moves(self):
        ident = rigmath.m4()
        for pid in ("rectifier", "head", "chutes", "pipecopper", "pipelead", "frame"):
            for theta in (0.0, 2.5, -7.0):
                m = matrix(pid, theta)
                self.assertEqual([[round(v, 12) + 0.0 for v in row] for row in m], ident, pid)


class Chutes(unittest.TestCase):
    def walls(self, short):
        return [e for e in SHAPE["elements"] if e["name"].startswith(f"ch_{short}_wall")]

    def test_each_chute_is_the_games_section_flush_with_its_face(self):
        faces = {"up": (1, 1), "south": (2, 1), "east": (0, 1)}
        cases = [("in", RIG["infeedCell"], RIG["infeedFace"])] + [(n[:4], RIG[f"{n}Cell"], RIG[f"{n}Face"]) for n in ("concentrate", "middlings", "tailings")]
        for short, cell, face in cases:
            axis, _ = faces[face]
            across = [k for k in range(3) if k != axis]
            walls = self.walls(short)
            self.assertEqual(len(walls), 4, short)
            prof = sorted((round(w["from"][across[0]] - cell[across[0]] * 16, 4), round(w["to"][across[0]] - cell[across[0]] * 16, 4),
                           round(w["from"][across[1]] - cell[across[1]] * 16, 4), round(w["to"][across[1]] - cell[across[1]] * 16, 4)) for w in walls)
            self.assertEqual(prof, make_shape.chute_profile(axis), short)
            self.assertEqual({w["to"][axis] for w in walls}, {(cell[axis] + 1) * 16.0}, short)
            self.assertEqual({f["texture"] for w in walls for f in w["faces"].values()}, {"#chute"})
        self.assertEqual(SHAPE["textures"]["chute"], make_shape.VANILLA_CHUTE["texture"])

    def test_vanilla_chute_is_the_games(self):
        game = make_shape.read_vanilla_chute()
        if game is None:
            raise unittest.SkipTest("the game is not here ($VINTAGE_STORY or ~/Games/vintagestory)")
        walls, runs, tex = game
        self.assertEqual(walls, sorted(make_shape.VANILLA_CHUTE["walls"]))
        self.assertEqual(runs, {(0.0, 16.0)})
        self.assertEqual(tex, make_shape.VANILLA_CHUTE["texture"])


class Textures(unittest.TestCase):
    def test_every_face_uses_a_declared_code_and_the_frame_matches(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertEqual(used, set(shape["textures"]))
        for code, path in FRAME["textures"].items():
            self.assertEqual(SHAPE["textures"][code], path, code)

    def test_the_water_line_is_the_same_in_each_metal(self):
        def line(pid):
            return sorted((e["name"][len(pid):], e["from"], e["to"]) for e in SHAPE["elements"] if e["name"].startswith(pid + "_"))
        self.assertEqual(line("pipecopper"), line("pipelead"))
        self.assertTrue(line("pipecopper"))

    def test_every_game_texture_exists_in_the_game(self):
        install = os.environ.get("VINTAGE_STORY")
        if not install:
            raise unittest.SkipTest("VINTAGE_STORY is not set to a game or server install")
        assets = Path(install) / "assets"
        for code, path in SHAPE["textures"].items():
            domain, rel = path.split(":", 1)
            self.assertEqual(domain, "game", code)
            self.assertTrue((assets / "survival" / "textures" / f"{rel}.png").is_file(), f"{code}: {path}")


if __name__ == "__main__":
    unittest.main()
