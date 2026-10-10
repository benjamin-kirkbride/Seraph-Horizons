#!/usr/bin/env python3
"""Generate the crusher's shapes, rig and reference poses.

The crusher is one machine upgraded in place (issue #711): a timber mill frame, three blocks each way,
whose footprint is its tier 4 size from the start, and the working parts each tier fits into it (tier 1
is the game's pulverizer, not this model):

    tier 2  a five-stamp battery: a cast-iron mortar box with its dies, screen and apron (`mortar`); a
            camshaft with ten involute cams, a bull wheel loose on it and a cone clutch (`camshaft`); five
            stamps, each a shoe, head, stem and tappet, in two guide girts (`stamps`). A hopper's worth of
            ore at a time: the battery runs a load, W, of camshaft turns, and stands between loads.
    tier 3  a Blake jaw crusher (`jaw`): a cast frame on two bearers, a fixed jaw and a swinging jaw hung
            from the top, an eccentric shaft with a flywheel and a spur wheel driving a pitman, whose foot
            straightens two toggle plates that rock the swinging jaw's lower end; tension rods with springs
            keep the toggles seated. Its product drops to a launder under it and out of the spout.
    tier 4  the jaw crusher and crushing rolls (`rolls`): two rolls under the jaw's discharge, geared together
            to turn towards each other, one in fixed bearings and one held by springs, fed by a hopper that
            also takes the oversize returned from the classifier through the west face.

Fitting the next tier's set replaces the last tier's working parts, so tier 2's battery never stands with
the jaw: each tier's parts are checked against the frame and against each other, never against another
tier's. The frame carries what every tier shares: the power train (the entry shaft from the vanilla axle, a
rectifier and the line shaft's pinion, which every tier's driven wheel meshes), the feed hopper on top, the
product spout at the north face and the oversize inlet on the west face. Everything is built here from plain
boxes; no other mod's model is used.

It writes, deterministically,

    crusher.json         the whole machine, every tier's parts         (assets/.../shapes/block/)
    crusher_frame.json   the static frame only (block and item)        (assets/.../shapes/block/)
    crusher-rig.json     cells, anchors and the part rig               (assets/.../config/)
    rig-reference.json   every part's matrix at a grid of poses        (tests/Crusher/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_crusher.py) and exits non-zero
if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
machine box's north-west-bottom corner (the "build frame"); the controller cell is the bottom middle cell of
the north face, ORIGIN_CELL, so the shipped files are the build frame moved by -ORIGIN_CELL blocks.

The rig's inputs, as this machine uses them (README "Rig"):

    theta  the axle angle: the entry shaft and the rectifier's gears
    psi    the axle's travel: the line shaft and everything every tier drives from it but the camshaft
    W      the rig's work, camshaft turns into a load of ore at tier 2 (STAMP_LOAD a load)
    k      the load: 0 none, 1 or 2 (two classes the battery treats alike)
    p      its presence, 0..1: the battery's clutch goes in as a load goes on and out as it is cleared
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
REFERENCE_OUT = MOD / "tests" / "Crusher" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/Crusher/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 3, 3, 3          # three blocks each way: the tier 4 size, fixed from the frame on
ORIGIN_CELL = (1, 0, 0)                      # the controller: the bottom middle cell of the north face, over the product spout
POWER_CELL, POWER_FACE = (2, 0, 1), "east"   # the vanilla axle comes in along x at (y 8, z 24)
INFEED_CELL, INFEED_FACE = (1, 2, 1), "up"   # the feed hopper's mouth
OUTPUT_CELL, OUTPUT_FACE = (1, 0, 0), "north"   # the product spout
OVERSIZE_CELL, OVERSIZE_FACE = (0, 1, 1), "west"   # the classifier's oversize comes back in here (tier 4)

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "planks": "game:block/wood/planks/oak1",
    "iron": "game:block/metal/plate/iron",
    "steel": "game:block/metal/sheet-plain/steel1",
    "mesh": "game:block/metal/mesh1",
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the frame (oak, every tier's)
SIDE_W, SIDE_E = (1.0, 6.0), (37.0, 42.0)    # the two side frames (x): posts, sills, rails and caps
POSTS_W = ((1.0, 6.0), (42.0, 47.0))         # posts (z) of the west frame
POSTS_E = ((1.0, 6.0), (21.0, 27.0), (42.0, 47.0))   # the east frame's middle post carries the entry shaft and the line shaft
SILL_Y = (0.0, 4.0)
CAP_Y = (44.0, 48.0)
RAIL_IN = 0.5                                # rails stand in from the posts' faces
DECK_Y = (17.0, 21.0)                        # the deck rails: the jaw's bearers, the oversize inlet and the lower stem guide rest on them
UPPER_Y = (38.5, 41.0)                       # the upper rails: the upper stem guide rests on them
BEARER_Y = (21.0, 24.0)                      # the jaw's two bearers, across the frame on the deck rails
BEARER_N, BEARER_S = (1.5, 5.5), (33.0, 38.0)
HOPPER_BEAMS = ((18.0, 21.0), (34.0, 37.0))  # across the frame under its caps' top: they carry the feed hopper
HOPPER_TOP = 48.0
HOPPER_THROAT_Y = 43.6
HOPPER_RIM = {"x": (16.4, 27.6), "z": (19.0, 36.5)}
HOPPER_MOUTH = {"x": (18.0, 26.0), "z": (24.0, 32.5)}
PLANK = 0.8                                  # board thickness: hopper, chutes
SPOUT_X = (16.0, 28.0)                       # the product spout's floor at the north face
SPOUT_Z = (0.0, 3.6)
SPOUT_SIDE = 4.0                             # its sides' height
INLET_Y = 21.0                               # the oversize inlet's floor, on the west deck rail
INLET_Z = (26.5, 32.2)
INLET_X1 = SIDE_W[1] - RAIL_IN                # the inlet lies on the west deck rail

# ---------------------------------------------------------------- the power train (the frame's)
MOD1 = 1.0                                   # module of every drive gear
ENTRY = (8.0, 24.0)                          # (y, z): the entry shaft, along x from the east face (the power cell's centre)
LINE = (24.0, 24.0)                          # (y, z): the rectified line shaft
RECT_A, RECT_B = 12, 20                      # rectifier: A1 on the entry shaft drives B1, loose on the line shaft, directly
RECT_A2, RECT_I, RECT_B2 = 9, 8, 15          # and A2 drives B2 through an idler: the same ratio, smaller so A2 and B2 clear each other
RECT_RATIO = RECT_A / RECT_B                 # the line shaft turns 0.6 of the axle's travel
LINE_SIGN = -1.0                             # the line shaft turns negative about x (so the rolls turn towards their nip)
X_RECT1, X_RECT2 = (42.6, 44.0), (44.6, 46.0)
X_BRACKET = (46.4, 48.0)                     # the outer bearing bracket at the east face
PINION_N = 8                                 # the line shaft's pinion: every tier's driven wheel meshes it
X_DRIVE = (34.0, 36.0)                       # the drive plane, inside the east frame
SHAFT_R = 1.5


def _idler():
    """The rectifier's idler: r(A) + r(I) from the entry shaft, r(I) + r(B) from the line shaft, to the south."""
    (y1, z1), (y2, z2) = ENTRY, LINE
    r1, r2 = (RECT_A2 + RECT_I) * MOD1 / 2, (RECT_I + RECT_B2) * MOD1 / 2
    d = math.hypot(y2 - y1, z2 - z1)
    a = (r1 * r1 - r2 * r2 + d * d) / (2 * d)
    h = math.sqrt(r1 * r1 - a * a)
    uy, uz = (y2 - y1) / d, (z2 - z1) / d
    cands = [(y1 + a * uy + s * h * (-uz), z1 + a * uz + s * h * uy) for s in (1.0, -1.0)]
    return max(cands, key=lambda c: c[1])


IDLER = _idler()


def on_pinion(teeth, y=None, z=None):
    """A shaft whose wheel of `teeth` meshes the line shaft's pinion: given its z (or y), the other coordinate
    puts the centres the two pitch radii apart (the one above the line shaft, or north of it)."""
    d = (teeth + PINION_N) * MOD1 / 2
    if z is not None:
        return (LINE[0] + math.sqrt(d * d - (z - LINE[1]) ** 2), z)
    return (y, LINE[1] - math.sqrt(d * d - (y - LINE[0]) ** 2))


