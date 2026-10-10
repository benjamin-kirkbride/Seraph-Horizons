#!/usr/bin/env python3
"""Generate the amalgam pan's shapes, rig and reference poses (and its provisional mercury texture).

The amalgam pan is a hand-worked amalgamating pan of the mid 1800s (the Washoe process, scaled down to
one block), driven from below as the Washoe pan was: a wide, shallow sheet-iron pan with a beaded rim on
an oak stand, a hollow cast-iron cone in the middle of its floor, and an iron spindle that rises from a
footstep under the pan, through the cone, to the muller (a hub with four arms, an iron shoe bolted under
each arm's end, dragging on the pan's floor). Under the pan a crown wheel on the spindle is turned by a
pinion on a horizontal crankshaft, which runs out to a crank with an oak handle on the operator's side.
The player holds right-click on the pan, as on the quern, and turns the crank: the muller turns with it,
geared down 3:2, and its shoes grind the concentrate into the mercury on the floor. Its contents are drawn as flat layers on the
floor: a mercury pool, or the grey amalgam paste. Everything is built here from plain boxes; no other
mod's model is used.

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
shipped files are the build frame divided by 16. The pan's axis, the spindle's, is the cell's vertical
centre line; the crankshaft runs north from the gears along the cell's north-south centre line.

The rig's one input, as this station uses it (README "Rig schema"):

    theta  the crank's angle, the hold-to-work clock: the crank turns by it about its shaft, and the gears
           (6 teeth to 9) turn the spindle, the muller and its shoes by 2/3 of it about the pan's axis
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
    "iron": "game:block/metal/plate/iron",          # cast and wrought iron: the cone, the bearings, the gears, the muller, the crank
    "mercury": "seraphhorizons:block/liquid/mercury",   # provisional, made by mercury_texture.py
    "amalgam": "game:block/metal/ingot/leadsolder",     # a dull, mottled grey: the paste
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the pan
CX, CZ = 8.0, 8.0                            # the pan's axis, the spindle's: the cell's centre line
AXIS = (CX, 0.0, CZ)
SIDES = 16                                   # the pan is a 16-sided polygon; every round part's faces are on these angles
PAN_Y0 = 9.0                                 # the pan's underside, on the stand: room under it for the gears and the crank
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
SPINDLE_Y = (3.0, PAN_Y0 + 5.1)              # its foot in the footstep under the pan; its top just over the muller's hub

# ---------------------------------------------------------------- the muller
HUB = (1.35, PAN_Y0 + 3.1, PAN_Y0 + 4.7)     # apothem, from y, to y: keyed on the spindle, 0.2 over the cone
ARM_Y = (PAN_Y0 + 3.15, PAN_Y0 + 3.95)       # the four arms, out from the hub, under the rim
ARM_W = 1.0
ARM_R = (1.0, 5.4)
ARM_ANGLE0 = 45.0                            # the first arm's bearing from south at rest (degrees, about +y); the rest every 90
SHOE_R = (2.3, 5.6)                          # each shoe, under its arm's end: radial extent
SHOE_W = 2.4                                 # across
SHOE_Y = (FLOOR_TOP, ARM_Y[0])               # on the floor, up to the arm

# ---------------------------------------------------------------- the drive: crankshaft, crown wheel and pinion, footstep
# A right-angle drive of the plainest kind: a spur pinion on the crankshaft meshing with a crown wheel (the
# bevel wheel whose pitch cone is flat, its teeth standing straight down from its rim) keyed on the
# spindle. Straight teeth read at block scale where a cone's leaning teeth do not.
SHAFT_Y = 4.5                                # the crankshaft's axis (x = CX, running north-south): its line meets the spindle's
SHAFT_R = 0.5                                # the crankshaft's apothem
SHAFT_Z = (1.9, 6.4)                         # from inside the crank's eye to inside the inner bearing
PINION_TEETH, WHEEL_TEETH = 6, 9
GEAR_RATIO = PINION_TEETH / WHEEL_TEETH      # the spindle's turns per turn of the crank: geared down 3:2
PITCH = 2 * math.pi * 2.0 / PINION_TEETH     # the teeth's circular pitch, the pair's: room for two teeth 0.85 wide and play
R_PINION = PINION_TEETH * PITCH / (2 * math.pi)     # 2.0: its pitch circle's top meets the crown's pitch plane
R_WHEEL = WHEEL_TEETH * PITCH / (2 * math.pi)       # 3.0: the crown's pitch circle, over the pinion's middle
PITCH_Y = SHAFT_Y + R_PINION                 # the crown's pitch plane
PINION_W, CROWN_W = 0.8, 0.75               # the teeth's widths along the pitch line: two and some play in a pitch
FACE, BAND = 1.0, 1.0                        # the pinion's face width (z); the crown's teeth's radial length, over it
ADD, DED = 0.5, 0.75                         # each tooth's addendum and dedendum: the mate's tip clears the root
PINION_Z = (CZ - R_WHEEL - FACE / 2, CZ - R_WHEEL + FACE / 2)    # the pinion's face, inside the crown's teeth's radial band
PINION_BODY = R_PINION - DED                 # its hub to the teeth's roots
CROWN_TEETH_R = (R_WHEEL - BAND / 2, R_WHEEL + BAND / 2)     # the crown's teeth's radial band
CROWN_Y = (PITCH_Y + DED, PITCH_Y + DED + 0.7)     # the crown's web, over its teeth
BEARING_R = 0.95                             # the crankshaft's two bearings: apothem of their bosses
BEARING_O = (3.2, 4.0)                       # the outer bearing (z), on the north sill, before the pinion
BEARING_I = (5.8, 6.6)                       # the inner bearing (z), cast with the footstep, beyond the pinion
PEDESTAL_W = 1.5                             # each bearing's pedestal, across
PEDESTAL_TOP = 0.7                           # its top this far under the shaft's axis: inside the boss, under the shaft
STEPBOX = ((6.4, 9.6), (5.7, 9.4), 0.6)      # the footstep's base plate: x, z; thickness, on the south sill
FOOTSTEP = (1.0, 0.9)                        # the footstep round the spindle's foot: apothem; height over the plate

# ---------------------------------------------------------------- the crank (operator's side, north)
CRANK_R = 3.6                                # the handle's axis from the shaft's
EYE = (BEARING_R, 1.9, BEARING_O[0] - 0.2)   # the crank's eye on the shaft: apothem, from z, to z (0.2 clear of the bearing)
CRANK_Z = (2.0, 2.8)                         # the crank's arm
CRANK_W = 1.0
CRANK_ARM = 4.1                              # the arm, out from the shaft
HANDLE = (0.55, 0.3, CRANK_Z[0])             # apothem, from z, to z: an oak peg, out towards the operator
CRANK_ANGLE0 = 180.0                         # at rest the crank points west, level (degrees about +z from east)

# ---------------------------------------------------------------- the stand
LEG = ((0.6, 2.4), (13.6, 15.4))             # the legs' x (and z) spans: one at each corner, clear of the pan
APRON_Y = (PAN_Y0 - 1.6, PAN_Y0)
APRON = (0.9, 2.1)                           # the aprons' thickness span (west, south); mirrored east; none north, where the crank turns
BEARER_Z = ((2.9, 4.0), (12.0, 13.1))        # two bearers across under the floor, clear of the crown wheel and the crank
BEARER_Y = (PAN_Y0 - 1.4, PAN_Y0)
LOW_Y = (0.9, 2.1)                           # the low rails, west and east, and the two sills across between them
SILL_Z = ((2.9, 4.1), (5.7, 9.4))          # the north sill (the outer bearing), the south sill (the footstep and inner bearing)

# ---------------------------------------------------------------- the contents
CONTENT_R = R_IN + 0.05                      # their edge, just into the wall: no gap at the wall
CONTENT_Y0 = FLOOR_TOP - 0.3                 # their underside, sunk in the floor (never seen)
MERCURY_TOP = FLOOR_TOP + 0.4                # a pool 0.4 deep
AMALGAM_TOP = FLOOR_TOP + 0.8                # a paste 0.8 deep: the shoes stand out of it
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
    """The oak stand: a leg at each corner, aprons west, east and south and two bearers across under the
    pan; low rails west and east, and two sills across between them that carry the drive. The north side
    has no apron: the crank turns there."""
    f = "frame"
    out = []
    for i, (x0, x1) in enumerate(LEG):
        for j, (z0, z1) in enumerate(LEG):
            out.append(box([x0, 0.0, z0], [x1, PAN_Y0, z1], f"fr_leg_{'ns'[j]}{'we'[i]}", f, "oak"))
    a0, a1 = APRON
    inner = (LEG[0][1], LEG[1][0])
    out.append(box([inner[0], APRON_Y[0], 16.0 - a1], [inner[1], APRON_Y[1], 16.0 - a0], "fr_apron_s", f, "oak"))
    out.append(box([a0, APRON_Y[0], inner[0]], [a1, APRON_Y[1], inner[1]], "fr_apron_w", f, "oak"))
    out.append(box([16.0 - a1, APRON_Y[0], inner[0]], [16.0 - a0, APRON_Y[1], inner[1]], "fr_apron_e", f, "oak"))
    for i, (z0, z1) in enumerate(BEARER_Z):
        out.append(box([a1, BEARER_Y[0], z0], [16.0 - a1, BEARER_Y[1], z1], f"fr_bearer_{i + 1}", f, "oak"))
    out.append(box([a0, LOW_Y[0], inner[0]], [a1, LOW_Y[1], inner[1]], "fr_rail_w", f, "oak"))
    out.append(box([16.0 - a1, LOW_Y[0], inner[0]], [16.0 - a0, LOW_Y[1], inner[1]], "fr_rail_e", f, "oak"))
    for s, (z0, z1) in zip("ns", SILL_Z):
        out.append(box([a1, LOW_Y[0], z0], [16.0 - a1, LOW_Y[1], z1], f"fr_sill_{s}", f, "oak"))
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


def build_bearings():
    """The fixed half of the drive, cast iron: the footstep under the spindle's foot on its base plate,
    cast with the crankshaft's inner bearing (a pedestal and a boss) on the south sill, and the outer
    bearing on the north sill."""
    f = "frame"
    (x0, x1), (z0, z1), t = STEPBOX
    plate_y = (LOW_Y[1], LOW_Y[1] + t)
    out = [box([x0, plate_y[0], z0], [x1, plate_y[1], z1], "fr_stepbox", f, "iron")]
    out += disc("y", AXIS, plate_y[1], plate_y[1] + FOOTSTEP[1], FOOTSTEP[0], "fr_footstep", f, "iron")
    shaft = (CX, SHAFT_Y, 0.0)
    for s, (b0, b1), base in (("i", BEARING_I, plate_y[1]), ("o", BEARING_O, LOW_Y[1])):
        out.append(box([CX - PEDESTAL_W / 2, base, b0], [CX + PEDESTAL_W / 2, SHAFT_Y - PEDESTAL_TOP, b1], f"fr_pedestal_{s}", f, "iron"))
        out += disc("z", shaft, b0, b1, BEARING_R, f"fr_bearing_{s}", f, "iron")
    return out


def pinion(name, part):
    """The spur pinion on the crankshaft (about z): a hub to the teeth's roots and PINION_TEETH teeth
    standing out from it, tooth 1 straight up at rest, where it meets the crown wheel."""
    c = (CX, SHAFT_Y, 0.0)
    z0, z1 = PINION_Z
    out = disc("z", c, z0, z1, PINION_BODY, f"{name}_body", part, "iron")
    for i in range(PINION_TEETH):
        t = box([CX - PINION_W / 2, SHAFT_Y + PINION_BODY - 0.2, z0 + 0.03], [CX + PINION_W / 2, SHAFT_Y + R_PINION + ADD, z1 - 0.03],
                f"{name}tooth{i + 1}", part, "iron")
        rotate([t], "z", 360.0 * i / PINION_TEETH, c)
        out.append(t)
    return out


def crown(name, part):
    """The crown wheel keyed on the spindle (about y): a web of the pan's sixteen sides and WHEEL_TEETH
    teeth standing straight down from its rim, a gap to the north at rest so the pinion's top tooth sits
    in it."""
    out = disc("y", AXIS, CROWN_Y[0], CROWN_Y[1], CROWN_TEETH_R[1], f"{name}_web", part, "iron", k=8)
    for i in range(WHEEL_TEETH):
        t = box([CX - CROWN_W / 2, PITCH_Y - ADD, CZ - CROWN_TEETH_R[1]], [CX + CROWN_W / 2, CROWN_Y[0] + 0.05, CZ - CROWN_TEETH_R[0]],
                f"{name}tooth{i + 1}", part, "iron")
        rotate([t], "y", 180.0 / WHEEL_TEETH + 360.0 * i / WHEEL_TEETH, AXIS)
        out.append(t)
    return out


def build_muller():
    """The muller and its spindle, one body turning about the pan's axis: the spindle from its footstep
    up through the cone, the crown wheel keyed on it under the pan, and over the cone the muller's hub
    with four arms and an iron shoe bolted under each arm's end, dragging on the floor."""
    p = "muller"
    out = disc("y", AXIS, SPINDLE_Y[0], SPINDLE_Y[1], SPINDLE_R, "muller_spindle", p, "iron")
    out += crown("muller_crown", p)
    out += disc("y", AXIS, HUB[1], HUB[2], HUB[0], "muller_hub", p, "iron")
    for i in range(4):
        out.append(radial(ARM_R[0], ARM_R[1], ARM_W, ARM_Y[0], ARM_Y[1], ARM_ANGLE0 + 90.0 * i, f"muller_arm{i + 1}", p, "iron"))
    for i in range(4):
        out.append(radial(SHOE_R[0], SHOE_R[1], SHOE_W, SHOE_Y[0], SHOE_Y[1], ARM_ANGLE0 + 90.0 * i, f"muller_shoe{i + 1}", p, "iron"))
    return out


