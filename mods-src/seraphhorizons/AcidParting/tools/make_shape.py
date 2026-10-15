#!/usr/bin/env python3
"""Generate the acid parting vessel's shapes, rig and reference poses.

The acid parting vessel parts gold from silver with nitric acid, as the mints and refineries of the late nineteenth
century did (Rose 1898, pp. 398-400: the granulated alloy in "cylindrical vessels of earthenware, porcelain, or
platinum" set in a cast-iron sand bath over a small fire, under a brick hood to the chimney; the gold left behind
washed with water). It is one such vessel, built whole as one machine: a block of brickwork one block wide, two deep
and two high, bound at its corners and all four top edges with angle iron. In its front row a small fire burns on a grate of iron firebars, stoked through a sliding iron fire door
in the front, its ash pit under the grate reached by the ash mouth under the door, which an iron damper slides over
(open, half, shut), natural draught only. Over the fire a cast-iron sand bath is let into the masonry, its iron
bottom the fire chamber's roof; a fired clay parting vessel (the game's crucible) stands in its sand, under a brick hood on two piers, open
in front, whose breast is carried on an iron lintel. In front of the bath, on the cold end of the bench under the same
hood, stands the precipitating tub: a lead siphon draws the silver solution off the vessel into it, copper plates hang
in it on a copper rod, and the silver comes down on them as a grey sludge on its bottom, which is drawn by hand; the
gold stays in the vessel. The wash water is poured in by hand from a bucket (no pipe: #931's draw is 1 L a 10 units).
The fire's flue runs back from the top of the fire chamber's back wall into a stack in the back row; the hood
draws its fumes back into the same stack near its top, and the stack rises to an outlet on the top, centred in the
back row, where the player stands the chimney (or a flat iron cap, on every vessel in a row but the one carrying the
row's chimney). Units stand side by side in a row along their width, their plain brick side walls meeting.

It is not a furnace, and takes from the crucible furnace (`CrucibleFurnace/`) only what it needs: the fire (a grate on
bearers, an ash mouth with the three-place damper, natural draught), the outlet with its curb and cap and the chimney
counted from it, the brick in its two roles (any fired brick for the brickwork, tier-3 refractory lining 3 voxels
thick on the hot faces: the fire chamber, its flue and the stack), the iron binding (here angle iron wrapping the corners and the top edges, the owner's rule), the staged
build and its measured counts, and the contents drawn as parts that come and go. The hood is brickwork, not lined:
it carries acid fumes, not the fire's heat.

Everything is built here from plain boxes; no other mod's model is used. It writes, deterministically,

    acidparting.json          the whole vessel, every part that moves or comes and goes   (assets/.../shapes/block/)
    acidparting_frame.json    the iron frame only (block and item)                         (assets/.../shapes/block/)
    acidparting-rig.json      cells, anchors and the part rig                              (assets/.../config/)
    rig-reference.json        every part's matrix at a grid of poses                       (tests/AcidParting/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_acidparting.py) and exits non-zero if
a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the machine box's
north-west-bottom corner. The front (the fire door, the ash mouth, the hood's opening) is north; the stack and the
outlet are in the back row (south). The controller cell is the front cell at ground level, [0,0,0], the fire door's
and the ash mouth's: the build frame is the shipped frame.

The rig's inputs, as this vessel uses them:

    depth  the fire door: 0 open, slid up in its guides (the rest pose), 1 shut. A step slide.
    theta  the stoker's clock: a ratio-0 rotate on the door carries it and moves nothing (so the viewer offers Play,
           which opens and shuts the door). Nothing turns: no power.

Everything else that shows or not (the build's stages: the brickwork with the hood's lintel, the lining, the sand bath
and the fire door (with the damper), the vessel and the tub, the services (the siphon); the damper's three places; the fire laid and burning; the vessel's contents: the dore, the acid, the
silver solution, the parted gold, the wash water; the tub's: the copper plates, the drawn-off liquor, the silver
sludge; the outlet's cap) is a `requires`: gameplay's state, not a motion.
The iron frame has none.
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

from machinegen.checks import cell_boxes, cells_touched  # noqa: E402
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
REFERENCE_OUT = MOD / "tests" / "AcidParting" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/AcidParting/tools/make_shape.py"

B = 16.0
OCT = math.tan(math.pi / 8)                  # an octagon's half side over its apothem

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 1, 2, 2          # one wide (x, the row's way), two high, two deep (z)
ORIGIN_CELL = (0, 0, 0)                      # the controller: the front cell at ground level (the fire door's, the ash
#                                              mouth's): the build frame is the shipped frame
CHIMNEY_CELL = (0, 2, 1)                     # over the outlet: the player's chimney, not the machine's
ROW_MAX = 6                                  # vessels in a row, side by side along x, sharing one chimney

TEXTURES = {
    "brick": "game:block/clay/brick/four/running/red1",     # the brickwork: the red brick's colour (BRICKS)
    "lining": "game:block/clay/refractory/tier3/front1",    # the hot-face lining and the outlet's curb: tier-3 refractory
    "iron": "game:block/metal/plate/iron",                  # the frame, the hood's lintel, the sand bath, door, damper, cap
    "sand": "game:block/stone/sand/sandstone",              # the sand bath's sand
    "ceramic": "game:block/clay/hardened/blue",            # the parting vessel: fired clay, as the game's fired crucible
#                                                            (Rose's earthenware), the item it is built from
    "fuel": "game:block/coal/charcoal",                     # the fire laid
    "ember": "game:block/coal/ember",                       # the same fuel, burning; its faces glow
    "dore": "game:block/metal/ingot/electrum",              # the dore (gold and silver together), granulated
    "gold": "game:block/metal/ingot/gold",                  # the parted gold
    "acid": "game:block/liquid/brine",                      # aqua fortis: nitric acid, pale yellow
    "solution": "game:block/liquid/dye/woad",               # the acid with the silver (and the dore's copper) in it: blue
    "water": "game:block/liquid/waterportion",              # the wash water, poured in from a bucket
    "oak": "game:block/wood/planks/oak1",                   # the precipitating tub's boards
    "lead": "game:block/metal/sheet-plain/lead4",           # the siphon: lead pipe
    "copper": "game:block/metal/plate/copper",              # the copper plates and their rod
    "silver": "game:block/metal/ingot/silver",              # the silver sludge
}
TEX = 16                                     # shape texture units: one a voxel, one texture across a block face
FIRE_GLOW = 200                              # the burning fuel's faces (the crucible furnace's coke; a lit firepit is 160)

W, H, D = CELLS_X * B, CELLS_Y * B, CELLS_Z * B
TOP = H                                      # the top: flat, the roof over the hood and the stack's head

# ---------------------------------------------------------------- the fire
# A small fire, as under a sand bath: a grate of four firebars on two iron bearers that run front to back, their ends
# built into the front and back walls; under it the ash pit, open at the front through the ash mouth; over it the fire
# chamber, its roof the sand bath's iron bottom. The fire door and the ash mouth are both in the front, one over the
# other, centred, in one recess in the front face whose iron guides carry the damper (low) and the door (high).
FIRE_X = (4.5, 11.5)                         # the ash pit and the fire chamber: 7 wide, centred, walls of 4.5 each side
FIRE_Z = (8.0, 16.0)                         # 8 deep, under the vessel: a front wall of 8 (the stoke hole and the ash
#                                              mouth run through it) under the cold front of the bench, where the tub stands
CAVE_Y = (1.0, 5.0)                          # the ash pit, on a course of brick
BEARER_Y = (5.0, 6.0)                        # the bearers, along z, their ends in the front and back walls
BEARER_X = ((4.5, 5.5), (10.5, 11.5))        # one under each end of the bars
BEARER_Z = (FIRE_Z[0] - 1.0, FIRE_Z[1] + 1.0)
BAR_Y = (6.0, 7.0)                           # the firebars, along x, wall to wall
BARS, BAR_W = 4, 1.2                         # four rows 1.2 wide across the 8: gaps of 1.07
GRATE_TOP = BAR_Y[1]
FIRE_TOP = 13.0                              # the fire chamber's top: the sand bath's iron bottom

MOUTH = ((5.5, 10.5), (1.0, 3.5))            # the ash mouth (x, y), a passage through the front wall to the ash pit
DOOR_HOLE = ((6.0, 10.0), (8.5, 11.5))       # the fire door's opening (x, y), through the front wall into the chamber
RECESS = ((4.5, 11.5), (1.0, 15.75), (0.0, 1.0))   # one recess in the front face, the damper's and the door's slide
GUIDES = ((4.5, 5.5), (10.5, 11.5))          # iron guides at the recess's sides (x), its full height and depth
LINTEL = ((4.5, 11.5), (3.5, 4.5), (1.0, 2.0))     # the iron lintel over the ash mouth, behind the recess
DOOR_LINTEL = ((4.5, 11.5), (11.5, 12.5), (1.0, 2.0))  # and over the fire door's opening
SLIDE_Z = (0.35, 0.85)                       # the damper's plate and the door's, in the recess, behind the face
DAMPER_H = 3.0                               # the damper: shut, it covers the ash mouth and overlaps its top
DAMPER = {"open": 3.5, "half": 2.25, "shut": 1.0}   # its underside (y) in each place: open clear of the mouth, half over
#                                                     its upper half, shut down on the recess's floor
KNOB = ((7.5, 8.5), (0.1, 0.35), (1.0, 2.0))  # the damper's iron knob (x, z, y over its underside), in the recess
DOOR_H = 4.0                                 # the fire door: shut it covers its opening and overlaps it 0.5 round
DOOR_SHUT_Y = 8.0                            # its underside shut
DOOR_OPEN_Y = 11.75                          # and open, slid up clear of the opening (the rest pose)
DOOR_HANDLE = ((6.75, 9.25), (0.1, 0.35), (0.55, 1.15))   # its handle bar (x, z, y over its underside), in the recess

# ---------------------------------------------------------------- the sand bath and the vessel
# The cast-iron sand bath is let into the masonry over the fire, its rim flush with the bench (the hood's floor), its
# bottom bearing on the fire chamber's walls all round; the fired clay vessel stands in its sand, centred on it and on
# the fire, behind the precipitating tub.
TRAY = ((3.0, 13.0), (FIRE_TOP, 16.0), (6.5, 17.5))   # the bath: a plate 0.75 thick, a rim 0.5 thick, flush at the bench
TRAY_T, RIM_T = 0.75, 0.5
BENCH = TRAY[1][1]                           # the bench: the tray's rim and the brick round it, the hood's floor
SAND_TOP = 15.5                              # the sand's top, half a voxel under the rim
VESSEL_C = (8.0, 12.0)                       # (x, z): the vessel's axis, centred on the bath and the fire
VESSEL_Y = (14.25, 15.0, 23.0, 23.5)         # its bottom (set in the sand, half a voxel of sand under it), the bottom's
#                                              top, the wall's top, the lip's top
VESSEL_A = 3.5                               # its outer apothem (an octagon, 7 across), the wall 0.6
VESSEL_WALL = 0.6
LIP_A = 3.8                                  # the lip: a flare round the mouth, its inside the wall's
INNER_A = VESSEL_A - VESSEL_WALL
LIQUID_A = INNER_A + 0.01                    # a liquid's surface: a hundredth into the walls, so no gap shows round it
CHARGE_Y = (VESSEL_Y[1], 15.6)               # the dore, granulated, on the vessel's floor
GOLD_Y = (VESSEL_Y[1], 15.5)                 # the parted gold, a spongy cake on the floor
ACID_Y = (19.0, 20.0)                        # the acid's surface (or the silver solution's): the vessel two-thirds full
WASH_Y = (18.0, 19.0)                        # the wash water's

# ---------------------------------------------------------------- the hood
# Two piers carry a breast over the front opening, on an iron lintel; the roof spans the piers; the hood's back wall
# stands behind the bath, a flue opening near its top into the stack. The hood is the brickwork's.
HOOD = ((2.5, 13.5), (BENCH, 30.0), (3.0, 17.0))     # the hood's chamber, behind the breast, over the tub and the bath
HOOD_OPEN = ((2.5, 13.5), (BENCH, 28.0), (0.0, 3.0))  # its front opening, under the breast: tall enough to reach over
#                                                       the tub to the vessel, and for the siphon's run over the tub
HOOD_LINTEL = ((1.5, 14.5), (28.0, 29.0), (1.0, 3.0))   # the breast's iron lintel, its ends in the piers, the breast's
#                                                         depth, set back behind the corner angles' face legs
LINTEL_FACE = ((2.5, 13.5), (28.0, 29.0), (0.0, 1.0))   # where it shows, set back a voxel under the brick face
HOOD_FLUE = ((5.0, 11.0), (25.0, 30.0), (17.0, 21.0))   # from the top of the hood's back wall into the stack

# ---------------------------------------------------------------- the precipitating tub
# Period practice (#931): the silver-bearing acid is drawn off the vessel into a separate tub, where copper plates throw
# the silver down as a grey sludge, which is collected, washed and melted. The tub stands on the cold front of the bench,
# in front of the bath, under the hood's open front: a wooden vat (oak boards) with a copper rod across its rims from
# which three copper plates hang into the liquor. A lead siphon draws the solution off: its leg dips into the vessel
# (above the gold), it rests on the vessel's lip, runs forward over the tub, and its outlet leg dips
# into the tub, under the vessel's liquid surface, so it runs. The sludge lies on the tub's bottom; it is the silver's
# hand-draw point (tubCell, tubFace, tub), the gold staying in the vessel.
TUB = ((3.0, 13.0), (BENCH, 21.0), (0.5, 5.5))      # the tub, outside (x, y, z): its floor on the bench
TUB_T = 0.75                                 # its boards
TUB_IN = ((TUB[0][0] + TUB_T, TUB[0][1] - TUB_T), (TUB[1][0] + TUB_T, TUB[1][1]), (TUB[2][0] + TUB_T, TUB[2][1] - TUB_T))
LIQUOR_Y = (18.5, 19.25)                     # the drawn-off liquor's surface in the tub, under the vessel's (ACID_Y)
SLUDGE_Y = (TUB_IN[1][0], 17.25)             # the silver sludge on the tub's bottom
SLUDGE_LUMPS = ((6.0, 2.0, 30.0), (8.2, 4.0, 65.0), (10.9, 1.8, 15.0))   # (x, z, turn): lumps on it, clear of the plates
ROD = (0.6, 3.4)                             # the copper rod along x across the tub's rims: its section, its axis (z)
PLATES = (5.0, 7.0, 11.0)                    # the copper plates' middles (x), hung from the rod, thin across x
PLATE = (0.3, 1.2, 18.25)                    # a plate's thickness (x), its half depth (z) about the rod, its foot (y)
SIPHON = (0.8, (9.0, 9.8), (9.8, 10.6), 16.8, 1.5, 18.0)   # the siphon: its section; its x; the vessel leg's z; that leg's
#                                                             foot (y, over the gold); the tub leg's z (from); its foot (y)

# ---------------------------------------------------------------- the flue, the stack, the outlet
FIRE_FLUE = ((6.0, 10.0), (8.5, 12.0), (FIRE_Z[1], 21.0))   # from the top of the fire chamber's back wall to the stack
STACK = ((5.0, 11.0), (8.5, TOP), (21.0, 27.0))             # the stack, in the back row behind the hood, to the outlet:
#                                                              6 square, centred in the back cell, under the chimney's line
CURB = ((3.0, 13.0), (30.0, TOP), (19.0, 29.0))  # the outlet's curb, of the lining's refractory brick, flush: a rim 1
CURB_RIM = 1.0                                   # wide round the cap's seat, the seat a ledge 1 wide round the mouth
CAP_Y = (31.5, TOP)                          # the cap, a flat iron plate, set flush in it
CAP_SEAT = ((CURB[0][0] + CURB_RIM, CURB[0][1] - CURB_RIM), CAP_Y, (CURB[2][0] + CURB_RIM, CURB[2][1] - CURB_RIM))

# ---------------------------------------------------------------- the brick
# Two roles. The brickwork (the base, the ash pit and the ash mouth, the walls, the bench, the hood, the stack's outer
# walls) is any one type of the game's fired brick (BRICKS); the hot faces (the fire chamber's walls from the bearers
# up, its flue, the stack and the outlet's curb) are lined with tier-3 refractory brick LINING_T thick.
LINING_T = 3.0
MIN_WALL = 4.5                               # the least brick beside the fire chamber (the side walls: a half-brick skin
#                                              outside the lining); the front wall is 3 behind the recess
FRONT_WALL = 7.0                             # the front wall behind the recess, the stoke hole's depth

# The iron binding is angle iron (the owner's rule for every furnace-like brickwork): an L section wrapping each of the
# block's four vertical corners, one leg on each face, its inside corner bedded against the brick's outside corner, so
# it locks the brick in on both faces; and along all four top edges an angle wrapping that edge (a leg down the face, a
# leg on the top), a ring whose ends meet the corner angles and each other, so the binding is one closed frame. The
# brick is cut back by a leg's thickness where the iron lies, so the angles' outer faces are flush with the block's:
# on a side face only the corner angles' side legs and the side's top angle show, and two vessels side by side in a
# row meet there, angle against angle and brick against brick.
ANGLE_T = 1.0                                # a leg's thickness
ANGLE_LEG = 2.5                              # the corner angles' legs, from the corner
BAND_FACE = 3.0                              # the top angles' legs: down the face, from the top
BAND_TOP = 2.0                               #                      and on the top, from the face

# ---------------------------------------------------------------- the build
# Built in stages, as the owner settled them (2026-10-15; the rig's `build`, one requires value a stage, fitted in
# order; their states are the viewer's Build select): the iron frame, a grid recipe placed as the machine, then 1 the
# brickwork (with the hood's lintel at no cost of its own), 2 the lining, 3 the sand bath and the fire door (the damper
# with them, in one of its places), 4 the vessel and the tub, and 5 the services: the lead siphon, last as on every
# machine, which hangs on the vessel's lip over the tub and so waits for both. The outlet's cap is fitted by hand. The
# brickwork's and the lining's counts are measured from the model's volume at the game's rates, as the crucible
# furnace's: a claybricks block is 2 fired bricks, a refractory brick block 4, rounded up, the lining's fire clay one
# for every LINING_PER_FIRECLAY of its bricks. The frame's and the bath's iron is the settled rate (128 voxels of iron
# an ingot, a plate 2, rods counted, the rest nails and strips at 4 an ingot) over the iron measured in the model.
FRAME_RECIPE = (("game:rod-iron", 2), ("game:metalplate-iron", 2), ("game:metalnailsandstrips-iron", 6))
BRICKS_PER_BLOCK = 2
LINING_PER_BLOCK = 4
LINING_PER_FIRECLAY = 2.5
FIRED_BRICK = "game:burnedbrick-*"
LINING_BRICK = "game:refractorybrick-fired-tier3"
FIRECLAY = "game:clay-fire"
SANDBATH_STAGE = (("game:sand-*", 1), ("game:metalnailsandstrips-iron", 5))
#                                              the bath's sand; its tray, the fire door and the damper
VESSEL_ITEM = "game:crucible-*-fired"        # the parting vessel: a fired clay crucible, any colour (the owner's ruling)
VESSELTUB_STAGE = ((VESSEL_ITEM, 1), ("game:plank-oak", 1))   # the vessel; the tub's boards
SERVICES_STAGE = (("seraphhorizons:pipesection-lead", 1),)    # the siphon: the pack's lead pipe section
CAP_ITEM = ("game:metalplate-iron", 1)
STAGES = ("brickwork", "lining", "sandbath", "vesseltub", "services")   # after the frame, in order: their requires
STAGE_PARTS = {                              # the parts each stage fits
    "brickwork": ("brickwork", "hoodiron"),
    "lining": ("lining",),
    "sandbath": ("bath", "door"),
    "vesseltub": ("vessel", "tub"),
    "services": ("siphon",),
}
STAGE_STATES = (                             # the viewer's Build select, a state a stage: (id, the stages fitted)
    ("frame", ()),
    *((sid, STAGES[:n + 1]) for n, sid in enumerate(STAGES)),
)
COMPLETE = STAGES[-1]                        # the build finished: the contents and the fire are drawn from here

# The fired bricks the brickwork takes: game:burnedbrick-{type}, every type the game has, each with the block its
# bricks make and that block's texture, which the renderer sets the `brick` code to (the crucible furnace's table).
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
BRICK_DEFAULT = "red"                        # the shape's own `brick` is its colour layer (a shape names one texture)

# The scenario's states, as the viewer gives them (site/models.json; tools/tests holds the two together). Each is the
# requires values fitted.
VESSEL_STATES = (
    ("empty", "Empty", ()),
    ("charged", "Charged: granulated dore", ("charge",)),
    ("acid", "The acid poured on", ("charge", "acid")),
    ("parting", "Parting: the silver dissolved, the gold left", ("gold", "solution")),
    ("washing", "The solution drawn off into the tub, the gold washed", ("gold", "wash")),
    ("parted", "Parted: the gold left, washed", ("gold",)),
)
TUB_STATES = (
    ("empty", "Empty", ()),
    ("copper", "Copper plates hung", ("copper",)),
    ("drawn", "The solution drawn off into it", ("copper", "liquor")),
    ("precipitating", "The silver coming down on the copper", ("copper", "liquor", "silver")),
    ("sludge", "The liquor run off, the sludge to draw", ("copper", "silver")),
)
FIRE_STATES = (
    ("none", "No fire", ()),
    ("laid", "Laid, not lit", ("fuel",)),
    ("burning", "Burning", ("fire",)),
)
DAMPERS = ("open", "half", "shut")
CONTENTS = ("fuel", "fire", "charge", "acid", "solution", "gold", "wash", "copper", "liquor", "silver")


# ---------------------------------------------------------------- box helpers (the crucible furnace's)
AXES_UV = {"north": (0, 1), "south": (0, 1), "east": (2, 1), "west": (2, 1), "up": (0, 2), "down": (0, 2)}


def skin(el, tex):
    """Every face takes `tex`, its UVs a region of the texture in proportion to the face's size."""
    el.faces = {}
    for d, (u, v) in AXES_UV.items():
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
    """An axis-aligned element whose faces take their UVs from where they stand in their block, so boxes side by
    side continue one texture across a block face with no seam (the brickwork is many boxes). The game's face
    directions, as the crucible furnace reads them."""
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