# ---------------------------------------------------------------- tier 2: the stamp battery
STAMPS = 5
STEM_X0, STEM_PITCH = 11.6, 4.4              # the stems' x
STEM_Z = 10.5                                # the stems' line (z)
STEM_R = 0.6
SHOE_R, HEAD_R = 1.8, 1.9                    # octagons (apothem)
TAPPET_H = 1.9                               # the tappet: a square collar 3.8 across
TAPPET_T = 3.4                               # its height
CAM_RB = 3.0                                 # the cams' base circle: the involute's lift per radian
CAM_T0 = 0.4                                 # the involute's roll at first contact: the tappet rests CAM_RB * CAM_T0 over the camshaft
DROP = 3.2                                   # each stamp's lift (and drop)
CAM_EDGE = 0.2                               # the contact line stands this far inside the tappet's edge on the camshaft's side
CAM_X = (0.75, 1.65)                         # each cam's plate beside its stem (x, from the stem's axis): it sweeps past the stem
CAM_ARM = 1.0                                # the arm's depth behind its working face
CAM_HUB_R = 2.6
CAM_SEGS = 12                                # boxes per arm
CAM_ROOT = 0.25                              # the arm's working face starts at this roll; a web joins its root to the hub
CAM_Z = STEM_Z + TAPPET_H - CAM_EDGE + CAM_RB  # the camshaft's z: the contact line just inside the tappet's edge
BULL_N = 16                                  # the bull wheel, loose on the camshaft, meshes the line shaft's pinion
CAM_Y, _ = on_pinion(BULL_N, z=CAM_Z)        # the camshaft's height
CAM_C = (CAM_Y, CAM_Z)
TAPPET_Y0 = CAM_Y + CAM_RB * CAM_T0          # the tappet's underside, the stamp at rest on its die
CAM_T1 = CAM_T0 + DROP / CAM_RB
CAM_TIP = CAM_RB * math.hypot(1.0, CAM_T1)   # the cams' reach from the camshaft
CAM_Y_TOP = CAM_Y - 2.0                      # the cam rails' top: the pillow blocks stand on them
ORDER = (1, 3, 5, 2, 4)                      # the firing order
LIFT_W = DROP / CAM_RB / TAU                 # a lift, in camshaft turns (the involute lifts CAM_RB a radian)
DROP_W = 0.02                                # the drop, in camshaft turns
STAMP_LOAD = 4                               # camshaft turns a load (two drops a stamp each turn)
DIE_TOP = 6.0                                # the dies' top: the shoes rest on them
SHOE_T, HEAD_T = 3.5, 3.0
STEM_TOP = 44.5
MORTAR_X = (7.7, 33.1)
MORTAR_Z = (4.0, 16.5)
MORTAR_Y = (2.0, 18.0)
MORTAR_WALL = 1.5
MORTAR_FLOOR = 4.0
SCREEN_Y = (9.0, 14.0)                       # the screen in the front wall
FEED_LIP_Y = 14.5                            # the feed opening in the back wall, over the lip
GUIDE_LO_Y, GUIDE_HI_Y = (21.0, 23.5), (41.0, 43.5)
GUIDE_Z = (STEM_Z - 2.0, STEM_Z + 2.0)
X_CLUTCH = (31.0, 33.3)                      # the clutch's grooved collar and cone on the camshaft, west of the bull wheel
CLUTCH_THROW = 0.6
LEVER_PIVOT = (32.7, 22.0, 17.5)             # the clutch lever's pin (along z), on a bracket from the east deck rail


def stem_x(i):
    return STEM_X0 + STEM_PITCH * i


def stamp_phase(i):
    """Stamp i's (0-based) first lift, in camshaft turns: the ten drops a turn are evenly spaced in the firing order."""
    return ORDER.index(i + 1) * 0.1


def stamp_lift(i, w):
    """Stamp i's lift (voxels) at W: rises DROP over LIFT_W as its cam turns, falls over DROP_W, rests on its die."""
    a = stamp_phase(i)
    out = 0.0
    for k in range(-2, 2 * STAMP_LOAD + 2):
        w0 = a + 0.5 * k
        out += DROP * (min(1.0, max(0.0, (w - w0) / LIFT_W)) - min(1.0, max(0.0, (w - w0 - LIFT_W) / DROP_W)))
    return out


# ---------------------------------------------------------------- tier 3: the Blake jaw crusher
JAW_X = (16.0, 28.0)                         # the chamber (x) between the cheeks
CHEEK_W, CHEEK_E = (13.0, 16.0), (28.0, 31.0)
JAW_BASE_Y = BEARER_Y[1]                     # the cheeks stand on the bearers
FIXED_Z = 33.0                               # the fixed jaw's face (south)
FACE_BOT, FACE_TOP = (26.5, 30.0), (41.5, 23.5)   # (y, z): the swinging jaw's face, a gap of 3 at the bottom
SLAB_T = 3.5                                 # the swinging jaw behind its face (iron, with a steel face plate)
TOGGLE_Y = 26.5                              # the toggles' outer seats (y)
TOGGLE_SAG = 2.0                             # the pitman's foot hangs this far below them: raised, it straightens them
TOGGLE_L = 10.0
ECC = 1.2                                    # the eccentric's throw
E_TEETH = 18                                 # the spur wheel on the eccentric shaft
HINGE_UP = 1.6                               # the hinge pin over the slab's top back corner


def _jaw_points():
    fb, ft = FACE_BOT, FACE_TOP
    dy, dz = ft[0] - fb[0], ft[1] - fb[1]
    n = math.hypot(dy, dz)
    d = (dy / n, dz / n)
    back = (-d[1], d[0]) if d[1] > 0 else (d[1], -d[0])       # perpendicular, pointing north (-z)
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
E_C = on_pinion(E_TEETH, z=P0[1])           # the eccentric shaft over the pitman's foot, meshing the pinion
DISCHARGE_Z = (FACE_BOT[1] + FIXED_Z) / 2
FLY_X = (8.0, 10.5)                          # the flywheel, west of the west cheek
FLY_R = 8.0
ROD_X = (16.8, 27.2)                         # the two tension rods, either side of the toggles
JAW_BACK_Z = (B0[1] - 3.0, B0[1])            # the back wall: the back toggle seats on its south face

# ---------------------------------------------------------------- tier 4: the crushing rolls
ROLL_R = 6.0
ROLL_GAP = 1.0
ROLL_X = JAW_X
NIP_Z = DISCHARGE_Z
R1_TEETH = 16                                # the north roll's wheel meshes the pinion; the rolls' pair gears are equal
R1_Z = NIP_Z - ROLL_R - ROLL_GAP / 2
R2_Z = NIP_Z + ROLL_R + ROLL_GAP / 2
R1_C = on_pinion(R1_TEETH, z=R1_Z)
R1_C = (LINE[0] - (R1_C[0] - LINE[0]), R1_Z)  # below the line shaft
R2_C = (R1_C[0], R2_Z)
PAIR_N = 13                                  # the pair gears at the west end: centres 13 apart
PAIR_MOD = (R2_Z - R1_Z) / PAIR_N
X_PAIR = (8.0, 10.0)
PED_W, PED_E = (12.0, 15.0), (29.0, 32.0)    # the rolls' pedestals (x)
PED_Z = (R1_Z - 4.0, R2_Z + 5.0)
CHUTE_LOW = 20.6                             # the oversize chute's floor where it enters the rolls' hopper


# ---------------------------------------------------------------- box helpers (as the draw bench's)
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
    w = 2 * r_out * math.tan(math.pi / n)
    out = []
    for i in range(n):
        s = 0.02 if i % 2 else 0.0
        out.append(radial(axis, c, a0 + s, a1 - s, r_in, r_out, w, phase + TAU * i / n, f"{name}{i + 1}", part, tex))
    return out


def gear(axis, c, a0, a1, pitch_r, n, module, name, part, tex, phase=0.0, body_k=4):
    """A spur gear: a polygon body to the root and n teeth of `module` (phase: angle of tooth 1)."""
    width = math.pi * module / 2
    root, tip = pitch_r - 1.25 * module, pitch_r + module
    body = disc(axis, c, a0, a1, root, f"{name}_body", part, tex, k=body_k, phase=phase)
    return body + [radial(axis, c, a0 + 0.03, a1 - 0.03, root - 0.25, tip, width, phase + TAU * i / n, f"{name}_tooth{i + 1}", part, tex)
                   for i in range(n)]


def wheel(axis, c, a0, a1, pitch_r, n, module, name, part, tex, hub_r, phase=0.0, arms=6, hub_x=None):
    """A large spur wheel: a toothed rim, arms and a hub (the rim a ring of boxes under the teeth); the hub stands
    out 0.2 either side unless `hub_x` says where it runs."""
    root, tip = pitch_r - 1.25 * module, pitch_r + module
    width = math.pi * module / 2
    rim_in = root - 1.4
    out = annulus(axis, c, a0, a1, rim_in, root, max(16, n), f"{name}_rim", part, tex, phase=phase)
    out += [radial(axis, c, a0 + 0.03, a1 - 0.03, root - 0.25, tip, width, phase + TAU * i / n, f"{name}_tooth{i + 1}", part, tex)
            for i in range(n)]
    out += [radial(axis, c, a0 + 0.25, a1 - 0.25, hub_r - 0.2, rim_in + 0.2, 1.0, phase + TAU * (i + 0.5) / arms, f"{name}_arm{i + 1}", part, tex)
            for i in range(arms)]
    hx0, hx1 = hub_x or (a0 - 0.2, a1 + 0.2)
    out += disc(axis, c, hx0, hx1, hub_r, f"{name}_hub", part, tex, k=4, phase=phase)
    return out


def rod(axis, c, a0, a1, r, name, part, tex, k=4):
    return disc(axis, c, a0, a1, r, name, part, tex, k=k)


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


def mesh_phase(c1, phase1, n1, c2, n2):
    """The tooth phase (angle of tooth 1, about +x, from +y towards +z) for gear 2 meshing gear 1."""
    d = math.atan2(c2[2] - c1[2], c2[1] - c1[1])
    p1, p2 = TAU / n1, TAU / n2
    off = ((d - phase1) / p1) % 1.0
    return (d + math.pi) + p2 / 2 + off * p2


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


def oriented(center, size, ax, ay, az, name, part, tex):
    """A box of `size` (along its own x, y, z) whose local axes are the world unit vectors ax, ay, az."""
    el = box([-s / 2 for s in size], [s / 2 for s in size], name, part, tex)
    el.c = list(center)
    el.r = [[ax[i], ay[i], az[i]] for i in range(3)]
    return el


def unit(v):
    n = math.sqrt(sum(c * c for c in v))
    return [c / n for c in v]


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


# ---------------------------------------------------------------- the ratios, from the line shaft's pinion
LINE_RATIO = LINE_SIGN * RECT_RATIO          # the line shaft (and its pinion) per radian of the axle's travel
BULL_RATIO = -LINE_RATIO * PINION_N / BULL_N  # the bull wheel (+: the cams lift on the stems' side)
E_RATIO = -LINE_RATIO * PINION_N / E_TEETH   # the eccentric shaft
R1_RATIO = -LINE_RATIO * PINION_N / R1_TEETH  # the north roll (+: its south face, at the nip, moves down)
R2_RATIO = -R1_RATIO
TURNS_PER_REV = 1.0 / BULL_RATIO             # axle turns per camshaft turn, the clutch in


