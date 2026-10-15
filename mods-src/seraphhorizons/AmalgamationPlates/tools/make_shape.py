#!/usr/bin/env python3
"""Generate the amalgamation plates' shapes and rig.

The amalgamation plates (#917, model #918) are a plate table of the 1890s stamp mill, as its own machine:
an inclined plank bed falling 1 in 10 on three oak trestles, three copper plates laid down it, dressed with
mercury, a distributor at the head that spreads the pulp across the plates' width, and a spray pipe across
the head; the pulp runs down the plates in a thin sheet, the free gold and silver that touch them are held as
amalgam, and the rest falls off the foot into a tail box and out by chute. Level 2 fits a mercury trap in the
tail box: a cast-iron well with a hanging baffle the pulp passes under and a weir it passes over, catching
the amalgam and mercury the stream scours off the plates.

    level 1  the table and plates   the frame (sills, trestles, bed, distributor, tail box) and the plates,
                                    dressed with mercury (`plates`, `dressing`)
    level 2  the mercury trap       the trap fitted in the tail box (`trap`)

It takes no power: nothing in it moves (#917: "Water, no power"), so the rig has no power cell and no driver,
and every part is a still part fitted or drawn by gameplay. Continuous: one vanilla copper chute section in
through the head cell's top face into the distributor, one out through the foot cell's end face (`chutes`);
water from a Pipes and Power Expanded pipe on the head cell's west face, met by a short length of ppex's
pipe that narrows through a soldered plug (a reducer) to a small spray pipe across the head, as the vanner's
water line, drawn soldered in copper or lead (Pipes/tools/solderedpipe.py): a `pipecopper` and a `pipelead`
part, the same elements, one drawn. The amalgam builds up on the plates in three states
(`amalgam1`..`amalgam3`, one drawn at a time) and is scraped off by hand. Everything is built here from plain
boxes; no other mod's model is used.

It writes, deterministically,

    amalgamationplates.json         the whole machine, every fitted part and amalgam state  (assets/.../shapes/block/)
    amalgamationplates_frame.json   the frame only (block and item)                         (assets/.../shapes/block/)
    amalgamationplates-rig.json     the cells, anchors and the part rig                     (assets/.../config/)

or, with `--out DIR`, all three into DIR. There are no reference poses: no part has a driver, so every part's
matrix is the identity. It validates its own output (validate_amalgamationplates.py) and exits non-zero if a
check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
machine box's north-west-bottom corner (the "build frame"). The controller is the head cell, the build
frame's [0,0,0], at the front: the plates fall away from the player who places them, south, to the foot.
"""

from __future__ import annotations

import argparse
import copy
import json
import math
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Machines" / "tools"))
sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Pipes" / "tools"))

from machinegen.checks import cell_boxes, cells_touched, with_lids  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import IDENT, El, flatten, rotate, translate  # noqa: E402
from machinegen.output import rig_dumps, shape_dumps, shift_cell, shift_point  # noqa: E402
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

import solderedpipe  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
ASSETS = MOD / "assets" / "seraphhorizons"
SHAPE_DIR = ASSETS / "shapes" / "block"
RIG_DIR = ASSETS / "config"
SCRIPT = "mods-src/seraphhorizons/AmalgamationPlates/tools/make_shape.py"

B = 16.0

# ---------------------------------------------------------------- the box, the cells, the anchors
CELLS_X, CELLS_Y, CELLS_Z = 1, 1, 3          # one wide, one high, three long (south, away from the player)
X_LEN, Y_LEN, Z_LEN = CELLS_X * B, CELLS_Y * B, CELLS_Z * B
ORIGIN_CELL = (0, 0, 0)                      # the controller: the head cell, at the front
FEED_CELL, FEED_FACE = (0, 0, 0), "up"       # a vanilla chute from above meets the infeed section in the head cell's top face
WATER_CELL, WATER_FACE = (0, 0, 0), "west"   # a ppex pipe meets the spray pipe end to end on the head cell's west face
OUTPUT_CELL, OUTPUT_FACE = (0, 0, 2), "south"   # the outlet section in the foot cell's end face

LEVELS = (                                   # (level, what it fits, its name)
    (1, ("plates", "dressing"), "the table and plates"),
    (2, ("plates", "dressing", "trap"), "the mercury trap"),
)
AMALGAM = ("amalgam1", "amalgam2", "amalgam3")   # the amalgam on the plates, one state drawn at a time
PIPE_METALS = ("copper", "lead")             # the spray pipe's metals: one part each, the supplied one drawn
PIPES = tuple(f"pipe{m}" for m in PIPE_METALS)
REQUIRES = ("chutes", "plates", "dressing", *AMALGAM, "trap", *PIPES)

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",         # timber: sills, posts, trestles, cleats, the cross bars, the pipe's rest
    "planks": "game:block/wood/planks/oak1",       # boards: the bed, the distributor, the tail box
    "copper": "game:block/metal/sheet-plain/copper1",   # the plates
    "mercury": "seraphhorizons:block/liquid/mercury",   # the plates' dressing and the trap's well (the amalgam pan's texture)
    "amalgam": "game:block/metal/ingot/leadsolder",     # the amalgam on the plates: the amalgam pan's paste
    "iron": "game:block/metal/plate/iron",         # the trap: cast iron
    "chute": "game:block/metal/sheet/copper1",     # vanilla's chute section, as a placed chute shows in the pack
    "pipecopper": "game:block/metal/sheet-plain/copper4",   # the spray pipe in copper (UnifiedPipes' copper pipe)
    "pipelead": "game:block/metal/sheet-plain/lead4",       # ... in lead (UnifiedPipes' lead pipe)
    "solder": solderedpipe.SOLDER_TEXTURE,         # the reducer and the spray pipe's cap
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face
AMALGAM_PAN_SHAPE = SHAPE_DIR / "amalgampan.json"   # the amalgam's texture is held to the amalgam pan's

