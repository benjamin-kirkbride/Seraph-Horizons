#!/usr/bin/env python3
"""Item base values computed from the pack's recipe export (#449).

Stdlib only, Python 3.11+. See README.md next to this file for the rules and the numbers.

  python3 tools/item-values/itemvalues.py build   build/recipes.json   # writes the mod's item-values.json and a report
  python3 tools/item-values/itemvalues.py report  build/recipes.json   # the report only (stdout)
  python3 tools/item-values/itemvalues.py explain build/recipes.json game:pickaxe-tinbronze
  python3 tools/item-values/itemvalues.py check   build/recipes.json   # fails on a stale table, an unvalued trade list item, a retired item listed

Every item's value is the cheapest route to it: hand-priced raws (raw-values.json) and overrides
(overrides.json) are fixed; every other item is the cheapest of its recipes, where a recipe costs
its consumed ingredients (each slot at its cheapest accepted stack) times the kind's markup
(markups.json) plus a flat labour charge, less its other outputs' value (a lottery's losers),
divided by the output quantity, plus the kind's charge per output item (`perItem`: what ageing in
a barrel or a cellar adds to each portion, which a flat per batch of thousands cannot). Schematics are free kept tools. Items settle cheapest first, so
chains of any length and cycles resolve; the table also records the config switches each value
exists by. The scrap floor then raises an item to what breaking it down gives back (5 x for a found
item, 1 x for a made one; scrapFloor in markups.json), and what is made from it with it. The solver works per item (recipes count portions); the table stores liquids, the
codes the export marks `extra.liquid`, in gears per litre (`perLitre`).
"""

from __future__ import annotations

import argparse
import fnmatch
import heapq
import json
import math
import os
import re
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
DEFAULT_OUT = REPO / "mods-src/seraphhorizons/assets/seraphhorizons/config/item-values.json"
DEFAULT_TRADELISTS = REPO / "mods-src/seraphhorizons/assets/seraphhorizons/config/tradelists"

# Portions (liquids) are items: 100 per litre for every liquid in the game and the pack's mods. The
# solver converts recipes' litres with this; the table's per-litre values read the export's own
# items[code].attributes.extra.liquid.itemsPerLitre instead (per_litre).
ITEMS_PER_LITRE = 100
# Recipe types that never make anything worth pricing from: perishing makes rot, burning ash.
# Butchery is skipped as a route: one carcass gives a dozen outputs, and hides and meat are raws.
SKIPPED_TYPES = {"perishing", "burning", "butchery"}
EPS = 1e-9


# ------------------------------------------------------------------ inputs


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


@dataclass
class Rules:
    """raw-values.json, markups.json and overrides.json."""

    raws: dict[str, float]  # exact code -> gears per item
    raw_patterns: list[tuple[str, float, str]]  # (glob, gears, note), first match wins
    defaults: list[tuple[str, float, str]]  # (regex on path, gears, category) for unknown leaves
    markups: dict[str, dict]  # kind -> {pct, flat, perItem}
    tool_fraction: float
    exclude: list[re.Pattern]  # recipe ids never used as routes (uncrafting, recycling)
    voxels_per_unit: dict[str, dict]  # recipe type -> {match, voxels}
    overrides: dict[str, float]
    ores: dict = field(default_factory=dict)
    trader_fallback: dict = field(default_factory=dict)
    schematics: list[str] = field(default_factory=list)  # globs: kept, worth nothing, never block
    # The scrap floor's multipliers of salvage: {"found": x, "made": y}; none, no floor.
    scrap_floor: dict = field(default_factory=dict)

    @staticmethod
    def load(directory: Path = HERE) -> "Rules":
        raw = load_json(directory / "raw-values.json")
        mk = load_json(directory / "markups.json")
        ov = load_json(directory / "overrides.json") if (directory / "overrides.json").exists() else {}
        raws: dict[str, float] = {}
        patterns: list[tuple[str, float, str]] = []
        for group, entries in raw["groups"].items():
            for code, value in entries.items():
                if "*" in code:
                    patterns.append((code, float(value), group))
                else:
                    raws[code] = float(value)
        defaults = [(d["match"], float(d["value"]), d["category"]) for d in raw.get("defaults", [])]
        return Rules(
            raws=raws,
            raw_patterns=patterns,
            defaults=defaults,
            markups=mk["kinds"],
            tool_fraction=float(mk["toolFraction"]),
            exclude=[re.compile(p) for p in mk.get("excludeRecipes", [])],
            voxels_per_unit={k: v for k, v in mk.get("voxelsPerUnit", {}).items() if isinstance(v, dict)},
            overrides={k: float(v) for k, v in ov.get("values", {}).items()},
            ores=raw.get("ores", {}),
            trader_fallback=raw.get("traderFallback", {}),
            schematics=list(mk.get("schematics", [])),
            scrap_floor={k: float(v) for k, v in (mk.get("scrapFloor") or {}).items() if k in ("found", "made")},
        )

    def is_schematic(self, code: str) -> bool:
        return any(fnmatch.fnmatchcase(code, p) for p in self.schematics)

    def markup(self, kind: str) -> tuple[float, float]:
        m = self.markups.get(kind) or self.markups["mod"]
        return float(m.get("pct", 0)), float(m.get("flat", 0))

    def flat(self, route: "Route") -> float:
        """The kind's flat for this route's batch. With minBatchLitres, a recipe making a liquid is
        charged its flat over at least that many litres: a barrel recipe written for 0.1 L (mead)
        or one portion (cider) is run by the barrelful, not one labour charge per portion. With
        minBatchItems, a recipe making a solid likewise, over that many items or a stack."""
        m = self.markups.get(route.kind) or self.markups["mod"]
        flat = float(m.get("flat", 0))
        least = float(m.get("minBatchLitres", 0)) * route.liquid
        if route.liquid and route.quantity < least:
            flat *= route.quantity / least
        # minBatchItems: a recipe making a solid is charged its flat over at least that many items,
        # or the output's stack if smaller: a barrel washes or soaks a whole stack at once.
        items = min(float(m.get("minBatchItems", 0)), float(route.stack or 0))
        if not route.liquid and route.quantity < items:
            flat *= route.quantity / items
        return flat

    def per_item(self, kind: str) -> float:
        """Gears the kind adds to each output item, after the division by the output quantity."""
        m = self.markups.get(kind) or self.markups["mod"]
        return float(m.get("perItem", 0))

    def raw_value(self, code: str) -> tuple[float, str] | None:
        if code in self.raws:
            return self.raws[code], "raw"
        for pattern, value, group in self.raw_patterns:
            if fnmatch.fnmatchcase(code, pattern):
                return value, f"raw:{group} {pattern}"
        return None

    def default_value(self, code: str) -> tuple[float, str] | None:
        path = code.split(":", 1)[1]
        for rx, value, category in self.defaults:
            if re.search(rx, path):
                return value, f"default:{category}"
        return None


# ------------------------------------------------------------------ routes


@dataclass
class Slot:
    """One ingredient slot: any one of the alternatives, each (code, items)."""

    alternatives: list[tuple[str, float]]
    consumed: bool = True
    returned: tuple[str, float] | None = None  # a different stack handed back (bucket of X -> bucket)


@dataclass
class Route:
    kind: str  # markup key
    recipe: str  # recipe id or attribute route ("smelting|<code>")
    output: str
    quantity: float  # output items per batch (expected, for a lottery)
    slots: list[Slot] = field(default_factory=list)
    # The batch's other outputs, (code, expected items): credited against this output's cost.
    byproducts: list[tuple[str, float]] = field(default_factory=list)
    switch: str | None = None  # the seraphhorizons config switch that owns the recipe
    # The output stack is in litres (a recipe's): its items per litre, for the kind's minBatchLitres; 0 if not.
    liquid: float = 0.0
    # A solid output's stack size (the export's maxStackSize), for the kind's minBatchItems; 0 if unknown.
    stack: int = 0


def _items(stack: dict, litres: dict[str, float] | None = None) -> float:
    """A stack's items; a liquid's litres at the export's items per litre for it (per_litre), else
    ITEMS_PER_LITRE (Expanded Foods' hardened lard is 5 a litre, not 100)."""
    if stack.get("litres") is not None:
        return float(stack["litres"]) * (litres or {}).get(stack.get("code"), ITEMS_PER_LITRE)
    return float(stack["quantity"])