# ---------------------------------------------------------------- the jaw's linkage
LEN_P = math.hypot(E_C[0] - P0[0], E_C[1] - P0[1])   # the pitman, the eccentric's centre to its foot (designed with the eccentric centred)
LEN_B = math.hypot(P0[0] - B0[0], P0[1] - B0[1])
LEN_F = math.hypot(P0[0] - J0[0], P0[1] - J0[1])


def ecc_centre(phi):
    """The eccentric's centre at its angle phi (from +y towards +z): it is drawn at phi 0, up."""
    return (E_C[0] + ECC * math.cos(phi), E_C[1] + ECC * math.sin(phi))


def jaw_point(p, beta):
    """A point of the swinging jaw (designed at beta 0) with the jaw turned beta about its hinge."""
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


FIT_N = 128                                  # samples a turn of the eccentric for the harmonic fits
FIT_HARMONICS = 6


def _slab_back_z(y):
    """The swinging jaw's back face (designed) at height y."""
    bb = (FACE_BOT[0] + SLAB_T * JAW_BACK[0], FACE_BOT[1] + SLAB_T * JAW_BACK[1])
    return bb[1] + (y - bb[0]) / JAW_DIR[0] * JAW_DIR[1]


K0 = (TOGGLE_Y - 1.4, _slab_back_z(TOGGLE_Y - 1.4))   # the tension rods' pin on the swinging jaw's back
ROD_NUT_Z = (0.7, 1.4)


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
    """The mean and harmonics (n, amplitude, phase) of a function sampled at len(values) even angles of a turn:
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


def harmonic(h, phi):
    return sum(a * math.sin(n * phi + p) for n, a, p in h)


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
    """Where a point of the pitman, given at the linkage's design pose, is drawn (its mean pose)."""
    return _to_mean(p, FITS["a_p"][0], P0, MEAN_P)


def design_to_mean_back(p):
    return _to_mean(p, FITS["a_b"][0], B0, B0)


def design_to_mean_front(p):
    return _to_mean(p, FITS["a_f"][0], J0, MEAN_J)


def design_to_mean_jaw(p):
    return _to_mean(p, FITS["beta"][0], H0, H0)


def placed(els, angle, pivot, to):
    """Elements drawn at the linkage's design pose, turned `angle` about `pivot` ((y, z)) and moved so the pivot
    lands at `to`: a body put at its mean pose."""
    rotate(els, "x", math.degrees(angle), yz(pivot))
    translate(els, [0.0, to[0] - pivot[0], to[1] - pivot[1]])
    return els


# ---------------------------------------------------------------- the cams
def cam_profile(i, lobe, t):
    """A point (y, z, relative to the camshaft) of stamp i's cam, lobe 0 or 1, at the involute's roll t, as the cam
    is drawn (W = 0): the point that meets the tappet's underside, CAM_RB * t over the camshaft on the contact line
    CAM_RB on the stems' side of it, when its lobe has turned that far."""
    phi_s = TAU * (stamp_phase(i) + 0.5 * lobe)
    return rot2(-(phi_s + t - CAM_T0), (CAM_RB * t, -CAM_RB))


def cam_normal(i, lobe, t):
    """The cam's working face's outward normal at roll t, as drawn: the tappet's underside is level (straight up)
    where that point meets it."""
    phi_s = TAU * (stamp_phase(i) + 0.5 * lobe)
    return rot2(-(phi_s + t - CAM_T0), (1.0, 0.0))


# ---------------------------------------------------------------- builders: the frame
def build_frame():
    f = "frame"
    out = []
    for tag, (x0, x1), posts in (("w", SIDE_W, POSTS_W), ("e", SIDE_E, POSTS_E)):
        out.append(box([x0, SILL_Y[0], 0.0], [x1, SILL_Y[1], 48.0], f"fr_sill_{tag}", f, "oak"))
        out.append(box([x0, CAP_Y[0], 0.0], [x1, CAP_Y[1], 48.0], f"fr_cap_{tag}", f, "oak"))
        for i, (z0, z1) in enumerate(posts, 1):
            out.append(box([x0, SILL_Y[1], z0], [x1, CAP_Y[0], z1], f"fr_post_{tag}{i}", f, "oak"))
        spans = [(posts[i][1], posts[i + 1][0]) for i in range(len(posts) - 1)]
        for name, (y0, y1) in (("deck", DECK_Y), ("cam", (CAM_Y_TOP - 3.5, CAM_Y_TOP)), ("upper", UPPER_Y)):
            for j, (z0, z1) in enumerate(spans, 1):
                out.append(box([x0 + RAIL_IN, y0, z0], [x1 - RAIL_IN, y1, z1], f"fr_{name}_{tag}{j}", f, "oak"))
    out.append(box([SIDE_W[1], SILL_Y[0], 43.0], [SIDE_E[0], SILL_Y[1], 48.0], "fr_sill_s", f, "oak"))
    for tag, (z0, z1) in (("n", POSTS_W[0]), ("s", POSTS_W[1])):
        out.append(box([SIDE_W[1], CAP_Y[0], z0], [SIDE_E[0], CAP_Y[1], z1], f"fr_tie_{tag}", f, "oak"))
    for tag, (z0, z1) in (("n", BEARER_N), ("s", BEARER_S)):
        out.append(box([SIDE_W[0], BEARER_Y[0], z0], [SIDE_E[1], BEARER_Y[1], z1], f"fr_bearer_{tag}", f, "oak"))
    for tag, (z0, z1) in zip("ns", HOPPER_BEAMS):
        out.append(box([SIDE_W[0], 45.5, z0], [SIDE_E[1], 47.99, z1], f"fr_hopperbeam_{tag}", f, "oak"))
    out += build_hopper()
    out += build_spouts()
    out += build_drive_frame()
    return out


def build_hopper():
    """The feed hopper: four boards from its rim on the hopper beams down to its throat over the jaw's mouth."""
    f = "frame"
    (rx0, rx1), (rz0, rz1) = HOPPER_RIM["x"], HOPPER_RIM["z"]
    (mx0, mx1), (mz0, mz1) = HOPPER_MOUTH["x"], HOPPER_MOUTH["z"]
    top, bot = HOPPER_TOP - 0.45, HOPPER_THROAT_Y
    out = [plank((top, rz0), (bot, mz0), rx0, rx1, "fr_hopper_n", f, down=(0.0, -1.0)),
           plank((top, rz1), (bot, mz1), rx0 + 0.03, rx1 - 0.03, "fr_hopper_s", f, down=(0.0, 1.0))]
    zm = (rz0 + rz1) / 2
    for tag, (xa, xb), sgn in (("w", (rx0, mx0), 1.0), ("e", (rx1, mx1), -1.0)):   # each board's thickness inward
        dx, dy = xb - xa, bot - top
        n = math.hypot(dx, dy)
        nrm = (dy / n, -dx / n)                       # (x, y), perpendicular in the x-y plane
        if nrm[0] * sgn < 0:
            nrm = (-nrm[0], -nrm[1])
        a = (xa + nrm[0] * PLANK / 2, top + nrm[1] * PLANK / 2, zm)
        b = (xb + nrm[0] * PLANK / 2, bot + nrm[1] * PLANK / 2, zm)
        out.append(strut(a, b, PLANK, rz1 - rz0 - 0.06, f"fr_hopper_{tag}", f, "planks", axis="z"))
    return out


def build_spouts():
    """The product spout at the north face, on the ground, and the oversize inlet at the west face, on the
    west deck rail: both the frame's, so every tier's chutes end at them and nothing outside moves."""
    f = "frame"
    (x0, x1), (z0, z1) = SPOUT_X, SPOUT_Z
    out = [box([x0, 0.0, z0], [x1, 0.8, z1], "fr_spout_floor", f, "iron"),
           box([x0 - 0.8, 0.0, z0], [x0, 3.0, z1], "fr_spout_w", f, "iron"),
           box([x1, 0.0, z0], [x1 + 0.8, 3.0, z1], "fr_spout_e", f, "iron")]
    iz0, iz1 = INLET_Z
    out += [box([0.0, INLET_Y, iz0], [INLET_X1, INLET_Y + 0.8, iz1], "fr_inlet_floor", f, "iron"),
            box([0.0, INLET_Y, iz0 - 0.8], [INLET_X1, INLET_Y + 3.5, iz0], "fr_inlet_n", f, "iron"),
            box([0.0, INLET_Y, iz1], [INLET_X1, INLET_Y + 3.5, iz1 + 0.8], "fr_inlet_s", f, "iron")]
    return out


def build_drive_frame():
    """The power train's fixed parts: bearing plates on the east frame's middle post for the entry shaft and the
    line shaft, the outer bracket at the east face with their outer bearings, the idler's arm and stud."""
    f = "frame"
    out = []
    pz0, pz1 = POSTS_E[1]
    for tag, (y, z), r in (("entry", ENTRY, 1.2), ("line", LINE, SHAFT_R)):
        for side, (x0, x1) in (("w", (SIDE_E[0] - 0.6, SIDE_E[0])), ("e", (SIDE_E[1], SIDE_E[1] + 0.6))):
            out.append(box([x0, y - r - 1.2, pz0 + 0.3], [x1, y + r + 1.2, pz1 - 0.3], f"fr_{tag}plate_{side}", f, "iron"))
    bx0, bx1 = X_BRACKET
    out.append(box([bx0, 0.0, 21.0], [bx1, LINE[0] + 3.0, 27.0], "fr_bracket", f, "iron"))
    out.append(box([bx0, 0.0, 19.5], [bx1, 1.2, 28.5], "fr_bracket_foot", f, "iron"))
    iy, iz = IDLER
    out.append(box([bx0, iy - 1.0, 27.0], [bx1, iy + 1.0, iz + 1.0], "fr_idler_arm", f, "iron"))
    out += rod("x", yz(IDLER), X_RECT2[0] - 0.3, bx1 - 0.3, 0.7, "fr_idler_stud", f, "steel")
    return out


