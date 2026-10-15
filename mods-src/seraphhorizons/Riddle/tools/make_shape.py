#!/usr/bin/env python3
"""Generate the riddle's shape, rig and reference poses.

The riddle is a miner's hand riddle of the 1800s, the hand station of the classify stage of ore processing (epic
#684, gameplay #714, model #715): a square, shallow sieve after the proportions of vanilla's pan (a frame of two
stepped tiers of oak boards, 11.5 across at the top and 2 deep) with a coarse woven mesh of iron wire let into its
lower tier. It rests by its north and south boards on two oak bearers laid across the mouth of a plank box, whose
corners are bound with nailed angle-iron straps; the player lifts it just off the bearers and swirls it, to and fro
and side to side, and the fines fall into the box. One block. (Its stand form is dropped: #714, 2026-10-13.)

A charge of crushed ore (three lumps, riddled in twelve shakes: #714, 2026-10-14) is drawn on the mesh as three
lumps of oversize and one layer of fines over them. As it is riddled the layer shrinks into the mesh and the fines
in the box grow from its bottom (stretching gauges); the lumps, the oversize, are left on the mesh, to be taken out by
hand. The fines in the box are a contents state of their own (`fines`): they stay drawn, whole, once the charge is
taken out, until gameplay takes them off. Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    riddle.json, riddle_frame.json     the riddle, whole, and its static frame              (assets/.../shapes/block/)
    riddle-rig.json                    its cell, anchors, contents, work, pace and rig       (assets/.../config/)
    rig-reference.json                 every part's matrix at a grid of poses               (tests/Riddle/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_riddle.py) and exits non-zero if a
check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the cell's
north-west-bottom corner (the "build frame"); the station is one cell, the controller [0,0,0], so the shipped files
are the build frame divided by 16. The operator stands to the north.

The rig's inputs (README, "Rig schema"):

    theta  the riddling clock, the player's hold-to-work: one turn a shake. Nothing reads theta itself; its travel
           psi (|theta| summed) phases the shake, a gauge's lobes
    W      the rig's work, the riddling of one charge, 0..1
    k      the charge: 0 none, 1 a charge (one class, "thin")
    p      its presence, 0..1 (1 while a charge is on: see the README's "Presence")
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
from machinegen.geometry import IDENT, El, flatten, rot, translate  # noqa: E402
from machinegen.output import (reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               shift_point, worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, progress_of, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_DIR = MOD / "tests" / "Riddle"
SCRIPT = "mods-src/seraphhorizons/Riddle/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
CELLS = (1, 1, 1)                            # one block
ORIGIN_CELL = (0, 0, 0)                      # the controller: the riddle itself
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "planks": "game:block/wood/planks/oak1",
    "iron": "game:block/metal/plate/iron",
    # the charge and the fines: crushed ore wears its ore's nugget texture (itemtypes/oreprocessing/crushedore.json);
    # the renderer sets this code to the riddled ore's
    "ore": "game:item/resource/nugget/hematite",
}

# ---------------------------------------------------------------- the riddle; half widths from its middle, heights from its underside
LOWER_HALF = 5.0                             # the lower tier's outer faces: 10 across (vanilla's pan: 7, 9, 11)
UPPER_HALF = 5.75                            # the upper tier's, stepped out: 11.5 across
BOARD = 1.0                                  # the tiers' boards: 1 thick, 1 deep
RIM_H = 2.0                                  # two tiers: shallow, a sieve and not a box
WIRE = 0.75                                  # the woven mesh: square iron wires, chunky enough to read at block scale,
WIRES = (-3.0, -1.0, 1.0, 3.0)               # 2 apart across the 8 of the opening: openings of 1.25
WIREX_Y = 0.15                               # the wires along x: their underside, clear of the riddle's underside
WIREZ_Y = 0.55                               # the wires along z lie over them, sunk 0.35 into them where they cross (crimped)
WIRE_HALF = LOWER_HALF - BOARD + 0.4         # the wires' ends are let 0.4 into the lower tier's boards
MESH_TOP = WIREZ_Y + WIRE                    # the mesh's top, the charge's bed

# ---------------------------------------------------------------- the charge on the mesh: three lumps (#714)
LUMPS = (                                    # lumps of oversize standing on the mesh, each over a wire along z:
    ((-3.0, -2.4), (1.5, 1.5), 25.0),        #   (x, z) from the middle, (width, height), turned about y (degrees)
    ((3.0, 2.3), (1.35, 1.35), -35.0),
    ((1.0, -3.0), (1.2, 1.3), 10.0),
)
HEAP_SUNK = 0.15                             # the layer of fines settles this far into the mesh: its underside between
HEAP_Y = MESH_TOP - HEAP_SUNK                # the wires' tops and bottoms, in no other face's plane
HEAP = (3.4, 0.9)                            # the charge's one layer of fines: half width, top over the mesh

# ---------------------------------------------------------------- the box: plank walls and a bottom
BOX_HALF = 6.5                               # 13 square outside
WALL = 1.0
BOX_BOTTOM = 1.0                             # the bottom, on the ground inside the walls
BOX_H = 4.4

# ---------------------------------------------------------------- the box's corner irons
# As a period plank box had them (the jig's hutch is the pack's other): an angle-iron strap down each vertical corner,
# wrapping it (a leg flat on each outer face, the legs meeting at the corner in an L), nailed to both boards, so the
# walls are held together at the corner. They stop short of the rim and the ground, under the bearers' height, so
# nothing that moves comes near them.
CSTRAP_T = 0.3                               # the iron's thickness
CSTRAP_W = 1.8                               # each leg's width on its face, from the box's corner: past the end of the wall
                                             # it laps (1 thick) onto the side of the other, so it binds both
CSTRAP_Y = (0.3, BOX_H - 0.3)                # from just off the ground to just under the rim
NAIL = (0.5, 0.12)                           # a nail head's side and how far it stands proud of its leg
NAIL_YS = (0.9, BOX_H - 0.9)                 # the nails down each leg, one low and one high,
NAIL_AT = 1.3                                # this far from the corner: on the west and east legs, into the side wall's face
CORNERS = {                                  # each vertical corner of the box: its (x, z) and which way its faces look
    "nw": ((8.0 - BOX_HALF, 8.0 - BOX_HALF), (-1, -1)),
    "ne": ((8.0 + BOX_HALF, 8.0 - BOX_HALF), (1, -1)),
    "sw": ((8.0 - BOX_HALF, 8.0 + BOX_HALF), (-1, 1)),
    "se": ((8.0 + BOX_HALF, 8.0 + BOX_HALF), (1, 1)),
}

# ---------------------------------------------------------------- the fines in the box, (half width, thickness), grown from the bottom
FINES = ((4.6, 0.75), (3.0, 0.6))            # a layer over most of the bottom and a mound on it, under the mesh

# ---------------------------------------------------------------- the riddle on its bearers
CX, CZ = 8.0, 8.0                            # the box's and the riddle's middle
BEARER = 1.0                                 # two oak bearers across the box's mouth, 1 square,
BEARER_Z = (CZ - LOWER_HALF + BOARD / 2, CZ + LOWER_HALF - BOARD / 2)   # under the riddle's north and south boards
BEARER_X = (0.9, 15.1)                       # their ends past the box's walls
Y0 = BOX_H + BEARER                          # the riddle's underside, on the bearers
LIFT = 0.5                                   # held this far off the bearers while it is shaken
SHAKE_Z = 1.5                                # to and fro (z), as the operator pushes and pulls it
SHAKE_X = 0.75                               # side to side (x), a quarter turn behind: it is swirled
SHAKE_PIVOT_Y = 48.0                         # the shake turns it about axes this high: it moves nearly level

# ---------------------------------------------------------------- the cycle (t = W, one charge)
T = {"shake": (0.04, 0.08, 0.80, 0.84), "fines": (0.10, 0.76)}
SHAKES = 12.0                                # shakes a charge: the pace (#714's starting value, 2026-10-14)
CLASS = "thin"                               # the one charge's class (k 1)


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


def square(cx, cz, half, y0, y1, name, part, tex):
    return box([cx - half, y0, cz - half], [cx + half, y1, cz + half], name, part, tex)


# ---------------------------------------------------------------- builders
def build_riddle():
    """The riddle, its underside at Y0: the frame of oak boards in two stepped tiers (north and south boards across,
    east and west between them) and the woven mesh, iron wires along x under wires along z, crimped into them where
    they cross, their ends let into the lower tier's boards."""
    out = []
    for tier, half, ya in (("lower", LOWER_HALF, 0.0), ("upper", UPPER_HALF, BOARD)):
        inner = half - BOARD
        out.append(box([-half, ya, -half], [half, ya + BOARD, -inner], f"riddle_{tier}_n", "riddle", "oak"))
        out.append(box([-half, ya, inner], [half, ya + BOARD, half], f"riddle_{tier}_s", "riddle", "oak"))
        out.append(box([-half, ya, -inner], [-inner, ya + BOARD, inner], f"riddle_{tier}_w", "riddle", "oak"))
        out.append(box([inner, ya, -inner], [half, ya + BOARD, inner], f"riddle_{tier}_e", "riddle", "oak"))
    for j, off in enumerate(WIRES):
        out.append(box([-WIRE_HALF, WIREX_Y, off - WIRE / 2], [WIRE_HALF, WIREX_Y + WIRE, off + WIRE / 2],
                       f"riddle_wirex_{j + 1}", "riddle", "iron"))
        out.append(box([off - WIRE / 2, WIREZ_Y, -WIRE_HALF], [off + WIRE / 2, WIREZ_Y + WIRE, WIRE_HALF],
                       f"riddle_wirez_{j + 1}", "riddle", "iron"))
    return translate(out, [CX, Y0, CZ])


