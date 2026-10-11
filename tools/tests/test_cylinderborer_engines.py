"""seraphhorizons' CylinderBorer patch gives Pipes and Power Expanded's engines a bored cylinder.

`mods-src/seraphhorizons/assets/seraphhorizons/patches/cylinderborer-ppex.json` (#639) replaces the bottom
row of ppex's Watt and Cornish engine recipes, two plates and the pipe(s), with one bored cylinder from the
cylinder borer: `seraphhorizons:cylinder-iron` for the Watt engine, `seraphhorizons:cylinder-steel` for the
Cornish. It patches by index, and `gearconsumers-ppex.json` patches the same recipes' gears, so this reads
ppex's `recipes/grid/machines.json` from its zip in build/mods and applies both patches, in either order, as
the game's patch loader does: each path must land where it was written for (the engine it means, the
pattern and the pipe it replaces), the two patches must not touch the same keys, and the result must be the
recipe the owner approved, every other ingredient as ppex ships it.

Needs `python3 tools/packtool.py fetch`; skips without ppex's zip. Run with
`python3 -m unittest discover -s tools/tests -p test_cylinderborer_engines.py`.
"""

from __future__ import annotations

import copy
import unittest

from test_gear_consumers import MODS_DIR, PATCHES, lock_mods, loads, mod_assets, resolve

PATCH = PATCHES / "cylinderborer-ppex.json"
GEARS = PATCHES / "gearconsumers-ppex.json"
ASSET = "ppex:recipes/grid/machines.json"
ITEMTYPE = PATCHES.parent / "itemtypes" / "cylinder.json"

# index: (output, ppex's pattern, the patched pattern, the pipe ingredient ppex has, the cylinder)
ENGINES = {
    4: ("ppex:enginecornish-north", "GHR,PNP,PIP", "GHR,PNP,_C_", ("ppex:pipe-straight-*-steel", 2), "seraphhorizons:cylinder-steel"),
    5: ("ppex:enginewatt-north", "GH_,PRP,PIP", "GH_,PRP,_C_", ("ppex:pipe-straight-*", 1), "seraphhorizons:cylinder-iron"),
    9: ("ppex:enginecornish-north", "GHR,PNP,PIP", "GHR,PNP,_C_", ("ppex:pipe-straight-*-steel", 2), "seraphhorizons:cylinder-steel"),
    10: ("ppex:enginewatt-north", "GH_,PRP,PIP", "GH_,PRP,_C_", ("ppex:pipe-straight-*", 1), "seraphhorizons:cylinder-iron"),
}


def _tokens(path: str) -> list[str]:
    return [t.replace("~1", "/").replace("~0", "~") for t in path.split("/")[1:]]


def apply(doc, ops):
    """The game's JSON patch ops this pack uses on recipe files: add, replace, remove (objects and arrays)."""
    doc = copy.deepcopy(doc)
    for op in ops:
        *parents, last = _tokens(op["path"])
        node = doc
        for t in parents:
            node = node[int(t)] if isinstance(node, list) else node[t]
        if op["op"] == "remove":
            if isinstance(node, list):
                del node[int(last)]
            else:
                del node[last]
        elif op["op"] == "replace":
            if isinstance(node, list):
                node[int(last)] = op["value"]
            else:
                if last not in node:
                    raise LookupError(f"{op['path']}: nothing to replace")
                node[last] = op["value"]
        elif op["op"] == "add":
            if isinstance(node, list):
                node.insert(len(node) if last == "-" else int(last), op["value"])
            else:
                node[last] = op["value"]
        else:
            raise ValueError(f"unexpected op {op['op']}")
    return doc


def cells(pattern: str) -> list[str]:
    return [c for c in pattern.replace(",", "") if c != "_"]


