#!/usr/bin/env python3
"""Generate the riddle's shapes, rigs and reference poses: the hand riddle and the riddle on its stand.

The riddle is a miner's riddle of the 1800s, the hand station of the classify stage of ore processing (epic
#684, gameplay #714, model #715): a square, shallow sieve after the proportions of vanilla's pan (a frame of two
stepped tiers of oak boards, 11.5 across at the top and 2 deep) with a coarse woven mesh of iron wire let into its
lower tier, over a plank box that catches the fines. It comes in two forms, one block each, sharing the riddle and
the box element for element:

    riddle        the hand riddle: the riddle rests by its north and south boards on two oak bearers laid across
                  the box's mouth; the player lifts it just off them and swirls it, to and fro and side to side
    riddlestand   the riddle on its stand: the same riddle hung by four iron straps from a light oak stand over the
                  same box; the player swings it to and fro by a grip on its north side, the straps carrying it

A charge of crushed ore is drawn on the mesh as lumps of oversize and a heap of fines over them, one layer (a part
charge) or two (a full charge). As it is riddled the heap shrinks into the mesh, layer by layer, and the fines grow
in the box from its bottom, layer by layer (stretching gauges); the lumps, the oversize, are left on the mesh, to be
taken out by hand. Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    riddle.json, riddle_frame.json                the hand riddle, whole, and its static frame   (assets/.../shapes/block/)
    riddlestand.json, riddlestand_frame.json      the riddle on its stand, the same              (assets/.../shapes/block/)
    riddle-rig.json, riddlestand-rig.json         each one's cell, anchors, work, pace and rig    (assets/.../config/)
    rig-reference.json, stand-rig-reference.json  every part's matrix at a grid of poses          (tests/Riddle/)

or, with `--out DIR`, all eight into DIR. It validates its own output (validate_riddle.py) and exits non-zero if a
check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the cell's
north-west-bottom corner (the "build frame"); each form is one cell, the controller [0,0,0], so the shipped files
are the build frame divided by 16. The operator stands to the north.

The rigs' inputs (README, "Rig schema"):

    theta  the riddling clock, the player's hold-to-work: one turn a shake. Nothing reads theta itself; its travel
           psi (|theta| summed) phases the shake, a gauge's lobes
    W      the rig's work, the riddling of one charge, 0..1
    k      the charge: 0 none, 1 a part charge (thin), 2 a full charge (thick)
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
CELLS = (1, 1, 1)                            # one block, either form
ORIGIN_CELL = (0, 0, 0)                      # the controller: the riddle itself
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "planks": "game:block/wood/planks/oak1",
    "iron": "game:block/metal/plate/iron",
    # the charge: crushed ore wears its ore's nugget texture (itemtypes/oreprocessing/crushedore.json); the renderer
    # sets this code to the riddled ore's
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

# ---------------------------------------------------------------- the charge on the mesh
LUMPS = (                                    # lumps of oversize standing on the mesh, each over a wire along z:
    ((-3.0, -2.4), (1.5, 1.5), 25.0),        #   (x, z) from the middle, (width, height), turned about y (degrees)
    ((3.0, 2.3), (1.35, 1.35), -35.0),
    ((1.0, -3.0), (1.2, 1.3), 10.0),
    ((-1.0, 3.0), (1.45, 1.4), -20.0),       #   a full charge's fourth
)
LUMPS_PART = 3                               # a part charge has the first three
HEAP_SUNK = 0.15                             # the heap of fines settles this far into the mesh: its underside between the
HEAP_Y = MESH_TOP - HEAP_SUNK                # wires' tops and bottoms, in no other face's plane
PART = (3.4, 0.9)                            # a part charge's one layer of fines: half width, top over the mesh
LOW = (3.6, 1.0)                             # a full charge's lower layer
TOP = (2.4, 0.7)                             # and its top, on the lower layer: heaped 1.0 over the rim
LOW_TOP = MESH_TOP + LOW[1]                  # the top's underside, the plane it shrinks to

# ---------------------------------------------------------------- the box: plank walls and a bottom
BOX_HALF = 6.5                               # 13 square outside
WALL = 1.0
BOX_BOTTOM = 1.0                             # the bottom, on the ground inside the walls
BOX_H = 4.4

# ---------------------------------------------------------------- the fines in the box, (half width, thickness), grown from the bottom
F1 = (4.6, 0.75)                             # a full charge's first layer of fines
F2 = (3.0, 0.6)                              # and its second, on the first
FS = (4.0, 0.75)                             # a part charge's one layer

# ---------------------------------------------------------------- the hand riddle
CX, CZ = 8.0, 8.0                            # the box's and the riddle's middle
BEARER = 1.0                                 # two oak bearers across the box's mouth, 1 square,
BEARER_Z = (CZ - LOWER_HALF + BOARD / 2, CZ + LOWER_HALF - BOARD / 2)   # under the riddle's north and south boards
BEARER_X = (0.9, 15.1)                       # their ends past the box's walls
Y0 = BOX_H + BEARER                          # the riddle's underside, on the bearers (and hung at the same height on the stand)
LIFT = 0.5                                   # held this far off the bearers while it is shaken
SHAKE_Z = 1.5                                # to and fro (z), as the operator pushes and pulls it
SHAKE_X = 0.75                               # side to side (x), a quarter turn behind: it is swirled
SHAKE_PIVOT_Y = 48.0                         # the shake turns it about axes this high: it moves nearly level

# ---------------------------------------------------------------- the riddle on its stand
TIMBER = 1.5                                 # the stand's oak: sills, posts and heads 1.5 square, outside the box's walls
STAND_Z = (1.0, 15.0)                        # the sills and the heads run this far along z, either side of the box
POST_Z = (CZ - TIMBER / 2, CZ + TIMBER / 2)  # a post in the middle of each side, on its sill, under its head
HEAD_Y = (16.0 - TIMBER, 16.0)               # the heads, on the posts: the stand's top is the block's
TIE_Z = (1.0, 2.0)                           # two ties between the heads at their north and south ends (mirrored)
TIE_Y = (HEAD_Y[0] + 0.1, HEAD_Y[1] - 0.1)   # a hair inside the heads' height, so no faces share a plane
BRACE = (1.0, TIMBER - 0.3)                  # knee braces from each post to its head, both ways: width in their plane, thickness
BRACE_RUN = 3.0                              # each 45 degrees, meeting the post this far under the head and the head this far out,
BRACE_HOUSED = 0.375                         # its end faces' middles this far into them (the two in a post clear of each other)
HANG_Z = (CZ - 4.25, CZ + 4.25)              # the hangers: near the riddle's north and south ends, their pins half a
                                             # board in from the ends of its side boards
PIVOT_Y = 15.25                              # their pins through the heads
PIN_Y = Y0 + BOARD + BOARD / 2               # their pins into the riddle's upper side boards
HANGER_X = (TIMBER, CX - UPPER_HALF)         # the west straps fill the gap between the head's inner face and the riddle's side
HANGER_W = 1.0                               # the straps' width (z); their thickness is HANGER_X's span, 0.75
HANGER_END = 0.6                             # each strap's ends past its pins
PIN = 0.75                                   # the pins' square heads: on the straps' inner faces at the top, on their outer faces at the riddle
SWING = 1.5                                  # to and fro (z) at the riddle, as the operator pushes and pulls the grip
GRIP_POST_X = 2.25                           # the grip: two iron posts on the riddle's north board, this far either side of its middle,
GRIP_RISE = 1.5                              # this tall,
GRIP = 1.0                                   # carrying an oak bar 1 square,
GRIP_HALF = 3.0                              # 6 long

# ---------------------------------------------------------------- the cycle (t = W, one charge); the same for both forms
T = {"shake": (0.04, 0.08, 0.80, 0.84), "top": (0.10, 0.38), "mid": (0.42, 0.76), "top1": (0.10, 0.76)}
SHAKES = {"thin": 12.0, "thick": 20.0}       # shakes a charge: the pace (provisional, the gameplay's)

CLASSES = (("thin", "c1", "chargesmall"), ("thick", "c2", "chargefull"))
FORMS = ("hand", "stand")


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


def mirror_x(x0, x1):
    """The east twin of a span on the west side."""
    return 16.0 - x1, 16.0 - x0


def brace_x(a, b, width, depth, name, part, tex):
    """A bar in the y-z plane from point a to point b (their x is its middle), `width` across in that plane and `depth`
    along x: built along y and turned about x."""
    dy, dz = b[1] - a[1], b[2] - a[2]
    length = math.hypot(dy, dz)
    mid = [(a[k] + b[k]) / 2 for k in range(3)]
    el = box([mid[0] - depth / 2, mid[1] - length / 2, mid[2] - width / 2],
             [mid[0] + depth / 2, mid[1] + length / 2, mid[2] + width / 2], name, part, tex)
    el.r = rot("x", math.degrees(math.atan2(dz, dy)))
    return el


# ---------------------------------------------------------------- builders
def build_riddle():
    """The riddle, its underside at Y0: the frame of oak boards in two stepped tiers (north and south boards across,
    east and west between them) and the woven mesh, iron wires along x under wires along z, crimped into them where
    they cross, their ends let into the lower tier's boards. Both forms' riddle."""
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
    """The charge on the riddle's mesh, per class as loaded: the lumps of oversize standing on the mesh, and the heap
    of fines over them, settled into the mesh (a part charge's one layer; a full charge's lower layer and its top)."""
    m, h = MESH_TOP, HEAP_Y
    out = []
    for _c, pre, _r in CLASSES:
        for i, ((x, z), (w, ht), turn) in enumerate(LUMPS[:LUMPS_PART] if pre == "c1" else LUMPS):
            lump = square(x, z, w / 2, m, m + ht, f"{pre}over_lump{i + 1}", f"{pre}over", "ore")
            lump.r = rot("y", turn)
            out.append(lump)
    out.append(square(0.0, 0.0, PART[0], h, m + PART[1], "c1heap_1", "c1heap", "ore"))
    out.append(square(0.0, 0.0, LOW[0], h, LOW_TOP, "c2heap_1", "c2heap", "ore"))
    out.append(square(0.0, 0.0, TOP[0], LOW_TOP, LOW_TOP + TOP[1], "c2top_1", "c2top", "ore"))
    return translate(out, [CX, Y0, CZ])


def build_box():
    """The plank box (both forms): north and south walls across, east and west between them, a bottom inside them
    on the ground."""
    h, w = BOX_HALF, WALL
    return [box([CX - h, 0.0, CZ - h], [CX + h, BOX_H, CZ - h + w], "fr_box_wall_n", "frame", "planks"),
            box([CX - h, 0.0, CZ + h - w], [CX + h, BOX_H, CZ + h], "fr_box_wall_s", "frame", "planks"),
            box([CX - h, 0.0, CZ - h + w], [CX - h + w, BOX_H, CZ + h - w], "fr_box_wall_w", "frame", "planks"),
            box([CX + h - w, 0.0, CZ - h + w], [CX + h, BOX_H, CZ + h - w], "fr_box_wall_e", "frame", "planks"),
            box([CX - h + w, 0.0, CZ - h + w], [CX + h - w, BOX_BOTTOM, CZ + h - w], "fr_box_bottom", "frame", "planks")]


def build_bearers():
    """The hand riddle's two oak bearers, laid across the box's mouth on its walls."""
    return [box([BEARER_X[0], BOX_H, z - BEARER / 2], [BEARER_X[1], BOX_H + BEARER, z + BEARER / 2], f"fr_bearer_{name}",
                "frame", "oak") for name, z in (("n", BEARER_Z[0]), ("s", BEARER_Z[1]))]


def build_fines():
    """The fines in the box, as they lie at the end (each grows to this from nothing, from its underside)."""
    return [square(CX, CZ, FS[0], BOX_BOTTOM, BOX_BOTTOM + FS[1], "c1fines_1", "c1fines", "ore"),
            square(CX, CZ, F1[0], BOX_BOTTOM, BOX_BOTTOM + F1[1], "c2fines1_1", "c2fines1", "ore"),
            square(CX, CZ, F2[0], BOX_BOTTOM + F1[1], BOX_BOTTOM + F1[1] + F2[1], "c2fines2_1", "c2fines2", "ore")]


def build_stand():
    """The stand: on each side of the box an oak sill on the ground, a post in its middle, a head along its top and
    two knee braces from the post to the head; two ties between the heads at their ends."""
    out = []
    for side in ("w", "e"):
        x0, x1 = (0.0, TIMBER) if side == "w" else mirror_x(0.0, TIMBER)
        xm = (x0 + x1) / 2
        out.append(box([x0, 0.0, STAND_Z[0]], [x1, TIMBER, STAND_Z[1]], f"fr_sill_{side}", "frame", "oak"))
        out.append(box([x0, TIMBER, POST_Z[0]], [x1, HEAD_Y[0], POST_Z[1]], f"fr_post_{side}", "frame", "oak"))
        out.append(box([x0, HEAD_Y[0], STAND_Z[0]], [x1, HEAD_Y[1], STAND_Z[1]], f"fr_head_{side}", "frame", "oak"))
        for ns, sgn in (("n", -1.0), ("s", 1.0)):
            face = CZ + sgn * TIMBER / 2                 # the post's north or south face
            # from inside the post, low, to inside the head, out along it: housed at each end
            a = [xm, HEAD_Y[0] - BRACE_RUN - BRACE_HOUSED, face - sgn * BRACE_HOUSED]
            b = [xm, HEAD_Y[0] + BRACE_HOUSED, face + sgn * (BRACE_RUN + BRACE_HOUSED)]
            out.append(brace_x(a, b, BRACE[0], BRACE[1], f"fr_brace_{side}{ns}", "frame", "oak"))
    for ns, (z0, z1) in (("n", TIE_Z), ("s", (16.0 - TIE_Z[1], 16.0 - TIE_Z[0]))):
        out.append(box([TIMBER, TIE_Y[0], z0], [16.0 - TIMBER, TIE_Y[1], z1], f"fr_tie_{ns}", "frame", "oak"))
    return out


def build_hangers():
    """The four iron straps the riddle hangs from: each on a pin through its head (the pin's square head on the strap's
    inner face) and a pin into the riddle's upper side board; the north pair is one part, the south pair another."""
    out = []
    for ns, z in (("n", HANG_Z[0]), ("s", HANG_Z[1])):
        part = "hangernorth" if ns == "n" else "hangersouth"
        for side in ("w", "e"):
            x0, x1 = HANGER_X if side == "w" else mirror_x(*HANGER_X)
            out.append(box([x0, PIN_Y - HANGER_END, z - HANGER_W / 2], [x1, PIVOT_Y + HANGER_END, z + HANGER_W / 2],
                           f"hanger_{ns}_{side}", part, "iron"))
            px0, px1 = (HANGER_X[1], HANGER_X[1] + PIN) if side == "w" else mirror_x(HANGER_X[1], HANGER_X[1] + PIN)
            out.append(box([px0, PIVOT_Y - PIN / 2, z - PIN / 2], [px1, PIVOT_Y + PIN / 2, z + PIN / 2],
                           f"hanger_{ns}_{side}pin", part, "iron"))
    return out


