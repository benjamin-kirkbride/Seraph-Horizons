"""The acid parting vessel's generated model files (mods-src/seraphhorizons/AcidParting/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared rig
maths and uses the `requires` vocabulary (the build's stages, brickwork, lining and fittings; the damper's three
places; the fire laid and burning; the vessel's contents; the precipitating tub's; the outlet's cap), its reference
poses are its own maths, the stages' counts are measured from the shape's brick, the brick
types map to the game's textures (checked against the game's files when they are at hand: $VINTAGE_STORY or
~/Games/vintagestory), the cells are rebuilt from the shipped shape, the sliding fire door is the only part that moves
(in its recess, shut over its opening, open clear of it), the precipitating tub stands in front of the vessel on the
cold bench with the siphon from the vessel into it, there is no water line (watered from a bucket), the binding is
angle iron wrapping the corners and a ring of top angles whose ends meet them, one connected frame, the textures are the game's own (no new texture), only the burning fuel glows, the
anchors are where the contract puts them (no power cell, no air port, no chute, no water anchor; the vessel and the tub
worked from the front; the outlet square and centred under the chimney's cell, the chimney six blocks from it up), the
side faces are plain brick but for the angles' legs, for a row, and the model viewer's scenario (site/models.json) gives the generator's states.
Run with `python3 -m unittest discover -s tools/tests -p test_acidparting_model.py`.
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
sys.path.insert(0, str(MOD / "AcidParting" / "tools"))

from machinegen import rigmath  # noqa: E402
from machinegen.geometry import aabb_of, flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("acidparting_make_shape", MOD / "AcidParting" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "acidparting-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "acidparting.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "acidparting_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "AcidParting" / "rig-reference.json").read_text())
MODELS = json.loads((ROOT / "site" / "models.json").read_text())
STAGES = ("brickwork", "lining", "fittings")
CONTENTS = ("charge", "acid", "solution", "gold", "wash", "copper", "liquor", "silver")
REQUIRES = {None, *STAGES, "damperopen", "damperhalf", "dampershut", "fuel", "fire", *CONTENTS, "outletcap"}
FLAT = flatten(SHAPE["elements"], textures={})


def game_assets():
    for root in (os.environ.get("VINTAGE_STORY"), str(Path.home() / "Games" / "vintagestory")):
        if root and (Path(root) / "assets" / "survival").is_dir():
            return Path(root) / "assets"
    return None


def matrix(pid, **ins):
    return rigmath.part_matrix(RIG["parts"], pid, ins)


def posed_part(pid, **ins):
    m = matrix(pid, **ins)
    return [rigmath.posed(el, m) for el in FLAT if rigmath.part_of(RIG["parts"], el.name) == pid]


def named(pid, prefix):
    return [el for el in FLAT if rigmath.part_of(RIG["parts"], el.name) == pid and el.name.startswith(prefix)]


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
        # the fire door is a fitting and the only part that moves: a ratio-0 clock, then a step slide down
        moving = {p["id"] for p in RIG["parts"] if p["drivers"]}
        self.assertEqual(moving, {"door"})
        req = {p["id"]: p["requires"] for p in RIG["parts"]}
        self.assertEqual((req["hoodiron"], req["bath"], req["vessel"], req["tub"], req["siphon"], req["door"], req["frame"]),
                         ("brickwork", "fittings", "fittings", "fittings", "fittings", "fittings", None))
        door = next(p for p in RIG["parts"] if p["id"] == "door")
        self.assertEqual([(d["type"], d.get("motion"), d.get("ratio")) for d in door["drivers"]],
                         [("rotate", None, 0.0), ("step", "slide", None)])

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
        self.assertTrue(all(v.startswith("game:") for v in SHAPE["textures"].values()))
        self.assertEqual(SHAPE["textures"], {k: v for k, v in make_shape.TEXTURES.items() if k in SHAPE["textures"]})
        self.assertEqual(set(FRAME["textures"]), {"iron"})
        assets = game_assets()
        if assets is None:
            self.skipTest("no game assets ($VINTAGE_STORY or ~/Games/vintagestory)")
        for code, path in SHAPE["textures"].items():
            self.assertTrue((assets / "survival" / "textures" / (path.split(":", 1)[1] + ".png")).is_file(), (code, path))

    def test_only_the_burning_fuel_glows(self):
        for e in SHAPE["elements"]:
            pid = rigmath.part_of(RIG["parts"], e["name"])
            glow = {f.get("glow", 0) for f in e["faces"].values()}
            if not glow:
                continue                                  # a brick box wholly inside the wall draws no face
            self.assertEqual(glow, {make_shape.FIRE_GLOW} if pid == "fire" else {0}, e["name"])


class FireDoor(unittest.TestCase):
    def test_slides_in_its_recess_from_open_to_shut(self):
        (dx0, dx1), (dy0, dy1) = make_shape.DOOR_HOLE
        (_, (ry0, ry1), (rz0, rz1)) = make_shape.RECESS
        for depth in [i / 20 for i in range(21)]:
            lo, hi = aabb_of(posed_part("door", theta=0.0, depth=depth))
            self.assertGreaterEqual(lo[1], ry0 - 1e-6)
            self.assertLessEqual(hi[1], ry1 + 1e-6)
            self.assertGreaterEqual(lo[2], rz0 - 1e-6)
            self.assertLessEqual(hi[2], rz1 + 1e-6)
        plate = lambda d: next(e for e in posed_part("door", theta=0.0, depth=d) if e.name == "door_plate").aabb()
        shut, open_ = plate(1.0), plate(0.0)
        self.assertTrue(shut[0][0] < dx0 and shut[1][0] > dx1 and shut[0][1] < dy0 and shut[1][1] > dy1)
        self.assertGreaterEqual(open_[0][1], dy1)

    def test_theta_moves_nothing_and_depth_only_the_door(self):
        for p in RIG["parts"]:
            for depth in (0.0, 0.6, 1.0):
                a, b = matrix(p["id"], theta=0.0, depth=depth), matrix(p["id"], theta=2.7, depth=depth)
                self.assertEqual([[round(v, 9) for v in r] for r in a], [[round(v, 9) for v in r] for r in b], p["id"])
                if p["id"] != "door":
                    self.assertEqual([[round(v, 9) for v in r] for r in a[:3]], [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0]], p["id"])


class Tub(unittest.TestCase):
    def test_in_front_of_the_vessel_with_the_siphon_between(self):
        tub = aabb_of(named("tub", "tub_"))
        vessel = aabb_of(named("vessel", "vessel_"))
        self.assertLess(tub[1][2], vessel[0][2])
        self.assertLess(tub[1][1], vessel[1][1])
        self.assertAlmostEqual(tub[0][1], 16.0)
        legs = {e.name: e.aabb() for e in named("siphon", "siphon_")}
        vleg, tleg = legs["siphon_vesselleg"], legs["siphon_tubleg"]
        self.assertTrue(vessel[0][2] < vleg[0][2] and vleg[1][2] < vessel[1][2])
        self.assertTrue(tub[0][2] < tleg[0][2] and tleg[1][2] < tub[1][2])
        self.assertLess(tleg[0][1], vleg[1][1])
        sludge = aabb_of(named("silver", "silver_"))
        self.assertTrue(tub[0][2] < sludge[0][2] and sludge[1][2] < tub[1][2] and sludge[0][1] < tleg[0][1])
        self.assertEqual(len(named("copper", "copper_plate")), 3)

    def test_the_tub_is_the_silvers_draw_point_and_there_is_no_water_line(self):
        self.assertEqual((RIG["tubCell"], RIG["tubFace"]), ([0, 1, 0], "north"))
        self.assertEqual((RIG["vesselCell"], RIG["vesselFace"]), ([0, 1, 0], "north"))
        self.assertLess(RIG["tub"]["pos"][2], RIG["vessel"]["pos"][2])
        for key in ("waterCell", "waterFace", "cock"):
            self.assertNotIn(key, RIG)
        self.assertFalse(any(p["id"].startswith(("pipe", "cock")) for p in RIG["parts"]))
        self.assertFalse(set(SHAPE["textures"]) & {"pipecopper", "pipelead", "solder", "brass"})


class Binding(unittest.TestCase):
    def test_angle_iron_wraps_the_corners(self):
        names = {e["name"] for e in FRAME["elements"]}
        self.assertFalse(any(n.startswith(("fr_strap", "fr_kerb")) for n in names))
        for corner in ("nw", "ne", "sw", "se"):
            a = aabb_of([e for e in FLAT if e.name.startswith(f"fr_angle_{corner}_a")])
            b = aabb_of([e for e in FLAT if e.name.startswith(f"fr_angle_{corner}_b")])
            for lo, hi in (a, b):
                self.assertAlmostEqual(min(hi[k] - lo[k] for k in range(3)), make_shape.ANGLE_T)
                self.assertAlmostEqual(lo[1], 0.0)
                self.assertAlmostEqual(hi[1], 32.0)
            # one leg on the front or back face, the other on the side face, meeting in the corner
            x = 0.0 if corner[1] == "w" else 16.0
            z = 0.0 if corner[0] == "n" else 32.0
            self.assertIn(z, (a[0][2], a[1][2]))
            self.assertIn(x, (b[0][0], b[1][0]))
            ov = [min(a[1][k], b[1][k]) - max(a[0][k], b[0][k]) for k in range(3)]
            self.assertEqual(sum(o > 0.05 for o in ov), 2)

    def test_top_angles_close_every_corner_in_one_frame(self):
        # a ring of top angles along all four top edges; each leg's end is butted by iron (no brick between members),
        # and the angles are one connected frame
        legs = {n: [aabb_of([e for e in FLAT if e.name.startswith(f"{n}_{leg}")]) for leg in ("a", "b")]
                for n in [f"fr_angle_{c}" for c in ("nw", "ne", "sw", "se")] + [f"fr_band_{f}" for f in "nswe"]}
        iron = [b for bs in legs.values() for b in bs]

        def in_iron(p):
            return any(all(lo[k] - 1e-9 <= p[k] <= hi[k] + 1e-9 for k in range(3)) for lo, hi in iron)

        for face in "nswe":
            axis = 0 if face in "ns" else 2
            for lo, hi in legs[f"fr_band_{face}"]:
                self.assertAlmostEqual(hi[1], 32.0)
                o = [k for k in range(3) if k != axis]
                mid = [(lo[k] + hi[k]) / 2 for k in range(3)]
                for at in (lo[axis] - 0.1, hi[axis] + 0.1):
                    p = list(mid)
                    p[axis] = at
                    self.assertTrue(in_iron(p), (face, p))

        def meet(a, b):
            ov = [min(a[1][k], b[1][k]) - max(a[0][k], b[0][k]) for k in range(3)]
            return min(ov) > -1e-9 and sum(x > 1e-6 for x in ov) >= 2

        seen, todo = {"fr_angle_nw"}, ["fr_angle_nw"]
        while todo:
            n = todo.pop()
            for o in legs:
                if o not in seen and any(meet(a, b) for a in legs[n] for b in legs[o]):
                    seen.add(o)
                    todo.append(o)
        self.assertEqual(seen, set(legs))


class Vessel(unittest.TestCase):
    def test_cells_decks_and_anchors(self):
        cells = {tuple(c["pos"]): c for c in RIG["cells"]}
        self.assertEqual(set(cells), {(0, 0, 0), (0, 1, 0), (0, 0, 1), (0, 1, 1)})
        self.assertEqual({p for p, c in cells.items() if "lid" in c}, {(0, 1, 0), (0, 1, 1)})
        self.assertFalse(any(c.get("hollow") for c in cells.values()))
        for key in ("powerCell", "powerFace", "airCell", "inputCell", "outputCell", "infeedSide", "outputSide", "waterCell", "waterFace"):
            self.assertNotIn(key, RIG)
        self.assertEqual((RIG["outletCell"], RIG["outletFace"]), ([0, 1, 1], "up"))
        chimney = RIG["chimney"]
        self.assertEqual((chimney["axis"], chimney["length"]), ("y", 6.0))
        self.assertAlmostEqual(chimney["origin"][1] - chimney["length"] / 2, 2.0)
        # the chimney's line through the middle of its cell [0,2,1], the outlet square and centred on it
        self.assertEqual((chimney["origin"][0], chimney["origin"][2]), (0.5, 1.5))
        for xs, _, zs in (make_shape.STACK, make_shape.CURB):
            self.assertEqual(xs[1] - xs[0], zs[1] - zs[0])
            self.assertEqual(((xs[0] + xs[1]) / 2, (zs[0] + zs[1]) / 2), (8.0, 24.0))
        self.assertEqual((RIG["vesselCell"], RIG["vesselFace"]), ([0, 1, 0], "north"))
        for key in ("ash", "firedoor"):
            self.assertEqual(RIG[f"{key}Side"], "north")
            self.assertEqual(RIG[key]["pos"][2], 0.0)
        self.assertLess(RIG["ash"]["pos"][1], RIG["firedoor"]["pos"][1])

    def test_side_faces_are_plain_brick_for_a_row(self):
        for el in FLAT:
            lo, hi = el.aabb()
            if lo[0] < 1e-6 or hi[0] > 16 - 1e-6:
                part = rigmath.part_of(RIG["parts"], el.name)
                self.assertTrue(part == "brickwork" or el.name.startswith(("fr_angle_", "fr_band_w_", "fr_band_e_")), el.name)


class Build(unittest.TestCase):
    def test_the_stages_counts_are_measured_from_the_brick(self):
        build = RIG["build"]
        self.assertEqual([s["requires"] for s in build["stages"]], list(STAGES))
        vol = {w: sum(abs((e.aabb()[1][0] - e.aabb()[0][0]) * (e.aabb()[1][1] - e.aabb()[0][1]) * (e.aabb()[1][2] - e.aabb()[0][2]))
                      for e in FLAT if e.name.startswith(f"{w}_brick_")) / 16 ** 3 for w in ("bk", "ln")}
        st = {s["requires"]: s for s in build["stages"]}
        self.assertAlmostEqual(st["brickwork"]["volumeBlocks"], vol["bk"], delta=0.01)
        self.assertAlmostEqual(st["lining"]["volumeBlocks"], vol["ln"], delta=0.01)
        bricks = math.ceil(st["brickwork"]["volumeBlocks"] * make_shape.BRICKS_PER_BLOCK - 1e-9)
        lining = math.ceil(st["lining"]["volumeBlocks"] * make_shape.LINING_PER_BLOCK - 1e-9)
        self.assertEqual(st["brickwork"]["materials"], [{"code": "game:burnedbrick-*", "quantity": bricks}])
        self.assertEqual(st["lining"]["materials"], [{"code": "game:refractorybrick-fired-tier3", "quantity": lining},
                                                     {"code": "game:clay-fire", "quantity": math.ceil(lining / make_shape.LINING_PER_FIRECLAY - 1e-9)}])
        self.assertEqual(st["brickwork"]["alsoParts"], ["hoodiron"])
        self.assertEqual(set(st["fittings"]["parts"]), {"bath", "vessel", "tub", "siphon", "door"})
        self.assertNotIn("waterLine", build)

    def test_any_fired_brick_maps_to_its_blocks_texture(self):
        bricks = RIG["bricks"]
        self.assertEqual((bricks["code"], bricks["default"], bricks["item"]), ("brick", "red", "game:burnedbrick-{type}"))
        self.assertEqual(list(bricks["types"]), ["fire", "black", "brown", "cream", "gray", "orange", "red", "tan", "clinker"])
        self.assertEqual(SHAPE["textures"]["brick"], bricks["types"]["red"]["overlays"][0])
        assets = game_assets()
        if assets is None:
            self.skipTest("no game assets ($VINTAGE_STORY or ~/Games/vintagestory)")
        text = (assets / "survival" / "itemtypes" / "resource" / "burnedbrick.json").read_text()
        states = re.search(r'code:\s*"type",\s*states:\s*\[([^\]]*)\]', text).group(1)
        self.assertEqual(sorted(re.findall(r'"([^"]+)"', states)), sorted(bricks["types"]))


class Scenario(unittest.TestCase):
    def test_the_viewer_gives_the_generators_states(self):
        model = next(m for m in MODELS["models"] if m["id"] == "acid-parting")
        self.assertEqual(model["rig"], "mods-src/seraphhorizons/assets/seraphhorizons/config/acidparting-rig.json")
        sc = model["scenario"]
        self.assertEqual(set(sc["requires"]), REQUIRES - {None})
        self.assertNotIn("choices", sc)
        groups = {g["id"]: g for g in sc["states"]}
        self.assertEqual(set(groups), {"build", "vessel", "tub", "fire", "damper", "outlet"})
        build = groups["build"]
        self.assertEqual((build["values"], build["default"]), (list(STAGES), "complete"))
        self.assertEqual([(o["id"], tuple(o["fitted"])) for o in build["options"]], list(make_shape.STAGE_STATES))
        for gid, states in (("vessel", make_shape.VESSEL_STATES), ("tub", make_shape.TUB_STATES), ("fire", make_shape.FIRE_STATES)):
            self.assertEqual([(o["id"], tuple(o["fitted"])) for o in groups[gid]["options"]], [(s, tuple(w)) for s, _, w in states], gid)
            self.assertEqual(groups[gid]["needs"], list(STAGES))
        self.assertEqual([(o["id"], o["fitted"]) for o in groups["damper"]["options"]],
                         [("open", ["damperopen"]), ("half", ["damperhalf"]), ("shut", ["dampershut"])])
        self.assertEqual(groups["outlet"]["needs"], ["brickwork", "lining"])
        targets = [p.get("to") for p in sc["play"]["phases"] if not p.get("wait")]
        self.assertEqual(sorted(targets), [0, 1])


if __name__ == "__main__":
    unittest.main()
