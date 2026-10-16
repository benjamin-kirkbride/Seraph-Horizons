#!/usr/bin/env python3
"""Generate the ball mill's shapes, rig and reference poses.

The ball mill is the ore line's continuous grinder (#915, model #916): a Krupp (Gruson) ball mill of the
1890s, a short drum on hollow trunnions turning on its side, half full of iron balls that tumble over the
ore and grind it, and round the drum the screens that let out only what is ground fine enough. Its parts,
west (the feed end) to east (the drive end), along the drum's axis:

    feed box  a sheet-iron box on four legs at the feed trunnion's mouth: the infeed chute through its west
              wall, the ppex water pipe's elbow through its south wall, a V floor down to a level strip, and
              the strip out through its east wall as the feed spout, which runs into the trunnion's bore
    bed       a masonry bed the length of the machine; on it two cast-iron pedestals, each with a bearing
              closed round a trunnion, and two pillow blocks carrying the pinion's shaft
    drum      the hollow west trunnion, two cast end plates (heads), between them the drum of riveted
              grinding plates, and the solid east journal; it turns about its axis at a third of the axle
    screens   round the drum and turning with it: a coarse screen of perforated plate, over it a fine
              screen of wire cloth on twelve longitudinal bars and a middle hoop
    girth     a wooden mortise wheel of 24 oak cogs: two staggered courses of oak felloes in an iron tyre,
              bolted to the drum's cast flange on the east journal, outside the hopper
    pinion    8 stainless teeth on its shaft in the pillow blocks, low at the front; the shaft's end is the vanilla
              axle's cross at the power face: the axle drives the pinion's shaft directly
    hopper    the discharge hopper under the screens: sheet-iron end plates, V sides, a floor falling to a
              channel in the middle, and the output chute out of the channel through the south side

The iron balls inside the drum are not drawn (the owner's ruling).

Everything is built here from plain boxes, the water pipe with the mod's own soldered pipe
(Pipes/tools/solderedpipe.py) and the chute sections as the game's own chute (VANILLA_CHUTE); no other mod's
model is used. No engine, wheel or animal: power comes from a vanilla axle on the power face, and the drum
is symmetric, so it works whichever way the axle turns (no rectifier).

It writes, deterministically,

    ballmill.json         the whole machine, every fitted part                (assets/.../shapes/block/)
    ballmill_frame.json   the static frame only (block and item)             (assets/.../shapes/block/)
    ballmill-rig.json     cells, anchors and the rig                          (assets/.../config/)
    rig-reference.json    every part's matrix at a grid of axle angles       (tests/BallMill/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_ballmill.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
machine box's north-west-bottom corner (the "build frame"); `shipped()` moves it so the controller cell
(ORIGIN_CELL, the middle of the front, north, row at ground level) is [0,0,0]. The machine is three blocks
long (x), two high and two deep (z, away from the player at the front). The drive comes in low at the
front of the east end; the ore comes in high at the west end, the water beside it; the ground pulp leaves
low at the back.

The rig reads theta alone: the axle's angle. The pinion turns with it, the drum (and the screens and the
girth wheel on it) at -8/24 of it; nothing else moves.
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

from machinegen.checks import cell_boxes, cells_touched, with_lids  # noqa: E402
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
REFERENCE_OUT = MOD / "tests" / "BallMill" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/BallMill/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

# ---------------------------------------------------------------- the box, the cells, the anchors
CELLS_X, CELLS_Y, CELLS_Z = 3, 2, 2          # three long (the drum's axis), two high, two deep: 12 cells, all the machine's
ORIGIN_CELL = (1, 0, 0)                      # the controller: the middle of the front (north) row at ground level
POWER_CELL, POWER_FACE = (2, 0, 0), "east"   # the vanilla axle comes in along x at (y 8, z 8): the pinion's shaft
INFEED_CELL, INFEED_FACE = (0, 1, 0), "west"  # a chute line from the west into the feed box (fresh ore, and the
                                             # table's middlings merged into it by the player's chutes)
WATER_CELL, WATER_FACE = (0, 1, 1), "west"   # the ppex water pipe, into the feed box (wet grinding)
OUTPUT_CELL, OUTPUT_FACE = (1, 0, 1), "south"  # the output chute from the hopper's channel, low at the back

TEXTURES = {
    "stonebrick": "game:block/stone/brick/granite1",     # the masonry bed: cut granite
    "iron": "game:block/metal/plate/iron",               # cast iron: pedestals, bearings, heads, the drum's flange; the girth's bolts and tyre
    "sheet": "game:block/metal/sheet/iron1",             # sheet iron: the feed box, the hopper
    "steel": "game:block/metal/sheet-plain/steel1",      # the pinion's shaft and collars
    "stainless": "game:block/metal/plate/stainlesssteel",  # the pinion (the owner's rule: a metal gear is stainless)
    # the girth wheel is wooden (the owner's ruling): oak felloes and cogs, side grain and end grain as the
    # gantry's timbers (EidolonGantry/tools/make_shape.py)
    "oak": "game:block/wood/debarked/oak",
    "oak-end": "game:block/wood/treetrunk/debarked/oak",
    "riveted": "game:block/metal/riveted/iron1",         # the drum's riveted grinding plates
    "perforated": "game:block/metal/mesh1",              # the coarse screen: perforated plate (the game's mesh, holes in it)
    "wirecloth": "game:block/metal/mesh4",               # the fine screen: wire cloth
    "axle": "game:block/wood/planks/generic",            # the vanilla axle's own texture, on its cross where it comes in
    # the water pipe wears ppex's pipe textures as UnifiedPipes gives them, its joints the solder its soldered pipes
    # are wiped with (as the rosser's drip pipes)
    **{f"pipe{m}": f"game:block/metal/sheet-plain/{m}4" for m in ("copper", "lead")},
    "solder": solderedpipe.SOLDER_TEXTURE,
    # the game's chute: a placed chute shows its copper sheet (the pack's lead patch is the chute section item's,
    # patches/unifiedpipes-chutesection.json; the chute block's own texture is the game's)
    "chute": "game:block/metal/sheet/copper1",
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the drum (turning about its axis, along x)
DRUM_Y, DRUM_Z = 20.0, 16.0                  # its axis: high enough for the hopper under it, centred across the depth
DRUM_C = (0.0, DRUM_Y, DRUM_Z)
TRUNNION_R = (3.4, 4.8)                      # the hollow west trunnion: the feed goes in by its bore
TRUNNION_N = 12
HEAD_R = 11.8                                # the cast end plates, a hair over the screens they carry
HEAD_N = 24
W_HEAD_X = (13.3, 14.7)
E_HEAD_X = (38.2, 39.6)
W_TRUNNION_X = (8.6, W_HEAD_X[0])            # from its mouth at the feed box to the head
E_JOURNAL_X = (E_HEAD_X[1] - 0.1, 46.9)      # the east journal: solid (the pulp leaves by the screens, not by it), its end
                                             # in the bearing, under the bearing's end cover
SCREEN_X = (W_HEAD_X[1], E_HEAD_X[0])        # the drum and its screens between the heads: 23.5 long
PLATES_R = (8.2, 9.0)                        # the grinding plates: the drum the balls tumble in
PLATES_N = 16
COARSE_R = (9.5, 9.9)                        # the coarse screen, perforated plate
FINE_R = (10.5, 10.8)                        # the fine screen, wire cloth
SCREEN_N = 24
BARS = {"r": (10.8, 11.4), "n": 12, "w": 1.0}   # the fine screen's longitudinal bars
HOOP = {"r": (10.8, 11.6), "w": 1.0}         # and its middle hoop, a hair prouder than the bars

# ---------------------------------------------------------------- the girth wheel and the pinion
PINION_C = (8.0, 8.0)                        # (y, z): the pinion shaft's axis, the power face's centre
PINION_T, GIRTH_T = 8, 24                    # the drum turns a third of the axle; wooden cogs want a coarse pitch
CENTRES = math.hypot(DRUM_Y - PINION_C[0], DRUM_Z - PINION_C[1])
MODULE = 2 * CENTRES / (PINION_T + GIRTH_T)  # 0.901: the teeth fit the centre distance the cells set
PINION_R = PINION_T * MODULE / 2             # 3.61
GIRTH_R = GIRTH_T * MODULE / 2               # 10.82
PITCH = math.pi * MODULE                     # 2.83 along the pitch circle
COG_W = 0.55 * PITCH                         # a wooden cog is thicker than the metal tooth it meets, as in a mortise
PINION_W = 0.34 * PITCH                      # wheel run with a metal pinion (wood is the weaker)
GIRTH_X = (40.6, 43.0)                       # the wheel: the drum's flange, then the felloes, east of the hopper's end plate
FLANGE = (4.8, 8.2, 40.6, 41.2)              # the drum's cast flange on the journal: radii, x
FLANGE_HUB = (5.2, 43.1)                     # its boss along the journal: outer radius, east end (a thin ring, under the felloes' bore)
FELLOE_R = 5.8                               # the felloes' inside, down over the flange: the wheel's face is oak, not iron
BAND_T = 0.4                                 # the tyre shrunk on round the felloes, under the cogs' roots (clear of the mesh)
FELLOE_X = (FLANGE[3], 42.1, GIRTH_X[1])     # two courses of six felloes, the east course turned half a felloe
FELLOES = 6
COG_TAIL = 0.5                               # a cog's tail shows inside the felloes (mortised through, wedged)
BOLT_R, BOLT_HEAD = 7.6, (1.0, 0.15)         # the bolts through felloes and flange: radius, head across and proud
PINION_X = (41.3, 42.9)                      # the pinion meets the felloes' width
SHAFT_R = 1.0                                # the pinion's shaft (an octagon's apothem)
SHAFT_X = (41.2, 47.6)
PILLOWS_X = ((43.4, 44.6), (45.2, 46.4))     # its two pillow blocks, on the bed
PILLOW_HALF = 2.2                            # a pillow block's bearing: this far each way from the shaft's axis
PILLOW_BASE_Z = (5.2, 10.8)
COLLARS_X = ((43.0, PILLOWS_X[0][0]), (PILLOWS_X[1][1], 46.9))   # a collar outside each pillow block: the shaft located
COLLAR_R = 1.6
CROSS_X = 46.6                               # from here to the power face, the vanilla axle's cross (two 4 x 2 boards)

# ---------------------------------------------------------------- the bed, the pedestals (the frame)
BED_Y = 3.0                                  # the masonry bed's top
BED_X, BED_Z = (0.5, 47.5), (0.5, 31.5)
BEARINGS_X = ((8.8, 12.4), (43.4, 47.0))     # the drum's two bearings, west on the trunnion, east on the journal
HOUSING_T = 1.4                              # a bearing's walls round its trunnion
COVER_T = 0.4                                # the east bearing's end cover
SOLE_T = 0.6
SOLE_Z = (11.0, 21.0)
PEDESTAL_Z = (11.8, 20.2)                    # the pedestal's column under its bearing

# ---------------------------------------------------------------- the feed box
FEED_X = (0.0, 8.2)                          # at the west end, against the infeed and water faces
FEED_Z = (3.2, 21.0)
FEED_Y = (20.0, 29.6)                        # its walls (their foot the infeed chute's floor plate's); the lid on them
FEED_WALL = 0.8
LID_T = 0.8
STRIP_Z = (14.2, 17.8)                       # the level strip at the bottom of its V floor, on the drum's axis
STRIP_TOP = 20.4
FLOOR_HIGH = 21.0                            # the V floor's top at the north and south walls: the infeed chute's floor
SPOUT_X = (FEED_X[1] - FEED_WALL, 11.4)      # the spout: the strip run on out through the east wall into the bore
SPOUT_DROP = 1.0                             # its floor falls this much from the box to its end
SPOUT_SIDE = (0.6, 1.2)                      # its sides: thickness, height over its floor
FLOOR_T = 0.4
LEGS_X = ((0.5, 1.9), (6.4, 7.8))            # under the box's corners, its walls on them
LEGS_Z = ((3.4, 4.8), (19.4, 20.8))
RAIL_Y = (8.0, 9.0)
# The game's straight chute (assets/survival/shapes/block/metal/chute/straight.json, 1.22.7), its elements as the game
# has them: a chute-straight-we is this shape as it is, a chute-straight-ns the same turned by rotateY 90 about the
# block's centre. The infeed and the output are drawn as it, so a chute line meets each with no gap or step
# (validate_ballmill.check_chute_profile, which also holds these numbers to the game's file when the game is installed).
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
CHUTE_FLOOR = 5.0                            # a chute's floor's top over its cell's bottom (the Bottom element's top)
PIPE_METALS = ("copper", "lead")             # the water pipe is ppex's straight pipe in either metal UnifiedPipes adds
WATER_HUB_X = 4.1                            # the water pipe's elbow: its cube's centre (x); y and z the water face's centre
PIPE_END_Z = 19.6                            # its open end, just inside the feed box's south wall

# ---------------------------------------------------------------- the discharge hopper
HOP_END_X = ((12.6, 13.0), (39.9, 40.3))     # its end plates, outboard of the heads and inboard of the bearing and the girth wheel
HOP_T = 0.5                                  # its sheet
HOP_FOOT = ((6.6, 7.0), (25.0, 25.4), 10.0)  # the low walls (z, north and south) up to the V sides' feet (y): the output
                                             # chute goes out through the south one, its top clear under the screens
HOP_TOP = 14.6                               # the V sides' tops: under the trunnions; the wet pulp leaves the drum low
HOP_SLOPE = (3.6, 4.6)                       # the V sides' run outward (z) for their rise (y): about 52 degrees
HOP_BOTTOM = 4.4                             # the walls' and end plates' feet: on four cast feet on the bed
CHANNEL_X = (20.0, 28.0)                     # the floor's middle channel: the output chute's width, under its cell's middle
CHANNEL_Y = (5.6, CHUTE_FLOOR)               # the channel's floor: from the north wall down to the chute's floor
WING_HIGH = 7.2                              # the floor's wings, from the end plates down to the channel's edges
OUT_CHUTE_Z = (HOP_FOOT[1][0], CELLS_Z * B)  # the output chute: from the channel's end through the south wall to the face


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


def angle_of(axis, v):
    """The angle about `axis` of the direction `v` (its part across the axis)."""
    u, w = REF[axis]
    return math.atan2(v[w], v[u])


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


def blocks(lo, hi, name, part, tex, seg=16.0):
    """A box cut into lengths of at most `seg` along x and z, so its texture is not stretched."""
    def cuts(a, b):
        n = max(1, math.ceil((b - a) / seg - 1e-9))
        return [(a + (b - a) * i / n, a + (b - a) * (i + 1) / n) for i in range(n)]
    out = []
    for i, (x0, x1) in enumerate(cuts(lo[0], hi[0])):
        for j, (z0, z1) in enumerate(cuts(lo[2], hi[2])):
            out.append(box([x0, lo[1], z0], [x1, hi[1], z1], f"{name}{i + 1}{j + 1}", part, tex))
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


def ring(axis, c, a0, a1, r_in, r_out, n, name, part, tex, phase=0.0, step=0.008):
    """A tube of n boxes from r_in to r_out (an n-gon of apothem r_out outside), box i at phase + 2 pi i / n.
    Boxes overlap their neighbours near the bore, so their ends step in by 0, step, 2 step, 3 step round the
    ring: no two that overlap end in one plane."""
    w = 2 * r_out * math.tan(math.pi / n)
    out = []
    for i in range(n):
        s = step * (i % 4)
        out.append(radial(axis, c, a0 + s, a1 - s, r_in, r_out, w, phase + TAU * i / n, f"{name}{i + 1}", part, tex))
    return out


def spur(axis, c, a0, a1, R, n, m, phase, name, part, tex, body_r=None, body_k=4, width=None):
    """A spur gear: a body to the root (or `body_r`) and n box teeth of module m (`width` thick, by default
    0.45 of the pitch), tooth 1 at angle `phase`."""
    root, tip = R - 1.25 * m, R + m
    out = disc(axis, c, a0, a1, root if body_r is None else body_r, f"{name}_body", part, tex, k=body_k, phase=phase)
    width = math.pi * m / 2 * 0.9 if width is None else width
    out += [radial(axis, c, a0 + 0.03, a1 - 0.03, root - 0.25, tip, width, phase + TAU * i / n, f"{name}_tooth{i + 1}", part, tex)
            for i in range(n)]
    return out


def pitch_point():
    """The girth wheel's and the pinion's pitch point: on the line of centres, the pinion's radius from its axis."""
    k = PINION_R / CENTRES
    y = PINION_C[0] + (DRUM_Y - PINION_C[0]) * k
    z = PINION_C[1] + (DRUM_Z - PINION_C[1]) * k
    return [(GIRTH_X[0] + GIRTH_X[1]) / 2, y, z]


