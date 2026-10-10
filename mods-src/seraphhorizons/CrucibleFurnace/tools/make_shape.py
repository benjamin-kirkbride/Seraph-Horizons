#!/usr/bin/env python3
"""Generate the crucible furnace's shapes, rig and reference poses.

The crucible furnace is one Huntsman (Sheffield) melting hole of the late nineteenth century, built whole as one
machine: a block of tier-3 refractory brickwork two blocks wide, two deep and two high, bound with iron on its front
and back, with one hole set into its top at the front. The hole is lined with firebrick and holds two fireclay pots
side by side, each on a fireclay stand on a grate of iron firebars, packed round in one bed of coke up to their
shoulders. A hinged lid (firebrick in an iron frame, with a handle) closes the hole flush with the floor; it is hinged
on a pin at its back edge, in a hinge pit behind the hole, and swings up to rest just past upright against an iron
stop at the back of the pit. Under the grate is the ash pit, open at the front through the ash mouth, which an iron
damper slides over (open, half, shut): the cellar lad's draught control, as firing was natural draught only. Near the
top of the hole's back wall a flue opening leads into a flue in the back row and up a stack to an outlet on the top,
with an iron curb, on which the player stands the chimney; in a row of furnaces sharing one chimney the other outlets
are capped with a flat iron plate set flush in the curb. Furnaces stand side by side in a row along their width,
their plain brick side walls meeting. Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    cruciblefurnace.json          the whole furnace, every part that moves or comes and goes   (assets/.../shapes/block/)
    cruciblefurnace_frame.json    the static frame only (block and item)                       (assets/.../shapes/block/)
    cruciblefurnace-rig.json      cells, anchors and the part rig                              (assets/.../config/)
    rig-reference.json            every part's matrix at a grid of poses                       (tests/CrucibleFurnace/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_cruciblefurnace.py) and exits non-zero
if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the machine
box's north-west-bottom corner. The hole is at the front (north), pot 1 west and pot 2 east; the stack and the outlet
are in the back row (south), the outlet over the east back cell. The controller cell is the front west cell at ground
level, (0, 0, 0), so the shipped files are the build frame divided by 16.

The rig's inputs, as this furnace uses them:

    depth  the lid: 0 open, resting just past upright on its stop (the rest pose), 1 shut. A step rotation about the
           hinge pin.
    theta  the melter's clock: a ratio-0 rotate on the lid carries it and moves nothing (so the viewer offers Play,
           which opens and shuts the lid). Nothing in the furnace turns.

Everything else that shows or not (the two pots, their charges, their melts, the coke in three layers and the same
coke burning, the damper's three places, the outlet's cap) is a `requires`: gameplay's state, not a motion.
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
REFERENCE_OUT = MOD / "tests" / "CrucibleFurnace" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/CrucibleFurnace/tools/make_shape.py"

B = 16.0
OCT = math.tan(math.pi / 8)                  # an octagon's half side over its apothem

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 2, 2, 2          # two wide (x, the row's way), two high, two deep (z)
ORIGIN_CELL = (0, 0, 0)                      # the controller: the front west cell at ground level
# Over the hole: room for the lid, which stands there open. Reserved as hollow cells (ghosts with no boxes of their
# own), so nothing is built where it swings. The cell over the outlet, (1, 2, 1), is the chimney's and the one beside
# it, (0, 2, 1), is free: neither is the machine's, and nothing of it reaches them.
LID_CELLS = ((0, 2, 0), (1, 2, 0))
CHIMNEY_CELL = (1, 2, 1)
FREE_CELL = (0, 2, 1)
ROW_MAX = 6                                  # furnaces in a row, side by side along x, sharing one chimney

TEXTURES = {
    "brick": "game:block/clay/refractory/tier3/front1",    # the old hole's brick: tier-3 refractory
    "iron": "game:block/metal/plate/iron",
    "clay": "game:block/clay/hardened/fire",                # the fired pot's (meltingpot-fired)
    "coke": "game:block/coal/coke",
    "ember": "game:block/coal/ember",                       # the same coke, burning
    "melt": "game:block/metal/ingot/stainlesssteel",        # the melt: the renderer may set it to the pot's metal
    "charge": "game:block/metal/ingot/iron",                # the charge (iron bits and ferrochrome): likewise
}
TEX = 16                                     # shape texture units: one a voxel, one texture across a block face
FIRE_GLOW = 200                              # the burning coke's faces (the game's lit firepit embers are 160)
MELT_GLOW = 255                              # the melt's

W, H, D = CELLS_X * B, CELLS_Y * B, CELLS_Z * B
FLOOR_TOP = H                                # the furnace's top: the melting floor, flat

# ---------------------------------------------------------------- the hole and its pots
POTS = (1, 2)                                # pot 1 west, pot 2 east
POT_X = {1: 10.0, 2: 22.0}                   # each pot's axis (x): 12 apart, 3.4 of coke between their bellies
HOLE_X = (4.0, 28.0)                         # the hole: 24 wide, 1.7 of coke between a belly and a side wall
HOLE_Z = (2.0, 13.6)                         # 11.6 deep, 1.5 of coke before and behind a belly
POT_Z = (HOLE_Z[0] + HOLE_Z[1]) / 2          # the pots' axes (z)
LEDGE = 0.5                                  # the lid's seat: a ledge round the hole's mouth, front and sides
LID_T = 2.0                                  # the lid: 2 thick, flush with the floor when shut
SEAT_Y = FLOOR_TOP - LID_T
SEAT_X = (HOLE_X[0] - LEDGE, HOLE_X[1] + LEDGE)
SEAT_Z = (HOLE_Z[0] - LEDGE, HOLE_Z[1] + LEDGE)
# The hinge: a pin along x through the lid's back bar, 0.4 above the lid's underside and 0.4 in front of its back
# edge, its ends in iron brackets set in the side walls. Behind the lid the hinge pit (to PIT_BACK, its floor PIT_Y)
# gives the lid's heel room as it swings up; an iron stop along the pit's back edge holds the lid open just past
# upright (OPEN_DEG), its weight behind the pin.
PIVOT = (SEAT_Y + 0.4, SEAT_Z[1] - 0.4)      # (y, z)
PIN_R = 0.3
PIT_Y = 29.4
PIT_BACK = 15.6
PIT_FRONT = PIVOT[1] - 0.6                  # the pit reaches under the lid's back bar, where its heel dips as it swings
STOP_Y = 31.4                                # the stop: an iron strip on the pit's back wall, from here to the floor
OPEN_DEG = 91.0                              # the lid open: just past upright
BRACKET = ((2.5, 3.5), (28.5, 29.5), (PIT_Y, 31.4), (PIVOT[1] - 1.0, PIVOT[1] + 1.0))   # x west, x east, y, z
LID_RIM = 1.5                                # the lid's iron frame's width
POCKET = (1.5, 3.5, 1.2)                     # the handle's pocket: half side, its centre from the lid's front, its depth
HANDLE = (0.35, 0.3)                         # the handle bar across the pocket: its half width (z), under the lid's top

# the ash pit (the cave) and the grate
CAVE = ((HOLE_X[0], HOLE_X[1]), (1.0, 9.0), (HOLE_Z[0], HOLE_Z[1]))
MOUTH = ((10.0, 22.0), (1.0, 7.0))           # the ash mouth through the front wall (x, y): the air's way in
LINTEL = ((9.0, 23.0), (7.0, 8.0), (1.0, 2.0))   # the iron lintel over it, behind the damper's recess
RECESS = ((9.0, 23.0), (1.0, 14.2), (0.0, 1.0))  # the damper's recess in the front face, the damper's slide
GUIDES = ((9.0, 10.0), (22.0, 23.0))         # iron guides at the recess's sides (x), its full height and depth
DAMPER_Z = (0.35, 0.85)                      # the damper plate, in the recess, behind the face
DAMPER_H = 6.6                               # its height: shut, it covers the mouth and overlaps its top
DAMPER = {"open": 7.6, "half": 4.0, "shut": 1.0}   # its underside (y) in each place: open clear of the mouth, half
#                                                    over its upper half, shut down on the cave's floor
KNOB = ((15.0, 17.0), (0.1, 0.35), (2.7, 3.9))   # the damper's iron knob (x, z, y over its underside), in the recess
BEARER_Y = (9.0, 10.5)                       # the iron bearers under the firebars, along z, their ends built into the
BEARER_X = ((4.0, 5.5), (15.25, 16.75), (26.5, 28.0))   # front and back walls; one under each end and one under the
BEARER_Z = (HOLE_Z[0] - 1.0, HOLE_Z[1] + 1.0)           # middle, where the bars meet
BAR_Y = (10.5, 12.0)                         # the firebars, along x, two lengths a row (wall to the middle bearer)
BARS, BAR_W = 6, 1.2                         # six rows 1.2 wide across the 11.6: gaps of 0.88
GRATE_TOP = BAR_Y[1]

# the pots and their stands. A pot holds POT_UNITS (PotCapacityUnits, 300 since #768: a firing of the two is 600),
# drawn full: its melt stands where the pot's inside holds POT_UNITS at VOXELS_PER_UNIT (the first pass's figure, when
# a pot the size of the game's meltingpot block, 8 by 11, held 200 with 2.4 to spare). For 300 the pot is a little
# larger, 8.6 across the belly and 12 tall.
STAND = (GRATE_TOP, 14.0, 2.6)               # the fireclay stand on the grate: y0, y1, apothem
POT_BASE = (14.0, 15.0, 3.2)                 # the pot's foot: a solid octagon, wider than the stand
POT_WALL = 0.9
POT_TUBES = (("lower", 15.0, 18.0, 3.9), ("belly", 18.0, 23.5, 4.3), ("upper", 23.5, 26.0, 3.9))
POT_TOP = POT_TUBES[-1][2]
POT_UNITS = 300                              # CrucibleFurnaceSettings.PotCapacityUnits (#768)
VOXELS_PER_UNIT = 1.1                        # a unit of metal in the pot, cubic voxels


def oct_area(a):
    """A regular octagon's area, apothem a."""
    return 8.0 * math.tan(math.pi / 8) * a * a


