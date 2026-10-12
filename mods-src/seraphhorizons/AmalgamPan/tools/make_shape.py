#!/usr/bin/env python3
"""Generate the amalgam pan's shapes, rig and reference poses (and its slurry texture).

The amalgam pan is an amalgamating pan of the mid 1800s (the Washoe process, scaled down to one block),
worked like the game's quern, both ways: by hand, the player holding right-click on it, or by a vanilla
axle under it, as the Washoe pan was driven from below. A wide, shallow sheet-iron pan with a beaded rim
on an oak stand, a hollow cast-iron cone in the middle of its floor, and an iron spindle that carries the
muller (a hub with four arms, an iron shoe bolted under each arm's end, dragging on the pan's floor) over
the cone and comes down through the cone and a footstep bearing on an oak bridge-tree to the block's
bottom face, where it ends in the vanilla axle's oak cross profile: an axle in the block below turns it.
The muller turns with the axle, once a turn, as the quern's stone does; by hand there is nothing to see
but the muller turning. Nothing goes in or out but by the player's hand: there is no chute, hopper or
other transfer. Its contents are drawn as flat layers on the floor: a mercury pool; the charge, a grey-brown
slurry of ground concentrate worked into the mercury; or the grey amalgam paste. Everything is built here
from plain boxes; no other mod's model is used.

It writes, deterministically,

    amalgampan.json         the whole pan, every moving part and the contents    (assets/.../shapes/block/)
    amalgampan_frame.json   the static frame only (block and item)                (assets/.../shapes/block/)
    amalgampan-rig.json     the cell, anchors and the part rig                    (assets/.../config/)
    rig-reference.json      every part's matrix at a spread of shaft angles       (tests/AmalgamPan/)
    slurry.png              the charge's slurry texture                         (assets/.../textures/block/liquid/)

or, with `--out DIR`, all five into DIR. It validates its own output (validate_amalgampan.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
cell's north-west-bottom corner (the "build frame"); the pan is one cell, the controller [0,0,0], so the
shipped files are the build frame divided by 16. The pan's axis, the spindle's, is the cell's vertical
centre line, and the axle below comes up into the cell's down face on it (`powerCell` [0,0,0],
`powerFace` down).

The rig's one input, as this station uses it (README "Rig schema"):

    theta  the spindle's angle: the axle's under power, or the hold-to-work clock by hand; the spindle,
           the muller and its shoes turn by it about the pan's axis, once a turn
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

import slurry_texture  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
ASSETS = MOD / "assets" / "seraphhorizons"
SHAPE_DIR = ASSETS / "shapes" / "block"
RIG_DIR = ASSETS / "config"
TEXTURE_OUT = ASSETS / "textures" / "block" / "liquid" / "slurry.png"
REFERENCE_OUT = MOD / "tests" / "AmalgamPan" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
DEG = math.pi / 180

# ---------------------------------------------------------------- the box, the cell
CELLS_X, CELLS_Y, CELLS_Z = 1, 1, 1          # one block
ORIGIN_CELL = (0, 0, 0)                      # the controller: the pan itself
POWER_CELL, POWER_FACE = (0, 0, 0), "down"   # a vanilla axle in the block below comes up into the pan's down face, as into the quern's

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "pan": "game:block/metal/sheet/iron1",          # the pan: riveted sheet iron
    "iron": "game:block/metal/plate/iron",          # cast and wrought iron: the cone, the footstep, the spindle, the muller
    # mercury as Expanded Matter draws it: the game's andesite, with its kimberlite blended over it (Lighten),
    # which only a blocktype can say (README "Gameplay"); the shape names the base
    "mercury": "game:block/stone/rock/andesite2",
    "slurry": "seraphhorizons:block/liquid/slurry",     # the charge: made by slurry_texture.py to sit beside that mercury
    "amalgam": "game:block/metal/ingot/leadsolder",     # a dull, mottled grey: the paste
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the pan
CX, CZ = 8.0, 8.0                            # the pan's axis, the spindle's: the cell's centre line
AXIS = (CX, 0.0, CZ)
SIDES = 16                                   # the pan is a 16-sided polygon; every round part's faces are on these angles
PAN_Y0 = 5.0                                 # the pan's underside, on the stand: room under it for the footstep and the coupling
FLOOR_TOP = PAN_Y0 + 1.0                     # the floor's top: the shoes drag on it, the contents lie on it
RIM_TOP = PAN_Y0 + 4.2                       # the wall's top: 3.2 deep inside, 12.4 across: shallow
WALL_Y0 = PAN_Y0 + 0.5                       # the wall's foot, sunk into the floor's edge so no slit shows between them
R_OUT = 7.0                                  # the wall's outside (apothem)
R_IN = 6.2                                   # its inside
R_FLOOR = R_OUT - 0.02                       # the floor's edge, a hair inside the wall's outside: its staggered strips never show
BEAD = (7.0, 7.3, PAN_Y0 + 3.4)              # the rim's bead: apothem from, to; its bottom (its top is the rim's)
ALT = 0.03                                   # every other ring segment this much shorter at each end, so neighbours' ends never share a plane
PLUG = (PAN_Y0 + 0.3, PAN_Y0 + 1.3)          # the discharge plug's boss on the wall's east face: from y, to y

# ---------------------------------------------------------------- the cone (the spindle's upper bearing) and the spindle
CONE_BASE = (1.7, PAN_Y0 + 0.6, PAN_Y0 + 2.1)    # apothem, from y, to y (its foot sunk in the floor): hollow, the spindle runs up through it
CONE_NECK = (1.15, PAN_Y0 + 1.7, PAN_Y0 + 2.9)   # the neck: the spindle's upper bearing, above the paste
SPINDLE_R = 0.5
SPINDLE_Y = (1.0, PAN_Y0 + 5.1)              # from inside the coupling, down through the footstep, to just over the muller's hub

# ---------------------------------------------------------------- the muller
HUB = (1.35, PAN_Y0 + 3.1, PAN_Y0 + 4.7)     # apothem, from y, to y: keyed on the spindle, 0.2 over the cone
ARM_Y = (PAN_Y0 + 3.15, PAN_Y0 + 3.95)       # the four arms, out from the hub, under the rim
ARM_W = 1.0
ARM_R = (1.0, 5.4)
ARM_ANGLE0 = 45.0                            # the first arm's bearing from south at rest (degrees, about +y); the rest every 90
SHOE_R = (2.3, 5.6)                          # each shoe, under its arm's end: radial extent
SHOE_W = 2.4                                 # across
SHOE_Y = (FLOOR_TOP, ARM_Y[0])               # on the floor, up to the arm

# ---------------------------------------------------------------- the drive: the footstep and the coupling
# No gearing: as the quern's stone turns with the axle under it (the game's quern draws its top at the
# mechanical network's angle), the spindle turns with the vanilla axle in the block below, once a turn.
GEAR_RATIO = 1.0                             # the spindle's turns per turn of the axle (or of the hand's clock)
FOOTSTEP = (1.1, 0.8)                        # the footstep bearing round the spindle, on the bridge-tree: apothem; height
COUPLING = (1.5, 0.6, 1.4)                   # the spindle's foot in the vanilla axle's oak cross profile: bars 3 by 1.2 crossed, 1.4 high
AXLE_MEET = 0.0                              # where the axle below meets it: the block's bottom face, on the pan's axis

# ---------------------------------------------------------------- the stand
LEG = ((0.6, 2.4), (13.6, 15.4))             # the legs' x (and z) spans: one at each corner, clear of the pan
APRON_Y = (PAN_Y0 - 1.6, PAN_Y0)
APRON = (0.9, 2.1)                           # the aprons' thickness span (west, north); mirrored east, south
BEARER_Z = ((4.4, 5.6), (10.4, 11.6))        # two bearers across under the floor
BEARER_Y = (PAN_Y0 - 1.4, PAN_Y0)
LOW_Y = (1.6, 2.8)                           # the low rails, west and east, and the bridge-tree across between them
BRIDGE_Z = (6.8, 9.2)                        # the bridge-tree, under the pan's axis: it carries the footstep, the coupling clear below it

# ---------------------------------------------------------------- the contents
CONTENT_R = R_IN + 0.05                      # their edge, just into the wall: no gap at the wall
CONTENT_Y0 = FLOOR_TOP - 0.3                 # their underside, sunk in the floor (never seen)
MERCURY_TOP = FLOOR_TOP + 0.4                # a pool 0.4 deep
SLURRY_TOP = FLOOR_TOP + 1.6                 # the charge, 1.6 deep: a loaded pan, over the cone's foot, the shoes mostly in it
AMALGAM_TOP = FLOOR_TOP + 0.8                # a paste 0.8 deep: the shoes stand out of it
CONTENTS = ((1, "mercury"), (2, "slurry"), (3, "amalgam"))   # (pose's contents index, part id = requires = texture code), in the pan's order

SECONDS_PER_TURN = 1.5                       # the viewer's Play: 40 turns a minute, an axle's easy pace


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


def disc(axis, c, a0, a1, r, name, part, tex, k=4):
    """A plain round part on a line along `axis` ("y" or "z") through c: k strips as long as the 2k-gon
    is across, 180/k degrees apart, each a hair shorter than the last so no two ends share a plane. k 8
    gives the pan's 16 sides. About y the first strip's ends face north and south; about z, up and down."""
    half = r * math.tan(math.pi / (2 * k))
    out = []
    st = min(0.012, (a1 - a0) / (4 * k))
    for i in range(k):
        if axis == "y":
            lo, hi = [c[0] - half, a0 + st * i, c[2] - r], [c[0] + half, a1 - st * i, c[2] + r]
        else:
            lo, hi = [c[0] - half, c[1] - r, a0 + st * i], [c[0] + half, c[1] + r, a1 - st * i]
        el = box(lo, hi, f"{name}_{i + 1}" if k > 1 else name, part, tex)
        if i:
            rotate([el], axis, 180.0 * i / k, c)
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
    """A bar from r0 to r1 out from the pan's axis, w across, turned deg about +y from south."""
    el = box([CX - w / 2, y0, CZ + r0], [CX + w / 2, y1, CZ + r1], name, part, tex)
    rotate([el], "y", deg, AXIS)
    return el


