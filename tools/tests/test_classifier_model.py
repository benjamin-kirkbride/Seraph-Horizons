"""The classifier's generated model files (mods-src/seraphhorizons/Classifier/tools/make_shape.py).

These hold what the generator wrote to its own rules: it reproduces the committed files; the shipped rig
parses with the shared rig maths, its `requires` are exactly the tiers' sets and every element is in a
part; its reference poses are its own maths; the cells are rebuilt from the shipped shape; the power and
the four ports are where the README says, the feed high and the outlets low; the trommel turns about its
own inclined axis a third of a turn an axle turn, and tier 4's outer jacket turns with it, concentric and shorter
at the low end; tier 2's linkage holds at its pins and the screen moves without turning; every texture is declared (and, with VINTAGE_STORY set, exists in the game); and the
README names every rig part and the tiers' sets as the generator has them. Run with
`python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import math
import os
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
TOOLS = MOD / "Classifier" / "tools"
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(TOOLS))

from machinegen import checks, rigmath  # noqa: E402
from machinegen.geometry import flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("classifier_make_shape", TOOLS / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

SHAPE_PATH = MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "classifier.json"
FRAME_PATH = MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "classifier_frame.json"
RIG_PATH = MOD / "assets" / "seraphhorizons" / "config" / "classifier-rig.json"
REFERENCE_PATH = MOD / "tests" / "Classifier" / "rig-reference.json"
SHAPE = json.loads(SHAPE_PATH.read_text())
FRAME = json.loads(FRAME_PATH.read_text())
RIG = json.loads(RIG_PATH.read_text())
REFERENCE = json.loads(REFERENCE_PATH.read_text())
README = (MOD / "Classifier" / "README.md").read_text()
TIERS = {"2": ["grizzly", "screen", "eccentric"], "3": ["trommel", "bevel", "discharge"], "4": ["trommel", "bevel", "discharge", "jacket", "middlings"]}


def matrix(pid, theta):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta, "travel": abs(theta)})


def point(pid, theta, p):
    return [c * 16 for c in rigmath.apply(matrix(pid, theta), [v / 16 for v in p])]


class Generator(unittest.TestCase):
    def test_the_generator_reproduces_the_files(self):
        with tempfile.TemporaryDirectory() as d:
            run = subprocess.run([sys.executable, str(TOOLS / "make_shape.py"), "--out", d], capture_output=True, text=True)
            self.assertEqual(run.returncode, 0, run.stdout[-3000:] + run.stderr[-3000:])
            for path in (SHAPE_PATH, FRAME_PATH, RIG_PATH, REFERENCE_PATH):
                self.assertEqual((Path(d) / path.name).read_text(), path.read_text(), f"{path.name} is not what the generator writes: regenerate it")


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
        self.assertEqual(RIG["tiers"], TIERS)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, {v for vs in TIERS.values() for v in vs} | {None})
        # the frame and the entry shaft need nothing: the frame alone is the bare machine with its shaft turning
        self.assertEqual([p["id"] for p in RIG["parts"] if p["requires"] is None], ["entry", "frame"])

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        self.assertEqual({rigmath.part_of(RIG["parts"], n) for n in names}, {p["id"] for p in RIG["parts"]})
        self.assertEqual({e["name"] for e in FRAME["elements"]}, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreaterEqual(len(poses), 12)
        for pose in poses:
            self.assertEqual(set(pose["matrices"]), {p["id"] for p in RIG["parts"]})
            for pid, want in pose["matrices"].items():
                got = rigmath.part_matrix(RIG["parts"], pid, {"theta": pose["theta"], "travel": pose["travel"]})
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at theta {pose['theta']}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        self.assertEqual(len(RIG["cells"]), 4 * 3 * 2)
        self.assertEqual(checks.lid_gaps(RIG["cells"]), [])

    def test_only_theta_moves_it(self):
        # the rig reads the axle's angle and nothing else: no work, no depth, no oil
        self.assertNotIn("work", RIG)
        self.assertNotIn("trunkPath", RIG)
        for p in RIG["parts"]:
            for d in p["drivers"]:
                self.assertIn(d["type"], ("rotate", "swing", "slide"), p["id"])
                self.assertNotIn("input", d, p["id"])
                self.assertNotIn("rectified", d, p["id"])


class Ports(unittest.TestCase):
    def test_power_and_ports(self):
        self.assertEqual((RIG["powerCell"], RIG["powerFace"]), ([3, 1, 0], "north"))
        want = {"feed": ([0, 2, 0], "west"), "fines": ([1, 0, 0], "north"), "oversize": ([3, 0, 0], "east"), "middlings": ([2, 0, 1], "south")}
        axis = {"west": (0, 0), "east": (0, 1), "north": (2, 0), "south": (2, 1)}
        for key, (cell, face) in want.items():
            self.assertEqual((RIG[f"{key}Cell"], RIG[f"{key}Face"]), (cell, face), key)
            p = RIG[key]["pos"]
            k, hi = axis[face]
            self.assertAlmostEqual(p[k], cell[k] + hi, places=4, msg=key)
            for j in range(3):
                if j != k:
                    self.assertTrue(cell[j] <= p[j] <= cell[j] + 1, f"{key}'s point is outside its cell")
        # the feed comes in on the top row, everything leaves on the bottom one
        self.assertEqual(RIG["feedCell"][1], 2)
        self.assertEqual({RIG[f"{k}Cell"][1] for k in ("fines", "oversize", "middlings")}, {0})
        self.assertNotIn("returnCell", RIG)

    def test_the_entry_shaft_meets_the_power_face_at_the_cells_centre(self):
        entry = next(p for p in RIG["parts"] if p["id"] == "entry")
        (drv,) = entry["drivers"]
        self.assertEqual((drv["type"], drv["axis"], drv["ratio"]), ("rotate", "z", 1.0))
        x, y = drv["pivot"][0], drv["pivot"][1]
        self.assertEqual((x, y), (3.5, 1.5))           # the power cell [3,1,0]'s centre
        zs = [min(e["from"][2], e["to"][2]) for e in SHAPE["elements"] if e["name"].startswith("entry_shaft")]
        self.assertEqual(min(zs), 0.0)                   # it starts on the north face


class Trommel(unittest.TestCase):
    def test_it_turns_about_its_own_inclined_axis_a_third_of_a_turn_a_turn(self):
        t = RIG["trommel"]
        self.assertAlmostEqual(t["turnsPerAxleTurn"], 1 / 3, places=5)
        self.assertAlmostEqual(t["slope"], 1 / 12, places=5)
        apex = [make_shape.APEX[k] for k in range(3)]
        far = list(make_shape.drum_axis_point(20.0))
        for th in (0.7, 2.9, 6.1, 15.0):
            for p in (apex, far):
                self.assertLess(math.dist(point("drum", th, p), p), 1e-3)
        # three axle turns: one turn of the drum, back where it started
        m = matrix("drum", 3 * 2 * math.pi)
        for r in range(3):
            for c in range(3):
                self.assertAlmostEqual(m[r][c], 1.0 if r == c else 0.0, places=4)
        # a third of a turn: a point on the jacket comes round 120 degrees about the axis
        p = [far[0], far[1] + 7.5, far[2]]
        q = point("drum", 2 * math.pi, p)
        self.assertAlmostEqual(math.dist(q, far), math.dist(p, far), places=3)
        self.assertAlmostEqual(math.dist(q, p), 2 * 7.5 * math.sin(math.pi / 3), delta=0.05)

    def test_the_bevel_pair_is_8_to_24(self):
        self.assertEqual((make_shape.PINION_TEETH, make_shape.WHEEL_TEETH), (8, 24))
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(sum(1 for n in names if re.match(r"pinion_tooth\d+$", n)), 8)
        self.assertEqual(sum(1 for n in names if re.match(r"wheel_tooth\d+$", n)), 24)
        self.assertEqual(next(p for p in RIG["parts"] if p["id"] == "pinion")["ride"], "entry")
        self.assertEqual(next(p for p in RIG["parts"] if p["id"] == "wheel")["ride"], "drum")


class Compound(unittest.TestCase):
    """Tier 4: the drum wrapped in a shorter outer jacket of finer mesh, turning with it."""

    def test_the_jacket_rides_the_drum(self):
        jacket = next(p for p in RIG["parts"] if p["id"] == "jacket")
        self.assertEqual((jacket["requires"], jacket["ride"], jacket["drivers"]), ("jacket", "drum", []))
        for th in (0.4, 3.3, 11.0):
            a, b = matrix("jacket", th), matrix("drum", th)
            for r in range(3):
                for c in range(4):
                    self.assertAlmostEqual(a[r][c], b[r][c], places=9)

    def test_it_is_concentric_finer_and_shorter_at_the_low_end(self):
        a0, a1 = make_shape.drum_axis_point(0.0), make_shape.drum_axis_point(60.0)
        ax = [a1[k] - a0[k] for k in range(3)]
        n = math.sqrt(sum(c * c for c in ax))
        ax = [c / n for c in ax]

        def extent(prefix):
            rs, alongs = [], []
            for el in flatten([e for e in SHAPE["elements"] if e["name"].startswith(prefix)], textures={}):
                for p in el.corners():
                    d = [p[k] - a0[k] for k in range(3)]
                    along = sum(d[k] * ax[k] for k in range(3))
                    alongs.append(along)
                    rs.append(math.sqrt(max(0.0, sum(c * c for c in d) - along * along)))
            return min(rs), max(rs), min(alongs), max(alongs)
        drum = extent("drum_jacket")
        jacket = extent("jacket_mesh")
        lip = extent("drum_lipring")
        self.assertGreater(jacket[0], drum[1] + 0.5)              # the annulus the middlings run in
        self.assertLess(jacket[3], lip[3] - 4.0)                  # the drum runs on past the jacket's low end
        textures = {f["texture"] for e in SHAPE["elements"] if e["name"].startswith("jacket_mesh") for f in e["faces"].values()}
        self.assertEqual(textures, {"#finemesh"})
        self.assertNotEqual(SHAPE["textures"]["finemesh"], SHAPE["textures"]["mesh"])


class Linkage(unittest.TestCase):
    def test_tier_two_holds_at_its_pins(self):
        e0 = make_shape.ecc_centre(0.0)
        pin = make_shape.ROD_PIN
        zd = make_shape.ZD
        xs = []
        for i in range(36):
            th = 2 * math.pi * (i + 0.3) / 36
            for side in ("n", "s"):
                self.assertLess(math.dist(point(f"ecc{side}", th, (*e0, zd)), point(f"rod{side}", th, (*e0, zd))), 2e-3)
                self.assertLess(math.dist(point(f"rod{side}", th, (*pin, zd)), point("screen", th, (*pin, zd))), 2e-3)
            for i_h, (top, foot) in enumerate(make_shape.hanger_pins(), 1):
                for k in (2 * i_h - 1, 2 * i_h):
                    self.assertLess(math.dist(point(f"hanger{k}", th, (*top, zd)), (*top, zd)), 2e-3)
                    self.assertLess(math.dist(point(f"hanger{k}", th, (*foot, zd)), point("screen", th, (*foot, zd))), 2e-3)
            m = matrix("screen", th)
            for r in range(3):
                for c in range(3):
                    self.assertAlmostEqual(m[r][c], 1.0 if r == c else 0.0, places=9)
            xs.append(m[0][3] * 16)
        self.assertAlmostEqual(max(xs) - min(xs), 2 * make_shape.ECC, delta=0.1)


class Textures(unittest.TestCase):
    def test_every_face_uses_a_declared_code(self):
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertLessEqual(used, set(shape["textures"]))
        for code, path in FRAME["textures"].items():
            self.assertEqual(SHAPE["textures"][code], path, code)

    def test_every_texture_exists_in_the_game(self):
        install = os.environ.get("VINTAGE_STORY")
        if not install:
            raise unittest.SkipTest("VINTAGE_STORY is not set to a game or server install")
        assets = Path(install) / "assets"
        for code, path in SHAPE["textures"].items():
            domain, rel = path.split(":", 1)
            self.assertEqual(domain, "game", code)
            self.assertTrue((assets / "survival" / "textures" / f"{rel}.png").is_file(), f"{code}: {path}")


class Readme(unittest.TestCase):
    def test_the_readme_names_every_part_and_the_tiers(self):
        missing = [p["id"] for p in RIG["parts"] if f"`{p['id']}`" not in README]
        self.assertEqual(missing, [], "rig parts the README does not name")
        for tier, values in TIERS.items():
            row = re.search(rf"^\| {tier} \|(.*)$", README, re.M)
            self.assertIsNotNone(row, f"tier {tier}'s row")
            self.assertEqual(re.findall(r"`(\w+)`", row.group(1)), values, f"tier {tier}")


if __name__ == "__main__":
    unittest.main()
