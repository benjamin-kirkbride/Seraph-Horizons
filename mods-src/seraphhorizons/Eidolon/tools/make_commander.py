#!/usr/bin/env python3
"""Generate the eidolon command tool's item shape (Python 3.11 stdlib only).

The command tool (`seraphhorizons:eidoloncommander`, ../README.md "The command tool") is a small
Jonas device made of what its recipe takes: a tarnished brass casing (the plate) bound with two steel
bands and a cupronickel face plate (the nails and strips), the game's Jonas cylinder
(`item/jonas/parts/cylinder01`, its temporal core in a glass dome) lying along its top, and the
game's Jonas connector (`item/jonas/parts/connector01`) at its front end as the emitter. The two
Jonas parts are Anego Studios' models (Vintage Story 1.22.7; ../../CREDITS.md), copied unchanged
but moved into place, as the eidolon's body is: frozen into our namespace so a game update cannot
change them under us, each pinned by sha256. Textures point at the game's own files by `game:`
path (none are copied). The device runs along x, 13 voxels long and centred on the block, the
connector at −x.

    python3 mods-src/seraphhorizons/Eidolon/tools/make_commander.py            # write the shape
    python3 mods-src/seraphhorizons/Eidolon/tools/make_commander.py --check    # compare, write nothing

The game install comes from --game or $VINTAGE_STORY.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
MOD = HERE.parents[1]
SHAPE_OUT = MOD / "assets" / "seraphhorizons" / "shapes" / "item" / "eidoloncommander.json"
PARTS_REL = Path("assets") / "survival" / "shapes" / "item" / "jonas" / "parts"
VANILLA_SHA256 = {  # game 1.22.7
    "connector01": "d27efa8f7acb7a9a0855f0312ce93ae51c8e2e3d1b8fc2ee5812e2a283ea1280",
    "cylinder01": "2bc953c0d6c518efdfd122216c265c89255db308e8a09a9ad1bf2df884edb902",
}

# The casing and its fittings, in item voxels (16 to a block): a box 7 long (x), 3 high and 3 wide.
CASING_FROM = (7.5, 0.0, 6.5)
CASING_TO = (14.5, 3.0, 9.5)
# Where each Jonas part's root goes: a shift of its whole tree (the children are relative to it).
# The cylinder lies on the casing's top, centred on it; the connector lies along x (its own root's
# rotation turns it so) with its far end two voxels into the casing's front.
SHIFT = {"cylinder01": (3.0, 3.0, 0.25), "connector01": (-1.5, 0.0, 0.0)}
PREFIX = {"cylinder01": "cylinder-", "connector01": "connector-"}

OWN_TEXTURES = {
    "brass": "game:block/metal/tarnished/brass",
    "steel": "game:block/metal/tarnished/steel",
    "cupronickel": "game:block/metal/tarnished/cupronickel",
}


def box_faces(texture, size):
    """Every face of a box, its uv the face's size from the texture's corner."""
    x, y, z = size
    dims = {"north": (x, y), "south": (x, y), "east": (z, y), "west": (z, y), "up": (x, z), "down": (x, z)}
    return {f: {"texture": f"#{texture}", "uv": [0.0, 0.0, round(w, 4), round(h, 4)]} for f, (w, h) in dims.items()}


def box(name, texture, frm, to, children=None):
    size = tuple(to[i] - frm[i] for i in range(3))
    el = {"name": name, "from": list(frm), "to": list(to), "faces": box_faces(texture, size)}
    if children:
        el["children"] = children
    return el


def casing():
    w = CASING_TO[2] - CASING_FROM[2]
    length = CASING_TO[0] - CASING_FROM[0]
    h = CASING_TO[1] - CASING_FROM[1]
    # Children are relative to the casing's from.
    bands = [box(f"band{i + 1}", "steel", (x, -0.25, -0.25), (x + 0.75, h + 0.25, w + 0.25)) for i, x in enumerate((0.75, length - 1.5))]
    face = box("faceplate", "cupronickel", (1.75, h, 0.5), (length - 1.75, h + 0.25, w - 0.5))
    return box("casing", "brass", CASING_FROM, CASING_TO, bands + [face])


def moved(part, shape):
    """The part's root elements shifted into place, every element renamed with the part's prefix."""
    dx, dy, dz = SHIFT[part]
    roots = copy.deepcopy(shape["elements"])

    def rename(el):
        el["name"] = PREFIX[part] + el["name"]
        for c in el.get("children", []):
            rename(c)

    for el in roots:
        for key in ("from", "to", "rotationOrigin"):
            if key in el:
                el[key] = [el[key][0] + dx, el[key][1] + dy, el[key][2] + dz]
        rename(el)
    return roots


def textures_of(shape):
    return {k: v if ":" in v else f"game:{v}" for k, v in shape["textures"].items()}


def build(parts):
    textures = dict(OWN_TEXTURES)
    elements = [casing()]
    for part in ("cylinder01", "connector01"):
        for k, v in textures_of(parts[part]).items():
            if textures.get(k, v) != v:
                raise SystemExit(f"{part}: texture {k} is {v}, already {textures[k]}")
            textures[k] = v
        elements += moved(part, parts[part])
    return {
        "editor": {"allAngles": True, "entityTextureMode": False},
        "textureWidth": 16,
        "textureHeight": 16,
        "textures": dict(sorted(textures.items())),
        "elements": elements,
    }


def render(obj):
    return json.dumps(obj, indent="\t") + "\n"


def load_part(game, part):
    path = Path(game) / PARTS_REL / f"{part}.json"
    data = path.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    if digest != VANILLA_SHA256[part]:
        raise SystemExit(f"{path}: sha256 {digest}, expected {VANILLA_SHA256[part]} (game 1.22.7). "
                         "The snapshot is frozen; update VANILLA_SHA256 only on purpose.")
    return json.loads(data)


def main(argv=None):
    ap = argparse.ArgumentParser(description="Generate the eidolon command tool's shape.")
    ap.add_argument("--game", help="a game install (default: $VINTAGE_STORY)")
    ap.add_argument("--check", action="store_true", help="compare with the committed file, write nothing")
    args = ap.parse_args(argv)
    game = args.game or os.environ.get("VINTAGE_STORY")
    if not game:
        raise SystemExit("no game install: pass --game or set VINTAGE_STORY")
    text = render(build({p: load_part(game, p) for p in VANILLA_SHA256}))
    if args.check:
        if not SHAPE_OUT.exists() or SHAPE_OUT.read_text() != text:
            print(f"stale: {SHAPE_OUT.relative_to(MOD)}")
            return 1
        print("up to date")
        return 0
    SHAPE_OUT.parent.mkdir(parents=True, exist_ok=True)
    SHAPE_OUT.write_text(text)
    print(f"wrote {SHAPE_OUT.relative_to(MOD)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
