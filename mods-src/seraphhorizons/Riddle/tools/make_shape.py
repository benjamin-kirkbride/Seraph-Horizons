#!/usr/bin/env python3
"""Generate the riddle's shapes, rigs and reference poses: the hand riddle and the riddle on its stand.

The riddle is a miner's riddle of the 1800s, the hand tier of classifying crushed ore: a square, shallow
sieve after the proportions of vanilla's pan (a frame of two stepped tiers of oak boards, 11.5 across at the
top and 2 deep) with a coarse woven mesh of iron wire let into its lower tier. Two models share it, element
for element:

  * the hand riddle, one block: a square plank box with iron corners and two oak bearers laid across its
    mouth, and the riddle on them. The player shakes it, lifted just clear of the bearers, to and fro and
    side to side, and the fines fall through into the box;
  * the riddle on its stand, tier 1, two blocks long: a light oak stand over the same box (the fines box)
    and, in the second block, a low box of the same make for the oversize. The riddle hangs at its north end
    from two iron hangers and rests near its south end on an oak roller across the stand. A long hand lever
    on the operator's right, pivoted on the side rail and rising above the block, swings the hangers through
    an iron link (lever, link and hanger are a parallelogram): worked to and fro it riddles the charge;
    pulled right back it swings the riddle out over the roller, which tips it into the second block, and the
    oversize goes off its far end into the oversize box.

A charge of crushed ore is drawn as a heap on the mesh: an oversize bed and one (a part charge) or two (a
full charge) layers of fines over it. As the charge is riddled each layer of fines sinks into the one under
it and the fines rise in the box, layer by layer, out of its bottom. By hand the bed is left in the riddle
when the charge is done; on the stand it is tipped off into the oversize box. Everything is built here from
plain boxes; no other mod's model is used.

It writes, deterministically,

    riddle.json                    the hand riddle, every moving part         (assets/.../shapes/block/)
    riddle_frame.json              its static frame only (the box, bearers)   (assets/.../shapes/block/)
    riddlestand.json               the riddle on its stand, every moving part (assets/.../shapes/block/)
    riddlestand_frame.json         the stand's static frame only              (assets/.../shapes/block/)
    riddle-rig.json                the hand riddle's cells, anchors and rig   (assets/.../config/)
    riddlestand-rig.json           the stand's                                (assets/.../config/)
    rig-reference.json             the hand riddle's parts' matrices at a grid of poses  (tests/Riddle/)
    stand-rig-reference.json       the stand's                                           (tests/Riddle/)

or, with `--out DIR`, all eight into DIR. It validates its own output (validate_riddle.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
controller cell's north-west-bottom corner (the "build frame"), so the shipped files are the build frame
divided by 16. The operator stands to the north; the stand runs south from the controller.

The rigs' inputs, as these models use them (README, "Rig schema"):

    theta  the riddling clock, the player's hold-to-work: one turn a shake. Nothing reads theta itself; its
           travel psi (|theta| summed) phases the shake, a gauge's lobes
    W      the rig's work, the riddling of one charge, 0..1
    k      the charge: 0 none, 1 a part charge (thin), 2 a full charge (thick)
    p      its presence, 0..1, eased in as the charge goes on and out after it is delivered
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
from machinegen.rigmath import about, apply, part_of, posed, progress_of, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_DIR = MOD / "tests" / "Riddle"
SCRIPT = "mods-src/seraphhorizons/Riddle/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
DEG = math.pi / 180
ORIGIN_CELL = (0, 0, 0)                      # the controller: the hand riddle's one cell, the stand's fines end
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "planks": "game:block/wood/planks/oak1",
    "iron": "game:block/metal/plate/iron",
    "ore": "game:block/stone/gravel/granite",   # the charge: the renderer sets it to the crushed ore's texture
}

# ---------------------------------------------------------------- the riddle (both models); half widths from its middle, heights from its underside
LOWER_HALF = 5.0                             # the lower tier's outer faces: 10 across (vanilla's pan: 7, 9, 11)
UPPER_HALF = 5.75                            # the upper tier's, stepped out: 11.5 across
BOARD = 1.0                                  # the tiers' boards: 1 thick, 1 deep
RIM_H = 2.0                                  # two tiers: shallow, a sieve and not a box
WIRE = 0.25                                  # the woven mesh: square iron wires,
WIRES = tuple(i - 3.5 for i in range(8))     # a voxel apart: openings of 0.75
WIRE_Y = 0.2                                 # the lower wires' underside; the upper wires cross on them
WIRE_HALF = LOWER_HALF - BOARD + 0.4         # the wires run into the lower tier's boards
MESH_TOP = WIRE_Y + 2 * WIRE                 # the mesh's top, the charge's bed

# ---------------------------------------------------------------- the charge: square layers on the mesh, (half width, thickness)
BASE = (3.0, 0.7)                            # the oversize bed; the mesh shows round it
MID = (2.3, 0.5)                             # a full charge's lower layer of fines
TOP = (1.4, 0.36)                            # a full charge's top
TOP1 = (2.0, 0.5)                            # a part charge's one layer of fines
MID_SINK = 0.6                               # into the bed: its bottom 0.1 over the bed's
TOP_SINK = 0.43                              # into the lower layer (and with it into the bed)
TOP1_SINK = 0.6
HIDE_MARGIN = 0.05                           # a sunk layer stays this far inside what hides it

# ---------------------------------------------------------------- the boxes: plank walls, a bottom, iron corners
BOX_HALF = 6.5                               # 13 square outside
WALL = 0.75
BOX_BOTTOM = 1.0                             # the bottom, on the ground inside the walls
FINES_H = 4.4                                # the fines box (both models)
OVERSIZE_H = 2.5                             # the stand's oversize box, the same make, low
OVERSIZE_HALF_Z = 6.25
CORNER = (1.0, 0.12)                         # the iron corners: each leg's width, thickness

# ---------------------------------------------------------------- the fines in the box, (half width, thickness)
F1 = (4.6, 0.75)                             # a full charge's first layer of fines
F2 = (3.0, 0.6)                              # and its second, on the first
FS = (4.0, 0.75)                             # a part charge's one layer
F_HIDDEN = 0.1                               # hidden in the box's bottom (its underside), until it rises
F2_HIDDEN = 0.175                            # hidden in the first layer
F1_RISE = BOX_BOTTOM - F_HIDDEN              # onto the bottom
F2_RISE = (F_HIDDEN + F1[1]) - F2_HIDDEN     # onto the first layer

# ---------------------------------------------------------------- the hand riddle
HAND_C = (8.0, 8.0)                          # (x, z): the box's and the riddle's middle
BEARER_Z = (HAND_C[1] - 3.5, HAND_C[1] + 3.5)   # two oak bearers across the box's mouth, under the riddle's side boards
BEARER_W, BEARER_H = 0.9, 0.8
BEARER_X = (0.9, 15.1)
HAND_Y0 = FINES_H + BEARER_H                 # the riddle's underside, on the bearers
LIFT = 0.4                                   # held this far off the bearers while it is shaken
SHAKE_Z = 1.2                                # to and fro (z), as the operator pushes and pulls it
SHAKE_X = 0.6                                # side to side (x), a quarter turn behind: it is swirled
SHAKE_PIVOT_Y = 40.0                         # the shake turns it about axes this high: it moves nearly level

# ---------------------------------------------------------------- the stand: 1 wide, 2 long (z), the controller the fines end
STAND_CELLS = (1, 1, 2)
FINES_C = (8.0, 9.0)                         # the fines box: z 2.5 .. 15.5
OVERSIZE_C = (8.0, 23.25)                    # the oversize box: z 17 .. 29.5
STAND_C = (8.0, 10.25)                       # the riddle's middle at rest
STAND_Y0 = 5.5                               # its underside, on the roller
LEG = 1.2                                    # oak legs at the four corners and at the roller
MID_LEG_Z = (14.15, 15.35)
RAIL_Y = (14.6, 16.0)                        # the top rails: east and west (along z), north and south (along x)
STRETCH_Y = (1.4, 2.6)                       # the low stretchers, all round
ROLLER = (5.0, 14.75, 0.5)                   # (y, z, radius): the oak roller the riddle rests on, in iron bearings on the middle legs
PIVOT_Y = 15.25                              # the hangers' and the lever's pivots, in the side rails
HANG_Z = STAND_C[1] - UPPER_HALF + 0.5       # the hangers carry the riddle's north end
HANG_Y = STAND_Y0 + RIM_H                    # on pins through lugs at its rim's top
HANGER_L = PIVOT_Y - HANG_Y
HANGER_X = (LEG + 0.15, LEG + 0.55)          # the west hanger's strap (mirrored east), inside its rail
BRACKET_X = (LEG, HANGER_X[0])               # iron plates on the rails' inner faces round the pivots
STRAP_W = 0.8
PIN_R = 0.25
LUG_X = (HANGER_X[1], STAND_C[0] - UPPER_HALF + 0.05)
LUG_Y = (HANG_Y - 0.6, HANG_Y + 0.6)
LEVER_Z = 8.0                                # the hand lever's pivot, on the west rail, south of the hanger
LEVER_TOP = 21.25                            # its grip: above the block, where the player's hand is
ARM = 3.0                                    # the lever's arm below its pivot = the hanger's link pin under its pivot: a parallelogram
LINK_X = (HANGER_X[1], HANGER_X[1] + 0.4)    # the iron link, inside the lever and the hanger
LINK_PIN_Y = PIVOT_Y - ARM
LINK_PIN_R = 0.22
RIDDLE_SWING = 1.5                           # riddling: to and fro on the hangers
SWING_OUT = 60.0                             # degrees: pulled right back, the hangers swing this far south
KNOTS = 10                                   # the swing out is drawn in this many straight steps, the tip fitted at each

# ---------------------------------------------------------------- the cycles (t = W, one charge)
HAND_T = {"shake": (0.04, 0.08, 0.80, 0.84), "top": (0.10, 0.38), "mid": (0.42, 0.76), "top1": (0.10, 0.76)}
STAND_T = {"shake": (0.04, 0.08, 0.62, 0.66), "top": (0.10, 0.34), "mid": (0.38, 0.60), "top1": (0.10, 0.60),
           "out": 0.68, "step": 0.012, "back": 0.88,     # the swing out in KNOTS steps from 0.68; back from 0.88 to 1
           "lift": (0.80, 0.82), "away": (0.82, 0.85), "level": (0.85, 0.865), "down": (0.865, 0.88)}

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


def pin_x(x0, x1, y, z, r, name, part, tex="iron"):
    """A round pin along x: two square strips, turned 0 and 45 degrees about x."""
    out = []
    for i in range(2):
        el = box([x0 + 0.01 * i, y - r, z - r], [x1 - 0.01 * i, y + r, z + r], f"{name}_{i + 1}", part, tex)
        if i:
            el.r = rot("x", 45.0)
        out.append(el)
    return out


def round_x(x0, x1, y, z, r, name, part, tex):
    """A round bar along x: a regular octagon of apothem r, four strips turned 0, 45, 90 and 135 degrees
    about x (each a hair shorter than the last so no two ends share a plane): its top is r over its axis."""
    half = r * math.tan(math.pi / 8)
    out = []
    for i in range(4):
        el = box([x0 + 0.012 * i, y - half, z - r], [x1 - 0.012 * i, y + half, z + r], f"{name}_{i + 1}", part, tex)
        if i:
            el.r = rot("x", 45.0 * i if i < 3 else -45.0)
        out.append(el)
    return out


def mirror_x(x0, x1):
    return (16.0 - x1, 16.0 - x0)


def sides():
    """The west and east (name suffix, x mapper) pairs: both models are symmetric about x 8 but for the lever."""
    return (("w", lambda a, b: (a, b)), ("e", mirror_x))


# ---------------------------------------------------------------- builders shared by both models
def build_riddle(cx, y0, cz):
    """The riddle, its underside at y0, its middle at (cx, cz): the frame of oak boards in two stepped tiers
    (north and south boards across, east and west between them) and the woven mesh, iron wires along x under
    wires along z, crossing on them, their ends let into the lower tier's boards. Built at the origin and
    moved, so both models' riddles are the same boxes."""
    out = []
    for tier, half, ya in (("lower", LOWER_HALF, 0.0), ("upper", UPPER_HALF, BOARD)):
        inner = half - BOARD
        out.append(box([-half, ya, -half], [half, ya + BOARD, -inner], f"riddle_{tier}_n", "riddle", "oak"))
        out.append(box([-half, ya, inner], [half, ya + BOARD, half], f"riddle_{tier}_s", "riddle", "oak"))
        out.append(box([-half, ya, -inner], [-inner, ya + BOARD, inner], f"riddle_{tier}_w", "riddle", "oak"))
        out.append(box([inner, ya, -inner], [half, ya + BOARD, inner], f"riddle_{tier}_e", "riddle", "oak"))
    for j, off in enumerate(WIRES):
        out.append(box([-WIRE_HALF, WIRE_Y, off - WIRE / 2], [WIRE_HALF, WIRE_Y + WIRE, off + WIRE / 2],
                       f"riddle_wirex_{j + 1}", "riddle", "iron"))
        out.append(box([off - WIRE / 2, WIRE_Y + WIRE, -WIRE_HALF], [off + WIRE / 2, WIRE_Y + 2 * WIRE, WIRE_HALF],
                       f"riddle_wirez_{j + 1}", "riddle", "iron"))
    return translate(out, [cx, y0, cz])


