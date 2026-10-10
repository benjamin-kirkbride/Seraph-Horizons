#!/usr/bin/env python3
"""Generate the classifier's shapes, rig and reference poses.

The classifier is the "classify" stage of the pack's 1800s ore mill line (issue #731; its gameplay is
#730, the shared upgradeable-machine code #711): one machine, upgraded in place. Hand and tier 1 use
the riddle, a separate station; this model is tiers 2 to 4, in one frame fixed at tier 4's size, so
the feed inlet, the outlets and the power input never move as it is upgraded.

    tier 2   grizzly and screen: a fixed grizzly of iron bars at 40 degrees under the feed scalps the
             lumps onto a shaking screen hung on four hangers, which an eccentric on each end of the
             entry shaft shakes through a connecting rod; the screen's oversize runs off its tail into
             a spout to the oversize outlet, its fines fall into the fines bin
    tier 3   trommel: a cylindrical screening drum on spiders, its shaft inclined 1 in 12, turned
             from the entry shaft by a bevel pair (8:24); a feed chute from the inlet into its high
             end, its oversize off the low end into a discharge spout to the oversize outlet, its fines
             through the jacket into the same bin
    tier 4   the trommel with an oversize return: a return chute in place of the discharge spout takes
             the oversize out of the south side, the return outlet, back to the crusher

Fitting a tier's set replaces the previous tier's working parts (#711): the grizzly is not kept as a
scalper ahead of the trommel (README, "Open for the owner").

It writes, deterministically,

    classifier.json         the whole machine, every tier's parts   (assets/.../shapes/block/)
    classifier_frame.json   the static frame only (block and item)  (assets/.../shapes/block/)
    classifier-rig.json     cells, anchors and the part rig         (assets/.../config/)
    rig-reference.json      every part's matrix at a grid of poses  (tests/Classifier/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_classifier.py) and
exits non-zero if a check fails. Everything is built here from plain boxes; no other mod's model is
used.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from
the machine box's north-west-bottom corner (the "build frame"); the controller cell is the build
frame's [0,0,0], so the shipped files are the build frame divided by 16. Material flows east: the feed
comes in high at the west end.

The rig's one input is theta, the axle's angle (and psi, its travel, which nothing reads): the entry
shaft turns with it, the eccentrics shake the screen once a turn, and the trommel turns a third of a
turn a turn. Either way round works: nothing here needs a rectifier.
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
REFERENCE_OUT = MOD / "tests" / "Classifier" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/Classifier/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

# ---------------------------------------------------------------- the box, the cells, the ports
CELLS_X, CELLS_Y, CELLS_Z = 4, 3, 2          # four long (x, the flow), three high, two wide
ORIGIN_CELL = (0, 0, 0)                      # the controller: the feed end's north-west bottom cell
POWER_CELL, POWER_FACE = (3, 1, 0), "north"  # the vanilla axle comes in along z at (x 56, y 24)
ZD = 13.5                                    # the work's centre line (z): grizzly, screen, drum, bin

# The four ports, fixed from the frame on: (cell, face, point in voxels). The feed comes in high at the
# west end; the fines leave low on the north side, the oversize low on the east end (tiers 2 and 3), and
# the oversize return low on the south side (tier 4).
FEED = ((0, 2, 0), "west")
FINES = ((1, 0, 0), "north")
OVERSIZE = ((3, 0, 0), "east")
RETURN = ((2, 0, 1), "south")

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",            # the frame's timbers and the axle's continuation
    "planks": "game:block/wood/planks/oak1",           # chutes, spouts, the bin, the screen's sides
    "iron": "game:block/metal/plate/iron",             # castings, bars, bearings, rings, spiders, hangers
    "steel": "game:block/metal/sheet-plain/steel1",    # shafts and gears
    "sheet": "game:block/metal/sheet/iron1",           # blank plates and chute linings
    "mesh": "game:block/metal/mesh2",                  # the screening surfaces: the screen's deck, the drum's jacket
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the frame (oak, 4 x 4 posts)
POSTS_X = ((0.0, 4.0), (30.0, 34.0), (60.0, 64.0))
SIDES_Z = ((0.0, 4.0), (28.0, 32.0))         # the north and south side frames
SILL_Y = (0.0, 3.0)                          # cross sills along z, on the ground
SILLS_X = ((0.0, 4.0), (6.0, 9.0), (30.0, 34.0), (38.5, 41.5))      # across the machine; the east posts on short sills
EAST_SILLS_Z = ((0.0, 5.0), (27.0, 32.0))   # the east posts' sills, either side of the oversize outlet
GIRT_Y = (18.0, 21.5)                        # side girts along x, between the posts; the entry shaft's bearings on them
TOP_Y = (44.5, 48.0)                         # top side rails, end ties and cross beams
TOP_BEAMS_X = ((13.0, 16.0), (38.0, 41.0), (46.0, 49.0))   # cross beams: the grizzly and the screen's head; its tail; the drum's east bearing
KNEE_RUN, KNEE_W, KNEE_EMBED = 7.0, 2.4, 0.9    # knee braces: how far along the rail and down the post, their width, how far into each
WEST_TIE = ((0.0, 2.4), (36.5, 40.0))        # the west end tie under the inlet chute (x, y): clear of the grizzly's and the feed chute's heads

# ---------------------------------------------------------------- the feed inlet chute (frame)
INLET_Z = (ZD - 4.0, ZD + 4.0)               # its inside: the grizzly's width
INLET_FLOOR = ((0.18, 41.35), (5.0, 39.9))    # (x, y) of the floor's top, from the west face to its lip
INLET_TOP = 44.3                             # its sides' top, under the top end tie
BOARD = 0.6                                  # a plank's thickness

# ---------------------------------------------------------------- the fines bin (frame)
BIN_X = (6.0, 41.5)
BIN_Z = (ZD - 8.5, ZD + 8.5)                 # 5 .. 22
BIN_RIM = 15.5
BIN_FLOOR = 2.5                              # its sides' foot, on the sills
BIN_LOW = (23.75, 3.0)                       # (x, y) of the V floor's bottom: the spout's mouth
SPOUT_X = (21.0, 26.5)                       # the fines spout, through the north side
SPOUT_FACE_Y = 1.6                           # its floor's top at the north face

# ---------------------------------------------------------------- the entry shaft (frame)
ENTRY = (56.0, 24.0)                         # (x, y) of its axis, along z: the power cell's centre
SHAFT_R = 0.9
ENTRY_Z = (0.0, 31.4)
OAK_Z = 1.2                                  # the vanilla axle's cross profile, then a round shaft
BEARINGS_Z = ((1.4, 3.4), (28.5, 31.5))      # its pillow blocks, on the girts
BEARING_H = 2.5                              # a pillow block's half-height round a shaft

# ---------------------------------------------------------------- tier 2: the grizzly
GRIZ_ANGLE = math.radians(40.0)
GRIZ_HEAD = (5.2, 39.6)                      # (x, y) of the bars' top at the head, just under the inlet's lip
GRIZ_LEN = 13.0
GRIZ_BARS, GRIZ_BAR_W, GRIZ_GAP = 4, 1.1, 1.2   # four bars across the inlet's width, 1.2 between them
GRIZ_DEPTH = 2.2
GRIZ_STRAPS_X = (13.6, 14.6)                 # its foot's iron straps, from the first top cross beam


def griz_point(s, below=0.0):
    """The point s along the grizzly's top line from its head, `below` under it (perpendicular)."""
    c, sn = math.cos(GRIZ_ANGLE), math.sin(GRIZ_ANGLE)
    return (GRIZ_HEAD[0] + s * c - below * sn, GRIZ_HEAD[1] - s * sn - below * c)


GRIZ_FOOT = griz_point(GRIZ_LEN)

