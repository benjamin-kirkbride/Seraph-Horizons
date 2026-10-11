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
(machinegen.coil, which draws the draw bench's follower spring too). Every piece is a rigid bar: in each of 24
equal slots of W a side bends off the straight rod's shoulder end round its corner, across the next face,
while the rod hops over that face along an arc above it to the side's far corner, where it comes down on the
side's end and the next side is bent off it: so the rod goes round the mandrel a face a slot, never stopping,
and is used up from its shoulder end, a wire's width a side, its far end never moving (the wire's real
length is not kept: the coil is six times longer than the rod). At W 1 the spring is wound, on the mandrel; gameplay then
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
from machinegen.geometry import IDENT, El, flatten, mmul, mvec, rot, translate  # noqa: E402
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
ROD = WIRE + STEP                            # the straight rod: a side waits inside it before it is bent off it
# the first side's centre line starts this far from the shoulder: a side's tilted end reaches w/2 * sqrt 2 back along
# the mandrel, and must clear the collar
SPRING_Z0 = SHOULDER_Z[1] + WIRE / 2 * math.sqrt(2) + 0.01
COIL = Coil((X0, YM), MH, WIRE, COIL_PITCH, SPRING_Z0, COIL_TURNS)
# the straight rod's far end (its centre line's): fixed for the whole winding, as far along as the last side reaches
# lying along the rod before it is bent off it, so the rod hides every side until it is bent and is used up by the last
ROD_END = COIL.corner(COIL.sides - 1)[2] + COIL.length
SIDE_STORE = (X0, YM, 8.0)                   # where a side waits for its turn: inside the mandrel's body
ROD_STORE = (X0, YM, 2.0)                    # where a spent stretch of the straight rod goes (its start): inside the body
# The rod's hop over a face, corner to corner: up off the corner along a 45-degree arc to HOP_RISE over the coil's layer,
# straight across the face at that height, and down the same arc onto the far corner. Every face's hop leaves and lands
# at 45 degrees, so the path round the mandrel has no kink at a corner, and the rod rides HOP_RISE - WIRE / 2 - ROD / 2
# over the side being laid under it. 0.75 keeps the rod inside the station's box over the top (its top face 15.96 of 16)
# and clear of the side's top by 0.14; a circle through the corners would leave the box there by 0.16.
HOP_RISE = 0.75
HOP_TAKEOFF = math.pi / 4
# The bend's profile over a slot of W (the slot is the rod's hop; the side is bent off the rod in it): (fraction of the
# slot, fraction of the angle turned by then). The side turns slowly while the rod lifts off it, through most of its
# angle while the rod is up over the face, and eases home as the rod comes down onto its end: so the two never cut into
# each other away from their joint, and the coil's growing end never stops dead.
BEND = ((0.2, 0.04), (0.8, 0.97), (1.0, 1.0))
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
# The winding is planned piece by piece (`plan`), every piece a rigid bar. At W 0 the straight rod (`spr0`) lies along the
# mandrel's top east edge (the coil's first corner line), from the shoulder to ROD_END, a little past the nose. W is cut
# into 24 equal slots, one a side. In side j's slot, from the top east corner A round the top west B, the bottom west C
# and the bottom east D (three instants at its start, then the slot):
#   - the side, waiting inside the mandrel's body, is put inside the rod along its shoulder end, from its corner;
#   - the rod is swapped for the next stretch of it (`spr<j+1>`), a wire's width shorter at its shoulder end: the stretch
#     it loses lies inside the side, so nothing seen changes; the spent one goes back inside the mandrel;
#   - over the slot, together: the side is bent off the rod round its corner (a turn about the face's normal through it,
#     across the face into its place on the coil, BEND's profile), while the rod hops over that face to the side's far
#     corner, carried along an arc (ARC_CENTRE) above the side as it is laid, never turning (a turn about its own axis
#     undoes the arc's), and comes down on the side's end as the side eases home. The next slot's side is then bent off
#     it there.
# So the straight rod goes round the mandrel a face a slot, always along it, used up from its shoulder end; its far end
# never moves, and it never stops: each hop starts as the last lands. The coil's wire is six times the rod's length: the
# side bent off it is the one thing that is not the rod's own length (the wire's length is not kept).
def spring_name(j):
    return f"sp{COIL.name(j)}"


def spring_across(j):
    """Side j's wire across (0-based j): the top and bottom sides full, the west and east sides STEP thinner on each face,
    so no two neighbours share a face's plane."""
    return EVEN if j % 2 == 0 else ODD


