#!/usr/bin/env python3
"""Generate the Parkes kettle's shapes, rig and reference poses (Python 3.11 stdlib only).

The Parkes kettle desilverizes lead (#927, model #928): a cast-iron kettle of molten silver-lead over its own fire,
into which zinc is stirred; the zinc takes up the silver and rises as a crust that is skimmed off, leaving
desilverized lead to be ladled out. It is one machine, built on the crucible furnace's conventions (its generator,
`CrucibleFurnace/tools/make_shape.py`, is the template): a block of brickwork three blocks wide (x), three deep (z)
and two high, any one type of fired brick with tier-3 refractory lining 3 voxels thick on every hot face, bound with
angle iron (an angle wrapping each vertical corner, angle bands wrapping the top edges of its front and back), its
top flat (the floor). The kettle hangs by its flange in the setting, the flange flush
with the floor, centred left to right in the front two rows: a faceted hemisphere 24 across inside, its flange 28
across. Under it the fire burns on a grate of iron firebars over an ash pit; the fuel goes in at a fire door in the
front (an iron door lifting in guides), the air at the ash mouth under it, which an iron damper slides across (open,
half, shut): natural draught. The flame rises round the kettle in the setting and leaves at the back by a flue to a
stack in the back row's east corner, whose outlet on the top takes the player's chimney or a flat iron cap.

Over the kettle stands the stirrer's gallows: two cast-iron plate standards on the floor east and west of it, joined
by three beams. The axle comes in at the back, at the gallows' height (the power cell, the back middle cell over the
setting), along a drive shaft carried by a pedestal bearing on the back row and the gallows' middle beam, to a bevel
pinion meshing with a mitre wheel on the stirrer's vertical shaft, which hangs from the gallows' upper beam and turns
in its lower one, two flat paddles crossing at its foot in the bath. Flat paddles stir alike either way, so there is
no rectifier: the stirrer turns with the axle, at its speed, the way the mitre pair sends it.

Every element was made for this mod; no other mod's model is used. It writes, deterministically,

    parkeskettle.json          the whole machine, every part that moves or comes and goes   (assets/.../shapes/block/)
    parkeskettle_frame.json    the iron frame only (the placed frame block and the item)    (assets/.../shapes/block/)
    parkeskettle-rig.json      cells, anchors, the build and the part rig                   (assets/.../config/)
    rig-reference.json         every part's matrix at a grid of poses                       (tests/ParkesKettle/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_parkeskettle.py) and exits non-zero if
a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the machine box's
north-west-bottom corner. The fire door and the ash mouth face the front (north), the player; the stack and the drive
are in the back row (south). The controller cell is the front middle cell at ground level, build cell (1, 0, 0), as
the crucible furnace's: the shipped files move it to [0,0,0].

The rig's inputs, as this machine uses them:

    theta  the axle's angle about native z (the power face is south): the drive shaft and its pinion turn with it, the
           stirrer with it through the mitre pair (ratio +1 about y).
    depth  the fire door: 0 shut (the rest pose), 1 open, lifted in its guides. A step slide.

Everything else that shows or not (the build's stages: brickwork, lining, kettle (with the fire door and the damper)
and stirrer (with the gallows and the drive); the bath at four levels, the zinc, the crust; the fuel in two layers and
the same fuel burning; the damper's three places; the outlet's cap) is a `requires`: gameplay's state, not a motion.
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
REFERENCE_OUT = MOD / "tests" / "ParkesKettle" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/ParkesKettle/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
OCT = math.tan(math.pi / 8)                  # an octagon's half side over its apothem

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 3, 2, 3          # the setting: three wide (x, the row's way), two high, three deep (z)
ORIGIN_CELL = (1, 0, 0)                      # the controller: the front middle cell at ground level (the fire's front)
# Over the setting, the stirrer's gallows and its drive: the middle row's three cells (the standards, the beams, the
# gears) and the back middle cell, the power cell, where the axle comes in at the gallows' height from the back. The
# front row's three cells are the kettle's mouth's: hollow (the machine's, with no boxes of their own), so nothing is
# built over the kettle where the lead goes in and the crust and the lead come out, and the flange, standing a little
# proud of the floor, is in its own cells. The back west cell is free; the back east one is the chimney's, standing on
# the outlet.
UPPER_CELLS = ((0, 2, 1), (1, 2, 1), (2, 2, 1), (1, 2, 2))
HOLLOW_CELLS = ((0, 2, 0), (1, 2, 0), (2, 2, 0))
POWER_CELL, POWER_FACE = (1, 2, 2), "south"
CHIMNEY_CELL = (2, 2, 2)
FREE_CELLS = ((0, 2, 2),)
ROW_MAX = 5                                  # kettles in a row, side by side along x, sharing one chimney

TEXTURES = {
    "brick": "game:block/clay/brick/four/running/red1",     # the common brickwork: the red brick's colour (BRICKS)
    "lining": "game:block/clay/refractory/tier3/front1",    # the hot-face lining, the seat, the outlet's curb
    "iron": "game:block/metal/plate/iron",                  # the kettle (cast iron), the frame, the gallows, the doors
    "steel": "game:block/metal/sheet-plain/steel1",         # the shafts and the gears, as the draw bench's
    "oak": "game:block/wood/debarked/oak",                  # the axle's oak continuation at the power face
    "lead": "game:block/metal/ingot/lead",                  # the bath: the renderer may set its glow
    "zinc": "game:block/metal/ingot/zinc",                  # the zinc stirred in
    "crust": "game:block/metal/ingot/zinc",                 # the zinc crust (zinc, silver and lead) that rises
    "fuel": "game:block/coal/bituminous",                   # the coal on the grate
    "ember": "game:block/coal/ember",                       # the same coal, burning
}
TEX = 16                                     # shape texture units: one a voxel, one texture across a block face
FIRE_GLOW = 200                              # the burning coal's faces (the game's lit firepit embers are 160)

W, H, D = CELLS_X * B, CELLS_Y * B, CELLS_Z * B
FLOOR_TOP = H                                # the setting's top: the floor, flat but for the kettle and the outlet

# ---------------------------------------------------------------- the kettle
KX, KZ = 24.0, 20.0                          # the kettle's axis: centred left to right, its flange clear of the
#                                              chimney's cell behind (x 32.., z 32..)
RIM_Y = 31.0                                 # the bowl's rim, the centre of its sphere; the flange lies on it
RI = 12.0                                    # the bowl's inside radius: a hemisphere 24 across inside
KT = 1.0                                     # its wall
RM = RI + KT / 2
RF = 14.0                                    # the flange's outer apothem: 28 across, "about two blocks"
FLANGE_PROUD = 0.15                          # the flange stands this little proud of the floor: it lies bedded in the
#                                              seat's brick round the kettle's mouth, whose top it would z-fight flush
FLANGE_Y = (RIM_Y, FLOOR_TOP + FLANGE_PROUD)
BANDS = (0.0, 22.5, 45.0, 67.5)              # the wall's facets' edges, degrees below the rim (eight facets a band)
BAND_LAP = 0.25                              # each facet runs on past its band's edge into the next (hidden)
BOTTOM_Y = 19.9                              # the bowl's flat bottom's top: inside, under the lowest band's edge
BOTTOM_A = 5.2                               # its apothem, tucked under the lowest band's facets
CHARGE_UNITS = 2400                          # a charge (#927): the kettle drawn full holds this many units
VOXELS_PER_UNIT = 1.1                        # a unit of metal, cubic voxels (the crucible furnace's figure)
LEVELS = 4                                   # the bath is drawn at a quarter, a half, three quarters and full
BATH_T = 0.5                                 # the bath's surface layer
SEAT_Y = 29.0                                # the seat: the lining's top courses round the bowl, which the flange lies on
SEAT_MARGIN = 0.15                           # the seat's opening (MOUTH_RECTS) stands this far off the bowl ...
SEAT_EDGE = 0.5                              # ... and this far inside the flange's edge, along the diagonals (x + z)
CAVITY = ((10.5, 37.5), (10.0, SEAT_Y), (6.5, 33.5))  # the setting's inside: the fire under the kettle and the flame's
#                                                       way round it, square, the seat closing its top round the bowl

# ---------------------------------------------------------------- the fire: the ash pit, the grate, the doors
CAVE = ((14.0, 34.0), (1.0, 7.0), (9.0, 29.0))           # the ash pit under the grate
BEARER_Y = (7.0, 8.5)                        # the iron bearers under the firebars, along z, ends built into the walls
BEARER_X = ((14.0, 15.5), (23.25, 24.75), (32.5, 34.0))
BEARER_Z = (8.0, 30.0)
BAR_Y = (8.5, 10.0)                          # the firebars, along x, two lengths a row (wall to the middle bearer)
BARS, BAR_W = 10, 1.2                        # ten rows 1.2 wide across the 20: gaps of 0.89
GRATE = ((14.0, 34.0), (7.0, 10.0), (9.0, 29.0))         # the grate's room
GRATE_TOP = BAR_Y[1]
MOUTH = ((19.0, 29.0), (1.0, 6.0))           # the ash mouth, centred, through the front wall to the ash pit: the air
FIRE_DOOR = ((20.0, 28.0), (11.0, 17.0))     # the fire door's opening, centred, through the front wall to the fire
FRONT = ((17.0, 31.0), (0.0, 19.0), (1.0, 2.0))          # the cast-iron front set in the brick behind the recesses
D_RECESS = ((17.0, 41.0), (0.0, 7.2), (0.0, 1.0))        # the damper's recess: it slides east across the mouth
D_RAILS = ((0.0, 0.5), (6.7, 7.2))           # its iron rails, under and over it, the recess's length
DAMPER_W, DAMPER_Y, DAMPER_Z = 11.0, (0.5, 6.5), (0.35, 0.85)
DAMPER = {"shut": 18.5, "half": 24.0, "open": 29.6}       # its west edge in each place: shut over the whole mouth,
#                                                           half over its east half, open clear of it
F_RECESS = ((17.0, 31.0), (10.5, 25.0), (0.0, 1.0))      # the fire door's recess: it lifts in it
F_GUIDES = ((17.0, 17.8), (30.2, 31.0))      # iron guides at the recess's sides, its full height
FD_Y = (10.5, 17.6)                          # the door shut: over the whole opening
FD_Z = (0.45, 0.85)
FD_LIFT = 7.2                                # how far it lifts to open: clear of the opening, inside its recess

# ---------------------------------------------------------------- the flue, the stack, the outlet
FLUE_OPENING = ((28.0, 36.0), (22.0, 27.0), (CAVITY[2][1], 35.5))   # through the setting's back wall, under the seat
BACK_FLUE = ((28.0, 42.5), (21.0, 27.0), (35.5, 42.5))
STACK = ((35.5, 42.5), (21.0, 31.5), (35.5, 42.5))       # in the back row's east corner, under the outlet, clear of
#                                                          the back's top angle band
CURB = ((33.5, 44.5), (30.0, FLOOR_TOP), (33.5, 44.5))   # the outlet's curb, the lining's, flush: a rim 1 wide round
CURB_RIM = 1.0                                           # the cap's seat, the seat a ledge 1 wide round the stack
CAP_Y = (31.5, FLOOR_TOP)
CAP_SEAT = ((CURB[0][0] + CURB_RIM, CURB[0][1] - CURB_RIM), CAP_Y, (CURB[2][0] + CURB_RIM, CURB[2][1] - CURB_RIM))
MIN_CHIMNEY = 6                              # chimney blocks stood on the outlet, counted from it up (the crucible
#                                              furnace's MinChimneyHeight)

# the brick: in two roles, as the crucible furnace's
LINING_T = 3.0
MIN_WALL = 5.5                               # the least brick between the setting's inside and its faces: the front wall
#                                              is 6.5, 5.5 behind the doors' recesses (the kettle stands as far back as
#                                              the chimney's cell lets its flange)
BIND_T = 1.0                                 # the binding is angle iron (the owner's rule for brick structures): its
BIND_LEG = 2.5                               # legs 1 thick and 2.5 long, from the brick's corner out along each face

# ---------------------------------------------------------------- the gallows, the drive, the stirrer
AXLE = (KX, 40.0)                            # (x, y): the drive's axis along z, the power cell's face centre
SHAFT_R = 1.0                                # the drive shaft's apothem (an octagon)
STIR_R = 0.75                                # the stirrer's shaft's
GEAR_MOD = 1.2
PINION_TEETH, WHEEL_TEETH = 6, 6             # a mitre pair: the stirrer turns at the axle's speed
GEAR_FACE = 2.0
APEX = (KX, AXLE[1], KZ)                     # the pair's cone apex: where the two axes cross
STIR_RATIO = 1.0                             # the wheel under the apex, the pinion behind it: the same sign as theta
FOOT_Y = (FLOOR_TOP, 32.6)                   # the standards' feet on the floor
STANDARD_X = ((4.0, 6.0), (42.0, 44.0))      # the plate standards, east and west of the kettle, clear of its flange
STANDARD_Z = (18.5, 28.5)
LOWER = ((33.0, 34.6), (19.2, 20.8))         # the lower beam (y, z): the stirrer's lower bearing
MIDDLE = ((38.4, 41.6), (26.2, 27.8))        # the middle beam: the drive shaft's front bearing
UPPER = ((44.8, 46.4), (19.2, 20.8))         # the upper beam: the stirrer hangs from it
GAL_TOP = UPPER[0][1]
BOSS_LOW = ((32.8, 34.8), (18.0, 22.0))
BOSS_MID = ((37.6, 42.4), (25.8, 28.2))
BOSS_UP = ((44.6, 46.6), (18.0, 22.0))
PEDESTAL_Z = (42.0, 45.0)                    # the back pedestal's bearing (z), on the back row
OAK_Z = (46.0, D)                            # the axle's oak continuation at the power face
STIR_Y = (21.6, 47.2)                        # the stirrer's shaft, the paddles at its foot, its collar on top
PADDLES = (((21.8, 25.0), 5.6), ((25.0, 29.4), 8.6))     # two tiers of the flat paddles: (y span, reach)
PADDLE_T = 0.8
ZINC_AT = (20.0, 140.0, 260.0)               # degrees round from +x towards +z: the zinc floating, beyond the paddles
ZINC_R, ZINC_SIZE = 10.1, (2.6, 0.6, 1.4)    # its distance from the axis; a piece's (tangential, y, radial) size
CRUST_T = 0.7

# ---------------------------------------------------------------- the fuel
FUEL_LAYERS = ((GRATE_TOP, 12.5), (12.5, 15.0))
FUEL_CAPACITY = 8                            # coal on the grate, two layers of four (provisional: #927 has no figure)
FUEL_PER_LAYER = FUEL_CAPACITY // len(FUEL_LAYERS)
LUMP = 1.4

# ---------------------------------------------------------------- the build
# Built in stages (the rig's `build`; their requires values are the viewer's Build states), as the crucible
# furnace's: the iron frame, a grid recipe placed as the machine; the brickwork, then the lining, measured from the
# model at the game's rates; the kettle (the cast-iron kettle set in its seat, with the fire door and the damper);
# the stirrer (the gallows, the drive and the stirrer). The kettle's and the stirrer's materials are proposals, for
# the owner to settle (README, "Open for the owner").
FRAME_RECIPE = (("game:metalplate-iron", 2), ("game:rod-iron", 4), ("game:metalnailsandstrips-iron", 10))
BRICKS_PER_BLOCK = 2
LINING_PER_BLOCK = 4
LINING_PER_FIRECLAY = 2.5
FIRED_BRICK = "game:burnedbrick-*"
LINING_BRICK = "game:refractorybrick-fired-tier3"
FIRECLAY = "game:clay-fire"
KETTLE_STAGE = (("game:metalplate-iron", 6),)
STIRRER_STAGE = (("game:metalplate-iron", 4), ("game:rod-iron", 4), ("game:metalnailsandstrips-iron", 4))
CAP_ITEM = ("game:metalplate-iron", 1)
STAGES = ("brickwork", "lining", "kettle", "stirrer")
STAGE_STATES = (
    ("frame", ()),
    ("brickwork", ("brickwork",)),
    ("lining", ("brickwork", "lining")),
    ("kettle", ("brickwork", "lining", "kettle")),
    ("complete", ("brickwork", "lining", "kettle", "stirrer")),
)

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

# The scenario's states (site/models.json; tools/tests holds the two together). Each is the requires values fitted.
KETTLE_STATES = (
    ("empty", "Empty", ()),
    ("quarter", "A quarter full (600 units)", ("lead1",)),
    ("half", "Half full (1,200 units)", ("lead2",)),
    ("threequarters", "Three quarters full (1,800 units)", ("lead3",)),
    ("full", "Full: a charge of 2,400 units, molten", ("lead4",)),
    ("zinc", "Zinc stirred in", ("lead4", "zinc")),
    ("crust", "The crust risen", ("lead4", "crust")),
)
FIRE_STATES = (
    ("cold", "Cold, the grate bare", ()),
    ("laid", "Coal laid, not lit", ("fuel1", "fuel2")),
    ("lit", "Lit", ("fire1", "fire2")),
    ("low", "Burnt low", ("fire1",)),
)
DAMPERS = ("open", "half", "shut")
CONTENTS = tuple(f"lead{n}" for n in range(1, LEVELS + 1)) + ("zinc", "crust", "fuel1", "fuel2", "fire1", "fire2")


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
    side continue one texture across a block face with no seam (the crucible furnace's: the game's face directions,
    as the mandrel station's banding reads them)."""
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


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def unit(v):
    n = math.sqrt(sum(x * x for x in v))
    return [x / n for x in v]


