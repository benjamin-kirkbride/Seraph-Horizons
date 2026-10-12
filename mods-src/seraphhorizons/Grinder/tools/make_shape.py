#!/usr/bin/env python3
"""Generate the grinder's shapes, rig and reference poses.

The grinder is the ore line's grind stage (#732, model #733), one machine upgraded in place on the shared
upgradeable-machine code (#711): a frame, then each level's parts fitted into it. Its footprint is fixed at its
final size from the frame on, and the feed, the water, the discharge and the drive stay at the same cells at
every level:

    frame     a stone-paved circular pit at ground level (a drystone curb round cobbled paving, a centre stone,
              an overflow notch in the curb with a launder out to the output face); over it a timber head
              frame (four posts, two stringers, two transoms); hung from it the power shaft (the vanilla axle's
              cross at the south face, then an iron shaft in two drop hangers); between the stringers the
              feed hopper, its mouth the infeed's top face, the water pipe's inlet through its north wall and
              a steep chute from its outlet down towards the pit
    level 1   water tier, the arrastra: a post stepped on the centre stone and held in a bearing under the
              transom, turned from overhead through a bevel pinion on the power shaft and a bevel wheel on the
              post; four sweep arms through the post, each dragging a granite drag stone round the paving on a
              bridle of two chains
    level 2   mill tier, the ball mill: the arrastra's post, arms and stones come out, and a short riveted
              drum on hollow trunnions goes into the same pit, in two bearings on cast pedestals on the
              paving, its girth gear turned by a pinion on the power shaft's end where the arrastra's pinion
              was; fed through the north trunnion by a lip from the chute (fresh ore and water from the
              hopper, the concentrator's middlings by the same hopper), discharging over the south
              trunnion's lip onto the paving, from where the pulp leaves by the arrastra's overflow and
              launder; a charge of iron balls inside

Everything is built here from plain boxes, the inlet pipe with the mod's own soldered pipe
(Pipes/tools/solderedpipe.py); no other mod's model is used. No animal, wheel or engine: power comes from a
vanilla axle on the power face, and both levels work whichever way it turns.

It writes, deterministically,

    grinder.json         the whole machine, both levels' parts      (assets/.../shapes/block/)
    grinder_frame.json   the static frame only (block and item)     (assets/.../shapes/block/)
    grinder-rig.json     cells, anchors, the levels and the rig     (assets/.../config/)
    rig-reference.json   every part's matrix at a grid of poses     (tests/Grinder/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_grinder.py) and exits non-zero
if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
machine box's north-west-bottom corner (the "build frame"); `shipped()` moves it so the controller cell
(ORIGIN_CELL, the middle of the north side at ground level, under the hopper) is [0,0,0]. The drive comes in
from the south, high; the ore comes in at the top on the north and leaves low on the east.

The rig reads theta alone: the axle's angle. Every part turns with it at its gearing's ratio, or stands still.
"""

from __future__ import annotations

import argparse
import copy
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Machines" / "tools"))
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Pipes" / "tools"))
sys.path.insert(0, str(Path(__file__).resolve().parent))

from machinegen.checks import cell_boxes, cells_touched  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import IDENT, El, flatten, from_template, rotate, translate  # noqa: E402
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
REFERENCE_OUT = MOD / "tests" / "Grinder" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/Grinder/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

# ---------------------------------------------------------------- the box, the cells, the anchors
CELLS_X, CELLS_Y, CELLS_Z = 3, 2, 2          # the box: three wide, two high, two deep; the pit is two blocks across
EMPTY_CELLS = ((0, 1, 0), (2, 1, 0))         # over the pit's north corners, beside the hopper: nothing of the machine is
                                             # ever there, so they are not the machine's (10 cells, not 12)
ORIGIN_CELL = (1, 0, 0)                      # the controller: the middle of the north side at ground level, under the hopper
CX, CZ = 24.0, 16.0                          # the pit's centre: the arrastra's post; the drum's axis runs along z at x CX
POWER_CELL, POWER_FACE = (1, 1, 1), "south"  # the vanilla axle comes in along z at (x 24, y 24), high: the drive is overhead
INFEED_CELL, INFEED_FACE = (1, 1, 0), "up"   # the hopper's mouth: a player or a chute tips ore in; at level 2 the
                                             # concentrator's middlings come back the same way
WATER_CELL, WATER_FACE = (1, 1, 0), "north"  # the ppex water pipe's inlet, through the hopper's north wall
OUTPUT_CELL, OUTPUT_FACE = (2, 0, 1), "east"  # the launder's lip: the ground pulp leaves eastwards, low

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "planks": "game:block/wood/planks/oak1",
    "axle": "game:block/wood/planks/generic",            # the vanilla axle's own texture, on its cross where it comes in
    "iron": "game:block/metal/plate/iron",
    "steel": "game:block/metal/sheet-plain/steel1",
    "riveted": "game:block/metal/riveted/iron1",
    "chain": "game:block/metal/armor-generic/chain-iron",
    "cobble": "game:block/stone/cobblestone/granite1",
    "drystone": "game:block/stone/drystone/granite1",
    "granite": "game:block/stone/rock/granite1",
    "basalt": "game:block/stone/rock/basalt1",           # the drag stones: a hard, dark rock, as they were chosen
    # the water pipe wears ppex's pipe textures as UnifiedPipes gives them, its joints the solder its soldered pipes
    # are wiped with (as the rosser's drip pipes)
    **{f"pipe{m}": f"game:block/metal/sheet-plain/{m}4" for m in ("copper", "lead")},
    "solder": solderedpipe.SOLDER_TEXTURE,
    # the game's chute: a placed chute shows its copper sheet (the pack's lead patch is the chute section item's,
    # patches/unifiedpipes-chutesection.json; the chute block's own texture is the game's)
    "chute": "game:block/metal/sheet/copper1",
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the pit (frame)
PAVE_TOP = 1.5                               # the paving's top: flags laid on the ground, the pit's floor at ground level
CENTRE_STONE = (3.2, 2.0)                    # the centre stone (radius, top), a little proud: the post's step sits on it
PAVE_ROWS = 9                                # the paving: rows of cobbled flags across the pit, their ends under the edging
EDGING = (12.0, 13.5, 16)                    # a course of edging stones round the paving, against the curb (radii, stones)
EDGING_TOP = PAVE_TOP + 0.3
CURB_R = (13.5, 16.0)                        # the drystone curb round the paving, two blocks across outside
CURB_TOP = 6.0
CURB_N = 16
NOTCH = 3                                    # the curb stone (index round the ring) cut down to the sill: the overflow
NOTCH_SILL = 4.0                             # the pulp stands at most this high; above it runs out by the notch
LAUNDER_Z = (19.6, 23.6)                     # the launder from the notch east to the output face (z), its floor's top
LAUNDER_X = (37.0, CELLS_X * B)              # from the sill's height at its west end to LAUNDER_LIP at the face
LAUNDER_LIP = 3.4
LAUNDER_SIDE = 1.6                           # its sides' height over the floor

# ---------------------------------------------------------------- the head frame (frame)
POST_X = ((17.0, 20.0), (28.0, 31.0))        # four posts outside the paving, set in the curb, under the two stringers
POST_Z = ((0.0, 2.8), (29.2, 32.0))
TOP = (27.0, 31.0)                           # the stringers (along z, on the posts) and the transoms between them
T1_Z = (14.0, 18.0)                          # the transom over the post: the arrastra's top bearing hangs from it
T2_Z = (24.8, 30.4)                          # the transom over the power shaft: its two drop hangers hang from it
BRACE = (6.0, 6.5)                           # each post's knee brace: along the stringer, and down the post