def build_charge(cx, y0, cz):
    """The charge on the riddle's mesh: per class the oversize bed and its fines over it, as loaded."""
    m = MESH_TOP
    out = [square(0.0, 0.0, BASE[0], m, m + BASE[1], f"{pre}base_1", f"{pre}base", "ore") for _c, pre, _r in CLASSES]
    b = m + BASE[1]
    out.append(square(0.0, 0.0, TOP1[0], b, b + TOP1[1], "c1top_1", "c1top", "ore"))
    out.append(square(0.0, 0.0, MID[0], b, b + MID[1], "c2mid_1", "c2mid", "ore"))
    out.append(square(0.0, 0.0, TOP[0], b + MID[1], b + MID[1] + TOP[1], "c2top_1", "c2top", "ore"))
    return translate(out, [cx, y0, cz])


def build_box(prefix, part, cx, cz, half_x, half_z, height):
    """A plank box: north and south walls across, east and west between them, a bottom inside them on the
    ground, and an iron angle down each corner (a plate on each face)."""
    out = [box([cx - half_x, 0.0, cz - half_z], [cx + half_x, height, cz - half_z + WALL], f"{prefix}wall_n", part, "planks"),
           box([cx - half_x, 0.0, cz + half_z - WALL], [cx + half_x, height, cz + half_z], f"{prefix}wall_s", part, "planks"),
           box([cx - half_x, 0.0, cz - half_z + WALL], [cx - half_x + WALL, height, cz + half_z - WALL], f"{prefix}wall_w", part, "planks"),
           box([cx + half_x - WALL, 0.0, cz - half_z + WALL], [cx + half_x, height, cz + half_z - WALL], f"{prefix}wall_e", part, "planks"),
           box([cx - half_x + WALL, 0.0, cz - half_z + WALL], [cx + half_x - WALL, BOX_BOTTOM, cz + half_z - WALL],
               f"{prefix}bottom", part, "planks")]
    leg, t = CORNER
    y0, y1 = 0.3, height - 0.3
    for zn, zs in (("n", -1), ("s", 1)):
        for xn, xs in (("w", -1), ("e", 1)):
            x, z = cx + xs * half_x, cz + zs * half_z
            out.append(box([min(x - xs * leg, x + xs * t), y0, min(z, z + zs * t)], [max(x - xs * leg, x + xs * t), y1, max(z, z + zs * t)],
                           f"{prefix}corner_{zn}{xn}_a", part, "iron"))
            out.append(box([min(x, x + xs * t), y0, min(z - zs * leg, z)], [max(x, x + xs * t), y1, max(z - zs * leg, z)],
                           f"{prefix}corner_{zn}{xn}_b", part, "iron"))
    return out