def obox(c, ax, size, name, part, tex):
    """A box of `size` along the orthonormal world vectors `ax` (local x, y; z is x cross y), centred on c."""
    x, y = unit(ax[0]), unit(ax[1])
    z = cross(x, y)
    r = [[x[i], y[i], z[i]] for i in range(3)]
    return skin(El(name, list(size), list(c), r, {}, part), tex)


def oct_disc(cx, cz, y0, y1, a, name, part, tex):
    """A solid octagonal disc about a vertical axis, apothem `a`, its flats facing the four sides and the diagonals:
    four strips 2a long and one side wide, at 0, 45, 90 and 135 degrees about y. Their union is the octagon."""
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
    the axis (cx, cz): one side of an octagonal ring, or a corner fill. The four square directions are built square
    to the axes."""
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


def rod(axis, c, a0, a1, r, name, part, tex):
    """A solid octagonal rod along `axis` through c (its other two coordinates), apothem r, from a0 to a1: four strips
    turned 0, 45, 90 and 135 degrees about the axis, each a hair shorter than the last so no two ends share a plane."""
    k = {"x": 0, "y": 1, "z": 2}[axis]
    u, w = [q for q in range(3) if q != k]
    s = r * OCT
    out = []
    st = min(0.012, (a1 - a0) / 16)
    for i, ang in enumerate((0.0, 45.0, 90.0, 135.0)):
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[k], hi[k] = a0 + st * i, a1 - st * i
        lo[u], hi[u] = c[u] - r, c[u] + r
        lo[w], hi[w] = c[w] - s, c[w] + s
        el = box(lo, hi, f"{name}_{i + 1}", part, tex)
        if ang:
            rotate([el], axis, ang, c)
        out.append(el)
    return out


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


def blocks_of(name, lo, hi, part, tex):
    """A long box as pieces no longer than a block (split_blocks), each skinned in proportion."""
    return [box(l2, h2, n2, part, tex) for n2, l2, h2 in split_blocks(name, lo, hi)]


# ---------------------------------------------------------------- the kettle's bowl: the maths
def oct_area(a):
    """A regular octagon's area, apothem a."""
    return 8.0 * OCT * a * a


