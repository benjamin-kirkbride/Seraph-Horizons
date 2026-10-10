#!/usr/bin/env python3
"""Generate the concentrator's (wash house's) shapes, rig and reference poses.

The concentrator is the ore line's gravity concentration stage (#734), one machine upgraded in place
(#711): a timber wash house, built at its tier 4 size, into which each tier's working parts are fitted,
the next tier's set replacing the last. Its fixed points never move: the axle comes in at the west end
into a rectifier and a main shaft under the roof (the frame's), the water comes in by a ppex pipe on the
north wall to a header with two cocks, the feed comes in by a chute through the north wall to a spout over
the head of every tier's machine, the tailings leave by a launder out of the west end, and the concentrate
by a launder out of the east end. The tiers:

    1  longtom  a long tom: the tom, its riddle (tom iron), the riffle box with its cleats; a rake on a
                crosshead combs the gravel across the riddle, driven by a slotted crank (a Scotch yoke)
    2  jig      a two-compartment Hartz jig: the hutch, its sieves, two plungers pumped by slotted cranks
    3  table    a shaking table: the riffled deck on slides, shaken by an enclosed head motion (a quick
                return: slow forward, quick back), the water launder and feed box along its upper edge
    4  table + vanner   the table as tier 3, and a Frue-style vanner taking the table's middlings: an
                endless belt on a frame shaken sideways by three slotted cranks, crept up-slope by a worm
                on its head roller, whose bottom dips through a wash tank

From tier 2, amalgamation plates (`plates`, one part at any tier) can lie in the frame's plate table, the
concentrate's way out: three copper plates and a mercury trap, shown bare or dressed (`dressing`, a silvery
coat over them).

Every tier is belted from the main shaft (an open belt, drawn still: a plain belt moving along its own
length looks the same, as the gear cutter's does); the main shaft turns one way whichever way the axle
does (the rectifier), so the table's quick return and the vanner's creep always run the right way.
Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    concentrator.json         the whole machine, every tier's parts      (assets/.../shapes/block/)
    concentrator_frame.json   the wash house frame only (block and item)  (assets/.../shapes/block/)
    concentrator-rig.json     cells, anchors and the part rig             (assets/.../config/)
    rig-reference.json        every part's matrix at a grid of poses      (tests/Concentrator/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_concentrator.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
machine box's north-west-bottom corner (the "build frame"); the controller cell is the build frame's
[0,0,0], so the shipped files are the build frame divided by 16. The machines run along x, head (feed)
at the west, tail at the east.

The rig's inputs, as this machine uses them (README "Rig"):

    theta  the axle angle: the entry shaft and the rectifier's gears
    psi    the axle's travel either way: the main shaft and everything belted from it
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

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "Concentrator" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/Concentrator/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
DEG = math.pi / 180

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 6, 3, 5          # six long (x, the machines' length), three high, five wide
X_LEN, Y_LEN, Z_LEN = CELLS_X * B, CELLS_Y * B, CELLS_Z * B
ORIGIN_CELL = (0, 0, 0)                      # the controller: the north-west corner, at the head (feed) end
POWER_CELL, POWER_FACE = (0, 2, 2), "west"   # the vanilla axle comes in along x at (y 40, z 40)
WATER_CELL, WATER_FACE = (0, 1, 0), "north"  # a ppex pipe meets the inlet at (x 8, y 24) on the north face
FEED_CELL, FEED_FACE = (1, 1, 0), "north"    # a chute comes in at (x 27, y ~31) on the north face

TIERS = (
    (1, "longtom", ("longtom",), "long tom"),
    (2, "jig", ("jig",), "Hartz jig"),
    (3, "table", ("table",), "shaking table"),
    (4, "vanner", ("table", "vanner"), "shaking table and vanner"),
)
REQUIRES = ("longtom", "jig", "table", "vanner")
PLATES = ("plates", "dressing")              # the amalgamation plates and their mercury dressing: fitted at tiers 2 to 4
PLATES_FROM = 2

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",         # timber: posts, beams, sills, braces, trestles, gallows
    "planks": "game:block/wood/planks/oak1",       # boards: troughs, launders, the hutch, the deck, boxes
    "iron": "game:block/metal/plate/iron",         # castings, cheeks, plates, straps, brackets, bearings, yokes
    "steel": "game:block/metal/sheet-plain/steel1",  # shafts, gears, pins, pulleys, rods
    "screen": "game:block/metal/mesh1",            # the riddle (tom iron) and the jig's sieves
    "leather": "game:block/leather/plain",         # belts
    "pipe": "game:block/metal/sheet-plain/lead4",  # the water pipes: ppex's pipe in UnifiedPipes' lead
    "brass": "game:block/metal/sheet/brass1",      # the cocks
    "copper": "game:block/metal/sheet-plain/copper1",  # the amalgamation plates, bare
    "amalgam": "game:block/metal/sheet-plain/silver1",  # their mercury dressing: the amalgam's silver sheen
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the wash house frame
POST = 4.0                                   # posts and beams 4 x 4
PLATE_Y = (44.0, 48.0)                       # the wall plates and the tie beams on them
MID_X = (46.0, 50.0)                         # the long walls' middle posts and the middle tie beam
DRIVE_BEAM_X = (12.0, 16.0)                  # a tie beam over the drive: the rectifier's east cheek hangs from it
BRACE = 12.0                                 # knee braces: 12 down the post, 12 along the beam
BRACE_W = 3.0

# ---------------------------------------------------------------- the drive: entry, rectifier, main shaft
ENTRY = (40.0, 40.0)                         # (y, z): the entry shaft along x from the west face (the power cell's centre)
RECT_MOD = 0.75
RECT_A1, RECT_B1 = 12, 12                    # the direct pair: the main shaft turns as the axle, the other way
RECT_A2, RECT_I, RECT_B2 = 9, 9, 9           # through the idler: as the axle, the same way
RECT_R = {k: n * RECT_MOD / 2 for k, n in (("a1", RECT_A1), ("b1", RECT_B1), ("a2", RECT_A2), ("i", RECT_I), ("b2", RECT_B2))}
MAIN = (ENTRY[0] - RECT_R["a1"] - RECT_R["b1"], ENTRY[1])   # (y, z): the main shaft, under the entry
CHEEK1_X, CHEEK2_X = (4.0, 5.0), (11.0, 12.0)  # the rectifier's cast cheeks: against the end tie beam, the drive beam
CHEEK_Y, CHEEK_Z = (26.5, 48.0), (37.5, 42.5)   # each cheek a cast bar down from its beam, round both shafts
RECT_X1, RECT_X2 = (5.6, 7.0), (8.0, 9.4)    # A1-B1, then A2-idler-B2
MAIN_X = (0.8, 52.0)
MAIN_R = 0.9
HANGER_X = (47.0, 49.0)                      # the main shaft's third bearing, hung from the middle tie beam
PULLEY_R = 4.0                               # every pulley on the main shaft


def _idler():
    """The idler: a2 + i from the entry, i + b2 from the main shaft, south of their line."""
    r1, r2 = RECT_R["a2"] + RECT_R["i"], RECT_R["i"] + RECT_R["b2"]
    (y1, z1), (y2, z2) = ENTRY, MAIN
    d = math.hypot(y2 - y1, z2 - z1)
    a = (r1 * r1 - r2 * r2 + d * d) / (2 * d)
    h = math.sqrt(r1 * r1 - a * a)
    uy, uz = (y2 - y1) / d, (z2 - z1) / d
    cands = [(y1 + a * uy + s * h * (-uz), z1 + a * uz + s * h * uy) for s in (1.0, -1.0)]
    return max(cands, key=lambda c: c[1])


IDLER = _idler()

# ---------------------------------------------------------------- the fixed points
# the feed: a board chute through the north wall, its lip over the head of every tier's machine
FEED_X = (24.5, 29.5)                        # the chute's channel
FEED_Z = (0.9, 16.0)
FEED_Y = (35.0, 31.0)                        # its floor at the wall and at its lip
FEED_LIP = (27.0, FEED_Y[1], FEED_Z[1])      # where every tier's feed lands (x, y, z)
# the water: the inlet on the north face, a header under the north plate, two cocks
PIPE_H = 3.0                                 # ppex's pipe: a square tube 6 across (its block's 5..11)
HEADER_H = 2.0                               # the header and its fittings past the inlet's reducer: 4 across
WATER_IN = (8.0, 24.0)                       # (x, y): the inlet's axis on the water cell's north face
HEADER = (40.0, 7.5)                         # (y, z): the header along x
HEADER_X = (WATER_IN[0] + PIPE_H, 77.0)
COCK1_X = 33.0                               # the head cock: over every tier's head
COCK2_X = 73.5                               # the vanner's cock (shut until the vanner is fitted)
SPOUT_Z = 12.0                               # the head cock's nozzle reaches over the table's water launder
SPOUT_Y = 30.0                               # its mouth
COCK2_Y = 32.5                               # the vanner's cock's outlet
# the tailings launder: along x, falling west, out of the west end
TL_Z = (39.0, 47.0)                          # outside; its channel 40..46
TL_Y = (1.0, 3.6)                            # its floor at the west end (x 0) and the east end (x TL_X1)
TL_X0, TL_X1 = 0.3, 80.0                     # its west (open) end, a hair inside the box, and its east end
TL_WALL = 3.2
# the concentrate launder: along z at the east end, falling north, then out of the east wall
CL_X = (80.0, 88.0)                          # outside; its channel 81..87
CL_Z = (5.0, 76.0)
CL_Y = (3.0, 4.3)                            # its floor at the north end (z 5) and the south end (z 76)
CL_WALL = 2.0
CL_OUT_Z = (5.0, 13.0)                       # its east wall stops here: it spills east over a lip onto the plate table
CL_LIP = ((86.8, 90.6), (6.2, 12.2))         # (x, z): the lip from the launder's floor over the plate table's head
# the plate table: a wooden bed east of the concentrate launder, falling south to a notch in its east side,
# the concentrate's way out at every tier; the amalgamation plates (fitted from tier 2) lie in it
PT_X = (88.6, 95.6)                          # outside; boards PT_T thick (channel 89.2..95.0)
PT_Z = (5.5, 21.5)
PT_Y = (2.1, 0.8)                            # its floor's top at the head (z 5.5) and the foot (z 21.5): 1 in 12
PT_T = 0.6
PT_SIDE = 1.5
PT_CURB = 0.25                               # the west side under the lip is a low curb
PT_CURB_Z = 13.2
PT_NOTCH_Z = (18.0, 20.9)                    # the outlet: a notch in the east side at the foot, a spout to the east face
PLATE_S = [(0.9 + 3.7 * i, 0.9 + 3.7 * i + 3.5) for i in range(3)]   # the three plates along the bed's slope
PLATE_T = 0.25
TRAP_S = (12.0, 12.4)                        # the mercury trap: a riffle across the bed after the plates
TRAP_H = 0.8
COAT_T = 0.06                                # the mercury dressing on each plate

# ---------------------------------------------------------------- tier 1: the long tom
LT_Z = (10.0, 22.0)                          # the tom, outside (sides 1 thick: channel 11..21)
LT_X = (24.0, 52.0)                          # head .. tail
LT_SLOPE = 1 / 12
LT_TAIL_Y = 20.2                             # the tom's floor at its tail (x 52)
LT_SIDE = 4.5
RIDDLE_X = (44.0, 52.0)                      # the tom iron: the last 8 of the tom's floor, perforated
RF_Z = (8.0, 24.0)                           # the riffle box (channel 9..23)
RF_X = (42.0, 70.5)
RF_SLOPE = 1 / 16
RF_TAIL_Y = 13.5                             # its floor at x 70.5
RF_SIDE = 4.0
CLEATS = [50.0 + 4.0 * i for i in range(5)]  # the riffle cleats (x of their downstream faces)
REST_X = (42.6, 43.8)                        # a cross bar on the riffle box's sides carrying the tom
TC_X = (66.0, 75.0)                          # the tail chute along z, under the riffle box's open end (channel 67..74)
TC_Z = (10.0, 44.5)
TC_Y = (10.6, 8.4)                           # its floor at z 10 and at its lip
TC_SIDE = 2.0
GALLOWS_X = ((40.0, 42.0), (54.0, 56.0))     # the rake's gallows: two frames across the tom
GALLOWS_Z = ((4.5, 6.5), (25.5, 27.5))
GALLOWS_TOP = 32.0                           # the cross beams' top
RAKE_C = (35.0, 16.0)                        # (y, z): the crank shaft along x, over the tom's middle
RAKE_E = 3.0                                 # the crank's throw: the rake combs 3 either side
RAKE_X = (35.4, 57.6)                        # the crank shaft
RAKE_CRANK_X = (47.0, 49.2)                  # the crank throw's pin between its webs; the yoke straddles it
LT_PULLEY_X = (35.6, 37.8)

# ---------------------------------------------------------------- tier 2: the jig
JG_X = (22.0, 60.0)                          # the hutch, outside
JG_Z = (9.0, 33.0)
JG_FLOOR = 6.0                               # the hutch's floor (top), on two sills
JG_TOP = 24.5
JG_PART_Z = (19.5, 20.5)                     # the long partition: sieve side north, plunger side south
JG_PART_Y0 = 13.0                            # it stops this far up: the water passes under it
JG_CROSS_X = (40.5, 41.5)                    # the cross partition between the two compartments
SIEVE_Y = 18.0                               # the sieves' top
LIP1_Y, LIP2_Y = 22.0, 21.0                  # the overflow into compartment 2, the tail lip
PLUNGERS = ((25.0, 38.5), (43.5, 57.0))      # x of each plunger
PLUNGER_Z = (21.5, 31.0)
PLUNGER_Y = (14.0, 16.0)                     # at rest (mid-stroke)
JG_C = (31.0, 26.25)                         # (y, z): the crank shaft along x, over the plungers
JG_E = 1.5                                   # the cranks' throw: the plungers' stroke 3
JG_SHAFT_X = (16.6, 69.8)
JG_PULLEY_X = (16.8, 19.0)
JG_PULLEY_R = 5.0
JG_PEDESTALS = ((19.2, 21.8), (67.0, 69.6))
SPIGOT_X = ((30.0, 32.0), (49.0, 51.0))      # the hutch's spigots on its north wall
SPIGOT_Y = (8.2, 9.6)
JL_Z = (4.5, 8.5)                            # the jig's concentrate launder along the north wall
JL_X = (26.0, 83.0)
JL_Y = (6.6, 5.95)                           # its floor at x 26 and at its lip over the concentrate launder
JL_SIDE = 1.5
JT_X = (60.1, 66.5)                          # the jig's tail chute along z (channel 61.1..65.5)
JT_Z = (9.5, 44.5)
JT_Y = (17.5, 8.6)
JT_SIDE = 3.0

# ---------------------------------------------------------------- tier 3: the shaking table
DECK_X = (22.0, 78.0)
DECK_Z = (9.0, 33.0)
DECK_TOP = 25.0                              # the deck's top along its upper (north) edge (high: its middlings fall to the vanner)
DECK_TILT = 2.5                              # degrees: the deck falls south, across its width
DECK_T = 1.0
JOIST_Y = 2.0                                # the deck's two joists under it
JOISTS_Z = ((12.0, 14.0), (27.0, 29.0))
BEARERS_X = ((28.0, 31.0), (58.0, 61.0))     # the legs and slide plates the joists slide on
RIFFLES = 12                                 # riffles along the deck, across its width
RIFFLE_H = 0.5
WL_Z = (9.5, 14.0)                           # the water launder along the upper edge (channel 10.1..13.4)
WL_X = (31.0, 74.0)
FB_X = (21.5, 31.0)                          # the feed box at the head corner
FB_Z = (14.0, 21.0)
TB_A = 0.6                                   # the deck's stroke: x = A (sin t - sin 2t / 2), about 1.56 end to end
HM_X = (5.5, 17.0)                           # the head motion's box
HM_Z = (14.0, 25.0)
HM_Y = (10.0, 20.0)
HM_IN = (17.5, 16.5)                         # (y, z): its input shaft along x, the pulley outside the box's west face
HM_PULLEY_X = (1.2, 3.4)
HM_PULLEY_R = 5.0
PULLROD = (18.5, 21.5)                       # (y, z): the pull rod along x, the box to the deck's head
MB_X = (65.0, 70.0)                          # the middlings box under the deck's low edge, at its tail
MB_Z = (30.5, 43.5)                          # from under the edge to its spout's lip over the tailings launder
MB_TOP = 22.7
APRON_X = (DECK_X[0], MB_X[0])

# ---------------------------------------------------------------- tier 4: the vanner
VN_RAIL_Z = ((54.0, 56.0), (73.0, 75.0))     # the shaking frame's side rails
VN_RAIL_Y = (8.5, 11.5)
VN_RAIL_X = (25.5, 66.0)
BELT_Z = (57.5, 71.5)                        # the belt's width
FOOT = (28.0, 8.0)                           # (x, y): the foot roller's axis
HEAD = (74.0, 9.5)                           # the head roller's axis (it dips into the tank)
ROLL_R = 3.0
BELT_T = 0.4
CARRY = (37.0, 46.0, 55.0)                   # carrying rollers under the upper run
CARRY_R = 1.2
VN_TIES = (32.5, 40.5)                       # under the rails, where the lower run is low
VN_E = 1.0                                   # the shake: 1 either side
VN_C = (6.0, 51.5)                           # (y, z): the crank shaft along x, north of the frame
VN_CRANKS = (33.0, 47.0, 61.0)               # x of the three crank throws' middles
VN_THROW = 0.9                               # each pin's half length between its webs
VN_SHAFT_X = (12.6, 66.0)
VN_PULLEY_X = (12.8, 15.0)
VN_PEDESTALS = ((16.9, 17.9), (39.0, 41.0), (63.0, 65.0))
VN_SLIDES = (36.5, 64.2)
WORM_N = 16                                  # the head roller's worm wheel: 16 teeth, a single-start worm
WHEEL_MOD = 0.5
WHEEL_R = WORM_N * WHEEL_MOD / 2             # its pitch radius, 4
WORM_R = 1.2                                 # the worm's pitch radius
WHEEL_Z = (51.2, 52.6)
WORM_C = (HEAD[1] + WHEEL_R + WORM_R, sum(WHEEL_Z) / 2)   # (y, z): the worm shaft along x, over the wheel
WORM_X = (15.4, 77.0)
WORM_BEARINGS = (36.5, 52.5)
SMALL_R = 1.5                                # the small belt's pulleys (crank shaft to worm shaft)
SMALL_X = (15.6, 16.8)
TANK_X = (66.5, 79.5)
TANK_Z = (55.0, 74.0)
TANK_Y = 7.6
DEFLECT = (66.9, 8.8)                        # (x, y): the deflector roller over the tank's west wall, outside the loop
DEFLECT_R = 0.9
FEEDBOX_X = (63.0, 72.0)                     # the vanner's feed box over the belt, on the frame
DIST_X = (72.5, 74.5)                        # the water distributor over the head, on the frame
VC_X = (63.8, 70.8)                          # the middlings chute along z (channel 64.4..70.2), clear of the worm
VC_Z = (40.0, 62.0)                          # its open mouth under the middlings box's last 3.5
TAILS_X = (18.5, 24.5)                       # the tails box beside the belt's foot (channel 19.1..23.9)
TAILS_Z = (42.0, 73.5)                       # its spout's lip over the tailings launder .. its closed end
TAILS_Y = (7.6, 8.4)                         # its floor at the lip and at the closed end (over the crank shaft)
VPIPE_Z = (48.0, 51.0)                       # the vanner's water pipe's drop (z), north of the worm


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
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}   # a disc's angle 0 direction, and the in-plane direction it turns towards


def radial(axis, c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box from radius r0 to r1 about `axis` through c, a0..a1 along the axis, `width` across, at angle
    `ang` (radians, right-handed about +axis from the axis's reference direction)."""
    a = AX[axis]
    u, w = REF[axis]
    lo, hi = [0.0] * 3, [0.0] * 3
    lo[a], hi[a] = a0, a1
    lo[u], hi[u] = c[u] + r0, c[u] + r1
    lo[w], hi[w] = c[w] - width / 2, c[w] + width / 2
    el = box(lo, hi, name, part, tex)
    if abs(ang) > 1e-12:
        rotate([el], axis, math.degrees(ang), c)
    return el