def wbox(lo, hi, name, part, tex):
    """A box with world-mapped UVs (`world_skin`)."""
    return world_skin(box(lo, hi, name, part, tex), tex)


def oct_disc(cx, cz, y0, y1, a, name, part, tex):
    """A solid octagonal disc about a vertical axis, apothem `a`: four strips 2a long and one side wide, at 0, 45, 90
    and 135 degrees about y (0 and 90 built square to the axes). Their union is the octagon exactly."""
    s = a * OCT
    out = []
    for i, ang in enumerate((0.0, 45.0, 90.0, 135.0), 1):
        if ang == 90.0:
            el = box([cx - s, y0, cz - a], [cx + s, y1, cz + a], f"{name}_{i}", part, tex)
        else:
            el = box([cx - a, y0, cz - s], [cx + a, y1, cz + s], f"{name}_{i}", part, tex)
            if ang:
                rotate([el], "y", ang, (cx, 0.0, cz))
        out.append(el)
    return out


def oct_slab(cx, cz, y0, y1, a0, a1, length, normal_deg, name, part, tex):
    """A box `length` long across a direction `normal_deg` degrees from +x towards +z, from a0 to a1 along it from
    the axis (cx, cz): one side of an octagonal ring, or the sand's corner fill. The four square directions are built
    square to the axes."""
    n = round(normal_deg) % 360
    h = length / 2
    if n == 0:
        return box([cx + a0, y0, cz - h], [cx + a1, y1, cz + h], name, part, tex)
    if n == 180:
        return box([cx - a1, y0, cz - h], [cx - a0, y1, cz + h], name, part, tex)
    if n == 90:
        return box([cx - h, y0, cz + a0], [cx + h, y1, cz + a1], name, part, tex)
    if n == 270:
        return box([cx - h, y0, cz - a1], [cx + h, y1, cz - a0], name, part, tex)
    el = box([cx + a0, y0, cz - h], [cx + a1, y1, cz + h], name, part, tex)
    turn = -float(n) if n <= 180 else 360.0 - n          # rot y by -n carries +x onto the normal; kept in (-180, 180]
    rotate([el], "y", turn, (cx, 0.0, cz))
    return el


