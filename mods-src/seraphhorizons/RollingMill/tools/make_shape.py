#!/usr/bin/env python3
"""Generate the rolling mill's shapes, rig and reference poses.

The rolling mill is a two-high mill with grooved rolls after Henry Cort's patent of 1783, rolling a hot
iron bar into angle iron. Two cast-iron housings on a sole plate hold a top and a bottom roll in chocks with
brasses, the top roll's chocks set by a screw through each housing's cap. Each roll's barrel carries three
passes of diminishing size, cut half in each roll: the top roll a V-groove matching the bar's outside, the
bottom roll a ridge matching its inside, from a shallow 150-degree vee through 120 degrees to the finished
90-degree angle (legs 4 voxels outside, 1 thick: the press brake's angle). A pinion housing beside the stand
carries two equal pinions that turn the rolls at one speed in opposite directions, through short spindles
with wobbler ends in coupling boxes.

Not Cort's: the drive and the handling. The vanilla axle comes in along z on the south face and turns the
bottom pinion's shaft through a crown wheel and pinion, 1:3 (a slow, heavy roll speed), and a double-start
worm on the same entry shaft turns the tumbling shaft under the catcher's table once a pass. Its cams tilt
the catcher's table, which lifts the rolled bar against the top roll's back, so the top roll (whose top runs
towards the front) pulls it up and carries it over its crown, as the period catcher handed it back over the
top roll; a bell crank on the same shaft works the transfer fingers that move the bar one groove along first.
In front, a counterweighted swinging tray takes the bar from the top roll, swings down under its weight and
lets it slide into the next groove. Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    rollingmill.json         the whole machine, every moving part   (assets/.../shapes/block/)
    rollingmill_frame.json   the static frame only                  (assets/.../shapes/block/)
    rollingmill-rig.json     cells, anchors and the part rig        (assets/.../config/)
    rig-reference.json       every part's matrix at a grid of poses (tests/RollingMill/)

or, with `--out DIR`, all of them into DIR. It validates its own output (validate_rollingmill.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
machine box's north-west-bottom corner (the "build frame"); `shipped()` moves it so the controller cell
(ORIGIN_CELL, the fore tray's) is [0,0,0]. The bar travels south (+z) through the passes: the fore tray at the
north end, the stand in the middle, the catcher's table at the south end, where the axle comes in.

The rig's inputs, as this machine uses them (README "Rig schema"):

    theta  the axle angle: the entry shaft, its pinion and its worm; the crown wheel, the pinions, the
           spindles and the rolls turn at a third of it
    W      the rig's work, in passes: 0..3, one pass a unit, geared to the axle (6 turns a pass). The
           tumbling shaft turns once a pass; the tables, the transfer and the bar are gauges on W
    k      the bar: 0 none, 1 a hot iron bar (one class; the metal is the gameplay's to settle)
    p      its presence, 0..1
    oil    the oil tank's fill, 0..1: the two top chocks' sight-feed cups
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
from machinegen.geometry import IDENT, TRANSPARENT, El, flatten, rotate, translate  # noqa: E402
from machinegen.output import (reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               shift_point, worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import apply, part_matrix as _part_matrix, part_of, posed, progress_of, validate_driver  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "RollingMill" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/RollingMill/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
DEG = math.pi / 180

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 4, 2, 3          # four wide (the rolls' axis), two high, three long (the bar's path)
ORIGIN_CELL = (2, 0, 0)                      # the controller: the fore tray's cell, nearest the player
POWER_CELL, POWER_FACE = (0, 0, 2), "south"  # the vanilla axle comes in along z at (x 8, y 8), that cell's centre

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "iron": "game:block/metal/plate/iron",              # wrought iron: shafts, plates, links, fingers
    "castiron": "game:block/metal/sheet-plain/iron5",   # castings: the housings, the pinion housing, the sole plate, the chocks
    "roll": "game:block/metal/sheet-plain/iron1",       # the rolls' and the pinions' turned faces, bright
    "bar": "game:block/metal/ingot/iron",               # the work: a hot iron bar, rolled to angle (k = 1)
    "barsteel": "game:block/metal/ingot/steel",         # ... or a steel one (k = 2): the same bar, the same passes
    "brass": "game:block/metal/sheet/brass1",           # the brasses the necks run in, the oil cups
    "glass": "game:block/glass/plain",
    "oil": "game:block/liquid/honey",
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the rolls and the passes
ROLL_Z = 20.0                                # both rolls' axes (along x), and the pinions'
BOT_Y = 8.0                                  # the bottom roll's axis: the entry shaft's height, so the crown wheel's axis meets it
R_ROLL = 5.5                                 # the barrel's collars
ROLL_GAP = 0.5                               # between the collars
TOP_Y = BOT_Y + 2 * R_ROLL + ROLL_GAP        # 19.5
PASS_LOW = BOT_Y + R_ROLL + 0.05             # 13.55: the bar's lowest points (its legs' inner tips) in a pass, on every table
NECK_R = 2.75
BARREL_X = (31.8, 56.2)                      # 0.3 clear of the housings' inner faces
HOUSINGS = (("w", (28.0, 31.5)), ("e", (56.5, 60.0)))
LANES = (36.5, 44.5, 52.5)                   # the three grooves' centres; the transfer moves the bar one lane (8) a pass
LANE_PITCH = 8.0
DELIVER_X = LANES[-1] + LANE_PITCH           # where the finished angle is pushed to, off the end of the barrel
PASSES = ({"angle": 150.0, "leg": 3.75, "t": 1.6},   # each groove's section: the vee's included angle, a leg outside, thick
          {"angle": 120.0, "leg": 3.9, "t": 1.25},
          {"angle": 90.0, "leg": 4.0, "t": 1.0})     # the finished angle: the press brake's (legs 4 outside, 1 thick)
BAR_LENGTHS = (10.0, 12.5, 16.0)             # the bar out of each pass (about constant volume from the billet)
BILLET = {"w": 7.0, "t": 2.0, "len": 8.0}    # the billet: a flat bar, 7 x 2, 8 long
CLASSES = ("thin", "thick")                  # the bar's two classes, the same bar in two metals (the gameplay's to settle)
BAR_SETS = (("thin", "i", "iron", "bar"), ("thick", "s", "steel", "barsteel"))   # (class, element prefix, part suffix, texture)
STEPS = 4                                    # each flank of a groove or a ridge drawn in this many turned steps
GROOVE_CLR = 0.05                            # the rolls stand this far off the finished section
ROLL_RATIO = 1.0 / 3.0                       # the rolls turn a third of the axle (the crown wheel)

# ---------------------------------------------------------------- the stand
SOLE_Y = 1.5                                 # the sole plate's top
FOOT_Y = 3.0                                 # the housings' feet's top
POST_Z = ((13.0, 16.5), (23.5, 27.0))        # each housing's two posts (front, back); the window between them
CAP_Y = (23.5, 27.0)
CHOCK_BOT_Y = (FOOT_Y, 12.2)
CHOCK_TOP_Y = (15.3, CAP_Y[0])
BRASS_T = 0.6
SCREW_R = 1.1
SCREW_HEAD_Y = (28.6, 29.8)

# ---------------------------------------------------------------- the pinion housing, the spindles, the drive
PIN_CHEEKS = ((12.5, 14.0), (19.0, 20.5))
PINION_X = (14.3, 18.7)
PINION_MOD = 0.5
PINION_T = int(round((TOP_Y - BOT_Y) / PINION_MOD))   # 23: equal pinions on centres the rolls' distance apart
R_PINION = PINION_T * PINION_MOD / 2         # 5.75
COUPLE = ((20.8, 23.0), (25.4, 27.6))        # the coupling boxes on each spindle
WOBBLER_R = 1.8                              # the clover-leaf ends
BOX_R = (1.95, 2.6)                          # the coupling boxes' bore and outside
ENTRY_X = 8.0                                # the entry shaft along z at (x 8, y 8), from the south face
CROWN_T, CROWN_PIN_T = 27, 9                 # the crown wheel on the bottom pinion's shaft, its pinion on the entry shaft: 1:3
CROWN_MOD = 1.0 / 3.0
R_CROWN, R_CPIN = CROWN_T * CROWN_MOD / 2, CROWN_PIN_T * CROWN_MOD / 2   # 4.5 and 1.5
CROWN_FACE_X = ENTRY_X - R_CPIN              # 6.5: the crown wheel's pitch plane, west of the entry shaft
WEST_BRG_X = (1.2, 3.4)
PIN_TIE_Y = (26.4, 27.8)                     # the pinion housing's tie, over the top pinion

# ---------------------------------------------------------------- the tumbling shaft and its worm
WORM_STARTS = 2
WORM_TEETH = 18                              # 18 / 2 starts: the tumbling shaft turns once in 9 axle turns, once a pass
TURNS_PER_PASS = WORM_TEETH / WORM_STARTS    # axle turns a pass: the gameplay's pace (9), drawn
WORM_MOD = 1.0 / 3.0
R_WWHEEL = WORM_TEETH * WORM_MOD / 2         # 3
R_WORM = 1.0
TUMBLE_Z = 42.4                              # the tumbling shaft's z: under the lift rods, near the table's hinge
TUMBLE_Y = BOT_Y - R_WORM - R_WWHEEL         # 4: under the entry shaft's worm
TUMBLE_X = (3.6, 62.6)
WORM_LEAD = WORM_STARTS * math.pi * WORM_MOD

# ---------------------------------------------------------------- the catcher's table (behind the stand)
TABLE_Z = (27.4, 46.6)
TABLE_X = (30.4, 62.6)
TABLE_T = 1.0                                # its iron top
TABLE_PIVOT = (12.0, 45.6)                   # (y, z): the hinge under its far end
TABLE_SLOTS = ((30.7, 32.1), (36.7, 38.1))   # the fingers' slots across the top (z), 0.4 clear of them either side
TABLE_BEAMS = ((27.6, 28.8), (41.8, 43.0), (44.4, 46.4))   # its oak cross beams (z); the lift rods bear under the middle one
TABLE_UNDER = 10.7                           # the beams' bottoms
LIFT_CAM_X = (33.4, 59.4)                    # a lift cam and rod under each end of the table
ROLLER_R = 0.5
ROD_R = 0.4
ROD_GUIDE_Y = (9.1, 9.9)                     # the lift rods' guides: between their crowns' top, lifted, and their pads, down
CAM_BASE = 2.0                               # the lift cams' base circle
CAM_W = 1.2                                  # their faces
RUNOUT = 8.4                                 # the bar runs out this far past the rolls onto the table

# ---------------------------------------------------------------- the transfer
FINGER_Z = tuple((a + b) / 2 for a, b in TABLE_SLOTS)
FINGER_HALF = 0.3
FINGER_TOP = PASS_LOW + 0.7                  # enough to bear on the bar's legs' tip faces
TRANSFER_GAP = 0.3                           # each finger stands this far off its bar's west tip before it pushes
SLIDEBAR_Y = (9.6, 10.5)
STROKE = LANE_PITCH + TRANSFER_GAP           # the fingers' stroke: each bar goes one lane
DRUM_R = 3.0                                 # the transfer's barrel cam on the tumbling shaft
DRUM_X = (41.0, 51.6)
RIB = {"h": 0.7, "t": 0.6}                   # its rib, which the follower's fork straddles
FOLLOW_X0 = DRUM_X[0] + 0.9                  # the fork's centre at rest
FORK_PLAY = 0.42                             # the fork's prongs stand this far off the rib: they are short (FORK_HALF along z) for the steep ramps
FORK_HALF = 0.15

# ---------------------------------------------------------------- the fore tray (in front of the stand)
TRAY_PHI = -50.0                             # degrees: where on the top roll the bar leaves it, onto the tray (from the top, + towards the back)
TRAY_NEAR = 5.0                              # the tray's surface starts this far down the tangent from the roll
TRAY_LEN = 17.6                              # ... and ends at its stop this far down
STOP_T = 0.6
TRAY_DOWN_Z = 13.8                           # its near end's z when it has swung down
TRAY_SLOPE = 5.0                             # degrees: down, it falls towards the rolls
TRAY_X = (32.4, 56.0)
TRAY_T = 1.0

# ---------------------------------------------------------------- the oil cups (one on each top chock, one tank)
CUP = {"x": {"w": (28.9, 30.6), "e": (57.0, 58.7)}, "y": (27.4, 28.9), "z": (15.6, 17.1)}
OIL_Y = CUP["y"][0] + 0.01
OIL_EMPTY = 0.05
OIL_FULL = CUP["y"][1] - OIL_Y - 0.15


# ---------------------------------------------------------------- box helpers (the cylinder borer's)
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


def long_box(lo, hi, name, part, tex, axis=2, seg=16.0):
    """A long box split at block boundaries along `axis`, so its texture is not stretched."""
    cuts = [lo[axis]] + [k * seg for k in range(1, 8) if lo[axis] + 0.5 < k * seg < hi[axis] - 0.5] + [hi[axis]]
    out = []
    for i, (a, b) in enumerate(zip(cuts, cuts[1:])):
        l2, h2 = list(lo), list(hi)
        l2[axis], h2[axis] = a, b
        out.append(box(l2, h2, f"{name}{i + 1}" if len(cuts) > 2 else name, part, tex))
    return out


AX = {"x": 0, "y": 1, "z": 2}
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}   # a disc's angle 0 direction, and the in-plane direction it turns towards


def radial(axis, c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box from radius r0 to r1 about `axis` through c, a0..a1 along the axis, `width` across, at angle `ang`
    (radians, right-handed about +axis from the axis's reference direction: about x, from +y towards +z)."""
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


