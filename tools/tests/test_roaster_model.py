"""The roaster's generated model files (mods-src/seraphhorizons/Roaster/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the
shared rig maths and uses the `requires` vocabulary of the tiers, its reference poses are its own maths, the
cells are rebuilt from the shipped shape, the tiers are the scenario's states in site/models.json (tier 2
shares nothing with the furnace, tier 4 is tier 3 with the hopper and feeder for its charging box), the
gearing turns the rabbles at the crown wheels' ratio and the feeder's yoke follows its crank pin, and the
anchors (power, infeed, output, the stack) are the frame's, where the contract puts them. Run with
`python3 -m unittest discover -s tools/tests`.
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
TIER2 = ["stalls", "stallflue", "stallbin", "heaps"]
FURNACE = ["hearth", "walls", "arch", "ironwork", "fire", "drive", "rabbles"]
REQUIRES = set(TIER2) | set(FURNACE) | {"charger", "hopper", "feeder", None}


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
        # the only input is theta: no work, no trunk, no depth
        self.assertNotIn("work", RIG)
        self.assertNotIn("trunkPath", RIG)
        for p in RIG["parts"]:
            for d in p["drivers"]:
                self.assertIn(d["type"], ("rotate", "slide"))
                self.assertEqual(rigmath.driver_input(d), "theta")

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
        # the fire's code is its own, so a renderer can set it to ember while the furnace burns
        fire = {f["texture"] for e in SHAPE["elements"] if requires_of(e["name"]) == "fire" for f in e["faces"].values()}
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


class Tiers(unittest.TestCase):
    def test_the_tiers_replace_each_others_working_parts(self):
        tiers = RIG["tiers"]
        self.assertEqual(sorted(tiers), ["2", "3", "4"])
        self.assertEqual(tiers["2"], TIER2)
        self.assertEqual(tiers["3"], FURNACE + ["charger"])
        self.assertEqual(tiers["4"], FURNACE + ["hopper", "feeder"])
        self.assertFalse(set(tiers["2"]) & (set(tiers["3"]) | set(tiers["4"])))

    def test_the_scenarios_states_are_the_rigs_tiers(self):
        model = next(m for m in MANIFEST["models"] if m["id"] == "roaster")
        self.assertEqual(model["rig"], "mods-src/seraphhorizons/assets/seraphhorizons/config/roaster-rig.json")
        states = model["scenario"]["states"]
        self.assertEqual(states["default"], "tier4")
        got = {o["id"]: o["fitted"] for o in states["options"]}
        self.assertEqual(list(got), ["frame", "tier2", "tier3", "tier4"])
        self.assertEqual(got["frame"], [])
        for k in ("2", "3", "4"):
            self.assertEqual(got[f"tier{k}"], RIG["tiers"][k])
        self.assertEqual(set(model["scenario"]["requires"]), REQUIRES - {None})

    def test_the_stalls_stand_where_the_furnace_does(self):
        # tier 2's parts and the furnace's share the footprint: the states never draw them together
        els = flatten(SHAPE["elements"], textures={})
        boxes = {}
        for el in els:
            r = requires_of(el.name)
            key = "tier2" if r in TIER2 else "furnace" if r in FURNACE else None
            if key:
                lo, hi = el.aabb()
                b = boxes.setdefault(key, [list(lo), list(hi)])
                boxes[key] = [[min(b[0][k], lo[k]) for k in range(3)], [max(b[1][k], hi[k]) for k in range(3)]]
        (a0, a1), (b0, b1) = boxes["tier2"], boxes["furnace"]
        self.assertTrue(all(max(a0[k], b0[k]) < min(a1[k], b1[k]) for k in range(3)))


class Motion(unittest.TestCase):
    def test_the_rabbles_turn_at_the_crown_wheels_ratio(self):
        g = RIG["gearing"]
        self.assertEqual((g["pinionTeeth"], g["crownTeeth"]), (make_shape.NP, make_shape.NC))
        for k in (1, 2):
            d = part(f"rabble{k}")["drivers"][0]
            self.assertEqual((d["type"], d["axis"]), ("rotate", "y"))
            self.assertAlmostEqual(d["ratio"], -g["pinionTeeth"] / g["crownTeeth"], places=6)
            self.assertAlmostEqual(abs(d["ratio"]), g["rabbleTurnsPerAxleTurn"], places=6)
        # three turns of the axle bring every part back where it was (to the ratio's six places)
        for pid in (p["id"] for p in RIG["parts"]):
            a, b = matrix(pid, 0.3), matrix(pid, 0.3 + 3 * math.tau)
            for r in range(3):
                for c in range(4):
                    self.assertAlmostEqual(a[r][c], b[r][c], places=4, msg=pid)

    def test_the_yoke_follows_the_crank_pin(self):
        pin = [flatten([e], textures={})[0] for e in SHAPE["elements"] if e["name"].startswith("crank_pin")]
        x0 = sum(e.c[0] for e in pin) / len(pin)
        y0 = sum(e.c[1] for e in pin) / len(pin)
        z0 = sum(e.c[2] for e in pin) / len(pin)
        for th in (0.0, 0.7, 1.9, 3.3, 5.0):
            px = rigmath.apply(matrix("crank", th), [x0 / 16, y0 / 16, z0 / 16])[0]
            yoke = matrix("yoke", th)[0][3]
            self.assertAlmostEqual(px - x0 / 16, yoke, places=9)

    def test_theta_moves_only_the_drive(self):
        moving = {"lineshaft", "crank", "yoke", "rabble1", "rabble2"}
        for p in RIG["parts"]:
            a, b = matrix(p["id"], 0.0), matrix(p["id"], 2.1)
            same = all(abs(a[r][c] - b[r][c]) < 1e-12 for r in range(3) for c in range(4))
            self.assertEqual(not same, p["id"] in moving, p["id"])
        self.assertEqual({part(pid)["requires"] for pid in moving}, {"drive", "rabbles", "feeder"})


class Anchors(unittest.TestCase):
    def test_cells_and_the_fixed_anchors(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        self.assertEqual(len(cells), 44)
        self.assertIn((0, 0, 0), cells)
        normals = {"north": (0, 0, -1), "east": (1, 0, 0), "up": (0, 1, 0)}
        for key, face in (("power", "north"), ("infeed", "up"), ("output", "east")):
            cell = tuple(RIG[f"{key}Cell"])
            self.assertEqual(RIG[f"{key}Face"], face)
            self.assertIn(cell, cells)
            self.assertNotIn(tuple(cell[k] + normals[face][k] for k in range(3)), cells)
        self.assertEqual(RIG["powerCell"], [0, 2, 0])
        self.assertEqual(RIG["infeedCell"], [0, 2, 4])
        self.assertEqual(RIG["outputCell"], [1, 0, 2])
        # the line shaft's axis is the power cell's centre, where the vanilla axle comes in
        self.assertEqual(part("lineshaft")["drivers"][0]["pivot"], [0.5, 2.5, 0.0])
        # the infeed point is at the infeed cell's top; the output point on the output cell's east face
        self.assertEqual(RIG["infeed"]["pos"][1], 3.0)
        self.assertEqual(RIG["output"]["pos"][0], 2.0)
        self.assertEqual((RIG["fuelSide"], RIG["sulfurSide"]), ("north", "east"))
        self.assertEqual(RIG["smoke"]["pos"][1], make_shape.STACK_TOP / 16)

    def test_the_fixed_things_are_the_frames(self):
        names = [e["name"] for e in SHAPE["elements"]]
        for prefix in ("fr_stack_", "fr_stackcap", "fr_chamber_", "fr_spout", "fr_plinth", "fr_sulfurdoor"):
            found = [n for n in names if n.startswith(prefix)]
            self.assertTrue(found, prefix)
            self.assertEqual({requires_of(n) for n in found}, {None}, prefix)
        # every tier's hopper has its mouth at the infeed cell's top
        els = flatten(SHAPE["elements"], textures={})
        for pid in ("stallbin", "charger", "hopper"):
            top = max(el.aabb()[1][1] for el in els if rigmath.part_of(RIG["parts"], el.name) == pid)
            self.assertAlmostEqual(top, (RIG["infeedCell"][1] + 1) * 16, places=6, msg=pid)


if __name__ == "__main__":
    unittest.main()
