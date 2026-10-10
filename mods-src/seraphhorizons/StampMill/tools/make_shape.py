#!/usr/bin/env python3
"""Generate the stamp mill's shapes, rig and reference poses.

The stamp mill crushes and classifies in one machine (#728), upgraded in place (#711): a timber battery frame
whose footprint is its final (mill level) size from the start, and the parts each level fits into it. A level
adds parts; it never takes the earlier ones off.

    frame    the battery's two end frames, its guide girts and ties; the power train (the entry shaft from the
             vanilla axle into a closed rectifier gear case, and the line shaft along the back of the machine)
    level 1  (water) a five-stamp battery: the mortar box on its block, with its dies, the mesh discharge
             screen in its front wall, the apron and launder out to the north face, the feed box at its back,
             where the player shovels ore (`mortar`); the water line from the west face into the feed box, ppex's
             straight pipe soldered in copper or lead (`pipecopper`, `pipelead`); the camshaft with ten
             involute cams and the bull pulley, belted from the line shaft (`camshaft`); five stamps (`stamps`)
    level 2  (mill) a Blake jaw rock breaker in front of the battery, on its own stand, belted from the same line
             shaft, its discharge falling on the feed box's floor, which is the chute into the battery (`jaw`);
             over its mouth a grizzly, fed by chute from the top face: the fines fall through its bars into the
             feed box, the oversize slides off their foot into the breaker (`grizzly`)

Everything is built here from plain boxes; no other mod's model is used. It writes, deterministically,

    stampmill.json         the whole machine, every level's parts          (assets/.../shapes/block/)
    stampmill_frame.json   the static frame only (block and item)          (assets/.../shapes/block/)
    stampmill-rig.json     cells, anchors and the part rig                 (assets/.../config/)
    rig-reference.json     every part's matrix at a grid of poses          (tests/StampMill/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_stampmill.py) and exits non-zero if
a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the machine
box's north-west bottom corner (the "build frame"); the controller cell is the bottom middle cell of the north
face, ORIGIN_CELL, so the shipped files are the build frame moved by -ORIGIN_CELL blocks.

The rig's inputs: theta, the axle's angle (the entry shaft), and psi, its travel either way, which everything
behind the rectifier follows: the line shaft, the camshaft, the stamps (each a sum of harmonics of the camshaft's
turn: lifted by its cam, thrown, falling and resting on its die) and the jaw's linkage. The rig has no work.
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

import solderedpipe  # noqa: E402

from machinegen.checks import cell_boxes, cells_touched, with_lids  # noqa: E402
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
REFERENCE_OUT = MOD / "tests" / "StampMill" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/StampMill/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

# ---------------------------------------------------------------- the box, the cells, the anchors
CELLS_X, CELLS_Y, CELLS_Z = 3, 4, 4          # the mill level's size, fixed from the frame on
ORIGIN_CELL = (1, 0, 0)                      # the controller: the bottom middle cell of the north face, over the launder's spout
POWER_CELL, POWER_FACE = (2, 0, 2), "east"   # the vanilla axle, along x at (y 8, z 40), into the gear case
WATER_CELL, WATER_FACE = (0, 1, 2), "west"   # the ppex pipe, along x at (y 24, z 40), the water line into the feed box
FEED_CELL, FEED_FACE = (1, 1, 1), "up"       # the feed box's open top: the player shovels ore in here (level 1 on)
INFEED_CELL, INFEED_FACE = (1, 3, 1), "up"   # the grizzly's head: ore comes in here by chute (level 2)
OUTPUT_CELL, OUTPUT_FACE = (1, 0, 0), "north"   # the launder's spout: the pulp goes out here

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "planks": "game:block/wood/planks/oak1",
    "iron": "game:block/metal/plate/iron",
    "steel": "game:block/metal/sheet-plain/steel1",
    # the water line wears ppex's pipe textures as UnifiedPipes gives them, as the rosser's drip pipes do
    **{f"pipe{m}": f"game:block/metal/sheet-plain/{m}4" for m in ("copper", "lead")},
    "solder": solderedpipe.SOLDER_TEXTURE,
    "mesh": "game:block/metal/mesh1",
    "leather": "game:block/leather/plain",
    "rubber": "game:block/leather/black",
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the frame (oak): two end frames
END_W, END_E = (3.5, 7.5), (40.5, 44.5)      # the end frames (x): sill, two posts, rails and a cap each
FRAME_Z = (1.0, 23.0)                        # their sills' and caps' length (z)
POST_F, POST_B = (3.0, 7.0), (18.0, 22.0)    # their front and back posts (z)
SILL_Y, CAP_Y = (0.0, 4.0), (60.0, 64.0)
RAIL_IN = 0.5                                # rails stand in from the posts' faces
RAIL_LO_Y = (19.0, 23.0)                     # carries the lower guide girts
RAIL_HI_Y = (42.0, 46.0)                     # carries the upper guide girts
TIE_B_Y = (56.0, 60.0)                       # the back tie, between the back posts: the grizzly's head rests on it
PLANK = 1.0                                  # board thickness: launder, feed box, guides' strips

# ---------------------------------------------------------------- level 1: the stamps
STAMPS = 5
STEM_X0, STEM_PITCH = 12.4, 5.8              # the stems' x
STEM_Z = 16.0                                # the stems' line (z)
STEM_R = 0.8                                 # octagons, by apothem
SHOE_R, HEAD_R = 2.3, 2.4
DIE_TOP = 8.5                                # the dies' top: the shoes rest on them
SHOE_T, HEAD_T = 4.0, 4.5
STEM_TOP = 54.0                              # at rest
TAPPET_H, TAPPET_T = 2.2, 4.0                # the tappet: a square collar 4.4 across, 4 high
LIFT = 4.0                                   # each stamp's lift on its cam
ORDER = (1, 3, 5, 2, 4)                      # the firing order: ten drops a camshaft turn, evenly spaced

# the cams: involutes of a base circle CAM_RB, beside each stem, lifting the tappet's north edge
CAM_RB = 3.5                                 # the cams' base circle: the involute lifts the tappet CAM_RB a radian
CAM_T0 = 0.4                                 # the involute's roll at pick-up: the tappet rests CAM_RB * CAM_T0 over the camshaft
CAM_T1 = CAM_T0 + LIFT / CAM_RB              # its roll at the tip, where the tappet is let go
CAM_EDGE = 0.25                              # the contact line stands this far inside the tappet's north edge
CAM_X = (1.0, 2.1)                           # each cam's plate east of its stem's axis: it sweeps past the stem
CAM_ARM = 1.2                                # an arm's depth behind its working face
CAM_HUB_R = 2.6
CAM_SEGS = 4                                 # chords an arm
CAM_ROOT = 0.25                              # an arm's working face starts at this roll; a web joins its root to the hub
CAM_Y = 34.0
CAM_Z = STEM_Z - (TAPPET_H - CAM_EDGE + CAM_RB)   # the camshaft, north of the stems
CAM_C = (CAM_Y, CAM_Z)
CAM_TIP = CAM_RB * math.hypot(1.0, CAM_T1)   # the cams' reach from the camshaft
TAPPET_Y0 = CAM_Y + CAM_RB * CAM_T0          # the tappet's underside, the stamp resting on its die
SHAFT_R = 1.5

# the stamp's lift through a period (half a camshaft turn: double cams): the cam's rise, then thrown and falling
# freely onto its die (FALL radians of the camshaft), then resting; smoothed by a Gaussian of SMOOTH radians (the
# cam's pick-up and the shoe's landing on the ore bed), drawn by its first HARMONICS harmonics
FALL = 0.9
SMOOTH = 0.06
HARMONICS = 10
PROFILE_SAMPLES = 2048

# ---------------------------------------------------------------- level 1: the mortar, launder, feed box, water
MORTAR_X = (8.0, 40.0)
MORTAR_Z = (10.5, 21.5)
MORTAR_Y = (3.0, 16.5)                       # on the mortar block (oak, y 0..3): low, so the heads show over its rim
MORTAR_WALL = 1.5
MORTAR_FLOOR = 6.0                           # the floor's top; the dies stand on it
SCREEN_Y = (10.0, 14.5)                      # the screen in the front wall
FEED_LIP_Y = 14.0                            # the feed opening in the back wall, over the lip
FEED_X = (18.0, 30.0)                        # the feed box, between the jaw stand's sills
FEED_SLOPE = math.radians(40.0)              # its floor's fall towards the lip
FEED_END_Z = 34.8                            # its south end: under the breaker's discharge
FEED_SIDE = 3.2                              # its sides' height over the floor
LAUNDER_Z = (1.0, 5.0)
LAUNDER_Y = (3.6, 8.4)                       # its floor's underside, its sides' top
SPOUT_X = (21.0, 27.0)
# The water line: Pipes and Power Expanded's straight pipe in copper or lead, drawn as UnifiedPipes draws those
# pipes, soldered (Pipes/tools/solderedpipe.py), as the rosser's drip pipes are: ppex's tube round its bore, a
# hub where two lengths meet. From the west face's inlet it runs east under the breaker's west flywheel, north
# beside the stand to the west end frame's back post (which it bears against), east along the battery's back
# and down into the feed box over the lip. The part is drawn in each metal (`pipecopper`, `pipelead`); a renderer
# draws the fitted metal's.
PIPE_METALS = ("copper", "lead")
PIPE_HALF = solderedpipe.HALF
WATER_Y, WATER_Z = 24.0, 40.0                # the inlet's axis: the water cell's centre, on the ppex pipe beyond the face
PIPE_X1 = END_W[1] + PIPE_HALF               # the north run (x): against the west end frame's back post
PIPE_Z2 = 21.4                               # the east run (z): between the guide girts (18) and the feed box's west side
PIPE_X3 = 22.0                               # the drop into the feed box (x)
PIPE_END_Y = 18.5                            # the drop's open end, over the feed lip

# ---------------------------------------------------------------- the power train (the frame's)
ENTRY = (8.0, 40.0)                          # (y, z): the entry shaft and the line shaft (the power cell's centre)
LINE = ENTRY
LINE_R = 1.4
GEARCASE_X, GEARCASE_Y, GEARCASE_Z = (42.0, 46.5), (0.0, 15.0), (35.0, 45.0)
PED_W_X, PED_M_X = (3.5, 6.5), (22.0, 26.0)  # the line shaft's pedestals (x)
BULL_X = (0.5, 3.0)                          # the drive plane: the bull pulley and its belt, outside the west end frame
BULL_R = 9.4                                 # the bull pulley on the camshaft
LINE_PULLEY_R = 4.7                          # the line shaft's pulley under it
JAWDRIVE_X = (35.5, 38.0)                    # the breaker's drive plane: its pulley-flywheel and the line's pulley
JAWDRIVE_R = 6.0                             # the line shaft's pulley for the breaker
BELT_T, BELT_IN = 0.8, 0.35                  # a belt's thickness, and how far its edges stand in from the pulley's faces

LINE_RATIO = -1.0                            # the line shaft per radian of the axle's travel (the rectifier: one way)
CAM_RATIO = LINE_RATIO * LINE_PULLEY_R / BULL_R   # the camshaft (an open belt: the same sense)
TURNS_PER_REV = abs(1.0 / CAM_RATIO)         # axle turns a camshaft turn

# ---------------------------------------------------------------- level 2: the Blake jaw breaker
# Built in the breaker's own frame, the crusher's (#729's first model) as it was: y as there, u along the flow from
# the back wall (u 0) to the fixed jaw's front (u 37.6). It is then mirrored and raised into the build frame,
# z = JAW_C - u and y + JAW_DY, so its fixed jaw faces the battery and its discharge falls on the feed box.
JAW_C = 64.0
JAW_DY = 5.0
JAW_X = (18.0, 30.0)                         # the chamber (x) between the cheeks
JAW_XC = 24.0
CHEEK_W, CHEEK_E = (15.0, 18.0), (30.0, 33.0)
JAW_BASE_Y = 24.0                            # (breaker frame) the cheeks stand on the stand's sills
FIXED_U = 33.0                               # the fixed jaw's face
FRONT_U = 37.6                               # the front of the cheeks and the front wall
FACE_BOT, FACE_TOP = (26.5, 30.0), (41.5, 23.5)   # (y, u): the swinging jaw's face, a gap of 3 at the bottom
SLAB_T = 3.5
TOGGLE_Y = 26.5
TOGGLE_SAG = 2.0
TOGGLE_L = 10.0
ECC = 1.2                                    # the eccentric's throw
E_Y = 34.538                                 # the eccentric shaft's height (the pitman's length over its foot)
HINGE_UP = 1.6
FLY_R = 8.0                                  # the two flywheels; the east one is the belt pulley
FLY_W_X = (10.0, 12.5)
FLY_E_X = JAWDRIVE_X
ROD_X = (19.6, 28.4)                         # the two tension rods, either side of the toggles
ROD_R = 0.5
ROD_NUT_U = (0.7, 1.4)
BUFFERS = 2                                  # india-rubber buffers on each rod, between its nut and the back wall
TOGGLE_W = 6.0                               # the toggles' width (x)
JAW_E_RATIO = -LINE_RATIO * JAWDRIVE_R / FLY_R   # the eccentric shaft in the breaker's frame (mirrored: the line's sense)
STAND_X = ((14.0, 18.0), (30.0, 34.0))       # the stand's sills and posts (x), under the cheeks
STAND_Y = (25.0, 29.0)                       # its sills (y)
STAND_POST_Z = ((27.0, 31.0), (56.0, 60.0))

# ---------------------------------------------------------------- level 2: the grizzly
GRIZ_X = (18.6, 29.4)
GRIZ_BARS = 5
GRIZ_BAR_W = 1.2
GRIZ_HEAD = (21.0, 63.0)                     # (z, y): the bars' top at their head, on the head block on the back tie
GRIZ_FOOT = (32.0, 53.0)                     # (z, y): their top at their foot, over the breaker's mouth
GRIZ_BAR_T = 1.4
CROSSBAR_Z = (31.0, 32.6)                    # the cross bar under the bars' foot
JAW_TOP_Y = 42.5 + JAW_DY                    # the cheeks' and the front wall's top
JAW_FRONT_Z = JAW_C - FRONT_U                # the front wall's north face
THROAT_SLOPE = math.radians(40.0)            # the throat plate's fall to the north
THROAT_Z = (JAW_FRONT_Z - 1.0, 30.4)         # its extent (z): past the front wall's face, to over the fixed jaw
THROAT_T = 0.8


def stem_x(i):
    return STEM_X0 + STEM_PITCH * i


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


def plank(p0, p1, x0, x1, name, part, tex="planks", t=PLANK, down=None):
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


# ---------------------------------------------------------------- belts
def tangents(c1, r1, c2, r2):
    """The open belt's two straight runs between circles (y, z) c1, r1 and c2, r2: for each, the angle of the
    radius to its tangent points (the same on both circles) and the points."""
    d = math.hypot(c2[0] - c1[0], c2[1] - c1[1])
    alpha = math.atan2(c2[1] - c1[1], c2[0] - c1[0])
    beta = math.acos((r1 - r2) / d)
    out = []
    for s in (1.0, -1.0):
        g = alpha + s * beta
        e = (math.cos(g), math.sin(g))
        out.append((g, (c1[0] + r1 * e[0], c1[1] + r1 * e[1]), (c2[0] + r2 * e[0], c2[1] + r2 * e[1])))
    return alpha, beta, out


def belt(c1, r1, c2, r2, x0, x1, name, part, seg_deg=36.0):
    """An open leather belt on two pulleys (rims of radius r1 at c1 and r2 at c2, (y, z)) in the plane x0..x1: its
    two straight runs, tangent to both rims, and its wraps round each rim as segments (a belt looks alike as it
    runs, so it is drawn still). Alternate pieces stand in 0.02 at their edges, so no two share a side plane."""
    rc1, rc2 = r1 + BELT_T / 2, r2 + BELT_T / 2           # the belt's centre line
    alpha, beta, runs = tangents(c1, rc1, c2, rc2)
    a0, a1 = x0 + BELT_IN, x1 - BELT_IN
    out = []
    for k, (g, p, q) in enumerate(runs):
        s = 0.02 if k % 2 else 0.0
        out.append(strut(yz(p, (a0 + a1) / 2), yz(q, (a0 + a1) / 2), BELT_T, a1 - a0 - 2 * s, f"{name}_run{k + 1}", part, "leather"))
    # the wraps: round c1 from alpha + beta the long way (through alpha + pi) to alpha - beta; round c2 from
    # alpha - beta through alpha to alpha + beta
    for tag, c, r, g0, sweep in (("a", c1, r1, alpha + beta, TAU - 2 * beta), ("b", c2, r2, alpha - beta, 2 * beta)):
        n = max(2, math.ceil(math.degrees(sweep) / seg_deg))
        dg = sweep / n
        w = 2 * (r + BELT_T) * math.tan(dg / 2) + 0.05
        for i in range(n):
            s = 0.02 if (i + 1) % 2 else 0.04
            ang = g0 + dg * (i + 0.5)
            # radial() turns +y towards +z: a box first along +y, turned by ang
            out.append(radial("x", yz(c), a0 + s, a1 - s, r, r + BELT_T, w, ang, f"{name}_wrap{tag}{i + 1}", part, "leather"))
    return out


# ---------------------------------------------------------------- the stamp's lift
def _target(x):
    """The stamp's lift (voxels) at x radians of the camshaft into its period (pi): the cam's rise at CAM_RB a radian
    from 0, let go at LIFT, thrown and falling freely onto its die over FALL, then resting."""
    x %= math.pi
    tr = LIFT / CAM_RB
    if x <= tr:
        return CAM_RB * x
    t = x - tr
    g = 2 * (LIFT + CAM_RB * FALL) / FALL ** 2
    return LIFT + CAM_RB * t - g * t * t / 2 if t <= FALL else 0.0


def _profile():
    """The lift's harmonics: (mean, [(n, sin coefficient, cos coefficient)]) of the period pi, each harmonic scaled by
    the Gaussian smoothing's transfer, exp(-(2 n SMOOTH)^2 / 2); and the series' least value."""
    m = PROFILE_SAMPLES
    vals = [_target(math.pi * k / m) for k in range(m)]
    a0 = sum(vals) / m
    out = []
    for n in range(1, HARMONICS + 1):
        s = 2 / m * sum(v * math.sin(2 * n * math.pi * k / m) for k, v in enumerate(vals))
        c = 2 / m * sum(v * math.cos(2 * n * math.pi * k / m) for k, v in enumerate(vals))
        w = math.exp(-((2 * n * SMOOTH) ** 2) / 2)
        out.append((n, s * w, c * w))
    lo = min(a0 + sum(s * math.sin(2 * n * math.pi * k / m) + c * math.cos(2 * n * math.pi * k / m) for n, s, c in out)
             for k in range(m))
    return a0, out, lo


PROFILE_MEAN, PROFILE_H, PROFILE_LO = _profile()


def lift_of(x):
    """The stamp's drawn lift (voxels, 0 its least) at x radians of the camshaft into its period."""
    return PROFILE_MEAN - PROFILE_LO + sum(s * math.sin(2 * n * x) + c * math.cos(2 * n * x) for n, s, c in PROFILE_H)


