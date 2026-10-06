#!/usr/bin/env python3
"""Generate the rosser's shapes, rig and reference poses.

The rosser is a ring debarker for Logging Expanded's (LE's) trunks. A trunk slides lengthwise
through the machine, from the infeed bed at the west end to the outfeed bed at the east end, and a
spinning cutter ring strips its bark on the way: scraper arms pivoted on the ring close on the
trunk under their springs, and their tips are four bark spud heads. Most of the model is built here
from plain boxes. The entry crown disc and the two rectifier pinions are from Immersive Woodworking's
(IW's) sawmill model by Bobrik00 (see ../../CREDITS.md), as is the tooth every
other toothed wheel is built from (IW's main rotor flange tooth); new boxes borrow IW elements' face
mapping, as the bucking mill's do. So this script reads `build/mods/immersivewoodworking_*.zip` and
`build/mods/LoggingMod*.zip` (the trunks the machine is checked against) and writes,
deterministically,

    rosser.json            the whole machine, every moving part   (assets/.../shapes/block/)
    rosser_frame.json      the static frame only (block and item)  (assets/.../shapes/block/)
    rosser-rig.json        cells, anchors and the part rig         (assets/.../config/)
    rig-reference.json     every part's matrix at a grid of poses  (tests/Rosser/)

or, with `--out DIR`, all four into DIR. Run it from anywhere, after `python3 tools/packtool.py
fetch`. It validates its own output (validate_rosser.py) and exits non-zero if a check fails.

Everything is in voxels in the native, south-facing frame (x west to east along the trunk's
travel, y up, z north to south), measured from the machine box's north-west-bottom corner (the
"build frame"); `shipped` moves it so the controller cell, the middle of the east (outfeed) end at
ground level, is [0,0,0]. The mechanism (the README's tables say the same, part by part):

* Power comes in along native z on the north face beside the ring, and turns IW's crown disc. The
  disc meshes both of IW's pinions, loose on the main shaft (along x, high on the north side), each
  on a one-way catch of opposite hand, so the main shaft always turns one way, whichever way the
  axle turns. A flywheel sits on the entry shaft.
* The main shaft's pinion drives the cutter ring's toothed rim. The ring has no axle: it runs on
  four flanged rollers bearing on its two iron tyres. Four scraper arms are pivoted on its
  downstream face; torsion springs close them until their tails rest on the ring's face, and a
  trunk's nose pushes them open, so their tips (the spud heads) ride its surface.
* Two worms on the main shaft each drive a worm wheel fixed on a lay shaft across the machine. Each
  lay shaft carries two change gears, in constant mesh with two gears loose on the cross shaft
  beside it. A selector dog keyed to the cross shaft has three places: out (nothing turns past it),
  on the fast gear (thin trunks) and on the slow gear (thick trunks). The cross shaft drives the top
  roll through a banjo: a gear on the cross shaft, a gear on the roll, the roll's arms swinging about
  the cross shaft.
* The selector is thrown by a floating lever: the rock shaft pushes its top, its fork moves the
  selector, and it pivots on a pin carried up from the cradle. With the cradle up (no trunk or a
  thin one) the pin is between the two, so the selector goes one way; a thick trunk weighs the
  cradle down, the pin drops below the fork, and the selector goes the other way. The weight that
  lowers the cradle changes the feed's speed.
* The bottom rolls and the bed saddles stand on two cradles tied by one counterweighted rocker
  shaft: a thick trunk weighs them down, so both sizes travel on the ring's axis.
* A waiting trunk presses a treadle plate in the infeed saddle; its lever lifts a pushrod that
  throws the rock shaft (along x, high on the south side) in. Each top roll, raised by the trunk,
  brings an arc on its arm under a finger of the rock shaft, which holds it in; when the last roll
  drops, the rock shaft's weight throws it out and both selectors go to neutral.
* Limb breaker V bars hang in the throat before the ring; a drip pipe from the south face wets the
  trunk ahead of the ring; bark and sticks fall to a chute at the south face.
"""

from __future__ import annotations

import argparse
import copy
import json
import math
import re
import sys
import zipfile
from pathlib import Path

# The generic, machine-free half of this script is shared with the bucking mill's generator.
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Machines" / "tools"))
sys.path.insert(0, str(Path(__file__).resolve().parent))

from machinegen.checks import cell_boxes, cells_touched, with_lids  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import (IDENT, aabb_of, beam, flatten, from_template, metal, pick,  # noqa: E402
                                 rename, rotate, strut, tpl, translate)