def _filled(voxels: list) -> int:
    # Smithing and clayforming have layers of rows; knapping exports one layer (a list of rows).
    count = 0
    for layer in voxels:
        rows = [layer] if isinstance(layer, str) else layer
        count += sum(row.count("#") for row in rows)
    return count


def kind_of(rtype: str, shape: str) -> str:
    if shape == "transition":
        return "transition"
    if rtype in ("grid", "smithing", "knapping", "clayforming", "barrel", "cooking", "alloy", "construction"):
        return rtype
    return "mod"


def routes_from_recipes(export: dict, rules: Rules, breakdown: bool = False) -> tuple[list[Route], Counter]:
    """The routes the export's recipes give. With breakdown, only the excluded recipes instead
    (recycling, uncrafting, chiselling jewellery into bits), each variant one route whose every
    output after the first is a byproduct: the scrap floor's salvage routes (salvage)."""
    routes: list[Route] = []
    skipped: Counter = Counter()
    types = export.get("recipeTypes", {})
    litres = per_litre(export)
    for r in export["recipes"]:
        rtype = r["type"]
        if rtype in SKIPPED_TYPES:
            skipped[rtype] += 1
            continue
        if r.get("enabled") is False:
            skipped["disabled"] += 1
            continue
        if any(p.search(r["id"]) for p in rules.exclude) != breakdown:
            skipped["excluded" if not breakdown else "ordinary"] += 1
            continue
        shape = types.get(rtype, {}).get("shape", "generic")
        kind = rtype if rtype in rules.markups else kind_of(rtype, shape)
        defs = r["ingredients"]
        # Voxel recipes: the definition's one material slot is used by volume.
        vox = rules.voxels_per_unit.get(rtype) if "voxels" in r else None
        units = _filled(r["voxels"]) / float(vox["voxels"]) if vox else 1.0
        # Shaped or shapeless, the pattern has one cell per ingredient item.
        grid_counts = Counter("".join(r["grid"].get("pattern") or [])) if r.get("grid") else None
        mids: list[float] = []
        total = 1.0
        if rtype == "alloy":
            mids = [((i.get("minRatio") or 0) + (i.get("maxRatio") or 0)) / 2 for i in defs]
            total = sum(mids) or 1.0
        lottery = expected_shares(r) if r.get("lottery") else None
        # A machine's fitted parts (machine.kept: the gear cutter's master) are never consumed.
        kept = set((r.get("machine") or {}).get("kept") or [])
        for v in r["variants"]:
            if not v["outputs"]:
                continue
            out = v["outputs"][0]
            if rules.is_schematic(out["code"]):
                skipped["schematic"] += 1
                continue
            slots: list[Slot] = []
            ok = True
            for idx, (d, accepted) in enumerate(zip(defs, v["ingredients"])):
                if rtype == "cooking" and (d.get("minQuantity") or 0) == 0:
                    continue  # optional cooking ingredient
                if not accepted:
                    ok = False
                    break
                if all(rules.is_schematic(a["code"]) for a in accepted):
                    continue  # a schematic: kept, worth nothing, never blocks the route
                accepted = [a for a in accepted if not rules.is_schematic(a["code"])]
                # Kept: a tool, a station, a fitted part ("kept" role, or the machine's kept list: the
                # gear cutter's master), or a grid ingredient with consume false (the exporter writes
                # extra.consumed false).
                consumed = (not d.get("isTool") and d.get("role") not in ("station", "tool", "kept")
                            and idx not in kept and (d.get("extra") or {}).get("consumed") is not False)
                factor = 1.0
                if vox and idx == 0 and vox["match"] in accepted[0]["code"]:
                    factor = units / max(_items(accepted[0], litres), 1.0)
                if rtype == "alloy":
                    # Shares of one output unit: the alloy's ingot is made of 1 ingot's worth of inputs.
                    factor = (mids[idx] / total) / max(_items(accepted[0], litres), 1.0)
                if rtype == "cooking":
                    factor = float(d.get("minQuantity") or 1)
                if grid_counts and d.get("key"):
                    # A grid ingredient's quantity is per cell its key fills.
                    factor *= grid_counts.get(d["key"], 1)
                ret = None
                if d.get("returned"):
                    rc = d["returned"]["code"]
                    if any(a["code"] == rc for a in accepted):
                        consumed = False
                    else:
                        ret = (rc, _items(d["returned"], litres))
                slots.append(Slot([(a["code"], _items(a, litres) * factor) for a in accepted], consumed, ret))
            if not ok:
                continue
            if breakdown:
                shares = lottery or [1.0] * len(v["outputs"])
                expected = [(o["code"], _items(o, litres) * share) for o, share in zip(v["outputs"], shares)]
                routes.append(Route(kind, r["id"], expected[0][0], expected[0][1], slots, expected[1:], r.get("switch")))
                continue
            if lottery is None:
                routes.append(Route(kind, r["id"], out["code"], _items(out, litres), slots, switch=r.get("switch"),
                                    liquid=litres.get(out["code"], ITEMS_PER_LITRE) if out.get("litres") is not None else 0.0))
                continue
            # A lottery: one route per output that can come out, the others credited at their value.
            expected = [(o["code"], _items(o, litres) * share) for o, share in zip(v["outputs"], lottery)]
            for j, (code, q) in enumerate(expected):
                if q <= EPS or rules.is_schematic(code):
                    continue
                others = [(c, n) for k, (c, n) in enumerate(expected) if k != j and n > EPS]
                routes.append(Route(kind, r["id"], code, q, slots, others, r.get("switch")))
    for rt in routes:
        if not rt.liquid:
            rt.stack = stack_size(export, rt.output)
    return routes, skipped


def expected_shares(r: dict) -> list[float]:
    """A lottery record's chance per output: each output belongs to one outcome (schema.md), so
    its expected items per input are its outcome's chance times its quantity."""
    shares = [0.0] * len(r.get("outputs") or [])
    for outcome in r["lottery"].get("outcomes") or []:
        for idx in outcome.get("outputs") or []:
            if 0 <= idx < len(shares):
                shares[idx] += float(outcome.get("chance") or 0)
    return shares


def routes_from_attributes(export: dict) -> list[Route]:
    """Smelting (incl. baking), crushing, grinding, the fruit press (juicing) and the still
    (distillation), which the export keeps on the items."""
    routes = []
    litres = per_litre(export)
    for code, item in export["items"].items():
        a = item.get("attributes") or {}
        sm = a.get("smelting")
        if sm and sm.get("output") and sm["output"]["code"] != code:
            n = float(sm.get("inputQuantity") or 1)
            kind = "baking" if sm.get("method") == "bake" else "smelting"
            routes.append(Route(kind, f"{kind}|{code}", sm["output"]["code"], _items(sm["output"], litres),
                                [Slot([(code, n)])]))
        extra = a.get("extra") or {}
        for kind in ("crushing", "grinding"):
            p = extra.get(kind)
            if p and p.get("output") and p["output"]["code"] != code:
                avg = float((p.get("quantity") or {}).get("avg", 1.0))
                routes.append(Route(kind, f"{kind}|{code}", p["output"]["code"], _items(p["output"], litres) * avg,
                                    [Slot([(code, 1.0)])]))
        # The fruit press: one item gives litresPerItem litres. Its by-products (the pressed mash,
        # a returned stack such as honeycomb's beeswax) are not credited, as butchery's many outputs
        # are not routes: the juice carries the whole input. Mash has no litresPerItem: what is
        # left in it rides on the stack, so it is no route.
        j = extra.get("juicing")
        if j and j.get("output") and j.get("litresPerItem") and j["output"]["code"] != code:
            routes.append(Route("pressing", f"pressing|{code}", j["output"]["code"],
                                float(j["litresPerItem"]) * litres.get(j["output"]["code"], ITEMS_PER_LITRE), [Slot([(code, 1.0)])]))
        # The still: a portion of the liquid (cider) distils into `ratio` portions of the output
        # (spirit): 10 L of fruit cider make 1 L of brandy (0.1), 20 L of grain cider or mead 1 L (0.05).
        d = extra.get("distillation")
        if d and d.get("output") and d.get("ratio") and d["output"]["code"] != code:
            routes.append(Route("distilling", f"distilling|{code}", d["output"]["code"], float(d["ratio"]),
                                [Slot([(code, 1.0)])]))
    return routes


