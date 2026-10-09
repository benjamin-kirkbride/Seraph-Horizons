#!/usr/bin/env python3
"""Generate the gear cutter's shapes, rig and reference poses.

The gear cutter is an 1880s-style generating gear cutter on a knee-and-column bench frame. A gear blank
on a horizontal arbor rolls under a cutter whose edge is one tooth of a rack, as a master gear on the
same arbor rolls under a fixed rack: the master sets the blank's angle at every point of the pass, so
the cutter generates the master's tooth form in the blank (Bilgram 1884, Fellows 1896). The master is a
vanilla temporal gear (12 teeth, for the stainless gear, the size of the game's gear items) or a large
temporal gear (20 teeth, for the large gear, the size of the game's large temporal gear): one master
station, either master. Between passes the knee drops, taking the master out of the rack and the blank
off the cutter, and a Jonas planetary head (a looted sub-assembly) indexes the arbor one tooth. A
sight-feed drip oiler over the cutter shows the machine's oil (its level is the rig's `oil` input).
The machine is two blocks by two by two: the feed's rectifier runs inside the column and its clutch,
worm and wheel in a gearbox on the bed, both closed by removable covers (the rig's `cover` part).
Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    gearcutter.json         the whole machine, every moving part   (assets/.../shapes/block/)
    gearcutter_frame.json   the static frame only (block and item)  (assets/.../shapes/block/)
    gearcutter-rig.json     cells, anchors and the part rig         (assets/.../config/)
    rig-reference.json      every part's matrix at a grid of poses  (tests/GearCutter/)
    gearcutter/<item>.json  the five forged parts' item shapes      (assets/.../shapes/item/)

or, with `--out DIR`, all of them into DIR (the item shapes into DIR/item/). It validates its own output (validate_gearcutter.py) and
exits non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from
the machine box's north-west-bottom corner (the "build frame"); `shipped` moves it so the controller
cell is [0,0,0]. The player stands east of the machine (the operator's side); the column is at the
back (west).

The rig's inputs, as this machine uses them (README "Rig"):

    theta  the axle angle: the cone pulleys, the head shaft, the cutter spindle, the feed rectifier's
           gears
    psi    the axle's travel: the rectified feed shaft and the clutch sleeve
    W      the rig's work, teeth cut: the worm and camshaft turn once per tooth, and every gauge's
           windows are one per tooth. The rig's `work` declares it (unit teeth, end 12 with the
           temporal gear master and 20 with the large one); W = 3.4 is 0.4 through the fourth tooth
    k      the master fitted: 0 none, 1 the temporal gear (small), 2 the large temporal gear
    p      the master's presence, 0..1, eased as it is fitted or taken off
    oil    the oil tank's fill, 0..1: the sight-feed cup's level
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
from machinegen.rigmath import part_of, posed, progress_of, validate_driver, work_end  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "GearCutter" / "rig-reference.json"
ITEM_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "item" / "gearcutter"
SCRIPT = "mods-src/seraphhorizons/GearCutter/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
DEG = math.pi / 180

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 2, 2, 2          # machine box (blocks): x 0..1 (back, front), y 0..1, z 0..1
ORIGIN_CELL = (1, 0, 0)                      # the controller: the operator's (east) side, north, at ground level
POWER_CELL, POWER_FACE = (0, 0, 0), "west"   # the vanilla axle comes in along x at (y 8, z 8)

TEXTURES = {
    "iron": "game:block/metal/plate/iron",
    "steel": "game:block/metal/sheet-plain/steel1",
    "oak": "game:block/wood/debarked/oak",
    "leather": "game:block/leather/plain",
    "brass": "game:block/metal/sheet/brass1",
    "cupronickel": "game:block/metal/sheet/cupronickel1",
    "gold": "game:block/metal/sheet/gold1",
    "temporal": "game:item/resource/temporalgear",
    "glass": "game:block/glass/plain",
    "oil": "game:block/liquid/honey",
    "marble": "game:block/stone/rock/whitemarble2",
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the gears being cut, the masters
# Sized from the game's own items: the gear and the temporal gear are 6.7 voxels across and read as 12
# tips; the large temporal gear is 10.2 across. One module serves both: 12 teeth at pitch radius 3 and
# 20 at 5 (the large temporal gear's 11 chunky teeth cannot share a module with the small gear's 12).
MODULE = 0.5
ADD, DED = 1.0 * MODULE, 1.25 * MODULE
TEETH = {"thin": 12, "thick": 20}            # k = 1: the temporal gear master, the stainless gear; k = 2: the large ones
PITCH_R = {c: n * MODULE / 2 for c, n in TEETH.items()}         # 3 and 5
STEP = {c: TAU / n for c, n in TEETH.items()}                    # the arbor's index per tooth: 30 and 18 degrees
CLASSES = ("thin", "thick")
# Thicknesses measured element by element from the game's shapes (not their bounding boxes, which take in
# rotated pins and mover elements): the gear and the temporal gear are 1.4 thick (the temporal gear's twelve
# 0.2 nubs stand out to 2.0 overall), the large temporal gear's rim and teeth 2.0. A blank is as thick as the
# gear it becomes.
FACE_W = {("blank", "thin"): 1.4, ("blank", "thick"): 2.0, ("master", "thin"): 1.4, ("master", "thick"): 2.0}
NUB_SPAN, NUB_R = 2.0, 1.9                   # the temporal gear's nubs: 0.2 square, 2.0 long, at this radius
X_MASTER, X_BLANK = 14.4, 19.9               # the master's and the blank's mid-planes on the arbor (x)


def face(kind, cls):
    """The x span of a master or blank of the class, centred on its station."""
    c = X_MASTER if kind == "master" else X_BLANK
    w = FACE_W[(kind, cls)] / 2
    return (c - w, c + w)


STATION = {"master": (X_MASTER - 1.0, X_MASTER + 1.0), "blank": (X_BLANK - 1.0, X_BLANK + 1.0)}   # the large ones' faces

# ---------------------------------------------------------------- heights
SILL_TOP, BED_TOP = 1.0, 2.5
CAM_Y = 4.9                                  # the camshaft (along z), in the bed
DRUM_R, RAIL_R = 1.5, 2.1
SLIDER_Y = (CAM_Y + RAIL_R + 0.15, CAM_Y + RAIL_R + 0.85)
LIFT = 1.5                                   # the knee rises this much to cut; down, the master is out of the rack
CLASS_DROP = PITCH_R["thick"] - PITCH_R["thin"]   # the knee sits this much lower for the large master (2)
KNEE_H, TABLE_T, ARB_UP = 2.5, 1.2, 6.3      # knee bottom to ways; table; table top to the arbor's axis (the pawl's pin passes under it)
KNEE_LOWEST = SLIDER_Y[1] + 0.45             # the knee's bottom with the large master, knee down: over the sliders' guides
ARBOR_Y = KNEE_LOWEST + KNEE_H + TABLE_T + ARB_UP + CLASS_DROP   # the arbor's axis as authored (small master, knee down)
TABLE_TOP = ARBOR_Y - ARB_UP
WAY_TOP = TABLE_TOP - TABLE_T
KNEE_BOTTOM = WAY_TOP - KNEE_H
KNEE_BACK_TOP = KNEE_BOTTOM + 1.0            # the knee's low back part, under the blank and master
H_PITCH = ARBOR_Y + LIFT + PITCH_R["thin"]   # the rack's and the cutter's pitch line (y): fixed, on the overarm
CUTTER_R = 3.2
SPINDLE_Y = H_PITCH - DED + CUTTER_R         # the cutter spindle (along z) and the head shaft (along x)
OVERARM_Y = (29.5, 32.0)

# ---------------------------------------------------------------- along z: the pass
Z_REST = 12.0                                # the arbor's z with the table at rest
STROKE = 7.5                                 # the table's travel: the master rolls this far along the rack
Z_CUT = Z_REST + STROKE / 2                  # the cutter (and a rack tooth) over the arbor at mid-pass
PITCH = math.pi * MODULE
RACK_K = (-5, 5)                             # rack teeth at Z_CUT + k * PITCH
HEAD_Z = 28.0                                # the head shaft (along x, at SPINDLE_Y), south of the rack
MITRE_R = 1.4

# ---------------------------------------------------------------- the cycle, per tooth (t = frac(T))
T_KNEE_UP = (0.00, 0.10)
T_FWD = (0.10, 0.38)
T_BACK = (0.40, 0.68)
T_KNEE_DOWN = (0.68, 0.78)
T_GAP = (0.78, 0.80, 0.98, 1.00)             # the index pusher closes its gap to the lever's roller, and opens it again
T_LEVER = (0.80, 0.89, 0.98)                 # the lever is pushed (0.80..0.89) and springs back (..0.98)
LOST = 5.0 * DEG                             # the pawl's free travel before it meets a tooth
RIM_TEETH = 40                               # the Jonas housing's rim ratchet: 9 degrees a tooth
PUSH = {"thin": 45.0 * DEG, "thick": 27.0 * DEG}   # the housing's step: 5 rim teeth (small) or 3 (large, past the shield)
LEVER_SWING = LOST + PUSH["thin"]
T_DETENT = (0.09, 0.10, 0.68, 0.69)          # the sun's detent rides out before the roll starts, and drops in after it ends


def t_at_lever(angle):
    """When the lever has turned `angle` of its swing."""
    return T_LEVER[0] + (T_LEVER[1] - T_LEVER[0]) * angle / LEVER_SWING


T_STEP = {c: (t_at_lever(LEVER_SWING - PUSH[c]), T_LEVER[1]) for c in CLASSES}   # when the housing turns, per master

# ---------------------------------------------------------------- the Jonas head (planetary; ring in, carrier out)
J_MOD = 0.3
J_TEETH = {"sun": 10, "planet": 5, "ring": 20}
J_R = {k: v * J_MOD / 2 for k, v in J_TEETH.items()}           # 1.5, 0.75, 3.0
J_CARRIER = J_R["sun"] + J_R["planet"]                           # 2.25
# With the sun held, the carrier (the arbor) turns Zr / (Zr + Zs) = 2/3 of the ring: the ring steps 45 or
# 27 degrees for an arbor step of 30 or 18. With the ring held, the sun turns 3 times the carrier.
J_RING_PER_ARBOR = (J_TEETH["ring"] + J_TEETH["sun"]) / J_TEETH["ring"]     # 1.5
J_SUN_PER_ARBOR = 1 + J_TEETH["ring"] / J_TEETH["sun"]                     # 3
J_X = (25.2, 28.4)                           # back plate .. front plate
J_BAND = (27.5, 28.0)                        # the rim ratchet's band
J_OUT_R = 4.5
RIM_R = (4.3, 5.0)                           # the rim ratchet's root and tip
SHIELD_X = (28.45, 28.65)
DETENT_X = (28.75, 29.15)
LEVER_X = (29.4, 29.9)
LEVER_L = 5.6                                # arbor axis to the roller
LEVER_REST = 25.0 * DEG                      # the arm points down and 25 degrees north at rest
ROLLER_R = 0.45
SUN_SHAFT_X = (25.6, 30.6)                   # piloted into the arbor's end

# ---------------------------------------------------------------- the drive
ENTRY = (8.0, 8.0)                           # (y, z): the entry shaft, along x from the west face (the power cell's centre)
CONE_X = ((0.5, 1.9), (1.9, 3.3), (3.3, 4.7))
CONE_LOW = (3.8, 3.2, 2.6)                   # on the entry shaft; the belt runs on the first step
CONE_HIGH = (2.6, 3.2, 3.8)                  # on the head shaft
RECT_MOD = 0.3
RECT_X = ((6.4, 7.4), (7.8, 8.8))            # A1/B1 and A2/idler/B2, inside the column
RECT_TEETH = (15, 12)                        # A1 = B1 (direct, the shafts turn opposite ways); A2 = idler = B2
RECT_R = tuple(n * RECT_MOD / 2 for n in RECT_TEETH)            # 3.0, 2.4
WORM_TEETH = 12                              # the worm wheel on the camshaft: TURNS_PER_TOOTH axle turns per tooth
WORM_MOD = 0.5
WHEEL_R = WORM_TEETH * WORM_MOD / 2          # 3
WORM_R = 1.0
WORM_LEAD = math.pi * WORM_MOD
_FY = CAM_Y + WHEEL_R + WORM_R               # the feed shaft carries the worm over the wheel
FEED = (_FY, ENTRY[1] - math.sqrt((2 * RECT_R[0]) ** 2 - (_FY - ENTRY[0]) ** 2))   # (y, z) of the feed shaft, north of the entry
CAM_X = 25.9                                 # the camshaft's x: between the knee's girders, under the worm
WORM_X = (CAM_X - 1.5 * WORM_LEAD, CAM_X + 0.5 * WORM_LEAD)   # two turns, the thread at the bottom over the wheel's top gap
TURNS_PER_TOOTH = WORM_TEETH                 # the rectifier is 1:1
WHEEL_Z = FEED[1]
DRUM_INDEX_Z = (Z_REST - 5.6, Z_REST + 2.4)   # round the index slider's pin and its travel
DRUM_FEED_Z = (Z_REST + 3.4, Z_REST + 13.2)   # round the feed slider's pin and the stroke
LIFT_Z = (DRUM_FEED_Z[1] + 1.3, DRUM_FEED_Z[1] + 2.1)   # south of the feed drum, under the knee's screw web
CAM_BEARINGS_Z = ((WHEEL_Z - 1.5, WHEEL_Z - 0.85), (WHEEL_Z + 1.0, WHEEL_Z + 1.65), (Z_REST + 2.55, Z_REST + 3.25),
                  (DRUM_FEED_Z[1] + 0.2, DRUM_FEED_Z[1] + 0.9), (LIFT_Z[1] + 0.35, LIFT_Z[1] + 1.05))
CAM_Z = (WHEEL_Z - 1.7, LIFT_Z[1] + 1.1)
CAM_R0 = 2.0                                 # the lift cam's base circle
FOOT_R = 0.5                                 # the elevating screw's roller on the lift cam
SCREW_C = (CAM_X, 0.0, (LIFT_Z[0] + LIFT_Z[1]) / 2)
SEGS = 72
GROOVE_HALF = 0.42                           # the pin is 0.5 across: 0.17 of slack each side

# ---------------------------------------------------------------- the frame
COLUMN = ((5.0, 11.0), (0.2, 31.0))          # x, z: the whole length; the rectifier is inside it
OUTLINE_X, OUTLINE_Z = (2.0, 31.0), (COLUMN[1][0], COLUMN[1][1])   # the base's plan: the sills' outer faces

KNEE_X = (11.2, 29.4)
KNEE_Z = (6.0, 31.0)                         # south of the gearbox casing
CASE = {"x": (11.0, 30.7), "y": (BED_TOP, 11.8), "z": (1.0, 5.75), "t": 0.5}   # the feed gearbox: clutch, worm and wheel
WAYS_X = ((22.4, 24.0), (27.8, 29.4))
TABLE_X = (22.4, 29.4)
TABLE_Z = (Z_REST - 6.2, Z_REST + 6.6)

FEED_GUIDE_Z = (Z_REST + 5.2, Z_REST + 5.9)  # the gap between the table's guide bars (rest)
FEED_PIN_Z0 = Z_REST + 4.6                   # the feed slider's pin with the table at rest
CROSSHEAD_X = (11.2, 23.6)
CROSSHEAD_Z0, OVERARM_Z = 6.0, 22.0         # the cross-head over the work, the overarm south of it to the column's end
OILER = {"x": (24.0, 25.6), "y": (29.7, 31.1), "z": (Z_CUT - 0.75, Z_CUT + 0.75)}   # the glass; base under it, cap, needle and knob over it
OIL_EMPTY = 0.05                             # the cup's liquid element is authored this tall (empty)
# ---------------------------------------------------------------- box helpers
# The least gap between two faces that face the same way and overlap where both can be seen. The depth
# buffer cannot part faces a hundredth of a voxel apart at a few blocks' distance, so the work (blanks,
# fills, masters) and the cutter stack their faces at least this far apart (validate's close-faces check).
ZF_GAP = 0.025


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


def obox(c, size, r, name, part, tex):
    return skin(El(name, list(size), list(c), [row[:] for row in r], {}, part), tex)


AX = {"x": 0, "y": 1, "z": 2}
# For a disc about `axis`: the reference direction of angle 0, and the other in-plane direction
# (rotation about +axis turns REF towards OTHER).
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}


def frame_of(axis):
    a = AX[axis]
    u, w = REF[axis]
    sign_w = -1.0 if axis == "z" else 1.0     # rot about +z turns +y towards -x
    return a, u, w, sign_w


def radial(axis, c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box from radius r0 to r1 about `axis` through point c, a0..a1 along the axis, `width` across,
    at angle `ang` (radians, right-handed about +axis from the axis's reference direction)."""
    a, u, w, _ = frame_of(axis)
    lo, hi = [0.0] * 3, [0.0] * 3
    lo[a], hi[a] = a0, a1
    lo[u], hi[u] = c[u] + r0, c[u] + r1
    lo[w], hi[w] = c[w] - width / 2, c[w] + width / 2
    el = box(lo, hi, name, part, tex)
    if abs(ang) > 1e-12:
        rotate([el], axis, math.degrees(ang), c)
    return el


