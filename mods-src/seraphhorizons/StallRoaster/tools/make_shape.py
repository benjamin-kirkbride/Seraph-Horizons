#!/usr/bin/env python3
"""Generate the stall roaster's shapes, rig and reference poses.

The stall roaster is one roasting stall of the late nineteenth century (Peters 1895, ch. VI; Hofman 1904, figs.
281-284): a three-sided bay of brickwork, open at the front and to the sky, two blocks wide, two deep and two high.
The bay is the front row: its floor and the inner faces of its side walls and its back wall are lined with tier-3
refractory brick; the back row is the back wall, with the flue in it: a flue mouth at the foot of the bay's back face,
centred, leads into a back flue low in the wall and up a stack to an outlet on the top over the stack, where the
player stands the chimney (or a flat iron cap, as on every stall in a row but one). The brickwork is any one type of
fired brick (its texture the renderer's, from the rig's bricks). The front's iron is a frame: an angle up each jamb
of the opening, a sill across its foot and a tie bar across its top; the back has a strap up each end and a band
along its top edge. The side faces are plain brick, so stalls stand side by side in a row along their width, their
side walls meeting.

The charge is laid by hand through the open front: a bed of firewood (logs across the bay and sticks along it), the
ore on it in four courses of eight rock (32, two stacks), each course stepped back at the front. It is kindled at the
bed and then roasts for days on its own sulfur; the front is walled up with loose fired brick, open, half or whole
(its bottom course laid with two draught holes), which sets the draught; the roasted rock is raked out through the
front. Sulfur condenses in the cooler crust on the heap's top, where it is taken out by hand when the stall is cold.
Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    stallroaster.json          the whole stall, every part that comes and goes            (assets/.../shapes/block/)
    stallroaster_frame.json    the iron frame only (block and item)                       (assets/.../shapes/block/)
    stallroaster-rig.json      cells, anchors, the build and the part rig                 (assets/.../config/)
    rig-reference.json         every part's matrix at a grid of poses                     (tests/StallRoaster/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_stallroaster.py) and exits non-zero if
a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the machine
box's north-west-bottom corner. The bay's open front is north; the back wall, the flue and the outlet are south, the
outlet over the west back cell. The controller cell is the front cell at ground level under the outlet's column, build
cell (0, 0, 0), which the shipped files keep at [0,0,0].

Nothing in the stall moves: it takes no power and no input drives any part. Everything that shows or not (the
build's stages, the brickwork and the lining; the wood bed, cold or burning; the four courses of the charge, cold,
burning or roasted; the sulfur; the front's walling, half or whole; the outlet's cap) is a `requires`: gameplay's
state, not a motion. The iron frame has none.
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
REFERENCE_OUT = MOD / "tests" / "StallRoaster" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/StallRoaster/tools/make_shape.py"

B = 16.0

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 2, 2, 2          # two wide (x, the row's way), two high, two deep (z)
ORIGIN_CELL = (0, 0, 0)                      # the controller: the front cell at ground level under the outlet's column
# The cell over the outlet is the chimney's; nothing of the stall reaches it, nor the cells beside it or over the bay.
CHIMNEY_CELL = (0, 2, 1)
FREE_CELLS = ((1, 2, 1), (-1, 2, 1), (0, 2, 0), (1, 2, 0))
ROW_MAX = 6                                  # stalls in a row, side by side along x, sharing one chimney (#926)

TEXTURES = {
    "brick": "game:block/clay/brick/four/running/red1",     # the brickwork and the loose walling: the red brick's colour (BRICKS)
    "lining": "game:block/clay/refractory/tier3/front1",    # the hot faces, the flue and the outlet's curb: tier-3 refractory
    "iron": "game:block/metal/plate/iron",
    "wood": "game:block/wood/firepit/log",                  # the bed of firewood (the firepit's logs)
    "ore": "game:item/resource/crushed/galena",             # the charge: raw or crushed sulfide rock; the renderer may set it
    "ember": "game:block/coal/ember",                       # the bed and the charge burning
    "roasted": "game:item/resource/crushed/hematite",       # the charge roasted: the sulfide burnt to oxide, red-brown
    "sulfur": "game:item/resource/crushed/sulfur",          # the sulfur condensed in the heap's crust
}
TEX = 16                                     # shape texture units: one a voxel, one texture across a block face
KINDLING_GLOW = 200                          # the wood bed burning (the game's lit firepit embers are 160)
HEAP_GLOW = 120                              # the charge smouldering on its own sulfur: a dull glow

W, H, D = CELLS_X * B, CELLS_Y * B, CELLS_Z * B
TOP = H                                      # the walls' tops: flat

# ---------------------------------------------------------------- the bay
LINING_T = 3.0                               # tier-3 refractory brick on every hot face
WALL_T = 6.0                                 # each side wall: 3 of brickwork outside 3 of lining
BAY_X = (WALL_T, W - WALL_T)                 # 20 wide between the linings: five loose bricks' length, in courses
BAY_Z = (0.0, B)                             # the front row: open at the front, its back the back wall's lining
BACK_MIN = 6.0                               # the back wall behind its flue: 3 of brickwork outside 3 of lining
BASE_TOP = 2.0                               # the brickwork's base under the floor
FLOOR_Y = BASE_TOP + LINING_T                # the bay's floor: the lining's, on the base

# the flue: a mouth at the foot of the bay's back face, centred, through the lining into a back flue low in the back
# wall, which runs west under the stack; the stack rises in the west back cell to the outlet on the top
MOUTH = ((12.0, 20.0), (FLOOR_Y, 10.0), (BAY_Z[1], 20.0))
BACK_FLUE = ((6.0, 20.0), (FLOOR_Y, 10.0), (20.0, 26.0))
STACK = ((6.0, 12.0), (FLOOR_Y, TOP), (20.0, 26.0))
CURB = ((4.0, 14.0), (TOP - 2.0, TOP), (18.0, 28.0))  # the outlet's curb, of the lining's refractory brick, flush:
CURB_RIM = 1.0                                         # a rim 1 wide round the cap's seat, the seat a ledge 1 wide
CAP_Y = (TOP - 0.5, TOP)                     # round the stack's mouth; the cap, a flat iron plate, set flush in it
CAP_SEAT = ((CURB[0][0] + CURB_RIM, CURB[0][1] - CURB_RIM), CAP_Y, (CURB[2][0] + CURB_RIM, CURB[2][1] - CURB_RIM))

# ---------------------------------------------------------------- the iron
IRON_T = 0.5                                 # plates and angles set into the brick, flush
JAMB_X = ((0.5, WALL_T), (W - WALL_T, W - 0.5))   # each jamb angle's front leg, over the side wall's front face
JAMB_LEG = 2.5                               # its inner leg, along the opening's side face, flush in the lining
JAMB_TOP = 30.0                              # the jambs stand up to the tie bar
TIE_Y = (JAMB_TOP, TOP)                      # the tie bar across the top of the opening, its ends let into the walls
TIE_Z = (0.0, 1.5)
TIE_X = (0.5, W - 0.5)
SILL_D = 2.5                                 # the sill angle across the opening's foot: its top leg, flush in the floor
STRAP_X = ((0.5, 3.0), (W - 3.0, W - 0.5))   # the back: a flat strap up each end
KERB_Y = (TOP - 1.5, TOP)                    # and a band along its top edge between them

# ---------------------------------------------------------------- the front's walling
# Loose fired brick, dry-laid in the opening in courses of the brick texture's own size (8 long, 4 high, 4 deep), in
# running bond: whole bricks and halves, 0.2 apart, each set back a little more or less (JITTER) so the wall reads as
# loose. The bottom course has two draught holes. Half walled is three courses; walled up, six, to a voxel under the
# tie bar.
FRONTS = ("open", "half", "walled")          # the front's three places; open has no walling
FRONT_COURSES = {"half": 3, "walled": 6}
BRICK_H = 4.0                                # a course: the texture's own courses are 4 voxels
BRICK_L = 8.0
FRONT_Z = (0.5, 4.5)                         # the walling's thickness, behind the jambs' front legs
BRICK_GAP = 0.2
JITTER = (0.0, 0.15, -0.1, 0.1, 0.2, -0.05, 0.05, -0.1, 0.15)   # each brick's set back (z), in turn
# the courses' bricks, (x0, x1) across the opening (BAY_X 6..26): the bottom course with two draught holes, then
# running bond, halves at the ends
DRAUGHT_COURSE = ((6.0, 10.0), (12.0, 20.0), (22.0, 26.0))
BOND = (((6.0, 14.0), (14.0, 22.0), (22.0, 26.0)), ((6.0, 10.0), (10.0, 18.0), (18.0, 26.0)))
# one brick of the texture per face, its mortar cropped: the brick it takes (u, v) in turn
BRICK_UV = ((0.0, 0.0), (8.0, 0.0), (4.0, 4.0), (0.0, 8.0), (8.0, 8.0), (4.0, 12.0))

# ---------------------------------------------------------------- the charge
LOGS = 4                                     # the bed: four logs across the bay (along x)
LOG_H, LOG_D = 2.5, 2.2
STICKS = (7.5, 11.0, 15.25, 19.5, 23.0)      # and five sticks along it (their west sides, x), on the logs
STICK_W, STICK_H = 1.5, 1.5
BED_Z = (4.75, 15.75)                        # the bed: behind the walling, clear of the back wall
BED_X = (BAY_X[0] + 0.25, BAY_X[1] - 0.25)
BED_TOP = FLOOR_Y + LOG_H + STICK_H
COURSES = ((BED_TOP, 13.5), (13.5, 18.0), (18.0, 22.5), (22.5, 26.5))   # four courses of eight rock, from the bed up
COURSE_FRONT = (4.75, 5.5, 6.75, 8.0)        # each stepped back at the front (its front face, z)
TOP_COURSE_X = (BAY_X[0] + 1.0, BAY_X[1] - 1.0)   # the top course mounded: in from the walls
COURSE_BACK = BAY_Z[1]                       # the courses lie against the back wall's lining (over the flue mouth)
CHARGE = 32                                  # rock: two stacks (#925)
PER_COURSE = CHARGE // len(COURSES)          # 8 a course: gameplay's rock count -> courses shown
LUMP = 1.6                                   # the lumps lying on each course's top
LUMP_TURNS = ((("y", 28.0), ("x", 21.0)), (("y", 63.0), ("z", 17.0)), (("y", 11.0), ("x", -24.0)), (("y", 47.0), ("z", -19.0)))
# the sulfur: pools in the top course's crust, between its lumps, (x, z) of each and its size (x, z)
SULFUR_POOLS = (((13.0, 11.55), (2.4, 1.0)), ((19.0, 11.55), (2.4, 1.0)))
SULFUR_T = 0.15

# ---------------------------------------------------------------- the build
# Built in stages (the rig's `build`; their requires values are the viewer's Build states): the iron frame, a grid
# recipe placed as the machine (the controller and its ghosts), then the brickwork and the lining. The counts are
# measured from the model's volume at the game's rates: a claybricks block is 2 fired bricks (8 and a mortar make 4),
# a refractory brick block 4 (8 and a mortar make 2), rounded up; the lining's fire clay is one for every
# LINING_PER_FIRECLAY of its bricks, rounded up. The front's walling is not a stage: loose bricks of the stall's own
# type, one a brick drawn, kept by the stall once given (FRONT_BRICKS), as the outlet's cap is put on by hand.
FRAME_RECIPE = (("game:metalplate-iron", 2), ("game:rod-iron", 2), ("game:metalnailsandstrips-iron", 6))
BRICKS_PER_BLOCK = 2
LINING_PER_BLOCK = 4
LINING_PER_FIRECLAY = 2.5
FIRED_BRICK = "game:burnedbrick-*"
LINING_BRICK = "game:refractorybrick-fired-tier3"
FIRECLAY = "game:clay-fire"
CAP_ITEM = ("game:metalplate-iron", 1)
STAGES = ("brickwork", "lining")             # the stages after the frame, in order: their requires values
STAGE_STATES = (                             # the viewer's Build select: (id, the stages fitted)
    ("frame", ()),
    ("brickwork", ("brickwork",)),
    ("complete", ("brickwork", "lining")),
)

# The fired bricks the brickwork takes: game:burnedbrick-{type}, every type the game has (its burnedbrick item type,
# in its order), each with the block its bricks make and that block's texture, as the crucible furnace's
# (CrucibleFurnace/tools/make_shape.py, BRICKS). (type, block, base, overlay)
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

# The scenario's states of the bay's contents, as the viewer gives them (site/models.json; tools/tests holds the two
# together). Each is the requires values fitted.
COURSE_IDS = tuple(f"ore{j}" for j in range(1, len(COURSES) + 1))
FIRE_IDS = tuple(f"fire{j}" for j in range(1, len(COURSES) + 1))
ROASTED_IDS = tuple(f"roasted{j}" for j in range(1, len(COURSES) + 1))
HEAP_STATES = (
    ("empty", "Swept and empty", ()),
    ("bed", "The wood bed laid", ("wood",)),
    ("charged", "Charged: 32 rock in four courses on the bed", ("wood",) + COURSE_IDS),
    ("kindled", "Kindled: the bed lit under the charge", ("kindling",) + COURSE_IDS),
    ("roasting", "Roasting on its own sulfur", FIRE_IDS),
    ("roasted", "Roasted and cold, sulfur in the crust", ROASTED_IDS + ("sulfur",)),
    ("raked", "Half raked out", ROASTED_IDS[:2]),
    ("toplit", "Native sulfur rock, lit from above", COURSE_IDS[:3] + FIRE_IDS[3:]),
)
HEAP_STATE_IDS = tuple(s[0] for s in HEAP_STATES)
CONTENTS = ("wood", "kindling") + COURSE_IDS + FIRE_IDS + ROASTED_IDS + ("sulfur",)


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
    side continue one texture across a block face with no seam (the brickwork is many boxes). The directions are
    the game's (CubeMeshUtil, as the mandrel station's banding reads them): a side face's u runs left to right as
    seen from outside it (south with +x, north against it, west with +z, east against it) and its v down from the
    block's top; up runs u with +x and v with +z, down u against x and v with +z."""
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


