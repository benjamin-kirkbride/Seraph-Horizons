#!/usr/bin/env python3
"""Generate the cupellation furnace's shapes and rig.

The cupellation furnace is an English cupellation (test) furnace of the nineteenth century [Percy 1870; Hofman
1904, ch. XI], built whole as one machine: brickwork three blocks wide (x), two high and two deep in its front two
rows, with the stack in a column behind their west end, bound with iron on its front and back faces. In the front two
rows, side by side, the test and the fireplace: the test, an oval of bone ash in an iron ring, stands on the hearth of
a low chamber under a stepped vault; east of it, behind a fire bridge, the fireplace burns coal on a grate of
firebars over an ash pit, which the air enters at an ash mouth under an iron damper (open, half, shut). The flame
crosses the bridge and the test and leaves at the chamber's west end, by a flue opening in the back wall, along a flue
into the stack column and up its stack to an outlet on the top, where the player stands the chimney (or a flat iron
cap, every furnace but one in a row). Through the back wall a tuyere blows across the lead on the test towards the
front, where the litharge runs out at a gate in the test's breast, along an iron gutter through the working door's
sill, and falls into an iron pot standing in a bay of the front face under the door.

The blast comes by pipe (the owner's ruling on #740): Steelmaking Expanded's engine air blower, a sub-machine of
Pipes and Power Expanded's steam engine, puts air into the pipe network at its output face, and ppex pipes carry it to
the furnace's blast inlet, a socket at the middle of the back wall's face (blastCell, blastFace), as smex's own blast
furnace takes its air from the network at its tuyere blocks. Behind the socket an iron wind box in the back wall feeds
the tuyere. The furnace takes no mechanical power. Everything is built here from plain boxes; no other mod's model is
used.

It writes, deterministically,

    cupellationfurnace.json          the whole furnace, every part that comes and goes   (assets/.../shapes/block/)
    cupellationfurnace_frame.json    the iron frame only (block and item)                 (assets/.../shapes/block/)
    cupellationfurnace-rig.json      cells, anchors and the part rig                      (assets/.../config/)

or, with `--out DIR`, all three into DIR. It validates its own output (validate_cupellationfurnace.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the machine
box's north-west-bottom corner. The working face is the front (north); the stack, the outlet and the blast inlet face
the back (south). The controller cell is the front middle cell at ground level, build cell (1, 0, 0): the shipped
files move it to [0,0,0].

Nothing moves: the rig's parts have no drivers. Everything that shows or not (the build's stages, the test's
contents, the fire, the litharge, the damper's places, the outlet's cap) is a `requires`: gameplay's state, not a
motion. The iron frame has none.
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
from machinegen.output import (rig_dumps, shape_dumps, shift_cell,  # noqa: E402
                               shift_point, worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
SCRIPT = "mods-src/seraphhorizons/CupellationFurnace/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
OCT = math.tan(math.pi / 8)                  # an octagon's half side over its apothem
C22, S22 = math.cos(math.pi / 8), math.sin(math.pi / 8)

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 3, 2, 3          # three wide (x, the row's way), two high, three deep (z)
ORIGIN_CELL = (1, 0, 0)                      # the controller: the front middle cell at ground level (the litharge bay's)
BLAST_CELL, BLAST_FACE = (1, 1, 1), "south"  # the blast inlet: the back wall's face of the middle top cell, where a
#                                              ppex pipe carrying smex's air blower's air meets the wind box's socket
GAP = ((B, 3 * B), (0.0, 2 * B), (2 * B, 3 * B))   # the back row's middle and east columns: not the furnace's (the
#                                                  blast's pipe comes in there, behind the back wall)
CHIMNEY_CELL = (0, 2, 2)                     # over the outlet: the chimney's, not the machine's
ROW_MAX = 5                                  # furnaces in a row, side by side along x, sharing one chimney
BLAST_RATE = 10.0                            # the blast the furnace draws at the socket, L/s of "Air", and the
BLAST_PRESSURE = 1.5                         # least pressure (atm) it runs at: the owner's ruling on #740, as smex's
#                                              Bessemer converter's intake port

TEXTURES = {
    "brick": "game:block/clay/brick/four/running/red1",     # the common brickwork: the red brick's colour (BRICKS)
    "lining": "game:block/clay/refractory/tier3/front1",    # the hot-face lining, the vault, the fire bridge
    "iron": "game:block/metal/plate/iron",                  # the binding, the grate, the castings, the wind box
    "boneash": "game:item/resource/crushed/lime",           # the test's bone ash: the fired cupel's texture
    "lead": "game:block/metal/ingot/lead",                  # the molten lead on the test
    "silver": "game:block/metal/ingot/silver",              # the silver cake, brightened
    "litharge": "game:item/resource/crushed/limonite",      # litharge (the litharge item's base texture)
    "coal": "game:block/coal/bituminous",                   # the fuel on the grate
    "ember": "game:block/coal/ember",                       # the same coal, burning
    "copper": "game:block/metal/sheet-plain/copper4",       # the blast stub: UnifiedPipes' copper pipe's
    "solder": "game:block/metal/ingot/leadsolder",           # its soldered joint (solderedpipe.SOLDER_TEXTURE)
}
TEX = 16                                     # shape texture units: one a voxel, one texture across a block face
FIRE_GLOW = 200                              # the burning coal's faces (the game's lit firepit embers are 160)
LEAD_GLOW = 200                              # the molten lead at cupelling heat
SILVER_GLOW = 255                            # the silver cake as it brightens
STREAM_GLOW = 160                            # the litharge running molten off the test

W, H, D = CELLS_X * B, CELLS_Y * B, CELLS_Z * B
TOP = H                                      # the furnace's top: flat, the brickwork's

# ---------------------------------------------------------------- the furnace body (the front two rows)
BODY_D = 2 * B                               # the front two rows: the test and the fireplace side by side
CH_X = (8.0, 30.5)                           # the chamber over the test: 8 of brick west of it
CH_Z = (8.0, 24.0)                           # 16 deep: 8 of brick in front (the front wall) and behind (the back wall)
BRIDGE_X = (30.5, 33.5)                      # the fire bridge, between the chamber and the fireplace
BRIDGE_TOP = 17.0                            # 2.8 over the test's rim: the fuel stays off the charge
FIRE_X = (33.5, 40.0)                        # the fireplace: 6.5 wide, 16 deep; 8 of brick east of it
HEARTH_Y = 10.5                              # the chamber's floor: the test stands on it
SPRING_Y = 19.0                              # the vault springs off the front and back walls here
VAULT = ((1.5, 19.0, 21.0), (3.5, 21.0, 22.5), (6.0, 22.5, 23.5))   # the stepped vault: (inset from CH_Z, y0, y1)
CROWN = VAULT[-1][2]
VAULT_X = (CH_X[0], FIRE_X[1])               # one vault over the chamber, the bridge and the fireplace
MIN_WALL = 8.0                               # the least brick round the chamber and the fireplace, any side

# the fireplace: the ash pit (the cave), the ash mouth and its damper, the grate, the fire door
CAVE = (FIRE_X, (1.0, 7.0), CH_Z)
MOUTH = ((34.5, 39.0), (1.0, 5.0))           # the ash mouth through the front wall under the fireplace: the air's way in
LINTEL = ((34.0, 39.5), (5.0, 6.0), (1.0, 2.0))   # the iron lintel over it, behind the damper's recess
RECESS = ((33.5, 40.0), (1.0, 10.6), (0.0, 1.0))  # the damper's recess in the front face, the damper's slide
GUIDES = ((33.5, 34.5), (39.0, 40.0))        # iron guides at the recess's sides (x), its full height and depth
DAMPER_Z = (0.25, 0.75)                      # the damper plate, in the recess, behind the face
DAMPER_H = 4.5                               # its height: shut, it covers the mouth and overlaps its top
DAMPER = {"open": 5.0, "half": 3.0, "shut": 1.0}   # its underside (y) in each place: open clear of the mouth (to 9.5,
#                                                    under the recess's top), half over its upper half, shut on the sill
KNOB = ((36.0, 37.5), (0.05, 0.25), (2.0, 3.0))    # the damper's iron knob (x, z, y over its underside)
BEARER_Y = (7.0, 8.5)                        # the grate's two iron bearers, along x, their ends built into the fire
BEARER_X = (31.5, 41.5)                      # bridge and the east wall; the firebars lie on them front to back
BEARER_Z = ((8.5, 9.7), (22.3, 23.5))
BAR_Y = (8.5, 10.0)
BARS, BAR_W = 4, 1.0                         # four bars 1 wide across the 6.5: gaps of 0.83
GRATE_TOP = BAR_Y[1]
FIREDOOR = ((34.5, 39.0), (11.5, 16.5))      # the fire door's opening through the front wall, over the grate
FIREDOOR_FRAME = ((33.8, 39.7), (10.6, 17.0), (0.0, 0.5))   # its cast frame, flush in the face, on the damper's guides
DOOR_PLATE = ((34.3, 39.2), (11.3, 16.7), (0.5, 1.0))       # the door, shut, in a rebate behind the frame

# the working door, the litharge bay and its pot (centred on the test)
X_T = 19.25                                  # the test's centre (x): the door, the gutter, the pot and the tuyere
BAY = ((13.25, 25.25), (1.0, 19.0), (0.0, 3.0))     # a bay in the front face, the working door at its back
DOOR = ((13.25, 25.25), (14.2, SPRING_Y), (3.0, CH_Z[0]))   # the working door, its sill level with the test's rim
NICHE = ((14.25, 24.25), (1.0, 11.0), (3.0, 6.5))   # the pot's niche, under the door at the bay's back
JAMBS = ((12.75, 13.25), (25.25, 25.75))     # the bay's and the door's iron jambs (x), y 0..19, set in the brick
JAMB_Z = (0.0, 5.5)
BAY_LINTELS = (("front", (0.5, 2.0)), ("back", (4.0, 5.5)))   # iron lintels over the bay and the door (z), on the jambs
BAY_LINTEL_Y = (SPRING_Y, 20.0)
NICHE_LINTEL = ((13.25, 25.25), (11.0, 12.0), (3.5, 5.0))   # over the niche, under the door's sill, between the jambs

# the test: an oval of bone ash in an iron ring, standing on the hearth, the breast and its gate at the front
TEST_C = (X_T, (CH_Z[0] + CH_Z[1]) / 2)      # (x, z)
TEST_A = (9.0, 6.4)                          # the ring's inside, the bone ash's oval: half axes (x, z)
RING_T, RING_EXT = 0.9, 0.4                  # the ring's thickness (outward) and how far each side runs on at its ends
RING_TOP = 14.2
ASH_TOP = 13.0                               # the bone ash's floor: the dish the lead lies in
BAND = (1.2, 14.0)                           # the bone ash's rim inside the ring: inward thickness, top
BREAST_IN = 1.6                              # the breast (the front side, no ring): inward from the front chord
GATE = 0.9                                   # the gate in the breast: half width (x), from the floor up
FRONT_K = 6                                  # the ring's side facing the front (t 270): left out, the breast is there
POOL = (7.5, 4.5, 13.7)                      # the lead pool: half axes (x, z), top
CAKE = (4.5, 3.0, 13.6)                      # the silver cake


def ell(t_deg, a, b, c=TEST_C):
    """A point on the oval of half axes (a, b) about c at angle t (0 east, 90 south: x = a cos t, z = b sin t)."""
    t = math.radians(t_deg)
    return (c[0] + a * math.cos(t), c[1] + b * math.sin(t))


FRONT_CHORD_Z = TEST_C[1] - C22 * TEST_A[1]  # the front side's chord (z), where the breast stands
BREAST_Z = (FRONT_CHORD_Z - RING_T, FRONT_CHORD_Z + BREAST_IN)
BREAST_X = (TEST_C[0] - S22 * TEST_A[0], TEST_C[0] + S22 * TEST_A[0])

# the litharge's way: the gutter from the gate through the door's sill, off its end into the pot
GUTTER_Z = (1.6, BREAST_Z[0])                # from over the pot to the breast's front face
GUTTER_Y = (12.6, ASH_TOP)                   # its bottom's top surface at the outer end and at the gate
GUTTER_HALF, GUTTER_WALL, GUTTER_H = 0.9, 0.3, 0.8
GROOVE = ((X_T - 1.25, X_T + 1.25), (12.42, 14.2), (BAY[2][1], CH_Z[0]))   # its groove in the door's sill
POT_C = (X_T, 2.7)                           # the litharge pot, in the bay and its niche
POT_A, POT_WALL, POT_Y, POT_FLOOR = 2.6, 0.6, (1.0, 7.0), 1.6
LITH_Y = (5.0, 5.6)                          # the litharge in the pot
STREAM = ((X_T - 0.45, X_T + 0.45), (LITH_Y[1], GUTTER_Y[0]), (0.95, GUTTER_Z[0]))   # falling off the gutter's end

# the flue and the stack, in the back row's west column, the outlet on its top
FLUE_OPENING = ((8.0, 12.5), (14.5, 19.0), (CH_Z[1], BODY_D))   # through the back wall at the chamber's west end
BACK_FLUE = ((4.0, 12.0), (15.0, 19.0), (BODY_D, 44.0))
STACK = ((4.0, 12.0), (15.0, 31.5), (36.0, 44.0))
CURB = ((2.0, 14.0), (29.0, TOP), (34.0, 46.0))   # the outlet's curb, of the lining's refractory brick, flush:
CURB_RIM = 1.0                                     # a rim 1 wide round the cap's seat, the seat a ledge round the
CAP_Y = (31.5, TOP)                                # stack's mouth; the cap, a flat iron plate, set flush in it
CAP_SEAT = ((CURB[0][0] + CURB_RIM, CURB[0][1] - CURB_RIM), CAP_Y, (CURB[2][0] + CURB_RIM, CURB[2][1] - CURB_RIM))

# the tuyere, through the back wall over the test's back, blowing north across the lead towards the breast; fed from an
# iron wind box set in the back wall. Its face plate is set back in a pocket in the wall's face, lined with iron, and
# a short pipe stub flanged to the plate stands in the pocket, its mouth at the blast cell's face: ppex's pipe (a square
# tube 6 across, its block's 5..11, 1-voxel walls round a 4 x 4 bore), copper, with half a soldered joint round its
# mouth as UnifiedPipes draws copper and lead pipe (Pipes/tools/solderedpipe.py), so the pipe the player lays in the
# cell behind meets it as one ppex pipe meets the next. Nothing stands proud of the wall's face.
TUYERE = (X_T, 17.1)                         # (x, y): its axis, along z
TUYERE_R = 1.1
SOCKET = ((B + 5.0, B + 11.0), (B + 5.0, B + 11.0))   # (x, y): the stub's outside at the blast face's middle, 6 x 6
PIPE_WALL = 1.0                              # ppex's pipe's wall: a 4 x 4 bore
JOINT_LENGTH, JOINT_SWELL = 0.75, 0.2        # half a soldered joint (solderedpipe.py): along the pipe, proud of it
POCKET = ((B + 3.5, B + 12.5), (B + 3.5, B + 12.5), (29.5, BODY_D))   # the pocket in the wall's face (x, y, z)
SLEEVE_T = 0.5                               # the iron lining the pocket's four sides, flush with the face
FLANGE = (3.7, 0.5)                          # the stub's flange on the plate: half across, thickness
BOLT = 0.6                                   # its four bolt heads, at the corners
WINDBOX = ((17.5, 28.5), (15.5, 28.5), (27.0, POCKET[2][0]))   # the wind box, in the back wall behind the pocket
WINDBOX_T = 0.5                              # its walls
FACEPLATE_T = 0.6                            # its face plate, at the pocket's back
TUYERE_Z = (23.0, WINDBOX[2][0])             # the tuyere: its mouth a voxel into the chamber, its back end in the box

# the brick, in two roles: the brickwork (any one fired brick) and the lining (tier-3 refractory brick, LINING_T
# thick on every hot face: the chamber's walls, floor and vault, the fire bridge, the fireplace from its bearers up,
# the flue opening, the back flue, the stack and the outlet's curb)
LINING_T = 3.0

# the iron binding: angle iron (an L section, ANGLE_T thick) wrapping the brickwork's corners, one leg on each face, its
# inside corner bedded against the brick's corner, set flush in a rebate cut in the brick so nothing leaves the
# footprint. An angle up every outside vertical corner (the owner's rule for furnace brickwork: the binding locks the
# brick in on both faces), and an angle band round the top, wrapping every top edge, joining their heads.
ANGLE_T = 1.0                                # the legs' thickness (period angle iron)
ANGLE_W = 2.5                                # an upright's legs: their width over the brick's faces
BAND_W = 2.0                                 # the top band's legs (the stack column's top: clear of the outlet's curb)
# the outside vertical corners: (name, x, z, dx, dz), the corner's point and the way into the brick along x and z
CORNERS = (("nw", 0.0, 0.0, 1, 1), ("ne", 3 * B, 0.0, -1, 1), ("se", 3 * B, 2 * B, -1, -1),
           ("sw", 0.0, 3 * B, 1, -1), ("sm", B, 3 * B, -1, -1))
# the top edges: (name, the axis the edge runs along, its fixed coordinate on the other axis, the way into the brick
# from it, its span along its axis). The band is one frame (`band_ends`): the bands along x run through every corner,
# the bands along z butt against them, and at the L's inside corner the x band reaches over the top to meet the z one;
# the uprights stop under the band and carry it.
TOP_EDGES = (("n", "x", 0.0, 1, (0.0, 3 * B)), ("e", "z", 3 * B, -1, (0.0, 2 * B)), ("s", "x", 2 * B, -1, (B, 3 * B)),
             ("m", "z", B, -1, (2 * B, 3 * B)), ("sw", "x", 3 * B, -1, (0.0, B)), ("w", "z", 0.0, 1, (0.0, 3 * B)))

# ---------------------------------------------------------------- the build
# Built in stages (the rig's `build`; their requires values are the viewer's Build states): the iron frame, a grid
# recipe placed as the machine (the controller and its ghosts), then the brickwork, the lining and the fittings (the
# fire door, the gutter, the litharge pot and the damper); last the test, the wearing part (the owner's ruling on #740:
# like the cylinder borer's cutter head, fitted after the fittings and renewed when spent, every TEST_CHARGES charges,
# about TEST_LEAD units of lead). The test's iron ring is a fitting and stays on the hearth: the test is its bone ash,
# so a spent test is broken out of the ring and a new one beaten in, through the working door (a whole test, 18.4 across
# its ring, is wider than the door's 12). The brickwork's and the
# lining's counts are measured from the model's volume at the game's rates, as the crucible furnace's are: a claybricks
# block is 2 fired bricks, a refractory brick block 4, rounded up; the lining's fire clay one for every
# LINING_PER_FIRECLAY of its bricks; the test's bone meal at the bone-ash cupel's rate (4 bone meal for its 41 cubic
# voxels). The frame's recipe and the other stages' materials are proposals for the owner (README, "Open").
FRAME_RECIPE = (("game:metalplate-iron", 3), ("game:rod-iron", 4), ("game:metalnailsandstrips-iron", 12))
BRICKS_PER_BLOCK = 2
LINING_PER_BLOCK = 4
LINING_PER_FIRECLAY = 2.5
BONEMEAL_PER_VOXEL = 4 / 41.0                # the bone-ash cupel: 4 bone meal pressed into 41 cubic voxels
FIRED_BRICK = "game:burnedbrick-*"
LINING_BRICK = "game:refractorybrick-fired-tier3"
FIRECLAY = "game:clay-fire"
BONEMEAL = "game:bonemeal"
TEST_CHARGES = 15                            # the test's wear: charges cupelled on it before it is spent (#740)
TEST_LEAD = 3000                             # about the lead (units) a test takes over them, 200 a charge
FITTINGS_STAGE = (("game:metalplate-iron", 3),   # the fire door, the gutter and the pot, the damper; the test's ring
                  ("game:metalplate-copper", 1))  # the blast stub and its flange
CAP_ITEM = ("game:metalplate-iron", 1)
STAGES = ("brickwork", "lining", "fittings", "test")   # the stages after the frame, in order: the test the wearing part
STAGE_STATES = (                             # the viewer's Build select: (id, the stages fitted)
    ("frame", ()),
    ("brickwork", ("brickwork",)),
    ("lining", ("brickwork", "lining")),
    ("fittings", ("brickwork", "lining", "fittings")),
    ("complete", STAGES),
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

# The scenario's states of the test and the fire, as the viewer gives them (site/models.json; tools/tests holds the
# two together). Each is the requires values fitted.
WORK_STATES = (
    ("cold", "Cold and empty", ()),
    ("coal", "Coal on the grate", ("coal",)),
    ("lit", "Lit: the flame drawing over the test", ("fire",)),
    ("charged", "Lead melted on the test, the blast on", ("fire", "lead")),
    ("running", "Cupelling: the litharge running off", ("fire", "lead", "stream", "litharge")),
    ("brightened", "Brightened: the silver cake", ("fire", "silver", "litharge")),
    ("emptied", "The cake and the litharge taken out", ("fire",)),
)
WORK_STATE_IDS = tuple(s[0] for s in WORK_STATES)
DAMPERS = ("open", "half", "shut")
CONTENTS = ("coal", "fire", "lead", "silver", "stream", "litharge")


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
    side continue one texture across a block face with no seam (the brickwork is many boxes). The directions are the
    game's (CubeMeshUtil): a side face's u runs left to right as seen from outside it and its v down from the block's
    top; up runs u with +x and v with +z, down u against x and v with +z (as the crucible furnace's)."""
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


