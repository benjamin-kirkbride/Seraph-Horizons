"""seraphhorizons' soldered copper and lead pipes (UnifiedPipes).

Pipes and Power Expanded (ppex) draws every pipe with an iron band and bolts at its ends; copper and
lead pipe is soldered, so `mods-src/seraphhorizons/Pipes/tools/make_pipe_shapes.py` writes this mod's
own straight, bend, T- and X-junction (`shapes/block/pipes/soldered-*.json`, built by
`solderedpipe.py`: ppex's cross-section, no band or bolts, half a wiped joint of solder at each open
end) and `patches/unifiedpipes-solderedjoints.json`, which puts ppex's copper and lead pipes on them.

These hold the shipped shapes to the generator (no zip needed) and the patch to ppex's zip and to the
C# tests' fixture (needs `python3 tools/packtool.py fetch`; skips without ppex's zip). The game's own
shapes are checked in Atlas (`UnifiedPipes_copper_and_lead_pipes_are_soldered_...`); the rosser's drip
pipes, built from the same module, in test_rosser_model.py.
"""

from __future__ import annotations

import json
import os
import sys
import unittest
from pathlib import Path

from test_gear_consumers import MODS_DIR, PATCHES, loads

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
sys.path.insert(0, str(MOD / "Pipes" / "tools"))
sys.path.insert(0, str(MOD / "Machines" / "tools"))

import make_pipe_shapes  # noqa: E402
import solderedpipe  # noqa: E402

SHAPES = MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "pipes"
PATCH = PATCHES / "unifiedpipes-solderedjoints.json"
FIXTURE = MOD / "tests" / "Pipes" / "fixtures" / "ppex-pipe-shapes.json"
KINDS = ("straight", "bend", "tjunction", "xjunction")


def shipped(kind):
    return json.loads((SHAPES / f"soldered-{kind}.json").read_text())


class SolderedShapes(unittest.TestCase):
    def test_the_shipped_shapes_are_the_generators(self):
        from machinegen.output import shape_dumps
        for kind in KINDS:
            with self.subTest(kind=kind):
                self.assertEqual(shape_dumps(make_pipe_shapes.shape(kind)), (SHAPES / f"soldered-{kind}.json").read_text())

    def test_no_band_or_bolts_only_the_pipe_and_its_solder(self):
        for kind in KINDS:
            shape = shipped(kind)
            with self.subTest(kind=kind):
                self.assertEqual({"iron4": "game:block/metal/sheet-plain/copper4", "solder": "game:block/metal/ingot/leadsolder"},
                                 shape["textures"])
                used = {f["texture"] for e in shape["elements"] for f in e["faces"].values()}
                self.assertEqual({"#iron4", "#solder"}, used)
                # every box in the block, axis-aligned
                for e in shape["elements"]:
                    self.assertFalse({"rotationX", "rotationY", "rotationZ", "children"} & set(e), e["name"])
                    self.assertTrue(all(0 <= v <= 16 for v in e["from"] + e["to"]), e["name"])

    def test_the_solder_texture_is_the_games(self):
        install = os.environ.get("VINTAGE_STORY")
        if not install or not (Path(install) / "assets" / "survival").is_dir():
            self.skipTest("VINTAGE_STORY is not set to a game or server install")
        path = solderedpipe.SOLDER_TEXTURE.removeprefix("game:")
        self.assertTrue((Path(install) / "assets" / "survival" / "textures" / f"{path}.png").is_file())

    def test_a_joint_at_every_open_end_and_the_tube_round_an_open_bore(self):
        for kind, arms in make_pipe_shapes.PIPES.items():
            boxes = make_pipe_shapes.pipe_boxes(kind)
            with self.subTest(kind=kind):
                self.assertEqual([], solderedpipe.check(boxes))
                for arm in arms:
                    k, sgn = solderedpipe.FACE_NORMAL[arm]
                    face = 0.0 if sgn < 0 else 16.0
                    rings = [b for b in boxes if b.name.startswith(f"joint_{arm}")]
                    self.assertEqual(4 * len(solderedpipe.JOINT), len(rings), arm)
                    # the fattest ring reaches the block face, the joint runs JOINT_LENGTH into the block
                    at_face = [b for b in rings if (b.lo[k] if sgn < 0 else b.hi[k]) == face]
                    self.assertEqual(4, len(at_face), arm)
                    reach = max(abs(face - (b.hi[k] if sgn < 0 else b.lo[k])) for b in rings)
                    self.assertAlmostEqual(solderedpipe.JOINT_LENGTH, reach, places=6)
                    across = [b.hi[a] - b.lo[a] for b in at_face for a in range(3) if a != k]
                    self.assertAlmostEqual(2 * (solderedpipe.HALF + solderedpipe.JOINT[0][1]), max(across), places=6)

    def test_each_face_maps_its_texture_one_texel_a_voxel(self):
        for kind in KINDS:
            for e in shipped(kind)["elements"]:
                size = [e["to"][k] - e["from"][k] for k in range(3)]
                for d, f in e["faces"].items():
                    self.assertEqual(solderedpipe.uv(d, size), f["uv"], f"{kind} {e['name']} {d}")


