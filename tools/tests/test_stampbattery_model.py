"""The stamp battery's generated model files (mods-src/seraphhorizons/StampBattery/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared rig
maths and its `requires` are the levels' sets (the frame's none; level 1 mortar, camshaft, stamps and feedbox; level 2
the feeder in the feed box's place), which site/models.json's build states fit; its reference poses are its own maths;
the cells are rebuilt from the shipped shape, every column decked; the line shaft turns one way whichever way the axle
turns; each stamp drops twice a camshaft turn in the firing order and rests between drops; the feeder's tray turns one
way, a step at each of the middle stamp's strikes; both chutes are the game's chute section on their cells' axes; and
the anchors (power, water, infeed and output) are on their cells' faces. Run with
`python3 -m unittest discover -s tools/tests -p test_stampbattery_model.py`.
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
sys.path.insert(0, str(MOD / "StampBattery" / "tools"))

from machinegen import rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("stampbattery_make_shape", MOD / "StampBattery" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "stampbattery-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "stampbattery.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "stampbattery_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "StampBattery" / "rig-reference.json").read_text())
MANIFEST = json.loads((ROOT / "site" / "models.json").read_text())
README = (MOD / "StampBattery" / "README.md").read_text()
BATTERY = ["mortar", "camshaft", "stamps"]
LEVEL1 = BATTERY + ["feedbox"]
LEVEL2 = BATTERY + ["feeder"]
VALUES = BATTERY + ["feedbox", "feeder"]
PIPES = ["pipecopper", "pipelead"]


def matrix(pid, theta=0.0, travel=None):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta, "travel": abs(theta) if travel is None else travel})


def part(pid):
    return next(p for p in RIG["parts"] if p["id"] == pid)


def angle_x(m):
    return math.atan2(m[2][1], m[1][1])


def angle_y(m):
    return math.atan2(m[0][2], m[0][0])


class Rig(unittest.TestCase):
    def test_parts_parse_and_requires_are_the_levels(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            self.assertEqual(p.get("ride"), "stamp3" if p["id"] == "feedtappet" else None, p["id"])
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertIsNone(rigmath.progress_of(RIG))
        self.assertEqual({k: v for k, v in RIG["levels"].items() if not k.startswith("_")}, {"1": LEVEL1, "2": LEVEL2})
        self.assertEqual({p["requires"] for p in RIG["parts"]}, {None} | set(VALUES) | set(PIPES))
        for pid in ("entry", "line", "frame"):
            self.assertIsNone(part(pid)["requires"], pid)
        for pid in ("feedtappet", "bumper", "lever", "feedshaft", "tray", "feeder"):
            self.assertEqual(part(pid)["requires"], "feeder", pid)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        self.assertEqual({rigmath.part_of(RIG["parts"], n) for n in names}, {p["id"] for p in RIG["parts"]})
        self.assertEqual({e["name"] for e in FRAME["elements"]}, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})
        used = {f["texture"].lstrip("#") for e in SHAPE["elements"] for f in e["faces"].values()}
        self.assertLessEqual(used, set(SHAPE["textures"]))

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreater(len(poses), 40)
        for pose in poses[::3]:
            for pid, want in pose["matrices"].items():
                got = matrix(pid, pose["theta"], pose["travel"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {pose['theta']}, {pose['travel']}")

    def test_cells_are_rebuilt_from_the_shipped_shape_every_column_decked(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        self.assertEqual(len(RIG["cells"]), 36)
        for c in RIG["cells"]:
            self.assertNotIn("fitted", c)
            self.assertEqual("lid" in c, c["pos"][1] == 3, c["pos"])

    def test_anchors_are_on_their_cells_faces(self):
        faces = {"up": (1, 1), "north": (2, 0), "west": (0, 0), "east": (0, 1)}
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        for name in ("power", "water", "infeed", "output"):
            cell = tuple(RIG[f"{name}Cell"])
            self.assertIn(cell, cells, name)
            axis, side = faces[RIG[f"{name}Face"]]
            self.assertNotIn(tuple(cell[k] + (0 if k != axis else (1 if side else -1)) for k in range(3)), cells, name)
            if name != "power":
                pos = RIG[name]["pos"]
                self.assertAlmostEqual(pos[axis], cell[axis] + side, places=6, msg=name)
                for k in range(3):
                    self.assertTrue(cell[k] - 1e-6 <= pos[k] <= cell[k] + 1 + 1e-6, name)
        self.assertEqual([RIG[f"{n}Face"] for n in ("power", "water", "infeed", "output")], ["east", "west", "up", "north"])
        self.assertNotIn("feedCell", RIG)                          # hand feeding goes in at the infeed's mouth

    def test_both_chutes_are_vanilla_chute_sections_on_their_cells_axes(self):
        # each anchor is the middle of its cell's face, where a vanilla chute's axis meets it
        cell, pos = RIG["infeedCell"], RIG["infeed"]["pos"]
        self.assertEqual(pos, [cell[0] + 0.5, cell[1] + 1.0, cell[2] + 0.5])
        cell, pos = RIG["outputCell"], RIG["output"]["pos"]
        self.assertEqual(pos, [cell[0] + 0.5, cell[1] + 0.5, cell[2]])
        self.assertEqual(SHAPE["textures"]["chute"], "game:block/metal/sheet/copper1")
        for tag in ("chutein", "chuteout"):
            walls = [e for e in SHAPE["elements"] if e["name"].startswith(f"mortar_{tag}_")]
            self.assertEqual(len(walls), 4, tag)
            for e in walls:
                self.assertEqual({f["texture"] for f in e["faces"].values()}, {"#chute"})


class Drive(unittest.TestCase):
    def test_the_line_shaft_turns_one_way_whichever_way_the_axle_turns(self):
        for th in (0.6, -0.6):
            turned = angle_x(matrix("line", th)) - angle_x(matrix("line", 0.0))
            self.assertLess(turned, 0.0)
            self.assertAlmostEqual(turned, -0.6, places=6)

    def test_everything_but_the_entry_shaft_ignores_the_axles_direction(self):
        for psi in (0.8, 5.3, 21.0):
            for p in RIG["parts"]:
                a, b = matrix(p["id"], psi, psi), matrix(p["id"], -psi, psi)
                same = all(abs(a[r][c] - b[r][c]) < 1e-12 for r in range(3) for c in range(4))
                self.assertEqual(same, p["id"] != "entry", p["id"])

    def test_the_belt_runs_its_pulleys_at_the_drawn_ratio(self):
        line = next(d for d in part("line")["drivers"] if d["type"] == "rotate")
        cam = next(d for d in part("camshaft")["drivers"] if d["type"] == "rotate")
        self.assertAlmostEqual(cam["ratio"] / line["ratio"], make_shape.LINE_PULLEY_R / make_shape.BULL_R, places=6)
        self.assertAlmostEqual(RIG["stamps"]["turnsPerRev"], 1 / abs(cam["ratio"]), places=5)
        self.assertEqual(part("linepulley")["drivers"], part("line")["drivers"])


class Battery(unittest.TestCase):
    def lift(self, i, phi):
        psi = phi * RIG["stamps"]["turnsPerRev"]
        return matrix(f"stamp{i + 1}", psi)[1][3] * 16

    def test_each_stamp_drops_twice_a_turn_in_the_firing_order(self):
        drops = []
        for i in range(5):
            ys = [self.lift(i, 2 * math.pi * s / 720) for s in range(721)]
            lo, hi = min(ys), max(ys)
            self.assertGreater(hi - lo, make_shape.LIFT * 0.98)
            self.assertLess(hi - lo, make_shape.LIFT * 1.15)
            mid = lo + make_shape.LIFT / 2
            drops += [(s, i + 1) for s in range(1, 721) if ys[s - 1] >= mid > ys[s]]
        drops.sort()
        for i in range(5):
            self.assertEqual(sum(1 for _, s in drops if s == i + 1), 2, i + 1)
        first = next(k for k, (_, s) in enumerate(drops) if s == 1)
        self.assertEqual([s for _, s in (drops[first:] + drops[:first])[:5]], RIG["stamps"]["firingOrder"])
        self.assertEqual(RIG["stamps"]["firingOrder"], [1, 3, 5, 2, 4])

    def test_each_stamp_rests_between_drops(self):
        # a third of each half turn after its landing it stands within 0.12 voxels of its lowest
        tr = make_shape.LIFT / make_shape.CAM_RB
        for i in range(5):
            ys = [self.lift(i, 2 * math.pi * s / 720) for s in range(721)]
            lo = min(ys)
            for k in range(20):
                x = tr + make_shape.FALL + 0.12 + (math.pi - tr - make_shape.FALL - 0.12) * k / 19
                y = self.lift(i, make_shape.stamp_phase(i) + x)
                self.assertLess(y - lo, 0.12, f"stamp {i + 1} at {x:.2f}")


class Feeder(unittest.TestCase):
    def test_the_tray_turns_one_way_a_step_a_strike(self):
        per_rev = RIG["stamps"]["turnsPerRev"]
        n, turns = 1440, 4
        total, back, prev = 0.0, 0.0, None
        for k in range(n + 1):
            psi = 2 * math.pi * per_rev * turns * k / n
            a = angle_y(matrix("tray", psi, psi))
            if prev is not None:
                d = math.remainder(a - prev, math.tau)
                total += d
                back = max(back, -d)                    # the tray turns forwards, positive about y
            prev = a
        per_strike = math.degrees(total) / (turns * RIG["feeder"]["strikesPerRev"])
        self.assertAlmostEqual(abs(per_strike), RIG["feeder"]["trayDegreesPerStrike"], places=2)
        self.assertGreater(per_strike, 0.0)
        self.assertLess(math.degrees(back), 0.05)

    def test_the_pinion_and_the_tray_keep_their_gearing(self):
        # both are drawn at their mean turn, in mesh; the rig turns them by the same terms in the teeth's ratio (the
        # shaft less than half a turn here, so its angle does not wrap)
        ratio = -make_shape.PINION_N / make_shape.TEETH_N
        for psi in (0.0, 3.1, 17.7, 64.2, 140.0):
            shaft = angle_x(matrix("feedshaft", psi, psi))
            tray = angle_y(matrix("tray", psi, psi))
            self.assertLess(abs(shaft), math.pi)
            self.assertAlmostEqual(math.remainder(tray - ratio * shaft, math.tau), 0.0, places=4)

    def test_the_bumper_rod_is_pushed_down_by_the_middle_stamp_and_comes_back(self):
        ys = [matrix("bumper", psi, psi)[1][3] * 16 for psi in (k * 0.01 for k in range(int(2 * math.pi * RIG["stamps"]["turnsPerRev"] / 0.01)))]
        push = RIG["feeder"]["push"] * 16
        self.assertGreater(max(ys) - min(ys), push * 0.95)
        self.assertLess(max(ys) - min(ys), push * 1.1)
        self.assertEqual(RIG["feeder"]["stamp"], 3)


class Manifest(unittest.TestCase):
    def test_the_viewer_shows_each_level(self):
        model = next(m for m in MANIFEST["models"] if m["id"] == "stamp-battery")
        self.assertTrue(model["rig"].endswith("stampbattery-rig.json"))
        group, pipes = model["scenario"]["states"]
        got = {o["id"]: o["fitted"] for o in group["options"]}
        self.assertEqual(got, {"frame": [], "level1": LEVEL1, "level2": LEVEL2})
        self.assertEqual((group["default"], group["values"]), ("level2", VALUES))
        self.assertEqual({o["id"]: o["fitted"] for o in pipes["options"]}, {m: [m] for m in PIPES})
        self.assertEqual((pipes["default"], pipes["values"], pipes["needs"]), ("pipecopper", PIPES, ["mortar"]))

    def test_the_water_line_is_the_same_pipe_in_either_metal(self):
        names = [e["name"] for e in SHAPE["elements"]]
        copper = [n[len("pipecopper_"):] for n in names if n.startswith("pipecopper_")]
        lead = [n[len("pipelead_"):] for n in names if n.startswith("pipelead_")]
        self.assertEqual(copper, lead)
        self.assertEqual(SHAPE["textures"]["pipecopper"], "game:block/metal/sheet-plain/copper4")
        self.assertEqual(SHAPE["textures"]["pipelead"], "game:block/metal/sheet-plain/lead4")

    def test_the_readme_names_every_part(self):
        for p in RIG["parts"]:
            pid = p["id"]
            base = re.sub(r"\d+$", "", pid)
            ok = f"`{pid}`" in README or (base != pid and re.search(rf"`{re.escape(base)}1`\.\.`{re.escape(base)}\d+`", README))
            self.assertTrue(ok, f"the README does not name the part {pid}")


if __name__ == "__main__":
    unittest.main()
