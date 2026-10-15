#!/usr/bin/env python3
"""Generate the jig's shapes, rig and reference poses.

The jig (#919, model #920, the ore processing epic #684) is a Harz plunger jig, one machine and one
level: a plank box of water (the hutch) divided across by a partition open below it, the sieve
compartment at the front and the plunger compartment behind. An eccentric on the jig's own shaft,
from the vanilla axle at the power face, works the plunger up and down through an iron yoke (a slotted
crosshead, the Scotch yoke's way, so the stroke is exactly a sine of the shaft's angle and the same
either way the axle turns: no rectifier). Each stroke pulses the water up through the sieve and the
ore bed on it: the heavy grains sink through into the hutch, whose bottom slopes to the spout at the front
wall's foot, into the concentrate chute; the light ones are carried along the sieve, from the head end where
the feed lands (east) to the tail end (west), and over the weir in the tail end's wall, into the tailings
chute. Richards 1903, I, Harz jigs, figs. 306-307.

    feed         vanilla's straight chute, upright, down through the front top cell's top face onto a
                 feed trough that carries the ore to the east end of the sieve
    tailings     along the sieve to its tail end and over the weir, an iron lip in a port through the
                 west wall's head (the wall raised round the chute's end), into vanilla's chute out of
                 the front top cell's west face
    concentrate  through the sieve into the hutch, down its sloping bottom into the iron spout through
                 the front wall's foot, off its lip into vanilla's chute out of the front bottom cell's
                 north face
    water        a ppex pipe (soldered copper or lead, Pipes/tools/solderedpipe.py) into the plunger
                 compartment under the plunger, through the back bottom cell's south face
    power        the vanilla axle into the back top cell's west face, on the shaft's oak end

It writes, deterministically,

    jig.json             the whole machine                       (assets/.../shapes/block/)
    jig_frame.json       the hutch only (block and item)         (assets/.../shapes/block/)
    jig-rig.json         cells, anchors and the part rig         (assets/.../config/)
    rig-reference.json   every part's matrix at a spread of poses (tests/Jig/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_jig.py) and exits non-zero
if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
machine box's north-west-bottom corner (the "build frame"). The controller cell is the build frame's
[0,0,0], the front bottom cell nearest the player who placed it (the machine runs south, away from the
player), so the shipped files are the build frame divided by 16.

The rig's input is theta, the axle's angle: the shaft turns with it, the plunger rises and falls once a
turn, and the water and the bed answer it.
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
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Pipes" / "tools"))

from machinegen.checks import cell_boxes, cells_touched, with_lids  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import IDENT, TRANSPARENT, El, flatten, rotate, translate  # noqa: E402
from machinegen.output import (reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               shift_point, worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

import solderedpipe  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "Jig" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/Jig/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

# ---------------------------------------------------------------- the box, the cells, the anchors
CELLS_X, CELLS_Y, CELLS_Z = 1, 2, 2          # one wide, two high, two deep (away from the player)
X_LEN, Y_LEN, Z_LEN = CELLS_X * B, CELLS_Y * B, CELLS_Z * B
ORIGIN_CELL = (0, 0, 0)                      # the controller: the front bottom cell, the hutch's spigot end
# Every service sits on a face of its own, centred in it, so a vanilla axle, chute or ppex pipe placed in
# the block beyond meets it with no gap or step (#711 has not settled the shared anchors yet: these are
# the v2 branches' convention, provisional).
POWER_CELL, POWER_FACE = (0, 1, 1), "west"   # the axle comes in along x at the face's centre (y 24, z 24)
INFEED_CELL, INFEED_FACE = (0, 1, 0), "up"   # a chute above pushes down into the upright infeed section
TAILINGS_CELL, TAILINGS_FACE = (0, 1, 0), "west"        # the weir's chute, out to a chute beyond
CONCENTRATE_CELL, CONCENTRATE_FACE = (0, 0, 0), "north"  # the spigot's chute, out to a chute beyond
WATER_CELL, WATER_FACE = (0, 0, 1), "south"  # a ppex pipe meets the inlet end to end at the face's centre

REQUIRES = ("sieve", "drive", "plunger", "infeed", "outlets")   # the fitted parts (the hutch is the frame)
PIPE_METALS = ("copper", "lead")             # the water inlet's metals: one part each, the supplied one drawn
PIPES = tuple(f"pipe{m}" for m in PIPE_METALS)
CONTENTS = ("water", "bed")                  # what the machine holds while it works (no part of its build)

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",         # timber: sills, standards, beam, bearers, ledges, plunger board
    "planks": "game:block/wood/planks/oak1",       # boards: the hutch's floor, walls and partition, the feed trough
    "iron": "game:block/metal/plate/iron",         # castings and straps: bushes, yoke, boss, spigot, weir lip, brackets
    "steel": "game:block/metal/sheet-plain/steel1",  # the shaft, its collars, the sheave, the rods
    "screen": "game:block/metal/mesh1",            # the sieve
    "chute": "game:block/metal/sheet/copper1",     # vanilla's chute sections, as a placed chute shows in the pack
    "pipecopper": "game:block/metal/sheet-plain/copper4",   # the water inlet in copper (UnifiedPipes' copper pipe)
    "pipelead": "game:block/metal/sheet-plain/lead4",       # ... in lead (UnifiedPipes' lead pipe)
    "solder": solderedpipe.SOLDER_TEXTURE,         # the pipe's solder (none drawn on a single length; kept for its kin)
    "water": "game:block/liquid/waterportion",     # the water in the hutch, as a barrel's water is drawn
    "bed": "game:block/stone/gravel/granite",      # the ore bed on the sieve (provisional: the ore's host rock)
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the hutch (the frame)
HUTCH_X = (2.5, 15.5)                        # outside; the west wall inset 2.5 for the axle's oak end and the
HUTCH_Z = (3.0, 29.0)                        # tailings chute beside it, both on the west face
WALL_T = 1.5                                 # planks 1.5 thick (about 3.5 in)
FLOOR = (4.0, 5.0)                           # the floor's underside and top, on the sills
RIM = 22.5                                   # the walls' tops
SILLS_Z = ((3.0, 5.5), (15.5, 18.0), (26.5, 29.0))   # three oak sills across, under the front wall, the
SILL_X = (2.0, 16.0)                                 # partition and the back wall
PART_Z = (16.5, 17.5)                        # the partition: the sieve compartment north, the plunger's south
PART_Y0 = 10.0                               # it stops this far up: the water passes under it
SIEVE_Y = (17.2, 17.6)                       # the sieve's underside and top, on its ledges: high in the hutch, so
                                             # the bed's top stands a little under the weir (the tail board's top)
LEDGE = 0.8                                  # the ledges under the sieve, round the sieve compartment
IN_X = (HUTCH_X[0] + WALL_T, HUTCH_X[1] - WALL_T)        # the inside: x 4 .. 14
SIEVE_Z = (HUTCH_Z[0] + WALL_T, PART_Z[0])               # the sieve compartment: z 4.5 .. 16.5
PLUNGE_Z = (PART_Z[1], HUTCH_Z[1] - WALL_T)              # the plunger compartment: z 17.5 .. 27.5

# The hutch's ironwork, as a period plank tank had it: an angle-iron strap down each vertical corner, wrapping
# it (a leg on each outer face, the legs meeting at the corner), from the sills to the rim (at the tail end's
# corner up the head, to its top), and a flat corner iron on the rim over each corner the head does not cover,
# lapping the vertical strap's head; every leg nailed to its board.
CSTRAP_T = 0.3                               # the iron's thickness
CSTRAP_W = 0.9                               # each leg's width on its face, from the wall's corner (the tail end's
                                             # corner: clear of the tailings chute's north wall at z 4 by 0.1)
CSTRAP_Y0 = FLOOR[0]                         # from the sills' top (the floor's underside) ...
RIM_IRON = 2.4                               # the rim's corner iron: each arm this long from the corner, outside
NAIL = (0.5, 0.12)                           # a nail head's side and how far it stands proud
NAIL_YS = (FLOOR[0] + 1.5, (FLOOR[0] + RIM) / 2, RIM - 1.5)   # the nails down each vertical leg
CORNERS = {                                  # each vertical corner: its (x, z) and which way its two faces look
    "nw": ((2.5, 3.0), (-1, -1)),
    "ne": ((15.5, 3.0), (1, -1)),
    "sw": ((2.5, 29.0), (-1, 1)),
    "se": ((15.5, 29.0), (1, 1)),
}
HEAD_CORNER = "nw"                           # the corner the tail end's head rises over: its strap goes up the head

# ---------------------------------------------------------------- the vanilla chute sections
VANILLA_CHUTE = {                            # game 1.22.7, shapes/block/metal/chute/straight.json (the "we" shape):
    "walls": ((4.0, 12.0, 4.0, 5.0),         # its walls across the run, (a0, a1, b0, b1) cell-local over the two
              (4.0, 12.0, 11.0, 12.0),       # axes across it, (y, z): two full-width walls, two between them; 1
              (4.0, 5.0, 5.0, 11.0),         # thick, an 8 x 8 tube centred in the block (its block's 4..12), a 6 x 6
              (11.0, 12.0, 5.0, 11.0)),      # bore. Its "ns" variant is the same turned (rotateY 90), its "ud" upright
    "texture": "game:block/metal/sheet/copper1",   # (rotateZ 90); the section is square, so mirroring changes nothing
    "shape": "shapes/block/metal/chute/straight.json",
}
BORE = (5.0, 11.0)                           # the chute's bore, cell-local, across either axis
INFEED_Y0 = 26.2                             # the infeed section: from the top face down to here, over the feed trough
TAIL_X1 = HUTCH_X[0]                         # the tailings section: from the west face to the hutch's west wall
CONC_Z1 = HUTCH_Z[0]                         # the concentrate section: from the north face to the hutch's front wall

# ---------------------------------------------------------------- the feed, the weir, the spigot
TROUGH_X = (4.1, 13.0)                       # the feed trough: its closed west end under the chute, its lip east
TROUGH_Y0 = 24.86                            # its floor's top at the west end; it falls 1 in 10 to the lip
TROUGH_SLOPE = 0.1
TROUGH_T = 0.6                               # its boards
TROUGH_SIDE = 1.2                            # its sides over the floor
TROUGH_Z = (4.4, 11.6)                       # outside; inside 5 .. 11, the chute's bore
BEARERS_X = ((5.6, 7.0), (10.4, 11.8))       # its two oak bearers, from the front wall's rim to the partition's
STRAPS_X = ((5.9, 6.7), (10.7, 11.5))        # the chute's iron straps, down its north and south walls to the bearers
STRAP_T = 0.4
STRAP_TOP = 30.0
WEIR_Y = 21.25                               # the weir's sill (the iron lip's top): the water's level
WEIR_Z = (5.2, 10.8)                         # the notch in the west wall, inside the tailings chute's bore
LIP_X0 = 2.0                                 # the lip reaches half a voxel into the chute's mouth from the wall's
LIP_X1 = HUTCH_X[0] + WALL_T                 # inner face (the water's surface meets it there, at its top)
LIP_T = 0.1
# The tail end: the west wall rises over the rim as a plank head round the tailings chute's end (the chute's
# section reaches 5.5 over the rim), the overflow passing through a port in it over the weir's iron lip.
HEAD_Z = (HUTCH_Z[0], 13.0)                  # the head's width: the front wall's west end to past the chute
HEAD_TOP = 28.0                              # the chute section's top
PORT_TOP = 24.0                              # the port: from the weir's sill up to here, across WEIR_Z
# The concentrate: through the sieve into the hutch, whose bottom under the sieve compartment slopes down to
# the front wall (boards on an oak cleat at the partition); at the front wall's foot a port lined with an iron
# spout, its floor flush with the slope's foot, its lip in the concentrate chute's mouth, over the chute's floor.
HOPPER_Z = (SIEVE_Z[0], PART_Z[0] - 0.1)     # the slope: from the front wall's inner face to the partition
HOPPER_Y = (5.65, 7.65)                      # its top at the front wall and at the partition (1 in 6)
HOPPER_T = 0.6
CLEAT_Z = (HOPPER_Z[1] - 1.0, HOPPER_Z[1])   # the oak cleat under its upper edge
SPIGOT_X = (5.2, 10.8)                       # the spout's outside: the wall's port, inside the chute's bore
SPIGOT_Y = (FLOOR[1], 8.5)                   # from the floor to the port's head
SPIGOT_FLOOR = HOPPER_Y[0]                   # the spout's floor top, flush with the slope's foot
SPIGOT_T = 0.5                               # its sides and head
SPIGOT_Z = (2.4, HUTCH_Z[0] + WALL_T)        # from 0.6 into the chute's mouth to the wall's inner face

# ---------------------------------------------------------------- the drive
SHAFT = (24.0, 24.0)                         # (y, z): the shaft along x, at the power cell's face's centre
SHAFT_R = 0.55                               # its radius (steel, an octagon)
SHAFT_X = (1.8, 15.98)
ENTRY_X = 1.6                                # the axle's oak cross runs from the power face to here
FLANGE_X = (1.6, 2.38)                       # the iron flange the oak end is socketed in, against the west bush
FLANGE_R = 2.2
POSTS_X = ((HUTCH_X[0], IN_X[0]), (IN_X[1], HUTCH_X[1]))   # the two oak standards on the side walls' rims
POST_Z = (22.25, 25.75)
POST_TOP = 30.0
BUSH = 1.1                                   # the iron bushes: 2.2 across about the shaft, proud of the posts by
BUSH_PROUD = 0.1                             # a tenth each side
BEAM_Y = (28.5, 30.0)                        # the guide beam between the standards' heads
BEAM_Z = (23.25, 24.75)
GUIDE_Y = (30.0, 30.6)                       # the iron guide on the beam the tail rod runs in
GUIDE_HALF = 0.7
COLLAR_R = 1.0
COLLAR_W = 0.5
SHEAVE_X = (IN_X[0] + IN_X[1]) / 2           # the eccentric, over the middle of the plunger compartment
SHEAVE_R = 1.25                              # a 12-sided disc, apothem
SHEAVE_RC = SHEAVE_R / math.cos(math.pi / 12)    # its corners: the yoke clears them
SHEAVE_W = 1.2                               # its thickness along the shaft
E = 0.5                                      # its throw: the plunger's stroke is 1.0 (about 2.5 in)
YOKE_T = 0.5                                 # the yoke's bars
YOKE_GAP = 0.3                               # its end bars clear the sheave's swing by this
ROD = 0.9                                    # the plunger rod, square
TAIL = 0.8                                   # the tail rod above the yoke, square
TAIL_TOP = 31.3                              # its top at rest
BOARD_X = (IN_X[0] + 0.4, IN_X[1] - 0.4)     # the plunger board: the compartment less 0.4 all round
BOARD_Z = (PLUNGE_Z[0] + 0.4, PLUNGE_Z[1] - 0.4)
BOARD_Y = (15.5, 17.0)                       # at mid-stroke
BOSS = (1.2, 0.6)                            # the iron boss on it: half across, thick

# ---------------------------------------------------------------- the outlets' supports
TAIL_ARM = (0.6, 19.4, 20.0)                 # an iron bracket under the tailings chute: its arm (x0, y0, y1) ...
TAIL_LEG_Y = 16.5                            # ... and its leg down the wall's outer face to here
TAIL_BRACKET_Z = (7.4, 8.6)
CONC_BLOCK = ((5.0, 11.0), (0.3, 2.7))       # an oak block under the concentrate chute (x, z)

# ---------------------------------------------------------------- the water inlet (ppex pipe, soldered)
PIPE_HALF = solderedpipe.HALF                # 3: ppex's pipe is 6 across
WATER_IN = (8.0, 8.0)                        # (x, y): its axis on the water face's centre
WATER_END_Z = PLUNGE_Z[1] - 0.5              # its open end, half a voxel inside the back wall, under the plunger

# ---------------------------------------------------------------- the contents: water and the ore bed
WATER_T = 0.1                                # the water's surface, drawn as a thin sheet at the weir's level
SIEVE_AREA = (IN_X[1] - IN_X[0]) * (SIEVE_Z[1] - SIEVE_Z[0])
PLUNGE_AREA = (IN_X[1] - IN_X[0]) * (PLUNGE_Z[1] - PLUNGE_Z[0])
BOARD_AREA = (BOARD_X[1] - BOARD_X[0]) * (BOARD_Z[1] - BOARD_Z[0])
# Down the plunger goes and pushes the water under the partition and up through the sieve: the sieve
# compartment's surface rises by the board's displacement over its area, the plunger compartment's falls
# with the board, by its area over the compartment's (the board fits close). A sine of theta, like the stroke.
WATER_SIEVE_E = -E * BOARD_AREA / SIEVE_AREA
WATER_PLUNGE_E = E * BOARD_AREA / PLUNGE_AREA
BED_Y = (SIEVE_Y[1], SIEVE_Y[1] + 2.0)       # the bed on the sieve, two voxels deep at rest
BED_E = -0.15                                # it lifts a little on each pulse up through it, and settles


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


def beam(lo, hi, name, part, tex, seg=16.0):
    """A long box split into pieces of at most `seg` along its longest axis (so no texture stretches)."""
    axis = max(range(3), key=lambda a: hi[a] - lo[a])
    n = max(1, math.ceil((hi[axis] - lo[axis]) / seg - 1e-9))
    out = []
    for i in range(n):
        l2, h2 = list(lo), list(hi)
        l2[axis] = lo[axis] + (hi[axis] - lo[axis]) * i / n
        h2[axis] = lo[axis] + (hi[axis] - lo[axis]) * (i + 1) / n
        out.append(box(l2, h2, f"{name}{i + 1}" if n > 1 else name, part, tex))
    return out


AX = {"x": 0, "y": 1, "z": 2}
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}   # a disc's angle 0 direction, and the in-plane direction it turns towards


def disc(axis, c, a0, a1, r, name, part, tex, k=4, phase=0.0):
    """A plain round part: k strips as long as the 2k-gon is across, 180/k degrees apart, each a hair
    shorter than the last so no two ends share a plane."""
    a = AX[axis]
    u, w = REF[axis]
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


def sloped(axis, a0, a1, y0, y1, pieces, part):
    """Boxes laid on a straight slope along `axis` ("x" or "z") from a0 (height y0) to a1 (height y1),
    either way along the axis. Each piece is (s0, s1, h0, h1, c0, c1, name, tex): s along the slope from
    the a0 end (0 .. its length), h above the slope's line, c across the axis (z for x, x for z). They are
    built level from a0 and turned about the a0 end's line, so a0's end stays where it is."""
    a = AX[axis]
    acr = 2 if axis == "x" else 0
    length = a1 - a0
    dirn = 1.0 if length >= 0 else -1.0
    out = []
    for s0, s1, h0, h1, c0, c1, name, tex in pieces:
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[a], hi[a] = sorted((a0 + dirn * s0, a0 + dirn * s1))
        lo[1], hi[1] = y0 + h0, y0 + h1
        lo[acr], hi[acr] = c0, c1
        out.append(box(lo, hi, name, part, tex))
    target = math.atan2(y1 - y0, abs(length))
    if abs(target) > 1e-12:
        if axis == "x":
            rotate(out, "z", math.degrees(dirn * target), (a0, y0, 0.0))
        else:
            rotate(out, "x", -math.degrees(dirn * target), (0.0, y0, a0))
    return out


def slope_len(a0, a1, y0, y1):
    return math.hypot(a1 - a0, y1 - y0)


def floor_y(y0, y1, a0, a1, at):
    """A trough's floor top at `at` along its axis (its floor's top runs straight from y0 at a0 to y1 at a1)."""
    return y0 + (y1 - y0) * (at - a0) / (a1 - a0)


def under_slope(y0, y1, a0, a1, lo, hi, t):
    """The lowest point of a sloped board's underside over lo..hi along its axis (its top runs from y0 at
    a0 to y1 at a1, it is t thick), less a hundredth: where a block under it stops."""
    ang = math.atan2(y1 - y0, abs(a1 - a0))
    return min(floor_y(y0, y1, a0, a1, lo), floor_y(y0, y1, a0, a1, hi)) - t / math.cos(ang) - 0.01


def yoke(x, c, e, name, part):
    """The iron yoke a sheave of throw e runs in, about the shaft through c = (y, z), at rest: a horizontal
    slot, its top and bottom bars on the sheave (the yoke follows the sheave up and down), clearing its
    corners by a hundredth; its end bars clear the sheave's swing across by YOKE_GAP."""
    y, z = c
    t = YOKE_T
    x0, x1 = x - SHEAVE_W / 2 - 0.15, x + SHEAVE_W / 2 + 0.15
    rc = SHEAVE_RC + 0.01
    far = e + rc + YOKE_GAP
    return [box([x0, y + rc, z - far - t], [x1, y + rc + t, z + far + t], f"{name}_top", part, "iron"),
            box([x0, y - rc - t, z - far - t], [x1, y - rc, z + far + t], f"{name}_bottom", part, "iron"),
            box([x0, y - rc, z - far - t], [x1, y + rc, z - far], f"{name}_n", part, "iron"),
            box([x0, y - rc, z + far], [x1, y + rc, z + far + t], f"{name}_s", part, "iron")]