# ---------------------------------------------------------------- the frame: sills, front posts, trestles
SILL_X = ((0.4, 2.4), (13.6, 15.6))          # two oak sills on the ground, from the head to the tail box
SILL_Y = (0.0, 1.6)
SILL_Z = (0.4, 35.8)
POST_X = ((0.4, 2.0), (14.0, 15.6))          # the front posts, on the sills: they carry the distributor's north end
POST_Z = (0.4, 2.0)
TRESTLES_Z = ((12.4, 14.0), (23.4, 25.0), (33.8, 35.4))   # three trestles under the bed, head to foot
LEG_X = ((0.6, 2.2), (13.8, 15.4))           # each trestle's legs, on the sills
BEARER_X = (0.4, 15.6)                       # each trestle's bearer, across under the bed
BEARER_H = 1.4

# ---------------------------------------------------------------- the bed (the plate table)
SLOPE = 0.1                                  # the bed falls 1 in 10 to the foot (#918: "about 1 in 10")
BED_Z = (11.8, 37.6)                         # its floor's top from the head to the foot's lip
BED_HEAD_Y = 9.4                             # the floor's top at the head: the spray pipe's nozzles clear the head board
BED_T = 0.6                                  # the floor's boards
BED_X = (0.4, 15.6)                          # outside the side boards
SIDE_T = 0.8
SIDE_H = 1.6                                 # the side boards over the floor's top: low, the amalgam is scraped over them
HEAD_T = 0.6                                 # the head board, across the bed's upper end
HEAD_H = 0.3                                 # its top over the floor's top at the head
PLATE_X = (1.4, 14.6)                        # the three copper plates: 13.2 wide (a period plate was about 1.4 m)
PLATE_Z = ((12.0, 20.4), (20.5, 28.9), (29.0, 37.4))   # down the bed, a hair apart; the foot's lip clear past the last
PLATE_T = 0.25
COAT_T = 0.06                                # the mercury dressing rubbed into each plate
COAT_INSET = 0.25

# ---------------------------------------------------------------- the distributor, at the head
# A plank box under the infeed chute, across the whole width over the plates' head; the pulp falls into it,
# spreads, and drops onto the head of the plates through a row of holes along its floor's lower edge. Its
# north end on the front posts, its south end on two cleats on the bed's side boards; two cross bars over it
# hold the infeed chute between them.
DIST_X = (0.6, 15.4)                         # outside
DIST_Z = (2.0, 15.6)
DIST_Y = (11.4, 15.4)                        # the floor's underside (just over the spray pipe) and the walls' top
DIST_T = 0.6                                 # its boards
HOLES_Z = (12.8, 14.2)                       # the row of holes, over the head of the first plate
HOLES_X = (1.6, 14.4)
HOLES_N = 7
HOLE_W = 0.9
CROSS_Z = ((2.6, 4.0), (12.0, 13.4))         # the cross bars on the walls' tops, the chute between them
CROSS_Y = (15.4, 16.0)
CLEAT_Z = (13.6, 15.2)                       # the cleats on the side boards under the box's south end

# ---------------------------------------------------------------- the chutes
VANILLA_CHUTE = {                            # game 1.22.7, shapes/block/metal/chute/straight.json (the "we" shape):
    "walls": ((4.0, 12.0, 4.0, 5.0),         # its walls across the run, (a0, a1, b0, b1) cell-local over (y, z): two
              (4.0, 12.0, 11.0, 12.0),       # full-height walls, two between them; 1 thick, an 8 x 8 tube centred in
              (4.0, 5.0, 5.0, 11.0),         # the block (its 4..12), a 6 x 6 bore. The "ns" variant is the same turned
              (11.0, 12.0, 5.0, 11.0)),      # about y, the "ud" turned upright
    "texture": "game:block/metal/sheet/copper1",
    "shape": "shapes/block/metal/chute/straight.json",
}
INFEED_Y0 = 13.0                             # the infeed section: from the top face down to here, inside the distributor
OUTLET_Z0 = 43.4                             # the outlet section: from the tail box's south wall to the end face

# ---------------------------------------------------------------- the tail box, at the foot
# A plank box on the ground beyond the sills, under the bed's foot and the whole width of its lip: the pulp
# falls off the lip into it and leaves over a sill in its south wall, straight into the outlet chute's bore.
TB_X = (0.2, 15.8)
TB_Z = (36.0, 43.4)
TB_FLOOR = 0.8                               # its floor's top (the floor on the ground)
TB_TOP = 6.1                                 # its walls' top, under the bed's foot
TB_T = 0.6
NOTCH_X = (5.2, 10.8)                        # the notch in the south wall, inside the chute's bore
SILL_TOP = 5.5                               # its sill: the box's water level, half a voxel over the chute's floor
NOTCH_TOP = 11.0                             # the notch's top, in the collar: the bore's
COLLAR_X = (3.0, 13.0)                       # the collar round the outlet chute's end, over the box's south wall
COLLAR_TOP = 12.6