def pot_inner(y):
    """The pot's inner apothem at height y: its tube's there (the narrower where two meet)."""
    inner = [a - POT_WALL for _, t0, t1, a in POT_TUBES if t0 <= y <= t1]
    return min(inner) if inner else 0.0


def pot_level(units):
    """The height the pot's inside, from its floor (the foot's top) up, holds `units` at VOXELS_PER_UNIT."""
    left = units * VOXELS_PER_UNIT
    for _, t0, t1, a in POT_TUBES:
        cap = oct_area(a - POT_WALL) * (t1 - t0)
        if left <= cap:
            return t0 + left / oct_area(a - POT_WALL)
        left -= cap
    raise ValueError(f"the pot does not hold {units} units")


LEVEL_TOP = round(pot_level(POT_UNITS), 3)
LEVEL = (LEVEL_TOP - 1.0, LEVEL_TOP)         # the charge's top layer, or the melt, inside the pot (y)
LEVEL_A = pot_inner(LEVEL_TOP) + 0.01        # its apothem: a hundredth into the walls, so no gap shows round it

# the coke: one bed round both pots, three layers from the grate to the pots' shoulders (where the belly ends)
COKE_LAYERS = ((GRATE_TOP, 16.0), (16.0, 20.0), (20.0, 23.5))
COKE_PER_LAYER = 2                           # CokeCapacity (6) over the layers: gameplay's coke count -> layers shown
LUMP = 1.5                                   # the coke lumps lying on each layer's top
LUMP_AT = 4.3                                # their place from a pot's axis, along both x and z (and in the middle)