def yoke_bottom():
    """The underside of the yoke's bottom bar at rest."""
    return SHAFT[0] - SHEAVE_RC - 0.01 - YOKE_T


def yoke_top():
    """The top of the yoke's top bar at rest."""
    return SHAFT[0] + SHEAVE_RC + 0.01 + YOKE_T


# ---------------------------------------------------------------- the vanilla chute
def chute(axis, cell, a0, a1, name, part):
    """A section of vanilla's straight chute along `axis` ("x", "y" or "z") in `cell`, from a0 to a1 (build
    frame) along it: VANILLA_CHUTE's four walls, two full-width and two between, its section exactly the
    game's, centred in the cell's face, in the chute's texture. Each wall is named for the side it faces."""
    a = AX[axis]
    u, v = CHUTE_ACROSS[a]
    out = []
    for nm, (p0, p1, q0, q1) in zip(CHUTE_WALL_NAMES[a], chute_walls(axis)):
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[a], hi[a] = a0, a1
        lo[u], hi[u] = cell[u] * B + p0, cell[u] * B + p1
        lo[v], hi[v] = cell[v] * B + q0, cell[v] * B + q1
        out.append(box(lo, hi, f"{name}_{nm}", part, "chute"))
    return out


# The two axes across a chute's run, in the order VANILLA_CHUTE's (a, b) pairs are over for it: the "we"
# shape's own (y, z); turned for "ns" (along z, rotateY 90) its z becomes x, so (y, x); turned upright for "ud"
# (along y, rotateZ 90) its y becomes x, mirrored, so (x, z). And each wall's name, by the side it faces.
CHUTE_ACROSS = {0: (1, 2), 2: (1, 0), 1: (0, 2)}
CHUTE_WALL_NAMES = {0: ("n", "s", "bottom", "top"), 2: ("w", "e", "bottom", "top"), 1: ("n", "s", "e", "w")}


