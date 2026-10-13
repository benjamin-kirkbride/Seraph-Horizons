#!/usr/bin/env python3
"""Generate the vanner's shapes, rig and reference poses.

The vanner is the last of the ore line's concentrators (#923; model #924): a Frue vanner (1874), an
endless rubber belt that creeps slowly up to its head roller under a spray of wash water while its
frame is shaken from side to side. The heavy grains ride up over the head roller and wash off into the
concentrate box; the light ones run back down the belt and off its foot into the tailings box. One
real machine, one level.

    the frame         oak sills, posts and pedestals, standing still: the pins the shaking frame's
                      uprights stand on, the countershaft's bearings, the posts that carry the feed box and the
                      spray pipe, and the two boxes, each ending in a vanilla chute section
    the countershaft  along the east side, coaxial with the vanilla axle at the power face (south, at
                      the back): two eccentric sheaves keyed on it, each running in an iron yoke on
                      the shaking frame, so the frame is shaken across (x) once a turn, a sine of the
                      axle's angle, alike whichever way the axle turns
    the shaking frame two oak rails on six swinging oak uprights (the period's flexible uprights, drawn as
                      rigid posts pinned top and bottom: a parallel motion that carries the frame across
                      and holds it along its length), tied at the ends and the middle; on them the head
                      and foot rollers' bearings, and the hangers of the worm shaft
    the belt          round the head roller (north, at the front, the higher) and the foot roller
                      (south), with raised edges along its top run
    the creep         a ratchet on the worm shaft, which a pawl fixed to the frame pushes a tooth each
                      stroke of the shake (and slips back under it on the return), so it turns one way
                      whichever way the axle turns; the worm on the same shaft turns the worm wheel on
                      the head roller's journal a tooth a turn, and the head roller draws the belt's
                      top run up to the head
    the feed box      over the belt below the head, the infeed chute coming down into it through the
                      feed cell's top face; the pulp spills over its weir down an apron onto the belt
    the water line    Pipes and Power Expanded's pipe drawn soldered in copper or lead
                      (Pipes/tools/solderedpipe.py, as the rosser's drip), from the water cell's top
                      face down and round to a perforated spray pipe across the belt near the head; one
                      rig part per metal, `pipecopper` and `pipelead`, the same elements, one drawn

Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    vanner.json           the whole machine, every part                    (assets/.../shapes/block/)
    vanner_frame.json     the frame only (block and item)                  (assets/.../shapes/block/)
    vanner-rig.json       cells, anchors and the part rig                  (assets/.../config/)
    rig-reference.json    every part's matrix at a spread of poses         (tests/Vanner/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_vanner.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
machine box's north-west-bottom corner (the "build frame"). The player stands north of the machine,
which runs away from them (south, z); the controller is the front west cell, the build frame's
[0,0,0], so the shipped files are the build frame divided by 16.

The rig's inputs (README "Rig schema"):

    theta   the axle's angle: the countershaft turns with it, and the shaking frame follows its eccentrics
    travel  psi, the axle's travel either way: the ratchet and worm, and the rollers they creep, which only
            ever turn forward
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
from machinegen.output import (reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

import solderedpipe  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "Vanner" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/Vanner/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
DEG = math.pi / 180

# ---------------------------------------------------------------- the box, the cells, the anchors
CELLS_X, CELLS_Y, CELLS_Z = 2, 1, 3          # two wide (the belt; the countershaft), one high, three long
X_LEN, Y_LEN, Z_LEN = CELLS_X * B, CELLS_Y * B, CELLS_Z * B
ORIGIN_CELL = (0, 0, 0)                      # the controller: the front west cell, the belt's head
POWER_CELL, POWER_FACE = (1, 0, 2), "south"  # the vanilla axle comes in along z at (x 24, y 8), at the back
WATER_CELL, WATER_FACE = (1, 0, 0), "up"     # a ppex pipe comes down onto the inlet at (x 24, z 8)
FEED_CELL, FEED_FACE = (0, 0, 1), "up"       # a chute from above meets the infeed section at (x 8, z 24)
CONC_CELL, CONC_FACE = (0, 0, 0), "north"    # the concentrate chute leaves at (x 8, y 8), the front
TAIL_CELL, TAIL_FACE = (0, 0, 2), "south"    # the tailings chute leaves at (x 8, y 8), the back

# The fitted parts (requires), in the order the floating check builds the machine up (the build stages
# are the owner's to settle: README "Open for the owner"); the water line is one of PIPES.
REQUIRES = ("countershaft", "shaker", "rollers", "belt", "creep", "feedbox")
PIPE_METALS = ("copper", "lead")             # the water line's metals: one part each, the supplied one drawn
PIPES = tuple(f"pipe{m}" for m in PIPE_METALS)

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",         # timber: sills, posts, pedestals, rails, ties, bearers, rollers
    "planks": "game:block/wood/planks/oak1",       # boards: the feed box, the concentrate and tailings boxes
    "iron": "game:block/metal/plate/iron",         # castings and straps: bearings, cheeks, clevises, yokes, arms, hangers, pawl
    "steel": "game:block/metal/sheet-plain/steel1",  # shafts, sheaves, collars, the worm, its wheel, the ratchet
    "rubber": "game:block/leather/black",          # the belt and its edges (no rubber in the game: black leather)
    "chute": "game:block/metal/sheet/copper1",     # the chute sections: vanilla's chute, as a placed chute shows
    "pipecopper": "game:block/metal/sheet-plain/copper4",   # the water line in copper (UnifiedPipes' copper pipe)
    "pipelead": "game:block/metal/sheet-plain/lead4",       # ... in lead (UnifiedPipes' lead pipe)
    "solder": solderedpipe.SOLDER_TEXTURE,         # the pipes' joints, the reducer and the spray pipe's cap
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the belt and the rollers
# A 4 ft belt 12 ft long (#924): 11 wide, the rollers 31 apart, at about 2.75 voxels a foot
BELT_X = (4.5, 15.5)                         # the belt's width
HEAD = (9.3, 7.0)                            # (y, z): the head roller's axis, at the front, the higher end
FOOT = (8.1, 38.0)                           # the foot roller's: the belt falls 1.2 in 31 to the foot (1 in 26)
ROLL_R = 2.0                                 # both rollers (a 12-sided drum, apothem)
ROLL_X = (4.3, 15.7)                         # the drums, a little wider than the belt
BELT_T = 0.4
EDGE_W, EDGE_H = 0.7, 0.6                    # the raised edges along the top run, inside the belt's sides
JOURNAL_R = 0.5
COLLAR_R = 1.0

# ---------------------------------------------------------------- the shaking frame (the shaker)
E = 0.5                                      # the eccentrics' throw: the frame shakes 1 voxel across, a sine of theta
RAILS_X = ((2.5, 4.0), (16.0, 17.5))         # the two oak rails, west and east of the belt
RAIL_Y = (7.0, 8.5)
RAIL_Z = (2.0, 42.2)
TIE_Y = (7.3, 8.5)                           # the ties between the rails: the ends beyond the belt, one inside its loop
TIES_Z = ((3.6, 4.2), (22.2, 22.8), (41.6, 42.2))
BRG = 1.0                                    # a roller's bearing block: 2 x 2 about its journal
BRG_X = ((2.3, 4.2), (15.8, 17.7))           # the bearing blocks on the rails, west and east
UPRIGHTS_Z = (16.6, 27.0, 34.0)              # the uprights under each rail, on its centre line
UP_PIVOTS_Y = (1.9, 6.3)                     # each upright's pins: at its foot (on the sill) and at its head (under the rail)
UP_LEN = UP_PIVOTS_Y[1] - UP_PIVOTS_Y[0]
UP_SWING = math.asin(E / UP_LEN)             # how far an upright leans at the end of a stroke (radians)
UP_Y = (1.3, 6.8)                            # each upright's ends
UP_W, UP_D = 1.2, 1.0                        # across (x) and along (z)
PIN_R = 0.35
CHEEK_GAP, CHEEK_W = 0.05, 0.4               # the cheeks either side of an upright's ends: the frame's at the foot, the rail's clevis at the head
CHEEK_X = 0.7                                # their half width across
FOOT_CHEEK_Y = (1.0, 2.6)
HEAD_CHEEK_Y = (5.6, RAIL_Y[0])
SILL_Y = 1.0                                 # the sills' tops

# ---------------------------------------------------------------- the countershaft and the eccentrics
CS = (24.0, 8.0)                             # (x, y): along z, on the power cell's centre line
CS_Z = (15.6, 48.0)
ENTRY_Z0 = 45.4                              # the vanilla axle's oak cross from here to the power face
CS_R = 0.7
CS_BEARINGS_Z = ((16.0, 17.4), (28.8, 30.2), (42.4, 43.8))
CS_BRG = 1.3                                 # the bearing blocks: 2.6 square about the shaft
PEDESTAL_X = (22.9, 25.1)
CS_SILL_X = (22.4, 25.6)
CS_SILL_Z = (15.4, 44.4)
SHEAVES_Z = (22.5, 36.0)                     # the eccentric sheaves on the countershaft
SHEAVE_R = 1.75                              # a 12-sided disc, apothem
SHEAVE_RC = SHEAVE_R / math.cos(math.pi / 12)    # its corners: the yokes clear them
SHEAVE_W = 1.4
YOKE_T = 0.7                                 # the yokes' bars
YOKE_GAP = 0.3                               # a yoke's top and bottom bars clear the sheave's swing by this
ARM_Y = (7.3, 8.3)                           # each yoke's arm to the east rail

# ---------------------------------------------------------------- the creep: worm wheel, worm, ratchet, pawl
WHEEL_N, WHEEL_MOD = 12, 0.6                 # the head roller's worm wheel: 12 teeth, module 0.6
WHEEL_R = WHEEL_N * WHEEL_MOD / 2            # its pitch radius, 3.6
WHEEL_X = (18.9, 20.3)                       # on the head roller's journal, east of the east rail
WORM = (19.6, 4.3)                           # (x, y): the worm shaft, along z under the wheel, on the shaker
WORM_R = HEAD[0] - WHEEL_R - WORM[1]         # the worm's pitch radius: the wheel's pitch circle on it, 1.4
WORM_LEAD = math.pi * WHEEL_MOD              # a single-start worm: one tooth a turn
WORM_TILT = 7.0                              # the thread rings' lean (degrees about x): drawn right-handed
WORM_SHAFT_Z = (1.6, 14.0)
WORM_BEARINGS_Z = ((2.2, 3.2), (10.8, 11.8))
WORM_BRG = 1.3
RATCHET_N = 12
RATCHET_R = 2 * E * RATCHET_N / TAU          # its pitch radius: a tooth's pitch (2 pi R / N) is the shake's stroke
RATCHET_MOD = 0.45
RATCHET_Z = (12.6, 13.4)
RATCHET_LEAN = -20.0                         # its saw teeth lean this far (degrees about z) about their roots


def ratchet_reach():
    """How far the ratchet's leaned teeth reach from its axis: the farthest corner of a tooth."""
    root, tip = RATCHET_R - 1.25 * RATCHET_MOD, RATCHET_R + RATCHET_MOD
    w = math.pi * RATCHET_MOD / 2
    a = math.radians(RATCHET_LEAN)
    out = 0.0
    for x, y in ((-w / 2, -0.25), (w / 2, -0.25), (-w / 2, tip - root), (w / 2, tip - root)):
        X, Y = x * math.cos(a) - y * math.sin(a), x * math.sin(a) + y * math.cos(a)
        out = max(out, math.hypot(X, root + Y))
    return out


