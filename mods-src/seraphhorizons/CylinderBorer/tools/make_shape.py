#!/usr/bin/env python3
"""Generate the cylinder borer's shapes, rig and reference poses.

The cylinder borer is John Wilkinson's boring mill of 1774-75, the machine that first bored a steam
engine's cylinder true. A cast cylinder lies fixed in a timber cradle, bedded in two V-blocks and held
down by chains; a stiff iron boring bar runs right through it, carried in a bearing at each end (Wilkinson's
improvement on Smeaton's cantilevered borer, which wandered). The bar is turned from the vanilla axle by
a pinion and a great wheel, 1:4, a slow, heavy cut. A cutter head slides along the bar (it turns with it
on a key) and carries four cutters set out by wedges; it is drawn through the bore by a control rod from
a crosshead that runs on the rails of a long timber bench at the far end, and the crosshead is pulled
by a rope wound on a drum whose shaft a falling weight turns: a weighted feed, as Wilkinson's was, so the
head bears on the cut as hard as the weight pulls and advances as fast as the cutters take metal. The
crosshead's handle is how the head is pushed back for the next cylinder, winding the weight up. Two
cylinders: a small one (k = 1, a Watt engine's) and a large one (k = 2, a Cornish engine's, with a larger
bore and a heavier wall); the V-blocks are wound down on screw jacks and the cutters set out for the large
one, so both lie concentric with the bar. Everything is built here from plain boxes; no other mod's model
is used.

It writes, deterministically,

    cylinderborer.json         the whole machine, every moving part   (assets/.../shapes/block/)
    cylinderborer_frame.json   the static frame only (block and item)  (assets/.../shapes/block/)
    cylinderborer-rig.json     cells, anchors and the part rig         (assets/.../config/)
    rig-reference.json         every part's matrix at a grid of poses  (tests/CylinderBorer/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_cylinderborer.py) and
exits non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
machine box's north-west-bottom corner (the "build frame"); the controller cell is the build frame's
[0,0,0], so the shipped files are the build frame divided by 16. The machine runs along z: the bench
with the feed's weight at the north end (the controller's), the cradle in the middle, the drive at the
south end, where the axle comes in.

The rig's inputs, as this machine uses them (README "Rig"):

    theta  the axle angle: the entry shaft and its pinion; the bar, the great wheel and the cutter
           head turn at a quarter of it, backwards
    W      the rig's work, the cutter head's travel through the bore, in blocks: 0 with the head
           outside the bore's drive end, 17/16 (small) or 21/16 (large) with the cutters past its far
           end. The crosshead, the rod, the drum, the weight and the ropes follow it, and each slice of
           the bore's cast skin sinks into the wall as the cutters cross it
    k      the cylinder: 0 none, 1 small (a Watt engine's), 2 large (a Cornish engine's)
    p      its presence, 0..1, eased as a cylinder is loaded and cleared: the V-blocks and the cutters
           are set for the large one with it, and the head comes back with it
    oil    the oil tank's fill, 0..1: the two bearings' sight-feed cups
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

from machinegen.checks import cell_boxes, cells_touched, with_lids  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import IDENT, TRANSPARENT, El, flatten, rotate, translate  # noqa: E402
from machinegen.output import (reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               shift_point, worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, progress_of, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "CylinderBorer" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/CylinderBorer/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
DEG = math.pi / 180
COS30 = math.cos(30 * DEG)

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 2, 2, 4          # two wide, two high, four long (z)
ORIGIN_CELL = (0, 0, 0)                      # the controller: the bench end's west cell at ground level, nearest the player
POWER_CELL, POWER_FACE = (0, 1, 3), "south"  # the vanilla axle comes in along z at (x 8, y 24), the upper west cell's centre

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "iron": "game:block/metal/plate/iron",
    "steel": "game:block/metal/sheet-plain/steel1",
    "cast": "game:block/metal/corroded/normal1",     # the cylinder casting, outside
    "rough": "game:block/metal/corroded/rusty1",     # the bore's cast skin, scaled and rough, before the cut
    "bored": "game:block/metal/sheet-plain/steel1",  # the bore once the cutters have passed: bright
    "brass": "game:block/metal/sheet/brass1",
    "rope": "game:item/resource/rope",
    "glass": "game:block/glass/plain",
    "oil": "game:block/liquid/honey",
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the bar and the drive
BAR = (16.0, 18.0)                           # (x, y): the boring bar's axis, along z
BAR_R = 2.0                                  # its apothem (an octagon 4 across)
BAR_Z = (0.3, 63.5)                          # from beyond the far bearing into the drive bearing
ENTRY = (8.0, 24.0)                          # (x, y): the entry shaft, along z from the south face (the power cell's centre)
GEAR_MOD = 0.5
PINION_T, WHEEL_T = 8, 32                    # the pinion on the entry shaft, the great wheel on the bar: 1:4, a slow, heavy cut
R_PINION, R_WHEEL = PINION_T * GEAR_MOD / 2, WHEEL_T * GEAR_MOD / 2     # 2 and 8: their centres are 10 apart
BAR_RATIO = -PINION_T / WHEEL_T              # the bar turns a quarter of the axle, the other way
GEAR_Z = (58.2, 60.4)                        # both gears' faces
BRG_HALF = 2.6                               # the bar's bearings: blocks 5.2 square round it
BRG_FAR_Z = (1.0, 4.4)                       # the far bearing, on the bench end (the "sturdy frame")
BRG_DRIVE_Z = (61.2, 63.8)                   # the drive bearing, beyond the great wheel
ENTRY_HALF = 2.2
ENTRY_BRG_Z = ((55.0, 57.2), (61.4, 63.8))   # the entry shaft's bearings either side of its pinion
COLLAR_R = 2.6
KEY_HALF, KEY_PROUD = 0.3, 0.3               # the feather on the bar's top flat: 0.6 wide, 0.3 proud
KEY_Z = (6.0, 57.4)

# ---------------------------------------------------------------- the cylinders (k = 1 small, 2 large)
CLASSES = ("thin", "thick")
Z_DRIVE_MOUTH = 51.6                         # both cylinders' drive-end face: the head starts just beyond it
CYL = {
    "thin": {"bore": 5.5, "rough": 5.0, "out": 7.5, "flange": 8.7, "z": (36.6, Z_DRIVE_MOUTH)},    # a Watt engine's
    "thick": {"bore": 6.5, "rough": 6.0, "out": 9.5, "flange": 10.7, "z": (32.6, Z_DRIVE_MOUTH)},  # a Cornish engine's
}
FLANGE_T = 1.0
NGON = 12                                    # the castings are 12-gons, a flat at the bottom (they bed on two flats in the V)
SKIN_SLICES = 5                              # the cast skin, in slices along the bore, each in four quarters
SKIN_SINK = 0.8                              # how far a cut quarter sinks into the wall (out of sight behind the bored face)
SKIN_END = 0.03                              # the skin stops this short of the cylinder's end faces
CUTTER_SET = CYL["thick"]["bore"] - CYL["thin"]["bore"]          # the cutters are set out this much for the large bore

# ---------------------------------------------------------------- the cutter head (the kit), at rest
HEAD_R = 4.5                                 # its body, a 12-gon inside the small bore's skin
HEAD_Z = (52.0, 54.8)
HUB_R = 2.8
HUB_LIPS = ((50.6, 51.0), (51.6, 52.0))      # the yoke's groove between them
GROOVE_Z, GROOVE_R = (51.0, 51.6), 2.2
BACKHUB_Z = (54.8, 55.4)
CUTTER_ANGLES = (0, 1, 2, 3)                 # quarter turns from the top (+y), towards -x: the four cutters
HOLDER = {"r": (2.5, 5.1), "half": 0.5, "z": (52.0, 53.2)}
BIT = {"r0": 5.1, "half": 0.4, "z": (52.0, 52.8), "clear": 0.02}
WEDGE = {"r": (2.8, 4.75), "t": (0.55, 1.0), "z": (52.0, 53.4)}
CUT_SPAN = (HOLDER["z"][0], HOLDER["z"][1])  # the cutters' leading and trailing faces at rest

# ---------------------------------------------------------------- the feed
TRAVEL = {"thin": 17.0, "thick": 21.0}       # the head's travel: its cutters from outside the drive end to past the far end
E = TRAVEL["thick"] / B                      # the work's longest end, blocks
XH_Z = (29.0, 32.0)                          # the crosshead at rest, just outside the large cylinder's far end
ROD = {"x": (15.5, 16.5), "y": (14.0, 15.0), "z": (30.0, 51.4)}
YOKE_Z, YOKE_IN, YOKE_OUT = (51.05, 51.55), 2.4, 3.2
DRUM_C = (23.8, 3.0)                         # (y, z): the feed drum's shaft, along x, on the bench end's posts
R_DRUM, R_WDRUM = 2.0, 1.0                   # the crosshead's rope on the big drum, the weight's on the small: 2:1
DRUM_X, WDRUM_X = (14.0, 18.0), (21.0, 23.5)
ROPE_W = 0.3
ROPE_LAP = 0.1                               # each rope piece runs this far into the next, so no inset opens a gap
XROPE_Y = DRUM_C[0] + R_DRUM                 # the crosshead's rope leaves the drum's top, level
XROPE_TOP = (DRUM_C[1], 10.625)              # its piece the crosshead never reaches
XROPE_SEGS = 7
WROPE_X = (WDRUM_X[0] + WDRUM_X[1]) / 2
WROPE_Z = DRUM_C[1] - R_WDRUM                # the weight's rope leaves the small drum's north side, down
WEIGHT = {"x": (20.75, 23.75), "z": (0.5, 3.5), "y": (17.0, 20.6), "cap": 0.3}
W_CAP0 = WEIGHT["y"][1] + WEIGHT["cap"]      # the weight's top at rest, where its rope goes in
DROP = TRAVEL["thick"] * R_WDRUM / R_DRUM    # how far the weight falls in a large bore (10.5)
WROPE_SEGS = 4

# ---------------------------------------------------------------- the frame
SILLS = ((0.5, 4.0), (28.0, 31.5))           # the long oak sills (x), y 0..3, the machine's whole length
SILL_Y, SILL_Z = (0.0, 3.0), (0.3, 63.7)
CROSS = {"far": (0.6, 4.6), "benchmid": (16.2, 19.8), "benchnear": (29.2, 32.6), "saddle1": (38.4, 41.2),
         "chain": (42.0, 46.4), "saddle2": (47.2, 50.0), "entry": (54.6, 57.6), "drive": (60.0, 63.6)}
POSTS = ((0.8, 4.2), (27.8, 31.2))           # the bench end's posts (x), z 1..4.5, up to the top beam
POST_Z, POST_TOP = (1.0, 4.5), 27.0
RAILS = ((5.0, 8.0), (24.0, 27.0))           # the bench's oak rails (x), y 8..11, iron ways on them
RAIL_Y, RAIL_Z = (8.0, 11.0), (4.5, 32.4)
WAY_Y = (11.0, 11.4)
LEGS_Z = ((16.6, 19.4), (29.6, 32.4))

# ---------------------------------------------------------------- the cradle
SADDLE_Z = ((38.6, 41.0), (47.4, 49.8))      # inside both cylinders' barrels
V_HALF = 30 * DEG                            # the V's faces rise 30 degrees: they bed the castings' flats either side of the bottom one
V_VERTEX = {c: BAR[1] - CYL[c]["out"] / COS30 for c in CLASSES}    # the V's bottom for each cylinder, concentric with the bar
V_DROP = V_VERTEX["thin"] - V_VERTEX["thick"]  # how far the V-blocks are wound down for the large cylinder (2.31)
JACK_LEAD = 1.0                              # the jack screws' lead: the nut turns V_DROP / lead turns
JACK_X = (11.5, 20.5)
NUT_H, NUT_R = 1.0, 0.95
CRADLE_TOP = SILL_Y[1]                       # the cradle's cross beams lie between the sills, their tops at 3
V_BOTTOM = {"thick": CRADLE_TOP + NUT_H, "thin": CRADLE_TOP + NUT_H + V_DROP}
V_X = (8.6, 23.4)
CHEEK_T = 3.5
CHEEK_U = (0.25, 7.8)                        # along each face from the V's bottom
CHAIN_Z = (43.0, 45.4)                       # two chains over each cylinder, between the V-blocks
LINK = {"pitch": 1.4, "wide": (1.0, 0.66), "narrow": (0.6, 0.84)}   # (across, i.e. along z; deep, i.e. radial)
CHAIN_OFF = 0.42                             # the chain's centre line stands this far off the casting's flats
HOOK_Y = 3.6                               # the chains' hook bars, across their cross beam
HOOKBAR_X = (5.0, 27.0)

# ---------------------------------------------------------------- the oil cups (one on each bar bearing, one tank)
CUP = {"x": (11.0, 12.4), "y": (18.6, 20.2), "far_z": (2.0, 3.4), "drive_z": (61.8, 63.2)}
OIL_Y = CUP["y"][0] + 0.01                  # the oil's bottom, a hair over the glass's
OIL_EMPTY = 0.05
OIL_FULL = CUP["y"][1] - OIL_Y - 0.15

# ---------------------------------------------------------------- gameplay figures (provisional)
TURNS_PER_BLOCK = 64.0                       # axle turns per block of the head's travel: the gameplay's pace, not drawn (the feed is weighted)


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


def long_box(lo, hi, name, part, tex, axis=2, seg=16.0):
    """A long box split at block boundaries along `axis`, so its texture is not stretched."""
    cuts = [lo[axis]] + [k * seg for k in range(1, 8) if lo[axis] + 0.5 < k * seg < hi[axis] - 0.5] + [hi[axis]]
    out = []
    for i, (a, b) in enumerate(zip(cuts, cuts[1:])):
        l2, h2 = list(lo), list(hi)
        l2[axis], h2[axis] = a, b
        out.append(box(l2, h2, f"{name}{i + 1}" if len(cuts) > 2 else name, part, tex))
    return out


AX = {"x": 0, "y": 1, "z": 2}
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}   # a disc's angle 0 direction, and the in-plane direction it turns towards


def frame_of(axis):
    a = AX[axis]
    u, w = REF[axis]
    return a, u, w


def radial(axis, c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box from radius r0 to r1 about `axis` through c, a0..a1 along the axis, `width` across, at
    angle `ang` (radians, right-handed about +axis from the axis's reference direction; about z, from +y
    towards -x)."""
    a, u, w = frame_of(axis)
    lo, hi = [0.0] * 3, [0.0] * 3
    lo[a], hi[a] = a0, a1
    lo[u], hi[u] = c[u] + r0, c[u] + r1
    lo[w], hi[w] = c[w] - width / 2, c[w] + width / 2
    el = box(lo, hi, name, part, tex)
    if abs(ang) > 1e-12:
        rotate([el], axis, math.degrees(ang), c)
    return el


