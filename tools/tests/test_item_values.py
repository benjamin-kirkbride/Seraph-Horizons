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
        "mod": {"pct": 0.0, "flat": 1.0},
    },
    "excludeRecipes": ["/destroy"],
}


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

    # ------------------------------------------------------------ schematics (#506)

    GATES = """// schematic-gates.json style: // comments, globs in "sold"
{
  "replacement": "game:paper-parchment",
  "sold": [
    { "mod": "game", "code": "game:schematic-*" },
    { "mod": "seraphhorizons", "code": "seraphhorizons:schematic-*" }
  ],
  // a gate
  "gates": [ { "machine": "windmill", "outputs": [ { "mod": "game", "code": "game:windmillrotor-*" } ] } ]
}"""

    LISTS = {
        "trader-carpenter.json": """{
          // the pack's own list
          selling: { list: [
            { code: "seraphhorizons:schematic-windmill", stacksize: 1, price: { avg: 30, var: 3 } },
            { code: "game:schematic-glider", stacksize: 1, price: { avg: 20 } },
            { code: "game:log", stacksize: 4, price: { avg: 8 } },
          ] },
          buying: { list: [
            { code: "game:schematic-glider", type: "item", stacksize: 1, price: { avg: 10 } },
          ] }
        }""",
    }

    def write_pack(self):
        (self.dir / "gates.json").write_text(self.GATES)
        lists = self.dir / "tradelists"
        lists.mkdir(exist_ok=True)
        for name, text in self.LISTS.items():
            (lists / name).write_text(text)
        return self.dir / "gates.json", lists

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

    def test_schematics_are_valued_from_the_pack_trade_lists(self):
        gates, lists = self.write_pack()
        rules = iv.Rules.load(self.dir, gates, lists)
        self.assertEqual(rules.schematics, ["game:schematic-*", "seraphhorizons:schematic-*"])
        val = iv.solve(self.gated_export(), rules)
        # traderFallback (sells 0.5, buys 1.0 here): 30 x 0.5; the glider's mean of 20 x 0.5 and
        # 10 / 0.2 x 1.0 (a buying price is the final pay, value x the buy spread).
        self.assertAlmostEqual(val.value["seraphhorizons:schematic-windmill"], 15.0)
        self.assertEqual(val.source["seraphhorizons:schematic-windmill"], "schematic:trade lists")
        self.assertAlmostEqual(val.value["game:schematic-glider"], (10.0 + 50.0) / 2)
        # Only schematics: a log the trade lists price stays a raw.
        self.assertEqual(val.source["game:log"], "raw")
        # A schematic no trade list sells gets nothing from this rule (only the later sibling fallback).
        self.assertTrue(val.source["seraphhorizons:schematic-unsold"].startswith("default:siblings"))
        self.assertEqual(iv.report(self.gated_export(), val, rules)["sources"]["schematic"], 2)

    def test_kept_schematic_adds_nothing_to_a_gated_recipe(self):
        gates, lists = self.write_pack()
        rules = iv.Rules.load(self.dir, gates, lists)
        ex = self.gated_export()
        val = iv.solve(ex, rules)
        # 4 logs + the hammer's tool fraction (10 x 0.1); the schematic (15) adds 0, not 15 x 0.1.
        self.assertAlmostEqual(val.value["game:windmillrotor-oak"], 4.0 + 1.0)
        text = "\n".join(iv.explain(ex, val, rules, "game:windmillrotor-oak"))
        self.assertIn("kept schematic seraphhorizons:schematic-windmill: 0", text)
        self.assertEqual(iv.below_ingredients(ex, val, rules), [])

    def test_kept_schematic_needs_no_value(self):
        # No trade list prices it: the gated recipe still prices, by its consumed parts.
        gates, _ = self.write_pack()
        rules = iv.Rules.load(self.dir, gates, self.dir / "no-lists")
        val = iv.solve(self.gated_export(), rules)
        self.assertNotIn("seraphhorizons:schematic-windmill", val.value)
        self.assertAlmostEqual(val.value["game:windmillrotor-oak"], 5.0)

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

    def test_check_with_and_without_the_mod(self):
        gates, lists = self.write_pack()
        (lists / "trader-smith.json").write_text("""{
          buying: { list: [ { code: "game:ingot", type: "item", price: { avg: 2 } } ] }
        }""")
        # With the mod: the schematics are in the export.
        path = self.dir / "with.json"
        path.write_text(json.dumps(self.gated_export("game:ingot")))
        args = ("--rules", str(self.dir), "--gates", str(gates), "--tradelists", str(lists),
                "--table", str(self.dir / "absent.json"))
        code, text, err = self.run_cli("check", str(path), *args)
        self.assertEqual(code, 1)  # game:ingot has no value
        self.assertIn("validated 2 trade list items traders buy (2 distinct codes) in 2 lists", text)
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
        self.assertIn("every item traders buy has a value", text)

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
            { code: "plank", type: "item", stacksize: 4, price: { avg: 1 } },
            { code: "game:soldonly", type: "item", price: { avg: 5 } },
            { code: "game:cast", type: "item", price: { avg: 5 }, playerSupplied: true },
          ] },
          buying: { list: [
            { code: "game:unobtainium", type: "item", price: { avg: 5 } },
            { code: "game:ore-copper", type: "item", price: { avg: 1 } },
          ] }
        }""")
        code, _, err = self.run_cli("check", str(path), "--rules", str(self.dir), "--tradelists", str(lists), "--table", str(out))
        self.assertEqual(code, 1)
        self.assertIn("smith.json: game:unobtainium has no value", err)
        # Player-supplied goods are bought (off the list, at their value) as well as sold.
        self.assertIn("smith.json: game:cast has no value", err)
        # What a trader only sells is priced by its list.
        self.assertNotIn("soldonly", err)
        # A code missing from the table takes its family's value, as the mod looks it up.
        self.assertNotIn("plank", err)
        self.assertNotIn("ore-copper", err)

    def test_family_prefixes_match_the_mod(self):
        # ItemValues.FamilyPrefixes: longest first, never past the path's first segment.
        self.assertEqual(list(iv.family_prefixes("game:axe-felling-silver")), ["game:axe-felling-", "game:axe-"])
        self.assertEqual(list(iv.family_prefixes("game:plank")), [])


class ShippedRulesTest(unittest.TestCase):
    """The shipped rule files load, and the shipped table has the shape the mod reads."""

    def test_rules_load(self):
        rules = iv.Rules.load()
        self.assertEqual(rules.raws["game:gear-rusty"], 1)
        self.assertIn("grid", rules.markups)

    def test_buy_spread_matches_the_mod(self):
        # BUY_SPREAD is the mod's default buy spread: Pricing.DefaultBuySpread and the config default.
        import re
        src = iv.REPO / "mods-src/seraphhorizons"
        pricing = src / "Trading/Economy/Core/Pricing.cs"
        config = src / "SeraphHorizons.cs"
        if not pricing.exists() or not config.exists():
            self.skipTest("no mod source")
        m = re.search(r"DefaultBuySpread\s*=\s*([0-9.]+)", pricing.read_text(encoding="utf-8"))
        self.assertIsNotNone(m)
        self.assertEqual(float(m.group(1)), iv.BUY_SPREAD)
        m = re.search(r"double BuySpread\s*\{[^}]*\}\s*=\s*([^;]+);", config.read_text(encoding="utf-8"))
        self.assertIsNotNone(m)
        default = m.group(1).strip()
        self.assertIn(default, ("Pricing.DefaultBuySpread", repr(iv.BUY_SPREAD)))

    def test_steel_gear_override(self):
        # The reclamation line's cost (#506), its arithmetic in "why".
        rules = iv.Rules.load()
        self.assertEqual(rules.overrides["seraphhorizons:gear-steel"], 15)
        self.assertIn("14.66", iv.load_json(iv.HERE / "overrides.json")["why"]["seraphhorizons:gear-steel"])
        self.assertNotIn("seraphhorizons:largegear-steel", rules.overrides)

    def test_shipped_schematics_are_all_priced_by_the_trade_lists(self):
        # Every schematic a trader sells gets a value; the shipped gates file parses.
        if not iv.DEFAULT_GATES.exists() or not iv.DEFAULT_TRADELISTS.is_dir():
            self.skipTest("no gates file or trade lists")
        rules = iv.Rules.load(iv.HERE, iv.DEFAULT_GATES, iv.DEFAULT_TRADELISTS)
        self.assertIn("seraphhorizons:schematic-*", rules.schematics)
        sold = [c for c in rules.pack_trade if rules.is_schematic(c)]
        self.assertIn("seraphhorizons:schematic-windmill", sold)
        self.assertTrue(all(rules.pack_trade[c] > 0 for c in sold))

    def test_shipped_table_shape(self):
        if not iv.DEFAULT_OUT.exists():
            self.skipTest("no shipped table")
        table = json.loads(iv.DEFAULT_OUT.read_text())
        self.assertEqual(table["values"]["game:gear-rusty"], 1)
        self.assertTrue(set(table["floorZero"]) <= set(table["values"]))


if __name__ == "__main__":
    unittest.main()
