"""The roaster's generated model files (mods-src/seraphhorizons/Roaster/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the
shared rig maths and uses the `requires` vocabulary of the levels, its reference poses are its own maths, the
cells are rebuilt from the shipped shape (the frame's boxes, and each fitted part's), the levels are the
scenario's states in site/models.json (level 2 keeps level 1's hopper and pulls down the rest), the gearing
turns the rabbles at the bevel pairs' ratio and the feed roll with the shaft, and the anchors (power, infeed,
output, fuel, the stack) are where the contract puts them. Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import math
import re
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(MOD / "Roaster" / "tools"))

from machinegen import checks, rigmath  # noqa: E402
from machinegen.geometry import flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("roaster_make_shape", MOD / "Roaster" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "roaster-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "roaster.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "roaster_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "Roaster" / "rig-reference.json").read_text())
MANIFEST = json.loads((ROOT / "site" / "models.json").read_text())
LEVEL1 = ["stalls", "stallflue", "trestle", "hopper"]
LEVEL2 = ["hopper", "hearth", "walls", "arch", "ironwork", "firebox", "drive", "rabbles", "feeder"]
CONTENTS = {"1": ["heaps"], "2": ["charge", "fire"]}
REQUIRES = set(LEVEL1) | set(LEVEL2) | {"heaps", "charge", "fire", None}
MOVING = {"entry", "lineshaft", "rabble1", "rabble2", "feedroll"}
RECTIFIED = {"lineshaft", "rabble1", "rabble2", "feedroll"}


def matrix(pid, theta):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta})


def part(pid):
    return next(p for p in RIG["parts"] if p["id"] == pid)


def requires_of(name):
    return part(rigmath.part_of(RIG["parts"], name))["requires"]


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            self.assertIsNone(p.get("ride"))
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        # the entry reads theta, everything beyond the gear case's rectifier the axle's travel: no work, no trunk
        self.assertNotIn("work", RIG)
        self.assertNotIn("trunkPath", RIG)
        for p in RIG["parts"]:
            for d in p["drivers"]:
                self.assertEqual(d["type"], "rotate")
                self.assertEqual(rigmath.driver_input(d), "travel" if p["id"] in RECTIFIED else "theta", p["id"])

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_textures_are_the_games(self):
        self.assertEqual(set(SHAPE["textures"]), set(make_shape.TEXTURES))
        for code, path in SHAPE["textures"].items():
            self.assertTrue(path.startswith("game:block/"), code)
        # the burning bed's code is its own, so a renderer can set it to ember while the furnace burns
        fire = {f["texture"] for e in SHAPE["elements"] if e["name"].startswith("fire_bed") for f in e["faces"].values()}
        self.assertEqual(fire, {"#fire"})

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreater(len(poses), 10)
        for pose in poses:
            self.assertEqual(set(pose["matrices"]), {p["id"] for p in RIG["parts"]})
            for pid, want in pose["matrices"].items():
                got = matrix(pid, pose["theta"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at theta {pose['theta']}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        cells = make_shape.shipped_cells(SHAPE, RIG["parts"])
        self.assertEqual(cells, RIG["cells"])
        self.assertEqual(checks.lid_gaps(RIG["cells"]), [])
        self.assertFalse([c for c in RIG["cells"] if c.get("hollow")])


class Collision(unittest.TestCase):
    """A cell's `boxes` are the frame's; its `fitted` boxes are each requires value's, there only while it is
    fitted (the shared format, docs/recipe-browser/models.md "Collision by what is fitted")."""

    def test_frame_boxes_come_from_the_frame_and_fitted_boxes_from_their_parts(self):
        els = flatten(SHAPE["elements"], textures={})
        by_value = {}
        for el in els:
            lo, hi = el.aabb()
            for c in checks.cells_touched(lo, hi):
                by_value.setdefault(tuple(c), set()).add(requires_of(el.name))
        for cell in RIG["cells"]:
            pos = tuple(cell["pos"])
            for r, boxes in cell.get("fitted", {}).items():
                self.assertIn(r, REQUIRES - {None})
                self.assertIn(r, by_value[pos], (pos, r))
                for b in boxes:
                    self.assertEqual(len(b), 6)
                    self.assertTrue(all(0 <= b[k] < b[k + 3] <= 1 for k in range(3)), (pos, r, b))
            if "boxes" in cell:
                self.assertIn(None, by_value[pos], pos)
            self.assertTrue(cell.get("boxes") or cell.get("fitted"), pos)

    def test_level_1_collides_with_its_stalls_not_the_furnace(self):
        # the middle of each stall and of the forecourt in front of it is open at level 1, and in the furnace's
        # west wall at level 2
        def solid(p, fitted):
            cell = tuple(int(p[k] // 16) - make_shape.ORIGIN_CELL[k] for k in range(3))
            c = next(c for c in RIG["cells"] if tuple(c["pos"]) == cell)
            local = [(p[k] / 16) % 1.0 for k in range(3)]
            boxes = list(c.get("boxes", [])) + [b for r, bs in c.get("fitted", {}).items() if r in fitted for b in bs]
            return any(all(b[k] < local[k] < b[k + 3] for k in range(3)) for b in boxes)
        for s0, s1 in make_shape.STALLS:
            z = (s0 + s1) / 2
            self.assertFalse(solid([21.0, 13.0, z], LEVEL1), z)
            self.assertFalse(solid([38.0, 13.0, z], LEVEL1), z)
            self.assertTrue(solid([5.0, 9.0, z], LEVEL2), z)
            self.assertTrue(solid([7.0, 13.0, z], LEVEL1), z)          # the stalls' back wall


class Levels(unittest.TestCase):
    def test_level_2_keeps_the_hopper_and_pulls_down_the_rest(self):
        self.assertEqual(RIG["levels"], {"1": LEVEL1, "2": LEVEL2})
        self.assertEqual(RIG["contents"], CONTENTS)
        self.assertEqual(set(LEVEL1) & set(LEVEL2), {"hopper"})
        self.assertEqual(sorted(set(LEVEL1) - set(LEVEL2)), sorted(make_shape.RETIRED))

    def test_the_scenarios_states_are_the_rigs_levels(self):
        model = next(m for m in MANIFEST["models"] if m["id"] == "roaster")
        self.assertEqual(model["rig"], "mods-src/seraphhorizons/assets/seraphhorizons/config/roaster-rig.json")
        states = model["scenario"]["states"]
        self.assertEqual(states["default"], "level2")
        got = {o["id"]: o["fitted"] for o in states["options"]}
        self.assertEqual(list(got), ["frame", "level1", "level2"])
        self.assertEqual(got["frame"], [])
        for k in ("1", "2"):
            self.assertEqual(got[f"level{k}"], RIG["levels"][k] + RIG["contents"][k])
        self.assertEqual(set(model["scenario"]["requires"]), REQUIRES - {None})
        # the global tiers are named (water, mill); a machine's own steps are levels, never "tier 2"
        self.assertIsNone(re.search(r"tier\s*\d", json.dumps(model).lower()))

    def test_the_stalls_stand_where_the_furnace_does(self):
        # level 1's retired parts and the furnace share the footprint: the states never draw them together
        els = flatten(SHAPE["elements"], textures={})
        boxes = {}
        for el in els:
            r = requires_of(el.name)
            key = "retired" if r in make_shape.RETIRED else "furnace" if r in LEVEL2 and r != "hopper" else None
            if key:
                lo, hi = el.aabb()
                b = boxes.setdefault(key, [list(lo), list(hi)])
                boxes[key] = [[min(b[0][k], lo[k]) for k in range(3)], [max(b[1][k], hi[k]) for k in range(3)]]
        (a0, a1), (b0, b1) = boxes["retired"], boxes["furnace"]
        self.assertTrue(all(max(a0[k], b0[k]) < min(a1[k], b1[k]) for k in range(3)))


class Motion(unittest.TestCase):
    def test_the_rabbles_turn_at_the_bevel_pairs_ratio(self):
        g = RIG["gearing"]
        self.assertEqual((g["pinionTeeth"], g["wheelTeeth"]), (make_shape.NP, make_shape.NW))
        for k in (1, 2):
            d = part(f"rabble{k}")["drivers"][0]
            self.assertEqual((d["type"], d["axis"]), ("rotate", "y"))
            self.assertAlmostEqual(d["ratio"], -g["pinionTeeth"] / g["wheelTeeth"], places=6)
            self.assertAlmostEqual(abs(d["ratio"]), g["rabbleTurnsPerAxleTurn"], places=6)
        # two turns of the axle bring every part back where it was
        for pid in (p["id"] for p in RIG["parts"]):
            a, b = matrix(pid, 0.3), matrix(pid, 0.3 + 2 * math.tau)
            for r in range(3):
                for c in range(4):
                    self.assertAlmostEqual(a[r][c], b[r][c], places=4, msg=pid)

    def test_the_axle_may_turn_either_way(self):
        # the owner's rule: beyond the rectifier everything is posed alike for the axle turned back as forward (the
        # rabbles plough towards the discharge, the roll feeds); the entry alone turns back
        for th in (0.6, 2.4, 9.0):
            for pid in MOVING:
                fwd = rigmath.part_matrix(RIG["parts"], pid, {"theta": th, "travel": th})
                back = rigmath.part_matrix(RIG["parts"], pid, {"theta": -th, "travel": th})
                same = all(abs(fwd[r][c] - back[r][c]) < 1e-12 for r in range(3) for c in range(4))
                self.assertEqual(same, pid in RECTIFIED, pid)

    def test_the_feed_roll_turns_with_the_line_shaft(self):
        self.assertEqual(RIG["gearing"]["feedRollTurnsPerAxleTurn"], 1)
        for th in (0.0, 0.7, 1.9, 3.3, 5.0):
            self.assertEqual(matrix("feedroll", th), matrix("lineshaft", th))

    def test_theta_moves_only_the_drive(self):
        for p in RIG["parts"]:
            a, b = matrix(p["id"], 0.0), matrix(p["id"], 2.1)
            same = all(abs(a[r][c] - b[r][c]) < 1e-12 for r in range(3) for c in range(4))
            self.assertEqual(not same, p["id"] in MOVING, p["id"])
        self.assertEqual({part(pid)["requires"] for pid in MOVING}, {"drive", "rabbles", "feeder"})


class Anchors(unittest.TestCase):
    def test_cells_and_the_fixed_anchors(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        self.assertEqual(len(cells), 44)
        self.assertIn((0, 0, 0), cells)
        normals = {"north": (0, 0, -1), "east": (1, 0, 0), "up": (0, 1, 0)}
        for key, face in (("power", "north"), ("infeed", "up"), ("output", "east"), ("fuel", "up")):
            cell = tuple(RIG[f"{key}Cell"])
            self.assertEqual(RIG[f"{key}Face"], face)
            self.assertIn(cell, cells)
            self.assertNotIn(tuple(cell[k] + normals[face][k] for k in range(3)), cells)
        self.assertEqual(RIG["powerCell"], [0, 2, 0])
        self.assertEqual(RIG["infeedCell"], [0, 2, 4])
        self.assertEqual(RIG["outputCell"], [1, 0, 2])
        self.assertEqual(RIG["fuelCell"], [-1, 1, 0])
        # the line shaft's axis is the power cell's centre, where the vanilla axle comes in
        self.assertEqual(part("lineshaft")["drivers"][0]["pivot"], [0.5, 2.5, 0.0])
        # the infeed point is at the infeed cell's top; the output point on the output cell's east face; the fuel
        # point the bunker's mouth over the fuel cell
        self.assertEqual(RIG["infeed"]["pos"][1], 3.0)
        self.assertEqual(RIG["output"]["pos"][0], 2.0)
        self.assertEqual(RIG["fuel"]["pos"][1], 2.0)
        self.assertEqual(RIG["sulfurSide"], "east")
        self.assertEqual(RIG["smoke"]["pos"][1], make_shape.STACK_TOP / 16)
        # no process water: roasting is dry
        self.assertFalse([k for k in RIG if "water" in k.lower() or "pipe" in k.lower()])

    def test_the_chute_fed_infeeds_end_in_vanilla_chute_sections(self):
        # the owner's rule: a chute-fed infeed is drawn as the vanilla chute (its "ud" straight section: an 8 x 8 tube
        # centred in the cell, walls 1 thick, the copper sheet a placed chute wears), up to the anchor's face, so a
        # chute placed in the next cell meets it with no gap or step
        self.assertEqual(SHAPE["textures"]["chute"], "game:block/metal/sheet/copper1")
        els = flatten(SHAPE["elements"], textures={})
        want = sorted(make_shape.CHUTE_UD_WALLS)
        for key, prefix in (("infeed", "feeder_infeedchute"), ("fuel", "firebox_fuelchute")):
            self.assertEqual(RIG[f"{key}Face"], "up")
            o = [RIG[f"{key}Cell"][k] * 16 for k in range(3)]
            walls = [el for el in els if el.name.startswith(prefix)]
            got = []
            for el in walls:
                lo, hi = el.aabb()
                got.append((round(lo[0] - o[0], 6), round(hi[0] - o[0], 6), round(lo[2] - o[2], 6), round(hi[2] - o[2], 6)))
                self.assertAlmostEqual(hi[1], o[1] + 16, places=6, msg=el.name)
                self.assertEqual({f["texture"] for f in el.faces.values()}, {"#chute"}, el.name)
            self.assertEqual(sorted(got), want, key)
            # they are fitted at level 2 only, with the chute-fed level's parts
            self.assertEqual({requires_of(el.name) for el in walls} <= set(LEVEL2), True, key)
            # the anchor's point is the face's middle
            self.assertEqual(RIG[key]["pos"], [o[0] / 16 + 0.5, o[1] / 16 + 1.0, o[2] / 16 + 0.5])

    def test_the_fixed_things_are_the_frames(self):
        names = [e["name"] for e in SHAPE["elements"]]
        for prefix in ("fr_stack_", "fr_stackcap", "fr_chamber_", "fr_spout", "fr_plinth", "fr_sulfurdoor"):
            found = [n for n in names if n.startswith(prefix)]
            self.assertTrue(found, prefix)
            self.assertEqual({requires_of(n) for n in found}, {None}, prefix)
        # the hopper, both levels', has its mouth at the infeed cell's top
        els = flatten(SHAPE["elements"], textures={})
        top = max(el.aabb()[1][1] for el in els if rigmath.part_of(RIG["parts"], el.name) == "hopper")
        self.assertAlmostEqual(top, (RIG["infeedCell"][1] + 1) * 16, places=6)


if __name__ == "__main__":
    unittest.main()
