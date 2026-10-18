"""The amalgamation plates' generated model files (mods-src/seraphhorizons/AmalgamationPlates/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the
shared rig maths, uses its `requires` vocabulary and has no driver, no power and no work (the plates take no
power: nothing in them moves); the cells are rebuilt from the shipped shape; the anchors are the head cell's
top face (the infeed) and west face (the water) and the foot cell's end face (the outlet); the chute sections
are vanilla's; the water line's inlet meets a ppex pipe end to end and narrows through a soldered reducer to
the vanner's spray pipe; the bed falls 1 in 10; the amalgam is the amalgam
pan's; every texture is declared and exists. Run with `python3 -m unittest discover -s tools/tests`.
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
sys.path.insert(0, str(MOD / "AmalgamationPlates" / "tools"))

from machinegen import rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("amalgamationplates_make_shape", MOD / "AmalgamationPlates" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((ASSETS / "config" / "amalgamationplates-rig.json").read_text())
SHAPE = json.loads((ASSETS / "shapes" / "block" / "amalgamationplates.json").read_text())
FRAME = json.loads((ASSETS / "shapes" / "block" / "amalgamationplates_frame.json").read_text())
PAN = json.loads((ASSETS / "shapes" / "block" / "amalgampan.json").read_text())
REQUIRES = {"chutes", "plates", "dressing", "amalgam1", "amalgam2", "amalgam3", "trap", "pipecopper", "pipelead", None}


def elements(prefix):
    return [e for e in SHAPE["elements"] if e["name"].startswith(prefix)]


def span(els, axis):
    return (min(min(e["from"][axis], e["to"][axis]) for e in els), max(max(e["from"][axis], e["to"][axis]) for e in els))


class Rig(unittest.TestCase):
    def test_parts_parse_and_nothing_moves(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(ids, ["amalgam1", "amalgam2", "amalgam3", "dressing", "plates", "trap", "chutes", "pipecopper", "pipelead", "frame"])
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            self.assertEqual((p["drivers"], p["ride"]), ([], None), p["id"])
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        self.assertIsNone(rigmath.progress_of(RIG))
        for key in ("powerCell", "powerFace", "work", "trunkPath"):
            self.assertNotIn(key, RIG)
        ident = rigmath.m4()
        for p in RIG["parts"]:
            m = rigmath.part_matrix(RIG["parts"], p["id"], {"theta": 1.3}, None)
            self.assertEqual([[round(v, 12) + 0.0 for v in row] for row in m], ident, p["id"])

    def test_build_stages_and_states(self):
        self.assertEqual(RIG["buildStages"], [{"stage": 1, "name": "the table and plates", "adds": ["plates"]},
                                              {"stage": 2, "name": "the mercury trap", "adds": ["trap"]},
                                              {"stage": 3, "name": "services", "adds": ["chutes", "pipecopper", "pipelead"]}])
        self.assertNotIn("dressing", [r for st in RIG["buildStages"] for r in st["adds"]])   # recurring, not built
        self.assertEqual(RIG["amalgamStates"], ["amalgam1", "amalgam2", "amalgam3"])
        self.assertEqual(RIG["pipes"]["requires"], {"copper": "pipecopper", "lead": "pipelead"})

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        self.assertEqual([c["pos"] for c in RIG["cells"]], [[0, 0, 0], [0, 0, 1], [0, 0, 2]])
        for c in RIG["cells"]:
            self.assertTrue(c["boxes"], c["pos"])
            self.assertGreater(c["lid"], 0.9)

    def test_anchors(self):
        self.assertEqual((RIG["infeedCell"], RIG["infeedFace"]), ([0, 0, 0], "up"))
        self.assertEqual((RIG["waterCell"], RIG["waterFace"]), ([0, 0, 0], "west"))
        self.assertEqual((RIG["outputCell"], RIG["outputFace"]), ([0, 0, 2], "south"))
        for key in ("feedSpout", "spray", "amalgam", "trapWell"):
            pos = RIG[key]["pos"]
            self.assertTrue(0 <= pos[0] <= 1 and 0 <= pos[1] <= 1 and 0 <= pos[2] <= 3, key)


class Anchors(unittest.TestCase):
    def test_the_inlet_meets_a_ppex_pipe_on_the_water_faces_centre(self):
        for metal in ("pipecopper", "pipelead"):
            tube = elements(f"{metal}_inlet_")
            self.assertEqual(len(tube), 4)
            (x0, _), (y0, y1), (z0, z1) = span(tube, 0), span(tube, 1), span(tube, 2)
            self.assertEqual(x0, 0.0)
            self.assertEqual((y0, y1, z0, z1), (5.0, 11.0, 5.0, 11.0))     # ppex's 6 x 6 tube, its block's 5..11

    def test_the_inlet_narrows_through_a_soldered_reducer_to_the_vanners_spray_pipe(self):
        for metal in ("pipecopper", "pipelead"):
            _, ix1 = span(elements(f"{metal}_inlet_"), 0)
            spray = [e for e in elements(f"{metal}_spray_") if not e["name"].endswith("_cap")]
            (sx0, sx1), (sy0, sy1), (sz0, sz1) = span(spray, 0), span(spray, 1), span(spray, 2)
            self.assertAlmostEqual(sy1 - sy0, 2.4)                         # the vanner's spray pipe, 2.4 across
            self.assertAlmostEqual(sz1 - sz0, 2.4)
            self.assertEqual(sx0, ix1)                                     # it leaves the inlet's plugged end
            self.assertTrue(elements(f"{metal}_plug_"))
            ring = elements(f"{metal}_reducer_")
            self.assertEqual(len(ring), 4)
            self.assertEqual({f["texture"] for e in ring for f in e["faces"].values()}, {"#solder"})
            (cap,) = elements(f"{metal}_spray_cap")
            self.assertEqual(max(cap["from"][0], cap["to"][0]), sx1)
            self.assertEqual({f["texture"] for f in cap["faces"].values()}, {"#solder"})

    def test_the_chute_sections_are_vanillas(self):
        walls = sorted(tuple(w) for w in make_shape.VANILLA_CHUTE["walls"])
        infeed = sorted((min(e["from"][0], e["to"][0]), max(e["from"][0], e["to"][0]), min(e["from"][2], e["to"][2]),
                         max(e["from"][2], e["to"][2])) for e in elements("ch_in_"))
        self.assertEqual(infeed, sorted((16.0 - a1, 16.0 - a0, b0, b1) for a0, a1, b0, b1 in walls))
        self.assertEqual(span(elements("ch_in_"), 1)[1], 16.0)                  # flush with the top face
        outlet = sorted((e["from"][1], e["to"][1], e["from"][0], e["to"][0]) for e in elements("ch_out_"))
        self.assertEqual(outlet, walls)
        self.assertEqual(span(elements("ch_out_"), 2)[1], 48.0)                 # flush with the end face
        self.assertEqual(SHAPE["textures"]["chute"], "game:block/metal/sheet/copper1")

    def test_the_bed_falls_one_in_ten(self):
        floors = elements("fr_bed_floor")
        self.assertEqual(len(floors), 2)
        for e in floors:
            self.assertAlmostEqual(e["rotationX"], math.degrees(math.atan(0.1)), places=3)


class Amalgam(unittest.TestCase):
    def test_each_state_is_its_own_requires_and_the_amalgam_pans_texture(self):
        for n in (1, 2, 3):
            els = elements(f"am{n}_")
            self.assertTrue(els)
            self.assertEqual({rigmath.part_of(RIG["parts"], e["name"]) for e in els}, {f"amalgam{n}"})
            self.assertEqual({f["texture"] for e in els for f in e["faces"].values()}, {"#amalgam"})
        self.assertEqual(SHAPE["textures"]["amalgam"], PAN["textures"]["amalgam"])
        self.assertEqual(SHAPE["textures"]["mercury"], PAN["textures"]["mercury"])

    def test_the_states_grow_down_the_plates(self):
        reach = [span(elements(f"am{n}_"), 2)[1] for n in (1, 2, 3)]
        self.assertEqual(reach, sorted(reach))
        self.assertEqual(len(set(reach)), 3)


class Textures(unittest.TestCase):
    def test_every_face_uses_a_declared_code_and_the_frame_matches(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertEqual(used, set(shape["textures"]))
        for code, path in FRAME["textures"].items():
            self.assertEqual(SHAPE["textures"][code], path, code)

    def test_the_mods_own_textures_exist(self):
        for code, path in SHAPE["textures"].items():
            domain, rel = path.split(":", 1)
            if domain == "seraphhorizons":
                self.assertTrue((ASSETS / "textures" / f"{rel}.png").is_file(), f"{code}: {path}")

    def test_every_game_texture_exists_in_the_game(self):
        install = os.environ.get("VINTAGE_STORY")
        if not install:
            raise unittest.SkipTest("VINTAGE_STORY is not set to a game or server install")
        assets = Path(install) / "assets"
        for code, path in SHAPE["textures"].items():
            domain, rel = path.split(":", 1)
            if domain == "game":
                self.assertTrue((assets / "survival" / "textures" / f"{rel}.png").is_file(), f"{code}: {path}")


if __name__ == "__main__":
    unittest.main()
