#!/usr/bin/env python3
"""Generate the jaw breaker's shapes, rig and reference poses.

The jaw breaker (#912, model #914) is the first machine of the ore line (epic #684): a Blake jaw breaker under a
grizzly, one real piece of equipment with one level. Ore comes in by the game's chute from above onto the grizzly's
head; it runs down the bars, the fines falling between them, the oversize sliding off their foot into the jaws; the
breaker's product and the fines meet in a small iron hopper under the breaker, which leads into the output chute low
on the front face. It is dry and takes mechanical power from a vanilla axle at one side, coupled straight onto the
eccentric shaft: a Blake works either way round, so there is no rectifier.

    frame       the timber stand (four posts, side and cross sills, the head beam and the top tie between the tall
                front posts), the breaker's cast frame (two cheeks with their bearings and the rods' abutments, the
                front wall, the back wall), the outboard pedestal at the drive end, the hopper, and the two chute
                sections (the infeed on the top face, the output on the front face)
    jaws        the fixed jaw's plate, the hinge pin and the swinging jaw hung on it
    eccentric   the eccentric shaft with its eccentric, the two flywheels, the coupling and the axle's stub; the
                pitman hung on the eccentric
    toggles     the back and front toggles, the two tension rods and their steel coil springs
    grizzly     the grizzly: five bars with side curbs from a head block on the head beam to a cross bar on the
                cheeks, and the throat plate over the front wall

The Blake linkage is the stamp mill v2's (origin/729-stampmill-model-v2), built in the breaker's own frame and
solved exactly (jaw_at, linkage, solve3), then fitted to four harmonics of the eccentric's turn (fit, harmonics).
Everything is built here from plain boxes; no other mod's model is used. It writes, deterministically,

    jawbreaker.json         the whole machine, every stage's parts              (assets/.../shapes/block/)
    jawbreaker_frame.json   the static frame only (block and item)              (assets/.../shapes/block/)
    jawbreaker-rig.json     cells, anchors and the part rig                     (assets/.../config/)
    rig-reference.json      every part's matrix at a grid of poses              (tests/JawBreaker/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_jawbreaker.py) and exits non-zero if
a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the machine
box's north-west bottom corner, which is also the controller cell's: the bottom cell of the west column's front,
where the output chute leaves. So the build frame is the shipped frame.

The rig's one input is theta, the axle's angle: the eccentric shaft turns with it (ratio 1) and the linkage (pitman,
toggles, swinging jaw, rods, springs) follows as harmonics of it. Either way round is the same machine: nothing reads
the axle's travel. The rig has no work.
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
from machinegen.coil import Coil  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import IDENT, El, flatten, mmul, rotate, translate  # noqa: E402
from machinegen.output import (reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               shift_point, worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "JawBreaker" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/JawBreaker/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

# ---------------------------------------------------------------- the box, the cells, the anchors
CELLS_X, CELLS_Y, CELLS_Z = 2, 3, 2          # 2 wide (cheeks, a flywheel outside each), 3 high (stand, jaws, grizzly
                                             # and chute), 2 deep (the breaker's frame and the grizzly's run)
ORIGIN_CELL = (0, 0, 0)                      # the controller: the west column's front bottom cell, the output chute's
POWER_CELL, POWER_FACE = (1, 1, 1), "east"   # the vanilla axle, along x at (y 24, z 24), onto the eccentric shaft
INFEED_CELL, INFEED_FACE = (0, 2, 0), "up"   # the infeed chute's mouth on the top face, over the grizzly's head
OUTPUT_CELL, OUTPUT_FACE = (0, 0, 0), "north"   # the output chute's mouth on the front face, low
AXLE = (24.0, 24.0)                          # (y, z): the power cell's centre line, the eccentric shaft's axis

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "iron": "game:block/metal/plate/iron",
    "sheet": "game:block/metal/sheet/iron1",
    "steel": "game:block/metal/sheet-plain/steel1",
    "chute": "game:block/metal/sheet/copper1",   # the game's chute; the pack leaves its texture as the game's (copper)
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the stand (oak)
POST_X = ((0.3, 3.8), (19.0, 22.5))          # the posts (x), west and east, outside the hopper and the chute
POST_FZ = (0.4, 3.9)                         # the front posts (z): tall, to the top tie, carrying the grizzly's head
POST_BZ = (28.1, 31.6)                       # the back posts (z): short, under the back cross sill
SIDE_Y = (9.5, 12.0)                         # the side sills (y), along z between the posts
SIDE_W_X1 = 4.0                              # the west side sill reaches the hopper's west wall
CROSS_Y = (12.0, 14.5)                       # the cross sills (y), along x on the side sills: the breaker stands on them
HEAD_BEAM_Y = (37.4, 40.0)                   # the head beam between the front posts: the grizzly's head block on it
TIE_Y = (45.5, 48.0)                         # the top tie between the front posts' tops: the infeed's section against it
TIE_Z1 = 4.0                                 # the tie's back face: the infeed section's front wall

# ---------------------------------------------------------------- the Blake jaw breaker
# Built in the breaker's own frame: y as in the build frame, u along the ore's flow from the back wall (low u) to the
# fixed jaw (high u). It is mirrored into the build frame, z = JAW_C - u, so the fixed jaw faces the front (north),
# the toggles and the eccentric shaft lie behind it, and the pitman's foot is under the eccentric shaft at AXLE.
JAW_X = (5.0, 17.0)                          # the chamber (x) between the cheeks: over the west column's chutes
JAW_XC = 11.0
CHEEK_W, CHEEK_E = (3.2, 5.0), (17.0, 18.8)
EBEAR_OUT = 1.0                              # the eccentric shaft's bearing bosses stand this far outside the cheeks
JAW_BASE_Y = 14.5                            # the cheeks' and walls' foot, on the cross sills
BASE_T = 1.5                                 # the cheeks' base flanges
JAW_TOP_Y = 31.0                             # the cheeks' and the front wall's top
FIXED_U = 30.0                               # (breaker frame) the fixed jaw's face
GAP, GAPE = 2.5, 7.0                         # the jaws' gap at the bottom and at the top (the mouth), designed
FACE_BOT, FACE_TOP = (17.5, FIXED_U - GAP), (30.0, FIXED_U - GAPE)   # (y, u): the swinging jaw's face
SLAB_T = 2.6                                 # the swinging jaw's thickness (its steel face and iron slab)
TOGGLE_Y = 17.5                              # the toggles' seats on the back wall and the jaw
TOGGLE_SAG = 1.3                             # how far the pitman's foot hangs below the seats
TOGGLE_LF, TOGGLE_LB = 7.5, 5.0              # the front and back toggles' lengths
ECC = 0.8                                    # the eccentric's throw
E_Y = AXLE[0]                                # the eccentric shaft's height
HINGE_UP = 1.6                               # the hinge pin over the slab's top back corner
FIXED_T, FRONT_T, BACK_T = 1.2, 2.2, 2.4     # the fixed jaw's plate, the front wall, the back wall (u)
TOGGLE_W = 6.0                               # the toggles' width (x)
TOGGLE_T = 1.2
ROD_X = (JAW_XC - 4.3, JAW_XC + 4.3)         # the two tension rods, either side of the toggles
ROD_R = 0.45
ABUT_Z = (23.2, 24.2)                        # (build z) the rods' abutments, lugs on the cheeks' posts
ABUT_H = 1.2                                 # the lugs' half height round the rods
NUT_Z = (27.3, 27.9)                         # (build z) the rods' nuts, behind the springs
# a steel coil spring on each rod, between its nut and its abutment (the pack's own spring-steel, which the mandrel
# station winds, is a square coil: machinegen.coil draws it). One part a turn, each sliding its share of the nut's
# travel, so the coil closes up evenly as the nut comes in and opens as it goes back.
SPRING_TURNS = 4
SPRING_WIRE = 0.35                           # its square wire
SPRING_BORE = 0.75                           # its bore's half width round the rod
SHAFT_R = 1.3                                # the eccentric shaft
ECC_R = 2.4                                  # the eccentric, a disc ECC off the shaft's axis
STRAP_R = (2.45, 3.3)                        # the pitman's strap round the eccentric
FLY_R, FLY_RIM, FLY_HUB_R = 7.5, 1.8, 2.2    # the two flywheels: radius, rim, hub
FLY_W_X, FLY_E_X = (0.3, 2.0), (20.0, 21.7)  # outside each cheek, their hubs against the bearing bosses
PED_X = (25.0, 28.2)                         # the outboard pedestal at the drive end: an oak post, an iron pillow block
COUPLING_X, COUPLING_R = (28.6, 29.8), 2.4   # the coupling on the shaft's end
STUB_X = (29.8, 32.0)                        # the vanilla axle's cross profile, oak, out to the power face
JAW_E_RATIO = -1.0                           # the eccentric's turn a radian of the axle, in the breaker's frame (mirrored:
                                             # the build frame's rotation about x is +1, the axle's own sense)

# ---------------------------------------------------------------- the grizzly
GRIZ_X = (5.2, 16.8)                         # the bars' span (x): the chamber's, so the oversize lands in the mouth
GRIZ_BARS = 5
GRIZ_BAR_W, GRIZ_BAR_T = 1.2, 1.4
GRIZ_SLOPE = math.radians(40.0)
GRIZ_FOOT = (13.2, 36.0)                     # (z, y): the bars' top at their foot, over the mouth
GRIZ_HEAD_Z = 2.4                            # the bars' head (z), on the head block
CURB_X = ((4.2, 5.0), (17.0, 17.8))          # the side curbs along the bars' edges: the west one's inner face on the
                                             # infeed's bore's west side, so the whole stream lands between them
CURB_H = 1.0                                 # how far the curbs stand over the bars
CURB_Z0 = POST_FZ[1]                         # the curbs start behind the top tie
CROSSBAR_Z = (GRIZ_FOOT[0] - 1.6, GRIZ_FOOT[0] + 0.2)   # the cross bar under the bars' foot, on brackets on the cheeks
THROAT_T = 0.8
THROAT_SLOPE = math.radians(40.0)            # the throat plate's fall to the front

# ---------------------------------------------------------------- the chutes: the game's own chute section
# The infeed and the output are the game's own chute section (shapes/block/metal/chute/straight.json, game 1.22.7), so
# a player's chute line meets them seamlessly: a vanilla `chute-straight-ud` placed on the infeed cell's top face, or a
# `chute-straight-ns` against the output cell's front face, carries straight on into them. Its four walls, as the game
# has them along x (the `we` chute; voxels in a block, 16 a block, UVs on the game's 16 scale), cut to the last
# CHUTE_LEN along the chute and turned as the blocktype turns it: `ud` by rotateZ 90, `ns` by rotateY 90 about the
# block's centre. A section 8 across, walls 1 thick round a 6 x 6 bore, in the middle of the cell's face (4..12),
# flush with it. Its texture is the chute's, game:block/metal/sheet/copper1. validate_jawbreaker checks these against
# the game's file when the game is installed.
VANILLA_CHUTE = (
    ("North", (0.0, 4.0, 4.0), (16.0, 12.0, 5.0),
     {"north": ((0.0, 0.0, 8.0, 16.0), 270), "east": ((13.0, 0.0, 14.0, 8.0), 0), "south": ((4.0, 0.0, 12.0, 16.0), 90),
      "west": ((0.0, 0.0, 1.0, 8.0), 0), "up": ((0.0, 8.0, 16.0, 9.0), 0), "down": ((0.0, 4.0, 16.0, 5.0), 0)}),
    ("South", (0.0, 4.0, 11.0), (16.0, 12.0, 12.0),
     {"north": ((4.0, 0.0, 12.0, 16.0), 90), "east": ((11.0, 0.0, 12.0, 8.0), 0), "south": ((4.0, 0.0, 12.0, 16.0), 270),
      "west": ((15.0, 0.0, 16.0, 8.0), 0), "up": ((0.0, 2.0, 16.0, 3.0), 0), "down": ((0.0, 4.0, 16.0, 5.0), 0)}),
    ("Bottom", (0.0, 4.0, 5.0), (16.0, 5.0, 11.0),
     {"east": ((5.0, 0.0, 11.0, 1.0), 0), "west": ((0.5, 0.0, 6.5, 1.0), 0), "up": ((5.0, 0.0, 11.0, 16.0), 90),
      "down": ((0.0, 5.0, 16.0, 11.0), 0)}),
    ("Top", (0.0, 11.0, 5.0), (16.0, 12.0, 11.0),
     {"east": ((5.0, 0.0, 11.0, 1.0), 0), "west": ((0.0, 2.0, 1.0, 8.0), 90), "up": ((0.0, 5.0, 16.0, 11.0), 0),
      "down": ((5.0, 0.0, 11.0, 16.0), 90)}),
)
CHUTE_LEN = 3.0                              # each section's length inside the machine, in from its face
CHUTES = (("chutein", INFEED_CELL, "z"), ("chuteout", OUTPUT_CELL, "y"))   # name, cell, the axis the blocktype turns about

# ---------------------------------------------------------------- the hopper (sheet iron)
# Under the breaker, between the cross sills: a trough continuing the output section's bore back under the
# discharge, its west wall the section's, a low lip on its east side; east of it a floor falling west to the lip, past
# the chamber's east side; plates closing its back. The fines (through the bars and off the throat plate, in front of
# the front wall) and the breaker's product (between the jaws) fall into it and run out through the output section.
HOPPER_Z = (CHUTE_LEN, 15.6)                 # from the output section's back end to behind the discharge
HOPPER_TOP = 12.0                            # its walls' top, under the cross sills (the output section's top)
HOPPER_E = 17.6                              # its floor's top (east) edge
LIP_Y = 6.5                                  # the trough's east lip, where the floor ends
SHEET = 0.8                                  # the hopper's plate
SIDE_H = 1.6                                 # its floor's front and back strips over the floor


# ---------------------------------------------------------------- box helpers
def skin(el, tex):
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


AX = {"x": 0, "y": 1, "z": 2}
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}


def frame_of(axis):
    a = AX[axis]
    u, w = REF[axis]
    return a, u, w


def radial(axis, c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box along an axis, from r0 to r1 out from the centre c, `width` across, turned `ang` (radians)."""
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
    """A plain round part: k strips as long as the 2k-gon is across, 180/k degrees apart, each a hair shorter than
    the last so no two ends share a plane (k 4: an octagon; 3: a hexagon)."""
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
    w = 2 * r_out * math.tan(math.pi / n)
    out = []
    for i in range(n):
        s = 0.02 if i % 2 else 0.0
        out.append(radial(axis, c, a0 + s, a1 - s, r_in, r_out, w, phase + TAU * i / n, f"{name}{i + 1}", part, tex))
    return out


