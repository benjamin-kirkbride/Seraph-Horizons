#!/usr/bin/env python3
"""Generate the liquation furnace's shapes, rig and reference poses.

The liquation furnace parts teallite's and franckeite's lead from their tin by heat (#929, model #930): tin melts at
232 degrees and lead at 327, so cakes of the tin-lead laid on a cast-iron hearth plate over a gentle fire sweat their
tin out, and it runs down the plate, which falls to the front, off its low end through a short spout into an iron
receiving pot, while the lead stays on the plate until it is melted off or raked out. It is one small furnace of
brick, two blocks wide, two deep and two high, on the crucible furnace's conventions (CrucibleFurnace/): its
brickwork is any one type of fired brick (its texture the renderer's, from the rig's bricks), its hot faces are
lined with tier-3 refractory brick 3 voxels thick, it is bound with angle iron, one frame, fired on natural
draught only, through an ash mouth with a three-place damper, and its smoke leaves by an outlet on the top, where
the player stands the chimney, or which a flat iron plate caps in a row of furnaces sharing one chimney.

    the front   the working opening: the plate's low end under a low brick vault, open at the front, so the cakes
                go on and the lead comes off from in front; under the plate's low end a niche the width of the
                chamber holds the receiving pot, the spout over it.
    the middle  the plate, falling to the front, over the firebox (a grate of iron firebars on three bearers, the
                ash pit under it), the cakes on its upper part, over the fire.
    the back    behind the plate's high end the throat, open to the fire behind the plate and to the chamber under
                a hanging wall, and the stack rising from it to the outlet, in the back row's east cell (a chimney
                stands on one cell), the stack a square flue centred under the chimney; in the back face the fire door, an iron plate sliding up in iron guides,
                over the ash mouth and its damper, which slides sideways.

Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    liquationfurnace.json          the whole furnace, every part that moves or comes and goes   (assets/.../shapes/block/)
    liquationfurnace_frame.json    the iron frame only (block and item)                         (assets/.../shapes/block/)
    liquationfurnace-rig.json      cells, anchors and the part rig                              (assets/.../config/)
    rig-reference.json             every part's matrix at a grid of poses                       (tests/LiquationFurnace/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_liquationfurnace.py) and exits non-zero
if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the machine
box's north-west-bottom corner. The front (the working opening, the pot) is north, z 0; the back (the fire door, the
ash mouth, the stack) is south. The controller cell is the front west cell at ground level, build cell (0, 0, 0), so
the shipped files are the build frame as it is.

The rig's inputs, as this furnace uses them:

    depth  the fire door: 0 open, slid up in its guides (the rest pose), 1 shut over the fire hole. A step slide.
    theta  the stoker's clock: a ratio-0 rotate on the door carries it and moves nothing (so the viewer offers Play,
           which opens and shuts the door). Nothing in the furnace turns.

Everything else that shows or not (the build's stages: the brickwork, the lining and the fittings, which are the
hearth plate, the receiving pot and the fire door; the cakes, the tin running, the tin in the pot, the lead left on
the plate; the fuel in two layers and the same fuel burning; the damper's three places; the outlet's cap) is a
`requires`: gameplay's state, not a motion. The iron frame has none.
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
REFERENCE_OUT = MOD / "tests" / "LiquationFurnace" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/LiquationFurnace/tools/make_shape.py"

B = 16.0
OCT = math.tan(math.pi / 8)                  # an octagon's half side over its apothem

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 2, 2, 2          # two wide (x, the row's way), two high, two deep (z)
ORIGIN_CELL = (0, 0, 0)                      # the controller: the front west cell at ground level (the niche's)
OUTLET_CELL = (1, 1, 1)                      # the back east top cell: the stack's top, the chimney stood on it
CHIMNEY_CELL = (1, 2, 1)                     # the chimney's first block: not the furnace's, nor is its neighbour
FREE_CELLS = ((0, 2, 1),)
ROW_MAX = 5                                  # furnaces in a row, side by side along x, sharing one chimney

TEXTURES = {
    "brick": "game:block/clay/brick/four/running/red1",     # the common brickwork: the red brick's colour (BRICKS)
    "lining": "game:block/clay/refractory/tier3/front1",    # the hot-face lining and the vault: tier-3 refractory
    "iron": "game:block/metal/plate/iron",                  # the frame, the hearth plate, the pot, the door, the damper
    "cake": "game:block/metal/ingot/leadsolder",            # the tin-lead cakes (the game's solder is tin and lead)
    "tin": "game:block/metal/ingot/tin",                    # the tin sweated out: running, and in the pot
    "lead": "game:block/metal/ingot/lead",                  # the lead left on the plate
    "fuel": "game:block/coal/charcoal",                     # the fuel on the grate
    "ember": "game:block/coal/ember",                       # the same fuel, burning
}
TEX = 16                                     # shape texture units: one a voxel, one texture across a block face
FIRE_GLOW = 200                              # the burning fuel's faces (the crucible furnace's coke; the firepit's 160)
#                                              The tin and the lead do not glow: 232 to 327 degrees is far under the
#                                              game's glow, which starts at 500 (the viewer's hotGlow, vanilla's rule).

W, H, D = CELLS_X * B, CELLS_Y * B, CELLS_Z * B
FLOOR_TOP = H                                # the furnace's top: flat, a deck
BASE_TOP = 1.0                               # the base course: the niche's and the ash pit's floor
LINING_T = 3.0
MIN_WALL = 5.0                               # the least wall (brick, lining, iron set in it) round the fire, the plate
#                                              and the stack
OUTER_BRICK = 2.0                            # the least fired brick (or iron) in from any outer face, a recess's or a
#                                              seat's floor with it, before the lining: the lining never shows outside
FRONT_RING = OUTER_BRICK                     # the front face's fired brick round the working opening: the arch ring
#                                              over it and the side walls' reveals, the lining behind them

# ---------------------------------------------------------------- the inside: one width, the chamber over the plate,
# the firebox under it, the niche under its low end
IN_X = (8.0, 24.0)                           # 16 wide, centred: side walls of 8 (5 brick, 3 lining)
XM = (IN_X[0] + IN_X[1]) / 2

# The hearth plate: cast iron, falling to the front at one in five, from its high end over the back of the firebox to
# its low end over the niche. Its top's height at z is plate_top(z); its underside is PLATE_TV under it (straight down).
PLATE_Z = (5.0, 24.0)                        # its top's ends (z): the low end (front), the high end (back)
PLATE_LOW_TOP = 13.0                         # its top at the low end
PLATE_SLOPE = 0.2                            # rise per voxel back
PLATE_TV = 1.0                               # its thickness, measured straight down
PLATE_ANGLE = math.atan(PLATE_SLOPE)         # 11.3 degrees
RIM = (1.0, 1.0)                             # the plate's raised rims: width, height over its top
SPOUT_GAP = (14.5, 17.5)                     # the gap in the low end's rim (x), where the spout leaves
SPOUT_Z = 3.0                                # the spout's lip (z): it runs forward from the plate's low end to here
SPOUT_ANGLE = math.radians(25.0)             # steeper than the plate
SPOUT_T = 0.5                                # its trough's bottom
SPOUT_SIDE = (0.5, 0.8)                      # its sides: thickness, height


def plate_top(z):
    return PLATE_LOW_TOP + PLATE_SLOPE * (z - PLATE_Z[0])


def plate_under(z):
    return plate_top(z) - PLATE_TV


# the niche: the receiving pot's, under the plate's low end, open to the front and, in front of the plate, above
NICHE = (IN_X, (BASE_TOP, 12.0), (0.0, 9.0))
FRONT_WALL_Z = (9.0, 14.0)                   # between the niche and the firebox: 2 of brick, 3 of lining; its top
FRONT_WALL_TOP = plate_under(FRONT_WALL_Z[0])  # meets the plate's underside at its front face

# the firebox, the ash pit and the grate
FIRE_Z = (FRONT_WALL_Z[1], 26.0)             # 12 deep, from the front wall to the back wall (6 thick)
ASH_PIT_Y = (BASE_TOP, 5.0)
BEARER_Y = (5.0, 6.5)                        # the grate's three bearers, along z, their ends built into the front and
BEARER_X = ((8.0, 9.5), (15.25, 16.75), (22.5, 24.0))   # back walls; one under each end of the bars, one in the middle
BEARER_Z = (FIRE_Z[0] - 1.0, FIRE_Z[1] + 1.0)
BAR_Y = (6.5, 8.0)                           # the firebars, along x, two lengths a row
BARS, BAR_W = 6, 1.2                         # six rows 1.2 wide across the 12: gaps of 0.96
GRATE_TOP = BAR_Y[1]
FIREBOX_TOP = 16.0                           # the firebox's void reaches the plate's underside (15.8 at its high end)

# the chamber over the plate, under a segmental barrel vault along z, open at the front
SPRING = 21.0                                # the vault springs off the side walls here
RISE = 4.0                                   # its rise: span 16, so a radius of 10
VAULT_Z = (0.0, 17.0)                        # from the front face to the hanging wall
STAVES = 8
VAULT_R = ((IN_X[1] - IN_X[0]) ** 2 / 4 + RISE * RISE) / (2 * RISE)
VAULT_CY = SPRING + RISE - VAULT_R           # the arc's centre's height
VAULT_TH = math.asin((IN_X[1] - IN_X[0]) / 2 / VAULT_R)   # half the arc's angle
VAULT_ZONE = ((IN_X[0] - LINING_T, IN_X[1] + LINING_T), (SPRING, 28.25), VAULT_Z)  # the staves and their backing:
#                                              fired brick for FRONT_RING from the front face (the arch ring), lining behind
CHAMBER_FRONT = (IN_X, (12.0, SPRING), (0.0, FRONT_WALL_Z[0]))
CHAMBER_BACK = (IN_X, (FRONT_WALL_TOP, SPRING), (FRONT_WALL_Z[0], VAULT_Z[1]))

# behind the plate's high end: the hanging wall (the chamber's back wall, its foot over the plate, the chamber's air
# going under it), the throat behind it (open down to the fire behind the plate's high end), and the stack
HANG_Z = (VAULT_Z[1], 20.0)                  # 3 thick, the lining's
HANG_FOOT = 20.0
THROAT_TOP = 23.0
# The stack: a square flue 4 by 4 (STACK_SIDE) centred under the chimney's line, the middle of the outlet's cell, from
# the hanging wall's foot (where it opens off the throat) to the top; its mouth is the outlet's opening, square and
# centred under the chimney the player stands on it. The outlet cell is the back east corner's, so the flue's side is
# what the wall to the east and back faces leaves: 6 from the flue to each face (MIN_WALL and more), LINING_T of
# lining and 3 of fired brick, the back wall's (an 8 by 8 flue, the crucible furnace's, left 3 of lining and 1 of
# brick, and the lining showed in the fire door's recess).
STACK_SIDE = 4.0
STACK_C = ((OUTLET_CELL[0] + 0.5) * B, (OUTLET_CELL[2] + 0.5) * B)   # (x, z): the outlet cell's middle, the chimney's
STACK = ((STACK_C[0] - STACK_SIDE / 2, STACK_C[0] + STACK_SIDE / 2), (HANG_FOOT, FLOOR_TOP),
         (STACK_C[1] - STACK_SIDE / 2, STACK_C[1] + STACK_SIDE / 2))
CURB_W = 1.5                                 # the outlet's curb round the stack's mouth: square and centred with it, the
#                                              top course's fired brick
CURB = ((STACK[0][0] - CURB_W, STACK[0][1] + CURB_W), (30.0, FLOOR_TOP), (STACK[2][0] - CURB_W, STACK[2][1] + CURB_W))
CURB_RIM = 1.0                               # the outlet's curb, of the brickwork's fired brick, flush: a rim 1 wide round
CAP_Y = (31.5, FLOOR_TOP)                    # the cap's seat, the seat a ledge 0.5 wide round the stack's mouth; the cap,
CAP_SEAT = ((CURB[0][0] + CURB_RIM, CURB[0][1] - CURB_RIM), CAP_Y,   # a flat iron plate, set flush in it
            (CURB[2][0] + CURB_RIM, CURB[2][1] - CURB_RIM))
STACK_LINING_TOP = CAP_Y[0] - OUTER_BRICK    # the stack's lining ends OUTER_BRICK under the cap's seat, the curb over it

# the back face: the fire door over the ash mouth, both centred, each in its recess with its iron
FIRE_HOLE = ((12.0, 20.0), (GRATE_TOP, 13.0))           # through the back wall, its sill the grate's top
ASH_MOUTH = ((13.0, 19.0), (BASE_TOP, 4.5))            # through the back wall into the ash pit: the air's way in,
#                                                         its top under the lining's (from the grate's bearers up)
JAMBS = ((11.0, 12.0), (20.0, 21.0))         # the fire hole's iron jambs (x), from the ground to its lintel
FRAME_Z = (30.0, 31.0)                       # the jambs and the lintels, behind the recesses
DOOR_LINTEL_Y = (13.0, 14.0)
ASH_LINTEL_Y = (ASH_MOUTH[1][1], ASH_MOUTH[1][1] + 1.0)
DOOR_RECESS = ((11.0, 21.0), (7.0, 20.0), (31.0, D))     # the fire door's slide
DOOR_Z = (31.25, 31.75)                      # the door plate, in its recess
DOOR_SHUT_Y = (7.5, 13.5)                    # shut, it covers the fire hole and overlaps it
DOOR_TRAVEL = 6.0                            # open, it is slid up this far (the rest pose)
HANDLE = ((14.5, 17.5), (0.8, 1.6), (DOOR_Z[1], 31.98))  # its handle: x, y over its underside, z
DAMPER_RECESS = ((12.5, 27.0), (0.5, 5.75), (31.0, D))   # the damper's slide: it moves east to open
RAILS = (("bottom", (0.5, 0.75)), ("top", (5.5, 5.75)))  # its iron rails (y), the recess's length
DAMPER_Z = (31.35, 31.85)
DAMPER_W = 6.6
DAMPER_Y = (0.75, 5.5)
DAMPER = {"shut": 12.7, "half": 16.0, "open": 19.3}      # its west edge (x) in each place: shut over the whole mouth,
#                                                          half over its east half, open clear of it
KNOB = ((2.6, 4.0), (DAMPER_Z[1], 31.98), (2.4, 3.6))    # the damper's knob: x from its west edge, z, y

# the plate's two cross bearers (along x, their ends built into the side walls, on posts in the brick): one under its
# low end over the niche, one under its high end; between them it lies on the front wall's top
PLATE_BEARERS = (("front", (5.25, 6.75)), ("back", (22.0, 23.5)))
PLATE_BEARER_X = (3.0, 29.0)
PLATE_BEARER_H = 1.2

# the receiving pot: a cast-iron kettle, octagonal, on the niche's floor, the spout's lip over its mouth: a foot, a
# narrower lower course and the body, as wide as the niche's depth allows (its corners 0.2 inside it, front and back)
POT_C = (XM, 4.5)                            # (x, z)
POT_FOOT = (BASE_TOP, 1.6, 3.0)              # y0, y1, apothem
POT_TUBES = (("lower", 1.6, 3.2, 3.5), ("body", 3.2, 7.6, 3.95))   # (name, y0, y1, outer apothem)
POT_TOP = POT_TUBES[-1][2]
POT_A = POT_TUBES[-1][3]
POT_WALL = 0.6
TIN_LEVEL = (5.7, 6.5)                       # the tin in it (the surface 1.1 under the rim)
TIN_A = POT_A - POT_WALL + 0.01

# the charge: four cakes of tin-lead on the plate's upper part, over the fire; the lead left where they lay
CAKES_X = ((9.6, 15.4), (16.6, 22.4))
CAKES_Z = (14.6, 19.2)                       # their middles on the plate (z)
CAKE = (4.0, 1.8)                            # along the slope, thick

# the fuel on the grate, two layers, and the same burning
FUEL_LAYERS = ((GRATE_TOP, 9.5), (9.5, 11.0))
LUMP = 1.4

# The iron binding, angle iron (an L section) throughout, set flush into the brick so nothing stands proud of the
# furnace's box: an angle up each of the four vertical corners, wrapping it, one leg on the front or back face and one
# on the side face, its inside corner bedded against the brick's corner; and along each of the four top edges an angle
# band wrapping that edge (one leg down the face, one on the top), running from corner angle to corner angle and
# joining them, the four bands one ring round the top: one frame. The side faces are plain brick but for the corner
# angles' and the side bands' legs, flush, so furnaces still stand side by side in a row, their walls meeting.
ANGLE_T = 1.0                                # the legs' thickness
ANGLE_LEG = 2.5                              # each leg's width, outside

# ---------------------------------------------------------------- the build
# Built in stages (the rig's `build`; their requires values are the viewer's Build states): the iron frame, a grid
# recipe placed as the machine, then the brickwork, the lining, and the fittings (the hearth plate with its spout, the
# receiving pot and the fire door; the damper's places with them). The brickwork's and the lining's counts are
# measured from the model's volume at the game's rates, as the crucible furnace's are; the frame's recipe and the
# fittings' materials are proposals for the owner (README, "Open for the owner").
FRAME_RECIPE = (("game:metalplate-iron", 1), ("game:rod-iron", 4), ("game:metalnailsandstrips-iron", 8))
BRICKS_PER_BLOCK = 2
LINING_PER_BLOCK = 4
LINING_PER_FIRECLAY = 2.5
FIRED_BRICK = "game:burnedbrick-*"
LINING_BRICK = "game:refractorybrick-fired-tier3"
FIRECLAY = "game:clay-fire"
FITTINGS_STAGE = (("game:metalplate-iron", 4),)
CAP_ITEM = ("game:metalplate-iron", 1)
STAGES = ("brickwork", "lining", "fittings")  # the stages after the frame, in order: their requires values
STAGE_STATES = (                             # the viewer's Build select: (id, the stages fitted)
    ("frame", ()),
    ("brickwork", ("brickwork",)),
    ("lining", ("brickwork", "lining")),
    ("complete", ("brickwork", "lining", "fittings")),
)

# The fired bricks the brickwork takes: the crucible furnace's list (CrucibleFurnace/tools/make_shape.py, BRICKS), every
# type of the game's burnedbrick, with the block its bricks make and that block's texture. (type, block, base, overlay)
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

# The scenario's states, as the viewer gives them (site/models.json; tools/tests holds the two together). Each is the
# requires values fitted.
CHARGE_STATES = (
    ("empty", "Empty", ()),
    ("charged", "Charged: four cakes on the plate", ("cakes",)),
    ("sweating", "Sweating: the tin running to the pot", ("cakes", "tinrun", "tin")),
    ("sweated", "Sweated: the tin in the pot, the lead left on the plate", ("tin", "lead")),
    ("tapped", "The tin taken, the lead still on the plate", ("lead",)),
)
FIRE_STATES = (
    ("cold", "No fire", ()),
    ("laid", "Fuel laid on the grate", ("fuel1", "fuel2")),
    ("lit", "Lit", ("fire1", "fire2")),
    ("low", "Burnt low", ("fire1",)),
)
CHARGE_IDS = tuple(s[0] for s in CHARGE_STATES)
FIRE_IDS = tuple(s[0] for s in FIRE_STATES)
DAMPERS = ("open", "half", "shut")
CHARGE_PARTS = ("cakes", "tinrun", "tin", "lead")
FIRE_PARTS = ("fuel1", "fuel2", "fire1", "fire2")
CONTENTS = CHARGE_PARTS + FIRE_PARTS


# ---------------------------------------------------------------- box helpers
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
    side continue one texture across a block face with no seam (the crucible furnace's: CubeMeshUtil's directions)."""
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


