#!/usr/bin/env python3
"""Generate the mandrel forging station's shapes, rig and reference poses.

The mandrel station is a smith's hand station of the 1700s: a squared oak stump with an iron hoop,
carrying on its top an iron bracket (a base plate and two strap bands) that holds the root of a square
iron mandrel horizontally, cantilevered out over the stump's far end like an anvil's beak, with a
shoulder where it leaves the bracket and a tapered nose; beside the bracket, a small bottom swage with
a square groove. A hollow section (vanilla's chute section: an 8 x 8 box, 8 long, 1-voxel walls) is
slipped over the mandrel against the shoulder and hammered down onto it blow by blow: as the blows go on it
closes from 8 to 6 across and stretches from 8 to 16 long, evenly along its whole length, in one motion.
The box is drawn as eight short rings (no rig driver scales a part): they overlap at first, and spread
along the mandrel as it stretches, each by its index times the stretch, while every ring's walls and
corner bars slide in, over the whole work, W 0..1. At W 1 the finished tube, 16 long, its far end at the
tip, stays on the mandrel; gameplay then delivers the two pipe sections (the joint between rings 4 and 5
is where it is parted) and stops drawing the work. No hammer is modelled: the player holds it.

Its second job winds a coil spring on the same mandrel: a hot steel rod laid straight along the mandrel's
top east edge, from the shoulder to a little past the nose, is wound round the fixed mandrel from its
shoulder end (by hand, with tongs: neither is modelled), a side at a time, into a square coil of six turns
(machinegen.coil, which draws the draw bench's follower spring too). Each side is one bar that grows from its
start corner along its own line across the face, as wire laid down, over its slot of W (24 equal slots, one a
side; a stretching gauge), while the rod, one bar the coil's length, moves across that face with the side's
growing end, its shoulder end at the bend, shrinking into the coil a wire's width a side (a stretching gauge
about its far end face): so the rod goes round the mandrel a face a side, never stopping, used up from its
shoulder end, its far end never moving, and is gone as the last side ends (the wire's real length is not
kept: the coil is six times longer than the rod). At W 1 the spring is wound, on the mandrel; gameplay then
delivers it and stops drawing it. The finished coil is also the spring item's shape.
Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    mandrelstation.json         the whole station, every moving part   (assets/.../shapes/block/)
    mandrelstation_frame.json   the static frame only (block and item)  (assets/.../shapes/block/)
    mandrelstation-rig.json     cells, anchors and the part rig         (assets/.../config/)
    rig-reference.json          every part's matrix at a grid of poses  (tests/MandrelStation/)
    spring.json                 the spring item: the wound coil         (assets/.../shapes/item/)

or, with `--out DIR`, all of them into DIR (the item into DIR/item/). It validates its own output
(validate_mandrelstation.py) and exits non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from
the machine box's north-west-bottom corner (the "build frame"); the controller cell is the build
frame's [0,0,0], so the shipped files are the build frame divided by 16. The mandrel runs along z: its
root in the bracket at the north end (the controller, nearest the player), its tip to the south.

The rig's inputs, as this station uses them (the press brake's README, "Rig schema"):

    theta  the hammer's clock (2 pi a blow; for a spring, the held right-click's, a turn a second): read
           by a ratio-0 rotate on the mandrel, it moves nothing
    W      the rig's work, 0..1: the forging of one hollow (blows struck over blows needed), or the
           winding of one spring (the plan's steps, by their pace)
    k      the work's class, which is the job: 0 none, 1 a lead hollow (thin), 2 a copper hollow (thick), 3 a
           spring; each job's parts move for its class alone
    p      its presence, 0..1, eased as the work goes on and after it is delivered
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
from machinegen.coil import Coil  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import IDENT, El, euler_xyz, flatten, from_euler, translate  # noqa: E402
from machinegen.output import (reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               shift_point, worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, progress_of, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "MandrelStation" / "rig-reference.json"
ITEM_OUT = MOD / "assets" / "seraphhorizons" / "shapes" / "item" / "spring.json"
SCRIPT = "mods-src/seraphhorizons/MandrelStation/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 1, 1, 2          # one wide, one high, two long (z)
ORIGIN_CELL = (0, 0, 0)                      # the controller: the stump, nearest the player who placed it

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "iron": "game:block/metal/plate/iron",
    "mandrel": "game:block/metal/plate/iron",   # the mandrel: the renderer sets it to the fitted rod's metal
    "lead": "game:block/metal/sheet/lead1",     # vanilla's chute section is sheet/{metal}1
    "copper": "game:block/metal/sheet/copper1",
    "spring": "game:block/metal/sheet-plain/steel1",   # the spring's wire: its own code, so a renderer can tint or swap it hot
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the stump, its hoop
STUMP_X = (1.0, 15.0)
STUMP_Z = (0.5, 13.5)
YT = 7.0                                     # the stump's top
HOOP_Y = (4.5, 5.5)
HOOP_T = 0.3                                 # the hoop's thickness, outside the stump's faces

# ---------------------------------------------------------------- the mandrel and its bracket
X0 = 8.0                                     # the mandrel's axis (x)
YM = 12.6                                    # the mandrel's axis (y): the hollow hung on it clears the stump
MH = 2.0                                     # the mandrel's half width: a 4 x 4 square, the tube's bore
ROOT_Z = 0.8                                 # the root's end, in the bracket
SHOULDER_Z = (6.4, 7.0)                      # the collar the hollow is driven against
COLLAR_H = 2.6                               # the collar's half width
NOSE_Z = (22.3, 23.5)                        # the tapered nose; the tip at 23.5
NOSE_H = 1.6
BASE = ((4.8, 11.2), (YT, YT + 0.6), (0.8, 6.4))   # the bracket's base plate (x, y, z)
BANDS_Z = ((1.0, 2.6), (4.6, 6.2))           # the two strap bands round the root
BAND_X = (X0 - MH - 0.6, X0 + MH + 0.6)
BAND_TOP = 0.8                               # the cap's thickness over the mandrel
SWAGE_X = (11.8, 14.8)
SWAGE_Z = (1.0, 6.0)
SWAGE_Y = (YT, YT + 1.4, YT + 2.4)           # its block's top, the groove's lips' top
GROOVE_X = (12.8, 13.8)

# ---------------------------------------------------------------- the work
Z0 = SHOULDER_Z[1]                           # the hollow's near end, against the shoulder
L = 8.0                                      # the hollow's length, and a section's
OUT = 4.0                                    # the box's outer half width (8 across)
SEC = 3.0                                    # a section's outer half width (6 across)
WALL = 1.0
CLOSE = OUT - SEC                            # each wall closes this far
N_RINGS = 8                                  # the hollow is drawn as this many short rings along the axis
SECTION_RINGS = N_RINGS // 2                 # a section's rings
RING_L = 2.1                                 # a ring's length: when forged a section's rings overlap a little, 8 long
START_STEP = (L - RING_L) / (N_RINGS - 1)    # ring to ring at rest: overlapping, filling L
END_STEP = (L - RING_L) / (SECTION_RINGS - 1)   # ring to ring in a section when forged
NEAR = range(SECTION_RINGS)                  # the near section's rings (from the shoulder); the rest are the far one's
RECESS = 0.03                                # the side walls' ends stand back this far, the corner bars' twice: no two
                                             # end faces of a ring share a plane, so the z-fighting fix leaves them be
TIP = NOSE_Z[1]
# The hollow hangs on the mandrel: its bore's ceiling (the top wall's underside) rests on the bar's top face at
# every W, the gap below the bar. Its cross-section's centre is HANG0 below the axis at rest and HANG1 when
# closed; the whole section rises by the difference as the bottom wall comes up and the sides close in, so
# the top wall stays on the bar.
HANG0 = (OUT - WALL) - MH                    # at rest: a 6 bore on a 4 bar, its centre 1 below the axis
HANG1 = (SEC - WALL) - MH                    # closed: a 4 bore on a 4 bar, centred
LIFT = HANG0 - HANG1                         # how far the section's centre rises as it closes

# ---------------------------------------------------------------- the cycle (t = W, one hollow)
T_FORGE = (0.0, 1.0)                         # closes from 8 to 6 across and stretches from 8 to 16, evenly, the whole work
BLOWS = {"thin": 9.0, "thick": 14.0}          # the pace: blows a hollow

# ---------------------------------------------------------------- the spring (a hot steel rod wound on the mandrel)
WIRE = 0.6                                   # the spring's square wire: six turns of it hold the game's rod's metal
                                             # (2 x 2 x 10 = 40 voxels; 24 sides of 4.64, 0.6 square, are 40.2)
COIL_TURNS = 6
COIL_PITCH = 2.4                             # wound free: the coil, 15.1 long, fits the mandrel from the shoulder to the nose
STEP = 0.015                                 # sizes across the wire step by this (half of it on each face, more than the
                                             # z-fighting check's 0.005): no two pieces that meet or nest share a face's plane
EVEN = WIRE                                  # the top and bottom sides (u, d), full size
ODD = WIRE - 2 * STEP                        # the west and east sides (w, e): no two neighbours share a face's plane
ROD = WIRE + STEP                            # the straight rod: a little wider than the wire, its end inside the tip
# the first side's centre line starts this far from the shoulder: a side's tilted end reaches w/2 * sqrt 2 back along
# the mandrel, and must clear the collar
SPRING_Z0 = SHOULDER_Z[1] + WIRE / 2 * math.sqrt(2) + 0.01
COIL = Coil((X0, YM), MH, WIRE, COIL_PITCH, SPRING_Z0, COIL_TURNS)
# the straight rod: along the mandrel on the coil's first corner line, from ROD_BACK before the coil's first corner (so
# its end covers the first side's back end as it grows, tilt and all) to ROD_BACK before the coil's last corner: 14.4
# long, the coil's. It shrinks a wire's width a side from its shoulder end, about its far face, and is gone as the last
# side ends
ROD_BACK = 0.35
US = 1_000_000                               # W in millionths: every window starts and ends on one
WIND_TURNS = 4.0                            # the pace: turns of the held right-click's clock (a turn a second) a spring
SPRING_ROD = "game:rod-steel"
SPRING_ITEM = "seraphhorizons:spring-steel"

METALS = (("thin", "l", "lead", "hollowlead"), ("thick", "c", "copper", "hollowcopper"))
# a ring's walls and corner bars: (name, x direction to the axis, y direction to the axis)
SIDES = (("u", 0, -1), ("d", 0, 1), ("e", -1, 0), ("w", 1, 0), ("ue", -1, -1), ("uw", 1, -1), ("de", -1, 1), ("dw", 1, 1))


# ---------------------------------------------------------------- box helpers
def skin(el, tex):
    """Every face takes `tex`, its UVs a region of the texture in proportion to the face's size."""
    axes = {"north": (0, 1), "south": (0, 1), "east": (2, 1), "west": (2, 1), "up": (0, 2), "down": (0, 2)}
    el.faces = {}
    for d, (u, v) in axes.items():
        w = min(abs(el.size[u]) * TEX / 16, TEX)
        h = min(abs(el.size[v]) * TEX / 16, TEX)
        el.faces[d] = {"texture": "#" + tex, "uv": [0.0, 0.0, w, h]}
    return el