def brick_skin(el, n):
    """A loose brick: every face shows one brick of the texture, its mortar cropped, the n-th of BRICK_UV."""
    u, v = BRICK_UV[n % len(BRICK_UV)]
    t = "#brick"
    sx, sy, sz = (abs(s) for s in el.size)
    long_u = min(sx, BRICK_L) / BRICK_L * 7.5
    end_u = min(sz, BRICK_H) / BRICK_H * 3.5
    el.faces = {d: {"texture": t, "uv": [u, v, u + (long_u if d in ("north", "south", "up", "down") else end_u), v + 3.5]}
                for d in AXES_UV}
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


def lump(c, size, turns, name, part, tex):
    """A small cube turned about its own centre by `turns` [(axis, degrees), ...] in order."""
    el = box([c[k] - size / 2 for k in range(3)], [c[k] + size / 2 for k in range(3)], name, part, tex)
    for axis, deg in turns:
        rotate([el], axis, deg, c)
    return el


# ---------------------------------------------------------------- the iron frame
def iron_pieces():
    """The iron frame, as (name, lo, hi): at the front, an angle up each jamb of the opening (its front leg over the
    side wall's front face, its inner leg along the opening's side, both flush), a sill angle across the opening's
    foot (its top leg flush in the floor, its front leg in the floor's front face) and a tie bar across the top of the
    opening, its ends let into the side walls' tops; at the back, a flat strap up each end and a band along the top
    edge between them. The side faces are left plain brick: a row's neighbours meet there."""
    out = []
    for side, (x0, x1) in zip(("w", "e"), JAMB_X):
        out.append((f"fr_jamb_{side}_front", [x0, 0.0, 0.0], [x1, JAMB_TOP, IRON_T]))
        lx = (BAY_X[0] - IRON_T, BAY_X[0]) if side == "w" else (BAY_X[1], BAY_X[1] + IRON_T)
        out.append((f"fr_jamb_{side}_side", [lx[0], FLOOR_Y, IRON_T], [lx[1], JAMB_TOP, JAMB_LEG]))
    out += split_blocks("fr_sill_top", [BAY_X[0], FLOOR_Y - IRON_T, 0.0], [BAY_X[1], FLOOR_Y, SILL_D])
    out += split_blocks("fr_sill_front", [BAY_X[0], BASE_TOP, 0.0], [BAY_X[1], FLOOR_Y - IRON_T, IRON_T])
    out += split_blocks("fr_tiebar", [TIE_X[0], TIE_Y[0], TIE_Z[0]], [TIE_X[1], TIE_Y[1], TIE_Z[1]])
    for side, (x0, x1) in zip(("w", "e"), STRAP_X):
        out += split_blocks(f"fr_strap_{side}", [x0, 0.0, D - IRON_T], [x1, H, D])
    out += split_blocks("fr_kerb", [STRAP_X[0][1], KERB_Y[0], D - IRON_T], [STRAP_X[1][0], KERB_Y[1], D])
    return out


