#!/usr/bin/env python3
"""Generate the squaring shear's shapes, rig and reference poses.

The squaring shear is a tinsmith's foot-treadle squaring shear of the early-to-mid 1800s, in oak with
iron blades: an oak table (the bed) with the fixed lower blade along its far edge; an oak crosshead
carrying the upper blade, its ends sliding in slots in the oak housings on the end cheeks, pulled down
by two iron links from a foot treadle that pivots at the back of the frame; an iron hold-down bar that
comes down on the sheet before the cut; and the back gauge, two iron arms behind the blades with a stop
across them, that sets the cut. A lead or copper plate goes on the table, 8 x 8, pushed under the blade
against the gauge so 4 lies on each side of the cut. The hold-down clamps it, the treadle brings the
blade down through it, and the two halves (half plates, 8 x 4) are drawn off towards the operator under
the raised blade. Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    squaringshear.json         the whole machine, every moving part   (assets/.../shapes/block/)
    squaringshear_frame.json   the static frame only (block and item)  (assets/.../shapes/block/)
    squaringshear-rig.json     cells, anchors and the part rig         (assets/.../config/)
    rig-reference.json         every part's matrix at a grid of poses  (tests/SquaringShear/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_squaringshear.py) and
exits non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from
the machine box's north-west-bottom corner (the "build frame"); the controller cell is the build
frame's [0,0,0], so the shipped files are the build frame divided by 16. The shear runs along z: the
treadle's foot and the table at the north end (the controller, nearest the player), the blades on the
cells' boundary, the gauge and the treadle's pivot to the south.

The rig's inputs, as this machine uses them (README "Rig schema"):

    theta  the treadle's work, the hold-to-work clock: read by a ratio-0 rotate on the treadle, it moves nothing
    W      the rig's work, the cut cycle of one plate, 0..1
    k      the plate's metal: 0 none, 1 lead (thin), 2 copper (thick)
    p      its presence, 0..1, eased as the plate goes on and after the halves are delivered
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
from machinegen.rigmath import about, apply, part_of, posed, progress_of, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402
from machinegen.geometry import rot  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "SquaringShear" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/SquaringShear/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
DEG = math.pi / 180

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 1, 1, 2          # one wide, one high, two long (z)
ORIGIN_CELL = (0, 0, 0)                      # the controller: the table and the treadle's foot, nearest the player

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "iron": "game:block/metal/plate/iron",
    "blade": "game:block/metal/plate/iron",     # the blades: the renderer sets it to the fitted plate's metal
    "gauge": "game:block/metal/plate/iron",     # the back gauge and the hold-down: the renderer sets it to the fitted rods' metal
    "lead": "game:block/metal/sheet-plain/lead1",
    "copper": "game:block/metal/sheet-plain/copper1",
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the table, the cut and the sheet
YB = 9.0                                     # the table's top: the sheet's underside, the lower blade's edge
ZB = 16.0                                    # the cut: the lower blade's back face, on the cells' boundary
T = 1.0                                      # the sheet's thickness: the game's plate
HALF = 4.0                                   # a half plate: 8 along the blade (x), 4 across it (z)
SHEET_X = (4.0, 12.0)                        # the plate, 8 along the blade
Z_PLATE = (ZB - HALF, ZB + HALF)             # laid against the gauge, 4 each side of the cut
DRAW = 8.0                                   # the halves are drawn this far north, towards the operator

# ---------------------------------------------------------------- the frame
CHEEK = (0.0, 1.2)                           # the oak end cheeks (x); mirrored east
LEG_N, LEG_S = (3.0, 5.0), (29.4, 31.4)      # the cheeks' legs (z)
RAIL_Y = (YB - 1.5, YB)                      # the cheeks' top rails, under the table's ends
LOW_Y = (0.8, 2.0)
BED_X = (1.2, 14.8)                          # the table spans the cheeks
BED_Y0 = YB - 2.0
BED_Z = (LEG_N[0], ZB)
BLADE_X = (2.6, 13.4)                        # the blades, and the rebate the lower blade sits in
POST_Y = (YB, 15.6)                          # the housings on the cheeks, either side of the hold-down's and the crosshead's ends
POST_FRONT_Z = (12.4, 13.3)
POST_MID_Z = (15.65, 16.75)
POST_BACK_Z = (18.85, 20.0)
CAP_Y = (15.6, 16.0)
GRAIL_Z = ((20.5, 22.0), (26.5, 28.0))       # the oak rails the gauge arms lie on
GRAIL_Y = (YB - 2.5, YB - 1.0)
STRETCHER = ((3.6, 5.0), (29.6, 31.2))       # (y, z): ties the back legs, over the treadle's knuckles

# ---------------------------------------------------------------- the crosshead and the upper blade
BLADE_GAP = 0.04                             # the upper blade passes this far behind the lower blade's back face
BLADE_T = 0.76                               # the upper blade's thickness (z)
REST_EDGE = YB + T + 0.8                     # the upper blade's edge at rest: clear of the sheet
DOWN_EDGE = YB - 0.3                         # at the bottom of the stroke: past the lower blade's edge
DROP = REST_EDGE - DOWN_EDGE                 # the crosshead's stroke
BLADE_H = 2.5
HEAD_Z = (ZB + BLADE_GAP + BLADE_T, ZB + BLADE_GAP + BLADE_T + 2.0)
HEAD_Y = (REST_EDGE + 1.8, REST_EDGE + 4.6)  # its underside stays over the far half through the stroke
HEAD_X = (0.3, 15.7)                         # its ends (iron shoes) in the housings' slots

# ---------------------------------------------------------------- the treadle and its links
PIVOT = (2.5, 30.4)                          # (y, z): the treadle's pivot, on the back legs
FOOT = (6.0, 1.5)                            # (y, z): the foot bar at rest
ARM_X = (2.9, 3.7)                           # the treadle's oak arms (west; mirrored east), under the table
ARM_W = 1.0                                  # the arms' depth (in their plane)
LINK_X = (2.2, 2.85)                         # the iron links, just outside the arms (west; mirrored east)
LINK_Z = (HEAD_Z[0] + 0.6, HEAD_Z[0] + 1.4)  # under the crosshead's middle
PIN_R = 0.25
SLOT = 0.35                                  # the links' eyes are slotted: the treadle's pin may wander this far (z) in them

# ---------------------------------------------------------------- the hold-down and the gauge
HOLD_X = (0.4, 15.6)                         # its ends in the housings' front slots
HOLD_Z = (POST_FRONT_Z[1] + 0.1, POST_MID_Z[0] - 0.05)
HOLD_H = 1.0
LIFT = 1.0                                   # unclamped, the hold-down stands this far over the sheet
GAUGE_ARMS_X = ((4.5, 5.5), (10.5, 11.5))
GAUGE_ARM_Z = (ZB + 1.0, GRAIL_Z[1][1])
STOP_X = (3.5, 12.5)
STOP_Z = (Z_PLATE[1], Z_PLATE[1] + 1.0)
STOP_H = 1.5

# ---------------------------------------------------------------- the cycle (t = W, one plate)
T_CLAMP, T_UNCLAMP = (0.06, 0.16), (0.66, 0.76)
T_CUT = (0.20, 0.40, 0.60)                   # the treadle down from, at the bottom, back up
T_OFF = (0.80, 0.96)                         # the halves are drawn north under the raised blade
STROKES = {"thin": 1.0, "thick": 1.5}        # the pace: treadle strokes (theta / 2 pi) a plate

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


# ---------------------------------------------------------------- the treadle's geometry
def link_pin():
    """(y, z) of the treadle's pin under the link at rest: on the arm's line, under the link's middle."""
    z = (LINK_Z[0] + LINK_Z[1]) / 2
    return (PIVOT[0] + (PIVOT[1] - z) * (FOOT[0] - PIVOT[0]) / (PIVOT[1] - FOOT[1]), z)


