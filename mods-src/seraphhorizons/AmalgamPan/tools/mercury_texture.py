"""The amalgam pan's provisional mercury texture: a 32 x 32 liquid silver, written by make_shape.py.

Neither the game nor the pack's mods have a mercury texture: Expanded Matter's `em:mercuryportion`
borrows the game's andesite and kimberlite rock, blended, for its bucket. This one is made for the pan's
pool in the style of the game's liquids (32 x 32): a smooth silver with broad, soft swells of light and
shade, as a mirror-bright liquid shows its surroundings, and one soft glint on the brightest swell. The
waves have whole periods across the tile, so it tiles, and only low ones, so it shows no grid or pattern
of dots (the first version's diagonal ripples and glints read as quilted cloth); a fixed hash gives a
faint grain, so every run writes the same pixels. Stdlib only.
"""

from __future__ import annotations

import math
import struct
import zlib

SIZE = 32
BASE = (182, 187, 194)                       # the pool's silver, a little blue
SWELL = 20.0                                 # how far the swells take it lighter or darker
GLINT = 90.0                                 # extra light on the brightest swell's crest
GRAIN = 0.8
WAVES = (                                    # (amplitude, cycles across x, across y, phase): low, so the swells are broad
    (0.50, 1, 0, 0.0),
    (0.30, 1, 1, 2.0),
    (0.20, 0, 1, 4.1),
    (0.12, 2, 1, 1.0),
    (0.07, 1, -3, 2.9),
)


def _grain(x, y):
    """A fixed pixel noise in -1..1 (an integer hash: no random state)."""
    h = (x * 374761393 + y * 668265263) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFF) / 127.5 - 1.0


def pixels():
    """Rows of (r, g, b, a) tuples, SIZE x SIZE."""
    rows = []
    t = 2 * math.pi / SIZE
    for y in range(SIZE):
        row = []
        for x in range(SIZE):
            w = sum(a * math.sin(t * (kx * x + ky * y) + ph) for a, kx, ky, ph in WAVES)
            light = SWELL * math.tanh(1.6 * w) + GLINT * max(0.0, w - 0.55) ** 2 + GRAIN * _grain(x, y)
            row.append(tuple(max(0, min(255, round(c + light * (1.0 if i < 2 else 1.03)))) for i, c in enumerate(BASE)) + (255,))
        rows.append(row)
    return rows


def png_bytes(rows=None):
    """The texture as a PNG (8-bit RGBA, no interlace)."""
    rows = pixels() if rows is None else rows
    h, w = len(rows), len(rows[0])
    raw = b"".join(b"\x00" + bytes(v for px in row for v in px) for row in rows)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def decode(data):
    """The pixel rows of a PNG png_bytes wrote (8-bit RGBA, filter 0), for the tests: they compare pixels,
    not bytes, since another zlib may compress the same rows differently."""
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    pos, idat, w, h = 8, b"", 0, 0
    while pos < len(data):
        n = struct.unpack(">I", data[pos:pos + 4])[0]
        kind, body = data[pos + 4:pos + 8], data[pos + 8:pos + 8 + n]
        if kind == b"IHDR":
            w, h, depth, colour = struct.unpack(">IIBB", body[:10])
            assert (depth, colour) == (8, 6), "not 8-bit RGBA"
        elif kind == b"IDAT":
            idat += body
        pos += 12 + n
    raw = zlib.decompress(idat)
    rows = []
    for y in range(h):
        line = raw[y * (1 + 4 * w):(y + 1) * (1 + 4 * w)]
        assert line[0] == 0, "a filtered row"
        rows.append([tuple(line[1 + 4 * x:5 + 4 * x]) for x in range(w)])
    return rows
