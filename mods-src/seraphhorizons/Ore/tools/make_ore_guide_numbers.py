#!/usr/bin/env python3
"""Write the figures in ore processing's handbook texts (lang/en.json) from where they come from.

The "Ore processing" guide (`oreprocessing-guide-text`) and the ore forms' handbook sections
(`oreproduct-*-handbook-text`, ...) quote the pack's figures: the grade shares, the roast losses, the
concentrators' catch, the parting shares, the bullion's second metal. Those live in
`assets/seraphhorizons/config/ore-processing.json` and in a few C# constants, and en.json is plain
JSON (no comments, so no cog). So the texts are kept in

    Ore/tools/ore-guide-numbers.template.json
        each such lang key's text with `{{name}}` where a figure goes (figures() below names them),

and this script fills them and writes them into en.json, editing only those keys' values and leaving
every other byte of the file as it was. Edit a text in the template, never in en.json, then run

    python3 mods-src/seraphhorizons/Ore/tools/make_ore_guide_numbers.py           # rewrite en.json
    python3 mods-src/seraphhorizons/Ore/tools/make_ore_guide_numbers.py --check   # exit 1 on drift

`--check` lists the keys whose text in en.json is not the template's filled in;
tools/tests/test_ore_guide_numbers.py runs it, so CI fails when a figure or a text drifts.

Sources: ore-processing.json (comments stripped; numbers read exactly, as decimals), and the C#
constants in CSHARP, each read by a strict regex that must match once. A figure the wording can only
state for one value (that a form "smelts whole", that the parting machines share one figure) is a
REQUIRE: the script stops and says which text to reword, rather than writing something untrue.

Stdlib only; two runs write the same bytes.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from decimal import ROUND_FLOOR, ROUND_HALF_UP, Decimal
from fractions import Fraction
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
MOD = ROOT / "mods-src" / "seraphhorizons"
ASSETS = MOD / "assets" / "seraphhorizons"
CONFIG = ASSETS / "config" / "ore-processing.json"
LANG = ASSETS / "lang" / "en.json"
TEMPLATE = HERE / "ore-guide-numbers.template.json"
SCRIPT = "mods-src/seraphhorizons/Ore/tools/make_ore_guide_numbers.py"

# C# constants the texts quote: name -> (file under the mod, regex with one group per value).
CSHARP = {
    "roastPoint": ("Ore/Core/OreRoasting.cs", r"public const int MeltingPoint = (\d+);"),
    "rawUnits": ("Ore/Core/OreProducts.cs", r"private static readonly int\[\] DefaultUnits = \[(\d+), (\d+), (\d+), (\d+)\];"),
    "lithargeRate": ("Ore/Core/OreProducts.cs", r"public static readonly SmeltRate LithargeRate = new\((\d+), (\d+)\);"),
    "bitUnits": ("Ore/Core/Cupellation.cs", r"public const double UnitsPerItem = (\d+);"),
    "barUnits": ("Ore/Core/Bullion.cs", r"public const double UnitsPerItem = (\d+);"),
}

GRADES = ("poor", "medium", "rich", "bountiful")
CONCENTRATORS = ("sluice", "jig", "table", "vanner")
PARTING_MACHINES = ("cupellationfurnace", "liquationfurnace", "acidparting")
BULLION_ORES = ("galena", "galena_nativesilver", "tetrahedrite", "freibergite", "teallite", "franckeite",
                "quartz_nativegold")

# Shares the texts write as words.
FRACTION_WORDS = {
    Fraction(1, 10): "a tenth", Fraction(1, 5): "a fifth", Fraction(1, 4): "a quarter",
    Fraction(1, 3): "a third", Fraction(1, 2): "half", Fraction(2, 3): "two thirds",
    Fraction(3, 4): "three quarters", Fraction(4, 5): "four fifths", Fraction(9, 10): "nine tenths",
}
MULTIPLE_WORDS = {1: "as much", 2: "twice as much", 3: "three times as much"}


class FigureError(Exception):
    """A source that cannot be read, or a figure the texts' wording cannot state."""


# ------------------------------------------------------------------ sources


def strip_comments(text: str) -> str:
    """The game's JSON with its // and /* */ comments blanked out (strings left alone)."""
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            j = i + 1
            while text[j] != '"':
                j += 2 if text[j] == "\\" else 1
            out.append(text[i:j + 1])
            i = j + 1
        elif text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
        elif text.startswith("/*", i):
            i = text.index("*/", i) + 2
        else:
            out.append(c)
            i += 1
    return "".join(out)


def load_config(path: Path = CONFIG) -> dict:
    """ore-processing.json, its numbers as exact decimals."""
    return json.loads(strip_comments(path.read_text(encoding="utf-8")), parse_float=Decimal, parse_int=Decimal)