def build_iron():
    """The iron frame (`frame`): what the frame stage puts down, all of it standing on its own, set into the brick
    once it is in."""
    return [box(lo, hi, name, "frame", "iron") for name, lo, hi in iron_pieces()]


# ---------------------------------------------------------------- the brickwork
def brick_voids():
    """Every space cut out of the brick, as (lo, hi) in voxels: the bay (open at the front and the top, its floor
    the lining's); the flue mouth, the back flue and the stack, and the cap's seat in the curb; and the room for the
    iron set into the brick. The top is flat but for the bay and the outlet."""
    v = [([BAY_X[0], FLOOR_Y, BAY_Z[0]], [BAY_X[1], TOP, BAY_Z[1]])]
    for (x0, x1), (y0, y1), (z0, z1) in (MOUTH, BACK_FLUE, STACK, CAP_SEAT):
        v.append(([x0, y0, z0], [x1, y1, z1]))
    v += [(lo, hi) for _, lo, hi in iron_pieces()]
    return v


def lining_zones():
    """Where the brick is the lining's, as (lo, hi): LINING_T round the bay (its floor, the side walls' inner faces and
    the back wall's), and round the flue (the mouth, the back flue and the stack, the stack's to the top: the outlet's
    curb is the lining's). The rest of the brick is the brickwork's: the base, the side walls' outer halves, the back
    wall."""
    t = LINING_T
    (mx0, mx1), (_, my1), (mz0, mz1) = MOUTH
    (fx0, fx1), (_, fy1), (fz0, fz1) = BACK_FLUE
    (sx0, sx1), _, (sz0, sz1) = STACK
    return [([BAY_X[0] - t, BASE_TOP, BAY_Z[0]], [BAY_X[1] + t, TOP, BAY_Z[1] + t]),          # the bay's U
            ([mx0 - t, BASE_TOP, mz0], [mx1 + t, my1 + t, mz1]),                               # the mouth
            ([fx0 - t, BASE_TOP, fz0 - t], [fx1 + t, fy1 + t, fz1 + t]),                       # the back flue
            ([sx0 - t, BASE_TOP, sz0 - t], [sx1 + t, TOP, sz1 + t])]                           # the stack


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
    with it is left out (inside the wall): a brickwork face against brickwork, a lining face against either (the
    lining goes in after the brickwork). A brickwork face against the lining is kept: until the lining is in, it is
    the wall's face."""
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


