"""The handcar's generated model files (mods-src/seraphhorizons/Handcar/tools/make_shape.py).

The generator solves the riders' arms against the game's seraph, so CI does not run it whole; these
tests hold what it wrote without the game: the shipped rig parses with the shared rig maths and its
reference poses are its own maths, the car's shape is what the generator builds today, its pump
animation follows the rig, the linkage stays on its pins, the riders' patch and hand targets name the
rig's animations and follow its beam, and Yang's bogie offsets match the entity type. Run with
`python3 -m unittest discover -s tools/tests`.
"""

import contextlib
import importlib.util
import io
import json
import math
import re
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
TOOLS = MOD / "Handcar" / "tools"
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(TOOLS))

from machinegen import rigmath  # noqa: E402

_spec = importlib.util.spec_from_file_location("handcar_make_shape", TOOLS / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)
import validate_handcar  # noqa: E402

ASSETS = MOD / "assets" / "seraphhorizons"
RIG = json.loads((ASSETS / "config" / "handcar-rig.json").read_text())
SHAPE_TEXT = (ASSETS / "shapes" / "entity" / "handcar.json").read_text()
SHAPE = json.loads(SHAPE_TEXT)
AXLEBOX_TEXT = (ASSETS / "shapes" / "entity" / "handcar-axlebox.json").read_text()
PATCH = json.loads((ASSETS / "patches" / "handcar-riders.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "Handcar" / "rig-reference.json").read_text())
RIDERS = json.loads((MOD / "tests" / "Handcar" / "rider-reference.json").read_text())


def lenient(text):
    """Yang-style entity JSON: comments and trailing commas."""
    text = re.sub(r"(?m)^\s*//.*$", "", text)
    text = re.sub(r",(\s*[}\]])", r"\1", text)
    return json.loads(text)


ENTITY = lenient((ASSETS / "entities" / "handcar.json").read_text())


def quiet(fn, *args):
    with contextlib.redirect_stdout(io.StringIO()):
        return fn(*args)


def flatten(elements):
    for e in elements:
        yield e
        yield from flatten(e.get("children", []))


def built():
    els = make_shape.build_all()
    parts = make_shape.rig_parts()
    quiet(validate_handcar.fix, els, parts, make_shape)
    return els, parts


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            self.assertIn(p["requires"], {"left", "straight", "right", None}, p["id"])
            for d in p["drivers"]:
                rigmath.validate_driver(d)
                self.assertIn(d.get("input", "theta"), {"theta"}, p["id"])

    def test_the_rig_is_the_generators(self):
        self.assertEqual(RIG["parts"], json.loads(json.dumps(make_shape.rig_parts())))
        self.assertEqual(RIG["cycle"], make_shape.cycle_info())

    def test_globs_tell_the_parts_apart(self):
        parts = RIG["parts"]
        self.assertEqual(rigmath.part_of(parts, "pm_rod"), "pitman")
        self.assertEqual(rigmath.part_of(parts, "bm_handle_front"), "beam")
        self.assertEqual(rigmath.part_of(parts, "TNL_LFT_knob"), "lever_left")
        self.assertEqual(rigmath.part_of(parts, "TNL_RGT"), "lever_right")
        self.assertEqual(rigmath.part_of(parts, "af_tyre_l"), "axle_front")
        self.assertEqual(rigmath.part_of(parts, "fr_pivotpin"), "frame")

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in flatten(SHAPE["elements"])]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreaterEqual(len(poses), 20)
        for pose in poses:
            for pid, want in pose["matrices"].items():
                got = rigmath.part_matrix(RIG["parts"], pid, {"theta": pose["theta"]})
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {pose['theta']}")

    def test_a_cycle_is_three_turns_of_the_wheel(self):
        cyc = RIG["cycle"]
        self.assertEqual(cyc["axleTurns"], 3.0)
        self.assertAlmostEqual(cyc["distancePerCycle"], 2 * math.pi * cyc["axleTurns"] * cyc["wheelRadius"], places=5)
        # after a cycle every part is back where it started
        for p in RIG["parts"]:
            a = rigmath.part_matrix(RIG["parts"], p["id"], {"theta": 0.3})
            b = rigmath.part_matrix(RIG["parts"], p["id"], {"theta": 0.3 + 2 * math.pi * cyc["axleTurns"]})
            for r in range(3):
                for c in range(4):
                    self.assertAlmostEqual(a[r][c], b[r][c], places=6, msg=p["id"])


class Mechanism(unittest.TestCase):
    def test_the_pitman_stays_on_its_pins_and_the_gears_mesh(self):
        parts = RIG["parts"]
        self.assertEqual(quiet(validate_handcar.check_linkage, parts, make_shape), [])
        self.assertEqual(quiet(validate_handcar.check_gearing, parts, make_shape), [])

    def test_the_shape_is_what_the_generator_builds(self):
        els, parts = built()
        self.assertEqual(quiet(validate_handcar.check_parts, els, parts), [])
        self.assertEqual(quiet(validate_handcar.check_textures, els, make_shape), [])
        self.assertEqual(quiet(validate_handcar.check_frame, els, parts, make_shape), [])
        self.assertEqual(make_shape.body_shape(els, parts), SHAPE_TEXT)
        self.assertEqual(make_shape.axlebox_shape(), AXLEBOX_TEXT)

    def test_the_pump_animation_follows_the_rig(self):
        els, parts = built()
        self.assertEqual(quiet(validate_handcar.check_body_animation, els, parts, SHAPE, make_shape), [])

    def test_only_vanilla_textures(self):
        for code in SHAPE["textures"].values():
            self.assertTrue(code.startswith("game:"), code)


class Riders(unittest.TestCase):
    def test_the_patch_adds_each_seats_two_animations_and_their_metadata(self):
        shape_ops = [op for op in PATCH if op["file"].endswith("seraph-faceless.json")]
        meta_ops = [op for op in PATCH if op["file"].endswith("player.json")]
        codes = {c for seat in RIG["riders"].values() for c in (seat["grip"], seat["pump"])}
        self.assertEqual({op["value"]["code"] for op in shape_ops}, codes)
        self.assertEqual({op["value"]["code"] for op in meta_ops}, codes)
        for op in PATCH:
            self.assertEqual(op["dependsOn"], [{"modid": "yangtransport"}])
        for op in shape_ops:
            self.assertEqual(op["value"]["quantityframes"], RIG["cycle"]["frames"])
            self.assertNotIn("side", op)            # the client draws it: both sides patch the shape
        for op in meta_ops:
            self.assertEqual(op["side"], "Server")   # entity types reach the client from the server
            meta = op["value"]
            self.assertLess(meta["animationSpeed"], 0.001)   # frames are set by distance, not by time
            self.assertEqual(meta["blendMode"], "Average")
            self.assertEqual(meta.get("clientSide", False), meta["code"].startswith("seraphhorizons-handcar-pump"))

    def test_the_hand_targets_follow_the_beams_handles(self):
        self.assertEqual(RIDERS["frames"], RIG["cycle"]["frames"])
        self.assertEqual(RIDERS["animations"], RIG["riders"])
        parts = RIG["parts"]
        for pose in RIDERS["poses"]:
            want = make_shape.grip_targets(parts, pose["seat"], pose["frame"])
            for side, key in (("R", "right"), ("L", "left")):
                for got, w in zip(pose[key], want[side]):
                    self.assertAlmostEqual(got, w, places=3, msg=f"{pose['seat']} {key} at frame {pose['frame']}")

    def test_the_seats_face_each_other_across_the_beam(self):
        front, rear = RIG["seatFront"], RIG["seatRear"]
        self.assertEqual((front["turn"], rear["turn"]), (180.0, 0.0))
        pivot_x = make_shape.PIVOT[0] / 16
        self.assertLess(front["pos"][0], pivot_x)
        self.assertGreater(rear["pos"][0], pivot_x)
        for key in ("Front", "Rear"):
            for side in "RL":
                self.assertEqual(RIG[f"grip{key}{side}"]["part"], "beam")


class Entity(unittest.TestCase):
    def test_yangs_bogies_and_seats_are_the_rigs(self):
        render = ENTITY["attributes"]["SGLocomotive"]["Render"]
        bogies = RIG["bogies"]
        self.assertAlmostEqual(render["BodyOffsetForward"], bogies["bodyOffsetForward"], places=6)
        self.assertEqual([b["OffsetForward"] for b in render["Bogies"]], [bogies["front"], bogies["rear"]])
        self.assertEqual({b["Shape"] for b in render["Bogies"]}, {bogies["shape"]})
        for side in ("client", "server"):
            seatable = next(b for b in ENTITY[side]["behaviors"] if b["code"] == "seatable")
            for seat in seatable["seats"]:
                self.assertEqual(seat["animation"], RIG["riders"][seat["seatId"]]["grip"])
                ap = seat["apName"]
                self.assertTrue(any(a["code"] == ap for e in flatten(SHAPE["elements"]) for a in e.get("attachmentpoints", [])), ap)


if __name__ == "__main__":
    unittest.main()