# ---------------------------------------------------------------- tier 2: the screen and its drive
SCREEN_HEAD, SCREEN_TAIL = (2.6, 27.8), (44.0, 23.4)   # (x, y) of the deck's top at its head board and its tail lip
SCREEN_IN = (ZD - 6.0, ZD + 6.0)             # the deck between the side boards (z)
SIDE_W = 1.5                                 # the side boards' thickness (z)
SIDE_UP, SIDE_DOWN = 2.2, 1.6                # how far they stand above and below the deck's top
DECK_T = 0.3
MESH_X = (12.0, 40.0)                        # the perforated deck; blank plates at the head (the feed plate) and the tail
HANGERS_X = (14.5, 39.5)                     # head and tail hangers, either side
HANGER_H = 13.0                              # every hanger the same length, so the screen moves without turning
HANGER_W, HANGER_T = 0.9, 0.6
HANGER_Z = (SCREEN_IN[0] - SIDE_W - 0.95, SCREEN_IN[1] + SIDE_W + 0.35)   # the hangers' inner faces' z (north, south)
ECC = 1.25                                   # the eccentrics' throw
ECC_PHASE = math.pi / 2                      # the eccentric's angle at rest (from +x towards +y): up
ECC_R = 2.3                                  # the sheave's radius
STRAP = 0.65                                 # the strap's thickness round the sheave
ROD_PIN = (43.0, 23.6)                       # (x, y) of the rods' small-end pins on the screen's tail, at rest
ROD_Z = ((SCREEN_IN[0] - SIDE_W - 1.3, SCREEN_IN[0] - SIDE_W - 0.1),   # north rod, outside the side board
         (SCREEN_IN[1] + SIDE_W + 0.1, SCREEN_IN[1] + SIDE_W + 1.3))   # south rod
ROD_W = 1.0
HARMONICS = 6                                # Fourier terms for the linkage's motions

# ---------------------------------------------------------------- tier 3: the trommel (built level, then tilted)
TILT = math.atan(1.0 / 12.0)                 # the drum's shaft falls 1 in 12 towards the discharge (east)
APEX = (ENTRY[0], ENTRY[1], ZD)              # the bevel pair's apex: the entry shaft's and the drum's axes cross here
DRUM_X = (10.0, 45.0)                        # the drum's length, along its axis (level frame: x' from the west)
DRUM_R = 7.5                                 # the jacket's middle radius
JACKET_T = 0.3
RING_OUT = 8.1
FEED_RING_IN, MID_RING_IN, LIP_RING_IN = 6.4, 6.9, 6.6
RING_T = 0.8
MID_RING_X = (27.0, 28.0)
JACKET_SPANS = ((DRUM_X[0] + RING_T, MID_RING_X[0]), (MID_RING_X[1], 40.5))   # the mesh; then a blank band to the lip
SPIDERS_X = ((13.5, 14.3), (41.0, 41.8))
SPIDER_ARMS = 6
DRUM_SHAFT_X = (4.0, 54.6)
DRUM_SHAFT_R = 0.9
WEST_BEARING_X = (4.5, 6.5)
EAST_BEARING_X = (46.5, 48.5)
MODULE = 0.5                                 # the bevel pair: a pinion of 8 on the entry shaft, a wheel of 24 on the drum's
PINION_TEETH, WHEEL_TEETH = 8, 24
R_PINION, R_WHEEL = MODULE * PINION_TEETH / 2, MODULE * WHEEL_TEETH / 2
DRUM_RATIO = -PINION_TEETH / WHEEL_TEETH     # the drum's turn about its axis (level frame +x) per axle turn
FACE = 1.6                                   # a bevel tooth's face width, along its generator towards the apex
FEED_CHUTE_Z = (ZD + 1.7, ZD + 3.9)          # the feed chute's throat (z): beside the drum's shaft, so the feed falls clear of it
FEED_HEAD = (4.9, 39.1, ZD - 4.6)            # its head plate's north-west corner: just under the inlet's lip, past its north side
FEED_SLOPE = 1.775                           # it falls this much a voxel east (60.6 degrees)
FEED_WING_TILT = math.radians(15.0)          # the head plate falls south into the throat as well as down the chute: a funnel
FEED_WING_X = 7.4                            # the head plate's east edge at its north end; the throat goes on alone
FEED_LIP_X = 11.0                            # the throat's lip, inside the drum's feed opening
FEED_CHUTE_SIDE = 1.0

# ---------------------------------------------------------------- tiers 2-4: the oversize spouts and the return chute
OUTLET_Y = 0.9                               # both oversize spouts' floor's top at the east face: the oversize outlet
TAIL_SPOUT_X0 = 41.6                         # tier 2's spout: its back board, under the screen's tail
TAIL_SPOUT_Y0 = 19.0                         # its floor's top at its back board (its sides' top under the screen)
DISCHARGE_X0 = 43.4                          # tier 3's spout, under the drum's lip
DISCHARGE_Y0 = 13.2                          # (its sides' top under the drum)
SPOUT_Z = (ZD - 5.0, ZD + 5.0)               # their inside, narrowing to the outlet
OUTLET_Z = (ZD - 5.0, ZD + 2.0)              # the oversize outlet: within the east end's north cell
SPOUT_SIDE = 3.0
RETURN_X = (43.0, 47.6)                      # the return chute's inside (x), under the drum's lip, within its cell
RETURN_Z0, RETURN_Y0 = 9.5, 13.5             # its back board (north of where the oversize falls) and its floor's top there
RETURN_FACE_Y = 1.2                          # its floor's top at the south face
RETURN_SIDE = 2.5                            # (its sides' top under the drum)

TIERS = (
    ("frame", "The frame", ()),
    ("tier2", "Tier 2: grizzly and screen", ("grizzly", "screen", "eccentric")),
    ("tier3", "Tier 3: trommel", ("trommel", "bevel", "discharge")),
    ("tier4", "Tier 4: trommel with oversize return", ("trommel", "bevel", "return")),
)
STATES = {k: set(v) for k, _, v in TIERS}
FINISHED = "tier4"                           # the finished machine: what the cells' boxes are built from


# ---------------------------------------------------------------- box helpers
AX = {"x": 0, "y": 1, "z": 2}
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}   # a disc's angle 0 direction, and the in-plane direction it turns towards


def r6(v):
    return round(v + 0.0, 6) + 0.0


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


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def unit(v):
    n = math.sqrt(sum(x * x for x in v))
    return [x / n for x in v]


def obox(c, u, n, size, name, part, tex):
    """A box centred at c whose local axes are u, n and u x n (unit, u and n perpendicular), size along each."""
    u, n = unit(u), unit(n)
    t = cross(u, n)
    r = [[u[i], n[i], t[i]] for i in range(3)]
    return skin(El(name, list(size), list(c), r, {}, part), tex)


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


def annulus(axis, c, a0, a1, r_in, r_out, n, name, part, tex, phase=0.0):
    """A ring of n boxes from r_in to r_out, each as wide as a side of the n-gon at r_out; every other
    one a hair shorter along the axis, so their ends never share a plane."""
    w = 2 * r_out * math.tan(math.pi / n)
    out = []
    for i in range(n):
        s = 0.02 if i % 2 else 0.0
        out.append(radial(axis, c, a0 + s, a1 - s, r_in, r_out, w, phase + TAU * i / n, f"{name}{i + 1}", part, tex))
    return out


def strut(a, b, w, d, name, part, tex, axis="z"):
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


def plate_xy(p0, p1, z0, z1, t, name, part, tex, up=True):
    """A plate in the x-y plane from p0 to p1 (x, y), its top face (or, with up False, its bottom face) on
    that line, `t` thick, across z0..z1."""
    d = unit([p1[0] - p0[0], p1[1] - p0[1], 0.0])
    nrm = [-d[1], d[0]]
    if nrm[1] < 0:
        nrm = [-nrm[0], -nrm[1]]
    s = -1.0 if up else 1.0
    a = [p0[0] + s * nrm[0] * t / 2, p0[1] + s * nrm[1] * t / 2, (z0 + z1) / 2]
    b = [p1[0] + s * nrm[0] * t / 2, p1[1] + s * nrm[1] * t / 2, (z0 + z1) / 2]
    return strut(a, b, t, z1 - z0, name, part, tex, axis="z")