def oct_tube(cx, cz, y0, y1, a, t, name, part, tex):
    """An octagonal tube about a vertical axis, outer apothem `a`, wall `t`: eight slabs, each as long as a side of
    the octagon at its outer face; each slab's inner corners lie inside its neighbours, so the ring is closed."""
    return [oct_slab(cx, cz, y0, y1, a - t, a, 2 * a * OCT, 45.0 * k, f"{name}_{k + 1}", part, tex) for k in range(8)]


def lump(c, size, turns, name, part, tex):
    """A small cube turned about its own centre by `turns` [(axis, degrees), ...] in order."""
    el = box([c[k] - size / 2 for k in range(3)], [c[k] + size / 2 for k in range(3)], name, part, tex)
    for axis, deg in turns:
        rotate([el], axis, deg, c)
    return el


def bar(c, size, turns, name, part, tex):
    """A small bar of `size` (x, y, z) turned about its own centre."""
    el = box([c[k] - size[k] / 2 for k in range(3)], [c[k] + size[k] / 2 for k in range(3)], name, part, tex)
    for axis, deg in turns:
        rotate([el], axis, deg, c)
    return el


def split_blocks(name, lo, hi):
    """A long box cut at the blocks' boundaries along its longest axis, so its texture is not stretched."""
    axis = max(range(3), key=lambda k: hi[k] - lo[k])
    cuts = [lo[axis]] + [B * c for c in range(1, 8) if lo[axis] + 1e-6 < B * c < hi[axis] - 1e-6] + [hi[axis]]
    if len(cuts) == 2:
        return [(name, lo, hi)]
    out = []
    for n, (a, b) in enumerate(zip(cuts, cuts[1:]), 1):
        l2, h2 = list(lo), list(hi)
        l2[axis], h2[axis] = a, b
        out.append((f"{name}{n}", l2, h2))
    return out