PAWL_GAP = 0.05                              # the fixed pawl just under the ratchet's bottom teeth
PAWL_T = 0.5
PAWL_X = (18.4, 20.8)                        # wide enough to stay under the bottom tooth through the shake
PAWL_Z = (12.7, 13.3)
PAWL_POST_X = (18.9, 20.3)                   # its post, from the ground
PAWL_POST_Z = (12.4, 13.6)
# the drawn gearing, as ratios of the axle's travel psi (radians a radian)
WORM_RATIO = -1.0 / RATCHET_N                # the ratchet (and the worm) a tooth a turn, one way: -z
HEAD_RATIO = WORM_RATIO / WHEEL_N            # the wheel a tooth a worm turn: the head's top runs north, to the head
FOOT_RATIO = HEAD_RATIO                      # the same radius, the same belt

# ---------------------------------------------------------------- the boxes and the chutes
VANILLA_CHUTE = {                            # game 1.22.7, shapes/block/metal/chute/straight.json (the "we" shape):
    "walls": ((4.0, 12.0, 4.0, 5.0),         # its walls across the run, (a0, a1, b0, b1) cell-local over the two
              (4.0, 12.0, 11.0, 12.0),       # axes across it (y, z): two full-width walls, two between them; 1
              (4.0, 5.0, 5.0, 11.0),         # thick, an 8 x 8 tube centred in the block (its block's 4..12), a 6 x 6
              (11.0, 12.0, 5.0, 11.0)),      # bore, the block's length along x
    "texture": "game:block/metal/sheet/copper1",
    "shape": "shapes/block/metal/chute/straight.json",
}
CONC_CHUTE_Z = (0.0, 1.8)                    # the concentrate chute section, in from the front face
TAIL_CHUTE_Z = (44.0, 48.0)                  # the tailings chute section, in from the back face
FEED_CHUTE_Y0 = 14.2                         # the infeed section, from the top face down into the feed box
BOX_X = (3.2, 16.8)                          # the concentrate and tailings boxes, outside
BOX_T = 0.6
BOX_FLOOR = (4.6, 5.2)                       # their floors: the top just over the chutes' bore (y 5)
CB_Z = (1.8, 8.8)                            # the concentrate box, under the head roller
CB_TOP = 6.5                                 # its walls: under the belt's wrap and its lower run
TB_Z = (39.8, 44.0)                          # the tailings box, behind the foot roller
TB_NORTH = (39.8, 40.3, 6.3)                 # its north wall (z0, z1, top), low, under the foot roller's wrap
TB_TOP = 6.8
SPOUT_X = (5.6, 10.4)                        # each box's spout, through its wall into its chute's bore
SPOUT_Y = (5.0, 5.4)                         # resting on the chute's bottom wall
CHEEK_T, CHEEK_TOP = 0.4, 6.2
BEARER_X = ((3.6, 5.0), (15.0, 16.4))        # the oak bearers under each box