def plate_zy(p0, p1, x0, x1, t, name, part, tex):
    """A plate in the z-y plane from p0 to p1 (z, y), its top face on that line, `t` thick, across x0..x1."""
    d = unit([0.0, p1[1] - p0[1], p1[0] - p0[0]])
    ny, nz = d[2], -d[1]                     # the in-plane normal (y, z), turned towards +y
    if ny < 0:
        ny, nz = -ny, -nz
    a = [(x0 + x1) / 2, p0[1] - ny * t / 2, p0[0] - nz * t / 2]
    b = [(x0 + x1) / 2, p1[1] - ny * t / 2, p1[0] - nz * t / 2]
    return strut(a, b, t, x1 - x0, name, part, tex, axis="x")


def eye(axis, c, a0, a1, r_in, r_out, name, part, tex):
    """A square eye round an axis: four bars."""
    a, u, w = frame_of(axis)
    out = []
    for i, (du, dw) in enumerate(((1, 0), (-1, 0), (0, 1), (0, -1)), 1):
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[a], hi[a] = a0, a1
        if du:
            lo[u], hi[u] = (c[u] + r_in, c[u] + r_out) if du > 0 else (c[u] - r_out, c[u] - r_in)
            lo[w], hi[w] = c[w] - r_out, c[w] + r_out
        else:
            lo[u], hi[u] = c[u] - r_in, c[u] + r_in
            lo[w], hi[w] = (c[w] + r_in, c[w] + r_out) if dw > 0 else (c[w] - r_out, c[w] - r_in)
        out.append(box(lo, hi, f"{name}{i}", part, tex))
    return out


def tilt(els):
    """Elements built level (the drum's axis along x through APEX) turned to the drum's slope: about z
    through the apex, the west end up."""
    return rotate(els, "z", -math.degrees(TILT), APEX)


def drum_axis_point(xl):
    """The drum's axis at level-frame x' (voxels), tilted."""
    dx = xl - APEX[0]
    return (APEX[0] + dx * math.cos(TILT), APEX[1] - dx * math.sin(TILT), APEX[2])


def deck_y(x):
    """The screen's deck top at x, at rest."""
    (x0, y0), (x1, y1) = SCREEN_HEAD, SCREEN_TAIL
    return y0 + (y1 - y0) * (x - x0) / (x1 - x0)


DECK_ANGLE = math.atan2(SCREEN_HEAD[1] - SCREEN_TAIL[1], SCREEN_TAIL[0] - SCREEN_HEAD[0])


# ---------------------------------------------------------------- the frame
def build_frame():
    f = "frame"
    out = []
    zs = {"n": SIDES_Z[0], "s": SIDES_Z[1]}
    names = ("w", "m", "e")
    for (x0, x1), tag in zip(POSTS_X, names):
        for side, (z0, z1) in zs.items():
            out.append(box([x0, SILL_Y[1], z0], [x1, TOP_Y[0], z1], f"fr_post_{tag}{side}", f, "oak"))
    for i, (x0, x1) in enumerate(SILLS_X, 1):
        out.append(box([x0, SILL_Y[0], 0.0], [x1, SILL_Y[1], CELLS_Z * B], f"fr_sill{i}", f, "oak"))
    for side, (z0, z1) in zip(("n", "s"), EAST_SILLS_Z):
        out.append(box([POSTS_X[2][0] - 1.0, SILL_Y[0], z0], [POSTS_X[2][1], SILL_Y[1], z1], f"fr_sille{side}", f, "oak"))
    for side, (z0, z1) in zs.items():
        for i, (xa, xb) in enumerate(((POSTS_X[0][1], POSTS_X[1][0]), (POSTS_X[1][1], POSTS_X[2][0])), 1):
            out.append(box([xa, GIRT_Y[0], z0], [xb, GIRT_Y[1], z1], f"fr_girt_{side}{i}", f, "oak"))
        out.append(box([0.0, TOP_Y[0], z0], [CELLS_X * B, TOP_Y[1], z1], f"fr_rail_{side}", f, "oak"))
    zi = (SIDES_Z[0][1], SIDES_Z[1][0])
    out.append(box([POSTS_X[0][0], TOP_Y[0], zi[0]], [POSTS_X[0][1], TOP_Y[1], zi[1]], "fr_endtie_w", f, "oak"))
    out.append(box([POSTS_X[2][0], TOP_Y[0], zi[0]], [POSTS_X[2][1], TOP_Y[1], zi[1]], "fr_endtie_e", f, "oak"))
    for i, (x0, x1) in enumerate(TOP_BEAMS_X, 1):
        out.append(box([x0, TOP_Y[0], zi[0]], [x1, TOP_Y[1], zi[1]], f"fr_beam{i}", f, "oak"))
    (tx0, tx1), (ty0, ty1) = WEST_TIE
    out.append(box([tx0, ty0, zi[0]], [tx1, ty1, zi[1]], "fr_westtie", f, "oak"))
    # knee braces in each side frame, from the posts up to the top rail
    for side, (z0, z1) in zs.items():
        zc = (z0 + z1) / 2
        k = 0
        for (x0, x1), dirs in zip(POSTS_X, ((1,), (-1, 1), (-1,))):
            for d in dirs:
                k += 1
                xp = x1 if d > 0 else x0
                a = [xp - d * KNEE_EMBED, TOP_Y[0] - KNEE_RUN - KNEE_EMBED, zc]
                b = [xp + d * (KNEE_RUN + KNEE_EMBED), TOP_Y[0] + KNEE_EMBED, zc]
                out.append(strut(a, b, KNEE_W, z1 - z0 - 0.6, f"fr_knee_{side}{k}", f, "oak"))
    out += build_inlet() + build_bin() + build_entry_frame()
    return out


def build_inlet():
    """The feed inlet: a short plank chute from the west face, its floor on the west tie, its lip over the
    grizzly's head (tier 2) and the trommel's feed chute (tiers 3 and 4), an iron lip bearer under it."""
    f = "frame"
    (x0, y0), (x1, y1) = INLET_FLOOR
    z0, z1 = INLET_Z
    out = [plate_xy((x0, y0), (x1, y1), z0, z1, BOARD, "fr_inlet_floor", f, "planks")]
    for side, (za, zb) in (("n", (z0 - BOARD, z0)), ("s", (z1, z1 + BOARD))):
        out.append(box([x0, y1 - BOARD - 0.4, za], [x1, INLET_TOP, zb], f"fr_inlet_side{side}", f, "planks"))
    out.append(box([x1 - 1.0, y1 - BOARD - 1.0, z0 - BOARD], [x1 + 0.2, y1 - BOARD, z1 + BOARD], "fr_inlet_lip", f, "iron"))
    return out