def build_fines(cx, cz):
    """The fines in the box, hidden in its bottom (and the second layer in the first) until they rise."""
    return [square(cx, cz, FS[0], F_HIDDEN, F_HIDDEN + FS[1], "c1fines_1", "c1fines", "ore"),
            square(cx, cz, F1[0], F_HIDDEN, F_HIDDEN + F1[1], "c2fines1_1", "c2fines1", "ore"),
            square(cx, cz, F2[0], F2_HIDDEN, F2_HIDDEN + F2[1], "c2fines2_1", "c2fines2", "ore")]


# ---------------------------------------------------------------- the hand riddle
def build_hand():
    cx, cz = HAND_C
    out = build_riddle(cx, HAND_Y0, cz) + build_charge(cx, HAND_Y0, cz) + build_fines(cx, cz)
    out += build_box("fr_box_", "frame", cx, cz, BOX_HALF, BOX_HALF, FINES_H)
    for name, z in (("n", BEARER_Z[0]), ("s", BEARER_Z[1])):
        out.append(box([BEARER_X[0], FINES_H, z - BEARER_W / 2], [BEARER_X[1], FINES_H + BEARER_H, z + BEARER_W / 2],
                       f"fr_bearer_{name}", "frame", "oak"))
    return out


# ---------------------------------------------------------------- the stand
def build_stand_frame():
    """The oak stand, 16 x 32: legs at the corners and at the roller, top rails all round, low stretchers all
    round; the oak roller the riddle rests on, in iron bearings on the middle legs; the iron plates and pins
    the hangers and the lever turn on."""
    f = "frame"
    out = []
    ry, rz, rr = ROLLER
    for s, mx in sides():
        lx = mx(0.0, LEG)
        for zn, (z0, z1) in (("n", (0.0, LEG)), ("m", MID_LEG_Z), ("s", (32.0 - LEG, 32.0))):
            out.append(box([lx[0], 0.0, z0], [lx[1], RAIL_Y[0], z1], f"fr_leg{zn}_{s}", f, "oak"))
        out.append(box([lx[0], RAIL_Y[0], 0.0], [lx[1], RAIL_Y[1], 32.0], f"fr_rail_{s}", f, "oak"))
        out.append(box([lx[0], STRETCH_Y[0], LEG], [lx[1], STRETCH_Y[1], MID_LEG_Z[0]], f"fr_stretcher_{s}1", f, "oak"))
        out.append(box([lx[0], STRETCH_Y[0], MID_LEG_Z[1]], [lx[1], STRETCH_Y[1], 32.0 - LEG], f"fr_stretcher_{s}2", f, "oak"))
        bx = mx(LEG, LEG + 0.2)
        out.append(box([bx[0], ry - 0.8, rz - 0.8], [bx[1], ry + 0.8, rz + 0.8], f"fr_bearing_{s}", f, "iron"))
        bx = mx(*BRACKET_X)
        out.append(box([bx[0], RAIL_Y[0] + 0.05, HANG_Z - 0.7], [bx[1], RAIL_Y[1] - 0.15, HANG_Z + 0.7], f"fr_bracket_{s}", f, "iron"))
        px = mx(0.3, HANGER_X[1] + 0.05)
        out += pin_x(px[0], px[1], PIVOT_Y, HANG_Z, PIN_R, f"fr_pin_{s}", f)
    for zn, (z0, z1) in (("n", (0.0, LEG)), ("s", (32.0 - LEG, 32.0))):
        out.append(box([LEG, RAIL_Y[0], z0], [16.0 - LEG, RAIL_Y[1], z1], f"fr_endrail_{zn}", f, "oak"))
        out.append(box([LEG, STRETCH_Y[0], z0], [16.0 - LEG, STRETCH_Y[1], z1], f"fr_endstretcher_{zn}", f, "oak"))
    out += round_x(0.6, 15.4, ry, rz, rr, "fr_roller", f, "oak")
    out.append(box([BRACKET_X[0], RAIL_Y[0] + 0.05, LEVER_Z - 0.7], [BRACKET_X[1], RAIL_Y[1] - 0.15, LEVER_Z + 0.7], "fr_bracket_lever", f, "iron"))
    out += pin_x(0.3, HANGER_X[1] + 0.05, PIVOT_Y, LEVER_Z, PIN_R, "fr_pin_lever", f)
    return out