# ---------------------------------------------------------------- builders: the power train
def build_drive():
    """The entry shaft (the vanilla axle's cross, oak, then steel) with the rectifier's A1 and A2; B1 and B2
    loose on the line shaft with their one-way catches; the idler; the line shaft and its pinion."""
    out = []
    y, z = ENTRY
    c = yz(ENTRY)
    out.append(box([X_BRACKET[0] - 0.5, y - 1.0, z - 2.0], [48.0, y + 1.0, z + 2.0], "entry_shafta", "entry", "oak"))
    out.append(box([X_BRACKET[0] - 0.5, y - 2.0, z - 1.0], [47.99, y + 2.0, z + 1.0], "entry_shaftb", "entry", "oak"))
    out += rod("x", c, SIDE_E[0] - 0.6, X_BRACKET[0] + 0.6, 1.2, "entry_rod", "entry", "steel")
    ra, rb, ra2, ri, rb2 = (n * MOD1 / 2 for n in (RECT_A, RECT_B, RECT_A2, RECT_I, RECT_B2))
    lc, ic = yz(LINE), yz(IDLER)
    out += gear("x", c, *X_RECT1, ra, RECT_A, MOD1, "entry_a1", "entry", "steel")
    out += gear("x", c, *X_RECT2, ra2, RECT_A2, MOD1, "entry_a2", "entry", "steel")
    out += gear("x", lc, *X_RECT1, rb, RECT_B, MOD1, "rectb1", "rectb1", "steel", phase=mesh_phase(c, 0.0, RECT_A, lc, RECT_B), body_k=6)
    out.append(box([X_RECT1[1], LINE[0] + 2.8, LINE[1] - 0.5], [X_RECT1[1] + 0.3, LINE[0] + 4.6, LINE[1] + 0.5], "rectb1_catch", "rectb1", "steel"))
    ip = mesh_phase(c, 0.0, RECT_A2, ic, RECT_I)
    out += gear("x", ic, *X_RECT2, ri, RECT_I, MOD1, "idler", "idler", "steel", phase=ip)
    out += gear("x", lc, *X_RECT2, rb2, RECT_B2, MOD1, "rectb2", "rectb2", "steel", phase=mesh_phase(ic, ip, RECT_I, lc, RECT_B2), body_k=6)
    out.append(box([X_RECT2[1], LINE[0] - 4.6, LINE[1] - 0.5], [X_RECT2[1] + 0.3, LINE[0] - 2.8, LINE[1] + 0.5], "rectb2_catch", "rectb2", "steel"))
    out += rod("x", lc, X_DRIVE[0] - 0.2, X_BRACKET[1] - 0.4, SHAFT_R, "line_shaft", "line", "steel")
    out += gear("x", lc, *X_DRIVE, PINION_N * MOD1 / 2, PINION_N, MOD1, "line_pinion", "line", "steel")
    out += disc("x", lc, X_DRIVE[1], SIDE_E[0] - 0.6, 2.2, "line_collar", "line", "steel")
    out += disc("x", lc, X_RECT1[1], X_RECT2[0], 2.6, "line_spacer", "line", "steel")
    return out


def driven_phase(centre, teeth):
    """A wheel's tooth phase meshing the line shaft's pinion (whose tooth 1 is at angle 0)."""
    return mesh_phase(yz(LINE), 0.0, PINION_N, yz(centre), teeth)


# ---------------------------------------------------------------- builders: tier 2, the stamp battery
def build_mortar():
    """The mortar set: the oak mortar block on the ground, the cast-iron mortar box with five dies, the screen in
    its front wall and the feed opening in its back, the apron down to the product spout with two guide boards,
    and the feed chute from the hopper's throat down behind the camshaft to the feed opening."""
    p = "mortar"
    (x0, x1), (z0, z1), (y0, y1) = MORTAR_X, MORTAR_Z, MORTAR_Y
    w, fl = MORTAR_WALL, MORTAR_FLOOR
    out = [box([x0 - 0.5, 0.0, SPOUT_Z[1]], [x1 + 0.5, y0, z1 + 0.5], "mortar_block", p, "oak"),
           box([x0, y0, z0], [x1, fl, z1], "mortar_floor", p, "iron"),
           box([x0, fl, z0], [x1, SCREEN_Y[0], z0 + w], "mortar_front_lo", p, "iron"),
           box([x0, SCREEN_Y[1], z0], [x1, y1, z0 + w], "mortar_front_hi", p, "iron"),
           box([x0, SCREEN_Y[0], z0], [x0 + w, SCREEN_Y[1], z0 + w], "mortar_front_w", p, "iron"),
           box([x1 - w, SCREEN_Y[0], z0], [x1, SCREEN_Y[1], z0 + w], "mortar_front_e", p, "iron"),
           box([x0 + w, SCREEN_Y[0], z0 + 0.5], [x1 - w, SCREEN_Y[1], z0 + 1.0], "mortar_screen", p, "mesh"),
           box([x0, fl, z1 - w], [x1, FEED_LIP_Y, z1], "mortar_back_lo", p, "iron"),
           box([x0, FEED_LIP_Y, z1 - w], [stem_x(0) + 0.4, y1, z1], "mortar_back_w", p, "iron"),
           box([stem_x(STAMPS - 1) - 0.4, FEED_LIP_Y, z1 - w], [x1, y1, z1], "mortar_back_e", p, "iron"),
           box([x0, fl, z0 + w], [x0 + w, y1, z1 - w], "mortar_end_w", p, "iron"),
           box([x1 - w, fl, z0 + w], [x1, y1, z1 - w], "mortar_end_e", p, "iron")]
    for i in range(STAMPS):
        out += disc("y", (stem_x(i), 0.0, STEM_Z), fl, DIE_TOP, HEAD_R, f"mortar_die{i + 1}", p, "steel")
    # the apron: from under the screen's lip down to the spout, and two boards on it turning the pulp into the spout
    a, b = (SCREEN_Y[0] - 0.2, z0), (3.6, 1.2)
    out.append(plank(a, b, x0 + w, x1 - w, "mortar_apron", p, "iron", t=0.5))
    slope = unit([0.0, a[0] - b[0], a[1] - b[1]])               # up the apron
    normal = unit(cross([1.0, 0.0, 0.0], slope))
    if normal[1] < 0:
        normal = [-v for v in normal]
    for tag, xa, xb in (("w", x0 + w + 0.3, SPOUT_X[0] + 0.3), ("e", x1 - w - 0.3, SPOUT_X[1] - 0.3)):
        top = [xa, a[0], a[1]]
        f = 0.85                                                  # the board stops short of the apron's lower edge
        bot = [xb, a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f]
        along = unit([bot[k] - top[k] for k in range(3)])
        side = unit(cross(along, normal))
        mid = [(top[k] + bot[k]) / 2 + normal[k] * 0.75 for k in range(3)]
        length = math.dist(top, bot)
        out.append(oriented(mid, (length, 1.5, 0.4), along, normal, side, f"mortar_guide_{tag}", p, "iron"))
    # the feed chute: a trough from the hopper's throat down behind the cams, then a spout into the feed opening
    hx0, hx1 = HOPPER_MOUTH["x"]
    top, knee, lip = (HOPPER_THROAT_Y - 0.4, HOPPER_MOUTH["z"][1] - 0.1), (25.6, 22.6), (y1 + 0.3, z1 - 0.5)
    out.append(plank(top, knee, hx0 + 0.2, hx1 - 0.2, "mortar_chute_floor", p, down=(-0.5, -0.866)))
    out.append(plank(knee, lip, hx0 + 0.2, hx1 - 0.2, "mortar_feeder_floor", p, down=(-0.7, -0.7)))
    for tag, (sx0, sx1) in (("w", (hx0 - 0.6, hx0 + 0.2)), ("e", (hx1 - 0.2, hx1 + 0.6))):
        for seg, (p0, p1) in (("chute", (top, knee)), ("feeder", (knee, lip))):
            mid0 = (p0[0] + 1.25, p0[1])
            mid1 = (p1[0] + 1.25, p1[1])
            out.append(plank(mid0, mid1, sx0, sx1, f"mortar_{seg}_{tag}", p, t=2.5, down=(-1.0, 0.0)))
    return out