# ---------------------------------------------------------------- builders
def build_stand():
    """The oak stand: a leg at each corner, an apron on each side and two bearers across under the pan;
    low rails west and east, and the bridge-tree across between them under the pan's axis, which carries
    the spindle's footstep."""
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
    out.append(box([a1, LOW_Y[0], BRIDGE_Z[0]], [16.0 - a1, LOW_Y[1], BRIDGE_Z[1]], "fr_bridgetree", f, "oak"))
    return out


def build_pan():
    """The pan: a 16-sided sheet-iron floor and wall with a beaded rim; the hollow cast-iron cone in the
    middle of its floor, through which the spindle rises; a discharge plug low on the wall."""
    f = "frame"
    out = disc("y", AXIS, PAN_Y0, FLOOR_TOP, R_FLOOR, "fr_floor", f, "pan", k=8)
    out += ring(R_IN, R_OUT, WALL_Y0, RIM_TOP, "fr_wall", f, "pan")
    out += ring(BEAD[0], BEAD[1], BEAD[2], RIM_TOP, "fr_rim", f, "pan")
    out += disc("y", AXIS, CONE_BASE[1], CONE_BASE[2], CONE_BASE[0], "fr_cone_base", f, "iron")
    out += disc("y", AXIS, CONE_NECK[1], CONE_NECK[2], CONE_NECK[0], "fr_cone_neck", f, "iron")
    # the discharge plug, low on the wall's east face, for drawing the amalgam off: an iron boss, an oak plug
    out.append(box([CX + R_OUT, PLUG[0], CZ - 0.6], [CX + R_OUT + 0.5, PLUG[1], CZ + 0.6], "fr_plug_boss", f, "iron"))
    out.append(box([CX + R_OUT + 0.5, PLUG[0] + 0.2, CZ - 0.4], [CX + R_OUT + 0.9, PLUG[1] - 0.2, CZ + 0.4], "fr_plug_bung", f, "oak"))
    return out