def build_fittings():
    """The stand's fittings on the riddle (in its part, riding with it): the grip on its north board, two iron posts
    and an oak bar, and the four pins the straps hang it by, their heads on the straps' outer faces."""
    zc = CZ - UPPER_HALF + BOARD / 2                     # the north upper board's middle
    top = Y0 + RIM_H
    out = []
    for side, x in (("w", CX - GRIP_POST_X), ("e", CX + GRIP_POST_X)):
        out.append(box([x - PIN / 2, top, zc - PIN / 2], [x + PIN / 2, top + GRIP_RISE, zc + PIN / 2],
                       f"riddle_grip_post_{side}", "riddle", "iron"))
    out.append(box([CX - GRIP_HALF, top + GRIP_RISE, zc - GRIP / 2], [CX + GRIP_HALF, top + GRIP_RISE + GRIP, zc + GRIP / 2],
                   "riddle_grip_bar", "riddle", "oak"))
    for ns, z in (("n", HANG_Z[0]), ("s", HANG_Z[1])):
        for side in ("w", "e"):
            x0, x1 = (HANGER_X[0] - PIN, HANGER_X[0]) if side == "w" else mirror_x(HANGER_X[0] - PIN, HANGER_X[0])
            out.append(box([x0, PIN_Y - PIN / 2, z - PIN / 2], [x1, PIN_Y + PIN / 2, z + PIN / 2], f"riddle_pin_{ns}{side}",
                           "riddle", "iron"))
    return out


