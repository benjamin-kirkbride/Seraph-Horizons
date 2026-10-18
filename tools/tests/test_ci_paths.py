"""ci/paths.py: the green-tree memo's no-op paths, and the planner that slices a PR run.

- ci/code-tree.sh hashes the tree with the same no-op pattern the classifier skips, so the two
  can't drift.
- The Atlas matrix (ci/atlas-shards.json) selects every test method in tests/PackTests exactly
  once, and the workflow runs that file's list rather than one of its own.
- The planner reads the scenario classes a change touches, at base and head, and plans the full
  suite for anything it cannot account for: a helper, a path no rule knows, a class no shard
  selects, an error.
- The allow-list's patterns match what a machine model PR changes and nothing else.
- A shipped machine's rig or shape (the bucking sawmill's buckingmill-rig.json, ...) is never
  models-only, and the reference check that decides it finds every way the mod names a machine's
  assets. A rig or shape the fast path let through would ship untested by smoke and Atlas.

Run with `python3 -m unittest discover -s tools/tests -p test_ci_paths.py`.
"""

import importlib.util
import json
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
_spec = importlib.util.spec_from_file_location("ci_paths", ROOT / "ci" / "paths.py")
paths = importlib.util.module_from_spec(_spec)
sys.modules["ci_paths"] = paths  # dataclasses look their module up there
_spec.loader.exec_module(paths)

MOD = "mods-src/seraphhorizons/"
ASSETS = MOD + "assets/seraphhorizons/"
HAVE_GIT = shutil.which("git") is not None and (ROOT / ".git").exists()


def git(*args, cwd=ROOT):
    return subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True, check=True).stdout


class Matching(unittest.TestCase):
    def test_noop_paths(self):
        for p in ["docs/merge-queue.md", "README.md", "mods-src/seraphhorizons/AmalgamPan/README.md",
                  ".claude/settings.json", ".mergify.yml", ".gitignore", "LICENSE"]:
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


class Filters(unittest.TestCase):
    """parse_filter reads `dotnet test --filter` as VSTest does, for the conditions the matrix uses."""

    def match(self, text, name):
        return paths.parse_filter(text)(name)

    def test_contains_and_not_contains(self):
        n = "SeraphHorizons.PackTests.EidolonMineScenarios.Digs"
        self.assertTrue(self.match("FullyQualifiedName~PackTests.EidolonMineScenarios.", n))
        self.assertFalse(self.match("FullyQualifiedName!~PackTests.Eidolon", n))
        self.assertTrue(self.match("FullyQualifiedName~packtests.eidolonmine", n), "case-insensitive")
        self.assertTrue(self.match("FullyQualifiedName=seraphhorizons.packtests.eidolonminescenarios.digs", n))
        self.assertFalse(self.match("FullyQualifiedName!=" + n, n))

    def test_and_binds_tighter_than_or(self):
        f = "FullyQualifiedName~A|FullyQualifiedName~B&FullyQualifiedName~C"
        self.assertTrue(self.match(f, "x.A.y"))           # A | (B & C)
        self.assertFalse(self.match(f, "x.B.y"))
        self.assertTrue(self.match(f, "x.B.C"))
        self.assertFalse(self.match("(FullyQualifiedName~A|FullyQualifiedName~B)&FullyQualifiedName~C", "x.A.y"))

    def test_shapes_the_matrix_uses(self):
        tidy = "FullyQualifiedName~.TidyVariants"
        self.assertTrue(self.match(tidy, "SeraphHorizons.PackTests.TidyVariantsScenarios.Hides"))
        self.assertTrue(self.match(tidy, "SeraphHorizons.PackTests.SharedWorldScenarios.TidyVariants_hide"),
                        "a method name matches too, which is why classes are matched method by method")
        self.assertTrue(self.match("FullyQualifiedName~FixScenarios.", "SeraphHorizons.PackTests.PackFixScenarios.X"))
        jobs = ("(FullyQualifiedName~PackTests.Eidolon&FullyQualifiedName!~PackTests.EidolonScenarios.)"
                "|FullyQualifiedName~PackTests.VirtualClockScenarios.")
        self.assertTrue(self.match(jobs, "SeraphHorizons.PackTests.EidolonChunkScenarios.Loads"))
        self.assertFalse(self.match(jobs, "SeraphHorizons.PackTests.EidolonScenarios.Spawns"))
        self.assertTrue(self.match(jobs, "SeraphHorizons.PackTests.VirtualClockScenarios.Ticks"))

    def test_escape(self):
        self.assertTrue(self.match(r"FullyQualifiedName~a\|b", "x.a|b"))

    def test_what_it_cannot_evaluate_raises(self):
        for bad in ["Category=Slow", "FullyQualifiedName~A&", "(FullyQualifiedName~A", "FullyQualifiedName~A)",
                    "FullyQualifiedName~", "~A"]:
            with self.subTest(bad), self.assertRaises(paths.FilterError):
                paths.parse_filter(bad)


