#!/usr/bin/env python3
"""Generate the amalgam pan's shapes, rig and reference poses (and its provisional mercury texture).

The amalgam pan is a hand-worked amalgamating pan of the mid 1800s (the Washoe process, scaled down to
one block): a wide, shallow sheet-iron pan with a beaded rim on an oak stand, a cast-iron step (the
central cone) in its floor carrying the foot of an iron spindle, and an iron bridge across the rim
carrying the spindle's upper bearing. On the spindle are the muller (a hub with four arms, an iron shoe
bolted under each arm's end, dragging on the pan's floor) and, above the bridge, a crank with an oak
handle. The player holds right-click on the pan, as on the quern, and turns the crank: the muller turns
with it, and its shoes grind the concentrate into the mercury on the floor. Its contents are drawn as
flat layers on the floor: a mercury pool, or the grey amalgam paste. Everything is built here from plain
boxes; no other mod's model is used.

It writes, deterministically,

    amalgampan.json         the whole pan, every moving part and both contents   (assets/.../shapes/block/)
    amalgampan_frame.json   the static frame only (block and item)                (assets/.../shapes/block/)
    amalgampan-rig.json     the cell, anchors and the part rig                    (assets/.../config/)
    rig-reference.json      every part's matrix at a spread of crank angles       (tests/AmalgamPan/)
    mercury.png             the provisional mercury texture                       (assets/.../textures/block/liquid/)

or, with `--out DIR`, all five into DIR. It validates its own output (validate_amalgampan.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
cell's north-west-bottom corner (the "build frame"); the pan is one cell, the controller [0,0,0], so the
shipped files are the build frame divided by 16. The pan's axis is the cell's vertical centre line.

The rig's one input, as this station uses it (README "Rig schema"):

    theta  the crank's angle, the hold-to-work clock: the muller and its shoes turn by it (ratio 1)
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

import mercury_texture  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
ASSETS = MOD / "assets" / "seraphhorizons"
SHAPE_DIR = ASSETS / "shapes" / "block"
RIG_DIR = ASSETS / "config"
TEXTURE_OUT = ASSETS / "textures" / "block" / "liquid" / "mercury.png"
REFERENCE_OUT = MOD / "tests" / "AmalgamPan" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
DEG = math.pi / 180

# ---------------------------------------------------------------- the box, the cell
CELLS_X, CELLS_Y, CELLS_Z = 1, 1, 1          # one block
ORIGIN_CELL = (0, 0, 0)                      # the controller: the pan itself

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "pan": "game:block/metal/sheet/iron1",          # the pan: riveted sheet iron
    "iron": "game:block/metal/plate/iron",          # cast and wrought iron: the step, the bridge, the muller, the crank
    "shoe": "game:block/metal/plate/iron",          # the muller's shoes: their own code, so a renderer can set the fitted metal
    "mercury": "seraphhorizons:block/liquid/mercury",   # provisional, made by mercury_texture.py
    "amalgam": "game:block/metal/ingot/leadsolder",     # a dull, mottled grey: the paste
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the pan
CX, CZ = 8.0, 8.0                            # the pan's axis, the spindle's: the cell's centre line
AXIS = (CX, 0.0, CZ)
SIDES = 16                                   # the pan is a 16-sided polygon; every round part's faces are on these angles
PAN_Y0 = 5.0                                 # the pan's underside, on the stand
FLOOR_TOP = 6.0                              # the floor's top: the shoes drag on it, the contents lie on it
RIM_TOP = 9.2                                # the wall's top: 3.2 deep inside, 12.4 across: shallow
WALL_Y0 = 5.5                                # the wall's foot, sunk into the floor's edge so no slit shows between them
R_OUT = 7.0                                  # the wall's outside (apothem)
R_IN = 6.2                                   # its inside
R_FLOOR = R_OUT - 0.02                       # the floor's edge, a hair inside the wall's outside: its staggered strips never show
BEAD = (7.0, 7.3, 8.4)                       # the rim's bead: apothem from, to; its bottom (its top is the rim's)
ALT = 0.03                                   # every other ring segment this much shorter at each end, so neighbours' ends never share a plane
PLUG = (6.3, 7.3)                            # the discharge plug's boss on the wall's east face: from y, to y

# ---------------------------------------------------------------- the step (the central cone) and the spindle
CONE_BASE = (1.7, 5.6, 7.1)                  # apothem, from y, to y (its foot sunk in the floor)
CONE_NECK = (1.15, 6.7, 7.9)                 # the neck: the spindle's lower bearing, above the paste
SPINDLE_R = 0.45
SPINDLE_Y = (7.0, 14.7)                      # its foot in the step, clear of the paste; its top through the crank's hub

# ---------------------------------------------------------------- the muller
HUB = (1.35, 8.1, 9.7)                       # apothem, from y, to y: keyed on the spindle, 0.2 over the step
ARM_Y = (8.15, 8.95)                         # the four arms, out from the hub, under the rim
ARM_W = 1.0
ARM_R = (1.0, 5.4)
ARM_ANGLE0 = 45.0                            # the first arm's bearing from south at rest (degrees, about +y); the rest every 90
SHOE_R = (2.3, 5.6)                          # each shoe, under its arm's end: radial extent
SHOE_W = 2.4                                 # across
SHOE_Y = (FLOOR_TOP, ARM_Y[0])               # on the floor, up to the arm

# ---------------------------------------------------------------- the bridge (the upper bearing): a shallow cast arch
FOOT_X = (0.7, 2.0)                          # west; mirrored east: a foot on the rim, over the bead and the wall
FOOT_Y = (RIM_TOP, 10.0)
FOOT_Z = (7.3, 8.7)
BOSS = (1.0, 11.0, 12.6)                     # the bearing boss at the arch's crown: apothem, from y, to y
ARCH_W = 1.0                                 # each half of the arch: two bars this deep in their own plane
ARCH = ((1.35, 9.75), (3.6, 11.15), (7.2, 11.85))   # (x, y): the west half runs from inside the foot, bends, and ends in the boss
ARCH_Z = ((7.4, 8.6), (7.43, 8.57))          # each bar's z: the upper a hair narrower, so their sides never share a plane at the bend

# ---------------------------------------------------------------- the crank
CRANK_HUB = (0.8, 13.2, 14.4)                # keyed on the spindle, over the boss
CRANK_Y = (13.4, 14.1)
CRANK_W = 0.75
CRANK_R = (0.6, 4.95)                        # the arm, out from the hub
HANDLE_R = 4.4                               # the handle's axis from the spindle's
HANDLE = (0.55, CRANK_Y[1], 15.9)            # apothem, from y, to y: an oak peg
CRANK_ANGLE0 = 180.0                         # at rest the crank points north, towards the operator

# ---------------------------------------------------------------- the stand
LEG = ((0.6, 2.4), (13.6, 15.4))             # the legs' x (and z) spans: one at each corner, clear of the pan
APRON_Y = (3.4, PAN_Y0)
APRON = (0.9, 2.1)                           # the aprons' thickness span (west, north); mirrored east, south
BEARER_Z = ((4.4, 5.6), (10.4, 11.6))        # two bearers across under the floor
BEARER_Y = (3.6, PAN_Y0)
LOW_Y = (0.9, 2.1)                           # the low rails, west and east

# ---------------------------------------------------------------- the contents
CONTENT_R = R_IN + 0.05                      # their edge, just into the wall: no gap at the wall
CONTENT_Y0 = 5.7                             # their underside, sunk in the floor (never seen)
MERCURY_TOP = 6.4                            # a pool 0.4 deep
AMALGAM_TOP = 6.8                            # a paste 0.8 deep: the shoes stand out of it
CONTENTS = ((1, "mercury"), (2, "amalgam"))  # (pose's contents index, part id = requires = texture code)

SECONDS_PER_TURN = 1.5                       # the viewer's Play: about 40 turns a minute by hand


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


def disc(c, y0, y1, r, name, part, tex, k=4):
    """A plain round part on a vertical axis through c: k strips as long as the 2k-gon is across, 180/k
    degrees apart, each a hair shorter than the last so no two ends share a plane. k 8 gives the pan's
    16 sides; the first strip's ends face north and south."""
    half = r * math.tan(math.pi / (2 * k))
    out = []
    st = min(0.012, (y1 - y0) / (4 * k))
    for i in range(k):
        lo = [c[0] - half, y0 + st * i, c[2] - r]
        hi = [c[0] + half, y1 - st * i, c[2] + r]
        el = box(lo, hi, f"{name}_{i + 1}" if k > 1 else name, part, tex)
        if i:
            rotate([el], "y", 180.0 * i / k, c)
        out.append(el)
    return out


