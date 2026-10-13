#!/usr/bin/env python3
"""Generate the roaster's shapes, rig and reference poses.

The roaster roasts sulfide concentrate into roasted concentrate, giving off sulfur (#736). It is one of
the ore line's upgradeable machines (#711): a frame placed at its final size, then each level's parts
fitted into it. Nothing that connects to the outside moves between levels: the infeed hopper, the
output spout, the power cell, the flue chamber with its sulfur door and the stack are where the frame
(or level 1) puts them. The levels, as period devices (#737):

    level 1 (water tier)  a stall roaster: three open-fronted rubble stalls against a back wall, the ore
            roasted in heaps on beds of firewood; each stall's flue rises in the back wall to a collecting
            flue that a header carries into the frame's flue chamber, where the sulfur condenses, and up
            the stack. An iron hopper on a timber trestle at the cold end feeds the southern stall by a
            trough; the roasted ore is raked into the frame's discharge pit in front of the stalls.
    level 2 (mill tier)   a reverberatory roaster, chute-fed: the stalls, their flue and the trestle are
            pulled down (a stall roaster cannot be turned into a furnace) and a brick furnace under a
            firebrick arch is built on the same plinth, its hearth opening into the same flue chamber. Its
            fire burns on a step grate fed by gravity from a coal bunker on the fire end (a chute keeps the
            bunker filled: no stoking door). Two rabbles on vertical spindles through the arch (the
            Edwards furnace of the 1890s) plough the charge along the hearth to the discharge hole over the
            pit; bevel gears from a line shaft on the roof turn them, and the line shaft continues the
            vanilla axle at the power cell. The level 1 hopper stays where it is, now set on a fluted feed
            roll keyed on the line shaft's end, which meters the previous machine's chute down through the
            arch. A gear case at the fire end holds a rectifier, so all of that turns forward whichever way
            the axle turns. The furnace's work doors stand open (their plates raised in their guides), so the
            hearth and the rabbles can be seen.

The roaster takes no process water at either level: roasting is dry, so it has no pipe inlet.

Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    roaster.json           the whole machine, every level's parts    (assets/.../shapes/block/)
    roaster_frame.json     the static frame only (block and item)    (assets/.../shapes/block/)
    roaster-rig.json       cells, anchors, the levels and the rig    (assets/.../config/)
    rig-reference.json     every part's matrix at a grid of poses    (tests/Roaster/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_roaster.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from
the machine box's north-west-bottom corner (the "build frame"); the controller cell, the middle of the
near (fire) end at ground level, is the build frame's cell (1, 0, 0), so the shipped files are the
build frame moved one block west and divided by 16. The roaster runs along z: the fire end (north,
the controller) to the stack (south).

The rig's input, as this machine uses it (README "Rig"):

    theta  the axle angle, either way: the entry shaft into the gear case
    psi    the axle's travel, which only grows (`"input": "travel"`): beyond the gear case's rectifier, the line shaft,
           the feed roll on its end and, through the bevel gears, the rabbles, which so turn forward either way
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
INFEED_CELL, INFEED_FACE = (1, 2, 4), "up"   # the hopper (both levels): its mouth is this cell's top, filled by hand or by a chute
OUTPUT_CELL, OUTPUT_FACE = (2, 0, 2), "east" # the frame's discharge spout: the output chute's end, both levels
FUEL_CELL, FUEL_FACE = (0, 1, 0), "up"       # level 2's coal bunker: a chute drops into it at the fire end's west corner
STACK_CELLS = ((1, 2, 5), (1, 3, 5), (1, 4, 5))   # the stack above the flue chamber
DRIVE_CELLS = tuple((1, 2, z) for z in range(5))  # the line shaft, the bevel gears and the hopper over the roof's middle

TEXTURES = {
    "stone": "game:block/stone/brick/granite1",              # the plinth, the stack's cap: dressed granite
    "rubble": "game:block/stone/drystone/granite1",          # the stalls' walls: rubble masonry
    "brick": "game:block/clay/brick/four/running/red1",      # the furnace's and the flue chamber's walls, the stack, the stalls' flue
    "firebrick": "game:block/clay/brick/four/running/fire1", # the hearth, the bridge wall, the arch, the skewbacks
    "iron": "game:block/metal/plate/iron",                   # buckstays, rods, doors, grate, shafts, gears, hopper, bunker, feeder
    "oak": "game:block/wood/debarked/oak",                   # level 1's trestle and trough, the line shaft's entry (the vanilla axle's)
    "ore": "game:block/pile/oldore",                         # the ore heaped in the stalls, the charge on the hearth
    "firewood": "game:block/wood/firepit/log",               # the stall heaps' beds of firewood
    "coal": "game:block/coal/bituminous",                    # the coal in the bunker and its throat
    "fire": "game:block/coal/charcoal",                      # the fire on the step grate: a renderer may set it to game:block/coal/ember
    "chute": "game:block/metal/sheet/copper1",               # the vanilla chute's sections at the chute-fed infeeds (a placed chute's, in the pack too)
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the frame
PLINTH = 5.0                                 # the stone plinth under everything (y 0..5); both levels stand on it
PIT = ((32.0, 40.0), (30.0, 38.0))           # (x, z) of the discharge pit sunk in the plinth (y 1..5), by the bridge
CHANNEL = ((40.0, 48.0), (32.0, 38.0), (1.0, 4.0))   # (x, z, y): the tunnel from the pit to the spout on the east face
SPOUT_X = (46.5, 48.0)                       # the spout: an iron lining in the tunnel's mouth, flush with the plinth's face
SPOUT_T = 0.75
FLUEBOX = ((2.0, 46.0), (PLINTH, 32.0), (80.0, 96.0))   # the flue chamber at the cold end (x, y, z)
FB_T = 3.0                                   # its walls' thickness
INLET = ((18.0, 30.0), (12.0, 24.0))         # (x, y): the throat in its north wall, the stalls' header's or the hearth's flue
STACK_XZ = ((17.0, 31.0), (81.0, 95.0))      # the stack's brickwork on the chamber's roof
STACK_TOP = 80.0                             # five blocks up
STACK_FLUE = ((21.0, 27.0), (85.0, 91.0))
CAP_Y = (76.0, 80.0)                         # the stone cap
BANDS_Y = ((50.0, 51.0), (66.0, 67.0))       # iron bands round the stack
BAND_T = 0.8
SULFUR_DOOR = ((84.0, 92.0), (7.0, 14.0))    # (z, y) of the clean-out door on the chamber's east face: sulfur condenses in the chamber

# ---------------------------------------------------------------- level 1: the stall roaster
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
LOG_H = 2.5                                  # the firewood under a heap
# the hopper, fitted at level 1 and kept at level 2: an open iron box, its mouth the infeed cell's top
HOPPER = ((16.5, 31.5), (42.0, 48.0), (67.0, 77.0))   # centred on the infeed cell (z 72), under a vanilla chute's line
HOPPER_T = 1.0
HOPPER_SLOT = ((21.0, 27.0), (69.5, 74.5))   # (x, z): the slot in its floor, over the trough (level 1) or the feed roll (level 2)
# level 1's trestle: the hopper's legs on the header, and the trough from its slot into the southern stall
LEGS_X = ((17.0, 19.0), (29.0, 31.0))
LEGS_Z = ((67.2, 69.2), (74.5, 76.5))         # the north pair on the plinth, the south pair on the header
LEG_RAIL_Y = (36.0, 37.4)
TROUGH = ((24.0, 41.0, 72.0), (24.0, 26.5, 56.5))   # the trough floor's middle line: under the slot, and its foot in the southern stall
TROUGH_W = 6.0
TROUGH_POST_Z = (63.0, 65.0)                 # a post on the last partition wall carries the trough

# ---------------------------------------------------------------- the vanilla chute (the game's blocktypes/metal/chute-straight.json,
# shapes/block/metal/chute/straight.json): an 8 x 8 square tube centred in its cell, walls 1 thick; up and down (the
# "ud" variant, the straight shape turned 90 about z), its walls in cell-local (x, z): north and south across the
# whole width, west and east between them. Every chute-fed infeed ends in a section of it, so a player's chute line
# meets the machine with no gap or step.
CHUTE_UD_WALLS = ((4.0, 12.0, 4.0, 5.0), (4.0, 12.0, 11.0, 12.0), (4.0, 5.0, 5.0, 11.0), (11.0, 12.0, 5.0, 11.0))   # (x0, x1, z0, z1)
CHUTE_OUTER, CHUTE_WALL = (4.0, 12.0), 1.0
INFEED_SECTION_Y = 44.0                      # the ore infeed's section hangs from the cell's top into the hopper's mouth
FUEL_SECTION_Y = 28.5                        # the fuel infeed's from the fuel cell's top into the coal bunker

# ---------------------------------------------------------------- level 2: the reverberatory furnace
WALL_X = ((2.0, 8.0), (40.0, 46.0))          # the side walls
IN_X = (8.0, 40.0)                           # the firebox and the hearth inside them
FRONT_Z = (1.0, 6.0)                         # the fire end's wall: no stoking door (the grate is fed from the bunker over it)
FIREBOX_Z = (6.0, 20.0)
BRIDGE_Z = (20.0, 25.0)
HEARTH_Z = (25.0, 80.0)                      # to the flue chamber's face
HEARTH_Y = 12.0                              # the hearth's floor
BRIDGE_TOP = 17.0
SPRING_Y = 21.0                              # the side walls' tops: the arch springs here
RISE, ARCH_T, VOUSSOIRS = 5.0, 4.0, 7        # the segmental arch over IN_X: its rise, thickness and strips
ARCH_Z = (13.0, 80.0)                        # from the coal bunker's back to the flue chamber
SKEW_X = ((3.0, 9.0), (39.0, 45.0))          # the firebrick skewbacks on the walls' tops, the arch's springing
SKEW_Y = (SPRING_Y, 25.0)
SKEWBAR_X = ((2.0, 3.0), (45.0, 46.0))       # the iron skewback bars outside them
STAY_X = ((1.0, 2.0), (46.0, 47.0))          # buckstays, outside the walls
NUT_X = ((0.0, 1.0), (47.0, 48.0))
STAY_Z = (14.0, 22.0, 50.0, 79.2)            # buckstay pairs (centres): clear of the doors, the pedestals and the feed seat
STAY_W = 2.0
STAY_TOP = 32.0
ROD_Y = (30.3, 31.1)                         # the tie rods over the arch's crown (30)
ROD_T = 0.8
DISCHARGE = ((33.0, 39.0), (31.0, 37.0))     # (x, z) the discharge hole through the hearth into the pit, by the bridge
WORK_DOOR = (7.0, (12.0, 20.0))              # half width (z) about each rabble, and y: a work door each side, open
DOOR_FRAME = 1.0
DOOR_PLATE_Y = (21.0, 28.5)                  # the doors' plates, raised in their guides
ASH_DOOR = ((18.0, 30.0), (PLINTH, 8.5))     # (x, y) the ash pit's clean-out door, under the grate, on the fire end's face
# the firing: a step grate under a coal bunker (gravity-fed; no stoking door)
STEPS = 4
STEP_Z0, STEP_D = FIREBOX_Z[0], 3.5          # the top step's front edge; each step's depth (the steps reach the bridge)
STEP_TOP0, STEP_DROP, STEP_T = 15.0, 2.0, 1.0   # the top step's top, the drop from one to the next, a bar's thickness
BUNKER = ((2.0, 46.0), (SPRING_Y, 32.0), (FRONT_Z[0], ARCH_Z[0]))   # the coal bunker on the fire end (x, y, z): mouth at y 32
BUNKER_T = 1.0
BUNKER_SLOT = ((9.0, 39.0), (7.0, 12.0))     # (x, z): the slot in its floor, over the top step
THROAT_Y0 = 17.0                             # the throat under the slot hangs down to here, just over the top step

# ---------------------------------------------------------------- level 2: the drive and the rabbles
SHAFT = (24.0, 40.0)                         # (x, y) of the line shaft along z: the power cell's centre
SHAFT_R = 0.9
ENTRY_Z = (0.0, 4.0)                         # the oak entry, the vanilla axle's cross profile, from the power face into the gear case
SHAFT_Z = (8.0, 75.4)                        # the iron line shaft, from inside the gear case on through the feed roll to the casing's far wall
# the gear case at the fire end: a closed cast-iron case on the coal bunker's rim, holding a rectifier (as the stamp
# mill's: two pairs of gears on one-way catches, one through an idler, its gears not drawn), so the line shaft coming
# out of its south side turns one way whichever way the axle turns
GEARCASE = ((18.0, 30.0), (33.0, 46.0), (2.0, 12.0))      # the case (x, y, z)
GEARCASE_BASE = ((17.0, 31.0), (32.0, 33.0), (1.0, 13.0))  # its foot, on the bunker's north and south walls
GEARCASE_BOSS = (2.2, 1.0)                   # the bosses round the entry and the line shaft: radius, length out of the case
PEDESTALS = ((16.0, 19.0), (25.0, 28.0), (46.4, 49.2))   # z: the line shaft's pedestals on the arch
PED_BASE_X = (21.5, 26.5)
PED_COL_X = (22.8, 25.2)
PED_BEARING = 2.3                            # the bearing block's half side round the shaft
MODULE = 0.7
NP, NW = 8, 16                               # the bevel pinions on the line shaft, the bevel wheels on the spindles
RP, RW = MODULE * NP / 2, MODULE * NW / 2    # 2.8, 5.6: pitch radii
CONE_P, CONE_W = math.atan2(RP, RW), math.atan2(RW, RP)   # the pitch cones' half angles (their apex where the axes meet)
ADD, DED = MODULE, 1.25 * MODULE             # a tooth's addendum and dedendum
FACE = 2.0                                   # the teeth's face width, along the cone
TOOTH_W = 1.0                                # a tooth's width: a little under half the pitch (2.2)
PINION_BODY = ((-1.5, -0.8, 2.1), (-0.8, -0.1, 1.75), (-0.1, 0.5, 1.45))   # the pinion's body in steps (z from its pitch circle, radius), under its roots
PINION_DED = 1.85 * MODULE                   # its teeth sink into the body (the wheel's tips run in its gaps)
RATIO = -NP / NW                             # spindle turns per shaft turn (a wheel driven from its north rim turns back)
RABBLE_Z = (39.0, 61.0)                      # the two spindles, on the hearth's middle line
WHEEL_Y = SHAFT[1] - RP                      # 37.2: the wheels' pitch plane, where the pinions' pitch circles touch them
SPINDLE_R = 0.75
SPINDLE_Y = (12.8, 37.0)
WHEEL_BODY = (35.0, 36.0, 5.6)               # y0, y1, radius: the wheel's web under its teeth, clear of the pinion's tips
WHEEL_DED = 2.0 * MODULE                     # the wheel's teeth reach down into the web (under the pinion's tips' gaps)
WHEEL_HUB = (34.0, 35.0, 1.8)                # y0, y1, radius: the hub rests on the spindle's bearing
RABBLE_HUB = (12.6, 15.2, 1.8)
ARM_Y = (13.4, 14.6)
ARM_W = 1.2
ARM_R = 11.6                                 # the arms' length from the spindle
BLADES_R = (4.0, 7.5, 10.8)                  # each arm's three ploughs (their middles)
BLADE = (2.6, 1.0, (12.3, 13.8))             # length, thickness, y
BLADE_SET = 40.0                             # degrees the ploughs are set across their arm
GLAND_Z = 1.6                                # half length of a spindle's gland, filling its hole in the crown strip
BRACKET = ((21.5, 26.5), 3.0, (30.0, 31.0), 2.0, (31.0, WHEEL_HUB[0]))   # base plate x, half length z, y; bearing half side, y

# ---------------------------------------------------------------- level 2: the feeder under the hopper
CASING = ((20.0, 28.0), (32.0, HOPPER[1][0]), (68.5, 75.5))   # the feed roll's casing (x, y, z), on its flange, under the hopper
CASING_T = 1.0
ROLL_Z = (70.0, 74.0)
ROLL_R, ROLL_FLUTES, ROLL_FLUTE_R = 2.0, 6, 2.6   # the fluted roll on the line shaft's end: its body, its flutes
CHUTE = ((21.5, 26.5), (70.0, 74.0))         # (x, z): the feed chute from under the roll down through the arch
CHUTE_T = 1.0
CHUTE_Y = (25.5, 34.0)                       # its foot just inside the arch; its top in the casing, under the roll
SEAT = ((19.5, 28.5), (68.5, 75.5), (30.0, 31.0))     # (x, z, y): the chute's iron seat on the arch
FLANGE = ((19.5, 28.5), (68.5, 75.5), (31.0, CASING[1][0]))   # the casing's flange on the seat
CHARGE_HOLE_Z = (69.8, 74.2)                 # the hole through the arch's crown strip

# ---------------------------------------------------------------- the levels (the gameplay's sets and the scenario's states)
LEVELS = {
    "1": ["stalls", "stallflue", "trestle", "hopper"],
    "2": ["hopper", "hearth", "walls", "arch", "ironwork", "firebox", "drive", "rabbles", "feeder"],
}
CONTENTS = {"1": ["heaps"], "2": ["charge", "fire"]}   # what each level shows while roasting (not fitted parts)
RETIRED = ["stalls", "stallflue", "trestle"]           # level 1's parts that level 2 pulls down (README "The levels")
STATES = {"frame": [], "level1": LEVELS["1"] + CONTENTS["1"], "level2": LEVELS["2"] + CONTENTS["2"]}


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


def bar_y(lo, hi, name, part, tex):
    """An iron bar standing up: cut at the block grid along y only (so its faces do not stretch past one
    texture), whole across x and z."""
    pieces = splits(lo[1], hi[1])
    return [box([lo[0], a, lo[2]], [hi[0], b, hi[2]], f"{name}_{i + 1}" if len(pieces) > 1 else name, part, tex)
            for i, (a, b) in enumerate(pieces)]


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


def bevel(axis, c, at, pitch_r, cone, n, name, part, tex, toward, phase=0.0, ded=DED):
    """The teeth of a bevel gear about `axis` through c, its pitch circle (radius pitch_r) in the plane `at`
    along the axis, its pitch cone's half angle `cone` (radians) with the apex `toward` (+1 or -1) along the
    axis: n teeth of MODULE (tooth 1 at `phase`), each a box FACE long along the cone's generator, ADD above
    the pitch cone and `ded` below it along the cone's normal and TOOTH_W wide, centred on the pitch circle. As
    the gear cutter's mitre bevels, at any cone angle; the gear's body is drawn by the caller (a deeper `ded`
    sinks the teeth into it, under the mating gear's tips, which run in the gaps)."""
    a = AX[axis]
    u, w = REF[axis]
    out = []
    for i in range(n):
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[a], hi[a] = at - FACE / 2, at + FACE / 2
        lo[u], hi[u] = c[u] + pitch_r - ded, c[u] + pitch_r + ADD
        lo[w], hi[w] = c[w] - TOOTH_W / 2, c[w] + TOOTH_W / 2
        el = box(lo, hi, f"{name}_tooth{i + 1:02d}", part, tex)
        piv = list(c)
        piv[a], piv[u] = at, c[u] + pitch_r
        for sgn in (1.0, -1.0):
            trial = el.clone()
            rotate([trial], "xyz"[w], math.degrees(cone) * sgn, piv)
            if trial.r[a][u] * toward > 0:     # the tooth's height (its local u) leans towards the apex
                el = trial
                break
        rotate([el], axis, math.degrees(phase + TAU * i / n), c)
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
    """An open-topped box (a bin, a hopper, a bunker): four walls and, given `floor`, a floor, with `hole`
    ((x0, x1), (z0, z1)) cut out of the floor."""
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
    """The holes through the crown strip: a gland round each spindle, the feed chute's at the cold end."""
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
    out += frame_bars([fx1, dy0, dz0], [fx1 + 1.0, dy1, dz1], [fx1, dy0 + 1.0, dz0 + 1.0], [fx1 + 1.0, dy1 - 1.0, dz1 - 1.0],
                      "x", "fr_sulfurdoor_frame", f, "iron")
    out.append(box([fx1 + 0.1, dy0 + 1.0, dz0 + 1.0], [fx1 + 0.9, dy1 - 1.0, dz1 - 1.0], "fr_sulfurdoor_plate", f, "iron"))
    out.append(box([fx1 + 0.9, (dy0 + dy1) / 2 - 0.5, dz1 - 3.2], [fx1 + 1.7, (dy0 + dy1) / 2 + 0.5, dz1 - 1.6], "fr_sulfurdoor_handle", f, "iron"))
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


# ---------------------------------------------------------------- builders: level 1
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


def chute_section(cell, y0, name, part):
    """A section of the vanilla chute, up and down, in `cell` (build-frame block coordinates): CHUTE_UD_WALLS from
    y0 up to the cell's top, where a chute placed in the cell above meets it."""
    ox, oy, oz = (cell[k] * B for k in range(3))
    return [box([ox + x0, y0, oz + z0], [ox + x1, oy + B, oz + z1], f"{name}{i}", part, "chute")
            for i, (x0, x1, z0, z1) in enumerate(CHUTE_UD_WALLS, 1)]


def build_hopper():
    """The hopper, fitted at level 1 and kept at level 2: an open iron box, its mouth at the infeed cell's top,
    a slot in its floor over the trough (level 1) or the feed roll (level 2)."""
    (x0, x1), (y0, y1), (z0, z1) = HOPPER
    return open_box([x0, y0, z0], [x1, y1, z1], HOPPER_T, "hopper_bin", "hopper", "iron", hole=HOPPER_SLOT)


def build_trestle():
    """Level 1's trestle: four oak legs on the header carrying the hopper, a rail between each pair, and the
    trough from under the hopper's slot down into the southern stall, on a post on the last partition wall."""
    p = "trestle"
    out = []
    y0 = HOPPER[1][0]
    for i, (lx0, lx1) in enumerate(LEGS_X, 1):
        for j, ((lz0, lz1), foot) in enumerate(zip(LEGS_Z, (PLINTH, HEADER[1][1])), 1):
            out.append(box([lx0, foot, lz0], [lx1, y0, lz1], f"trestle_leg{i}{j}", p, "oak"))
        out.append(box([lx0 + 0.3, LEG_RAIL_Y[0], LEGS_Z[0][1]], [lx1 - 0.3, LEG_RAIL_Y[1], LEGS_Z[1][0]], f"trestle_rail{i}", p, "oak"))
    out.append(box([LEGS_X[0][0] + 0.3, PLINTH + 12.0, LEGS_Z[0][0] + 0.3], [LEGS_X[1][1] - 0.3, PLINTH + 13.4, LEGS_Z[0][1] - 0.3],
                   "trestle_brace", p, "oak"))
    top, bot = TROUGH
    out.append(strut(top, bot, 1.0, TROUGH_W, "trestle_trough", p, "oak"))
    for i, dx in enumerate((-(TROUGH_W / 2 - 0.5), TROUGH_W / 2 - 0.5), 1):
        a = [top[0] + dx, top[1] + 1.2, top[2]]
        b = [bot[0] + dx, bot[1] + 1.2, bot[2]]
        out.append(strut(a, b, 2.6, 1.0, f"trestle_troughside{i}", p, "oak"))
    # the post: from the partition wall's top to just inside the trough's underside
    zc = (TROUGH_POST_Z[0] + TROUGH_POST_Z[1]) / 2
    (tx, ty, tz), (bx, by, bz) = top, bot
    yc = by + (zc - bz) / (tz - bz) * (ty - by) - 0.3
    out.append(box([23.0, ST_TOP, TROUGH_POST_Z[0]], [25.0, yc, TROUGH_POST_Z[1]], "trestle_post", p, "oak"))
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


# ---------------------------------------------------------------- builders: level 2, the furnace
def build_hearth():
    """The hearth's bed (firebrick, the discharge hole through it into the pit) and the bridge wall."""
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
    return out


def door_runs():
    hw, _ = WORK_DOOR
    return [(zc - hw, zc + hw) for zc in RABBLE_Z]


def build_walls():
    """The side walls (red brick, a work door through each beside each rabble) and the fire end's wall (the
    ash pit's clean-out door at its foot), and the firebrick skewbacks on the side walls' tops."""
    p = "walls"
    out = []
    _, (dy0, dy1) = WORK_DOOR
    z0, z1 = FIREBOX_Z[0], HEARTH_Z[1]
    for s, (x0, x1) in zip("we", WALL_X):
        out += wall([x0, PLINTH, z0], [x1, dy0, z1], f"walls_side{s}_foot", p, "brick")
        out += wall([x0, dy1, z0], [x1, SPRING_Y, z1], f"walls_side{s}_head", p, "brick")
        for i, (a, b) in enumerate(runs(z0, z1, door_runs()), 1):
            out += wall([x0, dy0, a], [x1, dy1, b], f"walls_side{s}_pier{i}", p, "brick")
    (ax0, ax1), (ay0, ay1) = ASH_DOOR
    f0, f1 = FRONT_Z
    xw, xe = WALL_X[0][0], WALL_X[1][1]
    out += wall([xw, PLINTH, f0], [ax0, ay1, f1], "walls_front_w", p, "brick")
    out += wall([ax1, PLINTH, f0], [xe, ay1, f1], "walls_front_e", p, "brick")
    out += wall([xw, ay1, f0], [xe, SPRING_Y, f1], "walls_front_top", p, "brick")
    for s, (x0, x1) in zip("we", SKEW_X):
        out += wall([x0, SKEW_Y[0], ARCH_Z[0]], [x1, SKEW_Y[1], ARCH_Z[1]], f"walls_skew{s}", p, "firebrick")
    return out


def build_arch():
    """The firebrick arch over the firebox's back and the hearth: VOUSSOIRS strips round the arch's centre, cut
    at the block grid; the crown strip cut round the spindles' glands and the feed chute. The chute's iron
    seat on the crown."""
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
    (hx0, hx1), (hz0, hz1) = CHUTE
    (sx, sz, sy) = SEAT
    out += frame_bars([sx[0], sy[0], sz[0]], [sx[1], sy[1], sz[1]], [hx0, sy[0], hz0], [hx1, sy[1], hz1], "y", "arch_seat", p, "iron")
    return out


def build_ironwork():
    """The buckstays either side and their tie rods over the arch, with nuts; the skewback bars; each work door's
    frame, its plate raised in its guides (the door open); the ash pit's door on the fire end."""
    p = "ironwork"
    out = []
    for i, zc in enumerate(STAY_Z, 1):
        z0, z1 = zc - STAY_W / 2, zc + STAY_W / 2
        for s, (x0, x1) in zip("we", STAY_X):
            out += bar_y([x0, PLINTH, z0], [x1, STAY_TOP, z1], f"ironwork_stay{i}{s}", p, "iron")
        out += wall([STAY_X[0][0], ROD_Y[0], zc - ROD_T / 2], [STAY_X[1][1], ROD_Y[1], zc + ROD_T / 2], f"ironwork_rod{i}", p, "iron")
        for s, (x0, x1) in zip("we", NUT_X):
            out.append(box([x0, ROD_Y[0] - 0.4, zc - 0.8], [x1, ROD_Y[1] + 0.4, zc + 0.8], f"ironwork_nut{i}{s}", p, "iron"))
    for s, (x0, x1) in zip("we", SKEWBAR_X):
        out += wall([x0, SKEW_Y[0], ARCH_Z[0]], [x1, SKEW_Y[1], ARCH_Z[1]], f"ironwork_skewbar{s}", p, "iron")
    fr = DOOR_FRAME
    hw, (y0, y1) = WORK_DOOR
    (py0, py1) = DOOR_PLATE_Y
    for k, zc in enumerate(RABBLE_Z, 1):
        for s, (wx0, wx1) in zip("we", WALL_X):
            face = wx0 if s == "w" else wx1
            sgn = -1.0 if s == "w" else 1.0

            def xs(d0, d1):
                return sorted((face + sgn * d0, face + sgn * d1))
            fa, fb = xs(0.0, 0.8)
            out += frame_bars([fa, y0 - fr, zc - hw - fr], [fb, y1 + fr, zc + hw + fr], [fa, y0, zc - hw], [fb, y1, zc + hw], "x",
                              f"ironwork_door{k}{s}_frame", p, "iron")
            ga, gb = xs(0.0, 2.0)
            for g, (gz0, gz1) in enumerate(((zc - hw - fr - 1.0, zc - hw - fr), (zc + hw + fr, zc + hw + fr + 1.0)), 1):
                out.append(box([ga, y0 - fr, gz0], [gb, py1 + 0.5, gz1], f"ironwork_door{k}{s}_guide{g}", p, "iron"))
            pa, pb = xs(0.8, 1.8)
            out.append(box([pa, py0, zc - hw - fr - 0.5], [pb, py1, zc + hw + fr + 0.5], f"ironwork_door{k}{s}_plate", p, "iron"))
    (ax0, ax1), (ay0, ay1) = ASH_DOOR
    f0 = FRONT_Z[0]
    out += frame_bars([ax0 - fr, ay0, f0 - 0.8], [ax1 + fr, ay1 + fr, f0], [ax0, ay0, f0 - 0.8], [ax1, ay1, f0], "z",
                      "ironwork_ashframe", p, "iron")
    out.append(box([ax0, ay0, f0 - 0.7], [ax1, ay1, f0 - 0.1], "ironwork_ashdoor", p, "iron"))
    return out


def steps():
    """The step grate's treads, top first: (z0, z1, top)."""
    return [(STEP_Z0 + STEP_D * i, STEP_Z0 + STEP_D * (i + 1), STEP_TOP0 - STEP_DROP * i) for i in range(STEPS)]


def build_firebox():
    """The firing: a step grate of STEPS iron treads across the firebox, each lower than the last towards the
    bridge, built into the side walls (air comes in between them from the ash pit); over its top tread, the
    throat of the coal bunker on the fire end, so the coal slides down the steps as it burns."""
    p = "firebox"
    out = []
    for i, (z0, z1, top) in enumerate(steps(), 1):
        out.append(box([IN_X[0], top - STEP_T, z0], [IN_X[1], top, z1], f"firebox_step{i}", p, "iron"))
    (x0, x1), (y0, y1), (z0, z1) = BUNKER
    out += open_box([x0, y0, z0], [x1, y1, z1], BUNKER_T, "firebox_bunker", p, "iron", hole=BUNKER_SLOT)
    (sx0, sx1), (sz0, sz1) = BUNKER_SLOT
    t = BUNKER_T
    out += tube([sx0 - t, THROAT_Y0, sz0 - t], [sx1 + t, y0, sz1 + t], t, "y", "firebox_throat", p, "iron")
    out += chute_section(FUEL_CELL, FUEL_SECTION_Y, "firebox_fuelchute", p)
    return out


def build_charge():
    """The charge on the hearth: a bed of ore, open round the discharge hole."""
    p = "charge"
    (dx0, dx1), (dz0, dz1) = DISCHARGE
    x0, x1 = IN_X[0] + 1.0, IN_X[1] - 1.0
    z0, z1 = HEARTH_Z[0] + 0.5, HEARTH_Z[1] - 0.5
    y0, y1 = HEARTH_Y, HEARTH_Y + 1.0
    return [box([x0, y0, z0], [x1, y1, dz0 - 0.5], "charge_n", p, "ore"),
            box([x0, y0, dz0 - 0.5], [dx0 - 0.5, y1, dz1 + 0.5], "charge_w", p, "ore"),
            box([x0, y0, dz1 + 0.5], [x1, y1, z1], "charge_s", p, "ore")]


def build_fire():
    """The fire: a burning bed on each tread of the step grate, the coal in the bunker's throat on the top
    tread, and the coal in the bunker."""
    p = "fire"
    out = []
    for i, (z0, z1, top) in enumerate(steps(), 1):
        out.append(box([IN_X[0] + 1.0, top, z0 + 0.3], [IN_X[1] - 1.0, top + 1.5, z1 - 0.3], f"fire_bed{i}", p, "fire"))
    (sx0, sx1), (sz0, sz1) = BUNKER_SLOT
    top = STEP_TOP0 + 1.5
    out.append(box([sx0 + 0.2, top, sz0 + 0.2], [sx1 - 0.2, BUNKER[1][0] + BUNKER_T, sz1 - 0.2], "fire_coalthroat", p, "coal"))
    (x0, x1), (y0, _), (z0, z1) = BUNKER
    t = BUNKER_T
    out.append(box([x0 + t, y0 + t, z0 + t], [x1 - t, y0 + t + 6.0, z1 - t], "fire_coalbunker", p, "coal"))
    return out


# ---------------------------------------------------------------- builders: level 2, the drive and the rabbles
SHAFT_C = (SHAFT[0], SHAFT[1], 0.0)


def pinion_z(zs):
    """Where a spindle's pinion sits on the line shaft: its pitch circle a wheel's pitch radius north of the
    spindle, where the two pitch cones' apex is the axes' meeting point."""
    return zs - RW


def build_drive():
    """The drive: the oak entry from the power face (the vanilla axle's cross) into the gear case, whose rectifier
    turns the line shaft one way whichever way the axle turns; the iron line shaft along the roof's middle to the
    feeder's casing, a bevel pinion for each rabble's wheel; its pedestals on the arch."""
    p = "lineshaft"
    x, y = SHAFT
    z0, z1 = ENTRY_Z
    out = [box([x - 1.5, y - 0.6, z0], [x + 1.5, y + 0.6, z1], "entry_a", "entry", "oak"),
           box([x - 0.6, y - 1.5, z0], [x + 0.6, y + 1.5, z1], "entry_b", "entry", "oak")]
    (cx0, cx1), (cy0, cy1), (cz0, cz1) = GEARCASE
    (bx0, bx1), (by0, by1), (bz0, bz1) = GEARCASE_BASE
    br, bl = GEARCASE_BOSS
    out += wall([bx0, by0, bz0], [bx1, by1, bz1], "gearcase_foot", "gearcase", "iron")
    out += wall([cx0, cy0, cz0], [cx1, cy1, cz1], "gearcase_case", "gearcase", "iron")
    out += disc("z", SHAFT_C, cz0 - bl, cz0, br, "gearcase_bossn", "gearcase", "iron", k=6)
    out += disc("z", SHAFT_C, cz1, cz1 + bl, br, "gearcase_bosss", "gearcase", "iron", k=6)
    out += disc("z", SHAFT_C, *SHAFT_Z, SHAFT_R, "lineshaft_rod", p, "iron")
    for k, zs in enumerate(RABBLE_Z, 1):
        zp = pinion_z(zs)
        # the body follows the teeth's roots, which close in towards the apex (south), in steps
        for j, (a, b, r) in enumerate(PINION_BODY, 1):
            out += disc("z", SHAFT_C, zp + a, zp + b, r, f"lineshaft_pinion{k}_body{j}", p, "iron", k=6)
        out += bevel("z", SHAFT_C, zp, RP, CONE_P, NP, f"lineshaft_pinion{k}", p, "iron", toward=1.0, phase=math.pi, ded=PINION_DED)
    q = "pedestals"
    for k, (a, b) in enumerate(PEDESTALS, 1):
        out.append(box([PED_BASE_X[0], CROWN_Y[1], a], [PED_BASE_X[1], CROWN_Y[1] + 1.0, b], f"pedestal{k}_base", q, "iron"))
        out.append(box([PED_COL_X[0], CROWN_Y[1] + 1.0, a + 0.6], [PED_COL_X[1], y - PED_BEARING, b - 0.6], f"pedestal{k}_column", q, "iron"))
        out.append(box([x - PED_BEARING, y - PED_BEARING, a], [x + PED_BEARING, y + PED_BEARING, b], f"pedestal{k}_bearing", q, "iron"))
    return out


def build_rabbles():
    """Each rabble: a spindle through a gland in the arch's crown and a bearing on the roof, a bevel wheel on
    its top (teeth up, its line shaft pinion on its north rim), and in the furnace a hub with two arms, each
    with three ploughs set across it."""
    out = []
    for k, zs in enumerate(RABBLE_Z, 1):
        p = f"rabble{k}"
        c = (SHAFT[0], 0.0, zs)
        out += disc("y", c, *SPINDLE_Y, SPINDLE_R, f"{p}_spindle", p, "iron")
        out += disc("y", c, WHEEL_HUB[0], WHEEL_HUB[1], WHEEL_HUB[2], f"{p}_wheelhub", p, "iron")
        out += disc("y", c, WHEEL_BODY[0], WHEEL_BODY[1], WHEEL_BODY[2], f"{p}_wheelbody", p, "iron", k=6)
        # a gap faces north (the pinion's tooth 1) at theta 0
        out += bevel("y", c, WHEEL_Y, RW, CONE_W, NW, f"{p}_wheel", p, "iron", toward=1.0, phase=math.pi + math.pi / NW, ded=WHEEL_DED)
        out += disc("y", c, RABBLE_HUB[0], RABBLE_HUB[1], RABBLE_HUB[2], f"{p}_hub", p, "iron")
        rest = (k - 1) * math.pi / 2                             # the second rabble a quarter turn on from the first
        for a, arm in enumerate((0.0, math.pi), 1):
            ang = rest + arm
            els = [radial("y", c, ARM_Y[0], ARM_Y[1], RABBLE_HUB[2] - 0.4, ARM_R, ARM_W, 0.0, f"{p}_arm{a}", p, "iron")]
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


def build_feeder():
    """The feeder under the hopper: a fluted roll keyed on the line shaft's end turns in a casing under the
    hopper's slot, carrying the charge over and dropping it down the feed chute through the arch; the casing
    stands on its flange on the chute's seat, and its end walls are the shaft's last bearings."""
    out = disc("z", SHAFT_C, *ROLL_Z, ROLL_R, "feedroll_body", "feedroll", "iron", k=6)
    for i in range(ROLL_FLUTES):
        out.append(radial("z", SHAFT_C, ROLL_Z[0] + 0.1, ROLL_Z[1] - 0.1, ROLL_R - 0.4, ROLL_FLUTE_R, 1.0, TAU * i / ROLL_FLUTES + math.pi / ROLL_FLUTES,
                          f"feedroll_flute{i + 1}", "feedroll", "iron"))
    p = "feeder"
    (x0, x1), (y0, y1), (z0, z1) = CASING
    t = CASING_T
    out += [box([x0, y0, z0], [x0 + t, y1, z1], "feeder_casing_w", p, "iron"),
            box([x1 - t, y0, z0], [x1, y1, z1], "feeder_casing_e", p, "iron"),
            box([x0 + t, y0, z0], [x1 - t, y1, z0 + t], "feeder_casing_n", p, "iron"),
            box([x0 + t, y0, z1 - t], [x1 - t, y1, z1], "feeder_casing_s", p, "iron")]
    (fx, fz, fy) = FLANGE
    (hx0, hx1), (hz0, hz1) = CHUTE
    out += frame_bars([fx[0], fy[0], fz[0]], [fx[1], fy[1], fz[1]], [hx0, fy[0], hz0], [hx1, fy[1], hz1], "y", "feeder_flange", p, "iron")
    out += tube([hx0, CHUTE_Y[0], hz0], [hx1, CHUTE_Y[1], hz1], CHUTE_T, "y", "feeder_chute", p, "iron")
    out += chute_section(INFEED_CELL, INFEED_SECTION_Y, "feeder_infeedchute", p)
    return out


def build():
    els = build_drive() + build_rabbles() + build_feeder()
    els += build_hearth() + build_walls() + build_arch() + build_ironwork() + build_firebox() + build_charge() + build_fire()
    els += build_stalls() + build_stallflue() + build_trestle() + build_hopper() + build_heaps()
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
    """The parts in the levels' order (the viewer lists the requires values in the order the parts first name
    them): level 1's, the hopper both share, level 2's, then the frame."""
    shaft = pt(SHAFT[0], SHAFT[1], 0.0)
    parts = [static("stalls"), static("stallflue"), static("trestle"), static("heaps"), static("hopper"),
             static("hearth"), static("walls"), static("arch"), static("ironwork"), static("firebox"),
             # the entry turns with the axle (theta), either way; everything beyond the gear case's rectifier reads the
             # axle's travel (psi), which only grows, so it turns forward whichever way the axle turns
             {"id": "entry", "match": ["entry_*"], "requires": "drive",
              "drivers": [{"type": "rotate", "axis": "z", "pivot": shaft, "ratio": 1.0}]},
             static("gearcase", "drive"),
             {"id": "lineshaft", "match": ["lineshaft_*"], "requires": "drive",
              "drivers": [{"type": "rotate", "axis": "z", "pivot": shaft, "ratio": 1.0, "input": "travel"}]},
             static("pedestals", "drive", "pedestal*")]
    for k, zs in enumerate(RABBLE_Z, 1):
        parts.append({"id": f"rabble{k}", "match": [f"rabble{k}_*"], "requires": "rabbles",
                      "drivers": [{"type": "rotate", "axis": "y", "pivot": pt(SHAFT[0], 0.0, zs), "ratio": r6(RATIO), "input": "travel"}]})
    parts += [static("rabblemounts", "rabbles", "rabblemount*"),
              {"id": "feedroll", "match": ["feedroll_*"], "requires": "feeder",
               "drivers": [{"type": "rotate", "axis": "z", "pivot": shaft, "ratio": 1.0, "input": "travel"}]},
              static("feeder"), static("charge"), static("fire"),
              {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []}]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


MOVING = ("entry", "lineshaft", "rabble1", "rabble2", "feedroll")
RECTIFIED = ("lineshaft", "rabble1", "rabble2", "feedroll")   # beyond the rectifier: forward either way


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


ANCHORS = ("infeed", "output", "fuel", "charge", "discharge", "sulfur", "smoke")
CELL_ANCHORS = ("powerCell", "infeedCell", "outputCell", "fuelCell")


def make_rig(parts):
    (hx0, hx1), (hz0, hz1) = CHUTE
    (fx0, fx1), _, _ = FLUEBOX
    (dz0, dz1), (dy0, dy1) = SULFUR_DOOR
    (cx0, cx1), (cz0, cz1), (cy0, cy1) = CHANNEL
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the middle of the fire end, "
                    "nearest the player who placed it; the roaster runs south to the stack. One of the ore line's upgradeable "
                    "machines (#711): the cells, the power cell, the infeed, the output spout and the stack are the frame's and "
                    "never move; levels lists the requires values each level fits (level 2 keeps level 1's hopper and pulls down "
                    "its stalls, flue and trestle), contents what each shows while roasting. A cell's boxes are the frame's, its "
                    "fitted boxes those of each requires value while it is fitted. The entry shaft reads theta, the axle's angle; "
                    "everything beyond the gear case's rectifier reads the axle's travel, so it turns forward either way. "
                    "See the roaster's README for the schema.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "infeedCell": list(INFEED_CELL),
        "infeedFace": INFEED_FACE,
        "outputCell": list(OUTPUT_CELL),
        "outputFace": OUTPUT_FACE,
        "fuelCell": list(FUEL_CELL),
        "fuelFace": FUEL_FACE,
        "infeed": {"pos": pt((INFEED_CELL[0] + 0.5) * B, (INFEED_CELL[1] + 1) * B, (INFEED_CELL[2] + 0.5) * B)},
        "output": {"pos": pt(cx1, (cy0 + cy1) / 2, (cz0 + cz1) / 2)},
        "fuel": {"pos": pt((FUEL_CELL[0] + 0.5) * B, (FUEL_CELL[1] + 1) * B, (FUEL_CELL[2] + 0.5) * B)},
        "charge": {"pos": pt((hx0 + hx1) / 2, HEARTH_Y, (hz0 + hz1) / 2)},
        "discharge": {"pos": pt((DISCHARGE[0][0] + DISCHARGE[0][1]) / 2, HEARTH_Y, (DISCHARGE[1][0] + DISCHARGE[1][1]) / 2)},
        "sulfur": {"pos": pt(fx1 + 1.0, (dy0 + dy1) / 2, (dz0 + dz1) / 2)},
        "sulfurSide": "east",
        "smoke": {"pos": pt(SHAFT[0], STACK_TOP, (STACK_XZ[1][0] + STACK_XZ[1][1]) / 2)},
        "levels": {k: list(v) for k, v in LEVELS.items()},
        "contents": {k: list(v) for k, v in CONTENTS.items()},
        "gearing": {"rabbleTurnsPerAxleTurn": r6(abs(RATIO)), "feedRollTurnsPerAxleTurn": 1, "pinionTeeth": NP, "wheelTeeth": NW,
                    "_comment": "As drawn: each rabble's bevel wheel is driven from its north rim by a bevel pinion on the line "
                                f"shaft ({NP}:{NW}), so the rabbles turn {abs(RATIO):.4g} as fast as the axle, against it; the feed "
                                "roll is keyed on the shaft's end, one turn a turn; the line shaft turns once a turn of the axle, "
                                "forward either way (the gear case's rectifier, 1:1). A renderer has nothing to read here: the "
                                "parts' drivers carry it."},
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


def requires_order(parts):
    out = []
    for p in parts:
        if p["requires"] is not None and p["requires"] not in out:
            out.append(p["requires"])
    return out


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig (every reader rebuilds
    them the same way): each cell's `boxes` from the frame's elements, and its `fitted` boxes, by requires value,
    from the elements of the parts that need it; then the lids."""
    written = flatten(shape["elements"], textures={})
    req = {p["id"]: p["requires"] for p in ship_parts}
    by_cell = {}
    for w in written:
        pid = part_of(ship_parts, w.name)
        el = posed(w, _part_matrix(ship_parts, pid, {"theta": 0.0}))
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, {}).setdefault(req[pid], []).append(el)
    order = requires_order(ship_parts)
    out = []
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        groups = by_cell.get(pos, {})
        cell = {"pos": list(pos)}
        frame = cell_boxes(groups[None], pos) if None in groups else None
        if frame:
            cell["boxes"] = frame
        fitted = {}
        for r in order:
            boxes = cell_boxes(groups[r], pos) if r in groups else None
            if boxes:
                fitted[r] = boxes
        if fitted:
            cell["fitted"] = fitted
        out.append(cell)
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
          f"output {ship['outputCell']} {ship['outputFace']}, fuel {ship['fuelCell']} {ship['fuelFace']}")
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
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Both levels' parts are in this one "
             "shape, the stalls of level 1 where the furnace of level 2 stands: a renderer draws the fitted level's. The fire's "
             "texture code is 'fire': a renderer may set it to ember while the furnace burns. Keep element names when editing: "
             "the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


COPLANAR_THETAS = (0.0, 0.4, 1.3, 2.9)


def coplanar_poses():
    """The poses the z-fighting fix and its check look at: each state at rest, and level 2 at a few angles.
    Never both levels at once: the stalls stand where the furnace does, and are never drawn with it."""
    return ((0.0, "level2"), (0.0, "level1"), (0.0, "frame")) + tuple((th, "level2") for th in COPLANAR_THETAS[1:])


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
    """machinegen's fix, once with each level's rest pose first: the fix removes the faces pressed against their
    own part at its first pose only, and each part is in place only in its own states."""
    def fn(es, pose):
        return shown([posed(el, pm(parts, el.part, pose)) for el in es], pose, parts)
    poses = coplanar_poses()
    before, hidden = {}, 0
    for first in ("level2", "level1"):
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