SCENARIO = """using Atlas.XUnit;

namespace SeraphHorizons.PackTests;

/// <summary>Names <see cref="Other"/> only in a comment.</summary>
[AtlasWorld]
public partial class {name} : AtlasScenarioBase
{{
    [AtlasScenario]
    public void Does_a_thing() => Helper.Run(); // Other.Thing() in a comment
}}
"""


class Scanning(unittest.TestCase):
    def test_types_bases_tests_and_tokens(self):
        f = paths.scan_cs("""using static SeraphHorizons.PackTests.GearUses;

namespace SeraphHorizons.PackTests;

internal static class Parts
{
    public static int Count(this string s) => s.Length;
    public sealed record Nested(int A);
}

[AtlasWorld]
public class FooScenarios(ITestOutputHelper output)
    : GradeBase(output), IDisposable
{
    // Bar.Baz() in a comment
    [AtlasScenario]
    [ReadsBootLog]
    public async Task Async_one() { var s = "//"; Thing.Go(); }

    [AtlasTheory]
    [InlineData(1)]
    public void Theory_two(int x) { }
}
""")
        self.assertIsNone(f.problem)
        self.assertEqual(f.namespace, "SeraphHorizons.PackTests")
        self.assertEqual([t.name for t in f.types], ["Parts", "FooScenarios"])
        parts, foo = f.types
        self.assertTrue(parts.static)
        self.assertEqual(parts.extensions, {"Count"})
        self.assertEqual(foo.base, "GradeBase")
        self.assertEqual(foo.tests, ["Async_one", "Theory_two"])
        self.assertIn("Thing", foo.tokens)
        self.assertIn("AtlasWorld", foo.tokens, "attributes above a type are its code")
        self.assertIn("GearUses", foo.tokens, "usings are every type's")
        self.assertNotIn("Bar", foo.tokens, "a comment line names nothing")
        self.assertNotIn("Nested", [t.name for t in f.types], "nested types are not top-level")

    def test_problems(self):
        cases = {
            "namespace X\n{\npublic class A {}\n}\n": "column 0",
            "namespace X;\npublic class A\n{\n    [AtlasScenario]\n}\n": "no method",
            "public class A {}\n": "namespace",
            'namespace X;\npublic class A\n{\n    const string J = """\n{ "a": 1 }\n""";\n}\n': "column 0",
        }
        for text, want in cases.items():
            with self.subTest(text):
                self.assertIn(want, paths.scan_cs(text).problem or "")

    def test_global_effects(self):
        for text in ["[assembly: Foo]\nnamespace X;\n", "global using Foo;\nnamespace X;\n",
                     "namespace X;\ninternal static class S\n{\n    [ModuleInitializer]\n    internal static void I() {}\n}\n"]:
            with self.subTest(text):
                self.assertTrue(paths.scan_cs(text).global_effect)

    def tree(self, files):
        return paths.ScenarioTree({f"tests/PackTests/{k}": paths.scan_cs(v) for k, v in files.items()})

    def test_test_classes(self):
        t = self.tree({
            "Base.cs": "namespace N;\npublic abstract class GradeBase : AtlasScenarioBase\n{\n"
                       "    [AtlasScenario]\n    public void Inherited() {}\n}\n",
            "A.cs": "namespace N;\npublic class AScenarios : GradeBase\n{\n}\n",
            "B1.cs": "namespace N;\npublic partial class BScenarios : AtlasScenarioBase\n{\n}\n",
            "B2.cs": "namespace N;\npublic partial class BScenarios\n{\n    [AtlasScenario]\n    public void Two() {}\n}\n",
            "H.cs": "namespace N;\ninternal static class Helper\n{\n}\n",
        })
        self.assertEqual(t.test_classes(), {"AScenarios": {"N.AScenarios.Inherited"},
                                            "BScenarios": {"N.BScenarios.Two"}})

    def test_dependents(self):
        t = self.tree({
            "A.cs": "namespace N;\ninternal static class Flax\n{\n}\n"
                    "public partial class AScenarios : AtlasScenarioBase\n{\n}\n",
            "B.cs": "namespace N;\npublic class BScenarios : AtlasScenarioBase\n{\n    void M() => Flax.Go();\n}\n",
            "C.cs": "namespace N;\npublic class CScenarios : AtlasScenarioBase\n{\n    /// <see cref=\"Flax\"/>\n}\n",
            "D.cs": "namespace N;\ninternal static class Ext\n{\n    public static void Spin(this int x) {}\n}\n",
            "E.cs": "namespace N;\npublic class EScenarios : AtlasScenarioBase\n{\n    void M() => 3.Spin();\n}\n",
        })
        self.assertEqual(t.dependents({"Flax"}), {"Flax", "BScenarios"})
        self.assertEqual(t.dependents({"Ext"}), {"Ext", "EScenarios"})
        self.assertEqual(t.dependents({"AScenarios"}), {"AScenarios"})