def build_frame():
    """Everything that stands still and is not the bay's contents: the brick (brickwork and lining) and the iron
    frame."""
    return build_brick() + build_iron()


# ---------------------------------------------------------------- the charge
def bed_pieces():
    """The bed of firewood, as (name, lo, hi): four logs across the bay, and five sticks along it lying on them."""
    out = []
    z0, z1 = BED_Z
    pitch = (z1 - z0 - LOG_D) / (LOGS - 1)
    for i in range(LOGS):
        a = z0 + pitch * i
        out.append((f"log{i + 1}", [BED_X[0], FLOOR_Y, a], [BED_X[1], FLOOR_Y + LOG_H, a + LOG_D]))
    for i, x in enumerate(STICKS, 1):
        out.append((f"stick{i}", [x, FLOOR_Y + LOG_H, z0], [x + STICK_W, BED_TOP, z1]))
    return out


def build_bed(kind):
    """The bed (`wood`), or the same bed burning (`kindling`, kind "kindling": the ember texture, its faces
    glowing)."""
    tex = "wood" if kind == "wood" else "ember"
    return [box(lo, hi, f"{kind}_{n}", kind, tex) for n, lo, hi in bed_pieces()]


def course_box(j):
    """Course j's box (lo, hi): across the bay from wall to wall (the top course mounded in from them), from its
    front, stepped back, to the back wall's lining."""
    y0, y1 = COURSES[j - 1]
    x0, x1 = TOP_COURSE_X if j == len(COURSES) else BAY_X
    return [x0, y0, COURSE_FRONT[j - 1]], [x1, y1, COURSE_BACK]