def pulley(axis, c, a0, a1, r, name, part, rim_tex, hub_tex, hub_r, rim_t=1.6, n=12, arms=6):
    """A spoked wheel: a rim of n boxes, `arms` arms and an octagonal hub standing 0.2 out either side."""
    out = annulus(axis, c, a0, a1, r - rim_t, r, n, f"{name}_rim", part, rim_tex)
    out += [radial(axis, c, a0 + 0.3, a1 - 0.3, hub_r - 0.2, r - rim_t + 0.2, 1.2, TAU * (i + 0.5) / arms, f"{name}_arm{i + 1}", part, rim_tex)
            for i in range(arms)]
    out += disc(axis, c, a0 - 0.2, a1 + 0.2, hub_r, f"{name}_hub", part, hub_tex)
    return out


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


def yz(p, x=0.0):
    """A (y, z) point as a 3D point at x."""
    return (x, p[0], p[1])


def rot2(ang, v):
    """(y, z) turned by ang about +x (+y towards +z)."""
    c, s = math.cos(ang), math.sin(ang)
    return (v[0] * c - v[1] * s, v[0] * s + v[1] * c)


def angle_of(v):
    return math.atan2(v[1], v[0])


def plank(p0, p1, x0, x1, name, part, tex, t, down=None):
    """A board across x0..x1 whose working face runs from p0 to p1 ((y, z)); its thickness t lies on the side
    `down` (a (y, z) direction; by default the lower side)."""
    dy, dz = p1[0] - p0[0], p1[1] - p0[1]
    n = math.hypot(dy, dz)
    nrm = (-dz / n, dy / n)
    if down is None:
        down = (-1.0, 0.0)
    if nrm[0] * down[0] + nrm[1] * down[1] < 0:
        nrm = (-nrm[0], -nrm[1])
    xm = (x0 + x1) / 2
    a = (xm, p0[0] + nrm[0] * t / 2, p0[1] + nrm[1] * t / 2)
    b = (xm, p1[0] + nrm[0] * t / 2, p1[1] + nrm[1] * t / 2)
    return strut(a, b, t, x1 - x0, name, part, tex, axis="x")


