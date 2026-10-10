#!/usr/bin/env python3
"""Generate the parting furnace's shapes, rig and reference poses (Python 3.11 stdlib only).

The parting furnace is one upgradeable machine that does every parting step of the ore line (#740): a small
parting works of the 1800s, four blocks wide and two deep on a paved floor, one chimney stack at the back, every
furnace's working face to the front (south). Its footprint is its level 2 size from the frame on:

    the frame    the brick-paved floor and the chimney stack (the fires' flues run under the floor to it);
    level 1      (water) the blast (an engine shaft from the vanilla axle, a crank in a Scotch yoke working a small
                 blowing cylinder, and the blast pipe round the back to the tuyere), the English cupellation (test)
                 furnace, the liquation furnace, and the acid parting vessel under its hood with the process-water
                 cock over it, fed from a ppex pipe;
    level 2      (mill) the Parkes kettle in its setting, its stirrer (a bevel pair off the engine shaft), and its
                 chutes: the input chute into the kettle, and the siphon and output spout out of it.

Level 2 adds to level 1 and takes nothing away: the Parkes process still needs the rich crust cupelled, and
liquation and acid parting serve other ores.

Every element was made for the Seraph Horizons mod; no other mod's model is used.

It writes, deterministically,

    partingfurnace.json         the whole machine, every level's parts  (assets/.../shapes/block/)
    partingfurnace_frame.json   the frame only (block and item)         (assets/.../shapes/block/)
    partingfurnace-rig.json     cells, anchors and the part rig         (assets/.../config/)
    rig-reference.json          every part's matrix at a grid of poses  (tests/PartingFurnace/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_partingfurnace.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the machine
box's north-west-bottom corner (the "build frame"); `shipped()` moves it so the controller cell, the cupellation
furnace's front cell at ground level, is [0,0,0].

The rig reads one input, theta, the axle's angle: the engine shaft and (level 2) the bevel pinion turn with it,
the yoke rises and falls with the crank, and the stirrer turns at half the shaft's speed. Every other part stands
still.
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

from machinegen.checks import cell_boxes, cells_touched  # noqa: E402
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
CELLS_X, CELLS_Y, CELLS_Z = 4, 4, 2          # four wide (x), two deep (z); two high, the stack to four
ORIGIN_CELL = (2, 0, 1)                      # the controller: the cupellation furnace's front cell, at ground level
POWER_CELL, POWER_FACE = (3, 1, 0), "north"  # the vanilla axle comes in along z at (x 56, y 24): the engine shaft
WATER_CELL, WATER_FACE = (1, 1, 1), "south"  # the process-water pipe comes in along z at (x 24, y 24): the acid's cock
INPUT_CELL, INPUT_FACE = (3, 1, 1), "east"   # level 2: the input chute comes in from the east, into the kettle
OUTPUT_CELL, OUTPUT_FACE = (3, 0, 1), "south"   # level 2: the output spout, fed by the kettle's siphon

TEXTURES = {
    "brick": "game:block/clay/brick/eight/running/orange1",     # the furnaces' brickwork, the stack
    "paving": "game:block/clay/brick/eight/running/red1",       # the floor
    "firebrick": "game:block/clay/brick/eight/running/cream1",  # fire bridge, liquation steps, the stack's cap
    "iron": "game:block/metal/plate/iron",                      # castings: the cylinder, pedestals, pots, the kettle
    "steel": "game:block/metal/sheet-plain/steel1",             # shafts, pins, gears, rods
    "oak": "game:block/wood/debarked/oak",                      # the axle's continuation
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
    "leadpipe": "game:block/metal/sheet-plain/lead4",           # the process-water pipe (ppex's lead pipe's texture)
    "brass": "game:block/metal/plate/brass",                    # the water cock
    "soot": "game:block/black",                                 # down the stack's flue
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face
SEG = 24.0                                   # brickwork is split into pieces no longer than this (UVs stretch at most 1.5x)

FLOOR = 2.0                                  # the paved floor's top

# ---------------------------------------------------------------- the blast (level 1): engine shaft, crank, yoke, cylinder
SHAFT = (56.0, 24.0)                         # (x, y): the engine shaft's axis, along z from the power face
SHAFT_R = 1.0                                # its apothem (an octagon)
OAK_Z = 1.0                                  # the vanilla axle's oak continuation, z 0..OAK_Z
CYL_C = (56.0, 8.0)                          # (x, z): the blowing cylinder's axis, under the crank pin
CYL_R, CYL_FLANGE_R = 3.6, 4.2               # its barrel and its flanges (apothems)
CYL_Y = (2.8, 13.0)                          # its barrel, between the flanges
CYL_TOP = 13.8                               # the top flange's top: the cover and the guides' feet stand on it
GLAND_Y = (14.4, 16.0)                       # the stuffing box the piston rod runs through
CRANK_R = 2.5                                # the crank's throw: the yoke's stroke is twice it
PIN_R = 0.8                                  # the crank pin's apothem (an octagon, 0.866 to its corners)
WEB_Z = ((5.4, 6.4), (9.6, 10.6))            # the crank's two webs, either side of the yoke
YOKE_Z = (7.1, 8.9)                          # the yoke, its slippers and its guides
SLOT_H = 0.95                                # the yoke's slot's half height (the pin's corners, and a little)
SLOT_L = CRANK_R + 0.87 + 0.2                # its half length along x
YOKE_BAR, YOKE_SIDE, SLIPPER, GUIDE_T = 1.2, 1.0, 0.6, 0.6
ROD_LOW = 13.4                               # the piston rod's foot at rest (in the gland at the top of the stroke)
GUIDE_TOP = 30.0                             # the guides' cap
BEARINGS = (("n", (1.8, 3.4)), ("s", (12.6, 14.0)))   # the two pedestal bearings (z)
SHAFT_END = 15.6                             # the shaft's south end, in the bevel pinion's body (level 2)
BLAST_Y = 12.0                               # the blast pipe's axis height
BLAST_R = 1.1
BLAST_BACK_Z = 1.4                           # it runs along the back at z 1.4
ELBOW = 1.2                                  # an elbow's half size

# ---------------------------------------------------------------- the cupellation furnace (level 1)
CUP_X = (28.0, 48.5)
CUP_Z = (3.0, 21.0)
CUP_WALL = 2.5
CUP_HEARTH = 9.5                             # the hearth's top: the test's floor
CUP_SPRING = 16.5                            # the vault springs off the side walls here
CUP_RISE = 3.0
CUP_STAVES, CUP_T = 5, 2.0
GRATE_Y = 5.0
SILL_Y = 8.0                                 # the breast's sill: the working opening comes down to it
BRIDGE_X = (40.8, 42.4)                      # the fire bridge
FIRE_X = (BRIDGE_X[1], CUP_X[1] - CUP_WALL)  # the fireplace, at the east end, sunk below the hearth
TEST_C = (35.6, 12.0)                        # (x, z): the test's centre
TEST_A = (4.4, 3.4)                          # its half axes: an oval of bone ash in an iron ring
TEST_RING = 0.9
TEST_SEGS = 8
TEST_GAP = (2,)                              # the ring's front segment is left out: the breast
OPENING_X = (31.2, BRIDGE_X[0])              # the working opening in the front wall, over the breast
OPENING_TOP = 15.2
FIRE_HOLE_X, FIRE_HOLE_Y = (42.8, 45.6), (CUP_HEARTH, 13.6)   # the fire hole, over the grate
FIRE_DOOR_OPEN = 100.0                       # degrees the fire door stands open
TUYERE_Y = BLAST_Y                           # the blast comes in through the back wall over the test
POT_L = (TEST_C[0], 25.6)                    # (x, z): the litharge pot

# ---------------------------------------------------------------- the liquation furnace (level 1)
# Built in its own frame along x (the hearth falling west to its open end, its firebox door on the -z side),
# then turned a quarter about y onto the works' west end: the open end and the receiving pot face the front
# (south), the firebox door the west face.
LIQ_X = (24.0, 46.0)
LIQ_Z = (45.5, 58.5)
LIQ_BASE = 8.0
LIQ_WALL = 2.0
LIQ_SPRING, LIQ_RISE = 15.0, 2.5
LIQ_STAVES, LIQ_T = 4, 1.8
PLATE = ((24.8, 8.7), (41.5, 12.0))          # ((x, y), (x, y)): the inclined hearth plate's top, low end west
PLATE_T = 0.7
POT_Q = (20.5, 52.0)                         # (x, z): the receiving pot, at the low end
LIQ_PIVOT = (35.0, 52.0)                     # (x, z) in its own frame ...
LIQ_AT = (9.0, 14.0)                         # ... goes here in the works' (x, z), turned +90 degrees about y


def liq_world(p):
    """A point of the liquation furnace's own frame in the works' frame."""
    x, y, z = p
    return (LIQ_AT[0] + (z - LIQ_PIVOT[1]), y, LIQ_AT[1] - (x - LIQ_PIVOT[0]))


# ---------------------------------------------------------------- the acid parting (level 1)
AC_X = (16.0, 28.0)
AC_PIER = 1.5
AC_BACK = 13.0                               # the hood stands against the stack's front
AC_FRONT = 30.0
AC_FIRE_TOP = 8.5                            # the firebox's top: the sand bath's tray on it
TRAY_Y = (AC_FIRE_TOP, 10.6)
VESSEL_C = (22.0, 22.0)                      # (x, z): the stoneware parting vessel
VESSEL_R = 3.6
VESSEL_Y = (9.6, 15.4)                       # its body; the rim over it
RIM_Y = (15.4, 16.6)
RIM_R = (2.9, 3.8)
HOOD_Y = (28.0, 30.0)
WATER = (24.0, 24.0)                         # (x, y): the process-water pipe's axis, along z from the water face
PIPE_HALF = 3.0                              # ppex's pipe: 6 voxels across
COCK_Z = (21.8, 23.4)

# ---------------------------------------------------------------- the stack (frame)
STACK_X, STACK_Z = (16.4, 27.6), (1.0, 12.2)
STACK_SHAFT = 0.6                            # the shaft is set in this far from the base and the cap
STACK_Y = (8.0, 58.0, 62.0)                  # the base's top, the cap's foot, the top
FLUE = 2.2                                   # the flue's half width at the top

# ---------------------------------------------------------------- the Parkes kettle (level 2)
KET_C = (SHAFT[0], 22.0)                     # (x, z): the kettle's axis, under the engine shaft
SET_R = 6.8                                  # the brick setting's apothem (an octagon)
SET_TOP = 12.0
KET_R = 5.0                                  # the kettle's mouth: the flange ring's inside
KET_FLANGE = 6.3
FLANGE_Y = (SET_TOP, 12.8)
BATH_Y = (11.6, 12.4)                        # the molten lead's surface layer
STIR_RATIO = -0.5                            # the stirrer turns at half the shaft's speed (the other way, seen from above)
GEAR_MOD = 1.0
PINION_TEETH, WHEEL_TEETH = 6, 12
GEAR_FACE = 2.0
APEX = (KET_C[0], SHAFT[1], KET_C[1])        # the bevel pair's cone apex: where the two axes cross
GAL_Z = (15.7, 16.9, 27.1, 28.3)             # the gallows' standards (z), on the kettle's flange
GAL_UP = (17.6, 18.6)                        # its upper crossbeam
GAL_LOW = (14.2, 15.2)                       # its lower crossbeam
STIR_Y = (11.0, 21.4)                        # the stirrer's shaft
PADDLE = (11.8, 12.9, 3.4)                   # the paddles: their y span (they break the surface) and reach
SIPHON = (53.0, 25.6)                        # (x, z): the siphon's leg in the kettle
SIPHON_TOP = 15.6
SIPHON_OUT = 30.4                            # its outer leg (z), in front of the setting
SPOUT_Y = 5.6                                # the output spout's bottom


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


def tiles(lo, hi, name, part, tex, seg=SEG):
    """A box split into pieces no longer than `seg` along each axis, so no face's texture is stretched far."""
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
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}   # a disc's angle 0 direction, and the in-plane direction across it


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
    """A tube of n boxes from r_in to r_out (an n-gon outside). Boxes overlap their neighbours near the bore,
    so their ends step in by 0, 0.008, 0.016, 0.024 round the ring (no two that overlap end in one plane)."""
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