AX = {"x": 0, "y": 1, "z": 2}
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}   # a disc's angle 0 direction, and the in-plane direction across it


def disc(axis, c, a0, a1, r, name, part, tex, k=4, phase=0.0):
    """A plain round part (an octagon for k 4): k strips as long as the 2k-gon is across, 180/k degrees apart, each a
    hair shorter than the last so no two ends share a plane. Phase 0 puts flats square to the axes."""
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


def segment_y(p0, p1, y0, y1, thick, name, part, tex):
    """A wall piece standing on the chord p0 -> p1 ((x, z) points), `thick` to the chord's outer side (for a chord
    taken the oval's way round, east then south, the outside), y0..y1 high."""
    dx, dz = p1[0] - p0[0], p1[1] - p0[1]
    length = math.hypot(dx, dz)
    along = [dx / length, 0.0, dz / length]
    out_n = [along[2], 0.0, -along[0]]
    c = [(p0[0] + p1[0]) / 2 + out_n[0] * thick / 2, (y0 + y1) / 2, (p0[1] + p1[1]) / 2 + out_n[2] * thick / 2]
    return obox(c, (along, [0.0, 1.0, 0.0]), (length, y1 - y0, thick), name, part, tex)


def channel(p0, p1, half_w, wall, height, name, part, tex):
    """An open trough (a bottom and two sides) along p0 -> p1 in a plane x = const, the points on its bottom's top
    surface."""
    d = [p1[k] - p0[k] for k in range(3)]
    along = unit(d)
    across = [1.0, 0.0, 0.0]
    up = cross(along, across)
    if up[1] < 0:
        up = [-v for v in up]
    length = math.sqrt(sum(v * v for v in d))
    mid = [(p0[k] + p1[k]) / 2 for k in range(3)]
    out = [obox([mid[k] - up[k] * wall / 2 for k in range(3)], (along, up), (length, wall, 2 * (half_w + wall)),
                f"{name}_bottom", part, tex)]
    side = cross(along, up)
    for tag, s in (("a", -1), ("b", 1)):
        c = [mid[k] + side[k] * s * (half_w + wall / 2) + up[k] * height / 2 for k in range(3)]
        out.append(obox(c, (along, up), (length - 0.02, height, wall), f"{name}_side{tag}", part, tex))
    return out


