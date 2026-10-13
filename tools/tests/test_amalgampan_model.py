"""The amalgam pan's generated model files (mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with
the shared rig maths and uses its `requires` vocabulary (#718's fitted stages, the muller and then its
shoes, and the three contents), it takes a vanilla axle from below at its own cell, as the quern does, its
reference poses are its own maths, the cell is rebuilt from the shipped shape, theta (the spindle's angle)
turns the muller about the pan's axis once a turn, the shoes ride it and nothing else moves, the spindle's
foot is the axle's cross profile at the block's bottom face, the contents lie on the pan's floor, the
shoes alone wear the `shoe` code, every texture is declared and exists, and the mercury is drawn as
Expanded Matter draws it and the slurry texture is the generator's. Run with
`python3 -m unittest discover -s tools/tests`.
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
sys.path.insert(0, str(MOD / "AmalgamPan" / "tools"))

from machinegen import rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("amalgampan_make_shape", MOD / "AmalgamPan" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)
import slurry_texture  # noqa: E402

RIG = json.loads((ASSETS / "config" / "amalgampan-rig.json").read_text())
SHAPE = json.loads((ASSETS / "shapes" / "block" / "amalgampan.json").read_text())
FRAME = json.loads((ASSETS / "shapes" / "block" / "amalgampan_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "AmalgamPan" / "rig-reference.json").read_text())
SLURRY = ASSETS / "textures" / "block" / "liquid" / "slurry.png"
# #718's fitted stages (the muller, a rod; its shoes, a plate) and the contents (a mercury pool, the charge's slurry, amalgam)
REQUIRES = {"muller", "shoes", "mercury", "slurry", "amalgam", None}


def matrix(pid, theta):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta}, None)


def turn_y(m):
    return math.atan2(-m[2][0], m[0][0])


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(ids, ["muller", "shoes", "mercury", "slurry", "amalgam", "frame"])
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            if p.get("ride"):
                self.assertIn(p["ride"], ids)
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        self.assertEqual(rigmath.progress_of(RIG), None)          # no work: nothing is posed by one
        for key in ("work", "trunkPath"):
            self.assertNotIn(key, RIG)

    def test_it_takes_a_vanilla_axle_from_below_at_its_own_cell(self):
        # as the quern does (BlockQuern.HasMechPowerConnectorAt: up or down; the pan's is the down face)
        self.assertEqual(RIG["powerCell"], [0, 0, 0])
        self.assertEqual(RIG["powerFace"], "down")

    def test_it_takes_no_automated_input_or_output(self):
        # the player loads it and takes the amalgam out by hand: no anchor for a chute, hopper or chest
        sides = [k for k in RIG if k.endswith("Side")]
        self.assertEqual(sides, ["operatorSide"])
        self.assertEqual([k for k in RIG if k.startswith(("infeed", "output", "outfeed"))], [])

    def test_the_fitted_stages_are_the_muller_then_its_shoes(self):
        # #718: the muller from a rod (the rod is the spindle), then the shoes from a plate, bolted under its arms
        parts = {p["id"]: p for p in RIG["parts"]}
        self.assertEqual([p["requires"] for p in RIG["parts"] if p["requires"] in ("muller", "shoes")], ["muller", "shoes"])
        self.assertEqual((parts["muller"]["requires"], parts["muller"]["ride"]), ("muller", None))
        self.assertEqual([d["type"] for d in parts["muller"]["drivers"]], ["rotate"])
        self.assertEqual((parts["shoes"]["requires"], parts["shoes"]["ride"], parts["shoes"]["drivers"]), ("shoes", "muller", []))
        self.assertEqual(parts["frame"]["requires"], None)

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
                got = matrix(pid, pose["theta"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at theta {pose['theta']}")

    def test_cell_is_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        (cell,) = RIG["cells"]
        self.assertEqual(cell["pos"], [0, 0, 0])
        self.assertEqual(cell["lid"], max(b[4] for b in cell["boxes"]))

    def test_anchors(self):
        self.assertEqual(RIG["operatorSide"], "north")
        self.assertEqual(RIG["pan"]["pos"], [0.5, make_shape.FLOOR_TOP / 16, 0.5])


class Turning(unittest.TestCase):
    def test_the_muller_turns_with_the_axle_once_a_turn_about_the_pans_axis(self):
        # the game's quern draws its stone at the network's angle: ratio 1
        for theta in (0.3, 1.7, -2.2, 5.0, 11.0):
            m = matrix("muller", theta)
            self.assertAlmostEqual(math.remainder(turn_y(m) - theta, 2 * math.pi), 0.0, places=9)
            axis = rigmath.apply(m, [0.5, 0.7, 0.5])
            for got, want in zip(axis, [0.5, 0.7, 0.5]):
                self.assertAlmostEqual(got, want, places=12)
            # the shoes ride it, bolted under its arms
            self.assertEqual(matrix("shoes", theta), m)

    def test_nothing_else_moves(self):
        ident = rigmath.m4()
        for pid in ("mercury", "slurry", "amalgam", "frame"):
            for theta in (0.0, 2.5, -7.0):
                m = matrix(pid, theta)
                self.assertEqual([[round(v, 12) + 0.0 for v in row] for row in m], ident, pid)

    def test_the_spindles_foot_meets_the_axle_below_in_its_own_profile(self):
        # the vanilla axle's oak cross profile, on the pan's axis, from the block's bottom face
        bars = [e for e in SHAPE["elements"] if e["name"].startswith("muller_coupling")]
        self.assertEqual(len(bars), 2)
        # one bar's foot may be set in a hair by the z-fighting fix, where the two cross
        self.assertEqual(min(min(e["from"][1], e["to"][1]) for e in bars), 0.0)
        for e in bars:
            self.assertLessEqual(min(e["from"][1], e["to"][1]), 0.02)
            self.assertAlmostEqual((e["from"][0] + e["to"][0]) / 2, 8.0, places=4)
            self.assertAlmostEqual((e["from"][2] + e["to"][2]) / 2, 8.0, places=4)
            self.assertEqual({f["texture"] for f in e["faces"].values()}, {"#oak"})
        self.assertEqual(sorted(sorted([round(abs(e["to"][0] - e["from"][0]), 4), round(abs(e["to"][2] - e["from"][2]), 4)]) for e in bars),
                         [[1.2, 3.0], [1.2, 3.0]])


class Contents(unittest.TestCase):
    def layer(self, pid):
        els = [e for e in SHAPE["elements"] if rigmath.part_of(RIG["parts"], e["name"]) == pid]
        self.assertEqual(len(els), 8, pid)
        return min(min(e["from"][1], e["to"][1]) for e in els), max(max(e["from"][1], e["to"][1]) for e in els)

    def test_the_contents_are_flat_layers_on_the_floor_the_charge_deepest(self):
        floor = make_shape.FLOOR_TOP
        m0, m1 = self.layer("mercury")
        s0, s1 = self.layer("slurry")
        a0, a1 = self.layer("amalgam")
        for bottom in (m0, s0, a0):
            self.assertLess(bottom, floor)
        self.assertAlmostEqual(m1, make_shape.MERCURY_TOP, places=4)
        self.assertAlmostEqual(s1, make_shape.SLURRY_TOP, places=4)
        self.assertAlmostEqual(a1, make_shape.AMALGAM_TOP, places=4)
        # the mercury a shallow pool, the amalgam paste deeper, the charge the deepest: a loaded pan
        self.assertLess(floor, m1)
        self.assertLess(m1, a1)
        self.assertLess(a1, s1)
        # the shoes stand out of all three
        shoes = [e for e in SHAPE["elements"] if e["name"].startswith("shoe_")]
        self.assertEqual(len(shoes), 4)
        self.assertGreater(min(max(e["from"][1], e["to"][1]) for e in shoes), s1)

    def test_each_content_is_its_own_requires_and_texture(self):
        for pid in ("mercury", "slurry", "amalgam"):
            (part,) = [p for p in RIG["parts"] if p["id"] == pid]
            self.assertEqual(part["requires"], pid)
            self.assertEqual((part["drivers"], part["ride"]), ([], None))
            els = [e for e in SHAPE["elements"] if rigmath.part_of(RIG["parts"], e["name"]) == pid]
            self.assertEqual({f["texture"] for e in els for f in e["faces"].values()}, {"#" + pid})


class Textures(unittest.TestCase):
    def test_every_face_uses_a_declared_code_and_the_frame_matches(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertEqual(used, set(shape["textures"]))
        for code, path in FRAME["textures"].items():
            self.assertEqual(SHAPE["textures"][code], path, code)

    def test_the_muller_is_iron_and_its_coupling_oak(self):
        els = [e for e in SHAPE["elements"] if rigmath.part_of(RIG["parts"], e["name"]) == "muller"]
        self.assertEqual({f["texture"] for e in els if not e["name"].startswith("muller_coupling") for f in e["faces"].values()}, {"#iron"})

    def test_the_shoes_alone_wear_their_own_code(self):
        # the renderer sets `shoe` to the fitted plate's metal (iron or steel); in the shape it is iron plate
        by_part = {}
        for e in SHAPE["elements"]:
            by_part.setdefault(rigmath.part_of(RIG["parts"], e["name"]), set()).update(f["texture"] for f in e["faces"].values())
        self.assertEqual(by_part["shoes"], {"#shoe"})
        self.assertEqual([pid for pid, codes in by_part.items() if "#shoe" in codes], ["shoes"])
        self.assertEqual(SHAPE["textures"]["shoe"], "game:block/metal/plate/iron")
        self.assertNotIn("shoe", FRAME["textures"])

    def test_the_mods_own_textures_exist(self):
        for code, path in SHAPE["textures"].items():
            domain, rel = path.split(":", 1)
            if domain == "seraphhorizons":
                self.assertTrue((ASSETS / "textures" / f"{rel}.png").is_file(), f"{code}: {path}")
        self.assertEqual(SHAPE["textures"]["slurry"], "seraphhorizons:block/liquid/slurry")

    def test_the_mercury_is_drawn_from_the_games_rock_as_expanded_matter_draws_it(self):
        # em:mercuryportion: game:block/stone/rock/andesite2 with game:block/stone/rock/kimberlite1 over it,
        # Lighten; a shape names only the base, the blocktype adds the overlay (README "Gameplay")
        self.assertEqual(SHAPE["textures"]["mercury"], "game:block/stone/rock/andesite2")
        self.assertFalse((ASSETS / "textures" / "block" / "liquid" / "mercury.png").exists())

    def test_the_slurry_texture_is_the_generators(self):
        rows = slurry_texture.decode(SLURRY.read_bytes())
        self.assertEqual((len(rows), len(rows[0])), (32, 32))
        self.assertEqual(rows, [list(r) for r in slurry_texture.pixels()])

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
