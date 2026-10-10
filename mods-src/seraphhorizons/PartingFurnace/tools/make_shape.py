#!/usr/bin/env python3
"""Generate the parting furnace's shapes, rig and reference poses (Python 3.11 stdlib only).

The parting furnace is one upgradeable machine that does every parting step of the ore line (#740), a small
parting works of the 1800s on a paved floor under one chimney stack, its footprint its tier 4 size from the
frame on:

    the frame    the brick-paved floor (the fires' flues run under it to the stack), the chimney stack and four
                 cast-iron columns with plummer blocks for the line shaft;
    tier 2       the blowing engine (a double-acting blowing tub worked by a Scotch yoke from an overhung crank on
                 the line shaft, which comes in from the vanilla axle), its blast main to an English cupellation
                 (test) furnace, and a liquation furnace;
    tier 3       the acid parting vessel (stoneware on a sand bath over a firebox, under a hood) and the
                 precipitating tub with its copper plates;
    tier 4       the Parkes kettle in its brick setting, its mechanical stirrer (a bevel gear off the line shaft),
                 and the input and output chutes (a siphon out of the kettle).

Every element was made for the Seraph Horizons mod; no other mod's model is used.

It writes, deterministically,

    partingfurnace.json         the whole machine, every tier's parts   (assets/.../shapes/block/)
    partingfurnace_frame.json   the frame only (block and item)         (assets/.../shapes/block/)
    partingfurnace-rig.json     cells, anchors and the part rig         (assets/.../config/)
    rig-reference.json          every part's matrix at a grid of poses  (tests/PartingFurnace/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_partingfurnace.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the machine
box's north-west-bottom corner (the "build frame"); `shipped()` moves it so the controller cell, the middle of
the front row at ground level, is [0,0,0]. The works' front, where the fire doors, the litharge pot and the
kettle's door are, faces south (+z); the chimney stands at the back.

The rig reads one input, theta, the axle's angle: the line shaft turns with it, the blowing tub's yoke rises
and falls with the crank, and the stirrer turns at half the shaft's speed. Every other part stands still.
"""

from __future__ import annotations

import argparse
import copy
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Machines" / "tools"))
sys.path.insert(0, str(Path(__file__).resolve().parent))