def build_bin():
    """The fines bin under the screen and the drum: plank sides on the sills, a V floor down to its
    middle, and a spout from its bottom out of the north side."""
    f = "frame"
    out = []
    (bx0, bx1), (bz0, bz1) = BIN_X, BIN_Z
    lx, ly = BIN_LOW
    out.append(plate_xy((bx0, BIN_RIM), (lx, ly), bz0 + BOARD, bz1 - BOARD, BOARD, "fr_bin_floorw", f, "planks"))
    out.append(plate_xy((lx, ly), (bx1, BIN_RIM), bz0 + BOARD, bz1 - BOARD, BOARD, "fr_bin_floore", f, "planks"))
    # the sides, the north one round the spout's mouth
    sx0, sx1 = SPOUT_X
    mouth_top = ly + 3.2
    out.append(box([bx0, BIN_FLOOR, bz1 - BOARD], [bx1, BIN_RIM, bz1], "fr_bin_sides", f, "planks"))
    out.append(box([bx0, BIN_FLOOR, bz0], [sx0, BIN_RIM, bz0 + BOARD], "fr_bin_siden1", f, "planks"))
    out.append(box([sx1, BIN_FLOOR, bz0], [bx1, BIN_RIM, bz0 + BOARD], "fr_bin_siden2", f, "planks"))
    out.append(box([sx0, mouth_top, bz0], [sx1, BIN_RIM, bz0 + BOARD], "fr_bin_siden3", f, "planks"))
    # an oak rim on the sides' tops
    for tag, (za, zb) in (("n", (bz0 - 0.4, bz0 + BOARD)), ("s", (bz1 - BOARD, bz1 + 0.4))):
        out.append(box([bx0, BIN_RIM, za], [bx1, BIN_RIM + 0.8, zb], f"fr_bin_rim{tag}", f, "oak"))
    # the spout: a floor from the bin's bottom out through the north side to the face, and its sides
    out.append(plate_zy((bz0 + BOARD + 0.4, ly), (0.0, SPOUT_FACE_Y), sx0 + BOARD, sx1 - BOARD, BOARD, "fr_bin_spoutfloor", f, "planks"))
    for tag, (xa, xb) in (("w", (sx0, sx0 + BOARD)), ("e", (sx1 - BOARD, sx1))):
        out.append(box([xa, SPOUT_FACE_Y - BOARD - 0.2, 0.0], [xb, mouth_top, bz0], f"fr_bin_spoutside{tag}", f, "planks"))
    # a block under its outer end, on the ground
    out.append(box([sx0, 0.0, 0.0], [sx1, SPOUT_FACE_Y - BOARD - 0.2, 3.0], "fr_bin_spoutfoot", f, "oak"))
    return out


def build_entry_frame():
    """The entry shaft's two pillow blocks on the side girts."""
    f = "frame"
    x, y = ENTRY
    out = []
    for i, (z0, z1) in enumerate(BEARINGS_Z, 1):
        # a pillow block round the shaft (it runs through it), on a foot on the girt
        out.append(box([x - 2.0, y - BEARING_H + 0.5, z0], [x + 2.0, y + BEARING_H - 0.5, z1], f"fr_bearing{i}_block", f, "iron"))
        out.append(box([x - 2.6, GIRT_Y[1], z0 - 0.2], [x + 2.6, y - BEARING_H + 0.5, z1 + 0.2], f"fr_bearing{i}_foot", f, "iron"))
    return out


def build_entry():
    """The entry shaft: the vanilla axle's cross profile at the power face, then a round steel shaft
    across the machine in the two pillow blocks."""
    x, y = ENTRY
    out = [box([x - 1.5, y - 0.6, ENTRY_Z[0]], [x + 1.5, y + 0.6, OAK_Z], "entry_shafta", "entry", "oak"),
           box([x - 0.6, y - 1.5, ENTRY_Z[0]], [x + 0.6, y + 1.5, OAK_Z], "entry_shaftb", "entry", "oak")]
    out += disc("z", (x, y, 0.0), OAK_Z, ENTRY_Z[1], SHAFT_R, "entry_shaft", "entry", "steel")
    return out


# ---------------------------------------------------------------- tier 2
def build_grizzly():
    """The grizzly: five heavy iron bars at 40 degrees, their heads hooked under the inlet's lip, their
    feet on an iron bearer hung by two straps from the first top cross beam."""
    g = "grizzly"
    out = []
    z_mid = [ZD + (i - (GRIZ_BARS - 1) / 2) * (GRIZ_BAR_W + GRIZ_GAP) for i in range(GRIZ_BARS)]
    for i, zc in enumerate(z_mid, 1):
        a, b = griz_point(-0.2, GRIZ_DEPTH / 2), griz_point(GRIZ_LEN, GRIZ_DEPTH / 2)
        out.append(strut([a[0], a[1], zc], [b[0], b[1], zc], GRIZ_DEPTH, GRIZ_BAR_W, f"grizzly_bar{i}", g, "iron"))
    zlo, zhi = z_mid[0] - GRIZ_BAR_W / 2 - 0.5, z_mid[-1] + GRIZ_BAR_W / 2 + 0.5
    fx, fy = griz_point(GRIZ_LEN - 1.2, GRIZ_DEPTH)
    out.append(box([fx - 0.9, fy - 1.1, zlo], [fx + 0.9, fy + 0.15, zhi], "grizzly_bearer", g, "iron"))
    for tag, (za, zb) in (("n", (zlo - 0.6, zlo)), ("s", (zhi, zhi + 0.6))):
        out.append(box([GRIZ_STRAPS_X[0], fy - 1.1, za], [GRIZ_STRAPS_X[1], TOP_Y[0], zb], f"grizzly_strap{tag}", g, "iron"))
    return out


def hanger_pins():
    """Each hanger's (top pin, bottom pin) at rest, (x, y): the bottom on the screen's side board."""
    out = []
    for x in HANGERS_X:
        bottom = (x, deck_y(x) + 0.3)
        out.append(((x, bottom[1] + HANGER_H), bottom))
    return out


def build_screen():
    """The shaking screen: a deck of wire cloth between plank side boards, a blank feed plate at its head
    and a blank tail plate, iron cross bars under it; hung on four iron hangers from brackets under the
    top cross beams; the rods' pins at its tail. Its oversize spout to the oversize outlet is `screen` too."""
    s = "screen"
    out = []
    z0, z1 = SCREEN_IN
    hx, tx = SCREEN_HEAD[0], SCREEN_TAIL[0]
    out.append(plate_xy((hx + BOARD, deck_y(hx + BOARD)), (MESH_X[0], deck_y(MESH_X[0])), z0, z1, DECK_T, "screen_feedplate", s, "sheet"))
    out.append(plate_xy((MESH_X[0], deck_y(MESH_X[0])), (MESH_X[1], deck_y(MESH_X[1])), z0, z1, DECK_T, "screen_deck", s, "mesh"))
    out.append(plate_xy((MESH_X[1], deck_y(MESH_X[1])), (tx, deck_y(tx)), z0, z1, DECK_T, "screen_tailplate", s, "sheet"))
    for tag, (za, zb) in (("n", (z0 - SIDE_W, z0)), ("s", (z1, z1 + SIDE_W))):
        a = (hx, deck_y(hx) + SIDE_UP)
        b = (tx, deck_y(tx) + SIDE_UP)
        out.append(plate_xy(a, b, za, zb, SIDE_UP + SIDE_DOWN, f"screen_side{tag}", s, "planks"))
    out.append(box([hx, deck_y(hx) - SIDE_DOWN, z0], [hx + BOARD, deck_y(hx) + SIDE_UP + 0.4, z1], "screen_headboard", s, "planks"))
    for i, x in enumerate((MESH_X[0] + 0.5, (MESH_X[0] + MESH_X[1]) / 2, MESH_X[1] - 0.5), 1):
        y = deck_y(x) - DECK_T
        out.append(box([x - 0.35, y - 0.8, z0], [x + 0.35, y, z1], f"screen_bar{i}", s, "iron"))
    # the pins: the hangers' (along z, through the side boards) and the rods' at the tail
    for i, (_, (bx, by)) in enumerate(hanger_pins(), 1):
        out += disc("z", (bx, by, 0.0), HANGER_Z[0] - 0.3, z0, 0.32, f"screen_pinn{i}", s, "iron", k=2)
        out += disc("z", (bx, by, 0.0), z1, HANGER_Z[1] + HANGER_T + 0.3, 0.32, f"screen_pins{i}", s, "iron", k=2)
    px, py = ROD_PIN
    out.append(box([px - 1.0, py - 0.9, z0 - SIDE_W - 0.05], [px + 1.0, deck_y(px) - 0.4, z0 - SIDE_W], "screen_lugn_pad", s, "iron"))
    out += disc("z", (px, py, 0.0), ROD_Z[0][0] - 0.3, z0 - SIDE_W + 0.4, 0.4, "screen_rodpinn", s, "iron", k=2)
    out += disc("z", (px, py, 0.0), z1 + SIDE_W - 0.4, ROD_Z[1][1] + 0.3, 0.4, "screen_rodpins", s, "iron", k=2)
    return out


