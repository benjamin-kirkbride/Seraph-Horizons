#!/usr/bin/env python3
"""Generate the arrastra's shapes, rig and reference poses.

The arrastra is the ore line's small grinder (behaviour #732, model #733, epic #684): a mule-sized drag-stone mill,
batch, loaded and emptied by hand, turned by a vanilla axle in place of the mule. One machine, built in stages on
its frame (the crucible furnace's pattern):

    frame     placed: a timber head frame (four posts, two stringers, two transoms, knee braces) and, hung from it, the
              power shaft (the vanilla axle's cross at the back, high, then an iron shaft in two drop hangers); it turns
              with the axle from the start
    pit       a stone-paved circular pit at ground level: a centre stone, cobbled paving in rows, a course of edging,
              a drystone curb round them; in the curb at the front the draw gate (a threshold, two oak jambs and a
              plank gate with an iron grip)
    post      the footstep on the centre stone and the top bearing under the transom, the oak post between them with
              its wooden cog wheel, and the lantern pinion on the power shaft's end: the post turns at a third of the axle
    arms      four sweep arms through the post
    stones    four basalt drag stones, each on a bridle of two chains from its arm

and, apart from the stages, the pit's contents: the charge as tipped in (crushed rock, damped) or the same ground to a
paste. No chute, no pipe and no load anchor anywhere: batch equipment is loaded and emptied by hand (#711), and the
owner's rule is that nothing automated feeds it, so nothing is drawn that a chute could meet (no hopper). The player
tips the charge straight into the open pit over its low curb, and pours a bucket of water a rock in after it (the
owner's ruling: the period damped a charge to a paste and added a little as needed, by the barrel, not a flow). The
pit is open from above either side of the head frame for it. The ground charge is drawn out at the gate in the curb
at the pit's front, at the controller (draw, the only hand action with an anchor).

Everything is built here from plain boxes; no other mod's model is used. No mule, wheel or engine: power comes from a
vanilla axle on the power face, and the arrastra grinds whichever way it turns (the sweep is symmetric).

It writes, deterministically,

    arrastra.json         the whole machine, every stage and both contents               (assets/.../shapes/block/)
    arrastra_frame.json   the static frame only (block and item)                        (assets/.../shapes/block/)
    arrastra-rig.json     cells, anchors, the build's stages and the part rig           (assets/.../config/)
    rig-reference.json    every part's matrix at a grid of axle angles                  (tests/Arrastra/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_arrastra.py) and exits non-zero if a
check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the machine
box's north-west-bottom corner (the "build frame"); `shipped()` moves it so the controller cell (ORIGIN_CELL, the
middle of the front (north) row at ground level, at the draw gate) is [0,0,0]. The player works it
from the front, and the axle comes in at the back.

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
REFERENCE_OUT = MOD / "tests" / "Arrastra" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/Arrastra/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

# ---------------------------------------------------------------- the box, the cells, the anchors
CELLS_X, CELLS_Y, CELLS_Z = 3, 2, 2          # the box: three wide, two high, two deep; the pit is two blocks across
EMPTY_CELLS = ((0, 1, 0), (2, 1, 0), (0, 1, 1), (2, 1, 1))   # the upper layer's side cells, over the pit's four corners:
                                             # nothing of the machine is ever there (the head frame and the drive are all in
                                             # the middle column), so they are not the machine's (8 cells, not 12); over
                                             # the pit's front corners they leave it open for tipping the charge in by hand
ORIGIN_CELL = (1, 0, 0)                      # the controller: the middle of the front row at ground level, the draw gate's
CX, CZ = 24.0, 16.0                          # the pit's centre: the post's axis
POWER_CELL, POWER_FACE = (1, 1, 1), "south"  # the vanilla axle comes in along z at (x 24, y 24), high: the drive is overhead
DRAW_CELL, DRAW_FACE = (1, 0, 0), "north"    # a hand action: the draw gate in the curb, where the player draws it out

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "planks": "game:block/wood/planks/oak1",
    "axle": "game:block/wood/planks/generic",            # the vanilla axle's own texture, on its cross where it comes in
    # the wooden gearing (the cog wheel and the lantern): millwright's work, in vanilla's wooden gears' own texture, as the
    # eidolon gantry's winch gearing
    "mechanics": "game:block/wood/planks/generic",
    "iron": "game:block/metal/plate/iron",
    "steel": "game:block/metal/sheet-plain/steel1",
    "chain": "game:block/metal/armor-generic/chain-iron",
    "cobble": "game:block/stone/cobblestone/granite1",
    "drystone": "game:block/stone/drystone/granite1",
    "granite": "game:block/stone/rock/granite1",
    "basalt": "game:block/stone/rock/basalt1",           # the drag stones: a hard, dark rock, as they were chosen
    # the contents, stand-ins (a renderer may set either code to the charge's own ore): the charge as tipped in, crushed
    # rock; the same ground fine, a paste
    "charge": "game:block/stone/gravel/granite",
    "paste": "game:block/stone/sand/granite",
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the pit (stage `pit`)
PAVE_TOP = 1.5                               # the paving's top: flags laid on the ground, the pit's floor at ground level
CENTRE_STONE = (3.2, 2.0)                    # the centre stone (radius, top), a little proud: the post's step sits on it
PAVE_ROWS = 9                                # the paving: rows of cobbled flags across the pit, their ends under the edging
EDGING = (12.0, 13.5, 16)                    # a course of edging stones round the paving, against the curb (radii, stones)
EDGING_TOP = PAVE_TOP + 0.3
CURB_R = (13.5, 16.0)                        # the drystone curb round the paving, two blocks across outside
CURB_TOP = 6.0                               # low: the curb is the open rim the charge and the water are tipped over by hand
TIP_RIM_MAX = 8.0                            # no higher than half a block, so it reads as a rim to tip over, not a wall
CURB_N = 16                                  # its stones, stone i centred at i / 16 of a turn from +z towards +x
CURB_W = 2 * CURB_R[1] * math.tan(math.pi / CURB_N)   # a curb stone's width (its box, across its axis)
GATE_STONE = CURB_N // 2                     # the stone at the front (-z), left out: the draw gate stands in its place
GATE_SILL_TOP = EDGING_TOP                   # the gate's threshold, level with the edging: the charge is drawn out over it
GATE_JAMB = 0.9                              # the gate's two oak jambs, at the opening's sides, across the curb
GATE_TOP = CURB_TOP + 1.2                    # the jambs' tops
GATE_BOARD = (14.3, 15.2)                    # the gate (planks) between the jambs, its ends in their grooves (radii)
GATE_BOARD_TOP = CURB_TOP + 0.4
GROOVE = 0.3                                 # how far the gate's ends run into the jambs
HANDLE = (1.2, (4.6, 5.6), 0.5)              # the iron grip on the gate's outer face: half its length, its heights, how proud

# ---------------------------------------------------------------- the head frame (frame)
POST_X = ((17.0, 20.0), (28.0, 31.0))        # four posts outside the paving, set in the curb, under the two stringers
POST_Z = ((0.0, 2.8), (29.2, 32.0))
TOP = (27.0, 31.0)                           # the stringers (along z, on the posts) and the transoms between them
T1_Z = (14.0, 18.0)                          # the transom over the post: its top bearing hangs from it
T2_Z = (24.8, 30.4)                          # the transom over the power shaft: its two drop hangers hang from it
BRACE = (6.0, 6.5)                           # each post's knee brace: along the stringer, and down the post

# ---------------------------------------------------------------- the power shaft (frame; it turns from the start)
SHAFT_Y = 24.0                               # its axis, along z at x CX: the power cell's centre
SHAFT_R = 1.0                                # its iron shaft's apothem (an octagon)
SHAFT_END = 19.8                             # its front end, just through the lantern's front disc
COUPLING_Z = 30.6                            # from here to the back face, the vanilla axle's cross (two 4 x 2 boards)
HANGERS_Z = ((25.4, 27.4), (28.4, 30.0))     # its two drop hangers under T2
COLLAR_R = 1.6

# ---------------------------------------------------------------- the post and the sweep (stages `post`, `arms`, `stones`)
# The gearing is wooden, as the period's was (the owner's ruling): a built-up cog wheel on the post, its cogs mortised
# upright into the face of its rim (a face wheel), driven by a lantern pinion (staves between two discs) on the power
# shaft's end, the millwright's right-angle drive. 4 staves to 12 cogs, 3:1, the post turns a third of the axle. Not
# 6:18: above the ground layer the machine is only its middle column, so nothing turning there may reach more than 8
# voxels from the post, and 18 cogs in a rim that size would be finer than the iron teeth this replaces.
GEAR = {"wheel": 12, "pinion": 4}
R_WHEEL = 6.7                                # the cogs' pitch circle (their middles), about the post
R_LANTERN = R_WHEEL * GEAR["pinion"] / GEAR["wheel"]   # the staves' circle, about the shaft: the same pitch, 3.5
STAVE_R = 0.75                               # a stave's apothem (an octagon, 1.5 across)
COG_L = (5.8, 7.5)                           # a cog's radial span on the rim (its length along the radius)
COG_W, COG_TIP_W = 1.6, 1.2                  # its width across: from the rim to the pitch line, and from there to its tip
PITCH_Y = SHAFT_Y - R_LANTERN                # the pitch line: the bottom staves' axis, where the cogs drive them
COG_TIP = PITCH_Y + 0.8                      # a cog's top: under the shaft, inside the lantern
RIM = ((5.6, 7.6), (18.6, 20.2), 12)         # the rim: radii (an apothem outside), heights, felloes
WHEEL_ARM_W = 1.6                            # its four arms, mortised through the post
LANTERN_GAP = (COG_L[0] - 0.9, COG_L[1] + 0.3)   # between the lantern's discs (radii from the post): the cogs run here; the
                                             # back disc stays clear in front of T2
DISC_T = 0.9                                 # a lantern disc's thickness
DISC_R = R_LANTERN + STAVE_R + 0.35          # its apothem: the staves stand inside it
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

# ---------------------------------------------------------------- the contents (charge / paste)
CONTENT_R = (4.4, CURB_R[0] - 0.05)          # an annulus over the paving: clear of the footstep, inside the curb
CONTENT_BED = PAVE_TOP - 0.3                 # its underside, bedded into the paving: hidden, and in no plane the stones' is
CHARGE_TOP = PAVE_TOP + 1.5                  # the charge as tipped in: crushed rock, damped; the stones stand out of it
PASTE_TOP = PAVE_TOP + 1.2                   # ground to a paste, settled a little lower


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


def cog_wheel(phase, name, part):
    """The post's cog wheel, built up in wood: a rim of RIM felloes, four arms mortised through the post under it, and
    GEAR["wheel"] cogs mortised upright into the rim's face, a gap at `phase` (about y, from +z towards +x). A cog is
    two boxes: COG_W wide from inside the rim to the pitch line, COG_TIP_W from there to its top."""
    c = (CX, 0.0, CZ)
    (r_in, r_out), (y0, y1), n = RIM
    out = ring("y", c, y0, y1, r_in, r_out, n, f"{name}_rim", part, "mechanics", phase=phase + math.pi / n)
    for i in range(4):
        s = 0.02 * i
        out.append(radial("y", c, y0 + 0.15 + s, y1 - 0.25 - s, POST_R - 0.6, r_in + 0.5, WHEEL_ARM_W,
                          phase + math.pi / 4 + math.pi / 2 * i, f"{name}_arm{i + 1}", part, "mechanics"))
    nw = GEAR["wheel"]
    for i in range(nw):
        a = phase + TAU * (i + 0.5) / nw
        out.append(radial("y", c, y0 + 0.3, PITCH_Y, COG_L[0], COG_L[1], COG_W, a, f"{name}_cog{i + 1}a", part, "mechanics"))
        out.append(radial("y", c, PITCH_Y - 0.05, COG_TIP, COG_L[0] + 0.1, COG_L[1] - 0.1, COG_TIP_W, a,
                          f"{name}_cog{i + 1}b", part, "mechanics"))
    return out


def lantern(phase, name, part):
    """The lantern pinion on the power shaft's end: GEAR["pinion"] staves (octagons) on its circle, one at `phase`
    (about z, from +x towards +y), let into two board discs either side of the gap the wheel's cogs run in, each
    disc bound with an iron hoop. The shaft runs through both discs."""
    z0, z1 = (CZ + r for r in LANTERN_GAP)
    c = (CX, SHAFT_Y, 0.0)
    out = []
    for i, (a, b) in enumerate(((z0 - DISC_T, z0), (z1, z1 + DISC_T)), 1):
        out += disc("z", c, a, b, DISC_R, f"{name}_disc{i}", part, "mechanics", phase=math.pi / 8)
        out += disc("z", c, a + 0.2, b - 0.2, DISC_R + 0.2, f"{name}_hoop{i}", part, "iron", phase=math.pi / 8)
    npn = GEAR["pinion"]
    for k in range(npn):
        a = phase + TAU * k / npn
        sc = (CX + R_LANTERN * math.cos(a), SHAFT_Y + R_LANTERN * math.sin(a), 0.0)
        out += disc("z", sc, z0 - 0.4, z1 + 0.4, STAVE_R, f"{name}_stave{k + 1}", part, "mechanics")
    return out


def pitch_point():
    """The mesh's pitch point: under the power shaft, where the bottom stave meets the cogs' middles."""
    return [CX, PITCH_Y, CZ + R_WHEEL]


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
    """The power shaft (the part `shaft`, the frame's: it turns from the start): the vanilla axle's cross from the
    back face, then an octagonal iron shaft to its front end; collars on both faces of the first hanger."""
    p = "shaft"
    zf = CELLS_Z * B
    out = [box([CX - 2.0, SHAFT_Y - 1.0, COUPLING_Z], [CX + 2.0, SHAFT_Y + 1.0, zf], "sh_axle_a", p, "axle"),
           box([CX - 1.0, SHAFT_Y - 2.0, COUPLING_Z], [CX + 1.0, SHAFT_Y + 2.0, zf - 0.01], "sh_axle_b", p, "axle")]
    out += disc("z", (CX, SHAFT_Y, 0.0), SHAFT_END, COUPLING_Z + 0.4, SHAFT_R, "sh_rod", p, "steel")
    z0, z1 = HANGERS_Z[0]
    out += disc("z", (CX, SHAFT_Y, 0.0), z0 - 0.6, z0, COLLAR_R, "sh_collar_n", p, "steel")
    out += disc("z", (CX, SHAFT_Y, 0.0), z1, z1 + 0.6, COLLAR_R, "sh_collar_s", p, "steel")
    return out


def build_frame():
    return build_head() + build_power()


# ---------------------------------------------------------------- stage: the pit
def build_pit():
    """The pit (stage `pit`): the centre stone, cobbled flags in rows to the edging, a course of edging stones, and
    the drystone curb round them, at the front the draw gate in the place of a curb stone (build_gate)."""
    q = "pit"
    c = (CX, 0.0, CZ)
    out = disc("y", c, 0.0, CENTRE_STONE[1], CENTRE_STONE[0], f"{q}_centre", q, "granite")
    z0 = CZ - EDGING[1]
    rw = 2 * EDGING[1] / PAVE_ROWS
    reach = (EDGING[0] + EDGING[1]) / 2
    for i in range(PAVE_ROWS):
        za, zb = z0 + rw * i, z0 + rw * (i + 1)
        near = 0.0 if za < CZ < zb else min(abs(za - CZ), abs(zb - CZ))
        half = math.sqrt(max(reach ** 2 - near ** 2, 0.0))
        out.append(box([CX - half, 0.0, za], [CX + half, PAVE_TOP, zb], f"{q}_pave{i + 1}", q, "cobble"))
    out += ring("y", c, 0.0, EDGING_TOP, EDGING[0], EDGING[1], EDGING[2], f"{q}_edge", q, "cobble", phase=math.pi / EDGING[2])
    out += ring("y", c, 0.0, CURB_TOP, CURB_R[0], CURB_R[1], CURB_N, f"{q}_curb", q, "drystone", skip=(GATE_STONE,))
    return out + build_gate()


def gate_span():
    """The draw gate's opening across the curb (x): the left-out stone's width about the front's centre line."""
    return CX - CURB_W / 2, CX + CURB_W / 2


def build_gate():
    """The draw gate (stage `pit`) in the place of the front curb stone, where the ground charge is drawn out by hand:
    a granite threshold across the curb level with the edging, two oak jambs at the opening's sides, and between them
    a plank gate standing in their grooves to above the curb, an iron grip on its outer face. Built square to the front
    (the left-out stone points straight at -z), so every box is axis-aligned: radius r is z = CZ - r."""
    q = "pit"
    x0, x1 = gate_span()
    zo, zi = CZ - CURB_R[1], CZ - CURB_R[0]
    out = [box([x0, 0.0, zo], [x1, GATE_SILL_TOP, zi], f"{q}_gate_sill", q, "granite"),
           box([x0, 0.0, zo], [x0 + GATE_JAMB, GATE_TOP, zi], f"{q}_gate_jambw", q, "oak"),
           box([x1 - GATE_JAMB, 0.0, zo], [x1, GATE_TOP, zi], f"{q}_gate_jambe", q, "oak")]
    bz = (CZ - GATE_BOARD[1], CZ - GATE_BOARD[0])
    out.append(box([x0 + GATE_JAMB - GROOVE, GATE_SILL_TOP, bz[0]], [x1 - GATE_JAMB + GROOVE, GATE_BOARD_TOP, bz[1]],
                   f"{q}_gate_board", q, "planks"))
    hl, (hy0, hy1), hp = HANDLE
    out.append(box([CX - hl, hy0, bz[0] - hp], [CX + hl, hy1, bz[0]], f"{q}_gate_grip", q, "iron"))
    return out


# ---------------------------------------------------------------- stages: the post, the arms, the stones
def build_post():
    """Stage `post`: the footstep on the centre stone and the top bearing under T1 (`step`, fixed), the post with its
    gudgeons, bands, cap and wooden cog wheel (`post`, turning), and the lantern pinion on the power shaft's end
    (`pinion`, riding the shaft)."""
    out = []
    c = (CX, 0.0, CZ)
    s = "step"
    (sx0, sx1), (sy0, sy1), (sz0, sz1) = STEP
    out += [box([sx0 - 0.4, sy0, sz0 - 0.4], [sx1 + 0.4, sy0 + 0.6, sz1 + 0.4], f"{s}_sole", s, "iron"),
            box([sx0, sy0 + 0.6, sz0], [sx1, sy1, sz1], f"{s}_block", s, "iron")]
    out += [box([CX - 2.6, TOP_BEARING_Y[0], CZ - 2.6], [CX + 2.6, TOP_BEARING_Y[1], CZ + 2.6], f"{s}_bearing", s, "iron"),
            box([CX - 1.6, TOP_BEARING_Y[1], CZ - 1.6], [CX + 1.6, TOP[0], CZ + 1.6], f"{s}_stem", s, "iron")]
    p = "post"
    out += disc("y", c, sy0 + 0.6, POST_Y[0] + 0.4, GUDGEON_R, f"{p}_footgudgeon", p, "steel")
    out += disc("y", c, POST_Y[1] - 0.4, TOP_BEARING_Y[1] - 0.2, GUDGEON_R + 0.2, f"{p}_topgudgeon", p, "steel")
    out += disc("y", c, POST_Y[0], POST_Y[1], POST_R, f"{p}_post", p, "oak")
    out += disc("y", c, ARM_Y[0] - 0.8, ARM_Y[0], POST_R + 0.3, f"{p}_bandlow", p, "iron")
    out += disc("y", c, ARM_Y[1], ARM_Y[1] + 0.8, POST_R + 0.3, f"{p}_bandhigh", p, "iron")
    out += disc("y", c, POST_Y[1] - 1.2, POST_Y[1], POST_R + 0.3, f"{p}_cap", p, "iron")
    ph_w = angle_of("y", [0.0, 0.0, 1.0])                 # the wheel's gap straight back, under the lantern
    out += cog_wheel(ph_w, f"{p}_wheel", p)
    out += disc("y", c, RIM[1][0] - 0.8, RIM[1][0], POST_R + 0.3, f"{p}_bandwheel", p, "iron")   # under the arms
    ph_p = angle_of("z", [0.0, -1.0, 0.0])                # a stave straight down, in the wheel's gap
    out += lantern(ph_p, "pinion_lantern", "pinion")
    return out


def build_sweep():
    """Stages `arms` and `stones`: four sweep arms through the post with a staple under each, and the drag stones on
    their bridles of two chains, one fore and one aft, so the same bridle drags a stone whichever way the arms go."""
    out = []
    c = (CX, 0.0, CZ)
    a_part, s_part = "arms", "stones"
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


# ---------------------------------------------------------------- the contents
def build_contents():
    """The pit's contents, one drawn at a time: the charge as tipped in (`charge`: crushed rock, damped with a bucket of
    water a rock poured in after it) and the same
    ground to a paste (`paste`), each a level annulus on the paving from clear of the footstep to the curb's inner
    face, the drag stones standing out of it. Drawn still: the charge is worked round under the stones, which no
    driver of a fixed layer could show, and a level surface reads right whichever way they go."""
    c = (CX, 0.0, CZ)
    return (ring("y", c, CONTENT_BED, CHARGE_TOP, *CONTENT_R, CURB_N, "charge_ring", "charge", "charge")
            + ring("y", c, CONTENT_BED, PASTE_TOP, *CONTENT_R, CURB_N, "paste_ring", "paste", "paste"))


def build():
    return build_frame() + build_pit() + build_post() + build_sweep() + build_contents()


# ---------------------------------------------------------------- the build's stages
# The stages in the order they go on, each one requires value, and the rig parts each brings. The frame (the head frame
# and the power shaft) is placed; the items and materials of each stage are open (#733).
STAGES = (
    ("pit", ("pit",)),
    ("post", ("step", "post", "pinion")),
    ("arms", ("arms",)),
    ("stones", ("stones",)),
)
FRAME_PARTS = ("frame", "shaft")             # the frame's parts, requires null
CONTENTS = ("charge", "paste")               # the pit's contents, one at a time; needs the whole build
SWEEP_PARTS = ("arms", "stones")            # what passes over the pit as it turns; the only parts over it outside the middle column


def stage_of_part():
    return {pid: req for req, pids in STAGES for pid in pids}


def requires_of(pid):
    """A part's requires value: its stage's, its own (the contents), or None (the frame's)."""
    if pid in FRAME_PARTS:
        return None
    if pid in CONTENTS:
        return pid
    return stage_of_part()[pid]


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def ratios():
    """Each turning part's turn per axle turn, from the teeth: the sign makes the mesh's pitch points move together
    (check_gearing holds it to it)."""
    return {"post": GEAR["pinion"] / GEAR["wheel"]}


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    k = ratios()

    def rot(axis, pivot, ratio):
        return [{"type": "rotate", "axis": axis, "pivot": pivot, "ratio": r6(ratio)}]

    def part(pid, drivers=(), ride=None, match=None):
        return {"id": pid, "match": [match or f"{pid}_*"], "requires": requires_of(pid), "ride": ride, "drivers": list(drivers)}

    parts = [part("shaft", rot("z", pt(CX, SHAFT_Y, 24.0), 1.0), match="sh_*"),
             part("pinion", ride="shaft"),
             part("post", rot("y", pt(CX, 0.0, CZ), k["post"])),
             part("arms", ride="post"),
             part("stones", ride="post"),
             part("step"),
             part("pit"),
             *[part(c) for c in CONTENTS],
             part("frame", match="fr_*")]
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


def build_json():
    return {"_comment": "Provisional (#733): the stages in the order they go on, each fitting its requires value once its "
                        "materials are in; the frame (the head frame and the power shaft) is placed. The frame's recipe and "
                        "each stage's items are open. The contents (charge, paste) are the batch's, not stages.",
            "stages": [{"requires": req} for req, _ in STAGES]}


def make_rig(parts):
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the middle of the "
                    "front (north) row at ground level, where the draw gate is. Batch, by hand: no chute, "
                    "no load or output anchor and no pipe; nothing automated feeds it. The player tips the charge "
                    "straight into the open pit over its low curb and pours a bucket of water a rock after it, and draws "
                    "the ground charge out at the gate in the curb (drawCell, its north face); the axle comes in high at the back (powerCell). Model only: no block or "
                    "renderer reads it yet (#732). build: the stages in order. cells: the boxes of the whole machine built "
                    "(the frame and every stage), the turning parts' swept over the cycle. The only "
                    "input is theta, the axle's angle. See the arrastra's README.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "drawCell": list(DRAW_CELL),
        "drawFace": DRAW_FACE,
        **{key: {"pos": pt(*p), "part": "stones"} for key, p in stone_points().items()},
        "build": build_json(),
        "parts": parts,
    }


