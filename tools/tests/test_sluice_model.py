"""The sluice's generated model files (mods-src/seraphhorizons/Sluice/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with
the shared rig maths and uses its `requires` vocabulary, nothing in it moves (no driver, no ride, no
work, no power cell), the cells are rebuilt from the shipped shape, the three chute sections (in, tailings
and concentrate) are vanilla's straight chute flush with their faces, the water inlet meets a ppex pipe on the
water face's centre and comes up through the head box's floor, the floor falls 1 in 12 from the head box's
sill to the tailings chute, the concentrate is drawn off through a slot behind each riffle into the launder
under the floor and on to the concentrate box and its chute, and every texture is declared (and exists,
where the game is installed). Run with `python3 -m unittest discover -s tools/tests`.
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
REQUIRES = {"riffles", "concentrate", "infeedchute", "tailingschute", "concentratechute", "pipecopper", "pipelead", None}
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
        self.assertEqual(ids, ["riffles", "concentrate", "infeedchute", "tailingschute", "concentratechute", "pipecopper",
                               "pipelead", "frame"])
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            self.assertEqual((p["drivers"], p["ride"]), ([], None), p["id"])
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        self.assertIsNone(rigmath.progress_of(RIG))
        for key in ("powerCell", "powerFace", "work", "trunkPath", "fill"):
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
        self.assertEqual((RIG["concentrateCell"], RIG["concentrateFace"]), ([0, 0, 2], "west"))
        for key in ("feedSpout", "waterSpout", "concentrateSpout"):
            p = RIG[key]["pos"]
            self.assertTrue(0 <= p[0] <= 1 and 0 <= p[1] <= 1 and 0 <= p[2] <= 3, key)


class Chutes(unittest.TestCase):
    def section(self, pid, a, b, off=(0.0, 0.0)):
        """The walls' rectangles across the run (over axes a and b, less a cell's offset), sorted."""
        els = of(pid)
        self.assertEqual(len(els), 4, pid)
        rects = sorted(tuple(round(v, 4) for v in (e.aabb()[0][a] - off[0], e.aabb()[1][a] - off[0], e.aabb()[0][b] - off[1],
                                                   e.aabb()[1][b] - off[1])) for e in els)
        return rects, els

    def test_the_infeed_is_vanillas_chute_upright_flush_with_the_heads_top(self):
        rects, els = self.section("infeedchute", 0, 2)
        self.assertEqual(rects, make_shape.vanilla_profile("y"))
        self.assertEqual({round(e.aabb()[1][1], 4) for e in els}, {16.0})

    def test_the_tailings_chute_is_vanillas_chute_flush_with_the_tails_end(self):
        rects, els = self.section("tailingschute", 0, 1)
        self.assertEqual(rects, make_shape.vanilla_profile("z"))
        self.assertEqual({round(e.aabb()[1][2], 4) for e in els}, {48.0})

    def test_the_concentrate_chute_is_vanillas_chute_flush_with_the_tail_cells_west_face(self):
        rects, els = self.section("concentratechute", 1, 2, (0.0, 32.0))
        self.assertEqual(rects, make_shape.vanilla_profile("x"))
        self.assertEqual({round(e.aabb()[0][0], 4) for e in els}, {0.0})

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
            self.assertEqual([round(v, 4) for v in hi[:2]], [11.0, 11.0], pid)
            # up through the head box's floor, the nozzle on it under the sill
            rlo, rhi = aabb([e for e in of(pid) if "_riser_" in e.name])
            self.assertEqual(round(rhi[1], 4), make_shape.HB_FLOOR, pid)
            nlo, nhi = aabb([e for e in of(pid) if e.name.endswith(("_reducer", "_nozzle1", "_nozzle2"))])
            self.assertEqual(round(nlo[1], 4), make_shape.HB_FLOOR, pid)
            self.assertLess(nhi[1], make_shape.SL_HEAD_Y, pid)

    def test_both_metals_are_the_same_elements(self):
        copper = sorted((e["name"][len("pipecopper"):], e["from"], e["to"]) for e in SHAPE["elements"] if e["name"].startswith("pipecopper_"))
        lead = sorted((e["name"][len("pipelead"):], e["from"], e["to"]) for e in SHAPE["elements"] if e["name"].startswith("pipelead_"))
        self.assertEqual(copper, lead)


class Sluice(unittest.TestCase):
    def test_the_floor_falls_1_in_12_from_the_sill_to_the_chute(self):
        floors = [e for e in FLAT if e.name.startswith("fr_sl_floor")]
        self.assertEqual(len(floors), 7)            # in lengths between the six draw slots
        for e in floors:
            # the floor's local up, turned about x: its grade is its tilt
            self.assertAlmostEqual(e.r[2][1] / e.r[1][1], 1 / 12, places=5)   # the written angle is rounded to 1e-4 degrees
        sill = next(e for e in FLAT if e.name == "fr_hb_sill")
        top = max(c[1] for e in floors for c in e.corners())
        self.assertAlmostEqual(sill.aabb()[1][1], top, places=4)
        lip = make_shape.sl_floor(make_shape.SL_Z[1])
        self.assertTrue(make_shape.CHUTE_BORE[0] < lip < make_shape.CHUTE_BORE[1], lip)     # in the tailings chute's mouth

    def test_seven_riffles_and_the_concentrate_behind_them(self):
        bars = sorted((e for e in FLAT if e.name.startswith("riffle_") and e.name.endswith("_bar")), key=lambda b: b.c[2])
        self.assertEqual(len(bars), 7)
        zs = [e.c[2] for e in bars]
        steps = {round(b - a, 3) for a, b in zip(zs, zs[1:])}
        self.assertEqual(len(steps), 1)
        beds = [e for e in of("concentrate") if "_pocket" in e.name]
        self.assertEqual(len(beds), 12)
        for e in beds:
            i = int(e.name.split("pocket")[1][0]) - 1
            # each bed starts just inside the downstream face of the riffle upstream of it
            self.assertGreater(e.aabb()[0][2], bars[i].aabb()[0][2], e.name)
            self.assertLess(e.aabb()[1][2], bars[i + 1].aabb()[1][2], e.name)


class ConcentrateOut(unittest.TestCase):
    """The concentrate leaves by chute, continuously: a draw slot behind each of riffles 1..6, the launder
    under the floor, the concentrate box under the tail, open into the concentrate chute."""

    def test_a_slot_through_the_floor_behind_each_riffle_over_the_launder(self):
        floors = sorted((e for e in FLAT if e.name.startswith("fr_sl_floor")), key=lambda e: e.c[2])
        launder = [e for e in FLAT if e.name.startswith("fr_ld_floor")]
        ld_lo, ld_hi = aabb(launder)
        for i, (a, b) in enumerate(zip(floors, floors[1:])):
            # the top face's corners: the four highest over the slope (y cos + z sin)
            up = lambda e: sorted(e.corners(), key=lambda c: c[1] * math.cos(make_shape.THETA) + c[2] * math.sin(make_shape.THETA))[4:]  # noqa: E731
            top_a = max(up(a), key=lambda c: c[2])                     # the downstream top corner of the length before the slot
            top_b = min(up(b), key=lambda c: c[2])                     # the upstream top corner of the one after it
            self.assertAlmostEqual(top_b[2] - top_a[2], make_shape.SLOT * math.cos(make_shape.THETA), places=3)
            # the slot starts at riffle i + 1's downstream face, and lies over the launder
            self.assertAlmostEqual(top_a[2], make_shape.riffle_zs()[i][1], places=3)
            self.assertTrue(ld_lo[2] < top_a[2] and top_b[2] < ld_hi[2], i)

    def test_the_launder_falls_with_the_sluice_into_the_box(self):
        launder = [e for e in FLAT if e.name.startswith("fr_ld_floor")]
        for e in launder:
            self.assertAlmostEqual(e.r[2][1] / e.r[1][1], 1 / 12, places=5)
        lip = make_shape.launder_lip()
        box = next(e for e in FLAT if e.name == "fr_cb_floor").aabb()
        self.assertTrue(box[0][2] < lip[2] < box[1][2] and box[0][0] < lip[0] < box[1][0])
        self.assertGreater(lip[1], box[1][1])
        self.assertEqual([round(c * 16, 2) for c in RIG["concentrateSpout"]["pos"]], [round(c, 2) for c in lip])

    def test_the_box_is_the_concentrate_chutes_bore_open_into_it(self):
        box = next(e for e in FLAT if e.name == "fr_cb_floor").aabb()
        bottom = next(e for e in of("concentratechute") if e.name.endswith("_bottom")).aabb()
        self.assertEqual(round(box[1][1], 4), round(bottom[1][1], 4))         # the bore floor, 5
        self.assertEqual((round(box[0][2], 4), round(box[1][2], 4)), (round(bottom[0][2], 4), round(bottom[1][2], 4)))
        self.assertEqual(round(box[0][0], 4), round(bottom[1][0], 4))         # flush, end to end
        # nothing of the frame across the box's open (west) side, over the bore
        west = [e.name for e in FLAT if e.name.startswith("fr_cb_") and e.aabb()[0][0] < 5.0 - 1e-6
                and e.aabb()[1][2] > box[0][2] + 1e-6 and e.aabb()[0][2] < box[1][2] - 1e-6 and e.aabb()[1][1] > box[1][1] + 1e-6]
        self.assertEqual(west, [])

    def test_the_two_outputs_are_apart(self):
        self.assertNotEqual((RIG["tailingsCell"], RIG["tailingsFace"]), (RIG["concentrateCell"], RIG["concentrateFace"]))
        tail = aabb(of("tailingschute"))
        conc = aabb(of("concentratechute"))
        self.assertTrue(conc[1][0] <= tail[0][0] or conc[1][2] <= tail[0][2])


class Textures(unittest.TestCase):
    def test_every_face_uses_a_declared_code_and_the_frame_matches(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertEqual(used, set(shape["textures"]))
        for code, path in FRAME["textures"].items():
            self.assertEqual(SHAPE["textures"][code], path, code)

    def test_the_concentrate_has_its_own_code(self):
        els = [e for e in SHAPE["elements"] if e["name"].startswith("conc_")]
        self.assertTrue(els)
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