def build_crank():
    """The crankshaft and everything keyed on it: the pinion under the crown wheel, the crank's eye and
    arm on the north end, and the oak handle out towards the operator."""
    p = "crank"
    c = (CX, SHAFT_Y, 0.0)
    out = disc("z", c, SHAFT_Z[0], SHAFT_Z[1], SHAFT_R, "crank_shaft", p, "iron")
    out += pinion("crank_pinion", p)
    out += disc("z", c, EYE[1], EYE[2], EYE[0], "crank_eye", p, "iron")
    arm = box([CX - CRANK_W / 2, SHAFT_Y, CRANK_Z[0]], [CX + CRANK_W / 2, SHAFT_Y + CRANK_ARM, CRANK_Z[1]], "crank_arm", p, "iron")
    handle = disc("z", (CX, SHAFT_Y + CRANK_R, 0.0), HANDLE[1], HANDLE[2], HANDLE[0], "crank_handle", p, "oak")
    rotate([arm] + handle, "z", CRANK_ANGLE0 - 90.0, c)
    return out + [arm] + handle


def build_contents():
    """The contents, flat layers on the floor inside the wall: the mercury pool and the amalgam paste."""
    out = disc("y", AXIS, CONTENT_Y0, MERCURY_TOP, CONTENT_R, "mercury", "mercury", "mercury", k=8)
    out += disc("y", AXIS, CONTENT_Y0, AMALGAM_TOP, CONTENT_R, "amalgam", "amalgam", "amalgam", k=8)
    return out