def plank_xy(p0, p1, z0, z1, name, part, tex, t):
    """A board across z0..z1 whose top face runs from p0 to p1 ((x, y)); its thickness t below it."""
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    n = math.hypot(dx, dy)
    nrm = (dy / n, -dx / n)
    if nrm[1] > 0:
        nrm = (-nrm[0], -nrm[1])
    zm = (z0 + z1) / 2
    a = (p0[0] + nrm[0] * t / 2, p0[1] + nrm[1] * t / 2, zm)
    b = (p1[0] + nrm[0] * t / 2, p1[1] + nrm[1] * t / 2, zm)
    return strut(a, b, t, z1 - z0, name, part, tex, axis="z")


# ---------------------------------------------------------------- the jaw's linkage (in the breaker's frame)
def _jaw_points():
    fb, ft = FACE_BOT, FACE_TOP
    dy, dz = ft[0] - fb[0], ft[1] - fb[1]
    n = math.hypot(dy, dz)
    d = (dy / n, dz / n)
    back = (-d[1], d[0]) if d[1] > 0 else (d[1], -d[0])       # perpendicular, pointing to the back wall (-u)
    if back[1] > 0:
        back = (-back[0], -back[1])
    bb = (fb[0] + SLAB_T * back[0], fb[1] + SLAB_T * back[1])   # the slab's back line
    s = (TOGGLE_Y - bb[0]) / d[0]
    j = (TOGGLE_Y, bb[1] + s * d[1])                            # the front toggle's seat on the slab's back
    p = (TOGGLE_Y - TOGGLE_SAG, j[1] - math.sqrt(TOGGLE_LF ** 2 - TOGGLE_SAG ** 2))   # the pitman's foot
    b = (TOGGLE_Y, p[1] - math.sqrt(TOGGLE_LB ** 2 - TOGGLE_SAG ** 2))               # the back toggle's seat
    top = (ft[0] + SLAB_T * back[0], ft[1] + SLAB_T * back[1])   # the slab's top back corner
    h = (top[0] + HINGE_UP, top[1])
    return d, back, j, p, b, h


JAW_DIR, JAW_BACK, J0, P0, B0, H0 = _jaw_points()
E_C = (E_Y, P0[1])                           # the eccentric shaft over the pitman's foot
JAW_C = AXLE[1] + P0[1]                      # z = JAW_C - u: the eccentric shaft lands on the power cell's centre line
FRONT_U = FIXED_U + FIXED_T + FRONT_T        # the front of the cheeks and the front wall
BACKWALL_U = (B0[1] - BACK_T, B0[1])         # the back wall: the back toggle seats on its front face
ABUT_U = JAW_C - ABUT_Z[1]                   # the abutments' back face (u): the springs bear on it
ROD_NUT_U = (JAW_C - NUT_Z[1], JAW_C - NUT_Z[0])
LEN_P = math.hypot(E_C[0] - P0[0], E_C[1] - P0[1])
LEN_B = math.hypot(P0[0] - B0[0], P0[1] - B0[1])
LEN_F = math.hypot(P0[0] - J0[0], P0[1] - J0[1])


def ecc_centre(phi):
    """The eccentric's centre at its angle phi (breaker frame, from +y towards +u): drawn at phi 0, up."""
    return (E_C[0] + ECC * math.cos(phi), E_C[1] + ECC * math.sin(phi))


def jaw_point(p, beta):
    v = rot2(beta, (p[0] - H0[0], p[1] - H0[1]))
    return (H0[0] + v[0], H0[1] + v[1])


def solve3(a, b):
    m = [row[:] + [b[i]] for i, row in enumerate(a)]
    for c in range(3):
        piv = max(range(c, 3), key=lambda r: abs(m[r][c]))
        m[c], m[piv] = m[piv], m[c]
        for r in range(3):
            if r != c:
                f = m[r][c] / m[c][c]
                m[r] = [m[r][k] - f * m[c][k] for k in range(4)]
    return [m[i][3] / m[i][i] for i in range(3)]


def jaw_at(phi, guess=None):
    """The linkage at the eccentric's angle phi: the pitman's foot P and the swinging jaw's turn beta about its
    hinge, from the pitman's, the back toggle's and the front toggle's lengths (Newton's method)."""
    t = ecc_centre(phi)
    x = list(guess or (P0[0], P0[1], 0.0))

    def f(v):
        py, pz, be = v
        j = jaw_point(J0, be)
        return [(t[0] - py) ** 2 + (t[1] - pz) ** 2 - LEN_P ** 2,
                (py - B0[0]) ** 2 + (pz - B0[1]) ** 2 - LEN_B ** 2,
                (py - j[0]) ** 2 + (pz - j[1]) ** 2 - LEN_F ** 2]
    for _ in range(80):
        r = f(x)
        if max(abs(v) for v in r) < 1e-12:
            break
        cols = []
        for k in range(3):
            xp = list(x)
            xp[k] += 1e-7
            rp = f(xp)
            cols.append([(rp[i] - r[i]) / 1e-7 for i in range(3)])
        a = [[cols[k][i] for k in range(3)] for i in range(3)]
        dx = solve3(a, [-v for v in r])
        x = [x[i] + dx[i] for i in range(3)]
    return (x[0], x[1]), x[2]