# ---------------------------------------------------------------- level 2: the mercury trap
# A cast-iron well standing in the tail box under the lip; an iron baffle hung across it from lugs on the box's
# walls, the pulp passing under it, over the mercury in the well's bottom, and up over the well's south wall
# (its weir); a drain plug through the box's east wall for drawing the amalgam off.
TRAP_X = (0.9, 15.1)                         # the well, outside: inside it, the whole width of the lip
TRAP_Z = (36.9, 41.2)
TRAP_FLOOR = (TB_FLOOR, 1.2)                 # its floor on the box's floor
TRAP_WALL = 0.4
TRAP_TOP = 3.4                               # its walls' top: the south wall is the weir
BAFFLE_Z = (38.8, 39.2)
BAFFLE_Y0 = 2.2                              # the baffle's foot: under the weir, over the mercury
LUG_X = ((0.4, 1.8), (14.2, 15.6))           # its lugs on the box's walls' tops
LUG_H = 0.7
LUG_Z = (38.65, 39.35)
MERCURY_TOP = 1.7                            # the mercury in the well, 0.5 deep
PLUG_Y = (1.8, 2.6)                          # the drain plug, from the well's east wall out through the box's
PLUG_Z = (39.4, 40.2)
PLUG_X1 = 15.95

# ---------------------------------------------------------------- the water line (soldered ppex pipe, reduced)
# As the vanner's: a short length of ppex's full 6 x 6 pipe from the west face, on the axis of the ppex pipe
# beyond it, plugged at its east end round a smaller soldered spray pipe (a reducer, its seam soldered); the
# spray pipe runs on across the head under the distributor to a soldered cap, five nozzles on its south face
# playing over the head board onto the plates. The spray pipe sits high and south in the plug (its bore inside
# the ppex pipe's), so its nozzles clear the head board. As in the rosser's drip, there is no joint at the
# face: the pipe beyond brings its own. The vanner's dimensions and textures, so the two read alike.
PIPE_HALF = solderedpipe.HALF                # 3: ppex's pipe is 6 across
PIPE_C = (8.0, 8.0)                          # (y, z): the inlet's axis, the west face's centre
RUN_X1 = 2.0                                 # the inlet's east end, plugged round the spray pipe
SPRAY = (9.2, 9.2)                           # (y, z): the spray pipe's axis, its bore at the top south corner of the inlet's
SPRAY_HALF, SPRAY_WALL = 1.2, 0.4            # the vanner's spray pipe: 2.4 across round a 1.6 bore
REDUCER_L = 0.6                              # the solder ring round the spray pipe where it leaves the plug (the vanner's)
PIPE_END = 14.4                              # the spray pipe's east end, capped
NOZZLE_XS = (3.4, 5.9, 8.4, 10.9, 13.4)
NOZZLE_W = 0.6                               # each nozzle: across (x and y)
NOZZLE_Y = (9.76, 9.76 + NOZZLE_W)           # high on the spray pipe's south face: clear over the head board
NOZZLE_Z1 = 12.4                             # each nozzle from the spray pipe's south face to here
REST = (12.0, 13.6, 8.4, 10.0)               # the spray pipe's rest under its east end: x0, x1, z0, z1, on the ground

# ---------------------------------------------------------------- the amalgam, in three states
# Each state's pieces: (plate 1..3, from, to (fractions of the plate's length), x0, x1, height over the dressing
# (from, to), name). Most of the gold is caught at the head, so the amalgam starts there and creeps down; on the
# first plate it starts past the spray's nozzles, which reach over its head.
AMALGAM_PIECES = {
    "amalgam1": [                            # a film: patches on the head of the first plate
        (1, 0.07, 0.33, 2.2, 6.0, 0.0, 0.10, "a"),
        (1, 0.08, 0.30, 7.0, 10.4, 0.0, 0.12, "b"),
        (1, 0.12, 0.40, 11.0, 13.6, 0.0, 0.10, "c"),
        (1, 0.45, 0.62, 4.0, 8.6, 0.0, 0.08, "d"),
        (1, 0.50, 0.70, 9.8, 12.6, 0.0, 0.08, "e"),
    ],
    "amalgam2": [                            # building up: the first plate's upper part covered, the second's head patched
        (1, 0.07, 0.80, 1.9, 14.1, 0.0, 0.16, "sheet1"),
        (1, 0.84, 0.97, 3.0, 7.2, 0.0, 0.10, "a"),
        (1, 0.85, 0.95, 9.0, 12.6, 0.0, 0.10, "b"),
        (2, 0.05, 0.30, 2.4, 7.5, 0.0, 0.10, "c"),
        (2, 0.06, 0.28, 8.6, 13.2, 0.0, 0.10, "d"),
        (2, 0.40, 0.55, 5.0, 10.0, 0.0, 0.08, "e"),
    ],
    "amalgam3": [                            # thick: two plates covered, ridged at the head, the third's head patched
        (1, 0.07, 0.97, 1.9, 14.1, 0.0, 0.32, "sheet1"),
        (1, 0.12, 0.21, 2.4, 13.6, 0.32, 0.52, "ridge1"),
        (1, 0.38, 0.47, 2.6, 13.4, 0.32, 0.48, "ridge2"),
        (1, 0.64, 0.72, 3.0, 13.0, 0.32, 0.44, "ridge3"),
        (2, 0.03, 0.85, 1.9, 14.1, 0.0, 0.20, "sheet2"),
        (3, 0.05, 0.35, 2.5, 7.0, 0.0, 0.10, "a"),
        (3, 0.06, 0.32, 8.5, 13.0, 0.0, 0.10, "b"),
    ],
}


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