def build_hangers():
    """The two iron hangers, each a strap from its pivot in the side rail down to the pin through the riddle's
    north lug; the west one carries the link's pin."""
    out = []
    for s, mx in sides():
        hx = mx(*HANGER_X)
        out.append(box([hx[0], HANG_Y - 0.45, HANG_Z - STRAP_W / 2], [hx[1], PIVOT_Y + 0.45, HANG_Z + STRAP_W / 2],
                       f"hanger_strap_{s}", "hangers", "iron"))
    out += pin_x(HANGER_X[0], LINK_X[1] + 0.05, LINK_PIN_Y, HANG_Z, LINK_PIN_R, "hanger_linkpin", "hangers")
    return out


def build_lugs():
    """The riddle's lugs, iron straps on its upper tier's east and west faces at its north end, and the pins
    through them and the hangers' eyes."""
    out = []
    for s, mx in sides():
        lx = mx(*LUG_X)
        out.append(box([lx[0], LUG_Y[0], HANG_Z - 0.4], [lx[1], LUG_Y[1], HANG_Z + 0.4], f"lug_strap_{s}", "lugs", "iron"))
        px = mx(HANGER_X[0] - 0.05, LUG_X[1] + 0.05)
        out += pin_x(px[0], px[1], HANG_Y, HANG_Z, PIN_R, f"lug_pin_{s}", "lugs")
    return out


def build_lever():
    """The hand lever, on the operator's right (west): an oak bar pivoted on the west rail, its arm below the
    pivot carrying the link's pin, its handle rising above the block; an iron strap round it at the pivot."""
    out = [box([HANGER_X[0], LINK_PIN_Y - 0.45, LEVER_Z - STRAP_W / 2], [HANGER_X[1], LEVER_TOP, LEVER_Z + STRAP_W / 2], "lever_bar", "lever", "oak"),
           box([HANGER_X[0] - 0.02, PIVOT_Y - 0.6, LEVER_Z - 0.5], [HANGER_X[1] + 0.02, PIVOT_Y + 0.6, LEVER_Z + 0.5], "lever_strap", "lever", "iron")]
    out += pin_x(HANGER_X[0], LINK_X[1] + 0.05, LINK_PIN_Y, LEVER_Z, LINK_PIN_R, "lever_pin", "lever")
    return out


def build_link():
    """The iron link from the lever's pin to the west hanger's."""
    return [box([LINK_X[0], LINK_PIN_Y - 0.35, HANG_Z - 0.6], [LINK_X[1], LINK_PIN_Y + 0.35, LEVER_Z + 0.6], "link_bar", "link", "iron")]