def disc(axis, c, a0, a1, r, name, part, tex, k=6, phase=0.0, step=0.012):
    """A plain round part: k strips as long as the 2k-gon is across, 180/k degrees apart, each a hair shorter
    than the last so no two ends share a plane."""
    a = AX[axis]
    u, w = REF[axis]
    half = r * math.tan(math.pi / (2 * k))
    out = []
    st = min(step, (a1 - a0) / (4 * k))
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


def gear(axis, c, a0, a1, pitch_r, n, module, name, part, tex, phase=0.0, body_k=6):
    """A spur gear: a polygon body to the root and n teeth of `module` (phase: the angle of tooth 1)."""
    width = math.pi * module / 2
    root, tip = pitch_r - 1.25 * module, pitch_r + module
    body = disc(axis, c, a0, a1, root, f"{name}_body", part, tex, k=body_k, phase=phase)
    return body + [radial(axis, c, a0 + 0.03, a1 - 0.03, root - 0.25, tip, width, phase + TAU * i / n, f"{name}_tooth{i + 1}", part, tex)
                   for i in range(n)]


def eye(axis, c, a0, a1, r_in, r_out, name, part, tex):
    """A square eye round an axis: four bars."""
    a = AX[axis]
    u, w = REF[axis]
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


def yz_rot(p, c, a):
    """Point p (y, z) turned by a radians about x through c (y, z), right-handed: (y, z) -> (y cos - z sin, y sin + z cos)."""
    y, z = p[0] - c[0], p[1] - c[1]
    ca, sa = math.cos(a), math.sin(a)
    return (c[0] + y * ca - z * sa, c[1] + y * sa + z * ca)


BOT_C = (0.0, BOT_Y, ROLL_Z)
TOP_C = (0.0, TOP_Y, ROLL_Z)
ENTRY_C = (ENTRY_X, BOT_Y, 0.0)
TUMBLE_C = (0.0, TUMBLE_Y, TUMBLE_Z)


# ---------------------------------------------------------------- posing
def posed_el(el, m):
    """machinegen's `posed`, except for an element turned off the world axes in a part that a world-axis stretch
    scales (the bar's legs, turned about z, grown and shrunk along z): `posed` sizes each of its axes by the sum of
    the scales its components fall on, which overstates a turned axis; here each axis is sized by how far the matrix
    stretches it. Every element is so scaled along one of its own axes (it stays a box)."""
    lin = [row[:3] for row in m[:3]]
    turned = any(1e-9 < abs(v) < 1 - 1e-9 for row in el.r for v in row)
    scaled = any(abs(math.sqrt(sum(lin[i][j] ** 2 for i in range(3))) - 1) > 1e-9 for j in range(3))
    flat = all(v == 0.0 for row in lin for v in row)
    if not (turned and scaled) or flat:
        return posed(el, m)
    out = posed(el, m)
    for k in range(3):
        col = [el.r[j][k] for j in range(3)]
        w = [sum(lin[i][j] * col[j] for j in range(3)) for i in range(3)]
        out.size[k] = el.size[k] * math.sqrt(sum(c * c for c in w))
    return out


# ---------------------------------------------------------------- the bar's sections
def section(j):
    """Pass j's section (0-based), an inverted vee lying apex up across the pass: the angle's legs `leg` outside
    and `t` thick. Returns its figures in voxels: s, c (sin and cos of half the included angle), w (an outer
    tip's x from the centre line), ix (an inner tip's), y_apex (the outside apex), y_tip (an outer tip), y_in (the
    inside apex). The inner tips are its lowest points, at PASS_LOW, as on every table."""
    p = PASSES[j]
    half = math.radians(p["angle"]) / 2
    s, c = math.sin(half), math.cos(half)
    a, t = p["leg"], p["t"]
    y_apex = PASS_LOW + a * c + t * s
    return {"a": a, "t": t, "s": s, "c": c, "w": a * s, "ix": a * s - t * c, "y_apex": y_apex,
            "y_tip": y_apex - a * c, "y_in": y_apex - t / s}


def bar_top(j, dx):
    """The section's top at |dx| from its centre line (its outside), or None beyond it."""
    q = section(j)
    dx = abs(dx)
    return None if dx > q["w"] + 1e-9 else q["y_apex"] - dx * q["c"] / q["s"]


def bar_bottom(j, dx):
    """The section's bottom at |dx| (its inside, then its legs' tip faces), or None beyond it."""
    q = section(j)
    dx = abs(dx)
    if dx > q["w"] + 1e-9:
        return None
    if dx <= q["ix"]:
        return q["y_in"] - dx * q["c"] / q["s"]
    return PASS_LOW + (dx - q["ix"]) * (q["y_tip"] - PASS_LOW) / (q["w"] - q["ix"])


def groove_steps(j, top):
    """The turned steps of groove j on the top roll (a vee cut in it, the bar's outside) or of its ridge on the
    bottom roll (the bar's inside and its tips): [(x0, x1, r)], from the groove's west edge to its east edge, each
    step's radius the largest that keeps GROOVE_CLR off the section over the whole step (the top roll's capped at
    its collars; the bottom roll's never under them: the section's inner tips stand GROOVE_CLR over the collars)."""
    q = section(j)
    xc, w, n = LANES[j], q["w"], STEPS

    def allowed(dx):
        if top:
            return TOP_Y - bar_top(j, dx) - GROOVE_CLR
        return bar_bottom(j, dx) - BOT_Y - GROOVE_CLR

    def least(d0, d1):
        pts = [d0, d1] + ([q["ix"]] if d0 < q["ix"] < d1 else []) + [d0 + (d1 - d0) * i / 8 for i in range(1, 8)]
        r = min(allowed(d) for d in pts)
        return min(R_ROLL, r) if top else max(R_ROLL, r)      # the top roll's vee is cut in; the bottom roll's ridge stands up
    edges = [w * k / n for k in range(n + 1)]
    out = [(xc - edges[1], xc + edges[1], least(0.0, edges[1]))]
    for k in range(1, n):
        r = least(edges[k], edges[k + 1])
        out.insert(0, (xc - edges[k + 1], xc - edges[k], r))
        out.append((xc + edges[k], xc + edges[k + 1], r))
    return out


def barrel_profile(top):
    """The whole barrel along x: [(x0, x1, r)], the collars at R_ROLL between and outside the grooves."""
    out, x = [], BARREL_X[0]
    for j in range(len(PASSES)):
        steps = groove_steps(j, top)
        if steps[0][0] > x + 1e-6:
            out.append((x, steps[0][0], R_ROLL))
        out += steps
        x = steps[-1][1]
    out.append((x, BARREL_X[1], R_ROLL))
    merged = []
    for x0, x1, r in out:                         # neighbours turned to one radius are one step
        if merged and abs(merged[-1][2] - r) < 1e-9:
            merged[-1] = (merged[-1][0], x1, r)
        else:
            merged.append((x0, x1, r))
    return merged