# ---------------------------------------------------------------- the frame: the bed, the pedestals, the pillow blocks
def build_frame():
    """The frame (requires nothing; the block draws it): the masonry bed, the drum's two cast pedestals on
    sole plates, each with a bearing closed round its trunnion on four sides and its cap bolted down, and the
    pinion shaft's two pillow blocks on the bed."""
    f = "frame"
    out = blocks([BED_X[0], 0.0, BED_Z[0]], [BED_X[1], BED_Y, BED_Z[1]], "fr_bed", f, "stonebrick")
    th = TRUNNION_R[1]
    lo_y, hi_y = DRUM_Y - th, DRUM_Y + th
    for i, (x0, x1) in enumerate(BEARINGS_X, 1):
        out += [box([x0 - 0.2, BED_Y, SOLE_Z[0]], [x1 + 0.1, BED_Y + SOLE_T, SOLE_Z[1]], f"fr_sole{i}", f, "iron"),
                box([x0 + 0.2, BED_Y + SOLE_T, PEDESTAL_Z[0]], [x1 - 0.2, lo_y - HOUSING_T, PEDESTAL_Z[1]], f"fr_pedestal{i}", f, "iron"),
                box([x0, lo_y - HOUSING_T, DRUM_Z - th - HOUSING_T], [x1, lo_y, DRUM_Z + th + HOUSING_T], f"fr_bearing{i}_low", f, "iron"),
                box([x0, hi_y, DRUM_Z - th - HOUSING_T], [x1, hi_y + HOUSING_T, DRUM_Z + th + HOUSING_T], f"fr_bearing{i}_cap", f, "iron"),
                box([x0, lo_y, DRUM_Z - th - HOUSING_T], [x1, hi_y, DRUM_Z - th], f"fr_bearing{i}_n", f, "iron"),
                box([x0, lo_y, DRUM_Z + th], [x1, hi_y, DRUM_Z + th + HOUSING_T], f"fr_bearing{i}_s", f, "iron")]
        xm = (x0 + x1) / 2
        for k, zc in enumerate((DRUM_Z - th - HOUSING_T / 2, DRUM_Z + th + HOUSING_T / 2), 1):
            out.append(box([xm - 0.5, hi_y + HOUSING_T, zc - 0.5], [xm + 0.5, hi_y + HOUSING_T + 0.6, zc + 0.5], f"fr_bearing{i}_bolt{k}", f, "iron"))
    # the east bearing's end cover over the journal's end (the west trunnion runs on through its bearing to the feed box)
    x1 = BEARINGS_X[1][1]
    out.append(box([x1, lo_y - 0.6, DRUM_Z - th - 0.6], [x1 + COVER_T, hi_y + 0.6, DRUM_Z + th + 0.6], "fr_bearing2_cover", f, "iron"))
    py, pz = PINION_C
    h = PILLOW_HALF
    for i, (x0, x1) in enumerate(PILLOWS_X, 1):
        out += [box([x0, BED_Y, PILLOW_BASE_Z[0]], [x1, py - h, PILLOW_BASE_Z[1]], f"fr_pillow{i}_base", f, "iron"),
                box([x0, py - h, pz - h], [x1, py + h, pz + h], f"fr_pillow{i}_bearing", f, "iron")]
    return out


