"""The jig's generated model files (mods-src/seraphhorizons/Jig/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the
shared rig maths and uses its `requires` vocabulary, its reference poses are its own maths, the cells are
rebuilt from the shipped shape, theta (the axle) turns the shaft and strokes the plunger once a turn the
same either way, every service sits centred on a face of its own (the chutes in vanilla's section), and
every texture is declared. Run with `python3 -m unittest discover -s tools/tests -p test_jig_model.py`.
"""

import importlib.util
import json
import math
import os
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
ASSETS = MOD / "assets" / "seraphhorizons"
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(MOD / "Jig" / "tools"))

from machinegen import rigmath  # noqa: E402
from machinegen.geometry import flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("jig_make_shape", MOD / "Jig" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((ASSETS / "config" / "jig-rig.json").read_text())
SHAPE = json.loads((ASSETS / "shapes" / "block" / "jig.json").read_text())
FRAME = json.loads((ASSETS / "shapes" / "block" / "jig_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "Jig" / "rig-reference.json").read_text())
# the fitted parts, the water inlet in either metal, and the contents
REQUIRES = {"sieve", "drive", "plunger", "infeed", "outlets", "pipecopper", "pipelead", "water", "bed", None}
STILL = ("stand", "sieve", "infeed", "outlets", "pipecopper", "pipelead", "frame")


def matrix(pid, theta):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta}, None)


def elements(prefix):
    return [e for e in SHAPE["elements"] if e["name"].startswith(prefix)]


def span(els, k):
    return min(min(e["from"][k], e["to"][k]) for e in els), max(max(e["from"][k], e["to"][k]) for e in els)


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(ids, ["shaft", "sheave", "plunger", "watersieve", "waterplunger", "bed", "stand", "sieve", "infeed",
                               "outlets", "pipecopper", "pipelead", "frame"])
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            if p.get("ride"):
                self.assertIn(p["ride"], ids)
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        self.assertIsNone(rigmath.progress_of(RIG))          # no work: only the axle poses it

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreaterEqual(len(poses), 10)
        for pose in poses:
            self.assertEqual(set(pose["matrices"]), {p["id"] for p in RIG["parts"]})
            for pid, want in pose["matrices"].items():
                got = matrix(pid, pose["theta"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at theta {pose['theta']}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        self.assertEqual(sorted(tuple(c["pos"]) for c in RIG["cells"]), [(0, 0, 0), (0, 0, 1), (0, 1, 0), (0, 1, 1)])
        for c in RIG["cells"]:
            self.assertTrue(c["boxes"])
            self.assertEqual("lid" in c, c["pos"][1] == 1)


class Motion(unittest.TestCase):
    def test_the_shaft_turns_with_the_axle_and_carries_the_sheave(self):
        for theta in (0.3, 1.7, -2.2, 5.0, 11.0):
            m = matrix("shaft", theta)
            axis = rigmath.apply(m, [0.5, 1.5, 1.5])
            for got, want in zip(axis, [0.5, 1.5, 1.5]):
                self.assertAlmostEqual(got, want, places=12)
            p = rigmath.apply(m, [0.5, 1.5, 2.5])
            self.assertAlmostEqual(p[1] - 1.5, -math.sin(theta), places=9)
            self.assertAlmostEqual(p[2] - 1.5, math.cos(theta), places=9)
            self.assertEqual(matrix("sheave", theta), m)

    def test_the_plunger_strokes_once_a_turn_the_same_either_way(self):
        for theta in (0.3, math.pi / 2, -2.2, 4.0):
            m = matrix("plunger", theta)
            self.assertAlmostEqual(m[1][3], make_shape.E / 16 * math.sin(theta), places=9)
            self.assertAlmostEqual(matrix("plunger", -theta)[1][3], -m[1][3], places=12)
            self.assertEqual((m[0][3], m[2][3]), (0.0, 0.0))
        self.assertEqual(RIG["drive"]["strokesPerTurn"], 1)
        self.assertAlmostEqual(RIG["drive"]["stroke"], 2 * make_shape.E / 16, places=6)

    def test_the_water_and_the_bed_answer_the_plunger(self):
        for theta in (0.7, 2.0, 4.4):
            dy = matrix("plunger", theta)[1][3]
            self.assertLess(matrix("watersieve", theta)[1][3] * dy, 0)      # up through the sieve as it goes down
            self.assertGreater(matrix("waterplunger", theta)[1][3] * dy, 0)  # down with the plunger
            self.assertLess(matrix("bed", theta)[1][3] * dy, 0)             # the bed lifts on the pulse

    def test_nothing_else_moves(self):
        ident = rigmath.m4()
        for pid in STILL:
            for theta in (0.0, 2.5, -7.0):
                m = matrix(pid, theta)
                self.assertEqual([[round(v, 12) + 0.0 for v in row] for row in m], ident, pid)


class Anchors(unittest.TestCase):
    CELL_FACE = {"power": ([0, 1, 1], "west"), "infeed": ([0, 1, 0], "up"), "tailings": ([0, 1, 0], "west"),
                 "concentrate": ([0, 0, 0], "north"), "water": ([0, 0, 1], "south")}

    def test_every_service_is_on_a_face_of_its_own(self):
        for key, (cell, face) in self.CELL_FACE.items():
            self.assertEqual((RIG[f"{key}Cell"], RIG[f"{key}Face"]), (cell, face), key)
        self.assertEqual(len({(tuple(c), f) for c, f in self.CELL_FACE.values()}), len(self.CELL_FACE))

    def test_the_axle_meets_the_shafts_oak_end_at_the_power_faces_centre(self):
        lo, hi = [], []
        for e in elements("shaft_entry"):
            lo.append(e["from"])
            hi.append(e["to"])
        self.assertEqual(min(p[0] for p in lo), 0.0)
        self.assertEqual((min(p[1] for p in lo), max(p[1] for p in hi)), (22.0, 26.0))
        self.assertEqual((min(p[2] for p in lo), max(p[2] for p in hi)), (22.0, 26.0))

    def test_the_chutes_are_vanillas_section_flush_with_their_faces(self):
        walls = sorted(make_shape.VANILLA_CHUTE["walls"])
        # the tailings chute along x out of the front top cell's west face: walls over (y, z) cell-local
        tail = elements("outlet_tailchute_")
        self.assertEqual(sorted((e["from"][1] - 16, e["to"][1] - 16, e["from"][2], e["to"][2]) for e in tail), walls)
        self.assertEqual(span(tail, 0), (0.0, make_shape.HUTCH_X[0]))
        # the concentrate chute along z out of the controller's north face: walls over (y, x)
        conc = elements("outlet_concchute_")
        self.assertEqual(sorted((e["from"][1], e["to"][1], e["from"][0], e["to"][0]) for e in conc), walls)
        self.assertEqual(span(conc, 2)[0], 0.0)
        # the infeed upright through the front top cell's top face: walls over (x, z), y mirrored to x
        inf = elements("infeed_chute_")
        self.assertEqual(sorted((e["from"][0], e["to"][0], e["from"][2], e["to"][2]) for e in inf),
                         sorted((16 - a1, 16 - a0, b0, b1) for a0, a1, b0, b1 in walls))
        self.assertEqual(span(inf, 1)[1], 32.0)
        for e in tail + conc + inf:
            self.assertEqual({f["texture"] for f in e["faces"].values()} - {"#chute"}, set(), e["name"])
        self.assertEqual(SHAPE["textures"]["chute"], "game:block/metal/sheet/copper1")

    def test_the_water_inlet_is_ppexs_pipe_on_the_water_faces_centre(self):
        for metal in ("copper", "lead"):
            els = elements(f"pipe{metal}_inlet_")
            self.assertEqual(len(els), 4)
            self.assertEqual(span(els, 0), (5.0, 11.0))
            self.assertEqual(span(els, 1), (5.0, 11.0))
            self.assertEqual(span(els, 2)[1], 32.0)

    def test_the_weir_overflows_into_the_tailings_chute(self):
        lip = [e for e in SHAPE["elements"] if e["name"] == "hutch_weir_lip"][0]
        bore_floor = 16 + 5.0
        self.assertGreater(lip["to"][1], bore_floor)
        self.assertLess(lip["from"][0], make_shape.HUTCH_X[0])
        self.assertAlmostEqual(RIG["levels"]["weirY"] * 16, lip["to"][1], places=4)
        # the bed's top stands a little under the lip
        bed = elements("bed_ore")[0]
        self.assertLess(lip["to"][1] - bed["to"][1], 2.0)

    def test_the_tailings_leave_through_a_port_in_the_tail_ends_head(self):
        # the west wall rises round the tailings chute's end; the port through it over the lip is open
        head = elements("hutch_head_")
        self.assertEqual({e["name"] for e in head}, {"hutch_head_n", "hutch_head_s", "hutch_head_lintel"})
        tail = elements("outlet_tailchute_")
        self.assertLessEqual(span(head, 2)[0], span(tail, 2)[0])
        self.assertGreaterEqual(span(head, 2)[1], span(tail, 2)[1])
        self.assertAlmostEqual(span(head, 1)[1], span(tail, 1)[1], delta=0.05)
        lintel = elements("hutch_head_lintel")[0]
        lip = elements("hutch_weir_lip")[0]
        self.assertGreater(lintel["from"][1] - lip["to"][1], 2.0)
        for e in elements("hutch_wall_w") + head:
            port = e["from"][1] < lintel["from"][1] - 0.05 and e["to"][1] > lip["to"][1] + 0.05
            across = e["from"][2] < lip["to"][2] - 0.05 and e["to"][2] > lip["from"][2] + 0.05
            self.assertFalse(port and across, f"{e['name']} closes the weir's port")

    def test_the_concentrate_leaves_by_an_open_spout(self):
        # an iron spout lines a port through the front wall's foot: a bore through it, its lip in the chute's mouth
        sp = {e["name"].rsplit("_", 1)[1]: e for e in elements("hutch_spigot_")}
        self.assertEqual(set(sp), {"bottom", "top", "w", "e"})
        bore = ((sp["w"]["to"][0], sp["e"]["from"][0]), (sp["bottom"]["to"][1], sp["top"]["from"][1]))
        self.assertGreater(bore[0][1] - bore[0][0], 3.0)
        self.assertGreater(bore[1][1] - bore[1][0], 1.5)
        conc = elements("outlet_concchute_")
        self.assertGreaterEqual(span(sp.values(), 0)[0], 5.0 - 0.05)
        self.assertLessEqual(span(sp.values(), 0)[1], 11.0 + 0.05)
        self.assertLess(span(sp.values(), 2)[0], span(conc, 2)[1])
        self.assertGreater(bore[1][0], 5.0)                     # it pours down onto the chute's floor
        for e in elements("hutch_wall_n"):
            through = (e["from"][0] < bore[0][1] and e["to"][0] > bore[0][0]
                       and e["from"][1] < bore[1][1] and e["to"][1] > bore[1][0])
            self.assertFalse(through, f"{e['name']} closes the spout's port")

    def test_every_corner_of_the_hutch_is_strapped(self):
        # an angle-iron strap down each vertical corner, in the hutch's own shape: a leg flat on each outer face,
        # the legs meeting at the corner, from the sills' top to the corner's top, nailed
        walls = [e for e in FRAME["elements"] if e["name"].startswith(("hutch_wall", "hutch_head_"))]
        (x0, x1), (z0, z1) = span(walls, 0), span(walls, 2)
        straps = {e["name"]: e for e in FRAME["elements"] if e["name"].startswith("hutch_strap_")}
        for xc in (x0, x1):
            for zc in (z0, z1):
                legs = [e for n, e in straps.items() if "nail" not in n and "_rim" not in n
                        and min(e["from"][0], e["to"][0]) - 1e-4 <= xc <= max(e["from"][0], e["to"][0]) + 1e-4
                        and min(e["from"][2], e["to"][2]) - 1e-4 <= zc <= max(e["from"][2], e["to"][2]) + 1e-4]
                self.assertEqual(len(legs), 2, f"the corner ({xc}, {zc})")
                faces = {("z" if abs(e["from"][2] - zc) < 1e-4 or abs(e["to"][2] - zc) < 1e-4 else "")
                         + ("x" if abs(e["from"][0] - xc) < 1e-4 or abs(e["to"][0] - xc) < 1e-4 else "") for e in legs}
                self.assertTrue(any("z" in f for f in faces) and any("x" in f for f in faces), f"the corner ({xc}, {zc})")
                for e in legs:
                    self.assertLessEqual(e["from"][1], make_shape.FLOOR[0] + 1e-4)
                    self.assertGreaterEqual(e["to"][1], make_shape.RIM - 1e-4)
                    self.assertEqual({f["texture"] for f in e["faces"].values()}, {"#iron"})
                    stem = e["name"]
                    self.assertGreaterEqual(sum(1 for n in straps if n.startswith(stem + "nail")), 2, stem)


class Textures(unittest.TestCase):
    def test_every_face_uses_a_declared_code_and_the_frame_matches(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertEqual(used, set(shape["textures"]))
        for code, path in FRAME["textures"].items():
            self.assertEqual(SHAPE["textures"][code], path, code)

    def test_only_the_waters_surface_is_drawn_in_the_transparent_pass(self):
        passes = {e["name"] for e in SHAPE["elements"] if "renderPass" in e}
        self.assertEqual(passes, {"watersieve_surface", "waterplunger_surface"})

    def test_every_game_texture_exists_in_the_game(self):
        install = os.environ.get("VINTAGE_STORY")
        if not install:
            raise unittest.SkipTest("VINTAGE_STORY is not set to a game or server install")
        assets = Path(install) / "assets"
        for code, path in SHAPE["textures"].items():
            domain, rel = path.split(":", 1)
            self.assertEqual(domain, "game", code)
            self.assertTrue((assets / "survival" / "textures" / f"{rel}.png").is_file(), f"{code}: {path}")


class Shape(unittest.TestCase):
    def test_everything_stays_in_the_four_cells_through_a_turn(self):
        flat = flatten(SHAPE["elements"], textures={})
        for theta in (0.0, math.pi / 2, math.pi, 3 * math.pi / 2):
            for w in flat:
                pid = rigmath.part_of(RIG["parts"], w.name)
                lo, hi = rigmath.posed(w, matrix(pid, theta)).aabb()
                self.assertGreaterEqual(min(lo), -0.011, w.name)
                self.assertLessEqual(hi[0], 16.011, w.name)
                self.assertLessEqual(max(hi[1], hi[2]), 32.011, w.name)


if __name__ == "__main__":
    unittest.main()