# ---------------------------------------------------------------- builders: the rolls
def build_roll(top):
    """A roll: its barrel (collars and the turned steps of its three passes, each a 12-gon), its necks through the
    housings, and the wobbler on its west neck's end, in the coupling box."""
    pre, part = ("troll", "rolltop") if top else ("broll", "rollbottom")
    c = TOP_C if top else BOT_C
    out = []
    for i, (x0, x1, r) in enumerate(barrel_profile(top), 1):
        out += disc("x", c, x0, x1, r, f"{pre}_b{i:02d}", part, "roll")
    out += disc("x", c, COUPLE[1][1], BARREL_X[0], NECK_R, f"{pre}_neckw", part, "roll")
    out += disc("x", c, BARREL_X[1], HOUSINGS[1][1][1] - 0.4, NECK_R, f"{pre}_necke", part, "roll")
    out += wobbler(c, (COUPLE[1][0] + COUPLE[1][1]) / 2 + 0.05, COUPLE[1][1], f"{pre}_wob", part)
    return out


def wobbler(c, x0, x1, name, part):
    """A clover-leaf (wobbler) end: two crossed bars along x."""
    out = []
    for i, ang in enumerate((math.pi / 4, 3 * math.pi / 4), 1):
        out.append(radial("x", c, x0, x1, -WOBBLER_R, WOBBLER_R, 0.9 - 0.01 * i, ang, f"{name}{i}", part, "iron"))
    return out


def build_spindle(top):
    """A short spindle between the pinion's shaft and the roll's neck: its wobbler ends and body, and the two
    coupling boxes slipped over the joints."""
    pre, part = ("tsp", "spindletop") if top else ("bsp", "spindlebottom")
    c = TOP_C if top else BOT_C
    (a0, a1), (b0, b1) = COUPLE
    out = wobbler(c, (a0 + a1) / 2 + 0.05, a1, f"{pre}_wobw", part)
    out += wobbler(c, b0, (b0 + b1) / 2 - 0.05, f"{pre}_wobe", part)
    out += disc("x", c, a1, b0, 1.5, f"{pre}_body", part, "iron", k=4)
    for i, (x0, x1) in enumerate(COUPLE, 1):
        out += eye("x", c, x0, x1, BOX_R[0], BOX_R[1], f"{pre}_box{i}_", part, "iron")
    return out


# ---------------------------------------------------------------- builders: the pinions and the drive
def build_pinions():
    """The pinion housing's two equal pinions on their shafts, meshing on the line of centres; the bottom
    shaft runs west through the housing to the crown wheel and its outer bearing, the top one is a stub. Each
    shaft ends east in a wobbler in its spindle's first coupling box."""
    out = []
    for top in (False, True):
        pre, part = ("tpin", "piniontop") if top else ("bpin", "pinionbottom")
        c = TOP_C if top else BOT_C
        ph = (math.pi + math.pi / PINION_T) if top else 0.0
        out += gear("x", c, *PINION_X, R_PINION, PINION_T, PINION_MOD, f"{pre}_gear", part, "roll", phase=ph)
        x0 = WEST_BRG_X[0] + 0.2 if not top else PIN_CHEEKS[0][0] + 0.3
        out += disc("x", c, x0, COUPLE[0][0] + 0.2, 1.4, f"{pre}_shaft", part, "iron", k=4)
        out += wobbler(c, COUPLE[0][0], (COUPLE[0][0] + COUPLE[0][1]) / 2 - 0.05, f"{pre}_wob", part)
    return out


def build_crown():
    """The crown wheel on the bottom pinion's shaft: a cast disc with its 27 teeth standing out of its east face,
    meshing the entry shaft's pinion at its south side."""
    out = disc("x", BOT_C, CROWN_FACE_X - 1.25 * CROWN_MOD - 1.0, CROWN_FACE_X - 1.25 * CROWN_MOD, R_CROWN + 0.6, "crown_disc", "crown", "castiron")
    out += disc("x", BOT_C, CROWN_FACE_X - 2.6, CROWN_FACE_X - 1.25 * CROWN_MOD - 0.9, 2.0, "crown_hub", "crown", "castiron")
    ph = math.pi / 2 + math.pi / CROWN_T          # a gap on the mesh, due south of the shaft
    width = math.pi * CROWN_MOD / 2
    for i in range(CROWN_T):
        out.append(radial("x", BOT_C, CROWN_FACE_X - 1.25 * CROWN_MOD - 0.05, CROWN_FACE_X + CROWN_MOD, R_CROWN - 0.5, R_CROWN + 0.5,
                          width, ph + TAU * i / CROWN_T, f"crown_tooth{i + 1:02d}", "crown", "castiron"))
    return out


CROWN_PIN_Z = (ROLL_Z + R_CROWN - 0.5, ROLL_Z + R_CROWN + 0.5)
ENTRY_BRG = ((27.6, 29.8), (46.2, 47.8))     # the entry shaft's bearings: inner, and outer at the south face
WORM_Z = (TUMBLE_Z - 2.2, TUMBLE_Z + 2.2)


def build_entry():
    """The entry shaft along z: the vanilla axle's oak cross profile in its outer bearing at the south face, then a
    round iron shaft carrying the double-start worm over the tumbling shaft's wheel, to the crown wheel's pinion
    at its north end."""
    x, y = ENTRY_X, BOT_Y
    z1 = 48.0
    z0 = ENTRY_BRG[1][0] - 0.2
    out = [box([x - 0.6, y - 1.5, z0], [x + 0.6, y + 1.5, z1], "entry_shafta", "entry", "oak"),
           box([x - 1.5, y - 0.6, z0 + 0.012], [x + 1.5, y + 0.6, z1 - 0.012], "entry_shaftb", "entry", "oak")]
    out += disc("z", ENTRY_C, CROWN_PIN_Z[0] - 0.6, z0 + 0.2, 0.6, "entry_rod", "entry", "iron", k=4)
    out += gear("z", ENTRY_C, *CROWN_PIN_Z, R_CPIN, CROWN_PIN_T, CROWN_MOD, "entry_pinion", "entry", "iron", phase=math.pi / 2, body_k=4)
    out += disc("z", ENTRY_C, WORM_Z[0], WORM_Z[1], R_WORM - 1.25 * WORM_MOD + 0.05, "entry_wormcore", "entry", "iron", k=4)
    n = int(12 * (WORM_Z[1] - WORM_Z[0]) / WORM_LEAD)
    lam = math.degrees(math.atan2(WORM_LEAD, TAU * R_WORM))
    chord = TAU * (R_WORM + 0.15) / 12 / math.cos(math.radians(lam)) + 0.08
    for s in range(WORM_STARTS):
        for i in range(n):
            ang = TAU * i / 12 + math.pi * s
            z = WORM_Z[0] + 0.2 + WORM_LEAD * i / 12
            el = box([x - chord / 2, y + R_WORM - 1.25 * WORM_MOD, z - 0.2], [x + chord / 2, y + R_WORM + WORM_MOD, z + 0.2],
                     f"entry_worm{s + 1}_{i + 1:02d}", "entry", "iron")
            rotate([el], "y", lam, (x, y + R_WORM, z))
            rotate([el], "z", math.degrees(ang) + 180.0, ENTRY_C)
            out.append(el)
    return out


# ---------------------------------------------------------------- builders: the stand (the frame)
def build_stand():
    """Oak sleepers under a cast-iron sole plate; on it the two housings, each two posts on a foot and a cap
    bolted across their tops, the window between holding the bottom and top chocks with their brasses round the
    necks, two distance pieces between them, and the screw down through the cap onto the top chock, set by a
    capstan head and a bar."""
    f = "frame"
    out = []
    for i, (z0, z1) in enumerate(((10.6, 13.4), (26.6, 29.4)), 1):
        out.append(box([25.8, 0.0, z0], [62.0, SOLE_Y - 0.6, z1], f"fr_sleeper{i}", f, "oak"))
    for tag, (x0, x1) in HOUSINGS:              # a sole plate under each housing, tied across at both ends, clear of the rolls' sweep
        out.append(box([x0 - 0.8, SOLE_Y - 0.6, 11.2], [x1 + 0.8, SOLE_Y, 28.8], f"fr_sole{tag}", f, "castiron"))
    for k, (z0, z1) in enumerate(((11.2, 12.9), (27.1, 28.8)), 1):
        out.append(box([HOUSINGS[0][1][1] + 0.8, SOLE_Y - 0.6, z0], [HOUSINGS[1][1][0] - 0.8, SOLE_Y, z1], f"fr_soletie{k}", f, "castiron"))
    for tag, (x0, x1) in HOUSINGS:
        p = f"fr_hs{tag}"
        fx = (x0 - 0.8, x1) if tag == "w" else (x0, x1 + 0.8)        # the foot spreads outwards, clear of the barrel
        out.append(box([fx[0], SOLE_Y, POST_Z[0][0] - 1.2], [fx[1], FOOT_Y, POST_Z[1][1] + 1.2], f"{p}_foot", f, "castiron"))
        for k, (z0, z1) in enumerate(POST_Z, 1):
            out.append(box([x0, FOOT_Y, z0], [x1, CAP_Y[0], z1], f"{p}_post{k}", f, "castiron"))
        out.append(box([x0 - 0.2, CAP_Y[0], POST_Z[0][0] - 0.4], [x1 + 0.2, CAP_Y[1], POST_Z[1][1] + 0.4], f"{p}_cap", f, "castiron"))
        xm = (x0 + x1) / 2
        out.append(box([xm - 1.6, CAP_Y[1], ROLL_Z - 1.6], [xm + 1.6, CAP_Y[1] + 0.9, ROLL_Z + 1.6], f"{p}_boss", f, "castiron"))
        for k, zb in enumerate((POST_Z[0][0] + 0.9, POST_Z[1][1] - 0.9), 1):      # the cap's bolts' heads
            out.append(box([xm - 0.5, CAP_Y[1], zb - 0.5], [xm + 0.5, CAP_Y[1] + 0.5, zb + 0.5], f"{p}_bolt{k}", f, "iron"))
        out += disc("y", (xm, 0.0, ROLL_Z), CHOCK_TOP_Y[1], SCREW_HEAD_Y[0] + 0.1, SCREW_R, f"{p}_screw", f, "iron", k=4)
        out += disc("y", (xm, 0.0, ROLL_Z), *SCREW_HEAD_Y, 1.6, f"{p}_head", f, "iron", k=4, phase=math.pi / 8)
        out.append(box([xm - 2.8, SCREW_HEAD_Y[0] + 0.35, ROLL_Z - 0.25], [xm + 2.8, SCREW_HEAD_Y[1] - 0.35, ROLL_Z + 0.25], f"{p}_tommy", f, "iron"))
        # the chocks: iron blocks in the window round each neck, a brass between, two distance pieces between them
        for top, (y0, y1) in ((False, CHOCK_BOT_Y), (True, CHOCK_TOP_Y)):
            c = TOP_C if top else BOT_C
            q = f"{p}_chock{'t' if top else 'b'}"
            zw = (POST_Z[0][1], POST_Z[1][0])
            r_in = NECK_R + BRASS_T
            out.append(box([x0 + 0.1, y0, zw[0]], [x1 - 0.1, c[1] - r_in, zw[1]], f"{q}_lo", f, "castiron"))
            out.append(box([x0 + 0.1, c[1] + r_in, zw[0]], [x1 - 0.1, y1, zw[1]], f"{q}_hi", f, "castiron"))
            out.append(box([x0 + 0.1, c[1] - r_in, zw[0]], [x1 - 0.1, c[1] + r_in, ROLL_Z - r_in], f"{q}_n", f, "castiron"))
            out.append(box([x0 + 0.1, c[1] - r_in, ROLL_Z + r_in], [x1 - 0.1, c[1] + r_in, zw[1]], f"{q}_s", f, "castiron"))
            out += eye("x", c, x0 - (0.15 if tag == "e" else 0.0), x1 + (0.15 if tag == "w" else 0.0), NECK_R, r_in, f"fr_brass{tag}{'t' if top else 'b'}_", f, "brass")
        for k, (z0, z1) in enumerate(((POST_Z[0][1], POST_Z[0][1] + 1.6), (POST_Z[1][0] - 1.6, POST_Z[1][0])), 1):
            out.append(box([x0 + 0.4, CHOCK_BOT_Y[1], z0], [x1 - 0.4, CHOCK_TOP_Y[0], z1], f"{p}_dist{k}", f, "iron"))
    return out


