#!/usr/bin/env python3
"""Generate the riddle's shapes, rig and reference poses.

The riddle is a miner's riddle of the 1800s, the hand station of the classify stage of ore processing: a
square, shallow sieve after the proportions of vanilla's pan (a frame of two stepped tiers of oak boards,
11.5 across at the top and 2 deep) with a coarse woven mesh of iron wire let into its lower tier. It rests
by its north and south boards on two oak bearers laid across the mouth of a plank box, one block in all.
The player shakes it, lifted just clear of the bearers, to and fro and side to side, and the fines fall
through into the box; the oversize stays in the riddle, to be taken out by hand.

A charge of crushed ore is drawn as a heap on the mesh: an oversize bed and one (a part charge) or two (a
full charge) layers of fines over it. As the charge is riddled each layer of fines sinks into the one under
it and the fines rise in the box, layer by layer, out of its bottom; the bed is left in the riddle when the
charge is done. Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    riddle.json            the whole station, every moving part and both charges   (assets/.../shapes/block/)
    riddle_frame.json      its static frame only (the box and the bearers)          (assets/.../shapes/block/)
    riddle-rig.json        the cell, anchors, work, pace and part rig               (assets/.../config/)
    rig-reference.json     every part's matrix at a grid of poses                   (tests/Riddle/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_riddle.py) and exits non-zero
if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
cell's north-west-bottom corner (the "build frame"); the riddle is one cell, the controller [0,0,0], so the
shipped files are the build frame divided by 16. The operator stands to the north.

The rig's inputs, as this station uses them (README, "Rig schema"):

    theta  the riddling clock, the player's hold-to-work: one turn a shake. Nothing reads theta itself; its
           travel psi (|theta| summed) phases the shake, a gauge's lobes
    W      the rig's work, the riddling of one charge, 0..1
    k      the charge: 0 none, 1 a part charge (thin), 2 a full charge (thick)
    p      its presence, 0..1, eased in as the charge goes on and out as it is taken away
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
    "ore": "game:block/stone/gravel/granite",   # the charge: the renderer sets it to the crushed ore's texture
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

# ---------------------------------------------------------------- the charge: square layers on the mesh, (half width, thickness)
BASE = (2.5, 0.8)                            # the oversize bed; the mesh shows round it
LUMPS = (                                    # lumps of oversize on the bed, standing out of the fines from the start:
    ((-1.25, 1.0), (1.6, 1.3), 25.0),        #   (x, z) from the middle, (width, height), turned about y (degrees)
    ((1.15, -1.05), (1.35, 1.1), -35.0),
)
LUMP_SUNK = 0.3                              # their feet sunk this far into the bed
MID = (2.3, 0.55)                            # a full charge's lower layer of fines
TOP = (1.4, 0.4)                             # a full charge's top
TOP1 = (2.2, 0.55)                           # a part charge's one layer of fines
MID_SINK = 0.675                             # into the bed: 0.125 inside it above and below
TOP_SINK = 0.475                             # into the lower layer (and with it into the bed)
TOP1_SINK = 0.675
HIDE_MARGIN = 0.05                           # a sunk layer stays at least this far inside what hides it

# ---------------------------------------------------------------- the box: plank walls and a bottom
BOX_HALF = 6.5                               # 13 square outside
WALL = 1.0
BOX_BOTTOM = 1.0                             # the bottom, on the ground inside the walls
BOX_H = 4.4

# ---------------------------------------------------------------- the fines in the box, (half width, thickness)
F1 = (4.6, 0.75)                             # a full charge's first layer of fines
F2 = (3.0, 0.6)                              # and its second, on the first
FS = (4.0, 0.75)                             # a part charge's one layer
F_HIDDEN = 0.1                               # hidden in the box's bottom (its underside), until it rises
F2_HIDDEN = 0.175                            # hidden in the first layer
F1_RISE = BOX_BOTTOM - F_HIDDEN              # onto the bottom
F2_RISE = (F_HIDDEN + F1[1]) - F2_HIDDEN     # onto the first layer

# ---------------------------------------------------------------- the station
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
T = {"shake": (0.04, 0.08, 0.80, 0.84), "top": (0.10, 0.38), "mid": (0.42, 0.76), "top1": (0.10, 0.76)}
SHAKES = {"thin": 12.0, "thick": 20.0}       # shakes a charge: the pace (provisional, the gameplay's)

CLASSES = (("thin", "c1", "chargesmall"), ("thick", "c2", "chargefull"))


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
    """The riddle, its underside on the bearers: the frame of oak boards in two stepped tiers (north and
    south boards across, east and west between them) and the woven mesh, iron wires along x under wires
    along z, crimped into them where they cross, their ends let into the lower tier's boards."""
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
    """The charge on the riddle's mesh: per class the oversize bed and its fines over it, as loaded."""
    m = MESH_TOP
    b = m + BASE[1]
    out = []
    for _c, pre, _r in CLASSES:
        out.append(square(0.0, 0.0, BASE[0], m, b, f"{pre}base_bed", f"{pre}base", "ore"))
        for i, ((x, z), (w, h), turn) in enumerate(LUMPS):
            lump = square(x, z, w / 2, b - LUMP_SUNK, b - LUMP_SUNK + h, f"{pre}base_lump{i + 1}", f"{pre}base", "ore")
            lump.r = rot("y", turn)
            out.append(lump)
    out.append(square(0.0, 0.0, TOP1[0], b, b + TOP1[1], "c1top_1", "c1top", "ore"))
    out.append(square(0.0, 0.0, MID[0], b, b + MID[1], "c2mid_1", "c2mid", "ore"))
    out.append(square(0.0, 0.0, TOP[0], b + MID[1], b + MID[1] + TOP[1], "c2top_1", "c2top", "ore"))
    return translate(out, [CX, Y0, CZ])