def build_camshaft():
    """The camshaft set: the camshaft in two pillow blocks on the cam rails, ten involute cams (a double cam
    beside each stem), the bull wheel loose on it meshing the line shaft's pinion, with the clutch's cup on its
    hub; the clutch's cone and grooved collar on a feather on the camshaft, its lever on a pin in a bracket off
    the east deck rail."""
    c = yz(CAM_C)
    out = rod("x", c, SIDE_W[0] + 0.7, SIDE_E[1] - 0.7, SHAFT_R, "camshaft_shaft", "camshaft", "steel")
    for tag, (x0, x1) in (("w", (SIDE_W[0] + RAIL_IN, SIDE_W[1] - RAIL_IN)), ("e", (SIDE_E[0] + RAIL_IN, SIDE_E[1] - RAIL_IN))):
        out.append(box([x0, CAM_Y_TOP, CAM_Z - 2.4], [x1, CAM_Y + 2.4, CAM_Z + 2.4], f"camframe_pillow_{tag}", "camframe", "iron"))
    out += disc("x", c, SIDE_W[1] - RAIL_IN, SIDE_W[1] + 0.1, 2.2, "camshaft_collar_w", "camshaft", "steel")
    out += disc("x", c, X_DRIVE[1] + 0.25, SIDE_E[0] + RAIL_IN, 2.2, "camshaft_collar_e", "camshaft", "steel")
    for i in range(STAMPS):
        xa, xb = stem_x(i) + CAM_X[0], stem_x(i) + CAM_X[1]
        xm = (xa + xb) / 2
        out += disc("x", c, xa, xb, CAM_HUB_R, f"camshaft_hub{i + 1}", "camshaft", "iron")
        for lobe in (0, 1):
            # the arm: the involute's working face as chords (its vertices on the curve, equal in arc length), each
            # box CAM_ARM deep behind its chord; a web from the hub to the arm's root
            s0, s1 = CAM_RB * CAM_ROOT ** 2 / 2, CAM_RB * CAM_T1 ** 2 / 2
            ts = [math.sqrt(2 * (s0 + (s1 - s0) * k / CAM_SEGS) / CAM_RB) for k in range(CAM_SEGS + 1)]
            pts = [cam_profile(i, lobe, t) for t in ts]
            for k in range(CAM_SEGS):
                (ay, az), (by, bz) = pts[k], pts[k + 1]
                ny, nz = cam_normal(i, lobe, (ts[k] + ts[k + 1]) / 2)
                off = CAM_ARM / 2
                a3 = (xm, CAM_Y + ay - ny * off, CAM_Z + az - nz * off)
                b3 = (xm, CAM_Y + by - ny * off, CAM_Z + bz - nz * off)
                out.append(strut(a3, b3, CAM_ARM, xb - xa - 0.02 * (k % 2), f"camshaft_cam{i + 1}{'ab'[lobe]}{k + 1:02d}", "camshaft", "iron"))
            ny, nz = cam_normal(i, lobe, ts[0])
            root = (pts[0][0] - ny * CAM_ARM / 2, pts[0][1] - nz * CAM_ARM / 2)
            rr = math.hypot(*root)
            inner = (root[0] * (CAM_HUB_R - 0.6) / rr, root[1] * (CAM_HUB_R - 0.6) / rr)
            out.append(strut((xm, CAM_Y + inner[0], CAM_Z + inner[1]), (xm, CAM_Y + root[0], CAM_Z + root[1]), 1.3, xb - xa - 0.04,
                             f"camshaft_cam{i + 1}{'ab'[lobe]}00", "camshaft", "iron"))
    # the bull wheel and the clutch's cup on its hub
    bc = c
    out += wheel("x", bc, *X_DRIVE, BULL_N * MOD1 / 2, BULL_N, MOD1, "bullwheel", "bullwheel", "iron", 2.6, phase=driven_phase(CAM_C, BULL_N),
                 hub_x=(X_DRIVE[0], X_DRIVE[1] + 0.2))
    out += annulus("x", bc, X_CLUTCH[1], X_DRIVE[0] - 0.05, 2.9, 3.6, 12, "bullwheel_cup", "bullwheel", "iron")
    out += disc("x", bc, X_DRIVE[0] - 0.05, X_DRIVE[0] + 0.25, 3.6, "bullwheel_cupback", "bullwheel", "iron", k=6)
    # the cone: a grooved collar for the lever's yoke, then three steps that go into the cup
    x0 = X_CLUTCH[0]
    out += disc("x", bc, x0, x0 + 0.4, 2.6, "cone_flange1", "cone", "steel", k=6)
    out += disc("x", bc, x0 + 0.4, x0 + 1.0, 2.0, "cone_groove", "cone", "steel")
    out += disc("x", bc, x0 + 1.0, x0 + 1.4, 2.6, "cone_flange2", "cone", "steel", k=6)
    step = (X_CLUTCH[1] - x0 - 1.4) / 3
    for k, r in enumerate((2.2, 2.5, 2.8)):
        out += disc("x", bc, x0 + 1.4 + step * k, x0 + 1.4 + step * (k + 1), r, f"cone_step{k + 1}", "cone", "steel", k=6)
    # the clutch lever on its pin, its yoke in the collar's groove; the bracket off the east deck rail
    px, py, pz = LEVER_PIVOT
    gx = x0 + 0.7
    lz = (CAM_Z + 2.2, CAM_Z + 3.0)
    out += [box([gx - 0.25, py - 0.8, lz[0]], [px + 1.0, py + 0.8, lz[1]], "clutchlever_boss", "clutchlever", "iron"),
            box([gx - 0.25, py + 0.8, lz[0]], [gx + 0.25, 40.0, lz[1]], "clutchlever_bar", "clutchlever", "iron"),
            box([gx - 0.2, CAM_Y + 2.05, CAM_Z], [gx + 0.2, CAM_Y + 2.55, lz[0]], "clutchlever_prongup", "clutchlever", "steel"),
            box([gx - 0.2, CAM_Y - 2.55, CAM_Z], [gx + 0.2, CAM_Y - 2.05, lz[0]], "clutchlever_prongdn", "clutchlever", "steel"),
            box([gx - 0.5, 40.0, lz[0] - 0.2], [gx + 0.5, 41.2, lz[1] + 0.2], "clutchlever_knob", "clutchlever", "oak")]
    out += rod("z", (px, py, 0.0), lz[0] - 0.75, lz[1] + 0.75, 0.35, "camframe_leverpin", "camframe", "steel")
    out += [box([gx - 0.3, py - 2.6, lz[0] - 0.8], [SIDE_E[0] + RAIL_IN, py - 1.0, lz[1] + 0.8], "camframe_leverbracket", "camframe", "iron"),
            box([px - 0.5, py - 1.0, lz[0] - 0.75], [px + 0.5, py + 0.6, lz[0] - 0.05], "camframe_leverlug1", "camframe", "iron"),
            box([px - 0.5, py - 1.0, lz[1] + 0.05], [px + 0.5, py + 0.6, lz[1] + 0.75], "camframe_leverlug2", "camframe", "iron")]
    return out


def build_stamps():
    """Five stamps, each a steel shoe, an iron head, a steel stem and an iron tappet, drawn as they stand at W 0
    (a stamp then part way up on its cam stands so); the lower and upper guide girts, oak, on the deck and upper
    rails, with a square hole for each stem."""
    out = []
    for i in range(STAMPS):
        p = f"stamp{i + 1}"
        x = stem_x(i)
        lift = stamp_lift(i, 0.0)
        c = (x, 0.0, STEM_Z)
        y0 = DIE_TOP + lift
        out += disc("y", c, y0, y0 + SHOE_T, SHOE_R, f"{p}_shoe", p, "steel")
        out += disc("y", c, y0 + SHOE_T, y0 + SHOE_T + HEAD_T, HEAD_R, f"{p}_head", p, "iron")
        out += disc("y", c, y0 + SHOE_T + HEAD_T, STEM_TOP + lift, STEM_R, f"{p}_stem", p, "steel")
        ty = TAPPET_Y0 + lift
        out.append(box([x - TAPPET_H, ty, STEM_Z - TAPPET_H], [x + TAPPET_H, ty + TAPPET_T, STEM_Z + TAPPET_H], f"{p}_tappet", p, "iron"))
    gz0, gz1 = GUIDE_Z
    hole = STEM_R + 0.05
    for tag, (y0, y1) in (("lo", GUIDE_LO_Y), ("hi", GUIDE_HI_Y)):
        xa, xb = SIDE_W[0] + RAIL_IN, SIDE_E[1] - RAIL_IN
        out += [box([xa, y0, gz0], [xb, y1, STEM_Z - hole], f"guides_{tag}_front", "guides", "oak"),
                box([xa, y0, STEM_Z + hole], [xb, y1, gz1], f"guides_{tag}_back", "guides", "oak")]
        edges = [xa] + [v for i in range(STAMPS) for v in (stem_x(i) - hole, stem_x(i) + hole)] + [xb]
        for k in range(0, len(edges), 2):
            out.append(box([edges[k], y0 + 0.02, STEM_Z - hole], [edges[k + 1], y1 - 0.02, STEM_Z + hole], f"guides_{tag}_block{k // 2 + 1}", "guides", "oak"))
    return out


# ---------------------------------------------------------------- builders: tier 3, the jaw crusher
def build_jaw_frame():
    """The jaw crusher's cast frame: two cheeks on the bearers (a base flange, a post at the back wall, a post under
    the eccentric shaft's bearing, a post under the hinge's bearing and the chamber's side plate), the back wall
    with the back toggle's seat, the front wall behind the fixed jaw, the fixed jaw, the hinge pin; and the
    launder under the discharge that carries the product north to the spout."""
    p = "jawframe"
    out = []
    ey, ez = E_C
    hy, hz = H0
    zb0 = JAW_BACK_Z[0]
    zf1 = BEARER_S[1] - 0.4
    for tag, (x0, x1), (bx0, bx1) in (("w", CHEEK_W, (CHEEK_W[0] - 1.5, CHEEK_W[1])), ("e", CHEEK_E, (CHEEK_E[0], CHEEK_E[1] + 1.5))):
        out += [box([x0, JAW_BASE_Y, zb0], [x1, JAW_BASE_Y + 1.5, zf1], f"jawframe_base_{tag}", p, "iron"),
                box([x0, JAW_BASE_Y + 1.5, zb0], [x1, 31.5, JAW_BACK_Z[1]], f"jawframe_backpost_{tag}", p, "iron"),
                box([x0, JAW_BASE_Y + 1.5, ez - 3.0], [x1, ey - 2.6, ez + 3.0], f"jawframe_epost_{tag}", p, "iron"),
                box([bx0, ey - 2.6, ez - 2.6], [bx1, ey + 2.6, ez + 2.6], f"jawframe_ebearing_{tag}", p, "iron"),
                box([x0, JAW_BASE_Y + 1.5, hz - 1.8], [x1, hy - 1.8, hz + 1.8], f"jawframe_hpost_{tag}", p, "iron"),
                box([bx0 + 0.5 * (1 if tag == "w" else 0), hy - 1.8, hz - 1.8], [bx1 - 0.5 * (1 if tag == "e" else 0), hy + 1.8, hz + 1.8], f"jawframe_hbearing_{tag}", p, "iron"),
                box([x0, JAW_BASE_Y + 1.5, hz + 1.8], [x1, 42.5, zf1], f"jawframe_side_{tag}", p, "iron")]
    out += [box([JAW_X[0], JAW_BASE_Y, zb0], [JAW_X[1], 31.5, JAW_BACK_Z[1]], "jawframe_backwall", p, "iron"),
            box([JAW_X[0] + 2.0, B0[0] - 1.1, JAW_BACK_Z[1]], [JAW_X[1] - 2.0, B0[0] + 1.1, JAW_BACK_Z[1] + 0.4], "jawframe_backseat", p, "steel"),
            box([JAW_X[0], JAW_BASE_Y, FIXED_Z + 1.5], [JAW_X[1], 42.5, zf1], "jawframe_frontwall", p, "iron"),
            box([JAW_X[0] + 0.2, FACE_BOT[0] - 0.5, FIXED_Z], [JAW_X[1] - 0.2, 42.0, FIXED_Z + 1.5], "jawframe_fixedjaw", p, "steel")]
    out += rod("x", yz(H0), CHEEK_W[0] - 0.9, CHEEK_E[1] + 0.9, 0.9, "jawframe_hingepin", p, "steel")
    # the launder: from under the discharge (and the rolls' nip) down to the product spout
    lo, hi = (3.3, PED_Z[1] - 5.0), (0.8, SPOUT_Z[1])
    out.append(plank(lo, hi, SPOUT_X[0], SPOUT_X[1], "jawframe_launder", p, "iron", t=0.6))
    for tag, (sx0, sx1) in (("w", (SPOUT_X[0] - 0.8, SPOUT_X[0])), ("e", (SPOUT_X[1], SPOUT_X[1] + 0.8))):
        out.append(plank((lo[0] + 2.4, lo[1]), (hi[0] + 2.2, hi[1]), sx0, sx1, f"jawframe_launder_{tag}", p, "iron", t=2.4 + 0.6))
    out.append(box([SPOUT_X[0] - 0.8, 0.0, lo[1]], [SPOUT_X[1] + 0.8, lo[0] + 2.4, lo[1] + 0.6], "jawframe_launder_end", p, "iron"))
    return out