from machinegen.checks import cell_boxes, cells_touched, with_lids  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import IDENT, El, flatten, rotate, translate  # noqa: E402
from machinegen.output import (reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               shift_point, worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "PartingFurnace" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/PartingFurnace/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 6, 5, 4          # six wide (x), four deep (z); three high, the stack to five
ORIGIN_CELL = (2, 0, 3)                      # the controller: the middle of the front row, at ground level
POWER_CELL, POWER_FACE = (5, 2, 1), "east"   # the vanilla axle comes in along x at (y 40, z 24): the line shaft
INPUT_CELL, INPUT_FACE = (5, 1, 0), "north"  # tier 4: the input chute comes in through the back, into the kettle
OUTPUT_CELL, OUTPUT_FACE = (5, 0, 3), "south"   # tier 4: the output chute runs out at the front, from the siphon

TEXTURES = {
    "brick": "game:block/clay/brick/eight/running/orange1",     # the furnaces' brickwork, the stack
    "paving": "game:block/clay/brick/eight/running/red1",       # the floor
    "firebrick": "game:block/clay/brick/eight/running/cream1",  # fire bridge, linings, the stack's cap
    "iron": "game:block/metal/plate/iron",                      # castings: columns, pots, plates, the kettle
    "steel": "game:block/metal/sheet-plain/steel1",             # shafts, pins, gears, rods
    "oak": "game:block/wood/debarked/oak",                      # the axle's continuation, the tub's bars
    "planks": "game:block/wood/planks/oak1",                    # the precipitating tub's staves
    "boneash": "game:block/stone/rock/chalk1",                  # the test
    "lead": "game:block/metal/ingot/lead",                      # molten lead
    "litharge": "game:block/clay/hardened/orange",              # litharge in its pot
    "tin": "game:block/metal/ingot/tin",                        # liquation cakes, the tin run out
    "zinc": "game:block/metal/ingot/zinc",                      # the Parkes crust
    "dross": "game:block/coal/charcoal",                        # dross left on the liquation hearth
    "coke": "game:block/coal/coke",                             # fuel on the grate
    "ember": "game:block/coal/ember",                           # the fire
    "sand": "game:block/stone/sand/sandstone",                  # the sand bath
    "stoneware": "game:block/clay/ceramic-dark",                # the parting vessel
    "acid": "game:block/liquid/dilutedalum",                    # aqua fortis with the silver in it
    "solution": "game:block/liquid/dye/blue",                   # the precipitating tub: copper nitrate
    "copper": "game:block/metal/sheet-plain/copper1",           # the copper plates
    "soot": "game:block/black",                                 # down the stack's flue
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

FLOOR = 2.0                                  # the paved floor's top

# ---------------------------------------------------------------- the line shaft and its columns
LS = (40.0, 24.0)                            # (y, z): the line shaft's axis, along x, at the power cell's centre
LS_R = 1.0                                   # its apothem (wrought iron, an octagon)
LS_WEST = 9.6                                # its west end, in the crank's boss
X_FACE = CELLS_X * B                         # the east face, where the axle comes in
OAK = (95.0, X_FACE)                         # the vanilla axle's continuation: its cross profile, to the coupling
FLANGE = (94.5, 95.0)                        # the coupling's flange, against the east bearing
COL_W = 2.4                                  # a column's section
COLUMNS = (("s1", 14.4), ("s2", 46.35), ("s3", 63.95), ("s4", 92.1))   # west face of each column (x)
COL_TOP = 36.0                               # the columns' capitals; the plummer blocks on them
BEARING_Y = (37.6, 42.0)                     # a plummer block's body round the shaft
BEARING_Z = (22.2, 25.8)

# ---------------------------------------------------------------- the blowing engine (tier 2)
TUB_C = (7.8, 24.0)                          # (x, z): the blowing tub's axis, under the crank's yoke
TUB_R = 5.4
TUB_Y = (3.2, 19.5)                          # its barrel, between the flanges
CRANK_X = (9.0, 10.2)                        # the overhung crank's disc, on the line shaft's west end
CRANK_DISC_R = 4.6
CRANK_R = 3.5                                # the crank pin's radius: the yoke's stroke is twice it
PIN_R = 0.8                                  # the pin's apothem (an octagon, so 0.866 to its corners)
PIN_X = (6.4, CRANK_X[0])
YOKE_X = (7.0, 8.6)                          # the slotted yoke, in the y-z plane, round the pin
SLOT_H = 0.95                                # the slot's half height (the pin's corners, and a little)
SLOT_L = CRANK_R + 0.866 + 0.2               # its half length along z
YOKE_BAR = 1.2                               # the yoke's top and bottom bars
YOKE_SIDE = 1.0                              # its side bars
ROD_LOW = 14.0                               # the piston rod's foot at rest (inside the tub)
GUIDE_TOP = 47.5                             # the yoke's guides' crossbar

# ---------------------------------------------------------------- the cupellation furnace (tier 2)
CUP_X = (17.2, 45.8)
CUP_Z = (6.0, 32.0)
CUP_WALL = 3.5
CUP_HEARTH = 12.0                            # the hearth's top: the test's floor
CUP_SPRING = 22.0                            # the vault springs off the side walls here
CUP_RISE = 4.5
CUP_STAVES, CUP_T = 7, 2.5
FIRE_X = (CUP_X[0] + CUP_WALL, 27.5)         # the fireplace, at the west end, sunk into the hearth
GRATE_Y = 9.0
BRIDGE_X = (27.5, 29.5)                      # the fire bridge
TEST_C = (36.0, 19.0)                        # (x, z): the test's centre
TEST_A = (5.0, 7.5)                          # its half axes (x, z): an oval of bone ash in an iron ring
TEST_RING = 0.9
TEST_TOP = 13.2
OPENING_X = (32.5, 39.5)                     # the working opening in the front wall, over the breast
FIRE_HOLE_X, FIRE_HOLE_Y = (21.6, 26.4), (12.6, 17.0)   # the fire hole in the front wall, over the grate
FIRE_DOOR_OPEN = 105.0                       # degrees the fire door stands open
OPENING_TOP = 17.5
TUYERE = (36.0, 14.8)                        # (x, y): the blast's nozzle through the back wall
POT_L = (36.0, 38.0)                         # (x, z): the litharge pot
BLAST_R = 1.3
BLAST_Z = 3.0                                # the blast main runs along the back at z 3

# ---------------------------------------------------------------- the liquation furnace (tier 2)
# Built in its own frame along x (the hearth falling west to its open end, its firebox door on the +z side),
# then turned a quarter about y onto the works' west front corner: the open end and the receiving pot face the
# front (south), the firebox door the working floor in front of the cupellation furnace (east).
LIQ_X = (24.0, 44.0)
LIQ_Z = (46.0, 60.0)
LIQ_BASE = 10.0
LIQ_WALL = 2.2
LIQ_SPRING, LIQ_RISE = 20.0, 3.0
LIQ_STAVES, LIQ_T = 5, 2.0
PLATE = ((24.8, 10.72), (41.0, 15.0))        # ((x, y), (x, y)): the inclined hearth plate's top, low end west
PLATE_T = 0.7
POT_Q = (16.6, 53.0)                         # (x, z): the receiving pot, at the low end
LIQ_PIVOT = (32.0, 53.0)                     # (x, z) in its own frame ...
LIQ_AT = (8.6, 44.6)                         # ... goes here in the works' (x, z), turned +90 degrees about y


def liq_world(p):
    """A point of the liquation furnace's own frame in the works' frame."""
    x, y, z = p
    return (LIQ_AT[0] + (z - LIQ_PIVOT[1]), y, LIQ_AT[1] - (x - LIQ_PIVOT[0]))

# ---------------------------------------------------------------- the acid parting (tier 3)
AC_X = (49.8, 62.7)
AC_PIER = 1.2
AC_BACK = (16.0, 18.8)                       # the hood's back wall, against the stack
AC_FRONT = 31.5
TRAY_Y = (11.0, 13.2)
VESSEL_C = (56.25, 25.0)
VESSEL_R = (3.5, 4.2)
VESSEL_Y = (12.0, 21.0)
HOOD_Y = (28.0, 30.0)
TUB2_C = (56.25, 45.5)                       # the precipitating tub
TUB2_R = (4.6, 5.3)
TUB2_Y = (FLOOR, 12.0)

# ---------------------------------------------------------------- the Parkes kettle (tier 4)
KET_C = (79.1, 24.0)                         # (x, z): the kettle's axis, under the line shaft
SET_R = 12.0                                 # the round brick setting's apothem
SET_TOP = 24.0
KET_R = (9.2, 10.0)                          # the kettle's wall near its rim
KET_FLANGE = 11.4
SKIM_ANGLE = 45.0                            # the skimmer lies across the flange this far round from the front (degrees, towards -x)
BATH_Y = 22.0
STIR_RATIO = 0.5                             # the stirrer turns at half the line shaft's speed
GEAR_MOD = 0.5
PINION_TEETH, WHEEL_TEETH = 12, 24
GEAR_FACE = 2.0
APEX = (KET_C[0], LS[0], LS[1])              # the bevel pair's cone apex: where the two axes cross
STIR_TOP = 36.6
GAL_X = (68.0, 69.6, 88.6, 90.2)             # the gallows' standards (x), on the kettle's flange
GAL_UP = (31.6, 33.6)                        # its upper crossbeam
GAL_LOW = (26.8, 28.4)                       # its lower crossbeam
SIPHON = (85.5, 27.5)                        # (x, z): the siphon's leg in the kettle
SIPHON_TOP = 27.0
SIPHON_OUT = 38.6                            # its outer leg (z), in front of the setting


# ---------------------------------------------------------------- box helpers
def skin(el, tex):
    """Every face takes `tex`, its UVs a region of the texture in proportion to the face's size."""
    axes = {"north": (0, 1), "south": (0, 1), "east": (2, 1), "west": (2, 1), "up": (0, 2), "down": (0, 2)}
    el.faces = {}
    for d, (u, v) in axes.items():
        w = min(abs(el.size[u]) * TEX / 16, TEX)
        h = min(abs(el.size[v]) * TEX / 16, TEX)
        el.faces[d] = {"texture": "#" + tex, "uv": [0.0, 0.0, w, h]}
    return el


def box(lo, hi, name, part, tex):
    c = [(lo[k] + hi[k]) / 2 for k in range(3)]
    return skin(El(name, [hi[k] - lo[k] for k in range(3)], c, [r[:] for r in IDENT], {}, part), tex)


def tiles(lo, hi, name, part, tex, seg=16.0):
    """A box split into pieces no longer than `seg` along each axis, so no face's texture is stretched."""
    n = [max(1, math.ceil((hi[k] - lo[k]) / seg - 1e-9)) for k in range(3)]
    out = []
    for i in range(n[0]):
        for j in range(n[1]):
            for k in range(n[2]):
                idx = (i, j, k)
                a = [lo[q] + (hi[q] - lo[q]) * idx[q] / n[q] for q in range(3)]
                b = [lo[q] + (hi[q] - lo[q]) * (idx[q] + 1) / n[q] for q in range(3)]
                tag = "" if n == [1, 1, 1] else "_" + "".join(str(v + 1) for v, m in zip(idx, n) if m > 1)
                out.append(box(a, b, name + tag, part, tex))
    return out


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def unit(v):
    n = math.sqrt(sum(x * x for x in v))
    return [x / n for x in v]


def obox(c, ax, size, name, part, tex):
    """A box of `size` along the orthonormal world vectors `ax` (local x, y; z is x cross y), centred on c."""
    x, y = unit(ax[0]), unit(ax[1])
    z = cross(x, y)
    r = [[x[i], y[i], z[i]] for i in range(3)]
    return skin(El(name, list(size), list(c), r, {}, part), tex)


AX = {"x": 0, "y": 1, "z": 2}
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}   # a disc's angle 0 direction, and the in-plane direction it turns towards


def frame_of(axis):
    a = AX[axis]
    u, w = REF[axis]
    return a, u, w


def radial(axis, c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box from radius r0 to r1 about `axis` through c, a0..a1 along the axis, `width` across, at
    angle `ang` (radians, right-handed about +axis from the axis's reference direction)."""
    a, u, w = frame_of(axis)
    lo, hi = [0.0] * 3, [0.0] * 3
    lo[a], hi[a] = a0, a1
    lo[u], hi[u] = c[u] + r0, c[u] + r1
    lo[w], hi[w] = c[w] - width / 2, c[w] + width / 2
    el = box(lo, hi, name, part, tex)
    if abs(ang) > 1e-12:
        rotate([el], axis, math.degrees(ang), c)
    return el


def disc(axis, c, a0, a1, r, name, part, tex, k=4, phase=0.0):
    """A plain round part: k strips as long as the 2k-gon is across, 180/k degrees apart, each a hair
    shorter than the last so no two ends share a plane."""
    a, u, w = frame_of(axis)
    half = r * math.tan(math.pi / (2 * k))
    out = []
    st = min(0.012, (a1 - a0) / (4 * k))
    for i in range(k):
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[a], hi[a] = a0 + st * i, a1 - st * i
        lo[u], hi[u] = c[u] - r, c[u] + r
        lo[w], hi[w] = c[w] - half, c[w] + half
        el = box(lo, hi, f"{name}_{i + 1}" if k > 1 else name, part, tex)
        ang = phase + math.pi * i / k
        if abs(ang) > 1e-12:
            rotate([el], axis, math.degrees(ang), c)
        out.append(el)
    return out


def rods(axis, c, a0, a1, r, name, part, tex, k=4, seg=16.0):
    """A long round bar split into lengths of at most `seg`."""
    n = max(1, math.ceil((a1 - a0) / seg - 1e-9))
    out = []
    for i in range(n):
        b0, b1 = a0 + (a1 - a0) * i / n, a0 + (a1 - a0) * (i + 1) / n
        out += disc(axis, c, b0, b1, r, f"{name}{i + 1}" if n > 1 else name, part, tex, k=k)
    return out


def ring(axis, c, a0, a1, r_in, r_out, n, name, part, tex, odd=0, phase=None):
    """A tube of n boxes from r_in to r_out (an n-gon outside). Boxes overlap their neighbours and the next
    but one near the bore, so their ends step in by 0, 0.008, 0.016, 0.024 round the ring (no two that
    overlap end in one plane)."""
    w = 2 * r_out * math.tan(math.pi / n)
    first = math.pi / n if phase is None else phase
    out = []
    for i in range(n):
        s = 0.008 * ((i + 2 * odd) % 4)
        out.append(radial(axis, c, a0 + s, a1 - s, r_in, r_out, w, first + TAU * i / n, f"{name}{i + 1}", part, tex))
    return out


def channel(p0, p1, half_w, wall, height, name, part, tex, runs):
    """An open trough (a bottom and two sides) along p0 -> p1, the points on its bottom's top surface; it `runs`
    along "x" or "z" (it is level across)."""
    d = [p1[k] - p0[k] for k in range(3)]
    along = unit(d)
    across = [0.0, 0.0, 1.0] if runs == "x" else [1.0, 0.0, 0.0]
    up = cross(along, across)
    if up[1] < 0:
        up = [-v for v in up]
    length = math.sqrt(sum(v * v for v in d))
    mid = [(p0[k] + p1[k]) / 2 for k in range(3)]
    out = [obox([mid[k] - up[k] * wall / 2 for k in range(3)], (along, up), (length, wall, 2 * (half_w + wall)),
                f"{name}_bottom", part, tex)]
    side = cross(along, up)
    for tag, s in (("a", -1), ("b", 1)):
        c = [mid[k] + side[k] * s * (half_w + wall / 2) + up[k] * height / 2 for k in range(3)]
        out.append(obox(c, (along, up), (length - 0.02, height, wall), f"{name}_side{tag}", part, tex))
    return out


def segment_y(p0, p1, y0, y1, thick, name, part, tex):
    """A wall piece standing on the chord p0 -> p1 ((x, z) points), `thick` outward (to the chord's left
    seen from above, +y), y0..y1 high."""
    dx, dz = p1[0] - p0[0], p1[1] - p0[1]
    length = math.hypot(dx, dz)
    along = [dx / length, 0.0, dz / length]
    out_n = [along[2], 0.0, -along[0]]       # (dz, -dx): outward for a counter-clockwise chord seen from +y
    c = [(p0[0] + p1[0]) / 2 + out_n[0] * thick / 2, (y0 + y1) / 2, (p0[1] + p1[1]) / 2 + out_n[2] * thick / 2]
    return obox(c, (along, [0.0, 1.0, 0.0]), (length, y1 - y0, thick), name, part, tex)


def vault(x0, x1, cz, spring, span, rise, n, t, name, part, tex, seg=16.0):
    """A segmental barrel vault along x: n staves of thickness t on the arc through the springing points
    (cz -+ span/2, spring) and the crown (cz, spring + rise). Returns (staves, the arc's centre y, radius)."""
    half = span / 2
    r = (half * half + rise * rise) / (2 * rise)
    cy = spring + rise - r
    th0 = math.asin(half / r)
    d = 2 * th0 / n
    w = 2 * (r + t) * math.tan(d / 2) + 0.02
    pieces = max(1, math.ceil((x1 - x0) / seg - 1e-9))
    out = []
    for i in range(n):
        ang = -th0 + d * (i + 0.5)
        for j in range(pieces):
            a0 = x0 + (x1 - x0) * j / pieces
            a1 = x0 + (x1 - x0) * (j + 1) / pieces
            s = 0.006 * (i % 2)
            out.append(radial("x", (0.0, cy, cz), a0 + s, a1 - s, r, r + t, w, ang,
                              f"{name}{i + 1}" + (f"_{j + 1}" if pieces > 1 else ""), part, tex))
    return out, cy, r


def intrados(cy, r, cz, z):
    """The vault's underside's height at z."""
    return cy + math.sqrt(max(0.0, r * r - (z - cz) ** 2))


RING_PROUD = 0.3                             # a vault's arch ring stands this far proud of the gable under it


def gable(x0, x1, z0, z1, spring, cy, r, cz, n, name, part, tex):
    """An end wall under a vault: n strips from the springing to the vault's underside (each to its higher
    edge, into the staves, so no gap shows); set RING_PROUD back from the vault's end by its caller, so the
    staves' ends, not the strips' tops, show there."""
    out = []
    for i in range(n):
        a, b = z0 + (z1 - z0) * i / n, z0 + (z1 - z0) * (i + 1) / n
        top = max(intrados(cy, r, cz, a), intrados(cy, r, cz, b)) + 0.4
        out.append(box([x0, spring, a], [x1, top, b], f"{name}{i + 1}", part, tex))
    return out


def stepped_oval(c, y0, y1, a, b, name, part, tex):
    """An oval slab, a x b half axes (x, z), as three overlapping rectangles."""
    out = []
    for i, t in enumerate((15, 45, 75)):
        hx, hz = a * math.cos(math.radians(t)), b * math.sin(math.radians(t))
        out.append(box([c[0] - hx, y0, c[1] - hz], [c[0] + hx, y1, c[1] + hz], f"{name}{i + 1}", part, tex))
    return out


TIP, ROOT = 0.5 * 1.0, 0.625                  # addendum and dedendum, in modules
BODY_TIERS = 4
TOOTH_SHARE = 0.36                           # a tooth's share of the circular pitch at mid face: cast teeth, with backlash
BODY_GAP = 0.1                               # how far a body tier stays under the root cone


def bevel_cone(n, delta):
    """(outer cone distance, the root cone's radius and its distance from the apex along the axis, as functions
    of the distance along the pitch cone)."""
    a_o = GEAR_MOD * n / 2 / math.sin(delta)
    d = ROOT * GEAR_MOD

    def rad(a):
        return a * math.sin(delta) - d * math.cos(delta)

    def ax(a):
        return a * math.cos(delta) + d * math.sin(delta)
    return a_o, rad, ax


def bevel(name, part, tex, toward, ref, delta, n, phase):
    """A bevel gear on the cone with its apex at APEX: `toward` the unit vector from the gear along its axis to
    the apex, `ref` the (angle 0, angle 90) radial directions of its rotation axis (right-handed about it),
    `delta` the pitch cone's half angle, n teeth of module GEAR_MOD, tooth 1 at `phase`. The face runs
    GEAR_FACE in from the outer cone distance; each tooth stands from the root cone (a dedendum under the pitch
    cone) to its tip (an addendum over it). The body is BODY_TIERS discs stepping in under the root cone (each
    rim BODY_GAP under it), so the other gear's tips clear it; returns (elements, the body's back face's distance
    from the apex along the axis)."""
    a_o, rad, ax = bevel_cone(n, delta)
    a_mid = a_o - GEAR_FACE / 2
    e1, e2 = ref
    out = []
    pitch = TAU / n
    depth = (TIP + ROOT) * GEAR_MOD
    for i in range(n):
        ang = phase + pitch * i
        u = [math.cos(ang) * e1[k] + math.sin(ang) * e2[k] for k in range(3)]
        g = [-toward[k] * math.cos(delta) + u[k] * math.sin(delta) for k in range(3)]
        nrm = [toward[k] * math.sin(delta) + u[k] * math.cos(delta) for k in range(3)]
        c = [APEX[k] + g[k] * a_mid + nrm[k] * (TIP - ROOT) / 2 * GEAR_MOD for k in range(3)]
        thick = math.pi * GEAR_MOD * TOOTH_SHARE * a_mid / a_o
        out.append(obox(c, (g, nrm), (GEAR_FACE, depth, thick), f"{name}_tooth{i + 1:02d}", part, tex))
    axis = "x" if abs(toward[0]) > 0.5 else "y"
    k_ax = AX[axis]
    sign = toward[k_ax]
    corner = 1 / math.cos(math.pi / 12)      # a 12-gon's corners stand this much past its apothem
    r_out, r_in = rad(a_o), rad(a_o - GEAR_FACE)
    back = ax(a_o) + 0.8
    prev = back
    for j in range(BODY_TIERS):
        rho = r_out - (r_out - r_in) * j / (BODY_TIERS - 1)
        a_at = (rho * corner + ROOT * GEAR_MOD * math.cos(delta)) / math.sin(delta)
        top = ax(a_at) + BODY_GAP
        lo, hi = sorted((APEX[k_ax] - sign * prev, APEX[k_ax] - sign * top))
        out += disc(axis, APEX, lo, hi, rho, f"{name}_body{j + 1}", part, tex, k=6, phase=math.pi / 12)
        prev = top
    return out, back


DELTA_P = math.atan2(PINION_TEETH, WHEEL_TEETH)   # the pinion's pitch cone half angle (shaft angle 90 degrees)
DELTA_W = math.pi / 2 - DELTA_P
R_P = GEAR_MOD * PINION_TEETH / 2
R_W = GEAR_MOD * WHEEL_TEETH / 2


# ---------------------------------------------------------------- the frame
def build_frame():
    f = "frame"
    out = []
    for cx in range(CELLS_X):
        for cz in range(CELLS_Z):
            out.append(box([cx * B, 0.0, cz * B], [(cx + 1) * B, FLOOR, (cz + 1) * B], f"fr_floor_{cx}{cz}", f, "paving"))
    # the chimney stack, at the back between the cupellation furnace and the kettle
    out += tiles([48.0, FLOOR, 0.0], [64.0, 8.0, 16.0], "fr_stack_base", f, "brick")
    out += tiles([48.6, 8.0, 0.6], [63.4, 48.0, 15.4], "fr_stack_low", f, "brick")
    out += tiles([49.2, 48.0, 1.2], [62.8, 72.0, 14.8], "fr_stack_high", f, "brick")
    flue = (52.5, 59.5, 4.5, 11.5)
    out.append(box([flue[0], 72.0, flue[2]], [flue[1], 72.4, flue[3]], "fr_stack_soot", f, "soot"))
    for tag, (y0, y1, o) in (("cap", (72.0, 75.0, (49.2, 62.8, 1.2, 14.8))), ("corbel", (75.0, 77.5, (48.6, 63.4, 0.6, 15.4)))):
        x0, x1, z0, z1 = o
        tex = "firebrick" if tag == "corbel" else "brick"
        out += [box([x0, y0, z0], [x1, y1, flue[2]], f"fr_stack_{tag}n", f, tex),
                box([x0, y0, flue[3]], [x1, y1, z1], f"fr_stack_{tag}s", f, tex),
                box([x0, y0, flue[2]], [flue[0], y1, flue[3]], f"fr_stack_{tag}w", f, tex),
                box([flue[1], y0, flue[2]], [x1, y1, flue[3]], f"fr_stack_{tag}e", f, tex)]
    for i, (y, o) in enumerate(((30.0, (48.6, 63.4, 0.6, 15.4)), (58.0, (49.2, 62.8, 1.2, 14.8))), 1):
        x0, x1, z0, z1 = o
        p = 0.35
        out += [box([x0 - p, y, z0 - p], [x1 + p, y + 0.8, z0], f"fr_band{i}n", f, "iron"),
                box([x0 - p, y, z1], [x1 + p, y + 0.8, z1 + p], f"fr_band{i}s", f, "iron"),
                box([x0 - p, y, z0], [x0, y + 0.8, z1], f"fr_band{i}w", f, "iron"),
                box([x1, y, z0], [x1 + p, y + 0.8, z1], f"fr_band{i}e", f, "iron")]
    # four cast-iron columns under the line shaft, each with a plummer block
    y, z = LS
    for tag, x0 in COLUMNS:
        x1 = x0 + COL_W
        out.append(box([x0 - 0.3, FLOOR, z - 1.5], [x1 + 0.3, FLOOR + 0.8, z + 1.5], f"fr_{tag}_foot", f, "iron"))
        out += tiles([x0, FLOOR + 0.8, z - COL_W / 2], [x1, COL_TOP - 1.2, z + COL_W / 2], f"fr_{tag}_column", f, "iron")
        out.append(box([x0 - 0.3, COL_TOP - 1.2, z - 1.5], [x1 + 0.3, COL_TOP, z + 1.5], f"fr_{tag}_capital", f, "iron"))
        out.append(box([x0 - 0.2, COL_TOP, z - 2.6], [x1 + 0.2, BEARING_Y[0], z + 2.6], f"fr_{tag}_sole", f, "iron"))
        out.append(box([x0, BEARING_Y[0], BEARING_Z[0]], [x1, BEARING_Y[1], BEARING_Z[1]], f"fr_{tag}_bearing", f, "iron"))
        out.append(box([x0 + 0.2, BEARING_Y[1], BEARING_Z[0] + 0.4], [x1 - 0.2, BEARING_Y[1] + 0.8, BEARING_Z[1] - 0.4], f"fr_{tag}_cap", f, "iron"))
        for s, zz in (("n", z - 2.0), ("s", z + 2.0)):
            out.append(box([x0 + 0.5, BEARING_Y[0] - 0.01, zz - 0.4], [x1 - 0.5, BEARING_Y[0] + 1.6, zz + 0.4], f"fr_{tag}_bolt{s}", f, "steel"))
    return out


# ---------------------------------------------------------------- tier 2: the line shaft and the blowing engine
LS_C = (0.0, LS[0], LS[1])


def build_lineshaft():
    """The line shaft: the axle's oak continuation, a coupling, the iron shaft in four plummer blocks, collars
    either side of the west bearing, and the overhung crank (disc, boss, pin and nut) at its west end."""
    p = "lineshaft"
    y, z = LS
    out = [box([OAK[0], y - 0.6, z - 1.5], [OAK[1], y + 0.6, z + 1.5], "ls_oaka", p, "oak"),
           box([OAK[0], y - 1.5, z - 0.6], [OAK[1], y + 1.5, z + 0.6], "ls_oakb", p, "oak")]
    out += disc("x", LS_C, *FLANGE, 2.2, "ls_flange", p, "iron", k=4)
    out += rods("x", LS_C, LS_WEST, FLANGE[0], LS_R, "ls_shaft", p, "steel")
    s1 = COLUMNS[0][1]
    out += disc("x", LS_C, s1 - 0.4, s1, 1.6, "ls_collarw", p, "steel", k=4)
    out += disc("x", LS_C, s1 + COL_W, s1 + COL_W + 0.4, 1.6, "ls_collare", p, "steel", k=4)
    out += disc("x", LS_C, *CRANK_X, CRANK_DISC_R, "ls_crank", p, "iron", k=6, phase=math.pi / 12)
    out += disc("x", LS_C, CRANK_X[1], CRANK_X[1] + 1.2, 1.8, "ls_crankboss", p, "iron", k=4)
    pin = (0.0, y, z - CRANK_R)                  # at theta 0 the pin points north: the yoke at mid stroke
    out += disc("x", pin, *PIN_X, PIN_R, "ls_pin", p, "steel", k=4)
    out += disc("x", pin, PIN_X[0] - 0.4, PIN_X[0] + 0.2, 1.15, "ls_pinnut", p, "steel", k=2)
    return out


def build_yoke():
    """The slotted yoke (a Scotch yoke): the crank pin runs along its slot, so it rises and falls exactly with
    the pin's height; slippers either side run in the guides; the piston rod goes down into the tub."""
    p = "yoke"
    y, z = LS
    x0, x1 = YOKE_X
    zs0, zs1 = z - SLOT_L, z + SLOT_L
    out = [box([x0, y + SLOT_H, zs0 - YOKE_SIDE], [x1, y + SLOT_H + YOKE_BAR, zs1 + YOKE_SIDE], "yk_top", p, "iron"),
           box([x0, y - SLOT_H - YOKE_BAR, zs0 - YOKE_SIDE], [x1, y - SLOT_H, zs1 + YOKE_SIDE], "yk_bottom", p, "iron"),
           box([x0, y - SLOT_H, zs0 - YOKE_SIDE], [x1, y + SLOT_H, zs0], "yk_siden", p, "iron"),
           box([x0, y - SLOT_H, zs1], [x1, y + SLOT_H, zs1 + YOKE_SIDE], "yk_sides", p, "iron")]
    for tag, (a, b) in (("n", (zs0 - YOKE_SIDE - 0.6, zs0 - YOKE_SIDE)), ("s", (zs1 + YOKE_SIDE, zs1 + YOKE_SIDE + 0.6))):
        out.append(box([x0 + 0.2, y - SLOT_H - YOKE_BAR - 0.4, a], [x1 - 0.2, y + SLOT_H + YOKE_BAR + 0.4, b], f"yk_slipper{tag}", p, "steel"))
    cx = TUB_C[0]
    out += tiles([cx - 0.4, ROD_LOW, z - 0.4], [cx + 0.4, y - SLOT_H - YOKE_BAR, z + 0.4], "yk_rod", p, "steel")
    out += disc("y", (cx, 0.0, z), ROD_LOW - 0.6, ROD_LOW, 4.6, "yk_piston", p, "iron", k=6)
    return out


def guide_z():
    """The yoke's guides' channels (z): just outside its slippers."""
    z = LS[1]
    zs0, zs1 = z - SLOT_L - YOKE_SIDE - 0.6, z + SLOT_L + YOKE_SIDE + 0.6
    return (zs0 - 0.6, zs0), (zs1, zs1 + 0.6)


def build_tub():
    """The blowing tub: a cast-iron double-acting cylinder with its valve chests, a stuffing box for the rod,
    the yoke's guides on its cover, and the blast main round the back to the cupellation furnace's tuyere,
    with a blast valve and a stand."""
    p = "tub"
    cx, cz = TUB_C
    c = (cx, 0.0, cz)
    out = disc("y", c, FLOOR, TUB_Y[0], TUB_R + 0.6, "tb_footflange", p, "iron", k=6, phase=math.pi / 12)
    out += disc("y", c, TUB_Y[0], TUB_Y[1], TUB_R, "tb_barrel", p, "iron", k=6, phase=math.pi / 12)
    out += disc("y", c, TUB_Y[1], TUB_Y[1] + 1.2, TUB_R + 0.6, "tb_flange", p, "iron", k=6, phase=math.pi / 12)
    top = TUB_Y[1] + 1.2
    out += disc("y", c, top, top + 0.6, 4.2, "tb_cover", p, "iron", k=6, phase=math.pi / 12)
    out += disc("y", c, top + 0.6, top + 2.4, 1.3, "tb_gland", p, "steel", k=4)
    for tag, (y0, y1) in (("low", (4.2, 7.6)), ("high", (14.6, 18.0))):
        out.append(box([cx - TUB_R - 1.2, y0, cz - 3.0], [cx - TUB_R + 0.4, y1, cz + 3.0], f"tb_valve{tag}", p, "iron"))
    # the guides: a channel either side, a crossbar over the top, feet on the flange
    (g0, g1), (h0, h1) = guide_z()
    x0, x1 = YOKE_X[0] - 0.1, YOKE_X[1] + 0.1
    for tag, (a, b), lip in (("n", (g0, g1), (g1, g1 + 0.45)), ("s", (h0, h1), (h0 - 0.45, h0))):
        out += tiles([x0, top, a], [x1, GUIDE_TOP - 1.0, b], f"tb_guide{tag}", p, "iron")
        out.append(box([x0, top + 2.0, lip[0]], [x0 + 0.25, GUIDE_TOP - 1.0, lip[1]], f"tb_lip{tag}w", p, "iron"))
        out.append(box([x1 - 0.25, top + 2.0, lip[0]], [x1, GUIDE_TOP - 1.0, lip[1]], f"tb_lip{tag}e", p, "iron"))
        foot = (min(a, b, cz - 4.0), max(a, b, cz - 4.0)) if tag == "n" else (min(a, b, cz + 4.0), max(a, b, cz + 4.0))
        out.append(box([x0, top, foot[0]], [x1, top + 1.5, foot[1]], f"tb_foot{tag}", p, "iron"))
    out.append(box([x0, GUIDE_TOP - 1.0, g0], [x1, GUIDE_TOP, h1], "tb_crossbar", p, "iron"))
    # the blast main: north from the tub, east along the back, south into the back wall to the tuyere
    by = TUYERE[1]
    out += rods("z", (cx, by, 0.0), BLAST_Z, cz - TUB_R + 0.4, BLAST_R, "tb_mainn", p, "iron")
    out.append(box([cx - 1.5, by - 1.5, BLAST_Z - 1.5], [cx + 1.5, by + 1.5, BLAST_Z + 1.5], "tb_elbow1", p, "iron"))
    out += rods("x", (0.0, by, BLAST_Z), cx + 1.5, TUYERE[0] - 1.5, BLAST_R, "tb_maine", p, "iron")
    out.append(box([TUYERE[0] - 1.5, by - 1.5, BLAST_Z - 1.5], [TUYERE[0] + 1.5, by + 1.5, BLAST_Z + 1.5], "tb_elbow2", p, "iron"))
    out += disc("z", (TUYERE[0], by, 0.0), BLAST_Z + 1.5, CUP_Z[0] + 0.5, BLAST_R, "tb_mainin", p, "iron", k=4)
    # a blast valve on the back run, its spindle and wheel; a stand under the run
    vx = 25.0
    out.append(box([vx - 1.4, by - 1.9, BLAST_Z - 1.9], [vx + 1.4, by + 1.9, BLAST_Z + 1.9], "tb_valve", p, "iron"))
    out += disc("y", (vx, 0.0, BLAST_Z), by + 1.9, by + 4.2, 0.35, "tb_spindle", p, "steel", k=2)
    out += disc("y", (vx, 0.0, BLAST_Z), by + 4.2, by + 4.6, 1.6, "tb_wheel", p, "iron", k=4)
    sx = 20.5
    out.append(box([sx - 0.6, FLOOR, BLAST_Z - 0.8], [sx + 0.6, by - BLAST_R, BLAST_Z + 0.8], "tb_stand", p, "iron"))
    out.append(box([sx - 1.2, by - BLAST_R - 0.4, BLAST_Z - 1.4], [sx + 1.2, by - BLAST_R + 0.3, BLAST_Z + 1.4], "tb_saddle", p, "iron"))
    return out


# ---------------------------------------------------------------- tier 2: the cupellation furnace
def cup_vault():
    x0, x1 = CUP_X
    z0, z1 = CUP_Z[0] + CUP_WALL, CUP_Z[1] - CUP_WALL
    return vault(x0, x1, (z0 + z1) / 2, CUP_SPRING, z1 - z0, CUP_RISE, CUP_STAVES, CUP_T, "cp_vault", "cupel", "brick")


def ellipse_pt(c, a, t):
    return (c[0] + a[0] * math.cos(t), c[1] + a[1] * math.sin(t))


TEST_SEGS = 16
TEST_GAP = (3, 4)                            # the ring's segments left out at the front: the breast


def build_cupel():
    """The English cupellation furnace: a hearth block with the fireplace sunk in its west end (grate, coke,
    fire), a fire bridge, the test (an oval iron ring of bone ash, the molten lead on it) in the hearth, walls,
    a low segmental vault, the binding (buckstays and tie rods), the doors, the tuyere through the back wall,
    and at the front the breast: the litharge's channel out through the working opening into its pot."""
    p = "cupel"
    x0, x1 = CUP_X
    z0, z1 = CUP_Z
    w = CUP_WALL
    h = CUP_HEARTH
    fx0, fx1 = FIRE_X
    out = tiles([x0, FLOOR, z0], [x1, GRATE_Y, z1], "cp_hearth_low", p, "brick")
    out += tiles([x0, GRATE_Y, z0], [fx0, h, z1], "cp_hearth_w", p, "brick")
    out += tiles([fx1, GRATE_Y, z0], [x1, h, z1], "cp_hearth_e", p, "brick")
    out.append(box([fx0, GRATE_Y, z0], [fx1, h, z0 + w], "cp_hearth_n", p, "brick"))
    out.append(box([fx0, GRATE_Y, z1 - w], [fx1, h, z1], "cp_hearth_s", p, "brick"))
    # the fireplace: grate bars, a coke bed, the fire
    for i in range(6):
        gx = fx0 + 0.6 + i * (fx1 - fx0 - 1.2) / 5
        out.append(box([gx - 0.3, GRATE_Y, z0 + w], [gx + 0.3, GRATE_Y + 0.8, z1 - w], f"cp_grate{i + 1}", p, "iron"))
    out.append(box([fx0, GRATE_Y + 0.8, z0 + w], [fx1, h - 0.8, z1 - w], "cp_coke", p, "coke"))
    for i, (ex, ez) in enumerate(((22.6, 13.0), (25.4, 18.5), (23.0, 24.5))):
        out.append(box([ex - 1.3, h - 0.8, ez - 2.0], [ex + 1.3, h - 0.1, ez + 2.0], f"cp_fire{i + 1}", p, "ember"))
    out.append(box([BRIDGE_X[0], h, z0 + w], [BRIDGE_X[1], h + 4.0, z1 - w], "cp_bridge", p, "firebrick"))
    # walls to the springing; the front one round the working opening
    out += tiles([x0, h, z0], [x1, CUP_SPRING, z0 + w], "cp_wall_n", p, "brick")
    hx0, hx1 = FIRE_HOLE_X
    out.append(box([x0, h, z1 - w], [hx0, CUP_SPRING, z1], "cp_wall_sw", p, "brick"))
    out.append(box([hx0, h, z1 - w], [hx1, FIRE_HOLE_Y[0], z1], "cp_wall_firesill", p, "brick"))
    out.append(box([hx0, FIRE_HOLE_Y[1], z1 - w], [hx1, CUP_SPRING, z1], "cp_wall_firelintel", p, "brick"))
    out.append(box([hx1, h, z1 - w], [OPENING_X[0], CUP_SPRING, z1], "cp_wall_sm", p, "brick"))
    out.append(box([OPENING_X[1], h, z1 - w], [x1, CUP_SPRING, z1], "cp_wall_se", p, "brick"))
    out.append(box([OPENING_X[0], OPENING_TOP, z1 - w], [OPENING_X[1], CUP_SPRING, z1], "cp_lintel", p, "brick"))
    out.append(box([OPENING_X[0] - 0.3, OPENING_TOP, z1], [OPENING_X[1] + 0.3, OPENING_TOP + 0.8, z1 + 0.4], "cp_lintel_iron", p, "iron"))
    out.append(box([x0, h, z0 + w], [x0 + w, CUP_SPRING, z1 - w], "cp_wall_w", p, "brick"))
    out.append(box([x1 - w, h, z0 + w], [x1, CUP_SPRING, z1 - w], "cp_wall_e", p, "brick"))
    # the vault, the haunches over the side walls, the end walls' gables
    staves, cy, r = cup_vault()
    out += staves
    vz0, vz1 = z0 + w, z1 - w
    cz = (vz0 + vz1) / 2
    out += tiles([x0, CUP_SPRING, z0], [x1, CUP_SPRING + 1.6, z0 + w], "cp_haunch_n", p, "brick")
    out += tiles([x0, CUP_SPRING, z1 - w], [x1, CUP_SPRING + 1.6, z1], "cp_haunch_s", p, "brick")
    out += gable(x0 + RING_PROUD, x0 + w, vz0, vz1, CUP_SPRING, cy, r, cz, 6, "cp_gable_w", p, "brick")
    out += gable(x1 - w, x1 - RING_PROUD, vz0, vz1, CUP_SPRING, cy, r, cz, 6, "cp_gable_e", p, "brick")
    # the test: a ring of iron segments round the oval, open at the front (the breast); bone ash in it, the
    # molten lead on that
    ty0 = h - 1.2
    for i in range(TEST_SEGS):
        if i in TEST_GAP:
            continue
        t0, t1 = TAU * i / TEST_SEGS, TAU * (i + 1) / TEST_SEGS
        pa, pb = ellipse_pt(TEST_C, TEST_A, t0), ellipse_pt(TEST_C, TEST_A, t1)
        dx, dz = pb[0] - pa[0], pb[1] - pa[1]
        ln = math.hypot(dx, dz)
        ext = 0.35 / ln
        pa2 = (pa[0] - dx * ext, pa[1] - dz * ext)
        pb2 = (pb[0] + dx * ext, pb[1] + dz * ext)
        s = 0.02 * (i % 2)
        out.append(segment_y(pa2, pb2, ty0 + s, TEST_TOP - s, TEST_RING, f"cp_ring{i + 1:02d}", p, "iron"))
    out += stepped_oval(TEST_C, h, h + 0.7, TEST_A[0] - 0.05, TEST_A[1] - 0.05, "cp_boneash", p, "boneash")
    out += stepped_oval(TEST_C, h + 0.7, h + 0.9, TEST_A[0] - 1.4, TEST_A[1] - 1.9, "cp_lead", p, "lead")
    # the breast: an iron channel from the test's front through the opening, down into the litharge pot
    tz = TEST_C[1] + TEST_A[1] - 0.5
    out += channel((TEST_C[0], h + 0.6, tz), (TEST_C[0], h - 1.4, POT_L[1] - 1.5), 1.0, 0.3, 0.8, "cp_channel", p, "iron", "z")
    pc = (POT_L[0], 0.0, POT_L[1])
    out += disc("y", pc, FLOOR, 8.5, 3.3, "cp_pot", p, "iron", k=6, phase=math.pi / 12)
    out += disc("y", pc, 8.5, 8.9, 2.9, "cp_litharge", p, "litharge", k=6, phase=math.pi / 12)
    # the tuyere: a tapered iron nozzle through the back wall over the test's back edge
    tc = (TUYERE[0], TUYERE[1], 0.0)
    out += disc("z", tc, z0 + 0.5, z0 + w + 0.4, 1.0, "cp_tuyere", p, "iron", k=4)
    out += disc("z", tc, z0 + w + 0.4, TEST_C[1] - TEST_A[1] - 0.2, 0.7, "cp_nozzle", p, "iron", k=4)
    # the doors: the fire door and the ash pit's on the front, at the fireplace
    # the fire door stands open on its hinge at the hole's west edge, so the fire on the grate shows
    hy0, hy1 = FIRE_HOLE_Y
    door = [box([hx0, hy0 - 0.3, z1], [hx1 + 0.3, hy1 + 0.3, z1 + 0.5], "cp_firedoor", p, "iron"),
            box([hx1 - 0.6, (hy0 + hy1) / 2 - 0.4, z1 + 0.5], [hx1 + 0.2, (hy0 + hy1) / 2 + 0.4, z1 + 1.1], "cp_firedoor_latch", p, "iron")]
    rotate(door, "y", -FIRE_DOOR_OPEN, (hx0 - 0.3, 0.0, z1 + 0.25))
    out += door
    for i, yy in enumerate((hy0 + 0.6, hy1 - 1.2), 1):
        out.append(box([hx0 - 0.9, yy, z1], [hx0 - 0.1, yy + 0.6, z1 + 0.5], f"cp_firehinge{i}", p, "iron"))
    out.append(box([fx0 + 1.3, FLOOR + 1.0, z1], [fx1 - 1.3, FLOOR + 5.0, z1 + 0.4], "cp_ashdoor", p, "iron"))
    # the binding: buckstays on the long faces, tie rods over the vault, nuts
    top = cy + r + CUP_T + 0.6
    for i, bx in enumerate((18.8, 30.0, 44.3), 1):
        out += [box([bx - 0.5, FLOOR, z0 - 0.6], [bx + 0.5, top + 0.8, z0], f"cp_buck{i}n", p, "iron"),
                box([bx - 0.5, FLOOR, z1], [bx + 0.5, top + 0.8, z1 + 0.6], f"cp_buck{i}s", p, "iron"),
                box([bx - 0.3, top, z0 - 0.6], [bx + 0.3, top + 0.6, z1 + 0.6], f"cp_tie{i}", p, "iron"),
                box([bx - 0.45, top - 0.15, z0 - 1.1], [bx + 0.45, top + 0.75, z0 - 0.6], f"cp_nut{i}n", p, "iron"),
                box([bx - 0.45, top - 0.15, z1 + 0.6], [bx + 0.45, top + 0.75, z1 + 1.1], f"cp_nut{i}s", p, "iron")]
    return out


# ---------------------------------------------------------------- tier 2: the liquation furnace
def plate_y(x):
    (xa, ya), (xb, yb) = PLATE
    return ya + (x - xa) * (yb - ya) / (xb - xa)


PLATE_ANGLE = math.atan2(PLATE[1][1] - PLATE[0][1], PLATE[1][0] - PLATE[0][0])


def liq_vault():
    x0, x1 = LIQ_X
    z0, z1 = LIQ_Z[0] + LIQ_WALL, LIQ_Z[1] - LIQ_WALL
    return vault(x0, x1, (z0 + z1) / 2, LIQ_SPRING, z1 - z0, LIQ_RISE, LIQ_STAVES, LIQ_T, "lq_vault", "liquation", "brick")


def on_plate(x, lift, along, thick, half_z, zc, name, part, tex):
    """A slab lying on the hearth plate: centred `along` voxels from x along the slope, `lift` over its top."""
    a = PLATE_ANGLE
    g = [math.cos(a), math.sin(a), 0.0]
    up = [-math.sin(a), math.cos(a), 0.0]
    base = (x, plate_y(x), zc)
    c = [base[0] + up[0] * (lift + thick / 2), base[1] + up[1] * (lift + thick / 2), zc]
    return obox(c, (g, up), (along, thick, 2 * half_z), name, part, tex)


def build_liquation():
    """The liquation furnace: a brick base with its firebox, an inclined cast-iron hearth plate on brick steps,
    raised rims, a low vault over it open at the low end, the cakes on the slope and dross, a spout out of the open
    end into the receiving pot, the binding and the fire door; built along x, then turned onto its corner."""
    p = "liquation"
    x0, x1 = LIQ_X
    z0, z1 = LIQ_Z
    w = LIQ_WALL
    out = tiles([x0, FLOOR, z0], [x1, LIQ_BASE, z1], "lq_base", p, "brick")
    out += tiles([x0, LIQ_BASE, z0], [x1, LIQ_SPRING, z0 + w], "lq_wall_n", p, "brick")
    out += tiles([x0, LIQ_BASE, z1 - w], [x1, LIQ_SPRING, z1], "lq_wall_s", p, "brick")
    end = PLATE[1][0]
    out.append(box([end, LIQ_BASE, z0 + w], [x1, LIQ_SPRING, z1 - w], "lq_wall_e", p, "brick"))
    staves, cy, r = liq_vault()
    out += staves
    vz0, vz1 = z0 + w, z1 - w
    cz = (vz0 + vz1) / 2
    out += tiles([x0, LIQ_SPRING, z0], [x1, LIQ_SPRING + 1.2, z0 + w], "lq_haunch_n", p, "brick")
    out += tiles([x0, LIQ_SPRING, z1 - w], [x1, LIQ_SPRING + 1.2, z1], "lq_haunch_s", p, "brick")
    out += gable(end, x1 - RING_PROUD, vz0, vz1, LIQ_SPRING, cy, r, cz, 4, "lq_gable", p, "brick")
    # the steps under the plate, the plate and its rims
    pz0, pz1 = vz0 + 0.2, vz1 - 0.2
    (xa, _), (xb, _) = PLATE
    dy = PLATE_T / math.cos(PLATE_ANGLE)
    prev = LIQ_BASE
    for i, sx in enumerate((27.0, 31.0, 35.0, 38.5), 1):
        top = plate_y(sx) - dy
        out.append(box([sx, prev, pz0], [end, top, pz1], f"lq_step{i}", p, "firebrick"))
        prev = top
    along = math.hypot(xb - xa, PLATE[1][1] - PLATE[0][1])
    mid = (xa + xb) / 2
    zc = (pz0 + pz1) / 2
    out.append(on_plate(mid, -PLATE_T, along, PLATE_T, (pz1 - pz0) / 2, zc, "lq_plate", p, "iron"))
    for tag, z in (("n", pz0 + 0.3), ("s", pz1 - 0.3)):
        out.append(on_plate(mid, 0.0, along - 0.02, 1.0, 0.3, z, f"lq_rim{tag}", p, "iron"))
    # the cakes on the slope, dross at the low end
    for i, cx in enumerate((29.0, 33.3, 37.6), 1):
        out.append(on_plate(cx, 0.0, 4.0, 1.6, 3.0, zc, f"lq_cake{i}", p, "tin"))
    out.append(on_plate(25.8, 0.0, 1.8, 0.6, 2.2, zc + 0.8, "lq_dross", p, "dross"))
    # the spout out of the open end into the receiving pot
    out += channel((xa + 0.6, plate_y(xa + 0.6) + 0.05, zc), (POT_Q[0] + 2.0, plate_y(xa) - 1.1, zc), 1.0, 0.3, 0.8, "lq_spout", p, "iron", "x")
    pc = (POT_Q[0], 0.0, POT_Q[1])
    out += disc("y", pc, FLOOR, 8.2, 3.2, "lq_pot", p, "iron", k=6, phase=math.pi / 12)
    out += disc("y", pc, 8.2, 8.5, 2.8, "lq_potmetal", p, "tin", k=6, phase=math.pi / 12)
    # the fire door on the front; the binding
    out.append(box([28.0, FLOOR + 1.0, z1], [33.6, FLOOR + 6.5, z1 + 0.5], "lq_firedoor", p, "iron"))
    out.append(box([32.4, FLOOR + 3.4, z1 + 0.5], [33.2, FLOOR + 4.2, z1 + 1.1], "lq_firedoor_latch", p, "iron"))
    top = cy + r + LIQ_T + 0.5
    for i, bx in enumerate((25.0, 34.6, 43.0), 1):
        out += [box([bx - 0.5, FLOOR, z0 - 0.6], [bx + 0.5, top + 0.8, z0], f"lq_buck{i}n", p, "iron"),
                box([bx - 0.5, FLOOR, z1], [bx + 0.5, top + 0.8, z1 + 0.6], f"lq_buck{i}s", p, "iron"),
                box([bx - 0.3, top, z0 - 0.6], [bx + 0.3, top + 0.6, z1 + 0.6], f"lq_tie{i}", p, "iron"),
                box([bx - 0.45, top - 0.15, z0 - 1.1], [bx + 0.45, top + 0.75, z0 - 0.6], f"lq_nut{i}n", p, "iron"),
                box([bx - 0.45, top - 0.15, z1 + 0.6], [bx + 0.45, top + 0.75, z1 + 1.1], f"lq_nut{i}s", p, "iron")]
    pv = (LIQ_PIVOT[0], 0.0, LIQ_PIVOT[1])
    rotate(out, "y", 90.0, pv)
    translate(out, [LIQ_AT[0] - LIQ_PIVOT[0], 0.0, LIQ_AT[1] - LIQ_PIVOT[1]])
    return out


# ---------------------------------------------------------------- tier 3: the acid parting vessel and the tub
def build_acid():
    """The parting vessel: brick piers either side of a firebox, a cast-iron tray of sand on it, the stoneware
    vessel set in the sand with the aqua fortis in it, and a brick hood over all, its back wall against the
    stack, which takes the fumes; an iron apron along the hood's front."""
    p = "acid"
    x0, x1 = AC_X
    fx0, fx1 = x0 + AC_PIER, x1 - AC_PIER
    zb0, zb1 = AC_BACK
    zf = AC_FRONT
    out = tiles([x0, FLOOR, zb0], [x1, HOOD_Y[0], zb1], "ac_back", p, "brick")
    out += tiles([x0, FLOOR, zb1], [fx0, HOOD_Y[0], zf + 0.5], "ac_pierw", p, "brick")
    out += tiles([fx1, FLOOR, zb1], [x1, HOOD_Y[0], zf + 0.5], "ac_piere", p, "brick")
    out.append(box([x0, HOOD_Y[0], zb0], [x1, HOOD_Y[1], zf + 0.5], "ac_hood", p, "brick"))
    out.append(box([fx0, HOOD_Y[0] - 2.5, zf], [fx1, HOOD_Y[0], zf + 0.5], "ac_apron", p, "iron"))
    out.append(box([fx0, FLOOR, zb1], [fx1, TRAY_Y[0], zf], "ac_firebox", p, "brick"))
    out.append(box([fx0 + 2.5, FLOOR + 1.5, zf], [fx1 - 2.5, FLOOR + 6.5, zf + 0.5], "ac_firedoor", p, "iron"))
    ty0, ty1 = TRAY_Y
    out.append(box([fx0, ty0, zb1], [fx1, ty0 + 0.6, zf], "ac_tray", p, "iron"))
    rim = 0.5
    out += [box([fx0, ty0 + 0.6, zb1], [fx1, ty1, zb1 + rim], "ac_trayn", p, "iron"),
            box([fx0, ty0 + 0.6, zf - rim], [fx1, ty1, zf], "ac_trays", p, "iron"),
            box([fx0, ty0 + 0.6, zb1 + rim], [fx0 + rim, ty1, zf - rim], "ac_trayw", p, "iron"),
            box([fx1 - rim, ty0 + 0.6, zb1 + rim], [fx1, ty1, zf - rim], "ac_traye", p, "iron")]
    out.append(box([fx0 + rim, ty0 + 0.6, zb1 + rim], [fx1 - rim, ty1 - 0.4, zf - rim], "ac_sand", p, "sand"))
    vc = (VESSEL_C[0], 0.0, VESSEL_C[1])
    out += ring("y", vc, VESSEL_Y[0], VESSEL_Y[1], VESSEL_R[0], VESSEL_R[1], 12, "ac_vessel", p, "stoneware")
    out += disc("y", vc, VESSEL_Y[0], VESSEL_Y[0] + 0.7, VESSEL_R[0] + 0.05, "ac_vesselfoot", p, "stoneware", k=6, phase=math.pi / 12)
    out += ring("y", vc, VESSEL_Y[1] - 0.6, VESSEL_Y[1] + 0.4, VESSEL_R[0] + 0.1, VESSEL_R[1] + 0.45, 12, "ac_lip", p, "stoneware", odd=1)
    out += disc("y", vc, VESSEL_Y[1] - 2.6, VESSEL_Y[1] - 2.3, VESSEL_R[0] + 0.05, "ac_acid", p, "acid", k=6, phase=math.pi / 12)
    return out


def build_precip():
    """The precipitating tub: oak staves in two iron hoops, the copper nitrate solution, two oak bars across its
    top, each with two copper plates hung in the solution."""
    p = "precip"
    tc = (TUB2_C[0], 0.0, TUB2_C[1])
    y0, y1 = TUB2_Y
    out = ring("y", tc, y0, y1, TUB2_R[0], TUB2_R[1], 12, "pr_stave", p, "planks")
    out += disc("y", tc, y0, y0 + 0.8, TUB2_R[0] + 0.05, "pr_bottom", p, "planks", k=6, phase=math.pi / 12)
    for i, hy in enumerate((3.6, 9.4), 1):
        out += ring("y", tc, hy, hy + 1.0, TUB2_R[1] - 0.05, TUB2_R[1] + 0.35, 12, f"pr_hoop{i}_", p, "iron", odd=1)
    out += disc("y", tc, y1 - 1.8, y1 - 1.5, TUB2_R[0] + 0.05, "pr_solution", p, "solution", k=6, phase=math.pi / 12)
    cx, cz = TUB2_C
    for i, bz in enumerate((cz - 1.8, cz + 1.8), 1):
        out.append(box([cx - TUB2_R[1] - 0.6, y1, bz - 0.5], [cx + TUB2_R[1] + 0.6, y1 + 1.0, bz + 0.5], f"pr_bar{i}", p, "oak"))
        for j, px in enumerate((cx - 2.3, cx + 2.3), 1):
            out.append(box([px - 1.5, 4.5, bz - 0.15], [px + 1.5, y1 + 0.5, bz + 0.15], f"pr_plate{i}{j}", p, "copper"))
    return out


# ---------------------------------------------------------------- tier 4: the Parkes kettle, its stirrer, the chutes
KC = (KET_C[0], 0.0, KET_C[1])


def build_kettle():
    """The Parkes kettle: a round brick setting (its firebox inside, the fire and ash-pit doors on its front),
    the cast-iron kettle's rim and flange showing above it, the molten lead in it and the zinc crust rising."""
    p = "kettle"
    out = disc("y", KC, FLOOR, 13.0, SET_R, "kt_setting", p, "brick", k=8)
    out += ring("y", KC, 13.0, SET_TOP, KET_R[1], SET_R, 16, "kt_settop", p, "brick", phase=0.0)
    out += ring("y", KC, 18.0, 21.0, KET_R[0] - 0.8, KET_R[1] - 0.6, 16, "kt_bowl", p, "iron", odd=1, phase=0.0)
    out += ring("y", KC, 21.0, SET_TOP, KET_R[0], KET_R[1], 16, "kt_wall", p, "iron", odd=1, phase=0.0)
    out += ring("y", KC, SET_TOP, SET_TOP + 0.8, KET_R[0], KET_FLANGE, 16, "kt_flange", p, "iron", phase=0.0)
    out += disc("y", KC, BATH_Y, BATH_Y + 0.3, KET_R[0] + 0.05, "kt_bath", p, "lead", k=8)
    for i, (ang, rr, sz) in enumerate(((0.7, 6.8, (3.4, 2.2)), (2.6, 7.0, (2.6, 3.0)), (4.4, 6.6, (3.0, 2.4))), 1):
        cx, cz = KET_C[0] + rr * math.sin(ang), KET_C[1] + rr * math.cos(ang)
        el = box([cx - sz[0] / 2, BATH_Y + 0.3, cz - sz[1] / 2], [cx + sz[0] / 2, BATH_Y + 0.6, cz + sz[1] / 2], f"kt_crust{i}", p, "zinc")
        rotate([el], "y", math.degrees(ang), (cx, 0.0, cz))
        out.append(el)
    # the skimmer, a perforated ladle for taking off the crust, lying across the flange at the front left
    ang = math.radians(SKIM_ANGLE)
    u = (-math.sin(ang), math.cos(ang))      # outward, towards the front left
    bc = (KET_C[0] + u[0] * (KET_R[0] + 1.2), KET_C[1] + u[1] * (KET_R[0] + 1.2))
    out += disc("y", (bc[0], 0.0, bc[1]), SET_TOP + 0.8, SET_TOP + 1.2, 1.8, "kt_skimmer", p, "iron", k=4)
    h0 = (bc[0] + u[0] * 1.6, SET_TOP + 1.0, bc[1] + u[1] * 1.6)
    h1 = (bc[0] + u[0] * 9.0, SET_TOP + 1.0, bc[1] + u[1] * 9.0)
    el = box([0.0, h0[1] - 0.2, -0.25], [math.dist((h0[0], h0[2]), (h1[0], h1[2])), h0[1] + 0.2, 0.25], "kt_skimhandle", p, "iron")
    rotate([el], "y", -math.degrees(math.atan2(u[1], u[0])), (0.0, 0.0, 0.0))
    translate([el], [h0[0], 0.0, h0[2]])
    out.append(el)
    zf = KET_C[1] + SET_R
    out.append(box([KET_C[0] - 2.0, FLOOR + 3.0, zf], [KET_C[0] + 2.0, FLOOR + 8.5, zf + 0.5], "kt_firedoor", p, "iron"))
    out.append(box([KET_C[0] + 0.9, FLOOR + 5.4, zf + 0.5], [KET_C[0] + 1.7, FLOOR + 6.2, zf + 1.1], "kt_firedoor_latch", p, "iron"))
    out.append(box([KET_C[0] - 1.6, FLOOR + 0.3, zf], [KET_C[0] + 1.6, FLOOR + 2.4, zf + 0.4], "kt_ashdoor", p, "iron"))
    return out


def build_gallows():
    """The stirrer's gallows: two cast-iron standards bolted to the kettle's flange, an upper and a lower
    crossbeam between them, each with a bearing boss round the stirrer's shaft."""
    p = "gallows"
    z = KET_C[1]
    out = []
    a0, a1, b0, b1 = GAL_X
    for tag, (x0, x1) in (("w", (a0, a1)), ("e", (b0, b1))):
        out.append(box([x0, SET_TOP + 0.8, z - 0.8], [x1, GAL_UP[1], z + 0.8], f"gl_standard{tag}", p, "iron"))
        out.append(box([x0 - 0.7, SET_TOP + 0.8, z - 1.5], [x1 + 0.7, SET_TOP + 1.4, z + 1.5], f"gl_foot{tag}", p, "iron"))
    out += tiles([a0, GAL_UP[0], z - 1.2], [b1, GAL_UP[1], z + 1.2], "gl_upper", p, "iron")
    out += tiles([a0, GAL_LOW[0], z - 1.0], [b1, GAL_LOW[1], z + 1.0], "gl_lower", p, "iron")
    sc = (KET_C[0], 0.0, z)
    out.append(box([sc[0] - 1.6, GAL_UP[1], z - 1.6], [sc[0] + 1.6, GAL_UP[1] + 1.0, z + 1.6], "gl_bossup", p, "iron"))
    out.append(box([sc[0] - 1.4, GAL_LOW[0] - 0.4, z - 1.4], [sc[0] + 1.4, GAL_LOW[1] + 0.4, z + 1.4], "gl_bosslow", p, "iron"))
    return out




def build_stirrer():
    """The stirrer: a vertical steel shaft in the gallows' two bearings, a collar under the lower one, two
    paddles crossing in the bath (under its surface), and the bevel wheel at its top, under the line shaft."""
    p = "stirrer"
    sc = (KET_C[0], 0.0, KET_C[1])
    out = rods("y", sc, 17.0, STIR_TOP, 0.8, "st_shaft", p, "steel")
    out += disc("y", sc, GAL_LOW[0] - 1.2, GAL_LOW[0] - 0.4, 1.3, "st_collar", p, "steel", k=4)
    for i in range(2):
        out.append(radial("y", sc, 17.5, 21.0, -5.5, 5.5, 0.4, math.pi / 2 * i + math.pi / 4, f"st_paddle{i + 1}", p, "iron"))
    # the wheel: its tooth 1 half a pitch past +x, so a gap faces the pinion (at +x) at theta 0
    e1, e2 = [0.0, 0.0, 1.0], [1.0, 0.0, 0.0]
    wheel, back = bevel("st_wheel", p, "steel", [0.0, 1.0, 0.0], (e1, e2), DELTA_W, WHEEL_TEETH, math.pi / 2 + math.pi / WHEEL_TEETH)
    out += wheel
    out += disc("y", sc, GAL_UP[1] + 1.0, APEX[1] - back + 0.3, 1.8, "st_hub", p, "steel", k=4)
    return out


def build_pinion():
    """The bevel pinion, keyed to the line shaft over the kettle, east of the stirrer: its tooth 1 down, into the
    wheel's gap at theta 0."""
    p = "bevelpinion"
    e1, e2 = [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]
    out, back = bevel("bp", p, "steel", [-1.0, 0.0, 0.0], (e1, e2), DELTA_P, PINION_TEETH, math.pi)
    x0 = APEX[0] + back - 0.3
    out += disc("x", LS_C, x0, x0 + 1.7, 1.8, "bp_hub", p, "steel", k=4)
    return out


CHUTE_WALL, CHUTE_SIDE = 0.4, 1.6             # a chute's plate, and how high its sides stand over its bottom


def chute_y(top, low, z):
    return top[1] + (z - top[0]) * (low[1] - top[1]) / (low[0] - top[0])


def _slope_sin(top, low):
    return (top[1] - low[1]) / math.hypot(low[0] - top[0], top[1] - low[1])


def _onto_face(top, low, z_face, upper):
    """The chute's end moved along its line so its outermost corner is on the face z_face: the bottom plate's
    underside pokes out at the upper end, the sides' tops at the lower end (both lean along the slope)."""
    for _ in range(8):
        s = _slope_sin(top, low)
        if upper:
            z = z_face + CHUTE_WALL * s
            top = (z, chute_y(top, low, z))
        else:
            z = z_face - CHUTE_SIDE * s
            low = (z, chute_y(top, low, z))
    return top, low


IN_X = 83.0                                  # the input chute's centre line (x)
IN_TOP, IN_LOW = _onto_face((0.0, 30.0), (16.6, 26.2), 0.0, True)   # (z, y) of its bottom's top at the face, at its lower end
OUT_X = SIPHON[0]
OUT_TOP, OUT_LOW = _onto_face((36.8, 14.1), (CELLS_Z * B, 6.6), CELLS_Z * B, False)   # the output chute's: the head, the face


def build_chutes():
    """Tier 4 makes the kettle chute-fed: an inclined chute in through the back (its mouth on the input cell's
    north face) drops the charge into the kettle; a siphon over the kettle's front rim runs the lead into a head
    box and an inclined chute out to the output cell's south face. Iron legs carry both chutes."""
    p = "chutes"
    out = channel((IN_X, IN_TOP[1], IN_TOP[0]), (IN_X, IN_LOW[1], IN_LOW[0]), 2.0, CHUTE_WALL, CHUTE_SIDE, "ch_in", p, "iron", "z")
    m0, m1 = IN_TOP[1] - 0.6, IN_TOP[1] + 2.4
    out += [box([IN_X - 2.4, m0, 0.0], [IN_X + 2.4, m0 + 0.5, 1.2], "ch_inmouth_low", p, "iron"),
            box([IN_X - 2.4, m1 - 0.5, 0.0], [IN_X + 2.4, m1, 1.2], "ch_inmouth_top", p, "iron"),
            box([IN_X - 2.4, m0 + 0.5, 0.0], [IN_X - 1.9, m1 - 0.5, 1.2], "ch_inmouth_w", p, "iron"),
            box([IN_X + 1.9, m0 + 0.5, 0.0], [IN_X + 2.4, m1 - 0.5, 1.2], "ch_inmouth_e", p, "iron")]
    for i, lz in enumerate((3.0, 10.0), 1):
        top = chute_y(IN_TOP, IN_LOW, lz) - 0.3
        out.append(box([IN_X - 0.4, FLOOR, lz - 0.4], [IN_X + 0.4, top, lz + 0.4], f"ch_inleg{i}", p, "iron"))
    # the siphon: down into the bath, over the rim, down in front of the setting into the head box
    sx, sz = SIPHON
    pr = 0.6
    sc = (sx, 0.0, sz)
    out += disc("y", sc, 17.5, SIPHON_TOP - 0.7, pr, "ch_siphonin", p, "iron", k=4)
    out.append(box([sx - 0.8, SIPHON_TOP - 0.7, sz - 0.8], [sx + 0.8, SIPHON_TOP + 0.7, sz + 0.8], "ch_siphonbend1", p, "iron"))
    out += disc("z", (sx, SIPHON_TOP, 0.0), sz + 0.8, SIPHON_OUT - 0.8, pr, "ch_siphontop", p, "iron", k=4)
    out.append(box([sx - 0.8, SIPHON_TOP - 0.7, SIPHON_OUT - 0.8], [sx + 0.8, SIPHON_TOP + 0.7, SIPHON_OUT + 0.8], "ch_siphonbend2", p, "iron"))
    head_top = OUT_TOP[1] + 2.6
    out += disc("y", (sx, 0.0, SIPHON_OUT), OUT_TOP[1] + 1.0, SIPHON_TOP - 0.7, pr, "ch_siphonout", p, "iron", k=4)
    hz0, hz1 = OUT_TOP[0] - 0.4, SIPHON_OUT + 1.8
    out += [box([sx - 2.4, OUT_TOP[1] - 0.6, hz0], [sx + 2.4, head_top, OUT_TOP[0]], "ch_head_n", p, "iron"),
            box([sx - 2.4, OUT_TOP[1] + 0.2, OUT_TOP[0]], [sx - 2.0, head_top, hz1], "ch_head_w", p, "iron"),
            box([sx + 2.0, OUT_TOP[1] + 0.2, OUT_TOP[0]], [sx + 2.4, head_top, hz1], "ch_head_e", p, "iron")]
    out += channel((OUT_X, OUT_TOP[1], OUT_TOP[0]), (OUT_X, OUT_LOW[1], OUT_LOW[0]), 2.0, CHUTE_WALL, CHUTE_SIDE, "ch_out", p, "iron", "z")
    for i, lz in enumerate((OUT_TOP[0] + 2.0, 50.0, 60.0), 1):
        top = chute_y(OUT_TOP, OUT_LOW, lz) - 0.3
        out.append(box([OUT_X - 0.4, FLOOR, lz - 0.4], [OUT_X + 0.4, top, lz + 0.4], f"ch_outleg{i}", p, "iron"))
    return out


def build():
    els = build_lineshaft() + build_yoke() + build_tub() + build_cupel() + build_liquation()
    els += build_acid() + build_precip() + build_kettle() + build_gallows() + build_stirrer() + build_pinion()
    els += build_chutes() + build_frame()
    return els


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


TIERS = {
    "2": ["t2blast", "t2cupel", "t2liquation"],
    "3": ["t3acid", "t3precip"],
    "4": ["t4kettle", "t4stirrer", "t4chutes"],
}
REQUIRES = [v for k in sorted(TIERS) for v in TIERS[k]]

_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    shaft = {"type": "rotate", "axis": "x", "pivot": pt(0.0, *LS), "ratio": 1.0}
    parts = [
        {"id": "lineshaft", "match": ["ls_*"], "requires": "t2blast", "drivers": [dict(shaft)]},
        # the crank pin is north of the shaft at theta 0, so its height is y + r sin(theta): the yoke rides it
        {"id": "yoke", "match": ["yk_*"], "requires": "t2blast",
         "drivers": [{"type": "slide", "axis": "y", "amplitude": r6(CRANK_R / B), "ratio": 1.0, "phase": 0.0}]},
        {"id": "tub", "match": ["tb_*"], "requires": "t2blast", "drivers": []},
        {"id": "cupel", "match": ["cp_*"], "requires": "t2cupel", "drivers": []},
        {"id": "liquation", "match": ["lq_*"], "requires": "t2liquation", "drivers": []},
        {"id": "acid", "match": ["ac_*"], "requires": "t3acid", "drivers": []},
        {"id": "precip", "match": ["pr_*"], "requires": "t3precip", "drivers": []},
        {"id": "kettle", "match": ["kt_*"], "requires": "t4kettle", "drivers": []},
        {"id": "gallows", "match": ["gl_*"], "requires": "t4stirrer", "drivers": []},
        {"id": "stirrer", "match": ["st_*"], "requires": "t4stirrer",
         "drivers": [{"type": "rotate", "axis": "y", "pivot": pt(KET_C[0], 0.0, KET_C[1]), "ratio": r6(STIR_RATIO)}]},
        {"id": "bevelpinion", "match": ["bp_*"], "requires": "t4stirrer", "drivers": [dict(shaft)]},
        {"id": "chutes", "match": ["ch_*"], "requires": "t4chutes", "drivers": []},
        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


MOVING = ("lineshaft", "yoke", "stirrer", "bevelpinion")


# ---------------------------------------------------------------- poses
REST = (0.0, 0.0)                            # (theta, travel): the authored pose


def inputs_of(pose):
    th = pose[0]
    ps = pose[1] if len(pose) > 1 else abs(th)
    return {"theta": th, "travel": ps}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


def cycle_poses(n):
    """n poses over the rig's cycle: two turns of the shaft (the stirrer turns once)."""
    return [(2 * TAU * i / n, 2 * TAU * i / n) for i in range(n)]


# ---------------------------------------------------------------- the rig file
def footprint(els, parts):
    """Every cell an element reaches into at rest (the cells the machine occupies, its tier 4 size)."""
    cells = set()
    for el in els:
        lo, hi = posed(el, pm(parts, el.part, REST)).aabb()
        cells.update(cells_touched(lo, hi))
    return sorted(cells, key=lambda c: (c[1], c[2], c[0]))


ANCHORS = ("input", "output")


def anchor_points():
    return {"input": (IN_X, IN_TOP[1] + 0.6, 0.0), "output": (OUT_X, chute_y(OUT_TOP, OUT_LOW, CELLS_Z * B) + 0.6, CELLS_Z * B)}


def make_rig(parts):
    pts = anchor_points()
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the middle of the front "
                    "(south) row at ground level, nearest the player who placed it; the works run 6 wide (x) and 4 deep (z), "
                    "the chimney stack at the back. The rig reads theta alone (the axle's angle): the line shaft and the bevel "
                    "pinion turn with it, the blowing tub's yoke rises and falls with the crank, the stirrer turns at half its "
                    "speed. requires: one value per fitted set, its tier first (tiers lists them); the frame is the paved "
                    "floor, the stack and the line shaft's columns. See the parting furnace's README for the schema.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "inputCell": list(INPUT_CELL),
        "inputFace": INPUT_FACE,
        "outputCell": list(OUTPUT_CELL),
        "outputFace": OUTPUT_FACE,
        "input": {"pos": pt(*pts["input"])},
        "output": {"pos": pt(*pts["output"])},
        "tiers": {k: list(v) for k, v in TIERS.items()},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0]."""
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    db = [v / B for v in d]
    ship_els = copy.deepcopy(els)
    translate(ship_els, d)
    ship_parts = copy.deepcopy(parts)
    for p in ship_parts:
        for drv in p["drivers"]:
            for key in ("pivot", "anchor"):
                if key in drv:
                    drv[key] = [r6(drv[key][k] + db[k]) for k in range(3)]
    ship = copy.deepcopy(rig)
    ship["cells"] = [{**c, "pos": shift_cell(c["pos"], ORIGIN_CELL)} for c in rig["cells"]]
    for key in ("powerCell", "inputCell", "outputCell"):
        ship[key] = shift_cell(rig[key], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def no_lid(x, z):
    """No column gets a lid yet: the works' tops are at many heights (open floor, pots, roofs, the stack), and
    where a player walks on them is the gameplay's to decide (README, open questions)."""
    return False


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig (every reader rebuilds
    them the same way); every cell an element reaches into is the machine's."""
    written = flatten(shape["elements"], textures={})
    rest = [posed(w, _part_matrix(ship_parts, part_of(ship_parts, w.name), inputs_of(REST))) for w in written]
    by_cell = {}
    for el in rest:
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, []).append(el)
    out = []
    for pos in sorted(by_cell, key=lambda c: (c[1], c[2], c[0])):
        boxes = cell_boxes(by_cell[pos], pos)
        out.append({"pos": list(pos), "boxes": boxes} if boxes else {"pos": list(pos), "hollow": True})
    return with_lids(out, columns=no_lid)


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    poses = [REST, (0.7, 0.7), (-2.2, 2.2), (4.0, 9.0), (9.3, 9.3)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    span = [(min(c[k] for c in cells), max(c[k] for c in cells)) for k in range(3)]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; {len(cells)} cells, x {span[0]}, "
          f"y {span[1]}, z {span[2]}; power {ship['powerCell']} {ship['powerFace']}, input {ship['inputCell']} {ship['inputFace']}, "
          f"output {ship['outputCell']} {ship['outputFace']}")
    want = {tuple(shift_cell(c, ORIGIN_CELL)) for c in (POWER_CELL, INPUT_CELL, OUTPUT_CELL)}
    if worst > 1e-6 or (0, 0, 0) not in cells or not want <= set(cells):
        print("FAIL: the shipped model is not the checked one moved, or an anchor cell is not the machine's")
        return False
    return True


# ---------------------------------------------------------------- reference poses
def reference_poses():
    """theta over both signs and past two turns (the stirrer's cycle), with travel |theta| and more."""
    out = []
    for i, th in enumerate((0.0, 0.3, 1.1, -2.3, 2.9, 4.0, -5.5, 7.5, -9.1, 12.0)):
        out.append((th, round(abs(th) + (0.0, 3.7)[i % 2], 6)))
    return out


def reference_json(ship_parts):
    poses = []
    for pose in reference_poses():
        th, ps = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose))) for q in ship_parts}
        poses.append({"theta": th, "travel": ps, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped partingfurnace-rig.json's parts: each part's matrix as 3 rows of "
                        "4 (block units) at each pose. The site's and the generator's tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Keep element names when editing: the rig "
             "finds its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return (REST, (0.9, 0.9), (2.6, 2.6), (4.4, 4.4))


def fix_coplanar(els, parts):
    return fix_coplanar_posed(els, lambda es, pose: [posed(el, pm(parts, el.part, pose)) for el in es], coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the parting furnace's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_partingfurnace
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        for pose, pairs in before.items():
            print(f"coplanar faces before the fix at {pose}: {len(pairs)} pairs")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_partingfurnace.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "partingfurnace.json", args.out / "partingfurnace_frame.json", args.out / "partingfurnace-rig.json",
                args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "partingfurnace.json", SHAPE_DIR / "partingfurnace_frame.json", RIG_DIR / "partingfurnace-rig.json",
                REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_partingfurnace.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship), reference_dumps(reference_json(ship_parts)))
    for path, text in zip(outs, texts):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        json.loads(path.read_text())
        print(f"wrote {path} ({path.stat().st_size // 1024} KiB)")
    if not (check_shipped(els, parts, ship_els, ship_parts, ship) and ok):
        sys.exit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