def chute_walls(axis):
    """VANILLA_CHUTE's walls as a section along `axis` has them, in VANILLA_CHUTE's order, cell-local over
    CHUTE_ACROSS: the "we" and "ns" shapes' are the walls as they are (the square section does not mind
    rotateY's mirror); the "ud" shape's are its (y, z) with y mirrored to x (rotateZ 90)."""
    if axis == "y":
        return [(16.0 - a1, 16.0 - a0, b0, b1) for a0, a1, b0, b1 in VANILLA_CHUTE["walls"]]
    return list(VANILLA_CHUTE["walls"])


def chute_profile(els, prefix, axis, cell):
    """A chute section's walls as built, cell-local over CHUTE_ACROSS for its run, sorted; with the spans of
    its run."""
    a = AX[axis]
    u, v = CHUTE_ACROSS[a]
    out, runs = [], set()
    for el in els:
        if not el.name.startswith(prefix):
            continue
        lo, hi = el.aabb()
        out.append(tuple(round(x, 4) for x in (lo[u] - cell[u] * B, hi[u] - cell[u] * B, lo[v] - cell[v] * B, hi[v] - cell[v] * B)))
        runs.add((round(lo[a], 4), round(hi[a], 4)))
    return sorted(out), runs


def read_vanilla_chute():
    """The game's own straight chute, if the game is here ($VINTAGE_STORY or ~/Games/vintagestory): its
    walls' sections over (y, z), its run and its texture, or None. The generator holds VANILLA_CHUTE to it."""
    import os
    for base in (os.environ.get("VINTAGE_STORY"), str(Path.home() / "Games" / "vintagestory")):
        if not base:
            continue
        f = Path(base) / "assets" / "survival" / VANILLA_CHUTE["shape"]
        if f.exists():
            shape = json.loads(f.read_text())
            walls = sorted((e["from"][1], e["to"][1], e["from"][2], e["to"][2]) for e in shape["elements"])
            runs = {(e["from"][0], e["to"][0]) for e in shape["elements"]}
            tex = "game:" + next(iter(shape["textures"].values()))
            return walls, runs, tex
    return None