def span(t):
    """A ((x0, x1), (y0, y1), (z0, z1)) as (lo, hi)."""
    return [t[0][0], t[1][0], t[2][0]], [t[0][1], t[1][1], t[2][1]]


# ---------------------------------------------------------------- the brickwork
def angles():
    """The binding's angles, as (name, corner, legs): each leg (lo, hi). The corner angles (`fr_angle_<n|s><w|e>`), the
    full height at each vertical corner: a leg on the front or back face (`a`, ANGLE_LEG wide) and a leg on the side
    face (`b`, from the first leg's back to ANGLE_LEG from the corner), the two meeting in the corner. The top angles
    (`fr_band_<n|s|w|e>`), along all four top edges, a ring joining the corner angles: a leg down the face (`a`,
    BAND_FACE, between the corner angles' legs on that face, meeting them end to end) and a leg on the top (`b`, to
    BAND_TOP from the face). The front's and the back's top legs run the block's width between the corner angles' side
    legs, which close their ends; the sides' run between the front's and the back's top legs, which close theirs, so
    the top is iron BAND_TOP wide all round and each corner a closed joint. `corner` is the brick's outside corner the
    angle wraps: (axis, the face's coordinate) for each of its two faces."""
    t, g, bt = ANGLE_T, ANGLE_LEG, BAND_TOP
    out = []
    for face, (z0, z1), zf, zin in (("n", (0.0, t), 0.0, (t, g)), ("s", (D - t, D), D, (D - g, D - t))):
        for side, (x0, x1), xf, xin in (("w", (0.0, g), 0.0, (0.0, t)), ("e", (W - g, W), W, (W - t, W))):
            out.append((f"fr_angle_{face}{side}", ((2, zf), (0, xf)),
                        [([x0, 0.0, z0], [x1, H, z1]), ([xin[0], 0.0, zin[0]], [xin[1], H, zin[1]])]))
    for face, zf, za, top in (("n", 0.0, (0.0, t), (t, bt)), ("s", D, (D - t, D), (D - bt, D - t))):
        out.append((f"fr_band_{face}", ((2, zf), (1, TOP)),
                    [([g, TOP - BAND_FACE, za[0]], [W - g, TOP, za[1]]), ([t, TOP - t, top[0]], [W - t, TOP, top[1]])]))
    for side, xf, xa, top in (("w", 0.0, (0.0, t), (t, bt)), ("e", W, (W - t, W), (W - bt, W - t))):
        out.append((f"fr_band_{side}", ((0, xf), (1, TOP)),
                    [([xa[0], TOP - BAND_FACE, g], [xa[1], TOP, D - g]), ([top[0], TOP - t, bt], [top[1], TOP, D - bt])]))
    return out


def binding():
    """The binding's elements, as (name, lo, hi): every angle's two legs (`angles`), each long leg cut at the blocks'
    boundaries. The side faces are plain brick but for the corner angles' side legs, flush."""
    out = []
    for name, _, legs in angles():
        for leg, (lo, hi) in zip(("a", "b"), legs):
            out += split_blocks(f"{name}_{leg}", lo, hi)
    return out


def ties():
    """The frame's ties, as (name, lo, hi): the least iron that joins each piece of the frame to the ground before the
    brick is in, all of it in the brickwork (voids are cut for it), so it is hidden once the brick is in. A post under
    each end of the ash mouth's lintel, behind the recess (the guides stand against the lintel's face and carry the
    fire door's lintel), and a post under each end of each bearer, in the front and back walls, half a voxel clear of
    the ash pit and the ash mouth."""
    out = []
    (lx, ly, lz) = LINTEL
    for side, (x0, x1) in (("w", (lx[0] - 1.0, lx[0])), ("e", (lx[1], lx[1] + 1.0))):
        out.append((f"fr_tie_lintel_{side}", [x0, 0.0, lz[0]], [x1, ly[1], lz[1]]))
    for n, (x0, x1) in enumerate(BEARER_X, 1):
        px = (x0, x0 + 0.5) if n == 1 else (x1 - 0.5, x1)
        for end, (z0, z1) in (("n", (BEARER_Z[0], BEARER_Z[0] + 0.5)), ("s", (BEARER_Z[1] - 0.5, BEARER_Z[1]))):
            out.append((f"fr_tie_bearer{n}{end}", [px[0], 0.0, z0], [px[1], BEARER_Y[0], z1]))
    return out


def brick_voids():
    """Every space cut out of the brick, as (lo, hi) in voxels: the ash pit and the fire chamber; the ash mouth's and
    the fire door's passages and the recess in the front face; the sand bath's seat; the hood's chamber and its front
    opening; the fire's flue, the hood's flue, the stack and the cap's seat in the curb; and the room for the iron set
    into the brick (the bearers' ends, the lintels, the hood's lintel, the binding,
    the frame's ties). The top is flat but for the outlet."""
    v = [([FIRE_X[0], CAVE_Y[0], FIRE_Z[0]], [FIRE_X[1], FIRE_TOP, FIRE_Z[1]])]      # the ash pit and the fire chamber
    for x0, x1 in BEARER_X:
        v.append(([x0, BEARER_Y[0], BEARER_Z[0]], [x1, BEARER_Y[1], BEARER_Z[1]]))      # the bearers, ends in the walls
    (mx0, mx1), (my0, my1) = MOUTH
    v.append(([mx0, my0, 0.0], [mx1, my1, FIRE_Z[0]]))                                  # the ash mouth
    (dx0, dx1), (dy0, dy1) = DOOR_HOLE
    v.append(([dx0, dy0, 0.0], [dx1, dy1, FIRE_Z[0]]))                                  # the fire door's opening
    for t in (RECESS, LINTEL, DOOR_LINTEL, TRAY, HOOD, HOOD_OPEN, HOOD_LINTEL, LINTEL_FACE, HOOD_FLUE, FIRE_FLUE, STACK,
              CAP_SEAT):
        v.append(span(t))
    v += [(lo, hi) for _, lo, hi in binding()]
    v += [(lo, hi) for _, lo, hi in ties()]
    return v


def lining_zones():
    """Where the brick is the lining's, as (lo, hi): LINING_T round the fire chamber from the bearers' level up to its
    top, round the fire's flue, and round the stack from its floor up to the top (the outlet's curb with it). The rest
    of the brick is the brickwork's: the base, the ash pit and the ash mouth, the walls' outer skins, the bench, the
    hood and its flue's way through the hood's back wall."""
    t = LINING_T
    (fx0, fx1), (fy0, fy1), (fz0, fz1) = FIRE_FLUE
    (sx0, sx1), (sy0, _), (sz0, sz1) = STACK
    return [([FIRE_X[0] - t, BEARER_Y[0], FIRE_Z[0] - t], [FIRE_X[1] + t, FIRE_TOP, FIRE_Z[1] + t]),
            ([fx0 - t, fy0 - t, fz0], [fx1 + t, fy1 + t, fz1]),
            ([sx0 - t, sy0 - t, sz0 - t], [sx1 + t, TOP, sz1 + t])]


def _inside(p, lo, hi):
    return all(lo[k] < p[k] < hi[k] for k in range(3))


