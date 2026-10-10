#!/usr/bin/env python3
"""Generate the riddle's shapes, rigs and reference poses: the hand riddle and the riddle on its stand.

The riddle is a miner's riddle of the 1800s, the hand tier of classifying crushed ore: a round sieve with a
bent-wood rim and a coarse woven mesh of iron wire held in it by an iron band. Two models share it,
element for element:

  * the hand riddle, one block: a cooper's low tub (oak staves, two iron hoops, a bottom set in above the
    chime) with two oak bearers laid across its mouth, and the riddle on them. The player shakes it, lifted
    just clear of the bearers, to and fro and side to side, and the fines fall through into the tub;
  * the riddle on its stand, tier 1, one block: the same tub under a light oak stand (four legs, top rails,
    low stretchers), the same riddle hung from the side rails by two iron hangers on trunnions at its
    rim, and a hand lever on the operator's right that swings it to and fro through an iron link to the
    west hanger. A full charge, a stack, is riddled without being held.

A charge of crushed ore is drawn as a heap on the mesh: an oversize bed and one (a part charge) or two (a
full charge) layers of fines over it. As the charge is riddled each layer of fines sinks into the one under
it and the fines rise in the tub, layer by layer, out of the tub's bottom; the oversize bed is left in the
riddle when the charge is done. Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    riddle.json                    the hand riddle, every moving part         (assets/.../shapes/block/)
    riddle_frame.json              its static frame only (the tub and bearers) (assets/.../shapes/block/)
    riddlestand.json               the riddle on its stand, every moving part (assets/.../shapes/block/)
    riddlestand_frame.json         the stand's static frame only              (assets/.../shapes/block/)
    riddle-rig.json                the hand riddle's cells, anchors and rig   (assets/.../config/)
    riddlestand-rig.json           the stand's                                (assets/.../config/)
    rig-reference.json             the hand riddle's parts' matrices at a grid of poses  (tests/Riddle/)
    stand-rig-reference.json       the stand's                                           (tests/Riddle/)

or, with `--out DIR`, all eight into DIR. It validates its own output (validate_riddle.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
cell's north-west-bottom corner (the "build frame"); each model is one cell, the controller [0,0,0], so the
shipped files are the build frame divided by 16. The operator stands to the north.

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

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 1, 1, 1          # each model is one block
ORIGIN_CELL = (0, 0, 0)                      # the controller: the only cell
CX, CZ = 8.0, 8.0                            # the tub's axis, and the riddle's at rest

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "planks": "game:block/wood/planks/oak1",
    "iron": "game:block/metal/plate/iron",
    "ore": "game:block/stone/gravel/granite",   # the charge: the renderer sets it to the crushed ore's texture
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the tub (both models)
SIDES_N = 16                                 # the tub's staves and hoops, the riddle's rim and band: 16-gons
TUB_R = 5.75                                 # the staves' outer faces
STAVE_T = 0.7
TUB_H = 5.5                                  # the staves' tops: the tub's rim
TUB_BOTTOM = (0.6, 1.6)                      # the bottom, set in the staves above their chime
TUB_BOTTOM_R = 5.1                           # into the staves (their inner faces at TUB_R - STAVE_T)
HOOPS_Y = ((0.9, 1.4), (4.1, 4.6))           # two iron hoops
HOOP_OUT, HOOP_IN = 0.15, 0.05               # proud of the staves, and into them

# ---------------------------------------------------------------- the riddle (both models); heights from the rim's bottom edge
RIM_R = 5.5                                  # the bent-wood rim's outer faces: 11 across
RIM_T = 0.45
RIM_H = 2.4
BAND_R = (4.75, 5.1)                         # the iron band inside the rim that holds the mesh (into the rim)
BAND_Y = (0.45, 1.15)
WIRE = 0.25                                  # the woven mesh: square iron wires,
PITCH = 1.25                                 # this far apart: openings of 1
WIRE_Y = 0.55                                # the lower wires' underside; the upper wires cross on them
WIRE_END = 4.9                               # the wires end inside the band
WIRES = tuple(PITCH * (i - 3.5) for i in range(8))
MESH_TOP = WIRE_Y + 2 * WIRE                 # the mesh's top, the charge's bed

# ---------------------------------------------------------------- the charge: a heap on the mesh, (radius, thickness)
BASE = (3.7, 0.7)                            # the oversize bed, left in the riddle; the mesh shows round it
MID = (2.8, 0.5)                             # a full charge's lower layer of fines
TOP = (1.7, 0.36)                            # a full charge's top
TOP1 = (2.4, 0.5)                            # a part charge's one layer of fines
MID_SINK = 0.6                               # into the bed: its bottom 0.1 over the bed's
TOP_SINK = 0.43                              # into the lower layer (and with it into the bed)
TOP1_SINK = 0.6
DISC_STEP = 0.012                            # a disc's strips are this much shorter each, so their ends share no plane
HIDE_MARGIN = 0.02                           # a sunk layer stays this far inside what hides it

# ---------------------------------------------------------------- the fines in the tub, (radius, thickness)
F1 = (4.0, 0.75)                             # a full charge's first layer of fines
F2 = (2.6, 0.6)                              # and its second, on the first
FS = (3.4, 0.75)                             # a part charge's one layer
F_HIDDEN = 0.7                               # hidden in the tub's bottom (its underside), until it rises
F2_HIDDEN = 0.775                            # hidden in the first layer
F1_RISE = TUB_BOTTOM[1] - F_HIDDEN           # onto the bottom
F2_RISE = (F_HIDDEN + F1[1]) - F2_HIDDEN     # onto the first layer

# ---------------------------------------------------------------- the hand riddle
BEARER_Z = (CZ - 3.3, CZ + 3.3)              # two oak bearers laid across the tub's mouth (centres)
BEARER_W, BEARER_H = 0.9, 0.8
BEARER_X = (1.6, 14.4)
HAND_Y0 = TUB_H + BEARER_H                   # the riddle's rim's bottom edge, on the bearers
LIFT = 0.4                                   # held this far off the bearers while it is shaken
SHAKE_Z = 1.2                                # to and fro (z), as the operator pushes and pulls it
SHAKE_X = 0.6                                # side to side (x), a quarter turn behind: it is swirled
SHAKE_PIVOT_Y = 40.0                         # the shake turns it about axes this high: it moves nearly level

# ---------------------------------------------------------------- the stand
LEG = 1.5                                    # four oak legs at the corners
RAIL_Y = (14.6, 16.0)                        # the top rails: east and west (along z) and north and south (along x)
STRETCH_Y = (1.4, 2.6)                       # the low stretchers, all round
STAND_Y0 = 7.6                               # the riddle's rim's bottom edge, hung
TRUNNION_Y = STAND_Y0 + RIM_H                # its trunnions, at the rim's top: it hangs level under them
PIVOT_Y = 15.25                              # the hangers' pivots, in the side rails, over the trunnions
HANGER_L = PIVOT_Y - TRUNNION_Y
SWING = 1.5                                  # to and fro, on the hangers
HANGER_X = (LEG + 0.3, LEG + 0.7)            # the west hanger's strap (mirrored east), inside its rail
HANGER_W = 0.8
BRACKET_X = (LEG, HANGER_X[0])               # an iron plate on the rail's inner face, the hanger's wearing face
PIN_R = 0.25
LUG_X = (HANGER_X[1], CX - RIM_R + 0.05)     # the riddle's trunnion lugs, iron straps on its rim
LUG_Y = (TRUNNION_Y - 0.6, TRUNNION_Y + 0.6)
LUG_Z = (CZ - 0.4, CZ + 0.4)
LINK_PIN_Y = 11.3                            # the link's pin on the west hanger
LEVER_PIVOT = (2.0, 4.0)                     # (y, z): the hand lever's pivot, in the west stretcher
LEVER_X = (LEG + 0.1, HANGER_X[1])           # the lever, beside the link
LEVER_W = 0.8
LEVER_TOP = 15.4                             # its grip
BOSS_X = (LEG, LEG + 0.08)                   # an iron plate on the stretcher's inner face round the lever's pin
KNUCKLE_X = (LEG + 0.08, HANGER_X[1] + 0.02)
LINK_X = (HANGER_X[1], HANGER_X[1] + 0.48)   # the iron link, inside the lever and the hanger
LINK_Y = (LINK_PIN_Y - 0.35, LINK_PIN_Y + 0.35)
SLOT_Y = (LINK_PIN_Y - 0.6, LINK_PIN_Y + 0.6)   # its eye at the lever is a slot: the lever's pin may ride up and down in it
LINK_PIN_R = 0.22

# ---------------------------------------------------------------- the cycle (t = W, one charge)
T_SHAKE = (0.04, 0.08, 0.80, 0.84)           # shaken: in over the first pair, out over the second
T_TOP = (0.10, 0.38)                         # a full charge's top sinks into its lower layer; the first fines rise
T_MID = (0.42, 0.76)                         # its lower layer sinks into the bed; the second fines rise
T_TOP1 = (0.10, 0.76)                        # a part charge's one layer sinks into the bed; its fines rise

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


def half_turn(deg):
    """An angle in (-90, 90]: a box turned half a turn is the same box."""
    return ((deg + 90.0) % 180.0) - 90.0 if ((deg + 90.0) % 180.0) else 90.0


def turned_box(c, size, deg, name, part, tex):
    """A box of `size` (x, y, z) centred on `c`, turned `deg` about y."""
    el = box([c[k] - size[k] / 2 for k in range(3)], [c[k] + size[k] / 2 for k in range(3)], name, part, tex)
    a = half_turn(deg)
    if abs(a) > 1e-9:
        el.r = rot("y", a)
    return el


def ring(c, r_out, t, y0, y1, name, part, tex, n=SIDES_N):
    """A hoop about the vertical axis through c (x, z): n straight segments, each as long as a side of the
    n-gon of its outer faces (r_out from the axis), t thick, y0..y1. Neighbours meet at their outer corners."""
    length = 2 * r_out * math.tan(math.pi / n)
    rc = r_out - t / 2
    out = []
    for i in range(n):
        a = 360.0 * i / n
        ar = math.radians(a)
        centre = [c[0] + rc * math.cos(ar), (y0 + y1) / 2, c[1] - rc * math.sin(ar)]
        out.append(turned_box(centre, [t, y1 - y0, length], a, f"{name}_{i + 1}", part, tex))
    return out


def disc(c, y0, y1, r, name, part, tex, k=4):
    """A plain round slab about the vertical axis through c (x, z): k strips as long as the 2k-gon is
    across, 180/k degrees apart; each a hair thinner than the last (DISC_STEP off both faces) so no two
    strips' faces share a plane. The union holds the circle of radius r."""
    half = r * math.tan(math.pi / (2 * k))
    out = []
    for i in range(k):
        st = DISC_STEP * i
        centre = [c[0], (y0 + y1) / 2, c[1]]
        out.append(turned_box(centre, [2 * r, (y1 - y0) - 2 * st, 2 * half], 180.0 * i / k, f"{name}_{i + 1}", part, tex))
    return out