def rod_name(j):
    """The straight rod as side j is bent off it: `spr0` at W 0, one stretch a side, each a wire's width shorter."""
    return f"spr{j}"


def _tr(r):
    return [[r[j][i] for j in range(3)] for i in range(3)]


def _col(r, k):
    return [r[i][k] for i in range(3)]


def _at(p, v, d):
    return [p[i] + v[i] * d for i in range(3)]


def _rot_pose(pose, axis, ang, pivot):
    """A pose (rotation, centre line's start) turned `ang` radians about `axis` through `pivot`."""
    r = rot(axis, math.degrees(ang))
    rm, s = pose
    q = mvec(r, [s[i] - pivot[i] for i in range(3)])
    return mmul(r, rm), [pivot[i] + q[i] for i in range(3)]


def _slide_pose(pose, v):
    return pose[0], [pose[1][i] + v[i] for i in range(3)]


def _ang(a):
    return r6(a)


def _pivot(p):
    """A pivot as the rig writes it (blocks to 6 places), in voxels."""
    return [r6(v / B) * B for v in p]


def _shift(v):
    """A slide as the rig writes it (blocks to 6 places, per axis), in voxels."""
    return [r6(x / B) * B for x in v]


class Piece:
    """A bar of the rod, square `across`, along its local z from `lo` to `hi` past its centre line's start; its centre
    line runs `run` from there (a side's is the coil's side, its bar half a wire longer at each end). `home` is where it
    is authored, (rotation, centre line's start); `pose` where its drivers have taken it so far; `seen` the millionths
    of W over which it can be seen, [first, last] pairs."""

    def __init__(self, pid, across, lo, hi, run, home, kind, j=None):
        self.pid, self.across, self.lo, self.hi, self.run, self.kind, self.j = pid, across, lo, hi, run, kind, j
        self.home = self.pose = home
        self.drivers = []
        self.seen = []

    def show(self, t):
        self.seen.append([t, US])

    def hide(self, t):
        self.seen[-1][1] = t

    def start(self):
        return self.pose[1]

    def end(self):
        return _at(self.pose[1], _col(self.pose[0], 2), self.run)

    def element(self):
        r, s = self.home
        el = El(f"{self.pid}_wire", [self.across, self.across, self.hi - self.lo], _at(s, _col(r, 2), (self.lo + self.hi) / 2),
                [row[:] for row in r], {}, self.pid)
        return skin(el, "spring")


def _rotate(pieces, t0, t1, axis, ang, pivot):
    a, pv = _ang(ang), _pivot(pivot)
    for pc in pieces:
        pc.drivers.append({"type": "gauge", "motion": "rotate", "axis": axis, "pivot": pt(*pivot), "amount": spring_amount(a),
                           "windows": [win(t0 / US, t1 / US)]})
        pc.pose = _rot_pose(pc.pose, axis, a, pv)


def _slide(pieces, t0, t1, v):
    d = _shift(v)
    for pc in pieces:
        for i, axis in enumerate("xyz"):
            if d[i]:
                pc.drivers.append({"type": "gauge", "motion": "slide", "axis": axis, "amount": spring_amount(d[i] / B),
                                   "windows": [win(t0 / US, t1 / US)]})
        pc.pose = _slide_pose(pc.pose, d)


def _place(pc, t, target):
    """In an instant (W t .. t + 1 millionth): `pc` turned and moved to `target` (rotation, centre line's start), turning
    about x, y, then z through where it starts, then sliding."""
    q = mmul(target[0], _tr(pc.pose[0]))
    b = -math.asin(max(-1.0, min(1.0, q[2][0])))
    if abs(math.cos(b)) < 1e-3:
        raise ValueError(f"{pc.pid}: no clean turn to its place")
    a, g = math.atan2(q[2][1], q[2][2]), math.atan2(q[1][0], q[0][0])
    s = list(pc.pose[1])
    for axis, ang in (("x", a), ("y", b), ("z", g)):
        if _ang(ang):
            _rotate([pc], t, t + 1, axis, ang, s)
    _slide([pc], t, t + 1, [target[1][i] - pc.pose[1][i] for i in range(3)])


def side_pose(j):
    """Side j wound: (rotation, centre line's start), as machinegen.coil puts it."""
    return COIL.side(j, "x", "x", "spring").r, list(COIL.corner(j))


_PLAN = []
WINDOWS = []                                 # the plan's steps in order: (kind, first, last millionth of W): "instant", or
                                             # "slot" (side j bent off the rod while the rod hops over its face)
INSTANTS = 3                                 # at the start of each slot: put, take, away


