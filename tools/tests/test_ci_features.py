"""ci/features.json and ci/features.py: the feature map that slices a PR run's Atlas shards by the
pack's own mod's features (docs/merge-queue.md, "Sliced PR runs"; ci/atlas-shards.md, "Feature
slicing").

- The map holds to the tree: every feature's code, exporter files and classes exist, every class it
  names runs in some shard, every ModConfig setting is one feature's (or listed as global), and every
  top-level entry of mods-src/seraphhorizons is a feature's, global, shared or listed as unmapped, so
  a new feature folder, file or setting cannot slip past it. No code file has two owners, and global
  code and the shared files are never claimed.
- Ownership by path, asset code, lang key and exporter file.
- The C# reading the dependency step rests on: top-level types, what a file can see, one step
  through the mod's code and transitively through the scenario files.
- The shared files' attribution, key by key and line by line, on synthetic changes: any change it
  cannot place leaves the file to run everything.
- The planner end to end in a scratch repository with a small mod, scenarios and map.

Run with `python3 -m unittest discover -s tools/tests -p test_ci_features.py`.
"""

import difflib
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
if "ci_paths" not in sys.modules:
    _spec = importlib.util.spec_from_file_location("ci_paths", ROOT / "ci" / "paths.py")
    _module = importlib.util.module_from_spec(_spec)
    sys.modules["ci_paths"] = _module  # dataclasses look their module up there
    _spec.loader.exec_module(_module)
paths = sys.modules["ci_paths"]
F = paths.feature_map

MOD = F.MOD
ASSETS = F.ASSETS
HAVE_GIT = shutil.which("git") is not None and (ROOT / ".git").exists()


def settings(text: str) -> set[str]:
    """The SeraphHorizonsConfig properties declared in SeraphHorizons.cs."""
    body = text[text.index("public class SeraphHorizonsConfig"):]
    return set(re.findall(r"^    public \S.*? (\w+) \{ get; set; \}", body, re.M))