def ring(r0, r1, y0, y1, name, part, tex):
    """A 16-sided ring about the pan's axis, apothem r0..r1: one box a side, each as long as the side is
    at r1 so the outside closes; every other one ALT shorter at each end."""
    length = 2 * r1 * math.tan(math.pi / SIDES)
    out = []
    for i in range(SIDES):
        d = ALT if i % 2 else 0.0
        el = box([CX - length / 2, y0 + d, CZ + r0], [CX + length / 2, y1 - d, CZ + r1], f"{name}_{i + 1:02d}", part, tex)
        rotate([el], "y", 360.0 * i / SIDES, AXIS)
        out.append(el)
    return out


def radial(r0, r1, w, y0, y1, deg, name, part, tex):
    """A bar from r0 to r1 out from the axis, w across, turned deg about +y from south."""
    el = box([CX - w / 2, y0, CZ + r0], [CX + w / 2, y1, CZ + r1], name, part, tex)
    rotate([el], "y", deg, AXIS)
    return el


def at_angle(r, deg):
    """(x, z) at radius r from the axis, deg about +y from south."""
    return (CX + r * math.sin(deg * DEG), CZ + r * math.cos(deg * DEG))


def mirror_x(x0, x1):
    return (16.0 - x1, 16.0 - x0)


