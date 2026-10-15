"""The reverberatory roaster's generated model files (mods-src/seraphhorizons/ReverberatoryRoaster/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with the shared rig
maths and uses the build's `requires` vocabulary, its reference poses are its own maths, the cells are rebuilt from the
shipped shape (the finished machine's boxes) with a deck on every column, the build's stages (the services
last) are the scenario's states in site/models.json, the brick counts are measured again from the shipped brick, the bricks are the
crucible furnace's types, and the anchors (power, infeed, output, fuel, water, the outlet beside the drive's row with
the chimney on it) are where the contract puts them. Run with `python3 -m unittest discover -s tools/tests`.
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
sys.path.insert(0, str(MOD / "Pipes" / "tools"))
sys.path.insert(0, str(MOD / "ReverberatoryRoaster" / "tools"))

from machinegen import checks, rigmath  # noqa: E402
from machinegen.geometry import flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("reverberatory_make_shape", MOD / "ReverberatoryRoaster" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "reverberatory-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "reverberatory.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "reverberatory_frame.json").read_text())
CRUCIBLE = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "cruciblefurnace-rig.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "ReverberatoryRoaster" / "rig-reference.json").read_text())
MANIFEST = json.loads((ROOT / "site" / "models.json").read_text())
MOVING = {"entry", "lineshaft", "rabble1", "rabble2", "feedroll"}
RECTIFIED = {"lineshaft", "rabble1", "rabble2", "feedroll"}


def model():
    return next(m for m in MANIFEST["models"] if m["id"] == "reverberatory")


def group(gid):
    return next(g for g in model()["scenario"]["states"] if g["id"] == gid)


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        build = RIG["build"]
        vocab = {s["requires"] for s in build["stages"]} | {None, "charge", "fire", "outletcap"}
        for st in build["stages"]:
            vocab |= set(st.get("also", []))
        self.assertEqual({p["requires"] for p in RIG["parts"]}, vocab)
        self.assertNotIn("work", RIG)
        self.assertNotIn("trunkPath", RIG)
        for p in RIG["parts"]:
            self.assertIsNone(p.get("ride"))
            for d in p["drivers"]:
                rigmath.validate_driver(d)
                self.assertEqual(d["type"], "rotate")
                self.assertEqual(rigmath.driver_input(d), "travel" if p["id"] in RECTIFIED else "theta", p["id"])
        self.assertEqual({p["id"] for p in RIG["parts"] if p["drivers"]}, MOVING)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = [e["name"] for e in FRAME["elements"]]
        self.assertEqual(frame, [n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"])
        self.assertEqual({f["texture"] for e in FRAME["elements"] for f in e["faces"].values()}, {"#iron"})

    def test_reference_poses_are_the_rigs_own_maths(self):
        self.assertGreater(len(REFERENCE["poses"]), 10)
        for pose in REFERENCE["poses"]:
            self.assertEqual(pose["travel"], abs(pose["theta"]))
            for p in RIG["parts"]:
                got = rigmath.part_matrix(RIG["parts"], p["id"], {"theta": pose["theta"]})
                want = pose["matrices"][p["id"]]
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=6, msg=f"{p['id']} at {pose['theta']}")

    def test_beyond_the_rectifier_forward_either_way(self):
        for th in (0.6, 2.4, 9.0):
            for p in RIG["parts"]:
                a = rigmath.part_matrix(RIG["parts"], p["id"], {"theta": th})
                b = rigmath.part_matrix(RIG["parts"], p["id"], {"theta": -th})
                same = all(abs(a[r][c] - b[r][c]) < 1e-12 for r in range(3) for c in range(4))
                self.assertEqual(same, p["id"] != "entry", p["id"])
        gear = RIG["gearing"]
        self.assertEqual(gear["rabbleTurnsPerAxleTurn"], gear["pinionTeeth"] / gear["wheelTeeth"])
        for k in (1, 2):
            d = next(p for p in RIG["parts"] if p["id"] == f"rabble{k}")["drivers"][0]
            self.assertEqual(d["ratio"], -gear["rabbleTurnsPerAxleTurn"])


class Cells(unittest.TestCase):
    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(RIG["cells"], make_shape.shipped_cells(SHAPE, RIG["parts"]))
        self.assertEqual(checks.lid_gaps(RIG["cells"]), [])
        self.assertEqual(len(RIG["cells"]), 35)
        self.assertTrue(all(c.get("boxes") for c in RIG["cells"]))

    def test_anchors(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        normals = {"north": (0, 0, -1), "south": (0, 0, 1), "east": (1, 0, 0), "west": (-1, 0, 0), "up": (0, 1, 0)}
        for key in ("power", "infeed", "output", "fuel", "water", "outlet"):
            cell, face = tuple(RIG[f"{key}Cell"]), RIG[f"{key}Face"]
            self.assertIn(cell, cells, key)
            self.assertNotIn(tuple(cell[k] + normals[face][k] for k in range(3)), cells, key)
        self.assertEqual(RIG["powerCell"], [0, 2, 0])
        self.assertEqual(RIG["infeedCell"], [0, 2, 4])
        # the outlet stands beside the drive's row (x 0, y 2), not under it nor under the infeed; the chimney's cell is free
        out = RIG["outletCell"]
        self.assertNotEqual(out[0], 0)
        self.assertNotIn((out[0], out[1] + 1, out[2]), cells)
        line = RIG["chimney"]
        self.assertEqual((line["axis"], line["length"]), ("y", float(RIG["furnace"]["minChimney"])))
        self.assertAlmostEqual(line["origin"][1] - line["length"] / 2, out[1] + 1)
        self.assertTrue(out[0] <= line["origin"][0] <= out[0] + 1 and out[2] <= line["origin"][2] <= out[2] + 1)


class Build(unittest.TestCase):
    def test_stages_are_the_viewers_states(self):
        build = RIG["build"]
        self.assertTrue(build["provisional"])
        stages = [s["requires"] for s in build["stages"]]
        self.assertEqual(stages, ["brickwork", "lining", "arch", "hopper", "doors", "drive", "rabbles", "feeder", "stoker",
                                  "output", "services"])
        self.assertEqual(group("build")["values"], stages)
        self.assertEqual(group("build")["default"], "services")
        opts = {o["id"]: o["fitted"] for o in group("build")["options"]}
        self.assertEqual(opts, {"frame": [], **{sid: stages[:i + 1] for i, sid in enumerate(stages)}})
        run = {o["id"]: o["fitted"] for o in group("run")["options"]}
        self.assertEqual(run, {"cold": [], "running": ["charge", "fire"]})
        self.assertEqual(group("run")["needs"], stages)
        st = {s["requires"]: s for s in build["stages"]}
        self.assertEqual(group("damper")["values"], st["doors"]["also"])
        self.assertEqual(group("damper")["needs"], stages[:stages.index("doors") + 1])
        self.assertEqual(group("water")["values"], st["services"]["also"])
        self.assertEqual(group("water")["needs"], stages)
        ids = {g["id"] for g in model()["scenario"]["states"]}
        self.assertNotIn("level", ids)

    def test_counts_are_measured_from_the_shipped_brick(self):
        vol = {"bk": 0.0, "ln": 0.0, "arch": 0.0}
        for e in flatten(SHAPE["elements"], textures={}):
            v = abs(e.size[0] * e.size[1] * e.size[2])
            for k, prefix in (("bk", "bk_brick_"), ("ln", "ln_brick_"), ("arch", "arch_")):
                if e.name.startswith(prefix):
                    vol[k] += v
        st = {s["requires"]: s for s in RIG["build"]["stages"]}
        # the z-fighting fix steps a few faces in: the measured volume is the rig's within a hair
        self.assertAlmostEqual(vol["bk"] / 4096, st["brickwork"]["volumeBlocks"], places=2)
        self.assertAlmostEqual(vol["ln"] / 4096, st["lining"]["volumeBlocks"], places=2)
        self.assertAlmostEqual(vol["arch"] / 4096, st["arch"]["volumeBlocks"], places=2)
        self.assertEqual(st["brickwork"]["materials"], [{"code": "game:burnedbrick-*", "quantity": math.ceil(st["brickwork"]["volumeBlocks"] * 2 - 1e-9)}])
        lining = math.ceil(st["lining"]["volumeBlocks"] * 4 - 1e-9)
        self.assertEqual(st["lining"]["materials"], [{"code": "game:refractorybrick-fired-tier3", "quantity": lining},
                                                     {"code": "game:clay-fire", "quantity": math.ceil(lining / 2.5 - 1e-9)}])
        self.assertTrue(st["brickwork"]["oneType"])

    def test_bricks_are_the_crucible_furnaces(self):
        self.assertEqual(RIG["bricks"]["types"], CRUCIBLE["bricks"]["types"])
        self.assertEqual(RIG["bricks"]["default"], CRUCIBLE["bricks"]["default"])
        select = model()["scenario"]["textures"][0]
        self.assertEqual([o["id"] for o in select["options"]], list(RIG["bricks"]["types"]))
        crucible = next(m for m in MANIFEST["models"] if m["id"] == "crucible-furnace")["scenario"]["textures"][0]
        self.assertEqual(select["options"], crucible["options"])


class Binding(unittest.TestCase):
    """The shipped shape's binding is a closed box frame of angle iron: an upright at every outside vertical corner (the
    end faces' corners, the footing's step, the end blocks' inner east corners), a band along every top edge (the end
    faces', the west wall's outer and inner ones, the end blocks' east and inner ones, the footing's, the skewback angle
    under the arch's bay), knees at the footing's steps, and with the ironwork one connected frame: every member meets
    another over an area."""

    @staticmethod
    def boxes():
        return {e["name"]: (e["from"], e["to"]) for e in SHAPE["elements"]
                if e["name"].startswith(("fr_angle_", "fr_knee_", "fr_band_", "fr_skew_", "iw_"))}

    @staticmethod
    def touch(a, b, eps=1e-6):
        gap = [max(a[0][k], b[0][k]) - min(a[1][k], b[1][k]) for k in range(3)]
        return all(g <= eps for g in gap) and sum(1 for g in gap if g < -eps) >= 2

    @staticmethod
    def leg(boxes, name):
        """A leg's pieces (its name, or its name and a number: cut at the block grid)."""
        return [b for n, b in boxes.items() if n == name or n.startswith(name) and n[len(name):].isdigit()]

    def test_every_corner_and_top_edge_has_its_angle(self):
        boxes = self.boxes()
        legs = [f"fr_angle_{f}{s}_{leg}" for f in "ns" for s in "wefi" for leg in ("face", "side")]
        legs += [f"fr_knee_{f}_face" for f in "ns"]
        legs += [f"fr_band_{b}_{leg}" for b in ("n", "s", "w", "ne", "se", "f") for leg in ("face", "top")]
        legs += ["fr_band_ni_top", "fr_band_si_top", "fr_band_ni_facea", "fr_band_si_facea", "fr_band_wi_face",
                 "fr_skew_face", "fr_skew_top"]
        for name in legs:
            self.assertTrue(self.leg(boxes, name), name)

    def test_one_connected_frame(self):
        boxes = self.boxes()
        seen, todo = set(), [next(iter(boxes))]
        while todo:
            n = todo.pop()
            if n not in seen:
                seen.add(n)
                todo += [o for o in boxes if o not in seen and self.touch(boxes[n], boxes[o])]
        self.assertEqual(seen, set(boxes))

    def test_the_box_closes_at_its_corners(self):
        boxes = self.boxes()

        def meet(a, b):
            return any(self.touch(x, y) for x in self.leg(boxes, a) for y in self.leg(boxes, b))
        for f in "ns":
            self.assertTrue(meet(f"fr_knee_{f}_face", f"fr_angle_{f}f_face"))
            self.assertTrue(meet(f"fr_knee_{f}_face", f"fr_angle_{f}e_face"))
            self.assertTrue(meet(f"fr_band_{f}_top", f"fr_angle_{f}w_side"))
            self.assertTrue(meet(f"fr_band_{f}_top", f"fr_angle_{f}e_side"))
            self.assertTrue(meet("fr_band_w_face", f"fr_angle_{f}w_side"))
            self.assertTrue(meet("fr_skew_face", f"fr_angle_{f}e_side"))
            self.assertTrue(meet(f"fr_angle_{f}i_side", "fr_skew_face"))
            self.assertTrue(meet(f"fr_band_{f}e_top", f"fr_angle_{f}i_face"))
            self.assertTrue(meet("fr_band_f_top", f"fr_angle_{f}f_face"))


if __name__ == "__main__":
    unittest.main()