def band_planes():
    """Each band of the bowl's wall: (e0, e1, mean elevation, half its arc), in radians, below the rim."""
    out = []
    for e0, e1 in zip(BANDS, BANDS[1:]):
        a0, a1 = math.radians(e0), math.radians(e1)
        out.append((a0, a1, (a0 + a1) / 2, (a1 - a0) / 2))
    return out


def inner_apothem(y):
    """The bowl's inside at height y: the apothem of the octagon its facets' inner faces bound there (the inside is
    convex, so the nearest facet plane is the wall), 0 under the bottom."""
    if y < BOTTOM_Y:
        return 0.0
    best = 1e9
    for _, _, em, half in band_planes():
        s = (RM * math.cos(half) - KT / 2 - (RIM_Y - y) * math.sin(em)) / math.cos(em)
        best = min(best, s)
    return min(best, RI)


def outer_apothem(y):
    """The bowl's outside at height y (the facets' outer faces)."""
    best = 1e9
    for _, _, em, half in band_planes():
        s = (RM * math.cos(half) + KT / 2 - (RIM_Y - y) * math.sin(em)) / math.cos(em)
        best = min(best, s)
    return best


def bowl_volume(level, dy=0.002):
    """The volume inside the bowl from its bottom up to `level` (cubic voxels)."""
    v, y = 0.0, BOTTOM_Y + dy / 2
    while y < level:
        v += oct_area(inner_apothem(y)) * min(dy, level - (y - dy / 2))
        y += dy
    return v


def level_of(units):
    """The height the bowl's inside, from its bottom, holds `units` at VOXELS_PER_UNIT (bisection)."""
    want = units * VOXELS_PER_UNIT
    lo, hi = BOTTOM_Y, RIM_Y
    if bowl_volume(hi) < want:
        raise ValueError(f"the kettle does not hold {units} units")
    for _ in range(40):
        mid = (lo + hi) / 2
        if bowl_volume(mid) < want:
            lo = mid
        else:
            hi = mid
    return round((lo + hi) / 2, 3)


BATH_LEVELS = tuple(level_of(CHARGE_UNITS * n / LEVELS) for n in range(1, LEVELS + 1))
FULL = BATH_LEVELS[-1]


def mouth_rects():
    """The seat's opening round the kettle's mouth, from SEAT_Y to the floor, as (half x, half z) rectangles about
    the axis whose union is a stepped octagon: it holds the bowl's outer octagon (its diagonal flats covered with
    SEAT_MARGIN to spare) and stays inside the flange's (its corners SEAT_EDGE inside the flange's diagonal flats),
    so the brick round it is the seat the flange lies on all round, and no gap shows round the flange at the floor.
    The lattice is axis-aligned, so the octagon is made of steps."""
    inner = outer_apothem(RIM_Y)
    lo = inner * math.sqrt(2.0) + SEAT_MARGIN            # each inner corner's x + z at least this
    hi = RF * math.sqrt(2.0) - SEAT_EDGE                 # each outer corner's x + z at most this
    step = hi - lo
    xs, x = [], round(inner + 0.1, 3)
    while x > hi / 2:
        xs.append(round(x, 3))
        x -= step
    rects = [(x, round(hi - x, 3)) for x in xs] + [(round(hi / 2, 3), round(hi / 2, 3))]
    rects += [(z, x) for x, z in rects]
    return tuple(sorted(set(rects), reverse=True))


MOUTH_RECTS = mouth_rects()


# ---------------------------------------------------------------- the brickwork
CORNERS = (("nw", 0.0, 0.0, 1, 1), ("ne", W, 0.0, -1, 1), ("sw", 0.0, D, 1, -1), ("se", W, D, -1, -1))
#                                              (tag, the brick's corner x and z, the way into the brick along x and z)


def span(c, sign, a, b):
    """The interval from a to b in from c, inward along `sign`."""
    return tuple(sorted((c + sign * a, c + sign * b)))


def binding():
    """The iron binding, as (name, lo, hi), all angle iron (an L section, legs BIND_T thick and BIND_LEG long),
    flush with the setting's faces, the brick notched for it. At each vertical corner, from the ground to the floor,
    an angle wraps the corner (`fr_angle_<corner>_a` on the front or back face, `_b` on the side face): its inside
    corner bedded against the brick's outside corner, a leg on each face, so it locks the brick in on both. Along the
    top edge of the front and of the back, between the corner angles and joining their face legs, an angle band wraps
    that edge (`fr_band_<face>_a` down the face, `_b` on the top). The side legs lie flush, so a row's neighbours meet
    face to face."""
    t, leg = BIND_T, BIND_LEG
    out = []
    for tag, xc, zc, sx, sz in CORNERS:
        out.append((f"fr_angle_{tag}_a", [span(xc, sx, 0.0, leg)[0], 0.0, span(zc, sz, 0.0, t)[0]],
                    [span(xc, sx, 0.0, leg)[1], H, span(zc, sz, 0.0, t)[1]]))
        out.append((f"fr_angle_{tag}_b", [span(xc, sx, 0.0, t)[0], 0.0, span(zc, sz, t, leg)[0]],
                    [span(xc, sx, 0.0, t)[1], H, span(zc, sz, t, leg)[1]]))
    for face, zc, sz in (("n", 0.0, 1), ("s", D, -1)):
        out.append((f"fr_band_{face}_a", [leg, H - leg, span(zc, sz, 0.0, t)[0]], [W - leg, H, span(zc, sz, 0.0, t)[1]]))
        out.append((f"fr_band_{face}_b", [leg, H - t, span(zc, sz, t, leg)[0]], [W - leg, H, span(zc, sz, t, leg)[1]]))
    return [piece for name, lo, hi in out for piece in split_blocks(name, lo, hi)]