def turned(yz, deg):
    """(y, z) turned `deg` about x through the pivot, as the rig turns it."""
    m = about(rot("x", deg), [0.0, PIVOT[0] / B, PIVOT[1] / B])
    q = apply(m, [0.0, yz[0] / B, yz[1] / B])
    return (q[1] * B, q[2] * B)


def treadle_throw():
    """The treadle's turn (degrees, signed) that brings the pin, and so the links and the crosshead,
    DROP down: the foot goes down."""
    y0 = link_pin()[0]
    lo, hi = 0.0, 40.0
    sign = -1.0 if turned(FOOT, -1.0)[0] < FOOT[0] else 1.0
    for _ in range(200):
        mid = (lo + hi) / 2
        if y0 - turned(link_pin(), sign * mid)[0] < DROP:
            lo = mid
        else:
            hi = mid
    return sign * (lo + hi) / 2


# ---------------------------------------------------------------- builders
def build_frame():
    """The oak cheeks (legs, rails), the table, the housings with their caps, the gauge rails, a
    stretcher; the iron bosses and pins the treadle turns on."""
    f = "frame"
    out = []
    for s, mx in sides():
        cx = mx(*CHEEK)
        out.append(box([cx[0], 0.0, LEG_N[0]], [cx[1], RAIL_Y[0], LEG_N[1]], f"fr_legn_{s}", f, "oak"))
        out.append(box([cx[0], 0.0, LEG_S[0]], [cx[1], RAIL_Y[0], LEG_S[1]], f"fr_legs_{s}", f, "oak"))
        out.append(box([cx[0], RAIL_Y[0], LEG_N[0]], [cx[1], RAIL_Y[1], LEG_S[1]], f"fr_rail_{s}", f, "oak"))
        out.append(box([cx[0], LOW_Y[0], LEG_N[1]], [cx[1], LOW_Y[1], LEG_S[0]], f"fr_rail_{s}_low", f, "oak"))
        for name, z in (("front", POST_FRONT_Z), ("mid", POST_MID_Z), ("back", POST_BACK_Z)):
            out.append(box([cx[0], POST_Y[0], z[0]], [cx[1], POST_Y[1], z[1]], f"fr_post{name}_{s}", f, "oak"))
        out.append(box([cx[0], CAP_Y[0], POST_FRONT_Z[0]], [cx[1], CAP_Y[1], POST_BACK_Z[1]], f"fr_cap_{s}", f, "oak"))
        # the treadle's pivot: an iron boss on the back leg's inner face, a pin through it and the arm
        bx = mx(CHEEK[1], CHEEK[1] + 0.6)
        out.append(box([bx[0], PIVOT[0] - 1.0, PIVOT[1] - 0.8], [bx[1], PIVOT[0] + 1.0, PIVOT[1] + 0.8], f"fr_boss_{s}", f, "iron"))
        px = mx(0.3, ARM_X[1] + 0.05)
        out += disc("x", (0.0, PIVOT[0], PIVOT[1]), px[0], px[1], PIN_R, f"fr_pin_{s}", f, "iron")
    # the table: an oak top on the cheeks' rails, its far edge rebated for the lower blade
    out.append(box([BED_X[0], BED_Y0, BED_Z[0]], [BED_X[1], YB, ZB - 1.0], "fr_bed", f, "oak"))
    out.append(box([BED_X[0], BED_Y0, ZB - 1.0], [BED_X[1], YB - 1.0, ZB], "fr_bed_rebate", f, "oak"))
    for s, mx in sides():
        ex = mx(BED_X[0], BLADE_X[0])
        out.append(box([ex[0], YB - 1.0, ZB - 1.0], [ex[1], YB, ZB], f"fr_bed_end_{s}", f, "oak"))
    for i, z in enumerate(GRAIL_Z):
        out.append(box([BED_X[0], GRAIL_Y[0], z[0]], [BED_X[1], GRAIL_Y[1], z[1]], f"fr_grail{i + 1}", f, "oak"))
    out.append(box([BED_X[0], STRETCHER[0][0], STRETCHER[1][0]], [BED_X[1], STRETCHER[0][1], STRETCHER[1][1]], "fr_stretcher", f, "oak"))
    return out