def oval_fill(c, y0, y1, a, b, name, part, tex):
    """The octagon inscribed in the oval of half axes (a, b) about c (its corners at 22.5 + 45 k degrees), solid
    from y0 to y1: two crossed rectangles, and a slab along each diagonal side, inward, over the corner between
    them. Every element stays inside the octagon."""
    cx, cz = c
    out = [box([cx - C22 * a, y0, cz - S22 * b], [cx + C22 * a, y1, cz + S22 * b], f"{name}_a", part, tex),
           box([cx - S22 * a, y0, cz - C22 * b], [cx + S22 * a, y1, cz + C22 * b], f"{name}_b", part, tex)]
    for n, t in enumerate((22.5, 112.5, 202.5, 292.5), 1):
        p0, p1 = ell(t, a, b, c), ell(t + 45.0, a, b, c)
        q = (cx + math.copysign(S22 * a, p0[0] + p1[0] - 2 * cx), cz + math.copysign(S22 * b, p0[1] + p1[1] - 2 * cz))
        dx, dz = p1[0] - p0[0], p1[1] - p0[1]
        ln = math.hypot(dx, dz)
        depth = abs((q[0] - p0[0]) * dz - (q[1] - p0[1]) * dx) / ln
        out.append(segment_y(p1, p0, y0, y1, depth, f"{name}_d{n}", part, tex))
    return out


def oct_disc(cx, cz, y0, y1, a, name, part, tex):
    """A solid octagonal disc about a vertical axis, apothem `a` (four strips at 0, 45, 90 and 135 degrees)."""
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


# ---------------------------------------------------------------- the brickwork
UPRIGHT_TOP = TOP - BAND_W                   # the uprights' heads: under the band's face leg, which sits on them


def angle_legs(name, x, z, dx, dz):
    """An upright angle at the corner (x, z), the brick towards (dx, dz): two legs, as (name, lo, hi): `a` over the face
    across z (its full width), `b` over the face across x (from the first's back on), ANGLE_T thick, flush, from the
    ground to UPRIGHT_TOP, where the band's face legs sit on their heads."""
    t, w = ANGLE_T, ANGLE_W
    xa, za = sorted((x, x + dx * w)), sorted((z, z + dz * t))
    xb, zb = sorted((x, x + dx * t)), sorted((z + dz * t, z + dz * w))
    return [(f"fr_angle_{name}_a", [xa[0], 0.0, za[0]], [xa[1], UPRIGHT_TOP, za[1]]),
            (f"fr_angle_{name}_b", [xb[0], 0.0, zb[0]], [xb[1], UPRIGHT_TOP, zb[1]])]