FIT_N = 128
FIT_HARMONICS = 6
MAX_HARMONICS = 4


def _slab_back_u(y):
    bb = (FACE_BOT[0] + SLAB_T * JAW_BACK[0], FACE_BOT[1] + SLAB_T * JAW_BACK[1])
    return bb[1] + (y - bb[0]) / JAW_DIR[0] * JAW_DIR[1]


K0 = (TOGGLE_Y - 1.3, _slab_back_u(TOGGLE_Y - 1.3))   # the tension rods' pin on the swinging jaw's back


def linkage(phi, guess=None):
    p, be = jaw_at(phi, guess)
    t = ecc_centre(phi)
    j = jaw_point(J0, be)
    return {"phi": phi, "p": p, "beta": be, "t": t, "j": j, "k": jaw_point(K0, be),
            "a_p": angle_of((t[0] - p[0], t[1] - p[1])) - angle_of((E_C[0] - P0[0], E_C[1] - P0[1])),
            "a_b": angle_of((p[0] - B0[0], p[1] - B0[1])) - angle_of((P0[0] - B0[0], P0[1] - B0[1])),
            "a_f": angle_of((p[0] - j[0], p[1] - j[1])) - angle_of((P0[0] - J0[0], P0[1] - J0[1]))}


def linkage_samples():
    out, guess = [], None
    for i in range(FIT_N):
        s = linkage(TAU * i / FIT_N, guess)
        guess = (s["p"][0], s["p"][1], s["beta"])
        out.append(s)
    return out


def fit(values):
    """The mean and harmonics (n, amplitude, phase) of a function sampled at even angles of a turn:
    f = mean + sum amplitude sin(n phi + phase)."""
    n_s = len(values)
    mean = sum(values) / n_s
    out = []
    for n in range(1, FIT_HARMONICS + 1):
        a = 2 / n_s * sum(v * math.sin(n * TAU * i / n_s) for i, v in enumerate(values))
        b = 2 / n_s * sum(v * math.cos(n * TAU * i / n_s) for i, v in enumerate(values))
        amp = math.hypot(a, b)
        if amp > 2e-6:
            out.append((n, amp, math.atan2(b, a)))
    return mean, out


SAMPLES = linkage_samples()
FITS = {key: fit([s[key] for s in SAMPLES]) for key in ("beta", "a_p", "a_b", "a_f")}
for _key in ("p", "j", "k"):
    for _i, _c in enumerate("yz"):
        FITS[_key + _c] = fit([s[_key][_i] for s in SAMPLES])
MEAN_P = (FITS["py"][0], FITS["pz"][0])
MEAN_J = (FITS["jy"][0], FITS["jz"][0])
MEAN_K = (FITS["ky"][0], FITS["kz"][0])


def _to_mean(p, angle, pivot, to):
    v = rot2(angle, (p[0] - pivot[0], p[1] - pivot[1]))
    return (to[0] + v[0], to[1] + v[1])


def design_to_mean_pitman(p):
    return _to_mean(p, FITS["a_p"][0], P0, MEAN_P)


def design_to_mean_back(p):
    return _to_mean(p, FITS["a_b"][0], B0, B0)


def design_to_mean_front(p):
    return _to_mean(p, FITS["a_f"][0], J0, MEAN_J)


def design_to_mean_jaw(p):
    return _to_mean(p, FITS["beta"][0], H0, H0)


def placed(els, angle, pivot, to):
    rotate(els, "x", math.degrees(angle), yz(pivot))
    translate(els, [0.0, to[0] - pivot[0], to[1] - pivot[1]])
    return els


def jaw_to_build(p):
    """A breaker-frame point (y, u) in the build frame (y, z)."""
    return (p[0], JAW_C - p[1])


def u_to_z(u):
    return JAW_C - u


MIRROR = [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, -1.0]]


def mirrored(els):
    """Breaker-frame elements into the build frame: z = JAW_C - u. A box mirrored is the same box turned by M r M (M the
    mirror), a proper rotation; its faces are alike, so nothing else changes."""
    for el in els:
        el.c = [el.c[0], el.c[1], JAW_C - el.c[2]]
        el.r = mmul(MIRROR, mmul(el.r, MIRROR))
    return els


JAW_FRONT_Z = u_to_z(FRONT_U)                # the front wall's (and the cheeks') front face
FIXED_Z = u_to_z(FIXED_U)                    # the fixed jaw's face: the mouth's front edge
BACK_Z = u_to_z(BACKWALL_U[0])               # the back wall's back face


# ---------------------------------------------------------------- builders: the stand
def build_stand():
    """The timber stand: four posts, the two front ones tall; side sills along z between them; two cross sills on the
    side sills, under the breaker's front (its front wall and fixed jaw) and back (its back wall), on which the
    cheeks and walls stand; between the front posts the head beam (the grizzly's head block on it) and the top tie
    (the infeed's section against it)."""
    f = "frame"
    out = []
    top = CELLS_Y * B
    for tag, (x0, x1) in zip("we", POST_X):
        out.append(box([x0, 0.0, POST_FZ[0]], [x1, top, POST_FZ[1]], f"fr_post_{tag}f", f, "oak"))
        out.append(box([x0, 0.0, POST_BZ[0]], [x1, CROSS_Y[0], POST_BZ[1]], f"fr_post_{tag}b", f, "oak"))
        sx1 = SIDE_W_X1 if tag == "w" else x1
        out.append(box([x0, SIDE_Y[0], POST_FZ[1]], [sx1, SIDE_Y[1], POST_BZ[0]], f"fr_sill_{tag}", f, "oak"))
    x0, x1 = POST_X[0][0], POST_X[1][1]
    out.append(box([x0, CROSS_Y[0], JAW_FRONT_Z], [x1, CROSS_Y[1], FIXED_Z], "fr_cross_f", f, "oak"))
    out.append(box([x0, CROSS_Y[0], POST_BZ[0]], [x1, CROSS_Y[1], POST_BZ[1]], "fr_cross_b", f, "oak"))
    xi0, xi1 = POST_X[0][1], POST_X[1][0]
    out.append(box([xi0, HEAD_BEAM_Y[0], POST_FZ[0]], [xi1, HEAD_BEAM_Y[1], POST_FZ[1]], "fr_beam_head", f, "oak"))
    out.append(box([xi0, TIE_Y[0], POST_FZ[0]], [xi1, TIE_Y[1], TIE_Z1], "fr_tie_top", f, "oak"))
    return out


