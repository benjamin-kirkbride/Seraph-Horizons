#!/usr/bin/env python3
"""Generate the press brake's shapes, rig and reference poses.

The press brake is a hand-worked leaf brake (a cornice brake) of the early-to-mid 1800s, in oak with
iron wearing edges and iron clamping screws: an oak bed with an iron folding edge along its near end;
an oak clamping bar with an iron nose, brought down on the sheet by two iron screws threaded through
nuts in iron gallows on the oak end cheeks; and an oak folding leaf with an iron edge, hinged on pins in
the cheeks at the folding edge and swung up by its bail handle. A lead or copper plate goes on in two
halves lying end to end along the edge, and both are folded together, twice, into two open U sections:
clamp, fold the near flange up, unclamp, pull the sheet one panel towards the operator, clamp, fold the
middle panel up (the first flange swings back over the bar's low nose), unclamp, and slide the two U
sections off the bar onto the leaf. Everything is built here from plain boxes; no other mod's model is
used.

It writes, deterministically,

    pressbrake.json         the whole machine, every moving part   (assets/.../shapes/block/)
    pressbrake_frame.json   the static frame only (block and item)  (assets/.../shapes/block/)
    pressbrake-rig.json     cells, anchors and the part rig         (assets/.../config/)
    rig-reference.json      every part's matrix at a grid of poses  (tests/PressBrake/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_pressbrake.py) and
exits non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from
the machine box's north-west-bottom corner (the "build frame"); the controller cell is the build
frame's [0,0,0], so the shipped files are the build frame divided by 16. The brake runs along z: the
leaf at the north end (the controller, nearest the player), the bed extending south.

The rig's inputs, as this machine uses them (README "Rig schema"):

    theta  the lever's work, the hold-to-work clock: read by a ratio-0 rotate on the lever, it moves nothing
    W      the rig's work, the fold cycle of one plate, 0..1
    k      the plate's metal: 0 none, 1 lead (thin), 2 copper (thick)
    p      its presence, 0..1, eased as the plate goes on and after it is delivered
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
REFERENCE_OUT = MOD / "tests" / "PressBrake" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/PressBrake/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
DEG = math.pi / 180

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 1, 1, 2          # one wide, one high, two long (z)
ORIGIN_CELL = (0, 0, 0)                      # the controller: the leaf end, nearest the player who placed it

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "iron": "game:block/metal/plate/iron",
    "edge": "game:block/metal/plate/iron",      # the wearing edges: the renderer sets it to the fitted plate's metal
    "screw": "game:block/metal/plate/iron",     # the clamp screws: the renderer sets it to the fitted rods' metal
    "lead": "game:block/metal/sheet-plain/lead1",
    "copper": "game:block/metal/sheet-plain/copper1",
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the folding edge and the sheet
YB = 4.6                                     # the bed's top and the leaf's face at rest
EZ = 10.0                                    # the folding edge: the leaf's hinge axis runs along x at (YB, EZ)
HINGE = (YB, EZ)
T = 1.0                                      # the sheet's thickness: the open section item's walls
S = 8.0                                      # a panel: the U's outside, the game's chute section is 8 across
HALVES = ((1.45, 7.9), (8.1, 14.55))         # the plate's two halves, end to end along the edge (x)
Z_A0 = EZ - S                                # the sheet laid on: flange A over the leaf, M and B on the bed
SHIFT = S                                    # between the folds the sheet is pulled one panel north
THROW = {"thin": 95.0, "thick": 100.0}       # the leaf's throw (degrees): over-bent for the spring-back
SET = 90.0                                   # where a flange springs back to

# ---------------------------------------------------------------- the frame
CHEEK = ((0.0, 1.0), (15.0, 16.0))           # the oak end cheeks (x)
CHEEK_Z = (9.0, 31.6)
LEG, RAIL = 2.0, 1.5                         # the end frames' legs (z) and top rail (y)
BED_X = (1.0, 15.0)
BED_Y0 = YB - 3.0
BED_Z1 = 31.6
EDGE_X = (1.4, 14.6)                         # the folding edge's iron, the bed's front piece and the leaf
BOSS_Y = (YB - 1.0, YB + 1.0)                          # the hinge bearings on the cheeks' fronts
BOSS_Z = (CHEEK_Z[0], EZ + 0.45)
PIN_R = 0.3
LEAF_Y0 = YB - 2.0                           # the leaf is two voxels of oak under its face
LEAF_Z0 = EZ - S                             # it is as wide as a panel
UP_FRONT_Z = (9.3, 10.3)                     # the gallows' uprights, either side of the bar's ends
UP_BACK_Z = (16.6, 17.6)
UP_X = (0.05, 0.95)                           # west; mirrored east
BRIDGE_Y = (YB + 6.5, YB + 7.5)

# ---------------------------------------------------------------- the clamping bar and its screws
BAR_X = (0.2, 15.8)
BAR_Z = (11.45, 16.4)                        # its iron nose at the front; the oak body set back
BAR_BODY_Z0 = 11.95                          # where the oak starts above the nose: clear of a flange over-bent to 100 degrees
BAR_H = 3.5
NOSE_H = 1.0
LIFT = 1.0                                   # unclamped, the bar stands this far over the sheet
SCREW_X = 0.75                               # west; mirrored east
SCREW_Z = (BAR_Z[0] + BAR_Z[1]) / 2
SCREW_R = 0.3
CUP_Y = 0.4                                  # the iron cup on the bar's top that the screw's tip turns in
SCREW_L = 4.5
TOMMY = 0.72                                 # the tommy bar's half length
TOMMY_H = 0.35
PITCH = 0.25                                 # the screws' thread, voxels a turn
EXTRA_TURN = {"thin": 0.0, "thick": 0.25}    # copper is screwed down a quarter turn harder each clamp
NUT_Y = (YB + 6.55, YB + 7.55)

# ---------------------------------------------------------------- the lever (the leaf's bail handle)
LEVER_R = 9.2                                # the hand bar's axis from the hinge
LEVER_REST = -20.0                           # degrees above the horizontal, pointing north, at rest
HANDLE_R = 0.6
HANDLE_X = (2.4, 13.6)
ARMS_X = ((2.6, 3.2), (12.8, 13.4))

# ---------------------------------------------------------------- the cycle (t = W, one plate)
T_CLAMP1, T_UNCLAMP1 = (0.04, 0.12), (0.38, 0.46)
T_FOLD1 = (0.13, 0.25, 0.37)                 # rise from, top, back down
T_SHIFT = (0.46, 0.54)
T_CLAMP2, T_UNCLAMP2 = (0.54, 0.62), (0.88, 0.96)
T_FOLD2 = (0.63, 0.75, 0.87)
T_OFF = (0.96, 1.00)
LEVER_TURNS = {"thin": 6.0, "thick": 9.0}    # the pace: lever turns (theta / 2 pi) a plate

METALS = (("thin", "l", "lead", "platelead"), ("thick", "c", "copper", "platecopper"))


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


AX = {"x": 0, "y": 1, "z": 2}
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}


def disc(axis, c, a0, a1, r, name, part, tex, k=4):
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
        if i:
            rotate([el], axis, 180.0 * i / k, c)
        out.append(el)
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


def mirror_x(x0, x1):
    return (16.0 - x1, 16.0 - x0)


def sides():
    """The west and east (name suffix, x mapper) pairs: the frame is symmetric about x 8."""
    return (("w", lambda a, b: (a, b)), ("e", mirror_x))


def lever_point(r, deg):
    """(y, z) at radius r from the hinge, deg above the horizontal towards the north."""
    return (YB + r * math.sin(deg * DEG), EZ - r * math.cos(deg * DEG))


# ---------------------------------------------------------------- builders
def build_frame():
    """The oak cheeks and bed, the iron hinge bearings and pins, the gallows with their nuts, a stretcher."""
    f = "frame"
    out = []
    for s, mx in sides():
        cx = mx(*CHEEK[0])
        # an end frame: two legs, a top rail under the bed's edge, a bottom rail
        out.append(box([cx[0], 0.0, CHEEK_Z[0]], [cx[1], YB - RAIL, CHEEK_Z[0] + LEG], f"fr_legn_{s}", f, "oak"))
        out.append(box([cx[0], 0.0, CHEEK_Z[1] - LEG], [cx[1], YB - RAIL, CHEEK_Z[1]], f"fr_legs_{s}", f, "oak"))
        out.append(box([cx[0], YB - RAIL, CHEEK_Z[0]], [cx[1], YB, CHEEK_Z[1]], f"fr_rail_{s}", f, "oak"))
        out.append(box([cx[0], 0.8, CHEEK_Z[0] + LEG], [cx[1], 2.0, CHEEK_Z[1] - LEG], f"fr_rail_{s}_low", f, "oak"))
        out.append(box([cx[0], BOSS_Y[0], BOSS_Z[0]], [cx[1], BOSS_Y[1], BOSS_Z[1]], f"fr_boss_{s}", f, "iron"))
        px = mx(0.25, EDGE_X[0])
        out += disc("x", (0.0, YB, EZ), px[0], px[1], PIN_R, f"fr_pin_{s}", f, "iron")
        ux = mx(*UP_X)
        out.append(box([ux[0], BOSS_Y[1], UP_FRONT_Z[0]], [ux[1], BRIDGE_Y[0], UP_FRONT_Z[1]], f"fr_upfront_{s}", f, "iron"))
        out.append(box([ux[0], YB, UP_BACK_Z[0]], [ux[1], BRIDGE_Y[0], UP_BACK_Z[1]], f"fr_upback_{s}", f, "iron"))
        out.append(box([ux[0], BRIDGE_Y[0], UP_FRONT_Z[0]], [ux[1], BRIDGE_Y[1], SCREW_Z - 0.7], f"fr_bridge_{s}_n", f, "iron"))
        out.append(box([ux[0], BRIDGE_Y[0], SCREW_Z + 0.7], [ux[1], BRIDGE_Y[1], UP_BACK_Z[1]], f"fr_bridge_{s}_s", f, "iron"))
        nx = mx(0.2, 1.3)
        sx = SCREW_X if s == "w" else 16.0 - SCREW_X
        # the nut: four bars round the screw, so the screw shows through it
        out.append(box([nx[0], NUT_Y[0], SCREW_Z - 0.7], [nx[1], NUT_Y[1], SCREW_Z - SCREW_R - 0.02], f"fr_nut_{s}_n", f, "iron"))
        out.append(box([nx[0], NUT_Y[0], SCREW_Z + SCREW_R + 0.02], [nx[1], NUT_Y[1], SCREW_Z + 0.7], f"fr_nut_{s}_s", f, "iron"))
        if s == "w":
            out.append(box([nx[0], NUT_Y[0], SCREW_Z - SCREW_R - 0.02], [sx - SCREW_R - 0.02, NUT_Y[1], SCREW_Z + SCREW_R + 0.02], f"fr_nut_{s}_o", f, "iron"))
            out.append(box([sx + SCREW_R + 0.02, NUT_Y[0], SCREW_Z - SCREW_R - 0.02], [nx[1], NUT_Y[1], SCREW_Z + SCREW_R + 0.02], f"fr_nut_{s}_i", f, "iron"))
        else:
            out.append(box([nx[0], NUT_Y[0], SCREW_Z - SCREW_R - 0.02], [sx - SCREW_R - 0.02, NUT_Y[1], SCREW_Z + SCREW_R + 0.02], f"fr_nut_{s}_i", f, "iron"))
            out.append(box([sx + SCREW_R + 0.02, NUT_Y[0], SCREW_Z - SCREW_R - 0.02], [nx[1], NUT_Y[1], SCREW_Z + SCREW_R + 0.02], f"fr_nut_{s}_o", f, "iron"))
    # the bed: an oak beam, its front piece under the iron folding edge (a rebate when no edge is fitted)
    out.append(box([BED_X[0], BED_Y0, EZ + 1.0], [BED_X[1], YB, BED_Z1], "fr_bed", f, "oak"))
    out.append(box([EDGE_X[0], BED_Y0, EZ], [EDGE_X[1], YB - 1.0, EZ + 1.0], "fr_bed_front", f, "oak"))
    out.append(box([BED_X[0], 0.6, 27.5], [BED_X[1], 2.2, 29.5], "fr_stretcher", f, "oak"))
    return out


def build_edges():
    """The iron wearing edges (the `edge` stage): the bed's folding edge here; the leaf's and the bar's
    with their parts."""
    return [box([EDGE_X[0], YB - 1.0, EZ], [EDGE_X[1], YB, EZ + 1.0], "bededge_strip", "bededge", "edge")]


def build_leaf():
    """The folding leaf: oak, as wide as a panel, its iron edge along the hinge (a rebate in the oak),
    iron knuckles at its ends round the pins; and its bail handle (the lever): two iron arms under the
    leaf and an oak hand bar."""
    out = [box([EDGE_X[0], LEAF_Y0, LEAF_Z0], [EDGE_X[1], YB, EZ - 1.0], "leaf_body", "leaf", "oak"),
           box([EDGE_X[0], LEAF_Y0, EZ - 1.0], [EDGE_X[1], YB - 1.0, EZ - 0.02], "leaf_heel", "leaf", "oak"),
           box([EDGE_X[0], YB - 1.0, EZ - 1.0], [EDGE_X[1], YB, EZ - 0.02], "leafedge_strip", "leafedge", "edge")]
    for s, mx in sides():
        kx = mx(1.0, EDGE_X[0])
        out.append(box([kx[0], YB - 0.65, EZ - 0.65], [kx[1], YB - 0.0, EZ + 0.6], f"leaf_knuckle_{s}", "leaf", "iron"))
        out.append(box([kx[0], LEAF_Y0 + 0.4, EZ - 3.0], [kx[1], YB - 0.65, EZ - 0.2], f"leaf_strap_{s}", "leaf", "iron"))
    hy, hz = lever_point(LEVER_R, LEVER_REST)
    for i, (a0, a1) in enumerate(ARMS_X):
        out.append(box([a0, LEAF_Y0 - 0.4, LEAF_Z0 + 1.0], [a1, LEAF_Y0, EZ - 2.0], f"lever_strap{i + 1}", "lever", "iron"))
        a = (0.0, LEAF_Y0 - 0.2, LEAF_Z0 + 1.3)
        b = (0.0, hy, hz)
        el = strut(a, b, 0.6, a1 - a0, f"lever_arm{i + 1}", "lever", "iron")
        el.c[0] = (a0 + a1) / 2
        out.append(el)
    out += disc("x", (0.0, hy, hz), HANDLE_X[0], HANDLE_X[1], HANDLE_R, "lever_handle", "lever", "oak")
    return out


def build_bar():
    """The clamping bar, at rest lying on the bed: the iron nose (the `edge` stage), the oak body set
    back above it, iron cups for the screws' tips at its ends."""
    out = [box([BAR_X[0], YB, BAR_Z[0]], [BAR_X[1], YB + NOSE_H, BAR_BODY_Z0 + 0.4], "baredge_nose", "baredge", "edge"),
           box([BAR_X[0], YB, BAR_BODY_Z0 + 0.4], [BAR_X[1], YB + NOSE_H, BAR_Z[1]], "bar_sole", "bar", "oak"),
           box([BAR_X[0], YB + NOSE_H, BAR_BODY_Z0], [BAR_X[1], YB + BAR_H, BAR_Z[1]], "bar_body", "bar", "oak")]
    for s, mx in sides():
        cx = mx(0.3, 1.2)
        out.append(box([cx[0], YB + BAR_H, SCREW_Z - 0.55], [cx[1], YB + BAR_H + CUP_Y, SCREW_Z + 0.55], f"bar_cup_{s}", "bar", "iron"))
    return out