def build_eshaft():
    """The eccentric shaft: its eccentric under the pitman's head (drawn up, at angle 0), the flywheel west of the west
    cheek and the spur wheel east of the east cheek, meshing the line shaft's pinion; collars at the bearings."""
    p = "eshaft"
    c = yz(E_C)
    out = rod("x", c, FLY_X[0] - 0.4, X_DRIVE[1] + 0.5, 1.6, "eshaft_shaft", p, "steel")
    out += disc("x", yz((E_C[0] + ECC, E_C[1])), JAW_X[0] + 3.0, JAW_X[1] - 3.0, 3.1, "eshaft_eccentric", p, "steel", k=6)
    out += annulus("x", c, *FLY_X, FLY_R - 1.4, FLY_R, 16, "eshaft_flyrim", p, "iron")
    out += [radial("x", c, FLY_X[0] + 0.5, FLY_X[1] - 0.5, 2.4, FLY_R - 1.2, 1.0, TAU * (k + 0.5) / 6, f"eshaft_flyarm{k + 1}", p, "iron") for k in range(6)]
    out += disc("x", c, FLY_X[0] - 0.2, FLY_X[1] + 0.2, 2.6, "eshaft_flyhub", p, "iron")
    out += wheel("x", c, *X_DRIVE, E_TEETH * MOD1 / 2, E_TEETH, MOD1, "eshaft_wheel", p, "iron", 2.6, phase=driven_phase(E_C, E_TEETH))
    out += disc("x", c, CHEEK_W[0] - 2.1, CHEEK_W[0] - 1.5, 2.3, "eshaft_collar_w", p, "steel")
    out += disc("x", c, CHEEK_E[1] + 1.5, CHEEK_E[1] + 2.1, 2.3, "eshaft_collar_e", p, "steel")
    return out


def build_linkage():
    """The pitman, the two toggles, the swinging jaw, the tension rods and their springs, drawn at the linkage's
    design pose (the eccentric centred) and each put at its mean pose; the rig's harmonics move them from there."""
    out = []
    (ey, ez), (py, pz) = E_C, P0
    # the pitman: a strap round the eccentric, a body down to its foot, the foot with the toggles' seats
    pit = annulus("x", yz(E_C), JAW_X[0] + 3.0, JAW_X[1] - 3.0, 3.15, 4.0, 14, "pitman_strap", "pitman", "iron")
    pit += [box([JAW_X[0] + 4.0, py + FOOT_UP, ez - 1.0], [JAW_X[1] - 4.0, ey - 3.7, ez + 1.0], "pitman_body", "pitman", "iron"),
            box([JAW_X[0] + 3.4, py - 1.3, pz - 1.0], [JAW_X[1] - 3.4, py + FOOT_UP, pz + 1.0], "pitman_foot", "pitman", "iron")]
    out += placed(pit, FITS["a_p"][0], P0, MEAN_P)
    # the toggles: each from its outer seat to a seat in the pitman's foot, short of its pin so the two never meet
    def toggle(a, b, name):
        d = math.dist(a, b)
        tip = (b[0] + (a[0] - b[0]) * TOGGLE_TIP / d, b[1] + (a[1] - b[1]) * TOGGLE_TIP / d)
        return strut(yz(a, 22.0), yz(tip, 22.0), 1.4, 7.0, name, name.split("_")[0], "steel")
    out += placed([toggle(B0, P0, "backtoggle_plate")], FITS["a_b"][0], B0, B0)
    out += placed([toggle(J0, P0, "fronttoggle_plate")], FITS["a_f"][0], J0, MEAN_J)
    # the swinging jaw: the steel face plate, the iron slab behind it, the hinge boss, the toggle seat, the rods' lugs
    bk = JAW_BACK
    sx0, sx1 = JAW_X[0] + 0.3, JAW_X[1] - 0.3
    jaw = [plank(FACE_BOT, FACE_TOP, sx0, sx1, "swingjaw_face", "swingjaw", "steel", t=1.0, down=bk),
           plank((FACE_BOT[0] + bk[0], FACE_BOT[1] + bk[1]), (FACE_TOP[0] + bk[0], FACE_TOP[1] + bk[1]), sx0 + 0.02, sx1 - 0.02,
                 "swingjaw_slab", "swingjaw", "iron", t=SLAB_T - 1.0, down=bk)]
    top = (FACE_TOP[0] + SLAB_T * bk[0], FACE_TOP[1] + SLAB_T * bk[1])
    jaw += disc("x", yz(H0), sx0, sx1, 1.8, "swingjaw_boss", "swingjaw", "iron", k=6)
    jaw.append(strut(yz((top[0] - 0.6, top[1] + 1.2), 22.0), yz(H0, 22.0), 2.4, sx1 - sx0 - 0.04, "swingjaw_web", "swingjaw", "iron"))
    jaw.append(box([JAW_X[0] + 2.0, J0[0] - 1.1, J0[1] - 0.4], [JAW_X[1] - 2.0, J0[0] + 1.1, J0[1] + 0.3], "swingjaw_seat", "swingjaw", "steel"))
    for tag, x in zip("we", ROD_X):
        jaw += eye("x", yz(K0), x - 0.4, x + 0.4, 0.42, 0.9, f"swingjaw_lug{tag}", "swingjaw", "iron")
    out += placed(jaw, FITS["beta"][0], H0, H0)
    # the tension rods, pinned in the jaw's lugs, through the back wall to a nut; the springs between
    rods = []
    for tag, x in zip("we", ROD_X):
        rods += rod("z", (x, K0[0], 0.0), ROD_NUT_Z[0], K0[1] + 0.3, 0.35, f"rods_{tag}", "rods", "steel", k=2)
        rods += disc("z", (x, K0[0], 0.0), *ROD_NUT_Z, 0.75, f"rods_nut{tag}", "rods", "steel", k=3)
        rods += rod("x", yz(K0), x - 0.5, x + 0.5, 0.3, f"rods_pin{tag}", "rods", "steel", k=2)
    translate(rods, [0.0, MEAN_K[0] - K0[0], MEAN_K[1] - K0[1]])
    out += rods
    for c in range(SPRING_COILS):
        share = spring_share(c)
        coils = []
        for tag, x in zip("we", ROD_X):
            z = spring_z(c)
            coils += eye("z", (x, K0[0], 0.0), z - 0.18, z + 0.18, 0.35, 0.8, f"spring{c + 1}_{tag}", f"spring{c + 1}", "steel")
        translate(coils, [0.0, 0.0, share * (MEAN_K[1] - K0[1])])
        out += coils
    return out


SPRING_COILS = 3
FOOT_UP = 2.4                                # the pitman's foot rises this far over its pin: the toggles swing under its body
TOGGLE_TIP = 0.6                             # each toggle stops this far short of the foot's pin, in its seat


def spring_z(c):
    """Coil c's centre (designed): evenly between the nut's south face and the back wall's north face."""
    a, b = ROD_NUT_Z[1], JAW_BACK_Z[0]
    return a + (b - a) * (c + 0.5) / SPRING_COILS


def spring_share(c):
    """The share of the nut's travel coil c takes: all of it at the nut, none at the wall."""
    a, b = ROD_NUT_Z[1], JAW_BACK_Z[0]
    return (b - spring_z(c)) / (b - a)


