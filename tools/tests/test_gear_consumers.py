"""seraphhorizons' GearConsumers patches cover every crafting use of a rusty gear in the pack.

The rusty gear is salvage and money only (#473, epic #484): every recipe that takes
`game:gear-rusty`, or one of ppex's hand-forged gears (`ppex:gear-*`, `ppex:largegear-*`), is
patched by `mods-src/seraphhorizons/assets/seraphhorizons/patches/gearconsumers-<modid>.json` to
take the pack's stainless gear, or switched off, unless it is one of the few uses listed in EXEMPT
below, each with its reason. These tests read every locked mod's zip in build/mods (and the game's
own assets when VINTAGE_STORY is set), so a mod update that adds a use, or moves a patched recipe,
fails here. The Atlas scenario (tests/PackTests/GearConsumersScenarios.cs) checks the same from
the running game's recipe export.

Needs `python3 tools/packtool.py fetch`: without the mod zips the mod half skips, and without
VINTAGE_STORY the game half skips. Run with `python3 -m unittest discover -s tools/tests`.
"""

from __future__ import annotations

import fnmatch
import json
import os
import re
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LOCK = ROOT / "pack" / "lock.json"
MODS_DIR = ROOT / "build" / "mods"
PATCHES = ROOT / "mods-src" / "seraphhorizons" / "assets" / "seraphhorizons" / "patches"

STAINLESS_GEAR = "seraphhorizons:gear-stainless"
STAINLESS_LARGE_GEAR = "seraphhorizons:largegear-stainless"

# What a use is a use of: the rusty gear, and ppex's anvil gears and large gears.
SMALL_GEARS = ("game:gear-rusty", "ppex:gear-iron", "ppex:gear-steel")
LARGE_GEARS = ("ppex:largegear-iron", "ppex:largegear-steel")

# Uses left alone, as (asset, JSON pointer of the ingredient): (why).
EXEMPT = {
    ("game:recipes/grid/clothes/neck.json", "/21/ingredients/S"):
        "the rusty gear amulet: the gear on a string, uncrafted back into the gear (entry /23)",
    ("game:recipes/barrel/dye/gray.json", "/0/ingredients/1"):
        "rust as pigment: the gear is consumed as salvage, nothing is built from it",
    ("game:recipes/barrel/dye/black.json", "/0/ingredients/1"):
        "rust as pigment: the gear is consumed as salvage, nothing is built from it",
    ("betterloot:recipes/grid/rustygearpart.json", "/ingredients/G"):
        "change: a gear broken into four gear parts, which craft back into the gear (money)",
    ("cartwrightscaravan:recipes/grid/signs.json", "/7/ingredients/M"):
        "a shop sign showing a rusty gear: decoration",
}


# ------------------------------------------------------------------ lenient JSON (the game's)