# ------------------------------------------------------------------ generated raws


GRADES = ("poor", "medium", "rich", "bountiful")


def ore_raws(export: dict, rules: Rules) -> dict[str, tuple[float, str]]:
    """Graded ore chunks, crystallized ores and nuggets, priced by their metal units."""
    spec = rules.ores
    if not spec:
        return {}
    items = export["items"]
    unit_value = spec.get("unitValue", {})
    no_metal = set(spec.get("noMetal", []))
    fallback = float(spec.get("noMetalUnitValue", 0))
    metal: dict[str, str | None] = {}
    for code, item in items.items():
        if code.startswith("game:nugget-"):
            sm = (item.get("attributes") or {}).get("smelting") or {}
            metal[code[len("game:nugget-"):]] = (sm.get("output") or {}).get("code")

    def per_unit(ore: str) -> tuple[float, str]:
        m = None if ore in no_metal else metal.get(ore)
        if m in unit_value:
            return float(unit_value[m]), m
        return fallback, "no metal"

    out: dict[str, tuple[float, str]] = {}
    grade_units = spec.get("gradeUnits", {})
    by_ore = spec.get("unitsByOre", {})
    for code in items:
        dom, path = code.split(":", 1)
        parts = path.split("-")
        if dom == "game" and parts[0] in ("ore", "crystalizedore") and len(parts) >= 4 and parts[1] in GRADES:
            ore = parts[2]
            # Host rock and ore: quartz_nativegold breaks into nativegold nuggets.
            nugget = ore if ore in metal else ore.split("_")[-1]
            units = float(by_ore.get(ore, grade_units).get(parts[1], grade_units.get(parts[1], 15)))
            v, m = per_unit(nugget)
            out[code] = (units * v, f"raw:ore {units:g} units of {m}")
        elif dom == "game" and parts[0] == "nugget" and len(parts) == 2:
            v, m = per_unit(parts[1])
            n = float(spec.get("nuggetUnits", 5))
            out[code] = (n * v, f"raw:nugget {n:g} units of {m}")
    return out


def trader_value(item: dict, rules: Rules) -> float | None:
    tf = rules.trader_fallback
    if not tf:
        return None
    vals = []
    for s in item.get("sources") or []:
        if s.get("type") not in ("traderSells", "traderBuys") or s.get("price") is None:
            continue
        qty = float((s.get("quantity") or {}).get("avg") or 1)
        factor = float(tf["sells"] if s["type"] == "traderSells" else tf["buys"])
        vals.append(float(s["price"]) / max(qty, 1.0) * factor)
    return sum(vals) / len(vals) if vals else None


# ------------------------------------------------------------------ solving


@dataclass
class Valuation:
    value: dict[str, float]
    source: dict[str, str]  # "raw...", "override", "default:...", or the route's recipe id
    route: dict[str, Route]
    routes_by_output: dict[str, list[Route]]
    passes: int
    # Codes whose value exists only with these seraphhorizons config switches on (sorted).
    switches: dict[str, list[str]] = field(default_factory=dict)
    # The scrap floor: every code with a salvage route's floor (an override's too, never applied),
    # the values before any floor, and the solves it took to settle.
    floors: dict[str, "Floor"] = field(default_factory=dict)
    unfloored: dict[str, float] = field(default_factory=dict)
    floor_rounds: int = 0
    picks: dict[str, list[str]] = field(default_factory=dict)  # what each routed value was priced from


@dataclass
class Floor:
    """The scrap floor of one item: what its best break-down route gives back per item (salvage),
    times the multiplier for a found item (no ordinary route makes it) or a made one."""

    value: float  # gears per item (a liquid's per portion) the item is worth at least
    salvage: float
    multiplier: float
    recipe: str
    uses: list[str]  # what the salvage was priced from: the route's outputs and other inputs

    @property
    def label(self) -> str:
        return f"floor:{self.multiplier:g}x {self.recipe}"


def route_eval(route: Route, value: dict[str, float], rules: Rules,
               wait_for: set[str] | None = None, hint: dict[str, float] | None = None,
               hint_byproducts: bool = False) -> tuple[float, list[str]] | None:
    """Gears per output item and the codes the price was made of (each slot's cheapest stack, the
    returned containers and the credited byproducts), or None while a consumed slot has no valued
    alternative (or, with wait_for, a tool slot or a byproduct has none yet although it will get
    one). A tool not valued yet takes its `hint` value (the first pass's) instead of waiting, and
    so does a byproduct with `hint_byproducts`.

        (consumed x (1 + pct) + flat + tools x toolFraction - byproducts) / quantity + perItem

    with the quotient floored at 0, where a byproduct is each other output of the batch at its
    value times its expected items (a lottery's losers), and perItem the kind's charge per output
    item (markups.json). A kind's minBatchLitres spreads the flat of a recipe making a liquid
    over at least that many litres (Rules.flat)."""
    total = 0.0
    tools = 0.0
    picks: list[str] = []
    for slot in route.slots:
        best = None
        pick = None
        for code, n in slot.alternatives:
            v = value.get(code)
            if v is not None and (best is None or v * n < best):
                best, pick = v * n, code
        if not slot.consumed:
            if best is None and hint:
                for code, n in slot.alternatives:
                    v = hint.get(code)
                    if v is not None and (best is None or v * n < best):
                        best, pick = v * n, code
            if best is None and wait_for and any(code in wait_for for code, _ in slot.alternatives):
                return None
            tools += best or 0.0
            if pick is not None:
                picks.append(pick)
            continue
        if best is None:
            return None
        picks.append(pick)
        if slot.returned:
            rv = value.get(slot.returned[0])
            if rv is not None:
                best = max(best - rv * slot.returned[1], 0.0)
                picks.append(slot.returned[0])
        total += best
    credit = 0.0
    for code, n in route.byproducts:
        v = value.get(code)
        if v is None and hint_byproducts and hint:
            v = hint.get(code)
        if v is None:
            if wait_for and code in wait_for:
                return None
            continue
        credit += v * n
        picks.append(code)
    pct, _ = rules.markup(route.kind)
    flat = rules.flat(route)
    cost = (total * (1 + pct) + flat + tools * rules.tool_fraction - credit) / max(route.quantity, EPS)
    return max(cost, 0.0) + rules.per_item(route.kind), picks


def route_cost(route: Route, value: dict[str, float], rules: Rules, wait_for: set[str] | None = None) -> float | None:
    """Gears per output item (route_eval), or None while the route cannot be priced."""
    got = route_eval(route, value, rules, wait_for)
    return None if got is None else got[0]


class Siblings:
    """Valued codes by variant: the codes that differ from a code in exactly one '-' segment of its
    path (toolmold-black-fired-axe and toolmold-blue-fired-axe; planks-oak-ns and planks-oak-ud)."""

    def __init__(self, value: dict[str, float]):
        self.by_key: dict[tuple, list[float]] = defaultdict(list)
        for code, v in value.items():
            for key in self._keys(code):
                self.by_key[key].append(v)

    @staticmethod
    def _keys(code: str):
        dom, _, path = code.partition(":")
        parts = path.split("-")
        if len(parts) < 2:
            return
        for i in range(1, len(parts)):  # the first segment, what the thing is, must match
            yield (dom, i, tuple(parts[:i]), tuple(parts[i + 1:]))

    def average(self, code: str) -> tuple[float, str, int] | None:
        vals: list[float] = []
        pattern = ""
        for key in self._keys(code):
            found = self.by_key.get(key)
            if found:
                vals += found
                dom, i, head, tail = key
                pattern = f"{dom}:" + "-".join(head + ("*",) + tail)
        if not vals:
            return None
        return sum(vals) / len(vals), pattern, len(vals)