def _cam_shave():
    """How far the cams' working faces are drawn inside the true involute so the tappet, which follows the drawn
    lift, never sinks into its cam: the most the drawn lift falls short of the cam's rise, and 0.02 more."""
    tr = LIFT / CAM_RB
    worst = max(CAM_RB * (tr * k / 400) - lift_of(tr * k / 400) for k in range(401))
    return max(0.0, worst) + 0.02


CAM_SHAVE = _cam_shave()
DRAWN_LIFT = PROFILE_MEAN - PROFILE_LO        # the stamps are drawn this far over their rest: the harmonics' mean


def stamp_phase(i):
    """Stamp i's (0-based) first pick-up, in radians of the camshaft: the ten drops a turn evenly spaced in the
    firing order."""
    return ORDER.index(i + 1) * math.pi / 5


def cam_angle(psi):
    """The camshaft's working angle (radians, growing as it lifts) after the axle's travel psi."""
    return abs(CAM_RATIO) * psi


def stamp_lift(i, psi):
    return lift_of(cam_angle(psi) - stamp_phase(i))


def cam_profile(i, lobe, t):
    """A point (y, z, relative to the camshaft) of stamp i's cam, lobe 0 or 1, at the involute's roll t, as the cam
    is drawn (psi 0): the point that meets the tappet's underside, CAM_RB * t over the camshaft on the contact line
    CAM_RB south of it, when its lobe has turned that far. The camshaft turns negatively about x (its south side
    rises), so the drawn point is the contact turned back, positively."""
    phi = stamp_phase(i) + math.pi * lobe + t - CAM_T0
    return rot2(phi, (CAM_RB * t, CAM_RB))