@unittest.skipUnless(HAVE_GIT, "needs git and the repository")
class TheMap(unittest.TestCase):
    """ci/features.json against the tree at HEAD."""

    @classmethod
    def setUpClass(cls):
        cls.fmap = F.load_map()
        cls.code = F.read_tree("HEAD", str(ROOT))
        cls.tests = paths.read_scenario_tree("HEAD", str(ROOT)).test_classes()
        cls.shards = paths.load_shards()
        ls = subprocess.run(["git", "ls-tree", "-r", "--name-only", "HEAD", "--", MOD], cwd=ROOT,
                            capture_output=True, text=True, check=True).stdout
        cls.files = ls.split()

    def test_always_on_shards_exist(self):
        names = {s["shard"] for s in self.shards}
        self.assertTrue(self.fmap.always)
        self.assertLessEqual(set(self.fmap.always), names)

    def test_every_code_entry_exists(self):
        for f in self.fmap.features.values():
            for c in f.code:
                with self.subTest(feature=f.name, code=c):
                    p = ROOT / MOD / c
                    self.assertTrue(p.is_dir() if c.endswith("/") else p.is_file(), p)
        for g in self.fmap.global_:
            self.assertTrue((ROOT / MOD / g).exists(), g)
        for u in self.fmap.unmapped:
            self.assertTrue((ROOT / MOD / u).is_dir(), u)

    def test_every_exporter_file_exists(self):
        for f in self.fmap.features.values():
            for e in f.export:
                with self.subTest(feature=f.name, export=e):
                    self.assertTrue((ROOT / F.EXPORT / e).is_file())

    def test_every_class_a_feature_lists_exists_and_runs_in_a_shard(self):
        import fnmatch
        for f in self.fmap.features.values():
            for pattern in f.classes:
                with self.subTest(feature=f.name, classes=pattern):
                    hits = [c for c in self.tests if fnmatch.fnmatchcase(c, pattern)]
                    self.assertTrue(hits, f"no test class is {pattern}")
                    for c in hits:
                        for fqn in self.tests[c]:
                            self.assertTrue(paths.shards_selecting(fqn, self.shards), fqn)

    def test_every_setting_is_one_features_or_global(self):
        declared = settings((ROOT / MOD / "SeraphHorizons.cs").read_text())
        self.assertGreater(len(declared), 50, "the settings reading broke")
        owners: dict[str, list[str]] = {}
        for f in self.fmap.features.values():
            for s in f.switches:
                owners.setdefault(s, []).append(f.name)
        for s, fs in owners.items():
            self.assertEqual(len(fs), 1, f"{s} is the switch of {fs}")
            self.assertIn(s, declared, f"{fs[0]} names {s}, which SeraphHorizonsConfig does not declare")
        self.assertFalse(set(self.fmap.global_switches) & owners.keys())
        self.assertLessEqual(set(self.fmap.global_switches), declared)
        missing = declared - owners.keys() - set(self.fmap.global_switches)
        self.assertFalse(missing, f"settings in no feature: add each to its feature's switches in "
                                  f"ci/features.json, or to global_switches: {sorted(missing)}")

    def test_every_top_level_entry_is_mapped(self):
        """A new folder or top-level file in the mod needs a feature (or a place in global or
        unmapped): the remedy is ci/features.json."""
        tops = {p[len(MOD):].split("/")[0] + ("/" if "/" in p[len(MOD):] else "") for p in self.files}
        shared = {p[len(MOD):] for p in F.SHARED_CS if p.startswith(MOD)}
        codes = [c for f in self.fmap.features.values() for c in f.code]
        for top in sorted(tops):
            if top in ("assets/", "tests/") or top in shared or paths.is_noop(MOD + top):
                continue
            with self.subTest(top):
                self.assertTrue(top in self.fmap.global_ or top in self.fmap.unmapped
                                or any(c == top or c.startswith(top) for c in codes),
                                f"{top} is in no feature: add it to ci/features.json (a feature's code, "
                                f"global, or unmapped if it holds no C#)")

    def test_every_code_file_has_one_owner_or_is_global_or_shared(self):
        for p in self.files:
            if not F.is_code(p):
                continue
            with self.subTest(p):
                owners = [f.name for f in self.fmap.features.values() if f.owns_code(p[len(MOD):])]
                if p in F.SHARED_CS:
                    continue
                if self.fmap.is_global(p):
                    self.assertEqual(owners, [], "global code a feature also claims")
                else:
                    self.assertEqual(len(owners), 1, f"owned by {owners}")

    def test_unmapped_entries_hold_no_code(self):
        for u in self.fmap.unmapped:
            self.assertFalse([p for p in self.files if p.startswith(MOD + u) and F.is_code(p)], u)

    def test_global_and_shared_paths_are_never_claimed(self):
        for p in self.files:
            if self.fmap.is_global(p) or p in F.SHARED_CS or p == F.LANG or p.startswith(F.SNAPSHOTS):
                self.assertIsNone(self.fmap.owners(p), p)
        for p in [MOD + "SeraphHorizons.csproj", MOD + "modinfo.json", MOD + "Machines/Game/BEOreMachine.cs",
                  MOD + "Core/LangEntries.cs", F.EXPORT + "Exporter.cs", F.EXPORT + "RecipeSection.cs",
                  "schema/recipe-export.schema.json", "pack/pack.toml", ".github/workflows/ci.yml"]:
            self.assertIsNone(self.fmap.owners(p), p)

    def test_the_shared_files_exist(self):
        for p in [*F.SHARED_CS, F.LANG]:
            self.assertTrue((ROOT / p).is_file(), p)
        self.assertTrue([p for p in self.files if F.SNAPSHOT_FILE.fullmatch(p)])

    def test_every_feature_settings_attribute_on_their_own(self):
        """Each switch, alone in a config defaults snapshot change, is its feature's."""
        for f in self.fmap.features.values():
            for s in f.switches:
                self.assertEqual(self.fmap.switch_owners(s), {f.name})

    def test_the_features_types_are_named_for_the_shared_files(self):
        nm = F.names(self.fmap, self.code)
        self.assertEqual(nm.features.get("DebarkedTrunks"), {"Rosser"})
        self.assertEqual(nm.features.get("FillJig"), {"Jig"})
        self.assertEqual(nm.features.get("Rosser"), {"Rosser"})
        self.assertIn("OreMachinesConfig", nm.global_)
        self.assertIn("Machines", nm.global_)
        self.assertNotIn("Core", nm.global_)