def brick_lattice():
    """The brick as a lattice: the breakpoints along each axis (every void's faces, the lining's zones' and every
    block's), and for each lattice box what it is: "bk" (the brickwork's), "ln" (the lining's) or None (a void)."""
    voids = brick_voids()
    zones = lining_zones()
    axes = []
    for k, n in enumerate((CELLS_X, CELLS_Y, CELLS_Z)):
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
    is left out (inside the wall): a brickwork face against brickwork, a lining face against either (the lining goes in
    after the brickwork). A brickwork face against the lining is kept: until the lining is in, it is the wall's face."""
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
        el = wbox([xs[i0], ys[j0], zs[k0]], [xs[i1 + 1], ys[j1 + 1], zs[k1 + 1]], f"{m0}_brick_{count[m0]:03d}", part, tex)
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


# ---------------------------------------------------------------- builders: the frame and the hood's iron
def build_iron():
    """The iron frame (`frame`): what the frame stage puts down, all of it standing on its own, bedded in the brick
    once it is in. The two bearers (front to back) and the four firebars across them, the ash mouth's lintel and the
    fire door's, the recess's guides (they stand against both lintels' faces), the binding's angles, and the frame's
    ties (`ties`, hidden in the brickwork) under the bearers and the ash mouth's lintel."""
    f, t = "frame", "iron"
    out = []
    for n, (x0, x1) in enumerate(BEARER_X, 1):
        out.append(box([x0, BEARER_Y[0], BEARER_Z[0]], [x1, BEARER_Y[1], BEARER_Z[1]], f"fr_bearer{n}", f, t))
    gap = (FIRE_Z[1] - FIRE_Z[0] - BARS * BAR_W) / (BARS - 1)
    for n in range(BARS):
        z0 = FIRE_Z[0] + n * (BAR_W + gap)
        out.append(box([FIRE_X[0], BAR_Y[0], z0], [FIRE_X[1], BAR_Y[1], z0 + BAR_W], f"fr_bar{n + 1}", f, t))
    for name, tt in (("fr_lintel_ash", LINTEL), ("fr_lintel_door", DOOR_LINTEL)):
        lo, hi = span(tt)
        out.append(box(lo, hi, name, f, t))
    (_, ry, rz) = RECESS
    for side, (x0, x1) in zip(("w", "e"), GUIDES):
        out.append(box([x0, ry[0], rz[0]], [x1, ry[1], rz[1]], f"fr_guide_{side}", f, t))
    out += [box(lo, hi, name, f, t) for name, lo, hi in binding()]
    out += [box(lo, hi, name, f, t) for name, lo, hi in ties()]
    return out


def build_hood_iron():
    """The hood's iron (`hoodiron`, fitted with the brickwork, which carries it): the breast's lintel over the hood's
    front opening, its ends in the piers."""
    lo, hi = span(HOOD_LINTEL)
    return [box(lo, hi, "hi_lintel", "hoodiron", "iron")]


def build_frame():
    """Everything that stands still and is not a fitting or the contents: the brick (brickwork and lining), the iron
    frame and the hood's iron."""
    return build_brick() + build_iron() + build_hood_iron()


# ---------------------------------------------------------------- builders: the fittings
def build_bath():
    """The sand bath (`bath`): a cast-iron tray let into the masonry over the fire, its bottom the fire chamber's roof
    bearing on its walls all round, its rim flush with the bench; and its sand, under the vessel and round it to half a
    voxel under the rim (strips and corner fills round the vessel's octagon, as the crucible furnace's coke round its
    pots)."""
    p = "bath"
    (x0, x1), (y0, y1), (z0, z1) = TRAY
    r = RIM_T
    yb = y0 + TRAY_T
    out = [box([x0, y0, z0], [x1, yb, z1], "bath_tray", p, "iron"),
           box([x0, yb, z0], [x1, y1, z0 + r], "bath_rim_n", p, "iron"),
           box([x0, yb, z1 - r], [x1, y1, z1], "bath_rim_s", p, "iron"),
           box([x0, yb, z0 + r], [x0 + r, y1, z1 - r], "bath_rim_w", p, "iron"),
           box([x1 - r, yb, z0 + r], [x1, y1, z1 - r], "bath_rim_e", p, "iron")]
    sx0, sx1, sz0, sz1 = x0 + r, x1 - r, z0 + r, z1 - r
    vy0 = VESSEL_Y[0]
    out.append(box([sx0, yb, sz0], [sx1, vy0, sz1], "bath_sand_under", p, "sand"))
    cx, cz = VESSEL_C
    a = VESSEL_A
    out += [box([sx0, vy0, sz0], [sx1, SAND_TOP, cz - a], "bath_sand_n", p, "sand"),
            box([sx0, vy0, cz + a], [sx1, SAND_TOP, sz1], "bath_sand_s", p, "sand"),
            box([sx0, vy0, cz - a], [cx - a, SAND_TOP, cz + a], "bath_sand_w", p, "sand"),
            box([cx + a, vy0, cz - a], [sx1, SAND_TOP, cz + a], "bath_sand_e", p, "sand")]
    depth = a * (math.sqrt(2.0) - 1.0)           # from a diagonal flat to its square's corner
    for name, deg in (("se", 45.0), ("sw", 135.0), ("nw", 225.0), ("ne", 315.0)):
        out.append(oct_slab(cx, cz, vy0, SAND_TOP, a, a + depth, 2 * a * OCT, deg, f"bath_sand_{name}", p, "sand"))
    return out


def build_vessel():
    """The parting vessel (`vessel`): a fired clay pot (a crucible), an octagonal barrel 7 across with a flat bottom and a flared lip,
    standing in the sand, its mouth open (the acid and the gold show): the dore, the acid and the wash water's bucket go
    in there, and the gold comes out."""
    p = "vessel"
    cx, cz = VESSEL_C
    yb, yf, yw, yl = VESSEL_Y
    out = oct_disc(cx, cz, yb, yf, VESSEL_A, "vessel_bottom", p, "ceramic")
    out += oct_tube(cx, cz, yf, yw, VESSEL_A, VESSEL_WALL, "vessel_wall", p, "ceramic")
    out += oct_tube(cx, cz, yw, yl, LIP_A, LIP_A - INNER_A, "vessel_lip", p, "ceramic")
    return out


def build_door():
    """The fire door (`door`): an iron plate sliding in the recess's guides over the fire door's opening, with a handle
    bar, authored open (slid up clear of the opening: the rest pose); depth slides it down shut."""
    p = "door"
    x0, x1 = GUIDES[0][1], GUIDES[1][0]
    y0 = DOOR_OPEN_Y
    (hx, hz, hy) = DOOR_HANDLE
    return [box([x0, y0, SLIDE_Z[0]], [x1, y0 + DOOR_H, SLIDE_Z[1]], "door_plate", p, "iron"),
            box([hx[0], y0 + hy[0], hz[0]], [hx[1], y0 + hy[1], hz[1]], "door_handle", p, "iron")]


def build_damper(place):
    """The ash-pit damper (`damper<place>`): an iron plate sliding in the recess's guides over the ash mouth, with a
    knob, in one of its three places (DAMPER). Each place is its own part; gameplay draws the one the damper is in."""
    p = f"damper{place}"
    y0 = DAMPER[place]
    x0, x1 = GUIDES[0][1], GUIDES[1][0]
    (kx, kz, ky) = KNOB
    return [box([x0, y0, SLIDE_Z[0]], [x1, y0 + DAMPER_H, SLIDE_Z[1]], f"{p}_plate", p, "iron"),
            box([kx[0], y0 + ky[0], kz[0]], [kx[1], y0 + ky[1], kz[1]], f"{p}_knob", p, "iron")]


def build_cap():
    """The outlet's cap (`outletcap`): a flat iron plate set flush in the curb's seat, closing the outlet of a vessel
    whose chimney is another's in the row."""
    lo, hi = span(CAP_SEAT)
    return [box(lo, hi, "outletcap_plate", "outletcap", "iron")]


# ---------------------------------------------------------------- builders: the fire and the vessel's contents
FUEL_Y = (GRATE_TOP, 8.5)                    # the fuel's bed on the bars, to the fire door's sill
LUMP = 1.2
LUMP_PLACES = ((6.0, 2.0), (10.0, 2.5), (7.5, 6.0), (10.0, 6.5))   # (x, z from the chamber's front): lumps on the bed
LUMP_TURNS = ((("y", 28.0), ("x", 21.0)), (("y", 63.0), ("z", 17.0)), (("y", 11.0), ("x", -24.0)), (("y", 47.0), ("z", -19.0)))
CINDERS = ((6.5, 3.0, 2.0, 1.6, 20.0), (9.2, 5.0, 1.8, 1.4, -35.0), (8.0, 1.6, 1.6, 1.2, 60.0))   # (x, z from the
#                                                                                                     front, w, d, turn)