def sloped(a0, a1, y0, y1, pieces, part):
    """Boxes laid on a straight slope along z from a0 (height y0) to a1 (height y1). Each piece is (s0, s1, h0,
    h1, x0, x1, name, tex): s along the slope from the a0 end (0 .. its length), h above the slope's line, x
    across. They are built level from a0 and turned about the a0 end's line, so a0's end stays where it is."""
    length = a1 - a0
    out = []
    for s0, s1, h0, h1, x0, x1, name, tex in pieces:
        out.append(box([x0, y0 + h0, a0 + s0], [x1, y0 + h1, a0 + s1], name, part, tex))
    target = math.atan2(y1 - y0, length)
    if abs(target) > 1e-12:
        rotate(out, "x", -math.degrees(target), (0.0, y0, a0))
    return out


def slope_len(a0, a1, y0, y1):
    return math.hypot(a1 - a0, y1 - y0)


def bed_y(z):
    """The bed floor's top at z (along the slope's line)."""
    return BED_HEAD_Y - SLOPE * (z - BED_Z[0])


BED_FOOT_Y = bed_y(BED_Z[1])
BED_RUN = slope_len(BED_Z[0], BED_Z[1], BED_HEAD_Y, BED_FOOT_Y)
COS = (BED_Z[1] - BED_Z[0]) / BED_RUN       # the slope's cosine: a height h over the line is h / COS straight up


def along(z):
    """The distance along the bed's slope from its head to the point over z."""
    return (z - BED_Z[0]) / COS


def on_bed(pieces, part):
    """Pieces laid on the bed's slope (sloped, from the bed's head)."""
    return sloped(BED_Z[0], BED_Z[1], BED_HEAD_Y, BED_FOOT_Y, pieces, part)


def under_bed(z0, z1):
    """The lowest point of the bed's underside over z0..z1, less a hundredth: where a bearer under it stops."""
    return min(bed_y(z0), bed_y(z1)) - BED_T / COS - 0.01


# ---------------------------------------------------------------- builders: the frame
def build_timbers():
    """The two sills on the ground, the front posts on them, and three trestles (two legs on the sills and a
    bearer across) under the bed."""
    f = "frame"
    out = []
    for s, (x0, x1) in zip("we", SILL_X):
        out += beam([x0, SILL_Y[0], SILL_Z[0]], [x1, SILL_Y[1], SILL_Z[1]], f"fr_sill_{s}", f, "oak")
    for s, (x0, x1) in zip("we", POST_X):
        out.append(box([x0, SILL_Y[1], POST_Z[0]], [x1, DIST_Y[1], POST_Z[1]], f"fr_post_{s}", f, "oak"))
    for i, (z0, z1) in enumerate(TRESTLES_Z, 1):
        top = under_bed(z0, z1)
        out.append(box([BEARER_X[0], top - BEARER_H, z0], [BEARER_X[1], top, z1], f"fr_bearer{i}", f, "oak"))
        for s, (x0, x1) in zip("we", LEG_X):
            out.append(box([x0, SILL_Y[1], z0], [x1, top - BEARER_H, z1], f"fr_leg{i}_{s}", f, "oak"))
    return out


def build_bed():
    """The plate table's bed: a plank floor falling 1 in 10 from the head to the foot's lip, low side boards
    running on past the head over the head board's ends, and the head board across the upper end. Each in two
    lengths, so no texture stretches."""
    f = "frame"
    x0, x1 = BED_X
    t = SIDE_T
    half = BED_RUN / 2
    pieces = []
    for i, (s0, s1) in enumerate(((0.0, half), (half, BED_RUN)), 1):
        pieces.append((s0, s1, -BED_T, 0.0, x0 + t, x1 - t, f"fr_bed_floor{i}", "planks"))
    for i, (s0, s1) in enumerate(((-HEAD_T, half), (half, BED_RUN)), 1):
        pieces.append((s0, s1, -BED_T, SIDE_H, x0, x0 + t, f"fr_bed_side_w{i}", "planks"))
        pieces.append((s0, s1, -BED_T, SIDE_H, x1 - t, x1, f"fr_bed_side_e{i}", "planks"))
    out = on_bed(pieces, f)
    # the head board: level, across between the side boards, over the floor's upper end
    out.append(box([x0 + t, BED_HEAD_Y - BED_T - 0.1, BED_Z[0] - HEAD_T], [x1 - t, BED_HEAD_Y + HEAD_H, BED_Z[0] + 0.05],
                   "fr_bed_head", f, "planks"))
    return out