class Ownership(unittest.TestCase):
    """FeatureMap.owners on the real map: what a path, an asset code and a lang key belong to."""

    @classmethod
    def setUpClass(cls):
        cls.fmap = F.load_map()

    def owners(self, path):
        return self.fmap.owners(path)

    def test_code(self):
        self.assertEqual(self.owners(MOD + "Rosser/Game/BERosser.cs"), {"Rosser"})
        self.assertEqual(self.owners(MOD + "Ore/Processing/OreSpalling.cs"), {"OreProcessing"})
        self.assertEqual(self.owners(MOD + "Ore/Core/OreCells.cs"), {"Ore"})
        self.assertEqual(self.owners(MOD + "FellingWear.cs"), {"Woodworking"})
        self.assertEqual(self.owners(MOD + "Core/BigPileRules.cs"), {"StorageTweaks"}, "a feature's file in Core/")
        self.assertIsNone(self.owners(MOD + "Core/LangEntries.cs"), "the rest of Core/ is global")
        self.assertIsNone(self.owners(MOD + "Rosser/tools/make_shape.py"), "a generator is the models rule's")
        self.assertIsNone(self.owners(MOD + "tests/Rosser/RosserRigTests.cs"), "the units rule's")
        self.assertIsNone(self.owners(MOD + "NewThing/NewThingSystem.cs"), "unmapped runs everything")

    def test_assets_by_code(self):
        for rel, want in [("blocktypes/rosser/frame.json", {"Rosser"}), ("shapes/block/rosser_frame.json", {"Rosser"}),
                          ("config/rosser-rig.json", {"Rosser"}), ("recipes/grid/rosser.json", {"Rosser"}),
                          ("patches/rosser-debarkedtrunk.json", {"Rosser"}),
                          ("config/handbook/eidolon.json", {"Eidolon"}), ("itemtypes/spring.json", {"MandrelStation"}),
                          ("textures/block/spring/seep-normal.png", {"Springs"}),
                          ("config/tradelists/trader-cook.json", {"Trading"}),
                          ("patches/gearconsumers-ppex.json", {"Gears"})]:
            with self.subTest(rel):
                self.assertEqual(self.owners(ASSETS + rel), want)
        self.assertIsNone(self.owners(ASSETS + "blocktypes/rosserx/frame.json"), "a code ends at - _ . or /")
        self.assertIsNone(self.owners(ASSETS + "config/unknownthing.json"))
        self.assertIsNone(self.owners(F.LANG))
        self.assertIsNone(self.owners(F.SNAPSHOTS + "0.1.0/files/seraphhorizons.json"))

    def test_lang_keys(self):
        for key, want in [("blockhelp-rosser-fitpart", {"Rosser"}), ("block-rosser-frame-*", {"Rosser"}),
                          ("loggingmod:block-treetrunk-oak-*-debarked-*", {"Rosser"}),
                          ("item-schematic-rosser", {"Trading"}), ("tidyvariants-creative-group-title", {"TidyVariants"}),
                          ("item-creature-eidolon", {"Eidolon"}), ("item-spring-steel", {"MandrelStation"})]:
            with self.subTest(key):
                self.assertEqual(self.fmap.lang_owners(key), want)
        for key in ["machine-error-toofew", "configdefaults-chat-header", "somethingnew-title"]:
            self.assertEqual(self.fmap.lang_owners(key), set(), key)

    def test_exporter(self):
        self.assertEqual(self.owners(F.EXPORT + "Recipes/JigExport.cs"), {"Jig"})
        self.assertEqual(self.owners(F.EXPORT + "RecipeSection.Jig.cs"), {"Jig"})
        self.assertIsNone(self.owners(F.EXPORT + "Recipes/Readers.cs"))

    def test_shared_files(self):
        for p in [*F.SHARED_CS, F.LANG, F.SNAPSHOTS + "0.1.0/files/seraphhorizons.json"]:
            self.assertTrue(F.is_shared(p), p)
        for p in [F.SNAPSHOTS + "0.1.0/index.json", F.SNAPSHOTS + "0.1.0/files/ppex.json"]:
            self.assertFalse(F.is_shared(p), p)