def vault(x0, x1, cz, spring, span, rise, n, t, name, part, tex, seg=SEG):
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


def crossed_oval(c, y0, y1, a, b, name, part, tex):
    """An oval slab, a x b half axes (x, z), as two overlapping rectangles."""
    out = []
    for i, t in enumerate((25, 65)):
        hx, hz = a * math.cos(math.radians(t)), b * math.sin(math.radians(t))
        out.append(box([c[0] - hx, y0, c[1] - hz], [c[0] + hx, y1, c[1] + hz], f"{name}{i + 1}", part, tex))
    return out


# ---------------------------------------------------------------- bevel gears
TIP, ROOT = 0.5, 0.625                       # addendum and dedendum, in modules (stub teeth, as cast)
BODY_TIERS, BODY_K = 2, 4                    # a gear's body: discs (octagons) stepping in under the root cone
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
    k_ax = max(range(3), key=lambda k: abs(toward[k]))
    axis = "xyz"[k_ax]
    sign = toward[k_ax]
    corner = 1 / math.cos(math.pi / (2 * BODY_K))   # a 2k-gon's corners stand this much past its apothem
    r_out, r_in = rad(a_o), rad(a_o - GEAR_FACE)
    back = ax(a_o) + 0.8
    prev = back
    for j in range(BODY_TIERS):
        rho = r_out - (r_out - r_in) * j / max(1, BODY_TIERS - 1)
        a_at = (rho * corner + ROOT * GEAR_MOD * math.cos(delta)) / math.sin(delta)
        top = ax(a_at) + BODY_GAP
        lo, hi = sorted((APEX[k_ax] - sign * prev, APEX[k_ax] - sign * top))
        out += disc(axis, APEX, lo, hi, rho, f"{name}_body{j + 1}", part, tex, k=BODY_K, phase=math.pi / (2 * BODY_K))
        prev = top
    return out, back


