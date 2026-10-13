#!/usr/bin/env python3
"""Generate the shaking table's shapes, rig and reference poses.

The shaking table is a Wilfley table of about 1900 (#921, its model #922): a riffled deck, tilted a little
across its width, shaken along its length by a differential head, a slow push forward and a quick pull back,
so the heavy grains walk along the riffles to the far end while the wash water carries the light ones down
across the deck to its low side. The ore pulp comes in by chute to the feed box at the head end's upper
corner; the wash water by soldered ppex pipe to the water box along the upper edge. Three launders take the
products to three chutes: the concentrate off the far end, the middlings off the far end's lower corner, the
tailings off the low side.

The vanilla axle may turn either way, and a differential head run backwards would make its quick stroke the
forward one, so the axle drives the head through a rectifier (#711, rule 3): a closed cast-iron gear case
whose output shaft turns one way, three times the axle's speed, whichever way the axle turns. The head is a
closed cast-iron box (its toggles and return spring inside), a flywheel on its shaft outside it; its pull rod
works the deck. The deck's motion is drawn as three harmonics of a slow-forward, quick-return stroke on the
axle's travel (three `slide` drivers along z, summed): no new rig driver.

Everything is built here from plain boxes; no other mod's model is used. The chutes are the game's straight
chute section (its walls exactly, `VANILLA_CHUTE`, checked against the game's file when the game is here),
the water line Pipes and Power Expanded's pipe drawn soldered in copper and lead (Pipes/tools/solderedpipe.py,
as the rosser's drip pipes): one rig part per metal, `pipecopper` and `pipelead`, the same elements, only the
supplied metal's drawn.

It writes, deterministically,

    shakingtable.json         the whole machine, every fitted part          (assets/.../shapes/block/)
    shakingtable_frame.json   the stand and its launders only (block, item)  (assets/.../shapes/block/)
    shakingtable-rig.json     cells, anchors and the part rig                (assets/.../config/)
    rig-reference.json        every part's matrix at a spread of poses       (tests/ShakingTable/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_shakingtable.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
machine box's north-west-bottom corner (the "build frame"); the controller cell is the build frame's [0,0,0]
(the head end's upper corner, where the axle comes in), so the shipped files are the build frame divided by
16. The deck runs along z, its head end north (the front, the player's side), its far end south; its upper
edge is west, its low side east.

The rig's inputs (README "Rig"):

    theta  the axle's angle: the axle's oak cross into the rectifier case
    psi    the axle's travel either way: the rectified head shaft (and its flywheel), and the deck's stroke
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
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Pipes" / "tools"))

from machinegen.checks import cell_boxes, cells_touched, with_lids  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import IDENT, El, flatten, rotate, translate  # noqa: E402
from machinegen.output import (reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

import solderedpipe  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "ShakingTable" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/ShakingTable/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
DEG = math.pi / 180

# ---------------------------------------------------------------- the box, the cells, the anchors
CELLS_X, CELLS_Y, CELLS_Z = 2, 1, 3          # two wide (x), one high, three long (z, the deck's length)
X_LEN, Y_LEN, Z_LEN = CELLS_X * B, CELLS_Y * B, CELLS_Z * B
ORIGIN_CELL = (0, 0, 0)                      # the controller: the head end's upper corner, the rectifier case's cell
POWER_CELL, POWER_FACE = (0, 0, 0), "west"   # the vanilla axle comes in along x at (y 8, z 8)
INFEED_CELL, INFEED_FACE = (0, 0, 1), "up"   # a vanilla chute from above, into the feed box
WATER_CELL, WATER_FACE = (0, 0, 2), "up"     # a ppex pipe from above, into the water line's elbow
# the outputs: (name, cell, face, the chute's axis); each a vanilla chute section flush with that face
OUTLETS = (("concentrate", (0, 0, 2), "south", 2),
           ("middlings", (1, 0, 2), "south", 2),
           ("tailings", (1, 0, 0), "east", 0))

REQUIRES = ("deck", "head", "rectifier", "chutes")   # what is fitted to the frame (the stand and its launders)
PIPE_METALS = ("copper", "lead")             # the water line's metals: one part each, the supplied one drawn
PIPES = tuple(f"pipe{m}" for m in PIPE_METALS)

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",         # timber: sills, legs, rails, joists, riffles, posts, the axle's cross
    "planks": "game:block/wood/planks/oak1",       # boards: the deck, its boxes, the launders, the splitter
    "iron": "game:block/metal/plate/iron",         # castings and straps: the cases, the flywheel, pedestal, slides, bracket
    "steel": "game:block/metal/sheet-plain/steel1",  # the head shaft, its coupling's bolts and collars, the pull rod
    "chute": "game:block/metal/sheet/copper1",     # the chutes: vanilla's chute, as a placed chute shows in the pack
    "pipecopper": "game:block/metal/sheet-plain/copper4",   # the water line in copper (UnifiedPipes' copper pipe)
    "pipelead": "game:block/metal/sheet-plain/lead4",       # ... in lead (UnifiedPipes' lead pipe)
    "solder": solderedpipe.SOLDER_TEXTURE,         # the pipes' joints
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the head end: the axle, the rectifier, the head
AXLE = (8.0, 8.0)                            # (y, z): the axle's line along x, the power cell's centre; the head shaft's too
HEAD_RATIO = 3.0                             # the rectifier steps up 3:1: the head shaft turns three times a turn of the axle
ENTRY_X1 = 2.2                               # the axle's oak cross (the game's axle profile) runs from the face to here
BED_X = (1.4, 26.4)                          # the head's bed: two oak sills along x under the case, the head box and pedestal
BED_Z = ((3.0, 5.0), (11.0, 13.0))
BED_Y = 1.6
FEET_Z = ((3.2, 4.8), (11.2, 12.8))          # the castings' feet on the sills
CASE_X = (2.6, 8.2)                          # the rectifier case (a closed cast-iron gear case)
CASE_Y = (3.0, 12.6)
CASE_Z = (4.0, 12.0)
BOSS_R = 1.7                                 # the bearing bosses on the shaft line (apothem)
CASE_BOSS = ((1.9, CASE_X[0]), (CASE_X[1], 8.9))
SHAFT_R = 0.6                                # the head shaft (steel)
SHAFT_X = (8.4, 26.65)
COUPLING_X = (9.1, 9.68, 9.72, 10.3)         # its flanged coupling by the case: two flanges, a hair apart
COUPLING_R = 1.8
HEAD_X = (12.0, 19.0)                        # the differential head's box (cast iron, closed)
HEAD_Y = (3.6, 11.4)
HEAD_Z = (4.2, 11.8)
HEAD_BOSS = ((11.4, HEAD_X[0]), (HEAD_X[1], 19.6))
FLY_X = (21.0, 22.6)                         # the flywheel on the head shaft, past the head box
FLY_R = (3.9, 4.6)                           # its rim, inside and outside (apothem)
FLY_HUB = 1.2
SPOKES = 6
PED_X = (24.2, 25.8)                         # the outer bearing's pedestal
COLLARS = ((23.35, 24.15), (25.85, SHAFT_X[1]))   # either side of it: they hold the shaft endwise
SCREW = (15.5, 6.4)                          # (x, z): the stroke-adjusting screw on the head box's cover, and its handwheel
WHEEL_Y = (13.4, 13.9)

# ---------------------------------------------------------------- the deck (it slides along z)
DECK_X = (3.0, 27.8)                         # its upper (west) edge .. its low (east) edge
DECK_Z = (14.5, 42.5)                        # its head end .. its far end
DECK_TOP = 9.5                               # its top along the upper edge
DECK_T = 1.0
DECK_TILT = 2.5                              # degrees: the deck falls east, across its width
TILT_O = (DECK_X[0], DECK_TOP, 0.0)          # the deck is built level and turned about its upper edge's top
BOARDS_X = 4                                 # the deck boards: four across, two along
LIP_Z = (DECK_Z[1], 44.8)                    # the far end's lip, a step down from the deck's top, over the end launders
LIP_Y = (-1.0, -0.4)                         # its underside and top from the deck's top
JOISTS_X = ((6.0, 8.0), (21.0, 23.0))        # the two joists along the deck's length, under it
JOIST_Z = (14.8, 42.2)
JOIST_H = 2.0
BEARER_Z = (14.8, 16.8)                      # a cross bearer between the joists at the head end, carrying the rod's bracket
BEARER_H = 1.6
RIFFLE_XS = [13.0 + 2.0 * i for i in range(8)]   # the riffles along the deck, west to east (their middles)
RIFFLE_W = 0.8
RIFFLE_Z0 = 15.4                             # they start by the head end ...
RIFFLE_ENDS = (31.0, 41.5)                   # ... and end on a diagonal: the upper one's end, the lowest one's
RIFFLE_H = (1.0, 0.35)                       # tapering from 1 high to 0.35
RIFFLE_SUNK = 0.3                            # their underside sunk in the deck at the head end
BOX_T = 0.6                                  # the feed box's and water box's walls
FEEDBOX_X = (4.0, 12.0)                      # the feed box, at the head end's upper corner, under the infeed chute
FEEDBOX_Z = (19.4, 28.6)
FEEDBOX_H = 1.8                              # its outer walls; the deck side is a weir the pulp spills over
FEEDBOX_WEIR = 0.7
WATERBOX_X = (3.4, 8.6)                      # the water box along the upper edge, under the water line's nozzles
WATERBOX_Z = (29.4, 42.0)
WATERBOX_H = 1.15                            # its outer wall; the rest are low (the water spills over the weir onto the deck)
WATERBOX_WEIR = 0.35

# ---------------------------------------------------------------- the stroke: the differential head's motion
# A slow push forward (south, towards the concentrate end) and a quick pull back: three harmonics of a sawtooth
# whose ramp is the forward stroke, smoothed by Lanczos' sigma factors so the stroke has no ripple (its speed
# changes sign twice a stroke). Each term is a `slide` along z on the travel, ratio n x HEAD_RATIO.
STROKE = 1.2                                 # the deck's whole stroke, voxels (peak to peak)
HARMONICS = 3


def _stroke_terms():
    """[(n, amplitude in voxels, phase)]: the sigma-smoothed sawtooth's first HARMONICS terms, scaled so the
    stroke is STROKE; deterministic (the scale from a fixed grid)."""
    raw = []
    for n in range(1, HARMONICS + 1):
        x = math.pi * n / (HARMONICS + 1)
        raw.append((n, 2.0 / n * math.sin(x) / x, 0.0 if n % 2 else math.pi))
    grid = [sum(a * math.sin(n * TAU * i / 7200 + p) for n, a, p in raw) for i in range(7200)]
    k = STROKE / (max(grid) - min(grid))
    return [(n, a * k, p) for n, a, p in raw]


STROKE_TERMS = _stroke_terms()


def stroke_at(phi):
    """The deck's offset along z (voxels) at the head shaft's angle phi."""
    return sum(a * math.sin(n * phi + p) for n, a, p in STROKE_TERMS)


def stroke_speed(phi):
    return sum(a * n * math.cos(n * phi + p) for n, a, p in STROKE_TERMS)


ROD = (15.4, 5.4)                            # (x, y): the pull rod along z, under the deck's middle
ROD_R = 0.5
ROD_Z = (9.4, 16.4)                          # from inside the head box to past the bracket
GUIDE_Z = (HEAD_Z[1], 13.4)                  # its guide boss on the head box's south face
GUIDE_R = 1.2
BRACKET_Z = (15.0, 15.8)                     # the deck's bracket, under the bearer
BRACKET_Y0 = 4.4
NUTS_Z = ((14.4, 14.95), (15.85, ROD_Z[1]))  # the rod's nuts either side of the bracket
NUT_R = 0.75

# ---------------------------------------------------------------- the stand
SLIDES_Z = ((17.6, 20.4), (35.6, 38.4))      # the iron slide plates the joists run on, two under each
SLIDE_T = 0.5
SILL_Y = 1.4                                 # two oak sills along z under the legs, a tie across between them
SILLS_Z = (16.6, 39.4)
TIE_Z = (27.0, 29.0)
RAIL_Y = (3.0, 4.2)                          # a rail along each pair of legs

# ---------------------------------------------------------------- the launders and the chutes
LAUNDER_T = 0.6
LAUNDER_SIDE = 1.2                           # their sides over the floor
TL_X = (26.8, 30.6)                          # the tailings launder along the low side, falling north to its outlet
TL_Z = (3.4, 43.2)
TL_Y = (6.0, 5.02)                           # its floor's top at the south end and the north end
FL_Z = (43.2, 46.6)                          # the end launders across the far end, under the lip
CL_X = (3.0, 18.7)                           # the concentrate launder: from the splitter, falling west
ML_X = (19.3, 28.2)                          # the middlings launder: from the splitter, falling east, under the far corner
FL_Y = (6.0, 5.02)                           # the concentrate launder's floor's top at the splitter and at its outer end
ML_Y = (5.6, 5.02)                           # the middlings launder's: shorter, so it falls less
SPLIT_TOP = 7.5                              # the splitter between them: a board, its top
CHUTE_IN_Y0 = 11.7                           # the infeed chute: from the top face down to here, over the feed box
VANILLA_CHUTE = {                            # game 1.22.7, shapes/block/metal/chute/straight.json (the "we" shape):
    "walls": ((4.0, 12.0, 4.0, 5.0),         # its walls across the run, (y0, y1, z0, z1) cell-local: two full-width
              (4.0, 12.0, 11.0, 12.0),       # walls, two between them; 1 thick, an 8 x 8 tube centred in the block
              (4.0, 5.0, 5.0, 11.0),         # (its 4..12), a 6 x 6 bore. Its "ns" variant is the same turned about y,
              (11.0, 12.0, 5.0, 11.0)),      # its "ud" variant turned upright (rotateZ 90)
    "texture": "game:block/metal/sheet/copper1",
    "shape": "shapes/block/metal/chute/straight.json",
}
CHUTE_FLOOR = 5.0                            # the bore's floor in a horizontal chute (its bottom wall's top)

# ---------------------------------------------------------------- the water line (soldered ppex pipe)
PIPE_HALF = solderedpipe.HALF                # 3: ppex's pipe is 6 across
PIPE_Y = Y_LEN - PIPE_HALF                   # the line's axis: its top flush with the top face
WATER_IN = (WATER_CELL[0] * B + 8.0, WATER_CELL[2] * B + 8.0)   # (x, z): the elbow under the water face's centre
RUN_Z1 = 30.4                                # the run north from the elbow ends here, capped
NOZZLES_Z = (31.6, 34.0, 36.4, 39.4)         # the nozzles over the water box (the last under the elbow)
NOZZLE_X = (5.2, 6.4)                        # under the pipe's upper-edge side
NOZZLE_W = 1.2
NOZZLE_Y0 = 9.7                              # their mouths

# ---------------------------------------------------------------- the upper side's posts (they carry the chute and pipe)
SVC_X = (0.4, 2.4)
SVC_POSTS_Z = ((22.8, 24.8), (33.6, 35.6), (41.0, 43.0))
SVC_RAIL_Y = (14.0, 15.6)

SECONDS_PER_TURN = 2.1                       # the viewer's Play: the axle at the reference speed 0.6 (0.48 turns a second)


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


def beam(lo, hi, name, part, tex, seg=16.0):
    """A long box split into pieces of at most `seg` along its longest axis (so no texture stretches)."""
    axis = max(range(3), key=lambda a: hi[a] - lo[a])
    n = max(1, math.ceil((hi[axis] - lo[axis]) / seg - 1e-9))
    out = []
    for i in range(n):
        l2, h2 = list(lo), list(hi)
        l2[axis] = lo[axis] + (hi[axis] - lo[axis]) * i / n
        h2[axis] = lo[axis] + (hi[axis] - lo[axis]) * (i + 1) / n
        out.append(box(l2, h2, f"{name}{i + 1}" if n > 1 else name, part, tex))
    return out


AX = {"x": 0, "y": 1, "z": 2}
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}   # a disc's angle 0 direction, and the in-plane direction it turns towards


def radial(axis, c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box from radius r0 to r1 about `axis` through c, a0..a1 along the axis, `width` across, at angle
    `ang` (radians, right-handed about +axis from the axis's reference direction)."""
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
    """A plain round part: k strips as long as the 2k-gon is across, 180/k degrees apart, each a hair
    shorter than the last so no two ends share a plane."""
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