def unit(v):
    n = math.sqrt(sum(x * x for x in v))
    return [x / n for x in v]


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def obox(c, ax, size, name, part, tex):
    """A box of `size` along the orthonormal world vectors `ax` (local x, y; z is x cross y), centred on c."""
    x, y = unit(ax[0]), unit(ax[1])
    z = cross(x, y)
    r = [[x[i], y[i], z[i]] for i in range(3)]
    return skin(El(name, list(size), list(c), r, {}, part), tex)


def slope_axes(angle):
    """A slope rising towards +z at `angle`: across (x), its normal (up and a little forward), along (up the slope)."""
    return [1.0, 0.0, 0.0], [0.0, math.cos(angle), -math.sin(angle)], [0.0, math.sin(angle), math.cos(angle)]


def on_plate(z, lift, along, thick, x0, x1, name, part, tex):
    """A slab lying on the plate's top: its middle over the plate's top line at z, `lift` over it (along its normal),
    `along` long up the slope, `thick` along the normal, x0..x1 across."""
    ax, n, _ = slope_axes(PLATE_ANGLE)
    base = [(x0 + x1) / 2, plate_top(z), z]
    c = [base[k] + n[k] * (lift + thick / 2) for k in range(3)]
    return obox(c, (ax, n), (x1 - x0, thick, along), name, part, tex)