# ---------------------------------------------------------------- the drum, its screens, the girth wheel
def build_drum():
    """The drum (turning): the hollow west trunnion, the two heads, the riveted grinding plates between them, the
    solid east journal and the cast flange on it that carries the girth wheel."""
    d = "drum"
    out = ring("x", DRUM_C, *W_TRUNNION_X, *TRUNNION_R, TRUNNION_N, f"{d}_wtrunnion", d, "iron", phase=TAU / (2 * TRUNNION_N))
    out += ring("x", DRUM_C, *W_HEAD_X, TRUNNION_R[0], HEAD_R, HEAD_N, f"{d}_whead", d, "iron")
    out += ring("x", DRUM_C, *E_HEAD_X, TRUNNION_R[0], HEAD_R, HEAD_N, f"{d}_ehead", d, "iron")
    out += ring("x", DRUM_C, SCREEN_X[0] - 0.05, SCREEN_X[1] + 0.05, *PLATES_R, PLATES_N, f"{d}_plate", d, "riveted")
    out += disc("x", DRUM_C, *E_JOURNAL_X, TRUNNION_R[1], f"{d}_ejournal", d, "iron", k=6, phase=TAU / (2 * TRUNNION_N))
    # the cast flange on the journal that the wooden girth wheel's felloes are bolted to, and its hub along the journal
    r0, r1, x0, x1 = FLANGE
    out += ring("x", DRUM_C, x0, x1, r0 - 0.2, r1, HEAD_N, f"{d}_flange", d, "iron", phase=girth_phase())
    out += ring("x", DRUM_C, x1, FLANGE_HUB[1], r0 - 0.2, FLANGE_HUB[0], 12, f"{d}_flangehub", d, "iron", phase=girth_phase())
    return out