def annulus(axis, c, a0, a1, r_in, r_out, n, name, part, tex, phase=0.0):
    """A ring of n boxes from r_in to r_out, each as wide as a side of the n-gon at r_out; every other
    one a hair shorter along the axis, so their ends never share a plane."""
    w = 2 * r_out * math.tan(math.pi / n)
    out = []
    for i in range(n):
        s = 0.02 if i % 2 else 0.0
        out.append(radial(axis, c, a0 + s, a1 - s, r_in, r_out, w, phase + TAU * i / n, f"{name}{i + 1}", part, tex))
    return out


def sloped(axis, a0, a1, y0, y1, pieces, part):
    """Boxes laid on a straight slope along `axis` ("x" or "z") from a0 (height y0) to a1 (height y1),
    either way along the axis. Each piece is (s0, s1, h0, h1, c0, c1, name, tex): s along the slope from
    the a0 end (0 .. its length), h above the slope's line, c across the axis (z for x, x for z). They are
    built level from a0 and turned about the a0 end's line, so a0's end stays where it is."""
    a = AX[axis]
    acr = 2 if axis == "x" else 0
    length = a1 - a0
    dirn = 1.0 if length >= 0 else -1.0
    out = []
    for s0, s1, h0, h1, c0, c1, name, tex in pieces:
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[a], hi[a] = sorted((a0 + dirn * s0, a0 + dirn * s1))
        lo[1], hi[1] = y0 + h0, y0 + h1
        lo[acr], hi[acr] = c0, c1
        out.append(box(lo, hi, name, part, tex))
    target = math.atan2(y1 - y0, abs(length))
    if abs(target) > 1e-12:
        if axis == "x":
            rotate(out, "z", math.degrees(dirn * target), (a0, y0, 0.0))
        else:
            rotate(out, "x", -math.degrees(dirn * target), (0.0, y0, a0))
    return out


