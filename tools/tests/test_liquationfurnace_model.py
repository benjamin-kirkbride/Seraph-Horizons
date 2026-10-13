"""The liquation furnace's generated model files (mods-src/seraphhorizons/LiquationFurnace/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared rig
maths and uses the `requires` vocabulary (the build's stages, brickwork, lining and fittings, the fittings being the
plate, the pot and the door; the charge's cakes, the tin running, the tin in the pot and the lead left; the fuel and the
fuel burning; the damper's three places; the outlet's cap), its reference poses are its own maths, the stages' counts
are measured from the shape's brick, the brick types map to the game's textures (checked against the game's files when
they are at hand: $VINTAGE_STORY or ~/Games/vintagestory), the cells are rebuilt from the shipped shape, the fire door
is the only part that moves (sliding up its guides, shut over the fire hole), the plate falls to the front and its
spout's lip is over the pot's mouth, the side faces are plain brick for a row, the textures are the game's own (no new
texture), only the burning fuel glows, the anchors are where the contract puts them (no power cell, no air port; the
pot and the opening at the front, the fire door and the ash mouth in the back face's middle; the outlet under the
chimney's cell, the chimney six blocks from it up), and the model viewer's scenario (site/models.json) gives the
generator's states. Run with `python3 -m unittest discover -s tools/tests`.
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
sys.path.insert(0, str(MOD / "LiquationFurnace" / "tools"))

from machinegen import rigmath  # noqa: E402
from machinegen.geometry import aabb_of, flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("liquationfurnace_make_shape", MOD / "LiquationFurnace" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "liquationfurnace-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "liquationfurnace.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "liquationfurnace_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "LiquationFurnace" / "rig-reference.json").read_text())
MODELS = json.loads((ROOT / "site" / "models.json").read_text())
CHARGE = ("cakes", "tinrun", "tin", "lead")
FIRE = ("fuel1", "fuel2", "fire1", "fire2")
STAGES = ("brickwork", "lining", "fittings")
REQUIRES = {None, *STAGES, "damperopen", "damperhalf", "dampershut", "outletcap", *CHARGE, *FIRE}
GAME_TEXTURES = {
    "brick": "game:block/clay/brick/four/running/red1",
    "lining": "game:block/clay/refractory/tier3/front1",
    "iron": "game:block/metal/plate/iron",
    "cake": "game:block/metal/ingot/leadsolder",
    "tin": "game:block/metal/ingot/tin",
    "lead": "game:block/metal/ingot/lead",
    "fuel": "game:block/coal/charcoal",
    "ember": "game:block/coal/ember",
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
        # the fire door is the only part that moves: a step slide down its guides; the fittings are the plate, the pot
        # and the door; the iron frame has no requires (placing it puts the machine down)
        moving = {p["id"] for p in RIG["parts"] if p["drivers"]}
        self.assertEqual(moving, {"door"})
        req = {p["id"]: p["requires"] for p in RIG["parts"]}
        self.assertEqual((req["plate"], req["pot"], req["door"], req["frame"]), ("fittings", "fittings", "fittings", None))
        door = next(p for p in RIG["parts"] if p["id"] == "door")
        self.assertEqual([(d["type"], d.get("motion")) for d in door["drivers"]], [("rotate", None), ("step", "slide")])
        self.assertEqual(door["drivers"][0]["ratio"], 0.0)

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

    def test_only_the_burning_fuel_glows(self):
        # the tin and the lead, at 232 to 327 degrees, are far under the game's glow (500 and over)
        for e in SHAPE["elements"]:
            pid = rigmath.part_of(RIG["parts"], e["name"])
            glow = {f.get("glow", 0) for f in e["faces"].values()}
            if not glow:
                continue                                  # a brick box inside the wall draws no face
            if pid.startswith("fire"):
                self.assertEqual(glow, {make_shape.FIRE_GLOW}, e["name"])
            else:
                self.assertEqual(glow, {0}, e["name"])


class Door(unittest.TestCase):
    def test_slides_from_open_to_shut_over_the_fire_hole(self):
        (hx0, hx1), (hy0, hy1) = make_shape.FIRE_HOLE
        travel = RIG["furnace"]["doorTravel"] * 16.0
        plate = lambda depth: next(e for e in posed_part("door", theta=0.0, depth=depth) if e.name == "door_plate").aabb()  # noqa: E731
        shut, opened = plate(1.0), plate(0.0)
        self.assertTrue(shut[0][0] <= hx0 and shut[1][0] >= hx1 and shut[0][1] <= hy0 and shut[1][1] >= hy1)
        self.assertGreaterEqual(opened[0][1], hy1)
        self.assertAlmostEqual(opened[0][1] - shut[0][1], travel)
        # in the back face's recess at every depth, never past the back face
        for k in range(21):
            lo, hi = aabb_of(posed_part("door", theta=0.0, depth=k / 20))
            self.assertLessEqual(hi[2], 32.0 + 1e-6)
            self.assertGreaterEqual(lo[2], make_shape.DOOR_RECESS[2][0] - 1e-6)

    def test_theta_moves_nothing_and_depth_only_the_door(self):
        for pid in (p["id"] for p in RIG["parts"]):
            a = matrix(pid, theta=0.0, depth=0.4)
            b = matrix(pid, theta=4.4, depth=0.4)
            self.assertEqual([[round(v, 12) for v in r] for r in a], [[round(v, 12) for v in r] for r in b], pid)
            if pid != "door":
                self.assertEqual([[round(v, 12) + 0.0 for v in r] for r in matrix(pid, depth=0.6)],
                                 [[1.0, 0.0, 0.0, 0.0], [0.0, 1.0, 0.0, 0.0], [0.0, 0.0, 1.0, 0.0], [0.0, 0.0, 0.0, 1.0]], pid)


class Plate(unittest.TestCase):
    def test_falls_to_the_front_and_spouts_into_the_pot(self):
        slab = next(e for e in posed_part("plate", depth=0.0) if e.name == "plate_slab")
        lo, hi = slab.aabb()
        self.assertEqual((lo[0], hi[0]), make_shape.IN_X)
        # its top rises to the back: the corners at its back end are higher than at its front
        corners = slab.corners()
        front = max(c[1] for c in corners if c[2] < (lo[2] + hi[2]) / 2)
        back = max(c[1] for c in corners if c[2] > (lo[2] + hi[2]) / 2)
        self.assertGreater(back - front, 3.0)
        self.assertEqual(RIG["furnace"]["plateSlope"], make_shape.PLATE_SLOPE)
        # the spout's lip over the pot's mouth, above its rim
        pot = aabb_of(posed_part("pot", depth=0.0))
        lip = [v * 16.0 for v in RIG["spout"]["pos"]]
        self.assertTrue(pot[0][0] < lip[0] < pot[1][0] and pot[0][2] < lip[2] < pot[1][2])
        self.assertGreater(lip[1], pot[1][1])
        mouth = [v * 16.0 for v in RIG["pot"]["pos"]]
        self.assertAlmostEqual(mouth[1], pot[1][1], places=3)

    def test_the_cakes_lie_on_the_plate_over_the_fire(self):
        for e in posed_part("cakes", depth=0.0):
            self.assertGreater(e.c[2], make_shape.FIRE_Z[0])
            self.assertTrue(make_shape.IN_X[0] < e.c[0] < make_shape.IN_X[1])
        self.assertEqual(len(posed_part("cakes", depth=0.0)), RIG["furnace"]["cakes"])


class Furnace(unittest.TestCase):
    def test_cells_decks_and_anchors(self):
        cells = {tuple(c["pos"]): c for c in RIG["cells"]}
        box = {(x, y, z) for x in (0, 1) for y in (0, 1) for z in (0, 1)}
        self.assertEqual(set(cells), box)
        self.assertTrue(all(c.get("boxes") and not c.get("hollow") for c in cells.values()))
        self.assertEqual({p for p, c in cells.items() if "lid" in c}, {p for p in box if p[1] == 1})
        # fired by hand on natural draught: no power cell, no air port
        for key in ("powerCell", "powerFace", "airCell", "airFace"):
            self.assertNotIn(key, RIG)
        # worked from the front (the pot, the spout, the opening, the plate), fired from the back (the fire door, the
        # ash mouth, in the back face's middle)
        for key in ("pot", "spout", "opening"):
            self.assertTrue(0.0 <= RIG[key]["pos"][2] <= 1.0, key)
        self.assertEqual(RIG["plateSide"], "north")
        for key in ("fireDoor", "ash"):
            self.assertEqual((RIG[f"{key}Side"], RIG[key]["pos"][0], RIG[key]["pos"][2]), ("south", 1.0, 2.0))
        self.assertGreater(RIG["fireDoor"]["pos"][1], RIG["ash"]["pos"][1])
        # the outlet in the back row's east cell (a chimney stands on one cell), the chimney's cell over it and its
        # neighbour not the furnace's; six from the outlet up
        self.assertEqual((RIG["outletCell"], RIG["outletFace"]), ([1, 1, 1], "up"))
        for c in ((1, 2, 1), (0, 2, 1)):
            self.assertNotIn(c, cells)
        line = RIG["chimney"]
        self.assertEqual((line["axis"], line["length"]), ("y", 6.0))
        self.assertAlmostEqual(line["origin"][1] - line["length"] / 2, 2.0)
        self.assertAlmostEqual(line["origin"][1] + line["length"] / 2, 8.0)
        self.assertTrue(1.0 <= line["origin"][0] <= 2.0 and 1.0 <= line["origin"][2] <= 2.0)
        self.assertEqual(RIG["furnace"]["minChimney"], 6)
        self.assertEqual((RIG["furnace"]["rowMax"], RIG["furnace"]["rowAxis"]), (5, "x"))

    def test_centred_but_the_outlet(self):
        m = make_shape
        for x0, x1 in (m.IN_X, m.NICHE[0], m.SPOUT_GAP, m.FIRE_HOLE[0], m.ASH_MOUTH[0], m.DOOR_RECESS[0]):
            self.assertAlmostEqual(x0 + x1, m.W)
        self.assertAlmostEqual(m.POT_C[0], m.W / 2)
        # the stack's mouth inside the outlet's cell, the east one
        self.assertTrue(16.0 <= m.STACK[0][0] and m.STACK[0][1] <= 32.0 and 16.0 <= m.STACK[2][0] and m.STACK[2][1] <= 32.0)

    def test_side_faces_are_plain_brick_for_a_row(self):
        # nothing but brick reaches the side planes, so furnaces stand side by side, walls meeting
        for el in FLAT:
            lo, hi = el.aabb()
            if lo[0] < 1e-6 or hi[0] > 32.0 - 1e-6:
                self.assertTrue(el.name.startswith("bk_brick"), el.name)
                self.assertEqual({f["texture"] for f in next(e for e in SHAPE["elements"] if e["name"] == el.name)["faces"].values()} - {"#brick"}, set())


def game_assets():
    for root in (os.environ.get("VINTAGE_STORY"), str(Path.home() / "Games" / "vintagestory")):
        if root and (Path(root) / "assets" / "survival").is_dir():
            return Path(root) / "assets"
    return None


def element_volume(e):
    return math.prod(e["to"][k] - e["from"][k] for k in range(3))


class Build(unittest.TestCase):
    def test_the_stages_counts_are_measured_from_the_brick(self):
        # 2 fired bricks a block of brickwork, 4 refractory bricks a block of lining (the vault's zone with it: its box
        # less the chamber under the arc), fire clay one per 2.5 of those, each rounded up; the shipped shape's faces
        # are stepped in by at most 0.015
        build = RIG["build"]
        self.assertEqual([s["requires"] for s in build["stages"]], list(STAGES))
        st = {s["requires"]: s for s in build["stages"]}
        bk = sum(element_volume(e) for e in SHAPE["elements"] if e["name"].startswith("bk_brick_")) / 4096
        ln = sum(element_volume(e) for e in SHAPE["elements"] if e["name"].startswith("ln_brick_")) / 4096
        ln += make_shape.vault_volume() / 4096
        self.assertAlmostEqual(bk, st["brickwork"]["volumeBlocks"], delta=0.01)
        self.assertAlmostEqual(ln, st["lining"]["volumeBlocks"], delta=0.01)
        bricks, lining = math.ceil(st["brickwork"]["volumeBlocks"] * 2), math.ceil(st["lining"]["volumeBlocks"] * 4)
        self.assertEqual([(m["code"], m["quantity"]) for m in st["brickwork"]["materials"]], [("game:burnedbrick-*", bricks)])
        self.assertTrue(st["brickwork"]["oneType"])
        self.assertEqual([(m["code"], m["quantity"]) for m in st["lining"]["materials"]],
                         [("game:refractorybrick-fired-tier3", lining), ("game:clay-fire", math.ceil(lining / 2.5))])
        self.assertEqual(st["fittings"]["parts"], ["plate", "pot", "door"])
        self.assertEqual(st["fittings"]["also"], ["damperopen", "damperhalf", "dampershut"])
        self.assertEqual([(m["code"], m["quantity"]) for m in st["fittings"]["materials"]], list(make_shape.FITTINGS_STAGE))
        self.assertEqual([(m["code"], m["quantity"]) for m in build["frame"]["ingredients"]], list(make_shape.FRAME_RECIPE))
        self.assertEqual((build["outletCap"]["code"], build["outletCap"]["quantity"]), ("game:metalplate-iron", 1))

    def test_the_frame_is_the_iron_frame(self):
        # the block draws the iron frame alone: the brick, the lining and the fittings are stages; what is left stands
        # on its own, with no thin post taller than half the furnace (the straps are bands, wider)
        self.assertEqual({f["texture"] for e in FRAME["elements"] for f in e["faces"].values()}, {"#iron"})
        self.assertTrue(all(e["name"].startswith("fr_") for e in FRAME["elements"]))
        for e in FRAME["elements"]:
            size = [e["to"][k] - e["from"][k] for k in range(3)]
            if size[0] < 1.5 and size[2] < 1.5:
                self.assertLessEqual(size[1], 16.0, e["name"])

    def test_any_fired_brick_maps_to_its_blocks_texture(self):
        bricks = RIG["bricks"]
        self.assertEqual((bricks["code"], bricks["default"], bricks["item"]), ("brick", "red", "game:burnedbrick-{type}"))
        self.assertEqual(list(bricks["types"]), ["fire", "black", "brown", "cream", "gray", "orange", "red", "tan", "clinker"])
        self.assertEqual(SHAPE["textures"]["brick"], bricks["types"]["red"]["overlays"][0])
        # the crucible furnace's list, the same
        crucible = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "cruciblefurnace-rig.json").read_text())["bricks"]
        self.assertEqual(bricks["types"], crucible["types"])
        assets = game_assets()
        if assets is None:
            self.skipTest("no game assets ($VINTAGE_STORY or ~/Games/vintagestory)")
        for t, d in bricks["types"].items():
            for code in [d["base"]] + d.get("overlays", []):
                self.assertTrue((assets / "survival" / "textures" / (code.split(":", 1)[1] + ".png")).is_file(), (t, code))
        for code in GAME_TEXTURES.values():
            self.assertTrue((assets / "survival" / "textures" / (code.split(":", 1)[1] + ".png")).is_file(), code)
        text = (assets / "survival" / "itemtypes" / "resource" / "burnedbrick.json").read_text()
        states = re.search(r'code:\s*"type",\s*states:\s*\[([^\]]*)\]', text).group(1)
        self.assertEqual(sorted(re.findall(r'"([^"]+)"', states)), sorted(bricks["types"]))


class Scenario(unittest.TestCase):
    def test_the_viewer_gives_the_generators_states(self):
        model = next(m for m in MODELS["models"] if m["id"] == "liquation-furnace")
        self.assertEqual(model["rig"], "mods-src/seraphhorizons/assets/seraphhorizons/config/liquationfurnace-rig.json")
        sc = model["scenario"]
        self.assertEqual(set(sc["requires"]), REQUIRES - {None})
        self.assertNotIn("choices", sc)
        groups = {g["id"]: g for g in sc["states"]}
        self.assertEqual(set(groups), {"build", "charge", "fire", "damper", "outlet"})
        # the build, stage by stage, the furnace built by default; the contents and the damper wait for it, the cap
        # for the lining (its curb)
        build = groups["build"]
        self.assertEqual((build["values"], build["default"]), (list(STAGES), "complete"))
        self.assertEqual([(o["id"], tuple(o["fitted"])) for o in build["options"]], list(make_shape.STAGE_STATES))
        for gid in ("charge", "fire", "damper"):
            self.assertEqual(groups[gid]["needs"], list(STAGES), gid)
        self.assertEqual(groups["outlet"]["needs"], ["brickwork", "lining"])
        self.assertEqual([(o["id"], o["fitted"]) for o in groups["damper"]["options"]],
                         [("open", ["damperopen"]), ("half", ["damperhalf"]), ("shut", ["dampershut"])])
        for gid, states, values in (("charge", make_shape.CHARGE_STATES, CHARGE), ("fire", make_shape.FIRE_STATES, FIRE)):
            g = groups[gid]
            self.assertEqual(g["values"], list(values))
            self.assertEqual([(o["id"], o["label"], tuple(o["fitted"])) for o in g["options"]], list(states))
        self.assertEqual([(o["id"], o["fitted"]) for o in groups["outlet"]["options"]], [("open", []), ("capped", ["outletcap"])])
        # the stages' labels give the rig's counts
        st = {s["requires"]: s for s in RIG["build"]["stages"]}
        labels = {o["id"]: o["label"] for o in build["options"]}
        self.assertIn(f"{st['brickwork']['materials'][0]['quantity']} fired bricks", labels["brickwork"])
        self.assertIn(f"{st['lining']['materials'][0]['quantity']} tier-3 refractory bricks", labels["lining"])
        self.assertIn(f"{st['lining']['materials'][1]['quantity']} fire clay", labels["lining"])
        # Play opens and shuts the fire door
        targets = [p.get("to") for p in sc["play"]["phases"] if not p.get("wait")]
        self.assertEqual(sorted(targets), [0, 1])


if __name__ == "__main__":
    unittest.main()