def band_ends(axis, at, span):
    """How far the band along one top edge reaches past (+) or stops short of (-) its edge's two ends, for its top leg
    and its face leg: ((top0, face0), (top1, face1)). At an outside corner (in CORNERS) the band along x runs to the
    corner and the band along z butts against it (its top leg against the x band's top leg's side, its face leg against
    the x band's face leg's back). At the inside corner the band along x reaches BAND_W on over the top, so the band
    along z, running to the corner, butts against its side: the two top legs meet in one piece of frame."""
    outside = {(x, z) for _, x, z, _, _ in CORNERS}
    ends = []
    for s in span:
        pt = (s, at) if axis == "x" else (at, s)
        if pt in outside:
            ends.append((0.0, 0.0) if axis == "x" else (-BAND_W, -ANGLE_T))
        else:
            ends.append((BAND_W, 0.0) if axis == "x" else (0.0, 0.0))
    return tuple(ends)


def band_legs(name, axis, at, d, span):
    """The top band along one top edge: an angle wrapping it, as (name, lo, hi): its top leg over the top, BAND_W
    wide, and its face leg down the face, ANGLE_T thick, flush; their ends set by `band_ends`."""
    t, w = ANGLE_T, BAND_W
    (t0, f0), (t1, f1) = band_ends(axis, at, span)
    ts, fs = (span[0] - t0, span[1] + t1), (span[0] - f0, span[1] + f1)
    across, face = sorted((at, at + d * w)), sorted((at, at + d * t))
    if axis == "x":
        return [(f"fr_band_{name}_top", [ts[0], TOP - t, across[0]], [ts[1], TOP, across[1]]),
                (f"fr_band_{name}_face", [fs[0], TOP - w, face[0]], [fs[1], TOP - t, face[1]])]
    return [(f"fr_band_{name}_top", [across[0], TOP - t, ts[0]], [across[1], TOP, ts[1]]),
            (f"fr_band_{name}_face", [face[0], TOP - w, fs[0]], [face[1], TOP - t, fs[1]])]


def binding():
    """The iron binding, as (name, lo, hi): an angle up each outside vertical corner (`angle_legs`) and an angle band
    round the top wrapping each top edge (`band_legs`), its legs over the faces next to the corners and over the top,
    set flush in rebates in the brick. The side faces carry the angles' legs and the band at their edges and are
    otherwise plain brick: in a row, the neighbours' legs meet flush at the shared plane."""
    out = [leg for c in CORNERS for leg in angle_legs(*c)]
    out += [leg for e in TOP_EDGES for leg in band_legs(*e)]
    return [piece for name, lo, hi in out for piece in split_blocks(name, lo, hi)]


def vault_voids():
    """The chamber's stepped vault over the chamber, the bridge and the fireplace, from the springing up."""
    return [([VAULT_X[0], y0, CH_Z[0] + inset], [VAULT_X[1], y1, CH_Z[1] - inset]) for inset, y0, y1 in VAULT]


def hot_voids():
    """The hot spaces, as (lo, hi): the chamber over the hearth, over the fire bridge, the fireplace from its bearers up,
    and the vault."""
    return [([CH_X[0], HEARTH_Y, CH_Z[0]], [CH_X[1], SPRING_Y, CH_Z[1]]),
            ([BRIDGE_X[0], BRIDGE_TOP, CH_Z[0]], [BRIDGE_X[1], SPRING_Y, CH_Z[1]]),
            ([FIRE_X[0], BEARER_Y[0], CH_Z[0]], [FIRE_X[1], SPRING_Y, CH_Z[1]])] + vault_voids()


def flue_voids():
    """The flue's way: the opening, the back flue and the stack (the cap's seat is cut separately, in the curb)."""
    return [([x0, y0, z0], [x1, y1, z1]) for (x0, x1), (y0, y1), (z0, z1) in (FLUE_OPENING, BACK_FLUE, STACK)]


def brick_voids():
    """Every space cut out of the brick, as (lo, hi) in voxels: the chamber, the bridge's top, the fireplace and the
    vault; the cave and the ash mouth; the bay, the working door, the niche and the gutter's groove; the fire door's
    opening, its rebate and its frame; the flue opening, the back flue, the stack and the cap's seat; the room for
    the iron set into the brick (the bearers' ends, the lintels, the jambs, the damper's recess with its guides, the
    tuyere and its wind box, the binding, the frame's ties); and the back row's middle and east columns, which are
    not the furnace's."""
    v = hot_voids()
    (mx0, mx1), (my0, my1) = MOUTH
    v.append(([mx0, my0, 0.0], [mx1, my1, CH_Z[0]]))                                       # the ash mouth
    v.append(([CAVE[0][0], CAVE[1][0], CAVE[2][0]], [CAVE[0][1], CAVE[1][1], CAVE[2][1]]))   # the cave
    for x0, x1 in ((BEARER_X[0], FIRE_X[0]), (FIRE_X[1], BEARER_X[1])):
        for z0, z1 in BEARER_Z:
            v.append(([x0, BEARER_Y[0], z0], [x1, BEARER_Y[1], z1]))                       # the bearers' ends
    for (x0, x1), (y0, y1), (z0, z1) in (LINTEL, RECESS, BAY, DOOR, NICHE, GROOVE, NICHE_LINTEL):
        v.append(([x0, y0, z0], [x1, y1, z1]))
    (fx0, fx1), (fy0, fy1) = FIREDOOR
    v.append(([fx0, fy0, 0.0], [fx1, fy1, CH_Z[0]]))                                       # the fire door's opening
    (px0, px1), (py0, py1), (pz0, pz1) = DOOR_PLATE
    v.append(([px0, py0, pz0], [px1, py1, pz1]))                                           # its rebate
    (gx0, gx1), (gy0, gy1), (gz0, gz1) = FIREDOOR_FRAME
    v.append(([gx0, gy0, gz0], [gx1, gy1, gz1]))                                           # its frame
    for x0, x1 in JAMBS:
        v.append(([x0, 0.0, JAMB_Z[0]], [x1, BAY[1][1], JAMB_Z[1]]))                       # the jambs
    for _, (z0, z1) in BAY_LINTELS:
        v.append(([JAMBS[0][0], BAY_LINTEL_Y[0], z0], [JAMBS[1][1], BAY_LINTEL_Y[1], z1]))   # the bay's lintels
    v.append(([TUYERE[0] - TUYERE_R, TUYERE[1] - TUYERE_R, CH_Z[1]], [TUYERE[0] + TUYERE_R, TUYERE[1] + TUYERE_R, WINDBOX[2][0]]))
    (wx0, wx1), (wy0, wy1), (wz0, wz1) = WINDBOX
    v.append(([wx0, wy0, wz0], [wx1, wy1, wz1]))                                           # the wind box
    (qx0, qx1), (qy0, qy1), (qz0, qz1) = POCKET
    v.append(([qx0, qy0, qz0], [qx1, qy1, qz1]))                                           # its pocket
    v += flue_voids()
    (sx0, sx1), (sy0, sy1), (sz0, sz1) = CAP_SEAT
    v.append(([sx0, sy0, sz0], [sx1, sy1, sz1]))                                           # the cap's seat
    (gx0, gx1), (gy0, gy1), (gz0, gz1) = GAP
    v.append(([gx0, gy0, gz0], [gx1, gy1, gz1]))                                           # not the furnace's
    v += [(lo, hi) for _, lo, hi in binding()]
    v += [(lo, hi) for _, lo, hi in ties()]
    return v


def ties():
    """The frame's ties, as (name, lo, hi): the least iron that joins each piece of iron to the ground before the
    brick is in, all of it inside the brickwork (voids are cut for it), so it is hidden once the brick is in: a post
    under each end of each grate bearer (in the bridge's foot and in the east wall), a post each side of the ash
    mouth under the lintel's ends, behind the damper's recess (the guides stand against the lintel's face), and a
    post under the wind box in the back wall (the tuyere is carried by the box)."""
    out = []
    for side, (x0, x1) in (("w", (BEARER_X[0], BEARER_X[0] + 1.0)), ("e", (BEARER_X[1] - 1.0, BEARER_X[1]))):
        for end, (z0, z1) in zip(("n", "s"), BEARER_Z):
            out.append((f"fr_tie_bearer_{end}{side}", [x0, 0.0, z0], [x1, BEARER_Y[0], z1]))
    (lx, ly, lz) = LINTEL
    for side, (x0, x1) in (("w", (lx[0] - 1.0, lx[0])), ("e", (lx[1], lx[1] + 1.0))):
        out.append((f"fr_tie_lintel_{side}", [x0, 0.0, lz[0]], [x1, ly[1], lz[1]]))
    (wx0, _), (wy0, _), (wz0, wz1) = WINDBOX
    out.append(("fr_tie_windbox", [22.0, 0.0, wz0 + WINDBOX_T + 0.2], [24.5, wy0, wz1 - FACEPLATE_T - 0.2]))
    return out