def build_distributor():
    """The distributor: a plank box across the head, its floor solid but for a row of holes along its lower
    edge over the head of the first plate; two cross bars on its walls' tops hold the infeed chute between
    them; its north end on the front posts, its south end on a cleat on each side board."""
    f = "frame"
    x0, x1 = DIST_X
    z0, z1 = DIST_Z
    y0, y1 = DIST_Y
    t = DIST_T
    fy = y0 + t                                  # the floor's top
    out = [box([x0, y0, z0], [x1, y1, z0 + t], "fr_dist_n", f, "planks"),
           box([x0, y0, z1 - t], [x1, y1, z1], "fr_dist_s", f, "planks"),
           box([x0, y0, z0 + t], [x0 + t, y1, z1 - t], "fr_dist_w", f, "planks"),
           box([x1 - t, y0, z0 + t], [x1, y1, z1 - t], "fr_dist_e", f, "planks"),
           box([x0 + t, y0, z0 + t], [x1 - t, fy, HOLES_Z[0]], "fr_dist_floor", f, "planks"),
           box([x0 + t, y0, HOLES_Z[1]], [x1 - t, fy, z1 - t], "fr_dist_lip", f, "planks")]
    edges = [x0 + t]
    for c in hole_centres():
        edges += [c - HOLE_W / 2, c + HOLE_W / 2]
    edges.append(x1 - t)
    for i in range(0, len(edges), 2):
        out.append(box([edges[i], y0, HOLES_Z[0]], [edges[i + 1], fy, HOLES_Z[1]], f"fr_dist_bar{i // 2 + 1}", f, "planks"))
    for s, (c0, c1) in zip("ns", CROSS_Z):
        out.append(box([x0, CROSS_Y[0], c0], [x1, CROSS_Y[1], c1], f"fr_dist_cross_{s}", f, "oak"))
    # the cleats: from just into the side board's sloped top up to the box's underside
    for s, (c0, c1) in zip("we", ((BED_X[0], BED_X[0] + SIDE_T), (BED_X[1] - SIDE_T, BED_X[1]))):
        low = bed_y(CLEAT_Z[1]) + SIDE_H / COS - 0.02
        out.append(box([c0, low, CLEAT_Z[0]], [c1, y0, CLEAT_Z[1]], f"fr_dist_cleat_{s}", f, "oak"))
    return out


def hole_centres():
    pitch = (HOLES_X[1] - HOLES_X[0]) / HOLES_N
    return [HOLES_X[0] + pitch * (i + 0.5) for i in range(HOLES_N)]


def build_tailbox():
    """The tail box under the bed's foot: a plank floor on the ground past the sills' ends, four walls under
    the bed, the south wall notched down to a sill that the outlet chute's bore takes the pulp from. Over the
    notch the south wall rises in a collar round the chute's end, which butts against it, the bore open from
    the sill up."""
    f = "frame"
    x0, x1 = TB_X
    z0, z1 = TB_Z
    t = TB_T
    y0, y1 = TB_FLOOR, TB_TOP
    n0, n1 = NOTCH_X
    c0, c1 = COLLAR_X
    return [box([x0, 0.0, z0], [x1, y0, z1], "fr_tb_floor", f, "planks"),
            box([x0, y0, z0], [x1, y1, z0 + t], "fr_tb_n", f, "planks"),
            box([x0, y0, z0 + t], [x0 + t, y1, z1 - t], "fr_tb_w", f, "planks"),
            box([x1 - t, y0, z0 + t], [x1, y1, z1 - t], "fr_tb_e", f, "planks"),
            box([x0, y0, z1 - t], [n0, y1, z1], "fr_tb_s1", f, "planks"),
            box([n1, y0, z1 - t], [x1, y1, z1], "fr_tb_s2", f, "planks"),
            box([n0, y0, z1 - t], [n1, SILL_TOP, z1], "fr_tb_sill", f, "planks"),
            box([c0, y1, z1 - t], [n0, COLLAR_TOP, z1], "fr_tb_collar_w", f, "planks"),
            box([n1, y1, z1 - t], [c1, COLLAR_TOP, z1], "fr_tb_collar_e", f, "planks"),
            box([n0, NOTCH_TOP, z1 - t], [n1, COLLAR_TOP, z1], "fr_tb_collar_top", f, "planks")]


def build_pipe_rest():
    x0, x1, z0, z1 = REST
    return [box([x0, 0.0, z0], [x1, SPRAY[0] - SPRAY_HALF, z1], "fr_pipe_rest", "frame", "oak")]


def build_frame():
    return build_timbers() + build_bed() + build_distributor() + build_tailbox() + build_pipe_rest()


# ---------------------------------------------------------------- builders: the chutes
def build_chutes():
    """The two chute sections (`chutes`): vanilla's straight chute exactly (VANILLA_CHUTE: its cross-section,
    wall thickness, place in the block and texture), so a chute placed against the face meets it with no gap
    or step. The infeed upright from the head cell's top face down into the distributor; the outlet along z
    from the tail box's south wall to the foot cell's end face, its bore round the sill."""
    p = "chutes"
    out = []
    # upright: the "ud" variant, the "we" shape turned about z: its (y, z) walls become (x, z)
    cx, cz = FEED_CELL[0] * B, FEED_CELL[2] * B
    top = (FEED_CELL[1] + 1) * B
    for name, (a0, a1, b0, b1) in zip(("n", "s", "w", "e"), upright_walls()):
        out.append(box([cx + a0, INFEED_Y0, cz + b0], [cx + a1, top, cz + b1], f"ch_in_{name}", p, "chute"))
    # along z: the "ns" variant, the "we" shape turned about y: its (y, z) walls become (y, x)
    ox, oy = OUTPUT_CELL[0] * B, OUTPUT_CELL[1] * B
    for name, (a0, a1, b0, b1) in zip(("w", "e", "bottom", "top"), VANILLA_CHUTE["walls"]):
        out.append(box([ox + b0, oy + a0, OUTLET_Z0], [ox + b1, oy + a1, Z_LEN], f"ch_out_{name}", p, "chute"))
    return out


def upright_walls():
    """VANILLA_CHUTE's walls as an upright chute has them over (x, z): the "we" shape's (y, z) turned upright
    (rotateZ 90 about the block's centre), its y becoming x, mirrored, which the square section does not mind."""
    return [(16.0 - a1, 16.0 - a0, b0, b1) for a0, a1, b0, b1 in VANILLA_CHUTE["walls"]]