def build_fuel(kind):
    """The fire laid (`fuel`), or the same fuel burning (`fire`, kind "fire": the ember texture, its faces glowing): a
    bed of fuel across the grate to the fire door's sill, with lumps lying on it; burning, cinders have fallen through
    the bars onto the ash pit's floor."""
    p = kind
    tex = "fuel" if kind == "fuel" else "ember"
    out = [box([FIRE_X[0], FUEL_Y[0], FIRE_Z[0]], [FIRE_X[1], FUEL_Y[1], FIRE_Z[1]], f"{p}_bed", p, tex)]
    for n, (x, z) in enumerate(LUMP_PLACES):
        out.append(lump([x, FUEL_Y[1] + 0.1, FIRE_Z[0] + z], LUMP, LUMP_TURNS[n % 4], f"{p}_lump{n + 1}", p, tex))
    if kind == "fire":
        for n, (x, z, w, d, deg) in enumerate(CINDERS, 1):
            out.append(bar([x, CAVE_Y[0] + 0.3, FIRE_Z[0] + z], (w, 0.6, d), (("y", deg),), f"{p}_cinder{n}", p, tex))
    return out


GRAINS = ((-1.2, -0.8, 20.0), (0.9, -1.1, 55.0), (0.2, 0.9, 35.0), (-0.9, 1.2, 70.0), (1.4, 0.6, 10.0))   # (dx, dz, turn)
GOLD_LUMPS = ((-0.8, -0.6, 25.0), (1.0, 0.4, 60.0), (-0.2, 1.3, 40.0))


def build_charge():
    """The charge (`charge`): granulated dore lying on the vessel's floor, a layer with grains on it."""
    p = "charge"
    cx, cz = VESSEL_C
    out = oct_disc(cx, cz, CHARGE_Y[0], CHARGE_Y[1], LIQUID_A, f"{p}_layer", p, "dore")
    for n, (dx, dz, deg) in enumerate(GRAINS, 1):
        out.append(lump([cx + dx, CHARGE_Y[1] + 0.2, cz + dz], 0.7, (("y", deg), ("x", 30.0)), f"{p}_grain{n}", p, "dore"))
    return out


def build_gold():
    """The parted gold (`gold`): a spongy cake on the vessel's floor, with lumps on it, left when the silver has gone
    into the acid."""
    p = "gold"
    cx, cz = VESSEL_C
    out = oct_disc(cx, cz, GOLD_Y[0], GOLD_Y[1], LIQUID_A, f"{p}_cake", p, "gold")
    for n, (dx, dz, deg) in enumerate(GOLD_LUMPS, 1):
        out.append(lump([cx + dx, GOLD_Y[1] + 0.25, cz + dz], 0.9, (("y", deg), ("z", 25.0)), f"{p}_lump{n}", p, "gold"))
    return out


def build_liquid(part, ys, tex):
    """A liquid in the vessel (`acid`, `solution`, `wash`): its surface, a voxel thick, at its level."""
    cx, cz = VESSEL_C
    return oct_disc(cx, cz, ys[0], ys[1], LIQUID_A, f"{part}_surface", part, tex)


def build_tub():
    """The precipitating tub (`tub`, a fitting): a wooden vat of oak boards standing on the cold front of the bench,
    under the hood's open front: a floor and four walls."""
    p = "tub"
    (x0, x1), (y0, y1), (z0, z1) = TUB
    t = TUB_T
    yf = y0 + t
    return [box([x0, y0, z0], [x1, yf, z1], "tub_floor", p, "oak"),
            box([x0, yf, z0], [x1, y1, z0 + t], "tub_n", p, "oak"),
            box([x0, yf, z1 - t], [x1, y1, z1], "tub_s", p, "oak"),
            box([x0, yf, z0 + t], [x0 + t, y1, z1 - t], "tub_w", p, "oak"),
            box([x1 - t, yf, z0 + t], [x1, y1, z1 - t], "tub_e", p, "oak")]


def build_siphon():
    """The siphon (`siphon`, a fitting): a lead pipe bent over the vessel's lip, its leg dipping into the vessel over the
    gold, its run lying on the lip and going forward over the tub, its outlet leg dipping into the tub,
    under the vessel's liquid surface (so it runs: the solution drawn off into the tub)."""
    p = "siphon"
    s, (x0, x1), (vz0, vz1), vfoot, tz0, tfoot = SIPHON
    ry0 = VESSEL_Y[3]                        # the run lies on the lip
    ry1 = ry0 + s
    return [box([x0, vfoot, vz0], [x1, ry1, vz1], "siphon_vesselleg", p, "lead"),
            box([x0, ry0, tz0], [x1, ry1, vz0], "siphon_run", p, "lead"),
            box([x0, tfoot, tz0], [x1, ry0, tz0 + s], "siphon_tubleg", p, "lead")]


def build_copper():
    """The copper plates (`copper`): a copper rod across the tub's rims along x, and three plates hung from it into
    the liquor, thin across x, clear of the siphon's leg; the silver comes down on them."""
    p = "copper"
    s, rz = ROD
    (x0, x1), (_, y1), _ = TUB
    out = [box([x0, y1, rz - s / 2], [x1, y1 + s, rz + s / 2], "copper_rod", p, "copper")]
    t, hz, foot = PLATE
    for n, x in enumerate(PLATES, 1):
        out.append(box([x - t / 2, foot, rz - hz], [x + t / 2, y1 + s / 2, rz + hz], f"copper_plate{n}", p, "copper"))
    return out


def build_liquor():
    """The liquor in the tub (`liquor`): the drawn-off solution's surface, a level under the vessel's."""
    (x0, x1), _, (z0, z1) = TUB_IN
    return [box([x0, LIQUOR_Y[0], z0], [x1, LIQUOR_Y[1], z1], "liquor_surface", "liquor", "solution")]


def build_silver():
    """The silver (`silver`): the grey sludge the copper throws down, on the tub's bottom, with lumps on it."""
    p = "silver"
    (x0, x1), _, (z0, z1) = TUB_IN
    out = [box([x0, SLUDGE_Y[0], z0], [x1, SLUDGE_Y[1], z1], "silver_sludge", p, "silver")]
    for n, (x, z, deg) in enumerate(SLUDGE_LUMPS, 1):
        out.append(lump([x, SLUDGE_Y[1] + 0.15, z], 0.7, (("y", deg), ("x", 20.0)), f"silver_lump{n}", p, "silver"))
    return out


def build():
    els = build_door()
    for place in DAMPERS:
        els += build_damper(place)
    els += build_bath() + build_vessel() + build_tub() + build_siphon()
    els += build_fuel("fuel") + build_fuel("fire") + build_charge()
    els += build_liquid("acid", ACID_Y, "acid") + build_liquid("solution", ACID_Y, "solution")
    els += build_gold() + build_liquid("wash", WASH_Y, "water")
    els += build_copper() + build_liquor() + build_silver()
    return els + build_cap() + build_frame()


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def door_drivers():
    """Shut as depth goes 0 -> 1: slid down its guides from open (DOOR_OPEN_Y) to shut (DOOR_SHUT_Y). It also carries
    theta at ratio 0 first (the clock; it moves nothing)."""
    pivot = pt((GUIDES[0][1] + GUIDES[1][0]) / 2, DOOR_OPEN_Y, SLIDE_Z[0])
    return [{"type": "rotate", "axis": "x", "pivot": pivot, "ratio": 0.0},
            {"type": "step", "motion": "slide", "axis": "y", "from": 0.0, "to": 1.0,
             "amount": r6((DOOR_SHUT_Y - DOOR_OPEN_Y) / B)}]


PART_IDS = (("brickwork", "hoodiron", "lining", "bath", "vessel", "tub", "siphon", "door") + tuple(f"damper{d}" for d in DAMPERS)
            + CONTENTS + ("outletcap",))
MATCH = {"brickwork": "bk_*", "hoodiron": "hi_*", "lining": "ln_*"}
REQUIRES_OF = {pid: sid for sid, pids in STAGE_PARTS.items() for pid in pids}