def pin_x(x0, x1, y, z, r, name, part, tex="iron"):
    """A round pin along x: two square strips, turned 0 and 45 degrees about x."""
    out = []
    for i in range(2):
        el = box([x0 + 0.01 * i, y - r, z - r], [x1 - 0.01 * i, y + r, z + r], f"{name}_{i + 1}", part, tex)
        if i:
            el.r = rot("x", 45.0)
        out.append(el)
    return out


def mirror_x(x0, x1):
    return (16.0 - x1, 16.0 - x0)


def sides():
    """The west and east (name suffix, x mapper) pairs: both models are symmetric about x 8 but for the lever."""
    return (("w", lambda a, b: (a, b)), ("e", mirror_x))


# ---------------------------------------------------------------- builders shared by both models
def build_tub(prefix, part):
    """The tub: sixteen oak staves, a bottom set in them above the chime, two iron hoops round them."""
    out = ring((CX, CZ), TUB_R, STAVE_T, 0.0, TUB_H, f"{prefix}stave", part, "planks")
    out += disc((CX, CZ), TUB_BOTTOM[0], TUB_BOTTOM[1], TUB_BOTTOM_R, f"{prefix}bottom", part, "planks")
    for i, (y0, y1) in enumerate(HOOPS_Y):
        out += ring((CX, CZ), TUB_R + HOOP_OUT, HOOP_OUT + HOOP_IN, y0, y1, f"{prefix}hoop{i + 1}", part, "iron")
    return out