def front_pieces():
    """The cast-iron front, as (name, lo, hi): a plate set in the brick behind the two recesses, its two openings the
    ash mouth's and the fire door's; it stands on the ground and carries the damper's rails and the door's guides."""
    (x0, x1), (y0, y1), (z0, z1) = FRONT
    (mx0, mx1), (my0, my1) = MOUTH
    (fx0, fx1), (fy0, fy1) = FIRE_DOOR
    return [("fr_front_w", [x0, y0, z0], [mx0, y1, z1]), ("fr_front_e", [mx1, y0, z0], [x1, y1, z1]),
            ("fr_front_sill", [mx0, y0, z0], [mx1, my0, z1]), ("fr_front_mid", [mx0, my1, z0], [mx1, fy0, z1]),
            ("fr_front_jw", [mx0, fy0, z0], [fx0, fy1, z1]), ("fr_front_je", [fx1, fy0, z0], [mx1, fy1, z1]),
            ("fr_front_top", [mx0, fy1, z0], [mx1, y1, z1])]


def ties():
    """The frame's ties, as (name, lo, hi): a post under the front end of each outer bearer and under the back end of
    the middle one (the cave's walls), half a voxel thick, hidden in the brickwork, so the grate stands on its own
    before the brick is in."""
    out = []
    for side, n, (z0, z1) in (("w", 0, (BEARER_Z[0], BEARER_Z[0] + 0.5)), ("m", 1, (BEARER_Z[1] - 0.5, BEARER_Z[1])),
                              ("e", 2, (BEARER_Z[0], BEARER_Z[0] + 0.5))):
        x0, x1 = BEARER_X[n]
        out.append((f"fr_tie_bearer_{side}", [x0, 0.0, z0], [x1, BEARER_Y[0], z1]))
    return out


def brick_voids():
    """Every space cut out of the brick, as (lo, hi) in voxels: the ash pit, the grate's room and the bearers' ends,
    the setting's inside (the fire and the flame's way round the kettle) and the seat's opening round the bowl's top
    (MOUTH_RECTS); the ash mouth's and
    the fire door's passages, their recesses and the iron front; the flue opening, the back flue, the stack and the
    cap's seat; the binding and the ties."""
    v = []
    for (x0, x1), (y0, y1), (z0, z1) in (CAVE, GRATE, CAVITY, FLUE_OPENING, BACK_FLUE, STACK, CAP_SEAT, D_RECESS,
                                         F_RECESS, FRONT):
        v.append(([x0, y0, z0], [x1, y1, z1]))
    for hx, hz in MOUTH_RECTS:
        v.append(([KX - hx, SEAT_Y, KZ - hz], [KX + hx, FLOOR_TOP, KZ + hz]))
    for x0, x1 in BEARER_X:
        v.append(([x0, BEARER_Y[0], BEARER_Z[0]], [x1, BEARER_Y[1], BEARER_Z[1]]))
    (mx0, mx1), (my0, my1) = MOUTH
    v.append(([mx0, my0, 0.0], [mx1, my1, CAVE[2][0]]))
    (fx0, fx1), (fy0, fy1) = FIRE_DOOR
    v.append(([fx0, fy0, 0.0], [fx1, fy1, CAVITY[2][0]]))
    v += [(lo, hi) for _, lo, hi in binding()]
    v += [(lo, hi) for _, lo, hi in ties()]
    return v


def lining_zones():
    """Where the brick is the lining's, as (lo, hi): LINING_T round the setting's inside from the grate's room up to
    the floor, and round the flue (the opening, the back flue, the stack and the outlet's curb) from its floor up. The
    rest is the brickwork's: the base, the ash pit and the ash mouth, the outer walls."""
    t = LINING_T
    (cx0, cx1), _, (cz0, cz1) = CAVITY
    (bx0, bx1), (by0, _), (bz0, bz1) = BACK_FLUE
    return [([cx0 - t, GRATE[1][0], cz0 - t], [cx1 + t, FLOOR_TOP, cz1 + t]),
            ([bx0 - t, by0 - t, min(cz1, bz0 - t)], [bx1 + t, FLOOR_TOP, bz1 + t])]


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
    `brickwork` part, `ln_brick_NNN` in the `lining`. A face that lies on its whole area against brick always there
    with it is left out: a brickwork face against brickwork, a lining face against either. A brickwork face against
    the lining is kept: until the lining is in, it is the wall's face."""
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
    """The iron frame (`frame`): what the frame stage puts down, standing on its own. The three bearers and the
    firebars (two lengths a row), the cast-iron front with the damper's rails and the fire door's guides, the
    binding, and the bearers' posts (`ties`, hidden in the brickwork)."""
    f, t = "frame", "iron"
    out = []
    for n, (x0, x1) in enumerate(BEARER_X, 1):
        out.append(box([x0, BEARER_Y[0], BEARER_Z[0]], [x1, BEARER_Y[1], BEARER_Z[1]], f"fr_bearer{n}", f, t))
    (gx0, gx1), _, (gz0, gz1) = GRATE
    gap = (gz1 - gz0 - BARS * BAR_W) / (BARS - 1)
    xm = BEARER_X[1][0] + (BEARER_X[1][1] - BEARER_X[1][0]) / 2
    for n in range(BARS):
        z0 = gz0 + n * (BAR_W + gap)
        for side, (x0, x1) in (("w", (gx0, xm)), ("e", (xm, gx1))):
            out.append(box([x0, BAR_Y[0], z0], [x1, BAR_Y[1], z0 + BAR_W], f"fr_bar{n + 1:02d}{side}", f, t))
    out += [box(lo, hi, name, f, t) for name, lo, hi in front_pieces()]
    (rx0, rx1), _, (rz0, rz1) = D_RECESS
    for tag, (y0, y1) in zip(("b", "t"), D_RAILS):
        out += blocks_of(f"fr_rail_{tag}", [rx0, y0, rz0], [rx1, y1, rz1], f, t)
    (_, _), (fy0, fy1), (fz0, fz1) = F_RECESS
    for tag, (x0, x1) in zip(("w", "e"), F_GUIDES):
        out += blocks_of(f"fr_guide_{tag}", [x0, fy0, fz0], [x1, fy1, fz1], f, t)
    out += [box(lo, hi, name, f, t) for name, lo, hi in binding()]
    out += [box(lo, hi, name, f, t) for name, lo, hi in ties()]
    return out


def build_frame():
    """Everything that stands still and is not a stage's fitting: the brick (brickwork and lining) and the iron
    frame."""
    return build_brick() + build_iron()


def bowl_facet(e0, e1, phi, name):
    """One facet of the bowl's wall: a flat slab KT thick on the chord of the mid-surface sphere (radius RM about the
    rim's centre) between elevations e0 and e1 (radians below the rim) at azimuth phi (radians from +x towards +z),
    as wide as the outer octagon's side at its top, so the eight facets of a band close outside and lap inside."""
    u = [math.cos(phi), 0.0, math.sin(phi)]
    t = [-math.sin(phi), 0.0, math.cos(phi)]
    c = (KX, RIM_Y, KZ)

    def on(e, r):
        return [c[0] + r * math.cos(e) * u[0], c[1] - r * math.sin(e), c[2] + r * math.cos(e) * u[2]]
    p0, p1 = on(e0, RM), on(e1, RM)
    along = unit([p1[k] - p0[k] for k in range(3)])
    mid = [(p0[k] + p1[k]) / 2 for k in range(3)]
    inward = unit([c[k] - mid[k] for k in range(3)])
    top_out = [p0[k] - inward[k] * KT / 2 for k in range(3)]
    reach = (top_out[0] - KX) * u[0] + (top_out[2] - KZ) * u[2]
    width = 2 * reach * OCT
    length = math.dist(p0, p1)
    lap0 = 0.0 if e0 < 1e-9 else BAND_LAP
    lap1 = BAND_LAP
    centre = [mid[k] + along[k] * (lap1 - lap0) / 2 for k in range(3)]
    return obox(centre, (along, inward), (length + lap0 + lap1, KT, width), name, "kettle", "iron")


