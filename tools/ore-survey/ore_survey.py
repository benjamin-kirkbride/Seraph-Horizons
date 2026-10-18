#!/usr/bin/env python3
"""Summarise ore survey runs: deposits per metal, minerals, rich gravel fields.

  ore_survey.py summary RUN_DIR... [--dump DUMP] [--json OUT]
  ore_survey.py compare BEFORE.json AFTER.json [--targets FILE]
  ore_survey.py check SUMMARY.json [--targets FILE]
  ore_survey.py census CENSUS_DIR... --dump DUMP [--json OUT] [--check [--source all|cell|district]]

A RUN_DIR holds one seed's orescan.json and orescan.json.cells.csv (from run.sh). The dump
(orescan.json from `run.sh --dump`) turns ore blocks into ingots; by default it is the
run's sibling folder with `-s<seed>` replaced by `-dump`. README.md describes the method.
Stdlib only.
"""

from __future__ import annotations

import argparse
import collections
import csv
import json
import math
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
GRADES = {"poor", "medium", "rich", "bountiful", "low", "high"}
# Ore types without a smeltable nugget that still count as a resource, by group.
MINERALS = {
    "anthracite": "coal", "bituminouscoal": "coal", "lignite": "coal",
    "sulfur": "sulfur", "saltpeterore": "saltpeter", "borax": "borax", "cinnabar": "cinnabar",
    "alum": "alum", "rhodochrosite": "manganese",
}
UNITS_PER_NUGGET = 5
# Ores whose metal is read from another nugget than their own name's last part: argentiferous
# galena is a lead ore since #690 (its silver is cupelled); vanilla's nugget for it is native silver.
NUGGET_OF = {"galena_nativesilver": "galena"}
NUGGETS_PER_INGOT = 20
SURFACE_DEPTH = 6  # a deposit "reaches the surface" if any of its ore is this close
ONE_GRADE = 0.8  # a deposit "is one grade" if its commonest grade holds this share of its blocks


def quantile(xs, p):
    """The scratch survey's quantile (no interpolation), kept so the epic's table reproduces."""
    xs = sorted(xs)
    return xs[min(len(xs) - 1, int(p * len(xs)))] if xs else 0


# ---------------------------------------------------------------------------------------------
# Block codes


class Classifier:
    """What a recorded block code counts as, from the dump of drops and metal units."""

    def __init__(self, dump: dict):
        self.blocks = dump["blocks"]
        self.items = dump["items"]
        self.cache: dict[str, tuple | None] = {}

    def __call__(self, code: str):
        """-> ("metal", metal, ore, nuggets per block) | ("mineral", group, ore, 0)
        | ("gravel", "richgravel", rock, 0) | None (gems, quartz, loose surface ores, ...)."""
        if code not in self.cache:
            self.cache[code] = self._classify(code)
        return self.cache[code]

    def _classify(self, code):
        path = code.split(":", 1)[-1]
        parts = path.split("-")
        if parts[0] == "richgravel":
            return ("gravel", "richgravel", parts[-1], 0)
        if parts[0] == "looseores":
            return None
        if parts[0] == "ore":
            ore = parts[2] if len(parts) > 2 and parts[1] in GRADES else parts[1]
        else:
            ore = parts[0]
        metal = self.metal(ore)
        if metal:
            return ("metal", metal, ore, self.nuggets_per_block(code))
        if ore in MINERALS:
            return ("mineral", MINERALS[ore], ore, 0)
        return None

    def metal(self, ore: str):
        """The metal an ore's nugget smelts to (quartz_nativegold -> nativegold -> gold;
        galena_nativesilver -> galena -> lead, NUGGET_OF)."""
        nugget = self.items.get("Item game:nugget-" + NUGGET_OF.get(ore, ore.split("_")[-1]))
        smelts = nugget and nugget.get("smeltsTo")
        if not smelts:
            return None
        return smelts.split(":", 1)[-1].replace("ingot-", "").replace("ironbloom", "iron")

    def nuggets_per_block(self, code: str) -> float:
        """Sum over the block's drops of (average count x metal units) / units per nugget:
        one raw ore with single ore drops (vanilla's 1.25 plus 0.01 crystallised ore without),
        each with its grade's metalUnits."""
        units = 0.0
        for drop in self.blocks.get(code) or []:
            item = self.items.get(drop["code"])
            if item and item.get("metalUnits"):
                units += drop["avg"] * item["metalUnits"]
        return units / UNITS_PER_NUGGET


def grade_of(code: str):
    """An ore block's grade (ore-rich-nativecopper-granite -> rich), or None."""
    parts = code.split(":", 1)[-1].split("-")
    return parts[1] if len(parts) > 2 and parts[0] == "ore" and parts[1] in GRADES else None


def top_grade_share(d: dict):
    """The share of a deposit's graded blocks in its commonest grade, or None if it has none."""
    g = d.get("grades") or {}
    n = sum(g.values())
    return max(g.values()) / n if n else None


# ---------------------------------------------------------------------------------------------
# Grouping