def build_drive_frame():
    """The pinion housing (two cast cheeks with their bearings, tied across the top, on a foot), the bottom pinion
    shaft's outer bearing, the entry shaft's two bearings on posts, and the tumbling shaft's three bearings."""
    f = "frame"
    out = [box([10.6, 0.0, 14.0], [23.0, SOLE_Y - 0.6, 26.0], "fr_pinsleeper", f, "oak"),
           box([PIN_CHEEKS[0][0] - 0.5, SOLE_Y - 0.6, 14.6], [PIN_CHEEKS[1][1] + 0.5, SOLE_Y, 25.4], "fr_pinfoot", f, "castiron")]
    for k, (x0, x1) in enumerate(PIN_CHEEKS, 1):
        out.append(box([x0, SOLE_Y, 14.6], [x1, BOT_Y - 2.0, 25.4], f"fr_pincheek{k}_lo", f, "castiron"))
        out.append(box([x0, BOT_Y + 2.0, 14.6], [x1, TOP_Y - 2.0, 25.4], f"fr_pincheek{k}_mid", f, "castiron"))
        out.append(box([x0, TOP_Y + 2.0, 14.6], [x1, PIN_TIE_Y[0], 25.4], f"fr_pincheek{k}_hi", f, "castiron"))
        for y in (BOT_Y, TOP_Y):
            out.append(box([x0, y - 2.0, 14.6], [x1, y + 2.0, ROLL_Z - 1.9], f"fr_pincheek{k}_n{int(y)}", f, "castiron"))
            out.append(box([x0, y - 2.0, ROLL_Z + 1.9], [x1, y + 2.0, 25.4], f"fr_pincheek{k}_s{int(y)}", f, "castiron"))
            out += eye("x", (0.0, y, ROLL_Z), x0 - 0.15, x1 + 0.15, 1.55, 1.9, f"fr_pinbrass{k}_{int(y)}_", f, "brass")
    out.append(box([PIN_CHEEKS[0][0], PIN_TIE_Y[0], 14.6], [PIN_CHEEKS[1][1], PIN_TIE_Y[1], 25.4], "fr_pintie", f, "castiron"))
    # the bottom shaft's outer bearing, west of the crown wheel
    x0, x1 = WEST_BRG_X
    out.append(box([x0, SOLE_Y, 17.6], [x1, BOT_Y - 2.2, 22.4], "fr_westpost", f, "castiron"))
    out.append(box([x0 - 0.5, 0.0, 16.8], [x1 + 0.5, SOLE_Y, 23.2], "fr_westfoot", f, "castiron"))
    out += eye("x", BOT_C, x0, x1, 1.55, 2.2, "fr_westbrg", f, "iron")
    # the entry shaft's bearings (along z), on posts
    for k, (z0, z1) in enumerate(ENTRY_BRG, 1):
        out += eye("z", ENTRY_C, z0, z1, 0.8 if k == 1 else 1.5, 2.0, f"fr_entrybrg{k}_", f, "iron")
        out.append(box([ENTRY_X - 1.8, 0.0, z0 + 0.2], [ENTRY_X + 1.8, BOT_Y - 2.0, z1 - 0.2], f"fr_entrypost{k}", f, "oak"))
    # the tumbling shaft's bearings (along x), on posts
    for k, (x0, x1) in enumerate(((3.6, 5.4), (26.6, 28.4), (61.2, 63.0)), 1):
        out += eye("x", TUMBLE_C, x0, x1, 0.6, 1.3, f"fr_tumblebrg{k}_", f, "iron")
        out.append(box([x0, 0.0, TUMBLE_Z - 1.3], [x1, TUMBLE_Y - 1.3, TUMBLE_Z + 1.3], f"fr_tumblepost{k}", f, "oak"))
    return out


# ---------------------------------------------------------------- the oil cups (one on each housing's cap, feeding its top brass)
def build_cups():
    """A sight-feed cup on the front of each housing's cap, a pipe from its base down the cap into the top chock's
    brass. Their oil is one rig part (both levels are at one height). The glass is in the Transparent pass."""
    out = []
    (y0, y1), (z0, z1) = CUP["y"], CUP["z"]
    g = 0.15
    for tag, (x0, x1) in CUP["x"].items():
        xm = (x0 + x1) / 2
        p = f"fr_cup{tag}"
        els = [box([x0, y0 - 0.3, z0], [x1, y0, z1], f"{p}_base", "frame", "brass"),
               box([x0, y1, z0], [x1, y1 + 0.3, z1], f"{p}_cap", "frame", "brass"),
               box([x0, y0, z0], [x1, y1, z0 + g], f"{p}_glass_n", "frame", "glass"),
               box([x0, y0, z1 - g], [x1, y1, z1], f"{p}_glass_s", "frame", "glass"),
               box([x0, y0, z0 + g], [x0 + g, y1, z1 - g], f"{p}_glass_w", "frame", "glass"),
               box([x1 - g, y0, z0 + g], [x1, y1, z1 - g], f"{p}_glass_e", "frame", "glass"),
               box([xm - 0.3, y1 + 0.3, (z0 + z1) / 2 - 0.15], [xm + 0.3, y1 + 0.55, (z0 + z1) / 2 + 0.15], f"{p}_knob", "frame", "brass"),
               box([xm - 0.25, CAP_Y[1], (z0 + z1) / 2 - 0.25], [xm + 0.25, y0 - 0.3, (z0 + z1) / 2 + 0.25], f"{p}_pipe", "frame", "brass")]
        for el in els:
            if "_glass_" in el.name:
                el.render_pass = TRANSPARENT
        out += els
        out.append(box([x0 + g + 0.02, OIL_Y, z0 + g + 0.02], [x1 - g - 0.02, OIL_Y + OIL_EMPTY, z1 - g - 0.02], f"oillevel_{tag}", "oillevel", "oil"))
    return out


# ---------------------------------------------------------------- the cycle: one pass a unit of W
# Every pass runs the same cycle (u, the fraction of its unit): the bar on the fore tray swings down and slides
# into its groove; the rolls take it through onto the catcher's table; the fingers move it one lane east; the
# table tilts it up against the top roll's back, which pulls it up and carries it over its crown onto the fore
# tray, standing up empty, where it slides down to the tray's stop; the table comes back down and the fingers
# back. The third pass's angle is pushed off the table's east end instead.
V_ROLL = TAU * R_ROLL * TURNS_PER_PASS * ROLL_RATIO     # voxels the rolls' surface runs in a pass (~104)
OVERHANG = 0.6                               # how far the bar's end goes past the top roll's back before it is carried over
DOWN = (0.00, 0.06)                          # the tray swings down under the bar's weight
FEED = (0.06, 0.12)                          # the bar slides down the tray into the groove's guide and the bite
ROLL0 = 0.12                                 # the rolls bite
TRAY_EASE = 0.06                             # the tray swings back up as fast, once the bar has gone into the rolls
SHIFT = (0.36, 0.98, 0.24)                   # the transfer: out over 0.36..0.60, back over 0.74..0.98 (from, to, ease)
TILT = (0.60, 0.99, 0.12)                    # the table: up over 0.60..0.72, down over 0.87..0.99
SLIDE = 0.05                                 # the bar slides down the fore tray to its stop
HANDOUT = len(PASSES) - 1 + SHIFT[0] + SHIFT[2]   # the finished angle, pushed to the table's east end, is handed out (it leaves the model)
HANDOUT_EASE = 0.005


def roll_window(k):
    """Pass k's rolling (0-based): W over which its bar goes through, the bar out of it at the rolls' speed."""
    return k + ROLL0, k + ROLL0 + BAR_LENGTHS[k] / V_ROLL


def runout_window(k):
    a = roll_window(k)[1]
    return a, a + RUNOUT / V_ROLL


def r_top_at(x):
    for x0, x1, r in barrel_profile(True):
        if x0 - 1e-9 <= x <= x1 + 1e-9:
            return r
    return None


def ride_radius(j, lane):
    """How far from the top roll's axis section j's lowest line rides when it lies over lane `lane` on the top
    roll: the most, across the bar, of the roll's radius there less the bar's bottom's height over its lowest."""
    q = section(j)
    best = 0.0
    for i in range(161):
        dx = -q["w"] + 2 * q["w"] * i / 160
        best = max(best, r_top_at(LANES[0] + LANE_PITCH * lane + dx) - (bar_bottom(j, dx) - PASS_LOW))
    return best


_GEO = {}