@unittest.skipUnless(HAVE_GIT, "needs git and a checkout")
class Matrix(unittest.TestCase):
    """ci/atlas-shards.json against tests/PackTests at HEAD, and the workflow against both."""

    @classmethod
    def setUpClass(cls):
        cls.shards = paths.load_shards()
        cls.tree = paths.read_scenario_tree("HEAD", cwd=ROOT)
        cls.ci = (ROOT / ".github" / "workflows" / "ci.yml").read_text()

    def test_every_scenario_file_reads(self):
        # A file the planner cannot read makes every PR touching it run the full suite: not
        # wrong, but the slicing is lost. Lay it out as the others are (ci/paths.py scan_cs).
        for p, f in self.tree.files.items():
            self.assertIsNone(f.problem, p)

    def test_every_test_attribute_is_one_the_planner_knows(self):
        # The planner finds test methods by their attribute. One it does not know (a custom
        # [SlowScenario] deriving from AtlasScenario) would hide its methods from the shard
        # check below and from planning: add it to paths.TEST_ATTRIBUTES.
        texts = [(ROOT / p).read_text(encoding="utf-8") for p in git("ls-files", "tests/PackTests").splitlines()
                 if p.endswith(".cs")]
        used = {m for t in texts for m in re.findall(r"(?m)^\s+\[(?:[^\]]*[\s,])?(\w*(?:Scenario|Theory|Fact))\b(?!\s*[.=])", t)}
        self.assertTrue(used)
        self.assertLessEqual(used, set(paths.TEST_ATTRIBUTES))
        lines = sum(1 for t in texts for line in t.splitlines() if paths._TEST_ATTR.match(line))
        found = sum(len(t.tests) for f in self.tree.files.values() for t in f.types)
        self.assertEqual(found, lines, "a test attribute whose method the planner did not find")

    def test_every_test_method_runs_in_exactly_one_shard(self):
        classes = self.tree.test_classes()
        self.assertGreaterEqual(len(classes), 40)
        self.assertIn("SharedWorldScenarios", classes)
        self.assertIn("OreCellOrderSeamFarFirst", classes)
        for cls, methods in sorted(classes.items()):
            for fqn in sorted(methods):
                with self.subTest(fqn):
                    hit = paths.shards_selecting(fqn, self.shards)
                    self.assertEqual(len(hit), 1, f"{fqn} is run by {hit or 'no shard'}: fix ci/atlas-shards.json")

    def test_every_shard_runs_something(self):
        fqns = [m for ms in self.tree.test_classes().values() for m in ms]
        for s in self.shards:
            with self.subTest(s["shard"]):
                self.assertTrue(any(paths.parse_filter(s["filter"])(m) for m in fqns), "selects no test")

    def test_the_workflow_runs_the_file(self):
        self.assertIn("matrix: ${{ fromJSON(needs.tree.outputs.atlas_matrix) }}", self.ci)
        self.assertNotRegex(self.ci, r"(?m)^\s*filter:\s*FullyQualifiedName", "ci.yml has its own shard list")
        self.assertNotRegex(self.ci, r"(?m)^\s*- shard:", "ci.yml has its own shard list")
        self.assertIn("FILTER: ${{ matrix.filter }}", self.ci)
        for s in ("rest",):
            self.assertIn(f"matrix.shard == '{s}'", self.ci)
            self.assertIn(s, [x["shard"] for x in self.shards])

    def test_full_outputs_are_the_file(self):
        out = paths.outputs(paths.full_plan(["x"], self.shards), self.shards)
        self.assertEqual(json.loads(out["atlas_matrix"]), {"include": self.shards})
        self.assertEqual({k: v for k, v in out.items() if k != "atlas_matrix"},
                         {"sliced": "false", "smoke": "true", "cairn": "true", "site": "true", "atlas": "true"})