# ---------------------------------------------------------------- builders: the hutch (frame)
def build_hutch():
    """The hutch: three oak sills across, a plank floor on them, four plank walls to the rim (the west wall,
    the tail end, notched for the weir, an iron lip on its sill reaching from its inner face into the tailings
    chute's mouth, and raised over the rim as a head round the chute's end, the port through it over the
    lip), the partition across, open under it, the ledges the sieve lies on, the bottom under the sieve
    compartment sloping down to the front wall on an oak cleat, and the spout, an iron lining in a port
    through the front wall's foot, its floor flush with the slope's foot and its lip in the concentrate
    chute's mouth."""
    f = "frame"
    x0, x1 = HUTCH_X
    z0, z1 = HUTCH_Z
    t = WALL_T
    out = []
    for i, (sz0, sz1) in enumerate(SILLS_Z, 1):
        out.append(box([SILL_X[0], 0.0, sz0], [SILL_X[1], FLOOR[0], sz1], f"hutch_sill{i}", f, "oak"))
    out += beam([x0, FLOOR[0], z0], [x1, FLOOR[1], z1], "hutch_floor", f, "planks")
    y0 = FLOOR[1]
    # the front wall, round the spout's port at its foot
    sx0, sx1 = SPIGOT_X
    py1 = SPIGOT_Y[1]
    out.append(box([x0, y0, z0], [sx0, py1, z0 + t], "hutch_wall_nw", f, "planks"))
    out.append(box([sx1, y0, z0], [x1, py1, z0 + t], "hutch_wall_ne", f, "planks"))
    out.append(box([x0, py1, z0], [x1, RIM, z0 + t], "hutch_wall_n", f, "planks"))
    out.append(box([x0, y0, z1 - t], [x1, RIM, z1], "hutch_wall_s", f, "planks"))
    out += beam([x1 - t, y0, z0 + t], [x1, RIM, z1 - t], "hutch_wall_e", f, "planks")
    # the west wall: under the weir's sill its whole length, then beside the notch to the rim
    wz0, wz1 = WEIR_Z
    sill = WEIR_Y - LIP_T
    out += beam([x0, y0, z0 + t], [x0 + t, sill, z1 - t], "hutch_wall_w", f, "planks")
    out.append(box([x0, sill, z0 + t], [x0 + t, RIM, wz0], "hutch_wall_wn", f, "planks"))
    out.append(box([x0, sill, wz1], [x0 + t, RIM, z1 - t], "hutch_wall_ws", f, "planks"))
    out.append(box([LIP_X0, sill, wz0], [LIP_X1, WEIR_Y, wz1], "hutch_weir_lip", f, "iron"))
    # the head over the tail end: two cheeks beside the port on the walls' rims, its lintel over the port
    hz0, hz1 = HEAD_Z
    out.append(box([x0, RIM, hz0], [x0 + t, HEAD_TOP, wz0], "hutch_head_n", f, "planks"))
    out.append(box([x0, RIM, wz1], [x0 + t, HEAD_TOP, hz1], "hutch_head_s", f, "planks"))
    out.append(box([x0, PORT_TOP, wz0], [x0 + t, HEAD_TOP, wz1], "hutch_head_lintel", f, "planks"))
    # the partition, open below
    out.append(box([IN_X[0], PART_Y0, PART_Z[0]], [IN_X[1], RIM, PART_Z[1]], "hutch_partition", f, "planks"))
    # the sieve's ledges round the sieve compartment
    ly0, ly1 = SIEVE_Y[0] - LEDGE, SIEVE_Y[0]
    sz0, sz1 = SIEVE_Z
    out += [box([IN_X[0], ly0, sz0], [IN_X[0] + LEDGE, ly1, sz1], "hutch_ledge_w", f, "oak"),
            box([IN_X[1] - LEDGE, ly0, sz0], [IN_X[1], ly1, sz1], "hutch_ledge_e", f, "oak"),
            box([IN_X[0] + LEDGE, ly0, sz0], [IN_X[1] - LEDGE, ly1, sz0 + LEDGE], "hutch_ledge_n", f, "oak"),
            box([IN_X[0] + LEDGE, ly0, sz1 - LEDGE], [IN_X[1] - LEDGE, ly1, sz1], "hutch_ledge_s", f, "oak")]
    # the hutch's bottom under the sieve compartment, sloping down to the spout; the cleat under its upper edge
    hz0, hz1 = HOPPER_Z
    hy0, hy1 = HOPPER_Y
    run = slope_len(hz1, hz0, hy1, hy0)
    out += sloped("z", hz1, hz0, hy1, hy0, [(0.0, run, -HOPPER_T, 0.0, IN_X[0], IN_X[1], "hutch_hopper", "planks")], f)
    cz0, cz1 = CLEAT_Z
    out.append(box([IN_X[0], y0, cz0], [IN_X[1], under_slope(hy1, hy0, hz1, hz0, cz0, cz1, HOPPER_T), cz1], "hutch_hopper_cleat", f, "oak"))
    # the spout: an iron lining in the front wall's port, its floor flush with the slope's foot, its lip in the
    # concentrate chute's mouth, over the chute's floor
    sz0, sz1 = SPIGOT_Z
    st = SPIGOT_T
    out.append(box([sx0, y0, sz0], [sx1, SPIGOT_FLOOR, sz1], "hutch_spigot_bottom", f, "iron"))
    out.append(box([sx0, SPIGOT_FLOOR, sz0], [sx0 + st, py1 - st, sz1], "hutch_spigot_w", f, "iron"))
    out.append(box([sx1 - st, SPIGOT_FLOOR, sz0], [sx1, py1 - st, sz1], "hutch_spigot_e", f, "iron"))
    out.append(box([sx0, py1 - st, sz0], [sx1, py1, sz1], "hutch_spigot_top", f, "iron"))
    return out + build_corner_straps()