def screw_x(s):
    return SCREW_X if s == "w" else 16.0 - SCREW_X


def build_screws():
    """The two clamp screws, at rest with the bar on the bed: a threaded rod through the gallows' nut,
    its tip in the bar's cup, a tommy bar across its head."""
    out = []
    y0 = YB + BAR_H + CUP_Y
    for s, _ in sides():
        x = screw_x(s)
        pid = f"screw{s}"
        out += disc("y", (x, 0.0, SCREW_Z), y0, y0 + SCREW_L, SCREW_R, f"{pid}_rod", pid, "screw")
        top = y0 + SCREW_L
        out.append(box([x - TOMMY, top - TOMMY_H, SCREW_Z - 0.18], [x + TOMMY, top, SCREW_Z + 0.18], f"{pid}_tommy", pid, "screw"))
        out.append(box([x - 0.42, top - TOMMY_H - 0.3, SCREW_Z - 0.42], [x + 0.42, top - TOMMY_H, SCREW_Z + 0.42], f"{pid}_head", pid, "screw"))
    return out


def build_work():
    """The plate in two halves, each three panels (A over the leaf, M and B on the bed), lead and copper."""
    out = []
    for _cls, pre, tex, _req in METALS:
        for j, (x0, x1) in enumerate(HALVES, 1):
            for n, (z0, z1) in zip("amb", ((Z_A0, EZ), (EZ, EZ + S), (EZ + S, EZ + 2 * S))):
                out.append(box([x0, YB, z0], [x1, YB + T, z1], f"{pre}{n}_{j}", f"{pre}{n}", tex))
    return out