def build_box():
    """The plank box: north and south walls across, east and west between them, a bottom inside them on the
    ground; and the two oak bearers laid across its mouth."""
    h, w = BOX_HALF, WALL
    out = [box([CX - h, 0.0, CZ - h], [CX + h, BOX_H, CZ - h + w], "fr_box_wall_n", "frame", "planks"),
           box([CX - h, 0.0, CZ + h - w], [CX + h, BOX_H, CZ + h], "fr_box_wall_s", "frame", "planks"),
           box([CX - h, 0.0, CZ - h + w], [CX - h + w, BOX_H, CZ + h - w], "fr_box_wall_w", "frame", "planks"),
           box([CX + h - w, 0.0, CZ - h + w], [CX + h, BOX_H, CZ + h - w], "fr_box_wall_e", "frame", "planks"),
           box([CX - h + w, 0.0, CZ - h + w], [CX + h - w, BOX_BOTTOM, CZ + h - w], "fr_box_bottom", "frame", "planks")]
    for name, z in (("n", BEARER_Z[0]), ("s", BEARER_Z[1])):
        out.append(box([BEARER_X[0], BOX_H, z - BEARER / 2], [BEARER_X[1], BOX_H + BEARER, z + BEARER / 2],
                       f"fr_bearer_{name}", "frame", "oak"))
    return out


def build_fines():
    """The fines in the box, hidden in its bottom (and the second layer in the first) until they rise."""
    return [square(CX, CZ, FS[0], F_HIDDEN, F_HIDDEN + FS[1], "c1fines_1", "c1fines", "ore"),
            square(CX, CZ, F1[0], F_HIDDEN, F_HIDDEN + F1[1], "c2fines1_1", "c2fines1", "ore"),
            square(CX, CZ, F2[0], F2_HIDDEN, F2_HIDDEN + F2[1], "c2fines2_1", "c2fines2", "ore")]


def build():
    return build_riddle() + build_charge() + build_fines() + build_box()


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def per_class(thin, thick=None):
    return {"thin": r6(thin), "thick": r6(thin if thick is None else thick)}


WORK = {"name": "riddling", "unit": "charges", "step": 0.005, "end": {"thin": 1.0, "thick": 1.0}}
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
    """A turn about `axis` through `pivot` (blocks) by e * amplitude * cos(psi + phase): the shake, eased in
    and out with the shaking window, its phase the riddling clock's travel."""
    return {"type": "gauge", "motion": "rotate", "axis": axis, "pivot": pivot, "amount": per_class(0.0),
            "windows": [win(*t)], "lobes": {"ratio": 1.0, "phase": r6(phase), "amplitude": per_class(amplitude)}}


def amplitudes():
    """The shake's two turns (radians) that move the riddle's middle SHAKE_Z to and fro and SHAKE_X side to side."""
    d = SHAKE_PIVOT_Y - (Y0 + LIFT + RIM_H / 2)
    return math.asin(SHAKE_Z / d), math.asin(SHAKE_X / d)


def rig_parts():
    az, ax = amplitudes()
    piv = pt(CX, SHAKE_PIVOT_Y, CZ)
    parts = [{"id": "riddle", "match": ["riddle_*"], "requires": "riddle",
              "drivers": [slide("y", LIFT, [win(*T["shake"])]),
                          shake("x", piv, az, -math.pi / 2, T["shake"]),
                          shake("z", piv, ax, 0.0, T["shake"])]}]
    # the beds ride the riddle and carry their layers of fines, each sinking into the one under it
    for _cls, pre, req in CLASSES:
        parts.append({"id": f"{pre}base", "match": [f"{pre}base_*"], "requires": req, "ride": "riddle"})
    parts += [{"id": "c1top", "match": ["c1top_*"], "requires": "chargesmall", "ride": "c1base",
               "drivers": [slide("y", -TOP1_SINK, [win(*T["top1"])])]},
              {"id": "c2mid", "match": ["c2mid_*"], "requires": "chargefull", "ride": "c2base",
               "drivers": [slide("y", -MID_SINK, [win(*T["mid"])])]},
              {"id": "c2top", "match": ["c2top_*"], "requires": "chargefull", "ride": "c2mid",
               "drivers": [slide("y", -TOP_SINK, [win(*T["top"])])]}]
    # the fines in the box, rising out of its bottom as they fall through
    parts += [{"id": "c1fines", "match": ["c1fines_*"], "requires": "chargesmall", "drivers": [slide("y", F1_RISE, [win(*T["top1"])])]},
              {"id": "c2fines1", "match": ["c2fines1_*"], "requires": "chargefull", "drivers": [slide("y", F1_RISE, [win(*T["top"])])]},
              {"id": "c2fines2", "match": ["c2fines2_*"], "requires": "chargefull", "ride": "c2fines1",
               "drivers": [slide("y", F2_RISE, [win(*T["mid"])])]}]
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None})
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


