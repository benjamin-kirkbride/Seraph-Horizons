"""The rosser's generated model files (mods-src/seraphhorizons/Rosser/tools/make_shape.py).

The generator itself needs the fetched IW and Logging Expanded zips, so CI does not run it; these
tests hold what it wrote to its own rules without them: the shipped rig parses, its reference poses
are its own maths, its anchors and feed constants agree with the generator's constants, and its
parts cover the shape. Run with `python3 -m unittest discover -s tools/tests`.
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

from machinegen import checks, rigmath  # noqa: E402
from test_gear_consumers import PATCHES, loads  # noqa: E402

_spec = importlib.util.spec_from_file_location("rosser_make_shape", MOD / "Rosser" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "rosser-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "rosser.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "rosser_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "Rosser" / "rig-reference.json").read_text())
BLOCKTYPE = loads((MOD / "assets" / "seraphhorizons" / "blocktypes" / "rosser" / "frame.json").read_text())
SOLDERED = MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "pipes"   # UnifiedPipes' copper and lead ppex pipes
# the stages' requires (RosserRequires.KnownRequires): the drip's pipes are one per metal, the fitted one drawn
REQUIRES = {"shaft", "ring", "tyres", "rollsin", "rollsout", "breaker", "levers", "pipecopper", "pipelead", "heads", None}


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

    def test_globs_tell_the_traps_apart(self):
        parts = RIG["parts"]
        self.assertEqual(rigmath.part_of(parts, "ring_body1"), "ring")
        self.assertEqual(rigmath.part_of(parts, "ringtyre1_1"), "ringtyre")
        self.assertEqual(rigmath.part_of(parts, "ringpinion_iwtooth1"), "main")
        self.assertEqual(rigmath.part_of(parts, "rock_shaft"), "rock")
        self.assertEqual(rigmath.part_of(parts, "rocker_shaft"), "rocker")
        self.assertEqual(rigmath.part_of(parts, "toproll_in_body_1"), "toproll_in")
        self.assertEqual(rigmath.part_of(parts, "toparm_in_tie"), "toparm_in")
        self.assertEqual(rigmath.part_of(parts, "fr_bearing_rock58"), "frame")

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_reference_poses_are_the_rigs_own_maths(self):
        path = RIG["trunkPath"]
        poses = REFERENCE["poses"]
        self.assertGreater(len(poses), 100)
        self.assertEqual({p["size"] for p in poses}, {0, 1, 2})
        for pose in poses[::5]:
            ins = {k: pose[k] for k in ("theta", "travel", "feed", "trunk", "size", "presence")}
            for pid, want in pose["matrices"].items():
                got = rigmath.part_matrix(RIG["parts"], pid, ins, path)
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {ins}")


    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        cells = make_shape.shipped_cells(SHAPE, RIG["parts"], RIG["trunkPath"], RIG["cells"])
        self.assertEqual(cells, RIG["cells"])


class Anchors(unittest.TestCase):
    def test_cells_and_faces(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        self.assertIn((0, 0, 0), cells)
        for c in RIG["cells"]:
            self.assertFalse(c.get("hollow") and c.get("boxes"), c["pos"])
            self.assertTrue(c.get("hollow") or c.get("boxes"), c["pos"])     # (a cell with neither would be a full cube)
        self.assertEqual(checks.lid_gaps(RIG["cells"], make_shape.shipped_station_column), [])
        lidded = {(c["pos"][0], c["pos"][2]) for c in RIG["cells"] if "lid" in c}
        self.assertEqual(len(lidded), 45)                                    # the station's 9 x 5, none over the beds
        self.assertTrue(all(-12 <= x <= -4 for x, _ in lidded), lidded)
        power, water = tuple(RIG["powerCell"]), tuple(RIG["waterCell"])
        self.assertIn(power, cells)
        self.assertIn(water, cells)
        self.assertGreater(power[1], 0)
        self.assertEqual((RIG["powerFace"], RIG["waterFace"], RIG["chute"]["side"], RIG["chuteSide"]), ("north", "south", "south", "south"))
        self.assertNotIn((power[0], power[1], power[2] - 1), cells)
        self.assertNotIn((water[0], water[1], water[2] + 1), cells)
        # the east end leaves a mill in line its power feed cell (the mill's [-6,3,0] is our [0,3,0])
        self.assertNotIn((0, 3, 0), cells)

    def test_trunk_path_matches_the_generator(self):
        m = make_shape
        p = RIG["trunkPath"]
        dx = -m.ORIGIN_CELL[0]
        self.assertAlmostEqual(p["nose0"], m.NOSE0 / 16 + dx, places=6)
        self.assertAlmostEqual(p["tailStop"], m.TAIL_STOP / 16 + dx, places=6)
        self.assertEqual(p["lengths"], {"thin": 4, "thick": 5})
        self.assertAlmostEqual(p["origin"][1], m.H / 16, places=6)
        self.assertAlmostEqual(p["origin"][2], m.TZ / 16 - m.ORIGIN_CELL[2], places=6)
        st = p["stations"]
        self.assertLess(p["nose0"], st["breaker"])
        self.assertLess(st["breaker"], st["ring"])
        self.assertLessEqual(st["ring"], p["tailStop"])
        # the heads touch the trunk downstream of the ring's plane, the thick trunk's further out,
        # and the tail clears them before the trip ends
        tips = p["tips"]
        self.assertLess(st["ring"], tips["thin"])
        self.assertLess(tips["thin"], tips["thick"])
        self.assertLess(tips["thick"], p["tailStop"])
        self.assertLess(tips["thick"] - st["ring"], 2.0)
        # a delivered thick trunk stays inside the machine
        self.assertLessEqual(p["tailStop"] + 5, 1.0)

    def test_feed_constants_are_the_drawn_gears(self):
        m = make_shape
        feed = RIG["feed"]
        self.assertAlmostEqual(feed["blocksPerRadian"], m.ROLL_RHO / 16 * m.BANJO[0] / m.BANJO[1], places=6)
        lay = m.G / m.WHEEL_TEETH
        self.assertAlmostEqual(feed["gear"]["thin"], lay * m.FAST[0] / m.FAST[1], places=5)
        self.assertAlmostEqual(feed["gear"]["thick"], lay * m.SLOW[0] / m.SLOW[1], places=5)
        self.assertLess(feed["gear"]["thick"], feed["gear"]["thin"])
        # the cross shafts turn once per feed radian, the selectors keyed to them
        for st in ("in", "out"):
            cross = next(p for p in RIG["parts"] if p["id"] == f"cross_{st}")["drivers"][0]
            self.assertEqual((cross["input"], abs(cross["ratio"])), ("feed", 1.0))

    def test_change_gears_are_whole_teeth_at_one_pitch_and_one_centre_distance(self):
        m = make_shape
        self.assertEqual(sum(m.FAST), sum(m.SLOW))
        self.assertAlmostEqual(m.CHANGE_DX, sum(m.FAST) * m.CHANGE_MODULE / 2)
        self.assertAlmostEqual(m.WORM_LEAD, 2 * math.pi * m.WHEEL_PITCH_R / m.WHEEL_TEETH)
        self.assertAlmostEqual(m.MAIN_Y - m.SHAFT_Y, m.WORM_PITCH_R + m.WHEEL_PITCH_R)
        self.assertAlmostEqual(2 * math.pi * m.RING_PITCH_R / m.RING_TEETH, 2 * math.pi * m.PINION_R / m.PINION_TEETH)
        dist = math.hypot(m.MAIN_Y - m.H, m.MAIN_Z - m.TZ)
        self.assertAlmostEqual(dist, m.RING_PITCH_R + m.PINION_R, delta=0.05)


class Pipes(unittest.TestCase):
    """The drip's pipes: a part per metal the rosser takes (copper, lead), the same elements in each
    metal's pipe texture, the one UnifiedPipes gives that metal's ppex pipes, and their joints in
    solder; the inlet meets a pipe on the water face end to end; and they are UnifiedPipes' soldered
    pipe, the copper and lead ppex pipes' own model (shapes/block/pipes/soldered-*.json): the same
    tube, texture mapping and wiped joint."""

    @staticmethod
    def elements(metal):
        return sorted((e for e in SHAPE["elements"] if e["name"].startswith(f"pipe{metal}_")), key=lambda e: e["name"])

    def test_the_block_declares_every_texture_of_the_shape(self):
        # the renderer draws the moving parts, the pipes among them, with the frame block's textures
        self.assertEqual({code: t["base"] for code, t in BLOCKTYPE["textures"].items()}, SHAPE["textures"])
        for code, path in FRAME["textures"].items():
            self.assertEqual(SHAPE["textures"][code], path, code)
        for shape in (SHAPE, FRAME):
            used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
            self.assertLessEqual(used, set(shape["textures"]))

    def test_each_metal_wears_its_ppex_pipe_texture(self):
        patch = loads((PATCHES / "unifiedpipes-ppex.json").read_text())
        straight = {op["path"].rsplit("/", 1)[1]: op["value"]["iron4"]["base"] for op in patch
                    if op["file"] == "ppex:blocktypes/pipes/straight.json" and op["path"].startswith("/texturesByType/")}
        self.assertEqual(list(make_shape.PIPE_METALS), ["copper", "lead"])
        for metal in make_shape.PIPE_METALS:
            self.assertEqual(SHAPE["textures"][f"pipe{metal}"], straight[f"*-{metal}"], metal)
            els = self.elements(metal)
            self.assertTrue(els, metal)
            self.assertEqual({f["texture"] for e in els for f in e["faces"].values()}, {f"#pipe{metal}", "#solder"})
        # the solder is the soldered ppex pipes'
        soldered = json.loads((SOLDERED / "soldered-straight.json").read_text())
        self.assertEqual(SHAPE["textures"]["solder"], soldered["textures"]["solder"])
        # no other element wears a pipe's metal or solder, and the frame has no pipes (they are a stage)
        others = {f["texture"] for e in SHAPE["elements"] if not e["name"].startswith("pipe") for f in e["faces"].values()}
        self.assertFalse({t for t in others if t.startswith("#pipe") or t == "#solder"})
        self.assertFalse([e["name"] for e in FRAME["elements"] if e["name"].startswith("pipe")])

    def test_the_metals_are_the_same_pipes(self):
        copper, lead = self.elements("copper"), self.elements("lead")
        self.assertEqual([e["name"][len("pipecopper"):] for e in copper], [e["name"][len("pipelead"):] for e in lead])
        for a, b in zip(copper, lead):
            strip = {k: v for k, v in a.items() if k not in ("name", "faces")}
            self.assertEqual(strip, {k: v for k, v in b.items() if k not in ("name", "faces")}, a["name"])
            self.assertEqual({d: {**f, "texture": "#pipelead" if f["texture"] == "#pipecopper" else f["texture"]} for d, f in a["faces"].items()},
                             b["faces"], a["name"])
        for metal in ("copper", "lead"):
            part = next(p for p in RIG["parts"] if p["id"] == f"pipe{metal}")
            self.assertEqual((part["requires"], part["drivers"], part.get("ride")), (f"pipe{metal}", [], None))

    def test_the_inlet_meets_a_pipe_on_the_water_face(self):
        # shipped voxels: the water cell's south face, the inlet on its middle in ppex's 6 x 6 section
        wx, wy, wz = RIG["waterCell"]
        self.assertEqual(RIG["waterFace"], "south")
        inlet = [e for e in self.elements("copper") if e["name"].startswith("pipecopper_inlet")]
        self.assertTrue(inlet)
        lo = [min(e["from"][k] for e in inlet) for k in range(3)]
        hi = [max(e["to"][k] for e in inlet) for k in range(3)]
        self.assertAlmostEqual(hi[2], (wz + 1) * 16, places=2)
        self.assertAlmostEqual((lo[0] + hi[0]) / 2, (wx + 0.5) * 16, places=2)
        self.assertAlmostEqual((lo[1] + hi[1]) / 2, (wy + 0.5) * 16, places=2)
        self.assertAlmostEqual(hi[0] - lo[0], 6, places=2)
        self.assertAlmostEqual(hi[1] - lo[1], 6, places=2)

    @staticmethod
    def relative(els, origin):
        """Each element's (from, to) less `origin`, rounded, by name order."""
        return sorted((tuple(round(e["from"][k] - origin[k], 4) for k in range(3)), tuple(round(e["to"][k] - origin[k], 4) for k in range(3)))
                      for e in els)

    def test_the_pipes_are_the_soldered_ppex_pipe(self):
        """A rosser pipe and a copper or lead ppex pipe read as the same pipe: the inlet's tube is the
        soldered straight's in section about the axis, every face maps its texture as the soldered
        pipes' do (one texel a voxel from the corner, on this model's 64-texel scale), and the header's
        joint is two soldered straights' half joints where they meet."""
        straight = json.loads((SOLDERED / "soldered-straight.json").read_text())
        pipe = self.elements("copper")
        scale = SHAPE["textureSizes"]["pipecopper"][0] / straight["textureWidth"]
        self.assertEqual(SHAPE["textureSizes"]["solder"], SHAPE["textureSizes"]["pipecopper"])
        for e in pipe:
            size = [e["to"][k] - e["from"][k] for k in range(3)]
            self.assertFalse({"rotationX", "rotationY", "rotationZ"} & set(e), e["name"])
            for d, f in e["faces"].items():
                self.assertEqual([round(v, 3) for v in make_shape.solderedpipe.uv(d, size, scale)], f["uv"], f"{e['name']} {d}")
        # the tube: the inlet's walls about its axis are the soldered straight's about the block's middle
        wx, wy, wz = RIG["waterCell"]
        section = lambda els, cx, cy: sorted((round(e["from"][0] - cx, 4), round(e["from"][1] - cy, 4),  # noqa: E731
                                              round(e["to"][0] - cx, 4), round(e["to"][1] - cy, 4)) for e in els)
        body = [e for e in straight["elements"] if e["name"].startswith("pipe_")]
        inlet = [e for e in pipe if e["name"].startswith("pipecopper_inlet_")]
        self.assertEqual(4, len(inlet))
        self.assertEqual(section(body, 8, 8), section(inlet, (wx + 0.5) * 16, (wy + 0.5) * 16))
        # the header's joint: a straight's south half joint and the next straight's north one, about the seam
        halves = [e for e in straight["elements"] if e["name"].startswith("joint_south")]
        halves += [{**e, "from": [e["from"][0], e["from"][1], e["from"][2] + 16], "to": [e["to"][0], e["to"][1], e["to"][2] + 16]}
                   for e in straight["elements"] if e["name"].startswith("joint_north")]
        joint = [e for e in pipe if e["name"].startswith("pipecopper_joint_header")]
        header = [e for e in pipe if e["name"].startswith("pipecopper_header")]
        cx = (min(e["from"][0] for e in header) + max(e["to"][0] for e in header)) / 2
        cy = (min(e["from"][1] for e in header) + max(e["to"][1] for e in header)) / 2
        seam = (min(e["from"][2] for e in joint) + max(e["to"][2] for e in joint)) / 2
        self.assertEqual(self.relative(halves, (8, 8, 16)), self.relative(joint, (cx, cy, seam)))
        self.assertEqual({"#solder"}, {f["texture"] for e in joint for f in e["faces"].values()})
        # the joint sits on a seam of the header's lengths, as two blocks' pipes meet at their faces
        self.assertIn(round(seam, 4), {round(e["to"][2], 4) for e in header})


if __name__ == "__main__":
    unittest.main()
