"""The concentrator's generated model files (mods-src/seraphhorizons/Concentrator/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the
shared rig maths and uses its `requires` vocabulary (a level's parts, the plates and their dressing, the
water line in each metal), its
levels add parts and only level 2 takes one out (the sluice), its reference poses are its own maths, the
cells and their per-level collision are rebuilt from the shipped shape, every sheave's yoke follows its
sheave exactly, the vanner's belt creeps to its head whichever way the axle turns (its ratchet), the water
line is the same soldered pipe in either metal, the anchors are on their faces
(one water inlet; the return water and the tailings leave by cells of their own), the site's states are
the rig's levels, and the README's parts table lists the rig's parts.
Run with `python3 -m unittest discover -s tools/tests`.
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
ASSETS = MOD / "assets" / "seraphhorizons"
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(MOD / "Concentrator" / "tools"))

from machinegen import rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("concentrator_make_shape", MOD / "Concentrator" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((ASSETS / "config" / "concentrator-rig.json").read_text())
SHAPE = json.loads((ASSETS / "shapes" / "block" / "concentrator.json").read_text())
FRAME = json.loads((ASSETS / "shapes" / "block" / "concentrator_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "Concentrator" / "rig-reference.json").read_text())
README = (MOD / "Concentrator" / "README.md").read_text()
MANIFEST = json.loads((ROOT / "site" / "models.json").read_text())
LEVEL_REQUIRES = ["sluice", "lineshaft", "jig", "table", "infeed", "vanner"]
PLATES = ["plates", "dressing"]
PIPES = ["pipecopper", "pipelead"]


def matrix(pid, theta, travel=None):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta, "travel": abs(theta) if travel is None else travel}, None)


def point(pid, theta, p):
    """A point (voxels) moved by a part's matrix (turned from rest: the travel is |theta|)."""
    q = rigmath.apply(matrix(pid, theta), [c / 16 for c in p])
    return [c * 16 for c in q]


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        for p in RIG["parts"]:
            self.assertIn(p["requires"], [*LEVEL_REQUIRES, *PLATES, *PIPES, None], p["id"])
            if p.get("ride"):
                self.assertIn(p["ride"], ids)
            for d in p["drivers"]:
                rigmath.validate_driver(d)
                # the ratchet's train alone reads the travel: it only ever turns forward
                want = "travel" if p["id"] in ("vannerworm", "vannerhead", "vannerfoot") else "theta"
                self.assertEqual(rigmath.driver_input(d), want, p["id"])
        self.assertEqual({p["requires"] for p in RIG["parts"]}, {*LEVEL_REQUIRES, *PLATES, *PIPES, None})
        self.assertEqual([p["id"] for p in RIG["parts"] if p["requires"] is None], ["frame"])
        self.assertIsNone(rigmath.progress_of(RIG))

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreaterEqual(len(poses), 6)
        self.assertTrue(any(p["theta"] < 0 for p in poses))
        for pose in poses:
            self.assertEqual(set(pose["matrices"]), {p["id"] for p in RIG["parts"]})
            for pid, want in pose["matrices"].items():
                got = matrix(pid, pose["theta"], pose["travel"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at theta {pose['theta']}")


class Levels(unittest.TestCase):
    def test_four_levels_two_tiers_each_adding_but_level_two_retiring_the_sluice(self):
        levels = RIG["levels"]
        self.assertEqual([lv["level"] for lv in levels], [1, 2, 3, 4])
        self.assertEqual([lv["tier"] for lv in levels], ["water", "water", "mill", "mill"])
        self.assertEqual(levels[0]["requires"], ["sluice"])
        for a, b in zip(levels, levels[1:]):
            self.assertLessEqual(set(a["requires"]) - set(b["requires"]), {"sluice"}, b["level"])
            self.assertTrue(set(b["requires"]) - set(a["requires"]), b["level"])
        self.assertNotIn("sluice", levels[1]["requires"])
        self.assertEqual(RIG["plates"]["requires"], "plates")
        self.assertEqual(RIG["plates"]["dressing"], "dressing")
        self.assertEqual(RIG["plates"]["fromLevel"], 2)

    def test_the_site_states_are_the_rigs_levels_and_the_plates_wait_for_the_jig(self):
        (model,) = [m for m in MANIFEST["models"] if m["id"] == "concentrator"]
        level, plates = model["scenario"]["states"]
        self.assertEqual(level["values"], LEVEL_REQUIRES)
        self.assertEqual([o["id"] for o in level["options"]], ["frame", "level1", "level2", "level3", "level4"])
        self.assertEqual(level["options"][0]["fitted"], [])
        for lv in RIG["levels"]:
            (opt,) = [o for o in level["options"] if o["id"] == f"level{lv['level']}"]
            self.assertEqual(opt["fitted"], lv["requires"])
        self.assertEqual(plates["values"], PLATES)
        self.assertEqual(plates["needs"], ["jig"])
        self.assertEqual([o["fitted"] for o in plates["options"]], [[], ["plates"], ["plates", "dressing"]])


class Collision(unittest.TestCase):
    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        self.assertEqual(len(RIG["cells"]), 4 * 2 * 3)

    def test_collision_is_per_level_not_the_union(self):
        fitted = {r for c in RIG["cells"] for r in c.get("fitted", {})}
        self.assertEqual(fitted, {*LEVEL_REQUIRES, *PLATES, *PIPES})
        # the frame's boxes never hold a level's parts: a box per level only where that level is fitted
        frame_only = [c for c in RIG["cells"] if c.get("boxes")]
        self.assertTrue(frame_only)
        for c in RIG["cells"]:
            for r, boxes in c.get("fitted", {}).items():
                for b in boxes:
                    self.assertEqual(len(b), 6)
                    self.assertTrue(all(0.0 <= v <= 1.0 for v in b))
                    self.assertTrue(all(b[k] < b[k + 3] for k in range(3)), (c["pos"], r))
        # the sluice and the jig stand in the same place: the sluice's cells are the jig's (level 2 retires it)
        sluice = {tuple(c["pos"]) for c in RIG["cells"] if "sluice" in c.get("fitted", {})}
        jig = {tuple(c["pos"]) for c in RIG["cells"] if "jig" in c.get("fitted", {})}
        self.assertTrue(sluice & jig)


class Mechanism(unittest.TestCase):
    def test_the_line_shaft_turns_with_the_axle_at_the_power_cells_centre(self):
        (ls,) = [p for p in RIG["parts"] if p["id"] == "lineshaft"]
        (d,) = ls["drivers"]
        self.assertEqual((d["type"], d["axis"], d["ratio"]), ("rotate", "x", 1.0))
        pc = RIG["powerCell"]
        self.assertEqual(RIG["powerFace"], "west")
        self.assertEqual(d["pivot"], [pc[0], pc[1] + 0.5, pc[2] + 0.5])

    def test_each_yoke_follows_its_sheave_exactly(self):
        m = make_shape
        cases = [("jigsheave", (m.JIG_X, m.LS[0], m.LS[1] - m.JIG_E), "jigplunger", 1),
                 ("tablesheave", (m.TABLE_X, m.LS[0] + m.TABLE_E, m.LS[1]), "tabledeck", 2)]
        for xs in m.CS_SHEAVES_X:
            cases.append(("vannershaft", (xs, m.CS[0] + m.VN_E, m.CS[1]), "vannerframe", 2))
        for sheave, centre, follower, axis in cases:
            rest = point(follower, 0.0, list(centre))
            for theta in (0.4, 1.9, -2.7, 4.4):
                got = point(sheave, theta, list(centre))[axis] - centre[axis]
                moved = point(follower, theta, list(centre))[axis] - rest[axis]
                self.assertAlmostEqual(got, moved, places=5, msg=f"{follower} at {theta}")

    def test_the_motions_are_small(self):
        m = make_shape
        self.assertLessEqual(2 * m.JIG_E, 1.5)       # the plunger's stroke, voxels
        self.assertLessEqual(2 * m.TABLE_E, 1.0)     # the deck's
        self.assertLessEqual(2 * m.VN_E, 1.0)        # the vanner's shake

    def test_the_vanner_belt_creeps_to_the_head_whichever_way_the_axle_turns(self):
        m = make_shape
        worm = [math.atan2(matrix("vannerworm", th)[2][1], matrix("vannerworm", th)[1][1]) for th in (0.0, 1.0, -1.0)]
        head = [math.atan2(matrix("vannerhead", th)[1][0], matrix("vannerhead", th)[0][0]) for th in (0.0, 1.0, -1.0)]
        for i in (1, 2):
            self.assertAlmostEqual(worm[i] - worm[0], m.WORM_RATIO, places=5)              # forward, a push a turn
            self.assertAlmostEqual(abs(head[i] - head[0]), m.WORM_RATIO / m.WORM_N, places=5)
        top = [m.HEAD[0], m.HEAD[1] + m.ROLL_R, 37.0]
        for th in (0.3, -0.3):
            moved = point("vannerhead", th, top)[0] - point("vannerframe", th, top)[0]
            self.assertGreater(moved, 0.0)           # the upper run moves east, to the head
        # the pawl pushes a tooth's worth each stroke: the shake's stroke is the ratchet's pitch
        self.assertAlmostEqual(m.PAWL_TRAVEL, 2 * math.pi * m.RATCHET_R / m.RATCHET_N, delta=0.1)


class Anchors(unittest.TestCase):
    def test_one_water_inlet_and_the_outlets_on_their_faces(self):
        self.assertEqual((RIG["waterCell"], RIG["waterFace"]), ([1, 1, 0], "north"))
        self.assertEqual((RIG["feedCell"], RIG["feedFace"]), ([0, 1, 0], "up"))
        self.assertEqual(RIG["concentrateSide"], "west")
        self.assertEqual(RIG["concentrate"]["pos"][0], 0.0)
        self.assertEqual(RIG["tailingsSide"], "east")
        self.assertEqual(RIG["tailings"]["pos"][0], 4.0)
        self.assertEqual((RIG["returnWaterCell"], RIG["returnWaterFace"]), ([3, 0, 0], "east"))
        tail_cell = [3, int(RIG["tailings"]["pos"][1]), int(RIG["tailings"]["pos"][2])]
        self.assertNotEqual(tail_cell, RIG["returnWaterCell"])
        cells = [tuple(RIG[k]) for k in ("powerCell", "waterCell", "feedCell", "returnWaterCell")]
        self.assertEqual(len(cells), len(set(cells)))
        # nothing carries water back inside the machine: no pump, sump or return launder
        self.assertFalse([e["name"] for e in SHAPE["elements"] if re.search(r"pump|sump|return", e["name"])])

    def test_the_inlet_is_ppex_pipe_end_to_end_on_the_water_face(self):
        for pid in PIPES:
            inlet = [e for e in SHAPE["elements"] if e["name"].startswith(f"{pid}_inlet_")]
            lo = [min(min(e["from"][k], e["to"][k]) for e in inlet) for k in range(3)]
            hi = [max(max(e["from"][k], e["to"][k]) for e in inlet) for k in range(3)]
            self.assertEqual(lo[2], 0.0)
            self.assertEqual((hi[0] - lo[0], hi[1] - lo[1]), (6.0, 6.0))
            cell = RIG["waterCell"]
            self.assertEqual(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2), (cell[0] * 16 + 8.0, cell[1] * 16 + 8.0))


class Infeed(unittest.TestCase):
    def test_the_mill_levels_infeed_is_vanillas_chute_at_the_feed_cells_top_face(self):
        cell = RIG["feedCell"]
        els = [e for e in SHAPE["elements"] if e["name"].startswith("ch_")]
        walls = sorted((min(e["from"][0], e["to"][0]) - cell[0] * 16, max(e["from"][0], e["to"][0]) - cell[0] * 16,
                        min(e["from"][2], e["to"][2]) - cell[2] * 16, max(e["from"][2], e["to"][2]) - cell[2] * 16) for e in els)
        # the game's straight chute, upright: an 8 x 8 tube, 1 thick, centred in the block (shapes/block/metal/chute/straight.json)
        self.assertEqual(walls, [(4.0, 5.0, 5.0, 11.0), (4.0, 12.0, 4.0, 5.0), (4.0, 12.0, 11.0, 12.0), (11.0, 12.0, 5.0, 11.0)])
        self.assertEqual({max(e["from"][1], e["to"][1]) for e in els}, {(cell[1] + 1) * 16.0})
        self.assertEqual(SHAPE["textures"]["chute"], "game:block/metal/sheet/copper1")
        self.assertEqual({f["texture"] for e in els for f in e["faces"].values()}, {"#chute"})
        self.assertEqual([lv["level"] for lv in RIG["levels"] if "infeed" in lv["requires"]], [3, 4])


class Pipes(unittest.TestCase):
    def els(self, pid):
        return [e for e in SHAPE["elements"] if e["name"].startswith(pid + "_")]

    def test_one_part_per_metal_the_same_elements_in_its_texture_the_joints_in_solder(self):
        cu, pb = self.els("pipecopper"), self.els("pipelead")
        self.assertTrue(cu)
        self.assertEqual([(e["name"][len("pipecopper"):], e["from"], e["to"]) for e in cu],
                         [(e["name"][len("pipelead"):], e["from"], e["to"]) for e in pb])
        for pid, metal in (("pipecopper", "copper4"), ("pipelead", "lead4")):
            self.assertEqual(SHAPE["textures"][pid], f"game:block/metal/sheet-plain/{metal}")
            for e in self.els(pid):
                codes = {f["texture"] for f in e["faces"].values()}
                self.assertLessEqual(codes, {f"#{pid}", "#solder"}, e["name"])
                if "joint" in e["name"] or e["name"].endswith("_cap"):
                    self.assertEqual(codes, {"#solder"}, e["name"])
        self.assertEqual(SHAPE["textures"]["solder"], "game:block/metal/ingot/leadsolder")
        for pid in PIPES:
            (part,) = [p for p in RIG["parts"] if p["id"] == pid]
            self.assertEqual((part["requires"], part["drivers"]), (pid, []))


class Files(unittest.TestCase):
    def test_every_face_uses_a_declared_code(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertLessEqual(used, set(shape["textures"]))

    def test_the_readme_lists_every_part(self):
        table = README.split("## Every moving part", 1)[1].split("\n## ", 1)[0]
        for p in RIG["parts"]:
            self.assertIn(f"(`{p['id']}`)", table, p["id"])


if __name__ == "__main__":
    unittest.main()