# ---------------------------------------------------------------- builders
def build_stand():
    """The oak stand: a leg at each corner, an apron on each side and two bearers across under the pan,
    low rails west and east."""
    f = "frame"
    out = []
    for i, (x0, x1) in enumerate(LEG):
        for j, (z0, z1) in enumerate(LEG):
            out.append(box([x0, 0.0, z0], [x1, PAN_Y0, z1], f"fr_leg_{'ns'[j]}{'we'[i]}", f, "oak"))
    a0, a1 = APRON
    inner = (LEG[0][1], LEG[1][0])
    out.append(box([inner[0], APRON_Y[0], a0], [inner[1], APRON_Y[1], a1], "fr_apron_n", f, "oak"))
    out.append(box([inner[0], APRON_Y[0], 16.0 - a1], [inner[1], APRON_Y[1], 16.0 - a0], "fr_apron_s", f, "oak"))
    out.append(box([a0, APRON_Y[0], inner[0]], [a1, APRON_Y[1], inner[1]], "fr_apron_w", f, "oak"))
    out.append(box([16.0 - a1, APRON_Y[0], inner[0]], [16.0 - a0, APRON_Y[1], inner[1]], "fr_apron_e", f, "oak"))
    for i, (z0, z1) in enumerate(BEARER_Z):
        out.append(box([a1, BEARER_Y[0], z0], [16.0 - a1, BEARER_Y[1], z1], f"fr_bearer_{i + 1}", f, "oak"))
    out.append(box([a0, LOW_Y[0], inner[0]], [a1, LOW_Y[1], inner[1]], "fr_rail_w", f, "oak"))
    out.append(box([16.0 - a1, LOW_Y[0], inner[0]], [16.0 - a0, LOW_Y[1], inner[1]], "fr_rail_e", f, "oak"))
    return out


def build_pan():
    """The pan: a 16-sided sheet-iron floor and wall with a beaded rim; the cast-iron step in the middle
    of its floor, which carries the spindle's foot."""
    f = "frame"
    out = disc(AXIS, PAN_Y0, FLOOR_TOP, R_FLOOR, "fr_floor", f, "pan", k=8)
    out += ring(R_IN, R_OUT, WALL_Y0, RIM_TOP, "fr_wall", f, "pan")
    out += ring(BEAD[0], BEAD[1], BEAD[2], RIM_TOP, "fr_rim", f, "pan")
    out += disc(AXIS, CONE_BASE[1], CONE_BASE[2], CONE_BASE[0], "fr_cone_base", f, "iron")
    out += disc(AXIS, CONE_NECK[1], CONE_NECK[2], CONE_NECK[0], "fr_cone_neck", f, "iron")
    # the discharge plug, low on the wall's east face, for drawing the amalgam off: an iron boss, an oak plug
    out.append(box([CX + R_OUT, PLUG[0], CZ - 0.6], [CX + R_OUT + 0.5, PLUG[1], CZ + 0.6], "fr_plug_boss", f, "iron"))
    out.append(box([CX + R_OUT + 0.5, PLUG[0] + 0.2, CZ - 0.4], [CX + R_OUT + 0.9, PLUG[1] - 0.2, CZ + 0.4], "fr_plug_bung", f, "oak"))
    return out