class CylinderBorerPatchFitsPpex(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        ppex = [m for m in lock_mods() if m["id"] == "ppex"]
        if not ppex or not (MODS_DIR / ppex[0]["fileName"]).exists():
            raise unittest.SkipTest("ppex is not fetched: run `python3 tools/packtool.py fetch`")
        cls.doc = loads(mod_assets(MODS_DIR / ppex[0]["fileName"])[ASSET])
        cls.patch = loads(PATCH.read_text())
        cls.gears = [op for op in loads(GEARS.read_text()) if op["file"] == ASSET]

    def test_every_op_depends_on_ppex_and_targets_its_machines(self):
        for op in self.patch:
            with self.subTest(op=op):
                self.assertEqual([{"modid": "ppex"}], op.get("dependsOn"))
                self.assertEqual(ASSET, op["file"])
        self.assertEqual(set(ENGINES), {int(_tokens(op["path"])[0]) for op in self.patch})

    def test_ppex_ships_the_engines_the_patch_was_written_for(self):
        for i, (output, before, _, (pipe, pipes), _) in ENGINES.items():
            with self.subTest(recipe=i):
                recipe = self.doc[i]
                self.assertEqual(output, resolve(self.doc, f"/{i}/output/code"))
                self.assertEqual(before, recipe["ingredientPattern"])
                self.assertEqual(pipe, recipe["ingredients"]["I"]["code"])
                self.assertEqual(pipes, recipe["ingredients"]["I"]["quantity"])
                self.assertNotIn("C", recipe["ingredients"])

    def test_the_gear_patch_touches_other_keys_of_the_same_recipes(self):
        mine = {op["path"] for op in self.patch}
        theirs = {op["path"] for op in self.gears}
        self.assertFalse(mine & theirs, "both patches write the same path")
        for path in theirs:
            self.assertFalse(any(path.startswith(p + "/") or p.startswith(path + "/") for p in mine), path)

    def test_the_engines_take_a_bored_cylinder_for_their_plates_and_pipes(self):
        for order in ("gears first", "cylinder first"):
            ops = self.gears + self.patch if order == "gears first" else self.patch + self.gears
            after = apply(self.doc, ops)
            for i, (output, before, pattern, _, cylinder) in ENGINES.items():
                with self.subTest(order=order, recipe=i):
                    was, now = self.doc[i], after[i]
                    self.assertEqual(pattern, now["ingredientPattern"])
                    self.assertEqual({"type": "item", "code": cylinder, "quantity": 1}, now["ingredients"]["C"])
                    self.assertNotIn("I", now["ingredients"])
                    # every letter of the pattern is an ingredient and every ingredient is used
                    self.assertEqual(set(cells(pattern)), set(now["ingredients"]))
                    # the bottom row's two plates and the pipe(s) went; two plates stay in the middle row
                    self.assertEqual(4, cells(before).count("P"))
                    self.assertEqual(2, cells(pattern).count("P"))
                    # every other ingredient as ppex ships it, but for the gear patch's gear
                    for key, ing in now["ingredients"].items():
                        if key in ("C", "G"):
                            continue
                        self.assertEqual(was["ingredients"][key], ing, key)
                    self.assertEqual(was["ingredients"]["G"]["quantity"], now["ingredients"]["G"]["quantity"])
                    want = "seraphhorizons:gear-stainless" if i in (4, 5) else was["ingredients"]["G"]["code"]
                    self.assertEqual(want, now["ingredients"]["G"]["code"])
                    self.assertEqual(i in (9, 10), now.get("enabled", True) is False)
                    self.assertEqual(output, now["output"]["code"])
                    self.assertEqual({k: v for k, v in was.items() if k not in ("ingredientPattern", "ingredients", "enabled")},
                                     {k: v for k, v in now.items() if k not in ("ingredientPattern", "ingredients", "enabled")})
            # nothing else in the file changes but what the gear patch does
            self.assertEqual(apply(self.doc, self.gears)[:4], after[:4])
            self.assertEqual(apply(self.doc, self.gears)[11:], after[11:])

    def test_the_watt_engine_keeps_iron_or_steel_and_the_cornish_steel_only(self):
        after = apply(self.doc, self.gears + self.patch)
        self.assertEqual(["iron", "steel"], after[5]["ingredients"]["P"]["allowedVariants"])
        self.assertEqual("game:metalplate-steel", after[4]["ingredients"]["P"]["code"])
        self.assertEqual("game:rod-steel", after[4]["ingredients"]["R"]["code"])
        self.assertEqual("game:metalnailsandstrips-steel", after[4]["ingredients"]["N"]["code"])

    def test_the_cylinders_are_the_mods_items(self):
        itemtype = loads(ITEMTYPE.read_text())
        self.assertEqual("cylinder", itemtype["code"])
        self.assertEqual(["iron", "steel"], itemtype["variantgroups"][0]["states"])
        codes = {f"seraphhorizons:cylinder-{m}" for m in itemtype["variantgroups"][0]["states"]}
        self.assertEqual(codes, {e[4] for e in ENGINES.values()})


if __name__ == "__main__":
    unittest.main()