def disc(axis, c, a0, a1, r, name, part, tex, k=4, phase=0.0, step=0.012):
    """A plain round part: k strips as long as the 2k-gon is across, 180/k degrees apart, each a hair
    shorter than the last so no two ends share a plane."""
    a, u, w = frame_of(axis)
    half = r * math.tan(math.pi / (2 * k))
    out = []
    st = min(step, (a1 - a0) / (4 * k))
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


def ngon_ring(c, z0, z1, r_in, r_out, name, part, tex, n=NGON, inner=None, phase=0.0, steps=True):
    """A tube along z of n boxes from r_in to r_out, a flat at the top (and the bottom); each box as wide as
    a side at r_out, so the outside is the n-gon and the inside is too (each box's inner face shows only
    where it is the bore's flat). Their ends step in by 0, 0.008, 0.016, 0.024 round the ring, so no two
    that overlap end in one plane. `inner` retextures the inner (bore) faces."""
    w = 2 * r_out * math.tan(math.pi / n)
    out = []
    for i in range(n):
        s = 0.008 * (i % 4) if steps else 0.0
        el = radial("z", c, z0 + s, z1 - s, r_in, r_out, w, phase + TAU * i / n, f"{name}{i + 1}", part, tex)
        if inner:
            el.faces["down"]["texture"] = "#" + inner
        out.append(el)
    return out