def cam_normal(i, lobe, t):
    phi = stamp_phase(i) + math.pi * lobe + t - CAM_T0
    return rot2(phi, (1.0, 0.0))


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
    reach = math.sqrt(TOGGLE_L ** 2 - TOGGLE_SAG ** 2)
    p = (TOGGLE_Y - TOGGLE_SAG, j[1] - reach)                   # the pitman's foot
    b = (TOGGLE_Y, p[1] - reach)                                # the back toggle's seat on the back wall
    top = (ft[0] + SLAB_T * back[0], ft[1] + SLAB_T * back[1])   # the slab's top back corner
    h = (top[0] + HINGE_UP, top[1])
    return d, back, j, p, b, h


JAW_DIR, JAW_BACK, J0, P0, B0, H0 = _jaw_points()
E_C = (E_Y, P0[1])                           # the eccentric shaft over the pitman's foot
BACKWALL_U = (B0[1] - 3.0, B0[1])            # the back wall: the back toggle seats on its front face
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


K0 = (TOGGLE_Y - 1.4, _slab_back_u(TOGGLE_Y - 1.4))   # the tension rods' pin on the swinging jaw's back


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
    return (p[0] + JAW_DY, JAW_C - p[1])


MIRROR = [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, -1.0]]


def mirrored(els):
    """Breaker-frame elements into the build frame: z = JAW_C - u, y + JAW_DY. A box mirrored is the same box turned
    by M r M (M the mirror), a proper rotation; its faces are alike, so nothing else changes."""
    for el in els:
        el.c = [el.c[0], el.c[1] + JAW_DY, JAW_C - el.c[2]]
        el.r = mmul(MIRROR, mmul(el.r, MIRROR))
    return els


# ---------------------------------------------------------------- builders: the frame
def build_frame():
    f = "frame"
    out = []
    z0, z1 = FRAME_Z
    for tag, (x0, x1) in (("w", END_W), ("e", END_E)):
        out.append(box([x0, SILL_Y[0], z0], [x1, SILL_Y[1], z1], f"fr_sill_{tag}", f, "oak"))
        out.append(box([x0, CAP_Y[0], z0], [x1, CAP_Y[1], z1], f"fr_cap_{tag}", f, "oak"))
        for i, (pz0, pz1) in enumerate((POST_F, POST_B), 1):
            out.append(box([x0, SILL_Y[1], pz0], [x1, CAP_Y[0], pz1], f"fr_post_{tag}{i}", f, "oak"))
        for name, (y0, y1) in (("lo", RAIL_LO_Y), ("cam", (CAM_Y - 2.0 - 3.5, CAM_Y - 2.0)), ("hi", RAIL_HI_Y)):
            out.append(box([x0 + RAIL_IN, y0, POST_F[1]], [x1 - RAIL_IN, y1, POST_B[0]], f"fr_rail{name}_{tag}", f, "oak"))
    out.append(box([END_W[1], CAP_Y[0], POST_F[0]], [END_E[0], CAP_Y[1], POST_F[1]], "fr_tie_f", f, "oak"))
    out.append(box([END_W[1], TIE_B_Y[0], POST_B[0]], [END_E[0], TIE_B_Y[1], POST_B[1]], "fr_tie_b", f, "oak"))
    out += build_guides()
    out += build_power_frame()
    return out


def build_guides():
    """The guide girts, oak, on the rails: each two strips either side of the stems' line, the stems in the slot
    between them."""
    out = []
    hole = STEM_R                            # the stems run against both strips
    xa, xb = END_W[0] + RAIL_IN, END_E[1] - RAIL_IN
    for tag, (y0, y1) in (("lo", (RAIL_LO_Y[1], RAIL_LO_Y[1] + 3.0)), ("hi", (RAIL_HI_Y[1], RAIL_HI_Y[1] + 3.0))):
        out += [box([xa, y0, STEM_Z - 2.6], [xb, y1, STEM_Z - hole], f"fr_guide{tag}_f", "frame", "oak"),
                box([xa + 0.02, y0, STEM_Z + hole], [xb - 0.02, y1, STEM_Z + 2.0], f"fr_guide{tag}_b", "frame", "oak")]
    return out


def build_power_frame():
    """The power train's fixed parts: the rectifier's gear case at the power cell (its gears are inside it, not
    drawn) and the line shaft's two pedestals."""
    f = "frame"
    (gx0, gx1), (gy0, gy1), (gz0, gz1) = GEARCASE_X, GEARCASE_Y, GEARCASE_Z
    ly, lz = LINE
    out = [box([gx0 - 0.5, 0.0, gz0 - 1.0], [gx1 + 0.5, 1.5, gz1 + 1.0], "fr_gearcase_foot", f, "iron"),
           box([gx0, 1.5, gz0], [gx1, gy1, gz1], "fr_gearcase", f, "iron"),
           box([gx0 + 0.6, gy1, gz0 + 0.6], [gx1 - 0.6, gy1 + 0.8, gz1 - 0.6], "fr_gearcase_lid", f, "iron"),
           box([gx0 - 1.0, ly - 2.6, lz - 2.6], [gx0, ly + 2.6, lz + 2.6], "fr_gearcase_boss", f, "iron")]
    for tag, (x0, x1) in (("w", PED_W_X), ("m", PED_M_X)):
        out += [box([x0 - 0.6, 0.0, lz - 3.6], [x1 + 0.6, 1.2, lz + 3.6], f"fr_ped{tag}_foot", f, "iron"),
                box([x0, 1.2, lz - 2.6], [x1, ly + 2.6, lz + 2.6], f"fr_ped{tag}", f, "iron")]
    return out