DELTA_P = math.atan2(PINION_TEETH, WHEEL_TEETH)   # the pinion's pitch cone half angle (shaft angle 90 degrees)
DELTA_W = math.pi / 2 - DELTA_P
R_P = GEAR_MOD * PINION_TEETH / 2
R_W = GEAR_MOD * WHEEL_TEETH / 2


# ---------------------------------------------------------------- the frame
def build_frame():
    """The brick-paved floor over the whole footprint and the chimney stack at the back, between the liquation
    furnace and the cupellation furnace: a base, the shaft, a fire-brick cap with the flue open, soot-black in it."""
    f = "frame"
    out = []
    for cx in range(CELLS_X):
        for cz in range(CELLS_Z):
            out.append(box([cx * B, 0.0, cz * B], [(cx + 1) * B, FLOOR, (cz + 1) * B], f"fr_floor_{cx}{cz}", f, "paving"))
    (x0, x1), (z0, z1) = STACK_X, STACK_Z
    s = STACK_SHAFT
    base, foot, top = STACK_Y
    out.append(box([x0, FLOOR, z0], [x1, base, z1], "fr_stack_base", f, "brick"))
    out += tiles([x0 + s, base, z0 + s], [x1 - s, foot, z1 - s], "fr_stack_shaft", f, "brick", seg=16.0)
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    fx0, fx1, fz0, fz1 = cx - FLUE, cx + FLUE, cz - FLUE, cz + FLUE
    out += [box([x0, foot, z0], [x1, top, fz0], "fr_stack_capn", f, "firebrick"),
            box([x0, foot, fz1], [x1, top, z1], "fr_stack_caps", f, "firebrick"),
            box([x0, foot, fz0], [fx0, top, fz1], "fr_stack_capw", f, "firebrick"),
            box([fx1, foot, fz0], [x1, top, fz1], "fr_stack_cape", f, "firebrick")]
    out.append(box([fx0, foot - 2.0, fz0], [fx1, top - 0.3, fz1], "fr_stack_soot", f, "soot"))
    return out


# ---------------------------------------------------------------- level 1: the blast
SH_C = (SHAFT[0], SHAFT[1], 0.0)


def build_shaft():
    """The engine shaft: the axle's oak continuation at the power face, the iron shaft in two pedestal bearings,
    and between them a crank (two webs and a pin) over the blowing cylinder. At theta 0 the pin is east of the
    shaft: the yoke at mid stroke."""
    p = "shaft"
    x, y = SHAFT
    out = [box([x - 1.5, y - 0.6, 0.0], [x + 1.5, y + 0.6, OAK_Z], "sh_oaka", p, "oak"),
           box([x - 0.6, y - 1.5, 0.0], [x + 0.6, y + 1.5, OAK_Z], "sh_oakb", p, "oak")]
    out += disc("z", SH_C, OAK_Z, WEB_Z[0][0], SHAFT_R, "sh_shaftn", p, "steel", k=4)
    out += disc("z", SH_C, WEB_Z[1][1], SHAFT_END, SHAFT_R, "sh_shafts", p, "steel", k=4)
    for tag, (z0, z1) in (("n", WEB_Z[0]), ("s", WEB_Z[1])):
        out.append(box([x - 1.6, y - 1.4, z0], [x + CRANK_R + 1.3, y + 1.4, z1], f"sh_web{tag}", p, "iron"))
    out += disc("z", (x + CRANK_R, y, 0.0), WEB_Z[0][1], WEB_Z[1][0], PIN_R, "sh_pin", p, "steel", k=4)
    return out


def build_yoke():
    """The slotted yoke (a Scotch yoke): the crank pin runs along its slot, so it rises and falls exactly with
    the pin's height; slippers at its ends run on the guides; the piston rod goes down into the cylinder."""
    p = "yoke"
    x, y = SHAFT
    z0, z1 = YOKE_Z
    xs0, xs1 = x - SLOT_L, x + SLOT_L
    out = [box([xs0 - YOKE_SIDE, y + SLOT_H, z0], [xs1 + YOKE_SIDE, y + SLOT_H + YOKE_BAR, z1], "yk_top", p, "iron"),
           box([xs0 - YOKE_SIDE, y - SLOT_H - YOKE_BAR, z0], [xs1 + YOKE_SIDE, y - SLOT_H, z1], "yk_bottom", p, "iron"),
           box([xs0 - YOKE_SIDE, y - SLOT_H, z0], [xs0, y + SLOT_H, z1], "yk_sidew", p, "iron"),
           box([xs1, y - SLOT_H, z0], [xs1 + YOKE_SIDE, y + SLOT_H, z1], "yk_sidee", p, "iron")]
    h = SLOT_H + YOKE_BAR + 0.4
    out += [box([xs0 - YOKE_SIDE - SLIPPER, y - h, z0], [xs0 - YOKE_SIDE, y + h, z1], "yk_slipperw", p, "steel"),
            box([xs1 + YOKE_SIDE, y - h, z0], [xs1 + YOKE_SIDE + SLIPPER, y + h, z1], "yk_slippere", p, "steel")]
    cx, cz = CYL_C
    out.append(box([cx - 0.5, ROD_LOW, cz - 0.5], [cx + 0.5, y - SLOT_H - YOKE_BAR, cz + 0.5], "yk_rod", p, "steel"))
    return out


def guide_x():
    """The yoke's guides (x): just outside its slippers."""
    x = SHAFT[0]
    a = x - SLOT_L - YOKE_SIDE - SLIPPER
    b = x + SLOT_L + YOKE_SIDE + SLIPPER
    return (a - GUIDE_T, a), (b, b + GUIDE_T)


def blast_path():
    """The blast pipe's corners (x, z) at BLAST_Y: out of the valve chest west, north, along the back west to
    over the tuyere, then south into the back wall."""
    ex = CYL_C[0] - CYL_R - 1.2 - ELBOW       # elbow 1, against the valve chest
    return [(ex, CYL_C[1]), (ex, BLAST_BACK_Z), (TEST_C[0], BLAST_BACK_Z)]