def build_footstep():
    """The spindle's lower bearing: a cast-iron footstep on the bridge-tree, which the spindle comes down
    through to its coupling."""
    return disc("y", AXIS, LOW_Y[1], LOW_Y[1] + FOOTSTEP[1], FOOTSTEP[0], "fr_footstep", "frame", "iron")


def build_muller():
    """The muller and its spindle, one body turning about the pan's axis: over the cone the muller's hub
    with four arms and an iron shoe bolted under each arm's end, dragging on the floor; the spindle down
    through the cone, the floor and the footstep; and at its foot, under the bridge-tree, the coupling, the
    vanilla axle's oak cross profile, which meets the axle in the block below at the block's bottom face."""
    p = "muller"
    out = disc("y", AXIS, SPINDLE_Y[0], SPINDLE_Y[1], SPINDLE_R, "muller_spindle", p, "iron")
    half, thick, high = COUPLING
    out.append(box([CX - thick, AXLE_MEET, CZ - half], [CX + thick, AXLE_MEET + high, CZ + half], "muller_couplinga", p, "oak"))
    out.append(box([CX - half, AXLE_MEET, CZ - thick], [CX + half, AXLE_MEET + high, CZ + thick], "muller_couplingb", p, "oak"))
    out += disc("y", AXIS, HUB[1], HUB[2], HUB[0], "muller_hub", p, "iron")
    for i in range(4):
        out.append(radial(ARM_R[0], ARM_R[1], ARM_W, ARM_Y[0], ARM_Y[1], ARM_ANGLE0 + 90.0 * i, f"muller_arm{i + 1}", p, "iron"))
    for i in range(4):
        out.append(radial(SHOE_R[0], SHOE_R[1], SHOE_W, SHOE_Y[0], SHOE_Y[1], ARM_ANGLE0 + 90.0 * i, f"muller_shoe{i + 1}", p, "iron"))
    return out


