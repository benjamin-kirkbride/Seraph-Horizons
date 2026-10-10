"""The amalgam pan's provisional mercury texture: a plain 32 x 32 silver liquid, written by make_shape.py.

Neither the game nor the pack's mods have a mercury texture: Expanded Matter's `em:mercuryportion`
borrows the game's andesite and kimberlite rock, blended, for its bucket. This one is made for the pan's
pool in the style of the game's liquids (32 x 32, soft ripples, a few bright glints), from integer-period
waves so it tiles, and a fixed hash for its grain, so every run writes the same pixels. Stdlib only.
"""

from __future__ import annotations

import math
import struct
import zlib

SIZE = 32
BASE = (190, 196, 203)                       # the pool's silver, a little blue
RIPPLE = 20.0                                # how far the waves take it lighter or darker
GLINT = 26.0                                 # extra light on the crests


def _grain(x, y):
    """A fixed pixel noise in -1..1 (an integer hash: no random state)."""
    h = (x * 374761393 + y * 668265263) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFF) / 127.5 - 1.0


def pixels():
    """Rows of (r, g, b, a) tuples, SIZE x SIZE."""
    rows = []
    for y in range(SIZE):
        row = []
        for x in range(SIZE):
            t = 2 * math.pi / SIZE
            w = (0.42 * math.sin(t * (x + 2 * y))
                 + 0.28 * math.sin(t * (2 * x - y) + 1.3)
                 + 0.17 * math.sin(t * (3 * x + 4 * y) + 0.7)
                 + 0.13 * math.sin(t * (5 * x - 3 * y) + 2.1))
            light = RIPPLE * w + GLINT * max(0.0, w - 0.62) / 0.38 + 3.0 * _grain(x, y)
            row.append(tuple(max(0, min(255, round(c + light * (1.0 if i < 2 else 1.04)))) for i, c in enumerate(BASE)) + (255,))
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