def on_spout(s, lift, along, thick, x0, x1, name, part, tex):
    """A slab lying on the spout's trough, its middle `s` voxels forward of the plate's low end along it."""
    ax, n, g = slope_axes(SPOUT_ANGLE)
    base = [(x0 + x1) / 2, PLATE_LOW_TOP - s * g[1], PLATE_Z[0] - s * g[2]]
    c = [base[k] + n[k] * (lift + thick / 2) for k in range(3)]
    return obox(c, (ax, n), (x1 - x0, thick, along), name, part, tex)


SPOUT_LEN = (PLATE_Z[0] - SPOUT_Z) / math.cos(SPOUT_ANGLE)


def spout_lip():
    """The spout's lip: the middle of its trough's top at its forward end (x, y, z)."""
    return (XM, PLATE_LOW_TOP - SPOUT_LEN * math.sin(SPOUT_ANGLE), SPOUT_Z)


def oct_disc(cx, cz, y0, y1, a, name, part, tex):
    """A solid octagonal disc about a vertical axis, apothem `a`: four strips at 0, 45, 90 and 135 degrees."""
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
    the axis (cx, cz): one side of an octagonal ring."""
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
    turn = -float(n) if n <= 180 else 360.0 - n
    rotate([el], "y", turn, (cx, 0.0, cz))
    return el


def oct_tube(cx, cz, y0, y1, a, t, name, part, tex):
    """An octagonal tube about a vertical axis, outer apothem `a`, wall `t`: eight slabs."""
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


def split_cells(name, lo, hi):
    """A box cut at the blocks' boundaries along every axis, as (name, lo, hi): each piece in one block."""
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


# ---------------------------------------------------------------- the vault
def intrados(x):
    """The vault's underside's height at x (inside the span)."""
    return VAULT_CY + math.sqrt(max(0.0, VAULT_R ** 2 - (x - XM) ** 2))


def extrados(x):
    """The vault's back's height at x: the arc LINING_T out from the underside."""
    return VAULT_CY + math.sqrt(max(0.0, (VAULT_R + LINING_T) ** 2 - (x - XM) ** 2))