def build_kettle():
    """The kettle (`kettle`): the cast-iron bowl, a faceted hemisphere (three bands of eight facets and a flat bottom),
    and its flange round the rim, an octagonal ring bedded in the seat's brick, standing FLANGE_PROUD over the floor."""
    p = "kettle"
    out = []
    for b, (e0, e1, _, _) in enumerate(band_planes(), 1):
        for j in range(8):
            out.append(bowl_facet(e0, e1, math.radians(45.0 * j), f"kt_wall{b}{j + 1}"))
    out += oct_disc(KX, KZ, BOTTOM_Y - KT, BOTTOM_Y, BOTTOM_A, "kt_bottom", p, "iron")
    for j in range(8):
        out.append(oct_slab(KX, KZ, FLANGE_Y[0], FLANGE_Y[1], RI, RF, 2 * RF * OCT, 45.0 * j, f"kt_flange{j + 1}", p, "iron"))
    return out


def build_bath(n):
    """The bath at level n of LEVELS (`lead<n>`): the molten lead's surface layer where the bowl holds n quarters of a
    charge, an octagon a hundredth into the wall."""
    p = f"lead{n}"
    y1 = BATH_LEVELS[n - 1]
    return oct_disc(KX, KZ, y1 - BATH_T, y1, inner_apothem(y1) + 0.01, f"{p}_pool", p, "lead")


ZINC_TURNS = (8.0, -12.0, 5.0)


def build_zinc():
    """The zinc (`zinc`): slabs of it floating on the full bath, beyond the paddles' reach, half sunk."""
    out = []
    w, h, r = ZINC_SIZE
    for n, deg in enumerate(ZINC_AT, 1):
        phi = math.radians(deg)
        u = [math.cos(phi), 0.0, math.sin(phi)]
        t = [-math.sin(phi), 0.0, math.cos(phi)]
        c = [KX + ZINC_R * u[0], FULL + 0.05, KZ + ZINC_R * u[2]]
        el = obox(c, (t, [0.0, 1.0, 0.0]), (w, h, r), f"zinc_slab{n}", "zinc", "zinc")
        rotate([el], "x", ZINC_TURNS[n - 1] * 0.2, c)
        out.append(el)
    return out


CRUST_LUMPS = ((4.5, 30.0), (6.5, 100.0), (5.0, 175.0), (7.0, 230.0), (4.0, 290.0), (7.5, 340.0))


def build_crust():
    """The crust (`crust`): the zinc crust risen over the full bath, rich in the silver, with lumps on it."""
    p = "crust"
    y0, y1 = FULL, FULL + CRUST_T
    out = oct_disc(KX, KZ, y0, y1, inner_apothem(y1) + 0.01, f"{p}_cake", p, "crust")
    for n, (r, deg) in enumerate(CRUST_LUMPS, 1):
        phi = math.radians(deg)
        c = [KX + r * math.cos(phi), y1 + 0.15, KZ + r * math.sin(phi)]
        out.append(lump(c, 1.1, (("y", 17.0 * n), ("x", 23.0 - 7.0 * n)), f"{p}_lump{n}", p, "crust"))
    return out


LUMP_PLACES = ((-6.0, -6.0), (6.0, -6.0), (0.0, -2.0), (-6.0, 5.0), (6.0, 5.0), (0.0, 7.0))
LUMP_TURNS = ((("y", 28.0), ("x", 21.0)), (("y", 63.0), ("z", 17.0)), (("y", 11.0), ("x", -24.0)), (("y", 47.0), ("z", -19.0)))


def build_fuel(j, kind):
    """The coal's layer j on the grate (`fuel<j>`), or the same coal burning (`fire<j>`, the ember texture, its faces
    glowing): one bed over the grate with lumps on its top. The burning bottom layer also has cinders fallen through
    the grate onto the ash pit's floor."""
    p = f"{kind}{j}"
    tex = "fuel" if kind == "fuel" else "ember"
    (gx0, gx1), _, (gz0, gz1) = GRATE
    y0, y1 = FUEL_LAYERS[j - 1]
    out = [box([gx0, y0, gz0], [gx1, y1, gz1], f"{p}_bed", p, tex)]
    for n, (dx, dz) in enumerate(LUMP_PLACES):
        out.append(lump([KX + dx, y1 + 0.1, KZ + dz], LUMP, LUMP_TURNS[(n + j) % 4], f"{p}_lump{n + 1}", p, tex))
    if kind == "fire" and j == 1:
        y = CAVE[1][0]
        for n, (dx, dz, w, d, deg) in enumerate(((-5.0, -3.0, 3.0, 2.4, 20.0), (4.0, 2.0, 2.6, 2.0, -35.0),
                                                 (-1.0, 5.0, 2.2, 1.8, 60.0), (6.0, -5.0, 2.4, 1.6, 10.0)), 1):
            out.append(bar([KX + dx, y + 0.3, KZ + dz], (w, 0.6, d), (("y", deg),), f"{p}_cinder{n}", p, tex))
    return out


def build_damper(place):
    """The ash-pit damper (`damper<place>`): an iron plate sliding sideways along its rails over the ash mouth, with a
    knob, in one of its three places. Each place is its own part; gameplay draws the one the damper is in."""
    p = f"damper{place}"
    x0 = DAMPER[place]
    out = [box([x0, DAMPER_Y[0], DAMPER_Z[0]], [x0 + DAMPER_W, DAMPER_Y[1], DAMPER_Z[1]], f"{p}_plate", p, "iron")]
    out.append(box([x0 + 1.0, 2.9, 0.1], [x0 + 2.4, 4.1, DAMPER_Z[0]], f"{p}_knob", p, "iron"))
    return out


def build_firedoor():
    """The fire door (`firedoor`, fitted with the kettle): an iron door lifting in its guides over the fire door's
    opening, with a handle, built shut; depth lifts it open."""
    p = "firedoor"
    (gx0, gx1) = (F_GUIDES[0][1], F_GUIDES[1][0])
    out = [box([gx0, FD_Y[0], FD_Z[0]], [gx1, FD_Y[1], FD_Z[1]], "fd_plate", p, "iron")]
    out.append(box([KX - 2.0, 14.6, 0.1], [KX + 2.0, 15.4, FD_Z[0]], "fd_handle", p, "iron"))
    return out


def build_cap():
    """The outlet's cap (`outletcap`): a flat iron plate set flush in the curb's seat."""
    (x0, x1), _, (z0, z1) = CURB
    r = CURB_RIM
    return [box([x0 + r, CAP_Y[0], z0 + r], [x1 - r, CAP_Y[1], z1 - r], "outletcap_plate", "outletcap", "iron")]


# ---------------------------------------------------------------- the gallows, the drive, the stirrer
TIP, ROOT = 0.5, 0.625                       # addendum and dedendum, in modules (stub teeth, as cast)
BODY_TIERS, BODY_K = 2, 4                    # a gear's body: discs stepping in under the root cone
TOOTH_SHARE = 0.30                           # a tooth's share of the circular pitch at mid face: cast teeth, backlash
BODY_GAP = 0.1                               # how far a body tier stays under the root cone
TOOTH_ROOT = 0.2                             # each tooth runs on this far under the root cone, into the body: cast as one

DELTA_P = math.atan2(PINION_TEETH, WHEEL_TEETH)   # the pinion's pitch cone half angle (shaft angle 90 degrees)
DELTA_W = math.pi / 2 - DELTA_P
R_P = GEAR_MOD * PINION_TEETH / 2
R_W = GEAR_MOD * WHEEL_TEETH / 2