def build_corner_straps():
    """The hutch's corner irons (in the frame, `hutch_strap_*`): an angle-iron strap down each vertical corner,
    a leg on each outer face, the face across x (north or south) lapping the other's edge so the two meet at
    the corner in an L; from the sills' top to the rim, or up the head to its top at the tail end's corner,
    whose rim corner the head covers. On each other corner a flat L of iron on the rim, an arm along each wall
    and lapping the vertical strap's head. Nail heads on every leg and arm."""
    f = "frame"
    t, w = CSTRAP_T, CSTRAP_W
    ns, proud = NAIL
    out = []

    def nail(name, c, axis, sign):
        lo, hi = [c[k] - ns / 2 for k in range(3)], [c[k] + ns / 2 for k in range(3)]
        lo[axis], hi[axis] = (c[axis], c[axis] + proud) if sign > 0 else (c[axis] - proud, c[axis])
        return box(lo, hi, name, f, "iron")

    for tag, ((xc, zc), (sx, sz)) in CORNERS.items():
        top = HEAD_TOP if tag == HEAD_CORNER else RIM
        # the leg on the face across x (north or south): from past the corner by the other leg's thickness
        xa, xb = sorted((xc + sx * t, xc - sx * w))
        za, zb = sorted((zc, zc + sz * t))
        out.append(box([xa, CSTRAP_Y0, za], [xb, top, zb], f"hutch_strap_{tag}_x", f, "iron"))
        # the leg on the face across z (west or east): from the corner along the wall
        xa2, xb2 = sorted((xc, xc + sx * t))
        za2, zb2 = sorted((zc, zc - sz * w))
        out.append(box([xa2, CSTRAP_Y0, za2], [xb2, top, zb2], f"hutch_strap_{tag}_z", f, "iron"))
        ys = NAIL_YS + ((HEAD_TOP - 1.5,) if tag == HEAD_CORNER else ())
        for i, y in enumerate(ys, 1):
            out.append(nail(f"hutch_strap_{tag}_xnail{i}", [xc - sx * w / 2, y, zc + sz * t], 2, sz))
            out.append(nail(f"hutch_strap_{tag}_znail{i}", [xc + sx * t, y, zc - sz * w / 2], 0, sx))
        if tag == HEAD_CORNER:
            continue
        # the rim's corner iron: an arm along each wall's rim from past the vertical strap's outside
        ya, yb = RIM, RIM + t
        x_out, z_out = xc + sx * t, zc + sz * t
        xa, xb = sorted((x_out, xc - sx * RIM_IRON))
        za, zb = sorted((z_out, zc - sz * WALL_T))
        out.append(box([xa, ya, za], [xb, yb, zb], f"hutch_strap_{tag}_rimx", f, "iron"))
        xa, xb = sorted((x_out, xc - sx * WALL_T))
        za, zb = sorted((zc - sz * WALL_T, zc - sz * RIM_IRON))
        out.append(box([xa, ya, za], [xb, yb, zb], f"hutch_strap_{tag}_rimz", f, "iron"))
        out.append(nail(f"hutch_strap_{tag}_rimxnail", [xc - sx * (RIM_IRON + WALL_T) / 2, yb, zc - sz * WALL_T / 2], 1, 1))
        out.append(nail(f"hutch_strap_{tag}_rimznail", [xc - sx * WALL_T / 2, yb, zc - sz * (RIM_IRON + WALL_T) / 2], 1, 1))
    return out


def build_sieve():
    """The sieve (`sieve`): an iron mesh across the sieve compartment, on its ledges."""
    sz0, sz1 = SIEVE_Z
    return [box([IN_X[0], SIEVE_Y[0], sz0], [IN_X[1], SIEVE_Y[1], sz1], "sieve_mesh", "sieve", "screen")]


# ---------------------------------------------------------------- builders: the drive and the plunger
SHAFT_C = (0.0, SHAFT[0], SHAFT[1])