def lining_zones():
    """Where the brick is the lining's, as (lo, hi): LINING_T round the hot spaces (the chamber, the bridge, the
    fireplace from its bearers up, the vault), and round the flue (the opening, the back flue, the stack and the
    outlet's curb). The rest of the brick is the brickwork's: the base, the cave and the ash mouth, the outer walls."""
    t = LINING_T
    out = [([lo[0] - t, max(lo[1] - t, CAVE[1][1]), lo[2] - t], [hi[0] + t, hi[1] + t, hi[2] + t]) for lo, hi in hot_voids()]
    out += [([lo[0] - t, lo[1] - t, lo[2] - t], [hi[0] + t, min(hi[1] + t, TOP), hi[2] + t]) for lo, hi in flue_voids()]
    (cx0, cx1), (cy0, cy1), (cz0, cz1) = CURB
    out.append(([cx0, cy0, cz0], [cx1, cy1, cz1]))
    return out


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
    `brickwork` part, `ln_brick_NNN` in the `lining`. A face lying on its whole area against brick always there with
    it is left out: a brickwork face against brickwork, a lining face against either (the lining goes in after the
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


# ---------------------------------------------------------------- builders: the iron frame
def build_iron():
    """The iron frame (`frame`): what the frame stage puts down, all of it standing on its own, set into the brick once
    it is in. The binding; the grate's two bearers and its firebars; the ash mouth's lintel and the damper's guides;
    the fire door's cast frame on the guides; the jambs of the bay and the working door, with the lintels over the
    bay, the door and the niche; the tuyere through the back wall and its wind box, the blast inlet's socket in its
    face plate; and the frame's ties (`ties`, hidden in the brickwork)."""
    f, t = "frame", "iron"
    out = [box(lo, hi, name, f, t) for name, lo, hi in binding()]
    for n, (z0, z1) in enumerate(BEARER_Z, 1):
        out.append(box([BEARER_X[0], BEARER_Y[0], z0], [BEARER_X[1], BEARER_Y[1], z1], f"fr_bearer{n}", f, t))
    gap = (FIRE_X[1] - FIRE_X[0] - BARS * BAR_W) / (BARS - 1)
    for n in range(BARS):
        x0 = FIRE_X[0] + n * (BAR_W + gap)
        out.append(box([x0, BAR_Y[0], BEARER_Z[0][0]], [x0 + BAR_W, BAR_Y[1], BEARER_Z[1][1]], f"fr_bar{n + 1}", f, t))
    (lx, ly, lz) = LINTEL
    out.append(box([lx[0], ly[0], lz[0]], [lx[1], ly[1], lz[1]], "fr_lintel", f, t))
    (_, ry, rz) = RECESS
    for side, (x0, x1) in zip(("w", "e"), GUIDES):
        out.append(box([x0, ry[0], rz[0]], [x1, ry[1], rz[1]], f"fr_guide_{side}", f, t))
    (gx0, gx1), (gy0, gy1), (gz0, gz1) = FIREDOOR_FRAME
    (fx0, fx1), (fy0, fy1) = FIREDOOR
    out += [box([gx0, gy0, gz0], [gx1, fy0, gz1], "fr_firedoor_sill", f, t),
            box([gx0, fy1, gz0], [gx1, gy1, gz1], "fr_firedoor_head", f, t),
            box([gx0, fy0, gz0], [fx0, fy1, gz1], "fr_firedoor_w", f, t),
            box([fx1, fy0, gz0], [gx1, fy1, gz1], "fr_firedoor_e", f, t)]
    for side, (x0, x1) in zip(("w", "e"), JAMBS):
        out += [box(lo, hi, n2, f, t) for n2, lo, hi in split_blocks(f"fr_jamb_{side}", [x0, 0.0, JAMB_Z[0]], [x1, BAY[1][1], JAMB_Z[1]])]
    for tag, (z0, z1) in BAY_LINTELS:
        out += [box(lo, hi, n2, f, t) for n2, lo, hi in
                split_blocks(f"fr_baylintel_{tag}", [JAMBS[0][0], BAY_LINTEL_Y[0], z0], [JAMBS[1][1], BAY_LINTEL_Y[1], z1])]
    (nx, ny, nz) = NICHE_LINTEL
    out.append(box([nx[0], ny[0], nz[0]], [nx[1], ny[1], nz[1]], "fr_nichelintel", f, t))
    tx, ty = TUYERE
    out += disc("z", (tx, ty, 0.0), TUYERE_Z[0], TUYERE_Z[1], TUYERE_R, "fr_tuyere", f, t)
    out += build_windbox()
    out += [box(lo, hi, name, f, t) for name, lo, hi in ties()]
    return out


def build_windbox():
    """The wind box (`fr_windbox_*`): an iron box set in the back wall, its front plate (towards the chamber) taking
    the tuyere's back end, its walls round it, and its face plate at the back of a pocket in the wall's face, with the
    stub's bore through it (the stub, flanged to it, is a fitting); the pocket's sides lined with iron (`fr_pocket_*`)
    from the plate to the wall's face, so no brick shows in it."""
    f, t = "frame", "iron"
    (x0, x1), (y0, y1), (z0, z1) = WINDBOX
    w, fp = WINDBOX_T, FACEPLATE_T
    zf = z1 - fp
    out = [box([x0, y0, z0], [x1, y1, z0 + w], "fr_windbox_front", f, t),
           box([x0, y0, z0 + w], [x0 + w, y1, zf], "fr_windbox_w", f, t),
           box([x1 - w, y0, z0 + w], [x1, y1, zf], "fr_windbox_e", f, t),
           box([x0 + w, y0, z0 + w], [x1 - w, y0 + w, zf], "fr_windbox_bottom", f, t),
           box([x0 + w, y1 - w, z0 + w], [x1 - w, y1, zf], "fr_windbox_top", f, t)]
    (sx0, sx1), (sy0, sy1) = bore()
    out += [box([x0, y0, zf], [sx0, y1, z1], "fr_windbox_face_w", f, t),
            box([sx1, y0, zf], [x1, y1, z1], "fr_windbox_face_e", f, t),
            box([sx0, y0, zf], [sx1, sy0, z1], "fr_windbox_face_s", f, t),
            box([sx0, sy1, zf], [sx1, y1, z1], "fr_windbox_face_n", f, t)]
    (qx0, qx1), (qy0, qy1), (qz0, qz1) = POCKET
    st = SLEEVE_T
    out += [box([qx0, qy0, qz0], [qx0 + st, qy1, qz1], "fr_pocket_w", f, t),
            box([qx1 - st, qy0, qz0], [qx1, qy1, qz1], "fr_pocket_e", f, t),
            box([qx0 + st, qy0, qz0], [qx1 - st, qy0 + st, qz1], "fr_pocket_s", f, t),
            box([qx0 + st, qy1 - st, qz0], [qx1 - st, qy1, qz1], "fr_pocket_n", f, t)]
    return out


def bore():
    """The stub's bore (x, y): ppex's pipe's 4 x 4, in the middle of the socket."""
    (sx0, sx1), (sy0, sy1) = SOCKET
    return (sx0 + PIPE_WALL, sx1 - PIPE_WALL), (sy0 + PIPE_WALL, sy1 - PIPE_WALL)


def square_ring(lo, hi, inner, z0, z1, name, part, tex):
    """Four boxes round a square hole: the outside lo..hi (x, y), the hole inner = ((x0, x1), (y0, y1)), z0..z1; the
    two across y full width, as solderedpipe's walls."""
    (ix0, ix1), (iy0, iy1) = inner
    return [box([lo[0], lo[1], z0], [hi[0], iy0, z1], f"{name}_s", part, tex),
            box([lo[0], iy1, z0], [hi[0], hi[1], z1], f"{name}_n", part, tex),
            box([lo[0], iy0, z0], [ix0, iy1, z1], f"{name}_w", part, tex),
            box([ix1, iy0, z0], [hi[0], iy1, z1], f"{name}_e", part, tex)]


def build_stub():
    """The blast stub (`ft_stub*`, a fitting): a short length of ppex's pipe in copper, flanged to the wind box's
    recessed face plate (`ft_stubflange`, four iron bolt heads at its corners), its mouth at the blast cell's face with
    half a soldered joint round it (`ft_stubjoint`, JOINT_SWELL proud, JOINT_LENGTH long, ending at the face), as
    UnifiedPipes draws a copper or lead pipe's end, so the pipe laid in the cell behind meets it as at any joint."""
    p = "fittings"
    (sx0, sx1), (sy0, sy1) = SOCKET
    cx, cy = (sx0 + sx1) / 2, (sy0 + sy1) / 2
    zp = POCKET[2][0]                        # the face plate's face
    fa, ft = FLANGE
    zt = zp + ft
    out = square_ring([cx - fa, cy - fa], [cx + fa, cy + fa], bore(), zp, zt, "ft_stubflange", p, "copper")
    out += square_ring([sx0, sy0], [sx1, sy1], bore(), zt, BODY_D, "ft_stub", p, "copper")
    sw = JOINT_SWELL
    out += square_ring([sx0 - sw, sy0 - sw], [sx1 + sw, sy1 + sw], ((sx0, sx1), (sy0, sy1)), BODY_D - JOINT_LENGTH, BODY_D,
                       "ft_stubjoint", p, "solder")
    for k, (bx, by) in enumerate(((-1, -1), (1, -1), (-1, 1), (1, 1))):
        x = cx + bx * (fa - 0.05 - BOLT / 2)
        y = cy + by * (fa - 0.05 - BOLT / 2)
        out.append(box([x - BOLT / 2, y - BOLT / 2, zt], [x + BOLT / 2, y + BOLT / 2, zt + BOLT], f"ft_stubbolt{k + 1}", p, "iron"))
    return out


def build_frame():
    """Everything that stands still and is the furnace's body: the brick (brickwork and lining) and the iron frame."""
    return build_brick() + build_iron()


# ---------------------------------------------------------------- the test
def ring_side(k, a, b, y0, y1, thick, name, part, tex, ext=0.0, inward=False):
    """Side k of the octagon inscribed in the oval (a, b): its chord from t 45k - 22.5 to 45k + 22.5, run on by `ext`
    at each end but the one against the front side (the breast's: the bone ash, the wearing part, and the ring, a
    fitting, only meet there), standing `thick` outward (or inward) from it."""
    p0, p1 = ell(45.0 * k - 22.5, a, b), ell(45.0 * k + 22.5, a, b)
    dx, dz = p1[0] - p0[0], p1[1] - p0[1]
    ln = math.hypot(dx, dz)
    e0 = 0.0 if (k - 1) % 8 == FRONT_K else ext / ln
    e1 = 0.0 if (k + 1) % 8 == FRONT_K else ext / ln
    p0, p1 = (p0[0] - dx * e0, p0[1] - dz * e0), (p1[0] + dx * e1, p1[1] + dz * e1)
    return segment_y(p1, p0, y0, y1, thick, name, part, tex) if inward else segment_y(p0, p1, y0, y1, thick, name, part, tex)


def build_test():
    """The test (`test`, the wearing part): an oval of bone ash in an iron ring, standing on the chamber's hearth. The
    ring (`ft_ring<k>`, a fitting: it stays when a spent test is broken out) is seven sides of an octagon inscribed in
    the oval, each run on a little at its ends so the corners close; the front side is left out, and the breast stands
    there, bone ash, thicker, with the gate cut in it from the floor up. Inside the ring the bone ash: its floor (the
    dish the lead lies in) and a rim round it."""
    p = "test"
    a, b = TEST_A
    out = []
    for k in range(8):
        if k == FRONT_K:
            continue
        out.append(ring_side(k, a, b, HEARTH_Y, RING_TOP, RING_T, f"ft_ring{k + 1}", "fittings", "iron", ext=RING_EXT))
    out += oval_fill(TEST_C, HEARTH_Y, ASH_TOP, a, b, "ts_ash", p, "boneash")
    for k in range(8):
        if k == FRONT_K:
            continue
        out.append(ring_side(k, a, b, ASH_TOP, BAND[1], BAND[0], f"ts_rim{k + 1}", p, "boneash", inward=True))
    (bx0, bx1), (bz0, bz1) = BREAST_X, BREAST_Z
    out += [box([bx0, HEARTH_Y, bz0], [bx1, ASH_TOP, FRONT_CHORD_Z], "ts_breast_foot", p, "boneash"),
            box([bx0, ASH_TOP, bz0], [X_T - GATE, RING_TOP, bz1], "ts_breast_w", p, "boneash"),
            box([X_T + GATE, ASH_TOP, bz0], [bx1, RING_TOP, bz1], "ts_breast_e", p, "boneash")]
    return out


def build_lead():
    """The molten lead (`lead`) in the test's dish, glowing."""
    a, b, top = POOL
    return oval_fill(TEST_C, ASH_TOP, top, a, b, "lead_pool", "lead", "lead")


def build_silver():
    """The silver cake (`silver`), brightened, on the test where the lead was."""
    a, b, top = CAKE
    return oval_fill(TEST_C, ASH_TOP, top, a, b, "silver_cake", "silver", "silver")


def gutter_y(z):
    """The gutter's bottom's top surface at z: from GUTTER_Y[1] at the gate down to GUTTER_Y[0] at its outer end."""
    (z0, z1), (y0, y1) = GUTTER_Z, GUTTER_Y
    return y0 + (y1 - y0) * (z - z0) / (z1 - z0)


def build_stream():
    """The litharge running (`stream`): through the gate, a thin layer down the gutter, and falling off its end into
    the pot, glowing."""
    p = "stream"
    out = [box([X_T - GATE, ASH_TOP, BREAST_Z[0]], [X_T + GATE, ASH_TOP + 0.25, pool_front_z()], "stream_gate", p, "litharge")]
    (z0, z1) = GUTTER_Z
    p0, p1 = (X_T, gutter_y(z1), z1), (X_T, gutter_y(z0), z0)
    along = unit([p1[k] - p0[k] for k in range(3)])
    up = cross(along, [1.0, 0.0, 0.0])
    if up[1] < 0:
        up = [-v for v in up]
    th = 0.15
    mid = [(p0[k] + p1[k]) / 2 + up[k] * th / 2 for k in range(3)]
    ln = math.dist(p0, p1)
    out.append(obox(mid, (along, up), (ln, th, 2 * GUTTER_HALF - 0.1), "stream_gutter", p, "litharge"))
    (sx0, sx1), (sy0, sy1), (sz0, sz1) = STREAM
    out.append(box([sx0, sy0, sz0], [sx1, sy1, sz1], "stream_fall", p, "litharge"))
    return out


def pool_front_z():
    """The lead pool's front edge (z): the gate's litharge runs back to it."""
    return TEST_C[1] - C22 * POOL[1]


def build_litharge():
    """The litharge in the pot (`litharge`): what the player takes out."""
    cx, cz = POT_C
    return oct_disc(cx, cz, LITH_Y[0], LITH_Y[1], POT_A - POT_WALL + 0.01, "litharge_fill", "litharge", "litharge")


# ---------------------------------------------------------------- the fittings, the damper, the cap
def build_fittings():
    """The fittings (`fittings`): the fire door, shut in its rebate behind its frame, with its hinge straps and handle;
    the litharge gutter, from the gate through the working door's sill, bedded at its outer end in its groove; and
    the litharge pot standing in the bay under the gutter's end."""
    p = "fittings"
    (px0, px1), (py0, py1), (pz0, pz1) = DOOR_PLATE
    out = [box([px0, py0, pz0], [px1, py1, pz1], "ft_firedoor", p, "iron"),
           box([FIREDOOR[0][0] + 0.1, py0 + 0.9, 0.3], [FIREDOOR[0][0] + 2.5, py0 + 1.5, pz0], "ft_firedoor_strap1", p, "iron"),
           box([FIREDOOR[0][0] + 0.1, py1 - 1.5, 0.3], [FIREDOOR[0][0] + 2.5, py1 - 0.9, pz0], "ft_firedoor_strap2", p, "iron"),
           box([px1 - 1.6, 13.4, 0.1], [px1 - 0.8, 14.6, pz0], "ft_firedoor_handle", p, "iron")]
    (z0, z1) = GUTTER_Z
    out += channel((X_T, gutter_y(z1), z1), (X_T, gutter_y(z0), z0), GUTTER_HALF, GUTTER_WALL, GUTTER_H, "ft_gutter", p, "iron")
    cx, cz = POT_C
    out += oct_disc(cx, cz, POT_Y[0], POT_FLOOR, POT_A, "ft_pot_floor", p, "iron")
    out += oct_tube(cx, cz, POT_FLOOR, POT_Y[1], POT_A, POT_WALL, "ft_pot_wall", p, "iron")
    return out + build_stub()


def build_damper(place):
    """The ash-pit damper (`damper<place>`): an iron plate sliding in its recess over the ash mouth, with a knob, in
    one of its three places. Each place is its own part; gameplay draws the one the damper is in."""
    p = f"damper{place}"
    y0 = DAMPER[place]
    out = [box([GUIDES[0][1], y0, DAMPER_Z[0]], [GUIDES[1][0], y0 + DAMPER_H, DAMPER_Z[1]], f"{p}_plate", p, "iron")]
    (kx, kz, ky) = KNOB
    out.append(box([kx[0], y0 + ky[0], kz[0]], [kx[1], y0 + ky[1], kz[1]], f"{p}_knob", p, "iron"))
    return out


def build_cap():
    """The outlet's cap (`outletcap`): a flat iron plate set flush in the curb's seat."""
    (x0, x1), _, (z0, z1) = CAP_SEAT
    return [box([x0, CAP_Y[0], z0], [x1, CAP_Y[1], z1], "outletcap_plate", "outletcap", "iron")]


COAL_TOP = 12.2
LUMP_TURNS = ((("y", 28.0), ("x", 21.0)), (("y", 63.0), ("z", 17.0)), (("y", 11.0), ("x", -24.0)), (("y", 47.0), ("z", -19.0)))


def build_coal(kind):
    """The coal on the grate (`coal`), or the same burning (`fire`, kind "fire": the ember texture, glowing): a bed
    across the fireplace from the bars up, lumps on its top; burning, cinders fallen through onto the cave's floor."""
    p = kind
    tex = "coal" if kind == "coal" else "ember"
    (x0, x1), (z0, z1) = FIRE_X, CH_Z
    out = [box([x0, GRATE_TOP, z0], [x1, COAL_TOP, z1], f"{p}_bed", p, tex)]
    n = 0
    for x in (x0 + 1.7, x1 - 1.7):
        for z in (z0 + 3.0, (z0 + z1) / 2, z1 - 3.0):
            n += 1
            out.append(lump([x, COAL_TOP + 0.1, z], 1.4, LUMP_TURNS[n % 4], f"{p}_lump{n}", p, tex))
    if kind == "fire":
        for m, (dx, dz, w, d, deg) in enumerate(((1.6, 4.0, 2.4, 1.8, 20.0), (4.6, 11.0, 2.0, 1.6, -35.0), (2.8, 13.5, 1.8, 1.4, 60.0)), 1):
            out.append(bar([x0 + dx, CAVE[1][0] + 0.3, z0 + dz], (w, 0.6, d), (("y", deg),), f"{p}_cinder{m}", p, tex))
    return out


def build():
    els = []
    for kind in ("coal", "fire"):
        els += build_coal(kind)
    els += build_lead() + build_silver() + build_stream() + build_litharge()
    for place in DAMPERS:
        els += build_damper(place)
    els += build_cap() + build_fittings() + build_test()
    return els + build_frame()


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


PART_IDS = STAGES + CONTENTS + tuple(f"damper{d}" for d in DAMPERS) + ("outletcap",)
MATCH = {"brickwork": "bk_*", "lining": "ln_*", "test": "ts_*", "fittings": "ft_*"}
MOVING = ()                                  # nothing moves: the blast comes by pipe


def requires_of(pid):
    """The requires value a part is fitted by (None for the iron frame)."""
    return None if pid == "frame" else pid


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    parts = []
    for pid in PART_IDS:
        parts.append({"id": pid, "match": [MATCH.get(pid, f"{pid}_*")], "requires": requires_of(pid), "drivers": []})
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []})
    for p in parts:
        p["ride"] = None
        for d in p["drivers"]:
            validate_driver(d)
    return [{"id": p["id"], "match": p["match"], "requires": p["requires"], "drivers": p["drivers"], "ride": p["ride"]}
            for p in parts]