def slope_len(a0, a1, y0, y1):
    return math.hypot(a1 - a0, y1 - y0)


def along(a0, a1, y0, y1, at):
    """The distance along a slope from its a0 end to the point over `at`."""
    return abs(at - a0) * slope_len(a0, a1, y0, y1) / abs(a1 - a0)


def floor_y(y0, y1, a0, a1, at):
    """A floor's top at `at` along its axis (its top runs straight from y0 at a0 to y1 at a1)."""
    return y0 + (y1 - y0) * (at - a0) / (a1 - a0)


def under_slope(y0, y1, a0, a1, lo, hi, t):
    """The lowest point of a sloped board's underside over lo..hi along its axis (its top runs from y0 at
    a0 to y1 at a1, it is t thick), less a hundredth: where a block under it stops."""
    ang = math.atan2(y1 - y0, abs(a1 - a0))
    return min(floor_y(y0, y1, a0, a1, lo), floor_y(y0, y1, a0, a1, hi)) - t / math.cos(ang) - 0.01


def launder(axis, a0, a1, y0, y1, c0, c1, name, part, ends=(True, True), gaps=(), t=LAUNDER_T, side=LAUNDER_SIDE):
    """An open board launder along `axis` from a0 to a1 (either way), its floor's top running straight from
    y0 at a0 to y1 at a1 (a0 and a1 its outer ends), its outside c0..c1 across, sides `side` over the floor;
    `ends` closes either end with an end board. `gaps`: (side 1 or 2, g0, g1), a stretch of a side left out
    (an outlet), g0..g1 along the axis."""
    run = slope_len(a0, a1, y0, y1)
    s = lambda at: along(a0, a1, y0, y1, at)     # noqa: E731
    pieces = [(0.0, run, -t, 0.0, c0 + t, c1 - t, f"{name}_floor", "planks")]
    for k, (d0, d1) in ((1, (c0, c0 + t)), (2, (c1 - t, c1))):
        cuts = sorted(tuple(sorted((s(g0), s(g1)))) for sd, g0, g1 in gaps if sd == k)
        at, i = 0.0, 1
        for g0, g1 in cuts + [(run, run)]:
            if g0 - at > 1e-6:
                nm = f"{name}_side{k}" + (f"_{i}" if cuts else "")
                pieces.append((at, g0, -t, side, d0, d1, nm, "planks"))
                i += 1
            at = g1
    if ends[0]:
        pieces.append((0.0, t, 0.0, side, c0 + t, c1 - t, f"{name}_end1", "planks"))
    if ends[1]:
        pieces.append((run - t, run, 0.0, side, c0 + t, c1 - t, f"{name}_end2", "planks"))
    return sloped(axis, a0, a1, y0, y1, pieces, part)


