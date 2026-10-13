"""The sluice's generated model files (mods-src/seraphhorizons/Sluice/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with
the shared rig maths and uses its `requires` vocabulary, nothing in it moves (no driver, no ride, no
work, no power cell), the cells are rebuilt from the shipped shape, both chute sections are vanilla's
straight chute flush with their faces, the water inlet meets a ppex pipe on the water face's centre, the
floor falls 1 in 12 from the head box's sill to the tailings chute, the concentrate's three states lie
behind the riffles and fill from the head down, and every texture is declared (and exists, where the
game is installed). Run with `python3 -m unittest discover -s tools/tests`.
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
sys.path.insert(0, str(MOD / "Sluice" / "tools"))

from machinegen import rigmath  # noqa: E402
from machinegen.geometry import flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("sluice_make_shape", MOD / "Sluice" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((ASSETS / "config" / "sluice-rig.json").read_text())
SHAPE = json.loads((ASSETS / "shapes" / "block" / "sluice.json").read_text())
FRAME = json.loads((ASSETS / "shapes" / "block" / "sluice_frame.json").read_text())
REQUIRES = {"riffles", "concentrate1", "concentrate2", "concentrate3", "infeedchute", "tailingschute", "pipecopper", "pipelead",
            None}
FLAT = flatten(SHAPE["elements"], textures={})


def part(name):
    return rigmath.part_of(RIG["parts"], name)


def of(pid):
    return [e for e in FLAT if part(e.name) == pid]


def aabb(els):
    boxes = [e.aabb() for e in els]
    return [min(b[0][k] for b in boxes) for k in range(3)], [max(b[1][k] for b in boxes) for k in range(3)]


class Rig(unittest.TestCase):
    def test_parts_parse_use_the_vocabulary_and_nothing_moves(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(ids, ["riffles", "concentrate1", "concentrate2", "concentrate3", "infeedchute", "tailingschute",
                               "pipecopper", "pipelead", "frame"])
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            self.assertEqual((p["drivers"], p["ride"]), ([], None), p["id"])
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        self.assertIsNone(rigmath.progress_of(RIG))
        for key in ("powerCell", "powerFace", "work", "trunkPath"):
            self.assertNotIn(key, RIG)
        ident = rigmath.m4()
        for p in RIG["parts"]:
            for inputs in ({"theta": 0.0}, {"theta": 4.2, "depth": 0.7, "work": 2.0, "size": 2, "presence": 1.0}):
                self.assertEqual(rigmath.part_matrix(RIG["parts"], p["id"], inputs), ident, p["id"])

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        self.assertEqual({part(n) for n in names}, {p["id"] for p in RIG["parts"]})
        self.assertEqual({e["name"] for e in FRAME["elements"]}, {n for n in names if part(n) == "frame"})

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        self.assertEqual([c["pos"] for c in RIG["cells"]], [[0, 0, 0], [0, 0, 1], [0, 0, 2]])
        for c in RIG["cells"]:
            self.assertTrue(c["boxes"], c["pos"])
            self.assertNotIn("lid", c)

    def test_anchors(self):
        self.assertEqual((RIG["infeedCell"], RIG["infeedFace"]), ([0, 0, 0], "up"))
        self.assertEqual((RIG["waterCell"], RIG["waterFace"]), ([0, 0, 0], "north"))
        self.assertEqual((RIG["tailingsCell"], RIG["tailingsFace"]), ([0, 0, 2], "south"))
        self.assertEqual(RIG["fill"]["requires"], ["concentrate1", "concentrate2", "concentrate3"])
        for key in ("feedSpout", "waterSpout", "concentrate"):
            p = RIG[key]["pos"]
            self.assertTrue(0 <= p[0] <= 1 and 0 <= p[1] <= 1 and 0 <= p[2] <= 3, key)


class Chutes(unittest.TestCase):
    def section(self, pid, across):
        """The walls' rectangles across the run, cell-local, sorted, and their ends along it."""
        els = of(pid)
        self.assertEqual(len(els), 4, pid)
        rects = sorted(tuple(round(v, 4) for v in (e.aabb()[0][0], e.aabb()[1][0], e.aabb()[0][across], e.aabb()[1][across]))
                       for e in els)
        return rects, els

    def test_the_infeed_is_vanillas_chute_upright_flush_with_the_heads_top(self):
        rects, els = self.section("infeedchute", 2)
        self.assertEqual(rects, make_shape.vanilla_profile("y"))
        self.assertEqual({round(e.aabb()[1][1], 4) for e in els}, {16.0})

    def test_the_tailings_chute_is_vanillas_chute_flush_with_the_tails_end(self):
        rects, els = self.section("tailingschute", 1)
        self.assertEqual(rects, make_shape.vanilla_profile("z"))
        self.assertEqual({round(e.aabb()[1][2], 4) for e in els}, {48.0})

    def test_the_profile_is_an_8_by_8_tube_one_thick_centred_in_the_block(self):
        walls = make_shape.VANILLA_CHUTE["walls"]
        lo = min(min(w[0], w[2]) for w in walls)
        hi = max(max(w[1], w[3]) for w in walls)
        self.assertEqual((lo, hi), (4.0, 12.0))
        self.assertEqual(sum((w[1] - w[0]) * (w[3] - w[2]) for w in walls), 8 * 8 - 6 * 6)
        self.assertEqual(SHAPE["textures"]["chute"], make_shape.VANILLA_CHUTE["texture"])

    def test_vanilla_chute_is_the_games(self):
        install = os.environ.get("VINTAGE_STORY")
        if not install:
            raise unittest.SkipTest("VINTAGE_STORY is not set to a game or server install")
        f = Path(install) / "assets" / "survival" / make_shape.VANILLA_CHUTE["shape"]
        shape = json.loads(f.read_text())
        walls = sorted((e["from"][1], e["to"][1], e["from"][2], e["to"][2]) for e in shape["elements"])
        self.assertEqual(walls, sorted(make_shape.VANILLA_CHUTE["walls"]))