def build(form):
    if form == "hand":
        return build_riddle() + build_charge() + build_fines() + build_box() + build_bearers()
    return (build_hangers() + build_riddle() + build_fittings() + build_charge() + build_fines() + build_box()
            + build_stand())


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
    """A turn about `axis` through `pivot` (blocks) by e * amplitude * cos(psi + phase): the shake, eased in and out
    with the shaking window, its phase the riddling clock's travel."""
    return {"type": "gauge", "motion": "rotate", "axis": axis, "pivot": pivot, "amount": per_class(0.0),
            "windows": [win(*t)], "lobes": {"ratio": 1.0, "phase": r6(phase), "amplitude": per_class(amplitude)}}


def shrink(cls, y, t):
    """A heap layer of class `cls` shrinking to nothing along y onto the plane at height y (voxels) over the window t
    = (a, b), and staying gone (the stretching gauge's recipe, docs/recipe-browser/models.md)."""
    a, b = t
    return {"type": "gauge", "motion": "stretch", "axis": "y", "origin": pt(CX, y, CZ), "amount": {cls: -1.0},
            "windows": [{"from": r6(a), "to": r6(FOREVER), "ease": r6(b - a)}]}


def grow(cls, y, t):
    """A layer of fines of class `cls` growing from nothing along y from the plane at height y over t = (a, b), and
    staying whole."""
    a, b = t
    return {"type": "gauge", "motion": "stretch", "axis": "y", "origin": pt(CX, y, CZ), "amount": {cls: -1.0},
            "windows": [{"from": r6(-FOREVER), "to": r6(b), "ease": r6(b - a)}]}