def build_contents():
    """The contents, flat layers on the floor inside the wall: the mercury pool, the charge (the slurry of
    concentrate worked into the mercury) and the amalgam paste."""
    out = disc("y", AXIS, CONTENT_Y0, MERCURY_TOP, CONTENT_R, "mercury", "mercury", "mercury", k=8)
    out += disc("y", AXIS, CONTENT_Y0, SLURRY_TOP, CONTENT_R, "slurry", "slurry", "slurry", k=8)
    out += disc("y", AXIS, CONTENT_Y0, AMALGAM_TOP, CONTENT_R, "amalgam", "amalgam", "amalgam", k=8)
    return out


def build():
    return build_muller() + build_contents() + build_stand() + build_pan() + build_footstep()


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
        # theta is the spindle's angle: the axle's under power, the hold-to-work clock by hand; the muller turns with it
        {"id": "muller", "match": ["muller_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "y", "pivot": pt(CX, 0.0, CZ), "ratio": GEAR_RATIO}]},
        {"id": "mercury", "match": ["mercury_*"], "requires": "mercury", "drivers": []},
        {"id": "slurry", "match": ["slurry_*"], "requires": "slurry", "drivers": []},
        {"id": "amalgam", "match": ["amalgam_*"], "requires": "amalgam", "drivers": []},
        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses
# A pose is (theta, contents): contents 0 empty, 1 mercury, 2 slurry, 3 amalgam (which layer is drawn; the rig has
# no input for it: each is a requires value that gameplay fits or not).
REST = (0.0, 0)


def inputs_of(pose):
    return {"theta": pose[0]}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose), None)


def turn_poses(step_deg=5.0, contents=(0, 1, 2, 3)):
    """A whole turn of the spindle every step_deg, with each contents."""
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
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, one cell, the controller [0,0,0]: the pan. Worked "
                    "as the quern is, both ways: a vanilla axle in the block below comes up into the pan's down face "
                    "(powerCell, powerFace), or the player holds right-click on it; no work. theta is the spindle's angle, the "
                    "axle's or the hold-to-work clock: the muller turns by it about the pan's axis (y), ratio 1. Every part "
                    "but the contents is always fitted. The contents are requires values: mercury, slurry (the charge) and amalgam, one "
                    "drawn at a time. Nothing goes in or out but by hand: no infeed or output. See the amalgam pan's README for "
                    "the schema.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
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
    ship["powerCell"] = shift_cell(rig["powerCell"], ORIGIN_CELL)
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
    poses = [REST, (0.7, 1), (2.9, 2), (-1.3, 0), (13.0, 3)]
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
                        "at a spread of shaft angles, from the generator's reference maths "
                        "(Machines/tools/machinegen/rigmath.py). The site's and the mod's tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. 'mercury' is drawn as Expanded "
             "Matter draws it (README), 'slurry' is the mod's own texture. Keep element names when editing: the rig finds "
             "its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return (REST, (0.0, 1), (0.0, 2), (0.0, 3), (0.3, 1), (45.0 * DEG, 2), (1.2, 0), (2.0, 3), (22.5 * DEG, 2), (math.pi / 2, 1),
            (1.0, 3))


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
    ap = argparse.ArgumentParser(description="Generate the amalgam pan's shapes, rig, reference poses and slurry texture.")
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
        texture = args.out / "slurry.png"
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
    texture.write_bytes(slurry_texture.png_bytes())
    print(f"wrote {texture} ({texture.stat().st_size} bytes)")
    if not (check_shipped(els, parts, ship_els, ship_parts, ship) and ok):
        sys.exit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