# ---------------------------------------------------------------- builders: tier 4, the crushing rolls
def build_rolls():
    """The two rolls (a shell with bolt heads on its ends, a shaft), the pair gears at their west ends that turn them
    towards each other, the north roll's wheel meshing the line shaft's pinion; the pedestals (the north roll's
    bearings fixed, the south roll's in a slide with two springs behind it); the rolls' hopper under the jaw's
    discharge; the oversize chute from the west face's inlet into it."""
    out = []
    pr = PAIR_N * PAIR_MOD / 2
    p1 = mesh_phase(yz(R1_C), 0.0, PAIR_N, yz(R2_C), PAIR_N)
    for tag, cen, part, x1 in (("1", R1_C, "roll1", X_DRIVE[1] + 0.5), ("2", R2_C, "roll2", PED_E[1] + 0.3)):
        c = yz(cen)
        out += disc("x", c, ROLL_X[0] + 0.3, ROLL_X[1] - 0.3, ROLL_R, f"{part}_shell", part, "steel", k=8)
        out += rod("x", c, X_PAIR[0] - 0.4, x1, SHAFT_R, f"{part}_shaft", part, "steel")
        for side, (a0, a1) in (("w", (ROLL_X[0] - 0.05, ROLL_X[0] + 0.5)), ("e", (ROLL_X[1] - 0.5, ROLL_X[1] + 0.05))):
            for k in range(4):
                out.append(radial("x", c, a0, a1, 3.6, 4.6, 1.0, TAU * k / 4 + (0.0 if tag == "1" else 0.3), f"{part}_bolt{side}{k + 1}", part, "iron"))
        out += gear("x", c, *X_PAIR, pr, PAIR_N, PAIR_MOD, f"{part}_pair", part, "steel", phase=0.0 if tag == "1" else p1, body_k=6)
        out += disc("x", c, X_PAIR[1], PED_W[0] - 0.05, 2.2, f"{part}_collarw", part, "steel")
    out += wheel("x", yz(R1_C), *X_DRIVE, R1_TEETH * MOD1 / 2, R1_TEETH, MOD1, "roll1_wheel", "roll1", "iron", 2.6, phase=driven_phase(R1_C, R1_TEETH))
    out += disc("x", yz(R1_C), PED_E[1] + 0.05, X_DRIVE[0] - 0.2, 2.2, "roll1_collare", "roll1", "steel")
    # the pedestals
    f = "rollframe"
    z0, z1 = PED_Z
    (y1c, z1c), (y2c, z2c) = R1_C, R2_C
    for tag, (x0, x1) in (("w", PED_W), ("e", PED_E)):
        out += [box([x0, 0.0, z0], [x1, 2.0, z1], f"rollframe_base_{tag}", f, "iron"),
                box([x0, 2.0, z0], [x1, y1c + 3.4, R1_Z + 4.0], f"rollframe_housing_{tag}", f, "iron"),
                box([x0, 2.0, R1_Z + 4.0], [x1, y2c - 2.6, z1], f"rollframe_lower_{tag}", f, "iron"),
                box([x0, y2c + 2.6, R1_Z + 4.0], [x1, y1c + 3.4, z1], f"rollframe_upper_{tag}", f, "iron"),
                box([x0 + 0.1, y2c - 2.6, z1 - 0.8], [x1 - 0.1, y2c + 2.6, z1], f"rollframe_end_{tag}", f, "iron"),
                box([x0 + 0.1, y2c - 2.6, z2c - 2.0], [x1 - 0.1, y2c + 2.6, z2c + 2.0], f"rollframe_block_{tag}", f, "iron")]
        for k, dy in enumerate((-1.4, 1.4), 1):
            yc = y2c + dy
            xc = (x0 + x1) / 2
            out += rod("z", (xc, yc, 0.0), z2c + 2.0, z1 - 0.8, 0.3, f"rollframe_springrod_{tag}{k}", f, "steel", k=2)
            for j in range(3):
                zc = z2c + 2.0 + (z1 - 0.8 - z2c - 2.0) * (j + 0.5) / 3
                out += eye("z", (xc, yc, 0.0), zc - 0.15, zc + 0.15, 0.3, 0.7, f"rollframe_coil_{tag}{k}{j + 1}", f, "steel")
    # the rolls' hopper: a sloping north board, a south board against the bearer, two end boards (the west one
    # under the oversize chute)
    hx0, hx1 = ROLL_X[0] + 0.2, ROLL_X[1] - 0.2
    top_y, low_y = BEARER_Y[1] - 0.1, 18.9
    out.append(plank((top_y, R1_Z + 1.4), (low_y, NIP_Z - 2.5), hx0, hx1, "rollframe_hopper_n", f, down=(0.0, -1.0)))
    out.append(box([hx0, low_y, BEARER_S[0] - 0.8], [hx1, top_y, BEARER_S[0]], "rollframe_hopper_s", f, "planks"))
    # the oversize chute from the west face's inlet down into the hopper, over its west board
    iz0, iz1 = INLET_Z
    zc = (iz0 + iz1) / 2
    y_in, y_out = INLET_Y + 0.8, CHUTE_LOW
    a, b = (INLET_X1, y_in - 0.4, zc), (hx0 + 0.6, y_out - 0.4, zc)
    out.append(strut(a, b, 0.8, iz1 - iz0, "rollframe_chute", f, "planks", axis="z"))
    for tag, (s0, s1) in (("n", (iz0 - 0.8, iz0)), ("s", (iz1, iz1 + 0.8))):
        out.append(strut((INLET_X1, y_in + 1.2, (s0 + s1) / 2), (hx0 - 0.9, y_out + 1.2 + 0.05, (s0 + s1) / 2), 2.4, s1 - s0,
                         f"rollframe_chute_{tag}", f, "planks", axis="z"))
    slope = (y_in - y_out) / (hx0 + 0.6 - INLET_X1)
    w_top = y_out + slope * 0.6 - 0.8                                 # under the chute's floor where it crosses the west board
    for tag, (x0, x1), ytop in (("w", (hx0 - 0.8, hx0), w_top), ("e", (hx1, hx1 + 0.8), top_y)):
        out.append(box([x0, low_y, R1_Z + 1.0], [x1, ytop, BEARER_S[0]], f"rollframe_hopper_{tag}", f, "planks"))
    return out


def build():
    els = build_frame() + build_drive()
    els += build_mortar() + build_camshaft() + build_stamps()
    els += build_jaw_frame() + build_eshaft() + build_linkage()
    els += build_rolls()
    return els


# ---------------------------------------------------------------- the tiers
TIERS = {"t2": ("mortar", "camshaft", "stamps"), "t3": ("jaw",), "t4": ("jaw", "rolls")}


def in_state(requires, state):
    """Whether a part with this `requires` is drawn in a build state ("frame", "t2", "t3", "t4")."""
    return requires is None or (state != "frame" and requires in TIERS[state])


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def per_class(v):
    return {"thin": r6(v), "thick": r6(v)}


WORK = {"name": "load crushed", "unit": "camshaft turns", "step": 0.005, "end": {"thin": float(STAMP_LOAD), "thick": float(STAMP_LOAD)}}
PATH = WORK
FOREVER = 1000.0
LEVER_ANGLE = -math.asin(CLUTCH_THROW / (CAM_Y - LEVER_PIVOT[1]))   # the lever turns this far about z to carry its yoke CLUTCH_THROW east
MAX_HARMONICS = 4


def harmonics(key, motion, axis, pivot=None, scale=1.0):
    """The rig drivers for one of the linkage's fits: one swing or slide per harmonic, on the axle's travel."""
    out = []
    for n, a, p in FITS[key][1][:MAX_HARMONICS]:
        d = {"type": motion, "axis": axis}
        if pivot is not None:
            d["pivot"] = pt(*yz(pivot))
        d.update({"amplitude": r6(a * scale / (B if motion == "slide" else 1.0)), "ratio": r6(n * E_RATIO), "phase": r6(p), "input": "travel"})
        out.append(d)
    return out


def gauge(motion, axis, amount, wins=None, pivot=None, mode=None):
    d = {"type": "gauge", "motion": motion, "axis": axis}
    if pivot is not None:
        d["pivot"] = pivot
    d["amount"] = amount
    if mode:
        d["mode"] = mode
    else:
        d["windows"] = wins
    return d


def stamp_ramps(i):
    """Stamp i's lift as ramps (from, ease, amount in voxels) open to the end of the load, from its first change in
    the load to its last, and the lift at W 0 those that straddle W 0 had already given."""
    a = stamp_phase(i)
    ramps, before = [], 0.0
    for k in range(-2, 2 * STAMP_LOAD + 1):
        w0 = a + 0.5 * k
        for f, e, amt in ((w0, LIFT_W, DROP), (w0 + LIFT_W, DROP_W, -DROP)):
            if f + e <= 0 or f >= STAMP_LOAD:
                continue
            ramps.append((f, e, amt))
            if f < 0:
                before += amt * (-f / e)
    return ramps, before