class Water(unittest.TestCase):
    def test_the_inlet_meets_a_ppex_pipe_on_the_water_faces_centre(self):
        for pid in ("pipecopper", "pipelead"):
            lo, hi = aabb([e for e in of(pid) if "_inlet_" in e.name])
            self.assertEqual([round(v, 4) for v in lo], [5.0, 5.0, 0.0], pid)
            self.assertEqual([round(v, 4) for v in hi], [11.0, 11.0, 4.0], pid)

    def test_both_metals_are_the_same_elements(self):
        copper = sorted((e["name"][len("pipecopper"):], e["from"], e["to"]) for e in SHAPE["elements"] if e["name"].startswith("pipecopper_"))
        lead = sorted((e["name"][len("pipelead"):], e["from"], e["to"]) for e in SHAPE["elements"] if e["name"].startswith("pipelead_"))
        self.assertEqual(copper, lead)


class Sluice(unittest.TestCase):
    def test_the_floor_falls_1_in_12_from_the_sill_to_the_chute(self):
        floors = [e for e in FLAT if e.name.startswith("fr_sl_floor")]
        self.assertEqual(len(floors), 2)
        for e in floors:
            # the floor's local up, turned about x: its grade is its tilt
            self.assertAlmostEqual(e.r[2][1] / e.r[1][1], 1 / 12, places=5)   # the written angle is rounded to 1e-4 degrees
        sill = next(e for e in FLAT if e.name == "fr_hb_sill")
        top = max(c[1] for e in floors for c in e.corners())
        self.assertAlmostEqual(sill.aabb()[1][1], top, places=4)
        self.assertAlmostEqual(make_shape.sl_floor(make_shape.SL_Z[1]), 5.5, places=9)

    def test_seven_riffles_and_the_concentrate_behind_them(self):
        bars = [e for e in FLAT if e.name.startswith("riffle_") and e.name.endswith("_bar")]
        self.assertEqual(len(bars), 7)
        zs = sorted(e.c[2] for e in bars)
        steps = {round(b - a, 3) for a, b in zip(zs, zs[1:])}
        self.assertEqual(len(steps), 1)
        for k in (1, 2, 3):
            for e in of(f"concentrate{k}"):
                i = int(e.name.split("pocket")[1][0]) - 1
                # each deposit starts just inside the downstream face of the riffle upstream of it
                upstream = sorted(bars, key=lambda b: b.c[2])[i]
                self.assertGreater(e.aabb()[0][2], upstream.aabb()[0][2], e.name)
                self.assertLess(e.aabb()[1][2], sorted(bars, key=lambda b: b.c[2])[i + 1].aabb()[1][2], e.name)

    def test_the_states_fill_from_the_head_down_and_each_holds_more(self):
        fill = make_shape.FILL
        for k in (1, 2, 3):
            self.assertEqual(list(fill[k]), sorted(fill[k], reverse=True), k)
        for a, b in zip(fill[1], fill[2]):
            self.assertLessEqual(a, b)
        for a, b in zip(fill[2], fill[3]):
            self.assertLessEqual(a, b)
        self.assertEqual(set(fill[3]), {1.0})


class Textures(unittest.TestCase):
    def test_every_face_uses_a_declared_code_and_the_frame_matches(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertEqual(used, set(shape["textures"]))
        for code, path in FRAME["textures"].items():
            self.assertEqual(SHAPE["textures"][code], path, code)

    def test_the_concentrate_has_its_own_code(self):
        for k in (1, 2, 3):
            els = [e for e in SHAPE["elements"] if e["name"].startswith(f"conc{k}_")]
            self.assertEqual({f["texture"] for e in els for f in e["faces"].values()}, {"#concentrate"})

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