class UnionFind:
    def __init__(self, n):
        self.parent = list(range(n))

    def find(self, i):
        while self.parent[i] != i:
            self.parent[i] = self.parent[self.parent[i]]
            i = self.parent[i]
        return i

    def union(self, i, j):
        self.parent[self.find(i)] = self.find(j)

    def groups(self):
        out = collections.defaultdict(list)
        for i in range(len(self.parent)):
            out[self.find(i)].append(i)
        return list(out.values())


def fragments(cells: dict) -> list[dict]:
    """26-neighbour flood fill over 8-block cells of one ore type.

    cells: {(x, z, y): [blocks, nuggets, minDepth, {grade: blocks}]} -> [{blocks, nuggets, minDepth,
    grades, x, z}], x and z the unweighted mean of the cell centres in blocks."""
    seen = set()
    out = []
    for start in cells:
        if start in seen:
            continue
        seen.add(start)
        stack = [start]
        blocks = 0
        nuggets = 0.0
        mind = math.inf
        grades = collections.Counter()
        xs = zs = n = 0
        while stack:
            k = stack.pop()
            c = cells[k]
            blocks += c[0]
            nuggets += c[1]
            mind = min(mind, c[2])
            if len(c) > 3:
                grades.update(c[3])
            xs += k[0]
            zs += k[1]
            n += 1
            for dx in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        nk = (k[0] + dx, k[1] + dz, k[2] + dy)
                        if nk in cells and nk not in seen:
                            seen.add(nk)
                            stack.append(nk)
        out.append({"blocks": blocks, "nuggets": nuggets, "minDepth": mind, "grades": dict(grades),
                    "x": 8 * xs / n + 4, "z": 8 * zs / n + 4})
    return out


def merge(frags: list[dict], link: float) -> list[dict]:
    """Single linkage on fragment centres closer than `link` metres. A merged deposit sits at
    its largest fragment's centre; it reaches the surface if any fragment does."""
    uf = UnionFind(len(frags))
    for i in range(len(frags)):
        for j in range(i):
            if math.hypot(frags[i]["x"] - frags[j]["x"], frags[i]["z"] - frags[j]["z"]) < link:
                uf.union(i, j)
    out = []
    for g in uf.groups():
        members = [frags[i] for i in g]
        big = max(members, key=lambda f: f["blocks"])
        out.append({
            "blocks": sum(f["blocks"] for f in members),
            "nuggets": sum(f["nuggets"] for f in members),
            "minDepth": min(f["minDepth"] for f in members),
            "grades": dict(sum((collections.Counter(f.get("grades", {})) for f in members), collections.Counter())),
            "x": big["x"], "z": big["z"],
            "ores": sorted({o for f in members for o in f.get("ores", [])}),
            "fragments": len(members),
        })
    return out