def disc(axis, c, a0, a1, r, name, part, tex, k=4, phase=0.0, step=None):
    """A plain round part: k strips as long as the 2k-gon is across, 180/k degrees apart; each strip `step`
    shorter at both ends than the last (by default a hair, at most 0.012)."""
    a, u, w, _ = frame_of(axis)
    half = r * math.tan(math.pi / (2 * k))
    out = []
    st = min(0.012, (a1 - a0) / (4 * k)) if step is None else step   # each strip shorter than the last, so no two ends share a plane
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


def annulus(axis, c, a0, a1, r_in, r_out, n, name, part, tex, phase=0.0, skip=(), step=0.02):
    """A ring of n boxes from r_in to r_out, each as wide as a side of the n-gon at r_out; every
    other one `step` shorter at each end, so their ends never share a plane."""
    w = 2 * r_out * math.tan(math.pi / n)
    out = []
    for i in range(n):
        if i in skip:
            continue
        s = step if i % 2 else 0.0
        out.append(radial(axis, c, a0 + s, a1 - s, r_in, r_out, w, phase + TAU * i / n, f"{name}{i + 1}", part, tex))
    return out


def teeth(axis, c, a0, a1, root, tip, n, width, name, part, tex, phase=0.0, taper=None):
    """n teeth from just inside `root` to `tip`; with `taper` (a width at the tip), each tooth is two
    boxes, the outer one narrower, so it reads as a tapered tooth."""
    out = []
    for i in range(n):
        ang = phase + TAU * i / n
        if taper is None:
            out.append(radial(axis, c, a0, a1, root - 0.25, tip, width, ang, f"{name}{i + 1}", part, tex))
        else:
            mid = (root + tip) / 2 + 0.1
            out.append(radial(axis, c, a0, a1, root - 0.25, mid, width, ang, f"{name}{i + 1}a", part, tex))
            out.append(radial(axis, c, a0 + 0.02, a1 - 0.02, mid, tip, taper, ang, f"{name}{i + 1}b", part, tex))
    return out


def gear(axis, c, a0, a1, pitch_r, n, module, name, part, tex, phase=0.0, body_k=4, internal=False):
    """A spur gear: a polygon body to the root and n teeth of `module` (phase: angle of tooth 1)."""
    width = math.pi * module / 2
    if internal:
        return [radial(axis, c, a0, a1, pitch_r - module, pitch_r + 1.25 * module + 0.3, width, phase + TAU * i / n, f"{name}_tooth{i + 1}", part, tex)
                for i in range(n)]
    root, tip = pitch_r - 1.25 * module, pitch_r + module
    body = disc(axis, c, a0, a1, root, f"{name}_body", part, tex, k=body_k, phase=phase)
    return body + teeth(axis, c, a0 + 0.03, a1 - 0.03, root, tip, n, width, f"{name}_tooth", part, tex, phase)


def bevel(axis, c, a0, a1, pitch_r, n, name, part, tex, toward=1.0, phase=0.0):
    """A small bevel gear: a disc and n teeth leaning 45 degrees, their tips towards `toward` along
    the axis (towards the pair's apex)."""
    a, u, w, _ = frame_of(axis)
    root = pitch_r - 0.6
    out = disc(axis, c, a0, a1, root, f"{name}_body", part, tex, k=4, phase=phase)
    for i in range(n):
        el = radial(axis, c, a0 + 0.05, a1 - 0.05, root - 0.2, pitch_r + 0.45, 0.5, 0.0, f"{name}_tooth{i + 1}", part, tex)
        piv = list(c)
        piv[a] = (a0 + a1) / 2
        piv[u] = c[u] + pitch_r
        for sgn in (1.0, -1.0):
            trial = el.clone()
            rotate([trial], "xyz"[w], 45.0 * sgn, piv)
            tip = max(trial.corners(), key=lambda q: q[u])
            if (tip[a] - piv[a]) * toward > 0:
                el = trial
                break
        rotate([el], axis, math.degrees(phase + TAU * i / n), c)
        out.append(el)
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


def ring_point(c, r, ang):
    """A point at radius r and angle ang (about +x from +y towards +z) around an x axis through c."""
    return [c[0], c[1] + r * math.cos(ang), c[2] + r * math.sin(ang)]




# ---------------------------------------------------------------- derived positions
ARBOR = (0.0, ARBOR_Y, Z_REST)               # a point on the arbor's axis (x varies)
FEED_C = (0.0, FEED[0], FEED[1])
ENTRY_C = (0.0, ENTRY[0], ENTRY[1])


def _idler():
    """The idler between A2 (entry) and B2 (feed shaft): two pitch radii from each, above their line."""
    ya, za = ENTRY
    yb, zb = FEED
    d = math.hypot(yb - ya, zb - za)
    mid = ((ya + yb) / 2, (za + zb) / 2)
    h = math.sqrt((2 * RECT_R[1]) ** 2 - (d / 2) ** 2)
    ny, nz = -(zb - za) / d, (yb - ya) / d
    if ny < 0:
        ny, nz = -ny, -nz
    return (mid[0] + h * ny, mid[1] + h * nz)


IDLER = _idler()
IDLER_C = (0.0, IDLER[0], IDLER[1])
CAM_C = (CAM_X, CAM_Y, 0.0)
SPINDLE_C = (X_BLANK, SPINDLE_Y, 0.0)
HEAD_C = (0.0, SPINDLE_Y, HEAD_Z)


def gap_angle(cls, j):
    """The authored angle (about +x from +y towards +z) of tooth gap j of the class's blank and master:
    it reaches the top, under the cutter and under a rack tooth, at the middle of pass j."""
    return j * STEP[cls] + (STROKE / 2) / PITCH_R[cls]


def lever_dir(swing):
    """The lever arm's direction after turning `swing` (radians about +x; the index turns it negative):
    down and LEVER_REST north at rest."""
    b = LEVER_REST + swing
    return (0.0, -math.cos(b), -math.sin(b))


def on_axis(c, d, r):
    return tuple(c[k] + r * d[k] for k in range(3))


ROLLER = on_axis(ARBOR, lever_dir(0.0), LEVER_L)
PAWL_PIN_R = 5.55
PAWL_PIN = on_axis(ARBOR, lever_dir(0.0), PAWL_PIN_R)
PAWL_TIP_R = 4.45
PAWL_TIP_ANG = math.atan2(PAWL_PIN[2] - ARBOR[2], PAWL_PIN[1] - ARBOR[1]) - 16.0 * DEG
RIM_FACE0 = PAWL_TIP_ANG - LOST              # a rim tooth's face: the pawl pushes faces towards -angle
PAWL_LIFT = 22.0 * DEG
CHECK_ANG = RIM_FACE0 + 10 * TAU / RIM_TEETH + 3.0 * DEG    # the check pawl's tip, just past a face, up and north
CHECK_TIP_R = 4.45
CHECK_PIN = ring_point(ARBOR, 5.55, CHECK_ANG + 14.0 * DEG)
CHECK_LIFT = 20.0 * DEG
SHIELD_SWING = 60.0 * DEG                    # stowed for the small master; swung in for the large one
PUSHER_GAP = 0.3
PUSHER_Z0 = ROLLER[2] - ROLLER_R - PUSHER_GAP                       # the pusher's face at rest (z)
PUSH_TRAVEL = LEVER_L * (math.sin(LEVER_REST) - math.sin(LEVER_REST - LEVER_SWING))   # the roller's travel along z
INDEX_PIN_Z0 = PUSHER_Z0 - 0.7               # the index slider's pin with the pusher at rest
DETENT_R, NOTCH_R = 1.2, 0.9                 # the sun's detent disc and its one notch
STOP_Z = (Z_REST + 3.2, Z_REST + 3.8)        # the lever's stop post, south of the pusher's reach


# ---------------------------------------------------------------- builders: the drive
def build_drive():
    """Entry shaft (oak, the vanilla axle's cross profile) with the lower cone and the rectifier's
    driving gears; the feed rectifier (loose gears with catches, idler); the feed shaft, clutch and worm;
    the head shaft with the upper cone and its bevel; the belt; the spindle, its bevel and the cutter."""
    out = []
    y, z = ENTRY
    # the axle's cross profile as far as A1, then a round steel shaft through the rectifier to the column's front wall
    out.append(box([0.0, y - 0.6, z - 1.5], [RECT_X[0][0] + 0.2, y + 0.6, z + 1.5], "entry_shafta", "entry", "oak"))
    out.append(box([0.0, y - 1.5, z - 0.6], [RECT_X[0][0] + 0.2, y + 1.5, z + 0.6], "entry_shaftb", "entry", "oak"))
    out += rod("x", ENTRY_C, RECT_X[0][0], 10.4, 0.5, "entry_shaft_rod", "entry", "steel")
    for i, ((a0, a1), r) in enumerate(zip(CONE_X, CONE_LOW), 1):
        out += disc("x", ENTRY_C, a0 + 0.01 * (i % 2), a1 - 0.01 * (i % 2), r, f"entry_cone{i}", "entry", "iron", k=8)
    (a0, a1), (b0, b1) = RECT_X
    out += gear("x", ENTRY_C, a0, a1, RECT_R[0], RECT_TEETH[0], RECT_MOD, "entry_a1", "entry", "steel")
    out += gear("x", ENTRY_C, b0, b1, RECT_R[1], RECT_TEETH[1], RECT_MOD, "entry_a2", "entry", "steel")
    # the feed rectifier: B1 meshes A1 directly (turns -theta); B2 through the idler (+theta)
    out += gear("x", FEED_C, a0, a1, RECT_R[0], RECT_TEETH[0], RECT_MOD, "rectb1", "rectb1", "steel",
                phase=mesh_phase(ENTRY_C, 0.0, RECT_TEETH[0], FEED_C, RECT_TEETH[0]))
    out.append(box([a1, FEED[0] + 0.5, FEED[1] - 0.25], [a1 + 0.25, FEED[0] + 1.6, FEED[1] + 0.25], "rectb1_catch", "rectb1", "steel"))
    ip = mesh_phase(ENTRY_C, 0.0, RECT_TEETH[1], IDLER_C, RECT_TEETH[1])
    out += gear("x", IDLER_C, b0, b1, RECT_R[1], RECT_TEETH[1], RECT_MOD, "idler", "idler", "steel", phase=ip)
    out += gear("x", FEED_C, b0, b1, RECT_R[1], RECT_TEETH[1], RECT_MOD, "rectb2", "rectb2", "steel",
                phase=mesh_phase(IDLER_C, ip, RECT_TEETH[1], FEED_C, RECT_TEETH[1]))
    out.append(box([b1, FEED[0] - 1.6, FEED[1] - 0.25], [b1 + 0.25, FEED[0] - 0.5, FEED[1] + 0.25], "rectb2_catch", "rectb2", "steel"))
    # feed shaft (rectified), with the catches' hubs and the clutch sleeve
    out += rod("x", FEED_C, 6.3, 30.8, 0.4, "feedshaft_rod", "feedshaft", "steel")
    out += disc("x", FEED_C, a1, b0, 0.9, "feedshaft_hub1", "feedshaft", "steel", k=4)
    out += disc("x", FEED_C, b1, b1 + 0.6, 0.9, "feedshaft_hub2", "feedshaft", "steel", k=4)
    out += disc("x", FEED_C, CLUTCH_X[0], CLUTCH_X[1], 0.9, "clutch_sleeve", "clutch", "steel", k=4)
    out += disc("x", FEED_C, CLUTCH_PIVOT[0] - 0.2, CLUTCH_PIVOT[0] + 0.2, 1.15, "clutch_groove", "clutch", "steel", k=4, phase=math.pi / 8)
    for i in range(3):
        out.append(radial("x", FEED_C, CLUTCH_X[1], CLUTCH_X[1] + 0.25, 0.45, 0.85, 0.4, TAU * i / 3, f"clutch_dog{i + 1}", "clutch", "steel"))
    out += worm()
    for i, ((a0, a1), r) in enumerate(zip(CONE_X, CONE_HIGH), 1):
        out += disc("x", HEAD_C, a0 + 0.01 * (i % 2), a1 - 0.01 * (i % 2), r, f"headshaft_cone{i}", "headshaft", "iron", k=8)
    out += rod("x", HEAD_C, 0.4, X_BLANK - MITRE_R - 0.35, 0.5, "headshaft_rod", "headshaft", "steel")
    out += bevel("x", HEAD_C, X_BLANK - MITRE_R - 0.7, X_BLANK - MITRE_R + 0.05, MITRE_R, 10, "headshaft_bevel", "headshaft", "steel", toward=1.0)
    out += rod("z", SPINDLE_C, Z_CUT - 4.15, HEAD_Z - MITRE_R + 0.2, 0.5, "spindle_rod", "spindle", "steel")
    out += bevel("z", SPINDLE_C, HEAD_Z - MITRE_R - 0.05, HEAD_Z - MITRE_R + 0.7, MITRE_R, 10, "spindle_bevel", "spindle", "steel",
                 toward=1.0, phase=math.pi / 10)
    for i, (a0, a1) in enumerate(((Z_CUT - 1.75, Z_CUT - 1.1), (Z_CUT + 1.1, Z_CUT + 1.75)), 1):
        out += disc("z", SPINDLE_C, a0, a1, 0.9, f"spindle_collar{i}", "spindle", "cupronickel", k=4)
    out += cutter()
    out += belt()
    return out