def build():
    return build_leaf() + build_bar() + build_screws() + build_edges() + build_work() + build_frame()


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def per_class(thin, thick=None):
    return {"thin": r6(thin), "thick": r6(thin if thick is None else thick)}


WORK = {"name": "fold", "unit": "plates", "step": 0.005, "end": {"thin": 1.0, "thick": 1.0}}
PATH = WORK
FOREVER = 1000.0


def win(t0, t1, t2=None, t3=None):
    """A window rising over t0..t1 and, given t2..t3 (as long), falling over it; else open to the end."""
    ease = t1 - t0
    if t2 is not None:
        assert abs((t3 - t2) - ease) < 1e-9, (t0, t1, t2, t3)
    return {"from": r6(t0), "to": r6(FOREVER if t3 is None else t3), "ease": r6(ease)}


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


def hinge_pt():
    return pt(0.0, YB, EZ)


def fold(t):
    """The leaf's fold, rising over t[0]..t[1] and falling over t[1]..t[2]."""
    return gauge("rotate", "x", per_class(THROW["thin"] * DEG, THROW["thick"] * DEG), [win(t[0], t[1], t[1], t[2])], pivot=hinge_pt())


def flange(t):
    """A panel carried by the leaf: up with it, then it springs back to SET as the leaf leaves it. The
    spring-back's window is copper's; lead's gain makes it as long as lead's leaf takes to fall to SET."""
    up = gauge("rotate", "x", per_class(THROW["thin"] * DEG, THROW["thick"] * DEG), [win(t[0], t[1])], pivot=hinge_pt())
    fall = t[2] - t[1]
    span = {c: fall * (THROW[c] - SET) / THROW[c] for c in ("thin", "thick")}
    back = win(t[1], t[1] + span["thick"])
    back["gain"] = per_class(span["thick"] / span["thin"], 1.0)
    sb = gauge("rotate", "x", per_class(-(THROW["thin"] - SET) * DEG, -(THROW["thick"] - SET) * DEG), [back], pivot=hinge_pt())
    return [up, sb]