# ---------------------------------------------------------------- the power shaft (frame; it turns whatever is fitted)
SHAFT_Y = 24.0                               # its axis, along z at x CX: the power cell's centre
SHAFT_R = 1.0                                # its iron shaft's apothem (an octagon)
SHAFT_END = 20.6                             # its north end: each level's pinion is keyed on from here
COUPLING_Z = 30.6                            # from here to the south face, the vanilla axle's cross (two 4 x 2 boards)
HANGERS_Z = ((25.4, 27.4), (28.4, 30.0))     # its two drop hangers under T2
COLLAR_R = 1.6
P = (CX, SHAFT_Y, CZ)                        # where the power shaft's axis meets the post's: the arrastra's bevels' apex

# ---------------------------------------------------------------- the hopper, the chute and the water inlet (frame)
HOP_X = (20.0, 28.0)                         # between the stringers, over the pit's north edge
HOP_Z = (1.0, 13.0)
HOP_TOP = CELLS_Y * B                        # its mouth: the infeed cell's top face
BIN_TOP = 28.8                               # the hopper's bin (the frame's) stops here; above it is each level's infeed: the
                                             # hopper's rim the player fills (level 1), or a chute head (level 2)
HOP_WALL = 0.8
HOP_STEP_Z = 8.2                             # its walls come down to HOP_LOW north of here, HOP_HIGH south of it (over the
HOP_LOW, HOP_HIGH = 19.6, 23.8               # arrastra's bevel wheel)
HOP_FLOOR = ((12.2, 28.2), (4.4, 20.4))      # (z, y): its floor's top, sloping down north to the outlet at 45 degrees
OUTLET_Z = (HOP_Z[0] + HOP_WALL, HOP_FLOOR[1][0])   # the outlet: the open end of the floor, over the chute
CHUTE = ((HOP_Z[0] + HOP_WALL, HOP_FLOOR[1][1]), (5.6, 13.0))   # (z, y): the chute's floor's top, from the outlet down
CHUTE_W = 3.6                                # its floor's width (x); its sides stand on its edges
CHUTE_SIDE = 1.6
CHUTE_SIDES_END = 4.0                        # z: the sides stop here, square to the slope, short of the trunnion's mouth
PIPE_Y = SHAFT_Y                             # the water pipe's axis: the water face's centre, (x 24, y 24)
# The game's straight chute (assets/survival/shapes/block/metal/chute/straight.json, 1.22.7), its elements as the game
# has them; a placed chute-straight-ud is this shape turned by the blocktype's rotateZ 90 about the block's centre. Level
# 2's infeed is drawn as it, so a chute line from above meets it with no gap or step (validate_grinder.check_chute_profile,
# which also holds these numbers to the game's file when the game is installed).
VANILLA_CHUTE_SHAPE = "assets/survival/shapes/block/metal/chute/straight.json"
VANILLA_CHUTE = """[
 {"name": "North", "from": [0.0, 4.0, 4.0], "to": [16.0, 12.0, 5.0], "rotationOrigin": [8.0, 8.0, 8.0], "faces": {
  "north": {"texture": "#copper1", "uv": [0.0, 0.0, 8.0, 16.0], "rotation": 270}, "east": {"texture": "#copper1", "uv": [13.0, 0.0, 14.0, 8.0]},
  "south": {"texture": "#copper1", "uv": [4.0, 0.0, 12.0, 16.0], "rotation": 90}, "west": {"texture": "#copper1", "uv": [0.0, 0.0, 1.0, 8.0]},
  "up": {"texture": "#copper1", "uv": [0.0, 8.0, 16.0, 9.0]}, "down": {"texture": "#copper1", "uv": [0.0, 4.0, 16.0, 5.0]}}},
 {"name": "South", "from": [0.0, 4.0, 11.0], "to": [16.0, 12.0, 12.0], "rotationOrigin": [8.0, 8.0, 8.0], "faces": {
  "north": {"texture": "#copper1", "uv": [4.0, 0.0, 12.0, 16.0], "rotation": 90}, "east": {"texture": "#copper1", "uv": [11.0, 0.0, 12.0, 8.0]},
  "south": {"texture": "#copper1", "uv": [4.0, 0.0, 12.0, 16.0], "rotation": 270}, "west": {"texture": "#copper1", "uv": [15.0, 0.0, 16.0, 8.0]},
  "up": {"texture": "#copper1", "uv": [0.0, 2.0, 16.0, 3.0]}, "down": {"texture": "#copper1", "uv": [0.0, 4.0, 16.0, 5.0]}}},
 {"name": "Bottom", "from": [0.0, 4.0, 5.0], "to": [16.0, 5.0, 11.0], "rotationOrigin": [4.0, 12.0, 8.0], "faces": {
  "north": {"texture": "#copper1", "uv": [0.0, 0.0, 16.0, 1.0], "enabled": false}, "east": {"texture": "#copper1", "uv": [5.0, 0.0, 11.0, 1.0]},
  "south": {"texture": "#copper1", "uv": [0.0, 0.0, 16.0, 1.0], "enabled": false}, "west": {"texture": "#copper1", "uv": [0.5, 0.0, 6.5, 1.0]},
  "up": {"texture": "#copper1", "uv": [5.0, 0.0, 11.0, 16.0], "rotation": 90}, "down": {"texture": "#copper1", "uv": [0.0, 5.0, 16.0, 11.0]}}},
 {"name": "Top", "from": [0.0, 11.0, 5.0], "to": [16.0, 12.0, 11.0], "rotationOrigin": [8.0, 8.0, 8.0], "faces": {
  "north": {"texture": "#copper1", "uv": [0.0, 0.0, 16.0, 1.0], "enabled": false}, "east": {"texture": "#copper1", "uv": [5.0, 0.0, 11.0, 1.0]},
  "south": {"texture": "#copper1", "uv": [4.0, 0.0, 20.0, 1.0], "enabled": false}, "west": {"texture": "#copper1", "uv": [0.0, 2.0, 1.0, 8.0], "rotation": 90},
  "up": {"texture": "#copper1", "uv": [0.0, 5.0, 16.0, 11.0]}, "down": {"texture": "#copper1", "uv": [5.0, 0.0, 11.0, 16.0], "rotation": 90}}}
]"""
PIPE_METALS = ("copper", "lead")             # the water pipe is ppex's straight pipe in either metal UnifiedPipes adds

# ---------------------------------------------------------------- level 1: the arrastra
BEVEL = {"wheel": 18, "pinion": 6, "m": 0.8333, "face": 2.0}   # 3:1, the post turns a third of the axle
POST_R = 2.0                                 # the oak post's apothem
POST_Y = (3.6, 24.4)                         # the oak post; iron gudgeons below and above it
STEP = ((21.6, 26.4), (CENTRE_STONE[1], 3.6), (13.6, 18.4))   # the iron footstep on the centre stone
GUDGEON_R = 1.0
TOP_BEARING_Y = (24.8, 26.4)                 # the top bearing's box round the post's upper gudgeon; its stem to T1
ARM_Y = (8.5, 11.5)                          # four sweep arms through the post
ARM_R = 12.0
ARM_W = 2.6
ARM_ANGLE = 45.0                             # degrees: the first arm's angle (about y, from +z towards +x)
STONE_R = (4.6, 11.5)                        # a drag stone's span from the centre, under its arm
STONE_W = 4.0
STONE_TOP = PAVE_TOP + 3.0
EYE_R = 7.75                                 # the eyes on its back, fore and aft, and the staple under the arm
EYE_T = 1.5
STAPLE_Y = (7.5, ARM_Y[0])

