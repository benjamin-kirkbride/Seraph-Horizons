"""The jaw breaker's generated model files (mods-src/seraphhorizons/JawBreaker/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared rig
maths and its `requires` are the frame's none and the four stages (jaws, eccentric, toggles, grizzly); it reads the
axle's angle alone (a Blake works either way round: no rectifier, no travel) and has no work; its reference poses are
its own maths; the cells are rebuilt from the shipped shape; the eccentric shaft turns with the axle once a turn on
the power cell's centre line; the swinging jaw rocks once a turn and never closes on the fixed one; and the anchors
(power, infeed, output) are on their cells' faces, both chutes in one column. Run with
`python3 -m unittest discover -s tools/tests -p test_jawbreaker_model.py`.
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
sys.path.insert(0, str(MOD / "JawBreaker" / "tools"))

from machinegen import rigmath  # noqa: E402
from machinegen.geometry import flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("jawbreaker_make_shape", MOD / "JawBreaker" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((ASSETS / "config" / "jawbreaker-rig.json").read_text())
SHAPE = json.loads((ASSETS / "shapes" / "block" / "jawbreaker.json").read_text())
FRAME = json.loads((ASSETS / "shapes" / "block" / "jawbreaker_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "JawBreaker" / "rig-reference.json").read_text())
MANIFEST = json.loads((ROOT / "site" / "models.json").read_text())
STAGES = ["jaws", "eccentric", "toggles", "grizzly"]


def matrix(pid, theta):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta})


def part(pid):
    return next(p for p in RIG["parts"] if p["id"] == pid)


def angle_x(m):
    return math.atan2(m[2][1], m[1][1])


class Rig(unittest.TestCase):
    def test_parts_parse_and_requires_are_the_stages(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            self.assertIsNone(p.get("ride"))
            for d in p["drivers"]:
                rigmath.validate_driver(d)
                self.assertEqual(rigmath.driver_input(d), "theta", p["id"])
        self.assertIsNone(rigmath.progress_of(RIG))
        self.assertEqual(RIG["stages"], STAGES)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, {None} | set(STAGES))
        self.assertIsNone(part("frame")["requires"])
        for key in ("work", "trunkPath", "lid"):
            self.assertNotIn(key, RIG)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        self.assertEqual({rigmath.part_of(RIG["parts"], n) for n in names}, {p["id"] for p in RIG["parts"]})
        self.assertEqual({e["name"] for e in FRAME["elements"]}, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertEqual(used, set(shape["textures"]))

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreaterEqual(len(poses), 20)
        for pose in poses:
            self.assertEqual(set(pose["matrices"]), {p["id"] for p in RIG["parts"]})
            for pid, want in pose["matrices"].items():
                got = matrix(pid, pose["theta"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at theta {pose['theta']}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        self.assertEqual(sorted(tuple(c["pos"]) for c in RIG["cells"]),
                         [(x, y, z) for x in range(2) for y in range(3) for z in range(2)])
        self.assertFalse(any("lid" in c for c in RIG["cells"]))


class Mechanism(unittest.TestCase):
    def test_the_eccentric_shaft_turns_with_the_axle_on_the_power_cells_centre_line(self):
        for theta in (0.4, 2.2, -1.3, 7.9):
            m = matrix("eshaft", theta)
            self.assertAlmostEqual(math.remainder(angle_x(m) - theta, 2 * math.pi), 0.0, places=9)
            centre = rigmath.apply(m, [1.0, 1.5, 1.5])
            for got, want in zip(centre, [1.0, 1.5, 1.5]):
                self.assertAlmostEqual(got, want, places=9)

    def test_the_jaw_rocks_once_a_turn_and_never_closes(self):
        # the swinging jaw's face's bottom edge, at the chamber's middle, against the fixed jaw's face
        bottom = [make_shape.JAW_XC / 16, make_shape.FACE_BOT[0] / 16, make_shape.u_to_z(make_shape.FACE_BOT[1]) / 16]
        gaps = []
        for k in range(73):
            theta = 2 * math.pi * k / 72
            p = rigmath.apply(matrix("swingjaw", theta), bottom)
            gaps.append(p[2] * 16 - make_shape.FIXED_Z)
        self.assertGreater(min(gaps), 1.0)
        self.assertGreater(max(gaps) - min(gaps), 0.5)            # a throw the eye can see
        self.assertAlmostEqual(gaps[0], gaps[-1], places=9)       # once a turn of the axle
        g = gaps[:72]
        closes = sum(1 for k in range(72) if g[k] < g[k - 1] and g[k] <= g[(k + 1) % 72])   # round the turn
        self.assertEqual(closes, 1)

    def test_either_way_round_the_jaw_passes_the_same_poses(self):
        for theta in (0.3, 1.9, 4.4):
            for pid in ("pitman", "swingjaw", "backtoggle", "fronttoggle", "rods"):
                a, b = matrix(pid, -theta), matrix(pid, 2 * math.pi - theta)
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(a[r][c], b[r][c], places=9)

    def test_the_static_parts_stand_still(self):
        ident = rigmath.m4()
        for pid in ("fixedjaw", "grizzly", "frame"):
            self.assertEqual(part(pid)["drivers"], [])
            self.assertEqual([[round(v, 12) + 0.0 for v in row] for row in matrix(pid, 2.5)], ident, pid)


class Anchors(unittest.TestCase):
    def test_the_power_face_is_on_the_eccentric_shafts_axis(self):
        self.assertEqual((RIG["powerCell"], RIG["powerFace"]), ([1, 1, 1], "east"))
        (rot,) = part("eshaft")["drivers"]
        self.assertEqual((rot["type"], rot["axis"], rot["pivot"][1:], rot["ratio"]), ("rotate", "x", [1.5, 1.5], 1.0))
        stub = [e for e in SHAPE["elements"] if e["name"].startswith("eshaft_stub")]
        self.assertEqual(len(stub), 2)
        self.assertEqual(max(e["to"][0] for e in stub), 32.0)

    def test_both_chutes_in_one_column_on_their_faces(self):
        self.assertEqual((RIG["infeedCell"], RIG["infeedFace"]), ([0, 2, 0], "up"))
        self.assertEqual((RIG["outputCell"], RIG["outputFace"]), ([0, 0, 0], "north"))
        self.assertEqual(RIG["infeed"]["pos"], [0.5, 3.0, 0.5])
        self.assertEqual(RIG["output"]["pos"], [0.5, 0.5, 0.0])
        flat = flatten(SHAPE["elements"], textures={})
        for name, face_axis, face in (("chutein", 1, 48.0), ("chuteout", 2, 0.0)):
            walls = [e for e in flat if e.name.startswith(f"fr_{name}_")]
            self.assertEqual(len(walls), 4, name)
            self.assertEqual({f["texture"] for e in walls for f in e.faces.values()}, {"#chute"})
            self.assertEqual(SHAPE["textures"]["chute"], "game:block/metal/sheet/copper1")
            ends = {round(e.aabb()[1][face_axis] if face_axis == 1 else e.aabb()[0][face_axis], 6) for e in walls}
            self.assertEqual(ends, {face}, name)
            # a vanilla chute's profile in the cell's face: 8 across, centred (4..12 in both other axes)
            across = [k for k in range(3) if k != face_axis]
            lo = [min(e.aabb()[0][k] for e in walls) for k in across]
            hi = [max(e.aabb()[1][k] for e in walls) for k in across]
            self.assertEqual([round(v % 16, 6) for v in lo + hi], [4.0, 4.0, 12.0, 12.0], name)


class Page(unittest.TestCase):
    def test_the_site_shows_it(self):
        (model,) = [m for m in MANIFEST["models"] if m["id"] == "jaw-breaker"]
        self.assertEqual(model["shape"], "mods-src/seraphhorizons/assets/seraphhorizons/shapes/block/jawbreaker.json")
        self.assertEqual(model["rig"], "mods-src/seraphhorizons/assets/seraphhorizons/config/jawbreaker-rig.json")
        self.assertEqual(set(model["scenario"]["requires"]), set(STAGES))

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
