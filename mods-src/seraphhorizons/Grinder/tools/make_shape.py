#!/usr/bin/env python3
"""Generate the grinder's shapes, rig and reference poses.

The grinder is the ore line's grind stage (#732, model #733), one machine upgraded in place: a frame, then
each tier's parts fitted one item each in order, the next tier's set replacing the previous tier's working
parts. Its footprint is fixed at its tier 4 size from the frame on, and the feed, the discharge and the drive
stay at the same cells at every tier (at tier 4 the concentrator's middlings come back by the feed too):

    frame    a timber deck over the whole footprint; the power shaft (the vanilla axle's cross from the south
             face, then an iron shaft in two pillow blocks) running north under the middle to the centre; the
             feed hopper over the west end, its mouth the top of the infeed cell and its outlet in its east
             wall; the discharge box at the east end, its outlet lip through the east face
    tier 2   the arrastra: a stone-paved basin with a curb on piers over the drive, a post in its middle
             stepped on the deck, turned by a bevel pinion on the power shaft through a bevel wheel on its
             foot; four sweep arms on the post, each dragging a granite drag stone on a bridle of two chains
    tier 3   the Chilean mill: an iron pan with a steel die ring on legs over the drive, a vertical shaft
             turned the same way (a faster bevel pair), a cross-head carrying a horizontal axle with two
             granite edge runners rolling on the die, and two ploughs turning the pulp back under them;
             the pulp leaves through a screen in the pan's east side
    tier 4   the ball mill: a riveted iron drum on two hollow trunnions in pillow blocks on cast portal
             pedestals, a girth gear on its east head turned by a pinion on a countershaft under it, which
             a mitre pair on the power shaft's end turns; fed through the west trunnion from a feed box
             under the hopper's outlet (fresh ore and the concentrator's middlings alike come in by the
             hopper), discharging over the east trunnion's lip into the discharge box, a charge of iron
             balls inside

Everything is built here from plain boxes; no other mod's model is used. No animal, wheel or engine:
power comes from a vanilla axle on the power face, and every tier works whichever way it turns.

It writes, deterministically,

    grinder.json         the whole machine, every tier's parts     (assets/.../shapes/block/)
    grinder_frame.json   the static frame only (block and item)     (assets/.../shapes/block/)
    grinder-rig.json     cells, anchors, the tiers and the part rig (assets/.../config/)
    rig-reference.json   every part's matrix at a grid of poses     (tests/Grinder/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_grinder.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
machine box's north-west-bottom corner (the "build frame"); `shipped()` moves it so the controller cell
(ORIGIN_CELL, the middle of the north side at ground level) is [0,0,0]. The ore runs west to east along x;
the drive comes in from the south.

The rig reads θ alone: the axle's angle. Every part turns with it at its gearing's ratio, or stands still.
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
REFERENCE_OUT = MOD / "tests" / "Grinder" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/Grinder/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

# ---------------------------------------------------------------- the box, the cells, the anchors
CELLS_X, CELLS_Y, CELLS_Z = 3, 2, 3          # three by three, two high: the ball mill's size, fixed from the frame on
ORIGIN_CELL = (1, 0, 0)                      # the controller: the middle of the north (operator's) side at ground level
CX, CZ = 24.0, 24.0                          # the centre line: the pan's axis (x, z); the drum's axis runs along x at z CZ
POWER_CELL, POWER_FACE = (1, 0, 2), "south"  # the vanilla axle comes in along z at (x 24, y 8): the line shaft's side
INFEED_CELL, INFEED_FACE = (0, 1, 1), "up"   # the hopper's mouth: a chute (or a player) tips ore in from above; at
                                             # tier 4 the concentrator's middlings come back the same way
OUTPUT_CELL, OUTPUT_FACE = (2, 0, 1), "east"  # the discharge box's lip: the ground ore leaves eastwards, downhill

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "planks": "game:block/wood/planks/oak1",
    "axle": "game:block/wood/planks/generic",            # the vanilla axle's own texture, on its cross where it comes in
    "iron": "game:block/metal/plate/iron",
    "steel": "game:block/metal/sheet-plain/steel1",
    "riveted": "game:block/metal/riveted/iron1",
    "chain": "game:block/metal/armor-generic/chain-iron",
    "mesh": "game:block/metal/mesh1",
    "cobble": "game:block/stone/cobblestone/granite1",
    "drystone": "game:block/stone/drystone/granite1",
    "granite": "game:block/stone/rock/granite1",
    "polished": "game:block/stone/polishedrock/granite",
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the frame: deck, power shaft, hopper, discharge box
DECK = 2.0                                   # the deck's top: timber boards over the whole footprint
DECK_BOARD = 8.0                             # a board's width (z)
SHAFT_Y = 8.0                                # the power shaft's axis, along z at x CX: the power cell's centre
SHAFT_R = 1.0                                # its iron shaft's apothem (an octagon)
SHAFT_END = 26.5                             # its north end, short of the centre: the tiers' pinions are keyed on from here
COUPLING_Z = 42.5                            # from here to the south face, the vanilla axle's cross (two 4 x 2 boards)
BEARINGS_Z = ((33.0, 35.5), (39.5, 42.0))    # the frame's two pillow blocks under it
BEARING_H = 2.0                              # a pillow block's half width (x) round the shaft
BEARING_TOP = SHAFT_Y + 2.5
COLLAR_R = 1.6
P = (CX, SHAFT_Y, CZ)                        # where the power shaft's axis meets the vertical shaft's (tiers 2-3) and the
                                             # countershaft's (tier 4): every bevel pair's apex

HOP_X = (0.5, 7.5)                           # the feed hopper over the west end
HOP_Z = (17.5, 30.5)
HOP_Y = (21.4, 32.0)                         # its bottom board, its mouth (the infeed cell's top face)
HOP_WALL = 0.8
HOP_POSTS = ((15.5, 17.5), (30.5, 32.5))     # its two posts (z), either side of it at the west edge
HOP_POST_X = (0.5, 3.0)
OUTLET_Z = (22.5, 25.5)                      # the outlet in its east wall, where every tier's feed spout starts
OUTLET_Y = (22.0, 24.0)
HOP_FLOOR = ((1.3, 26.5), (6.7, 22.0))       # (x, y): the sloping floor's top, down to the outlet's sill

BOX_X = (43.0, 47.5)                         # the discharge box at the east end, open at the top
BOX_Z = (19.0, 29.0)
BOX_TOP = 13.0
BOX_WALL = 0.8
BOX_FLOOR = ((43.8, 8.0), (46.7, 5.0))       # (x, y): its sloping floor, down to the outlet
LIP_Z = (22.0, 26.0)                         # the outlet through its east wall, and the lip out of the east face
LIP_Y = (4.4, 5.0)

# ---------------------------------------------------------------- tiers 2 and 3: the basin and the pan over the drive
PIER_R = 12.0                                # the piers (tier 2) and legs (tier 3) under the basin, eight round the centre
PIER_ANGLES = tuple(22.5 + 45.0 * i for i in range(8))   # degrees about y from +z (south) towards +x: none over the shaft
UNDER = 12.5                                 # the basin's platform and the pan's bottom start here, over the gearing
FLOOR_Y = 15.5                               # the working floor: the tier 2 paving's top, the tier 3 die's top
RIM_TOP = 21.0                               # the curb's and the pan wall's top
RIM_R = (15.0, 16.5)                         # the curb (tier 2); the pan's wall is RIM_R[1] - 1 .. RIM_R[1]
SPOUT_END = 9.8                              # where tiers 2 and 3's feed spouts end over the floor (x): clear of the runners
SPOUT_END_Y = 21.6                           # the floor's top at its end (it starts at the outlet's sill)
DRAIN_Z = (23.0, 25.0)                       # tier 2's drain trough, through the curb at the east, over the discharge box
DRAIN_X = (38.6, 44.5)

T2_BEVEL = {"wheel": 30, "pinion": 10, "m": 0.5, "face": 2.2}   # 3:1, the post turns a third of the axle
T3_BEVEL = {"wheel": 20, "pinion": 10, "m": 0.5, "face": 1.6}   # 2:1, the runners' shaft half the axle

ARM_Y = (22.5, 25.0)                         # tier 2's four sweep arms, through the post
ARM_R = 12.5
ARM_W = 2.4
POST_R = 2.0                                 # the oak post's apothem
POST_TOP = 28.0
GUDGEON_TOP = 11.2                           # the post's iron foot, from the footstep through the bevel wheel's hub
STONE_R = (5.5, 14.0)                        # a drag stone's span from the centre, under its arm
STONE_W = 4.6
STONE_Y = (FLOOR_Y, 18.8, 19.6)              # its block, and the smaller top block
EYE_R = 9.75                                 # the eyes on its back, fore and aft of the arm, and the staple under the arm
EYE_T = 1.6
STAPLE_T = 0.4
STAPLE_Y = 22.0

RUNNER_R = 7.2                               # tier 3's edge runners: radius, track radius (mid-face), width
RUNNER_TRACK = 9.6                           # RUNNER_TRACK / RUNNER_R = 4/3: each turns 2/3 of the axle
RUNNER_W = 4.5
AXLE_Y = FLOOR_Y + RUNNER_R                  # the runners' axle, through the cross-head
DIE_R = (6.0, 12.8)
SCRAPERS = ((0.0, 11.6, 35.0), (180.0, 7.2, -35.0))   # (angle about y, radius, blade's turn in degrees): outer and inner

# ---------------------------------------------------------------- tier 4: the ball mill
GIRTH = {"teeth": 30, "m": 0.64}              # the girth gear on the east head
PINION = {"teeth": 10, "m": 0.64}              # the countershaft's pinion under it
MITRE = {"teeth": 12, "m": 0.5, "face": 1.3}  # the mitre pair from the power shaft to the countershaft (1:1)
GIRTH_R = GIRTH["teeth"] * GIRTH["m"] / 2    # 9.6
PINION_R = PINION["teeth"] * PINION["m"] / 2  # 3.2
DRUM_Y = SHAFT_Y + PINION_R + GIRTH_R        # 20.8: the drum's axis, along x at z CZ, over the countershaft
MITRE_R = MITRE["teeth"] * MITRE["m"] / 2    # 3.0
SHELL_R = (7.9, 8.5)                         # the riveted shell
SHELL_X = (20.0, 33.5)
HEAD_R = 9.0                                 # the heads' flanges
HEAD_T = 1.5
TRUNNION_R = (2.6, 4.0)                      # the hollow trunnions: the bore the feed goes in by and the pulp leaves by
W_TRUNNION_X = (11.5, SHELL_X[0] - HEAD_T)
E_TRUNNION_X = (SHELL_X[1] + HEAD_T, 45.0)
GIRTH_X = (E_TRUNNION_X[0], E_TRUNNION_X[0] + 2.5)
LIP = (44.4, 45.2, 4.8)                      # the discharge lip on the east trunnion's end (x0, x1, radius)
PEDESTALS_X = ((13.0, 16.5), (38.5, 42.0))   # the trunnions' pillow blocks on cast portals
PORTAL_LEGS_Z = ((17.5, 20.5), (27.5, 30.5))
BEDPLATE = ((7.5, 43.0), (DECK, 3.2), (17.0, 31.0))
PORTAL_CAP = (13.5, DRUM_Y - TRUNNION_R[1] - 1.6)   # its cap, carrying the bearing's lower block
HOUSING_T = 1.6                              # a trunnion bearing's walls round the trunnion
COUNTER_X = (25.6, 41.6)                     # the countershaft, along x at (y SHAFT_Y, z CZ)
CBEARINGS_X = ((29.6, 32.0), (38.6, 41.0))
MANHOLE_X = (24.0, 29.5)
FEEDBOX = ((HOP_X[1], W_TRUNNION_X[0] - 0.2), (19.5, 30.0), (19.2, 26.0))   # tier 4's feed box at the trunnion's mouth
FEEDBOX_FLOOR = ((8.1, OUTLET_Y[0] - 0.3), (10.7, 19.9))   # (x, y): its floor's top, from the hopper's outlet to the spout
FEED_SPOUT_END = (W_TRUNNION_X[1] - 0.5, 19.1)            # the spout's floor's top at its end, inside the west trunnion
CHARGE_FACE = (2.2, 0.45)                    # the charge's face: this far under the axis at its middle, rising this much a voxel towards -z


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
    """The point at angle a (degrees about y, from +z towards +x) and radius r from the centre line."""
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


def bevel(axis, sign, apex, R, R_other, n, m, face, phase, name, part, tex, back=0.8):
    """A bevel gear on `axis` (its direction from the apex towards the gear: sign * the axis) whose pitch cone
    has its apex at `apex`, heel pitch radius R, meshing a gear of heel pitch radius R_other on a shaft at right
    angles through the same apex. Its n box teeth run along the cone from the toe to the heel (`face` long),
    tooth 1 at angle `phase`; under them a ring of n root segments along the root cone and a back disc behind
    the heel."""
    d = v_mul(UNIT[axis], sign)
    A, g_ang, s = bevel_geometry(R, R_other, face)
    cg, sg = math.cos(g_ang), math.sin(g_ang)
    out = []
    root_seg = 2 * (R - 1.25 * m * cg) * s * math.tan(math.pi / n) * 1.08
    for i in range(n):
        e = radial_dir(axis, phase + TAU * i / n)
        g = v_add(v_mul(d, cg), v_mul(e, sg))
        nn = v_sub(v_mul(e, cg), v_mul(d, sg))
        t = v_cross(g, nn)
        mid = v_add(apex, v_mul(g, A - face / 2))
        tooth = v_add(mid, v_mul(nn, -0.125 * m * s))
        out.append(oriented(f"{name}_tooth{i + 1}", [face * 0.96, 2.25 * m * s, math.pi * m / 2 * s * 0.88], tooth, [g, nn, t], part, tex))
        e2 = radial_dir(axis, phase + TAU * (i + 0.5) / n)
        g2 = v_add(v_mul(d, cg), v_mul(e2, sg))
        n2 = v_sub(v_mul(e2, cg), v_mul(d, sg))
        seg = v_add(v_add(apex, v_mul(g2, A - face / 2)), v_mul(n2, -(1.25 * m * s + back / 2) + 0.05))
        out.append(oriented(f"{name}_root{i + 1}", [face, back, root_seg], seg, [g2, n2, v_cross(g2, n2)], part, tex))
    # the back disc behind the heel: from the heel's root, `back` thick, to the root radius
    a_heel = R_other + 1.25 * m * sg
    c = list(apex)
    a = AX[axis]
    lo_a = apex[a] + sign * a_heel
    hi_a = lo_a + sign * back
    r_back = R - 1.25 * m * cg
    out += disc(axis, c, min(lo_a, hi_a), max(lo_a, hi_a), r_back, f"{name}_back", part, tex, k=8 if r_back > 4 else 4, phase=phase)
    return out


# ---------------------------------------------------------------- the frame
def build_deck():
    out = []
    n = int(CELLS_Z * B / DECK_BOARD)
    for i in range(n):
        z0, z1 = i * DECK_BOARD, (i + 1) * DECK_BOARD
        out += boards([0.0, 0.0, z0], [CELLS_X * B, DECK, z1], f"fr_deck{i + 1}_", "frame", "planks")
    return out


def pillow(x, z, y, top, name, part, tex="iron", axis="z", foot=DECK):
    """A pillow block round a shaft at height y: a block as far below the shaft as `top` is above it, x and z its
    span, on a pedestal down to `foot`. The shaft runs through it."""
    lo_x, hi_x = x
    lo_z, hi_z = z
    out = [box([lo_x, y - (top - y), lo_z], [hi_x, top, hi_z], f"{name}_block", part, tex)]
    if axis == "z":
        out.append(box([lo_x + 0.4, foot, lo_z + 0.2], [hi_x - 0.4, y - (top - y), hi_z - 0.2], f"{name}_pedestal", part, tex))
    else:
        out.append(box([lo_x + 0.2, foot, lo_z + 0.4], [hi_x - 0.2, y - (top - y), hi_z - 0.4], f"{name}_pedestal", part, tex))
    return out


def build_power():
    """The power shaft (the part `shaft`, the frame's: it turns whatever is fitted): the vanilla axle's cross from
    the south face, then an octagonal iron shaft to its north end; collars on both faces of the inner pillow
    block. The pillow blocks are the frame's."""
    p = "shaft"
    out = [box([CX - 2.0, SHAFT_Y - 1.0, COUPLING_Z], [CX + 2.0, SHAFT_Y + 1.0, CELLS_Z * B], "sh_axle_a", p, "axle"),
           box([CX - 1.0, SHAFT_Y - 2.0, COUPLING_Z], [CX + 1.0, SHAFT_Y + 2.0, CELLS_Z * B - 0.01], "sh_axle_b", p, "axle")]
    out += disc("z", (CX, SHAFT_Y, 0.0), SHAFT_END, COUPLING_Z + 0.4, SHAFT_R, "sh_rod", p, "steel")
    z0, z1 = BEARINGS_Z[0]
    out += disc("z", (CX, SHAFT_Y, 0.0), z0 - 0.6, z0, COLLAR_R, "sh_collar_n", p, "steel")
    out += disc("z", (CX, SHAFT_Y, 0.0), z1, z1 + 0.6, COLLAR_R, "sh_collar_s", p, "steel")
    for i, z in enumerate(BEARINGS_Z, 1):
        out += pillow((CX - BEARING_H, CX + BEARING_H), z, SHAFT_Y, BEARING_TOP, f"fr_bearing{i}", "frame")
    return out


def build_hopper():
    """The feed hopper: a timber bin between two posts at the west edge, a bottom board and a floor sloping east
    down to the outlet in its east wall; its mouth is the infeed cell's top face."""
    f, t = "frame", "planks"
    (x0, x1), (z0, z1), (y0, y1), w = HOP_X, HOP_Z, HOP_Y, HOP_WALL
    out = []
    for i, (pz0, pz1) in enumerate(HOP_POSTS, 1):
        out.append(box([HOP_POST_X[0], DECK, pz0], [HOP_POST_X[1], y1, pz1], f"fr_hopper_post{i}", f, "oak"))
    out += [box([x0, y0, z0], [x1, y1, z0 + w], "fr_hopper_n", f, t),
            box([x0, y0, z1 - w], [x1, y1, z1], "fr_hopper_s", f, t),
            box([x0, y0 + 0.6, z0 + w], [x0 + w, y1, z1 - w], "fr_hopper_w", f, t),
            box([x1 - w, y0 + 0.6, z0 + w], [x1, y1, OUTLET_Z[0]], "fr_hopper_e1", f, t),
            box([x1 - w, y0 + 0.6, OUTLET_Z[1]], [x1, y1, z1 - w], "fr_hopper_e2", f, t),
            box([x1 - w, OUTLET_Y[1], OUTLET_Z[0]], [x1, y1, OUTLET_Z[1]], "fr_hopper_e3", f, t),
            box([x0, y0, z0 + w], [x1, y0 + 0.6, z1 - w], "fr_hopper_bottom", f, t)]
    (fx0, fy0), (fx1, fy1) = HOP_FLOOR
    th = 0.6
    d = v_norm([fx1 - fx0, fy1 - fy0, 0.0])
    up = [-d[1], d[0], 0.0]
    a = v_add([fx0, fy0, 0.0], v_mul(up, -th / 2))
    b = v_add([fx1, fy1, 0.0], v_mul(up, -th / 2))
    a[2] = b[2] = CZ
    out.append(bar(a, b, z1 - z0 - 2 * w, th, "fr_hopper_floor", f, t, up=up, extend=0.2))
    # iron straps round the bin
    for i, y in enumerate((y0 + 1.6, y1 - 1.6), 1):
        out += [box([x0 - 0.0, y - 0.4, z0 - 0.15], [x1, y + 0.4, z0], f"fr_hopper_strap{i}n", f, "iron"),
                box([x0 - 0.0, y - 0.4, z1], [x1, y + 0.4, z1 + 0.15], f"fr_hopper_strap{i}s", f, "iron")]
    return out


def build_box():
    """The discharge box: four timber walls on the deck, a floor sloping east to the outlet in its east wall,
    and the lip out of the east face (the output)."""
    f, t = "frame", "planks"
    (x0, x1), (z0, z1), w = BOX_X, BOX_Z, BOX_WALL
    lz0, lz1 = LIP_Z
    out = [box([x0, DECK, z0], [x1, BOX_TOP, z0 + w], "fr_box_n", f, t),
           box([x0, DECK, z1 - w], [x1, BOX_TOP, z1], "fr_box_s", f, t),
           box([x0, DECK, z0 + w], [x0 + w, BOX_TOP, z1 - w], "fr_box_w", f, t),
           box([x1 - w, DECK, z0 + w], [x1, BOX_TOP, lz0], "fr_box_e1", f, t),
           box([x1 - w, DECK, lz1], [x1, BOX_TOP, z1 - w], "fr_box_e2", f, t),
           box([x1 - w, LIP_Y[1] + 2.5, lz0], [x1, BOX_TOP, lz1], "fr_box_e3", f, t),
           box([x1 - w, DECK, lz0], [x1, LIP_Y[0], lz1], "fr_box_e4", f, t),
           box([x1 - w - 0.2, LIP_Y[0], lz0], [CELLS_X * B, LIP_Y[1], lz1], "fr_box_lip", f, "iron")]
    (fx0, fy0), (fx1, fy1) = BOX_FLOOR
    th = 0.6
    d = v_norm([fx1 - fx0, fy1 - fy0, 0.0])
    up = [-d[1], d[0], 0.0]
    a = v_add([fx0, fy0, CZ], v_mul(up, -th / 2))
    b = v_add([fx1, fy1, CZ], v_mul(up, -th / 2))
    out.append(bar(a, b, z1 - z0 - 2 * w, th, "fr_box_floor", f, t, up=up, extend=0.2))
    out.append(box([x0 + w, DECK, z0 + w], [x0 + w + 0.6, fy0 - 0.4, z1 - w], "fr_box_floorpost", f, t))
    return out


def build_frame():
    return build_deck() + build_power() + build_hopper() + build_box()


# ---------------------------------------------------------------- tiers 2 and 3: shared pieces
def feed_spout(part, tex, end_x, end_y):
    """A trough from the hopper's outlet east and down: its floor and two sides."""
    y0, x0 = OUTLET_Y[0], HOP_X[1]
    th, side, w = 0.4, 1.4, OUTLET_Z[1] - OUTLET_Z[0] - 0.6
    d = v_norm([end_x - x0, end_y - y0, 0.0])
    up = [-d[1], d[0], 0.0]
    out = []
    a = v_add([x0, y0, CZ], v_mul(up, -th / 2))
    b = v_add([end_x, end_y, CZ], v_mul(up, -th / 2))
    out.append(bar(a, b, w, th, f"{part}_spout_floor", part, tex, up=up))
    for tag, zc in (("n", CZ - w / 2 + 0.15), ("s", CZ + w / 2 - 0.15)):
        a = v_add([x0, y0, zc], v_mul(up, side / 2 - th))
        b = v_add([end_x, end_y, zc], v_mul(up, side / 2 - th))
        out.append(bar(a, b, 0.3, side, f"{part}_spout_{tag}", part, tex, up=up))
    return out


def footstep(part):
    """The vertical shaft's footstep on the deck."""
    return [box([CX - 2.0, DECK, CZ - 2.0], [CX + 2.0, DECK + 2.6, CZ + 2.0], f"{part}_block", part, "iron"),
            box([CX - 2.6, DECK, CZ - 2.6], [CX + 2.6, DECK + 0.8, CZ + 2.6], f"{part}_sole", part, "iron")]


def vertical_bevels(spec, pinion_part, wheel_part, wheel_tex="iron"):
    """A bevel pinion on the power shaft's end (rides the shaft) and the bevel wheel above it on the vertical
    shaft's foot, apex P: the pinion's tooth 1 on the line to the wheel's axis, the wheel's gap there."""
    m, nw, npn, face = spec["m"], spec["wheel"], spec["pinion"], spec["face"]
    Rw, Rp = nw * m / 2, npn * m / 2
    ph_p = angle_of("z", [0.0, 1.0, 0.0])                # the pinion's tooth straight up, at the wheel
    ph_w = angle_of("y", [0.0, 0.0, 1.0]) + math.pi / nw  # the wheel's gap straight south, over the pinion
    out = bevel("z", 1.0, P, Rp, Rw, npn, m, face, ph_p, f"{pinion_part}_bevel", pinion_part, "steel")
    A, g, s = bevel_geometry(Rp, Rw, face)
    toe = CZ + (A - face) * math.cos(g)
    out += disc("z", P, SHAFT_END, toe + 0.2, Rp - 1.25 * m - 0.1, f"{pinion_part}_hub", pinion_part, "steel")
    out += bevel("y", 1.0, P, Rw, Rp, nw, m, face, ph_w, f"{wheel_part}_bevel", wheel_part, wheel_tex)
    return out


def bevel_pitch_point(spec):
    m = spec["m"]
    return [CX, SHAFT_Y + spec["pinion"] * m / 2, CZ + spec["wheel"] * m / 2]


def rim_index(n):
    """The ring index facing east (+x) when box i is at 2 pi i / n about y from +z."""
    return n // 4


# ---------------------------------------------------------------- tier 2: the arrastra
def build_t2():
    out = []
    # the basin: piers, a timber platform, two courses of paving, the centre stone, the curb, the drain, the spout
    b = "t2basin"
    for i, a in enumerate(PIER_ANGLES, 1):
        c = polar(a, PIER_R)
        out.append(box([c[0] - 1.5, DECK, c[2] - 1.5], [c[0] + 1.5, UNDER, c[2] + 1.5], f"{b}_pier{i}", b, "drystone"))
    c = (CX, 0.0, CZ)
    out += ring("y", c, UNDER, 14.0, 2.8, 8.5, 10, f"{b}_platin", b, "planks")
    out += ring("y", c, UNDER, 14.0, 8.5, RIM_R[1], 20, f"{b}_platout", b, "planks")
    out += ring("y", c, 14.0, FLOOR_Y, 3.4, 9.2, 12, f"{b}_pavein", b, "cobble")
    out += ring("y", c, 14.0, FLOOR_Y, 9.2, RIM_R[0], 20, f"{b}_paveout", b, "cobble")
    out += disc("y", c, 14.0, 17.2, 3.4, f"{b}_centre", b, "granite")
    n = 20
    east = rim_index(n)
    out += ring("y", c, 14.0, RIM_TOP, RIM_R[0], RIM_R[1], n, f"{b}_curb", b, "drystone", skip=(east,))
    w = 2 * RIM_R[1] * math.tan(math.pi / n)
    ang = TAU * east / n
    out.append(radial("y", c, 14.0, 15.5, RIM_R[0], RIM_R[1], w, ang, f"{b}_curb{east + 1}a", b, "drystone"))
    out.append(radial("y", c, 17.0, RIM_TOP, RIM_R[0], RIM_R[1], w, ang, f"{b}_curb{east + 1}b", b, "drystone"))
    (dx0, dx1), (dz0, dz1) = DRAIN_X, DRAIN_Z
    out.append(bar([dx0, FLOOR_Y - 0.25, CZ], [dx1, FLOOR_Y - 0.85, CZ], dz1 - dz0, 0.5, f"{b}_drain_floor", b, "planks"))
    for tag, zc in (("n", dz0 - 0.2), ("s", dz1 + 0.2)):
        out.append(bar([dx0, FLOOR_Y + 0.3, zc], [dx1, FLOOR_Y - 0.3, zc], 0.4, 1.6, f"{b}_drain_{tag}", b, "planks"))
    out += feed_spout(b, "planks", SPOUT_END, SPOUT_END_Y)
    # the post: its iron foot in the footstep, the bevel wheel keyed on it, the oak post through the centre stone
    p = "t2post"
    out += footstep("t2foot")
    out += disc("y", c, DECK + 0.6, GUDGEON_TOP, 1.0, f"{p}_gudgeon", p, "steel")
    out += vertical_bevels(T2_BEVEL, "t2pinion", p)
    out += disc("y", c, 10.3, 12.4, 2.6, f"{p}_hub", p, "iron")
    out += disc("y", c, GUDGEON_TOP, POST_TOP, POST_R, f"{p}_post", p, "oak")
    out += disc("y", c, POST_TOP - 0.8, POST_TOP + 0.4, POST_R + 0.3, f"{p}_cap", p, "iron")
    out += disc("y", c, ARM_Y[0] - 0.9, ARM_Y[0] - 0.2, POST_R + 0.3, f"{p}_bandlow", p, "iron")
    out += disc("y", c, ARM_Y[1] + 0.2, ARM_Y[1] + 0.9, POST_R + 0.3, f"{p}_bandhigh", p, "iron")
    # the sweep arms and the drag stones on their bridles
    a_part, s_part = "t2arms", "t2stones"
    for i in range(4):
        ang = TAU * i / 4
        out.append(radial("y", c, ARM_Y[0], ARM_Y[1], POST_R - 0.3, ARM_R, ARM_W, ang, f"{a_part}_arm{i + 1}", a_part, "oak"))
        out.append(radial("y", c, STAPLE_Y, ARM_Y[0], EYE_R - 0.4, EYE_R + 0.4, 2 * STAPLE_T + 0.6, ang, f"{a_part}_staple{i + 1}", a_part, "iron"))
        stone = [radial("y", c, STONE_Y[0], STONE_Y[1], STONE_R[0], STONE_R[1], STONE_W, 0.0, f"{s_part}_stone{i + 1}", s_part, "granite"),
                 radial("y", c, STONE_Y[1], STONE_Y[2], STONE_R[0] + 0.6, STONE_R[1] - 0.6, STONE_W - 1.0, 0.0, f"{s_part}_stone{i + 1}top", s_part, "granite")]
        for side, sgn in (("f", 1.0), ("b", -1.0)):
            ex = CX + sgn * EYE_T
            ez = CZ + EYE_R
            stone.append(box([ex - 0.35, STONE_Y[2], ez - 0.5], [ex + 0.35, STONE_Y[2] + 0.7, ez + 0.5], f"{s_part}_eye{i + 1}{side}", s_part, "iron"))
            p0 = [ex, STONE_Y[2] + 0.55, ez]
            p1 = [CX + sgn * STAPLE_T, STAPLE_Y + 0.15, ez]
            mid = v_mul(v_add(p0, p1), 0.5)
            stone.append(bar(p0, mid, 0.5, 0.2, f"{s_part}_chain{i + 1}{side}1", s_part, "chain", up=(0.0, 0.0, 1.0), extend=0.15))
            stone.append(bar(mid, p1, 0.2, 0.5, f"{s_part}_chain{i + 1}{side}2", s_part, "chain", up=(0.0, 0.0, 1.0), extend=0.15))
        if abs(ang) > 1e-12:
            rotate(stone, "y", math.degrees(ang), c)
        out += stone
    return out


# ---------------------------------------------------------------- tier 3: the Chilean mill
def build_t3():
    out = []
    c = (CX, 0.0, CZ)
    p = "t3pan"
    for i, a in enumerate(PIER_ANGLES, 1):
        q = polar(a, PIER_R)
        out.append(box([q[0] - 1.1, DECK, q[2] - 1.1], [q[0] + 1.1, UNDER, q[2] + 1.1], f"{p}_leg{i}", p, "iron"))
        out.append(box([q[0] - 1.6, DECK, q[2] - 1.6], [q[0] + 1.6, DECK + 0.5, q[2] + 1.6], f"{p}_foot{i}", p, "iron"))
    out += ring("y", c, UNDER, 14.5, 2.8, 8.5, 10, f"{p}_bottomin", p, "iron")
    out += ring("y", c, UNDER, 14.5, 8.5, RIM_R[1], 20, f"{p}_bottomout", p, "iron")
    out += ring("y", c, 14.5, FLOOR_Y, DIE_R[0], DIE_R[1], 20, f"{p}_die", p, "steel")
    out += disc("y", c, 14.5, 18.0, 3.2, f"{p}_boss", p, "iron")
    n = 20
    east = rim_index(n)
    wall = (RIM_R[1] - 1.0, RIM_R[1])
    out += ring("y", c, 14.5, RIM_TOP, wall[0], wall[1], n, f"{p}_wall", p, "iron", skip=(east,))
    w = 2 * wall[1] * math.tan(math.pi / n)
    ang = TAU * east / n
    out.append(radial("y", c, 14.5, 16.0, wall[0], wall[1], w, ang, f"{p}_wall{east + 1}a", p, "iron"))
    out.append(radial("y", c, 19.5, RIM_TOP, wall[0], wall[1], w, ang, f"{p}_wall{east + 1}b", p, "iron"))
    out.append(radial("y", c, 16.0, 19.5, wall[0] + 0.35, wall[1] - 0.35, w - 0.2, ang, f"{p}_screen", p, "mesh"))
    # the discharge launder outside the screen and its lip over the box
    lx0, lx1 = CX + wall[1], CX + wall[1] + 2.4
    out += [box([lx0, 14.6, 21.4], [lx1, 15.2, 26.6], f"{p}_launder_floor", p, "iron"),
            box([lx0, 15.2, 21.4], [lx1, 19.6, 21.8], f"{p}_launder_n", p, "iron"),
            box([lx0, 15.2, 26.2], [lx1, 19.6, 26.6], f"{p}_launder_s", p, "iron"),
            box([lx1 - 0.4, 15.2, 21.8], [lx1, 17.0, 26.2], f"{p}_launder_e", p, "iron"),
            box([lx1, 14.6, 22.6], [lx1 + 1.8, 15.0, 25.4], f"{p}_launder_lip", p, "iron")]
    out += feed_spout(p, "iron", SPOUT_END, SPOUT_END_Y)
    # the vertical shaft and its bevel wheel, the cross-head and the runners' axle
    s = "t3shaft"
    out += footstep("t3foot")
    out += disc("y", c, DECK + 0.6, 29.6, 1.6, f"{s}_shaft", s, "steel")
    out += vertical_bevels(T3_BEVEL, "t3pinion", s)
    out += disc("y", c, 10.3, 12.4, 2.6, f"{s}_hub", s, "iron")
    out.append(box([CX - 2.6, AXLE_Y - 2.5, CZ - 2.6], [CX + 2.6, AXLE_Y + 2.5, CZ + 2.6], f"{s}_yoke", s, "iron"))
    out += disc("y", c, 29.0, 30.0, 2.0, f"{s}_cap", s, "iron")
    reach = RUNNER_TRACK + RUNNER_W / 2 + 0.75
    out += disc("x", (0.0, AXLE_Y, CZ), CX - reach, CX + reach, 1.1, f"{s}_axle", s, "steel")
    for sgn, tag in ((1.0, "e"), (-1.0, "w")):
        inner = CX + sgn * (RUNNER_TRACK - RUNNER_W / 2)
        outer = CX + sgn * (RUNNER_TRACK + RUNNER_W / 2)
        out += disc("x", (0.0, AXLE_Y, CZ), min(inner, inner - sgn * 0.75), max(inner, inner - sgn * 0.75), 2.2, f"{s}_collar{tag}", s, "iron")
        out += disc("x", (0.0, AXLE_Y, CZ), min(outer, outer + sgn * 0.75), max(outer, outer + sgn * 0.75), 2.2, f"{s}_nut{tag}", s, "iron")
    # the runners: granite wheels with iron tyres, loose on the axle between collar and nut
    for i, sgn in enumerate((1.0, -1.0), 1):
        r = f"t3runner{i}"
        x0 = CX + sgn * (RUNNER_TRACK - RUNNER_W / 2)
        x1 = CX + sgn * (RUNNER_TRACK + RUNNER_W / 2)
        lo, hi = min(x0, x1), max(x0, x1)
        out += disc("x", (0.0, AXLE_Y, CZ), lo + 0.02, hi - 0.02, RUNNER_R - 0.6, f"{r}_stone", r, "polished", k=6)
        out += ring("x", (0.0, AXLE_Y, CZ), lo, hi, RUNNER_R - 0.7, RUNNER_R, 18, f"{r}_tyre", r, "iron")
        out += disc("x", (0.0, AXLE_Y, CZ), lo - 0.01, hi + 0.01, 1.9, f"{r}_hub", r, "iron")
    # the ploughs: an arm out of the cross-head, a hanger down, a blade just over the die turned to the track
    sc = "t3scrapers"
    for i, (a_deg, rad, turn) in enumerate(SCRAPERS, 1):
        piece = [radial("y", c, AXLE_Y + 0.7, AXLE_Y + 1.7, 2.6, rad + 0.6, 1.2, 0.0, f"{sc}_arm{i}", sc, "iron"),
                 radial("y", c, 18.6, AXLE_Y + 0.7, rad - 0.4, rad + 0.4, 0.8, 0.0, f"{sc}_hanger{i}", sc, "iron")]
        blade = box([CX - 1.8, FLOOR_Y + 0.3, CZ + rad - 0.2], [CX + 1.8, 18.8, CZ + rad + 0.2], f"{sc}_blade{i}", sc, "steel")
        rotate([blade], "y", turn, (CX, 0.0, CZ + rad))
        piece.append(blade)
        rotate(piece, "y", a_deg, c)
        out += piece
    return out


# ---------------------------------------------------------------- tier 4: the ball mill
DRUM_C = (0.0, DRUM_Y, CZ)
COUNTER_C = (0.0, SHAFT_Y, CZ)


def tilted(top0, top1, across, th, name, part, tex, axis):
    """A board whose top runs from top0 to top1 (points in the plane normal to `axis`), `across` wide along `axis`
    about the points' own coordinate on it, `th` thick under the top."""
    d = v_norm(v_sub(top1, top0))
    up = v_norm(v_cross(UNIT[axis], d))
    if up[1] < 0:
        up = v_mul(up, -1.0)
    a = v_add(top0, v_mul(up, -th / 2))
    b = v_add(top1, v_mul(up, -th / 2))
    return bar(a, b, across, th, name, part, tex, up=up, extend=0.2)


def build_t4():
    out = []
    bed = "t4bed"
    (bx0, bx1), (by0, by1), (bz0, bz1) = BEDPLATE
    out.append(box([bx0, by0, bz0], [bx1, by1, bz1], f"{bed}_plate", bed, "iron"))
    th = TRUNNION_R[1]
    for i, (x0, x1) in enumerate(PEDESTALS_X, 1):
        for j, (z0, z1) in enumerate(PORTAL_LEGS_Z, 1):
            out.append(box([x0, by1, z0], [x1, PORTAL_CAP[0], z1], f"{bed}_leg{i}{j}", bed, "iron"))
        out.append(box([x0, PORTAL_CAP[0], PORTAL_LEGS_Z[0][0]], [x1, PORTAL_CAP[1], PORTAL_LEGS_Z[1][1]], f"{bed}_cap{i}", bed, "iron"))
        lo_y, hi_y = DRUM_Y - th, DRUM_Y + th
        out += [box([x0, PORTAL_CAP[1], CZ - th - HOUSING_T], [x1, lo_y, CZ + th + HOUSING_T], f"{bed}_bearing{i}_low", bed, "iron"),
                box([x0, hi_y, CZ - th - HOUSING_T], [x1, hi_y + HOUSING_T, CZ + th + HOUSING_T], f"{bed}_bearing{i}_cap", bed, "iron"),
                box([x0, lo_y, CZ - th - HOUSING_T], [x1, hi_y, CZ - th], f"{bed}_bearing{i}_n", bed, "iron"),
                box([x0, lo_y, CZ + th], [x1, hi_y, CZ + th + HOUSING_T], f"{bed}_bearing{i}_s", bed, "iron")]
        for k, zc in enumerate((CZ - th - HOUSING_T / 2, CZ + th + HOUSING_T / 2), 1):
            xm = (x0 + x1) / 2
            out.append(box([xm - 0.5, hi_y + HOUSING_T, zc - 0.5], [xm + 0.5, hi_y + HOUSING_T + 0.5, zc + 0.5], f"{bed}_bearing{i}_bolt{k}", bed, "iron"))
    # the drum: trunnions, heads, the riveted shell, the manhole, the girth gear, the discharge lip
    d = "t4drum"
    out += ring("x", DRUM_C, *W_TRUNNION_X, *TRUNNION_R, 16, f"{d}_wtrunnion", d, "iron")
    out += ring("x", DRUM_C, *E_TRUNNION_X, *TRUNNION_R, 16, f"{d}_etrunnion", d, "iron")
    for tag, (x0, x1) in (("w", (SHELL_X[0] - HEAD_T, SHELL_X[0])), ("e", (SHELL_X[1], SHELL_X[1] + HEAD_T))):
        out += ring("x", DRUM_C, x0, x1, TRUNNION_R[1] - 0.2, 6.2, 12, f"{d}_{tag}headin", d, "iron")
        out += ring("x", DRUM_C, x0, x1, 6.2, HEAD_R, 24, f"{d}_{tag}headout", d, "iron", phase=TAU / 48)
    out += ring("x", DRUM_C, SHELL_X[0] - 0.05, SHELL_X[1] + 0.05, *SHELL_R, 24, f"{d}_shell", d, "riveted")
    out += ring("x", DRUM_C, LIP[0], LIP[1], TRUNNION_R[0], LIP[2], 16, f"{d}_lip", d, "iron", phase=TAU / 32)
    out.append(radial("x", DRUM_C, *MANHOLE_X, SHELL_R[1] - 0.1, SHELL_R[1] + 0.5, 4.4, 0.0, f"{d}_manhole", d, "iron"))
    for k, (xb, dt) in enumerate(((MANHOLE_X[0] + 0.6, -1.6), (MANHOLE_X[0] + 0.6, 1.6), (MANHOLE_X[1] - 0.6, -1.6), (MANHOLE_X[1] - 0.6, 1.6)), 1):
        y = DRUM_Y + SHELL_R[1] + 0.5
        out.append(box([xb - 0.35, y, CZ + dt - 0.35], [xb + 0.35, y + 0.4, CZ + dt + 0.35], f"{d}_manholebolt{k}", d, "iron"))
    m, n = GIRTH["m"], GIRTH["teeth"]
    ph_g = angle_of("x", [0.0, -1.0, 0.0]) + math.pi / n          # a gap straight down, over the pinion
    gx0, gx1 = GIRTH_X
    root = GIRTH_R - 1.25 * m
    out += ring("x", DRUM_C, gx0, gx1, root - 0.6, root, 24, f"{d}_girthrim", d, "iron", phase=ph_g)
    width = math.pi * m / 2 * 0.9
    out += [radial("x", DRUM_C, gx0 + 0.03, gx1 - 0.03, root - 0.25, GIRTH_R + m, width, ph_g + TAU * i / n,
                   f"{d}_girth_tooth{i + 1}", d, "iron") for i in range(n)]
    out += ring("x", DRUM_C, gx0, gx0 + 0.8, TRUNNION_R[1] - 0.2, root - 0.5, 12, f"{d}_girthweb", d, "iron")
    # the drive: the mitre pinion on the power shaft's end, the mitre wheel and the girth pinion on the countershaft
    mi, cs = "t4mitre", "t4counter"
    hub_r = 1.4                                             # inside the root cone at the toe: clear of the other wheel's teeth
    out += bevel("z", 1.0, P, MITRE_R, MITRE_R, MITRE["teeth"], MITRE["m"], MITRE["face"], angle_of("z", [1.0, 0.0, 0.0]),
                 f"{mi}_bevel", mi, "steel")
    out += disc("z", P, SHAFT_END, CZ + MITRE_R + 1.0, hub_r, f"{mi}_hub", mi, "steel")
    out += bevel("x", 1.0, P, MITRE_R, MITRE_R, MITRE["teeth"], MITRE["m"], MITRE["face"],
                 angle_of("x", [0.0, 0.0, 1.0]) + math.pi / MITRE["teeth"], f"{cs}_bevel", cs, "steel")
    out += disc("x", COUNTER_C, COUNTER_X[0], CX + MITRE_R + 1.0, hub_r, f"{cs}_hub", cs, "steel")
    out += disc("x", COUNTER_C, COUNTER_X[0] + 0.2, COUNTER_X[1], 1.0, f"{cs}_shaft", cs, "steel")
    out += spur("x", COUNTER_C, gx0, gx1, PINION_R, PINION["teeth"], PINION["m"], angle_of("x", [0.0, 1.0, 0.0]),
                f"{cs}_pinion", cs, "steel")
    cb = "t4cbear"
    for i, (x0, x1) in enumerate(CBEARINGS_X, 1):
        out += pillow((x0, x1), (CZ - 2.0, CZ + 2.0), SHAFT_Y, SHAFT_Y + 2.5, f"{cb}_{i}", cb, axis="x", foot=BEDPLATE[1][1])
        out += disc("x", COUNTER_C, x0 - 0.6, x0, COLLAR_R, f"{cs}_collar{i}w", cs, "steel")
        out += disc("x", COUNTER_C, x1, x1 + 0.6, COLLAR_R, f"{cs}_collar{i}e", cs, "steel")
    out += build_t4_feed()
    out += build_charge()
    return out


def build_t4_feed():
    """The feed box at the west trunnion's mouth: the hopper's outlet opens into it through its west wall (fresh ore
    and, at this tier, the concentrator's middlings: both come in by the hopper), and a spout from its east wall
    runs into the trunnion's bore. Two legs carry it on the bedplate."""
    fd, t, w = "t4feed", "planks", HOP_WALL
    (x0, x1), (z0, z1), (y0, y1) = FEEDBOX
    (fx0, fy0), (fx1, fy1) = FEEDBOX_FLOOR
    sz = (CZ - 1.4, CZ + 1.4)                                # the spout's hole in the east wall
    out = [box([x0, y0, z0], [x1, y1, z0 + w], f"{fd}_box_n", fd, t),
           box([x0, y0, z1 - w], [x1, y1, z1], f"{fd}_box_s", fd, t),
           box([x0, y0, z0 + w], [x0 + 0.6, OUTLET_Y[0], z1 - w], f"{fd}_box_w1", fd, t),
           box([x0, OUTLET_Y[1], z0 + w], [x0 + 0.6, y1, z1 - w], f"{fd}_box_w2", fd, t),
           box([x0, OUTLET_Y[0], z0 + w], [x0 + 0.6, OUTLET_Y[1], OUTLET_Z[0]], f"{fd}_box_w3", fd, t),
           box([x0, OUTLET_Y[0], OUTLET_Z[1]], [x0 + 0.6, OUTLET_Y[1], z1 - w], f"{fd}_box_w4", fd, t),
           box([x1 - 0.6, fy1 + 1.8, z0 + w], [x1, y1, z1 - w], f"{fd}_box_e1", fd, t),
           box([x1 - 0.6, y0, z0 + w], [x1, fy1 + 1.8, sz[0]], f"{fd}_box_e2", fd, t),
           box([x1 - 0.6, y0, sz[1]], [x1, fy1 + 1.8, z1 - w], f"{fd}_box_e3", fd, t),
           tilted([fx0, fy0, CZ], [fx1, fy1, CZ], z1 - z0 - 2 * w, 0.6, f"{fd}_box_floor", fd, t, "z")]
    for i, (lz0, lz1) in enumerate(((z0 + 0.1, z0 + 1.6), (z1 - 1.6, z1 - 0.1)), 1):
        out.append(box([x0 + 0.3, BEDPLATE[1][1], lz0], [x0 + 1.8, y0, lz1], f"{fd}_box_leg{i}", fd, "oak"))
    # the spout into the bore
    sx, sy = FEED_SPOUT_END
    out.append(tilted([x1 - 0.7, fy1, CZ], [sx, sy, CZ], sz[1] - sz[0] - 0.6, 0.4, f"{fd}_spout_floor", fd, "iron", "z"))
    for tag, zc in (("n", sz[0] + 0.45), ("s", sz[1] - 0.45)):
        a = [x1 - 0.7, fy1 + 1.0, zc]
        b = [sx, sy + 1.0, zc]
        out.append(tilted(a, b, 0.3, 1.4, f"{fd}_spout_{tag}", fd, "iron", "z"))
    return out


def build_charge():
    """The charge: iron balls heaped in the drum's bottom, the heap's face raised up the side the drum lifts it
    (towards -z when the axle turns forward). The lowest rests on the shell, every other on its neighbours."""
    out = []
    r_in = SHELL_R[0]
    step = 1.85
    cols = 4
    xs = [SHELL_X[0] + 1.8 + (SHELL_X[1] - SHELL_X[0] - 3.6) * i / (cols - 1) for i in range(cols)]
    n = 0
    for xi, x in enumerate(xs):
        for j in range(5):
            for i in range(-5, 6):
                size = (1.9, 2.1, 2.0)[(i + 2 * j + xi) % 3]
                h = size / 2
                y = DRUM_Y - r_in + h - 0.06 + step * 0.86 * j
                z = CZ + step * (i + 0.5 * (j % 2))
                dy, dz = abs(y - DRUM_Y), abs(z - CZ)
                if math.hypot(dy + h * (1 if y < DRUM_Y else -1), dz + h) > r_in + 0.35 or y > DRUM_Y - CHARGE_FACE[0] + CHARGE_FACE[1] * (CZ - z):
                    continue
                n += 1
                el = box([x - h, y - h, z - h], [x + h, y + h, z + h], f"t4balls_ball{n}", "t4balls", "steel")
                out.append(el)
    return out


def build():
    return build_frame() + build_t2() + build_t3() + build_t4()


# ---------------------------------------------------------------- the tiers
# Each tier's set of fitted parts in the order they go on (one item each; the items are #732's to choose), and the
# rig parts each brings. A tier's set replaces the one before it; the frame and the shaft stay.
TIERS = (
    (2, "arrastra", (("t2basin", ("t2basin",)), ("t2post", ("t2foot", "t2pinion", "t2post")), ("t2arms", ("t2arms",)),
                     ("t2stones", ("t2stones",)))),
    (3, "Chilean mill", (("t3pan", ("t3pan",)), ("t3shaft", ("t3foot", "t3pinion", "t3shaft")),
                         ("t3runners", ("t3runner1", "t3runner2")), ("t3scrapers", ("t3scrapers",)))),
    (4, "ball mill", (("t4bed", ("t4bed",)), ("t4drum", ("t4drum",)), ("t4drive", ("t4mitre", "t4counter", "t4cbear")),
                      ("t4balls", ("t4balls",)), ("t4feed", ("t4feed",)))),
)


def tier_of(pid):
    """The tier a part belongs to: 0 for the frame's (the frame and the power shaft), else 2, 3 or 4."""
    return int(pid[1]) if pid[:1] == "t" and pid[1:2].isdigit() else 0


def stage_of_part():
    return {pid: req for _, _, stages in TIERS for req, pids in stages for pid in pids}


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def ratios():
    """Each turning part's turn per axle turn, from the teeth (and the runners' rolling): the signs make every mesh's
    pitch points move together (check_gearing holds them to it)."""
    t2 = -T2_BEVEL["pinion"] / T2_BEVEL["wheel"]
    t3 = -T3_BEVEL["pinion"] / T3_BEVEL["wheel"]
    counter = -1.0                                            # the mitre pair
    drum = -counter * PINION["teeth"] / GIRTH["teeth"]
    spin = RUNNER_TRACK / RUNNER_R * t3
    return {"t2post": t2, "t3shaft": t3, "t3runner1": -spin, "t3runner2": spin, "t4counter": counter, "t4drum": drum}


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    k = ratios()
    centre = pt(CX, 0.0, CZ)

    def rot(axis, pivot, ratio):
        return [{"type": "rotate", "axis": axis, "pivot": pivot, "ratio": r6(ratio)}]

    def part(pid, requires, drivers=(), ride=None):
        return {"id": pid, "match": [f"{pid}_*"], "requires": requires, "ride": ride, "drivers": list(drivers)}

    stage = stage_of_part()
    parts = [{"id": "shaft", "match": ["sh_*"], "requires": None, "ride": None, "drivers": rot("z", pt(CX, SHAFT_Y, 40.0), 1.0)},
             part("t2pinion", stage["t2pinion"], ride="shaft"),
             part("t2post", stage["t2post"], rot("y", centre, k["t2post"])),
             part("t2arms", stage["t2arms"], ride="t2post"),
             part("t2stones", stage["t2stones"], ride="t2post"),
             part("t2foot", stage["t2foot"]),
             part("t2basin", stage["t2basin"]),
             part("t3pinion", stage["t3pinion"], ride="shaft"),
             part("t3shaft", stage["t3shaft"], rot("y", centre, k["t3shaft"])),
             part("t3runner1", stage["t3runner1"], rot("x", pt(CX + RUNNER_TRACK, AXLE_Y, CZ), k["t3runner1"]), ride="t3shaft"),
             part("t3runner2", stage["t3runner2"], rot("x", pt(CX - RUNNER_TRACK, AXLE_Y, CZ), k["t3runner2"]), ride="t3shaft"),
             part("t3scrapers", stage["t3scrapers"], ride="t3shaft"),
             part("t3foot", stage["t3foot"]),
             part("t3pan", stage["t3pan"]),
             part("t4mitre", stage["t4mitre"], ride="shaft"),
             part("t4counter", stage["t4counter"], rot("x", pt(CX, SHAFT_Y, CZ), k["t4counter"])),
             part("t4drum", stage["t4drum"], rot("x", pt(CX, DRUM_Y, CZ), k["t4drum"])),
             part("t4cbear", stage["t4cbear"]),
             part("t4bed", stage["t4bed"]),
             part("t4feed", stage["t4feed"]),
             part("t4balls", stage["t4balls"]),
             {"id": "frame", "match": ["fr_*"], "requires": None, "ride": None, "drivers": []}]
    for p in parts:
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses
REST = 0.0                                   # a pose is the axle's angle, theta (radians)
CYCLE_TURNS = 6                              # every part is back where it started after six turns of the axle


def inputs_of(pose):
    return {"theta": float(pose)}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


def output_point():
    """Where the ground ore leaves: the end of the discharge box's lip, on the east face."""
    return (CELLS_X * B, LIP_Y[1], (LIP_Z[0] + LIP_Z[1]) / 2)


def tiers_json():
    return [{"tier": t, "name": name, "fitted": [req for req, _ in stages]} for t, name, stages in TIERS]


def make_rig(parts):
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the middle of the north "
                    "(operator's) side at ground level; the ore runs west to east (in at the infeed, the middlings too at tier 4; out "
                    "at the output), the axle comes in from the south. Model only: "
                    "no block or renderer reads it yet (#732). tiers: each tier's fitted parts in order (requires values); a tier's "
                    "set replaces the one before it, the frame (requires null) stays. The only input is theta, the axle's angle. "
                    "See the grinder's README.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "infeedCell": list(INFEED_CELL),
        "infeedFace": INFEED_FACE,
        "outputCell": list(OUTPUT_CELL),
        "outputFace": OUTPUT_FACE,
        "output": {"pos": pt(*output_point())},
        "tiers": tiers_json(),
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
CELL_KEYS = ("powerCell", "infeedCell", "outputCell")


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
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig, every tier's parts
    together (the footprint is the same for every tier; which boxes a tier's state uses is gameplay's, #711). No
    lids: whether the top is walked on is an open question."""
    written = flatten(shape["elements"], textures={})
    rest = [posed(w, _part_matrix(ship_parts, part_of(ship_parts, w.name), inputs_of(REST))) for w in written]
    by_cell = {}
    for el in rest:
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, []).append(el)
    out = []
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        boxes = cell_boxes(by_cell[pos], pos) if pos in by_cell else None
        out.append({"pos": list(pos), "boxes": boxes} if boxes else {"pos": list(pos), "hollow": True})
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
          f"{ship['infeedFace']}, output {ship['outputCell']} {ship['outputFace']}")
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
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Every tier's parts are here, "
             "each under its tier's requires; keep element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return (REST, 0.9, 2.3)


def state_els(els, k):
    """The elements shown in state k: the frame's, and tier k's (k 0: the frame alone)."""
    return [el for el in els if tier_of(el.part) in (0, k)]


def fix_coplanar(els, parts):
    """The z-fighting fix, a tier at a time: the frame with tier 2, with tier 3, with tier 4 (tiers are never drawn
    together, so faces of two tiers in one plane do not fight), round again until a round finds nothing, so the frame's insets settle."""
    log = []
    for _ in range(3):
        found = 0
        for k in (2, 3, 4):
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
            print(f"coplanar faces before the fix, frame and tier {k}: {before}; {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_grinder.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "grinder.json", args.out / "grinder_frame.json", args.out / "grinder-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "grinder.json", SHAPE_DIR / "grinder_frame.json", RIG_DIR / "grinder-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_grinder.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship, list_keys=("cells", "tiers", "parts")),
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