POINTS = tuple(f"stone{i + 1}" for i in range(4))


def stone_points():
    """Where the particles hang (build frame, voxels, at rest): stone1..stone4, the middle of each drag stone's run
    at the charge's top, where it drags through the wet charge. They ride the stones part round with the post."""
    c = (CX, CHARGE_TOP, CZ)
    r = (STONE_R[0] + STONE_R[1]) / 2
    out = {}
    for i in range(4):
        d = radial_dir("y", math.radians(ARM_ANGLE) + TAU * i / 4)
        out[f"stone{i + 1}"] = (c[0] + r * d[0], c[1], c[2] + r * d[2])
    return out


# ---------------------------------------------------------------- shipped
CELL_KEYS = ("powerCell", "drawCell")
CELL_PARTS_OUT = CONTENTS                    # not in the cells' boxes: the batch's contents


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
    for key in POINTS:
        ship[key] = {**rig[key], "pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed by the shipped rig: the whole machine built (the
    frame and every stage; not the contents), a turning part's elements swept over the cycle
    (sweep_poses), so the boxes hold wherever the sweep is and nobody stands in its way. No lids: whether the top is
    walked on is open."""
    written = flatten(shape["elements"], textures={})
    by_cell = {}
    for w in written:
        pid = part_of(ship_parts, w.name)
        if pid in CELL_PARTS_OUT:
            continue
        moving = any(q["drivers"] or q.get("ride") for q in ship_parts if q["id"] == pid)
        for pose in (sweep_poses() if moving else (REST,)):
            el = posed(w, _part_matrix(ship_parts, pid, inputs_of(pose)))
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
          f"y {span[1]}, z {span[2]}; power {ship['powerCell']} {ship['powerFace']}, draw {ship['drawCell']} {ship['drawFace']}")
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
    return {"_comment": f"Generated by {SCRIPT} from the shipped arrastra-rig.json's parts: each part's matrix as 3 rows of 4 "
                        "(block units) at each axle angle theta (travel |theta|; nothing else is read). The site's and the "
                        "repository's tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Every stage's parts and both "
             "contents are here, each under its requires; keep element names when editing: the rig finds "
             "its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return (REST, 0.9, 2.3)


def state_els(els, content):
    """The elements drawn with the machine built and `content` (charge or paste) in the pit: everything but the other
    content."""
    return [el for el in els if el.part not in CONTENTS or el.part == content]


def fix_coplanar(els, parts):
    """The z-fighting fix, a content at a time with the whole machine (the two contents are never drawn together, so
    their faces in one plane do not fight), round again until a round finds nothing, so the shared insets settle. A
    state with fewer stages fitted draws a subset of these elements, so it fights nowhere either."""
    log = []
    for _ in range(3):
        found = 0
        for k in CONTENTS:
            sub = state_els(els, k)
            before, hidden = fix_coplanar_posed(sub, lambda es, pose: [posed(el, pm(parts, el.part, pose)) for el in es],
                                                coplanar_poses(), rounds=20)
            log.append((k, {p: len(v) for p, v in before.items()}, hidden))
            found += sum(len(v) for v in before.values()) + hidden
        if not found:
            break
    return log


def main():
    ap = argparse.ArgumentParser(description="Generate the arrastra's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_arrastra
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    for el in els:
        if el.part is None:
            raise ValueError(f"{el.name} has no part")
    if not args.quick:
        for k, before, hidden in fix_coplanar(els, parts):
            print(f"coplanar faces before the fix, the machine with the {k}: {before}; {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_arrastra.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "arrastra.json", args.out / "arrastra_frame.json", args.out / "arrastra-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "arrastra.json", SHAPE_DIR / "arrastra_frame.json", RIG_DIR / "arrastra-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_arrastra.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship, list_keys=("cells", "parts")),
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