CLUTCH_X = (20.0, 21.2)
CLUTCH_PIVOT = (20.6, FEED[0] + 1.9, FEED[1])


def mesh_phase(c1, phase1, n1, c2, n2):
    """The tooth phase (angle of tooth 1, about +x) for gear 2 meshing gear 1 (whose tooth 1 is at
    phase1): on the line of centres, gear 1's local tooth phase there and gear 2's gap meet."""
    d = math.atan2(c2[2] - c1[2], c2[1] - c1[1])
    p1, p2 = TAU / n1, TAU / n2
    off = ((d - phase1) / p1) % 1.0
    return (d + math.pi) + p2 / 2 + off * p2


def worm():
    """A single-start worm loose on the feed shaft, over the worm wheel's top: a core, a helix of short
    boxes (12 per turn) at the pitch radius, its lead the wheel's circular pitch, and a hub with dogs
    facing the clutch sleeve."""
    dog0 = CLUTCH_X[1] + CLUTCH_THROW          # the worm's dogs start where the sleeve's reach when it is thrown in
    hub0 = dog0 + 0.3
    out = disc("x", FEED_C, hub0, WORM_X[1] + 0.3, WORM_R - 1.25 * WORM_MOD + 0.05, "worm_core", "worm", "steel", k=4)
    out += disc("x", FEED_C, hub0, WORM_X[0] - 0.4, 0.8, "worm_hub", "worm", "steel", k=4)
    for i in range(3):
        out.append(radial("x", FEED_C, dog0, hub0, 0.45, 0.85, 0.4, TAU * i / 3 + math.pi / 3, f"worm_dog{i + 1}", "worm", "steel"))
    n = int(12 * (WORM_X[1] - WORM_X[0]) / WORM_LEAD)
    lam = math.degrees(math.atan2(WORM_LEAD, TAU * WORM_R))
    chord = TAU * (WORM_R + 0.15) / 12 / math.cos(math.radians(lam)) + 0.08
    for i in range(n):
        ang = TAU * i / 12
        x = WORM_X[0] + WORM_LEAD * i / 12
        el = box([x - 0.2, FEED[0] + WORM_R - 1.25 * WORM_MOD, FEED[1] - chord / 2], [x + 0.2, FEED[0] + WORM_R + WORM_MOD, FEED[1] + chord / 2],
                 f"worm_thread{i + 1}", "worm", "steel")
        rotate([el], "y", -lam, (x, FEED[0] + WORM_R, FEED[1]))
        rotate([el], "x", math.degrees(ang), FEED_C)
        out.append(el)
    return out


def cutter():
    """The formed cutter: one tooth of the rack, gashed: three bands of 12 teeth whose thickness along z
    steps with the rack tooth's flanks, on a thicker body. The body reaches out to just short of the
    blank's tip circle at the bottom of the cutter (a box's corner is its lowest point), and each band
    overlaps the next and the body, so the teeth stand on the body with no gap or seam at any angle."""
    c = (X_BLANK, SPINDLE_Y, Z_CUT)
    # the body: an octagon whose corners stay inside CUTTER_R - (ADD + DED), the radius at the blank's tip circle
    clear = CUTTER_R - ADD - DED - 0.015
    out = disc("z", c, Z_CUT - 0.6, Z_CUT + 0.6, clear * math.cos(math.pi / 16), "cutter_body", "cutter", "steel", k=8, step=ZF_GAP)
    tw = lambda depth: math.pi * MODULE / 2 - 2 * (DED - depth) * math.tan(20 * DEG)    # noqa: E731  the tooth's width `depth` above its tip
    # (r0, r1, thickness, the radius the gash's width is taken at): each band is as thick as the rack tooth at its
    # outer end's depth or thinner, so it stays inside the tooth's flanks; the tip band runs into the middle one,
    # the middle one into the inner one, and the inner one into the body. The inner one is the rack tooth's
    # thickness at its pitch line, 0.1 inside its flanks either side: the blank's box teeth are not the generated
    # form, and a band as thick as the flanks allow there would graze their tips
    bands = ((CUTTER_R - 0.4, CUTTER_R, tw(0.0), CUTTER_R), (CUTTER_R - 0.95, CUTTER_R - 0.35, tw(0.35), CUTTER_R - 0.35),
             (clear - 0.15, CUTTER_R - 0.9, tw(DED), CUTTER_R - 0.35))
    for b, (r0, r1, t, rw) in enumerate(bands, 1):
        w = TAU * rw / 12 * 0.62 - (0.02 if b == 3 else 0.0)    # the inner band a hair narrower than the middle one it carries
        for i in range(12):
            out.append(radial("z", c, Z_CUT - t / 2, Z_CUT + t / 2, r0, r1, w, TAU * i / 12 + 0.02 * b, f"cutter_band{b}_{i + 1}", "cutter", "steel"))
    return out


def belt():
    """The open belt on the cones' first steps: two straight runs on the pulleys' outer tangents and a
    wrap of short boxes round each pulley, in the plane of the first step."""
    x0, x1 = CONE_X[0][0] + 0.15, CONE_X[0][1] - 0.15
    (y1, z1), r1 = ENTRY, CONE_LOW[0] + 0.02
    (y2, z2), r2 = (SPINDLE_Y, HEAD_Z), CONE_HIGH[0] + 0.02
    d = math.hypot(y2 - y1, z2 - z1)
    base = math.atan2(z2 - z1, y2 - y1)
    beta = math.asin((r1 - r2) / d)
    out, normals = [], {}
    for side, sgn in (("a", 1), ("b", -1)):
        n = base + sgn * (math.pi / 2 - beta)     # outer tangent: both centres on one side
        normals[side] = n
        p1 = (y1 + (r1 + 0.15) * math.cos(n), z1 + (r1 + 0.15) * math.sin(n))
        p2 = (y2 + (r2 + 0.15) * math.cos(n), z2 + (r2 + 0.15) * math.sin(n))
        el = strut([0.0, p1[0], p1[1]], [0.0, p2[0], p2[1]], 0.3, x1 - x0, f"belt_run{side}", "belt", "leather", axis="x")
        el.c[0] = (x0 + x1) / 2
        out.append(el)
    for tag, (yc, zc), r, a_from, a_to in (("low", (y1, z1), r1, normals["a"], normals["b"] + TAU),
                                            ("high", (y2, z2), r2, normals["b"], normals["a"])):
        while a_to < a_from:
            a_to += TAU
        span = a_to - a_from
        n = max(3, int(span / (TAU / 24)) + 1)
        for i in range(n):
            a = a_from + span * (i + 0.5) / n
            seg = span / n * (r + 0.15) * 1.03
            el = box([x0, yc + r, zc - seg / 2], [x1, yc + r + 0.3, zc + seg / 2], f"belt_{tag}{i + 1}", "belt", "leather")
            rotate([el], "x", math.degrees(a), (0.0, yc, zc))
            out.append(el)
    return out


# ---------------------------------------------------------------- the cycle (exactly what the gauges draw)
def trap(t, r0, r1, f0, f1):
    """One window's occupancy over a tooth: up over r0..r1, 1, down over f0..f1 (t in [0, 1))."""
    up = min(1.0, max(0.0, (t - r0) / (r1 - r0)))
    down = min(1.0, max(0.0, (f1 - t) / (f1 - f0)))
    return up * down


def table_s(t):
    return STROKE * trap(t, *T_FWD, *T_BACK)


def knee_s(t):
    return LIFT * trap(t, *T_KNEE_UP, *T_KNEE_DOWN)


def pusher_s(t):
    return PUSHER_GAP * trap(t, *T_GAP) + PUSH_TRAVEL * trap(t, T_LEVER[0], T_LEVER[1], T_LEVER[1], T_LEVER[2])


# ---------------------------------------------------------------- builders: the bed, camshaft and cams
def build_cams():
    """The camshaft in the bed (along z, under the knee): the worm wheel, the lift cam (the elevating
    screw's roller rides it), the index drum (a barrel cam: its groove moves the index slider) and the
    feed drum (moves the feed slider, which carries the table). Grooves and the lift cam's profile are
    drawn from the same windows the rig's gauges use."""
    c = CAM_C
    out = rod("z", c, CAM_Z[0], CAM_Z[1], 0.6, "cam_shaft", "camshaft", "steel")
    out += gear("z", c, WHEEL_Z - 0.6, WHEEL_Z + 0.6, WHEEL_R, WORM_TEETH, WORM_MOD, "cam_wheel", "camshaft", "steel", body_k=6,
                phase=math.pi / WORM_TEETH)
    radii = lift_profile(72)
    out += disc("z", c, LIFT_Z[0] + 0.08, LIFT_Z[1] - 0.08, CAM_R0 - 0.3, "cam_lifthub", "liftcam", "iron", k=8)
    for j in range(72):
        phi = TAU * j / 72
        r = radii[j]
        w = 2 * r * math.tan(math.pi / 72) + 0.03
        s = 0.016 * (j % 4)
        out.append(radial("z", c, LIFT_Z[0] + s, LIFT_Z[1] - s, CAM_R0 - 0.6, r, w, phi, f"cam_lift{j + 1}", "liftcam", "iron"))

    out += disc("z", c, DRUM_INDEX_Z[0], DRUM_INDEX_Z[1], DRUM_R, "cam_idrum", "camindex", "iron", k=8)
    out += disc("z", c, DRUM_FEED_Z[0], DRUM_FEED_Z[1], DRUM_R, "cam_fdrum", "camfeed", "iron", k=8)
    out += groove_rails("cam_igroove", INDEX_PIN_Z0, pusher_s)
    out += groove_rails("cam_fgroove", FEED_PIN_Z0, table_s)
    return out


def lift_profile(n, samples=1440):
    """The lift cam's radius at n angles: the inner envelope of the screw's roller (radius FOOT_R, its
    centre CAM_R0 + FOOT_R + lift(t) over the camshaft) as the cam turns under it."""
    out = []
    pts = []
    for i in range(samples):
        t = i / samples
        rc = CAM_R0 + FOOT_R + knee_s(t)
        a = -TAU * t
        pts.append((-rc * math.sin(a), rc * math.cos(a)))
    for j in range(n):
        phi = TAU * j / n
        u = (-math.sin(phi), math.cos(phi))
        best = CAM_R0 + LIFT + 1.0
        for px, py in pts:
            up = u[0] * px + u[1] * py
            disc_ = FOOT_R ** 2 - (px * px + py * py - up * up)
            if disc_ >= 0 and up > 0:
                best = min(best, up - math.sqrt(disc_))
        out.append(best)
    return out


def groove_rails(name, z0, s):
    """A barrel cam's groove: two rails of short boxes round the drum, the groove's centre at
    z0 + s(t) at the angle that is at the top (under the follower pin) at time t."""
    out = []
    half = GROOVE_HALF + 0.2
    for side, off in (("a", -half), ("b", half)):
        for j in range(SEGS):
            pa, pb = TAU * j / SEGS, TAU * (j + 1) / SEGS
            za = z0 + s((-pa / TAU) % 1.0) + off
            zb = z0 + s((-pb / TAU) % 1.0 + (1.0 if j == SEGS - 1 else 0.0)) + off if j < SEGS - 1 else z0 + s(0.0) + off
            r = (DRUM_R + RAIL_R) / 2
            pm_ = (pa + pb) / 2
            arc = r * (pb - pa)
            tx, ty = -math.cos(pm_), -math.sin(pm_)
            dz = zb - za
            length = math.hypot(arc, dz)
            e1 = (tx * arc / length, ty * arc / length, dz / length)
            e2 = (-math.sin(pm_), math.cos(pm_), 0.0)
            e3 = (e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0])
            rmat = [[e1[i], e2[i], e3[i]] for i in range(3)]
            centre = (CAM_C[0] + r * e2[0], CAM_C[1] + r * e2[1], (za + zb) / 2)
            out.append(obox(centre, (length + 0.05, RAIL_R - DRUM_R, 0.4), rmat, f"{name}{side}{j + 1}",
                            "camindex" if "igroove" in name else "camfeed", "steel"))

    return out