# ---------------------------------------------------------------- the feed box, its posts, the spray's post
FB_X = (3.4, 16.6)                           # outside: its west and south walls' inner faces on the chute's outer ones
FB_Z = (19.4, 28.6)
FB_FLOOR = (12.2, 12.7)
FB_TOP = 15.8
FB_WEIR = 13.6                               # the north wall is a weir; the pulp spills over it
FB_T = 0.6
APRON_X = (6.4, 13.6)                        # the apron down to the belt, inside its edges as they shake
APRON_Z = 17.4                               # its lip
APRON_LIP_Y = 12.0                           # its top at the lip
APRON_T = 0.4
POSTS_X = ((0.3, 1.8), (18.3, 19.8))         # the feed box's posts, west and east of the shaking frame
POSTS_Z = ((19.6, 21.0), (27.0, 28.4))
BRACKET_Y = (11.6, FB_FLOOR[0])
BRACKETS_X = ((1.8, 3.95), (16.05, 18.3))   # clear of the belt's edges as they shake
SPRAY_POST_Z = (13.8, 15.2)

# ---------------------------------------------------------------- the water line
PIPE_HALF = solderedpipe.HALF                # 3: ppex's pipe is 6 across
INLET = (24.0, 13.0, 8.0)                    # the inlet: a bend, its top open on the water face
ELBOW = (24.0, 13.0, 14.5)                   # the second bend, turning west
RUN_X0 = 19.0                                # the ppex pipe's end, plugged round the spray pipe
SPRAY = (13.4, 14.5)                         # (y, z): the spray pipe across the belt (a reducer off the ppex pipe)
SPRAY_HALF, SPRAY_WALL = 1.2, 0.4            # a smaller soldered tube, 2.4 across round a 1.6 bore
SPRAY_X0 = 3.0                               # its capped west end, on its post's bracket
NOZZLES_X = (6.4, 8.2, 10.0, 11.8, 13.6)     # its nozzles over the belt, inside the edges as they shake
NOZZLE = (0.8, 0.5)                          # each: across, how far under the spray pipe

SECONDS_PER_TURN = 0.5                       # the viewer's Play: two strokes a second


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