def build_shaft():
    """The shaft (`shaft`, turning with the axle): the axle's oak cross at the power face (the vanilla axle's
    own profile, 4 x 2 and 2 x 4), socketed in an iron flange, the steel shaft through both bushes, and three
    collars: inside the west bush (the flange is outside it) and either side of the east one."""
    p = "shaft"
    y, z = SHAFT
    out = [box([0.0, y - 1.0, z - 2.0], [ENTRY_X, y + 1.0, z + 2.0], "shaft_entrya", p, "oak"),
           box([0.01, y - 2.0, z - 1.0], [ENTRY_X - 0.01, y + 2.0, z + 1.0], "shaft_entryb", p, "oak")]
    out += disc("x", SHAFT_C, FLANGE_X[0], FLANGE_X[1], FLANGE_R, "shaft_flange", p, "iron", k=6)
    out += disc("x", SHAFT_C, SHAFT_X[0], SHAFT_X[1], SHAFT_R, "shaft_rod", p, "steel")
    bw, be = bushes()
    for i, (a0, a1) in enumerate(((bw[1] + 0.02, bw[1] + 0.02 + COLLAR_W), (be[0] - 0.02 - COLLAR_W, be[0] - 0.02),
                                  (be[1] + 0.02, SHAFT_X[1])), 1):
        out += disc("x", SHAFT_C, a0, a1, COLLAR_R, f"shaft_collar{i}", p, "steel", k=6)
    return out


def bushes():
    """The two bushes' spans along x: each its standard's thickness and a tenth proud either side."""
    return [(x0 - BUSH_PROUD, x1 + BUSH_PROUD) for x0, x1 in POSTS_X]


def build_standards():
    """The standards (`stand`, static, fitted with the shaft): an oak post on each side wall's rim over the
    plunger compartment, an iron bush in each carrying the shaft, iron straps down each post's inner face
    onto its wall, and the guide beam across between their heads with the iron guide the plunger's tail rod
    runs in."""
    p = "stand"
    y, z = SHAFT
    out = []
    for tag, (x0, x1), (b0, b1) in zip("we", POSTS_X, bushes()):
        out.append(box([x0, RIM, POST_Z[0]], [x1, POST_TOP, POST_Z[1]], f"stand_post_{tag}", p, "oak"))
        out.append(box([b0, y - BUSH, z - BUSH], [b1, y + BUSH, z + BUSH], f"stand_bush_{tag}", p, "iron"))
        sx = (x1, x1 + 0.3) if tag == "w" else (x0 - 0.3, x0)
        for i, (sz0, sz1) in enumerate(((POST_Z[0] + 0.1, POST_Z[0] + 0.7), (POST_Z[1] - 0.7, POST_Z[1] - 0.1)), 1):
            out.append(box([sx[0], RIM - 3.0, sz0], [sx[1], RIM + 3.0, sz1], f"stand_strap_{tag}{i}", p, "iron"))
    out.append(box([POSTS_X[0][1], BEAM_Y[0], BEAM_Z[0]], [POSTS_X[1][0], BEAM_Y[1], BEAM_Z[1]], "stand_beam", p, "oak"))
    g = GUIDE_HALF
    out.append(box([SHEAVE_X - g, GUIDE_Y[0], z - g], [SHEAVE_X + g, GUIDE_Y[1], z + g], "stand_guide", p, "iron"))
    return out


def build_sheave():
    """The eccentric sheave (`sheave`), keyed on the shaft: a 12-sided steel disc, its centre E north of the
    shaft's at rest (theta 0), so as the shaft turns the centre rises and falls by E sin theta."""
    sc = (0.0, SHAFT[0], SHAFT[1] - E)
    return disc("x", sc, SHEAVE_X - SHEAVE_W / 2, SHEAVE_X + SHEAVE_W / 2, SHEAVE_R, "sheave", "sheave", "steel", k=6)


def build_plunger():
    """The plunger (`plunger`, one rigid part): the yoke round the sheave, the tail rod up from its top bar
    through the guide on the beam, the plunger rod down from its bottom bar to an iron boss on the oak
    plunger board, which nearly fills the plunger compartment."""
    p = "plunger"
    out = yoke(SHEAVE_X, SHAFT, E, "plunger_yoke", p)
    xc, zc = SHEAVE_X, SHAFT[1]
    out.append(box([xc - TAIL / 2, yoke_top() - 0.3, zc - TAIL / 2], [xc + TAIL / 2, TAIL_TOP, zc + TAIL / 2], "plunger_tail", p, "steel"))
    by0, by1 = BOARD_Y
    out.append(box([BOARD_X[0], by0, BOARD_Z[0]], [BOARD_X[1], by1, BOARD_Z[1]], "plunger_board", p, "oak"))
    bh, bt = BOSS
    out.append(box([xc - bh, by1, zc - bh], [xc + bh, by1 + bt, zc + bh], "plunger_boss", p, "iron"))
    out.append(box([xc - ROD / 2, by1 + bt, zc - ROD / 2], [xc + ROD / 2, yoke_bottom() + 0.3, zc + ROD / 2], "plunger_rod", p, "steel"))
    return out


# ---------------------------------------------------------------- builders: the feed and the outlets
def trough_floor(x):
    return TROUGH_Y0 - (x - TROUGH_X[0]) * TROUGH_SLOPE


def build_infeed():
    """The infeed (`infeed`): vanilla's straight chute, upright, from the front top cell's top face down to
    just over the feed trough, so a chute placed in the cell above meets it with no gap or step; the feed
    trough under it, closed at its west end and falling east, its lip over the east end of the sieve; the two
    oak bearers the trough lies on, from the front wall's rim to the partition's; and four iron straps
    carrying the chute down its north and south walls onto the bearers."""
    p = "infeed"
    top = (INFEED_CELL[1] + 1) * B
    out = chute("y", INFEED_CELL, INFEED_Y0, top, "infeed_chute", p)
    a0, a1 = TROUGH_X
    y0, y1 = trough_floor(a0), trough_floor(a1)
    run = slope_len(a0, a1, y0, y1)
    t = TROUGH_T
    zc0, zc1 = TROUGH_Z
    pieces = [(0.0, run, -t, 0.0, zc0 + t, zc1 - t, "infeed_trough_floor", "planks"),
              (0.0, run, -t, TROUGH_SIDE, zc0, zc0 + t, "infeed_trough_n", "planks"),
              (0.0, run, -t, TROUGH_SIDE, zc1 - t, zc1, "infeed_trough_s", "planks"),
              (0.0, t, 0.0, TROUGH_SIDE, zc0 + t, zc1 - t, "infeed_trough_end", "planks")]
    out += sloped("x", a0, a1, y0, y1, pieces, p)
    for i, (bx0, bx1) in enumerate(BEARERS_X, 1):
        top_y = under_slope(y0, y1, a0, a1, bx0, bx1, t)
        out.append(box([bx0, RIM, HUTCH_Z[0]], [bx1, top_y, PART_Z[1]], f"infeed_bearer{i}", p, "oak"))
    for i, ((sx0, sx1), (bx0, bx1)) in enumerate(zip(STRAPS_X, BEARERS_X), 1):
        top_y = under_slope(y0, y1, a0, a1, bx0, bx1, t)
        cz0, cz1 = INFEED_CELL[2] * B + 4.0, INFEED_CELL[2] * B + 12.0
        out.append(box([sx0, top_y, cz0 - STRAP_T], [sx1, STRAP_TOP, cz0], f"infeed_strap{i}n", p, "iron"))
        out.append(box([sx0, top_y, cz1], [sx1, STRAP_TOP, cz1 + STRAP_T], f"infeed_strap{i}s", p, "iron"))
    return out


