#!/usr/bin/env python3
"""Generate the reverberatory roaster's shapes, rig and reference poses.

The reverberatory roaster roasts sulfide concentrate into roasted concentrate (behaviour #736, model #737): a long,
low brick furnace with a curved roof. A fire at one end throws its flame over a bridge wall and along a flat hearth
where the charge lies, and up an uptake at the other end to the outlet the player's chimney stands on. It is one real
piece of equipment with two levels, a level being only parts fitted to that same furnace:

    level 1   batch, rabbled by hand: the player charges through the hopper's mouth on the roof, stokes coal through
              the fire door onto a flat grate, rabbles the charge through the work doors on the east side (drawn
              open) and draws it at the discharge door at the foot of that side. Natural draught, in at the ash mouth
              under the grate (an iron damper over it, open, half or shut).
    level 2   continuous: two water-cooled mechanical rabbles on spindles through the arch, turned by bevel gears from
              a line shaft on the roof, which a rectifier in a closed gear case at the fire end turns one way whichever
              way the vanilla axle turns; a fluted feed roll on the line shaft's end under the hopper; a coal bunker in
              the fire end's well over a step grate (in place of the flat grate), filled by a chute; the infeed, fuel
              and output chutes are sections of the vanilla chute; water comes by ppex pipe to swivels on the
              spindles. The work doors stay, shut.

It follows the crucible furnace's conventions (CrucibleFurnace/README.md): one unit; units stand back to back, two
in a row, sharing their west (plain, flush) long wall, the flue between them implied; the model stops at an outlet
on the top (open with the chimney on it, or capped flush); the brickwork takes any fired brick, the hot faces are
lined with tier-3 refractory brick 3 voxels thick, and the arch is that refractory; it is built in stages. Every
element is plain boxes made for this mod; no other mod's model is used.

It writes, deterministically,

    reverberatory.json           the whole furnace, every part of both levels    (assets/.../shapes/block/)
    reverberatory_frame.json     the iron frame only (block and item)            (assets/.../shapes/block/)
    reverberatory-rig.json       cells, anchors, the build and the part rig      (assets/.../config/)
    rig-reference.json           every part's matrix at a grid of poses          (tests/ReverberatoryRoaster/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_reverberatory.py) and exits non-zero
if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the machine box's
north-west-bottom corner (the "build frame"). The furnace runs along z: the fire end (north, nearest the player who
placed it) to the flue end (south). The controller cell, the middle of the fire end at ground level (the ash mouth's
and the fire door's), is the build frame's cell (1, 0, 0): the shipped files are the build frame moved one block west.

The rig's inputs, as this machine uses them:

    theta  the axle angle, either way: the entry shaft into the gear case
    psi    the axle's travel, which only grows ("input": "travel"): beyond the gear case's rectifier, the line shaft,
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
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Pipes" / "tools"))
sys.path.insert(0, str(Path(__file__).resolve().parent))

import solderedpipe  # noqa: E402
from machinegen.checks import cell_boxes, cells_touched, with_lids  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import IDENT, El, flatten, rotate  # noqa: E402
from machinegen.output import (reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               shift_point, worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "ReverberatoryRoaster" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/ReverberatoryRoaster/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

# ---------------------------------------------------------------- the box, the cells, the anchors
CELLS_X, CELLS_Y, CELLS_Z = 3, 3, 5          # the machine box (blocks); the footprint is the cells below, not all of it
BODY_Y = 2                                   # the body: every cell x 0..2, y 0..1, z 0..4 (30 cells)
W, H, D = CELLS_X * B, BODY_Y * B, CELLS_Z * B   # 48, 32, 80
TOP = H                                      # the body's top: the end blocks' flat tops, the decks
ORIGIN_CELL = (1, 0, 0)                      # the controller: the middle of the fire end at ground level
DRIVE_CELLS = tuple((1, 2, z) for z in range(CELLS_Z))   # the drive's row on the roof's middle, fixed from the frame on
POWER_CELL, POWER_FACE = (1, 2, 0), "north"  # the vanilla axle comes in along z over the fire end, at the cell's centre (24, 40)
INFEED_CELL, INFEED_FACE = (1, 2, 4), "up"   # the hopper's mouth: charged by hand (level 1) or by a chute (level 2)
OUTPUT_CELL, OUTPUT_FACE = (2, 0, 2), "east" # the discharge tunnel's mouth: the discharge door (level 1), the output chute (2)
FUEL_CELL, FUEL_FACE = (0, 1, 0), "up"       # the coal bunker's chute (level 2), in the fire end's west corner
WATER_CELL, WATER_FACE = (1, 2, 2), "up"     # the water: a ppex pipe from above onto the manifold (level 2)
OUTLET_CELL = (0, 1, 4)                      # the outlet: the flue end's west corner, beside the drive's row, not under it
CHIMNEY_CELL = (0, 2, 4)                     # the chimney's first block, on the outlet: never the furnace's
ROW_MAX = 2                                  # two stand back to back, the second turned half round, sharing the west wall

TEXTURES = {
    "brick": "game:block/clay/brick/four/running/red1",     # the brickwork: the red brick's colour (BRICKS); the renderer's
    "lining": "game:block/clay/refractory/tier3/front1",    # the hot faces, the arch and its skewback: tier-3 refractory
    "iron": "game:block/metal/plate/iron",                  # the frame, the ironwork, the doors, the drive, the rabbles
    "oak": "game:block/wood/debarked/oak",                  # the entry shaft: the vanilla axle's continuation
    "ore": "game:block/pile/oldore",                        # the charge on the hearth
    "coal": "game:block/coal/bituminous",                   # the coal in the bunker and its throat (level 2)
    "ember": "game:block/coal/ember",                       # the fire: the burning bed on the grate, glowing
    "chute": "game:block/metal/sheet/copper1",              # the vanilla chute's sections (a placed chute's texture)
    "pipecopper": "game:block/metal/sheet-plain/copper4",   # the water pipe in copper (UnifiedPipes' pipe texture)
    "pipelead": "game:block/metal/sheet-plain/lead4",       # ... or in lead
    "solder": solderedpipe.SOLDER_TEXTURE,                  # the pipe's joints
}
TEX = 16                                     # shape texture units: one a voxel, one texture across a block face
FIRE_GLOW = 200                              # the burning bed's faces (the crucible furnace's coke: 200)

# ---------------------------------------------------------------- the walls, the hearth
WEST = (0.0, 8.0)                            # the shared long wall: plain brick, flush, to the top the whole length
IN_X = (8.0, 40.0)                           # the firebox, the hearth and the flue end's cavity, between the walls
EAST = (40.0, 46.0)                          # the door side's wall; the end blocks' east faces at 46
FACE_E = EAST[1]
HEARTH_Y = 14.0                              # the hearth's floor; the footing's top
FOOT = ((40.0, W), (0.0, HEARTH_Y))          # the footing under the east wall, out to the footprint's face (x, y)
SPRING_Y = 22.0                              # the side walls' tops in the arched part: the arch springs here
FIRE_BLOCK = (0.0, 16.0)                     # z: the fire end block, flat-topped, to TOP
FLUE_BLOCK = (64.0, 80.0)                    # z: the flue end block, flat-topped, to TOP
ARCH_Z = (FIRE_BLOCK[1], FLUE_BLOCK[0])      # the arch between them
FRONT_Z = (0.0, 6.0)                         # the fire end wall
FIREBOX_Z = (6.0, 20.0)
BRIDGE_Z = (20.0, 25.0)
BRIDGE_TOP = 20.0
HEARTH_Z = (25.0, 74.0)                      # to the flue end wall
END_Z = (74.0, 80.0)                         # the flue end wall
ASHPIT_Y = (1.0, 10.0)                       # the ash pit under the grate, on a course of brick
CAVITY_TOP = 27.0                            # the flue end's cavity under its flat roof: the arch's crown intrados
LINING_T = 3.0                               # the lining: tier-3 refractory brick on every hot face

# the arch: segmental, over IN_X, VOUSSOIRS strips of refractory round its centre, springing at SPRING_Y
RISE, ARCH_T, VOUSSOIRS = 5.0, 4.0, 9
SKEW_X = (39.0, 45.25)                       # the east skewback (x), refractory, on the east wall's top, to the skewback
#                                              angle's face leg; its top is the arch's foot (SKEW, under the arch); the
#                                              west foot is built into the shared wall

# ---------------------------------------------------------------- the fire end: ash mouth, damper, fire door, grates, well
MOUTH = ((18.0, 30.0), (1.0, 6.5))           # the ash mouth, centred in the fire end face (x, y), a passage into the ash pit
RECESS = ((17.0, 31.0), (0.0, 13.0), (0.0, 1.0))   # the damper's recess in the face, its slide, from the ground to the
#                                                    fire door's recess (no course of brick under it or between them)
SILL = (0.0, 1.0)                            # (y) the damper's iron sill, between the guides under the ash mouth
GUIDES = ((17.0, 18.0), (30.0, 31.0))        # its iron guides (x), the recess's full height and depth, on the ground
ASH_LINTEL = ((17.0, 31.0), (6.5, 7.5), (1.0, 2.0))  # the iron lintel over the ash mouth, behind the recess
DAMPER_Z = (0.35, 0.85)
DAMPER_H = 6.0
DAMPER = {"open": 7.0, "half": 3.75, "shut": 1.0}   # the plate's underside in each place: clear of the mouth (to the recess's
#                                                     top, so no brick shows over it), over its upper
#                                                     half, over all of it
KNOB = ((23.0, 25.0), (0.1, 0.35), (2.4, 3.6))   # the damper's knob (x, z, y over its underside), in the recess
FIRE_DOOR = ((19.0, 29.0), (14.0, 20.0))     # the fire door's opening through the fire end wall (x, y), over the flat grate
FD_RECESS = ((18.0, 30.0), (13.0, 21.0), (0.0, 1.0))   # its recess in the face: the door shuts in it, flush
FD_LINTEL = ((18.0, 30.0), (20.0, 21.0), (1.0, 3.0))   # the iron lintel over the opening
FD_SILL = ((18.0, 30.0), (13.0, 14.0), (1.0, 3.0))     # the dead plate: the iron sill of the opening, the recess wide, to the lining
# the flat grate (level 1): bars along z on two bearers across the firebox, the bearers' ends built into the side walls
BEARER_Y = (8.5, 10.0)
BEARER_Z = ((7.0, 8.5), (17.5, 19.0))
BAR_Y = (10.0, 11.0)
BARS, BAR_W = 14, 1.2                        # across the 32 of the firebox: gaps of 1.17
BAR_Z = (6.5, 19.5)
# the step grate (level 2): treads across the firebox, each lower than the last towards the bridge
STEPS = 4
STEP_Z0, STEP_D = FIREBOX_Z[0], 3.5
STEP_TOP0, STEP_DROP, STEP_T = 18.5, 2.0, 1.0
# the well in the fire end block's top: the coal bunker's place (level 2), covered flush at level 1
WELL = ((3.0, 41.0), (25.0, 31.5), (3.0, 13.0))
WELL_SEAT = ((2.0, 44.0), (31.5, TOP), (2.0, 14.0))   # the cover's seat round its mouth, out to the binding's top legs
THROAT = ((9.0, 39.0), (SPRING_Y, 25.0), (7.0, 12.0))   # the slot from the well's floor through the firebox's roof
LINER_T = 0.5                                # the bunker's iron liner (level 2)
THROAT_FOOT = 20.0                           # the iron throat hangs to here, over the top tread

# ---------------------------------------------------------------- the east side: work doors, discharge
RABBLE_Z = (39.0, 60.0)                      # the two spindles, on the hearth's middle line; a work door at each
WORK_DOOR = (5.0, (15.0, 21.0))              # half width (z) about each rabble, and y: through the east wall
DOOR_FRAME = 1.0
DOOR_PLATE = {"open": (22.5, 29.5), "shut": (14.5, 21.5)}   # the plates' y: raised in their guides, or down over the opening
GUIDE_TOP = 30.0
PEEP_DOOR = 1                                # the work door with a peep hole: shut (level 2), the rabble is seen through it
PEEP = (1.75, (1.6, 5.0))                    # the hole: half width (z) about the door's middle, and y over the plate's foot
PEEP_SHUTTER = (0.1, 0.3, 0.2)               # its shutter: thickness, lap over the hole each way, gap when slid clear (z)
PEEP_GUIDE = (0.15, 0.4)                     # the shutter's two guides over and under it: thickness (x, out to the handles'
#                                              face, inside the footprint), height (y)
DISCHARGE = ((30.0, 36.0), (37.0, 43.0))     # (x, z) the discharge hole through the hearth, in the first rabble's sweep
TUNNEL = ((30.0, W), (4.0, 12.0), (36.0, 44.0))   # the tunnel under it to the east face: the output chute's bore and walls
D_RECESS = ((46.5, W), (3.5, 12.0), (36.0, 52.0))   # the discharge door's recess in the footing's face: it slides along z,
#                                              under the footing band's face leg; twice the mouth's length, its low rail's
#                                              top the tunnel's floor (no strip of brick shows round the plate or the chute)
D_SLOT = ((46.5, W - 0.75), (12.0, 12.5))    # (x, y) the plate's top edge runs in a slot behind the band's face leg, its lip
D_RAIL = 0.5                                 # the low rail the plate runs on
D_PLATE = {"shut": (36.0, 44.0), "open": (44.0, 52.0)}   # the plate's z: over the mouth, or slid south clear of it
D_PLATE_X = (46.65, 47.15)
D_PLATE_Y = (D_RECESS[1][0] + D_RAIL, D_SLOT[1][1])

# ---------------------------------------------------------------- the flue end: uptake, outlet, feed hole
UPTAKE = ((8.0, 14.0), (68.0, 74.0))         # (x, z) the bore from the cavity's roof to the outlet, its south-west corner
CURB = ((2.0, 18.0), (66.0, 78.0))           # the outlet's curb, the lining's brick, flush: from the binding's top legs (west,
#                                              north, south) round the cap's seat, so no brickwork shows narrower than a brick
CAP_Y = (31.5, TOP)
CAP_SEAT = ((7.0, 15.0), (66.0, 75.0))       # the cap's seat, in the curb against the inner band's top leg
FEED_HOLE = ((21.5, 26.5), (69.0, 73.0))     # (x, z) the feed chute's hole through the flue end block's roof
MIN_CHIMNEY = 6                              # chimney blocks stood on the outlet, counted from it up (the crucible furnace's)

# ---------------------------------------------------------------- the iron: the box frame, buckstays, rods
# The binding is angle iron (the owner's rule for every furnace): an L section wrapping the brickwork's corner, one leg on
# each face, its inside corner bedded against the brick's corner, so it holds the brick in on both faces. It is a closed
# box frame following the brickwork's outline (binding_members): an angle up every outside vertical corner and an angle
# band along every top edge, each meeting the next at its ends. The end faces are the footprint's and the west face is
# the shared wall's, so nothing may stand proud of them: every leg is set into the brick, its outer face flush.
ANGLE_T = 0.75                               # angle iron: a leg's thickness
ANGLE_L = 2.0                                # and its width, out from the corner along its face
ANGLE_FACES = (("n", 0.0, 1.0), ("s", D, -1.0))   # the end faces: (name, z, inward)
ANGLE_SIDES = (("w", WEST[0], 1.0, (0.0, TOP)),   # the corners of each: (name, x, inward, y span): the shared wall's,
               ("e", FACE_E, -1.0, (HEARTH_Y, TOP)),   # the end block's east corner over the footing,
               ("f", W, -1.0, (0.0, HEARTH_Y)))        # the footing's east corner
INNER_FACES = (("n", FIRE_BLOCK[1], 1.0), ("s", FLUE_BLOCK[0], -1.0))   # the end blocks' inner faces over the arch: (name,
#                                              z, outward into the arch's bay)
MIN_BRICK = 3.0                              # no piece of brick showing narrower than this either way (check_slivers)
STAY_Z = (21.5, 30.75, 51.75)                # the east buckstays (centres): clear of the pedestals and the brackets, the
#                                              second and third against the work doors' north guides (no sliver of wall)
STAY_X = (FACE_E, FACE_E + ANGLE_T)          # each an angle: its flange on the east face (x), 2.5 wide (z),
STAY_W = 2.5
STAY_Y = (HEARTH_Y, TOP)                     # its web standing out from the flange's south edge to the footprint's face
ROD_Y = (31.4, TOP)                          # the tie rods over the arch's crown (31), from the east buckstays into the shared
ROD_T = 0.8                                  # wall, through the west inner band, each ending on an anchor plate in its top
ANCHOR = ((ANGLE_L, ANGLE_L + 1.0), (29.5, TOP), 1.5)   # the anchor plates: x (against the west band's top leg), y, half width
ROD_X = ((ANCHOR[0][0] + ANCHOR[0][1]) / 2, STAY_X[1] + 1.0)   # from the anchor through the buckstay's flange and its nut
NUT = ((STAY_X[1], STAY_X[1] + 1.0), (31.0, TOP), 0.5)   # on the flange, against the web

# ---------------------------------------------------------------- the vanilla chute (the game's blocktypes/metal/chute-straight.json,
# shapes/block/metal/chute/straight.json): an 8 x 8 square tube centred in its cell, walls 1 thick, along x as drawn (its
# "we" variant, rotateY 0). Up and down (its "ud" variant, turned 90 about z) its walls in cell-local (x, z): north and south
# across the whole width, west and east between them. Every chute this machine takes ends in a section of it, so a player's
# chute line meets the machine with no gap or step.
CHUTE_UD_WALLS = ((4.0, 12.0, 4.0, 5.0), (4.0, 12.0, 11.0, 12.0), (4.0, 5.0, 5.0, 11.0), (11.0, 12.0, 5.0, 11.0))   # (x0, x1, z0, z1)
CHUTE_WE_WALLS = ((4.0, 12.0, 4.0, 5.0), (4.0, 12.0, 11.0, 12.0), (4.0, 5.0, 5.0, 11.0), (11.0, 12.0, 5.0, 11.0))   # (y0, y1, z0, z1)
INFEED_SECTION_Y = 44.0                      # the ore infeed's hangs from the cell's top into the hopper's mouth
FUEL_SECTION_Y = 28.5                        # the fuel infeed's from the fuel cell's top into the bunker
OUTPUT_SECTION_X = 40.0                      # the output's runs from here out to the east face, in the tunnel's mouth

# ---------------------------------------------------------------- level 2: the drive and the rabbles
SHAFT = (24.0, 40.0)                         # (x, y) of the line shaft along z: the power cell's centre
SHAFT_R = 0.9
ENTRY_Z = (0.0, 4.0)                         # the oak entry, the vanilla axle's cross profile, from the power face into the case
SHAFT_Z = (8.0, 74.4)                        # the iron line shaft, from inside the gear case on through the feed roll
# the gear case at the fire end: a closed cast-iron case on the fire end block's top, bridging the well, holding a
# rectifier (two pairs of gears on one-way catches, one through an idler, its gears not drawn), so the line shaft out of
# its south side turns one way whichever way the axle turns
GEARCASE = ((18.0, 30.0), (33.0, 46.0), (2.0, 12.0))
GEARCASE_BASE = ((17.0, 31.0), (TOP, 33.0), (0.5, 15.5))   # its foot, on the well's rims
GEARCASE_BOSS = (2.2, 1.0)
PEDESTALS = ((24.0, 27.0), (46.6, 49.2))     # z: the line shaft's pedestals on the arch's crown
PED_BASE_X = (21.5, 26.5)
PED_COL_X = (22.8, 25.2)
PED_BEARING = 1.9                            # the bearing block's half side round the shaft (under the water manifold)
MODULE = 0.7
NP, NW = 8, 16                               # the bevel pinions on the line shaft, the bevel wheels on the spindles
RP, RW = MODULE * NP / 2, MODULE * NW / 2    # 2.8, 5.6: pitch radii
CONE_P, CONE_W = math.atan2(RP, RW), math.atan2(RW, RP)
ADD, DED = MODULE, 1.25 * MODULE
FACE = 2.0
TOOTH_W = 1.0
PINION_BODY = ((-1.5, -0.8, 2.1), (-0.8, -0.1, 1.75), (-0.1, 0.5, 1.45))
PINION_DED = 1.85 * MODULE
RATIO = -NP / NW                             # spindle turns per shaft turn (a wheel driven from its north rim turns back)
WHEEL_Y = SHAFT[1] - RP                      # 37.2: the wheels' pitch plane
SPINDLE_R = 0.75
SPINDLE_Y = (14.8, 37.0)
WHEEL_BODY = (35.0, 36.0, 5.6)
WHEEL_DED = 2.0 * MODULE
WHEEL_HUB = (34.0, 35.0, 1.8)                # rests on the swivel
RABBLE_HUB = (14.6, 17.2, 1.8)
ARM_Y = (15.4, 16.6)
ARM_W = 1.2
ARM_R = 12.0
BLADES_R = (4.2, 7.8, 11.2)
BLADE = (2.6, 1.0, (14.3, 15.8))             # length, thickness, y
BLADE_SET = 40.0
GLAND_Z = 1.6
BRACKET = ((21.5, 26.5), 3.0, (31.0, TOP))   # each spindle's bracket on the crown: x, half length z, y
SWIVEL = (TOP, WHEEL_HUB[0], 2.0)            # the swivel head round each spindle (y0, y1, apothem): its upper bearing,
#                                              the water's way into the hollow spindle and arms
PORT = (26.0, 26.8)                          # its port on the east, where the water pipe comes in (x): against the swivel's
#                                              flat, clear of the wheel's hub turning over it

# ---------------------------------------------------------------- the hopper and the feeder
HOPPER = ((16.5, 31.5), (42.0, 48.0), (66.0, 78.0))   # the hopper (level 1): its mouth the infeed cell's top
HOPPER_T = 1.0
HOPPER_SLOT = ((21.0, 27.0), (68.5, 73.5))   # (x, z): the slot in its floor into the neck
NECK = ((20.0, 28.0), (33.0, HOPPER[1][0]), (67.5, 74.5))   # the neck under it: the feed roll's casing at level 2
NECK_T = 1.0
FLANGE = ((18.5, 29.5), (TOP, 33.0), (FLUE_BLOCK[0] + ANGLE_L, D - ANGLE_L))   # the neck's base plate: across the feed
#                                              hole's lining (no strip of it shows), from the inner band to the south band
FEED_CHUTE = (26.5, 35.0)                    # y: the iron feed chute down through FEED_HOLE into the cavity
FEED_CHUTE_T = 1.0
ROLL_Z = (69.0, 73.0)
ROLL_R, ROLL_FLUTES, ROLL_FLUTE_R = 2.0, 6, 2.6

# ---------------------------------------------------------------- level 2: the water
MANIFOLD = (24.0, 45.0, 40.0)                # the soldered pipe's cube under the water cell's top (x, y, z): ppex's pipe
CONNECTOR_Z = 46.5                           # a full-size length of it south from the manifold, plugged at its end round the
#                                              small pipe's bore (the reducer, as the vanner's: Vanner/tools/make_shape.py)
SMALL_HALF, SMALL_WALL = 1.2, 0.4            # the small pipe: a soldered tube 2.4 across round a 1.6 bore (the vanner's)
REDUCER_L = 0.6                              # the reducer's ring of solder round the small pipe where it leaves the plug
SMALL_BEND = SMALL_HALF + solderedpipe.JOINT_SWELL   # each bend and the tee: a soldered cube round the turn
SMALL_Z = 49.5                               # the small pipe turns east here, between the wheels (z), over pedestal 2
SMALL_X = 30.4                               # its drop down the drive row's east side, clear of both wheels
SMALL_Y = (MANIFOLD[1], 33.4)                # its high run (the manifold's height), and its low run under the wheels
PIPE_METALS = ("copper", "lead")

# ---------------------------------------------------------------- the build
# The structure (the iron frame, then the brickwork, the lining, the arch with its ironwork), as the crucible furnace's,
# then level 1's fittings, then level 2's. Every brick count is measured from the model's volume at the game's rates: a
# claybricks block is 2 fired bricks, a refractory brick block 4, rounded up; fire clay one per 2.5 refractory bricks.
BRICKS_PER_BLOCK = 2
LINING_PER_BLOCK = 4
LINING_PER_FIRECLAY = 2.5
FIRED_BRICK = "game:burnedbrick-*"
LINING_BRICK = "game:refractorybrick-fired-tier3"
FIRECLAY = "game:clay-fire"
CAP_ITEM = ("game:metalplate-iron", 1)
STRUCTURE = ("brickwork", "lining", "arch")
BUILD_STATES = (("frame", ()), ("brickwork", ("brickwork",)), ("lining", ("brickwork", "lining")),
                ("arch", ("brickwork", "lining", "arch")))
LEVEL1 = ("hopper", "firedoor", "handfiring", "doorsopen", "dischargeshut")
LEVEL2 = ("hopper", "firedoor", "doorsshut", "dischargeopen", "drive", "rabbles", "feeder", "stoker", "output")
REPLACED = {"handfiring": "stoker", "doorsopen": "doorsshut", "dischargeshut": "dischargeopen"}   # level 2 in place of level 1's
LEVEL_STATES = (                             # the viewer's Level select: (id, label, fitted)
    ("none", "Built, nothing fitted", ()),
    ("level1cold", "Level 1: hand-rabbled, batch (cold)", LEVEL1),
    ("level1", "Level 1: roasting a batch by hand", LEVEL1 + ("charge", "fireflat")),
    ("level2cold", "Level 2: mechanical rabbles, continuous (cold)", LEVEL2),
    ("level2", "Level 2: running", LEVEL2 + ("charge", "firestep")),
)
DAMPERS = ("open", "half", "shut")

# The fired bricks the brickwork takes (the crucible furnace's list: every type of the game's burnedbrick, with the
# block its bricks make and that block's texture, base and overlay).
RUNNING = "game:block/clay/brick/four/running/"
BRICKS = (
    ("fire", "game:claybricks-good-fire", RUNNING + "fire1", None),
    ("black", "game:brickcourse-four-running-black", RUNNING + "cream1", RUNNING + "black1"),
    ("brown", "game:brickcourse-four-running-brown", RUNNING + "cream1", RUNNING + "brown1"),
    ("cream", "game:brickcourse-four-running-cream", RUNNING + "cream1", None),
    ("gray", "game:brickcourse-four-running-gray", RUNNING + "cream1", RUNNING + "gray1"),
    ("orange", "game:brickcourse-four-running-orange", RUNNING + "cream1", RUNNING + "orange1"),
    ("red", "game:brickcourse-four-running-red", RUNNING + "cream1", RUNNING + "red1"),
    ("tan", "game:brickcourse-four-running-tan", RUNNING + "cream1", RUNNING + "tan1"),
    ("clinker", "game:brickcourse-four-running-clinker", RUNNING + "clinker1", None),
)
BRICK_DEFAULT = "red"


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


def box(lo, hi, name, part, tex):
    c = [(lo[k] + hi[k]) / 2 for k in range(3)]
    return skin(El(name, [hi[k] - lo[k] for k in range(3)], c, [r[:] for r in IDENT], {}, part), tex)


def cell_span(lo, hi, axis):
    """An axis-aligned box's span along `axis`, from the start of the block it lies in (voxels)."""
    base = math.floor((lo + 1e-6) / B) * B
    if hi - base > B + 1e-6:
        raise ValueError(f"a world-mapped box crosses a block's boundary along axis {axis}: {lo}..{hi}")
    return lo - base, hi - base