# ---------------------------------------------------------------- poses
# A pose is (theta, scene): theta reads nothing (no part moves), and what is fitted. A scene is (the test's state, the damper's place,
# whether the outlet is capped, the build's state): gameplay's requires. The test's contents, the fire and the
# litharge need the furnace complete (the fittings, then the test); the damper is fitted with the fittings, the cap
# once the curb is in (the lining).
DEFAULT_SCENE = ("running", "open", False, "complete")
REST = (0.0, DEFAULT_SCENE)


def inputs_of(pose):
    return {"theta": pose[0], "travel": abs(pose[0])}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


def fitted(scene):
    """The requires values fitted in a scene."""
    state, damper, capped, stage = scene
    out = set(dict(STAGE_STATES)[stage])
    if "fittings" in out:
        out.add(f"damper{damper}")
    if "test" in out:
        out |= set(dict((s[0], s[2]) for s in WORK_STATES)[state])
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
        if not on_show(e.part, pose[1]):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 * (1 + index[e.part]), e.c[2]]
        out.append(e)
    return out


# ---------------------------------------------------------------- the rig file
def in_gap(c):
    """Whether cell c is in the back row's middle and east columns, which are not the furnace's."""
    return all(GAP[k][0] <= c[k] * B < GAP[k][1] for k in range(3))