def lump_places(j):
    """Where the lumps lie on course j's top: three across, near its front and its back."""
    (x0, _, z0), (x1, _, z1) = course_box(j)
    xs = (x0 + 3.0, (x0 + x1) / 2, x1 - 3.0)
    return [(x, z) for z in (z0 + 1.6, z1 - 2.25) for x in xs]


def build_course(j, kind):
    """The charge's course j (`ore<j>`), the same course burning (`fire<j>`: the ember texture, glowing) or roasted
    (`roasted<j>`): one box from wall to wall, with lumps of the rock lying on its top."""
    p = f"{kind}{j}"
    tex = {"ore": "ore", "fire": "ember", "roasted": "roasted"}[kind]
    lo, hi = course_box(j)
    out = [box(lo, hi, f"{p}_course", p, tex)]
    for n, (x, z) in enumerate(lump_places(j)):
        out.append(lump([x, hi[1] + 0.1, z], LUMP, LUMP_TURNS[(n + j) % 4], f"{p}_lump{n + 1}", p, tex))
    return out


def build_sulfur():
    """The sulfur (`sulfur`): yellow pools in the roasted heap's crust, on the top course between its lumps, where it
    condensed in the cooler top and is taken out by hand."""
    top = COURSES[-1][1]
    out = []
    for n, ((x, z), (sx, sz)) in enumerate(SULFUR_POOLS, 1):
        out.append(box([x - sx / 2, top, z - sz / 2], [x + sx / 2, top + SULFUR_T, z + sz / 2], f"sulfur_pool{n}", "sulfur", "sulfur"))
    return out