# ---------------------------------------------------------------- the chutes
def chute_profile(axis):
    """The vanilla chute's walls for a section along `axis` (0 x, 1 y, 2 z), cell-local, as boxes over the
    two axes across it in index order: (lo0, hi0, lo1, hi1). The "we" shape's are over (y, z); turned about
    y ("ns") its z becomes x (the square section does not mind the mirror); turned upright ("ud", rotateZ
    90 about the block's centre) its y becomes x, mirrored."""
    walls = VANILLA_CHUTE["walls"]
    if axis == 0:
        return sorted(walls)
    if axis == 2:
        return sorted((z0, z1, y0, y1) for y0, y1, z0, z1 in walls)
    return sorted((16.0 - y1, 16.0 - y0, z0, z1) for y0, y1, z0, z1 in walls)


def chute(name, cell, axis, a0, a1):
    """A vanilla straight chute section (`chutes`) along `axis` from a0 to a1 in `cell`: its walls exactly,
    centred in the cell's face, so a chute placed in the next block meets it with no gap or step."""
    across = [k for k in range(3) if k != axis]
    out = []
    for i, (p0, p1, q0, q1) in enumerate(chute_profile(axis), 1):
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[axis], hi[axis] = a0, a1
        lo[across[0]], hi[across[0]] = cell[across[0]] * B + p0, cell[across[0]] * B + p1
        lo[across[1]], hi[across[1]] = cell[across[1]] * B + q0, cell[across[1]] * B + q1
        out.append(box(lo, hi, f"ch_{name}_wall{i}", "chutes", "chute"))
    return out


def read_vanilla_chute():
    """The game's own straight chute, if the game is here ($VINTAGE_STORY or ~/Games/vintagestory): its
    walls' sections over (y, z), the run of each along x, and its texture; or None. The generator holds
    VANILLA_CHUTE to it."""
    import os
    for base in (os.environ.get("VINTAGE_STORY"), str(Path.home() / "Games" / "vintagestory")):
        if not base:
            continue
        f = Path(base) / "assets" / "survival" / VANILLA_CHUTE["shape"]
        if f.exists():
            shape = json.loads(f.read_text())
            walls = sorted((e["from"][1], e["to"][1], e["from"][2], e["to"][2]) for e in shape["elements"])
            runs = {(e["from"][0], e["to"][0]) for e in shape["elements"]}
            tex = "game:" + next(iter(shape["textures"].values()))
            return walls, runs, tex
    return None


def outlet_span(name):
    """An outlet's bore across its face, along the launder it leaves (x for the end launders, z for the
    tailings launder): the cell's 5..11."""
    _, cell, _, axis = next(o for o in OUTLETS if o[0] == name)
    k = 0 if axis == 2 else 2
    return (cell[k] * B + 5.0, cell[k] * B + 11.0)


def outlet_from(name):
    """Where an output chute starts: its launder's outer wall; it runs from there to the face."""
    return {"concentrate": FL_Z[1], "middlings": FL_Z[1], "tailings": TL_X[1]}[name]


def build_chutes():
    """The four chute sections: the infeed upright from the infeed cell's top face down over the feed box; the
    concentrate and middlings out of the end launders' south walls to the south face; the tailings out of the
    tailings launder's east wall to the east face."""
    c = INFEED_CELL
    out = chute("in", c, 1, CHUTE_IN_Y0, (c[1] + 1) * B)
    for name, cell, face, axis in OUTLETS:
        out += chute(name[:4], cell, axis, outlet_from(name), (cell[axis] + 1) * B)
    return out


# ---------------------------------------------------------------- builders: the head end
AXLE_C = (0.0, AXLE[0], AXLE[1])


def build_bed():
    """The head's bed: two oak sills along x on the ground, under the rectifier case's, the head box's and the
    pedestal's feet."""
    return [box([BED_X[0], 0.0, z0], [BED_X[1], BED_Y, z1], f"fr_bed{i}", "frame", "oak") for i, (z0, z1) in enumerate(BED_Z, 1)]


def casting_feet(x0, x1, y1, name, part):
    """A casting's two feet on the bed's sills, up into its underside at y1."""
    return [box([x0, BED_Y, z0], [x1, y1 + 0.2, z1], f"{name}_foot{i}", part, "iron") for i, (z0, z1) in enumerate(FEET_Z, 1)]


def bolts(xs, zs, y, name, part, s=0.5, h=0.3):
    """Bolt heads standing on a face at height y."""
    out = []
    i = 1
    for x in xs:
        for z in zs:
            out.append(box([x - s / 2, y, z - s / 2], [x + s / 2, y + h, z + s / 2], f"{name}{i}", part, "iron"))
            i += 1
    return out


def build_rectifier():
    """The rectifier (`rectifier`): a closed cast-iron gear case on the bed, its cover bolted on, a joint
    flange round it at the shaft's height, its bosses on the shaft line; the gears inside (a pinion and wheel
    each way, one through an idler, on one-way catches) are not drawn. The axle's oak cross comes in through
    its west boss (`entry`, turning with the axle); the head shaft leaves its east boss."""
    p = "rectifier"
    (x0, x1), (y0, y1), (z0, z1) = CASE_X, CASE_Y, CASE_Z
    out = [box([x0, y0, z0], [x1, y1, z1], "rc_body", p, "iron")]
    out += casting_feet(x0 - 0.3, x1 + 0.3, y0, "rc", p)
    y, z = AXLE
    out += [box([x0 + 0.2, y - 0.4, z0 - 0.3], [x1 - 0.2, y + 0.4, z0], "rc_flange_n", p, "iron"),
            box([x0 + 0.2, y - 0.4, z1], [x1 - 0.2, y + 0.4, z1 + 0.3], "rc_flange_s", p, "iron")]
    out += disc("x", AXLE_C, *CASE_BOSS[0], BOSS_R, "rc_boss_w", p, "iron", k=6)
    out += disc("x", AXLE_C, *CASE_BOSS[1], BOSS_R, "rc_boss_e", p, "iron", k=6)
    out.append(box([x0 + 0.4, y1, z0 + 0.4], [x1 - 0.4, y1 + 0.4, z1 - 0.4], "rc_cover", p, "iron"))
    out += bolts((x0 + 1.0, x1 - 1.0), (z0 + 1.0, z1 - 1.0), y1 + 0.4, "rc_bolt", p)
    out += disc("y", ((x0 + x1) / 2, 0.0, (z0 + z1) / 2), y1 + 0.4, y1 + 1.1, 0.55, "rc_filler", p, "iron")
    # the axle's cross, the game's axle profile (4 x 2 twice), into the west boss
    e = "entry"
    out += [box([0.0, y - 1.0, z - 2.0], [ENTRY_X1 - 0.01, y + 1.0, z + 2.0], "entry_a", e, "oak"),
            box([0.01, y - 2.0, z - 1.0], [ENTRY_X1, y + 2.0, z + 1.0], "entry_b", e, "oak")]
    return out