def load_csharp(mod: Path = MOD) -> dict[str, tuple[Decimal, ...]]:
    """CSHARP's constants: name -> the values its regex captured. Each regex must match exactly once."""
    out = {}
    for name, (rel, pattern) in CSHARP.items():
        text = (mod / rel).read_text(encoding="utf-8")
        hits = re.findall(pattern, text)
        if len(hits) != 1:
            raise FigureError(f"{rel}: expected one match of {pattern!r} for {name}, found {len(hits)}; "
                              f"update CSHARP in {SCRIPT}")
        hit = hits[0] if isinstance(hits[0], tuple) else (hits[0],)
        out[name] = tuple(Decimal(v) for v in hit)
    return out


def load_template(path: Path = TEMPLATE) -> dict[str, str]:
    """The template: lang key -> text with {{name}} placeholders (// comment lines allowed)."""
    return json.loads(strip_comments(path.read_text(encoding="utf-8")))


# ------------------------------------------------------------------ formatting


def num(x: Decimal) -> str:
    """A number as the texts write it: whole without a point, else as few decimals as it has."""
    x = Decimal(x)
    if x == x.to_integral_value():
        return str(int(x))
    return format(x.normalize(), "f")


def pct(share: Decimal) -> str:
    """A share as a percentage to one decimal, rounded half up, without a trailing .0: 0.2125 -> 21.3."""
    return num((Decimal(share) * 100).quantize(Decimal("0.1"), rounding=ROUND_HALF_UP))


def fraction_words(share: Decimal) -> str:
    """A share the texts write in words (0.9 -> nine tenths)."""
    for f, words in FRACTION_WORDS.items():
        if abs(Decimal(share) - Decimal(f.numerator) / Decimal(f.denominator)) < Decimal("1e-9"):
            return words
    raise FigureError(f"{share} has no words in FRACTION_WORDS; add them or reword the text")


def one_list(values) -> str:
    """3, 5, 7 or 10."""
    values = [num(v) for v in values]
    return ", ".join(values[:-1]) + " or " + values[-1]


def require(condition: bool, message: str) -> None:
    if not condition:
        raise FigureError(f"{message}: reword the text in {TEMPLATE.name} (or add a figure for it) and rerun {SCRIPT}")


# ------------------------------------------------------------------ figures


def figures(cfg: dict, cs: dict[str, tuple[Decimal, ...]]) -> dict[str, str]:
    """Every placeholder's text, from the config and the C# constants."""
    hand, machines = cfg["handStations"], cfg["machines"]
    firepit, stall = hand["firepit"], machines["stallroaster"]["recovery"]
    reverb = machines["reverberatory"]["recovery"]
    f: dict[str, str] = {}

    f["rawUnits"] = one_list(cs["rawUnits"])
    for g in GRADES:
        share = cfg["smeltByGrade"][g]
        f[f"smelt.{g}"] = pct(share)
        f[f"roasted.{g}"] = pct(share * firepit)
        f[f"stallRoasted.{g}"] = pct(share * stall)

    whole = cfg["smelt"]
    require(all(whole[k] == 1 for k in ("concentrate", "roastedconcentrate", "sponge")),
            "the texts say concentrate, roasted concentrate and sponge smelt whole, but smelt is " + str(dict(whole)))
    f["smeltWhole"] = pct(whole["concentrate"])
    f["concentrateUnits"] = num(cfg["concentrateUnits"])

    f["pan"] = pct(hand["pan"])
    f["rocker"] = pct(hand["rocker"])
    for m in CONCENTRATORS:
        f[m] = pct(machines[m]["recovery"])

    f["roastPoint"] = num(cs["roastPoint"][0])
    f["firepit"] = pct(firepit)
    # The firepit carries the fraction over (UnitCarry), so 20 concentrate give the whole items of 20 x its share.
    f["firepit.of20"] = num((20 * firepit).to_integral_value(rounding=ROUND_FLOOR))
    f["stallroaster"] = pct(stall)
    f["stallroaster.sulfur"] = fraction_words(machines["stallroaster"]["sulfur"])
    f["reverberatory.sulfur"] = pct(machines["reverberatory"]["sulfur"])
    f["reverberatory.loses"] = "loses none of its metal" if reverb == 1 else f"keeps {pct(reverb)} % of its metal"
    f["reverberatory.keeps"] = "keeps all of it" if reverb == 1 else f"keeps {pct(reverb)} % of it"

    f["mercuryReturn"] = fraction_words(cfg["retort"]["mercuryReturn"])

    bar = cs["barUnits"][0]
    f["barUnits"] = num(bar)
    for ore in BULLION_ORES:
        products = cfg["ores"][ore]["byProducts"]
        require(len(products) == 1, f"the texts give {ore} one second metal, the config {len(products)}")
        f[f"byProduct.{ore}"] = num(products[0]["share"] * bar)
    lead = cfg["cupel"]["leadPerOreUnit"]
    if lead not in MULTIPLE_WORDS:
        raise FigureError(f"cupel.leadPerOreUnit {lead} has no words in MULTIPLE_WORDS")
    f["cupel.leadAdded"] = MULTIPLE_WORDS[int(lead)]

    parting = {m: machines[m]["recovery"] for m in PARTING_MACHINES}
    require(len(set(parting.values())) == 1, f"the texts give the parting machines one share, the config {parting}")
    f["partingMachines"] = pct(parting[PARTING_MACHINES[0]])
    require(hand["cupel"] == hand["liquationpan"],
            f"the texts give the cupel and the liquation pan one share, the config {hand['cupel']} and {hand['liquationpan']}")
    f["handParting"] = pct(hand["cupel"])
    f["unparted.quartz_nativegold"] = pct(cfg["ores"]["quartz_nativegold"]["unparted"])
    f["richLeadSilver"] = fraction_words(cfg["bullion"]["richLeadSilver"])

    # 21 litharge (the rate's ratio) smelt to an ingot's 100 units x its output: so many bits' worth.
    output, ratio = cs["lithargeRate"]
    f["litharge.in"] = num(ratio)
    f["litharge.out"] = num(100 * output / cs["bitUnits"][0])
    return f