def requires_of(pid):
    """The requires value a part is fitted by (None for the iron frame)."""
    return None if pid == "frame" else REQUIRES_OF.get(pid, pid)


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    parts = []
    for pid in PART_IDS:
        parts.append({"id": pid, "match": [MATCH.get(pid, f"{pid}_*")], "requires": requires_of(pid),
                      "drivers": door_drivers() if pid == "door" else []})
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []})
    for p in parts:
        p["ride"] = None
        for d in p["drivers"]:
            validate_driver(d)
    return [{"id": p["id"], "match": p["match"], "requires": p["requires"], "drivers": p["drivers"], "ride": p["ride"]}
            for p in parts]


# ---------------------------------------------------------------- poses
# A pose is (theta, depth, scene): the clock, the fire door, and what is fitted. A scene is (the vessel's state, the
# tub's, the fire's, the damper's place, whether the outlet is capped, the build's state): gameplay's requires. The
# contents and the fire are fitted only once the build is complete (the services), the damper with the sand bath and
# the fire door, the cap once the curb is in (the lining).
DEFAULT_SCENE = ("parting", "precipitating", "burning", "open", False, COMPLETE)
REST = (0.0, 0.0, DEFAULT_SCENE)
SHUT = 1.0


def inputs_of(pose):
    return {"theta": pose[0], "depth": pose[1]}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


def fitted(scene):
    """The requires values fitted in a scene."""
    vessel, tub, fire, damper, capped, stage = scene
    out: set[str] = set(dict(STAGE_STATES)[stage])
    if COMPLETE in out:
        out |= set(dict((s[0], s[2]) for s in VESSEL_STATES)[vessel])
        out |= set(dict((s[0], s[2]) for s in TUB_STATES)[tub])
        out |= set(dict((s[0], s[2]) for s in FIRE_STATES)[fire])
    if "sandbath" in out:
        out.add(f"damper{damper}")
    if capped and "lining" in out:
        out.add("outletcap")
    return out


def on_show(part, scene):
    """Whether a part is drawn in a scene: the iron frame always, the rest when what fits them is fitted."""
    return part == "frame" or requires_of(part) in fitted(scene)


def shown(parts, posed_els, pose):
    """The posed elements as they can be seen in the pose's scene: every part not drawn moved far away, each part by
    its own distance (copies, in order), so a hidden part's own elements still lie as they do together."""
    index = {p["id"]: n for n, p in enumerate(parts)}
    out = []
    for e in posed_els:
        if not on_show(e.part, pose[2]):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 * (1 + index[e.part]), e.c[2]]
        out.append(e)
    return out


# ---------------------------------------------------------------- the rig file
def footprint():
    """The four cells: the front row (the fire, the bath and the hood) and the back row (the stack)."""
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("vessel", "tub", "firedoor", "ash")


def vessel_point():
    """The middle of the vessel's mouth: where the charge goes in and the gold comes out, and the fumes rise."""
    return (VESSEL_C[0], VESSEL_Y[3], VESSEL_C[1])


def tub_point():
    """The middle of the tub's mouth: the silver's hand-draw point (the sludge drawn from its bottom), the copper put
    in and taken out."""
    (x0, x1), (_, y1), (z0, z1) = TUB
    return ((x0 + x1) / 2, y1, (z0 + z1) / 2)


def firedoor_point():
    """The middle of the fire door's opening in the front face: the fuel goes in there."""
    (x0, x1), (y0, y1) = DOOR_HOLE
    return ((x0 + x1) / 2, (y0 + y1) / 2, 0.0)


def ash_point():
    """The middle of the ash mouth in the front face: the damper, worked from in front, and the ash raked out."""
    (mx0, mx1), (my0, my1) = MOUTH
    return ((mx0 + mx1) / 2, (my0 + my1) / 2, 0.0)


POINTS = {"vessel": vessel_point, "tub": tub_point, "firedoor": firedoor_point, "ash": ash_point}