def build_riddle(y0):
    """The riddle, its rim's bottom edge at y0: the bent-wood rim, the iron band inside it and the woven mesh
    of iron wires (the lower layer along x, the upper along z, crossing on it), their ends in the band. Built
    at 0 and raised, so both models' riddles are the same boxes."""
    out = ring((CX, CZ), RIM_R, RIM_T, 0.0, RIM_H, "riddle_rim", "riddle", "oak")
    out += ring((CX, CZ), BAND_R[1], BAND_R[1] - BAND_R[0], BAND_Y[0], BAND_Y[1], "riddle_band", "riddle", "iron")
    for j, off in enumerate(WIRES):
        half = math.sqrt(WIRE_END ** 2 - off ** 2)
        out.append(box([CX - half, WIRE_Y, CZ + off - WIRE / 2], [CX + half, WIRE_Y + WIRE, CZ + off + WIRE / 2],
                       f"riddle_wirex_{j + 1}", "riddle", "iron"))
        out.append(box([CX + off - WIRE / 2, WIRE_Y + WIRE, CZ - half], [CX + off + WIRE / 2, WIRE_Y + 2 * WIRE, CZ + half],
                       f"riddle_wirez_{j + 1}", "riddle", "iron"))
    return translate(out, [0.0, y0, 0.0])