# the flue: one opening from the hole, a flue in the back row, the stack, the outlet over the east back cell
FLUE_OPENING = ((12.0, 20.0), (25.0, 28.0), (HOLE_Z[1], 20.0))   # through the hole's back wall, near its top
BACK_FLUE = ((12.0, 28.0), (23.0, 28.0), (20.0, 28.0))
STACK = ((20.0, 28.0), (23.0, 31.5), (20.0, 28.0))
CURB = ((18.0, 30.0), (30.0, FLOOR_TOP), (18.0, 30.0))   # the outlet's iron curb, flush: a ring to 31.5 round the
CURB_RIM = 1.0                                           # stack, then a rim 1 wide round the cap's seat
CAP_Y = (31.5, FLOOR_TOP)                    # the cap: a flat iron plate set flush in the rim, on the ring

# the iron binding, on the front and back faces only (the sides meet the row's neighbours and are plain brick)
BIND_T = 0.5                                 # set into the brick, flush
STRAP_X = ((0.5, 3.0), (29.0, 31.5))         # a flat strap up each end of the front and the back
KERB_Y = (30.5, FLOOR_TOP)                   # a band along the top edge of the front and the back, between the straps

# The scenario's states of the hole's contents, as the viewer gives them (site/models.json; tools/tests holds the two
# together). Each is the requires values fitted.
POT_STATES = (
    ("cold", "Cold and empty", ()),
    ("pots", "Two pots on their stands", ("pot1", "pot2")),
    ("charged", "Charged, coke packed round", ("pot1", "pot2", "charge1", "charge2", "coke1", "coke2", "coke3")),
    ("lit", "Lit: the charges melting", ("pot1", "pot2", "charge1", "charge2", "fire1", "fire2", "fire3")),
    ("melted", "Melted", ("pot1", "pot2", "melt1", "melt2", "fire1", "fire2", "fire3")),
    ("burntlow", "Melted, the coke burnt low", ("pot1", "pot2", "melt1", "melt2", "fire1")),
    ("pulled", "The pots pulled, the fire still lit", ("fire1", "fire2", "fire3")),
)
POT_STATE_IDS = tuple(s[0] for s in POT_STATES)
DAMPERS = ("open", "half", "shut")
CONTENTS = ("pot1", "pot2", "charge1", "charge2", "melt1", "melt2", "coke1", "coke2", "coke3", "fire1", "fire2", "fire3")


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


def oct_disc(cx, cz, y0, y1, a, name, part, tex):
    """A solid octagonal disc about a vertical axis, apothem `a`, its flats facing the four sides and the diagonals:
    four strips 2a long and one side wide, at 0, 45, 90 and 135 degrees about y (0 and 90 built square to the axes).
    Their union is the octagon exactly."""
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
    the axis (cx, cz): one side of an octagonal ring, or the coke's corner fill. The four square directions are
    built square to the axes."""
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


def oct_rod_x(x0, x1, cy, cz, a, name, part, tex):
    """A solid octagonal rod along x (a pin): four strips turned 0, 45, 90 and 135 degrees about x."""
    s = a * OCT
    out = []
    for i, ang in enumerate((0.0, 45.0, 90.0, 135.0), 1):
        if ang == 90.0:
            el = box([x0, cy - a, cz - s], [x1, cy + a, cz + s], f"{name}_{i}", part, tex)
        else:
            el = box([x0, cy - s, cz - a], [x1, cy + s, cz + a], f"{name}_{i}", part, tex)
            if ang:
                rotate([el], "x", ang, (0.0, cy, cz))
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


def split_cells(name, lo, hi):
    """A box cut at the blocks' boundaries along every axis, as (name, lo, hi): each piece in one block (a world-mapped
    box must be). One piece keeps the name; more are numbered."""
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


# ---------------------------------------------------------------- the lid's open angle and its stop
def stop_edge_z(deg=OPEN_DEG):
    """Where the lid's back face (its top when shut) crosses the floor's level with the lid open `deg` degrees: the
    stop's front edge, which the lid leans on there."""
    a = math.radians(deg)
    top = FLOOR_TOP - PIVOT[0]                   # the lid's top over the pin
    dz = (PIVOT[0] + top * math.cos(a) - FLOOR_TOP) / math.sin(a)
    return PIVOT[1] + top * math.sin(a) + dz * math.cos(a)


STOP_Z = round(stop_edge_z(), 4)


# ---------------------------------------------------------------- the brickwork
def binding():
    """The iron binding, as (name, lo, hi): a flat strap up each end of the front and of the back, and a band along
    the top edge of each between them, all BIND_T thick and flush. The side faces are left plain brick: a row's
    neighbours meet there."""
    out = []
    for face, z0, z1 in (("n", 0.0, BIND_T), ("s", D - BIND_T, D)):
        for side, (x0, x1) in zip(("w", "e"), STRAP_X):
            out.append((f"fr_strap_{face}{side}", [x0, 0.0, z0], [x1, H, z1]))
        out.append((f"fr_kerb_{face}", [STRAP_X[0][1], KERB_Y[0], z0], [STRAP_X[1][0], KERB_Y[1], z1]))
    return [piece for name, lo, hi in out for piece in split_blocks(name, lo, hi)]