def build_charge():
    """The charge on the riddle's mesh, as loaded: the three lumps of oversize standing on the mesh, and the layer of
    fines over them, settled into the mesh."""
    m = MESH_TOP
    out = []
    for i, ((x, z), (w, ht), turn) in enumerate(LUMPS):
        lump = square(x, z, w / 2, m, m + ht, f"oversize_lump{i + 1}", "oversize", "ore")
        lump.r = rot("y", turn)
        out.append(lump)
    out.append(square(0.0, 0.0, HEAP[0], HEAP_Y, m + HEAP[1], "heap_1", "heap", "ore"))
    return translate(out, [CX, Y0, CZ])


def build_box():
    """The plank box: north and south walls across, east and west between them, a bottom inside them on the
    ground."""
    h, w = BOX_HALF, WALL
    return [box([CX - h, 0.0, CZ - h], [CX + h, BOX_H, CZ - h + w], "fr_box_wall_n", "frame", "planks"),
            box([CX - h, 0.0, CZ + h - w], [CX + h, BOX_H, CZ + h], "fr_box_wall_s", "frame", "planks"),
            box([CX - h, 0.0, CZ - h + w], [CX - h + w, BOX_H, CZ + h - w], "fr_box_wall_w", "frame", "planks"),
            box([CX + h - w, 0.0, CZ - h + w], [CX + h, BOX_H, CZ + h - w], "fr_box_wall_e", "frame", "planks"),
            box([CX - h + w, 0.0, CZ - h + w], [CX + h - w, BOX_BOTTOM, CZ + h - w], "fr_box_bottom", "frame", "planks")]