def build_outlets():
    """The outlets (`outlets`): vanilla's straight chute out of the front top cell's west face from the weir,
    on an iron bracket from the hutch's west wall, and out of the front bottom cell's north face from the
    spigot, on an oak block; each its section exactly the game's, flush with its face."""
    p = "outlets"
    out = chute("x", TAILINGS_CELL, 0.0, TAIL_X1, "outlet_tailchute", p)
    ax0, ay0, ay1 = TAIL_ARM
    bz0, bz1 = TAIL_BRACKET_Z
    out.append(box([ax0, ay0, bz0], [TAIL_X1, ay1, bz1], "outlet_tail_arm", p, "iron"))
    out.append(box([TAIL_X1 - 0.5, TAIL_LEG_Y, bz0], [TAIL_X1, ay0, bz1], "outlet_tail_leg", p, "iron"))
    out += chute("z", CONCENTRATE_CELL, 0.0, CONC_Z1, "outlet_concchute", p)
    (cx0, cx1), (cz0, cz1) = CONC_BLOCK
    out.append(box([cx0, 0.0, cz0], [cx1, CONCENTRATE_CELL[1] * B + 4.0, cz1], "outlet_conc_block", p, "oak"))
    return out


# ---------------------------------------------------------------- builders: the water inlet (soldered pipe)
def pipe_boxes():
    """The water inlet as UnifiedPipes' soldered pipe (solderedpipe's boxes, build-frame voxels): one straight
    length from the water face, on the axis of the ppex pipe beyond it, through the back wall into the
    plunger compartment under the plunger, its end open there. As in the rosser's drip, it has no joint at
    the face: the pipe beyond brings its own."""
    x, y = WATER_IN
    out = solderedpipe.tube("inlet", 2, WATER_END_Z, Z_LEN, (x, y))
    return solderedpipe.cull_pressed(out)


def build_pipes(metal_=PIPE_METALS[0]):
    """The water inlet as elements of the part `pipe<metal_>` (`pipe_boxes`): the body in that metal's pipe
    texture, joints in solder, each face's UVs solderedpipe's on this model's texture scale. Built in
    copper; `pipe_copies` makes lead."""
    p = f"pipe{metal_}"
    codes = {solderedpipe.BODY: f"#{p}", solderedpipe.SOLDER: "#solder"}
    out = []
    for b in pipe_boxes():
        size = b.size()
        faces = {d: {"texture": codes[b.role], "uv": solderedpipe.uv(d, size, TEX / 16)} for d in b.faces}
        out.append(El(f"{p}_{b.name}", size, [(b.lo[k] + b.hi[k]) / 2 for k in range(3)], [row[:] for row in IDENT], faces, p))
    return out


def pipe_copies(els):
    """The water inlet in the other metals: the first metal's elements as they are (after the z-fighting
    fix, so every copy has the same insets), renamed and in their own metal's texture. Only one metal's are
    ever drawn, so the copies are not checked against each other."""
    first = PIPES[0]
    out = []
    for m in PIPE_METALS[1:]:
        for el in els:
            if el.part == first:
                c = el.clone(f"pipe{m}" + el.name[len(first):], f"pipe{m}")
                for face in c.faces.values():
                    if face["texture"] == f"#{first}":
                        face["texture"] = f"#pipe{m}"
                out.append(c)
    return out


# ---------------------------------------------------------------- builders: the contents
def build_contents():
    """The water (`water`): its surface in each compartment, a thin sheet at the weir's level from wall to
    wall, drawn in the transparent pass; and the ore bed (`bed`) on the sieve. The sieve compartment's
    surface and the bed are in the part `watersieve` and `bed`, the plunger compartment's in
    `waterplunger`: each moves with the stroke (`WATER_SIEVE_E`, `WATER_PLUNGE_E`, `BED_E`)."""
    out = []
    y = WEIR_Y
    for pid, (z0, z1) in (("watersieve", SIEVE_Z), ("waterplunger", PLUNGE_Z)):
        el = box([IN_X[0], y - WATER_T, z0], [IN_X[1], y, z1], f"{pid}_surface", pid, "water")
        el.render_pass = TRANSPARENT
        out.append(el)
    out.append(box([IN_X[0], BED_Y[0], SIEVE_Z[0]], [IN_X[1], BED_Y[1], SIEVE_Z[1]], "bed_ore", "bed", "bed"))
    return out


def build():
    return (build_shaft() + build_sheave() + build_plunger() + build_contents() + build_standards() + build_sieve()
            + build_infeed() + build_outlets() + build_pipes() + build_hutch())


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def rot(axis, c, ratio):
    """A rotate about `axis` through c (x, y, z voxels) by ratio x theta."""
    return {"type": "rotate", "axis": axis, "pivot": pt(*c), "ratio": r6(ratio)}


def slide(axis, amplitude):
    return {"type": "slide", "axis": axis, "amplitude": r6(amplitude / B), "ratio": 1.0}


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    parts = [
        # the shaft turns with the axle; the sheave is keyed on it
        {"id": "shaft", "match": ["shaft_*"], "requires": "drive", "drivers": [rot("x", SHAFT_C, 1.0)]},
        {"id": "sheave", "match": ["sheave_*"], "requires": "plunger", "ride": "shaft", "drivers": []},
        # the yoke follows the sheave's centre exactly, up and down: the plunger's stroke, E sin theta
        {"id": "plunger", "match": ["plunger_*"], "requires": "plunger", "drivers": [slide("y", E)]},
        # the water answers the plunger: up through the sieve as it goes down, and the bed lifts on the pulse
        {"id": "watersieve", "match": ["watersieve_*"], "requires": "water", "drivers": [slide("y", WATER_SIEVE_E)]},
        {"id": "waterplunger", "match": ["waterplunger_*"], "requires": "water", "drivers": [slide("y", WATER_PLUNGE_E)]},
        {"id": "bed", "match": ["bed_*"], "requires": "bed", "drivers": [slide("y", BED_E)]},
        # the fitted parts that stand still, and the hutch
        {"id": "stand", "match": ["stand_*"], "requires": "drive", "drivers": []},
        {"id": "sieve", "match": ["sieve_*"], "requires": "sieve", "drivers": []},
        {"id": "infeed", "match": ["infeed_*"], "requires": "infeed", "drivers": []},
        {"id": "outlets", "match": ["outlet_*"], "requires": "outlets", "drivers": []},
        # the water inlet, one part per metal: only the supplied metal's is drawn
        *[{"id": pid, "match": [f"{pid}_*"], "requires": pid, "drivers": []} for pid in PIPES],
        {"id": "frame", "match": ["hutch_*"], "requires": None, "drivers": []},
    ]
    out = []
    for p in parts:
        for d in p["drivers"]:
            validate_driver(d)
        out.append({"id": p["id"], "match": p["match"], "requires": p["requires"], "drivers": p["drivers"],
                    "ride": p.get("ride")})
    return out