def plan():
    """Every piece of the rod, its drivers and when it can be seen (cached: the rig and the shape read the same plan)."""
    if not _PLAN:
        _PLAN.extend(_plan())
    return _PLAN


def corner(j):
    return list(COIL.corner(j))


def slot(j):
    """Side j's slot of W, in millionths: (first, last). Its INSTANTS come first; the rest is the hop and the bend."""
    return int(round(j * US / COIL.sides)), int(round((j + 1) * US / COIL.sides))


def _turn(c, p, q):
    """The angle about +z, from the centre c, from point p to point q (radians, signed)."""
    u, v = (p[0] - c[0], p[1] - c[1]), (q[0] - c[0], q[1] - c[1])
    return math.atan2(u[0] * v[1] - u[1] * v[0], u[0] * v[0] + u[1] * v[1])


def hop(j):
    """The rod's hop over face j, from corner j to corner j + 1, as segments in order, each (kind, start (x, y), end (x, y),
    centre (x, y), angle about +z, length): an "arc" up off corner j, HOP_TAKEOFF steep, bending over until it runs along
    the face at HOP_RISE over the coil's layer; "straight" across; an "arc" down onto corner j + 1 the same way."""
    axis, sign = COIL.normal(j)
    n = [0.0, 0.0]
    n["xy".index(axis)] = sign
    a, b = corner(j)[:2], corner(j + 1)[:2]
    run = math.hypot(b[0] - a[0], b[1] - a[1])
    t = [(b[0] - a[0]) / run, (b[1] - a[1]) / run]
    r = HOP_RISE / (1 - math.cos(HOP_TAKEOFF))
    adv = r * math.sin(HOP_TAKEOFF)
    if 2 * adv > run:
        raise ValueError("the hop's arcs overlap: HOP_RISE too high or HOP_TAKEOFF too flat")
    c1 = [a[k] + r * (math.sin(HOP_TAKEOFF) * t[k] - math.cos(HOP_TAKEOFF) * n[k]) for k in range(2)]
    p1 = [c1[k] + r * n[k] for k in range(2)]
    c2 = [b[k] + r * (-math.sin(HOP_TAKEOFF) * t[k] - math.cos(HOP_TAKEOFF) * n[k]) for k in range(2)]
    p2 = [c2[k] + r * n[k] for k in range(2)]
    arc = r * HOP_TAKEOFF
    return [("arc", a, p1, c1, _turn(c1, a, p1), arc),
            ("straight", p1, p2, None, 0.0, run - 2 * adv),
            ("arc", p2, b, c2, _turn(c2, p2, b), arc)]


def hop_off(j, p):
    """How far the point p (x, y) lies off hop j's path (voxels), and how far along it (0 .. 1)."""
    segs = hop(j)
    total = sum(s[5] for s in segs)
    best, done = 1e9, 0.0
    for kind, a, b, c, ang, length in segs:
        if kind == "straight":
            d = [b[k] - a[k] for k in range(2)]
            f = max(0.0, min(1.0, sum((p[k] - a[k]) * d[k] for k in range(2)) / (length * length)))
            off = math.hypot(*[p[k] - a[k] - f * d[k] for k in range(2)])
        else:
            r = math.hypot(p[0] - c[0], p[1] - c[1])
            turned = _turn(c, a, p)
            f = max(0.0, min(1.0, turned / ang)) if ang else 0.0
            at = _rot_xy(c, a, f * ang)
            off = math.hypot(p[0] - at[0], p[1] - at[1])
        if off < best:
            best, along = off, (done + f * length) / total
        done += length
    return best, along


def _rot_xy(c, p, ang):
    """p turned ang about +z through c (x, y)."""
    u = (p[0] - c[0], p[1] - c[1])
    return [c[0] + u[0] * math.cos(ang) - u[1] * math.sin(ang), c[1] + u[0] * math.sin(ang) + u[1] * math.cos(ang)]