def build_charge(y0):
    """The charge on the mesh of a riddle whose rim's bottom edge is at y0: per class the oversize bed and
    its fines over it, as loaded (the authored pose). Built at 0 and raised, as the riddle is."""
    m = MESH_TOP
    out = []
    for _cls, pre, _req in CLASSES:
        out += disc((CX, CZ), m, m + BASE[1], BASE[0], f"{pre}base", f"{pre}base", "ore")
    b = m + BASE[1]
    out += disc((CX, CZ), b, b + TOP1[1], TOP1[0], "c1top", "c1top", "ore")
    out += disc((CX, CZ), b, b + MID[1], MID[0], "c2mid", "c2mid", "ore")
    out += disc((CX, CZ), b + MID[1], b + MID[1] + TOP[1], TOP[0], "c2top", "c2top", "ore")
    return translate(out, [0.0, y0, 0.0])


def build_fines():
    """The fines in the tub, hidden in its bottom (and the second layer in the first) until they rise."""
    out = disc((CX, CZ), F_HIDDEN, F_HIDDEN + FS[1], FS[0], "c1fines", "c1fines", "ore")
    out += disc((CX, CZ), F_HIDDEN, F_HIDDEN + F1[1], F1[0], "c2fines1", "c2fines1", "ore")
    out += disc((CX, CZ), F2_HIDDEN, F2_HIDDEN + F2[1], F2[0], "c2fines2", "c2fines2", "ore")
    return out


# ---------------------------------------------------------------- the hand riddle
def build_bearers():
    out = []
    for name, z in (("n", BEARER_Z[0]), ("s", BEARER_Z[1])):
        out.append(box([BEARER_X[0], TUB_H, z - BEARER_W / 2], [BEARER_X[1], TUB_H + BEARER_H, z + BEARER_W / 2],
                       f"fr_bearer_{name}", "frame", "oak"))
    return out


