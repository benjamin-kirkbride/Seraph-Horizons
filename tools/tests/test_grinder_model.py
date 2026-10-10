"""The grinder's generated model files (mods-src/seraphhorizons/Grinder/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the
shared rig maths and uses the tiers' `requires` vocabulary, each tier's set is in the rig's `tiers` once, its
reference poses are its own maths, the cells are rebuilt from the shipped shape, the frame shape is the frame
part, the only input is the axle's angle and the machine repeats every six turns at its gearing's ratios, the
anchors are where the layout puts them, and the README's tier table is the generator's. Run with
`python3 -m unittest discover -s tools/tests`.
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
sys.path.insert(0, str(MOD / "Grinder" / "tools"))

from machinegen import rigmath  # noqa: E402
from machinegen.geometry import aabb_of, flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("grinder_make_shape", MOD / "Grinder" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "grinder-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "grinder.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "grinder_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "Grinder" / "rig-reference.json").read_text())
README = (MOD / "Grinder" / "README.md").read_text()
TIERS = {2: ("t2basin", "t2post", "t2arms", "t2stones"), 3: ("t3pan", "t3shaft", "t3runners", "t3scrapers"),
         4: ("t4bed", "t4drum", "t4drive", "t4balls", "t4feed")}


def matrix(pid, theta):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta})


def posed_part(pid, theta=0.0):
    out = []
    for el in flatten(SHAPE["elements"], textures={}):
        if rigmath.part_of(RIG["parts"], el.name) == pid:
            out.append(rigmath.posed(el, matrix(pid, theta)))
    return out


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_tiers_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[:1] + ids[-1:], ["shaft", "frame"])
        for p in RIG["parts"]:
            for d in p["drivers"]:
                rigmath.validate_driver(d)
                self.assertEqual(d["type"], "rotate")
                self.assertNotIn("input", d)
            tier = int(p["id"][1]) if re.match(r"t\d", p["id"]) else 0
            if tier:
                self.assertIn(p["requires"], TIERS[tier], p["id"])
            else:
                self.assertIsNone(p["requires"], p["id"])
        self.assertEqual({p["requires"] for p in RIG["parts"]} - {None}, {r for rs in TIERS.values() for r in rs})

    def test_the_tiers_list_each_set_in_order(self):
        self.assertEqual([(t["tier"], tuple(t["fitted"])) for t in RIG["tiers"]], sorted(TIERS.items()))
        self.assertEqual([t["name"] for t in RIG["tiers"]], ["arrastra", "Chilean mill", "ball mill"])

    def test_every_element_has_a_part_and_the_frame_shape_is_the_frame(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_textures_are_declared(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertLessEqual(used, set(shape["textures"]))
        self.assertEqual(SHAPE["textures"]["riveted"], "game:block/metal/riveted/iron1")

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreaterEqual(len(poses), 10)
        for pose in poses:
            for pid, want in pose["matrices"].items():
                got = matrix(pid, pose["theta"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {pose['theta']}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])


class Motion(unittest.TestCase):
    def test_each_part_turns_at_its_gearings_ratio(self):
        # turns per axle turn: the post a third (3:1 bevels), the Chilean mill's shaft a half (2:1), the
        # countershaft one (mitres) and the drum a third (the girth gear's 30 to its pinion's 10); the runners roll
        # at their track (9.6 from the centre, radius 7.2): 4/3 of the shaft's half
        want = {"shaft": 1.0, "t2post": -1 / 3, "t3shaft": -1 / 2, "t3runner1": 2 / 3, "t3runner2": -2 / 3, "t4counter": -1.0, "t4drum": 1 / 3}
        for p in RIG["parts"]:
            if p["drivers"]:
                self.assertAlmostEqual(p["drivers"][0]["ratio"], want[p["id"]], places=5, msg=p["id"])
        rides = {p["id"]: p["ride"] for p in RIG["parts"] if p["ride"]}
        self.assertEqual(rides, {"t2pinion": "shaft", "t2arms": "t2post", "t2stones": "t2post", "t3pinion": "shaft", "t3runner1": "t3shaft",
                                 "t3runner2": "t3shaft", "t3scrapers": "t3shaft", "t4mitre": "shaft"})

    def test_the_machine_repeats_every_six_turns(self):
        # within the ratios' six places (a third is written 0.333333)
        for p in RIG["parts"]:
            a, b = matrix(p["id"], 0.0), matrix(p["id"], 6 * 2 * math.pi)
            self.assertLess(max(abs(a[i][j] - b[i][j]) for i in range(3) for j in range(4)), 1e-4, p["id"])

    def test_the_runners_roll_on_the_die(self):
        # each runner's lowest point at its track stands still as the axle turns
        h = 1e-4
        m = make_shape
        for pid, sgn in (("t3runner1", 1.0), ("t3runner2", -1.0)):
            p = [(m.CX + sgn * m.RUNNER_TRACK) / 16 - m.ORIGIN_CELL[0], m.FLOOR_Y / 16, m.CZ / 16]
            a = rigmath.apply(matrix(pid, h), p)
            b = rigmath.apply(matrix(pid, -h), p)
            self.assertLess(max(abs(a[i] - b[i]) for i in range(3)) / (2 * h), 1e-4, pid)


class Anchors(unittest.TestCase):
    def test_cells_faces_and_the_output(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        self.assertEqual(cells, {(x, y, z) for x in (-1, 0, 1) for y in (0, 1) for z in (0, 1, 2)})
        self.assertFalse([c for c in RIG["cells"] if "lid" in c])
        got = {k: (tuple(RIG[k + "Cell"]), RIG[k + "Face"]) for k in ("power", "infeed", "output")}
        self.assertEqual(got, {"power": ((0, 0, 2), "south"), "infeed": ((-1, 1, 1), "up"), "output": ((1, 0, 1), "east")})
        self.assertEqual(RIG["output"]["pos"][0], 2.0)
        # the middlings return is merged into the feed (the owner's ruling): no anchor of its own
        self.assertFalse([k for k in RIG if k.startswith("return")])

    def test_the_axle_meets_the_power_face_and_the_hopper_is_the_only_way_in(self):
        # shipped voxels: the power cell [0,0,2] spans x 0..16, z 32..48
        axle = aabb_of([e for e in posed_part("shaft") if e.name.startswith("sh_axle")])
        self.assertAlmostEqual(axle[1][2], 48.0, places=4)
        self.assertAlmostEqual((axle[0][0] + axle[1][0]) / 2, 8.0, places=4)
        self.assertAlmostEqual((axle[0][1] + axle[1][1]) / 2, 8.0, places=4)
        hopper = aabb_of([e for e in posed_part("frame") if re.fullmatch(r"fr_hopper_(n|s|w|e\d)", e.name)])
        self.assertAlmostEqual(hopper[1][1], 32.0, places=4)
        self.assertTrue(-16.0 <= hopper[0][0] and hopper[1][0] <= 0.0 and 16.0 <= hopper[0][2] and hopper[1][2] <= 32.0)
        # fresh ore and, at tier 4, the middlings come in by the hopper alone: nothing else reaches the top face
        high = set()
        for el in flatten(SHAPE["elements"], textures={}):
            pid = rigmath.part_of(RIG["parts"], el.name)
            if rigmath.posed(el, matrix(pid, 0.0)).aabb()[1][1] > 31.5:
                high.add(el.name)
        self.assertTrue(high)
        self.assertTrue(all(n.startswith("fr_hopper") for n in high), sorted(high))


class Readme(unittest.TestCase):
    def test_the_readme_holds_the_generators_tiers(self):
        rows = re.findall(r"^\| ([234]) \| (\d) \| `(t\d\w+)` \|", README, re.M)
        got = {}
        for tier, n, req in rows:
            got.setdefault(int(tier), []).append((int(n), req))
        self.assertEqual({t: [r for _, r in sorted(v)] for t, v in got.items()}, {t: list(v) for t, v in TIERS.items()})
        self.assertEqual([(t, [r for r, _ in stages]) for t, _, stages in make_shape.TIERS], [(t, list(v)) for t, v in TIERS.items()])


if __name__ == "__main__":
    unittest.main()