class Reading(unittest.TestCase):
    """read_code, CodeFile.sees and Code.dependents."""

    def test_top_level_types_namespace_usings_extensions(self):
        f = F.read_code("""using SeraphHorizons.Mod.Core;
using static SeraphHorizons.Mod.Rosser.RosserParts;
using C = SeraphHorizons.Mod.PicklingTub.Core.PicklingTubConfig;

namespace SeraphHorizons.Mod.Jig;

/// <summary>Not a class Commented in a comment.</summary>
[Serializable]
public sealed class JigSystem
{
    private sealed class Nested { }
    public static int Count(this string s) => s.Length;
}

internal static class JigText { }
public record struct JigCell(int X);
public delegate void JigDone(int n);
""")
        self.assertEqual(f.namespace, "SeraphHorizons.Mod.Jig")
        self.assertEqual(f.types, {"JigSystem", "JigText", "JigCell", "JigDone"})
        self.assertEqual(f.extensions, {"Count"})
        self.assertEqual(f.usings, {"SeraphHorizons.Mod.Core", "SeraphHorizons.Mod.Rosser.RosserParts",
                                    "SeraphHorizons.Mod.PicklingTub.Core.PicklingTubConfig"})
        self.assertNotIn("Commented", f.tokens)

    def test_what_a_file_sees(self):
        ns = "SeraphHorizons.Mod.Rosser.Core"
        cases = [
            ("namespace SeraphHorizons.Mod.Rosser.Core;\nclass A { RosserConfig c; }", True),
            ("namespace SeraphHorizons.Mod.Rosser.Core.Deeper;\nclass A { RosserConfig c; }", True),
            ("using SeraphHorizons.Mod.Rosser.Core;\nnamespace X;\nclass A { RosserConfig c; }", True),
            ("using static SeraphHorizons.Mod.Rosser.Core.RosserConfig;\nnamespace X;\nclass A { RosserConfig c; }", True),
            ("namespace SeraphHorizons.Mod;\nclass A { Rosser.Core.RosserConfig c; }", True),
            ("namespace X;\nclass A { SeraphHorizons.Mod.Rosser.Core.RosserConfig c; }", True),
            ("using SeraphHorizons.Mod.Other;\nnamespace X;\nclass A { RosserConfig c; }", False),
            ("namespace SeraphHorizons.Mod.Rosser;\nclass A { RosserConfig c; }", False),
            ("using SeraphHorizons.Mod.Rosser.Core;\nnamespace X;\n// RosserConfig\nclass A { }", False),
        ]
        for text, want in cases:
            with self.subTest(text):
                self.assertEqual(F.read_code(text).sees(ns, "RosserConfig"), want)

    def test_dependents_one_step_in_the_mod_and_on_through_the_scenarios(self):
        code = F.Code({
            MOD + "Foo/Foo.cs": F.read_code("namespace SeraphHorizons.Mod.Foo;\npublic class Foo { }"),
            MOD + "Bar/Bar.cs": F.read_code("using SeraphHorizons.Mod.Foo;\nnamespace SeraphHorizons.Mod.Bar;\n"
                                            "public class Bar { Foo f; }"),
            MOD + "Baz/Baz.cs": F.read_code("using SeraphHorizons.Mod.Bar;\nnamespace SeraphHorizons.Mod.Baz;\n"
                                            "public class Baz { Bar b; }"),
            MOD + "Qux/Foo.cs": F.read_code("namespace SeraphHorizons.Mod.Qux;\npublic class Other { Foo f; }"),
            "tests/PackTests/Helper.cs": F.read_code("using SeraphHorizons.Mod.Foo;\nnamespace SeraphHorizons.PackTests;\n"
                                                    "internal static class FooHelper { static Foo f; }"),
            "tests/PackTests/A.cs": F.read_code("namespace SeraphHorizons.PackTests;\n"
                                               "public partial class AScenarios { void T() => FooHelper.Go(); }"),
            "tests/PackTests/A2.cs": F.read_code("namespace SeraphHorizons.PackTests;\n"
                                                "public partial class AScenarios { }\nclass OnlyHere { }"),
            "tests/PackTests/B.cs": F.read_code("namespace SeraphHorizons.PackTests;\n"
                                               "public class BScenarios { OnlyHere x; }"),
        })
        seed = {MOD + "Foo/Foo.cs": code.files[MOD + "Foo/Foo.cs"]}
        got = code.dependents(seed, leaves={"AScenarios", "BScenarios"})
        self.assertEqual(got, {MOD + "Foo/Foo.cs", MOD + "Bar/Bar.cs", "tests/PackTests/Helper.cs",
                               "tests/PackTests/A.cs"},
                         "Baz is two steps on; Qux's Foo is another namespace's; A2 is A's other part, not a user")
        got = code.dependents(seed)
        self.assertIn("tests/PackTests/B.cs", got, "without leaves a partial class's parts reach each other")


def diff(old: str, new: str) -> str:
    return "\n".join(difflib.unified_diff(old.splitlines(), new.splitlines(), "a", "b", n=0, lineterm=""))


NAMES = F.Names({"Rosser": {"Rosser"}, "RosserConfig": {"Rosser"}, "RosserSettings": {"Rosser"},
                 "DebarkedTrunks": {"Rosser"}, "Jig": {"Jig"}, "FillJig": {"Jig"}, "JigSystem": {"Jig"},
                 "GearReclamation": {"Gears"}, "PicklingTubSystem": {"Gears"}},
                {"OreMachinesConfig", "Machines"})