def build_engine():
    """The blowing engine's fixed parts: two cast-iron pedestals carrying the shaft's bearings, the blowing
    cylinder (a single-acting tub under the yoke, its piston drawn in on the downstroke) with its flanges,
    cover, gland and valve chest, the yoke's guides standing on its top flange, and the blast pipe from the
    valve chest round the back of the cupellation furnace to its tuyere."""
    p = "engine"
    x, y = SHAFT
    out = []
    for tag, (z0, z1) in BEARINGS:
        out.append(box([x - 3.0, FLOOR, z0 - 0.4], [x + 3.0, FLOOR + 0.8, z1 + 0.4], f"en_ped{tag}_foot", p, "iron"))
        out.append(box([x - 1.8, FLOOR + 0.8, z0], [x + 1.8, y - 2.4, z1], f"en_ped{tag}", p, "iron"))
        out.append(box([x - 2.2, y - 2.4, z0], [x + 2.2, y + 2.6, z1], f"en_ped{tag}_bearing", p, "iron"))
    cx, cz = CYL_C
    c = (cx, 0.0, cz)
    out += disc("y", c, FLOOR, CYL_Y[0], CYL_FLANGE_R, "en_cylfoot", p, "iron", k=4, phase=math.pi / 8)
    out += disc("y", c, CYL_Y[0], CYL_Y[1], CYL_R, "en_cyl", p, "iron", k=4, phase=math.pi / 8)
    out += disc("y", c, CYL_Y[1], CYL_TOP, CYL_FLANGE_R, "en_cylflange", p, "iron", k=4, phase=math.pi / 8)
    out += disc("y", c, CYL_TOP, GLAND_Y[0], 3.0, "en_cover", p, "iron", k=4, phase=math.pi / 8)
    out.append(box([cx - 1.2, GLAND_Y[0], cz - 1.2], [cx + 1.2, GLAND_Y[1], cz + 1.2], "en_gland", p, "steel"))
    vx = cx - CYL_R
    out.append(box([vx - 1.2, 8.0, cz - 2.0], [vx + 0.2, CYL_Y[1], cz + 2.0], "en_valvechest", p, "iron"))
    # the guides: a bar either side of the yoke on the top flange, a foot under each, a cap over them
    (g0, g1), (h0, h1) = guide_x()
    z0, z1 = YOKE_Z
    out += [box([g0, CYL_TOP, z0], [g1, GUIDE_TOP, z1], "en_guidew", p, "iron"),
            box([h0, CYL_TOP, z0], [h1, GUIDE_TOP, z1], "en_guidee", p, "iron"),
            box([g0, CYL_TOP, z0], [cx - CYL_FLANGE_R + 0.4, CYL_TOP + 0.8, z1], "en_footw", p, "iron"),
            box([cx + CYL_FLANGE_R - 0.4, CYL_TOP, z0], [h1, CYL_TOP + 0.8, z1], "en_foote", p, "iron"),
            box([g0, GUIDE_TOP, z0], [h1, GUIDE_TOP + 1.2, z1], "en_guidecap", p, "iron")]
    # the blast pipe: elbows at its corners, straight runs between them
    pts = blast_path()
    for i, (px, pz) in enumerate(pts, 1):
        out.append(box([px - ELBOW, BLAST_Y - ELBOW, pz - ELBOW], [px + ELBOW, BLAST_Y + ELBOW, pz + ELBOW], f"en_elbow{i}", p, "iron"))
    out += disc("z", (pts[0][0], BLAST_Y, 0.0), pts[1][1] + ELBOW, pts[0][1] - ELBOW, BLAST_R, "en_mainn", p, "iron", k=4)
    out += disc("x", (0.0, BLAST_Y, BLAST_BACK_Z), pts[2][0] + ELBOW, pts[1][0] - ELBOW, BLAST_R, "en_maine", p, "iron", k=4)
    return out


# ---------------------------------------------------------------- level 1: the cupellation furnace
def cup_vault():
    x0, x1 = CUP_X
    z0, z1 = CUP_Z[0] + CUP_WALL, CUP_Z[1] - CUP_WALL
    return vault(x0, x1, (z0 + z1) / 2, CUP_SPRING, z1 - z0, CUP_RISE, CUP_STAVES, CUP_T, "cp_vault", "cupel", "brick")


def ellipse_pt(c, a, t):
    return (c[0] + a[0] * math.cos(t), c[1] + a[1] * math.sin(t))