def gear(axis, c, a0, a1, pitch_r, n, module, name, part, tex, phase=0.0, body_k=4, body_r=None):
    """A spur gear: a polygon body to the root and n teeth of `module` (phase: angle of tooth 1)."""
    width = math.pi * module / 2
    root, tip = pitch_r - 1.25 * module, pitch_r + module
    body = disc(axis, c, a0, a1, body_r or root, f"{name}_body", part, tex, k=body_k, phase=phase) if (body_r or root) > 0 else []
    return body + [radial(axis, c, a0 + 0.03, a1 - 0.03, root - 0.25, tip, width, phase + TAU * i / n, f"{name}_tooth{i + 1}", part, tex)
                   for i in range(n)]


def strut(a, b, w, d, name, part, tex, axis="x"):
    """A bar from point a to point b in the plane normal to `axis`, `w` wide in that plane, `d` along it."""
    ia = AX[axis]
    u, v = [(1, 2), (2, 0), (0, 1)][ia]
    du, dv = b[u] - a[u], b[v] - a[v]
    length = math.hypot(du, dv)
    mid = [(a[i] + b[i]) / 2 for i in range(3)]
    lo, hi = list(mid), list(mid)
    lo[u], hi[u] = mid[u] - length / 2, mid[u] + length / 2
    lo[v], hi[v] = mid[v] - w / 2, mid[v] + w / 2
    lo[ia], hi[ia] = mid[ia] - d / 2, mid[ia] + d / 2
    el = box(lo, hi, name, part, tex)
    rotate([el], axis, math.degrees(math.atan2(dv, du)), mid)
    return el


def eye(axis, c, a0, a1, r_in, r_out, name, part, tex):
    """A square eye round an axis: four bars."""
    a, u, w = frame_of(axis)
    out = []
    for i, (du, dw) in enumerate(((1, 0), (-1, 0), (0, 1), (0, -1)), 1):
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[a], hi[a] = a0, a1
        if du:
            lo[u], hi[u] = (c[u] + r_in, c[u] + r_out) if du > 0 else (c[u] - r_out, c[u] - r_in)
            lo[w], hi[w] = c[w] - r_out, c[w] + r_out
        else:
            lo[u], hi[u] = c[u] - r_in, c[u] + r_in
            lo[w], hi[w] = (c[w] + r_in, c[w] + r_out) if dw > 0 else (c[w] - r_out, c[w] - r_in)
        out.append(box(lo, hi, f"{name}{i}", part, tex))
    return out


def ang_dir(ang):
    """The unit (x, y) of angle `ang` about z, from +y towards -x (radial's convention)."""
    return (-math.sin(ang), math.cos(ang))


def dir_ang(dx, dy):
    return math.atan2(-dx, dy)


def mesh_phase(c1, phase1, n1, c2, n2):
    """The tooth phase (angle of tooth 1 about +z, radial's convention) for gear 2 meshing gear 1 (whose
    tooth 1 is at phase1): on the line of centres, gear 1's tooth and gear 2's gap meet."""
    d = dir_ang(c2[0] - c1[0], c2[1] - c1[1])
    p1, p2 = TAU / n1, TAU / n2
    off = ((d - phase1) / p1) % 1.0
    return (d + math.pi) + p2 / 2 + off * p2


# ---------------------------------------------------------------- derived positions
BAR_C = (BAR[0], BAR[1], 0.0)
ENTRY_C = (ENTRY[0], ENTRY[1], 0.0)
DRUM_P = (0.0, DRUM_C[0], DRUM_C[1])


def wedge_dir(q):
    return ang_dir(q * math.pi / 2)


# ---------------------------------------------------------------- builders: the drive
def build_entry():
    """The entry shaft: the vanilla axle's oak cross profile into its outer bearing, then a round iron
    shaft through the pinion to its inner bearing."""
    x, y = ENTRY
    z0, z1 = ENTRY_BRG_Z[1][0], 64.0
    out = [box([x - 0.6, y - 1.5, z0], [x + 0.6, y + 1.5, z1], "entry_shafta", "entry", "oak"),
           box([x - 1.5, y - 0.6, z0 + 0.012], [x + 1.5, y + 0.6, z1 - 0.012], "entry_shaftb", "entry", "oak")]
    out += disc("z", ENTRY_C, ENTRY_BRG_Z[0][0] + 0.2, z0 + 0.2, 0.8, "entry_rod", "entry", "iron")
    out += gear("z", ENTRY_C, *GEAR_Z, R_PINION, PINION_T, GEAR_MOD, "entry_pinion", "entry", "iron", body_k=4)
    out += disc("z", ENTRY_C, GEAR_Z[0] - 0.4, GEAR_Z[1] + 0.4, 1.2, "entry_hub", "entry", "iron")
    return out


def build_bar():
    """The boring bar (an iron octagon, in block lengths), its two collars at the far bearing (the bar's
    thrust), and the great wheel keyed on its drive end: a cast spoked wheel of 32 teeth."""
    out = []
    z0, z1 = BAR_Z
    cuts = [z0, 16.0, 32.0, 48.0, z1]
    for i, (a, b) in enumerate(zip(cuts, cuts[1:])):
        out += disc("z", BAR_C, a, b, BAR_R, f"bar_seg{i + 1}", "bar", "iron", step=0.0 if 0 < i < 3 else 0.012)
    # the feather the head slides on (it keys the head to the bar, so the head turns with it): a steel strip on the
    # bar's top flat between the far collar and the great wheel's hub
    out += long_box([BAR[0] - KEY_HALF, BAR[1] + BAR_R - 0.1, KEY_Z[0]], [BAR[0] + KEY_HALF, BAR[1] + BAR_R + KEY_PROUD, KEY_Z[1]], "bar_key", "bar", "steel")
    out += disc("z", BAR_C, z0, BRG_FAR_Z[0] - 0.05, COLLAR_R, "bar_collar1", "bar", "iron")
    out += disc("z", BAR_C, BRG_FAR_Z[1] + 0.05, BRG_FAR_Z[1] + 0.7, COLLAR_R, "bar_collar2", "bar", "iron")
    ph = mesh_phase(ENTRY, 0.0, PINION_T, BAR, WHEEL_T)
    root = R_WHEEL - 1.25 * GEAR_MOD
    for i in range(WHEEL_T):
        out.append(radial("z", BAR_C, GEAR_Z[0] + 0.03, GEAR_Z[1] - 0.03, root - 0.25, R_WHEEL + GEAR_MOD, math.pi * GEAR_MOD / 2,
                          ph + TAU * i / WHEEL_T, f"wheel_tooth{i + 1}", "bar", "iron"))
    out += ngon_ring(BAR_C, *GEAR_Z, root - 0.9, root + 0.05, "wheel_rim", "bar", "iron", n=24, phase=ph)
    for i in range(6):
        out.append(radial("z", BAR_C, GEAR_Z[0] + 0.5, GEAR_Z[1] - 0.5, 2.6, root - 0.6, 1.1, TAU * i / 6 + math.pi / 6, f"wheel_spoke{i + 1}", "bar", "iron"))
    out += disc("z", BAR_C, GEAR_Z[0] - 0.4, GEAR_Z[1] + 0.4, 3.0, "wheel_hub", "bar", "iron", k=6)
    return out