def stamp_drivers(i):
    ramps, before = stamp_ramps(i)
    out = [gauge("slide", "y", per_class(amt / B), [{"from": r6(f), "to": FOREVER, "ease": r6(e)}]) for f, e, amt in ramps]
    if abs(before) > 1e-9:
        out.append(gauge("slide", "y", per_class(-before / B), mode="present"))
    return out


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    rot_x = lambda centre, ratio, inp=None: dict({"type": "rotate", "axis": "x", "pivot": pt(*yz(centre)), "ratio": r6(ratio)},  # noqa: E731
                                                  **({"input": inp} if inp else {}))
    parts = [
        {"id": "entry", "match": ["entry_*"], "requires": None, "drivers": [rot_x(ENTRY, 1.0)]},
        {"id": "rectb1", "match": ["rectb1_*"], "requires": None, "drivers": [rot_x(LINE, -RECT_RATIO)]},
        {"id": "idler", "match": ["idler_*"], "requires": None, "drivers": [rot_x(IDLER, -RECT_A2 / RECT_I)]},
        {"id": "rectb2", "match": ["rectb2_*"], "requires": None, "drivers": [rot_x(LINE, RECT_A2 / RECT_B2)]},
        {"id": "line", "match": ["line_*"], "requires": None, "drivers": [rot_x(LINE, LINE_RATIO, "travel")]},
        # tier 2: the battery
        {"id": "bullwheel", "match": ["bullwheel_*"], "requires": "camshaft", "drivers": [rot_x(CAM_C, BULL_RATIO, "travel")]},
        {"id": "camshaft", "match": ["camshaft_*"], "requires": "camshaft", "drivers": [rot_x(CAM_C, TAU, "work")]},
        {"id": "cone", "match": ["cone_*"], "requires": "camshaft",
         "drivers": [rot_x(CAM_C, TAU, "work"), gauge("slide", "x", per_class(CLUTCH_THROW / B), mode="present")]},
        {"id": "clutchlever", "match": ["clutchlever_*"], "requires": "camshaft",
         "drivers": [gauge("rotate", "z", per_class(LEVER_ANGLE), pivot=pt(*LEVER_PIVOT), mode="present")]},
        {"id": "camframe", "match": ["camframe_*"], "requires": "camshaft", "drivers": []},
    ]
    for i in range(STAMPS):
        parts.append({"id": f"stamp{i + 1}", "match": [f"stamp{i + 1}_*"], "requires": "stamps", "drivers": stamp_drivers(i)})
    parts += [
        {"id": "guides", "match": ["guides_*"], "requires": "stamps", "drivers": []},
        {"id": "mortar", "match": ["mortar_*"], "requires": "mortar", "drivers": []},
        # tier 3: the jaw crusher
        {"id": "eshaft", "match": ["eshaft_*"], "requires": "jaw", "drivers": [rot_x(E_C, E_RATIO, "travel")]},
        {"id": "pitman", "match": ["pitman_*"], "requires": "jaw",
         "drivers": harmonics("a_p", "swing", "x", MEAN_P) + harmonics("py", "slide", "y") + harmonics("pz", "slide", "z")},
        {"id": "backtoggle", "match": ["backtoggle_*"], "requires": "jaw", "drivers": harmonics("a_b", "swing", "x", B0)},
        {"id": "fronttoggle", "match": ["fronttoggle_*"], "requires": "jaw",
         "drivers": harmonics("a_f", "swing", "x", MEAN_J) + harmonics("jy", "slide", "y") + harmonics("jz", "slide", "z")},
        {"id": "swingjaw", "match": ["swingjaw_*"], "requires": "jaw", "drivers": harmonics("beta", "swing", "x", H0)},
        {"id": "rods", "match": ["rods_*"], "requires": "jaw", "drivers": harmonics("ky", "slide", "y") + harmonics("kz", "slide", "z")},
    ]
    for c in range(SPRING_COILS):
        parts.append({"id": f"spring{c + 1}", "match": [f"spring{c + 1}_*"], "requires": "jaw",
                      "drivers": harmonics("kz", "slide", "z", scale=spring_share(c))})
    parts += [
        {"id": "jawframe", "match": ["jawframe_*"], "requires": "jaw", "drivers": []},
        # tier 4: the rolls
        {"id": "roll1", "match": ["roll1_*"], "requires": "rolls", "drivers": [rot_x(R1_C, R1_RATIO, "travel")]},
        {"id": "roll2", "match": ["roll2_*"], "requires": "rolls", "drivers": [rot_x(R2_C, R2_RATIO, "travel")]},
        {"id": "rollframe", "match": ["rollframe_*"], "requires": "rolls", "drivers": []},
        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses
REST = (0.0, 0.0, 0.0, 0, 0.0)               # (theta, travel, W, k, p): no load; the authored pose


def inputs_of(pose):
    th, ps, W, k, p = pose[:5]
    return {"theta": th, "travel": ps, "work": W, "size": k, "presence": p}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path or PATH)


def pose_at(W, k=1, theta=None):
    """A pose of a load in progress at W, the axle at the battery's drawn pace (the clutch in)."""
    th = TAU * TURNS_PER_REV * W if theta is None else theta
    return (th, abs(th), W, k, 1.0)


def turning(psi, theta=None):
    """A pose of the axle having travelled psi, no load on (the jaw and the rolls turn; the battery stands)."""
    return (psi if theta is None else theta, psi, 0.0, 0, 0.0)


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("infeed", "output", "oversize")
CELL_ANCHORS = (("power", POWER_CELL, POWER_FACE), ("infeed", INFEED_CELL, INFEED_FACE), ("output", OUTPUT_CELL, OUTPUT_FACE),
                ("oversize", OVERSIZE_CELL, OVERSIZE_FACE))


def make_rig(parts):
    progress_of({"work": WORK})
    rig = {"_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the bottom middle cell of the "
                       "north face, over the product spout. One machine upgraded in place (#711): the frame's parts have no "
                       "requires; tier 2 is mortar, camshaft and stamps, tier 3 jaw, tier 4 jaw and rolls (tiers). work is a load "
                       "at tier 2, W, in camshaft turns (stamps.revsPerLoad a load); k is the load (1 or 2, alike); p its presence: "
                       "the battery's clutch is in while p is 1. The jaw crusher and the rolls turn with the axle's travel. Gauge "
                       "windows are placed in camshaft turns. See the crusher's README for the schema.",
           "cells": []}
    for name, cell, face in CELL_ANCHORS:
        rig[f"{name}Cell"] = list(cell)
        rig[f"{name}Face"] = face
    rig["infeed"] = {"pos": pt(sum(HOPPER_RIM["x"]) / 2, HOPPER_TOP, sum(HOPPER_RIM["z"]) / 2)}
    rig["output"] = {"pos": pt(sum(SPOUT_X) / 2, 0.8, 0.0)}
    rig["oversize"] = {"pos": pt(0.0, INLET_Y + 0.8, sum(INLET_Z) / 2)}
    rig["work"] = dict(WORK)
    rig["stamps"] = {"turnsPerRev": r6(TURNS_PER_REV), "revsPerLoad": STAMP_LOAD, "dropsPerRev": 2, "firingOrder": list(ORDER),
                     "_comment": f"turnsPerRev: axle turns per camshaft turn with the clutch in, as the gearing is drawn (the "
                                 f"rectifier's {RECT_A}:{RECT_B}, the pinion's {PINION_N} to the bull wheel's {BULL_N}). revsPerLoad: "
                                 "the load's end in camshaft turns; each stamp drops dropsPerRev times a turn, in firingOrder. The "
                                 "stamps stand at W = 0 and W = revsPerLoad alike, so a load can start where the last one ended."}
    rig["tiers"] = {"2": list(TIERS["t2"]), "3": list(TIERS["t3"]), "4": list(TIERS["t4"]),
                    "_comment": "The requires values each tier's set fits; fitting the next tier's set takes the last tier's off "
                                "(tier 4 keeps tier 3's jaw and adds the rolls)."}
    rig["parts"] = parts
    return rig


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0]. Gauge windows are in camshaft turns, so they
    stay as they are."""
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


def shipped_cells(shape, ship_parts, sp):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig, every tier's parts
    together (whatever is fitted lies inside them); then the lids."""
    written = flatten(shape["elements"], textures={})
    rest = [posed(w, _part_matrix(ship_parts, part_of(ship_parts, w.name), inputs_of(REST), sp)) for w in written]
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
    poses = [REST, pose_at(0.3), pose_at(1.77, k=2), turning(2.1), (1.3, 7.0, 2.2, 1, 1.0), (0.4, 2.0, 4.0, 2, 0.5)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose), sp), d, poses)
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
    """theta in {0, 1.1, -2.3, 2.9} with psi, no load; for each load class, W over the stamps' lifts and drops in the
    first, second and last turns, W at 0 and at the end, p at 1 and 0.4."""
    out = []
    for i, th in enumerate((0.0, 1.1, -2.3, 2.9)):
        for extra in (0.0, 7.3):
            out.append((th, round(abs(th) + extra, 6), 0.0, 0, 0.0))
    for k in (1, 2):
        ws = {0.0, float(STAMP_LOAD)}
        for base in (0.0, 1.0, STAMP_LOAD - 1.0):
            for e in (0.03, 0.08, 0.15, 0.175, 0.185, 0.21, 0.26, 0.33, 0.37, 0.44, 0.49, 0.55, 0.66, 0.79, 0.93):
                ws.add(round(base + e, 6))
        for i, W in enumerate(sorted(ws)):
            th = r6(TAU * TURNS_PER_REV * W)
            for p in ((1.0,) if i % 5 else (1.0, 0.4)):
                out.append((th, round(abs(th), 6), W, k, p))
    return out


def reference_json(ship_parts, sp):
    poses = []
    for pose in reference_poses():
        th, ps, W, k, p = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose), sp)) for q in ship_parts}
        poses.append({"theta": th, "travel": ps, "work": W, "size": k, "presence": p, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped crusher-rig.json's parts and work: each part's matrix as 3 rows of 4 "
                        "(block units) at each pose (W in camshaft turns). The site's tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Every tier's parts are in this "
             "shape; the rig's requires say which tier each belongs to. Keep element names when editing: the rig finds its "
             "parts by them.", TEXTURES, tex_size=TEX)


def coplanar_poses():
    """(pose, state): each tier at rest and running, so faces that never show together are never compared."""
    return (REST + ("t4",), pose_at(1.23) + ("t2",), pose_at(2.61) + ("t2",), REST + ("t2",), turning(1.7) + ("t4",), turning(4.2) + ("t3",))


def shown(posed_els, pose):
    """The posed elements of the pose's build state; the rest moved far away (copies; the order kept), so the
    z-fighting fix and its check deal only with faces that show together."""
    state = pose[5] if len(pose) > 5 else "t4"
    out = []
    for e in posed_els:
        if not in_state(REQUIRES[e.part], state):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


REQUIRES = {}


def fix_coplanar(els, parts):
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose), coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the crusher's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_crusher
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
    ok = validate_crusher.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "crusher.json", args.out / "crusher_frame.json", args.out / "crusher-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "crusher.json", SHAPE_DIR / "crusher_frame.json", RIG_DIR / "crusher-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts, ship["work"])
    ok = validate_crusher.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