def build_cupel():
    """The English cupellation furnace: a hearth block with the fireplace sunk in its east end (grate, coke,
    fire), a fire bridge, the test (an oval iron ring of bone ash, the molten lead on it) in the hearth, walls,
    a low segmental vault, buckstays and tie rods, the tuyere through the back wall, and at the front the breast:
    a wide working opening down to the breast's sill, the litharge's channel out through it into its pot. The
    fire door stands open, so the fire shows; the working opening shows the test."""
    p = "cupel"
    x0, x1 = CUP_X
    z0, z1 = CUP_Z
    w = CUP_WALL
    h = CUP_HEARTH
    fx0, fx1 = FIRE_X
    ox0, ox1 = OPENING_X
    zb = TEST_C[1] + TEST_A[1] + 0.1            # the breast's notch starts at the test's front
    out = tiles([x0, FLOOR, z0], [x1, GRATE_Y, z1], "cp_hearth_low", p, "brick")
    out += tiles([x0, GRATE_Y, z0], [fx0, SILL_Y, z1], "cp_hearth_mid", p, "brick")
    out += tiles([x0, SILL_Y, z0], [fx0, h, zb], "cp_hearth_top", p, "brick")
    out.append(box([x0, SILL_Y, zb], [ox0, h, z1], "cp_hearth_sw", p, "brick"))
    out.append(box([ox1, SILL_Y, zb], [fx0, h, z1], "cp_hearth_se", p, "brick"))
    out.append(box([fx0, GRATE_Y, z0], [fx1, h, z0 + w], "cp_hearth_n", p, "brick"))
    out.append(box([fx0, GRATE_Y, z1 - w], [fx1, h, z1], "cp_hearth_s", p, "brick"))
    out.append(box([fx1, GRATE_Y, z0], [x1, h, z1], "cp_hearth_e", p, "brick"))
    # the fireplace: grate bars, a coke bed, the fire; the fire bridge between it and the test
    for i in range(4):
        gx = fx0 + 0.6 + i * (fx1 - fx0 - 1.2) / 3
        out.append(box([gx - 0.35, GRATE_Y, z0 + w], [gx + 0.35, GRATE_Y + 0.8, z1 - w], f"cp_grate{i + 1}", p, "iron"))
    out.append(box([fx0, GRATE_Y + 0.8, z0 + w], [fx1, h - 0.9, z1 - w], "cp_coke", p, "coke"))
    for i, (ez, ex) in enumerate(((9.0, fx0 + 1.4), (15.0, fx1 - 1.4)), 1):
        out.append(box([ex - 1.2, h - 0.9, ez - 2.6], [ex + 1.2, h - 0.2, ez + 2.6], f"cp_fire{i}", p, "ember"))
    out.append(box([BRIDGE_X[0], h, z0 + w], [BRIDGE_X[1], h + 3.0, z1 - w], "cp_bridge", p, "firebrick"))
    # walls to the springing; the front one round the working opening and the fire hole
    out += tiles([x0, h, z0], [x1, CUP_SPRING, z0 + w], "cp_wall_n", p, "brick")
    hx0, hx1 = FIRE_HOLE_X
    out.append(box([x0, h, z1 - w], [ox0, CUP_SPRING, z1], "cp_wall_sw", p, "brick"))
    out.append(box([ox0, OPENING_TOP, z1 - w], [ox1, CUP_SPRING, z1], "cp_lintel", p, "brick"))
    out.append(box([ox1, h, z1 - w], [hx0, CUP_SPRING, z1], "cp_wall_sm", p, "brick"))
    out.append(box([hx0, FIRE_HOLE_Y[1], z1 - w], [hx1, CUP_SPRING, z1], "cp_wall_firelintel", p, "brick"))
    out.append(box([hx1, h, z1 - w], [x1, CUP_SPRING, z1], "cp_wall_se", p, "brick"))
    out.append(box([ox0 - 0.3, OPENING_TOP, z1], [ox1 + 0.3, OPENING_TOP + 0.8, z1 + 0.4], "cp_lintel_iron", p, "iron"))
    out.append(box([x0, h, z0 + w], [x0 + w, CUP_SPRING, z1 - w], "cp_wall_w", p, "brick"))
    out.append(box([x1 - w, h, z0 + w], [x1, CUP_SPRING, z1 - w], "cp_wall_e", p, "brick"))
    # the vault, the haunches over the long walls, the end walls' gables
    staves, cy, r = cup_vault()
    out += staves
    vz0, vz1 = z0 + w, z1 - w
    cz = (vz0 + vz1) / 2
    out += tiles([x0, CUP_SPRING, z0], [x1, CUP_SPRING + 1.4, z0 + w], "cp_haunch_n", p, "brick")
    out += tiles([x0, CUP_SPRING, z1 - w], [x1, CUP_SPRING + 1.4, z1], "cp_haunch_s", p, "brick")
    out += gable(x0 + RING_PROUD, x0 + w, vz0, vz1, CUP_SPRING, cy, r, cz, 3, "cp_gable_w", p, "brick")
    out += gable(x1 - w, x1 - RING_PROUD, vz0, vz1, CUP_SPRING, cy, r, cz, 3, "cp_gable_e", p, "brick")
    # the test: a ring of iron segments round the oval, open at the front (the breast); bone ash in it, the
    # molten lead on that
    ty0, ty1 = h - 1.2, h + 1.2
    for i in range(TEST_SEGS):
        if i in TEST_GAP:
            continue
        t0, t1 = TAU * (i - 0.5) / TEST_SEGS, TAU * (i + 0.5) / TEST_SEGS
        pa, pb = ellipse_pt(TEST_C, TEST_A, t0), ellipse_pt(TEST_C, TEST_A, t1)
        dx, dz = pb[0] - pa[0], pb[1] - pa[1]
        ln = math.hypot(dx, dz)
        ext = 0.4 / ln
        pa2 = (pa[0] - dx * ext, pa[1] - dz * ext)
        pb2 = (pb[0] + dx * ext, pb[1] + dz * ext)
        s = 0.02 * (i % 2)
        out.append(segment_y(pa2, pb2, ty0 + s, ty1 - s, TEST_RING, f"cp_ring{i + 1}", p, "iron"))
    out += crossed_oval(TEST_C, h, h + 0.7, TEST_A[0] - 0.05, TEST_A[1] - 0.05, "cp_boneash", p, "boneash")
    out += crossed_oval(TEST_C, h + 0.7, h + 0.9, TEST_A[0] - 1.3, TEST_A[1] - 1.1, "cp_lead", p, "lead")
    # the breast: an iron channel from the test's front, out through the opening, down into the litharge pot
    tz = TEST_C[1] + TEST_A[1] - 0.6
    out += channel((TEST_C[0], h + 0.6, tz), (TEST_C[0], 7.6, POT_L[1] - 1.4), 1.0, 0.3, 0.8, "cp_channel", p, "iron", "z")
    pc = (POT_L[0], 0.0, POT_L[1])
    out += disc("y", pc, FLOOR, 7.0, 2.8, "cp_pot", p, "iron", k=4, phase=math.pi / 8)
    out += disc("y", pc, 7.0, 7.3, 2.4, "cp_litharge", p, "litharge", k=4, phase=math.pi / 8)
    # the tuyere: an iron nozzle from the blast pipe's last elbow through the back wall, over the test's back edge
    out += disc("z", (TEST_C[0], TUYERE_Y, 0.0), BLAST_BACK_Z + ELBOW, TEST_C[1] - TEST_A[1] - 0.6, 0.8, "cp_tuyere", p, "iron", k=4)
    # the fire door stands open on its hinge at the hole's west edge, so the fire shows; the ash-pit door under it
    hy0, hy1 = FIRE_HOLE_Y
    door = [box([hx0, hy0, z1], [hx1 + 0.3, hy1 + 0.3, z1 + 0.6], "cp_firedoor", p, "iron")]
    rotate(door, "y", -FIRE_DOOR_OPEN, (hx0, 0.0, z1 + 0.3))
    out += door
    out.append(box([fx0 + 0.6, FLOOR + 0.6, z1], [fx1 - 0.6, GRATE_Y - 0.4, z1 + 0.4], "cp_ashdoor", p, "iron"))
    # the binding: buckstays on the long faces and tie rods over the vault
    top = cy + r + CUP_T + 0.4
    for i, bx in enumerate((x0 + 1.2, x1 - 1.2), 1):
        out += [box([bx - 0.5, FLOOR, z0 - 0.6], [bx + 0.5, top + 0.6, z0], f"cp_buck{i}n", p, "iron"),
                box([bx - 0.5, FLOOR, z1], [bx + 0.5, top + 0.6, z1 + 0.6], f"cp_buck{i}s", p, "iron"),
                box([bx - 0.4, top, z0 - 0.6], [bx + 0.4, top + 0.6, z1 + 0.6], f"cp_tie{i}", p, "iron")]
    return out