def segment_area():
    """The chamber's cross-section under the vault, over the springing: a circular segment of radius VAULT_R."""
    r, h = VAULT_R, RISE
    return r * r * math.acos((r - h) / r) - (r - h) * math.sqrt(2 * r * h - h * h)


STRIP_X = (5.0, 6.5, 8.0, 9.0, 10.0, 11.0, 12.0, 13.0, 14.0, 15.0, 16.0, 17.0, 18.0, 19.0, 20.0, 21.0, 22.0, 23.0,
           24.0, 25.5, 27.0)


def build_vault():
    """The vault: STAVES staves, each LINING_T thick, on the arc through the springing points and the crown, from the
    front face to the hanging wall (cut at the block's boundary), and their backing: upright strips from the staves'
    back (or the springing, beside the span) up to the vault zone's top, where the brickwork above begins. For
    FRONT_RING from the front face they are the brickwork's fired brick (`bk_ring<n>`, `bk_ringbacking<nn>`: the arch
    ring the front face shows), behind it the lining's refractory (`ln_vault<n>_<j>`, `ln_backing<nn>_<j>`), whose
    front faces, against the ring, are left out. Each strip dips into the staves at its higher edge, so no gap shows
    between them; each stave is a hair shorter than its neighbour at its ends, so their end faces never share a
    plane."""
    p, t = "lining", "lining"
    ring = FRONT_RING
    out = []
    d = 2 * VAULT_TH / STAVES
    width = 2 * (VAULT_R + LINING_T) * math.tan(d / 2) + 0.02
    c = (XM, VAULT_CY, 0.0)
    for i in range(STAVES):
        ang = -VAULT_TH + d * (i + 0.5)
        s = 0.006 * (i % 2)
        el = box([XM - width / 2, VAULT_CY + VAULT_R, VAULT_Z[0] + s], [XM + width / 2, VAULT_CY + VAULT_R + LINING_T,
                 ring + s], f"bk_ring{i + 1}", "brickwork", "brick")
        rotate([el], "z", math.degrees(ang), c)
        out.append(el)
        for j, (z0, z1) in enumerate(((ring + s, B), (B, VAULT_Z[1] - s)), 1):
            el = box([XM - width / 2, VAULT_CY + VAULT_R, z0], [XM + width / 2, VAULT_CY + VAULT_R + LINING_T, z1],
                     f"ln_vault{i + 1}_{j}", p, t)
            if j == 1:
                del el.faces["north"]
            rotate([el], "z", math.degrees(ang), c)
            out.append(el)
    (_, _), (_, top), (z0, z1) = VAULT_ZONE
    for idx, (a, b) in enumerate(zip(STRIP_X, STRIP_X[1:]), 1):
        if b <= IN_X[0] + 1e-9 or a >= IN_X[1] - 1e-9:
            bottom = SPRING
        else:
            bottom = min(extrados(a), extrados(b))
        if top - bottom < 0.05:
            continue
        out.append(wbox([a, bottom, z0], [b, top, ring], f"bk_ringbacking{idx:02d}", "brickwork", "brick"))
        for n, (_, lo, hi) in enumerate(split_cells("backing", [a, bottom, ring], [b, top, z1]), 1):
            el = wbox(lo, hi, f"ln_backing{idx:02d}_{n}", p, t)
            if lo[2] <= ring + 1e-9:
                del el.faces["north"]
            out.append(el)
    return out


# ---------------------------------------------------------------- the brickwork
def binding():
    """The iron binding, as (name, lo, hi), all angle iron ANGLE_T thick with legs ANGLE_LEG wide, flush with the
    furnace's faces: up each vertical corner an angle (`fr_angle_<face><side>_face`, its leg on the front or back face,
    and `_side`, its leg on the side face), and along the top front and back edges an angle band (`fr_band_<face>_face`,
    its leg down the face between the corner angles, and `_top`, its leg on the top from one corner angle's side leg to
    the other's), and along the top side edges an angle band (`fr_band_<w|e>_face`, its leg down the side face, flush,
    between the corner angles' side legs, and `_top`, its leg on the top between the front and back bands' top legs),
    so the four bands are one ring round the top, joined to the four corner angles: one frame. The legs of one angle
    meet along its corner and do not overlap."""
    t, leg = ANGLE_T, ANGLE_LEG
    out = []
    for face, (zf0, zf1), (zs0, zs1) in (("n", (0.0, t), (t, leg)), ("s", (D - t, D), (D - leg, D - t))):
        for side, (xf0, xf1), (xs0, xs1) in (("w", (0.0, leg), (0.0, t)), ("e", (W - leg, W), (W - t, W))):
            out.append((f"fr_angle_{face}{side}_face", [xf0, 0.0, zf0], [xf1, H, zf1]))
            out.append((f"fr_angle_{face}{side}_side", [xs0, 0.0, zs0], [xs1, H, zs1]))
        out.append((f"fr_band_{face}_face", [leg, FLOOR_TOP - leg, zf0], [W - leg, FLOOR_TOP, zf1]))
        out.append((f"fr_band_{face}_top", [t, FLOOR_TOP - t, zs0], [W - t, FLOOR_TOP, zs1]))
    for side, (xf0, xf1), (xs0, xs1) in (("w", (0.0, t), (t, leg)), ("e", (W - t, W), (W - leg, W - t))):
        out.append((f"fr_band_{side}_face", [xf0, FLOOR_TOP - leg, leg], [xf1, FLOOR_TOP, D - leg]))
        out.append((f"fr_band_{side}_top", [xs0, FLOOR_TOP - t, leg], [xs1, FLOOR_TOP, D - leg]))
    return [piece for name, lo, hi in out for piece in split_blocks(name, lo, hi)]


def binding_angles():
    """The binding as angles: [(name, (leg, leg), corner)] for each corner angle and each top band, the two legs'
    (lo, hi) and the brick corner the angle wraps: (axis a, side a, axis b, side b) for its two faces."""
    t, leg = ANGLE_T, ANGLE_LEG
    pieces = {}
    for name, lo, hi in binding():
        base = name.rstrip("0123456789")
        pieces.setdefault(base, []).append((lo, hi))

    def whole(base):
        ps = pieces[base]
        return [min(p[0][k] for p in ps) for k in range(3)], [max(p[1][k] for p in ps) for k in range(3)]
    out = []
    for face, zsign in (("n", -1), ("s", 1)):
        for side, xsign in (("w", -1), ("e", 1)):
            n = f"fr_angle_{face}{side}"
            out.append((n, (whole(n + "_face"), whole(n + "_side")), ((2, zsign), (0, xsign))))
        n = f"fr_band_{face}"
        out.append((n, (whole(n + "_face"), whole(n + "_top")), ((2, zsign), (1, 1))))
    for side, xsign in (("w", -1), ("e", 1)):
        n = f"fr_band_{side}"
        out.append((n, (whole(n + "_face"), whole(n + "_top")), ((0, xsign), (1, 1))))
    return out


def plate_bearer(z0, z1):
    """A cross bearer of the plate under z0..z1: its top at the plate's underside at its front edge."""
    top = plate_under(z0)
    return [PLATE_BEARER_X[0], top - PLATE_BEARER_H, z0], [PLATE_BEARER_X[1], top, z1]


