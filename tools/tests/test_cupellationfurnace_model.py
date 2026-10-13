"""The cupellation furnace's generated model files (mods-src/seraphhorizons/CupellationFurnace/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared rig
maths and uses the `requires` vocabulary (the build's stages, brickwork, lining, test, engine and fittings; the coal and
the fire, the lead, the silver cake, the running litharge and the litharge in the pot; the damper's three places; the
outlet's cap), its reference poses are its own maths, only the engine's shaft and yoke move (the yoke exactly with the
crank pin), the cells are rebuilt from the shipped shape, the stages' counts are measured from the shape, the frame is
the iron frame alone, the brick types map to the game's textures (checked against the game's files when they are at
hand: $VINTAGE_STORY or ~/Games/vintagestory), the textures are the game's own (no new texture), only what burns
glows, the anchors are where the contract puts them (the axle at the back of the east top cell; the outlet under the
chimney's cell, the chimney six blocks from it up; the ash mouth, the fire door and the working door on the front
face), the side faces are plain brick for a row, and the model viewer's scenario (site/models.json) gives the
generator's states. Run with `python3 -m unittest discover -s tools/tests -p test_cupellationfurnace_model.py`.
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
sys.path.insert(0, str(MOD / "CupellationFurnace" / "tools"))

from machinegen import rigmath  # noqa: E402
from machinegen.geometry import aabb_of, flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("cupellationfurnace_make_shape", MOD / "CupellationFurnace" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "cupellationfurnace-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "cupellationfurnace.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "cupellationfurnace_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "CupellationFurnace" / "rig-reference.json").read_text())
MODELS = json.loads((ROOT / "site" / "models.json").read_text())
CONTENTS = ("coal", "fire", "lead", "silver", "stream", "litharge")
STAGES = ("brickwork", "lining", "test", "engine", "fittings")
REQUIRES = {None, *STAGES, "damperopen", "damperhalf", "dampershut", "outletcap", *CONTENTS}
GAME_TEXTURES = {
    "brick": "game:block/clay/brick/four/running/red1",
    "lining": "game:block/clay/refractory/tier3/front1",
    "iron": "game:block/metal/plate/iron",
    "steel": "game:block/metal/sheet-plain/steel1",
    "oak": "game:block/wood/debarked/oak",
    "boneash": "game:item/resource/crushed/lime",
    "lead": "game:block/metal/ingot/lead",
    "silver": "game:block/metal/ingot/silver",
    "litharge": "game:item/resource/crushed/limonite",
    "leather": "game:block/leather/plain",
    "coal": "game:block/coal/bituminous",
    "ember": "game:block/coal/ember",
}
FLAT = flatten(SHAPE["elements"], textures={})
DX = -16.0 * make_shape.ORIGIN_CELL[0]       # the shipped frame is the build frame moved a block west


def matrix(pid, theta=0.0):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta, "travel": abs(theta)})


def posed_part(pid, theta=0.0):
    m = matrix(pid, theta)
    return [rigmath.posed(el, m) for el in FLAT if rigmath.part_of(RIG["parts"], el.name) == pid]


def game_assets():
    for root in (os.environ.get("VINTAGE_STORY"), str(Path.home() / "Games" / "vintagestory")):
        if root and (Path(root) / "assets" / "survival").is_dir():
            return Path(root) / "assets"
    return None


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
        # the engine's shaft and yoke are the only parts that move, and come with the engine stage
        moving = {p["id"]: p for p in RIG["parts"] if p["drivers"]}
        self.assertEqual(set(moving), {"shaft", "yoke"})
        self.assertEqual({p["requires"] for p in moving.values()}, {"engine"})
        self.assertEqual([(d["type"], d["axis"], d.get("ratio")) for d in moving["shaft"]["drivers"]], [("rotate", "z", 1.0)])
        self.assertEqual([(d["type"], d["axis"], d.get("phase")) for d in moving["yoke"]["drivers"]], [("slide", "x", 0.0)])

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
        for pose in poses:
            for pid, want in pose["matrices"].items():
                got = matrix(pid, pose["theta"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {pose['theta']}")

    def test_the_yoke_follows_the_crank_pin(self):
        # an exact Scotch yoke: the pin (under the shaft at theta 0) and the slot's middle line stay together in x
        pivot = next(p for p in RIG["parts"] if p["id"] == "shaft")["drivers"][0]["pivot"]
        r = next(p for p in RIG["parts"] if p["id"] == "yoke")["drivers"][0]["amplitude"]
        for k in range(36):
            th = 2 * math.pi * k / 36 - 1.0
            pin = rigmath.apply(matrix("shaft", th), [pivot[0], pivot[1] - r, 2.5])
            slot = rigmath.apply(matrix("yoke", th), [pivot[0], pivot[1], 2.5])
            self.assertAlmostEqual(pin[0], slot[0], places=12)
            self.assertAlmostEqual(slot[1], pivot[1], places=12)

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])


class Textures(unittest.TestCase):
    def test_only_the_games_own_textures(self):
        self.assertEqual(SHAPE["textures"], GAME_TEXTURES)
        self.assertTrue(all(v.startswith("game:") for v in FRAME["textures"].values()))
        assets = game_assets()
        if assets is None:
            self.skipTest("no game assets ($VINTAGE_STORY or ~/Games/vintagestory)")
        for code, path in GAME_TEXTURES.items():
            self.assertTrue((assets / "survival" / "textures" / (path.split(":", 1)[1] + ".png")).is_file(), code)

    def test_only_what_burns_glows(self):
        glows = {"fire": make_shape.FIRE_GLOW, "lead": make_shape.LEAD_GLOW, "silver": make_shape.SILVER_GLOW,
                 "stream": make_shape.STREAM_GLOW}
        for e in SHAPE["elements"]:
            if not e["faces"]:
                continue
            pid = rigmath.part_of(RIG["parts"], e["name"])
            self.assertEqual({f.get("glow", 0) for f in e["faces"].values()}, {glows.get(pid, 0)}, e["name"])


class Furnace(unittest.TestCase):
    def test_cells_decks_and_anchors(self):
        cells = {tuple(c["pos"]): c for c in RIG["cells"]}
        box = {(x, y, z) for x in (-1, 0, 1) for y in (0, 1) for z in (0, 1, 2)}
        self.assertEqual(set(cells), box)
        self.assertTrue(all(c.get("boxes") and not c.get("hollow") for c in cells.values()))
        self.assertEqual({p for p, c in cells.items() if "lid" in c}, {p for p in box if p[1] == 1})
        # the axle comes in at the back of the east top cell, at its centre, along z: the engine shaft
        self.assertEqual((RIG["powerCell"], RIG["powerFace"]), ([1, 1, 2], "south"))
        pivot = next(p for p in RIG["parts"] if p["id"] == "shaft")["drivers"][0]["pivot"]
        self.assertEqual(pivot[:2], [1.5, 1.5])
        oak = aabb_of([e for e in posed_part("shaft") if e.name.startswith("sh_oak")])
        self.assertAlmostEqual(oak[1][2], 48.0)
        # the outlet in the back row's west column, the chimney's cell over it not the furnace's; six from it up
        self.assertEqual((RIG["outletCell"], RIG["outletFace"]), ([-1, 1, 2], "up"))
        self.assertNotIn((-1, 2, 2), cells)
        line = RIG["chimney"]
        self.assertEqual((line["axis"], line["length"]), ("y", 6.0))
        self.assertAlmostEqual(line["origin"][1] - line["length"] / 2, 2.0)
        self.assertEqual((RIG["furnace"]["minChimney"], RIG["furnace"]["rowMax"], RIG["furnace"]["rowAxis"]), (6, 5, "x"))
        # the hand actions, from the front: the ash mouth's damper, the fire door, the working door, the litharge pot
        for key in ("ash", "fireDoor", "door", "litharge"):
            self.assertEqual(RIG[f"{key}Side"], "north", key)
        for key in ("ash", "fireDoor", "door"):
            self.assertEqual(RIG[key]["pos"][2], 0.0, key)
        # the test's dish and the tuyere on the test's middle line; the tuyere's mouth over the test's back, in the chamber
        self.assertAlmostEqual(RIG["dish"]["pos"][0], RIG["tuyere"]["pos"][0])
        self.assertAlmostEqual(RIG["door"]["pos"][0], RIG["dish"]["pos"][0])
        self.assertGreater(RIG["tuyere"]["pos"][1], RIG["dish"]["pos"][1])
        self.assertGreater(RIG["tuyere"]["pos"][2], RIG["dish"]["pos"][2])

    def test_side_faces_are_plain_brick_for_a_row(self):
        # nothing but brick reaches the side planes, so furnaces stand side by side, walls meeting
        west, east = DX, DX + 16.0 * make_shape.CELLS_X
        for el in FLAT:
            lo, hi = el.aabb()
            if lo[0] < west + 1e-6 or hi[0] > east - 1e-6:
                self.assertTrue(el.name.startswith("bk_brick"), el.name)
        for k in range(12):
            for pid in ("shaft", "yoke"):
                lo, hi = aabb_of(posed_part(pid, 2 * math.pi * k / 12))
                self.assertGreater(lo[0], west)
                self.assertLess(hi[0], east)


def element_volume(e):
    return math.prod(e["to"][k] - e["from"][k] for k in range(3))


class Build(unittest.TestCase):
    def test_the_stages_counts_are_measured_from_the_brick(self):
        # 2 fired bricks a block of brickwork, 4 refractory bricks a block of lining, fire clay one per 2.5 of those,
        # bone meal at the cupel's rate; each rounded up; the shipped faces are stepped in by at most 0.015
        build = RIG["build"]
        self.assertEqual([s["requires"] for s in build["stages"]], list(STAGES))
        st = {s["requires"]: s for s in build["stages"]}
        bk = sum(element_volume(e) for e in SHAPE["elements"] if e["name"].startswith("bk_brick_")) / 4096
        ln = sum(element_volume(e) for e in SHAPE["elements"] if e["name"].startswith("ln_brick_")) / 4096
        self.assertAlmostEqual(bk, st["brickwork"]["volumeBlocks"], delta=0.01)
        self.assertAlmostEqual(ln, st["lining"]["volumeBlocks"], delta=0.01)
        bricks, lining = math.ceil(st["brickwork"]["volumeBlocks"] * 2), math.ceil(st["lining"]["volumeBlocks"] * 4)
        self.assertEqual([(m["code"], m["quantity"]) for m in st["brickwork"]["materials"]], [("game:burnedbrick-*", bricks)])
        self.assertTrue(st["brickwork"]["oneType"])
        self.assertEqual([(m["code"], m["quantity"]) for m in st["lining"]["materials"]],
                         [("game:refractorybrick-fired-tier3", lining), ("game:clay-fire", math.ceil(lining / 2.5))])
        bonemeal = math.ceil(st["test"]["volumeBlocks"] * 4096 * 4 / 41 - 1e-6)
        self.assertEqual([m["code"] for m in st["test"]["materials"]], ["game:bonemeal", "game:metalplate-iron"])
        self.assertLessEqual(abs(st["test"]["materials"][0]["quantity"] - bonemeal), 1)
        self.assertEqual(st["engine"]["alsoParts"], ["shaft", "yoke"])
        self.assertEqual(st["fittings"]["also"], ["damperopen", "damperhalf", "dampershut"])
        self.assertEqual((build["outletCap"]["code"], build["outletCap"]["quantity"]), ("game:metalplate-iron", 1))

    def test_the_frame_is_the_iron_frame(self):
        # the block draws the iron frame alone, standing on its own, with no thin post taller than half the furnace
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
        model = next(m for m in MODELS["models"] if m["id"] == "cupellation-furnace")
        self.assertEqual(model["rig"], "mods-src/seraphhorizons/assets/seraphhorizons/config/cupellationfurnace-rig.json")
        sc = model["scenario"]
        self.assertEqual(set(sc["requires"]), REQUIRES - {None})
        groups = {g["id"]: g for g in sc["states"]}
        self.assertEqual(set(groups), {"build", "work", "damper", "outlet"})
        build = groups["build"]
        self.assertEqual((build["values"], build["default"]), (list(STAGES), "complete"))
        self.assertEqual([(o["id"], tuple(o["fitted"])) for o in build["options"]], list(make_shape.STAGE_STATES))
        self.assertEqual(groups["work"]["needs"], list(STAGES))
        self.assertEqual(groups["damper"]["needs"], list(STAGES))
        self.assertEqual(groups["outlet"]["needs"], ["brickwork", "lining"])
        work = groups["work"]
        self.assertEqual(work["values"], list(CONTENTS))
        got = [(o["id"], o["label"], tuple(o["fitted"])) for o in work["options"]]
        self.assertEqual(got, [(sid, label, tuple(what)) for sid, label, what in make_shape.WORK_STATES])
        self.assertEqual([(o["id"], o["fitted"]) for o in groups["damper"]["options"]],
                         [("open", ["damperopen"]), ("half", ["damperhalf"]), ("shut", ["dampershut"])])
        self.assertEqual([(o["id"], o["fitted"]) for o in groups["outlet"]["options"]], [("open", []), ("capped", ["outletcap"])])
        # the build's labels carry the rig's quantities
        st = {s["requires"]: s for s in RIG["build"]["stages"]}
        labels = {o["id"]: o["label"] for o in build["options"]}
        self.assertIn(f"{st['brickwork']['materials'][0]['quantity']} fired bricks", labels["brickwork"])
        self.assertIn(f"{st['lining']['materials'][0]['quantity']} tier-3 refractory bricks", labels["lining"])
        self.assertIn(f"{st['lining']['materials'][1]['quantity']} fire clay", labels["lining"])
        self.assertIn(f"{st['test']['materials'][0]['quantity']} bone meal", labels["test"])
        # Play turns the axle at the reference speed 0.6 (57 turns an in-game hour of two real minutes)
        self.assertEqual(sc["play"]["phases"], [])
        self.assertAlmostEqual(sc["play"]["secondsPerTurn"], 120 / (95.5 * 0.6), delta=0.05)


if __name__ == "__main__":
    unittest.main()