def build_drive():
    """The entry shaft (the vanilla axle's cross, oak) into the gear case; the line shaft out of it, west, in its two
    pedestals."""
    out = []
    y, z = ENTRY
    out.append(box([GEARCASE_X[1] - 0.5, y - 1.0, z - 2.0], [48.0, y + 1.0, z + 2.0], "entry_shafta", "entry", "oak"))
    out.append(box([GEARCASE_X[1] - 0.5, y - 2.0, z - 1.0], [47.99, y + 2.0, z + 1.0], "entry_shaftb", "entry", "oak"))
    out += disc("x", yz(LINE), BULL_X[0] + 0.3, GEARCASE_X[0] + 0.5, LINE_R, "line_shaft", "line", "steel")
    return out


def build_line_pulleys():
    """The line shaft's two pulleys: the battery's under the bull pulley (level 1) and the breaker's (level 2)."""
    out = disc("x", yz(LINE), *BULL_X, LINE_PULLEY_R, "linepulley", "linepulley", "iron", k=4)
    out += disc("x", yz(LINE), BULL_X[0] - 0.2, BULL_X[1] + 0.2, 2.2, "linepulley_boss", "linepulley", "iron")
    out += disc("x", yz(LINE), *JAWDRIVE_X, JAWDRIVE_R, "jawpulley", "jawpulley", "iron", k=4)
    out += disc("x", yz(LINE), JAWDRIVE_X[0] - 0.2, JAWDRIVE_X[1] + 0.2, 2.2, "jawpulley_boss", "jawpulley", "iron")
    return out


# ---------------------------------------------------------------- builders: level 1
def build_mortar():
    """The mortar set: the oak mortar block, the cast-iron mortar box with five dies, the screen in its front wall and
    the feed opening in its back; the apron under the screen and the launder out to the north face; the feed box at
    the back, its floor the chute down to the feed lip; the water pipe from the east face's inlet into the feed box."""
    p = "mortar"
    (x0, x1), (z0, z1), (y0, y1) = MORTAR_X, MORTAR_Z, MORTAR_Y
    w = MORTAR_WALL
    fx0, fx1 = FEED_X
    out = [box([x0 - 0.5, 0.0, z0 - 1.0], [x1 + 0.5, y0, z1 + 0.5], "mortar_block", p, "oak"),
           box([x0, y0, z0], [x1, MORTAR_FLOOR, z1], "mortar_floor", p, "iron"),
           box([x0, MORTAR_FLOOR, z0], [x1, SCREEN_Y[0], z0 + w], "mortar_front_lo", p, "iron"),
           box([x0, SCREEN_Y[1], z0], [x1, y1, z0 + w], "mortar_front_hi", p, "iron"),
           box([x0, SCREEN_Y[0], z0], [x0 + w, SCREEN_Y[1], z0 + w], "mortar_front_w", p, "iron"),
           box([x1 - w, SCREEN_Y[0], z0], [x1, SCREEN_Y[1], z0 + w], "mortar_front_e", p, "iron"),
           box([x0 + w, SCREEN_Y[0], z0 + 0.5], [x1 - w, SCREEN_Y[1], z0 + 1.0], "mortar_screen", p, "mesh"),
           box([x0, MORTAR_FLOOR, z1 - w], [x1, FEED_LIP_Y, z1], "mortar_back_lo", p, "iron"),
           box([x0, FEED_LIP_Y, z1 - w], [fx0, y1, z1], "mortar_back_w", p, "iron"),
           box([fx1, FEED_LIP_Y, z1 - w], [x1, y1, z1], "mortar_back_e", p, "iron"),
           box([x0, MORTAR_FLOOR, z0 + w], [x0 + w, y1, z1 - w], "mortar_end_w", p, "iron"),
           box([x1 - w, MORTAR_FLOOR, z0 + w], [x1, y1, z1 - w], "mortar_end_e", p, "iron")]
    for i in range(STAMPS):
        out += disc("y", (stem_x(i), 0.0, STEM_Z), MORTAR_FLOOR, DIE_TOP, HEAD_R, f"mortar_die{i + 1}", p, "steel")
    # the apron: from under the screen's lip down onto the launder's back side
    lz0, lz1 = LAUNDER_Z
    ly0, ly1 = LAUNDER_Y
    out.append(plank((SCREEN_Y[0] - 0.3, z0), (ly1 + 0.5, lz1 - 0.6), x0 + w, x1 - w, "mortar_apron", p, "iron", t=0.8))
    # the launder: a trough along the front, a spout out of its middle through the north face
    lx0, lx1 = x0 + 0.5, x1 - 0.5
    sx0, sx1 = SPOUT_X
    out += [box([lx0, ly0, lz0], [lx1, ly0 + PLANK, lz1], "mortar_launder_floor", p, "planks"),
            box([lx0, ly0 + PLANK, lz1 - PLANK], [lx1, ly1, lz1], "mortar_launder_back", p, "planks"),
            box([lx0, ly0 + PLANK, lz0], [sx0, ly1, lz0 + PLANK], "mortar_launder_frontw", p, "planks"),
            box([sx1, ly0 + PLANK, lz0], [lx1, ly1, lz0 + PLANK], "mortar_launder_fronte", p, "planks"),
            box([lx0, ly0 + PLANK, lz0 + PLANK], [lx0 + PLANK, ly1 - 0.02, lz1 - PLANK], "mortar_launder_endw", p, "planks"),
            box([lx1 - PLANK, ly0 + PLANK, lz0 + PLANK], [lx1, ly1 - 0.02, lz1 - PLANK], "mortar_launder_ende", p, "planks"),
            box([sx0, ly0, 0.0], [sx1, ly0 + PLANK, lz0], "mortar_spout_floor", p, "planks"),
            box([sx0 - PLANK, ly0, 0.0], [sx0, ly1 - 1.5, lz0], "mortar_spout_w", p, "planks"),
            box([sx1, ly0, 0.0], [sx1 + PLANK, ly1 - 1.5, lz0], "mortar_spout_e", p, "planks")]
    out += build_feed_box()
    return out


def feed_floor_y(z):
    """The feed box floor's top at z: from the lip at the mortar's back down which the ore slides."""
    return FEED_LIP_Y + (z - MORTAR_Z[1]) * math.tan(FEED_SLOPE)


def build_feed_box():
    """The feed box at the battery's back: a floor sloping down to the feed lip, which is also the chute from the
    breaker's discharge, two sides and an end board; open on top, where the player shovels ore in."""
    p = "mortar"
    fx0, fx1 = FEED_X
    za, zb = MORTAR_Z[1], FEED_END_Z
    a, b = (feed_floor_y(za), za), (feed_floor_y(zb), zb)
    out = [plank(a, b, fx0 + PLANK, fx1 - PLANK, "mortar_feed_floor", p)]
    t = math.tan(FEED_SLOPE)
    rise = FEED_SIDE / math.cos(FEED_SLOPE)
    for tag, (sx0, sx1) in (("w", (fx0, fx0 + PLANK)), ("e", (fx1 - PLANK, fx1))):
        # parallel to the floor, from its underside to FEED_SIDE over its top
        out.append(plank((a[0] + rise, za), (b[0] + rise - PLANK * t, zb - PLANK), sx0, sx1, f"mortar_feed_{tag}", p,
                         t=FEED_SIDE + PLANK, down=(-1.0, t)))
    out.append(box([fx0 + PLANK, b[0] - 0.4, zb - PLANK], [fx1 - PLANK, b[0] + FEED_SIDE, zb], "mortar_feed_end", p, "planks"))
    return out