def slide_z(dist, t):
    return gauge("slide", "z", per_class(dist / B), [win(*t)])


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def clamp_windows():
    return [win(T_CLAMP1[0], T_CLAMP1[1], *T_UNCLAMP1), win(T_CLAMP2[0], T_CLAMP2[1], *T_UNCLAMP2)]


def _rig_parts():
    up = T + LIFT                            # the bar's rise off the bed as a plate goes on
    parts = [
        {"id": "leaf", "match": ["leaf_*"], "requires": None, "drivers": [fold(T_FOLD1), fold(T_FOLD2)]},
        {"id": "leafedge", "match": ["leafedge_*"], "requires": "edge", "ride": "leaf", "drivers": []},
        # theta is the lever's work, the hold-to-work clock: a ratio-0 rotate carries it and moves nothing
        {"id": "lever", "match": ["lever_*"], "requires": None, "ride": "leaf",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": hinge_pt(), "ratio": 0.0}]},
        {"id": "bar", "match": ["bar_*"], "requires": None,
         "drivers": [gauge("slide", "y", per_class(up / B), mode="present"),
                     gauge("slide", "y", per_class(-LIFT / B), clamp_windows())]},
        {"id": "baredge", "match": ["baredge_*"], "requires": "edge", "ride": "bar", "drivers": []},
    ]
    for s, _ in sides():
        pv = pt(screw_x(s), 0.0, SCREW_Z)
        # a right-handed thread: turned clockwise from above (negative about y) it goes down
        down = {c: LIFT + EXTRA_TURN[c] * PITCH for c in ("thin", "thick")}
        parts.append({"id": f"screw{s}", "match": [f"screw{s}_*"], "requires": "screws", "drivers": [
            gauge("rotate", "y", per_class(TAU * up / PITCH), mode="present", pivot=pv),
            gauge("rotate", "y", per_class(-TAU * down["thin"] / PITCH, -TAU * down["thick"] / PITCH), clamp_windows(), pivot=pv),
            gauge("slide", "y", per_class(up / B), mode="present"),
            gauge("slide", "y", per_class(-down["thin"] / B, -down["thick"] / B), clamp_windows())]})
    parts.append({"id": "bededge", "match": ["bededge_*"], "requires": "edge", "drivers": []})
    for _cls, pre, _tex, req in METALS:
        a = flange(T_FOLD1) + [slide_z(-SHIFT, T_SHIFT)] + flange(T_FOLD2) + [slide_z(-S, T_OFF)]
        m = [slide_z(-SHIFT, T_SHIFT)] + flange(T_FOLD2) + [slide_z(-S, T_OFF)]
        b = [slide_z(-SHIFT, T_SHIFT), slide_z(-S, T_OFF)]
        for n, drv in (("a", a), ("m", m), ("b", b)):
            parts.append({"id": f"{pre}{n}", "match": [f"{pre}{n}_*"], "requires": req, "drivers": drv})
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []})
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses
REST = (0.0, 0.0, 0, 0.0)                     # (theta, W, k, p): no plate; the authored pose