def build_hand():
    return build_riddle(HAND_Y0) + build_charge(HAND_Y0) + build_fines() + build_tub("fr_tub_", "frame") + build_bearers()


# ---------------------------------------------------------------- the stand
def build_stand_frame():
    """The oak stand: four legs, the top rails (east and west carrying the hangers, north and south tying
    them), low stretchers all round; the iron plates the hangers and the lever bear on and their pins."""
    f = "frame"
    out = []
    for s, mx in sides():
        lx = mx(0.0, LEG)
        for zn, (z0, z1) in (("n", (0.0, LEG)), ("s", (16.0 - LEG, 16.0))):
            out.append(box([lx[0], 0.0, z0], [lx[1], RAIL_Y[0], z1], f"fr_leg{zn}_{s}", f, "oak"))
        out.append(box([lx[0], RAIL_Y[0], 0.0], [lx[1], RAIL_Y[1], 16.0], f"fr_rail_{s}", f, "oak"))
        out.append(box([lx[0], STRETCH_Y[0], LEG], [lx[1], STRETCH_Y[1], 16.0 - LEG], f"fr_stretcher_{s}", f, "oak"))
        bx = mx(*BRACKET_X)
        out.append(box([bx[0], RAIL_Y[0] + 0.05, CZ - 0.7], [bx[1], RAIL_Y[1] - 0.15, CZ + 0.7], f"fr_bracket_{s}", f, "iron"))
        px = mx(0.3, HANGER_X[1] + 0.05)
        out += pin_x(px[0], px[1], PIVOT_Y, CZ, PIN_R, f"fr_pin_{s}", f)
    for zn, (z0, z1) in (("n", (0.0, LEG)), ("s", (16.0 - LEG, 16.0))):
        out.append(box([LEG, RAIL_Y[0], z0], [16.0 - LEG, RAIL_Y[1], z1], f"fr_endrail_{zn}", f, "oak"))
        out.append(box([LEG, STRETCH_Y[0], z0], [16.0 - LEG, STRETCH_Y[1], z1], f"fr_endstretcher_{zn}", f, "oak"))
    ly, lz = LEVER_PIVOT
    out.append(box([BOSS_X[0], STRETCH_Y[0], lz - 0.6], [BOSS_X[1], STRETCH_Y[1], lz + 0.6], "fr_boss", f, "iron"))
    out += pin_x(0.3, KNUCKLE_X[1] + 0.03, ly, lz, PIN_R, "fr_leverpin", f)
    return out


def build_hangers():
    """The two iron hangers, each a strap from its pivot in the side rail down to the riddle's trunnion; the
    west one carries the link's pin."""
    out = []
    for s, mx in sides():
        hx = mx(*HANGER_X)
        out.append(box([hx[0], TRUNNION_Y - 0.45, CZ - HANGER_W / 2], [hx[1], PIVOT_Y + 0.45, CZ + HANGER_W / 2],
                       f"hanger_strap_{s}", "hangers", "iron"))
    out += pin_x(HANGER_X[0], LINK_X[1] + 0.05, LINK_PIN_Y, CZ, LINK_PIN_R, "hanger_linkpin", "hangers")
    return out


def build_lugs():
    """The riddle's trunnion lugs, iron straps on its rim at the east and west, and the trunnion pins
    through them and the hangers' eyes."""
    out = []
    for s, mx in sides():
        lx = mx(*LUG_X)
        out.append(box([lx[0], LUG_Y[0], LUG_Z[0]], [lx[1], LUG_Y[1], LUG_Z[1]], f"lug_strap_{s}", "lugs", "iron"))
        px = mx(HANGER_X[0] - 0.05, LUG_X[1] + 0.05)
        out += pin_x(px[0], px[1], TRUNNION_Y, CZ, PIN_R, f"lug_pin_{s}", "lugs")
    return out