def build_stand():
    cx, cz = STAND_C
    return (build_hangers() + build_riddle(cx, STAND_Y0, cz) + build_lugs() + build_lever() + build_link() + build_charge(cx, STAND_Y0, cz)
            + build_box("box_", "boxes", FINES_C[0], FINES_C[1], BOX_HALF, BOX_HALF, FINES_H)
            + build_box("obox_", "boxes", OVERSIZE_C[0], OVERSIZE_C[1], BOX_HALF, OVERSIZE_HALF_Z, OVERSIZE_H)
            + build_fines(*FINES_C) + build_stand_frame())


# ---------------------------------------------------------------- the stand's kinematics
def hang_point(gamma):
    """(y, z) of the pin through the riddle's north lug with the hangers swung `gamma` radians south."""
    return (PIVOT_Y - HANGER_L * math.cos(gamma), HANG_Z + HANGER_L * math.sin(gamma))


def tilt(gamma):
    """The riddle's tip (radians, its far end down) with the hangers swung `gamma` south: hung at its north
    lug and resting on the roller, its underside (RIM_H under the lug's pin) is tangent to the roller."""
    ny, nz = hang_point(gamma)
    ry, rz, rr = ROLLER

    def gap(b):
        # the roller's centre's height over the underside's line, less its radius; the line through the
        # lug's pin's foot, turned b about x (b > 0: the south end down)
        return (ry - ny) * math.cos(b) + (rz - nz) * math.sin(b) + RIM_H + rr

    lo, hi = -0.6, 1.4
    for _ in range(100):
        mid = (lo + hi) / 2
        if (gap(lo) > 0) == (gap(mid) > 0):
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2


def riddling_amplitude():
    return math.asin(RIDDLE_SWING / HANGER_L)


def riddling_tilt():
    """The riddle's tip while riddling, as a1 * gamma + a2 * gamma^2 fitted at +-the swing: it rides the roller."""
    a = riddling_amplitude()
    tp, tm = tilt(a), tilt(-a)
    return (tp - tm) / (2 * a), (tp + tm) / (2 * a * a)


def swing_knots():
    """The swing out's knots: (gamma_i, tilt_i), gamma from 0 to SWING_OUT in KNOTS even steps."""
    return [(SWING_OUT * DEG * i / KNOTS, tilt(SWING_OUT * DEG * i / KNOTS)) for i in range(KNOTS + 1)]


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def per_class(thin, thick=None):
    return {"thin": r6(thin), "thick": r6(thin if thick is None else thick)}


WORK = {"name": "riddling", "unit": "charges", "step": 0.005, "end": {"thin": 1.0, "thick": 1.0}}
PATH = WORK
FOREVER = 1000.0


def win(t0, t1, t2=None, t3=None):
    """A window rising over t0..t1 and, given t2..t3 (as long), falling over it; else open to the end."""
    ease = t1 - t0
    if t2 is not None:
        assert abs((t3 - t2) - ease) < 1e-9, (t0, t1, t2, t3)
    return {"from": r6(t0), "to": r6(FOREVER if t3 is None else t3), "ease": r6(ease)}


def slide(axis, dist, wins):
    return {"type": "gauge", "motion": "slide", "axis": axis, "amount": per_class(dist / B), "windows": wins}


def turn(axis, pivot, angle, wins):
    return {"type": "gauge", "motion": "rotate", "axis": axis, "pivot": pivot, "amount": per_class(angle), "windows": wins}


def shake(axis, pivot, amplitude, phase, t, ratio=1.0, amount=0.0):
    """A turn about `axis` through `pivot` (blocks) by e * (amount + amplitude * cos(ratio * psi + phase)): the
    shake, eased in and out with the shaking window, its phase the riddling clock's travel."""
    return {"type": "gauge", "motion": "rotate", "axis": axis, "pivot": pivot, "amount": per_class(amount),
            "windows": [win(*t)], "lobes": {"ratio": r6(ratio), "phase": r6(phase), "amplitude": per_class(amplitude)}}


def charge_parts(t, base_ride, base_drivers):
    """The charge's parts: the beds carry their layers of fines, each sinking into the one under it."""
    parts = []
    for _cls, pre, req in CLASSES:
        parts.append({"id": f"{pre}base", "match": [f"{pre}base_*"], "requires": req, "ride": base_ride,
                      "drivers": copy.deepcopy(base_drivers)})
    parts.append({"id": "c1top", "match": ["c1top_*"], "requires": "chargesmall", "ride": "c1base",
                  "drivers": [slide("y", -TOP1_SINK, [win(*t["top1"])])]})
    parts.append({"id": "c2mid", "match": ["c2mid_*"], "requires": "chargefull", "ride": "c2base",
                  "drivers": [slide("y", -MID_SINK, [win(*t["mid"])])]})
    parts.append({"id": "c2top", "match": ["c2top_*"], "requires": "chargefull", "ride": "c2mid",
                  "drivers": [slide("y", -TOP_SINK, [win(*t["top"])])]})
    return parts


def fines_parts(t):
    """The fines in the box, rising out of its bottom as they fall through."""
    return [{"id": "c1fines", "match": ["c1fines_*"], "requires": "chargesmall", "drivers": [slide("y", F1_RISE, [win(*t["top1"])])]},
            {"id": "c2fines1", "match": ["c2fines1_*"], "requires": "chargefull", "drivers": [slide("y", F1_RISE, [win(*t["top"])])]},
            {"id": "c2fines2", "match": ["c2fines2_*"], "requires": "chargefull", "ride": "c2fines1",
             "drivers": [slide("y", F2_RISE, [win(*t["mid"])])]}]


def finish(parts):
    out = []
    for p in parts:
        p.setdefault("ride", None)
        p.setdefault("drivers", [])
        for d in p["drivers"]:
            validate_driver(d)
        out.append({"id": p["id"], "match": p["match"], "requires": p.get("requires"), "ride": p["ride"], "drivers": p["drivers"]})
    return out


def hand_amplitudes():
    """The shake's two turns (radians) that move the riddle's middle SHAKE_Z to and fro and SHAKE_X side to side."""
    d = SHAKE_PIVOT_Y - (HAND_Y0 + LIFT + RIM_H / 2)
    return math.asin(SHAKE_Z / d), math.asin(SHAKE_X / d)