def disc(axis, c, a0, a1, r, name, part, tex, k=4, phase=0.0):
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
        ang = phase + math.pi * i / k
        if abs(ang) > 1e-12:
            rotate([el], axis, math.degrees(ang), c)
        out.append(el)
    return out


def annulus(axis, c, a0, a1, r_in, r_out, n, name, part, tex, phase=0.0):
    """A ring of n boxes from r_in to r_out, each as wide as a side of the n-gon at r_out; every other
    one a hair shorter along the axis, so their ends never share a plane."""
    w = 2 * r_out * math.tan(math.pi / n)
    out = []
    for i in range(n):
        s = 0.02 if i % 2 else 0.0
        out.append(radial(axis, c, a0 + s, a1 - s, r_in, r_out, w, phase + TAU * i / n, f"{name}{i + 1}", part, tex))
    return out


def gear(axis, c, a0, a1, pitch_r, n, module, name, part, tex, phase=0.0, body_k=4):
    """A spur gear: a polygon body to the root and n teeth of `module` (phase: angle of tooth 1)."""
    width = math.pi * module / 2
    root, tip = pitch_r - 1.25 * module, pitch_r + module
    body = disc(axis, c, a0, a1, root, f"{name}_body", part, tex, k=body_k, phase=phase)
    return body + [radial(axis, c, a0 + 0.03, a1 - 0.03, root - 0.25, tip, width, phase + TAU * i / n, f"{name}_tooth{i + 1}", part, tex)
                   for i in range(n)]


def pulley(axis, c, a0, a1, r, name, part, tex="steel", hub_r=1.6):
    """A plain pulley: a rim (a ring of 12) and a web to a hub. No teeth: it drives a belt."""
    out = annulus(axis, c, a0, a1, r - 0.7, r, 12, f"{name}_rim", part, tex)
    mid = (a0 + a1) / 2
    out += disc(axis, c, mid - 0.25, mid + 0.25, r - 0.65, f"{name}_web", part, tex, k=6)
    out += disc(axis, c, a0 + 0.1, a1 - 0.1, hub_r, f"{name}_hub", part, tex)
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


def mesh_phase(c1, phase1, n1, c2, n2):
    """The tooth phase (angle of tooth 1, about +x, from +y towards +z) for gear 2 meshing gear 1
    (whose tooth 1 is at phase1): on the line of centres, gear 1's tooth and gear 2's gap meet."""
    d = math.atan2(c2[2] - c1[2], c2[1] - c1[1])
    p1, p2 = TAU / n1, TAU / n2
    off = ((d - phase1) / p1) % 1.0
    return (d + math.pi) + p2 / 2 + off * p2


def throw(c, xa, xb, e, ang, name, part, web=0.6, w=2.2, pin_r=0.4, tex="steel"):
    """A crank throw on a shaft along x through c = (_, y, z): two webs (xa - web .. xa and xb .. xb + web)
    from round the shaft out past the pin, and the pin between them at radius e, at angle `ang` about +x
    from +y towards +z. The shaft is broken between the webs (`shaft_pieces`), so a yoke can straddle the
    pin there."""
    u = (math.cos(ang), math.sin(ang))
    pin = (c[1] + e * u[0], c[2] + e * u[1])
    a = [0.0, c[1] - 1.0 * u[0], c[2] - 1.0 * u[1]]
    b = [0.0, pin[0] + 0.9 * u[0], pin[1] + 0.9 * u[1]]
    out = []
    for tag, (x0, x1) in (("a", (xa - web, xa)), ("b", (xb, xb + web))):
        el = strut(a, b, w, x1 - x0, f"{name}_web{tag}", part, tex, axis="x")
        el.c[0] = (x0 + x1) / 2
        out.append(el)
    out += disc("x", (0.0, *pin), xa - 0.05, xb + 0.05, pin_r, f"{name}_pin", part, tex, k=2)
    return out


def shaft_pieces(c, x0, x1, gaps, r, name, part, tex="steel"):
    """A shaft along x from x0 to x1, broken at each throw's gap (xa, xb), each piece running 0.3 into the
    webs either side."""
    xs = [x0]
    for xa, xb in sorted(gaps):
        xs += [xa - 0.3, xb + 0.3]
    xs.append(x1)
    out = []
    for j in range(0, len(xs), 2):
        out += disc("x", c, xs[j], xs[j + 1], r, f"{name}{j // 2 + 1}", part, tex)
    return out


def sloped(axis, a0, a1, y0, y1, pieces, part):
    """Boxes laid on a straight slope along `axis` ("x" or "z") from a0 (height y0) to a1 (height y1),
    either way along the axis. Each piece is (s0, s1, h0, h1, c0, c1, name, tex): s along the slope from
    the a0 end (0 .. its length), h above the slope's line, c across the axis (z for x, x for z). They are
    built level from a0 and turned about the a0 end's line, so a0's end stays where it is."""
    a = AX[axis]
    acr = 2 if axis == "x" else 0
    length = a1 - a0
    dirn = 1.0 if length >= 0 else -1.0
    out = []
    for s0, s1, h0, h1, c0, c1, name, tex in pieces:
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[a], hi[a] = sorted((a0 + dirn * s0, a0 + dirn * s1))
        lo[1], hi[1] = y0 + h0, y0 + h1
        lo[acr], hi[acr] = c0, c1
        out.append(box(lo, hi, name, part, tex))
    target = math.atan2(y1 - y0, abs(length))
    if abs(target) > 1e-12:
        if axis == "x":
            rotate(out, "z", math.degrees(dirn * target), (a0, y0, 0.0))
        else:
            rotate(out, "x", -math.degrees(dirn * target), (0.0, y0, a0))
    return out


def slope_len(a0, a1, y0, y1):
    return math.hypot(a1 - a0, y1 - y0)


def trough(axis, a0, a1, y0, y1, c0, c1, side, name, part, tex="planks", t=1.0, ends=(True, False), extra=()):
    """An open board trough along `axis` from a0 to a1 (either way), its floor's top falling or rising
    straight from y0 at a0 to y1 at a1, its outside c0..c1 across, sides `side` high over the floor,
    boards t thick; `ends` closes the a0 and a1 ends with an end board as high as the sides. The floor is
    `<name>_floor`, the sides `<name>_side1`/`2`, the ends `<name>_end1`/`2`; `extra` pieces (as `sloped`
    takes them) are laid with it."""
    run = slope_len(a0, a1, y0, y1)
    pieces = [(0.0, run, -t, 0.0, c0 + t, c1 - t, f"{name}_floor", tex),
              (0.0, run, -t, side, c0, c0 + t, f"{name}_side1", tex),
              (0.0, run, -t, side, c1 - t, c1, f"{name}_side2", tex)]
    if ends[0]:
        pieces.append((0.0, t, 0.0, side, c0 + t, c1 - t, f"{name}_end1", tex))
    if ends[1]:
        pieces.append((run - t, run, 0.0, side, c0 + t, c1 - t, f"{name}_end2", tex))
    return sloped(axis, a0, a1, y0, y1, pieces + list(extra), part)


def under_slope(y0, y1, a0, a1, lo, hi, t):
    """The lowest point of a sloped board's underside over lo..hi along its axis (its top runs from y0 at
    a0 to y1 at a1, it is t thick), less a hundredth: where a leg or block under it stops."""
    ang = math.atan2(y1 - y0, abs(a1 - a0))
    return min(floor_y(y0, y1, a0, a1, lo), floor_y(y0, y1, a0, a1, hi)) - t / math.cos(ang) - 0.01


def floor_y(y0, y1, a0, a1, at):
    """A trough's floor top at `at` along its axis (its floor's top runs straight from y0 at a0 to y1 at a1)."""
    return y0 + (y1 - y0) * (at - a0) / (a1 - a0)


def belt(axis, c1, r1, c2, r2, a0, a1, name, part, tex="leather", t=0.3, n_wrap=10, wraps=(True, True)):
    """An open belt on two pulleys of radii r1, r2 about `axis` ("x" or "z") through c1, c2 (3-vectors;
    the axis coordinate is ignored), a0..a1 along the axis: two straight runs on the outer tangents and
    a wrap of short boxes round the far side of each pulley. Drawn still: a plain belt moving along its
    length looks the same."""
    ia = AX[axis]
    u, v = [(1, 2), (2, 0), (0, 1)][ia]
    p1, p2 = (c1[u], c1[v]), (c2[u], c2[v])
    d = math.hypot(p2[0] - p1[0], p2[1] - p1[1])
    base = math.atan2(p2[1] - p1[1], p2[0] - p1[0])
    beta = math.asin((r1 - r2) / d)
    out, normals = [], {}
    for side, sgn in (("a", 1), ("b", -1)):
        n = base + sgn * (math.pi / 2 - beta)
        normals[side] = n
        q1 = [0.0] * 3
        q2 = [0.0] * 3
        q1[u], q1[v] = p1[0] + (r1 + t / 2) * math.cos(n), p1[1] + (r1 + t / 2) * math.sin(n)
        q2[u], q2[v] = p2[0] + (r2 + t / 2) * math.cos(n), p2[1] + (r2 + t / 2) * math.sin(n)
        q1[ia] = q2[ia] = (a0 + a1) / 2
        out.append(strut(q1, q2, t, a1 - a0, f"{name}_run{side}", part, tex, axis=axis))
    for tag, (cu, cv), r, f, to, on in (("1", p1, r1, normals["a"], normals["b"] + TAU, wraps[0]),
                                        ("2", p2, r2, normals["b"], normals["a"], wraps[1])):
        if not on:
            continue
        while to < f:
            to += TAU
        span = to - f
        n = max(3, int(round(n_wrap * span / math.pi)))
        for i in range(n):
            ang = f + span * (i + 0.5) / n
            seg = span / n * (r + t / 2) * 1.04
            q1 = [0.0] * 3
            q2 = [0.0] * 3
            for q, s in ((q1, -1), (q2, 1)):
                tang = ang + math.pi / 2
                q[u] = cu + (r + t / 2) * math.cos(ang) + s * seg / 2 * math.cos(tang)
                q[v] = cv + (r + t / 2) * math.sin(ang) + s * seg / 2 * math.sin(tang)
                q[ia] = (a0 + a1) / 2
            out.append(strut(q1, q2, t, a1 - a0 - (0.02 if i % 2 else 0.0), f"{name}_wrap{tag}_{i + 1}", part, tex, axis=axis))
    return out