# ---------------------------------------------------------------- level 2: the ball mill (its axis along z at x CX)
GIRTH = {"teeth": 24, "m": 0.75}             # the girth gear on the south head
PINION = {"teeth": 8, "m": 0.75}             # the pinion on the power shaft over it
GIRTH_R = GIRTH["teeth"] * GIRTH["m"] / 2    # 9.0
PINION_R = PINION["teeth"] * PINION["m"] / 2  # 3.0
DRUM_Y = SHAFT_Y - PINION_R - GIRTH_R        # 12: the drum's axis, under the power shaft
SHELL_R = (7.0, 7.6)                         # the riveted shell: a short, wide drum, as Krupp's were
HEAD_R = 8.2
TRUNNION_R = (2.6, 3.8)                      # the hollow trunnions: the feed goes in by one and the pulp leaves by the other
N_TRUNNION_Z = (6.0, 9.4)
N_HEAD_Z = (9.4, 10.6)
SHELL_Z = (10.6, 20.4)
S_HEAD_Z = (20.4, 21.6)
GIRTH_Z = (21.6, 23.6)
S_TRUNNION_Z = (21.6, 28.0)
LIP = (28.0, 28.7, 4.6)                      # the discharge lip on the south trunnion's end (z0, z1, radius)
BEARINGS_Z = ((6.6, 8.8), (24.4, 27.2))      # the trunnions' bearings on cast pedestals
HOUSING_T = 1.4
PEDESTAL_X = (20.4, 27.6)
SOLE_X = (19.2, 28.8)
SOLE_TOP = PAVE_TOP + 0.8
MANHOLE_Z = (13.4, 17.6)
FEED_LIP = ((CHUTE[1][0] - 0.2, CHUTE[1][1]), (8.0, 11.8))   # (z, y): the feed lip's floor's top, from the chute into the bore
CHARGE_FACE = (2.0, 0.0)                     # the charge's top: this far under the axis, level (rising this much a voxel towards
                                             # -x would heap it up one side, true for one direction of the axle only)
BALL = 2.2                                   # a ball's size (they vary by 0.2 either way)


# ---------------------------------------------------------------- vectors
def v_add(a, b):
    return [a[i] + b[i] for i in range(3)]


def v_sub(a, b):
    return [a[i] - b[i] for i in range(3)]


def v_mul(a, s):
    return [a[i] * s for i in range(3)]


def v_dot(a, b):
    return sum(a[i] * b[i] for i in range(3))


def v_cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def v_norm(a):
    n = math.sqrt(v_dot(a, a))
    return [x / n for x in a]


AX = {"x": 0, "y": 1, "z": 2}
UNIT = {"x": [1.0, 0.0, 0.0], "y": [0.0, 1.0, 0.0], "z": [0.0, 0.0, 1.0]}
REF = {"x": (1, 2), "y": (2, 0), "z": (0, 1)}   # an angle about the axis: 0 along the first, turning towards the second


def radial_dir(axis, a):
    """The unit direction at angle `a` (radians) about `axis`: right-handed from REF's first axis."""
    u, w = REF[axis]
    out = [0.0, 0.0, 0.0]
    out[u], out[w] = math.cos(a), math.sin(a)
    return out


def angle_of(axis, v):
    """The angle about `axis` of the direction `v` (its part across the axis)."""
    u, w = REF[axis]
    return math.atan2(v[w], v[u])


def polar(a_deg, r, y=0.0):
    """The point at angle a (degrees about y, from +z towards +x) and radius r from the pit's centre."""
    d = radial_dir("y", math.radians(a_deg))
    return [CX + r * d[0], y, CZ + r * d[2]]


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


def boards(lo, hi, name, part, tex, seg=16.0):
    """A long box cut into lengths of at most `seg` along its longest axis, so its texture is not stretched."""
    axis = max(range(3), key=lambda a: hi[a] - lo[a])
    n = max(1, math.ceil((hi[axis] - lo[axis]) / seg - 1e-9))
    out = []
    for i in range(n):
        l2, h2 = list(lo), list(hi)
        l2[axis] = lo[axis] + (hi[axis] - lo[axis]) * i / n
        h2[axis] = lo[axis] + (hi[axis] - lo[axis]) * (i + 1) / n
        out.append(box(l2, h2, f"{name}{i + 1}" if n > 1 else name, part, tex))
    return out


def oriented(name, size, c, cols, part, tex):
    """A box of `size` along the unit columns `cols` (a right-handed frame), centred on c."""
    r = [[cols[k][i] for k in range(3)] for i in range(3)]
    return skin(El(name, list(size), list(c), r, {}, part), tex)


def bar(a, b, w, h, name, part, tex, up=(0.0, 1.0, 0.0), extend=0.0):
    """A bar from point a to point b, `h` across it towards `up`, `w` across it the other way, lengthened by
    `extend` at each end."""
    d = v_norm(v_sub(b, a))
    u = v_norm(v_sub(list(up), v_mul(d, v_dot(up, d))))
    t = v_cross(d, u)
    length = math.dist(a, b) + 2 * extend
    return oriented(name, [length, h, w], v_mul(v_add(a, b), 0.5), [d, u, t], part, tex)


def tilted(top0, top1, across, th, name, part, tex, axis, extend=0.0):
    """A board whose top runs from top0 to top1 (points in the plane normal to `axis`), `across` wide along `axis`
    about the points' own coordinate on it, `th` thick under the top."""
    d = v_norm(v_sub(top1, top0))
    up = v_norm(v_cross(UNIT[axis], d))
    if up[1] < 0:
        up = v_mul(up, -1.0)
    a = v_add(top0, v_mul(up, -th / 2))
    b = v_add(top1, v_mul(up, -th / 2))
    return bar(a, b, across, th, name, part, tex, up=up, extend=extend)