def outlet_cell():
    return tuple(int(v // B) for v in ((STACK[0][0] + STACK[0][1]) / 2, TOP - 1.0, (STACK[2][0] + STACK[2][1]) / 2))


def vessel_cell():
    return tuple(int(v // B) for v in (VESSEL_C[0], VESSEL_Y[3] + 0.5, VESSEL_C[1]))


def tub_cell():
    x, y, z = tub_point()
    return tuple(int(v // B) for v in (x, y - 0.5, z))


MIN_CHIMNEY = 6                              # chimney blocks stood on the outlet, counted from it up: the crucible
#                                              furnace's MinChimneyHeight


def box_volume(el):
    return abs(el.size[0] * el.size[1] * el.size[2])


def build_volumes(els):
    """The brickwork's and the lining's volumes, in blocks, measured from their brick boxes (each lattice box in
    exactly one)."""
    bk = sum(box_volume(e) for e in els if e.name.startswith("bk_brick_"))
    ln = sum(box_volume(e) for e in els if e.name.startswith("ln_brick_"))
    return bk / B ** 3, ln / B ** 3


def build_counts(els):
    """What the brickwork and the lining stages take, from their volumes: fired bricks at BRICKS_PER_BLOCK, refractory
    bricks at LINING_PER_BLOCK, fire clay one per LINING_PER_FIRECLAY of those, each rounded up."""
    bk, ln = build_volumes(els)
    bricks = math.ceil(bk * BRICKS_PER_BLOCK - 1e-9)
    lining = math.ceil(ln * LINING_PER_BLOCK - 1e-9)
    return {"brickVolume": bk, "liningVolume": ln, "bricks": bricks, "lining": lining,
            "fireclay": math.ceil(lining / LINING_PER_FIRECLAY - 1e-9)}


_COUNTS = {}


def counts():
    """build_counts of the model as built (before the z-fighting fix, which steps faces in)."""
    if not _COUNTS:
        _COUNTS.update(build_counts(build_frame()))
    return dict(_COUNTS)


def materials(pairs):
    return [{"code": code, "quantity": q} for code, q in pairs]


def build_json():
    c = counts()
    return {
        "frame": {"recipe": "grid", "tool": "hammer", "ingredients": materials(FRAME_RECIPE)},
        "stages": [
            {"requires": "brickwork", "volumeBlocks": r6(c["brickVolume"]), "oneType": True, "perClick": "stack",
             "materials": materials(((FIRED_BRICK, c["bricks"]),)), "alsoParts": ["hoodiron"]},
            {"requires": "lining", "volumeBlocks": r6(c["liningVolume"]), "perClick": "stack",
             "materials": materials(((LINING_BRICK, c["lining"]), (FIRECLAY, c["fireclay"])))},
            {"requires": "sandbath", "parts": list(STAGE_PARTS["sandbath"]), "also": [f"damper{d}" for d in DAMPERS],
             "materials": materials(SANDBATH_STAGE)},
            {"requires": "vesseltub", "parts": list(STAGE_PARTS["vesseltub"]), "materials": materials(VESSELTUB_STAGE)},
            {"requires": "services", "parts": list(STAGE_PARTS["services"]), "materials": materials(SERVICES_STAGE)},
        ],
        "outletCap": materials((CAP_ITEM,))[0],
        "_comment": "The build's stages, in order, as the owner settled them (2026-10-15). frame: the iron frame's "
                    "grid recipe (with a hammer); placing it puts down the controller and its ghosts, and draws the "
                    "frame part. Each stage then fits its requires when its materials are all in, in order: "
                    "brickwork (its alsoParts, the hood's lintel, comes with it at no cost of its own); lining; "
                    "sandbath (its parts: the sand bath with its sand, and the fire door; also: the damper, in one of "
                    "its places); vesseltub (the parting vessel, a fired clay crucible, and the precipitating tub); "
                    "services (the lead siphon, hung on the vessel's lip over the tub, so after both). The vessel "
                    "runs once services is in. volumeBlocks: the stage's brick measured from the model, in blocks; "
                    "its quantities are that at 2 fired bricks or 4 refractory bricks a block (the game's brick block "
                    "recipes), rounded up, fire clay one per 2.5 refractory bricks. perClick stack: a click puts in "
                    "as many of the held stack as are still needed. oneType: the first fired brick put in sets the "
                    "brick type (bricks), and the rest must match. The tub's copper plates are contents (copper), put "
                    "in by hand and worn away. No water line: the wash water is poured in from a bucket. outletCap: "
                    "an item interaction, not a stage."}


def bricks_json():
    return {"code": "brick", "item": "game:burnedbrick-{type}", "default": BRICK_DEFAULT,
            "types": {t: {"block": block, "base": base, **({"overlays": [over]} if over else {})}
                      for t, block, base, over in BRICKS},
            "_comment": "The fired brick types the brickwork takes (every game:burnedbrick type), each with the block "
                        "its bricks make and that block's texture: the renderer sets the shape's 'brick' code to the "
                        "vessel's type's base, with its overlays drawn over it (the crucible furnace's table). The "
                        "type is the first brick put in; until then no brick is drawn."}


def make_rig(parts):
    sx, sz = (STACK[0][0] + STACK[0][1]) / 2, (STACK[2][0] + STACK[2][1]) / 2
    rig = {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the front cell at "
                    "ground level, where the fire door and the ash mouth are. One fired clay vessel in a sand bath over "
                    "a small fire, under a brick hood open in front; vessels stand side by side in a row along x (up to "
                    "parting.rowMax), sharing one chimney on one member's outlet, the others capped. No power and no "
                    "forced air: natural draught, in at the ash mouth (ash, ashSide) under the damper, the fuel in at "
                    "the fire door (firedoor, firedoorSide). depth is the fire door: 0 open, slid up in its guides "
                    "(the rest pose), 1 shut. theta is the stoker's clock, carried by a ratio-0 rotate on the door: "
                    "nothing turns. requires: the build's stages, in order, brickwork (with the hoodiron part), "
                    "lining, sandbath (the bath and door parts), vesseltub (the vessel and tub parts) and services "
                    "(the siphon part) (build; the frame, iron, has none); the "
                    "damper's place, damperopen, damperhalf or dampershut (one at a time); the fire, "
                    "fuel (laid) or fire (burning); the vessel's contents: charge (granulated dore), acid, solution "
                    "(the silver dissolved), gold (the parted gold), wash (the wash water, poured in from a bucket); the "
                    "tub's: copper (the copper "
                    "plates on their rod), liquor (the drawn-off solution), silver (the silver sludge); and outletcap. The "
                    "brickwork's texture is the brick type's (bricks). vesselCell, vesselFace: the hood's cell, the "
                    "vessel worked through its open front (vessel: its mouth, where the gold comes out); tubCell, tubFace: "
                    "the same cell and face, the precipitating tub at its front (tub: its mouth, the silver's hand-draw "
                    "point): a click there is the nearer of vessel and tub's. No water anchor: the vessel is watered "
                    "from a bucket, at the vessel. outletCell: the stack's head, in the back row, the chimney stood "
                    "on its top; "
                    "chimney: the shortest chimney, counted from the outlet up. See AcidParting/README.md.",
        "cells": [],
        "vesselCell": list(vessel_cell()),
        "vesselFace": "north",
        "vessel": {"pos": pt(*vessel_point())},
        "tubCell": list(tub_cell()),
        "tubFace": "north",
        "tub": {"pos": pt(*tub_point())},
        "firedoor": {"pos": pt(*firedoor_point())},
        "firedoorSide": "north",
        "ash": {"pos": pt(*ash_point())},
        "ashSide": "north",
        "outletCell": list(outlet_cell()),
        "outletFace": "up",
        "chimney": {"origin": pt(sx, TOP + MIN_CHIMNEY * B / 2, sz), "axis": "y", "length": float(MIN_CHIMNEY)},
        "build": build_json(),
        "bricks": bricks_json(),
        "parting": {"minChimney": MIN_CHIMNEY, "rowMax": ROW_MAX, "rowAxis": "x",
                    "_comment": "minChimney: chimney blocks stood on the outlet, counted from it up (the crucible "
                                "furnace's MinChimneyHeight); rowMax, rowAxis: vessels in a row, side by side along x, "
                                "sharing one chimney."},
        "parts": parts,
    }
    return rig


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (it already is: the move is nothing, kept so the
    controller cell costs one constant to change, and check_shipped proves it)."""
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    db = [v / B for v in d]
    ship_els = copy.deepcopy(els)
    for el in ship_els:
        el.c = [el.c[k] + d[k] for k in range(3)]
    ship_parts = copy.deepcopy(parts)
    for p in ship_parts:
        for drv in p["drivers"]:
            if "pivot" in drv:
                drv["pivot"] = [r6(drv["pivot"][k] + db[k]) for k in range(3)]
    ship = copy.deepcopy(rig)
    for key in ("vesselCell", "tubCell", "outletCell"):
        ship[key] = shift_cell(rig[key], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["chimney"] = {**rig["chimney"], "origin": shift_point(rig["chimney"]["origin"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


CELL_PARTS = ("frame", "brickwork", "hoodiron", "lining", "bath", "vessel", "tub", "door")   # the cells' boxes: built,
#                                                                                        door shut
CELL_POSE = (0.0, SHUT, DEFAULT_SCENE)


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed by the shipped rig with the door shut, the vessel
    built; a lid (a deck) on both top cells, at the top, so the roof is walked on whole."""
    by_cell = {}
    for w in flatten(shape["elements"], textures={}):
        pid = part_of(ship_parts, w.name)
        if pid not in CELL_PARTS:
            continue
        el = posed(w, _part_matrix(ship_parts, pid, inputs_of(CELL_POSE)))
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, []).append(el)
    out = []
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        cell = {"pos": list(pos), "boxes": cell_boxes(by_cell[pos], pos)}
        if c[1] == CELLS_Y - 1:
            cell["lid"] = 1.0
        out.append(cell)
    return out


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    poses = [REST, (0.0, 0.3, DEFAULT_SCENE), (1.3, 0.7, DEFAULT_SCENE), (-2.0, 1.0, DEFAULT_SCENE)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; {len(cells)} cells")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
REF_DEPTHS = (0.0, 0.05, 0.1, 0.2, 0.25, 0.4, 0.5, 0.6, 0.75, 0.8, 0.9, 0.95, 1.0)


def reference_poses():
    """depth over the door's travel, at theta 0, 1.1, -2.3 and 2.9 in turn."""
    return [((0.0, 1.1, -2.3, 2.9)[n % 4], dp) for n, dp in enumerate(REF_DEPTHS)]


def reference_json(ship_parts):
    poses = []
    for th, dp in reference_poses():
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], {"theta": th, "depth": dp})) for q in ship_parts}
        poses.append({"theta": th, "depth": dp, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped acidparting-rig.json's parts: each part's matrix as 3 "
                        "rows of 4 (block units) at each pose (depth, the fire door: 0 open, 1 shut; theta moves "
                        "nothing). The site's tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    shape = machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Texture codes the renderer "
             "sets: 'brick' (the brickwork: the brick type's block texture, from the rig's bricks; the shape's own is "
             "the red brick's colour layer). The burning fuel ('ember') glows. Keep element names when editing: the rig "
             "finds its parts by them.",
        TEXTURES, tex_size=TEX)
    for e in shape["elements"]:
        if e["name"].split("_")[0] == "fire":
            for f in e["faces"].values():
                f["glow"] = FIRE_GLOW
    return shape


def coplanar_scenes():
    scenes = [(vs, ts, "burning", "open", False, COMPLETE)
              for vs, ts in (("charged", "copper"), ("acid", "drawn"), ("parting", "precipitating"), ("parted", "sludge"))]
    scenes += [("washing", "precipitating", "laid", "half", True, COMPLETE),
               ("empty", "empty", "none", "shut", True, COMPLETE)]
    scenes += [("empty", "empty", "none", "open", False, "frame"), ("empty", "empty", "none", "open", False, "brickwork"),
               ("empty", "empty", "none", "open", True, "lining"), ("empty", "empty", "none", "shut", True, "sandbath"),
               ("empty", "empty", "none", "half", False, "vesseltub")]
    return scenes


def coplanar_poses():
    return tuple((0.0, dp, sc) for sc in coplanar_scenes() for dp in (0.0, 0.5, 1.0))


def fix_coplanar(els, parts):
    def posed_fn(es, pose):
        return shown(parts, [posed(el, pm(parts, el.part, pose)) for el in es], pose)
    return fix_coplanar_posed(els, posed_fn, coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the acid parting vessel's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_acidparting
    els = build()
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        print(f"coplanar faces before the fix: {sum(len(p) for p in before.values())} pairs over {len(before)} poses")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_acidparting.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "acidparting.json", args.out / "acidparting_frame.json", args.out / "acidparting-rig.json",
                args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "acidparting.json", SHAPE_DIR / "acidparting_frame.json", RIG_DIR / "acidparting-rig.json",
                REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_acidparting.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