def world_skin(el, tex):
    """An axis-aligned element whose faces take their UVs from where they stand in their block, so boxes side by side
    continue one texture across a block face with no seam (the crucible furnace's mapping, the game's face directions)."""
    lo, hi = el.aabb()
    x0, x1 = cell_span(lo[0], hi[0], 0)
    y0, y1 = cell_span(lo[1], hi[1], 1)
    z0, z1 = cell_span(lo[2], hi[2], 2)
    t = "#" + tex
    k = TEX / 16
    el.faces = {
        "north": {"texture": t, "uv": [k * (16 - x1), k * (16 - y1), k * (16 - x0), k * (16 - y0)]},
        "east": {"texture": t, "uv": [k * (16 - z1), k * (16 - y1), k * (16 - z0), k * (16 - y0)]},
        "south": {"texture": t, "uv": [k * x0, k * (16 - y1), k * x1, k * (16 - y0)]},
        "west": {"texture": t, "uv": [k * z0, k * (16 - y1), k * z1, k * (16 - y0)]},
        "up": {"texture": t, "uv": [k * x0, k * z0, k * x1, k * z1]},
        "down": {"texture": t, "uv": [k * (16 - x1), k * z0, k * (16 - x0), k * z1]},
    }
    return el


def split_cells(name, lo, hi):
    """A box cut at the blocks' boundaries along every axis, as (name, lo, hi): each piece in one block. One piece keeps
    the name; more are numbered."""
    pieces = [(list(lo), list(hi))]
    for axis in range(3):
        nxt = []
        for l0, h0 in pieces:
            cuts = [l0[axis]] + [B * c for c in range(1, 8) if l0[axis] + 1e-6 < B * c < h0[axis] - 1e-6] + [h0[axis]]
            for a, b in zip(cuts, cuts[1:]):
                l2, h2 = list(l0), list(h0)
                l2[axis], h2[axis] = a, b
                nxt.append((l2, h2))
        pieces = nxt
    if len(pieces) == 1:
        return [(name, lo, hi)]
    return [(f"{name}{n}", l2, h2) for n, (l2, h2) in enumerate(pieces, 1)]