def inputs_of(pose):
    th, W, k, p = pose
    return {"theta": th, "work": W, "size": k, "presence": p}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path or PATH)


def pose_at(k, W, theta=None, p=1.0):
    """A plate of metal k at W, the lever at its pace."""
    th = TAU * LEVER_TURNS[("thin", "thick")[k - 1]] * W if theta is None else theta
    return (th, W, k, p)


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("output", "plate", "edge")


def make_rig(parts):
    progress_of({"work": WORK})
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the leaf end, nearest the "
                    "player who placed it; the bed runs south. A hand machine: no power cell. work is the fold cycle of one "
                    "plate, W 0..1; k is the plate's metal (1 lead, 2 copper); gauge windows are placed in plates. theta is "
                    "the lever's work (the hold-to-work clock), carried by a ratio-0 rotate on the lever. See the press "
                    "brake's README for the schema.",
        "cells": [],
        "infeedSide": "south",
        "outputSide": "north",
        "output": {"pos": pt(8.0, YB + S / 2, Z_A0)},
        "plate": {"pos": pt(8.0, YB + T, Z_A0 + 1.5 * S)},
        "edge": {"pos": pt(8.0, YB, EZ)},
        "work": dict(WORK),
        "fold": {"leverTurnsPerPlate": per_class(LEVER_TURNS["thin"], LEVER_TURNS["thick"]),
                 "throwDegrees": per_class(THROW["thin"], THROW["thick"]),
                 "plates": {"thin": "game:metalplate-lead", "thick": "game:metalplate-copper"},
                 "sections": {"thin": "seraphhorizons:chutesectionopen-lead", "thick": "seraphhorizons:chutesectionopen-copper"},
                 "sectionsPerPlate": len(HALVES),
                 "_comment": "leverTurnsPerPlate: the pace, lever turns (theta / 2 pi) a plate; copper half as much again "
                             f"(screwed down a quarter turn harder each clamp, thrown {THROW['thick']:g} degrees against lead's "
                             f"{THROW['thin']:g}). throwDegrees: the leaf's throw as drawn. plates: each class's work. sections: "
                             "what a plate makes, sectionsPerPlate of them."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: the build frame's
    corner is the controller's). Gauge windows are in plates, so they stay as they are."""
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
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts, sp):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig; then the lids."""
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
    poses = [REST, pose_at(1, 0.2), pose_at(2, 0.5), pose_at(1, 0.7), pose_at(2, 0.99), (1.3, 0.3, 1, 0.6)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose), sp), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells {cells}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
REF_EDGES = (0.0, 0.03, 0.08, 0.12, 0.16, 0.22, 0.25, 0.255, 0.262, 0.3, 0.37, 0.42, 0.5, 0.58, 0.62, 0.66, 0.72,
             0.75, 0.756, 0.8, 0.87, 0.92, 0.97, 0.99, 1.0)


def reference_poses():
    """theta in {0, 1.1, -2.3, 2.9} with no plate; for each metal, W over the cycle's edges with p 1, and
    every fifth also at p 0.4."""
    out = [(th, 0.0, 0, 0.0) for th in (0.0, 1.1, -2.3, 2.9)]
    for k in (1, 2):
        for i, W in enumerate(REF_EDGES):
            th = (0.0, 1.1, -2.3, 2.9)[i % 4]
            for p in ((1.0,) if i % 5 else (1.0, 0.4)):
                out.append((th, W, k, p))
    return out


def reference_json(ship_parts, sp):
    poses = []
    for pose in reference_poses():
        th, W, k, p = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose), sp)) for q in ship_parts}
        poses.append({"theta": th, "work": W, "size": k, "presence": p, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped pressbrake-rig.json's parts and work: each part's matrix as 3 "
                        "rows of 4 (block units) at each pose (W in plates; travel is |theta|). The site's and the mod's tests "
                        "check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. The wearing edges' texture code is "
             "'edge' and the screws' 'screw': the renderer sets them to the fitted items' metal. Keep element names when "
             "editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return (REST, pose_at(1, 0.1), pose_at(2, 0.25), pose_at(1, 0.4), pose_at(2, 0.6), pose_at(1, 0.75), pose_at(2, 0.92), pose_at(1, 1.0))


def on_show(part, k):
    """Whether a part can be seen with metal k on the brake: each metal's sheet only with that metal."""
    if part in ("la", "lm", "lb"):
        return k == 1
    if part in ("ca", "cm", "cb"):
        return k == 2
    return True


def shown(posed_els, pose):
    """The posed elements as they can be seen: another metal's sheet moved far away (copies, in order)."""
    k = pose[2]
    out = []
    for e in posed_els:
        if not on_show(e.part, k):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def fix_coplanar(els, parts):
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose), coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the press brake's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_pressbrake
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
    ok = validate_pressbrake.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "pressbrake.json", args.out / "pressbrake_frame.json", args.out / "pressbrake-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "pressbrake.json", SHAPE_DIR / "pressbrake_frame.json", RIG_DIR / "pressbrake-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts, ship["work"])
    ok = validate_pressbrake.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