def bevel_cone(n, delta):
    """(outer cone distance, the root cone's radius and its distance from the apex along the axis, as functions of
    the distance along the pitch cone)."""
    a_o = GEAR_MOD * n / 2 / math.sin(delta)
    d = ROOT * GEAR_MOD

    def rad(a):
        return a * math.sin(delta) - d * math.cos(delta)

    def ax(a):
        return a * math.cos(delta) + d * math.sin(delta)
    return a_o, rad, ax


def disc(axis, c, a0, a1, r, name, part, tex, k=4, phase=0.0):
    """A plain round part about `axis` through c: k strips as long as the 2k-gon is across, 180/k degrees apart,
    each a hair shorter than the last so no two ends share a plane (the parting furnace's v2 helper)."""
    a = {"x": 0, "y": 1, "z": 2}[axis]
    u, w = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}[axis]
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


def bevel(name, part, tex, toward, ref, delta, n, phase):
    """A bevel gear on the cone with its apex at APEX (the parting furnace's v2 `bevel`): `toward` the unit vector
    from the gear along its axis to the apex, `ref` the (angle 0, angle 90) radial directions of its rotation axis,
    `delta` the pitch cone's half angle, n teeth of module GEAR_MOD, tooth 1 at `phase`. Each tooth stands from
    TOOTH_ROOT under the root cone (into the body, so it is joined to it) to its tip; the body is BODY_TIERS discs
    stepping in under the root cone. Returns (elements, the body's back face's distance from the apex along the
    axis)."""
    a_o, rad, ax = bevel_cone(n, delta)
    a_mid = a_o - GEAR_FACE / 2
    e1, e2 = ref
    out = []
    pitch = TAU / n
    depth = (TIP + ROOT) * GEAR_MOD + TOOTH_ROOT
    for i in range(n):
        ang = phase + pitch * i
        u = [math.cos(ang) * e1[k] + math.sin(ang) * e2[k] for k in range(3)]
        g = [-toward[k] * math.cos(delta) + u[k] * math.sin(delta) for k in range(3)]
        nrm = [toward[k] * math.sin(delta) + u[k] * math.cos(delta) for k in range(3)]
        c = [APEX[k] + g[k] * a_mid + nrm[k] * ((TIP - ROOT) / 2 * GEAR_MOD - TOOTH_ROOT / 2) for k in range(3)]
        thick = math.pi * GEAR_MOD * TOOTH_SHARE * a_mid / a_o
        out.append(obox(c, (g, nrm), (GEAR_FACE, depth, thick), f"{name}_tooth{i + 1:02d}", part, tex))
    k_ax = max(range(3), key=lambda k: abs(toward[k]))
    axis = "xyz"[k_ax]
    sign = toward[k_ax]
    corner = 1 / math.cos(math.pi / (2 * BODY_K))
    r_out, r_in = rad(a_o), rad(a_o - GEAR_FACE)
    back = ax(a_o) + 0.8
    prev = back
    for j in range(BODY_TIERS):
        rho = r_out - (r_out - r_in) * j / max(1, BODY_TIERS - 1)
        a_at = (rho * corner + ROOT * GEAR_MOD * math.cos(delta)) / math.sin(delta)
        top = ax(a_at) + BODY_GAP
        lo, hi = sorted((APEX[k_ax] - sign * prev, APEX[k_ax] - sign * top))
        out += disc(axis, APEX, lo, hi, rho, f"{name}_body{j + 1}", part, tex, k=BODY_K, phase=math.pi / (2 * BODY_K))
        prev = top
    return out, back


PINION_BACK = bevel_cone(PINION_TEETH, DELTA_P)[2](bevel_cone(PINION_TEETH, DELTA_P)[0]) + 0.8
WHEEL_BACK = bevel_cone(WHEEL_TEETH, DELTA_W)[2](bevel_cone(WHEEL_TEETH, DELTA_W)[0]) + 0.8


def build_gallows():
    """The stirrer's gallows (`gallows`, fitted with the stirrer): two cast-iron plate standards on feet on the floor,
    east and west of the kettle, joined by three beams over it, each with a bearing boss: the lower and the upper
    beams carry the stirrer's shaft, the middle one the drive shaft's front end; and the pedestal bearing on the back
    row carrying the drive shaft's back end."""
    p, t = "gallows", "iron"
    out = []
    (sz0, sz1) = STANDARD_Z
    for side, (x0, x1) in zip(("w", "e"), STANDARD_X):
        out.append(box([x0 - 1.5, FOOT_Y[0], sz0 - 0.5], [x1 + 1.5, FOOT_Y[1], sz1 + 0.5], f"gl_foot_{side}", p, t))
        out.append(box([x0, FOOT_Y[1], sz0], [x1, GAL_TOP, sz1], f"gl_standard_{side}", p, t))
    xa, xb = STANDARD_X[0][1], STANDARD_X[1][0]
    for name, (y0, y1), (z0, z1) in (("lower", *LOWER), ("middle", *MIDDLE), ("upper", *UPPER)):
        out += blocks_of(f"gl_{name}", [xa, y0, z0], [xb, y1, z1], p, t)
    for name, (y0, y1), (z0, z1), half in (("lower", *BOSS_LOW, 2.0), ("upper", *BOSS_UP, 2.0)):
        out.append(box([KX - half, y0, z0], [KX + half, y1, z1], f"gl_boss_{name}", p, t))
    (my0, my1), (mz0, mz1) = BOSS_MID
    out.append(box([KX - 2.4, my0, mz0], [KX + 2.4, my1, mz1], "gl_boss_middle", p, t))
    # the pedestal bearing on the back row: a base plate on the floor, a body, the bearing round the shaft
    pz0, pz1 = PEDESTAL_Z
    out.append(box([KX - 4.0, FLOOR_TOP, pz0 - 1.0], [KX + 4.0, FLOOR_TOP + 0.8, pz1 + 1.0], "gl_ped_base", p, t))
    out.append(box([KX - 2.0, FLOOR_TOP + 0.8, pz0 + 0.3], [KX + 2.0, AXLE[1] - 2.4, pz1 - 0.3], "gl_ped_body", p, t))
    out.append(box([KX - 2.4, AXLE[1] - 2.4, pz0], [KX + 2.4, AXLE[1] + 2.4, pz1], "gl_ped_bearing", p, t))
    return out


def build_drive():
    """The drive (`drive`, fitted with the stirrer): the axle's oak continuation at the power face (the vanilla axle's
    cross profile), the steel shaft in the pedestal's and the middle beam's bearings, collars either side of the
    pedestal, and the bevel pinion keyed to its front end, its tooth 1 down into the wheel's gap at theta 0."""
    p = "drive"
    x, y = AXLE
    out = [box([x - 1.5, y - 0.6, OAK_Z[0]], [x + 1.5, y + 0.6, OAK_Z[1]], "dr_oaka", p, "oak"),
           box([x - 0.6, y - 1.5, OAK_Z[0]], [x + 0.6, y + 1.5, OAK_Z[1]], "dr_oakb", p, "oak")]
    c = (x, y, 0.0)
    front = KZ + PINION_BACK - 0.4
    out += disc("z", c, front, OAK_Z[0], SHAFT_R, "dr_shaft", p, "steel")
    pz0, pz1 = PEDESTAL_Z
    out += disc("z", c, pz0 - 0.7, pz0 - 0.05, 1.6, "dr_collar_n", p, "steel")
    out += disc("z", c, pz1 + 0.05, pz1 + 0.7, 1.6, "dr_collar_s", p, "steel")
    e1, e2 = [1.0, 0.0, 0.0], [0.0, 1.0, 0.0]
    pinion, back = bevel("dr_pinion", p, "steel", [0.0, 0.0, -1.0], (e1, e2), DELTA_P, PINION_TEETH, -math.pi / 2)
    out += pinion
    out += disc("z", c, KZ + back - 0.3, BOSS_MID[1][0] - 0.1, 1.7, "dr_hub", p, "steel")
    return out


