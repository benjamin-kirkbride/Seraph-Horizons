"""The stall roaster's generated model files (mods-src/seraphhorizons/StallRoaster/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared rig
maths and uses the `requires` vocabulary (the build's stages, brickwork and lining; the bed, cold or burning; the
charge's four courses cold, burning or roasted; the sulfur; the front's walling, half or whole; the outlet's cap),
nothing moves (every reference matrix is the identity), the stages' counts are measured from the shape's brick, the
brick types map to the game's textures (checked against the game's files when they are at hand: $VINTAGE_STORY or
~/Games/vintagestory), the cells are rebuilt from the shipped shape, the side faces are plain brick for a row, the
textures are the game's own (no new texture), only the burning bed and the burning charge glow, the anchors are where
the contract puts them (no power cell, no chute; the outlet under the chimney's cell, the chimney six blocks from it
up; the front in the opening's middle), and the model viewer's scenario (site/models.json) gives the generator's
states. Run with `python3 -m unittest discover -s tools/tests -p test_stallroaster_model.py`.
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
sys.path.insert(0, str(MOD / "StallRoaster" / "tools"))

from machinegen import rigmath  # noqa: E402
from machinegen.geometry import aabb_of, flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("stallroaster_make_shape", MOD / "StallRoaster" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "stallroaster-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "stallroaster.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "stallroaster_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "StallRoaster" / "rig-reference.json").read_text())
MODELS = json.loads((ROOT / "site" / "models.json").read_text())
COURSES = tuple(f"{k}{j}" for k in ("ore", "fire", "roasted") for j in (1, 2, 3, 4))
CONTENTS = ("wood", "kindling") + COURSES + ("sulfur",)
STAGES = ("brickwork", "lining")
REQUIRES = {None, *STAGES, *CONTENTS, "fronthalf", "frontwalled", "outletcap"}
GAME_TEXTURES = {
    "brick": "game:block/clay/brick/four/running/red1",
    "lining": "game:block/clay/refractory/tier3/front1",
    "iron": "game:block/metal/plate/iron",
    "wood": "game:block/wood/firepit/log",
    "ore": "game:item/resource/crushed/galena",
    "ember": "game:block/coal/ember",
    "roasted": "game:item/resource/crushed/hematite",
    "sulfur": "game:item/resource/crushed/sulfur",
}
FLAT = flatten(SHAPE["elements"], textures={})
IDENTITY = [[1.0, 0.0, 0.0, 0.0], [0.0, 1.0, 0.0, 0.0], [0.0, 0.0, 1.0, 0.0]]


def part(pid):
    return [el for el in FLAT if rigmath.part_of(RIG["parts"], el.name) == pid]


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            self.assertEqual((p["drivers"], p["ride"]), ([], None), p["id"])     # nothing moves
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        self.assertEqual({p["id"]: p["requires"] for p in RIG["parts"]}["frame"], None)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_reference_poses_are_the_identity(self):
        poses = REFERENCE["poses"]
        self.assertGreaterEqual(len(poses), 3)
        for pose in poses:
            self.assertEqual(set(pose["matrices"]), {p["id"] for p in RIG["parts"]})
            for pid, want in pose["matrices"].items():
                got = rigmath.part_matrix(RIG["parts"], pid, {"theta": pose["theta"], "depth": pose["depth"]})
                self.assertEqual([[round(v, 6) + 0.0 for v in r] for r in got[:3]], want, pid)
                self.assertEqual(want, IDENTITY, pid)

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])


class Textures(unittest.TestCase):
    def test_only_the_games_own_textures(self):
        self.assertEqual(SHAPE["textures"], GAME_TEXTURES)
        self.assertTrue(all(v.startswith("game:") for v in FRAME["textures"].values()))

    def test_only_the_burning_bed_and_the_burning_charge_glow(self):
        for e in SHAPE["elements"]:
            pid = rigmath.part_of(RIG["parts"], e["name"])
            glow = {f.get("glow", 0) for f in e["faces"].values()}
            if pid == "kindling":
                self.assertEqual(glow, {make_shape.KINDLING_GLOW}, e["name"])
            elif re.fullmatch(r"fire\d", pid):
                self.assertEqual(glow, {make_shape.HEAP_GLOW}, e["name"])
            else:
                self.assertEqual(glow, {0}, e["name"])


class Stall(unittest.TestCase):
    def test_cells_decks_and_anchors(self):
        cells = {tuple(c["pos"]): c for c in RIG["cells"]}
        box = {(x, y, z) for x in (0, 1) for y in (0, 1) for z in (0, 1)}
        self.assertEqual(set(cells), box)
        self.assertTrue(all(c.get("boxes") and not c.get("hollow") for c in cells.values()))
        self.assertEqual({p for p, c in cells.items() if c.get("lid") == 1.0}, {p for p in box if p[1] == 1})
        # no power, no chute: batch, by hand
        for key in ("powerCell", "powerFace", "airCell", "airFace", "infeedCell", "outputCell", "work", "trunkPath"):
            self.assertNotIn(key, RIG)
        # the front in the middle of the opening, on the front face
        self.assertEqual((RIG["frontSide"], RIG["front"]["pos"][0], RIG["front"]["pos"][2]), ("north", 1.0, 0.0))
        # the outlet in the back row of the controller's column, the chimney's cell over it and its neighbours and the
        # cells over the bay not the stall's; six from the outlet up
        self.assertEqual((RIG["outletCell"], RIG["outletFace"]), ([0, 1, 1], "up"))
        for c in ((0, 2, 1), (1, 2, 1), (-1, 2, 1), (0, 2, 0), (1, 2, 0)):
            self.assertNotIn(c, cells)
        line = RIG["chimney"]
        self.assertEqual((line["axis"], line["length"]), ("y", 6.0))
        self.assertAlmostEqual(line["origin"][1] - line["length"] / 2, 2.0)
        self.assertTrue(0.0 <= line["origin"][0] <= 1.0 and 1.0 <= line["origin"][2] <= 2.0)
        fur = RIG["furnace"]
        self.assertEqual((fur["charge"], fur["courses"], fur["perCourse"], fur["minChimney"], fur["rowMax"], fur["rowAxis"]),
                         (32, 4, 8, 6, 6, "x"))

    def test_nothing_leaves_the_eight_cells(self):
        lo, hi = aabb_of(FLAT)
        self.assertTrue(all(lo[k] >= -1e-6 and hi[k] <= 32.0 + 1e-6 for k in range(3)), (lo, hi))

    def test_side_faces_are_brick_and_flush_angles_for_a_row(self):
        # only the brickwork and the corner angles' legs (and the angle bands' ends) reach a side plane, flush
        for el in FLAT:
            lo, hi = el.aabb()
            if lo[0] < 1e-6 or hi[0] > 32.0 - 1e-6:
                self.assertTrue(el.name.startswith(("bk_brick", "fr_corner_", "fr_band_")), el.name)
                self.assertTrue(lo[0] >= -1e-6 and hi[0] <= 32.0 + 1e-6, el.name)
                faces = next(e for e in SHAPE["elements"] if e["name"] == el.name)["faces"]
                want = {"#iron"} if el.name.startswith("fr_") else {"#brick"}
                self.assertEqual({f["texture"] for f in faces.values()}, want)

    def test_every_binder_is_an_angle_wrapping_a_corner(self):
        # owner's rule: the iron is angle iron, an L of two legs IRON_T thick, LEG wide, one on each face of a corner
        frame = part("frame")
        legs = {}
        for e in frame:
            stem = re.sub(r"\d+$", "", e.name)
            self.assertRegex(stem, r"^fr_(corner_(nw|ne|sw|se)|jamb_[we]|sill|band_[ns])_[ab]$")
            legs.setdefault(stem, []).append(e)
        for name, *_ in make_shape.ANGLES:
            a, b = aabb_of(legs[f"{name}_a"]), aabb_of(legs[f"{name}_b"])
            for lo, hi in (a, b):
                cross = sorted(hi[k] - lo[k] for k in range(3))[:2]
                self.assertAlmostEqual(cross[0], make_shape.IRON_T, msg=name)
                self.assertGreaterEqual(cross[1] + 1e-9, make_shape.LEG - make_shape.IRON_T, name)
            # the legs meet: they share a face along the angle's length, at right angles to each other
            ov = [min(a[1][k], b[1][k]) - max(a[0][k], b[0][k]) for k in range(3)]
            self.assertTrue(min(ov) > -1e-6 and sum(o > 0.05 for o in ov) == 2, name)

    def test_the_charge_lies_in_the_bay_in_four_courses(self):
        bay_x, bay_z = make_shape.BAY_X, make_shape.BAY_Z
        for j in (1, 2, 3, 4):
            lo, hi = aabb_of([e for e in part(f"ore{j}") if e.name.endswith("_course")])
            self.assertTrue(bay_x[0] <= lo[0] and hi[0] <= bay_x[1] and bay_z[0] <= lo[2] and hi[2] <= bay_z[1], j)
            for kind in ("fire", "roasted"):
                twin = aabb_of([e for e in part(f"{kind}{j}") if e.name.endswith("_course")])
                self.assertEqual([[round(v, 3) for v in c] for c in twin], [[round(v, 3) for v in c] for c in (lo, hi)])
        wood = aabb_of(part("wood"))
        self.assertAlmostEqual(wood[0][1], make_shape.FLOOR_Y)
        self.assertGreater(wood[0][2], aabb_of(part("frontwalled"))[1][2])           # behind the walling

    def test_the_walling_stands_in_the_opening(self):
        tie = aabb_of([e for e in part("frame") if e.name.startswith("fr_band_n")])
        for place, courses in (("half", 3), ("walled", 6)):
            els = part(f"front{place}")
            lo, hi = aabb_of(els)
            self.assertAlmostEqual(lo[1], make_shape.FLOOR_Y)
            self.assertAlmostEqual(hi[1] - lo[1], 4.0 * courses)
            self.assertLess(hi[1], tie[0][1])
            self.assertTrue(make_shape.BAY_X[0] <= lo[0] and hi[0] <= make_shape.BAY_X[1])
            self.assertEqual(len(els), 3 * courses)


def game_assets():
    for root in (os.environ.get("VINTAGE_STORY"), str(Path.home() / "Games" / "vintagestory")):
        if root and (Path(root) / "assets" / "survival").is_dir():
            return Path(root) / "assets"
    return None


def element_volume(e):
    return math.prod(e["to"][k] - e["from"][k] for k in range(3))


class Build(unittest.TestCase):
    def test_the_stages_counts_are_measured_from_the_brick(self):
        # 2 fired bricks a block of brickwork, 4 refractory bricks a block of lining, fire clay one per 2.5 of those,
        # each rounded up; the shipped shape's faces are stepped in by at most 0.015
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
        self.assertEqual([(m["code"], m["quantity"]) for m in build["frame"]["ingredients"]], list(make_shape.FRAME_RECIPE))
        self.assertEqual((build["outletCap"]["code"], build["outletCap"]["quantity"]), ("game:metalplate-iron", 1))
        front = build["front"]
        self.assertEqual(front["places"], ["fronthalf", "frontwalled"])
        self.assertEqual(front["materials"], [{"code": "game:burnedbrick-*", "quantity": len(part("frontwalled"))}])
        self.assertEqual(front["half"], len(part("fronthalf")))

    def test_the_frame_is_the_iron_frame(self):
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
        for code in list(GAME_TEXTURES.values()) + [c for d in bricks["types"].values() for c in [d["base"]] + d.get("overlays", [])]:
            self.assertTrue((assets / "survival" / "textures" / (code.split(":", 1)[1] + ".png")).is_file(), code)
        text = (assets / "survival" / "itemtypes" / "resource" / "burnedbrick.json").read_text()
        states = re.search(r'code:\s*"type",\s*states:\s*\[([^\]]*)\]', text).group(1)
        self.assertEqual(sorted(re.findall(r'"([^"]+)"', states)), sorted(bricks["types"]))


class Scenario(unittest.TestCase):
    def test_the_viewer_gives_the_generators_states(self):
        model = next(m for m in MODELS["models"] if m["id"] == "stall-roaster")
        self.assertEqual(model["rig"], "mods-src/seraphhorizons/assets/seraphhorizons/config/stallroaster-rig.json")
        sc = model["scenario"]
        self.assertEqual(set(sc["requires"]), REQUIRES - {None})
        self.assertNotIn("play", sc)
        groups = {g["id"]: g for g in sc["states"]}
        self.assertEqual(list(groups), ["build", "heap", "front", "outlet"])
        build = groups["build"]
        self.assertEqual((build["values"], build["default"]), (list(STAGES), "complete"))
        self.assertEqual([(o["id"], tuple(o["fitted"])) for o in build["options"]], list(make_shape.STAGE_STATES))
        for g in ("heap", "front", "outlet"):
            self.assertEqual(groups[g]["needs"], list(STAGES))
        heap = groups["heap"]
        self.assertEqual(heap["values"], list(CONTENTS))
        got = [(o["id"], o["label"], tuple(o["fitted"])) for o in heap["options"]]
        self.assertEqual(got, [(sid, label, tuple(what)) for sid, label, what in make_shape.HEAP_STATES])
        self.assertEqual([(o["id"], o["fitted"]) for o in groups["front"]["options"]],
                         [("open", []), ("half", ["fronthalf"]), ("walled", ["frontwalled"])])
        self.assertEqual([(o["id"], o["fitted"]) for o in groups["outlet"]["options"]], [("open", []), ("capped", ["outletcap"])])
        # the build's labels name the measured counts
        st = {s["requires"]: s for s in RIG["build"]["stages"]}
        self.assertIn(f"({st['brickwork']['materials'][0]['quantity']} fired bricks", build["options"][1]["label"])
        self.assertIn(f"({st['lining']['materials'][0]['quantity']} tier-3 refractory bricks, "
                      f"{st['lining']['materials'][1]['quantity']} fire clay)", build["options"][2]["label"])
        self.assertIn(f"({len(part('frontwalled'))} loose bricks)", sc["requires"]["frontwalled"])
        self.assertIn(f"({len(part('fronthalf'))} loose bricks)", sc["requires"]["fronthalf"])


if __name__ == "__main__":
    unittest.main()