def amplitudes():
    """The hand riddle's shake: the two turns (radians) that move its middle SHAKE_Z to and fro and SHAKE_X side to
    side."""
    d = SHAKE_PIVOT_Y - (Y0 + LIFT + RIM_H / 2)
    return math.asin(SHAKE_Z / d), math.asin(SHAKE_X / d)


def swing_amplitude():
    """The stand's swing: how far (radians) the straps turn to move the riddle SWING to and fro."""
    return math.asin(SWING / (PIVOT_Y - PIN_Y))


def charge_parts():
    """The charge's parts, riding the riddle: per class the lumps (no driver of their own) and the heap, each layer
    shrinking into the mesh; then the fines in the box, each layer growing from its underside."""
    parts = [{"id": f"{pre}over", "match": [f"{pre}over_*"], "requires": req, "ride": "riddle"} for _c, pre, req in CLASSES]
    parts += [{"id": "c1heap", "match": ["c1heap_*"], "requires": "chargesmall", "ride": "riddle",
               "drivers": [shrink("thin", HEAP_Y + Y0, T["top1"])]},
              {"id": "c2heap", "match": ["c2heap_*"], "requires": "chargefull", "ride": "riddle",
               "drivers": [shrink("thick", HEAP_Y + Y0, T["mid"])]},
              {"id": "c2top", "match": ["c2top_*"], "requires": "chargefull", "ride": "riddle",
               "drivers": [shrink("thick", LOW_TOP + Y0, T["top"])]},
              {"id": "c1fines", "match": ["c1fines_*"], "requires": "chargesmall", "drivers": [grow("thin", BOX_BOTTOM, T["top1"])]},
              {"id": "c2fines1", "match": ["c2fines1_*"], "requires": "chargefull", "drivers": [grow("thick", BOX_BOTTOM, T["top"])]},
              {"id": "c2fines2", "match": ["c2fines2_*"], "requires": "chargefull",
               "drivers": [grow("thick", BOX_BOTTOM + F1[1], T["mid"])]}]
    return parts