def build_screens():
    """The screens (turning with the drum): the coarse screen of perforated plate round the grinding plates, the
    fine screen of wire cloth over it, carried by twelve longitudinal bars and a middle hoop."""
    s = "screens"
    out = ring("x", DRUM_C, SCREEN_X[0] - 0.05, SCREEN_X[1] + 0.05, *COARSE_R, SCREEN_N, f"{s}_coarse", s, "perforated")
    out += ring("x", DRUM_C, SCREEN_X[0] - 0.05, SCREEN_X[1] + 0.05, *FINE_R, SCREEN_N, f"{s}_fine", s, "wirecloth")
    n = BARS["n"]
    out += [radial("x", DRUM_C, SCREEN_X[0], SCREEN_X[1], *BARS["r"], BARS["w"], TAU * i / n, f"{s}_bar{i + 1}", s, "iron")
            for i in range(n)]
    xm = (SCREEN_X[0] + SCREEN_X[1]) / 2
    out += ring("x", DRUM_C, xm - HOOP["w"] / 2, xm + HOOP["w"] / 2, *HOOP["r"], SCREEN_N, f"{s}_hoop", s, "iron")
    return out


def girth_phase():
    """Tooth 1's angle on the girth wheel: a gap on the line of centres, towards the pinion."""
    return angle_of("x", [0.0, PINION_C[0] - DRUM_Y, PINION_C[1] - DRUM_Z]) + math.pi / GIRTH_T


def pinion_phase():
    """Tooth 1's angle on the pinion: on the line of centres, towards the drum's axis."""
    return angle_of("x", [0.0, DRUM_Y - PINION_C[0], DRUM_Z - PINION_C[1]])


def grain(el, across=False, ends=()):
    """Oak's grain along the part: `across` turns the side grain a quarter on the x faces (a felloe's grain runs
    round the wheel); the faces in `ends` show end grain (a cog's tip and tail)."""
    for d, f in el.faces.items():
        if d in ends:
            f["texture"] = "#oak-end"
        elif across and d in ("east", "west"):
            f["rotation"] = 90
    return el