def footprint():
    """The machine's fourteen cells: the front two rows' twelve and the stack column behind their west end."""
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z) if not in_gap((x, y, z))]


ANCHORS = ("ash", "fireDoor", "door", "dish", "litharge", "tuyere")


def anchor_points():
    """The points gameplay reads (voxels, build frame): the ash mouth's middle in the front face (the damper, worked
    from in front); the fire door's middle (fuel by hand); the working door's middle at the bay's face (charging the
    lead, taking the cake out); the middle of the test's dish (the lead, the cake); the litharge pot's mouth (the
    litharge taken out); the tuyere's mouth in the chamber (the blast: sound, particles)."""
    (mx0, mx1), (my0, my1) = MOUTH
    (fx0, fx1), (fy0, fy1) = FIREDOOR
    (dx0, dx1), (dy0, dy1), _ = DOOR
    return {
        "ash": ((mx0 + mx1) / 2, (my0 + my1) / 2, 0.0),
        "fireDoor": ((fx0 + fx1) / 2, (fy0 + fy1) / 2, 0.0),
        "door": ((dx0 + dx1) / 2, (dy0 + dy1) / 2, 0.0),
        "dish": (TEST_C[0], ASH_TOP, TEST_C[1]),
        "litharge": (POT_C[0], POT_Y[1], POT_C[1]),
        "tuyere": (TUYERE[0], TUYERE[1], TUYERE_Z[0]),
    }


ANCHOR_SIDES = {"ash": "north", "fireDoor": "north", "door": "north", "litharge": "north"}


def outlet_cell():
    return tuple(int(v // B) for v in ((STACK[0][0] + STACK[0][1]) / 2, TOP - 1.0, (STACK[2][0] + STACK[2][1]) / 2))


MIN_CHIMNEY = 6                              # chimney blocks stood on the outlet, counted from it up (the crucible
#                                              furnace's MinChimneyHeight; the stack's base in the furnace does not count)


def box_volume(el):
    return abs(el.size[0] * el.size[1] * el.size[2])


def oct_fill_area(a, b):
    """The area of the octagon inscribed in the oval (a, b), corners at 22.5 + 45 k degrees."""
    pts = [ell(22.5 + 45.0 * k, a, b, (0.0, 0.0)) for k in range(8)]
    return abs(sum(pts[i][0] * pts[(i + 1) % 8][1] - pts[(i + 1) % 8][0] * pts[i][1] for i in range(8))) / 2


def test_ash_volume():
    """The test's bone ash, cubic voxels, measured from its shape: the floor (the inscribed octagon), the rim's sides
    (each a slab, less the overlap at its ends' corners, small: counted whole) and the breast."""
    a, b = TEST_A
    vol = oct_fill_area(a, b) * (ASH_TOP - HEARTH_Y)
    for k in range(8):
        if k == FRONT_K:
            continue
        p0, p1 = ell(45.0 * k - 22.5, a, b), ell(45.0 * k + 22.5, a, b)
        vol += math.dist(p0, p1) * BAND[0] * (BAND[1] - ASH_TOP)
    (bx0, bx1), (bz0, bz1) = BREAST_X, BREAST_Z
    vol += (bx1 - bx0) * (FRONT_CHORD_Z - bz0) * (ASH_TOP - HEARTH_Y)
    vol += (bx1 - bx0 - 2 * GATE) * (bz1 - bz0) * (RING_TOP - ASH_TOP)
    return vol


def build_volumes(els):
    """The brickwork's and the lining's volumes, in blocks, measured from their brick boxes (each lattice box in
    exactly one)."""
    bk = sum(box_volume(e) for e in els if e.name.startswith("bk_brick_"))
    ln = sum(box_volume(e) for e in els if e.name.startswith("ln_brick_"))
    return bk / B ** 3, ln / B ** 3


def build_counts(els):
    """What the brickwork, the lining and the test take, from their volumes: fired bricks at BRICKS_PER_BLOCK,
    refractory bricks at LINING_PER_BLOCK, fire clay one per LINING_PER_FIRECLAY of those, bone meal at the cupel's
    rate, each rounded up."""
    bk, ln = build_volumes(els)
    bricks = math.ceil(bk * BRICKS_PER_BLOCK - 1e-9)
    lining = math.ceil(ln * LINING_PER_BLOCK - 1e-9)
    ash = test_ash_volume()
    return {"brickVolume": bk, "liningVolume": ln, "bricks": bricks, "lining": lining,
            "fireclay": math.ceil(lining / LINING_PER_FIRECLAY - 1e-9), "ashVolume": ash / B ** 3,
            "bonemeal": math.ceil(ash * BONEMEAL_PER_VOXEL - 1e-9)}


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
            {"requires": "fittings", "also": [f"damper{d}" for d in DAMPERS], "materials": materials(FITTINGS_STAGE)},
            {"requires": "test", "volumeBlocks": r6(c["ashVolume"]), "perClick": "stack",
             "materials": materials(((BONEMEAL, c["bonemeal"]),)),
             "wearing": {"charges": TEST_CHARGES, "lead": TEST_LEAD,
                         "renew": materials(((BONEMEAL, c["bonemeal"]),))}},
        ],
        "outletCap": materials((CAP_ITEM,))[0],
        "_comment": "The build's stages, in order (a proposal: the owner settles them). frame: the iron frame's grid "
                    "recipe (with a hammer); placing it puts down the controller and its ghosts, and draws the frame "
                    "part. Each stage then fits its requires when its materials are all in: brickwork, lining (their "
                    "volumeBlocks measured from the model; 2 fired bricks or 4 refractory bricks a block, rounded up, "
                    "fire clay one per 2.5 refractory bricks; oneType: the first fired brick sets the furnace's type, "
                    "bricks), fittings (the fire door, the gutter, the litharge pot, the test's iron ring and the copper "
                    "blast stub; also: "
                    "the damper's places, one fitted), then test, the wearing part, as the cylinder borer's cutter "
                    "head: the bone ash beaten into the ring, measured from the model at the bone-ash cupel's rate, 4 "
                    "bone meal for 41 cubic voxels. wearing: the test is spent after charges charges (about lead units "
                    "of lead) and broken out of the ring, the furnace stopped until a new one is beaten in; renew: what "
                    "a new test takes. outletCap: an item interaction, not a stage. The test's contents, the "
                    "fire and the litharge are drawn once the test is in, the damper once the fittings are, the cap "
                    "once the lining is."}