def build_corner_straps():
    """The box's corner irons (in the frame, `fr_strap_*`): an angle-iron strap down each vertical corner, a leg on
    each outer face, the leg on the north or south face lapping the other's edge so the two meet at the corner in an
    L; two nail heads on each leg, the north and south legs' into the end walls' faces, the west and east legs' past
    the end walls' ends, into the side walls, so each strap binds both walls of its corner."""
    f = "frame"
    t, w = CSTRAP_T, CSTRAP_W
    y0, y1 = CSTRAP_Y
    ns, proud = NAIL
    out = []

    def nail(name, c, axis, sign):
        lo, hi = [c[k] - ns / 2 for k in range(3)], [c[k] + ns / 2 for k in range(3)]
        lo[axis], hi[axis] = (c[axis], c[axis] + proud) if sign > 0 else (c[axis] - proud, c[axis])
        return box(lo, hi, name, f, "iron")

    for tag, ((xc, zc), (sx, sz)) in CORNERS.items():
        # the leg on the face across x (north or south): from past the corner by the other leg's thickness
        xa, xb = sorted((xc + sx * t, xc - sx * w))
        za, zb = sorted((zc, zc + sz * t))
        out.append(box([xa, y0, za], [xb, y1, zb], f"fr_strap_{tag}_x", f, "iron"))
        # the leg on the face across z (west or east): from the corner along the wall
        xa2, xb2 = sorted((xc, xc + sx * t))
        za2, zb2 = sorted((zc, zc - sz * w))
        out.append(box([xa2, y0, za2], [xb2, y1, zb2], f"fr_strap_{tag}_z", f, "iron"))
        for i, y in enumerate(NAIL_YS, 1):
            out.append(nail(f"fr_strap_{tag}_xnail{i}", [xc - sx * NAIL_AT, y, zc + sz * t], 2, sz))
            out.append(nail(f"fr_strap_{tag}_znail{i}", [xc + sx * t, y, zc - sz * NAIL_AT], 0, sx))
    return out