# ---------------------------------------------------------------- level 1: the liquation furnace
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
    """The liquation furnace: a brick base over its firebox, an inclined cast-iron hearth plate on fire-brick
    steps, raised rims, a low vault over it open at the low end, the cakes on the slope and dross at its foot, a
    spout out of the open end into the receiving pot, buckstays, and the fire door; built along x, then turned
    onto the works' west end."""
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
    out += gable(end, x1 - RING_PROUD, vz0, vz1, LIQ_SPRING, cy, r, cz, 3, "lq_gable", p, "brick")
    # the steps under the plate, the plate and its rims
    pz0, pz1 = vz0 + 0.2, vz1 - 0.2
    (xa, _), (xb, _) = PLATE
    dy = PLATE_T / math.cos(PLATE_ANGLE)
    prev = LIQ_BASE
    for i, sx in enumerate((28.0, 33.5, 38.5), 1):
        top = plate_y(sx) - dy
        out.append(box([sx, prev, pz0], [end, top, pz1], f"lq_step{i}", p, "firebrick"))
        prev = top
    along = math.hypot(xb - xa, PLATE[1][1] - PLATE[0][1])
    mid = (xa + xb) / 2
    zc = (pz0 + pz1) / 2
    out.append(on_plate(mid, -PLATE_T, along, PLATE_T, (pz1 - pz0) / 2, zc, "lq_plate", p, "iron"))
    for tag, z in (("n", pz0 + 0.5), ("s", pz1 - 0.5)):
        out.append(on_plate(mid, 0.0, along - 0.02, 1.0, 0.5, z, f"lq_rim{tag}", p, "iron"))
    # the cakes on the slope, dross at the low end
    for i, cx in enumerate((30.5, 36.5), 1):
        out.append(on_plate(cx, 0.0, 4.4, 1.6, 2.6, zc, f"lq_cake{i}", p, "tin"))
    out.append(on_plate(26.4, 0.0, 2.0, 0.6, 2.0, zc + 0.6, "lq_dross", p, "dross"))
    # the spout out of the open end into the receiving pot
    out += channel((xa + 0.6, plate_y(xa + 0.6) + 0.05, zc), (POT_Q[0] + 0.8, 7.9, zc), 1.0, 0.3, 0.8, "lq_spout", p, "iron", "x")
    pc = (POT_Q[0], 0.0, POT_Q[1])
    out += disc("y", pc, FLOOR, 6.8, 2.4, "lq_pot", p, "iron", k=4, phase=math.pi / 8)
    out += disc("y", pc, 6.8, 7.1, 2.0, "lq_potmetal", p, "tin", k=4, phase=math.pi / 8)
    # the fire door on the west face; the binding
    out.append(box([29.0, FLOOR + 1.0, z0 - 0.5], [34.0, FLOOR + 5.5, z0], "lq_firedoor", p, "iron"))
    top = cy + r + LIQ_T + 0.4
    for i, bx in enumerate((25.2, 44.8), 1):
        out += [box([bx - 0.5, FLOOR, z0 - 0.6], [bx + 0.5, top + 0.6, z0], f"lq_buck{i}n", p, "iron"),
                box([bx - 0.5, FLOOR, z1], [bx + 0.5, top + 0.6, z1 + 0.6], f"lq_buck{i}s", p, "iron"),
                box([bx - 0.4, top, z0 - 0.6], [bx + 0.4, top + 0.6, z1 + 0.6], f"lq_tie{i}", p, "iron")]
    pv = (LIQ_PIVOT[0], 0.0, LIQ_PIVOT[1])
    rotate(out, "y", 90.0, pv)
    translate(out, [LIQ_AT[0] - LIQ_PIVOT[0], 0.0, LIQ_AT[1] - LIQ_PIVOT[1]])
    return out


# ---------------------------------------------------------------- level 1: the acid parting vessel
def build_acid():
    """The parting vessel: a brick firebox between two brick piers, a cast-iron tray of sand on it, the
    stoneware vessel set in the sand with the aqua fortis in it, and a brick hood over all against the stack,
    which takes the fumes. The process water comes in from the water face by a lead pipe to a brass cock over
    the vessel: it dilutes the silver solution and washes the gold left in the vessel."""
    p = "acid"
    x0, x1 = AC_X
    fx0, fx1 = x0 + AC_PIER, x1 - AC_PIER
    zb, zf = AC_BACK, AC_FRONT
    out = tiles([x0, FLOOR, zb], [fx0, HOOD_Y[0], zf], "ac_pierw", p, "brick", seg=16.0)
    out += tiles([fx1, FLOOR, zb], [x1, HOOD_Y[0], zf], "ac_piere", p, "brick", seg=16.0)
    out += tiles([x0, HOOD_Y[0], zb], [x1, HOOD_Y[1], zf], "ac_hood", p, "brick", seg=16.0)
    out.append(box([fx0, FLOOR, zb], [fx1, AC_FIRE_TOP, zf], "ac_firebox", p, "brick"))
    out.append(box([fx0 + 2.4, FLOOR + 1.0, zf], [fx1 - 2.4, FLOOR + 5.0, zf + 0.5], "ac_firedoor", p, "iron"))
    ty0, ty1 = TRAY_Y
    rim = 0.5
    out.append(box([fx0, ty0, zb], [fx1, ty0 + 0.6, zf], "ac_tray", p, "iron"))
    out += [box([fx0, ty0 + 0.6, zb], [fx1, ty1, zb + rim], "ac_trayn", p, "iron"),
            box([fx0, ty0 + 0.6, zf - rim], [fx1, ty1, zf], "ac_trays", p, "iron"),
            box([fx0, ty0 + 0.6, zb + rim], [fx0 + rim, ty1, zf - rim], "ac_trayw", p, "iron"),
            box([fx1 - rim, ty0 + 0.6, zb + rim], [fx1, ty1, zf - rim], "ac_traye", p, "iron")]
    out.append(box([fx0 + rim, ty0 + 0.6, zb + rim], [fx1 - rim, ty1 - 0.4, zf - rim], "ac_sand", p, "sand"))
    vc = (VESSEL_C[0], 0.0, VESSEL_C[1])
    out += disc("y", vc, VESSEL_Y[0], VESSEL_Y[1], VESSEL_R, "ac_vessel", p, "stoneware", k=4, phase=math.pi / 8)
    out += ring("y", vc, RIM_Y[0], RIM_Y[1], RIM_R[0], RIM_R[1], 8, "ac_rim", p, "stoneware")
    out += disc("y", vc, RIM_Y[0] + 0.1, RIM_Y[0] + 0.4, RIM_R[0] + 0.05, "ac_acid", p, "acid", k=4, phase=math.pi / 8)
    # the process water: a lead pipe (ppex's 6 x 6 section) from the water face to a brass cock over the vessel
    wx, wy = WATER
    h = PIPE_HALF
    out.append(box([wx - h, wy - h, COCK_Z[1]], [wx + h, wy + h, CELLS_Z * B], "ac_pipe", p, "leadpipe"))
    out.append(box([wx - 1.6, wy - 3.4, COCK_Z[0]], [wx + 1.6, wy + 2.6, COCK_Z[1]], "ac_cock", p, "brass"))
    out.append(box([wx - 0.6, RIM_Y[1] + 0.9, COCK_Z[0] + 0.2], [wx + 0.6, wy - 3.4, COCK_Z[0] + 1.4], "ac_nozzle", p, "brass"))
    return out


# ---------------------------------------------------------------- level 2: the Parkes kettle, its stirrer, the chutes
KC = (KET_C[0], 0.0, KET_C[1])