def brick_voids():
    """Every space cut out of the brickwork, as (lo, hi) in voxels: the hole from the bearers up, the lid's seat and
    the hinge pit; the cave and the ash mouth; the flue opening, the back flue and the stack; and the room for the
    iron set flush into the brick (the bearers' ends, the lintel, the damper's recess with its guides, the hinge's
    brackets and stop, the curb, the binding). The top is flat but for the seat, the pit and the outlet."""
    v = [([HOLE_X[0], BEARER_Y[0], HOLE_Z[0]], [HOLE_X[1], SEAT_Y, HOLE_Z[1]]),                # the hole, the grate's level
         ([SEAT_X[0], SEAT_Y, SEAT_Z[0]], [SEAT_X[1], FLOOR_TOP, SEAT_Z[1]]),                # the lid's seat
         ([SEAT_X[0], PIT_Y, PIT_FRONT], [SEAT_X[1], FLOOR_TOP, PIT_BACK])]                   # the hinge pit, the stop in it
    for x0, x1 in BEARER_X:
        v.append(([x0, BEARER_Y[0], BEARER_Z[0]], [x1, BEARER_Y[1], BEARER_Z[1]]))           # the bearers, ends in the walls
    (bxw, bxe, by, bz) = BRACKET
    for x in (bxw, bxe):
        v.append(([x[0], by[0], bz[0]], [x[1], by[1], bz[1]]))                               # the hinge's brackets
    (mx0, mx1), (my0, my1) = MOUTH
    v.append(([mx0, my0, 0.0], [mx1, my1, CAVE[2][0]]))                                       # the ash mouth
    (lx, ly, lz) = LINTEL
    v.append(([lx[0], ly[0], lz[0]], [lx[1], ly[1], lz[1]]))                                  # its lintel
    (rx, ry, rz) = RECESS
    v.append(([rx[0], ry[0], rz[0]], [rx[1], ry[1], rz[1]]))                                  # the damper's recess
    for (x0, x1), (y0, y1), (z0, z1) in (CAVE, FLUE_OPENING, BACK_FLUE, STACK, CURB):
        v.append(([x0, y0, z0], [x1, y1, z1]))
    v += [(lo, hi) for _, lo, hi in binding()]
    return v


def _inside(p, lo, hi):
    return all(lo[k] < p[k] < hi[k] for k in range(3))