def geo():
    """The bar's way back over the top roll, solved once: the ride radius, the table's tilt and the tangent
    points, the fore tray's swing and pivot, and each returned bar's slides."""
    if _GEO:
        return _GEO
    g = _GEO
    g["R"] = R = max(ride_radius(0, 1), ride_radius(1, 2))
    tc = (TOP_Y, ROLL_Z)
    piv = TABLE_PIVOT
    p0 = (PASS_LOW, piv[1])

    def dist(a):
        q = yz_rot(p0, piv, a)
        return math.cos(a) * (tc[0] - q[0]) + math.sin(a) * (tc[1] - q[1])
    lo, hi = 0.0, math.radians(60)
    for _ in range(80):                       # the table's tilt: its top tangent to the ride circle, the roll under it
        mid = (lo + hi) / 2
        lo, hi = (mid, hi) if dist(mid) > -R else (lo, mid)
    g["alpha"] = a = (lo + hi) / 2
    g["phi_f"] = pf = math.radians(TRAY_PHI)
    g["Tb"] = (tc[0] + R * math.cos(a), tc[1] + R * math.sin(a))
    g["Tf"] = tf = (tc[0] + R * math.cos(pf), tc[1] + R * math.sin(pf))
    g["ub"] = (math.sin(a), -math.cos(a))                   # up the tilted table, towards the roll
    g["uf"] = uf = (math.sin(pf), -math.cos(pf))            # down the fore tray, away from the roll
    g["dphi"] = pf - a                                      # the bar carried over the crown: turned about the roll's axis
    gam = math.radians(TRAY_SLOPE)
    v = (math.sin(gam), -math.cos(gam))                     # the swung-down tray, away from the rolls
    g["gamma"], g["v"] = gam, v
    g["K"] = k_pt = (PASS_LOW, ROLL_Z)                      # the bite: the bar's entering end on the line of centres
    up_near = (tf[0] + TRAY_NEAR * uf[0], tf[1] + TRAY_NEAR * uf[1])
    dn_near = (PASS_LOW + (ROLL_Z - TRAY_DOWN_Z) * math.tan(gam), TRAY_DOWN_Z)
    d = math.atan2(v[1], v[0]) - math.atan2(uf[1], uf[0])
    d = math.atan2(math.sin(d), math.cos(d))
    g["delta"] = d
    c, s = math.cos(d), math.sin(d)
    ra = (up_near[0] * c - up_near[1] * s, up_near[0] * s + up_near[1] * c)
    b = (dn_near[0] - ra[0], dn_near[1] - ra[1])
    det = (1 - c) * (1 - c) + s * s
    g["C"] = ((b[0] * (1 - c) - s * b[1]) / det, ((1 - c) * b[1] + s * b[0]) / det)
    g["up_near"], g["dn_near"] = up_near, dn_near
    g["bars"] = []
    for j in range(len(PASSES)):
        L = BAR_LENGTHS[j]
        q = yz_rot((PASS_LOW, ROLL_Z + RUNOUT), piv, a)       # its tail, tilted
        pull = (g["Tb"][0] - q[0]) * g["ub"][0] + (g["Tb"][1] - q[1]) * g["ub"][1] + OVERHANG
        over = R * abs(g["dphi"])
        slide = TRAY_LEN - OVERHANG - over
        far = (tf[0] + (TRAY_LEN - L) * uf[0], tf[1] + (TRAY_LEN - L) * uf[1])   # its far end on the tray, before it swings
        fd = yz_rot(far, g["C"], d)
        feed = (k_pt[0] - fd[0]) * -v[0] + (k_pt[1] - fd[1]) * -v[1]
        g["bars"].append({"pull": pull, "over": over, "slide": slide, "feed": feed, "far_down": fd})
    far = (tf[0] + (TRAY_LEN - BILLET["len"]) * uf[0], tf[1] + (TRAY_LEN - BILLET["len"]) * uf[1])
    fd = yz_rot(far, g["C"], d)
    g["billet_feed"] = (k_pt[0] - fd[0]) * -v[0] + (k_pt[1] - fd[1]) * -v[1]
    return g


def cycle_windows(k):
    """Pass k's W windows, by name."""
    g = geo()
    t0 = k + TILT[0] + TILT[2]
    pull = g["bars"][k]["pull"] / V_ROLL
    over = g["bars"][k]["over"] / V_ROLL
    return {"down": (k + DOWN[0], k + DOWN[1]), "feed": (k + FEED[0], k + FEED[1]), "roll": roll_window(k),
            "runout": runout_window(k), "pull": (t0, t0 + pull), "over": (t0 + pull, t0 + pull + over),
            "slide": (t0 + pull + over, t0 + pull + over + SLIDE)}


def engage(windows, w):
    """A gauge's engagement at W for `windows` [(from, to, ease)], as rigmath reads an occupy gauge with p = 1:
    the most engaged window."""
    best = 0.0
    for a, b, e in windows:
        best = max(best, min(1.0, max(0.0, (w - a) / e)) * min(1.0, max(0.0, (b - w) / e)))
    return best


def tilt_windows():
    return [(k + TILT[0], k + TILT[1], TILT[2]) for k in range(len(PASSES))]


def shift_windows():
    return [(k + SHIFT[0], k + SHIFT[1], SHIFT[2]) for k in range(len(PASSES))]


def tray_windows():
    """The fore tray swings down with each bar as it is laid on it, and back up once the rolls have taken it."""
    return [(k + DOWN[0], roll_window(k)[1] + TRAY_EASE, TRAY_EASE) for k in range(len(PASSES))]


def rod_lift(beta):
    """How far the table's underside over the lift rods (at TUMBLE_Z) rises when the table has tilted beta."""
    yu = TABLE_UNDER - TABLE_PIVOT[0]
    py, pz = TABLE_PIVOT[0] + yu * math.cos(beta), TABLE_PIVOT[1] + yu * math.sin(beta)
    return py + (pz - TUMBLE_Z) * math.tan(beta) - TABLE_UNDER


ROD_LIFT = None


def rod_max():
    return rod_lift(geo()["alpha"])


# ---------------------------------------------------------------- builders: the catcher's table and its gear
def build_table():
    """The catcher's table: an iron top in three strips (the fingers' slots between them) on two oak side beams
    and three cross beams, hinged at its far end on a pin through the side beams; a wear plate under the middle
    beam where the lift rods bear."""
    t = "table"
    y0 = PASS_LOW - TABLE_T
    out = []
    zs = [TABLE_Z[0], TABLE_SLOTS[0][0], TABLE_SLOTS[0][1], TABLE_SLOTS[1][0], TABLE_SLOTS[1][1], TABLE_Z[1]]
    for i in range(3):
        out += long_box([TABLE_X[0], y0, zs[2 * i]], [TABLE_X[1], PASS_LOW, zs[2 * i + 1]], f"table_top{i + 1}_", t, "iron", axis=0)
    for tag, (x0, x1) in (("w", (TABLE_X[0], TABLE_X[0] + 1.2)), ("e", (TABLE_X[1] - 1.2, TABLE_X[1]))):
        out += long_box([x0, TABLE_UNDER, TABLE_Z[0]], [x1, y0, TABLE_Z[1]], f"table_side{tag}", t, "oak")
    for i, (z0, z1) in enumerate(TABLE_BEAMS, 1):
        out += long_box([TABLE_X[0] + 1.2, TABLE_UNDER, z0], [TABLE_X[1] - 1.2, y0, z1], f"table_beam{i}_", t, "oak", axis=0)
    for i, cx in enumerate(LIFT_CAM_X, 1):
        out.append(box([cx - 0.9, TABLE_UNDER - 0.3, TABLE_BEAMS[1][0] + 0.1], [cx + 0.9, TABLE_UNDER, TABLE_BEAMS[1][1] - 0.1], f"table_wear{i}", t, "iron"))
    py, pz = TABLE_PIVOT
    out += disc("x", (0.0, py, pz), TABLE_X[0] - 1.2, TABLE_X[1] + 1.2, 0.5, "table_pin", t, "iron", k=4)
    return out


def build_tumble():
    """The tumbling shaft: its worm wheel under the entry shaft's worm, the two lift cams (a base circle and the
    lobe that lifts the table, drawn from the lift rods' rise), and the transfer's barrel cam, a drum whose rib
    the transfer's fork straddles (drawn from the transfer's stroke)."""
    p = "tumble"
    c = TUMBLE_C
    out = disc("x", c, TUMBLE_X[0], TUMBLE_X[1], 0.6, "tumble_shaft", p, "iron", k=4)
    out += gear("x", c, ENTRY_X - 0.7, ENTRY_X + 0.7, R_WWHEEL, WORM_TEETH, WORM_MOD, "tumble_wheel", p, "iron", body_k=4)
    lift = rod_max()
    for i, cx in enumerate(LIFT_CAM_X, 1):
        out += disc("x", c, cx - CAM_W / 2, cx + CAM_W / 2, CAM_BASE, f"tumble_cam{i}_base", p, "iron")
        for b, (ang, r) in enumerate(cam_lobe(lift), 1):
            inset = 0.02 + 0.016 * (b % 2)            # neighbouring spokes overlap: their faces in two planes, so none share one
            out.append(radial("x", c, cx - CAM_W / 2 + inset, cx + CAM_W / 2 - inset, CAM_BASE - 0.1, r, 2 * r * math.tan(math.pi / CAM_BINS), ang,
                              f"tumble_cam{i}_lobe{b:02d}", p, "iron"))
    out += disc("x", c, *DRUM_X, DRUM_R, "tumble_drum", p, "castiron")
    for b, el in enumerate(drum_rib(), 1):
        el.name, el.part = f"tumble_rib{b:02d}", p
        out.append(el)
    return out


CAM_BINS = 120


def cam_lobe(lift):
    """The lift cams' lobe: (angle about x, radius) of a spoke in each 3-degree bin where the rods stand above the
    base circle; the cam turns once a pass (2 pi W), the rod's roller over its top, so the bin at the cam's angle
    -2 pi W carries the rise at W: the least rise in the bin, so the roller never sinks into the next spoke."""
    best = {}
    for i in range(3600):
        u = i / 3600
        e = engage([(TILT[0], TILT[1], TILT[2])], u)
        if e <= 0:
            continue
        b = int(round(((-TAU * u) % TAU) / (TAU / CAM_BINS))) % CAM_BINS
        best[b] = min(best.get(b, 1.0), e)
    return [(TAU * b / CAM_BINS, CAM_BASE + lift * e) for b, e in sorted(best.items()) if lift * e > 0.02]


DRUM_SEGS = 60


def rib_x(u):
    """The barrel cam's rib's centre (x) at the drum's top at W = u (mod 1): the fork's place."""
    return FOLLOW_X0 + STROKE * engage([SHIFT], u % 1.0)


