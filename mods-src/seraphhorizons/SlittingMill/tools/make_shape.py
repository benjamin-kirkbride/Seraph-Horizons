#!/usr/bin/env python3
"""Generate the slitting mill's shapes, rig and reference poses.

The slitting mill is the 17th to 19th century nail-rod mill, a slitter stand, with a powered cut-off shear
at its exit. A metal plate (the game's `metalplate-{metal}`, 8 x 8, 1 thick) is offered on the feed table
to the slitters: two parallel arbors in cast-iron housings, each carrying a stack of steel collars, cutter
discs alternating with smaller spacers, the cutters of the top arbor running in the gaps of the bottom one.
The arbors are geared together by a pair of equal spur gears (the pinions) and turn the opposite ways, so
the plate is drawn in and comes out cut lengthwise into eight square rods (nail rods), in two layers, as a
slitter leaves them. Behind the stand a guillotine knife, hung in slots in the housings' back posts and
worked by a scotch yoke from a crank, comes down once a stroke just past a fixed bed knife: it shears a
nail's length off the rods' ends, pressing each nail down with its edge until it parts, and the nails fall
into the nail box. When the plate's tail leaves the slitters nothing pushes the rods on, so the last length
of them, from the slitters to the knife, is left whole: the strips. A plate makes nails and strips (the
game's `metalnailsandstrips-{metal}`).

The feed is intermittent, as a nail cutter's was: the crank's shaft also carries a mutilated gear, three
teeth on the pitch of a twenty-six, which turns the great wheel on the top arbor by five of its fifty teeth once a stroke
while the knife is up, and leaves it standing while the knife is down. So the rolls and the plate dwell
while the blade is in the rods, and a nail parts at the instant the blade's edge has gone through it.

The power: a vanilla axle at the near end's upper east face turns the entry shaft; through a rectifier (two
loose wheels with one-way catches, one driven directly and one through an idler) it turns the rectified
shaft forward whichever way it turns, and that shaft's gear turns a gear loose on the crankshaft, which
carries the cup of a cone clutch. The cone, feathered on the crankshaft, is thrown into the cup by its fork
while a plate is on: its smooth faces are where the axle's half meets the half the work drives. Everything
is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    slittingmill.json         the whole machine, every moving part   (assets/.../shapes/block/)
    slittingmill_frame.json   the static frame only (block and item)  (assets/.../shapes/block/)
    slittingmill-rig.json     cells, anchors and the part rig         (assets/.../config/)
    rig-reference.json        every part's matrix at a grid of poses  (tests/SlittingMill/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_slittingmill.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
machine box's north-west-bottom corner (the "build frame"); the controller cell is the build frame's
[0,0,0], so the shipped files are the build frame divided by 16. The plate travels south (+z): the feed
table at the north end (the controller's, nearest the player), the stand, the knife and the nail box south.

The rig's inputs, as this machine uses them (README "Rig schema"):

    theta  the axle angle: the entry shaft, its two rectifier pinions, the rectifier's loose wheels and idler
    psi    the axle's travel either way: the rectified shaft and the clutch's gear and cup
    W      the rig's work, knife strokes since the plate went in: the crankshaft, the cone, the crank and the
           knife turn and move with it (a turn a stroke, the knife at the bottom at every whole W); the great
           wheel, the arbors and the pinions step once a stroke (a gauge per step) and stand between; the plate
           feeds a nail's length a step, the rods grow out of the slitters, the knife parts a nail off every rod
           in the strokes ending at W 3 and 4, and the nails and the strips are taken off at 5
    k      the work: 0 none, 1 a plate (the metal is a texture, set by the renderer: code `metal`)
    p      its presence, 0..1: the clutch is in while it is
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
from machinegen.rigmath import part_of, posed, progress_of, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "SlittingMill" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/SlittingMill/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 2, 2, 2          # two wide, two high, two long (z, the plate's way)
ORIGIN_CELL = (0, 0, 0)                      # the controller: the feed table's west half, nearest the player
POWER_CELL, POWER_FACE = (1, 1, 0), "east"   # the axle comes in along x at (y 24, z 8): the near upper east cell's centre

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "iron": "game:block/metal/plate/iron",
    "steel": "game:block/metal/sheet-plain/steel1",
    "brass": "game:block/metal/sheet/brass1",
    "cupronickel": "game:block/metal/sheet/cupronickel1",  # the cone clutch, as the draw bench's
    "metal": "game:block/metal/ingot/iron",  # the work (plate, rods, nails): the renderer sets it to the plate's metal
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the work and its cycle
XC = 16.0                                    # the machine's centre line (x)
PLATE_W, PLATE_L, PLATE_T = 8.0, 8.0, 1.0    # the plate as the squaring shear lays it: 8 x 8, 1 thick
RODS = 8                                     # slit into eight square rods, 1 x 1
Y_M = 10.0                                   # the plate's middle as it is fed: the pass line
Z_N = 20.0                                   # the nip: the plane of the arbors' axes
L_NAIL = 2.0                                 # the plate feeds this far a step (one a stroke): a nail's length
D_KNIFE = 4.0                                # the cut (the bed knife's back face) is this far past the nip: the strips' length
Z_K = Z_N + D_KNIFE
STEPS = 5                                    # feed steps a job, one a stroke, centred half a stroke after each cut
M_T, SECTOR = 26, 3                          # the mutilated gear: three teeth on the pitch of a twenty-six
G_T, ADVANCE = 50, 5                         # the great wheel: five of its fifty teeth a step
FEED_MOD = 0.3
HALF_STEP = ADVANCE / (2.0 * M_T)            # a step lasts while any of the sector's teeth is in the wheel's teeth: five pitches
STEP_AT = tuple(m + 0.5 for m in range(STEPS))   # the steps' middles (W); the knife is at the top there
PLATE_STEPS, ROD_STEPS = 4, 2                # the plate is fed in four steps; the rods reach the cut in two
NAIL_STEP = {1: 2, 2: 3}                     # nail j comes past the cut in step NAIL_STEP[j] ...
CUTS = (3.0, 4.0)                            # ... and is parted in the knife's stroke down to W 3 or 4
END = float(STEPS)                           # the job's end: whole strokes, so the crank and the rolls look as at 0
TAKE_OFF = 0.04                              # the nails and the strips are taken off the box over this much W before the end
PUSH = (4.33, 4.67)                          # the push bar pushes the strips out past the cut, the knife clear above them
STRIP_FALL = (PUSH[1], 4.89)                 # past the bed knife, the strips fall into the box
PUSH_BACK = (4.69, 4.95)                     # the push bar is drawn back to the table's north end
PUSH_REST = Z_N - PLATE_L                    # the push bar's fingers' front at rest: against the plate's tail as laid
FINGER_L = 7.5                               # its fingers, long enough to reach the cut from behind the slitters' reach
FINGER_IN = 0.05                             # a finger is this much smaller each side than the rod it pushes
HEAD_T = 1.0
STRIP_REST = 2.8                             # the strips land on the second nails and the box's shelf
TURNS_PER_STROKE = 1                         # the drawn pace: the cone and the cup turn together at it
STEP_ANGLE = TAU * ADVANCE / G_T             # the great wheel and the arbors turn this far a step (36 degrees)
R_CUT = L_NAIL / STEP_ANGLE                  # 3.1831: the cutters' rim, carrying the plate a nail's length a step
ARB_TURN = STEPS * STEP_ANGLE                # half a turn a job: the collars, the pinions and the wheel look the same after it
EPS_ROD = 0.01                               # the rods are authored this long at the nip, and grow from it
EPS_NAIL = 0.006                             # the nail pieces this long, inside the rods' first voxel, until they are fed
NAIL_IN = 0.01                               # a nail piece is this much narrower each side than its rod
CUT_SEGS = 6                                 # a nail follows the knife's edge down in this many straight pieces
FALL = 0.3                                   # a parted nail falls into the box over this much W
X_ROD0 = XC - PLATE_W / 2                    # 12: rod i is x 12 + i .. 13 + i

# ---------------------------------------------------------------- the slitters (rolls)
PIN_T, PIN_MOD = 16, 0.375                   # the pinions: a pair of equal spur gears, one on each arbor's west neck
R_PIN = PIN_T * PIN_MOD / 2                  # 3.0
ARB_D = 2 * R_PIN                            # the arbors' centre distance
Y_B, Y_T = Y_M - ARB_D / 2, Y_M + ARB_D / 2  # 7 and 13: the bottom and top arbors
DELTA = 2 * R_CUT - ARB_D                    # 0.37: how far the cutters overlap
R_SPACER = 1.6                               # the spacers between the cutters
R_ARBOR = 1.2                                # the arbors' necks
COLLARS = 10                                 # collar positions x 11 .. 21; rod i runs in position i + 1
COLLAR_X0 = X_ROD0 - 1.0                     # 11
END_TRIM = 0.1                               # the end collars stop this short of the housings' faces
CUT_K, SPACER_K = 6, 4                       # the cutters 12-gons, the spacers octagons
LOW_TOP = Y_T - R_CUT                        # the lower layer's top (rods under the top cutters): 9.817
UP_BOT = Y_B + R_CUT                         # the upper layer's bottom (rods over the bottom cutters): 10.183

# ---------------------------------------------------------------- the stand
SILLS = ((8.0, 11.2), (20.8, 24.0))          # the oak sills (x), y 0..SILL_H, under the housings and the table's posts
SILL_H = 2.5
SILL_Z = (1.0, 31.0)
HOUSING_X = (8.5, 11.0)                      # the west housing; the east is its mirror
H_BASE_Y = (SILL_H, 5.0)
H_Z = (14.5, 25.5)                           # the housing's length: front post, window, back post
POST_F_Z = (14.5, 16.5)
POST_B_Z = (23.5, 25.5)
CAP_Y = (17.6, 19.6)
SLOT_X = 0.6                                 # the knife's slot in the back post's inner face, this deep
SLOT_Z = (23.97, 24.56)
CHOCK_X = (8.6, 10.9)
CHOCK_Z = (16.6, 23.4)
CHOCK_H = 1.8                                # each chock 3.6 tall round its neck
SCREW_R = 0.6
SCREW_TOP = 21.4

# ---------------------------------------------------------------- the drive: entry, rectifier, clutch, crankshaft
RECT_MOD = 0.4
A1_T, W1_T = 16, 16                          # A1 on the entry meshes W1 on the rectified shaft directly: 1:1
A2_T, I_T, W2_T = 12, 8, 12                  # A2 on the entry, the idler, W2 on the rectified shaft: 1:1, the other way
SK_T = 25                                    # the rectified shaft's gear and the clutch gear on the crankshaft: 1:1


def pr(n, m):
    return n * m / 2


E = (24.0, 8.0)                              # (y, z): the entry shaft, along x from the east face
K_Z = Z_K                                    # the crankshaft over the knife: its crank works the knife, its mutilated gear the great wheel
K = (Y_T + math.sqrt((pr(M_T, FEED_MOD) + pr(G_T, FEED_MOD)) ** 2 - (K_Z - Z_N) ** 2), K_Z)


def _circles(c1, r1, c2, r2):
    d = math.hypot(c2[0] - c1[0], c2[1] - c1[1])
    a = (r1 * r1 - r2 * r2 + d * d) / (2 * d)
    h = math.sqrt(r1 * r1 - a * a)
    u = ((c2[0] - c1[0]) / d, (c2[1] - c1[1]) / d)
    return [(c1[0] + a * u[0] + s * h * (-u[1]), c1[1] + a * u[1] + s * h * u[0]) for s in (1.0, -1.0)]


# the rectified shaft: the rectifier's centres from the entry, the clutch gears' from the crankshaft; the lower
R = min(_circles(E, pr(A1_T, RECT_MOD) + pr(W1_T, RECT_MOD), K, 2 * pr(SK_T, RECT_MOD)), key=lambda q: q[0])
# the idler: A2 + I from the entry, I + W2 from the rectified shaft; the lower
IDL = min(_circles(E, pr(A2_T, RECT_MOD) + pr(I_T, RECT_MOD), R, pr(I_T, RECT_MOD) + pr(W2_T, RECT_MOD)), key=lambda q: q[0])

G_X = (24.0, 25.6)                           # the great wheel and the mutilated gear
S_X = (25.8, 26.8)                           # the rectified shaft's gear and the clutch gear
CUP_X = (26.85, 27.45)                       # the cup, on the clutch gear's east face, opening east
CONE_X = (27.6, 28.4)                        # the cone (out); thrown in, it slides CONE_IN west into the cup
CONE_IN = 0.5
CONE_R = 1.55
CUP_R = (1.65, 2.25)
NECK_X = (28.4, 28.95)                       # the cone's neck, between it and its flange: the fork rides here
FLANGE_X = (28.95, 29.15)
X1 = (28.0, 28.7)                            # A1 and W1
X2 = (28.9, 29.6)                            # A2, the idler and W2
STD_X = (30.0, 31.0)                         # the outer standard's bearings
PED_X = (21.2, 23.3)                         # the pedestals on the east housing's cap
EPOST_X = (27.2, 27.85)                      # the entry shaft's inner bearing, on a post
ENTRY_R, RECT_R, CRANK_R0 = 0.6, 0.7, 0.75   # the shafts' radii

# ---------------------------------------------------------------- the knife and its crank
R_CRANK = 2.0                                # the crank's throw: the knife's stroke is 4
CRANK_X = (17.2, 18.4)                       # the crank disc, overhung on the crankshaft's west end
CRANK_DISC = 2.8
PIN_R = 0.45                                 # the crank pin, out west through the yoke's slot
YOKE_X = (15.6, 17.0)
KNIFE_Z = (Z_K + 0.03, Z_K + 0.5)            # the blade and its crosshead, a hair behind the bed knife's back face
LOW_BOT, UP_TOP = LOW_TOP - 1.0, UP_BOT + 1.0    # the rods' band
EDGE_DOWN = LOW_BOT - 0.29                   # the blade's edge at the bottom of the stroke, under the lower layer
EDGE_MID = EDGE_DOWN + R_CRANK               # the blade is authored at mid-stroke
BLADE_H = 3.4                                # deeper than both layers of rods, so its crosshead stays over them
XHEAD_H = 1.2
BLADE_X = (11.2, 20.8)
XHEAD_X = (HOUSING_X[1] - SLOT_X + 0.1, 32.0 - HOUSING_X[1] + SLOT_X - 0.1)
RAM_Z = (23.9, 24.6)
BED_Y = (7.4, LOW_BOT)                       # the bed knife: the lower layer's rods run on it
BED_Z = (23.62, Z_K)

# ---------------------------------------------------------------- the feed table and the nail box
TABLE_X = (11.1, 20.9)
TABLE_Z = (1.0, 16.2)                        # stops short of the bottom cutters' bite
TABLE_Y = (8.3, Y_M - PLATE_T / 2)
GUIDE_X = (11.4, 11.95)                      # the side guides on the table; mirrored east
BEARERS_Z = ((2.0, 3.5), (12.5, 14.0))
BEARER_Y = (7.1, TABLE_Y[0])
BOX_X = (11.3, 20.7)
BOX_Z = (23.62, 31.5)
BOX_FLOOR = 0.8
BOX_TOP = 5.0
BOX_WALL = 0.6


# ---------------------------------------------------------------- the cycle's arithmetic (W in strokes)
def step_frac(W, m):
    """How far step m has gone at W, 0..1."""
    return min(1.0, max(0.0, (W - (STEP_AT[m] - HALF_STEP)) / (2 * HALF_STEP)))


def feed(W, steps=STEPS):
    """How far the rolls have carried the work at W (voxels), over the first `steps` steps."""
    return L_NAIL * sum(step_frac(W, m) for m in range(steps))


def edge_at(W):
    """The knife's edge height at W: at the bottom at every whole stroke."""
    return EDGE_DOWN + R_CRANK * (1.0 - math.cos(TAU * W))