def build_lever():
    """The hand lever, on the operator's right (west): an oak bar from below its pivot in the west stretcher
    up to its grip, an iron knuckle round the pivot, and the pin the link hangs on."""
    ly, lz = LEVER_PIVOT
    out = [box([LEVER_X[0], STRETCH_Y[0], lz - LEVER_W / 2], [LEVER_X[1], LEVER_TOP, lz + LEVER_W / 2], "lever_bar", "lever", "oak"),
           box([KNUCKLE_X[0], ly - 0.65, lz - 0.55], [KNUCKLE_X[1], ly + 0.65, lz + 0.55], "lever_knuckle", "lever", "iron")]
    out += pin_x(LEVER_X[0], LINK_X[1] + 0.05, LINK_PIN_Y, lz, LINK_PIN_R, "lever_pin", "lever")
    return out


def build_link():
    """The iron link from the lever's pin to the west hanger's: a flat bar, its eye at the lever a slot."""
    _, lz = LEVER_PIVOT
    return [box([LINK_X[0], LINK_Y[0], lz - 0.6], [LINK_X[1], LINK_Y[1], CZ + 0.6], "link_bar", "link", "iron"),
            box([LINK_X[0], SLOT_Y[0], lz - 0.6], [LINK_X[1], SLOT_Y[1], lz + 0.6], "link_eye", "link", "iron")]


def build_stand():
    return (build_hangers() + build_riddle(STAND_Y0) + build_lugs() + build_lever() + build_link() + build_charge(STAND_Y0)
            + build_tub("tub_", "tub") + build_fines() + build_stand_frame())


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


def shake(axis, pivot, amplitude, phase):
    """A turn about `axis` through `pivot` (blocks) by e * amplitude * cos(psi + phase): the shake, its
    amplitude eased in and out with the shaking window, its phase the riddling clock's travel."""
    return {"type": "gauge", "motion": "rotate", "axis": axis, "pivot": pivot, "amount": per_class(0.0),
            "windows": [win(*T_SHAKE)], "lobes": {"ratio": 1.0, "phase": r6(phase), "amplitude": per_class(amplitude)}}


def hand_amplitudes():
    """The shake's two turns (radians) that move the riddle's middle SHAKE_Z to and fro and SHAKE_X side to side."""
    d = SHAKE_PIVOT_Y - (HAND_Y0 + LIFT + RIM_H / 2)
    return math.asin(SHAKE_Z / d), math.asin(SHAKE_X / d)


def stand_amplitudes():
    """The hangers' swing (radians) that carries the riddle SWING to and fro, and the lever's that moves its
    pin as far as the hanger's link pin goes."""
    a = math.asin(SWING / HANGER_L)
    b = -math.asin((PIVOT_Y - LINK_PIN_Y) * math.sin(a) / (LINK_PIN_Y - LEVER_PIVOT[0]))
    return a, b


def charge_parts(ride):
    """The charge's parts, riding the riddle: the bed stays, each layer of fines sinks into the one under it."""
    parts = []
    for _cls, pre, req in CLASSES:
        parts.append({"id": f"{pre}base", "match": [f"{pre}base_*"], "requires": req, "ride": ride, "drivers": []})
    parts.append({"id": "c1top", "match": ["c1top_*"], "requires": "chargesmall", "ride": ride,
                  "drivers": [slide("y", -TOP1_SINK, [win(*T_TOP1)])]})
    parts.append({"id": "c2mid", "match": ["c2mid_*"], "requires": "chargefull", "ride": ride,
                  "drivers": [slide("y", -MID_SINK, [win(*T_MID)])]})
    parts.append({"id": "c2top", "match": ["c2top_*"], "requires": "chargefull", "ride": "c2mid",
                  "drivers": [slide("y", -TOP_SINK, [win(*T_TOP)])]})
    return parts


