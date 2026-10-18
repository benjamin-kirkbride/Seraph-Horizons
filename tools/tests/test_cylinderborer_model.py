"""The cylinder borer's generated model files (mods-src/seraphhorizons/CylinderBorer/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with
the shared rig maths and uses its `requires` vocabulary, its reference poses are its own maths, the cells
are rebuilt from the shipped shape, the work is the cutter head's travel in blocks, the head and the feed
travel together and come back with the cylinder, the bar turns a quarter of the axle, the casting is ppex's
engine cylinder (the figures the generator holds to ppex's shapes on every run) in iron or steel and lies
concentric with the bar, and the oil cups follow the oil; the bushings are drawn in metal parts' cupronickel, the
items and the mold are the generator's, and the mold's clay forming recipe is its voxels. Run with
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
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(MOD / "CylinderBorer" / "tools"))

from machinegen import checks, rigmath  # noqa: E402
from machinegen.geometry import flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("cylinderborer_make_shape", MOD / "CylinderBorer" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "cylinderborer-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "cylinderborer.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "cylinderborer_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "CylinderBorer" / "rig-reference.json").read_text())
# the fitted parts in their order after the frame (CylinderBorer/Core/CylinderBorerParts.cs): the bar's bushings, the
# bar with its great wheel, the weighted feed, the cutter head (the wearing kit); the work is an iron or a steel casting
# of ppex's engine cylinder
REQUIRES = {"bushings", "bar", "feed", "cutterhead", "blankiron", "blanksteel", None}
END = 18 / 16


def inputs(pose):
    return {k: pose[k] for k in ("theta", "travel", "work", "size", "presence", "oil")}


def matrix(pid, **ins):
    return rigmath.part_matrix(RIG["parts"], pid, ins, RIG["work"])


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            if p.get("ride"):
                self.assertIn(p["ride"], ids)
            for d in p["drivers"]:
                rigmath.validate_driver(d)
            # the renderer draws a part with a requires, a ride or a driver; anything else must be the frame
            if p["id"] != "frame":
                self.assertTrue(p["requires"] or p.get("ride") or p["drivers"], p["id"])
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_every_face_uses_a_declared_code(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertLessEqual(used, set(shape["textures"]))
            for code, path in shape["textures"].items():
                self.assertTrue(path.startswith("game:"), code)

    def test_every_texture_exists_in_the_game(self):
        install = os.environ.get("VINTAGE_STORY")
        if not install:
            raise unittest.SkipTest("VINTAGE_STORY is not set to a game or server install")
        assets = Path(install) / "assets"
        for code, path in SHAPE["textures"].items():
            domain, rel = path.split(":", 1)
            self.assertEqual(domain, "game", code)
            self.assertTrue((assets / "survival" / "textures" / f"{rel}.png").is_file(), f"{code}: {path}")

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreater(len(poses), 40)
        self.assertEqual({p["size"] for p in poses}, {0, 1, 2})
        for pose in poses[::3]:
            for pid, want in pose["matrices"].items():
                got = rigmath.part_matrix(RIG["parts"], pid, inputs(pose), RIG["work"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {inputs(pose)}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        cells = make_shape.shipped_cells(SHAPE, RIG["parts"], RIG["work"])
        self.assertEqual(cells, RIG["cells"])


class Bore(unittest.TestCase):
    def test_work_is_the_heads_travel(self):
        self.assertNotIn("trunkPath", RIG)
        w = rigmath.progress_of(RIG)
        self.assertEqual((w["name"], w["unit"]), ("cutter travel", "blocks"))
        self.assertEqual(w["end"], {"thin": END, "thick": END})
        self.assertEqual(set(RIG["bore"]["turnsPerBlock"]), {"thin", "thick"})
        self.assertGreater(RIG["bore"]["turnsPerBlock"]["thick"], RIG["bore"]["turnsPerBlock"]["thin"])   # steel cuts slower
        for key in ("blanks", "cylinders"):
            self.assertEqual(set(RIG["bore"][key]), {"thin", "thick"})
        self.assertEqual(len(RIG["bore"]["box"]), 6)
        # the gameplay's items (CylinderBorer/Core/Boring.cs) and its settings' pace (CylinderBorerConfig)
        self.assertEqual(RIG["bore"]["blanks"], {"thin": "seraphhorizons:cylinderblank-iron", "thick": "seraphhorizons:cylinderblank-steel"})
        self.assertEqual(RIG["bore"]["cylinders"], {"thin": "seraphhorizons:cylinder-iron", "thick": "seraphhorizons:cylinder-steel"})
        self.assertEqual(RIG["bore"]["turnsPerBlock"], {"thin": 64.0, "thick": 96.0})

    def test_the_casting_is_ppexs_engine_cylinder(self):
        # the figures the generator holds to ppex's engine shapes on every run (make_shape.measure_ppex)
        self.assertEqual(RIG["bore"]["cylinder"], {"outside": 10.0, "bore": 8.0, "length": 16.0})
        self.assertEqual((make_shape.R_OUT * 2, make_shape.R_BORE * 2, make_shape.CYL_Z[1] - make_shape.CYL_Z[0]), (10.0, 8.0, 16.0))
        els = flatten(SHAPE["elements"], textures={})
        named = {e["name"]: e for e in SHAPE["elements"]}
        for t, tex in (("i", "castiron"), ("s", "caststeel")):
            walls = [e for e in els if e.name.startswith(f"bl{t}_wall")]
            # square outside, as ppex draws it: 10 x 10 round the bar, 16 long
            xs = [p[0] for e in walls for p in e.corners()]
            ys = [p[1] for e in walls for p in e.corners()]
            zs = [p[2] for e in walls for p in e.corners()]
            self.assertAlmostEqual(max(xs) - min(xs), 10.0, places=3)
            self.assertAlmostEqual(max(ys) - min(ys), 10.0, places=3)
            self.assertAlmostEqual(max(zs) - min(zs), 16.0, places=2)
            # the bore round: twelve bright flats, 4 from the bar's axis, a 12-gon
            angles = set()
            for e in walls:
                for d, f in named[e.name]["faces"].items():
                    if f["texture"] != "#bored":
                        continue
                    k, sgn = {"east": (0, 1), "west": (0, -1), "up": (1, 1), "down": (1, -1)}[d]
                    n = [sgn * e.r[i][k] for i in range(3)]
                    centre = [e.c[i] + n[i] * abs(e.size[k]) / 2 for i in range(3)]
                    self.assertAlmostEqual(-sum((centre[i] - make_shape.BAR[i]) * n[i] for i in range(2)), 4.0, places=3, msg=e.name)
                    angles.add(round(math.degrees(math.atan2(-n[0], -n[1])) % 360, 1))
            self.assertEqual(sorted(angles), [30.0 * i for i in range(12)])
            # ppex's Watt cylinder is iron5, its Cornish's steel5: each casting wears its metal's
            faces = {f["texture"] for e in SHAPE["elements"] if e["name"].startswith(f"bl{t}_") for f in e["faces"].values()}
            self.assertEqual(faces - {"#bored"}, {f"#{tex}"})
        self.assertEqual(SHAPE["textures"]["castiron"], "game:block/metal/sheet-plain/iron5")
        self.assertEqual(SHAPE["textures"]["caststeel"], "game:block/metal/sheet-plain/steel5")

    def test_the_head_and_the_feed_travel_together_and_come_back_with_the_cylinder(self):
        for k in (1, 2):
            for w in (0.0, 0.3, END / 2, END):
                for pid in ("head", "feed"):
                    self.assertAlmostEqual(matrix(pid, work=w, size=k, presence=1.0)[2][3], -w, places=6, msg=f"{pid} {k} {w}")
            # the cylinder taken away at its end: the head is pushed back as p eases out
            for p in (0.5, 0.0):
                self.assertAlmostEqual(matrix("head", work=END, size=k, presence=p)[2][3], -END * p, places=6)

    def test_the_drum_and_the_weight_follow_the_head(self):
        for k in (1, 2):
            m = matrix("drum", work=END, size=k, presence=1.0)
            turned = -math.atan2(m[2][1], m[1][1])
            self.assertAlmostEqual(math.remainder(turned * make_shape.R_DRUM - END * 16, 2 * math.pi * make_shape.R_DRUM), 0.0, places=3)
            fall = -matrix("weight", work=END, size=k, presence=1.0)[1][3] * 16
            self.assertAlmostEqual(fall, END * 16 * make_shape.R_WDRUM / make_shape.R_DRUM, places=3)

    def test_the_bar_turns_a_quarter_of_the_axle_and_the_head_with_it(self):
        for th in (0.7, -2.1):
            for pid in ("bar", "head"):
                m = matrix(pid, theta=th)
                self.assertAlmostEqual(math.atan2(m[1][0], m[0][0]), math.remainder(-th / 4, 2 * math.pi), places=5, msg=pid)
        self.assertEqual((make_shape.PINION_T, make_shape.WHEEL_T), (8, 32))
        d = math.hypot(make_shape.BAR[0] - make_shape.ENTRY[0], make_shape.BAR[1] - make_shape.ENTRY[1])
        self.assertAlmostEqual(d, make_shape.R_PINION + make_shape.R_WHEEL)

    def test_every_skin_slice_is_cut_by_the_end(self):
        # each quarter of the cast skin has sunk into the wall once the cutters are past it, for either metal
        for k, t in ((1, "i"), (2, "s")):
            for j in range(1, make_shape.SKIN_SLICES + 1):
                for q in "tlbr":
                    pid = f"s{t}{j}{q}"
                    rest = matrix(pid, work=0.0, size=k, presence=1.0)
                    done = matrix(pid, work=END, size=k, presence=1.0)
                    self.assertAlmostEqual(max(abs(rest[r][3]) for r in range(3)), 0.0, places=9, msg=pid)
                    self.assertAlmostEqual(max(abs(done[r][3]) for r in range(3)) * 16, make_shape.SKIN_SINK, places=4, msg=pid)

    def test_the_casting_lies_concentric_with_the_bar(self):
        els = flatten(SHAPE["elements"], textures={})
        for t in ("i", "s"):
            walls = [e for e in els if e.name.startswith(f"bl{t}_wall")]
            xs = [p[0] for e in walls for p in e.corners()]
            ys = [p[1] for e in walls for p in e.corners()]
            self.assertAlmostEqual((min(xs) + max(xs)) / 2, make_shape.BAR[0], places=3)
            self.assertAlmostEqual((min(ys) + max(ys)) / 2, make_shape.BAR[1], places=3)


class Anchors(unittest.TestCase):
    def test_cells_and_faces(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        self.assertEqual(cells, {(0, y, z) for y in range(2) for z in range(4)})
        self.assertEqual(checks.lid_gaps(RIG["cells"]), [])
        self.assertEqual(tuple(RIG["powerCell"]), (0, 0, 3))
        self.assertEqual(RIG["powerFace"], "south")
        self.assertEqual((RIG["infeedSide"], RIG["outputSide"]), ("east", "west"))
        for key in ("output", "chips", "drip"):
            self.assertEqual(len(RIG[key]["pos"]), 3)
        self.assertNotIn("oil", RIG)        # the oil is MachineOil's tank: no fill anchor

    def test_the_oil_level_follows_the_oil_input(self):
        (lvl,) = [p for p in RIG["parts"] if p["id"] == "oillevel"]
        self.assertEqual([d["input"] for d in lvl["drivers"]], ["oil"])
        heights = [matrix("oillevel", oil=oil)[1][1] * lvl["drivers"][0]["length"] * 16 for oil in (0.0, 0.5, 1.0)]
        self.assertAlmostEqual(heights[0], make_shape.OIL_EMPTY, places=5)
        self.assertAlmostEqual(heights[2], make_shape.OIL_FULL, places=5)
        self.assertAlmostEqual(heights[1], (heights[0] + heights[2]) / 2, places=5)


ITEMS = MOD / "assets" / "seraphhorizons" / "shapes" / "item" / "cylinderborer"
MOLD = MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "clay" / "mold" / "cylinder.json"
MOLD_RECIPE = MOD / "assets" / "seraphhorizons" / "recipes" / "clayforming" / "cylindermold.json"
MOLD_FIRED = MOD / "assets" / "seraphhorizons" / "blocktypes" / "clay" / "cylindermold-fired.json"


def lenient(path):
    """An asset file the game reads: comments and unquoted keys (the gear consumers test's reader)."""
    sys.path.insert(0, str(ROOT / "tools" / "tests"))
    from test_gear_consumers import loads
    return loads(path.read_text())


class Bushings(unittest.TestCase):
    def test_the_bushings_are_drawn_in_metal_parts_cupronickel(self):
        cupronickel = "game:block/metal/sheet/cupronickel1"
        self.assertEqual(SHAPE["textures"]["cupronickel"], cupronickel)
        bush = [e for e in SHAPE["elements"] if rigmath.part_of(RIG["parts"], e["name"]) == "bushings"]
        self.assertEqual(sorted(e["name"] for e in bush), ["bush_drive", "bush_far"])
        for e in bush:
            self.assertEqual({f["texture"] for f in e["faces"].values()}, {"#cupronickel"}, e["name"])
        others = [e["name"] for e in SHAPE["elements"] if e not in bush and any(f["texture"] == "#cupronickel" for f in e["faces"].values())]
        self.assertEqual(others, [])

    def test_the_bushings_stage_takes_the_games_metal_parts_not_an_item_of_its_own(self):
        self.assertFalse((ITEMS / "bushings.json").exists())
        self.assertFalse((MOD / "assets" / "seraphhorizons" / "itemtypes" / "cylinderborer" / "bushings.json").exists())

    def test_the_bar_and_the_feed_take_their_materials_at_the_frame_not_items_of_their_own(self):
        for name in ("bar", "feed"):
            with self.subTest(stage=name):
                self.assertFalse((ITEMS / f"{name}.json").exists())
                self.assertFalse((MOD / "assets" / "seraphhorizons" / "itemtypes" / "cylinderborer" / f"{name}.json").exists())
        recipes = (MOD / "assets" / "seraphhorizons" / "recipes" / "grid" / "cylinderborer.json").read_text()
        self.assertNotIn("cylinderborerbar", recipes)
        self.assertNotIn("cylinderborerfeed", recipes)


class Items(unittest.TestCase):
    def test_each_item_shape_sits_in_the_item_box(self):
        for name in ("cutterhead", "cylinderblank", "cylinder"):
            with self.subTest(item=name):
                shape = json.loads((ITEMS / f"{name}.json").read_text())
                els = flatten(shape["elements"], textures={})
                corners = [q for el in els for q in el.corners()]
                lo = [min(q[i] for q in corners) for i in range(3)]
                hi = [max(q[i] for q in corners) for i in range(3)]
                self.assertGreaterEqual(min(lo), -1e-3)
                self.assertLessEqual(max(hi), 16 + 1e-3)
                self.assertAlmostEqual(lo[1], 0, places=3)
                used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
                self.assertLessEqual(used, set(shape["textures"]))

    def test_the_casting_items_take_their_metal_from_the_item_type(self):
        for name in ("cylinderblank", "cylinder"):
            shape = json.loads((ITEMS / f"{name}.json").read_text())
            self.assertIn("cast", shape["textures"])
            self.assertNotIn("castiron", shape["textures"])
            itemtype = lenient(MOD / "assets" / "seraphhorizons" / "itemtypes" / f"{name}.json")
            self.assertEqual(itemtype["textures"]["cast"]["base"], "game:block/metal/sheet-plain/{metal}5")
            self.assertEqual(itemtype["variantgroups"][0]["states"], ["iron", "steel"])


class Mold(unittest.TestCase):
    def test_the_clay_forming_recipe_is_the_molds_voxels(self):
        recipe = lenient(MOLD_RECIPE)[0]
        (x0, x1), (z0, z1) = make_shape.MOLD["x"], make_shape.MOLD["z"]
        voxels = {(x0 + x, y, z0 + z) for y, layer in enumerate(recipe["pattern"]) for z, row in enumerate(layer)
                  for x, c in enumerate(row) if c == "#"}
        self.assertEqual(voxels, make_shape.mold_voxels())
        # the pattern is centred on the 16 grid as the game centres it: x 1..15 (14 wide), z 0..16
        self.assertEqual({len(row) for layer in recipe["pattern"] for row in layer}, {x1 - x0})
        self.assertEqual({len(layer) for layer in recipe["pattern"]}, {z1 - z0})
        self.assertEqual(recipe["output"]["code"], "seraphhorizons:cylindermold-{color}-raw")

    def test_the_shape_is_the_generators_and_the_fired_mold_casts_six_ingots(self):
        shape = json.loads(MOLD.read_text())
        self.assertEqual(shape["elements"], make_shape.mold_shape()["elements"])
        fired = lenient(MOLD_FIRED)
        self.assertEqual((fired["code"], fired["class"], fired["entityClass"]), ("cylindermold", "BlockToolMold", "ToolMold"))
        a = fired["attributes"]
        self.assertEqual(a["requiredUnits"], 600)
        self.assertEqual(a["fillHeight"], make_shape.MOLD["top"] - make_shape.MOLD["floor"])
        self.assertEqual(a["fillQuadsByLevel"], make_shape.mold_cavity())
        self.assertEqual(a["drop"], {"type": "item", "code": "seraphhorizons:cylinderblank-{metal}"})
        self.assertNotIn("breaksWhenFilled", a)


if __name__ == "__main__":
    unittest.main()