def build_girth():
    """The girth wheel (turning with the drum), wooden: two courses of six oak felloes, the second turned half a
    felloe so the joints break, on the drum's cast flange and bolted through it, an iron tyre shrunk on round
    them, and 24 oak cogs mortised through felloes and tyre, their tails showing inside."""
    g = "girth"
    m, n = MODULE, GIRTH_T
    root = GIRTH_R - 1.25 * m
    ph = girth_phase()
    out = []
    seg = HEAD_N // FELLOES                  # a felloe: four boxes of a 24-gon
    band_out = root - 0.1                    # the tyre's outside, a hair under the cogs' root
    rim = band_out - BAND_T                  # the felloes' outside, the tyre's inside
    w = 2 * rim * math.tan(math.pi / HEAD_N)
    for k in range(2):
        x0, x1 = FELLOE_X[k], FELLOE_X[k + 1]
        start = ph + math.pi / n + k * math.pi / FELLOES
        for j in range(FELLOES):
            for i in range(seg):
                st = 0.008 * (i % 4)
                el = radial("x", DRUM_C, x0 + st, x1 - st, FELLOE_R, rim, w,
                            start + TAU * (j * seg + i + 0.5) / HEAD_N, f"{g}_felloe{k + 1}_{j + 1}_{i + 1}", g, "oak")
                for f in el.faces.values():    # each felloe its own piece of the board: the joints read
                    f["uv"][0] += (7 * j + 11 * k) % 24
                    f["uv"][2] += (7 * j + 11 * k) % 24
                out.append(grain(el, across=True))
    out += [grain(radial("x", DRUM_C, FELLOE_X[0] + 0.03, GIRTH_X[1] - 0.03, FELLOE_R - COG_TAIL, GIRTH_R + m, COG_W,
                         ph + TAU * i / n, f"{g}_cog{i + 1}", g, "oak"), ends=("up", "down")) for i in range(n)]
    # the iron tyre round the felloes, the cogs coming out through it
    out += ring("x", DRUM_C, FELLOE_X[0] + 0.05, GIRTH_X[1] - 0.05, rim, band_out, 2 * n, f"{g}_band", g, "iron",
                phase=ph + math.pi / (2 * n))
    hw, hp = BOLT_HEAD
    for i in range(2 * FELLOES):
        out.append(radial("x", DRUM_C, GIRTH_X[1], GIRTH_X[1] + hp, BOLT_R - hw / 2, BOLT_R + hw / 2, hw,
                          ph + math.pi / n + math.pi / (2 * FELLOES) + TAU * i / (2 * FELLOES), f"{g}_bolt{i + 1}", g, "iron"))
    return out


# ---------------------------------------------------------------- the pinion and its shaft
def build_pinion():
    """The pinion and its shaft (turning with the axle): the vanilla axle's cross from the power face, the steel
    shaft through the two pillow blocks with a collar outside each, and the 8-tooth stainless pinion on its end."""
    p = "pinion"
    py, pz = PINION_C
    c = (0.0, py, pz)
    xf = CELLS_X * B
    out = [box([CROSS_X, py - 1.0, pz - 2.0], [xf, py + 1.0, pz + 2.0], f"{p}_axle_a", p, "axle"),
           box([CROSS_X + 0.01, py - 2.0, pz - 1.0], [xf, py + 2.0, pz + 1.0], f"{p}_axle_b", p, "axle")]
    out += disc("x", c, SHAFT_X[0], SHAFT_X[1], SHAFT_R, f"{p}_rod", p, "steel")
    for i, (x0, x1) in enumerate(COLLARS_X, 1):
        out += disc("x", c, x0, x1, COLLAR_R, f"{p}_collar{i}", p, "steel")
    out += spur("x", c, *PINION_X, PINION_R, PINION_T, MODULE, pinion_phase(), f"{p}_gear", p, "stainless",
                body_r=PINION_R - 1.25 * MODULE - 0.15, body_k=6, width=PINION_W)
    return out


# ---------------------------------------------------------------- the feed box, its chute and spout
def vanilla_chute(side):
    """The game's straight chute as a placed chute-straight-<side> draws it ('we' as it is, 'ns' turned by rotateY
    90 about the block's centre), block-local voxels, its faces the game's (disabled ones left out), UVs on this
    model's texture scale. Element templates for the infeed and output sections."""
    els = flatten(json.loads(VANILLA_CHUTE), textures={"#copper1": "#chute"})
    for el in els:
        el.faces = {d: {k: v for k, v in f.items() if k != "enabled"} for d, f in el.faces.items() if f.get("enabled", True)}
        for f in el.faces.values():
            f["uv"] = [v * TEX / 16 for v in f["uv"]]
    if side == "ns":
        rotate(els, "y", 90.0, (8.0, 8.0, 8.0))
    return els


def chute_profile(side):
    """A chute's walls in section, block-local, across its run: [(a0, y0, a1, y1)] sorted, a the cross axis (z for
    'we', x for 'ns')."""
    a = 2 if side == "we" else 0
    out = []
    for el in vanilla_chute(side):
        lo, hi = el.aabb()
        out.append((round(lo[a], 4), round(lo[1], 4), round(hi[a], 4), round(hi[1], 4)))
    return sorted(out)


def chute_section(side, cell, run, name, part):
    """The game's chute section in `cell`, cut to `run` (along x for 'we', z for 'ns'): the same walls, place in the
    block and texture as a placed chute there, so a chute in the next cell meets it with no gap or step."""
    a = 0 if side == "we" else 2
    o = [cell[k] * B for k in range(3)]
    out = []
    for t in vanilla_chute(side):
        lo, hi = t.aabb()
        lo = [lo[k] + o[k] for k in range(3)]
        hi = [hi[k] + o[k] for k in range(3)]
        lo[a], hi[a] = run
        out.append(from_template(t, lo, hi, f"{name}_{t.name.lower()}", part))
    return out


def walls_around(name, part, tex, axis, a0, a1, lo, hi, hole):
    """A wall `a0..a1` thick along `axis` (0 x, 2 z), spanning lo..hi over the other two axes (u, y), less the
    rectangular `hole` (u0, y0, u1, y1): up to four boxes round it."""
    u = 2 if axis == 0 else 0
    (ul, yl), (uh, yh) = lo, hi
    hu0, hy0, hu1, hy1 = hole
    rects = []
    if hu0 > ul + 1e-9:
        rects.append((ul, yl, hu0, yh))
    if hu1 < uh - 1e-9:
        rects.append((hu1, yl, uh, yh))
    if hy0 > yl + 1e-9:
        rects.append((hu0, yl, hu1, hy0))
    if hy1 < yh - 1e-9:
        rects.append((hu0, hy1, hu1, yh))
    out = []
    for i, (p0, q0, p1, q1) in enumerate(rects, 1):
        b0, b1 = [0.0] * 3, [0.0] * 3
        b0[axis], b1[axis] = a0, a1
        b0[u], b1[u] = p0, p1
        b0[1], b1[1] = q0, q1
        out.append(box(b0, b1, f"{name}{i}", part, tex))
    return out


