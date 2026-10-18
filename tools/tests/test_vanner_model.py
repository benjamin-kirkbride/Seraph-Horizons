"""The vanner's generated model files (mods-src/seraphhorizons/Vanner/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with
the shared rig maths and uses its `requires` vocabulary, its reference poses are its own maths, the cells
are rebuilt from the shipped shape, theta turns the countershaft and shakes the frame across (its uprights
leaning with it) the same either way, the travel creeps the belt to the head whichever way theta went, the
anchors sit on their cells' faces (the axle's cross, the water inlet, three vanilla chute sections), and
every texture is declared (and exists in the game, when one is given).
Run with `python3 -m unittest discover -s tools/tests -p test_vanner_model.py`.
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
ASSETS = MOD / "assets" / "seraphhorizons"
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(MOD / "Pipes" / "tools"))
sys.path.insert(0, str(MOD / "Vanner" / "tools"))

from machinegen import rigmath  # noqa: E402
from machinegen.geometry import flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("vanner_make_shape", MOD / "Vanner" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((ASSETS / "config" / "vanner-rig.json").read_text())
SHAPE = json.loads((ASSETS / "shapes" / "block" / "vanner.json").read_text())
FRAME = json.loads((ASSETS / "shapes" / "block" / "vanner_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "Vanner" / "rig-reference.json").read_text())
REQUIRES = {"countershaft", "shaker", "rollers", "belt", "creep", "feedbox", "chutes", "pipecopper", "pipelead", None}


def matrix(pid, theta, travel=None):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta, "travel": abs(theta) if travel is None else travel})


def elements(prefix):
    return [e for e in SHAPE["elements"] if e["name"].startswith(prefix)]


def aabb(els):
    lo = [min(min(e["from"][k], e["to"][k]) for e in els) for k in range(3)]
    hi = [max(max(e["from"][k], e["to"][k]) for e in els) for k in range(3)]
    return lo, hi


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(ids, ["countershaft", "shaker", "uprightw", "uprighte", "seam1", "seam2", "belt", "head", "foot", "worm",
                               "pawl", "chutes", "feedbox",
                               "pipecopper", "pipelead", "frame"])
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            if p.get("ride"):
                self.assertIn(p["ride"], ids)
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        self.assertIsNone(rigmath.progress_of(RIG))          # no work: nothing is posed by one
        for key in ("work", "trunkPath"):
            self.assertNotIn(key, RIG)

    def test_build_stages_as_settled(self):
        # the frame placed (owner, 2026-10-15), then five stages in order, services last with every chute
        # section and the water line in either metal
        self.assertEqual([(s["stage"], s["id"], s["adds"]) for s in RIG["buildStages"]], [
            (1, "countershaft", ["countershaft"]),
            (2, "shaker", ["shaker"]),
            (3, "rollers", ["rollers", "creep"]),
            (4, "belt", ["belt"]),
            (5, "services", ["feedbox", "chutes", "pipecopper", "pipelead"]),
        ])
        chutes = {e["name"] for e in SHAPE["elements"] if e["name"].startswith(("fr_chute_", "fb_chute_"))}
        self.assertEqual(len(chutes), 12)
        self.assertEqual({rigmath.part_of(RIG["parts"], n) for n in chutes}, {"chutes"})

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreaterEqual(len(poses), 20)
        for pose in poses:
            self.assertEqual(set(pose["matrices"]), {p["id"] for p in RIG["parts"]})
            for pid, want in pose["matrices"].items():
                got = matrix(pid, pose["theta"], pose["travel"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {pose['theta']}, {pose['travel']}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        self.assertEqual(sorted(tuple(c["pos"]) for c in RIG["cells"]), [(x, 0, z) for x in range(2) for z in range(3)])
        for c in RIG["cells"]:
            self.assertGreater(c["lid"], 0.9)
            self.assertNotIn("hollow", c)


class Motion(unittest.TestCase):
    def test_the_countershaft_turns_with_the_axle_and_the_frame_shakes_across(self):
        e = make_shape.E / 16
        for theta in (0.3, -1.7, math.pi / 2, 5.0):
            self.assertAlmostEqual(matrix("shaker", theta)[0][3], e * math.sin(theta), places=12)
            m = matrix("countershaft", theta)
            self.assertAlmostEqual(math.atan2(m[1][0], m[0][0]), math.remainder(theta, 2 * math.pi), places=9)
            axis = rigmath.apply(m, [1.5, 0.5, 2.0])
            for got, want in zip(axis, [1.5, 0.5, 2.0]):
                self.assertAlmostEqual(got, want, places=12)

    def test_the_uprights_carry_the_frame(self):
        drop = make_shape.UP_LEN * (1 - math.cos(make_shape.UP_SWING)) / 16
        for k, side in enumerate("we"):
            cx = sum(make_shape.RAILS_X[k]) / 2 / 16
            head = [cx, make_shape.UP_PIVOTS_Y[1] / 16, 1.0]
            for theta in (0.4, math.pi / 2, -math.pi / 2, 2.9):
                a = rigmath.apply(matrix(f"upright{side}", theta), head)
                b = rigmath.apply(matrix("shaker", theta), head)
                self.assertLessEqual(math.dist(a, b), drop + 1e-9)

    def test_the_belt_creeps_to_the_head_whichever_way_the_axle_turns(self):
        top = [0.6, (make_shape.HEAD[0] + make_shape.ROLL_R) / 16, make_shape.HEAD[1] / 16]
        for theta, travel in ((0.5, 0.5), (-0.5, 0.5), (6.0, 6.0), (-6.0, 6.0), (1.0, 50.0)):
            moved = rigmath.apply(matrix("head", theta, travel), top)
            self.assertLess(moved[2], top[2])
            for pid in ("head", "foot", "worm"):          # their turn is the travel's: the same for theta either way
                a, b = matrix(pid, theta, travel), matrix(pid, -theta, travel)
                self.assertEqual([row[:3] for row in a[:3]], [row[:3] for row in b[:3]], pid)
        # the ratchet a tooth a turn, the wheel a tooth a worm turn
        worm = {d["ratio"] for p in RIG["parts"] if p["id"] == "worm" for d in p["drivers"]}
        head = {d["ratio"] for p in RIG["parts"] if p["id"] == "head" for d in p["drivers"]}
        self.assertEqual(worm, {round(-1 / 12, 6)})
        self.assertEqual(head, {round(-1 / 144, 6)})

    def test_the_belts_seams_travel_round_the_loop_with_the_head_drums_surface(self):
        # each seam's lap, carried by the travel, lies where the generator's belt path puts it (a rigid lap, its
        # ends on the belt, at s0 + the drum's surface speed x travel) through a turn of the loop, shaking with the
        # frame; on the top run it goes to the head as far as the drum's surface turns
        speed = abs(next(d["ratio"] for p in RIG["parts"] if p["id"] == "head" for d in p["drivers"])) * make_shape.ROLL_R
        self.assertAlmostEqual(speed, make_shape.belt_speed(), places=12)
        self.assertAlmostEqual(RIG["drive"]["beltPerTurn"], speed * 2 * math.pi / 16, places=6)
        cycle = make_shape.BELT_LOOP / speed
        laps = {e["name"]: flatten([e], textures={})[0] for e in SHAPE["elements"] if re.fullmatch(r"belt_seam\d", e["name"])}
        self.assertEqual(sorted(laps), ["belt_seam1", "belt_seam2"])
        for i, z0 in enumerate(make_shape.SEAMS_Z, 1):
            s0 = make_shape.seam_s(z0)
            (c0y, c0z), a0 = make_shape.seam_frame(s0)
            lap = laps[f"belt_seam{i}"]
            for k in range(97):
                travel, theta = cycle * k / 96, 0.7 * k
                (cy, cz), a = make_shape.seam_frame(s0 + speed * travel)
                da = a - a0
                shift = rigmath.apply(matrix("shaker", theta, travel), [0.0, 0.0, 0.0])[0] * 16
                got = rigmath.posed(lap, matrix(f"seam{i}", theta, travel))
                for p, q in zip(lap.corners(), got.corners()):
                    y, z = p[1] - c0y, p[2] - c0z
                    want = (p[0] + shift, cy + y * math.cos(da) - z * math.sin(da), cz + y * math.sin(da) + z * math.cos(da))
                    self.assertLess(math.dist(want, q), 0.03, (i, travel))
            # on the top run, 6 voxels of belt: to the head, as far as the drum's surface turns
            t0 = ((make_shape.BELT_D / 2 - 3 - s0) % make_shape.BELT_LOOP) / speed
            t1 = t0 + 6 / speed
            c0 = rigmath.posed(lap, matrix(f"seam{i}", 0.0, t0)).c
            c1 = rigmath.posed(lap, matrix(f"seam{i}", 0.0, t1)).c
            self.assertLess(c1[2], c0[2])
            self.assertAlmostEqual(math.hypot(c1[1] - c0[1], c1[2] - c0[2]), 6.0, delta=0.03)
            # once round, back where it started
            back = matrix(f"seam{i}", 0.0, cycle)
            for r in range(3):
                for c in range(4):
                    self.assertAlmostEqual(back[r][c], 1.0 if r == c else 0.0, places=4)

    def test_the_worms_thread_runs_in_the_wheels_tooth_spaces(self):
        # the shipped thread is a helix (bars a 16th of a turn apart, rising a 16th of the lead), and wherever
        # the travel has turned the worm, the thread crosses under the wheel mid-way between two of its teeth
        bars = sorted(flatten(elements("worm_thread"), textures={}), key=lambda e: int(e.name[len("worm_thread"):]))
        teeth = [e for e in flatten(elements("head_wheel_tooth"), textures={}) if e.name.endswith("_c")]
        self.assertEqual((len(bars), len(teeth)), (make_shape.WORM_TURNS * make_shape.WORM_SEGS + 1, make_shape.WHEEL_N))
        lead, segs = make_shape.WORM_LEAD, make_shape.WORM_SEGS
        for a, b in zip(bars, bars[1:]):
            self.assertAlmostEqual(b.c[2] - a.c[2], lead / segs, delta=0.002)   # the files round, the z-fight fix nudges
        pitch = 2 * math.pi * make_shape.WHEEL_R / make_shape.WHEEL_N
        wx, wy = make_shape.WORM
        hy, hz = make_shape.HEAD
        worm_turn = 12 * 2 * math.pi
        for theta, travel in [(0.0, worm_turn * i / 10) for i in range(10)] + [(1.2, 33.0), (-2.0, 71.0)]:
            mw, mh = matrix("worm", theta, travel), matrix("head", theta, travel)
            shift = rigmath.apply(matrix("shaker", theta, travel), [0.0, 0.0, 0.0])[0] * 16
            posed = [rigmath.posed(e, mw) for e in bars]
            angs = []
            for e in posed:
                a = math.atan2(-(e.c[0] - wx - shift), e.c[1] - wy)
                angs.append(a if not angs else angs[-1] + math.remainder(a - angs[-1], 2 * math.pi))
            cross = []
            for i in range(len(posed) - 1):
                k = math.ceil(angs[i] / (2 * math.pi)) * 2 * math.pi
                if angs[i] <= k < angs[i + 1]:
                    f = (k - angs[i]) / (angs[i + 1] - angs[i])
                    z = posed[i].c[2] + f * (posed[i + 1].c[2] - posed[i].c[2])
                    cross.append(z - hz)
            cross = [min(cross, key=abs)]                  # the crossing nearest under the wheel's axis
            self.assertLessEqual(abs(cross[0]), pitch / 2 + 1e-3, (theta, travel))
            arcs = sorted(make_shape.WHEEL_R * math.atan2(t.c[2] - hz, -(t.c[1] - hy)) for t in (rigmath.posed(e, mh) for e in teeth))
            below = max(s for s in arcs if s <= cross[0])
            above = min(s for s in arcs if s > cross[0])
            self.assertAlmostEqual(above - below, pitch, places=3)
            self.assertLess(abs(cross[0] - (below + above) / 2), pitch / 20, (theta, travel))

    def test_nothing_else_moves(self):
        ident = rigmath.m4()
        for pid in ("pawl", "chutes", "feedbox", "pipecopper", "pipelead", "frame"):
            for theta in (0.0, 2.5, -7.0):
                m = matrix(pid, theta, 30.0)
                self.assertEqual([[round(v, 12) + 0.0 for v in row] for row in m], ident, pid)


class Anchors(unittest.TestCase):
    def test_the_power_face_meets_the_axles_cross(self):
        self.assertEqual((RIG["powerCell"], RIG["powerFace"]), ([1, 0, 2], "south"))
        lo, hi = aabb(elements("cs_entry"))
        self.assertEqual((hi[2], (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, hi[0] - lo[0], hi[1] - lo[1]), (48.0, 24.0, 8.0, 4.0, 4.0))

    def test_the_water_inlet_is_open_on_its_face(self):
        self.assertEqual((RIG["waterCell"], RIG["waterFace"]), ([1, 0, 0], "up"))
        for metal in ("copper", "lead"):
            walls = [e for e in elements(f"pipe{metal}_inlet_") if max(e["from"][1], e["to"][1]) == 16.0]
            lo, hi = aabb(walls)
            self.assertEqual((lo[0], hi[0], lo[2], hi[2]), (21.0, 27.0, 5.0, 11.0))

    def test_each_chute_section_is_vanillas_on_its_face(self):
        ring = {(a, b) for a in range(4, 12) for b in range(4, 12) if not (5 <= a < 11 and 5 <= b < 11)}
        for prefix, key, axis, flush in (("fr_chute_conc_", "concentrate", 2, 0.0), ("fr_chute_tail_", "tailings", 2, 48.0),
                                         ("fb_chute_", "infeed", 1, 16.0)):
            els = elements(prefix)
            self.assertEqual(len(els), 4, prefix)
            cell = RIG[f"{key}Cell"]
            across = [k for k in range(3) if k != axis]
            cover = set()
            for e in els:
                lo = [min(e["from"][k], e["to"][k]) for k in range(3)]
                hi = [max(e["from"][k], e["to"][k]) for k in range(3)]
                self.assertIn(flush, (lo[axis], hi[axis]), prefix)
                for a in range(int(lo[across[0]] - cell[across[0]] * 16), int(hi[across[0]] - cell[across[0]] * 16)):
                    for b in range(int(lo[across[1]] - cell[across[1]] * 16), int(hi[across[1]] - cell[across[1]] * 16)):
                        cover.add((a, b))
                self.assertEqual({f["texture"] for f in e["faces"].values()}, {"#chute"})
            self.assertEqual(cover, ring, prefix)
        self.assertEqual(SHAPE["textures"]["chute"], "game:block/metal/sheet/copper1")
        self.assertEqual((RIG["concentrateFace"], RIG["tailingsFace"], RIG["infeedFace"]), ("north", "south", "up"))


class Textures(unittest.TestCase):
    def test_every_face_uses_a_declared_code_and_the_frame_matches(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertEqual(used, set(shape["textures"]))
        for code, path in FRAME["textures"].items():
            self.assertEqual(SHAPE["textures"][code], path, code)

    def test_the_water_lines_metals_are_the_same_elements(self):
        def flat(metal):
            return sorted((e.name.split("_", 1)[1], [round(v, 4) for v in e.c], [round(v, 4) for v in e.size])
                          for e in flatten(elements(f"pipe{metal}_"), textures={}))
        self.assertEqual(flat("copper"), flat("lead"))

    def test_every_game_texture_exists_in_the_game(self):
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
