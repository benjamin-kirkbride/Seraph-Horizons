"""The crucible furnace's generated model files (mods-src/seraphhorizons/CrucibleFurnace/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared rig
maths and uses the `requires` vocabulary (two pots and their charges and melts, the coke and the burning coke, the
damper's three places, the outlet's cap), its reference poses are its own maths, the cells are rebuilt from the shipped
shape, the hinged lid is the only part that moves (flush in its seat shut, resting just past upright open), each
pot's melt holds a full pot of 300 units, the side faces are plain brick for a row, the textures are the game's own
(no new texture), only the burning coke and the melts glow, the anchors are where the contract puts them (no power
cell, no air port; the outlet under the chimney's cell, the chimney six blocks from it up), and the model viewer's
scenario (site/models.json) gives the generator's states. Run with `python3 -m unittest discover -s tools/tests`.
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
sys.path.insert(0, str(MOD / "CrucibleFurnace" / "tools"))

from machinegen import rigmath  # noqa: E402
from machinegen.geometry import aabb_of, flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("cruciblefurnace_make_shape", MOD / "CrucibleFurnace" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "cruciblefurnace-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "cruciblefurnace.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "cruciblefurnace_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "CrucibleFurnace" / "rig-reference.json").read_text())
MODELS = json.loads((ROOT / "site" / "models.json").read_text())
CONTENTS = ("pot1", "pot2", "charge1", "charge2", "melt1", "melt2", "coke1", "coke2", "coke3", "fire1", "fire2", "fire3")
REQUIRES = {None, "damperopen", "damperhalf", "dampershut", "outletcap", *CONTENTS}
GAME_TEXTURES = {
    "brick": "game:block/clay/refractory/tier3/front1",
    "iron": "game:block/metal/plate/iron",
    "clay": "game:block/clay/hardened/fire",
    "coke": "game:block/coal/coke",
    "ember": "game:block/coal/ember",
    "melt": "game:block/metal/ingot/stainlesssteel",
    "charge": "game:block/metal/ingot/iron",
}
FLAT = flatten(SHAPE["elements"], textures={})


def matrix(pid, **ins):
    return rigmath.part_matrix(RIG["parts"], pid, ins)


def posed_part(pid, **ins):
    m = matrix(pid, **ins)
    return [rigmath.posed(el, m) for el in FLAT if rigmath.part_of(RIG["parts"], el.name) == pid]


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        # the lid has no requires (it is the furnace's) and is the only part that moves: a step turn about its pin
        moving = {p["id"] for p in RIG["parts"] if p["drivers"]}
        self.assertEqual(moving, {"lid"})
        lid = next(p for p in RIG["parts"] if p["id"] == "lid")
        self.assertIsNone(lid["requires"])
        self.assertEqual([(d["type"], d.get("motion")) for d in lid["drivers"]], [("rotate", None), ("step", "rotate")])

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreater(len(poses), 10)
        self.assertTrue({p["depth"] for p in poses} >= {0.0, 0.5, 1.0})
        for pose in poses:
            for pid, want in pose["matrices"].items():
                got = matrix(pid, theta=pose["theta"], depth=pose["depth"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {pose['theta']}, {pose['depth']}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])


class Textures(unittest.TestCase):
    def test_only_the_games_own_textures(self):
        self.assertEqual(SHAPE["textures"], GAME_TEXTURES)
        self.assertTrue(all(v.startswith("game:") for v in FRAME["textures"].values()))

    def test_only_the_burning_coke_and_the_melts_glow(self):
        for e in SHAPE["elements"]:
            pid = rigmath.part_of(RIG["parts"], e["name"])
            glow = {f.get("glow", 0) for f in e["faces"].values()}
            if pid.startswith("fire"):
                self.assertEqual(glow, {make_shape.FIRE_GLOW}, e["name"])
            elif pid.startswith("melt"):
                self.assertEqual(glow, {make_shape.MELT_GLOW}, e["name"])
            else:
                self.assertEqual(glow, {0}, e["name"])


class Lid(unittest.TestCase):
    def test_shut_flush_in_its_seat_and_open_just_past_upright(self):
        lo, hi = aabb_of(posed_part("lid", theta=0.0, depth=1.0))
        want = ([make_shape.SEAT_X[0], make_shape.SEAT_Y, make_shape.SEAT_Z[0]], [make_shape.SEAT_X[1], make_shape.FLOOR_TOP, make_shape.SEAT_Z[1]])
        for a, b in zip(lo + hi, want[0] + want[1]):
            self.assertAlmostEqual(a, b, places=4)
        open_deg = RIG["furnace"]["lidOpenDegrees"]
        self.assertGreater(open_deg, 90.0)
        self.assertLess(open_deg, 95.0)
        # open: standing over the hole, in the cells over it (not the chimney's behind), its weight behind the pin
        els = posed_part("lid", theta=0.0, depth=0.0)
        lo, hi = aabb_of(els)
        self.assertLessEqual(hi[2], 16.0)
        self.assertLessEqual(hi[1], 48.0)
        vol = sum(abs(e.size[0] * e.size[1] * e.size[2]) for e in els)
        cz = sum(e.c[2] * abs(e.size[0] * e.size[1] * e.size[2]) for e in els) / vol
        self.assertGreater(cz, RIG["hinge"]["origin"][2] * 16.0)

    def test_theta_moves_nothing_and_depth_only_the_lid(self):
        for pid in (p["id"] for p in RIG["parts"]):
            a = matrix(pid, theta=0.0, depth=0.4)
            b = matrix(pid, theta=4.4, depth=0.4)
            self.assertEqual([[round(v, 12) for v in r] for r in a], [[round(v, 12) for v in r] for r in b], pid)
            if pid != "lid":
                self.assertEqual([[round(v, 12) + 0.0 for v in r] for r in matrix(pid, depth=0.6)],
                                 [[1.0, 0.0, 0.0, 0.0], [0.0, 1.0, 0.0, 0.0], [0.0, 0.0, 1.0, 0.0], [0.0, 0.0, 0.0, 1.0]], pid)


class Pots(unittest.TestCase):
    def test_each_melt_holds_a_full_pot(self):
        # a pot holds 300 units (#768), two a firing; each melt's top stands where a pot's inside holds them
        self.assertEqual((RIG["furnace"]["potUnits"], RIG["furnace"]["pots"]), (300, 2))
        level = make_shape.pot_level(300)
        for i in (1, 2):
            for what in ("melt", "charge"):
                top = max(e.aabb()[1][1] for e in posed_part(f"{what}{i}", depth=0.0) if "_piece" not in e.name)
                self.assertAlmostEqual(top, level, delta=0.02, msg=f"{what}{i}")
        self.assertGreaterEqual(make_shape.POT_TOP - level, 1.0)

    def test_two_pots_side_by_side_in_one_hole(self):
        p1, p2 = aabb_of(posed_part("pot1", depth=0.0)), aabb_of(posed_part("pot2", depth=0.0))
        self.assertLess(p1[1][0], p2[0][0])
        self.assertAlmostEqual((p1[0][2] + p1[1][2]) / 2, (p2[0][2] + p2[1][2]) / 2)
        self.assertEqual((RIG["pot1Cell"], RIG["pot2Cell"]), ([0, 1, 0], [1, 1, 0]))


class Furnace(unittest.TestCase):
    def test_cells_decks_and_anchors(self):
        cells = {tuple(c["pos"]): c for c in RIG["cells"]}
        box = {(x, y, z) for x in (0, 1) for y in (0, 1) for z in (0, 1)}
        lidcells = {(0, 2, 0), (1, 2, 0)}
        self.assertEqual(set(cells), box | lidcells)
        self.assertTrue(all(cells[p].get("hollow") and not cells[p].get("boxes") for p in lidcells))
        self.assertTrue(all(cells[p].get("boxes") for p in box))
        self.assertEqual({p for p, c in cells.items() if "lid" in c}, {p for p in box if p[1] == 1})
        # fired with coke on natural draught: no power cell, no air port
        for key in ("powerCell", "powerFace", "airCell", "airFace"):
            self.assertNotIn(key, RIG)
        self.assertEqual((RIG["ashSide"], RIG["ash"]["pos"][2]), ("north", 0.0))
        # the outlet over the east back cell, the chimney's cell over it not the furnace's; six from the outlet up
        self.assertEqual((RIG["outletCell"], RIG["outletFace"]), ([1, 1, 1], "up"))
        self.assertNotIn((1, 2, 1), cells)
        line = RIG["chimney"]
        self.assertEqual((line["axis"], line["length"]), ("y", 6.0))
        self.assertAlmostEqual(line["origin"][1] - line["length"] / 2, 2.0)
        self.assertAlmostEqual(line["origin"][1] + line["length"] / 2, 8.0)
        self.assertEqual(RIG["furnace"]["minChimney"], 6)
        self.assertEqual((RIG["furnace"]["rowMax"], RIG["furnace"]["rowAxis"]), (6, "x"))
        self.assertEqual(RIG["furnace"]["cokeLayers"] * RIG["furnace"]["cokePerLayer"], 6)   # CokeCapacity

    def test_side_faces_are_plain_brick_for_a_row(self):
        # nothing but brick reaches the side planes, so furnaces stand side by side, walls meeting
        for el in FLAT:
            lo, hi = el.aabb()
            if lo[0] < 1e-6 or hi[0] > 32.0 - 1e-6:
                self.assertTrue(el.name.startswith("fr_brick"), el.name)
                self.assertEqual({f["texture"] for f in next(e for e in SHAPE["elements"] if e["name"] == el.name)["faces"].values()}, {"#brick"})


class Scenario(unittest.TestCase):
    def test_the_viewer_gives_the_generators_states(self):
        model = next(m for m in MODELS["models"] if m["id"] == "crucible-furnace")
        self.assertEqual(model["rig"], "mods-src/seraphhorizons/assets/seraphhorizons/config/cruciblefurnace-rig.json")
        sc = model["scenario"]
        self.assertEqual(set(sc["requires"]), REQUIRES - {None})
        self.assertEqual(sc["choices"], [{"label": "Ash-pit damper", "values": ["damperopen", "damperhalf", "dampershut"],
                                          "default": "damperopen"}])
        groups = {g["id"]: g for g in sc["states"]}
        self.assertEqual(set(groups), {"pots", "outlet"})
        pots = groups["pots"]
        self.assertEqual(pots["values"], list(CONTENTS))
        got = [(o["id"], o["label"], tuple(o["fitted"])) for o in pots["options"]]
        self.assertEqual(got, [(sid, label, tuple(what)) for sid, label, what in make_shape.POT_STATES])
        self.assertEqual([(o["id"], o["fitted"]) for o in groups["outlet"]["options"]], [("open", []), ("capped", ["outletcap"])])
        # Play opens and shuts the lid
        targets = [p.get("to") for p in sc["play"]["phases"] if not p.get("wait")]
        self.assertEqual(sorted(targets), [0, 1])
        self.assertTrue(math.isclose(make_shape.OPEN_DEG, RIG["furnace"]["lidOpenDegrees"]))


if __name__ == "__main__":
    unittest.main()