def back_iron():
    """The back face's iron, as (name, lo, hi): the fire hole's jambs (from the ground) and its lintel, the ash mouth's
    lintel between the jambs, the fire door's guides in its recess, and the damper's rails in its."""
    z0, z1 = FRAME_Z
    out = []
    for side, (x0, x1) in zip(("w", "e"), JAMBS):
        out.append((f"fr_jamb_{side}", [x0, 0.0, z0], [x1, DOOR_LINTEL_Y[0], z1]))
    out.append(("fr_doorlintel", [JAMBS[0][0], DOOR_LINTEL_Y[0], z0], [JAMBS[1][1], DOOR_LINTEL_Y[1], z1]))
    out.append(("fr_ashlintel", [JAMBS[0][1], ASH_LINTEL_Y[0], z0], [JAMBS[1][0], ASH_LINTEL_Y[1], z1]))
    (rx, ry, rz) = DOOR_RECESS
    for side, (x0, x1) in zip(("w", "e"), JAMBS):
        out.append((f"fr_guide_{side}", [x0, ry[0], rz[0]], [x1, ry[1], rz[1]]))
    (dx, _, dz) = DAMPER_RECESS
    for name, (y0, y1) in RAILS:
        out.append((f"fr_rail_{name}", [dx[0], y0, dz[0]], [dx[1], y1, dz[1]]))
    return out


def brick_voids():
    """Every space cut out of the brick, as (lo, hi) in voxels: the niche, the chamber and the vault's zone (its
    staves and backing are elements of their own), the firebox and the ash pit, the space under the hanging wall,
    the throat and the stack, the cap's seat in the curb, the fire hole and the ash mouth with their recesses, and the
    room for the iron set into the brick (the grate's bearers' ends, the plate's bearers, the back face's iron, the
    binding, the frame's ties)."""
    v = []
    for (x0, x1), (y0, y1), (z0, z1) in (
            NICHE, CHAMBER_FRONT, CHAMBER_BACK, VAULT_ZONE,
            (IN_X, ASH_PIT_Y, FIRE_Z),                                         # the ash pit
            (IN_X, (ASH_PIT_Y[1], FIREBOX_TOP), FIRE_Z),                       # the firebox, the plate over it
            (IN_X, (ASH_PIT_Y[1], HANG_FOOT), HANG_Z),                         # under the hanging wall
            (IN_X, (ASH_PIT_Y[1], THROAT_TOP), (HANG_Z[1], FIRE_Z[1])),        # the throat
            STACK, CAP_SEAT,
            (FIRE_HOLE[0], FIRE_HOLE[1], (FIRE_Z[1], D)),                      # the fire hole
            (ASH_MOUTH[0], ASH_MOUTH[1], (FIRE_Z[1], D)),                      # the ash mouth
            DOOR_RECESS, DAMPER_RECESS):
        v.append(([x0, y0, z0], [x1, y1, z1]))
    for x0, x1 in BEARER_X:
        v.append(([x0, BEARER_Y[0], BEARER_Z[0]], [x1, BEARER_Y[1], BEARER_Z[1]]))
    for _, (z0, z1) in PLATE_BEARERS:
        v.append(plate_bearer(z0, z1))
    v += [(lo, hi) for _, lo, hi in back_iron()]
    v += [(lo, hi) for _, lo, hi in binding()]
    v += [(lo, hi) for _, lo, hi in ties()]
    return v


def ties():
    """The ties, as (name, lo, hi): the least iron that joins each piece of the frame to the ground before the brick
    is in, all of it inside the brickwork (voids are cut for it), so it is hidden once the brick is in: a post under
    the back end of each outer grate bearer and under the front end of the middle one (the ash mouth is under its back
    end), each half a voxel clear of the ash pit, and a post under each end of each of the plate's bearers, in the side
    walls."""
    out = []
    for side, n, (z0, z1) in (("w", 0, (BEARER_Z[1] - 0.5, BEARER_Z[1])), ("m", 1, (BEARER_Z[0], BEARER_Z[0] + 0.5)),
                              ("e", 2, (BEARER_Z[1] - 0.5, BEARER_Z[1]))):
        x0, x1 = BEARER_X[n]
        out.append((f"fr_tie_bearer_{side}", [x0, 0.0, z0], [x1, BEARER_Y[0], z1]))
    for name, (z0, z1) in PLATE_BEARERS:
        lo, _ = plate_bearer(z0, z1)
        zc = (z0 + z1) / 2
        for side, (x0, x1) in (("w", (PLATE_BEARER_X[0], PLATE_BEARER_X[0] + 1.0)),
                               ("e", (PLATE_BEARER_X[1] - 1.0, PLATE_BEARER_X[1]))):
            out.append((f"fr_tie_plate{name}_{side}", [x0, 0.0, zc - 0.5], [x1, lo[1], zc + 0.5]))
    return out