def brick_lattice():
    """The brickwork as a lattice: the breakpoints along each axis (every void's faces and every block's), and for
    each lattice box whether it is brick (its middle in no void)."""
    voids = brick_voids()
    axes = []
    for k, n in enumerate((CELLS_X, CELLS_Y, CELLS_Z)):
        pts = {round(B * c, 6) for c in range(n + 1)}
        for lo, hi in voids:
            pts.add(round(min(max(lo[k], 0.0), n * B), 6))
            pts.add(round(min(max(hi[k], 0.0), n * B), 6))
        axes.append(sorted(pts))
    xs, ys, zs = axes
    solid = {}
    for i in range(len(xs) - 1):
        for j in range(len(ys) - 1):
            for k in range(len(zs) - 1):
                mid = ((xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2, (zs[k] + zs[k + 1]) / 2)
                solid[(i, j, k)] = not any(_inside(mid, lo, hi) for lo, hi in voids)
    return xs, ys, zs, solid


def build_brick():
    """The brickwork: the lattice's brick merged greedily into boxes (along x, then z, then y), none crossing a
    block's boundary, each world-mapped so the boxes read as one wall. Faces that lie against brick on their whole
    area are left out (they are inside the wall)."""
    xs, ys, zs, solid = brick_lattice()
    nx, ny, nz = len(xs) - 1, len(ys) - 1, len(zs) - 1

    def blk(vals, i):
        return int((vals[i] + 1e-6) // B)

    used = set()
    spans = []
    for j in range(ny):
        for k in range(nz):
            for i in range(nx):
                if not solid[(i, j, k)] or (i, j, k) in used:
                    continue

                def free(a, b, c):
                    return solid[(a, b, c)] and (a, b, c) not in used
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
                spans.append(((i, i1), (j, j1), (k, k1)))
    out = []
    for n, ((i0, i1), (j0, j1), (k0, k1)) in enumerate(spans, 1):
        el = wbox([xs[i0], ys[j0], zs[k0]], [xs[i1 + 1], ys[j1 + 1], zs[k1 + 1]], f"fr_brick_{n:03d}", "frame", "brick")
        faces = {"west": [(i0 - 1, b, c) for b in range(j0, j1 + 1) for c in range(k0, k1 + 1)],
                 "east": [(i1 + 1, b, c) for b in range(j0, j1 + 1) for c in range(k0, k1 + 1)],
                 "down": [(a, j0 - 1, c) for a in range(i0, i1 + 1) for c in range(k0, k1 + 1)],
                 "up": [(a, j1 + 1, c) for a in range(i0, i1 + 1) for c in range(k0, k1 + 1)],
                 "north": [(a, b, k0 - 1) for a in range(i0, i1 + 1) for b in range(j0, j1 + 1)],
                 "south": [(a, b, k1 + 1) for a in range(i0, i1 + 1) for b in range(j0, j1 + 1)]}
        for d, nbrs in faces.items():
            if all(solid.get(q, False) for q in nbrs):
                del el.faces[d]
        out.append(el)
    return out


# ---------------------------------------------------------------- builders
def build_iron():
    """The iron set into the brickwork: the three bearers (front to back) and the twelve firebars (two lengths a row),
    the ash mouth's lintel and the damper's guides, the hinge's pin, brackets and stop, the outlet's curb (a ring
    round the stack, a rim round the cap's seat), and the binding."""
    f, t = "frame", "iron"
    out = []
    for n, (x0, x1) in enumerate(BEARER_X, 1):
        out.append(box([x0, BEARER_Y[0], BEARER_Z[0]], [x1, BEARER_Y[1], BEARER_Z[1]], f"fr_bearer{n}", f, t))
    gap = (HOLE_Z[1] - HOLE_Z[0] - BARS * BAR_W) / (BARS - 1)
    xm = (HOLE_X[0] + HOLE_X[1]) / 2
    for n in range(BARS):
        z0 = HOLE_Z[0] + n * (BAR_W + gap)
        for side, (x0, x1) in (("w", (HOLE_X[0], xm)), ("e", (xm, HOLE_X[1]))):
            out.append(box([x0, BAR_Y[0], z0], [x1, BAR_Y[1], z0 + BAR_W], f"fr_bar{n + 1}{side}", f, t))
    (lx, ly, lz) = LINTEL
    out.append(box([lx[0], ly[0], lz[0]], [lx[1], ly[1], lz[1]], "fr_lintel", f, t))
    (_, ry, rz) = RECESS
    for side, (x0, x1) in zip(("w", "e"), GUIDES):
        out.append(box([x0, ry[0], rz[0]], [x1, ry[1], rz[1]], f"fr_guide_{side}", f, t))
    out += oct_rod_x(BRACKET[0][0] + 0.1, BRACKET[1][1] - 0.1, PIVOT[0], PIVOT[1], PIN_R, "fr_pin", f, t)
    for side, x in zip(("w", "e"), BRACKET[:2]):
        out.append(box([x[0], BRACKET[2][0], BRACKET[3][0]], [x[1], BRACKET[2][1], BRACKET[3][1]], f"fr_bracket_{side}", f, t))
    out += [box(lo, hi, name, f, t) for name, lo, hi in split_blocks("fr_stop", [SEAT_X[0], STOP_Y, STOP_Z], [SEAT_X[1], FLOOR_TOP, PIT_BACK])]
    (x0, x1), (y0, y1), (z0, z1) = CURB
    (sx0, sx1), _, (sz0, sz1) = STACK
    yr = CAP_Y[0]
    out += [box([x0, y0, z0], [x1, yr, sz0], "fr_curb_n", f, t), box([x0, y0, sz1], [x1, yr, z1], "fr_curb_s", f, t),
            box([x0, y0, sz0], [sx0, yr, sz1], "fr_curb_w", f, t), box([sx1, y0, sz0], [x1, yr, sz1], "fr_curb_e", f, t)]
    r = CURB_RIM
    out += [box([x0, yr, z0], [x1, y1, z0 + r], "fr_rim_n", f, t), box([x0, yr, z1 - r], [x1, y1, z1], "fr_rim_s", f, t),
            box([x0, yr, z0 + r], [x0 + r, y1, z1 - r], "fr_rim_w", f, t), box([x1 - r, yr, z0 + r], [x1, y1, z1 - r], "fr_rim_e", f, t)]
    out += [box(lo, hi, name, f, t) for name, lo, hi in binding()]
    return out


def build_stands():
    """Each pot's fireclay stand on the grate (the hole's: they stay when the pots are pulled)."""
    y0, y1, a = STAND
    return [el for i in POTS for el in oct_disc(POT_X[i], POT_Z, y0, y1, a, f"fr_stand{i}", "frame", "clay")]


def build_frame():
    return build_brick() + build_iron() + build_stands()


def build_lid():
    """The lid (`lid`), shut, then turned open about the pin (the rest pose): an iron frame round firebrick, its back
    bar round the pin, with a pocket near its front edge and an iron handle bar across the pocket, under its top."""
    p = "lid"
    (x0, x1), (z0, z1) = SEAT_X, SEAT_Z
    y0, y1 = SEAT_Y, FLOOR_TOP
    r = LID_RIM
    cx = (x0 + x1) / 2
    q, at, deep = POCKET
    pz = z0 + at
    out = []
    for name, lo, hi in (("frame_n", [x0, y0, z0], [x1, y1, z0 + r]), ("frame_s", [x0, y0, z1 - r], [x1, y1, z1])):
        out += [box(l2, h2, f"{p}_{n2}", p, "iron") for n2, l2, h2 in split_blocks(name, lo, hi)]
    out += [box([x0, y0, z0 + r], [x0 + r, y1, z1 - r], f"{p}_frame_w", p, "iron"),
            box([x1 - r, y0, z0 + r], [x1, y1, z1 - r], f"{p}_frame_e", p, "iron")]
    fx0, fx1, fz0, fz1 = x0 + r, x1 - r, z0 + r, z1 - r
    for name, lo, hi in (("fill_w", [fx0, y0, fz0], [cx - q, y1, fz1]), ("fill_e", [cx + q, y0, fz0], [fx1, y1, fz1]),
                         ("fill_n", [cx - q, y0, fz0], [cx + q, y1, pz - q]), ("fill_s", [cx - q, y0, pz + q], [cx + q, y1, fz1]),
                         ("pocket", [cx - q, y0, pz - q], [cx + q, y1 - deep, pz + q])):
        out += [wbox(l2, h2, f"{p}_{n2}", p, "brick") for n2, l2, h2 in split_cells(name, lo, hi)]
    hw, under = HANDLE
    out.append(box([cx - q - 0.5, y1 - under - 0.7, pz - hw], [cx + q + 0.5, y1 - under, pz + hw], f"{p}_handle", p, "iron"))
    rotate(out, "x", OPEN_DEG, (0.0, PIVOT[0], PIVOT[1]))
    return out


def build_pot(i):
    """Pot i (`pot<i>`), on its stand: a fireclay crucible, an octagonal barrel, its foot narrower than its belly and
    its mouth open (no pot lid is drawn, so the charge and the melt show)."""
    p = f"pot{i}"
    cx, cz = POT_X[i], POT_Z
    y0, y1, a = POT_BASE
    out = oct_disc(cx, cz, y0, y1, a, f"{p}_foot", p, "clay")
    for name, t0, t1, a in POT_TUBES:
        out += oct_tube(cx, cz, t0, t1, a, POT_WALL, f"{p}_{name}", p, "clay")
    return out


# the charge's pieces in a pot's mouth: iron bar crop ends and ferrochrome lumps, (x, y, z) from the pot's axis at
# the level's top, size (x, y, z), turns
CHARGE_PIECES = (
    ((-1.0, 1.3, -0.8), (1.0, 2.6, 1.0), (("x", 18.0), ("z", -14.0))),
    ((0.9, 0.6, 0.7), (1.0, 1.0, 2.6), (("y", 30.0), ("x", 12.0))),
    ((1.0, 0.6, -1.1), (1.3, 1.3, 1.3), (("y", 40.0), ("x", 25.0))),
    ((-1.1, 0.6, 1.2), (1.2, 1.2, 1.2), (("y", 15.0), ("z", 30.0))),
    ((0.1, 1.7, 0.1), (0.9, 3.2, 0.9), (("z", 11.0), ("x", -16.0))),
)


def build_charge(i):
    """Pot i's charge (`charge<i>`): the pot filled to LEVEL with it, and pieces standing up out of it, a little above
    the rim, as a crucible was charged full."""
    p = f"charge{i}"
    cx, cz = POT_X[i], POT_Z
    out = oct_disc(cx, cz, LEVEL[0], LEVEL[1], LEVEL_A, f"{p}_fill", p, "charge")
    for n, (d, size, turns) in enumerate(CHARGE_PIECES, 1):
        out.append(bar([cx + d[0], LEVEL[1] + d[1], cz + d[2]], size, turns, f"{p}_piece{n}", p, "charge"))
    return out


def build_melt(i):
    """Pot i's melt (`melt<i>`): the molten charge's surface in the pot, glowing."""
    p = f"melt{i}"
    return oct_disc(POT_X[i], POT_Z, LEVEL[0], LEVEL[1], LEVEL_A, f"{p}_pool", p, "melt")


def pot_apothem(y):
    """A pot's outer apothem at height y: its widest section there (the foot, a tube)."""
    best = 0.0
    if POT_BASE[0] <= y <= POT_BASE[1]:
        best = POT_BASE[2]
    for _, t0, t1, a in POT_TUBES:
        if t0 <= y <= t1:
            best = max(best, a)
    return best


def coke_opening(j):
    """The opening layer j leaves for each pot: the pot's widest section over the layer's height, so the coke hugs it
    where it is widest and its top meets the pot with no gap (what lies under a ledge of the pot is not seen)."""
    y0, y1 = COKE_LAYERS[j - 1]
    return max([a for _, t0, t1, a in POT_TUBES if t0 < y1 and t1 > y0] +
               ([POT_BASE[2]] if POT_BASE[0] < y1 and POT_BASE[1] > y0 else []))


LUMP_TURNS = ((("y", 28.0), ("x", 21.0)), (("y", 63.0), ("z", 17.0)), (("y", 11.0), ("x", -24.0)), (("y", 47.0), ("z", -19.0)))


def lump_places():
    """Where the lumps lie on a layer's top: each pot's two outer corners and the middle, front and back."""
    a = LUMP_AT
    (p1, p2), cz = (POT_X[1], POT_X[2]), POT_Z
    mid = (p1 + p2) / 2
    return [(p1 - a, cz - a), (p1 - a, cz + a), (mid, cz - a), (mid, cz + a), (p2 + a, cz - a), (p2 + a, cz + a)]


def build_coke(j, kind):
    """The coke's layer j (`coke<j>`), or the same coke burning (`fire<j>`, kind "fire": the ember texture, its faces
    glowing): one bed across the hole round both pots' octagons, strips and corner fills, with lumps of coke lying on
    its top. The burning bottom layer also has cinders fallen through the grate onto the cave's floor."""
    p = f"{kind}{j}"
    tex = "coke" if kind == "coke" else "ember"
    (hx0, hx1), (hz0, hz1), cz = HOLE_X, HOLE_Z, POT_Z
    p1, p2 = POT_X[1], POT_X[2]
    y0, y1 = COKE_LAYERS[j - 1]
    a = coke_opening(j)
    out = [box([hx0, y0, hz0], [hx1, y1, cz - a], f"{p}_n", p, tex),
           box([hx0, y0, cz + a], [hx1, y1, hz1], f"{p}_s", p, tex),
           box([hx0, y0, cz - a], [p1 - a, y1, cz + a], f"{p}_w", p, tex),
           box([p1 + a, y0, cz - a], [p2 - a, y1, cz + a], f"{p}_m", p, tex),
           box([p2 + a, y0, cz - a], [hx1, y1, cz + a], f"{p}_e", p, tex)]
    depth = a * (math.sqrt(2.0) - 1.0)           # from a diagonal flat to its square's corner
    for i in POTS:
        for name, deg in (("se", 45.0), ("sw", 135.0), ("nw", 225.0), ("ne", 315.0)):
            out.append(oct_slab(POT_X[i], cz, y0, y1, a, a + depth, 2 * a * OCT, deg, f"{p}_{name}{i}", p, tex))
    for n, (x, z) in enumerate(lump_places()):
        out.append(lump([x, y1 + 0.1, z], LUMP, LUMP_TURNS[(n + j) % 4], f"{p}_lump{n + 1}", p, tex))
    if kind == "fire" and j == 1:
        y = CAVE[1][0]
        for i in POTS:
            for n, (dx, dz, w, d, deg) in enumerate(((-2.0, -1.0, 3.0, 2.4, 20.0), (1.6, 2.0, 2.6, 2.0, -35.0), (0.6, -3.0, 2.2, 1.8, 60.0)), 1):
                out.append(bar([POT_X[i] + dx, y + 0.3, cz + dz], (w, 0.6, d), (("y", deg),), f"{p}_cinder{i}{n}", p, tex))
    return out


def build_damper(place):
    """The ash-pit damper (`damper<place>`): an iron plate sliding in its recess over the ash mouth, with a knob, in
    one of its three places (DAMPER). Each place is its own part; gameplay draws the one the damper is in."""
    p = f"damper{place}"
    y0 = DAMPER[place]
    (gx0, _), (_, gx1) = GUIDES
    out = [box([GUIDES[0][1], y0, DAMPER_Z[0]], [GUIDES[1][0], y0 + DAMPER_H, DAMPER_Z[1]], f"{p}_plate", p, "iron")]
    (kx, kz, ky) = KNOB
    out.append(box([kx[0], y0 + ky[0], kz[0]], [kx[1], y0 + ky[1], kz[1]], f"{p}_knob", p, "iron"))
    return out


def build_cap():
    """The outlet's cap (`outletcap`): a flat iron plate set flush in the curb's rim, on its ring, closing the outlet
    of a furnace whose chimney is another's in the row."""
    (x0, x1), _, (z0, z1) = CURB
    r = CURB_RIM
    return [box([x0 + r, CAP_Y[0], z0 + r], [x1 - r, CAP_Y[1], z1 - r], "outletcap_plate", "outletcap", "iron")]


def build():
    els = build_lid()
    for i in POTS:
        els += build_pot(i)
    for i in POTS:
        els += build_charge(i)
    for i in POTS:
        els += build_melt(i)
    for kind in ("coke", "fire"):
        for j in (1, 2, 3):
            els += build_coke(j, kind)
    for place in DAMPERS:
        els += build_damper(place)
    return els + build_cap() + build_frame()


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def lid_drivers():
    """Shut as depth goes 0 -> 1: turned down about the pin from open (OPEN_DEG) into the seat. It also carries theta
    at ratio 0 first (the clock; it moves nothing)."""
    pivot = pt(0.0, PIVOT[0], PIVOT[1])
    return [{"type": "rotate", "axis": "x", "pivot": pivot, "ratio": 0.0},
            {"type": "step", "motion": "rotate", "axis": "x", "pivot": pivot, "from": 0.0, "to": 1.0,
             "amount": r6(-math.radians(OPEN_DEG))}]


PART_IDS = ("lid",) + CONTENTS + tuple(f"damper{d}" for d in DAMPERS) + ("outletcap",)

_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    parts = []
    for pid in PART_IDS:
        if pid == "lid":
            parts.append({"id": pid, "match": [f"{pid}_*"], "requires": None, "drivers": lid_drivers()})
        else:
            parts.append({"id": pid, "match": [f"{pid}_*"], "requires": pid, "drivers": []})
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []})
    for p in parts:
        p["ride"] = None
        for d in p["drivers"]:
            validate_driver(d)
    return [{"id": p["id"], "match": p["match"], "requires": p["requires"], "drivers": p["drivers"], "ride": p["ride"]}
            for p in parts]


