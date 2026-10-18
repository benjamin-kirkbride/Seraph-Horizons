"""ci/paths.py: the green-tree memo's no-op paths and the models-only fast path's allow-list.

- ci/code-tree.sh hashes the tree with the same no-op pattern the classifier skips, so the two
  can't drift.
- The allow-list's patterns match what a machine model PR changes and nothing else.
- A shipped machine's rig or shape (the bucking sawmill's buckingmill-rig.json, ...) is never
  models-only, and the reference check that decides it finds every way the mod names a machine's
  assets. A rig or shape the fast path let through would ship untested by smoke and Atlas.

Run with `python3 -m unittest discover -s tools/tests -p test_ci_paths.py`.
"""

import importlib.util
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
_spec = importlib.util.spec_from_file_location("ci_paths", ROOT / "ci" / "paths.py")
paths = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(paths)

MOD = "mods-src/seraphhorizons/"
ASSETS = MOD + "assets/seraphhorizons/"
HAVE_GIT = shutil.which("git") is not None and (ROOT / ".git").exists()


def git(*args, cwd=ROOT):
    return subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True, check=True).stdout


class Matching(unittest.TestCase):
    def test_noop_paths(self):
        for p in ["docs/merge-queue.md", "README.md", "mods-src/seraphhorizons/AmalgamPan/README.md",
                  ".claude/settings.json", ".mergify.yml", ".gitignore", "LICENSE",
                  "mods-src/seraphhorizons/benchmarks/Program.cs"]:
            self.assertTrue(paths.is_noop(p), p)
        for p in ["ci/paths.py", "ci/code-tree.sh", ".github/workflows/ci.yml", "docs.txt",
                  "mods-src/seraphhorizons/LICENSE.txt", "site/models.json"]:
            self.assertFalse(paths.is_noop(p), p)

    def test_model_sources(self):
        for p in [MOD + "AmalgamPan/tools/make_shape.py",
                  MOD + "AmalgamPan/tools/validate_amalgampan.py",
                  MOD + "Machines/tools/machinegen/rig.py",
                  MOD + "tests/AmalgamPan/AmalgamPanRigTests.cs",
                  MOD + "tests/AmalgamPan/rig-reference.json",
                  MOD + "tests/SeraphHorizons.Tests.csproj",
                  "tools/tests/test_amalgampan_model.py",
                  "site/models.json", "site/src/lib/model-view.ts", "site/test/amalgampan.test.ts"]:
            self.assertTrue(paths.is_model_source(p), p)
        for p in [MOD + "AmalgamPan/Core/AmalgamPanRig.cs",      # compiled into the mod
                  MOD + "AmalgamPan/tools/helper.cs",             # .cs outside tests/ is compiled
                  MOD + "assets/tools/x.py",                      # under assets/: ships
                  MOD + "tests/HideTests.cs",                     # not a feature's folder
                  MOD + "SeraphHorizons.csproj", MOD + "modinfo.json", MOD + "SeraphHorizons.cs",
                  "tools/tests/test_mods_src.py", "tools/packtool.py",
                  "site/src/lib/rig.ts", "site/src/components/ModelPage.svelte", "site/e2e/models.spec.ts",
                  "tests/PackTests/WoodworkingScenarios.cs", "tools/recipe-export/Exporter.cs"]:
            self.assertFalse(paths.is_model_source(p), p)

    def test_model_assets(self):
        cases = {
            ASSETS + "shapes/block/amalgampan.json": "amalgampan",
            ASSETS + "shapes/block/amalgampan_frame.json": "amalgampan",
            ASSETS + "shapes/block/rocker_item.json": "rocker",
            ASSETS + "config/amalgampan-rig.json": "amalgampan",
            ASSETS + "config/item-values.json": None,
            ASSETS + "config/ore-processing.json": None,
            ASSETS + "shapes/block/pipes/soldered-copper.json": None,
            ASSETS + "shapes/item/amalgampan.json": None,
            ASSETS + "textures/block/liquid/mercury.png": None,
            ASSETS + "blocktypes/amalgampan/frame.json": None,
            ASSETS + "patches/amalgampan.json": None,
            "mods-src/allowedvariantsfix/assets/allowedvariantsfix/config/x-rig.json": None,
        }
        for p, want in cases.items():
            self.assertEqual(paths.model_asset_id(p), want, p)

    def test_classify_without_assets_needs_no_git(self):
        self.assertEqual(paths.classify([], "HEAD")[0], False)
        self.assertEqual(paths.classify(["docs/x.md", MOD + "AmalgamPan/tools/make_shape.py"], "HEAD")[0], True)
        ok, reasons = paths.classify(["site/models.json", MOD + "SeraphHorizons.cs"], "HEAD")
        self.assertFalse(ok)
        self.assertEqual(len(reasons), 1)
        self.assertIn("SeraphHorizons.cs", reasons[0])


