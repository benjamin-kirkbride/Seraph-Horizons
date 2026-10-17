"""The amalgam pan's slurry texture: the charge being worked, written by make_shape.py.

The pan's mercury is drawn as Expanded Matter draws its mercury (the game's andesite with its kimberlite
blended over it, Lighten: a speckled blue-grey); this is the charge on top of it, ground concentrate
worked into the mercury, as a working Washoe pan looked: a grey-brown slurry. It is made to sit beside
that mercury: a 32 x 32 tile with the same fine per-pixel grain as the game's rock textures, a warmer and
browner base, soft swirls of the pulp being stirred (low waves of whole periods, so it tiles) and a
scatter of small bright beads of mercury showing through. A fixed hash gives the grain and the beads, so
every run writes the same pixels. Stdlib only.
"""

from __future__ import annotations

import math
import struct
import zlib

SIZE = 32
BASE = (121, 113, 102)                       # the pulp's grey-brown
GRAIN = 9.0                                  # the per-pixel grain, as in the game's rock textures
SWIRL = 11.0                                 # the stirred pulp's soft lighter and darker swirls
WAVES = (                                    # (amplitude, cycles across x, across y, phase): low, so the swirls are broad
    (0.50, 1, 1, 0.4),
    (0.32, 2, -1, 1.9),
    (0.18, 1, -2, 3.7),
)
BEAD = (152, 162, 168)                       # a bead of mercury showing through: the mercury's blue-grey, catching the light
BEADS = 0.03                                 # the share of pixels that are beads


def _hash(x, y, salt=0):
    """A fixed pixel hash in 0..1 (an integer hash: no random state)."""
    h = (x * 374761393 + y * 668265263 + salt * 2246822519) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0


def pixels():
    """Rows of (r, g, b, a) tuples, SIZE x SIZE."""
    rows = []
    t = 2 * math.pi / SIZE
    for y in range(SIZE):
        row = []
        for x in range(SIZE):
            if _hash(x, y, 1) < BEADS:
                row.append(BEAD + (255,))
                continue
            w = sum(a * math.sin(t * (kx * x + ky * y) + ph) for a, kx, ky, ph in WAVES)
            light = SWIRL * w + GRAIN * (2.0 * _hash(x, y) - 1.0)
            row.append(tuple(max(0, min(255, round(c + light))) for c in BASE) + (255,))
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