def hand_parts():
    t = HAND_T
    az, ax = hand_amplitudes()
    piv = pt(HAND_C[0], SHAKE_PIVOT_Y, HAND_C[1])
    parts = [{"id": "riddle", "match": ["riddle_*"], "requires": "riddle",
              "drivers": [slide("y", LIFT, [win(*t["shake"])]),
                          shake("x", piv, az, -math.pi / 2, t["shake"]),
                          shake("z", piv, ax, 0.0, t["shake"])]}]
    parts += charge_parts(t, "riddle", []) + fines_parts(t)
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None})
    return finish(parts)


def out_windows(closing=True):
    """The swing out's knot windows: knot i rises over its step from STAND_T['out']; closing, it falls again in
    the reverse order from STAND_T['back'], the riddle back at rest at W 1; else it stays."""
    t = STAND_T
    s = t["step"]
    wins = []
    for i in range(KNOTS):
        t0 = t["out"] + i * s
        if closing:
            t2 = t["back"] + (KNOTS - 1 - i) * s
            wins.append(win(t0, t0 + s, t2, t2 + s))
        else:
            wins.append(win(t0, t0 + s))
    return wins


def stand_chain(closing):
    """The drivers of the riddle's own motion and of the hangers', as (riddle's own, hangers'): riddling, the
    riddle turned about its north pin as far as the hangers turn (so it moves level) plus its small tip on the
    roller; the swing out in KNOTS steps, each turning the hangers south and the riddle by as much back plus
    its tip at that knot. `closing` False keeps the swing out (for the oversize, which stays tipped off)."""
    t = STAND_T
    a = riddling_amplitude()
    a1, a2 = riddling_tilt()
    hang = pt(STAND_C[0], HANG_Y, HANG_Z)
    piv = pt(STAND_C[0], PIVOT_Y, HANG_Z)
    own = [shake("x", hang, (1.0 + a1) * a, -math.pi / 2, t["shake"]),
           shake("x", hang, -a2 * a * a / 2, 0.0, t["shake"], ratio=2.0, amount=a2 * a * a / 2)]
    hangers = [shake("x", piv, -a, -math.pi / 2, t["shake"])]
    knots = swing_knots()
    for i, w in enumerate(out_windows(closing)):
        dg = knots[i + 1][0] - knots[i][0]
        db = knots[i + 1][1] - knots[i][1]
        own.append(turn("x", hang, dg + db, [w]))
        hangers.append(turn("x", piv, -dg, [w]))
    return own, hangers


BED_HOLD = 5.0                               # the oversize is carried off at this height (its middle), over the oversize box


def bed_path():
    """The oversize's way off, once the riddle is tipped: lifted clear of the rim in the riddle's frame, carried
    straight off the far end to over the oversize box (away from the tipped rim all the way), levelled there
    and laid on the box's floor. Returns (local lift, world move (y, z), the level pivot and turn, the drop)."""
    gamma, beta = swing_knots()[-1]
    lift = RIM_H - MESH_TOP + 0.05
    # the bed's middle after the lift, in the swung riddle: turned beta about the north pin, carried to its swung place
    ny, nz = hang_point(gamma)
    ly = STAND_Y0 + MESH_TOP + BASE[1] / 2 + lift - HANG_Y
    lz = STAND_C[1] - HANG_Z
    y = ny + ly * math.cos(beta) - lz * math.sin(beta)
    z = nz + ly * math.sin(beta) + lz * math.cos(beta)
    move = (BED_HOLD - y, OVERSIZE_C[1] - z)
    drop = BOX_BOTTOM + BASE[1] / 2 - BED_HOLD
    return lift, move, (OVERSIZE_C[0], BED_HOLD, OVERSIZE_C[1]), -beta, drop


def stand_parts():
    t = STAND_T
    own, hangers = stand_chain(True)
    a = riddling_amplitude()
    lever = [shake("x", pt(STAND_C[0], PIVOT_Y, LEVER_Z), -a, -math.pi / 2, t["shake"])]
    link = [shake("x", pt(STAND_C[0], LINK_PIN_Y, HANG_Z), a, -math.pi / 2, t["shake"])]
    knots = swing_knots()
    for i, w in enumerate(out_windows(True)):
        dg = knots[i + 1][0] - knots[i][0]
        lever.append(turn("x", pt(STAND_C[0], PIVOT_Y, LEVER_Z), -dg, [w]))
        link.append(turn("x", pt(STAND_C[0], LINK_PIN_Y, HANG_Z), dg, [w]))
    # the oversize bed: the riddle's motion written out (it does not ride: it is left behind), its swing out kept
    keep_own, keep_hangers = stand_chain(False)
    lift, move, level_pivot, level, drop = bed_path()
    bed = ([slide("y", lift, [win(*t["lift"])])] + keep_own + keep_hangers
           + [slide("y", move[0], [win(*t["away"])]), slide("z", move[1], [win(*t["away"])]),
              turn("x", pt(*level_pivot), level, [win(*t["level"])]), slide("y", drop, [win(*t["down"])])])
    parts = [
        {"id": "hangers", "match": ["hanger_*"], "requires": "hangers", "drivers": hangers},
        {"id": "riddle", "match": ["riddle_*"], "requires": "riddle", "ride": "hangers", "drivers": own},
        {"id": "lugs", "match": ["lug_*"], "requires": "hangers", "ride": "riddle"},
        {"id": "lever", "match": ["lever_*"], "requires": "lever", "drivers": lever},
        # the link stays parallel to itself: turned back about its pin on the hanger
        {"id": "link", "match": ["link_*"], "requires": "lever", "ride": "hangers", "drivers": link},
    ]
    parts += charge_parts(t, None, bed)
    parts.append({"id": "boxes", "match": ["box_*", "obox_*"], "requires": "boxes"})
    parts += fines_parts(t)
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None})
    return finish(parts)