def build_bearers():
    """The two oak bearers, laid across the box's mouth on its walls."""
    return [box([BEARER_X[0], BOX_H, z - BEARER / 2], [BEARER_X[1], BOX_H + BEARER, z + BEARER / 2], f"fr_bearer_{name}",
                "frame", "oak") for name, z in (("n", BEARER_Z[0]), ("s", BEARER_Z[1]))]


def build_fines():
    """The fines in the box, as they lie once a charge is riddled (they grow to this from nothing, from the box's
    bottom): a layer over most of the bottom and a mound on it, under the mesh."""
    (h1, t1), (h2, t2) = FINES
    return [square(CX, CZ, h1, BOX_BOTTOM, BOX_BOTTOM + t1, "fines_1", "fines", "ore"),
            square(CX, CZ, h2, BOX_BOTTOM + t1, BOX_BOTTOM + t1 + t2, "fines_2", "fines", "ore")]


def build():
    return build_riddle() + build_charge() + build_fines() + build_box() + build_corner_straps() + build_bearers()


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def per_class(v):
    return {CLASS: r6(v)}


WORK = {"name": "riddling", "unit": "charges", "step": 0.005, "end": {CLASS: 1.0}}
FOREVER = 1000.0


def win(t0, t1, t2=None, t3=None):
    """A window rising over t0..t1 and, given t2..t3 (as long), falling over it; else open to the end."""
    ease = t1 - t0
    if t2 is not None:
        assert abs((t3 - t2) - ease) < 1e-9, (t0, t1, t2, t3)
    return {"from": r6(t0), "to": r6(FOREVER if t3 is None else t3), "ease": r6(ease)}


def slide(axis, dist, wins):
    return {"type": "gauge", "motion": "slide", "axis": axis, "amount": per_class(dist / B), "windows": wins}


def shake(axis, pivot, amplitude, phase, t):
    """A turn about `axis` through `pivot` (blocks) by e * amplitude * cos(psi + phase): the shake, eased in and out
    with the shaking window, its phase the riddling clock's travel."""
    return {"type": "gauge", "motion": "rotate", "axis": axis, "pivot": pivot, "amount": per_class(0.0),
            "windows": [win(*t)], "lobes": {"ratio": 1.0, "phase": r6(phase), "amplitude": per_class(amplitude)}}


def shrink(y, t):
    """A layer shrinking to nothing along y onto the plane at height y (voxels) over the window t = (a, b), and
    staying gone (the stretching gauge's recipe, docs/recipe-browser/models.md)."""
    a, b = t
    return {"type": "gauge", "motion": "stretch", "axis": "y", "origin": pt(CX, y, CZ), "amount": per_class(-1.0),
            "windows": [{"from": r6(a), "to": r6(FOREVER), "ease": r6(b - a)}]}


def grow(y, t):
    """Fines growing from nothing along y from the plane at height y over t = (a, b), and staying whole. With no
    charge on (k 0) a gauge does nothing, so they are drawn whole: the box keeps its fines."""
    a, b = t
    return {"type": "gauge", "motion": "stretch", "axis": "y", "origin": pt(CX, y, CZ), "amount": per_class(-1.0),
            "windows": [{"from": r6(-FOREVER), "to": r6(b), "ease": r6(b - a)}]}


def amplitudes():
    """The shake: the two turns (radians) that move the riddle's middle SHAKE_Z to and fro and SHAKE_X side to
    side."""
    d = SHAKE_PIVOT_Y - (Y0 + LIFT + RIM_H / 2)
    return math.asin(SHAKE_Z / d), math.asin(SHAKE_X / d)