def build_hangers():
    """Four iron hangers, each from a pin in a bracket under a top cross beam to a pin on the screen's side."""
    out = []
    for i, ((tx, ty), (bx, by)) in enumerate(hanger_pins(), 1):
        for side, za in (("n", HANGER_Z[0]), ("s", HANGER_Z[1])):
            k = 2 * i - (1 if side == "n" else 0)
            part = f"hanger{k}"
            out.append(box([tx - HANGER_W / 2, by - 0.6, za], [tx + HANGER_W / 2, ty + 0.6, za + HANGER_T], f"{part}_bar", part, "iron"))
    return out


def build_hanger_brackets():
    """The hangers' brackets and top pins, under the top cross beams (the screen's, fixed)."""
    out = []
    for i, ((tx, ty), _) in enumerate(hanger_pins(), 1):
        for side, za in (("n", HANGER_Z[0]), ("s", HANGER_Z[1])):
            zb0, zb1 = (za - 0.7, za) if side == "n" else (za + HANGER_T, za + HANGER_T + 0.7)
            out.append(box([tx - 0.8, ty - 0.8, zb0], [tx + 0.8, TOP_Y[0], zb1], f"brackets_{side}{i}", "brackets", "iron"))
            out += disc("z", (tx, ty, 0.0), min(zb0, za) - 0.15, max(zb1, za + HANGER_T) + 0.15, 0.3, f"brackets_pin{side}{i}", "brackets", "iron", k=2)
    return out


def ecc_centre(theta=0.0):
    a = theta + ECC_PHASE
    return (ENTRY[0] + ECC * math.cos(a), ENTRY[1] + ECC * math.sin(a))


ROD_LEN = math.dist(ROD_PIN, ecc_centre())


def build_eccentrics():
    """An eccentric sheave on the entry shaft at each side, and its rod: a strap round the sheave and a
    bar to the small end's eye on the screen's pin."""
    out = []
    ex, ey = ecc_centre()
    px, py = ROD_PIN
    for tag, (z0, z1) in zip(("n", "s"), ROD_Z):
        e, r = f"ecc{tag}", f"rod{tag}"
        out += disc("z", (ex, ey, 0.0), z0 - 0.1, z1 + 0.1, ECC_R, f"{e}_sheave", e, "iron", k=4)
        # the strap: eight segments round the sheave, and the rod from it to the small end
        n = 8
        w = 2 * (ECC_R + STRAP) * math.tan(math.pi / n)
        for k in range(n):
            out.append(radial("z", (ex, ey, 0.0), z0, z1, ECC_R, ECC_R + STRAP, w, TAU * k / n + math.pi / n, f"{r}_strap{k + 1}", r, "iron"))
        d = unit([ex - px, ey - py, 0.0])
        a = [px + d[0] * 0.8, py + d[1] * 0.8, 0.0]
        b = [ex - d[0] * (ECC_R + STRAP - 0.2), ey - d[1] * (ECC_R + STRAP - 0.2), 0.0]
        a[2] = b[2] = (z0 + z1) / 2
        out.append(strut(a, b, ROD_W, z1 - z0 - 0.1, f"{r}_bar", r, "iron"))
        out += eye("z", (px, py, 0.0), z0 + 0.05, z1 - 0.05, 0.42, 0.95, f"{r}_eye", r, "iron")
    return out


# ---------------------------------------------------------------- tier 2's spout and tier 3's: to the oversize outlet
def spout(prefix, part, x0, y0):
    """A plank spout from a back board at x0 (its floor's top at y0 there) straight down east to the oversize
    outlet on the east face (its floor's top at OUTLET_Y there), narrowing to the outlet; a leg under its back end."""
    out = []
    x1 = CELLS_X * B
    y1 = OUTLET_Y
    slope = (y0 - y1) / (x1 - x0)
    zs0, zs1 = SPOUT_Z
    zo0, zo1 = OUTLET_Z
    xm = x0 + 7.0                            # it narrows from here on
    ym = y0 - (xm - x0) * slope
    out.append(plate_xy((x0, y0), (xm, ym), zs0, zs1, BOARD, f"{prefix}_floor1", part, "planks"))
    out.append(plate_xy((xm, ym), (x1, y1), zo0, zo1, BOARD, f"{prefix}_floor2", part, "planks"))
    for tag, (za, zb), (xa, xb, ya, yb) in (
            ("n1", (zs0 - BOARD, zs0), (x0, xm, y0, ym)), ("s1", (zs1, zs1 + BOARD), (x0, xm, y0, ym)),
            ("n2", (zo0 - BOARD, zo0), (xm, x1, ym, y1)), ("s2", (zo1, zo1 + BOARD), (xm, x1, ym, y1))):
        # a side board: from under its floor up SPOUT_SIDE above it, along the floor
        out.append(plate_xy((xa, ya + SPOUT_SIDE), (xb, yb + SPOUT_SIDE), za, zb, SPOUT_SIDE + BOARD, f"{prefix}_side{tag}", part, "planks"))
    # where it narrows, a board across the step on each side
    out.append(box([xm - BOARD, ym - BOARD, zo1 + BOARD], [xm, ym + SPOUT_SIDE, zs1 + BOARD], f"{prefix}_step", part, "planks"))
    out.append(box([x0 - BOARD, y0 - BOARD, zs0 - BOARD], [x0, y0 + SPOUT_SIDE, zs1 + BOARD], f"{prefix}_back", part, "planks"))
    out.append(box([x0 + 0.4, 0.0, ZD - 1.0], [x0 + 1.6, y0 - BOARD - 1.6 * slope, ZD + 1.0], f"{prefix}_leg", part, "oak"))
    return out


def build_tailspout():
    return spout("tailspout", "tailspout", TAIL_SPOUT_X0, TAIL_SPOUT_Y0)


def build_discharge():
    return spout("discharge", "discharge", DISCHARGE_X0, DISCHARGE_Y0)


def build_return():
    """Tier 4's return chute: under the drum's lip, a plank chute down south to the return outlet on the
    south face, in place of the discharge spout; a leg under its back end."""
    p = "return"
    x0, x1 = RETURN_X
    zf = CELLS_Z * B
    out = [plate_zy((RETURN_Z0, RETURN_Y0), (zf, RETURN_FACE_Y), x0, x1, BOARD, "return_floor", p, "planks")]
    for tag, (xa, xb) in (("w", (x0 - BOARD, x0)), ("e", (x1, x1 + BOARD))):
        out.append(plate_zy((RETURN_Z0, RETURN_Y0 + RETURN_SIDE), (zf, RETURN_FACE_Y + RETURN_SIDE), xa, xb, RETURN_SIDE + BOARD, f"return_side{tag}", p, "planks"))
    out.append(box([x0 - BOARD, RETURN_Y0 - BOARD, RETURN_Z0 - BOARD], [x1 + BOARD, RETURN_Y0 + RETURN_SIDE, RETURN_Z0], "return_back", p, "planks"))
    slope = (RETURN_Y0 - RETURN_FACE_Y) / (zf - RETURN_Z0)
    zl = RETURN_Z0 + 1.2
    out.append(box([(x0 + x1) / 2 - 1.0, 0.0, zl - 0.6], [(x0 + x1) / 2 + 1.0, RETURN_Y0 - BOARD - 1.2 * slope, zl + 0.6], "return_leg", p, "oak"))
    return out