def build_crosshead():
    """The crosshead at rest: an oak beam with iron shoes at its ends in the housings' slots; and the
    upper blade (the `blade` stage) bolted to its front face."""
    out = [box([CHEEK[1], HEAD_Y[0], HEAD_Z[0]], [16.0 - CHEEK[1], HEAD_Y[1], HEAD_Z[1]], "crosshead_beam", "crosshead", "oak")]
    for s, mx in sides():
        sx = mx(HEAD_X[0], CHEEK[1])
        out.append(box([sx[0], HEAD_Y[0], HEAD_Z[0]], [sx[1], HEAD_Y[1], HEAD_Z[1]], f"crosshead_shoe_{s}", "crosshead", "iron"))
    z0 = ZB + BLADE_GAP
    out.append(box([BLADE_X[0], REST_EDGE, z0], [BLADE_X[1], REST_EDGE + BLADE_H, z0 + BLADE_T], "upperblade_plate", "upperblade", "blade"))
    return out


def build_treadle():
    """The foot treadle at rest: two oak arms from the pivot under the table to the foot bar, iron
    knuckles round the pivot's pins and iron pins the links hang on."""
    out = []
    py, pz = link_pin()
    for s, mx in sides():
        ax = mx(*ARM_X)
        a = (0.0, PIVOT[0], PIVOT[1])
        b = (0.0, FOOT[0], FOOT[1])
        el = strut(a, b, ARM_W, ax[1] - ax[0], f"treadle_arm_{s}", "treadle", "oak")
        el.c[0] = (ax[0] + ax[1]) / 2
        out.append(el)
        kx = mx(ARM_X[0] - 0.05, ARM_X[1] + 0.05)
        out.append(box([kx[0], PIVOT[0] - 0.7, PIVOT[1] - 0.7], [kx[1], PIVOT[0] + 0.7, PIVOT[1] + 0.7], f"treadle_knuckle_{s}", "treadle", "iron"))
        lx = mx(LINK_X[0] - 0.1, ARM_X[0] + 0.3)
        out += disc("x", (0.0, py, pz), lx[0], lx[1], PIN_R, f"treadle_pin_{s}", "treadle", "iron")
    out.append(box([ARM_X[0], FOOT[0] - 0.5, FOOT[1] - 1.0], [16.0 - ARM_X[0], FOOT[0] + 0.5, FOOT[1] + 1.0], "treadle_foot", "treadle", "oak"))
    return out