class _Json5:
    """The asset JSON the game reads: comments, unquoted keys, single quotes, trailing commas."""

    _ws = re.compile(r"(?:\s+|//[^\n]*|/\*.*?\*/)+", re.S)
    _ident = re.compile(r"[A-Za-z_$][A-Za-z0-9_$\-]*")
    _number = re.compile(r"[+-]?(?:0[xX][0-9a-fA-F]+|(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?|Infinity|NaN)")

    def __init__(self, text: str):
        self.s, self.i = text.lstrip("﻿"), 0

    def parse(self):
        value = self.value()
        self.skip()
        if self.i != len(self.s):
            raise ValueError(f"trailing text at {self.i}")
        return value

    def skip(self):
        m = self._ws.match(self.s, self.i)
        if m:
            self.i = m.end()

    def value(self):
        self.skip()
        c = self.s[self.i]
        if c == "{":
            return self.obj()
        if c == "[":
            return self.arr()
        if c in "\"'":
            return self.string()
        for word, val in (("true", True), ("false", False), ("null", None)):
            if self.s.startswith(word, self.i):
                self.i += len(word)
                return val
        m = self._number.match(self.s, self.i)
        if not m:
            raise ValueError(f"unexpected {c!r} at {self.i}")
        self.i = m.end()
        t = m.group(0)
        if t.lstrip("+-").lower().startswith("0x"):
            return int(t, 16)
        return float(t) if any(ch in t for ch in ".eEIN") else int(t)

    def string(self) -> str:
        q, self.i, out = self.s[self.i], self.i + 1, []
        while self.s[self.i] != q:
            c = self.s[self.i]
            if c == "\\":
                self.i += 1
                e = self.s[self.i]
                if e == "u":
                    out.append(chr(int(self.s[self.i + 1:self.i + 5], 16)))
                    self.i += 4
                else:
                    out.append({"n": "\n", "t": "\t", "r": "\r", "b": "\b", "f": "\f"}.get(e, e))
            else:
                out.append(c)
            self.i += 1
        self.i += 1
        return "".join(out)

    def obj(self) -> dict:
        self.i += 1
        out: dict = {}
        while True:
            self.skip()
            if self.s[self.i] == "}":
                self.i += 1
                return out
            if self.s[self.i] in "\"'":
                key = self.string()
            else:
                m = self._ident.match(self.s, self.i)
                if not m:
                    raise ValueError(f"bad key at {self.i}")
                key, self.i = m.group(0), m.end()
            self.skip()
            if self.s[self.i] != ":":
                raise ValueError(f"expected ':' at {self.i}")
            self.i += 1
            out[key] = self.value()
            self.skip()
            if self.s[self.i] == ",":
                self.i += 1

    def arr(self) -> list:
        self.i += 1
        out: list = []
        while True:
            self.skip()
            if self.s[self.i] == "]":
                self.i += 1
                return out
            out.append(self.value())
            self.skip()
            if self.s[self.i] == ",":
                self.i += 1


def loads(text: str):
    return _Json5(text).parse()


# ------------------------------------------------------------------ sources


def lock_mods() -> list[dict]:
    return json.loads(LOCK.read_text())["mods"]


def mod_assets(zip_path: Path) -> dict[str, str]:
    """A mod zip's JSON assets as {"domain:path": text}."""
    out = {}
    with zipfile.ZipFile(zip_path) as z:
        for name in z.namelist():
            parts = name.split("/")
            if len(parts) >= 3 and parts[0] == "assets" and name.endswith(".json"):
                out[f"{parts[1]}:{'/'.join(parts[2:])}"] = z.read(name).decode("utf-8-sig", "replace")
    return out


def game_assets(install: Path) -> dict[str, str]:
    """The game's own recipes and itemtypes as {"game:path": text}."""
    base = install / "assets" / "survival"
    return {
        f"game:{p.relative_to(base).as_posix()}": p.read_text(encoding="utf-8-sig")
        for sub in ("recipes", "itemtypes") for p in sorted((base / sub).rglob("*.json"))
    }


def _code(code: str, domain: str) -> str:
    return code if ":" in code else f"{domain}:{code}"


def _matches(pattern: str, codes) -> bool:
    return any(fnmatch.fnmatchcase(c, pattern) for c in codes)


def gear_kind(code: str) -> str | None:
    """'small' or 'large' when an ingredient code takes a rusty or ppex gear, else None."""
    if _matches(code, SMALL_GEARS):
        return "small"
    if _matches(code, LARGE_GEARS):
        return "large"
    return None


def ingredient_kind(node: dict, code: str) -> str | None:
    """gear_kind for an ingredient, narrowed by its allowedVariants and skipVariants, whose values
    the game puts in the code's one wildcard (metalbit-jewelryscrap.json takes "*" with
    allowedVariants naming the items)."""
    allowed, skipped = node.get("allowedVariants"), node.get("skipVariants") or []
    if code.count("*") != 1 or not (allowed or skipped):
        return gear_kind(code)
    gears = [g for g in SMALL_GEARS + LARGE_GEARS if fnmatch.fnmatchcase(g, code)]
    if allowed:
        gears = [g for g in gears if any(g == code.replace("*", v) for v in allowed)]
    gears = [g for g in gears if not any(g == code.replace("*", v) for v in skipped)]
    return gear_kind(gears[0]) if gears else None