# ---------------------------------------------------------------- tier 3: the trommel
def build_drum():
    """The trommel, built level with its axis along x through the apex, then tilted: the jacket of wire
    cloth in two lengths and a blank band at the discharge, a feed ring with the feed opening, a middle
    ring, a lip ring, two spiders (hub and six arms) on the shaft."""
    d = "drum"
    c = (0.0, APEX[1], APEX[2])
    out = []
    x0, x1 = DRUM_X
    for i, (a0, a1) in enumerate(JACKET_SPANS, 1):
        out += annulus("x", c, a0, a1, DRUM_R - JACKET_T / 2, DRUM_R + JACKET_T / 2, 16, f"drum_jacket{i}_", d, "mesh")
    out += annulus("x", c, JACKET_SPANS[1][1], x1 - RING_T, DRUM_R - JACKET_T / 2, DRUM_R + JACKET_T / 2, 16, "drum_band", d, "sheet")
    out += annulus("x", c, x0, x0 + RING_T, FEED_RING_IN, RING_OUT, 16, "drum_feedring", d, "iron", phase=math.pi / 16)
    out += annulus("x", c, *MID_RING_X, MID_RING_IN, RING_OUT, 16, "drum_midring", d, "iron", phase=math.pi / 16)
    out += annulus("x", c, x1 - RING_T, x1, LIP_RING_IN, RING_OUT, 16, "drum_lipring", d, "iron", phase=math.pi / 16)
    for i, (a0, a1) in enumerate(SPIDERS_X, 1):
        out += disc("x", c, a0 - 0.3, a1 + 0.3, 1.8, f"drum_spider{i}_hub", d, "iron", k=4)
        for k in range(SPIDER_ARMS):
            out.append(radial("x", c, a0, a1, 1.4, DRUM_R - JACKET_T / 2 + 0.02, 0.9, TAU * k / SPIDER_ARMS + 0.3 * i, f"drum_spider{i}_arm{k + 1}", d, "iron"))
    out += disc("x", c, DRUM_SHAFT_X[0], DRUM_SHAFT_X[1], DRUM_SHAFT_R, "drum_shaft", d, "steel")
    return tilt(out)


def bevel_tooth(centre_axis, axis_dir, pitch_pt, name, part):
    """A box tooth on a bevel gear: along its generator from just outside the pitch circle towards the
    apex, as deep as the module twice, centred on the pitch cone."""
    u = unit([APEX[k] - pitch_pt[k] for k in range(3)])
    # the pitch cone's normal at the tooth, in the plane of the gear's axis and the generator, pointing
    # away from the gear's own axis side (towards the mating gear)
    rad = [pitch_pt[k] - centre_axis[k] for k in range(3)]
    along = sum(rad[k] * axis_dir[k] for k in range(3))
    rad = unit([rad[k] - along * axis_dir[k] for k in range(3)])
    n = [rad[k] - sum(rad[j] * u[j] for j in range(3)) * u[k] for k in range(3)]
    n = unit(n)
    c = [pitch_pt[k] + u[k] * (FACE / 2 - 0.25) for k in range(3)]
    return obox(c, u, n, [FACE + 0.25, 2 * MODULE, math.pi * MODULE / 2], name, part, "steel")


def build_bevel():
    """The bevel pair: the pinion (8) on the entry shaft, the wheel (24) on the drum's shaft, built level
    and meshed (a wheel's tooth on the pitch point, the pinion's gap there), then turned with the drum."""
    out = []
    ax, ay, az = APEX
    # the wheel: its pitch circle R_WHEEL about the drum's axis, R_PINION short of the apex
    wx = ax - R_PINION
    wc = (wx, ay, az)
    phi_q = -math.pi / 2                       # the pitch point Q is straight north of the wheel's centre: (wx, ay, az - R_WHEEL)
    for i in range(WHEEL_TEETH):
        ph = phi_q + TAU * i / WHEEL_TEETH
        p = (wx, ay + R_WHEEL * math.cos(ph), az + R_WHEEL * math.sin(ph))
        out.append(bevel_tooth(wc, (1.0, 0.0, 0.0), p, f"wheel_tooth{i + 1}", "wheel"))
    out += disc("x", (0.0, ay, az), wx - 1.25, wx - 0.35, R_WHEEL - 0.55, "wheel_body", "wheel", "steel", k=6)
    out += disc("x", (0.0, ay, az), wx - 1.75, wx + 0.1, 1.7, "wheel_hub", "wheel", "steel", k=4)
    # the pinion: its pitch circle R_PINION about the entry shaft, R_WHEEL short of the apex; a gap on Q
    pz = az - R_WHEEL
    pc = (ax, ay, pz)
    psi_q = math.pi                            # Q is west of the pinion's centre
    for i in range(PINION_TEETH):
        ps = psi_q + math.pi / PINION_TEETH + TAU * i / PINION_TEETH
        p = (ax + R_PINION * math.cos(ps), ay + R_PINION * math.sin(ps), pz)
        out.append(bevel_tooth(pc, (0.0, 0.0, 1.0), p, f"pinion_tooth{i + 1}", "pinion"))
    out += disc("z", (ax, ay, 0.0), pz - 0.9, pz + 0.05, R_PINION - 0.45, "pinion_body", "pinion", "steel", k=4)
    out += disc("z", (ax, ay, 0.0), pz - 1.7, pz + 0.05, SHAFT_R + 0.55, "pinion_hub", "pinion", "steel", k=4)
    return tilt(out)


def build_mount():
    """What carries the trommel, fixed: the west pillow block on its pedestal on a cross timber across
    the girts, the east one hung from the third top cross beam, and the feed chute from under the inlet's
    lip into the drum's feed opening."""
    m = "mount"
    out = []
    # the bearings, built level round the shaft and tilted with it; their supports plumb
    c = (0.0, APEX[1], APEX[2])
    bearings = []
    for tag, (a0, a1) in (("w", WEST_BEARING_X), ("e", EAST_BEARING_X)):
        h = BEARING_H - 0.5
        bearings.append(box([a0, c[1] - h, c[2] - h], [a1, c[1] + h, c[2] + h], f"mount_bearing{tag}", m, "iron"))
    out += tilt(bearings)
    wx = sum(WEST_BEARING_X) / 2
    wax = drum_axis_point(wx)
    tx0, tx1 = wax[0] - 1.9, wax[0] + 1.9
    out.append(box([tx0, GIRT_Y[1], 0.0], [tx1, GIRT_Y[1] + 3.5, CELLS_Z * B], "mount_timberw", m, "oak"))
    out.append(box([wax[0] - 1.1, GIRT_Y[1] + 3.5, ZD - 2.0], [wax[0] + 1.1, wax[1] - BEARING_H + 0.6, ZD + 2.0], "mount_pedestalw", m, "iron"))
    ex = sum(EAST_BEARING_X) / 2
    eax = drum_axis_point(ex)
    out.append(box([eax[0] - 0.8, eax[1] + BEARING_H - 0.6, ZD - 1.5], [eax[0] + 0.8, TOP_Y[0], ZD + 1.5], "mount_hangere", m, "iron"))
    return out + build_feed_chute()


def feed_frame():
    """The feed chute's plane: its head plate's north-west corner Q (just under the inlet's lip), e1 down the
    chute, n the plate's upward normal, e2 across it towards the south (in the plane), the head plate's
    length (along e1) and width (along e2)."""
    q = list(FEED_HEAD)
    e1 = unit([1.0, -FEED_SLOPE, 0.0])
    across = [0.0, -math.tan(FEED_WING_TILT), 1.0]
    n = unit(cross(e1, across))
    if n[1] < 0:
        n = [-c for c in n]
    e2 = unit(cross(n, e1))
    if e2[2] < 0:
        e2 = [-c for c in e2]
    length = (FEED_WING_X - q[0]) / e1[0]
    width = (FEED_CHUTE_Z[1] - q[2]) / e2[2]
    return q, e1, n, e2, length, width