def _settle(routes: list[Route], fixed_value: dict[str, float], fixed_source: dict[str, str], rules: Rules,
            picks: dict[str, list[str]] | None = None, hint: dict[str, float] | None = None,
            floors: dict[str, "Floor"] | None = None,
            ) -> tuple[dict[str, float], dict[str, str], dict[str, Route], int]:
    """Knuth's generalisation of Dijkstra: settle items cheapest first, each at its cheapest route over
    items already settled. A settled value never drops again, so a cycle that makes more than it
    consumes (two linen -> four sails -> eight linen) cannot pull prices down: the sail is priced
    from settled linen, and the linen was settled before any sail existed. A route's cost is at
    least its output's scrap floor (`floors`), and an item settled at its floor has the floor as
    its source."""
    floors = floors or {}
    value = dict(fixed_value)
    source = dict(fixed_source)
    best_route: dict[str, Route] = {}
    uses: dict[str, list[int]] = defaultdict(list)
    for i, rt in enumerate(routes):
        for slot in rt.slots:
            for code, _ in slot.alternatives:
                uses[code].append(i)
            if slot.returned:
                uses[slot.returned[0]].append(i)
        for code, _ in rt.byproducts:
            uses[code].append(i)
    # What will get a value at all, so a route waits for its tools to be priced (a hammer is) but
    # not for a tool nothing prices.
    reachable = set(value)
    grew = True
    while grew:
        grew = False
        for rt in routes:
            if rt.output not in reachable and all(
                    not slot.consumed or any(c in reachable for c, _ in slot.alternatives) for slot in rt.slots):
                reachable.add(rt.output)
                grew = True
    tentative: dict[str, tuple[float, int, list[str], bool]] = {}
    heap: list[tuple[float, str, int]] = []
    picks = {} if picks is None else picks

    def consider(i: int, wait: set[str] | None) -> None:
        rt = routes[i]
        if rt.output in value:
            return
        got = route_eval(rt, value, rules, wait, hint)
        if got is None:
            return
        cost, used = got
        fl = floors.get(rt.output)
        bound = fl is not None and fl.value > cost
        if bound:
            cost = fl.value
        cur = tentative.get(rt.output)
        if cur is None or cost < cur[0] - EPS * max(1.0, cur[0]):
            tentative[rt.output] = (cost, i, used, bound)
            heapq.heappush(heap, (cost, rt.output, i))

    settled = 0
    for i in range(len(routes)):
        consider(i, reachable)
    while True:
        while heap:
            cost, code, i = heapq.heappop(heap)
            if code in value or tentative.get(code, (None, None))[1] != i or tentative[code][0] != cost:
                continue
            value[code] = cost
            source[code] = floors[code].label if tentative[code][3] else routes[i].recipe
            best_route[code] = routes[i]
            picks[code] = tentative[code][2]
            settled += 1
            for j in uses.get(code, ()):
                consider(j, reachable)
        # A knot: routes that wait on each other (a tool or a byproduct made only through the item
        # that waits for it) never settle. Settle the cheapest of them without waiting, then wait
        # again for the rest.
        knot = None
        for i, rt in enumerate(routes):
            if rt.output in value:
                continue
            got = route_eval(rt, value, rules, None, hint, True)
            if got is None:
                continue
            fl = floors.get(rt.output)
            bound = fl is not None and fl.value > got[0]
            cost = fl.value if bound else got[0]
            if knot is None or cost < knot[0]:
                knot = (cost, i, got[1], bound)
        if knot is None:
            break
        cost, i, used, bound = knot
        tentative[routes[i].output] = (cost, i, used, bound)
        heapq.heappush(heap, (cost, routes[i].output, i))
    return value, source, best_route, settled


MAX_FLOOR_ROUNDS = 50


def solve(export: dict, rules: Rules) -> Valuation:
    """Every item's value: the cheapest route over hand prices and fallbacks (_solve), then the
    scrap floor. An item is worth at least multiplier x its salvage (scrapFloor in markups.json:
    `found` for an item no ordinary route makes, `made` for one that is), where salvage is what its
    best break-down route (an excluded recipe: recycling, uncrafting, chiselling jewellery) gives
    back per item. A raised item raises what is made from it, which can raise another's salvage, so
    the solve repeats with the floors found until none grows: values only rise, and a salvage priced
    from the item itself is never used, so it ends. Overrides keep their value."""
    items = export["items"]
    recipe_routes, _ = routes_from_recipes(export, rules)
    routes = recipe_routes + routes_from_attributes(export)
    by_out: dict[str, list[Route]] = defaultdict(list)
    for rt in routes:
        by_out[rt.output].append(rt)
    scrap = routes_from_recipes(export, rules, breakdown=True)[0] if rules.scrap_floor else []

    floors: dict[str, Floor] = {}
    val = _solve(export, rules, routes, by_out, floors)
    unfloored = dict(val.value)
    rounds = 1
    while scrap:
        found = scrap_floors(scrap, val, by_out, rules)
        grew = {c: f for c, f in found.items()
                if c not in floors or f.value > floors[c].value + EPS * max(1.0, f.value)}
        if not grew:
            break
        if rounds >= MAX_FLOOR_ROUNDS:
            raise RuntimeError(f"the scrap floor did not settle in {MAX_FLOOR_ROUNDS} solves; still rising: "
                               + ", ".join(sorted(grew)[:10]))
        floors = {**floors, **grew}
        val = _solve(export, rules, routes, by_out, floors)
        rounds += 1
    # Every floor the last values give, for the report (an override's, which is never applied).
    for c, f in (scrap_floors(scrap, val, by_out, rules) if scrap else {}).items():
        if c not in floors or f.value > floors[c].value:
            floors[c] = f
    val.floors, val.unfloored, val.floor_rounds = floors, unfloored, rounds
    return val


def scrap_floors(scrap: list[Route], val: Valuation, by_out: dict[str, list[Route]], rules: Rules) -> dict[str, Floor]:
    """Each item's best scrap floor over the break-down routes that consume it: the outputs' value
    (byproducts and handed-back containers included) less the other consumed inputs (tools are
    kept), per item consumed, times the found or made multiplier. A route whose outputs or other
    inputs are priced, through any chain, from the item itself is skipped: it would feed on itself."""
    value = val.value
    found_x, made_x = rules.scrap_floor.get("found", 1.0), rules.scrap_floor.get("made", 1.0)
    reach_memo: dict[str, set[str]] = {}

    def edges(code: str) -> list[str]:
        out = list(val.picks.get(code, ()))
        rt = val.route.get(code)
        if rt is not None:  # the whole route, not only its picks: another alternative may win later
            for slot in rt.slots:
                out += [c for c, _ in slot.alternatives]
        if (f := val.floors.get(code)) is not None:
            out += f.uses
        return out

    def reach(code: str) -> set[str]:
        """Everything code's value is priced from, itself included."""
        if code in reach_memo:
            return reach_memo[code]
        seen = {code}
        todo = [code]
        while todo:
            for c in edges(todo.pop()):
                if c not in seen:
                    seen.add(c)
                    todo.append(c)
        reach_memo[code] = seen
        return seen

    best: dict[str, Floor] = {}
    for rt in scrap:
        back = 0.0
        uses: list[str] = []
        for code, n in [(rt.output, rt.quantity)] + rt.byproducts:
            if (v := value.get(code)) is not None:
                back += v * n
                uses.append(code)
        cheapest: list[tuple[float, str] | None] = []
        for slot in rt.slots:
            got = [(value[c] * n, c) for c, n in slot.alternatives if c in value]
            cheapest.append(min(got) if got else None)
            if slot.consumed and slot.returned and (v := value.get(slot.returned[0])) is not None:
                back += v * slot.returned[1]
                uses.append(slot.returned[0])
        for i, slot in enumerate(rt.slots):
            if not slot.consumed:
                continue
            others = [cheapest[j] for j, o in enumerate(rt.slots) if j != i and o.consumed]
            if any(o is None for o in others):
                continue
            spent = sum(o[0] for o in others)
            fed = uses + [o[1] for o in others]
            for code, n in slot.alternatives:
                if n <= EPS:
                    continue
                salvage = (back - spent) / n
                if salvage <= EPS or any(code in reach(c) for c in fed):
                    continue
                mult = made_x if code in by_out else found_x
                if code not in best or mult * salvage > best[code].value:
                    best[code] = Floor(mult * salvage, salvage, mult, rt.recipe, fed)
    return best


