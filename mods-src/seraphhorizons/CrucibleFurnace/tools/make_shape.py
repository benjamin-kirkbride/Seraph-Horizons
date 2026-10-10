#!/usr/bin/env python3
"""Generate the crucible furnace's shapes, rig and reference poses.

The crucible furnace is a Huntsman (Sheffield) melting furnace of about 1850, built whole as one machine:
a block of tier-3 refractory brickwork three blocks wide, two deep and two high, with two melting holes
set into its top, side by side at the front. Each hole is lined with firebrick, 12 voxels square, and
holds a fireclay pot on a fireclay stand on a grate of iron firebars, packed round with coke up to the
pot's shoulders. Its cover (firebrick in an iron frame) sits flush in a seat at the hole's mouth and is
lifted aside, straight back into a bay of its own in the floor behind the hole, to charge the pot, top
up the coke and pull the pot. Under the grates runs one ashpit (the cave), open at the front under each
hole for the ash and the draught, with an iron-flanged air port into it at the back for forced air.
Near the top of each hole's back wall a flue opening leads into a flue running behind both holes, and
from it the stack rises in the middle of the back row to an outlet on the furnace's top, with an iron
curb, on which the player stands the chimney. Everything is built here from plain boxes; no other
mod's model is used.

It writes, deterministically,

    cruciblefurnace.json          the whole furnace, every part that moves or comes and goes   (assets/.../shapes/block/)
    cruciblefurnace_frame.json    the static frame only (block and item)                       (assets/.../shapes/block/)
    cruciblefurnace-rig.json      cells, anchors and the part rig                              (assets/.../config/)
    rig-reference.json            every part's matrix at a grid of poses                       (tests/CrucibleFurnace/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_cruciblefurnace.py) and
exits non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
machine box's north-west-bottom corner (the "build frame"). The holes are at the front (north), hole 1
west and hole 2 east; the bays and the stack are in the back row (south). The controller cell is the
front middle cell at ground level, build cell (1, 0, 0), which the shipped files move to [0,0,0].

The rig's inputs, as this furnace uses them:

    depth  a cover's place: 0 lifted aside into its bay (at rest), 1 seated in its hole. In the game each
           hole's cover has its own (the renderer poses cover i with hole i's); the viewer moves both.
    theta  the melter's clock: a ratio-0 rotate on hole 1's cover carries it and moves nothing (so the
           viewer offers Play, which lifts the covers on and off). Nothing in the furnace turns.

Everything else that shows or not (the pot, its charge, the melt, the coke in three layers and the same
coke burning) is a `requires` per hole, `h1...` and `h2...`: gameplay's state, not a motion.
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
REFERENCE_OUT = MOD / "tests" / "CrucibleFurnace" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/CrucibleFurnace/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
OCT = math.tan(math.pi / 8)                  # an octagon's half side over its apothem

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 3, 2, 2          # the brickwork: three wide (x), two high, two deep (z)
ORIGIN_CELL = (1, 0, 0)                      # the controller: the front middle cell at ground level
# Over each hole and its bay: room for the cover as it is lifted aside (it rises 2.6 voxels over the floor).
# Reserved as hollow cells (ghosts with no boxes), so nothing is built where the covers pass. The cell
# over the outlet, (1, 2, 1), is the chimney's and is not the machine's.
LIFT_CELLS = ((0, 2, 0), (0, 2, 1), (2, 2, 0), (2, 2, 1))
CHIMNEY_CELL = (1, 2, 1)

TEXTURES = {
    "brick": "game:block/clay/refractory/tier3/front1",    # the existing hole's brick: tier-3 refractory
    "iron": "game:block/metal/plate/iron",
    "clay": "game:block/clay/hardened/fire",                # the fired pot's (meltingpot-fired)
    "coke": "game:block/coal/coke",
    "ember": "game:block/coal/ember",                       # the same coke, burning
    "melt": "game:block/metal/ingot/stainlesssteel",        # the melt: the renderer may set it to the charge's metal
    "charge": "game:block/metal/ingot/iron",                # the charge (iron bits and ferrochrome): likewise
}
TEX = 16                                     # shape texture units: one a voxel, one texture across a block face
FIRE_GLOW = 200                              # the burning coke's faces (the game's lit firepit embers are 160)
MELT_GLOW = 255                              # the melt's

# ---------------------------------------------------------------- the holes
HOLES = (1, 2)                               # hole 1 west, hole 2 east
HOLE_X = {1: 8.0, 2: 40.0}                   # each hole's axis (x): the middle of its cell
HOLE_Z = 8.0                                 # (z): the front row's middle
HOLE_HALF = 6.0                              # a hole is 12 square inside (the period's two feet, at the game's scale)
SEAT_HALF = 7.0                              # the cover's seat at the mouth, 14 square: a 1-voxel ledge round the hole
BAY_Z = HOLE_Z + B                           # each cover's bay, straight behind its hole in the back row
FLOOR_TOP = 32.0                             # the furnace's top: the melting floor
SEAT_Y = 29.5                                # the seat's ledge: the cover is 2.5 thick, flush with the floor
BAY_PLATE_Y = (29.0, 29.5)                   # the iron plate the cover lies on in its bay

# the cave (the ashpit, one under both holes) and the grates
CAVE_FLOOR = (0.0, 1.0)                      # brick under the cave
CAVE = ((2.0, 46.0), (1.0, 9.0), (2.0, 14.0))   # x, y, z: under both holes and the pier between them
MOUTH_HALF, MOUTH_TOP = 4.0, 7.0             # each hole's ash mouth in the front wall, 8 wide, 6 tall from the cave floor
LINTEL = (7.0, 8.0)                          # the iron lintel over a mouth (y); it runs a voxel into the wall each side
BEARER_Y = (9.0, 10.5)                       # the iron bearers under the firebars' ends, along z, their ends built
BEARER_X = ((-6.0, -4.5), (4.5, 6.0))        # into the front and back walls (which stand on the cave's floor); x from
#                                              the hole's axis
BAR_Y = (10.5, 12.0)                         # the firebars, along x, side wall to side wall, lying on the bearers
BARS, BAR_W = 6, 1.2                         # six bars 1.2 wide across the 12: gaps of 0.96
GRATE_TOP = BAR_Y[1]

# the pot and its stand (the existing pot block is 8 across and 11 tall)
STAND = (GRATE_TOP, 14.0, 2.6)               # the fireclay stand on the grate: y0, y1, apothem
POT_BASE = (14.0, 15.0, 3.0)                 # the pot's foot: a solid octagon
POT_WALL = 0.9
POT_TUBES = (("lower", 15.0, 18.0, 3.6), ("belly", 18.0, 22.5, 4.0), ("upper", 22.5, 25.0, 3.6))
POT_TOP = POT_TUBES[-1][2]
POT_INNER = POT_TUBES[-1][3] - POT_WALL      # the mouth's inner apothem
LEVEL = (21.6, 22.6)                         # the charge's top layer, or the melt, inside the pot (y)
LEVEL_A = POT_INNER + 0.01                   # its apothem: a hundredth into the walls, so no gap shows round it

# the coke: three layers from the grate to the pot's shoulders (where the belly ends), two coke a layer
COKE_LAYERS = ((GRATE_TOP, 16.0), (16.0, 19.5), (19.5, 22.5))
COKE_PER_LAYER = 2                           # CokeCapacity (6) over the layers: gameplay's coke count -> layers shown
LUMP = 1.5                                   # the coke lumps lying on each layer's top, in its corners
LUMP_AT = 4.3                                # their place from the hole's axis, along both x and z

# the flue
FLUE_OPENING = ((-3.0, 3.0), (24.0, 28.0), (14.0, 20.0))   # through each hole's back wall, near its top (x from its axis)
BACK_FLUE = ((5.0, 43.0), (23.0, 28.0), (20.0, 28.0))      # behind both holes, under the bays
STACK = ((20.0, 28.0), (23.0, FLOOR_TOP), (20.0, 28.0))    # the stack's base, up to the outlet on the top
CURB = ((18.0, 30.0), (30.0, FLOOR_TOP), (18.0, 30.0))     # the outlet's iron curb, flush, round the stack's mouth

# the air port, for forced air: a duct from the back face into the middle of the cave
DUCT = ((22.0, 26.0), (3.0, 7.0), (CAVE[2][1], 2 * B))
FLANGE = ((20.0, 28.0), (1.0, 9.0), (2 * B - 1.0, 2 * B))  # the iron flange, flush with the back face
SLEEVE = (0.5, 2 * B - 5.0)                  # the iron sleeve in the duct's mouth: its wall, and how far in it reaches (z)

# the iron binding of the brickwork: an angle iron up each corner and a band round the floor's edge, flush
BIND_T = 0.5                                 # their thickness, set into the brick
CORNER_LEG = 3.0                             # each corner angle's legs
KERB_Y = (30.5, FLOOR_TOP)                   # the band round the top, between the corner angles

# the cover: firebrick in an iron frame, with a pocket and a bar for the melter's hook
COVER_RIM = 1.5                              # the iron frame's width
POCKET = (1.5, 30.8)                         # the hook's pocket: its half side, its floor (y)
HOOK = (0.35, 31.0, 31.7)                    # the bar across the pocket: its half width (z), its underside and top (y)
LIFT = (FLOOR_TOP - SEAT_Y) + 0.1            # how far a cover is lifted: its underside clears the floor by 0.1
T_LIFT, T_SET = 0.2, 0.8                     # depth: lifted over 0..0.2, carried over 0.2..0.8, set down over 0.8..1

PARTS_OF_HOLE = ("cover", "pot", "charge", "melt", "coke1", "coke2", "coke3", "fire1", "fire2", "fire3")

# The scenario's states of a hole's contents, as the viewer gives them (site/models.json; tools/tests holds the two
# together). Each is the hole's requires values fitted, without its h<i> prefix.
STATES = (
    ("cold", "Cold and empty", ()),
    ("pot", "A pot on its stand", ("pot",)),
    ("charged", "Charged, coke packed round", ("pot", "charge", "coke1", "coke2", "coke3")),
    ("lit", "Lit: the charge melting", ("pot", "charge", "fire1", "fire2", "fire3")),
    ("melted", "Melted", ("pot", "melt", "fire1", "fire2", "fire3")),
    ("burntlow", "Melted, the coke burnt low", ("pot", "melt", "fire1")),
    ("pulled", "The pot pulled, the fire still lit", ("fire1", "fire2", "fire3")),
)
STATE_IDS = tuple(s[0] for s in STATES)


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
def binding():
    """The iron binding, as (name, lo, hi): an angle iron up each vertical corner (a leg on each face, the x leg
    taking the corner) and a band round the top edge between them, all BIND_T thick and flush with the faces."""
    W, H, D = CELLS_X * B, CELLS_Y * B, CELLS_Z * B
    t, L = BIND_T, CORNER_LEG
    out = []
    for name, x0, z0 in (("nw", 0.0, 0.0), ("ne", W, 0.0), ("sw", 0.0, D), ("se", W, D)):
        sx, sz = (1.0 if x0 == 0 else -1.0), (1.0 if z0 == 0 else -1.0)
        xs = sorted((x0, x0 + sx * L))
        zs = sorted((z0, z0 + sz * t))
        out.append((f"fr_corner_{name}_x", [xs[0], 0.0, zs[0]], [xs[1], H, zs[1]]))
        xs = sorted((x0, x0 + sx * t))
        zs = sorted((z0 + sz * t, z0 + sz * L))
        out.append((f"fr_corner_{name}_z", [xs[0], 0.0, zs[0]], [xs[1], H, zs[1]]))
    y0, y1 = KERB_Y
    out += [("fr_kerb_n", [L, y0, 0.0], [W - L, y1, t]), ("fr_kerb_s", [L, y0, D - t], [W - L, y1, D]),
            ("fr_kerb_w", [0.0, y0, L], [t, y1, D - L]), ("fr_kerb_e", [W - t, y0, L], [W, y1, D - L])]
    return [piece for name, lo, hi in out for piece in split_blocks(name, lo, hi)]


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


def brick_voids():
    """Every space cut out of the brickwork, as (lo, hi) in voxels: each hole from the bearers up and its seat; the
    bays; the cave and its mouths; the flue openings, the back flue and the stack; and the room for the iron set into
    the brick (the bearers' ends, the lintels, the curb, the flange; the duct takes the sleeve)."""
    v = []
    for i in HOLES:
        cx = HOLE_X[i]
        h, s = HOLE_HALF, SEAT_HALF
        v.append(([cx - h, BEARER_Y[0], HOLE_Z - h], [cx + h, SEAT_Y, HOLE_Z + h]))          # the hole, the grate's level
        v.append(([cx - s, SEAT_Y, HOLE_Z - s], [cx + s, FLOOR_TOP, HOLE_Z + s]))           # the cover's seat
        v.append(([cx - s, BAY_PLATE_Y[0], BAY_Z - s], [cx + s, FLOOR_TOP, BAY_Z + s]))     # the bay and its plate
        for x0, x1 in BEARER_X:
            v.append(([cx + x0, BEARER_Y[0], HOLE_Z - s], [cx + x1, BEARER_Y[1], HOLE_Z + s]))   # the bearers, ends in the walls
        v.append(([cx - MOUTH_HALF, CAVE[1][0], 0.0], [cx + MOUTH_HALF, MOUTH_TOP, CAVE[2][0]]))   # the ash mouth
        v.append(([cx - MOUTH_HALF - 1.0, LINTEL[0], 0.0], [cx + MOUTH_HALF + 1.0, LINTEL[1], CAVE[2][0]]))   # its lintel
        (fx0, fx1), (fy0, fy1), (fz0, fz1) = FLUE_OPENING
        v.append(([cx + fx0, fy0, fz0], [cx + fx1, fy1, fz1]))                              # the flue opening
    for (x0, x1), (y0, y1), (z0, z1) in (CAVE, BACK_FLUE, STACK, CURB, DUCT, FLANGE):
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
    """The iron set into the brickwork: each hole's two bearers (front to back, under the bars' ends) and six firebars
    (side to side), its ash mouth's lintel, its bay's plate; the outlet's curb; the air port's flange and its sleeve;
    the binding (corner angles and the band round the top)."""
    f, t = "frame", "iron"
    out = []
    for i in HOLES:
        cx = HOLE_X[i]
        for n, (x0, x1) in enumerate(BEARER_X, 1):
            out.append(box([cx + x0, BEARER_Y[0], HOLE_Z - SEAT_HALF], [cx + x1, BEARER_Y[1], HOLE_Z + SEAT_HALF], f"fr_bearer{i}{n}", f, t))
        gap = (2 * HOLE_HALF - BARS * BAR_W) / (BARS - 1)
        for n in range(BARS):
            z0 = HOLE_Z - HOLE_HALF + n * (BAR_W + gap)
            out.append(box([cx - HOLE_HALF, BAR_Y[0], z0], [cx + HOLE_HALF, BAR_Y[1], z0 + BAR_W], f"fr_bar{i}{n + 1}", f, t))
        out.append(box([cx - MOUTH_HALF - 1.0, LINTEL[0], 0.0], [cx + MOUTH_HALF + 1.0, LINTEL[1], CAVE[2][0]], f"fr_lintel{i}", f, t))
        out.append(box([cx - SEAT_HALF, BAY_PLATE_Y[0], BAY_Z - SEAT_HALF], [cx + SEAT_HALF, BAY_PLATE_Y[1], BAY_Z + SEAT_HALF],
                       f"fr_plate{i}", f, t))
    (x0, x1), (y0, y1), (z0, z1) = CURB
    (sx0, sx1), _, (sz0, sz1) = STACK
    out += [box([x0, y0, z0], [x1, y1, sz0], "fr_curb_n", f, t), box([x0, y0, sz1], [x1, y1, z1], "fr_curb_s", f, t),
            box([x0, y0, sz0], [sx0, y1, sz1], "fr_curb_w", f, t), box([sx1, y0, sz0], [x1, y1, sz1], "fr_curb_e", f, t)]
    (x0, x1), (y0, y1), (z0, z1) = FLANGE
    (dx0, dx1), (dy0, dy1), _ = DUCT
    out += [box([x0, y0, z0], [x1, dy0, z1], "fr_flange_d", f, t), box([x0, dy1, z0], [x1, y1, z1], "fr_flange_u", f, t),
            box([x0, dy0, z0], [dx0, dy1, z1], "fr_flange_w", f, t), box([dx1, dy0, z0], [x1, dy1, z1], "fr_flange_e", f, t)]
    w, zin = SLEEVE
    z1 = 2 * B
    out += [box([dx0, dy0, zin], [dx1, dy0 + w, z1], "fr_sleeve_d", f, t), box([dx0, dy1 - w, zin], [dx1, dy1, z1], "fr_sleeve_u", f, t),
            box([dx0, dy0 + w, zin], [dx0 + w, dy1 - w, z1], "fr_sleeve_w", f, t),
            box([dx1 - w, dy0 + w, zin], [dx1, dy1 - w, z1], "fr_sleeve_e", f, t)]
    out += [box(lo, hi, name, f, t) for name, lo, hi in binding()]
    return out


def build_stands():
    """Each hole's fireclay stand on the grate, the pot's seat (the hole's, it stays when the pot is pulled)."""
    y0, y1, a = STAND
    return [el for i in HOLES for el in oct_disc(HOLE_X[i], HOLE_Z, y0, y1, a, f"fr_stand{i}", "frame", "clay")]


def build_frame():
    return build_brick() + build_iron() + build_stands()


def build_cover(i):
    """Hole i's cover (`h<i>cover`), at rest in its bay: an iron frame round firebrick, with a pocket in the middle and
    an iron bar across it, flush with the top, for the melter's hook."""
    p = f"h{i}cover"
    cx, cz = HOLE_X[i], BAY_Z
    s, r = SEAT_HALF, COVER_RIM
    y0, y1 = SEAT_Y, FLOOR_TOP
    q, pf = POCKET
    inner = s - r
    out = [box([cx - s, y0, cz - s], [cx + s, y1, cz - inner], f"{p}_frame_n", p, "iron"),
           box([cx - s, y0, cz + inner], [cx + s, y1, cz + s], f"{p}_frame_s", p, "iron"),
           box([cx - s, y0, cz - inner], [cx - inner, y1, cz + inner], f"{p}_frame_w", p, "iron"),
           box([cx + inner, y0, cz - inner], [cx + s, y1, cz + inner], f"{p}_frame_e", p, "iron"),
           wbox([cx - inner, y0, cz - inner], [cx - q, y1, cz + inner], f"{p}_fill_w", p, "brick"),
           wbox([cx + q, y0, cz - inner], [cx + inner, y1, cz + inner], f"{p}_fill_e", p, "brick"),
           wbox([cx - q, y0, cz - inner], [cx + q, y1, cz - q], f"{p}_fill_n", p, "brick"),
           wbox([cx - q, y0, cz + q], [cx + q, y1, cz + inner], f"{p}_fill_s", p, "brick"),
           wbox([cx - q, y0, cz - q], [cx + q, pf, cz + q], f"{p}_pocket", p, "brick")]
    hw, hy0, hy1 = HOOK
    out.append(box([cx - q - 0.5, hy0, cz - hw], [cx + q + 0.5, hy1, cz + hw], f"{p}_hook", p, "iron"))
    return out


def build_pot(i):
    """Hole i's pot (`h<i>pot`), on its stand: a fireclay crucible, an octagonal barrel, its foot narrower than its
    belly and its mouth open (no pot lid is drawn, so the charge and the melt show)."""
    p = f"h{i}pot"
    cx, cz = HOLE_X[i], HOLE_Z
    y0, y1, a = POT_BASE
    out = oct_disc(cx, cz, y0, y1, a, f"{p}_foot", p, "clay")
    for name, t0, t1, a in POT_TUBES:
        out += oct_tube(cx, cz, t0, t1, a, POT_WALL, f"{p}_{name}", p, "clay")
    return out


# the charge's pieces in the pot's mouth: iron bar crop ends and ferrochrome lumps, (x, y, z) from the pot's axis at
# the level's top, size (x, y, z), turns
CHARGE_PIECES = (
    ((-1.0, 1.3, -0.8), (1.0, 2.6, 1.0), (("x", 18.0), ("z", -14.0))),
    ((0.9, 0.6, 0.7), (1.0, 1.0, 2.6), (("y", 30.0), ("x", 12.0))),
    ((1.0, 0.6, -1.1), (1.3, 1.3, 1.3), (("y", 40.0), ("x", 25.0))),
    ((-1.1, 0.6, 1.2), (1.2, 1.2, 1.2), (("y", 15.0), ("z", 30.0))),
    ((0.1, 1.7, 0.1), (0.9, 3.2, 0.9), (("z", 11.0), ("x", -16.0))),
)


def build_charge(i):
    """Hole i's charge (`h<i>charge`): the pot filled to LEVEL with it, and pieces standing up out of it, a little
    above the rim, as a crucible was charged full."""
    p = f"h{i}charge"
    cx, cz = HOLE_X[i], HOLE_Z
    out = oct_disc(cx, cz, LEVEL[0], LEVEL[1], LEVEL_A, f"{p}_fill", p, "charge")
    for n, (d, size, turns) in enumerate(CHARGE_PIECES, 1):
        out.append(bar([cx + d[0], LEVEL[1] + d[1], cz + d[2]], size, turns, f"{p}_piece{n}", p, "charge"))
    return out


def build_melt(i):
    """Hole i's melt (`h<i>melt`): the molten charge's surface in the pot, glowing."""
    p = f"h{i}melt"
    return oct_disc(HOLE_X[i], HOLE_Z, LEVEL[0], LEVEL[1], LEVEL_A, f"{p}_pool", p, "melt")


def pot_apothem(y):
    """The pot's outer apothem at height y: its widest section there (the foot, a tube)."""
    best = 0.0
    if POT_BASE[0] <= y <= POT_BASE[1]:
        best = POT_BASE[2]
    for _, t0, t1, a in POT_TUBES:
        if t0 <= y <= t1:
            best = max(best, a)
    return best


def coke_opening(j):
    """The opening layer j leaves for the pot: the pot's widest section over the layer's height, so the coke hugs it
    where it is widest and its top meets the pot with no gap (what lies under a ledge of the pot is not seen)."""
    y0, y1 = COKE_LAYERS[j - 1]
    widest = max([a for _, t0, t1, a in POT_TUBES if t0 < y1 and t1 > y0] +
                 ([POT_BASE[2]] if POT_BASE[0] < y1 and POT_BASE[1] > y0 else []))
    return widest


LUMP_TURNS = ((("y", 28.0), ("x", 21.0)), (("y", 63.0), ("z", 17.0)), (("y", 11.0), ("x", -24.0)), (("y", 47.0), ("z", -19.0)))


def build_coke(i, j, kind):
    """Hole i's coke layer j (`h<i>coke<j>`), or the same coke burning (`h<i>fire<j>`, kind "fire": the ember texture,
    its faces glowing): a square ring from the hole's walls to the pot's octagon, four strips and four corner fills,
    with a lump of coke lying in each corner on its top. The burning bottom layer also has cinders fallen through
    the grate onto the cave's floor."""
    p = f"h{i}{kind}{j}"
    tex = "coke" if kind == "coke" else "ember"
    cx, cz = HOLE_X[i], HOLE_Z
    y0, y1 = COKE_LAYERS[j - 1]
    a, h = coke_opening(j), HOLE_HALF
    out = [box([cx - h, y0, cz - h], [cx + h, y1, cz - a], f"{p}_n", p, tex),
           box([cx - h, y0, cz + a], [cx + h, y1, cz + h], f"{p}_s", p, tex),
           box([cx - h, y0, cz - a], [cx - a, y1, cz + a], f"{p}_w", p, tex),
           box([cx + a, y0, cz - a], [cx + h, y1, cz + a], f"{p}_e", p, tex)]
    depth = a * (math.sqrt(2.0) - 1.0)           # from the diagonal flat to the square's corner
    for name, deg in (("se", 45.0), ("sw", 135.0), ("nw", 225.0), ("ne", 315.0)):
        out.append(oct_slab(cx, cz, y0, y1, a, a + depth, 2 * a * OCT, deg, f"{p}_{name}", p, tex))
    for n, (sx, sz) in enumerate(((1, 1), (-1, 1), (-1, -1), (1, -1))):
        turns = LUMP_TURNS[(n + j) % 4]
        out.append(lump([cx + sx * LUMP_AT, y1 + 0.1, cz + sz * LUMP_AT], LUMP, turns, f"{p}_lump{n + 1}", p, tex))
    if kind == "fire" and j == 1:
        y = CAVE[1][0]
        for n, (dx, dz, w, d, deg) in enumerate(((-2.0, -1.0, 3.0, 2.4, 20.0), (1.6, 2.0, 2.6, 2.0, -35.0), (0.6, -3.0, 2.2, 1.8, 60.0)), 1):
            out.append(bar([cx + dx, y + 0.3, cz + dz], (w, 0.6, d), (("y", deg),), f"{p}_cinder{n}", p, tex))
    return out


def build():
    els = []
    for i in HOLES:
        els += build_cover(i) + build_pot(i) + build_charge(i) + build_melt(i)
        for kind in ("coke", "fire"):
            for j in (1, 2, 3):
                els += build_coke(i, j, kind)
    return els + build_frame()


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def step(axis, t0, t1, amount):
    return {"type": "step", "motion": "slide", "axis": axis, "from": r6(t0), "to": r6(t1), "amount": r6(amount / B)}


def cover_drivers(i):
    """Put on as depth goes 0 -> 1: lifted out of its bay, carried forward over the floor, set down into the hole's
    seat. Taken off the other way. Hole 1's also carries theta at ratio 0 (the clock; it moves nothing)."""
    drv = []
    if i == 1:
        drv.append({"type": "rotate", "axis": "y", "pivot": pt(HOLE_X[i], FLOOR_TOP, BAY_Z), "ratio": 0.0})
    drv += [step("y", 0.0, T_LIFT, LIFT), step("z", T_LIFT, T_SET, -(BAY_Z - HOLE_Z)), step("y", T_SET, 1.0, -LIFT)]
    return drv


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    parts = []
    for i in HOLES:
        for what in PARTS_OF_HOLE:
            pid = f"h{i}{what}"
            if what == "cover":
                parts.append({"id": pid, "match": [f"{pid}_*"], "requires": None, "drivers": cover_drivers(i)})
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
# A pose is (theta, depth, state): the clock, where the covers are, and what both holes hold (a STATES id). The
# rig sees theta and depth; the state says which parts are shown (gameplay's requires).
REST = (0.0, 0.0, "charged")


def inputs_of(pose):
    return {"theta": pose[0], "depth": pose[1]}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


def fitted(state, holes=HOLES):
    """The requires values fitted when the holes `holes` are in `state` (the others empty)."""
    what = dict((s[0], s[2]) for s in STATES)[state]
    return {f"h{i}{w}" for i in holes for w in what}


def on_show(part, state):
    """Whether a part is drawn with both holes in `state`: the covers and the frame always, the rest when fitted."""
    if part == "frame" or part.endswith("cover"):
        return True
    return part in fitted(state)


def shown(parts, posed_els, pose):
    """The posed elements as they can be seen in the pose's state: every part not drawn moved far away, each part by
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
    """The brickwork's twelve cells, then the four over the holes and the bays, which the covers pass through."""
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)] + list(LIFT_CELLS)