def belt_loop(axis, pulleys, a0, a1, name, part, tex="leather", t=0.4, seg=1.4):
    """An endless belt round `pulleys`, [((u, v), r, side)], in their order counter-clockwise in the plane
    normal to `axis` ("z": u, v = x, y), each inside the loop (side +1, the belt wrapping it
    counter-clockwise) or outside it (side -1, a deflector the belt wraps the other way). Straight runs on
    the tangents between each pulley and the next, and a wrap of short boxes round each. Drawn still."""
    ia = AX[axis]
    u_ax, v_ax = [(1, 2), (2, 0), (0, 1)][ia]
    mid = (a0 + a1) / 2
    R = [r + t / 2 for _, r, _ in pulleys]
    n = len(pulleys)
    legs = []
    for i in range(n):
        (c1, _, s1), (c2, _, s2) = pulleys[i], pulleys[(i + 1) % n]
        r1, r2 = R[i], R[(i + 1) % n]
        dx, dy = c2[0] - c1[0], c2[1] - c1[1]
        ell, phi = math.hypot(dx, dy), math.atan2(dy, dx)
        k = s2 * r2 - s1 * r1
        tn = phi + math.acos(k / ell)
        nrm = (math.cos(tn), math.sin(tn))
        t1 = (c1[0] - s1 * r1 * nrm[0], c1[1] - s1 * r1 * nrm[1])
        t2 = (c2[0] - s2 * r2 * nrm[0], c2[1] - s2 * r2 * nrm[1])
        legs.append((t1, t2))

    def pt3(p):
        q = [0.0] * 3
        q[u_ax], q[v_ax], q[ia] = p[0], p[1], mid
        return q

    out = []
    for i, (t1, t2) in enumerate(legs, 1):
        out.append(strut(pt3(t1), pt3(t2), t, a1 - a0, f"{name}_run{i}", part, tex, axis=axis))
    for i, (c, r, side) in enumerate(pulleys):
        arrive, leave = legs[i - 1][1], legs[i][0]
        rr = R[i]
        aa = math.atan2(arrive[1] - c[1], arrive[0] - c[0])
        bb = math.atan2(leave[1] - c[1], leave[0] - c[0])
        span = (bb - aa) % TAU if side > 0 else -((aa - bb) % TAU)
        m = max(2, int(math.ceil(abs(span) * rr / seg)))
        for j in range(m):
            g0, g1 = aa + span * j / m, aa + span * (j + 1) / m
            q1 = (c[0] + rr * math.cos(g0), c[1] + rr * math.sin(g0))
            q2 = (c[0] + rr * math.cos(g1), c[1] + rr * math.sin(g1))
            ext = 0.06
            dq = (q2[0] - q1[0], q2[1] - q1[1])
            ln = math.hypot(*dq)
            e = (dq[0] / ln * ext, dq[1] / ln * ext)
            out.append(strut(pt3((q1[0] - e[0], q1[1] - e[1])), pt3((q2[0] + e[0], q2[1] + e[1])), t,
                             a1 - a0 - (0.02 if j % 2 else 0.0), f"{name}_wrap{i + 1}_{j + 1}", part, tex, axis=axis))
    return out


def pipe_x(x0, x1, y, z, name, part, h=PIPE_H, tex="pipe"):
    return box([x0, y - h, z - h], [x1, y + h, z + h], name, part, tex)


def pipe_z(z0, z1, x, y, name, part, h=PIPE_H, tex="pipe"):
    return box([x - h, y - h, z0], [x + h, y + h, z1], name, part, tex)


def pipe_y(y0, y1, x, z, name, part, h=PIPE_H, tex="pipe"):
    return box([x - h, y0, z - h], [x + h, y1, z + h], name, part, tex)


def flange_z(x, y, z0, z1, name, part):
    """A pipe's flange across z (ppex's pipe ends in one 8 across), with four bolt heads on its inner face."""
    out = [box([x - 4.0, y - 4.0, z0], [x + 4.0, y + 4.0, z1], f"{name}", part, "iron")]
    for i, (dx, dy) in enumerate(((-3.0, -3.0), (3.0, -3.0), (3.0, 3.0), (-3.0, 3.0)), 1):
        out.append(box([x + dx - 0.5, y + dy - 0.5, z1], [x + dx + 0.5, y + dy + 0.5, z1 + 0.6], f"{name}_bolt{i}", part, "iron"))
    return out


# ---------------------------------------------------------------- builders: the wash house frame
def build_timbers():
    """The posts, sills, plates, tie beams, the north wall's stud and girt, and knee braces."""
    f = "frame"
    out = []
    xs = ((0.0, POST), MID_X, (X_LEN - POST, X_LEN))
    zs = ((0.0, POST), (Z_LEN - POST, Z_LEN))
    for i, (x0, x1) in enumerate(xs):
        for j, (z0, z1) in enumerate(zs):
            out.append(box([x0, 0.0, z0], [x1, PLATE_Y[0], z1], f"fr_post_{'wme'[i]}{'ns'[j]}", f, "oak"))
    # sills along the long walls between the posts (the ends are open at the floor: the launders leave there)
    for j, (z0, z1) in enumerate(zs):
        out.append(box([POST, 0.0, z0], [MID_X[0], POST, z1], f"fr_sill_{'ns'[j]}w", f, "oak"))
        out.append(box([MID_X[1], 0.0, z0], [X_LEN - POST, POST, z1], f"fr_sill_{'ns'[j]}e", f, "oak"))
        out.append(box([0.0, PLATE_Y[0], z0], [X_LEN, PLATE_Y[1], z1], f"fr_plate_{'ns'[j]}", f, "oak"))
    for tag, (x0, x1) in (("w", (0.0, POST)), ("d", DRIVE_BEAM_X), ("m", MID_X), ("e", (X_LEN - POST, X_LEN))):
        out.append(box([x0, PLATE_Y[0], POST], [x1, PLATE_Y[1], Z_LEN - POST], f"fr_tie_{tag}", f, "oak"))
    # knee braces: each corner post to its plate and its end tie beam; each middle post to its plate both ways
    b = BRACE

    def brace(a, c, axis, name):
        out.append(strut(a, c, BRACE_W, POST - 0.6, name, f, "oak", axis=axis))

    yb = PLATE_Y[0] - b
    for j, (z0, z1) in enumerate(zs):
        zc = (z0 + z1) / 2
        brace([POST - 1.0, yb, zc], [POST + b - 1.0, PLATE_Y[0] + 1.0, zc], "z", f"fr_brace_w{'ns'[j]}x")
        brace([X_LEN - POST + 1.0, yb, zc], [X_LEN - POST - b + 1.0, PLATE_Y[0] + 1.0, zc], "z", f"fr_brace_e{'ns'[j]}x")
        brace([MID_X[0] + 1.0, yb, zc], [MID_X[0] - b + 1.0, PLATE_Y[0] + 1.0, zc], "z", f"fr_brace_m{'ns'[j]}w")
        brace([MID_X[1] - 1.0, yb, zc], [MID_X[1] + b - 1.0, PLATE_Y[0] + 1.0, zc], "z", f"fr_brace_m{'ns'[j]}e")
    for i, (x0, x1) in enumerate(((0.0, POST), (X_LEN - POST, X_LEN))):
        xc = (x0 + x1) / 2
        brace([xc, yb, POST - 1.0], [xc, PLATE_Y[0] + 1.0, POST + b - 1.0], "x", f"fr_brace_{'we'[i]}nz")
        brace([xc, yb, Z_LEN - POST + 1.0], [xc, PLATE_Y[0] + 1.0, Z_LEN - POST - b + 1.0], "x", f"fr_brace_{'we'[i]}sz")
    return out


def build_drive_frame():
    """The rectifier's two cast cheeks (bolted to the end tie beam and the drive beam), their bosses,
    the idler's stud, and the main shaft's hanger from the middle tie beam."""
    f = "frame"
    out = []
    for i, (x0, x1) in enumerate((CHEEK1_X, CHEEK2_X), 1):
        out.append(box([x0, CHEEK_Y[0], CHEEK_Z[0]], [x1, CHEEK_Y[1], CHEEK_Z[1]], f"fr_cheek{i}", f, "iron"))
    # the east cheek's arm out to the idler's stud
    iy, iz = IDLER
    out.append(box([CHEEK2_X[0], iy - 1.4, CHEEK_Z[1] - 0.2], [CHEEK2_X[1], iy + 1.4, iz + 1.4], "fr_cheek2_arm", f, "iron"))
    # bosses round the shafts on the cheeks' outer faces
    for (y, z), tag in ((ENTRY, "entry"), (MAIN, "main")):
        out += disc("x", (0.0, y, z), CHEEK1_X[0] - 0.6, CHEEK1_X[0], 2.0, f"fr_boss1{tag}", f, "iron")
        out += disc("x", (0.0, y, z), CHEEK2_X[1], CHEEK2_X[1] + 0.6, 2.0, f"fr_boss2{tag}", f, "iron")
    out += disc("x", (0.0, *IDLER), RECT_X2[0] - 0.3, CHEEK2_X[1], 0.6, "fr_idler_stud", f, "steel")
    # the hanger: a strap from the middle tie beam down to a bearing block round the main shaft
    y, z = MAIN
    out.append(box([HANGER_X[0], y + 2.0, z - 1.0], [HANGER_X[1], PLATE_Y[0], z + 1.0], "fr_hanger_strap", f, "iron"))
    out.append(box([HANGER_X[0], y - 2.0, z - 2.0], [HANGER_X[1], y + 2.0, z + 2.0], "fr_hanger_bearing", f, "iron"))
    return out


def build_feed():
    """The feed chute: a board chute through the north wall (a chute from outside pours into its open
    upper end), falling south to its lip over the head of every tier, resting on a timber post on the
    north sill and held further in by an iron knee from that post."""
    f = "frame"
    out = trough("z", FEED_Z[0], FEED_Z[1], FEED_Y[0], FEED_Y[1], FEED_X[0] - 1.0, FEED_X[1] + 1.0, 3.0, "fr_feed", f, ends=(False, False))
    zp = (0.6, 3.4)
    xp = (FEED_X[0] + 0.5, FEED_X[1] - 0.5)
    under = floor_y(FEED_Y[0], FEED_Y[1], FEED_Z[0], FEED_Z[1], zp[1]) - 1.0
    out.append(box([xp[0], POST, zp[0]], [xp[1], under - 0.02, zp[1]], "fr_feed_post", f, "oak"))
    zk = 8.6
    yk = floor_y(FEED_Y[0], FEED_Y[1], FEED_Z[0], FEED_Z[1], zk) - 1.0
    knee = strut([0.0, under - 7.0, zp[1] - 0.6], [0.0, yk - 0.35, zk], 0.7, xp[1] - xp[0] - 1.0, "fr_feed_knee", f, "iron", axis="x")
    knee.c[0] = (xp[0] + xp[1]) / 2
    out.append(knee)
    out.append(box([xp[0] + 0.5, yk - 0.7, zk - 1.0], [xp[1] - 0.5, yk - 0.02, zk + 1.0], "fr_feed_pad", f, "iron"))
    return out


