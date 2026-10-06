#!/usr/bin/env python3
"""Writes the pack's trader entities from the game's own.

    VINTAGE_STORY=$HOME/Games/vintagestory python3 mods-src/seraphhorizons/Trading/tools/make_entities.py

Reads the game's assets/survival/entities/humanoid/trader-{male,female}.json and writes
assets/seraphhorizons/entities/humanoid/trader-{male,female}.json: the same humanoid (shape,
skins, outfits, voice, AI, revive on death) with the pack's eleven trader types in place of
vanilla's nine, the pack's entity class, and every asset path the game resolves in the entity's
own domain given the game's domain. Re-run it after a game update; the output is committed.
Trading/Core/TraderTypes.cs holds the same eleven codes.
"""

import json
import os
import re
import sys
from pathlib import Path

TYPES = [
    "smith", "mechanic", "prospector", "farmer", "cook", "tailor",
    "carpenter", "mason", "animaldealer", "generalstore", "curiodealer",
]
# Vanilla dresses its luxuries trader in its own finer outfit set; the curio dealer wears it.
FINE_OUTFITS = {"curiodealer": "luxuries"}
ENTITY_CLASS = "SeraphHorizons.Trader"

HERE = Path(__file__).resolve().parent
OUT = HERE.parent.parent / "assets" / "seraphhorizons" / "entities" / "humanoid"


def json5(text: str):
    """The game's lenient JSON (comments, unquoted keys, trailing commas) as plain JSON."""
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
            i = text.index("\n", i)
        elif text.startswith("/*", i):
            i = text.index("*/", i) + 2
        elif re.match(r"[A-Za-z_]", c):
            m = re.match(r"[A-Za-z_][A-Za-z0-9_]*", text[i:])
            word = m.group(0)
            out.append(word if word in ("true", "false", "null") else json.dumps(word))
            i += len(word)
        else:
            out.append(c)
            i += 1
    plain = re.sub(r",(\s*[}\]])", r"\1", "".join(out))
    return json.loads(plain)


def convert(entity: dict, gender: str) -> dict:
    entity["class"] = ENTITY_CLASS
    groups = entity["variantgroups"]
    assert [g["code"] for g in groups] == ["gender", "type", "climate"], groups
    groups[1]["states"] = TYPES

    client = entity["client"]
    client["shape"]["base"] = "game:" + client["shape"]["base"]
    client["texture"]["base"] = "game:" + client["texture"]["base"]

    attributes = entity["attributes"]
    attributes["outfitConfigFileName"] = "game:" + attributes["outfitConfigFileName"]
    # The pack's types stock from their own lists (Trading/Game/TradeLists.cs), not these.
    attributes.pop("tradePropsFile", None)
    attributes.pop("tradeProps", None)
    outfits = attributes["partialRandomOutfitsByType"]
    renamed = {}
    for key, value in outfits.items():
        _, g, kind, climate = key.split("-")
        assert g == gender, key
        if kind == "*":
            renamed[key] = value
        else:
            for ours, theirs in FINE_OUTFITS.items():
                if theirs == kind:
                    renamed[f"trader-{gender}-{ours}-{climate}"] = value
    # The specific keys first: the game takes the first that matches.
    attributes["partialRandomOutfitsByType"] = dict(
        sorted(renamed.items(), key=lambda kv: "*" in kv[0]))

    for side in ("client", "server"):
        for behavior in entity[side]["behaviors"]:
            if behavior["code"] == "conversable":
                # BetterRuins gives two types its quest dialogues by a patch of its own
                # (patches/trading-betterruins-dialogue.json), which needs "*" to stay a key here.
                behavior["dialogueByType"] = {"*": "game:config/dialogue/trader"}
    return entity


def main() -> int:
    game = os.environ.get("VINTAGE_STORY")
    if not game:
        print("set VINTAGE_STORY to the game's folder", file=sys.stderr)
        return 1
    src = Path(game) / "assets" / "survival" / "entities" / "humanoid"
    OUT.mkdir(parents=True, exist_ok=True)
    for gender in ("male", "female"):
        entity = json5((src / f"trader-{gender}.json").read_text(encoding="utf-8"))
        entity = convert(entity, gender)
        path = OUT / f"trader-{gender}.json"
        path.write_text(json.dumps(entity, indent="\t", ensure_ascii=False) + "\n", encoding="utf-8")
        print(path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