def spout_floor(x):
    """The spout's floor's top at x, from the box's east wall down to its end in the bore."""
    x0, x1 = SPOUT_X
    return STRIP_TOP - SPOUT_DROP * (x - x0) / (x1 - x0)


def build_feedbox():
    """The feed box (fixed): a sheet-iron box on four legs at the feed trunnion's mouth. The infeed chute comes
    through its west wall, the water pipe's elbow through its south wall; its floor is a V down to a level strip
    on the drum's axis, and the strip runs on out through the east wall as the feed spout, a trough falling
    into the trunnion's bore. A lid on top."""
    fb = "feedbox"
    (x0, x1), (z0, z1), (y0, y1), w = FEED_X, FEED_Z, FEED_Y, FEED_WALL
    sz0, sz1 = STRIP_Z
    st, sh = SPOUT_SIDE
    out = [box([x0, y0, z0], [x1, y1, z0 + w], f"{fb}_n", fb, "sheet")]
    # the south wall, round the water pipe's run
    hx, hy, hz = WATER_HUB_X, *water_face_centre()[1:]
    h = solderedpipe.HALF
    out += walls_around(f"{fb}_s", fb, "sheet", 2, z1 - w, z1, (x0, y0), (x1, y1), (hx - h, hy - h, hx + h, hy + h))
    # the west wall, round the infeed chute
    ci = [INFEED_CELL[k] * B for k in range(3)]
    out += walls_around(f"{fb}_w", fb, "sheet", 0, x0, x0 + w, (z0 + w, y0), (z1 - w, y1),
                        (ci[2] + 4.0, ci[1] + 4.0, ci[2] + 12.0, ci[1] + 12.0))
    # the east wall, round the spout
    out += walls_around(f"{fb}_e", fb, "sheet", 0, x1 - w, x1, (z0 + w, y0), (z1 - w, y1), (sz0 - st, y0, sz1 + st, STRIP_TOP + sh + 0.2))
    out.append(box([x0, y1, z0], [x1, y1 + LID_T, z1], f"{fb}_lid", fb, "sheet"))
    # the V floor and the strip at its bottom
    xm, across = (x0 + w + x1 - w) / 2, x1 - x0 - 2 * w
    out.append(tilted([xm, FLOOR_HIGH, z0 + w], [xm, STRIP_TOP, sz0], across, FLOOR_T, f"{fb}_floor_n", fb, "sheet", "x"))
    out.append(tilted([xm, FLOOR_HIGH, z1 - w], [xm, STRIP_TOP, sz1], across, FLOOR_T, f"{fb}_floor_s", fb, "sheet", "x"))
    out.append(box([x0 + w, STRIP_TOP - FLOOR_T, sz0], [x1 - w, STRIP_TOP, sz1], f"{fb}_strip", fb, "sheet"))
    # the spout: its floor and two sides, from the east wall's inside into the bore
    a, b = SPOUT_X
    zc = (sz0 + sz1) / 2
    out.append(tilted([a, spout_floor(a), zc], [b, spout_floor(b), zc], sz1 - sz0, FLOOR_T, f"{fb}_spout_floor", fb, "iron", "z"))
    for tag, zs in (("n", sz0 - st / 2), ("s", sz1 + st / 2)):
        out.append(tilted([a, spout_floor(a) + sh, zs], [b, spout_floor(b) + sh, zs], st, sh + FLOOR_T, f"{fb}_spout_{tag}", fb, "iron", "z"))
    # the legs, and rails between them
    for i, (lx0, lx1) in enumerate(LEGS_X, 1):
        for j, (lz0, lz1) in enumerate(LEGS_Z, 1):
            out.append(box([lx0, BED_Y, lz0], [lx1, y0, lz1], f"{fb}_leg{i}{j}", fb, "iron"))
        out.append(box([lx0, RAIL_Y[0], LEGS_Z[0][1]], [lx1, RAIL_Y[1], LEGS_Z[1][0]], f"{fb}_rail_x{i}", fb, "iron"))
    for j, (lz0, lz1) in enumerate(LEGS_Z, 1):
        out.append(box([LEGS_X[0][1], RAIL_Y[0] + 0.1, lz0 + 0.1], [LEGS_X[1][0], RAIL_Y[1] - 0.1, lz1 - 0.1], f"{fb}_rail_z{j}", fb, "iron"))
    return out


def water_face_centre():
    c = WATER_CELL
    return (c[0] * B, c[1] * B + B / 2, c[2] * B + B / 2)


def build_inlet(metal=PIPE_METALS[0]):
    """The water pipe, drawn as the rosser draws its drip pipes: ppex's straight pipe as UnifiedPipes draws it
    soldered (Pipes/tools/solderedpipe.py: ppex's 6 x 6 tube round its 4 x 4 bore), from the water face on the axis
    of the ppex pipe beyond it, into an elbow (the pipe's own cube, as UnifiedPipes' soldered bend), and from the
    elbow north through the feed box's south wall, its open end just inside. It has no joint of its own at the
    face: the pipe placed beyond brings its own half joint, as a soldered ppex pipe does against any block. Built
    in copper as the part `pipecopper`; `pipe_copies` makes the lead part, and whichever metal is supplied is
    drawn."""
    p = f"pipe{metal}"
    _, wy, wz = water_face_centre()
    h = solderedpipe.HALF
    boxes = solderedpipe.hub("elbow", (WATER_HUB_X, wy, wz), ("west", "north"))
    boxes += solderedpipe.tube("inlet", 0, 0.0, WATER_HUB_X - h, (wz, wy))
    boxes += solderedpipe.tube("run", 2, PIPE_END_Z, wz - h, (WATER_HUB_X, wy))
    codes = {solderedpipe.BODY: f"#{p}", solderedpipe.SOLDER: "#solder"}
    out = []
    for b in solderedpipe.cull_pressed(boxes):
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