def build_water():
    """The water: the inlet stub on the north face (ppex's 6 x 6 section, flanged), an elbow, the header
    along x under the north plate, carried by iron brackets from the plate and a hanger from the middle tie
    beam; two drops, each with a brass cock: the head cock, its nozzle over every tier's head, and the
    vanner's cock at the east end."""
    f = "frame"
    out = []
    x, y = WATER_IN
    hy, hz = HEADER
    out.append(pipe_z(1.0, hz - PIPE_H, x, y, "fr_pipe_inlet", f))
    out += flange_z(x, y, 0.0, 1.0, "fr_pipe_flange", f)
    # the riser: an elbow up from the inlet to the header's level, against the west end's knee brace
    out.append(box([x - PIPE_H, y - PIPE_H, hz - PIPE_H], [x + PIPE_H, hy + PIPE_H, hz + PIPE_H], "fr_pipe_elbow", f, "pipe"))
    out.append(pipe_x(HEADER_X[0], HEADER_X[1], hy, hz, "fr_pipe_header", f, h=HEADER_H))
    out.append(box([x - PIPE_H - 0.6, y + 4.0, hz - 0.6], [x - PIPE_H, y + 5.2, hz + 0.6], "fr_pipe_clip", f, "iron"))
    out.append(box([POST - 0.6 - 0.0, y + 4.0, hz - 0.6], [x - PIPE_H - 0.6, y + 5.2, hz + 0.6], "fr_pipe_clipbar", f, "iron"))
    out.append(box([HEADER_X[1], hy - HEADER_H + 0.5, hz - HEADER_H + 0.5], [HEADER_X[1] + 0.6, hy + HEADER_H - 0.5, hz + HEADER_H - 0.5], "fr_pipe_cap", f, "pipe"))
    # brackets: an iron strap down from the north plate's underside and an arm under the header
    for i, xs in enumerate((31.0, 68.0), 1):
        out.append(box([xs - 0.6, hy - HEADER_H - 0.8, POST - 0.8], [xs + 0.6, PLATE_Y[0], POST], f"fr_pipe_bracket{i}", f, "iron"))
        out.append(box([xs - 0.6, hy - HEADER_H - 0.8, POST], [xs + 0.6, hy - HEADER_H, hz + HEADER_H + 0.6], f"fr_pipe_arm{i}", f, "iron"))
    # the hanger from the middle tie beam: a band over and round the header
    xm = sum(MID_X) / 2
    out.append(box([xm - 0.6, hy - HEADER_H - 0.6, hz + HEADER_H], [xm + 0.6, PLATE_Y[0], hz + HEADER_H + 0.6], "fr_pipe_hanger_s", f, "iron"))
    out.append(box([xm - 0.6, hy - HEADER_H - 0.6, hz - HEADER_H - 0.6], [xm + 0.6, PLATE_Y[0], hz - HEADER_H], "fr_pipe_hanger_n", f, "iron"))
    out.append(box([xm - 0.6, hy - HEADER_H - 0.6, hz - HEADER_H], [xm + 0.6, hy - HEADER_H, hz + HEADER_H], "fr_pipe_hanger_b", f, "iron"))
    # the head cock: a drop from the header, a run south to the nozzle's line, the cock, the nozzle
    b0 = SPOUT_Y + 1.1                           # the cock's body: over its nozzle, at the end of the run from the drop
    out.append(pipe_y(b0 + 0.4, hy - HEADER_H, COCK1_X, hz, "fr_pipe_drop1", f, h=1.5))
    out.append(box([COCK1_X - 1.5, b0 + 0.4, hz + 1.5], [COCK1_X + 1.5, b0 + 1.9, SPOUT_Z - 1.6], "fr_pipe_run1", f, "pipe"))
    out.append(box([COCK1_X - 1.6, b0, SPOUT_Z - 1.6], [COCK1_X + 1.6, b0 + 2.0, SPOUT_Z + 1.6], "fr_cock1_body", f, "brass"))
    out.append(box([COCK1_X - 0.4, b0 + 2.0, SPOUT_Z - 0.4], [COCK1_X + 0.4, b0 + 3.0, SPOUT_Z + 0.4], "fr_cock1_stem", f, "brass"))
    out.append(box([COCK1_X - 1.8, b0 + 3.0, SPOUT_Z - 0.3], [COCK1_X + 1.8, b0 + 3.5, SPOUT_Z + 0.3], "fr_cock1_handle", f, "brass"))
    out.append(box([COCK1_X - 0.8, SPOUT_Y, SPOUT_Z - 0.8], [COCK1_X + 0.8, b0, SPOUT_Z + 0.8], "fr_cock1_nozzle", f, "brass"))
    # the vanner's cock: a drop at the east end to the cock, its outlet pointing south (the vanner's pipe meets it)
    out.append(pipe_y(COCK2_Y + 2.0, hy - HEADER_H, COCK2_X, hz, "fr_pipe_drop2", f, h=1.5))
    out.append(box([COCK2_X - 1.6, COCK2_Y, hz - 1.6], [COCK2_X + 1.6, COCK2_Y + 2.0, hz + 1.6], "fr_cock2_body", f, "brass"))
    out.append(box([COCK2_X - 3.4, COCK2_Y + 0.7, hz - 0.3], [COCK2_X - 1.6, COCK2_Y + 1.2, hz + 0.3], "fr_cock2_handle", f, "brass"))
    out.append(box([COCK2_X - 1.0, COCK2_Y, hz + 1.6], [COCK2_X + 1.0, COCK2_Y + 2.0, hz + 2.4], "fr_cock2_outlet", f, "brass"))
    out.append(box([COCK2_X - 1.2, COCK2_Y - 0.4, hz - 1.2], [COCK2_X + 1.2, COCK2_Y, hz + 1.2], "fr_cock2_cap", f, "brass"))
    return out


def build_launders():
    """The tailings launder (along x, falling west, out of the west end) and the concentrate launder
    (along z at the east end, falling north, then east through the east wall), on oak blocks."""
    f = "frame"
    out = trough("x", TL_X0, TL_X1, TL_Y[0], TL_Y[1], TL_Z[0], TL_Z[1], TL_WALL, "fr_tl", f, ends=(False, True))
    for i, x in enumerate((8.0, 30.0, 52.0, 74.0), 1):
        out.append(box([x - 1.5, 0.0, TL_Z[0] + 1.0], [x + 1.5, under_slope(TL_Y[0], TL_Y[1], TL_X0, TL_X1, x - 1.5, x + 1.5, 1.0), TL_Z[1] - 1.0],
                       f"fr_tl_block{i}", f, "oak"))
    # the concentrate launder, built from its south end: its east wall stops at the outlet
    z0, z1 = CL_Z
    y0, y1 = CL_Y[1], CL_Y[0]
    run = slope_len(z1, z0, y0, y1)
    s_out = along(z1, z0, y0, y1, CL_OUT_Z[1])
    x0, x1 = CL_X
    t = 1.0
    out += sloped("z", z1, z0, y0, y1, [
        (0.0, run, -t, 0.0, x0 + t, x1 - t, "fr_cl_floor", "planks"),
        (0.0, run, -t, CL_WALL, x0, x0 + t, "fr_cl_side1", "planks"),
        (0.0, s_out, -t, CL_WALL, x1 - t, x1, "fr_cl_side2", "planks"),
        (0.0, t, 0.0, CL_WALL, x0 + t, x1 - t, "fr_cl_end1", "planks"),
        (run - t, run, 0.0, CL_WALL, x0 + t, x1 - t, "fr_cl_end2", "planks"),
    ], f)
    for i, z in enumerate((20.0, 44.0, 68.0), 1):
        y = under_slope(CL_Y[1], CL_Y[0], CL_Z[1], CL_Z[0], z - 1.5, z + 1.5, 1.0)
        out.append(box([CL_X[0] + 1.5, 0.0, z - 1.5], [CL_X[1] - 1.5, y, z + 1.5], f"fr_cl_block{i}", f, "oak"))
    # the lip: the launder's floor carried on east over the plate table's head
    (lx0, lx1), (lz0, lz1) = CL_LIP
    ly = floor_y(CL_Y[0], CL_Y[1], CL_Z[0], CL_Z[1], lz0) - 0.03
    out.append(box([lx0, ly - 0.6, lz0], [lx1, ly, lz1], "fr_cl_lip", f, "planks"))
    out += build_plate_table()
    return out


def pt_s(z):
    """The distance along the plate table's slope from its head to over z."""
    return along(PT_Z[0], PT_Z[1], PT_Y[0], PT_Y[1], z)


def pt_floor(z):
    return floor_y(PT_Y[0], PT_Y[1], PT_Z[0], PT_Z[1], z)


def build_plate_table():
    """The plate table's bed: a plank floor falling south from under the concentrate launder's lip, a low
    curb under the lip and a side beyond it, the east side to the notch, the end boards; the spout from the
    notch to the east face; two oak blocks under it. Bare, it carries the concentrate to the outlet; the
    amalgamation plates lie in it when fitted."""
    f = "frame"
    x0, x1 = PT_X
    t = PT_T
    run = slope_len(PT_Z[0], PT_Z[1], PT_Y[0], PT_Y[1])
    sc, sn0, sn1 = pt_s(PT_CURB_Z), pt_s(PT_NOTCH_Z[0]), pt_s(PT_NOTCH_Z[1])
    out = sloped("z", PT_Z[0], PT_Z[1], PT_Y[0], PT_Y[1], [
        (0.0, run, -t, 0.0, x0 + t, x1 - t, "fr_pt_floor", "planks"),
        (0.0, sc, -t, PT_CURB, x0, x0 + t, "fr_pt_curb", "planks"),
        (sc, run, -t, PT_SIDE, x0, x0 + t, "fr_pt_side1", "planks"),
        (0.0, sn0, -t, PT_SIDE, x1 - t, x1, "fr_pt_side2", "planks"),
        (0.0, t, 0.0, PT_SIDE, x0 + t, x1 - t, "fr_pt_end1", "planks"),
        (run - t, run, 0.0, PT_SIDE, x0 + t, x1 - t, "fr_pt_end2", "planks"),
        (sn0 + 0.2, sn1 - 0.1, -t, 0.0, x1 - t, X_LEN - 0.15, "fr_pt_spout", "planks"),
    ], f)
    for i, z in enumerate((9.0, 17.0), 1):
        y = under_slope(PT_Y[0], PT_Y[1], PT_Z[0], PT_Z[1], z - 1.0, z + 1.0, t)
        out.append(box([x0 + 1.4, 0.0, z - 1.0], [x1 - 1.4, y, z + 1.0], f"fr_pt_block{i}", f, "oak"))
    return out


def build_plates():
    """The amalgamation plates (`plates`): three copper plates laid in the plate table's bed and an iron
    riffle across it after them, the trap that holds the amalgam the pulp carries off the plates. The
    dressing (`dressing`): the mercury rubbed into each plate, a thin silvery coat, and the mercury lying
    in the trap's well. Static: the pulp runs over them."""
    x0, x1 = PT_X[0] + PT_T, PT_X[1] - PT_T
    pieces = [(a, b, 0.0, PLATE_T, x0 + 0.15, x1 - 0.15, f"pl_plate{i + 1}", "copper") for i, (a, b) in enumerate(PLATE_S)]
    pieces.append((TRAP_S[0], TRAP_S[1], 0.0, TRAP_H, x0, x1, "pl_trap", "iron"))
    out = sloped("z", PT_Z[0], PT_Z[1], PT_Y[0], PT_Y[1], pieces, "plates")
    coats = [(a + 0.15, b - 0.15, PLATE_T, PLATE_T + COAT_T, x0 + 0.3, x1 - 0.3, f"pd_coat{i + 1}", "amalgam") for i, (a, b) in enumerate(PLATE_S)]
    coats.append((PLATE_S[-1][1] + 0.05, TRAP_S[0] - 0.02, 0.0, 0.35, x0 + 0.05, x1 - 0.05, "pd_well", "amalgam"))
    out += sloped("z", PT_Z[0], PT_Z[1], PT_Y[0], PT_Y[1], coats, "dressing")
    return out


def build_frame():
    return build_timbers() + build_drive_frame() + build_feed() + build_water() + build_launders()


# ---------------------------------------------------------------- builders: the drive's moving parts
ENTRY_C = (0.0, ENTRY[0], ENTRY[1])
MAIN_C = (0.0, MAIN[0], MAIN[1])
IDLER_C = (0.0, IDLER[0], IDLER[1])


def build_drive():
    """The entry shaft (oak, the vanilla axle's cross profile, then a round steel shaft through both
    cheeks) with A1 and A2; B1 and B2 loose on the main shaft with their catches; the idler; the main
    shaft, on which every tier's pulley is keyed."""
    out = []
    y, z = ENTRY
    # the vanilla axle's two boards, their ends a hundredth apart as the game's are (so they never share a plane)
    out.append(box([0.0, y - 1.0, z - 2.0], [CHEEK1_X[0] - 0.64, y + 1.0, z + 2.0], "entry_shafta", "entry", "oak"))
    out.append(box([0.01, y - 2.0, z - 1.0], [CHEEK1_X[0] - 0.65, y + 2.0, z + 1.0], "entry_shaftb", "entry", "oak"))
    out += disc("x", ENTRY_C, CHEEK1_X[0] - 0.9, CHEEK2_X[1] + 0.9, MAIN_R, "entry_shaft_rod", "entry", "steel")
    out += disc("x", ENTRY_C, CHEEK1_X[0] - 0.9, CHEEK1_X[0] - 0.62, 2.1, "entry_coupling", "entry", "steel", k=6)
    out += gear("x", ENTRY_C, *RECT_X1, RECT_R["a1"], RECT_A1, RECT_MOD, "entry_a1", "entry", "steel")
    out += gear("x", ENTRY_C, *RECT_X2, RECT_R["a2"], RECT_A2, RECT_MOD, "entry_a2", "entry", "steel")
    out += gear("x", MAIN_C, *RECT_X1, RECT_R["b1"], RECT_B1, RECT_MOD, "rectb1", "rectb1", "steel",
                phase=mesh_phase(ENTRY_C, 0.0, RECT_A1, MAIN_C, RECT_B1), body_k=6)
    out.append(box([RECT_X1[1], MAIN[0] + 1.1, MAIN[1] - 0.3], [RECT_X1[1] + 0.25, MAIN[0] + 2.6, MAIN[1] + 0.3], "rectb1_catch", "rectb1", "steel"))
    ip = mesh_phase(ENTRY_C, 0.0, RECT_A2, IDLER_C, RECT_I)
    out += gear("x", IDLER_C, *RECT_X2, RECT_R["i"], RECT_I, RECT_MOD, "idler", "idler", "steel", phase=ip)
    out += gear("x", MAIN_C, *RECT_X2, RECT_R["b2"], RECT_B2, RECT_MOD, "rectb2", "rectb2", "steel",
                phase=mesh_phase(IDLER_C, ip, RECT_I, MAIN_C, RECT_B2), body_k=6)
    out.append(box([RECT_X2[0] - 0.25, MAIN[0] - 2.4, MAIN[1] - 0.3], [RECT_X2[0], MAIN[0] - 1.1, MAIN[1] + 0.3], "rectb2_catch", "rectb2", "steel"))
    out += disc("x", MAIN_C, *MAIN_X, MAIN_R, "mainshaft_rod", "mainshaft", "steel")
    out += disc("x", MAIN_C, RECT_X1[1], RECT_X2[0], 1.4, "mainshaft_hub", "mainshaft", "steel")
    # collars either side of the hanger
    for i, x0 in enumerate((HANGER_X[0] - 0.6, HANGER_X[1]), 1):
        out += disc("x", MAIN_C, x0, x0 + 0.6, 1.5, f"mainshaft_collar{i}", "mainshaft", "steel")
    return out


def main_pulley(x0, x1, name, part):
    return pulley("x", MAIN_C, x0, x1, PULLEY_R, name, part)


# ---------------------------------------------------------------- builders: tier 1, the long tom
def lt_floor(x):
    return LT_TAIL_Y + (LT_X[1] - x) * LT_SLOPE


def rf_floor(x):
    return RF_TAIL_Y + (RF_X[1] - x) * RF_SLOPE


def tc_floor(z):
    return floor_y(TC_Y[0], TC_Y[1], TC_Z[0], TC_Z[1], z)


def along(a0, a1, y0, y1, at):
    """The distance along a slope from its a0 end to the point over `at`."""
    return abs(at - a0) * math.hypot(a1 - a0, y1 - y0) / abs(a1 - a0)