def build_sliders():
    """The two followers: blocks in guides on the drums' tops, a pin each down into its groove. The
    index slider's arm carries the pusher's post at the front; the feed slider's arm carries the feed
    post, which runs between the table's guide bars (the table follows it along z and slides up and
    down it with the knee)."""
    out = []
    y0, y1 = SLIDER_Y
    for tag, z0 in (("indexslider", INDEX_PIN_Z0), ("feedslider", FEED_PIN_Z0)):
        out.append(box([CAM_X - 1.2, y0, z0 - 0.5], [CAM_X + 1.2, y1, z0 + 0.5], f"{tag}_block", tag, "iron"))
        out.append(box([CAM_X - 0.25, CAM_Y + DRUM_R + 0.1, z0 - 0.25], [CAM_X + 0.25, y0, z0 + 0.25], f"{tag}_pin", tag, "steel"))
    z0 = INDEX_PIN_Z0
    out.append(box([CAM_X + 1.2, y0 + 0.15, z0 - 0.35], [31.9, y1 - 0.15, z0 + 0.35], "indexslider_arm", "indexslider", "iron"))
    out.append(box([31.3, y1 - 0.15, z0 - 0.35], [31.9, ROLLER[1] + 0.7, z0 + 0.35], "indexslider_post", "indexslider", "iron"))
    # the pusher's pad slides on the post, set at the roller's height for the master fitted
    out.append(box([30.0, ROLLER[1] - 0.75, PUSHER_Z0 - 0.35], [31.25, ROLLER[1] + 0.25, PUSHER_Z0], "pusher_pad", "pusher", "steel"))
    out.append(box([31.25, ROLLER[1] - 0.75, z0 - 0.55], [31.95, ROLLER[1] + 0.25, z0 - 0.35], "pusher_collar1", "pusher", "iron"))
    out.append(box([31.25, ROLLER[1] - 0.75, z0 + 0.35], [31.95, ROLLER[1] + 0.25, z0 + 0.55], "pusher_collar2", "pusher", "iron"))
    z0 = FEED_PIN_Z0
    out.append(box([CAM_X + 1.2, y0 + 0.15, z0 - 0.35], [31.4, y1 - 0.15, z0 + 0.35], "feedslider_arm", "feedslider", "iron"))
    out.append(box([30.6, y0 + 0.15, z0 + 0.35], [31.4, y1 - 0.15, FEED_GUIDE_Z[1] + 0.05], "feedslider_arm2", "feedslider", "iron"))
    out.append(box([30.6, y1 - 0.15, FEED_GUIDE_Z[0] + 0.05], [31.4, TABLE_TOP + LIFT + 0.5, FEED_GUIDE_Z[1] - 0.05], "feedslider_post", "feedslider", "steel"))
    return out


# ---------------------------------------------------------------- builders: the knee and its screw
def build_knee():
    """The knee (authored with the small master's knee down): a back plate on the column's gibs, a low
    back part under the blank and master with a drip tray, two girders with the table's ways, webs; the
    elevating screw through its north web, its roller on the lift cam."""
    k = "knee"
    (bx0, bx1), (fx0, fx1) = WAYS_X
    z0, z1 = KNEE_Z
    out = [
        box([KNEE_X[0], KNEE_BOTTOM, z0], [12.0, KNEE_BACK_TOP, z1], "knee_back", k, "iron"),
        box([12.0, KNEE_BOTTOM, z0], [bx0, KNEE_BACK_TOP, z1], "knee_floor", k, "iron"),
        box([bx0, KNEE_BOTTOM, z0], [bx1, WAY_TOP - 1.5, z1], "knee_girder_b", k, "iron"),
        box([fx0, KNEE_BOTTOM, z0], [fx1, WAY_TOP - 1.5, z1], "knee_girder_f", k, "iron"),
        box([bx0, WAY_TOP - 1.5, z0], [bx1, WAY_TOP, z1], "knee_way_b", k, "steel"),
        box([fx0, WAY_TOP - 1.5, z0], [fx1, WAY_TOP, z1], "knee_way_f", k, "steel"),
        box([bx1, KNEE_BOTTOM, z0], [fx0, WAY_TOP - 1.5, z0 + 1.0], "knee_web_n", k, "iron"),
        box([bx1, KNEE_BOTTOM, SCREW_C[2] - 0.9], [fx0, WAY_TOP - 0.2, SCREW_C[2] + 0.9], "knee_web_m", k, "iron"),
        box([bx1, KNEE_BOTTOM, z1 - 1.0], [fx0, WAY_TOP - 1.5, z1], "knee_web_s", k, "iron"),
    ]
    # the drip tray under the blank: a pan on the knee's back part, catching the cutting oil
    tx0, tx1, tz0, tz1 = 16.6, bx0, max(z0 + 0.1, Z_REST - 6.2), Z_REST + STROKE + 6.2
    out.append(box([tx0, KNEE_BACK_TOP, tz0], [tx1, KNEE_BACK_TOP + 0.15, tz1], "knee_tray", k, "iron"))
    for tag, lo, hi in (("w", [tx0, KNEE_BACK_TOP + 0.15, tz0], [tx0 + 0.25, KNEE_BACK_TOP + 0.6, tz1]),
                        ("n", [tx0 + 0.25, KNEE_BACK_TOP + 0.15, tz0], [tx1, KNEE_BACK_TOP + 0.6, tz0 + 0.25]),
                        ("s", [tx0 + 0.25, KNEE_BACK_TOP + 0.15, tz1 - 0.25], [tx1, KNEE_BACK_TOP + 0.6, tz1])):
        out.append(box(lo, hi, f"knee_tray_{tag}", k, "iron"))
    foot = CAM_Y + CAM_R0 + FOOT_R
    top = WAY_TOP + 2.2
    sc = (SCREW_C[0], 0.0, SCREW_C[2])
    out += rod("y", sc, foot + FOOT_R + 0.25, top, 0.4, "screw_rod", "screw", "steel")
    out += rod("z", (SCREW_C[0], foot, 0.0), LIFT_Z[0] + 0.08, LIFT_Z[1] - 0.08, FOOT_R, "screw_foot", "screw", "steel", k=4)
    out.append(box([SCREW_C[0] - 0.55, foot - 0.15, LIFT_Z[1] - 0.05], [SCREW_C[0] + 0.55, foot + FOOT_R + 0.25, LIFT_Z[1] + 0.2], "screw_fork1", "screw", "steel"))
    out.append(box([SCREW_C[0] - 0.55, foot - 0.15, LIFT_Z[0] - 0.2], [SCREW_C[0] + 0.55, foot + FOOT_R + 0.25, LIFT_Z[0] + 0.05], "screw_fork2", "screw", "steel"))
    out += annulus("y", sc, top - 0.4, top, 0.9, 1.3, 8, "screw_wheel", "screw", "iron")
    for i in range(4):
        out.append(radial("y", sc, top - 0.36, top - 0.04, 0.38, 0.95, 0.25, math.pi * i / 2, f"screw_spoke{i + 1}", "screw", "iron"))
    return out


# ---------------------------------------------------------------- builders: the table and its fittings
def build_table():
    t = "table"
    zc = Z_REST
    out = [
        box([TABLE_X[0], WAY_TOP, TABLE_Z[0]], [TABLE_X[1], TABLE_TOP, TABLE_Z[1]], "table_plate", t, "iron"),
        box([TABLE_X[1], WAY_TOP + 0.4, zc + 4.4], [31.9, TABLE_TOP, FEED_GUIDE_Z[0]], "table_ear_s1", t, "iron"),
        box([TABLE_X[1], WAY_TOP + 0.4, FEED_GUIDE_Z[1]], [31.9, TABLE_TOP, TABLE_Z[1]], "table_ear_s2", t, "iron"),
        box([30.6, WAY_TOP - 2.5, FEED_GUIDE_Z[0] - 0.35], [31.4, WAY_TOP + 0.4, FEED_GUIDE_Z[0]], "table_guide1", t, "steel"),
        box([30.6, WAY_TOP - 2.5, FEED_GUIDE_Z[1]], [31.4, WAY_TOP + 0.4, FEED_GUIDE_Z[1] + 0.35], "table_guide2", t, "steel"),
    ]
    # the dividing head's pedestal: two uprights with bearing caps round the arbor
    for i, (x0, x1) in enumerate(((22.6, 23.4), (24.2, 25.0)), 1):
        out.append(box([x0, TABLE_TOP, zc - 1.6], [x1, ARBOR_Y - 1.1, zc + 1.6], f"table_upright{i}", t, "iron"))
        out.append(box([x0, ARBOR_Y - 1.1, zc - 1.2], [x1, ARBOR_Y + 1.1, zc + 1.2], f"table_bearing{i}", t, "iron"))
    out.append(box([23.4, TABLE_TOP, zc - 1.6], [24.2, TABLE_TOP + 1.8, zc + 1.6], "table_upright_tie", t, "iron"))
    out += disc("x", ARBOR, 25.0, J_X[0] - 0.02, 1.2, "table_boss", t, "iron", k=4)
    # the check pawl's post, up the north side of the head clear of the rim, and an arm over to the pin
    pz = Z_REST - RIM_R[1] - 0.25
    out.append(box([J_BAND[0], TABLE_TOP, pz - 0.7], [J_BAND[1], CHECK_PIN[1] + 0.3, pz], "table_checkpost", t, "iron"))
    out.append(box([J_BAND[0], CHECK_PIN[1] - 0.3, pz], [J_BAND[1], CHECK_PIN[1] + 0.3, CHECK_PIN[2] + 0.3], "table_checkarm", t, "iron"))
    # the lever's stop: a post on a front ear south of the pusher's reach, a bar over to the lever's tail
    tail = on_axis(ARBOR, lever_dir(0.0), -1.2)
    out.append(box([TABLE_X[1], WAY_TOP + 0.4, STOP_Z[0] - 0.2], [30.2, TABLE_TOP, STOP_Z[1] + 0.2], "table_stopfoot", t, "iron"))
    out.append(box([LEVER_X[0] + 0.05, TABLE_TOP, STOP_Z[0]], [LEVER_X[1] - 0.05, tail[1] + 0.5, STOP_Z[1]], "table_stoppost", t, "iron"))
    out.append(box([LEVER_X[0] + 0.05, tail[1] - 0.15, tail[2] + 0.3], [LEVER_X[1] - 0.05, tail[1] + 0.5, STOP_Z[0]], "table_stop", t, "steel"))
    # the detent's post on the south edge
    out.append(box([DETENT_X[0] - 0.15, TABLE_TOP, zc + 5.7], [DETENT_X[1] + 0.15, ARBOR_Y + 0.6, zc + 6.4], "table_detentpost", t, "iron"))
    return out


# ---------------------------------------------------------------- builders: the arbor, the masters and blanks
ARBOR_X = (11.7, 25.95)


def build_arbor():
    """The arbor through the pedestal, with its nut and the collars between the stations; the carrier's
    back spider is on its end, and the sun shaft pilots into it."""
    out = rod("x", ARBOR, ARBOR_X[0], ARBOR_X[1], 0.55, "arbor_rod", "arbor", "steel")
    out += disc("x", ARBOR, ARBOR_X[0], STATION["master"][0], 1.1, "arbor_nut", "arbor", "steel", k=3)
    out += disc("x", ARBOR, STATION["master"][1], STATION["blank"][0], 1.3, "arbor_collar1", "arbor", "steel", k=4)
    out += disc("x", ARBOR, STATION["blank"][1], 22.6, 1.3, "arbor_collar2", "arbor", "steel", k=4)
    return out


UNDERCUT = 0.1


def toothed(cls, prefix, part, x0, x1, tex, body_k):
    """A gear of the class's size: a body to the root and tapered teeth at the generated pitch, gaps at
    gap_angle (the master's teeth are the gear's)."""
    root = PITCH_R[cls] - DED
    out = disc("x", ARBOR, x0, x1, root * math.cos(math.pi / (2 * body_k)) - 0.05, f"{prefix}_body", part, tex, k=body_k, step=ZF_GAP)
    # the teeth's faces a gap in from the body's deepest strip's, where they run into the body
    return out + gear_teeth(cls, prefix, part, x0, x1, tex, inset=body_k * ZF_GAP)


def gear_teeth(cls, prefix, part, x0, x1, tex, inset):
    """The class's teeth, their faces `inset` in from x0 and x1 (the outer box of each 0.02 further)."""
    r, n = PITCH_R[cls], TEETH[cls]
    root, tip = r - DED, r + ADD
    # fewer than 17 teeth off a 20-degree rack are undercut at the root: the box teeth are drawn a little thinner instead
    width = math.pi * MODULE / 2 - (UNDERCUT if n < 17 else 0.0)
    return teeth("x", ARBOR, x0 + inset, x1 - inset, root, tip, n, width, f"{prefix}_tooth", part, tex,
                 phase=gap_angle(cls, 0) + STEP[cls] / 2, taper=0.45)


def spacers(cls, kind, prefix, part, tex):
    """Hubs that fill a thin gear's station to the collars, so it is located on both faces. They run 0.3 into
    the gear, so their inner ends are inside it, never a face beside the gear's own."""
    out = []
    (s0, s1), (f0, f1) = STATION[kind], face(kind, cls)
    if f0 - s0 > 0.05:
        out += disc("x", ARBOR, s0, f0 + 0.3, 1.05, f"{prefix}_hub1", part, tex, k=4, step=ZF_GAP)
        out += disc("x", ARBOR, f1 - 0.3, s1, 1.05, f"{prefix}_hub2", part, tex, k=4, step=ZF_GAP)
    return out


def build_masters():
    """The two masters, one at a time on one station: the temporal gear (12 teeth) with its crossed
    bars, and the large temporal gear (20) with an octagonal rim and a cross web, in the temporal gear's
    texture with a gold boss; each is a copy of the gear it generates, and as thick as the game's item."""
    a, b = face("master", "thin")
    out = toothed("thin", "master", "master", a, b, "temporal", 6) + spacers("thin", "master", "master", "master", "temporal")
    xa = a - 0.12
    y, z = ARBOR_Y, Z_REST
    # the bars and the boss run 0.3 into the body, so none of their inner faces lies by the body's own; their
    # outer faces are a gap apart where they cross
    g = ZF_GAP
    out.append(box([xa, y - 2.3, z - 0.35], [a + 0.3, y + 2.3, z + 0.35], "master_barv", "master", "temporal"))
    out.append(box([xa - g, y - 0.35, z - 2.3], [a + 0.3, y + 0.35, z + 2.3], "master_barh", "master", "temporal"))
    for i, dz in enumerate((-1.15, 1.15), 1):
        out.append(box([xa + g, y - 1.6, z + dz - 0.25], [a + 0.3, y + 1.6, z + dz + 0.25], f"master_barsec{i}", "master", "temporal"))
    out += disc("x", ARBOR, xa - 0.3, a + 0.3, 0.8, "master_boss", "master", "gold", k=4, step=g)
    # the twelve nubs through the body, standing out 0.3 each side as on the game's item
    for i in range(TEETH["thin"]):
        ang = gap_angle("thin", 0) + STEP["thin"] / 2 + i * STEP["thin"]
        c = ring_point(ARBOR, NUB_R, ang)
        el = box([X_MASTER - NUB_SPAN / 2, c[1] - 0.1, c[2] - 0.1], [X_MASTER + NUB_SPAN / 2, c[1] + 0.1, c[2] + 0.1],
                 f"master_nub{i + 1}", "master", "temporal")
        rotate([el], "x", math.degrees(ang), (0.0, c[1], c[2]))
        out.append(el)

    a, b = face("master", "thick")
    out += toothed("thick", "mastl", "masterlarge", a, b, "temporal", 8)
    out += annulus("x", ARBOR, a - 0.15, a + 0.3, 3.0, 3.9, 8, "mastl_rim", "masterlarge", "temporal", phase=math.pi / 8, step=g)
    for i in range(4):
        out.append(radial("x", ARBOR, a - 0.15 + 2 * g, a + 0.3, 0.9, 3.05, 0.8, math.pi * i / 2, f"mastl_web{i + 1}", "masterlarge", "temporal"))
    out += disc("x", ARBOR, a - 0.4, a + 0.3, 1.0, "mastl_boss", "masterlarge", "gold", k=4, step=g)
    return out