# ---------------------------------------------------------------- the two models
class Model:
    """What differs between the hand riddle and the stand."""

    def __init__(self, key, **kw):
        self.key = key
        self.__dict__.update(kw)


HAND = Model("hand", shape_name="riddle", rig_name="riddle-rig", ref_name="rig-reference", title="riddle",
             cells=(1, 1, 1), centre=HAND_C, y0=HAND_Y0, shakes={"thin": 12.0, "thick": 20.0}, t=HAND_T,
             build_fn=build_hand, parts_fn=hand_parts, infeed="east", output="south", fines_c=HAND_C, above=())
STAND = Model("stand", shape_name="riddlestand", rig_name="riddlestand-rig", ref_name="stand-rig-reference",
              title="riddle on its stand", cells=STAND_CELLS, centre=STAND_C, y0=STAND_Y0, shakes={"thin": 20.0, "thick": 32.0},
              t=STAND_T, build_fn=build_stand, parts_fn=stand_parts, infeed="east", output="west", fines_c=FINES_C,
              above=("lever",))
MODELS = (HAND, STAND)


# ---------------------------------------------------------------- poses
REST = (0.0, 0.0, 0, 0.0)                     # (theta, W, k, p): no charge; the authored pose


def inputs_of(pose):
    th, W, k, p = pose
    return {"theta": th, "work": W, "size": k, "presence": p}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path or PATH)


def pose_at(md, k, W, theta=None, p=1.0):
    """A charge of class k at W, the riddling clock at the model's pace (theta >= 0, so psi = theta)."""
    th = TAU * md.shakes[("thin", "thick")[k - 1]] * W if theta is None else theta
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


def coplanar_poses(md):
    q = math.pi / 2
    poses = [REST, pose_at(md, 1, 0.0), pose_at(md, 2, 0.0), pose_at(md, 1, 0.3, q), pose_at(md, 2, 0.25, 3 * q),
             pose_at(md, 2, 0.5, 0.7), pose_at(md, 1, 0.55, 2 * q), pose_at(md, 2, 0.6, q), pose_at(md, 2, 1.0)]
    if md is STAND:
        poses += [pose_at(md, 2, w) for w in (0.7, 0.75, 0.8, 0.83, 0.86, 0.9, 0.95)]
    else:
        poses.append(pose_at(md, 1, 0.9))
    return tuple(poses)


def fix_coplanar(md, els, parts):
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose),
                              coplanar_poses(md))


# ---------------------------------------------------------------- the rig file
def footprint(md):
    cx, cy, cz = md.cells
    return [(x, y, z) for x in range(cx) for y in range(cy) for z in range(cz)]


def make_rig(md, parts):
    progress_of({"work": WORK})
    fx, fz = md.fines_c
    rig = {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, the controller cell [0,0,0]; the operator stands to "
                    "the north. A hand station: no power cell. work is the riddling of one charge, W 0..1; k is the charge "
                    "(1 a part charge, 2 a full charge); gauge windows are placed in charges. theta is the riddling clock, "
                    "the player's hold-to-work, one turn a shake: nothing reads it but the shake's lobes, through its travel "
                    "psi. " + ("The riddle sits on bearers over the box and is shaken lifted just off them."
                               if md is HAND else
                               "The riddle hangs at its north end from two hangers and rests on a roller; a hand lever, rising "
                               "above the block (it has no boxes there), swings the hangers through a link, and pulled right "
                               "back tips the riddle over the roller into the oversize box in the second cell.")
                    + " See the riddle's README for the schema.",
        "cells": [],
        "infeedSide": md.infeed,
        "outputSide": md.output,
        "output": {"pos": pt(fx, BOX_BOTTOM + F1[1] + F2[1], fz)},
        "charge": {"pos": pt(md.centre[0], md.y0 + MESH_TOP + BASE[1] / 2, md.centre[1])},
    }
    if md is STAND:
        rig["oversizeSide"] = "south"
        rig["oversize"] = {"pos": pt(OVERSIZE_C[0], BOX_BOTTOM + BASE[1], OVERSIZE_C[1])}
    rig["work"] = dict(WORK)
    rig["riddling"] = {"shakesPerCharge": per_class(md.shakes["thin"], md.shakes["thick"]),
                       "_comment": "shakesPerCharge: the pace, shakes (theta / 2 pi) a charge; a full charge takes longer. "
                                   "Provisional: the gameplay (#714) sets it."}
    rig["parts"] = parts
    return rig


def anchors(md):
    return ("output", "charge", "oversize") if md is STAND else ("output", "charge")


# ---------------------------------------------------------------- shipped
def shipped(md, els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: the build frame's
    corner is the controller's). Gauge windows are in charges, so they stay as they are."""
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
    for key in anchors(md):
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts, sp, cells=None):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig; then the lids.
    Only the footprint's cells are written, and each box is clipped to its cell: whatever is drawn outside
    the footprint (the stand's lever above the block) has no box."""
    written = flatten(shape["elements"], textures={})
    rest = [posed(w, _part_matrix(ship_parts, part_of(ship_parts, w.name), inputs_of(REST), sp)) for w in written]
    by_cell = {}
    for el in rest:
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, []).append(el)
    cx, cy, cz = cells or (1, 1, 1)
    out = []
    for c in [(x, y, z) for x in range(cx) for y in range(cy) for z in range(cz)]:
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        boxes = cell_boxes(by_cell[pos], pos) if pos in by_cell else None
        out.append({"pos": list(pos), "boxes": boxes} if boxes else {"pos": list(pos), "hollow": True})
    return with_lids(out)