def build_longtom():
    """The tom (a plank floor, then the last 8 of it the tom iron, a perforated plate turned up at the
    tail), the riffle box with its cleats, the tail chute to the tailings launder, the trestles and a rest
    bar, and the rake's gallows (posts, cross beams, the crank shaft's bearings, the crosshead's guides)."""
    p = "longtom"
    out = []
    y0, y1 = lt_floor(LT_X[0]), lt_floor(LT_X[1])
    s_r = along(LT_X[0], LT_X[1], y0, y1, RIDDLE_X[0])
    run = slope_len(LT_X[0], LT_X[1], y0, y1)
    c0, c1 = LT_Z
    out += sloped("x", LT_X[0], LT_X[1], y0, y1, [
        (0.0, s_r, -1.0, 0.0, c0 + 1.0, c1 - 1.0, "lt_tom_floor", "planks"),
        (s_r, run, -0.5, 0.0, c0 + 1.0, c1 - 1.0, "lt_riddle", "screen"),
        (0.0, run, -1.0, LT_SIDE, c0, c0 + 1.0, "lt_tom_side1", "planks"),
        (0.0, run, -1.0, LT_SIDE, c1 - 1.0, c1, "lt_tom_side2", "planks"),
        (0.0, 1.0, 0.0, LT_SIDE, c0 + 1.0, c1 - 1.0, "lt_tom_end", "planks"),
    ], p)
    # the riddle's turned-up tail: a screen rising 35 degrees
    yt = y1 - 0.25
    a = 35 * DEG
    up = strut([LT_X[1] - 0.2, yt, 0.0], [LT_X[1] - 0.2 + 2.4 * math.cos(a), yt + 2.4 * math.sin(a), 0.0], 0.5, c1 - c0 - 2.0,
               "lt_riddle_end", p, "screen", axis="z")
    up.c[2] = (c0 + c1) / 2
    out.append(up)
    # the riffle box: closed at its head, open at its tail over the tail chute; cleats across its floor
    r0, r1 = rf_floor(RF_X[0]), rf_floor(RF_X[1])
    cleats = []
    for i, x in enumerate(CLEATS, 1):
        s = along(RF_X[0], RF_X[1], r0, r1, x)
        cleats.append((s - 0.8, s, 0.0, 1.2, RF_Z[0] + 1.0, RF_Z[1] - 1.0, f"lt_cleat{i}", "oak"))
    out += trough("x", RF_X[0], RF_X[1], r0, r1, RF_Z[0], RF_Z[1], RF_SIDE, "lt_rf", p, ends=(True, False), extra=cleats)
    # the rest: a cross bar on the riffle box's sides under the tom's floor
    out.append(box([REST_X[0], rf_floor(REST_X[0]) + RF_SIDE + 0.02, RF_Z[0]], [REST_X[1], lt_floor(REST_X[1]) - 1.02, RF_Z[1]], "lt_rest", p, "oak"))
    # the tail chute: along z under the riffle box's open end, to its lip over the tailings launder
    out += trough("z", TC_Z[0], TC_Z[1], TC_Y[0], TC_Y[1], TC_X[0], TC_X[1], TC_SIDE, "lt_tc", p, ends=(True, False))
    # trestles: a cross bar on two legs under the tom and under the riffle box; blocks under the tail chute
    def trestle(name, x, top, z0, z1):
        out.append(box([x - 1.0, top - 1.5, z0], [x + 1.0, top - 0.02, z1], f"{name}_bar", p, "oak"))
        for j, zl in enumerate((z0, z1 - 2.0), 1):
            out.append(box([x - 0.9, 0.0, zl + 0.1], [x + 0.9, top - 1.5, zl + 1.9], f"{name}_leg{j}", p, "oak"))

    for i, x in enumerate((28.0, 36.0), 1):
        trestle(f"lt_trestle{i}", x, lt_floor(x) - 1.0, LT_Z[0] - 1.0, LT_Z[1] + 1.0)
    for i, x in enumerate((47.0, 56.5, 64.5), 3):
        trestle(f"lt_trestle{i}", x, rf_floor(x) - 1.0, RF_Z[0] - 1.0, RF_Z[1] + 1.0)
    for i, z in enumerate((28.0, 38.0), 1):
        out.append(box([TC_X[0] + 1.5, 0.0, z - 1.0], [TC_X[1] - 1.5, under_slope(TC_Y[0], TC_Y[1], TC_Z[0], TC_Z[1], z - 1.0, z + 1.0, 1.0), z + 1.0], f"lt_tcblock{i}", p, "oak"))
    # the gallows: two frames across the tom, each two posts and a cross beam, a bearing block for the
    # crank shaft on the beam and a guide bar under it for the rake's crosshead
    y, z = RAKE_C
    for i, (x0, x1) in enumerate(GALLOWS_X, 1):
        for j, (z0, z1) in enumerate(GALLOWS_Z, 1):
            out.append(box([x0, 0.0, z0], [x1, GALLOWS_TOP - 2.0, z1], f"lt_gallows{i}_post{j}", p, "oak"))
        out.append(box([x0, GALLOWS_TOP - 2.0, GALLOWS_Z[0][0]], [x1, GALLOWS_TOP, GALLOWS_Z[1][1]], f"lt_gallows{i}_beam", p, "oak"))
        out.append(box([x0, GALLOWS_TOP, z - 1.6], [x1, y + 1.6, z + 1.6], f"lt_gallows{i}_bearing", p, "iron"))
        out.append(box([x0 + 0.3, GALLOWS_TOP - 3.2, z - 9.0], [x1 - 0.3, GALLOWS_TOP - 2.0, z + 9.0], f"lt_gallows{i}_guide", p, "iron"))
    out += belt("x", MAIN_C, PULLEY_R, (0.0, *RAKE_C), PULLEY_R, LT_PULLEY_X[0] + 0.15, LT_PULLEY_X[1] - 0.15, "lt_belt", p)
    return out


def build_longtom_moving():
    """The pulley on the main shaft; the crank shaft with its pulley, collars and the crank (its pin up
    at rest); the rake: shoes on the guide bars, the crosshead between the gallows, two hangers, the tine
    bar and five tines over the riddle, and the yoke, whose upright slot the crank pin runs in."""
    out = main_pulley(*LT_PULLEY_X, "ltpulley", "ltpulley")
    c = (0.0, *RAKE_C)
    y, z = RAKE_C
    xa, xb = RAKE_CRANK_X
    out += shaft_pieces(c, *RAKE_X, [(xa, xb)], MAIN_R * 0.8, "ltcrank_rod", "ltcrank")
    out += pulley("x", c, *LT_PULLEY_X, PULLEY_R, "ltcrank_pulley", "ltcrank")
    out += throw(c, xa, xb, RAKE_E, 0.0, "ltcrank", "ltcrank")
    for i, (x0, x1) in enumerate(GALLOWS_X, 1):
        for j, xc in enumerate((x0 - 0.55, x1 + 0.05), 1):
            out += disc("x", c, xc, xc + 0.5, 1.3, f"ltcrank_collar{i}{j}", "ltcrank", "steel")
    g_lo = GALLOWS_TOP - 3.2
    for i, (x0, x1) in enumerate(GALLOWS_X, 1):
        out.append(box([x0 - 0.2, g_lo - 1.2, z - 2.0], [x1 + 0.2, g_lo - 0.02, z + 2.0], f"ltrake_shoe{i}", "ltrake", "iron"))
    out.append(box([GALLOWS_X[0][0] - 0.2, g_lo - 2.4, z - 1.0], [GALLOWS_X[1][1] + 0.2, g_lo - 1.2, z + 1.0], "ltrake_crosshead", "ltrake", "oak"))
    tine_y = lt_floor(RIDDLE_X[0]) + 0.7
    tbar = (RIDDLE_X[0] + 0.6, RIDDLE_X[1] - 0.6)
    out.append(box([tbar[0], tine_y + 2.6, z - 0.8], [tbar[1], tine_y + 3.8, z + 0.8], "ltrake_bar", "ltrake", "iron"))
    for i, xc in enumerate((tbar[0] + 0.7, tbar[1] - 0.7), 1):
        out.append(box([xc - 0.5, tine_y + 3.8, z - 0.5], [xc + 0.5, g_lo - 2.4, z + 0.5], f"ltrake_hanger{i}", "ltrake", "iron"))
    for i in range(5):
        xc = tbar[0] + 0.8 + (tbar[1] - tbar[0] - 1.6) * i / 4
        yb = lt_floor(xc) + 0.6
        out.append(box([xc - 0.3, yb, z - 0.3], [xc + 0.3, tine_y + 2.6, z + 0.3], f"ltrake_tine{i + 1}", "ltrake", "iron"))
    # the yoke: two uprights either side of the pin's slot, a cap over them, standing on the crosshead
    yx = (xa + 0.35, xb - 0.35)
    slot = 0.42
    top = y + RAKE_E + 0.9
    out.append(box([yx[0], g_lo - 1.2, z - slot - 0.7], [yx[1], top, z - slot], "ltrake_yoke1", "ltrake", "iron"))
    out.append(box([yx[0], g_lo - 1.2, z + slot], [yx[1], top, z + slot + 0.7], "ltrake_yoke2", "ltrake", "iron"))
    out.append(box([yx[0], top, z - slot - 0.7], [yx[1], top + 0.7, z + slot + 0.7], "ltrake_yokecap", "ltrake", "iron"))
    return out


# ---------------------------------------------------------------- builders: tier 2, the jig
def jl_floor(x):
    return floor_y(JL_Y[0], JL_Y[1], JL_X[0], JL_X[1], x)


def jt_floor(z):
    return floor_y(JT_Y[0], JT_Y[1], JT_Z[0], JT_Z[1], z)


def jig_crank_x(i):
    """The throw for plunger i: its two webs' x either side of the pin."""
    px0, px1 = PLUNGERS[i]
    xc = (px0 + px1) / 2
    return (xc - 1.6, xc - 1.0), (xc + 1.0, xc + 1.6)


def build_jig():
    """The hutch on two sills: plank walls, the long partition (open under it), the cross partition, the
    sieves on ledges, the overflow and the tail lip, the plunger rods' guides, two spigots, the
    concentrate launder to the concentrate launder and the tail chute to the tailings launder; the crank
    shaft's two pedestals; the belt."""
    p = "jig"
    out = []
    x0, x1 = JG_X
    z0, z1 = JG_Z
    for i, (za, zb) in enumerate(((z0 + 1.0, z0 + 4.0), (z1 - 4.0, z1 - 1.0)), 1):
        out.append(box([x0 + 1.0, 0.0, za], [x1 - 1.0, JG_FLOOR - 1.0, zb], f"jg_sill{i}", p, "oak"))
    out.append(box([x0, JG_FLOOR - 1.0, z0], [x1, JG_FLOOR, z1], "jg_floor", p, "planks"))
    out.append(box([x0, JG_FLOOR, z0], [x1, JG_TOP, z0 + 1.0], "jg_wall_n", p, "planks"))
    out.append(box([x0, JG_FLOOR, z1 - 1.0], [x1, JG_TOP, z1], "jg_wall_s", p, "planks"))
    out.append(box([x0, JG_FLOOR, z0 + 1.0], [x0 + 1.0, JG_TOP, z1 - 1.0], "jg_wall_w", p, "planks"))
    # the east wall: low on the sieve side (the tail lip, with a lip board over the tail chute), full on the plunger side
    out.append(box([x1 - 1.0, JG_FLOOR, z0 + 1.0], [x1, LIP2_Y, JG_PART_Z[1]], "jg_wall_e1", p, "planks"))
    out.append(box([x1 - 1.0, JG_FLOOR, JG_PART_Z[1]], [x1, JG_TOP, z1 - 1.0], "jg_wall_e2", p, "planks"))
    out.append(box([x1 - 1.0, LIP2_Y, z0 + 1.0], [JT_X[0] + 1.6, LIP2_Y + 0.6, JG_PART_Z[0]], "jg_lip", p, "planks"))
    out.append(box([x0 + 1.0, JG_PART_Y0, JG_PART_Z[0]], [x1 - 1.0, JG_TOP, JG_PART_Z[1]], "jg_partition", p, "planks"))
    cx0, cx1 = JG_CROSS_X
    out.append(box([cx0, JG_FLOOR, z0 + 1.0], [cx1, LIP1_Y, JG_PART_Z[0]], "jg_cross1", p, "planks"))
    out.append(box([cx0, JG_FLOOR, JG_PART_Z[1]], [cx1, JG_TOP, z1 - 1.0], "jg_cross2", p, "planks"))
    # the sieves, on ledges along the walls
    for i, (sx0, sx1) in enumerate(((x0 + 1.0, cx0), (cx1, x1 - 1.0)), 1):
        out.append(box([sx0, SIEVE_Y - 0.5, z0 + 1.0], [sx1, SIEVE_Y, JG_PART_Z[0]], f"jg_sieve{i}", p, "screen"))
        out.append(box([sx0, SIEVE_Y - 1.2, z0 + 1.0], [sx1, SIEVE_Y - 0.5, z0 + 1.6], f"jg_ledge{i}n", p, "oak"))
        out.append(box([sx0, SIEVE_Y - 1.2, JG_PART_Z[0] - 0.6], [sx1, SIEVE_Y - 0.5, JG_PART_Z[0]], f"jg_ledge{i}s", p, "oak"))
    # the rods' guides: a bar across the plunger side over each plunger, an eye round the rod
    zc = JG_C[1]
    for i, (px0, px1) in enumerate(PLUNGERS, 1):
        xc = (px0 + px1) / 2
        out.append(box([xc - 1.5, JG_TOP, JG_PART_Z[0]], [xc + 1.5, JG_TOP + 1.0, zc - 1.0], f"jg_guide{i}n", p, "oak"))
        out.append(box([xc - 1.5, JG_TOP, zc + 1.0], [xc + 1.5, JG_TOP + 1.0, z1], f"jg_guide{i}s", p, "oak"))
        out.append(box([xc - 1.5, JG_TOP + 0.05, zc - 1.0], [xc + 1.5, JG_TOP + 0.95, zc - 0.55], f"jg_guide{i}_eyen", p, "iron"))
        out.append(box([xc - 1.5, JG_TOP + 0.05, zc + 0.55], [xc + 1.5, JG_TOP + 0.95, zc + 1.0], f"jg_guide{i}_eyes", p, "iron"))
        out.append(box([xc - 1.5, JG_TOP + 0.05, zc - 0.55], [xc - 0.55, JG_TOP + 0.95, zc + 0.55], f"jg_guide{i}_eyew", p, "iron"))
        out.append(box([xc + 0.55, JG_TOP + 0.05, zc - 0.55], [xc + 1.5, JG_TOP + 0.95, zc + 0.55], f"jg_guide{i}_eyee", p, "iron"))
    # the spigots: short pipes out of the north wall near the hutch's floor, with plugs
    for i, (sx0, sx1) in enumerate(SPIGOT_X, 1):
        out.append(box([sx0, SPIGOT_Y[0], JL_Z[1] - 1.5], [sx1, SPIGOT_Y[1], z0], f"jg_spigot{i}", p, "iron"))
        out.append(box([sx0 + 0.4, SPIGOT_Y[1], JL_Z[1] - 1.2], [sx1 - 0.4, SPIGOT_Y[1] + 0.8, JL_Z[1] - 0.6], f"jg_spigot{i}_plug", p, "iron"))
    # the concentrate launder along the north wall to the concentrate launder; the tail chute to the tailings launder
    out += trough("x", JL_X[0], JL_X[1], JL_Y[0], JL_Y[1], JL_Z[0], JL_Z[1], JL_SIDE, "jg_cl", p, ends=(True, False), t=0.8)
    for i, x in enumerate((34.0, 58.0, 76.0), 1):
        out.append(box([x - 1.0, 0.0, JL_Z[0] + 0.8], [x + 1.0, under_slope(JL_Y[0], JL_Y[1], JL_X[0], JL_X[1], x - 1.0, x + 1.0, 0.8), JL_Z[1] - 0.8], f"jg_clblock{i}", p, "oak"))
    out += trough("z", JT_Z[0], JT_Z[1], JT_Y[0], JT_Y[1], JT_X[0], JT_X[1], JT_SIDE, "jg_tc", p, ends=(True, False))
    for i, z in enumerate((26.0, 38.0), 1):
        out.append(box([JT_X[0] + 1.5, 0.0, z - 1.0], [JT_X[1] - 1.5, under_slope(JT_Y[0], JT_Y[1], JT_Z[0], JT_Z[1], z - 1.0, z + 1.0, 1.0), z + 1.0], f"jg_tcblock{i}", p, "oak"))
    # the crank shaft's pedestals: one west of the hutch, one east of the tail chute
    y, z = JG_C
    for i, (px0, px1) in enumerate(JG_PEDESTALS, 1):
        out.append(box([px0, 0.0, z - 1.2], [px1, y - 1.6, z + 1.2], f"jg_pedestal{i}", p, "oak"))
        out.append(box([px0, y - 1.6, z - 1.6], [px1, y + 1.6, z + 1.6], f"jg_bearing{i}", p, "iron"))
    out += belt("x", MAIN_C, PULLEY_R, (0.0, *JG_C), JG_PULLEY_R, JG_PULLEY_X[0] + 0.15, JG_PULLEY_X[1] - 0.15, "jg_belt", p)
    return out