from machinegen.output import (element_json, reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               shift_point, worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import CLASSES, part_of, posed, trunk_end, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "Rosser" / "rig-reference.json"   # the site's and the Python tests check the rig maths against these
IW_SHAPE = "assets/immersivewoodworking/shapes/block/sawmill/sawmill.json"
LE_SHAPE = "assets/loggingmod/shapes/treetrunk-{}-no.json"
SCRIPT = "mods-src/seraphhorizons/Rosser/tools/make_shape.py"

B = 16.0                                     # voxels per block

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 16, 4, 5          # machine box (blocks): x 0..15, y 0..3, z 0..4
ORIGIN_CELL = (15, 0, 2)                     # the controller: the middle of the east (outfeed) end, at ground level
STATION_CELLS = (3, 11)                      # x cells with the full 4 x 5 section; the beds either side are 3 x 3
BED_CELLS_Y, BED_CELLS_Z = 3, (1, 3)         # the beds: y cells 0..2, z cells 1..3 (the mill's power feed cell over the east end stays free)
INFEED_SIDE, OUTPUT_SIDE, CHUTE_SIDE = "west", "east", "south"

# ---------------------------------------------------------------- the trunk's path
H, TZ = 27.0, 43.0                           # the trunk's axis (y, z): the ring's centre; z is the mill's bed centreline
NOSE0 = 86.0                                 # a waiting trunk's nose (x), just short of the infeed top roll
TAIL_STOP = 175.5                            # a delivered trunk's tail (x): where the outfeed roll has let the rock shaft go
LENGTHS = {"thin": 4, "thick": 5}            # the shown model's length per class (blocks): LE's lg and xxl
SHOWN = {"thin": "lg", "thick": "xxl"}       # TrunkBox.DisplaySize
# The section's [flats, corners] reach from the axis, centred as the renderer centres LE's model;
# every run measures them from the zip and fails if LE's models moved (to 0.05 voxels).
TRUNK_RADII = {"thin": (7.5, 9.0), "thick": (15.05, 18.5)}
TYPICAL_TURNS = {"thin": 68.0, "thick": 169.0}   # RosserConfig's typical trunks (agent D): the drawn gearing is held to them

# ---------------------------------------------------------------- drive: entry, rectifier, main shaft
G = 6.05 / 2.54                              # IW's crown disc peg ring / pinion peg ring: main shaft turns per axle turn
MAIN_Y, MAIN_Z = 56.0, 29.5                  # the main shaft (along x): high on the north side, 32 from the trunk axis
ENTRY_X = 104.0                              # the entry shaft (along z) and crown disc: the power cell's centre
IW_DISC_X, IW_MAIN_Z = -10.66, 8.0           # IW's crown disc axis (x) and main rotor axis (z)
IW_PEG_TIP_Z = 2.19                          # IW's crown disc peg tips (z)
DISC_PEG_R, MESH_DX, MESH_DZ = 6.05, 0.68, 2.34   # as the mill places IW's pinions on the disc's peg ring
IW_PINION_X = (-13.96, -7.46)
FLYWHEEL_Z = (5.0, 8.0)                      # the flywheel on the entry shaft (z), just inside the north post
FLYWHEEL_R = 5.4

# ---------------------------------------------------------------- the ring
RING_X = (118.0, 124.0)                      # the ring's faces (x)
TEETH_X = (118.0, 120.4)                     # the toothed band (upstream); the tyre band is downstream
TYRE_X = (121.0, 124.0)
BORE_R = 20.5                                # clears the thick trunk's knots (19.45) by 1.05
BODY_R = 23.0                                # the body's rim under the tyres
ROOT_R = 24.0                                # the toothed band's body
RING_PITCH_R, RING_TEETH = 25.0, 50          # circular pitch pi, as every toothed wheel here
TYRE_R = 24.0                                # the tyres' outer surface, the rollers' track
PINION_R, PINION_TEETH = 7.0, 14             # the ring pinion on the main shaft: pitch radius 7, so 25 + 7 = 32
PINION_X = (118.3, 120.1)
ROLLER_RHO = 3.0                             # the four flanged rollers' radius on the tyres
ROLLER_AXIS_R = TYRE_R + ROLLER_RHO
ROLLER_ANGLES = (55.0, -55.0, 135.0, -135.0)   # degrees from +y toward +z: two above, 30 clear of the pinion (-25), two below
ROLLER_FLANGE_R = 3.7                        # the flanges reach 23.3 from the trunk axis, over the body's rim (23)
ROLLER_FLANGE_X = ((120.45, 120.9), (124.1, 124.55))
ROLLER_BEARING_X = ((113.6, 116.6), (125.6, 128.6))
# The scraper arms: four, on pins tangential to the ring on its downstream face, at ring angles 0,
# 90, 180, 270. Each reaches downstream and inward; its spring closes it until its tail rests on
# the ring's face, and a trunk's nose pushes it open.
ARM_PIN_R, ARM_PIN_X = 21.6, 125.3
ARM_LEN = 18.0                               # pin to the tip block's centre
ARM_REST_TIP_R = 5.5                         # closed, the tips' inner faces are this far from the axis
ARM_W = 1.2                                  # the arm's section (iron)
TIP = (2.6, 2.4, 2.4)                        # the spud head block: along the arm, across it (radial), tangential
ARM_ANGLES = (0.0, 90.0, 180.0, 270.0)
ARM_HARMONICS = (1, 2, 3, 4, 6, 8)           # lobes: the section's shape as the tips go round it (LE's sections are not symmetric)
ARM_PRESS = 0.25                             # voxels: the fitted tips may press this far into the outline (the check allows 0.3)

# ---------------------------------------------------------------- feed stations
SHAFT_Y = 48.5                               # the lay and cross shafts (along z), above the thick trunk's top (42.05)
WORM_PITCH_R, WHEEL_PITCH_R, WHEEL_TEETH = 2.5, 5.0, 10   # single-start worm, 2.5 + 5 = 56 - 48.5
WORM_LEAD = 2 * math.pi * WHEEL_PITCH_R / WHEEL_TEETH     # the wheel's circular pitch, pi
WORM_TURNS = 2.5
WORM_ROOT = 1.7                              # the worm's core (its root), clear of the wheel's tips
WHEEL_ADDENDUM = 0.6                         # the wheel's teeth stop 0.1 short of the worm's root
# change gears, (lay, cross) teeth; pitch radius = teeth / 2 (circular pitch pi); both pairs 21 teeth, 10.5 apart
FAST = (13, 14)                              # thin trunks
SLOW = (8, 19)                               # thick trunks
CHANGE_MODULE = 0.7                          # the change gears' pitch radius is teeth x 0.35 (circular pitch 0.7 pi)
CHANGE_DX = (FAST[0] + FAST[1]) * CHANGE_MODULE / 2   # lay to cross shaft (x)
BANJO = (8, 20)                              # cross-shaft gear, roll gear: pitch radii 4 and 10, so the arm is 14
TOP_ARM = (BANJO[0] + BANJO[1]) / 2
ROLL_RHO = 4.0                               # top and bottom feed rolls
ROLL_Z = (22.0, 62.5)                        # the top roll covers its journal out to the arms
ARM_Z = {"n": (20.0, 21.5), "s": (63.0, 64.5)}   # the top-roll arms: just outside the thick trunk's knots (23.55, 62.45)
REST_DROP = 2.0                              # the top roll hangs this far below a thin trunk's top
STATIONS = {
    # the cross shaft (C) and the lay shaft (X). The roll's arms trail: the roll is downstream of its
    # cross shaft, so a nose lifts it up and away.
    "in": {"C": 83.0, "X": 83.0 - CHANGE_DX, "requires": "rollsin"},
    "out": {"C": 157.5, "X": 157.5 - CHANGE_DX, "requires": "rollsout"},
}
BANJO_Z = (65.0, 66.5)                       # the banjo's gears, outboard of the south arm
GEAR_FACE = 1.5
SLOW_Z = (67.0, 68.5)                        # the slow pair (south of the banjo), then the selector, then the fast pair
FAST_Z = (74.3, 75.8)
SEL_Z = 71.6                                 # the selector dog's centre in neutral (z): under the rock shaft
SEL_LEN = 2.0
SEL_R = (1.6, 2.2)                           # the selector's groove and sleeve radius
# the floating lever (beside the selector, x = C + LEVER_DX), its pins' heights
LEVER_DX = 9.6                               # (outside the change gears' tips, so it swings clear of them)
LEVER_P, LEVER_Q = 53.0, SHAFT_Y             # the rock's pin (top) and the fork (at the selector's groove)
LEVER_F_UP = 50.5                            # the cradle's fulcrum pin with the cradle up (between P and Q)
UPRIGHT_DX = 10.9                            # the cradle's fulcrum upright (x = C + UPRIGHT_DX)

# ---------------------------------------------------------------- beds and cradles
SADDLE_UP = H - TRUNK_RADII["thin"][0]       # 19.5: a thin trunk's underside
SADDLE_Z = ((38.5, 41.5), (44.5, 47.5))      # the two skids, under both sizes' flat undersides
CRADLE_X = {"in": (6.0, 100.0), "out": (167.0, 254.0)}
BOTROLL_X = {"in": 95.0, "out": 169.5}
BEARER_X = {"in": (14.0, 38.0, 78.0), "out": (180.0, 210.0, 240.0)}
BEARER_Z = (30.5, 55.5)
TREADLE_X = (54.0, 70.0)                     # the treadle plate, in the infeed saddle's slot
TREADLE_PROUD = 1.5
TREADLE_LEVER_Y = 10.2                       # the treadle lever (along z) under the bed: a thick trunk's plate takes its foot to 1.15

# ---------------------------------------------------------------- rocker, rock, levers
ROCKER_Y, ROCKER_Z = 5.0, 10.0               # the rocker shaft (along x): low on the north side
ROCKER_X = (72.0, 176.0)
ROCKER_ARM_X = {"in": 92.6, "out": 171.5}    # its arms bear under the cradles' north cheeks
ROCK_Y = 57.8                                # the rock shaft (along x): over the selectors (z SEL_Z), its finger over the slow gear
ROCK_X = (56.0, 166.0)
ROCK_ARM = LEVER_P - 4.0                     # (the rock's lever arm hangs from ROCK_Y to LEVER_P)
PUSHROD_X = 60.0
TREADLE_PIVOT_Z = 58.0
FINGER_W = 0.5                               # the rock shaft's hold fingers' width (x)
THROW_THIN = 0.8                             # the selector's throw onto the fast gear (south)
ROLL_DROP_EASE = {("in", "thin"): 20.0, ("out", "thick"): 1.5}     # how far (voxels of tail travel) a top roll takes to drop behind the tail (else 3)
# (a thin trunk's rolls come down slowly, so the infeed's hold lasts until the outfeed's has started)

# ---------------------------------------------------------------- throat
BREAKER_X, BREAKER_Y = 104.0, 47.0           # the breaker bars' cross pin (along z), just over the thick trunk's knots
BREAKER_LEN = 15.0
BREAKER_PIN_Z = (33.0, 64.4)
DRIP_X, DRIP_Y = 112.4, 47.6                 # the drip pipe across the trunk, over the breaker bars when a thick trunk lifts them
DRIP_Z0 = 33.0
CHUTE_X = (128.0, 144.0)                     # the chute's mouth on the south face
CHUTE_BOARDS = ((100.5, 111.5), (129.5, 143.5))   # under the breaker and under the scraper arms (x)
CHUTE_NORTH = (25.0, 3.4)                    # each board's high (north) end (z, y); it falls to the ground at the mouth
CHUTE_MOUTH_Y = 0.8                          # the boards' top at the south face, where bark and sticks leave

# ---------------------------------------------------------------- frame
TOP_Y = (60.0, 64.0)
POST = 4.0
TEXTURES = {"oak": "game:block/wood/debarked/oak", "metal": "game:block/metal/plate/iron"}
TEX_SIZE = 64


# ---------------------------------------------------------------- sources
def load_iw():
    zips = sorted((ROOT / "build" / "mods").glob("immersivewoodworking_*.zip"))
    if not zips:
        sys.exit("no build/mods/immersivewoodworking_*.zip: run `python3 tools/packtool.py fetch` first")
    with zipfile.ZipFile(zips[-1]) as z:
        shape = json.loads(z.read(IW_SHAPE))
    return zips[-1].name, flatten(shape["elements"])


def load_le():
    """LE's lg and xxl trunk models, flattened (voxels, LE's own frame: long axis z)."""
    zips = sorted((ROOT / "build" / "mods").glob("LoggingMod*.zip"))
    if not zips:
        sys.exit("no build/mods/LoggingMod*.zip: run `python3 tools/packtool.py fetch` first")
    out = {}
    with zipfile.ZipFile(zips[-1]) as z:
        for cls, size in SHOWN.items():
            out[cls] = flatten(json.loads(z.read(LE_SHAPE.format(size)))["elements"], textures={})
    return zips[-1].name, out


def trunk_placed(le_els, cls, travel):
    """LE's model placed as the renderer places it (MillMotion.TrunkOnAxis): its bounding box
    centred on the path's axis, its long axis (LE's z) turned onto x, nose at nose0 + T."""
    lo, hi = aabb_of(le_els)
    c = [(lo[k] + hi[k]) / 2 for k in range(3)]
    mid = (NOSE0 / B + travel - LENGTHS[cls] / 2) * B
    out = [el.clone() for el in le_els]
    translate(out, [-v for v in c])
    rotate(out, "y", 90.0, (0.0, 0.0, 0.0))       # LE +z (the nose) to +x, LE x to -z
    translate(out, (mid, H, TZ))
    return out


def section(le_els):
    """The trunk's section seen along its axis, as (y, z) offsets from the axis: every element
    corner, after the renderer's centring and turn (LE x becomes -z)."""
    lo, hi = aabb_of(le_els)
    c = [(lo[k] + hi[k]) / 2 for k in range(3)]
    return [(p[1] - c[1], -(p[0] - c[0])) for el in le_els for p in el.corners()]


def hull(points):
    """Convex hull (Andrew's monotone chain), counter-clockwise."""
    pts = sorted(set((round(a, 6), round(b, 6)) for a, b in points))

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lower, upper = [], []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


def support(points, phi, half_w=0.0):
    """How far the section reaches in the direction phi (radians from +y toward +z), over a band
    `half_w` either side of that ray (the reach a flat face of that half width meets)."""
    d = (math.cos(phi), math.sin(phi))
    t = (-math.sin(phi), math.cos(phi))
    if half_w <= 0:
        return max(y * d[0] + z * d[1] for y, z in points)
    best = -1e9
    pts = densify(points)
    for y, z in pts:
        if abs(y * t[0] + z * t[1]) <= half_w + 1e-9:
            best = max(best, y * d[0] + z * d[1])
    return best


_DENSE = {}


def densify(points, step=0.05):
    key = id(points)
    if key not in _DENSE:
        hl = hull(points)
        out = []
        for i in range(len(hl)):
            a, b2 = hl[i - 1], hl[i]
            n = max(1, int(math.dist(a, b2) / step))
            out += [(a[0] + (b2[0] - a[0]) * j / n, a[1] + (b2[1] - a[1]) * j / n) for j in range(n)]
        _DENSE[key] = out
    return _DENSE[key]


# ---------------------------------------------------------------- box helpers
def box(t, lo, hi, name, part):
    return from_template(t, lo, hi, name, part)


def to_axis(els, axis):
    """Turn elements built along x (see `along_x`) onto `axis`: for z, built x becomes z and built
    z is -x; for y, built x becomes y and built y is -x. Rotation angles about built x become the
    same angles about the new axis."""
    if axis == "z":
        rotate(els, "y", -90.0, (0.0, 0.0, 0.0))
    elif axis == "y":
        rotate(els, "z", 90.0, (0.0, 0.0, 0.0))
    return els


def build_coords(axis, c):
    """The (y, z) to build at along x so `to_axis(axis)` puts the axis through point c (the
    coordinate along the axis is ignored)."""
    if axis == "x":
        return c[1], c[2]
    if axis == "z":
        return c[1], -c[0]
    return -c[0], c[2]


def ngon(t, axis, c, a0, a1, apothem, k, name, part, phase=0.0):
    """A plain round part on `axis` through c, from a0 to a1 along it: k strips of the template,
    each as long as the polygon is across and as wide as one side, turned 180/k degrees apart (k
    = 4 is an octagon). `phase` turns the whole polygon (degrees)."""
    cy, cz = build_coords(axis, c)
    half = apothem * math.tan(math.pi / (2 * k))
    out = []
    for i in range(k):
        el = box(t, [a0, cy - half, cz - apothem], [a1, cy + half, cz + apothem], f"{name}_{i + 1}" if k > 1 else name, part)
        rotate([el], "x", phase + 180.0 * i / k, (0.0, cy, cz))
        out.append(el)
    return to_axis(out, axis)


def radial_boxes(t, axis, c, a0, a1, r0, r1, width, angles, name, part, numbered=True):
    """Boxes from radius r0 to r1 about `axis` through c, `width` across (tangentially), one per
    angle (degrees, about the axis from +y as `to_axis` maps it)."""
    cy, cz = build_coords(axis, c)
    out = []
    for i, ang in enumerate(angles):
        el = box(t, [a0, cy + r0, cz - width / 2], [a1, cy + r1, cz + width / 2], f"{name}{i + 1}" if numbered else name, part)
        rotate([el], "x", ang, (0.0, cy, cz))
        out.append(el)
    return to_axis(out, axis)


def annulus(t, axis, c, a0, a1, r_in, r_out, n, name, part, phase=0.0, stagger=0.02):
    """A ring of n boxes from r_in to r_out (each as wide as a side of the n-gon at r_in, so they
    meet there and leave only tiny notches outside); every other one is a hair shorter along the
    axis, so their ends never share a plane."""
    cy, cz = build_coords(axis, c)
    w = 2 * r_out * math.tan(math.pi / n)
    out = []
    for i in range(n):
        s = stagger if i % 2 else 0.0
        el = box(t, [a0 + s, cy + r_in, cz - w / 2], [a1 - s, cy + r_out, cz + w / 2], f"{name}{i + 1}", part)
        rotate([el], "x", phase + 360.0 * i / n, (0.0, cy, cz))
        out.append(el)
    return to_axis(out, axis)


def gear(t_body, t_tooth, axis, c, a0, a1, pitch_r, n, name, part, phase=0.0, tooth_tilt=0.0, module=1.0, addendum=None):
    """A spur gear (or worm wheel, with `tooth_tilt` degrees about each tooth's radial axis): a
    polygon body to the root radius and n of IW's flange teeth from just inside it to the tip,
    at circular pitch 2 pi pitch_r / n (pi for every gear here)."""
    root = pitch_r - module
    add = module if addendum is None else addendum
    k = 4 if root < 3.0 else 8
    body = ngon(t_body, axis, c, a0, a1, root, k, f"{name}_body", part, phase)
    cy, cz = build_coords(axis, c)
    width = min(1.5 * module, math.pi * pitch_r / n)
    tip = math.sqrt((pitch_r + add) ** 2 - (width / 2) ** 2)    # the tooth's outer corners on the tip circle
    teeth = []
    for i in range(n):
        el = box(t_tooth, [a0 + 0.03, cy + root - 0.4, cz - width / 2], [a1 - 0.03, cy + tip, cz + width / 2],
                 f"{name}_iwtooth{i + 1}", part)
        if tooth_tilt:
            rotate([el], "y", tooth_tilt, ((a0 + a1) / 2, cy + pitch_r, cz))
        rotate([el], "x", phase + 360.0 * i / n, (0.0, cy, cz))
        teeth.append(el)
    return body + to_axis(teeth, axis)


def shaft(t_a, t_b, axis, c, a0, a1, name, part, seg=10.0):
    """A wooden shaft of IW's cross profile (two crossed bars, as the vanilla axle), on `axis`."""
    cy, cz = build_coords(axis, c)
    out = []
    for t, tag in ((t_a, "a"), (t_b, "b")):
        lo, hi = t.aabb()
        hy, hz = (hi[1] - lo[1]) / 2, (hi[2] - lo[2]) / 2
        out += beam(t, [a0, cy - hy, cz - hz], [a1, cy + hy, cz + hz], f"{name}{tag}", part, seg=seg)
    return to_axis(out, axis)


def rod(t, axis, c, a0, a1, r, name, part):
    """A square iron bar of half-size r on `axis`."""
    cy, cz = build_coords(axis, c)
    return to_axis([metal(box(t, [a0, cy - r, cz - r], [a1, cy + r, cz + r], name, part))], axis)


def bar(t, a, b2, w, d, name, part, axis):
    """machinegen's strut, from point a to point b in the plane normal to `axis`."""
    return strut(t, a, b2, w, d, name, part, axis=axis)


# ---------------------------------------------------------------- derived geometry
def top_roll_centre(st, lift):
    """The top roll's axis (x, y) on its arm, `lift` voxels above its rest height."""
    c = STATIONS[st]["C"]
    y = roll_rest_y() + lift
    dy = SHAFT_Y - y
    return c + math.sqrt(TOP_ARM ** 2 - dy ** 2), y


def roll_rest_y():
    return H + TRUNK_RADII["thin"][0] - REST_DROP + ROLL_RHO


def roll_lift(cls):
    """How far a trunk lifts the top roll from rest."""
    return TRUNK_RADII[cls][0] - TRUNK_RADII["thin"][0] + REST_DROP


def arm_angle(st, lift):
    """The top-roll arm's angle about its cross shaft (radians, +z: up toward downstream) at a lift."""
    c = STATIONS[st]["C"]
    x, y = top_roll_centre(st, lift)
    return math.atan2(y - SHAFT_Y, x - c)


def arm_raise(st, cls):
    return arm_angle(st, roll_lift(cls)) - arm_angle(st, 0.0)


def cradle_drop():
    return TRUNK_RADII["thick"][0] - TRUNK_RADII["thin"][0]


def entry_disc_z():
    """The crown disc's offset (z) so its peg tips stop MESH_DZ short of the main shaft's axis."""
    return MAIN_Z - MESH_DZ - IW_PEG_TIP_Z


def lever_geometry():
    """The selector's floating lever: P (the rock's pin, top), Q (the fork, at the selector's
    groove), F (the cradle's fulcrum pin) up (no trunk or thin: between P and Q) or down (thick:
    below Q). The rock shaft moves P north by u; the lever turns about F, so Q goes south (onto the
    fast gear) with F between, and north (onto the slow gear) with F below."""
    f_up, f_down = LEVER_F_UP, LEVER_F_UP - cradle_drop()
    r_thin = (LEVER_Q - f_up) / (LEVER_P - f_up)
    r_thick = (LEVER_Q - f_down) / (LEVER_P - f_down)
    u = THROW_THIN / -r_thin
    a = {"thin": -math.asin(u / (LEVER_P - f_up)), "thick": -math.asin(u / (LEVER_P - f_down))}
    q = {"thin": (LEVER_Q - f_up) * math.sin(a["thin"]), "thick": (LEVER_Q - f_down) * math.sin(a["thick"])}
    rock = math.asin(u / (ROCK_Y - LEVER_P))
    return {"f_up": f_up, "f_down": f_down, "u": u, "angle": a, "throw": q, "rock": rock, "ratio": {"thin": r_thin, "thick": r_thick}}


def treadle_geometry():
    """The treadle lever (pivot along x under the bed): its foot under the plate's stem, its tail
    under the pushrod's slot. The pushrod's thin-trunk rise turns the rock shaft through its whole
    throw at the tappet arm (pointing north from the rock); a thick trunk's extra travel is lost in
    the pushrod's slot."""
    rock = lever_geometry()["rock"]
    y = TREADLE_LEVER_Y
    foot = TZ
    a1 = TREADLE_PIVOT_Z - foot
    drop = {"thin": TREADLE_PROUD, "thick": TREADLE_PROUD + cradle_drop()}
    ang = {cls: math.asin(d / a1) for cls, d in drop.items()}
    a2, tap = 10.0, 3.0
    for _ in range(60):
        rise = a2 * math.sin(ang["thin"])
        tap = rise / math.sin(rock)
        a2 = SEL_Z - tap - TREADLE_PIVOT_Z
    return {"y": y, "foot": foot, "pivot": TREADLE_PIVOT_Z, "tail": TREADLE_PIVOT_Z + a2, "a1": a1, "a2": a2, "angle": ang, "drop": drop,
            "rise": {cls: a2 * math.sin(v) for cls, v in ang.items()}, "tappet": tap}


def hold_geometry(st):
    """The rock shaft's hold finger (pointing north from the rock, level when the rock is in) and
    the arc on the south top-roll arm that comes under it when the roll is raised: an arc about the
    cross shaft, its top at the finger's underside, spanning a thin trunk's raise to a thick one's
    (in the arm's rest position it is all clockwise of the finger, clear of it)."""
    xc = STATIONS[st]["C"]
    fx = xc
    top = ROCK_Y - 0.4                               # the finger's underside, in
    r = top - SHAFT_Y
    thin, thick = arm_raise(st, "thin"), arm_raise(st, "thick")
    half = FINGER_W / 2 / r                         # the finger's half width, as an angle
    a1 = math.pi / 2 - half - 0.8 / r               # at rest its end is clear of the finger
    a0 = math.pi / 2 - thick - 2 * half - 0.05
    n = max(3, int(math.ceil((a1 - a0) / 0.2)))
    arcs = [(a0 + (a1 - a0) * i / n, a0 + (a1 - a0) * (i + 1) / n) for i in range(n)]
    return {"xc": xc, "fx": fx, "r": r, "arcs": arcs, "a0": a0, "a1": a1, "half": half, "release": math.pi / 2 - a1 + half,
            "finger_len": SEL_Z - (ARM_Z["s"][0] + ARM_Z["s"][1]) / 2}


# Iron: pins, wearing surfaces and thin linkage (builders call metal() on most); these too: the feed
# rolls (the trunk runs on them; they are fitted as iron rods), the worms and the selector dogs.
METAL_NAMES = re.compile(r"^(gear_pinion_._catch|toproll_\w+_body|botroll_\w+_body|worm_|sel_)")
# ---------------------------------------------------------------- builders: drive
def build_entry(iw):
    """The entry shaft (IW's cross profile, continuing the vanilla axle) from the north face to
    IW's crown disc, with a plain flywheel just inside the north post."""
    dz = entry_disc_z()
    disc = translate(pick(iw, r"^Rotor_default_3_(01[1-4]|01[7-9]|02[0-7])$"), (ENTRY_X - IW_DISC_X, MAIN_Y - 56.0, dz))
    rename(disc, "entry_", "entry")
    d_lo, _ = aabb_of(disc)
    t_a, t_b = tpl(iw, "MainRotor_twoway_001"), tpl(iw, "MainRotor_twoway_002")
    out = disc + shaft(t_a, t_b, "z", (ENTRY_X, MAIN_Y, 0.0), 0.0, d_lo[2] + 0.4, "entry_shaft_", "entry")
    t_fly = tpl(iw, "MainRotor_twoway_021")
    out += ngon(t_fly, "z", (ENTRY_X, MAIN_Y, 0.0), FLYWHEEL_Z[0], FLYWHEEL_Z[1], FLYWHEEL_R, 8, "entry_flywheel_rim", "entry")
    out += ngon(t_fly, "z", (ENTRY_X, MAIN_Y, 0.0), FLYWHEEL_Z[0] - 0.6, FLYWHEEL_Z[1] + 0.6, 2.4, 4, "entry_flywheel_hub", "entry")
    return out


def build_rectifier(iw):
    """IW's two pinions on the main shaft at the disc's peg ring extremities, each with a one-way
    catch of opposite hand on its outer face, and collars either side of each."""
    pin_w = translate(pick(iw, r"^Rotor_default_1_"), (ENTRY_X - DISC_PEG_R - MESH_DX - IW_PINION_X[0], MAIN_Y - 56.0, MAIN_Z - IW_MAIN_Z))
    pin_e = translate(pick(iw, r"^Rotor_default_2_"), (ENTRY_X + DISC_PEG_R + MESH_DX - IW_PINION_X[1], MAIN_Y - 56.0, MAIN_Z - IW_MAIN_Z))
    rename(pin_w, "gear_pinion_w_", "pinion_w")
    rename(pin_e, "gear_pinion_e_", "pinion_e")
    t_metal = tpl(iw, "sash_001")
    catches = []
    for side, x, hand in (("w", aabb_of(pin_w)[0][0] - 0.2, 1.0), ("e", aabb_of(pin_e)[1][0] + 0.2, -1.0)):
        catches.append(metal(strut(t_metal, [x, MAIN_Y + 1.55, MAIN_Z - 0.3 * hand], [x, MAIN_Y + 2.3, MAIN_Z + 0.5 * hand], 0.35, 0.4,
                                   f"gear_pinion_{side}_catch", f"pinion_{side}", axis="x")))
    t_collar = tpl(iw, "MainRotor_twoway_021")
    collars = []
    for tag, (a, b2) in (("w1", (aabb_of(pin_w)[0][0] - 0.85, aabb_of(pin_w)[0][0] - 0.45)), ("w2", (aabb_of(pin_w)[1][0] + 0.05, aabb_of(pin_w)[1][0] + 0.45)),
                         ("e1", (aabb_of(pin_e)[0][0] - 0.45, aabb_of(pin_e)[0][0] - 0.05)), ("e2", (aabb_of(pin_e)[1][0] + 0.45, aabb_of(pin_e)[1][0] + 0.85))):
        collars += ngon(t_collar, "x", (0.0, MAIN_Y, MAIN_Z), a, b2, 2.0, 4, f"main_collar_{tag}", "main")
    return pin_w + pin_e + catches + collars


def worm(iw, x0, name):
    """A single-start worm on the main shaft from x0: an octagonal core and a helix of short boxes
    (12 per turn) at the worm's pitch radius, its lead the worm wheel's circular pitch."""
    t_tooth = tooth_template(iw)
    t_core = tpl(iw, "MainRotor_twoway_021")
    n = int(12 * WORM_TURNS)
    length = WORM_LEAD * WORM_TURNS
    out = ngon(t_core, "x", (0.0, MAIN_Y, MAIN_Z), x0 - 0.6, x0 + length + 0.6, WORM_ROOT, 4, f"{name}_core", "main")
    lam = math.degrees(math.atan2(WORM_LEAD, 2 * math.pi * WORM_PITCH_R))
    chord = 2 * math.pi * (WORM_PITCH_R + 0.3) / 12 / math.cos(math.radians(lam)) + 0.15
    for i in range(n):
        ang = 360.0 * i / 12
        x = x0 + WORM_LEAD * i / 12
        el = box(t_tooth, [x - 0.45, MAIN_Y + WORM_ROOT - 0.3, MAIN_Z - chord / 2], [x + 0.45, MAIN_Y + WORM_PITCH_R + 0.8, MAIN_Z + chord / 2],
                 f"{name}_iwthread{i + 1}", "main")
        rotate([el], "y", -lam, (x, MAIN_Y + WORM_PITCH_R, MAIN_Z))
        rotate([el], "x", ang, (0.0, MAIN_Y, MAIN_Z))
        out.append(el)
    return out


def tooth_template(iw):
    """IW's main rotor flange tooth (a peg tilted on IW's flange), squared up, as a template for
    every tooth: its faces and UVs."""
    t = tpl(iw, "MainRotor_twoway_013.001").clone()
    t.r = [row[:] for row in IDENT]
    return t


def worm_x(st):
    return STATIONS[st]["X"] - WORM_LEAD * WORM_TURNS / 2


def build_main(iw):
    """The main shaft (IW's cross profile), the ring pinion, both worms and collars."""
    t_a, t_b = tpl(iw, "MainRotor_twoway_001"), tpl(iw, "MainRotor_twoway_002")
    t_body = tpl(iw, "MainRotor_twoway_021")
    out = shaft(t_a, t_b, "x", (0.0, MAIN_Y, MAIN_Z), main_x()[0], main_x()[1], "main_shaft_", "main")
    out += gear(t_body, tooth_template(iw), "x", (0.0, MAIN_Y, MAIN_Z), PINION_X[0], PINION_X[1], PINION_R, PINION_TEETH,
                "ringpinion", "main", phase=pinion_phase())
    for st in STATIONS:
        out += worm(iw, worm_x(st), f"worm_{st}")
    return out


def pinion_phase():
    """The ring pinion's tooth phase (degrees about x) that puts a gap of its own on the line to
    the ring's axis, where a ring tooth sits at rest."""
    ang = math.degrees(math.atan2(TZ - MAIN_Z, H - MAIN_Y))      # from +y toward +z, pinion to ring
    return ang + 180.0 / PINION_TEETH


def ring_mesh_angle():
    """The ring's angle (degrees from +y toward +z) of its mesh with the pinion."""
    return math.degrees(math.atan2(MAIN_Z - TZ, MAIN_Y - H))


# ---------------------------------------------------------------- builders: the ring
def build_ring(iw):
    """The ring: a body annulus from the bore to the rim in four quarter sectors with iron joint
    straps, the toothed band's thicker rim and its 50 teeth (IW's flange tooth), the arms' pin lugs,
    pins and springs, and the two iron tyres (part `ringtyre`)."""
    t_body = tpl(iw, "Frame.119")
    t_tooth = tooth_template(iw)
    t_metal = tpl(iw, "sash_001")
    c = (0.0, H, TZ)
    out = annulus(t_body, "x", c, RING_X[0], RING_X[1], BORE_R, BODY_R - 0.1, 32, "ring_body", "ring")
    out += annulus(t_body, "x", c, TEETH_X[0] + 0.05, TEETH_X[1], BODY_R - 0.3, ROOT_R, 32, "ring_rim", "ring", phase=5.625)
    mesh = ring_mesh_angle()
    width = 1.5
    teeth = radial_boxes(t_tooth, "x", c, TEETH_X[0] + 0.08, TEETH_X[1] - 0.03, ROOT_R - 0.4, RING_PITCH_R + 1.0, width,
                         [mesh + 360.0 * i / RING_TEETH for i in range(RING_TEETH)], "ring_iwtooth", "ring")
    out += teeth
    # the joint straps on the upstream face between the quarters (four sectors, as the game's large gear)
    for i, ang in enumerate((45.0, 135.0, 225.0, 315.0), 1):
        out += [metal(el) for el in radial_boxes(t_metal, "x", c, RING_X[0] - 0.3, RING_X[0], BORE_R + 0.4, BODY_R - 0.6, 1.2, [ang], f"ring_strap{i}", "ring", numbered=False)]
    # tyres: two iron hoops on the tyre band's rim
    tyres = []
    mid = (TYRE_X[0] + TYRE_X[1]) / 2
    for j, (a0, a1) in enumerate(((TYRE_X[0] + 0.05, mid - 0.05), (mid + 0.05, TYRE_X[1] - 0.05)), 1):
        tyres += [metal(el) for el in annulus(t_metal, "x", c, a0, a1, BODY_R - 0.05, TYRE_R, 32, f"ringtyre{j}_", "ringtyre", phase=5.625 * j)]
    out += tyres
    out += build_arm_pins(iw)
    return out


def arm_axis(phi0):
    """The arm at ring angle phi0 (degrees): its pin's rig axis and the sign that opens it."""
    s, c = math.sin(math.radians(phi0)), math.cos(math.radians(phi0))
    t = (0.0, -s, c)                                  # tangential: x × radial
    k = max(range(3), key=lambda i: abs(t[i]))
    return "xyz"[k], math.copysign(1.0, t[k])


def ring_point(phi0, x, r):
    """A point at ring angle phi0 (degrees), radius r from the trunk axis, at x."""
    a = math.radians(phi0)
    return [x, H + r * math.cos(a), TZ + r * math.sin(a)]


def build_arm_pins(iw):
    """For each arm, on the ring's downstream face: two lugs either side of the arm carrying its
    pin (iron), and a coiled torsion spring round the pin either side of the arm."""
    t_body, t_metal = tpl(iw, "Frame.119"), tpl(iw, "sash_001")
    out = []
    for i, phi0 in enumerate(ARM_ANGLES, 1):
        axis, _ = arm_axis(phi0)
        p = ring_point(phi0, ARM_PIN_X, ARM_PIN_R)
        half = ARM_W / 2 + 0.05
        for tag, (s0, s1) in (("a", (-half - 2.6, -half - 1.4)), ("b", (half + 1.4, half + 2.6))):
            out.append(lug_box(t_body, phi0, s0, s1, f"ring_lug{i}{tag}"))
        # the pin through both lugs and the arm's boss
        pin_len = half + 2.6
        out.append(pin_box(t_metal, phi0, -pin_len, pin_len, 0.45, f"ring_pin{i}"))
        for tag, (s0, s1) in (("a", (-half - 1.35, -half - 0.1)), ("b", (half + 0.1, half + 1.35))):
            out += spring_turns(t_metal, phi0, s0, s1, f"ring_spring{i}{tag}")
    return out


def tangential_frame(phi0):
    """Unit vectors (radial, tangential) at ring angle phi0 (degrees)."""
    a = math.radians(phi0)
    return (0.0, math.cos(a), math.sin(a)), (0.0, -math.sin(a), math.cos(a))


def oriented_box(t, centre, axes, half, name, part):
    """An element of half-sizes `half` along the three orthonormal `axes` (columns of its
    rotation), centred at `centre`, with the template's faces cropped to its size."""
    el = box(t, [-half[0], -half[1], -half[2]], [half[0], half[1], half[2]], name, part)
    el.r = [[axes[j][i] for j in range(3)] for i in range(3)]
    el.c = list(centre)
    fix_handed(el)
    return el


def fix_handed(el):
    """Flip the third axis if the rotation is a reflection."""
    r = el.r
    det = (r[0][0] * (r[1][1] * r[2][2] - r[1][2] * r[2][1]) - r[0][1] * (r[1][0] * r[2][2] - r[1][2] * r[2][0])
           + r[0][2] * (r[1][0] * r[2][1] - r[1][1] * r[2][0]))
    if det < 0:
        for i in range(3):
            r[i][2] = -r[i][2]


def lug_box(t, phi0, s0, s1, name):
    """A pin lug on the ring's downstream face: from the face out to the pin, along the tangent
    from s0 to s1."""
    er, et = tangential_frame(phi0)
    centre_s = (s0 + s1) / 2
    x0, x1 = RING_X[1] - 0.2, ARM_PIN_X + 0.9
    r0, r1 = ARM_PIN_R - 0.9, ARM_PIN_R + 0.9
    c = [(x0 + x1) / 2, H + er[1] * (r0 + r1) / 2 + et[1] * centre_s, TZ + er[2] * (r0 + r1) / 2 + et[2] * centre_s]
    return oriented_box(t, c, [(1.0, 0.0, 0.0), er, et], [(x1 - x0) / 2, (r1 - r0) / 2, (s1 - s0) / 2], name, "ring")


def pin_box(t, phi0, s0, s1, r, name):
    er, et = tangential_frame(phi0)
    p = ring_point(phi0, ARM_PIN_X, ARM_PIN_R)
    cs = (s0 + s1) / 2
    c = [p[0], p[1] + et[1] * cs, p[2] + et[2] * cs]
    return metal(oriented_box(t, c, [(1.0, 0.0, 0.0), er, et], [r, r, (s1 - s0) / 2], name, "ring"))


def spring_turns(t, phi0, s0, s1, name):
    """A torsion spring coiled round an arm's pin: three square iron turns."""
    er, et = tangential_frame(phi0)
    p = ring_point(phi0, ARM_PIN_X, ARM_PIN_R)
    out = []
    n = 3
    for j in range(n):
        a = s0 + (s1 - s0) * (j + 0.5) / n
        for k, (u, v, hu, hv) in enumerate(((0.0, 0.85, 0.85, 0.12), (0.0, -0.85, 0.85, 0.12), (0.85, 0.0, 0.12, 0.85), (-0.85, 0.0, 0.12, 0.85))):
            c = [p[0] + u, p[1] + er[1] * v + et[1] * a, p[2] + er[2] * v + et[2] * a]
            out.append(metal(oriented_box(t, c, [(1.0, 0.0, 0.0), er, et], [hu, hv, 0.12], f"{name}_{j + 1}{'abcd'[k]}", "ring")))
    return out


def arm_geometry():
    """The scraper arm in its own plane, as (x along the trunk, r from the axis): the pin, the
    arm's rest angle below the axis (radians), and the tip block's inner reach as a function of
    the opening angle."""
    # rest: the tip block's inner face at ARM_REST_TIP_R
    def tip_min_r(beta):
        u = (math.cos(beta), -math.sin(beta))          # along the arm
        n = (math.sin(beta), math.cos(beta))           # across it, outward
        cx, cr = ARM_PIN_X + ARM_LEN * u[0], ARM_PIN_R + ARM_LEN * u[1]
        return min(cr + su * TIP[0] / 2 * u[1] + sn * TIP[1] / 2 * n[1] for su in (-1, 1) for sn in (-1, 1))
    lo, hi = 0.05, 1.5
    for _ in range(60):
        mid = (lo + hi) / 2
        lo, hi = (mid, hi) if tip_min_r(mid) > ARM_REST_TIP_R else (lo, mid)
    return {"beta0": (lo + hi) / 2, "tip_min_r": tip_min_r}


def build_arms(iw):
    """Each arm: an iron bar from its boss on the pin to the tip, a short tail past the pin that
    rests on the ring's face when closed (the stop), and the spud head (part tipN), authored closed."""
    t_metal = tpl(iw, "leveler_metal_static_003")
    geo = arm_geometry()
    beta = geo["beta0"]
    out = []
    for i, phi0 in enumerate(ARM_ANGLES, 1):
        er, et = tangential_frame(phi0)
        u = [math.cos(beta), -math.sin(beta) * er[1], -math.sin(beta) * er[2]]
        n = [math.sin(beta), math.cos(beta) * er[1], math.cos(beta) * er[2]]
        p = ring_point(phi0, ARM_PIN_X, ARM_PIN_R)
        arm_end = ARM_LEN - TIP[0] / 2
        tail = 1.1
        c = [p[k] + u[k] * (arm_end - tail) / 2 for k in range(3)]
        out.append(metal(oriented_box(t_metal, c, [u, n, et], [(arm_end + tail) / 2, ARM_W / 2, ARM_W / 2], f"arm{i}_bar", f"arm{i}")))
        out.append(metal(oriented_box(t_metal, p, [u, n, et], [0.9, 0.9, ARM_W / 2 + 0.05], f"arm{i}_boss", f"arm{i}")))
        tc = [p[k] + u[k] * ARM_LEN for k in range(3)]
        out.append(metal(oriented_box(t_metal, tc, [u, n, et], [TIP[0] / 2, TIP[1] / 2, TIP[2] / 2], f"tip{i}_head", f"tip{i}")))
    return out


def build_rollers(iw):
    """Four plain iron rollers on the tyres, each with a flange either side of the tyre band, on a
    pin running in two bearing blocks (frame)."""
    t_metal = tpl(iw, "sash_001")
    out = []
    for i, ang in enumerate(ROLLER_ANGLES, 1):
        p = ring_point(ang, 0.0, ROLLER_AXIS_R)
        c = (0.0, p[1], p[2])
        out += [metal(el) for el in ngon(t_metal, "x", c, TYRE_X[0] + 0.05, TYRE_X[1] - 0.05, ROLLER_RHO, 4, f"roller{i}_core", f"roller{i}")]
        for j, (a0, a1) in enumerate(ROLLER_FLANGE_X, 1):
            out += [metal(el) for el in ngon(t_metal, "x", c, a0, a1, ROLLER_FLANGE_R, 4, f"roller{i}_flange{j}", f"roller{i}", phase=22.5)]
        out += rod(t_metal, "x", c, ROLLER_BEARING_X[0][0] - 0.3, ROLLER_BEARING_X[1][1] + 0.3, 0.6, f"roller{i}_pin", f"roller{i}")
    return out


# ---------------------------------------------------------------- builders: throat
def breaker_rest():
    """The breaker bars' rest angle below the horizontal (radians), leaning downstream."""
    return math.radians(60.0)


def breaker_bar_ends(side):
    """A V bar's ends (at rest): from the hub on the pin, splayed outward and down-downstream."""
    sgn = -1.0 if side == "n" else 1.0
    rest = breaker_rest()
    a = [BREAKER_X, BREAKER_Y, TZ + sgn * 1.4]
    b2 = [BREAKER_X + BREAKER_LEN * math.cos(rest), BREAKER_Y - BREAKER_LEN * math.sin(rest), TZ + sgn * 6.5]
    return a, b2


def build_breaker(iw):
    """Two iron V bars on one cross pin (along z): splayed so that, seen end on, they form a V
    opening downward over the trunk; they lean downstream so a nose rides under them and lifts
    them. Their pin runs in two hangers either side of the trunk."""
    t_metal = tpl(iw, "leveler_metal_static_003")
    out = []
    for side in ("n", "s"):
        a, b2 = breaker_bar_ends(side)
        d = [b2[k] - a[k] for k in range(3)]
        ln = math.sqrt(sum(v * v for v in d))
        u = [v / ln for v in d]
        w = [0.0, 0.0, 1.0]
        n = [u[1] * w[2] - u[2] * w[1], u[2] * w[0] - u[0] * w[2], u[0] * w[1] - u[1] * w[0]]
        nl = math.sqrt(sum(v * v for v in n))
        n = [v / nl for v in n]
        w = [u[1] * n[2] - u[2] * n[1], u[2] * n[0] - u[0] * n[2], u[0] * n[1] - u[1] * n[0]]
        c = [(a[k] + b2[k]) / 2 for k in range(3)]
        out.append(metal(oriented_box(t_metal, c, [u, n, w], [ln / 2 + 0.5, 0.55, 0.8], f"breaker_bar_{side}", "breaker")))
    out += rod(t_metal, "z", (BREAKER_X, BREAKER_Y, 0.0), BREAKER_PIN_Z[0], BREAKER_PIN_Z[1], 0.5, "breaker_pin", "breaker")
    out += [metal(el) for el in ngon(t_metal, "z", (BREAKER_X, BREAKER_Y, 0.0), TZ - 2.6, TZ + 2.6, 1.1, 4, "breaker_hub", "breaker")]
    return out


def build_chute(iw):
    """Two oak boards that catch what the breaker and the scraper arms take off and slope down
    south to the ground, through a gap in the south sill (the chute's mouth); each stands on a short
    post at its high end and on the ground at the other."""
    t_bar, t_post = tpl(iw, "Frame.119"), tpl(iw, "Frame.011")
    zmax = CELLS_Z * B
    z0, y0 = CHUTE_NORTH
    z1, y1 = zmax - 0.3, CHUTE_MOUTH_Y - 0.6
    out = []
    for i, (x0, x1) in enumerate(CHUTE_BOARDS, 1):
        a, b2 = [(x0 + x1) / 2, y0, z0], [(x0 + x1) / 2, y1, z1]
        out.append(bar(t_bar, [a[0], a[1] + 0.5, a[2]], [b2[0], b2[1] + 0.5, b2[2]], 1.0, x1 - x0, f"fr_chute{i}_board", "frame", "x"))
        out += beam(t_post, [x0 + 2.0, 0.0, z0 - 0.2], [x1 - 2.0, y0 + 0.2, z0 + 1.6], f"fr_chute{i}_post", "frame")
    return out


def build_drip(iw):
    """The drip pipe across the trunk, from a flange on the south face, with a row of drip nozzles
    under it, on a hanger at its north end and a strap to the ring station's south post."""
    t_metal = tpl(iw, "sash_001")
    zmax = CELLS_Z * B
    out = rod(t_metal, "z", (DRIP_X, DRIP_Y, 0.0), DRIP_Z0, zmax, 0.55, "fr_drip_pipe", "frame")
    for i in range(5):
        z = TZ - 8.0 + 4.0 * i
        out.append(metal(box(t_metal, [DRIP_X - 0.3, DRIP_Y - 1.05, z - 0.3], [DRIP_X + 0.3, DRIP_Y - 0.5, z + 0.3], f"fr_drip_nozzle{i + 1}", "frame")))
    out.append(metal(box(t_metal, [DRIP_X - 0.9, DRIP_Y - 0.9, zmax - 0.8], [ring_post_x()[0][0] + 0.5, DRIP_Y + 0.9, zmax - 0.2], "fr_drip_flange", "frame")))
    out.append(metal(box(t_metal, [DRIP_X - 0.7, DRIP_Y - 0.7, DRIP_Z0 + 0.4], [DRIP_X + 0.7, TOP_Y[0], DRIP_Z0 + 1.6], "fr_drip_hanger", "frame")))
    return out


def ring_post_x():
    """The ring station's posts (x spans), up and downstream of the ring."""
    return (113.0, 117.0), (125.0, 129.0)


# ---------------------------------------------------------------- builders: feed stations
def build_station(iw, st):
    """One feed station: the lay shaft with its worm wheel and change gears (all fixed on it), the
    cross shaft with its two loose change gears either side of the selector dog and the banjo gear,
    and the top roll on its arms."""
    s = STATIONS[st]
    xc, xx = s["C"], s["X"]
    t_body, t_tooth = tpl(iw, "MainRotor_twoway_021"), tooth_template(iw)
    t_a, t_b = tpl(iw, "MainRotor_twoway_001"), tpl(iw, "MainRotor_twoway_002")
    t_metal = tpl(iw, "sash_001")
    cx = (xx, SHAFT_Y, 0.0)
    cc = (xc, SHAFT_Y, 0.0)
    zmax = CELLS_Z * B
    lam = math.degrees(math.atan2(WORM_LEAD, 2 * math.pi * WORM_PITCH_R))
    out = []
    # the lay shaft: its worm wheel under the worm, its change gears; collars locate them
    out += shaft(t_a, t_b, "z", cx, 0.6, zmax - 0.6, f"lay_{st}_shaft_", f"lay_{st}")
    out += gear(t_body, t_tooth, "z", cx, MAIN_Z - 1.0, MAIN_Z + 1.0, WHEEL_PITCH_R, WHEEL_TEETH, f"lay_{st}_wheel", f"lay_{st}",
                phase=180.0 / WHEEL_TEETH, tooth_tilt=lam, addendum=WHEEL_ADDENDUM)
    out += ngon(t_body, "z", cx, MAIN_Z - 2.2, MAIN_Z + 2.2, 2.0, 4, f"lay_{st}_wheelboss", f"lay_{st}")
    cm = CHANGE_MODULE
    out += gear(t_body, t_tooth, "z", cx, SLOW_Z[0], SLOW_Z[1], SLOW[0] * cm / 2, SLOW[0], f"lay_{st}_slow", f"lay_{st}", module=cm)
    out += gear(t_body, t_tooth, "z", cx, FAST_Z[0], FAST_Z[1], FAST[0] * cm / 2, FAST[0], f"lay_{st}_fast", f"lay_{st}", module=cm)
    out += ngon(t_body, "z", cx, SLOW_Z[1], FAST_Z[0], 1.8, 4, f"lay_{st}_spacer", f"lay_{st}")
    # the cross shaft
    out += shaft(t_a, t_b, "z", cc, 0.6, zmax - 0.6, f"cross_{st}_shaft_", f"cross_{st}")
    out += gear(t_body, t_tooth, "z", cc, BANJO_Z[0], BANJO_Z[1], BANJO[0] / 2, BANJO[0], f"cross_{st}_banjo", f"cross_{st}")
    out += ngon(t_metal, "z", cc, FAST_Z[1] + 0.05, FAST_Z[1] + 0.45, 2.0, 4, f"cross_{st}_collar", f"cross_{st}")
    sel = selector_faces()
    out += gear(t_body, t_tooth, "z", cc, SLOW_Z[0], SLOW_Z[1], SLOW[1] * cm / 2, SLOW[1], f"slow_{st}", f"slow_{st}", phase=180.0 / SLOW[1], module=cm)
    out += ngon(t_body, "z", cc, SLOW_Z[1], sel["slow"], 2.0, 4, f"slow_{st}_hub", f"slow_{st}")
    out += gear(t_body, t_tooth, "z", cc, FAST_Z[0], FAST_Z[1], FAST[1] * cm / 2, FAST[1], f"fast_{st}", f"fast_{st}", phase=180.0 / FAST[1], module=cm)
    out += ngon(t_body, "z", cc, sel["fast"], FAST_Z[0], 2.0, 4, f"fast_{st}_hub", f"fast_{st}")
    out += dog(t_body, t_metal, "z", cc, SEL_Z - SEL_LEN / 2, SEL_LEN, SEL_R, f"sel_{st}", f"sel_{st}")
    out += build_top_arm(iw, st)
    out += build_lever(iw, st)
    return out


def selector_faces():
    """The loose gears' hub faces the selector's dogs meet: the slow gear's (north) and the fast's (south)."""
    lg = lever_geometry()
    return {"slow": SEL_Z - SEL_LEN / 2 + lg["throw"]["thick"], "fast": SEL_Z + SEL_LEN / 2 + lg["throw"]["thin"]}


def dog(t_body, t_metal, axis, c, a0, length, radii, name, part):
    """A dog clutch sleeve on `axis` from a0, with a groove in its middle for its fork and two small
    iron dogs on each face."""
    g, r = radii
    mid = a0 + length / 2
    out = ngon(t_body, axis, c, mid - 0.45, mid + 0.45, g, 4, f"{name}_groove", part)
    out += ngon(t_body, axis, c, a0, mid - 0.45, r, 4, f"{name}_sleeve_a", part)
    out += ngon(t_body, axis, c, mid + 0.45, a0 + length, r, 4, f"{name}_sleeve_b", part)
    cy, cz = build_coords(axis, c)
    for fa, sg in ((a0, -1), (a0 + length, 1)):
        for i, dy in enumerate((1.2, -1.2), 1):
            a1, a2 = (fa - 0.25, fa) if sg < 0 else (fa, fa + 0.25)
            el = metal(box(t_metal, [a1, cy + dy - 0.3, cz - 0.3], [a2, cy + dy + 0.3, cz + 0.3], f"{name}_dog{'ab'[sg > 0]}{i}", part))
            out += to_axis([el], axis)
    return out


def build_top_arm(iw, st):
    """The top roll's arms (one each side of the trunk), each from a boss on the cross shaft to the
    roll's journal and on to a cast weight; a tie bar joins them over the roll. The south arm
    carries the hold arc. The roll, its journal and its banjo gear (part toproll). Authored at rest."""
    xc = STATIONS[st]["C"]
    t_bar, t_metal = tpl(iw, "Frame.119"), tpl(iw, "sash_001")
    t_body, t_tooth = tpl(iw, "MainRotor_twoway_021"), tooth_template(iw)
    xr, yr = top_roll_centre(st, 0.0)
    ang = math.atan2(yr - SHAFT_Y, xr - xc)
    u = (math.cos(ang), math.sin(ang))
    out = []
    ext = TOP_ARM + 2.4
    for side, (z0, z1) in ARM_Z.items():
        zc = (z0 + z1) / 2
        out.append(bar(t_bar, [xc + 2.0 * u[0], SHAFT_Y + 2.0 * u[1], zc], [xc + ext * u[0], SHAFT_Y + ext * u[1], zc], 2.2, z1 - z0, f"toparm_{st}_{side}_bar", f"toparm_{st}", "z"))
        out += ngon(t_bar, "z", (xc, SHAFT_Y, 0.0), z0 - 0.05, z1 + 0.05, 2.4, 4, f"toparm_{st}_{side}_boss", f"toparm_{st}")
        wc = [xc + (TOP_ARM + 3.2) * u[0], SHAFT_Y + (TOP_ARM + 3.2) * u[1]]
        out.append(metal(bar(t_metal, [wc[0] - 1.4 * u[0], wc[1] - 1.4 * u[1], zc], [wc[0] + 1.4 * u[0], wc[1] + 1.4 * u[1], zc],
                             3.2, z1 - z0 + 0.8, f"toparm_{st}_{side}_weight", f"toparm_{st}", "z")))
    tc = [xc + TOP_ARM * 0.3 * u[0], SHAFT_Y + TOP_ARM * 0.3 * u[1]]
    out.append(box(t_bar, [tc[0] - 1.0, tc[1] - 0.8, ARM_Z["n"][1] - 0.2], [tc[0] + 1.0, tc[1] + 0.8, ARM_Z["s"][0] + 0.2], f"toparm_{st}_tie", f"toparm_{st}"))
    out += hold_arc(t_metal, st)
    rc = (xr, yr, 0.0)
    out += ngon(t_body, "z", rc, ROLL_Z[0], ROLL_Z[1], ROLL_RHO, 8, f"toproll_{st}_body", f"toproll_{st}")
    out += rod(t_metal, "z", rc, ARM_Z["n"][0] - 0.3, BANJO_Z[1] + 0.3, 0.7, f"toproll_{st}_journal", f"toproll_{st}")
    out += gear(t_body, t_tooth, "z", rc, BANJO_Z[0], BANJO_Z[1], BANJO[1] / 2, BANJO[1], f"toproll_{st}_gear", f"toproll_{st}", phase=180.0 / BANJO[1])
    return out


def hold_arc(t, st):
    """The hold arc on the south arm's boss: iron segments of an arc about the cross shaft whose
    top meets the rock shaft's finger, with a web down to the boss."""
    geo = hold_geometry(st)
    z0, z1 = ARM_Z["s"]
    zc = (z0 + z1) / 2
    out = []
    for i, (a0, a1) in enumerate(geo["arcs"], 1):
        am = (a0 + a1) / 2
        rm = geo["r"] - 0.6
        half = (geo["r"]) * math.tan((a1 - a0) / 2) + 0.05
        c = [geo["xc"] + rm * math.cos(am), SHAFT_Y + rm * math.sin(am), zc]
        er = [math.cos(am), math.sin(am), 0.0]
        et = [-math.sin(am), math.cos(am), 0.0]
        out.append(metal(oriented_box(t, c, [et, er, [0.0, 0.0, 1.0]], [half, 0.6, (z1 - z0) / 2], f"toparm_{st}_arc{i}", f"toparm_{st}")))
    for j, a in enumerate((geo["a0"] + 0.06,), 1):
        out.append(metal(bar(t, [geo["xc"] + 2.0 * math.cos(a), SHAFT_Y + 2.0 * math.sin(a), zc],
                             [geo["xc"] + (geo["r"] - 1.0) * math.cos(a), SHAFT_Y + (geo["r"] - 1.0) * math.sin(a), zc], 0.9, z1 - z0, f"toparm_{st}_web{j}", f"toparm_{st}", "z")))
    return out


def build_lever(iw, st):
    """The selector's floating lever (beside the selector dog): a slotted iron bar from below the
    cradle's lowest fulcrum to above the rock's pin, with a fork whose prongs ride in the selector's
    groove."""
    t_metal = tpl(iw, "leveler_metal_static_003")
    lg = lever_geometry()
    x = STATIONS[st]["C"] + LEVER_DX
    out = [metal(box(t_metal, [x - 0.4, lg["f_down"] - 1.2, SEL_Z - 0.6], [x + 0.4, LEVER_P + 1.2, SEL_Z + 0.6], f"sellever_{st}_bar", f"sellever_{st}"))]
    xc = STATIONS[st]["C"]
    for i, dy in enumerate((1.0, -1.0), 1):
        out.append(metal(box(t_metal, [xc + 1.45, LEVER_Q + dy * 1.3 - 0.2, SEL_Z - 0.35], [x - 0.4, LEVER_Q + dy * 1.3 + 0.2, SEL_Z + 0.35], f"sellever_{st}_prong{i}", f"sellever_{st}")))
    return out


# ---------------------------------------------------------------- builders: beds, cradles
def build_cradle(iw, st):
    """A cradle, authored up (a thin trunk's or no trunk's height): two oak saddle skids (with a slot
    between them for the treadle plate at the infeed), bearers under them whose tongues run in
    grooved guide posts, cheeks carrying the bottom roll's journal at the throat end, a pad the
    rocker's arm bears under, and the iron upright carrying the selector lever's fulcrum pin; the
    bottom roll (part botroll)."""
    t_bar, t_body, t_metal = tpl(iw, "Frame.119"), tpl(iw, "MainRotor_twoway_021"), tpl(iw, "leveler_metal_static_003")
    x0, x1 = CRADLE_X[st]
    xb = BOTROLL_X[st]
    part = f"cradle_{st}"
    top = SADDLE_UP
    out = []
    sx0, sx1 = (x0, xb - ROLL_RHO - 1.0) if st == "in" else (xb + ROLL_RHO + 1.0, x1)
    for j, (z0, z1) in enumerate(SADDLE_Z, 1):
        if st == "in" and j == 2:                # the treadle lever passes under the bed through a gap in this skid
            g0, g1 = PUSHROD_X - 1.4, PUSHROD_X + 1.4
            out += beam(t_bar, [sx0, top - 3.0, z0], [g0, top, z1], f"cradle_{st}_skid{j}w", part)
            out += beam(t_bar, [g1, top - 3.0, z0], [sx1, top, z1], f"cradle_{st}_skid{j}e", part)
        else:
            out += beam(t_bar, [sx0, top - 3.0, z0], [sx1, top, z1], f"cradle_{st}_skid{j}", part)
    for j, bx in enumerate(BEARER_X[st], 1):
        out.append(box(t_bar, [bx - 2.0, top - 5.0, BEARER_Z[0]], [bx + 2.0, top - 3.0, BEARER_Z[1]], f"cradle_{st}_bearer{j}", part))
        for side, z in (("n", BEARER_Z[0]), ("s", BEARER_Z[1])):
            zz = (z - 1.5, z) if side == "n" else (z, z + 1.5)
            out.append(box(t_bar, [bx - 0.75, top - 11.0, zz[0]], [bx + 0.75, top - 3.0, zz[1]], f"cradle_{st}_tongue{j}{side}", part))
    cx0, cx1 = cheek_x(st)
    for side, (z0, z1) in (("n", (SADDLE_Z[0][0] - 6.0, SADDLE_Z[0][0] - 3.5)), ("s", (SADDLE_Z[1][1] + 3.5, SADDLE_Z[1][1] + 6.0))):
        out.append(box(t_bar, [cx0, top - ROLL_RHO - 3.0, z0], [cx1, top - 2.0, z1], f"cradle_{st}_cheek_{side}", part))
    xe = cx0 if st == "in" else cx1 - 4.0
    out.append(box(t_bar, [xe, top - ROLL_RHO - 5.0, SADDLE_Z[0][0] - 6.0], [xe + 4.0, top - ROLL_RHO - 3.0, SADDLE_Z[1][1] + 6.0], f"cradle_{st}_crossbar", part))
    # the skids' end rests on the crossbar
    rc = (xb, top - ROLL_RHO, 0.0)
    out += ngon(t_body, "z", rc, SADDLE_Z[0][0] - 3.4, SADDLE_Z[1][1] + 3.4, ROLL_RHO, 8, f"botroll_{st}_body", f"botroll_{st}")
    out += rod(t_metal, "z", rc, SADDLE_Z[0][0] - 6.3, SADDLE_Z[1][1] + 6.3, 0.7, f"botroll_{st}_journal", f"botroll_{st}")
    # the fulcrum upright: out from the south cheek under the station, up beside the lever
    ux = STATIONS[st]["C"] + UPRIGHT_DX
    lg = lever_geometry()
    cz1 = SADDLE_Z[1][1] + 6.0
    yb = top - ROLL_RHO - 3.0
    out.append(metal(box(t_metal, [ux - 0.5, yb, cz1 - 0.5], [ux + 0.5, yb + 1.4, SEL_Z + 0.5], f"cradle_{st}_upright_foot", part)))
    out.append(metal(box(t_metal, [ux - 0.5, yb + 1.4, SEL_Z - 0.5], [ux + 0.5, lg["f_up"] + 0.8, SEL_Z + 0.5], f"cradle_{st}_upright", part)))
    out.append(metal(box(t_metal, [STATIONS[st]["C"] + LEVER_DX - 0.6, lg["f_up"] - 0.3, SEL_Z - 0.3], [ux - 0.5, lg["f_up"] + 0.3, SEL_Z + 0.3], f"cradle_{st}_fulcrum", part)))
    if ux < cx0 or ux > cx1:
        out.append(box(t_bar, [min(ux - 0.5, cx0), yb, cz1 - 2.5], [max(ux + 0.5, cx1), yb + 1.4, cz1], f"cradle_{st}_upright_rail", part))
    return out


def cheek_x(st):
    xb = BOTROLL_X[st]
    if st == "in":
        return xb - 9.0, xb + 4.5
    return STATIONS[st]["C"] + UPRIGHT_DX - 1.0, xb + 9.0


# ---------------------------------------------------------------- builders: levers
ROCKER_ROLLER = 0.8                          # the roller on each rocker arm's tip, under the cradle's cheek


def rocker_tip(st):
    """The rocker arm's tip roller's axis, cradle up (y, z): the roller touches the south cheek's underside."""
    return SADDLE_UP - ROLL_RHO - 3.0 - ROCKER_ROLLER, SADDLE_Z[0][0] - 5.6


def rocker_geometry(st):
    ty, tz = rocker_tip(st)
    ln = math.hypot(ty - ROCKER_Y, tz - ROCKER_Z)
    a0 = math.atan2(ty - ROCKER_Y, tz - ROCKER_Z)
    a1 = math.asin((ty - cradle_drop() - ROCKER_Y) / ln)
    return {"len": ln, "a0": a0, "a1": a1, "turn": a0 - a1}


def build_rocker(iw):
    """The rocker shaft along x, low on the south side, its arm under each cradle's south cheek,
    and a counterweight on the other side of the shaft at each arm, heavier than a thin trunk."""
    t_metal = tpl(iw, "leveler_metal_static_003")
    out = rod(t_metal, "x", (0.0, ROCKER_Y, ROCKER_Z), ROCKER_X[0], ROCKER_X[1], 0.7, "rocker_shaft", "rocker")
    for st in STATIONS:
        bx = ROCKER_ARM_X[st]
        ty, tz = rocker_tip(st)
        d = math.hypot(ty - ROCKER_Y, tz - ROCKER_Z)
        end = [bx, ROCKER_Y + (ty - ROCKER_Y) * (d - ROCKER_ROLLER) / d, ROCKER_Z + (tz - ROCKER_Z) * (d - ROCKER_ROLLER) / d]
        out.append(metal(bar(t_metal, [bx, ROCKER_Y, ROCKER_Z], end, 1.0, 1.0, f"rocker_arm_{st}", "rocker", "x")))
        out += [metal(e) for e in ngon(t_metal, "x", (0.0, ty, tz), bx - 1.1, bx - 0.55, ROCKER_ROLLER, 4, f"rocker_pad_{st}a", "rocker")]
        out += [metal(e) for e in ngon(t_metal, "x", (0.0, ty, tz), bx + 0.55, bx + 1.1, ROCKER_ROLLER, 4, f"rocker_pad_{st}b", "rocker")]
        w = [bx, ROCKER_Y + 2.4, ROCKER_Z - 4.0]
        out.append(metal(bar(t_metal, [bx, ROCKER_Y, ROCKER_Z], w, 1.0, 1.0, f"rocker_cwarm_{st}", "rocker", "x")))
        out.append(metal(box(t_metal, [bx - 2.4, w[1] - 1.6, w[2] - 1.6], [bx + 2.4, w[1] + 1.6, w[2] + 1.6], f"rocker_cw_{st}", "rocker")))
    return out


def build_treadle(iw):
    """The treadle plate in the infeed saddle's slot on two stems, its lever (pivot along x under
    the bed) and the slotted pushrod up the south side to the rock shaft's tappet arm."""
    t_metal, t_bar = tpl(iw, "leveler_metal_static_003"), tpl(iw, "Frame.119")
    tg = treadle_geometry()
    z0, z1 = SADDLE_Z[0][1] + 0.1, SADDLE_Z[1][0] - 0.1
    top = SADDLE_UP + TREADLE_PROUD
    out = [box(t_bar, [TREADLE_X[0], top - 1.5, z0], [TREADLE_X[1], top, z1], "treadle_plate", "treadle")]
    out.append(metal(box(t_metal, [PUSHROD_X - 0.4, tg["y"] + 0.5, TZ - 0.4], [PUSHROD_X + 0.4, top - 1.5, TZ + 0.4], "treadle_stem", "treadle")))
    out.append(metal(box(t_metal, [PUSHROD_X - 0.6, tg["y"] + 0.5, TZ - 0.6], [PUSHROD_X + 0.6, tg["y"] + 1.0, TZ + 0.6], "treadle_foot", "treadle")))
    out.append(metal(bar(t_metal, [PUSHROD_X, tg["y"], tg["foot"]], [PUSHROD_X, tg["y"], tg["tail"]], 0.9, 1.0, "treadlever_bar", "treadlever", "x")))
    out.append(metal(box(t_metal, [PUSHROD_X - 0.75, tg["y"] - 0.75, tg["pivot"] - 0.75], [PUSHROD_X + 0.75, tg["y"] + 0.75, tg["pivot"] + 0.75], "treadlever_boss", "treadlever")))
    cz = tg["pivot"] + 2.6                        # on the tail's side: it lifts the plate again
    out.append(metal(box(t_metal, [PUSHROD_X - 0.9, tg["y"] - 2.4, cz - 1.2], [PUSHROD_X + 0.9, tg["y"] - 0.5, cz + 1.2], "treadlever_counterweight", "treadlever")))
    # the pushrod: a slotted foot round the lever's tail pin, a plain bar up to the tappet arm's tip
    pz = tg["tail"]
    slot = tg["rise"]["thick"] - tg["rise"]["thin"] + 0.6
    sw = stirrup(tg)
    y0, y1 = tg["y"] - 0.6, tg["y"] + slot + 0.7                # the slot's window (y), the tail roller swinging over sw (z)
    for side, (x0, x1) in (("w", (PUSHROD_X - 1.6, PUSHROD_X - 1.0)), ("e", (PUSHROD_X + 1.0, PUSHROD_X + 1.6))):
        out.append(metal(box(t_metal, [x0, y0, sw[0] - 0.5], [x1, y1, sw[0]], f"pushrod_cheek_{side}n", "pushrod")))
        out.append(metal(box(t_metal, [x0, y0, sw[1]], [x1, y1, sw[1] + 0.5], f"pushrod_cheek_{side}s", "pushrod")))
    # the strap the roller lifts the pushrod by: only under the roller's place at a thin trunk's lift
    out.append(metal(box(t_metal, [PUSHROD_X - 1.6, y0 - 0.5, pz - 0.6], [PUSHROD_X + 1.6, y0, sw[1] + 0.5], "pushrod_strap", "pushrod")))
    out.append(metal(box(t_metal, [PUSHROD_X - 1.6, y1, sw[0] - 0.5], [PUSHROD_X + 1.6, y1 + 0.5, sw[1] + 0.5], "pushrod_head", "pushrod")))
    out.append(metal(box(t_metal, [PUSHROD_X - 0.4, tg["y"] + slot + 1.2, pz - 0.4], [PUSHROD_X + 0.4, ROCK_Y - 0.5 - tg["rise"]["thin"], pz + 0.4], "pushrod_bar", "pushrod")))
    out.append(metal(rod(t_metal, "x", (0.0, tg["y"], pz), PUSHROD_X - 1.7, PUSHROD_X + 1.7, 0.3, "treadlever_pin", "treadlever")[0]))
    out += [metal(e) for e in ngon(t_metal, "x", (0.0, tg["y"], pz), PUSHROD_X - 0.55, PUSHROD_X + 0.55, 0.6, 4, "treadlever_roller", "treadlever")]
    out.append(metal(rod(t_metal, "x", (0.0, tg["y"], tg["pivot"]), PUSHROD_X - 3.4, PUSHROD_X + 3.4, 0.4, "treadlever_axle", "treadlever")[0]))
    return out


def stirrup(tg):
    """The pushrod's stirrup's inner faces (z): the lever's tail pin swings toward the pivot as it
    rises, through a thick trunk's whole stroke."""
    swing = tg["a2"] * (1 - math.cos(tg["angle"]["thick"]))
    return tg["tail"] - swing - 0.65, tg["tail"] + 0.65


def build_rock(iw):
    """The rock shaft: its tappet arm (north, over the pushrod), the lever arm at each selector
    lever (down, to the pin in the lever's slot), the hold finger over each station's arc (north,
    level when in), and the throw-out weight (north of the shaft at every angle)."""
    t_metal = tpl(iw, "leveler_metal_static_003")
    lg = lever_geometry()
    tg = treadle_geometry()
    out = rod(t_metal, "x", (0.0, ROCK_Y, SEL_Z), ROCK_X[0], ROCK_X[1], 0.55, "rock_shaft", "rock")
    level = []                                  # built as they lie with the rock in, then turned out
    for st, s in STATIONS.items():
        x = s["C"] + LEVER_DX - 0.95
        out.append(metal(box(t_metal, [x - 0.35, LEVER_P - 0.4, SEL_Z - 0.4], [x + 0.35, ROCK_Y, SEL_Z + 0.4], f"rock_arm_{st}", "rock")))
        out.append(metal(box(t_metal, [x - 0.35, LEVER_P - 0.3, SEL_Z - 0.3], [x + 1.35, LEVER_P + 0.3, SEL_Z + 0.3], f"rock_pin_{st}", "rock")))
        hg = hold_geometry(st)
        fz = SEL_Z - hg["finger_len"]
        level.append(metal(box(t_metal, [hg["fx"] - FINGER_W / 2, ROCK_Y - 0.4, fz - 0.4], [hg["fx"] + FINGER_W / 2, ROCK_Y + 0.4, SEL_Z], f"rock_finger_{st}", "rock")))
    level.append(metal(box(t_metal, [PUSHROD_X - 0.5, ROCK_Y - 0.5, tg["tail"] - 0.5], [PUSHROD_X + 0.5, ROCK_Y + 0.5, SEL_Z], "rock_tappet", "rock")))
    wx = 133.0
    level.append(metal(bar(t_metal, [wx, ROCK_Y, SEL_Z], [wx, ROCK_Y - 2.2, SEL_Z - 3.4], 0.8, 0.8, "rock_weight_arm", "rock", "x")))
    level.append(metal(box(t_metal, [wx - 1.5, ROCK_Y - 3.6, SEL_Z - 5.0], [wx + 1.5, ROCK_Y - 1.6, SEL_Z - 3.0], "rock_weight", "rock")))
    rotate(level, "x", -math.degrees(lg["rock"]), (0.0, ROCK_Y, SEL_Z))
    out += level
    return out


# ---------------------------------------------------------------- builders: frame
def build_frame(iw):
    """The frame: the station's sills, posts, top rails and cross beams; the top beams over the main
    and rock shafts and the bearings hanging from them; the stations' side frames; the entry's, the
    ring's, the breaker's, the rocker's and the treadle's bearings and posts; the beds."""
    t_post, t_beam, t_block = tpl(iw, "Frame.011"), tpl(iw, "Frame.119"), tpl(iw, "Frame.088")
    out = []
    zmax = CELLS_Z * B
    sx0, sx1 = STATION_CELLS[0] * B, (STATION_CELLS[1] + 1) * B
    for side, (z0, z1) in (("n", (0.0, POST)), ("s", (zmax - POST, zmax))):
        out += beam(t_beam, [sx0, TOP_Y[0], z0], [sx1, TOP_Y[1], z1], f"fr_toprail_{side}", "frame")
        if side == "s":                            # the chute's mouth is a gap in the south sill
            out += beam(t_beam, [sx0, 0.0, z0], [CHUTE_X[0], 4.0, z1], f"fr_sill_{side}w", "frame")
            out += beam(t_beam, [CHUTE_X[1], 0.0, z0], [sx1, 4.0, z1], f"fr_sill_{side}e", "frame")
        else:
            out += beam(t_beam, [sx0, 0.0, z0], [sx1, 4.0, z1], f"fr_sill_{side}", "frame")
        for x in post_xs(side):
            out += beam(t_post, [x, 4.0, z0], [x + POST, TOP_Y[0], z1], f"fr_post_{side}{int(x)}", "frame")
    for x in cross_xs():
        out += beam(t_beam, [x, TOP_Y[0], POST], [x + POST, TOP_Y[1], zmax - POST], f"fr_cross{int(x)}", "frame")
    # the top beams: over the main shaft (two pieces either side of the ring pinion) and over the rock shaft
    for i, (a, b2) in enumerate(top_beam_spans(), 1):
        out += beam(t_beam, [a, TOP_Y[0], MAIN_Z - 4.0], [b2, TOP_Y[1], MAIN_Z + 4.0], f"fr_topbeam_main{i}", "frame")
    out += beam(t_beam, [sx0, TOP_Y[0], SEL_Z - 3.5], [sx1, TOP_Y[1], SEL_Z + 3.5], "fr_topbeam_rock", "frame")
    for x in main_bearing_xs():
        out.append(box(t_block, [x - 1.5, MAIN_Y - 3.0, MAIN_Z - 3.0], [x + 1.5, TOP_Y[0], MAIN_Z + 3.0], f"fr_bearing_main{int(round(x))}", "frame"))
    for x in rock_bearing_xs():
        out.append(box(t_block, [x - 1.25, ROCK_Y - 1.5, SEL_Z - 1.5], [x + 1.25, TOP_Y[0], SEL_Z + 1.5], f"fr_bearing_rock{int(round(x))}", "frame"))
    out += build_station_frames(iw)
    out += build_entry_frame(iw)
    out += build_ring_frame(iw)
    out += build_bed_frames(iw)
    out += build_lever_frames(iw)
    out += build_stops(iw)
    return out


def post_xs(side):
    """The station's side posts (west faces), clear of the stations' own posts and the ring's."""
    return [48.0, 108.0, 140.0, 188.0] if side == "n" else [48.0, 100.0, 188.0]


def cross_xs():
    return [56.0, 88.0, 113.0, 131.0, 176.0]     # none over the crown disc and flywheel (x 96..112)


def top_beam_spans():
    disc = ENTRY_X - 8.0, ENTRY_X + 8.0
    return ((56.0, disc[0]), (disc[1], PINION_X[0] - 1.8), (PINION_X[1] + 1.8, 180.0))


def main_x():
    """The main shaft's ends."""
    return STATIONS["in"]["X"] - 8.0, STATIONS["out"]["X"] + 8.0


def main_bearing_xs():
    xi, xo = STATIONS["in"]["X"], STATIONS["out"]["X"]
    w = WORM_LEAD * WORM_TURNS / 2 + 2.6
    return [xi - w, xi + w, 92.5, 115.0, 126.5, xo - w, xo + w]


def rock_bearing_xs():
    return [ROCK_X[0] + 1.5, 100.0, 140.0, ROCK_X[1] - 1.5]


def build_station_frames(iw):
    """Each station's side frames, north and south: a beam along x on two posts, carrying the lay
    and cross shafts' bearing blocks."""
    t_beam, t_block, t_post = tpl(iw, "Frame.119"), tpl(iw, "Frame.088"), tpl(iw, "Frame.011")
    out = []
    zmax = CELLS_Z * B
    for st, s in STATIONS.items():
        xs = sorted((s["C"], s["X"]))
        a, b2 = xs[0] - 6.0, xs[1] + 6.0
        for side, (z0, z1) in (("n", (0.0, POST)), ("s", (zmax - POST, zmax))):
            out += beam(t_beam, [a, SHAFT_Y - 7.0, z0], [b2, SHAFT_Y - 3.0, z1], f"fr_{st}_shaftbeam_{side}", "frame")
            for x in (a, b2 - POST):
                out += beam(t_post, [x, 4.0, z0], [x + POST, SHAFT_Y - 7.0, z1], f"fr_{st}_post_{side}{int(x)}", "frame")
            for tag, x in (("lay", s["X"]), ("cross", s["C"])):
                out.append(box(t_block, [x - 2.0, SHAFT_Y - 3.0, z0], [x + 2.0, SHAFT_Y + 3.0, z1], f"fr_{st}_bearing_{tag}_{side}", "frame"))
    return out


def arm_stop(st):
    """The stop the top-roll arms rest on with no trunk: a bracket from the station's north side
    frame under the north arm's bar, near the cross shaft (x span, y top, z span)."""
    xc = STATIONS[st]["C"]
    a = arm_angle(st, 0.0)
    r = 3.6                                          # along the arm from the cross shaft
    cx, cy = xc + r * math.cos(a), SHAFT_Y + r * math.sin(a)
    # the arm bar's underside at that point, measured square to the arm: its lowest corner there
    under = cy + 0.9 * math.tan(a) - 1.1 / math.cos(a)  # (the bar's lower face at the stop's downhill edge)
    return (cx - 0.9, cx + 0.9), under, (POST, ARM_Z["n"][1])


def build_stops(iw):
    """The top-roll arms' rest stops, and the rock shaft's stop at the end of its throw (over its
    tappet arm), which the pushrod's slot lets a thick trunk's treadle go on past."""
    t_block, t_beam = tpl(iw, "Frame.088"), tpl(iw, "Frame.119")
    out = []
    zmax = CELLS_Z * B
    for st in STATIONS:
        (x0, x1), top, (z0, z1) = arm_stop(st)
        out.append(box(t_beam, [x0, top - 1.4, 0.0], [x1, top, z1], f"fr_{st}_armstop", "frame"))
        out.append(box(t_block, [x0, top - 1.4 - 2.6, 0.0], [x1, top - 1.4, POST], f"fr_{st}_armstop_foot", "frame"))
    tg = treadle_geometry()
    tz = tg["tail"]
    out.append(box(t_block, [PUSHROD_X - 0.8, ROCK_Y + 0.5, tz - 0.5], [PUSHROD_X + 0.8, TOP_Y[0], tz + 0.6], "fr_rock_stop", "frame"))
    return out


def build_entry_frame(iw):
    """The entry shaft's bearings: one in a post at the power face, one north of the crown disc
    hanging from an arm out of the main shaft's top beam."""
    t_beam, t_block, t_post = tpl(iw, "Frame.119"), tpl(iw, "Frame.088"), tpl(iw, "Frame.011")
    out = beam(t_post, [ENTRY_X - 2.0, 4.0, 0.0], [ENTRY_X + 2.0, MAIN_Y - 3.0, POST], "fr_entry_post", "frame")
    out.append(box(t_block, [ENTRY_X - 2.5, MAIN_Y - 3.0, 0.0], [ENTRY_X + 2.5, TOP_Y[0], POST], "fr_entry_bearing_n", "frame"))
    dz = entry_disc_z()
    out.append(box(t_block, [ENTRY_X - 2.0, MAIN_Y - 3.0, dz - 7.5], [ENTRY_X + 2.0, MAIN_Y + 3.0, dz - 4.5], "fr_entry_bearing_s", "frame"))
    out += beam(t_post, [ENTRY_X - 1.75, 0.0, dz - 7.25], [ENTRY_X + 1.75, MAIN_Y - 3.0, dz - 4.75], "fr_entry_post_s", "frame")
    return out


def build_ring_frame(iw):
    """The ring station's posts (up and downstream of the ring, both sides), the rollers' bearing
    blocks (the lower ones on short posts, the upper ones on brackets from the posts), and the
    breaker pin's hangers."""
    t_beam, t_block, t_post = tpl(iw, "Frame.119"), tpl(iw, "Frame.088"), tpl(iw, "Frame.011")
    out = []
    zmax = CELLS_Z * B
    for j, (x0, x1) in enumerate(ring_post_x(), 1):
        for side, (z0, z1) in (("n", (0.0, POST)), ("s", (zmax - POST, zmax))):
            out += beam(t_post, [x0, 4.0, z0], [x1, TOP_Y[0], z1], f"fr_ringpost{j}{side}", "frame")
    for i, ang in enumerate(ROLLER_ANGLES, 1):
        p = ring_point(ang, 0.0, ROLLER_AXIS_R)
        for j, (x0, x1) in enumerate(ROLLER_BEARING_X, 1):
            out.append(box(t_block, [x0, p[1] - 2.0, p[2] - 2.0], [x1, p[1] + 2.0, p[2] + 2.0], f"fr_roller{i}_bearing{j}", "frame"))
            if p[1] < H:
                out += beam(t_post, [x0, 0.0, p[2] - 1.5], [x1, p[1] - 2.0, p[2] + 1.5], f"fr_roller{i}_post{j}", "frame")
            else:
                zz = (POST, p[2] - 2.0) if p[2] < TZ else (p[2] + 2.0, zmax - POST)
                out += beam(t_beam, [x0, p[1] - 1.5, zz[0]], [x1, p[1] + 1.5, zz[1]], f"fr_roller{i}_bracket{j}", "frame")
    # the breaker pin's hangers: north from the main shaft's top beam, south from an arm off the south top rail
    z0, z1 = BREAKER_PIN_Z
    out.append(box(t_block, [BREAKER_X - 1.5, BREAKER_Y - 1.5, z0], [BREAKER_X + 1.5, TOP_Y[0], z0 + 1.6], "fr_breaker_hanger_n", "frame"))
    out += beam(t_beam, [top_beam_spans()[0][1] - 4.0, TOP_Y[0], z0 - 0.4], [BREAKER_X + 2.0, TOP_Y[1], z0 + 2.0], "fr_breaker_beam_n", "frame")
    out.append(box(t_block, [BREAKER_X - 1.5, BREAKER_Y - 1.5, z1 - 1.6], [BREAKER_X + 1.5, TOP_Y[0], z1], "fr_breaker_hanger_s", "frame"))
    out += beam(t_beam, [BREAKER_X - 2.0, TOP_Y[0], z1 - 2.0], [BREAKER_X + 2.0, TOP_Y[1], zmax - POST], "fr_breaker_arm", "frame")
    return out


def build_bed_frames(iw):
    """Under each cradle: sills, and grooved guide posts either side of each bearer whose tongues
    run in their grooves."""
    t_beam, t_post = tpl(iw, "Frame.119"), tpl(iw, "Frame.011")
    out = []
    top = SADDLE_UP - 3.0 - cradle_drop() + 2.0
    for st in STATIONS:
        x0, x1 = CRADLE_X[st]
        if st == "out":
            x0 = BEARER_X["out"][0] - 6.0
        for side, (z0, z1) in (("n", (BEARER_Z[0] - 3.6, BEARER_Z[0] - 1.6)), ("s", (BEARER_Z[1] + 1.6, BEARER_Z[1] + 3.6))):
            out += beam(t_beam, [x0, 0.0, z0], [x1 if st == "out" else BEARER_X["in"][-1] + 6.0, 3.0, z1], f"fr_{st}_sill_{side}", "frame")
            for j, bx in enumerate(BEARER_X[st], 1):
                for k, (a, b2) in enumerate(((bx - 2.6, bx - 0.85), (bx + 0.85, bx + 2.6)), 1):
                    out.append(box(t_post, [a, 3.0, z0], [b2, top, z1], f"fr_{st}_guide{j}{side}{k}", "frame"))
                gz = (z0, BEARER_Z[0] - 1.6 + 0.05) if side == "n" else (BEARER_Z[1] + 1.6 - 0.05, z1)
                out.append(box(t_post, [bx - 0.85, 3.0, gz[0] if side == "n" else z1 - 0.6], [bx + 0.85, top, gz[0] + 0.6 if side == "n" else z1], f"fr_{st}_guideback{j}{side}", "frame"))
    return out


def build_lever_frames(iw):
    """The rocker's bearings on low posts, the treadle lever's bearings, the treadle plate's stem
    guide, and the rock shaft's stop."""
    t_block, t_post = tpl(iw, "Frame.088"), tpl(iw, "Frame.011")
    out = []
    for i, x in enumerate((ROCKER_X[0] + 1.6, ROCKER_X[1] - 1.6), 1):
        out.append(box(t_block, [x - 1.5, ROCKER_Y - 1.6, ROCKER_Z - 1.6], [x + 1.5, ROCKER_Y + 1.6, ROCKER_Z + 1.6], f"fr_rocker_bearing{i}", "frame"))
        out.append(box(t_post, [x - 1.5, 0.0, ROCKER_Z - 1.5], [x + 1.5, ROCKER_Y - 1.6, ROCKER_Z + 1.5], f"fr_rocker_post{i}", "frame"))
    tg = treadle_geometry()
    for i, x in enumerate((PUSHROD_X - 2.4, PUSHROD_X + 2.4), 1):
        out.append(box(t_block, [x - 0.8, tg["y"] - 1.4, tg["pivot"] - 1.4], [x + 0.8, tg["y"] + 1.4, tg["pivot"] + 1.4], f"fr_treadle_bearing{i}", "frame"))
        out.append(box(t_post, [x - 0.8, 0.0, tg["pivot"] - 1.2], [x + 0.8, tg["y"] - 1.4, tg["pivot"] + 1.2], f"fr_treadle_post{i}", "frame"))
    return out


# ---------------------------------------------------------------- build
def build(iw):
    els = []
    els += build_entry(iw) + build_rectifier(iw) + build_main(iw)
    els += build_ring(iw) + build_rollers(iw) + build_arms(iw)
    els += build_breaker(iw) + build_drip(iw) + build_chute(iw)
    for st in STATIONS:
        els += build_station(iw, st) + build_cradle(iw, st)
    els += build_rocker(iw) + build_treadle(iw) + build_rock(iw)
    els += build_frame(iw)
    for el in els:
        if METAL_NAMES.match(el.name):
            metal(el)
    return els


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    """A point in blocks from voxels, rounded."""
    return [r6(x / B) for x in v]


def per_class(thin, thick):
    return {"thin": r6(thin), "thick": r6(thick)}


def window(frm, to, ease, thin=1.0, thick=1.0):
    """A gauge window from voxel positions along the path."""
    return {"from": r6(frm / B), "to": r6(to / B), "ease": r6(ease / B), "gain": per_class(thin, thick)}


def class_only(cls):
    return {"thin": 1.0 if cls == "thin" else 0.0, "thick": 1.0 if cls == "thick" else 0.0}


def blocks_per_radian():
    """Trunk travel per radian of the feed input: the cross shaft turns once per radian (ratio
    -1), the banjo turns the roll BANJO[0]/BANJO[1] of that, and its surface moves rho."""
    return ROLL_RHO / B * BANJO[0] / BANJO[1]


def feed_gear():
    """The drawn two-speed ratio dphi/dpsi per class: cross shaft radians per axle radian."""
    lay = G / WHEEL_TEETH
    return {"thin": lay * FAST[0] / FAST[1], "thick": lay * SLOW[0] / SLOW[1]}


def gameplay_gear():
    """The ratio agent D's RosserPace gives at its defaults and copper speed: T_end(k) over the
    typical trunk's turns, per feed radian."""
    out = {}
    for k, cls in ((1, "thin"), (2, "thick")):
        out[cls] = t_end(k) / (TYPICAL_TURNS[cls] * 2 * math.pi * blocks_per_radian())
    return out


_PARTS = []


def rig_parts():
    """The parts, in first-match order, with the drivers that pose them (build frame); built once."""
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    b = 1.0 / B
    ring_ratio = -G * PINION_R / RING_PITCH_R
    lay_ratio = G / WHEEL_TEETH
    lg = lever_geometry()
    rock_w = rock_windows()
    parts = [
        {"id": "entry", "match": ["entry_*"], "requires": "shaft",
         "drivers": [{"type": "rotate", "axis": "z", "pivot": pt(ENTRY_X, MAIN_Y, 0.0), "ratio": 1.0}]},
        # the rectifier: the disc drives both pinions, always, opposite ways; whichever turns the main
        # shaft's way carries it through its catch, so the main shaft turns forward either way
        {"id": "pinion_w", "match": ["gear_pinion_w_*"], "requires": "shaft",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, MAIN_Y, MAIN_Z), "ratio": r6(-G)}]},
        {"id": "pinion_e", "match": ["gear_pinion_e_*"], "requires": "shaft",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, MAIN_Y, MAIN_Z), "ratio": r6(G)}]},
        {"id": "main", "match": ["main_*", "worm_in_*", "worm_out_*", "ringpinion_*"], "requires": "shaft",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, MAIN_Y, MAIN_Z), "ratio": r6(G), "input": "travel"}]},
        {"id": "ringtyre", "match": ["ringtyre*"], "requires": "tyres", "ride": "ring", "drivers": []},
        {"id": "ring", "match": ["ring_*"], "requires": "ring",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, H, TZ), "ratio": r6(ring_ratio), "input": "travel"}]},
    ]
    for i, ang in enumerate(ROLLER_ANGLES, 1):
        p = ring_point(ang, 0.0, ROLLER_AXIS_R)
        parts.append({"id": f"roller{i}", "match": [f"roller{i}_*"], "requires": "ring",
                      "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, p[1], p[2]),
                                   "ratio": r6(-ring_ratio * TYRE_R / ROLLER_RHO), "input": "travel"}]})
    arms = arm_drivers(ring_ratio)
    for i in range(1, 5):
        parts.append({"id": f"arm{i}", "match": [f"arm{i}_*"], "requires": "ring", "ride": "ring", "drivers": arms[i]})
    for i in range(1, 5):
        parts.append({"id": f"tip{i}", "match": [f"tip{i}_*"], "requires": "heads", "ride": f"arm{i}", "drivers": []})
    parts.append({"id": "breaker", "match": ["breaker_*"], "requires": "breaker", "drivers": [breaker_driver()]})
    for st, s in STATIONS.items():
        req = s["requires"]
        cx, cc = pt(s["X"], SHAFT_Y, 0.0), pt(s["C"], SHAFT_Y, 0.0)
        xr, yr = top_roll_centre(st, 0.0)
        arm_w = top_arm_windows(st)
        raise_ = {cls: arm_raise(st, cls) for cls in ("thin", "thick")}
        k = BANJO[0] / BANJO[1]
        lx = s["C"] + LEVER_DX
        parts += [
            {"id": f"lay_{st}", "match": [f"lay_{st}_*"], "requires": req,
             "drivers": [{"type": "rotate", "axis": "z", "pivot": cx, "ratio": r6(lay_ratio), "input": "travel"}]},
            {"id": f"fast_{st}", "match": [f"fast_{st}_*"], "requires": req,
             "drivers": [{"type": "rotate", "axis": "z", "pivot": cc, "ratio": r6(-lay_ratio * FAST[0] / FAST[1]), "input": "travel"}]},
            {"id": f"slow_{st}", "match": [f"slow_{st}_*"], "requires": req,
             "drivers": [{"type": "rotate", "axis": "z", "pivot": cc, "ratio": r6(-lay_ratio * SLOW[0] / SLOW[1]), "input": "travel"}]},
            {"id": f"sel_{st}", "match": [f"sel_{st}_*"], "requires": req,
             "drivers": [{"type": "gauge", "motion": "slide", "axis": "z", "amount": per_class(lg["throw"]["thin"] * b, lg["throw"]["thick"] * b), "windows": rock_w},
                         {"type": "rotate", "axis": "z", "pivot": cc, "ratio": -1.0, "input": "feed"}]},
            {"id": f"sellever_{st}", "match": [f"sellever_{st}_*"], "requires": "levers",
             "drivers": [{"type": "gauge", "motion": "rotate", "axis": "x", "pivot": pt(lx, lg["f_up"], SEL_Z), "amount": per_class(lg["angle"]["thin"], 0.0), "windows": rock_w},
                         {"type": "gauge", "motion": "rotate", "axis": "x", "pivot": pt(lx, lg["f_down"], SEL_Z), "amount": per_class(0.0, lg["angle"]["thick"]), "windows": rock_w}]},
            {"id": f"cross_{st}", "match": [f"cross_{st}_*"], "requires": req,
             "drivers": [{"type": "rotate", "axis": "z", "pivot": cc, "ratio": -1.0, "input": "feed"}]},
            {"id": f"toproll_{st}", "match": [f"toproll_{st}_*"], "requires": req, "ride": f"toparm_{st}",
             "drivers": [{"type": "rotate", "axis": "z", "pivot": pt(xr, yr, 0.0), "ratio": r6(k), "input": "feed"},
                         {"type": "gauge", "motion": "rotate", "axis": "z", "pivot": pt(xr, yr, 0.0),
                          "amount": per_class(raise_["thin"] * k, raise_["thick"] * k), "windows": arm_w}]},
            {"id": f"toparm_{st}", "match": [f"toparm_{st}_*"], "requires": req,
             "drivers": [{"type": "gauge", "motion": "rotate", "axis": "z", "pivot": cc, "amount": per_class(raise_["thin"], raise_["thick"]), "windows": arm_w}]},
            {"id": f"botroll_{st}", "match": [f"botroll_{st}_*"], "requires": req, "ride": f"cradle_{st}",
             "drivers": [{"type": "roll", "axis": "z", "pivot": pt(BOTROLL_X[st], SADDLE_UP - ROLL_RHO, 0.0), "at": r6(BOTROLL_X[st] / B), "ratio": r6(-B / ROLL_RHO)}]},
            {"id": f"cradle_{st}", "match": [f"cradle_{st}_*"], "requires": None,
             "drivers": [{"type": "gauge", "motion": "slide", "axis": "y", "mode": "present", "amount": per_class(0.0, -cradle_drop() * b)}]},
        ]
    tg = treadle_geometry()
    tw = [window(TREADLE_X[0], TREADLE_X[1], 1.0)]
    gain = tg["drop"]["thick"] / tg["drop"]["thin"]
    rk = rocker_geometry("in")
    parts += [
        {"id": "rocker", "match": ["rocker_*"], "requires": None,
         "drivers": [{"type": "gauge", "motion": "rotate", "axis": "x", "mode": "present", "pivot": pt(0.0, ROCKER_Y, ROCKER_Z),
                      "amount": per_class(0.0, rk["turn"] if rocker_tip("in")[1] > ROCKER_Z else -rk["turn"])}]},
        {"id": "treadle", "match": ["treadle_*"], "requires": "levers",
         "drivers": [{"type": "gauge", "motion": "slide", "axis": "y", "amount": per_class(-tg["drop"]["thin"] * b, -tg["drop"]["thick"] * b), "windows": tw}]},
        {"id": "treadlever", "match": ["treadlever_*"], "requires": "levers",
         "drivers": [{"type": "gauge", "motion": "rotate", "axis": "x", "pivot": pt(0.0, tg["y"], tg["pivot"]),
                      "amount": per_class(-tg["angle"]["thin"], -tg["angle"]["thick"]), "windows": tw}]},
        {"id": "pushrod", "match": ["pushrod_*"], "requires": "levers",
         "drivers": [{"type": "gauge", "motion": "slide", "axis": "y", "amount": per_class(tg["rise"]["thin"] * b, tg["rise"]["thin"] * b),
                      "windows": [window(TREADLE_X[0], TREADLE_X[1], 1.0, thin=1.0, thick=gain)]}]},
        {"id": "rock", "match": ["rock_*"], "requires": "levers",
         "drivers": [{"type": "gauge", "motion": "rotate", "axis": "x", "pivot": pt(0.0, ROCK_Y, SEL_Z), "amount": per_class(lg["rock"], lg["rock"]),
                      "windows": rock_w}]},
        {"id": "frame", "match": ["*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        for d in p["drivers"]:
            validate_driver(d)
    return parts


def top_arm_windows(st):
    """Per class (the other class's gain is 0), two windows whose max is the arm's motion: the
    first rises as the nose rides under the roll and ends before the second falls; the second is
    full by then and falls as the roll drops behind the tail. Both ramps are fitted to the contact
    (see `fit_rise`), so the roll is never lower than the trunk lets it be."""
    out = []
    for cls in ("thin", "thick"):
        frm, full = roll_ride(st, cls)
        d0, d1 = arm_drop(st, cls)
        out += rise_and_drop(frm, full, d0, d1, cls)
    return out


FIT_TOL = 0.2                                # voxels: the ramps are fitted allowing a touch this deep (the checks allow 0.3)


def fit_rise(need, x0, x1, n=200):
    """A linear ramp 0..1 over [frm, full] that is never below need(x) (the fraction of its motion a
    part needs with the trunk's nose at x): of all such ramps, the one that stays closest to the
    need (the least area between them), so the part moves as the nose pushes it."""
    xs = [x0 + (x1 - x0) * i / n for i in range(n + 1)]
    vals = [need(x) for x in xs]
    best = None
    for full in xs:
        frm = full - 1e-3
        for x, v in zip(xs, vals):
            if x >= full:
                break
            if v >= 1.0 - 1e-9:
                frm = None
                break
            if v > 1e-9:
                frm = min(frm, x - (full - x) * v / (1.0 - v))
        if frm is None:
            continue
        area = sum(min(1.0, max(0.0, (x - frm) / (full - frm))) - v for x, v in zip(xs, vals))
        if best is None or area < best[0] - 1e-12:
            best = (area, frm, full)
    _, frm, full = best
    return frm - 0.05, full


def rise_and_drop(frm, full, d0, d1, cls):
    """Two gauge windows (this class only) whose max rises over nose positions [frm, full] and falls
    over tail positions [d0, d1]: the first rises and is gone before the second falls; the second
    rises only once the first is full."""
    e_in, e_out = full - frm, d1 - d0
    # (a window's `to` must be past its `from`; when the part drops behind the tail at about where it
    # rose at the nose, the first window falls a little later than the second, which is on the safe side)
    return [window(frm, max(d0, frm + 0.05), e_in, **class_only(cls)), window(full, max(d1, full + 0.05), e_out, **class_only(cls))]


def fit_drop(need, x0, x1, ease, n=200):
    """A linear ramp 1..0 of length `ease` (tail positions) never below need(x) (falling): its start."""
    xs = [x0 + (x1 - x0) * i / n for i in range(n + 1)]
    d0 = x0
    for x in xs:
        v = need(x)
        if v > 1e-9:
            d0 = max(d0, x - ease * (1.0 - v))
    return d0 + 0.05


def roll_need(st, cls, edge, rising=True):
    """The fraction of its raise the top roll needs with the trunk's nose (rising) or tail at `edge`:
    seen from the side the trunk is a corner at (edge, its top); the roll's circle must clear it."""
    xc = STATIONS[st]["C"]
    r0 = arm_angle(st, 0.0)
    amount = arm_raise(st, cls)
    top = H + TRUNK_RADII[cls][0]

    def clear(f):
        a = r0 + amount * f
        cx, cy = xc + TOP_ARM * math.cos(a), SHAFT_Y + TOP_ARM * math.sin(a)
        dx = max(0.0, cx - edge) if rising else max(0.0, edge - cx)
        dy = max(0.0, cy - top)
        return math.hypot(dx, dy) >= ROLL_RHO - FIT_TOL
    # the highest fraction at which the roll is not clear (it must be above it): clear angles need not
    # be one interval (a lowered roll is clear upstream of the tail but not on the way down to it)
    for i in range(400, -1, -1):
        if not clear(i / 400):
            return min(1.0, (i + 1) / 400)
    return 0.0


def roll_ride(st, cls):
    """The rise: from where the ramp must start to where the roll is fully raised (nose positions)."""
    xr, _ = top_roll_centre(st, 0.0)
    xr2, _ = top_roll_centre(st, roll_lift(cls))
    return fit_rise(lambda n: roll_need(st, cls, n, True), xr - 2 * ROLL_RHO, xr2 + ROLL_RHO)


def arm_drop(st, cls):
    """The tail positions over which the top roll drops behind the tail (from, to)."""
    xr2, _ = top_roll_centre(st, roll_lift(cls))
    e = ROLL_DROP_EASE.get((st, cls), 3.0)
    d0 = fit_drop(lambda t: roll_need(st, cls, t, False), xr2 - ROLL_RHO, xr2 + 2 * ROLL_RHO, e)
    return d0, d0 + e


def rock_windows():
    """The rock shaft is in while any of these holds it: the treadle (a thick trunk's plate throws it
    in a sixth of the way down, as the pushrod's stop does), or either top roll's arc under its
    finger. Per station and class two windows: one in from the nose's ride until the arc lets go,
    one that falls from there (the rock dropping as the arc's end passes the finger), the
    outfeed's ending exactly at tailStop."""
    tg = treadle_geometry()
    out = [window(TREADLE_X[0], TREADLE_X[1], 1.0, thin=1.0, thick=tg["drop"]["thick"] / tg["drop"]["thin"])]
    for st in STATIONS:
        hg = hold_geometry(st)
        for cls in ("thin", "thick"):
            frm, _ = roll_ride(st, cls)
            d0, d1 = arm_drop(st, cls)
            tail_rel = d0 + (d1 - d0) * (1.0 - hg["release"] / arm_raise(st, cls))    # the arm is down to the release angle
            end = TAIL_STOP if st == "out" else tail_rel + 0.6
            if end <= tail_rel + 0.05:
                raise ValueError(f"the {st} hold lets go at {tail_rel:.2f}, past the trip's end {end:.2f}")
            out += rise_and_drop(frm, frm + 0.5, tail_rel, end, cls)
    return out


def breaker_driver():
    return {"type": "gauge", "motion": "rotate", "axis": "z", "pivot": pt(BREAKER_X, BREAKER_Y, 0.0),
            "amount": per_class(*breaker_lift()), "windows": breaker_windows()}


# ---------------------------------------------------------------- contact with the trunk (2D section, extruded)
def hull_signed(hl, q):
    """Signed distance of a point (y, z offsets from the axis) to the convex hull: negative inside."""
    inside = True
    best_out, best_in = 1e9, 1e9
    for i in range(len(hl)):
        a, b2 = hl[i - 1], hl[i]
        ex, ey = b2[0] - a[0], b2[1] - a[1]
        ln = math.hypot(ex, ey)
        cross = (ex * (q[1] - a[1]) - ey * (q[0] - a[0])) / ln
        if cross < 0:
            inside = False
        t = max(0.0, min(1.0, ((q[0] - a[0]) * ex + (q[1] - a[1]) * ey) / (ln * ln)))
        best_out = min(best_out, math.hypot(q[0] - a[0] - t * ex, q[1] - a[1] - t * ey))
        best_in = min(best_in, cross)
    return -best_in if inside else best_out


def samples(el, n=3):
    """Points over an element's surface (an n x n grid on each face), voxels."""
    h = [abs(v) / 2 for v in el.size]
    pts = []
    grid = [-1 + 2 * i / (n - 1) for i in range(n)]
    for k in range(3):
        for sg in (-1, 1):
            for a in grid:
                for b2 in grid:
                    loc = [0.0, 0.0, 0.0]
                    loc[k] = sg * h[k]
                    u, w = [j for j in range(3) if j != k]
                    loc[u], loc[w] = a * h[u], b2 * h[w]
                    d = [sum(el.r[i][j] * loc[j] for j in range(3)) for i in range(3)]
                    pts.append([el.c[i] + d[i] for i in range(3)])
    return pts


def trunk_depth(cls, nose, els, n=3):
    """How deep (voxels) any sample point of `els` is inside the trunk with its nose at `nose`
    (negative: the clearance). The trunk is its section's hull, extruded from tail to nose."""
    hl = hull_cache(SECTIONS[cls])
    tail = nose - LENGTHS[cls] * B
    worst = -1e9
    for el in els:
        for p in samples(el, n):
            ds = -hull_signed(hl, (p[1] - H, p[2] - TZ))
            de = min(p[0] - tail, nose - p[0])
            d = min(ds, de) if de > 0 else (de if ds > 0 else -math.hypot(de, ds))
            worst = max(worst, d)
    return worst


def penetrates(cls, nose, els, tol=0.0, tail=None, n=3):
    """Whether any sample point of `els` is more than `tol` inside the trunk (nose at `nose`, or with
    `tail` given, the tail there and the trunk reaching on downstream): a fast yes or no."""
    hl = hull_cache(SECTIONS[cls])
    reach = max(math.hypot(a, b2) for a, b2 in hl)
    lo_x, hi_x = (nose - LENGTHS[cls] * B, nose) if tail is None else (tail, 1e9)
    for el in els:
        lo, hi = el.aabb()
        if hi[0] <= lo_x + tol or lo[0] >= hi_x - tol:
            continue
        dy = max(lo[1] - H, H - hi[1], 0.0)
        dz = max(lo[2] - TZ, TZ - hi[2], 0.0)
        if math.hypot(dy, dz) >= reach:
            continue
        for q in samples(el, n):
            if q[0] - lo_x > tol and hi_x - q[0] > tol and -hull_signed(hl, (q[1] - H, q[2] - TZ)) > tol:
                return True
    return False


def hull_cache(points):
    key = id(points)
    if key not in _HULLS:
        _HULLS[key] = hull(points)
    return _HULLS[key]


_HULLS = {}


# ---------------------------------------------------------------- breaker
def breaker_bars(lift):
    out = []
    for el in ELS.get("breaker", []):
        e = el.clone()
        rotate([e], "z", math.degrees(lift), (BREAKER_X, BREAKER_Y, 0.0))
        out.append(e)
    return out


def breaker_lift():
    """How far (radians about the pin, +z lifts the bars' downstream ends) each class lifts the bars:
    until they rest on the section."""
    out = []
    for cls in ("thin", "thick"):
        lo, hi = 0.0, 1.3
        for _ in range(40):
            mid = (lo + hi) / 2
            lo, hi = (mid, hi) if penetrates(cls, 1e4, breaker_bars(mid), 0.0, tail=-1e9, n=4) else (lo, mid)
        out.append(hi)
    return out


def breaker_windows():
    """Per class two windows: the nose rides under the bars and lifts them (the ramp fitted to the
    contact), and they drop back behind the tail."""
    out = []
    lifts = breaker_lift()
    for cls, lift in zip(("thin", "thick"), lifts):
        def need(edge, rising=True):
            def pen(f):
                return penetrates(cls, edge, breaker_bars(lift * f), 0.05, tail=None if rising else edge)
            if not pen(0.0):
                return 0.0
            lo, hi = 0.0, 1.0
            for _ in range(20):
                mid = (lo + hi) / 2
                lo, hi = (lo, mid) if not pen(mid) else (mid, hi)
            return hi
        frm, full = fit_rise(need, BREAKER_X - 2.0, BREAKER_X + BREAKER_LEN + 4.0, 80)
        d0 = fit_drop(lambda t: need(t, False), BREAKER_X - 2.0, BREAKER_X + BREAKER_LEN + 4.0, 3.0, 80)
        out += rise_and_drop(frm, full, d0, d0 + 3.0, cls)
    return out


# ---------------------------------------------------------------- scraper arms
def arm_drivers(ring_ratio):
    """Each scraper arm's drivers: a gauge opening it to the trunk's mean reach (windows per class
    for the nose's ride), and lobes (harmonics of the ring's turn) that follow the section's shape
    as the tip goes round it."""
    fit = arm_fit()
    out = {}
    wins = arm_windows(ring_ratio)
    for i, phi0 in enumerate(ARM_ANGLES, 1):
        axis, sign = arm_axis(phi0)
        piv = pt(*ring_point(phi0, ARM_PIN_X, ARM_PIN_R))
        out[i] = arm_driver_list(fit, axis, sign, piv, phi0, ring_ratio, wins)
    return out


def arm_driver_list(fit, axis, sign, piv, phi0, ring_ratio, wins):
    ds = [{"type": "gauge", "motion": "rotate", "axis": axis, "pivot": piv,
           "amount": per_class(sign * fit["thin"]["a0"], sign * fit["thick"]["a0"]), "windows": wins}]
    for n in ARM_HARMONICS:
        for comp, shift in (("c", 0.0), ("s", -math.pi / 2)):
            amp = {cls: sign * fit[cls][comp][n] for cls in ("thin", "thick")}
            phase = math.remainder(n * math.radians(phi0) + shift, 2 * math.pi)
            ds.append({"type": "gauge", "motion": "rotate", "axis": axis, "pivot": piv, "amount": per_class(0.0, 0.0), "windows": wins,
                       "lobes": {"ratio": r6(n * ring_ratio), "phase": r6(phase), "amplitude": per_class(amp["thin"], amp["thick"])}})
    return ds


_ARM_FIT = {}
SECTIONS = {}
ELS = {}


def arm_open(phi, alpha, cls=None):
    """Arm 1 (authored at the top, pin along z) with its tip, opened by alpha about its pin and
    carried round the ring to direction phi (radians from +y toward +z)."""
    els = [e.clone() for e in ELS.get("arm1", []) + ELS.get("tip1", [])]
    pin = ring_point(0.0, ARM_PIN_X, ARM_PIN_R)
    rotate(els, "z", math.degrees(alpha), pin)
    rotate(els, "x", math.degrees(phi), (0.0, H, TZ))
    return els


def arm_need(cls, phi, tol=0.0):
    """The opening (radians) at which arm and tip just clear the trunk (by `tol` inside) in direction phi."""
    lo, hi = 0.0, arm_geometry()["beta0"]
    for _ in range(18):
        mid = (lo + hi) / 2
        lo, hi = (lo, mid) if not penetrates(cls, 1e4, arm_open(phi, mid), tol, tail=-1e9, n=3) else (mid, hi)
    return hi


def arm_fit():
    """The opening angle each class needs round the ring, from the arm's own geometry: at each
    direction the opening at which arm and tip just clear LE's section; fitted with the harmonics
    (least squares), then set so the tips press in at most ARM_PRESS anywhere round the ring."""
    if _ARM_FIT:
        return _ARM_FIT
    n_s = 120
    for cls in ("thin", "thick"):
        need = [arm_need(cls, 2 * math.pi * j / n_s) for j in range(n_s)]
        a0 = sum(need) / n_s
        c, sn = {}, {}
        for n in ARM_HARMONICS:
            c[n] = 2 / n_s * sum(need[j] * math.cos(n * 2 * math.pi * j / n_s) for j in range(n_s))
            sn[n] = 2 / n_s * sum(need[j] * math.sin(n * 2 * math.pi * j / n_s) for j in range(n_s))

        def model(phi, a0=0.0, c=c, sn=sn):
            return a0 + sum(c[n] * math.cos(n * phi) + sn[n] * math.sin(n * phi) for n in ARM_HARMONICS)
        # set the mean so the deepest press round the ring is ARM_PRESS: the opening that presses that
        # deep, wherever the fit is lowest against the need
        press = [arm_need(cls, 2 * math.pi * j / n_s, ARM_PRESS) for j in range(n_s)]
        a0 = max(press[j] - model(2 * math.pi * j / n_s) for j in range(n_s))
        _ARM_FIT[cls] = {"a0": a0, "c": c, "s": sn, "need": need}
    return _ARM_FIT


_ARM_WINDOWS = []


def arm_windows(ring_ratio):
    """Per class two windows: the nose rides under the closed arms and opens them (the ramp fitted so
    no arm or tip is ever inside the trunk's end, at any ring angle), and they close behind the
    tail. Fitted with the arms' own drivers, at full presence."""
    if _ARM_WINDOWS:
        return _ARM_WINDOWS
    fit = arm_fit()
    out = []
    for cls, k in (("thin", 1), ("thick", 2)):
        def arm_els(f, j):
            # the four arms at ring travel psi_j, opened to fraction f of their drivers' full motion
            psi = 2 * math.pi * j / 12 / abs(ring_ratio)          # j of 3: with four arms, 12 directions
            res = []
            for i, phi0 in enumerate(ARM_ANGLES, 1):
                axis, sign = arm_axis(phi0)
                piv = pt(*ring_point(phi0, ARM_PIN_X, ARM_PIN_R))
                ds = arm_driver_list(fit, axis, sign, piv, phi0, ring_ratio, [{"from": -1e3, "to": 1e3, "ease": 1.0}])
                parts = [{"id": "ring", "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, H, TZ), "ratio": ring_ratio, "input": "travel"}]},
                         {"id": "arm", "ride": "ring", "drivers": ds}]
                mm = _part_matrix(parts, "arm", {"theta": 0.0, "travel": psi, "trunk": 0.0, "size": k, "presence": f}, {"nose0": 0.0, "lengths": dict(LENGTHS), "tailStop": 0.0})
                res += [posed(el, mm) for el in ELS.get(f"arm{i}", []) + ELS.get(f"tip{i}", [])]
            return res
        cache = {}

        def need(edge, rising=True):
            worst = 0.0
            for j in range(3):
                def pen(f):
                    key = (round(f, 6), j)
                    if key not in cache:
                        cache[key] = arm_els(f, j)
                    # fully open, the tips are on the surface: allow the fit's tolerance there
                    tol = 0.3 if f > 0.97 else FIT_TOL / 2
                    return penetrates(cls, edge, cache[key], tol, tail=None if rising else edge)
                if not pen(0.0):
                    continue
                lo, hi = 0.0, 1.0
                for _ in range(12):
                    mid = (lo + hi) / 2
                    lo, hi = (lo, mid) if not pen(mid) else (mid, hi)
                worst = max(worst, hi)
            return worst
        x0, x1 = ARM_PIN_X - 2.0, ARM_PIN_X + ARM_LEN + 6.0
        frm, full = fit_rise(need, x0, x1, 48)
        d0 = fit_drop(lambda t: need(t, False), x0, x1, 3.0, 48)
        out += rise_and_drop(frm, full, d0, d0 + 3.0, cls)
    _ARM_WINDOWS.extend(out)
    return out


# ---------------------------------------------------------------- the rig file
def trunk_path():
    stations = {"treadle": (TREADLE_X[0] + TREADLE_X[1]) / 2, "infeed": BOTROLL_X["in"], "breaker": BREAKER_X, "drip": DRIP_X,
                "ring": (RING_X[0] + RING_X[1]) / 2, "outfeed": BOTROLL_X["out"]}
    return {"origin": pt(0.0, H, TZ), "axis": "x", "length": float(CELLS_X), "nose0": r6(NOSE0 / B),
            "lengths": dict(LENGTHS), "tailStop": r6(TAIL_STOP / B),
            "radius": {cls: [r6(v) for v in TRUNK_RADII[cls]] for cls in ("thin", "thick")},
            "stations": {k: r6(v / B) for k, v in stations.items()},
            "tips": {cls: r6(tip_x(cls) / B) for cls in ("thin", "thick")}}


def tip_x(cls):
    """Where the spud heads touch a trunk of `cls` (x): the tip block's centre with the arm at the
    class's mean opening. Downstream of the ring's plane, and further for a thick trunk, whose arms
    swing wider. The bark comes off here, not at the ring."""
    beta = arm_geometry()["beta0"] - arm_fit()[cls]["a0"]
    return ARM_PIN_X + ARM_LEN * math.cos(beta)


def plan_cells():
    """The footprint: the station's full section and the beds' 3 x 3."""
    out = []
    for x in range(CELLS_X):
        station = STATION_CELLS[0] <= x <= STATION_CELLS[1]
        for y in range(CELLS_Y if station else BED_CELLS_Y):
            for z in (range(CELLS_Z) if station else range(BED_CELLS_Z[0], BED_CELLS_Z[1] + 1)):
                out.append((x, y, z))
    return out


def make_rig(els, parts):
    """Cells with boxes from the model at rest (no trunk); a cell with nothing of its own is hollow."""
    # from the elements as the shape file writes them (rounded), so anything that rebuilds the boxes
    # from the shipped shape gets the same split
    written = flatten([element_json(el) for el in els], textures={})
    rest = [posed(w, pm(parts, el.part, REST)) for w, el in zip(written, els)]
    by_cell = {}
    for el in rest:
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, []).append(el)
    cells = []
    for c in plan_cells():
        boxes = cell_boxes(by_cell[c], c) if c in by_cell else None
        cells.append({"pos": list(c), "boxes": boxes} if boxes else {"pos": list(c), "hollow": True})
    cells = with_lids(cells)
    gear = feed_gear()
    power = (int(ENTRY_X // B), int(MAIN_Y // B), 0)
    water = (int(DRIP_X // B), int(DRIP_Y // B), CELLS_Z - 1)
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame (south-facing), block units, controller cell at [0,0,0]: the middle "
                    "of the east (outfeed) end at ground level, nearest the player who placed it; the west (infeed) end is "
                    "farthest. A hollow cell has no boxes of its own (solid only where the loaded trunk's box is). See the "
                    "rosser's README for the schema.",
        "cells": cells,
        "powerCell": list(power),
        "powerFace": "north",
        "waterCell": list(water),
        "waterFace": "south",
        "infeedSide": INFEED_SIDE,
        "outputSide": OUTPUT_SIDE,
        "chute": {"pos": pt((CHUTE_X[0] + CHUTE_X[1]) / 2, CHUTE_MOUTH_Y + 1.0, CELLS_Z * B + 0.8), "side": CHUTE_SIDE},
        "chuteSide": CHUTE_SIDE,
        "trunkPath": trunk_path(),
        "feed": {"blocksPerRadian": r6(blocks_per_radian()), "gear": per_class(gear["thin"], gear["thick"]),
                 "_comment": "blocksPerRadian: trunk travel per radian of the feed input (the cross shafts' turn). gear: the drawn "
                             "change gears' ratio, feed radians per axle radian, per class (the fast pair for thin, the slow for thick)."},
        "parts": parts,
    }


# ---------------------------------------------------------------- poses
REST = (0.0, 0.0, 0.0, 0.0, 0, 0.0)          # (theta, travel, feed, trunk, size, presence)


def inputs_of(pose):
    th, ps, ph, T, k, p = pose
    return {"theta": th, "travel": ps, "feed": ph, "trunk": T, "size": k, "presence": p}


PATH_BUILD = {"nose0": NOSE0 / B, "lengths": dict(LENGTHS), "tailStop": TAIL_STOP / B}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path or PATH_BUILD)


def mat_ratio(parts, pid):
    """A part's first rotate driver's ratio."""
    return next(d for d in next(p for p in parts if p["id"] == pid)["drivers"] if d["type"] == "rotate")["ratio"]


def t_end(k):
    return trunk_end(PATH_BUILD, k) if k else 0.0


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model, rig and parts moved so ORIGIN_CELL is the controller cell [0,0,0]:
    pivots and anchors by the shift, and every position along the path (windows, a roll's `at`, the
    path's nose0, tailStop, stations and tips) by the shift along x."""
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    db = [v / B for v in d]
    ship_els = copy.deepcopy(els)
    translate(ship_els, d)
    ship_parts = shift_rig_parts(parts, db)
    ship = copy.deepcopy(rig)
    ship["cells"] = [{**c, "pos": shift_cell(c["pos"], ORIGIN_CELL)} for c in rig["cells"]]
    ship["powerCell"] = shift_cell(rig["powerCell"], ORIGIN_CELL)
    ship["waterCell"] = shift_cell(rig["waterCell"], ORIGIN_CELL)
    ship["chute"] = {**rig["chute"], "pos": shift_point(rig["chute"]["pos"], db)}
    tp = copy.deepcopy(rig["trunkPath"])
    tp["origin"] = shift_point(tp["origin"], db, 6)
    tp["nose0"] = r6(tp["nose0"] + db[0])
    tp["tailStop"] = r6(tp["tailStop"] + db[0])
    tp["stations"] = {k: r6(v + db[0]) for k, v in tp["stations"].items()}
    tp["tips"] = {k: r6(v + db[0]) for k, v in tp["tips"].items()}
    ship["trunkPath"] = tp
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shift_rig_parts(parts, db):
    out = copy.deepcopy(parts)
    for p in out:
        for drv in p["drivers"]:
            if "pivot" in drv:
                drv["pivot"] = [r6(drv["pivot"][k] + db[k]) for k in range(3)]
            if "windows" in drv:                 # (windows are shared between drivers: shift copies)
                drv["windows"] = [{**w, "from": r6(w["from"] + db[0]), "to": r6(w["to"] + db[0])} for w in drv["windows"]]
            if "at" in drv:
                drv["at"] = r6(drv["at"] + db[0])
    return out


def shipped_cells(shape, ship_parts, sp, cells):
    """The shipped cells' boxes rebuilt from the shipped shape as written (rounded, in the shipped
    frame) posed at rest by the shipped rig: exactly what the site's test and any other reader of
    the two files computes, so the greedy split cannot come out differently there. The lids go on
    after (`with_lids`)."""
    written = flatten(shape["elements"], textures={})
    rest = [posed(w, _part_matrix(ship_parts, part_of(ship_parts, w.name), inputs_of(REST), sp)) for w in written]
    by_cell = {}
    for el in rest:
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, []).append(el)
    out = []
    for c in cells:
        pos = tuple(c["pos"])
        boxes = cell_boxes(by_cell[pos], pos) if pos in by_cell else None
        out.append({"pos": list(pos), "boxes": boxes} if boxes else {"pos": list(pos), "hollow": True})
    return with_lids(out)


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    sp = ship["trunkPath"]
    poses = [REST, (1.3, 4.0, 2.0, 0.0, 1, 1.0), (-2.2, 9.0, 4.4, 3.3, 2, 1.0), (0.7, 12.0, 7.0, 6.1, 1, 0.6),
             (4.0, 20.0, 9.0, t_end(2), 2, 1.0), (2.0, 21.0, 3.0, 0.4, 2, 0.3), (0.3, 3.0, 1.0, 7.9, 1, 1.0)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose), sp), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    span = [(min(c[k] for c in cells), max(c[k] for c in cells)) for k in range(3)]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells x {span[0]}, y {span[1]}, z {span[2]}; "
          f"power {ship['powerCell']} {ship['powerFace']}; water {ship['waterCell']} {ship['waterFace']}; chute {ship['chute']['pos']}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
def reference_poses(parts):
    """theta in {0, 1.1, 2.9, -2.3}, psi = |theta| + {0, 7.3}; for each class, T at 0, at T_end, and
    at every window's edges and mid-ramps (by nose and by tail), with phi and p varied."""
    out = []
    thetas = (0.0, 1.1, 2.9, -2.3)
    for th in thetas:
        for extra in (0.0, 7.3):
            out.append((th, abs(th) + extra, 0.0, 0.0, 0, 0.0))
    path = PATH_BUILD
    wins = []
    for p in parts:
        for d in p["drivers"]:
            for w in d.get("windows", []):
                if w not in wins:
                    wins.append(w)
    for k in (1, 2):
        cls = CLASSES[k]
        ts = {0.0, round(t_end(k), 6)}
        for w in wins:
            if w["gain"].get(cls, 1.0) == 0:
                continue
            for edge in (w["from"] - path["nose0"], w["to"] - path["nose0"] + LENGTHS[cls]):
                for off in (-w["ease"] / 2, 0.0, w["ease"] / 2):
                    t = edge + off
                    if 0 <= t <= t_end(k):
                        ts.add(round(t, 6))
        for i, T in enumerate(sorted(ts)):
            th = thetas[i % 4]
            for p in ((1.0,) if i % 3 else (1.0, 0.4)):
                out.append((th, round(abs(th) + 3.1 * i, 6), round(T / blocks_per_radian(), 6) if i % 2 else 3.7, T, k, p))
    return out


def reference_json(ship_parts, sp, parts):
    poses = []
    for pose in reference_poses(parts):
        th, ps, ph, T, k, p = pose
        ins = inputs_of(pose)
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], ins, sp)) for q in ship_parts}
        poses.append({"theta": th, "travel": ps, "feed": ph, "trunk": T, "size": k, "presence": p, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped rosser-rig.json's parts and trunkPath: each part's matrix as 3 rows "
                        "of 4 (block units) at each pose (trunk T in blocks along the shipped path). The C# and site tests check their rig "
                        "maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els, source):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. The entry crown disc (entry_Rotor_default_3_*) and the two rectifier pinions "
             f"(gear_pinion_*) are from Immersive Woodworking's sawmill model ({source}) by Bobrik00, as is the tooth "
             f"every toothed wheel is built from (*iwtooth*, *iwthread*); see CREDITS.md. Keep element names when editing.", TEXTURES)


COPLANAR_POSES = (REST, (0.4, 0.4, 0.4, 4.6, 1, 1.0), (0.7, 2.2, 1.1, 4.6, 2, 1.0))


def fix_coplanar(els, parts):
    return fix_coplanar_posed(els, lambda es, pose: [posed(el, pm(parts, el.part, pose)) for el in es], COPLANAR_POSES)


def main():
    ap = argparse.ArgumentParser(description="Generate the rosser's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_rosser
    source, iw = load_iw()
    _, le = load_le()
    for cls in ("thin", "thick"):
        SECTIONS[cls] = section(le[cls])
    els = build(iw)
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
        ELS.setdefault(el.part, []).append(el)
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        for pose, pairs in before.items():
            print(f"coplanar faces before the fix at {pose}: {len(pairs)} pairs")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    # The cells' boxes come from the model as it ships, after the z-fighting insets (unlike the mill's):
    # an inset of hundredths of a voxel can tip the greedy split of a cell's boxes, and the site's and
    # the C# tests rebuild the boxes from the shipped shape.
    rig = make_rig(els, parts)
    ok = validate_rosser.validate(sys.modules[__name__], els, parts, rig, le, quick=args.quick)
    if args.out:
        outs = (args.out / "rosser.json", args.out / "rosser_frame.json", args.out / "rosser-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "rosser.json", SHAPE_DIR / "rosser_frame.json", RIG_DIR / "rosser-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els, source), shape_json([el for el in ship_els if el.part == "frame"], source)
    ship["cells"] = shipped_cells(shape, ship_parts, ship["trunkPath"], ship["cells"])
    ok = validate_rosser.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship), reference_dumps(reference_json(ship_parts, ship["trunkPath"], parts)))
    for path, text in zip(outs, texts):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        json.loads(path.read_text())
        print(f"wrote {path} ({path.stat().st_size // 1024} KiB)")
    if not (ok and check_shipped(els, parts, ship_els, ship_parts, ship)):
        sys.exit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