def find_uses(asset: str, doc) -> list[tuple[str, str, str]]:
    """Every ingredient of a recipe file that takes a gear: (pointer to the ingredient, its code,
    'small' or 'large'). An ingredient is any object with a code under "ingredient(s)", not inside
    an output or a returned stack."""
    domain = asset.split(":", 1)[0]
    found = []

    def walk(node, pointer, under_ingredients):
        if isinstance(node, dict):
            if under_ingredients and isinstance(node.get("code"), str):
                code = _code(node["code"], domain)
                kind = ingredient_kind(node, code)
                if kind:
                    found.append((pointer, code, kind))
            for key, value in node.items():
                if key in ("output", "outputs", "returnedStack"):
                    continue
                walk(value, f"{pointer}/{key}", under_ingredients or key in ("ingredient", "ingredients"))
        elif isinstance(node, list):
            for i, value in enumerate(node):
                walk(value, f"{pointer}/{i}", under_ingredients)

    walk(doc, "", False)
    return found


def recipe_root(doc, pointer: str) -> str:
    """The pointer of the recipe holding `pointer`: "/<n>" in a file that is a list, "" in one
    that is a single recipe."""
    return "/" + pointer.split("/")[1] if isinstance(doc, list) else ""


def outputs_ppex_gear(recipe, domain: str) -> bool:
    """Whether a recipe makes one of ppex's gears (its smithing recipes: "ppex:gear-{metal}")."""
    out = recipe.get("output") if isinstance(recipe, dict) else None
    code = out.get("code") if isinstance(out, dict) else None
    if not isinstance(code, str):
        return False
    return _matches(re.sub(r"\{[^}]*\}", "*", _code(code, domain)), SMALL_GEARS[1:] + LARGE_GEARS)


def resolve(doc, pointer: str):
    """The node at a JSON pointer; LookupError naming the pointer when there is none."""
    node = doc
    for token in pointer.split("/")[1:]:
        token = token.replace("~1", "/").replace("~0", "~")
        try:
            node = node[int(token)] if isinstance(node, list) else node[token]
        except (LookupError, ValueError, TypeError):
            raise LookupError(f"{pointer}: no {token!r} there") from None
    return node


def load_patches() -> dict[str, list[dict]]:
    """Every gearconsumers-<modid>.json, by modid."""
    return {p.stem.removeprefix("gearconsumers-"): loads(p.read_text())
            for p in sorted(PATCHES.glob("gearconsumers-*.json"))}


# ------------------------------------------------------------------ tests