def bricks_json():
    return {"code": "brick", "item": "game:burnedbrick-{type}", "default": BRICK_DEFAULT,
            "types": {t: {"block": block, "base": base, **({"overlays": [over]} if over else {})}
                      for t, block, base, over in BRICKS},
            "_comment": "The fired brick types the brickwork takes (every game:burnedbrick type), each with the block "
                        "its bricks make and that block's texture: the renderer sets the shape's 'brick' code to the "
                        "furnace's type's base, with its overlays drawn over it, as the crucible furnace's does. A type "
                        "missing here (another mod's) is refused."}


def make_rig(parts):
    sx, sz = (STACK[0][0] + STACK[0][1]) / 2, (STACK[2][0] + STACK[2][1]) / 2
    pts = anchor_points()
    rig = {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the front middle "
                    "cell at ground level, the litharge bay's. An English cupellation (test) furnace: the test and the "
                    "fireplace side by side in the front two rows, the stack in a column behind their west end. "
                    "Furnaces stand side by side in a row along x (up to furnace.rowMax), sharing one chimney on one "
                    "member's outlet, the others capped. No mechanical power: the blast comes by pipe, air in the ppex "
                    "pipe network (Steelmaking Expanded's engine air blower makes it) taken at blastCell's blastFace, "
                    "where the wind box's socket is, and blown by the tuyere across the test. Natural draught for the "
                    "fire, in at the ash mouth (ash, ashSide) under the damper. Nothing moves. requires: the build's "
                    "stages, brickwork, lining, fittings and test, the wearing part (build; the frame, iron, has none); coal, fire, lead, "
                    "silver, stream, litharge (the test's and the fire's state); the damper's place, damperopen, "
                    "damperhalf or dampershut (one at a time); outletcap. The brickwork's texture is the furnace's "
                    "brick type's (bricks). door: the working door, where the lead is charged and the cake taken out "
                    "(over dish, the middle of the test's dish); fireDoor: fuel by hand; litharge: the pot the litharge "
                    "runs into, emptied by hand; tuyere: the blast's mouth. outletCell: the stack's top cell, the "
                    "chimney stood on its top; chimney: the shortest chimney, counted from the outlet up. See "
                    "CupellationFurnace/README.md.",
        "cells": [],
        "blastCell": list(BLAST_CELL),
        "blastFace": BLAST_FACE,
        "outletCell": list(outlet_cell()),
        "outletFace": "up",
    }
    for key in ANCHORS:
        rig[key] = {"pos": pt(*pts[key])}
        if key in ANCHOR_SIDES:
            rig[f"{key}Side"] = ANCHOR_SIDES[key]
    rig.update({
        "chimney": {"origin": pt(sx, TOP + MIN_CHIMNEY * B / 2, sz), "axis": "y", "length": float(MIN_CHIMNEY)},
        "build": build_json(),
        "bricks": bricks_json(),
        "furnace": {"minChimney": MIN_CHIMNEY, "rowMax": ROW_MAX, "rowAxis": "x", "blastGas": "Air",
                    "blastRate": BLAST_RATE, "blastPressure": BLAST_PRESSURE,
                    "_comment": "minChimney: chimney blocks stood on the outlet, counted from it up; rowMax, rowAxis: "
                                "furnaces in a row, side by side along x, sharing one chimney; blastGas: the gas the "
                                "tuyere takes from the ppex pipe network at blastFace (exlib's PipeNetwork, network type "
                                "'pipe'; Steelmaking Expanded's engine air blower produces it), one fixed connector on "
                                "the back face, as Steelmaking Expanded's Bessemer converter's intake port; blastRate: "
                                "litres a second drawn; blastPressure: the least pressure (atm) the furnace runs at "
                                "(#740's ruling)."},
        "parts": parts,
    })
    return rig


# ---------------------------------------------------------------- shipped
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
    for key in ("blastCell", "outletCell"):
        ship[key] = shift_cell(rig[key], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["chimney"] = {**rig["chimney"], "origin": shift_point(rig["chimney"]["origin"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


CELL_PARTS = ("frame",) + STAGES   # the cells' boxes: the furnace built


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written (the furnace built, the contents aside); a lid (a deck) on
    every top cell at the top."""
    by_cell = {}
    for w in flatten(shape["elements"], textures={}):
        pid = part_of(ship_parts, w.name)
        if pid not in CELL_PARTS:
            continue
        el = posed(w, _part_matrix(ship_parts, pid, inputs_of(REST)))
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
    poses = [REST, (0.7, DEFAULT_SCENE), (2.9, DEFAULT_SCENE), (-4.1, DEFAULT_SCENE)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; {len(cells)} cells")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- shape files
GLOWS = {"fire": FIRE_GLOW, "lead": LEAD_GLOW, "silver": SILVER_GLOW, "stream": STREAM_GLOW}


def shape_json(els):
    shape = machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Texture codes the renderer "
             "sets: 'brick' (the common brickwork: the furnace's brick type's block texture, from the rig's bricks; "
             "the shape's own is the red brick's colour layer). The burning coal ('ember'), the molten lead, the silver "
             "cake and the running litharge glow. Keep element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)
    for e in shape["elements"]:
        glow = GLOWS.get(e["name"].split("_")[0], 0)
        if glow:
            for f in e["faces"].values():
                f["glow"] = glow
    return shape


def coplanar_scenes():
    scenes = [(st, "open", False, "complete") for st in ("running", "brightened", "coal")]
    scenes += [("cold", "half", True, "complete"), ("cold", "shut", True, "complete")]
    scenes += [("cold", "open", False, s) for s in ("frame", "brickwork", "fittings")]
    scenes += [("cold", "open", True, "lining")]
    return scenes


def coplanar_poses():
    return tuple((0.0, sc) for sc in coplanar_scenes())


def fix_coplanar(els, parts):
    def posed_fn(es, pose):
        return shown(parts, [posed(el, pm(parts, el.part, pose)) for el in es], pose)
    return fix_coplanar_posed(els, posed_fn, coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the cupellation furnace's shapes and rig.")
    ap.add_argument("--out", type=Path, help="write the three files into this directory instead of the mod's assets")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_cupellationfurnace
    els = build()
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        print(f"coplanar faces before the fix: {sum(len(p) for p in before.values())} pairs over {len(before)} poses")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_cupellationfurnace.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    names = ("cupellationfurnace.json", "cupellationfurnace_frame.json", "cupellationfurnace-rig.json")
    if args.out:
        outs = tuple(args.out / n for n in names)
    else:
        outs = (SHAPE_DIR / names[0], SHAPE_DIR / names[1], RIG_DIR / names[2])
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_cupellationfurnace.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship))
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