def check_shipped(md, els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    sp = ship["work"]
    poses = [REST, pose_at(md, 1, 0.2), pose_at(md, 2, 0.45), pose_at(md, 1, 0.7), pose_at(md, 2, 0.85), pose_at(md, 2, 0.99),
             (1.3, 0.3, 1, 0.6)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose), sp), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"{md.key}: shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells {cells}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print(f"FAIL {md.key}: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
REF_EDGES = {"hand": (0.0, 0.03, 0.06, 0.09, 0.14, 0.24, 0.33, 0.4, 0.45, 0.55, 0.65, 0.72, 0.78, 0.82, 0.86, 0.93, 1.0),
             "stand": (0.0, 0.03, 0.06, 0.09, 0.2, 0.36, 0.5, 0.63, 0.67, 0.69, 0.71, 0.74, 0.77, 0.8, 0.81, 0.83, 0.85, 0.87,
                       0.89, 0.92, 0.95, 0.98, 1.0)}
REF_THETAS = (0.0, 1.1, 2.3, 2.9, 4.4, 5.6)


def reference_poses(md):
    """theta in {0, 1.1, 2.3, 2.9} with no charge; for each class, W over the cycle's edges with p 1 and
    theta cycling through REF_THETAS (psi = theta: the shake's phase), and every fourth also at p 0.4."""
    out = [(th, 0.0, 0, 0.0) for th in REF_THETAS[:4]]
    for k in (1, 2):
        for i, W in enumerate(REF_EDGES[md.key]):
            th = REF_THETAS[(i + 2 * k) % len(REF_THETAS)]
            for p in ((1.0,) if i % 4 else (1.0, 0.4)):
                out.append((th, W, k, p))
    return out


def reference_json(md, ship_parts, sp):
    poses = []
    for pose in reference_poses(md):
        th, W, k, p = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose), sp)) for q in ship_parts}
        poses.append({"theta": th, "work": W, "size": k, "presence": p, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped {md.rig_name}.json's parts and work: each part's matrix as "
                        "3 rows of 4 (block units) at each pose (W in charges; travel is |theta|, the shake's phase). The "
                        "site's and the mod's tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(md, els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}: the {md.title}. Every element was made for the Seraph Horizons mod. The charge's "
             "texture code is 'ore': the renderer sets it to the crushed ore's texture. Keep element names when editing: "
             "the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def riddle_elements(els):
    return [el for el in els if el.part == "riddle"]


def check_same_riddle(hand_els, stand_els):
    """The riddle on the stand is the hand riddle's, element for element, moved: the same frame and mesh."""
    a, b = riddle_elements(hand_els), riddle_elements(stand_els)
    d = (STAND_C[0] - HAND_C[0], STAND_Y0 - HAND_Y0, STAND_C[1] - HAND_C[1])
    worst = 0.0 if len(a) == len(b) else 1e9
    for x, y in zip(a, b):
        if x.name != y.name:
            worst = 1e9
            break
        worst = max(worst, max(abs(x.size[i] - y.size[i]) for i in range(3)), max(abs(x.c[i] + d[i] - y.c[i]) for i in range(3)),
                    max(abs(x.r[i][j] - y.r[i][j]) for i in range(3) for j in range(3)))
        for f in set(x.faces) | set(y.faces):
            fa, fb = x.faces.get(f), y.faces.get(f)
            if fa is None or fb is None or fa["texture"] != fb["texture"]:
                worst = max(worst, 1.0)
            else:
                worst = max(worst, max(abs(p - q) for p, q in zip(fa["uv"], fb["uv"])))
    print(f"the same riddle: {len(a)} elements in each model, the stand's the hand's moved by {[round(v, 4) for v in d]}, "
          f"worst difference {worst:.1e}")
    if worst > 1e-9:
        print("FAIL the stand's riddle is not the hand riddle")
        return False
    return True


def main():
    ap = argparse.ArgumentParser(description="Generate the riddle's shapes, rigs and reference poses (hand and stand).")
    ap.add_argument("--out", type=Path, help="write the eight files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_riddle
    ok = True
    built = {}
    for md in MODELS:
        print(f"==== the {md.title} ({md.key})")
        els = md.build_fn()
        parts = md.parts_fn()
        if not args.quick:
            before, hidden = fix_coplanar(md, els, parts)
            print(f"coplanar faces before the fix: {sum(len(v) for v in before.values())} pairs over {len(before)} poses; "
                  f"{hidden} faces pressed against their own part removed")
        rig = make_rig(md, parts)
        ok = validate_riddle.validate(sys.modules[__name__], md, els, parts, rig, quick=args.quick) and ok
        built[md.key] = (els, parts, rig)
    ok = check_same_riddle(built["hand"][0], built["stand"][0]) and ok
    for md in MODELS:
        els, parts, rig = built[md.key]
        if args.out:
            outs = (args.out / f"{md.shape_name}.json", args.out / f"{md.shape_name}_frame.json", args.out / f"{md.rig_name}.json",
                    args.out / f"{md.ref_name}.json")
        else:
            outs = (SHAPE_DIR / f"{md.shape_name}.json", SHAPE_DIR / f"{md.shape_name}_frame.json", RIG_DIR / f"{md.rig_name}.json",
                    REFERENCE_DIR / f"{md.ref_name}.json")
        ship_els, ship_parts, ship = shipped(md, els, parts, rig)
        shape, frame_shape = shape_json(md, ship_els), shape_json(md, [el for el in ship_els if el.part == "frame"])
        ship["cells"] = shipped_cells(shape, ship_parts, ship["work"], md.cells)
        ok = validate_riddle.validate_files(sys.modules[__name__], md, shape, frame_shape, ship) and ok
        texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship), reference_dumps(reference_json(md, ship_parts, ship["work"])))
        for path, text in zip(outs, texts):
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text)
            json.loads(path.read_text())
            print(f"wrote {path} ({path.stat().st_size // 1024} KiB)")
        ok = check_shipped(md, els, parts, ship_els, ship_parts, ship) and ok
    if not ok:
        sys.exit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