def build_kettle():
    """The Parkes kettle: an octagonal brick setting over its firebox (fire and ash-pit doors on its front), the
    cast-iron kettle's flange showing round its mouth, the molten lead in it with the zinc crust rising, and a
    skimmer lying across the flange."""
    p = "kettle"
    out = disc("y", KC, FLOOR, SET_TOP, SET_R, "kt_setting", p, "brick", k=4, phase=math.pi / 8)
    out += ring("y", KC, FLANGE_Y[0], FLANGE_Y[1], KET_R, KET_FLANGE, 8, "kt_flange", p, "iron")
    out += disc("y", KC, BATH_Y[0], BATH_Y[1], KET_R + 0.05, "kt_bath", p, "lead", k=4, phase=math.pi / 8)
    for i, (ang, rr, sz) in enumerate(((0.9, 3.0, (2.6, 1.8)), (3.6, 2.8, (2.2, 2.4))), 1):
        cx, cz = KET_C[0] + rr * math.sin(ang), KET_C[1] + rr * math.cos(ang)
        el = box([cx - sz[0] / 2, BATH_Y[1], cz - sz[1] / 2], [cx + sz[0] / 2, BATH_Y[1] + 0.3, cz + sz[1] / 2], f"kt_crust{i}", p, "zinc")
        rotate([el], "y", math.degrees(ang), (cx, 0.0, cz))
        out.append(el)
    # the skimmer, a perforated ladle for taking off the crust, lying on the flange at the east
    sx = KET_C[0] + KET_R + 0.2
    out.append(box([sx - 1.4, FLANGE_Y[1], KET_C[1] + 1.4], [sx + 1.4, FLANGE_Y[1] + 0.4, KET_C[1] + 4.2], "kt_skimmer", p, "iron"))
    zf = KET_C[1] + SET_R
    out.append(box([KET_C[0] - 1.8, FLOOR + 3.0, zf], [KET_C[0] + 1.8, FLOOR + 7.4, zf + 0.5], "kt_firedoor", p, "iron"))
    out.append(box([KET_C[0] - 1.5, FLOOR + 0.4, zf], [KET_C[0] + 1.5, FLOOR + 2.2, zf + 0.4], "kt_ashdoor", p, "iron"))
    return out


def build_gallows():
    """The stirrer's gallows: two cast-iron standards bolted to the kettle's flange north and south, an upper and
    a lower crossbeam between them, each with a bearing boss round the stirrer's shaft."""
    p = "gallows"
    x = KET_C[0]
    out = []
    a0, a1, b0, b1 = GAL_Z
    for tag, (z0, z1) in (("n", (a0, a1)), ("s", (b0, b1))):
        out.append(box([x - 0.8, FLANGE_Y[1], z0], [x + 0.8, GAL_UP[1], z1], f"gl_standard{tag}", p, "iron"))
    out.append(box([x - 1.0, GAL_UP[0], a0], [x + 1.0, GAL_UP[1], b1], "gl_upper", p, "iron"))
    out.append(box([x - 0.8, GAL_LOW[0], a0], [x + 0.8, GAL_LOW[1], b1], "gl_lower", p, "iron"))
    z = KET_C[1]
    out.append(box([x - 1.5, GAL_UP[1], z - 1.5], [x + 1.5, GAL_UP[1] + 0.8, z + 1.5], "gl_bossup", p, "iron"))
    out.append(box([x - 1.4, GAL_LOW[0] - 0.4, z - 1.4], [x + 1.4, GAL_LOW[1] + 0.4, z + 1.4], "gl_bosslow", p, "iron"))
    return out


def build_stirrer():
    """The stirrer: a vertical steel shaft in the gallows' two bearings, two paddles crossing in the bath (their
    tops break its surface, so the stirring shows), and the bevel wheel at its top, under the engine shaft."""
    p = "stirrer"
    sc = KC
    out = disc("y", sc, STIR_Y[0], STIR_Y[1], 0.7, "st_shaft", p, "steel", k=4)
    y0, y1, reach = PADDLE
    for i in range(2):
        out.append(radial("y", sc, y0, y1, -reach, reach, 1.0, math.pi / 2 * i + math.pi / 4, f"st_paddle{i + 1}", p, "iron"))
    # the wheel: a gap faces the pinion (north, -z) at theta 0
    e1, e2 = [0.0, 0.0, 1.0], [1.0, 0.0, 0.0]
    wheel, _ = bevel("st_wheel", p, "steel", [0.0, 1.0, 0.0], (e1, e2), DELTA_W, WHEEL_TEETH, math.pi + math.pi / WHEEL_TEETH)
    out += wheel
    return out


def build_pinion():
    """The bevel pinion, keyed to the engine shaft's south end over the kettle: its tooth 1 down, into the
    wheel's gap at theta 0."""
    p = "bevelpinion"
    e1, e2 = [1.0, 0.0, 0.0], [0.0, 1.0, 0.0]
    out, _ = bevel("bp", p, "steel", [0.0, 0.0, 1.0], (e1, e2), DELTA_P, PINION_TEETH, -math.pi / 2)
    return out


CHUTE_WALL, CHUTE_SIDE = 0.4, 1.6             # a chute's plate, and how high its sides stand over its bottom
IN_Z = 24.0                                  # the input chute's centre line (z)
IN_TOP, IN_LOW = (CELLS_X * B, 17.2), (59.4, 14.0)   # (x, y) of its bottom's top: at the input face, over the bath
IN_HALF = 1.8


def chute_y(top, low, x):
    return top[1] + (x - top[0]) * (low[1] - top[1]) / (low[0] - top[0])


def build_chutes():
    """Level 2 makes the kettle chute-fed: an inclined chute from the input face (east) drops the charge into the
    kettle, a leg under it on the setting; a siphon over the kettle's front rim runs the desilverized lead down
    into the output spout, its lip on the output face (south)."""
    p = "chutes"
    out = channel((IN_TOP[0], IN_TOP[1], IN_Z), (IN_LOW[0], IN_LOW[1], IN_Z), IN_HALF, CHUTE_WALL, CHUTE_SIDE, "ch_in", p, "iron", "x")
    lx = KET_C[0] + KET_FLANGE - 0.4
    out.append(box([lx - 0.5, FLANGE_Y[1], IN_Z - 0.5], [lx + 0.5, chute_y(IN_TOP, IN_LOW, lx) - CHUTE_WALL - 0.3, IN_Z + 0.5], "ch_inleg", p, "iron"))
    # the siphon: down into the bath, over the rim, down in front of the setting into the spout
    sx, sz = SIPHON
    pr = 0.5
    out.append(box([sx - pr, BATH_Y[0] - 1.0, sz - pr], [sx + pr, SIPHON_TOP - 0.6, sz + pr], "ch_siphonin", p, "iron"))
    out.append(box([sx - 0.7, SIPHON_TOP - 0.6, sz - 0.7], [sx + 0.7, SIPHON_TOP + 0.8, sz + 0.7], "ch_siphonbend1", p, "iron"))
    out.append(box([sx - pr, SIPHON_TOP - pr + 0.1, sz + 0.7], [sx + pr, SIPHON_TOP + pr + 0.1, SIPHON_OUT - 0.7], "ch_siphontop", p, "iron"))
    out.append(box([sx - 0.7, SIPHON_TOP - 0.6, SIPHON_OUT - 0.7], [sx + 0.7, SIPHON_TOP + 0.8, SIPHON_OUT + 0.7], "ch_siphonbend2", p, "iron"))
    out.append(box([sx - pr, SPOUT_Y + 1.0, SIPHON_OUT - pr], [sx + pr, SIPHON_TOP - 0.6, SIPHON_OUT + pr], "ch_siphonout", p, "iron"))
    # the spout: a short iron trough on a stand, from behind the siphon's leg out to the output face
    out += channel((sx, SPOUT_Y, SIPHON_OUT - 1.6), (sx, SPOUT_Y - 0.6, CELLS_Z * B - CHUTE_SIDE * 0.07), 1.4, CHUTE_WALL, CHUTE_SIDE,
                   "ch_spout", p, "iron", "z")
    out.append(box([sx - 0.6, FLOOR, SIPHON_OUT - 0.6], [sx + 0.6, SPOUT_Y - CHUTE_WALL - 0.05, SIPHON_OUT + 0.6], "ch_spoutleg", p, "iron"))
    return out