def radial(axis, c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box from radius r0 to r1 about `axis` through c, a0..a1 along it, `width` across, at angle `ang`."""
    a = AX[axis]
    u, w = REF[axis]
    lo, hi = [0.0] * 3, [0.0] * 3
    lo[a], hi[a] = a0, a1
    lo[u], hi[u] = c[u] + r0, c[u] + r1
    lo[w], hi[w] = c[w] - width / 2, c[w] + width / 2
    el = box(lo, hi, name, part, tex)
    if abs(ang) > 1e-12:
        rotate([el], axis, math.degrees(ang), c)
    return el


def disc(axis, c, a0, a1, r, name, part, tex, k=4, phase=0.0):
    """A plain round part, a 2k-gon of apothem r: k strips as long as it is across, 180/k degrees apart, each
    a hair shorter than the last so no two ends share a plane."""
    a = AX[axis]
    u, w = REF[axis]
    half = r * math.tan(math.pi / (2 * k))
    st = min(0.012, (a1 - a0) / (4 * k))
    out = []
    for i in range(k):
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[a], hi[a] = a0 + st * i, a1 - st * i
        lo[u], hi[u] = c[u] - r, c[u] + r
        lo[w], hi[w] = c[w] - half, c[w] + half
        el = box(lo, hi, f"{name}{i + 1}" if k > 1 else name, part, tex)
        ang = phase + math.pi * i / k
        if abs(ang) > 1e-12:
            rotate([el], axis, math.degrees(ang), c)
        out.append(el)
    return out


def ring(axis, c, a0, a1, r_in, r_out, n, name, part, tex, phase=0.0, step=0.008, skip=()):
    """A tube of n boxes from r_in to r_out (an n-gon of apothem r_out outside), box i at phase + 2 pi i / n.
    Boxes overlap their neighbours near the bore, so their ends step in by 0, step, 2 step, 3 step round the
    ring: no two that overlap end in one plane. Indices in `skip` are left out (the caller builds them)."""
    w = 2 * r_out * math.tan(math.pi / n)
    out = []
    for i in range(n):
        if i in skip:
            continue
        s = step * (i % 4)
        out.append(radial(axis, c, a0 + s, a1 - s, r_in, r_out, w, phase + TAU * i / n, f"{name}{i + 1}", part, tex))
    return out


def spur(axis, c, a0, a1, R, n, m, phase, name, part, tex, body_r=None, body_k=4):
    """A spur gear: a body to the root (or `body_r`) and n box teeth of module m, tooth 1 at angle `phase`."""
    root, tip = R - 1.25 * m, R + m
    out = disc(axis, c, a0, a1, root if body_r is None else body_r, f"{name}_body", part, tex, k=body_k, phase=phase)
    width = math.pi * m / 2 * 0.9
    out += [radial(axis, c, a0 + 0.03, a1 - 0.03, root - 0.25, tip, width, phase + TAU * i / n, f"{name}_tooth{i + 1}", part, tex)
            for i in range(n)]
    return out


def bevel_geometry(R, R_other, face):
    A = math.hypot(R, R_other)
    return A, math.atan2(R, R_other), (A - face / 2) / A


def bevel(axis, sign, apex, R, R_other, n, m, face, phase, name, part, tex, back=0.8, under=0.0):
    """A bevel gear on `axis` (its direction from the apex towards the gear: sign * the axis) whose pitch cone
    has its apex at `apex`, heel pitch radius R, meshing a gear of heel pitch radius R_other on a shaft at right
    angles through the same apex. Its n box teeth run along the cone from the toe to the heel (`face` long),
    tooth 1 at angle `phase`; behind the heel a back disc to the root radius carries them. `under` lengthens each
    tooth below its root, into the gear's own body (where the other gear's tips never reach), so teeth on a flat
    cone meet the back disc."""
    d = v_mul(UNIT[axis], sign)
    A, g_ang, s = bevel_geometry(R, R_other, face)
    cg, sg = math.cos(g_ang), math.sin(g_ang)
    out = []
    for i in range(n):
        e = radial_dir(axis, phase + TAU * i / n)
        g = v_add(v_mul(d, cg), v_mul(e, sg))
        nn = v_sub(v_mul(e, cg), v_mul(d, sg))
        t = v_cross(g, nn)
        mid = v_add(apex, v_mul(g, A - face / 2))
        tooth = v_add(mid, v_mul(nn, -0.125 * m * s - under / 2))
        out.append(oriented(f"{name}_tooth{i + 1}", [face * 0.96, 2.25 * m * s + under, math.pi * m / 2 * s * 0.8], tooth, [g, nn, t], part, tex))
    a_heel = R_other + 1.25 * m * sg
    a = AX[axis]
    lo_a = apex[a] + sign * a_heel
    hi_a = lo_a + sign * back
    r_back = R - 1.25 * m * cg
    out += disc(axis, list(apex), min(lo_a, hi_a), max(lo_a, hi_a), r_back, f"{name}_back", part, tex, k=8 if r_back > 4 else 4, phase=phase)
    return out


def bevel_pitch_point():
    """The arrastra's bevels' pitch point: under the power shaft, where the pinion meets the wheel."""
    m = BEVEL["m"]
    return [CX, SHAFT_Y - BEVEL["pinion"] * m / 2, CZ + BEVEL["wheel"] * m / 2]


def girth_pitch_point():
    """The ball mill's girth gear's pitch point: on top of the gear, under the pinion on the power shaft."""
    return [CX, DRUM_Y + GIRTH_R, (GIRTH_Z[0] + GIRTH_Z[1]) / 2]


# ---------------------------------------------------------------- the frame: the pit
def build_pit():
    """The pit (frame): the centre stone, two courses of cobbled flags to the curb, the drystone curb round them
    with its overflow notch, and the launder from the notch out to the output face on a post."""
    f = "frame"
    c = (CX, 0.0, CZ)
    out = disc("y", c, 0.0, CENTRE_STONE[1], CENTRE_STONE[0], "fr_centre", f, "granite")
    z0 = CZ - EDGING[1]
    rw = 2 * EDGING[1] / PAVE_ROWS
    reach = (EDGING[0] + EDGING[1]) / 2
    for i in range(PAVE_ROWS):
        za, zb = z0 + rw * i, z0 + rw * (i + 1)
        near = 0.0 if za < CZ < zb else min(abs(za - CZ), abs(zb - CZ))
        half = math.sqrt(max(reach ** 2 - near ** 2, 0.0))
        out.append(box([CX - half, 0.0, za], [CX + half, PAVE_TOP, zb], f"fr_pave{i + 1}", f, "cobble"))
    out += ring("y", c, 0.0, EDGING_TOP, EDGING[0], EDGING[1], EDGING[2], "fr_paveedge", f, "cobble", phase=math.pi / EDGING[2])
    out += ring("y", c, 0.0, CURB_TOP, CURB_R[0], CURB_R[1], CURB_N, "fr_curb", f, "drystone", skip=(NOTCH,))
    w = 2 * CURB_R[1] * math.tan(math.pi / CURB_N)
    out.append(radial("y", c, 0.0, NOTCH_SILL, CURB_R[0], CURB_R[1], w, TAU * NOTCH / CURB_N, "fr_curb_sill", f, "drystone"))
    # the launder: a floor falling from the sill's height to the lip at the face, two sides, a post under its end
    (x0, x1), (z0, z1) = LAUNDER_X, LAUNDER_Z
    zc = (z0 + z1) / 2
    th = 0.6
    out.append(tilted([x0, NOTCH_SILL, zc], [x1, LAUNDER_LIP, zc], z1 - z0, th, "fr_launder_floor", f, "planks", "z"))
    for tag, zs in (("n", (z0 - 0.8, z0)), ("s", (z1, z1 + 0.8))):
        zm = (zs[0] + zs[1]) / 2
        out.append(tilted([x0, NOTCH_SILL + LAUNDER_SIDE, zm], [x1, LAUNDER_LIP + LAUNDER_SIDE, zm], 0.8, LAUNDER_SIDE + th,
                          f"fr_launder_{tag}", f, "planks", "z"))
    out.append(box([x1 - 3.0, 0.0, zc - 1.2], [x1 - 1.4, LAUNDER_LIP - th - 0.02, zc + 1.2], "fr_launder_post", f, "oak"))
    return out


# ---------------------------------------------------------------- the frame: the head frame and the power shaft
def build_head():
    """The head frame (frame): four oak posts set in the curb, two stringers along z on them, the transoms
    between the stringers, a knee brace from each post to its stringer, and the power shaft's two drop hangers
    under T2."""
    f = "frame"
    out = []
    for i, (x0, x1) in enumerate(POST_X, 1):
        for j, (z0, z1) in enumerate(POST_Z, 1):
            out.append(box([x0, 0.0, z0], [x1, TOP[0], z1], f"fr_post{i}{j}", f, "oak"))
        out += boards([x0, TOP[0], 0.0], [x1, TOP[1], CELLS_Z * B], f"fr_stringer{i}_", f, "oak")
        xm = (x0 + x1) / 2
        for j, (zp, sgn) in enumerate(((POST_Z[0][1], 1.0), (POST_Z[1][0], -1.0)), 1):
            a = [xm, TOP[0] - BRACE[1], zp]
            b = [xm, TOP[0], zp + sgn * BRACE[0]]
            out.append(bar(a, b, 2.0, 2.0, f"fr_brace{i}{j}", f, "oak", up=(0.0, 0.0, sgn), extend=0.9))
    inner = (POST_X[0][1], POST_X[1][0])
    for k, (z0, z1) in enumerate((T1_Z, T2_Z), 1):
        out.append(box([inner[0], TOP[0], z0], [inner[1], TOP[1], z1], f"fr_transom{k}", f, "oak"))
    for k, (z0, z1) in enumerate(HANGERS_Z, 1):
        out += hanger((z0, z1), SHAFT_Y, f"fr_hanger{k}", f)
    return out


def hanger(zs, y, name, part):
    """A drop hanger under a transom: a bearing box round a shaft along z at (CX, y), and its stem up to the
    transom's underside."""
    z0, z1 = zs
    return [box([CX - 2.6, y - 2.2, z0], [CX + 2.6, y + 2.2, z1], f"{name}_box", part, "iron"),
            box([CX - 1.4, y + 2.2, z0 + 0.3], [CX + 1.4, TOP[0], z1 - 0.3], f"{name}_stem", part, "iron")]