def pipe_boxes():
    """The water line as UnifiedPipes' soldered pipe (solderedpipe's boxes, build-frame voxels): the inlet from the
    west face on the axis of the ppex pipe beyond it, a hub, a run north, a hub against the west end frame's back
    post, a run east along the battery's back, a hub, and a short drop down into the feed box, open at its end over
    the feed lip. Every run is shorter than a block, so there are no seams and no joints but the hubs; the inlet has
    no joint at the face (the pipe beyond brings its own half joint, as a soldered ppex pipe does)."""
    h, wy = PIPE_HALF, WATER_Y
    out = solderedpipe.hub("hub1", (PIPE_X1, wy, WATER_Z), ("west", "north"))
    out += solderedpipe.tube("inlet", 0, 0.0, PIPE_X1 - h, (WATER_Z, wy))
    out += solderedpipe.tube("north", 2, PIPE_Z2 + h, WATER_Z - h, (PIPE_X1, wy))
    out += solderedpipe.hub("hub2", (PIPE_X1, wy, PIPE_Z2), ("south", "east"))
    out += solderedpipe.tube("east", 0, PIPE_X1 + h, PIPE_X3 - h, (PIPE_Z2, wy))
    out += solderedpipe.hub("hub3", (PIPE_X3, wy, PIPE_Z2), ("west", "down"))
    out += solderedpipe.tube("drop", 1, PIPE_END_Y, wy - h, (PIPE_X3, PIPE_Z2))
    return solderedpipe.cull_pressed(out)