def box(lo, hi, name, part, tex):
    c = [(lo[k] + hi[k]) / 2 for k in range(3)]
    return skin(El(name, [hi[k] - lo[k] for k in range(3)], c, [r[:] for r in IDENT], {}, part), tex)


def section_box(x0, x1, y0, y1, z0, z1, name, part, tex):
    """A box in the work's cross-section frame at rest: x and y measured from the hollow's centre, hung
    HANG0 below the mandrel's axis."""
    yc = YM - HANG0
    return box([X0 + x0, yc + y0, z0], [X0 + x1, yc + y1, z1], name, part, tex)


# ---------------------------------------------------------------- builders
def build_frame():
    """The oak stump and its iron hoop, the iron bracket (base plate, two bands of saddle, cheeks and
    cap round the mandrel's root), the swage with its square groove."""
    f = "frame"
    out = [box([STUMP_X[0], 0.0, STUMP_Z[0]], [STUMP_X[1], YT, STUMP_Z[1]], "fr_stump", f, "oak")]
    hx = (STUMP_X[0] - HOOP_T, STUMP_X[1] + HOOP_T)
    out.append(box([hx[0], HOOP_Y[0], STUMP_Z[0] - HOOP_T], [hx[1], HOOP_Y[1], STUMP_Z[0]], "fr_hoop_n", f, "iron"))
    out.append(box([hx[0], HOOP_Y[0], STUMP_Z[1]], [hx[1], HOOP_Y[1], STUMP_Z[1] + HOOP_T], "fr_hoop_s", f, "iron"))
    out.append(box([hx[0], HOOP_Y[0], STUMP_Z[0]], [STUMP_X[0], HOOP_Y[1], STUMP_Z[1]], "fr_hoop_w", f, "iron"))
    out.append(box([STUMP_X[1], HOOP_Y[0], STUMP_Z[0]], [hx[1], HOOP_Y[1], STUMP_Z[1]], "fr_hoop_e", f, "iron"))
    (bx, by, bz) = BASE
    out.append(box([bx[0], by[0], bz[0]], [bx[1], by[1], bz[1]], "fr_base", f, "iron"))
    m_lo, m_hi = YM - MH, YM + MH
    for i, (z0, z1) in enumerate(BANDS_Z, 1):
        out.append(box([BAND_X[0], by[1], z0], [BAND_X[1], m_lo, z1], f"fr_band{i}_saddle", f, "iron"))
        out.append(box([BAND_X[0], m_lo, z0], [X0 - MH, m_hi, z1], f"fr_band{i}_w", f, "iron"))
        out.append(box([X0 + MH, m_lo, z0], [BAND_X[1], m_hi, z1], f"fr_band{i}_e", f, "iron"))
        out.append(box([BAND_X[0], m_hi, z0], [BAND_X[1], m_hi + BAND_TOP, z1], f"fr_band{i}_cap", f, "iron"))
    sy = SWAGE_Y
    out.append(box([SWAGE_X[0], sy[0], SWAGE_Z[0]], [SWAGE_X[1], sy[1], SWAGE_Z[1]], "fr_swage", f, "iron"))
    out.append(box([SWAGE_X[0], sy[1], SWAGE_Z[0]], [GROOVE_X[0], sy[2], SWAGE_Z[1]], "fr_swage_w", f, "iron"))
    out.append(box([GROOVE_X[1], sy[1], SWAGE_Z[0]], [SWAGE_X[1], sy[2], SWAGE_Z[1]], "fr_swage_e", f, "iron"))
    return out


