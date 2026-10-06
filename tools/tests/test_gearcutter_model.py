"""The gear cutter's generated model files (mods-src/seraphhorizons/GearCutter/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with
the shared rig maths, its reference poses are its own maths, the cells are rebuilt from the shipped
shape, the anchors and the cut's constants agree with the generator's, and the index lands on whole
teeth. Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import math
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(MOD / "GearCutter" / "tools"))

from machinegen import checks, rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("gearcutter_make_shape", MOD / "GearCutter" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "gearcutter-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "gearcutter.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "gearcutter_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "GearCutter" / "rig-reference.json").read_text())
# the build order (#480): the frame, spindle, feedscrew, camfeed, camindex, liftcam, index, oiler, head, cutter, then a master
REQUIRES = {"spindle", "feedscrew", "camfeed", "camindex", "liftcam", "index", "oiler", "head", "cutter", "master", "masterlarge",
            "blanksmall", "blanklarge", "cover", None}


def inputs(pose):
    return {k: pose[k] for k in ("theta", "travel", "work", "size", "presence", "oil")}


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

    def test_each_cam_drum_is_its_own_stage(self):
        parts = {p["id"]: p for p in RIG["parts"]}
        self.assertIsNone(parts["camshaft"]["requires"])
        self.assertEqual(parts["camfeed"]["requires"], "camfeed")
        self.assertEqual(parts["camindex"]["requires"], "camindex")
        self.assertEqual(parts["liftcam"]["requires"], "liftcam")
        names = [e["name"] for e in SHAPE["elements"]]
        part = lambda n: rigmath.part_of(RIG["parts"], n)  # noqa: E731
        for n in names:
            if n.startswith(("cam_shaft", "cam_wheel")):
                self.assertEqual(part(n), "camshaft", n)
            elif n.startswith(("cam_fdrum", "cam_fgroove")):
                self.assertEqual(part(n), "camfeed", n)
            elif n.startswith(("cam_idrum", "cam_igroove")):
                self.assertEqual(part(n), "camindex", n)
            elif n.startswith("cam_lift"):
                self.assertEqual(part(n), "liftcam", n)
        # all three turn as one shaft
        drivers = {pid: parts[pid]["drivers"] for pid in ("camshaft", "camfeed", "camindex", "liftcam")}
        for pid in ("camfeed", "camindex", "liftcam"):
            self.assertEqual(drivers[pid], drivers["camshaft"], pid)

    def test_the_oiler_stage_is_the_injection_valve(self):
        parts = {p["id"]: p for p in RIG["parts"]}
        self.assertEqual(parts["valve"]["requires"], "oiler")
        self.assertEqual(parts["valveplunger"]["requires"], "oiler")
        # the reservoir and its oil are the frame's: filled whether or not the valve is fitted
        self.assertIsNone(parts["oillevel"]["requires"])
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertTrue(any(n.startswith("fr_oiler_glass") for n in names))
        self.assertTrue(all(rigmath.part_of(RIG["parts"], n) == "valve" for n in names if n.startswith("valve_")))
        # the plunger pumps once a tooth, off the work
        (d,) = parts["valveplunger"]["drivers"]
        self.assertEqual((d["type"], d["input"]), ("slide", "work"))
        self.assertAlmostEqual(d["ratio"], 2 * math.pi, places=5)
        # the drip anchor is the nozzle's mouth: under it, the cutter's teeth
        x, y, z = (c * 16 + o * 16 for c, o in zip(RIG["drip"]["pos"], make_shape.ORIGIN_CELL))
        self.assertAlmostEqual(z, make_shape.Z_CUT, places=2)       # anchors are rounded to 1e-6 blocks
        self.assertLess(abs(x - make_shape.X_BLANK), make_shape.CUTTER_R)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

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


class Cut(unittest.TestCase):
    def test_work_counts_teeth(self):
        self.assertNotIn("trunkPath", RIG)
        w = rigmath.progress_of(RIG)
        self.assertEqual((w["name"], w["unit"]), ("teeth cut", "teeth"))
        self.assertEqual(rigmath.work_end(w, 1), 12.0)
        self.assertEqual(rigmath.work_end(w, 2), 20.0)
        self.assertEqual(w["end"], {"thin": 12.0, "thick": 20.0})
        self.assertEqual(RIG["cut"]["turnsPerTooth"], make_shape.WORM_TEETH)

    def test_the_gears_are_the_games_sizes(self):
        # the gear and the temporal gear are 6.7 voxels across, the large temporal gear 10.2: one module, 12 and 20
        # teeth, each across between its pitch and its tip circles
        self.assertEqual(make_shape.MODULE, 0.5)
        self.assertEqual(make_shape.PITCH_R, {"thin": 3.0, "thick": 5.0})
        for cls, across in (("thin", 6.7), ("thick", 10.2)):
            self.assertTrue(2 * make_shape.PITCH_R[cls] <= across <= 2 * (make_shape.PITCH_R[cls] + make_shape.ADD), cls)
        # thicknesses measured element by element: the gear and the temporal gear 1.4, the large temporal gear 2.0;
        # a blank as thick as the gear it becomes
        self.assertEqual(make_shape.FACE_W, {("blank", "thin"): 1.4, ("blank", "thick"): 2.0,
                                             ("master", "thin"): 1.4, ("master", "thick"): 2.0})
        for kind, cls in make_shape.FACE_W:
            a, b = make_shape.face(kind, cls)
            s0, s1 = make_shape.STATION[kind]
            self.assertTrue(s0 - 1e-9 <= a and b <= s1 + 1e-9, (kind, cls))



    def test_the_index_lands_on_whole_teeth_and_ends_where_it_started(self):
        parts, path = RIG["parts"], RIG["work"]
        for k, cls in ((1, "thin"), (2, "thick")):
            n = int(RIG["work"]["end"][cls])
            for j in (0, 1, n // 2, n):
                ins = {"theta": 0.0, "travel": 0.0, "work": float(j), "size": k, "presence": 1.0}
                m = rigmath.part_matrix(parts, "arbor", ins, path)
                got = math.atan2(m[2][1], m[1][1])
                self.assertLess(abs(math.remainder(got + j * 2 * math.pi / n, 2 * math.pi)), 1e-4, f"{cls} after {j}")
                # the housing turns 1.5 times the arbor's step (the sun held); at the end of a gear it is half a turn on
                ring = rigmath.part_matrix(parts, "jring", ins, path)
                want = -1.5 * j * 2 * math.pi / n
                self.assertLess(abs(math.remainder(math.atan2(ring[2][1], ring[1][1]) - want, 2 * math.pi)), 1e-4, f"{cls} after {j}")

    def test_the_knee_drops_for_the_large_master(self):
        parts, path = RIG["parts"], RIG["work"]
        small = rigmath.part_matrix(parts, "knee", {"work": 0.0, "size": 1, "presence": 1.0}, path)
        large = rigmath.part_matrix(parts, "knee", {"work": 0.0, "size": 2, "presence": 1.0}, path)
        self.assertAlmostEqual((small[1][3] - large[1][3]) * 16, make_shape.PITCH_R["thick"] - make_shape.PITCH_R["thin"], places=5)


class Anchors(unittest.TestCase):
    def test_cells_and_faces(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        self.assertIn((0, 0, 0), cells)
        self.assertEqual(len(cells), 8)
        self.assertEqual(checks.lid_gaps(RIG["cells"]), [])
        power = tuple(RIG["powerCell"])
        self.assertIn(power, cells)
        self.assertEqual(RIG["powerFace"], "west")
        self.assertNotIn((power[0] - 1, power[1], power[2]), cells)
        for key in ("output", "chips", "drip"):
            self.assertEqual(len(RIG[key]["pos"]), 3)
        self.assertNotIn("oil", RIG)        # the oil is MachineOil's tank: no fill anchor

    def test_the_oil_level_follows_the_oil_input(self):
        parts, path = RIG["parts"], RIG["work"]
        (lvl,) = [p for p in parts if p["id"] == "oillevel"]
        self.assertEqual([d["input"] for d in lvl["drivers"]], ["oil"])
        heights = []
        for oil in (0.0, 0.5, 1.0):
            m = rigmath.part_matrix(parts, "oillevel", {"oil": oil}, path)
            heights.append(m[1][1] * lvl["drivers"][0]["length"] * 16)
        self.assertAlmostEqual(heights[0], make_shape.OIL_EMPTY, places=5)
        self.assertAlmostEqual(heights[2], make_shape.OIL_FULL, places=5)
        self.assertAlmostEqual(heights[1], (heights[0] + heights[2]) / 2, places=5)


if __name__ == "__main__":
    unittest.main()