def drum_rib():
    """The rib round the drum: DRUM_SEGS boxes, each from one sample of the fork's path to the next, lying on the
    drum at the local angle that comes to the top at that W."""
    out = []
    r0, r1 = DRUM_R - 0.1, DRUM_R + RIB["h"]
    rm = (r0 + r1) / 2
    for i in range(DRUM_SEGS):
        ua, ub = i / DRUM_SEGS, (i + 1) / DRUM_SEGS
        xa, xb = rib_x(ua), rib_x(ub)
        arc = rm * TAU / DRUM_SEGS
        lean = math.atan2(xb - xa, arc)
        t = RIB["t"] * math.cos(lean) - 0.032 * (i % 2)   # across the rib, so it is RIB.t wide along x at the top; every other a hair thinner, so the dwells' sides share no plane
        length = math.hypot(arc, xb - xa) + 0.05
        xm = (xa + xb) / 2
        el = box([xm - t / 2, TUMBLE_Y + r0, TUMBLE_Z - length / 2], [xm + t / 2, TUMBLE_Y + r1, TUMBLE_Z + length / 2],
                 "rib", "tumble", "iron")
        # the drum turns +2 pi W about x, so the place that is at the top at W = u is at angle -2 pi u on the drum: a rib
        # segment lies along the drum's surface (-z at the top) from u to u + 1/SEGS, leaning in x as the path does
        rotate([el], "y", -math.degrees(lean), (xm, TUMBLE_Y + rm, TUMBLE_Z))
        rotate([el], "x", -math.degrees(TAU * (ua + ub) / 2), TUMBLE_C)
        out.append(el)
    return out


def build_liftrods():
    """Two lift rods, each standing on its cam with a roller in a fork at its foot and bearing under the table's
    middle beam with a pad at its head; they slide in guides on posts."""
    p = "liftrods"
    out = []
    y_roll = TUMBLE_Y + CAM_BASE + ROLLER_R
    for i, cx in enumerate(LIFT_CAM_X, 1):
        out += disc("x", (0.0, y_roll, TUMBLE_Z), cx - 0.55, cx + 0.55, ROLLER_R, f"lrod{i}_roller", p, "iron", k=4)
        for side, (x0, x1) in (("w", (cx - 0.95, cx - 0.7)), ("e", (cx + 0.7, cx + 0.95))):
            out.append(box([x0, y_roll - 0.35, TUMBLE_Z - 0.4], [x1, y_roll + 0.9, TUMBLE_Z + 0.4], f"lrod{i}_fork{side}", p, "iron"))
        out += disc("x", (0.0, y_roll, TUMBLE_Z), cx - 0.7, cx + 0.7, 0.18, f"lrod{i}_axle", p, "iron", k=4)
        out.append(box([cx - 0.95, y_roll + 0.9, TUMBLE_Z - 0.4], [cx + 0.95, y_roll + 1.2, TUMBLE_Z + 0.4], f"lrod{i}_crown", p, "iron"))
        out += disc("y", (cx, 0.0, TUMBLE_Z), y_roll + 1.2, TABLE_UNDER - 0.6, ROD_R, f"lrod{i}_rod", p, "iron", k=4)
        out.append(box([cx - 0.7, TABLE_UNDER - 0.6, TUMBLE_Z - 0.5], [cx + 0.7, TABLE_UNDER - 0.3, TUMBLE_Z + 0.5], f"lrod{i}_pad", p, "iron"))
    return out


def finger_x(j):
    """Finger j's east face at rest: TRANSFER_GAP west of the west tip of the bar out of pass j."""
    return LANES[j] - section(j)["w"] - TRANSFER_GAP


def build_transfer():
    """The transfer: two slide bars along x under the table, running on rails, each with three fingers that stand up
    through the table's slots west of each lane; a cross bar joining them and, at its south end, the fork that
    straddles the barrel cam's rib on top of the drum."""
    p = "transfer"
    out = []
    x0, x1 = finger_x(0) - 0.8, finger_x(2) + 0.2
    for r, zf in enumerate(FINGER_Z, 1):
        out += long_box([x0, SLIDEBAR_Y[0], zf - 0.4], [x1, SLIDEBAR_Y[1], zf + 0.4], f"transfer_bar{r}_", p, "iron", axis=0)
        for j in range(len(PASSES)):
            fx = finger_x(j)
            out.append(box([fx - 2 * FINGER_HALF, SLIDEBAR_Y[1], zf - 0.3], [fx, FINGER_TOP, zf + 0.3], f"transfer_finger{r}{j + 1}", p, "iron"))
    xc = FOLLOW_X0
    out.append(box([xc - 0.5, SLIDEBAR_Y[0], FINGER_Z[0] + 0.4], [xc + 0.5, SLIDEBAR_Y[1], TUMBLE_Z + 0.4], "transfer_cross", p, "iron"))
    top = TUMBLE_Y + DRUM_R + RIB["h"]
    for side, sg in (("w", -1), ("e", 1)):
        a = xc + sg * (RIB["t"] / 2 + FORK_PLAY)
        b = xc + sg * (RIB["t"] / 2 + FORK_PLAY + 0.3)
        out.append(box([min(a, b), top - 0.55, TUMBLE_Z - FORK_HALF], [max(a, b), SLIDEBAR_Y[0], TUMBLE_Z + FORK_HALF], f"transfer_fork{side}", p, "iron"))
    return out


def tray_place(els):
    """Elements built with the tray's surface level along -z from the tangent point T_f (y the surface) turned to
    lie on the tangent at TRAY_PHI: the tray as it stands empty, up against the top roll's front."""
    g = geo()
    rotate(els, "x", math.degrees(g["phi_f"]), (0.0, g["Tf"][0], g["Tf"][1]))
    return els


def build_tray():
    """The fore tray, hung from its pivot by two arms at its ends and held up empty by a counterweight: an iron
    plate on two oak rails under its edges, its stop at the far end; drawn standing up empty against the top roll's
    front."""
    p = "tray"
    g = geo()
    ty, tz = g["Tf"]
    s0, s1 = tz - TRAY_LEN - STOP_T, tz - TRAY_NEAR
    els = long_box([TRAY_X[0], ty - TRAY_T, s0], [TRAY_X[1], ty, s1], "tray_plate", p, "iron", axis=0)
    for tag, (x0, x1) in (("w", (TRAY_X[0], TRAY_X[0] + 1.0)), ("e", (TRAY_X[1] - 1.0, TRAY_X[1]))):
        els.append(box([x0, ty - TRAY_T - 1.2, s0], [x1, ty - TRAY_T, s1 - 1.2], f"tray_rail{tag}", p, "oak"))
    els.append(box([TRAY_X[0], ty, s0], [TRAY_X[1], ty + 1.6, s0 + STOP_T], "tray_stop", p, "iron"))
    tray_place(els)
    # the arms: from the pivot to each rail's middle, outside the plate; the counterweight's arms beyond the pivot
    cy, cz = g["C"]
    mid = yz_rot((ty - TRAY_T - 0.6, (s0 + s1) / 2), (ty, tz), g["phi_f"])
    for tag, (x0, x1), sg in (("w", (TRAY_X[0] - 0.8, TRAY_X[0]), -1), ("e", (TRAY_X[1], TRAY_X[1] + 0.8), 1)):
        els.append(strut_yz((cy, cz), mid, x0, x1, 0.8, f"tray_arm{tag}", p, "iron"))
        cw = (cy + (cy - mid[0]) * CW_REACH, cz + (cz - mid[1]) * CW_REACH)
        els.append(box([x0, cy - 0.6, cz - 0.6], [x1, cy + 0.6, cz + 0.6], f"tray_hub{tag}", p, "iron"))
        # outboard of the bearing, on the same pin, the counterweight's hub, arm and weight
        ox = (x0 - 1.6, x0 - 1.0) if sg < 0 else (x1 + 1.0, x1 + 1.6)
        els.append(box([ox[0], cy - 0.6, cz - 0.6], [ox[1], cy + 0.6, cz + 0.6], f"tray_cwhub{tag}", p, "iron"))
        els.append(strut_yz((cy, cz), cw, ox[0], ox[1], 0.7, f"tray_cwarm{tag}", p, "iron"))
        wx = (ox[0] - 0.3, ox[1]) if sg < 0 else (ox[0], ox[1] + 0.3)
        els.append(box([wx[0], cw[0] - 1.1, cw[1] - 1.1], [wx[1], cw[0] + 1.1, cw[1] + 1.1], f"tray_weight{tag}", p, "castiron"))
    return els


CW_REACH = 0.42                              # the counterweight's arm, as a share of the tray arm, the other way


def strut_yz(a, b, x0, x1, width, name, part, tex):
    """A bar from (y, z) a to b, x0..x1 across."""
    ln = math.hypot(b[0] - a[0], b[1] - a[1])
    my, mz = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
    el = box([x0, my - width / 2, mz - ln / 2 - width / 2], [x1, my + width / 2, mz + ln / 2 + width / 2], name, part, tex)
    ang = math.atan2(b[0] - a[0], b[1] - a[1])          # from +z towards +y
    rotate([el], "x", -math.degrees(ang), (0.0, my, mz))
    return el


FORE_Z = (TRAY_DOWN_Z + 0.3, 15.0)           # the fore-plate, between the swung-down tray and the bottom roll
DELIVERY_Z = (25.6, TABLE_Z[0] - 0.1)        # the delivery guide, between the rolls and the table
RAIL_X = (31.0, 58.4)                        # the transfer's rails


