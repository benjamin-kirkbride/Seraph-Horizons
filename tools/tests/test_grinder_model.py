"""The grinder's generated model files (mods-src/seraphhorizons/Grinder/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the
shared rig maths and uses the levels' `requires` vocabulary, each level's set is in the rig's `levels` once and
level 2 retires level 1's, its reference poses are its own maths, the cells (frame boxes and fitted boxes) are
rebuilt from the shipped shape, the frame shape is the frame part, the only input is the axle's angle and the
machine repeats every three turns at its gearing's ratios, the anchors are where the layout puts them, the pit is
at ground level, and the README's level table is the generator's. Run with
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
sys.path.insert(0, str(MOD / "Pipes" / "tools"))
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
LEVELS = {1: ("l1hopper", "l1post", "l1arms", "l1stones"), 2: ("l2bed", "l2drum", "l2drive", "l2balls", "l2feed", "l2chute")}
PIPES = ("pipecopper", "pipelead")


def matrix(pid, theta):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta})


def posed_part(pid, theta=0.0):
    out = []
    for el in flatten(SHAPE["elements"], textures={}):
        if rigmath.part_of(RIG["parts"], el.name) == pid:
            out.append(rigmath.posed(el, matrix(pid, theta)))
    return out


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_levels_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[:1] + ids[-1:], ["shaft", "frame"])
        for p in RIG["parts"]:
            for d in p["drivers"]:
                rigmath.validate_driver(d)
                self.assertEqual(d["type"], "rotate")
                self.assertNotIn("input", d)
            level = int(p["id"][1]) if re.match(r"l\d", p["id"]) else 0
            if level:
                self.assertIn(p["requires"], LEVELS[level], p["id"])
            elif p["id"] in PIPES:
                self.assertEqual(p["requires"], p["id"])
            else:
                self.assertIsNone(p["requires"], p["id"])
        self.assertEqual({p["requires"] for p in RIG["parts"]} - {None}, {r for rs in LEVELS.values() for r in rs} | set(PIPES))

    def test_the_levels_list_each_set_in_order_and_level_2_retires_the_arrastra(self):
        self.assertEqual([(lv["level"], tuple(lv["fitted"])) for lv in RIG["levels"]], sorted(LEVELS.items()))
        self.assertEqual([(lv["tier"], lv["name"]) for lv in RIG["levels"]], [("water", "arrastra"), ("mill", "ball mill")])
        self.assertNotIn("retires", RIG["levels"][0])
        self.assertEqual(RIG["levels"][1]["retires"], list(LEVELS[1]))
        self.assertFalse([k for k in RIG if k == "tiers"])

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
        self.assertEqual({k: SHAPE["textures"][k] for k in PIPES},
                         {"pipecopper": "game:block/metal/sheet-plain/copper4", "pipelead": "game:block/metal/sheet-plain/lead4"})

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

    def test_collision_is_the_frame_plus_what_is_fitted(self):
        # every cell's frame boxes are the frame's (the pit, the head frame, the hopper), the rest are keyed by what is
        # fitted; the arrastra's and the ball mill's boxes are never the frame's
        reqs = {p["requires"] for p in RIG["parts"]} - {None}
        for c in RIG["cells"]:
            self.assertLessEqual(set(c.get("fitted", {})), reqs, c["pos"])
            for bs in [c.get("boxes") or []] + list(c.get("fitted", {}).values()):
                for b in bs:
                    self.assertTrue(all(0 <= x <= 1 for x in b) and all(b[k] < b[k + 3] for k in range(3)), (c["pos"], b))
        ground = [c for c in RIG["cells"] if c["pos"][1] == 0]
        self.assertTrue(all(c.get("boxes") for c in ground))
        self.assertTrue(any("l1stones" in c.get("fitted", {}) for c in ground))
        self.assertTrue(any("l2drum" in c.get("fitted", {}) for c in ground))


class Motion(unittest.TestCase):
    def test_each_part_turns_at_its_gearings_ratio(self):
        # turns per axle turn: the arrastra's post a third (its 18-tooth bevel wheel under the 6-tooth pinion), the
        # drum a third the other way (the girth gear's 24 teeth under the pinion's 8)
        want = {"shaft": 1.0, "l1post": 1 / 3, "l2drum": -1 / 3}
        for p in RIG["parts"]:
            if p["drivers"]:
                self.assertAlmostEqual(p["drivers"][0]["ratio"], want[p["id"]], places=5, msg=p["id"])
        rides = {p["id"]: p["ride"] for p in RIG["parts"] if p["ride"]}
        self.assertEqual(rides, {"l1pinion": "shaft", "l1arms": "l1post", "l1stones": "l1post", "l2pinion": "shaft"})

    def test_the_machine_repeats_every_three_turns(self):
        # within the ratios' six places (a third is written 0.333333)
        for p in RIG["parts"]:
            a, b = matrix(p["id"], 0.0), matrix(p["id"], 3 * 2 * math.pi)
            self.assertLess(max(abs(a[i][j] - b[i][j]) for i in range(3) for j in range(4)), 1e-4, p["id"])


class Anchors(unittest.TestCase):
    def test_cells_faces_and_the_output(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        # the box less the two cells over the pit's north corners, which nothing of the machine ever reaches
        self.assertEqual(cells, {(x, y, z) for x in (-1, 0, 1) for y in (0, 1) for z in (0, 1)} - {(-1, 1, 0), (1, 1, 0)})
        self.assertFalse([c for c in RIG["cells"] if "lid" in c])
        got = {k: (tuple(RIG[k + "Cell"]), RIG[k + "Face"]) for k in ("power", "infeed", "water", "output")}
        self.assertEqual(got, {"power": ((0, 1, 1), "south"), "infeed": ((0, 1, 0), "up"), "water": ((0, 1, 0), "north"),
                               "output": ((1, 0, 1), "east")})
        self.assertEqual(RIG["output"]["pos"][0], 2.0)
        # the middlings come back by the feed (the owner's ruling): no anchor of their own
        self.assertFalse([k for k in RIG if k.startswith("return")])

    def test_the_axle_meets_the_power_face_high_and_each_level_has_one_way_in(self):
        # shipped voxels: the power cell [0,1,1] spans x 0..16, y 16..32, z 16..32
        axle = aabb_of([e for e in posed_part("shaft") if e.name.startswith("sh_axle")])
        self.assertAlmostEqual(axle[1][2], 32.0, places=4)
        self.assertAlmostEqual((axle[0][0] + axle[1][0]) / 2, 8.0, places=4)
        self.assertAlmostEqual((axle[0][1] + axle[1][1]) / 2, 24.0, places=4)
        # level 1's hopper rim and level 2's chute head are each the infeed cell's top face; nothing else reaches it
        for pid in ("l1hopper", "l2chute"):
            mouth = aabb_of(posed_part(pid))
            self.assertAlmostEqual(mouth[1][1], 32.0, places=3, msg=pid)
            self.assertTrue(0.0 <= mouth[0][0] and mouth[1][0] <= 16.0 and 0.0 <= mouth[0][2] and mouth[1][2] <= 16.0, pid)
        high = set()
        for el in flatten(SHAPE["elements"], textures={}):
            pid = rigmath.part_of(RIG["parts"], el.name)
            if rigmath.posed(el, matrix(pid, 0.0)).aabb()[1][1] > 31.5:
                high.add(pid)
        self.assertEqual(high, {"l1hopper", "l2chute"})

    def test_level_2s_infeed_is_the_games_chute_at_the_face(self):
        # the owner's rule for every chute-fed infeed: a chute-straight-ud in the cell above meets it with no gap or step,
        # wall for wall, in the game's chute texture (shipped: the infeed cell [0,1,0] spans x 0..16, y 16..32, z 0..16)
        walls = [e for e in posed_part("l2chute") if re.search(r"_(north|south|bottom|top)$", e.name)]
        got = sorted((round(lo[0], 2), round(lo[2], 2), round(hi[0], 2), round(hi[2], 2)) for lo, hi in (e.aabb() for e in walls))
        self.assertEqual(got, [tuple(round(x, 2) for x in r) for r in make_shape.chute_profile()])
        self.assertTrue(all(abs(e.aabb()[1][1] - 32.0) < 0.02 for e in walls))
        self.assertEqual(SHAPE["textures"]["chute"], "game:block/metal/sheet/copper1")
        self.assertEqual({f["texture"] for e in SHAPE["elements"] if e["name"].startswith("l2chute_") and "_board" not in e["name"]
                          for f in e["faces"].values()}, {"#chute"})

    def test_the_water_inlet_is_a_ppex_section_on_the_water_face(self):
        # drawn as the rosser's pipes: the same elements in each metal, only the metal's texture differing
        copper = sorted((e["name"][len("pipecopper"):], e["from"], e["to"]) for e in SHAPE["elements"] if e["name"].startswith("pipecopper_"))
        lead = sorted((e["name"][len("pipelead"):], e["from"], e["to"]) for e in SHAPE["elements"] if e["name"].startswith("pipelead_"))
        self.assertTrue(copper)
        self.assertEqual(copper, lead)
        self.assertFalse([e for e in SHAPE["elements"] if e["name"].startswith("fr_inlet")])
        pipe = aabb_of(posed_part("pipecopper"))
        self.assertAlmostEqual(pipe[0][2], 0.0, places=4)
        self.assertEqual([round(pipe[1][k] - pipe[0][k], 3) for k in (0, 1)], [6.0, 6.0])
        self.assertEqual([round((pipe[0][k] + pipe[1][k]) / 2, 3) for k in (0, 1)], [8.0, 24.0])


class Pit(unittest.TestCase):
    def test_the_arrastra_is_a_paved_pit_at_ground_level(self):
        # no basin on piers: the paving lies on the ground, and the drag stones run on it inside the curb
        pave = [e for e in posed_part("frame") if re.fullmatch(r"fr_pave\d+", e.name)]
        lo, hi = aabb_of(pave)
        self.assertEqual((round(lo[1], 4), round(hi[1], 4)), (0.0, make_shape.PAVE_TOP))
        stones = [e for e in posed_part("l1stones") if "_stone" in e.name]
        self.assertTrue(all(abs(e.aabb()[0][1] - make_shape.PAVE_TOP) < 1e-3 for e in stones))
        curb = [e for e in posed_part("frame") if e.name.startswith("fr_curb")]
        self.assertAlmostEqual(min(e.aabb()[0][1] for e in curb), 0.0, places=4)
        self.assertLessEqual(max(e.aabb()[1][1] for e in curb), make_shape.CURB_TOP + 1e-4)


class Readme(unittest.TestCase):
    def test_the_readme_holds_the_generators_levels(self):
        rows = re.findall(r"^\| ([12]) \| (\d) \| `(l\d\w+)` \|", README, re.M)
        got = {}
        for level, n, req in rows:
            got.setdefault(int(level), []).append((int(n), req))
        self.assertEqual({lv: [r for _, r in sorted(v)] for lv, v in got.items()}, {lv: list(v) for lv, v in LEVELS.items()})
        self.assertEqual([(lv, [r for r, _ in stages]) for lv, _, _, stages, _ in make_shape.LEVELS], [(lv, list(v)) for lv, v in LEVELS.items()])
        self.assertNotRegex(README, r"[Tt]ier [234]\b")


if __name__ == "__main__":
    unittest.main()
