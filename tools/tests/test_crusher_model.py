"""The crusher's generated model files (mods-src/seraphhorizons/Crusher/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared
rig maths and its `requires` are the tiers' sets (the frame's none; tier 2 mortar, camshaft and stamps; tier 3 the
jaw; tier 4 the jaw and the rolls), which site/models.json's build states fit; its reference poses are its own
maths; the cells are rebuilt from the shipped shape; the work counts camshaft turns, a load of them, at the drawn
gearing's pace; each stamp drops twice a turn in the firing order and stands at the end of a load as at its start;
the swinging jaw rocks and never closes on the fixed one; the rolls turn alike towards their nip; and the anchors
(power, feed, product and oversize) are on their cells' faces. Run with `python3 -m unittest discover -s tools/tests`.
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
sys.path.insert(0, str(MOD / "Crusher" / "tools"))

from machinegen import rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("crusher_make_shape", MOD / "Crusher" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "crusher-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "crusher.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "crusher_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "Crusher" / "rig-reference.json").read_text())
MANIFEST = json.loads((ROOT / "site" / "models.json").read_text())
README = (MOD / "Crusher" / "README.md").read_text()
TIERS = {"2": ["mortar", "camshaft", "stamps"], "3": ["jaw"], "4": ["jaw", "rolls"]}


def inputs(pose):
    return {k: pose[k] for k in ("theta", "travel", "work", "size", "presence")}


def matrix(pid, **ins):
    return rigmath.part_matrix(RIG["parts"], pid, ins, RIG["work"])


def part(pid):
    return next(p for p in RIG["parts"] if p["id"] == pid)


def load_pose(w, k=1, p=1.0):
    th = 2 * math.pi * RIG["stamps"]["turnsPerRev"] * w
    return {"theta": th, "travel": th, "work": w, "size": k, "presence": p}


class Rig(unittest.TestCase):
    def test_parts_parse_and_requires_are_the_tiers(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            if p.get("ride"):
                self.assertIn(p["ride"], ids)
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({k: v for k, v in RIG["tiers"].items() if not k.startswith("_")}, TIERS)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, {None} | {r for t in TIERS.values() for r in t})
        # the frame carries what every tier shares: the power train is the frame's
        for pid in ("entry", "rectb1", "idler", "rectb2", "line", "frame"):
            self.assertIsNone(part(pid)["requires"], pid)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        self.assertEqual({rigmath.part_of(RIG["parts"], n) for n in names}, {p["id"] for p in RIG["parts"]})
        self.assertEqual({e["name"] for e in FRAME["elements"]}, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})
        used = {f["texture"].lstrip("#") for e in SHAPE["elements"] for f in e["faces"].values()}
        self.assertLessEqual(used, set(SHAPE["textures"]))

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreater(len(poses), 60)
        self.assertEqual({p["size"] for p in poses}, {0, 1, 2})
        for pose in poses[::4]:
            for pid, want in pose["matrices"].items():
                got = rigmath.part_matrix(RIG["parts"], pid, inputs(pose), RIG["work"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {inputs(pose)}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"], RIG["work"]), RIG["cells"])
        self.assertEqual(len(RIG["cells"]), 27)
        self.assertTrue(all("lid" in c for c in RIG["cells"] if c["pos"][1] == 2))

    def test_anchors_are_on_their_cells_faces(self):
        faces = {"up": (1, 1), "north": (2, 0), "west": (0, 0), "east": (0, 1)}
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        for name in ("power", "infeed", "output", "oversize"):
            cell = tuple(RIG[f"{name}Cell"])
            self.assertIn(cell, cells, name)
            axis, side = faces[RIG[f"{name}Face"]]
            # the face is on the footprint's outside
            self.assertNotIn(tuple(cell[k] + (0 if k != axis else (1 if side else -1)) for k in range(3)), cells, name)
            if name != "power":
                pos = RIG[name]["pos"]
                self.assertAlmostEqual(pos[axis], cell[axis] + side, places=6, msg=name)
                for k in range(3):
                    self.assertTrue(cell[k] - 1e-6 <= pos[k] <= cell[k] + 1 + 1e-6, name)
        self.assertEqual((RIG["powerFace"], RIG["infeedFace"], RIG["outputFace"], RIG["oversizeFace"]), ("east", "up", "north", "west"))


class Battery(unittest.TestCase):
    def test_work_is_a_load_of_camshaft_turns_at_the_drawn_pace(self):
        w = rigmath.progress_of(RIG)
        self.assertEqual((w["name"], w["unit"]), ("load crushed", "camshaft turns"))
        n = RIG["stamps"]["revsPerLoad"]
        self.assertEqual(w["end"], {"thin": float(n), "thick": float(n)})
        # the camshaft turns once a unit of W; the bull wheel at the axle's travel times the drawn ratio
        a, b = matrix("camshaft", **load_pose(0.25)), matrix("camshaft", **load_pose(0.26))
        turned = math.atan2(b[2][1], b[1][1]) - math.atan2(a[2][1], a[1][1])
        self.assertAlmostEqual(turned, 2 * math.pi * 0.01, places=6)
        a, b = matrix("bullwheel", **load_pose(0.25)), matrix("bullwheel", **load_pose(0.26))
        self.assertAlmostEqual(math.atan2(b[2][1], b[1][1]) - math.atan2(a[2][1], a[1][1]), turned, places=6)
        self.assertAlmostEqual(RIG["stamps"]["turnsPerRev"], make_shape.TURNS_PER_REV, places=5)

    def test_stamps_stand_at_the_end_of_a_load_as_at_its_start(self):
        ident = [[1.0 if r == c else 0.0 for c in range(4)] for r in range(3)]
        n = float(RIG["stamps"]["revsPerLoad"])
        for k in (1, 2):
            for w, p in ((0.0, 1.0), (n, 1.0), (n, 0.35), (0.0, 0.5)):
                for i in range(5):
                    got = matrix(f"stamp{i + 1}", work=w, size=k, presence=p)
                    for r in range(3):
                        for c in range(4):
                            self.assertAlmostEqual(got[r][c], ident[r][c], places=5, msg=f"stamp {i + 1} at W {w}, p {p}")

    def test_each_stamp_drops_twice_a_turn_in_the_firing_order(self):
        n = RIG["stamps"]["revsPerLoad"]
        drops = []
        for i in range(5):
            prev = None
            last = -1.0
            for s in range(int(n * 400) + 1):
                w = s / 400
                y = matrix(f"stamp{i + 1}", work=w, size=1, presence=1.0)[1][3] * 16
                if prev is not None and prev - y > 0.2 and w - last > 0.05:
                    drops.append((w, i + 1))
                    last = w
                prev = y
        drops.sort()
        for i in range(5):
            self.assertEqual(sum(1 for _, s in drops if s == i + 1), 2 * n, i + 1)
        first = next(k for k, (_, s) in enumerate(drops) if s == 1)
        self.assertEqual([s for _, s in drops[first:first + 5]], RIG["stamps"]["firingOrder"])
        self.assertEqual(RIG["stamps"]["firingOrder"], [1, 3, 5, 2, 4])

    def test_the_clutch_is_in_only_with_a_load_on(self):
        out = matrix("cone")[0][3]
        self.assertAlmostEqual(out, 0.0)
        self.assertAlmostEqual(matrix("cone", size=1, presence=1.0)[0][3] * 16, make_shape.CLUTCH_THROW, places=5)


class JawAndRolls(unittest.TestCase):
    def test_the_jaw_rocks_and_never_closes(self):
        # the swinging jaw's face at its bottom, posed by the shipped rig through a turn of the eccentric shaft
        e = next(d for d in part("eshaft")["drivers"] if d["type"] == "rotate")
        pt = [v / 16 for v in make_shape.design_to_mean_jaw(make_shape.FACE_BOT)]
        p = [0.0, pt[0], pt[1] - make_shape.ORIGIN_CELL[2]]
        gaps = []
        for k in range(36):
            psi = 2 * math.pi * k / 36 / e["ratio"]
            m = matrix("swingjaw", theta=psi, travel=psi)
            z = rigmath.apply(m, p)[2] * 16
            gaps.append(make_shape.FIXED_Z - z)
        self.assertGreater(min(gaps), 1.0)
        self.assertGreater(max(gaps) - min(gaps), 0.5)

    def test_the_rolls_turn_alike_towards_their_nip(self):
        r1 = next(d for d in part("roll1")["drivers"] if d["type"] == "rotate")
        r2 = next(d for d in part("roll2")["drivers"] if d["type"] == "rotate")
        self.assertEqual((r1.get("input"), r2.get("input")), ("travel", "travel"))
        self.assertGreater(r1["ratio"], 0)                    # the north roll's south face, at the nip, goes down
        self.assertAlmostEqual(r1["ratio"], -r2["ratio"])
        self.assertLess(r1["pivot"][2], r2["pivot"][2])


class Manifest(unittest.TestCase):
    def test_the_viewer_shows_each_tier(self):
        model = next(m for m in MANIFEST["models"] if m["id"] == "crusher")
        self.assertTrue(model["rig"].endswith("crusher-rig.json"))
        states = model["scenario"]["states"]
        got = {o["id"]: sorted(o["fitted"]) for o in states["options"]}
        self.assertEqual(got, {"frame": [], "t2": sorted(TIERS["2"]), "t3": sorted(TIERS["3"]), "t4": sorted(TIERS["4"])})
        self.assertEqual(states["default"], "t4")
        self.assertEqual(model["scenario"]["play"]["turnsPerWork"], "stamps.turnsPerRev")

    def test_the_readme_names_every_part(self):
        # in its moving-parts table: by id, or a numbered run of them as `stamp1`..`stamp5`
        for p in RIG["parts"]:
            pid = p["id"]
            base = re.sub(r"\d+$", "", pid)
            ok = f"`{pid}`" in README or (base != pid and re.search(rf"`{re.escape(base)}1`\.\.`{re.escape(base)}\d+`", README))
            self.assertTrue(ok, f"the README does not name the part {pid}")


if __name__ == "__main__":
    unittest.main()
