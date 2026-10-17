"""The ball mill's generated model files (mods-src/seraphhorizons/BallMill/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared
rig maths and uses its `requires` vocabulary (one per fitted part, the pipe's metal), its reference poses are its
own maths, the cells are rebuilt from the shipped shape, the frame shape is the frame part, the only input is the
axle's angle (the pinion at 1, the drum and what rides it at -1/3, back where they started after three turns),
the anchors are where the layout puts them (the axle onto the pinion's shaft, the infeed and output as the game's
chute at their faces, the water pipe on its face's axis), and every texture is declared. Run with
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
sys.path.insert(0, str(MOD / "BallMill" / "tools"))

from machinegen import rigmath  # noqa: E402
from machinegen.geometry import aabb_of, flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("ballmill_make_shape", MOD / "BallMill" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "ballmill-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "ballmill.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "ballmill_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "BallMill" / "rig-reference.json").read_text())
FITTED = ("pinion", "drum", "girth", "screens", "feedbox", "hopper", "chutes")
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
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(ids, list(FITTED) + list(PIPES) + ["frame"])
        for p in RIG["parts"]:
            for d in p["drivers"]:
                rigmath.validate_driver(d)
                self.assertEqual((d["type"], d["axis"]), ("rotate", "x"))
                self.assertNotIn("input", d)
            self.assertEqual(p["requires"], None if p["id"] == "frame" else p["id"])
        self.assertIsNone(rigmath.progress_of(RIG))
        for key in ("work", "trunkPath"):
            self.assertNotIn(key, RIG)

    def test_every_element_has_a_part_and_the_frame_shape_is_the_frame(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_the_build_stages_are_the_owners(self):
        # owner, 2026-10-15: the frame (bed, pedestals, bearings) placed; then the drum, the gearing (girth wheel,
        # pinion), the screens, the sheet iron (hopper, feed box), and services last (the chutes and the water pipe)
        self.assertEqual([(s["stage"], s["id"], s["adds"]) for s in RIG["buildStages"]], [
            (1, "drum", ["drum"]), (2, "gearing", ["girth", "pinion"]), (3, "screens", ["screens"]),
            (4, "sheetiron", ["hopper", "feedbox"]), (5, "services", ["chutes", *PIPES])])
        staged = [r for s in RIG["buildStages"] for r in s["adds"]]
        self.assertEqual(sorted(staged), sorted(p["requires"] for p in RIG["parts"] if p["requires"]))

    def test_no_balls_are_drawn(self):
        # the owner's ruling: the drum's charge of iron balls is not modelled
        self.assertFalse([e["name"] for e in SHAPE["elements"] if e["name"].startswith("ball")])
        self.assertNotIn("balls", {p["requires"] for p in RIG["parts"]})

    def test_the_girth_wheel_is_wooden_and_the_pinion_stainless(self):
        # the owner's rulings: an oak mortise wheel (felloes, cogs), iron only its bolts and tyre; a metal gear is stainless
        tex = {e["name"]: {f["texture"].lstrip("#") for f in e["faces"].values()} for e in SHAPE["elements"]}
        girth = {n: t for n, t in tex.items() if n.startswith("girth_")}
        self.assertEqual(sum(n.startswith("girth_cog") for n in girth), make_shape.GIRTH_T)
        self.assertTrue(any(n.startswith("girth_felloe2_") for n in girth))
        self.assertEqual(sum(n.startswith("girth_band") for n in girth), 2 * make_shape.GIRTH_T)   # the iron tyre
        for n, t in girth.items():
            self.assertLessEqual(t, {"iron"} if n.startswith(("girth_bolt", "girth_band")) else {"oak", "oak-end"}, n)
        gear = set().union(*(t for n, t in tex.items() if n.startswith("pinion_gear_")))
        self.assertEqual(gear, {"stainless"})
        self.assertEqual({k: SHAPE["textures"][k] for k in ("oak", "oak-end", "stainless")},
                         {"oak": "game:block/wood/debarked/oak", "oak-end": "game:block/wood/treetrunk/debarked/oak",
                          "stainless": "game:block/metal/plate/stainlesssteel"})
        self.assertGreater(make_shape.COG_W, make_shape.PINION_W)
        self.assertLess(make_shape.GIRTH_R + make_shape.MODULE, make_shape.HEAD_R)

    def test_textures_are_declared(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertLessEqual(used, set(shape["textures"]))
        self.assertEqual({k: SHAPE["textures"][k] for k in ("perforated", "wirecloth", "riveted")},
                         {"perforated": "game:block/metal/mesh1", "wirecloth": "game:block/metal/mesh4",
                          "riveted": "game:block/metal/riveted/iron1"})
        self.assertEqual({k: SHAPE["textures"][k] for k in PIPES},
                         {"pipecopper": "game:block/metal/sheet-plain/copper4", "pipelead": "game:block/metal/sheet-plain/lead4"})

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreaterEqual(len(poses), 10)
        for pose in poses:
            self.assertEqual(set(pose["matrices"]), {p["id"] for p in RIG["parts"]})
            for pid, want in pose["matrices"].items():
                got = matrix(pid, pose["theta"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {pose['theta']}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])

    def test_every_cell_has_boxes_and_the_top_layer_a_lid(self):
        cells = {tuple(c["pos"]): c for c in RIG["cells"]}
        self.assertEqual(set(cells), {(x, y, z) for x in (-1, 0, 1) for y in (0, 1) for z in (0, 1)})
        for pos, c in cells.items():
            self.assertTrue(c.get("boxes"), pos)
            self.assertNotIn("hollow", c)
            for b in c["boxes"]:
                self.assertTrue(all(0 <= x <= 1 for x in b) and all(b[k] < b[k + 3] for k in range(3)), (pos, b))
            self.assertEqual("lid" in c, pos[1] == 1, pos)


class Motion(unittest.TestCase):
    def test_the_pinion_turns_with_the_axle_and_the_drum_a_third_the_other_way(self):
        want = {"pinion": 1.0, "drum": -1 / 3}
        for p in RIG["parts"]:
            if p["drivers"]:
                self.assertAlmostEqual(p["drivers"][0]["ratio"], want[p["id"]], places=5, msg=p["id"])
        self.assertEqual({p["id"] for p in RIG["parts"] if p["drivers"]}, set(want))
        self.assertEqual({p["id"]: p["ride"] for p in RIG["parts"] if p["ride"]}, {"girth": "drum", "screens": "drum"})
        # the pivots: the pinion's on the power face's axis, the drum's on its own (shipped blocks)
        piv = {p["id"]: p["drivers"][0]["pivot"] for p in RIG["parts"] if p["drivers"]}
        self.assertEqual(piv["pinion"][1:], [0.5, 0.5])
        self.assertEqual(piv["drum"][1:], [make_shape.DRUM_Y / 16, make_shape.DRUM_Z / 16])

    def test_the_teeth_set_the_ratio(self):
        # wooden cogs want a coarse pitch: 8:24 (the drum a third of the axle, as before)
        self.assertEqual((make_shape.PINION_T, make_shape.GIRTH_T), (8, 24))
        self.assertAlmostEqual(make_shape.PINION_R + make_shape.GIRTH_R, make_shape.CENTRES, places=9)

    def test_the_machine_repeats_every_three_turns(self):
        # within the ratios' six places (a third is written 0.333333)
        for p in RIG["parts"]:
            a, b = matrix(p["id"], 0.0), matrix(p["id"], 3 * 2 * math.pi)
            self.assertLess(max(abs(a[i][j] - b[i][j]) for i in range(3) for j in range(4)), 1e-4, p["id"])

    def test_nothing_else_moves(self):
        for pid in ("feedbox", "hopper", "chutes", "frame") + PIPES:
            for theta in (0.0, 2.5, -7.0):
                m = matrix(pid, theta)
                self.assertEqual([[round(v, 12) + 0.0 for v in row] for row in m], rigmath.m4(), pid)


class Anchors(unittest.TestCase):
    def test_cells_and_faces(self):
        got = {k: (tuple(RIG[k + "Cell"]), RIG[k + "Face"]) for k in ("power", "infeed", "water", "output")}
        self.assertEqual(got, {"power": ((1, 0, 0), "east"), "infeed": ((-1, 1, 0), "west"), "water": ((-1, 1, 1), "west"),
                               "output": ((0, 0, 1), "south")})
        # the middlings come back by the infeed's chute line (the player merges them): no anchor of their own
        self.assertFalse([k for k in RIG if k.startswith("return") or k.startswith("middlings")])
        self.assertEqual(RIG["output"]["pos"], [0.5, 5 / 16, 2.0])

    def test_the_axle_meets_the_power_face_on_the_pinions_shaft(self):
        # shipped voxels: the power cell [1,0,0] spans x 16..32, y 0..16, z 0..16; the vanilla axle's cross
        axle = aabb_of([e for e in posed_part("pinion") if e.name.startswith("pinion_axle")])
        self.assertAlmostEqual(axle[1][0], 32.0, places=4)
        self.assertEqual([round((axle[0][k] + axle[1][k]) / 2, 4) for k in (1, 2)], [8.0, 8.0])
        self.assertEqual([round(axle[1][k] - axle[0][k], 4) for k in (1, 2)], [4.0, 4.0])
        rod = aabb_of([e for e in posed_part("pinion") if e.name.startswith("pinion_rod")])
        self.assertEqual([round((rod[0][k] + rod[1][k]) / 2, 4) for k in (1, 2)], [8.0, 8.0])

    def test_the_infeed_and_output_are_the_games_chute_at_their_faces(self):
        # a chute-straight-we in the cell west of the infeed, and a chute-straight-ns south of the output, meet them
        # wall for wall in the game's chute texture (shipped: the infeed cell [-1,1,0] spans x -16..0, y 16..32, z 0..16;
        # the output cell [0,0,1] x 0..16, y 0..16, z 16..32)
        for pid, side, (ox, oy, oz), k, face in (("in", "we", (-16, 16, 0), 0, -16.0), ("out", "ns", (0, 0, 16), 2, 32.0)):
            walls = [e for e in posed_part("chutes") if re.search(rf"^chutes_{pid}_(north|south|bottom|top)$", e.name)]
            a = 2 if side == "we" else 0
            o = (ox, oy, oz)
            got = sorted((round(lo[a] - o[a], 2), round(lo[1] - o[1], 2), round(hi[a] - o[a], 2), round(hi[1] - o[1], 2))
                         for lo, hi in (e.aabb() for e in walls))
            self.assertEqual(got, [tuple(round(x, 2) for x in r) for r in make_shape.chute_profile(side)], pid)
            end = min(e.aabb()[0][k] for e in walls) if face < 0 else max(e.aabb()[1][k] for e in walls)
            self.assertAlmostEqual(end, face, places=4, msg=pid)
        self.assertEqual(SHAPE["textures"]["chute"], "game:block/metal/sheet/copper1")
        self.assertEqual({f["texture"] for e in SHAPE["elements"] if e["name"].startswith("chutes_") for f in e["faces"].values()}, {"#chute"})

    def test_the_water_pipe_is_a_ppex_section_on_the_water_face(self):
        # drawn as the rosser's pipes: the same elements in each metal, only the metal's texture differing
        copper = sorted((e["name"][len("pipecopper"):], e["from"], e["to"]) for e in SHAPE["elements"] if e["name"].startswith("pipecopper_"))
        lead = sorted((e["name"][len("pipelead"):], e["from"], e["to"]) for e in SHAPE["elements"] if e["name"].startswith("pipelead_"))
        self.assertTrue(copper)
        self.assertEqual(copper, lead)
        inlet = aabb_of([e for e in posed_part("pipecopper") if "_inlet_" in e.name])
        self.assertAlmostEqual(inlet[0][0], -16.0, places=4)
        self.assertEqual([round(inlet[1][k] - inlet[0][k], 3) for k in (1, 2)], [6.0, 6.0])
        self.assertEqual([round((inlet[0][k] + inlet[1][k]) / 2, 3) for k in (1, 2)], [24.0, 24.0])


class Layout(unittest.TestCase):
    def test_the_drum_is_about_the_issues_size(self):
        # about 1.5 blocks across and two long (#916): measured over the heads and the screens at rest
        drum = [e for pid in ("drum", "screens") for e in posed_part(pid) if not re.search(r"trunnion|journal", e.name)]
        lo, hi = aabb_of(drum)
        self.assertGreater((hi[2] - lo[2]) / 16, 1.4)
        self.assertGreater((hi[0] - lo[0]) / 16, 1.6)

    def test_the_hopper_is_under_the_screens_and_the_feed_box_at_the_trunnions_mouth(self):
        hopper = aabb_of(posed_part("hopper"))
        drum = aabb_of([e for e in posed_part("screens")])
        self.assertLess(hopper[1][1], make_shape.DRUM_Y)
        self.assertLessEqual(hopper[0][0], drum[0][0])
        self.assertGreaterEqual(hopper[1][0], drum[1][0])
        feed = aabb_of([e for e in posed_part("feedbox") if not e.name.startswith("feedbox_spout")])
        trunnion = aabb_of([e for e in posed_part("drum") if "_wtrunnion" in e.name])
        self.assertLess(feed[1][0], trunnion[0][0])
        spout = aabb_of([e for e in posed_part("feedbox") if e.name.startswith("feedbox_spout")])
        self.assertGreater(spout[1][0], trunnion[0][0] + 1.0)


if __name__ == "__main__":
    unittest.main()