def chute_profile(els, prefix, axes, origin):
    """The walls of the chute section named `prefix`, cell-local over `axes` (two axis indices), sorted, with
    their extents along the third axis."""
    out, runs = [], set()
    run = 3 - sum(axes)
    for el in els:
        if not el.name.startswith(prefix):
            continue
        lo, hi = el.aabb()
        a, b = axes
        out.append(tuple(round(v, 4) for v in (lo[a] - origin[a], hi[a] - origin[a], lo[b] - origin[b], hi[b] - origin[b])))
        runs.add((round(lo[run], 4), round(hi[run], 4)))
    return sorted(out), runs


def read_vanilla_chute():
    """The game's own straight chute, if the game is here ($VINTAGE_STORY or ~/Games/vintagestory): its walls'
    sections over (y, z), their runs along x, and its texture; or None. The generator holds VANILLA_CHUTE to it."""
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


# ---------------------------------------------------------------- builders: the plates, their dressing, the amalgam
def plate_s(i):
    """The i-th plate's (1-based) run along the slope."""
    z0, z1 = PLATE_Z[i - 1]
    return along(z0), along(z1)


def build_plates():
    """The three copper plates (`plates`), laid on the bed's floor between its side boards."""
    pieces = []
    for i in range(1, len(PLATE_Z) + 1):
        s0, s1 = plate_s(i)
        pieces.append((s0, s1, 0.0, PLATE_T, *PLATE_X, f"pl_plate{i}", "copper"))
    return on_bed(pieces, "plates")


def build_dressing():
    """The mercury dressing (`dressing`): a thin coat rubbed into each plate, a hair inside its edges."""
    pieces = []
    for i in range(1, len(PLATE_Z) + 1):
        s0, s1 = plate_s(i)
        pieces.append((s0 + COAT_INSET, s1 - COAT_INSET, PLATE_T, PLATE_T + COAT_T,
                       PLATE_X[0] + COAT_INSET, PLATE_X[1] - COAT_INSET, f"pd_coat{i}", "mercury"))
    return on_bed(pieces, "dressing")


AMALGAM_H0 = PLATE_T + COAT_T                # the amalgam lies on the dressing


def build_amalgam():
    """The amalgam on the plates, in three states (`amalgam1`..`amalgam3`, one drawn at a time): patches on the
    head of the first plate, then that plate's upper part covered and the second's head patched, then two plates
    covered thick and ridged at the head and the third's head patched."""
    out = []
    for n, pid in enumerate(AMALGAM, 1):
        pieces = []
        for plate, f0, f1, x0, x1, h0, h1, name in AMALGAM_PIECES[pid]:
            s0, s1 = plate_s(plate)
            run = s1 - s0
            pieces.append((s0 + run * f0, s0 + run * f1, AMALGAM_H0 + h0, AMALGAM_H0 + h1, x0, x1, f"am{n}_{name}", "amalgam"))
        out += on_bed(pieces, pid)
    return out


# ---------------------------------------------------------------- builders: level 2, the mercury trap
def build_trap():
    """The mercury trap (`trap`): a cast-iron well standing on the tail box's floor under the lip, the mercury
    in its bottom; an iron baffle hung across it from lugs on the box's walls, its foot under the well's walls'
    top and over the mercury, so the pulp passes under it and up over the well's south wall; a drain plug from
    the well's east wall out through the box's."""
    p = "trap"
    x0, x1 = TRAP_X
    z0, z1 = TRAP_Z
    w = TRAP_WALL
    f0, f1 = TRAP_FLOOR
    out = [box([x0, f0, z0], [x1, f1, z1], "tr_floor", p, "iron"),
           box([x0, f1, z0], [x1, TRAP_TOP, z0 + w], "tr_wall_n", p, "iron"),
           box([x0, f1, z1 - w], [x1, TRAP_TOP, z1], "tr_weir", p, "iron"),
           box([x0, f1, z0 + w], [x0 + w, TRAP_TOP, z1 - w], "tr_wall_w", p, "iron"),
           box([x1 - w, f1, z0 + w], [x1, TRAP_TOP, z1 - w], "tr_wall_e", p, "iron"),
           box([x0 + w, BAFFLE_Y0, BAFFLE_Z[0]], [x1 - w, TB_TOP, BAFFLE_Z[1]], "tr_baffle", p, "iron")]
    for s, (l0, l1) in zip("we", LUG_X):
        out.append(box([l0, TB_TOP, LUG_Z[0]], [l1, TB_TOP + LUG_H, LUG_Z[1]], f"tr_lug_{s}", p, "iron"))
    out.append(box([x1, PLUG_Y[0], PLUG_Z[0]], [PLUG_X1, PLUG_Y[1], PLUG_Z[1]], "tr_plug", p, "iron"))
    out.append(box([x0 + w, f1, z0 + w], [x1 - w, MERCURY_TOP, z1 - w], "tr_mercury", p, "mercury"))
    return out


# ---------------------------------------------------------------- builders: the spray pipe (soldered pipe)
def small_tube(name, x0, x1, centre, half, wall, role):
    """A small square tube along x from x0 to x1 about `centre` = (y, z): four walls round its bore, as
    solderedpipe's tube is built, at the spray pipe's size (the vanner's)."""
    y, z = centre
    i = half - wall
    B_ = solderedpipe.Box
    return [B_(f"{name}_down", [x0, y - half, z - half], [x1, y - i, z + half], role),
            B_(f"{name}_up", [x0, y + i, z - half], [x1, y + half, z + half], role),
            B_(f"{name}_north", [x0, y - i, z - half], [x1, y + i, z - i], role),
            B_(f"{name}_south", [x0, y - i, z + i], [x1, y + i, z + half], role)]