def build():
    return build_crank() + build_muller() + build_contents() + build_stand() + build_pan() + build_bearings()


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
        # theta is the crank's angle: the crankshaft, and everything keyed on it, turns with the crank
        {"id": "crank", "match": ["crank_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "z", "pivot": pt(CX, SHAFT_Y, CZ), "ratio": 1.0}]},
        # the pinion turns the crown wheel, and the spindle and muller with it, once every two turns of the crank
        {"id": "muller", "match": ["muller_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "y", "pivot": pt(CX, 0.0, CZ), "ratio": GEAR_RATIO}]},
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


CYCLE_TURNS = WHEEL_TEETH // math.gcd(PINION_TEETH, WHEEL_TEETH)    # 3: the crank's turns in which the muller turns whole times


def turn_poses(step_deg=5.0, contents=(0, 1, 2)):
    """The crank's whole cycle (CYCLE_TURNS turns, the muller's two) every step_deg, with each contents."""
    n = int(round(360.0 * CYCLE_TURNS / step_deg))
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
                    "station: no power cell, no work. theta is the crank's angle, the hold-to-work clock: the crank turns by "
                    "it about its shaft (z), and the gears under the pan (6 teeth to 9) turn the muller by 2/3 of it about "
                    "the pan's axis (y). Every part but the contents is always fitted. The contents are requires values: "
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
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod; 'mercury' is the mod's own "
             "provisional texture. Keep element names when editing: the rig finds its parts by them.",
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