def radial(axis, c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box from radius r0 to r1 about `axis` through c, a0..a1 along the axis, `width` across, at angle
    `ang` (radians, right-handed about +axis from the axis's reference direction)."""
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


def gear(axis, c, a0, a1, pitch_r, n, module, name, part, tex, phase=0.0, body_k=4):
    """A toothed wheel: a polygon body to the root and n teeth of `module` (phase: angle of tooth 1)."""
    width = math.pi * module / 2
    root, tip = pitch_r - 1.25 * module, pitch_r + module
    body = disc(axis, c, a0, a1, root, f"{name}_body", part, tex, k=body_k, phase=phase)
    return body + [radial(axis, c, a0 + 0.03, a1 - 0.03, root - 0.25, tip, width, phase + TAU * i / n, f"{name}_tooth{i + 1}",
                          part, tex) for i in range(n)]


def strut(a, b, w, d, name, part, tex, axis="x"):
    """A bar from point a to point b in the plane normal to `axis`, `w` wide in that plane, `d` along it."""
    ia = AX[axis]
    u, v = [(1, 2), (2, 0), (0, 1)][ia]
    du, dv = b[u] - a[u], b[v] - a[v]
    length = math.hypot(du, dv)
    mid = [(a[i] + b[i]) / 2 for i in range(3)]
    lo, hi = list(mid), list(mid)
    lo[u], hi[u] = mid[u] - length / 2, mid[u] + length / 2
    lo[v], hi[v] = mid[v] - w / 2, mid[v] + w / 2
    lo[ia], hi[ia] = mid[ia] - d / 2, mid[ia] + d / 2
    el = box(lo, hi, name, part, tex)
    rotate([el], axis, math.degrees(math.atan2(dv, du)), mid)
    return el


def belt(axis, c1, r1, c2, r2, a0, a1, name, part, tex, t, n_wrap=8):
    """An endless belt on two rollers of radii r1, r2 about `axis` through c1, c2 (3-vectors; the axis
    coordinate is ignored), a0..a1 along the axis: two straight runs on the outer tangents and a wrap of
    short boxes round the far side of each roller. Drawn still: a plain belt moving along its length looks
    the same."""
    ia = AX[axis]
    u, v = [(1, 2), (2, 0), (0, 1)][ia]
    p1, p2 = (c1[u], c1[v]), (c2[u], c2[v])
    d = math.hypot(p2[0] - p1[0], p2[1] - p1[1])
    base = math.atan2(p2[1] - p1[1], p2[0] - p1[0])
    beta = math.asin((r1 - r2) / d)
    out, normals = [], {}
    for side, sgn in (("a", 1), ("b", -1)):
        n = base + sgn * (math.pi / 2 - beta)
        normals[side] = n
        q1, q2 = [0.0] * 3, [0.0] * 3
        q1[u], q1[v] = p1[0] + (r1 + t / 2) * math.cos(n), p1[1] + (r1 + t / 2) * math.sin(n)
        q2[u], q2[v] = p2[0] + (r2 + t / 2) * math.cos(n), p2[1] + (r2 + t / 2) * math.sin(n)
        q1[ia] = q2[ia] = (a0 + a1) / 2
        out.append(strut(q1, q2, t, a1 - a0, f"{name}_run{side}", part, tex, axis=axis))
    for tag, (cu, cv), r, f, to in (("1", p1, r1, normals["a"], normals["b"] + TAU), ("2", p2, r2, normals["b"], normals["a"])):
        while to < f:
            to += TAU
        span = to - f
        n = max(3, int(round(n_wrap * span / math.pi)))
        for i in range(n):
            ang = f + span * (i + 0.5) / n
            seg = span / n * (r + t / 2) * 1.04
            q1, q2 = [0.0] * 3, [0.0] * 3
            for q, s in ((q1, -1), (q2, 1)):
                tang = ang + math.pi / 2
                q[u] = cu + (r + t / 2) * math.cos(ang) + s * seg / 2 * math.cos(tang)
                q[v] = cv + (r + t / 2) * math.sin(ang) + s * seg / 2 * math.sin(tang)
                q[ia] = (a0 + a1) / 2
            out.append(strut(q1, q2, t, a1 - a0 - (0.02 if i % 2 else 0.0), f"{name}_wrap{tag}_{i + 1}", part, tex, axis=axis))
    return out


# ---------------------------------------------------------------- the belt's geometry
SLOPE = math.atan2(HEAD[0] - FOOT[0], FOOT[1] - HEAD[1])   # the line of centres' fall to the foot (radians)


def run_top(z):
    """The belt's top run's upper face at z: the outer tangent of the two (equal) rollers, R + T over the
    line of centres, square to it."""
    centre = HEAD[0] + (z - HEAD[1]) * (FOOT[0] - HEAD[0]) / (FOOT[1] - HEAD[1])
    return centre + (ROLL_R + BELT_T) / math.cos(SLOPE)


def run_bottom(z):
    """The belt's bottom run's lower face at z."""
    centre = HEAD[0] + (z - HEAD[1]) * (FOOT[0] - HEAD[0]) / (FOOT[1] - HEAD[1])
    return centre - (ROLL_R + BELT_T) / math.cos(SLOPE)


def wrap_bottom(c, z):
    """The belt's outer face under a roller centred c = (y, z) at z (within its radius)."""
    r = ROLL_R + BELT_T
    return c[0] - math.sqrt(max(0.0, r * r - (z - c[1]) ** 2))


def pin_and_cheeks(name, part, c, cheek_y):
    """A pin along z through c (x, y, z) with an iron cheek either side of what turns on it (an upright's
    end, UP_D along z): `name`_pin and `name`_cheek1/2, the cheeks from cheek_y[0] to cheek_y[1]."""
    x, y, z = c
    h = UP_D / 2
    out = [box([x - CHEEK_X, cheek_y[0], z - h - CHEEK_GAP - CHEEK_W], [x + CHEEK_X, cheek_y[1], z - h - CHEEK_GAP], f"{name}_cheek1", part, "iron"),
           box([x - CHEEK_X, cheek_y[0], z + h + CHEEK_GAP], [x + CHEEK_X, cheek_y[1], z + h + CHEEK_GAP + CHEEK_W], f"{name}_cheek2", part, "iron")]
    return out + disc("z", (x, y, 0.0), z - h - CHEEK_GAP - CHEEK_W + 0.05, z + h + CHEEK_GAP + CHEEK_W - 0.05, PIN_R, f"{name}_pin", part,
                      "steel")


def ratchet(c, a0, a1, pitch_r, n, module, name, part, tex, lean=-20.0, body_k=6):
    """A ratchet wheel about z through c: a polygon body to the root and n saw teeth, each a radial bar
    leaned `lean` degrees about its own root, so one face of it is steep and the other slopes, as a
    ratchet's teeth are."""
    root, tip = pitch_r - 1.25 * module, pitch_r + module
    width = math.pi * module / 2
    out = disc("z", c, a0, a1, root, f"{name}_body", part, tex, k=body_k)
    for i in range(n):
        el = radial("z", c, a0 + 0.03, a1 - 0.03, root - 0.25, tip, width, 0.0, f"{name}_tooth{i + 1}", part, tex)
        rotate([el], "z", lean, (c[0], c[1] + root, 0.0))
        rotate([el], "z", 360.0 * i / n, (c[0], c[1], 0.0))
        out.append(el)
    return out


# ---------------------------------------------------------------- builders: the frame
def build_frame():
    """The fixed frame: oak sills under each rail's uprights and under the countershaft's pedestals; the
    uprights' foot pins in iron cheeks on the sills; the countershaft's pedestals and bearing blocks; the
    feed box's four posts and brackets; the spray pipe's post and
    bracket; the saddle that carries the water line's bend on the front bearing's cap."""
    f = "frame"
    out = []
    zs = (UPRIGHTS_Z[0] - 1.5, UPRIGHTS_Z[-1] + 1.5)
    for side, (rx0, rx1) in zip("we", RAILS_X):
        cx = (rx0 + rx1) / 2
        out += beam([rx0 - 0.05, 0.0, zs[0]], [rx1 + 0.05, SILL_Y, zs[1]], f"fr_sill_{side}", f, "oak", seg=24.0)
        for i, z in enumerate(UPRIGHTS_Z, 1):
            out += pin_and_cheeks(f"fr_up{side}{i}", f, (cx, UP_PIVOTS_Y[0], z), FOOT_CHEEK_Y)
    out += beam([CS_SILL_X[0], 0.0, CS_SILL_Z[0]], [CS_SILL_X[1], SILL_Y, CS_SILL_Z[1]], "fr_sill_cs", f, "oak", seg=24.0)
    x, y = CS
    for i, (z0, z1) in enumerate(CS_BEARINGS_Z, 1):
        out.append(box([PEDESTAL_X[0], SILL_Y, z0], [PEDESTAL_X[1], y - CS_BRG, z1], f"fr_pedestal{i}", f, "oak"))
        out.append(box([x - CS_BRG, y - CS_BRG, z0], [x + CS_BRG, y + CS_BRG, z1], f"fr_bearing{i}", f, "iron"))
    # the saddle on the front bearing's cap, under the water line's second bend
    z0, z1 = CS_BEARINGS_Z[0]
    out.append(box([x - 0.6, y + CS_BRG, z0], [x + 0.6, ELBOW[1] - PIPE_HALF, z1], "fr_saddle", f, "iron"))
    # the feed box's posts, each with a bracket under the box's floor
    for side, (px0, px1), (bx0, bx1) in zip("we", POSTS_X, BRACKETS_X):
        for i, (pz0, pz1) in enumerate(POSTS_Z, 1):
            out.append(box([px0, 0.0, pz0], [px1, BRACKET_Y[1], pz1], f"fr_post_{side}{i}", f, "oak"))
            out.append(box([bx0, BRACKET_Y[0], pz0], [bx1, BRACKET_Y[1], pz1], f"fr_bracket_{side}{i}", f, "iron"))
    # the spray pipe's post, west, and its bracket under the pipe's capped end
    sy = SPRAY[0] - SPRAY_HALF
    out.append(box([POSTS_X[0][0], 0.0, SPRAY_POST_Z[0]], [POSTS_X[0][1], sy, SPRAY_POST_Z[1]], "fr_spraypost", f, "oak"))
    out.append(box([POSTS_X[0][1], sy - 0.6, SPRAY_POST_Z[0]], [BRACKETS_X[0][1], sy, SPRAY_POST_Z[1]], "fr_spraybracket", f, "iron"))
    return out


def chute_section(name, part, face, cell, depth):
    """Vanilla's straight chute section in the cell's `face`, `depth` long into the cell from the face: its
    walls, cross-section, place in the block and texture exactly (VANILLA_CHUTE), so a chute placed in the
    next block meets it with no gap or step. The game's shape runs along x with its walls over (y, z);
    turned to run along the face's axis, its walls lie over the other two axes the same way (the section is
    square, so which way it was turned does not show)."""
    axis = {"north": 2, "south": 2, "east": 0, "west": 0, "up": 1, "down": 1}[face]
    across = [k for k in range(3) if k != axis]          # (y, z) for x; (x, z) for y; (x, y) for z
    corner = [cell[k] * B for k in range(3)]
    if face in ("north", "west", "down"):
        a0, a1 = corner[axis], corner[axis] + depth
    else:
        a0, a1 = corner[axis] + B - depth, corner[axis] + B
    out = []
    for wname, (p0, p1, q0, q1) in zip(("a", "b", "c", "d"), VANILLA_CHUTE["walls"]):
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[axis], hi[axis] = a0, a1
        lo[across[0]], hi[across[0]] = corner[across[0]] + p0, corner[across[0]] + p1
        lo[across[1]], hi[across[1]] = corner[across[1]] + q0, corner[across[1]] + q1
        out.append(box(lo, hi, f"{name}_{wname}", part, "chute"))
    return out


def chute_profile(els, face, cell):
    """The chute section's walls where it meets its face, cell-local over the two axes across it (sorted),
    and the coordinate of its outer end."""
    axis = {"north": 2, "south": 2, "east": 0, "west": 0, "up": 1, "down": 1}[face]
    across = [k for k in range(3) if k != axis]
    corner = [cell[k] * B for k in range(3)]
    walls, ends = [], set()
    for el in els:
        lo, hi = el.aabb()
        walls.append(tuple(round(v, 4) for v in (lo[across[0]] - corner[across[0]], hi[across[0]] - corner[across[0]],
                                                  lo[across[1]] - corner[across[1]], hi[across[1]] - corner[across[1]])))
        ends.add(round(lo[axis] if face in ("north", "west", "down") else hi[axis], 4))
    return sorted(walls), ends


def read_vanilla_chute():
    """The game's own straight chute, if the game is here ($VINTAGE_STORY or ~/Games/vintagestory): its
    walls' sections over (y, z), the runs along x, and its texture, or None. The generator holds
    VANILLA_CHUTE to it."""
    for base in (os.environ.get("VINTAGE_STORY"), str(Path.home() / "Games" / "vintagestory")):
        if not base:
            continue
        f = Path(base) / "assets" / "survival" / VANILLA_CHUTE["shape"]
        if f.exists():
            shape = json.loads(f.read_text())
            walls = sorted((e["from"][1], e["to"][1], e["from"][2], e["to"][2]) for e in shape["elements"])
            runs = {(e["from"][0], round(e["to"][0])) for e in shape["elements"]}
            tex = "game:" + next(iter(shape["textures"].values()))
            return walls, runs, tex
    return None


def build_boxes():
    """The concentrate box under the head roller and the tailings box behind the foot roller: plank
    floors on oak bearers and low walls, each with a spout through one wall into its chute section,
    which leaves through the front face (concentrate) or the back face (tailings)."""
    f = "frame"
    out = []
    x0, x1 = BOX_X
    t = BOX_T
    fl0, fl1 = BOX_FLOOR
    sx0, sx1 = SPOUT_X
    for tag, (z0, z1), north_top, south_top, spout_wall in (
            ("cb", CB_Z, CB_TOP, CB_TOP, "n"),
            ("tb", TB_Z, TB_NORTH[2], TB_TOP, "s")):
        out.append(box([x0, fl0, z0], [x1, fl1, z1], f"fr_{tag}_floor", f, "planks"))
        nz1 = TB_NORTH[1] if tag == "tb" else z0 + t
        side_top = max(north_top, south_top)
        out.append(box([x0, fl1, z0], [x0 + t, side_top, z1], f"fr_{tag}_w", f, "planks"))
        out.append(box([x1 - t, fl1, z0], [x1, side_top, z1], f"fr_{tag}_e", f, "planks"))
        for wall, (wz0, wz1), top in (("n", (z0, nz1), north_top), ("s", (z1 - t, z1), south_top)):
            if wall == spout_wall:
                out.append(box([x0 + t, fl1, wz0], [sx0, top, wz1], f"fr_{tag}_{wall}1", f, "planks"))
                out.append(box([sx1, fl1, wz0], [x1 - t, top, wz1], f"fr_{tag}_{wall}2", f, "planks"))
            else:
                out.append(box([x0 + t, fl1, wz0], [x1 - t, top, wz1], f"fr_{tag}_{wall}", f, "planks"))
        # the spout: a floor resting on the chute's bottom wall, from inside the box through the wall's notch
        # into the chute's bore, and its two cheeks
        if spout_wall == "n":
            a, b = CONC_CHUTE_Z[1] - 1.4, z0 + t
        else:
            a, b = z1 - t, TAIL_CHUTE_Z[0] + 1.4
        out.append(box([sx0, SPOUT_Y[0], a], [sx1, SPOUT_Y[1], b], f"fr_{tag}_spout", f, "planks"))
        out.append(box([sx0, SPOUT_Y[1], a], [sx0 + CHEEK_T, CHEEK_TOP, b], f"fr_{tag}_cheek1", f, "planks"))
        out.append(box([sx1 - CHEEK_T, SPOUT_Y[1], a], [sx1, CHEEK_TOP, b], f"fr_{tag}_cheek2", f, "planks"))
        for i, (bx0, bx1) in enumerate(BEARER_X, 1):
            out.append(box([bx0, 0.0, z0 + 0.4], [bx1, fl0, z1 - 0.4], f"fr_{tag}_bearer{i}", f, "oak"))
    out += chute_section("fr_chute_conc", f, CONC_FACE, CONC_CELL, CONC_CHUTE_Z[1] - CONC_CHUTE_Z[0])
    out += chute_section("fr_chute_tail", f, TAIL_FACE, TAIL_CELL, TAIL_CHUTE_Z[1] - TAIL_CHUTE_Z[0])
    return out


# ---------------------------------------------------------------- builders: the countershaft
CS_C = (CS[0], CS[1], 0.0)


def sheave_centre():
    """An eccentric sheave's centre at rest, (x, y): E under the shaft, so it swings the frame east as the
    axle turns forward (x = CS_x + E sin theta)."""
    return (CS[0], CS[1] - E)


def build_countershaft():
    """The countershaft (`countershaft`): the vanilla axle's oak cross from the power face, the steel shaft
    through its three bearings, a collar each side of the middle one, and the two eccentric sheaves."""
    p = "countershaft"
    x, y = CS
    z1 = CS_Z[1]
    out = [box([x - 2.0, y - 1.0, ENTRY_Z0], [x + 2.0, y + 1.0, z1], "cs_entrya", p, "oak"),
           box([x - 1.0, y - 2.0, ENTRY_Z0 + 0.01], [x + 1.0, y + 2.0, z1 - 0.01], "cs_entryb", p, "oak")]
    out += disc("z", CS_C, CS_Z[0], ENTRY_Z0 + 0.2, CS_R, "cs_rod", p, "steel")
    mz0, mz1 = CS_BEARINGS_Z[1]
    out += disc("z", CS_C, mz0 - 0.6, mz0, 1.15, "cs_collar1", p, "steel")
    out += disc("z", CS_C, mz1, mz1 + 0.6, 1.15, "cs_collar2", p, "steel")
    sx, sy = sheave_centre()
    for i, z in enumerate(SHEAVES_Z, 1):
        out += disc("z", (sx, sy, 0.0), z - SHEAVE_W / 2, z + SHEAVE_W / 2, SHEAVE_R, f"cs_sheave{i}", p, "steel", k=6)
    return out


def yoke(z, name, part):
    """The iron yoke a sheave of throw E runs in, about the countershaft, at rest: an upright slot, so the
    yoke (and the frame it is fixed to) follows the sheave's centre across (x) and lets it swing up and
    down. Its west and east bars on the sheave's flats' circle (a hundredth clear of its corners), its top
    and bottom bars clear of its whole swing."""
    x, y = CS
    t = YOKE_T
    z0, z1 = z - SHEAVE_W / 2 - 0.15, z + SHEAVE_W / 2 + 0.15
    rc = SHEAVE_RC + 0.01
    far = E + rc + YOKE_GAP
    return [box([x - rc - t, y - far - t, z0], [x - rc, y + far + t, z1], f"{name}_w", part, "iron"),
            box([x + rc, y - far - t, z0], [x + rc + t, y + far + t, z1], f"{name}_e", part, "iron"),
            box([x - rc, y + far, z0], [x + rc, y + far + t, z1], f"{name}_top", part, "iron"),
            box([x - rc, y - far - t, z0], [x + rc, y - far, z1], f"{name}_bottom", part, "iron")]


# ---------------------------------------------------------------- builders: the shaking frame
def build_shaker():
    """The shaking frame (`shaker`): two oak rails, three ties (the ends beyond the belt, the middle inside
    its loop), the rollers' iron bearing blocks on the rails, a yoke round each sheave with its arm to the
    east rail, and the worm shaft's two bearings on hangers from the east rail."""
    p = "shaker"
    out = []
    for side, (x0, x1) in zip("we", RAILS_X):
        out += beam([x0, RAIL_Y[0], RAIL_Z[0]], [x1, RAIL_Y[1], RAIL_Z[1]], f"sk_rail_{side}", p, "oak")
    for i, (z0, z1) in enumerate(TIES_Z, 1):
        out.append(box([RAILS_X[0][1], TIE_Y[0], z0], [RAILS_X[1][0], TIE_Y[1], z1], f"sk_tie{i}", p, "oak"))
    for tag, (ry, rz) in (("head", HEAD), ("foot", FOOT)):
        for side, (bx0, bx1) in zip("we", BRG_X):
            out.append(box([bx0, ry - BRG, rz - BRG], [bx1, ry + BRG, rz + BRG], f"sk_{tag}bearing_{side}", p, "iron"))
    rc = SHEAVE_RC + 0.01
    for i, z in enumerate(SHEAVES_Z, 1):
        out += yoke(z, f"sk_yoke{i}", p)
        out.append(box([RAILS_X[1][1], ARM_Y[0], z - 0.6], [CS[0] - rc - YOKE_T, ARM_Y[1], z + 0.6], f"sk_arm{i}", p, "iron"))
    for side, (x0, x1) in zip("we", RAILS_X):
        cx = (x0 + x1) / 2
        for i, z in enumerate(UPRIGHTS_Z, 1):
            out += pin_and_cheeks(f"sk_up{side}{i}", p, (cx, UP_PIVOTS_Y[1], z), HEAD_CHEEK_Y)
    wx, wy = WORM
    for i, (z0, z1) in enumerate(WORM_BEARINGS_Z, 1):
        out.append(box([wx - WORM_BRG, wy - WORM_BRG, z0], [wx + WORM_BRG, wy + WORM_BRG, z1], f"sk_wbearing{i}", p, "iron"))
        out.append(box([RAILS_X[1][1], wy - 0.7, z0], [wx - WORM_BRG, RAIL_Y[1] - 0.2, z1], f"sk_whanger{i}", p, "iron"))
    return out


def build_uprights():
    """The six uprights (`uprightw`, `uprighte`): oak posts, each pinned at its foot to the frame and at its
    head to its rail's clevis, so they lean as the frame is shaken across, carrying it and holding it along
    its length."""
    out = []
    for side, (x0, x1) in zip("we", RAILS_X):
        cx = (x0 + x1) / 2
        for i, z in enumerate(UPRIGHTS_Z, 1):
            out.append(box([cx - UP_W / 2, UP_Y[0], z - UP_D / 2], [cx + UP_W / 2, UP_Y[1], z + UP_D / 2], f"up{side}_{i}",
                           f"upright{side}", "oak"))
    return out


def build_belt():
    """The belt (`belt`, riding the shaking frame): round the head and foot rollers, its top run on their
    upper tangent, and its raised edges along the top run, inside its sides."""
    p = "belt"
    out = belt("x", (0.0, HEAD[0], HEAD[1]), ROLL_R, (0.0, FOOT[0], FOOT[1]), ROLL_R, BELT_X[0], BELT_X[1], "belt", p,
               "rubber", BELT_T, n_wrap=7)
    za, zb = HEAD[1] + 0.3, FOOT[1] - 0.3
    for j, (ex0, ex1) in enumerate(((BELT_X[0], BELT_X[0] + EDGE_W), (BELT_X[1] - EDGE_W, BELT_X[1])), 1):
        a = [0.0, run_top(za) + EDGE_H / 2 / math.cos(SLOPE), za]
        b = [0.0, run_top(zb) + EDGE_H / 2 / math.cos(SLOPE), zb]
        el = strut(a, b, EDGE_H, ex1 - ex0 - 0.02, f"belt_edge{j}", p, "rubber", axis="x")
        el.c[0] = (ex0 + ex1) / 2
        out.append(el)
    return out


def build_rollers():
    """The head roller (`head`) with its worm wheel, and the foot roller (`foot`): oak drums on steel
    journals through the bearing blocks, a collar outside each bearing. Both ride the shaking frame."""
    out = []
    for p, (ry, rz) in (("head", HEAD), ("foot", FOOT)):
        c = (0.0, ry, rz)
        out += disc("x", c, ROLL_X[0], ROLL_X[1], ROLL_R, f"{p}_drum", p, "oak", k=6)
        x1 = WHEEL_X[1] + 0.3 if p == "head" else BRG_X[1][1] + 0.6
        out += disc("x", c, BRG_X[0][0] - 0.6, x1, JOURNAL_R, f"{p}_journal", p, "steel")
        out += disc("x", c, BRG_X[0][0] - 0.6, BRG_X[0][0], COLLAR_R, f"{p}_collar1", p, "steel")
        out += disc("x", c, BRG_X[1][1], BRG_X[1][1] + 0.6, COLLAR_R, f"{p}_collar2", p, "steel")
    out += gear("x", (0.0, HEAD[0], HEAD[1]), *WHEEL_X, WHEEL_R, WHEEL_N, WHEEL_MOD, "head_wheel", "head", "steel", body_k=6)
    return out


def build_creep():
    """The worm shaft (`worm`, riding the shaking frame): its rod in the two hung bearings, a collar at its
    north end and one each side of the ratchet; the worm under the head roller's wheel, a core and four
    thread rings a tooth's pitch apart, leaning as a right-hand thread does; the ratchet. The pawl
    (`pawl`): fixed, on a short oak post from the ground, just under the ratchet's bottom teeth: the
    bottom tooth catches on it as the frame moves east and slips over it as the frame moves back."""
    w = "worm"
    wx, wy = WORM
    c = (wx, wy, 0.0)
    hz = HEAD[1]
    out = disc("z", c, *WORM_SHAFT_Z, 0.5, "worm_rod", w, "steel")
    out += disc("z", c, WORM_SHAFT_Z[0], WORM_BEARINGS_Z[0][0], 1.0, "worm_collar1", w, "steel")
    out += disc("z", c, WORM_BEARINGS_Z[1][1], RATCHET_Z[0], 1.0, "worm_collar2", w, "steel")
    out += disc("z", c, RATCHET_Z[1], WORM_SHAFT_Z[1], 1.0, "worm_collar3", w, "steel")
    span = 1.5 * WORM_LEAD + 0.35
    out += disc("z", c, hz - span, hz + span, WORM_R - 1.25 * WHEEL_MOD + 0.15, "worm_core", w, "steel", k=4)
    for i in range(4):
        zt = hz - 1.5 * WORM_LEAD + i * WORM_LEAD
        ring = disc("z", c, zt - 0.35, zt + 0.35, WORM_R + WHEEL_MOD * 0.8, f"worm_thread{i + 1}", w, "steel", k=4)
        rotate(ring, "x", WORM_TILT, (wx, wy, zt))
        out += ring
    out += ratchet(c, *RATCHET_Z, RATCHET_R, RATCHET_N, RATCHET_MOD, "worm_ratchet", w, "steel", lean=RATCHET_LEAN)
    p = "pawl"
    tip = wy - ratchet_reach() - PAWL_GAP
    out.append(box([PAWL_POST_X[0], 0.0, PAWL_POST_Z[0]], [PAWL_POST_X[1], tip - PAWL_T, PAWL_POST_Z[1]], "pawl_post", p, "oak"))
    out.append(box([PAWL_X[0], tip - PAWL_T, PAWL_Z[0]], [PAWL_X[1], tip, PAWL_Z[1]], "pawl_arm", p, "iron"))
    return out


# ---------------------------------------------------------------- builders: the feed box
def build_feedbox():
    """The feed box (`feedbox`) over the belt below the head: a plank box on the four posts' brackets, the
    infeed chute section coming down into it from the feed cell's top face (its west and south walls'
    inner faces on the chute's outer ones), its north wall a weir, and an apron from the weir's top down to
    just over the belt, inside its edges, where the pulp spills onto it."""
    p = "feedbox"
    x0, x1 = FB_X
    z0, z1 = FB_Z
    f0, f1 = FB_FLOOR
    t = FB_T
    out = [box([x0, f0, z0], [x1, f1, z1], "fb_floor", p, "planks"),
           box([x0, f1, z0], [x0 + t, FB_TOP, z1], "fb_w", p, "planks"),
           box([x1 - t, f1, z0], [x1, FB_TOP, z1], "fb_e", p, "planks"),
           box([x0 + t, f1, z1 - t], [x1 - t, FB_TOP, z1], "fb_s", p, "planks"),
           box([x0 + t, f1, z0], [x1 - t, FB_WEIR, z0 + t], "fb_weir", p, "planks")]
    # the apron: a board from the weir's top edge down to its lip, turned about x
    a = [0.0, FB_WEIR - APRON_T / 2, z0 + 0.3]
    b = [0.0, APRON_LIP_Y - APRON_T / 2, APRON_Z]
    el = strut(a, b, APRON_T, APRON_X[1] - APRON_X[0], "fb_apron", p, "planks", axis="x")
    el.c[0] = sum(APRON_X) / 2
    out.append(el)
    cell_top = (FEED_CELL[1] + 1) * B
    out += chute_section("fb_chute", p, FEED_FACE, FEED_CELL, cell_top - FEED_CHUTE_Y0)
    return out


# ---------------------------------------------------------------- builders: the water line (soldered pipe)
def small_tube(name, x0, x1, centre, half, wall, role):
    """A small square tube along x from x0 to x1 about `centre` = (y, z): four walls round its bore, as
    solderedpipe's tube is built, at the spray pipe's size."""
    y, z = centre
    i = half - wall
    B_ = solderedpipe.Box
    return [B_(f"{name}_down", [x0, y - half, z - half], [x1, y - i, z + half], role),
            B_(f"{name}_up", [x0, y + i, z - half], [x1, y + half, z + half], role),
            B_(f"{name}_north", [x0, y - i, z - half], [x1, y + i, z - i], role),
            B_(f"{name}_south", [x0, y - i, z + i], [x1, y + i, z + half], role)]


def pipe_boxes():
    """The water line as UnifiedPipes' soldered pipe (solderedpipe's boxes, build-frame voxels): the inlet,
    a bend whose top is open on the water face (the pipe beyond brings its own joint, as at the rosser's),
    turning south; a short length to a second bend turning west; a length west to its end, plugged round
    the spray pipe (a reducer, its seam soldered); the spray pipe across the belt, smaller, capped with
    solder at its west end, and five nozzles under it over the belt."""
    h = PIPE_HALF
    x, y, z = INLET
    out = solderedpipe.hub("inlet", INLET, ("up", "south"))
    out += solderedpipe.tube("link", 2, z + h, ELBOW[2] - h, (x, y))
    out += solderedpipe.hub("elbow", ELBOW, ("north", "west"))
    ez = ELBOW[2]
    out += solderedpipe.tube("run", 0, RUN_X0, x - h, (ez, y))
    # the plug in the run's west end, round the spray pipe's bore: four plates inside its walls
    i = h - solderedpipe.WALL
    sy, sz = SPRAY
    si = SPRAY_HALF - SPRAY_WALL
    p0, p1 = RUN_X0, RUN_X0 + solderedpipe.WALL
    Bx = solderedpipe.Box
    out += [Bx("plug_down", [p0, y - i, ez - i], [p1, sy - si, ez + i], solderedpipe.BODY),
            Bx("plug_up", [p0, sy + si, ez - i], [p1, y + i, ez + i], solderedpipe.BODY),
            Bx("plug_north", [p0, sy - si, ez - i], [p1, sy + si, sz - si], solderedpipe.BODY),
            Bx("plug_south", [p0, sy - si, sz + si], [p1, sy + si, ez + i], solderedpipe.BODY)]
    # the reducer's joint: a ring of solder round the spray pipe where it leaves the plug
    j = solderedpipe.JOINT_SWELL
    out += small_tube("reducer", RUN_X0 - 0.6, RUN_X0, SPRAY, SPRAY_HALF + j, j, solderedpipe.SOLDER)
    out += small_tube("spray", SPRAY_X0, RUN_X0, SPRAY, SPRAY_HALF, SPRAY_WALL, solderedpipe.BODY)
    out.append(Bx("spray_cap", [SPRAY_X0, sy - si, sz - si], [SPRAY_X0 + SPRAY_WALL, sy + si, sz + si], solderedpipe.SOLDER))
    nw, nh = NOZZLE
    bottom = sy - SPRAY_HALF
    for k, nx in enumerate(NOZZLES_X, 1):
        out.append(Bx(f"nozzle{k}", [nx - nw / 2, bottom - nh, sz - nw / 2], [nx + nw / 2, bottom, sz + nw / 2], solderedpipe.BODY))
    return solderedpipe.cull_pressed(out)


def build_pipes(metal_=PIPE_METALS[0]):
    """The water line as elements of the part `pipe<metal_>` (`pipe_boxes`): the body in that metal's pipe
    texture, the joints in solder, each face's UVs solderedpipe's on this model's texture scale. Built in
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
    """The water line in the other metals: the first metal's elements as they are (after the z-fighting
    fix, so every copy has the same insets), renamed and in their own metal's texture; the joints stay
    solder. Only one metal's are ever drawn, so the copies are not checked against each other."""
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
    return (build_countershaft() + build_shaker() + build_uprights() + build_belt() + build_rollers() + build_creep() + build_feedbox()
            + build_pipes() + build_frame() + build_boxes())


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def rot(axis, c, ratio, travel=False):
    """A rotate about `axis` through c (x, y, z voxels) by ratio x theta (or x psi, the travel)."""
    d = {"type": "rotate", "axis": axis, "pivot": pt(*c), "ratio": r6(ratio)}
    if travel:
        d["input"] = "travel"
    return d


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    parts = [
        # the countershaft turns with the axle; the frame follows its eccentrics across, once a turn
        {"id": "countershaft", "match": ["cs_*"], "requires": "countershaft", "drivers": [rot("z", (*CS, 0.0), 1.0)]},
        {"id": "shaker", "match": ["sk_*"], "requires": "shaker",
         "drivers": [{"type": "slide", "axis": "x", "amplitude": r6(E / B), "ratio": 1.0}]},
        # the uprights lean with it, each about its foot pin: its head pin follows the rail's within a few
        # hundredths (the arc's drop, UP_LEN (1 - cos UP_SWING))
        *[{"id": f"upright{side}", "match": [f"up{side}_*"], "requires": "shaker",
           "drivers": [{"type": "swing", "axis": "z", "pivot": pt(sum(RAILS_X[k]) / 2, UP_PIVOTS_Y[0], 0.0),
                        "amplitude": r6(-UP_SWING), "ratio": 1.0}]} for k, side in enumerate("we")],
        {"id": "belt", "match": ["belt_*"], "requires": "belt", "ride": "shaker", "drivers": []},
        # the creep, by the travel: the ratchet a tooth a turn, one way; the worm with it; the rollers a
        # tooth of the wheel a worm turn
        {"id": "head", "match": ["head_*"], "requires": "rollers", "ride": "shaker",
         "drivers": [rot("x", (0.0, *HEAD), HEAD_RATIO, True)]},
        {"id": "foot", "match": ["foot_*"], "requires": "rollers", "ride": "shaker",
         "drivers": [rot("x", (0.0, *FOOT), FOOT_RATIO, True)]},
        {"id": "worm", "match": ["worm_*"], "requires": "creep", "ride": "shaker",
         "drivers": [rot("z", (*WORM, 0.0), WORM_RATIO, True)]},
        {"id": "pawl", "match": ["pawl_*"], "requires": "creep", "drivers": []},
        {"id": "feedbox", "match": ["fb_*"], "requires": "feedbox", "drivers": []},
        # the water line, one part per metal: only the supplied metal's is drawn
        *[{"id": pid, "match": [f"{pid}_*"], "requires": pid, "drivers": []} for pid in PIPES],
        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses and states
# A pose is (theta,) or (theta, travel) or (theta, travel, state): travel defaults to |theta| (turned from
# rest), the state to "all" (everything fitted, the water line in copper).
REST = (0.0,)


def inputs_of(pose):
    th = pose[0]
    ps = pose[1] if len(pose) > 1 and pose[1] is not None else abs(th)
    return {"theta": th, "travel": ps}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path)


# what is fitted in each state checked: the frame, then the parts added in REQUIRES' order, the water line
# last (copper; lead is the same elements, never drawn with it)
STATES = {"frame": ()}
for _i, _r in enumerate(REQUIRES):
    STATES[f"+{_r}"] = REQUIRES[:_i + 1]
STATES["all"] = REQUIRES + (PIPES[0],)


def state_of(pose):
    return pose[2] if len(pose) > 2 else "all"


def on_show(requires, state):
    """Whether a part needing `requires` shows in a state."""
    return requires is None or requires in STATES[state]


def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


def make_rig(parts):
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the front west "
                    "cell, under the head of the belt, nearest the player who placed it; the vanner runs south to the "
                    "power face at the back, the countershaft along its east side. theta is the axle: the countershaft "
                    "turns with it and the shaking frame follows its eccentrics across (a slide in x, alike either way), its "
                    "uprights leaning with it (a swing about each one's foot pin). "
                    "travel is the axle's travel either way: the ratchet the fixed pawl pushes a tooth a stroke turns "
                    "the worm and creeps the belt to the head whichever way the axle turns. The chute and water anchors "
                    "are provisional (#711). See the vanner's README for the schema.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "waterCell": list(WATER_CELL),
        "waterFace": WATER_FACE,
        "feedCell": list(FEED_CELL),
        "feedFace": FEED_FACE,
        "concentrateCell": list(CONC_CELL),
        "concentrateFace": CONC_FACE,
        "tailingsCell": list(TAIL_CELL),
        "tailingsFace": TAIL_FACE,
        "pipes": {"requires": {m: f"pipe{m}" for m in PIPE_METALS},
                  "_comment": "The water line, from the inlet to the spray pipe's nozzles: ppex's pipe, soldered, one part "
                              "per metal (the same elements); only the supplied metal's is drawn."},
        "drive": {"strokesPerTurn": 1.0,
                  "shake": r6(2 * E / B),
                  "beltPerTurn": r6(abs(HEAD_RATIO) * (ROLL_R + BELT_T / 2) * TAU / B),
                  "ratchet": {"teeth": RATCHET_N, "teethPerTurn": 1.0},
                  "worm": {"starts": 1, "wheelTeeth": WHEEL_N},
                  "_comment": "strokesPerTurn: the shaking frame's strokes (one across and back) per turn of the axle; "
                              "shake: the stroke, blocks. beltPerTurn: blocks the belt creeps to the head per turn of the "
                              "axle, either way (the fixed pawl pushes the ratchet a tooth a stroke; a single-start worm on "
                              "the head roller's 12-tooth wheel)."},
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
    for key in ("powerCell", "waterCell", "feedCell", "concentrateCell", "tailingsCell"):
        ship[key] = shift_cell(rig[key], ORIGIN_CELL)
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig: every
    element but the second metal's water line (the same as the first's), so each reader rebuilds them the
    same way; then a lid over every column."""
    written = flatten(shape["elements"], textures={})
    by_cell = {}
    for w in written:
        pid = part_of(ship_parts, w.name)
        if pid in PIPES[1:]:
            continue
        el = posed(w, _part_matrix(ship_parts, pid, inputs_of(REST)))
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
    poses = [REST, (1.3,), (-2.2, 5.0), (4.9,), (9.1, 20.0)]
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
    """(theta, travel): turned from rest either way, and with travel run up both ways first."""
    out = []
    for th in (0.0, 0.7, -1.9, 2.6, 4.1, -5.3, 11.05):
        for extra in (0.0, 3.7, 150.0):
            out.append((th, round(abs(th) + extra, 6)))
    return out


def reference_json(ship_parts):
    poses = []
    for th, ps in reference_poses():
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], {"theta": th, "travel": ps})) for q in ship_parts}
        poses.append({"theta": th, "travel": ps, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT}; do not edit. Every vanner part's matrix (blocks, first three rows) at "
                        "each pose (theta the axle's angle, travel its total travel either way, radians), from the "
                        "generator's reference maths (Machines/tools/machinegen/rigmath.py). The site's and the mod's "
                        "tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. The frame (fr_*, the chute "
             "sections fr_chute_*), the countershaft (cs_), the shaking frame (sk_), the belt (belt_), the rollers "
             "(head_, foot_), the worm shaft (worm_) and the pawl (pawl_), the feed box with the infeed chute section "
             "(fb_), and the water line in each metal (pipecopper_ / pipelead_). Keep element names when editing: the "
             "rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def shown(posed_els, pose, req_of):
    """The posed elements as they can be seen in the pose's state: the parts not fitted moved far away
    (copies, the order kept), so the z-fighting fix and its check deal only with faces that show together."""
    state = state_of(pose)
    out = []
    for e in posed_els:
        if not on_show(req_of[e.part], state):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def coplanar_poses():
    return ((0.0,), (0.9,), (2.3, 40.0), (math.pi / 2,), (4.0, 7.0), (-math.pi / 2,))


def fix_coplanar(els, parts):
    req_of = {p["id"]: p["requires"] for p in parts}
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose, req_of),
                              coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the vanner's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_vanner
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
    ok = validate_vanner.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "vanner.json", args.out / "vanner_frame.json", args.out / "vanner-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "vanner.json", SHAPE_DIR / "vanner_frame.json", RIG_DIR / "vanner-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_vanner.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