# ---------------------------------------------------------------- the front
def front_bricks(place):
    """The walling's bricks in one of the front's places, as (name, lo, hi): FRONT_COURSES[place] courses of loose
    brick on the sill and the floor, in the opening between the jambs; the bottom course with two draught holes."""
    out = []
    n = 0
    for c in range(FRONT_COURSES.get(place, 0)):
        y0 = FLOOR_Y + BRICK_H * c
        row = DRAUGHT_COURSE if c == 0 else BOND[(c - 1) % 2]
        for i, (x0, x1) in enumerate(row, 1):
            j = JITTER[n % len(JITTER)]
            g = BRICK_GAP / 2
            out.append((f"brick{c + 1}{i}", [x0 + g, y0, FRONT_Z[0] + j], [x1 - g, y0 + BRICK_H, FRONT_Z[1] + j]))
            n += 1
    return out


def build_front(place):
    """The front's walling (`front<place>`): loose fired brick in the opening, half (three courses) or whole (six, to
    a voxel under the tie bar). Each place is its own part; gameplay draws the one the front is in. Open has none."""
    p = f"front{place}"
    return [brick_skin(box(lo, hi, f"{p}_{name}", p, "brick"), n) for n, (name, lo, hi) in enumerate(front_bricks(place))]


def build_cap():
    """The outlet's cap (`outletcap`): a flat iron plate set flush in the curb's seat, closing the outlet of a stall
    whose chimney is another's in the row."""
    (x0, x1), _, (z0, z1) = CAP_SEAT
    return [box([x0, CAP_Y[0], z0], [x1, CAP_Y[1], z1], "outletcap_plate", "outletcap", "iron")]


def build():
    els = build_bed("wood") + build_bed("kindling")
    for kind in ("ore", "fire", "roasted"):
        for j in range(1, len(COURSES) + 1):
            els += build_course(j, kind)
    els += build_sulfur()
    for place in FRONTS[1:]:
        els += build_front(place)
    return els + build_cap() + build_frame()


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


FRONT_PARTS = tuple(f"front{p}" for p in FRONTS[1:])
PART_IDS = ("brickwork", "lining") + CONTENTS + FRONT_PARTS + ("outletcap",)
MATCH = {"brickwork": "bk_*", "lining": "ln_*"}


def requires_of(pid):
    """The requires value a part is fitted by (None for the iron frame)."""
    return None if pid == "frame" else pid


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    parts = [{"id": pid, "match": [MATCH.get(pid, f"{pid}_*")], "requires": requires_of(pid), "drivers": []}
             for pid in PART_IDS]
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []})
    for p in parts:
        p["ride"] = None
        for d in p["drivers"]:
            validate_driver(d)
    return [{"id": p["id"], "match": p["match"], "requires": p["requires"], "drivers": p["drivers"], "ride": p["ride"]}
            for p in parts]


# ---------------------------------------------------------------- poses
# A pose is (theta, depth, scene): the rig reads neither (nothing moves), so they are kept only so the pose has the
# shape the shared checks expect. A scene is (the heap's state, the front's place, whether the outlet is capped, the
# build's state): gameplay's requires. The bay's contents and the front's walling are fitted only once the stall is
# built (the lining), the cap once the curb is in (the lining).
DEFAULT_SCENE = ("charged", "half", False, "complete")
REST = (0.0, 0.0, DEFAULT_SCENE)


def inputs_of(pose):
    return {"theta": pose[0], "depth": pose[1]}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


def fitted(scene):
    """The requires values fitted in a scene."""
    state, front, capped, stage = scene
    out = set(dict(STAGE_STATES)[stage])
    if "lining" in out:
        out |= set(dict((s[0], s[2]) for s in HEAP_STATES)[state])
        if front != "open":
            out.add(f"front{front}")
        if capped:
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
    """The stall's eight cells: its two rows, two high."""
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("front", "heap")