def pipe_boxes():
    """The water line as UnifiedPipes' soldered pipe (solderedpipe's boxes, build-frame voxels), as the
    vanner's: the inlet, ppex's 6 x 6 pipe from the west face on the axis of the ppex pipe beyond it, plugged
    at its east end round the spray pipe's bore (a reducer, its seam soldered); the spray pipe, smaller, across
    the head to its east end, capped there with solder; five nozzles on its south face."""
    y, z = PIPE_C
    h = PIPE_HALF
    Bx = solderedpipe.Box
    out = solderedpipe.tube("inlet", 0, 0.0, RUN_X1, (z, y))
    # the plug in the inlet's east end, round the spray pipe's bore: four plates inside its walls
    i = h - solderedpipe.WALL
    sy, sz = SPRAY
    si = SPRAY_HALF - SPRAY_WALL
    p0, p1 = RUN_X1 - solderedpipe.WALL, RUN_X1
    out += [Bx("plug_down", [p0, y - i, z - i], [p1, sy - si, z + i], solderedpipe.BODY),
            Bx("plug_up", [p0, sy + si, z - i], [p1, y + i, z + i], solderedpipe.BODY),
            Bx("plug_north", [p0, sy - si, z - i], [p1, sy + si, sz - si], solderedpipe.BODY),
            Bx("plug_south", [p0, sy - si, sz + si], [p1, sy + si, z + i], solderedpipe.BODY)]
    out = [b for b in out if all(b.hi[k] - b.lo[k] > 1e-9 for k in range(3))]
    # the reducer's joint: a ring of solder round the spray pipe where it leaves the plug
    j = solderedpipe.JOINT_SWELL
    out += small_tube("reducer", RUN_X1, RUN_X1 + REDUCER_L, SPRAY, SPRAY_HALF + j, j, solderedpipe.SOLDER)
    out += small_tube("spray", RUN_X1, PIPE_END, SPRAY, SPRAY_HALF, SPRAY_WALL, solderedpipe.BODY)
    out.append(Bx("spray_cap", [PIPE_END - SPRAY_WALL, sy - si, sz - si], [PIPE_END, sy + si, sz + si], solderedpipe.SOLDER))
    for k, nx in enumerate(NOZZLE_XS, 1):
        out.append(Bx(f"nozzle{k}", [nx - NOZZLE_W / 2, NOZZLE_Y[0], sz + SPRAY_HALF], [nx + NOZZLE_W / 2, NOZZLE_Y[1], NOZZLE_Z1],
                      solderedpipe.BODY))
    return solderedpipe.cull_pressed(out)


def build_pipes(metal_=PIPE_METALS[0]):
    """The spray pipe as elements of the part `pipe<metal_>` (`pipe_boxes`): the body in that metal's pipe
    texture, the cap in solder, each face's UVs solderedpipe's on this model's texture scale. Built in copper;
    `pipe_copies` makes lead."""
    p = f"pipe{metal_}"
    codes = {solderedpipe.BODY: f"#{p}", solderedpipe.SOLDER: "#solder"}
    out = []
    for b in pipe_boxes():
        size = b.size()
        faces = {d: {"texture": codes[b.role], "uv": solderedpipe.uv(d, size, TEX / 16)} for d in b.faces}
        out.append(El(f"{p}_{b.name}", size, [(b.lo[k] + b.hi[k]) / 2 for k in range(3)], [row[:] for row in IDENT], faces, p))
    return out


def pipe_copies(els):
    """The spray pipe in the other metals: the first metal's elements as they are (after the z-fighting fix, so
    every copy has the same insets), renamed and in their own metal's texture; the cap stays solder. Only one
    metal's are ever drawn, so the copies are not checked against each other."""
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


def build():
    return (build_amalgam() + build_dressing() + build_plates() + build_trap() + build_chutes() + build_pipes()
            + build_frame())


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    """Every part stands still: no driver. The amalgam states first (each lies on the dressing, which lies on
    the plates), then the trap, the chutes, the spray pipe in each metal, and the frame."""
    parts = [
        *[{"id": pid, "match": [f"am{n}_*"], "requires": pid, "drivers": []} for n, pid in enumerate(AMALGAM, 1)],
        {"id": "dressing", "match": ["pd_*"], "requires": "dressing", "drivers": []},
        {"id": "plates", "match": ["pl_*"], "requires": "plates", "drivers": []},
        {"id": "trap", "match": ["tr_*"], "requires": "trap", "drivers": []},
        {"id": "chutes", "match": ["ch_*"], "requires": "chutes", "drivers": []},
        *[{"id": pid, "match": [f"{pid}_*"], "requires": pid, "drivers": []} for pid in PIPES],
        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- states
# A pose is (state,): what is fitted. Nothing moves, so a state is all a pose says.
STATES = {
    "frame": (),
    "frame_chutes": ("chutes", PIPES[0]),
    "level1bare": ("chutes", "plates", PIPES[0]),
    "level1": ("chutes", "plates", "dressing", PIPES[0]),
    "level1am3": ("chutes", "plates", "dressing", "amalgam3", PIPES[0]),
    "level2": ("chutes", "plates", "dressing", "trap", PIPES[0]),
    "level2am1": ("chutes", "plates", "dressing", "trap", "amalgam1", PIPES[0]),
    "level2am2": ("chutes", "plates", "dressing", "trap", "amalgam2", PIPES[0]),
    "level2am3": ("chutes", "plates", "dressing", "trap", "amalgam3", PIPES[0]),
}
REST_POSE = ("level2am3",)


def inputs_of(pose):
    return {"theta": 0.0}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose), None)