def regions():
    """Where the model may be (voxels, (lo, hi)): the brickwork's box, and the column over each hole and its bay."""
    out = [((0.0, 0.0, 0.0), (CELLS_X * B, CELLS_Y * B, CELLS_Z * B))]
    for i in HOLES:
        x = math.floor(HOLE_X[i] / B) * B
        out.append(((x, CELLS_Y * B, 0.0), (x + B, (CELLS_Y + 1) * B, CELLS_Z * B)))
    return out


ANCHORS = ("mouth1", "mouth2")


def mouth_point(i):
    """The middle of hole i's mouth at the floor: flames, the glow's light and the fire's sound when it is open."""
    return (HOLE_X[i], FLOOR_TOP, HOLE_Z)


def outlet_cell():
    return tuple(int(v // B) for v in ((STACK[0][0] + STACK[0][1]) / 2, FLOOR_TOP - 1.0, (STACK[2][0] + STACK[2][1]) / 2))


def air_cell():
    return tuple(int(v // B) for v in ((DUCT[0][0] + DUCT[0][1]) / 2, (DUCT[1][0] + DUCT[1][1]) / 2, DUCT[2][1] - 1.0))


def hole_cell(i):
    return (int(HOLE_X[i] // B), 1, int(HOLE_Z // B))


MIN_CHIMNEY = 6                              # CrucibleFurnaceConfig.MinChimneyHeight: chimney blocks on the outlet


def make_rig(parts):
    sx, sz = (STACK[0][0] + STACK[0][1]) / 2, (STACK[2][0] + STACK[2][1]) / 2
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the front middle "
                    "cell at ground level; the holes are in the front (north) row, hole 1 west and hole 2 east, and the "
                    "player works them from the front. No power: it is fired with coke. depth is a cover's place, 0 lifted "
                    "aside into its bay behind the hole (the rest pose), 1 seated in the hole: in the game each hole's cover "
                    "is posed with its own (h<i>cover with hole i's). theta is the melter's clock, carried by a ratio-0 "
                    "rotate on h1cover: nothing turns. requires: per hole i, h<i>pot, h<i>charge, h<i>melt, the coke's three "
                    "layers h<i>coke1..3 (two coke a layer, from the grate up) and the same layers burning, h<i>fire1..3. "
                    "The cells over each hole and its bay are hollow: the covers pass through them. holeNCell: the hole's "
                    "cell, worked from its top; outletCell: the stack, the chimney stood on its top; airCell and airFace: the "
                    "air port, a pipe to its south face; chimney: the shortest chimney on the outlet. See "
                    "CrucibleFurnace/README.md.",
        "cells": [],
        "hole1Cell": list(hole_cell(1)),
        "hole1Face": "up",
        "hole2Cell": list(hole_cell(2)),
        "hole2Face": "up",
        "outletCell": list(outlet_cell()),
        "outletFace": "up",
        "airCell": list(air_cell()),
        "airFace": "south",
        "chimney": {"origin": pt(sx, FLOOR_TOP + MIN_CHIMNEY * B / 2, sz), "axis": "y", "length": float(MIN_CHIMNEY)},
        "mouth1": {"pos": pt(*mouth_point(1))},
        "mouth2": {"pos": pt(*mouth_point(2))},
        "furnace": {"holes": len(HOLES), "cokeLayers": len(COKE_LAYERS), "cokePerLayer": COKE_PER_LAYER,
                    "minChimney": MIN_CHIMNEY,
                    "_comment": "holes: the melting holes, h1 and h2; cokeLayers, cokePerLayer: a hole's coke is drawn as "
                                "its layers h<i>coke1.. up to ceil(coke / cokePerLayer) (h<i>fire.. while lit); minChimney: "
                                "chimney blocks the outlet needs (CrucibleFurnaceSettings.MinChimneyHeight)."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0]."""
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    db = [v / B for v in d]
    ship_els = copy.deepcopy(els)
    translate(ship_els, d)
    ship_parts = copy.deepcopy(parts)
    for p in ship_parts:
        for drv in p["drivers"]:
            if "pivot" in drv:
                drv["pivot"] = [r6(drv["pivot"][k] + db[k]) for k in range(3)]
    ship = copy.deepcopy(rig)
    for key in ("hole1Cell", "hole2Cell", "outletCell", "airCell"):
        ship[key] = shift_cell(rig[key], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["chimney"] = {**rig["chimney"], "origin": shift_point(rig["chimney"]["origin"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


CELL_PARTS = ("frame", "h1cover", "h2cover")   # what the cells' boxes are made of: the brickwork, its iron and the
#                                              covers at rest in their bays; the holes' contents are inside the holes


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig; a lid on every top cell
    of the brickwork, at the floor, so no one falls into an open hole; the cells over the holes and bays hollow."""
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
    lift = {tuple(c[k] - ORIGIN_CELL[k] for k in range(3)) for c in LIFT_CELLS}
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        if pos in lift:
            out.append({"pos": list(pos), "hollow": True})
            continue
        cell = {"pos": list(pos), "boxes": cell_boxes(by_cell[pos], pos)}
        if c[1] == CELLS_Y - 1:
            cell["lid"] = 1.0
        out.append(cell)
    return out


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    poses = [REST, (0.0, 0.1, "lit"), (1.3, 0.5, "melted"), (-2.0, 0.85, "pot"), (0.0, 1.0, "charged")]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; {len(cells)} cells")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
REF_DEPTHS = (0.0, 0.05, 0.1, 0.2, 0.25, 0.4, 0.5, 0.6, 0.8, 0.85, 0.9, 0.95, 1.0)


def reference_poses():
    """depth over the covers' edges and between them, at theta 0, 1.1, -2.3 and 2.9 in turn."""
    return [((0.0, 1.1, -2.3, 2.9)[n % 4], dp) for n, dp in enumerate(REF_DEPTHS)]


def reference_json(ship_parts):
    poses = []
    for th, dp in reference_poses():
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], {"theta": th, "depth": dp})) for q in ship_parts}
        poses.append({"theta": th, "depth": dp, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped cruciblefurnace-rig.json's parts: each part's matrix as 3 "
                        "rows of 4 (block units) at each pose (depth, a cover's place; theta moves nothing). The site's and "
                        "the mod's tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    shape = machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Texture codes the renderer may "
             "set: 'melt' and 'charge' (the pot's metal). The burning coke ('ember') and the melt glow. Keep element names "
             "when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)
    for e in shape["elements"]:
        glow = FIRE_GLOW if "fire" in e["name"].split("_")[0] else MELT_GLOW if "melt" in e["name"].split("_")[0] else 0
        if glow:
            for f in e["faces"].values():
                f["glow"] = glow
    return shape


def coplanar_poses():
    return tuple((0.0, dp, st) for st in ("charged", "melted", "burntlow", "pulled", "pot") for dp in (0.0, 0.1, 0.5, 0.9, 1.0))


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