def front_point():
    """The middle of the opening in the front face: where the bay is charged, walled up and raked out."""
    return ((BAY_X[0] + BAY_X[1]) / 2, (FLOOR_Y + TOP) / 2, 0.0)


def heap_point():
    """The middle of the full charge's top: its smoke, glow and sound."""
    (x0, _, z0), (x1, y1, z1) = course_box(len(COURSES))
    return ((x0 + x1) / 2, y1, (z0 + z1) / 2)


def outlet_cell():
    return tuple(int(v // B) for v in ((STACK[0][0] + STACK[0][1]) / 2, TOP - 1.0, (STACK[2][0] + STACK[2][1]) / 2))


MIN_CHIMNEY = 6                              # chimney blocks stood on the outlet, counted from it up (the crucible
#                                              furnace's MinChimneyHeight; provisional for the stall)


def box_volume(el):
    return abs(el.size[0] * el.size[1] * el.size[2])


def build_volumes(els):
    """The brickwork's and the lining's volumes, in blocks, measured from their elements (each lattice box in exactly
    one brick box)."""
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


FRONT_BRICKS = len(front_bricks("walled"))   # the walling's loose bricks: one a brick drawn, the walled-up front's


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
        ],
        "front": {"places": list(FRONT_PARTS), "sameType": True, "materials": materials(((FIRED_BRICK, FRONT_BRICKS),)),
                  "half": len(front_bricks("half"))},
        "outletCap": materials((CAP_ITEM,))[0],
        "_comment": "The build's stages, in order. frame: the iron frame's grid recipe (with a hammer); placing it puts "
                    "down the controller and its ghosts, and draws the frame part. Each stage then fits its requires "
                    "when its materials are all in: brickwork, then lining. volumeBlocks: the stage's brick measured "
                    "from the model, in blocks; its quantities are that at 2 fired bricks or 4 refractory bricks a "
                    "block (the game's brick block recipes), rounded up, fire clay one per 2.5 refractory bricks. "
                    "perClick stack: a click puts in as many of the held stack as are still needed. oneType: the first "
                    "fired brick put in sets the stall's brick type (bricks), and the rest must match. front: not a "
                    "stage, the front's loose walling, one fired brick of the stall's own type (sameType) a brick drawn: "
                    "materials for the walled-up front (frontwalled), half of them for the half-walled (fronthalf), "
                    "kept by the stall once given; places: the parts, one drawn at a time, none when open. outletCap: "
                    "an item interaction, not a stage. The bay's contents and the walling are drawn once the lining is "
                    "in, as is the cap (the curb is the lining's)."}


def bricks_json():
    return {"code": "brick", "item": "game:burnedbrick-{type}", "default": BRICK_DEFAULT,
            "types": {t: {"block": block, "base": base, **({"overlays": [over]} if over else {})}
                      for t, block, base, over in BRICKS},
            "_comment": "The fired brick types the brickwork takes (every game:burnedbrick type), each with the block "
                        "its bricks make and that block's texture: the renderer sets the shape's 'brick' code to the "
                        "stall's type's base, with its overlays drawn over it (the brick course's cream brick under "
                        "the type's colour); the front's loose walling takes it too. The stall's type is the first "
                        "brick put in; until then no brick is drawn. A type missing here (another mod's) is refused."}