# where the brick is the lining's, as (lo, hi): round the chamber over the plate (its side walls from just under the
# plate's low end to the springing), the hanging wall, round the firebox and the throat (from the grate's bearers up),
# and round the stack (up to OUTER_BRICK under the cap's seat: the top courses round its mouth are the curb's), none
# of it nearer an outer face than OUTER_BRICK (the chamber's side walls' begin FRONT_RING back from the front face).
# The rest is the brickwork's: the base, the niche, the ash pit and the ash mouth, the outer walls, the curb, the brick over the vault.
def lining_zones():
    t = LINING_T
    (x0, x1) = IN_X
    return [([x0 - t, 12.5, FRONT_RING], [x1 + t, SPRING, VAULT_Z[1]]),
            ([x0 - t, FRONT_WALL_TOP, HANG_Z[0]], [x1 + t, VAULT_ZONE[1][1], HANG_Z[1]]),
            ([x0 - t, BEARER_Y[0], FIRE_Z[0] - t], [x1 + t, THROAT_TOP + t, FIRE_Z[1] + t]),
            ([STACK[0][0] - t, STACK[1][0] - t, STACK[2][0] - t], [STACK[0][1] + t, STACK_LINING_TOP, STACK[2][1] + t])]


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
    crossing a block's boundary or the other's, each world-mapped; `bk_brick_NNN` in the `brickwork` part,
    `ln_brick_NNN` in the `lining`. A face lying on its whole area against brick always there with it is left out: a
    brickwork face against brickwork, a lining face against either (the lining goes in after the brickwork)."""
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


# ---------------------------------------------------------------- builders
def build_iron():
    """The iron frame (`frame`): what the frame stage puts down, all of it standing on its own, set into the brick once
    it is in. The grate (three bearers and the firebars, two lengths a row), the plate's two cross bearers, the back
    face's iron (the fire hole's jambs and lintel, the ash mouth's lintel, the fire door's guides, the damper's rails),
    the binding (angle iron: the corner angles and the top bands), and the frame's ties (`ties`, hidden in the
    brickwork)."""
    f, t = "frame", "iron"
    out = []
    for n, (x0, x1) in enumerate(BEARER_X, 1):
        out.append(box([x0, BEARER_Y[0], BEARER_Z[0]], [x1, BEARER_Y[1], BEARER_Z[1]], f"fr_bearer{n}", f, t))
    gap = (FIRE_Z[1] - FIRE_Z[0] - BARS * BAR_W) / (BARS - 1)
    for n in range(BARS):
        z0 = FIRE_Z[0] + n * (BAR_W + gap)
        for side, (x0, x1) in (("w", (IN_X[0], XM)), ("e", (XM, IN_X[1]))):
            out.append(box([x0, BAR_Y[0], z0], [x1, BAR_Y[1], z0 + BAR_W], f"fr_bar{n + 1}{side}", f, t))
    for name, (z0, z1) in PLATE_BEARERS:
        lo, hi = plate_bearer(z0, z1)
        out += [box(l2, h2, n2, f, t) for n2, l2, h2 in split_blocks(f"fr_platebearer_{name}", lo, hi)]
    out += [box(lo, hi, name, f, t) for name, lo, hi in back_iron()]
    out += [box(lo, hi, name, f, t) for name, lo, hi in binding()]
    out += [box(lo, hi, name, f, t) for name, lo, hi in ties()]
    return out


def build_frame():
    """Everything that stands still and is not the furnace's contents or fittings: the brick (brickwork and lining,
    the vault with it) and the iron frame."""
    return build_brick() + build_vault() + build_iron()


def build_plate():
    """The hearth plate (`plate`, a fitting): one cast-iron slab falling to the front over the firebox and the niche,
    its edges against the side walls, on its two bearers and the front wall's top; raised rims along both sides and
    across the high end, and across the low end but for the gap in its middle, where the spout leaves: a short open
    trough, steeper than the plate, from the low end forward over the receiving pot."""
    p, t = "plate", "iron"
    length = (PLATE_Z[1] - PLATE_Z[0]) / math.cos(PLATE_ANGLE)
    zm = (PLATE_Z[0] + PLATE_Z[1]) / 2
    thick = PLATE_TV * math.cos(PLATE_ANGLE)
    out = [on_plate(zm, -thick, length, thick, IN_X[0], IN_X[1], "plate_slab", p, t)]
    rw, rh = RIM
    for side, (x0, x1) in (("w", (IN_X[0], IN_X[0] + rw)), ("e", (IN_X[1] - rw, IN_X[1]))):
        out.append(on_plate(zm, 0.0, length, rh, x0, x1, f"plate_rim_{side}", p, t))
    end = rw / math.cos(PLATE_ANGLE)                          # a rim across, its plan width rw
    out.append(on_plate(PLATE_Z[1] - rw / 2, 0.0, end, rh - 0.01, IN_X[0] + rw, IN_X[1] - rw, "plate_rim_high", p, t))
    for side, (x0, x1) in (("w", (IN_X[0] + rw, SPOUT_GAP[0])), ("e", (SPOUT_GAP[1], IN_X[1] - rw))):
        out.append(on_plate(PLATE_Z[0] + rw / 2, 0.0, end, rh - 0.01, x0, x1, f"plate_rim_low{side}", p, t))
    # the spout: its bottom continues the plate's top at the low end, its sides stand on it
    (sx0, sx1) = SPOUT_GAP
    sw, sh = SPOUT_SIDE
    out.append(on_spout(SPOUT_LEN / 2, -SPOUT_T, SPOUT_LEN, SPOUT_T, sx0, sx1, "plate_spout", p, t))
    for side, (x0, x1) in (("w", (sx0, sx0 + sw)), ("e", (sx1 - sw, sx1))):
        out.append(on_spout(SPOUT_LEN / 2, 0.0, SPOUT_LEN - 0.02, sh, x0, x1, f"plate_spout_{side}", p, t))
    return out


def build_pot():
    """The receiving pot (`pot`, a fitting): a cast-iron kettle standing on the niche's floor under the spout's lip, an
    octagon: its foot solid, a narrower lower course and the body over it, its mouth open."""
    p = "pot"
    cx, cz = POT_C
    y0, y1, a = POT_FOOT
    out = oct_disc(cx, cz, y0, y1, a, "pot_foot", p, "iron")
    for name, y0, y1, a in POT_TUBES:
        out += oct_tube(cx, cz, y0, y1, a, POT_WALL, f"pot_{name}", p, "iron")
    return out


def build_door():
    """The fire door (`door`, a fitting): a cast-iron plate sliding in its guides in the back face's recess, open (the
    rest pose: slid up DOOR_TRAVEL clear of the fire hole), with a handle bar across its outer face near its foot."""
    p = "door"
    (x0, x1) = (JAMBS[0][1], JAMBS[1][0])
    y0, y1 = DOOR_SHUT_Y[0] + DOOR_TRAVEL, DOOR_SHUT_Y[1] + DOOR_TRAVEL
    out = [box([x0, y0, DOOR_Z[0]], [x1, y1, DOOR_Z[1]], "door_plate", p, "iron")]
    (hx, hy, hz) = HANDLE
    out.append(box([hx[0], y0 + hy[0], hz[0]], [hx[1], y0 + hy[1], hz[1]], "door_handle", p, "iron"))
    return out


def build_damper(place):
    """The ash mouth's damper (`damper<place>`): an iron plate sliding sideways between its rails in the back face's
    recess, with a knob, in one of its three places (DAMPER). Each place is its own part; gameplay draws one."""
    p = f"damper{place}"
    x0 = DAMPER[place]
    out = [box([x0, DAMPER_Y[0], DAMPER_Z[0]], [x0 + DAMPER_W, DAMPER_Y[1], DAMPER_Z[1]], f"{p}_plate", p, "iron")]
    (kx, kz, ky) = KNOB
    out.append(box([x0 + kx[0], ky[0], kz[0]], [x0 + kx[1], ky[1], kz[1]], f"{p}_knob", p, "iron"))
    return out


def build_cap():
    """The outlet's cap (`outletcap`): a flat iron plate set flush in the curb's seat."""
    (x0, x1), _, (z0, z1) = CAP_SEAT
    return [box([x0, CAP_Y[0], z0], [x1, CAP_Y[1], z1], "outletcap_plate", "outletcap", "iron")]


def cake_places():
    """Each cake's place on the plate: (x0, x1, z) for its middle up the slope."""
    return [(x0, x1, z) for z in CAKES_Z for x0, x1 in CAKES_X]


def build_cakes():
    """The charge (`cakes`): four cakes of tin-lead laid on the plate's upper part, over the fire, between its rims."""
    along, thick = CAKE
    return [on_plate(z, 0.0, along, thick, x0, x1, f"cakes_{n}", "cakes", "cake")
            for n, (x0, x1, z) in enumerate(cake_places(), 1)]


LEAD_TURNS = (18.0, -24.0, 31.0, -12.0)


def build_lead():
    """The lead left on the plate (`lead`): where each cake lay, a flattened, spongy lump of lead, smaller than the
    cake (its tin has run), with a knob of it standing up."""
    along, _ = CAKE
    out = []
    for n, (x0, x1, z) in enumerate(cake_places(), 1):
        inset = 0.6
        out.append(on_plate(z, 0.0, along - 1.2, 0.8, x0 + inset, x1 - inset, f"lead_{n}", "lead", "lead"))
        cx = (x0 + x1) / 2 + (0.8 if n % 2 else -0.8)
        knob = on_plate(z + 0.4, 0.75, 1.6, 0.8, cx - 0.9, cx + 0.9, f"lead_knob{n}", "lead", "lead")
        rotate([knob], "y", LEAD_TURNS[n - 1], knob.c)
        out.append(knob)
    return out


def build_tinrun():
    """The tin running (`tinrun`): a thin stream down the plate's middle from under the lower cakes to the low end, on
    down the spout's trough, and falling from its lip into the pot."""
    p, t = "tinrun", "tin"
    lowest = min(z for _, _, z in cake_places()) - CAKE[0] / 2
    z0, z1 = PLATE_Z[0], lowest + 0.5
    along = (z1 - z0) / math.cos(PLATE_ANGLE)
    gap = (CAKES_X[1][0] - CAKES_X[0][1]) / 2 - 0.05          # between the cakes' edges
    out = [on_plate((z0 + z1) / 2, 0.0, along, 0.12, XM - gap, XM + gap, "tinrun_plate", p, t)]
    sw = SPOUT_SIDE[0]
    out.append(on_spout(SPOUT_LEN / 2, 0.0, SPOUT_LEN - 0.04, 0.12, SPOUT_GAP[0] + sw, SPOUT_GAP[1] - sw, "tinrun_spout", p, t))
    lx, ly, lz = spout_lip()
    out.append(box([lx - 0.3, TIN_LEVEL[1], lz - 0.62], [lx + 0.3, ly + 0.08, lz - 0.02], "tinrun_stream", p, t))
    return out


def build_tin():
    """The tin in the pot (`tin`): its molten surface, 1.5 under the rim."""
    return oct_disc(POT_C[0], POT_C[1], TIN_LEVEL[0], TIN_LEVEL[1], TIN_A, "tin_pool", "tin", "tin")