CONFIG = """using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod;

public class SeraphHorizonsSystem : ModSystem
{
    private Harmony? _harmony;
    private bool _tunRack;

    public override void Start(ICoreAPI api)
    {
        if (Config(api).Rosser && DebarkedTrunks.Applies(api))
            DebarkedTrunks.Patch(_harmony ??= new Harmony(HarmonyId));
        if (Config(api).Jig)
        {
            _harmony ??= new Harmony(HarmonyId);
            Log(api);
        }
        foreach (var x in Things(api))
            Use(x);
    }
}

public class SeraphHorizonsConfig
{
    /// <summary>The rosser: a ring debarker.</summary>
    public bool Rosser { get; set; } = true;

    /// <summary>The jig.</summary>
    public bool Jig { get; set; } = true;

    public OreMachinesConfig OreMachinesSettings { get; set; } = new();
}
"""


class SharedCs(unittest.TestCase):
    """attribute_cs on SeraphHorizons.cs-like and list-like synthetic changes."""

    def attribute(self, old, new):
        return F.attribute_cs(old, new, diff(old, new), NAMES)[0]

    def edit(self, old, new, base=CONFIG):
        self.assertIn(old, base)
        return self.attribute(base, base.replace(old, new))

    def test_a_setting_added_with_its_comment(self):
        self.assertEqual(self.edit("    /// <summary>The jig.</summary>",
                                   "    /// <summary>The rosser's figures.</summary>\n"
                                   "    public RosserConfig RosserSettings { get; set; } = new();\n\n"
                                   "    /// <summary>The jig.</summary>"), {"Rosser"})

    def test_a_comment_is_nothing(self):
        self.assertEqual(self.edit("The rosser: a ring debarker.", "The rosser: a ring barker."), set())
        self.assertEqual(self.edit("    /// <summary>The jig.</summary>\n", ""), set())

    def test_a_statement_over_two_lines(self):
        self.assertEqual(self.edit("DebarkedTrunks.Patch(_harmony", "DebarkedTrunks.Patch2(_harmony"), {"Rosser"})

    def test_a_line_in_a_features_block_is_its(self):
        self.assertEqual(self.edit("            Log(api);", "            Log(api, 2);"), {"Jig"})

    def test_a_removed_line_is_read_in_the_old_file(self):
        self.assertEqual(self.edit("            Log(api);\n", ""), {"Jig"})

    def test_two_features(self):
        new = CONFIG.replace("Log(api);", "Log(api, 2);").replace("bool Rosser { get; set; } = true",
                                                                  "bool Rosser { get; set; } = false")
        self.assertEqual(self.attribute(CONFIG, new), {"Jig", "Rosser"})

    def test_global_code_is_no_features(self):
        self.assertIsNone(self.edit("OreMachinesConfig OreMachinesSettings { get; set; } = new();",
                                    "OreMachinesConfig OreMachinesSettings { get; set; } = new(1);"))
        self.assertIsNone(self.edit("using SeraphHorizons.Mod.Core;", "using SeraphHorizons.Mod.Machines.Core;"))

    def test_a_line_naming_no_feature(self):
        self.assertIsNone(self.edit("    private bool _tunRack;", "    private bool _tunRack2;"))
        self.assertIsNone(self.edit("            Use(x);", "            Use(x, 1);"), "the foreach names no feature")

    def test_one_line_it_cannot_place_spoils_the_file(self):
        new = CONFIG.replace("Log(api);", "Log(api, 2);").replace("private bool _tunRack;", "private bool _tun;")
        self.assertIsNone(self.attribute(CONFIG, new))

    def test_a_using_naming_a_feature(self):
        self.assertEqual(self.edit("using SeraphHorizons.Mod.Core;\n",
                                   "using SeraphHorizons.Mod.Core;\nusing SeraphHorizons.Mod.Jig;\n"), {"Jig"})

    def test_blank_lines_are_nothing(self):
        self.assertEqual(self.edit("    private bool _tunRack;\n", "    private bool _tunRack;\n\n"), set())

    def test_list_entries_are_one_each(self):
        base = ("namespace SeraphHorizons.Mod.Core;\n\npublic sealed class SwitchOwnership\n{\n"
                "    public static readonly OwnedBySwitch[] HandListed =\n    [\n"
                "        new(\"Rosser\", [], [], [\"rosser\"]),\n    ];\n}\n")
        self.assertEqual(self.edit("        new(\"Rosser\", [], [], [\"rosser\"]),\n",
                                   "        new(\"Rosser\", [], [], [\"rosser\"]),\n"
                                   "        // The exporter's own recipe type for the jig.\n"
                                   "        new(\"Jig\", [], [], [\"jig\"]),\n", base), {"Jig"})

    def test_an_argument_on_a_line_of_its_own(self):
        base = ("namespace SeraphHorizons.Mod;\n\npublic static class SwitchRegistry\n{\n"
                "    public static IEnumerable<int> Features()\n    {\n"
                "        yield return (nameof(SeraphHorizonsConfig.GearReclamation),\n"
                "            [PicklingTubSystem.RecipeAsset],\n"
                "            [Other.BlockAsset]);\n    }\n}\n")
        self.assertEqual(self.edit("            [Other.BlockAsset]);", "            [Other.BlockAssets]);", base),
                         {"Gears"})

    def test_the_exporters_section_list(self):
        base = ("namespace SeraphHorizons.RecipeExport;\n\npublic static partial class RecipeSection\n{\n"
                "    public static void Fill(Ctx ctx)\n    {\n        FillRocker(ctx, records, types);\n    }\n}\n")
        self.assertEqual(self.edit("        FillRocker(ctx, records, types);\n",
                                   "        FillRocker(ctx, records, types);\n        FillJig(ctx, records, types);\n",
                                   base), {"Jig"})

    def test_a_new_file_and_a_deleted_one(self):
        text = "public bool Jig { get; set; }\n"
        self.assertIsNone(F.attribute_cs(None, text, diff("", text), NAMES)[0])
        self.assertIsNone(F.attribute_cs(text, None, diff(text, ""), NAMES)[0])

    def test_braces(self):
        self.assertEqual(self.edit("        {\n            _harmony ??= new Harmony(HarmonyId);\n            Log(api);\n        }\n",
                                   "        {\n            _harmony ??= new Harmony(HarmonyId);\n        }\n"
                                   "        if (Config(api).Jig)\n        {\n            Log(api);\n        }\n"), {"Jig"})
        self.assertIsNone(self.edit("        foreach (var x in Things(api))\n            Use(x);\n",
                                    "        foreach (var x in Things(api))\n        {\n            Use(x);\n        }\n"),
                          "the foreach names no feature")