# ---------------------------------------------------------------- the discharge hopper and the output chute
def v_side_z(foot_z, y, sign):
    """Where a V side's inner face is at height y: out from its foot at foot_z (sign -1 north, +1 south)."""
    return foot_z + sign * HOP_SLOPE[0] * (y - HOP_FOOT[2]) / HOP_SLOPE[1]


def build_hopper():
    """The discharge hopper (fixed) under the screens: two sheet-iron end plates outboard of the heads, low walls
    north and south, V sides from their tops out under the screens, a floor whose wings fall from the end plates to
    the middle channel and whose channel falls south to the output chute; the chute (the game's section) runs from
    the channel's end through the south wall and the V side to the output face, a bracket under it on the bed; four
    cast feet carry the hopper on the bed."""
    hp = "hopper"
    (wx0, wx1), (ex0, ex1) = HOP_END_X
    (nz0, nz1), (sz0, sz1), foot_y = HOP_FOOT
    t = HOP_T
    xa, xb = wx1, ex0                        # between the end plates
    cx0, cx1 = CHANNEL_X
    top_n = v_side_z(nz1, HOP_TOP, -1.0)
    top_s = v_side_z(sz0, HOP_TOP, 1.0)
    out = []
    for tag, (x0, x1) in (("w", (wx0, wx1)), ("e", (ex0, ex1))):
        out.append(box([x0, HOP_BOTTOM, nz0], [x1, foot_y, sz1], f"{hp}_end{tag}_low", hp, "sheet"))
        out.append(box([x0, foot_y, top_n - 0.6], [x1, HOP_TOP, top_s + 0.6], f"{hp}_end{tag}_high", hp, "sheet"))
    out.append(box([xa, HOP_BOTTOM, nz0], [xb, foot_y, nz1], f"{hp}_wall_n", hp, "sheet"))
    out.append(box([xa, HOP_BOTTOM, sz0], [cx0, foot_y, sz1], f"{hp}_wall_s1", hp, "sheet"))
    out.append(box([cx1, HOP_BOTTOM, sz0], [xb, foot_y, sz1], f"{hp}_wall_s2", hp, "sheet"))
    # the V sides: the north whole, the south round the output chute (cut below the chute's top)
    xm = (xa + xb) / 2
    out.append(tilted([xm, foot_y, nz1], [xm, HOP_TOP, top_n], xb - xa, t, f"{hp}_side_n", hp, "sheet", "x", extend=0.1))
    for tag, (p0, p1) in (("1", (xa, cx0)), ("2", (cx1, xb))):
        out.append(tilted([(p0 + p1) / 2, foot_y, sz0], [(p0 + p1) / 2, HOP_TOP, top_s], p1 - p0, t, f"{hp}_side_s{tag}", hp, "sheet", "x", extend=0.1))
    chute_top = OUTPUT_CELL[1] * B + 12.0
    out.append(tilted([(cx0 + cx1) / 2, chute_top, v_side_z(sz0, chute_top, 1.0)], [(cx0 + cx1) / 2, HOP_TOP, top_s], cx1 - cx0, t,
                      f"{hp}_side_s3", hp, "sheet", "x", extend=0.1))
    # the floor: the wings fall from the end plates to the channel's edges, the channel south to the chute's floor
    zc, zw = (nz1 + sz0) / 2, sz0 - nz1
    out.append(tilted([xa, WING_HIGH, zc], [cx0, CHANNEL_Y[0], zc], zw, t, f"{hp}_floor_w", hp, "sheet", "z"))
    out.append(tilted([cx1, CHANNEL_Y[0], zc], [xb, WING_HIGH, zc], zw, t, f"{hp}_floor_e", hp, "sheet", "z"))
    out.append(tilted([(cx0 + cx1) / 2, CHANNEL_Y[0], nz1], [(cx0 + cx1) / 2, CHANNEL_Y[1], sz0], cx1 - cx0, t + 0.1,
                      f"{hp}_channel", hp, "sheet", "x"))
    # four feet on the bed, under the end plates' corners
    for tag, (x0, x1) in (("w", (wx0, wx0 + 1.0)), ("e", (ex1 - 1.0, ex1))):
        for k, (z0, z1) in enumerate(((nz0, nz0 + 1.0), (sz1 - 1.0, sz1)), 1):
            out.append(box([x0, BED_Y, z0], [x1, HOP_BOTTOM, z1], f"{hp}_foot{tag}{k}", hp, "iron"))
    # the output chute's bracket on the bed by the face (the chute itself is a service: build_chutes)
    zf = CELLS_Z * B
    out.append(box([cx0 + 1.0, BED_Y, zf - 3.0], [cx1 - 1.0, OUTPUT_CELL[1] * B + 4.0, zf - 1.0], f"{hp}_bracket", hp, "iron"))
    return out


def build_chutes():
    """The two chute sections, a service fitted last: the infeed through the feed box's west wall, and the output
    from the hopper's channel through its south wall to the output face, each the game's section where a placed
    chute meets it."""
    x0 = FEED_X[0]
    return (chute_section("we", INFEED_CELL, (x0, x0 + FEED_WALL), "chutes_in", "chutes")
            + chute_section("ns", OUTPUT_CELL, OUT_CHUTE_Z, "chutes_out", "chutes"))


def build():
    return (build_pinion() + build_drum() + build_girth() + build_screens() + build_feedbox()
            + build_hopper() + build_chutes() + build_inlet() + build_frame())


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


PIPE_PARTS = tuple(f"pipe{m}" for m in PIPE_METALS)   # the water pipe: drawn in the metal supplied
TURNING = ("pinion", "drum", "girth", "screens")      # the parts that move (the pinion with the axle, the rest on the drum)