class Workflow(unittest.TestCase):
    """What ci.yml does with the plan: only an ordinary PR run plans, and ci-ok takes each skip
    only from the memo or the plan."""

    @classmethod
    def setUpClass(cls):
        cls.ci = (ROOT / ".github" / "workflows" / "ci.yml").read_text()
        m = re.search(r"\n      - id: plan\n(.*?)\n\n", cls.ci, re.S)
        cls.plan_step = m.group(1) if m else ""

    def test_only_an_ordinary_pr_run_plans(self):
        step = self.plan_step
        self.assertIn("python3 ci/paths.py plan HEAD^1 HEAD", step)
        plan_at = step.index("python3 ci/paths.py plan")
        for guard in ['[ "$EVENT" != pull_request ]', '[ "$TESTED" = true ]', "mergify/merge-queue/*",
                      "git rev-list --parents -n 1 HEAD"]:
            self.assertIn(guard, step[:plan_at], f"{guard} must come before the planner runs")
        self.assertIn("python3 ci/paths.py outputs full", step)

    def test_skippable_jobs_follow_the_plan(self):
        jobs = {"smoke": "smoke", "atlas": "atlas", "cairn": "cairn", "site": "site"}
        for job, flag in jobs.items():
            m = re.search(rf"\n  {job}:\n(?:    .*\n|\s*\n)*?    if: (.*)\n", self.ci)
            self.assertIsNotNone(m, job)
            self.assertIn(f"needs.tree.outputs.{flag} == 'true'", m.group(1), job)

    def test_ci_ok_checks_every_job_it_needs(self):
        m = re.search(r"\n  ci-ok:\n    if: always\(\)\n    needs: \[([^\]]*)\]", self.ci)
        needs = [j.strip() for j in m.group(1).split(",")]
        for job in needs:
            with self.subTest(job):
                self.assertRegex(self.ci, rf'(?m)^\s*check {re.escape(job)} "\$', f"ci-ok has no check line for {job}")
        self.assertRegex(self.ci, r'check tools "\$TOOLS" "\$TESTED"\n')
        self.assertRegex(self.ci, r'check units "\$UNITS" "\$TESTED"\n')
        self.assertIn("needs.tree.outputs.sliced == 'false'", self.ci, "a sliced run must not record a green tree")


