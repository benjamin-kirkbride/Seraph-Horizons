#!/usr/bin/env python3
"""Writes the springs' face overlays (README "Springs"): a wet, mossy, cracked mouth drawn over the
host rock's own texture (the blocktype's `blendedOverlays`), one for a spring and a larger one for a
rapids spring. 32 x 32 RGBA, deterministic (seeded), so a rerun gives the same files:

    python3 mods-src/seraphhorizons/Springs/tools/make_seep_texture.py

Needs Pillow. Not part of the build: the PNGs it writes are checked in.
"""

from __future__ import annotations

import math
import random
from pathlib import Path

from PIL import Image

OUT = Path(__file__).resolve().parents[2] / "assets" / "seraphhorizons" / "textures" / "block" / "spring"
SIZE = 32


def seep(mouth_w: float, mouth_h: float, wet_reach: float, seed: int) -> Image.Image:
    rnd = random.Random(seed)
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    px = img.load()
    cx, cy = 15.5, 13.0

    def put(x: int, y: int, rgba: tuple[int, int, int, int]) -> None:
        if 0 <= x < SIZE and 0 <= y < SIZE:
            px[x, y] = rgba

    # The wet sheen: darker rock round the mouth, running down the face below it.
    for y in range(SIZE):
        for x in range(SIZE):
            dx = (x - cx) / (mouth_w * 2.2)
            if y < cy:
                d = math.hypot(dx, (y - cy) / (mouth_h * 1.4))
            else:
                # A tongue of wet stone widening a little as it runs down.
                spread = 1 + (y - cy) / (wet_reach * 1.5)
                d = math.hypot(dx / spread, (y - cy) / wet_reach)
            if d < 1:
                a = int(120 * (1 - d) ** 0.7 + rnd.randint(-12, 12))
                put(x, y, (18, 26, 30, max(0, min(150, a))))

    # The crack the water comes out of: a dark, ragged slit.
    for y in range(SIZE):
        for x in range(SIZE):
            nx = (x - cx) / mouth_w
            ny = (y - cy) / mouth_h
            wobble = 0.18 * math.sin(y * 1.7 + seed) + 0.12 * math.sin(x * 2.3)
            if nx * nx + ny * ny < 1 + wobble:
                shade = rnd.randint(8, 18)
                put(x, y, (shade, shade + 4, shade + 6, 235))

    # Glints of water on the lip and the wet stone below.
    for _ in range(int(10 + wet_reach)):
        x = int(round(cx + rnd.uniform(-mouth_w * 1.6, mouth_w * 1.6)))
        y = int(round(cy + rnd.uniform(mouth_h * 0.6, wet_reach)))
        put(x, y, (170, 200, 215, rnd.randint(90, 140)))

    # Moss on the lip and in the wet: clumps of two or three greens.
    greens = [(58, 86, 38), (74, 104, 46), (92, 120, 52), (48, 70, 34)]
    for _ in range(26):
        ang = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(1.0, 1.9)
        x0 = cx + math.cos(ang) * mouth_w * r
        y0 = cy + math.sin(ang) * mouth_h * r * 0.8
        if rnd.random() < 0.35:
            y0 = cy + rnd.uniform(mouth_h, wet_reach * 0.9)
            x0 = cx + rnd.uniform(-mouth_w * 1.8, mouth_w * 1.8)
        for _ in range(rnd.randint(2, 4)):
            g = rnd.choice(greens)
            put(int(round(x0 + rnd.uniform(-1, 1))), int(round(y0 + rnd.uniform(-1, 1))), (*g, rnd.randint(190, 240)))
    return img


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    seep(2.2, 4.2, 14, seed=7).save(OUT / "seep-normal.png")
    seep(3.4, 5.6, 18, seed=11).save(OUT / "seep-rapid.png")
    print(f"wrote {OUT}/seep-normal.png, seep-rapid.png")


if __name__ == "__main__":
    main()