def build_jig_moving():
    """The pulley on the main shaft; the crank shaft (two throws, the shaft broken by each throw's webs,
    plunger 1's pin at +z and plunger 2's at -z at rest: the plungers at mid-stroke); the plungers: a
    board, a boss, a rod through its guide, and a yoke whose level slot the crank pin runs in."""
    out = main_pulley(*JG_PULLEY_X, "jgpulley", "jgpulley")
    c = (0.0, *JG_C)
    y, z = JG_C
    gaps = [(jig_crank_x(i)[0][1], jig_crank_x(i)[1][0]) for i in range(2)]
    out += shaft_pieces(c, *JG_SHAFT_X, gaps, MAIN_R * 0.9, "jgshaft_rod", "jgshaft")
    out += pulley("x", c, *JG_PULLEY_X, JG_PULLEY_R, "jgshaft_pulley", "jgshaft")
    for i, (xa, xb) in enumerate(gaps):
        out += throw(c, xa, xb, JG_E, math.pi / 2 if i == 0 else -math.pi / 2, f"jgshaft_throw{i + 1}", "jgshaft")
    for i, (px0, px1) in enumerate(JG_PEDESTALS, 1):
        for j, xc in enumerate((px0 - 0.55, px1 + 0.05), 1):
            out += disc("x", c, xc, xc + 0.5, 1.3, f"jgshaft_collar{i}{j}", "jgshaft", "steel")
    for i, (px0, px1) in enumerate(PLUNGERS):
        pid = f"jgplunger{i + 1}"
        xc = (px0 + px1) / 2
        (wa0, wa1), (wb0, wb1) = jig_crank_x(i)
        out.append(box([px0, PLUNGER_Y[0], PLUNGER_Z[0]], [px1, PLUNGER_Y[1], PLUNGER_Z[1]], f"{pid}_board", pid, "oak"))
        out.append(box([xc - 1.2, PLUNGER_Y[1], z - 1.2], [xc + 1.2, PLUNGER_Y[1] + 0.6, z + 1.2], f"{pid}_boss", pid, "iron"))
        slot = 0.41
        ylo = y - slot - 0.7
        out.append(box([xc - 0.4, PLUNGER_Y[1] + 0.6, z - 0.4], [xc + 0.4, ylo, z + 0.4], f"{pid}_rod", pid, "steel"))
        yx = (wa1 + 0.15, wb0 - 0.15)
        zl, zh = z - JG_E - 1.0, z + JG_E + 1.0
        out.append(box([yx[0], ylo, zl], [yx[1], y - slot, zh], f"{pid}_yokelow", pid, "iron"))
        out.append(box([yx[0], y + slot, zl], [yx[1], y + slot + 0.7, zh], f"{pid}_yokehigh", pid, "iron"))
        out.append(box([yx[0], y - slot, zl], [yx[1], y + slot, zl + 0.5], f"{pid}_yokeend1", pid, "iron"))
        out.append(box([yx[0], y - slot, zh - 0.5], [yx[1], y + slot, zh], f"{pid}_yokeend2", pid, "iron"))
    return out


# ---------------------------------------------------------------- builders: tier 3, the shaking table
TILT_O = (0.0, DECK_TOP, DECK_Z[0])           # the deck is built level with its top at DECK_TOP, then turned about its north edge


def deck_y(z):
    """The deck's top at z (it falls south by DECK_TILT)."""
    return DECK_TOP - (z - DECK_Z[0]) * math.tan(DECK_TILT * DEG)


def tilt(els):
    return rotate(els, "x", DECK_TILT, TILT_O)


def build_table_moving():
    """The pulley on the main shaft; the head motion's input shaft with its pulley; the deck: boards on
    two joists, the riffles (the low ones shorter, ending on a diagonal), the water launder along the
    upper edge and the feed box at the head corner, the end lip over the concentrate launder, a bracket
    under the head, and the pull rod to the head motion (all one part: the deck slides)."""
    out = main_pulley(*HM_PULLEY_X, "tbpulley", "tbpulley")
    c = (0.0, *HM_IN)
    out += disc("x", c, HM_PULLEY_X[0] - 0.2, HM_X[1] - 0.6, 0.7, "tbinput_rod", "tbinput", "steel")
    out += pulley("x", c, *HM_PULLEY_X, HM_PULLEY_R, "tbinput_pulley", "tbinput")
    d = "tbdeck"
    dk = []
    x0, x1 = DECK_X
    z0, z1 = DECK_Z
    dk.append(box([x0, DECK_TOP - DECK_T, z0], [x1, DECK_TOP, z1], f"{d}_board", d, "planks"))
    dk.append(box([x1, DECK_TOP - DECK_T, z0 + 0.5], [CL_X[0] + 2.4, DECK_TOP - 0.4, z1 - 0.5], f"{d}_lip", d, "planks"))
    for i, (jz0, jz1) in enumerate(JOISTS_Z, 1):
        dk.append(box([x0 + 1.0, DECK_TOP - DECK_T - JOIST_Y, jz0], [x1 - 1.0, DECK_TOP - DECK_T, jz1], f"{d}_joist{i}", d, "oak"))
    zr0, zr1 = WL_Z[1] + 1.0, z1 - 1.5
    for i in range(RIFFLES):
        zr = zr0 + (zr1 - zr0) * i / (RIFFLES - 1)
        f = i / (RIFFLES - 1)
        xe = x1 - 2.0 - f * 22.0
        xs = FB_X[1] + 1.0 if zr < FB_Z[1] + 0.5 else x0 + 2.0
        dk.append(box([xs, DECK_TOP, zr - 0.2], [xe, DECK_TOP + RIFFLE_H, zr + 0.2], f"{d}_riffle{i + 1}", d, "oak"))
    t = 0.6
    wl0, wl1 = WL_Z
    dk += [box([WL_X[0], DECK_TOP, wl0], [WL_X[1], DECK_TOP + t, wl1], f"{d}_wl_floor", d, "planks"),
           box([WL_X[0], DECK_TOP + t, wl0], [WL_X[1], DECK_TOP + 3.0, wl0 + t], f"{d}_wl_side1", d, "planks"),
           box([WL_X[0], DECK_TOP + t, wl1 - t], [WL_X[1], DECK_TOP + 3.0, wl1], f"{d}_wl_side2", d, "planks"),
           box([WL_X[0], DECK_TOP + t, wl0 + t], [WL_X[0] + t, DECK_TOP + 3.0, wl1 - t], f"{d}_wl_end1", d, "planks"),
           box([WL_X[1] - t, DECK_TOP + t, wl0 + t], [WL_X[1], DECK_TOP + 3.0, wl1 - t], f"{d}_wl_end2", d, "planks")]
    fb0, fb1 = FB_Z
    dk += [box([FB_X[0], DECK_TOP, fb0], [FB_X[1], DECK_TOP + 4.0, fb0 + t], f"{d}_fb_side1", d, "planks"),
           box([FB_X[0], DECK_TOP + 1.0, fb1 - t], [FB_X[1], DECK_TOP + 4.0, fb1], f"{d}_fb_side2", d, "planks"),
           box([FB_X[0], DECK_TOP, fb0 + t], [FB_X[0] + t, DECK_TOP + 4.0, fb1 - t], f"{d}_fb_end1", d, "planks"),
           box([FB_X[1] - t, DECK_TOP, fb0 + t], [FB_X[1], DECK_TOP + 4.0, fb1 - t], f"{d}_fb_end2", d, "planks")]
    py, pz = PULLROD
    dk.append(box([x0 + 0.5, py - 1.5, pz - 1.5], [x0 + 3.0, DECK_TOP - DECK_T, pz + 1.5], f"{d}_bracket", d, "iron"))
    tilt(dk)
    out += dk
    out += disc("x", (0.0, py, pz), HM_X[1] - 0.6, x0 + 1.0, 0.6, f"{d}_pullrod", d, "steel")
    return out


def joist_under(jz0, jz1):
    """The lowest point of a joist's underside (the deck is tilted: its low edge)."""
    return deck_y(jz1) - DECK_T - JOIST_Y


def build_table():
    """The head motion's box on its base (the input shaft goes in at its west face, the pull rod comes
    out of its east face: the toggle inside is enclosed, as a period head motion's is); the legs with
    slide plates under the deck's joists, braced across; the apron from the low edge into the tailings
    launder; the middlings box under the low edge at the tail, its spout over the tailings launder; the
    belt."""
    p = "table"
    out = []
    out.append(box([HM_X[0] - 0.5, 0.0, HM_Z[0] - 0.5], [HM_X[1] + 0.5, HM_Y[0], HM_Z[1] + 0.5], "tb_hm_base", p, "oak"))
    out.append(box([HM_X[0], HM_Y[0], HM_Z[0]], [HM_X[1], HM_Y[1], HM_Z[1]], "tb_hm_box", p, "iron"))
    out.append(box([HM_X[0] + 0.5, HM_Y[1], HM_Z[0] + 0.5], [HM_X[1] - 0.5, HM_Y[1] + 0.6, HM_Z[1] - 0.5], "tb_hm_lid", p, "iron"))
    y, z = HM_IN
    out += disc("x", (0.0, y, z), HM_X[0] - 0.8, HM_X[0], 1.6, "tb_hm_boss", p, "iron")
    py, pz = PULLROD
    out += disc("x", (0.0, py, pz), HM_X[1], HM_X[1] + 0.6, 1.4, "tb_hm_gland", p, "iron")
    for i, (bx0, bx1) in enumerate(BEARERS_X, 1):
        tops = []
        for j, (jz0, jz1) in enumerate(JOISTS_Z, 1):
            under = joist_under(jz0, jz1) - 0.02
            out.append(box([bx0 - 0.5, under - 0.6, jz0 - 0.5], [bx1 + 0.5, under, jz1 + 0.5], f"tb_slide{i}{j}", p, "iron"))
            out.append(box([bx0, 0.0, jz0 - 0.2], [bx1, under - 0.6, jz1 + 0.2], f"tb_leg{i}{j}", p, "oak"))
            tops.append(under)
        out.append(box([bx0 + 0.4, 6.0, JOISTS_Z[0][1] + 0.2], [bx1 - 0.4, 8.0, JOISTS_Z[1][0] - 0.2], f"tb_rail{i}", p, "oak"))
    # the apron: a board from just under the deck's low edge down into the tailings launder
    za, ya = DECK_Z[1] - 0.5, deck_y(DECK_Z[1]) - DECK_T - 0.5
    zb = TL_Z[0] + 2.6
    yb = floor_y(TL_Y[0], TL_Y[1], 0.0, TL_X1, APRON_X[1]) + TL_WALL + 0.25
    ap = strut([0.0, ya, za], [0.0, yb, zb], 0.6, APRON_X[1] - APRON_X[0] - 0.1, "tb_apron", p, "planks", axis="x")
    ap.c[0] = sum(APRON_X) / 2 - 0.05
    out.append(ap)
    zs = TL_Z[0] - 3.0
    for i, x in enumerate((APRON_X[0] + 3.0, (APRON_X[0] + APRON_X[1]) / 2, APRON_X[1] - 3.0), 1):
        yt = ya + (yb - ya) * (zs + 0.7 - za) / (zb - za)
        out.append(box([x - 0.6, 0.0, zs], [x + 0.6, yt - 0.7, zs + 1.4], f"tb_apron_post{i}", p, "oak"))
    # the middlings box: under the low edge at the tail, its floor falling south to its spout
    out += trough("z", MB_Z[0], MB_Z[1], MB_TOP - 2.0, MB_TOP - 3.4, MB_X[0], MB_X[1], 2.0, "tb_mb", p, ends=(True, False), t=0.6)
    for i, x in enumerate((MB_X[0] + 1.2, MB_X[1] - 1.2), 1):
        zl = MB_Z[0] + 1.5
        yl = under_slope(MB_TOP - 2.0, MB_TOP - 3.4, MB_Z[0], MB_Z[1], zl - 0.7, zl + 0.7, 0.6)
        out.append(box([x - 0.7, 0.0, zl - 0.7], [x + 0.7, yl, zl + 0.7], f"tb_mb_leg{i}", p, "oak"))
    out += belt("x", MAIN_C, PULLEY_R, (0.0, *HM_IN), HM_PULLEY_R, HM_PULLEY_X[0] + 0.15, HM_PULLEY_X[1] - 0.15, "tb_belt", p)
    return out


# ---------------------------------------------------------------- builders: tier 4, the vanner
def run_y(x):
    """The belt's upper run's underside at x (from the foot roller's top to the head roller's top)."""
    return FOOT[1] + ROLL_R + (x - FOOT[0]) * (HEAD[1] - FOOT[1]) / (HEAD[0] - FOOT[0])


def feedbox_y():
    """The vanner's feed box's floor (underside): over the belt at the box's east (higher) end."""
    return run_y(FEEDBOX_X[1]) + BELT_T + 0.5


def dist_y():
    """The water distributor pipe's middle: over the head roller's top."""
    return HEAD[1] + ROLL_R + BELT_T + 2.0