def rig_parts():
    az, ax = amplitudes()
    piv = pt(CX, SHAKE_PIVOT_Y, CZ)
    parts = [{"id": "riddle", "match": ["riddle_*"], "requires": "riddle",
              "drivers": [slide("y", LIFT, [win(*T["shake"])]),
                          shake("x", piv, az, -math.pi / 2, T["shake"]),
                          shake("z", piv, ax, 0.0, T["shake"])]},
             # the charge rides the riddle: the lumps as they are, the layer of fines shrinking into the mesh
             {"id": "oversize", "match": ["oversize_*"], "requires": "charge", "ride": "riddle"},
             {"id": "heap", "match": ["heap_*"], "requires": "charge", "ride": "riddle",
              "drivers": [shrink(Y0 + HEAP_Y, T["fines"])]},
             # the box's contents: the fines, growing from its bottom as they fall through the mesh
             {"id": "fines", "match": ["fines_*"], "requires": "fines", "drivers": [grow(BOX_BOTTOM, T["fines"])]},
             {"id": "frame", "match": ["fr_*"], "requires": None}]
    out = []
    for p in parts:
        drivers = p.get("drivers", [])
        for d in drivers:
            validate_driver(d)
        out.append({"id": p["id"], "match": p["match"], "requires": p.get("requires"), "ride": p.get("ride"), "drivers": drivers})
    return out


# ---------------------------------------------------------------- poses
REST = (0.0, 0.0, 0, 0.0)                     # (theta, W, k, p): no charge; the authored pose


def inputs_of(pose):
    th, W, k, p = pose
    return {"theta": th, "work": W, "size": k, "presence": p}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path or WORK)


def pose_at(W, theta=None, p=1.0):
    """A charge at W, the riddling clock at the pace (theta >= 0, so psi = theta)."""
    th = TAU * SHAKES * W if theta is None else theta
    return (th, W, 1, p)


CHARGE_PARTS = ("oversize", "heap")


def on_show(part, k):
    """Whether a part can be seen with charge k: the charge only while one is on; the fines (when fitted) always."""
    return k == 1 if part in CHARGE_PARTS else True


def shown(posed_els, pose):
    """The posed elements as they can be seen: the charge, with none on, moved far away (copies, in order)."""
    k = pose[2]
    out = []
    for e in posed_els:
        if not on_show(e.part, k):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def coplanar_poses():
    q = math.pi / 2
    return (REST, pose_at(0.0), pose_at(0.3, q), pose_at(0.25, 3 * q), pose_at(0.5, 0.7), pose_at(0.55, 2 * q),
            pose_at(0.6, q), pose_at(0.9), pose_at(1.0))


def fix_coplanar(els, parts):
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose),
                              coplanar_poses())


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS[0]) for y in range(CELLS[1]) for z in range(CELLS[2])]


ANCHORS = ("fines", "charge")
# A hand station takes no automated input or output (the owner's ruling): the player loads the charge and takes the
# fines and the oversize out by hand, so the rig has no infeed or output side, face or cell for a chute or a hopper,
# only the side the operator stands on.
NO_TRANSFER = ("infeedSide", "outputSide", "infeedFace", "outputFace", "infeedCell", "outputCell", "output", "infeed")