def build_head():
    """The differential head (`head`): its closed cast-iron box on the bed, its bosses on the shaft line, the
    pull rod's guide boss low on its south face, its cover with the stroke-adjusting screw and handwheel; the
    outer bearing's pedestal. Its shaft (`headshaft`), turned by the rectifier: the coupling by the case, the
    flywheel past the box, two collars at the pedestal. The pull rod (`pullrod`), from the head's toggles
    inside the box to the deck's bracket, with its nuts: it rides the deck."""
    p = "head"
    (x0, x1), (y0, y1), (z0, z1) = HEAD_X, HEAD_Y, HEAD_Z
    out = [box([x0, y0, z0], [x1, y1, z1], "hd_body", p, "iron")]
    out += casting_feet(x0 - 0.3, x1 + 0.3, y0, "hd", p)
    out += disc("x", AXLE_C, *HEAD_BOSS[0], BOSS_R, "hd_boss_w", p, "iron", k=6)
    out += disc("x", AXLE_C, *HEAD_BOSS[1], BOSS_R, "hd_boss_e", p, "iron", k=6)
    out += disc("z", (ROD[0], ROD[1], 0.0), *GUIDE_Z, GUIDE_R, "hd_guide", p, "iron", k=4)
    out.append(box([x0 + 0.4, y1, z0 + 0.4], [x1 - 0.4, y1 + 0.4, z1 - 0.4], "hd_cover", p, "iron"))
    out += bolts((x0 + 1.0, x1 - 1.0), (z0 + 1.0, z1 - 1.0), y1 + 0.4, "hd_bolt", p)
    sx, sz = SCREW
    out += disc("y", (sx, 0.0, sz), y1 + 0.4, y1 + 1.0, 0.9, "hd_screwboss", p, "iron")
    out += disc("y", (sx, 0.0, sz), y1 + 1.0, WHEEL_Y[1] + 0.3, 0.3, "hd_screw", p, "steel")
    c = (sx, 0.0, sz)
    out += annulus("y", c, *WHEEL_Y, 1.3, 1.7, 12, "hd_wheel_rim", p, "iron")
    for i in range(4):
        out.append(radial("y", c, WHEEL_Y[0] + 0.1, WHEEL_Y[1] - 0.1, 0.6, 1.35, 0.3, TAU * i / 4 + math.pi / 4,
                          f"hd_wheel_spoke{i + 1}", p, "iron"))
    out += disc("y", c, WHEEL_Y[0] - 0.05, WHEEL_Y[1] + 0.05, 0.75, "hd_wheel_hub", p, "iron")
    # the pedestal: a sole plate across both sills, a column, the bearing round the shaft, its cap
    px0, px1 = PED_X
    y, z = AXLE
    out += [box([px0 - 0.2, BED_Y, FEET_Z[0][0]], [px1 + 0.2, BED_Y + 1.0, FEET_Z[1][1]], "hd_ped_sole", p, "iron"),
            box([px0 + 0.2, BED_Y + 1.0, z - 1.2], [px1 - 0.2, y - 1.6, z + 1.2], "hd_ped_column", p, "iron"),
            box([px0, y - 1.6, z - 1.6], [px1, y + 1.6, z + 1.6], "hd_ped_bearing", p, "iron"),
            box([px0 + 0.1, y + 1.6, z - 2.0], [px1 - 0.1, y + 2.2, z + 2.0], "hd_ped_cap", p, "iron")]
    # the head shaft: the rod, the coupling, the flywheel, the collars
    s = "headshaft"
    out += disc("x", AXLE_C, *SHAFT_X, SHAFT_R, "headshaft_rod", s, "steel")
    a, b, c2, d = COUPLING_X
    out += disc("x", AXLE_C, a, b, COUPLING_R, "headshaft_coupling_a", s, "iron", k=6)
    out += disc("x", AXLE_C, c2, d, COUPLING_R, "headshaft_coupling_b", s, "iron", k=6)
    for i in range(4):
        ang = TAU * i / 4 + math.pi / 4
        out.append(radial("x", AXLE_C, a - 0.1, d + 0.1, 1.0, 1.5, 0.5, ang, f"headshaft_coupling_bolt{i + 1}", s, "steel"))
    out += annulus("x", AXLE_C, *FLY_X, FLY_R[0], FLY_R[1], 16, "headshaft_fly_rim", s, "iron")
    for i in range(SPOKES):
        out.append(radial("x", AXLE_C, FLY_X[0] + 0.45, FLY_X[1] - 0.45, FLY_HUB - 0.1, FLY_R[0] + 0.1, 0.7, TAU * i / SPOKES,
                          f"headshaft_fly_spoke{i + 1}", s, "iron"))
    out += disc("x", AXLE_C, FLY_X[0] + 0.1, FLY_X[1] - 0.1, FLY_HUB, "headshaft_fly_hub", s, "iron", k=6)
    for i, (c0, c1) in enumerate(COLLARS, 1):
        out += disc("x", AXLE_C, c0, c1, 1.0, f"headshaft_collar{i}", s, "steel", k=6)
    # the pull rod and its nuts
    r = "pullrod"
    rc = (ROD[0], ROD[1], 0.0)
    out += disc("z", rc, *ROD_Z, ROD_R, "pullrod_rod", r, "steel")
    for i, (z0n, z1n) in enumerate(NUTS_Z, 1):
        out += disc("z", rc, z0n, z1n, NUT_R, f"pullrod_nut{i}", r, "iron", k=3)
    return out


# ---------------------------------------------------------------- builders: the deck
def deck_y(x):
    """The deck's top at x (it falls east by DECK_TILT)."""
    return DECK_TOP - (x - DECK_X[0]) * math.tan(DECK_TILT * DEG)


def tilt(els):
    return rotate(els, "z", -DECK_TILT, TILT_O)


def tilted(p):
    """A point of the deck as built level, where the tilt puts it."""
    a = -DECK_TILT * DEG
    dx, dy = p[0] - TILT_O[0], p[1] - TILT_O[1]
    return (TILT_O[0] + dx * math.cos(a) - dy * math.sin(a), TILT_O[1] + dx * math.sin(a) + dy * math.cos(a), p[2])


def riffle_end(x):
    """A riffle's far end on the diagonal they end on."""
    return RIFFLE_ENDS[0] + (RIFFLE_ENDS[1] - RIFFLE_ENDS[0]) * (x - RIFFLE_XS[0]) / (RIFFLE_XS[-1] - RIFFLE_XS[0])