def make_rig(parts):
    sx, sz = (STACK[0][0] + STACK[0][1]) / 2, (STACK[2][0] + STACK[2][1]) / 2
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the front cell at "
                    "ground level under the outlet's column (west). One roasting stall: a bay of brickwork open at the "
                    "front (north) and to the sky, its back wall (the back row) holding the flue; stalls stand side by "
                    "side in a row along x (up to furnace.rowMax), sharing one chimney on one member's outlet, the "
                    "others capped. No power and nothing moves: no input drives any part. Natural draught, in at the "
                    "front (front, frontSide), through the charge, out at the flue mouth at the foot of the bay's back "
                    "face, along the back flue and up the stack to the outlet. requires: the build's stages, brickwork "
                    "and lining (build; the frame, iron, has none); the bay's contents: wood (the bed) or kindling (the "
                    "bed burning), the charge's four courses ore1..4 (from the bed up), the same burning fire1..4 or "
                    "roasted roasted1..4, and sulfur (in the roasted heap's crust); the front's walling, fronthalf or "
                    "frontwalled (one at a time, none when open); and outletcap. The brickwork's texture is the "
                    "stall's brick type's (bricks), and the walling's. outletCell: the stack's top cell, the chimney "
                    "stood on its top; chimney: the shortest chimney, counted from the outlet up; heap: the full "
                    "charge's top. See StallRoaster/README.md.",
        "cells": [],
        "outletCell": list(outlet_cell()),
        "outletFace": "up",
        "front": {"pos": pt(*front_point())},
        "frontSide": "north",
        "heap": {"pos": pt(*heap_point())},
        "chimney": {"origin": pt(sx, TOP + MIN_CHIMNEY * B / 2, sz), "axis": "y", "length": float(MIN_CHIMNEY)},
        "build": build_json(),
        "bricks": bricks_json(),
        "furnace": {"charge": CHARGE, "courses": len(COURSES), "perCourse": PER_COURSE, "minChimney": MIN_CHIMNEY,
                    "rowMax": ROW_MAX, "rowAxis": "x",
                    "_comment": "charge: rock a full charge holds (two stacks); courses, perCourse: the charge is drawn as "
                                "its courses ore1.. up to ceil(rock / perCourse) (fire.. while it burns, roasted.. once "
                                "roasted); minChimney: chimney blocks stood on the outlet, counted from it up; rowMax, "
                                "rowAxis: stalls in a row, side by side along x, sharing one chimney."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0]."""
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    db = [v / B for v in d]
    ship_els = copy.deepcopy(els)
    for el in ship_els:
        el.c = [el.c[k] + d[k] for k in range(3)]
    ship_parts = copy.deepcopy(parts)
    ship = copy.deepcopy(rig)
    ship["outletCell"] = shift_cell(rig["outletCell"], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["chimney"] = {**rig["chimney"], "origin": shift_point(rig["chimney"]["origin"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


CELL_PARTS = ("frame", "brickwork", "lining")   # the cells' boxes: the stall built, empty
CELL_POSE = (0.0, 0.0, DEFAULT_SCENE)


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed by the shipped rig, the stall built and empty; a lid
    (a deck) on every top cell at the top, so no one falls into the bay."""
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
    poses = [REST, (1.3, 0.7, DEFAULT_SCENE)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; {len(cells)} cells")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
REF_POSES = ((0.0, 0.0), (1.1, 0.5), (-2.3, 1.0))


def reference_json(ship_parts):
    poses = []
    for th, dp in REF_POSES:
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], {"theta": th, "depth": dp})) for q in ship_parts}
        poses.append({"theta": th, "depth": dp, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped stallroaster-rig.json's parts: each part's matrix as 3 "
                        "rows of 4 (block units) at each pose. Nothing in the stall moves, so every matrix is the "
                        "identity whatever the inputs; the site's and the tools' tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    shape = machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Texture codes the renderer "
             "sets: 'brick' (the brickwork and the front's loose walling: the stall's brick type's block texture, from "
             "the rig's bricks; the shape's own is the red brick's colour layer), 'ore' and 'roasted' (the charge's "
             "rock, raw and roasted). The burning bed ('kindling') and the burning charge ('fire1'..'fire4') glow. "
             "Keep element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)
    for e in shape["elements"]:
        stem = e["name"].split("_")[0]
        glow = KINDLING_GLOW if stem == "kindling" else HEAP_GLOW if stem.startswith("fire") else 0
        if glow:
            for f in e["faces"].values():
                f["glow"] = glow
    return shape


def coplanar_poses():
    scenes = [(st, "open", False, "complete") for st in HEAP_STATE_IDS]
    scenes += [(st, "walled", True, "complete") for st in ("charged", "roasted")]
    scenes += [("charged", "half", False, "complete"), ("empty", "walled", False, "complete")]
    scenes += [("empty", "open", False, "frame"), ("empty", "open", False, "brickwork")]
    return tuple((0.0, 0.0, sc) for sc in scenes)


def fix_coplanar(els, parts):
    def posed_fn(es, pose):
        return shown(parts, [posed(el, pm(parts, el.part, pose)) for el in es], pose)
    return fix_coplanar_posed(els, posed_fn, coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the stall roaster's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_stallroaster
    els = build()
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        print(f"coplanar faces before the fix: {sum(len(p) for p in before.values())} pairs over {len(before)} poses")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_stallroaster.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "stallroaster.json", args.out / "stallroaster_frame.json", args.out / "stallroaster-rig.json",
                args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "stallroaster.json", SHAPE_DIR / "stallroaster_frame.json", RIG_DIR / "stallroaster-rig.json",
                REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_stallroaster.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