def build_vanner_moving():
    """The pulley on the main shaft; the crank shaft (three cranks, their pins up at rest, and a small
    pulley for the worm's belt); the shaking frame: two rails, their arms out over the tank to the head
    roller, two ties, the foot roller's bearings, the yokes whose upright slots the crank pins run in, the
    belt and its raised edges, the carrying rollers' bearings, the feed box and the water distributor, the
    worm shaft's brackets; the worm shaft and worm; the head roller with its worm wheel; the foot roller
    and the carrying rollers."""
    out = main_pulley(*VN_PULLEY_X, "vnpulley", "vnpulley")
    c = (0.0, *VN_C)
    y, z = VN_C
    gaps = [(xc - VN_THROW, xc + VN_THROW) for xc in VN_CRANKS]
    out += shaft_pieces(c, *VN_SHAFT_X, gaps, MAIN_R * 0.8, "vncrank_rod", "vncrank")
    out += pulley("x", c, *VN_PULLEY_X, PULLEY_R, "vncrank_pulley", "vncrank")
    out += pulley("x", c, *SMALL_X, SMALL_R, "vncrank_small", "vncrank", hub_r=1.0)
    for i, (xa, xb) in enumerate(gaps, 1):
        out += throw(c, xa, xb, VN_E, 0.0, f"vncrank_throw{i}", "vncrank", web=0.5, w=2.0, pin_r=0.35)
    for i, (px0, px1) in enumerate(VN_PEDESTALS, 1):
        for j, xc in enumerate((px0 - 0.55, px1 + 0.05), 1):
            out += disc("x", c, xc, xc + 0.5, 1.2, f"vncrank_collar{i}{j}", "vncrank", "steel")
    f = "vnframe"
    for j, (rz0, rz1) in enumerate(VN_RAIL_Z, 1):
        out.append(box([VN_RAIL_X[0], VN_RAIL_Y[0], rz0], [VN_RAIL_X[1], VN_RAIL_Y[1], rz1], f"{f}_rail{j}", f, "oak"))
        out.append(box([VN_RAIL_X[1], VN_RAIL_Y[0], rz0 + 0.3], [HEAD[0] + 2.0, VN_RAIL_Y[1], rz1 - 0.3], f"{f}_arm{j}", f, "iron"))
        out.append(box([FOOT[0] - 1.2, FOOT[1] - 1.2, rz0 + 0.2], [FOOT[0] + 1.2, VN_RAIL_Y[0], rz1 - 0.2], f"{f}_footbearing{j}", f, "iron"))
    for i, x in enumerate(VN_TIES, 1):
        out.append(box([x - 1.0, VN_RAIL_Y[0] - 1.6, VN_RAIL_Z[0][0] + 0.3], [x + 1.0, VN_RAIL_Y[0], VN_RAIL_Z[1][1] - 0.3], f"{f}_tie{i}", f, "oak"))
    slot = 0.42
    for i, xc in enumerate(VN_CRANKS, 1):
        yx = (xc - VN_THROW + 0.3, xc + VN_THROW - 0.3)
        top = y + VN_E + 0.9
        out.append(box([yx[0], top, z - slot - 0.7], [yx[1], top + 0.8, VN_RAIL_Z[0][0]], f"{f}_yokearm{i}", f, "iron"))
        out.append(box([yx[0], y - VN_E - 0.9, z - slot - 0.7], [yx[1], top, z - slot], f"{f}_yoke{i}a", f, "iron"))
        out.append(box([yx[0], y - VN_E - 0.9, z + slot], [yx[1], top, z + slot + 0.7], f"{f}_yoke{i}b", f, "iron"))
        out.append(box([yx[0], y - VN_E - 1.6, z - slot - 0.7], [yx[1], y - VN_E - 0.9, z + slot + 0.7], f"{f}_yoke{i}c", f, "iron"))
    out += belt_loop("z", [((FOOT[0], FOOT[1]), ROLL_R, 1), (DEFLECT, DEFLECT_R, -1), ((HEAD[0], HEAD[1]), ROLL_R, 1)], *BELT_Z,
                     f"{f}_belt", f, t=BELT_T)

    for j, (ez0, ez1) in enumerate(((BELT_Z[0], BELT_Z[0] + 0.6), (BELT_Z[1] - 0.6, BELT_Z[1])), 1):
        a = [FOOT[0] + 0.3, run_y(FOOT[0] + 0.3) + BELT_T + 0.3, 0.0]
        b = [HEAD[0] - 0.3, run_y(HEAD[0] - 0.3) + BELT_T + 0.3, 0.0]
        el = strut(a, b, 0.6, ez1 - ez0 - 0.02, f"{f}_edge{j}", f, "leather", axis="z")
        el.c[2] = (ez0 + ez1) / 2
        out.append(el)
    # the feed box across the belt near the head: a shallow box, its floor slotted (a gap at its west side)
    fy = feedbox_y()
    fx0, fx1 = FEEDBOX_X
    zi0, zi1 = BELT_Z[0] + 0.5, BELT_Z[1] - 0.5
    out.append(box([fx0 + 1.2, fy, zi0], [fx1, fy + 0.4, zi1], f"{f}_fb_floor", f, "planks"))
    out.append(box([fx0, fy, zi0], [fx0 + 0.6, fy + 2.0, zi1], f"{f}_fb_side1", f, "planks"))
    out.append(box([fx1 - 0.6, fy + 0.4, zi0 + 0.6], [fx1, fy + 2.0, zi1 - 0.6], f"{f}_fb_side2", f, "planks"))
    out.append(box([fx0, fy, zi0 - 0.6], [fx1, fy + 2.0, zi0], f"{f}_fb_end1", f, "planks"))
    out.append(box([fx0, fy, zi1], [fx1, fy + 2.0, zi1 + 0.6], f"{f}_fb_end2", f, "planks"))
    for j, (zz0, zz1) in enumerate(((VN_RAIL_Z[0][0] + 0.3, zi0 - 0.6), (zi1 + 0.6, VN_RAIL_Z[1][1] - 0.3)), 1):
        out.append(box([fx0 + 1.4, fy, zz0], [fx0 + 2.8, fy + 0.6, zz1], f"{f}_fb_bracket{j}", f, "iron"))
        out.append(box([fx0 + 1.6, VN_RAIL_Y[1], (VN_RAIL_Z[j - 1][0] + VN_RAIL_Z[j - 1][1]) / 2 - 0.6],
                       [fx0 + 2.6, fy, (VN_RAIL_Z[j - 1][0] + VN_RAIL_Z[j - 1][1]) / 2 + 0.6], f"{f}_fb_post{j}", f, "iron"))
    # the water distributor: a perforated pipe across the belt over the head, with an open inlet box north of the belt
    dy = dist_y()
    out.append(box([DIST_X[0], dy - 1.0, BELT_Z[0] - 0.4], [DIST_X[1], dy + 1.0, BELT_Z[1] + 0.4], f"{f}_dist_pipe", f, "pipe"))
    out.append(box([DIST_X[0] - 0.6, dy - 1.0, VN_RAIL_Z[0][0] + 0.2], [DIST_X[1] + 0.6, dy + 2.5, BELT_Z[0] - 0.4], f"{f}_dist_box", f, "pipe"))
    out.append(box([DIST_X[0] + 0.4, VN_RAIL_Y[1], BELT_Z[1] + 0.4], [DIST_X[1] - 0.4, dy + 1.0, VN_RAIL_Z[1][1] - 0.3], f"{f}_dist_post", f, "iron"))
    wy, wz = WORM_C
    for i, x in enumerate(WORM_BEARINGS, 1):
        out.append(box([x - 1.0, wy - 1.2, wz - 1.2], [x + 1.0, wy + 1.2, VN_RAIL_Z[0][0] + 0.4], f"{f}_wormbearing{i}", f, "iron"))
        out.append(box([x - 0.8, VN_RAIL_Y[0], VN_RAIL_Z[0][0] - 0.6], [x + 0.8, wy - 1.2, VN_RAIL_Z[0][0] + 0.4], f"{f}_wormbracket{i}", f, "iron"))
    wc = (0.0, wy, wz)
    out += disc("x", wc, WORM_X[0], WORM_X[1], 0.45, "vnworm_rod", "vnworm", "steel")
    out += pulley("x", wc, *SMALL_X, SMALL_R, "vnworm_pulley", "vnworm", hub_r=1.0)
    for i, x in enumerate(WORM_BEARINGS, 1):
        for j, xc in enumerate((x - 1.55, x + 1.05), 1):
            out += disc("x", wc, xc, xc + 0.5, 0.9, f"vnworm_collar{i}{j}", "vnworm", "steel")
    out += disc("x", wc, HEAD[0] - 2.8, HEAD[0] + 2.8, WORM_R - 0.4, "vnworm_core", "vnworm", "steel", k=4)
    lead = math.pi * WHEEL_MOD                   # a single-start worm: its lead is the wheel's circular pitch
    for i in range(4):
        x0 = HEAD[0] - 2.4 + i * lead * 1.0
        el = disc("x", wc, x0, x0 + 0.5, WORM_R + WHEEL_MOD, f"vnworm_thread{i + 1}", "vnworm", "steel", k=4)
        rotate(el, "z", -8.0, (x0 + 0.25, wy, wz))
        out += el
    hc = (HEAD[0], HEAD[1], 0.0)
    out += disc("z", hc, BELT_Z[0] - 0.4, BELT_Z[1] + 0.4, ROLL_R, "vnhead_drum", "vnhead", "oak", k=6)
    out += disc("z", hc, WHEEL_Z[0] + 0.1, VN_RAIL_Z[1][1], 0.6, "vnhead_journal", "vnhead", "steel")
    out += gear("z", hc, *WHEEL_Z, WHEEL_R, WORM_N, WHEEL_MOD, "vnhead_wheel", "vnhead", "steel", body_k=6)
    dc = (DEFLECT[0], DEFLECT[1], 0.0)
    out += disc("z", dc, BELT_Z[0] - 0.3, BELT_Z[1] + 0.3, DEFLECT_R - 0.02, "vndeflect_roll", "vndeflect", "steel")
    out += disc("z", dc, VN_RAIL_Z[0][0] + 0.5, VN_RAIL_Z[1][1] - 0.5, 0.35, "vndeflect_journal", "vndeflect", "steel", k=2)
    fc = (FOOT[0], FOOT[1], 0.0)
    out += disc("z", fc, BELT_Z[0] - 0.4, BELT_Z[1] + 0.4, ROLL_R, "vnfoot_drum", "vnfoot", "oak", k=6)
    out += disc("z", fc, VN_RAIL_Z[0][0] + 0.3, VN_RAIL_Z[1][1] - 0.3, 0.6, "vnfoot_journal", "vnfoot", "steel")
    for i, x in enumerate(CARRY, 1):
        cc = (x, run_y(x) - CARRY_R, 0.0)
        out += disc("z", cc, BELT_Z[0] - 0.2, BELT_Z[1] + 0.2, CARRY_R - 0.02, f"vncarry{i}_roll", f"vncarry{i}", "steel")
        out += disc("z", cc, VN_RAIL_Z[0][0] + 0.4, VN_RAIL_Z[1][1] - 0.4, 0.35, f"vncarry{i}_journal", f"vncarry{i}", "steel", k=2)
    return out


def build_vanner():
    """The fixed parts: slide posts with plates under the rails; the crank shaft's pedestals; the wash
    tank under the head roller, its spout over the concentrate launder; the tails box beside the belt's
    foot, its floor falling north to a spout over the tailings launder; the middlings chute from the
    table's middlings spout to the feed box; the vanner's water pipe from its cock to over the
    distributor's box, on a post; the drive belt and the small belt to the worm shaft."""
    p = "vanner"
    out = []
    for i, x in enumerate(VN_SLIDES, 1):
        for j, (rz0, rz1) in enumerate(VN_RAIL_Z, 1):
            out.append(box([x - 1.0, 0.0, rz0], [x + 1.0, VN_RAIL_Y[0] - 0.62, rz1], f"vn_slidepost{i}{j}", p, "oak"))
            out.append(box([x - 1.2, VN_RAIL_Y[0] - 0.62, rz0 - 1.2], [x + 1.2, VN_RAIL_Y[0] - 0.02, rz1 + 1.2], f"vn_slide{i}{j}", p, "iron"))
    y, z = VN_C
    for i, (x0, x1) in enumerate(VN_PEDESTALS, 1):
        out.append(box([x0, 0.0, z - 1.0], [x1, y - 1.3, z + 1.0], f"vn_pedestal{i}", p, "oak"))
        out.append(box([x0, y - 1.3, z - 1.3], [x1, y + 1.3, z + 1.3], f"vn_bearing{i}", p, "iron"))
    tx0, tx1 = TANK_X
    tz0, tz1 = TANK_Z
    out.append(box([tx0, 0.0, tz0], [tx1, 0.8, tz1], "vn_tank_floor", p, "planks"))
    out.append(box([tx0, 0.8, tz0], [tx1, TANK_Y, tz0 + 0.8], "vn_tank_n", p, "planks"))
    out.append(box([tx0, 0.8, tz1 - 0.8], [tx1, TANK_Y, tz1], "vn_tank_s", p, "planks"))
    out.append(box([tx0, 0.8, tz0 + 0.8], [tx0 + 0.8, TANK_Y, tz1 - 0.8], "vn_tank_w", p, "planks"))
    sy = TANK_Y - 0.3
    out.append(box([tx1 - 0.8, 0.8, tz0 + 0.8], [tx1, sy - 0.6, tz1 - 0.8], "vn_tank_e", p, "planks"))
    out.append(box([tx1 - 0.8, sy - 0.6, tz0 + 0.8], [tx1, TANK_Y, 62.0], "vn_tank_e1", p, "planks"))
    out.append(box([tx1 - 0.8, sy - 0.6, 67.0], [tx1, TANK_Y, tz1 - 0.8], "vn_tank_e2", p, "planks"))
    out += trough("x", tx1 - 0.8, CL_X[0] + 3.0, sy, sy - 0.4, 61.4, 67.6, 1.0, "vn_tankspout", p, ends=(False, False), t=0.6)
    out += trough("z", TAILS_Z[1], TAILS_Z[0], TAILS_Y[1], TAILS_Y[0], TAILS_X[0], TAILS_X[1], 2.0, "vn_tails", p, ends=(True, False), t=0.6)
    for i, zc in enumerate((58.0, 71.0), 1):
        yl = under_slope(TAILS_Y[1], TAILS_Y[0], TAILS_Z[1], TAILS_Z[0], zc - 0.8, zc + 0.8, 0.6)
        out.append(box([TAILS_X[0] + 0.8, 0.0, zc - 0.8], [TAILS_X[1] - 0.8, yl, zc + 0.8], f"vn_tails_leg{i}", p, "oak"))
    # the middlings chute: under the table's middlings spout, over the tailings launder, to the feed box
    mouth = MB_TOP - 3.4 - 0.6 - 0.5
    lip = feedbox_y() + 2.0 + 0.9
    out += trough("z", VC_Z[0], VC_Z[1], mouth, lip, VC_X[0], VC_X[1], 2.0, "vn_chute", p, ends=(False, False), t=0.6)
    for i, zc in enumerate((48.3,), 1):
        yl = under_slope(mouth, lip, VC_Z[0], VC_Z[1], zc - 0.7, zc + 0.7, 0.6)
        out.append(box([VC_X[0] + 1.0, 0.0, zc - 0.7], [VC_X[1] - 1.0, yl, zc + 0.7], f"vn_chute_leg{i}", p, "oak"))
    # the water pipe: from the vanner's cock south over the table to its drop, down, and south over the distributor's box
    hy, hz = HEADER
    dz = VPIPE_Z
    out.append(box([COCK2_X - 1.0, COCK2_Y, hz + 2.4], [COCK2_X + 1.0, COCK2_Y + 2.0, dz[1]], "vn_pipe_run", p, "pipe"))
    nz = dist_y() + 2.5 + 1.2
    out.append(box([COCK2_X - 1.0, nz + 2.0, dz[1] - 2.0], [COCK2_X + 1.0, COCK2_Y, dz[1]], "vn_pipe_drop", p, "pipe"))
    out.append(box([COCK2_X - 1.0, nz, dz[1] - 2.0], [COCK2_X + 1.0, nz + 2.0, VN_RAIL_Z[0][0] + 1.4], "vn_pipe_nozzle", p, "pipe"))
    out.append(box([COCK2_X - 1.0, 0.0, dz[0]], [COCK2_X + 1.0, nz + 2.0, dz[1] - 2.0], "vn_pipe_post", p, "oak"))
    out += belt("x", MAIN_C, PULLEY_R, (0.0, *VN_C), PULLEY_R, VN_PULLEY_X[0] + 0.15, VN_PULLEY_X[1] - 0.15, "vn_belt", p)
    out += belt("x", (0.0, *VN_C), SMALL_R, (0.0, *WORM_C), SMALL_R, SMALL_X[0] + 0.12, SMALL_X[1] - 0.12, "vn_smallbelt", p, t=0.25, n_wrap=6)
    return out