def build_deck():
    """The deck (`deck`), all one part: it slides. Its boards, the lip at the far end, the two joists and the
    head end's bearer under it; the riffles, tapering, ending on a diagonal (the upper ones shortest: the
    concentrate rises out of them onto the smooth corner and is washed clean on its way to the far end); the
    feed box at the head end's upper corner, under the infeed chute; the water box along the upper edge,
    under the water line's nozzles; all built level and tilted. Then, upright, the cast-iron bracket under the
    bearer that the pull rod works."""
    d = "deck"
    out = []
    x0, x1 = DECK_X
    z0, z1 = DECK_Z
    top = DECK_TOP
    w = (x1 - x0) / BOARDS_X
    for i in range(BOARDS_X):
        out += beam([x0 + w * i, top - DECK_T, z0], [x0 + w * (i + 1), top, z1], f"deck_board{i + 1}_", d, "planks")
    lx = (x0 + 0.4, (x0 + x1) / 2, x1 - 0.4)
    for i in range(2):
        out.append(box([lx[i], top + LIP_Y[0], LIP_Z[0]], [lx[i + 1], top + LIP_Y[1], LIP_Z[1]], f"deck_lip{i + 1}", d, "planks"))
    jy = top - DECK_T - JOIST_H
    for i, (jx0, jx1) in enumerate(JOISTS_X, 1):
        out += beam([jx0, jy, JOIST_Z[0]], [jx1, top - DECK_T, JOIST_Z[1]], f"deck_joist{i}_", d, "oak")
    out.append(box([JOISTS_X[0][1], top - DECK_T - BEARER_H, BEARER_Z[0]], [JOISTS_X[1][0], top - DECK_T, BEARER_Z[1]],
                   "deck_bearer", d, "oak"))
    # the riffles, each turned about x so it tapers, its foot sunk in the deck
    for i, xr in enumerate(RIFFLE_XS, 1):
        ze = riffle_end(xr)
        length = ze - RIFFLE_Z0
        ang = math.atan2(RIFFLE_H[0] - RIFFLE_H[1], length)
        el = box([xr - RIFFLE_W / 2, top - RIFFLE_SUNK, RIFFLE_Z0], [xr + RIFFLE_W / 2, top + RIFFLE_H[0], RIFFLE_Z0 + length / math.cos(ang)],
                 f"deck_riffle{i}", d, "oak")
        rotate([el], "x", math.degrees(ang), (0.0, top - RIFFLE_SUNK, RIFFLE_Z0))
        out.append(el)
    # the feed box: three outer walls and the weir on the deck's side
    t = BOX_T
    (fx0, fx1), (fz0, fz1) = FEEDBOX_X, FEEDBOX_Z
    out += [box([fx0, top, fz0], [fx1, top + FEEDBOX_H, fz0 + t], "deck_fb_n", d, "planks"),
            box([fx0, top, fz1 - t], [fx1, top + FEEDBOX_H, fz1], "deck_fb_s", d, "planks"),
            box([fx0, top, fz0 + t], [fx0 + t, top + FEEDBOX_H, fz1 - t], "deck_fb_w", d, "planks"),
            box([fx1 - t, top, fz0 + t], [fx1, top + FEEDBOX_WEIR, fz1 - t], "deck_fb_weir", d, "planks")]
    # the water box: its outer wall, low ends and the weir
    (wx0, wx1), (wz0, wz1) = WATERBOX_X, WATERBOX_Z
    out += [box([wx0, top, wz0], [wx0 + t, top + WATERBOX_H, wz1], "deck_wb_w", d, "planks"),
            box([wx0 + t, top, wz0], [wx1, top + WATERBOX_WEIR, wz0 + t], "deck_wb_n", d, "planks"),
            box([wx0 + t, top, wz1 - t], [wx1, top + WATERBOX_WEIR, wz1], "deck_wb_s", d, "planks"),
            box([wx1 - t, top, wz0 + t], [wx1, top + WATERBOX_WEIR, wz1 - t], "deck_wb_weir", d, "planks")]
    tilt(out)
    # the bracket, upright, from under the rod up into the bearer's (tilted) underside
    under = min(tilted((x, top - DECK_T - BEARER_H, 0.0))[1] for x in (ROD[0] - 1.0, ROD[0] + 1.0))
    out.append(box([ROD[0] - 1.0, BRACKET_Y0, BRACKET_Z[0]], [ROD[0] + 1.0, under + 0.15, BRACKET_Z[1]], "deck_bracket", d, "iron"))
    return out


def joist_under(x):
    """Where a joist's tilted underside is at its edge x, less a hair: the top of the slide under it."""
    a = DECK_TILT * DEG
    return TILT_O[1] - (x - TILT_O[0]) * math.sin(a) - (DECK_T + JOIST_H) * math.cos(a) - 0.005


# ---------------------------------------------------------------- builders: the stand, the launders, the posts
def build_stand():
    """The stand: two oak sills along the deck's length and a tie between them, a pair of legs on each
    under its joist, an iron slide plate on each leg that the joist runs on, a rail along each pair."""
    f = "frame"
    out = []
    for i, (jx0, jx1) in enumerate(JOISTS_X, 1):
        out.append(box([jx0 - 0.6, 0.0, SILLS_Z[0]], [jx1 + 0.6, SILL_Y, SILLS_Z[1]], f"fr_sill{i}", f, "oak"))
        under = joist_under(jx1)
        for j, (sz0, sz1) in enumerate(SLIDES_Z, 1):
            out.append(box([jx0 - 0.3, under - SLIDE_T, sz0], [jx1 + 0.3, under, sz1], f"fr_slide{i}{j}", f, "iron"))
            out.append(box([jx0 + 0.1, SILL_Y, sz0 + 0.4], [jx1 - 0.1, under - SLIDE_T, sz1 - 0.4], f"fr_leg{i}{j}", f, "oak"))
        out.append(box([jx0 + 0.4, RAIL_Y[0], SLIDES_Z[0][1] - 0.4], [jx1 - 0.4, RAIL_Y[1], SLIDES_Z[1][0] + 0.4], f"fr_rail{i}", f, "oak"))
    out.append(box([JOISTS_X[0][1] + 0.6, 0.0, TIE_Z[0]], [JOISTS_X[1][0] - 0.6, SILL_Y, TIE_Z[1]], "fr_tie", f, "oak"))
    return out


def tl_floor(z):
    return floor_y(TL_Y[0], TL_Y[1], TL_Z[1], TL_Z[0], z)


def cl_floor(x):
    return floor_y(FL_Y[0], FL_Y[1], CL_X[1], CL_X[0], x)


def ml_floor(x):
    return floor_y(ML_Y[0], ML_Y[1], ML_X[0], ML_X[1], x)


