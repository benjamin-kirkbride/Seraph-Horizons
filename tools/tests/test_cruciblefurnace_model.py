"""The crucible furnace's generated model files (mods-src/seraphhorizons/CrucibleFurnace/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared rig
maths and uses the `requires` vocabulary (the build's stages, brickwork, lining and lid; two pots and their charges and
melts, the coke and the burning coke, the damper's three places, the outlet's cap), its reference poses are its own
maths, the stages' counts are measured from the shape's brick, the brick types map to the game's textures (checked
against the game's files when they are at hand: $VINTAGE_STORY or ~/Games/vintagestory), the cells are rebuilt from the shipped
shape, the hinged lid is the only part that moves (flush in its seat shut, resting just past upright open, swinging
in its own cells with no pit), each pot's melt holds a full pot of 300 units, the hole, the ash mouth and the outlet
are centred with thick walls round the hole, the side faces are plain brick for a row, the textures are the game's own
(no new texture), only the burning coke and the melts glow, the anchors are where the contract puts them (no power
cell, no air port; both pots in the hole's cell; the outlet under the chimney's cell, the chimney six blocks from it
up), and the model viewer's scenario (site/models.json) gives the generator's states. Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import math
import os
import re
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
STAGES = ("brickwork", "lining", "lid")
REQUIRES = {None, *STAGES, "damperopen", "damperhalf", "dampershut", "outletcap", *CONTENTS}
GAME_TEXTURES = {
    "brick": "game:block/clay/brick/four/running/red1",
    "lining": "game:block/clay/refractory/tier3/front1",
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
        # the lid is the last stage's and the only part that moves: a step turn about its top back edge; the iron
        # frame has no requires (placing it puts the machine down)
        moving = {p["id"] for p in RIG["parts"] if p["drivers"]}
        self.assertEqual(moving, {"lid"})
        req = {p["id"]: p["requires"] for p in RIG["parts"]}
        self.assertEqual((req["lid"], req["brickwork"], req["lining"], req["frame"]), ("lid", "brickwork", "lining", None))
        # the hinge's pins and stops, on their ties, come with the brickwork
        self.assertEqual(req["hinge"], "brickwork")
        lid = next(p for p in RIG["parts"] if p["id"] == "lid")
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
        # shut: its box (the hinge's knuckles aside) is the seat's, in the shipped frame (moved a block west)
        body = [e for e in posed_part("lid", theta=0.0, depth=1.0) if not e.name.startswith("lid_knuckle")]
        lo, hi = aabb_of(body)
        dx = -16.0 * make_shape.ORIGIN_CELL[0]
        want = ([make_shape.SEAT_X[0] + dx, make_shape.SEAT_Y, make_shape.SEAT_Z[0]],
                [make_shape.SEAT_X[1] + dx, make_shape.FLOOR_TOP, make_shape.SEAT_Z[1]])
        for a, b in zip(lo + hi, want[0] + want[1]):
            self.assertAlmostEqual(a, b, delta=0.01)
        open_deg = RIG["furnace"]["lidOpenDegrees"]
        self.assertGreater(open_deg, 90.0)
        self.assertLessEqual(open_deg, 100.0)
        # open: standing over the middle row, in the cells over it (not the chimney's behind), its weight behind the axis
        els = posed_part("lid", theta=0.0, depth=0.0)
        lo, hi = aabb_of(els)
        self.assertLessEqual(hi[2], 32.0)
        self.assertLessEqual(hi[1], 48.0)
        vol = sum(abs(e.size[0] * e.size[1] * e.size[2]) for e in els)
        cz = sum(e.c[2] * abs(e.size[0] * e.size[1] * e.size[2]) for e in els) / vol
        self.assertGreater(cz, RIG["hinge"]["origin"][2] * 16.0)

    def test_swings_in_its_own_cells_with_no_pit(self):
        # hinged about its top back edge: over the whole swing nothing of it but the knuckles round the axis goes
        # behind that edge under the floor, and all of it stays over the front two rows, under three blocks
        zb, yb = RIG["hinge"]["origin"][2] * 16.0, RIG["hinge"]["origin"][1] * 16.0
        self.assertAlmostEqual(yb, make_shape.FLOOR_TOP)
        for k in range(51):
            for e in posed_part("lid", theta=0.0, depth=k / 50):
                lo, hi = e.aabb()
                self.assertLessEqual(hi[2], 32.0 + 1e-6)
                self.assertLessEqual(hi[1], 48.0 + 1e-6)
                if not e.name.startswith("lid_knuckle") and lo[1] < yb - 1e-3:
                    self.assertLessEqual(hi[2], zb + 1e-3, (e.name, k))

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
        # both over one cell, the hole's (the controller's, a row back, at the top): a click there is the nearer pot's
        self.assertEqual((RIG["holeCell"], RIG["holeFace"]), ([0, 1, 1], "up"))
        for i, box in ((1, p1), (2, p2)):
            pos = RIG[f"pot{i}"]["pos"]
            self.assertTrue(all(RIG["holeCell"][k] <= pos[k] <= RIG["holeCell"][k] + 1 for k in range(3)), i)
            self.assertAlmostEqual(pos[0] * 16.0, (box[0][0] + box[1][0]) / 2, places=4)
        self.assertAlmostEqual(RIG["pot1"]["pos"][0] + RIG["pot2"]["pos"][0], 1.0)       # symmetric about the middle


class Furnace(unittest.TestCase):
    def test_cells_decks_and_anchors(self):
        cells = {tuple(c["pos"]): c for c in RIG["cells"]}
        box = {(x, y, z) for x in (-1, 0, 1) for y in (0, 1) for z in (0, 1, 2)}
        lidcells = {(x, 2, z) for x in (-1, 0, 1) for z in (0, 1)}
        self.assertEqual(set(cells), box | lidcells)
        self.assertTrue(all(cells[p].get("hollow") and not cells[p].get("boxes") for p in lidcells))
        self.assertTrue(all(cells[p].get("boxes") for p in box))
        self.assertEqual({p for p, c in cells.items() if "lid" in c}, {p for p in box if p[1] == 1})
        # fired with coke on natural draught: no power cell, no air port
        for key in ("powerCell", "powerFace", "airCell", "airFace"):
            self.assertNotIn(key, RIG)
        self.assertEqual((RIG["ashSide"], RIG["ash"]["pos"][0], RIG["ash"]["pos"][2]), ("north", 0.5, 0.0))
        # the outlet in the back row's middle, the chimney's cell over it and its neighbours not the furnace's; six
        # from the outlet up
        self.assertEqual((RIG["outletCell"], RIG["outletFace"]), ([0, 1, 2], "up"))
        for c in ((0, 2, 2), (-1, 2, 2), (1, 2, 2)):
            self.assertNotIn(c, cells)
        line = RIG["chimney"]
        self.assertEqual((line["axis"], line["length"]), ("y", 6.0))
        self.assertAlmostEqual(line["origin"][1] - line["length"] / 2, 2.0)
        self.assertAlmostEqual(line["origin"][1] + line["length"] / 2, 8.0)
        self.assertEqual(RIG["furnace"]["minChimney"], 6)
        self.assertEqual((RIG["furnace"]["rowMax"], RIG["furnace"]["rowAxis"]), (5, "x"))
        self.assertEqual(RIG["furnace"]["cokeLayers"] * RIG["furnace"]["cokePerLayer"], 12)   # CokeCapacity, two pots (#796)

    def test_centred_with_thick_walls(self):
        # the hole, the ash mouth and its damper, and the outlet on the middle line; brick round the hole's seat at
        # least MIN_WALL on the sides and the front (the front's less the damper's recess)
        m = make_shape
        for x0, x1 in (m.HOLE_X, m.MOUTH[0], m.RECESS[0], m.STACK[0], m.CURB[0]):
            self.assertAlmostEqual(x0 + x1, m.W)
        self.assertGreaterEqual(m.SEAT_X[0], m.MIN_WALL)
        self.assertGreaterEqual(m.W - m.SEAT_X[1], m.MIN_WALL)
        self.assertGreaterEqual(m.HOLE_Z[0] - m.RECESS[2][1], m.MIN_WALL)
        self.assertGreaterEqual(m.MIN_WALL, 8.0)

    def test_side_faces_are_plain_brick_for_a_row(self):
        # nothing but brick reaches the side planes, so furnaces stand side by side, walls meeting
        west, east = -16.0 * make_shape.ORIGIN_CELL[0], 16.0 * (make_shape.CELLS_X - make_shape.ORIGIN_CELL[0])
        for el in FLAT:
            lo, hi = el.aabb()
            if lo[0] < west + 1e-6 or hi[0] > east - 1e-6:
                self.assertTrue(el.name.startswith("bk_brick"), el.name)
                self.assertEqual({f["texture"] for f in next(e for e in SHAPE["elements"] if e["name"] == el.name)["faces"].values()}, {"#brick"})


def game_assets():
    for root in (os.environ.get("VINTAGE_STORY"), str(Path.home() / "Games" / "vintagestory")):
        if root and (Path(root) / "assets" / "survival").is_dir():
            return Path(root) / "assets"
    return None


def element_volume(e):
    return math.prod(e["to"][k] - e["from"][k] for k in range(3))


class Build(unittest.TestCase):
    def test_the_stages_counts_are_measured_from_the_brick(self):
        # 2 fired bricks a block of brickwork, 4 refractory bricks a block of lining (the stands' octagons with it),
        # fire clay one per 2.5 of those, each rounded up; the shipped shape's faces are stepped in by at most 0.015
        build = RIG["build"]
        self.assertEqual([s["requires"] for s in build["stages"]], list(STAGES))
        st = {s["requires"]: s for s in build["stages"]}
        bk = sum(element_volume(e) for e in SHAPE["elements"] if e["name"].startswith("bk_brick_")) / 4096
        ln = sum(element_volume(e) for e in SHAPE["elements"] if e["name"].startswith("ln_brick_")) / 4096
        y0, y1, a = make_shape.STAND
        ln += 2 * make_shape.oct_area(a) * (y1 - y0) / 4096
        self.assertAlmostEqual(bk, st["brickwork"]["volumeBlocks"], delta=0.01)
        self.assertAlmostEqual(ln, st["lining"]["volumeBlocks"], delta=0.01)
        bricks, lining = math.ceil(st["brickwork"]["volumeBlocks"] * 2), math.ceil(st["lining"]["volumeBlocks"] * 4)
        self.assertEqual([(m["code"], m["quantity"]) for m in st["brickwork"]["materials"]], [("game:burnedbrick-*", bricks)])
        self.assertTrue(st["brickwork"]["oneType"])
        self.assertEqual([(m["code"], m["quantity"]) for m in st["lining"]["materials"]],
                         [("game:refractorybrick-fired-tier3", lining), ("game:clay-fire", math.ceil(lining / 2.5))])
        self.assertEqual([(m["code"], m["quantity"]) for m in st["lid"]["materials"]],
                         [("game:refractorybrick-fired-tier3", 4), ("game:metalplate-iron", 2)])
        self.assertEqual([(m["code"], m["quantity"]) for m in build["frame"]["ingredients"]],
                         [("game:metalplate-iron", 2), ("game:rod-iron", 4), ("game:metalnailsandstrips-iron", 10)])
        self.assertEqual((build["outletCap"]["code"], build["outletCap"]["quantity"]), ("game:metalplate-iron", 1))

    def test_the_frame_is_the_iron_frame(self):
        # the block draws the iron frame alone: the brick, the lining and the lid are stages, and the hinge's pins and
        # stops come with the brickwork; what is left stands on its own, with no thin post taller than half the
        # furnace (the corner straps are bands, wider)
        self.assertEqual({f["texture"] for e in FRAME["elements"] for f in e["faces"].values()}, {"#iron"})
        self.assertTrue(all(e["name"].startswith("fr_") for e in FRAME["elements"]))
        names = {e["name"] for e in FRAME["elements"]}
        self.assertFalse(any(n.startswith(("fr_pin", "fr_stop", "hg_")) for n in names))
        for e in FRAME["elements"]:
            size = [e["to"][k] - e["from"][k] for k in range(3)]
            if size[0] < 1.5 and size[2] < 1.5:
                self.assertLessEqual(size[1], 16.0, e["name"])

    def test_any_fired_brick_maps_to_its_blocks_texture(self):
        bricks = RIG["bricks"]
        self.assertEqual((bricks["code"], bricks["default"], bricks["item"]), ("brick", "red", "game:burnedbrick-{type}"))
        self.assertEqual(list(bricks["types"]), ["fire", "black", "brown", "cream", "gray", "orange", "red", "tan", "clinker"])
        self.assertEqual(SHAPE["textures"]["brick"], bricks["types"]["red"]["overlays"][0])
        assets = game_assets()
        if assets is None:
            self.skipTest("no game assets ($VINTAGE_STORY or ~/Games/vintagestory)")
        for t, d in bricks["types"].items():
            for code in [d["base"]] + d.get("overlays", []):
                self.assertTrue((assets / "survival" / "textures" / (code.split(":", 1)[1] + ".png")).is_file(), (t, code))
        text = (assets / "survival" / "itemtypes" / "resource" / "burnedbrick.json").read_text()
        states = re.search(r'code:\s*"type",\s*states:\s*\[([^\]]*)\]', text).group(1)
        self.assertEqual(sorted(re.findall(r'"([^"]+)"', states)), sorted(bricks["types"]))


class Scenario(unittest.TestCase):
    def test_the_viewer_gives_the_generators_states(self):
        model = next(m for m in MODELS["models"] if m["id"] == "crucible-furnace")
        self.assertEqual(model["rig"], "mods-src/seraphhorizons/assets/seraphhorizons/config/cruciblefurnace-rig.json")
        sc = model["scenario"]
        self.assertEqual(set(sc["requires"]), REQUIRES - {None})
        self.assertNotIn("choices", sc)
        groups = {g["id"]: g for g in sc["states"]}
        self.assertEqual(set(groups), {"build", "pots", "damper", "outlet"})
        # the build, stage by stage, the furnace built by default; the contents and the damper wait for it, the cap
        # for the lining (its curb)
        build = groups["build"]
        self.assertEqual((build["values"], build["default"]), (list(STAGES), "complete"))
        self.assertEqual([(o["id"], tuple(o["fitted"])) for o in build["options"]], list(make_shape.STAGE_STATES))
        self.assertEqual(groups["pots"]["needs"], list(STAGES))
        self.assertEqual(groups["damper"]["needs"], list(STAGES))
        self.assertEqual(groups["outlet"]["needs"], ["brickwork", "lining"])
        self.assertEqual([(o["id"], o["fitted"]) for o in groups["damper"]["options"]],
                         [("open", ["damperopen"]), ("half", ["damperhalf"]), ("shut", ["dampershut"])])
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