def _solve(export: dict, rules: Rules, routes: list[Route], by_out: dict[str, list[Route]],
           floors: dict[str, Floor]) -> Valuation:
    items = export["items"]

    def floored(code: str, got: tuple[float, str] | None) -> tuple[float, str] | None:
        """A fixed value (raw, default, fallback) raised to the code's scrap floor."""
        fl = floors.get(code)
        if fl is not None and (got is None or fl.value > got[0]):
            return fl.value, fl.label
        return got

    value: dict[str, float] = {}
    source: dict[str, str] = {}
    ores = ore_raws(export, rules)
    # Schematics are worth nothing and never priced: kept tools that add nothing (routes_from_recipes).
    schematic = {code for code in items if rules.is_schematic(code)}
    for code in items:
        if code in schematic:
            continue
        if code in rules.overrides:
            value[code], source[code] = rules.overrides[code], "override"
            continue
        raw = (rules.raws[code], "raw") if code in rules.raws else ores.get(code) or rules.raw_value(code)
        if raw is not None:
            value[code], source[code] = floored(code, raw)
    # Overrides for codes the export lacks still count, so a typo shows in the report.
    for code, v in rules.overrides.items():
        value.setdefault(code, v)
        source.setdefault(code, "override")
    # Leaves nothing makes and nobody priced: a category default when the code says what it is.
    for code in items:
        if code not in value and code not in by_out and code not in schematic \
                and (dv := rules.default_value(code)) is not None:
            value[code], source[code] = floored(code, dv)

    # Settled in two layers. The fallbacks of the second, vanilla trader prices of leaves and category
    # defaults of items made only from unpriced things, are guesses, so they only price what the
    # first layer left unpriced and never undercut a production chain (copper buttons a trader
    # sells cheaply do not make copper ingots cheaper). The second layer raises a leaf's fallback
    # to its scrap floor, or gives a leaf with none its floor; a third gives an item whose routes
    # still cannot be priced its floor (only then: a route the fallbacks make priceable wins).
    def layers(hint: dict[str, float] | None):
        picks: dict[str, list[str]] = {}
        val, src, best_route, settled = _settle(routes, dict(value), dict(source), rules, picks, hint, floors)
        sibling = Siblings(val)
        late = 0
        for code in items:
            if code in val or code in by_out or code in schematic:
                continue
            dv = None
            if (tv := trader_value(items[code], rules)) is not None:
                dv = (tv, "default:trader")
            elif (sv := sibling.average(code)) is not None:
                dv = (sv[0], f"default:siblings {sv[1]} ({sv[2]})")
            dv = floored(code, dv)
            if dv is not None:
                val[code], src[code] = dv
                late += 1
        if late:
            val, src, more, n = _settle(routes, val, src, rules, picks, hint, floors)
            best_route.update(more)
            settled += n
        unrouted = [c for c in floors if c in by_out and c not in val and c in items and c not in schematic]
        for code in unrouted:
            val[code], src[code] = floors[code].value, floors[code].label
        if unrouted:
            val, src, more, n = _settle(routes, val, src, rules, picks, hint, floors)
            best_route.update(more)
            settled += n
        return val, src, best_route, settled, picks

    # Twice. Settling cheapest first, a route waits for its tools, so one whose tool is worth more
    # than its output (the gear cutter's frame, 103 gears, cutting a steel gear worth 13) would
    # only be priced after that output had settled by a dearer route. The second pass starts from
    # the same raws and takes a tool not yet valued at its first-pass value instead of waiting.
    first = layers(None)[0]
    value, source, best_route, settled, picks = layers(first)
    return Valuation(value, source, best_route, by_out, settled, switch_dependencies(items, value, best_route, picks),
                     floors=dict(floors), picks=picks)


def switch_dependencies(items: dict, value: dict[str, float], best_route: dict[str, Route],
                        picks: dict[str, list[str]]) -> dict[str, list[str]]:
    """The config switches each value exists by: the switch that adds the item itself
    (items[code].switch), the one that owns its route's recipe (recipes[i].switch), and every
    switch of what that route was priced from (its stacks, tools, returned containers and credited
    byproducts), through the whole chain. `value` is in the order items were valued, and a route is
    priced mostly from items valued before it, so a pass in that order sees nearly every input
    first; a tool priced at its first-pass value may come later, so passes repeat until nothing
    changes."""
    deps: dict[str, frozenset[str]] = {}
    changed = True
    while changed:
        changed = False
        for code in value:
            own = set()
            if (sw := (items.get(code) or {}).get("switch")):
                own.add(sw)
            rt = best_route.get(code)
            if rt is not None:
                if rt.switch:
                    own.add(rt.switch)
                for c in picks.get(code, ()):
                    own |= deps.get(c, frozenset())
            if deps.get(code) != own:
                deps[code] = frozenset(own)
                changed = True
    return {code: sorted(deps[code]) for code in sorted(value) if deps[code]}


# ------------------------------------------------------------------ output


def stack_size(export: dict, code: str) -> int:
    item = export["items"].get(code) or {}
    return int((item.get("attributes") or {}).get("maxStackSize") or 1)


def floor_zero(export: dict, code: str, value: float) -> bool:
    """Worth under a gear per full stack (value per item, a liquid's per portion): trading treats
    it as worthless."""
    return value * stack_size(export, code) < 1.0


def per_litre(export: dict) -> dict[str, float]:
    """The liquids: code -> items (portions) per litre, from the export's
    items[code].attributes.extra.liquid.itemsPerLitre (the game's waterTightContainerProps). Under
    one item a litre is no liquid a container holds (the engine stores 0.001 on world water)."""
    out: dict[str, float] = {}
    for code, item in export["items"].items():
        liquid = (((item.get("attributes") or {}).get("extra") or {}).get("liquid")) or {}
        n = liquid.get("itemsPerLitre") if isinstance(liquid, dict) else None
        if isinstance(n, (int, float)) and n >= 1:
            out[code] = int(n) if float(n).is_integer() else float(n)
    return out


def shown(export: dict, code: str, value: float, litres: dict[str, float] | None = None) -> tuple[float, str]:
    """A value as the table stores it, with its unit: gears per item, or a liquid's gears per litre."""
    litres = per_litre(export) if litres is None else litres
    if code in litres:
        return value * litres[code], "/L"
    return value, ""


ABOUT = ("Item base values in rusty gears: per item, except the liquids listed in perLitre (code: items "
         "per litre), whose values are gears per litre. From tools/item-values (#449). Generated: do not "
         "edit; change tools/item-values/*.json and rebuild.")


def table(export: dict, val: Valuation) -> dict:
    litres = per_litre(export)
    values = {}
    zero = []
    for code in sorted(export["items"]):
        if code not in val.value:
            continue
        values[code] = round(shown(export, code, val.value[code], litres)[0], 3)
        if floor_zero(export, code, val.value[code]):
            zero.append(code)
    pack = export.get("pack", {})
    return {
        "about": ABOUT,
        "schemaVersion": export.get("schemaVersion"),
        "pack": {k: pack.get(k) for k in ("id", "version")},
        "values": values,
        "floorZero": zero,
        "perLitre": {c: litres[c] for c in values if c in litres},
        "switches": {c: sw for c, sw in val.switches.items() if c in values},
    }


def write_table(path: Path, data: dict) -> None:
    """One entry per line, so a rebuild diffs cleanly."""
    lines = ["{"]
    for key in ("about", "schemaVersion", "pack"):
        lines.append(f"  {json.dumps(key)}: {json.dumps(data[key])},")
    lines.append('  "values": {')
    entries = [f"    {json.dumps(k)}: {json.dumps(v)}" for k, v in data["values"].items()]
    lines.append(",\n".join(entries))
    lines.append("  },")
    lines.append('  "floorZero": [')
    lines.append(",\n".join(f"    {json.dumps(c)}" for c in data["floorZero"]))
    lines.append("  ],")
    if data["perLitre"]:
        lines.append('  "perLitre": {')
        lines.append(",\n".join(f"    {json.dumps(c)}: {json.dumps(n)}" for c, n in data["perLitre"].items()))
        lines.append("  },")
    else:
        lines.append('  "perLitre": {},')
    if data["switches"]:
        lines.append('  "switches": {')
        lines.append(",\n".join(f"    {json.dumps(c)}: {json.dumps(sw)}" for c, sw in data["switches"].items()))
        lines.append("  }")
    else:
        lines.append('  "switches": {}')
    lines.append("}")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def domain(code: str) -> str:
    return code.split(":", 1)[0]


