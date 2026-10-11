#!/usr/bin/env python3
"""Make the stainless steel chain textures the game lacks (the eidolon's fittings, ../README.md
"Fittings"): the game's steel chain, desaturated and lightened, as the game's stainless steel plate
is to its steel plate (about a quarter brighter, near grey). Needs Pillow.

    assets/seraphhorizons/textures/block/metal/armor-generic/chain-stainlesssteel.png
        from game block/metal/armor-generic/chain-steel.png: the eidolon's chain skirt
    assets/game/textures/entity/humanoid/serapharmor/chain/stainlesssteel.png
        from game entity/humanoid/serapharmor/chain/steel.png: the stainless chain item's
        texture, where the game's metalchain item type looks for {metal}

The inputs are pinned by sha256 (game 1.22.7). Alpha is kept as it is.

    python3 mods-src/seraphhorizons/Eidolon/tools/make_stainless_chain.py           # write
    python3 mods-src/seraphhorizons/Eidolon/tools/make_stainless_chain.py --check   # compare pixels

The game install comes from --game or $VINTAGE_STORY.
"""

from __future__ import annotations

import argparse
import hashlib
import os
import sys
from pathlib import Path

from PIL import Image

MOD = Path(__file__).resolve().parents[2]
TEXTURES = Path("assets") / "survival" / "textures"

# (game input, its sha256, output)
JOBS = [
    (TEXTURES / "block/metal/armor-generic/chain-steel.png",
     "38a15398efae337c345de6b14e0cffcc9466332d8931e056b31304d1be4f59b0",
     MOD / "assets/seraphhorizons/textures/block/metal/armor-generic/chain-stainlesssteel.png"),
    (TEXTURES / "entity/humanoid/serapharmor/chain/steel.png",
     "68e124228407d5927ef400edc49f428d346bfb1ade24f9de5be6392f588cf1ff",
     MOD / "assets/game/textures/entity/humanoid/serapharmor/chain/stainlesssteel.png"),
]

SATURATION = 0.4   # what is left of each pixel's colour away from its grey
LIGHTEN = 1.25     # the game's stainless plate over its steel plate, on average


def restyle(image: Image.Image) -> Image.Image:
    rgba = image.convert("RGBA")
    data = rgba.tobytes()
    out = bytearray()
    for i in range(0, len(data), 4):
        r, g, b, a = data[i:i + 4]
        grey = 0.299 * r + 0.587 * g + 0.114 * b
        out += bytes(min(255, round((grey + (c - grey) * SATURATION) * LIGHTEN)) for c in (r, g, b))
        out.append(a)
    return Image.frombytes("RGBA", rgba.size, bytes(out))


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--game", default=os.environ.get("VINTAGE_STORY"), help="the game install")
    ap.add_argument("--check", action="store_true", help="compare with the committed files, write nothing")
    args = ap.parse_args()
    if not args.game:
        print("give the game install with --game or $VINTAGE_STORY", file=sys.stderr)
        return 2
    stale = 0
    for rel, sha, out in JOBS:
        src = Path(args.game) / rel
        if hashlib.sha256(src.read_bytes()).hexdigest() != sha:
            print(f"{src}: not the 1.22.7 file (sha256 differs); stopping", file=sys.stderr)
            return 1
        made = restyle(Image.open(src))
        if args.check:
            if not out.exists() or Image.open(out).convert("RGBA").tobytes() != made.tobytes():
                print(f"{out.relative_to(MOD)}: differs")
                stale += 1
            continue
        out.parent.mkdir(parents=True, exist_ok=True)
        made.save(out, optimize=True)
        print(f"wrote {out.relative_to(MOD)}")
    return 1 if stale else 0


if __name__ == "__main__":
    sys.exit(main())