LUMP_TURNS = ((("y", 28.0), ("x", 21.0)), (("y", 63.0), ("z", 17.0)), (("y", 11.0), ("x", -24.0)), (("y", 47.0), ("z", -19.0)))


def lump_places():
    """Where the lumps lie on a fuel layer's top: two rows of three across the grate."""
    xs = (IN_X[0] + 3.0, XM, IN_X[1] - 3.0)
    zs = (FIRE_Z[0] + 3.5, FIRE_Z[1] - 3.5)
    return [(x, z) for z in zs for x in xs]


def build_fuel(j, kind):
    """The fuel's layer j (`fuel<j>`), or the same burning (`fire<j>`, kind "fire": the ember texture, its faces
    glowing): one bed across the grate, with lumps lying on its top. The burning bottom layer also has cinders
    fallen through the grate onto the ash pit's floor."""
    p = f"{kind}{j}"
    tex = "fuel" if kind == "fuel" else "ember"
    y0, y1 = FUEL_LAYERS[j - 1]
    out = []
    for side, (x0, x1) in (("w", (IN_X[0], XM)), ("e", (XM, IN_X[1]))):
        out.append(box([x0, y0, FIRE_Z[0]], [x1, y1, FIRE_Z[1]], f"{p}_bed{side}", p, tex))
    for n, (x, z) in enumerate(lump_places()):
        out.append(lump([x + (0.6 if j == 2 else -0.6), y1 + 0.1, z], LUMP, LUMP_TURNS[(n + j) % 4], f"{p}_lump{n + 1}", p, tex))
    if kind == "fire" and j == 1:
        y = ASH_PIT_Y[0]
        for n, (x, z, w, d, deg) in enumerate(((11.5, 17.0, 3.0, 2.4, 20.0), (19.5, 19.5, 2.6, 2.0, -35.0),
                                               (14.0, 23.0, 2.2, 1.8, 60.0)), 1):
            out.append(bar([x, y + 0.3, z], (w, 0.6, d), (("y", deg),), f"{p}_cinder{n}", p, tex))
    return out


def build():
    els = build_door() + build_plate() + build_pot()
    els += build_cakes() + build_tinrun() + build_tin() + build_lead()
    for kind in ("fuel", "fire"):
        for j in (1, 2):
            els += build_fuel(j, kind)
    for place in DAMPERS:
        els += build_damper(place)
    return els + build_cap() + build_frame()


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def door_drivers():
    """Shut as depth goes 0 -> 1: slid down DOOR_TRAVEL from open (raised clear of the fire hole) over it. It also
    carries theta at ratio 0 first (the clock; it moves nothing)."""
    pivot = pt(XM, DOOR_SHUT_Y[1], (DOOR_Z[0] + DOOR_Z[1]) / 2)
    return [{"type": "rotate", "axis": "y", "pivot": pivot, "ratio": 0.0},
            {"type": "step", "motion": "slide", "axis": "y", "from": 0.0, "to": 1.0, "amount": r6(-DOOR_TRAVEL / B)}]


FITTINGS = ("plate", "pot", "door")
PART_IDS = ("brickwork", "lining") + FITTINGS + CONTENTS + tuple(f"damper{d}" for d in DAMPERS) + ("outletcap",)
MATCH = {"brickwork": "bk_*", "lining": "ln_*"}
REQUIRES_OF = {p: "fittings" for p in FITTINGS}   # the fittings stage's parts: the plate, the pot and the door


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
# A pose is (theta, depth, scene): the clock, the fire door, and what is fitted. A scene is (the charge's state, the
# fire's state, the damper's place, whether the outlet is capped, the build's state): gameplay's requires. The contents
# and the damper are fitted only once the furnace is built (the fittings stage), the cap once the stack is lined (the lining).
DEFAULT_SCENE = ("sweating", "lit", "open", False, "complete")
REST = (0.0, 0.0, DEFAULT_SCENE)
SHUT = 1.0


def inputs_of(pose):
    return {"theta": pose[0], "depth": pose[1]}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


def fitted(scene):
    """The requires values fitted in a scene."""
    charge, fire, damper, capped, stage = scene
    out = set(dict(STAGE_STATES)[stage])
    if "fittings" in out:
        out |= set(dict((s[0], s[2]) for s in CHARGE_STATES)[charge])
        out |= set(dict((s[0], s[2]) for s in FIRE_STATES)[fire])
        out.add(f"damper{damper}")
    if capped and "lining" in out:
        out.add("outletcap")
    return out


def on_show(part, scene):
    """Whether a part is drawn in a scene: the iron frame always, the rest when what fits them is fitted."""
    return part == "frame" or requires_of(part) in fitted(scene)


def shown(parts, posed_els, pose):
    """The posed elements as they can be seen in the pose's scene: every part not drawn moved far away, each part by
    its own distance, so a hidden part's own elements still lie as they do together."""
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
    """The furnace's eight cells."""
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


def regions():
    """Where the model may be (voxels, (lo, hi)): the machine's box."""
    return [((0.0, 0.0, 0.0), (W, H, D))]


ANCHORS = ("plate", "pot", "spout", "fireDoor", "ash", "opening")


def plate_point():
    """The middle of the cakes' place on the plate's top: charged and raked from the front, through the opening."""
    z = sum(CAKES_Z) / len(CAKES_Z)
    return (XM, plate_top(z), z)


def pot_point():
    """The middle of the receiving pot's mouth: the tin taken from it."""
    return (POT_C[0], POT_TOP, POT_C[1])


def fire_door_point():
    """The middle of the fire hole in the back face: the fuel goes in here (the stoker's)."""
    (x0, x1), (y0, y1) = FIRE_HOLE
    return ((x0 + x1) / 2, (y0 + y1) / 2, D)


def ash_point():
    """The middle of the ash mouth in the back face: the damper, worked from behind."""
    (x0, x1), (y0, y1) = ASH_MOUTH
    return ((x0 + x1) / 2, (y0 + y1) / 2, D)


def opening_point():
    """The middle of the working opening in the front face, over the plate's low end: the chamber's light and fumes."""
    return (XM, (PLATE_LOW_TOP + intrados(XM)) / 2, 0.0)


def outlet_cell():
    return tuple(int(v // B) for v in ((STACK[0][0] + STACK[0][1]) / 2, FLOOR_TOP - 1.0, (STACK[2][0] + STACK[2][1]) / 2))


MIN_CHIMNEY = 6                              # chimney blocks stood on the outlet, counted from it up (the crucible
#                                              furnace's MinChimneyHeight)


def box_volume(el):
    return abs(el.size[0] * el.size[1] * el.size[2])


def vault_volume():
    """The vault zone's brick, in cubic voxels, as (the arch ring's fired brick, the lining's refractory): the zone's
    box less the chamber's segment under the arc, the first FRONT_RING of it the ring's."""
    (x0, x1), (y0, y1), (z0, z1) = VAULT_ZONE
    a = (x1 - x0) * (y1 - y0) - segment_area()
    return a * FRONT_RING, a * (z1 - z0 - FRONT_RING)


def build_volumes(els):
    """The brickwork's and the lining's volumes, in blocks, measured from their elements: the brick boxes (each lattice
    box in exactly one), and the vault's zone, which its staves and backing fill but for the chamber under the arc."""
    ring, vault = vault_volume()
    bk = sum(box_volume(e) for e in els if e.name.startswith("bk_brick_")) + ring
    ln = sum(box_volume(e) for e in els if e.name.startswith("ln_brick_")) + vault
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
             "materials": materials(((FIRED_BRICK, c["bricks"]),))},
            {"requires": "lining", "volumeBlocks": r6(c["liningVolume"]), "perClick": "stack",
             "materials": materials(((LINING_BRICK, c["lining"]), (FIRECLAY, c["fireclay"])))},
            {"requires": "fittings", "parts": list(FITTINGS), "also": [f"damper{d}" for d in DAMPERS],
             "materials": materials(FITTINGS_STAGE)},
        ],
        "outletCap": materials((CAP_ITEM,))[0],
        "_comment": "The build's stages, in order. frame: the iron frame's grid recipe (with a hammer); placing it puts "
                    "down the controller and its ghosts, and draws the frame part. Each stage then fits its requires "
                    "when its materials are all in: brickwork, then lining (the vault with it), then fittings (parts: "
                    "the hearth plate with its spout, the receiving pot and the fire door; also: the damper, in one "
                    "of its places). volumeBlocks: the stage's brick measured from the model, in blocks; its "
                    "quantities are that at 2 fired bricks or 4 refractory bricks a block (the game's brick block "
                    "recipes), rounded up, fire clay one per 2.5 refractory bricks. perClick stack: a click puts in as "
                    "many of the held stack as are still needed. oneType: the first fired brick put in sets the "
                    "furnace's brick type (bricks), and the rest must match. The frame's recipe and the fittings' "
                    "materials are proposals (LiquationFurnace/README.md). outletCap: an item interaction, not a "
                    "stage. The contents and the damper are drawn once the fittings are in, the cap once the lining is."}


