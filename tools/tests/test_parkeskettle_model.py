"""The Parkes kettle's generated model files (mods-src/seraphhorizons/ParkesKettle/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared rig
maths and uses the `requires` vocabulary (the build's stages, brickwork, lining, kettle and stirrer; the bath's four
levels, the zinc and the crust; the coal and the burning coal; the damper's three places; the outlet's cap), its
reference poses are its own maths, the stages' counts are measured from the shape's brick, the cells are rebuilt from
the shipped shape, the drive and the stirrer turn alike with the axle and nothing else turns, the fire door is the only
part depth moves, each level of the bath holds its quarter of a charge, the paddles are symmetric and stir every level,
the anchors are where the contract puts them (the axle at the back at the gallows' height; the outlet under the
chimney's cell, the chimney six blocks from it up; no air, water or chute anchor), the side faces are plain brick for a
row, and the model viewer's scenario (site/models.json) gives the generator's states. Run with
`python3 -m unittest discover -s tools/tests -p test_parkeskettle_model.py`.
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
sys.path.insert(0, str(MOD / "ParkesKettle" / "tools"))

from machinegen import rigmath  # noqa: E402
from machinegen.geometry import aabb_of, flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("parkeskettle_make_shape", MOD / "ParkesKettle" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "parkeskettle-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "parkeskettle.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "parkeskettle_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "ParkesKettle" / "rig-reference.json").read_text())
MODELS = json.loads((ROOT / "site" / "models.json").read_text())
STAGES = ("brickwork", "lining", "kettle", "stirrer")
CONTENTS = ("lead1", "lead2", "lead3", "lead4", "zinc", "crust", "fuel1", "fuel2", "fire1", "fire2")
REQUIRES = {None, *STAGES, "damperopen", "damperhalf", "dampershut", "outletcap", *CONTENTS}
FLAT = flatten(SHAPE["elements"], textures={})
DX = -16.0 * make_shape.ORIGIN_CELL[0]           # the shipped frame is the build frame moved a block west


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
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        req = {p["id"]: p["requires"] for p in RIG["parts"]}
        # the fire door comes with the kettle, the gallows and the drive with the stirrer; the iron frame has none
        self.assertEqual((req["firedoor"], req["gallows"], req["drive"], req["frame"]), ("kettle", "stirrer", "stirrer", None))
        moving = {p["id"]: [(d["type"], d["axis"], d.get("motion")) for d in p["drivers"]] for p in RIG["parts"] if p["drivers"]}
        self.assertEqual(moving, {"drive": [("rotate", "z", None)], "stirrer": [("rotate", "y", None)],
                                  "firedoor": [("step", "y", "slide")]})

    def test_every_element_has_a_part_and_the_frame_is_the_iron_frame(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        self.assertEqual({rigmath.part_of(RIG["parts"], n) for n in names}, {p["id"] for p in RIG["parts"]})
        self.assertEqual({e["name"] for e in FRAME["elements"]}, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})
        self.assertEqual({f["texture"] for e in FRAME["elements"] for f in e["faces"].values()}, {"#iron"})

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreater(len(poses), 10)
        self.assertTrue({p["depth"] for p in poses} >= {0.0, 0.5, 1.0})
        self.assertTrue(any(p["theta"] < 0 for p in poses))
        for pose in poses:
            for pid, want in pose["matrices"].items():
                got = matrix(pid, theta=pose["theta"], depth=pose["depth"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {pose['theta']}, {pose['depth']}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])


class Motion(unittest.TestCase):
    def test_the_stirrer_turns_with_the_axle_either_way_and_nothing_reads_its_travel(self):
        for p in RIG["parts"]:
            for d in p["drivers"]:
                if d["type"] == "rotate":
                    self.assertEqual(rigmath.driver_input(d), "theta", p["id"])
                    self.assertEqual(d["ratio"], 1.0, p["id"])
        self.assertEqual(RIG["furnace"]["stirRatio"], 1.0)
        # the paddles cross on the stirrer's axis: symmetric, they stir alike either way
        axis = matrix("stirrer", theta=0.0)
        kx, kz = (make_shape.KX + DX), make_shape.KZ
        for e in posed_part("stirrer", theta=0.0):
            if e.name.startswith("st_paddle"):
                lo, hi = e.aabb()
                self.assertAlmostEqual((lo[0] + hi[0]) / 2, kx, places=6)
                self.assertAlmostEqual((lo[2] + hi[2]) / 2, kz, places=6)
        self.assertEqual(axis[0][0], 1.0)

    def test_depth_lifts_only_the_fire_door_and_theta_turns_only_the_drive_and_the_stirrer(self):
        ident = [[1.0, 0.0, 0.0, 0.0], [0.0, 1.0, 0.0, 0.0], [0.0, 0.0, 1.0, 0.0], [0.0, 0.0, 0.0, 1.0]]
        for pid in (p["id"] for p in RIG["parts"]):
            m = [[round(v, 12) + 0.0 for v in r] for r in matrix(pid, theta=1.7, depth=0.6)]
            if pid not in ("drive", "stirrer", "firedoor"):
                self.assertEqual(m, ident, pid)
        lift = RIG["furnace"]["fireDoorLift"]
        self.assertAlmostEqual(matrix("firedoor", theta=2.0, depth=1.0)[1][3], lift)
        self.assertAlmostEqual(matrix("firedoor", theta=2.0, depth=0.0)[1][3], 0.0)


class Kettle(unittest.TestCase):
    def test_each_level_of_the_bath_holds_its_quarter_of_a_charge(self):
        self.assertEqual((RIG["furnace"]["chargeUnits"], RIG["furnace"]["leadLevels"]), (2400, 4))
        for n in range(1, 5):
            top = max(e.aabb()[1][1] for e in posed_part(f"lead{n}"))
            self.assertAlmostEqual(top, make_shape.BATH_LEVELS[n - 1], places=3)
            units = make_shape.bowl_volume(top) / make_shape.VOXELS_PER_UNIT
            self.assertAlmostEqual(units, 600 * n, delta=0.02 * 600 * n)
        self.assertGreaterEqual(make_shape.RIM_Y - make_shape.FULL, 1.0)

    def test_the_paddles_stir_every_level_and_break_the_full_surface(self):
        lo, hi = aabb_of([e for e in posed_part("stirrer") if e.name.startswith("st_paddle")])
        self.assertLess(lo[1], make_shape.BATH_LEVELS[0] - 1.0)
        self.assertGreater(hi[1], make_shape.FULL)

    def test_the_flange_is_at_the_floor_and_clear_of_the_chimneys_cell(self):
        flange = [e for e in posed_part("kettle") if e.name.startswith("kt_flange")]
        lo, hi = aabb_of(flange)
        self.assertAlmostEqual(hi[1], 32.0 + make_shape.FLANGE_PROUD)
        self.assertLessEqual(hi[1] - 32.0, 0.2)
        # the chimney's cell is over the back row's east corner: x 16.. (shipped), z 32..
        for e in flange:
            for c in e.corners():
                if c[0] > 16.0:
                    self.assertLess(c[2], 32.0, e.name)


class Anchors(unittest.TestCase):
    def test_cells_power_and_the_outlet(self):
        cells = {tuple(c["pos"]): c for c in RIG["cells"]}
        setting = {(x, y, z) for x in (-1, 0, 1) for y in (0, 1) for z in (0, 1, 2)}
        upper = {(-1, 2, 1), (0, 2, 1), (1, 2, 1), (0, 2, 2)}
        hollow = {(-1, 2, 0), (0, 2, 0), (1, 2, 0)}
        self.assertEqual(set(cells), setting | upper | hollow)
        self.assertTrue(all(cells[p].get("hollow") and not cells[p].get("boxes") for p in hollow))
        self.assertTrue(all(cells[p].get("boxes") for p in setting | upper))
        self.assertEqual({p for p, c in cells.items() if "lid" in c}, {p for p in setting if p[1] == 1})
        # the axle at the back middle cell over the setting, its face south, at the gallows' height; the drive's oak
        # continuation meets the face at its centre
        self.assertEqual((RIG["powerCell"], RIG["powerFace"]), ([0, 2, 2], "south"))
        oak = aabb_of([e for e in posed_part("drive") if e.name.startswith("dr_oak")])
        self.assertAlmostEqual(oak[1][2], 48.0)
        self.assertAlmostEqual((oak[0][0] + oak[1][0]) / 2, 8.0)
        self.assertAlmostEqual((oak[0][1] + oak[1][1]) / 2, 40.0)
        for key in ("airCell", "waterCell", "inputCell", "outputCell", "chute"):
            self.assertNotIn(key, RIG)
        # the outlet in the back row's east corner, the chimney's cell over it and the back west cell not the machine's
        self.assertEqual((RIG["outletCell"], RIG["outletFace"]), ([1, 1, 2], "up"))
        for c in ((1, 2, 2), (-1, 2, 2)):
            self.assertNotIn(c, cells)
        line = RIG["chimney"]
        self.assertEqual((line["axis"], line["length"]), ("y", 6.0))
        self.assertAlmostEqual(line["origin"][1] - line["length"] / 2, 2.0)
        self.assertEqual((RIG["furnace"]["rowMax"], RIG["furnace"]["rowAxis"], RIG["furnace"]["minChimney"]), (5, "x", 6))
        # the fire door and the ash mouth on the controller's front face; the kettle worked from its top
        for key in ("fireDoor", "ash"):
            self.assertEqual((RIG[f"{key}Side"], RIG[key]["pos"][0], RIG[key]["pos"][2]), ("north", 0.5, 0.0))
        self.assertEqual((RIG["kettleCell"], RIG["kettleFace"]), ([0, 1, 1], "up"))

    def test_side_faces_are_flush_for_a_row(self):
        # only the brick and the corner angles' legs, set flush, reach the side planes, and nothing passes them
        west, east = DX, DX + 48.0
        for el in FLAT:
            lo, hi = el.aabb()
            self.assertGreaterEqual(lo[0], west - 1e-6, el.name)
            self.assertLessEqual(hi[0], east + 1e-6, el.name)
            if lo[0] < west + 1e-6 or hi[0] > east - 1e-6:
                self.assertTrue(el.name.startswith(("bk_brick", "fr_angle_")), el.name)

    def test_the_binding_is_angle_iron(self):
        # an angle at each vertical corner, ground to floor, legs 1 thick and 2.5 along each face from the corner; an
        # angle band along the top edge of the front and of the back between them; no flat strap or band
        names = [e["name"] for e in FRAME["elements"]]
        self.assertFalse(any(n.startswith(("fr_strap", "fr_kerb")) for n in names))
        t, leg = make_shape.BIND_T, make_shape.BIND_LEG
        self.assertLessEqual(t, 1.0)
        self.assertTrue(2.0 <= leg <= 3.0)
        for tag, xc, zc, sx, sz in make_shape.CORNERS:
            lo, hi = aabb_of([el for el in FLAT if el.name.startswith(f"fr_angle_{tag}_")])
            x0, x1 = sorted((xc + DX, xc + DX + sx * leg))
            z0, z1 = sorted((zc, zc + sz * leg))
            for a, b in zip(lo + hi, [x0, 0.0, z0, x1, 32.0, z1]):
                self.assertAlmostEqual(a, b, delta=0.05, msg=tag)
            # two legs, each thin across its own face
            legs = [el for el in FLAT if el.name.startswith(f"fr_angle_{tag}_")]
            thin_z = [el for el in legs if el.aabb()[1][2] - el.aabb()[0][2] <= t + 0.05]
            thin_x = [el for el in legs if el.aabb()[1][0] - el.aabb()[0][0] <= t + 0.05]
            self.assertTrue(thin_z and thin_x, tag)
        for face in ("n", "s"):
            lo, hi = aabb_of([el for el in FLAT if el.name.startswith(f"fr_band_{face}_")])
            self.assertAlmostEqual(lo[0], DX + leg, delta=0.05)
            self.assertAlmostEqual(hi[0], DX + 48.0 - leg, delta=0.05)
            self.assertAlmostEqual(hi[1], 32.0, delta=0.05)
            self.assertAlmostEqual(hi[1] - lo[1], leg, delta=0.05)


def element_volume(e):
    return math.prod(e["to"][k] - e["from"][k] for k in range(3))


class Build(unittest.TestCase):
    def test_the_stages_counts_are_measured_from_the_brick(self):
        build = RIG["build"]
        self.assertEqual([s["requires"] for s in build["stages"]], list(STAGES))
        st = {s["requires"]: s for s in build["stages"]}
        bk = sum(element_volume(e) for e in SHAPE["elements"] if e["name"].startswith("bk_brick_")) / 4096
        ln = sum(element_volume(e) for e in SHAPE["elements"] if e["name"].startswith("ln_brick_")) / 4096
        self.assertAlmostEqual(bk, st["brickwork"]["volumeBlocks"], delta=0.01)
        self.assertAlmostEqual(ln, st["lining"]["volumeBlocks"], delta=0.01)
        bricks, lining = math.ceil(st["brickwork"]["volumeBlocks"] * 2), math.ceil(st["lining"]["volumeBlocks"] * 4)
        self.assertEqual([(m["code"], m["quantity"]) for m in st["brickwork"]["materials"]], [("game:burnedbrick-*", bricks)])
        self.assertEqual([(m["code"], m["quantity"]) for m in st["lining"]["materials"]],
                         [("game:refractorybrick-fired-tier3", lining), ("game:clay-fire", math.ceil(lining / 2.5))])
        self.assertEqual(st["kettle"]["alsoParts"], ["firedoor"])
        self.assertEqual(st["stirrer"]["alsoParts"], ["gallows", "drive"])
        self.assertEqual(st["kettle"]["also"], ["damperopen", "damperhalf", "dampershut"])

    def test_bricks_are_the_crucible_furnaces(self):
        cruc = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "cruciblefurnace-rig.json").read_text())
        self.assertEqual(RIG["bricks"]["types"], cruc["bricks"]["types"])
        self.assertEqual(SHAPE["textures"]["brick"], RIG["bricks"]["types"]["red"]["overlays"][0])


class Scenario(unittest.TestCase):
    def test_the_viewer_gives_the_generators_states(self):
        model = next(m for m in MODELS["models"] if m["id"] == "parkeskettle")
        self.assertEqual(model["rig"], "mods-src/seraphhorizons/assets/seraphhorizons/config/parkeskettle-rig.json")
        sc = model["scenario"]
        self.assertEqual(set(sc["requires"]), REQUIRES - {None})
        groups = {g["id"]: g for g in sc["states"]}
        self.assertEqual(set(groups), {"build", "kettle", "fire", "damper", "outlet"})
        build = groups["build"]
        self.assertEqual((build["values"], build["default"]), (list(STAGES), "complete"))
        self.assertEqual([(o["id"], tuple(o["fitted"])) for o in build["options"]], list(make_shape.STAGE_STATES))
        for gid, states in (("kettle", make_shape.KETTLE_STATES), ("fire", make_shape.FIRE_STATES)):
            self.assertEqual([(o["id"], o["label"], tuple(o["fitted"])) for o in groups[gid]["options"]],
                             [(sid, label, tuple(what)) for sid, label, what in states])
            self.assertEqual(groups[gid]["needs"], ["brickwork", "lining", "kettle"])
        self.assertEqual([(o["id"], o["fitted"]) for o in groups["damper"]["options"]],
                         [("open", ["damperopen"]), ("half", ["damperhalf"]), ("shut", ["dampershut"])])
        self.assertEqual([(o["id"], o["fitted"]) for o in groups["outlet"]["options"]], [("open", []), ("capped", ["outletcap"])])
        # the stages' labels carry the measured counts
        st = {s["requires"]: s for s in RIG["build"]["stages"]}
        labels = {o["id"]: o["label"] for o in build["options"]}
        self.assertIn(f"{st['brickwork']['materials'][0]['quantity']} fired bricks", labels["brickwork"])
        self.assertIn(f"{st['lining']['materials'][0]['quantity']} tier-3 refractory bricks", labels["lining"])
        self.assertIn(f"{st['lining']['materials'][1]['quantity']} fire clay", labels["lining"])
        # Play lifts and lets down the fire door while the stirrer turns
        targets = [p.get("to") for p in sc["play"]["phases"] if not p.get("wait")]
        self.assertEqual(sorted(targets), [0, 1])


if __name__ == "__main__":
    unittest.main()