def build_bearings():
    """The bar's two bearings (the fitted `bearings` part): iron blocks on the bench end's pedestal and the
    drive pedestal."""
    x, y = BAR
    h = BRG_HALF
    return [box([x - h, y - h, BRG_FAR_Z[0]], [x + h, y + h, BRG_FAR_Z[1]], "brg_far", "bearings", "iron"),
            box([x - h, y - h, BRG_DRIVE_Z[0]], [x + h, y + h, BRG_DRIVE_Z[1]], "brg_drive", "bearings", "iron")]


# ---------------------------------------------------------------- the cutter head (the kit) and its cutters
def build_head():
    """The cutter head: a cast body keyed on the bar (it turns with it and slides along it), a hub ahead of
    it with the yoke's groove, a short hub behind; four wedges, one beside each cutter's slot."""
    out = disc("z", BAR_C, *HEAD_Z, HEAD_R, "head_body", "head", "iron", k=6, phase=math.pi / 12)
    for i, (a, b) in enumerate(HUB_LIPS, 1):
        out += disc("z", BAR_C, a, b, HUB_R, f"head_lip{i}", "head", "iron")
    out += disc("z", BAR_C, *GROOVE_Z, GROOVE_R, "head_groove", "head", "steel")
    out += disc("z", BAR_C, *BACKHUB_Z, HUB_R, "head_backhub", "head", "iron")
    for q in CUTTER_ANGLES:
        ang = q * math.pi / 2
        el = box([BAR[0] + WEDGE["t"][0], BAR[1] + WEDGE["r"][0], WEDGE["z"][0]], [BAR[0] + WEDGE["t"][1], BAR[1] + WEDGE["r"][1], WEDGE["z"][1]],
                 f"head_wedge{q + 1}", "head", "steel")
        rotate([el], "z", math.degrees(ang), BAR_C)
        out.append(el)
    return out


def build_cutters():
    """Four cutters at the head's leading face, a quarter turn apart: each an iron holder in a radial slot with
    a steel bit at its tip, set for the small bore (the large one sets them out CUTTER_SET)."""
    out = []
    bore = CYL["thin"]["bore"]
    for q in CUTTER_ANGLES:
        ang = q * math.pi / 2
        pid = f"cutter{q + 1}"
        h = HOLDER
        holder = box([BAR[0] - h["half"], BAR[1] + h["r"][0], h["z"][0]], [BAR[0] + h["half"], BAR[1] + h["r"][1], h["z"][1]], f"cut{q + 1}_holder", pid, "iron")
        bit = box([BAR[0] - BIT["half"], BAR[1] + BIT["r0"], BIT["z"][0]], [BAR[0] + BIT["half"], BAR[1] + bore - BIT["clear"], BIT["z"][1]],
                  f"cut{q + 1}_bit", pid, "steel")
        rotate([holder, bit], "z", math.degrees(ang), BAR_C)
        out += [holder, bit]
    return out


# ---------------------------------------------------------------- the feed
def build_feed():
    """The crosshead on the bench's ways (shoes, a base plate, two uprights, a top rail with the rope's eye),
    its push handle, the control rod from the crosshead through the bore, and the yoke in the head's groove."""
    f = "feed"
    z0, z1 = XH_Z
    (wx0, wx1), (ex0, ex1) = RAILS
    out = [box([wx0 + 0.2, WAY_Y[1], z0], [wx1 - 0.2, WAY_Y[1] + 0.6, z1], "feed_shoe_w", f, "iron"),
           box([ex0 + 0.2, WAY_Y[1], z0], [ex1 - 0.2, WAY_Y[1] + 0.6, z1], "feed_shoe_e", f, "iron"),
           box([wx0 + 0.2, 12.0, z0], [ex1 - 0.2, 14.0, z1], "feed_base", f, "oak"),
           box([14.8, 14.0, z0], [17.2, 15.6, z1], "feed_boss", f, "iron"),
           box([9.0, 14.0, z0], [11.5, 24.5, z1], "feed_upright_w", f, "oak"),
           box([20.5, 14.0, z0], [23.0, 24.5, z1], "feed_upright_e", f, "oak"),
           box([9.0, 24.5, z0], [23.0, 27.0, z1], "feed_toprail", f, "oak")]
    out += eye("z", (BAR[0], XROPE_Y, 0.0), z0 - 0.4, z0, ROPE_W / 2 + 0.02, ROPE_W / 2 + 0.3, "feed_ropeeye", f, "iron")
    # the push handle: two arms off the uprights' north faces and a grip across, over the bar
    for tag, (x0, x1) in (("w", (9.6, 10.9)), ("e", (21.1, 22.4))):
        out.append(box([x0, 21.0, z0 - 2.0], [x1, 22.0, z0], f"feed_handlearm_{tag}", f, "iron"))
    out.append(box([9.6, 21.0, z0 - 2.0], [22.4, 22.0, z0 - 1.2], "feed_grip", f, "oak"))
    out += long_box([ROD["x"][0], ROD["y"][0], ROD["z"][0]], [ROD["x"][1], ROD["y"][1], ROD["z"][1]], "feed_rod", f, "iron")
    out += eye("z", BAR_C, *YOKE_Z, YOKE_IN, YOKE_OUT, "feed_yoke", f, "steel")
    return out


def xrope_pieces():
    """The crosshead's rope between the drum and the crosshead, at rest: (z0, z1) of each piece the crosshead
    takes up into its top rail as it comes (the one nearest the drum first)."""
    a, b = XROPE_TOP[1], XH_Z[0]
    s = (b - a) / XROPE_SEGS
    return [(a + s * i, a + s * (i + 1)) for i in range(XROPE_SEGS)]


def wrope_pieces():
    """The weight's rope: each piece at rest (all inside the weight, under its cap) and how far it rides down
    with the weight before it stops, its place in the rope once the weight has fallen past it."""
    s = DROP / WROPE_SEGS
    return [((W_CAP0 - s, W_CAP0), s * i) for i in range(WROPE_SEGS)]


def build_ropes():
    out = []
    h = ROPE_W / 2
    y = XROPE_Y
    out.append(box([BAR[0] - h, y - h, XROPE_TOP[0]], [BAR[0] + h, y + h, XROPE_TOP[1] + ROPE_LAP], "ropes_xtop", "feedropes", "rope"))
    for i, (z0, z1) in enumerate(xrope_pieces(), 1):
        w = h - 0.006 * i
        out.append(box([BAR[0] - w, y - w, z0], [BAR[0] + w, y + w, z1 + ROPE_LAP], f"xr{i}_piece", f"xrope{i}", "rope"))
    x, z = WROPE_X, WROPE_Z
    out.append(box([x - h, W_CAP0 - 0.02, z - h], [x + h, DRUM_C[0], z + h], "ropes_wtop", "feedropes", "rope"))
    for i, ((y0, y1), ride) in enumerate(wrope_pieces(), 1):
        w = h - 0.006 * i
        part = "feedropes" if ride == 0 else f"wrope{i}"
        name = "ropes_w1" if ride == 0 else f"wr{i}_piece"
        out.append(box([x - w, y0 - ROPE_LAP, z - w], [x + w, y1 + ROPE_LAP, z + w], name, part, "rope"))
    return out