def on_show(requires, state):
    """Whether a part needing `requires` shows in a state."""
    return requires is None or requires in STATES[state]


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


POINTS = ("feedSpout", "spray", "amalgam", "trapWell")


def make_rig(parts):
    cx = (PLATE_X[0] + PLATE_X[1]) / 2
    mid_z = (PLATE_Z[0][0] + PLATE_Z[-1][1]) / 2
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, three cells, the controller [0,0,0]: the head "
                    "cell, at the front; the plates fall south, away from the player. No power and nothing moves: no "
                    "powerCell and no driver, every part is still and fitted or drawn by its requires. The infeed is "
                    "vanilla's chute section in the feed cell's top face, the outlet the same in the output cell's end "
                    "face; the spray pipe meets a ppex pipe end to end on the water face. levels: what each level fits. "
                    "amalgamStates: the amalgam on the plates, one drawn at a time. See the amalgamation plates' README "
                    "for the schema.",
        "cells": [],
        "feedCell": list(FEED_CELL),
        "feedFace": FEED_FACE,
        "waterCell": list(WATER_CELL),
        "waterFace": WATER_FACE,
        "outputCell": list(OUTPUT_CELL),
        "outputFace": OUTPUT_FACE,
        "feedSpout": {"pos": pt(cx, DIST_Y[0], sum(HOLES_Z) / 2)},
        "spray": {"pos": pt(cx, NOZZLE_Y[0], NOZZLE_Z1)},
        "amalgam": {"pos": pt(cx, bed_y(mid_z) + AMALGAM_H0 / COS, mid_z)},
        "trapWell": {"pos": pt((TRAP_X[0] + TRAP_X[1]) / 2, MERCURY_TOP, (TRAP_Z[0] + TRAP_Z[1]) / 2)},
        "levels": [{"level": n, "name": name, "requires": list(req)} for n, req, name in LEVELS],
        "amalgamStates": list(AMALGAM),
        "pipes": {"requires": {m: f"pipe{m}" for m in PIPE_METALS},
                  "_comment": "The spray pipe, ppex's straight pipe soldered, one part per metal (the same elements); "
                              "only the supplied metal's is drawn."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: the build frame's corner is
    the controller's). No part has a driver, so the parts are as they are."""
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    db = [v / B for v in d]
    ship_els = copy.deepcopy(els)
    translate(ship_els, d)
    ship = copy.deepcopy(rig)
    for key in ("feedCell", "waterCell", "outputCell"):
        ship[key] = shift_cell(rig[key], ORIGIN_CELL)
    for key in POINTS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = copy.deepcopy(parts)
    return ship_els, ship["parts"], ship


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig (every part, every
    amalgam state and both pipe metals: they lie inside the frame's outline); then the lids over every column."""
    written = flatten(shape["elements"], textures={})
    by_cell = {}
    for w in written:
        el = posed(w, _part_matrix(ship_parts, part_of(ship_parts, w.name), inputs_of(REST_POSE), None))
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, []).append(el)
    out = []
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        boxes = cell_boxes(by_cell[pos], pos) if pos in by_cell else None
        out.append({"pos": list(pos), "boxes": boxes} if boxes else {"pos": list(pos), "hollow": True})
    return with_lids(out)


def check_shipped(els, ship_els, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    worst = 0.0
    for a, b in zip(els, ship_els):
        worst = max(worst, max(abs(a.c[k] + d[k] - b.c[k]) for k in range(3)))
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst difference {worst:.2e} voxels; cells {cells}")
    if worst > 1e-9 or cells != [(0, 0, 0), (0, 0, 1), (0, 0, 2)]:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. The frame (fr_*), the "
             "chute sections (ch_*), the plates (pl_*), their mercury dressing (pd_*), the amalgam in three states "
             "(am1_ .. am3_), the mercury trap (tr_*) and the spray pipe in each metal (pipecopper_ / pipelead_). "
             "'mercury' is the mod's own provisional texture (the amalgam pan's). Keep element names when editing: "
             "the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return tuple((st,) for st in STATES)


def shown(posed_els, pose, req_of):
    """The posed elements as they can be seen in the pose's state: the parts not fitted moved far away (copies,
    in order), so the z-fighting fix and its check deal only with faces that show together."""
    out = []
    for e in posed_els:
        if not on_show(req_of[e.part], pose[0]):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def fix_coplanar(els, parts):
    req_of = {p["id"]: p["requires"] for p in parts}
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose, req_of),
                              coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the amalgamation plates' shapes and rig.")
    ap.add_argument("--out", type=Path, help="write the three files into this directory instead of the mod's assets")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and its check (not for files that ship)")
    args = ap.parse_args()
    import validate_amalgamationplates
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        for pose, pairs in before.items():
            if pairs:
                print(f"coplanar faces before the fix in {pose[0]}: {len(pairs)} pairs")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    els += pipe_copies(els)
    rig = make_rig(parts)
    ok = validate_amalgamationplates.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "amalgamationplates.json", args.out / "amalgamationplates_frame.json",
                args.out / "amalgamationplates-rig.json")
    else:
        outs = (SHAPE_DIR / "amalgamationplates.json", SHAPE_DIR / "amalgamationplates_frame.json",
                RIG_DIR / "amalgamationplates-rig.json")
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_amalgamationplates.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship))
    for path, text in zip(outs, texts):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        json.loads(path.read_text())
        print(f"wrote {path} ({path.stat().st_size // 1024} KiB)")
    if not (check_shipped(els, ship_els, ship) and ok):
        sys.exit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