# ---------------------------------------------------------------- poses
# A pose is (theta, depth, scene): the clock, the lid, and what is fitted. A scene is (the pots' state, the damper's
# place, whether the outlet is capped): gameplay's requires.
DEFAULT_SCENE = ("charged", "open", False)
REST = (0.0, 0.0, DEFAULT_SCENE)
SHUT = 1.0


def inputs_of(pose):
    return {"theta": pose[0], "depth": pose[1]}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


def fitted(scene):
    """The requires values fitted in a scene."""
    state, damper, capped = scene
    out = set(dict((s[0], s[2]) for s in POT_STATES)[state])
    out.add(f"damper{damper}")
    if capped:
        out.add("outletcap")
    return out


def on_show(part, scene):
    """Whether a part is drawn in a scene: the lid and the frame always, the rest when fitted."""
    return part in ("frame", "lid") or part in fitted(scene)


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
    """The brickwork's eight cells, then the two over the hole, where the lid stands open."""
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)] + list(LID_CELLS)


def regions():
    """Where the model may be (voxels, (lo, hi)): the brickwork's box, and the cells over the hole."""
    return [((0.0, 0.0, 0.0), (W, H, D)), ((0.0, H, 0.0), (W, H + B, B))]


ANCHORS = ("mouth", "ash")