def build_drum():
    """The feed drum's shaft across the bench end, the big drum the crosshead's rope winds onto (its wraps
    drawn on it), and the small drum the weight's rope pays off."""
    out = disc("x", DRUM_P, 4.4, 27.6, 0.55, "drum_shaft", "drum", "iron")
    out += disc("x", DRUM_P, *DRUM_X, R_DRUM, "drum_big", "drum", "oak", k=6)
    out += disc("x", DRUM_P, BAR[0] - 0.8, BAR[0] + 0.8, R_DRUM + 0.12, "drum_wraps", "drum", "rope", k=6, phase=0.15)
    for i, (a0, a1) in enumerate(((DRUM_X[0] - 0.4, DRUM_X[0]), (DRUM_X[1], DRUM_X[1] + 0.4)), 1):
        out += disc("x", DRUM_P, a0, a1, R_DRUM + 0.4, f"drum_flange{i}", "drum", "iron", k=6)
    out += disc("x", DRUM_P, *WDRUM_X, R_WDRUM, "drum_small", "drum", "oak", k=6)
    out += disc("x", DRUM_P, WROPE_X - 0.35, WROPE_X + 0.35, R_WDRUM + 0.12, "drum_swraps", "drum", "rope", k=6, phase=0.15)
    for i, (a0, a1) in enumerate(((WDRUM_X[0] - 0.4, WDRUM_X[0]), (WDRUM_X[1], WDRUM_X[1] + 0.4)), 1):
        out += disc("x", DRUM_P, a0, a1, R_WDRUM + 0.5, f"drum_sflange{i}", "drum", "iron", k=6)
    return out


def build_weight():
    (x0, x1), (z0, z1), (y0, y1) = WEIGHT["x"], WEIGHT["z"], WEIGHT["y"]
    out = [box([x0, y0, z0], [x1, y1, z1], "weight_body", "weight", "iron"),
           box([x0 + 0.25, y1, z0 + 0.25], [x1 - 0.25, W_CAP0, z1 - 0.25], "weight_cap", "weight", "iron")]
    out += eye("y", (WROPE_X, 0.0, WROPE_Z), W_CAP0, W_CAP0 + 0.3, ROPE_W / 2 + 0.02, ROPE_W / 2 + 0.25, "weight_eye", "weight", "iron")
    return out


# ---------------------------------------------------------------- the cylinders
def skin_slices(cls):
    """The cast skin's slices along the bore: (z0, z1), slice 1 at the drive end (cut first). Neighbours
    overlap a little; every other one stands a hair further in, so they never share a face."""
    z0, z1 = CYL[cls]["z"]
    a, b = z0 + SKIN_END, z1 - SKIN_END
    n = SKIN_SLICES
    s = (b - a) / n
    out = []
    for j in range(n):
        hi = b - s * j + (0.015 if j else 0.0)
        lo = b - s * (j + 1) - (0.015 if j < n - 1 else 0.0)
        out.append((lo, hi))
    return out


def blank_tag(cls):
    return {"thin": ("s", "blanksmall"), "thick": ("l", "blanklarge")}[cls]


def build_cylinder(cls):
    """The casting (the work): its barrel, a flange at each end (for the engine's covers), and the cast skin
    in the bore, slice by slice, a quarter of the 12-gon to a rig part."""
    t, req = blank_tag(cls)
    g = CYL[cls]
    z0, z1 = g["z"]
    out = ngon_ring(BAR_C, z0, z1, g["bore"], g["out"], f"bl{t}_wall", f"blank{'small' if t == 's' else 'large'}", "cast", inner="bored")
    for i, (a, b) in enumerate(((z0, z0 + FLANGE_T), (z1 - FLANGE_T, z1)), 1):
        out += ngon_ring(BAR_C, a + 0.004, b - 0.004, g["out"] - 0.3, g["flange"], f"bl{t}_flange{i}_", f"blank{'small' if t == 's' else 'large'}", "cast")
    for j, (a, b) in enumerate(skin_slices(cls), 1):
        r_in = g["rough"] - 0.01 * (j % 2)
        w = 2 * g["bore"] * math.tan(math.pi / NGON)
        for q in range(4):
            pid = f"s{t}{j}{'tlbr'[q]}"
            for i in (-1, 0, 1):
                k = (3 * q + i) % NGON
                s = 0.008 * ((i + 1) % 3)
                el = radial("z", BAR_C, a + s, b - s, r_in, g["bore"], w, TAU * k / NGON, f"{pid}_stave{i + 2}", pid, "rough")
                out.append(el)
    return out


def chain_path(cls):
    """The chain's centre line, as a polyline in the x-y plane: up from the west hook bar, round the casting's
    upper flats at CHAIN_OFF off them, down to the east hook bar."""
    rc = CYL[cls]["out"] + CHAIN_OFF
    rv = rc / math.cos(math.pi / NGON)
    pts = [(BAR[0] - rc, HOOK_Y)]
    for a in (75, 45, 15, -15, -45, -75):
        dx, dy = ang_dir(a * DEG)
        pts.append((BAR[0] + rv * dx, BAR[1] + rv * dy))
    pts.append((BAR[0] + rc, HOOK_Y))
    return pts


def build_chains(cls):
    t, _ = blank_tag(cls)
    pid = f"chain{'small' if t == 's' else 'large'}"
    pts = chain_path(cls)
    segs = list(zip(pts, pts[1:]))
    lens = [math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in segs]
    total = sum(lens)
    n = max(2, round(total / LINK["pitch"]))
    pitch = total / n
    out = []
    for zc_i, zc in enumerate(CHAIN_Z, 1):
        for i in range(n):
            s = (i + 0.5) * pitch
            for (a, b), ln in zip(segs, lens):
                if s <= ln + 1e-9:
                    break
                s -= ln
            u = s / ln
            px, py = a[0] + (b[0] - a[0]) * u, a[1] + (b[1] - a[1]) * u
            ang = math.atan2(b[1] - a[1], b[0] - a[0])
            across, deep = LINK["wide"] if i % 2 == 0 else LINK["narrow"]
            el = box([px - pitch / 2 - 0.12, py - deep / 2, zc - across / 2], [px + pitch / 2 + 0.12, py + deep / 2, zc + across / 2],
                     f"ch{t}{zc_i}_link{i + 1:02d}", pid, "iron")
            rotate([el], "z", math.degrees(ang), (px, py, zc))
            out.append(el)
    return out


# ---------------------------------------------------------------- the cradle
def v_cheek(side, vertex_y, name):
    """One face of a V-block: a box whose top face is the V's face on `side` (-1 west, +1 east), from
    CHEEK_U along it from the V's bottom, CHEEK_T thick below it."""
    u0, u1 = CHEEK_U
    tx, ty = side * math.cos(V_HALF), math.sin(V_HALF)       # along the face, away from the bottom
    nx, ny = -side * math.sin(V_HALF), math.cos(V_HALF)      # the face's normal, into the V
    p0 = (BAR[0] + tx * u0, vertex_y + ty * u0)
    p1 = (BAR[0] + tx * u1, vertex_y + ty * u1)
    a = (p0[0] - nx * CHEEK_T / 2, p0[1] - ny * CHEEK_T / 2, 0.0)
    b = (p1[0] - nx * CHEEK_T / 2, p1[1] - ny * CHEEK_T / 2, 0.0)
    return a, b


def build_saddles():
    """Two V-blocks of oak across the cradle, each on two screw jacks (the jacks' nuts are their own parts),
    authored as set for the small cylinder; the large one winds them down V_DROP."""
    out = []
    v = V_VERTEX["thin"]
    bot = V_BOTTOM["thin"]
    for i, (z0, z1) in enumerate(SADDLE_Z, 1):
        out.append(box([V_X[0], bot, z0], [V_X[1], v - 0.3, z1], f"saddle{i}_base", "saddles", "oak"))
        for side, tag in ((-1, "w"), (1, "e")):
            a, b = v_cheek(side, v, f"saddle{i}_{tag}")
            el = strut((a[0], a[1], (z0 + z1) / 2), (b[0], b[1], (z0 + z1) / 2), CHEEK_T, z1 - z0, f"saddle{i}_cheek_{tag}", "saddles", "oak", axis="z")
            out.append(el)
    return out


def jack_spots():
    return [(x, (z0 + z1) / 2) for (z0, z1) in SADDLE_Z for x in JACK_X]