class SharedJson(unittest.TestCase):
    def test_changed_keys(self):
        old = '{"a": 1, "b": {"x": 1}, "c": 3}'
        self.assertEqual(F.changed_keys(old, '{"a": 1, "b": {"x": 2}, "d": 4}'), {"b", "c", "d"})
        self.assertEqual(F.changed_keys(old, '{"c": 3, "b": {"x": 1}, "a": 1}'), set(), "order is not a change")
        self.assertEqual(F.changed_keys(None, '{"a": 1}'), {"a"})
        self.assertEqual(F.changed_keys('{"a": 1}', None), {"a"})

    def test_what_it_refuses(self):
        for text in ['{"a": 1, "a": 2}', '[1, 2]', '{"a": ']:
            with self.subTest(text), self.assertRaises(ValueError):
                F.changed_keys('{}', text)

    def test_lang_and_snapshot_by_key(self):
        fmap = F.load_map()
        keys = F.changed_keys('{"blockhelp-rosser-fitpart": "a", "block-jig": "J"}',
                              '{"blockhelp-rosser-fitpart": "b", "block-jig": "J", "jig-title": "J"}')
        self.assertEqual(keys, {"blockhelp-rosser-fitpart", "jig-title"})
        self.assertEqual(set().union(*(fmap.lang_owners(k) for k in keys)), {"Rosser", "Jig"})
        self.assertEqual(fmap.switch_owners("RosserSettings"), {"Rosser"})
        self.assertEqual(fmap.switch_owners("OreMachinesSettings"), set())


SCENARIO = """using Atlas.XUnit;
{usings}
namespace SeraphHorizons.PackTests;

public class {name} : AtlasScenarioBase
{{
    [AtlasScenario]
    public void Does_a_thing() => {body};
}}
"""

MAP = {
    "always": ["items", "rest"],
    "global": ["Core/"],
    "global_switches": [],
    "unmapped": [],
    "features": {
        "Foo": {"code": ["Foo/", "Core/FooRules.cs"], "codes": ["foo"], "export": ["RecipeSection.Foo.cs"],
                "switches": ["Foo"], "classes": []},
        "Bar": {"code": ["Bar/"], "codes": ["bar"], "switches": ["Bar"], "classes": ["BarScenarios"]},
    },
}