def build_mandrel():
    """The mandrel (the `mandrel` stage): a square bar, its root in the bracket, a collar where it
    leaves it, a tapered nose."""
    p, t = "mandrel", "mandrel"
    return [
        box([X0 - MH, YM - MH, ROOT_Z], [X0 + MH, YM + MH, NOSE_Z[0]], "mandrel_body", p, t),
        box([X0 - COLLAR_H, YM - COLLAR_H, SHOULDER_Z[0]], [X0 + COLLAR_H, YM + COLLAR_H, SHOULDER_Z[1]], "mandrel_collar", p, t),
        box([X0 - NOSE_H, YM - NOSE_H, NOSE_Z[0]], [X0 + NOSE_H, YM + NOSE_H, NOSE_Z[1]], "mandrel_nose", p, t),
    ]


def ring_z(i):
    """Ring i's (0 from the shoulder) span along the axis at rest."""
    z0 = Z0 + i * START_STEP
    return z0, z0 + RING_L


def stretch(i):
    """How far ring i slides along the axis as the hollow stretches: from its place at rest to its place in
    its section, the sections end to end. The last ring moves L, the first none; between, nearly in
    proportion to the index (exactly, within each half)."""
    end = Z0 + L * (i // SECTION_RINGS) + END_STEP * (i % SECTION_RINGS)
    return end - ring_z(i)[0]


def ring_side(side):
    """One wall or corner bar of the box (the 8 across hollow), x and y from the axis."""
    o, i = OUT, OUT - WALL
    spans = {"u": ((-i, i), (i, o)), "d": ((-i, i), (-o, -i)), "e": ((i, o), (-i, i)), "w": ((-o, -i), (-i, i)),
             "ue": ((i, o), (i, o)), "uw": ((-o, -i), (i, o)), "de": ((i, o), (-o, -i)), "dw": ((-o, -i), (-o, -i))}
    return spans[side]


# The work's banding: every ring's faces seen along the length take their UVs from one sheet running the
# length of the work, each ring's continuing where the one before it ends, so no ring repeats the texture's
# first strip. A ring is rigid and slides, so the sheet can match from ring to ring at one W only; between
# two rings the texture is off by 4 texture units a voxel times how far their spacing has moved since that
# W. Mapped by W_BAND = 0.5, the middle of the stretch, the offset at rest and at W 1 is half the one a
# mapping by either end leaves at the other (under 0.6 voxels, about a pixel of the 32-pixel sheet).
W_BAND = 0.5


def band_z(i):
    """Ring i's start on the sheet (in voxels from the work's near end): its place at W_BAND."""
    return ring_z(i)[0] + W_BAND * stretch(i) - Z0


def band(el, i, x0, x1, y0, y1, z0, z1):
    """Ring i's element at rest (section frame x, y; z along the axis): the faces running along the axis take
    their UVs from the work's one sheet, along it by the ring's place at W_BAND, across it by the place in the
    cross-section, so walls and corner bars meet without a seam. Directions are the game's (CubeMeshUtil):
    up and down run v with +z; west runs u with +z, east against it (mirrored, so it reads on the same way);
    up runs u with +x, down against it; east and west run v down from the top. The end faces keep `skin`'s."""
    k = TEX / 16
    t0, t1 = k * (band_z(i) + z0 - ring_z(i)[0]), k * (band_z(i) + z1 - ring_z(i)[0])
    mt = k * (band_z(N_RINGS - 1) + RING_L)          # the sheet's far end: east faces count back from it
    tex = el.faces["up"]["texture"]
    across_x = (k * (x0 + OUT), k * (x1 + OUT))
    down_x = (k * (OUT - x1), k * (OUT - x0))
    across_y = (k * (OUT - y1), k * (OUT - y0))
    el.faces["up"] = {"texture": tex, "uv": [across_x[0], t0, across_x[1], t1]}
    el.faces["down"] = {"texture": tex, "uv": [down_x[0], t0, down_x[1], t1]}
    el.faces["west"] = {"texture": tex, "uv": [t0, across_y[0], t1, across_y[1]]}
    el.faces["east"] = {"texture": tex, "uv": [mt - t1, across_y[0], mt - t0, across_y[1]]}
    return el


def build_work():
    """For each metal: the box as N_RINGS overlapping rings of four walls and four corner bars each, on the
    mandrel against the shoulder, banded as one sheet along the work (`band`)."""
    out = []
    for _cls, pre, tex, _req in METALS:
        for i in range(N_RINGS):
            z0, z1 = ring_z(i)
            for s, _, _ in SIDES:
                (x0, x1), (y0, y1) = ring_side(s)
                kind = "wall" if len(s) == 1 else "bar"
                cut = (0.0 if s in ("u", "d") else RECESS) if kind == "wall" else 2 * RECESS
                el = section_box(x0, x1, y0, y1, z0 + cut, z1 - cut, f"{pre}{i + 1}{s}_{kind}", f"{pre}{i + 1}{s}", tex)
                out.append(band(el, i, x0, x1, y0, y1, z0 + cut, z1 - cut))
    return out


# ---------------------------------------------------------------- the spring
# The winding is planned piece by piece (`plan`): the 24 sides and one straight rod, 25 parts, every motion a gauge. Each
# side is one bar, authored where the coil puts it, and grows from its start corner along its own line over its slot of
# W (24 equal slots, one a side) by a stretching gauge about the corner: its back end (half a wire behind the corner,
# inside the end of the side before, or of the rod for the first side) and its leading end grow together, and before
# its slot it is stretched to nothing and not drawn. The rod lies along the mandrel on the coil's first corner line at
# W 0, from ROD_BACK before the coil's first corner to ROD_BACK before its last (the coil's length), and over the job
# shrinks from its shoulder end about its far end face (a stretching gauge: a wire's width a side) while it slides
# across each face with the growing side's leading end, which stays inside its end. So the wire goes from the rod into
# the coil at the moving bend, the rod goes round the mandrel a face a side, and nothing is left of it as the last side
# ends. The wire's real length is not kept (the coil is six times longer than the rod).
def spring_name(j):
    return f"sp{COIL.name(j)}"


def spring_across(j):
    """Side j's wire across (0-based j): the top and bottom sides full, the west and east sides STEP thinner on each face,
    so no two neighbours share a face's plane."""
    return EVEN if j % 2 == 0 else ODD


ROD_ID = "spr"                               # the straight rod's part


class Piece:
    """A part of the spring: a box `size` (x, y, z) about its centre `c`, turned by `r`, as authored (a side where the coil
    puts it, the rod as it lies at W 0); `kind` "side" (of side `j`) or "rod"; `drivers` as the rig writes them."""

    def __init__(self, pid, size, c, r, kind, j=None):
        self.pid, self.size, self.c, self.r, self.kind, self.j = pid, list(size), list(c), [row[:] for row in r], kind, j
        self.drivers = []

    def element(self):
        return skin(El(f"{self.pid}_wire", list(self.size), list(self.c), [row[:] for row in self.r], {}, self.pid), "spring")


def side_bar(j):
    """Side j's bar where the coil puts it (its element at W 1), of its own wire across. It is turned by the angles the
    shape file carries (rounded to SHAPE_PLACES), which its stretch names too, so the written element and its stretch
    share one frame exactly: a stretch along any other axis would shear the bar."""
    bar = COIL.side(j, "x", "x", "spring", across=spring_across(j))
    bar.r = from_euler(*side_angles(j))
    return bar


SHAPE_PLACES = 4                             # the shape writer's rounding of an element's rotations (machinegen.output)


def side_angles(j):
    """Side j's rotationX/Y/Z as the shape file writes them."""
    return [round(x, SHAPE_PLACES) + 0.0 for x in euler_xyz(COIL.side(j, "x", "x", "spring").r)]


_PLAN = []
WINDOWS = []                                 # the plan's slots in order: ("slot", first, last millionth of W)


def plan():
    """Every part of the spring, with its drivers (cached: the rig and the shape read the same plan)."""
    if not _PLAN:
        _PLAN.extend(_plan())
    return _PLAN


def corner(j):
    return list(COIL.corner(j))


def slot(j):
    """Side j's slot of W, in millionths: (first, last). Over it the side grows and the rod crosses its face."""
    return int(round(j * US / COIL.sides)), int(round((j + 1) * US / COIL.sides))


def grow_origin(j):
    """The point side j grows about: its start corner, so its back end (half a wire behind the corner) grows inside the
    end of the side before; for the first side, which has none, its start face, the wire's free end, so it grows forward
    only and that end is uncovered where it lies as the rod moves on."""
    if j:
        return corner(j)
    return [corner(0)[i] - COIL.direction(0)[i] * WIRE / 2 for i in range(3)]


def _plan():
    n = COIL.sides
    sides = []
    for j in range(n):
        bar = side_bar(j)
        pc = Piece(spring_name(j), bar.size, bar.c, bar.r, "side", j)
        a, b = slot(j)
        # grows from nothing over its slot about its start corner (the first side about its start face), along its own
        # axis, and stays whole
        pc.drivers.append({"type": "gauge", "motion": "stretch", "axis": "z", "origin": pt(*grow_origin(j)),
                           "rotation": side_angles(j), "amount": spring_amount(-1.0),
                           "windows": [{"from": r6(-FOREVER), "to": r6(b / US), "ease": r6((b - a) / US)}]})
        sides.append(pc)
        WINDOWS.append(("slot", a, b))
    c0 = corner(0)
    near, far = c0[2] - ROD_BACK, corner(n)[2] - ROD_BACK
    rod = Piece(ROD_ID, [ROD, ROD, far - near], [c0[0], c0[1], (near + far) / 2], IDENT, "rod")
    # shrinks from its shoulder end about its far end face, evenly over the job, to nothing ...
    rod.drivers.append({"type": "gauge", "motion": "stretch", "axis": "z", "origin": pt(c0[0], c0[1], far),
                        "amount": spring_amount(-1.0), "windows": [{"from": 0.0, "to": r6(FOREVER), "ease": 1.0}]})
    # ... while it slides across each face with the growing side's leading end
    for j in range(n):
        a, b = slot(j)
        d = [corner(j + 1)[i] - corner(j)[i] for i in range(2)]
        for i, axis in enumerate("xy"):
            if abs(d[i]) > 1e-9:
                rod.drivers.append({"type": "gauge", "motion": "slide", "axis": axis, "amount": spring_amount(d[i] / B),
                                    "windows": [win(a / US, b / US)]})
    if WINDOWS[-1][2] != US:
        raise ValueError("the winding does not end at W 1")
    return sides + [rod]


def build_spring():
    """The spring's parts as authored: each side where the coil puts it (stretched to nothing until its slot), and the
    straight rod as it lies at W 0, along the mandrel's top east edge. Every face wears `spring`."""
    return [pc.element() for pc in plan()]


def build():
    return build_mandrel() + build_work() + build_spring() + build_frame()


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def per_class(thin, thick=None):
    return {"thin": r6(thin), "thick": r6(thin if thick is None else thick)}


SPRING_CLASS = 3                             # the spring job's class: its own, past the hollows' lead (1) and copper (2)


def spring_amount(v):
    """A spring part's amount: for the spring's class alone (it stands still for the hollows' classes)."""
    return {str(SPRING_CLASS): r6(v)}


# W is one hollow's forging or one spring's winding: each of the three classes has a job of 1
WORK = {"name": "blows", "unit": "hollows", "step": 0.005, "end": {"thin": 1.0, "thick": 1.0, str(SPRING_CLASS): 1.0}}
PATH = WORK
FOREVER = 1000.0


def win(t0, t1):
    """A window rising over t0..t1 and open to the end."""
    return {"from": r6(t0), "to": r6(FOREVER), "ease": r6(t1 - t0)}


def slide(axis, dist, t):
    return {"type": "gauge", "motion": "slide", "axis": axis, "amount": per_class(dist / B), "windows": [win(*t)]}


def spring_parts():
    """The spring's parts (`requires` rodsteel), from the plan: every piece of the rod on its own (none rides another), its
    drivers gauges in the order the plan moves it (each a turn about a fixed axis through a fixed point, or a slide, in
    its own window of W; an instant's window is a millionth wide). Every amount is for the spring's class alone
    (SPRING_CLASS, 3): with a hollow's class, or none, the spring's parts stand still."""
    return [{"id": pc.pid, "match": [f"{pc.pid}_*"], "requires": "rodsteel", "drivers": copy.deepcopy(pc.drivers), "ride": None}
            for pc in plan()]


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    # theta is the hammer's clock: a ratio-0 rotate on the mandrel carries it and moves nothing
    parts = [{"id": "mandrel", "match": ["mandrel_*"], "requires": "mandrel",
              "drivers": [{"type": "rotate", "axis": "z", "pivot": pt(X0, YM, 0.0), "ratio": 0.0}]}]
    for _cls, pre, _tex, req in METALS:
        for i in range(N_RINGS):
            for s, dx, dy in SIDES:
                drv = []
                if dx:
                    drv.append(slide("x", dx * CLOSE, T_FORGE))
                # closing about the section's centre, which rises as it closes: the top wall stays on the bar
                if dy * CLOSE + LIFT:
                    drv.append(slide("y", dy * CLOSE + LIFT, T_FORGE))
                if i:
                    drv.append(slide("z", stretch(i), T_FORGE))
                parts.append({"id": f"{pre}{i + 1}{s}", "match": [f"{pre}{i + 1}{s}_*"], "requires": req, "drivers": drv})
    parts += spring_parts()
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []})
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses
REST = (0.0, 0.0, 0, 0.0)                     # (theta, W, k, p): no work; the authored pose


