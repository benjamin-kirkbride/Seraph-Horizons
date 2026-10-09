"""The eidolon's generated shape (mods-src/seraphhorizons/Eidolon/tools/make_shape.py).

The committed files are checked as they are: strict JSON, every animation naming only elements that
exist and moving only the joints the vanilla animations move (plus the three anchors), the textures
the game's own, the attachment points in place, the build stages covering every element exactly once
with the body one piece after every stage, and the poses doing what they are for (feet on the ground, hands on their grips,
objects on the ground staying put). With a game install ($VINTAGE_STORY) the generator is run too
and its output must equal the committed files. Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
TOOLS = MOD / "Eidolon" / "tools"
sys.path.insert(0, str(TOOLS))

_spec = importlib.util.spec_from_file_location("eidolon_make_shape", TOOLS / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)
kin = make_shape.kin


def strict_load(path):
    def no_constants(name):
        raise ValueError(f"{path}: {name} is not JSON")

    def no_duplicates(pairs):
        keys = [k for k, _ in pairs]
        if len(keys) != len(set(keys)):
            raise ValueError(f"{path}: duplicate key in {keys}")
        return dict(pairs)

    return json.loads(Path(path).read_text(encoding="utf-8"), parse_constant=no_constants,
                      object_pairs_hook=no_duplicates)


SHAPE = strict_load(make_shape.SHAPE_OUT)
STAGES = strict_load(make_shape.STAGES_OUT)
RIG = kin.Rig(SHAPE)
ANIMS = {a["code"]: a for a in SHAPE["animations"]}
AUTHORED = {
    # code: (quantityframes, onAnimationEnd)
    "fell": (40, "Repeat"), "carry-idle": (60, "Repeat"), "carry-walk": (30, "Repeat"),
    "lift": (45, "Hold"), "setdown": (45, "Stop"),
    "trunk-carry-idle": (60, "Repeat"), "trunk-carry-walk": (30, "Repeat"),
    "trunk-pickup": (60, "Hold"), "trunk-setdown": (50, "Stop"),
    "trunk-thick-carry-idle": (60, "Repeat"), "trunk-thick-carry-walk": (30, "Repeat"),
    "trunk-thick-pickup": (60, "Hold"), "trunk-thick-setdown": (50, "Stop"),
    "guard-idle": (80, "Repeat"), "hung": (1, "Hold"), "activate": (90, "Stop"),
    "slump": (50, "Hold"), "standup": (60, "Stop"),
}
KEY_FIELDS = {n for names in kin.GROUPS.values() for n in names}


class Files(unittest.TestCase):
    def test_strict_json_written_as_the_generator_writes(self):
        # parsed strictly above; and written exactly as render() writes it
        self.assertEqual(make_shape.SHAPE_OUT.read_text(encoding="utf-8"), make_shape.render(SHAPE))
        self.assertEqual(make_shape.STAGES_OUT.read_text(encoding="utf-8"), make_shape.render(STAGES))

    def test_regenerates_unchanged(self):
        path = make_shape.vanilla_path(None)
        if path is None:
            self.skipTest("no game install ($VINTAGE_STORY)")
        shape = make_shape.build(make_shape.load_vanilla(path))
        self.assertEqual(make_shape.render(shape), make_shape.SHAPE_OUT.read_text(encoding="utf-8"),
                         "eidolon.json is stale: run Eidolon/tools/make_shape.py")
        self.assertEqual(make_shape.render(make_shape.stages_file(shape)),
                         make_shape.STAGES_OUT.read_text(encoding="utf-8"), "eidolon-stages.json is stale")


class Shape(unittest.TestCase):
    def test_element_names_are_unique(self):
        self.assertEqual(len(RIG.order), len(set(RIG.order)))
        self.assertEqual(len(RIG.order), 221)

    def test_textures_are_the_games(self):
        self.assertEqual(set(SHAPE["textures"]), set(make_shape.VANILLA_TEXTURES))
        for code, path in SHAPE["textures"].items():
            self.assertTrue(path.startswith("game:"), code)
        for n in RIG.order:
            for face, f in RIG.elements[n].get("faces", {}).items():
                if f.get("enabled", True):
                    self.assertIn(f["texture"].lstrip("#"), SHAPE["textures"], f"{n} {face}")

    def test_attachment_points(self):
        where = {a["code"]: n for n in RIG.order for a in RIG.elements[n].get("attachmentpoints", [])}
        self.assertEqual(where["RightHand"], "wristR")
        self.assertEqual(where["LeftHand"], "wristL")
        self.assertEqual(where["Carry"], "carry-anchor")
        self.assertEqual(where["Trunk"], "trunk-anchor")
        self.assertEqual(where["ThickTrunk"], "thick-trunk-anchor")
        self.assertEqual(where["ObjectR"], "wristR")  # vanilla's, kept
        for anchor in make_shape.ANCHORS:
            self.assertEqual(RIG.parent[anchor], "chest-inside")


class Animations(unittest.TestCase):
    def test_kept_dropped_and_authored(self):
        codes = [a["code"] for a in SHAPE["animations"]]
        self.assertEqual(len(codes), len(set(codes)))
        self.assertEqual(set(codes), set(make_shape.KEEP) | set(AUTHORED))
        self.assertFalse(set(codes) & set(make_shape.DROP))
        for code, (frames, end) in AUTHORED.items():
            a = ANIMS[code]
            self.assertEqual((a["quantityframes"], a["onAnimationEnd"]), (frames, end), code)

    def test_every_animation_names_only_existing_elements_and_fields(self):
        for a in SHAPE["animations"]:
            frames = [k["frame"] for k in a["keyframes"]]
            self.assertEqual(frames, sorted(set(frames)), a["code"])
            self.assertLess(frames[-1], a["quantityframes"], a["code"])
            for k in a["keyframes"]:
                for n, entry in k["elements"].items():
                    self.assertIn(n, RIG.elements, f"{a['code']} frame {k['frame']}")
                    self.assertLessEqual(set(entry), KEY_FIELDS, f"{a['code']} {n}")
                    for group in kin.GROUPS.values():  # the game reads a group's three values together
                        present = [g in entry for g in group]
                        self.assertIn(present, ([True] * 3, [False] * 3), f"{a['code']} {n}")

    def test_joints_stay_vanillas_plus_the_anchors(self):
        moved = {n for a in SHAPE["animations"] for k in a["keyframes"] for n in k["elements"]}
        self.assertLessEqual(moved, make_shape.VANILLA_JOINTS | set(make_shape.ANCHORS))

    def test_one_shots_end_on_their_last_frame(self):
        for code, (frames, end) in AUTHORED.items():
            if end in ("Hold", "Stop"):
                self.assertEqual(ANIMS[code]["keyframes"][-1]["frame"], frames - 1, code)

    def test_one_shots_meet_the_poses_they_lead_to(self):
        def same(a, fa, b, fb):
            pa, pb = kin.sample(ANIMS[a], fa), kin.sample(ANIMS[b], fb)
            for n in set(pa) | set(pb):
                for g in ("rot", "off"):
                    va = pa.get(n, {}).get(g, (0, 0, 0))
                    vb = pb.get(n, {}).get(g, (0, 0, 0))
                    for x, y in zip(va, vb):
                        self.assertAlmostEqual(x, y, delta=0.01, msg=f"{a}@{fa} vs {b}@{fb}: {n} {g}")

        same("lift", 44, "carry-idle", 0)
        same("setdown", 0, "carry-idle", 0)
        same("trunk-pickup", 59, "trunk-carry-idle", 0)
        same("trunk-setdown", 0, "trunk-carry-idle", 0)
        same("trunk-thick-pickup", 59, "trunk-thick-carry-idle", 0)
        same("trunk-thick-setdown", 0, "trunk-thick-carry-idle", 0)
        same("slump", 49, "standup", 0)
        same("hung", 0, "activate", 0)

    def test_ends_at_rest(self):
        for code, frame in (("setdown", 44), ("trunk-setdown", 49), ("trunk-thick-setdown", 49),
                            ("standup", 59), ("activate", 89)):
            pose = kin.sample(ANIMS[code], frame)
            for n, entry in pose.items():
                if n in make_shape.ANCHORS:
                    continue  # the released object stays where it was set down
                for g, vals in entry.items():
                    for v in vals:
                        self.assertAlmostEqual(v, 0.0, delta=0.01, msg=f"{code}: {n} {g}")


class Poses(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.report = make_shape.report(SHAPE)

    def test_feet(self):
        r = self.report
        for code in ("carry-walk", "trunk-carry-walk", "trunk-thick-carry-walk"):
            lo, hi = r[code + ": lowest sole y, min..max over frames"]
            self.assertGreater(lo, -0.5, code)
            self.assertLess(hi, 0.5, code)
        for code in ("fell", "guard-idle", "lift", "setdown", "trunk-pickup", "trunk-setdown", "trunk-thick-pickup",
                     "trunk-thick-setdown", "trunk-thick-carry-idle", "standup", "activate"):
            self.assertLess(r[code + ": worst sole height at planted keys"], 0.05, code)
        self.assertAlmostEqual(r["hung: lowest sole y"], make_shape.HUNG_CLEAR, delta=0.05)
        self.assertAlmostEqual(r["slump (held frame): lowest leg point y"], 0.0, delta=0.05)

    def test_hands_on_their_grips(self):
        for k, v in self.report.items():
            if "grip" in k:
                self.assertLess(v, 0.5, k)

    def test_objects_on_the_ground_stay_put(self):
        for k, v in self.report.items():
            if "drift" in k:
                self.assertLess(v, 0.5, k)

    def test_fell(self):
        r = self.report
        for side, want in (("right", make_shape.FELL_IMPACT_R), ("left", make_shape.FELL_IMPACT_L)):
            got = r[f"fell: {side} hand at impact (x, y, z)"]
            self.assertLess(kin.dist(got, want), 0.5, side)
        _, hi = r["fell: distance between the hands, min..max over frames"]
        self.assertLess(hi, 10.0)  # both on the handle all through the swing
        self.assertGreater(r["fell: right hand height at wind-up"], 58.0)  # above the head

    def test_trunk_clears_the_head(self):
        self.assertLessEqual(self.report["trunk-carry: deepest head/hood corner inside a thin trunk (voxels)"], 0.0)

    def test_thick_trunk(self):
        r = self.report
        self.assertLessEqual(r["trunk-thick: deepest body point inside the thick trunk, every frame (voxels)"], 0.0)
        self.assertLess(r["trunk-thick: deepest arm point (not the hands) inside the thick trunk, every frame (voxels)"],
                        0.25)
        self.assertGreater(r["trunk-thick: nearest head or hood point to the thick trunk, every frame (voxels)"], 1.0)
        for code, event in (("trunk-thick-pickup", "grab"), ("trunk-thick-setdown", "release")):
            frame = make_shape.EVENTS[code][event]
            self.assertLess(frame, ANIMS[code]["quantityframes"], code)
            self.assertAlmostEqual(r[f"{code}: thick trunk underside height at the {event}"], 0.0, delta=0.5)


class Stages(unittest.TestCase):
    def test_every_element_in_exactly_one_stage_one_piece_after_each(self):
        self.assertEqual(make_shape.check_stages(SHAPE, STAGES), [])
        counted = [n for st in STAGES["stages"] for n in st["elements"]]
        self.assertEqual(sorted(counted), sorted(RIG.order))

    def test_the_check_catches_a_floating_stage(self):
        # the torso's plates without the chest block they are on: two pieces after the torso
        bad = json.loads(json.dumps(STAGES))
        bad["stages"][1]["elements"].remove("chest-inside")
        bad["stages"][2]["elements"].append("chest-inside")
        self.assertTrue(any("pieces" in p for p in make_shape.check_stages(SHAPE, bad)))

    def test_the_torso_comes_first_with_the_spines_peg(self):
        torso = set(STAGES["stages"][1]["elements"])
        self.assertLessEqual({"chest-inside", "spine1", "spine-hook1"}, torso)
        # before its parent: the chest block hangs off the hip block in the shape's hierarchy
        self.assertEqual(RIG.parent["chest-inside"], "hip-inside")
        self.assertIn("hip-inside", STAGES["stages"][2]["elements"])

    def test_stages_are_the_proposal(self):
        self.assertEqual([s["code"] for s in STAGES["stages"]],
                         ["gantry", "torso", "pelvis", "legs", "arms", "head", "mind"])
        self.assertEqual(STAGES["stages"][0]["elements"], [])
        mind = set(STAGES["stages"][-1]["elements"])
        self.assertEqual(mind, {"brain", "bar-head1", "heart", "bar-heart1", "bar-heart2", "Eye-out"})

    def test_the_stage_file_is_the_stage_map(self):
        self.assertEqual(STAGES, make_shape.stages_file(SHAPE))


if __name__ == "__main__":
    unittest.main()