@unittest.skipUnless(HAVE_GIT, "needs git and a checkout")
class CodeTree(unittest.TestCase):
    def test_code_tree_reads_the_noop_pattern_from_paths_py(self):
        script = (ROOT / "ci" / "code-tree.sh").read_text()
        self.assertIn("paths.py\" noop-regex", script)
        self.assertNotRegex(script, r"(?m)^noop='", "code-tree.sh has its own copy of the pattern")

    def test_code_tree_skips_exactly_what_the_classifier_calls_noop(self):
        import hashlib
        listing = git("ls-tree", "-r", "--full-tree", "HEAD")
        kept = "".join(line + "\n" for line in listing.splitlines()
                       if not paths.is_noop(line.split("\t", 1)[1]))
        want = hashlib.sha256(kept.encode()).hexdigest()
        got = subprocess.run([str(ROOT / "ci" / "code-tree.sh"), "HEAD"], cwd=ROOT,
                             capture_output=True, text=True, check=True).stdout.strip()
        self.assertEqual(got, want)


@unittest.skipUnless(HAVE_GIT, "needs git and a checkout")
class ShippedMachines(unittest.TestCase):
    """The guard: what the mod loads today is never models-only."""

    def shipped_cs(self):
        for p in git("ls-files", "mods-src").splitlines():
            if p.endswith(".cs") and "/tests/" not in p and "/tools/" not in p:
                yield p, (ROOT / p).read_text(encoding="utf-8", errors="replace")

    def test_every_loaded_rig_and_shape_is_refused(self):
        # An oracle independent of the classifier's reference forms: a rig is loaded if a
        # shipped .cs names its asset path, a shape if a blocktype or a .cs names it.
        sources = "\n".join(text for _, text in self.shipped_cs())
        blocktypes = "\n".join((ROOT / p).read_text(encoding="utf-8") for p in
                               git("ls-files", ASSETS + "blocktypes").splitlines())
        loaded = []
        for p in git("ls-files", ASSETS + "config", ASSETS + "shapes/block").splitlines():
            machine = paths.model_asset_id(p)
            if machine is None:
                continue
            stem = Path(p).stem
            if p.endswith("-rig.json"):
                hit = f"config/{stem}.json" in sources
            else:
                hit = f"shapes/block/{stem}.json" in sources or f"seraphhorizons:block/{stem}\"" in blocktypes
            if hit:
                loaded.append(p)
        self.assertIn(ASSETS + "config/buckingmill-rig.json", loaded)
        self.assertIn(ASSETS + "shapes/block/rosser.json", loaded)
        self.assertGreaterEqual(len(loaded), 10)
        for p in loaded:
            with self.subTest(p):
                ok, reasons = paths.classify([p], "HEAD")
                self.assertFalse(ok, f"{p} is loaded by the mod but classified models-only")

    def test_named_shipped_assets_are_refused(self):
        for p in ["config/buckingmill-rig.json", "shapes/block/buckingmill.json",
                  "shapes/block/buckingmill_frame.json", "config/rosser-rig.json",
                  "config/drawbench-rig.json", "config/handcar-rig.json", "config/eidolongantry-rig.json"]:
            with self.subTest(p):
                self.assertFalse(paths.classify([ASSETS + p], "HEAD")[0])

    def test_no_generic_loader_of_shapes_or_configs(self):
        # The reference check looks for a machine by name. Code that loads every shape or rig
        # under a folder would load one without naming it, and the fast path must then go.
        loader = re.compile(r"Get(?:Many|Locations)\(\s*\"(?:shapes|config)/")
        for p, text in self.shipped_cs():
            self.assertIsNone(loader.search(text), f"{p} loads shapes/ or config/ wholesale")