def rig_parts(form):
    if form == "hand":
        az, ax = amplitudes()
        piv = pt(CX, SHAKE_PIVOT_Y, CZ)
        head = [{"id": "riddle", "match": ["riddle_*"], "requires": "riddle",
                 "drivers": [slide("y", LIFT, [win(*T["shake"])]),
                             shake("x", piv, az, -math.pi / 2, T["shake"]),
                             shake("z", piv, ax, 0.0, T["shake"])]}]
    else:
        # The straps turn about their pins in the heads; the riddle rides the north pair and turns back about its pin in
        # them by as much, so it moves as a parallelogram's fourth side: level, to and fro on an arc.
        a = swing_amplitude()
        head = [{"id": "hangernorth", "match": ["hanger_n_*"], "requires": "hangers",
                 "drivers": [shake("x", pt(CX, PIVOT_Y, HANG_Z[0]), a, -math.pi / 2, T["shake"])]},
                {"id": "hangersouth", "match": ["hanger_s_*"], "requires": "hangers",
                 "drivers": [shake("x", pt(CX, PIVOT_Y, HANG_Z[1]), a, -math.pi / 2, T["shake"])]},
                {"id": "riddle", "match": ["riddle_*"], "requires": "riddle", "ride": "hangernorth",
                 "drivers": [shake("x", pt(CX, PIN_Y, HANG_Z[0]), -a, -math.pi / 2, T["shake"])]}]
    parts = head + charge_parts() + [{"id": "frame", "match": ["fr_*"], "requires": None}]
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