def before_bottom(y):
    """How long (W) before a whole stroke the edge comes down through height y."""
    return math.acos(1.0 - (y - EDGE_DOWN) / R_CRANK) / TAU


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


def mirror(x0, x1):
    return (32.0 - x1, 32.0 - x0)


def sides():
    """The west and east (name suffix, x mapper) pairs: the stand is symmetric about x 16."""
    return (("w", lambda a, b: (a, b)), ("e", mirror))


def radial(c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box along x a0..a1, from radius r0 to r1 about the x axis through c = (y, z), `width` across, at
    angle `ang` (radians, right-handed about +x, from +y towards +z)."""
    el = box([a0, c[0] + r0, c[1] - width / 2], [a1, c[0] + r1, c[1] + width / 2], name, part, tex)
    if abs(ang) > 1e-12:
        rotate([el], "x", math.degrees(ang), (0.0, c[0], c[1]))
    return el


def disc(c, a0, a1, r, name, part, tex, k=4, phase=0.0, step=0.012):
    """A plain round part along x: k strips as long as the 2k-gon is across (apothem r), 180/k degrees apart,
    each a hair shorter than the last so no two ends share a plane."""
    half = r * math.tan(math.pi / (2 * k))
    out = []
    st = min(step, (a1 - a0) / (4 * k))
    for i in range(k):
        el = box([a0 + st * i, c[0] - r, c[1] - half], [a1 - st * i, c[0] + r, c[1] + half],
                 f"{name}_{i + 1}" if k > 1 else name, part, tex)
        ang = phase + math.pi * i / k
        if abs(ang) > 1e-12:
            rotate([el], "x", math.degrees(ang), (0.0, c[0], c[1]))
        out.append(el)
    return out


def ring(c, a0, a1, r_in, r_out, n, name, part, tex):
    """A ring of n boxes from r_in to r_out about x; every other one a hair shorter, so their ends never meet in a plane."""
    w = 2 * r_out * math.tan(math.pi / n)
    return [radial(c, a0 + (0.02 if i % 2 else 0.0), a1 - (0.02 if i % 2 else 0.0), r_in, r_out, w, TAU * i / n,
                   f"{name}{i + 1}", part, tex) for i in range(n)]


def teeth(c, a0, a1, n, module, phase, idx, name, part, tex):
    """Box teeth of an n-toothed gear of `module` about x, those of indices `idx` (tooth i at phase + i * pitch)."""
    p = n * module / 2
    root, tip = p - 1.25 * module, p + module
    return [radial(c, a0 + 0.03, a1 - 0.03, root - 0.25, tip, math.pi * module / 2, phase + TAU * i / n,
                   f"{name}_tooth{i + 1}", part, tex) for i in idx]


def gear(c, a0, a1, n, module, name, part, tex, phase=0.0, body_k=4):
    """A spur gear along x: a polygon body to the root and n box teeth of `module` (phase: tooth 1's angle)."""
    root = n * module / 2 - 1.25 * module
    return disc(c, a0, a1, root, f"{name}_body", part, tex, k=body_k, phase=phase) + teeth(c, a0, a1, n, module, phase, range(n), name, part, tex)


def dir_ang(c1, c2):
    """The angle (from +y towards +z) of the direction from c1 to c2."""
    return math.atan2(c2[1] - c1[1], c2[0] - c1[0])


def mesh_phase(c1, phase1, n1, c2, n2):
    """The tooth phase for gear 2 meshing gear 1 (whose tooth 1 is at phase1), both about x: on the line of
    centres, gear 1's tooth and gear 2's gap meet."""
    d = dir_ang(c1, c2)
    p1, p2 = TAU / n1, TAU / n2
    off = ((d - phase1) / p1) % 1.0
    return (d + math.pi) + p2 / 2 + off * p2


# ---------------------------------------------------------------- the work's geometry
def rod_x(i):
    return (X_ROD0 + i, X_ROD0 + i + 1)


def layer(i):
    """Rod i's (y0, y1): the even rods run under the top cutters (the lower layer), the odd over the bottom ones."""
    return (LOW_BOT, LOW_TOP) if i % 2 == 0 else (UP_BOT, UP_TOP)


def collar_x(p):
    x0, x1 = COLLAR_X0 + p, COLLAR_X0 + p + 1
    if p == 0:
        x0 += END_TRIM
    if p == COLLARS - 1:
        x1 -= END_TRIM
    return x0, x1


def top_cutter(p):
    """Collar position p is a cutter on the top arbor (odd p) or on the bottom one (even p)."""
    return p % 2 == 1


# ---------------------------------------------------------------- builders: the slitters
def build_arbor(which):
    """An arbor: its collars (cutters and spacers), its necks through the chocks, collars locating it; the top
    arbor's east neck runs on to the great wheel."""
    top = which == "top"
    y = Y_T if top else Y_B
    c = (y, Z_N)
    part = f"roll{which}"
    out = []
    for p in range(COLLARS):
        x0, x1 = collar_x(p)
        if top_cutter(p) == top:
            out += disc(c, x0, x1, R_CUT, f"{part}_cutter{p}", part, "steel", k=CUT_K, phase=math.pi / 12)
        else:
            out += disc(c, x0, x1, R_SPACER, f"{part}_spacer{p}", part, "steel", k=SPACER_K)
    out += disc(c, 5.0, COLLAR_X0 + END_TRIM, R_ARBOR, f"{part}_neckw", part, "steel")
    x_end = G_X[1] + 0.7 if top else 23.4
    out += disc(c, 32.0 - COLLAR_X0 - END_TRIM, x_end, R_ARBOR, f"{part}_necke", part, "steel")
    out += disc(c, 4.6, 5.0, 1.8, f"{part}_collarw", part, "iron")
    if top:
        out += disc(c, x_end, x_end + 0.4, 1.8, f"{part}_collare", part, "iron")
    else:
        out += disc(c, 23.45, 23.9, 1.6, f"{part}_collare", part, "iron")
    return out


def build_pinions():
    """The pinions: two equal spur gears (16 teeth, module 0.375) on the arbors' west necks, in mesh, so the
    arbors turn together the opposite ways; their hubs against the chocks."""
    out = []
    phase_t = 0.0
    phase_b = mesh_phase((Y_T, Z_N), phase_t, PIN_T, (Y_B, Z_N), PIN_T)
    for which, y, ph in (("top", Y_T, phase_t), ("bottom", Y_B, phase_b)):
        part = f"pinion{which}"
        out += gear((y, Z_N), 5.6, 7.4, PIN_T, PIN_MOD, f"{part}_gear", part, "iron", phase=ph, body_k=6)
        out += disc((y, Z_N), 7.4, CHOCK_X[0] - 0.05, 1.6, f"{part}_hub", part, "iron")
        out += disc((y, Z_N), 5.05, 5.6, 1.6, f"{part}_boss", part, "iron")
    return out


def sector_angles():
    """The mutilated gear's three teeth, authored at W 0: at the middle of the first step (W 0.5, the crankshaft
    half a turn on) the middle tooth points at the great wheel's centre."""
    d = dir_ang(K, (Y_T, Z_N))
    return [d - math.pi + (i - (SECTOR - 1) / 2) * TAU / M_T for i in range(SECTOR)]


def wheel_phase():
    """The great wheel's tooth phase, authored at W 0: at the middle of the first step (the wheel half a step,
    -STEP_ANGLE / 2, on) a gap of it faces the mutilated gear's middle tooth on the line of centres."""
    d = dir_ang((Y_T, Z_N), K)
    return d + math.pi / G_T + STEP_ANGLE / 2


def build_drive():
    """The great wheel on the top arbor's east neck (a cast spoked wheel of 30); the crankshaft with its mutilated
    gear (three teeth on a plain disc cut back below the wheel's teeth), the clutch gear loose on it with the cup,
    the cone feathered on it and its fork; the rectified shaft with its gear and the two loose wheels with their
    catches; the idler on its stud; the entry's two pinions."""
    out = []
    t = (Y_T, Z_N)
    g_ph = wheel_phase()
    out += teeth(t, *G_X, G_T, FEED_MOD, g_ph, range(G_T), "wheel", "wheel", "iron")
    root = pr(G_T, FEED_MOD) - 1.25 * FEED_MOD
    n_rim = 24
    w = 2 * (root + 0.05) * math.tan(math.pi / n_rim)
    for i in range(n_rim):
        s = 0.02 if i % 2 else 0.0
        out.append(radial(t, G_X[0] + s, G_X[1] - s, root - 0.7, root + 0.05, w, TAU * i / n_rim, f"wheel_rim{i + 1}", "wheel", "iron"))
    for i in range(6):
        out.append(radial(t, G_X[0] + 0.45, G_X[1] - 0.45, 1.9, root - 0.5, 0.9, TAU * i / 6 + math.pi / 6, f"wheel_spoke{i + 1}", "wheel", "iron"))
    out += disc(t, 32.0 - CHOCK_X[0] + 0.05, G_X[1] + 0.25, 2.1, "wheel_hub", "wheel", "iron", k=6)
    # the crankshaft: its rod from the crank to the standard, the mutilated gear keyed on it
    rim = (math.hypot(K[0] - t[0], K[1] - t[1]) - (pr(G_T, FEED_MOD) + FEED_MOD) - 0.08) * math.cos(math.pi / 12)   # its corners clear of the wheel's teeth
    out += disc(K, CRANK_X[1] - 0.1, STD_X[1], CRANK_R0, "crankshaft_rod", "crankshaft", "steel")
    out += disc(K, *G_X, rim, "crankshaft_disc", "crankshaft", "iron", k=6)
    for i, a in enumerate(sector_angles()):
        out.append(radial(K, G_X[0] + 0.03, G_X[1] - 0.03, rim - 0.3, pr(M_T, FEED_MOD) + FEED_MOD, math.pi * FEED_MOD / 2, a,
                          f"crankshaft_tooth{i + 1}", "crankshaft", "iron"))
    out += disc(K, PED_X[1] + 0.05, G_X[0], 1.3, "crankshaft_hub", "crankshaft", "steel")
    # the clutch gear, loose on the crankshaft, turned by the rectified shaft's gear; the cup on its east face
    s_ph = 0.0
    k_ph = mesh_phase(R, s_ph, SK_T, K, SK_T)
    out += gear(K, *S_X, SK_T, RECT_MOD, "clutchgear", "clutchgear", "steel", phase=k_ph, body_k=6)
    out += ring(K, *CUP_X, *CUP_R, 12, "clutchgear_cup", "clutchgear", "cupronickel")
    out += disc(K, CUP_X[0] - 0.05, CUP_X[1] - 0.25, CUP_R[0] + 0.05, "clutchgear_cupback", "clutchgear", "cupronickel", k=6)
    # the cone, feathered on the crankshaft, and its fork on a shifter rod through the standard
    out += disc(K, *CONE_X, CONE_R, "cone_body", "cone", "cupronickel", k=6)
    out += disc(K, *NECK_X, 1.0, "cone_neck", "cone", "steel")
    out += disc(K, *FLANGE_X, 1.6, "cone_flange", "cone", "steel", k=6)
    fy, fz = K
    fx = (NECK_X[0] + 0.05, NECK_X[1] - 0.05)
    out += [box([fx[0], fy - 0.4, fz - 1.55], [fx[1], fy + 2.2, fz - 1.1], "clutchfork_prongn", "clutchfork", "iron"),
            box([fx[0], fy - 0.4, fz + 1.1], [fx[1], fy + 2.2, fz + 1.55], "clutchfork_prongs", "clutchfork", "iron"),
            box([fx[0], fy + 1.75, fz - 1.1], [fx[1], fy + 2.2, fz + 1.1], "clutchfork_bridge", "clutchfork", "iron"),
            box([fx[0], fy + 2.2, fz - 0.22], [STD_X[1] + 0.7, fy + 2.64, fz + 0.22], "clutchfork_rod", "clutchfork", "iron"),
            box([STD_X[1] + 0.7, fy + 1.9, fz - 0.6], [STD_X[1] + 0.95, fy + 3.2, fz + 0.6], "clutchfork_knob", "clutchfork", "iron")]
    # the rectified shaft: its rod from the front pedestal to the standard, its gear keyed on it
    out += disc(R, PED_X[0] + 0.1, STD_X[1], RECT_R, "rectshaft_rod", "rectshaft", "steel")
    out += gear(R, *S_X, SK_T, RECT_MOD, "rectshaft_gear", "rectshaft", "steel", phase=s_ph, body_k=6)
    out += disc(R, PED_X[1] + 0.05, S_X[0], 1.3, "rectshaft_hub", "rectshaft", "steel")
    out += disc(R, S_X[1], X1[0], 1.3, "rectshaft_spacer", "rectshaft", "steel")
    # the entry's pinions and the loose wheels
    e = E
    ph_a1, ph_a2 = 0.0, 0.0
    out += gear(e, *X1, A1_T, RECT_MOD, "entrygears_a1", "entrygears", "steel", phase=ph_a1)
    out += gear(e, *X2, A2_T, RECT_MOD, "entrygears_a2", "entrygears", "steel", phase=ph_a2)
    out += disc(e, X1[1], X2[0], 1.3, "entrygears_hub", "entrygears", "steel")
    out += gear(R, *X1, W1_T, RECT_MOD, "rectw1_gear", "rectw1", "iron", phase=mesh_phase(e, ph_a1, A1_T, R, W1_T), body_k=6)
    out.append(box([X1[1] + 0.02, R[0] + 0.9, R[1] - 0.3], [X2[0] - 0.02, R[0] + 1.7, R[1] + 0.3], "rectw1_catch", "rectw1", "steel"))
    ph_i = mesh_phase(e, ph_a2, A2_T, IDL, I_T)
    out += gear(IDL, *X2, I_T, RECT_MOD, "idler_gear", "idler", "iron", phase=ph_i)
    out += gear(R, *X2, W2_T, RECT_MOD, "rectw2_gear", "rectw2", "iron", phase=mesh_phase(IDL, ph_i, I_T, R, W2_T), body_k=6)
    out += disc(R, X2[1], X2[1] + 0.3, 1.3, "rectw2_hub", "rectw2", "iron")
    out.append(box([X2[1] + 0.05, R[0] - 1.7, R[1] - 0.3], [X2[1] + 0.3, R[0] - 0.9, R[1] + 0.3], "rectw2_catch", "rectw2", "steel"))
    return out


def build_entry():
    """The entry shaft: the vanilla axle's oak cross profile from the east face to the standard, then a round iron
    shaft through it to the inner post."""
    y, z = E
    out = [box([STD_X[1], y - 0.6, z - 1.5], [32.0, y + 0.6, z + 1.5], "entry_oaka", "entry", "oak"),
           box([STD_X[1] + 0.012, y - 1.5, z - 0.6], [32.0 - 0.012, y + 1.5, z + 0.6], "entry_oakb", "entry", "oak")]
    out += disc(E, EPOST_X[0] - 0.1, STD_X[1] + 0.3, ENTRY_R, "entry_rod", "entry", "iron")
    return out


def build_shear():
    """The cut-off shear: the crank disc with its pin on the crankshaft's west end; the knife (the blade, its
    crosshead with its ends in the housings' slots, the ram and the yoke whose slot the pin runs in), authored at
    mid-stroke; and the fixed bed knife the rods run over."""
    out = disc(K, *CRANK_X, CRANK_DISC, "crank_disc", "crank", "iron", k=6)
    pin_c = (K[0] - R_CRANK, K[1])                # the pin at the bottom of its circle: W 0
    out += disc(pin_c, 15.4, CRANK_X[0] + 0.05, PIN_R, "crank_pin", "crank", "steel")
    out += disc(pin_c, 15.15, 15.45, 0.75, "crank_washer", "crank", "steel")
    e0 = EDGE_MID
    out.append(box([BLADE_X[0], e0, KNIFE_Z[0]], [BLADE_X[1], e0 + BLADE_H, KNIFE_Z[1]], "knife_blade", "knife", "steel"))
    xh = e0 + BLADE_H
    out.append(box([XHEAD_X[0], xh, KNIFE_Z[0]], [XHEAD_X[1], xh + XHEAD_H, KNIFE_Z[1]], "knife_crosshead", "knife", "iron"))
    slot_mid = K[0]                               # the yoke's slot, authored at mid-stroke: the crank's centre height
    yb0 = slot_mid - 0.5 - 0.8                    # the yoke's bottom bar
    out.append(box([YOKE_X[0], xh + XHEAD_H, RAM_Z[0]], [YOKE_X[1], yb0, RAM_Z[1]], "knife_ram", "knife", "iron"))
    zl, zh = K[1] - R_CRANK - PIN_R - 0.15, K[1] + R_CRANK + PIN_R + 0.15
    out.append(box([YOKE_X[0], yb0, zl - 0.5], [YOKE_X[1], slot_mid - 0.5, zh + 0.5], "knife_yokebottom", "knife", "iron"))
    out.append(box([YOKE_X[0], slot_mid + 0.5, zl - 0.5], [YOKE_X[1], slot_mid + 1.3, zh + 0.5], "knife_yoketop", "knife", "iron"))
    out.append(box([YOKE_X[0], slot_mid - 0.5, zl - 0.5], [YOKE_X[1], slot_mid + 0.5, zl], "knife_yokenorth", "knife", "iron"))
    out.append(box([YOKE_X[0], slot_mid - 0.5, zh], [YOKE_X[1], slot_mid + 0.5, zh + 0.5], "knife_yokesouth", "knife", "iron"))
    out.append(box([COLLAR_X0, BED_Y[0], BED_Z[0]], [32.0 - COLLAR_X0, BED_Y[1], BED_Z[1]], "bedknife_bar", "bedknife", "steel"))
    return out


def build_pusher():
    """The push bar, the operator's follower, at rest: a head between the table's guides with a handle back to the
    table's north end, and eight fingers forward from it, each the section of the rod it pushes (a hair smaller) at
    that rod's layer: the lower ones run in grooves in the table, all of them through the slitters' channels."""
    f0 = PUSH_REST
    hz = (f0 - FINGER_L - HEAD_T, f0 - FINGER_L)
    out = [box([GUIDE_X[1] + 0.05, TABLE_Y[1] + 0.05, hz[0]], [32.0 - GUIDE_X[1] - 0.05, UP_TOP + 1.2, hz[1]], "pusher_head", "pusher", "iron")]
    for i in range(RODS):
        rx, ly = rod_x(i), layer(i)
        n = FINGER_IN
        out.append(box([rx[0] + n, ly[0] + n, hz[1]], [rx[1] - n, ly[1] - n, f0], f"pusher_finger{i + 1}", "pusher", "iron"))
    hy = (UP_TOP + 0.4, UP_TOP + 0.9)
    out.append(box([XC - 0.35, hy[0], 0.6], [XC + 0.35, hy[1], hz[0]], "pusher_handle", "pusher", "oak"))
    out.append(box([XC - 2.0, hy[0] - 0.1, 0.2], [XC + 2.0, hy[1] + 0.1, 0.6], "pusher_grip", "pusher", "oak"))
    return out


# ---------------------------------------------------------------- builders: the frame
def build_frame():
    """The oak sills, the two cast-iron housings (base, posts with the knife's slot, cap) with their brass chocks
    and the screws on them, the pedestals for the crankshaft and the rectified shaft on the east cap, the entry's
    inner post, the outer standard with the idler's stud and the fork rod's guide, the feed table on its bearers and
    posts with its side guides, and the nail box."""
    f = "frame"
    out = []
    for i, (x0, x1) in enumerate(SILLS):
        out.append(box([x0, 0.0, SILL_Z[0]], [x1, SILL_H, SILL_Z[1]], f"fr_sill{i + 1}", f, "oak"))
    for s, mx in sides():
        hx = mx(*HOUSING_X)
        inner_lo = s == "e"                       # the east housing's inner face is its low x
        out.append(box([hx[0], H_BASE_Y[0], H_Z[0]], [hx[1], H_BASE_Y[1], H_Z[1]], f"fr_hbase_{s}", f, "iron"))
        out.append(box([hx[0], H_BASE_Y[1], POST_F_Z[0]], [hx[1], CAP_Y[0], POST_F_Z[1]], f"fr_hpostf_{s}", f, "iron"))
        sx = mx(HOUSING_X[1] - SLOT_X, HOUSING_X[1])
        ox = (sx[1], hx[1]) if inner_lo else (hx[0], sx[0])
        out.append(box([ox[0], H_BASE_Y[1], POST_B_Z[0]], [ox[1], CAP_Y[0], POST_B_Z[1]], f"fr_hpostb_{s}", f, "iron"))
        out.append(box([sx[0], H_BASE_Y[1], POST_B_Z[0]], [sx[1], CAP_Y[0], SLOT_Z[0]], f"fr_hcheekf_{s}", f, "iron"))
        out.append(box([sx[0], H_BASE_Y[1], SLOT_Z[1]], [sx[1], CAP_Y[0], POST_B_Z[1]], f"fr_hcheekb_{s}", f, "iron"))
        out.append(box([hx[0], CAP_Y[0], H_Z[0]], [hx[1], CAP_Y[1], H_Z[1]], f"fr_hcap_{s}", f, "iron"))
        cx = mx(*CHOCK_X)
        for which, y in (("b", Y_B), ("t", Y_T)):
            y0 = H_BASE_Y[1] if which == "b" else y - CHOCK_H      # the bottom chock sits on the housing's base
            out.append(box([cx[0], y0, CHOCK_Z[0]], [cx[1], y + CHOCK_H, CHOCK_Z[1]], f"fr_chock{which}_{s}", f, "brass"))
        # the screw down through the cap onto the top chock, its square head and capstan bar
        xs = (hx[0] + hx[1]) / 2
        top_ch = Y_T + CHOCK_H
        out += [box([xs - SCREW_R, top_ch, Z_N - SCREW_R], [xs + SCREW_R, SCREW_TOP - 0.6, Z_N + SCREW_R], f"fr_screw_{s}", f, "steel"),
                box([xs - 1.0, SCREW_TOP - 1.2, Z_N - 1.0], [xs + 1.0, SCREW_TOP, Z_N + 1.0], f"fr_screwhead_{s}", f, "iron"),
                box([xs - 1.4, SCREW_TOP - 0.85, Z_N - 0.22], [xs + 1.4, SCREW_TOP - 0.4, Z_N + 0.22], f"fr_capstan_{s}", f, "iron")]
    # the east cap's pedestals: the crankshaft's bearing (back) and the rectified shaft's (front)
    for name, (y, z) in (("crank", K), ("rect", R)):
        z0, z1 = max(z - 1.3, H_Z[0]), min(z + 1.3, H_Z[1])
        out.append(box([PED_X[0], CAP_Y[1], z0], [PED_X[1], y - 1.6, z1], f"fr_ped{name}", f, "iron"))
        out.append(box([PED_X[0], y - 1.6, z - 1.6], [PED_X[1], y + 1.6, z + 1.6], f"fr_brg{name}", f, "iron"))
    # the entry's inner post
    out.append(box([EPOST_X[0], 0.0, E[1] - 1.2], [EPOST_X[1], E[0] - 1.4, E[1] + 1.2], "fr_epost", f, "iron"))
    out.append(box([EPOST_X[0], E[0] - 1.4, E[1] - 1.4], [EPOST_X[1], E[0] + 1.4, E[1] + 1.4], "fr_brgentry", f, "iron"))
    # the outer standard: a foot, an upright under each bearing, rails, the idler's stud boss, the fork rod's guide
    out.append(box([29.6, 0.0, E[1] - 2.0], [32.0, 1.2, K[1] + 2.0], "fr_stdfoot", f, "iron"))
    for name, (y, z) in (("entry", E), ("rect", R), ("crank", K)):
        out.append(box([STD_X[0], 1.2, z - 1.6], [STD_X[1], y + 1.6, z + 1.6], f"fr_std{name}", f, "iron"))
    out.append(box([STD_X[0], 9.0, E[1] + 1.6], [STD_X[1], 11.0, R[1] - 1.6], "fr_stdrail1", f, "iron"))
    out.append(box([STD_X[0], 9.0, R[1] + 1.6], [STD_X[1], 11.0, K[1] - 1.6], "fr_stdrail2", f, "iron"))
    out.append(box([STD_X[0] - 0.05, IDL[0] - 0.9, E[1] + 1.6], [STD_X[1], IDL[0] + 0.9, IDL[1] + 0.9], "fr_studboss", f, "iron"))
    out += disc(IDL, X2[0] - 0.1, STD_X[0] - 0.05, 0.5, "fr_stud", f, "steel")
    gy = K[0] + 2.2
    out.append(box([STD_X[0], K[0] + 1.6, K[1] - 0.8], [STD_X[1], gy, K[1] + 0.8], "fr_forkguide", f, "iron"))
    out.append(box([STD_X[0], gy + 0.44, K[1] - 0.8], [STD_X[1], gy + 0.9, K[1] + 0.8], "fr_forkguidecap", f, "iron"))
    for zz in (K[1] - 0.8, K[1] + 0.22):
        out.append(box([STD_X[0], gy, zz], [STD_X[1], gy + 0.44, zz + 0.58], f"fr_forkguide{'ns'[zz > K[1]]}", f, "iron"))
    # the feed table on two bearers, each on two posts on the sills; the side guides
    for i, (z0, z1) in enumerate(BEARERS_Z):
        out.append(box([SILLS[0][0] + 0.5, BEARER_Y[0], z0], [SILLS[1][1] - 0.5, BEARER_Y[1], z1], f"fr_bearer{i + 1}", f, "oak"))
        for s, mx in sides():
            px = mx(SILLS[0][0] + 0.5, SILLS[0][0] + 2.5)
            out.append(box([px[0], SILL_H, z0], [px[1], BEARER_Y[0], z1], f"fr_tpost{i + 1}{s}", f, "oak"))
    # the table: a board under four grooves the push bar's lower fingers run in, and the lands between them
    gb = LOW_BOT + FINGER_IN - 0.1               # the grooves' floor, under the fingers
    out.append(box([TABLE_X[0], TABLE_Y[0], TABLE_Z[0]], [TABLE_X[1], gb, TABLE_Z[1]], "fr_table", f, "oak"))
    edges = [TABLE_X[0]]
    for i in range(0, RODS, 2):
        rx = rod_x(i)
        edges += [rx[0] - 0.05, rx[1] + 0.05]
    edges.append(TABLE_X[1])
    for k in range(0, len(edges), 2):
        out.append(box([edges[k], gb, TABLE_Z[0]], [edges[k + 1], TABLE_Y[1], TABLE_Z[1]], f"fr_tableland{k // 2 + 1}", f, "oak"))
    for s, mx in sides():
        gx = mx(*GUIDE_X)
        out.append(box([gx[0], TABLE_Y[1], TABLE_Z[0] + 2.0], [gx[1], TABLE_Y[1] + 0.8, TABLE_Z[1] - 0.3], f"fr_guide_{s}", f, "iron"))
    # the nail box: a floor on the ground, four walls
    (bx0, bx1), (bz0, bz1), t = BOX_X, BOX_Z, BOX_WALL
    out.append(box([bx0, 0.0, bz0], [bx1, BOX_FLOOR, bz1], "fr_boxfloor", f, "oak"))
    out.append(box([bx0, BOX_FLOOR, bz0], [bx1, BOX_TOP, Z_K], "fr_boxfront", f, "oak"))
    out.append(box([bx0, BOX_FLOOR, bz1 - t], [bx1, BOX_TOP, bz1], "fr_boxback", f, "oak"))
    out.append(box([bx0, BOX_FLOOR, Z_K], [bx0 + t, BOX_TOP, bz1 - t], "fr_boxwest", f, "oak"))
    out.append(box([bx1 - t, BOX_FLOOR, Z_K], [bx1, BOX_TOP, bz1 - t], "fr_boxeast", f, "oak"))
    # a shelf in the box beyond where the nails fall, level with them stacked: the strips lie on both
    out.append(box([bx0 + t, BOX_FLOOR, Z_K + L_NAIL], [bx1 - t, STRIP_REST, bz1 - t], "fr_boxshelf", f, "oak"))
    return out


# ---------------------------------------------------------------- builders: the work
def build_work():
    """The plate on the feed table, its front at the nip; the eight rods, each authored EPS_ROD long at the nip
    in its layer (they grow from it); and the two nails of each rod, authored EPS_NAIL long inside the rods' first
    voxel (they are carried to the cut as the rods reach it, grow there, are pressed down by the knife and fall)."""
    x0, x1 = XC - PLATE_W / 2, XC + PLATE_W / 2
    plate = box([x0, Y_M - PLATE_T / 2, Z_N - PLATE_L], [x1, Y_M + PLATE_T / 2, Z_N], "plate_sheet", "plate", "metal")
    out = [plate]
    for i in range(RODS):
        rx, ly = rod_x(i), layer(i)
        part = "rodsl" if i % 2 == 0 else "rodsu"
        el = box([rx[0], ly[0], Z_N], [rx[1], ly[1], Z_N + EPS_ROD], f"{part}_{i + 1}", part, "metal")
        retexture_full(el, (1.0, 1.0, D_KNIFE))
        out.append(el)
    for j in (1, 2):
        for i in range(RODS):
            rx, ly = rod_x(i), layer(i)
            lay = "l" if i % 2 == 0 else "u"
            n = NAIL_IN
            el = box([rx[0] + n, ly[0] + n, Z_N + 0.002], [rx[1] - n, ly[1] - n, Z_N + 0.002 + EPS_NAIL], f"nail{j}{lay}_{i + 1}", f"nail{j}{lay}", "metal")
            retexture_full(el, (1.0 - 2 * n, 1.0 - 2 * n, L_NAIL))
            out.append(el)
    return out


def retexture_full(el, size):
    """UVs for the size the element will be stretched to, so its texture is not smeared when it grows."""
    axes = {"north": (0, 1), "south": (0, 1), "east": (2, 1), "west": (2, 1), "up": (0, 2), "down": (0, 2)}
    for d, (u, v) in axes.items():
        el.faces[d]["uv"] = [0.0, 0.0, min(size[u] * TEX / 16, TEX), min(size[v] * TEX / 16, TEX)]


def build():
    return (build_arbor("top") + build_arbor("bottom") + build_pinions() + build_drive() + build_entry() + build_shear()
            + build_pusher() + build_work() + build_frame())


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def one(v):
    """A per-class value: the one class, a plate."""
    return {"thin": r6(v)}


WORK = {"name": "knife strokes", "unit": "strokes", "step": 0.005, "end": {"thin": END}}
PATH = WORK
FOREVER = 1000.0


def win(w0, w1, to=FOREVER, gain=None):
    """A window rising over W w0..w1 and open to `to`."""
    w = {"from": r6(w0), "to": r6(to), "ease": r6(w1 - w0)}
    if gain is not None:
        w["gain"] = one(gain)
    return w


def step_win(m, gain=None):
    """Feed step m's window: while the mutilated gear's sector is in mesh."""
    return win(STEP_AT[m] - HALF_STEP, STEP_AT[m] + HALF_STEP, gain=gain)


def gauge(motion, axis, amount, wins=None, pivot=None, origin=None, mode=None):
    d = {"type": "gauge", "motion": motion, "axis": axis}
    if pivot is not None:
        d["pivot"] = pivot
    if origin is not None:
        d["origin"] = origin
    d["amount"] = amount
    if mode:
        d["mode"] = mode
    else:
        d["windows"] = wins
    return d


def turn(ratio, c, inp=None):
    d = {"type": "rotate", "axis": "x", "pivot": pt(0.0, c[0], c[1]), "ratio": r6(ratio)}
    if inp:
        d["input"] = inp
    return d


def stepping(sign, c):
    """A part the mutilated gear steps (the great wheel, an arbor, a pinion): sign * STEP_ANGLE in each feed step,
    standing between. Written as sign * STEP_ANGLE * W, unscaled, plus p times (the steps less that), so that at a
    whole stroke (where the two agree: at the job's start and end) p easing in or out turns nothing. The angle is rounded
    as the rig writes it first, so the three agree exactly."""
    a = r6(sign * STEP_ANGLE)
    piv = pt(0.0, c[0], c[1])
    out = [{"type": "rotate", "axis": "x", "pivot": piv, "ratio": r6(a), "input": "work"},
           gauge("rotate", "x", one(-a * END), [win(0.0, END)], pivot=piv)]
    return out + [gauge("rotate", "x", one(a), [step_win(m)], pivot=piv) for m in range(STEPS)]


def grow(origin_z, eps, full, wins, y=Y_M):
    """A stretch along z about the plane z = origin_z: from `eps` long towards `full` as its windows fill."""
    return gauge("stretch", "z", one(full / eps - 1.0), wins, origin=pt(XC, y, origin_z))


def taken_off(origin_z, y=Y_M):
    """The nails and strips go at the hand-out: a stretch to nothing over the last TAKE_OFF of the job."""
    return gauge("stretch", "z", one(-1.0), [win(END - TAKE_OFF, END)], origin=pt(XC, y, origin_z))


def strip_drop(lay):
    """How far a strip falls: from its layer onto the second nails and the shelf."""
    return (LOW_BOT if lay == "l" else UP_BOT) - STRIP_REST


def nail_top(lay):
    return (LOW_TOP if lay == "l" else UP_TOP) - NAIL_IN


def nail_drop(lay, j):
    """How far nail set j of a layer goes down in all: from its layer to the box's floor, or onto the set before it."""
    return (LOW_BOT if lay == "l" else UP_BOT) - (BOX_FLOOR + (j - 1) * 1.0)


def cut_marks(lay, j):
    """W at which the knife's edge comes down onto nail j of a layer, and the W of each of CUT_SEGS equal depths
    after it, to the one at which it has gone through: the nail is pressed down with the edge and parts there."""
    w_c = CUTS[j - 1]
    return [w_c - before_bottom(nail_top(lay) - i / CUT_SEGS) for i in range(CUT_SEGS + 1)]


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    t, b = (Y_T, Z_N), (Y_B, Z_N)
    kp = pt(0.0, *K)
    w_turn = {"type": "rotate", "axis": "x", "pivot": kp, "ratio": r6(TAU), "input": "work"}
    parts = [
        {"id": "entry", "match": ["entry_*"], "requires": None, "drivers": [turn(1.0, E)]},
        {"id": "entrygears", "match": ["entrygears_*"], "requires": "drive", "drivers": [turn(1.0, E)]},
        {"id": "rectw1", "match": ["rectw1_*"], "requires": "drive", "drivers": [turn(-A1_T / W1_T, R)]},
        {"id": "idler", "match": ["idler_*"], "requires": "drive", "drivers": [turn(-A2_T / I_T, IDL)]},
        {"id": "rectw2", "match": ["rectw2_*"], "requires": "drive", "drivers": [turn(A2_T / W2_T, R)]},
        {"id": "rectshaft", "match": ["rectshaft_*"], "requires": "drive", "drivers": [turn(-1.0, R, "travel")]},
        {"id": "clutchgear", "match": ["clutchgear_*"], "requires": "drive", "drivers": [turn(1.0, K, "travel")]},
        {"id": "cone", "match": ["cone_*"], "requires": "drive",
         "drivers": [gauge("slide", "x", one(-CONE_IN / B), mode="present"), dict(w_turn)]},
        {"id": "clutchfork", "match": ["clutchfork_*"], "requires": "drive",
         "drivers": [gauge("slide", "x", one(-CONE_IN / B), mode="present")]},
        {"id": "crankshaft", "match": ["crankshaft_*"], "requires": "drive", "drivers": [dict(w_turn)]},
        {"id": "crank", "match": ["crank_*"], "requires": "shear", "drivers": [dict(w_turn)]},
        {"id": "knife", "match": ["knife_*"], "requires": "knife",
         "drivers": [{"type": "slide", "axis": "y", "amplitude": r6(R_CRANK / B), "ratio": r6(TAU), "phase": r6(-math.pi / 2),
                      "input": "work"}]},
        {"id": "bedknife", "match": ["bedknife_*"], "requires": "shear", "drivers": []},
        {"id": "wheel", "match": ["wheel_*"], "requires": "pinions", "drivers": stepping(-1.0, t)},
        {"id": "rolltop", "match": ["rolltop_*"], "requires": "rolls", "drivers": stepping(-1.0, t)},
        {"id": "rollbottom", "match": ["rollbottom_*"], "requires": "rolls", "drivers": stepping(1.0, b)},
        {"id": "piniontop", "match": ["piniontop_*"], "requires": "pinions", "drivers": stepping(-1.0, t)},
        {"id": "pinionbottom", "match": ["pinionbottom_*"], "requires": "pinions", "drivers": stepping(1.0, b)},
        # the work: the plate goes into the nip a step at a time, the rods come out of it, the nails come past the cut,
        # are pressed down by the knife's edge until they part, and fall
        {"id": "plate", "match": ["plate_*"], "requires": "plate",
         "drivers": [gauge("stretch", "z", one(-1.0), [step_win(m, 1.0 / PLATE_STEPS) for m in range(PLATE_STEPS)],
                           origin=pt(XC, Y_M, Z_N))]},
    ]
    for lay in ("l", "u"):
        parts.append({"id": f"rods{lay}", "match": [f"rods{lay}_*"], "requires": "plate",
                      "drivers": [grow(Z_N, EPS_ROD, D_KNIFE, [step_win(m, 1.0 / ROD_STEPS) for m in range(ROD_STEPS)]), taken_off(Z_N),
                                  gauge("slide", "z", one(D_KNIFE / B), [win(*PUSH)]),
                                  gauge("slide", "y", one(-strip_drop(lay) / B), [win(*STRIP_FALL)])]})
    # the push bar follows the plate's tail a step at a time, pushes the strips out and is drawn back: net nothing a job
    follow = [gauge("slide", "z", one(L_NAIL / B), [step_win(m)]) for m in range(PLATE_STEPS)]
    parts.append({"id": "pusher", "match": ["pusher_*"], "requires": "pusher",
                  "drivers": follow + [gauge("slide", "z", one(D_KNIFE / B), [win(*PUSH)]),
                                       gauge("slide", "z", one(-(PLATE_L + D_KNIFE) / B), [win(*PUSH_BACK)])]})
    z0 = Z_N + 0.002
    for j in (1, 2):
        s = NAIL_STEP[j]
        start = STEP_AT[s] - HALF_STEP
        for lay in ("l", "u"):
            marks = cut_marks(lay, j)
            drivers = [grow(z0, EPS_NAIL, L_NAIL, [step_win(s)]), taken_off(z0),
                       gauge("slide", "z", one((Z_K - z0) / B), [win(start - 0.001, start)])]
            drivers += [gauge("slide", "y", one(-1.0 / CUT_SEGS / B), [win(marks[i], marks[i + 1])]) for i in range(CUT_SEGS)]
            drivers.append(gauge("slide", "y", one(-(nail_drop(lay, j) - 1.0) / B), [win(marks[-1], marks[-1] + FALL)]))
            parts.append({"id": f"nail{j}{lay}", "match": [f"nail{j}{lay}_*"], "requires": "plate", "drivers": drivers})
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []})
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- what moves what
# Every moving part's cause: (part, cause, how, figure). The cause check (check_causes) follows them from each part to
# the axle or to the work, and holds every link. A cause is a part, or one of the three things outside the model: "axle"
# (the vanilla axle at the power face), "player" (who lays the plate on: its presence) and "operator" (who throws the
# clutch's fork in while a plate is on, and works the push bar by its handle). The ways:
#   keyed     on the cause's shaft, turning with it (the axle: turning with the axle's angle)
#   feather   keyed to the cause, free to slide along its shaft
#   forked    slid along its shaft by the cause, the fork on its neck
#   mesh      in mesh with the cause; figure: the two pitch radii (the part's, the cause's)
#   catch     carried by the cause's one-way catch (one loose wheel each way the axle turns)
#   clutch    turned by the cause's cup through the cone's smooth faces, at the drawn pace
#   sector    turned by the cause's teeth while one of them is in contact with its own; figure: the pitch radii and the
#             teeth's names (the part's, the cause's)
#   crankpin  the cause's crank pin in its yoke's slot
#   work      the work's own motion, or a part working on the work; figure: the check that holds it
#   fall      falls once the cause lets it go; figure: the check that holds it
#   placed    laid on by the player
#   fork      thrown in by the operator while a plate is on (a documented exception)
#   hand      worked by the operator (a documented exception: the push bar, following the plate's tail; check_pusher
#             holds it to the work)
def causes():
    rp = RECT_MOD
    out = [
        ("entry", "axle", "keyed", None),
        ("entrygears", "entry", "keyed", None),
        ("rectw1", "entrygears", "mesh", (pr(W1_T, rp), pr(A1_T, rp))),
        ("idler", "entrygears", "mesh", (pr(I_T, rp), pr(A2_T, rp))),
        ("rectw2", "idler", "mesh", (pr(W2_T, rp), pr(I_T, rp))),
        ("rectshaft", "rectw1", "catch", None),
        ("rectshaft", "rectw2", "catch", None),
        ("clutchgear", "rectshaft", "mesh", (pr(SK_T, rp), pr(SK_T, rp))),
        ("cone", "clutchgear", "clutch", None),
        ("cone", "clutchfork", "forked", None),
        ("clutchfork", "operator", "fork", None),
        ("crankshaft", "cone", "feather", None),
        ("crank", "crankshaft", "keyed", None),
        ("knife", "crank", "crankpin", None),
        ("wheel", "crankshaft", "sector", (pr(G_T, FEED_MOD), pr(M_T, FEED_MOD), r"_tooth", r"_tooth")),
        ("rolltop", "wheel", "keyed", None),
        ("piniontop", "rolltop", "keyed", None),
        ("pinionbottom", "piniontop", "mesh", (R_PIN, R_PIN)),
        ("rollbottom", "pinionbottom", "keyed", None),
        ("pusher", "operator", "hand", "check_pusher"),
        ("plate", "player", "placed", None),
        ("plate", "rolltop", "work", "check_feed"),
        ("plate", "rollbottom", "work", "check_feed"),
    ]
    for lay in ("l", "u"):
        out += [(f"rods{lay}", "plate", "work", "check_work"),           # out of the nip as the plate is fed
                (f"rods{lay}", "pusher", "work", "check_pusher"),        # pushed out past the cut by the fingers
                (f"rods{lay}", "pusher", "fall", "check_pusher")]        # fall once their tails are past the bed knife
        for j in (1, 2):
            out += [(f"nail{j}{lay}", f"rods{lay}", "work", "check_work"),    # carried past the cut on the rods' ends
                    (f"nail{j}{lay}", "knife", "work", "check_parting"),      # pressed down by the knife's edge
                    (f"nail{j}{lay}", "knife", "fall", "check_parting")]      # falls once the edge is through
    return out