def build_jacks():
    """The jack screws' nuts (each a rig part: it turns, and runs down its screw for the large cylinder), with
    a tommy bar through each; the V-block rests on them."""
    out = []
    for i, (x, z) in enumerate(jack_spots(), 1):
        y0 = V_BOTTOM["thin"] - NUT_H
        out += disc("y", (x, 0.0, z), y0, V_BOTTOM["thin"], NUT_R, f"jack{i}_nut", f"jack{i}", "iron")
        out.append(box([x - 0.15, y0 + 0.3, z - 1.25], [x + 0.15, y0 + 0.7, z + 1.25], f"jack{i}_bar", f"jack{i}", "iron"))
    return out


# ---------------------------------------------------------------- the oil cups
def build_cups():
    """A sight-feed cup on each bar bearing, on a bracket off the bearing's pedestal (the frame's); a feed pipe
    from the cup's base into the bearing. Their oil is one rig part (both levels are at one height). The
    glass is in the Transparent render pass, so the level shows from every side."""
    out = []
    (x0, x1), (y0, y1) = CUP["x"], CUP["y"]
    g = 0.15
    for tag, (z0, z1) in (("far", CUP["far_z"]), ("drive", CUP["drive_z"])):
        zm = (z0 + z1) / 2
        p = f"fr_cup{tag}"
        els = [box([x0, y0 - 0.3, z0], [x1, y0, z1], f"{p}_base", "frame", "brass"),
               box([x0, y1, z0], [x1, y1 + 0.3, z1], f"{p}_cap", "frame", "brass"),
               box([x0, y0, z0], [x1, y1, z0 + g], f"{p}_glass_n", "frame", "glass"),
               box([x0, y0, z1 - g], [x1, y1, z1], f"{p}_glass_s", "frame", "glass"),
               box([x0, y0, z0 + g], [x0 + g, y1, z1 - g], f"{p}_glass_w", "frame", "glass"),
               box([x1 - g, y0, z0 + g], [x1, y1, z1 - g], f"{p}_glass_e", "frame", "glass"),
               box([x0 + 0.5, y1 + 0.3, zm - 0.2], [x1 - 0.5, y1 + 0.6, zm + 0.2], f"{p}_knob", "frame", "brass"),
               box([x0 + 0.4, BAR[1] - BRG_HALF - 0.8, zm - 0.3], [x0 + 1.0, y0 - 0.3, zm + 0.3], f"{p}_bracket", "frame", "brass"),
               box([x0 + 0.4, BAR[1] - BRG_HALF - 0.8, zm - 0.3], [BAR[0] - 2.0, BAR[1] - BRG_HALF, zm + 0.3], f"{p}_arm", "frame", "brass"),
               box([x1, y0 - 0.25, zm - 0.1], [BAR[0] - BRG_HALF + 0.05, y0 - 0.05, zm + 0.1], f"{p}_pipe", "frame", "brass")]
        for el in els:
            if "_glass_" in el.name:
                el.render_pass = TRANSPARENT
        out += els
        out.append(box([x0 + g + 0.02, OIL_Y, z0 + g + 0.02], [x1 - g - 0.02, OIL_Y + OIL_EMPTY, z1 - g - 0.02], f"oillevel_{tag}", "oillevel", "oil"))
    return out


# ---------------------------------------------------------------- the frame
def pedestal_post(name, cx, z0, z1, top, half=2.0):
    return box([cx - half, SILL_Y[1], z0], [cx + half, top, z1], f"fr_{name}_post", "frame", "oak")


def build_frame():
    f = "frame"
    out = []
    # the long sills and the cross sills between them
    for tag, (x0, x1) in zip("we", SILLS):
        out += long_box([x0, SILL_Y[0], SILL_Z[0]], [x1, SILL_Y[1], SILL_Z[1]], f"fr_sill_{tag}", f, "oak")
    for tag, (z0, z1) in CROSS.items():
        out.append(box([SILLS[0][1], SILL_Y[0], z0], [SILLS[1][0], SILL_Y[1], z1], f"fr_cross_{tag}", f, "oak"))
    # the bench end: two posts, the top beam, the drum shaft's bearings, the beam carrying the rails' far ends
    for tag, (x0, x1) in zip("we", POSTS):
        out.append(box([x0, SILL_Y[1], POST_Z[0]], [x1, POST_TOP, POST_Z[1]], f"fr_post_{tag}", f, "oak"))
    out.append(box([POSTS[0][0], POST_TOP, POST_Z[0]], [POSTS[1][1], POST_TOP + 2.5, POST_Z[1]], "fr_topbeam", f, "oak"))
    for tag, (x0, x1) in (("w", (POSTS[0][1], POSTS[0][1] + 1.2)), ("e", (POSTS[1][0] - 1.2, POSTS[1][0]))):
        out.append(box([x0, DRUM_C[0] - 1.4, DRUM_C[1] - 1.4], [x1, DRUM_C[0] + 1.4, DRUM_C[1] + 1.4], f"fr_drum_bearing_{tag}", f, "iron"))
    out.append(box([POSTS[0][0], RAIL_Y[0] - 3.0, POST_Z[1]], [POSTS[1][1], RAIL_Y[0], 7.5], "fr_farbeam", f, "oak"))
    # the bench: two rails with iron ways, on legs at the middle and the near end
    for tag, (x0, x1) in zip("we", RAILS):
        out += long_box([x0, RAIL_Y[0], RAIL_Z[0]], [x1, RAIL_Y[1], RAIL_Z[1]], f"fr_rail_{tag}", f, "oak")
        out += long_box([x0 + 0.4, WAY_Y[0], RAIL_Z[0]], [x1 - 0.4, WAY_Y[1], RAIL_Z[1]], f"fr_way_{tag}", f, "iron")
        for i, (z0, z1) in enumerate(LEGS_Z, 1):
            out.append(box([x0, SILL_Y[1], z0], [x1, RAIL_Y[0], z1], f"fr_leg_{tag}{i}", f, "oak"))
    # the bar's pedestals (its bearings are the fitted part); the entry shaft's pedestals and bearings
    out.append(pedestal_post("farbrg", BAR[0], BRG_FAR_Z[0] + 0.3, BRG_FAR_Z[1] - 0.3, BAR[1] - BRG_HALF))
    out.append(pedestal_post("drivebrg", BAR[0], BRG_DRIVE_Z[0] + 0.3, BRG_DRIVE_Z[1] - 0.3, BAR[1] - BRG_HALF))
    for i, (z0, z1) in enumerate(ENTRY_BRG_Z, 1):
        x, y = ENTRY
        h = ENTRY_HALF
        out.append(box([x - h, y - h, z0], [x + h, y + h, z1], f"fr_entry_bearing{i}", f, "iron"))
        out.append(box([x - h + 0.4, SILL_Y[1], z0 + 0.4], [x + h - 0.4, y - h, z1 - 0.4], f"fr_entry_post{i}", f, "oak"))
    # the jack screws, in the saddles' cross beams
    for i, (x, z) in enumerate(jack_spots(), 1):
        out += disc("y", (x, 0.0, z), 1.5, V_BOTTOM["thick"] + 2.5, 0.4, f"fr_jack{i}_screw", f, "iron")
    # the chains' hook bars on their cross beam: an iron bar across, on three legs; either cylinder's chains hook over it
    for zi, zc in enumerate(CHAIN_Z, 1):
        out.append(box([HOOKBAR_X[0], HOOK_Y - 0.2, zc - 0.2], [HOOKBAR_X[1], HOOK_Y + 0.2, zc + 0.2], f"fr_hookbar{zi}", f, "iron"))
        for li, x in enumerate((HOOKBAR_X[0] + 0.3, BAR[0], HOOKBAR_X[1] - 0.3), 1):
            out.append(box([x - 0.25, SILL_Y[1], zc - 0.25], [x + 0.25, HOOK_Y - 0.2, zc + 0.25], f"fr_hookbar{zi}_leg{li}", f, "iron"))
    return out