def below_ingredients(export: dict, val: Valuation, rules: Rules) -> list[tuple[str, float, float, str]]:
    """Items valued below what the ingredients of their route cost (for an override or a raw, of the
    cheapest route that makes it). A derived value never is, so these are hand prices to review."""
    out = []
    for code, routes in val.routes_by_output.items():
        if code not in val.value:
            continue
        chosen = val.route.get(code)
        best = None
        for rt in [chosen] if chosen else routes:
            c = route_cost(rt, val.value, rules)
            if c is None:
                continue
            pct, _ = rules.markup(rt.kind)
            flat = rules.flat(rt)
            tools = sum(min((val.value.get(a, 0.0) * n for a, n in s.alternatives), default=0.0)
                        for s in rt.slots if not s.consumed) * rules.tool_fraction
            # Ingredients alone, without the labour markup.
            ing = (((c - rules.per_item(rt.kind)) * rt.quantity - flat - tools) / (1 + pct)) / rt.quantity
            if best is None or ing < best[0]:
                best = (ing, rt.recipe)
        if best and val.value[code] < best[0] - 1e-6 * max(1.0, best[0]):
            out.append((code, val.value[code], best[0], best[1]))
    return sorted(out)


def report(export: dict, val: Valuation, rules: Rules) -> dict:
    items = export["items"]
    codes = sorted(items)
    missing = [c for c in codes if c not in val.value]
    zero = [c for c in codes if c in val.value and floor_zero(export, c, val.value[c])]
    priced = [c for c in codes if c in val.value]
    litres = per_litre(export)

    def entry(c: str, v: float, **more) -> dict:
        """A value as the table has it: a liquid's per litre, marked so."""
        n, unit = shown(export, c, v, litres)
        e = {"code": c, "value": round(n, 3)}
        if unit:
            e["perLitre"] = True
        for k, x in more.items():
            e[k] = round(shown(export, c, x, litres)[0], 3) if isinstance(x, float) else x
        return e

    # Ranked by the table's number: a liquid by its litre, which is what a trader deals in.
    by_value = sorted(priced, key=lambda c: (shown(export, c, val.value[c], litres)[0], c))
    nonzero = [c for c in by_value if c not in set(zero)]
    per_domain: dict[str, list[int]] = defaultdict(lambda: [0, 0, 0])
    for c in codes:
        row = per_domain[domain(c)]
        row[0] += 1
        if c in val.value:
            row[1] += 1
            if floor_zero(export, c, val.value[c]):
                row[2] += 1
    src = Counter()
    for c in priced:
        s = val.source[c]
        src["override" if s == "override" else s.split(":")[0] if s.startswith(("raw", "default", "floor")) else "recipe"] += 1
    return {
        "items": len(codes),
        "valued": len(priced),
        "coverage": round(len(priced) / max(len(codes), 1), 4),
        "floorZero": len(zero),
        "floorZeroShare": round(len(zero) / max(len(priced), 1), 4),
        "passes": val.passes,
        "sources": dict(src),
        "missing": missing,
        "belowIngredients": [entry(c, v, ingredients=i, recipe=r) for c, v, i, r in below_ingredients(export, val, rules)],
        "floorRounds": val.floor_rounds,
        "raisedByFloor": raised_by_floor(export, val),
        "overridesBelowFloor": [
            {"code": c, "value": round(shown(export, c, val.value[c], litres)[0], 3),
             "floor": round(shown(export, c, f.value, litres)[0], 3), "multiplier": f.multiplier, "recipe": f.recipe}
            for c, f in sorted(val.floors.items()) if val.source.get(c) == "override" and f.value > val.value[c] + EPS],
        "mostValuable": [entry(c, val.value[c]) for c in reversed(by_value[-50:])],
        "leastValuable": [entry(c, val.value[c]) for c in nonzero[:50]],
        "domains": {
            d: {"items": n, "valued": v, "floorZero": z, "coverage": round(v / n, 4)}
            for d, (n, v, z) in sorted(per_domain.items())
        },
    }


def raised_by_floor(export: dict, val: Valuation) -> list[dict]:
    """Every item the scrap floor raised, the most raised first: its value before the floor (None:
    it had none), its salvage, its value now, and either the floor's multiplier and break-down
    recipe (raised to its own floor) or `via`, the route or fallback it was raised through."""
    litres = per_litre(export)
    out = []
    for c, new in val.value.items():
        old = val.unfloored.get(c)
        if old is not None and new <= old + EPS * max(1.0, old):
            continue
        e = {"code": c, "old": None if old is None else round(shown(export, c, old, litres)[0], 3),
             "value": round(shown(export, c, new, litres)[0], 3)}
        if c in litres:
            e["perLitre"] = True
        f = val.floors.get(c)
        if val.source.get(c, "").startswith("floor:") and f is not None:
            e.update(salvage=round(shown(export, c, f.salvage, litres)[0], 3), multiplier=f.multiplier, recipe=f.recipe)
        else:
            e["via"] = val.source.get(c)
        e["_rise"] = shown(export, c, new - (old or 0.0), litres)[0]
        out.append(e)
    out.sort(key=lambda e: (-e["_rise"], e["code"]))
    for e in out:
        del e["_rise"]
    return out


def _unit(e: dict) -> str:
    return "/L" if e.get("perLitre") else ""


def report_markdown(rep: dict, export: dict, val: Valuation, samples: list[str]) -> str:
    lines = ["# Item values report", "", "Gears per item; a liquid's (marked /L) per litre, as the table stores it.", ""]
    lines.append(f"- items: {rep['items']}, valued: {rep['valued']} ({rep['coverage']:.1%}), "
                 f"worthless (under a gear per stack): {rep['floorZero']} ({rep['floorZeroShare']:.1%} of valued)")
    lines.append(f"- sources: {rep['sources']}; {rep['passes']} items settled from recipes")
    lines.append(f"- items with no value: {len(rep['missing'])}; valued below their ingredients: {len(rep['belowIngredients'])}")
    lines.append("")
    if samples:
        lines += ["## Samples", "", "| item | gears | stack | source |", "|---|---|---|---|"]
        for c in samples:
            v = val.value.get(c)
            if v is not None:
                n, unit = shown(export, c, v)
            lines.append(f"| `{c}` | {'—' if v is None else f'{n:.3f}{unit}'} | {stack_size(export, c)} | {val.source.get(c, 'missing')} |")
        lines.append("")
    lines += ["## Coverage per domain", "", "| domain | items | valued | worthless | coverage |", "|---|---|---|---|---|"]
    for d, row in sorted(rep["domains"].items(), key=lambda kv: -kv[1]["items"]):
        lines.append(f"| {d} | {row['items']} | {row['valued']} | {row['floorZero']} | {row['coverage']:.1%} |")
    lines += ["", "## Most valuable", ""]
    lines += [f"- `{e['code']}` {e['value']}{_unit(e)}" for e in rep["mostValuable"]]
    lines += ["", "## Least valuable (not worthless)", ""]
    lines += [f"- `{e['code']}` {e['value']}{_unit(e)}" for e in rep["leastValuable"]]
    lines += ["", "## Valued below their ingredients", ""]
    lines += [f"- `{e['code']}` {e['value']}{_unit(e)} < {e['ingredients']}{_unit(e)} ({e['recipe']})"
              for e in rep["belowIngredients"]]
    lines += ["", "## Raised by the scrap floor", "",
              f"{len(rep['raisedByFloor'])} items, the most raised first ({rep['floorRounds']} solves): before -> after. "
              "Raised to its own floor: salvage x multiplier (5 found, 1 made) and the break-down recipe; "
              "otherwise the route or fallback it was raised through.", ""]
    for e in rep["raisedByFloor"]:
        old = "none" if e["old"] is None else f"{e['old']}{_unit(e)}"
        how = (f"salvage {e['salvage']}{_unit(e)} x {e['multiplier']:g} ({e['recipe']})" if "recipe" in e
               else f"via {e['via']}")
        lines.append(f"- `{e['code']}` {old} -> {e['value']}{_unit(e)}: {how}")
    lines += ["", "## Overrides below their scrap floor", ""]
    lines += [f"- `{e['code']}` {e['value']} < {e['floor']} ({e['multiplier']:g} x salvage, {e['recipe']})"
              for e in rep["overridesBelowFloor"]] or ["None."]
    lines += ["", "## No value", ""]
    lines += [f"- `{c}`" for c in rep["missing"]]
    return "\n".join(lines) + "\n"