def fields(cells: dict, link: float) -> list[dict]:
    """Rich gravel cells joined when their centres are within `link` metres horizontally
    (any height). cells: {(x, z, y): [blocks, _, minDepth]} -> [{blocks, minDepth, x, z}],
    x and z block-weighted."""
    keys = list(cells)
    index = {k: i for i, k in enumerate(keys)}
    columns = collections.defaultdict(list)
    for k in keys:
        columns[(k[0], k[1])].append(k)
    reach = int(link // 8)
    uf = UnionFind(len(keys))
    for (x, z), ks in columns.items():
        for k in ks[1:]:
            uf.union(index[k], index[ks[0]])
        for dx in range(-reach, reach + 1):
            for dz in range(-reach, reach + 1):
                if (dx or dz) and 8 * math.hypot(dx, dz) <= link and (x + dx, z + dz) in columns:
                    uf.union(index[ks[0]], index[columns[(x + dx, z + dz)][0]])
    out = []
    for g in uf.groups():
        members = [keys[i] for i in g]
        blocks = sum(cells[k][0] for k in members)
        out.append({
            "blocks": blocks,
            "minDepth": min(cells[k][2] for k in members),
            "x": sum((8 * k[0] + 4) * cells[k][0] for k in members) / blocks,
            "z": sum((8 * k[1] + 4) * cells[k][0] for k in members) / blocks,
        })
    return out


def nearest(points: list[dict]) -> list[float]:
    """Each point's distance to the nearest other point of the same seed."""
    by_seed = collections.defaultdict(list)
    for p in points:
        by_seed[p["seed"]].append(p)
    out = []
    for ps in by_seed.values():
        for a in ps:
            d = [math.hypot(a["x"] - b["x"], a["z"] - b["z"]) for b in ps if b is not a]
            if d:
                out.append(min(d))
    return out


# ---------------------------------------------------------------------------------------------
# Runs


def run_files(path: Path) -> tuple[Path, Path]:
    path = Path(path)
    doc = path if path.suffix == ".json" else path / "orescan.json"
    return doc, Path(str(doc) + ".cells.csv")


def default_dump(run: Path) -> Path:
    run = Path(run)
    if run.suffix == ".json":
        run = run.parent
    return run.parent / (re.sub(r"-s\d+$", "", run.name) + "-dump") / "orescan.json"


def read_run(path, classify: Classifier, opts) -> dict:
    """One seed -> its metal fragments, pockets, merged deposits, minerals and gravel fields."""
    doc_path, csv_path = run_files(path)
    doc = json.loads(Path(doc_path).read_text())
    seed = doc.get("seed")
    # (kind, group, ore) -> {(x, z, y): [blocks, nuggets, minDepth, {grade: blocks}]}
    by_ore = collections.defaultdict(dict)
    with open(csv_path, newline="") as f:
        for code, x, z, y, n, dep in csv.reader(f):
            kind = classify(code)
            if not kind:
                continue
            c = by_ore[kind[:3]].setdefault((int(x), int(z), int(y)), [0, 0.0, math.inf, collections.Counter()])
            c[0] += int(n)
            c[1] += int(n) * kind[3]
            c[2] = min(c[2], int(dep))
            if (grade := grade_of(code)):
                c[3][grade] += int(n)
    groups: dict[tuple, dict] = collections.defaultdict(
        lambda: {"frags": [], "pockets": [], "totalBlocks": 0, "totalNuggets": 0.0})
    gravel = []
    for (kind, group, ore), cells in by_ore.items():
        if kind == "gravel":
            gravel.append(cells)
            continue
        g = groups[(kind, group)]
        for fr in fragments(cells):
            fr["ores"] = [ore]
            fr["seed"] = seed
            g["totalBlocks"] += fr["blocks"]
            g["totalNuggets"] += fr["nuggets"]
            (g["frags"] if fr["blocks"] >= opts.min_blocks else g["pockets"]).append(fr)
    out = {"seed": seed, "areaKm2": doc["areaKm2"], "dir": str(path), "metal": {}, "mineral": {}, "gravel": []}
    for (kind, group), g in groups.items():
        deps = merge(g["frags"], opts.link)
        for d in deps:
            d["seed"] = seed
        out[kind][group] = {**g, "deposits": deps}
    gcells = {}
    for cells in gravel:  # one field may mix the rich gravel of several rocks
        for k, c in cells.items():
            t = gcells.setdefault(k, [0, 0.0, math.inf])
            t[0] += c[0]
            t[2] = min(t[2], c[2])
    out["gravel"] = fields(gcells, opts.gravel_link)
    for fl in out["gravel"]:
        fl["seed"] = seed
    return out


def summarise(runs: list[dict], opts) -> dict:
    area = sum(r["areaKm2"] for r in runs)

    def merged(kind, group, key):
        return [x for r in runs for x in r[kind].get(group, {}).get(key, [])]

    def total(kind, group, key):
        return sum(r[kind].get(group, {}).get(key, 0) for r in runs)

    metals = {}
    for metal in sorted({m for r in runs for m in r["metal"]}):
        deps = merged("metal", metal, "deposits")
        ingots = lambda d: d["nuggets"] / NUGGETS_PER_INGOT
        big = [d for d in deps if ingots(d) >= opts.min_ingots]
        iv = [ingots(d) for d in big]
        pockets = merged("metal", metal, "pockets")
        all_ingots = total("metal", metal, "totalNuggets") / NUGGETS_PER_INGOT
        tops = [t for t in map(top_grade_share, big) if t is not None]
        metals[metal] = {
            "deposits": len(big),
            "smallDeposits": len(deps) - len(big),
            "depositsPerKm2": len(big) / area,
            "ingotsP10": quantile(iv, .1), "ingotsMedian": quantile(iv, .5),
            "ingotsP90": quantile(iv, .9), "ingotsMax": max(iv, default=0),
            "nearestMedian": quantile(nearest(big), .5),
            "surfaceShare": sum(d["minDepth"] <= SURFACE_DEPTH for d in big) / len(big) if big else 0,
            "ingotsPerKm2": all_ingots / area,
            "pocketsPerKm2": len(pockets) / area,
            "pocketIngotsMedian": quantile([p["nuggets"] / NUGGETS_PER_INGOT for p in pockets], .5),
            "pocketSurfaceShare": sum(p["minDepth"] <= SURFACE_DEPTH for p in pockets) / len(pockets) if pockets else 0,
            "pocketShare": sum(p["nuggets"] for p in pockets) / NUGGETS_PER_INGOT / all_ingots if all_ingots else 0,
            # Grade within deposits (#944): how much of each is its commonest grade.
            "topGradeMedian": quantile(tops, .5),
            "oneGradeShare": sum(t >= ONE_GRADE for t in tops) / len(tops) if tops else 0,
            "ores": sorted({o for d in deps for o in d["ores"]}),
        }
    minerals = {}
    for group in sorted({m for r in runs for m in r["mineral"]}):
        deps = merged("mineral", group, "deposits")
        big = [d for d in deps if d["blocks"] >= opts.min_mineral_blocks]
        bv = [d["blocks"] for d in big]
        minerals[group] = {
            "deposits": len(big),
            "depositsPerKm2": len(big) / area,
            "blocksP10": quantile(bv, .1), "blocksMedian": quantile(bv, .5), "blocksP90": quantile(bv, .9),
            "nearestMedian": quantile(nearest(big), .5),
            "surfaceShare": sum(d["minDepth"] <= SURFACE_DEPTH for d in big) / len(big) if big else 0,
            "blocksPerKm2": total("mineral", group, "totalBlocks") / area,
        }
    allf = [f for r in runs for f in r["gravel"]]
    fl = [f for f in allf if f["blocks"] >= opts.min_field_blocks]
    fb = [f["blocks"] for f in fl]
    gravel = {
        "fields": len(fl),
        "fieldsPerKm2": len(fl) / area,
        "blocksP10": quantile(fb, .1), "blocksMedian": quantile(fb, .5), "blocksP90": quantile(fb, .9),
        "nearestMedian": quantile(nearest(fl), .5),
        "surfaceShare": sum(f["minDepth"] <= SURFACE_DEPTH for f in fl) / len(fl) if fl else 0,
        "blocksPerKm2": sum(f["blocks"] for f in allf) / area,
        "scatteredBlocksPerKm2": sum(f["blocks"] for f in allf if f["blocks"] < opts.min_field_blocks) / area,
    }
    return {
        "runs": [{"dir": r["dir"], "seed": r["seed"], "areaKm2": r["areaKm2"]} for r in runs],
        "areaKm2": area,
        "options": {k: getattr(opts, k) for k in ("min_blocks", "link", "min_ingots", "min_mineral_blocks", "gravel_link", "min_field_blocks")},
        "metals": metals, "minerals": minerals, "gravel": gravel,
    }


# ---------------------------------------------------------------------------------------------
# Output


def fmt(v, kind="n"):
    if kind == "%":
        return f"{v:.0%}"
    if kind == "f":
        return f"{v:.2f}"
    if kind == "a":  # km² per deposit
        return f"{1 / v:.1f}" if v else "-"
    return f"{v:,.0f}"


METAL_COLS = [("deposits/km²", "depositsPerKm2", "f"), ("km²/dep", "depositsPerKm2", "a"),
              ("ingots p10", "ingotsP10", "n"), ("median", "ingotsMedian", "n"), ("p90", "ingotsP90", "n"),
              ("max", "ingotsMax", "n"), ("nn m", "nearestMedian", "n"), ("surface", "surfaceShare", "%"),
              ("ingots/km²", "ingotsPerKm2", "n"), ("pockets/km²", "pocketsPerKm2", "f"),
              ("pocket med", "pocketIngotsMedian", "n"), ("pocket surf", "pocketSurfaceShare", "%"),
              ("in pockets", "pocketShare", "%"), ("top grade", "topGradeMedian", "%"),
              ("1 grade", "oneGradeShare", "%")]
MINERAL_COLS = [("deposits/km²", "depositsPerKm2", "f"), ("km²/dep", "depositsPerKm2", "a"),
                ("blocks p10", "blocksP10", "n"), ("median", "blocksMedian", "n"), ("p90", "blocksP90", "n"),
                ("nn m", "nearestMedian", "n"), ("surface", "surfaceShare", "%"), ("blocks/km²", "blocksPerKm2", "n")]
GRAVEL_COLS = [("fields/km²", "fieldsPerKm2", "f"), ("km²/field", "fieldsPerKm2", "a"),
               ("blocks p10", "blocksP10", "n"), ("median", "blocksMedian", "n"), ("p90", "blocksP90", "n"),
               ("nn m", "nearestMedian", "n"), ("surface", "surfaceShare", "%"),
               ("blocks/km²", "blocksPerKm2", "n"), ("scattered/km²", "scatteredBlocksPerKm2", "n")]


def table(rows: dict, cols, first: str) -> str:
    head = [first] + [c[0] for c in cols]
    body = [[name] + [fmt(r[key], kind) for _, key, kind in cols] for name, r in rows.items()]
    widths = [max(len(x[i]) for x in [head] + body) for i in range(len(head))]
    line = lambda xs: "  ".join(x.ljust(widths[0]) if i == 0 else x.rjust(widths[i]) for i, x in enumerate(xs))
    return "\n".join([line(head)] + [line(b) for b in body])


def format_summary(s: dict) -> str:
    o = s["options"]
    seeds = ", ".join(str(r["seed"]) for r in s["runs"])
    out = [f"{len(s['runs'])} seed(s) ({seeds}), {s['areaKm2']:.1f} km².",
           f"Deposit: ore of one metal within {o['link']:.0f} m (fragments of >= {o['min_blocks']} blocks),"
           f" >= {o['min_ingots']} ingots; smaller fragments are pockets. Surface: ore within {SURFACE_DEPTH} blocks.",
           f"Top grade: the median deposit's share in its commonest grade; 1 grade: deposits with >= {ONE_GRADE:.0%} in one.",
           "", "Metals", table(s["metals"], METAL_COLS, "metal"),
           "", f"Coal and industrial minerals (deposits >= {o['min_mineral_blocks']} blocks)",
           table(s["minerals"], MINERAL_COLS, "group"),
           "", f"Rich gravel (cells within {o['gravel_link']:.0f} m joined; fields >= {o['min_field_blocks']} blocks)",
           table({"richgravel": s["gravel"]}, GRAVEL_COLS, "")]
    return "\n".join(out)


def ratio(a, b):
    if not a:
        return "new" if b else "-"
    return f"{b / a:.2f}x"


def format_compare(a: dict, b: dict) -> str:
    out = [f"before: {len(a['runs'])} seed(s), {a['areaKm2']:.1f} km²; after: {len(b['runs'])} seed(s), {b['areaKm2']:.1f} km²"]
    sections = [("Metals", "metals", [("deposits/km²", "depositsPerKm2", "f"), ("ingots med", "ingotsMedian", "n"),
                                      ("p90", "ingotsP90", "n"), ("nn m", "nearestMedian", "n"),
                                      ("surface", "surfaceShare", "%"), ("ingots/km²", "ingotsPerKm2", "n"),
                                      ("top grade", "topGradeMedian", "%"), ("1 grade", "oneGradeShare", "%")]),
                ("Minerals", "minerals", [("deposits/km²", "depositsPerKm2", "f"), ("blocks med", "blocksMedian", "n"),
                                          ("blocks/km²", "blocksPerKm2", "n")])]
    for title, key, cols in sections:
        head = [title.lower()[:-1]] + [f"{c[0]} {w}" for c in cols for w in ("before", "after", "ratio")]
        body = []
        for name in sorted(set(a[key]) | set(b[key])):
            ra, rb = a[key].get(name), b[key].get(name)
            row = [name]
            for _, k, kind in cols:
                va, vb = (ra or {}).get(k, 0), (rb or {}).get(k, 0)
                row += [fmt(va, kind), fmt(vb, kind), ratio(va, vb)]
            body.append(row)
        widths = [max(len(x[i]) for x in [head] + body) for i in range(len(head))]
        out += ["", title] + ["  ".join(x.ljust(widths[0]) if i == 0 else x.rjust(widths[i]) for i, x in enumerate(r)) for r in [head] + body]
    out += ["", "Rich gravel"]
    for label, k, kind in GRAVEL_COLS:
        va, vb = a["gravel"][k], b["gravel"][k]
        out.append(f"  {label:<14}{fmt(va, kind):>10}{fmt(vb, kind):>10}{ratio(va, vb):>9}")
    return "\n".join(out)


# ---------------------------------------------------------------------------------------------
# Targets


def check(s: dict, targets: dict) -> list[tuple[str, str, str, bool]]:
    """-> [(what, measured, target, ok)]. Sizes are read as p10 / median / p90 against small /
    typical / large, each within the tolerance factor; densities likewise; the gravel field
    median must fall in the blocks range."""
    tol = targets["tolerance"]
    within = lambda v, t: t / tol <= v <= t * tol
    rows = []
    per = targets["areaPerDeposit_km2"]
    for metal, t in targets["metals"].items():
        m = s["metals"].get(metal)
        if not m or not m["deposits"]:
            rows.append((f"{metal} deposits", "none", f"1 per {per:g} km²", False))
            continue
        d = m["depositsPerKm2"] * per
        rows.append((f"{metal} deposits per {per:g} km²", f"{d:.2f}", "1", within(d, 1)))
        for label, key, tk in (("p10", "ingotsP10", "small"), ("median", "ingotsMedian", "typical"), ("p90", "ingotsP90", "large")):
            rows.append((f"{metal} ingots {label}", fmt(m[key]), f"{t[tk]:,}", within(m[key], t[tk])))
    g, tg = s["gravel"], targets["gravel"]
    per = tg["areaPerField_km2"]
    d = g["fieldsPerKm2"] * per
    rows.append((f"gravel fields per {per:g} km²", f"{d:.2f}", "1", within(d, 1)))
    lo, hi = tg["blocks"]
    rows.append(("gravel blocks per field, median", fmt(g["blocksMedian"]), f"{lo}-{hi}", lo <= g["blocksMedian"] <= hi))
    return rows


def format_check(rows, targets) -> str:
    w = max(len(r[0]) for r in rows)
    out = [f"Targets (tolerance {targets['tolerance']}x):"]
    out += [f"  {'ok  ' if ok else 'FAIL'}  {what:<{w}}  {got:>8}  target {want}" for what, got, want, ok in rows]
    n = sum(not r[3] for r in rows)
    out.append(f"{len(rows) - n} of {len(rows)} within target")
    return "\n".join(out)


def load_targets(path) -> dict:
    return json.loads(Path(path).read_text())


# ---------------------------------------------------------------------------------------------
# Census (run.sh --census): one record per deposit, flooded over chunk columns


def census_files(path: Path) -> tuple[Path, Path]:
    path = Path(path)
    doc = path if path.suffix == ".json" else path / "census.json"
    return doc, Path(str(doc) + ".cells.csv")


def read_census(path, classify: Classifier, opts) -> dict:
    """One census run -> {seed, records, deposits}. Records of one metal that share an 8-block cell
    are one deposit (a vein flooded from two anchors, or a cell's deposit run into a district vein);
    its cells are their union. A deposit's ingots are those of its fragments of at least
    `min_blocks` (smaller ones are pockets), as the summary counts them."""
    doc_path, csv_path = census_files(path)
    doc = json.loads(Path(doc_path).read_text())
    seed = doc.get("seed")
    records = {r["id"]: r for r in doc["deposits"]}
    cells_of = collections.defaultdict(dict)  # id -> {(code, x, z, y): (blocks, minDepth)}
    with open(csv_path, newline="") as f:
        for rid, code, x, z, y, n, dep in csv.reader(f):
            cells_of[int(rid)][(code, int(x), int(z), int(y))] = (int(n), int(dep))
    ids = sorted(records)
    index = {rid: i for i, rid in enumerate(ids)}
    uf = UnionFind(len(ids))
    owner = {}
    for rid in ids:
        metal = records[rid]["metal"]
        for (code, x, z, y) in cells_of.get(rid, {}):
            key = (metal, x, z, y)
            if key in owner:
                uf.union(index[rid], index[owner[key]])
            else:
                owner[key] = rid
    deposits = []
    for group in uf.groups():
        members = [records[ids[i]] for i in group]
        flooded = [m for m in members if m["status"] == "flooded"]
        if not flooded:
            continue
        metal = members[0]["metal"]
        cells = {}
        for m in members:
            cells.update(cells_of.get(m["id"], {}))
        by_ore = collections.defaultdict(dict)
        for (code, x, z, y), (n, dep) in cells.items():
            kind = classify(code)
            if not kind or kind[0] != "metal" or kind[1] != metal:
                continue
            c = by_ore[kind[2]].setdefault((x, z, y), [0, 0.0, math.inf, collections.Counter()])
            c[0] += n
            c[1] += n * kind[3]
            c[2] = min(c[2], dep)
            if (grade := grade_of(code)):
                c[3][grade] += n
        kept, pockets = [], []
        for ore, ocells in by_ore.items():
            for fr in fragments(ocells):
                fr["ores"] = [ore]
                (kept if fr["blocks"] >= opts.min_blocks else pockets).append(fr)
        allf = kept + pockets
        blocks = sum(f["blocks"] for f in kept)
        kinds = sorted({m["kind"] for m in members})
        deposits.append({
            "seed": seed, "metal": metal,
            "source": "cell" if "cell" in kinds else "district",
            "kinds": kinds,
            "records": [m["id"] for m in members],
            "blocks": blocks,
            "nuggets": sum(f["nuggets"] for f in kept),
            "allNuggets": sum(f["nuggets"] for f in allf),
            "grades": dict(sum((collections.Counter(f["grades"]) for f in kept), collections.Counter())),
            "minDepth": min((f["minDepth"] for f in kept), default=math.inf),
            "x": sum(f["x"] * f["blocks"] for f in kept) / blocks if blocks else members[0]["anchorX"],
            "z": sum(f["z"] * f["blocks"] for f in kept) / blocks if blocks else members[0]["anchorZ"],
            "capped": any(m.get("capped") for m in members),
            "columns": sum(m.get("columns", 0) for m in members),
            "generated": sum(m.get("generated", 0) for m in members),
            "district": next((m["district"] for m in members if m.get("district")), None),
            "rocks": next((m["rocks"] for m in members if m.get("rocks")), None),
            "cell": next(([m["cellX"], m["cellZ"], m["spot"]] for m in members if m["kind"] == "cell"), None),
        })
    none = [r for r in records.values() if r["status"] != "flooded"]
    return {"seed": seed, "dir": str(path), "seconds": doc.get("seconds", 0),
            # columns generated: seen at their Vegetation pass (or, without early scans, completed)
            "columnsScanned": doc.get("columnsEarly") or doc.get("columnsCompleted") or doc.get("columnsScanned", 0),
            "records": list(records.values()),
            "none": none, "deposits": deposits}


SOURCES = ("cell", "district", "all")


def summarise_census(runs: list[dict], opts) -> dict:
    ingots = lambda d: d["nuggets"] / NUGGETS_PER_INGOT
    out = {"runs": [{"dir": r["dir"], "seed": r["seed"], "seconds": r["seconds"], "columnsScanned": r["columnsScanned"]} for r in runs],
           "options": {"min_blocks": opts.min_blocks, "min_ingots": opts.min_ingots}, "metals": {}}
    for source in SOURCES:
        deps = [d for r in runs for d in r["deposits"] if source == "all" or d["source"] == source]
        rows = {}
        for metal in sorted({d["metal"] for d in deps}):
            md = [d for d in deps if d["metal"] == metal]
            big = [d for d in md if ingots(d) >= opts.min_ingots]
            iv = [ingots(d) for d in big]
            tops = [t for t in map(top_grade_share, big) if t is not None]
            none = [x for r in runs for x in r["none"] if x["metal"] == metal and source in ("cell", "all") and x["kind"] == "cell"]
            rows[metal] = {
                "deposits": len(big), "small": len(md) - len(big), "none": len(none),
                "ingotsP10": quantile(iv, .1), "ingotsMedian": quantile(iv, .5),
                "ingotsP90": quantile(iv, .9), "ingotsMax": max(iv, default=0),
                "surfaceShare": sum(d["minDepth"] <= SURFACE_DEPTH for d in big) / len(big) if big else 0,
                "topGradeMedian": quantile(tops, .5),
                "oneGradeShare": sum(t >= ONE_GRADE for t in tops) / len(tops) if tops else 0,
                "cappedShare": sum(d["capped"] for d in md) / len(md) if md else 0,
                "columnsMedian": quantile([d["columns"] for d in md], .5),
            }
        out["metals"][source] = rows
    vein = lambda d: {"seed": d["seed"], "metal": d["metal"], "ingots": round(ingots(d), 1),
                      "code": (d["district"] or {}).get("code"), "zoneKind": (d["district"] or {}).get("zoneKind"),
                      "rocks": d["rocks"], "x": round(d["x"]), "z": round(d["z"]), "capped": d["capped"]}
    out["districtVeins"] = [vein(d) for r in runs for d in r["deposits"] if d["source"] == "district"]
    # Per district type and metal: how its veins come out, and how much of the band is the host rock
    # (a shoot is counted whole where the host is a country rock, docs/oregen.md "Host rock").
    by_type = collections.defaultdict(list)
    for r in runs:
        for d in r["deposits"]:
            if d["source"] == "district" and d["district"]:
                by_type[(d["district"]["code"], d["metal"])].append(d)
    out["districtTypes"] = {
        f"{code} {metal}": {
            "veins": len(ds), "deposits": sum(ingots(d) >= opts.min_ingots for d in ds),
            "ingotsMedian": quantile([ingots(d) for d in ds], .5), "ingotsMax": max(ingots(d) for d in ds),
            "hostShareMedian": quantile([(d["rocks"] or {}).get(d["district"].get("host", "").split(":")[-1], 0) for d in ds], .5),
        } for (code, metal), ds in sorted(by_type.items())}
    seconds = sum(r["seconds"] for r in runs)
    records = sum(len(r["records"]) for r in runs)
    out["throughput"] = {"records": records, "deposits": sum(len(r["deposits"]) for r in runs),
                         "serverSeconds": seconds, "columnsScanned": sum(r["columnsScanned"] for r in runs),
                         "recordsPerServerHour": records / seconds * 3600 if seconds else 0}
    return out


CENSUS_COLS = [("deposits", "deposits", "n"), ("small", "small", "n"), ("none", "none", "n"),
               ("ingots p10", "ingotsP10", "n"), ("median", "ingotsMedian", "n"), ("p90", "ingotsP90", "n"),
               ("max", "ingotsMax", "n"), ("surface", "surfaceShare", "%"), ("top grade", "topGradeMedian", "%"),
               ("1 grade", "oneGradeShare", "%"), ("capped", "cappedShare", "%"), ("columns", "columnsMedian", "n")]


def format_census(s: dict) -> str:
    o = s["options"]
    t = s["throughput"]
    seeds = ", ".join(str(r["seed"]) for r in s["runs"])
    out = [f"{len(s['runs'])} seed(s) ({seeds}): {t['records']} records, {t['deposits']} deposits,"
           f" {t['columnsScanned']:,} columns generated in {t['serverSeconds'] / 3600:.2f} server hours"
           f" ({t['recordsPerServerHour']:.0f} records per server hour).",
           f"Deposit: >= {o['min_ingots']} ingots in fragments of >= {o['min_blocks']} blocks; small: flooded, under that;"
           " none: the cell has no deposit. Capped and columns over every flooded deposit."]
    for source in SOURCES:
        title = {"cell": "Ore cells", "district": "District veins", "all": "All"}[source]
        out += ["", title, table(s["metals"][source], CENSUS_COLS, "metal")]
    if s.get("districtTypes"):
        cols = [("veins", "veins", "n"), ("deposits", "deposits", "n"), ("median", "ingotsMedian", "n"),
                ("max", "ingotsMax", "n"), ("host rock in band", "hostShareMedian", "%")]
        out += ["", f"District veins by type (every vein, small ones too; deposits >= {o['min_ingots']} ingots;"
                    " host rock in band: the median vein's share of its band that is the district's host rock)",
                table(s["districtTypes"], cols, "district metal")]
    veins = s["districtVeins"]
    if veins:
        out += ["", "District veins (ingots; the commonest rocks in the band around the vein's centre)"]
        for v in sorted(veins, key=lambda v: (v["code"] or "", v["metal"], v["ingots"])):
            rocks = ", ".join(f"{k.removeprefix('rock-')} {x:.0%}" for k, x in (v["rocks"] or {}).items())
            out.append(f"  seed {v['seed']:<6} {v['code'] or '?':<26} {v['metal']:<9} {v['zoneKind'] or '?':<10}"
                       f" {v['ingots']:>8,.0f}{' capped' if v['capped'] else '':<7}  {rocks}")
    return "\n".join(out)


def check_census(s: dict, targets: dict, source: str = "all") -> list[tuple[str, str, str, bool]]:
    """The size rows of `check` over a census's deposits: p10 / median / p90 against small /
    typical / large, within the tolerance factor. A census measures deposits, not their spacing,
    so no density rows."""
    tol = targets["tolerance"]
    within = lambda v, t: t / tol <= v <= t * tol
    rows = []
    for metal, t in targets["metals"].items():
        m = s["metals"][source].get(metal)
        if not m or not m["deposits"]:
            continue
        for label, key, tk in (("p10", "ingotsP10", "small"), ("median", "ingotsMedian", "typical"), ("p90", "ingotsP90", "large")):
            rows.append((f"{metal} ingots {label} ({m['deposits']} deposits)", fmt(m[key]), f"{t[tk]:,}", within(m[key], t[tk])))
    return rows


# ---------------------------------------------------------------------------------------------


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    sp = sub.add_parser("summary", help="summarise one or more runs (seeds)")
    sp.add_argument("runs", nargs="+", type=Path, help="run folders (or their orescan.json)")
    sp.add_argument("--dump", type=Path, help="dump orescan.json (default: the first run's -dump sibling)")
    sp.add_argument("--json", type=Path, help="also write the summary here, for compare and check")
    sp.add_argument("--min-blocks", type=int, default=100, help="smaller fragments are pockets (100)")
    sp.add_argument("--link", type=float, default=150, help="merge fragments of one metal closer than this, m (150)")
    sp.add_argument("--min-ingots", type=float, default=50, help="smaller merged deposits are not counted (50)")
    sp.add_argument("--min-mineral-blocks", type=int, default=1000, help="the same for minerals, blocks (1000)")
    sp.add_argument("--gravel-link", type=float, default=16, help="join rich gravel cells closer than this, m (16)")
    sp.add_argument("--min-field-blocks", type=int, default=100, help="smaller gravel groups are scattered (100)")
    sp.add_argument("--check", action="store_true", help="also check against the targets")
    sp.add_argument("--targets", type=Path, default=HERE / "targets.json")
    cp = sub.add_parser("compare", help="two summaries side by side, and the second against the targets")
    cp.add_argument("before", type=Path)
    cp.add_argument("after", type=Path)
    cp.add_argument("--targets", type=Path, default=HERE / "targets.json")
    np_ = sub.add_parser("census", help="summarise census runs (run.sh --census): sizes per metal, district veins")
    np_.add_argument("runs", nargs="+", type=Path, help="census run folders (or their census.json)")
    np_.add_argument("--dump", type=Path, required=True, help="dump orescan.json (run.sh --dump) for the same mods")
    np_.add_argument("--json", type=Path, help="also write the summary here")
    np_.add_argument("--min-blocks", type=int, default=100, help="smaller fragments are pockets, left out (100)")
    np_.add_argument("--min-ingots", type=float, default=50, help="smaller deposits are counted as small (50)")
    np_.add_argument("--check", action="store_true", help="also check sizes against the targets; exits 1 on a miss")
    np_.add_argument("--source", choices=SOURCES, default="all", help="which deposits --check reads (all)")
    np_.add_argument("--targets", type=Path, default=HERE / "targets.json")
    kp = sub.add_parser("check", help="a summary against the targets; exits 1 if any miss")
    kp.add_argument("summary", type=Path)
    kp.add_argument("--targets", type=Path, default=HERE / "targets.json")
    a = ap.parse_args(argv)

    if a.cmd == "summary":
        dump = a.dump or default_dump(a.runs[0])
        classify = Classifier(json.loads(dump.read_text()))
        s = summarise([read_run(r, classify, a) for r in a.runs], a)
        s["dump"] = str(dump)
        print(format_summary(s))
        if a.json:
            a.json.write_text(json.dumps(s, indent=1) + "\n")
        if a.check:
            targets = load_targets(a.targets)
            print()
            print(format_check(check(s, targets), targets))
        return 0
    if a.cmd == "census":
        classify = Classifier(json.loads(a.dump.read_text()))
        s = summarise_census([read_census(r, classify, a) for r in a.runs], a)
        s["dump"] = str(a.dump)
        print(format_census(s))
        if a.json:
            a.json.write_text(json.dumps(s, indent=1) + "\n")
        if a.check:
            targets = load_targets(a.targets)
            rows = check_census(s, targets, a.source)
            print()
            print(f"Sizes of {a.source} deposits:")
            print(format_check(rows, targets))
            return 1 if any(not r[3] for r in rows) else 0
        return 0
    targets = load_targets(a.targets)
    if a.cmd == "compare":
        before, after = (json.loads(p.read_text()) for p in (a.before, a.after))
        print(format_compare(before, after))
        print()
        print(format_check(check(after, targets), targets))
        return 0
    rows = check(json.loads(a.summary.read_text()), targets)
    print(format_check(rows, targets))
    return 1 if any(not r[3] for r in rows) else 0


if __name__ == "__main__":
    sys.exit(main())
