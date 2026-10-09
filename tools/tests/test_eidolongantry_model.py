"""The eidolon gantry's generated model files (mods-src/seraphhorizons/EidolonGantry/tools/make_shape.py).

These hold what the generator wrote to its own rules: it reproduces the committed files from the
eidolon's shape as the repository has it now (so a changed eidolon.json fails here until the gantry is
regenerated); the body in the written shape is the eidolon's `hung` pose, element for element, by the
game's pose maths (Eidolon/tools/kin.py), each element under its build stage's part; the rig's
`requires` are one per stage, while the spine (vanilla's, cut off the eidolon: the gantry's) and the ring over its top
peg need nothing, there from the start and after the eidolon has woken; the let-down brings the lowest toe from 3
voxels to the floor, the spine with it; the cells are rebuilt from the shipped shape; the front is open; and the crank
is outside the frame, in its own cell. Run with
`python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import math
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
TOOLS = MOD / "EidolonGantry" / "tools"
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(MOD / "Eidolon" / "tools"))
sys.path.insert(0, str(TOOLS))

import kin  # noqa: E402
from machinegen import rigmath  # noqa: E402
from machinegen.geometry import flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("eidolongantry_make_shape", TOOLS / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

SHAPE_PATH = MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "eidolongantry.json"
FRAME_PATH = MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "eidolongantry_frame.json"
RIG_PATH = MOD / "assets" / "seraphhorizons" / "config" / "eidolongantry-rig.json"
SHAPE = json.loads(SHAPE_PATH.read_text())
FRAME = json.loads(FRAME_PATH.read_text())
RIG = json.loads(RIG_PATH.read_text())
EIDOLON = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "entity" / "eidolon" / "eidolon.json").read_text())
STAGES = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "eidolon-stages.json").read_text())
STAGE_CODES = [s["code"] for s in STAGES["stages"] if s["elements"]]
OFF = make_shape.OFF


def matrix(pid, depth):
    return rigmath.part_matrix(RIG["parts"], pid, {"depth": depth})


def posed_written(depth, parts):
    out = []
    for el in flatten(SHAPE["elements"], textures={}):
        pid = rigmath.part_of(RIG["parts"], el.name)
        if pid in parts:
            out.append(rigmath.posed(el, matrix(pid, depth)))
    return out


def drawn(e):
    return any(f.get("enabled", True) for f in e.get("faces", {}).values())


class Generator(unittest.TestCase):
    def test_the_generator_reproduces_the_files(self):
        with tempfile.TemporaryDirectory() as d:
            run = subprocess.run([sys.executable, str(TOOLS / "make_shape.py"), "--out", d], capture_output=True, text=True)
            self.assertEqual(run.returncode, 0, run.stdout[-3000:] + run.stderr[-3000:])
            for path in (SHAPE_PATH, FRAME_PATH, RIG_PATH):
                self.assertEqual((Path(d) / path.name).read_text(), path.read_text(),
                                 f"{path.name} is not what the generator writes from the current eidolon.json: regenerate it")


class Rig(unittest.TestCase):
    def test_parts_parse_and_requires_are_the_stages(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, set(STAGE_CODES) | {None})
        self.assertEqual(STAGE_CODES[0], "torso")
        for pid in ("ring", "spine"):
            part = next(p for p in RIG["parts"] if p["id"] == pid)
            self.assertEqual((part["requires"], part["ride"]), (None, "hook"), pid)
        staged = {n for st in STAGES["stages"] for n in st["elements"]}
        self.assertIn("spine-hook1", make_shape.spine_names())
        self.assertFalse(staged & make_shape.spine_names())
        for code in STAGE_CODES:
            part = next(p for p in RIG["parts"] if p["id"] == code)
            self.assertEqual((part["requires"], part["match"], part["ride"]), (code, [f"b_{code}_*"], "hook"))

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        self.assertEqual({rigmath.part_of(RIG["parts"], n) for n in names}, {p["id"] for p in RIG["parts"]})
        self.assertEqual({e["name"] for e in FRAME["elements"]}, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_textures_are_declared(self):
        used = {f["texture"].lstrip("#") for e in SHAPE["elements"] for f in e["faces"].values()}
        self.assertLessEqual(used, set(SHAPE["textures"]))
        for code, path in EIDOLON["textures"].items():
            self.assertEqual(SHAPE["textures"].get(code, path), path, code)

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        self.assertEqual(len(RIG["cells"]), make_shape.CELLS_X * make_shape.CELLS_Y * make_shape.CELLS_Z + 1)  # and the crank's
        self.assertIn("boxes", RIG["cells"][0])

    def test_anchors(self):
        self.assertEqual(RIG["exitSide"], "west")
        self.assertEqual((RIG["crankCell"], RIG["crankFace"]), ([5, 1, 5], "south"))
        self.assertNotIn("winchCell", RIG)
        self.assertEqual(RIG["body"]["pos"], [v / 16 for v in make_shape.BODY_AT])
        for key in ("hang", "fit", "exit"):
            self.assertEqual(len(RIG[key]["pos"]), 3)


class Body(unittest.TestCase):
    def test_the_written_body_is_the_hung_pose(self):
        # flattened as the game reads the shape, against kin's pose of the current eidolon.json at hung's frame
        rig = kin.Rig(make_shape.with_spine(EIDOLON))
        anim = next(a for a in EIDOLON["animations"] if a["code"] == "hung")
        mats = rig.all_matrices(kin.sample(anim, 0))
        written = {el.name: el for el in flatten(SHAPE["elements"], textures={}) if el.name.startswith(("b_", "sp_"))}
        self.assertTrue(any(n.startswith("sp_") for n in written))
        worst = 0.0
        for name, el in written.items():
            src = make_shape.source_name(name)
            want = [[p[k] + OFF[k] for k in range(3)] for p in rig.corners(src, mats[src])]
            worst = max(worst, max(min(math.dist(p, q) for q in el.corners()) for p in want))
        self.assertLess(worst, 0.01)

    def test_every_drawn_element_is_in_its_stage_once(self):
        stage = {n: s["code"] for s in STAGES["stages"] for n in s["elements"]}
        rig = kin.Rig(EIDOLON)
        want = {f"b_{stage[n]}_{n}" for n in rig.order if drawn(rig.elements[n])}
        have = {e["name"] for e in SHAPE["elements"] if e["name"].startswith("b_")}
        self.assertEqual(have, want)
        for name in have:
            self.assertEqual(rigmath.part_of(RIG["parts"], name), name.split("_", 2)[1])
        spine = {e["name"] for e in SHAPE["elements"] if e["name"].startswith("sp_")}
        self.assertEqual(spine, {f"sp_{n}" for n in make_shape.spine_names()})
        for name in spine:
            self.assertEqual(rigmath.part_of(RIG["parts"], name), "spine")

    def test_the_mind_glows(self):
        glowing = {e["name"] for e in SHAPE["elements"] if any(f.get("glow") for f in e["faces"].values())}
        self.assertTrue(glowing)
        self.assertTrue(any(n.startswith("b_mind_") for n in glowing))

    def test_let_down_brings_the_lowest_toe_from_three_voxels_to_the_floor(self):
        for depth, want in ((0.0, 3.0), (1.0, 0.0)):
            body = posed_written(depth, set(STAGE_CODES))
            low = min(min(c[1] for c in el.corners()) for el in body)
            self.assertAlmostEqual(low, want, delta=0.01)
        self.assertAlmostEqual(RIG["winch"]["drop"], 3.0 / 16, places=4)

    def test_the_hook_and_ring_come_down_with_the_body(self):
        for pid in ("hook", "ring", "spine", *STAGE_CODES):
            m = matrix(pid, 1.0)
            self.assertAlmostEqual(m[1][3], -3.0 / 16, places=4, msg=pid)


class Frame(unittest.TestCase):
    def test_the_front_is_open(self):
        # nothing of the gantry below the front beam between the front posts, across the front cell row
        z0, z1 = make_shape.Z_RIGHT[1], make_shape.Z_LEFT[0]
        for el in posed_written(0.0, {"frame", "winch", "sheave", "lead"}):
            lo, hi = el.aabb()
            if lo[0] < 16 and lo[2] < z1 - 0.01 and hi[2] > z0 + 0.01:
                self.assertGreaterEqual(lo[1], 64.0, el.name)

    def test_inside_the_machine_box_or_the_cranks_cell(self):
        regions = make_shape.regions()
        for depth in (0.0, 1.0):
            for el in posed_written(depth, {p["id"] for p in RIG["parts"]}):
                lo, hi = el.aabb()
                self.assertTrue(any(all(r[0][k] - 0.01 <= lo[k] and hi[k] <= r[1][k] + 0.01 for k in range(3)) for r in regions),
                                el.name)

    def test_the_crank_is_outside_the_frame_and_turns_with_the_drum(self):
        outer = make_shape.CELLS_Z * 16
        els = {el.name: el for el in posed_written(0.0, {"winch"})}
        for name in ("wn_crank", "wn_handle"):
            self.assertGreater(els[name].aabb()[0][2], outer, name)
        self.assertIn(list(make_shape.CRANK_CELL), [c["pos"] for c in RIG["cells"]])
        self.assertIn("boxes", next(c for c in RIG["cells"] if c["pos"] == list(make_shape.CRANK_CELL)))
        # one part: the drum, its axle and the crank turn together as the winch lets down
        self.assertEqual({rigmath.part_of(RIG["parts"], n) for n in ("wn_drum_1", "wn_axle_1", "wn_crank", "wn_handle")}, {"winch"})
        turned = matrix("winch", 1.0)
        self.assertGreater(abs(turned[0][1]), 0.5)   # turned well over half a radian


if __name__ == "__main__":
    unittest.main()