class SolderedJointsPatchFitsPpex(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        try:
            cls.blocktypes, cls.shapes, _ = make_pipe_shapes.load_ppex(MODS_DIR)
        except SystemExit:
            raise unittest.SkipTest("ppex is not fetched: run `python3 tools/packtool.py fetch`")

    def test_the_patch_is_the_generators_from_ppex(self):
        self.assertEqual(make_pipe_shapes.patch_dumps(make_pipe_shapes.patch_ops(self.blocktypes)), PATCH.read_text())

    def test_the_generator_checks_pass(self):
        self.assertEqual([], make_pipe_shapes.check(self.blocktypes, self.shapes))

    def test_iron_and_steel_keep_ppex_entries_and_copper_and_lead_match_first(self):
        import fnmatch
        for op in loads(PATCH.read_text()):
            kind = Path(op["file"]).stem
            table = op["value"]
            states = next(g for g in self.blocktypes[kind]["variantgroups"] if g["code"] == "orientation")["states"]
            for orientation in states:
                for metal in ("copper", "lead", "iron", "steel"):
                    code = f"pipe-{kind}-{orientation}-{metal}"
                    key = next(k for k in table if fnmatch.fnmatchcase(code, k))
                    want = f"seraphhorizons:block/pipes/soldered-{kind}" if metal in ("copper", "lead") else f"ppex:pipes/{kind}"
                    self.assertEqual(want, table[key]["base"], code)
                    ppex_key = next(k for k in self.blocktypes[kind]["shapebytype"] if fnmatch.fnmatchcase(code, k))
                    rot = {a: v for a, v in table[key].items() if a != "base"}
                    self.assertEqual({a: v for a, v in self.blocktypes[kind]["shapebytype"][ppex_key].items() if a != "base"}, rot, code)
            for value in table.values():
                base = value["base"]
                if base.startswith("seraphhorizons:"):
                    self.assertTrue((MOD / "assets" / "seraphhorizons" / "shapes" / (base.split(":", 1)[1] + ".json")).is_file(), base)

    def test_the_csharp_fixture_is_ppex(self):
        fixture = json.loads(FIXTURE.read_text())
        for kind in KINDS:
            self.assertEqual(self.blocktypes[kind]["shapebytype"], fixture[f"blocktypes/pipes/{kind}.json"]["shapebytype"], kind)
            self.assertEqual(list(self.blocktypes[kind]["shapebytype"]), list(fixture[f"blocktypes/pipes/{kind}.json"]["shapebytype"]), kind)

    def test_ppex_draws_a_band_the_soldered_shapes_replace(self):
        # the reason for all this: ppex's own pipe has its band and bolts in corroded iron at each end
        for kind in KINDS:
            self.assertTrue(make_pipe_shapes.ppex_band(self.shapes[kind]), kind)


if __name__ == "__main__":
    unittest.main()