# The blank's faces, as depths in from its nominal faces (negative: standing proud), each stack a gap
# (ZF_GAP) apart where they overlap: the teeth one gap in, the fills at the face (`lo`) and a gap proud
# (`hi`), in front of the teeth's flanks, so before its gap is cut the rim reads as solid disc with no tooth
# outline; and the body's strips from two gaps proud outwards, so a sunk fill is behind every one of them.
FILL_DEPTH = {"lo": 0.0, "hi": -ZF_GAP}
TEETH_DEPTH = ZF_GAP
BODY_DEPTH, BODY_STEP = -2 * ZF_GAP, -ZF_GAP   # the body's most-inset strip, and each further strip's step out
FILL_MARGIN = 0.005                           # how far short of the middle of the teeth either side a fill's inner corners stop
SINK = ADD + DED + 0.3                        # how far a gap's fill sinks into the blank as the cutter takes it


def body_r(cls):
    """The blank body's inradius: just inside the root, its corners too, so they stay under the teeth and fills."""
    return PITCH_R[cls] - DED - 0.1


def fill_boxes(cls):
    """A gap's fill as (tag, r0, r1, width): from inside the root to just past the tip, each box as wide as it
    can be without its inner corners reaching past the middle of the neighbouring teeth, so it covers the whole
    gap and the teeth's flank edges either side (a neighbour's fill meets it there); the outer box runs into the
    inner one."""
    r = PITCH_R[cls]
    root, tip = r - DED, r + ADD
    mid = (root + tip) / 2 + 0.1
    out = []
    for tag, r0, r1 in (("lo", root - 0.15, mid + 0.05), ("hi", mid - 0.05, tip + 0.02)):
        out.append((tag, r0, r1, 2 * (r0 * math.tan(STEP[cls] / 2) - FILL_MARGIN)))
    return out


def build_blanks():
    """The two blanks, steel, with one fill per tooth gap (two boxes): until the cutter reaches a gap its
    fill makes the rim there plain disc, face to face and root to tip; it sinks into the body as the cutter
    generates that gap, so the gear appears tooth by tooth, and once sunk it is wholly inside the body."""
    out = []
    for cls, prefix, part, gprefix in (("thin", "blanks", "blanksmall", "gs"), ("thick", "blankl", "blanklarge", "gl")):
        f0, f1 = face("blank", cls)
        k = 6 if cls == "thin" else 8
        rb = body_r(cls)
        half = rb * math.tan(math.pi / (2 * k))
        phase = gap_angle(cls, 0) + STEP[cls] / 2
        for i in range(k):
            d = BODY_DEPTH + BODY_STEP * (k - 1 - i)
            el = box([f0 + d, ARBOR[1] - rb, ARBOR[2] - half], [f1 - d, ARBOR[1] + rb, ARBOR[2] + half], f"{prefix}_body_{i + 1}", part, "steel")
            rotate([el], "x", math.degrees(phase + math.pi * i / k), ARBOR)
            out.append(el)
        out += gear_teeth(cls, prefix, part, f0, f1, "steel", inset=TEETH_DEPTH)
        out += spacers(cls, "blank", prefix, part, "steel")
        for j in range(TEETH[cls]):
            ang = gap_angle(cls, j)
            pid = f"{gprefix}{j + 1:02d}"
            for tag, r0, r1, w in fill_boxes(cls):
                d = FILL_DEPTH[tag]
                out.append(radial("x", ARBOR, f0 + d, f1 - d, r0, r1, w, ang, f"{pid}_{tag}", pid, "steel"))
    return out


# ---------------------------------------------------------------- builders: the Jonas head and the index
PLANET_ANG = (math.pi / 6, math.pi / 6 + TAU / 3, math.pi / 6 + 2 * TAU / 3)


def build_jonas():
    """The Jonas dividing head: a planetary in an ornate housing. The housing is the ring (20 internal
    teeth) and carries the rim ratchet the pawl steps; the carrier is the arbor's; the sun (10) is on a
    shaft held by the detent at the index and free while the master rolls. Cupronickel, steel and gold."""
    out = []
    j0, j1 = J_X
    c = ARBOR
    out += annulus("x", c, j0, j0 + 0.4, 0.62, J_OUT_R, 16, "jring_back", "jring", "cupronickel")
    out += annulus("x", c, j0 + 0.4, J_BAND[0], J_R["ring"] + 1.25 * J_MOD + 0.05, J_OUT_R, 16, "jring_drum", "jring", "cupronickel",
                   phase=math.pi / 16)
    out += gear("x", c, j0 + 0.55, J_BAND[0] - 0.1, J_R["ring"], J_TEETH["ring"], J_MOD, "jring_int", "jring", "steel", internal=True,
                phase=ring_phase())
    # the rim ratchet: a band of 40 sawtooth teeth round the housing (faces towards -angle)
    out += annulus("x", c, J_BAND[0], J_BAND[1], J_R["ring"] + 1.25 * J_MOD + 0.05, RIM_R[0], 20, "jring_rimbody", "jring", "steel")
    for k in range(RIM_TEETH):
        fa = RIM_FACE0 + TAU * k / RIM_TEETH
        a = ring_point(c, RIM_R[1], fa - 0.01)
        b = ring_point(c, RIM_R[0], fa - TAU / RIM_TEETH + 0.01)
        el = strut(a, b, 0.4, J_BAND[1] - J_BAND[0] - 0.04, f"jring_rim{k + 1}", "jring", "steel", axis="x")
        mid = [(a[i] + b[i]) / 2 for i in range(3)]
        rr = math.hypot(mid[1] - c[1], mid[2] - c[2])
        el.c = [(J_BAND[0] + J_BAND[1]) / 2, el.c[1] - 0.2 * (mid[1] - c[1]) / rr, el.c[2] - 0.2 * (mid[2] - c[2]) / rr]
        out.append(el)
    for i in range(6):
        out.append(radial("x", c, J_BAND[1], j1, 0.8, RIM_R[0] - 0.1, 0.5, TAU * i / 6 + math.pi / 6, f"jring_spoke{i + 1}", "jring", "steel"))
    out += annulus("x", c, J_BAND[1], j1, 0.42, 0.8, 8, "jring_eye", "jring", "steel")
    out += annulus("x", c, j0 + 0.4, j0 + 0.65, J_OUT_R - 0.1, J_OUT_R + 0.18, 16, "jring_band1_", "jring", "gold")
    out += annulus("x", c, J_BAND[0] - 0.3, J_BAND[0] - 0.05, J_OUT_R - 0.1, J_OUT_R + 0.18, 16, "jring_band2_", "jring", "gold")
    for i in range(8):
        out.append(radial("x", c, j0 + 1.0, j0 + 1.3, J_OUT_R - 0.1, J_OUT_R + 0.15, 0.3, TAU * i / 8, f"jring_rivet{i + 1}", "jring", "gold"))
    # the carrier: two spiders, the back one on the arbor, with the planets' pins
    for k, (a0, a1) in enumerate(((j0 + 0.45, j0 + 0.7), (J_BAND[0] - 0.55, J_BAND[0] - 0.3)), 1):
        out += disc("x", c, a0, a1, 0.75, f"jcarrier_hub{k}", "jcarrier", "cupronickel", k=4)
        for i, ang in enumerate(PLANET_ANG):
            out.append(radial("x", c, a0, a1, 0.6, J_CARRIER + 0.4, 0.7, ang, f"jcarrier_arm{k}_{i + 1}", "jcarrier", "cupronickel"))
    for i, ang in enumerate(PLANET_ANG):
        pc = ring_point(c, J_CARRIER, ang)
        out += rod("x", pc, j0 + 0.45, J_BAND[0] - 0.3, 0.15, f"jcarrier_pin{i + 1}", "jcarrier", "gold", k=2)
        out += gear("x", pc, j0 + 0.8, J_BAND[0] - 0.65, J_R["planet"], J_TEETH["planet"], J_MOD, f"jplanet{i + 1}", f"jplanet{i + 1}", "steel",
                    phase=planet_phase(i), body_k=2)
    out += gear("x", c, j0 + 0.8, J_BAND[0] - 0.65, J_R["sun"], J_TEETH["sun"], J_MOD, "sun_gear", "sun", "steel", phase=0.0)
    out += rod("x", c, SUN_SHAFT_X[0], SUN_SHAFT_X[1], 0.4, "sun_shaft", "sun", "steel")
    # the sun's detent disc: one notch, at the south, where the plunger comes in
    nz = 12
    notch = 3                                # segment 3 of 12 is at 90 degrees: +z, the south
    out += annulus("x", c, DETENT_X[0], DETENT_X[1], 0.42, DETENT_R, nz, "sun_detentdisc", "sun", "steel", skip=(notch,))
    out.append(radial("x", c, DETENT_X[0] + 0.02, DETENT_X[1] - 0.02, 0.42, NOTCH_R, 2 * DETENT_R * math.tan(math.pi / nz),
                      TAU * notch / nz, "sun_detentnotch", "sun", "steel"))
    return out


def planet_phase(i):
    pc = ring_point(ARBOR, J_CARRIER, PLANET_ANG[i])
    return mesh_phase(ARBOR, 0.0, J_TEETH["sun"], pc, J_TEETH["planet"])


def ring_phase():
    """The ring's internal tooth phase from its mesh with planet 0: on the line from the axis through
    the planet, outward, the planet's tooth meets the ring's gap."""
    a = PLANET_ANG[0]
    p0 = planet_phase(0)
    pp = TAU / J_TEETH["planet"]
    off = ((a - p0) / pp) % 1.0
    pr = TAU / J_TEETH["ring"]
    return a + pr / 2 - off * pr


def build_index():
    """The shield (stowed for the small master, swung in for the large one so the pawl takes three rim
    teeth instead of five), the lever with its roller, tail and pawl, the check pawl, and the detent
    plunger."""
    out = []
    c = ARBOR
    out += annulus("x", c, SHIELD_X[0], SHIELD_X[1], 0.42, 0.75, 8, "shield_hub", "shield", "steel")
    a_hi = PAWL_TIP_ANG + 3.0 * DEG - SHIELD_SWING
    span = LEVER_SWING - PUSH["thick"]
    for i in range(4):
        ang = a_hi - (span - 1.0 * DEG) * (i + 0.5) / 4
        out.append(radial("x", c, SHIELD_X[0], SHIELD_X[1], RIM_R[1] - 0.25, RIM_R[1] + 0.15, 0.7, ang, f"shield_arc{i + 1}", "shield", "steel"))
    out.append(radial("x", c, SHIELD_X[0], SHIELD_X[1], 0.7, RIM_R[1] - 0.2, 0.35, a_hi - span / 2, "shield_web", "shield", "steel"))
    d0 = lever_dir(0.0)
    out += annulus("x", c, LEVER_X[0], LEVER_X[1], 0.42, 0.85, 8, "lever_hub", "lever", "steel")
    out.append(strut(on_axis(c, d0, 0.7), on_axis(c, d0, LEVER_L), 0.55, LEVER_X[1] - LEVER_X[0], "lever_arm", "lever", "steel"))
    out[-1].c[0] = (LEVER_X[0] + LEVER_X[1]) / 2
    out.append(strut(on_axis(c, d0, -0.7), on_axis(c, d0, -1.2), 0.45, LEVER_X[1] - LEVER_X[0], "lever_tail", "lever", "steel"))
    out[-1].c[0] = (LEVER_X[0] + LEVER_X[1]) / 2
    out += rod("x", (0.0, ROLLER[1], ROLLER[2]), LEVER_X[0] + 0.05, 30.9, ROLLER_R, "lever_roller", "lever", "cupronickel", k=4)
    out += rod("x", (0.0, PAWL_PIN[1], PAWL_PIN[2]), J_BAND[0], LEVER_X[1], 0.18, "pawl_pin", "pawl", "cupronickel", k=2)
    tip = ring_point(c, PAWL_TIP_R + 0.1, PAWL_TIP_ANG)
    el = strut([0.0, PAWL_PIN[1], PAWL_PIN[2]], tip, 0.35, SHIELD_X[1] - J_BAND[0] - 0.05, "pawl_body", "pawl", "cupronickel")
    el.c[0] = (J_BAND[0] + SHIELD_X[1]) / 2 - 0.02
    out.append(el)
    ctip = ring_point(c, CHECK_TIP_R + 0.1, CHECK_ANG)
    el = strut([0.0, CHECK_PIN[1], CHECK_PIN[2]], ctip, 0.35, J_BAND[1] - J_BAND[0] - 0.06, "checkpawl_body", "checkpawl", "cupronickel")
    el.c[0] = (J_BAND[0] + J_BAND[1]) / 2
    out.append(el)
    out += rod("x", CHECK_PIN, J_BAND[0], J_BAND[1], 0.2, "checkpawl_pin", "checkpawl", "cupronickel", k=2)
    # the detent plunger: along z, from its post, its tip in the sun disc's notch
    out.append(box([DETENT_X[0] + 0.05, ARBOR_Y - 0.12, Z_REST + NOTCH_R + 0.03], [DETENT_X[1] - 0.05, ARBOR_Y + 0.12, Z_REST + 6.6],
                   "detent_plunger", "detent", "steel"))
    out.append(box([DETENT_X[0] - 0.15, ARBOR_Y - 0.4, Z_REST + 6.4], [DETENT_X[1] + 0.15, ARBOR_Y + 0.4, Z_REST + 6.7], "detent_knob", "detent", "brass"))
    return out