def build_launders():
    """The launders, on oak blocks: the tailings launder along the low side, falling north to its outlet in its
    east wall at the head end; across the far end the concentrate launder, falling west to its outlet in its
    south wall, and the middlings launder, falling east to its; the splitter between them, a board standing on
    the ridge where their floors meet."""
    f = "frame"
    sp = outlet_span
    out = launder("z", TL_Z[1], TL_Z[0], TL_Y[0], TL_Y[1], *TL_X, "fr_tl", f, gaps=((2, *sp("tailings")),))
    out += launder("x", CL_X[1], CL_X[0], FL_Y[0], FL_Y[1], *FL_Z, "fr_cl", f, ends=(False, True), gaps=((2, *sp("concentrate")),))
    out += launder("x", ML_X[0], ML_X[1], ML_Y[0], ML_Y[1], *FL_Z, "fr_ml", f, ends=(False, True), gaps=((2, *sp("middlings")),))
    out.append(box([CL_X[1], FL_Y[0] - LAUNDER_T - 0.6, FL_Z[0]], [ML_X[0], SPLIT_TOP, FL_Z[1]], "fr_split", f, "planks"))
    t = LAUNDER_T
    for i, z in enumerate((8.0, 22.0, 36.0), 1):
        y = under_slope(TL_Y[0], TL_Y[1], TL_Z[1], TL_Z[0], z - 1.0, z + 1.0, t)
        out.append(box([TL_X[0] + 0.9, 0.0, z - 1.0], [TL_X[1] - 0.9, y, z + 1.0], f"fr_tl_block{i}", f, "oak"))
    for i, (x, fl, ax, ay) in enumerate(((7.5, cl_floor, CL_X, FL_Y), (15.0, cl_floor, CL_X, FL_Y), (24.0, ml_floor, ML_X, ML_Y)), 1):
        y = min(fl(x - 1.0), fl(x + 1.0)) - t / math.cos(math.atan2(ay[0] - ay[1], ax[1] - ax[0])) - 0.01
        out.append(box([x - 1.0, 0.0, FL_Z[0] + 0.9], [x + 1.0, y, FL_Z[1] - 0.9], f"fr_fl_block{i}", f, "oak"))
    return out


def build_posts():
    """The upper side's posts and the rail along their tops, with iron straps to the infeed chute and the
    water line: they carry them over the moving deck."""
    f = "frame"
    out = []
    x0, x1 = SVC_X
    for i, (z0, z1) in enumerate(SVC_POSTS_Z, 1):
        out.append(box([x0, 0.0, z0], [x1, SVC_RAIL_Y[0], z1], f"fr_post{i}", f, "oak"))
    out.append(box([x0, SVC_RAIL_Y[0], SVC_POSTS_Z[0][0]], [x1, SVC_RAIL_Y[1], SVC_POSTS_Z[-1][1]], "fr_postrail", f, "oak"))
    cz = INFEED_CELL[2] * B
    z0, z1 = SVC_POSTS_Z[0]
    out.append(box([x1, 13.4, z0 + 0.4], [INFEED_CELL[0] * B + 4.02, 14.4, z1 - 0.4], "fr_strap_chute", f, "iron"))
    assert cz + 4.0 <= z0 + 0.4 and z1 - 0.4 <= cz + 12.0
    for i, z in enumerate((34.6, 42.0), 1):
        out.append(box([x1, PIPE_Y - 0.6, z - 0.6], [WATER_IN[0] - PIPE_HALF + 0.02, PIPE_Y + 0.4, z + 0.6], f"fr_strap_pipe{i}", f, "iron"))
    return out


# ---------------------------------------------------------------- builders: the water line (soldered pipe)
def pipe_boxes():
    """The water line as UnifiedPipes' soldered pipe (solderedpipe's boxes, build-frame voxels): an elbow under
    the water face's centre, open up (the ppex pipe above meets it there) and north; the run north from it over
    the water box, its end plugged with solder; a nozzle under it over the water box at each of NOZZLES_Z. As in
    the rosser's drip, the inlet has no joint at the face: the pipe beyond brings its own."""
    h = PIPE_HALF
    x, z = WATER_IN
    y = PIPE_Y
    out = solderedpipe.hub("elbow", (x, y, z), ("up", "north"))
    out += solderedpipe.tube("run", 2, RUN_Z1, z - h, (x, y))
    i_ = h - solderedpipe.WALL
    out.append(solderedpipe.Box("cap", [x - i_, y - i_, RUN_Z1], [x + i_, y + i_, RUN_Z1 + solderedpipe.WALL], solderedpipe.SOLDER))
    for i, nz in enumerate(NOZZLES_Z, 1):
        out.append(solderedpipe.Box(f"nozzle{i}", [NOZZLE_X[0], NOZZLE_Y0, nz - NOZZLE_W / 2], [NOZZLE_X[1], y - h, nz + NOZZLE_W / 2],
                                    solderedpipe.BODY))
    return solderedpipe.cull_pressed(out)


def build_pipes(metal_=PIPE_METALS[0]):
    """The water line as elements of the part `pipe<metal_>` (`pipe_boxes`): the body in that metal's pipe
    texture, the joints in solder, each face's UVs solderedpipe's on this model's texture scale. Built in
    copper; `pipe_copies` makes lead."""
    p = f"pipe{metal_}"
    codes = {solderedpipe.BODY: f"#{p}", solderedpipe.SOLDER: "#solder"}
    out = []
    for b in pipe_boxes():
        size = b.size()
        faces = {d: {"texture": codes[b.role], "uv": solderedpipe.uv(d, size, TEX / 16)} for d in b.faces}
        out.append(El(f"{p}_{b.name}", size, [(b.lo[k] + b.hi[k]) / 2 for k in range(3)], [row[:] for row in IDENT], faces, p))
    return out


def pipe_copies(els):
    """The water line in the other metals: the first metal's elements as they are (after the z-fighting fix,
    so every copy has the same insets), renamed and in their own metal's texture; the joints stay solder. Only
    one metal's are ever drawn, so the copies are not checked against each other."""
    first = PIPES[0]
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


def build():
    return (build_rectifier() + build_head() + build_deck() + build_chutes() + build_pipes()
            + build_bed() + build_stand() + build_launders() + build_posts())


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def rot(axis, c, ratio, travel=False):
    """A rotate about `axis` through c (x, y, z voxels) by ratio x theta (or x psi, the travel)."""
    d = {"type": "rotate", "axis": axis, "pivot": pt(*c), "ratio": r6(ratio)}
    if travel:
        d["input"] = "travel"
    return d


