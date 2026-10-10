"""The draw bench's generated model files (mods-src/seraphhorizons/DrawBench/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with
the shared rig maths and uses the `requires` vocabulary of the contract, its reference poses are its own
maths, the cells are rebuilt from the shipped shape, the work counts sections, the drawn pace is the rig's
`draw.turnsPerSection`, a stroke ends where it started, and each section leaves the model at `draw.handOut`. Run with `python3 -m unittest discover -s tools/tests`.
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
sys.path.insert(0, str(MOD / "DrawBench" / "tools"))

from machinegen import checks, rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("drawbench_make_shape", MOD / "DrawBench" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "drawbench-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "drawbench.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "drawbench_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "DrawBench" / "rig-reference.json").read_text())
BLOCKTYPE = (MOD / "assets" / "seraphhorizons" / "blocktypes" / "drawbench" / "frame.json").read_text()
# the build order: the frame, gearbox, chain, dog, mandrel, die; the work is a lead or a copper hollow section, drawn into four pipe sections
REQUIRES = {"gearbox", "chain", "dog", "mandrel", "die", "billetlead", "billetcopper", None}


def inputs(pose):
    return {k: pose[k] for k in ("theta", "travel", "work", "size", "presence", "oil")}


def matrix(pid, **ins):
    return rigmath.part_matrix(RIG["parts"], pid, ins, RIG["work"])


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            if p.get("ride"):
                self.assertIn(p["ride"], ids)
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_the_die_wears_its_own_texture_code(self):
        # the renderer sets the die's texture to the fitted die's metal: its elements use the code "die" alone
        die = [e for e in SHAPE["elements"] if rigmath.part_of(RIG["parts"], e["name"]) == "die"]
        self.assertTrue(die)
        self.assertEqual({f["texture"] for e in die for f in e["faces"].values()}, {"#die"})
        self.assertIn("die", SHAPE["textures"])

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreater(len(poses), 40)
        self.assertEqual({p["size"] for p in poses}, {0, 1, 2})
        for pose in poses[::3]:
            for pid, want in pose["matrices"].items():
                got = rigmath.part_matrix(RIG["parts"], pid, inputs(pose), RIG["work"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {inputs(pose)}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        cells = make_shape.shipped_cells(SHAPE, RIG["parts"], RIG["work"])
        self.assertEqual(cells, RIG["cells"])


class Draw(unittest.TestCase):
    def test_work_counts_sections(self):
        self.assertNotIn("trunkPath", RIG)
        w = rigmath.progress_of(RIG)
        self.assertEqual((w["name"], w["unit"]), ("sections drawn", "sections"))
        self.assertEqual(w["end"], {"thin": 4.0, "thick": 4.0})
        self.assertEqual(RIG["draw"]["sectionsPerHollow"], 4)
        self.assertEqual(RIG["draw"]["hollows"], {"thin": "game:chutesection-lead", "thick": "game:chutesection-copper"})
        for old in ("ingots", "sections", "sectionsPerIngot", "pipesPerSection", "pipesPerIngot", "turnsPerPipe"):
            self.assertNotIn(old, RIG["draw"])

    def test_turns_per_section_is_the_drawn_gearing(self):
        # copper takes twice the axle's turns a section: its change-gear pair is 10:30 against lead's 16:24
        tpp = RIG["draw"]["turnsPerSection"]
        self.assertAlmostEqual(tpp["thick"], 2 * tpp["thin"], places=5)
        for cls in ("thin", "thick"):
            self.assertAlmostEqual(tpp[cls], make_shape.turns_per_section(cls), places=5)
        # through a stroke the sleeve turns as the rectified shaft would at that pace: half the axle's travel
        span = make_shape.T_DRAW[1] - make_shape.T_DRAW[0]
        for k, cls in ((1, "thin"), (2, "thick")):
            a = matrix("sleeve", work=make_shape.T_DRAW[0], size=k, presence=1.0)
            b = matrix("sleeve", work=make_shape.T_DRAW[0] + 0.01, size=k, presence=1.0)
            turned = math.atan2(b[2][1], b[1][1]) - math.atan2(a[2][1], a[1][1])
            axle = 2 * math.pi * tpp[cls] * 0.01
            self.assertAlmostEqual(turned, make_shape.DRAW_SIGN * axle * make_shape.RECT_RATIO, places=5, msg=cls)
        self.assertGreater(span, 0)

    def test_a_stroke_ends_where_it_started(self):
        # every part that moves in a stroke stands at the end of a section's cycle as at its start
        cycle = [p["id"] for p in RIG["parts"]
                 if p["id"] in ("dog", "jaw", "clutchrod", "startlever", "crank", "cone", "weight", "driveshaft", "returnshaft", "barrel")
                 or p["id"].startswith(("ch", "rope"))]
        for k in (1, 2):
            for w in (1.0, 2.0, 3.0, 4.0):
                for pid in cycle:
                    a = matrix(pid, work=0.0, size=k, presence=1.0)
                    b = matrix(pid, work=w, size=k, presence=1.0)
                    for r in range(3):
                        for c in range(4):
                            self.assertAlmostEqual(a[r][c], b[r][c], places=5, msg=f"{pid} metal {k} at W {w}")

    def test_the_dog_draws_one_section_a_stroke(self):
        mid = matrix("dog", work=make_shape.T_DRAW[1], size=1, presence=1.0)
        self.assertAlmostEqual(mid[2][3] * 16, make_shape.S_DOG, places=4)
        # a pipe section on the bench is half a block long and 6 across (ppex's pipe; the item is a block long), drawn from a quarter of the hollow (the
        # game's chute section, 8 across and 8 long); pointed through the die, drawn to its tail, handed out as the jaws open
        self.assertEqual((make_shape.PIPE, 2 * make_shape.PIPE_R), (8.0, 6.0))
        self.assertEqual((2 * make_shape.HOLLOW_H, make_shape.HOLLOW_L, make_shape.SLUGS), (8.0, 8.0, 4))
        self.assertAlmostEqual(make_shape.S_TUBE + make_shape.POINT, make_shape.PIPE)

    def test_each_section_leaves_the_model_at_its_hand_out(self):
        # gameplay hands section m + 1 out at W = m + draw.handOut, as the jaws finish opening; from there its
        # parts stand at their rest place, hidden in the die stock (identity), so the item and the model never
        # both show it; just before, it lies where it was drawn
        out = RIG["draw"]["handOut"]
        self.assertAlmostEqual(out, make_shape.T_OUT, places=6)
        self.assertLess(make_shape.T_TUBE, out)
        self.assertLess(out, make_shape.T_RETURN[0])
        ident = [[1.0 if r == c else 0.0 for c in range(4)] for r in range(3)]
        for k, pre in ((1, "l"), (2, "c")):
            for m in range(4):
                for seg in "ab":
                    pid = f"{pre}sect{m + 1}{seg}"
                    before = matrix(pid, work=m + out - make_shape.OUT_EASE, size=k, presence=1.0)
                    self.assertGreater(abs(before[2][3]), 0.1, pid)
                    for w in (m + out, m + 1.0, 4.0):
                        got = matrix(pid, work=w, size=k, presence=1.0)
                        for r in range(3):
                            for c in range(4):
                                self.assertAlmostEqual(got[r][c], ident[r][c], places=6, msg=f"{pid} at W {w}")

    def test_copper_moves_the_change_gear_and_lead_does_not(self):
        lead = matrix("cluster", work=0.0, size=1, presence=1.0)
        copper = matrix("cluster", work=0.0, size=2, presence=1.0)
        self.assertAlmostEqual(lead[0][3], 0.0)
        self.assertAlmostEqual(copper[0][3] * 16, make_shape.SELECT, places=4)
        self.assertEqual(matrix("selector", work=0.0, size=2, presence=1.0)[0][3], copper[0][3])


class Anchors(unittest.TestCase):
    def test_cells_and_faces(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        self.assertEqual(cells, {(0, 0, z) for z in range(4)})
        self.assertEqual(checks.lid_gaps(RIG["cells"]), [])
        power = tuple(RIG["powerCell"])
        self.assertEqual(power, (0, 0, 3))
        self.assertEqual(RIG["powerFace"], "west")
        self.assertNotIn("infeedSide", RIG)
        self.assertEqual(RIG["outputSide"], "east")
        for key in ("output", "die", "drip"):
            self.assertEqual(len(RIG[key]["pos"]), 3)
        # the die anchor is the die's mouth on the draw line (anchors are rounded to 1e-4 blocks)
        x, y, z = (c * 16 for c in RIG["die"]["pos"])
        self.assertAlmostEqual(x, make_shape.DL[0], places=2)
        self.assertAlmostEqual(y, make_shape.DL[1], places=2)
        self.assertAlmostEqual(z, make_shape.Z_MOUTH, places=2)
        # sections are handed out at the east face of the die end's cell, beside the trough's north end
        ox, _, oz = RIG["output"]["pos"]
        self.assertTrue(0.9 < ox < 1.0 and 0.0 < oz < 1.0)
        self.assertNotIn("oil", RIG)        # the oil is MachineOil's tank: no fill anchor

    def test_the_oil_level_follows_the_oil_input(self):
        (lvl,) = [p for p in RIG["parts"] if p["id"] == "oillevel"]
        self.assertEqual([d["input"] for d in lvl["drivers"]], ["oil"])
        heights = [matrix("oillevel", oil=oil)[1][1] * lvl["drivers"][0]["length"] * 16 for oil in (0.0, 0.5, 1.0)]
        self.assertAlmostEqual(heights[0], make_shape.OIL_EMPTY, places=5)
        self.assertAlmostEqual(heights[2], make_shape.OIL_FULL, places=5)
        self.assertAlmostEqual(heights[1], (heights[0] + heights[2]) / 2, places=5)


class Textures(unittest.TestCase):
    """The renderer draws every part with the frame block's texture source, so a code the shape uses and the
    block does not declare renders white (the hollow's `leadsheet` and `coppersheet` once did)."""

    @staticmethod
    def block_textures():
        (body,) = re.findall(r"\btextures:\s*\{(.*?)\n\t\}", BLOCKTYPE, re.S)
        return dict(re.findall(r'"(\w+)":\s*\{\s*base:\s*"([^"]+)"', body))

    def test_the_block_declares_every_texture_of_the_shape(self):
        self.assertEqual(self.block_textures(), SHAPE["textures"])
        for code, path in FRAME["textures"].items():
            self.assertEqual(SHAPE["textures"][code], path, code)

    def test_every_face_uses_a_declared_code(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertLessEqual(used, set(shape["textures"]))

    def test_every_texture_exists_in_the_game(self):
        install = os.environ.get("VINTAGE_STORY")
        if not install:
            raise unittest.SkipTest("VINTAGE_STORY is not set to a game or server install")
        assets = Path(install) / "assets"
        for code, path in SHAPE["textures"].items():
            domain, rel = path.split(":", 1)
            self.assertEqual(domain, "game", code)
            self.assertTrue((assets / "survival" / "textures" / f"{rel}.png").is_file(), f"{code}: {path}")


if __name__ == "__main__":
    unittest.main()