ANCHORS = ("fines", "charge")
# A hand station takes no automated input or output (the owner's ruling): the player loads the charge and takes the
# fines and the oversize out by hand, so neither rig has an infeed or output side, face or cell for a chute or a hopper,
# only the side the operator stands on.
NO_TRANSFER = ("infeedSide", "outputSide", "infeedFace", "outputFace", "infeedCell", "outputCell", "output", "infeed")

FORM_COMMENT = {
    "hand": "The hand riddle: it rests on two bearers over the box and is lifted just off them and swirled while a charge "
            "is riddled.",
    "stand": "The riddle on its stand: the hand riddle's riddle and box, the riddle hung from the stand's heads by four iron "
             "straps and swung to and fro by its grip while a charge is riddled, level, as a parallelogram's fourth side.",
}


def make_rig(form, parts):
    progress_of({"work": WORK})
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, the controller cell [0,0,0]; the operator stands to "
                    f"the north. {FORM_COMMENT[form]} A hand station: no power cell, and no automated input or output (no "
                    "infeed or output side for chutes or hoppers: the player loads and unloads it by hand). work is the "
                    "riddling of one charge, W 0..1; k is the charge (1 a part charge, 2 a full charge); gauge windows are "
                    "placed in charges. theta is the riddling clock, the player's hold-to-work, one turn a shake: nothing "
                    "reads it but the shake's lobes, through its travel psi. The fines fall into the box (fines.pos) and "
                    "the oversize stays in the riddle (charge.pos), both taken out by hand. See the riddle's README for the "
                    "schema.",
        "cells": [],
        "operatorSide": "north",
        "fines": {"pos": pt(CX, BOX_BOTTOM + F1[1] + F2[1], CZ)},
        "charge": {"pos": pt(CX, Y0 + MESH_TOP + LOW[1] / 2, CZ)},
        "work": dict(WORK),
        "riddling": {"shakesPerCharge": per_class(SHAKES["thin"], SHAKES["thick"]),
                     "_comment": "shakesPerCharge: the pace, shakes (theta / 2 pi) a charge; a full charge takes longer. "
                                 "Provisional: the gameplay (#714) sets it."},
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


def check_shipped(form, els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    sp = ship["work"]
    poses = [REST, pose_at(1, 0.2), pose_at(2, 0.45), pose_at(1, 0.7), pose_at(2, 0.85), pose_at(2, 0.99), (1.3, 0.3, 1, 0.6)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose), sp), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"[{form}] shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells {cells}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print(f"FAIL [{form}] the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
REF_EDGES = (0.0, 0.03, 0.06, 0.09, 0.14, 0.24, 0.33, 0.4, 0.45, 0.55, 0.65, 0.72, 0.78, 0.82, 0.86, 0.93, 1.0)
REF_THETAS = (0.0, 1.1, 2.3, 2.9, 4.4, 5.6)


