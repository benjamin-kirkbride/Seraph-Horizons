"""A square coil spring: a square helix of straight bars, wound round a square bar.

The mandrel station winds one from a steel rod (MandrelStation/: the spring item is its finished coil)
and the draw bench's follower spring is one (DrawBench/), so the two are drawn by the same code.

The coil is `4 * turns` sides, each a straight bar of square wire running along one face of the bar
it is wound on, from corner to corner of the coil's centre line, and rising a quarter of the pitch: a
right-hand helix. Seen along +z from its start, the corners go top-east, top-west, bottom-west,
bottom-east (the angle from +x towards +y grows with z), so the sides lie on the top (`u`), west
(`w`), bottom (`d`) and east (`e`) faces in turn. Each side's bar is as long as its centre line plus
the wire's width, so neighbouring sides fill the corner between them.

A side is made lying along +z from its start corner (`axial`) and turned into place about the
face's normal through that corner (`swing`): a model can show it lying along the bar first, as the
rod before it is wound, with the same element.

Coordinates are voxels; `x`, `y` across the coil, `z` along it.
"""

from __future__ import annotations

import math

from .geometry import IDENT, El, rotate

# the faces in winding order: (name, the face normal's axis, its sign, the side's run across it (x, y))
FACES = (("u", "y", 1, (-1.0, 0.0)), ("w", "x", -1, (0.0, -1.0)), ("d", "y", -1, (1.0, 0.0)), ("e", "x", 1, (0.0, 1.0)))
CORNERS = ((1, 1), (-1, 1), (-1, -1), (1, -1))   # (x, y) signs of each face's side's start corner
FACE_UV = {"north": (0, 1), "south": (0, 1), "east": (2, 1), "west": (2, 1), "up": (0, 2), "down": (0, 2)}


class Coil:
    """A square coil of `turns` turns of square `wire`, wound on a bar of half width `bore_half` whose
    axis runs along z through `axis_xy`, its first side starting (on its centre line) at z `z0`, rising
    `pitch` a turn."""

    def __init__(self, axis_xy, bore_half, wire, pitch, z0, turns):
        self.cx, self.cy = axis_xy
        self.bore_half, self.wire, self.pitch, self.z0, self.turns = bore_half, wire, pitch, z0, turns
        self.h = bore_half + wire / 2                 # the centre line's half width
        self.run = 2 * self.h                         # a side's run across its face, corner to corner
        self.rise = pitch / 4                         # and its rise along z
        self.length = math.hypot(self.run, self.rise)  # its centre line
        self.tilt = math.atan2(self.rise, self.run)   # how far it leans from square across the bar (radians)

    @property
    def sides(self):
        return 4 * self.turns

    def face(self, j):
        """Side j's face name (u, w, d, e)."""
        return FACES[j % 4][0]

    def name(self, j):
        """Side j's name: its turn (1..) and its face, e.g. `3w`."""
        return f"{j // 4 + 1}{self.face(j)}"

    def corner(self, j):
        """The centre line's corner where side j starts (j from 0); side j ends at corner j + 1."""
        sx, sy = CORNERS[j % 4]
        return (self.cx + sx * self.h, self.cy + sy * self.h, self.z0 + j * self.rise)

    def direction(self, j):
        """Side j's unit direction along its centre line."""
        dx, dy = FACES[j % 4][3]
        c, s = math.cos(self.tilt), math.sin(self.tilt)
        return (dx * c, dy * c, s)

    def normal(self, j):
        """The axis (x or y) of side j's face normal, and the normal's sign (outward from the bar)."""
        return FACES[j % 4][1], FACES[j % 4][2]

    def swing(self, j):
        """(axis, radians): the turn about side j's face normal, through its start corner, that takes +z to
        its direction (geometry.rot's sense)."""
        axis = FACES[j % 4][1]
        u = self.direction(j)
        return axis, (math.atan2(-u[1], u[2]) if axis == "x" else math.atan2(u[0], u[2]))

    def span(self):
        """How far a side's bar reaches along z beyond its centre line's ends (the tilted bar's corner)."""
        return (self.wire / 2) * (math.sin(self.tilt) + math.cos(self.tilt))

    def axial(self, j, name, part, texture, tex_size=64, at=None, across=None, extend=0.0):
        """Side j's bar lying along +z from its start corner, before its swing: square `across` (the wire's
        width unless given) and as long as its centre line and the wire's width, `extend` more at each end.
        `at` puts its start corner somewhere else (x, y, z). Every face takes `texture`, its UVs a region of
        the texture in proportion to the face (tex_size / 16 texture units a voxel)."""
        c0 = self.corner(j) if at is None else at
        a = self.wire if across is None else across
        size = [a, a, self.length + self.wire + 2 * extend]
        centre = [c0[0], c0[1], c0[2] + self.length / 2]
        el = El(name, size, centre, [r[:] for r in IDENT], {}, part)
        for d, (u, v) in FACE_UV.items():
            w = min(abs(size[u]) * tex_size / 16, tex_size)
            h = min(abs(size[v]) * tex_size / 16, tex_size)
            el.faces[d] = {"texture": "#" + texture, "uv": [0.0, 0.0, w, h]}
        return el

    def side(self, j, name, part, texture, tex_size=64, across=None, extend=0.0):
        """Side j's bar in place on the coil: `axial` swung about its start corner."""
        el = self.axial(j, name, part, texture, tex_size=tex_size, across=across, extend=extend)
        axis, ang = self.swing(j)
        rotate([el], axis, math.degrees(ang), self.corner(j))
        return el

    def end(self, j):
        """The centre line's point where side j ends (corner j + 1)."""
        return self.corner(j + 1)