SHARDS = [
    {"shard": "foo", "filter": "FullyQualifiedName~PackTests.FooScenarios."},
    {"shard": "bar", "filter": "FullyQualifiedName~PackTests.BarScenarios."},
    {"shard": "items", "filter": "FullyQualifiedName~PackTests.ItemExportScenarios."},
    {"shard": "other", "filter": "FullyQualifiedName~PackTests.OtherScenarios."},
    {"shard": "rest", "filter": "FullyQualifiedName!~PackTests.FooScenarios.&FullyQualifiedName!~PackTests.BarScenarios."
                                "&FullyQualifiedName!~PackTests.ItemExportScenarios.&FullyQualifiedName!~PackTests.OtherScenarios."},
]


@unittest.skipUnless(shutil.which("git"), "needs git")
class FeaturePlanning(unittest.TestCase):
    """plan() in a scratch repository: a mod with features Foo and Bar (Bar uses Foo), global Core/,
    their scenarios, and a map of its own."""

    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.dir)
        outside = tempfile.mkdtemp()  # not in the scratch repository, whose every path is a change
        self.addCleanup(shutil.rmtree, outside)
        map_file = Path(outside) / "features.json"
        map_file.write_text(json.dumps(MAP))
        old = F.MAP_FILE
        F.MAP_FILE = map_file
        self.addCleanup(setattr, F, "MAP_FILE", old)
        self.git("init", "-q")
        self.write(MOD + "Foo/FooSystem.cs", "namespace SeraphHorizons.Mod.Foo;\n\npublic class FooSystem\n{\n"
                                             "    public static void Go() { }\n}\n")
        self.write(MOD + "Bar/BarSystem.cs", "using SeraphHorizons.Mod.Foo;\n\nnamespace SeraphHorizons.Mod.Bar;\n\n"
                                             "public class BarSystem\n{\n    public static void Go() => FooSystem.Go();\n}\n")
        self.write(MOD + "Core/Shared.cs", "namespace SeraphHorizons.Mod.Core;\n\npublic static class Shared { }\n")
        self.write(MOD + "Core/FooRules.cs", "namespace SeraphHorizons.Mod.Core;\n\npublic static class FooRules { }\n")
        self.write(F.EXPORT + "RecipeSection.Foo.cs", "namespace SeraphHorizons.RecipeExport;\n\n"
                                                      "public static partial class RecipeSection\n{\n"
                                                      "    static void FillFoo() { }\n}\n")
        self.write(MOD + "SeraphHorizons.cs", CONFIG.replace("Rosser", "Foo").replace("Jig", "Bar"))
        self.write(ASSETS + "blocktypes/foo.json", "{}\n")
        self.write(F.LANG, json.dumps({"block-foo": "Foo", "block-bar": "Bar", "machine-x": "x"}, indent=2))
        t = "tests/PackTests/"
        self.write(t + "FooScenarios.cs", SCENARIO.format(name="FooScenarios", usings="using SeraphHorizons.Mod.Foo;\n",
                                                          body="FooSystem.Go()"))
        self.write(t + "BarScenarios.cs", SCENARIO.format(name="BarScenarios", usings="", body="Nothing()"))
        self.write(t + "ItemExportScenarios.cs", SCENARIO.format(name="ItemExportScenarios", usings="", body="Nothing()"))
        self.write(t + "OtherScenarios.cs", SCENARIO.format(name="OtherScenarios", usings="", body="Nothing()"))
        self.write(t + "SharedWorldScenarios.cs", SCENARIO.format(name="SharedWorldScenarios", usings="", body="Nothing()"))
        self.commit()
        self.base = self.git("rev-parse", "HEAD").strip()

    def git(self, *args):
        return subprocess.run(["git", "-c", "user.name=t", "-c", "user.email=t@t", *args], cwd=self.dir,
                              capture_output=True, text=True, check=True).stdout

    def write(self, rel, text):
        p = Path(self.dir) / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text)

    def edit(self, rel, old, new):
        p = Path(self.dir) / rel
        text = p.read_text()
        self.assertIn(old, text)
        p.write_text(text.replace(old, new, 1))

    def commit(self):
        self.git("add", "-A")
        self.git("commit", "-q", "-m", "x")

    def plan(self):
        self.commit()
        return paths.plan(self.base, "HEAD", cwd=self.dir, shards=SHARDS)

    def test_a_features_code_runs_its_scenarios_and_its_users(self):
        self.edit(MOD + "Foo/FooSystem.cs", "Go() { }", "Go() { Go2(); }")
        p = self.plan()
        self.assertEqual((p["sliced"], p["atlas"], p["smoke"], p["cairn"], p["site"]),
                         (True, ["foo", "bar", "items", "rest"], True, True, True))
        self.assertIn("reaching Bar", " ".join(p["reasons"]))

    def test_a_feature_nothing_uses(self):
        self.edit(MOD + "Bar/BarSystem.cs", "FooSystem.Go();", "FooSystem.Go(); ")
        self.assertEqual(self.plan()["atlas"], ["bar", "items", "rest"])

    def test_an_asset_runs_the_scenarios_naming_its_feature(self):
        self.write(ASSETS + "blocktypes/foo.json", '{"code": "foo"}\n')
        self.assertEqual(self.plan()["atlas"], ["foo", "items", "rest"], "not Bar: an asset is not Foo's code")

    def test_a_lang_key(self):
        self.edit(F.LANG, '"Bar"', '"The bar"')
        self.assertEqual(self.plan()["atlas"], ["bar", "items", "rest"])

    def test_a_lang_key_no_feature_owns(self):
        self.edit(F.LANG, '"x"', '"y"')
        p = self.plan()
        self.assertEqual(p["atlas"], "all")
        self.assertIn("'machine-x' is no feature's", " ".join(p["reasons"]))

    def test_a_setting(self):
        self.edit(MOD + "SeraphHorizons.cs", "public bool Bar { get; set; } = true;", "public bool Bar { get; set; } = false;")
        self.assertEqual(self.plan()["atlas"], ["bar", "items", "rest"])

    def test_a_comment_in_a_shared_file_runs_the_always_on_shards(self):
        self.edit(MOD + "SeraphHorizons.cs", "/// <summary>The jig.</summary>", "/// <summary>The bar.</summary>")
        p = self.plan()
        self.assertEqual((p["atlas"], p["smoke"]), (["items", "rest"], True))

    def test_a_setting_beside_a_line_no_feature_owns(self):
        self.edit(MOD + "SeraphHorizons.cs", "private bool _tunRack;", "private bool _tun;")
        self.assertEqual(self.plan()["atlas"], "all")

    def test_global_code_runs_everything(self):
        self.edit(MOD + "Core/Shared.cs", "class Shared { }", "class Shared { int x; }")
        self.edit(MOD + "Bar/BarSystem.cs", "FooSystem.Go();", "FooSystem.Go(); ")
        self.assertEqual(self.plan()["atlas"], "all")

    def test_a_features_rules_in_core(self):
        self.edit(MOD + "Core/FooRules.cs", "class FooRules { }", "class FooRules { int x; }")
        self.assertEqual(self.plan()["atlas"], ["items", "rest"], "nothing names FooRules")

    def test_a_features_exporter_file(self):
        self.edit(F.EXPORT + "RecipeSection.Foo.cs", "FillFoo() { }", "FillFoo() { Go(); }")
        self.assertEqual(self.plan()["atlas"], ["foo", "items", "rest"])

    def test_code_no_feature_owns_that_uses_the_change(self):
        self.write(MOD + "Baz/Baz.cs", "using SeraphHorizons.Mod.Foo;\n\nnamespace SeraphHorizons.Mod.Baz;\n\n"
                                       "public class Baz\n{\n    void Go() => FooSystem.Go();\n}\n")
        self.commit()
        self.base = self.git("rev-parse", "HEAD").strip()
        self.edit(MOD + "Foo/FooSystem.cs", "Go() { }", "Go() { Go2(); }")
        p = self.plan()
        self.assertEqual(p["atlas"], "all")
        self.assertIn("reaches code no feature owns", " ".join(p["reasons"]))

    def test_a_class_the_map_lists_that_is_gone(self):
        m = json.loads(F.MAP_FILE.read_text())
        m["features"]["Bar"]["classes"] = ["GoneScenarios"]
        F.MAP_FILE.write_text(json.dumps(m))
        self.edit(MOD + "Bar/BarSystem.cs", "FooSystem.Go();", "FooSystem.Go(); ")
        p = self.plan()
        self.assertEqual(p["atlas"], ["items", "rest"], "noted, and test_ci_features fails on it")
        self.assertIn("not there: Bar's GoneScenarios", " ".join(p["reasons"]))

    def test_beside_a_scenario_change(self):
        self.edit("tests/PackTests/ItemExportScenarios.cs", "Nothing()", "Nothing(1)")
        self.edit(MOD + "Bar/BarSystem.cs", "FooSystem.Go();", "FooSystem.Go(); ")
        self.assertEqual(self.plan()["atlas"], ["bar", "items", "rest"])


if __name__ == "__main__":
    unittest.main()