def fines_parts():
    """The fines in the tub, rising out of its bottom as they fall through."""
    return [{"id": "c1fines", "match": ["c1fines_*"], "requires": "chargesmall", "drivers": [slide("y", F1_RISE, [win(*T_TOP1)])]},
            {"id": "c2fines1", "match": ["c2fines1_*"], "requires": "chargefull", "drivers": [slide("y", F1_RISE, [win(*T_TOP)])]},
            {"id": "c2fines2", "match": ["c2fines2_*"], "requires": "chargefull", "ride": "c2fines1",
             "drivers": [slide("y", F2_RISE, [win(*T_MID)])]}]


def finish(parts):
    for p in parts:
        p.setdefault("ride", None)
        p["requires"] = p.get("requires")
        p.setdefault("drivers", [])
        for d in p["drivers"]:
            validate_driver(d)
    return [{"id": p["id"], "match": p["match"], "requires": p["requires"], "ride": p["ride"], "drivers": p["drivers"]} for p in parts]


def hand_parts():
    az, ax = hand_amplitudes()
    piv = pt(CX, SHAKE_PIVOT_Y, CZ)
    parts = [{"id": "riddle", "match": ["riddle_*"], "requires": "riddle",
              "drivers": [slide("y", LIFT, [win(*T_SHAKE)]),
                          shake("x", piv, az, -math.pi / 2),
                          shake("z", piv, ax, 0.0)]}]
    parts += charge_parts("riddle") + fines_parts()
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None})
    return finish(parts)


def stand_parts():
    a, b = stand_amplitudes()
    ly, lz = LEVER_PIVOT
    parts = [
        {"id": "hangers", "match": ["hanger_*"], "requires": "hangers",
         "drivers": [shake("x", pt(CX, PIVOT_Y, CZ), a, -math.pi / 2)]},
        # it hangs level from its trunnions: turned back about them as far as the hangers turn
        {"id": "riddle", "match": ["riddle_*"], "requires": "riddle", "ride": "hangers",
         "drivers": [shake("x", pt(CX, TRUNNION_Y, CZ), -a, -math.pi / 2)]},
        {"id": "lugs", "match": ["lug_*"], "requires": "hangers", "ride": "riddle"},
        {"id": "lever", "match": ["lever_*"], "requires": "lever",
         "drivers": [shake("x", pt(CX, ly, lz), b, -math.pi / 2)]},
        # the link stays parallel to itself: turned back about its pin on the hanger
        {"id": "link", "match": ["link_*"], "requires": "lever", "ride": "hangers",
         "drivers": [shake("x", pt(CX, LINK_PIN_Y, CZ), -a, -math.pi / 2)]},
    ]
    parts += charge_parts("riddle")
    parts.append({"id": "tub", "match": ["tub_*"], "requires": "tub"})
    parts += fines_parts()
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None})
    return finish(parts)


# ---------------------------------------------------------------- the two models
class Model:
    """What differs between the hand riddle and the stand: names, geometry, rig, pace and anchors."""

    def __init__(self, key, shape_name, rig_name, ref_name, title, y0, shakes, build_fn, parts_fn, infeed, output):
        self.key, self.shape_name, self.rig_name, self.ref_name, self.title = key, shape_name, rig_name, ref_name, title
        self.y0, self.shakes, self.build_fn, self.parts_fn = y0, shakes, build_fn, parts_fn
        self.infeed, self.output = infeed, output

    def charge_y(self):
        return self.y0 + MESH_TOP + BASE[1] / 2


HAND = Model("hand", "riddle", "riddle-rig", "rig-reference", "riddle", HAND_Y0, {"thin": 12.0, "thick": 20.0},
             build_hand, hand_parts, "east", "south")