def build_stirrer():
    """The stirrer (`stirrer`): its vertical steel shaft in the gallows' lower and upper bosses, hung from the upper
    by a collar; the mitre wheel on it under the apex, a gap towards the pinion (south) at theta 0; and at its foot
    two flat iron paddles crossing in the bath, in two tiers that follow the bowl, symmetric about the shaft (they stir
    alike either way round)."""
    p = "stirrer"
    c = (KX, 0.0, KZ)
    out = disc("y", c, STIR_Y[0], STIR_Y[1] - 0.1, STIR_R, "st_shaft", p, "steel")
    out += disc("y", c, BOSS_UP[0][1], STIR_Y[1], 1.4, "st_collar", p, "steel")
    out += disc("y", c, BOSS_LOW[0][1] + 0.05, AXLE[1] - WHEEL_BACK + 0.3, 1.5, "st_hub", p, "steel")
    e1, e2 = [0.0, 0.0, 1.0], [1.0, 0.0, 0.0]
    wheel, _ = bevel("st_wheel", p, "steel", [0.0, 1.0, 0.0], (e1, e2), DELTA_W, WHEEL_TEETH, math.pi / WHEEL_TEETH)
    out += wheel
    for tier, ((y0, y1), reach) in enumerate(PADDLES, 1):
        for i, deg in enumerate((45.0, 135.0), 1):
            el = box([KX - reach, y0, KZ - PADDLE_T / 2], [KX + reach, y1, KZ + PADDLE_T / 2], f"st_paddle{tier}{i}", p, "iron")
            rotate([el], "y", deg, (KX, 0.0, KZ))
            out.append(el)
    return out


def build():
    els = build_kettle() + build_firedoor()
    for n in range(1, LEVELS + 1):
        els += build_bath(n)
    els += build_zinc() + build_crust()
    for kind in ("fuel", "fire"):
        for j in range(1, len(FUEL_LAYERS) + 1):
            els += build_fuel(j, kind)
    for place in DAMPERS:
        els += build_damper(place)
    return els + build_cap() + build_gallows() + build_drive() + build_stirrer() + build_frame()


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def drive_drivers():
    return [{"type": "rotate", "axis": "z", "pivot": pt(AXLE[0], AXLE[1], 0.0), "ratio": 1.0}]


def stirrer_drivers():
    return [{"type": "rotate", "axis": "y", "pivot": pt(KX, 0.0, KZ), "ratio": r6(STIR_RATIO)}]


def firedoor_drivers():
    """Lifted open as depth goes 0 -> 1: shut at rest."""
    return [{"type": "step", "motion": "slide", "axis": "y", "from": 0.0, "to": 1.0, "amount": r6(FD_LIFT / B)}]


PART_IDS = (("brickwork", "lining", "kettle", "firedoor") + CONTENTS + tuple(f"damper{d}" for d in DAMPERS)
            + ("outletcap", "gallows", "drive", "stirrer"))
MATCH = {"brickwork": "bk_*", "lining": "ln_*", "kettle": "kt_*", "firedoor": "fd_*", "gallows": "gl_*", "drive": "dr_*",
         "stirrer": "st_*"}
REQUIRES_OF = {"firedoor": "kettle", "gallows": "stirrer", "drive": "stirrer"}   # parts fitted with a stage's requires
DRIVERS = {"drive": drive_drivers, "stirrer": stirrer_drivers, "firedoor": firedoor_drivers}
MOVING = ("firedoor", "drive", "stirrer")


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
                      "drivers": DRIVERS[pid]() if pid in DRIVERS else []})
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []})
    for p in parts:
        p["ride"] = None
        for d in p["drivers"]:
            validate_driver(d)
    return [{"id": p["id"], "match": p["match"], "requires": p["requires"], "drivers": p["drivers"], "ride": p["ride"]}
            for p in parts]


# ---------------------------------------------------------------- poses
# A pose is (theta, depth, scene): the axle, the fire door, and what is fitted. A scene is (the kettle's state, the
# fire's, the damper's place, whether the outlet is capped, the build's state): gameplay's requires. The kettle's
# contents, the fire and the damper are fitted only once the kettle is in (the kettle stage), the cap once the curb
# is (the lining).
DEFAULT_SCENE = ("zinc", "lit", "open", False, "complete")
REST = (0.0, 0.0, DEFAULT_SCENE)


def inputs_of(pose):
    return {"theta": pose[0], "depth": pose[1]}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


def fitted(scene):
    """The requires values fitted in a scene."""
    kettle, fire, damper, capped, stage = scene
    out = set(dict(STAGE_STATES)[stage])
    if "kettle" in out:
        out |= set(dict((s[0], s[2]) for s in KETTLE_STATES)[kettle])
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
    """The setting's eighteen cells, then the four over it that the gallows and the drive stand in, then the three
    hollow ones over the kettle's mouth."""
    return ([(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)] + list(UPPER_CELLS)
            + list(HOLLOW_CELLS))


def regions():
    """Where the model may be (voxels, (lo, hi)): the setting's box, and each cell over it that is the machine's."""
    return [((0.0, 0.0, 0.0), (W, H, D))] + [((c[0] * B, c[1] * B, c[2] * B), ((c[0] + 1) * B, (c[1] + 1) * B, (c[2] + 1) * B))
                                              for c in UPPER_CELLS + HOLLOW_CELLS]


ANCHORS = ("mouth", "fireDoor", "ash")


def mouth_point():
    """The middle of the kettle's mouth at the floor: where lead and zinc go in and the crust and lead come out, and
    the bath's sound and fumes."""
    return (KX, FLOOR_TOP, KZ)


def fire_door_point():
    """The middle of the fire door in the front face: fuel goes in there, by hand."""
    (fx0, fx1), (fy0, fy1) = FIRE_DOOR
    return ((fx0 + fx1) / 2, (fy0 + fy1) / 2, 0.0)


def ash_point():
    """The middle of the ash mouth in the front face: the damper, worked from in front."""
    (mx0, mx1), (my0, my1) = MOUTH
    return ((mx0 + mx1) / 2, (my0 + my1) / 2, 0.0)