# ---------------------------------------------------------------- builders: the breaker (its own frame)
def build_jaw_frame_local():
    """The breaker's cast frame: two cheeks (a base flange, a front plate beside the chamber, a post carrying the
    eccentric shaft's bearing and the hinge's, a back post at the back wall, a lug on its inner face that the
    tension rod's spring bears on), the back wall with the back toggle's seat, the front wall behind the fixed jaw.
    The fixed jaw's plate and the hinge pin are the jaws' (build_jaws_local)."""
    p = "frame"
    out = []
    ey, ez = E_C
    hy, hz = H0
    zb0, zb1 = BACKWALL_U
    zf1 = FRONT_U
    post_u = (ez - 3.0, hz + 1.8)            # the cheek's post (u): under the eccentric's bearing, up to the hinge's
    abut = (ABUT_U, ABUT_U + 1.0)
    for tag, (x0, x1) in (("w", CHEEK_W), ("e", CHEEK_E)):
        bx = (x0 - EBEAR_OUT, x0) if tag == "w" else (x1, x1 + EBEAR_OUT)
        hx = (x0 - 0.5, x1) if tag == "w" else (x0, x1 + 0.5)
        inner = x1 if tag == "w" else x0
        lug = (inner, ROD_X[0] + 0.8) if tag == "w" else (ROD_X[1] - 0.8, inner)
        out += [box([x0, JAW_BASE_Y, zb0], [x1, JAW_BASE_Y + BASE_T, zf1], f"fr_cheek_{tag}_base", p, "iron"),
                box([x0, JAW_BASE_Y + BASE_T, post_u[1]], [x1, JAW_TOP_Y, zf1], f"fr_cheek_{tag}_front", p, "iron"),
                box([x0, JAW_BASE_Y + BASE_T, post_u[0]], [x1, hy - 1.8, post_u[1]], f"fr_cheek_{tag}_post", p, "iron"),
                box([x0, JAW_BASE_Y + BASE_T, zb0], [x1, TOGGLE_Y + 3.0, post_u[0]], f"fr_cheek_{tag}_back", p, "iron"),
                box([bx[0], ey - 2.6, ez - 2.6], [bx[1], ey + 2.6, ez + 2.6], f"fr_cheek_{tag}_ebear", p, "iron"),
                box([hx[0], hy - 1.8, hz - 1.8], [hx[1], hy + 1.8, hz + 1.8], f"fr_cheek_{tag}_hbear", p, "iron"),
                box([lug[0], K0[0] - ABUT_H, abut[0]], [lug[1], K0[0] + ABUT_H, abut[1]], f"fr_cheek_{tag}_lug", p, "iron")]
    out += [box([JAW_X[0], JAW_BASE_Y, zb0], [JAW_X[1], TOGGLE_Y + 3.0, zb1], "fr_backwall", p, "iron"),
            box([JAW_XC - TOGGLE_W / 2, B0[0] - 1.0, zb1], [JAW_XC + TOGGLE_W / 2, B0[0] + 1.0, zb1 + 0.5], "fr_backseat", p, "steel"),
            box([JAW_X[0], JAW_BASE_Y, FIXED_U + FIXED_T], [JAW_X[1], JAW_TOP_Y, zf1], "fr_frontwall", p, "iron")]
    return out


def build_jaws_local():
    """The jaws' fixed half: the fixed jaw's steel plate against the front wall, and the hinge pin through the cheeks'
    hinge bearings."""
    p = "fixedjaw"
    out = [box([JAW_X[0] + 0.2, FACE_BOT[0] - 0.5, FIXED_U], [JAW_X[1] - 0.2, JAW_TOP_Y - 0.5, FIXED_U + FIXED_T], "fixedjaw_plate", p, "steel")]
    out += disc("x", yz(H0), CHEEK_W[0] - 0.9, CHEEK_E[1] + 0.9, 0.8, "fixedjaw_hingepin", p, "steel")
    return out


def build_eshaft_local():
    """The eccentric shaft: its eccentric under the pitman's strap (drawn up), a flywheel outside each cheek, its hub
    against the bearing's boss; at its east end the coupling and the vanilla axle's cross stub out to the power
    face."""
    p = "eshaft"
    c = yz(E_C)
    out = disc("x", c, FLY_W_X[0] - 0.15, COUPLING_X[1] - 0.1, SHAFT_R, "eshaft_shaft", p, "steel")
    out += disc("x", yz((E_C[0] + ECC, E_C[1])), JAW_X[0] + 3.0, JAW_X[1] - 3.0, ECC_R, "eshaft_eccentric", p, "steel", k=4)
    out += pulley("x", c, *FLY_W_X, FLY_R, "eshaft_flyw", p, "iron", "iron", FLY_HUB_R, rim_t=FLY_RIM, n=14, arms=6)
    out += pulley("x", c, *FLY_E_X, FLY_R, "eshaft_flye", p, "iron", "iron", FLY_HUB_R, rim_t=FLY_RIM, n=14, arms=6)
    out += disc("x", c, *COUPLING_X, COUPLING_R, "eshaft_coupling", p, "iron")
    y, u = E_C
    out.append(box([STUB_X[0], y - 1.0, u - 2.0], [STUB_X[1], y + 1.0, u + 2.0], "eshaft_stuba", p, "oak"))
    out.append(box([STUB_X[0] + 0.02, y - 2.0, u - 1.0], [STUB_X[1] - 0.01, y + 2.0, u + 1.0], "eshaft_stubb", p, "oak"))
    return out


FOOT_UP = 1.6                                # the pitman's foot rises this far over its pin: the toggles swing under its body
TOGGLE_TIP = 0.5                             # each toggle stops this far short of the foot's pin, in its seat


def spring_coil(x):
    """The coil spring on the rod at x, in the breaker's frame (its axis along u at the rods' pin height), at the
    linkage's design pose: from the nut's front face to the abutment's back face, its bars' ends on them."""
    probe = Coil((x, K0[0]), SPRING_BORE, SPRING_WIRE, 1.0, 0.0, SPRING_TURNS)
    a, b = ROD_NUT_U[1], ABUT_U
    span = probe.span()
    for _ in range(8):                       # the pitch sets the bars' lean, which sets how far their ends reach
        pitch = (b - a - 2 * span) / SPRING_TURNS
        span = Coil((x, K0[0]), SPRING_BORE, SPRING_WIRE, pitch, 0.0, SPRING_TURNS).span()
    pitch = (b - a - 2 * span) / SPRING_TURNS
    return Coil((x, K0[0]), SPRING_BORE, SPRING_WIRE, pitch, a + span, SPRING_TURNS)


def spring_share(c):
    """The share of the nut's travel the spring's turn c (0 at the nut) takes: by where its middle lies between the
    abutment (none) and the nut (all of it)."""
    coil = spring_coil(ROD_X[0])
    a, b = ROD_NUT_U[1], ABUT_U
    u = coil.z0 + (c + 0.5) * coil.pitch
    return (b - u) / (b - a)