def build():
    els = build_entry() + build_bar() + build_bearings() + build_head() + build_cutters() + build_feed() + build_ropes()
    els += build_drum() + build_weight()
    for cls in CLASSES:
        els += build_cylinder(cls) + build_chains(cls)
    els += build_saddles() + build_jacks() + build_cups() + build_frame()
    return els


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def per_class(thin, thick=None):
    return {"thin": r6(thin), "thick": r6(thin if thick is None else thick)}


WORK = {"name": "cutter travel", "unit": "blocks", "step": 0.015625,
        "end": {"thin": TRAVEL["thin"] / B, "thick": TRAVEL["thick"] / B}}
PATH = WORK
FOREVER = 1000.0


def gauge(motion, axis, amount, wins, pivot=None, mode=None):
    d = {"type": "gauge", "motion": motion, "axis": axis}
    if pivot is not None:
        d["pivot"] = pivot
    d["amount"] = amount
    if mode:
        d["mode"] = mode
    else:
        d["windows"] = wins
    return d


def from_to(w0, w1):
    """One window rising over W w0..w1 and open to the end of the job."""
    return [{"from": r6(w0), "to": FOREVER, "ease": r6(w1 - w0)}]


def travel_gauge(per_block, axis="z", motion="slide", pivot=None):
    """A motion that follows the head's travel, `per_block` (blocks, or radians) per block of it, from W = 0 to
    the longest end, as the feed's: the small cylinder's job ends part way along it."""
    return gauge(motion, axis, per_class(per_block * E), from_to(0.0, E), pivot=pivot)


def skin_window(cls, j):
    """W over which the cutters cross skin slice j (1-based): from their leading face reaching its drive-end
    edge to their trailing face leaving its far edge."""
    lo, hi = skin_slices(cls)[j - 1]
    return (CUT_SPAN[0] - hi) / B, (CUT_SPAN[1] - lo) / B


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    bar_pivot = pt(BAR[0], BAR[1], 0.0)
    feed = travel_gauge(-1.0)
    parts = [
        {"id": "entry", "match": ["entry_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "z", "pivot": pt(ENTRY[0], ENTRY[1], 0.0), "ratio": 1.0}]},
        {"id": "bar", "match": ["bar_*", "wheel_*"], "requires": "bar",
         "drivers": [{"type": "rotate", "axis": "z", "pivot": bar_pivot, "ratio": r6(BAR_RATIO)}]},
        {"id": "bearings", "match": ["brg_*"], "requires": "bearings", "drivers": []},
        {"id": "head", "match": ["head_*"], "requires": "cutterhead",
         "drivers": [{"type": "rotate", "axis": "z", "pivot": bar_pivot, "ratio": r6(BAR_RATIO)}, feed]},
    ]
    for q in CUTTER_ANGLES:
        dx, dy = ang_dir(q * math.pi / 2)
        axis, sgn = ("y", dy) if abs(dy) > 0.5 else ("x", dx)
        parts.append({"id": f"cutter{q + 1}", "match": [f"cut{q + 1}_*"], "requires": "cutterhead", "ride": "head",
                      "drivers": [gauge("slide", axis, per_class(0.0, round(sgn) * CUTTER_SET / B), None, mode="present")]})
    parts += [
        {"id": "feed", "match": ["feed_*"], "requires": "feed", "drivers": [feed]},
        {"id": "drum", "match": ["drum_*"], "requires": "feed",
         "drivers": [travel_gauge(-B / R_DRUM, axis="x", motion="rotate", pivot=pt(0.0, *DRUM_C))]},
        {"id": "weight", "match": ["weight_*"], "requires": "feed", "drivers": [travel_gauge(-R_WDRUM / R_DRUM, axis="y")]},
    ]
    for i, (z0, z1) in enumerate(xrope_pieces(), 1):
        w0 = (XH_Z[0] - z0) / B              # the crosshead's north face reaches the piece's north end: from there it rides in the top rail
        parts.append({"id": f"xrope{i}", "match": [f"xr{i}_*"], "requires": "feed",
                      "drivers": [gauge("slide", "z", per_class(-(E - w0)), from_to(w0, E))]})
    for i, (_, ride) in enumerate(wrope_pieces(), 1):
        if ride == 0:
            continue
        w1 = ride * R_DRUM / R_WDRUM / B     # the head's travel by which the weight has fallen `ride`
        parts.append({"id": f"wrope{i}", "match": [f"wr{i}_*"], "requires": "feed",
                      "drivers": [gauge("slide", "y", per_class(-ride / B), from_to(0.0, w1))]})
    parts.append({"id": "feedropes", "match": ["ropes_*"], "requires": "feed", "drivers": []})
    parts.append({"id": "saddles", "match": ["saddle*"], "requires": None,
                  "drivers": [gauge("slide", "y", per_class(0.0, -V_DROP / B), None, mode="present")]})
    for i, (x, z) in enumerate(jack_spots(), 1):
        parts.append({"id": f"jack{i}", "match": [f"jack{i}_*"], "requires": None,
                      "drivers": [gauge("rotate", "y", per_class(0.0, -V_DROP / JACK_LEAD * TAU), None, pivot=pt(x, 0.0, z), mode="present"),
                                  gauge("slide", "y", per_class(0.0, -V_DROP / B), None, mode="present")]})
    (cx0, cx1), (cy0, _) = CUP["x"], CUP["y"]
    parts.append({"id": "oillevel", "match": ["oillevel_*"], "requires": None,
                  "drivers": [{"type": "stretch", "axis": "y", "anchor": pt((cx0 + cx1) / 2, OIL_Y, 0.0),
                               "length": r6(OIL_EMPTY / B), "travel": r6((OIL_FULL - OIL_EMPTY) / B), "input": "oil"}]})
    for cls in CLASSES:
        t, _ = blank_tag(cls)
        size = "small" if t == "s" else "large"
        parts.append({"id": f"blank{size}", "match": [f"bl{t}_*"], "requires": f"blank{size}", "drivers": []})
        parts.append({"id": f"chain{size}", "match": [f"ch{t}*"], "requires": f"blank{size}", "drivers": []})
        for j in range(1, SKIN_SLICES + 1):
            w0, w1 = skin_window(cls, j)
            for q in range(4):
                dx, dy = ang_dir(q * math.pi / 2)
                axis, sgn = ("y", dy) if abs(dy) > 0.5 else ("x", dx)
                pid = f"s{t}{j}{'tlbr'[q]}"
                parts.append({"id": pid, "match": [f"{pid}_*"], "requires": f"blank{size}",
                              "drivers": [gauge("slide", axis, per_class(round(sgn) * SKIN_SINK / B), from_to(w0, w1))]})
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []})
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses
REST = (0.0, 0.0, 0.0, 0, 0.0, 0.0)          # (theta, travel, W, k, p, oil): no cylinder, the tank empty; the authored pose


def inputs_of(pose):
    th, ps, W, k, p = pose[:5]
    oil = pose[5] if len(pose) > 5 else 0.0
    return {"theta": th, "travel": ps, "work": W, "size": k, "presence": p, "oil": oil}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path or PATH)


def pose_at(k, W, theta=None, oil=0.6, p=1.0):
    """A pose of a bore in progress: cylinder k, the head at W; the axle at the gameplay's pace."""
    th = TAU * TURNS_PER_BLOCK * W if theta is None else theta
    return (th, abs(th), W, k, p, oil)


def end_of(k):
    return TRAVEL[CLASSES[k - 1]] / B


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("output", "chips", "drip")


def blank_box(cls):
    g = CYL[cls]
    z0, z1 = g["z"]
    r = g["flange"] / math.cos(math.pi / NGON)
    return pt(BAR[0] - g["flange"], BAR[1] - g["flange"], z0) + pt(BAR[0] + g["flange"], BAR[1] + g["flange"], z1)