def build():
    return (build_drive() + build_longtom_moving() + build_longtom() + build_jig_moving() + build_jig()
            + build_table_moving() + build_table() + build_vanner_moving() + build_vanner() + build_plates() + build_frame())


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


LT_RATIO = PULLEY_R / PULLEY_R               # the rake's crank shaft per main shaft turn (open belt: the same way)
JG_RATIO = PULLEY_R / JG_PULLEY_R            # the jig's crank shaft
TB_RATIO = PULLEY_R / HM_PULLEY_R            # the head motion's input shaft: a stroke a turn of it
VN_RATIO = PULLEY_R / PULLEY_R               # the vanner's crank shaft: a shake a turn of it
WORM_RATIO = VN_RATIO * SMALL_R / SMALL_R    # the worm shaft
HEAD_RATIO = -WORM_RATIO / WORM_N            # the head roller: its top runs east (up the belt) as the worm turns +
FOOT_RATIO = HEAD_RATIO                      # the same radius, the same belt
CARRY_RATIO = HEAD_RATIO * ROLL_R / CARRY_R
DEFLECT_RATIO = -HEAD_RATIO * ROLL_R / DEFLECT_R  # outside the loop: it turns the other way


def rot_x(pid_c, ratio):
    return {"type": "rotate", "axis": "x", "pivot": pt(0.0, *pid_c), "ratio": r6(ratio), "input": "travel"}


def rot_z(c, ratio):
    return {"type": "rotate", "axis": "z", "pivot": pt(c[0], c[1], 0.0), "ratio": r6(ratio), "input": "travel"}


def slide(axis, amplitude, ratio, phase=0.0):
    d = {"type": "slide", "axis": axis, "amplitude": r6(amplitude / B), "ratio": r6(ratio), "input": "travel"}
    if phase:
        d["phase"] = r6(phase)
    return d


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    parts = [
        # the frame's drive: the entry turns with the axle, the rectifier's loose wheels with it, the main
        # shaft one way (+) whichever way the axle turns: B2 carries it when the axle turns +, B1 when -
        {"id": "entry", "match": ["entry_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *ENTRY), "ratio": 1.0}]},
        {"id": "rectb1", "match": ["rectb1_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *MAIN), "ratio": r6(-RECT_A1 / RECT_B1)}]},
        {"id": "idler", "match": ["idler_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *IDLER), "ratio": r6(-RECT_A2 / RECT_I)}]},
        {"id": "rectb2", "match": ["rectb2_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *MAIN), "ratio": r6(RECT_A2 / RECT_B2)}]},
        {"id": "mainshaft", "match": ["mainshaft_*"], "requires": None, "drivers": [rot_x(MAIN, 1.0)]},
        # tier 1: the long tom's rake
        {"id": "ltpulley", "match": ["ltpulley_*"], "requires": "longtom", "ride": "mainshaft", "drivers": []},
        {"id": "ltcrank", "match": ["ltcrank_*"], "requires": "longtom", "drivers": [rot_x(RAKE_C, LT_RATIO)]},
        {"id": "ltrake", "match": ["ltrake_*"], "requires": "longtom", "drivers": [slide("z", RAKE_E, LT_RATIO)]},
        {"id": "longtom", "match": ["lt_*"], "requires": "longtom", "drivers": []},
        # tier 2: the jig's plungers (plunger 1's pin at +z at rest: it rises as the shaft turns +... down first)
        {"id": "jgpulley", "match": ["jgpulley_*"], "requires": "jig", "ride": "mainshaft", "drivers": []},
        {"id": "jgshaft", "match": ["jgshaft_*"], "requires": "jig", "drivers": [rot_x(JG_C, JG_RATIO)]},
        {"id": "jgplunger1", "match": ["jgplunger1_*"], "requires": "jig", "drivers": [slide("y", -JG_E, JG_RATIO)]},
        {"id": "jgplunger2", "match": ["jgplunger2_*"], "requires": "jig", "drivers": [slide("y", JG_E, JG_RATIO)]},
        {"id": "jig", "match": ["jg_*"], "requires": "jig", "drivers": []},
        # tier 3: the table's deck, a quick return x = A (sin t - sin 2t / 2), t the input shaft's angle
        {"id": "tbpulley", "match": ["tbpulley_*"], "requires": "table", "ride": "mainshaft", "drivers": []},
        {"id": "tbinput", "match": ["tbinput_*"], "requires": "table", "drivers": [rot_x(HM_IN, TB_RATIO)]},
        {"id": "tbdeck", "match": ["tbdeck_*"], "requires": "table",
         "drivers": [slide("x", TB_A, TB_RATIO), slide("x", -TB_A / 2, 2 * TB_RATIO)]},
        {"id": "table", "match": ["tb_*"], "requires": "table", "drivers": []},
        # tier 4: the vanner's shake, its worm, its rollers
        {"id": "vnpulley", "match": ["vnpulley_*"], "requires": "vanner", "ride": "mainshaft", "drivers": []},
        {"id": "vncrank", "match": ["vncrank_*"], "requires": "vanner", "drivers": [rot_x(VN_C, VN_RATIO)]},
        {"id": "vnframe", "match": ["vnframe_*"], "requires": "vanner", "drivers": [slide("z", VN_E, VN_RATIO)]},
        {"id": "vnworm", "match": ["vnworm_*"], "requires": "vanner", "ride": "vnframe", "drivers": [rot_x(WORM_C, WORM_RATIO)]},
        {"id": "vnhead", "match": ["vnhead_*"], "requires": "vanner", "ride": "vnframe", "drivers": [rot_z(HEAD, HEAD_RATIO)]},
        {"id": "vnfoot", "match": ["vnfoot_*"], "requires": "vanner", "ride": "vnframe", "drivers": [rot_z(FOOT, FOOT_RATIO)]},
    ]
    parts.append({"id": "vndeflect", "match": ["vndeflect_*"], "requires": "vanner", "ride": "vnframe",
                  "drivers": [rot_z(DEFLECT, DEFLECT_RATIO)]})
    for i, x in enumerate(CARRY, 1):
        parts.append({"id": f"vncarry{i}", "match": [f"vncarry{i}_*"], "requires": "vanner", "ride": "vnframe",
                      "drivers": [rot_z((x, run_y(x) - CARRY_R), CARRY_RATIO)]})
    parts.append({"id": "vanner", "match": ["vn_*"], "requires": "vanner", "drivers": []})
    # the amalgamation plates, fitted from tier 2 whatever the tier, and their mercury dressing
    parts.append({"id": "plates", "match": ["pl_*"], "requires": "plates", "drivers": []})
    parts.append({"id": "dressing", "match": ["pd_*"], "requires": "dressing", "drivers": []})
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []})
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses
REST = (0.0, 0.0)                             # (theta, psi): the authored pose


def inputs_of(pose):
    th, ps = pose[:2]
    return {"theta": th, "travel": ps}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path)


def turn(psi):
    """The pose a main shaft travel of psi brings (the axle turning +)."""
    return (psi, psi)


STATES = {                                    # the scenario's states: what is fitted in each
    "frame": (),
    "tier1": ("longtom",),
    "tier2": ("jig",),
    "tier3": ("table",),
    "tier4": ("table", "vanner"),
    "tier2plates": ("jig", "plates"),                         # the plates bare: new copper
    "tier3plates": ("table", "plates", "dressing"),           # dressed with mercury
    "tier4plates": ("table", "vanner", "plates", "dressing"),
}


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("feedSpout", "waterSpout", "concentrate", "tailings")


def make_rig(parts):
    x, y = WATER_IN
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the wash house's "
                    "north-west corner, at the head (feed) end; the machines run east. One frame, one tier's working parts "
                    "fitted at a time (tiers: what each tier fits, by requires; tier 4 is the table and the vanner). theta "
                    "is the axle (the entry shaft, the rectifier); everything after the rectifier reads the travel psi, so "
                    "it runs one way whichever way the axle turns. See the concentrator's README for the schema.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "waterCell": list(WATER_CELL),
        "waterFace": WATER_FACE,
        "feedCell": list(FEED_CELL),
        "feedFace": FEED_FACE,
        "feedSpout": {"pos": pt(*FEED_LIP)},
        "waterSpout": {"pos": pt(COCK1_X, SPOUT_Y, SPOUT_Z)},
        "concentrate": {"pos": pt(X_LEN, pt_floor(sum(PT_NOTCH_Z) / 2), sum(PT_NOTCH_Z) / 2)},
        "concentrateSide": "east",
        "tailings": {"pos": pt(0.0, TL_Y[0], sum(TL_Z) / 2)},
        "tailingsSide": "west",
        "tiers": [{"tier": n, "name": name, "requires": list(req)} for n, _, req, name in TIERS],
        "plates": {"requires": PLATES[0], "dressing": PLATES[1], "fromTier": PLATES_FROM,
                   "_comment": "The amalgamation plates: one fitted part at any tier from fromTier, not a tier of their own; "
                               "dressing is their mercury coat, drawn over them while they are dressed."},
        "drive": {"strokesPerTurn": {"rake": r6(LT_RATIO), "jig": r6(JG_RATIO), "table": r6(TB_RATIO), "vanner": r6(VN_RATIO)},
                  "beltPerTurn": r6(abs(HEAD_RATIO) * ROLL_R / B * TAU),
                  "_comment": "strokesPerTurn: each tier's working strokes (the rake's combs, the plungers', the deck's, the "
                              "vanner's shakes) per turn of the main shaft, which turns once an axle turn. beltPerTurn: blocks the "
                              "vanner's belt creeps up-slope per main shaft turn (the worm wheel's 16 teeth on a single-start worm)."},
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
    for key in ("powerCell", "waterCell", "feedCell"):
        ship[key] = shift_cell(rig[key], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig, with every
    tier's parts (every reader rebuilds them the same way); cells with nothing in them are hollow; then
    the lids."""
    written = flatten(shape["elements"], textures={})
    rest = [posed(w, _part_matrix(ship_parts, part_of(ship_parts, w.name), inputs_of(REST))) for w in written]
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
    poses = [REST, turn(1.3), (-2.2, 2.2), turn(4.9), (0.7, 9.1)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; {len(cells)} cells")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
def reference_poses():
    """theta in {0, 1.1, -2.3, 2.9, -4.4} with psi |theta| and |theta| plus some travel."""
    out = []
    for th in (0.0, 1.1, -2.3, 2.9, -4.4):
        for extra in (0.0, 3.7, 11.05):
            out.append((th, round(abs(th) + extra, 6)))
    return out


def reference_json(ship_parts):
    poses = []
    for pose in reference_poses():
        th, ps = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose))) for q in ship_parts}
        poses.append({"theta": th, "travel": ps, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped concentrator-rig.json's parts: each part's matrix as 3 rows of 4 "
                        "(block units) at each pose (theta the axle's angle, travel the main shaft's, radians). The site's and the "
                        "mod's tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. The wash house frame (fr_*) and "
             "each tier's parts (lt_ / lt*: long tom; jg_ / jg*: jig; tb_ / tb*: table; vn_ / vn*: vanner). Keep element "
             "names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def state_of(pose):
    return pose[2] if len(pose) > 2 else "all"


def on_show(requires, state):
    """Whether a part needing `requires` shows in a state ("all": every tier at once, as the cells are built)."""
    return requires is None or state == "all" or requires in STATES[state]


def shown(posed_els, pose, req_of):
    """The posed elements as they can be seen in the pose's state: other tiers' parts moved far away
    (copies; the order kept), so the z-fighting fix and its check deal only with faces that show together."""
    state = state_of(pose)
    out = []
    for e in posed_els:
        if not on_show(req_of[e.part], state):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def coplanar_poses():
    out = []
    for state in ("tier4", "tier1", "tier2", "tier3"):
        for psi in (0.0, 0.9, 2.3, 4.0):
            out.append((psi, psi, state))
    out += [(0.0, 0.0, "tier2plates"), (0.9, 0.9, "tier4plates")]
    return tuple(out)


def fix_coplanar(els, parts):
    req_of = {p["id"]: p["requires"] for p in parts}
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose, req_of),
                              coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the concentrator's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_concentrator
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        for pose, pairs in before.items():
            if pairs:
                print(f"coplanar faces before the fix at {pose}: {len(pairs)} pairs")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_concentrator.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "concentrator.json", args.out / "concentrator_frame.json", args.out / "concentrator-rig.json",
                args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "concentrator.json", SHAPE_DIR / "concentrator_frame.json", RIG_DIR / "concentrator-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_concentrator.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship), reference_dumps(reference_json(ship_parts)))
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