SAMPLES = [
    "game:ingot-copper", "game:pickaxe-tinbronze", "game:bread-spelt-perfect", "game:linen-normal-down",
    "game:plank-oak", "game:glass-plain", "game:leather-normal-plain", "game:gear-rusty",
    "game:bed-wood-head-north", "game:barrel", "seraphhorizons:gear-oiled", "game:metalbit-steel",
    "seraphhorizons:gear-steel", "seraphhorizons:gear-steel-bare",
    "game:juiceportion-apple", "game:ciderportion-apple", "game:spiritportion-apple", "game:ciderportion-mead",
    "expandedfoods:foodoilportion-olive",
]


def explain(export: dict, val: Valuation, rules: Rules, code: str, depth: int = 0, seen: set | None = None,
            out: list | None = None, max_depth: int = 8) -> list[str]:
    out = [] if out is None else out
    seen = set() if seen is None else seen
    pad = "  " * depth
    v = val.value.get(code)
    src = val.source.get(code, "missing")
    name = (export["items"].get(code) or {}).get("name", "")
    if v is None:
        out.append(f"{pad}{code} ({name}): no value")
        return out
    zero = " [worthless: under 1 gear per stack]" if code in export["items"] and floor_zero(export, code, v) else ""
    litre, unit = shown(export, code, v)
    each = (f"{litre:.4f} gears/L ({v:.6g} a portion, {per_litre(export)[code]:g} portions a litre)"
            if unit else f"{v:.4f} gears/item")
    out.append(f"{pad}{code} ({name}) = {each}, stack {stack_size(export, code)}{zero}  <- {src}")
    if (fl := val.floors.get(code)) is not None:
        sv, unit_ = shown(export, code, fl.salvage)
        fv, _ = shown(export, code, fl.value)
        bound = "raised to it" if src.startswith("floor:") else "not binding" if src != "override" else "an override: not applied"
        out.append(f"{pad}  scrap floor: {fl.multiplier:g} x salvage {sv:.4f}{unit_} = {fv:.4f}{unit_} "
                   f"({fl.recipe}; {bound})")
    rt = val.route.get(code)
    if rt is None or code in seen or depth >= max_depth:
        return out
    seen.add(code)
    pct, _ = rules.markup(rt.kind)
    flat = rules.flat(rt)
    total = tools = 0.0
    for slot in rt.slots:
        best = None
        for c, n in slot.alternatives:
            cv = val.value.get(c)
            if cv is not None and (best is None or cv * n < best[1]):
                best = (c, cv * n, n)
        if best is None:
            out.append(f"{pad}  - (no valued alternative; {'tool' if not slot.consumed else 'consumed'})")
            continue
        c, cost, n = best
        if slot.consumed:
            ret = ""
            if slot.returned and slot.returned[0] in val.value:
                back = val.value[slot.returned[0]] * slot.returned[1]
                ret = f", minus {slot.returned[0]} handed back ({back:.4f})"
                cost = max(cost - back, 0.0)
            total += cost
            alts = f" (cheapest of {len(slot.alternatives)})" if len(slot.alternatives) > 1 else ""
            out.append(f"{pad}  - {n:g} x {c}{alts} = {cost:.4f}{ret}")
        else:
            tools += cost
            out.append(f"{pad}  - tool/kept {c}: {cost:.4f} x {rules.tool_fraction} = {cost * rules.tool_fraction:.4f}")
        explain(export, val, rules, c, depth + 2, seen, out, max_depth)
    credit = 0.0
    for c, n in rt.byproducts:
        cv = val.value.get(c)
        if cv is None:
            out.append(f"{pad}  - other outcome {n:g} x {c}: no value, credited 0")
            continue
        credit += cv * n
        out.append(f"{pad}  - other outcome {n:g} x {c} = {cv * n:.4f}, credited")
    less = f" - {credit:.4f}" if rt.byproducts else ""
    cost = (total * (1 + pct) + flat + tools * rules.tool_fraction - credit) / rt.quantity
    per = rules.per_item(rt.kind)
    plus = f" + {per:g} per item" if per else ""
    each = max(cost, 0.0) + per
    if unit:  # the route is per portion; the table's number is a litre's
        plus = plus.replace("per item", "a portion")
        ipl = per_litre(export)[code]
        result = f"{each:.6g} a portion = {each * ipl:.4f} gears/L"
    else:
        result = f"{each:.4f}"
    out.append(f"{pad}  = ({total:.4f} x (1 + {pct}) + {flat:.4g} + {tools * rules.tool_fraction:.4f}{less}) / {rt.quantity:g}{plus} "
               f"[{rt.kind}] = {result}" + (" (quotient floored at 0)" if cost < 0 else ""))
    sw = val.switches.get(code)
    if sw and depth == 0:
        out.append(f"{pad}  only with switches on: {', '.join(sw)}")
    return out


# ------------------------------------------------------------------ trade list check


def _lenient_json(text: str):
    """Vanilla-style asset JSON: comments, trailing commas, unquoted keys."""
    text = re.sub(r"(?m)^\s*//.*$", "", text)
    text = re.sub(r"(?<![:\"])//[^\n\"]*$", "", text, flags=re.M)
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r",(\s*[}\]])", r"\1", text)
    text = re.sub(r"([{,]\s*)([A-Za-z_][A-Za-z0-9_]*)\s*:", r'\1"\2":', text)
    return json.loads(text)


@dataclass(frozen=True)
class TradeEntry:
    code: str  # with its domain
    side: str  # "buying" or "selling"
    player_supplied: bool = False
    special: bool = False  # a "kind" entry (a map, a lead): a service the maps system prices
    override: bool = False  # a "price" with a "priceReason": priced by hand, not from the table

    @property
    def bought(self) -> bool:
        return self.side == "buying" or (self.side == "selling" and self.player_supplied)

    @property
    def valued(self) -> bool:
        """Priced from the value table: every entry but a special one or a price override."""
        return not self.special and not self.override


def trade_list_entries(directory: Path) -> dict[str, list[TradeEntry]]:
    """Every entry of every trade list file (docs/trading.md, Trade lists), per file name, in file
    order. Missing folder: none."""
    found: dict[str, list[TradeEntry]] = {}
    if not directory.is_dir():
        return found

    def walk(node, side, acc):
        if isinstance(node, dict):
            code = node.get("code")
            if side and isinstance(code, str) and any(k in node for k in ("type", "price", "stacksize", "stock", "kind")):
                reason = node.get("priceReason")
                acc.append(TradeEntry(code if ":" in code else "game:" + code, side,
                                      node.get("playerSupplied") is True, "kind" in node,
                                      "price" in node and isinstance(reason, str) and bool(reason.strip())))
            for k, v in node.items():
                walk(v, side or (k if k in ("buying", "selling") else None), acc)
        elif isinstance(node, list):
            for v in node:
                walk(v, side, acc)

    for path in sorted(directory.rglob("*.json")):
        acc: list[TradeEntry] = []
        walk(_lenient_json(path.read_text(encoding="utf-8")), None, acc)
        found[path.name] = acc
    return found


def trade_list_codes(directory: Path) -> dict[str, list[str]]:
    """The item codes the trade lists price from the value table, per trade list file: every entry,
    bought or sold, but the special ones (maps and leads, which the maps system prices) and those
    with a price override (`price` with a `priceReason`: schematics). A trader asks an item's value
    and pays 1.5 x it (docs/trading.md, "Trade list format"), so each needs one."""
    return {name: sorted({e.code for e in entries if e.valued})
            for name, entries in trade_list_entries(directory).items()}


def family_prefixes(code: str):
    """ItemValues.FamilyPrefixes (Trading/Values/Core/ItemValues.cs): every prefix of the code ending
    in '-', longest first, down to the one after the path's first segment."""
    first = code.find("-", code.find(":") + 1)
    if first < 0:
        return
    i = code.rfind("-")
    while i >= first:
        yield code[: i + 1]
        i = code.rfind("-", 0, i)