# ---------------------------------------------------------------- builders: the oiler
def build_oiler():
    """The oiler's reservoir, the frame's: a sight-feed glass cup between a brass base and cap, with a
    needle valve, on a brass bracket on the cross-head's front face over the cutter; its oil (the
    liquid stretches with the rig's `oil`, the MachineOil tank's fill). The glass is in the Transparent
    render pass (an opaque one draws its faint texture solid and hides the oil), so the level shows
    through all four panes.
    The bracket runs down past the base to carry the injection valve (build_valve)."""
    (x0, x1), (y0, y1), (z0, z1) = OILER["x"], OILER["y"], OILER["z"]
    g = 0.15
    cz = Z_CUT
    out = [
        box([CROSSHEAD_X[1], VALVE["y"][0], cz - 2.0], [x0, y1 + 0.45, cz + 2.0], "fr_oiler_bracket", "frame", "brass"),
        box([x0, y0 - 0.3, z0], [x1, y0, z1], "fr_oiler_base", "frame", "brass"),
        box([x0, y1, z0], [x1, y1 + 0.45, z1], "fr_oiler_cap", "frame", "brass"),
        box([x0, y0, z0], [x1, y1, z0 + g], "fr_oiler_glass_n", "frame", "glass"),
        box([x0, y0, z1 - g], [x1, y1, z1], "fr_oiler_glass_s", "frame", "glass"),
        box([x0, y0, z0 + g], [x0 + g, y1, z1 - g], "fr_oiler_glass_w", "frame", "glass"),
        box([x1 - g, y0, z0 + g], [x1, y1, z1 - g], "fr_oiler_glass_e", "frame", "glass"),
    ]
    xm, zm = (x0 + x1) / 2, (z0 + z1) / 2
    out += disc("y", (xm, 0.0, zm), y1 + 0.45, OVERARM_Y[1] - 0.25, 0.12, "fr_oiler_needle", "frame", "steel", k=2)
    out += disc("y", (xm, 0.0, zm), OVERARM_Y[1] - 0.25, OVERARM_Y[1], 0.4, "fr_oiler_knob", "frame", "brass", k=4)
    # the oil: authored empty (OIL_EMPTY tall), stretched up by the rig's `oil`
    out.append(box([x0 + g + 0.02, y0, z0 + g + 0.02], [x1 - g - 0.02, y0 + OIL_EMPTY, z1 - g - 0.02], "oillevel_oil", "oillevel", "oil"))
    for el in out:
        if el.name.startswith("fr_oiler_glass"):
            el.render_pass = TRANSPARENT
    return out


OIL_FULL = OILER["y"][1] - OILER["y"][0] - 0.2    # the oil's height in a full cup

# The injection valve (`game:jonasparts-valve01`, the oiler's stage): a cupronickel body under the
# reservoir, fed from its base, a nozzle pipe in under the cross-head ending in a tip over the cutter's
# teeth, and on the operator's side a stem with a marble handle, the plunger, that pumps once a tooth.
VALVE = {"x": (OILER["x"][0], OILER["x"][0] + 1.4), "y": (27.8, 29.2), "z": (Z_CUT - 1.0, Z_CUT + 1.0)}
NOZZLE_Y = (28.9, 29.2)                             # the nozzle pipe, its top flush with the body's
DRIP_Y = NOZZLE_Y[0] - 0.25                         # the tip's mouth: the drip anchor
TIP_HALF = 0.1
DRIP_X = X_BLANK + math.sqrt(CUTTER_R ** 2 - (DRIP_Y - 0.25 - SPINDLE_Y) ** 2) + TIP_HALF   # the tip's west edge 0.25 over the tips
PLUNGER_STROKE = 0.15


def build_valve():
    """The injection valve, after the game's Jonas valve: a cupronickel body with end bosses, the feed
    from the reservoir's base into its top, the nozzle pipe west to a downturned tip over the cutter's
    teeth, and the plunger (a stem out of its east face with a white marble handle across it)."""
    (vx0, vx1), (vy0, vy1), (vz0, vz1) = VALVE["x"], VALVE["y"], VALVE["z"]
    v = "valve"
    zm = Z_CUT
    xm = (OILER["x"][0] + OILER["x"][1]) / 2
    ym = (vy0 + vy1) / 2
    out = [box([vx0, vy0, vz0], [vx1, vy1, vz1], "valve_body", v, "cupronickel")]
    for i, (za, zb) in enumerate(((vz0 - 0.35, vz0), (vz1, vz1 + 0.35)), 1):
        out.append(box([vx0 + 0.25, vy0 + 0.25, za], [vx1 - 0.25, vy1 - 0.25, zb], f"valve_boss{i}", v, "cupronickel"))
    out.append(box([xm - 0.15, vy1, zm - 0.15], [xm + 0.15, OILER["y"][0] - 0.3, zm + 0.15], "valve_feed", v, "cupronickel"))
    out.append(box([DRIP_X - TIP_HALF, NOZZLE_Y[0], zm - 0.15], [vx0, NOZZLE_Y[1], zm + 0.15], "valve_nozzle", v, "cupronickel"))
    out.append(box([DRIP_X - TIP_HALF, DRIP_Y, zm - TIP_HALF], [DRIP_X + TIP_HALF, NOZZLE_Y[0], zm + TIP_HALF], "valve_nozzle_tip", v,
                   "cupronickel"))
    p = "valveplunger"
    out.append(box([vx1, ym - 0.15, zm - 0.15], [vx1 + 0.6, ym + 0.15, zm + 0.15], "valveplunger_stem", p, "cupronickel"))
    out.append(box([vx1 + 0.6, ym - 0.2, zm - 0.5], [vx1 + 1.0, ym + 0.2, zm + 0.5], "valveplunger_handle", p, "marble"))
    return out



# ---------------------------------------------------------------- builders: the frame
def build_frame():
    """Oak sills, the iron bed (pits under the worm wheel and the lift cam), the column with its gibs, the
    overarm with the rack and the spindle's bearings; pedestals and brackets for every fixed bearing; the
    sliders' guides."""
    f = "frame"
    pit_x = (CAM_X - WHEEL_R - 1.0, CAM_X + WHEEL_R + 1.0)
    pit1 = (WHEEL_Z - 1.1, WHEEL_Z + 1.1)
    pit2 = (LIFT_Z[0] - 0.4, LIFT_Z[1] + 0.4)
    out = [
        # the sills are the machine's outline: flush with the bed's and the column's outer faces, the
        # east and west ones the full length, meeting the north and south ones' outer faces at the corners
        box([OUTLINE_X[0], 0.0, OUTLINE_Z[0]], [OUTLINE_X[0] + 3.0, SILL_TOP, OUTLINE_Z[1]], "fr_sill_w", f, "oak"),
        box([OUTLINE_X[1] - 2.4, 0.0, OUTLINE_Z[0]], [OUTLINE_X[1], SILL_TOP, OUTLINE_Z[1]], "fr_sill_e", f, "oak"),
        box([OUTLINE_X[0] + 3.0, 0.0, OUTLINE_Z[0]], [OUTLINE_X[1] - 2.4, SILL_TOP, OUTLINE_Z[0] + 2.0], "fr_sill_n", f, "oak"),
        box([OUTLINE_X[0] + 3.0, 0.0, OUTLINE_Z[1] - 2.0], [OUTLINE_X[1] - 2.4, SILL_TOP, OUTLINE_Z[1]], "fr_sill_s", f, "oak"),
        box([OUTLINE_X[0], SILL_TOP, OUTLINE_Z[0]], [OUTLINE_X[1], BED_TOP, pit1[0]], "fr_bed_n", f, "iron"),
        box([OUTLINE_X[0], SILL_TOP, pit1[1]], [OUTLINE_X[1], BED_TOP, pit2[0]], "fr_bed_m", f, "iron"),
        box([OUTLINE_X[0], SILL_TOP, pit2[1]], [OUTLINE_X[1], BED_TOP, OUTLINE_Z[1]], "fr_bed", f, "iron"),
        box([OUTLINE_X[0], SILL_TOP, pit1[0]], [pit_x[0], BED_TOP, pit1[1]], "fr_bed_p1w", f, "iron"),
        box([pit_x[1], SILL_TOP, pit1[0]], [OUTLINE_X[1], BED_TOP, pit1[1]], "fr_bed_p1e", f, "iron"),
        box([OUTLINE_X[0], SILL_TOP, pit2[0]], [pit_x[0], BED_TOP, pit2[1]], "fr_bed_p2w", f, "iron"),
        box([pit_x[1], SILL_TOP, pit2[0]], [OUTLINE_X[1], BED_TOP, pit2[1]], "fr_bed_p2e", f, "iron"),
    ]
    (cx0, cx1), (cz0, cz1) = COLUMN
    top = OVERARM_Y[0]
    out += [
        box([cx0, BED_TOP, cz0], [cx0 + 1.2, top, cz1], "fr_col_back", f, "iron"),
        box([cx1 - 1.2, BED_TOP, cz0], [cx1, top, cz1], "fr_col_front", f, "iron"),
        # the column's north side is a removable door onto the rectifier (cover_colside, below)
        box([cx0 + 1.2, BED_TOP, cz1 - 1.2], [cx1 - 1.2, top, cz1], "fr_col_side_s", f, "iron"),
        # the cap runs to the column's north end, over the door (cover_colside), which closes up under it
        box([cx0 + 1.2, top - 0.8, cz0], [cx1 - 1.2, top, cz1 - 1.2], "fr_col_cap", f, "iron"),

        box([cx1, KNEE_LOWEST - 1.0, KNEE_Z[0] + 0.6], [KNEE_X[0], WAY_TOP + LIFT + 2.0, KNEE_Z[0] + 1.6], "fr_gib_n", f, "steel"),
        box([cx1, KNEE_LOWEST - 1.0, KNEE_Z[1] - 1.6], [KNEE_X[0], WAY_TOP + LIFT + 2.0, KNEE_Z[1] - 0.6], "fr_gib_s", f, "steel"),
    ]
    for tag, (x0, x1) in (("back", (CONE_X[2][1] + 0.05, cx0)), ("front", (cx1, cx1 + 0.4))):
        out.append(box([x0, SPINDLE_Y - 1.1, HEAD_Z - 1.1], [x1, SPINDLE_Y + 1.1, HEAD_Z + 1.1], f"fr_col_boss_{tag}", f, "iron"))
    cz = Z_CUT
    out += [
        box([cx0, OVERARM_Y[0], OVERARM_Z], [CROSSHEAD_X[1], OVERARM_Y[1], cz1], "fr_overarm", f, "iron"),
        box([CROSSHEAD_X[0], OVERARM_Y[0], CROSSHEAD_Z0], [CROSSHEAD_X[1], OVERARM_Y[1], cz - 1.3], "fr_crosshead_n", f, "iron"),
        box([CROSSHEAD_X[0], OVERARM_Y[0], cz + 1.3], [CROSSHEAD_X[1], OVERARM_Y[1], OVERARM_Z], "fr_crosshead_s", f, "iron"),
        box([CROSSHEAD_X[0], OVERARM_Y[0], cz - 1.3], [16.4, OVERARM_Y[1], cz + 1.3], "fr_crosshead_w", f, "iron"),
    ]
    # the rack: a bar hung from the cross-head over the master's station, teeth below (a tooth at Z_CUT)
    rz0, rz1 = Z_CUT + RACK_K[0] * PITCH - 0.8, Z_CUT + RACK_K[1] * PITCH + 0.8
    mx0, mx1 = STATION["master"]
    out.append(box([CROSSHEAD_X[0] + 0.4, H_PITCH + DED + 0.4, rz0], [16.4, OVERARM_Y[0], rz1], "fr_rack_bar", f, "iron"))
    out.append(box([mx0 - 0.1, H_PITCH + DED, rz0], [16.4, H_PITCH + DED + 0.4, rz1], "fr_rack_web", f, "steel"))
    for k in range(RACK_K[0], RACK_K[1] + 1):
        z = Z_CUT + k * PITCH
        wp = math.pi * MODULE / 2
        lo_w = wp - 2 * ADD * math.tan(20 * DEG) * 0.5
        hi_w = wp + 2 * DED * math.tan(20 * DEG) * 0.5
        out.append(box([mx0 - 0.05, H_PITCH - ADD, z - lo_w / 2], [mx1 + 0.05, H_PITCH, z + lo_w / 2], f"fr_rack_tooth{k - RACK_K[0] + 1}a", f, "steel"))
        out.append(box([mx0 - 0.03, H_PITCH, z - hi_w / 2], [mx1 + 0.03, H_PITCH + DED + 0.02, z + hi_w / 2], f"fr_rack_tooth{k - RACK_K[0] + 1}b", f, "steel"))
    for i, (z0, z1) in enumerate(((cz - 3.15, cz - 1.95), (cz + 1.95, cz + 3.15), (HEAD_Z - 5.5, HEAD_Z - 4.3)), 1):
        out.append(box([X_BLANK - 1.0, SPINDLE_Y - 1.0, z0], [X_BLANK + 1.0, OVERARM_Y[0], z1], f"fr_spindle_bearing{i}", f, "iron"))
    for i, (x0, x1) in enumerate(((5.0, 5.8), (9.6, 10.4)), 1):
        out.append(box([x0, BED_TOP, ENTRY[1] - 1.6], [x1, ENTRY[0] + 1.6, ENTRY[1] + 1.6], f"fr_entry_bearing{i}", f, "iron"))
    # the idler's stud is held from the column's front wall, over the entry shaft
    out.append(box([9.0, IDLER[0] - 0.6, IDLER[1] - 0.5], [cx1 - 1.15, IDLER[0] + 0.6, IDLER[1] + 0.5], "fr_idler_arm", f, "iron"))
    out += rod("x", IDLER_C, RECT_X[1][0] - 0.2, 9.6, 0.3, "fr_idler_stud", f, "steel", k=2)
    for i, (x0, x1) in enumerate(((10.6, 11.4), (15.4, 16.2), (29.9, 30.7)), 1):
        out.append(box([x0, BED_TOP, FEED[1] - 0.9], [x1, FEED[0] + 0.9, FEED[1] + 0.9], f"fr_feed_bearing{i}", f, "iron"))
    px = CLUTCH_PIVOT[0]
    out.append(box([px - 0.4, BED_TOP, FEED[1] - 2.4], [px + 0.4, CLUTCH_PIVOT[1] + 0.4, FEED[1] - 1.6], "fr_clutch_post", f, "iron"))
    out += rod("z", (px, CLUTCH_PIVOT[1], 0.0), FEED[1] - 1.6, FEED[1] + 0.35, 0.2, "fr_clutch_pin", f, "steel", k=2)
    for i, (z0, z1) in enumerate(CAM_BEARINGS_Z, 1):
        in_pit = any(z0 < p[1] and z1 > p[0] for p in (pit1, pit2))
        out.append(box([CAM_X - 1.0, SILL_TOP if in_pit else BED_TOP, z0], [CAM_X + 1.0, CAM_Y + 1.0, z1], f"fr_cam_bearing{i}", f, "iron"))
    # the feed gearbox: a cast casing round the clutch, the worm and the wheel, its top a removable cover
    (gx0, gx1), (gy0, gy1), (gz0, gz1), t = CASE["x"], CASE["y"], CASE["z"], CASE["t"]
    top = gy1 - t
    out += [
        box([gx0, gy0, gz0], [gx1, top, gz0 + t], "fr_case_n", f, "iron"),
        box([gx0, gy0, gz1 - t], [gx1, top, gz1], "fr_case_s", f, "iron"),
        box([gx1 - 0.7, gy0, gz0 + t], [gx1, top, gz1 - t], "fr_case_e", f, "iron"),
    ]
    for tag, z0, z1 in (("i", DRUM_INDEX_Z[0] + 0.3, DRUM_INDEX_Z[1] - 0.2), ("f", DRUM_FEED_Z[0] + 0.3, DRUM_FEED_Z[1] - 0.2)):
        for side, (x0, x1) in (("w", (CAM_X - 1.6, CAM_X - 1.25)), ("e", (CAM_X + 1.25, CAM_X + 1.6))):
            out.append(box([x0, SLIDER_Y[0] - 0.05, z0], [x1, SLIDER_Y[1] + 0.15, z1], f"fr_guide_{tag}{side}", f, "steel"))
            xo = CAM_X - RAIL_R - 0.9 if side == "w" else CAM_X + RAIL_R + 0.4
            for e, (za, zb) in enumerate(((z0 + 0.2, z0 + 0.8), (z1 - 0.8, z1 - 0.2)), 1):
                out.append(box([xo, BED_TOP, za], [xo + 0.5, SLIDER_Y[1] + 0.15, zb], f"fr_guide_{tag}{side}_post{e}", f, "iron"))
                out.append(box([min(x0, xo), SLIDER_Y[1] + 0.15, za], [max(x1, xo + 0.5), SLIDER_Y[1] + 0.35, zb], f"fr_guide_{tag}{side}_cap{e}", f, "iron"))
    return out


