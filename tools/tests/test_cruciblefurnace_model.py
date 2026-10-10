"""The crucible furnace's generated model files (mods-src/seraphhorizons/CrucibleFurnace/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared rig
maths and uses the per-hole `requires` vocabulary, its reference poses are its own maths, the cells are rebuilt from
the shipped shape, the covers lie flush in their bays at rest and in their holes at depth 1 and nothing else moves,
the textures are the game's own (no new texture), only the burning coke and the melt glow, the anchors are where the
contract puts them (no power cell; the outlet under the chimney's cell), and the model viewer's scenario
(site/models.json) gives each hole the generator's states. Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
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
CONTENTS = ("pot", "charge", "melt", "coke1", "coke2", "coke3", "fire1", "fire2", "fire3")
REQUIRES = {None} | {f"h{i}{w}" for i in (1, 2) for w in CONTENTS}
GAME_TEXTURES = {
    "brick": "game:block/clay/refractory/tier3/front1",
    "iron": "game:block/metal/plate/iron",
    "clay": "game:block/clay/hardened/fire",
    "coke": "game:block/coal/coke",
    "ember": "game:block/coal/ember",
    "melt": "game:block/metal/ingot/stainlesssteel",
    "charge": "game:block/metal/ingot/iron",
}


def matrix(pid, **ins):
    return rigmath.part_matrix(RIG["parts"], pid, ins)


def posed_part(pid, **ins):
    m = matrix(pid, **ins)
    return [rigmath.posed(el, m) for el in flatten(SHAPE["elements"], textures={}) if rigmath.part_of(RIG["parts"], el.name) == pid]


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
        # the covers have no requires (they are the furnace's) and are the only parts that move
        moving = {p["id"] for p in RIG["parts"] if p["drivers"]}
        self.assertEqual(moving, {"h1cover", "h2cover"})
        self.assertEqual({p["requires"] for p in RIG["parts"] if p["id"] in moving}, {None})

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
        self.assertEqual({p["depth"] for p in poses} >= {0.0, make_shape.T_LIFT, make_shape.T_SET, 1.0}, True)
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
        # no new texture: brick, iron, fired clay, coke and its ember, the ingots' metals, all the game's
        self.assertEqual(SHAPE["textures"], GAME_TEXTURES)
        self.assertTrue(all(v.startswith("game:") for v in FRAME["textures"].values()))

    def test_only_the_burning_coke_and_the_melt_glow(self):
        for e in SHAPE["elements"]:
            pid = rigmath.part_of(RIG["parts"], e["name"])
            glow = {f.get("glow", 0) for f in e["faces"].values()}
            if pid[2:].startswith("fire"):
                self.assertEqual(glow, {make_shape.FIRE_GLOW}, e["name"])
            elif pid[2:] == "melt":
                self.assertEqual(glow, {make_shape.MELT_GLOW}, e["name"])
            else:
                self.assertEqual(glow, {0}, e["name"])


class Covers(unittest.TestCase):
    def test_flush_in_the_bay_at_rest_and_in_the_hole_when_on(self):
        for i in (1, 2):
            cx = make_shape.HOLE_X[i] - 16.0 * make_shape.ORIGIN_CELL[0]
            for depth, z in ((0.0, make_shape.BAY_Z), (1.0, make_shape.HOLE_Z)):
                lo, hi = aabb_of(posed_part(f"h{i}cover", theta=0.0, depth=depth))
                self.assertAlmostEqual(lo[0], cx - make_shape.SEAT_HALF, places=4)
                self.assertAlmostEqual(hi[0], cx + make_shape.SEAT_HALF, places=4)
                self.assertAlmostEqual(lo[2], z - make_shape.SEAT_HALF, places=4)
                self.assertAlmostEqual(hi[2], z + make_shape.SEAT_HALF, places=4)
                self.assertAlmostEqual(hi[1], make_shape.FLOOR_TOP, places=4)
            # carried clear of the floor
            lo, _ = aabb_of(posed_part(f"h{i}cover", theta=0.0, depth=0.5))
            self.assertGreater(lo[1], make_shape.FLOOR_TOP)

    def test_theta_moves_nothing(self):
        for pid in (p["id"] for p in RIG["parts"]):
            a = matrix(pid, theta=0.0, depth=0.4)
            b = matrix(pid, theta=4.4, depth=0.4)
            self.assertEqual([[round(v, 12) for v in r] for r in a], [[round(v, 12) for v in r] for r in b], pid)


class Anchors(unittest.TestCase):
    def test_cells_lids_and_anchors(self):
        cells = {tuple(c["pos"]): c for c in RIG["cells"]}
        box = {(x, y, z) for x in (-1, 0, 1) for y in (0, 1) for z in (0, 1)}
        lift = {(-1, 2, 0), (-1, 2, 1), (1, 2, 0), (1, 2, 1)}
        self.assertEqual(set(cells), box | lift)
        self.assertTrue(all(cells[p].get("hollow") and not cells[p].get("boxes") for p in lift))
        self.assertTrue(all(cells[p].get("boxes") for p in box))
        # a lid at the floor on every top cell of the brickwork, so no one falls into an open hole
        self.assertEqual({p for p, c in cells.items() if "lid" in c}, {p for p in box if p[1] == 1})
        self.assertTrue(all(cells[p]["lid"] == 1.0 for p in box if p[1] == 1))
        # fired with coke: no power cell
        self.assertNotIn("powerCell", RIG)
        self.assertNotIn("powerFace", RIG)
        self.assertEqual((RIG["hole1Cell"], RIG["hole1Face"]), ([-1, 1, 0], "up"))
        self.assertEqual((RIG["hole2Cell"], RIG["hole2Face"]), ([1, 1, 0], "up"))
        # the outlet on the back row's middle, the chimney's cell over it not the furnace's
        self.assertEqual((RIG["outletCell"], RIG["outletFace"]), ([0, 1, 1], "up"))
        self.assertNotIn((0, 2, 1), cells)
        line = RIG["chimney"]
        self.assertEqual((line["axis"], line["length"]), ("y", float(make_shape.MIN_CHIMNEY)))
        self.assertAlmostEqual(line["origin"][1] - line["length"] / 2, 2.0)
        # the air port on the back face of the back row's middle, into the cave
        self.assertEqual((RIG["airCell"], RIG["airFace"]), ([0, 0, 1], "south"))
        self.assertEqual(RIG["furnace"]["holes"], 2)
        self.assertEqual(RIG["furnace"]["cokeLayers"] * RIG["furnace"]["cokePerLayer"], 6)   # CokeCapacity


class Scenario(unittest.TestCase):
    def test_the_viewer_gives_each_hole_the_generators_states(self):
        model = next(m for m in MODELS["models"] if m["id"] == "crucible-furnace")
        self.assertEqual(model["rig"], "mods-src/seraphhorizons/assets/seraphhorizons/config/cruciblefurnace-rig.json")
        sc = model["scenario"]
        self.assertEqual(set(sc["requires"]), REQUIRES - {None})
        groups = sc["states"]
        self.assertEqual([g["id"] for g in groups], ["hole1", "hole2"])
        for i, g in zip((1, 2), groups):
            self.assertEqual(g["values"], [f"h{i}{w}" for w in CONTENTS])
            got = [(o["id"], o["label"], tuple(o["fitted"])) for o in g["options"]]
            want = [(sid, label, tuple(f"h{i}{w}" for w in what)) for sid, label, what in make_shape.STATES]
            self.assertEqual(got, want)
        # Play lifts the covers on and off
        targets = [p.get("to") for p in sc["play"]["phases"] if not p.get("wait")]
        self.assertEqual(sorted(targets), [0, 1])


if __name__ == "__main__":
    unittest.main()