def ratios():
    """Each turning part's turn per axle turn, from the teeth: the girth wheel turns the other way to the pinion
    (check_gearing holds the pitch points together)."""
    return {"pinion": 1.0, "drum": -PINION_T / GIRTH_T}


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    k = ratios()

    def rot(pivot, ratio):
        return [{"type": "rotate", "axis": "x", "pivot": pivot, "ratio": r6(ratio)}]

    def part(pid, drivers=(), ride=None, requires=None):
        return {"id": pid, "match": [f"{pid}_*"], "requires": requires or pid, "ride": ride, "drivers": list(drivers)}

    parts = [part("pinion", rot(pt(24.0, *PINION_C), k["pinion"])),
             part("drum", rot(pt(24.0, DRUM_Y, DRUM_Z), k["drum"])),
             part("girth", ride="drum"),
             part("screens", ride="drum"),
             part("feedbox"),
             part("hopper"),
             part("chutes"),
             *[part(pid) for pid in PIPE_PARTS],
             {"id": "frame", "match": ["fr_*"], "requires": None, "ride": None, "drivers": []}]
    for p in parts:
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# The build (owner, 2026-10-15): the frame (bed, pedestals, bearings, the pinion's pillow blocks) is placed and
# needs nothing; then each stage is one group of requires, fitted in order, its fits in order, services last.
BUILD_STAGES = (                             # (id, name, its requires in the order fitted)
    ("drum", "the drum", ("drum",)),
    ("gearing", "the gearing", ("girth", "pinion")),
    ("screens", "the screens", ("screens",)),
    ("sheetiron", "the sheet iron", ("hopper", "feedbox")),
    ("services", "services", ("chutes",) + PIPE_PARTS),   # the water pipe in one metal: a choice
)
# The parts in the order they are fitted, for the build check: each must hold on to what is already there.
FIT_ORDER = tuple(r for _, _, g in BUILD_STAGES for r in g if r not in PIPE_PARTS[1:])
# The build's states, one a stage, named for it (the scenario's Build stage select): what is fitted, cumulative,
# the water pipe in its first metal.
BUILD_STATES = (("frame", ()),) + tuple(
    (sid, tuple(r for _, _, g in BUILD_STAGES[:i + 1] for r in g if r not in PIPE_PARTS[1:]))
    for i, (sid, _, _) in enumerate(BUILD_STAGES))

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
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


def output_point():
    """Where the ground pulp leaves: the middle of the output chute's floor, on the output face."""
    return ((CHANNEL_X[0] + CHANNEL_X[1]) / 2, OUTPUT_CELL[1] * B + CHUTE_FLOOR, CELLS_Z * B)


def make_rig(parts):
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the middle of the front "
                    "(north) row at ground level. The drum's axis runs along x; the drive comes in low at the front of the east "
                    "end onto the pinion's shaft, the ore (fresh, and the table's middlings merged into the same chute line) by "
                    "a chute into the feed box high on the west end, the water by a ppex pipe beside it, and the ground pulp "
                    "leaves by the output chute low at the back. Model only: no block or renderer reads it yet (#915). "
                    "buildStages: after the placed frame (bed, pedestals, bearings), what each stage adds, in order: the drum, "
                    "the gearing, the screens, the sheet iron (hopper and feed box), then services (the chutes and the water "
                    "pipe, in one metal). The only input is theta, the axle's angle. See the ball mill's README.",
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
        "buildStages": [{"stage": n, "id": sid, "name": name, "adds": list(g)} for n, (sid, name, g) in enumerate(BUILD_STAGES, 1)],
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
    """The cells' boxes from the shipped shape as written, posed by the shipped rig: every fitted part's elements
    (the water pipe in its first metal; the others are the same boxes), a turning part's swept over the cycle
    (sweep_poses) so the boxes hold wherever it is; then a lid on every column's top cell, so no one walking over
    the drum drops between the boxes."""
    written = flatten(shape["elements"], textures={})
    moving = {q["id"] for q in ship_parts if q["drivers"] or q.get("ride")}
    by_cell = {}
    for w in written:
        pid = part_of(ship_parts, w.name)
        if pid in PIPE_PARTS[1:]:
            continue
        for pose in (sweep_poses() if pid in moving else (REST,)):
            el = posed(w, _part_matrix(ship_parts, pid, inputs_of(pose)))
            lo, hi = el.aabb()
            for c in cells_touched(lo, hi):
                by_cell.setdefault(c, []).append(el)
    out = []
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        boxes = cell_boxes(by_cell[pos], pos) if pos in by_cell else None
        out.append({"pos": list(pos), "boxes": boxes} if boxes else {"pos": list(pos), "hollow": True})
    return with_lids(out)


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
    return {"_comment": f"Generated by {SCRIPT} from the shipped ballmill-rig.json's parts: each part's matrix as 3 rows of 4 "
                        "(block units) at each axle angle theta (travel |theta|; nothing else is read). The site's and the "
                        "repository's tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod; the chute sections are the game's "
             "chute's walls, the water pipe the mod's soldered pipe. Every fitted part is here, each under its requires; keep "
             "element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return (REST, 0.9, 2.3, 4.1)


def fix_coplanar(els, parts):
    """The z-fighting fix over the whole machine, every part fitted (the pipe in its first metal), round again
    until a round finds nothing, so the insets settle."""
    log = []
    for _ in range(3):
        before, hidden = fix_coplanar_posed(els, lambda es, pose: [posed(el, pm(parts, el.part, pose)) for el in es],
                                            coplanar_poses(), rounds=20)
        log.append(({p: len(v) for p, v in before.items()}, hidden))
        if not sum(len(v) for v in before.values()) + hidden:
            break
    return log


def main():
    ap = argparse.ArgumentParser(description="Generate the ball mill's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_ballmill
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    for el in els:
        if el.part is None:
            raise ValueError(f"{el.name} has no part")
    if not args.quick:
        for before, hidden in fix_coplanar(els, parts):
            print(f"coplanar faces before the fix: {before}; {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_ballmill.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    els += pipe_copies(els)
    if args.out:
        outs = (args.out / "ballmill.json", args.out / "ballmill_frame.json", args.out / "ballmill-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "ballmill.json", SHAPE_DIR / "ballmill_frame.json", RIG_DIR / "ballmill-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_ballmill.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