def build_cover():
    """The removable covers (one part, shown or hidden together): the gearbox's top, slotted for the
    clutch's handle, and the door in the column's north side over the rectifier."""
    c = "cover"
    (gx0, gx1), (gy0, gy1), (gz0, gz1), t = CASE["x"], CASE["y"], CASE["z"], CASE["t"]
    px, _, pz = CLUTCH_PIVOT
    s0, s1 = px - 1.4, px + 1.4
    (cx0, cx1), (cz0, _) = COLUMN
    return [
        box([gx0, gy1 - t, gz0], [s0, gy1, gz1], "cover_top_w", c, "iron"),
        box([s1, gy1 - t, gz0], [gx1, gy1, gz1], "cover_top_e", c, "iron"),
        box([s0, gy1 - t, gz0], [s1, gy1, pz - 0.6], "cover_top_n", c, "iron"),
        box([s0, gy1 - t, pz + 0.6], [s1, gy1, gz1], "cover_top_s", c, "iron"),
        box([cx0 + 1.2, BED_TOP, cz0], [cx1 - 1.2, OVERARM_Y[0] - 0.8, cz0 + 0.6], "cover_colside", c, "iron"),
    ]


def build_clutch_lever():
    """The clutch's lever: pivoted on a pin over the feed shaft, a fork sitting in the sleeve's groove,
    a handle up the north end of the machine."""
    px, py, pz = CLUTCH_PIVOT
    out = [box([px - 0.2, FEED[0] + 1.17, pz - 0.5], [px + 0.2, py, pz + 0.5], "clutchlever_fork", "clutchlever", "steel")]
    out.append(box([px - 0.25, py - 0.3, pz - 0.2], [px + 0.25, py + 3.0, pz + 0.2], "clutchlever_handle", "clutchlever", "iron"))
    out.append(box([px - 0.35, py + 2.7, pz - 0.35], [px + 0.35, py + 3.4, pz + 0.35], "clutchlever_knob", "clutchlever", "brass"))
    return out


def build():
    els = build_drive() + build_cams() + build_sliders() + build_knee() + build_table() + build_arbor()
    els += build_masters() + build_blanks() + build_jonas() + build_index() + build_clutch_lever() + build_oiler() + build_frame()
    els += build_cover() + build_valve()
    return els


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def per_class(thin, thick):
    return {"thin": r6(thin), "thick": r6(thick)}


# The rig's work: teeth cut, W, a point on its own scale. W = 3.4 is 0.4 of the way through the fourth
# tooth's cycle; the cut ends at 12 teeth with the temporal gear master and 20 with the large one.
WORK = {"name": "teeth cut", "unit": "teeth", "step": 0.005, "end": {"thin": float(TEETH["thin"]), "thick": float(TEETH["thick"])}}
PATH = WORK                                  # the progress the gauges read
FOREVER = 1000.0
ONLY = {"thin": {"thin": 1.0, "thick": 0.0}, "thick": {"thin": 0.0, "thick": 1.0}}


def windows(r0, r1, f0=None, f1=None, classes=CLASSES, teeth_from=0):
    """One window per tooth of each class's cut: rising over r0..r1 of the tooth's cycle and, given
    f0..f1 (as long as the rise), falling over it. A class's windows carry a gain of 0 for the other
    class."""
    ease = r1 - r0
    if f0 is not None:
        assert abs((f1 - f0) - ease) < 1e-9, (r0, r1, f0, f1)
    out = []
    for cls in classes:
        for j in range(teeth_from, TEETH[cls]):
            to = FOREVER if f1 is None else j + f1
            out.append({"from": r6(j + r0), "to": r6(to), "ease": r6(ease), "gain": dict(ONLY[cls])})
    return out


def gauge(motion, axis, amount, wins, pivot=None, mode=None):
    d = {"type": "gauge", "motion": motion, "axis": axis}
    if pivot is not None:
        d["pivot"] = pivot
    d["amount"] = amount
    if mode:
        d["mode"] = mode
    else:
        d["windows"] = wins
    return d


def staircase(axis, pivot, factor):
    """The index, one driver per tooth: tooth j's driver turns by `factor` times the arbor's step as
    the housing is pushed round in the j-th index (each class's own timing), and holds."""
    out = []
    for j in range(TEETH["thick"]):
        wins = []
        for cls in CLASSES:
            if j < TEETH[cls]:
                s0, s1 = T_STEP[cls]
                wins.append({"from": r6(j + s0), "to": FOREVER, "ease": r6(s1 - s0), "gain": dict(ONLY[cls])})
        out.append(gauge("rotate", axis, per_class(-factor * STEP["thin"], -factor * STEP["thick"]), wins, pivot=pivot))
    return out


def roll_amount(factor=1.0):
    """The arbor's roll as the master runs along the rack (the stroke over the pitch radius), times factor."""
    return per_class(-factor * STROKE / PITCH_R["thin"], -factor * STROKE / PITCH_R["thick"])


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def lift_sign(pin, tip_r, tip_ang):
    """+1 if turning a pawl about its pin by +x lifts its tip away from the arbor's axis."""
    tip = ring_point(ARBOR, tip_r, tip_ang)
    v = (tip[1] - pin[1], tip[2] - pin[2])
    a = 0.1
    moved = (pin[1] + v[0] * math.cos(a) - v[1] * math.sin(a), pin[2] + v[0] * math.sin(a) + v[1] * math.cos(a))
    return 1.0 if math.hypot(moved[0] - ARBOR[1], moved[1] - ARBOR[2]) > tip_r else -1.0


CLUTCH_THROW = 0.25                          # the sleeve's dogs move in among the worm's
SCREW_TURNS = CLASS_DROP / 1.0               # the elevating screw's lead is 1 voxel
DETENT_OUT = DETENT_R - NOTCH_R + 0.05       # the plunger rides out of the notch onto the disc's rim
T_PAWL_DROP = t_at_lever(LEVER_SWING - PUSH["thick"] - 2.0 * DEG)   # with the shield in, the pawl drops off it here


def clutch_windows():
    return [{"from": -0.05, "to": float(TEETH[c]), "ease": 0.05, "gain": dict(ONLY[c])} for c in CLASSES]