def build_power():
    """The power shaft (the part `shaft`, the frame's: it turns whatever is fitted): the vanilla axle's cross from
    the south face, then an octagonal iron shaft to its north end; collars on both faces of the first hanger."""
    p = "shaft"
    zf = CELLS_Z * B
    out = [box([CX - 2.0, SHAFT_Y - 1.0, COUPLING_Z], [CX + 2.0, SHAFT_Y + 1.0, zf], "sh_axle_a", p, "axle"),
           box([CX - 1.0, SHAFT_Y - 2.0, COUPLING_Z], [CX + 1.0, SHAFT_Y + 2.0, zf - 0.01], "sh_axle_b", p, "axle")]
    out += disc("z", (CX, SHAFT_Y, 0.0), SHAFT_END, COUPLING_Z + 0.4, SHAFT_R, "sh_rod", p, "steel")
    z0, z1 = HANGERS_Z[0]
    out += disc("z", (CX, SHAFT_Y, 0.0), z0 - 0.6, z0, COLLAR_R, "sh_collar_n", p, "steel")
    out += disc("z", (CX, SHAFT_Y, 0.0), z1, z1 + 0.6, COLLAR_R, "sh_collar_s", p, "steel")
    return out


# ---------------------------------------------------------------- the frame: the hopper, the chute, the water inlet
def build_hopper():
    """The hopper's bin between the stringers (the frame's): four plank walls up to BIN_TOP (stepped up over the
    arrastra's bevel wheel south of HOP_STEP_Z), a floor sloping down north to the outlet over the chute, and the
    steep chute from the outlet down towards the pit's north side. The water pipe's inlet comes through its north
    wall. Above the bin is each level's infeed (build_l1_mouth, build_l2_chute)."""
    f, t = "frame", "planks"
    (x0, x1), (z0, z1), w = HOP_X, HOP_Z, HOP_WALL
    top = BIN_TOP
    out = [box([x0, HOP_LOW, z0], [x1, top, z0 + w], "fr_hopper_n", f, t),
           box([x0, HOP_HIGH, z1 - w], [x1, top, z1], "fr_hopper_s", f, t)]
    for tag, (a, b) in (("w", (x0, x0 + w)), ("e", (x1 - w, x1))):
        out += [box([a, HOP_LOW, z0 + w], [b, top, HOP_STEP_Z], f"fr_hopper_{tag}1", f, t),
                box([a, HOP_HIGH, HOP_STEP_Z], [b, top, z1 - w], f"fr_hopper_{tag}2", f, t)]
    (fz0, fy0), (fz1, fy1) = HOP_FLOOR
    out.append(tilted([CX, fy0, fz0 + 0.1], [CX, fy1, fz1], x1 - x0 - 2 * w, 0.6, "fr_hopper_floor", f, t, "x"))
    # the chute: a floor and two sides from the outlet down, steep
    (cz0, cy0), (cz1, cy1) = CHUTE
    out.append(tilted([CX, cy0, cz0], [CX, cy1, cz1], CHUTE_W, 0.6, "fr_chute_floor", f, t, "x"))
    d = v_norm([0.0, cy1 - cy0, cz1 - cz0])
    up = [0.0, -d[2], d[1]] if d[2] > 0 else [0.0, d[2], -d[1]]
    up = up if up[1] > 0 else v_mul(up, -1.0)
    t_end = (CHUTE_SIDES_END - cz0) / d[2]
    for tag, sgn in (("w", -1.0), ("e", 1.0)):
        xs = CX + sgn * (CHUTE_W / 2 + 0.4)
        a = v_add([xs, cy0, cz0], v_mul(up, CHUTE_SIDE))
        b = v_add(v_add([xs, cy0, cz0], v_mul(d, t_end)), v_mul(up, CHUTE_SIDE))
        out.append(tilted(a, b, 0.8, CHUTE_SIDE + 0.6, f"fr_chute_{tag}", f, t, "x"))
    out += build_inlet()
    return out


def build_inlet(metal=PIPE_METALS[0]):
    """The water inlet, drawn as the rosser draws its drip pipes: ppex's straight pipe as UnifiedPipes draws it
    soldered (Pipes/tools/solderedpipe.py: ppex's 6 x 6 tube round its 4 x 4 bore, the same texture mapping),
    from the water face through the hopper's north wall, on the axis of the ppex pipe beyond the face. It has no
    joint of its own at the face: the pipe placed beyond brings its own half joint, as a soldered ppex pipe does
    against any block. Water runs into the hopper and down the chute with the ore. Built in copper as the part
    `pipecopper`; `pipe_copies` makes the lead part, and whichever metal is supplied is drawn."""
    p = f"pipe{metal}"
    codes = {solderedpipe.BODY: f"#{p}", solderedpipe.SOLDER: "#solder"}
    out = []
    for b in solderedpipe.cull_pressed(solderedpipe.tube("inlet", 2, 0.0, HOP_Z[0] + HOP_WALL, (CX, PIPE_Y))):
        size = b.size()
        faces = {d: {"texture": codes[b.role], "uv": solderedpipe.uv(d, size, TEX / 16)} for d in b.faces}
        out.append(El(f"{p}_{b.name}", size, [(b.lo[k] + b.hi[k]) / 2 for k in range(3)], [row[:] for row in IDENT], faces, p))
    return out


def pipe_copies(els):
    """The pipe in the other metals: the first metal's elements as they are (after the z-fighting fix, so every
    copy gets the same insets), renamed and in their own metal's texture; joints stay solder. Only one metal's
    is ever drawn, so the copies are not checked against each other (the rosser's rule)."""
    first = f"pipe{PIPE_METALS[0]}"
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


def build_frame():
    return build_pit() + build_head() + build_power() + build_hopper()


# ---------------------------------------------------------------- the infeeds above the bin
def build_l1_mouth():
    """Level 1's infeed: the hopper's rim, four plank boards on the bin's walls up to the infeed cell's top face, the
    mouth a player tips ore into."""
    q, t = "l1hopper", "planks"
    (x0, x1), (z0, z1), w = HOP_X, HOP_Z, HOP_WALL
    return [box([x0, BIN_TOP, z0], [x1, HOP_TOP, z0 + w], f"{q}_n", q, t),
            box([x0, BIN_TOP, z1 - w], [x1, HOP_TOP, z1], f"{q}_s", q, t),
            box([x0, BIN_TOP, z0 + w], [x0 + w, HOP_TOP, z1 - w], f"{q}_w", q, t),
            box([x1 - w, BIN_TOP, z0 + w], [x1, HOP_TOP, z1 - w], f"{q}_e", q, t)]


def vanilla_chute_ud():
    """The game's straight chute as a placed chute-straight-ud draws it: VANILLA_CHUTE turned by rotateZ 90 about the
    block's centre, block-local voxels, its faces the game's (disabled ones left out), UVs on this model's texture scale.
    Element templates for build_l2_chute."""
    els = flatten(json.loads(VANILLA_CHUTE), textures={"#copper1": "#chute"})
    for el in els:
        el.faces = {d: {k: v for k, v in f.items() if k != "enabled"} for d, f in el.faces.items() if f.get("enabled", True)}
        for f in el.faces.values():
            f["uv"] = [v * TEX / 16 for v in f["uv"]]
    rotate(els, "z", 90.0, (8.0, 8.0, 8.0))
    return els


def chute_profile():
    """The vanilla ud chute's walls in section, block-local: [(x0, z0, x1, z1)] sorted."""
    out = []
    for el in vanilla_chute_ud():
        lo, hi = el.aabb()
        out.append((round(lo[0], 4), round(lo[2], 4), round(hi[0], 4), round(hi[2], 4)))
    return sorted(out)