def make_rig(parts):
    progress_of({"work": WORK})
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, the controller cell [0,0,0]; the operator stands to "
                    "the north. The hand riddle: it rests on two bearers over the box and is lifted just off them and "
                    "swirled while a charge is riddled. A hand station: no power cell, and no automated input or output "
                    "(no infeed or output side for chutes or hoppers: the player loads and unloads it by hand). work is "
                    "the riddling of one charge, W 0..1; k is the charge (1 a charge, 0 none); gauge windows are placed in "
                    "charges. theta is the riddling clock, the player's hold-to-work, one turn a shake: nothing reads it "
                    "but the shake's lobes, through its travel psi. The fines fall into the box (fines.pos) and the "
                    "oversize stays in the riddle (charge.pos), both taken out by hand. See the riddle's README for the "
                    "schema.",
        "cells": [],
        "operatorSide": "north",
        "fines": {"pos": pt(CX, BOX_BOTTOM + FINES[0][1] + FINES[1][1], CZ)},
        "charge": {"pos": pt(CX, Y0 + MESH_TOP + HEAP[1] / 2, CZ)},
        "contents": {"charge": "charge", "fines": "fines",
                     "_comment": "requires values for what the riddle holds: charge (the lumps and the layer of fines on "
                                 "the mesh) while a charge is on, at k 1; fines (the fines in the box) while the box holds "
                                 "fines, from the charge's loading until the player takes them out. While a charge is "
                                 "riddled the fines grow from nothing; with no charge on they are drawn whole."},
        "work": dict(WORK),
        "riddling": {"lumpsPerCharge": len(LUMPS), "shakesPerCharge": per_class(SHAKES),
                     "_comment": "lumpsPerCharge: a charge's crushed ore, the lumps drawn on the mesh; shakesPerCharge: "
                                 "the pace, shakes (theta / 2 pi) a charge. #714's starting values (2026-10-14)."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: the build frame's corner is the
    controller's). Gauge windows are in charges, so they stay as they are."""
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
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts, sp):
    """The cell's boxes from the shipped shape as written, posed at rest by the shipped rig; then the lid. Each box is
    clipped to its cell."""
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
    poses = [REST, pose_at(0.2), pose_at(0.45), pose_at(0.7), pose_at(0.85), pose_at(0.99), (1.3, 0.3, 1, 0.6)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose), sp), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells {cells}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
REF_EDGES = (0.0, 0.03, 0.06, 0.09, 0.14, 0.24, 0.33, 0.4, 0.45, 0.55, 0.65, 0.72, 0.78, 0.82, 0.86, 0.93, 1.0)
REF_THETAS = (0.0, 1.1, 2.3, 2.9, 4.4, 5.6)


def reference_poses():
    """theta in {0, 1.1, 2.3, 2.9} with no charge; W over the cycle's edges with p 1 and theta cycling through
    REF_THETAS (psi = theta: the shake's phase), and every fourth also at p 0.4; and a charge's edges again with theta
    a quarter turn on."""
    out = [(th, 0.0, 0, 0.0) for th in REF_THETAS[:4]]
    for i, W in enumerate(REF_EDGES):
        th = REF_THETAS[(i + 2) % len(REF_THETAS)]
        for p in ((1.0,) if i % 4 else (1.0, 0.4)):
            out.append((th, W, 1, p))
    for i, W in enumerate(REF_EDGES):
        out.append((REF_THETAS[i % len(REF_THETAS)] + math.pi / 2, W, 1, 1.0))
    return out


def reference_json(ship_parts, sp):
    poses = []
    for pose in reference_poses():
        th, W, k, p = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose), sp)) for q in ship_parts}
        poses.append({"theta": r6(th), "work": W, "size": k, "presence": p, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped riddle-rig.json's parts and work: each part's matrix as 3 "
                        "rows of 4 (block units) at each pose (W in charges; travel is |theta|, the shake's phase). The "
                        "site's, the mod's and the Python tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}: the hand riddle, the riddle on its bearers over the fines box. Every element was "
             "made for the Seraph Horizons mod. The charge's and the fines' texture code is 'ore': the renderer sets it "
             "to the crushed ore's texture. Keep element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


FILES = ("riddle.json", "riddle_frame.json", "riddle-rig.json", "rig-reference.json")


def outputs(out_dir):
    shape, frame, rig, ref = FILES
    if out_dir:
        return out_dir / shape, out_dir / frame, out_dir / rig, out_dir / ref
    return SHAPE_DIR / shape, SHAPE_DIR / frame, RIG_DIR / rig, REFERENCE_DIR / ref


def main():
    ap = argparse.ArgumentParser(description="Generate the riddle's shape, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_riddle
    els = build()
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        print(f"coplanar faces before the fix: {sum(len(v) for v in before.values())} pairs over {len(before)} poses; "
              f"{hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_riddle.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape = shape_json(ship_els)
    frame_shape = shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts, ship["work"])
    ok = validate_riddle.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship),
             reference_dumps(reference_json(ship_parts, ship["work"])))
    for path, text in zip(outputs(args.out), texts):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        json.loads(path.read_text())
        print(f"wrote {path} ({path.stat().st_size // 1024} KiB)")
    ok = check_shipped(els, parts, ship_els, ship_parts, ship) and ok
    if not ok:
        sys.exit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