def pose_at(k, W, theta=None, p=1.0):
    """A charge of class k at W, the riddling clock at the pace (theta >= 0, so psi = theta)."""
    th = TAU * SHAKES[("thin", "thick")[k - 1]] * W if theta is None else theta
    return (th, W, k, p)


def on_show(part, k):
    """Whether a part can be seen with charge k on: each class's charge and fines only with that class."""
    if part.startswith("c1"):
        return k == 1
    if part.startswith("c2"):
        return k == 2
    return True


def shown(posed_els, pose):
    """The posed elements as they can be seen: the other class's charge moved far away (copies, in order)."""
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
    return (REST, pose_at(1, 0.0), pose_at(2, 0.0), pose_at(1, 0.3, q), pose_at(2, 0.25, 3 * q), pose_at(2, 0.5, 0.7),
            pose_at(1, 0.55, 2 * q), pose_at(2, 0.6, q), pose_at(2, 1.0), pose_at(1, 0.9))


def fix_coplanar(els, parts):
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose),
                              coplanar_poses())


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS[0]) for y in range(CELLS[1]) for z in range(CELLS[2])]


ANCHORS = ("output", "charge")


def make_rig(parts):
    progress_of({"work": WORK})
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, the controller cell [0,0,0]; the operator stands to "
                    "the north. A hand station: no power cell. work is the riddling of one charge, W 0..1; k is the charge "
                    "(1 a part charge, 2 a full charge); gauge windows are placed in charges. theta is the riddling clock, "
                    "the player's hold-to-work, one turn a shake: nothing reads it but the shake's lobes, through its travel "
                    "psi. The riddle rests on bearers over the box and is shaken lifted just off them; the fines fall into the "
                    "box and the oversize stays in the riddle, taken out by hand. See the riddle's README for the schema.",
        "cells": [],
        "operatorSide": "north",
        "infeedSide": "east",
        "outputSide": "south",
        "output": {"pos": pt(CX, BOX_BOTTOM + F1[1] + F2[1], CZ)},
        "charge": {"pos": pt(CX, Y0 + MESH_TOP + BASE[1] / 2, CZ)},
        "work": dict(WORK),
        "riddling": {"shakesPerCharge": per_class(SHAKES["thin"], SHAKES["thick"]),
                     "_comment": "shakesPerCharge: the pace, shakes (theta / 2 pi) a charge; a full charge takes longer. "
                                 "Provisional: the gameplay (#714) sets it."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: the build frame's corner
    is the controller's). Gauge windows are in charges, so they stay as they are."""
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
    """The cell's boxes from the shipped shape as written, posed at rest by the shipped rig; then the lid.
    Each box is clipped to its cell."""
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
    poses = [REST, pose_at(1, 0.2), pose_at(2, 0.45), pose_at(1, 0.7), pose_at(2, 0.85), pose_at(2, 0.99), (1.3, 0.3, 1, 0.6)]
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
    """theta in {0, 1.1, 2.3, 2.9} with no charge; for each class, W over the cycle's edges with p 1 and
    theta cycling through REF_THETAS (psi = theta: the shake's phase), and every fourth also at p 0.4."""
    out = [(th, 0.0, 0, 0.0) for th in REF_THETAS[:4]]
    for k in (1, 2):
        for i, W in enumerate(REF_EDGES):
            th = REF_THETAS[(i + 2 * k) % len(REF_THETAS)]
            for p in ((1.0,) if i % 4 else (1.0, 0.4)):
                out.append((th, W, k, p))
    return out


def reference_json(ship_parts, sp):
    poses = []
    for pose in reference_poses():
        th, W, k, p = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose), sp)) for q in ship_parts}
        poses.append({"theta": th, "work": W, "size": k, "presence": p, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped riddle-rig.json's parts and work: each part's matrix as "
                        "3 rows of 4 (block units) at each pose (W in charges; travel is |theta|, the shake's phase). The "
                        "site's, the mod's and the Python tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}: the riddle. Every element was made for the Seraph Horizons mod. The charge's "
             "texture code is 'ore': the renderer sets it to the crushed ore's texture. Keep element names when editing: "
             "the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def main():
    ap = argparse.ArgumentParser(description="Generate the riddle's shapes, rig and reference poses.")
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
    if args.out:
        outs = (args.out / "riddle.json", args.out / "riddle_frame.json", args.out / "riddle-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "riddle.json", SHAPE_DIR / "riddle_frame.json", RIG_DIR / "riddle-rig.json", REFERENCE_DIR / "rig-reference.json")
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts, ship["work"])
    ok = validate_riddle.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship), reference_dumps(reference_json(ship_parts, ship["work"])))
    for path, text in zip(outs, texts):
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
