"""seraphhorizons' UnifiedPipes patch still fits Pipes and Power Expanded.

`mods-src/seraphhorizons/assets/seraphhorizons/patches/unifiedpipes-ppex.json` adds copper and
lead to ppex's pipe blocktypes and the bronzes to its valves, and switches off its plate-and-nails
pipe recipes and its iron and steel valve recipes by index. This reads ppex's zip from build/mods
and requires every path to land where it was written for: the material group third with states
iron and steel, an iron4 texture by type to copy, and the disabled recipes making what the patch
means. The mod checks the same at run time (Pipes/Core/PipeAssetGuard.cs) and stands down with a
warning; this catches a ppex update before it ships. It also requires the patch to agree with
gearconsumers-ppex.json, which patches the same valve recipes.

`unifiedpipes-chutesection.json` adds lead to the game's chute section (the hollow section),
switches off the game's anvil recipe and its plate-and-solder grid recipe for it and gives the game's
five chute recipes a solder bar per section and a soldering iron, the grid ones by index; it is held
to the game's own files, as are the textures of this mod's angle and pipe section. `unifiedpipes-betterruins.json` switches off Better Ruins' five solderless blueprint chutes by
index; it is held to Better Ruins' zip. Pipes/Core/ChuteSections.cs checks the same at run time.

Needs `python3 tools/packtool.py fetch`; skips without ppex's zip. The chute section checks need
VINTAGE_STORY set to a game or server install, and skip without it.
"""

from __future__ import annotations

import os
import unittest
from pathlib import Path

from test_gear_consumers import MODS_DIR, PATCHES, game_assets, lock_mods, loads, mod_assets, resolve

PATCH = PATCHES / "unifiedpipes-ppex.json"
CHUTE_PATCH = PATCHES / "unifiedpipes-chutesection.json"
CHUTE_ITEM = "game:itemtypes/resource/chutesection.json"
CHUTE_RECIPES = "game:recipes/grid/chute.json"
CHUTE_SMITHING = "game:recipes/smithing/chutesection.json"
OWN_ASSETS = PATCHES.parent
BR_PATCH = PATCHES / "unifiedpipes-betterruins.json"
BR_RECIPES = "betterruins:recipes/grid/schematic-mechanical/mechanical.json"
# index: (output, the game's pattern, sections)
CHUTES = {0: ("chute-elbow-down-east", "I_,_I", 2), 1: ("chute-straight-ns", "I,I", 2), 2: ("chute-cross-ground", "_I_,I_I,_I_", 4),
          3: ("chute-t-ns", "_I_,I_I", 3), 4: ("chute-3way-down-east", "_I_,II_", 3)}
BR_CHUTES = {6: "game:chute-elbow-down-east", 7: "game:chute-straight-ns", 8: "game:chute-cross-ground",
             9: "game:chute-t-ns", 10: "game:chute-3way-down-east"}
PIPES = ["straight", "bend", "tjunction", "xjunction"]
VALVES = ["valve", "pressurevalve"]
DISABLED = {0: "ppex:pipe-straight-ns-", 1: "ppex:pipe-bend-nw-", 2: "ppex:pipe-tjunction-uns-",
            3: "ppex:pipe-xjunction-nswe-", 7: "ppex:pipe-valve-sn-", 8: "ppex:pipe-pressurevalve-sn-",
            9: "ppex:pipe-valve-sn-", 10: "ppex:pipe-pressurevalve-sn-"}