def build_linkage_local():
    """The pitman, the two toggles, the swinging jaw, the tension rods and their springs, drawn at the linkage's
    design pose (the eccentric centred) and each put at its mean pose; the rig's harmonics move them from there."""
    out = []
    (ey, ez), (py, pz) = E_C, P0
    pit = annulus("x", yz(E_C), JAW_X[0] + 3.0, JAW_X[1] - 3.0, STRAP_R[0], STRAP_R[1], 10, "pitman_strap", "pitman", "iron")
    pit += [box([JAW_X[0] + 4.0, py + FOOT_UP, ez - 1.0], [JAW_X[1] - 4.0, ey - STRAP_R[1] + 0.3, ez + 1.0], "pitman_body", "pitman", "iron"),
            box([JAW_X[0] + 3.4, py - 1.0, pz - 1.0], [JAW_X[1] - 3.4, py + FOOT_UP, pz + 1.0], "pitman_foot", "pitman", "iron")]
    out += placed(pit, FITS["a_p"][0], P0, MEAN_P)

    def toggle(a, b, name):
        d = math.dist(a, b)
        tip = (b[0] + (a[0] - b[0]) * TOGGLE_TIP / d, b[1] + (a[1] - b[1]) * TOGGLE_TIP / d)
        return strut(yz(a, JAW_XC), yz(tip, JAW_XC), TOGGLE_T, TOGGLE_W, name, name.split("_")[0], "steel")
    out += placed([toggle(B0, P0, "backtoggle_plate")], FITS["a_b"][0], B0, B0)
    out += placed([toggle(J0, P0, "fronttoggle_plate")], FITS["a_f"][0], J0, MEAN_J)
    bk = JAW_BACK
    sx0, sx1 = JAW_X[0] + 0.3, JAW_X[1] - 0.3
    jaw = [plank(FACE_BOT, FACE_TOP, sx0, sx1, "swingjaw_face", "swingjaw", "steel", t=1.0, down=bk),
           plank((FACE_BOT[0] + bk[0], FACE_BOT[1] + bk[1]), (FACE_TOP[0] + bk[0], FACE_TOP[1] + bk[1]), sx0 + 0.02, sx1 - 0.02,
                 "swingjaw_slab", "swingjaw", "iron", t=SLAB_T - 1.0, down=bk)]
    top = (FACE_TOP[0] + SLAB_T * bk[0], FACE_TOP[1] + SLAB_T * bk[1])
    jaw += disc("x", yz(H0), sx0, sx1, 1.6, "swingjaw_boss", "swingjaw", "iron", k=3)
    jaw.append(strut(yz((top[0] - 0.6, top[1] + 1.0), JAW_XC), yz(H0, JAW_XC), 2.2, sx1 - sx0 - 0.04, "swingjaw_web", "swingjaw", "iron"))
    jaw.append(box([JAW_XC - TOGGLE_W / 2, J0[0] - 1.0, J0[1] - 0.4], [JAW_XC + TOGGLE_W / 2, J0[0] + 1.0, J0[1] + 0.3], "swingjaw_seat", "swingjaw", "steel"))
    for tag, x in zip("we", ROD_X):
        jaw.append(box([x - 0.85, K0[0] - 0.85, K0[1] - 0.3], [x + 0.85, K0[0] + 0.85, _slab_back_u(K0[0]) + 1.4], f"swingjaw_lug{tag}", "swingjaw", "iron"))
    out += placed(jaw, FITS["beta"][0], H0, H0)
    rods = []
    for tag, x in zip("we", ROD_X):
        rods += disc("z", (x, K0[0], 0.0), ROD_NUT_U[0], K0[1] + 0.3, ROD_R, f"rods_{tag}", "rods", "steel", k=3)
        rods += disc("z", (x, K0[0], 0.0), *ROD_NUT_U, 0.95, f"rods_nut{tag}", "rods", "steel", k=3)
        rods += disc("x", yz(K0), x - 0.95, x + 0.95, 0.45, f"rods_pin{tag}", "rods", "steel", k=3)
    translate(rods, [0.0, MEAN_K[0] - K0[0], MEAN_K[1] - K0[1]])
    out += rods
    for c in range(SPRING_TURNS):
        turn = []
        for tag, x in zip("we", ROD_X):
            coil = spring_coil(x)
            for j in range(4 * c, 4 * c + 4):
                turn.append(coil.side(j, f"spring{c + 1}_{tag}{coil.face(j)}", f"spring{c + 1}", "steel", tex_size=TEX))
        sh = spring_share(c)
        translate(turn, [0.0, sh * (MEAN_K[0] - K0[0]), sh * (MEAN_K[1] - K0[1])])
        out += turn
    return out


def build_jaw():
    """The breaker, built in its own frame and mirrored into the build frame."""
    return mirrored(build_jaw_frame_local() + build_jaws_local() + build_eshaft_local() + build_linkage_local())


def build_drive_end():
    """The outboard pedestal at the drive end: an oak post from the ground, an iron pillow block on it round the
    eccentric shaft, between the east flywheel and the coupling."""
    f = "frame"
    y, z = AXLE
    x0, x1 = PED_X
    return [box([x0, 0.0, z - 1.6], [x1, y - 2.6, z + 1.6], "fr_ped_post", f, "oak"),
            box([x0 + 0.2, y - 2.6, z - 2.6], [x1 - 0.2, y + 2.6, z + 2.6], "fr_ped_block", f, "iron")]


# ---------------------------------------------------------------- builders: the grizzly
def griz_top(z):
    """The grizzly bars' top at z (build frame): a straight fall of GRIZ_SLOPE from their head to GRIZ_FOOT."""
    fz, fy = GRIZ_FOOT
    return fy + (fz - z) * math.tan(GRIZ_SLOPE)


def griz_under(z):
    """The bars' underside at z."""
    return griz_top(z) - GRIZ_BAR_T / math.cos(GRIZ_SLOPE)


def throat_under(z):
    """The throat plate's underside at z: it rests on the front wall's front top edge and rises back at THROAT_SLOPE."""
    return JAW_TOP_Y + (z - JAW_FRONT_Z) * math.tan(THROAT_SLOPE)


THROAT_Z = (JAW_FRONT_Z - 1.0, CROSSBAR_Z[0] - 0.2)   # the throat plate (z): past the front wall's face, to the cross bar


def bar_x(k):
    gap = (GRIZ_X[1] - GRIZ_X[0] - GRIZ_BARS * GRIZ_BAR_W) / (GRIZ_BARS - 1)
    x0 = GRIZ_X[0] + k * (GRIZ_BAR_W + gap)
    return x0, x0 + GRIZ_BAR_W