@unittest.skipUnless(shutil.which("git"), "needs git")
class ReferenceForms(unittest.TestCase):
    """classify in a scratch repository, a model asset beside each way the mod might name it."""

    RIG = ASSETS + "config/widget-rig.json"
    SHAPE = ASSETS + "shapes/block/widget_frame.json"

    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.dir)
        self.git("init", "-q")
        self.write("README.md", "x")
        self.commit()
        self.base = self.git("rev-parse", "HEAD").strip()
        self.write(self.RIG, "{}")
        self.write(self.SHAPE, '{"textures": {"a": "seraphhorizons:block/widget/wood"}}')
        self.write(MOD + "Widget/tools/make_shape.py", "# writes widget-rig.json and block/widget\n")
        self.write(MOD + "tests/Widget/WidgetRigTests.cs", 'var s = File("widget-rig.json");')

    def git(self, *args):
        return subprocess.run(["git", "-c", "user.name=t", "-c", "user.email=t@t", *args], cwd=self.dir,
                              capture_output=True, text=True, check=True).stdout

    def write(self, rel, text):
        p = Path(self.dir) / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text)

    def commit(self):
        self.git("add", "-A")
        self.git("commit", "-q", "-m", "x")

    def classify(self):
        self.commit()
        changed = paths.changed(self.base, "HEAD", cwd=self.dir)
        return paths.classify(changed, "HEAD", cwd=self.dir)

    def test_an_unreferenced_model_is_models_only(self):
        ok, reasons = self.classify()
        self.assertTrue(ok, reasons)

    def in_base(self, files):
        """Commit `files` alone as the base, leaving the model files to the change."""
        for rel, text in files.items():
            self.write(rel, text)
        self.git("add", "--", *files)
        self.git("commit", "-q", "-m", "base")
        self.base = self.git("rev-parse", "HEAD").strip()

    def test_names_that_load_nothing_are_ignored(self):
        self.in_base({
            MOD + "Ore/Core/Recovery.cs": "/// the widget's recovery\npublic enum Kind { Widget, Widgets }\n",
            ASSETS + "config/ore-processing.json": '{"widget": 0.5}',
            ASSETS + "lang/en.json": '{"widget": "Widget"}',
            MOD + "Widget/README.md": "`config/widget-rig.json`",
            ASSETS + "blocktypes/gadget.json": '{"code": "gadgetwidgetry"}',
        })
        ok, reasons = self.classify()
        self.assertTrue(ok, reasons)

    def assert_refused_by(self, rel, text):
        # Already on the base, so only the model's own files change.
        self.in_base({rel: text})
        ok, reasons = self.classify()
        self.assertFalse(ok, f"{rel} with {text!r} should make widget live")
        self.assertEqual(len(reasons), 1, reasons)
        self.assertIn(f"widget is named by {rel}", reasons[0])

    def test_a_rig_asset_path_in_csharp(self):
        self.assert_refused_by(MOD + "Widget/Game/WidgetSystem.cs",
                               'static readonly AssetLocation Rig = new(Domain, "config/widget-rig.json");')

    def test_a_shape_path_in_csharp(self):
        self.assert_refused_by(MOD + "Widget/Game/WidgetRenderer.cs", 'new(Domain, "shapes/block/widget.json")')

    def test_a_block_code_literal_in_csharp(self):
        self.assert_refused_by(MOD + "Widget/Game/BlockWidget.cs", 'new AssetLocation(Domain, "widget-frame-north")')

    def test_a_bare_code_literal_in_csharp(self):
        self.assert_refused_by(MOD + "Machines/Game/Registry.cs", 'string[] codes = ["rosser", "Widget"];')

    def test_a_shape_in_a_blocktype(self):
        self.assert_refused_by(ASSETS + "blocktypes/widget/frame.json",
                               '{ code: "frame", shape: { base: "seraphhorizons:block/widget_frame" } }')

    def test_a_block_coded_after_the_machine(self):
        self.assert_refused_by(ASSETS + "blocktypes/machines.json", '{ code: "widget", class: "BlockMachine" }')

    def test_a_patch_naming_it(self):
        self.assert_refused_by(ASSETS + "patches/widget.json", '[{"op": "add", "path": "/x", "value": "widget"}]')

    def test_an_atlas_scenario(self):
        self.assert_refused_by("tests/PackTests/WidgetScenarios.cs", 'Load("config/widget-rig.json")')

    def test_the_exporter(self):
        self.assert_refused_by("tools/recipe-export/Recipes/WidgetExport.cs", 'Code("widget")')

    def test_another_pack_mod(self):
        self.assert_refused_by("mods-src/othermod/Fix.cs", '"seraphhorizons:block/widget"')

    def test_a_non_model_path_beside_it(self):
        self.write(MOD + "SeraphHorizons.cs", "// changed")
        ok, reasons = self.classify()
        self.assertFalse(ok)
        self.assertEqual(reasons, [MOD + "SeraphHorizons.cs: not a models-only path"])

    def test_deleting_a_loaded_rig(self):
        self.write(MOD + "Widget/Game/WidgetSystem.cs", '"config/widget-rig.json"')
        self.commit()
        self.base = self.git("rev-parse", "HEAD").strip()
        (Path(self.dir) / self.RIG).unlink()
        ok, reasons = self.classify()
        self.assertFalse(ok)
        self.assertIn("widget is named by " + MOD + "Widget/Game/WidgetSystem.cs", " ".join(reasons))


if __name__ == "__main__":
    unittest.main()