PLACEHOLDER = re.compile(r"\{\{([A-Za-z0-9_.]+)\}\}")


def fill(template: dict[str, str], figs: dict[str, str]) -> dict[str, str]:
    """The template's texts with their placeholders filled; every placeholder must be a figure."""
    out, unknown = {}, set()

    def sub(m: re.Match) -> str:
        if m.group(1) not in figs:
            unknown.add(m.group(1))
            return m.group(0)
        return figs[m.group(1)]

    for key, text in template.items():
        out[key] = PLACEHOLDER.sub(sub, text)
        if "{{" in out[key] or "}}" in out[key]:
            unknown.add(f"(a malformed placeholder in {key})")
    if unknown:
        raise FigureError(f"{TEMPLATE.name}: unknown placeholders {sorted(unknown)}; figures() in {SCRIPT} names them")
    return out


# ------------------------------------------------------------------ en.json


def _value_span(lang: str, key: str) -> re.Match:
    """The line `  "key": "value"` in en.json, its value literal as group 1."""
    pattern = re.compile(r'^  ' + re.escape(json.dumps(key)) + r': ("(?:[^"\\\n]|\\.)*")', re.M)
    hits = list(pattern.finditer(lang))
    if len(hits) != 1:
        raise FigureError(f"{LANG.name}: expected one line for {key}, found {len(hits)}; add the key to en.json first")
    return hits[0]


def literal(text: str) -> str:
    """A value as en.json writes it: JSON escapes, non-ASCII as is."""
    return json.dumps(text, ensure_ascii=False)


def apply(lang: str, texts: dict[str, str]) -> tuple[str, list[str]]:
    """en.json with `texts` written into their keys' values, and the keys whose value changed."""
    stale = []
    for key, text in texts.items():
        m = _value_span(lang, key)
        new = literal(text)
        if m.group(1) != new:
            stale.append(key)
            lang = lang[:m.start(1)] + new + lang[m.end(1):]
    return lang, stale


def generate(config: Path = CONFIG, template: Path = TEMPLATE, lang: Path = LANG, mod: Path = MOD) -> tuple[str, list[str]]:
    """(en.json as it should read, the keys that differ from the file now)."""
    texts = fill(load_template(template), figures(load_config(config), load_csharp(mod)))
    return apply(lang.read_text(encoding="utf-8"), texts)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true", help="write nothing; exit 1 if en.json is stale")
    args = ap.parse_args(argv)
    try:
        new, stale = generate()
    except FigureError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    if args.check:
        if stale:
            print(f"{LANG.relative_to(ROOT)}: {len(stale)} ore processing text(s) are not their template's "
                  f"({TEMPLATE.relative_to(ROOT)}) with the current figures:", file=sys.stderr)
            for key in stale:
                print(f"  {key}", file=sys.stderr)
            print(f"Edit the text in the template, not en.json, then run: python3 {SCRIPT}", file=sys.stderr)
            return 1
        print(f"{LANG.name}: ore processing texts are current")
        return 0
    if stale:
        LANG.write_text(new, encoding="utf-8")
        print(f"wrote {len(stale)} key(s) of {LANG.relative_to(ROOT)}: {', '.join(stale)}")
    else:
        print(f"{LANG.name}: ore processing texts are current")
    return 0


if __name__ == "__main__":
    sys.exit(main())