def build_guides():
    """The fixed handling gear (frame): the fore-plate on the swung-down tray's line with its two lane guides, the
    delivery guide behind the rolls, the table's hinge bearings, the lift rods' guides, the transfer's rails, and
    the fore tray's pivot posts with the stops its arms come to, up and down."""
    f = "frame"
    g = geo()
    out = []
    gam = g["gamma"]
    k_y = g["K"][0]
    # the fore-plate: its top on the swung-down tray's line, through the bite
    fz0, fz1 = FORE_Z
    inner = (HOUSINGS[0][1][1], HOUSINGS[1][1][0])
    fp = [box([inner[0], k_y - 0.8, fz0], [inner[1], k_y, fz1], "fr_foreplate", f, "iron")]
    for x in (LANES[0] + LANE_PITCH / 2, LANES[1] + LANE_PITCH / 2):
        fp.append(box([x - 0.2, k_y, fz0 + 0.3], [x + 0.2, k_y + 1.6, fz1], f"fr_foreguide{int(x)}", f, "iron"))
    rotate(fp, "x", math.degrees(gam), (0.0, k_y, ROLL_Z))
    out += fp
    out.append(box([inner[0], PASS_LOW - TABLE_T, DELIVERY_Z[0]], [inner[1], PASS_LOW, DELIVERY_Z[1]], "fr_delivery", f, "iron"))
    # the table's hinge: a bearing and post outside each side beam
    py, pz = TABLE_PIVOT
    for tag, (x0, x1) in (("w", (TABLE_X[0] - 1.2, TABLE_X[0])), ("e", (TABLE_X[1], TABLE_X[1] + 1.2))):
        out += eye("x", (0.0, py, pz), x0, x1, 0.5, 1.2, f"fr_hinge{tag}_", f, "iron")
        out.append(box([x0, 0.0, pz - 1.2], [x1, py - 1.2, pz + 1.2], f"fr_hingepost{tag}", f, "oak"))
    # the lift rods' guides: a sleeve on an arm from a post beside each cam
    for i, cx in enumerate(LIFT_CAM_X, 1):
        out += eye("y", (cx, 0.0, TUMBLE_Z), *ROD_GUIDE_Y, ROD_R + 0.02, ROD_R + 0.35, f"fr_rodguide{i}_", f, "iron")
        out.append(box([cx + 1.0, 0.0, TUMBLE_Z + 1.2], [cx + 1.8, ROD_GUIDE_Y[1], TUMBLE_Z + 2.0], f"fr_rodpost{i}", f, "oak"))
        out.append(box([cx + ROD_R + 0.2, ROD_GUIDE_Y[0], TUMBLE_Z - 0.5], [cx + 1.8, ROD_GUIDE_Y[1], TUMBLE_Z + 2.0], f"fr_rodarm{i}", f, "iron"))
    # the transfer's rails, on posts at their ends
    for r, zf in enumerate(FINGER_Z, 1):
        out += long_box([RAIL_X[0], SLIDEBAR_Y[0] - 0.6, zf - 0.5], [RAIL_X[1], SLIDEBAR_Y[0], zf + 0.5], f"fr_rail{r}_", f, "iron", axis=0)
        for tag, (x0, x1) in (("w", (RAIL_X[0], RAIL_X[0] + 0.8)), ("e", (RAIL_X[1] - 0.8, RAIL_X[1]))):
            out.append(box([x0, 0.0, zf - 0.5], [x1, SLIDEBAR_Y[0] - 0.6, zf + 0.5], f"fr_railpost{r}{tag}", f, "oak"))
    # the fore tray's pivot: a post and bearing outside each arm, and the two stops the counterweight's arm meets
    cy, cz = g["C"]
    for tag, (x0, x1) in (("w", (TRAY_X[0] - 1.8, TRAY_X[0] - 0.8)), ("e", (TRAY_X[1] + 0.8, TRAY_X[1] + 1.8))):
        out += eye("x", (0.0, cy, cz), x0, x1, 0.45, 0.9, f"fr_traybrg{tag}_", f, "iron")
        out.append(box([x0, 0.0, cz - 0.9], [x1, cy - 0.9, cz + 0.9], f"fr_traypost{tag}", f, "oak"))
    return out


def build_trayshaft():
    """The fore tray's pivot: a stub pin at each end, through its hub into its bearing (the tray's; nothing crosses
    the bars' way)."""
    g = geo()
    c = (0.0, g["C"][0], g["C"][1])
    return (disc("x", c, TRAY_X[0] - 2.4, TRAY_X[0], 0.45, "tray_pinw", "tray", "iron", k=4)
            + disc("x", c, TRAY_X[1], TRAY_X[1] + 2.4, 0.45, "tray_pine", "tray", "iron", k=4))


# ---------------------------------------------------------------- the work
def leg_boxes(j, xc, z0, z1, name, part, tex="bar"):
    """Section j's two legs, xc its centre line, from z0 to z1: each a box from the outside apex to its tip, its
    thickness inwards."""
    q = section(j)
    out = []
    for tag, sx in (("e", 1), ("w", -1)):
        d = (sx * q["s"], -q["c"])                  # down the leg, outside
        n = (-sx * q["c"], -q["s"])                 # inwards
        cx = xc + d[0] * q["a"] / 2 + n[0] * q["t"] / 2
        cy = q["y_apex"] + d[1] * q["a"] / 2 + n[1] * q["t"] / 2
        el = box([cx - q["a"] / 2, cy - q["t"] / 2, z0], [cx + q["a"] / 2, cy + q["t"] / 2, z1], f"{name}{tag}", part, tex)
        rotate([el], "z", math.degrees(math.atan2(d[1], d[0])), (cx, cy, (z0 + z1) / 2))
        out.append(el)
    return out


def build_bars():
    """The work, each piece as it lies when it is made: the billet, flat at the bite of the first groove, and the
    bar out of each pass, in its groove's lane on the delivery side, from the line of centres."""
    out = []
    for cls, t, metal, tex in BAR_SETS:
        out.append(box([LANES[0] - BILLET["w"] / 2, PASS_LOW, ROLL_Z - BILLET["len"]], [LANES[0] + BILLET["w"] / 2, PASS_LOW + BILLET["t"], ROLL_Z],
                       f"{t}billet_body", f"billet{metal}", tex))
        for j in range(len(PASSES)):
            out += leg_boxes(j, LANES[j], ROLL_Z, ROLL_Z + BAR_LENGTHS[j], f"{t}bar{j + 1}_leg", f"bar{j + 1}{metal}", tex)
    return out


def is_work(pid):
    """The bar's parts (either metal's billet, or a bar out of a pass)."""
    return pid.startswith("billet") or (pid.startswith("bar") and pid[3:4].isdigit())


def class_of(pid):
    """1 or 2 for a part of the iron or the steel bar, else 0."""
    if not is_work(pid):
        return 0
    return 1 if pid.endswith("iron") else 2


def on_show(pid, k):
    c = class_of(pid)
    return c == 0 or c == k


def build():
    els = build_entry() + build_crown() + build_pinions()
    for top in (False, True):
        els += build_spindle(top) + build_roll(top)
    els += build_tumble() + build_liftrods() + build_transfer() + build_table() + build_tray() + build_trayshaft()
    els += build_bars() + build_cups()
    els += build_stand() + build_drive_frame() + build_guides()
    return els


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def per(v, cls=None):
    return {c: r6(v) for c in ((cls,) if cls else CLASSES)}


WORK = {"name": "passes", "unit": "passes", "step": 0.005, "end": {c: float(len(PASSES)) for c in CLASSES}}
PATH = WORK
FOREVER = 1000.0


def wins(ws):
    return [{"from": r6(a), "to": r6(b), "ease": r6(e)} for a, b, e in ws]


def rise(w0, w1):
    """Rises over W w0..w1 and stays."""
    return wins([(w0, FOREVER, w1 - w0)])


def undo(w0, w1):
    """Full until w0, gone by w1: a motion the part had made before it was drawn where it is authored."""
    return wins([(-FOREVER, w1, w1 - w0)])


def gauge(motion, axis, amount, windows=None, pivot=None, origin=None, mode=None, cls=None):
    d = {"type": "gauge", "motion": motion, "axis": axis}
    if pivot is not None:
        d["pivot"] = pivot
    if origin is not None:
        d["origin"] = origin
    d["amount"] = per(amount, cls)
    if mode:
        d["mode"] = mode
    else:
        d["windows"] = windows
    return d


def yz_pivot(p):
    return pt(0.0, p[0], p[1])


def spin(c, ratio, axis="x"):
    return {"type": "rotate", "axis": axis, "pivot": pt(*c), "ratio": r6(ratio)}


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    g = geo()
    bot, top = (0.0, BOT_Y, ROLL_Z), (0.0, TOP_Y, ROLL_Z)
    parts = [
        {"id": "entry", "match": ["entry_*"], "requires": None, "drivers": [spin(ENTRY_C, 1.0, "z")]},
        {"id": "crown", "match": ["crown_*"], "requires": "pinions", "drivers": [spin(bot, ROLL_RATIO)]},
        {"id": "pinionbottom", "match": ["bpin_*"], "requires": "pinions", "drivers": [spin(bot, ROLL_RATIO)]},
        {"id": "piniontop", "match": ["tpin_*"], "requires": "pinions", "drivers": [spin(top, -ROLL_RATIO)]},
        {"id": "spindlebottom", "match": ["bsp_*"], "requires": "spindles", "drivers": [spin(bot, ROLL_RATIO)]},
        {"id": "spindletop", "match": ["tsp_*"], "requires": "spindles", "drivers": [spin(top, -ROLL_RATIO)]},
        {"id": "rollbottom", "match": ["broll_*"], "requires": "rolls", "drivers": [spin(bot, ROLL_RATIO)]},
        {"id": "rolltop", "match": ["troll_*"], "requires": "rolls", "drivers": [spin(top, -ROLL_RATIO)]},
        {"id": "tumble", "match": ["tumble_*"], "requires": "catcher",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(*TUMBLE_C), "ratio": r6(TAU), "input": "work"}]},
        {"id": "liftrods", "match": ["lrod*"], "requires": "catcher", "drivers": [gauge("slide", "y", rod_max() / B, wins(tilt_windows()))]},
        {"id": "transfer", "match": ["transfer_*"], "requires": "catcher", "drivers": [gauge("slide", "x", STROKE / B, wins(shift_windows()))]},
        {"id": "table", "match": ["table_*"], "requires": "catcher",
         "drivers": [gauge("rotate", "x", g["alpha"], wins(tilt_windows()), pivot=yz_pivot(TABLE_PIVOT))]},
        {"id": "tray", "match": ["tray_*"], "requires": "tray",
         "drivers": [gauge("rotate", "x", g["delta"], wins(tray_windows()), pivot=yz_pivot(g["C"]))]},
    ]
    for cls, t, metal, _ in BAR_SETS:
        parts += bar_parts(cls, t, metal)
    xc = (CUP["x"]["w"][0] + CUP["x"]["w"][1]) / 2
    parts.append({"id": "oillevel", "match": ["oillevel_*"], "requires": None,
                  "drivers": [{"type": "stretch", "axis": "y", "anchor": pt(xc, OIL_Y, 0.0), "length": r6(OIL_EMPTY / B),
                               "travel": r6((OIL_FULL - OIL_EMPTY) / B), "input": "oil"}]})
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []})
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


