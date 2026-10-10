#!/usr/bin/env python3
"""Generate the roaster's shapes, rig and reference poses.

The roaster roasts sulfide concentrate into roasted concentrate, giving off sulfur (#736). It is one of
the ore line's upgradeable machines (#711): a frame placed at its tier 4 size, then one tier's parts
after another, each tier's working parts replacing the last's. Nothing that connects to the outside
moves between tiers: the infeed, the output chute's exit, the power cell and the stack are where the
frame puts them. The tiers, as period devices (#737):

    tier 2  a stall roaster: a row of three open-fronted masonry stalls against a back wall, the ore
            roasted in heaps on beds of firewood; each stall's back wall has a flue that the collecting
            flue on top of the wall carries to the frame's flue chamber and stack. A wooden bin on a
            trestle at the cold end is its hopper; the roasted ore is raked into the frame's discharge
            pit in front of the stalls, where it collects.
    tier 3  a reverberatory roaster: a long low brick furnace under a firebrick arch, held by iron
            buckstays and tie rods; a firebox (grate, ash pit, fire door) at the near end, the flame
            drawn over the bridge wall, along the hearth and out through the flue chamber's throat
            into the stack. Two mechanical rabbles (vertical spindles through the arch, as in the
            Edwards furnace of the 1890s) plough the charge along the hearth towards the discharge
            hole by the bridge, which drops into the frame's pit; they are turned by crown wheels from
            a line shaft on the roof, which continues the vanilla axle at the power cell. The charge is
            tipped by hand into a charging box over a hole in the arch at the cold end.
    tier 4  the same furnace, chute-fed: the charging box gives way to a hopper that the previous
            machine's chute fills, over a reciprocating plate feeder in a feed box (a Scotch yoke on a
            crank at the line shaft's end shuttles the plate across under the hopper's throat), which
            meters the charge down a feed chute through the arch.

Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    roaster.json           the whole machine, every tier's parts      (assets/.../shapes/block/)
    roaster_frame.json     the static frame only (block and item)     (assets/.../shapes/block/)
    roaster-rig.json       cells, anchors, the tiers and the part rig (assets/.../config/)
    rig-reference.json     every part's matrix at a grid of poses     (tests/Roaster/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_roaster.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from
the machine box's north-west-bottom corner (the "build frame"); the controller cell, the middle of the
near (fire) end at ground level, is the build frame's cell (1, 0, 0), so the shipped files are the
build frame moved one block west and divided by 16. The roaster runs along z: the fire end (north,
the controller) to the stack (south).

The rig's input, as this machine uses it (README "Rig"):

    theta  the axle angle: the line shaft and, through the crown wheels, the rabbles; the feeder's crank
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
REFERENCE_OUT = MOD / "tests" / "Roaster" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/Roaster/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

# ---------------------------------------------------------------- the box, the cells, the anchors
CELLS_X, CELLS_Y, CELLS_Z = 3, 5, 6          # the machine box (blocks); the footprint is the cells below, not all of it
BODY_Y = 2                                   # the body: every cell x 0..2, y 0..1, z 0..5
ORIGIN_CELL = (1, 0, 0)                      # the controller: the middle of the fire end, nearest the player who placed it
POWER_CELL, POWER_FACE = (1, 2, 0), "north"  # the vanilla axle comes in along z over the fire end, at the cell's centre (24, 40)
INFEED_CELL, INFEED_FACE = (1, 2, 4), "up"   # every tier's hopper: its mouth is this cell's top, filled by hand or by a chute
OUTPUT_CELL, OUTPUT_FACE = (2, 0, 2), "east" # the frame's discharge spout: the output chute's end, every tier
STACK_CELLS = ((1, 2, 5), (1, 3, 5), (1, 4, 5))   # the stack above the flue chamber
DRIVE_CELLS = tuple((1, 2, z) for z in range(5))  # the line shaft, the crown wheels and the hopper over the roof's middle

TEXTURES = {
    "stone": "game:block/stone/brick/granite1",              # the plinth, the stack's cap: dressed granite
    "rubble": "game:block/stone/drystone/granite1",          # the stalls' walls: rubble masonry
    "brick": "game:block/clay/brick/four/running/red1",      # the furnace's and the flue chamber's walls, the stack, the stalls' flue
    "firebrick": "game:block/clay/brick/four/running/fire1", # the hearth, the bridge wall, the arch, the skewbacks
    "iron": "game:block/metal/plate/iron",                   # buckstays, tie rods, doors, grate, shafts, gears, hoppers
    "oak": "game:block/wood/debarked/oak",                   # the tier 2 bin and trestle, the line shaft's entry (the vanilla axle's)
    "ore": "game:block/pile/oldore",                         # the ore heaped in the stalls
    "firewood": "game:block/wood/firepit/log",               # the stall heaps' beds of firewood
    "fire": "game:block/coal/charcoal",                      # the fire on the grate: a renderer may set it to game:block/coal/ember while firing
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the frame
PLINTH = 5.0                                 # the stone plinth under everything (y 0..5); every tier stands on it
PIT = ((32.0, 40.0), (30.0, 38.0))           # (x, z) of the discharge pit sunk in the plinth (y 1..5), by the bridge
CHANNEL = ((40.0, 48.0), (32.0, 38.0), (1.0, 4.0))   # (x, z, y): the tunnel from the pit to the spout on the east face
SPOUT_X = (46.5, 48.0)                       # the spout: an iron lining in the tunnel's mouth, flush with the plinth's face
SPOUT_T = 0.5
FLUEBOX = ((2.0, 46.0), (PLINTH, 32.0), (80.0, 96.0))   # the flue chamber at the cold end (x, y, z)
FB_T = 3.0                                   # its walls' thickness
INLET = ((18.0, 30.0), (12.0, 24.0))         # (x, y): the throat in its north wall, the hearth's flue
STACK_XZ = ((17.0, 31.0), (81.0, 95.0))      # the stack's brickwork on the chamber's roof
STACK_TOP = 80.0                             # five blocks up
STACK_FLUE = ((21.0, 27.0), (85.0, 91.0))
CAP_Y = (76.0, 80.0)                         # the stone cap
BANDS_Y = ((50.0, 51.0), (66.0, 67.0))       # iron bands round the stack
BAND_T = 0.4
SULFUR_DOOR = ((84.0, 92.0), (7.0, 14.0))    # (z, y) of the clean-out door on the chamber's east face: sulfur condenses in the chamber

# ---------------------------------------------------------------- tier 2: the stall roaster
ST_BACK = (2.0, 12.0)                        # x: the back wall (west), as thick as the collecting flue on it
ST_FRONT = 30.0                              # x: the stalls' open fronts (the forecourt and the pit lie east of them)
ST_TOP = 24.0                                # y: the walls' tops
ST_WALLS = ((2.0, 6.0), (22.0, 26.0), (42.0, 46.0), (62.0, 66.0))   # z: the end and partition walls
STALLS = ((6.0, 22.0), (26.0, 42.0), (46.0, 62.0))                  # z: the three stalls
ST_BACK_Z = (2.0, 74.0)                      # the back wall runs on to the header under the collecting flue
FLUE_MOUTH = (2.0, 5.0)                      # a stall's flue mouth at the foot of its back wall: half width (z), height above the floor
FLUE_MOUTH_D = 2.0                           # how deep it is cut into the wall
CFLUE_Y = (ST_TOP, 32.0)                     # the collecting flue on the back wall, x ST_BACK, z 2..74
HEADER = ((2.0, 32.0), (PLINTH, 32.0), (74.0, 80.0))   # the header that carries it into the chamber's throat
BIN = ((17.0, 31.0), (38.0, 48.0), (66.0, 79.0))       # the stall roaster's wooden bin (its hopper): mouth at y 48
BIN_T = 1.0
LEGS_X = ((17.2, 19.2), (28.8, 30.8))
LEGS_Z = ((66.2, 68.2), (76.8, 78.8))        # the north legs stand on the plinth, the south posts on the header
BIN_HOLE = ((22.0, 26.0), (67.0, 70.0))      # (x, z) the hole in the bin's floor over the trough
TROUGH = ((24.0, 38.2, 69.0), (24.0, 27.5, 56.0))   # the trough's top and bottom ends (its bottom's middle line)
LOG_H = 2.5                                  # the firewood under a heap

# ---------------------------------------------------------------- tier 3: the reverberatory furnace
WALL_X = ((2.0, 8.0), (40.0, 46.0))          # the side walls
IN_X = (8.0, 40.0)                           # the firebox and the hearth inside them
END_N = (1.0, 6.0)                           # the fire end's wall (the doors are on its face, z 0..1)
FIREBOX_Z = (6.0, 22.0)
BRIDGE_Z = (22.0, 27.0)
HEARTH_Z = (27.0, 80.0)                      # to the flue chamber's face
HEARTH_Y = 12.0                              # the hearth's floor
BRIDGE_TOP = 17.0
SPRING_Y = 21.0                              # the side walls' tops: the arch springs here
RISE, ARCH_T, VOUSSOIRS = 5.0, 4.0, 7        # the segmental arch over IN_X: its rise, thickness and strips
ARCH_Z = (END_N[0], HEARTH_Z[1])
SKEW_X = ((3.0, 9.0), (39.0, 45.0))          # the firebrick skewbacks on the walls' tops, the arch's springing
SKEW_Y = (SPRING_Y, 25.0)
SKEWBAR_X = ((2.0, 3.0), (45.0, 46.0))       # the iron skewback bars outside them
STAY_X = ((0.6, 2.0), (46.0, 47.4))          # buckstays, outside the walls
NUT_X = ((0.0, 0.6), (47.4, 48.0))
STAY_Z = (2.0, 14.0, 25.0, 44.0, 51.0, 64.0) # buckstay pairs (centres): clear of the doors, the pedestals and the charging seat
STAY_W = 2.0
STAY_TOP = 32.0
ROD_Y = (30.3, 31.1)                         # the tie rods over the arch's crown (30)
ROD_T = 0.8
DISCHARGE = ((33.0, 39.0), (31.0, 37.0))     # (x, z) the discharge hole through the hearth into the pit, by the bridge
GRATE_Y = (11.0, 12.0)
BEARER_Y = (9.0, 11.0)
BEARERS_Z = ((6.0, 7.5), (20.5, 22.0))
GRATE_BARS = 15                              # 1 wide, 1 apart, x 9..38
FIRE_DOOR = ((18.0, 30.0), (13.0, 21.0))     # (x, y) the stoking door's opening, on the fire end's face
ASH_DOOR = ((18.0, 30.0), (6.0, 10.0))
DOOR_FRAME = 1.0
WORK_DOOR = (3.0, (12.5, 17.5))              # half width (z) about each rabble, and y: the side doors
SEAT = ((19.5, 28.5), (72.0, 79.0), (30.0, 30.8))   # the charging hole's iron seat on the arch (x, z, y)
SEAT_HOLE = ((21.3, 26.7), (73.3, 77.7))
CHARGE_HOLE_Z = (73.2, 77.8)                 # the hole through the arch's crown strip
THROAT = ((21.5, 26.5), (73.5, 77.5))        # (x, z) the charging box's spout and the feed chute, through the hole
THROAT_T = 0.5
THROAT_Y0 = 26.5                             # their foot, just inside the arch
FLANGE = ((20.5, 27.5), (72.5, 78.5), (30.8, 31.3))   # their flange, resting on the seat

# ---------------------------------------------------------------- tier 3: the drive and the rabbles
SHAFT = (24.0, 40.0)                         # (x, y) of the line shaft along z: the power cell's centre
SHAFT_R = 0.9
ENTRY_Z = (0.0, 4.0)                         # the oak entry, the vanilla axle's cross profile, from the power face
SHAFT_Z = (3.0, 69.6)                        # the iron shaft, on into the feeder's crank (tier 4)
PEDESTALS = ((4.5, 7.5), (27.0, 30.0), (46.0, 49.0), (65.5, 68.5))   # z: the line shaft's pedestals on the arch
PED_BASE_X = (21.5, 26.5)
PED_COL_X = (22.8, 25.2)
PED_BEARING = 2.3                            # the bearing block's half side round the shaft
MODULE = 0.5
NP, NC = 8, 24                               # the pinions on the line shaft, the crown wheels on the spindles
RP, RC = MODULE * NP / 2, MODULE * NC / 2    # 2, 6
PINION_W = 1.0                               # the pinion as thick as the crown teeth are long in its plane: the box teeth clear
TOOTH_W = 0.4                                # a tooth's width (both gears): cogs, well under the gap, so the box teeth clear
RATIO = -NP / NC                             # spindle turns per shaft turn (a crown wheel driven from its north rim turns back)
RABBLE_Z = (38.5, 58.5)                      # the two spindles, on the hearth's middle line
RABBLE_R = 9.5                               # the arms' reach
CROWN_PITCH_Y = SHAFT[1] - RP                # 38: where the pinion's pitch circle meets the crown's teeth
CROWN_TEETH_Y = (37.3, CROWN_PITCH_Y + 0.4)      # 37.3..38.4: the box teeth engage a little short of a full addendum
CROWN_WEB_Y = (35.6, 37.4)
CROWN_HUB = (34.0, 35.6, 1.8)                # y0, y1, radius: the hub rests on the spindle's bearing
SPINDLE_R = 0.75
SPINDLE_Y = (12.8, 37.3)
RABBLE_HUB = (12.6, 15.2, 1.6)
ARM_Y = (13.4, 14.6)
ARM_W = 1.0
BLADES_R = (3.5, 6.0, 8.5)                   # each arm's three ploughs
BLADE = (2.4, 0.35, (12.3, 13.6))            # length, thickness, y
BLADE_SET = 40.0                             # degrees the ploughs are set across their arm
GLAND_Z = 1.6                                # half length of a spindle's gland, filling its hole in the crown strip
BRACKET = ((21.5, 26.5), 3.0, (30.0, 30.5), 2.0, (30.5, 34.0))   # base plate x, half length z, y; bearing half side, y

# ---------------------------------------------------------------- tier 4: the hopper and the feeder
CRANK_Z = (69.0, 70.2)
CRANK_R = 3.2
PIN_R, PIN_AT = 0.5, 2.2                     # the crank pin: its radius and its throw (straight up at theta 0)
PIN_Z = (70.2, 71.8)
YOKE_Z = (70.35, 71.65)
YOKE_SLOT = 0.62                             # the slot's half width: the pin rides in it
YOKE_CHEEK = 0.68
YOKE_Y = (36.0, 43.4)
ROD = ((23.4, 24.6), (35.0, 36.0), (71.65, 75.6))   # the push rod (x, y, z) from the yoke's foot into the feed box
DROPPER = ((23.4, 24.6), (33.5, 35.0), (74.8, 75.6))
PLATE = ((21.6, 26.4), (33.0, 33.5), (73.8, 78.2))  # the feed plate, shuttled across under the throat
FEEDBOX = ((18.6, 29.4), (FLANGE[2][0], 37.0), (72.6, 79.0))
FEEDBOX_T = 0.6
SLOT = ((20.8, 27.2), (34.8, 36.2))          # (x, y) the slot in the feed box's north wall for the push rod
RUNNERS = ((73.6, 74.2), (77.8, 78.4), (32.4, 33.0))   # z, z, y: the plate's runners
HOPPER = ((16.5, 31.5), (40.0, 48.0), (72.5, 80.0))    # the chute-fed hopper (x, y, z): mouth at y 48
HOPPER_T = 0.5
HOPPER_THROAT = ((20.5, 27.5), (73.5, 78.5), (37.0, 40.0))
CHARGER = ((18.0, 30.0), (36.0, 48.0), (72.5, 79.5))   # tier 3's charging box (x, y, z): mouth at y 48
GATE = ((21.7, 29.5), (32.6, 33.0), (73.7, 77.3))      # its slide gate, pushed in, the handle east

# ---------------------------------------------------------------- the tiers (the scenario's states and the gameplay's sets)
TIERS = {
    "2": ["stalls", "stallflue", "stallbin", "heaps"],
    "3": ["hearth", "walls", "arch", "ironwork", "fire", "drive", "rabbles", "charger"],
    "4": ["hearth", "walls", "arch", "ironwork", "fire", "drive", "rabbles", "hopper", "feeder"],
}
STATES = {"frame": [], "tier2": TIERS["2"], "tier3": TIERS["3"], "tier4": TIERS["4"]}


# ---------------------------------------------------------------- box helpers
FACE_UV = {"north": (0, 1), "south": (0, 1), "east": (2, 1), "west": (2, 1), "up": (0, 2), "down": (0, 2)}


def skin(el, tex):
    """Every face takes `tex`, its UVs a region of the texture in proportion to the face's size."""
    el.faces = {}
    for d, (u, v) in FACE_UV.items():
        w = min(abs(el.size[u]) * TEX / 16, TEX)
        h = min(abs(el.size[v]) * TEX / 16, TEX)
        el.faces[d] = {"texture": "#" + tex, "uv": [0.0, 0.0, w, h]}
    return el


