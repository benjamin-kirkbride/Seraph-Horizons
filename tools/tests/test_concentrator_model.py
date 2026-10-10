"""The concentrator's generated model files (mods-src/seraphhorizons/Concentrator/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the
shared rig maths and uses the tiers' `requires` vocabulary (one part set per tier, tier 4 the table's and
the vanner's), its reference poses are its own maths, the cells are rebuilt from the shipped shape, the
rectifier turns the main shaft one way whichever way the axle turns, the anchors are where the brief puts
them (fixed for every tier), the site's tier states are the rig's tiers, and the README's moving-parts
table names every part. Run with `python3 -m unittest discover -s tools/tests`.
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
sys.path.insert(0, str(MOD / "Concentrator" / "tools"))

from machinegen import checks, rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("concentrator_make_shape", MOD / "Concentrator" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "concentrator-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "concentrator.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "concentrator_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "Concentrator" / "rig-reference.json").read_text())
README = (MOD / "Concentrator" / "README.md").read_text()
MANIFEST = json.loads((ROOT / "site" / "models.json").read_text())
REQUIRES = {"longtom", "jig", "table", "vanner", "plates", "dressing", None}
FRAME_MOVING = {"entry", "rectb1", "idler", "rectb2", "mainshaft"}


def matrix(pid, theta=0.0, travel=None):
    return rigmath.part_matrix(RIG["parts"], pid, {"theta": theta, "travel": abs(theta) if travel is None else travel})


def turn_x(m):
    return math.atan2(m[2][1], m[1][1])


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_tiers_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            if p["requires"] is None:
                self.assertIn(p["id"], FRAME_MOVING | {"frame"})
            if p.get("ride"):
                ride = next(q for q in RIG["parts"] if q["id"] == p["ride"])
                self.assertIn(ride["requires"], (None, p["requires"]), p["id"])
            for d in p["drivers"]:
                rigmath.validate_driver(d)
                # nothing reads the depth, the work or the oil: the shaft is the only input
                self.assertIn(d["type"], ("rotate", "slide"), p["id"])
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)
        self.assertNotIn("work", RIG)
        self.assertNotIn("trunkPath", RIG)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})
        used = {f["texture"].lstrip("#") for e in SHAPE["elements"] for f in e["faces"].values()}
        self.assertLessEqual(used, set(SHAPE["textures"]))

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreaterEqual(len(poses), 15)
        self.assertTrue(any(p["theta"] < 0 for p in poses))
        for pose in poses:
            for pid, want in pose["matrices"].items():
                got = matrix(pid, pose["theta"], pose["travel"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {pose['theta']}, {pose['travel']}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        cells = make_shape.shipped_cells(SHAPE, RIG["parts"])
        self.assertEqual(cells, RIG["cells"])
        self.assertEqual(checks.lid_gaps(RIG["cells"]), [])
        self.assertEqual(len(RIG["cells"]), 6 * 3 * 5)


class Drive(unittest.TestCase):
    def test_the_main_shaft_turns_one_way_whichever_way_the_axle_turns(self):
        for a in (0.4, 1.7):
            fwd = turn_x(matrix("mainshaft", a, a))
            back = turn_x(matrix("mainshaft", -a, a))
            self.assertAlmostEqual(fwd, back, places=9)
            self.assertGreater(fwd, 0.0)
            # the entry turns with the axle, either way
            self.assertAlmostEqual(turn_x(matrix("entry", -a, a)), -a, places=9)

    def test_every_tier_runs_off_the_main_shaft(self):
        # each tier's pulley is keyed on the main shaft (it rides it); everything else of a tier reads psi
        for pulley, req in (("ltpulley", "longtom"), ("jgpulley", "jig"), ("tbpulley", "table"), ("vnpulley", "vanner")):
            p = next(q for q in RIG["parts"] if q["id"] == pulley)
            self.assertEqual((p["ride"], p["requires"], p["drivers"]), ("mainshaft", req, []))
        for p in RIG["parts"]:
            if p["requires"] is not None:
                for d in p["drivers"]:
                    self.assertEqual(d.get("input"), "travel", p["id"])

    def test_the_table_returns_quicker_than_it_goes(self):
        # x = A (sin t - sin 2t / 2): forward (east, to the concentrate end) for two thirds of a stroke
        xs = [matrix("tbdeck", 0.0, t)[0][3] for t in (i * 2 * math.pi / make_shape.TB_RATIO / 360 for i in range(361))]
        fwd = sum(1 for a, b in zip(xs, xs[1:]) if b > a)
        self.assertAlmostEqual(fwd / 360, 2 / 3, delta=0.02)
        self.assertAlmostEqual((max(xs) - min(xs)) * 16, 2 * 1.299 * make_shape.TB_A, delta=0.01)

    def test_the_vanner_belt_creeps_up_slope(self):
        # the head roller's top runs east (towards the head, up the belt's slope) as the main shaft turns,
        # whichever way the axle does, by beltPerTurn a turn
        head = next(q for q in RIG["parts"] if q["id"] == "vnhead")
        hx, hy, hz = head["drivers"][0]["pivot"]
        top = [hx, hy + make_shape.ROLL_R / 16, hz]
        for theta in (0.5, -0.5):
            frame = rigmath.apply(matrix("vnframe", theta, 0.5), top)
            moved = rigmath.apply(matrix("vnhead", theta, 0.5), top)
            self.assertGreater(moved[0] - frame[0], 0.0)
        m = matrix("vnhead", 0.0, 2 * math.pi)
        per_turn = abs(math.atan2(m[1][0], m[0][0])) * make_shape.ROLL_R / 16
        self.assertAlmostEqual(per_turn, RIG["drive"]["beltPerTurn"], places=5)


class Anchors(unittest.TestCase):
    def test_fixed_points(self):
        self.assertEqual((RIG["powerCell"], RIG["powerFace"]), ([0, 2, 2], "west"))
        self.assertEqual((RIG["waterCell"], RIG["waterFace"]), ([0, 1, 0], "north"))
        self.assertEqual((RIG["feedCell"], RIG["feedFace"]), ([1, 1, 0], "north"))
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        for key in ("powerCell", "waterCell", "feedCell"):
            self.assertIn(tuple(RIG[key]), cells)
            self.assertNotIn(True, [c.get("hollow") for c in RIG["cells"] if c["pos"] == RIG[key]])
        self.assertEqual(RIG["concentrateSide"], "east")
        self.assertEqual(RIG["tailingsSide"], "west")
        self.assertAlmostEqual(RIG["concentrate"]["pos"][0], 6.0)
        self.assertAlmostEqual(RIG["tailings"]["pos"][0], 0.0)
        # the fixed points are the frame's: no tier's element is on them
        for key in ("feedSpout", "waterSpout"):
            self.assertEqual(len(RIG[key]["pos"]), 3)

    def test_the_inlet_meets_a_ppex_pipe_on_the_water_face(self):
        inlet = next(e for e in SHAPE["elements"] if e["name"] == "fr_pipe_inlet")
        flange = next(e for e in SHAPE["elements"] if e["name"] == "fr_pipe_flange")
        # shapes are in voxels, the rig in blocks
        cx, cy, _ = ((c + 0.5) * 16 for c in RIG["waterCell"])
        self.assertEqual([round(inlet["to"][k] - inlet["from"][k], 4) for k in (0, 1)], [6.0, 6.0])
        self.assertAlmostEqual((inlet["from"][0] + inlet["to"][0]) / 2, cx)
        self.assertAlmostEqual((inlet["from"][1] + inlet["to"][1]) / 2, cy)
        self.assertAlmostEqual(flange["from"][2], 0.0)

    def test_the_entry_meets_the_axle_on_the_power_face(self):
        ends = [e for e in SHAPE["elements"] if re.match(r"^entry_shaft[ab]$", e["name"])]
        self.assertEqual(len(ends), 2)
        self.assertEqual(min(e["from"][0] for e in ends), 0.0)
        for e in ends:
            self.assertLessEqual(e["from"][0], 0.02)
            self.assertAlmostEqual((e["from"][1] + e["to"][1]) / 2, (RIG["powerCell"][1] + 0.5) * 16)
            self.assertAlmostEqual((e["from"][2] + e["to"][2]) / 2, (RIG["powerCell"][2] + 0.5) * 16)
        # the vanilla axle's cross: two boards 4 x 2 and 2 x 4
        self.assertEqual(sorted(sorted(round(e["to"][k] - e["from"][k], 4) for k in (1, 2)) for e in ends), [[2.0, 4.0], [2.0, 4.0]])


class Plates(unittest.TestCase):
    def test_the_plates_are_copper_and_their_dressing_silver_and_both_stand_still(self):
        for pid in ("plates", "dressing"):
            p = next(q for q in RIG["parts"] if q["id"] == pid)
            self.assertEqual((p["requires"], p["drivers"], p.get("ride")), (pid, [], None))
        coats = [e for e in SHAPE["elements"] if e["name"].startswith("pd_")]
        self.assertEqual({f["texture"] for e in coats for f in e["faces"].values()}, {"#amalgam"})
        plates = [e for e in SHAPE["elements"] if re.match(r"^pl_plate\d$", e["name"])]
        self.assertEqual(len(plates), 3)
        self.assertEqual({f["texture"] for e in plates for f in e["faces"].values()}, {"#copper"})
        self.assertIn("copper", SHAPE["textures"])
        self.assertIn("amalgam", SHAPE["textures"])

    def test_the_plates_lie_in_the_frames_plate_table_by_the_outlet(self):
        bed = next(e for e in SHAPE["elements"] if e["name"] == "fr_pt_floor")
        lo = [min(bed["from"][k], bed["to"][k]) for k in range(3)]
        hi = [max(bed["from"][k], bed["to"][k]) for k in range(3)]
        on = [e for e in SHAPE["elements"] if e["name"].startswith(("pl_", "pd_"))]
        for e in on:
            for k in (0, 2):
                self.assertGreaterEqual(min(e["from"][k], e["to"][k]), lo[k] - 1.0, e["name"])
                self.assertLessEqual(max(e["from"][k], e["to"][k]), hi[k] + 1.0, e["name"])
        # the outlet is the plate table's, on the east face, beyond the plates
        self.assertAlmostEqual(RIG["concentrate"]["pos"][0], 6.0)
        self.assertGreater(RIG["concentrate"]["pos"][2] * 16, max(max(e["from"][2], e["to"][2]) for e in on))


class Tiers(unittest.TestCase):
    def test_the_rig_and_the_site_agree_on_the_tiers(self):
        self.assertEqual([t["requires"] for t in RIG["tiers"]], [["longtom"], ["jig"], ["table"], ["table", "vanner"]])
        model = next(m for m in MANIFEST["models"] if m["id"] == "concentrator")
        self.assertEqual(model["shape"], "mods-src/seraphhorizons/assets/seraphhorizons/shapes/block/concentrator.json")
        states = model["scenario"]["states"]
        self.assertEqual(states["default"], "tier4plates")
        opts = {o["id"]: o["fitted"] for o in states["options"]}
        tiers = {f"tier{t['tier']}": t["requires"] for t in RIG["tiers"]}
        self.assertEqual({k: v for k, v in opts.items() if not k.endswith("plates")}, {"frame": [], **tiers})
        # the plates: fitted on top of a tier from fromTier, bare in one state and dressed in another
        plates = RIG["plates"]
        self.assertEqual((plates["requires"], plates["dressing"], plates["fromTier"]), ("plates", "dressing", 2))
        dressed = set()
        for k, v in opts.items():
            if k.endswith("plates"):
                n = int(k[4])
                self.assertGreaterEqual(n, plates["fromTier"])
                self.assertEqual(v[:len(tiers[f"tier{n}"])], tiers[f"tier{n}"])
                self.assertIn(v[len(tiers[f"tier{n}"]):], (["plates"], ["plates", "dressing"]))
                dressed.add("dressing" in v)
        self.assertEqual(dressed, {False, True})
        self.assertEqual(set(model["scenario"]["requires"]), REQUIRES - {None})

    def test_the_readme_names_every_part(self):
        table = README.split("## Every moving part", 1)[1].split("\n## ", 1)[0]
        for p in RIG["parts"]:
            self.assertIn(f"`{p['id']}`", table, p["id"])


if __name__ == "__main__":
    unittest.main()