def outlet_cell():
    return tuple(int(v // B) for v in ((STACK[0][0] + STACK[0][1]) / 2, FLOOR_TOP - 1.0, (STACK[2][0] + STACK[2][1]) / 2))


def kettle_cell():
    return (int(KX // B), CELLS_Y - 1, int(KZ // B))


def box_volume(el):
    return abs(el.size[0] * el.size[1] * el.size[2])


def build_volumes(els):
    """The brickwork's and the lining's volumes, in blocks, measured from their elements (the brick boxes: each
    lattice box in exactly one)."""
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
             "materials": materials(((FIRED_BRICK, c["bricks"]),))},
            {"requires": "lining", "volumeBlocks": r6(c["liningVolume"]), "perClick": "stack",
             "materials": materials(((LINING_BRICK, c["lining"]), (FIRECLAY, c["fireclay"])))},
            {"requires": "kettle", "alsoParts": ["firedoor"], "also": [f"damper{d}" for d in DAMPERS],
             "materials": materials(KETTLE_STAGE)},
            {"requires": "stirrer", "alsoParts": ["gallows", "drive"], "materials": materials(STIRRER_STAGE)},
        ],
        "outletCap": materials((CAP_ITEM,))[0],
        "_comment": "The build's stages, in order (provisional: the owner settles them, #928). frame: the iron frame's "
                    "grid recipe (with a hammer); placing it puts down the controller and its ghosts, and draws the "
                    "frame part. Each stage then fits its requires when its materials are all in: brickwork, then "
                    "lining, measured from the model (volumeBlocks) at 2 fired bricks or 4 refractory bricks a block, "
                    "rounded up, fire clay one per 2.5 refractory bricks; perClick stack: a click puts in as many of "
                    "the held stack as are still needed; oneType: the first fired brick sets the setting's type "
                    "(bricks). kettle: the cast-iron kettle set in its seat, and with it (alsoParts) the fire door and "
                    "(also) the damper's places. stirrer: the stirrer, and with it the gallows and the drive. The "
                    "kettle's contents, the fire and the damper are drawn once the kettle stage is in, the cap once "
                    "the lining is."}


def bricks_json():
    return {"code": "brick", "item": "game:burnedbrick-{type}", "default": BRICK_DEFAULT,
            "types": {t: {"block": block, "base": base, **({"overlays": [over]} if over else {})}
                      for t, block, base, over in BRICKS},
            "_comment": "The fired brick types the brickwork takes (every game:burnedbrick type), each with the block "
                        "its bricks make and that block's texture: the renderer sets the shape's 'brick' code to the "
                        "setting's type's base, with its overlays drawn over it. The setting's type is the first brick "
                        "put in; until then no brick is drawn. A type missing here (another mod's) is refused."}


def make_rig(parts):
    sx, sz = (STACK[0][0] + STACK[0][1]) / 2, (STACK[2][0] + STACK[2][1]) / 2
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the front middle "
                    "cell at ground level, the fire's front. The kettle, centred left to right in the front two rows, "
                    "hangs by its flange flush with the floor, worked from its top (kettleCell, mouth: lead and zinc "
                    "in, the crust skimmed and the lead ladled out, by hand); kettles stand side by side in a row "
                    "along x (up to furnace.rowMax), sharing one chimney on one member's outlet, the others capped. "
                    "Natural draught, in at the ash mouth (ash, ashSide) under the damper; fuel by hand at the fire "
                    "door (fireDoor, fireDoorSide). The axle comes in at powerCell's south face, at the gallows' "
                    "height: theta turns the drive shaft and its pinion about z (ratio 1) and the stirrer about y "
                    "(ratio 1) through a mitre pair; flat paddles stir alike either way, so nothing reads the shaft's "
                    "travel. depth is the fire door: 0 shut (the rest pose), 1 open, lifted in its guides. requires: "
                    "the build's stages, brickwork, lining, kettle (with the fire door and the damper) and stirrer "
                    "(with the gallows and the drive) (build; the frame, iron, has none); the bath's four levels, "
                    "lead1..lead4 (a quarter of a charge each, by volume); zinc, crust; the coal's two layers "
                    "fuel1..2 and the same burning, fire1..2; the damper's place, damperopen, damperhalf or "
                    "dampershut (one at a time); and outletcap. outletCell: the stack in the back row's east corner, "
                    "the chimney stood on its top; chimney: the shortest chimney, counted from the outlet up. See "
                    "ParkesKettle/README.md.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "kettleCell": list(kettle_cell()),
        "kettleFace": "up",
        "mouth": {"pos": pt(*mouth_point())},
        "fireDoor": {"pos": pt(*fire_door_point())},
        "fireDoorSide": "north",
        "ash": {"pos": pt(*ash_point())},
        "ashSide": "north",
        "outletCell": list(outlet_cell()),
        "outletFace": "up",
        "chimney": {"origin": pt(sx, FLOOR_TOP + MIN_CHIMNEY * B / 2, sz), "axis": "y", "length": float(MIN_CHIMNEY)},
        "build": build_json(),
        "bricks": bricks_json(),
        "furnace": {"chargeUnits": CHARGE_UNITS, "leadLevels": LEVELS, "fuelLayers": len(FUEL_LAYERS),
                    "fuelPerLayer": FUEL_PER_LAYER, "minChimney": MIN_CHIMNEY, "rowMax": ROW_MAX, "rowAxis": "x",
                    "stirRatio": r6(STIR_RATIO), "fireDoorLift": r6(FD_LIFT / B),
                    "_comment": "chargeUnits: what the kettle drawn full holds (#927's charge); leadLevels: the bath "
                                "is drawn as lead1.. up to ceil(fill x leadLevels), each a quarter of a charge by "
                                "volume; fuelLayers, fuelPerLayer: the grate's coal is drawn as its layers fuel1.. up "
                                "to ceil(coal / fuelPerLayer) (fire.. while lit; provisional); minChimney: chimney "
                                "blocks stood on the outlet, counted from it up; rowMax, rowAxis: kettles in a row, "
                                "side by side along x, sharing one chimney; stirRatio: the stirrer's turns a turn of "
                                "the axle (the mitre pair); fireDoorLift: how far the fire door lifts open (blocks)."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
CELL_KEYS = ("powerCell", "kettleCell", "outletCell")


def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0]."""
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
    for key in CELL_KEYS:
        ship[key] = shift_cell(rig[key], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["chimney"] = {**rig["chimney"], "origin": shift_point(rig["chimney"]["origin"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


CELL_PARTS = ("frame", "brickwork", "lining", "kettle", "firedoor", "gallows", "drive", "stirrer")
CELL_POSE = (0.0, 0.0, DEFAULT_SCENE)


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed by the shipped rig at rest with the machine built
    (the frame, the brick, the kettle, the fire door shut, the gallows, the drive and the stirrer; not the contents);
    a deck (`lid`) on every top cell of the setting, at the floor, so no one falls into the kettle; the cells over the
    kettle's mouth hollow."""
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
    hollow = {tuple(c[k] - ORIGIN_CELL[k] for k in range(3)) for c in HOLLOW_CELLS}
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        if pos in hollow:
            out.append({"pos": list(pos), "hollow": True})
            continue
        cell = {"pos": list(pos), "boxes": cell_boxes(by_cell[pos], pos)}
        if c[1] == CELLS_Y - 1:
            cell["lid"] = 1.0
        out.append(cell)
    return out


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    poses = [REST, (0.7, 0.3, DEFAULT_SCENE), (-2.2, 0.7, DEFAULT_SCENE), (4.0, 1.0, DEFAULT_SCENE), (9.3, 0.5, DEFAULT_SCENE)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; {len(cells)} cells; "
          f"power {ship['powerCell']} {ship['powerFace']}, outlet {ship['outletCell']}, kettle {ship['kettleCell']}")
    want = {tuple(shift_cell(c, ORIGIN_CELL)) for c in (POWER_CELL, kettle_cell(), outlet_cell())}
    if worst > 1e-6 or (0, 0, 0) not in cells or not want <= set(cells):
        print("FAIL: the shipped model is not the checked one moved, or an anchor cell is not the machine's")
        return False
    return True


# ---------------------------------------------------------------- reference poses
REF_POSES = ((0.0, 0.0), (0.3, 0.05), (1.1, 0.25), (-2.3, 0.4), (2.9, 0.5), (4.0, 0.75), (-5.5, 0.9), (7.5, 1.0),
             (-9.1, 0.6), (12.0, 0.1), (0.7853981634, 0.0), (-0.5235987756, 1.0))


def reference_json(ship_parts):
    poses = []
    for th, dp in REF_POSES:
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], {"theta": th, "depth": dp})) for q in ship_parts}
        poses.append({"theta": th, "depth": dp, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped parkeskettle-rig.json's parts: each part's matrix as 3 "
                        "rows of 4 (block units) at each pose (theta, the axle: the drive and the stirrer turn; depth, "
                        "the fire door: 0 shut, 1 open). The site's and the generator's tests check their rig maths "
                        "against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    shape = machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Texture codes the renderer "
             "sets: 'brick' (the brickwork: the setting's brick type's block texture, from the rig's bricks; the "
             "shape's own is the red brick's colour layer). The burning coal ('ember') glows; the renderer may set the "
             "bath's glow by its heat. Keep element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)
    for e in shape["elements"]:
        if e["name"].split("_")[0].startswith("fire"):
            for f in e["faces"].values():
                f["glow"] = FIRE_GLOW
    return shape


def coplanar_poses():
    scenes = [(k, "lit", "open", False, "complete") for k in ("empty", "quarter", "half", "threequarters", "full", "zinc", "crust")]
    scenes += [("empty", "laid", "half", True, "complete"), ("empty", "low", "shut", True, "complete")]
    scenes += [("empty", "cold", "open", False, s) for s in ("frame", "brickwork")]
    scenes += [("empty", "cold", "open", True, s) for s in ("lining", "kettle")]
    return tuple((th, dp, sc) for sc in scenes for th, dp in ((0.0, 0.0), (0.6, 0.5), (1.9, 1.0)))


def fix_coplanar(els, parts):
    def posed_fn(es, pose):
        return shown(parts, [posed(el, pm(parts, el.part, pose)) for el in es], pose)
    return fix_coplanar_posed(els, posed_fn, coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the Parkes kettle's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_parkeskettle
    els = build()
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        print(f"coplanar faces before the fix: {sum(len(p) for p in before.values())} pairs over {len(before)} poses")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_parkeskettle.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "parkeskettle.json", args.out / "parkeskettle_frame.json", args.out / "parkeskettle-rig.json",
                args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "parkeskettle.json", SHAPE_DIR / "parkeskettle_frame.json", RIG_DIR / "parkeskettle-rig.json",
                REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_parkeskettle.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