def _rig_parts():
    arbor = pt(0.0, ARBOR_Y, Z_REST)
    cone = CONE_LOW[0] / CONE_HIGH[0]
    feed_w = windows(T_FWD[0], T_FWD[1], T_BACK[0], T_BACK[1])
    knee_w = windows(T_KNEE_UP[0], T_KNEE_UP[1], T_KNEE_DOWN[0], T_KNEE_DOWN[1])
    lever_w = windows(T_LEVER[0], T_LEVER[1], T_LEVER[1], T_LEVER[2])
    both = per_class
    ret = (T_LEVER[1], T_LEVER[1] + 0.005, T_LEVER[2] - 0.005, T_LEVER[2])
    pawl_w = windows(*ret) + windows(T_LEVER[0], T_LEVER[0] + 0.005, T_PAWL_DROP - 0.005, T_PAWL_DROP, classes=("thick",))
    check_w = []
    for cls in CLASSES:
        s0, s1 = T_STEP[cls]
        check_w += windows(s0, s0 + 0.004, s1 - 0.004, s1, classes=(cls,))
    clutch_swing = CLUTCH_THROW / (CLUTCH_PIVOT[1] - (FEED[0] + 1.17))
    (ox0, ox1), (oy0, oy1), (oz0, oz1) = OILER["x"], OILER["y"], OILER["z"]
    parts = [
        {"id": "entry", "match": ["entry_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *ENTRY), "ratio": 1.0}]},
        {"id": "rectb1", "match": ["rectb1_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *FEED), "ratio": -1.0}]},
        {"id": "idler", "match": ["idler_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *IDLER), "ratio": -1.0}]},
        {"id": "rectb2", "match": ["rectb2_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *FEED), "ratio": 1.0}]},
        {"id": "feedshaft", "match": ["feedshaft_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *FEED), "ratio": 1.0, "input": "travel"}]},
        {"id": "clutch", "match": ["clutch_*"], "requires": "feedscrew",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *FEED), "ratio": 1.0, "input": "travel"},
                     gauge("slide", "x", both(CLUTCH_THROW / B, CLUTCH_THROW / B), clutch_windows())]},
        {"id": "clutchlever", "match": ["clutchlever_*"], "requires": None,
         "drivers": [gauge("rotate", "z", both(clutch_swing, clutch_swing), clutch_windows(), pivot=pt(*CLUTCH_PIVOT))]},
        {"id": "worm", "match": ["worm_*"], "requires": "feedscrew",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *FEED), "ratio": r6(TAU * WORM_TEETH), "input": "work"}]},
        # the shaft and its wheel are the frame's; each drum is a stage, one Jonas eccentric gearbox each
        {"id": "camshaft", "match": ["cam_shaft*", "cam_wheel*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "z", "pivot": pt(CAM_X, CAM_Y, 0.0), "ratio": r6(TAU), "input": "work"}]},
        {"id": "camfeed", "match": ["cam_fdrum*", "cam_fgroove*"], "requires": "camfeed",
         "drivers": [{"type": "rotate", "axis": "z", "pivot": pt(CAM_X, CAM_Y, 0.0), "ratio": r6(TAU), "input": "work"}]},
        {"id": "camindex", "match": ["cam_idrum*", "cam_igroove*"], "requires": "camindex",
         "drivers": [{"type": "rotate", "axis": "z", "pivot": pt(CAM_X, CAM_Y, 0.0), "ratio": r6(TAU), "input": "work"}]},
        # the lift cam is a forged steel part of its own
        {"id": "liftcam", "match": ["cam_lift*"], "requires": "liftcam",
         "drivers": [{"type": "rotate", "axis": "z", "pivot": pt(CAM_X, CAM_Y, 0.0), "ratio": r6(TAU), "input": "work"}]},
        {"id": "feedslider", "match": ["feedslider_*"], "requires": None,
         "drivers": [gauge("slide", "z", both(STROKE / B, STROKE / B), feed_w)]},
        {"id": "indexslider", "match": ["indexslider_*"], "requires": None,
         "drivers": [gauge("slide", "z", both(PUSHER_GAP / B, PUSHER_GAP / B), windows(*T_GAP)),
                     gauge("slide", "z", both(PUSH_TRAVEL / B, PUSH_TRAVEL / B), lever_w)]},
        {"id": "pusher", "match": ["pusher_*"], "requires": None, "ride": "indexslider",
         "drivers": [gauge("slide", "y", both(0.0, -CLASS_DROP / B), None, mode="present")]},
        {"id": "knee", "match": ["knee_*"], "requires": None,
         "drivers": [gauge("slide", "y", both(LIFT / B, LIFT / B), knee_w),
                     gauge("slide", "y", both(0.0, -CLASS_DROP / B), None, mode="present")]},
        {"id": "screw", "match": ["screw_*"], "requires": None,
         "drivers": [gauge("slide", "y", both(LIFT / B, LIFT / B), knee_w),
                     gauge("rotate", "y", both(0.0, SCREW_TURNS * TAU), None, pivot=pt(SCREW_C[0], 0.0, SCREW_C[2]), mode="present")]},
        {"id": "table", "match": ["table_*"], "requires": None, "ride": "knee",
         "drivers": [gauge("slide", "z", both(STROKE / B, STROKE / B), feed_w)]},
        {"id": "arbor", "match": ["arbor_*"], "requires": None, "ride": "table",
         "drivers": [gauge("rotate", "x", roll_amount(), feed_w, pivot=arbor)] + staircase("x", arbor, 1.0)},
        {"id": "master", "match": ["master_*"], "requires": "master", "ride": "arbor", "drivers": []},
        {"id": "masterlarge", "match": ["mastl_*"], "requires": "masterlarge", "ride": "arbor", "drivers": []},
        {"id": "blanksmall", "match": ["blanks_*"], "requires": "blanksmall", "ride": "arbor", "drivers": []},
        {"id": "blanklarge", "match": ["blankl_*"], "requires": "blanklarge", "ride": "arbor", "drivers": []},
    ]
    for cls, prefix, req in (("thin", "gs", "blanksmall"), ("thick", "gl", "blanklarge")):
        for j in range(TEETH[cls]):
            a = gap_angle(cls, j)
            w = [{"from": r6(j + T_FWD[0]), "to": FOREVER, "ease": r6(T_FWD[1] - T_FWD[0]), "gain": dict(ONLY[cls])}]
            amt = lambda v: per_class(v if cls == "thin" else 0.0, v if cls == "thick" else 0.0)   # noqa: E731
            parts.append({"id": f"{prefix}{j + 1:02d}", "match": [f"{prefix}{j + 1:02d}_*"], "requires": req, "ride": "arbor",
                          "drivers": [gauge("slide", "y", amt(-math.cos(a) * SINK / B), w), gauge("slide", "z", amt(-math.sin(a) * SINK / B), w)]})
    parts.append({"id": "jcarrier", "match": ["jcarrier_*"], "requires": "head", "ride": "arbor", "drivers": []})
    for i, ang in enumerate(PLANET_ANG, 1):
        pc = ring_point(ARBOR, J_CARRIER, ang)
        piv = pt(0.0, pc[1], pc[2])
        # relative to the carrier: -4 times its turn while the ring is held (the roll), +2 while the sun is (the index)
        parts.append({"id": f"jplanet{i}", "match": [f"jplanet{i}_*"], "requires": "head", "ride": "arbor",
                      "drivers": [gauge("rotate", "x", roll_amount(-4.0), feed_w, pivot=piv)] + staircase("x", piv, 2.0)})
    lift = lift_sign(PAWL_PIN, PAWL_TIP_R, PAWL_TIP_ANG) * PAWL_LIFT
    clift = lift_sign(CHECK_PIN, CHECK_TIP_R, CHECK_ANG) * CHECK_LIFT
    parts += [
        {"id": "jring", "match": ["jring_*"], "requires": "head", "ride": "table",
         "drivers": staircase("x", arbor, J_RING_PER_ARBOR)},
        {"id": "sun", "match": ["sun_*"], "requires": "head", "ride": "table",
         "drivers": [gauge("rotate", "x", roll_amount(J_SUN_PER_ARBOR), feed_w, pivot=arbor)]},
        {"id": "shield", "match": ["shield_*"], "requires": "index", "ride": "table",
         "drivers": [gauge("rotate", "x", both(0.0, SHIELD_SWING), None, pivot=arbor, mode="present")]},
        {"id": "lever", "match": ["lever_*"], "requires": "index", "ride": "table",
         "drivers": [gauge("rotate", "x", both(-LEVER_SWING, -LEVER_SWING), lever_w, pivot=arbor)]},
        {"id": "pawl", "match": ["pawl_*"], "requires": "index", "ride": "lever",
         "drivers": [gauge("rotate", "x", both(lift, lift), pawl_w, pivot=pt(0.0, PAWL_PIN[1], PAWL_PIN[2]))]},
        {"id": "checkpawl", "match": ["checkpawl_*"], "requires": "index", "ride": "table",
         "drivers": [gauge("rotate", "x", both(clift, clift), check_w, pivot=pt(0.0, CHECK_PIN[1], CHECK_PIN[2]))]},
        {"id": "detent", "match": ["detent_*"], "requires": "head", "ride": "table",
         "drivers": [gauge("slide", "z", both(DETENT_OUT / B, DETENT_OUT / B), windows(*T_DETENT))]},
        {"id": "headshaft", "match": ["headshaft_*"], "requires": "spindle",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, SPINDLE_Y, HEAD_Z), "ratio": r6(cone)}]},
        {"id": "belt", "match": ["belt_*"], "requires": "spindle", "drivers": []},
        {"id": "spindle", "match": ["spindle_*"], "requires": "spindle",
         "drivers": [{"type": "rotate", "axis": "z", "pivot": pt(X_BLANK, SPINDLE_Y, 0.0), "ratio": r6(-cone)}]},
        {"id": "cutter", "match": ["cutter_*"], "requires": "cutter",
         "drivers": [{"type": "rotate", "axis": "z", "pivot": pt(X_BLANK, SPINDLE_Y, 0.0), "ratio": r6(-cone)}]},
        {"id": "oillevel", "match": ["oillevel_*"], "requires": None,
         "drivers": [{"type": "stretch", "axis": "y", "anchor": pt((ox0 + ox1) / 2, oy0, (oz0 + oz1) / 2),
                      "length": r6(OIL_EMPTY / B), "travel": r6((OIL_FULL - OIL_EMPTY) / B), "input": "oil"}]},
        {"id": "valve", "match": ["valve_*"], "requires": "oiler", "drivers": []},
        # the plunger pumps once a tooth, in and out of the body
        {"id": "valveplunger", "match": ["valveplunger_*"], "requires": "oiler",
         "drivers": [{"type": "slide", "axis": "x", "amplitude": r6(PLUNGER_STROKE / B), "ratio": r6(TAU), "input": "work"}]},
        {"id": "cover", "match": ["cover_*"], "requires": "cover", "drivers": []},

        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses
REST = (0.0, 0.0, 0.0, 0, 0.0, 0.0)          # (theta, travel, T, k, p, oil): no master, the cup empty; the authored pose


def inputs_of(pose):
    th, ps, T, k, p = pose[:5]
    oil = pose[5] if len(pose) > 5 else 0.0
    return {"theta": th, "travel": ps, "work": T, "size": k, "presence": p, "oil": oil}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path or PATH)


def t_end(k):
    return work_end(WORK, k)


def pose_at(k, T, theta=None, oil=0.6):
    """A pose of a cut in progress: master k fitted, at T; the axle at TURNS_PER_TOOTH turns a tooth."""
    th = TAU * TURNS_PER_TOOTH * T if theta is None else theta
    return (th, abs(th), T, k, 1.0, oil)


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("output", "chips", "drip")


def make_rig(parts):
    progress_of({"work": WORK})
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the operator's "
                    "(east) side at ground level, north, nearest the player who placed it; the column is at the back "
                    "(west). work is the cut's progress, W, in teeth (0 to 12 with the temporal gear master, k = 1; "
                    "0 to 20 with the large one, k = 2); gauge windows are placed in teeth. The sight-feed cup's level "
                    "follows the rig's oil input (the MachineOil tank's fill, 0..1). See the gear cutter's README for "
                    "the schema.",

        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "infeedSide": "north",
        "outputSide": "south",
        "output": {"pos": pt(29.0, BED_TOP, 29.0)},

        "chips": {"pos": pt(X_BLANK, H_PITCH, Z_CUT)},
        "drip": {"pos": pt(DRIP_X, DRIP_Y, Z_CUT)},

        "work": dict(WORK),
        "cut": {"turnsPerTooth": TURNS_PER_TOOTH,
                "masters": {"thin": "game:gear-temporal", "thick": "game:largegear-temporal"},
                "_comment": "turnsPerTooth: axle turns per tooth as the gears are drawn (rectifier 1:1, single-start worm, "
                            "12-tooth worm wheel); the camshaft turns once per tooth. masters: the item each class's master "
                            "is (k = 1 the temporal gear, cutting 12 teeth; k = 2 the large temporal gear, cutting 20)."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0]: elements, pivots and anchors,
    cells and anchors. Gauge windows are in teeth, so they stay as they are."""
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
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts, sp):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig (every
    reader rebuilds them the same way); a cell nothing reaches is hollow; then the lids."""
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
    poses = [REST, pose_at(1, 0.3), pose_at(1, 5.83, oil=1.0), pose_at(2, 0.55), pose_at(2, 17.9, oil=0.2),
             (1.3, 7.0, 11.2, 1, 0.6, 0.3), (0.4, 2.0, 19.95, 2, 1.0, 1.0)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose), sp), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    span = [(min(c[k] for c in cells), max(c[k] for c in cells)) for k in range(3)]
    print(f"shipped: moved by {db_str(d)} blocks, worst posed difference {worst:.2e} voxels; cells x {span[0]}, y {span[1]}, z {span[2]}; "
          f"power {ship['powerCell']} {ship['powerFace']}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


def db_str(d):
    return [v / B for v in d]


# ---------------------------------------------------------------- reference poses
def reference_poses():
    """theta in {0, 1.1, -2.3, 2.9} with psi; for each class, T over the cycle's edges in a few teeth
    (every window edge and mid-ramp of teeth 0, 1, 7 and the last), T_end, and p in {1, 0.4}; no
    master; the oil at 0, 0.35 and 1 in turn."""
    out = []
    for i, th in enumerate((0.0, 1.1, -2.3, 2.9)):
        for extra in (0.0, 7.3):
            out.append((th, round(abs(th) + extra, 6), 0.0, 0, 0.0, (0.0, 0.35, 1.0)[i % 3]))
    edges = (0.05, 0.105, 0.24, 0.39, 0.54, 0.73, 0.79, T_STEP["thin"][0] + 0.01, T_STEP["thick"][0] + 0.01, T_PAWL_DROP - 0.002,
             0.865, 0.935, 0.99)
    for k, cls in ((1, "thin"), (2, "thick")):
        ts = {0.0, float(TEETH[cls])}
        for j, es in ((0, edges), (7, edges[::2]), (TEETH[cls] - 1, edges[1::2])):
            for e in es:
                ts.add(round(j + e, 6))
        for i, T in enumerate(sorted(ts)):
            th = (0.0, 1.1, -2.3, 2.9)[i % 4]
            for p in ((1.0,) if i % 5 else (1.0, 0.4)):
                out.append((th, round(abs(th) + 0.37 * i, 6), T, k, p, (1.0, 0.35, 0.0)[i % 3]))
    return out


def reference_json(ship_parts, sp):
    poses = []
    for pose in reference_poses():
        th, ps, T, k, p, oil = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose), sp)) for q in ship_parts}
        poses.append({"theta": th, "travel": ps, "work": T, "size": k, "presence": p, "oil": oil, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped gearcutter-rig.json's parts and work: each part's matrix "
                        "as 3 rows of 4 (block units) at each pose (T in teeth). The site's and the mod's tests check their rig "
                        "maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- the forged parts' item shapes
# Each stage's item looks as its part does in the machine: the rig parts it draws (README "Fitted parts",
# without the belt, which is the spindle stage's but no part of the forging), their elements as they are
# at rest, centred on the item box's floor (x and z on 8, the bottom at y 0) and scaled down uniformly
# when they would not fit in 16 voxels. The scales are fixed here, so an item does not change size
# because a part moved; validate checks each item fits at its scale.
ITEM_PARTS = {
    "spindle": ("headshaft", "spindle"),               # the head shaft with the upper cone and its mitre, the spindle with its own
    "feedscrew": ("worm", "clutch"),                   # the worm and the clutch sleeve
    "liftcam": ("liftcam",),                           # the lift cam on its hub
    "index": ("lever", "pawl", "checkpawl", "shield"),  # the lever with its roller, the pawls and the shield
    "kit": ("cutter",),                                # the formed cutter
}
ITEM_SCALE = {"spindle": 0.74, "feedscrew": 1.0, "liftcam": 1.0, "index": 1.0, "kit": 1.0}   # the spindle is 21.5 long


def item_elements(els, parts, item):
    """The item's elements: its parts' at rest, every face kept that the machine draws (an element with
    none left is dropped), moved and scaled into the item box; the UVs scale with the faces."""
    src = [posed(el, pm(parts, el.part, REST)) for el in els if el.part in ITEM_PARTS[item] and el.faces]
    cs = [q for el in src for q in el.corners()]
    lo = [min(q[i] for q in cs) for i in range(3)]
    hi = [max(q[i] for q in cs) for i in range(3)]
    sc = ITEM_SCALE[item]
    anchor = ((lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2)
    out = []
    for el in src:
        e = el.clone()
        e.c = [8.0 + (el.c[0] - anchor[0]) * sc, (el.c[1] - anchor[1]) * sc, 8.0 + (el.c[2] - anchor[2]) * sc]
        e.size = [v * sc for v in el.size]
        for f in e.faces.values():
            f["uv"] = [v * sc for v in f["uv"]]
        out.append(e)
    return out


def item_shape_json(els, item):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}: the gear cutter's {item} item, its parts' elements from gearcutter.json at rest "
             f"(scale {ITEM_SCALE[item]}). Every element was made for the Seraph Horizons mod.", TEXTURES, tex_size=TEX)


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod; the masters use the game's "
             "temporal gear texture. Keep element names when editing: the rig finds its parts by them.", TEXTURES, tex_size=TEX)


def coplanar_poses():
    return (REST, pose_at(1, 0.25), pose_at(2, 0.25), pose_at(1, 0.9), pose_at(2, 0.93))


def fix_coplanar(els, parts):
    """The z-fighting fix over the poses, with the parts not fitted at a pose (the other master's set-up,
    or both at rest) moved well away: faces of parts never seen together are not worth moving."""
    from validate_gearcutter import present
    away = [[1.0, 0.0, 0.0, 1000.0], [0.0, 1.0, 0.0, 0.0], [0.0, 0.0, 1.0, 0.0]]

    def posed_fn(es, pose):
        return [posed(el, pm(parts, el.part, pose) if present(el.part, pose[3]) else away) for el in es]
    return fix_coplanar_posed(els, posed_fn, coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the gear cutter's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_gearcutter
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
    ok = validate_gearcutter.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "gearcutter.json", args.out / "gearcutter_frame.json", args.out / "gearcutter-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "gearcutter.json", SHAPE_DIR / "gearcutter_frame.json", RIG_DIR / "gearcutter-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts, ship["work"])
    ok = validate_gearcutter.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship), reference_dumps(reference_json(ship_parts, ship["work"])))
    items = {item: item_elements(els, parts, item) for item in ITEM_PARTS}
    ok = validate_gearcutter.check_items(sys.modules[__name__], items) and ok
    item_dir = args.out / "item" if args.out else ITEM_DIR
    outs += tuple(item_dir / f"{item}.json" for item in items)
    texts += tuple(shape_dumps(item_shape_json(es, item)) for item, es in items.items())

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