MOVING = ("shaft", "sheave", "plunger", "watersieve", "waterplunger", "bed")

# ---------------------------------------------------------------- poses and states
REST = (0.0,)                                 # a pose is (theta,) or (theta, state)


def inputs_of(pose):
    return {"theta": pose[0], "travel": abs(pose[0])}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path)


COMPLETE = REQUIRES + (PIPES[0],)            # the finished machine, its inlet in copper (lead is the same elements)
STATES = {                                    # what is fitted in each state checked
    "frame": (),
    "complete": COMPLETE,                    # the finished machine, dry and empty
    "water": COMPLETE + ("water",),
    "working": COMPLETE + CONTENTS,          # running: water in it and the bed on the sieve
    # each fitting on the hutch alone, with what it stands on: nothing floats whatever is fitted first
    "sieve": ("sieve",),
    "drive": ("drive",),
    "plunger": ("drive", "plunger"),
    "infeed": ("infeed",),
    "outlets": ("outlets",),
    "pipe": (PIPES[0],),
    "bed": ("sieve", "bed"),
}
CELL_STATE = "complete"                      # the cells' boxes are the finished machine's, at rest


def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


POINTS = ("sieve",)                          # point anchors (moved by the origin shift)
CELL_KEYS = ("powerCell", "infeedCell", "tailingsCell", "concentrateCell", "waterCell")


def make_rig(parts):
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the front bottom "
                    "cell, nearest the player who placed it; the jig runs south from it. A Harz plunger jig: the sieve "
                    "compartment at the front, the plunger compartment behind. theta is the axle: the shaft turns "
                    "with it and the plunger rises and falls once a turn (an eccentric in a yoke, the same either way "
                    "the axle turns). Every service is on its own face, centred: the axle (powerCell/powerFace), "
                    "the infeed chute (infeedCell/infeedFace, from above), the tailings and concentrate chutes "
                    "(tailingsCell/tailingsFace, concentrateCell/concentrateFace, out to a chute beyond) and the "
                    "ppex water pipe (waterCell/waterFace). water and bed are what the machine holds while it "
                    "works, not parts of its build. See the jig's README for the schema.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "infeedCell": list(INFEED_CELL),
        "infeedFace": INFEED_FACE,
        "tailingsCell": list(TAILINGS_CELL),
        "tailingsFace": TAILINGS_FACE,
        "concentrateCell": list(CONCENTRATE_CELL),
        "concentrateFace": CONCENTRATE_FACE,
        "waterCell": list(WATER_CELL),
        "waterFace": WATER_FACE,
        "sieve": {"pos": pt((IN_X[0] + IN_X[1]) / 2, SIEVE_Y[1], (SIEVE_Z[0] + SIEVE_Z[1]) / 2)},
        "levels": {"weirY": r6(WEIR_Y / B), "sieveY": r6(SIEVE_Y[1] / B), "rimY": r6(RIM / B),
                   "_comment": "Heights in blocks: the weir's sill (the water's level), the sieve's top, the hutch's rim."},
        "pipes": {"requires": {m: f"pipe{m}" for m in PIPE_METALS},
                  "_comment": "The water inlet: ppex's straight pipe, soldered, one part per metal (the same elements); "
                              "only the supplied metal's is drawn."},
        "contents": {"water": "water", "bed": "bed",
                     "_comment": "requires values for what the jig holds: water (its surface in each compartment) while "
                                 "its reservoir has water, the ore bed on the sieve while it works."},
        "drive": {"strokesPerTurn": 1, "stroke": r6(2 * E / B),
                  "_comment": "The plunger's strokes per turn of the axle (the eccentric is keyed on the shaft, which "
                              "turns with the axle) and its stroke in blocks, either way the axle turns."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: the build frame's
    corner is the controller's)."""
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
    for key in CELL_KEYS:
        ship[key] = shift_cell(rig[key], ORIGIN_CELL)
    for key in POINTS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig: the finished
    machine's (CELL_STATE: every fitted part, its inlet in copper, no contents); then a lid over each
    column's top cell."""
    fitted = set(STATES[CELL_STATE])
    req = {p["id"]: p["requires"] for p in ship_parts}
    by_cell = {}
    for w in flatten(shape["elements"], textures={}):
        pid = part_of(ship_parts, w.name)
        if req[pid] is not None and req[pid] not in fitted:
            continue
        el = posed(w, _part_matrix(ship_parts, pid, inputs_of(REST)))
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, []).append(el)
    out = []
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        out.append({"pos": list(pos), "boxes": cell_boxes(by_cell[pos], pos)})
    return with_lids(out)


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    poses = [REST, (1.3,), (-2.2,), (4.9,), (9.1,)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; {len(cells)} cells")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
def reference_poses():
    return (0.0, 0.7, -1.9, 2.6, math.pi / 2, -math.pi / 2, 4.1, -5.3, 11.05, 100.0)


def reference_json(ship_parts):
    poses = []
    for th in reference_poses():
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], {"theta": th})) for q in ship_parts}
        poses.append({"theta": r6(th), "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped jig-rig.json's parts: each part's matrix as 3 rows of 4 "
                        "(block units) at each pose (theta, the axle's angle, radians). The site's and the mod's tests "
                        "check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. The hutch (hutch_*) and the "
             "fitted parts (sieve_: the sieve; shaft_ / stand_: the shaft and its standards; sheave_ / plunger_: the "
             "eccentric and the plunger; infeed_: the infeed chute and feed trough; outlet_: the tailings and "
             "concentrate chutes; pipecopper_ / pipelead_: the water inlet in each metal), and what it holds "
             "(watersieve_ / waterplunger_: the water; bed_: the ore bed). Keep element names when editing: the rig "
             "finds its parts by them.",
        TEXTURES, tex_size=TEX)


def state_of(pose):
    return pose[1] if len(pose) > 1 else "all"


def on_show(requires, state):
    """Whether a part needing `requires` shows in a state ("all": every part at once)."""
    return requires is None or state == "all" or requires in STATES[state]


def shown(posed_els, pose, req_of):
    """The posed elements as they can be seen in the pose's state: parts not fitted moved far away (copies;
    the order kept), so the z-fighting fix and its check deal only with faces that show together."""
    state = state_of(pose)
    out = []
    for e in posed_els:
        if not on_show(req_of[e.part], state):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def coplanar_poses():
    out = []
    for state in ("working", "complete"):
        for th in (0.0, 0.9, 2.3, 4.0, 5.1):
            out.append((th, state))
    return tuple(out)


def fix_coplanar(els, parts):
    req_of = {p["id"]: p["requires"] for p in parts}
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose, req_of),
                              coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the jig's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_jig
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        n = sum(len(v) for v in before.values())
        print(f"coplanar faces before the fix: {n} pairs over {len(before)} poses; {hidden} faces pressed against their own part removed")
    els += pipe_copies(els)
    rig = make_rig(parts)
    ok = validate_jig.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "jig.json", args.out / "jig_frame.json", args.out / "jig-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "jig.json", SHAPE_DIR / "jig_frame.json", RIG_DIR / "jig-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_jig.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