def strut(a, b, w, z0, z1, name, part, tex, past=(0.0, 0.0)):
    """A bar from (x, y) point a to point b, w thick in the x-y plane, from z0 to z1 (built west to east),
    running on past a and b by `past`."""
    if b[0] < a[0]:
        a, b, past = b, a, past[::-1]
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    ux, uy = dx / length, dy / length
    a = (a[0] - ux * past[0], a[1] - uy * past[0])
    b = (b[0] + ux * past[1], b[1] + uy * past[1])
    length += past[0] + past[1]
    mid = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (z0 + z1) / 2)
    el = box([mid[0] - length / 2, mid[1] - w / 2, z0], [mid[0] + length / 2, mid[1] + w / 2, z1], name, part, tex)
    rotate([el], "z", math.degrees(math.atan2(dy, dx)), mid)
    return el


def build_bridge():
    """The bridge, a shallow cast-iron arch across the rim: a foot on the rim each side (west and east),
    a half arch from each rising to the crown in two bars, bent where they meet, and at the crown the
    boss of the spindle's upper bearing."""
    f = "frame"
    out = []
    for s, (x0, x1), sign in (("w", FOOT_X, 1.0), ("e", mirror_x(*FOOT_X), -1.0)):
        out.append(box([x0, FOOT_Y[0], FOOT_Z[0]], [x1, FOOT_Y[1], FOOT_Z[1]], f"fr_foot_{s}", f, "iron"))
        pts = [(CX - sign * (CX - x), y) for x, y in ARCH]
        # each bar runs on past the bend by half its width times the tangent of half the bend, closing the outside corner
        turn = abs(math.atan2(pts[2][1] - pts[1][1], abs(pts[2][0] - pts[1][0])) - math.atan2(pts[1][1] - pts[0][1], abs(pts[1][0] - pts[0][0])))
        bend = ARCH_W / 2 * math.tan(turn / 2)
        out.append(strut(pts[0], pts[1], ARCH_W, *ARCH_Z[0], f"fr_arch_{s}1", f, "iron", past=(0.0, bend)))
        out.append(strut(pts[1], pts[2], ARCH_W, *ARCH_Z[1], f"fr_arch_{s}2", f, "iron", past=(bend, 0.0)))
    out += disc(AXIS, BOSS[1], BOSS[2], BOSS[0], "fr_boss", f, "iron")
    return out


def build_muller():
    """The muller: the spindle; on it the hub with four arms, and above the bridge the crank's hub, its
    arm and the oak handle. All of it turns with the crank."""
    p = "muller"
    out = disc(AXIS, SPINDLE_Y[0], SPINDLE_Y[1], SPINDLE_R, "muller_spindle", p, "iron")
    out += disc(AXIS, HUB[1], HUB[2], HUB[0], "muller_hub", p, "iron")
    for i in range(4):
        out.append(radial(ARM_R[0], ARM_R[1], ARM_W, ARM_Y[0], ARM_Y[1], ARM_ANGLE0 + 90.0 * i, f"muller_arm{i + 1}", p, "iron"))
    out += disc(AXIS, CRANK_HUB[1], CRANK_HUB[2], CRANK_HUB[0], "muller_crankhub", p, "iron")
    out.append(radial(CRANK_R[0], CRANK_R[1], CRANK_W, CRANK_Y[0], CRANK_Y[1], CRANK_ANGLE0, "muller_crank", p, "iron"))
    hx, hz = at_angle(HANDLE_R, CRANK_ANGLE0)
    out += disc((hx, 0.0, hz), HANDLE[1], HANDLE[2], HANDLE[0], "muller_handle", p, "oak")
    return out


def build_shoes():
    """The four iron shoes, one under each arm's end, dragging on the floor."""
    return [radial(SHOE_R[0], SHOE_R[1], SHOE_W, SHOE_Y[0], SHOE_Y[1], ARM_ANGLE0 + 90.0 * i, f"shoe_{i + 1}", "shoes", "shoe")
            for i in range(4)]


def build_contents():
    """The contents, flat layers on the floor inside the wall: the mercury pool and the amalgam paste."""
    out = disc(AXIS, CONTENT_Y0, MERCURY_TOP, CONTENT_R, "mercury", "mercury", "mercury", k=8)
    out += disc(AXIS, CONTENT_Y0, AMALGAM_TOP, CONTENT_R, "amalgam", "amalgam", "amalgam", k=8)
    return out