def build_pipes(metal_=PIPE_METALS[0]):
    """The water line as elements of the part `pipe<metal_>` (`pipe_boxes`): the body in that metal's pipe texture,
    the joints (none here) in solder, each face's UVs solderedpipe's on this model's texture scale. Built in copper;
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
    """The water line in the other metals: the first metal's elements as they are (after the z-fighting fix, so every
    copy gets the same insets), renamed and in their own metal's texture. Only one metal's is ever drawn, so the
    copies are not checked against each other."""
    first = f"pipe{PIPE_METALS[0]}"
    out = []
    for m in PIPE_METALS[1:]:
        for el in els:
            if el.part == first:
                copy_ = el.clone(f"pipe{m}" + el.name[len(first):], f"pipe{m}")
                for face in copy_.faces.values():
                    if face["texture"] == f"#{first}":
                        face["texture"] = f"#pipe{m}"
                out.append(copy_)
    return out


def build_camshaft():
    """The camshaft set: the camshaft in two pillow blocks on the end frames' cam rails, ten involute cams (a double
    cam beside each stem), the bull pulley at its west end outside the west end frame, belted from the line shaft's
    pulley under it."""
    c = yz(CAM_C)
    out = disc("x", c, BULL_X[0] + 0.1, END_E[1] - 0.3, SHAFT_R, "camshaft_shaft", "camshaft", "steel")
    for tag, (x0, x1) in (("w", END_W), ("e", END_E)):
        out.append(box([x0 + RAIL_IN, CAM_Y - 2.0, CAM_Z - 2.4], [x1 - RAIL_IN, CAM_Y + 2.4, CAM_Z + 2.4], f"camframe_pillow_{tag}", "camframe", "iron"))
    for i in range(STAMPS):
        xa, xb = stem_x(i) + CAM_X[0], stem_x(i) + CAM_X[1]
        xm = (xa + xb) / 2
        out += disc("x", c, xa - 0.15, xb + 0.15, CAM_HUB_R, f"camshaft_hub{i + 1}", "camshaft", "iron")
        for lobe in (0, 1):
            # the arm: the involute's working face as chords (vertices on the curve, CAM_SHAVE inside it, equal in
            # arc length), each box CAM_ARM deep behind its chord; a web from the hub to the arm's root
            s0, s1 = CAM_RB * CAM_ROOT ** 2 / 2, CAM_RB * CAM_T1 ** 2 / 2
            ts = [math.sqrt(2 * (s0 + (s1 - s0) * k / CAM_SEGS) / CAM_RB) for k in range(CAM_SEGS + 1)]
            pts = [cam_profile(i, lobe, t) for t in ts]
            for k in range(CAM_SEGS):
                (ay, az), (by, bz) = pts[k], pts[k + 1]
                ny, nz = cam_normal(i, lobe, (ts[k] + ts[k + 1]) / 2)
                off = CAM_ARM / 2 + CAM_SHAVE
                a3 = (xm, CAM_Y + ay - ny * off, CAM_Z + az - nz * off)
                b3 = (xm, CAM_Y + by - ny * off, CAM_Z + bz - nz * off)
                out.append(strut(a3, b3, CAM_ARM, xb - xa - 0.02 * (k % 2), f"camshaft_cam{i + 1}{'ab'[lobe]}{k + 1}", "camshaft", "iron"))
            ny, nz = cam_normal(i, lobe, ts[0])
            root = (pts[0][0] - ny * (CAM_ARM / 2 + CAM_SHAVE), pts[0][1] - nz * (CAM_ARM / 2 + CAM_SHAVE))
            rr = math.hypot(*root)
            inner = (root[0] * (CAM_HUB_R - 0.6) / rr, root[1] * (CAM_HUB_R - 0.6) / rr)
            out.append(strut((xm, CAM_Y + inner[0], CAM_Z + inner[1]), (xm, CAM_Y + root[0], CAM_Z + root[1]), 1.4, xb - xa - 0.04,
                             f"camshaft_cam{i + 1}{'ab'[lobe]}0", "camshaft", "iron"))
    out += pulley("x", c, *BULL_X, BULL_R, "camshaft_bull", "camshaft", "oak", "iron", 2.6, rim_t=1.8, n=14, arms=6)
    out += belt(LINE, LINE_PULLEY_R, CAM_C, BULL_R, *BULL_X, "camframe_belt", "camframe")
    return out


def build_stamps():
    """Five stamps, each a steel shoe, an iron head, a steel stem and an iron tappet, drawn at their mean lift
    (DRAWN_LIFT over resting on their dies): the harmonics move them from there."""
    out = []
    for i in range(STAMPS):
        p = f"stamp{i + 1}"
        x = stem_x(i)
        c = (x, 0.0, STEM_Z)
        y0 = DIE_TOP + DRAWN_LIFT
        out += disc("y", c, y0, y0 + SHOE_T, SHOE_R, f"{p}_shoe", p, "steel")
        out += disc("y", c, y0 + SHOE_T, y0 + SHOE_T + HEAD_T, HEAD_R, f"{p}_head", p, "iron")
        out += disc("y", c, y0 + SHOE_T + HEAD_T - 0.3, STEM_TOP + DRAWN_LIFT, STEM_R, f"{p}_stem", p, "steel", k=3)
        ty = TAPPET_Y0 + DRAWN_LIFT
        out.append(box([x - TAPPET_H, ty, STEM_Z - TAPPET_H], [x + TAPPET_H, ty + TAPPET_T, STEM_Z + TAPPET_H], f"{p}_tappet", p, "iron"))
    return out


# ---------------------------------------------------------------- builders: level 2, the breaker (its own frame)
def build_jaw_frame_local():
    """The breaker's cast frame, as the crusher's was: two cheeks (a base flange, a post at the back wall, a post
    under the eccentric shaft's bearing, a post under the hinge's bearing and the chamber's side plate), the back
    wall with the back toggle's seat, the front wall behind the fixed jaw, the fixed jaw, the hinge pin."""
    p = "jawframe"
    out = []
    ey, ez = E_C
    hy, hz = H0
    zb0 = BACKWALL_U[0]
    zf1 = FRONT_U
    for tag, (x0, x1), (bx0, bx1) in (("w", CHEEK_W, (CHEEK_W[0] - 1.5, CHEEK_W[1])), ("e", CHEEK_E, (CHEEK_E[0], CHEEK_E[1] + 1.5))):
        out += [box([x0, JAW_BASE_Y, zb0], [x1, JAW_BASE_Y + 1.5, zf1], f"jawframe_base_{tag}", p, "iron"),
                box([x0, JAW_BASE_Y + 1.5, zb0], [x1, 31.5, BACKWALL_U[1]], f"jawframe_backpost_{tag}", p, "iron"),
                box([x0, JAW_BASE_Y + 1.5, ez - 3.0], [x1, ey - 2.6, ez + 3.0], f"jawframe_epost_{tag}", p, "iron"),
                box([bx0, ey - 2.6, ez - 2.6], [bx1, ey + 2.6, ez + 2.6], f"jawframe_ebearing_{tag}", p, "iron"),
                box([x0, JAW_BASE_Y + 1.5, hz - 1.8], [x1, hy - 1.8, hz + 1.8], f"jawframe_hpost_{tag}", p, "iron"),
                box([bx0 + 0.5 * (1 if tag == "w" else 0), hy - 1.8, hz - 1.8], [bx1 - 0.5 * (1 if tag == "e" else 0), hy + 1.8, hz + 1.8],
                    f"jawframe_hbearing_{tag}", p, "iron"),
                box([x0, JAW_BASE_Y + 1.5, hz + 1.8], [x1, 42.5, zf1], f"jawframe_side_{tag}", p, "iron")]
    out += [box([JAW_X[0], JAW_BASE_Y, zb0], [JAW_X[1], 31.5, BACKWALL_U[1]], "jawframe_backwall", p, "iron"),
            box([JAW_XC - TOGGLE_W / 2, B0[0] - 1.1, BACKWALL_U[1]], [JAW_XC + TOGGLE_W / 2, B0[0] + 1.1, BACKWALL_U[1] + 0.6], "jawframe_backseat", p, "steel"),
            box([JAW_X[0], JAW_BASE_Y, FIXED_U + 1.5], [JAW_X[1], 42.5, zf1], "jawframe_frontwall", p, "iron"),
            box([JAW_X[0] + 0.2, FACE_BOT[0] - 0.5, FIXED_U], [JAW_X[1] - 0.2, 42.0, FIXED_U + 1.5], "jawframe_fixedjaw", p, "steel")]
    out += disc("x", yz(H0), CHEEK_W[0] - 0.9, CHEEK_E[1] + 0.9, 0.9, "jawframe_hingepin", p, "steel")
    return out


def build_eshaft_local():
    """The eccentric shaft: its eccentric under the pitman's head (drawn up), the flywheel west of the west cheek and
    the pulley-flywheel east of the east cheek, which the line shaft's belt drives; collars at the bearings."""
    p = "eshaft"
    c = yz(E_C)
    out = disc("x", c, FLY_W_X[0] - 0.4, FLY_E_X[1] + 0.4, 1.6, "eshaft_shaft", p, "steel")
    out += disc("x", yz((E_C[0] + ECC, E_C[1])), JAW_X[0] + 3.0, JAW_X[1] - 3.0, 3.1, "eshaft_eccentric", p, "steel", k=4)
    out += pulley("x", c, *FLY_W_X, FLY_R, "eshaft_flyw", p, "iron", "iron", 2.6, rim_t=2.0, n=12, arms=6)
    out += pulley("x", c, *FLY_E_X, FLY_R, "eshaft_flye", p, "iron", "iron", 2.6, rim_t=2.0, n=12, arms=6)
    out += disc("x", c, CHEEK_W[0] - 2.1, CHEEK_W[0] - 1.5, 2.3, "eshaft_collar_w", p, "steel")
    out += disc("x", c, CHEEK_E[1] + 1.5, CHEEK_E[1] + 2.1, 2.3, "eshaft_collar_e", p, "steel")
    return out


FOOT_UP = 2.4                                # the pitman's foot rises this far over its pin: the toggles swing under its body
TOGGLE_TIP = 0.6                             # each toggle stops this far short of the foot's pin, in its seat


def buffer_u(c):
    """Buffer c's centre (designed): evenly between the nut's back face and the back wall's front face."""
    a, b = ROD_NUT_U[1], BACKWALL_U[0]
    return a + (b - a) * (c + 0.5) / BUFFERS


def buffer_share(c):
    """The share of the nut's travel buffer c takes: all of it at the nut, none at the wall."""
    a, b = ROD_NUT_U[1], BACKWALL_U[0]
    return (b - buffer_u(c)) / (b - a)


def build_linkage_local():
    """The pitman, the two toggles, the swinging jaw, the tension rods and their buffers, drawn at the linkage's
    design pose (the eccentric centred) and each put at its mean pose; the rig's harmonics move them from there."""
    out = []
    (ey, ez), (py, pz) = E_C, P0
    pit = annulus("x", yz(E_C), JAW_X[0] + 3.0, JAW_X[1] - 3.0, 3.15, 4.2, 10, "pitman_strap", "pitman", "iron")
    pit += [box([JAW_X[0] + 4.0, py + FOOT_UP, ez - 1.0], [JAW_X[1] - 4.0, ey - 3.9, ez + 1.0], "pitman_body", "pitman", "iron"),
            box([JAW_X[0] + 3.4, py - 1.3, pz - 1.0], [JAW_X[1] - 3.4, py + FOOT_UP, pz + 1.0], "pitman_foot", "pitman", "iron")]
    out += placed(pit, FITS["a_p"][0], P0, MEAN_P)

    def toggle(a, b, name):
        d = math.dist(a, b)
        tip = (b[0] + (a[0] - b[0]) * TOGGLE_TIP / d, b[1] + (a[1] - b[1]) * TOGGLE_TIP / d)
        return strut(yz(a, JAW_XC), yz(tip, JAW_XC), 1.4, TOGGLE_W, name, name.split("_")[0], "steel")
    out += placed([toggle(B0, P0, "backtoggle_plate")], FITS["a_b"][0], B0, B0)
    out += placed([toggle(J0, P0, "fronttoggle_plate")], FITS["a_f"][0], J0, MEAN_J)
    bk = JAW_BACK
    sx0, sx1 = JAW_X[0] + 0.3, JAW_X[1] - 0.3
    jaw = [plank(FACE_BOT, FACE_TOP, sx0, sx1, "swingjaw_face", "swingjaw", "steel", t=1.0, down=bk),
           plank((FACE_BOT[0] + bk[0], FACE_BOT[1] + bk[1]), (FACE_TOP[0] + bk[0], FACE_TOP[1] + bk[1]), sx0 + 0.02, sx1 - 0.02,
                 "swingjaw_slab", "swingjaw", "iron", t=SLAB_T - 1.0, down=bk)]
    top = (FACE_TOP[0] + SLAB_T * bk[0], FACE_TOP[1] + SLAB_T * bk[1])
    jaw += disc("x", yz(H0), sx0, sx1, 1.8, "swingjaw_boss", "swingjaw", "iron", k=3)
    jaw.append(strut(yz((top[0] - 0.6, top[1] + 1.2), JAW_XC), yz(H0, JAW_XC), 2.4, sx1 - sx0 - 0.04, "swingjaw_web", "swingjaw", "iron"))
    jaw.append(box([JAW_XC - TOGGLE_W / 2, J0[0] - 1.1, J0[1] - 0.4], [JAW_XC + TOGGLE_W / 2, J0[0] + 1.1, J0[1] + 0.3], "swingjaw_seat", "swingjaw", "steel"))
    for tag, x in zip("we", ROD_X):
        jaw.append(box([x - 0.9, K0[0] - 0.9, K0[1] - 0.3], [x + 0.9, K0[0] + 0.9, _slab_back_u(K0[0]) + 1.4], f"swingjaw_lug{tag}", "swingjaw", "iron"))
    out += placed(jaw, FITS["beta"][0], H0, H0)
    rods = []
    for tag, x in zip("we", ROD_X):
        rods += disc("z", (x, K0[0], 0.0), ROD_NUT_U[0], K0[1] + 0.3, ROD_R, f"rods_{tag}", "rods", "steel", k=3)
        rods += disc("z", (x, K0[0], 0.0), *ROD_NUT_U, 1.0, f"rods_nut{tag}", "rods", "steel", k=3)
        rods += disc("x", yz(K0), x - 1.0, x + 1.0, 0.5, f"rods_pin{tag}", "rods", "steel", k=3)
    translate(rods, [0.0, MEAN_K[0] - K0[0], MEAN_K[1] - K0[1]])
    out += rods
    half = (BACKWALL_U[0] - ROD_NUT_U[1]) / BUFFERS / 2 - 0.08
    for c in range(BUFFERS):
        bufs = []
        u = buffer_u(c)
        for tag, x in zip("we", ROD_X):
            bufs += disc("z", (x, K0[0], 0.0), u - half, u + half, 1.2, f"buffer{c + 1}_{tag}", f"buffer{c + 1}", "rubber")
        translate(bufs, [0.0, MEAN_K[0] - K0[0], buffer_share(c) * (MEAN_K[1] - K0[1])])
        out += bufs
    return out


def build_jaw():
    """The breaker, built in its own frame and mirrored into the build frame; its stand (two posts under each sill,
    the sills under the cheeks) and its belt, built in the build frame."""
    out = mirrored(build_jaw_frame_local() + build_eshaft_local() + build_linkage_local())
    p = "jawframe"
    zf = JAW_C - FRONT_U
    zb = JAW_C - BACKWALL_U[0]
    for tag, (x0, x1) in zip("we", STAND_X):
        out.append(box([x0, STAND_Y[0], zf], [x1, STAND_Y[1], zb], f"jawframe_sill_{tag}", p, "oak"))
        for k, (z0, z1) in enumerate(STAND_POST_Z, 1):
            out.append(box([x0 + 0.02, 0.0, z0], [x1 - 0.02, STAND_Y[0], z1], f"jawframe_post_{tag}{k}", p, "oak"))
    out += belt(LINE, JAWDRIVE_R, jaw_to_build(E_C), FLY_R, *JAWDRIVE_X, "jawframe_belt", p)
    return out


def griz_top(z):
    """The grizzly bars' top at z (build frame): a straight fall from GRIZ_HEAD to GRIZ_FOOT."""
    (hz, hy), (fz, fy) = GRIZ_HEAD, GRIZ_FOOT
    return hy + (z - hz) * (fy - hy) / (fz - hz)


def griz_under(z):
    """The bars' underside at z."""
    (hz, hy), (fz, fy) = GRIZ_HEAD, GRIZ_FOOT
    return griz_top(z) - GRIZ_BAR_T * math.hypot(1.0, (hy - fy) / (fz - hz))


def throat_under(z):
    """The throat plate's underside at z: it rests on the breaker's front wall's north top edge and rises south at
    THROAT_SLOPE."""
    return JAW_TOP_Y + (z - JAW_FRONT_Z) * math.tan(THROAT_SLOPE)


def build_grizzly():
    """The grizzly: iron bars falling from a head block on the back tie to their foot over the breaker's mouth,
    resting there on a cross bar on two brackets on the cheeks; and a throat plate under the bars over the
    breaker's front wall, which sheds the fines that fall there north into the feed box."""
    p = "grizzly"
    (hz, hy), (fz, fy) = GRIZ_HEAD, GRIZ_FOOT
    gx0, gx1 = GRIZ_X
    z_head, z_foot = hz - 1.0, fz + 1.0                      # the bars run on a voxel past each
    out = [box([gx0 - 1.6, TIE_B_Y[1], POST_B[0] + 0.5], [gx1 + 1.6, griz_under(z_head + 1.6) + 0.05, hz + 1.5], "grizzly_head", p, "iron")]
    gap = (gx1 - gx0 - GRIZ_BARS * GRIZ_BAR_W) / (GRIZ_BARS - 1)
    for k in range(GRIZ_BARS):
        x0 = gx0 + k * (GRIZ_BAR_W + gap)
        out.append(plank((griz_top(z_head), z_head), (griz_top(z_foot), z_foot), x0, x0 + GRIZ_BAR_W, f"grizzly_bar{k + 1}", p, "iron",
                         t=GRIZ_BAR_T))
    # the foot: a cross bar under the bars, its top at their underside at its south edge, on two brackets on the cheeks
    cz0, cz1 = CROSSBAR_Z
    cy1 = griz_under(cz1) + 0.05
    cy0 = cy1 - 1.2
    for tag, (x0, x1) in (("w", CHEEK_W), ("e", CHEEK_E)):
        out.append(box([x0 + 0.2, JAW_TOP_Y, cz0 + 0.1], [x1 - 0.2, cy0 + 0.05, cz1 - 0.1], f"grizzly_bracket{tag}", p, "iron"))
    out.append(box([CHEEK_W[0] + 0.2, cy0, cz0], [CHEEK_E[1] - 0.2, cy1, cz1], "grizzly_crossbar", p, "iron"))
    # the throat plate over the front wall, and a lip under its south end standing on the fixed jaw's top
    t = THROAT_T
    za, zb = THROAT_Z
    out.append(plank((throat_under(zb) + t / math.cos(THROAT_SLOPE), zb), (throat_under(za) + t / math.cos(THROAT_SLOPE), za),
                     JAW_X[0] + 0.2, JAW_X[1] - 0.2, "grizzly_throat", p, "iron", t=t))
    lz0, lz1 = zb - 0.9, zb - 0.1
    out.append(box([JAW_X[0] + 0.3, JAW_TOP_Y - 0.5, lz0], [JAW_X[1] - 0.3, throat_under(lz0) + 0.05, lz1], "grizzly_lip", p, "iron"))
    return out


def build():
    els = build_frame() + build_drive() + build_line_pulleys()
    els += build_mortar() + build_camshaft() + build_stamps()
    els += build_jaw() + build_grizzly() + build_pipes()
    return els


# ---------------------------------------------------------------- the levels
LEVELS = {"level1": ("mortar", "camshaft", "stamps"), "level2": ("mortar", "camshaft", "stamps", "jaw", "grizzly")}


PIPE_REQUIRES = tuple(f"pipe{m}" for m in PIPE_METALS)


def in_state(requires, state):
    """Whether a part with this `requires` is drawn in a build state ("frame", "level1", "level2"): at each level
    the water line in its first metal (the others are copies, never drawn with it)."""
    if requires == PIPE_REQUIRES[0]:
        return state != "frame"
    return requires is None or (state != "frame" and requires in LEVELS[state])


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def norm_phase(p):
    return math.remainder(p, TAU)


def stamp_drivers(i):
    """Stamp i's lift as harmonics of the axle's travel: the profile's n-th harmonic of the camshaft's period (pi,
    so 2n times its turn), shifted to the stamp's phase."""
    k = abs(CAM_RATIO)
    out = []
    for n, s, c in PROFILE_H:
        amp = math.hypot(s, c)
        ph = math.atan2(c, s) - 2 * n * stamp_phase(i)
        out.append({"type": "slide", "axis": "y", "amplitude": r6(amp / B), "ratio": r6(2 * n * k), "phase": r6(norm_phase(ph)), "input": "travel"})
    return out


def harmonics(key, motion, axis, pivot=None, scale=1.0):
    """The rig drivers for one of the linkage's fits, mirrored into the build frame: one swing or slide per
    harmonic, on the axle's travel. In the breaker's frame the eccentric turns +JAW_E_RATIO a radian of travel;
    mirrored (z = JAW_C - u) a turn about x changes sign, as does a slide along z."""
    out = []
    flip = -1.0 if (motion == "swing" or axis == "z") else 1.0
    for n, a, p in FITS[key][1][:MAX_HARMONICS]:
        d = {"type": motion, "axis": axis}
        if pivot is not None:
            d["pivot"] = pt(0.0, *jaw_to_build(pivot))
        d.update({"amplitude": r6(flip * a * scale / (B if motion == "slide" else 1.0)), "ratio": r6(n * JAW_E_RATIO), "phase": r6(p),
                  "input": "travel"})
        out.append(d)
    return out


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    def rot_x(centre, ratio, inp=None):
        return dict({"type": "rotate", "axis": "x", "pivot": pt(*yz(centre)), "ratio": r6(ratio)}, **({"input": inp} if inp else {}))
    parts = [
        {"id": "entry", "match": ["entry_*"], "requires": None, "drivers": [rot_x(ENTRY, 1.0)]},
        {"id": "line", "match": ["line_*"], "requires": None, "drivers": [rot_x(LINE, LINE_RATIO, "travel")]},
        # level 1: the battery
        {"id": "linepulley", "match": ["linepulley*"], "requires": "camshaft", "drivers": [rot_x(LINE, LINE_RATIO, "travel")]},
        {"id": "camshaft", "match": ["camshaft_*"], "requires": "camshaft", "drivers": [rot_x(CAM_C, CAM_RATIO, "travel")]},
        {"id": "camframe", "match": ["camframe_*"], "requires": "camshaft", "drivers": []},
    ]
    for i in range(STAMPS):
        parts.append({"id": f"stamp{i + 1}", "match": [f"stamp{i + 1}_*"], "requires": "stamps", "drivers": stamp_drivers(i)})
    parts += [
        {"id": "mortar", "match": ["mortar_*"], "requires": "mortar", "drivers": []},
        # level 2: the breaker and the grizzly
        {"id": "jawpulley", "match": ["jawpulley*"], "requires": "jaw", "drivers": [rot_x(LINE, LINE_RATIO, "travel")]},
        {"id": "eshaft", "match": ["eshaft_*"], "requires": "jaw", "drivers": [rot_x(jaw_to_build(E_C), -JAW_E_RATIO, "travel")]},
        {"id": "pitman", "match": ["pitman_*"], "requires": "jaw",
         "drivers": harmonics("a_p", "swing", "x", MEAN_P) + harmonics("py", "slide", "y") + harmonics("pz", "slide", "z")},
        {"id": "backtoggle", "match": ["backtoggle_*"], "requires": "jaw", "drivers": harmonics("a_b", "swing", "x", B0)},
        {"id": "fronttoggle", "match": ["fronttoggle_*"], "requires": "jaw",
         "drivers": harmonics("a_f", "swing", "x", MEAN_J) + harmonics("jy", "slide", "y") + harmonics("jz", "slide", "z")},
        {"id": "swingjaw", "match": ["swingjaw_*"], "requires": "jaw", "drivers": harmonics("beta", "swing", "x", H0)},
        {"id": "rods", "match": ["rods_*"], "requires": "jaw", "drivers": harmonics("ky", "slide", "y") + harmonics("kz", "slide", "z")},
    ]
    for c in range(BUFFERS):
        parts.append({"id": f"buffer{c + 1}", "match": [f"buffer{c + 1}_*"], "requires": "jaw",
                      "drivers": harmonics("kz", "slide", "z", scale=buffer_share(c))})
    parts += [
        {"id": "jawframe", "match": ["jawframe_*"], "requires": "jaw", "drivers": []},
        {"id": "grizzly", "match": ["grizzly_*"], "requires": "grizzly", "drivers": []},
        # the water line, one part per metal (requires pipecopper, pipelead): only the fitted metal's is drawn
        *({"id": r, "match": [f"{r}_*"], "requires": r, "drivers": []} for r in PIPE_REQUIRES),
        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses
REST = (0.0, 0.0)                            # (theta, travel): the authored pose's inputs


def inputs_of(pose):
    th, ps = pose[:2]
    return {"theta": th, "travel": ps}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


def turning(psi, theta=None):
    """A pose of the axle having travelled psi (its angle psi, unless given)."""
    return (psi if theta is None else theta, psi)


def at_cam(phi):
    """A pose with the camshaft turned phi radians the working way."""
    psi = phi / abs(CAM_RATIO)
    return (psi, psi)


def at_ecc(phi):
    """A pose with the eccentric turned phi radians (the breaker's frame)."""
    psi = phi / JAW_E_RATIO
    return (psi, psi)


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("water", "feed", "infeed", "output")
CELL_ANCHORS = (("power", POWER_CELL, POWER_FACE), ("water", WATER_CELL, WATER_FACE), ("feed", FEED_CELL, FEED_FACE),
                ("infeed", INFEED_CELL, INFEED_FACE), ("output", OUTPUT_CELL, OUTPUT_FACE))


def anchor_points():
    """The anchors' points (build frame, voxels): the water flange's face, the feed box's mouth on its cell's top, the
    grizzly's head on the top face, the launder spout's lip."""
    return {"water": (0.0, WATER_Y, WATER_Z),
            "feed": (sum(FEED_X) / 2, FEED_CELL[1] * B + B, FEED_CELL[2] * B + 8.0),
            "infeed": (sum(GRIZ_X) / 2, CELLS_Y * B, GRIZ_HEAD[0]),
            "output": (sum(SPOUT_X) / 2, LAUNDER_Y[0] + PLANK, 0.0)}


def make_rig(parts):
    rig = {"_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the bottom middle cell of the "
                       "north face, over the launder's spout. One machine upgraded in place (#711): the frame's parts have no "
                       "requires; level 1 (water) fits mortar, camshaft and stamps, level 2 (mill) adds jaw and grizzly (levels); the "
                       "water line is pipecopper or pipelead, the same pipe in the fitted metal. "
                       "A cell's boxes are the frame's; its fitted boxes are there while that requires value is fitted. theta "
                       "turns the entry shaft; everything behind the rectifier's gear case follows travel (psi): the line shaft, "
                       "the camshaft, the stamps (harmonics of the camshaft's turn) and the breaker. No work. See the stamp "
                       "mill's README for the schema.",
           "cells": []}
    for name, cell, face in CELL_ANCHORS:
        rig[f"{name}Cell"] = list(cell)
        rig[f"{name}Face"] = face
    for key, p in anchor_points().items():
        rig[key] = {"pos": pt(*p)}
    rig["stamps"] = {"turnsPerRev": r6(TURNS_PER_REV), "dropsPerRev": 2, "firingOrder": list(ORDER), "lift": r6(LIFT / B),
                     "_comment": f"turnsPerRev: axle turns per camshaft turn as the belt is drawn (the line pulley's radius "
                                 f"{LINE_PULLEY_R} to the bull pulley's {BULL_R}, behind a 1:1 rectifier); each stamp drops "
                                 "dropsPerRev times a camshaft turn, in firingOrder, and is lifted `lift` blocks by its cam."}
    rig["jaw"] = {"turnsPerStroke": r6(1.0 / JAW_E_RATIO),
                  "_comment": f"Axle turns per turn of the eccentric shaft (the line's pulley's radius {JAWDRIVE_R} to the "
                              f"pulley-flywheel's {FLY_R}): the swinging jaw closes once a turn."}
    rig["levels"] = {"1": list(LEVELS["level1"]), "2": list(LEVELS["level2"]),
                     "_comment": "The requires values fitted at each level; a level adds parts to the last and takes none off. "
                                 "From level 1 the water line is fitted too, in one metal: pipecopper or pipelead."}
    rig["parts"] = parts
    return rig


# ---------------------------------------------------------------- shipped
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
    for name, _, _ in CELL_ANCHORS:
        ship[f"{name}Cell"] = shift_cell(rig[f"{name}Cell"], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def lidded(x, z):
    """The battery's columns have a lid (its caps and ties are a deck); the breaker's, behind it, have none."""
    return z <= 1


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig: `boxes` from the frame's
    parts, `fitted` per requires value from that value's parts; a cell with neither is hollow. Then the lids."""
    written = flatten(shape["elements"], textures={})
    requires = {p["id"]: p["requires"] for p in ship_parts}
    by_cell = {}
    for w in written:
        pid = part_of(ship_parts, w.name)
        el = posed(w, _part_matrix(ship_parts, pid, inputs_of(REST)))
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, {}).setdefault(requires[pid], []).append(el)
    out = []
    order = [None] + list(LEVELS["level2"]) + list(PIPE_REQUIRES)
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        groups = by_cell.get(pos, {})
        cell = {"pos": list(pos)}
        frame = cell_boxes(groups[None], pos) if None in groups else None
        if frame:
            cell["boxes"] = frame
        fitted = {}
        for r in order[1:]:
            if r in groups:
                bs = cell_boxes(groups[r], pos)
                if bs:
                    fitted[r] = bs
        if fitted:
            cell["fitted"] = fitted
        if not frame and not fitted:
            cell["hollow"] = True
        out.append(cell)
    shifted = lambda x, z: lidded(x + ORIGIN_CELL[0], z + ORIGIN_CELL[2])  # noqa: E731
    return with_lids(out, shifted)


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    poses = [REST, turning(0.3), turning(4.77, -2.0), at_cam(1.1), at_ecc(2.3), turning(31.0, 1.0)]
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
def reference_poses():
    """theta and psi: the axle at rest, either way, through four turns of the cycle (the eccentric's 4/3), at
    every stamp's pick-up, mid-lift, release and landing, and a long run."""
    out = []
    for th in (0.0, 1.1, -2.3, 2.9, -5.4):
        out.append((th, abs(th)))
    for extra in (0.37, 7.3, 13.9, 25.1, 50.27, 101.0):
        out.append((1.3, round(1.3 + extra, 6)))
    for i in range(STAMPS):
        for x in (0.0, 0.4, LIFT / CAM_RB, LIFT / CAM_RB + 0.45, LIFT / CAM_RB + FALL, 2.6):
            psi = r6((stamp_phase(i) + x) / abs(CAM_RATIO))
            out.append((r6(-psi), psi))
    for k in range(8):
        psi = r6(TAU * k / 8 / JAW_E_RATIO)
        out.append((psi, psi))
    return out


def reference_json(ship_parts):
    poses = []
    for pose in reference_poses():
        th, ps = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose))) for q in ship_parts}
        poses.append({"theta": th, "travel": ps, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped stampmill-rig.json's parts: each part's matrix as 3 rows of 4 "
                        "(block units) at each pose of the axle's angle (theta) and travel (psi). The site's and the Python "
                        "tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Every level's parts are in this "
             "shape; the rig's requires say which level each belongs to. Keep element names when editing: the rig finds "
             "its parts by them.", TEXTURES, tex_size=TEX)


def coplanar_poses():
    """(pose, state): the machine at each level at rest and running, so faces that never show together are never
    compared."""
    return (REST + ("level2",), at_cam(0.7) + ("level1",), at_cam(2.3) + ("level2",), at_ecc(1.9) + ("level2",), REST + ("frame",))


REQUIRES = {}


def shown(posed_els, pose):
    """The posed elements of the pose's build state; the rest moved far away (copies; the order kept), so the
    z-fighting fix and its check deal only with faces that show together."""
    state = pose[2] if len(pose) > 2 else "level2"
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
    ap = argparse.ArgumentParser(description="Generate the stamp mill's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_stampmill
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
    els += pipe_copies(els)
    rig = make_rig(parts)
    ok = validate_stampmill.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "stampmill.json", args.out / "stampmill_frame.json", args.out / "stampmill-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "stampmill.json", SHAPE_DIR / "stampmill_frame.json", RIG_DIR / "stampmill-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_stampmill.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