def build():
    els = build_shaft() + build_yoke() + build_engine() + build_cupel() + build_liquation() + build_acid()
    els += build_kettle() + build_gallows() + build_stirrer() + build_pinion() + build_chutes() + build_frame()
    return els


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


LEVELS = {
    "1": ["blast", "cupel", "liquation", "acid"],
    "2": ["kettle", "stirrer", "chutes"],
}
LEVEL_TIERS = {"1": "water", "2": "mill"}
REQUIRES = [v for k in sorted(LEVELS) for v in LEVELS[k]]

_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    shaft = {"type": "rotate", "axis": "z", "pivot": pt(SHAFT[0], SHAFT[1], 0.0), "ratio": 1.0}
    parts = [
        {"id": "shaft", "match": ["sh_*"], "requires": "blast", "drivers": [dict(shaft)]},
        # the crank pin is east of the shaft at theta 0, so its height is y + r sin(theta): the yoke rides it
        {"id": "yoke", "match": ["yk_*"], "requires": "blast",
         "drivers": [{"type": "slide", "axis": "y", "amplitude": r6(CRANK_R / B), "ratio": 1.0, "phase": 0.0}]},
        {"id": "engine", "match": ["en_*"], "requires": "blast", "drivers": []},
        {"id": "cupel", "match": ["cp_*"], "requires": "cupel", "drivers": []},
        {"id": "liquation", "match": ["lq_*"], "requires": "liquation", "drivers": []},
        {"id": "acid", "match": ["ac_*"], "requires": "acid", "drivers": []},
        {"id": "kettle", "match": ["kt_*"], "requires": "kettle", "drivers": []},
        {"id": "gallows", "match": ["gl_*"], "requires": "stirrer", "drivers": []},
        {"id": "stirrer", "match": ["st_*"], "requires": "stirrer",
         "drivers": [{"type": "rotate", "axis": "y", "pivot": pt(KET_C[0], 0.0, KET_C[1]), "ratio": r6(STIR_RATIO)}]},
        {"id": "bevelpinion", "match": ["bp_*"], "requires": "stirrer", "drivers": [dict(shaft)]},
        {"id": "chutes", "match": ["ch_*"], "requires": "chutes", "drivers": []},
        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


MOVING = ("shaft", "yoke", "stirrer", "bevelpinion")


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
    """Every cell an element reaches into at rest (the cells the machine occupies, its level 2 size)."""
    cells = set()
    for el in els:
        lo, hi = posed(el, pm(parts, el.part, REST)).aabb()
        cells.update(cells_touched(lo, hi))
    return sorted(cells, key=lambda c: (c[1], c[2], c[0]))


ANCHORS = ("input", "output")


def anchor_points():
    return {"input": (CELLS_X * B, IN_TOP[1] + 0.6, IN_Z),
            "output": (SIPHON[0], SPOUT_Y - 0.6 + 0.6, CELLS_Z * B)}


def make_rig(parts):
    pts = anchor_points()
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the cupellation furnace's "
                    "front cell at ground level; the works run 4 wide (x) and 2 deep (z), every furnace's working face to the "
                    "south, the chimney stack at the back. The rig reads theta alone (the axle's angle): the engine shaft and "
                    "(level 2) the bevel pinion turn with it, the yoke rises and falls with the crank, the stirrer turns at half "
                    "its speed. requires: one value per fitted set; levels lists each level's sets (1 water, 2 mill), level 2 "
                    "adding to level 1. A cell's boxes are the frame's (the floor, the stack); its fitted boxes are there only "
                    "while their set is fitted. See the parting furnace's README for the schema.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "waterCell": list(WATER_CELL),
        "waterFace": WATER_FACE,
        "inputCell": list(INPUT_CELL),
        "inputFace": INPUT_FACE,
        "outputCell": list(OUTPUT_CELL),
        "outputFace": OUTPUT_FACE,
        "input": {"pos": pt(*pts["input"])},
        "output": {"pos": pt(*pts["output"])},
        "levels": {k: list(v) for k, v in LEVELS.items()},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
CELL_KEYS = ("powerCell", "waterCell", "inputCell", "outputCell")


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
    for key in CELL_KEYS:
        ship[key] = shift_cell(rig[key], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig (every reader rebuilds
    them the same way); every cell an element reaches into is the machine's. A cell's `boxes` are its frame
    elements' (there at every level); its `fitted` boxes, by requires value in the levels' order, are each fitted
    set's, there only while that set is fitted."""
    written = flatten(shape["elements"], textures={})
    req = {p["id"]: p["requires"] for p in ship_parts}
    by_cell = {}
    for w in written:
        pid = part_of(ship_parts, w.name)
        el = posed(w, _part_matrix(ship_parts, pid, inputs_of(REST)))
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, {}).setdefault(req[pid], []).append(el)
    out = []
    for pos in sorted(by_cell, key=lambda c: (c[1], c[2], c[0])):
        groups = by_cell[pos]
        cell = {"pos": list(pos)}
        frame = cell_boxes(groups[None], pos) if None in groups else None
        if frame:
            cell["boxes"] = frame
        fitted = {}
        for r in REQUIRES:
            if r in groups:
                b = cell_boxes(groups[r], pos)
                if b:
                    fitted[r] = b
        if fitted:
            cell["fitted"] = fitted
        if not frame and not fitted:
            cell["hollow"] = True
        out.append(cell)
    return out


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    poses = [REST, (0.7, 0.7), (-2.2, 2.2), (4.0, 9.0), (9.3, 9.3)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    span = [(min(c[k] for c in cells), max(c[k] for c in cells)) for k in range(3)]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; {len(cells)} cells, x {span[0]}, "
          f"y {span[1]}, z {span[2]}; power {ship['powerCell']} {ship['powerFace']}, water {ship['waterCell']} {ship['waterFace']}, "
          f"input {ship['inputCell']} {ship['inputFace']}, output {ship['outputCell']} {ship['outputFace']}")
    want = {tuple(shift_cell(c, ORIGIN_CELL)) for c in (POWER_CELL, WATER_CELL, INPUT_CELL, OUTPUT_CELL)}
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