def stroke_drivers():
    """The deck's stroke: one slide along z per harmonic, on the travel (the head shaft turns HEAD_RATIO times
    a turn of the axle, one stroke a turn)."""
    return [{"type": "slide", "axis": "z", "input": "travel", "amplitude": r6(a / B), "ratio": r6(n * HEAD_RATIO),
             "phase": r6(p)} for n, a, p in STROKE_TERMS]


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    parts = [
        # the axle's cross turns with the axle, either way
        {"id": "entry", "match": ["entry_*"], "requires": "rectifier", "drivers": [rot("x", AXLE_C, 1.0)]},
        # past the rectifier, the head shaft turns forward whichever way the axle does, three times as fast
        {"id": "headshaft", "match": ["headshaft_*"], "requires": "head", "drivers": [rot("x", AXLE_C, HEAD_RATIO, travel=True)]},
        # the deck: the head's stroke, a slow push forward (+z) and a quick pull back, one a turn of the head shaft
        {"id": "deck", "match": ["deck_*"], "requires": "deck", "drivers": stroke_drivers()},
        # the pull rod is pinned in the deck's bracket: it moves with the deck
        {"id": "pullrod", "match": ["pullrod_*"], "requires": "head", "ride": "deck", "drivers": []},
        # the fixed parts of each fitted set, and the frame
        {"id": "rectifier", "match": ["rc_*"], "requires": "rectifier", "drivers": []},
        {"id": "head", "match": ["hd_*"], "requires": "head", "drivers": []},
        {"id": "chutes", "match": ["ch_*"], "requires": "chutes", "drivers": []},
        # the water line, one part per metal: only the supplied metal's is drawn
        *[{"id": pid, "match": [f"{pid}_*"], "requires": pid, "drivers": []} for pid in PIPES],
        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses and states
# A pose is (theta,) or (theta, state), turned from rest so the travel is |theta|; or (theta, state, travel).
REST = (0.0,)
STATES = {                                    # what is fitted in each state checked: the frame, the fitted sets in
    "frame": (),                              # a plausible order (the stages are the owner's to settle), and the
    "deck": ("deck",),                        # finished machine with the water line in copper (lead is the same
    "head": ("deck", "head"),                 # elements, never drawn with it)
    "rectifier": ("deck", "head", "rectifier"),
    "complete": ("deck", "head", "rectifier", "chutes", PIPES[0]),
}
CELL_PARTS = None                             # the cells' boxes: every part of the finished machine (the copper water line)


def inputs_of(pose):
    th = pose[0]
    travel = pose[2] if len(pose) > 2 else abs(th)
    return {"theta": th, "travel": travel}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path)


def state_of(pose):
    return pose[1] if len(pose) > 1 else "all"


def on_show(requires, state):
    """Whether a part needing `requires` shows in a state ("all": every part at once, both metals)."""
    return requires is None or state == "all" or requires in STATES[state]


def head_angle(travel):
    """The head shaft's angle at a travel (the stroke's phase)."""
    return HEAD_RATIO * travel


def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


def make_rig(parts):
    rig = {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the head end's upper "
                    "corner, where the axle comes in. The deck runs along z from its head end (north, the front) to its far "
                    "end; its upper edge is west, its low side east. theta is the axle: its cross turns with it; psi (its "
                    "travel either way) turns the rectified head shaft and works the deck's stroke, three harmonics of a "
                    "slow-forward, quick-return stroke (slides along z). Chutes: the infeed from above into the feed box, three "
                    "outputs (cell and face, a vanilla chute section flush with that face); water: a ppex pipe from above. "
                    "See the shaking table's README for the schema.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "infeedCell": list(INFEED_CELL),
        "infeedFace": INFEED_FACE,
        "waterCell": list(WATER_CELL),
        "waterFace": WATER_FACE,
    }
    for name, cell, face, _ in OUTLETS:
        rig[f"{name}Cell"] = list(cell)
        rig[f"{name}Face"] = face
    rig["stroke"] = {
        "strokesPerTurn": r6(HEAD_RATIO),
        "length": r6(STROKE / B),
        "forward": "south",
        "_comment": "strokesPerTurn: the deck's strokes (and the head shaft's turns) per turn of the axle, either way (the "
                    "rectifier steps up 3:1). length: the stroke, blocks. forward: the way the slow push goes, towards the "
                    "concentrate end; the quick return goes back.",
    }
    rig["parts"] = parts
    return rig


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: the build frame's
    corner is the controller's)."""
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
    for key in list(ship):
        if key.endswith("Cell"):
            ship[key] = shift_cell(rig[key], ORIGIN_CELL)
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig: every part of the
    finished machine (the water line in copper; lead is the same), so each reader rebuilds them the same way;
    then a lid over every column."""
    by_cell = {}
    for w in flatten(shape["elements"], textures={}):
        pid = part_of(ship_parts, w.name)
        if pid in PIPES[1:]:
            continue
        el = posed(w, _part_matrix(ship_parts, pid, inputs_of(REST)))
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, []).append(el)
    out = []
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        boxes = cell_boxes(by_cell[pos], pos) if pos in by_cell else None
        out.append({"pos": list(pos), "boxes": boxes} if boxes else {"pos": list(pos), "hollow": True})
    return with_lids(out)


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    poses = [REST, (1.3,), (-2.2,), (4.9,), (9.1,), (-0.6, "all", 7.4)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; {len(cells)} cells")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
def reference_poses():
    """(theta, travel): turned from rest either way, and with travel run up both ways first."""
    out = []
    for th in (0.0, 0.7, -1.9, 2.6, 4.1, -5.3, 11.05):
        for extra in (0.0, 3.7):
            out.append((th, round(abs(th) + extra, 6)))
    return out


def reference_json(ship_parts):
    poses = []
    for th, ps in reference_poses():
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], {"theta": th, "travel": ps})) for q in ship_parts}
        poses.append({"theta": th, "travel": ps, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT}; do not edit. Every shaking table part's matrix (blocks, first three rows) at "
                        "each pose (theta the axle's angle, travel its total travel either way, radians), from the generator's "
                        "reference maths (Machines/tools/machinegen/rigmath.py). The site's and the mod's tests check their rig "
                        "maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. The stand and launders (fr_*), "
             "the deck (deck_*), the differential head (hd_*: its box and pedestal; headshaft_*: its shaft, coupling and "
             "flywheel; pullrod_*: its pull rod), the rectifier (rc_*: its case; entry_*: the axle's cross), the chutes "
             "(ch_*) and the water line in each metal (pipecopper_*, pipelead_*). Keep element names when editing: the rig "
             "finds its parts by them.",
        TEXTURES, tex_size=TEX)


def shown(posed_els, pose, req_of):
    """The posed elements as they can be seen in the pose's state: parts not fitted moved far away (copies; the
    order kept), so the z-fighting fix and its check deal only with faces that show together."""
    state = state_of(pose)
    out = []
    for e in posed_els:
        if not on_show(req_of[e.part], state):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def coplanar_poses():
    out = []
    for state in ("complete", "frame", "deck"):
        for travel in (0.0, 0.35, 0.9, 1.6):
            out.append((travel, state))
    return tuple(out)


def fix_coplanar(els, parts):
    req_of = {p["id"]: p["requires"] for p in parts}
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose, req_of),
                              coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the shaking table's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_shakingtable
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        n = sum(len(v) for v in before.values())
        print(f"coplanar faces before the fix: {n} pairs over {len(before)} poses; {hidden} faces pressed against their own part removed")
    els += pipe_copies(els)
    rig = make_rig(parts)
    ok = validate_shakingtable.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "shakingtable.json", args.out / "shakingtable_frame.json", args.out / "shakingtable-rig.json",
                args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "shakingtable.json", SHAPE_DIR / "shakingtable_frame.json", RIG_DIR / "shakingtable-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_shakingtable.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