def build_l2_chute():
    """Level 2's infeed, for a chute line (the mill tier is chute-fed): the game's chute section set into the bin's
    mouth from BIN_TOP up to the infeed cell's top face, the same section, wall, place in the block and texture as a
    chute-straight-ud in the cell above, so the two meet with no gap or step; plank head boards close the rest of the
    bin's mouth either side of it."""
    q = "l2chute"
    o = [INFEED_CELL[k] * B for k in range(3)]
    out = []
    for t in vanilla_chute_ud():
        lo, hi = t.aabb()
        lo = [lo[0] + o[0], BIN_TOP, lo[2] + o[2]]
        hi = [hi[0] + o[0], o[1] + B, hi[2] + o[2]]
        out.append(from_template(t, lo, hi, f"{q}_{t.name.lower()}", q))
    zs = sorted({round(e.aabb()[0][2], 4) for e in out} | {round(e.aabb()[1][2], 4) for e in out})
    (x0, x1), (z0, z1) = HOP_X, HOP_Z
    out += [box([x0, BIN_TOP, z0], [x1, BIN_TOP + 0.8, zs[0]], f"{q}_board_n", q, "planks"),
            box([x0, BIN_TOP, zs[-1]], [x1, BIN_TOP + 0.8, z1], f"{q}_board_s", q, "planks")]
    return out


# ---------------------------------------------------------------- level 1: the arrastra
def build_l1():
    out = build_l1_mouth()
    c = (CX, 0.0, CZ)
    # the footstep on the centre stone and the top bearing under T1 (fixed)
    s = "l1step"
    (sx0, sx1), (sy0, sy1), (sz0, sz1) = STEP
    out += [box([sx0 - 0.4, sy0, sz0 - 0.4], [sx1 + 0.4, sy0 + 0.6, sz1 + 0.4], f"{s}_sole", s, "iron"),
            box([sx0, sy0 + 0.6, sz0], [sx1, sy1, sz1], f"{s}_block", s, "iron")]
    out += [box([CX - 2.6, TOP_BEARING_Y[0], CZ - 2.6], [CX + 2.6, TOP_BEARING_Y[1], CZ + 2.6], f"{s}_bearing", s, "iron"),
            box([CX - 1.6, TOP_BEARING_Y[1], CZ - 1.6], [CX + 1.6, TOP[0], CZ + 1.6], f"{s}_stem", s, "iron")]
    # the post: its iron gudgeons, the oak post, iron bands either side of the arms, the bevel wheel and its hub
    p = "l1post"
    out += disc("y", c, sy0 + 0.6, POST_Y[0] + 0.4, GUDGEON_R, f"{p}_footgudgeon", p, "steel")
    out += disc("y", c, POST_Y[1] - 0.4, TOP_BEARING_Y[1] - 0.2, GUDGEON_R + 0.2, f"{p}_topgudgeon", p, "steel")
    out += disc("y", c, POST_Y[0], POST_Y[1], POST_R, f"{p}_post", p, "oak")
    out += disc("y", c, ARM_Y[0] - 0.8, ARM_Y[0], POST_R + 0.3, f"{p}_bandlow", p, "iron")
    out += disc("y", c, ARM_Y[1], ARM_Y[1] + 0.8, POST_R + 0.3, f"{p}_bandhigh", p, "iron")
    out += disc("y", c, POST_Y[1] - 1.2, POST_Y[1], POST_R + 0.3, f"{p}_cap", p, "iron")
    m, nw, npn, face = BEVEL["m"], BEVEL["wheel"], BEVEL["pinion"], BEVEL["face"]
    Rw, Rp = nw * m / 2, npn * m / 2
    ph_p = angle_of("z", [0.0, -1.0, 0.0])                # the pinion's tooth straight down, at the wheel
    ph_w = angle_of("y", [0.0, 0.0, 1.0]) + math.pi / nw  # the wheel's gap straight south, under the pinion
    out += bevel("y", -1.0, P, Rw, Rp, nw, m, face, ph_w, f"{p}_wheel", p, "iron", under=0.5)
    A, g, _ = bevel_geometry(Rw, Rp, face)
    a_heel = Rp + 1.25 * m * math.sin(g)
    out += disc("y", c, SHAFT_Y - a_heel - 3.0, SHAFT_Y - a_heel - 0.4, 3.0, f"{p}_hub", p, "iron")
    # the pinion on the power shaft's end
    q = "l1pinion"
    out += bevel("z", 1.0, P, Rp, Rw, npn, m, face, ph_p, f"{q}_bevel", q, "steel")
    A, g, _ = bevel_geometry(Rp, Rw, face)
    toe = CZ + (A - face) * math.cos(g)
    out += disc("z", P, SHAFT_END, toe + 0.2, Rp - 1.25 * m * math.cos(g) - 0.15, f"{q}_hub", q, "steel")
    # the sweep arms, a staple under each, and the drag stones on their bridles
    a_part, s_part = "l1arms", "l1stones"
    for i in range(4):
        ang = math.radians(ARM_ANGLE) + TAU * i / 4
        out.append(radial("y", c, ARM_Y[0], ARM_Y[1], POST_R - 0.3, ARM_R, ARM_W, ang, f"{a_part}_arm{i + 1}", a_part, "oak"))
        out.append(radial("y", c, STAPLE_Y[0], STAPLE_Y[1], EYE_R - 0.6, EYE_R + 0.6, 1.2, ang, f"{a_part}_staple{i + 1}", a_part, "iron"))
        stone = [radial("y", c, PAVE_TOP, STONE_TOP, STONE_R[0], STONE_R[1], STONE_W, 0.0, f"{s_part}_stone{i + 1}", s_part, "basalt")]
        for side, sgn in (("f", 1.0), ("b", -1.0)):
            ex = CX + sgn * EYE_T
            ez = CZ + EYE_R
            stone.append(box([ex - 0.5, STONE_TOP, ez - 0.5], [ex + 0.5, STONE_TOP + 0.8, ez + 0.5], f"{s_part}_eye{i + 1}{side}", s_part, "iron"))
            p0 = [ex, STONE_TOP + 0.5, ez]
            p1 = [CX + sgn * 0.1, STAPLE_Y[0] + 0.3, ez]
            stone.append(bar(p0, p1, 1.0, 1.0, f"{s_part}_chain{i + 1}{side}", s_part, "chain", up=(0.0, 0.0, 1.0), extend=0.3))
        rotate(stone, "y", math.degrees(ang), c)
        out += stone
    return out


# ---------------------------------------------------------------- level 2: the ball mill
DRUM_C = (CX, DRUM_Y, 0.0)


def build_l2():
    out = build_bed() + build_drum()
    # the pinion on the power shaft's end, over the girth gear
    q = "l2pinion"
    out += spur("z", (CX, SHAFT_Y, 0.0), GIRTH_Z[0] + 0.1, GIRTH_Z[1] - 0.1, PINION_R, PINION["teeth"], PINION["m"],
                angle_of("z", [0.0, -1.0, 0.0]), f"{q}_gear", q, "steel")
    out += disc("z", (CX, SHAFT_Y, 0.0), SHAFT_END, HANGERS_Z[0][0] - 0.8, 1.5, f"{q}_hub", q, "steel")
    out += build_feed() + build_charge() + build_l2_chute()
    return out