def build():
    return build_muller() + build_shoes() + build_contents() + build_stand() + build_pan() + build_bridge()


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    parts = [
        # theta is the crank's angle: the spindle, and everything keyed on it, turns once a turn of the crank
        {"id": "muller", "match": ["muller_*"], "requires": "muller",
         "drivers": [{"type": "rotate", "axis": "y", "pivot": pt(CX, 0.0, CZ), "ratio": 1.0}]},
        {"id": "shoes", "match": ["shoe_*"], "requires": "shoes", "ride": "muller", "drivers": []},
        {"id": "mercury", "match": ["mercury_*"], "requires": "mercury", "drivers": []},
        {"id": "amalgam", "match": ["amalgam_*"], "requires": "amalgam", "drivers": []},
        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses
# A pose is (theta, contents): contents 0 empty, 1 mercury, 2 amalgam (which layer is drawn; the rig has
# no input for it: each is a requires value that gameplay fits or not).
REST = (0.0, 0)


def inputs_of(pose):
    return {"theta": pose[0]}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose), None)


def turn_poses(step_deg=5.0, contents=(0, 1, 2)):
    """A whole turn of the crank every step_deg, with each contents."""
    n = int(round(360.0 / step_deg))
    return [(i * step_deg * DEG, c) for c in contents for i in range(n)]


def on_show(part, contents):
    """Whether a part can be seen with these contents: each layer only as itself."""
    for idx, pid in CONTENTS:
        if part == pid:
            return contents == idx
    return True


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("pan",)


def make_rig(parts):
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, one cell, the controller [0,0,0]: the pan. A hand "
                    "station: no power cell, no work. theta is the crank's angle, the hold-to-work clock: the muller (and "
                    "the shoes riding it) turns by it about the pan's axis, ratio 1. The contents are requires values: "
                    "mercury and amalgam, one drawn at a time. See the amalgam pan's README for the schema.",
        "cells": [],
        "operatorSide": "north",
        "pan": {"pos": pt(CX, FLOOR_TOP, CZ)},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
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
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts):
    """The cell's boxes from the shipped shape as written, posed at rest by the shipped rig; then the lid."""
    written = flatten(shape["elements"], textures={})
    rest = [posed(w, _part_matrix(ship_parts, part_of(ship_parts, w.name), inputs_of(REST), None)) for w in written]
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
    poses = [REST, (0.7, 1), (2.9, 2), (-1.3, 0), (13.0, 1)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose), None), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells {cells}")
    if worst > 1e-6 or cells != [(0, 0, 0)]:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
REF_THETAS = (0.0, 0.4, 1.1, -0.7, -2.3, 2.9, round(math.pi, 6), 4.7, 7.5, -9.1, 13.0, 25.3)


def reference_json(ship_parts):
    poses = []
    for th in REF_THETAS:
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], {"theta": th}, None)) for q in ship_parts}
        poses.append({"theta": th, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT}; do not edit. Every amalgam pan part's matrix (blocks, first three rows) "
                        "at a spread of crank angles, from the generator's reference maths "
                        "(Machines/tools/machinegen/rigmath.py). The site's and the mod's tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. The shoes' texture code is "
             "'shoe', so a renderer can set it to the fitted metal; 'mercury' is the mod's own provisional texture. Keep "
             "element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return (REST, (0.0, 1), (0.0, 2), (0.3, 1), (45.0 * DEG, 2), (1.2, 0), (2.0, 1), (22.5 * DEG, 2), (math.pi / 2, 1))


def shown(posed_els, pose):
    """The posed elements as they can be seen: the contents not drawn moved far away (copies, in order)."""
    out = []
    for e in posed_els:
        if not on_show(e.part, pose[1]):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def fix_coplanar(els, parts):
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose), coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the amalgam pan's shapes, rig, reference poses and mercury texture.")
    ap.add_argument("--out", type=Path, help="write the five files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_amalgampan
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
    ok = validate_amalgampan.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "amalgampan.json", args.out / "amalgampan_frame.json", args.out / "amalgampan-rig.json",
                args.out / "rig-reference.json")
        texture = args.out / "mercury.png"
    else:
        outs = (SHAPE_DIR / "amalgampan.json", SHAPE_DIR / "amalgampan_frame.json", RIG_DIR / "amalgampan-rig.json", REFERENCE_OUT)
        texture = TEXTURE_OUT
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_amalgampan.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship), reference_dumps(reference_json(ship_parts)))
    for path, text in zip(outs, texts):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        json.loads(path.read_text())
        print(f"wrote {path} ({path.stat().st_size // 1024} KiB)")
    texture.parent.mkdir(parents=True, exist_ok=True)
    texture.write_bytes(mercury_texture.png_bytes())
    print(f"wrote {texture} ({texture.stat().st_size} bytes)")
    if not (check_shipped(els, parts, ship_els, ship_parts, ship) and ok):
        sys.exit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