def _plan():
    n, run = COIL.sides, COIL.length
    half, rh = WIRE / 2, ROD / 2
    side_store, rod_store = (IDENT, list(SIDE_STORE)), (IDENT, list(ROD_STORE))
    sides = [Piece(spring_name(j), spring_across(j), -half, run + half, run, side_store, "side", j) for j in range(n)]
    # rod j runs from corner j's height along the mandrel to ROD_END, reaching half its width past either end, as a side
    # does: the side bent off it, or the one it ends on, meets it with no step
    rods = []
    for j in range(n):
        length = ROD_END - COIL.corner(j)[2]
        rods.append(Piece(rod_name(j), ROD, -rh, length + rh, length, rod_store, "rod", j))
    rods[0].home = rods[0].pose = (IDENT, corner(0))
    rods[0].show(0)

    for j in range(n):
        side, rod = sides[j], rods[j]
        nxt = rods[j + 1] if j + 1 < n else None
        t, t_end = slot(j)
        # put: the side inside the rod, along its shoulder end from its corner
        gap = max(abs(rod.pose[1][i] - corner(j)[i]) for i in range(3))
        if gap > 1e-4:
            raise ValueError(f"{rod.pid} is not at corner {j} ({gap:.1e})")
        _place(side, t, (IDENT, corner(j)))
        WINDOWS.append(("instant", t, t + 1))
        t += 1
        # take: the next stretch of the rod in place of this one, a wire's width shorter at its shoulder end
        if nxt is not None:
            c = corner(j)
            _place(nxt, t, (IDENT, [c[0], c[1], COIL.corner(j + 1)[2]]))
            nxt.show(t + 1)
        side.show(t + 1)
        rod.hide(t)
        WINDOWS.append(("instant", t, t + 1))
        t += 1
        # away: the spent stretch back inside the mandrel
        _place(rod, t, rod_store)
        WINDOWS.append(("instant", t, t + 1))
        t += 1
        if t != slot(j)[0] + INSTANTS:
            raise ValueError("a slot has more instants than INSTANTS")
        # the slot: the side bent off the rod's end by BEND's profile ...
        axis, ang = COIL.swing(j)
        f0, g0 = 0.0, 0.0
        for f1, g1 in BEND:
            a, b = t + int(round(f0 * (t_end - t))), t + int(round(f1 * (t_end - t)))
            _rotate([side], a, b, axis, ang * (g1 - g0), corner(j))
            f0, g0 = f1, g1
        # ... while the rod hops over the face, its segments at one speed: carried, not turned (on an arc, a turn about its
        # own axis undoes the arc's turn about the arc's centre, so its square section stays as it lies)
        if nxt is not None:
            segs = hop(j)
            total = sum(s[5] for s in segs)
            a, done = t, 0.0
            for kind, p, q, c, ang, length in segs:
                done += length
                b = t + int(round(done / total * (t_end - t)))
                if kind == "straight":
                    _slide([nxt], a, b, [q[0] - p[0], q[1] - p[1], 0.0])
                else:
                    _rotate([nxt], a, b, "z", -ang, nxt.pose[1])
                    _rotate([nxt], a, b, "z", ang, [c[0], c[1], 0.0])
                a = b
        WINDOWS.append(("slot", t, t_end))
    if WINDOWS[-1][2] != US:
        raise ValueError(f"the winding ends at {WINDOWS[-1][2]}, not {US}")
    for pc in sides:
        gap = max(max(abs(pc.pose[1][i] - side_pose(pc.j)[1][i]) for i in range(3)),
                  max(abs(pc.pose[0][a][b] - side_pose(pc.j)[0][a][b]) for a in range(3) for b in range(3)))
        if gap > 1e-4:
            raise ValueError(f"{pc.pid} does not end where the coil puts it ({gap:.1e})")
    for pc in rods:
        gap = max(abs(pc.pose[1][i] - rod_store[1][i]) for i in range(3))
        if gap > 1e-4:
            raise ValueError(f"{pc.pid} does not end in the store ({gap:.1e})")
    return sides + rods


def build_spring():
    """The rod's pieces, each where it is authored: the straight rod as it lies at W 0, along the mandrel's top east edge
    from the shoulder, and every other piece (the sides, the rod's later stretches) inside the mandrel's body. Every face
    wears `spring`."""
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
                             "end a side at a time, the rod hopping over each face as the side is laid on it; turnsPerRod: the pace, "
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


_PIECES = {}


def piece(part):
    """The plan's piece for a spring part's id."""
    if not _PIECES:
        _PIECES.update({pc.pid: pc for pc in plan()})
    return _PIECES[part]


def spring_seen(part, W):
    """Whether a piece of the rod can be seen at W: a side from when the rod it waits in is swapped for a shorter
    stretch (it is then the rod's shoulder end, and is bent off it), a stretch of the rod until the next one takes its
    place; never while it waits in the mandrel or inside the rod."""
    u = W * US
    return any(a - 1e-3 <= u <= b + 1e-3 for a, b in piece(part).seen)


def seen(part, pose):
    return on_show(part, pose) and not (is_spring(part) and not spring_seen(part, pose[1]))


def shown(posed_els, pose):
    """The posed elements as they can be seen: another job's or metal's work, and the spring's pieces out of sight,
    moved far away (copies, in order)."""
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