class Planning(unittest.TestCase):
    """plan() and outputs() without git: paths given, scenario rule fed no tests/PackTests path."""

    SHARDS = [{"shard": "a", "filter": "FullyQualifiedName~PackTests.AScenarios."},
              {"shard": "rest", "filter": "FullyQualifiedName!~PackTests.AScenarios."}]

    def plan(self, changed):
        return paths.plan("BASE", "HEAD", shards=self.SHARDS, paths=changed)

    def test_noop_and_unit_tests_need_only_tools_and_units(self):
        p = self.plan(["docs/x.md", MOD + "tests/HideTests.cs", MOD + "AmalgamPan/tools/make_shape.py"])
        self.assertEqual((p["sliced"], p["atlas"], p["smoke"], p["cairn"], p["site"]), (True, [], False, False, False))
        out = paths.outputs(p, self.SHARDS)
        self.assertEqual(out["atlas"], "false")
        self.assertEqual(json.loads(out["atlas_matrix"])["include"], self.SHARDS, "never an empty matrix")

    def test_an_unknown_path_needs_everything(self):
        p = self.plan(["docs/x.md", "tools/packtool.py"])
        self.assertEqual((p["sliced"], p["atlas"], p["smoke"]), (False, "all", True))
        self.assertTrue(any("tools/packtool.py" in r for r in p["reasons"]))

    def test_no_change_needs_everything(self):
        self.assertFalse(self.plan([])["sliced"])

    def test_a_packtests_file_that_is_not_a_scenario_file_needs_everything(self):
        for path in ["tests/PackTests/PackTests.csproj", "tests/PackTests/fixtures/x/seraphhorizons.json"]:
            with self.subTest(path):
                self.assertEqual(self.plan([path])["atlas"], "all")

    def test_rest_step_inputs_keep_rest(self):
        def claim_icon_export(ctx, ps):
            mine = [p for p in ps if p.startswith("tools/icon-export/")]
            return [paths.Claim(mine, paths.Need(frozenset({"a"})), "test")] if mine else []
        old = paths.RULES
        paths.RULES = [*old, claim_icon_export]
        try:
            p = self.plan(["tools/icon-export/Core/X.cs"])
        finally:
            paths.RULES = old
        self.assertEqual(p["atlas"], "all", "a and rest are every shard here")
        self.assertTrue(p["sliced"])

    def test_site_keeps_smoke(self):
        p = paths.as_plan(paths.Need(frozenset(), site=True), [], self.SHARDS)
        self.assertTrue(p["smoke"], "the site's end-to-end tests read smoke's export")

    def test_an_error_is_a_full_plan(self):
        r = subprocess.run([sys.executable, str(ROOT / "ci" / "paths.py"), "plan", "no-such-rev", "HEAD"],
                           cwd=ROOT, capture_output=True, text=True)
        self.assertEqual(r.returncode, 0)
        p = json.loads(r.stdout)
        self.assertFalse(p["sliced"])
        self.assertTrue(p["reasons"][0].startswith("error:"), p["reasons"])