class Coverage:
    """Shared checks over one source: a mod zip or the game's assets."""

    def check_source(self, modid: str, assets: dict[str, str], patches: list[dict]):
        recipes = {a: t for a, t in assets.items() if ":recipes/" in a and re.search(r"gear", t)}
        for asset, text in recipes.items():
            doc = loads(text)
            for pointer, code, kind in find_uses(asset, doc):
                with self.subTest(mod=modid, asset=asset, ingredient=pointer, code=code):
                    if (asset, pointer) in EXEMPT:
                        continue
                    root = recipe_root(doc, pointer)
                    want = STAINLESS_GEAR if kind == "small" else STAINLESS_LARGE_GEAR
                    replaced = any(p["file"] == asset and p["op"] == "replace" and p["path"] == f"{pointer}/code"
                                   and p["value"] == want for p in patches)
                    disabled = any(p["file"] == asset and p["op"] in ("add", "replace")
                                   and p["path"] == f"{root}/enabled" and p["value"] is False for p in patches)
                    self.assertTrue(replaced or disabled,
                                    f"{asset} {pointer} takes {code}: no patch in gearconsumers-{modid}.json "
                                    f"replaces it with {want} or disables recipe {root or '(the file)'}")
            # ppex's anvil gears: every recipe making one is switched off.
            for i, recipe in enumerate(doc if isinstance(doc, list) else [doc]):
                if not outputs_ppex_gear(recipe, asset.split(":", 1)[0]):
                    continue
                root = f"/{i}" if isinstance(doc, list) else ""
                with self.subTest(mod=modid, asset=asset, recipe=root):
                    self.assertTrue(any(p["file"] == asset and p["path"] == f"{root}/enabled" and p["value"] is False
                                        for p in patches), f"{asset} {root} makes a ppex gear and is not disabled")

    def check_patches_hit(self, modid: str, assets: dict[str, str], patches: list[dict]):
        """Each patch entry still points at what it was written for."""
        for p in patches:
            with self.subTest(mod=modid, file=p["file"], path=p["path"]):
                self.assertIn(p["file"], assets, f"{p['file']} is not in {modid}")
                doc = loads(assets[p["file"]])
                parent, _, last = p["path"].rpartition("/")
                try:
                    resolve(doc, parent)
                except LookupError as e:
                    self.fail(f"{p['file']}: {e}: the recipe moved")
                if p["path"].endswith("/code"):
                    code = _code(resolve(doc, p["path"]), p["file"].split(":", 1)[0])
                    kind = gear_kind(code)
                    self.assertIsNotNone(kind, f"{p['path']} is {code}, not a gear: the recipe moved")
                    self.assertEqual(STAINLESS_GEAR if kind == "small" else STAINLESS_LARGE_GEAR, p["value"])
                elif last == "enabled":
                    recipe = resolve(doc, parent) if parent else doc
                    uses = find_uses(p["file"], recipe)
                    self.assertTrue(uses or outputs_ppex_gear(recipe, p["file"].split(":", 1)[0]),
                                    f"{p['file']} {parent or '(the file)'} neither takes nor makes a gear: the recipe moved")
                elif ":itemtypes/" in p["file"]:
                    self.assertIn(loads(assets[p["file"]]).get("code"), ("gear", "largegear"))
                else:
                    self.fail(f"unexpected patch {p}")


class ModsTakeTheSteelGear(Coverage, unittest.TestCase):
    """Every locked mod's recipes, from its zip in build/mods."""

    @classmethod
    def setUpClass(cls):
        cls.mods = lock_mods()
        present = [m for m in cls.mods if (MODS_DIR / m["fileName"]).exists()]
        if not present:
            raise unittest.SkipTest("no mods fetched: run `python3 tools/packtool.py fetch`")
        missing = [m["fileName"] for m in cls.mods if m not in present]
        if missing:
            raise AssertionError(f"build/mods is missing {missing}: run `python3 tools/packtool.py fetch`")
        cls.patches = load_patches()
        cls.assets = {m["id"]: mod_assets(MODS_DIR / m["fileName"]) for m in cls.mods}

    def test_every_use_is_patched_or_exempt(self):
        for mod in self.mods:
            self.check_source(mod["id"], self.assets[mod["id"]], self.patches.get(mod["id"], []))

    def test_every_patch_still_hits_its_recipe(self):
        for modid, patches in self.patches.items():
            if modid != "game":
                self.assertIn(modid, self.assets, f"gearconsumers-{modid}.json is for a mod not in the pack")
                self.check_patches_hit(modid, self.assets[modid], patches)

    def test_each_file_depends_on_its_mod(self):
        for modid, patches in self.patches.items():
            for p in patches:
                with self.subTest(file=f"gearconsumers-{modid}.json", path=p["path"]):
                    want = [] if modid == "game" else [{"modid": modid}]
                    self.assertEqual(want, p.get("dependsOn", []))

    def test_ppex_gears_are_hidden(self):
        ppex = self.patches["ppex"]
        for item in ("gear", "largegear"):
            asset = f"ppex:itemtypes/{item}.json"
            with self.subTest(item=item):
                self.assertIn({"file": asset, "op": "remove", "path": "/creativeinventory"},
                              [{k: p[k] for k in ("file", "op", "path")} for p in ppex])
                self.assertTrue(any(p["file"] == asset and p["op"] in ("add", "addmerge") and p["path"] == "/attributes"
                                    and p["value"].get("handbook", {}).get("exclude") is True for p in ppex))

    def test_exemptions_are_still_there(self):
        for (asset, pointer), why in EXEMPT.items():
            if asset.startswith("game:"):
                continue
            domain = asset.split(":", 1)[0]
            owners = [m for m, a in self.assets.items() if asset in a]
            with self.subTest(asset=asset):
                self.assertTrue(owners, f"{asset} is gone: drop its exemption")
                doc = loads(self.assets[owners[0]][asset])
                self.assertIn((pointer, _code(resolve(doc, pointer)["code"], domain)),
                              [(u[0], u[1]) for u in find_uses(asset, doc)], why)