def reference_poses():
    """theta in {0, 1.1, 2.3, 2.9} with no charge; for each class, W over the cycle's edges with p 1 and theta cycling
    through REF_THETAS (psi = theta: the shake's phase), and every fourth also at p 0.4."""
    out = [(th, 0.0, 0, 0.0) for th in REF_THETAS[:4]]
    for k in (1, 2):
        for i, W in enumerate(REF_EDGES):
            th = REF_THETAS[(i + 2 * k) % len(REF_THETAS)]
            for p in ((1.0,) if i % 4 else (1.0, 0.4)):
                out.append((th, W, k, p))
    return out


def reference_json(form, ship_parts, sp):
    poses = []
    for pose in reference_poses():
        th, W, k, p = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose), sp)) for q in ship_parts}
        poses.append({"theta": th, "work": W, "size": k, "presence": p, "matrices": mats})
    rig = "riddle-rig.json" if form == "hand" else "riddlestand-rig.json"
    return {"_comment": f"Generated by {SCRIPT} from the shipped {rig}'s parts and work: each part's matrix as 3 rows of 4 "
                        "(block units) at each pose (W in charges; travel is |theta|, the shake's phase). The site's, the "
                        "mod's and the Python tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
SHAPE_COMMENT = {
    "hand": "the hand riddle: the riddle on its bearers over the fines box",
    "stand": "the riddle on its stand: the hand riddle's riddle and box, the riddle hung from an oak stand by four iron straps",
}


def shape_json(form, els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}: {SHAPE_COMMENT[form]}. Every element was made for the Seraph Horizons mod. The "
             "charge's texture code is 'ore': the renderer sets it to the crushed ore's texture. Keep element names when "
             "editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


FILES = {"hand": ("riddle.json", "riddle_frame.json", "riddle-rig.json", "rig-reference.json"),
         "stand": ("riddlestand.json", "riddlestand_frame.json", "riddlestand-rig.json", "stand-rig-reference.json")}


def outputs(form, out_dir):
    shape, frame, rig, ref = FILES[form]
    if out_dir:
        return out_dir / shape, out_dir / frame, out_dir / rig, out_dir / ref
    return SHAPE_DIR / shape, SHAPE_DIR / frame, RIG_DIR / rig, REFERENCE_DIR / ref


def main():
    ap = argparse.ArgumentParser(description="Generate the riddle's shapes, rigs and reference poses (both forms).")
    ap.add_argument("--out", type=Path, help="write the eight files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_riddle
    ok = True
    built = {}
    for form in FORMS:
        print(f"---------------- {form}")
        els = build(form)
        parts = rig_parts(form)
        if not args.quick:
            before, hidden = fix_coplanar(els, parts)
            print(f"[{form}] coplanar faces before the fix: {sum(len(v) for v in before.values())} pairs over {len(before)} "
                  f"poses; {hidden} faces pressed against their own part removed")
        rig = make_rig(form, parts)
        ok = validate_riddle.validate(sys.modules[__name__], form, els, parts, rig, quick=args.quick) and ok
        built[form] = (els, parts, rig)
    print("---------------- both")
    ok = validate_riddle.check_same_riddle(built["hand"][0], built["stand"][0]) and ok
    for form in FORMS:
        els, parts, rig = built[form]
        ship_els, ship_parts, ship = shipped(els, parts, rig)
        shape = shape_json(form, ship_els)
        frame_shape = shape_json(form, [el for el in ship_els if el.part == "frame"])
        ship["cells"] = shipped_cells(shape, ship_parts, ship["work"])
        ok = validate_riddle.validate_files(sys.modules[__name__], form, shape, frame_shape, ship) and ok
        texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship),
                 reference_dumps(reference_json(form, ship_parts, ship["work"])))
        for path, text in zip(outputs(form, args.out), texts):
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text)
            json.loads(path.read_text())
            print(f"wrote {path} ({path.stat().st_size // 1024} KiB)")
        ok = check_shipped(form, els, parts, ship_els, ship_parts, ship) and ok
    if not ok:
        sys.exit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