class UnifiedPipesPatchFitsPpex(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        ppex = [m for m in lock_mods() if m["id"] == "ppex"]
        if not ppex or not (MODS_DIR / ppex[0]["fileName"]).exists():
            raise unittest.SkipTest("ppex is not fetched: run `python3 tools/packtool.py fetch`")
        cls.assets = mod_assets(MODS_DIR / ppex[0]["fileName"])
        cls.patch = loads(PATCH.read_text())

    def test_every_op_depends_on_ppex_and_targets_a_ppex_file(self):
        for op in self.patch:
            with self.subTest(op=op):
                self.assertEqual([{"modid": "ppex"}], op.get("dependsOn"))
                self.assertIn(op["file"], self.assets)

    def test_blocktypes_have_the_material_group_the_patch_adds_to(self):
        for name, added in [(n, ["copper", "lead"]) for n in PIPES] + [(n, ["tinbronze", "bismuthbronze", "blackbronze"]) for n in VALVES]:
            asset = f"ppex:blocktypes/pipes/{name}.json"
            with self.subTest(asset=asset):
                doc = loads(self.assets[asset])
                group = doc["variantgroups"][2]
                self.assertEqual(("material", ["iron", "steel"]), (group["code"], group["states"]))
                for metal in ("iron", "steel"):
                    self.assertIn("iron4", doc["texturesByType"][f"*-{metal}"])
                ops = [op for op in self.patch if op["file"] == asset]
                self.assertEqual(added, [op["value"] for op in ops if op["path"] == "/variantgroups/2/states/-"])
                self.assertEqual({f"/texturesByType/*-{m}" for m in added},
                                 {op["path"] for op in ops if op["path"].startswith("/texturesByType/")})

    def test_disabled_recipes_are_the_ones_meant(self):
        asset = "ppex:recipes/grid/pipes.json"
        doc = loads(self.assets[asset])
        paths = [op["path"] for op in self.patch if op["file"] == asset]
        self.assertEqual([f"/{i}/enabled" for i in sorted(DISABLED)], paths)
        for i, output in DISABLED.items():
            with self.subTest(recipe=i):
                self.assertTrue(resolve(doc, f"/{i}/output/code").startswith(output))
        # Every recipe left on makes a fitting from any pipe (passthroughs, outlet).
        for i, recipe in enumerate(doc):
            if i not in DISABLED:
                self.assertIn("{brick}", recipe["output"]["code"])

    def test_gear_consumers_patches_the_same_valves(self):
        gears = loads((PATCHES / "gearconsumers-ppex.json").read_text())
        touched = {op["path"].split("/")[1] for op in gears if op["file"] == "ppex:recipes/grid/pipes.json"}
        self.assertEqual({"7", "8", "9", "10"}, touched, "gearconsumers-ppex.json moved: re-check unifiedpipes-ppex.json")


class ChuteSectionPatchFitsTheGame(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        install = os.environ.get("VINTAGE_STORY")
        if not install or not (Path(install) / "assets" / "survival").is_dir():
            raise unittest.SkipTest("VINTAGE_STORY is not set to a game or server install")
        cls.assets = game_assets(Path(install))
        cls.patch = loads(CHUTE_PATCH.read_text())

    def test_every_op_targets_the_chute_section_or_its_recipes(self):
        for op in self.patch:
            with self.subTest(op=op):
                self.assertIn(op["file"], (CHUTE_ITEM, CHUTE_RECIPES, CHUTE_SMITHING))
                self.assertEqual("server", op["side"])

    def test_the_material_group_is_copper_alone_and_textured_by_material(self):
        doc = loads(self.assets[CHUTE_ITEM])
        group = doc["variantgroups"][0]
        self.assertEqual(("material", ["copper"]), (group["code"], group["states"]))
        self.assertIn("{material}", doc["textures"]["metaltex"]["base"])
        self.assertNotIn("handbook", doc["attributes"])
        ops = [op for op in self.patch if op["file"] == CHUTE_ITEM]
        self.assertEqual(["lead"], [op["value"] for op in ops if op["path"] == "/variantgroups/0/states/-"])
        # the new metal has the game's sheet texture the {material} template names
        for metal in ("lead",):
            sheet = Path(os.environ["VINTAGE_STORY"]) / "assets" / "survival" / "textures" / (
                doc["textures"]["metaltex"]["base"].replace("{material}", metal) + ".png")
            self.assertTrue(sheet.is_file(), sheet)

    def test_the_switched_off_recipe_is_the_plate_one_and_the_only_one_making_sections(self):
        doc = loads(self.assets[CHUTE_RECIPES])
        paths = [op["path"] for op in self.patch if op["file"] == CHUTE_RECIPES]
        self.assertEqual("/5/enabled", paths[0])
        self.assertEqual({p.split("/")[1] for p in paths[1:]}, {str(i) for i in CHUTES})
        recipe = resolve(doc, "/5")
        self.assertEqual(("chutesection-copper", 2), (recipe["output"]["code"], recipe["output"]["quantity"]))
        self.assertEqual("metalplate-copper", recipe["ingredients"]["P"]["code"])
        for i, other in enumerate(doc):
            if i != 5:
                self.assertFalse(other["output"]["code"].startswith("chutesection"), i)
        # and the game's chutes take the copper section by name, so the new metals make no chute
        for other in doc[:5]:
            self.assertEqual({"chutesection-copper"}, {ing["code"] for ing in other["ingredients"].values()})

    def test_the_anvil_recipe_switched_off_is_the_copper_section(self):
        doc = loads(self.assets[CHUTE_SMITHING])
        self.assertIsInstance(doc, dict)
        self.assertEqual("chutesection-copper", doc["output"]["code"])
        self.assertEqual(("ingot-*", ["copper"]), (doc["ingredient"]["code"], doc["ingredient"]["allowedVariants"]))
        self.assertNotIn("enabled", doc)
        ops = [op for op in self.patch if op["file"] == CHUTE_SMITHING]
        self.assertEqual([("add", "/enabled", False)], [(op["op"], op["path"], op["value"]) for op in ops])

    def test_the_angle_and_pipe_section_textures_exist(self):
        textures = Path(os.environ["VINTAGE_STORY"]) / "assets" / "survival" / "textures"
        for item, metals in (("angle", ["copper", "lead"]), ("pipesection", ["copper", "lead", "iron", "steel"])):
            doc = loads((OWN_ASSETS / "itemtypes" / f"{item}.json").read_text())
            self.assertEqual(metals, doc["variantgroups"][0]["states"])
            (base,) = [t["base"] for t in doc["textures"].values()]
            for metal in metals:
                sheet = textures / (base.removeprefix("game:").replace("{metal}", metal) + ".png")
                self.assertTrue(sheet.is_file(), sheet)

    def test_each_chute_recipe_is_the_one_the_patch_solders(self):
        doc = loads(self.assets[CHUTE_RECIPES])
        for i, (output, pattern, sections) in CHUTES.items():
            with self.subTest(recipe=i):
                recipe = doc[i]
                self.assertEqual((output, pattern), (recipe["output"]["code"], recipe["ingredientPattern"]))
                self.assertEqual(["I"], list(recipe["ingredients"]))
                ops = {op["path"]: op["value"] for op in self.patch if op["file"] == CHUTE_RECIPES and op["path"].startswith(f"/{i}/")}
                self.assertEqual(sections, ops[f"/{i}/ingredients/S"]["quantity"])
                self.assertEqual("game:solderingiron", ops[f"/{i}/ingredients/T"]["code"])
                soldered = ops[f"/{i}/ingredientPattern"]
                self.assertEqual(sections, soldered.count("I"))
                self.assertEqual(ops[f"/{i}/width"] * ops[f"/{i}/height"], len(soldered.replace(",", "")))


class BetterRuinsChutesOff(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        br = [m for m in lock_mods() if m["id"] == "betterruins"]
        if not br or not (MODS_DIR / br[0]["fileName"]).exists():
            raise unittest.SkipTest("betterruins is not fetched: run `python3 tools/packtool.py fetch`")
        cls.assets = mod_assets(MODS_DIR / br[0]["fileName"])
        cls.patch = loads(BR_PATCH.read_text())

    def test_the_patch_switches_off_exactly_the_five_chutes(self):
        self.assertEqual([f"/{i}/enabled" for i in sorted(BR_CHUTES)], [op["path"] for op in self.patch])
        for op in self.patch:
            self.assertEqual((BR_RECIPES, "add", False, [{"modid": "betterruins"}]), (op["file"], op["op"], op["value"], op["dependsOn"]))
        doc = loads(self.assets[BR_RECIPES])
        for i, output in BR_CHUTES.items():
            with self.subTest(recipe=i):
                self.assertEqual(output, doc[i]["output"]["code"])
                self.assertEqual({"betterruins:br-schematic-mechanical", "game:chutesection-copper"},
                                 {ing["code"] for ing in doc[i]["ingredients"].values()})
        # no other recipe there makes a chute
        others = [i for i, r in enumerate(doc) if i not in BR_CHUTES and r["output"]["code"].startswith("game:chute-")]
        self.assertEqual([], others)


if __name__ == "__main__":
    unittest.main()