def wboxes(lo, hi, name, part, tex):
    """A world-mapped box, cut at the blocks' boundaries (`split_cells`)."""
    if any(hi[k] - lo[k] < 1e-9 for k in range(3)):
        return []
    return [world_skin(box(l2, h2, n2, part, tex), tex) for n2, l2, h2 in split_cells(name, lo, hi)]


def splits(a, b):
    """a..b cut at every block boundary inside it."""
    cuts = [a] + [B * k for k in range(int(math.floor(a / B)) + 1, int(math.ceil(b / B))) if a + 1e-9 < B * k < b - 1e-9] + [b]
    return list(zip(cuts, cuts[1:]))


def long_box(lo, hi, name, part, tex):
    """A long box cut at the blocks' boundaries along its longest axis, so its texture is not stretched."""
    axis = max(range(3), key=lambda k: hi[k] - lo[k])
    pieces = splits(lo[axis], hi[axis])
    out = []
    for i, (a, b) in enumerate(pieces, 1):
        l2, h2 = list(lo), list(hi)
        l2[axis], h2[axis] = a, b
        out.append(box(l2, h2, f"{name}{i}" if len(pieces) > 1 else name, part, tex))
    return out


AX = {"x": 0, "y": 1, "z": 2}
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}   # a disc's angle 0 direction, and the in-plane direction it turns towards