def bar_parts(cls, t, metal):
    """One metal's bar: the billet and the bar out of each pass, every gauge's amount for that metal's class only, so
    the other metal's set stands still (and the viewer shows a set only with its class)."""
    g = geo()
    req = f"bar{metal}"
    w0 = cycle_windows(0)
    xb = LANES[0]
    out = [{"id": f"billet{metal}", "match": [f"{t}billet_*"], "requires": req, "drivers": [
        gauge("stretch", "z", -1.0, rise(*w0["roll"]), origin=pt(xb, PASS_LOW, ROLL_Z), cls=cls),
        gauge("slide", "z", -g["billet_feed"] / B, undo(*w0["feed"]), cls=cls),
        gauge("rotate", "x", g["gamma"], pivot=yz_pivot(g["K"]), mode="present", cls=cls),
        gauge("rotate", "x", -g["delta"], undo(*w0["down"]), pivot=yz_pivot(g["C"]), cls=cls)]}]
    for j in range(len(PASSES)):
        cw = cycle_windows(j)
        L = BAR_LENGTHS[j]
        xc = LANES[j]
        last = j == len(PASSES) - 1
        drv = [gauge("stretch", "z", -1.0, wins([(-FOREVER, cw["roll"][1], cw["roll"][1] - cw["roll"][0])]), origin=pt(xc, PASS_LOW, ROLL_Z), cls=cls)]
        if not last:
            nw = cycle_windows(j + 1)
            drv.append(gauge("stretch", "z", -1.0, rise(*nw["roll"]), origin=pt(xc, PASS_LOW, ROLL_Z + L), cls=cls))
        drv.append(gauge("slide", "z", RUNOUT / B, rise(*cw["runout"]), cls=cls))
        b = g["bars"][j]
        if not last:
            drv += [gauge("slide", "z", -b["pull"] / B, rise(*cw["pull"]), cls=cls),
                    gauge("slide", "z", -b["over"] / B, rise(*cw["over"]), cls=cls),
                    gauge("slide", "z", -b["slide"] / B, rise(*cw["slide"]), cls=cls),
                    gauge("slide", "z", b["feed"] / B, rise(*nw["feed"]), cls=cls)]
        s0 = j + SHIFT[0] + SHIFT[2] * TRANSFER_GAP / STROKE
        drv.append(gauge("slide", "x", LANE_PITCH / B, rise(s0, s0 + SHIFT[2] * LANE_PITCH / STROKE), cls=cls))
        if last:                                   # handed out: it leaves the model, drawn to nothing about its middle
            drv.insert(0, gauge("stretch", "z", -1.0, rise(HANDOUT, HANDOUT + HANDOUT_EASE), origin=pt(xc, PASS_LOW, ROLL_Z + L / 2), cls=cls))
        else:
            drv += [gauge("rotate", "x", g["alpha"], rise(j + TILT[0], j + TILT[0] + TILT[2]), pivot=yz_pivot(TABLE_PIVOT), cls=cls),
                    gauge("rotate", "x", g["dphi"], rise(*cw["over"]), pivot=yz_pivot((TOP_Y, ROLL_Z)), cls=cls),
                    gauge("rotate", "x", g["delta"], rise(*nw["down"]), pivot=yz_pivot(g["C"]), cls=cls)]
        out.append({"id": f"bar{j + 1}{metal}", "match": [f"{t}bar{j + 1}_*"], "requires": req, "drivers": drv})
    return out


# ---------------------------------------------------------------- poses
REST = (0.0, 0.0, 0.0, 0, 0.0, 0.0)          # (theta, travel, W, k, p, oil): no bar, the tank empty; the authored pose


def inputs_of(pose):
    th, ps, W, k, p = pose[:5]
    oil = pose[5] if len(pose) > 5 else 0.0
    return {"theta": th, "travel": ps, "work": W, "size": k, "presence": p, "oil": oil}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path or PATH)


def pose_at(W, k=1, p=1.0, oil=0.6, theta=None):
    """A pose of a job in progress at W: the axle at the drawn pace for it."""
    th = TAU * TURNS_PER_PASS * W if theta is None else theta
    return (th, abs(th), W, k, p, oil)


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("output", "bite", "drip")


def make_rig(parts):
    progress_of({"work": WORK})
    g = geo()
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the fore tray's, nearest the player "
                    "who placed it; the bar travels south through the passes, and the axle comes in on the south face. work is the "
                    "job in passes, W (0..3, one pass a unit, geared to the axle: mill.turnsPerPass); k is the bar (1 iron, 2 steel: the "
                    "same bar and passes, the metal the gameplay's to settle). "
                    "The top chocks' oil cups follow the rig's oil input (the MachineOil tank's fill, 0..1). See the rolling mill's "
                    "README for the schema.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "infeedSide": "north",
        "outputSide": "east",
        "output": {"pos": pt(CELLS_X * B - 0.01, PASS_LOW + 1.0, ROLL_Z + RUNOUT + BAR_LENGTHS[-1] / 2)},
        "bite": {"pos": pt(LANES[1], PASS_LOW + 1.5, ROLL_Z)},
        "drip": {"pos": pt(sum(CUP["x"]["w"]) / 2, CAP_Y[1] + 0.05, sum(CUP["z"]) / 2)},
        "work": dict(WORK),
        "mill": {"turnsPerPass": TURNS_PER_PASS, "rollRatio": r6(ROLL_RATIO), "passes": len(PASSES), "handOut": r6(HANDOUT),
                 "sections": [{"angle": p["angle"], "leg": p["leg"], "thick": p["t"]} for p in PASSES],
                 "lengths": list(BAR_LENGTHS), "billet": dict(BILLET), "laneX": [r6(x / B) for x in LANES],
                 "_comment": "turnsPerPass: axle turns a pass, drawn (the worm and the tumbling shaft turn once a pass; the "
                             "gameplay's pace should equal it). handOut: W at which the finished angle, pushed to the table's "
                             "east end, is handed out through the output face; the model draws it no more. rollRatio: the rolls turn this share of the axle. sections: each "
                             "groove's vee (included angle, a leg outside and its thickness, voxels), the last the finished angle; "
                             "lengths: the bar out of each pass (voxels); billet: the flat bar the job starts with. laneX: each "
                             "groove's centre (blocks, from the controller's west face before the origin shift is applied)."},
        "parts": parts,
    }


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
            for key in ("pivot", "anchor", "origin"):
                if key in drv:
                    drv[key] = [r6(drv[key][k] + db[k]) for k in range(3)]
    ship = copy.deepcopy(rig)
    ship["cells"] = [{**c, "pos": shift_cell(c["pos"], ORIGIN_CELL)} for c in rig["cells"]]
    ship["powerCell"] = shift_cell(rig["powerCell"], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["mill"]["laneX"] = [r6(x + db[0]) for x in rig["mill"]["laneX"]]
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts, sp):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig (every reader rebuilds
    them the same way), leaving out the work; a cell with nothing in it is hollow (the table and the bar pass through
    it). Lids over every column whose top cell is not hollow."""
    written = flatten(shape["elements"], textures={})
    rest = []
    for w in written:
        pid = part_of(ship_parts, w.name)
        if is_work(pid):
            continue
        rest.append(posed_el(w, _part_matrix(ship_parts, pid, inputs_of(REST), sp)))
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
    tops = {}
    for c in out:
        x, y, z = c["pos"]
        if (x, z) not in tops or y > tops[(x, z)]["pos"][1]:
            tops[(x, z)] = c
    return with_lids(out, columns=lambda x, z: not tops[(x, z)].get("hollow"))


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    sp = ship["work"]
    poses = [REST, pose_at(0.2), pose_at(0.8, oil=1.0), pose_at(1.5), pose_at(2.3, oil=0.2), (1.3, 7.0, 2.95, 1, 0.6, 0.3)]
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
    """theta in {0, 1.1, -2.3, 2.9} with psi, no bar, the oil at 0, 0.35 and 1; then W every 0.05 of the job (every
    window's edges among it), p at 1 and 0.4 now and then."""
    out = []
    for i, th in enumerate((0.0, 1.1, -2.3, 2.9)):
        out.append((th, round(abs(th) + 7.3 * i, 6), 0.0, 0, 0.0, (0.0, 0.35, 1.0)[i % 3]))
    ws = {round(0.05 * i, 6) for i in range(61)}
    for k in range(len(PASSES)):
        for a, b in cycle_windows(k).values():
            ws |= {round(a, 6), round((a + b) / 2, 6), round(b, 6)}
    for i, W in enumerate(sorted(w for w in ws if 0 <= w <= len(PASSES))):
        th = TAU * TURNS_PER_PASS * W
        k = 2 if i % 5 == 2 else 1                 # now and then the steel bar
        for p in ((1.0,) if i % 7 else (1.0, 0.4)):
            out.append((round(th, 6), round(abs(th), 6), W, k, p, (1.0, 0.35, 0.0)[i % 3]))
    return out


def reference_json(ship_parts, sp):
    poses = []
    for pose in reference_poses():
        th, ps, W, k, p, oil = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose), sp)) for q in ship_parts}
        poses.append({"theta": th, "travel": ps, "work": W, "size": k, "presence": p, "oil": oil, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped rollingmill-rig.json's parts and work: each part's matrix as 3 "
                        "rows of 4 (block units) at each pose (W in passes). The site's and the tools' tests check their rig maths "
                        "against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod; no other mod's model is used. Keep "
             "element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return (REST, pose_at(0.18), pose_at(0.5, k=2), pose_at(0.78), pose_at(1.1, k=2), pose_at(2.5))


def shown(posed_els, pose):
    """The posed elements as they can be seen: the other metal's bar moved far away (copies; the order kept), so
    the z-fighting fix and its check deal only with faces that show; a bar not yet made, or rolled away, is
    collapsed to nothing, which they skip."""
    k = pose[3]
    out = []
    for e in posed_els:
        if not on_show(e.part, k):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def fix_coplanar(els, parts):
    return fix_coplanar_posed(els, lambda es, pose: shown([posed_el(el, pm(parts, el.part, pose)) for el in es], pose), coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the rolling mill's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_rollingmill
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
    ok = validate_rollingmill.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "rollingmill.json", args.out / "rollingmill_frame.json", args.out / "rollingmill-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "rollingmill.json", SHAPE_DIR / "rollingmill_frame.json", RIG_DIR / "rollingmill-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts, ship["work"])
    ok = validate_rollingmill.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