def mouth_point():
    """The middle of the hole's mouth at the floor: flames, the glow's light and the fire's sound while it is open."""
    return ((HOLE_X[0] + HOLE_X[1]) / 2, FLOOR_TOP, POT_Z)


def ash_point():
    """The middle of the ash mouth in the front face: the damper, worked from in front (the cellar lad's)."""
    (mx0, mx1), (my0, my1) = MOUTH
    return ((mx0 + mx1) / 2, (my0 + my1) / 2, 0.0)


def outlet_cell():
    return tuple(int(v // B) for v in ((STACK[0][0] + STACK[0][1]) / 2, FLOOR_TOP - 1.0, (STACK[2][0] + STACK[2][1]) / 2))


def pot_cell(i):
    return (int(POT_X[i] // B), 1, int(POT_Z // B))


MIN_CHIMNEY = 6                              # CrucibleFurnaceConfig.MinChimneyHeight: chimney blocks stood on the outlet,
#                                              counted from it up (the stack's base in the furnace does not count)


def make_rig(parts):
    sx, sz = (STACK[0][0] + STACK[0][1]) / 2, (STACK[2][0] + STACK[2][1]) / 2
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the front west cell "
                    "at ground level. One hole across the front row with two pots side by side, pot 1 west and pot 2 "
                    "east, worked from the front; furnaces stand side by side in a row along x (up to furnace.rowMax), "
                    "sharing one chimney on one member's outlet, the others capped. No power and no forced air: natural "
                    "draught, in at the ash mouth (ash, ashSide) under the damper. depth is the lid: 0 open, resting just "
                    "past upright on its stop (the rest pose), 1 shut, turned about the hinge (hinge). theta is the "
                    "melter's clock, carried by a ratio-0 rotate on the lid: nothing turns. requires: pot1, pot2, "
                    "charge1, charge2, melt1, melt2, the coke's three layers coke1..3 (from the grate up) and the same "
                    "burning, fire1..3; the damper's place, damperopen, damperhalf or dampershut (one at a time); and "
                    "outletcap. The two cells over the hole are hollow: the lid stands there open (gameplay's collision "
                    "for it). potNCell: the cell over pot N, worked from its top; outletCell: the stack, the chimney "
                    "stood on its top; chimney: the shortest chimney, counted from the outlet up. See "
                    "CrucibleFurnace/README.md.",
        "cells": [],
        "pot1Cell": list(pot_cell(1)),
        "pot1Face": "up",
        "pot2Cell": list(pot_cell(2)),
        "pot2Face": "up",
        "outletCell": list(outlet_cell()),
        "outletFace": "up",
        "ash": {"pos": pt(*ash_point())},
        "ashSide": "north",
        "mouth": {"pos": pt(*mouth_point())},
        "hinge": {"origin": pt((SEAT_X[0] + SEAT_X[1]) / 2, PIVOT[0], PIVOT[1]), "axis": "x",
                  "length": r6((SEAT_X[1] - SEAT_X[0]) / B)},
        "chimney": {"origin": pt(sx, FLOOR_TOP + MIN_CHIMNEY * B / 2, sz), "axis": "y", "length": float(MIN_CHIMNEY)},
        "furnace": {"pots": len(POTS), "potUnits": POT_UNITS, "cokeLayers": len(COKE_LAYERS), "cokePerLayer": COKE_PER_LAYER,
                    "minChimney": MIN_CHIMNEY, "rowMax": ROW_MAX, "rowAxis": "x", "lidOpenDegrees": OPEN_DEG,
                    "_comment": "pots: the pots in the hole, pot1 and pot2; potUnits: what each pot's drawn melt holds "
                                "(PotCapacityUnits); cokeLayers, cokePerLayer: the hole's coke is drawn as its layers "
                                "coke1.. up to ceil(coke / cokePerLayer) (fire.. while lit); minChimney: chimney blocks "
                                "stood on the outlet, counted from it up (MinChimneyHeight); rowMax, rowAxis: furnaces in "
                                "a row, side by side along x, sharing one chimney; lidOpenDegrees: how far the lid opens "
                                "(the hinge's step)."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: the build frame's corner is the
    controller's)."""
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
    for key in ("pot1Cell", "pot2Cell", "outletCell"):
        ship[key] = shift_cell(rig[key], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    for key in ("hinge", "chimney"):
        ship[key] = {**rig[key], "origin": shift_point(rig[key]["origin"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


CELL_PARTS = ("frame", "lid")                # what the cells' boxes are made of: the brickwork, its iron and the lid shut
CELL_POSE = (0.0, SHUT, DEFAULT_SCENE)


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed by the shipped rig with the lid shut; a lid (a deck)
    on every top cell of the brickwork, at the floor, so no one falls into the open hole; the cells over the hole
    hollow."""
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
    hollow = {tuple(c[k] - ORIGIN_CELL[k] for k in range(3)) for c in LID_CELLS}
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
    """depth over the lid's swing, at theta 0, 1.1, -2.3 and 2.9 in turn."""
    return [((0.0, 1.1, -2.3, 2.9)[n % 4], dp) for n, dp in enumerate(REF_DEPTHS)]


def reference_json(ship_parts):
    poses = []
    for th, dp in reference_poses():
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], {"theta": th, "depth": dp})) for q in ship_parts}
        poses.append({"theta": th, "depth": dp, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped cruciblefurnace-rig.json's parts: each part's matrix as 3 "
                        "rows of 4 (block units) at each pose (depth, the lid: 0 open, 1 shut; theta moves nothing). The "
                        "site's and the mod's tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    shape = machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Texture codes the renderer may "
             "set: 'melt' and 'charge' (the pots' metal). The burning coke ('ember') and the melt glow. Keep element "
             "names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)
    for e in shape["elements"]:
        stem = e["name"].split("_")[0]
        glow = FIRE_GLOW if stem.startswith("fire") else MELT_GLOW if stem.startswith("melt") else 0
        if glow:
            for f in e["faces"].values():
                f["glow"] = glow
    return shape


def coplanar_poses():
    scenes = [(st, "open", False) for st in ("charged", "melted", "burntlow", "pulled", "pots")]
    scenes += [("cold", "half", True), ("cold", "shut", True)]
    return tuple((0.0, dp, sc) for sc in scenes for dp in (0.0, 0.5, 1.0))


def fix_coplanar(els, parts):
    def posed_fn(es, pose):
        return shown(parts, [posed(el, pm(parts, el.part, pose)) for el in es], pose)
    return fix_coplanar_posed(els, posed_fn, coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the crucible furnace's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_cruciblefurnace
    els = build()
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        print(f"coplanar faces before the fix: {sum(len(p) for p in before.values())} pairs over {len(before)} poses")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_cruciblefurnace.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "cruciblefurnace.json", args.out / "cruciblefurnace_frame.json", args.out / "cruciblefurnace-rig.json",
                args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "cruciblefurnace.json", SHAPE_DIR / "cruciblefurnace_frame.json", RIG_DIR / "cruciblefurnace-rig.json",
                REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_cruciblefurnace.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
