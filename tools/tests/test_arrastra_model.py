"""The arrastra's generated model files (mods-src/seraphhorizons/Arrastra/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared rig
maths and uses the build's `requires` vocabulary, its `build` lists the stages in order, its reference poses are its
own maths, the cells are rebuilt from the shipped shape, the frame shape is the frame part, the only input is the
axle's angle and the machine repeats every three turns at its gearing's ratio, the anchors are where the layout puts
them (batch, loaded, watered and emptied by hand: no load anchor, no hopper, no chute, no output and no pipe), the pit is at ground level, and
the README's stage table is the generator's. Run with `python3 -m unittest discover -s tools/tests -p test_arrastra_model.py`.
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
STAGES = ("pit", "post", "arms", "stones")
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
        for pid in CONTENTS:
            self.assertEqual(reqs[pid], pid)
        self.assertEqual(set(reqs.values()) - {None}, set(STAGES) | set(CONTENTS))

    def test_the_build_lists_the_stages_in_order(self):
        self.assertEqual([s["requires"] for s in RIG["build"]["stages"]], list(STAGES))
        self.assertEqual([req for req, _ in make_shape.STAGES], list(STAGES))

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
        # watered by hand: no pipe, so none of the pipe's textures
        self.assertFalse([k for k, v in SHAPE["textures"].items() if re.search(r"pipe|solder", k + v)])
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
        # turns per axle turn: the post a third (its 12-cog wooden wheel under the 4-stave lantern)
        want = {"shaft": 1.0, "post": 1 / 3}
        self.assertEqual({p["id"] for p in RIG["parts"] if p["drivers"]}, set(want))
        for p in RIG["parts"]:
            if p["drivers"]:
                self.assertAlmostEqual(p["drivers"][0]["ratio"], want[p["id"]], places=5, msg=p["id"])
        rides = {p["id"]: p["ride"] for p in RIG["parts"] if p["ride"]}
        self.assertEqual(rides, {"pinion": "shaft", "arms": "post", "stones": "post"})

    def test_the_gearing_is_wooden_and_chunky(self):
        # the owner's ruling: a wooden cog wheel and lantern pinion, iron only for the hoops and bands; 4 staves to 12
        # cogs (3:1), each cog and stave thicker across than the 1.0 of the iron teeth it replaced
        wood = SHAPE["textures"]["mechanics"]
        self.assertIn("/wood/", wood)
        els = flatten(SHAPE["elements"], textures={})
        gearing = [el for el in els if re.match(r"post_wheel_|pinion_", el.name)]
        for el in gearing:
            tex = {f["texture"].lstrip("#") for f in el.faces.values()}
            self.assertEqual(tex, {"iron"} if "_hoop" in el.name else {"mechanics"}, el.name)
        cogs = [el for el in gearing if re.search(r"_cog\d+a$", el.name)]
        staves = {re.sub(r"\d$", "", el.name) for el in gearing if "_stave" in el.name}
        self.assertEqual((len(cogs), len(staves)), (12, 4))
        for el in cogs + [el for el in gearing if "_stave" in el.name]:
            self.assertGreaterEqual(sorted(abs(x) for x in el.size)[1], 1.2, el.name)

    def test_the_machine_repeats_every_three_turns(self):
        # within the ratio's six places (a third is written 0.333333)
        for p in RIG["parts"]:
            a, b = matrix(p["id"], 0.0), matrix(p["id"], 3 * 2 * math.pi)
            self.assertLess(max(abs(a[i][j] - b[i][j]) for i in range(3) for j in range(4)), 1e-4, p["id"])


class Anchors(unittest.TestCase):
    def test_cells_and_faces_batch_and_watered_by_hand(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        # the ground layer, and over it the middle column only: nothing of the machine is over the pit's four corners
        self.assertEqual(cells, {(x, 0, z) for x in (-1, 0, 1) for z in (0, 1)} | {(0, 1, 0), (0, 1, 1)})
        got = {k: (tuple(RIG[k + "Cell"]), RIG[k + "Face"]) for k in ("power", "draw")}
        self.assertEqual(got, {"power": ((0, 1, 1), "south"), "draw": ((0, 0, 0), "north")})
        # batch equipment, loaded, watered (by the bucket) and emptied by hand, nothing automated feeding it: no load,
        # feed, chute, pour or water anchor, no output, no return
        self.assertFalse([k for k in RIG if re.search(r"load|feed|chute|hopper|pour|water|output|return|fill|input", k, re.I)])
        self.assertFalse([e.name for e in flatten(SHAPE["elements"], textures={}) if re.search(r"hopper|spout|chute", e.name)])
        self.assertFalse([k for k, v in SHAPE["textures"].items() if "chute" in k or "chute" in v])

    def test_the_axle_meets_the_power_face_high_and_the_pit_is_open_to_tip_into(self):
        # shipped voxels: the power cell [0,1,1] spans x 0..16, y 16..32, z 16..32
        axle = aabb_of([e for e in posed_part("shaft") if e.name.startswith("sh_axle")])
        self.assertAlmostEqual(axle[1][2], 32.0, places=4)
        self.assertAlmostEqual((axle[0][0] + axle[1][0]) / 2, 8.0, places=4)
        self.assertAlmostEqual((axle[0][1] + axle[1][1]) / 2, 24.0, places=4)
        # the charge and the water are tipped into the pit by hand over a low curb: nothing fixed stands over the pit
        # above the curb outside the middle column (shipped x 0..16), where only the sweep passes
        curb = [e for e in posed_part("pit") if re.fullmatch(r"pit_curb\d+", e.name)]
        rim = max(e.aabb()[1][1] for e in curb)
        self.assertLessEqual(rim, make_shape.TIP_RIM_MAX)
        cx, cz = make_shape.CX - 16.0, make_shape.CZ
        over = []
        for el in flatten(SHAPE["elements"], textures={}):
            pid = rigmath.part_of(RIG["parts"], el.name)
            if pid in make_shape.SWEEP_PARTS:
                continue
            lo, hi = rigmath.posed(el, matrix(pid, 0.0)).aabb()
            near = math.hypot(max(lo[0], min(cx, hi[0])) - cx, max(lo[2], min(cz, hi[2])) - cz)
            if hi[1] > rim + 1e-6 and (lo[0] < -1e-6 or hi[0] > 16.0 + 1e-6) and near < make_shape.CURB_R[1]:
                over.append(el.name)
        self.assertEqual(over, [])

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
        self.assertEqual(len(curb), make_shape.CURB_N - 1)
        # every curb stone to the curb's top: none is cut down (there is no pipe), only the gate's is left out
        self.assertTrue(all(abs(e.aabb()[1][1] - make_shape.CURB_TOP) < 0.05 for e in curb))
        self.assertLess(min(e.aabb()[0][1] for e in curb), 0.05)
        self.assertLessEqual(max(e.aabb()[1][1] for e in curb), make_shape.CURB_TOP + 1e-4)


class Readme(unittest.TestCase):
    def test_the_readme_holds_the_generators_stages(self):
        rows = re.findall(r"^\| (\d) \| `(\w+)` \|", README, re.M)
        self.assertEqual([req for _, req in sorted(rows)], list(STAGES))


if __name__ == "__main__":
    unittest.main()
