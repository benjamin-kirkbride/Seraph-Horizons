"""Tests for tools/item-values/itemvalues.py on a small synthetic export.

Run from the repository root:

  python3 -m unittest discover -s tools/tests -p test_item_values.py -v

Expected values are worked out by hand from the rules written here, not from the tool.
"""

from __future__ import annotations

import importlib.util
import io
import json
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

TOOL = Path(__file__).resolve().parents[1] / "item-values" / "itemvalues.py"
_spec = importlib.util.spec_from_file_location("itemvalues_tool", TOOL)
iv = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = iv  # dataclasses look their module up while the class is built
_spec.loader.exec_module(iv)


def item(stack: int = 64, **attrs) -> dict:
    return {"kind": "item", "name": "x", "mod": "game", "handbookVisible": True,
            "attributes": {"maxStackSize": stack, **attrs}}


def st(code: str, q: float = 1, **kw) -> dict:
    return {"code": code, "kind": "item", "quantity": q, **kw}


def recipe(rid: str, rtype: str, ingredients: list[dict], slots: list[list[dict]], outputs: list[dict], **extra) -> dict:
    return {"id": rid, "type": rtype, "mod": "game", "ingredients": ingredients, "outputs": outputs,
            "variants": [{"ingredients": slots, "outputs": outputs}], **extra}


def grid(rid, pattern, keyed, out, **ing_extra):
    """keyed: {key: [codes]}; one item per cell."""
    ings = [{"key": k, "code": codes[0], "kind": "item", "quantity": 1, **ing_extra.get(k, {})} for k, codes in keyed.items()]
    slots = [[st(c) for c in codes] for codes in keyed.values()]
    return recipe(rid, "grid", ings, slots, [out], grid={"width": len(pattern[0]), "height": len(pattern),
                                                          "shapeless": False, "pattern": pattern})


RAWS = {
    "groups": {
        "test": {"game:log": 1.0, "game:stick": 0.01, "game:ore-*": 0.5, "game:gem": 10},
    },
    "defaults": [{"category": "crops", "match": "^fruit(-|$)", "value": 0.03}],
    "traderFallback": {"sells": 0.5, "buys": 1.0},
}
MARKUPS = {
    "toolFraction": 0.1,
    "kinds": {
        "grid": {"pct": 0.0, "flat": 0.0},
        "smelting": {"pct": 0.0, "flat": 0.0},
        "barrel": {"pct": 0.5, "flat": 0.0},
        "pressing": {"pct": 0.0, "flat": 0.5},
        "curing": {"pct": 0.0, "flat": 0.0, "perItem": 0.01},
        "distilling": {"pct": 0.5, "flat": 0.0},
        "mod": {"pct": 0.0, "flat": 1.0},
        "lottery": {"pct": 0.0, "flat": 0.0},
    },
    "excludeRecipes": ["/destroy"],
    "scrapFloor": {"found": 5, "made": 1},
    "schematics": ["*:schematic-*", "*:*-schematic-*"],
}


def lottery(rid: str, item_code: str, outcomes: list[tuple[float, list[dict]]], **extra) -> dict:
    """A lottery record (schema.md): one ingredient decided into outcomes, each (chance, stacks)."""
    outputs, shaped = [], []
    for chance, stacks in outcomes:
        shaped.append({"chance": chance, "outputs": list(range(len(outputs), len(outputs) + len(stacks)))})
        outputs += stacks
    return recipe(rid, "lottery", [st(item_code)], [[st(item_code)]], outputs,
                  lottery={"trigger": "inventory", "outcomes": shaped}, **extra)


def liquid(stack: int = 5000, per_litre: float = 100, **attrs) -> dict:
    """A liquid as the export marks it: extra.liquid.itemsPerLitre (waterTightContainerProps)."""
    extra = {**attrs.pop("extra", {}), "liquid": {"itemsPerLitre": per_litre}}
    return item(stack, extra=extra, **attrs)


def export(items: dict, recipes: list[dict]) -> dict:
    types = {}
    for r in recipes:
        types.setdefault(r["type"], {"name": r["type"], "count": 0, "shape": r["type"] if r["type"] == "grid" else "generic"})
        types[r["type"]]["count"] += 1
    return {"schemaVersion": 1, "pack": {"id": "t", "version": "0"}, "mods": {}, "items": items,
            "recipes": recipes, "recipeTypes": types}


class ItemValuesTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)
        self.write_rules()

    def tearDown(self):
        self.tmp.cleanup()

    def write_rules(self, raws=RAWS, markups=MARKUPS, overrides=None):
        (self.dir / "raw-values.json").write_text(json.dumps(raws))
        (self.dir / "markups.json").write_text(json.dumps(markups))
        (self.dir / "overrides.json").write_text(json.dumps({"values": overrides or {}}))

    def solve(self, ex):
        rules = iv.Rules.load(self.dir)
        return iv.solve(ex, rules), rules

    # ------------------------------------------------------------ routes

    def test_grid_counts_cells_and_divides_by_output(self):
        # 2 logs (cells) + 1 stick -> 4 planks: (2 + 0.01) / 4.
        ex = export({"game:log": item(), "game:stick": item(), "game:plank": item()},
                    [grid("grid|p|0", ["L_", "LS"], {"L": ["game:log"], "S": ["game:stick"]}, st("game:plank", 4))])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:plank"], 2.01 / 4)

    def test_cheapest_route_and_cheapest_alternative_win(self):
        items = {c: item() for c in ("game:log", "game:stick", "game:gem", "game:box")}
        ex = export(items, [
            grid("grid|a|0", ["G"], {"G": ["game:gem"]}, st("game:box")),
            grid("grid|b|0", ["X"], {"X": ["game:gem", "game:log"]}, st("game:box")),  # log accepted: 1.0
        ])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:box"], 1.0)
        self.assertEqual(val.source["game:box"], "grid|b|0")

    def test_multi_step_chain_with_markup_and_liquids(self):
        # log -> 2 bark (mod: +1 flat): (1 + 1) / 2 = 1.0 each;
        # barrel: bark + 1 L water (100 portions, worth 0) -> 1 L tannin (100 portions): 1.0 * 1.5 / 100.
        items = {c: item() for c in ("game:log", "game:bark", "game:waterportion", "game:tanninportion")}
        raws = json.loads(json.dumps(RAWS))
        raws["groups"]["test"]["game:waterportion"] = 0
        self.write_rules(raws=raws)
        ex = export(items, [
            recipe("x:peel|a|0", "x:peel", [st("game:log")], [[st("game:log")]], [st("game:bark", 2)]),
            recipe("barrel|t|0", "barrel", [st("game:bark"), st("game:waterportion", litres=1)],
                   [[st("game:bark")], [st("game:waterportion", 100, litres=1)]],
                   [st("game:tanninportion", 1, litres=1)]),
        ])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:bark"], 1.0)
        self.assertAlmostEqual(val.value["game:tanninportion"], 0.015)

    def test_smelting_attribute_is_a_route(self):
        items = {"game:ore-copper": item(smelting={"inputQuantity": 20, "output": st("game:ingot")}),
                 "game:ingot": item(16)}
        val, _ = self.solve(export(items, []))
        self.assertAlmostEqual(val.value["game:ingot"], 10.0)

    def test_juicing_attribute_is_a_route_and_ignores_its_byproducts(self):
        # One log pressed gives 0.25 L = 25 portions: (1.0 + 0.5 pressing flat) / 25. The pressed
        # mash and the returned gems (worth 50) are not credited, or the juice would be free.
        juicing = {"litresPerItem": 0.25, "output": st("game:juiceportion", 1, litres=0.01),
                   "pressed": st("game:pressedmash"), "returned": st("game:gem", 5)}
        items = {"game:log": item(extra={"juicing": juicing}), "game:juiceportion": item(5000),
                 "game:pressedmash": item(), "game:gem": item()}
        val, _ = self.solve(export(items, []))
        self.assertAlmostEqual(val.value["game:juiceportion"], 1.5 / 25)
        self.assertEqual(val.source["game:juiceportion"], "pressing|game:log")
        self.assertNotIn("game:pressedmash", val.value)

    def test_juicing_without_litres_is_no_route(self):
        # Mash: what is left in it rides on the stack, so the export has no litresPerItem.
        items = {"game:pressedmash": item(extra={"juicing": {"output": st("game:juiceportion", 1, litres=0.01)}}),
                 "game:juiceportion": item(5000)}
        val, _ = self.solve(export(items, []))
        self.assertNotIn("game:juiceportion", val.value)

    def test_distillation_attribute_is_a_route_by_its_ratio(self):
        # 1 portion of cider (1.0 / 100 from a log, + 0.01 curing) distils into 0.1 portion of spirit:
        # 0.02 x 1.5 / 0.1 = 0.3 a portion, so a litre of spirit costs its 10 litres of cider and more.
        items = {"game:log": item(), "game:spiritportion": item(5000),
                 "game:ciderportion": item(5000, extra={"distillation": {"ratio": 0.1, "output": st("game:spiritportion", 1, litres=0.01)}})}
        ex = export(items, [recipe("curing|game:log|0", "curing", [st("game:log")], [[st("game:log")]],
                                   [st("game:ciderportion", 100, litres=1)])])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:ciderportion"], 0.02)
        self.assertAlmostEqual(val.value["game:spiritportion"], 0.3)
        self.assertEqual(val.source["game:spiritportion"], "distilling|game:ciderportion")

    def test_distillation_without_a_ratio_is_no_route(self):
        items = {"game:ciderportion": item(5000, extra={"distillation": {"output": st("game:spiritportion")}}),
                 "game:spiritportion": item(5000)}
        val, _ = self.solve(export(items, []))
        self.assertNotIn("game:spiritportion", val.value)

    def test_min_batch_litres_spreads_the_flat_of_a_liquid_recipe(self):
        # barrel: flat 1, over at least 1 L. One portion of juice (0.01) ages into one of cider:
        # (0.01 x 1.5 + 1 x 1/100) / 1 = 0.025, not 1.015; 2 L of tannin (200 portions) from a log
        # keep the whole flat: (1 x 1.5 + 1) / 200; a solid output (a hide) is never spread.
        markups = json.loads(json.dumps(MARKUPS))
        markups["kinds"]["barrel"] = {"pct": 0.5, "flat": 1.0, "minBatchLitres": 1}
        raws = json.loads(json.dumps(RAWS))
        raws["groups"]["test"]["game:juiceportion"] = 0.01
        self.write_rules(raws=raws, markups=markups)
        items = {c: item(5000) for c in ("game:log", "game:juiceportion", "game:ciderportion", "game:tanninportion", "game:hide")}
        ex = export(items, [
            recipe("barrel|c|0", "barrel", [st("game:juiceportion", litres=0.01)],
                   [[st("game:juiceportion", 1, litres=0.01)]], [st("game:ciderportion", 1, litres=0.01)]),
            recipe("barrel|t|0", "barrel", [st("game:log")], [[st("game:log")]], [st("game:tanninportion", 200, litres=2)]),
            recipe("barrel|h|0", "barrel", [st("game:juiceportion", litres=0.01)],
                   [[st("game:juiceportion", 1, litres=0.01)]], [st("game:hide")]),
        ])
        val, rules = self.solve(ex)
        self.assertAlmostEqual(val.value["game:ciderportion"], 0.025)
        self.assertAlmostEqual(val.value["game:tanninportion"], 2.5 / 200)
        self.assertAlmostEqual(val.value["game:hide"], 1.015)
        self.assertEqual(iv.below_ingredients(ex, val, rules), [])
        self.assertIn("+ 0.01 + ", "\n".join(iv.explain(ex, val, rules, "game:ciderportion")))

    def test_per_item_charge_is_added_after_the_division(self):
        # curing: a log ages into 1 L (100 portions): 1.0 / 100 + 0.01 per portion; a portion ages
        # into another one for 0.01 more. A flat per batch could not do this: 0.01 x 100 = 1 gear.
        items = {c: item(5000) for c in ("game:log", "game:ciderportion", "game:wineportion")}
        ex = export(items, [
            recipe("curing|game:log|0", "curing", [st("game:log")], [[st("game:log")]],
                   [st("game:ciderportion", 100, litres=1)]),
            recipe("curing|game:ciderportion|0", "curing", [st("game:ciderportion")], [[st("game:ciderportion")]],
                   [st("game:wineportion")]),
        ])
        val, rules = self.solve(ex)
        self.assertAlmostEqual(val.value["game:ciderportion"], 0.02)
        self.assertAlmostEqual(val.value["game:wineportion"], 0.03)
        self.assertEqual(rules.per_item("curing"), 0.01)
        self.assertEqual(rules.per_item("grid"), 0.0)
        self.assertEqual(rules.markup("curing"), (0.0, 0.0))
        # The charge is labour, not an ingredient: the route is not below its ingredients.
        self.assertEqual(iv.below_ingredients(ex, val, rules), [])
        text = "\n".join(iv.explain(ex, val, rules, "game:wineportion"))
        self.assertIn("/ 1 + 0.01 per item [curing] = 0.0300", text)

    def test_per_item_charge_survives_a_quotient_floored_at_zero(self):
        # A lottery-style credit can take the quotient below 0; the per-item charge is added after.
        markups = json.loads(json.dumps(MARKUPS))
        markups["kinds"]["lottery"]["perItem"] = 0.25
        self.write_rules(markups=markups)
        ex = export({"game:log": item(), "game:gem": item(), "game:plank": item()}, [
            lottery("lottery|x|0", "game:log", [(0.5, [st("game:plank")]), (0.5, [st("game:gem")])])])
        val, _ = self.solve(ex)
        # plank: (1.0 - 0.5 x 10) / 0.5 < 0 -> 0, + 0.25.
        self.assertAlmostEqual(val.value["game:plank"], 0.25)

    def test_value_creating_cycle_does_not_pull_prices_down(self):
        # 2 cloth -> 4 sails, 1 sail -> 2 cloth: a loop that doubles cloth. Cloth comes from a log.
        items = {c: item() for c in ("game:log", "game:cloth", "game:sail", "game:stick")}
        ex = export(items, [
            grid("grid|cloth|0", ["L"], {"L": ["game:log"]}, st("game:cloth")),
            grid("grid|sail|0", ["CC"], {"C": ["game:cloth"]}, st("game:sail", 4)),
            grid("grid|unsail|0", ["S"], {"S": ["game:sail"]}, st("game:cloth", 2)),
        ])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:cloth"], 1.0)
        self.assertAlmostEqual(val.value["game:sail"], 0.5)

    def test_cycle_of_conversions_resolves(self):
        # block <-> 4 bricks, both made from clay too: values settle without looping forever.
        items = {c: item() for c in ("game:log", "game:brick", "game:bricks")}
        ex = export(items, [
            grid("grid|brick|0", ["L"], {"L": ["game:log"]}, st("game:brick", 2)),
            grid("grid|bricks|0", ["BB", "BB"], {"B": ["game:brick"]}, st("game:bricks")),
            grid("grid|unbricks|0", ["X"], {"X": ["game:bricks"]}, st("game:brick", 4)),
        ])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:brick"], 0.5)
        self.assertAlmostEqual(val.value["game:bricks"], 2.0)

    def test_tools_are_not_consumed_but_add_a_fraction(self):
        # hammer (consume false) worth 10, toolFraction 0.1: ore 0.5 + 1.0.
        items = {c: item() for c in ("game:ore-tin", "game:gem", "game:hammer", "game:nugget")}
        ex = export(items, [
            grid("grid|hammer|0", ["G"], {"G": ["game:gem"]}, st("game:hammer")),
            grid("grid|nugget|0", ["HO"], {"H": ["game:hammer"], "O": ["game:ore-tin"]}, st("game:nugget"),
                 H={"isTool": True}),
        ])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:nugget"], 0.5 + 10 * 0.1)

    def test_returned_container_is_not_consumed(self):
        items = {c: item() for c in ("game:log", "game:gem", "game:out")}
        ex = export(items, [grid("grid|r|0", ["LG"], {"L": ["game:log"], "G": ["game:gem"]}, st("game:out"),
                                 G={"returned": st("game:gem")})])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:out"], 1.0 + 10 * 0.1)

    def test_excluded_recipes_are_not_routes(self):
        items = {c: item() for c in ("game:gem", "game:dust")}
        ex = export(items, [grid("grid|game:recipes/destroy/x.json|0", ["G"], {"G": ["game:gem"]}, st("game:dust", 100))])
        val, _ = self.solve(ex)
        self.assertNotIn("game:dust", val.value)

    # ------------------------------------------------------------ floor, overrides, fallbacks

    def test_floor_zero_under_a_gear_per_stack(self):
        items = {"game:stick": item(64), "game:log": item(1)}
        ex = export(items, [])
        val, _ = self.solve(ex)
        out = iv.table(ex, val)
        self.assertEqual(out["values"]["game:stick"], 0.01)  # 0.64 a stack
        self.assertIn("game:stick", out["floorZero"])
        self.assertNotIn("game:log", out["floorZero"])  # 1.0 a stack of one

    def test_override_wins_and_is_reported_below_its_ingredients(self):
        items = {c: item() for c in ("game:gem", "game:ring")}
        self.write_rules(overrides={"game:ring": 2.0})
        ex = export(items, [grid("grid|ring|0", ["G"], {"G": ["game:gem"]}, st("game:ring"))])
        val, rules = self.solve(ex)
        self.assertEqual(val.value["game:ring"], 2.0)
        self.assertEqual(val.source["game:ring"], "override")
        below = iv.below_ingredients(ex, val, rules)
        self.assertEqual([b[0] for b in below], ["game:ring"])

    def test_defaults_and_trader_prices_only_price_leaves(self):
        items = {
            "game:fruit-apple": item(),
            "game:lamp": {**item(), "sources": [{"type": "traderSells", "from": "game:trader", "price": 4, "quantity": {"avg": 2}}]},
            "game:mystery": item(),
        }
        val, _ = self.solve(export(items, []))
        self.assertAlmostEqual(val.value["game:fruit-apple"], 0.03)
        self.assertTrue(val.source["game:fruit-apple"].startswith("default:crops"))
        self.assertAlmostEqual(val.value["game:lamp"], 4 / 2 * 0.5)
        self.assertNotIn("game:mystery", val.value)

    def test_sibling_variant_takes_its_family_average(self):
        # Only the blue and red fired molds are made; the black one is a leaf of the same family.
        items = {c: item() for c in ("game:gem", "game:mold-blue-fired-axe", "game:mold-red-fired-axe",
                                     "game:mold-black-fired-axe", "game:other-black-fired-axe")}
        ex = export(items, [
            grid("grid|b|0", ["G"], {"G": ["game:gem"]}, st("game:mold-blue-fired-axe")),
            grid("grid|r|0", ["GG"], {"G": ["game:gem"]}, st("game:mold-red-fired-axe")),
        ])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:mold-black-fired-axe"], 15.0)
        self.assertTrue(val.source["game:mold-black-fired-axe"].startswith("default:siblings game:mold-*-fired-axe"))
        self.assertNotIn("game:other-black-fired-axe", val.value)  # the first segment must match

    # ------------------------------------------------------------ schematics on trade lists (#506)

    LISTS = {
        "trader-carpenter.json": """{
          // the pack's own list
          selling: { list: [
            { code: "seraphhorizons:schematic-windmill", stacksize: 1, price: 30, priceReason: "a gate" },
            { code: "game:schematic-glider", stacksize: 1, price: 20, priceReason: "a gate" },
            { code: "game:log", stacksize: 4 },
          ] },
          buying: { list: [
            { code: "game:schematic-glider", type: "item", stacksize: 1, price: 10, priceReason: "a gate" },
          ] }
        }""",
    }

    def write_lists(self):
        lists = self.dir / "tradelists"
        lists.mkdir(exist_ok=True)
        for name, text in self.LISTS.items():
            (lists / name).write_text(text)
        return lists

    def gated_export(self, *extra_items):
        # The windmill rotor, gated: 4 logs, a hammer (a tool) and the windmill schematic, kept (the
        # exporter writes a grid ingredient with consume false as extra.consumed false).
        items = {c: item() for c in ("game:log", "game:gem", "game:hammer", "game:windmillrotor-oak",
                                     "seraphhorizons:schematic-windmill", "game:schematic-glider",
                                     "seraphhorizons:schematic-unsold", *extra_items)}
        return export(items, [
            grid("grid|hammer|0", ["G"], {"G": ["game:gem"]}, st("game:hammer")),
            grid("grid|rotor|0", ["LL", "LL", "HS"],
                 {"L": ["game:log"], "H": ["game:hammer"], "S": ["seraphhorizons:schematic-windmill"]},
                 st("game:windmillrotor-oak"), H={"isTool": True}, S={"extra": {"consumed": False}}),
        ])

    def test_schematics_traders_sell_stay_unpriced_and_add_nothing_to_a_gated_recipe(self):
        # The trade lists sell and buy schematics, but they are free kept tools: never valued, and a
        # gated recipe costs 4 logs + the hammer's tool fraction (10 x 0.1), nothing for the schematic.
        ex = self.gated_export()
        val, rules = self.solve(ex)
        for code in ("seraphhorizons:schematic-windmill", "game:schematic-glider", "seraphhorizons:schematic-unsold"):
            self.assertNotIn(code, val.value)
        self.assertEqual(val.source["game:log"], "raw")
        self.assertAlmostEqual(val.value["game:windmillrotor-oak"], 4.0 + 1.0)
        self.assertNotIn("schematic", "\n".join(iv.explain(ex, val, rules, "game:windmillrotor-oak")))
        self.assertEqual(iv.below_ingredients(ex, val, rules), [])
        self.assertNotIn("schematic", iv.report(ex, val, rules)["sources"])

    def test_kept_ingredients_that_are_not_schematics_keep_the_tool_fraction(self):
        # consume false on a gem, and a gear cutter's master (role "kept"): kept, at the tool fraction.
        items = {c: item() for c in ("game:log", "game:gem", "game:out", "game:cut")}
        ex = export(items, [
            grid("grid|out|0", ["LG"], {"L": ["game:log"], "G": ["game:gem"]}, st("game:out"),
                 G={"extra": {"consumed": False}}),
            recipe("gearcutter|game:log|0", "gearcutter",
                   [st("game:log"), {**st("game:gem"), "role": "kept"}],
                   [[st("game:log")], [st("game:gem")]], [st("game:cut")]),
        ])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:out"], 1.0 + 10 * 0.1)
        self.assertAlmostEqual(val.value["game:cut"], 1.0 + 1.0 + 10 * 0.1)  # mod: +1 flat

    # ------------------------------------------------------------ scrap floor

    @staticmethod
    def destroy(name: str, code: str, out: dict, *more: dict, chisel: bool = True) -> dict:
        """A break-down recipe (excluded: /destroy): one code chiselled into the outputs."""
        ings = [{"key": "I", "code": code, "kind": "item", "quantity": 1}]
        slots = [[st(code)]]
        pattern = ["I"]
        if chisel:
            ings.insert(0, {"key": "C", "code": "game:chisel", "kind": "item", "quantity": 1, "isTool": True})
            slots.insert(0, [st("game:chisel")])
            pattern = ["CI"]
        return recipe(f"grid|game:recipes/destroy/{name}.json|0", "grid", ings, slots, [out, *more],
                      grid={"width": len(pattern[0]), "height": 1, "shapeless": False, "pattern": pattern})

    def test_scrap_floor_raises_a_found_item_to_five_times_its_salvage(self):
        # A gem (raw 10, no route makes it) chisels into 3 logs (3.0): at least 5 x 3 = 15. The chisel is kept.
        items = {c: item() for c in ("game:gem", "game:log", "game:chisel")}
        val, rules = self.solve(export(items, [self.destroy("gem", "game:gem", st("game:log", 3))]))
        self.assertAlmostEqual(val.value["game:gem"], 15.0)
        self.assertEqual(val.source["game:gem"], "floor:5x grid|game:recipes/destroy/gem.json|0")
        self.assertAlmostEqual(val.unfloored["game:gem"], 10.0)
        self.assertAlmostEqual(val.floors["game:gem"].salvage, 3.0)

    def test_scrap_floor_counts_byproducts_less_other_inputs_and_prices_an_unvalued_leaf(self):
        # A relic nothing prices breaks down, with a stick spent, into a log and 2 ore (0.5 each):
        # 1 + 1 - 0.01 = 1.99 salvage, 5 x 1.99.
        items = {c: item() for c in ("game:relic", "game:log", "game:stick", "game:ore-x")}
        r = self.destroy("relic", "game:relic", st("game:log"), st("game:ore-x", 2), chisel=False)
        r["ingredients"].append({"key": "S", "code": "game:stick", "kind": "item", "quantity": 1})
        r["variants"][0]["ingredients"].append([st("game:stick")])
        r["grid"]["pattern"] = ["IS"]
        val, _ = self.solve(export(items, [r]))
        self.assertAlmostEqual(val.value["game:relic"], 5 * 1.99)
        self.assertNotIn("game:relic", val.unfloored)

    def test_scrap_floor_raises_a_made_item_to_its_salvage(self):
        # A box made from a stick (0.01) breaks back down into a log (1.0): at least 1 x 1.0.
        items = {c: item() for c in ("game:stick", "game:log", "game:box", "game:chisel")}
        val, _ = self.solve(export(items, [
            grid("grid|box|0", ["S"], {"S": ["game:stick"]}, st("game:box")),
            self.destroy("box", "game:box", st("game:log")),
        ]))
        self.assertAlmostEqual(val.value["game:box"], 1.0)
        self.assertEqual(val.source["game:box"], "floor:1x grid|game:recipes/destroy/box.json|0")
        self.assertAlmostEqual(val.unfloored["game:box"], 0.01)

    def test_scrap_floor_never_lowers_a_value(self):
        # The gem (10) chisels into one log: 5 x 1 = 5 < 10, so it stays a raw at 10.
        items = {c: item() for c in ("game:gem", "game:log", "game:chisel")}
        val, _ = self.solve(export(items, [self.destroy("gem", "game:gem", st("game:log"))]))
        self.assertAlmostEqual(val.value["game:gem"], 10.0)
        self.assertEqual(val.source["game:gem"], "raw")
        self.assertAlmostEqual(val.floors["game:gem"].value, 5.0)

    def test_scrap_floor_propagates_up_the_routes_and_is_reported(self):
        # The gem rises to 15 (3 logs), so a ring of one gem rises from 10 to 15 with it, and a
        # crown of two rings from 20 to 30.
        items = {c: item() for c in ("game:gem", "game:log", "game:chisel", "game:ring", "game:crown")}
        ex = export(items, [
            self.destroy("gem", "game:gem", st("game:log", 3)),
            grid("grid|ring|0", ["G"], {"G": ["game:gem"]}, st("game:ring")),
            grid("grid|crown|0", ["RR"], {"R": ["game:ring"]}, st("game:crown")),
        ])
        val, rules = self.solve(ex)
        self.assertAlmostEqual(val.value["game:ring"], 15.0)
        self.assertAlmostEqual(val.value["game:crown"], 30.0)
        self.assertEqual(val.source["game:crown"], "grid|crown|0")
        raised = {e["code"]: e for e in iv.report(ex, val, rules)["raisedByFloor"]}
        self.assertEqual(list(raised), ["game:crown", "game:gem", "game:ring"])  # most raised first, ties by code
        self.assertEqual(raised["game:gem"], {"code": "game:gem", "old": 10.0, "value": 15.0, "salvage": 3.0,
                                              "multiplier": 5.0, "recipe": "grid|game:recipes/destroy/gem.json|0"})
        self.assertEqual(raised["game:crown"]["via"], "grid|crown|0")
        md = iv.report_markdown(iv.report(ex, val, rules), ex, val, [])
        self.assertIn("- `game:gem` 10.0 -> 15.0: salvage 3.0 x 5 (grid|game:recipes/destroy/gem.json|0)", md)
        self.assertIn("- `game:ring` 10.0 -> 15.0: via grid|ring|0", md)
        self.assertIn("scrap floor: 5 x salvage 3.0000 = 15.0000", "\n".join(iv.explain(ex, val, rules, "game:gem")))

    def test_scrap_floor_leaves_overrides_and_reports_them(self):
        items = {c: item() for c in ("game:gem", "game:log", "game:chisel", "game:ring")}
        self.write_rules(overrides={"game:gem": 2.0})
        ex = export(items, [
            self.destroy("gem", "game:gem", st("game:log", 3)),
            grid("grid|ring|0", ["G"], {"G": ["game:gem"]}, st("game:ring")),
        ])
        val, rules = self.solve(ex)
        self.assertEqual(val.value["game:gem"], 2.0)
        self.assertEqual(val.source["game:gem"], "override")
        self.assertAlmostEqual(val.value["game:ring"], 2.0)
        rep = iv.report(ex, val, rules)
        self.assertEqual(rep["raisedByFloor"], [])
        self.assertEqual(rep["overridesBelowFloor"], [{"code": "game:gem", "value": 2.0, "floor": 15.0, "multiplier": 5.0,
                                                       "recipe": "grid|game:recipes/destroy/gem.json|0"}])

    def test_scrap_floor_of_a_liquid_counts_portions(self):
        # A jug (a found item, raw 1) is broken into 2 L of oil, 0.02 a portion: 200 portions are 4
        # gears of salvage, so the jug is 20; the oil in the table is 2 a litre. Read as 2 items the
        # oil would give 0.04 and leave the jug at 1.
        raws = {**RAWS, "groups": {"test": {**RAWS["groups"]["test"], "game:jug": 1.0, "game:oilportion": 0.02}}}
        self.write_rules(raws=raws)
        items = {"game:jug": item(1), "game:oilportion": liquid(), "game:chisel": item()}
        ex = export(items, [self.destroy("jug", "game:jug", st("game:oilportion", litres=2))])
        val, rules = self.solve(ex)
        self.assertAlmostEqual(val.value["game:jug"], 20.0)
        self.assertEqual(iv.table(ex, val)["values"]["game:oilportion"], 2.0)
        # A liquid broken down: its salvage and floor are per portion, shown per litre.
        raws["groups"]["test"]["game:oilportion"] = 0.0001
        self.write_rules(raws=raws)
        ex = export(items, [self.destroy("oil", "game:oilportion", st("game:log"), chisel=False)])
        ex["recipes"][0]["variants"][0]["ingredients"][0] = [st("game:oilportion", litres=1)]
        items["game:log"] = item()
        val, rules = self.solve(ex)
        self.assertAlmostEqual(val.value["game:oilportion"], 5 * 1.0 / 100)  # a litre gives a log
        e = iv.report(ex, val, rules)["raisedByFloor"][0]
        self.assertEqual((e["code"], e["old"], e["value"], e["salvage"], e["perLitre"]),
                         ("game:oilportion", 0.01, 5.0, 1.0, True))

    def test_scrap_floor_ignores_salvage_priced_from_the_item_itself(self):
        # A gem breaks down into two dusts, but dust is made from gems (a gem makes one), so its
        # salvage feeds on itself: no floor, and no endless rise.
        items = {c: item() for c in ("game:gem", "game:dust", "game:chisel")}
        ex = export(items, [
            grid("grid|dust|0", ["G"], {"G": ["game:gem"]}, st("game:dust")),
            self.destroy("gem", "game:gem", st("game:dust", 2)),
        ])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:gem"], 10.0)
        self.assertAlmostEqual(val.value["game:dust"], 10.0)
        self.assertNotIn("game:gem", val.floors)

    def test_scrap_floor_of_two_items_breaking_into_each_other_settles(self):
        # Two found raws, each broken into two of the other: each is raised once (5 x 2 x 1), then
        # each salvage is priced from the item itself and the floor stops rising.
        raws = {**RAWS, "groups": {"test": {"game:a": 1.0, "game:b": 1.0}}}
        self.write_rules(raws=raws)
        items = {c: item() for c in ("game:a", "game:b", "game:chisel")}
        val, _ = self.solve(export(items, [self.destroy("a", "game:a", st("game:b", 2)),
                                           self.destroy("b", "game:b", st("game:a", 2))]))
        self.assertAlmostEqual(val.value["game:a"], 10.0)
        self.assertAlmostEqual(val.value["game:b"], 10.0)
        self.assertLess(val.floor_rounds, iv.MAX_FLOOR_ROUNDS)

    def test_no_scrap_floor_without_its_rule(self):
        markups = {k: v for k, v in MARKUPS.items() if k != "scrapFloor"}
        self.write_rules(markups=markups)
        items = {c: item() for c in ("game:gem", "game:log", "game:chisel")}
        val, _ = self.solve(export(items, [self.destroy("gem", "game:gem", st("game:log", 3))]))
        self.assertAlmostEqual(val.value["game:gem"], 10.0)
        self.assertEqual(val.floors, {})

    def test_check_with_and_without_the_mod(self):
        lists = self.write_lists()
        (lists / "trader-smith.json").write_text("""{
          buying: { list: [ { code: "game:ingot", type: "item" } ] }
        }""")
        # With the mod: the schematics are in the export.
        path = self.dir / "with.json"
        path.write_text(json.dumps(self.gated_export("game:ingot")))
        args = ("--rules", str(self.dir), "--tradelists", str(lists),
                "--table", str(self.dir / "absent.json"))
        code, text, err = self.run_cli("check", str(path), *args)
        self.assertEqual(code, 1)  # game:ingot has no value
        self.assertIn("validated 2 trade list items priced from values (2 distinct codes) in 2 lists", text)
        self.assertIn("trader-smith.json: game:ingot has no value", err)
        self.assertNotIn("glider", err)
        # Without the mod: no seraphhorizons codes in the export, and the check still runs.
        ex = self.gated_export("game:ingot")
        ex["items"] = {c: v for c, v in ex["items"].items() if not c.startswith("seraphhorizons:")}
        ex["recipes"] = [r for r in ex["recipes"] if r["id"] != "grid|rotor|0"]
        ex["items"]["game:ingot"]["sources"] = [{"type": "traderBuys", "price": 2, "quantity": {"avg": 1}}]
        path = self.dir / "without.json"
        path.write_text(json.dumps(ex))
        code, text, err = self.run_cli("check", str(path), *args)
        self.assertEqual(code, 0, err)
        self.assertIn("validated 2 trade list items", text)
        self.assertIn("every trade list item has a value", text)

    # ------------------------------------------------------------ CLI

    def run_cli(self, *args) -> tuple[int, str, str]:
        out, err = io.StringIO(), io.StringIO()
        with redirect_stdout(out), redirect_stderr(err):
            code = iv.main(list(args))
        return code, out.getvalue(), err.getvalue()

    def test_build_explain_and_check(self):
        items = {c: item() for c in ("game:log", "game:stick", "game:plank", "game:ore-tin")}
        ex = export(items, [grid("grid|p|0", ["LS"], {"L": ["game:log"], "S": ["game:stick"]}, st("game:plank", 4))])
        path = self.dir / "recipes.json"
        path.write_text(json.dumps(ex))
        out = self.dir / "out" / "item-values.json"
        code, _, _ = self.run_cli("build", str(path), "--rules", str(self.dir), "--out", str(out),
                                  "--report", str(self.dir / "report.md"))
        self.assertEqual(code, 0)
        table = json.loads(out.read_text())
        self.assertAlmostEqual(table["values"]["game:plank"], round(1.01 / 4, 3))
        self.assertIn("## Coverage per domain", (self.dir / "report.md").read_text())

        code, text, _ = self.run_cli("explain", str(path), "game:plank", "--rules", str(self.dir))
        self.assertIn("1 x game:log", text)
        self.assertIn("/ 4 [grid]", text)

        lists = self.dir / "tradelists"
        code, text, _ = self.run_cli("check", str(path), "--rules", str(self.dir), "--tradelists", str(lists), "--table", str(out))
        self.assertEqual(code, 0)
        self.assertIn("nothing to check", text)

        lists.mkdir()
        (lists / "smith.json").write_text("""{
          // vanilla style
          money: { avg: 20 },
          selling: { list: [
            { code: "plank", type: "item", stacksize: 4 },
            { code: "game:soldonly", type: "item" },
            { code: "game:cast", type: "item", playerSupplied: true },
            { code: "game:special", type: "item", price: 7, priceReason: "found, never made" },
            { code: "game:noreason", type: "item", price: 7 },
          ] },
          buying: { list: [
            { code: "game:unobtainium", type: "item" },
            { code: "game:ore-copper", type: "item" },
          ] }
        }""")
        code, _, err = self.run_cli("check", str(path), "--rules", str(self.dir), "--tradelists", str(lists), "--table", str(out))
        self.assertEqual(code, 1)
        self.assertIn("smith.json: game:unobtainium has no value", err)
        # Every entry is priced from its value, sold or bought...
        self.assertIn("smith.json: game:cast has no value", err)
        self.assertIn("smith.json: game:soldonly has no value", err)
        # ...but a price override with its reason; one without a reason is no override.
        self.assertNotIn("game:special", err)
        self.assertIn("smith.json: game:noreason has no value", err)
        # A code missing from the table takes its family's value, as the mod looks it up.
        self.assertNotIn("plank", err)
        self.assertNotIn("ore-copper", err)

    def test_family_prefixes_match_the_mod(self):
        # ItemValues.FamilyPrefixes: longest first, never past the path's first segment.
        self.assertEqual(list(iv.family_prefixes("game:axe-felling-silver")), ["game:axe-felling-", "game:axe-"])
        self.assertEqual(list(iv.family_prefixes("game:plank")), [])

    # ------------------------------------------------------------ lotteries

    def lottery_items(self, *extra):
        return {c: item() for c in ("sh:gear-oiled", "sh:gear-steel", "game:metalbit-steel", "game:log", "game:gem", *extra)}

    def test_lottery_winner_is_priced_with_the_losers_credited(self):
        # The oiled gear: 1 in 10 a steel gear, else one steel bit. Oiled 2.0, a bit 0.5 (raws):
        # steel gear = (2.0 - 0.9 x 0.5) / 0.1 = 15.5, i.e. 10 x oiled - 9 x bit.
        raws = json.loads(json.dumps(RAWS))
        raws["groups"]["test"].update({"sh:gear-oiled": 2.0, "game:metalbit-steel": 0.5})
        self.write_rules(raws=raws)
        ex = export(self.lottery_items(), [lottery("lottery|sh:gear-oiled|0", "sh:gear-oiled",
                                                   [(0.1, [st("sh:gear-steel")]), (0.9, [st("game:metalbit-steel")])])])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["sh:gear-steel"], 15.5)
        self.assertAlmostEqual(val.value["sh:gear-steel"], 10 * 2.0 - 9 * 0.5)
        self.assertEqual(val.source["sh:gear-steel"], "lottery|sh:gear-oiled|0")

    def test_lottery_markup_quantities_and_several_losers(self):
        # Through the 'mod' kind (+1 flat) with a lottery type of its own markup removed: an item
        # worth 4 gives 2 gems' worth (p 0.5, 2 each), else 3 sticks (p 0.25) or nothing (p 0.25).
        # gem route: (4 + 1 - 0.25 x 3 x 0.01) / (0.5 x 2).
        markups = json.loads(json.dumps(MARKUPS))
        del markups["kinds"]["lottery"]
        raws = json.loads(json.dumps(RAWS))
        raws["groups"]["test"]["game:box"] = 4.0
        del raws["groups"]["test"]["game:gem"]
        self.write_rules(raws=raws, markups=markups)
        ex = export({c: item() for c in ("game:box", "game:gem", "game:stick")},
                    [lottery("lottery|game:box|0", "game:box",
                             [(0.5, [st("game:gem", 2)]), (0.25, [st("game:stick", 3)]), (0.25, [])])])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:gem"], (4 + 1 - 0.25 * 3 * 0.01) / 1.0)

    def test_lottery_waits_for_a_loser_valued_later(self):
        # The bit is made from a log (1.0) after the gear would otherwise settle: the gear waits for
        # it, as a route waits for its tools. oiled 2.0: (2.0 - 0.9 x 1.0) / 0.1 = 11.
        raws = json.loads(json.dumps(RAWS))
        raws["groups"]["test"]["sh:gear-oiled"] = 2.0
        self.write_rules(raws=raws)
        ex = export(self.lottery_items(), [
            lottery("lottery|sh:gear-oiled|0", "sh:gear-oiled",
                    [(0.1, [st("sh:gear-steel")]), (0.9, [st("game:metalbit-steel")])]),
            grid("grid|bit|0", ["L"], {"L": ["game:log"]}, st("game:metalbit-steel")),
        ])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:metalbit-steel"], 1.0)
        self.assertAlmostEqual(val.value["sh:gear-steel"], 11.0)

    def test_lottery_loser_priced_only_through_the_winner_does_not_deadlock(self):
        # Bits only from the steel gear (a grid makes 4) and the gear only from the lottery, which
        # waits for the bits: a knot. The cheapest is settled without waiting (no credit: 20),
        # then the bits from it (5); the bits' own lottery route, (2 - 0.1 x 20) / 0.9 = 0, is
        # cheaper still but the bits were not valued when the gear settled, so it never waited.
        raws = json.loads(json.dumps(RAWS))
        raws["groups"]["test"]["sh:gear-oiled"] = 2.0
        self.write_rules(raws=raws)
        ex = export(self.lottery_items(), [
            lottery("lottery|sh:gear-oiled|0", "sh:gear-oiled",
                    [(0.1, [st("sh:gear-steel")]), (0.9, [st("game:metalbit-steel")])]),
            grid("grid|bits|0", ["G"], {"G": ["sh:gear-steel"]}, st("game:metalbit-steel", 4)),
        ])
        val, _ = self.solve(ex)
        self.assertIn("sh:gear-steel", val.value)
        self.assertIn("game:metalbit-steel", val.value)

    def test_lottery_value_is_floored_at_zero(self):
        # Losers worth more than the input: the winner is worth nothing, never negative.
        raws = json.loads(json.dumps(RAWS))
        raws["groups"]["test"].update({"sh:gear-oiled": 1.0, "game:metalbit-steel": 5.0})
        self.write_rules(raws=raws)
        ex = export(self.lottery_items(), [lottery("lottery|sh:gear-oiled|0", "sh:gear-oiled",
                                                   [(0.1, [st("sh:gear-steel")]), (0.9, [st("game:metalbit-steel")])])])
        val, _ = self.solve(ex)
        self.assertEqual(val.value["sh:gear-steel"], 0.0)

    def test_explain_shows_the_credited_outcomes(self):
        raws = json.loads(json.dumps(RAWS))
        raws["groups"]["test"].update({"sh:gear-oiled": 2.0, "game:metalbit-steel": 0.5})
        self.write_rules(raws=raws)
        ex = export(self.lottery_items(), [lottery("lottery|sh:gear-oiled|0", "sh:gear-oiled",
                                                   [(0.1, [st("sh:gear-steel")]), (0.9, [st("game:metalbit-steel")])])])
        val, rules = self.solve(ex)
        text = "\n".join(iv.explain(ex, val, rules, "sh:gear-steel"))
        self.assertIn("other outcome 0.9 x game:metalbit-steel = 0.4500, credited", text)
        self.assertIn("- 0.4500) / 0.1 [lottery] = 15.5000", text)

    def test_route_with_a_tool_dearer_than_its_output_still_wins(self):
        # A frame worth 10 (a gem) is the station of a machine turning a log into a gear: 1 + 10 x
        # 0.1 = 2. The gear's other route costs 5 (five logs). Cheapest first, the gear would settle
        # at 5 before the frame is valued; the second pass takes the frame at its first-pass value.
        items = {c: item() for c in ("game:log", "game:gem", "sh:frame", "sh:gear")}
        ex = export(items, [
            grid("grid|frame|0", ["G"], {"G": ["game:gem"]}, st("sh:frame")),
            grid("grid|dear|0", ["LLLLL"], {"L": ["game:log"]}, st("sh:gear")),
            grid("grid|machine|0", ["LF"], {"L": ["game:log"], "F": ["sh:frame"]}, st("sh:gear"),
                 F={"role": "station"}),
        ])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["sh:gear"], 2.0)
        self.assertEqual(val.source["sh:gear"], "grid|machine|0")

    def test_a_machines_kept_part_is_not_consumed(self):
        # The gear cutter's master (machine.kept, role kept) is fitted, not used up: it adds the
        # tool fraction only, and a master nothing values does not block the route.
        items = {c: item() for c in ("game:log", "game:gem", "sh:gear", "sh:master")}
        ex = export(items, [recipe("gearcutter|b|0", "gearcutter",
                                   [st("game:log"), st("game:gem", role="kept"), st("sh:master", role="kept")],
                                   [[st("game:log")], [st("game:gem")], [st("sh:master")]], [st("sh:gear")],
                                   machine={"kept": [1, 2]})])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["sh:gear"], 1.0 + 1.0 + 10 * 0.1)  # mod: +1 flat

    # ------------------------------------------------------------ schematics

    def test_schematic_is_a_free_kept_tool_and_never_valued(self):
        # A gated grid recipe: a log and the machine's schematic (kept) make a hub. The schematic
        # adds nothing and does not block the route, though nothing values it; and it gets no value
        # itself, not from a raw pattern, a trader price or a recipe that makes it.
        raws = json.loads(json.dumps(RAWS))
        raws["groups"]["test"]["game:*-schematic-*"] = 3.0
        self.write_rules(raws=raws)
        items = {c: item() for c in ("game:log", "game:hub", "sh:schematic-hub", "game:br-schematic-door")}
        items["sh:schematic-hub"]["sources"] = [{"type": "traderSells", "price": 30, "quantity": {"avg": 1}}]
        ex = export(items, [
            grid("grid|hub|0", ["LS"], {"L": ["game:log"], "S": ["sh:schematic-hub"]}, st("game:hub"),
                 S={"isTool": True}),
            grid("grid|copy|0", ["L"], {"L": ["game:log"]}, st("sh:schematic-hub")),
        ])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:hub"], 1.0)
        self.assertNotIn("sh:schematic-hub", val.value)
        self.assertNotIn("game:br-schematic-door", val.value)

    def test_schematic_slot_as_a_consumed_ingredient_is_also_free(self):
        items = {c: item() for c in ("game:log", "game:hub", "sh:schematic-hub")}
        ex = export(items, [grid("grid|hub|0", ["LS"], {"L": ["game:log"], "S": ["sh:schematic-hub"]}, st("game:hub"))])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:hub"], 1.0)

    # ------------------------------------------------------------ switches

    def test_switches_from_recipe_item_and_inputs(self):
        # blank: a grid owned by GearBlanks; cut gear: the cutter (owned by GearCutter) from the
        # blank, so both; kit: an item GearCutter adds, from a log by a plain grid; plank: plain.
        items = {c: item() for c in ("game:log", "sh:blank", "sh:gear", "sh:kit", "game:plank")}
        items["sh:kit"]["switch"] = "GearCutter"
        ex = export(items, [
            grid("grid|blank|0", ["L"], {"L": ["game:log"]}, st("sh:blank"), ) | {"switch": "GearBlanks"},
            recipe("gearcutter|sh:blank|0", "gearcutter", [st("sh:blank")], [[st("sh:blank")]], [st("sh:gear")],
                   switch="GearCutter"),
            grid("grid|kit|0", ["L"], {"L": ["game:log"]}, st("sh:kit")),
            grid("grid|plank|0", ["L"], {"L": ["game:log"]}, st("game:plank", 4)),
        ])
        val, _ = self.solve(ex)
        self.assertEqual(val.switches["sh:blank"], ["GearBlanks"])
        self.assertEqual(val.switches["sh:gear"], ["GearBlanks", "GearCutter"])
        self.assertEqual(val.switches["sh:kit"], ["GearCutter"])
        self.assertNotIn("game:plank", val.switches)
        self.assertNotIn("game:log", val.switches)
        out = iv.table(ex, val)
        self.assertEqual(out["switches"], {"sh:blank": ["GearBlanks"], "sh:gear": ["GearBlanks", "GearCutter"],
                                           "sh:kit": ["GearCutter"]})

    def test_switch_of_a_route_not_taken_does_not_count(self):
        # The gear has a plain route (2 logs) and a cheaper-looking switched one that is dearer.
        items = {c: item() for c in ("game:log", "game:gem", "sh:gear")}
        ex = export(items, [
            grid("grid|plain|0", ["LL"], {"L": ["game:log"]}, st("sh:gear")),
            grid("grid|switched|0", ["G"], {"G": ["game:gem"]}, st("sh:gear")) | {"switch": "GearCutter"},
        ])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["sh:gear"], 2.0)
        self.assertNotIn("sh:gear", val.switches)

    def test_switches_through_tools_and_credited_losers(self):
        # A switched tool and a lottery loser made by a switched recipe both carry their switch.
        raws = json.loads(json.dumps(RAWS))
        raws["groups"]["test"]["sh:gear-oiled"] = 2.0
        self.write_rules(raws=raws)
        items = self.lottery_items("sh:tool", "game:thing")
        ex = export(items, [
            lottery("lottery|sh:gear-oiled|0", "sh:gear-oiled",
                    [(0.1, [st("sh:gear-steel")]), (0.9, [st("game:metalbit-steel")])]),
            grid("grid|bit|0", ["L"], {"L": ["game:log"]}, st("game:metalbit-steel")) | {"switch": "GearReclamation"},
            grid("grid|tool|0", ["G"], {"G": ["game:gem"]}, st("sh:tool")) | {"switch": "Tools"},
            grid("grid|thing|0", ["TL"], {"T": ["sh:tool"], "L": ["game:log"]}, st("game:thing"), T={"isTool": True}),
        ])
        val, _ = self.solve(ex)
        self.assertEqual(val.switches["sh:gear-steel"], ["GearReclamation"])
        self.assertEqual(val.switches["game:thing"], ["Tools"])

    def test_table_writes_switches_one_per_line_after_floor_zero(self):
        items = {c: item() for c in ("game:log", "sh:blank")}
        ex = export(items, [grid("grid|blank|0", ["L"], {"L": ["game:log"]}, st("sh:blank")) | {"switch": "GearBlanks"}])
        val, _ = self.solve(ex)
        path = self.dir / "t.json"
        iv.write_table(path, iv.table(ex, val))
        text = path.read_text()
        self.assertEqual(json.loads(text)["switches"], {"sh:blank": ["GearBlanks"]})
        self.assertIn('\n  "switches": {\n    "sh:blank": ["GearBlanks"]\n  }\n}\n', text)
        self.assertLess(text.index('"floorZero"'), text.index('"switches"'))

    # ------------------------------------------------------------ check

    def check_fixture(self):
        items = {c: item() for c in ("game:log", "game:stick", "game:plank")}
        ex = export(items, [grid("grid|p|0", ["LS"], {"L": ["game:log"], "S": ["game:stick"]}, st("game:plank", 4))])
        path = self.dir / "recipes.json"
        path.write_text(json.dumps(ex))
        out = self.dir / "item-values.json"
        self.run_cli("build", str(path), "--rules", str(self.dir), "--out", str(out), "--report", str(self.dir / "r.md"))
        return path, out

    def test_check_fails_on_a_stale_table_and_says_how_to_rebuild(self):
        path, out = self.check_fixture()
        lists = self.dir / "none"
        code, text, _ = self.run_cli("check", str(path), "--rules", str(self.dir), "--tradelists", str(lists), "--table", str(out))
        self.assertEqual(code, 0)
        self.assertIn("the shipped table matches this export", text)

        table = json.loads(out.read_text())
        table["values"]["game:plank"] = 9.0
        table["values"]["game:gone"] = 1.0
        for i in range(30):
            table["values"][f"game:zz-{i:02}"] = 1.0
        table["floorZero"].remove("game:stick")
        table["switches"] = {"game:log": ["GearCutter"]}
        out.write_text(json.dumps(table))
        code, _, err = self.run_cli("check", str(path), "--rules", str(self.dir), "--tradelists", str(lists), "--table", str(out))
        self.assertEqual(code, 1)
        self.assertIn("is stale: 34 differences", err)
        self.assertIn("game:plank: 9.0 -> 0.253", err)
        self.assertIn("game:gone: 1.0 -> no value", err)
        self.assertIn("... and 9 more", err)
        self.assertIn(f"python3 tools/item-values/itemvalues.py build {path}", err)

    def test_check_drift_names_floor_zero_and_switch_changes(self):
        fresh = {"values": {"a": 1}, "floorZero": ["a"], "switches": {"a": ["X"]}}
        shipped = {"values": {"a": 1}, "floorZero": [], "switches": {}}
        self.assertEqual(iv.table_drift(fresh, shipped), ["a: floorZero added", "a: switches [] -> ['X']"])
        self.assertEqual(iv.table_drift(fresh, fresh), [])

    # ------------------------------------------------------------ liquids per litre

    def liquid_export(self):
        # A log (1.0) is pressed into 0.8 L (80 portions) of juice: (1.0 + 0.5) / 80 = 0.01875 a
        # portion, 1.875 a litre. An oil with 50 items a litre (read, not assumed) is a raw at 0.01:
        # 0.5 a litre. "game:fakeportion" is named like a liquid but not marked one: per item.
        raws = {**RAWS, "groups": {"test": {**RAWS["groups"]["test"], "game:oilportion": 0.01,
                                            "game:fakeportion": 0.0012345, "game:dyeportion": 0.0001}}}
        self.write_rules(raws=raws)
        items = {"game:log": item(1, extra={"juicing": {"litresPerItem": 0.8, "output": st("game:juiceportion", 1, litres=0.01)}}),
                 "game:juiceportion": liquid(), "game:oilportion": liquid(per_litre=50),
                 "game:fakeportion": item(5000), "game:dyeportion": liquid(),
                 "game:waterportion": liquid()}  # no value: not listed in perLitre either
        return export(items, [])

    def test_table_stores_liquids_in_gears_per_litre(self):
        ex = self.liquid_export()
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:juiceportion"], 0.01875)  # the solver stays per portion
        out = iv.table(ex, val)
        self.assertEqual(out["values"]["game:juiceportion"], 1.875)
        self.assertEqual(out["values"]["game:oilportion"], 0.5)
        self.assertEqual(out["values"]["game:fakeportion"], 0.001)  # per item, 3 decimals
        self.assertEqual(out["values"]["game:dyeportion"], 0.01)  # 0.0001 a portion survives as 0.01 a litre
        self.assertEqual(out["perLitre"], {"game:dyeportion": 100, "game:juiceportion": 100, "game:oilportion": 50})
        self.assertIn("per litre", out["about"])
        # floorZero is per item x stack: 0.01875 x 5000 = 93.75 a stack; dye 0.0001 x 5000 = 0.5.
        self.assertNotIn("game:juiceportion", out["floorZero"])
        self.assertIn("game:dyeportion", out["floorZero"])
        self.assertEqual(iv.per_litre(export({"game:log": item()}, [])), {})
        # Under one item a litre is no liquid (world water: 0.001, written 0); 70 and 5 are read as is.
        odd = {"game:water-still-7": liquid(per_litre=0), "game:tiny": liquid(per_litre=0.001),
               "hod:wellwater": liquid(per_litre=70), "game:lard": liquid(per_litre=5)}
        self.assertEqual(iv.per_litre(export(odd, [])), {"hod:wellwater": 70, "game:lard": 5})

    def test_table_writes_per_litre_one_per_line_between_floor_zero_and_switches(self):
        ex = self.liquid_export()
        val, _ = self.solve(ex)
        path = self.dir / "t.json"
        iv.write_table(path, iv.table(ex, val))
        text = path.read_text()
        self.assertIn('\n  "perLitre": {\n    "game:dyeportion": 100,\n    "game:juiceportion": 100,\n'
                      '    "game:oilportion": 50\n  },\n  "switches": {}\n}\n', text)
        self.assertEqual(list(json.loads(text))[-4:], ["values", "floorZero", "perLitre", "switches"])
        # No liquid: an empty object, still in its place.
        ex = export({"game:log": item()}, [])
        val, _ = self.solve(ex)
        iv.write_table(path, iv.table(ex, val))
        self.assertIn('\n  "perLitre": {},\n  "switches": {}\n}\n', path.read_text())

    def test_recipe_litres_count_the_liquids_own_items_per_litre(self):
        # Hardened lard is 5 items a litre: 0.2 L in a recipe is 1 item (0.5 gears), not 20. A
        # liquid the export does not mark counts 100 a litre. The barrel's 1 gear flat spreads over
        # at least 1 L of its output: 5 items of lard-oil (a litre at 5), so 0.2 a lard-oil.
        markups = {**MARKUPS, "kinds": {**MARKUPS["kinds"], "barrel": {"pct": 0.0, "flat": 1.0, "minBatchLitres": 1}}}
        raws = {**RAWS, "groups": {"test": {**RAWS["groups"]["test"], "game:lard": 0.5, "game:water": 0.001}}}
        self.write_rules(raws=raws, markups=markups)
        items = {"game:lard": liquid(32, per_litre=5), "game:water": item(5000),
                 "game:lardoil": liquid(32, per_litre=5), "game:soap": item()}
        ex = export(items, [
            recipe("barrel|soap|0", "barrel", [st("game:lard", litres=0.2), st("game:water", litres=0.5)],
                   [[st("game:lard", 1, litres=0.2)], [st("game:water", 50, litres=0.5)]], [st("game:soap")]),
            recipe("barrel|oil|0", "barrel", [st("game:lard", litres=0.2)],
                   [[st("game:lard", 1, litres=0.2)]], [st("game:lardoil", 1, litres=0.2)]),
        ])
        val, _ = self.solve(ex)
        self.assertAlmostEqual(val.value["game:soap"], 0.5 + 50 * 0.001 + 1.0)
        self.assertAlmostEqual(val.value["game:lardoil"], 0.5 + 0.2)
        self.assertEqual(iv.table(ex, val)["values"]["game:lardoil"], 3.5)  # 0.7 x 5 a litre

    def test_check_drift_names_per_litre_changes(self):
        fresh = {"values": {"a": 1}, "floorZero": [], "perLitre": {"a": 100, "b": 50}, "switches": {}}
        shipped = {"values": {"a": 1}, "floorZero": [], "perLitre": {"b": 100, "c": 100}, "switches": {}}
        self.assertEqual(iv.table_drift(fresh, shipped),
                         ["a: perLitre none -> 100", "b: perLitre 100 -> 50", "c: perLitre 100 -> none"])
        # A table from before perLitre is stale against a rebuild that has liquids.
        self.assertEqual(iv.table_drift(fresh, {k: v for k, v in fresh.items() if k != "perLitre"}),
                         ["a: perLitre none -> 100", "b: perLitre none -> 50"])

    def test_check_fails_on_a_table_with_liquids_per_portion(self):
        ex = self.liquid_export()
        path = self.dir / "recipes.json"
        path.write_text(json.dumps(ex))
        out = self.dir / "item-values.json"
        self.run_cli("build", str(path), "--rules", str(self.dir), "--out", str(out), "--report", str(self.dir / "r.md"))
        args = ("--rules", str(self.dir), "--tradelists", str(self.dir / "none"), "--table", str(out))
        code, text, err = self.run_cli("check", str(path), *args)
        self.assertEqual(code, 0, err)
        # The old shape: per portion, no perLitre.
        table = json.loads(out.read_text())
        table["values"]["game:juiceportion"] = 0.019
        del table["perLitre"]
        out.write_text(json.dumps(table))
        code, _, err = self.run_cli("check", str(path), *args)
        self.assertEqual(code, 1)
        self.assertIn("is stale: 4 differences", err)
        self.assertIn("game:juiceportion: 0.019 -> 1.875", err)
        self.assertIn("game:oilportion: perLitre none -> 50", err)

    def test_explain_prints_a_liquid_per_litre(self):
        ex = self.liquid_export()
        val, rules = self.solve(ex)
        text = "\n".join(iv.explain(ex, val, rules, "game:juiceportion"))
        self.assertIn("game:juiceportion (x) = 1.8750 gears/L (0.01875 a portion, 100 portions a litre)", text)
        self.assertIn("/ 80 [pressing] = 0.01875 a portion = 1.8750 gears/L", text)
        self.assertIn("game:log (x) = 1.0000 gears/item", text)
        self.assertIn("game:oilportion (x) = 0.5000 gears/L",
                      "\n".join(iv.explain(ex, val, rules, "game:oilportion")))
        self.assertIn("game:fakeportion (x) = 0.0012 gears/item",
                      "\n".join(iv.explain(ex, val, rules, "game:fakeportion")))
        # A per-item charge on a liquid's route is a portion's.
        items = {"game:log": item(), "game:ciderportion": liquid(), "game:wineportion": liquid()}
        ex = export(items, [
            recipe("curing|game:log|0", "curing", [st("game:log")], [[st("game:log")]],
                   [st("game:ciderportion", 100, litres=1)]),
            recipe("curing|game:ciderportion|0", "curing", [st("game:ciderportion")], [[st("game:ciderportion")]],
                   [st("game:wineportion")]),
        ])
        val, rules = self.solve(ex)
        text = "\n".join(iv.explain(ex, val, rules, "game:wineportion"))
        self.assertIn("+ 0.01 a portion [curing] = 0.03 a portion = 3.0000 gears/L", text)

    def test_report_ranks_and_prints_liquids_per_litre(self):
        ex = self.liquid_export()
        ex["items"]["game:stick"] = item(1)  # 0.01 an item: dearer per item than the juice's portion
        val, rules = self.solve(ex)
        rep = iv.report(ex, val, rules)
        most = [e["code"] for e in rep["mostValuable"]]
        # Juice 1.875 a litre ranks above the log (1.0); per portion (0.019) it would rank below the stick.
        self.assertEqual(most[:2], ["game:juiceportion", "game:log"])
        self.assertLess(most.index("game:stick"), most.index("game:fakeportion"))
        self.assertEqual(rep["mostValuable"][0], {"code": "game:juiceportion", "value": 1.875, "perLitre": True})
        self.assertNotIn("perLitre", rep["mostValuable"][1])
        md = iv.report_markdown(rep, ex, val, ["game:juiceportion", "game:log"])
        self.assertIn("- `game:juiceportion` 1.875/L", md)
        self.assertIn("- `game:log` 1.0\n", md)
        self.assertIn("| `game:juiceportion` | 1.875/L | 5000 |", md)

    def test_check_fails_on_a_retired_item_on_a_trade_list(self):
        path, out = self.check_fixture()
        ex = json.loads(path.read_text())
        ex["items"]["game:pitsaw-iron"] = {**item(), "handbookVisible": False}
        ex["items"]["game:creature-goat"] = item()
        ex["items"]["sh:schematic-mill"] = item()
        path.write_text(json.dumps(ex))
        lists = self.dir / "tradelists"
        lists.mkdir()
        (lists / "carpenter.json").write_text("""{
          selling: { list: [
            { code: "game:pitsaw-iron", type: "item" },
            { code: "game:creature-goat", type: "item", price: 5, priceReason: "a creature, never made" },
            { code: "sh:schematic-mill", type: "item", price: 30, priceReason: "a gate" },
            { kind: "lead", code: "sh:traderlead", price: 1, priceReason: "the maps system's" },
          ] },
          buying: { list: [
            { code: "sh:schematic-mill", type: "item", price: 10, priceReason: "a gate" },
          ] }
        }""")
        code, text, err = self.run_cli("check", str(path), "--rules", str(self.dir), "--tradelists", str(lists), "--table", str(out))
        self.assertEqual(code, 1)
        self.assertIn("carpenter.json: game:pitsaw-iron (sold) is retired", err)
        self.assertIn("1 trade list entries name items nothing values", err)
        # A creature priced by hand and schematics with their price overrides need no value.
        self.assertNotIn("creature-goat", err)
        self.assertNotIn("schematic", err)

        (lists / "carpenter.json").write_text((lists / "carpenter.json").read_text().replace(
            '{ code: "game:pitsaw-iron", type: "item" },', ""))
        code, text, err = self.run_cli("check", str(path), "--rules", str(self.dir), "--tradelists", str(lists), "--table", str(out))
        self.assertEqual(code, 0, err)
        self.assertIn("every trade list item has a value", text)

        # A schematic without its price override has nothing to be priced by.
        (lists / "carpenter.json").write_text((lists / "carpenter.json").read_text().replace(
            'price: 10, priceReason: "a gate" ', ""))
        code, text, err = self.run_cli("check", str(path), "--rules", str(self.dir), "--tradelists", str(lists), "--table", str(out))
        self.assertEqual(code, 1)
        self.assertIn("carpenter.json: sh:schematic-mill is a schematic", err)