def unrouted(export: dict, val: Valuation, rules: Rules, tradelists: Path) -> list[str]:
    """Dead trade list entries (#506): an item of the export that nothing values (no route makes
    it, and no raw, override or fallback prices it) and the handbook hides: retired by the pack
    (Immersive Woodworking's pit saws and blades) or technical. Bought or sold, such an entry comes
    off the list, even when the item's variant family has a value the mod would fall back to.
    (What the pack removes outright, Hydrate or Diedrate's tun, is not in the export at all.)
    Schematics are worth nothing by rule and traders are their only source, and a special entry (a
    map, a lead) is a service: neither counts."""
    dead = []
    items = export["items"]
    for name, entries in trade_list_entries(tradelists).items():
        for e in entries:
            if e.special or "*" in e.code or e.code not in items or e.code in val.value or rules.is_schematic(e.code):
                continue
            if items[e.code].get("handbookVisible") is False:
                dead.append(f"{name}: {e.code} ({'bought' if e.bought else 'sold'}) is retired: hidden from "
                            f"the handbook and nothing values it; take it off the trade list")
    return dead


def table_drift(fresh: dict, shipped: dict) -> list[str]:
    """What differs between a rebuilt table and the shipped one (values, floorZero, perLitre,
    switches), one line per code: "code: shipped -> rebuilt"."""
    lines = []
    fv, sv = fresh.get("values", {}), shipped.get("values", {})
    for c in sorted(set(fv) | set(sv)):
        if fv.get(c) != sv.get(c):
            lines.append(f"{c}: {sv.get(c, 'no value')} -> {fv.get(c, 'no value')}")
    fz, sz = set(fresh.get("floorZero", [])), set(shipped.get("floorZero", []))
    for c in sorted(fz ^ sz):
        lines.append(f"{c}: floorZero {'added' if c in fz else 'removed'}")
    fl, sl = fresh.get("perLitre", {}), shipped.get("perLitre", {})
    for c in sorted(set(fl) | set(sl)):
        if fl.get(c) != sl.get(c):
            lines.append(f"{c}: perLitre {sl.get(c, 'none')} -> {fl.get(c, 'none')}")
    fs, ss = fresh.get("switches", {}), shipped.get("switches", {})
    for c in sorted(set(fs) | set(ss)):
        if fs.get(c) != ss.get(c):
            lines.append(f"{c}: switches {ss.get(c, [])} -> {fs.get(c, [])}")
    return lines


DRIFT_SAMPLE = 25


def check(values: dict[str, float] | set[str], tradelists: Path, what: str = "",
          exempt=lambda code: False) -> list[str]:
    """Trade list entries priced from the table without a value in `values` (derived values, or the
    shipped table). As the mod looks values up (ItemValues.Lookup): a code missing from the table
    takes its variant family's average, and a code with * the average of what it matches; only a
    code with neither is missing. `exempt` codes (schematics, worth nothing by rule) need a price
    override (`price` with a `priceReason`) instead."""
    families = {p for c in values for p in family_prefixes(c)}
    problems = []
    for name, codes in trade_list_codes(tradelists).items():
        for code in codes:
            if exempt(code):
                problems.append(f"{name}: {code} is a schematic, which has no value: give it a price "
                                f"override with a priceReason")
                continue
            if "*" in code:
                if not any(fnmatch.fnmatchcase(c, code) for c in values):
                    problems.append(f"{name}: {code} matches nothing with a value{what}")
            elif code not in values and not any(p in families for p in family_prefixes(code)):
                problems.append(f"{name}: {code} has no value{what}")
    return problems


# ------------------------------------------------------------------ CLI


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=(__doc__ or "").split("\n\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("build", "report", "explain", "check"):
        p = sub.add_parser(name)
        p.add_argument("export", type=Path, help="recipe export (recipes.json)")
        p.add_argument("--rules", type=Path, default=HERE, help="folder with raw-values.json, markups.json, overrides.json")
        if name == "explain":
            p.add_argument("code")
            p.add_argument("--depth", type=int, default=8)
        if name == "build":
            p.add_argument("--out", type=Path, default=DEFAULT_OUT)
            p.add_argument("--report", type=Path, default=REPO / "build/item-values-report.md")
        if name == "report":
            p.add_argument("--json", action="store_true", help="the report as JSON")
        if name == "check":
            p.add_argument("--tradelists", type=Path, default=DEFAULT_TRADELISTS, help="the pack's trade lists")
            p.add_argument("--table", type=Path, default=DEFAULT_OUT, help="the shipped table, checked too")
    args = ap.parse_args(argv)

    export = load_json(args.export)
    rules = Rules.load(args.rules)
    val = solve(export, rules)

    if args.cmd == "build":
        write_table(args.out, table(export, val))
        rep = report(export, val, rules)
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(report_markdown(rep, export, val, SAMPLES), encoding="utf-8")
        args.report.with_suffix(".json").write_text(json.dumps(rep, indent=1) + "\n", encoding="utf-8")
        print(f"{rep['valued']}/{rep['items']} items valued ({rep['coverage']:.1%}), "
              f"{rep['floorZero']} worthless, {len(rep['belowIngredients'])} below ingredients")
        print(f"wrote {args.out} and {args.report}")
    elif args.cmd == "report":
        rep = report(export, val, rules)
        print(json.dumps(rep, indent=1) if args.json else report_markdown(rep, export, val, SAMPLES))
    elif args.cmd == "explain":
        code = args.code if ":" in args.code else "game:" + args.code
        print("\n".join(explain(export, val, rules, code, max_depth=args.depth)))
        others = val.routes_by_output.get(code, [])
        if len(others) > 1:
            print(f"\n{len(others)} routes make it:")
            costs = sorted(((route_cost(r, val.value, rules), r.recipe) for r in others),
                           key=lambda t: math.inf if t[0] is None else t[0])
            for c, rid in costs[:15]:
                print(f"  {'unpriced' if c is None else f'{c:.4f}'}  {rid}")
    elif args.cmd == "check":
        # In GitHub Actions a failure's headline is also an error annotation on the run.
        annotate = "::error::" if os.environ.get("GITHUB_ACTIONS") == "true" else ""
        failed = False
        problems = check(val.value, args.tradelists, exempt=rules.is_schematic)
        if args.table.exists():
            shipped = json.loads(args.table.read_text(encoding="utf-8"))
            problems += check(shipped["values"], args.tradelists, " in the shipped table", rules.is_schematic)
            drift = table_drift(table(export, val), shipped)
            if drift:
                failed = True
                try:
                    shown = args.table.resolve().relative_to(REPO)
                except ValueError:
                    shown = args.table
                print(f"{annotate}the shipped table {shown} is stale: {len(drift)} differences from what "
                      f"{args.export} derives (shipped -> rebuilt):", file=sys.stderr)
                for line in drift[:DRIFT_SAMPLE]:
                    print(f"  {line}", file=sys.stderr)
                if len(drift) > DRIFT_SAMPLE:
                    print(f"  ... and {len(drift) - DRIFT_SAMPLE} more", file=sys.stderr)
                print(f"rebuild it from this export and commit it:\n"
                      f"  python3 tools/item-values/itemvalues.py build {args.export}\n"
                      f"(in CI the export is this run's recipe-export artifact; locally "
                      f"'packtool.py smoke --export build/recipes.json' writes one, see tools/item-values/README.md)",
                      file=sys.stderr)
        lists = trade_list_codes(args.tradelists)
        if lists:
            n = sum(len(v) for v in lists.values())
            distinct = len({c for v in lists.values() for c in v})
            print(f"validated {n} trade list items priced from values ({distinct} distinct codes) in {len(lists)} lists")
        dead = unrouted(export, val, rules, args.tradelists)
        for p in problems + dead:
            print(p, file=sys.stderr)
        if problems:
            print(f"{annotate}{len(problems)} trade list entries lack a value (or a price override with a priceReason)", file=sys.stderr)
        if dead:
            print(f"{annotate}{len(dead)} trade list entries name items nothing values (no route)", file=sys.stderr)
        if failed or problems or dead:
            return 1
        print(f"every trade list item has a value ({sum(len(v) for v in lists.values())} entries)" if lists
              else "no trade lists: nothing to check")
        if args.table.exists():
            print("the shipped table matches this export")
    return 0


if __name__ == "__main__":
    sys.exit(main())