def build_grizzly():
    """The grizzly: iron bars falling from a head block on the head beam to their foot over the breaker's mouth,
    resting there on a cross bar on two brackets on the cheeks' tops; a curb along each side; and a throat plate
    under the bars over the front wall, which sheds the fines that fall there forward, past the front wall's face,
    into the hopper."""
    p = "grizzly"
    z_head, z_foot = GRIZ_HEAD_Z, GRIZ_FOOT[0]
    hz1 = POST_FZ[1]
    out = [box([GRIZ_X[0] - 0.6, HEAD_BEAM_Y[1], z_head - 0.4], [GRIZ_X[1] + 0.6, griz_under(hz1) + 0.08, hz1], "grizzly_head", p, "iron")]
    for k in range(GRIZ_BARS):
        x0, x1 = bar_x(k)
        out.append(plank((griz_top(z_head), z_head), (griz_top(z_foot), z_foot), x0, x1, f"grizzly_bar{k + 1}", p, "iron", t=GRIZ_BAR_T))
    # the curbs: boards along the bars' sides, from their underside to CURB_H over their top
    vert = CURB_H + GRIZ_BAR_T / math.cos(GRIZ_SLOPE)
    for tag, (x0, x1) in zip("we", CURB_X):
        out.append(plank((griz_top(CURB_Z0) + CURB_H, CURB_Z0), (griz_top(z_foot) + CURB_H, z_foot), x0, x1, f"grizzly_curb{tag}", p, "iron",
                         t=vert * math.cos(GRIZ_SLOPE)))
    # the foot: a cross bar under the bars, its top at their underside at its back edge, on two brackets on the cheeks
    cz0, cz1 = CROSSBAR_Z
    cy1 = griz_under(cz1) + 0.05
    cy0 = cy1 - 1.2
    for tag, (x0, x1) in (("w", CHEEK_W), ("e", CHEEK_E)):
        out.append(box([x0 + 0.2, JAW_TOP_Y, cz0 + 0.1], [x1 - 0.2, cy0 + 0.05, cz1 - 0.1], f"grizzly_bracket{tag}", p, "iron"))
    out.append(box([CHEEK_W[0] + 0.2, cy0, cz0], [CHEEK_E[1] - 0.2, cy1, cz1], "grizzly_crossbar", p, "iron"))
    # the throat plate over the front wall, and a lip under its back end standing on the front wall's top
    t = THROAT_T
    za, zb = THROAT_Z
    out.append(plank((throat_under(zb) + t / math.cos(THROAT_SLOPE), zb), (throat_under(za) + t / math.cos(THROAT_SLOPE), za),
                     JAW_X[0] + 0.2, JAW_X[1] - 0.2, "grizzly_throat", p, "iron", t=t))
    lz0, lz1 = zb - 0.9, zb - 0.1
    out.append(box([JAW_X[0] + 0.3, JAW_TOP_Y - 0.5, lz0], [JAW_X[1] - 0.3, throat_under(lz0) + 0.05, lz1], "grizzly_lip", p, "iron"))
    return out


# ---------------------------------------------------------------- builders: the chutes and the hopper
def chute_origin(cell):
    """A cell's corner (build frame, voxels)."""
    return [cell[k] * B for k in range(3)]


def build_chute(name, cell, axis):
    """A chute section: VANILLA_CHUTE's walls, cut to their last CHUTE_LEN along the chute (their UVs cropped alike
    along it, on this model's 64 scale), turned 90 degrees about `axis` through the cell's centre (z: the `ud`
    chute, its mouth on the cell's top face; y: the `ns` chute, its mouth on the cell's north face)."""
    p = "frame"
    o = chute_origin(cell)
    k = TEX / 16
    out = []
    for wall, lo, hi, faces in VANILLA_CHUTE:
        a, b = B - CHUTE_LEN, B                              # along the chute (the game's x)
        f0 = (a - lo[0]) / (hi[0] - lo[0])
        el_lo, el_hi = [a, lo[1], lo[2]], [b, hi[1], hi[2]]
        fs = {}
        for d, (uv, rotation) in faces.items():
            u0, v0, u1, v1 = uv
            if d in ("north", "south", "up", "down"):          # faces whose horizontal runs along the chute: crop it
                if rotation in (90, 270):
                    v0 = v0 + (v1 - v0) * f0
                else:
                    u0 = u0 + (u1 - u0) * f0
            face = {"texture": "#chute", "uv": [round(u0 * k, 4), round(v0 * k, 4), round(u1 * k, 4), round(v1 * k, 4)]}
            if rotation:
                face["rotation"] = rotation
            fs[d] = face
        c = [o[i] + (el_lo[i] + el_hi[i]) / 2 for i in range(3)]
        el = El(f"fr_{name}_{wall.lower()}", [el_hi[i] - el_lo[i] for i in range(3)], c, [r[:] for r in IDENT], fs, p)
        rotate([el], axis, 90.0, [o[0] + 8.0, o[1] + 8.0, o[2] + 8.0])
        out.append(el)
    return out


def hopper_floor_y(x):
    """The hopper's floor's top at x: from the lip up to its top edge."""
    return LIP_Y + (x - 12.0) * (HOPPER_TOP - LIP_Y) / (HOPPER_E - 12.0)


def build_hopper():
    """The hopper, sheet iron, hung under the front cross sill: the trough (the output section's bore carried on back
    under the discharge: its floor, its west wall, a low lip on its east side), the floor falling west from past the
    chamber's east side onto the lip, a strip along that floor's front and back edges, and the plates closing the
    trough's back."""
    f = "frame"
    z0, z1 = HOPPER_Z
    out = [box([5.0, 4.0, z0], [11.0, 5.0, z1], "fr_hopper_trough", f, "sheet"),
           box([4.0, 4.0, z0], [5.0, HOPPER_TOP, z1 + SHEET], "fr_hopper_west", f, "sheet"),
           box([11.0, 4.0, z0], [12.0, LIP_Y, z1], "fr_hopper_lip", f, "sheet"),
           box([5.0, 4.0, z1], [12.0, HOPPER_TOP, z1 + SHEET], "fr_hopper_back", f, "sheet")]
    top = (HOPPER_E, HOPPER_TOP)
    out.append(plank_xy((12.0, LIP_Y), top, z0, z1, "fr_hopper_floor", f, "sheet", t=SHEET))
    # the strips: boards parallel to the floor, from its underside to SIDE_H over its top, at its front and back ends
    for tag, (za, zb) in (("f", (z0, z0 + SHEET)), ("b", (z1, z1 + SHEET))):
        out.append(plank_xy((12.0, LIP_Y + SIDE_H), (HOPPER_E, HOPPER_TOP + SIDE_H), za, zb, f"fr_hopper_strip{tag}", f, "sheet",
                            t=SIDE_H + SHEET))
    return out


def build():
    els = build_stand() + build_jaw() + build_drive_end() + build_grizzly() + build_hopper()
    for name, cell, axis in CHUTES:
        els += build_chute(name, cell, axis)
    return els


# ---------------------------------------------------------------- the stages
STAGES = ("jaws", "eccentric", "toggles", "grizzly")   # the requires values, in the order they could be fitted
BUILD_STATES = ("frame",) + STAGES           # a build state: the frame and every stage up to that one


def fitted_in(state):
    return STAGES[:STAGES.index(state) + 1] if state in STAGES else ()