def build_bed():
    """The bed (fixed): under each trunnion a cast-iron sole plate on the paving, a pedestal on it, and a bearing
    closed round the trunnion on four sides, its cap bolted down."""
    bed = "l2bed"
    out = []
    th = TRUNNION_R[1]
    lo_y, hi_y = DRUM_Y - th, DRUM_Y + th
    for i, (z0, z1) in enumerate(BEARINGS_Z, 1):
        out += [box([SOLE_X[0], PAVE_TOP, z0 - 0.8], [SOLE_X[1], SOLE_TOP, z1 + 0.8], f"{bed}_sole{i}", bed, "iron"),
                box([PEDESTAL_X[0], SOLE_TOP, z0], [PEDESTAL_X[1], lo_y - HOUSING_T, z1], f"{bed}_pedestal{i}", bed, "iron"),
                box([CX - th - HOUSING_T, lo_y - HOUSING_T, z0], [CX + th + HOUSING_T, lo_y, z1], f"{bed}_bearing{i}_low", bed, "iron"),
                box([CX - th - HOUSING_T, hi_y, z0], [CX + th + HOUSING_T, hi_y + HOUSING_T, z1], f"{bed}_bearing{i}_cap", bed, "iron"),
                box([CX - th - HOUSING_T, lo_y, z0], [CX - th, hi_y, z1], f"{bed}_bearing{i}_w", bed, "iron"),
                box([CX + th, lo_y, z0], [CX + th + HOUSING_T, hi_y, z1], f"{bed}_bearing{i}_e", bed, "iron")]
        zm = (z0 + z1) / 2
        for k, xc in enumerate((CX - th - HOUSING_T / 2, CX + th + HOUSING_T / 2), 1):
            out.append(box([xc - 0.5, hi_y + HOUSING_T, zm - 0.5], [xc + 0.5, hi_y + HOUSING_T + 0.6, zm + 0.5], f"{bed}_bearing{i}_bolt{k}", bed, "iron"))
    return out


def build_drum():
    """The drum (turning): two hollow trunnions, two heads, the riveted shell, the manhole, the girth gear on the
    south head and the discharge lip on the south trunnion's end."""
    d = "l2drum"
    out = ring("z", DRUM_C, *N_TRUNNION_Z, *TRUNNION_R, 8, f"{d}_ntrunnion", d, "iron", phase=TAU / 16)
    out += ring("z", DRUM_C, *S_TRUNNION_Z, *TRUNNION_R, 8, f"{d}_strunnion", d, "iron", phase=TAU / 16)
    for tag, (z0, z1) in (("n", N_HEAD_Z), ("s", S_HEAD_Z)):
        out += ring("z", DRUM_C, z0, z1, TRUNNION_R[1] - 0.2, HEAD_R, 16, f"{d}_{tag}head", d, "iron")
    out += ring("z", DRUM_C, SHELL_Z[0] - 0.05, SHELL_Z[1] + 0.05, *SHELL_R, 16, f"{d}_shell", d, "riveted")
    out += ring("z", DRUM_C, LIP[0], LIP[1], TRUNNION_R[0], LIP[2], 8, f"{d}_lip", d, "iron", phase=TAU / 16)
    up = angle_of("z", [0.0, 1.0, 0.0])
    out.append(radial("z", DRUM_C, *MANHOLE_Z, SHELL_R[1] - 0.1, SHELL_R[1] + 0.5, 4.4, up, f"{d}_manhole", d, "iron"))
    for k, (zb, dx) in enumerate(((MANHOLE_Z[0] + 0.6, -1.6), (MANHOLE_Z[0] + 0.6, 1.6), (MANHOLE_Z[1] - 0.6, -1.6), (MANHOLE_Z[1] - 0.6, 1.6)), 1):
        y = DRUM_Y + SHELL_R[1] + 0.5
        out.append(box([CX + dx - 0.5, y, zb - 0.5], [CX + dx + 0.5, y + 0.5, zb + 0.5], f"{d}_manholebolt{k}", d, "iron"))
    m, n = GIRTH["m"], GIRTH["teeth"]
    ph_g = angle_of("z", [0.0, 1.0, 0.0]) + math.pi / n          # a gap straight up, under the pinion
    gz0, gz1 = GIRTH_Z
    root = GIRTH_R - 1.25 * m
    out += ring("z", DRUM_C, gz0, gz1, TRUNNION_R[1] - 0.2, root, 12, f"{d}_girthweb", d, "iron", phase=ph_g)
    width = math.pi * m / 2 * 0.9
    out += [radial("z", DRUM_C, gz0 + 0.03, gz1 - 0.03, root - 0.3, GIRTH_R + m, width, ph_g + TAU * i / n,
                   f"{d}_girth_tooth{i + 1}", d, "iron") for i in range(n)]
    return out


def build_feed():
    """The feed lip (fixed): a short iron trough from the chute's end into the north trunnion's bore, so the ore,
    the water and the middlings that come down the chute go into the drum."""
    fd = "l2feed"
    (z0, y0), (z1, y1) = FEED_LIP
    w = 2.0
    out = [tilted([CX, y0, z0], [CX, y1, z1], w, 0.4, f"{fd}_floor", fd, "iron", "x")]
    for tag, sgn in (("w", -1.0), ("e", 1.0)):
        xs = CX + sgn * (w / 2 + 0.3)
        out.append(tilted([xs, y0 + 1.0, z0], [xs, y1 + 1.0, z1], 0.6, 1.4, f"{fd}_{tag}", fd, "iron", "x"))
    return out


def build_charge():
    """The charge: iron balls in the drum's bottom, its top level: drawn still, it must read right whichever way the
    axle turns, and a heap raised up one side would be right for one direction only. The lowest rest on the shell,
    every other on its neighbours."""
    out = []
    r_in = SHELL_R[0]
    step = BALL - 0.15
    zs = (SHELL_Z[0] + 2.2, (SHELL_Z[0] + SHELL_Z[1]) / 2, SHELL_Z[1] - 2.2)
    n = 0
    for zi, z in enumerate(zs):
        for j in range(4):
            for i in range(-4, 5):
                size = (BALL - 0.2, BALL, BALL + 0.2)[(i + 2 * j + zi) % 3]
                h = size / 2
                y = DRUM_Y - r_in + h - 0.06 + step * 0.86 * j
                x = CX + step * (i + 0.5 * (j % 2)) + 0.3 * (zi - 1)
                if math.hypot(abs(y - DRUM_Y) + h * (1 if y < DRUM_Y else -1), abs(x - CX) + h) > r_in + 0.35:
                    continue
                if y > DRUM_Y - CHARGE_FACE[0] + CHARGE_FACE[1] * (CX - x):
                    continue
                n += 1
                out.append(box([x - h, y - h, z - h], [x + h, y + h, z + h], f"l2balls_ball{n}", "l2balls", "steel"))
    return out


def build():
    return build_frame() + build_l1() + build_l2()


# ---------------------------------------------------------------- the levels
# Each level's parts in the order they go on (one item each; the items are #732's to choose), and the rig parts each
# brings. Level 2 retires level 1's: the ball mill goes into the arrastra's pit, where its post stood, and the chute head
# takes the hopper's rim's place over the bin.
LEVELS = (
    (1, "water", "arrastra", (("l1hopper", ("l1hopper",)), ("l1post", ("l1step", "l1pinion", "l1post")), ("l1arms", ("l1arms",)),
                              ("l1stones", ("l1stones",))), ()),
    (2, "mill", "ball mill", (("l2bed", ("l2bed",)), ("l2drum", ("l2drum",)), ("l2drive", ("l2pinion",)), ("l2balls", ("l2balls",)),
                              ("l2feed", ("l2feed",)), ("l2chute", ("l2chute",))), ("l1hopper", "l1post", "l1arms", "l1stones")),
)


PIPE_PARTS = tuple(f"pipe{m}" for m in PIPE_METALS)   # the water pipe: the frame's, drawn in the metal supplied


def level_of(pid):
    """The level a part belongs to: 0 for the frame's (the frame, the power shaft and the water pipe), else 1 or 2."""
    return int(pid[1]) if pid[:1] == "l" and pid[1:2].isdigit() else 0