def feed_valley():
    """The throat's line (x, y) along the chute's south edge: from the head plate's south-west corner, its
    south-east corner (where the throat starts) and the lip (FEED_LIP_X)."""
    q, e1, _, e2, length, width = feed_frame()
    s0 = [q[k] + e2[k] * width for k in range(3)]
    s1 = [s0[k] + e1[k] * length for k in range(3)]
    lip = (FEED_LIP_X, s0[1] - FEED_SLOPE * (FEED_LIP_X - s0[0]))
    return (s0[0], s0[1]), (s1[0], s1[1]), lip


def build_feed_chute():
    """The trommel's feed chute, sheet iron: a straight throat into the drum's feed opening beside the shaft
    (south of it), so the feed falls past the shaft onto the drum's bottom; at its head, under the inlet's
    whole width, a head plate falling both down the chute and, gently, south into the throat (a funnel),
    with a board along its north edge and one across its east end down to the throat."""
    m = "mount"
    out = []
    z0, z1 = FEED_CHUTE_Z
    q, e1, n, e2, length, width = feed_frame()
    s0, s1, lip = feed_valley()
    out.append(plate_xy(s1, lip, z0, z1, BOARD, "mount_feedfloor", m, "sheet"))
    for tag, (za, zb), a in (("n", (z0 - BOARD, z0), s1), ("s", (z1, z1 + BOARD), s0)):
        out.append(plate_xy((a[0], a[1] + FEED_CHUTE_SIDE), (lip[0], lip[1] + FEED_CHUTE_SIDE), za, zb, FEED_CHUTE_SIDE + BOARD,
                            f"mount_feedside{tag}", m, "sheet"))
    c = [q[k] + e1[k] * length / 2 + e2[k] * width / 2 - n[k] * BOARD / 2 for k in range(3)]
    out.append(obox(c, e1, e2, [length, width, BOARD], "mount_feedwing", m, "sheet"))
    # its north board, standing on its north edge
    n1 = [q[k] + e1[k] * length for k in range(3)]
    out.append(plate_xy((q[0], q[1] + FEED_CHUTE_SIDE), (n1[0], n1[1] + FEED_CHUTE_SIDE), q[2] - BOARD, q[2],
                        FEED_CHUTE_SIDE + BOARD, "mount_feedwingside", m, "sheet"))
    # the end board: along the head plate's east edge, from the north board to the throat's north side, plumb
    t1 = ((z0 - BOARD) - q[2]) / e2[2]
    a = n1
    b = [n1[k] + e2[k] * t1 for k in range(3)]
    along = unit([b[k] - a[k] for k in range(3)])
    h = unit([(1.0 if k == 1 else 0.0) - along[1] * along[k] for k in range(3)])
    thick = cross(along, h)
    if thick[0] < 0:
        thick = [-c for c in thick]
    span = math.dist(a, b)
    height = FEED_CHUTE_SIDE + 1.6
    mid = [(a[k] + b[k]) / 2 + h[k] * (height / 2 - 0.9) + thick[k] * BOARD / 2 for k in range(3)]
    out.append(obox(mid, along, h, [span, height, BOARD], "mount_feedend", m, "sheet"))
    return out


def build():
    els = build_entry() + build_grizzly() + build_screen() + build_hangers() + build_hanger_brackets() + build_eccentrics()
    els += build_tailspout() + build_drum() + build_bevel() + build_mount() + build_discharge() + build_return() + build_frame()
    return els


# ---------------------------------------------------------------- the linkage: exact by Fourier series
def screen_shift(beta):
    """The screen's displacement (x, y) when its hangers have turned beta (about +z: the foot moves +x)."""
    return (HANGER_H * math.sin(beta), HANGER_H * (1.0 - math.cos(beta)))


def solve_beta(theta, guess=0.0):
    """The hangers' angle that keeps the rod its length (Newton on the pin distance)."""
    e = ecc_centre(theta)
    b = guess
    for _ in range(60):
        dx, dy = screen_shift(b)
        p = (ROD_PIN[0] + dx, ROD_PIN[1] + dy)
        f = math.dist(e, p) - ROD_LEN
        h = 1e-7
        dx2, dy2 = screen_shift(b + h)
        df = (math.dist(e, (ROD_PIN[0] + dx2, ROD_PIN[1] + dy2)) - math.dist(e, p)) / h
        step = f / df
        b -= step
        if abs(step) < 1e-14:
            break
    return b


def rod_angle(theta, beta):
    e = ecc_centre(theta)
    dx, dy = screen_shift(beta)
    return math.atan2(e[1] - (ROD_PIN[1] + dy), e[0] - (ROD_PIN[0] + dx))


def fourier(fn, n_terms, samples=720):
    """Fit fn(theta) (periodic in a turn) with a mean and n_terms harmonics: [(k, amplitude, phase)] with
    fn ~ sum amplitude * sin(k theta + phase) (k 0 the mean: amplitude * sin(phase), phase pi/2)."""
    vals = [fn(TAU * i / samples) for i in range(samples)]
    mean = sum(vals) / samples
    out = [(0, mean, math.pi / 2)]
    for k in range(1, n_terms + 1):
        a = 2 / samples * sum(v * math.sin(k * TAU * i / samples) for i, v in enumerate(vals))
        b = 2 / samples * sum(v * math.cos(k * TAU * i / samples) for i, v in enumerate(vals))
        out.append((k, math.hypot(a, b), math.atan2(b, a)))
    return out


def series(terms, theta):
    return sum(amp * math.sin(k * theta + ph) for k, amp, ph in terms)


_SERIES = {}


def linkage_series():
    """(beta, screen x, screen y, rod angle less its rest angle) as Fourier series in theta."""
    if not _SERIES:
        betas = {}

        def beta(th):
            key = round(th, 12)
            if key not in betas:
                betas[key] = solve_beta(th)
            return betas[key]
        g0 = rod_angle(0.0, 0.0)
        _SERIES["beta"] = fourier(beta, HARMONICS)
        _SERIES["x"] = fourier(lambda th: screen_shift(beta(th))[0], HARMONICS)
        _SERIES["y"] = fourier(lambda th: screen_shift(beta(th))[1], HARMONICS)
        _SERIES["rod"] = fourier(lambda th: rod_angle(th, beta(th)) - g0, HARMONICS)
    return _SERIES


# ---------------------------------------------------------------- the rig
def pt(*v):
    return [r6(x / B) for x in v]


def swings(terms, pivot, axis="z"):
    return [{"type": "swing", "axis": axis, "pivot": pivot, "amplitude": r6(amp), "ratio": float(k), "phase": r6(ph)}
            for k, amp, ph in terms if abs(amp) > 1e-9]


def slides(terms, axis):
    return [{"type": "slide", "axis": axis, "amplitude": r6(amp / B), "ratio": float(k), "phase": r6(ph)}
            for k, amp, ph in terms if abs(amp) > 1e-9]