def in_state(requires, state):
    """Whether a part with this `requires` is drawn in a build state ("frame", or the last stage fitted)."""
    return requires is None or requires in fitted_in(state)


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def harmonics(key, motion, axis, pivot=None, scale=1.0):
    """The rig drivers for one of the linkage's fits, mirrored into the build frame: one swing or slide per
    harmonic, on the axle's angle. In the breaker's frame the eccentric turns JAW_E_RATIO a radian of the axle;
    mirrored (z = JAW_C - u) a turn about x changes sign, as does a slide along z."""
    out = []
    flip = -1.0 if (motion == "swing" or axis == "z") else 1.0
    for n, a, p in FITS[key][1][:MAX_HARMONICS]:
        d = {"type": motion, "axis": axis}
        if pivot is not None:
            d["pivot"] = pt(0.0, *jaw_to_build(pivot))
        d.update({"amplitude": r6(flip * a * scale / (B if motion == "slide" else 1.0)), "ratio": r6(n * JAW_E_RATIO), "phase": r6(p)})
        out.append(d)
    return out


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    parts = [
        {"id": "eshaft", "match": ["eshaft_*"], "requires": "eccentric",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *AXLE), "ratio": r6(-JAW_E_RATIO)}]},
        {"id": "pitman", "match": ["pitman_*"], "requires": "eccentric",
         "drivers": harmonics("a_p", "swing", "x", MEAN_P) + harmonics("py", "slide", "y") + harmonics("pz", "slide", "z")},
        {"id": "backtoggle", "match": ["backtoggle_*"], "requires": "toggles", "drivers": harmonics("a_b", "swing", "x", B0)},
        {"id": "fronttoggle", "match": ["fronttoggle_*"], "requires": "toggles",
         "drivers": harmonics("a_f", "swing", "x", MEAN_J) + harmonics("jy", "slide", "y") + harmonics("jz", "slide", "z")},
        {"id": "swingjaw", "match": ["swingjaw_*"], "requires": "jaws", "drivers": harmonics("beta", "swing", "x", H0)},
        {"id": "rods", "match": ["rods_*"], "requires": "toggles", "drivers": harmonics("ky", "slide", "y") + harmonics("kz", "slide", "z")},
    ]
    for c in range(SPRING_TURNS):
        parts.append({"id": f"spring{c + 1}", "match": [f"spring{c + 1}_*"], "requires": "toggles",
                      "drivers": harmonics("ky", "slide", "y", scale=spring_share(c)) + harmonics("kz", "slide", "z", scale=spring_share(c))})
    parts += [
        {"id": "fixedjaw", "match": ["fixedjaw_*"], "requires": "jaws", "drivers": []},
        {"id": "grizzly", "match": ["grizzly_*"], "requires": "grizzly", "drivers": []},
        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses
REST = (0.0,)                                # (theta,): the authored pose's input


def inputs_of(pose):
    return {"theta": pose[0]}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


def at_ecc(phi):
    """A pose with the eccentric turned phi radians (the breaker's frame)."""
    return (phi / JAW_E_RATIO,)


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("infeed", "output")
CELL_ANCHORS = (("power", POWER_CELL, POWER_FACE), ("infeed", INFEED_CELL, INFEED_FACE), ("output", OUTPUT_CELL, OUTPUT_FACE))


def anchor_points():
    """The anchors' points (build frame, voxels): the infeed chute's mouth (the infeed cell's top face's centre, on a
    vanilla chute's axis) and the output chute's (the output cell's front face's centre)."""
    return {"infeed": (INFEED_CELL[0] * B + 8.0, (INFEED_CELL[1] + 1) * B, INFEED_CELL[2] * B + 8.0),
            "output": (OUTPUT_CELL[0] * B + 8.0, OUTPUT_CELL[1] * B + 8.0, OUTPUT_CELL[2] * B)}


def make_rig(parts):
    rig = {"_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the west column's front "
                       "bottom cell, where the output chute leaves. One machine, one level: the frame's parts have no requires; "
                       "the stages fit jaws, eccentric, toggles and grizzly (stages). theta is the axle's angle: the eccentric "
                       "shaft turns with it, ratio 1, and the linkage follows as harmonics of it, the same either way round. "
                       "No work. See the jaw breaker's README for the schema.",
           "cells": []}
    for name, cell, face in CELL_ANCHORS:
        rig[f"{name}Cell"] = list(cell)
        rig[f"{name}Face"] = face
    for key, p in anchor_points().items():
        rig[key] = {"pos": pt(*p)}
    rig["breaker"] = {"turnsPerBite": r6(1.0 / abs(JAW_E_RATIO)),
                      "_comment": "Axle turns per turn of the eccentric shaft (coupled straight on): the swinging jaw closes "
                                  "once a turn, either way round."}
    rig["stages"] = list(STAGES)
    rig["parts"] = parts
    return rig


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: the build frame's corner is the
    controller's)."""
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
    for name, _, _ in CELL_ANCHORS:
        ship[f"{name}Cell"] = shift_cell(rig[f"{name}Cell"], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, everything fitted, posed at rest by the shipped rig; a cell
    with none is hollow. No lids: the top row is the grizzly's and the infeed's, and a deck at the top would be an
    invisible floor over open space."""
    written = flatten(shape["elements"], textures={})
    by_cell = {}
    for w in written:
        el = posed(w, _part_matrix(ship_parts, part_of(ship_parts, w.name), inputs_of(REST)))
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
    poses = [REST, (0.3,), (-2.0,), at_ecc(1.1), at_ecc(2.3), (31.0,)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    span = [(min(c[k] for c in cells), max(c[k] for c in cells)) for k in range(3)]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells x {span[0]}, y {span[1]}, "
          f"z {span[2]}; power {ship['powerCell']} {ship['powerFace']}")
    if worst > 1e-6 or (0, 0, 0) not in cells or len(cells) != CELLS_X * CELLS_Y * CELLS_Z:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
def reference_poses():
    """theta: the axle at rest, either way, through a turn of the eccentric every 22.5 degrees, and a long run."""
    out = [0.0, 1.1, -2.3, 2.9, -5.4, 7.3, -13.9, 25.1, 50.27, -101.0]
    out += [r6(TAU * k / 16) for k in range(1, 16)]
    return out


def reference_json(ship_parts):
    poses = []
    for th in reference_poses():
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], {"theta": th})) for q in ship_parts}
        poses.append({"theta": th, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT}; do not edit. Every jaw breaker part's matrix (blocks, first three rows) at a "
                        "spread of axle angles (theta), from the generator's reference maths (Machines/tools/machinegen/rigmath.py). "
                        "The site's, the mod's and the Python tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod; the chute sections are the game's "
             "straight chute's walls. Every stage's parts are in this shape; the rig's requires say which stage each "
             "belongs to. Keep element names when editing: the rig finds its parts by them.", TEXTURES, tex_size=TEX)


def coplanar_poses():
    """(theta, state): the machine at each build state at rest, and complete through its turn, so faces that never
    show together are never compared."""
    return (REST + ("grizzly",), at_ecc(0.9) + ("grizzly",), at_ecc(2.3) + ("grizzly",), at_ecc(4.1) + ("grizzly",),
            at_ecc(5.5) + ("grizzly",), REST + ("frame",), REST + ("jaws",), at_ecc(1.6) + ("eccentric",), at_ecc(3.3) + ("toggles",))


REQUIRES = {}


def shown(posed_els, pose):
    """The posed elements of the pose's build state; the rest moved far away (copies; the order kept), so the
    z-fighting fix and its check deal only with faces that show together."""
    state = pose[1] if len(pose) > 1 else STAGES[-1]
    out = []
    for e in posed_els:
        if not in_state(REQUIRES[e.part], state):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def fix_coplanar(els, parts):
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose), coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the jaw breaker's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_jawbreaker
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    REQUIRES.update({p["id"]: p["requires"] for p in parts})
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        for pose, pairs in before.items():
            print(f"coplanar faces before the fix at {pose}: {len(pairs)} pairs")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_jawbreaker.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "jawbreaker.json", args.out / "jawbreaker_frame.json", args.out / "jawbreaker-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "jawbreaker.json", SHAPE_DIR / "jawbreaker_frame.json", RIG_DIR / "jawbreaker-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_jawbreaker.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