def inputs_of(pose):
    th, W, k, p = pose
    return {"theta": th, "work": W, "size": k, "presence": p}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path or PATH)


def pose_at(k, W, theta=None, p=1.0):
    """A hollow of metal k at W, the hammer's clock at its pace."""
    th = TAU * BLOWS[("thin", "thick")[k - 1]] * W if theta is None else theta
    return (th, W, k, p)


def spring_at(W, p=1.0, k=SPRING_CLASS):
    """A spring at W (its class, 3), the held clock at its pace."""
    return (TAU * WIND_TURNS * W, W, k, p)


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("output", "strike")


def output_point():
    """Where gameplay drops the two sections when it delivers them: on the ground beyond the tip, the middle
    of a pile of two (the tube itself stays on the mandrel at W 1 until then)."""
    return (X0, 2 * SEC, TIP + 0.5 + L / 2)


def strike_point():
    """Where the hammer lands: the top of the box's middle (its top wall lies on the bar throughout)."""
    return (X0, YM + MH + WALL, Z0 + L / 2)


def make_rig(parts):
    progress_of({"work": WORK})
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the stump, nearest the "
                    "player who placed it; the mandrel points south. A hand station: no power cell. work is the forging of one "
                    "hollow, W 0..1 (blows struck over blows needed), or the winding of one spring (wind); k is the job: the "
                    "hollow's metal (1 lead, 2 copper) or 3 a spring; gauge windows are placed in work units. theta is the hammer's (or "
                    "the held right-click's) clock, carried by a ratio-0 rotate on the mandrel. See the mandrel station's README "
                    "for the schema.",
        "cells": [],
        "infeedSide": "west",
        "outputSide": "south",
        "output": {"pos": pt(*output_point())},
        "strike": {"pos": pt(*strike_point())},
        "work": dict(WORK),
        "forge": {"blowsPerHollow": per_class(BLOWS["thin"], BLOWS["thick"]),
                  "hollows": {"thin": "game:chutesection-lead", "thick": "game:chutesection-copper"},
                  "sections": {"thin": "seraphhorizons:pipesection-lead", "thick": "seraphhorizons:pipesection-copper"},
                  "sectionsPerHollow": 2,
                  "_comment": "blowsPerHollow: the pace, hammer blows (right-clicks) a hollow; copper half as much again. "
                              "hollows: each class's work. sections: what a hollow makes, sectionsPerHollow of them."},
        "wind": {"turnsPerRod": r6(WIND_TURNS), "rod": SPRING_ROD, "class": SPRING_CLASS, "spring": SPRING_ITEM, "springsPerRod": 1,
                 "hot": ["rodsteel"],
                 "_comment": "The spring job: rod is its work, of its own class 3 (its parts, requires rodsteel, move for that "
                             "class alone), wound over W 0..1, the rod laid along the mandrel and wound round it from its shoulder "
                             "end a side at a time, each side grown from its corner as the rod shrinks into it; turnsPerRod: the pace, "
                             "turns of the held right-click's clock (a turn a second) a rod. spring: "
                             "what a rod makes, springsPerRod of them. hot: the requires values whose parts are drawn hot, with "
                             "vanilla's glow by the rod's temperature."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: the build frame's
    corner is the controller's). Gauge windows are in hollows, so they stay as they are."""
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
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts, sp):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig: the frame, the mandrel and
    the hollows as they go on; not the spring's rod, a thin bar that goes round the mandrel (its rest place, along the
    top east edge, would only widen the far cell's box by a rod's width). No lids: a hand station is not walked on."""
    written = [w for w in flatten(shape["elements"], textures={}) if not is_spring(part_of(ship_parts, w.name))]
    rest = [posed(w, _part_matrix(ship_parts, part_of(ship_parts, w.name), inputs_of(REST), sp)) for w in written]
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
    return out


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    sp = ship["work"]
    poses = [REST, pose_at(1, 0.2), pose_at(2, 0.5), pose_at(1, 0.7), pose_at(2, 0.9), pose_at(1, 0.99), (1.3, 0.3, 1, 0.6),
             spring_at(0.13), spring_at(0.52), spring_at(0.97), spring_at(1.0)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose), sp), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells {cells}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
REF_EDGES = (0.0, 0.01, 0.05, 0.1, 0.15, 0.2, 0.25, 0.333, 0.4, 0.45, 0.5, 0.55, 0.6, 0.7, 0.75, 0.8, 0.9, 0.99, 1.0)


def ref_spring():
    """The spring's W for the reference: four tenths into every third of the plan's slots (never near an instant's
    window, where a float's last bit could tip a piece's jump one way or the other), and its ends."""
    plan()
    timed = [(a, b) for kind, a, b in WINDOWS if kind != "instant"]
    return tuple(r6((a + 0.4 * (b - a)) / US) for a, b in timed[::3])


def reference_poses():
    """theta in {0, 1.1, -2.3, 2.9} with no work; for each metal, W over the cycle's edges, and for the spring (its class
    3) over its own and a few of them, with p 1 and every fifth also at p 0.4 (every part's matrix at every pose)."""
    out = [(th, 0.0, 0, 0.0) for th in (0.0, 1.1, -2.3, 2.9)]
    for k, edges in ((1, REF_EDGES), (2, REF_EDGES), (SPRING_CLASS, sorted(ref_spring() + (0.0, 1.0)))):
        for i, W in enumerate(edges):
            th = (0.0, 1.1, -2.3, 2.9)[i % 4]
            for p in ((1.0,) if i % 5 else (1.0, 0.4)):
                out.append((th, W, k, p))
    return out


def reference_json(ship_parts, sp):
    poses = []
    for pose in reference_poses():
        th, W, k, p = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose), sp)) for q in ship_parts}
        poses.append({"theta": th, "work": W, "size": k, "presence": p, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped mandrelstation-rig.json's parts and work: each part's matrix "
                        "as 3 rows of 4 (block units) at each pose (W in hollows; travel is |theta|). The site's and the mod's "
                        "tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. The mandrel's texture code is "
             "'mandrel': the renderer sets it to the fitted rod's metal. Keep element names when editing: the rig finds "
             "its parts by them.",
        TEXTURES, tex_size=TEX)


def spring_samples(fractions=(0.5,), every=1):
    """W inside the plan's slots: at each of `fractions` of every `every`-th."""
    plan()
    timed = [(a, b) for kind, a, b in WINDOWS if kind != "instant"]
    return [(a + f * (b - a)) / US for a, b in timed[::every] for f in fractions]


def coplanar_poses():
    # the hollows, and the spring at its start, halfway through every slot of its winding, early and late in every
    # third, and wound
    spring = [0.0] + spring_samples() + spring_samples((0.1, 0.9), 3) + [1.0]
    return (REST, pose_at(1, 0.1), pose_at(2, 0.25), pose_at(1, 0.4), pose_at(2, 0.5), pose_at(1, 0.6), pose_at(2, 0.7),
            pose_at(1, 0.8), pose_at(2, 0.9), pose_at(1, 0.95), pose_at(2, 1.0), pose_at(1, 1.0)) + tuple(spring_at(W) for W in sorted(spring))


def is_spring(part):
    return part.startswith("sp")


def on_show(part, pose):
    """Whether a part can be seen at a pose: each metal's hollow only with that metal's class, the spring only with its
    own (the class is the job)."""
    k = pose[2]
    if is_spring(part):
        return k == SPRING_CLASS
    if part[:1] in ("l", "c"):
        return k == ("l", "c").index(part[0]) + 1
    return True


def seen(part, pose):
    return on_show(part, pose)


def shown(posed_els, pose):
    """The posed elements as they can be seen: another job's or metal's work moved far away (copies, in order)."""
    out = []
    for e in posed_els:
        if not seen(e.part, pose):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def spring_item():
    """The spring item: the coil as the station leaves it wound (W 1: every side where machinegen.coil puts it, which
    check_spring holds the plan to), centred on the item box's floor (x and z on 8, the bottom at y 0), the coil along z,
    at scale 1."""
    src = [COIL.side(j, f"{spring_name(j)}_wire", spring_name(j), "spring", tex_size=TEX, across=spring_across(j))
           for j in range(COIL.sides)]
    cs = [q for el in src for q in el.corners()]
    lo = [min(q[i] for q in cs) for i in range(3)]
    hi = [max(q[i] for q in cs) for i in range(3)]
    d = [8.0 - (lo[0] + hi[0]) / 2, -lo[1], 8.0 - (lo[2] + hi[2]) / 2]
    return translate(src, d)


def item_shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}: the spring item ({SPRING_ITEM}), the coil the mandrel station winds from a steel rod, "
             "as it leaves it (mandrelstation.json's spring at W 1). Every element was made for the Seraph Horizons mod.",
        TEXTURES, tex_size=TEX)


def fix_coplanar(els, parts):
    # the rings overlap in stacks up to eight deep, so the fix needs more rounds than the default
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose), coplanar_poses(),
                              rounds=40)


def main():
    ap = argparse.ArgumentParser(description="Generate the mandrel station's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the files into this directory (the item into DIR/item/) instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_mandrelstation
    els = build()
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        for pose, pairs in before.items():
            print(f"coplanar faces before the fix at {pose}: {len(pairs)} pairs")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_mandrelstation.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "mandrelstation.json", args.out / "mandrelstation_frame.json", args.out / "mandrelstation-rig.json",
                args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "mandrelstation.json", SHAPE_DIR / "mandrelstation_frame.json", RIG_DIR / "mandrelstation-rig.json",
                REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts, ship["work"])
    ok = validate_mandrelstation.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship), reference_dumps(reference_json(ship_parts, ship["work"])))
    item = spring_item()
    ok = validate_mandrelstation.check_item(sys.modules[__name__], item) and ok
    outs += ((args.out / "item" / "spring.json") if args.out else ITEM_OUT,)
    texts += (shape_dumps(item_shape_json(item)),)
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