# ---------------------------------------------------------------- poses
REST = (0.0, 0.0, 0.0, 0, 0.0)               # (theta, travel, W, k, p): no plate; the knife at the bottom, the clutch out


def inputs_of(pose):
    th, ps, W, k, p = pose
    return {"theta": th, "travel": ps, "work": W, "size": k, "presence": p}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path or PATH)


def pose_at(W, k=1, p=1.0, back=False):
    """A plate at W, the axle at the drawn pace (a stroke a turn; backwards with `back`)."""
    th = TAU * TURNS_PER_STROKE * W
    return (-th if back else th, th, W, k, p)


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("output", "plate", "nip", "knife")


def make_rig(parts):
    progress_of({"work": WORK})
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the feed table's west half, "
                    "nearest the player who placed it; the plate travels south through the slitters to the knife and the nail box. "
                    "work is knife strokes since the plate went in, W: the crankshaft turns once a stroke behind the cone clutch, "
                    "the knife is at the bottom at every whole W, and the rolls step once a stroke while it is up. k is 1 with a "
                    "plate on, whatever its metal (the work's texture code is 'metal'); the clutch is in while it is present. "
                    "See the slitting mill's README for the schema.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "infeedSide": "north",
        "outputSide": "south",
        "output": {"pos": pt(XC, BOX_TOP, 32.0 - 0.01)},
        "plate": {"pos": pt(XC, Y_M + PLATE_T / 2, Z_N - PLATE_L / 2)},
        "nip": {"pos": pt(XC, Y_M, Z_N)},
        "knife": {"pos": pt(XC, Y_M, Z_K)},
        "work": dict(WORK),
        "slit": {"turnsPerStroke": TURNS_PER_STROKE,
                 "steps": [r6(s) for s in STEP_AT], "stepWidth": r6(2 * HALF_STEP),
                 "cuts": [r6(c) for c in CUTS],
                 "push": [r6(w) for w in PUSH], "pushBack": [r6(w) for w in PUSH_BACK],
                 "handOut": r6(END),
                 "rods": RODS, "nailsPerRod": len(CUTS),
                 "plate": "game:metalplate-{metal}", "output": "game:metalnailsandstrips-{metal}",
                 "_comment": "turnsPerStroke: the drawn pace, axle turns a knife stroke (at it the cone and the cup turn together; the "
                             "gameplay's pace may differ, which only the clutch's smooth faces would see). steps, stepWidth: W at the "
                             "middle of each feed step, and how long each lasts (the mutilated gear's three teeth in mesh, the knife "
                             "up). cuts: the whole strokes at which the knife is at the bottom having parted a nail off every rod. "
                             "handOut: W at which the nails and the strips are taken off, the job's end (a whole stroke: the knife at "
                             "the bottom, the crank and the rolls as at 0). push, pushBack: W over which the push bar pushes the strips out past the "
                             "cut (the knife clear above them) and is drawn back; they then lie in the box with the nails. rods, "
                             "nailsPerRod: what the model shows of a plate. plate, output: the work in and out, {metal} the plate's."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: the build frame's corner is
    the controller's)."""
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    db = [v / B for v in d]
    ship_els = copy.deepcopy(els)
    translate(ship_els, d)
    ship_parts = copy.deepcopy(parts)
    for p in ship_parts:
        for drv in p["drivers"]:
            for key in ("pivot", "anchor", "origin"):
                if key in drv:
                    drv[key] = [r6(drv[key][k] + db[k]) for k in range(3)]
    ship = copy.deepcopy(rig)
    ship["cells"] = [{**c, "pos": shift_cell(c["pos"], ORIGIN_CELL)} for c in rig["cells"]]
    ship["powerCell"] = shift_cell(rig["powerCell"], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


WORK_REQUIRES = ("plate",)


def shipped_cells(shape, ship_parts, sp):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig, leaving out the work;
    then the lids."""
    written = flatten(shape["elements"], textures={})
    req = {p["id"]: p["requires"] for p in ship_parts}
    rest = []
    for w in written:
        pid = part_of(ship_parts, w.name)
        if req[pid] in WORK_REQUIRES:
            continue
        rest.append(posed(w, _part_matrix(ship_parts, pid, inputs_of(REST), sp)))
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
    return with_lids(out)


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    sp = ship["work"]
    poses = [REST, pose_at(0.4), pose_at(2.5), pose_at(2.8, back=True), pose_at(4.2), (1.3, 7.0, 3.6, 1, 0.6)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose), sp), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; {len(cells)} cells; "
          f"power {ship['powerCell']} {ship['powerFace']}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
def ref_ws():
    """W over the job's edges: each step's start, middle and end, the cuts' contacts and partings, falls, the end."""
    ws = {0.0, 0.25, 1.0, 2.0, 2.65, 3.0, 3.6, 4.0, 4.8, END - TAKE_OFF / 2, END, *PUSH, (PUSH[0] + PUSH[1]) / 2, STRIP_FALL[1],
          (STRIP_FALL[0] + STRIP_FALL[1]) / 2, *PUSH_BACK, (PUSH_BACK[0] + PUSH_BACK[1]) / 2}
    for m in range(STEPS):
        ws |= {STEP_AT[m] - HALF_STEP, STEP_AT[m], STEP_AT[m] + HALF_STEP / 2}
    for j in (1, 2):
        for lay in ("l", "u"):
            marks = cut_marks(lay, j)
            ws |= {marks[0], marks[CUT_SEGS // 2], marks[-1], marks[-1] + FALL / 2}
    return sorted(r6(w) for w in ws)


def reference_poses():
    """No plate at theta in {0, 1.1, -2.3, 2.9} (and travel more); a plate at W over the job's edges, the axle at the
    drawn pace forward and, every third, backwards; p at 1 and, every fifth, 0.4; and p easing at the start and the end."""
    out = []
    for th in (0.0, 1.1, -2.3, 2.9):
        for extra in (0.0, 7.3):
            out.append((th, r6(abs(th) + extra), 0.0, 0, 0.0))
    for i, W in enumerate(ref_ws()):
        th = TAU * W * (-1.0 if i % 3 == 2 else 1.0)
        for p in ((1.0,) if i % 5 else (1.0, 0.4)):
            out.append((r6(th), r6(abs(th)), W, 1, p))
    for W, p in ((0.0, 0.5), (END, 0.5), (END, 0.1)):
        out.append((r6(TAU * W), r6(TAU * W), W, 1, p))
    return out


def reference_json(ship_parts, sp):
    poses = []
    for pose in reference_poses():
        th, ps, W, k, p = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose), sp)) for q in ship_parts}
        poses.append({"theta": th, "travel": ps, "work": W, "size": k, "presence": p, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped slittingmill-rig.json's parts and work: each part's matrix as 3 "
                        "rows of 4 (block units) at each pose (W in knife strokes). The site's and the mod's tests check their rig "
                        "maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. The work's texture code is 'metal': "
             "the renderer sets it to the plate's metal. Keep element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return (REST, pose_at(0.0), pose_at(0.3), pose_at(0.5), pose_at(1.25), pose_at(2.0), pose_at(2.5, back=True), pose_at(2.74),
            pose_at(2.87), pose_at(3.0), pose_at(3.5), pose_at(3.8), pose_at(4.3), pose_at(4.5), pose_at(4.75), pose_at(4.93),
            pose_at(END))


def is_work(part):
    return part in ("plate", "rodsl", "rodsu") or (part.startswith("nail") and part[4:5].isdigit())


def shown(posed_els, pose):
    """The posed elements as they can be seen: with no plate on, the work moved far away; with one, a work piece
    that is still a sliver (a rod or a nail not yet fed, hidden in the slitters or inside a rod) or gone moved away
    too (copies, in order)."""
    k = pose[3]
    out = []
    for e in posed_els:
        if is_work(e.part) and (k == 0 or e.vanished or min(abs(s) for s in e.size) < 0.05):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def fix_coplanar(els, parts):
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose), coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the slitting mill's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_slittingmill
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
    ok = validate_slittingmill.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "slittingmill.json", args.out / "slittingmill_frame.json", args.out / "slittingmill-rig.json",
                args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "slittingmill.json", SHAPE_DIR / "slittingmill_frame.json", RIG_DIR / "slittingmill-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts, ship["work"])
    ok = validate_slittingmill.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship), reference_dumps(reference_json(ship_parts, ship["work"])))
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