@unittest.skipUnless(shutil.which("git"), "needs git")
class ScenarioSlicing(unittest.TestCase):
    """plan() in a scratch repository holding a small tests/PackTests."""

    SHARDS = [
        {"shard": "a", "filter": "FullyQualifiedName~PackTests.AScenarios."},
        {"shard": "b", "filter": "FullyQualifiedName~PackTests.BScenarios.|FullyQualifiedName~PackTests.Renamed"},
        {"shard": "rest", "filter": "FullyQualifiedName!~PackTests.AScenarios.&FullyQualifiedName!~PackTests.BScenarios."
                                    "&FullyQualifiedName!~PackTests.Renamed"},
    ]
    T = "tests/PackTests/"

    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.dir)
        self.git("init", "-q")
        self.write("A.cs", SCENARIO.format(name="AScenarios").replace("Helper.Run()", "Flax.Go()"))
        self.write("AFlax.cs", "namespace SeraphHorizons.PackTests;\n\ninternal static class Flax\n{\n"
                               "    public static void Go() {}\n}\n\npublic partial class AScenarios\n{\n"
                               "    [AtlasScenario]\n    public void Flax_two() {}\n}\n")
        self.write("B.cs", SCENARIO.format(name="BScenarios"))
        self.write("Shared.cs", SCENARIO.format(name="SharedWorldScenarios"))
        self.write("Helper.cs", "namespace SeraphHorizons.PackTests;\n\ninternal static class Helper\n{\n"
                                "    public static void Run() {}\n}\n")
        self.write("README.md", "x")
        self.commit()
        self.base = self.git("rev-parse", "HEAD").strip()

    def git(self, *args):
        return subprocess.run(["git", "-c", "user.name=t", "-c", "user.email=t@t", *args], cwd=self.dir,
                              capture_output=True, text=True, check=True).stdout

    def write(self, name, text):
        p = Path(self.dir) / self.T / name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text)

    def commit(self):
        self.git("add", "-A")
        self.git("commit", "-q", "-m", "x")

    def plan(self):
        self.commit()
        return paths.plan(self.base, "HEAD", cwd=self.dir, shards=self.SHARDS)

    def edit(self, name, old, new):
        p = Path(self.dir) / self.T / name
        text = p.read_text()
        self.assertIn(old, text)
        p.write_text(text.replace(old, new))

    def test_a_scenario_change_runs_its_shard(self):
        self.edit("B.cs", "Does_a_thing", "Does_another")
        p = self.plan()
        self.assertEqual((p["sliced"], p["atlas"], p["smoke"], p["cairn"], p["site"]), (True, ["b"], False, False, False))

    def test_a_partial_class_part_in_a_file_named_otherwise(self):
        self.edit("AFlax.cs", "Flax_two", "Flax_three")
        self.assertEqual(self.plan()["atlas"], ["a"])

    def test_a_helper_in_a_scenario_file_runs_its_users(self):
        # Flax is declared in AFlax.cs; BScenarios starts using it at head.
        self.edit("B.cs", "Helper.Run()", "Flax.Go()")
        self.edit("AFlax.cs", "public static void Go() {}", "public static void Go() { }")
        self.assertEqual(self.plan()["atlas"], ["a", "b"])

    def test_a_helper_file_runs_everything(self):
        self.edit("Helper.cs", "Run() {}", "Run() { }")
        p = self.plan()
        self.assertEqual(p["atlas"], "all")
        self.assertIn("declares no test class", " ".join(p["reasons"]))

    def test_a_renamed_class_runs_both_names(self):
        self.edit("Shared.cs", "class SharedWorldScenarios", "class RenamedScenarios")
        self.assertEqual(self.plan()["atlas"], ["b", "rest"])

    def test_a_deleted_file_runs_its_class(self):
        (Path(self.dir) / self.T / "B.cs").unlink()
        self.assertEqual(self.plan()["atlas"], ["b"])

    def test_a_new_scenario_file(self):
        self.write("C.cs", SCENARIO.format(name="CScenarios"))
        self.assertEqual(self.plan()["atlas"], ["rest"])

    def test_noop_and_model_paths_beside_it(self):
        self.edit("B.cs", "Does_a_thing", "Does_another")
        p = Path(self.dir) / MOD / "AmalgamPan/tools/make_shape.py"
        p.parent.mkdir(parents=True)
        p.write_text("# x\n")
        self.write("README.md", "changed")
        self.assertEqual(self.plan()["atlas"], ["b"])

    def test_a_class_no_shard_selects_runs_everything(self):
        shards = [s for s in self.SHARDS if s["shard"] != "rest"]
        self.edit("Shared.cs", "Does_a_thing", "Does_another")
        self.commit()
        p = paths.plan(self.base, "HEAD", cwd=self.dir, shards=shards)
        self.assertEqual(p["atlas"], "all")
        self.assertIn("no shard's filter selects", " ".join(p["reasons"]))

    def test_a_class_two_shards_select_runs_both(self):
        shards = [*self.SHARDS, {"shard": "b2", "filter": "FullyQualifiedName~.BScenarios.Does"}]
        self.edit("B.cs", "Helper.Run()", "Helper.Run(); ")
        self.commit()
        self.assertEqual(paths.plan(self.base, "HEAD", cwd=self.dir, shards=shards)["atlas"], ["b", "b2"])

    def test_an_assembly_attribute_runs_everything(self):
        self.edit("B.cs", "using Atlas.XUnit;", "using Atlas.XUnit;\n[assembly: CollectionBehavior(MaxParallelThreads = 1)]")
        self.assertEqual(self.plan()["atlas"], "all")

    def test_a_file_it_cannot_read_runs_everything(self):
        self.edit("B.cs", "namespace SeraphHorizons.PackTests;", "namespace SeraphHorizons.PackTests\n{")
        self.assertEqual(self.plan()["atlas"], "all")

    def test_a_scenario_beside_a_code_change_runs_everything(self):
        self.edit("B.cs", "Does_a_thing", "Does_another")
        p = Path(self.dir) / MOD / "SeraphHorizons.cs"
        p.parent.mkdir(parents=True)
        p.write_text("// x\n")
        p = self.plan()
        self.assertFalse(p["sliced"])
        self.assertEqual(p["atlas"], "all")


if __name__ == "__main__":
    unittest.main()
