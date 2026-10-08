#!/usr/bin/env python3
"""Writes the pack's trader entities from the game's own.

    VINTAGE_STORY=$HOME/Games/vintagestory python3 mods-src/seraphhorizons/Trading/tools/make_entities.py

Reads the game's assets/survival/entities/humanoid/trader-{male,female}.json and writes
assets/seraphhorizons/entities/humanoid/trader-{male,female}.json: the same humanoid (shape,
skins, outfits, voice, AI, revive on death) with the pack's eleven trader types in place of
vanilla's nine, the pack's entity class, and every asset path the game resolves in the entity's
own domain given the game's domain. Re-run it after a game update; the output is committed.
Trading/Core/TraderTypes.cs holds the same eleven codes.

It also writes visitor-{male,female}.json: the travelling merchants (#456, Trading/Visitors/),
code `visitor`, class SeraphHorizons.VisitingTrader, the same humanoid with no revive, no fighting
back or fleeing (they take no damage), wandering at most 4 blocks from where they arrived.

And the dialogue all of them talk with, assets/seraphhorizons/config/dialogue/trader.json: the
game's config/dialogue/trader.json with one more option in its main menu, "How do you see me these
days?" (shown while trader standing is on: the entity variable `shstanding`), answered by the
component `seraphhorizons-standing`, whose text the mod writes per player (Trading/Window/). Only
"Got anything to trade?" opens the trade window, the pack's own (the dialogue's `opentrade`).
BetterRuins' two quest dialogues get the same option and component by
patches/trading-betterruins-dialogue.json.
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

# The pack's trader dialogue (the game's, with the standing option), and the option and its answer.
DIALOGUE = "seraphhorizons:config/dialogue/trader"
STANDING_OPTION = {
    "value": "seraphhorizons:dialogue-trader-standing",
    "jumpTo": "seraphhorizons-standing",
    "conditions": [{"variable": "entity.shstanding", "isValue": "on"}],
}
STANDING_COMPONENT = {
    "code": "seraphhorizons-standing",
    "owner": "trader",
    "type": "talk",
    "text": [{"value": "seraphhorizons:dialogue-standing-plain"}],
    "jumpTo": "main",
}

VISITOR_TYPES = ["travellingmerchant", "travellingcurio"]
VISITOR_FINE_OUTFITS = {"travellingcurio": "luxuries"}
VISITOR_CLASS = "SeraphHorizons.VisitingTrader"
# What a visitor that takes no damage has no use for.
VISITOR_DROPPED_BEHAVIORS = {"reviveondeath", "emotionstates"}
VISITOR_DROPPED_TASKS = {"meleeattack", "seekentity", "fleeentity"}

HERE = Path(__file__).resolve().parent
OUT = HERE.parent.parent / "assets" / "seraphhorizons" / "entities" / "humanoid"
DIALOGUE_OUT = HERE.parent.parent / "assets" / "seraphhorizons" / "config" / "dialogue" / "trader.json"


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
        elif c == "'":
            j = i + 1
            while text[j] != "'":
                j += 2 if text[j] == "\\" else 1
            out.append(json.dumps(text[i + 1:j]))
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


def convert(entity: dict, gender: str, code="trader", types=TYPES, fine=FINE_OUTFITS, cls=ENTITY_CLASS) -> dict:
    entity["code"] = code
    entity["class"] = cls
    groups = entity["variantgroups"]
    assert [g["code"] for g in groups] == ["gender", "type", "climate"], groups
    groups[1]["states"] = types

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
            renamed[f"{code}-{gender}-*-{climate}"] = value
        else:
            for ours, theirs in fine.items():
                if theirs == kind:
                    renamed[f"{code}-{gender}-{ours}-{climate}"] = value
    # The specific keys first: the game takes the first that matches.
    attributes["partialRandomOutfitsByType"] = dict(
        sorted(renamed.items(), key=lambda kv: "*" in kv[0]))

    for side in ("client", "server"):
        for behavior in entity[side]["behaviors"]:
            if behavior["code"] == "conversable":
                # BetterRuins gives two types its quest dialogues by a patch of its own
                # (patches/trading-betterruins-dialogue.json), which needs "*" to stay a key here.
                behavior["dialogueByType"] = {"*": DIALOGUE}
    return entity


def dialogue(config: dict) -> dict:
    """The game's trader dialogue with the standing option second in its main menu and its answer."""
    components = config["components"]
    main = next(c for c in components if c.get("code") == "main")
    assert main["text"][0]["jumpTo"] == "opentrade", main["text"][0]
    main["text"].insert(1, STANDING_OPTION)
    components.append(STANDING_COMPONENT)
    return config


def visitor(entity: dict, gender: str) -> dict:
    entity = convert(entity, gender, "visitor", VISITOR_TYPES, VISITOR_FINE_OUTFITS, VISITOR_CLASS)
    behaviors = entity["server"]["behaviors"]
    behaviors[:] = [b for b in behaviors if b["code"] not in VISITOR_DROPPED_BEHAVIORS]
    for behavior in behaviors:
        if behavior["code"] == "taskai":
            tasks = behavior["aitasks"]
            tasks[:] = [task for task in tasks if task["code"] not in VISITOR_DROPPED_TASKS]
            for task in tasks:
                if task["code"] == "wander":
                    assert task.get("maxDistanceToSpawn", 99) <= 4, task
    return entity


def main() -> int:
    game = os.environ.get("VINTAGE_STORY")
    if not game:
        print("set VINTAGE_STORY to the game's folder", file=sys.stderr)
        return 1
    src = Path(game) / "assets" / "survival" / "entities" / "humanoid"
    OUT.mkdir(parents=True, exist_ok=True)
    for gender in ("male", "female"):
        text = (src / f"trader-{gender}.json").read_text(encoding="utf-8")
        for name, entity in (("trader", convert(json5(text), gender)), ("visitor", visitor(json5(text), gender))):
            path = OUT / f"{name}-{gender}.json"
            path.write_text(json.dumps(entity, indent="\t", ensure_ascii=False) + "\n", encoding="utf-8")
            print(path)
    vanilla = (Path(game) / "assets" / "survival" / "config" / "dialogue" / "trader.json").read_text(encoding="utf-8")
    DIALOGUE_OUT.parent.mkdir(parents=True, exist_ok=True)
    DIALOGUE_OUT.write_text(json.dumps(dialogue(json5(vanilla)), indent="\t", ensure_ascii=False) + "\n", encoding="utf-8")
    print(DIALOGUE_OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