def stage_of_part():
    return {pid: req for _, _, _, stages, _ in LEVELS for req, pids in stages for pid in pids}


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def ratios():
    """Each turning part's turn per axle turn, from the teeth: the signs make every mesh's pitch points move
    together (check_gearing holds them to it)."""
    return {"l1post": BEVEL["pinion"] / BEVEL["wheel"], "l2drum": -PINION["teeth"] / GIRTH["teeth"]}


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    k = ratios()

    def rot(axis, pivot, ratio):
        return [{"type": "rotate", "axis": axis, "pivot": pivot, "ratio": r6(ratio)}]

    def part(pid, requires, drivers=(), ride=None):
        return {"id": pid, "match": [f"{pid}_*"], "requires": requires, "ride": ride, "drivers": list(drivers)}

    stage = stage_of_part()
    parts = [{"id": "shaft", "match": ["sh_*"], "requires": None, "ride": None, "drivers": rot("z", pt(CX, SHAFT_Y, 24.0), 1.0)},
             part("l1pinion", stage["l1pinion"], ride="shaft"),
             part("l1post", stage["l1post"], rot("y", pt(CX, 0.0, CZ), k["l1post"])),
             part("l1arms", stage["l1arms"], ride="l1post"),
             part("l1stones", stage["l1stones"], ride="l1post"),
             part("l1step", stage["l1step"]),
             part("l1hopper", stage["l1hopper"]),
             part("l2pinion", stage["l2pinion"], ride="shaft"),
             part("l2drum", stage["l2drum"], rot("z", pt(CX, DRUM_Y, 16.0), k["l2drum"])),
             part("l2bed", stage["l2bed"]),
             part("l2feed", stage["l2feed"]),
             part("l2balls", stage["l2balls"]),
             part("l2chute", stage["l2chute"]),
             *[part(f"pipe{m}", f"pipe{m}") for m in PIPE_METALS],
             {"id": "frame", "match": ["fr_*"], "requires": None, "ride": None, "drivers": []}]
    for p in parts:
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses
REST = 0.0                                   # a pose is the axle's angle, theta (radians)
CYCLE_TURNS = 3                              # every part is back where it started after three turns of the axle


def inputs_of(pose):
    return {"theta": float(pose)}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


def sweep_poses(n=24):
    """Poses spread over the whole cycle: what a turning part sweeps."""
    return [CYCLE_TURNS * TAU * i / n for i in range(n)]


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z) if (x, y, z) not in EMPTY_CELLS]


def output_point():
    """Where the ground pulp leaves: the end of the launder's floor, on the east face."""
    return (CELLS_X * B, LAUNDER_LIP, (LAUNDER_Z[0] + LAUNDER_Z[1]) / 2)


def levels_json():
    return [{"level": lv, "tier": tier, "name": name, "fitted": [req for req, _ in stages], **({"retires": list(ret)} if ret else {})}
            for lv, tier, name, stages, ret in LEVELS]


def make_rig(parts):
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the middle of the north "
                    "side at ground level, under the hopper. The drive comes in high on the south; ore (and at level 2 the "
                    "concentrator's middlings) and water come in by the hopper on the north, the pulp leaves low on the east. "
                    "Model only: no block or renderer reads it yet (#732). levels: each level's tier, its fitted parts in "
                    "order (requires values) and the parts it retires; the frame (requires null) stays. cells: boxes are "
                    "the frame's, fitted the boxes each requires value adds while fitted (turning parts swept over the "
                    "cycle). The only input is theta, the axle's angle. See the grinder's README.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "infeedCell": list(INFEED_CELL),
        "infeedFace": INFEED_FACE,
        "waterCell": list(WATER_CELL),
        "waterFace": WATER_FACE,
        "outputCell": list(OUTPUT_CELL),
        "outputFace": OUTPUT_FACE,
        "output": {"pos": pt(*output_point())},
        "levels": levels_json(),
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
CELL_KEYS = ("powerCell", "infeedCell", "waterCell", "outputCell")


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
    ship["output"] = {"pos": shift_point(rig["output"]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed by the shipped rig: `boxes` from the frame's
    elements (requires null) at rest, and `fitted` per requires value from its parts' elements, a turning part's
    swept over the cycle (sweep_poses) so the boxes hold wherever it is. A cell with neither is hollow. No lids:
    whether the top is walked on is an open question."""
    written = flatten(shape["elements"], textures={})
    req = {p["id"]: p["requires"] for p in ship_parts}
    by_cell = {}
    for w in written:
        pid = part_of(ship_parts, w.name)
        moving = any(q["drivers"] or q.get("ride") for q in ship_parts if q["id"] == pid)
        for pose in (sweep_poses() if moving else (REST,)):
            el = posed(w, _part_matrix(ship_parts, pid, inputs_of(pose)))
            lo, hi = el.aabb()
            for c in cells_touched(lo, hi):
                by_cell.setdefault(c, {}).setdefault(req[pid], []).append(el)
    out = []
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        groups = by_cell.get(pos, {})
        cell = {"pos": list(pos)}
        frame = cell_boxes(groups[None], pos) if None in groups else None
        if frame:
            cell["boxes"] = frame
        fitted = {}
        for r in sorted(k for k in groups if k is not None):
            bs = cell_boxes(groups[r], pos)
            if bs:
                fitted[r] = bs
        if fitted:
            cell["fitted"] = fitted
        if not frame and not fitted:
            cell["hollow"] = True
        out.append(cell)
    return out


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    poses = [REST, 0.7, -2.1, 5.3, 19.0, -33.3]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    span = [(min(c[k] for c in cells), max(c[k] for c in cells)) for k in range(3)]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells x {span[0]}, "
          f"y {span[1]}, z {span[2]}; power {ship['powerCell']} {ship['powerFace']}, infeed {ship['infeedCell']} "
          f"{ship['infeedFace']}, water {ship['waterCell']} {ship['waterFace']}, output {ship['outputCell']} {ship['outputFace']}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
REF_THETAS = (0.0, 0.37, 1.1, -2.3, 2.9, 3.1416, 6.2, -9.7, 15.4, 22.0, -37.7, 113.1)


def reference_json(ship_parts):
    poses = []
    for th in REF_THETAS:
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(th))) for q in ship_parts}
        poses.append({"theta": th, "travel": abs(th), "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped grinder-rig.json's parts: each part's matrix as 3 rows of 4 "
                        "(block units) at each axle angle theta (travel |theta|; nothing else is read). The site's and the "
                        "repository's tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Both levels' parts are here, "
             "each under its requires; keep element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return (REST, 0.9, 2.3)


def state_els(els, k):
    """The elements shown in state k: the frame's, and level k's (k 0: the frame alone)."""
    return [el for el in els if level_of(el.part) in (0, k)]


def fix_coplanar(els, parts):
    """The z-fighting fix, a level at a time: the frame with level 1, with level 2 (the levels are never drawn
    together, so faces of the two in one plane do not fight), round again until a round finds nothing, so the
    frame's insets settle."""
    log = []
    for _ in range(3):
        found = 0
        for k in (1, 2):
            sub = state_els(els, k)
            before, hidden = fix_coplanar_posed(sub, lambda es, pose: [posed(el, pm(parts, el.part, pose)) for el in es],
                                                coplanar_poses(), rounds=20)
            log.append((k, {p: len(v) for p, v in before.items()}, hidden))
            found += sum(len(v) for v in before.values()) + hidden
        if not found:
            break
    return log


def main():
    ap = argparse.ArgumentParser(description="Generate the grinder's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_grinder
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    for el in els:
        if el.part is None:
            raise ValueError(f"{el.name} has no part")
    if not args.quick:
        for k, before, hidden in fix_coplanar(els, parts):
            print(f"coplanar faces before the fix, frame and level {k}: {before}; {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_grinder.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    els += pipe_copies(els)
    if args.out:
        outs = (args.out / "grinder.json", args.out / "grinder_frame.json", args.out / "grinder-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "grinder.json", SHAPE_DIR / "grinder_frame.json", RIG_DIR / "grinder-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_grinder.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship, list_keys=("cells", "levels", "parts")),
             reference_dumps(reference_json(ship_parts)))
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