def const_turn(angle, pivot):
    """A fixed turn about z (a ratio-0 swing: amplitude sin(pi/2)), as the handcar writes its constant slides."""
    return {"type": "swing", "axis": "z", "pivot": pivot, "amplitude": r6(angle), "ratio": 0.0, "phase": r6(math.pi / 2)}


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    s = linkage_series()
    screen = slides(s["x"], "x") + slides(s["y"], "y")
    rod = swings(s["rod"], pt(*ROD_PIN, ZD)) + screen
    apex = pt(*APEX)
    drum = [const_turn(TILT, apex),
            {"type": "rotate", "axis": "x", "pivot": apex, "ratio": r6(DRUM_RATIO)},
            const_turn(-TILT, apex)]
    parts = [
        {"id": "entry", "match": ["entry_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "z", "pivot": pt(*ENTRY, 0.0), "ratio": 1.0}]},
        {"id": "eccn", "match": ["eccn_*"], "requires": "eccentric", "ride": "entry", "drivers": []},
        {"id": "eccs", "match": ["eccs_*"], "requires": "eccentric", "ride": "entry", "drivers": []},
        {"id": "rodn", "match": ["rodn_*"], "requires": "eccentric", "drivers": copy.deepcopy(rod)},
        {"id": "rods", "match": ["rods_*"], "requires": "eccentric", "drivers": copy.deepcopy(rod)},
        {"id": "screen", "match": ["screen_*"], "requires": "screen", "drivers": copy.deepcopy(screen)},
    ]
    for i, ((tx, ty), _) in enumerate(hanger_pins(), 1):
        for side in ("n", "s"):
            k = 2 * i - (1 if side == "n" else 0)
            parts.append({"id": f"hanger{k}", "match": [f"hanger{k}_*"], "requires": "screen",
                          "drivers": swings(s["beta"], pt(tx, ty, ZD))})
    parts += [
        {"id": "brackets", "match": ["brackets_*"], "requires": "screen", "drivers": []},
        {"id": "tailspout", "match": ["tailspout_*"], "requires": "screen", "drivers": []},
        {"id": "grizzly", "match": ["grizzly_*"], "requires": "grizzly", "drivers": []},
        {"id": "pinion", "match": ["pinion_*"], "requires": "bevel", "ride": "entry", "drivers": []},
        {"id": "drum", "match": ["drum_*"], "requires": "trommel", "drivers": drum},
        {"id": "wheel", "match": ["wheel_*"], "requires": "bevel", "ride": "drum", "drivers": []},
        {"id": "mount", "match": ["mount_*"], "requires": "trommel", "drivers": []},
        {"id": "discharge", "match": ["discharge_*"], "requires": "discharge", "drivers": []},
        {"id": "return", "match": ["return_*"], "requires": "return", "drivers": []},
        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    out = []
    for p in parts:
        for d in p["drivers"]:
            validate_driver(d)
        out.append({"id": p["id"], "match": p["match"], "requires": p["requires"], "ride": p.get("ride"), "drivers": p["drivers"]})
    return out


# ---------------------------------------------------------------- poses
REST = (0.0, "tier4")                         # (theta, the state): the authored pose


def inputs_of(pose):
    th = pose[0]
    return {"theta": th, "travel": abs(th)}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


def part_fitted(parts_by_id, pid, state):
    """Whether part `pid` is drawn in `state`: no requires, or its requires fitted there."""
    req = parts_by_id[pid]["requires"]
    return req is None or req in STATES[state]


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


def port_points():
    """Where material crosses each port (voxels): the middle of the inlet's mouth, and each outlet chute's
    floor at its face, a little above it."""
    zf = CELLS_Z * B
    x1 = CELLS_X * B
    oy = OUTLET_Y
    return {
        "feed": (0.0, (INLET_FLOOR[0][1] + INLET_TOP) / 2, ZD),
        "fines": ((SPOUT_X[0] + SPOUT_X[1]) / 2, SPOUT_FACE_Y + 1.0, 0.0),
        "oversize": (x1, oy + 1.0, sum(OUTLET_Z) / 2),
        "return": ((RETURN_X[0] + RETURN_X[1]) / 2, RETURN_FACE_Y + 1.0, zf),
    }


PORTS = {"feed": FEED, "fines": FINES, "oversize": OVERSIZE, "return": RETURN}


def make_rig(parts):
    rig = {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the feed end's north-west "
                    "bottom cell; material flows east. One input, theta, the axle's angle (the entry shaft; the eccentrics shake "
                    "the screen once a turn, the trommel turns a third of a turn a turn). requires: one value per fitted part set, "
                    "tiers lists which a tier fits (fitting a tier's set replaces the last tier's: the frame alone needs none). "
                    "The ports are fixed at every tier: feedCell and feedFace where the feed comes in, finesCell/finesFace, "
                    "oversizeCell/oversizeFace (tiers 2 and 3) and returnCell/returnFace (tier 4) where it leaves, each with a "
                    "point where material crosses the face. The cells' boxes are the finished machine's (tier 4). See the "
                    "classifier's README for the schema.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
    }
    pts = port_points()
    for key, (cell, face) in PORTS.items():
        rig[f"{key}Cell"] = list(cell)
        rig[f"{key}Face"] = face
        rig[key] = {"pos": pt(*pts[key])}
    rig["tiers"] = {k[-1]: list(v) for k, _, v in TIERS if v}
    rig["trommel"] = {"turnsPerAxleTurn": r6(-DRUM_RATIO), "slope": r6(math.tan(TILT)),
                      "_comment": "turnsPerAxleTurn: the drum's turns per axle turn, the bevel pair's 8:24; slope: its shaft's fall per "
                                  "unit length towards the discharge (1 in 12). The screen (tier 2) shakes once per axle turn."}
    rig["parts"] = parts
    return rig


# ---------------------------------------------------------------- shipped
ANCHORS = ("feed", "fines", "oversize", "return")


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
    ship["cells"] = [{**c, "pos": shift_cell(c["pos"], ORIGIN_CELL)} for c in rig["cells"]]
    ship["powerCell"] = shift_cell(rig["powerCell"], ORIGIN_CELL)
    for key in ANCHORS:
        ship[f"{key}Cell"] = shift_cell(rig[f"{key}Cell"], ORIGIN_CELL)
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig: the frame and
    the finished machine's parts (tier 4); cells with none of them are hollow. Then the lids."""
    by_id = {p["id"]: p for p in ship_parts}
    written = flatten(shape["elements"], textures={})
    by_cell = {}
    for w in written:
        pid = part_of(ship_parts, w.name)
        if not part_fitted(by_id, pid, FINISHED):
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
    poses = [REST, (1.3, "tier2"), (-2.2, "tier3"), (7.9, "tier4")]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    span = [(min(c[k] for c in cells), max(c[k] for c in cells)) for k in range(3)]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells x {span[0]}, y {span[1]}, "
          f"z {span[2]}; power {ship['powerCell']} {ship['powerFace']}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
REFERENCE_THETAS = (0.0, 0.3, 1.1, -0.7, 2.0, -2.3, 2.9, 3.6, 4.4, 5.2, 6.0, 7.5, -7.9, 12.3, 18.8)


def reference_json(ship_parts):
    poses = []
    for th in REFERENCE_THETAS:
        ins = {"theta": th, "travel": round(abs(th), 6)}
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], ins)) for q in ship_parts}
        poses.append({"theta": th, "travel": ins["travel"], "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped classifier-rig.json's parts: each part's matrix as 3 rows of 4 "
                        "(block units) at each pose (theta, the axle's angle; travel, which nothing reads). The site's and the "
                        "Python tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Every tier's parts are in this "
             "one shape (tier 2's grizzly and screen and tier 3's trommel stand in the same space): the rig's requires and "
             "tiers say which are fitted. Keep element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return ((0.0, "tier4"), (0.0, "tier2"), (1.9, "tier2"), (4.1, "tier2"), (2.6, "tier3"), (0.0, "frame"))


def shown(posed_els, pose, by_id):
    """The posed elements as they can be seen in the pose's state: parts not fitted there moved far away
    (copies; the order kept), so the z-fighting fix and its check deal only with faces that show together."""
    out = []
    for i, e in enumerate(posed_els):
        if not part_fitted(by_id, e.part, pose[1]):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * i, e.c[2]]
        out.append(e)
    return out


def fix_coplanar(els, parts):
    by_id = {p["id"]: p for p in parts}
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose, by_id), coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the classifier's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_classifier
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
    ok = validate_classifier.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "classifier.json", args.out / "classifier_frame.json", args.out / "classifier-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "classifier.json", SHAPE_DIR / "classifier_frame.json", RIG_DIR / "classifier-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_classifier.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
