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
        "lottery": {"pct": 0.0, "flat": 0.0},
    },
    "excludeRecipes": ["/destroy"],
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
            { code: "game:pitsaw-iron", type: "item", price: { avg: 5 } },
            { code: "game:creature-goat", type: "item", price: { avg: 5 } },
            { code: "sh:schematic-mill", type: "item", price: { avg: 30 } },
            { kind: "lead", code: "sh:traderlead", price: { avg: 1 } },
          ] },
          buying: { list: [
            { code: "sh:schematic-mill", type: "item", price: { avg: 10 } },
          ] }
        }""")
        code, text, err = self.run_cli("check", str(path), "--rules", str(self.dir), "--tradelists", str(lists), "--table", str(out))
        self.assertEqual(code, 1)
        self.assertIn("carpenter.json: game:pitsaw-iron (sold) is retired", err)
        self.assertIn("1 trade list entries name items nothing values", err)
        # A creature a trader sells is priced by its list; schematics are bought at their list price.
        self.assertNotIn("creature-goat", err)
        self.assertNotIn("schematic", err)

        (lists / "carpenter.json").write_text((lists / "carpenter.json").read_text().replace(
            '{ code: "game:pitsaw-iron", type: "item", price: { avg: 5 } },', ""))
        code, text, err = self.run_cli("check", str(path), "--rules", str(self.dir), "--tradelists", str(lists), "--table", str(out))
        self.assertEqual(code, 0, err)
        self.assertIn("1 entries traders only sell have no value", text)


class ShippedRulesTest(unittest.TestCase):
    """The shipped rule files load, and the shipped table has the shape the mod reads."""

    def test_rules_load(self):
        rules = iv.Rules.load()
        self.assertEqual(rules.raws["game:gear-rusty"], 1)
        self.assertIn("grid", rules.markups)

    def test_shipped_table_shape(self):
        if not iv.DEFAULT_OUT.exists():
            self.skipTest("no shipped table")
        table = json.loads(iv.DEFAULT_OUT.read_text())
        self.assertEqual(table["values"]["game:gear-rusty"], 1)
        self.assertTrue(set(table["floorZero"]) <= set(table["values"]))
        self.assertTrue(set(table.get("switches", {})) <= set(table["values"]))
        self.assertEqual(list(table)[-3:], ["values", "floorZero", "switches"])

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

    def test_lottery_has_no_labour(self):
        # steel gear = 10 x oiled gear - 9 x what a lost one gives, exactly (#523).
        self.assertEqual(iv.Rules.load().markup("lottery"), (0.0, 0.0))


if __name__ == "__main__":
    unittest.main()