class GameTakesTheSteelGear(Coverage, unittest.TestCase):
    """The game's own recipes, from the install VINTAGE_STORY points to."""

    @classmethod
    def setUpClass(cls):
        install = os.environ.get("VINTAGE_STORY")
        if not install or not (Path(install) / "assets" / "survival").is_dir():
            raise unittest.SkipTest("VINTAGE_STORY is not set to a game or server install")
        cls.assets = game_assets(Path(install))
        cls.patches = load_patches().get("game", [])

    def test_every_use_is_patched_or_exempt(self):
        self.check_source("game", self.assets, self.patches)

    def test_every_patch_still_hits_its_recipe(self):
        self.check_patches_hit("game", self.assets, self.patches)

    def test_exemptions_are_still_there(self):
        for (asset, pointer), why in EXEMPT.items():
            if not asset.startswith("game:"):
                continue
            with self.subTest(asset=asset):
                doc = loads(self.assets[asset])
                self.assertIn(pointer, [u[0] for u in find_uses(asset, doc)], why)


class Parser(unittest.TestCase):
    def test_reads_the_games_json(self):
        doc = loads("""// comment
            [ { ingredientPattern: "GH_", /* block */ ingredients: { "G": { type: 'item', code: "gear-rusty", quantity: 2, }, },
                output: { code: "x" } }, ]""")
        self.assertEqual([{"ingredientPattern": "GH_", "ingredients": {"G": {"type": "item", "code": "gear-rusty", "quantity": 2}},
                           "output": {"code": "x"}}], doc)

    def test_finds_uses_under_ingredients_only(self):
        doc = loads("""[{ "ingredients": { "G": { "code": "gear-rusty", "returnedStack": { "code": "gear-rusty" } },
                                           "P": { "code": "ppex:gear-*" }, "L": { "code": "ppex:largegear-steel" },
                                           "T": { "code": "gear-temporal" } },
                          "output": { "code": "gear-rusty" } },
                        { "ingredient": { "code": "game:gear-*" }, "output": { "code": "x" } }]""")
        self.assertEqual([("/0/ingredients/G", "game:gear-rusty", "small"), ("/0/ingredients/P", "ppex:gear-*", "small"),
                          ("/0/ingredients/L", "ppex:largegear-steel", "large"), ("/1/ingredient", "game:gear-*", "small")],
                         find_uses("game:recipes/grid/x.json", doc))

    def test_a_mods_bare_code_is_in_its_own_domain(self):
        self.assertEqual([], find_uses("betterruins:recipes/grid/x.json", {"ingredients": {"G": {"code": "gear-rusty"}}}))


if __name__ == "__main__":
    unittest.main()