STAND = Model("stand", "riddlestand", "riddlestand-rig", "stand-rig-reference", "riddle on its stand", STAND_Y0,
              {"thin": 20.0, "thick": 32.0}, build_stand, stand_parts, "east", "south")
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
    return (REST, pose_at(md, 1, 0.0), pose_at(md, 2, 0.0), pose_at(md, 1, 0.3, q), pose_at(md, 2, 0.25, 3 * q),
            pose_at(md, 2, 0.5, 0.7), pose_at(md, 1, 0.6, 2 * q), pose_at(md, 2, 0.6, q), pose_at(md, 1, 0.9),
            pose_at(md, 2, 1.0))


def fix_coplanar(md, els, parts):
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose),
                              coplanar_poses(md))


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("output", "charge")


def make_rig(md, parts):
    progress_of({"work": WORK})
    hand = md is HAND
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, one cell [0,0,0]; the operator stands to the north. "
                    "A hand station: no power cell. work is the riddling of one charge, W 0..1; k is the charge (1 a part "
                    "charge, 2 a full charge); gauge windows are placed in charges. theta is the riddling clock, the "
                    "player's hold-to-work, one turn a shake: nothing reads it but the shake's lobes, through its travel "
                    "psi. " + ("The riddle sits on bearers over the tub and is shaken lifted just off them."
                               if hand else "The riddle hangs from the stand on two hangers, swung by a hand lever through a link.")
                    + " See the riddle's README for the schema.",
        "cells": [],
        "infeedSide": md.infeed,
        "outputSide": md.output,
        "output": {"pos": pt(CX, TUB_BOTTOM[1] + F1[1] + F2[1], CZ)},
        "charge": {"pos": pt(CX, md.charge_y(), CZ)},
        "work": dict(WORK),
        "riddling": {"shakesPerCharge": per_class(md.shakes["thin"], md.shakes["thick"]),
                     "_comment": "shakesPerCharge: the pace, shakes (theta / 2 pi) a charge; a full charge takes longer. "
                                 "Provisional: the gameplay (#714) sets it."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
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


def check_shipped(md, els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    sp = ship["work"]
    poses = [REST, pose_at(md, 1, 0.2), pose_at(md, 2, 0.45), pose_at(md, 1, 0.7), pose_at(md, 2, 0.99), (1.3, 0.3, 1, 0.6)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose), sp), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"{md.key}: shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells {cells}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print(f"FAIL {md.key}: the shipped model is not the checked one moved")
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


def reference_json(md, ship_parts, sp):
    poses = []
    for pose in reference_poses():
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
    """The riddle on the stand is the hand riddle's, element for element, raised: the same rim and mesh."""
    a, b = riddle_elements(hand_els), riddle_elements(stand_els)
    dy = STAND_Y0 - HAND_Y0
    worst = 0.0 if len(a) == len(b) else 1e9
    for x, y in zip(a, b):
        if x.name != y.name:
            worst = 1e9
            break
        worst = max(worst, max(abs(x.size[i] - y.size[i]) for i in range(3)),
                    max(abs(x.c[i] + (dy if i == 1 else 0.0) - y.c[i]) for i in range(3)),
                    max(abs(x.r[i][j] - y.r[i][j]) for i in range(3) for j in range(3)))
        for d in set(x.faces) | set(y.faces):
            fa, fb = x.faces.get(d), y.faces.get(d)
            if fa is None or fb is None or fa["texture"] != fb["texture"]:
                worst = max(worst, 1.0)
            else:
                worst = max(worst, max(abs(p - q) for p, q in zip(fa["uv"], fb["uv"])))
    print(f"the same riddle: {len(a)} elements in each model, the stand's the hand's raised {dy:g}, worst difference {worst:.1e}")
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
            for pose, pairs in before.items():
                print(f"coplanar faces before the fix at {pose}: {len(pairs)} pairs")
            print(f"coplanar faces: {hidden} faces pressed against their own part removed")
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
        ship_els, ship_parts, ship = shipped(els, parts, rig)
        shape, frame_shape = shape_json(md, ship_els), shape_json(md, [el for el in ship_els if el.part == "frame"])
        ship["cells"] = shipped_cells(shape, ship_parts, ship["work"])
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
