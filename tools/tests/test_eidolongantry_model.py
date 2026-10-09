"""The eidolon gantry's generated model files (mods-src/seraphhorizons/EidolonGantry/tools/make_shape.py).

These hold what the generator wrote to its own rules: it reproduces the committed files from the
eidolon's shape as the repository has it now (so a changed eidolon.json fails here until the gantry is
regenerated); the body in the written shape is the eidolon's `hung` pose, element for element, by the
game's pose maths (Eidolon/tools/kin.py), each element under its build stage's part; the rig's
`requires` are one per stage of the build, in order: the frame needs none, the winch is fitted in stages (axles, the crank
shaft, gears, the drum, strapping, the ratchet, the crank, the chain with the ring), then the spine (vanilla's, cut off
the eidolon: the gantry's) is hung on the ring, then the body; the let-down brings the lowest toe from 3
voxels to the floor, the spine with it; the cells are rebuilt from the shipped shape; the front is open; the crank
is outside the frame, in its own cell, which is hollow; the winch is geared in wood, lantern pinions driving cog wheels, its
parts turning by their stave and cog counts; every wooden face of the frame, drum and sheave takes one of the two
wood-variant texture codes, and the axles and gears vanilla's mechanical power texture; the spine takes a support
beam for each of its mast's three lengths and is drawn in the gantry's wood; and the README's stage table is
the generator's. Run with
`python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import math
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
TOOLS = MOD / "EidolonGantry" / "tools"
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(MOD / "Eidolon" / "tools"))
sys.path.insert(0, str(TOOLS))

import kin  # noqa: E402
from machinegen import rigmath  # noqa: E402
from machinegen.geometry import flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("eidolongantry_make_shape", TOOLS / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

SHAPE_PATH = MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "eidolongantry.json"
FRAME_PATH = MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "eidolongantry_frame.json"
RIG_PATH = MOD / "assets" / "seraphhorizons" / "config" / "eidolongantry-rig.json"
SHAPE = json.loads(SHAPE_PATH.read_text())
FRAME = json.loads(FRAME_PATH.read_text())
RIG = json.loads(RIG_PATH.read_text())
EIDOLON = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "entity" / "eidolon" / "eidolon.json").read_text())
STAGES = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "eidolon-stages.json").read_text())
STAGE_CODES = [s["code"] for s in STAGES["stages"] if s["elements"]]
WINCH_STAGES = make_shape.winch_stages()
BUILD = [code for code, *_ in WINCH_STAGES] + ["spine"] + STAGE_CODES
OFF = make_shape.OFF
README = (MOD / "EidolonGantry" / "README.md").read_text()


def part(pid):
    return next(p for p in RIG["parts"] if p["id"] == pid)


def matrix(pid, depth):
    return rigmath.part_matrix(RIG["parts"], pid, {"depth": depth})


def posed_written(depth, parts):
    out = []
    for el in flatten(SHAPE["elements"], textures={}):
        pid = rigmath.part_of(RIG["parts"], el.name)
        if pid in parts:
            out.append(rigmath.posed(el, matrix(pid, depth)))
    return out


def drawn(e):
    return any(f.get("enabled", True) for f in e.get("faces", {}).values())


class Generator(unittest.TestCase):
    def test_the_generator_reproduces_the_files(self):
        with tempfile.TemporaryDirectory() as d:
            run = subprocess.run([sys.executable, str(TOOLS / "make_shape.py"), "--out", d], capture_output=True, text=True)
            self.assertEqual(run.returncode, 0, run.stdout[-3000:] + run.stderr[-3000:])
            for path in (SHAPE_PATH, FRAME_PATH, RIG_PATH):
                self.assertEqual((Path(d) / path.name).read_text(), path.read_text(),
                                 f"{path.name} is not what the generator writes from the current eidolon.json: regenerate it")


class Rig(unittest.TestCase):
    def test_parts_parse_and_requires_are_the_stages(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, set(BUILD) | {None})
        self.assertEqual(STAGE_CODES[0], "torso")
        self.assertEqual([p["id"] for p in RIG["parts"] if p["requires"] is None], ["frame"])
        for pid, want in (("ring", "chain"), ("spine", "spine")):
            self.assertEqual((part(pid)["requires"], part(pid)["ride"]), (want, "hook"), pid)
        staged = {n for st in STAGES["stages"] for n in st["elements"]}
        self.assertIn("spine-hook1", make_shape.spine_names())
        self.assertFalse(staged & make_shape.spine_names())
        for code in STAGE_CODES:
            self.assertEqual((part(code)["requires"], part(code)["match"], part(code)["ride"]), (code, [f"b_{code}_*"], "hook"))

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        self.assertEqual({rigmath.part_of(RIG["parts"], n) for n in names}, {p["id"] for p in RIG["parts"]})
        self.assertEqual({e["name"] for e in FRAME["elements"]}, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_textures_are_declared(self):
        used = {f["texture"].lstrip("#") for e in SHAPE["elements"] for f in e["faces"].values()}
        self.assertLessEqual(used, set(SHAPE["textures"]))
        for code, path in EIDOLON["textures"].items():
            self.assertEqual(SHAPE["textures"].get(code, path), path, code)

    def test_the_wood_is_a_variant(self):
        # the gantry's wood is two codes a blockType maps per wood; the shape maps them to oak
        self.assertEqual(SHAPE["textures"]["wood"], "game:block/wood/debarked/oak")
        self.assertEqual(SHAPE["textures"]["wood-end"], "game:block/wood/treetrunk/debarked/oak")
        self.assertNotIn("oak", SHAPE["textures"])
        wooden = ("fr_post", "fr_head", "fr_beam", "fr_hoist", "fr_sill", "fr_rail", "fr_knee", "fr_cheek",
                  "dr_drum", "ck_handle", "sv_hub", "sv_flange")
        # vanilla's wooden axles and spur gears are drawn as the game draws them, whatever the frame's wood
        self.assertEqual(SHAPE["textures"]["mechanics"], "game:block/wood/planks/generic")
        mechanics = ("ls_shaft", "dr_shaft", "ck_lantern_disc", "ck_lantern_stave", "ls_wheel", "ls_lantern_disc",
                     "ls_lantern_stave", "dr_wheel")
        for e in SHAPE["elements"]:
            if e["name"].startswith(wooden):
                self.assertLessEqual({f["texture"] for f in e["faces"].values()}, {"#wood", "#wood-end"}, e["name"])
            if e["name"].startswith(mechanics):
                self.assertEqual({f["texture"] for f in e["faces"].values()}, {"#mechanics"}, e["name"])
        self.assertTrue(any(f["texture"] == "#wood-end" for e in FRAME["elements"] for f in e["faces"].values()))
        for name in ("ck_ratchet_tooth1", "pw_pawl", "ck_lantern_hoop1_1", "ls_gudgeon1_1", "dr_collar2_1", "ck_shaft_1"):
            e = next(e for e in SHAPE["elements"] if e["name"] == name)
            self.assertEqual({f["texture"] for f in e["faces"].values()}, {"#iron"}, name)

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        self.assertEqual(make_shape.shipped_cells(SHAPE, RIG["parts"]), RIG["cells"])
        self.assertEqual(len(RIG["cells"]), make_shape.CELLS_X * make_shape.CELLS_Y * make_shape.CELLS_Z + 1)  # and the crank's
        self.assertIn("boxes", RIG["cells"][0])

    def test_anchors(self):
        self.assertEqual(RIG["exitSide"], "west")
        self.assertEqual((RIG["crankCell"], RIG["crankFace"]), ([5, 1, 5], "south"))
        self.assertNotIn("winchCell", RIG)
        self.assertEqual(RIG["body"]["pos"], [v / 16 for v in make_shape.BODY_AT])
        for key in ("hang", "fit", "exit"):
            self.assertEqual(len(RIG[key]["pos"]), 3)


class Body(unittest.TestCase):
    def test_the_written_body_is_the_hung_pose(self):
        # flattened as the game reads the shape, against kin's pose of the current eidolon.json at hung's frame
        rig = kin.Rig(make_shape.with_spine(EIDOLON))
        anim = next(a for a in EIDOLON["animations"] if a["code"] == "hung")
        mats = rig.all_matrices(kin.sample(anim, 0))
        written = {el.name: el for el in flatten(SHAPE["elements"], textures={}) if el.name.startswith(("b_", "sp_"))}
        self.assertTrue(any(n.startswith("sp_") for n in written))
        worst = 0.0
        for name, el in written.items():
            src = make_shape.source_name(name)
            want = [[p[k] + OFF[k] for k in range(3)] for p in rig.corners(src, mats[src])]
            worst = max(worst, max(min(math.dist(p, q) for q in el.corners()) for p in want))
        self.assertLess(worst, 0.01)

    def test_every_drawn_element_is_in_its_stage_once(self):
        stage = {n: s["code"] for s in STAGES["stages"] for n in s["elements"]}
        rig = kin.Rig(EIDOLON)
        want = {f"b_{stage[n]}_{n}" for n in rig.order if drawn(rig.elements[n])}
        have = {e["name"] for e in SHAPE["elements"] if e["name"].startswith("b_")}
        self.assertEqual(have, want)
        for name in have:
            self.assertEqual(rigmath.part_of(RIG["parts"], name), name.split("_", 2)[1])
        spine = {e["name"] for e in SHAPE["elements"] if e["name"].startswith("sp_")}
        self.assertEqual(spine, {f"sp_{n}" for n in make_shape.spine_names()})
        for name in spine:
            self.assertEqual(rigmath.part_of(RIG["parts"], name), "spine")

    def test_the_mind_glows(self):
        glowing = {e["name"] for e in SHAPE["elements"] if any(f.get("glow") for f in e["faces"].values())}
        self.assertTrue(glowing)
        self.assertTrue(any(n.startswith("b_mind_") for n in glowing))

    def test_let_down_brings_the_lowest_toe_from_three_voxels_to_the_floor(self):
        for depth, want in ((0.0, 3.0), (1.0, 0.0)):
            body = posed_written(depth, set(STAGE_CODES))
            low = min(min(c[1] for c in el.corners()) for el in body)
            self.assertAlmostEqual(low, want, delta=0.01)
        self.assertAlmostEqual(RIG["winch"]["drop"], 3.0 / 16, places=4)

    def test_the_hook_and_ring_come_down_with_the_body(self):
        for pid in ("hook", "ring", "spine", *STAGE_CODES):
            m = matrix(pid, 1.0)
            self.assertAlmostEqual(m[1][3], -3.0 / 16, places=4, msg=pid)


class Frame(unittest.TestCase):
    def test_the_front_is_open(self):
        # nothing of the gantry below the front beam between the front posts, across the front cell row
        z0, z1 = make_shape.Z_RIGHT[1], make_shape.Z_LEFT[0]
        for el in posed_written(0.0, {p["id"] for p in RIG["parts"] if p["ride"] != "hook"} - {"fall", "hook"}):
            lo, hi = el.aabb()
            if lo[0] < 16 and lo[2] < z1 - 0.01 and hi[2] > z0 + 0.01:
                self.assertGreaterEqual(lo[1], 64.0, el.name)

    def test_inside_the_machine_box_or_the_cranks_cell(self):
        regions = make_shape.regions()
        for depth in (0.0, 1.0):
            for el in posed_written(depth, {p["id"] for p in RIG["parts"]}):
                lo, hi = el.aabb()
                self.assertTrue(any(all(r[0][k] - 0.01 <= lo[k] and hi[k] <= r[1][k] + 0.01 for k in range(3)) for r in regions),
                                el.name)

    def test_the_crank_is_outside_the_frame_in_a_hollow_cell(self):
        outer = make_shape.CELLS_Z * 16
        els = {el.name: el for el in posed_written(0.0, {p["id"] for p in RIG["parts"]})}
        for name in ("ck_web", "ck_handle", "ck_ratchet_tooth1", "pw_pawl"):
            self.assertGreater(els[name].aabb()[0][2], outer, name)
        # the crank's cell is in the footprint, reserving its room, but has no collision or selection boxes
        cell = next(c for c in RIG["cells"] if c["pos"] == list(make_shape.CRANK_CELL))
        self.assertEqual(cell, {"pos": list(make_shape.CRANK_CELL), "hollow": True})


class Winch(unittest.TestCase):
    def amounts(self):
        names = {"crank": "crank", "layshaft": "layshaft", "drumshaft": "drum"}
        return {names[p["id"]]: next(d for d in p["drivers"] if d["type"] == "step")["amount"] for p in RIG["parts"]
                if p["id"] in names}

    def test_the_train_is_geared_by_its_tooth_counts(self):
        gearing = RIG["winch"]["gearing"]
        self.assertEqual(gearing["stages"], [[6, 30], [6, 30]])
        self.assertEqual(gearing["ratio"], 25)
        a = self.amounts()
        # meshing shafts turn opposite ways, by the tooth counts; the drum pays out the drop
        self.assertAlmostEqual(a["crank"] / a["layshaft"], -5.0, places=4)
        self.assertAlmostEqual(a["layshaft"] / a["drum"], -5.0, places=4)
        self.assertAlmostEqual(a["drum"] * RIG["winch"]["drumRadius"], RIG["winch"]["drop"], places=5)
        self.assertGreater(a["crank"] / (2 * math.pi), 2.5)          # the crank turns several times over the let-down
        for name, pid in (("ck_shaft_1", "crank"), ("ck_lantern_stave1_1", "cranklantern"), ("ck_handle", "crankarm"),
                          ("ck_ratchet_tooth1", "ratchet"), ("ls_wheel_cog1a", "laygears"), ("ls_lantern_stave1_1", "laygears"),
                          ("ls_gudgeon1_1", "layshaft"), ("ls_shaft1_1", "layshaft"), ("dr_wheel_cog1a", "drumwheel"),
                          ("dr_drum_1", "drum"), ("dr_gudgeon2_1", "drumshaft"), ("dr_hoop1_1", "drumstraps"), ("pw_pawl", "pawl"),
                          ("pm_pin_1", "pawlmount"), ("fr_iron_sheavepin_1", "frame")):
            self.assertEqual(rigmath.part_of(RIG["parts"], name), pid, name)
        # what is fitted on a shaft rides it, turning with it at any depth
        for shaft, riders in (("crank", ("cranklantern", "crankhoops", "ratchet", "crankarm")),
                              ("layshaft", ("laygears", "laystraps")), ("drumshaft", ("drumwheel", "drum", "drumstraps", "coil"))):
            for pid in riders:
                self.assertEqual((part(pid)["ride"], part(pid)["drivers"]), (shaft, []), pid)
                self.assertEqual(matrix(pid, 0.6), matrix(shaft, 0.6), pid)

    def test_meshing_centre_distances_are_the_pitch_radii(self):
        written = {el.name: el for el in posed_written(0.0, {"crank", "layshaft", "drumshaft"})}

        def centre(prefix):
            lo = [min(el.aabb()[0][k] for n, el in written.items() if n.startswith(prefix)) for k in range(2)]
            hi = [max(el.aabb()[1][k] for n, el in written.items() if n.startswith(prefix)) for k in range(2)]
            return [(lo[k] + hi[k]) / 2 for k in range(2)]
        names = {"crank": "crank", "layshaft": "layshaft", "drumshaft": "drum"}
        pivots = {names[p["id"]]: [v * 16 for v in p["drivers"][-1]["pivot"][:2]] for p in RIG["parts"] if p["id"] in names}
        for prefix, pid in (("ck_shaft", "crank"), ("ls_shaft", "layshaft"), ("dr_shaft", "drum")):
            for k in range(2):
                self.assertAlmostEqual(centre(prefix)[k], pivots[pid][k], delta=0.01)
        pitch = (1.0 * 6 / 2) + (1.0 * 30 / 2)      # a 6-stave lantern and a 30-cog wheel, module 1
        self.assertAlmostEqual(math.dist(pivots["crank"], pivots["layshaft"]), pitch, delta=1e-3)
        self.assertAlmostEqual(math.dist(pivots["layshaft"], pivots["drum"]), pitch, delta=1e-3)

    def test_the_pawl_is_thrown_off_as_the_let_down_starts(self):
        pawl = next(p for p in RIG["parts"] if p["id"] == "pawl")
        d = pawl["drivers"][0]
        self.assertEqual((d["type"], d["from"]), ("step", 0.0))
        self.assertLess(d["to"], 0.01)
        hung = matrix("pawl", 0.0)
        self.assertEqual([row[:3] for row in hung[:3]], [[1, 0, 0], [0, 1, 0], [0, 0, 1]])

class Build(unittest.TestCase):
    def test_the_winch_is_fitted_in_stages_after_the_frame(self):
        codes = [code for code, *_ in WINCH_STAGES]
        self.assertEqual(codes, ["axles", "crankshaft", "gears", "drum", "strapping", "ratchet", "crank", "chain"])
        fitted = {pid: code for code, _, _, pids in WINCH_STAGES for pid in pids}
        for p in RIG["parts"]:
            if p["id"] in fitted:
                self.assertEqual(p["requires"], fitted[p["id"]], p["id"])
        # the frame alone is the frame part and nothing else; each stage adds elements
        self.assertEqual({rigmath.part_of(RIG["parts"], e["name"]) for e in FRAME["elements"]}, {"frame"})
        for code in BUILD:
            self.assertTrue(any(rigmath.part_of(RIG["parts"], e["name"]) in {p["id"] for p in RIG["parts"] if p["requires"] == code}
                                for e in SHAPE["elements"]), code)

    def test_each_part_comes_after_what_it_is_fitted_onto(self):
        need = {"cranklantern": "crank", "laygears": "layshaft", "drumwheel": "drumshaft", "drum": "drumshaft",
                "ratchet": "crank", "crankarm": "ratchet", "coil": "drum", "lead": "sheave", "spine": "ring", "torso": "spine"}
        for pid, under in need.items():
            self.assertLessEqual(BUILD.index(part(under)["requires"]), BUILD.index(part(pid)["requires"]), pid)
        self.assertLess(BUILD.index("chain"), BUILD.index("spine"))
        self.assertLess(BUILD.index("spine"), BUILD.index(STAGE_CODES[0]))

    def test_the_stage_items_are_plain_existing_items(self):
        items = {code: (item, count) for code, item, count, _ in WINCH_STAGES}
        self.assertEqual(items["axles"], ("game:woodenaxle-ud", 8))         # one a block of the two wooden shafts
        self.assertEqual(items["gears"], ("game:spurgear-s", 4))            # two lanterns and two wheels
        self.assertEqual(items["chain"][0], "game:metalchain-{metal}")
        for code, (item, count) in items.items():
            self.assertTrue(item.startswith("game:") and count >= 1, code)

    def test_the_spine_takes_a_support_beam_a_length_of_its_mast(self):
        code, item, count, pids = make_shape.spine_stage()
        self.assertEqual((code, item, pids), ("spine", "game:supportbeam-{wood}", ("spine",)))
        mast = [e for e in SHAPE["elements"] if e["name"][3:] in make_shape.SPINE_TIMBERS]
        self.assertEqual(count, len(mast))
        self.assertEqual(count, 3)

    def test_the_spine_is_in_the_gantrys_wood(self):
        spine = {e["name"][3:]: {f["texture"] for f in e["faces"].values()} for e in SHAPE["elements"] if e["name"].startswith("sp_")}
        for name in make_shape.SPINE_WOODEN:
            self.assertLessEqual(spine[name], {"#wood", "#wood-end"}, name)
        for name in make_shape.SPINE_TIMBERS:
            self.assertEqual(spine[name], {"#wood", "#wood-end"}, name)       # side grain and its two ends
        # the four hooks are iron; the pulley and its handle keep vanilla's charred look; the clamps, staples and ropes theirs
        for name in make_shape.SPINE_IRON:
            self.assertEqual(spine[name], {"#iron"}, name)
        for name in ("pulley-capL", "winch-handle1"):
            self.assertEqual(spine[name], {"#charred"}, name)
        self.assertEqual(spine["bar-spine1"], {"#steel"})
        self.assertEqual(spine["spine-rope1"], {"#reedrope"})
        self.assertEqual(spine["spine-staple1"], {"#rusty-iron"})

    def test_the_readme_holds_the_generators_stages(self):
        rows = re.findall(r"^\| (\d+) \| `(\w+)` \| (\d+) × `([^`]+)`", README, re.M)
        stages = [*WINCH_STAGES, make_shape.spine_stage()]
        self.assertEqual([(code, int(count), item) for _, code, count, item in rows if code in {c for c, *_ in stages}],
                         [(code, count, item) for code, item, count, _ in stages])
        beams = len(make_shape.frame_timbers([make_shape.El(e["name"], [1, 1, 1], [0, 0, 0], [[1, 0, 0], [0, 1, 0], [0, 0, 1]],
                                                              e["faces"], "frame") for e in FRAME["elements"]]))
        self.assertEqual(beams, 24)
        self.assertIn(f"| {beams} × `game:supportbeam-{{wood}}`", README)


if __name__ == "__main__":
    unittest.main()