def build_links():
    """The two iron links, hung from the crosshead's underside down to the treadle's pins, their lower
    eyes slotted for the pin's small swing."""
    out = []
    py, _ = link_pin()
    for s, mx in sides():
        lx = mx(*LINK_X)
        out.append(box([lx[0], py - PIN_R - 0.45, LINK_Z[0]], [lx[1], HEAD_Y[0], LINK_Z[1]], f"link{s}_strap", f"link{s}", "iron"))
    return out


def build_gauge():
    """The `gauge` stage: the hold-down bar over the near half, its ends in the housings' front slots
    (lying on the table at rest), and the back gauge: two arms on the gauge rails and the stop across them."""
    out = [box([HOLD_X[0], YB, HOLD_Z[0]], [HOLD_X[1], YB + HOLD_H, HOLD_Z[1]], "holddown_bar", "holddown", "gauge")]
    for i, (x0, x1) in enumerate(GAUGE_ARMS_X):
        out.append(box([x0, YB - 1.0, GAUGE_ARM_Z[0]], [x1, YB, GAUGE_ARM_Z[1]], f"gauge_arm{i + 1}", "gauge", "gauge"))
    out.append(box([STOP_X[0], YB, STOP_Z[0]], [STOP_X[1], YB + STOP_H, STOP_Z[1]], "gauge_stop", "gauge", "gauge"))
    return out


def build_lower_blade():
    return [box([BLADE_X[0], YB - 1.0, ZB - 1.0], [BLADE_X[1], YB, ZB], "lowerblade_strip", "lowerblade", "blade")]


def build_work():
    """The plate, lead and copper, as laid against the gauge: the near half (f) on the table, the far
    half (b) over the lower blade and the gauge arms. The two halves are one 8 x 8 plate until the cut."""
    out = []
    x0, x1 = SHEET_X
    for _cls, pre, tex, _req in METALS:
        out.append(box([x0, YB, Z_PLATE[0]], [x1, YB + T, ZB], f"{pre}f_1", f"{pre}f", tex))
        out.append(box([x0, YB, ZB], [x1, YB + T, Z_PLATE[1]], f"{pre}b_1", f"{pre}b", tex))
    return out


def build():
    return build_crosshead() + build_treadle() + build_links() + build_gauge() + build_lower_blade() + build_work() + build_frame()


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def per_class(thin, thick=None):
    return {"thin": r6(thin), "thick": r6(thin if thick is None else thick)}


WORK = {"name": "cut", "unit": "plates", "step": 0.005, "end": {"thin": 1.0, "thick": 1.0}}
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


def pivot_pt():
    return pt(0.0, PIVOT[0], PIVOT[1])


def cut_windows():
    return [win(T_CUT[0], T_CUT[1], T_CUT[1], T_CUT[2])]