def bricks_json():
    return {"code": "brick", "item": "game:burnedbrick-{type}", "default": BRICK_DEFAULT,
            "types": {t: {"block": block, "base": base, **({"overlays": [over]} if over else {})}
                      for t, block, base, over in BRICKS},
            "_comment": "The fired brick types the brickwork takes (every game:burnedbrick type), each with the block "
                        "its bricks make and that block's texture: the renderer sets the shape's 'brick' code to the "
                        "furnace's type's base, with its overlays drawn over it. The furnace's type is the first brick "
                        "put in; until then no brick is drawn. A type missing here (another mod's) is refused. The "
                        "crucible furnace's list (cruciblefurnace-rig.json)."}


def make_rig(parts):
    sx, sz = (STACK[0][0] + STACK[0][1]) / 2, (STACK[2][0] + STACK[2][1]) / 2
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the front west "
                    "cell at ground level, where the niche is. A tin-lead liquation furnace (#929): an iron hearth "
                    "plate falling to the front under a low vault open at the front, over a fire; its low end spouts "
                    "into the receiving pot in the niche under it. Worked from the front (plate: the cakes put on and "
                    "the lead taken off; pot: the tin taken out) and fired from the back (fireDoor; ash, the damper). "
                    "Furnaces stand side by side in a row along x (up to furnace.rowMax), sharing one chimney on one "
                    "member's outlet, the others capped. No power and no forced air: natural draught, in at the ash "
                    "mouth under the damper and at the working opening, out at the outlet. depth is the fire door: 0 "
                    "open, slid up in its guides (the rest pose), 1 shut over the fire hole. theta is the stoker's "
                    "clock, carried by a ratio-0 rotate on the door: nothing turns. requires: the build's stages, "
                    "brickwork, lining and fittings (build; the fittings are the parts plate, pot and door; the frame, "
                    "iron, has none); cakes, tinrun, tin and lead (the charge); fuel1, fuel2 (the fuel's two layers "
                    "from the grate up) and the same burning, fire1, fire2; the damper's place, damperopen, damperhalf "
                    "or dampershut (one at a time); and outletcap. The brickwork's texture is the furnace's brick "
                    "type's (bricks). outletCell: the stack's top, in the back row's east cell (a chimney stands on "
                    "one cell), the chimney stood on its top; chimney: the shortest chimney, counted from the outlet "
                    "up. See LiquationFurnace/README.md.",
        "cells": [],
        "plate": {"pos": pt(*plate_point())},
        "plateSide": "north",
        "pot": {"pos": pt(*pot_point())},
        "spout": {"pos": pt(*spout_lip())},
        "opening": {"pos": pt(*opening_point())},
        "fireDoor": {"pos": pt(*fire_door_point())},
        "fireDoorSide": "south",
        "ash": {"pos": pt(*ash_point())},
        "ashSide": "south",
        "outletCell": list(outlet_cell()),
        "outletFace": "up",
        "chimney": {"origin": pt(sx, FLOOR_TOP + MIN_CHIMNEY * B / 2, sz), "axis": "y", "length": float(MIN_CHIMNEY)},
        "build": build_json(),
        "bricks": bricks_json(),
        "furnace": {"fuelLayers": len(FUEL_LAYERS), "cakes": len(cake_places()), "minChimney": MIN_CHIMNEY,
                    "rowMax": ROW_MAX, "rowAxis": "x", "doorTravel": r6(DOOR_TRAVEL / B),
                    "plateSlope": PLATE_SLOPE,
                    "_comment": "fuelLayers: the fuel on the grate is drawn as its layers fuel1.. (fire.. while lit), "
                                "how much a layer holds is gameplay's; cakes: the cakes drawn on the plate; "
                                "minChimney: chimney blocks stood on the outlet, counted from it up; rowMax, rowAxis: "
                                "furnaces in a row, side by side along x, sharing one chimney; doorTravel: how far the "
                                "fire door slides (blocks, the door's step); plateSlope: the hearth plate's fall to "
                                "the front, rise per run."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: it is the build frame's corner)."""
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
    ship["outletCell"] = shift_cell(rig["outletCell"], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["chimney"] = {**rig["chimney"], "origin": shift_point(rig["chimney"]["origin"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


CELL_PARTS = ("frame", "brickwork", "lining") + FITTINGS   # the cells' boxes: the furnace built, the door shut
CELL_POSE = (0.0, SHUT, DEFAULT_SCENE)


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed by the shipped rig with the door shut; a lid (a deck)
    on every top cell, at the top."""
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
    return {"_comment": f"Generated by {SCRIPT} from the shipped liquationfurnace-rig.json's parts: each part's matrix as "
                        "3 rows of 4 (block units) at each pose (depth, the fire door: 0 open, 1 shut; theta moves "
                        "nothing). The site's and the Python tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    shape = machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Texture codes the renderer "
             "sets: 'brick' (the common brickwork: the furnace's brick type's block texture, from the rig's bricks; "
             "the shape's own is the red brick's colour layer). The burning fuel ('ember') glows. Keep element names "
             "when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)
    for e in shape["elements"]:
        if e["name"].split("_")[0].startswith("fire"):
            for f in e["faces"].values():
                f["glow"] = FIRE_GLOW
    return shape


def coplanar_poses():
    scenes = [(ch, fi, "open", False, "complete") for ch, fi in
              (("sweating", "lit"), ("charged", "laid"), ("sweated", "low"), ("tapped", "cold"), ("empty", "lit"))]
    scenes += [("empty", "cold", "half", True, "complete"), ("empty", "cold", "shut", True, "complete")]
    scenes += [("empty", "cold", "open", False, "frame"), ("empty", "cold", "open", False, "brickwork"),
               ("empty", "cold", "open", True, "lining")]
    return tuple((0.0, dp, sc) for sc in scenes for dp in (0.0, 0.5, 1.0))


def fix_coplanar(els, parts):
    def posed_fn(es, pose):
        return shown(parts, [posed(el, pm(parts, el.part, pose)) for el in es], pose)
    return fix_coplanar_posed(els, posed_fn, coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the liquation furnace's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_liquationfurnace
    els = build()
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        print(f"coplanar faces before the fix: {sum(len(p) for p in before.values())} pairs over {len(before)} poses")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_liquationfurnace.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "liquationfurnace.json", args.out / "liquationfurnace_frame.json",
                args.out / "liquationfurnace-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "liquationfurnace.json", SHAPE_DIR / "liquationfurnace_frame.json",
                RIG_DIR / "liquationfurnace-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_liquationfurnace.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