def skin_world(el, tex, lo, hi):
    """An axis-aligned box's faces with UVs at its place in the block grid, so masonry runs on unbroken
    across the boxes it is built of (the box must not cross a block boundary on a face's axes)."""
    el.faces = {}
    for d, (u, v) in FACE_UV.items():
        u0 = (lo[u] % B) * TEX / B
        if v == 1:
            v0 = ((-hi[1]) % B) * TEX / B
        else:
            v0 = (lo[2] % B) * TEX / B
        el.faces[d] = {"texture": "#" + tex, "uv": [r3(u0), r3(v0), r3(u0 + (hi[u] - lo[u]) * TEX / B), r3(v0 + (hi[v] - lo[v]) * TEX / B)]}
    return el


def r3(v):
    return round(v + 0.0, 3) + 0.0


def box(lo, hi, name, part, tex):
    c = [(lo[k] + hi[k]) / 2 for k in range(3)]
    return skin(El(name, [hi[k] - lo[k] for k in range(3)], c, [r[:] for r in IDENT], {}, part), tex)


def splits(a, b):
    """a..b cut at every block boundary inside it."""
    cuts = [a] + [B * k for k in range(int(math.floor(a / B)) + 1, int(math.ceil(b / B))) if a + 1e-9 < B * k < b - 1e-9] + [b]
    return list(zip(cuts, cuts[1:]))


