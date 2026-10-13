"""The arrastra's generated model files (mods-src/seraphhorizons/Arrastra/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared rig
maths and uses the build's `requires` vocabulary, its `build` lists the stages in order, its reference poses are its
own maths, the cells are rebuilt from the shipped shape, the frame shape is the frame part, the only input is the
axle's angle and the machine repeats every three turns at its gearing's ratio, the anchors are where the layout puts
them (batch, by hand: no chute and no output), the pit is at ground level, and the README's stage table is the
generator's. Run with `python3 -m unittest discover -s tools/tests -p test_arrastra_model.py`.
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
sys.path.insert(0, str(MOD / "Arrastra" / "tools"))

from machinegen import rigmath  # noqa: E402
from machinegen.geometry import aabb_of, flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("arrastra_make_shape", MOD / "Arrastra" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "arrastra-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "arrastra.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "arrastra_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "Arrastra" / "rig-reference.json").read_text())
README = (MOD / "Arrastra" / "README.md").read_text()
STAGES = ("pit", "post", "arms", "stones", "hopper")
PIPES = ("pipecopper", "pipelead")
CONTENTS = ("charge", "paste")


def matrix(pid, theta):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta})


def posed_part(pid, theta=0.0):
    out = []
    for el in flatten(SHAPE["elements"], textures={}):
        if rigmath.part_of(RIG["parts"], el.name) == pid:
            out.append(rigmath.posed(el, matrix(pid, theta)))
    return out


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_builds_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[:1] + ids[-1:], ["shaft", "frame"])
        for p in RIG["parts"]:
            for d in p["drivers"]:
                rigmath.validate_driver(d)
                self.assertEqual(d["type"], "rotate")
                self.assertNotIn("input", d)
        reqs = {p["id"]: p["requires"] for p in RIG["parts"]}
        self.assertEqual({reqs["frame"], reqs["shaft"]}, {None})
        self.assertEqual({reqs["step"], reqs["post"], reqs["pinion"]}, {"post"})
        for pid in PIPES + CONTENTS:
            self.assertEqual(reqs[pid], pid)
        self.assertEqual(set(reqs.values()) - {None}, set(STAGES) | set(PIPES) | set(CONTENTS))

    def test_the_build_lists_the_stages_in_order(self):
        self.assertEqual([s["requires"] for s in RIG["build"]["stages"]], list(STAGES))
        self.assertEqual([req for req, _ in make_shape.STAGES], list(STAGES))
        self.assertFalse([k for k in RIG if k in ("levels", "tiers")])

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
        self.assertEqual({k: SHAPE["textures"][k] for k in PIPES},
                         {"pipecopper": "game:block/metal/sheet-plain/copper4", "pipelead": "game:block/metal/sheet-plain/lead4"})
        self.assertEqual({k: SHAPE["textures"][k] for k in CONTENTS},
                         {"charge": "game:block/stone/gravel/granite", "paste": "game:block/stone/sand/granite"})

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
        for c in RIG["cells"]:
            self.assertTrue(c.get("boxes"), c["pos"])
            self.assertNotIn("lid", c)
            self.assertNotIn("hollow", c)
            for b in c["boxes"]:
                self.assertTrue(all(0 <= x <= 1 for x in b) and all(b[k] < b[k + 3] for k in range(3)), (c["pos"], b))


class Motion(unittest.TestCase):
    def test_each_part_turns_at_its_gearings_ratio(self):
        # turns per axle turn: the post a third (its 18-tooth bevel wheel under the 6-tooth pinion)
        want = {"shaft": 1.0, "post": 1 / 3}
        self.assertEqual({p["id"] for p in RIG["parts"] if p["drivers"]}, set(want))
        for p in RIG["parts"]:
            if p["drivers"]:
                self.assertAlmostEqual(p["drivers"][0]["ratio"], want[p["id"]], places=5, msg=p["id"])
        rides = {p["id"]: p["ride"] for p in RIG["parts"] if p["ride"]}
        self.assertEqual(rides, {"pinion": "shaft", "arms": "post", "stones": "post"})

    def test_the_machine_repeats_every_three_turns(self):
        # within the ratio's six places (a third is written 0.333333)
        for p in RIG["parts"]:
            a, b = matrix(p["id"], 0.0), matrix(p["id"], 3 * 2 * math.pi)
            self.assertLess(max(abs(a[i][j] - b[i][j]) for i in range(3) for j in range(4)), 1e-4, p["id"])


class Anchors(unittest.TestCase):
    def test_cells_and_faces_batch_by_hand(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        # the ground layer, and over it the middle column only: nothing of the machine is over the pit's four corners
        self.assertEqual(cells, {(x, 0, z) for x in (-1, 0, 1) for z in (0, 1)} | {(0, 1, 0), (0, 1, 1)})
        got = {k: (tuple(RIG[k + "Cell"]), RIG[k + "Face"]) for k in ("power", "water", "load", "draw")}
        self.assertEqual(got, {"power": ((0, 1, 1), "south"), "water": ((0, 0, 1), "south"), "load": ((0, 1, 0), "up"),
                               "draw": ((0, 0, 0), "north")})
        # batch equipment, loaded and emptied by hand: no chute, no infeed, no output, no return
        self.assertFalse([k for k in RIG if re.search(r"infeed|output|chute|return", k, re.I)])
        self.assertFalse([k for k, v in SHAPE["textures"].items() if "chute" in k or "chute" in v])

    def test_the_axle_meets_the_power_face_high_and_the_hopper_alone_reaches_the_top(self):
        # shipped voxels: the power cell [0,1,1] spans x 0..16, y 16..32, z 16..32
        axle = aabb_of([e for e in posed_part("shaft") if e.name.startswith("sh_axle")])
        self.assertAlmostEqual(axle[1][2], 32.0, places=4)
        self.assertAlmostEqual((axle[0][0] + axle[1][0]) / 2, 8.0, places=4)
        self.assertAlmostEqual((axle[0][1] + axle[1][1]) / 2, 24.0, places=4)
        # the load: the hopper's mouth is the load cell's top face [0,1,0], and nothing else reaches the machine's top
        mouth = aabb_of(posed_part("hopper"))
        self.assertAlmostEqual(mouth[1][1], 32.0, places=3)
        self.assertTrue(0.0 <= mouth[0][0] and mouth[1][0] <= 16.0 and 0.0 <= mouth[0][2] and mouth[1][2] <= 16.0)
        high = set()
        for el in flatten(SHAPE["elements"], textures={}):
            pid = rigmath.part_of(RIG["parts"], el.name)
            if rigmath.posed(el, matrix(pid, 0.0)).aabb()[1][1] > 31.5:
                high.add(pid)
        self.assertEqual(high, {"hopper"})

    def test_the_water_inlet_is_a_ppex_section_on_the_water_face_pouring_into_the_pit(self):
        # drawn as the rosser's pipes: the same elements in each metal, only the metal's texture differing
        copper = sorted((e["name"][len("pipecopper"):], e["from"], e["to"]) for e in SHAPE["elements"] if e["name"].startswith("pipecopper_"))
        lead = sorted((e["name"][len("pipelead"):], e["from"], e["to"]) for e in SHAPE["elements"] if e["name"].startswith("pipelead_"))
        self.assertTrue(copper)
        self.assertEqual(copper, lead)
        pipe = aabb_of(posed_part("pipecopper"))
        # shipped: the water cell [0,0,1]; its south face at z 32, the face's centre (x 8, y 8)
        self.assertAlmostEqual(pipe[1][2], 32.0, places=4)
        self.assertEqual([round(pipe[1][k] - pipe[0][k], 3) for k in (0, 1)], [6.0, 6.0])
        self.assertEqual([round((pipe[0][k] + pipe[1][k]) / 2, 3) for k in (0, 1)], [8.0, 8.0])
        # its open end at the curb's inner face, over the pit
        self.assertAlmostEqual(pipe[0][2], make_shape.CZ + make_shape.CURB_R[0], places=4)

    def test_the_draw_gate_is_at_the_front_of_the_curb_in_the_controller_cell(self):
        gate = aabb_of([e for e in posed_part("pit") if "_gate_" in e.name])
        self.assertAlmostEqual(gate[0][2], 0.0, places=4)
        self.assertTrue(0.0 <= gate[0][0] and gate[1][0] <= 16.0 and gate[1][2] <= 16.0 and gate[1][1] <= 16.0)
        sill = aabb_of([e for e in posed_part("pit") if e.name.endswith("_gate_sill")])
        board = aabb_of([e for e in posed_part("pit") if e.name.endswith("_gate_board")])
        self.assertLess(sill[1][1], make_shape.PASTE_TOP)
        self.assertGreater(board[1][1], make_shape.CHARGE_TOP)


class Pit(unittest.TestCase):
    def test_the_arrastra_is_a_paved_pit_at_ground_level(self):
        # no basin on piers: the paving lies on the ground, and the drag stones run on it inside the curb
        pave = [e for e in posed_part("pit") if re.fullmatch(r"pit_pave\d+", e.name)]
        lo, hi = aabb_of(pave)
        self.assertLess(lo[1], 0.05)
        self.assertAlmostEqual(hi[1], make_shape.PAVE_TOP, places=3)
        stones = [e for e in posed_part("stones") if "_stone" in e.name]
        self.assertEqual(len(stones), 4)
        self.assertTrue(all(abs(e.aabb()[0][1] - make_shape.PAVE_TOP) < 1e-3 for e in stones))
        curb = [e for e in posed_part("pit") if re.fullmatch(r"pit_curb\d+", e.name)]
        self.assertEqual(len(curb), make_shape.CURB_N - 2)
        self.assertLess(min(e.aabb()[0][1] for e in curb), 0.05)
        self.assertLessEqual(max(e.aabb()[1][1] for e in curb), make_shape.CURB_TOP + 1e-4)


class Readme(unittest.TestCase):
    def test_the_readme_holds_the_generators_stages(self):
        rows = re.findall(r"^\| (\d) \| `(\w+)` \|", README, re.M)
        self.assertEqual([req for _, req in sorted(rows)], list(STAGES))


if __name__ == "__main__":
    unittest.main()