def clamp_windows():
    return [win(T_CLAMP[0], T_CLAMP[1], *T_UNCLAMP)]


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    throw = treadle_throw() * DEG
    parts = [
        {"id": "crosshead", "match": ["crosshead_*"], "requires": None,
         "drivers": [gauge("slide", "y", per_class(-DROP / B), cut_windows())]},
        {"id": "upperblade", "match": ["upperblade_*"], "requires": "blade", "ride": "crosshead", "drivers": []},
        # theta is the treadle's work, the hold-to-work clock: a ratio-0 rotate carries it and moves nothing
        {"id": "treadle", "match": ["treadle_*"], "requires": None,
         "drivers": [gauge("rotate", "x", per_class(throw), cut_windows(), pivot=pivot_pt()),
                     {"type": "rotate", "axis": "x", "pivot": pivot_pt(), "ratio": 0.0}]},
    ]
    for s, _ in sides():
        parts.append({"id": f"link{s}", "match": [f"link{s}_*"], "requires": None, "ride": "crosshead", "drivers": []})
    parts.append({"id": "holddown", "match": ["holddown_*"], "requires": "gauge",
                  "drivers": [gauge("slide", "y", per_class((T + LIFT) / B), mode="present"),
                              gauge("slide", "y", per_class(-LIFT / B), clamp_windows())]})
    parts.append({"id": "gauge", "match": ["gauge_*"], "requires": "gauge", "drivers": []})
    parts.append({"id": "lowerblade", "match": ["lowerblade_*"], "requires": "blade", "drivers": []})
    for _cls, pre, _tex, req in METALS:
        for n in ("f", "b"):
            parts.append({"id": f"{pre}{n}", "match": [f"{pre}{n}_*"], "requires": req,
                          "drivers": [gauge("slide", "z", per_class(-DRAW / B), [win(*T_OFF)])]})
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
    """A plate of metal k at W, the treadle at its pace."""
    th = TAU * STROKES[("thin", "thick")[k - 1]] * W if theta is None else theta
    return (th, W, k, p)


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("output", "plate", "edge")


def make_rig(parts):
    progress_of({"work": WORK})
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the table and the "
                    "treadle's foot, nearest the player who placed it; the gauge runs south. A hand machine: no power cell. "
                    "work is the cut cycle of one plate, W 0..1; k is the plate's metal (1 lead, 2 copper); gauge windows "
                    "are placed in plates. theta is the treadle's work (the hold-to-work clock), carried by a ratio-0 rotate "
                    "on the treadle. See the squaring shear's README for the schema.",
        "cells": [],
        "outputSide": "north",
        "output": {"pos": pt(8.0, YB + T, ZB - DRAW)},
        "plate": {"pos": pt(8.0, YB + T, ZB)},
        "edge": {"pos": pt(8.0, YB, ZB)},
        "work": dict(WORK),
        "cut": {"strokesPerPlate": per_class(STROKES["thin"], STROKES["thick"]),
                "plates": {"thin": "game:metalplate-lead", "thick": "game:metalplate-copper"},
                "halfPlates": {"thin": "seraphhorizons:halfplate-lead", "thick": "seraphhorizons:halfplate-copper"},
                "halfPlatesPerPlate": 2,
                "_comment": "strokesPerPlate: the pace, treadle strokes (theta / 2 pi) a plate; copper half as much again. "
                            "plates: each class's work. halfPlates: what a plate is cut into, halfPlatesPerPlate of them "
                            "(one cut, across the plate's middle)."},
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
    poses = [REST, pose_at(1, 0.2), pose_at(2, 0.4), pose_at(1, 0.7), pose_at(2, 0.99), (1.3, 0.3, 1, 0.6)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose), sp), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells {cells}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
REF_EDGES = (0.0, 0.04, 0.08, 0.11, 0.16, 0.2, 0.25, 0.3, 0.35, 0.4, 0.45, 0.5, 0.6, 0.65, 0.7, 0.76, 0.8, 0.85,
             0.9, 0.96, 0.98, 1.0)


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
    return {"_comment": f"Generated by {SCRIPT} from the shipped squaringshear-rig.json's parts and work: each part's matrix as "
                        "3 rows of 4 (block units) at each pose (W in plates; travel is |theta|). The site's and the mod's "
                        "tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. The blades' texture code is "
             "'blade' and the gauge's and hold-down's 'gauge': the renderer sets them to the fitted items' metal. Keep "
             "element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return (REST, pose_at(1, 0.1), pose_at(2, 0.2), pose_at(1, 0.3), pose_at(2, 0.4), pose_at(1, 0.55), pose_at(2, 0.7),
            pose_at(1, 0.88), pose_at(2, 1.0))


def on_show(part, k):
    """Whether a part can be seen with metal k on the shear: each metal's sheet only with that metal."""
    if part in ("lf", "lb"):
        return k == 1
    if part in ("cf", "cb"):
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
    ap = argparse.ArgumentParser(description="Generate the squaring shear's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_squaringshear
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
    ok = validate_squaringshear.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "squaringshear.json", args.out / "squaringshear_frame.json", args.out / "squaringshear-rig.json",
                args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "squaringshear.json", SHAPE_DIR / "squaringshear_frame.json", RIG_DIR / "squaringshear-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts, ship["work"])
    ok = validate_squaringshear.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