def make_rig(parts):
    progress_of({"work": WORK})
    rough = CYL["thin"]["rough"]
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the bench end's west cell, "
                    "nearest the player who placed it; the machine runs south to the drive, where the axle comes in. work is the "
                    "cutter head's travel through the bore, W, in blocks (0 outside the drive end; the end is past the far end of "
                    "the small or the large cylinder); k is the cylinder (1 small, 2 large). The bearings' oil cups follow the "
                    "rig's oil input (the MachineOil tank's fill, 0..1). See the cylinder borer's README for the schema.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "infeedSide": "east",
        "outputSide": "west",
        "output": {"pos": pt(0.01, 4.0, (SADDLE_Z[0][1] + SADDLE_Z[1][0]) / 2)},
        "chips": {"pos": pt(BAR[0], BAR[1] - rough, Z_DRIVE_MOUTH)},
        "drip": {"pos": pt(BAR[0] - BRG_HALF, CUP["y"][0] - 0.15, sum(CUP["far_z"]) / 2)},
        "work": dict(WORK),
        "bore": {"turnsPerBlock": TURNS_PER_BLOCK,
                 "blanks": {"thin": "seraphhorizons:cylinderblank-iron-small", "thick": "seraphhorizons:cylinderblank-iron-large"},
                 "cylinders": {"thin": "seraphhorizons:cylinder-iron-small", "thick": "seraphhorizons:cylinder-iron-large"},
                 "box": {"thin": blank_box("thin"), "thick": blank_box("thick")},
                 "_comment": "turnsPerBlock: axle turns per block of the head's travel, the gameplay's pace (provisional): the feed "
                             "is weighted, not geared to the bar, so no pace is drawn and any pace shows true. blanks, cylinders: "
                             "each class's work in and out (provisional item codes from #639; the items do not exist yet). box: "
                             "each cylinder's bounds (blocks, from the controller), for a collision box while it is loaded: the "
                             "cells' boxes leave the work out."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: the build frame's
    corner is the controller's). Gauge windows are in blocks of the head's travel, so they stay as they are."""
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
    ship["powerCell"] = shift_cell(rig["powerCell"], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


WORK_REQUIRES = ("blanksmall", "blanklarge")


def shipped_cells(shape, ship_parts, sp):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig (every reader
    rebuilds them the same way), leaving out the work (a loaded cylinder's box is the rig's bore.box); then
    the lids."""
    written = flatten(shape["elements"], textures={})
    req = {p["id"]: p["requires"] for p in ship_parts}
    rest = []
    for w in written:
        pid = part_of(ship_parts, w.name)
        if req[pid] in WORK_REQUIRES:
            continue
        rest.append(posed(w, _part_matrix(ship_parts, pid, inputs_of(REST), sp)))
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
    return with_lids(out)


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    sp = ship["work"]
    poses = [REST, pose_at(1, 0.3), pose_at(1, 0.9, oil=1.0), pose_at(2, 0.55), pose_at(2, 1.2, oil=0.2),
             (1.3, 7.0, 0.6, 1, 0.6, 0.3), (0.4, 2.0, 1.3, 2, 1.0, 1.0)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose), sp), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    span = [(min(c[k] for c in cells), max(c[k] for c in cells)) for k in range(3)]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells x {span[0]}, y {span[1]}, "
          f"z {span[2]}; power {ship['powerCell']} {ship['powerFace']}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
def reference_poses():
    """theta in {0, 1.1, -2.3, 2.9} with psi; for each cylinder, W through the travel (every skin window's
    edges and middles among it), W at 0 and its end, p at 1 and 0.4; no cylinder; the oil at 0, 0.35 and 1
    in turn."""
    out = []
    for i, th in enumerate((0.0, 1.1, -2.3, 2.9)):
        for extra in (0.0, 7.3):
            out.append((th, round(abs(th) + extra, 6), 0.0, 0, 0.0, (0.0, 0.35, 1.0)[i % 3]))
    for k in (1, 2):
        cls = CLASSES[k - 1]
        ws = {0.0, end_of(k), 0.05, 0.4, 0.75}
        for j in range(1, SKIN_SLICES + 1):
            w0, w1 = skin_window(cls, j)
            ws |= {round(w0, 6), round((w0 + w1) / 2, 6), round(w1, 6)}
        for i, W in enumerate(sorted(w for w in ws if w <= end_of(k) + 1e-9)):
            th = (0.0, 1.1, -2.3, 2.9)[i % 4]
            for p in ((1.0,) if i % 5 else (1.0, 0.4)):
                out.append((th, round(abs(th) + 0.37 * i, 6), W, k, p, (1.0, 0.35, 0.0)[i % 3]))
    return out


def reference_json(ship_parts, sp):
    poses = []
    for pose in reference_poses():
        th, ps, W, k, p, oil = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose), sp)) for q in ship_parts}
        poses.append({"theta": th, "travel": ps, "work": W, "size": k, "presence": p, "oil": oil, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped cylinderborer-rig.json's parts and work: each part's matrix as 3 "
                        "rows of 4 (block units) at each pose (W in blocks of the cutter head's travel). The site's and the mod's "
                        "tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Keep element names when editing: the rig "
             "finds its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return (REST, pose_at(1, 0.45), pose_at(2, 0.7), pose_at(1, end_of(1)), pose_at(2, end_of(2)))


def on_show(part, k):
    """Whether a part can be seen with cylinder k in the cradle: each cylinder's set only with that one."""
    if part in ("blanksmall", "chainsmall") or (part.startswith("ss") and part[2:3].isdigit()):
        return k == 1
    if part in ("blanklarge", "chainlarge") or (part.startswith("sl") and part[2:3].isdigit()):
        return k == 2
    return True


def is_skin(part):
    return part[:2] in ("ss", "sl") and part[2:3].isdigit()


def is_rope_piece(part):
    return (part.startswith("xrope") or part.startswith("wrope")) and part[5:].isdigit()


def shown(posed_els, pose, parts=None):
    """The posed elements as they can be seen: another cylinder's set, a skin quarter sunk into the wall, and
    the rope taken up into the crosshead or still inside the weight moved far away (copies; the order kept),
    so the z-fighting fix and its check deal only with faces that show."""
    k = pose[3]
    boxes = {}
    for e in posed_els:
        if e.name in ("feed_toprail", "weight_body", "weight_cap"):
            boxes[e.name] = e.aabb()
    out = []
    for e in posed_els:
        lo, hi = e.aabb()
        gone = not on_show(e.part, k)
        if is_skin(e.part) and not gone:
            g = CYL["thin" if e.part[1] == "s" else "thick"]
            r = math.hypot(e.c[0] - BAR[0], e.c[1] - BAR[1])
            gone = r - (g["bore"] - g["rough"]) / 2 > g["bore"] + 0.05
        if is_rope_piece(e.part) or e.part == "feedropes":
            for key in (("feed_toprail",), ("weight_body", "weight_cap")):
                bs = [boxes[n] for n in key if n in boxes]
                if not bs:
                    continue
                blo = [min(b[0][q] for b in bs) for q in range(3)]
                bhi = [max(b[1][q] for b in bs) for q in range(3)]
                if all(blo[q] - 1e-6 <= lo[q] and hi[q] <= bhi[q] + 1e-6 for q in range(3)):
                    gone = True
        if gone:
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def fix_coplanar(els, parts):
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose), coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the cylinder borer's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_cylinderborer
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        for pose, pairs in before.items():
            print(f"coplanar faces before the fix at {pose}: {len(pairs)} pairs")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_cylinderborer.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "cylinderborer.json", args.out / "cylinderborer_frame.json", args.out / "cylinderborer-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "cylinderborer.json", SHAPE_DIR / "cylinderborer_frame.json", RIG_DIR / "cylinderborer-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts, ship["work"])
    ok = validate_cylinderborer.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship), reference_dumps(reference_json(ship_parts, ship["work"])))
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