def wall(lo, hi, name, part, tex):
    """An axis-aligned box cut at the block grid, each piece's UVs at its place in the grid (masonry runs on
    across the pieces; no face is stretched past one texture). An empty box gives nothing."""
    if any(hi[k] - lo[k] < 1e-9 for k in range(3)):
        return []
    pieces = [(x, y, z) for x in splits(lo[0], hi[0]) for y in splits(lo[1], hi[1]) for z in splits(lo[2], hi[2])]
    out = []
    for i, (xs, ys, zs) in enumerate(pieces):
        a, b = [xs[0], ys[0], zs[0]], [xs[1], ys[1], zs[1]]
        c = [(a[k] + b[k]) / 2 for k in range(3)]
        el = El(f"{name}_{i + 1}" if len(pieces) > 1 else name, [b[k] - a[k] for k in range(3)], c, [r[:] for r in IDENT], {}, part)
        out.append(skin_world(el, tex, a, b))
    return out


AX = {"x": 0, "y": 1, "z": 2}
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}   # a disc's angle 0 direction, and the in-plane direction it turns towards


def radial(axis, c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box from radius r0 to r1 about `axis` through c, a0..a1 along the axis, `width` across, at angle
    `ang` (radians, right-handed about +axis from the axis's reference direction: +z about y, +y about z)."""
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


def frame_bars(lo, hi, hole_lo, hole_hi, axis, name, part, tex):
    """A rectangular frame: the box lo..hi less the opening hole_lo..hole_hi across `axis` (four bars)."""
    a = AX[axis]
    u, v = [k for k in range(3) if k != a]
    out = []
    specs = [((lo[u], lo[v]), (hi[u], hole_lo[v])),               # below the opening, full width
             ((lo[u], hole_hi[v]), (hi[u], hi[v])),               # above it
             ((lo[u], hole_lo[v]), (hole_lo[u], hole_hi[v])),     # beside it
             ((hole_hi[u], hole_lo[v]), (hi[u], hole_hi[v]))]
    for i, ((u0, v0), (u1, v1)) in enumerate(specs, 1):
        if u1 - u0 < 1e-9 or v1 - v0 < 1e-9:
            continue
        p, q = list(lo), list(hi)
        p[u], q[u], p[v], q[v] = u0, u1, v0, v1
        out.append(box(p, q, f"{name}{i}", part, tex))
    return out


def tube(lo, hi, t, axis, name, part, tex):
    """A square tube along `axis`, lo..hi outside, walls t thick: four plates, the pair across u running the
    whole width so the corners close."""
    a = AX[axis]
    u, v = [k for k in range(3) if k != a]
    out = []
    for i, (p0, p1) in enumerate((((lo[u], lo[v]), (hi[u], lo[v] + t)), ((lo[u], hi[v] - t), (hi[u], hi[v])),
                                  ((lo[u], lo[v] + t), (lo[u] + t, hi[v] - t)), ((hi[u] - t, lo[v] + t), (hi[u], hi[v] - t))), 1):
        p, q = list(lo), list(hi)
        p[u], p[v] = p0
        q[u], q[v] = p1
        out.append(box(p, q, f"{name}{i}", part, tex))
    return out


def open_box(lo, hi, t, name, part, tex, floor=True, hole=None):
    """An open-topped box (a bin or a hopper): four walls and, given `floor`, a floor, with `hole` ((x0, x1),
    (z0, z1)) cut out of the floor."""
    (x0, y0, z0), (x1, y1, z1) = lo, hi
    out = [box([x0, y0, z0], [x1, y1, z0 + t], f"{name}_n", part, tex),
           box([x0, y0, z1 - t], [x1, y1, z1], f"{name}_s", part, tex),
           box([x0, y0, z0 + t], [x0 + t, y1, z1 - t], f"{name}_w", part, tex),
           box([x1 - t, y0, z0 + t], [x1, y1, z1 - t], f"{name}_e", part, tex)]
    if floor:
        f0, f1 = [x0 + t, y0, z0 + t], [x1 - t, y0 + t, z1 - t]
        if hole is None:
            out.append(box(f0, f1, f"{name}_floor", part, tex))
        else:
            out += frame_bars(f0, f1, [hole[0][0], y0, hole[1][0]], [hole[0][1], y0 + t, hole[1][1]], "y", f"{name}_floor", part, tex)
    return out


# ---------------------------------------------------------------- the arch
R_IN = ((IN_X[1] - IN_X[0]) ** 2 / 4 + RISE ** 2) / (2 * RISE)   # the intrados' radius (28.1)
R_OUT = R_IN + ARCH_T
ARCH_C = ((IN_X[0] + IN_X[1]) / 2, SPRING_Y + RISE - R_IN)      # (x, y) the arch's centre, under the plinth
ALPHA = math.asin((IN_X[1] - IN_X[0]) / 2 / R_IN)               # its half angle
DELTA = 2 * ALPHA / VOUSSOIRS
STRIP_W = 2 * R_OUT * math.tan(DELTA / 2)                       # a strip's width: the strips meet at the extrados
CROWN_X = (ARCH_C[0] - STRIP_W / 2, ARCH_C[0] + STRIP_W / 2)     # the crown strip, axis-aligned
CROWN_Y = (ARCH_C[1] + R_IN, ARCH_C[1] + R_OUT)                  # 26..30


def intrados(x):
    return ARCH_C[1] + math.sqrt(max(0.0, R_IN ** 2 - (x - ARCH_C[0]) ** 2))


def crown_holes():
    """The holes through the crown strip: a gland round each spindle, the charging hole at the cold end."""
    return [(z - GLAND_Z, z + GLAND_Z) for z in RABBLE_Z] + [CHARGE_HOLE_Z]


def runs(a, b, holes):
    """a..b less the holes, cut at the block grid."""
    out, at = [], a
    for h0, h1 in sorted(holes):
        if h0 > at:
            out.append((at, h0))
        at = max(at, h1)
    if at < b:
        out.append((at, b))
    return [s for r in out for s in splits(*r)]


# ---------------------------------------------------------------- builders: the frame
def build_frame():
    """The stone plinth (with the discharge pit and the tunnel to the spout), the spout's iron frame, the
    flue chamber with its throat and its sulfur door, and the stack."""
    f = "frame"
    out = []
    (px0, px1), (pz0, pz1) = PIT
    (cx0, cx1), (cz0, cz1), (cy0, cy1) = CHANNEL
    # the base course, the pit's and the tunnel's floor
    out += wall([0.0, 0.0, 0.0], [48.0, 1.0, 96.0], "fr_plinth_base", f, "stone")
    # the courses above it, round the pit and the tunnel
    out += wall([0.0, 1.0, 0.0], [48.0, PLINTH, pz0], "fr_plinth_n", f, "stone")
    out += wall([0.0, 1.0, pz1], [48.0, PLINTH, 96.0], "fr_plinth_s", f, "stone")
    out += wall([0.0, 1.0, pz0], [px0, PLINTH, pz1], "fr_plinth_w", f, "stone")
    out += wall([px1, 1.0, pz0], [48.0, PLINTH, cz0], "fr_plinth_en", f, "stone")
    out += wall([px1, 1.0, cz1], [48.0, PLINTH, pz1], "fr_plinth_es", f, "stone")
    out += wall([px1, cy1, cz0], [48.0, PLINTH, cz1], "fr_plinth_tunnelroof", f, "stone")
    # the spout: an iron lining in the tunnel's mouth, flush with the plinth's face
    out += tube([SPOUT_X[0], cy0, cz0], [SPOUT_X[1], cy1, cz1], SPOUT_T, "x", "fr_spout", f, "iron")
    # the flue chamber: hollow, a throat in its north wall, the stack's flue through its roof
    (fx0, fx1), (fy0, fy1), (fz0, fz1) = FLUEBOX
    (ix0, ix1), (iy0, iy1) = INLET
    t = FB_T
    out += wall([fx0, fy0, fz0], [ix0, fy1, fz0 + t], "fr_chamber_nw", f, "brick")
    out += wall([ix1, fy0, fz0], [fx1, fy1, fz0 + t], "fr_chamber_ne", f, "brick")
    out += wall([ix0, fy0, fz0], [ix1, iy0, fz0 + t], "fr_chamber_nsill", f, "brick")
    out += wall([ix0, iy1, fz0], [ix1, fy1, fz0 + t], "fr_chamber_nhead", f, "brick")
    out += wall([fx0, fy0, fz1 - t], [fx1, fy1 - t, fz1], "fr_chamber_s", f, "brick")
    out += wall([fx0, fy0, fz0 + t], [fx0 + t, fy1 - t, fz1 - t], "fr_chamber_w", f, "brick")
    out += wall([fx1 - t, fy0, fz0 + t], [fx1, fy1 - t, fz1 - t], "fr_chamber_e", f, "brick")
    (sx0, sx1), (sz0, sz1) = STACK_FLUE
    ry = (fy1 - t, fy1)
    out += wall([fx0, ry[0], fz0 + t], [sx0, ry[1], fz1], "fr_chamber_roofw", f, "brick")
    out += wall([sx1, ry[0], fz0 + t], [fx1, ry[1], fz1], "fr_chamber_roofe", f, "brick")
    out += wall([sx0, ry[0], fz0 + t], [sx1, ry[1], sz0], "fr_chamber_roofn", f, "brick")
    out += wall([sx0, ry[0], sz1], [sx1, ry[1], fz1], "fr_chamber_roofs", f, "brick")
    # the sulfur door on its east face
    (dz0, dz1), (dy0, dy1) = SULFUR_DOOR
    out += frame_bars([fx1, dy0, dz0], [fx1 + 0.6, dy1, dz1], [fx1, dy0 + 1.0, dz0 + 1.0], [fx1 + 0.6, dy1 - 1.0, dz1 - 1.0],
                      "x", "fr_sulfurdoor_frame", f, "iron")
    out.append(box([fx1 + 0.1, dy0 + 1.0, dz0 + 1.0], [fx1 + 0.5, dy1 - 1.0, dz1 - 1.0], "fr_sulfurdoor_plate", f, "iron"))
    out.append(box([fx1 + 0.5, (dy0 + dy1) / 2 - 0.4, dz1 - 3.0], [fx1 + 0.9, (dy0 + dy1) / 2 + 0.4, dz1 - 1.6], "fr_sulfurdoor_handle", f, "iron"))
    # the stack: four walls round its flue, a stone cap, iron bands
    (kx0, kx1), (kz0, kz1) = STACK_XZ
    y0 = fy1
    out += wall([kx0, y0, kz0], [sx0, CAP_Y[0], kz1], "fr_stack_w", f, "brick")
    out += wall([sx1, y0, kz0], [kx1, CAP_Y[0], kz1], "fr_stack_e", f, "brick")
    out += wall([sx0, y0, kz0], [sx1, CAP_Y[0], sz0], "fr_stack_n", f, "brick")
    out += wall([sx0, y0, sz1], [sx1, CAP_Y[0], kz1], "fr_stack_s", f, "brick")
    out += wall([16.0, CAP_Y[0], 80.0], [sx0, CAP_Y[1], 96.0], "fr_stackcap_w", f, "stone")
    out += wall([sx1, CAP_Y[0], 80.0], [32.0, CAP_Y[1], 96.0], "fr_stackcap_e", f, "stone")
    out += wall([sx0, CAP_Y[0], 80.0], [sx1, CAP_Y[1], sz0], "fr_stackcap_n", f, "stone")
    out += wall([sx0, CAP_Y[0], sz1], [sx1, CAP_Y[1], 96.0], "fr_stackcap_s", f, "stone")
    for i, (b0, b1) in enumerate(BANDS_Y, 1):
        out.append(box([kx0 - BAND_T, b0, kz0 - BAND_T], [kx0, b1, kz1 + BAND_T], f"fr_stackband{i}_w", f, "iron"))
        out.append(box([kx1, b0, kz0 - BAND_T], [kx1 + BAND_T, b1, kz1 + BAND_T], f"fr_stackband{i}_e", f, "iron"))
        out.append(box([kx0, b0, kz0 - BAND_T], [kx1, b1, kz0], f"fr_stackband{i}_n", f, "iron"))
        out.append(box([kx0, b0, kz1], [kx1, b1, kz1 + BAND_T], f"fr_stackband{i}_s", f, "iron"))
    return out


# ---------------------------------------------------------------- builders: tier 2
def build_stalls():
    """The stalls' back wall (with a flue mouth at the foot of each stall's back) and their end and
    partition walls, open-fronted to the east."""
    p = "stalls"
    out = []
    (bx0, bx1), (z0, z1) = ST_BACK, ST_BACK_Z
    hw, hh = FLUE_MOUTH
    holes = [((s0 + s1) / 2 - hw, (s0 + s1) / 2 + hw) for s0, s1 in STALLS]
    out += wall([bx0, PLINTH, z0], [bx1 - FLUE_MOUTH_D, PLINTH + hh, z1], "stalls_backfootw", p, "rubble")
    for i, (a, b) in enumerate(runs(z0, z1, holes), 1):
        out += wall([bx1 - FLUE_MOUTH_D, PLINTH, a], [bx1, PLINTH + hh, b], f"stalls_backfoot{i}", p, "rubble")
    out += wall([bx0, PLINTH + hh, z0], [bx1, ST_TOP, z1], "stalls_back", p, "rubble")
    for i, (w0, w1) in enumerate(ST_WALLS, 1):
        out += wall([bx1, PLINTH, w0], [ST_FRONT, ST_TOP, w1], f"stalls_wall{i}", p, "rubble")
    return out


def build_stallflue():
    """The collecting flue on the back wall (brick, closed), and the header at the cold end that carries it
    into the flue chamber's throat, closing the throat."""
    p = "stallflue"
    (bx0, bx1), (z0, _) = ST_BACK, ST_BACK_Z
    (hx0, hx1), (hy0, hy1), (hz0, hz1) = HEADER
    out = wall([bx0, CFLUE_Y[0], z0], [bx1, CFLUE_Y[1], hz0], "stallflue_duct", p, "brick")
    out += wall([hx0, hy0, hz0], [hx1, hy1, hz1], "stallflue_header", p, "brick")
    return out


def build_stallbin():
    """The stall roaster's bin, its hopper: an open oak box on a trestle (its north legs on the plinth, its
    south posts on the header), a trough from a hole in its floor down into the southern stall."""
    p = "stallbin"
    (x0, x1), (y0, y1), (z0, z1) = BIN
    out = open_box([x0, y0, z0], [x1, y1, z1], BIN_T, "stallbin_box", p, "oak", hole=BIN_HOLE)
    for i, (lx0, lx1) in enumerate(LEGS_X, 1):
        out.append(box([lx0, PLINTH, LEGS_Z[0][0]], [lx1, y0, LEGS_Z[0][1]], f"stallbin_leg{i}", p, "oak"))
        out.append(box([lx0, HEADER[1][1], LEGS_Z[1][0]], [lx1, y0, LEGS_Z[1][1]], f"stallbin_post{i}", p, "oak"))
    out.append(box([LEGS_X[0][0], PLINTH + 9.0, LEGS_Z[0][0] + 0.4], [LEGS_X[1][1], PLINTH + 10.4, LEGS_Z[0][1] - 0.4], "stallbin_rail", p, "oak"))
    top, bot = TROUGH
    out.append(strut(top, bot, 0.6, 4.0, "stallbin_trough", p, "oak"))
    for i, dx in enumerate((-2.3, 2.3), 1):
        a = [top[0] + dx, top[1] + 0.9, top[2]]
        b = [bot[0] + dx, bot[1] + 0.9, bot[2]]
        out.append(strut(a, b, 2.4, 0.6, f"stallbin_troughside{i}", p, "oak"))
    return out


def build_heaps():
    """In each stall, a bed of firewood (logs across the stall) and the ore heaped on it in courses."""
    p = "heaps"
    out = []
    x0, x1 = ST_BACK[1] + 1.0, ST_FRONT - 1.0
    for s, (z0, z1) in enumerate(STALLS, 1):
        n = 4
        pitch = (z1 - z0 - 1.0) / n
        for i in range(n):
            a = z0 + 0.5 + pitch * i + 0.2
            out.append(box([x0, PLINTH, a], [x1, PLINTH + LOG_H, a + pitch - 0.4], f"heaps_s{s}log{i + 1}", p, "firewood"))
        y = PLINTH + LOG_H
        for c, (dx, dz, h) in enumerate(((0.5, 0.8, 4.0), (2.5, 2.3, 3.5), (4.5, 3.8, 3.0), (6.0, 5.3, 2.0)), 1):
            out.append(box([x0 + dx, y, z0 + dz], [x1 - dx, y + h, z1 - dz], f"heaps_s{s}course{c}", p, "ore"))
            y += h
    return out


# ---------------------------------------------------------------- builders: tier 3, the furnace
def build_hearth():
    """The hearth's bed (firebrick, the discharge hole through it into the pit), the bridge wall, and the
    firebox's grate on its bearers."""
    p = "hearth"
    out = []
    (dx0, dx1), (dz0, dz1) = DISCHARGE
    (ix0, ix1) = IN_X
    hz0, hz1 = HEARTH_Z
    out += wall([ix0, PLINTH, hz0], [ix1, HEARTH_Y, dz0], "hearth_bed_n", p, "firebrick")
    out += wall([ix0, PLINTH, dz1], [ix1, HEARTH_Y, hz1], "hearth_bed_s", p, "firebrick")
    out += wall([ix0, PLINTH, dz0], [dx0, HEARTH_Y, dz1], "hearth_bed_w", p, "firebrick")
    out += wall([dx1, PLINTH, dz0], [ix1, HEARTH_Y, dz1], "hearth_bed_e", p, "firebrick")
    out += wall([ix0, PLINTH, BRIDGE_Z[0]], [ix1, BRIDGE_TOP, BRIDGE_Z[1]], "hearth_bridge", p, "firebrick")
    for i, (z0, z1) in enumerate(BEARERS_Z, 1):
        out += wall([WALL_X[0][1] - 1.0, BEARER_Y[0], z0], [WALL_X[1][0] + 1.0, BEARER_Y[1], z1], f"hearth_bearer{i}", p, "iron")
    for i in range(GRATE_BARS):
        x = ix0 + 1.0 + 2.0 * i
        out += wall([x, GRATE_Y[0], FIREBOX_Z[0]], [x + 1.0, GRATE_Y[1], FIREBOX_Z[1]], f"hearth_grate{i + 1:02d}", p, "iron")
    return out


def build_walls():
    """The side walls and the fire end's wall (red brick), the firebrick gable under the arch at the fire
    end, and the firebrick skewbacks on the walls' tops."""
    p = "walls"
    out = []
    for s, (x0, x1) in zip("we", WALL_X):
        out += wall([x0, PLINTH, END_N[1]], [x1, SPRING_Y, HEARTH_Z[1]], f"walls_side{s}", p, "brick")
    out += wall([WALL_X[0][0], PLINTH, END_N[0]], [WALL_X[1][1], SPRING_Y, END_N[1]], "walls_end", p, "brick")
    # the gable under the arch at the fire end, stepped to the intrados
    x = IN_X[0] + 1.0
    i = 0
    while x < IN_X[1] - 1.0 - 1e-9:
        x1 = min(x + 4.0, IN_X[1] - 1.0)
        top = max(intrados(x), intrados(x1), intrados(min(max(ARCH_C[0], x), x1)))
        if top > SPRING_Y + 0.05:
            i += 1
            out.append(box([x, SPRING_Y, END_N[0]], [x1, top + 0.5, END_N[1]], f"walls_gable{i}", p, "firebrick"))
        x = x1
    for s, (x0, x1) in zip("we", SKEW_X):
        out += wall([x0, SKEW_Y[0], ARCH_Z[0]], [x1, SKEW_Y[1], ARCH_Z[1]], f"walls_skew{s}", p, "firebrick")
    return out


def build_arch():
    """The firebrick arch over the firebox and the hearth: VOUSSOIRS strips round the arch's centre, cut at
    the block grid; the crown strip cut round the spindles' glands and the charging hole. The charging
    hole's iron seat."""
    p = "arch"
    out = []
    cx, cy = ARCH_C
    for i in range(VOUSSOIRS):
        phi = ALPHA - (i + 0.5) * DELTA
        holes = crown_holes() if i == VOUSSOIRS // 2 else []
        for j, (z0, z1) in enumerate(runs(ARCH_Z[0], ARCH_Z[1], holes), 1):
            el = box([cx - STRIP_W / 2, cy + R_IN, z0], [cx + STRIP_W / 2, cy + R_OUT, z1], f"arch_v{i + 1}_{j}", p, "firebrick")
            if abs(phi) > 1e-12:
                rotate([el], "z", math.degrees(phi), (cx, cy, 0.0))
            out.append(el)
    hx, hz = SEAT_HOLE
    out += frame_bars([SEAT[0][0], SEAT[2][0], SEAT[1][0]], [SEAT[0][1], SEAT[2][1], SEAT[1][1]],
                      [hx[0], SEAT[2][0], hz[0]], [hx[1], SEAT[2][1], hz[1]], "y", "arch_seat", p, "iron")
    return out


def build_ironwork():
    """The buckstays either side and their tie rods over the arch, with nuts; the skewback bars; the fire,
    ash-pit and work doors in their frames."""
    p = "ironwork"
    out = []
    for i, zc in enumerate(STAY_Z, 1):
        z0, z1 = zc - STAY_W / 2, zc + STAY_W / 2
        for s, (x0, x1) in zip("we", STAY_X):
            out += wall([x0, PLINTH, z0], [x1, STAY_TOP, z1], f"ironwork_stay{i}{s}", p, "iron")
        out += wall([0.6, ROD_Y[0], zc - ROD_T / 2], [47.4, ROD_Y[1], zc + ROD_T / 2], f"ironwork_rod{i}", p, "iron")
        for s, (x0, x1) in zip("we", NUT_X):
            out.append(box([x0, ROD_Y[0] - 0.4, zc - 0.8], [x1, ROD_Y[1] + 0.4, zc + 0.8], f"ironwork_nut{i}{s}", p, "iron"))
    for s, (x0, x1) in zip("we", SKEWBAR_X):
        out += wall([x0, SKEW_Y[0], ARCH_Z[0]], [x1, SKEW_Y[1], ARCH_Z[1]], f"ironwork_skewbar{s}", p, "iron")
    fr = DOOR_FRAME
    for tag, ((x0, x1), (y0, y1)) in (("fire", FIRE_DOOR), ("ash", ASH_DOOR)):
        out += frame_bars([x0 - fr, y0 - fr, 0.5], [x1 + fr, y1 + fr, END_N[0]], [x0, y0, 0.5], [x1, y1, END_N[0]], "z",
                          f"ironwork_{tag}frame", p, "iron")
        out.append(box([x0, y0, 0.4], [x1, y1, 0.9], f"ironwork_{tag}door", p, "iron"))
        out.append(box([x1 - 4.0, (y0 + y1) / 2 - 0.5, 0.0], [x1 - 2.0, (y0 + y1) / 2 + 0.5, 0.4], f"ironwork_{tag}handle", p, "iron"))
    hw, (y0, y1) = WORK_DOOR
    for k, zc in enumerate(RABBLE_Z, 1):
        for s, (wx0, wx1) in zip("we", WALL_X):
            face = wx0 if s == "w" else wx1
            sgn = -1.0 if s == "w" else 1.0
            fa, fb = sorted((face, face + sgn * 0.6))
            out += frame_bars([fa, y0 - fr, zc - hw - fr], [fb, y1 + fr, zc + hw + fr], [fa, y0, zc - hw], [fb, y1, zc + hw], "x",
                              f"ironwork_workframe{k}{s}", p, "iron")
            da, db = sorted((face + sgn * 0.1, face + sgn * 0.5))
            out.append(box([da, y0, zc - hw], [db, y1, zc + hw], f"ironwork_workdoor{k}{s}", p, "iron"))
            ha, hb = sorted((face + sgn * 0.5, face + sgn * 0.9))
            out.append(box([ha, (y0 + y1) / 2 - 0.4, zc + hw - 2.0], [hb, (y0 + y1) / 2 + 0.4, zc + hw - 0.8], f"ironwork_workhandle{k}{s}", p, "iron"))
    return out


def build_fire():
    """The fire on the grate: a bed of fuel and a few lumps on it."""
    p = "fire"
    x0, x1 = IN_X[0] + 1.0, IN_X[1] - 1.0
    z0, z1 = FIREBOX_Z[0] + 1.0, FIREBOX_Z[1] - 1.0
    y = GRATE_Y[1]
    out = [box([x0, y, z0], [x1, y + 1.5, z1], "fire_bed", p, "fire")]
    for i, (lx, lz, w) in enumerate(((11.0, 9.0, 3.0), (17.5, 13.0, 2.5), (24.0, 9.5, 3.5), (29.0, 15.5, 2.5), (34.0, 11.0, 3.0)), 1):
        out.append(box([lx, y + 1.5, lz], [lx + w, y + 2.5, lz + w], f"fire_lump{i}", p, "fire"))
    return out


# ---------------------------------------------------------------- builders: tier 3, the drive and the rabbles
SHAFT_C = (SHAFT[0], SHAFT[1], 0.0)


def spur(c, a0, a1, pitch_r, n, name, part, tex, phase):
    """A spur gear on an axis along z: a body to the root and n teeth of MODULE (phase: tooth 1's angle,
    from +y towards -x)."""
    root, tip = pitch_r - 1.25 * MODULE, pitch_r + MODULE
    out = disc("z", c, a0, a1, root, f"{name}_body", part, tex, k=4, phase=phase)
    return out + [radial("z", c, a0 + 0.03, a1 - 0.03, root - 0.25, tip, TOOTH_W, phase + TAU * i / n, f"{name}_tooth{i + 1}", part, tex)
                  for i in range(n)]


def build_drive():
    """The line shaft: the oak entry from the power face (the vanilla axle's cross), the iron shaft along the
    roof's middle to the feeder's crank, a pinion for each crown wheel; its pedestals on the arch."""
    p = "lineshaft"
    x, y = SHAFT
    z0, z1 = ENTRY_Z
    out = [box([x - 1.5, y - 0.6, z0], [x + 1.5, y + 0.6, z1], "lineshaft_entrya", p, "oak"),
           box([x - 0.6, y - 1.5, z0], [x + 0.6, y + 1.5, z1], "lineshaft_entryb", p, "oak")]
    out += disc("z", SHAFT_C, *SHAFT_Z, SHAFT_R, "lineshaft_rod", p, "iron")
    for k, zs in enumerate(RABBLE_Z, 1):
        zp = zs - RC
        out += spur(SHAFT_C, zp - PINION_W / 2, zp + PINION_W / 2, RP, NP, f"lineshaft_pinion{k}", p, "iron", math.pi)
    q = "pedestals"
    for k, (a, b) in enumerate(PEDESTALS, 1):
        out.append(box([PED_BASE_X[0], CROWN_Y[1], a], [PED_BASE_X[1], CROWN_Y[1] + 0.6, b], f"pedestal{k}_base", q, "iron"))
        out.append(box([PED_COL_X[0], CROWN_Y[1] + 0.6, a + 0.6], [PED_COL_X[1], y - PED_BEARING, b - 0.6], f"pedestal{k}_column", q, "iron"))
        out.append(box([x - PED_BEARING, y - PED_BEARING, a], [x + PED_BEARING, y + PED_BEARING, b], f"pedestal{k}_bearing", q, "iron"))
    return out


def build_rabbles():
    """Each rabble: a spindle through a gland in the arch's crown and a bearing on the roof, a crown wheel
    on its top (teeth up, a pinion of the line shaft on its north rim), and in the furnace a hub with two
    arms, each with three ploughs set across it."""
    out = []
    for k, zs in enumerate(RABBLE_Z, 1):
        p = f"rabble{k}"
        c = (SHAFT[0], 0.0, zs)
        out += disc("y", c, *SPINDLE_Y, SPINDLE_R, f"{p}_spindle", p, "iron")
        out += disc("y", c, CROWN_HUB[0], CROWN_HUB[1], CROWN_HUB[2], f"{p}_crownhub", p, "iron")
        out += disc("y", c, *CROWN_WEB_Y, RC + MODULE + 0.05, f"{p}_crownweb", p, "iron", k=6)
        for j in range(NC):
            ang = math.pi + math.pi / NC + TAU * j / NC          # a gap faces north (the pinion) at theta 0
            out.append(radial("y", c, CROWN_TEETH_Y[0], CROWN_TEETH_Y[1], RC - 0.7, RC + MODULE + 0.05, TOOTH_W, ang,
                              f"{p}_crowntooth{j + 1:02d}", p, "iron"))
        out += disc("y", c, RABBLE_HUB[0], RABBLE_HUB[1], RABBLE_HUB[2], f"{p}_hub", p, "iron")
        rest = (k - 1) * math.pi / 2                             # the second rabble a quarter turn on from the first
        for a, arm in enumerate((0.0, math.pi), 1):
            ang = rest + arm
            els = [radial("y", c, ARM_Y[0], ARM_Y[1], RABBLE_HUB[2] - 0.4, RABBLE_R, ARM_W, 0.0, f"{p}_arm{a}", p, "iron")]
            for b, r in enumerate(BLADES_R, 1):
                L, t, (by0, by1) = BLADE
                bl = box([c[0] - L / 2, by0, zs + r - t / 2], [c[0] + L / 2, by1, zs + r + t / 2], f"{p}_arm{a}plough{b}", p, "iron")
                rotate([bl], "y", BLADE_SET, (c[0], 0.0, zs + r))
                els.append(bl)
            if abs(ang) > 1e-12:
                rotate(els, "y", math.degrees(ang), c)
            out += els
        q = "rabblemounts"
        out.append(box([CROWN_X[0], CROWN_Y[0], zs - GLAND_Z], [CROWN_X[1], CROWN_Y[1], zs + GLAND_Z], f"rabblemount{k}_gland", q, "iron"))
        (bx0, bx1), hz, (by0, by1), bh, (ey0, ey1) = BRACKET
        out.append(box([bx0, by0, zs - hz], [bx1, by1, zs + hz], f"rabblemount{k}_base", q, "iron"))
        out.append(box([c[0] - bh, ey0, zs - bh], [c[0] + bh, ey1, zs + bh], f"rabblemount{k}_bearing", q, "iron"))
    return out


def build_charger():
    """Tier 3's charging box, filled by hand: an open iron box, its mouth at the infeed cell's top, its
    spout down through the seat and the arch, a flange on the seat, the slide gate pushed in."""
    p = "charger"
    (x0, x1), (y0, y1), (z0, z1) = CHARGER
    (tx0, tx1), (tz0, tz1) = THROAT
    out = open_box([x0, y0, z0], [x1, y1, z1], HOPPER_T, "charger_box", p, "iron", hole=((tx0, tx1), (tz0, tz1)))
    out += tube([tx0, THROAT_Y0, tz0], [tx1, y0 + HOPPER_T, tz1], THROAT_T, "y", "charger_spout", p, "iron")
    (fx, fz, fy) = FLANGE
    out += frame_bars([fx[0], fy[0], fz[0]], [fx[1], fy[1], fz[1]], [tx0, fy[0], tz0], [tx1, fy[1], tz1], "y", "charger_flange", p, "iron")
    (gx0, gx1), (gy0, gy1), (gz0, gz1) = GATE
    out.append(box([gx0, gy0, gz0], [gx1, gy1, gz1], "charger_gate", p, "iron"))
    out.append(box([gx1, gy0 - 0.8, (gz0 + gz1) / 2 - 1.2], [gx1 + 0.6, gy1 + 0.8, (gz0 + gz1) / 2 + 1.2], "charger_gatehandle", p, "iron"))
    return out


# ---------------------------------------------------------------- builders: tier 4
def build_hopper():
    """The chute-fed hopper: an open iron bin, its mouth at the infeed cell's top, its throat down onto the
    feed box; the feed box (the push rod's slot in its north wall, the plate's runners inside it); the feed
    chute from it through the seat and the arch, its flange on the seat."""
    p = "hopper"
    (x0, x1), (y0, y1), (z0, z1) = HOPPER
    (hx0, hx1), (hz0, hz1), (hy0, hy1) = HOPPER_THROAT
    t = HOPPER_T
    out = open_box([x0, y0, z0], [x1, y1, z1], t, "hopper_bin", p, "iron", hole=((hx0 + t, hx1 - t), (hz0 + t, hz1 - t)))
    out += tube([hx0, hy0, hz0], [hx1, hy1, hz1], t, "y", "hopper_throat", p, "iron")
    (bx0, bx1), (by0, by1), (bz0, bz1) = FEEDBOX
    ft = FEEDBOX_T
    (sx0, sx1), (sy0, sy1) = SLOT
    out.append(box([bx0, by0, bz0], [bx1, sy0, bz0 + ft], "hopper_feedbox_n1", p, "iron"))
    out.append(box([bx0, sy0, bz0], [sx0, sy1, bz0 + ft], "hopper_feedbox_n2", p, "iron"))
    out.append(box([sx1, sy0, bz0], [bx1, sy1, bz0 + ft], "hopper_feedbox_n3", p, "iron"))
    out.append(box([bx0, sy1, bz0], [bx1, by1 - ft, bz0 + ft], "hopper_feedbox_n4", p, "iron"))
    out.append(box([bx0, by0, bz1 - ft], [bx1, by1 - ft, bz1], "hopper_feedbox_s", p, "iron"))
    out.append(box([bx0, by0, bz0 + ft], [bx0 + ft, by1 - ft, bz1 - ft], "hopper_feedbox_w", p, "iron"))
    out.append(box([bx1 - ft, by0, bz0 + ft], [bx1, by1 - ft, bz1 - ft], "hopper_feedbox_e", p, "iron"))
    out += frame_bars([bx0, by1 - ft, bz0], [bx1, by1, bz1], [hx0 + t, by1 - ft, hz0 + t], [hx1 - t, by1, hz1 - t], "y", "hopper_feedbox_roof", p, "iron")
    (rz0, rz1), (rz2, rz3), (ry0, ry1) = RUNNERS
    for i, (a, b) in enumerate(((rz0, rz1), (rz2, rz3)), 1):
        out.append(box([bx0 + ft, ry0, a], [bx1 - ft, ry1, b], f"hopper_runner{i}", p, "iron"))
    (tx0, tx1), (tz0, tz1) = THROAT
    out += tube([tx0, THROAT_Y0, tz0], [tx1, FLANGE[2][0], tz1], THROAT_T, "y", "hopper_chute", p, "iron")
    (fx, fz, fy) = FLANGE
    out += frame_bars([fx[0], fy[0], fz[0]], [fx[1], fy[1], fz[1]], [tx0, fy[0], tz0], [tx1, fy[1], tz1], "y", "hopper_flange", p, "iron")
    return out


def build_feeder():
    """The feeder: a crank disc keyed on the line shaft's end with its pin, and the Scotch yoke the pin
    rides in, its push rod into the feed box and the plate it shuttles across under the hopper's throat."""
    out = disc("z", SHAFT_C, *CRANK_Z, CRANK_R, "crank_disc", "crank", "iron", k=6)
    out += disc("z", (SHAFT[0], SHAFT[1] + PIN_AT, 0.0), *PIN_Z, PIN_R, "crank_pin", "crank", "iron")
    p = "yoke"
    x = SHAFT[0]
    z0, z1 = YOKE_Z
    y0, y1 = YOKE_Y
    out += [box([x - YOKE_SLOT - YOKE_CHEEK, y0, z0], [x - YOKE_SLOT, y1, z1], "yoke_cheekw", p, "iron"),
            box([x + YOKE_SLOT, y0, z0], [x + YOKE_SLOT + YOKE_CHEEK, y1, z1], "yoke_cheeke", p, "iron"),
            box([x - YOKE_SLOT - YOKE_CHEEK, ROD[1][0], z0], [x + YOKE_SLOT + YOKE_CHEEK, y0, z1], "yoke_foot", p, "iron"),
            box([ROD[0][0], ROD[1][0], ROD[2][0]], [ROD[0][1], ROD[1][1], ROD[2][1]], "yoke_rod", p, "iron"),
            box([DROPPER[0][0], DROPPER[1][0], DROPPER[2][0]], [DROPPER[0][1], DROPPER[1][1], DROPPER[2][1]], "yoke_dropper", p, "iron"),
            box([PLATE[0][0], PLATE[1][0], PLATE[2][0]], [PLATE[0][1], PLATE[1][1], PLATE[2][1]], "yoke_plate", p, "iron")]
    return out


def build():
    els = build_drive() + build_rabbles() + build_feeder()
    els += build_hearth() + build_walls() + build_arch() + build_ironwork() + build_fire() + build_charger() + build_hopper()
    els += build_stalls() + build_stallflue() + build_stallbin() + build_heaps()
    els += build_frame()
    return els


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


def static(pid, requires=None, match=None):
    return {"id": pid, "match": [match or f"{pid}_*"], "requires": requires or pid, "drivers": []}


def _rig_parts():
    """The parts in the tiers' order (the viewer lists the requires values in the order the parts first name
    them): tier 2's, the furnace's, tier 4's, then the frame."""
    shaft = pt(SHAFT[0], SHAFT[1], 0.0)
    parts = [static("stalls"), static("stallflue"), static("stallbin"), static("heaps"),
             static("hearth"), static("walls"), static("arch"), static("ironwork"), static("fire"),
             {"id": "lineshaft", "match": ["lineshaft_*"], "requires": "drive",
              "drivers": [{"type": "rotate", "axis": "z", "pivot": shaft, "ratio": 1.0}]},
             static("pedestals", "drive", "pedestal*")]
    for k, zs in enumerate(RABBLE_Z, 1):
        parts.append({"id": f"rabble{k}", "match": [f"rabble{k}_*"], "requires": "rabbles",
                      "drivers": [{"type": "rotate", "axis": "y", "pivot": pt(SHAFT[0], 0.0, zs), "ratio": r6(RATIO)}]})
    parts += [static("rabblemounts", "rabbles", "rabblemount*"), static("charger"), static("hopper"),
              {"id": "crank", "match": ["crank_*"], "requires": "feeder",
               "drivers": [{"type": "rotate", "axis": "z", "pivot": shaft, "ratio": 1.0}]},
              # a Scotch yoke: the pin, straight up at theta 0, is at x = -PIN_AT sin(theta) as the shaft turns
              {"id": "yoke", "match": ["yoke_*"], "requires": "feeder",
               "drivers": [{"type": "slide", "axis": "x", "amplitude": r6(-PIN_AT / B), "ratio": 1.0, "phase": 0.0}]},
              {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []}]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


MOVING = ("lineshaft", "crank", "yoke", "rabble1", "rabble2")


# ---------------------------------------------------------------- poses
REST = (0.0, "all")                          # (theta, state): the authored pose, everything shown


def inputs_of(pose):
    return {"theta": pose[0]}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


def state_shows(requires, state):
    """Whether a part needing `requires` is drawn in `state` ("all": every part)."""
    return state == "all" or requires is None or requires in STATES[state]


# ---------------------------------------------------------------- the rig file
def footprint():
    body = [(x, y, z) for x in range(CELLS_X) for y in range(BODY_Y) for z in range(CELLS_Z)]
    return sorted(set(body) | set(DRIVE_CELLS) | set(STACK_CELLS))


ANCHORS = ("infeed", "output", "charge", "discharge", "fuel", "sulfur", "smoke")
CELL_ANCHORS = ("powerCell", "infeedCell", "outputCell")


def make_rig(parts):
    (tx0, tx1), (tz0, tz1) = THROAT
    (fx0, fx1), _, _ = FLUEBOX
    (dz0, dz1), (dy0, dy1) = SULFUR_DOOR
    (cx0, cx1), (cz0, cz1), (cy0, cy1) = CHANNEL
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the middle of the fire end, "
                    "nearest the player who placed it; the roaster runs south to the stack. One of the ore line's upgradeable "
                    "machines (#711): the cells, the power cell, the infeed, the output spout and the stack are the frame's and "
                    "never move; tiers lists the requires values each tier fits (fitting the next tier's set replaces the last's "
                    "working parts). The only input is theta, the axle's angle. See the roaster's README for the schema.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "infeedCell": list(INFEED_CELL),
        "infeedFace": INFEED_FACE,
        "outputCell": list(OUTPUT_CELL),
        "outputFace": OUTPUT_FACE,
        "infeed": {"pos": pt(SHAFT[0], CHARGER[1][1], (CHARGER[2][0] + CHARGER[2][1]) / 2)},
        "output": {"pos": pt(cx1, (cy0 + cy1) / 2, (cz0 + cz1) / 2)},
        "charge": {"pos": pt((tx0 + tx1) / 2, HEARTH_Y, (tz0 + tz1) / 2)},
        "discharge": {"pos": pt((DISCHARGE[0][0] + DISCHARGE[0][1]) / 2, HEARTH_Y, (DISCHARGE[1][0] + DISCHARGE[1][1]) / 2)},
        "fuel": {"pos": pt((FIRE_DOOR[0][0] + FIRE_DOOR[0][1]) / 2, (FIRE_DOOR[1][0] + FIRE_DOOR[1][1]) / 2, 0.0)},
        "fuelSide": "north",
        "sulfur": {"pos": pt(fx1 + 0.6, (dy0 + dy1) / 2, (dz0 + dz1) / 2)},
        "sulfurSide": "east",
        "smoke": {"pos": pt(SHAFT[0], STACK_TOP, (STACK_XZ[1][0] + STACK_XZ[1][1]) / 2)},
        "tiers": {k: list(v) for k, v in TIERS.items()},
        "gearing": {"rabbleTurnsPerAxleTurn": r6(abs(RATIO)), "feederStrokesPerAxleTurn": 1, "pinionTeeth": NP, "crownTeeth": NC,
                    "_comment": "As drawn: each rabble's crown wheel is driven from its north rim by a pinion on the line shaft "
                                f"({NP}:{NC}), so the rabbles turn {abs(RATIO):.4g} as fast as the axle, against it; the feeder's crank "
                                "is keyed on the shaft's end, one stroke of the plate a turn. A renderer has nothing to read here: "
                                "the parts' drivers carry it."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0]: one block west."""
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
    for key in CELL_ANCHORS:
        ship[key] = shift_cell(rig[key], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig, every tier's parts
    together (every reader rebuilds them the same way); then the lids."""
    written = flatten(shape["elements"], textures={})
    rest = [posed(w, _part_matrix(ship_parts, part_of(ship_parts, w.name), {"theta": 0.0})) for w in written]
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
    poses = [REST] + [(th, "all") for th in (0.7, -2.3, 4.4, 9.1)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    span = [(min(c[k] for c in cells), max(c[k] for c in cells)) for k in range(3)]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; {len(cells)} cells, x {span[0]}, "
          f"y {span[1]}, z {span[2]}; power {ship['powerCell']} {ship['powerFace']}, infeed {ship['infeedCell']} {ship['infeedFace']}, "
          f"output {ship['outputCell']} {ship['outputFace']}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
REF_THETAS = (0.0, 0.35, 0.9, 1.7, -2.3, 3.1, 4.4, -5.6, 7.85, 9.42, 12.6, -15.0, 18.85)


def reference_json(ship_parts):
    poses = []
    for th in REF_THETAS:
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], {"theta": th})) for q in ship_parts}
        poses.append({"theta": th, "travel": abs(th), "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped roaster-rig.json's parts: each part's matrix as 3 rows of 4 "
                        "(block units) at each axle angle theta (travel is |theta|). The site's and the generator's tests check "
                        "their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Every tier's parts are in this one "
             "shape, the stalls of tier 2 where the furnace of tiers 3 and 4 stands: a renderer draws the fitted tier's. The fire's "
             "texture code is 'fire': a renderer may set it to ember while the furnace burns. Keep element names when editing: "
             "the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


COPLANAR_THETAS = (0.0, 0.4, 1.3, 2.9)


def coplanar_poses():
    """The poses the z-fighting fix and its check look at: each state at rest, and the working tiers at a few
    angles. Never every tier at once: the stalls stand where the furnace does, and are never drawn with it."""
    return ((0.0, "tier4"), (0.0, "tier3"), (0.0, "tier2"), (0.0, "frame")) + tuple(
        (th, s) for th in COPLANAR_THETAS[1:] for s in ("tier3", "tier4"))


def shown(posed_els, pose, parts=None):
    """The posed elements as they can be seen in the pose's state: the others moved far away (copies; the
    order kept), so the z-fighting fix and its check deal only with faces that show together."""
    req = {p["id"]: p["requires"] for p in (parts or rig_parts())}
    out = []
    for e in posed_els:
        if not state_shows(req[e.part], pose[1]):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def fix_coplanar(els, parts):
    """machinegen's fix, once with each working state's rest pose first: the fix removes the faces pressed
    against their own part at its first pose only, and each part is in place only in its own states."""
    def fn(es, pose):
        return shown([posed(el, pm(parts, el.part, pose)) for el in es], pose, parts)
    poses = coplanar_poses()
    before, hidden = {}, 0
    for first in ("tier4", "tier3", "tier2"):
        lead = (0.0, first)
        b, h = fix_coplanar_posed(els, fn, (lead,) + tuple(q for q in poses if q != lead))
        for pose, pairs in b.items():
            before.setdefault(pose, pairs)
        hidden += h
    return before, hidden


def main():
    ap = argparse.ArgumentParser(description="Generate the roaster's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_roaster
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
    ok = validate_roaster.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "roaster.json", args.out / "roaster_frame.json", args.out / "roaster-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "roaster.json", SHAPE_DIR / "roaster_frame.json", RIG_DIR / "roaster-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_roaster.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