class ShippedRulesTest(unittest.TestCase):
    """The shipped rule files load, and the shipped table has the shape the mod reads."""

    def test_rules_load(self):
        rules = iv.Rules.load()
        self.assertEqual(rules.raws["game:gear-rusty"], 1)
        self.assertIn("grid", rules.markups)

    def test_steel_gears_are_derived_not_overridden(self):
        # The steel gear takes its cheapest route (the reclamation lottery or the gear cutter), and
        # the large gear its gear cutter route (#506, #523): neither is pinned by hand.
        rules = iv.Rules.load()
        self.assertNotIn("seraphhorizons:gear-steel", rules.overrides)
        self.assertNotIn("seraphhorizons:largegear-steel", rules.overrides)

    def test_shipped_table_values_no_schematic(self):
        # Schematics are free and never consumed: the table has none, though traders sell them.
        if not iv.DEFAULT_OUT.exists():
            self.skipTest("no shipped table")
        rules = iv.Rules.load()
        values = json.loads(iv.DEFAULT_OUT.read_text())["values"]
        self.assertEqual([c for c in values if rules.is_schematic(c)], [])

    def test_shipped_table_shape(self):
        if not iv.DEFAULT_OUT.exists():
            self.skipTest("no shipped table")
        table = json.loads(iv.DEFAULT_OUT.read_text())
        self.assertEqual(table["values"]["game:gear-rusty"], 1)
        self.assertTrue(set(table["floorZero"]) <= set(table["values"]))
        self.assertTrue(set(table.get("switches", {})) <= set(table["values"]))
        self.assertTrue(set(table["perLitre"]) <= set(table["values"]))
        self.assertEqual(list(table)[-4:], ["values", "floorZero", "perLitre", "switches"])
        self.assertEqual(list(table["perLitre"]), sorted(table["perLitre"]))
        self.assertIn("per litre", table["about"])

    def test_shipped_table_prices_liquids_per_litre(self):
        # Every liquid the pack has is 100 portions a litre; a litre of cider is worth tenths of a
        # gear, not the thousandths a portion is (the table's old unit).
        if not iv.DEFAULT_OUT.exists():
            self.skipTest("no shipped table")
        table = json.loads(iv.DEFAULT_OUT.read_text())
        litres = table["perLitre"]
        for code in ("game:juiceportion-apple", "game:ciderportion-apple", "game:spiritportion-apple",
                     "expandedfoods:foodoilportion-olive"):
            self.assertEqual(litres.get(code), 100, code)
        self.assertNotIn("game:gear-rusty", litres)
        self.assertGreater(table["values"]["game:ciderportion-apple"], 0.1)

    def test_schematic_patterns_cover_every_sold_schematic(self):
        # Trading/Schematics' list of every schematic in the pack (and Scrolled's rolled copies).
        gates = iv.REPO / "mods-src/seraphhorizons/assets/seraphhorizons/config/schematic-gates.json"
        sold = [e["code"] for e in iv._lenient_json(gates.read_text(encoding="utf-8"))["sold"]]
        self.assertTrue(sold)
        rules = iv.Rules.load()
        for pattern in sold:
            self.assertTrue(rules.is_schematic(pattern.replace("*", "x")), pattern)
        self.assertTrue(rules.is_schematic("scrolled:br-rolled-schematic-bed"))
        self.assertFalse(rules.is_schematic("purposefulstorage:schematicrack-normal-east"))

    def test_per_item_charges(self):
        # Ageing (curing: cider, wine, spirits, yogurt, jerky) charges per output item, so a litre
        # of cider costs more than its juice; cooling, drying and the barrel do not (a barrel's
        # cheap liquids, dyes and tannin, go into other things by the litre).
        rules = iv.Rules.load()
        self.assertGreater(rules.per_item("curing"), 0)
        for kind in ("transition", "barrel", "pressing", "distilling", "grid"):
            self.assertEqual(rules.per_item(kind), 0.0, kind)
        self.assertIn("pressing", rules.markups)
        self.assertIn("distilling", rules.markups)
        self.assertEqual(rules.markups["barrel"].get("minBatchLitres"), 1)

    def test_shipped_table_beverages_age_upwards(self):
        # Pressed juice < cider < Expanded Foods' strong < potent wine, each a litre's ageing apart
        # (all per litre).
        if not iv.DEFAULT_OUT.exists():
            self.skipTest("no shipped table")
        values = json.loads(iv.DEFAULT_OUT.read_text())["values"]
        chain = ["game:juiceportion-apple", "game:ciderportion-apple",
                 "expandedfoods:strongwineportion-apple", "expandedfoods:potentwineportion-apple"]
        got = [values[c] for c in chain]
        self.assertEqual(got, sorted(got), dict(zip(chain, got)))
        self.assertLess(got[0], got[1])

    def test_lottery_has_no_labour(self):
        # steel gear = 10 x oiled gear - 9 x what a lost one gives, exactly (#523).
        self.assertEqual(iv.Rules.load().markup("lottery"), (0.0, 0.0))


if __name__ == "__main__":
    unittest.main()