def radial(axis, c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box from radius r0 to r1 about `axis` through c, a0..a1 along the axis, `width` across, at angle `ang`
    (radians, right-handed about +axis from the axis's reference direction: +z about y, +y about z)."""
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
    """A plain round part: k strips as long as the 2k-gon is across, 180/k degrees apart, each a hair shorter than the
    last so no two ends share a plane."""
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
    """The teeth of a bevel gear about `axis` through c, its pitch circle (radius pitch_r) in the plane `at` along the
    axis, its pitch cone's half angle `cone` (radians) with the apex `toward` (+1 or -1) along the axis: n teeth of MODULE
    (tooth 1 at `phase`), each a box FACE long along the cone's generator, ADD above the pitch cone and `ded` below it
    along the cone's normal and TOOTH_W wide, centred on the pitch circle. As the gear cutter's mitre bevels."""
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
            if trial.r[a][u] * toward > 0:
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
    specs = [((lo[u], lo[v]), (hi[u], hole_lo[v])),
             ((lo[u], hole_hi[v]), (hi[u], hi[v])),
             ((lo[u], hole_lo[v]), (hole_lo[u], hole_hi[v])),
             ((hole_hi[u], hole_lo[v]), (hi[u], hole_hi[v]))]
    for i, ((u0, v0), (u1, v1)) in enumerate(specs, 1):
        if u1 - u0 < 1e-9 or v1 - v0 < 1e-9:
            continue
        p, q = list(lo), list(hi)
        p[u], q[u], p[v], q[v] = u0, u1, v0, v1
        out.append(box(p, q, f"{name}{i}", part, tex))
    return out


def tube(lo, hi, t, axis, name, part, tex):
    """A square tube along `axis`, lo..hi outside, walls t thick: four plates, the pair across u running the whole width
    so the corners close."""
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
    """An open-topped box (a bin, a hopper, a liner): four walls and, given `floor`, a floor, with `hole`
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


# ---------------------------------------------------------------- the arch
R_IN = ((IN_X[1] - IN_X[0]) ** 2 / 4 + RISE ** 2) / (2 * RISE)   # the intrados' radius (28.1)
R_OUT = R_IN + ARCH_T
ARCH_C = ((IN_X[0] + IN_X[1]) / 2, SPRING_Y + RISE - R_IN)      # (x, y) the arch's centre, under the ground
ALPHA = math.asin((IN_X[1] - IN_X[0]) / 2 / R_IN)               # its half angle
DELTA = 2 * ALPHA / VOUSSOIRS
STRIP_W = 2 * R_OUT * math.tan(DELTA / 2)                       # a strip's width: the strips meet at the extrados
CROWN_X = (ARCH_C[0] - STRIP_W / 2, ARCH_C[0] + STRIP_W / 2)
CROWN_Y = (ARCH_C[1] + R_IN, ARCH_C[1] + R_OUT)                  # 27..31


def intrados(x):
    return ARCH_C[1] + math.sqrt(max(0.0, R_IN ** 2 - (x - ARCH_C[0]) ** 2))


def strip_corners(i):
    """Strip i's corners in (x, y) as they stand, turned about the arch's centre: (inner pair, outer pair), each by x."""
    phi = ALPHA - (i + 0.5) * DELTA
    (cx, cy), c, s_ = ARCH_C, math.cos(phi), math.sin(phi)

    def at(u, r):
        return (cx + u * c - r * s_, cy + u * s_ + r * c)
    return (sorted((at(-STRIP_W / 2, R_IN), at(STRIP_W / 2, R_IN))),
            sorted((at(-STRIP_W / 2, R_OUT), at(STRIP_W / 2, R_OUT))))


EXTRADOS = sorted({(round(x, 9), round(y, 9)) for i in range(VOUSSOIRS) for x, y in strip_corners(i)[1]})   # its polygon
ARCH_FOOT = EXTRADOS[-1]                     # the east strip's outer foot: the skewback's top and the skewback angle's edge
SKEW = (SKEW_X, (SPRING_Y, ARCH_FOOT[1]))    # the east skewback (x, y): to the arch's foot, under the skewback angle


def extrados_at(x):
    """The arch's extrados (its strips' outer faces) over x, between the feet."""
    for (x0, y0), (x1, y1) in zip(EXTRADOS, EXTRADOS[1:]):
        if x0 - 1e-9 <= x <= x1 + 1e-9:
            return y0 + (y1 - y0) * (x - x0) / (x1 - x0)
    raise ValueError(f"x {x} is off the arch")


def crown_holes():
    """The holes through the crown strip: a gland round each spindle."""
    return [(z - GLAND_Z, z + GLAND_Z) for z in RABBLE_Z]


# ---------------------------------------------------------------- the box frame
def binding_members():
    """The box frame: every member of the binding, as (name, kind, axis, legs), kind "upright" (up a vertical corner),
    "band" (along a top edge, `axis` 0 for x, 2 for z) or "knee" (a plate across a step), each leg (name, lo, hi, face):
    face (axis, plane, outward sign) the brickwork's face the leg is set into, its outer face flush in that plane.

    Uprights: at each vertical corner of the end faces (the shared wall's, full height; the end block's east corner over
    the footing; the footing's east corner) and at each end block's inner east corner, where the east wall drops to the
    skewback angle (standing on it). Knees: in each end face where the footing steps out from under the end block. Bands
    wrap every top edge: the end faces' (`fr_band_<n|s>`), the west wall's outer one (`fr_band_w`), each end block's east
    one (`fr_band_<n|s>e`) and inner one over the arch (`fr_band_<n|s>i`, its face leg reaching down behind the arch's
    end so no brick shows under it), the west wall's inner one over the arch (`fr_band_wi`, the tie rods through it),
    the footing's (`fr_band_f`) and the east wall's under the arch's bay: the skewback angle (`fr_skew`), its face leg the
    skewback's back, its top leg the skewback's top to the arch's foot, and its face leg on through the end blocks to the
    corner angles. A face leg takes the corner; the other leg runs on from it."""
    t, l = ANGLE_T, ANGLE_L
    out = []
    for face, zf, inz in ANGLE_FACES:
        for side, xc, inx, (y0, y1) in ANGLE_SIDES:
            xs, zs = sorted((xc, xc + inx * l)), sorted((zf, zf + inz * t))
            a = (f"fr_angle_{face}{side}_face", [xs[0], y0, zs[0]], [xs[1], y1, zs[1]], (2, zf, -inz))
            xs, zs = sorted((xc, xc + inx * t)), sorted((zf + inz * t, zf + inz * l))
            b = (f"fr_angle_{face}{side}_side", [xs[0], y0, zs[0]], [xs[1], y1, zs[1]], (0, xc, -inx))
            out.append((f"fr_angle_{face}{side}", "upright", 1, [a, b]))
    ys = SKEW[1][1]
    for face, zi, outz in INNER_FACES:
        zs = sorted((zi, zi - outz * t))
        a = (f"fr_angle_{face}i_face", [FACE_E - l, ys, zs[0]], [FACE_E, TOP, zs[1]], (2, zi, outz))
        zs = sorted((zi - outz * t, zi - outz * l))
        b = (f"fr_angle_{face}i_side", [FACE_E - t, ys, zs[0]], [FACE_E, TOP, zs[1]], (0, FACE_E, 1.0))
        out.append((f"fr_angle_{face}i", "upright", 1, [a, b]))
    for face, zf, inz in ANGLE_FACES:
        zs = sorted((zf, zf + inz * t))
        out.append((f"fr_knee_{face}", "knee", None,
                    [(f"fr_knee_{face}_face", [FACE_E - l, HEARTH_Y - l, zs[0]], [FACE_E, HEARTH_Y, zs[1]], (2, zf, -inz))]))
    for face, zf, inz in ANGLE_FACES:
        zs = sorted((zf, zf + inz * t))
        a = (f"fr_band_{face}_face", [WEST[0] + l, TOP - l, zs[0]], [FACE_E - l, TOP, zs[1]], (2, zf, -inz))
        zs = sorted((zf + inz * t, zf + inz * l))
        b = (f"fr_band_{face}_top", [WEST[0] + t, TOP - t, zs[0]], [FACE_E - t, TOP, zs[1]], (1, TOP, 1.0))
        out.append((f"fr_band_{face}", "band", 0, [a, b]))
    out.append(("fr_band_w", "band", 2, [("fr_band_w_face", [WEST[0], TOP - l, l], [WEST[0] + t, TOP, D - l], (0, WEST[0], -1.0)),
                                         ("fr_band_w_top", [WEST[0] + t, TOP - t, l], [WEST[0] + l, TOP, D - l], (1, TOP, 1.0))]))
    for (face, zf, inz), (_, zi, _) in zip(ANGLE_FACES, INNER_FACES):
        zs = sorted((zf + inz * l, zi - inz * l))
        a = (f"fr_band_{face}e_face", [FACE_E - t, TOP - l, zs[0]], [FACE_E, TOP, zs[1]], (0, FACE_E, 1.0))
        zs = sorted((zf + inz * l, zi - inz * t))
        b = (f"fr_band_{face}e_top", [FACE_E - l, TOP - t, zs[0]], [FACE_E - t, TOP, zs[1]], (1, TOP, 1.0))
        out.append((f"fr_band_{face}e", "band", 2, [a, b]))
    cuts = sorted({IN_X[0], IN_X[1], ARCH_FOOT[0], FACE_E - l} | {x for x, _ in EXTRADOS if IN_X[0] < x < ARCH_FOOT[0] - 1e-9})
    for face, zi, outz in INNER_FACES:
        zs = sorted((zi, zi - outz * t))
        legs = []
        for j, (x0, x1) in enumerate(zip(cuts, cuts[1:])):
            y0 = ys if x0 >= ARCH_FOOT[0] - 1e-9 else min(extrados_at(x0), extrados_at(x1))
            if face == "s" and x0 < IN_X[1] - 1e-9:
                y0 = max(y0, CAVITY_TOP)             # over the flue end's cavity: down to its roof, never into it
            legs.append((f"fr_band_{face}i_face{chr(97 + j)}", [x0, y0, zs[0]], [x1, TOP, zs[1]], (2, zi, outz)))
        zs = sorted((zi - outz * t, zi - outz * l))
        legs.append((f"fr_band_{face}i_top", [IN_X[0] - l, TOP - t, zs[0]], [FACE_E - l, TOP, zs[1]], (1, TOP, 1.0)))
        out.append((f"fr_band_{face}i", "band", 0, legs))
    z0, z1 = FIRE_BLOCK[1] - t, FLUE_BLOCK[0] + t
    legs = [("fr_band_wi_face", [IN_X[0] - t, TOP - l, z0], [IN_X[0], ROD_Y[0], z1], (0, IN_X[0], 1.0))]
    gaps = [z0] + [q for zc in STAY_Z for q in (zc - ROD_T / 2, zc + ROD_T / 2)] + [z1]
    for j, (a0, a1) in enumerate(zip(gaps[::2], gaps[1::2])):
        legs.append((f"fr_band_wi_lip{chr(97 + j)}", [IN_X[0] - t, ROD_Y[0], a0], [IN_X[0], TOP, a1], (0, IN_X[0], 1.0)))
        legs.append((f"fr_band_wi_top{chr(97 + j)}", [IN_X[0] - l, TOP - t, a0], [IN_X[0] - t, TOP, a1], (1, TOP, 1.0)))
    out.append(("fr_band_wi", "band", 2, legs))
    out.append(("fr_band_f", "band", 2, [("fr_band_f_face", [W - t, HEARTH_Y - l, l], [W, HEARTH_Y, D - l], (0, W, 1.0)),
                                         ("fr_band_f_top", [FACE_E, HEARTH_Y - t, t], [W - t, HEARTH_Y, D - t], (1, HEARTH_Y, 1.0))]))
    out.append(("fr_skew", "band", 2, [("fr_skew_face", [FACE_E - t, SPRING_Y, l], [FACE_E, ys, D - l], (0, FACE_E, 1.0)),
                                       ("fr_skew_top", [ARCH_FOOT[0], ys - t, ARCH_Z[0]], [FACE_E - t, ys, ARCH_Z[1]], (1, ys, 1.0))]))
    return out


def binding():
    """The box frame's legs, as (name, lo, hi)."""
    return [(n, lo, hi) for _, _, _, legs in binding_members() for n, lo, hi, _ in legs]


def frame_ties():
    """The iron frame's ties, as (name, lo, hi): a post under each end of the ash mouth's lintel, and one under each end
    block's east corner angle, hidden in the brickwork (the damper's guides stand against the lintel's face)."""
    (lx, ly, lz) = ASH_LINTEL
    out = [("fr_tie_lintel_w", [lx[0] - 1.0, 0.0, lz[0]], [lx[0], ly[1], lz[1]]),
           ("fr_tie_lintel_e", [lx[1], 0.0, lz[0]], [lx[1] + 1.0, ly[1], lz[1]])]
    # a post in the footing under each end block's east corner angle, inside its side leg, clear of the end face
    for face, zf, inz in ANGLE_FACES:
        zs = sorted((zf + inz * (ANGLE_T + 0.25), zf + inz * ANGLE_L))
        out.append((f"fr_tie_angle_{face}e", [FACE_E - ANGLE_T, 0.0, zs[0]], [FACE_E, HEARTH_Y, zs[1]]))
    return out


def bearers():
    """The flat grate's two bearers across the firebox, as (name, lo, hi), their ends built 3 into the side walls."""
    return [(f"fm_bearer{i}", [IN_X[0] - LINING_T, BEARER_Y[0], z0], [IN_X[1] + LINING_T, BEARER_Y[1], z1])
            for i, (z0, z1) in enumerate(BEARER_Z, 1)]


def door_spans():
    hw, _ = WORK_DOOR
    return [(zc - hw, zc + hw) for zc in RABBLE_Z]


def rod_boxes():
    """The tie rods over the arch, as (name, lo, hi), from the shared wall's anchors to the east buckstays."""
    return [(f"iw_rod{i}", [ROD_X[0], ROD_Y[0], zc - ROD_T / 2], [ROD_X[1], ROD_Y[1], zc + ROD_T / 2]) for i, zc in enumerate(STAY_Z, 1)]


def anchor_boxes():
    (ax, ay, hw) = ANCHOR
    return [(f"iw_anchor{i}", [ax[0], ay[0], zc - hw], [ax[1], ay[1], zc + hw]) for i, zc in enumerate(STAY_Z, 1)]


def brick_voids():
    """Every space cut out of the brick, as (lo, hi) in voxels: beyond the east face over the footing; over the springing
    between the end blocks (the arch's place, open above it); the ash pit and the firebox, over the bridge, the hearth's
    chamber and the flue end's cavity; the uptake, the feed hole, the well with its cover's seat and the throat from its
    floor; the ash mouth, the fire door and their recesses; the work doors; the discharge hole, the tunnel and the
    discharge door's recess and its plate's slot; the outlet cap's seat; and the room for the iron set into the brick (the binding, the
    lintels, the grate's bearers, the tie rods' chases and anchors, the frame's ties)."""
    v = []

    def add(lo, hi):
        v.append((list(lo), list(hi)))
    add([FACE_E, HEARTH_Y, 0.0], [W, TOP, D])
    add([IN_X[0], SPRING_Y, ARCH_Z[0]], [FACE_E, TOP, ARCH_Z[1]])
    add([IN_X[0], ASHPIT_Y[0], FIREBOX_Z[0]], [IN_X[1], SPRING_Y, FIREBOX_Z[1]])
    add([IN_X[0], BRIDGE_TOP, BRIDGE_Z[0]], [IN_X[1], SPRING_Y, BRIDGE_Z[1]])
    add([IN_X[0], HEARTH_Y, HEARTH_Z[0]], [IN_X[1], SPRING_Y, HEARTH_Z[1]])
    add([IN_X[0], SPRING_Y, FLUE_BLOCK[0]], [IN_X[1], CAVITY_TOP, HEARTH_Z[1]])
    (ux, uz) = UPTAKE
    add([ux[0], CAVITY_TOP, uz[0]], [ux[1], TOP, uz[1]])
    (fx, fz) = FEED_HOLE
    add([fx[0], CAVITY_TOP, fz[0]], [fx[1], TOP, fz[1]])
    for (x, y, z) in (WELL, WELL_SEAT, THROAT):
        add([x[0], y[0], z[0]], [x[1], y[1], z[1]])
    (mx, my) = MOUTH
    add([mx[0], my[0], FRONT_Z[0]], [mx[1], my[1], FRONT_Z[1]])
    (dx, dy) = FIRE_DOOR
    add([dx[0], dy[0], FRONT_Z[0]], [dx[1], dy[1], FRONT_Z[1]])
    for (x, y, z) in (RECESS, FD_RECESS, ASH_LINTEL, FD_LINTEL, FD_SILL):
        add([x[0], y[0], z[0]], [x[1], y[1], z[1]])
    _, (wy0, wy1) = WORK_DOOR
    for z0, z1 in door_spans():
        add([EAST[0], wy0, z0], [FACE_E, wy1, z1])
    (hx, hz) = DISCHARGE
    add([hx[0], TUNNEL[1][1], hz[0]], [hx[1], HEARTH_Y, hz[1]])
    (tx, ty, tz) = TUNNEL
    add([tx[0], ty[0], tz[0]], [tx[1], ty[1], tz[1]])
    (rx, ry, rz) = D_RECESS
    add([rx[0], ry[0], rz[0]], [rx[1], ry[1], rz[1]])
    (sx, sy) = D_SLOT
    add([sx[0], sy[0], rz[0]], [sx[1], sy[1], rz[1]])
    (cx, cz) = CAP_SEAT
    add([cx[0], CAP_Y[0], cz[0]], [cx[1], CAP_Y[1], cz[1]])
    v += [(lo, hi) for _, lo, hi in binding() + frame_ties() + bearers() + rod_boxes() + anchor_boxes()]
    return v


def lining_zones():
    """Where the brick is the lining's, as (lo, hi): LINING_T round every hot space, from where the fire is up. The
    firebox's walls from the grate's level and its roof (with the throat through it); the bridge, the hearth's bed and
    its walls to the springing; the flue end's cavity, its walls and roof; the uptake and the outlet's curb (out to the
    binding's top legs); the feed hole (its top course on to the curb and the bands). And so no brickwork shows narrower
    than a brick: the coal well's floor beside the throat. The rest is the brickwork's: the base, the ash pit and the
    ash mouth, the tunnel, the outer walls."""
    t = LINING_T
    (ux, uz), (fx, fz) = UPTAKE, FEED_HOLE
    (kx, kz), (wx, _, wz) = CURB, WELL
    return [([IN_X[0] - t, BEARER_Y[0] + 0.5, FRONT_Z[1] - t], [IN_X[1] + t, SPRING_Y + t, BRIDGE_Z[0] + t]),
            ([IN_X[0] - t, HEARTH_Y - t, BRIDGE_Z[0]], [IN_X[1] + t, SPRING_Y, HEARTH_Z[1] + t]),
            ([IN_X[0] - t, SPRING_Y, FLUE_BLOCK[0]], [IN_X[1] + t, CAVITY_TOP + t, HEARTH_Z[1] + t]),
            ([ux[0] - t, CAVITY_TOP, uz[0] - t], [ux[1] + t, TOP, uz[1] + t]),
            ([fx[0] - t, CAVITY_TOP, fz[0] - t], [fx[1] + t, TOP, fz[1] + t]),
            ([kx[0], TOP - t, FLUE_BLOCK[0] + ANGLE_T], [kx[1], TOP, kz[1]]),
            ([kx[1], TOP - t, kz[0]], [fx[1] + t, TOP, kz[1]]),
            ([wx[0], SPRING_Y, wz[0]], [IN_X[0] - t, SPRING_Y + t, wz[1]])]


def _inside(p, lo, hi):
    return all(lo[k] < p[k] < hi[k] for k in range(3))


def brick_lattice():
    """The brick as a lattice: the breakpoints along each axis (every void's faces, the lining's zones' and every
    block's), and for each lattice box what it is: "bk" (the brickwork's), "ln" (the lining's) or None (a void)."""
    voids = brick_voids()
    zones = lining_zones()
    axes = []
    for k, n in enumerate((CELLS_X, BODY_Y, CELLS_Z)):
        pts = {round(B * c, 6) for c in range(n + 1)}
        for lo, hi in voids + zones:
            pts.add(round(min(max(lo[k], 0.0), n * B), 6))
            pts.add(round(min(max(hi[k], 0.0), n * B), 6))
        axes.append(sorted(pts))
    xs, ys, zs = axes
    mat = {}
    for i in range(len(xs) - 1):
        for j in range(len(ys) - 1):
            for k in range(len(zs) - 1):
                mid = ((xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2, (zs[k] + zs[k + 1]) / 2)
                if any(_inside(mid, lo, hi) for lo, hi in voids):
                    mat[(i, j, k)] = None
                else:
                    mat[(i, j, k)] = "ln" if any(_inside(mid, lo, hi) for lo, hi in zones) else "bk"
    return xs, ys, zs, mat


BRICK_ROLES = {"bk": ("brickwork", "brick"), "ln": ("lining", "lining")}   # (part, texture)


def build_brick():
    """The brick: the lattice's brickwork and lining each merged greedily into boxes (along x, then z, then y), none
    crossing a block's boundary or the other's, each world-mapped so the boxes read as one wall; `bk_brick_NNN` in the
    `brickwork` part, `ln_brick_NNN` in the `lining`. A face lying on its whole area against brick always there with it
    is left out: a brickwork face against brickwork, a lining face against either (the lining goes in after the
    brickwork). A brickwork face against the lining is kept: until the lining is in, it is the wall's face."""
    xs, ys, zs, mat = brick_lattice()
    nx, ny, nz = len(xs) - 1, len(ys) - 1, len(zs) - 1

    def blk(vals, i):
        return int((vals[i] + 1e-6) // B)

    used = set()
    spans = []
    for j in range(ny):
        for k in range(nz):
            for i in range(nx):
                m0 = mat[(i, j, k)]
                if m0 is None or (i, j, k) in used:
                    continue

                def free(a, b, c):
                    return mat[(a, b, c)] == m0 and (a, b, c) not in used
                i1 = i
                while i1 + 1 < nx and blk(xs, i1 + 1) == blk(xs, i) and free(i1 + 1, j, k):
                    i1 += 1
                k1 = k
                while k1 + 1 < nz and blk(zs, k1 + 1) == blk(zs, k) and all(free(a, j, k1 + 1) for a in range(i, i1 + 1)):
                    k1 += 1
                j1 = j
                while j1 + 1 < ny and blk(ys, j1 + 1) == blk(ys, j) and all(
                        free(a, j1 + 1, c) for a in range(i, i1 + 1) for c in range(k, k1 + 1)):
                    j1 += 1
                for a in range(i, i1 + 1):
                    for b in range(j, j1 + 1):
                        for c in range(k, k1 + 1):
                            used.add((a, b, c))
                spans.append((m0, (i, i1), (j, j1), (k, k1)))
    out = []
    count = {"bk": 0, "ln": 0}
    hides = {"bk": ("bk",), "ln": ("bk", "ln")}
    for m0, (i0, i1), (j0, j1), (k0, k1) in spans:
        count[m0] += 1
        part, tex = BRICK_ROLES[m0]
        el = world_skin(box([xs[i0], ys[j0], zs[k0]], [xs[i1 + 1], ys[j1 + 1], zs[k1 + 1]], f"{m0}_brick_{count[m0]:03d}", part, tex), tex)
        faces = {"west": [(i0 - 1, b, c) for b in range(j0, j1 + 1) for c in range(k0, k1 + 1)],
                 "east": [(i1 + 1, b, c) for b in range(j0, j1 + 1) for c in range(k0, k1 + 1)],
                 "down": [(a, j0 - 1, c) for a in range(i0, i1 + 1) for c in range(k0, k1 + 1)],
                 "up": [(a, j1 + 1, c) for a in range(i0, i1 + 1) for c in range(k0, k1 + 1)],
                 "north": [(a, b, k0 - 1) for a in range(i0, i1 + 1) for b in range(j0, j1 + 1)],
                 "south": [(a, b, k1 + 1) for a in range(i0, i1 + 1) for b in range(j0, j1 + 1)]}
        for d, nbrs in faces.items():
            if all(mat.get(q) in hides[m0] for q in nbrs):
                del el.faces[d]
        out.append(el)
    return out


# ---------------------------------------------------------------- builders: the structure
def build_frame_iron():
    """The iron frame (`frame`, `fr_*`): what the frame stage puts down, standing on its own, set into the brick once it
    is in: the box frame (binding_members: the uprights, the knees at the footing's steps, the bands round every top
    edge and the skewback angle), the ash mouth's lintel, the damper's guides and its sill between them, and the posts
    under the lintel and the end blocks' east corner angles (hidden in the brickwork)."""
    f, t = "frame", "iron"
    out = []
    for name, lo, hi in binding():
        out += long_box(lo, hi, name, f, t)
    (lx, ly, lz) = ASH_LINTEL
    out.append(box([lx[0], ly[0], lz[0]], [lx[1], ly[1], lz[1]], "fr_lintel", f, t))
    (_, ry, rz) = RECESS
    for side, (x0, x1) in zip(("w", "e"), GUIDES):
        out.append(box([x0, ry[0], rz[0]], [x1, ry[1], rz[1]], f"fr_guide_{side}", f, t))
    out.append(box([GUIDES[0][1], SILL[0], rz[0]], [GUIDES[1][0], SILL[1], rz[1]], "fr_sill", f, t))
    out += [box(lo, hi, name, f, t) for name, lo, hi in frame_ties()]
    return out


def build_frames():
    """The iron set into the brick as it is laid (`frames`, `fm_*`, fitted with the brickwork): the fire door's lintel and
    its dead plate (the sill, through the brickwork to the lining),
    the flat grate's bearers (the step grate's treads are built into the side walls), and on the east side each work
    door's frame and guides and the discharge door's low rail in its recess (its top edge runs behind the footing band's
    face leg)."""
    p, t = "frames", "iron"
    (lx, ly, lz) = FD_LINTEL
    out = [box([lx[0], ly[0], lz[0]], [lx[1], ly[1], lz[1]], "fm_fdlintel", p, t)]
    (sx, sy, sz) = FD_SILL
    out.append(box([sx[0], sy[0], sz[0]], [sx[1], sy[1], sz[1]], "fm_fdsill", p, t))
    out += [box(lo, hi, name, p, t) for name, lo, hi in bearers()]
    fr = DOOR_FRAME
    hw, (y0, y1) = WORK_DOOR
    face = FACE_E
    for k, zc in enumerate(RABBLE_Z, 1):
        out += frame_bars([face, y0 - fr, zc - hw - fr], [face + 0.8, y1 + fr, zc + hw + fr], [face, y0, zc - hw], [face + 0.8, y1, zc + hw], "x",
                          f"fm_door{k}_frame", p, t)
        for g, (gz0, gz1) in enumerate(((zc - hw - fr - 1.0, zc - hw - fr), (zc + hw + fr, zc + hw + fr + 1.0)), 1):
            out.append(box([face, y0 - fr, gz0], [W, GUIDE_TOP, gz1], f"fm_door{k}_guide{g}", p, t))
    (rx, ry, rz) = D_RECESS
    out.append(box([rx[0], ry[0], rz[0]], [rx[1], ry[0] + D_RAIL, rz[1]], "fm_drail_low", p, t))
    return out


def build_arch():
    """The arch (`arch`): VOUSSOIRS strips of tier-3 refractory round the arch's centre from the fire end block to the
    flue end block, cut at the block grid, the crown strip round the spindles' glands; and the east skewback it springs
    from (the west foot is in the shared wall)."""
    p = "arch"
    out = []
    cx, cy = ARCH_C
    for i in range(VOUSSOIRS):
        phi = ALPHA - (i + 0.5) * DELTA
        holes = crown_holes() if i == VOUSSOIRS // 2 else []
        for j, (z0, z1) in enumerate(runs(ARCH_Z[0], ARCH_Z[1], holes), 1):
            el = box([cx - STRIP_W / 2, cy + R_IN, z0], [cx + STRIP_W / 2, cy + R_OUT, z1], f"arch_v{i + 1}_{j}", p, "lining")
            if abs(phi) > 1e-12:
                rotate([el], "z", math.degrees(phi), (cx, cy, 0.0))
            out.append(el)
    (sx, sy) = SKEW
    # under the skewback angle's top leg, and to the arch's foot beside it (under the east strip)
    out += wboxes([sx[0], sy[0], ARCH_Z[0]], [sx[1], sy[1] - ANGLE_T, ARCH_Z[1]], "arch_skew", p, "lining")
    out += wboxes([sx[0], sy[1] - ANGLE_T, ARCH_Z[0]], [ARCH_FOOT[0], sy[1], ARCH_Z[1]], "arch_skewfoot", p, "lining")
    return out


def build_ironwork():
    """The ironwork (`ironwork`, `iw_*`, fitted with the arch): the east buckstays on the footing, each an angle iron (its
    flange on the face against the skewback angle and the wall, its foot on the footing band, its web standing out from
    the flange's south edge), the tie rods over the crown from them through the west inner band into the shared wall,
    where each ends on an anchor plate set in its top against the west band, and the nuts on the buckstays' flanges.
    Nothing stands beyond the shared wall's face."""
    p, t = "ironwork", "iron"
    out = []
    for i, zc in enumerate(STAY_Z, 1):
        z0, z1 = zc - STAY_W / 2, zc + STAY_W / 2
        out += long_box([STAY_X[0], STAY_Y[0], z0], [STAY_X[1], STAY_Y[1], z1], f"iw_stay{i}_flange", p, t)
        out += long_box([STAY_X[1], STAY_Y[0], z1 - ANGLE_T], [W, STAY_Y[1], z1], f"iw_stay{i}_web", p, t)
        (nx, ny, nhw) = NUT
        out.append(box([nx[0], ny[0], zc - nhw], [nx[1], ny[1], zc + nhw], f"iw_nut{i}", p, t))
    for name, lo, hi in rod_boxes():
        out += long_box(lo, hi, name, p, t)
    out += [box(lo, hi, name, p, t) for name, lo, hi in anchor_boxes()]
    return out


# ---------------------------------------------------------------- builders: level 1's fittings
def chute_ud(cell, y0, name, part):
    """A section of the vanilla chute, up and down, in `cell` (build-frame block coordinates): CHUTE_UD_WALLS from y0
    up to the cell's top, where a chute placed in the cell above meets it."""
    ox, oy, oz = (cell[k] * B for k in range(3))
    return [box([ox + x0, y0, oz + z0], [ox + x1, oy + B, oz + z1], f"{name}{i}", part, "chute")
            for i, (x0, x1, z0, z1) in enumerate(CHUTE_UD_WALLS, 1)]


def chute_we(cell, x0, name, part):
    """A section of the vanilla chute along x in `cell`: CHUTE_WE_WALLS from x0 out to the cell's east face, where a
    chute placed in the cell beyond meets it."""
    ox, oy, oz = (cell[k] * B for k in range(3))
    return [box([x0, oy + y0, oz + z0], [ox + B, oy + y1, oz + z1], f"{name}{i}", part, "chute")
            for i, (y0, y1, z0, z1) in enumerate(CHUTE_WE_WALLS, 1)]


def build_hopper():
    """The hopper (`hopper`, level 1, kept at level 2): an open iron bin on the roof, its mouth the infeed cell's top,
    filled by hand; under its slot a neck (the feed roll's casing at level 2, its end walls the line shaft's last
    bearings) on a base plate on the flue end block's top, between the bands, and the iron feed chute down through the block's roof into the
    cavity over the hearth's far end, so a charge thrown in at level 1 falls through to the hearth."""
    p, t = "hopper", "iron"
    (x0, x1), (y0, y1), (z0, z1) = HOPPER
    out = open_box([x0, y0, z0], [x1, y1, z1], HOPPER_T, "hopper_bin", p, t, hole=HOPPER_SLOT)
    (nx, ny, nz) = NECK
    n = NECK_T
    out += [box([nx[0], ny[0], nz[0]], [nx[0] + n, ny[1], nz[1]], "hopper_neck_w", p, t),
            box([nx[1] - n, ny[0], nz[0]], [nx[1], ny[1], nz[1]], "hopper_neck_e", p, t),
            box([nx[0] + n, ny[0], nz[0]], [nx[1] - n, ny[1], nz[0] + n], "hopper_neck_n", p, t),
            box([nx[0] + n, ny[0], nz[1] - n], [nx[1] - n, ny[1], nz[1]], "hopper_neck_s", p, t)]
    (fx, fy, fz) = FLANGE
    (hx, hz) = FEED_HOLE
    out += frame_bars([fx[0], fy[0], fz[0]], [fx[1], fy[1], fz[1]], [hx[0], fy[0], hz[0]], [hx[1], fy[1], hz[1]], "y", "hopper_flange", p, t)
    out += tube([hx[0], FEED_CHUTE[0], hz[0]], [hx[1], FEED_CHUTE[1], hz[1]], FEED_CHUTE_T, "y", "hopper_chute", p, t)
    return out


def build_firedoor():
    """The fire door (`firedoor`, level 1, kept shut at level 2): an iron plate shut in its recess in the fire end face,
    filling it, over the opening, with a handle bar in the recess; stoked through at level 1."""
    p, t = "firedoor", "iron"
    (rx, ry, _) = FD_RECESS
    return [box([rx[0], ry[0], 0.4], [rx[1], ry[1], FD_RECESS[2][1]], "firedoor_plate", p, t),
            box([22.0, 17.0, 0.12], [26.0, 17.6, 0.4], "firedoor_handle", p, t)]


def build_handfiring():
    """Level 1's firing (`handfiring`, `hf_*`): the flat grate, BARS iron bars along z on the frame's bearers, stoked
    through the fire door; and the well's cover, three flat iron plates flush in their seat in the fire end block's
    top (level 2's coal bunker goes in the well, its throat onto the step grate)."""
    p, t = "handfiring", "iron"
    out = []
    gap = (IN_X[1] - IN_X[0] - BARS * BAR_W) / (BARS - 1)
    for n in range(BARS):
        x0 = IN_X[0] + n * (BAR_W + gap)
        out.append(box([x0, BAR_Y[0], BAR_Z[0]], [x0 + BAR_W, BAR_Y[1], BAR_Z[1]], f"hf_bar{n + 1:02d}", p, t))
    (sx, sy, sz) = WELL_SEAT
    cuts = [sx[0] + (sx[1] - sx[0]) * f for f in (0.0, 1 / 3, 2 / 3, 1.0)]
    for i, (a, b) in enumerate(zip(cuts, cuts[1:]), 1):
        out += long_box([a, sy[0], sz[0]], [b, sy[1], sz[1]], f"hf_cover{i}_", p, t)
    return out


def build_doors(place):
    """The work doors' plates in one place (`doors<place>`): raised in their guides (open, level 1: the hearth can be
    seen and rabbled through them) or down over the openings (shut, level 2). Door PEEP_DOOR's plate has a peep hole
    over the rabble's arms, with a shutter sliding along z in two guides: over the hole at level 1 (the door itself is
    open), slid clear of it at level 2, so the rabble is seen turning through the shut door."""
    p, t = f"doors{place}", "iron"
    hw, _ = WORK_DOOR
    fr = DOOR_FRAME
    py0, py1 = DOOR_PLATE[place]
    x0, x1 = FACE_E + 0.8, FACE_E + 1.8
    out = []
    for k, zc in enumerate(RABBLE_Z, 1):
        za, zb = zc - hw - fr - 0.5, zc + hw + fr + 0.5
        if k != PEEP_DOOR:
            out.append(box([x0, py0, za], [x1, py1, zb], f"{p}_plate{k}", p, t))
            out.append(box([x1, py0 + 1.0, zc - 1.5], [FACE_E + 1.95, py0 + 1.6, zc + 1.5], f"{p}_handle{k}", p, t))
            continue
        ph, (pa, pb) = PEEP
        ha, hb = py0 + pa, py0 + pb
        out += [box([x0, py0, za], [x1, ha, zb], f"{p}_plate{k}", p, t),
                box([x0, ha, za], [x1, hb, zc - ph], f"{p}_plate{k}n", p, t),
                box([x0, ha, zc + ph], [x1, hb, zb], f"{p}_plate{k}s", p, t),
                box([x0, hb, za], [x1, py1, zb], f"{p}_plate{k}top", p, t)]
        out.append(box([x1, py0 + 0.2, zc - 1.5], [FACE_E + 1.95, py0 + 0.8, zc + 1.5], f"{p}_handle{k}", p, t))
        st, lap, gap = PEEP_SHUTTER
        gt, gh = PEEP_GUIDE
        w = 2 * (ph + lap)
        sz = (zc - ph - lap, zc + ph + lap) if place == "open" else (zc + ph + gap, zc + ph + gap + w)
        out.append(box([x1, ha - lap, sz[0]], [x1 + st, hb + lap, sz[1]], f"{p}_peepshutter{k}", p, t))
        gz = (zc - ph - lap, zc + ph + gap + w)
        out.append(box([x1, ha - lap - gh, gz[0]], [x1 + gt, ha - lap, gz[1]], f"{p}_peepguide{k}a", p, t))
        out.append(box([x1, hb + lap, gz[0]], [x1 + gt, hb + lap + gh, gz[1]], f"{p}_peepguide{k}b", p, t))
    return out


def build_discharge(place):
    """The discharge door (`discharge<place>`): an iron plate sliding along z in its recess in the footing's face, on
    its low rail, its top edge in the slot behind the footing band's face leg: shut over the tunnel's mouth (level 1, the player opens it to draw the roasted charge), or slid
    clear of it (level 2, the output chute in the mouth)."""
    p, t = f"discharge{place}", "iron"
    z0, z1 = D_PLATE[place]
    out = [box([D_PLATE_X[0], D_PLATE_Y[0], z0], [D_PLATE_X[1], D_PLATE_Y[1], z1], f"{p}_plate", p, t)]
    zm = (z0 + z1) / 2
    out.append(box([D_PLATE_X[1], 7.4, zm - 0.6], [W - 0.05, 8.6, zm + 0.6], f"{p}_knob", p, t))
    return out


def build_damper(place):
    """The ash mouth's damper (`damper<place>`): an iron plate sliding in its recess between its guides, with a knob, in
    one of its three places (DAMPER): the crucible furnace's."""
    p = f"damper{place}"
    y0 = DAMPER[place]
    out = [box([GUIDES[0][1], y0, DAMPER_Z[0]], [GUIDES[1][0], y0 + DAMPER_H, DAMPER_Z[1]], f"{p}_plate", p, "iron")]
    (kx, kz, ky) = KNOB
    out.append(box([kx[0], y0 + ky[0], kz[0]], [kx[1], y0 + ky[1], kz[1]], f"{p}_knob", p, "iron"))
    return out


def build_cap():
    """The outlet's cap (`outletcap`): a flat iron plate flush in the curb's seat, on every furnace of a pair but the one
    carrying the chimney."""
    (x0, x1), (z0, z1) = CAP_SEAT
    return [box([x0, CAP_Y[0], z0], [x1, CAP_Y[1], z1], "outletcap_plate", "outletcap", "iron")]


# ---------------------------------------------------------------- builders: level 2's fittings
SHAFT_C = (SHAFT[0], SHAFT[1], 0.0)


def pinion_z(zs):
    """Where a spindle's pinion sits on the line shaft: its pitch circle a wheel's pitch radius north of the spindle,
    where the two pitch cones' apex is the axes' meeting point."""
    return zs - RW


def build_drive():
    """The drive: the oak entry from the power face (the vanilla axle's cross) into the gear case, whose rectifier turns
    the line shaft one way whichever way the axle turns; the iron line shaft along the roof's middle to the hopper's neck,
    a bevel pinion for each rabble's wheel; its pedestals on the arch's crown."""
    p = "lineshaft"
    x, y = SHAFT
    z0, z1 = ENTRY_Z
    out = [box([x - 1.5, y - 0.6, z0], [x + 1.5, y + 0.6, z1], "entry_a", "entry", "oak"),
           box([x - 0.6, y - 1.5, z0], [x + 0.6, y + 1.5, z1], "entry_b", "entry", "oak")]
    (cx0, cx1), (cy0, cy1), (cz0, cz1) = GEARCASE
    (bx0, bx1), (by0, by1), (bz0, bz1) = GEARCASE_BASE
    br, bl = GEARCASE_BOSS
    out += long_box([bx0, by0, bz0], [bx1, by1, bz1], "gearcase_foot", "gearcase", "iron")
    out.append(box([cx0, cy0, cz0], [cx1, cy1, cz1], "gearcase_case", "gearcase", "iron"))
    out += disc("z", SHAFT_C, cz0 - bl, cz0, br, "gearcase_bossn", "gearcase", "iron", k=6)
    out += disc("z", SHAFT_C, cz1, cz1 + bl, br, "gearcase_bosss", "gearcase", "iron", k=6)
    rods = []
    for i, (a, b) in enumerate(splits(*SHAFT_Z), 1):
        rods += disc("z", SHAFT_C, a, b, SHAFT_R, f"lineshaft_rod{i}", p, "iron")
    out += rods
    for k, zs in enumerate(RABBLE_Z, 1):
        zp = pinion_z(zs)
        for j, (a, b, r) in enumerate(PINION_BODY, 1):
            out += disc("z", SHAFT_C, zp + a, zp + b, r, f"lineshaft_pinion{k}_body{j}", p, "iron", k=6)
        out += bevel("z", SHAFT_C, zp, RP, CONE_P, NP, f"lineshaft_pinion{k}", p, "iron", toward=1.0, phase=math.pi, ded=PINION_DED)
    q = "pedestals"
    for k, (a, b) in enumerate(PEDESTALS, 1):
        out.append(box([PED_BASE_X[0], CROWN_Y[1], a], [PED_BASE_X[1], TOP, b], f"pedestal{k}_base", q, "iron"))
        out.append(box([PED_COL_X[0], TOP, a + 0.6], [PED_COL_X[1], y - PED_BEARING, b - 0.6], f"pedestal{k}_column", q, "iron"))
        out.append(box([x - PED_BEARING, y - PED_BEARING, a], [x + PED_BEARING, y + PED_BEARING, b], f"pedestal{k}_bearing", q, "iron"))
    return out


def build_rabbles():
    """Each rabble: a hollow spindle through a gland in the arch's crown and up through its swivel head, a bevel wheel on
    its top (teeth up, its line shaft pinion on its north rim), and in the furnace a hub with two arms, each with three
    ploughs set across it. The swivel stands on a bracket on the crown: the spindle's upper bearing, and where the water
    comes in by its port, down the spindle into the arms (water-cooled)."""
    out = []
    for k, zs in enumerate(RABBLE_Z, 1):
        p = f"rabble{k}"
        c = (SHAFT[0], 0.0, zs)
        out += disc("y", c, *SPINDLE_Y, SPINDLE_R, f"{p}_spindle", p, "iron")
        out += disc("y", c, WHEEL_HUB[0], WHEEL_HUB[1], WHEEL_HUB[2], f"{p}_wheelhub", p, "iron")
        out += disc("y", c, WHEEL_BODY[0], WHEEL_BODY[1], WHEEL_BODY[2], f"{p}_wheelbody", p, "iron", k=6)
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
        (bx0, bx1), hz, (by0, by1) = BRACKET
        out.append(box([bx0, by0, zs - hz], [bx1, by1, zs + hz], f"rabblemount{k}_base", q, "iron"))
        sy0, sy1, sa = SWIVEL
        out += disc("y", (c[0], 0.0, zs), sy0, sy1, sa, f"rabblemount{k}_swivel", q, "iron", k=4)
        ly, ph = SMALL_Y[1], SMALL_BEND
        out.append(box([PORT[0], ly - ph, zs - ph], [PORT[1], ly + ph, zs + ph], f"rabblemount{k}_port", q, "iron"))
    return out


def build_feeder():
    """The feeder (level 2): a fluted roll keyed on the line shaft's end turns in the hopper's neck under its slot,
    metering the charge down the feed chute; and the section of the vanilla chute hung in the hopper's mouth from two
    straps on its rim, where the previous machine's chute meets it."""
    out = disc("z", SHAFT_C, *ROLL_Z, ROLL_R, "feedroll_body", "feedroll", "iron", k=6)
    for i in range(ROLL_FLUTES):
        out.append(radial("z", SHAFT_C, ROLL_Z[0] + 0.1, ROLL_Z[1] - 0.1, ROLL_R - 0.4, ROLL_FLUTE_R, 1.0, TAU * i / ROLL_FLUTES + math.pi / ROLL_FLUTES,
                          f"feedroll_flute{i + 1}", "feedroll", "iron"))
    p = "feeder"
    out += chute_ud(INFEED_CELL, INFEED_SECTION_Y, "feeder_infeedchute", p)
    (hx0, hx1), _, _ = HOPPER
    t = HOPPER_T
    ox, oz = INFEED_CELL[0] * B, INFEED_CELL[2] * B
    top = (INFEED_CELL[1] + 1) * B
    zm = oz + B / 2
    out.append(box([hx0 + t, top - 1.0, zm - 1.0], [ox + 4.0, top, zm + 1.0], "feeder_strap_w", p, "iron"))
    out.append(box([ox + 12.0, top - 1.0, zm - 1.0], [hx1 - t, top, zm + 1.0], "feeder_strap_e", p, "iron"))
    return out


def steps():
    """The step grate's treads, top first: (z0, z1, top)."""
    return [(STEP_Z0 + STEP_D * i, STEP_Z0 + STEP_D * (i + 1), STEP_TOP0 - STEP_DROP * i) for i in range(STEPS)]


def build_stoker():
    """Level 2's firing (`stoker`): the coal bunker, an iron liner in the fire end block's well with a slot in its floor;
    the iron throat from the slot down through the firebox's roof to just over the top tread; the step grate, STEPS iron
    treads across the firebox, each lower than the last towards the bridge, built into the side walls, so the coal
    slides down them as it burns (no stoking); and the section of the vanilla chute dropping into the bunker from the
    fuel cell's top, hung from a strap on the liner."""
    p, t = "stoker", "iron"
    out = []
    (x0, x1), (y0, y1), (z0, z1) = WELL
    out += open_box([x0, y0, z0], [x1, y1, z1], LINER_T, "stoker_bunker", p, t, hole=(THROAT[0], THROAT[2]))
    (sx, _, sz) = THROAT
    out += tube([sx[0], THROAT_FOOT, sz[0]], [sx[1], y0 + LINER_T, sz[1]], LINER_T, "y", "stoker_throat", p, t)
    for i, (a, b, top) in enumerate(steps(), 1):
        out.append(box([IN_X[0], top - STEP_T, a], [IN_X[1], top, b], f"stoker_step{i}", p, t))
    out += chute_ud(FUEL_CELL, FUEL_SECTION_Y, "stoker_fuelchute", p)
    ox, oz = FUEL_CELL[0] * B, FUEL_CELL[2] * B
    zm = oz + B / 2
    out.append(box([x0 + LINER_T, y1 - 1.0, zm - 1.0], [ox + 4.0, y1, zm + 1.0], "stoker_strap", p, t))
    return out


def build_output():
    """The output (`output`, level 2): a section of the vanilla chute along x in the tunnel's mouth, flush with the east
    face, where the next machine's chute meets it; and an iron spout from under the discharge hole down onto the
    section's floor, so the roasted charge falling through slides into the chute."""
    p = "output"
    out = chute_we(OUTPUT_CELL, OUTPUT_SECTION_X, "output_chute", p)
    oy, oz = OUTPUT_CELL[1] * B, OUTPUT_CELL[2] * B
    floor = oy + CHUTE_WE_WALLS[2][1]
    (hx, hz) = DISCHARGE
    a = [TUNNEL[0][0], TUNNEL[1][1] - 1.5, 0.0]
    b = [OUTPUT_SECTION_X + 0.5, floor + 0.25, 0.0]
    zc = oz + 8.0
    out.append(strut(a, b, 0.5, CHUTE_WE_WALLS[2][3] - CHUTE_WE_WALLS[2][2], "output_spout", p, "iron", axis="z"))
    out[-1].c[2] = zc
    return out


def small_tube(name, axis, a, b, centre):
    """A length of the small pipe along `axis` from a to b about `centre` (solderedpipe's (u, v) across it): four walls
    round its bore, as solderedpipe's tube is built, at the vanner's spray pipe's size."""
    return solderedpipe._walls(name, axis, a, b, centre, SMALL_HALF, SMALL_HALF - SMALL_WALL, solderedpipe.BODY)


def pipe_boxes():
    """The water as UnifiedPipes' soldered pipe (solderedpipe's boxes, build-frame voxels), the vanner's way down to a
    small pipe: the manifold, a cube of ppex's pipe under the water cell's top, open up to the player's pipe and south,
    plugged below; a full-size length of it south (the connector), its end plugged round the small pipe's bore (four
    plates inside its walls) with a ring of solder round the small pipe where it leaves (the reducer); and the small pipe,
    2.4 across, on south over pedestal 2, east, down the drive row's east side clear of both wheels to a tee, along under
    the wheels both ways and in to each swivel's port, a soldered cube at each bend and the tee, a ring at each port."""
    h = solderedpipe.HALF
    x, y, z = MANIFOLD
    out = solderedpipe.hub("manifold", MANIFOLD, ("up", "south"))
    ze = CONNECTOR_Z
    out += solderedpipe.tube("connector", 2, z + h, ze, (x, y))
    i = h - solderedpipe.WALL
    si = SMALL_HALF - SMALL_WALL
    p0, p1 = ze - solderedpipe.WALL, ze
    Bx = solderedpipe.Box
    out += [Bx("plug_down", [x - i, y - i, p0], [x + i, y - si, p1], solderedpipe.BODY),
            Bx("plug_up", [x - i, y + si, p0], [x + i, y + i, p1], solderedpipe.BODY),
            Bx("plug_west", [x - i, y - si, p0], [x - si, y + si, p1], solderedpipe.BODY),
            Bx("plug_east", [x + si, y - si, p0], [x + i, y + si, p1], solderedpipe.BODY)]
    j, sb = solderedpipe.JOINT_SWELL, SMALL_BEND
    out += solderedpipe._walls("reducer", 2, ze, ze + REDUCER_L, (x, y), SMALL_HALF + j, SMALL_HALF, solderedpipe.SOLDER)
    hy, ly = SMALL_Y
    zb, xd = SMALL_Z, SMALL_X
    zs1, zs2 = RABBLE_Z

    def bend(name, p):
        return Bx(name, [p[k] - sb for k in range(3)], [p[k] + sb for k in range(3)], solderedpipe.SOLDER)
    out += small_tube("small_south", 2, ze, zb - sb, (x, hy))
    out.append(bend("small_bend1", (x, hy, zb)))
    out += small_tube("small_east", 0, x + sb, xd - sb, (zb, hy))
    out.append(bend("small_bend2", (xd, hy, zb)))
    out += small_tube("small_down", 1, ly + sb, hy - sb, (xd, zb))
    out.append(bend("small_tee", (xd, ly, zb)))
    for k, zs in enumerate((zs1, zs2), 1):
        out += small_tube(f"small_along{k}", 2, min(zs, zb) + sb, max(zs, zb) - sb, (xd, ly))
        out.append(bend(f"small_bend{k + 2}", (xd, ly, zs)))
        out += small_tube(f"small_in{k}", 0, PORT[1], xd - sb, (zs, ly))
        out += solderedpipe._walls(f"small_port{k}", 0, PORT[1], PORT[1] + REDUCER_L, (zs, ly), SMALL_HALF + j, SMALL_HALF, solderedpipe.SOLDER)
    return solderedpipe.cull_pressed(out)


def build_pipes(metal_=PIPE_METALS[0]):
    """The water's pipes as elements of the part `pipe<metal_>` (`pipe_boxes`): the body in that metal's pipe texture,
    the joints and elbows in solder, each face's UVs solderedpipe's. Built in copper; `pipe_copies` makes lead."""
    p = f"pipe{metal_}"
    codes = {solderedpipe.BODY: f"#{p}", solderedpipe.SOLDER: "#solder"}
    out = []
    for b in pipe_boxes():
        size = b.size()
        faces = {d: {"texture": codes[b.role], "uv": solderedpipe.uv(d, size, TEX / 16)} for d in b.faces}
        out.append(El(f"{p}_{b.name}", size, [(b.lo[k] + b.hi[k]) / 2 for k in range(3)], [row[:] for row in IDENT], faces, p))
    return out


def pipe_copies(els):
    """The pipes in the other metals: the first metal's elements, as they are (after the z-fighting fix), renamed and in
    their own metal's texture; the solder stays. Only one metal's are ever drawn."""
    first = f"pipe{PIPE_METALS[0]}"
    out = []
    for m in PIPE_METALS[1:]:
        for el in els:
            if el.part == first:
                c = el.clone(f"pipe{m}" + el.name[len(first):], f"pipe{m}")
                for face in c.faces.values():
                    if face["texture"] == f"#{first}":
                        face["texture"] = f"#pipe{m}"
                out.append(c)
    return out


# ---------------------------------------------------------------- builders: the contents
def build_charge():
    """The charge on the hearth (`charge`, both levels): a bed of concentrate, open round the discharge hole."""
    p = "charge"
    (dx0, dx1), (dz0, dz1) = DISCHARGE
    x0, x1 = IN_X[0] + 1.0, IN_X[1] - 1.0
    z0, z1 = HEARTH_Z[0] + 1.0, HEARTH_Z[1] - 1.0
    y0, y1 = HEARTH_Y, HEARTH_Y + 1.0
    return [box([x0, y0, z0], [x1, y1, dz0 - 0.5], "charge_n", p, "ore"),
            box([x0, y0, dz0 - 0.5], [dx0 - 0.5, y1, dz1 + 0.5], "charge_w", p, "ore"),
            box([dx1 + 0.5, y0, dz0 - 0.5], [x1, y1, dz1 + 0.5], "charge_e", p, "ore"),
            box([x0, y0, dz1 + 0.5], [x1, y1, z1], "charge_s", p, "ore")]


def build_fireflat():
    """Level 1's fire (`fireflat`): the burning bed on the flat grate, stoked through the fire door."""
    return [box([IN_X[0] + 1.0, BAR_Y[1], BAR_Z[0] + 0.5], [IN_X[1] - 1.0, BAR_Y[1] + 2.5, BAR_Z[1] - 0.5], "fireflat_bed", "fireflat", "ember")]


def build_firestep():
    """Level 2's fire (`firestep`): a burning bed on each tread of the step grate, the coal in the throat down onto the
    top tread, and the coal in the bunker."""
    p = "firestep"
    out = []
    for i, (z0, z1, top) in enumerate(steps(), 1):
        out.append(box([IN_X[0] + 1.0, top, z0 + 0.3], [IN_X[1] - 1.0, top + 1.5, z1 - 0.3], f"firestep_bed{i}", p, "ember"))
    (sx, _, sz) = THROAT
    t = LINER_T
    top0 = STEP_TOP0 + 1.5
    out.append(box([sx[0] + t, top0, sz[0] + t], [sx[1] - t, WELL[1][0] + t, sz[1] - t], "firestep_coalthroat", p, "coal"))
    (x0, x1), (y0, _), (z0, z1) = WELL
    out.append(box([x0 + t, y0 + t, z0 + t], [x1 - t, FUEL_SECTION_Y - 0.1, z1 - t], "firestep_coalbunker", p, "coal"))
    return out


def build():
    els = build_drive() + build_rabbles() + build_feeder()
    els += build_stoker() + build_output() + build_pipes()
    els += build_hopper() + build_firedoor() + build_handfiring()
    for place in ("open", "shut"):
        els += build_doors(place)
    for place in ("shut", "open"):
        els += build_discharge(place)
    for place in DAMPERS:
        els += build_damper(place)
    els += build_charge() + build_fireflat() + build_firestep() + build_cap()
    els += build_arch() + build_ironwork()
    els += build_structure()
    return els


def build_structure():
    """What stands still and is no level's: the brick (brickwork and lining), the iron frame and the iron set into the
    brick."""
    return build_brick() + build_frames() + build_frame_iron()


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
    """The parts in the build's order: the structure's stages, level 1's fittings, level 2's, the contents and the cap,
    then the iron frame (the viewer lists the requires values in the order the parts first name them)."""
    shaft = pt(SHAFT[0], SHAFT[1], 0.0)
    parts = [static("brickwork", match="bk_*"), static("frames", "brickwork", "fm_*"), static("lining", match="ln_*"),
             static("arch"), static("ironwork", "arch", "iw_*"),
             static("hopper"), static("firedoor"), static("handfiring", match="hf_*"),
             static("doorsopen"), static("doorsshut"), static("dischargeshut"), static("dischargeopen")]
    parts += [static(f"damper{d}") for d in DAMPERS]
    # the entry turns with the axle (theta), either way; everything beyond the gear case's rectifier reads the axle's
    # travel (psi), which only grows, so it turns forward whichever way the axle turns
    parts += [{"id": "entry", "match": ["entry_*"], "requires": "drive",
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
              static("feeder"), static("stoker"), static("output")]
    parts += [static(f"pipe{m}") for m in PIPE_METALS]
    parts += [static("charge"), static("fireflat"), static("firestep"), static("outletcap"),
              {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []}]
    for p in parts:
        p["ride"] = None
        for d in p["drivers"]:
            validate_driver(d)
    return [{"id": p["id"], "match": p["match"], "requires": p["requires"], "drivers": p["drivers"], "ride": p["ride"]} for p in parts]


MOVING = ("entry", "lineshaft", "rabble1", "rabble2", "feedroll")
RECTIFIED = ("lineshaft", "rabble1", "rabble2", "feedroll")   # beyond the rectifier: forward either way


# ---------------------------------------------------------------- scenes and poses
# A scene is what is fitted: (the build's state, the level's state, the damper's place, the pipes' metal, capped).
# Level fittings need the whole structure; the damper the fire door (level 1's, kept); the pipes the rabbles; the cap
# the lining (the curb).
DEFAULT_SCENE = ("arch", "level2", "open", "copper", False)


def fitted(scene):
    """The requires values fitted in a scene."""
    build_, level, damper, metal_, capped = scene
    out = set(dict(BUILD_STATES)[build_])
    if set(STRUCTURE) <= out:
        out |= set(next(s[2] for s in LEVEL_STATES if s[0] == level))
    if "firedoor" in out:
        out.add(f"damper{damper}")
    if "rabbles" in out:
        out.add(f"pipe{metal_}")
    if capped and "lining" in out:
        out.add("outletcap")
    return out


def requires_of(parts, pid):
    return next(p["requires"] for p in parts if p["id"] == pid)


def on_show(parts, pid, scene):
    r = requires_of(parts, pid)
    return r is None or r in fitted(scene)


def inputs_of(pose):
    return {"theta": pose[0]}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


def shown(parts, posed_els, pose):
    """The posed elements as they can be seen in the pose's scene: every part not drawn moved far away, each part by its
    own distance (copies, in order)."""
    index = {p["id"]: n for n, p in enumerate(parts)}
    req = {p["id"]: p["requires"] for p in parts}
    fit = fitted(pose[1])
    out = []
    for e in posed_els:
        if req[e.part] is not None and req[e.part] not in fit:
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 * (1 + index[e.part]), e.c[2]]
        out.append(e)
    return out


# ---------------------------------------------------------------- the rig file
def footprint():
    body = [(x, y, z) for x in range(CELLS_X) for y in range(BODY_Y) for z in range(CELLS_Z)]
    return sorted(set(body) | set(DRIVE_CELLS))


CELL_ANCHORS = ("powerCell", "infeedCell", "outputCell", "fuelCell", "waterCell", "outletCell")
POINT_ANCHORS = ("infeed", "output", "fuel", "water", "charge", "discharge", "ash", "stoke", "workDoor1", "workDoor2")


def box_volume(el):
    return abs(el.size[0] * el.size[1] * el.size[2])


def build_volumes(els):
    """The brickwork's and the lining's volumes, and the arch's refractory (its strips and skewback), in blocks, measured
    from their elements."""
    bk = sum(box_volume(e) for e in els if e.name.startswith("bk_brick_"))
    ln = sum(box_volume(e) for e in els if e.name.startswith("ln_brick_"))
    ar = sum(box_volume(e) for e in els if e.name.startswith("arch_"))
    return bk / B ** 3, ln / B ** 3, ar / B ** 3


def build_counts(els):
    bk, ln, ar = build_volumes(els)
    bricks = math.ceil(bk * BRICKS_PER_BLOCK - 1e-9)
    lining = math.ceil(ln * LINING_PER_BLOCK - 1e-9)
    arch = math.ceil(ar * LINING_PER_BLOCK - 1e-9)
    return {"brickVolume": bk, "liningVolume": ln, "archVolume": ar, "bricks": bricks, "lining": lining,
            "fireclay": math.ceil(lining / LINING_PER_FIRECLAY - 1e-9), "arch": arch,
            "archFireclay": math.ceil(arch / LINING_PER_FIRECLAY - 1e-9)}


_COUNTS = {}


def counts():
    """build_counts of the model as built (before the z-fighting fix, which steps faces in)."""
    if not _COUNTS:
        _COUNTS.update(build_counts(build_structure() + build_arch()))
    return dict(_COUNTS)


def materials(pairs):
    return [{"code": code, "quantity": q} for code, q in pairs]


def build_json():
    c = counts()
    return {
        "provisional": True,
        "frame": {"recipe": "grid", "tool": "hammer", "ingredients": None},
        "stages": [
            {"requires": "brickwork", "volumeBlocks": r6(c["brickVolume"]), "oneType": True, "perClick": "stack",
             "materials": materials(((FIRED_BRICK, c["bricks"]),)), "alsoParts": ["frames"]},
            {"requires": "lining", "volumeBlocks": r6(c["liningVolume"]), "perClick": "stack",
             "materials": materials(((LINING_BRICK, c["lining"]), (FIRECLAY, c["fireclay"])))},
            {"requires": "arch", "volumeBlocks": r6(c["archVolume"]), "perClick": "stack",
             "materials": materials(((LINING_BRICK, c["arch"]), (FIRECLAY, c["archFireclay"]))), "alsoParts": ["ironwork"]},
        ],
        "levels": [
            {"level": 1, "fits": list(LEVEL1), "also": [f"damper{d}" for d in DAMPERS]},
            {"level": 2, "fits": list(LEVEL2), "replaces": dict(REPLACED), "also": [f"pipe{m}" for m in PIPE_METALS]},
        ],
        "outletCap": materials((CAP_ITEM,))[0],
        "_comment": "PROVISIONAL, to be settled with the owner (#737): the structure's stages and the levels' fittings, in "
                    "order. frame: the iron frame, a grid recipe with a hammer (its ingredients not settled); placing it puts "
                    "down the controller and its ghosts. brickwork (with the iron set into it, frames), lining, arch (with its "
                    "ironwork): the brick measured from the model, in blocks, at 2 fired bricks or 4 refractory bricks a "
                    "block, rounded up, fire clay one per 2.5 refractory bricks; oneType: the first fired brick sets the "
                    "furnace's type (bricks). levels: what each level fits once the structure is built (level 1's damper one "
                    "of its places; level 2's pipes one metal); level 2's replaces: level 1's parts it takes the place of "
                    "(the flat grate and the well's cover, the work doors open, the discharge door shut). The contents "
                    "(charge, fireflat, firestep) are gameplay's state, not fittings.",
    }


def bricks_json():
    return {"code": "brick", "item": "game:burnedbrick-{type}", "default": BRICK_DEFAULT,
            "types": {t: {"block": block, "base": base, **({"overlays": [over]} if over else {})} for t, block, base, over in BRICKS},
            "_comment": "The fired brick types the brickwork takes (every game:burnedbrick type), each with the block its "
                        "bricks make and that block's texture: the renderer sets the shape's 'brick' code to the furnace's "
                        "type's base, its overlays drawn over it. The first brick put in sets the type."}


def outlet_centre():
    (ux, uz) = UPTAKE
    return ((ux[0] + ux[1]) / 2, (uz[0] + uz[1]) / 2)


def make_rig(parts):
    (tx, ty, tz) = TUNNEL
    (mx, my) = MOUTH
    (dx, dy) = FIRE_DOOR
    hw, (wy0, wy1) = WORK_DOOR
    (hx, hz) = DISCHARGE
    (fx, fz) = FEED_HOLE
    ox, oz = outlet_centre()
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the middle of the fire "
                    "end at ground level, nearest the player who placed it; the furnace runs south to the flue end. One "
                    "furnace with two levels (#736, #737), its footprint fixed from the frame on: level 1 batch, rabbled by "
                    "hand (hopper, fire door, flat grate, work doors open, discharge door); level 2 continuous (the drive, "
                    "the water-cooled rabbles, the feed roll, the stoker and step grate in place of the flat grate, the "
                    "output chute, the water pipes; the work doors shut). Two stand back to back sharing the west wall "
                    "(furnace.rowMax), one outlet capped. The entry shaft reads theta, the axle's angle; everything beyond "
                    "the gear case's rectifier reads the axle's travel, so it turns forward either way. requires: the "
                    "structure's stages (brickwork with frames, lining, arch with ironwork; the iron frame has none), the "
                    "levels' fittings (build.levels), the contents (charge, fireflat, firestep) and outletcap. See "
                    "ReverberatoryRoaster/README.md.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "infeedCell": list(INFEED_CELL),
        "infeedFace": INFEED_FACE,
        "outputCell": list(OUTPUT_CELL),
        "outputFace": OUTPUT_FACE,
        "fuelCell": list(FUEL_CELL),
        "fuelFace": FUEL_FACE,
        "waterCell": list(WATER_CELL),
        "waterFace": WATER_FACE,
        "outletCell": list(OUTLET_CELL),
        "outletFace": "up",
        "infeed": {"pos": pt((INFEED_CELL[0] + 0.5) * B, (INFEED_CELL[1] + 1) * B, (INFEED_CELL[2] + 0.5) * B)},
        "output": {"pos": pt(tx[1], (ty[0] + ty[1]) / 2, (tz[0] + tz[1]) / 2)},
        "fuel": {"pos": pt((FUEL_CELL[0] + 0.5) * B, (FUEL_CELL[1] + 1) * B, (FUEL_CELL[2] + 0.5) * B)},
        "water": {"pos": pt(MANIFOLD[0], (WATER_CELL[1] + 1) * B, MANIFOLD[2])},
        "charge": {"pos": pt((fx[0] + fx[1]) / 2, HEARTH_Y, (fz[0] + fz[1]) / 2)},
        "discharge": {"pos": pt((hx[0] + hx[1]) / 2, HEARTH_Y, (hz[0] + hz[1]) / 2)},
        "ash": {"pos": pt((mx[0] + mx[1]) / 2, (my[0] + my[1]) / 2, 0.0)},
        "ashSide": "north",
        "stoke": {"pos": pt((dx[0] + dx[1]) / 2, (dy[0] + dy[1]) / 2, 0.0)},
        "stokeSide": "north",
        "workDoor1": {"pos": pt(FACE_E, (wy0 + wy1) / 2, RABBLE_Z[0])},
        "workDoor1Side": "east",
        "workDoor2": {"pos": pt(FACE_E, (wy0 + wy1) / 2, RABBLE_Z[1])},
        "workDoor2Side": "east",
        "chimney": {"origin": pt(ox, TOP + MIN_CHIMNEY * B / 2, oz), "axis": "y", "length": float(MIN_CHIMNEY)},
        "build": build_json(),
        "bricks": bricks_json(),
        "furnace": {"minChimney": MIN_CHIMNEY, "rowMax": ROW_MAX, "rowAxis": "x", "rowFacing": "opposite",
                    "_comment": "minChimney: chimney blocks stood on the outlet, counted from it up (the crucible furnace's, "
                                "provisional); rowMax, rowAxis, rowFacing: two furnaces stand side by side along x, the "
                                "second turned half round, so they share their west walls (the doorless long side) and "
                                "each keeps its work doors outward; one outlet carries the chimney, the other is capped."},
        "gearing": {"rabbleTurnsPerAxleTurn": r6(abs(RATIO)), "feedRollTurnsPerAxleTurn": 1, "pinionTeeth": NP, "wheelTeeth": NW,
                    "_comment": "As drawn: each rabble's bevel wheel is driven from its north rim by a bevel pinion on the line "
                                f"shaft ({NP}:{NW}), so the rabbles turn {abs(RATIO):.4g} as fast as the axle, against it; the "
                                "feed roll is keyed on the shaft's end; the line shaft turns once a turn of the axle, forward "
                                "either way (the gear case's rectifier, 1:1). Nothing reads it: the parts' drivers carry it."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0]: one block west."""
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    db = [v / B for v in d]
    ship_els = copy.deepcopy(els)
    for el in ship_els:
        el.c = [el.c[k] + d[k] for k in range(3)]
    ship_parts = copy.deepcopy(parts)
    for p in ship_parts:
        for drv in p["drivers"]:
            for key in ("pivot", "anchor"):
                if key in drv:
                    drv[key] = [r6(drv[key][k] + db[k]) for k in range(3)]
    ship = copy.deepcopy(rig)
    for key in CELL_ANCHORS:
        ship[key] = shift_cell(rig[key], ORIGIN_CELL)
    for key in POINT_ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["chimney"] = {**rig["chimney"], "origin": shift_point(rig["chimney"]["origin"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


# the cells' boxes: the finished machine at level 2, every part of it at rest (a part-built or level 1 furnace's are
# gameplay's), with the doors shut and the discharge door open
CELL_SCENE = ("arch", "level2cold", "open", "copper", False)


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig, the finished machine
    (CELL_SCENE); then a deck (lid) on every column's top cell."""
    fit = fitted(CELL_SCENE)
    req = {p["id"]: p["requires"] for p in ship_parts}
    by_cell = {}
    for w in flatten(shape["elements"], textures={}):
        pid = part_of(ship_parts, w.name)
        if req[pid] is not None and req[pid] not in fit:
            continue
        el = posed(w, _part_matrix(ship_parts, pid, {"theta": 0.0}))
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, []).append(el)
    out = []
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        cell = {"pos": list(pos)}
        boxes = cell_boxes(by_cell[pos], pos) if pos in by_cell else None
        if boxes:
            cell["boxes"] = boxes
        out.append(cell)
    return with_lids(out)


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    poses = [(0.0, DEFAULT_SCENE)] + [(th, DEFAULT_SCENE) for th in (0.7, -2.3, 4.4, 9.1)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    span = [(min(c[k] for c in cells), max(c[k] for c in cells)) for k in range(3)]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; {len(cells)} cells, x {span[0]}, "
          f"y {span[1]}, z {span[2]}; " + ", ".join(f"{k} {ship[k]}" for k in CELL_ANCHORS))
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
    return {"_comment": f"Generated by {SCRIPT} from the shipped reverberatory-rig.json's parts: each part's matrix as 3 rows "
                        "of 4 (block units) at each axle angle theta (travel is |theta|). The site's and the generator's tests "
                        "check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    shape = machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Both levels' parts are in this one "
             "shape (level 2's step grate, stoker, doors shut and output in place of level 1's flat grate and well cover, doors "
             "open and discharge door shut): a renderer draws what is fitted. Texture codes the renderer sets: 'brick' (the "
             "furnace's brick type's, from the rig's bricks), 'pipecopper' or 'pipelead' (the pipes' metal, one part each). "
             "The burning bed ('ember') glows. Keep element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)
    for e in shape["elements"]:
        if e["name"].startswith(("fireflat_bed", "firestep_bed")):
            for f in e["faces"].values():
                f["glow"] = FIRE_GLOW
    return shape


def coplanar_poses():
    """The poses the z-fighting fix and its check look at: level 2 running at rest and three more angles, level 1 roasting,
    the structure's stages, the damper's other places with the outlet capped, and lead pipes."""
    out = [(th, DEFAULT_SCENE) for th in (0.0, 0.4, 1.3, 2.9)]
    out += [(0.0, ("arch", "level1", "open", "copper", False)), (0.0, ("arch", "level1cold", "half", "copper", True)),
            (0.0, ("arch", "level2cold", "shut", "lead", True))]
    out += [(0.0, (b, "none", "open", "copper", False)) for b, _ in BUILD_STATES[:-1]]
    return tuple(out)


def fix_coplanar(els, parts):
    def posed_fn(es, pose):
        return shown(parts, [posed(el, pm(parts, el.part, pose)) for el in es], pose)
    return fix_coplanar_posed(els, posed_fn, coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the reverberatory roaster's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_reverberatory
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        print(f"coplanar faces before the fix: {sum(len(p) for p in before.values())} pairs over {len(before)} poses")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    els += pipe_copies(els)
    rig = make_rig(parts)
    ok = validate_reverberatory.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "reverberatory.json", args.out / "reverberatory_frame.json", args.out / "reverberatory-rig.json",
                args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "reverberatory.json", SHAPE_DIR / "reverberatory_frame.json", RIG_DIR / "reverberatory-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_reverberatory.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
