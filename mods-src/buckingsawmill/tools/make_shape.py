#!/usr/bin/env python3
"""Generate the Bucking Sawmill's shapes and rig, taking some parts from Immersive Woodworking's sawmill.

Most of the bucking mill is built here from plain boxes. Its gears, saw blades, saw heads and
cranks are taken from the sawmill model of Immersive Woodworking (IW) by Bobrik00, used with
permission (see ../CREDITS.md), and the new boxes borrow IW elements' face mapping. So this
script reads `build/mods/immersivewoodworking_*.zip` and writes, deterministically,

    buckingmill.json        full machine, every moving part   (assets/.../shapes/block/)
    buckingmill_frame.json  static frame only (block + item)  (assets/.../shapes/block/)
    rig.json                cells, anchors and part rig        (assets/.../config/)

to the mod's assets, or with `--out DIR` all three into DIR. Run it from anywhere with
`python3 mods-src/buckingsawmill/tools/make_shape.py` after `python3 tools/packtool.py fetch`.
It validates its own output (see `validate`) and exits non-zero if a check fails. Stdlib only.

The machine is a pair of drag saws with a windlass lift (the mod's README describes it part by
part). Everything is in voxels (16 per block) in the native south-facing frame: x = machine
width, y up, z = depth (north is -z), controller cell at the origin. In short:

* Each station (x = 32 and 64) has a carriage (IW's sash, squeezed to a short frame) sliding
  up and down in a pair of IW posts on the north side of the trunk. A saw head (IW's sash
  rail, turned on its side) hangs from the carriage's guide bars and strokes along z. One of
  IW's blades, turned to cross-cut (normal x, long axis z, teeth down), is clamped in the head
  and reaches south across the trunk to a tail piece, which strokes in the slot of a guide block
  sliding on a slim post at the south edge: the blade is held at both ends.
* A slotted crosshead ("yoke"), a vertical bar sliding along z in fixed guides, carries the
  head's pin in its slot at every height. It is pushed by a rod from IW's crank on the
  shared shaft, so the stroke is driven at any depth.
* The windlass: IW's gear set at the west end, a rectifier that always runs. Each of IW's two
  loose pinions drives through its own one-way catch, so whichever way the shaft turns one is
  carried with it and IW's crown disc, which both mesh, always turns the same way. The disc's
  axle is in two halves joined by a dog clutch; the other half carries IW's small crown gear,
  which turns the drum pinion on the drum shaft, so clutching the halves winds the saws up; one
  drum per station (and a spool for each guide block) winds its rope. No toothed wheel meshes
  nothing. The mill cycles without stopping while it turns: nothing latches the saws at the top.
* The levers: a trip rod (pushrod) up station 1's west post, which the carriage pushes down at
  the bottom of the cut, and one rock shaft along x: its tappet arm on the rod's top, its fork
  arm in the dog clutch's groove, and an over-centre counterweight that holds the lever whichever
  way it was last thrown. A collar on the rod over the carriage's lug throws it back at the top.
"""

from __future__ import annotations

import argparse
import copy
import json
import math
import re
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
MOD = ROOT / "mods-src" / "buckingsawmill"
SHAPE_DIR = MOD / "assets" / "buckingsawmill" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "buckingsawmill" / "config"
REFERENCE_OUT = MOD / "tests" / "rig-reference.json"   # the C# tests check RigParts against these poses
IW_SHAPE = "assets/immersivewoodworking/shapes/block/sawmill/sawmill.json"

# ---------------------------------------------------------------- placement (voxels)
CELLS_X, CELLS_Y, CELLS_Z = 6, 4, 3          # machine box: x 0..5, y 0..3, z 0..2 (blocks)
# Everything below is built and checked in this box's frame (the "build frame": voxels from its
# north-west-bottom corner). The shipped files are shifted so the controller is the middle cell of
# the east (output) end: build cell ORIGIN_CELL becomes [0,0,0], the cells x -5..0, z -1..1.
ORIGIN_CELL = (CELLS_X - 1, 0, CELLS_Z // 2)
INFEED_SIDE, OUTPUT_SIDE = "west", "east"    # trunks slide in at the west (axle) end, logs leave the east end
PORTAL_X = (0.0, 4.0)                        # the west portal's posts and head beam (x)
PORTAL_N_Z = (3.0, 7.0)                      # its north post, clear of the trunk's path (z 11..43)
STATION_X = (32.0, 64.0)                     # blade planes of the two saws
SHAFT_Y, SHAFT_Z = 56.0, 24.0                # shared shaft (= centre of the west face of cell [0,3,1])
POST_N = (7.0, 11.0)                         # z span of the north posts (the carriage slides between them)
TRUNK_Z = POST_N[1] + 16.0                   # centre line of the bed (a 2-wide trunk spans z 11..43)
BED_TOP = 8.0                                # trunk rests on the rails at this height
SAW_TOP = 41.0                               # blades' cutting edge at the top of the cycle (depth 0)
SAW_BOTTOM = 7.0                             # ... at the end of a cut (depth 1), below the bed top
SINK = SAW_TOP - SAW_BOTTOM                  # how far the carriage falls over a cut

# Stroke: crank -> rod -> yoke -> head pin. The rod pin sits on the yoke's top block. The throw is
# set by the room south of the trunk: the blade's tail must stay in its guide block (see SLOT_Z).
CRANK_THROW = 1.75                           # IW's is 3.5; gives a stroke of +-1.89
ROD_DX = 3.25                                # rod (and crank pin, yoke) plane east of the blade plane
ROD_PIN_Y = 49.25                            # rod's lower pin, on the yoke's top block
YOKE_DX = (2.0, 4.5)                         # yoke's x span east of the blade plane
YOKE_Y = (2.25, 50.0)                        # yoke's full height
YOKE_END = 1.75                              # height of its top and bottom blocks
YOKE_BAR = 1.2                               # thickness (z) of each side of the slot
PIN_D = 1.0                                  # slot width
PIN_FIT = 0.1                                # the pin is this much thinner than the slot (no coplanar faces)
GUIDE_GAP = 0.05                             # running clearance between the yoke and its guides

# Saw head (IW sash rail turned on its side) and blades. Head z at mid-stroke.
HEAD_Z = (3.0, 5.5)
HEAD_Y = (-0.5, 7.0)                         # relative to the blades' cutting edge
PIN_Z = (HEAD_Z[0] + HEAD_Z[1]) / 2
PIN_Y = 3.0                                  # head pin height above the cutting edge
BLADE_THICK = 0.4                            # one of IW's blades (0.2 thick), thickened to read at this scale
GUIDE_DX = (1.25, 2.0)                       # carriage guide bars either side of the head
GUIDE_Y = (5.5, 7.0)                         # ... relative to the cutting edge

# South-end support. Between a 2x2 trunk's south face (TRUNK_Z + 16) and the cell boundary (48)
# a guide block slides up and down a slim post. A slot in it runs along z in the blade plane, and
# the blade's tail piece strokes in that slot, so the blade is held at both ends. The tail must
# stay in the slot over the whole stroke, which is what limits the crank's throw.
SLOT_Z = (TRUNK_Z + 16.0 + 0.1, CELLS_Z * 16.0 - 0.1)   # the guide block's length (z)
TAIL_LEN = 0.8                               # tail piece's length along z
TAIL_Z = ((SLOT_Z[0] + SLOT_Z[1] - TAIL_LEN) / 2, (SLOT_Z[0] + SLOT_Z[1] + TAIL_LEN) / 2)   # mid-stroke
TAIL_DX = 0.4                                # tail piece's half thickness (x)
TAIL_Y = (-0.4, 1.9)                         # ... relative to the cutting edge (the blade is 0..1.5)
BLADE_Z = (4.5, TAIL_Z[1] - 0.2)             # blade root (clamped in the head) to its end in the tail, mid-stroke
SLOT_DX = 0.55                               # the slot's half width (x)
CHEEK = 0.5                                  # the guide block's wall thickness
STRAP = 0.7                                  # length (z) of each strap over the slot
SLIDER_Y = (-1.6, 3.1)                       # guide block's height, relative to the cutting edge
# The guide block has its own lift: a second spool beside each drum pays its rope out from its
# underside (the side that pays out as the drum turns to lower the saw), south across the
# machine under the crank stub to a sheave hung on the guide post's west face, then down onto
# the guide block. The post therefore stands just east of that rope, west of the blade plane.
TAIL_ROPE_DX = -4.3                          # x of the guide block's rope, relative to the blade plane
SPOOL_HALF = 0.75                            # spool core half-length (x)
GPOST_X = (-3.9, -0.9)                       # guide post, 3x3 (x, relative to the blade plane)
GPOST_Z = (44.5, 47.5)                       # centred on the south posts (z 44..48)
SOUTH_Z = (44.0, 48.0)                       # the south posts: the sill and head beam line up on them
SLEEVE_GAP = 0.05                            # running clearance between the guide block and its post
SILL_Y = (0.0, 3.0)                          # the sill the guide post stands on, between the south posts' feet
SHEAVE_T = 0.6                               # sheave thickness (x)
DROP_Z = 47.6                                # where the drop meets the guide block (its west sleeve wall)
DRUM_BEARING = 2.0                           # half-size of the drum-shaft bearing blocks (y, z)

# Posts and carriage: IW's post pair and sash, unturned, squeezed to the north side.
POST_Z_MAP = [(-100.0, POST_N[0] - 51.5), (3.0, POST_N[0]), (9.0, POST_N[1]), (100.0, POST_N[1] + 45.5)]
POST_X_MAP = [(-100.0, -107.5), (-2.0, -9.5), (0.0, -7.5), (16.0, 7.5), (18.0, 9.5), (100.0, 91.5)]
POST_CAP_MAP = [(-100.0, -100.0), (53.0, 53.0), (59.0, 60.0), (100.0, 101.0)]
SOUTH_POST_PIVOT_Z = (POST_N[0] + 48.0) / 2  # the south posts are the north pair turned 180 about y
# IW sash height -> carriage, relative to the cutting edge (rails below and above the blades)
SASH_Y_MAP = [(-100.0, -108.0), (5.0, -3.0), (7.5, -1.0), (27.5, 5.5), (30.0, 7.5), (44.0, 9.5), (100.0, 65.5)]
LUG_X = (-7.0, -6.0)                         # carriage's trip lug, relative to the blade plane
LUG_Y = (-2.5, -1.5)

# Windlass
GEAR_X = 12.0                                # crown disc's axis (along z, at SHAFT_Y)
IW_DISC_X, IW_GEAR_X, IW_MAIN_Z = -10.66, -10.73, 8.0   # IW's crown disc and small crown gear axes, main shaft z
IW_PINION_X = (-13.96, -7.46)                # IW's west and east pinion centres
IW_PEG_TIP_Z, IW_GEAR_TIP_Z = 2.19, 5.66     # IW's disc and small crown gear peg tips (z)
DISC_PEG_R = 6.05                            # radius of the crown disc's peg ring
PINION_PEG_R = 2.54                          # radius of IW's pinions' peg rings
MESH_DX = 0.68                               # IW puts a pinion's centre this far outside the crown's peg ring
MESH_DZ = 2.34                               # ... and the crown's peg tips this far short of the pinion's axis
PINION_FACE = 0.92                           # IW pinion's inner face from its centre (x)
GEAR_RATIO = DISC_PEG_R / PINION_PEG_R       # pinion turns per crown-disc turn
RAISE_TURNS = 6.0                            # shaft turns for a full raise (the gameplay's RaiseRevolutions default)
DRUM_Z = POST_N[1] + 0.25                               # drum shaft axis (y = SHAFT_Y, so it meshes like IW's pinions)
DRUM_SPIN = 2 * math.pi * RAISE_TURNS / GEAR_RATIO   # drum radians over a full sink or raise
DRUM_R = SINK / DRUM_SPIN                    # rope radius on the drum; the rope hangs at z = DRUM_Z - DRUM_R
DRUM_HALF = 1.5                              # drum half-length (x)
# The lift's clutch: a dog clutch on the crown axle (along z), between the crown disc and the
# small crown gear. Its sleeve is keyed to the disc's half of the axle and slides north onto a dog
# hub on the small crown gear's half.
DOG_GROOVE = 0.6                             # half-width of the dog clutch's groove (the fork rides in it)
DOG_LEN = 1.8                                # the dog clutch's length (z)
DOG_HUB_Z1 = 15.5                            # the dog hub's south face (z), on the small crown gear's side
DOG_THROW = 1.0                              # how far the fork slides the clutch north
CROWN_AXLE_N = 10.8                          # the small crown gear's half runs north to here, into its bearing
DOG_R = (1.75, 2.25)                         # the groove's and the sleeve's half-size (x, y)
BEARING_DRUM_X = (17.0, 20.5)                # the drum shaft's west bearing
WRAP = 0.2                                   # rope thickness / 2: the rope's centre runs DRUM_R from the axis

# Levers (station 1's west side). Points are (x, y) at z LEVER_Z.
LEVER_Z = (POST_N[0] - 1.5, POST_N[0] - 0.5)
TRIP_X = 19.5                                # trip rod centre line
TRIP_TRAVEL = 2.0                            # how far the carriage pushes it down
TAPPET_Y = (5.5, 6.5)
# the collar over the lug's path: its underside is where the lug's top is at the top of the cycle,
# so the lug lifts it (and the rod) over the last TRIP_TRAVEL of the rise
COLLAR_Y = (SAW_TOP + LUG_Y[1], SAW_TOP + LUG_Y[1] + 1.0)
# The rock shaft runs along x under the dog clutch, from a bearing on the input post to one on
# station 1's west post. Its tappet arm points north to the trip rod's top, its fork arm up into
# the clutch's groove, and an over-centre counterweight on an upright arm holds it either way.
ROCK_X = (0.5, 23.0)
FORK_Y = SHAFT_Y - DOG_R[0]                  # the groove's underside, where the fork rides

# Bed
RAIL_Z = ((TRUNK_Z - 7.5, TRUNK_Z - 4.5), (TRUNK_Z + 4.5, TRUNK_Z + 7.5))   # 3x3 rails under both 1x1 and 2x2 trunks
RAIL_GAP = 2.5                               # rails stop this far either side of a blade plane
SLEEPER_X = (10.0, 22.0, 42.0, 54.0, 74.0, 86.0)
TOP_BEAM_Y = (60.0, 63.5)                    # top beams and head beams: 4 wide, 3.5 deep
# Each station's posts are solid 4x4 timbers either side of the carriage (inner faces at sx -/+
# POST_IN). The north pair have a groove down the inner face in which the carriage's stile
# tongues run; the south pair are plain.
POST_W, POST_IN = 4.0, 7.5
CARR_Z = (8.0, 10.9)                         # the carriage's depth (z): 2.9, between the saw head and the trunk
TONGUE_Z, TONGUE_D = (8.35, 9.85), 1.5       # the stile tongues (z) and how far they reach into the posts (x)
GROOVE_GAP = 0.1                             # running clearance round a tongue in its groove
STILE_W = 2.0                                # the carriage's stiles (x)


def post_x(sx, side):
    """A station post's x span: side -1 west, +1 east."""
    return (sx - POST_IN - POST_W, sx - POST_IN) if side < 0 else (sx + POST_IN, sx + POST_IN + POST_W)


POST_X0 = STATION_X[0] - POST_IN - POST_W    # station 1's west posts' west face

TEXTURES = {"oak": "game:block/wood/debarked/oak", "metal": "game:block/metal/plate/iron",
            "rope": "game:item/resource/rope"}
TEX_SIZE = 64

# IW elements kept as a post pair: posts with their slats (011-018, 022-049), post tops and caps
# (084-089), feet (090, 091) and base sills (119-121, 251).
POST_KEEP = {f"Frame.{n:03d}" for n in [*range(11, 19), *range(22, 50), *range(84, 92), 119, 120, 121, 251]}
POST_SILLS = {"Frame.119", "Frame.120", "Frame.121", "Frame.251"}
# IW sash parts dropped from the carriage: the crosshead bars, the lever bracket.
SASH_DROP = re.compile(r"^sash_(05[4-9]|068|083|081\.\d+)$")
# IW sash rail parts that make the saw head: rail, clamp bars, rivets.
HEAD_KEEP = re.compile(r"^sash_(00[1-9]|01[0-9]|02[01])$")

# Shapes the rig expects (also used in the trunk check), blocks: (length along x, width, height)
TRUNK_SIZES = {"xs": (1, 1, 1), "sm": (2, 1, 1), "md": (3, 1, 1), "lg": (4, 1, 1), "xl": (4, 2, 2), "xxl": (5, 2, 2)}


# ---------------------------------------------------------------- small 3x3 linear algebra
def mmul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def mvec(m, v):
    return [m[i][0] * v[0] + m[i][1] * v[1] + m[i][2] * v[2] for i in range(3)]


IDENT = [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]


def rot(axis: str, deg: float):
    """Right-handed rotation about a principal axis (VS's Mat4f.RotateX/Y/Z)."""
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    if axis == "x":
        return [[1, 0, 0], [0, c, -s], [0, s, c]]
    if axis == "y":
        return [[c, 0, s], [0, 1, 0], [-s, 0, c]]
    return [[c, -s, 0], [s, c, 0], [0, 0, 1]]


def euler_xyz(r):
    """Angles (degrees) with r = Rx(a) * Ry(b) * Rz(c), the order VS applies rotationX/Y/Z in."""
    sb = max(-1.0, min(1.0, r[0][2]))
    b = math.asin(sb)
    if abs(math.cos(b)) > 1e-6:
        a = math.atan2(-r[1][2], r[2][2])
        c = math.atan2(-r[0][1], r[0][0])
    else:
        a, c = math.atan2(r[2][1], r[1][1]), 0.0
    return [math.degrees(a), math.degrees(b), math.degrees(c)]


def from_euler(a, b, c):
    return mmul(rot("x", a), mmul(rot("y", b), rot("z", c)))


# ---------------------------------------------------------------- flattened elements
class El:
    """A box of `size` (local axes), rotated by `r` about its centre `c` (world voxels)."""

    def __init__(self, name, size, c, r, faces, part):
        self.name, self.size, self.c, self.r, self.faces, self.part = name, list(size), list(c), r, faces, part

    def clone(self, name=None, part=None):
        return El(name or self.name, self.size, self.c, [row[:] for row in self.r],
                  copy.deepcopy(self.faces), part or self.part)

    def corners(self):
        out = []
        for i in range(8):
            local = [(self.size[k] / 2) * (1 if (i >> k) & 1 else -1) for k in range(3)]
            w = mvec(self.r, local)
            out.append([self.c[k] + w[k] for k in range(3)])
        return out

    def aabb(self):
        cs = self.corners()
        return [min(p[k] for p in cs) for k in range(3)], [max(p[k] for p in cs) for k in range(3)]

    def local_axis_for(self, axis: int):
        """Index of the local axis that lies along world `axis`, or None if the box is tilted."""
        for k in range(3):
            if abs(abs(self.r[axis][k]) - 1.0) < 1e-4:
                return k
        return None


def _mat4_local(e):
    o = e.get("rotationOrigin", [0, 0, 0])
    r = from_euler(e.get("rotationX", 0), e.get("rotationY", 0), e.get("rotationZ", 0))
    t = [o[i] - mvec(r, o)[i] for i in range(3)]
    return r, t


def flatten(elements, parent_r=IDENT, parent_t=(0.0, 0.0, 0.0)):
    """Bake VS's hierarchy (child coordinates are relative to the parent's `from`)."""
    out = []
    for e in elements:
        lr, lt = _mat4_local(e)
        r = mmul(parent_r, lr)
        t = [parent_t[i] + mvec(parent_r, lt)[i] for i in range(3)]
        f, to = e["from"], e["to"]
        mid = [(f[i] + to[i]) / 2 for i in range(3)]
        c = [mvec(r, mid)[i] + t[i] for i in range(3)]
        faces = {d: dict(face) for d, face in e.get("faces", {}).items()}
        for face in faces.values():
            if face.get("texture") == "#0":  # IW's blocktype maps "0" to debarked oak too
                face["texture"] = "#oak"
        out.append(El(e["name"], [to[i] - f[i] for i in range(3)], c, r, faces, None))
        if e.get("children"):
            ct = [t[i] + mvec(r, f)[i] for i in range(3)]
            out += flatten(e["children"], r, ct)
    return out


# ---------------------------------------------------------------- UVs
# Which local axis each face's u and v run along (before the face's own rotation).
_FACE_UV_AXES = {"north": (0, 1), "south": (0, 1), "east": (2, 1), "west": (2, 1), "up": (0, 2), "down": (0, 2)}


def scale_uv(el: El, k: int, ratio: float):
    """Crop (ratio < 1) or extend (ratio > 1, clamped to the texture) the UVs along local axis k."""
    for d, face in el.faces.items():
        if "uv" not in face:
            continue
        u_axis, v_axis = _FACE_UV_AXES[d]
        if face.get("rotation", 0) in (90, 270):
            u_axis, v_axis = v_axis, u_axis
        if k not in (u_axis, v_axis):
            continue
        uv = list(face["uv"])
        i0, i1 = (0, 2) if k == u_axis else (1, 3)
        span = uv[i1] - uv[i0]
        new = span * ratio
        if abs(new) > TEX_SIZE:
            new = math.copysign(TEX_SIZE, new)
        lo, hi = sorted((uv[i0], uv[i0] + new))
        shift = -lo if lo < 0 else (TEX_SIZE - hi if hi > TEX_SIZE else 0.0)
        uv[i0] += shift
        uv[i1] = uv[i0] + new
        face["uv"] = uv


# ---------------------------------------------------------------- transformations
def piecewise(points):
    """Continuous piecewise-linear map through (in, out) points."""
    def f(x):
        if x <= points[0][0]:
            return x - points[0][0] + points[0][1]
        for (x0, y0), (x1, y1) in zip(points, points[1:]):
            if x <= x1:
                return y0 + (x - x0) * (y1 - y0) / (x1 - x0)
        return x - points[-1][0] + points[-1][1]
    return f


def remap(els, axis: int, points):
    """Move every element's extent along world `axis` through a piecewise map: elements inside a
    stretched span grow (UVs extended), those beyond it move, tilted ones only move."""
    f = piecewise(points)
    for el in els:
        k = el.local_axis_for(axis)
        if k is None:
            el.c[axis] = f(el.c[axis])
            continue
        half = abs(el.size[k]) / 2
        lo, hi = f(el.c[axis] - half), f(el.c[axis] + half)
        if hi - lo < 1e-3 or half < 1e-6:
            el.c[axis] = f(el.c[axis])
            continue
        ratio = (hi - lo) / (2 * half)
        if abs(ratio - 1) > 1e-6:
            scale_uv(el, k, ratio)
            el.size[k] *= ratio
        el.c[axis] = (lo + hi) / 2
    return els


def translate(els, d):
    for el in els:
        el.c = [el.c[i] + d[i] for i in range(3)]
    return els


def rotate(els, axis: str, deg: float, origin):
    m = rot(axis, deg)
    for el in els:
        rel = [el.c[i] - origin[i] for i in range(3)]
        el.c = [origin[i] + mvec(m, rel)[i] for i in range(3)]
        el.r = mmul(m, el.r)
    return els


def rotate_matrix(els, m, origin):
    """Turn elements by an arbitrary rotation matrix about `origin`."""
    for el in els:
        rel = [el.c[i] - origin[i] for i in range(3)]
        el.c = [origin[i] + mvec(m, rel)[i] for i in range(3)]
        el.r = mmul(m, el.r)
    return els


def spread(els, axis: int, about: float, factor: float):
    """Scale element positions (not sizes) along an axis about a point."""
    for el in els:
        el.c[axis] = about + (el.c[axis] - about) * factor
    return els


def rename(els, prefix: str, part: str):
    for el in els:
        el.name, el.part = prefix + el.name, part
    return els


def from_template(tpl: El, lo, hi, name: str, part: str) -> El:
    """A new axis-aligned box spanning lo..hi with a (90-degree-rotated) template's faces,
    its UVs cropped or extended to the new size."""
    el = tpl.clone(name, part)
    for axis in range(3):
        k = el.local_axis_for(axis)
        if k is None:
            raise ValueError(f"template {tpl.name} is tilted")
        new = hi[axis] - lo[axis]
        if abs(el.size[k]) > 1e-9 and abs(new - abs(el.size[k])) > 1e-9:
            scale_uv(el, k, new / abs(el.size[k]))
        el.size[k] = new
        el.c[axis] = (lo[axis] + hi[axis]) / 2
    return el


def beam(tpl: El, lo, hi, name: str, part: str, seg: float = 16.0):
    """A long box split into segments of at most `seg` along its longest axis, so the template's
    UVs are cropped rather than stretched."""
    axis = max(range(3), key=lambda a: hi[a] - lo[a])
    n = max(1, math.ceil((hi[axis] - lo[axis]) / seg - 1e-9))
    out = []
    for i in range(n):
        a = lo[axis] + (hi[axis] - lo[axis]) * i / n
        b = lo[axis] + (hi[axis] - lo[axis]) * (i + 1) / n
        l2, h2 = list(lo), list(hi)
        l2[axis], h2[axis] = a, b
        out.append(from_template(tpl, l2, h2, f"{name}_{i + 1}" if n > 1 else name, part))
    return out


def strut(tpl: El, a, b, width: float, depth: float, name: str, part: str, axis: str = "x") -> El:
    """A bar from point a to point b in the plane normal to `axis` (a rod, an arm, a weight): built
    along the plane's first axis from the template, then turned about `axis`. `width` is its size
    in that plane, `depth` its size along `axis`."""
    ia = "xyz".index(axis)
    u, v = [(1, 2), (2, 0), (0, 1)][ia]          # in-plane axes, right-handed with `axis`
    du, dv = b[u] - a[u], b[v] - a[v]
    length = math.hypot(du, dv)
    mid = [(a[i] + b[i]) / 2 for i in range(3)]
    lo, hi = list(mid), list(mid)
    lo[u], hi[u] = mid[u] - length / 2, mid[u] + length / 2
    lo[v], hi[v] = mid[v] - width / 2, mid[v] + width / 2
    lo[ia], hi[ia] = mid[ia] - depth / 2, mid[ia] + depth / 2
    el = from_template(tpl, lo, hi, name, part)
    rotate([el], axis, math.degrees(math.atan2(dv, du)), mid)
    return el


# ---------------------------------------------------------------- building the machine
def load_iw():
    zips = sorted((ROOT / "build" / "mods").glob("immersivewoodworking_*.zip"))
    if not zips:
        sys.exit("no build/mods/immersivewoodworking_*.zip: run `python3 tools/packtool.py fetch` first")
    with zipfile.ZipFile(zips[-1]) as z:
        shape = json.loads(z.read(IW_SHAPE))
    return zips[-1].name, flatten(shape["elements"])


def pick(iw, pattern):
    rx = re.compile(pattern)
    return [el.clone() for el in iw if rx.match(el.name)]


def tpl(iw, name):
    return next(el for el in iw if el.name == name)


def aabb_of(els):
    boxes = [el.aabb() for el in els]
    return ([min(b[0][k] for b in boxes) for k in range(3)], [max(b[1][k] for b in boxes) for k in range(3)])


def build_posts(iw, n: int, sx: float):
    """Each station's four posts, solid timbers from the ground to the top beams. The north pair
    have a groove down the inner face, as deep as the carriage's tongues reach and a little wider,
    so a post is a back with two cheeks; the south pair are plain."""
    t = tpl(iw, "Frame.011")
    out = []
    top = TOP_BEAM_Y[0]
    gz0, gz1 = TONGUE_Z[0] - GROOVE_GAP, TONGUE_Z[1] + GROOVE_GAP
    depth = TONGUE_D + GROOVE_GAP
    for side, tag in ((-1, "w"), (1, "e")):
        x0, x1 = post_x(sx, side)
        inner = x1 if side < 0 else x0
        back = (x0, inner - depth) if side < 0 else (inner + depth, x1)
        cheek = (inner - depth, inner) if side < 0 else (inner, inner + depth)
        out += beam(t, [back[0], 0.0, POST_N[0]], [back[1], top, POST_N[1]], f"f{n}_post_n_{tag}_back", "frame")
        out += beam(t, [cheek[0], 0.0, POST_N[0]], [cheek[1], top, gz0], f"f{n}_post_n_{tag}_cheek_n", "frame")
        out += beam(t, [cheek[0], 0.0, gz1], [cheek[1], top, POST_N[1]], f"f{n}_post_n_{tag}_cheek_s", "frame")
        out += beam(t, [x0, 0.0, SOUTH_Z[0]], [x1, top, SOUTH_Z[1]], f"f{n}_post_s_{tag}", "frame")
    return out


def build_carriage(iw, n: int, sx: float):
    """The carriage: two stiles and two rails, 2.9 deep, framing an opening the blade passes
    through; each stile carries a tongue that runs in its post's groove. Two guide bars run north
    from the top rail to hang the saw head, and a lug on the west stile knocks the trip's tappet."""
    t_bar = tpl(iw, "sash_022")
    y0, y1 = SAW_TOP - 3.0, carriage_top()
    out = []
    for tag, side in (("w", -1), ("e", 1)):
        a, b = (sx - POST_IN, sx - POST_IN + STILE_W) if side < 0 else (sx + POST_IN - STILE_W, sx + POST_IN)
        out.append(from_template(t_bar, [a, y0, CARR_Z[0]], [b, y1, CARR_Z[1]], f"f{n}_carriage_stile_{tag}", f"f{n}_carriage"))
        ta, tb = (sx - POST_IN - TONGUE_D, sx - POST_IN) if side < 0 else (sx + POST_IN, sx + POST_IN + TONGUE_D)
        out.append(from_template(t_bar, [ta, y0, TONGUE_Z[0]], [tb, y1, TONGUE_Z[1]], f"f{n}_carriage_tongue_{tag}", f"f{n}_carriage"))
    rx0, rx1 = sx - POST_IN + STILE_W, sx + POST_IN - STILE_W
    out.append(from_template(t_bar, [rx0, y0, CARR_Z[0]], [rx1, SAW_TOP - 1.0, CARR_Z[1]], f"f{n}_carriage_rail_bottom", f"f{n}_carriage"))
    out.append(from_template(t_bar, [rx0, SAW_TOP + 5.5, CARR_Z[0]], [rx1, y1, CARR_Z[1]], f"f{n}_carriage_rail_top", f"f{n}_carriage"))
    north_face = CARR_Z[0]
    for side, (x0, x1) in (("w", (sx - GUIDE_DX[1], sx - GUIDE_DX[0])), ("e", (sx + GUIDE_DX[0], sx + GUIDE_DX[1]))):
        out.append(from_template(t_bar, [x0, SAW_TOP + GUIDE_Y[0], 0.25], [x1, SAW_TOP + GUIDE_Y[1], north_face + 0.25],
                                 f"f{n}_carriage_guide_{side}", f"f{n}_carriage"))
    out.append(from_template(t_bar, [sx + LUG_X[0], SAW_TOP + LUG_Y[0], LEVER_Z[0]], [sx + LUG_X[1], SAW_TOP + LUG_Y[1], north_face + 0.25],
                             f"f{n}_carriage_lug", f"f{n}_carriage"))
    return out


def build_saw(iw, n: int, sx: float):
    """The saw head: IW's sash bottom rail with its clamp bars and rivets, turned on its side
    (rail length -> up, rail height -> z, rail depth -> x), so the clamp bars grip the blades'
    faces; plus the pin that rides in the yoke's slot. The blade: the middle one of IW's three
    (the other two are its rip saw's), turned 90 degrees about x (normal x, long axis z, teeth
    down), thickened, and stretched from the head across the trunk to its tail piece."""
    head = pick(iw, HEAD_KEEP.pattern)
    # IW (x, y, z) -> (z, x, y): a proper rotation (cyclic permutation)
    perm = [[0, 0, 1], [1, 0, 0], [0, 1, 0]]
    rotate_matrix(head, perm, (0.0, 0.0, 0.0))
    # now x = IW z (rail depth 5..7), y = IW x (rail length 0..16), z = IW y (rail height 5..7.5)
    remap(head, 1, [(0.0, SAW_TOP + HEAD_Y[0]), (16.0, SAW_TOP + HEAD_Y[1])])
    remap(head, 2, [(5.0, HEAD_Z[0]), (7.5, HEAD_Z[1])])
    translate(head, (sx - 6.0, 0.0, 0.0))
    rename(head, f"f{n}_saw_", f"f{n}_saw")
    r = (PIN_D - PIN_FIT) / 2
    pin = from_template(tpl(iw, "sash_001"), [sx + 1.0, SAW_TOP + PIN_Y - r, PIN_Z - r],
                        [sx + YOKE_DX[1] - GUIDE_GAP, SAW_TOP + PIN_Y + r, PIN_Z + r], f"f{n}_saw_pin", f"f{n}_saw")

    # one blade: the middle one of IW's three (x = 8), with its teeth and end clips
    blades = [el for el in pick(iw, r"^saw") if abs(el.c[0] - 8.0) < 0.6]
    rotate(blades, "x", 90.0, (0.0, 0.0, 0.0))     # IW y (length) -> z, IW z (teeth +z) -> -y
    for el in blades:                              # thicken the plate and teeth (IW's are 0.2)
        k = el.local_axis_for(0)
        if abs(abs(el.size[k]) - 0.2) < 1e-6:
            scale_uv(el, k, BLADE_THICK / 0.2)
            el.size[k] = BLADE_THICK
    lo, hi = aabb_of(blades)
    remap(blades, 2, [(lo[2], BLADE_Z[0]), (hi[2], BLADE_Z[1])])
    lo, _ = aabb_of(blades)
    translate(blades, (sx - 8.0, SAW_TOP - lo[1], 0.0))
    rename(blades, f"f{n}_blade_", f"f{n}_blade")

    # the tail piece: a metal stirrup clamped over the blade's south end, riding in the guide
    # block's slot; with the head it holds the blade taut between its two ends
    t_metal = tpl(iw, "leveler_metal_static_003")
    tail = [from_template(t_metal, [sx - TAIL_DX, SAW_TOP + TAIL_Y[0], TAIL_Z[0]], [sx + TAIL_DX, SAW_TOP + TAIL_Y[1], TAIL_Z[1]],
                          f"f{n}_saw_tail", f"f{n}_saw")]
    return head + [pin] + blades + tail


def tail_rope_geometry():
    """Where the guide block's rope runs: from the spool's underside (y, z) south to the top of the
    sheave, round it, and down its south side onto the guide block's south sleeve wall."""
    y = SHAFT_Y - DRUM_R                                     # leaves the spool's underside, horizontally
    drop_z = DROP_Z
    cz = (GPOST_Z[0] + GPOST_Z[1]) / 2                       # sheave pin on the post's centre line
    r = drop_z - cz
    return {"y": y, "drop_z": drop_z, "cz": cz, "cy": y - r, "r": r}


def build_slider(iw, n: int, sx: float):
    """The guide block at the blade's south end, authored at depth 0: a sleeve round the guide
    post (west of the blade), a web from it to the slot along z in the blade plane in which the
    tail piece strokes. It hangs from its own rope (see build_tail_rope) and rides the
    carriage's sink, not the stroke."""
    t_bar = tpl(iw, "sash_022")
    y0, y1 = SAW_TOP + SLIDER_Y[0], SAW_TOP + SLIDER_Y[1]
    z0, z1 = SLOT_Z
    px0, px1 = sx + GPOST_X[0] - SLEEVE_GAP, sx + GPOST_X[1] + SLEEVE_GAP       # the sleeve's bore (x)
    pz0, pz1 = GPOST_Z[0] - SLEEVE_GAP, GPOST_Z[1] + SLEEVE_GAP
    ty0, ty1 = SAW_TOP + TAIL_Y[0] - 0.1, SAW_TOP + TAIL_Y[1] + 0.1            # the slot's height
    boxes = [
        ("sleeve_w", [sx + TAIL_ROPE_DX - 0.5, y0, z0], [px0, y1, z1]),   # the rope is tied to its top
        ("sleeve_n", [px0, y0, z0], [px1, y1, pz0]),
        ("sleeve_s", [px0, y0, pz1], [px1, y1, z1]),
        ("web", [px1, y0, z0], [sx - SLOT_DX, y1, z1]),                      # the sleeve's east wall and the slot's west cheek
        ("cheek_e", [sx + SLOT_DX, y0, z0], [sx + SLOT_DX + CHEEK, y1, z1]),
        # the top of the slot is two straps with a window between them, so the tail shows
        ("strap_n", [sx - SLOT_DX, ty1, z0], [sx + SLOT_DX, y1, z0 + STRAP]),
        ("strap_s", [sx - SLOT_DX, ty1, z1 - STRAP], [sx + SLOT_DX, y1, z1]),
        ("bridge_foot", [sx - SLOT_DX, y0, z0], [sx + SLOT_DX, ty0, z1]),
        # the iron eye its rope is tied to, on top of the west sleeve wall
        ("eye", [sx + TAIL_ROPE_DX - 0.4, y1, DROP_Z - 0.3], [sx + TAIL_ROPE_DX + 0.4, y1 + EYE_H, DROP_Z + 0.3]),
    ]
    t_metal = tpl(iw, "sash_001")
    return [from_template(t_metal if name == "eye" else t_bar, lo, hi, f"f{n}_slider_{name}", f"f{n}_slider") for name, lo, hi in boxes]


def build_tail_post(iw, n: int, sx: float):
    """The guide post the guide block slides on, squared into the frame like IW's own posts: it
    stands centred on a sill laid between the feet of the station's two south posts, and runs up
    into the underside of a head beam laid between the station's two top beams, centred on it.
    The sheave hangs on its west face: an iron pin from the post, an iron strap outside the
    sheave carrying the pin's outer end, and an iron cap from the strap back to the post over the
    sheave (above the rope)."""
    t_post, t_beam, t_metal = tpl(iw, "Frame.011"), tpl(iw, "Frame.119"), tpl(iw, "sash_001")
    x0, x1 = sx + GPOST_X[0], sx + GPOST_X[1]
    z0, z1 = GPOST_Z
    top = TOP_BEAM_Y[0]
    g = tail_rope_geometry()
    out = beam(t_post, [x0, SILL_Y[1], z0], [x1, top, z1], f"f{n}_tailpost", "frame")
    out += beam(t_beam, [sx - 7.5, SILL_Y[0], SOUTH_Z[0]], [sx + 7.5, SILL_Y[1], SOUTH_Z[1]], f"f{n}_tailpost_sill", "frame")
    out += beam(t_beam, [sx - 7.5, top, SOUTH_Z[0]], [sx + 7.5, TOP_BEAM_Y[1], SOUTH_Z[1]], f"f{n}_tailpost_head", "frame")
    rx, h = sx + TAIL_ROPE_DX, SHEAVE_T / 2
    so0, so1 = rx - h - 0.4, rx - h - 0.1                    # the outer strap (x), 0.1 off the sheave
    cap = g["y"] + 0.5                                       # the cap's underside, above the rope
    out.append(from_template(t_metal, [so0, g["cy"] - 0.3, g["cz"] - 0.3], [x0 + 0.5, g["cy"] + 0.3, g["cz"] + 0.3], f"f{n}_tailpost_pin", "frame"))
    out.append(from_template(t_metal, [so0, g["cy"] - 0.6, g["cz"] - 0.4], [so1, cap + 0.5, g["cz"] + 0.4], f"f{n}_tailpost_strap", "frame"))
    out.append(from_template(t_metal, [so0, cap, g["cz"] - 0.4], [x0 + 0.5, cap + 0.5, g["cz"] + 0.4], f"f{n}_tailpost_cap", "frame"))
    return out


def build_tail_rope(iw, n: int, sx: float):
    """The guide block's lift: a plain spool on the drum shaft beside the drum (the same rope
    radius, so it pays out at the drum's rate), the rope's run south from the spool's underside to
    the top of the sheave (fixed: a rope sliding along itself shows no motion), the sheave (a
    plain grooved pulley), and the drop from the sheave down to the guide block (authored at
    depth 0; it stretches with depth like the carriage's rope)."""
    t_rope = tpl(iw, "spring_002")
    t_flange = tpl(iw, "MainRotor_twoway_021")
    g = tail_rope_geometry()
    rx = sx + TAIL_ROPE_DX
    out = spool(t_rope, t_flange, rx, SPOOL_HALF, f"f{n}_spool", f"f{n}_spool")
    out.append(from_template(t_rope, [rx - WRAP, g["y"] - WRAP, DRUM_Z], [rx + WRAP, g["y"] + WRAP, g["cz"]], f"f{n}_tailrope_run", f"f{n}_tailrun"))
    eye = SAW_TOP + SLIDER_Y[1] + EYE_H
    out.append(from_template(t_rope, [rx - WRAP, eye, g["drop_z"] - WRAP], [rx + WRAP, g["cy"], g["drop_z"] + WRAP],
                             f"f{n}_tailrope_drop", f"f{n}_taildrop"))
    h = SHEAVE_T / 2
    out += octagon(t_flange, rx - h, rx - h + 0.15, g["cy"], g["cz"], g["r"] + 0.15, f"f{n}_sheave_flange_w", f"f{n}_sheave")
    out += octagon(t_flange, rx - h + 0.15, rx + h - 0.15, g["cy"], g["cz"], g["r"] - WRAP, f"f{n}_sheave_core", f"f{n}_sheave")
    out += octagon(t_flange, rx + h - 0.15, rx + h, g["cy"], g["cz"], g["r"] + 0.15, f"f{n}_sheave_flange_e", f"f{n}_sheave")
    return out


def build_yoke(iw, n: int, sx: float):
    """The slotted crosshead: two bars with the head pin between them, joined by end blocks;
    authored at mid-stroke."""
    t_bar = tpl(iw, "sash_022")
    x0, x1 = sx + YOKE_DX[0], sx + YOKE_DX[1]
    z0, z1 = PIN_Z - PIN_D / 2 - YOKE_BAR, PIN_Z + PIN_D / 2 + YOKE_BAR
    y0, y1 = YOKE_Y
    out = []
    out += beam(t_bar, [x0, y0 + YOKE_END, z0], [x1, y1 - YOKE_END, z0 + YOKE_BAR], f"f{n}_yoke_bar_n", "")
    out += beam(t_bar, [x0, y0 + YOKE_END, z1 - YOKE_BAR], [x1, y1 - YOKE_END, z1], f"f{n}_yoke_bar_s", "")
    out.append(from_template(t_bar, [x0, y0, z0], [x1, y0 + YOKE_END, z1], f"f{n}_yoke_foot", ""))
    out.append(from_template(t_bar, [x0, y1 - YOKE_END, z0], [x1, y1, z1], f"f{n}_yoke_head", ""))
    for el in out:
        el.part = f"f{n}_yoke"
    return out


def build_crank(iw, n: int, sx: float, phase_deg: float):
    """IW's crank (stub, two webs, pin; its flange disc left off), throw shortened, pin centred on
    the rod plane, turned so the pin points up (station 1) or down (station 2) at theta = 0."""
    crank = pick(iw, r"^Rotor_default_4_00[1-5]$")
    remap([el for el in crank if el.name in ("Rotor_default_4_002", "Rotor_default_4_003", "Rotor_default_4_004")],
          1, [(52.0, 56.0 - CRANK_THROW - 0.5), (53.01, 56.0 - CRANK_THROW + 0.5), (56.5, 56.5)])
    translate(crank, (sx + ROD_DX - 8.0, SHAFT_Y - 56.0, SHAFT_Z - 6.0))
    rotate(crank, "x", phase_deg, (0.0, SHAFT_Y, SHAFT_Z))
    rename(crank, f"f{n}_crank_", "shaft")
    return crank


def build_rod(iw, n: int, sx: float, geo):
    """The connecting rod from the yoke's head pin up to the crank pin, authored at its mean angle
    (the swing driver rocks it about that)."""
    t_rod = tpl(iw, "Frame.119")
    a = [sx + ROD_DX, ROD_PIN_Y, PIN_Z]
    psi = geo["psi0"]
    b = [a[0], a[1] - geo["length"] * math.sin(psi), a[2] + geo["length"] * math.cos(psi)]
    rod = strut(t_rod, a, b, 1.25, 1.5, f"f{n}_rod_bar", f"f{n}_rod")
    t_eye = tpl(iw, "sash_001")
    eyes = [from_template(t_eye, [p[0] - 0.6, p[1] - 0.7, p[2] - 0.7], [p[0] + 0.6, p[1] + 0.7, p[2] + 0.7], f"f{n}_rod_eye{i}", f"f{n}_rod")
            for i, p in enumerate((a, b), 1)]
    return [rod] + eyes


def octagon(tpl_el, x0, x1, cy, cz, apothem, name, part):
    """A plain octagonal disc or drum along x: four strips of the template, each as long as the
    octagon is across and as wide as one of its sides, turned 0, 45, 90 and 135 degrees about x.
    Their union is exactly the regular octagon, so nothing sticks out like a tooth."""
    half_side = apothem * math.tan(math.pi / 8)
    out = []
    for i in range(4):
        el = from_template(tpl_el, [x0, cy - half_side, cz - apothem], [x1, cy + half_side, cz + apothem], f"{name}_{i + 1}", part)
        if i:
            rotate([el], "x", 45.0 * i, (0.0, cy, cz))
        out.append(el)
    return out


def build_rope_and_drum(iw, n: int, sx: float):
    """One drum per station on the drum shaft, a plain spool: a core of wound rope between two
    oak flanges, all octagonal; and the rope hanging from it to the carriage's top rail
    (authored at depth 0)."""
    t_rope = tpl(iw, "spring_002")
    t_flange = tpl(iw, "MainRotor_twoway_021")
    out = spool(t_rope, t_flange, sx, DRUM_HALF, f"drum{n}", "drum")
    # the rope leaves the wrap tangentially on the north side, plumb down to an iron eye on the
    # carriage's top rail
    rope_z = DRUM_Z - DRUM_R
    eye = carriage_top() + EYE_H
    out.append(from_template(tpl(iw, "sash_001"), [sx - 0.4, carriage_top(), rope_z - 0.4], [sx + 0.4, eye, rope_z + 0.4], f"f{n}_carriage_eye", f"f{n}_carriage"))
    out.append(from_template(t_rope, [sx - WRAP, eye, rope_z - WRAP], [sx + WRAP, SHAFT_Y, rope_z + WRAP], f"f{n}_rope", f"f{n}_rope"))
    return out


EYE_H = 0.6                                  # height of the iron eyes the ropes are tied to


def spool(t_rope, t_flange, cx, half, name, part):
    """A plain spool on the drum shaft: a wooden core, a wrap of rope on it (a rope-textured
    octagon just inside the rope's centre radius, so a rope leaving it at DRUM_R lies on it) and
    two oak flanges."""
    out = octagon(t_flange, cx - half, cx + half, SHAFT_Y, DRUM_Z, DRUM_R - 2 * WRAP - 0.05, f"{name}_core", part)
    out += octagon(t_rope, cx - half + 0.05, cx + half - 0.05, SHAFT_Y, DRUM_Z, DRUM_R - WRAP, f"{name}_wrap", part)
    for side, x in (("w", cx - half - 0.25), ("e", cx + half + 0.25)):
        out += octagon(t_flange, x - 0.25, x + 0.25, SHAFT_Y, DRUM_Z, DRUM_R + 0.75, f"{name}_flange_{side}", part)
    return out


def carriage_top():
    """Top of the carriage's top rail at depth 0 (where the rope is tied)."""
    return SAW_TOP + 7.5


def build_gearbox(iw):
    """The reversing gear and the drive to the drum shaft, from IW's gear set. IW's crown disc
    turns about z north of the shaft, its peg ring meshing both of IW's loose pinions on the
    shaft (placed as IW places its pinions against its small crown gear, but on the disc's
    larger ring), so the pinions always turn opposite ways. The disc's axle runs north to IW's
    small crown gear, turned half round, which meshes a pinion (a copy of IW's east pinion) on
    the drum shaft. IW's main rotor (a shaft section with toothed flanges) is left off: the
    shared shaft runs through instead. No toothed wheel here meshes nothing."""
    disc_dz = SHAFT_Z - MESH_DZ - IW_PEG_TIP_Z
    disc = translate(pick(iw, r"^Rotor_default_3_(01[1-4]|01[7-9]|02[0-7])$"), (GEAR_X - IW_DISC_X, 0.0, disc_dz))
    pin_w = translate(pick(iw, r"^Rotor_default_1_"), (GEAR_X - DISC_PEG_R - MESH_DX - IW_PINION_X[0], 0.0, SHAFT_Z - IW_MAIN_Z))
    pin_e = translate(pick(iw, r"^Rotor_default_2_"), (GEAR_X + DISC_PEG_R + MESH_DX - IW_PINION_X[1], 0.0, SHAFT_Z - IW_MAIN_Z))
    # the small crown gear, pegs turned to face north onto the drum pinion
    crown2 = translate(pick(iw, r"^Rotor_default_3_(00[1-9]|010)$"), (GEAR_X - IW_GEAR_X, 0.0, 0.0))
    pivot_z = (DRUM_Z + MESH_DZ + IW_GEAR_TIP_Z) / 2
    rotate(crown2, "y", 180.0, (GEAR_X, SHAFT_Y, pivot_z))
    pin_d = translate(pick(iw, r"^Rotor_default_2_"), (GEAR_X - IW_GEAR_X, 0.0, DRUM_Z - IW_MAIN_Z))
    # the disc's axle (IW's, two crossed bars), in two halves meeting at the dog hub: the small
    # crown gear's half (with the hub) and the disc's half (on which the dog clutch slides)
    axle_b, axle_d = [], []
    _, c2_hi = aabb_of(crown2)
    d_lo, _ = aabb_of(disc)
    for name in ("Rotor_default_3_015", "Rotor_default_3_016"):
        t = translate([tpl(iw, name).clone()], (GEAR_X - IW_DISC_X, 0.0, 0.0))[0]
        lo, hi = t.aabb()
        axle_b += beam(t, [lo[0], lo[1], CROWN_AXLE_N], [hi[0], hi[1], DOG_HUB_Z1 - 0.05], f"axle_{name[-3:]}", "", seg=10.0)
        axle_d += beam(t, [lo[0], lo[1], DOG_HUB_Z1 + 0.05], [hi[0], hi[1], d_lo[2] + 0.5], f"axle_{name[-3:]}", "", seg=10.0)
    hub = [from_template(tpl(iw, "MainRotor_twoway_021"), [GEAR_X - DOG_R[0], SHAFT_Y - DOG_R[0], c2_hi[2] - 0.1],
                         [GEAR_X + DOG_R[0], SHAFT_Y + DOG_R[0], DOG_HUB_Z1], "hub", "")]
    rename(pin_w, "gear_pinion_w_", "pinion_w")
    rename(pin_e, "gear_pinion_e_", "pinion_e")
    rename(crown2, "gear_crown_b_", "crown_b")
    # an iron pilot spigot on the hub's face, running on into the disc's half: it carries the
    # inner ends of both halves, each of which has one bearing of its own
    hub.append(from_template(tpl(iw, "sash_001"), [GEAR_X - 0.4, SHAFT_Y - 0.4, DOG_HUB_Z1], [GEAR_X + 0.4, SHAFT_Y + 0.4, DOG_HUB_Z1 + 1.0], "pilot", ""))
    rename(axle_b + hub, "gear_crownb_", "crown_b")
    rename(disc, "gear_crown_disc_", "crown")
    rename(axle_d, "gear_crown_", "crown")
    rename(pin_d, "drum_pinion_", "drum")
    # one-way catches: a small iron pawl on each loose pinion's outer hub face, opposite hands,
    # so whichever way the shaft turns one pinion is carried with it and the other idles
    t_metal = tpl(iw, "sash_001")
    catches = []
    for side, x, hand in (("w", aabb_of(pin_w)[0][0] - 0.2, 1.0), ("e", aabb_of(pin_e)[1][0] + 0.2, -1.0)):
        catches.append(strut(t_metal, [x, SHAFT_Y + 1.55, SHAFT_Z - 0.3 * hand], [x, SHAFT_Y + 2.3, SHAFT_Z + 0.5 * hand], 0.35, 0.4,
                             f"gear_pinion_{side}_catch", f"pinion_{side}", axis="x"))
    # fixed collars on the main shaft either side of each loose pinion, so they cannot slide
    t_collar = tpl(iw, "MainRotor_twoway_021")
    collars = []
    for tag, (a, b) in (("w1", (aabb_of(pin_w)[0][0] - 0.85, aabb_of(pin_w)[0][0] - 0.45)), ("w2", (aabb_of(pin_w)[1][0] + 0.05, aabb_of(pin_w)[1][0] + 0.45)),
                        ("e1", (aabb_of(pin_e)[0][0] - 0.45, aabb_of(pin_e)[0][0] - 0.05)), ("e2", (aabb_of(pin_e)[1][0] + 0.45, aabb_of(pin_e)[1][0] + 0.85))):
        collars.append(from_template(t_collar, [a, SHAFT_Y - 2.0, SHAFT_Z - 2.0], [b, SHAFT_Y + 2.0, SHAFT_Z + 2.0], f"shaft_collar_{tag}", "shaft"))
    return pin_w + pin_e + catches + collars + disc + axle_d + axle_b + hub + crown2 + pin_d + build_dog(iw)


def build_dog(iw):
    """The dog clutch on the crown axle: a sleeve keyed to the disc's half of the axle (so it
    always turns with the disc), a groove in its middle for the fork, and two iron dogs on its
    north face. Slid north by the fork it locks the disc to the small crown gear's half, which
    drives the drum pinion and winds the saws up."""
    t, t_metal = tpl(iw, "MainRotor_twoway_021"), tpl(iw, "sash_001")
    z0, z1, gz = DOG_Z0, DOG_Z0 + DOG_LEN, DOG_GZ
    g, r = DOG_R
    out = [from_template(t, [GEAR_X - g, SHAFT_Y - g, gz - DOG_GROOVE], [GEAR_X + g, SHAFT_Y + g, gz + DOG_GROOVE], "dog_hub", "dog")]
    for side, (a, b) in (("n", (z0, gz - DOG_GROOVE)), ("s", (gz + DOG_GROOVE, z1))):
        out.append(from_template(t, [GEAR_X - r, SHAFT_Y - r, a], [GEAR_X + r, SHAFT_Y + r, b], f"dog_sleeve_{side}", "dog"))
    for i, dx in enumerate((1.4, -1.4), 1):
        out.append(from_template(t_metal, [GEAR_X + dx - 0.35, SHAFT_Y - 0.35, z0 - 0.2], [GEAR_X + dx + 0.35, SHAFT_Y + 0.35, z0], f"dog_tooth{i}", "dog"))  # into the hub's sockets
    return out


def build_shafts(iw, occupied_main, occupied_drum, end_x):
    """The shared shaft and the drum shaft (IW's cross-shaped main-rotor profile), filling the
    gaps between the parts that sit on them."""
    tpl_a, tpl_b = tpl(iw, "MainRotor_twoway_001"), tpl(iw, "MainRotor_twoway_002")
    out = []

    def fill(occupied, start, end, axis_y, axis_z, prefix, part):
        spans, x = [], start
        for a, b in sorted(occupied):
            if a - x > 0.05:
                spans.append((x, a + 0.25))
            x = max(x, b - 0.25)
        if end - x > 0.05:
            spans.append((x, end))
        for i, (a, b) in enumerate(spans):
            for t, tag in ((tpl_a, "a"), (tpl_b, "b")):
                lo, hi = t.aabb()
                hy, hz = (hi[1] - lo[1]) / 2, (hi[2] - lo[2]) / 2
                out.extend(beam(t, [a, axis_y - hy, axis_z - hz], [b, axis_y + hy, axis_z + hz], f"{prefix}_{i + 1}{tag}", part, seg=10.0))

    # the main shaft (IW's cross profile, matching the vanilla axle that feeds it) runs only from
    # the input to the first crank; from there on the crank bar is the shaft (see build_crankbar)
    fill([], 0.0, min(a for a, _ in occupied_main) + 0.25, SHAFT_Y, SHAFT_Z, "shaft", "shaft")
    fill(occupied_drum, min(a for a, _ in occupied_drum), end_x, SHAFT_Y, DRUM_Z, "drum_shaft", "drum")
    return out


def build_crankbar(iw, crank_spans):
    """The crank bar between the two cranks: IW's crank stub profile carried on from crank 1's
    east stub to crank 2's west stub, so the two cranks read as one continuous crankshaft."""
    t = tpl(iw, "Rotor_default_4_005")
    r = abs(t.size[t.local_axis_for(1)]) / 2
    return [from_template(t, [crank_spans[0][1], SHAFT_Y - r, SHAFT_Z - r], [crank_spans[1][0], SHAFT_Y + r, SHAFT_Z + r],
                          "shaft_crankbar", "shaft")]


def metal(el: El):
    """Make an element iron: every face takes the `metal` texture, its UVs a region of the 64x64
    plate texture in proportion to the face's size (4 texels per voxel), unrotated."""
    for d, face in el.faces.items():
        u, v = _FACE_UV_AXES[d]
        w = min(abs(el.size[u]) * TEX_SIZE / 16, TEX_SIZE)
        h = min(abs(el.size[v]) * TEX_SIZE / 16, TEX_SIZE)
        el.faces[d] = {"texture": "#metal", "uv": [0.0, 0.0, w, h]}
    return el


# Elements that are iron: pins, wearing surfaces and thin linkage (everything else keeps its
# template's texture: wood for structure, for the shafts that continue the vanilla axle, for
# drums, gears, carriages, saw heads and bearings).
METAL_NAMES = re.compile(r"^(bearing_crown_s|f\d_carriage_(guide_|lug)|gear_crownb_pilot|f\d_crank_|shaft_crankbar|f\d_rod_eye|f\d_saw_pin|f\d_saw_tail|dog_|gear_pinion_._catch|lever_|f\d_slider_(strap_|eye)|f\d_carriage_eye|f\d_tailpost_(pin|strap|cap))")


def build_levers(iw):
    """The trip rod (a pushrod with a tappet under the carriage's lug and a collar over it) and the
    rock shaft that works the dog clutch on the crown axle."""
    t_oak, t_metal = tpl(iw, "sash_022"), tpl(iw, "leveler_metal_static_003")
    z0, z1 = LEVER_Z
    out = []
    # trip rod (a plain pushrod) with its tappet under the carriage's lug, up station 1's west post
    ry, rz = ROCK_PIVOT
    out.append(from_template(t_metal, [TRIP_X - 0.5, TAPPET_Y[1], z0], [TRIP_X + 0.5, ry, z1], "lever_trip_rod", "trip"))
    out.append(from_template(t_metal, [TRIP_X - 0.5, TAPPET_Y[0], z0], [STATION_X[0] + LUG_X[1] + 0.25, TAPPET_Y[1], z1], "lever_trip_tappet", "trip"))
    # the collar over the lug's path: at the top of the rise the lug lifts it, throwing the clutch out
    out.append(from_template(t_metal, [TRIP_X - 0.5, COLLAR_Y[0], z0], [STATION_X[0] + LUG_X[1] + 0.25, COLLAR_Y[1], z1], "lever_trip_collar", "trip"))
    # the rock shaft along x, its tappet arm north to the trip rod's top, its over-centre weight on
    # an upright arm, and its fork arm up into the dog clutch's groove
    out.append(from_template(t_metal, [ROCK_X[0], ry - 0.5, rz - 0.5], [ROCK_X[1], ry + 0.5, rz + 0.5], "lever_rock_shaft", "rock"))
    out.append(from_template(t_metal, [TRIP_X - 0.5, ry - 0.5, z0], [TRIP_X + 0.5, ry + 0.5, rz + 0.5], "lever_rock_tappet_arm", "rock"))
    (wy, wz), (ty, tz) = weight_centre(), weight_centre(WEIGHT_ARM + WEIGHT_SIZE / 2)
    out.append(strut(t_metal, [TRIP_X, ry, rz], [TRIP_X, ty, tz], 0.8, 0.8, "lever_rock_weight_arm", "rock"))
    out.append(strut(t_metal, [TRIP_X, *weight_centre(WEIGHT_ARM - WEIGHT_SIZE / 2)], [TRIP_X, ty, tz], WEIGHT_SIZE, WEIGHT_SIZE, "lever_rock_weight", "rock"))
    out.append(from_template(t_metal, [GEAR_X - 0.4, ry, rz - 0.25], [GEAR_X + 0.4, FORK_Y - 0.9, rz + 0.25], "lever_rock_fork_arm", "rock"))
    out.append(from_template(t_metal, [GEAR_X - 1.0, FORK_Y - 0.9, rz - 0.45], [GEAR_X + 1.0, FORK_Y - 0.05, rz + 0.45], "lever_rock_fork", "rock"))
    return out


def build_frame(iw):
    """Top beams, shaft bearings, the input end, the west posts, the yoke guides, trip-rod straps,
    the rock shaft's bearings and the bed. Every piece is fixed to the rest of the frame (a face shared
    or overlapping), which `validate` checks."""
    t_beam, t_post, t_block = tpl(iw, "Frame.119"), tpl(iw, "Frame.011"), tpl(iw, "Frame.088")
    t_bar = tpl(iw, "sash_022")
    out = []
    crank_r = 0.5                                       # half the crank bar's section
    for n, sx in enumerate(STATION_X, 1):
        for side, (x0, x1) in (("w", post_x(sx, -1)), ("e", post_x(sx, 1))):
            out += beam(t_beam, [x0, TOP_BEAM_Y[0], POST_N[0]], [x1, TOP_BEAM_Y[1], 48.0], f"top_beam{n}{side}", "frame")
            # main-shaft bearing hanging from the top beam: around IW's thick shaft at station 1's
            # west post, around the thinner crank bar everywhere else
            h = 2.0 if (n, side) == (1, "w") else crank_r + 1.0
            out.append(from_template(t_block, [x0, SHAFT_Y - h, SHAFT_Z - h], [x1, TOP_BEAM_Y[0], SHAFT_Z + h], f"bearing{n}{side}", "frame"))
            # drum-shaft bearing: built into the post's cap, under the top beam, round the drum shaft
            out.append(from_template(t_block, [x0 + 0.25, SHAFT_Y - DRUM_BEARING, DRUM_Z - DRUM_BEARING],
                                     [x1 - 0.25, TOP_BEAM_Y[0], DRUM_Z + DRUM_BEARING], f"bearing_drum{n}{side}", "frame"))
        # yoke guides on the east post: top and bottom, along z, with a bracket to the post
        gx0, gx1 = sx + YOKE_DX[1] + GUIDE_GAP, sx + YOKE_DX[1] + 2.0
        for tag, (y0, y1) in (("top", (YOKE_Y[1] - YOKE_END, YOKE_Y[1])), ("foot", (YOKE_Y[0], YOKE_Y[0] + YOKE_END))):
            out.append(from_template(t_bar, [gx0, y0, 0.0], [gx1, y1, POST_N[0] + 1.0], f"f{n}_guide_{tag}", "frame"))
            out.append(from_template(t_bar, [gx1, y0, POST_N[0] - 1.0], [sx + 7.5, y1, POST_N[0] + 0.5], f"f{n}_guide_{tag}_bracket", "frame"))
    # trip-rod straps on station 1's west post, lapped onto the post's west face
    for i, y in enumerate((18.0, 32.0, ROCK_PIVOT[0] - 4.0), 1):
        out.append(from_template(tpl(iw, "leveler_metal_static_003"), [TRIP_X - 0.75, y, LEVER_Z[1]], [POST_X0, y + 1.0, POST_N[0] + 0.5],
                                 f"trip_strap{i}", "frame"))
    # the west portal, which the trunk slides in through: a post either side of the bed, clear of
    # the trunk's path, and a head beam across them at the top beams' height. The input bearing
    # hangs from the head beam round the shaft, high above the trunk, with the axle's face flush
    # with the portal's west face; the west head beam runs east from the portal's beam.
    px_0, px_1 = PORTAL_X
    out += beam(t_post, [px_0, 0.0, PORTAL_N_Z[0]], [px_1, TOP_BEAM_Y[0], PORTAL_N_Z[1]], "portal_post_n", "frame")
    out += beam(t_post, [px_0, 0.0, SOUTH_Z[0]], [px_1, TOP_BEAM_Y[0], SOUTH_Z[1]], "portal_post_s", "frame")
    out += beam(t_beam, [px_0, TOP_BEAM_Y[0], PORTAL_N_Z[0]], [px_1, TOP_BEAM_Y[1], SOUTH_Z[1]], "portal_head_beam", "frame", seg=12.0)
    out.append(from_template(t_block, [0.0, SHAFT_Y - 2.0, SHAFT_Z - 2.0], [3.5, TOP_BEAM_Y[0], SHAFT_Z + 2.0], "input_bearing", "frame"))
    wz0, wz1 = DRUM_Z - 2.0, DRUM_Z + 2.0
    out += beam(t_beam, [px_1, TOP_BEAM_Y[0], wz0], [POST_X0, TOP_BEAM_Y[1], wz1], "west_head_beam", "frame")
    # drum-shaft bearing at the west end, beside the drum pinion, hanging from the head beam
    out.append(from_template(t_block, [BEARING_DRUM_X[0], SHAFT_Y - DRUM_BEARING, DRUM_Z - DRUM_BEARING], [BEARING_DRUM_X[1], TOP_BEAM_Y[0], DRUM_Z + DRUM_BEARING],
                             "bearing_drum", "frame"))
    # the rock shaft's bearings: a block round each end, the west one hanging from the portal's
    # head beam, the east one on an arm from station 1's west post
    ry, rz = ROCK_PIVOT
    out.append(from_template(t_block, [0.0, ry - 1.5, rz - 1.5], [3.5, ry + 1.5, rz + 1.5], "rock_bearing_w", "frame"))
    out.append(from_template(t_block, [0.0, ry + 1.5, rz - 1.25], [3.5, TOP_BEAM_Y[0], rz + 1.25], "rock_hanger_w", "frame"))
    px0 = POST_X0
    out.append(from_template(t_block, [px0, ry - 1.5, POST_N[1]], [px0 + 3.0, ry + 1.5, rz + 1.5], "rock_bearing_e", "frame"))
    # the crown axle's bearings: the small crown gear's half in a block hanging from the west head
    # beam, north of the gear; the disc's half in a block between the dog clutch and the disc, on a
    # rail from a hanger under station 1's west top beam
    cb = (GEAR_X - 1.75, SHAFT_Y - 1.75)
    out.append(from_template(t_block, [cb[0], cb[1], CROWN_AXLE_N - 0.9], [GEAR_X + 1.75, TOP_BEAM_Y[0], CROWN_AXLE_N + 2.1], "bearing_crown_n", "frame"))
    dz0, dz1 = DOG_Z0 + DOG_LEN + 0.1, CROWN_DISC_Z0 - 0.1
    out.append(from_template(t_block, [cb[0], cb[1], dz0], [GEAR_X + 1.75, SHAFT_Y + 1.75, dz1], "bearing_crown_s", "frame"))
    out.append(from_template(t_beam, [GEAR_X + 1.75, SHAFT_Y + 1.75 - 1.2, dz0], [POST_X0, SHAFT_Y + 1.75, dz1], "bearing_crown_s_rail", "frame"))
    out.append(from_template(t_block, [POST_X0, SHAFT_Y + 1.75 - 1.2, dz0], [POST_X0 + 2.0, TOP_BEAM_Y[0], dz1], "bearing_crown_s_hanger", "frame"))
    out += build_bed(iw)
    return out


def build_bed(iw):
    """Rails along x on sleepers, with gaps where the blades pass below the trunk."""
    tpl_rail, tpl_leg, tpl_cross = tpl(iw, "Frame.119"), tpl(iw, "Frame.102"), tpl(iw, "Frame.110")
    out = []
    # the rails run the machine's whole length, end face to end face: skids for the trunk to slide
    # on at the west (from the rack) and off at the east (the cut logs)
    edges = [0.0]
    for sx in STATION_X:
        edges += [sx - RAIL_GAP, sx + RAIL_GAP]
    edges.append(CELLS_X * 16.0)
    runs = list(zip(edges[0::2], edges[1::2]))
    for r, (z0, z1) in enumerate(RAIL_Z, 1):
        for s, (x0, x1) in enumerate(runs, 1):
            out += beam(tpl_rail, [x0, BED_TOP - 3.0, z0], [x1, BED_TOP, z1], f"bed_rail{r}_{s}", "frame")
    for i, sx in enumerate(SLEEPER_X, 1):
        out += beam(tpl_cross, [sx - 1.5, BED_TOP - 6.0, RAIL_Z[0][0] - 3.0], [sx + 1.5, BED_TOP - 3.0, RAIL_Z[1][1] + 3.0],
                    f"bed_sleeper{i}", "frame")
        for j, (z0, z1) in enumerate(((RAIL_Z[0][0] - 3.0, RAIL_Z[0][0]), (RAIL_Z[1][1], RAIL_Z[1][1] + 3.0)), 1):
            out.append(from_template(tpl_leg, [sx - 1.5, 0.0, z0], [sx + 1.5, BED_TOP - 6.0, z1], f"bed_leg{i}{j}", "frame"))
    return out


def build(iw):
    geo = linkage()
    els, crank_spans = [], []
    for n, sx in enumerate(STATION_X, 1):
        crank = build_crank(iw, n, sx, 180.0 if n == 1 else 0.0)
        lo, hi = aabb_of(crank)
        crank_spans.append((lo[0], hi[0]))
        els += build_posts(iw, n, sx) + build_carriage(iw, n, sx) + build_saw(iw, n, sx)
        els += build_slider(iw, n, sx) + build_tail_post(iw, n, sx) + build_tail_rope(iw, n, sx)
        els += build_yoke(iw, n, sx) + crank + build_rod(iw, n, sx, geo) + build_rope_and_drum(iw, n, sx)
    gears = build_gearbox(iw)
    els += gears
    main_occ = crank_spans
    pin_d = [e for e in gears if e.name.startswith("drum_pinion_")]
    drum_occ = [(aabb_of(pin_d)[0][0], aabb_of(pin_d)[1][0])]
    drum_occ += [(sx - DRUM_HALF - 0.5, sx + DRUM_HALF + 0.5) for sx in STATION_X]
    drum_occ += [(sx + TAIL_ROPE_DX - SPOOL_HALF - 0.75, sx + TAIL_ROPE_DX + SPOOL_HALF + 0.75) for sx in STATION_X]
    els += build_shafts(iw, main_occ, drum_occ, post_x(STATION_X[1], 1)[1])
    els += build_crankbar(iw, crank_spans)
    els += build_levers(iw)
    els += build_frame(iw)
    for el in els:
        if METAL_NAMES.match(el.name):
            metal(el)
    return els


# ---------------------------------------------------------------- linkage solution
def linkage():
    """The rod's length and mean angle, the yoke's stroke, and the rod's swing, from the exact
    slider-crank geometry: crank pin C(theta) = shaft + throw * (cos, sin) in (y, z) (station 1;
    station 2 runs half a turn behind), yoke pin at fixed height ROD_PIN_Y. The rig drives the
    yoke and the rod by their first harmonics; `validate` measures what that costs."""
    N = 720

    def pin(t):
        return SHAFT_Y + CRANK_THROW * math.cos(t), SHAFT_Z + CRANK_THROW * math.sin(t)

    def yoke_z(t, length):
        cy, cz = pin(t)
        return cz - math.sqrt(length ** 2 - (cy - ROD_PIN_Y) ** 2)

    lo, hi = 1.0, 60.0
    for _ in range(80):           # rod length that puts the mean yoke position at PIN_Z
        mid = (lo + hi) / 2
        mean = sum(yoke_z(2 * math.pi * i / N, mid) for i in range(N)) / N
        lo, hi = (mid, hi) if mean > PIN_Z else (lo, mid)
    length = (lo + hi) / 2

    def harmonic(f):
        a = 2 / N * sum(f(2 * math.pi * i / N) * math.sin(2 * math.pi * i / N) for i in range(N))
        c = 2 / N * sum(f(2 * math.pi * i / N) * math.cos(2 * math.pi * i / N) for i in range(N))
        return math.hypot(a, c), math.atan2(c, a)

    stroke, stroke_phase = harmonic(lambda t: yoke_z(t, length) - PIN_Z)

    def psi(t):                   # rod angle about +x: turns +z (towards the crank) to (0, -sin, cos)
        cy, cz = pin(t)
        return math.atan2(-(cy - ROD_PIN_Y), cz - yoke_z(t, length))

    psi0 = sum(psi(2 * math.pi * i / N) for i in range(N)) / N
    swing, swing_phase = harmonic(lambda t: psi(t) - psi0)
    return {"length": length, "stroke": stroke, "stroke_phase": stroke_phase, "psi0": psi0,
            "swing": swing, "swing_phase": swing_phase}


def lever_geometry():
    """The rock shaft's turn: the trip pushes the tappet arm's tip down TRIP_TRAVEL, which turns
    the shaft by asin(travel / arm), and the fork arm's tip moves north by DOG_THROW."""
    la = DOG_GZ - TRIP_ZC if "DOG_GZ" in globals() else 1.0
    rock = math.asin(TRIP_TRAVEL / la)
    return {"rock": rock, "dog": DOG_THROW, "tappet_arm": la, "fork_arm": FORK_Y - ROCK_Y if "ROCK_Y" in globals() else 0.0,
            "trip_from": 1.0 - TRIP_TRAVEL / SINK, "trip_top": TRIP_TRAVEL / SINK}


# The dog clutch's groove, and the rock shaft under it: the shaft's height is set so the trip's
# travel at the tappet arm's tip throws the clutch DOG_THROW at the fork's tip.
DOG_Z0 = DOG_HUB_Z1 + DOG_THROW                            # the clutch's north face at rest (z)
DOG_GZ = DOG_Z0 + DOG_LEN / 2                              # its groove (z)
TRIP_ZC = (LEVER_Z[0] + LEVER_Z[1]) / 2                    # the trip rod's centre line (z)
CROWN_DISC_Z0 = 19.15                                      # the crown disc's north face (z)
ROCK_Y = FORK_Y - DOG_THROW * (DOG_GZ - TRIP_ZC) / TRIP_TRAVEL
ROCK_PIVOT = (ROCK_Y, DOG_GZ)                              # (y, z) of the rock shaft's axis
# The over-centre weight: on an upright arm leaning so it stands exactly over the rock shaft half
# way through the throw. Out (at rest) it leans south and holds the lever out; thrown in, it leans
# north and holds it in, until the carriage's lug throws it back through the vertical.
WEIGHT_ARM, WEIGHT_SIZE = 3.6, 1.8                         # the weight's centre from the axis; its size


def weight_centre(r: float = WEIGHT_ARM):
    """(y, z) of the point r along the weight arm at rest: the vertical turned by half the throw."""
    half = lever_geometry()["rock"] / 2
    return ROCK_PIVOT[0] + r * math.cos(half), ROCK_PIVOT[1] + r * math.sin(half)


# ---------------------------------------------------------------- rig
def rig_parts():
    b = 1.0 / 16
    geo, lev = linkage(), lever_geometry()
    shaft_pivot = [0.0, SHAFT_Y * b, SHAFT_Z * b]
    spin = DRUM_SPIN                                           # drum radians over a full cut
    trip = {"from": round(lev["trip_from"], 6), "to": 1.0, "lifting": "trip", "top": round(lev["trip_top"], 6)}

    def r6(v):
        return round(v, 6)

    parts = [
        {"id": "shaft", "match": ["shaft_*", "f1_crank_*", "f2_crank_*"], "requires": "crankshaft",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": shaft_pivot, "ratio": 1.0}]},
        # the rectifier: each loose pinion is carried by its one-way catch when the shaft turns its
        # way and idles otherwise, so both turn with the shaft's travel, always the same way
        {"id": "pinion_w", "match": ["gear_pinion_w_*"], "requires": "crankshaft",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": shaft_pivot, "ratio": 1.0, "rectified": True}]},
        {"id": "pinion_e", "match": ["gear_pinion_e_*"], "requires": "crankshaft",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": shaft_pivot, "ratio": -1.0, "rectified": True}]},
        # the small crown gear's half of the crown axle (and its dog hub) turns with the drum train:
        # the drum pinion is fixed on the drum shaft
        {"id": "crown_b", "match": ["gear_crown_b_*", "gear_crownb_*"], "requires": "crankshaft",
         "drivers": [{"type": "step", "motion": "rotate", "axis": "z", "pivot": [GEAR_X * b, SHAFT_Y * b, 0.0], "amount": r6(spin)}]},
        {"id": "crown", "match": ["gear_crown_*"], "requires": "crankshaft",
         "drivers": [{"type": "rotate", "axis": "z", "pivot": [GEAR_X * b, SHAFT_Y * b, 0.0], "ratio": r6(-1.0 / GEAR_RATIO), "rectified": True}]},
        # the dog clutch turns with the disc and is slid north onto the dog hub by the fork
        {"id": "dog", "match": ["dog_*"], "requires": "crankshaft",
         "drivers": [{"type": "step", "motion": "slide", "axis": "z", "amount": r6(-lev["dog"] * b), **trip},
                     {"type": "rotate", "axis": "z", "pivot": [GEAR_X * b, SHAFT_Y * b, 0.0], "ratio": r6(-1.0 / GEAR_RATIO), "rectified": True}]},
        {"id": "drum", "match": ["drum*"], "requires": "crankshaft",
         "drivers": [{"type": "step", "motion": "rotate", "axis": "x", "pivot": [0.0, SHAFT_Y * b, DRUM_Z * b], "amount": r6(-spin)}]},
    ]
    tg = tail_rope_geometry()
    for n, sx in enumerate(STATION_X, 1):
        lag = 0.0 if n == 1 else math.pi             # station 2's crank runs half a turn behind
        stroke = {"type": "slide", "axis": "z", "amplitude": r6(geo["stroke"] * b), "ratio": 1.0,
                  "phase": r6(math.remainder(geo["stroke_phase"] + lag, 2 * math.pi))}
        parts += [
            {"id": f"f{n}_yoke", "match": [f"f{n}_yoke_*"], "requires": "crankshaft", "drivers": [stroke]},
            {"id": f"f{n}_rod", "match": [f"f{n}_rod_*"], "requires": "crankshaft", "ride": f"f{n}_yoke",
             "drivers": [{"type": "swing", "axis": "x", "pivot": [r6((sx + ROD_DX) * b), r6(ROD_PIN_Y * b), r6(PIN_Z * b)],
                          "amplitude": r6(geo["swing"]), "ratio": 1.0,
                          "phase": r6(math.remainder(geo["swing_phase"] + lag, 2 * math.pi))}]},
            {"id": f"f{n}_carriage", "match": [f"f{n}_carriage_*"], "requires": f"sash{n}",
             "drivers": [{"type": "feed", "axis": "y", "travel": r6(-SINK * b)}]},
            {"id": f"f{n}_saw", "match": [f"f{n}_saw_*"], "requires": f"sash{n}", "ride": f"f{n}_carriage", "drivers": [dict(stroke)]},
            {"id": f"f{n}_blade", "match": [f"f{n}_blade_*"], "requires": f"blade{n}", "ride": f"f{n}_saw", "drivers": []},
            {"id": f"f{n}_slider", "match": [f"f{n}_slider_*"], "requires": f"sash{n}", "ride": f"f{n}_carriage", "drivers": []},
            {"id": f"f{n}_spool", "match": [f"f{n}_spool_*"], "requires": f"sash{n}",
             "drivers": [{"type": "step", "motion": "rotate", "axis": "x", "pivot": [0.0, SHAFT_Y * b, DRUM_Z * b], "amount": r6(-spin)}]},
            {"id": f"f{n}_tailrun", "match": [f"f{n}_tailrope_run*"], "requires": f"sash{n}", "drivers": []},
            {"id": f"f{n}_taildrop", "match": [f"f{n}_tailrope_drop*"], "requires": f"sash{n}",
             "drivers": [{"type": "stretch", "axis": "y", "anchor": [r6((sx + TAIL_ROPE_DX) * b), r6(tg["cy"] * b), r6(tg["drop_z"] * b)],
                          "length": r6(-(tg["cy"] - SAW_TOP - SLIDER_Y[1] - EYE_H) * b), "travel": r6(-SINK * b)}]},
            {"id": f"f{n}_sheave", "match": [f"f{n}_sheave_*"], "requires": f"sash{n}",
             "drivers": [{"type": "step", "motion": "rotate", "axis": "x", "pivot": [0.0, r6(tg["cy"] * b), r6(tg["cz"] * b)],
                          "amount": r6(SINK / tg["r"])}]},
            {"id": f"f{n}_rope", "match": [f"f{n}_rope*"], "requires": f"sash{n}",
             "drivers": [{"type": "stretch", "axis": "y", "anchor": [r6(sx * b), r6(SHAFT_Y * b), r6((DRUM_Z - DRUM_R) * b)],
                          "length": r6(-(SHAFT_Y - carriage_top() - EYE_H) * b), "travel": r6(-SINK * b)}]},
        ]
    ry, rz = ROCK_PIVOT
    parts += [
        {"id": "trip", "match": ["lever_trip_*"], "requires": "levers",
         "drivers": [{"type": "step", "motion": "slide", "axis": "y", "amount": r6(-TRIP_TRAVEL * b), **trip}]},
        # one rigid part: the rock shaft, its tappet arm and counterweight, its fork arm and fork
        {"id": "rock", "match": ["lever_rock_*"], "requires": "levers",
         "drivers": [{"type": "step", "motion": "rotate", "axis": "x", "pivot": [0.0, r6(ry * b), r6(rz * b)], "amount": r6(-lev["rock"]), **trip}]},
        {"id": "frame", "match": ["*"], "requires": None, "drivers": []},
    ]
    return parts


def glob_rx(pattern):
    return re.compile("^" + re.escape(pattern).replace(r"\*", ".*") + "$")


def part_of(parts, name):
    for p in parts:
        if any(glob_rx(g).match(name) for g in p["match"]):
            return p["id"]
    return None


# ---------------------------------------------------------------- driver maths (reference implementation)
# The renderer (Core/RigAnimation.cs) and the browser viewer implement exactly this. Inputs:
#   theta    the signed shaft angle, radians
#   depth    the saw's depth, 0 (at the top) .. 1 (at the bed, through the trunk)
#   lifting  1 while the saw is being wound back up, else 0 (a renderer may ease it)
#   travel   the shaft's travel: the total angle it has turned through either way (radians, never
#            decreasing); a rotate driver with "rectified": true turns by ratio * travel
# Matrices are 4x4, block units; rotations are right-handed about the positive axis.
def _m4(r=IDENT, t=(0.0, 0.0, 0.0)):
    return [[r[0][0], r[0][1], r[0][2], t[0]], [r[1][0], r[1][1], r[1][2], t[1]], [r[2][0], r[2][1], r[2][2], t[2]], [0, 0, 0, 1]]


def _m4mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def _about(r, pivot):
    t = [pivot[i] - mvec(r, pivot)[i] for i in range(3)]
    return _m4(r, t)


AXES = {"x": 0, "y": 1, "z": 2}


def step_amount(d, depth, lifting):
    """A step driver's fraction e in [0, 1]: the depth's progress through [from, to], then gated:
    "hold" keeps it at 1 while lifting, "block" keeps it at 0 while lifting."""
    lo, hi = d.get("from", 0.0), d.get("to", 1.0)
    e = min(1.0, max(0.0, (depth - lo) / (hi - lo)))
    gate = d.get("lifting")
    if gate == "hold":
        e = max(e, lifting)
    elif gate == "block":
        e = e * (1.0 - lifting)
    elif gate == "trip":
        # thrown over [from, to] at the bottom on the way down, back over [0, top] at the top on
        # the way up; the two halves agree at both ends, where the direction changes
        e = e * (1.0 - lifting) + min(1.0, max(0.0, depth / d["top"])) * lifting
    return e


def driver_matrix(d, theta, depth, lifting=0.0, travel=None):
    if travel is None:
        travel = abs(theta)
    axis = d["axis"]
    unit = [0.0, 0.0, 0.0]
    unit[AXES[axis]] = 1.0
    kind = d["type"]
    if kind == "rotate":
        return _about(rot(axis, math.degrees(d.get("ratio", 1.0) * (travel if d.get("rectified") else theta))), d["pivot"])
    if kind == "swing":
        ang = d["amplitude"] * math.sin(d.get("ratio", 1.0) * theta + d.get("phase", 0.0))
        return _about(rot(axis, math.degrees(ang)), d["pivot"])
    if kind == "slide":
        return _m4(IDENT, [u * d["amplitude"] * math.sin(d.get("ratio", 1.0) * theta + d.get("phase", 0.0)) for u in unit])
    if kind == "feed":
        return _m4(IDENT, [u * d["travel"] * depth for u in unit])
    if kind == "step":
        e = step_amount(d, depth, lifting)
        if d["motion"] == "rotate":
            return _about(rot(axis, math.degrees(d["amount"] * e)), d["pivot"])
        return _m4(IDENT, [u * d["amount"] * e for u in unit])
    if kind == "stretch":
        k = AXES[axis]
        f = (d["length"] + d["travel"] * depth) / d["length"]
        m = _m4()
        m[k][k] = f
        m[k][3] = d["anchor"][k] * (1.0 - f)
        return m
    raise ValueError(kind)


def part_matrix(parts, pid, theta, depth, lifting=0.0, travel=None):
    """Drivers apply in list order to the authored geometry (pivots in the authored frame);
    then the `ride` part's whole transform is applied on top."""
    p = next(q for q in parts if q["id"] == pid)
    m = _m4()
    for d in p["drivers"]:
        m = _m4mul(driver_matrix(d, theta, depth, lifting, travel), m)
    if p.get("ride"):
        m = _m4mul(part_matrix(parts, p["ride"], theta, depth, lifting, travel), m)
    return m


def _apply(m, p):
    return [m[i][0] * p[0] + m[i][1] * p[1] + m[i][2] * p[2] + m[i][3] for i in range(3)]


def posed(el: El, m) -> El:
    """`el` (voxels) moved by a part matrix (blocks). A stretch scales the element's extent along
    its axis (the rope is axis-aligned, so this stays a box)."""
    out = el.clone()
    lin = [row[:3] for row in m[:3]]
    t = [m[i][3] * 16 for i in range(3)]
    scale = [math.sqrt(sum(lin[i][j] ** 2 for i in range(3))) for j in range(3)]
    r = [[lin[i][j] / scale[j] for j in range(3)] for i in range(3)]
    out.c = [mvec(lin, el.c)[i] + t[i] for i in range(3)]
    if any(abs(s - 1) > 1e-9 for s in scale):
        for k in range(3):                       # world axis j scaled: stretch the local axis along it
            ax = [abs(el.r[j][k]) for j in range(3)]
            out.size[k] = el.size[k] * sum(ax[j] * scale[j] for j in range(3))
    out.r = mmul(r, el.r)
    return out


REFERENCE_POSES = [(theta, depth, lifting, travel)
                   for theta in (0.0, 1.1, 2.9, 4.6)
                   for depth in (0.0, 0.002, 0.4, 0.96, 1.0)
                   for lifting in (0.0, 1.0)
                   for travel in (abs(theta), abs(theta) + 7.3)] + [(0.7, 0.5, 0.5, 0.7), (5.9, 0.97, 0.25, 40.2), (-2.3, 0.003, 0.6, 2.3), (13.0, 0.75, 0.0, 113.0)]


def reference_json(parts):
    """Every part's matrix (3 rows of 4, block units) at a grid of poses, from this file's
    reference maths, for the C# tests to compare against so the two cannot drift."""
    poses = []
    for theta, depth, lifting, travel in REFERENCE_POSES:
        mats = {p["id"]: [[round(v, 6) for v in row] for row in part_matrix(parts, p["id"], theta, depth, lifting, travel)[:3]] for p in parts}
        poses.append({"theta": theta, "depth": depth, "lifting": lifting, "travel": travel, "matrices": mats})
    return {"_comment": "Generated by mods-src/buckingsawmill/tools/make_shape.py from the shipped rig.json's parts: "
                        "each part's matrix as 3 rows of 4 (block units). RigAnimationTests checks Core/RigAnimation.cs against it.",
            "poses": poses}


def reference_dumps(ref):
    lines = ["{", f'\t"_comment": {json.dumps(ref["_comment"])},', '\t"poses": [']
    lines.append(",\n".join("\t\t" + json.dumps(p, separators=(",", ":")) for p in ref["poses"]))
    lines.append("\t]")
    lines.append("}")
    return "\n".join(lines) + "\n"


# ---------------------------------------------------------------- collision boxes
def cell_boxes(els, cell):
    """Up to three cuboids (cell-local 0..1) covering the elements' AABBs clipped to the cell."""
    lo_c = [cell[k] * 16 for k in range(3)]
    clipped = []
    for el in els:
        lo, hi = el.aabb()
        a = [max(lo[k], lo_c[k]) for k in range(3)]
        b = [min(hi[k], lo_c[k] + 16) for k in range(3)]
        if all(b[k] - a[k] > 0.01 for k in range(3)):
            clipped.append((a, b))
    if not clipped:
        return None

    def bound(bs):
        return [min(x[0][k] for x in bs) for k in range(3)], [max(x[1][k] for x in bs) for k in range(3)]

    def vol(b):
        return math.prod(b[1][k] - b[0][k] for k in range(3))

    groups = [clipped]
    while len(groups) < 3:
        best = None
        for gi, g in enumerate(groups):
            if len(g) < 2:
                continue
            whole = vol(bound(g))
            for axis in range(3):
                srt = sorted(g, key=lambda x: (x[0][axis] + x[1][axis]))
                for i in range(1, len(srt)):
                    a, b2 = srt[:i], srt[i:]
                    gain = whole - vol(bound(a)) - vol(bound(b2))
                    if best is None or gain > best[0]:
                        best = (gain, gi, a, b2)
        if best is None or best[0] < 0.08 * 16 ** 3:
            break
        _, gi, a, b2 = best
        groups[gi:gi + 1] = [a, b2]
    out = []
    for g in groups:
        lo, hi = bound(g)
        out.append([round((lo[k] - lo_c[k]) / 16, 4) for k in range(3)] + [round((hi[k] - lo_c[k]) / 16, 4) for k in range(3)])
    return sorted(out)


# ---------------------------------------------------------------- output
def r4(x, n=4):
    v = round(x, n)
    return 0.0 if v == 0 else v


def element_json(el: El):
    half = [abs(s) / 2 for s in el.size]
    e = {"name": el.name,
         "from": [r4(el.c[k] - half[k]) for k in range(3)],
         "to": [r4(el.c[k] + half[k]) for k in range(3)]}
    a = euler_xyz(el.r)
    if any(abs(v) > 1e-4 for v in a):
        e["rotationOrigin"] = [r4(v) for v in el.c]
        for key, v in zip(("rotationX", "rotationY", "rotationZ"), a):
            if abs(v) > 1e-4:
                e[key] = r4(v)
    faces = {}
    for d in ("north", "east", "south", "west", "up", "down"):
        if d not in el.faces:
            continue
        f = el.faces[d]
        out = {"texture": f["texture"], "uv": [r4(v, 3) for v in f["uv"]]}
        if f.get("rotation"):
            out["rotation"] = f["rotation"]
        out["autoUv"] = False
        faces[d] = out
    e["faces"] = faces
    return e


def shape_json(els, source):
    used = {f["texture"].lstrip("#") for el in els for f in el.faces.values()}
    textures = {k: v for k, v in TEXTURES.items() if k in used}
    return {
        "_comment": f"Generated by mods-src/buckingsawmill/tools/make_shape.py. The gears, saw blades, saw heads and "
                    f"cranks are from Immersive Woodworking's sawmill ({source}) by Bobrik00, used with permission "
                    f"(CREDITS.md). Keep element names when editing.",
        "textureWidth": TEX_SIZE, "textureHeight": TEX_SIZE,
        "textureSizes": {k: [TEX_SIZE, TEX_SIZE] for k in textures},
        "textures": textures,
        "elements": [element_json(el) for el in els],
    }


def shape_dumps(shape):
    head = {k: v for k, v in shape.items() if k != "elements"}
    lines = ["{"]
    for k, v in head.items():
        lines.append(f"\t{json.dumps(k)}: {json.dumps(v, separators=(', ', ': '))},")
    lines.append('\t"elements": [')
    lines.append(",\n".join("\t\t" + json.dumps(e, separators=(",", ":")) for e in shape["elements"]))
    lines.append("\t]")
    lines.append("}")
    return "\n".join(lines) + "\n"


def make_rig(els, parts):
    b = 1.0 / 16
    rest = [posed(el, part_matrix(parts, el.part, 0.0, 0.0)) for el in els]
    cells = []
    for x in range(CELLS_X):
        for y in range(CELLS_Y):
            for z in range(CELLS_Z):
                boxes = cell_boxes(rest, (x, y, z))
                if boxes is not None or (x, y, z) == ORIGIN_CELL:
                    cells.append({"pos": [x, y, z], "boxes": boxes or []})
    return {
        "_comment": "Generated by mods-src/buckingsawmill/tools/make_shape.py. Native frame (south-facing), "
                    "block units, controller cell at [0,0,0]: the middle of the east (output) end, nearest the player "
                    "who placed it; the west (axle and infeed) end is farthest. See the mod's README for the schema.",
        "cells": cells,
        "powerCell": [0, int(SHAFT_Y // 16), int(SHAFT_Z // 16)],
        "powerFace": "west",
        "infeedSide": INFEED_SIDE,
        "outputSide": OUTPUT_SIDE,
        "output": {"pos": [CELLS_X + 0.25, round((BED_TOP + 2) * b, 4), TRUNK_Z * b]},
        "trunkBed": {"origin": [CELLS_X / 2, BED_TOP * b, TRUNK_Z * b], "axis": "x", "length": 5.0,
                     "_origin": "centre of the bed's top surface; trunks are drawn centred on it, lying along +x"},
        "saw": {"topY": SAW_TOP * b, "bottomY": SAW_BOTTOM * b,
                "_comment": "height of the blades' cutting edge at depth 0 (top of the cycle) and depth 1 (end of the cut)"},
        "parts": parts,
    }


def rig_dumps(rig):
    lines = ["{"]
    keys = list(rig)
    for i, k in enumerate(keys):
        end = "," if i < len(keys) - 1 else ""
        v = rig[k]
        if k in ("cells", "parts"):
            lines.append(f"\t{json.dumps(k)}: [")
            lines.append(",\n".join("\t\t" + json.dumps(x, separators=(", ", ": ")) for x in v))
            lines.append("\t]" + end)
        else:
            lines.append(f"\t{json.dumps(k)}: {json.dumps(v, separators=(', ', ': '))}{end}")
    lines.append("}")
    return "\n".join(lines) + "\n"


# ---------------------------------------------------------------- validation
def obb_overlap(el: El, lo, hi, eps=0.02):
    """Separating-axis test between a rotated element and an axis-aligned box (voxels)."""
    bc = [(lo[k] + hi[k]) / 2 for k in range(3)]
    bh = [(hi[k] - lo[k]) / 2 - eps for k in range(3)]
    eh = [max(abs(s) / 2 - eps, 0.0) for s in el.size]
    ea = [[el.r[i][k] for i in range(3)] for k in range(3)]  # element axes (columns)
    ba = [[1, 0, 0], [0, 1, 0], [0, 0, 1]]
    d = [el.c[k] - bc[k] for k in range(3)]
    axes = ea + ba
    for u in ea:
        for v in ba:
            w = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
            if sum(x * x for x in w) > 1e-9:
                axes.append(w)
    for ax in axes:
        dist = abs(sum(d[k] * ax[k] for k in range(3)))
        ra = sum(eh[k] * abs(sum(ea[k][i] * ax[i] for i in range(3))) for k in range(3))
        rb = sum(bh[k] * abs(ax[k]) for k in range(3))
        if dist > ra + rb:
            return False
    return True


def obb_obb(a: El, b: El, eps=0.02):
    """Separating-axis test between two rotated elements (voxels)."""
    ah = [max(abs(s) / 2 - eps, 0.0) for s in a.size]
    bh = [max(abs(s) / 2 - eps, 0.0) for s in b.size]
    aa = [[a.r[i][k] for i in range(3)] for k in range(3)]
    ba = [[b.r[i][k] for i in range(3)] for k in range(3)]
    d = [a.c[k] - b.c[k] for k in range(3)]
    axes = aa + ba
    for u in aa:
        for v in ba:
            w = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
            if sum(x * x for x in w) > 1e-9:
                axes.append(w)
    for ax in axes:
        dist = abs(sum(d[k] * ax[k] for k in range(3)))
        ra = sum(ah[k] * abs(sum(aa[k][i] * ax[i] for i in range(3))) for k in range(3))
        rb = sum(bh[k] * abs(sum(ba[k][i] * ax[i] for i in range(3))) for k in range(3))
        if dist > ra + rb:
            return False
    return True


def poses():
    """(theta, depth, lifting, travel) samples: 8 shaft angles, five depths, cutting and lifting,
    and a spread of shaft travels (the rectified gears' input)."""
    out = []
    for i in range(8):
        for depth in (0.0, 0.25, 0.5, 0.75, 1.0):
            for lifting in (0.0, 1.0):
                out.append((i * math.pi / 4, depth, lifting, i * 0.9 + depth * 5.0 + lifting * 2.3))
    out += [(0.0, depth, 0.0, travel) for depth in (0.02, 0.9, 0.95, 0.98, 1.0) for travel in (0.0, 3.1)]
    return out


# Moving-part pairs that must not touch, besides the trunk and the frame (pin joints are
# allowed: a pair is checked only between the element-name patterns given).
CLEARANCE_PAIRS = [
    ("f{n}_rod", None, "f{n}_carriage", None), ("f{n}_rod", None, "drum", None), ("f{n}_rod", None, "frame", None),
    ("f{n}_yoke", None, "f{n}_carriage", None), ("f{n}_yoke", None, "frame", r"_guide_"),
    ("f{n}_saw", None, "f{n}_yoke", r"_(bar|head|foot)"), ("f{n}_saw", r"_pin$", "f{n}_carriage", None),
    ("f{n}_saw", None, "frame", None), ("f{n}_carriage", None, "frame", None),
    ("f{n}_blade", None, "frame", r"bed_"), ("f{n}_blade", None, "f{n}_carriage", None),
    ("f{n}_rope", None, "f{n}_saw", None), ("f{n}_rope", None, "f{n}_yoke", None),
    ("trip", None, "f1_carriage", r"(?<!lug)$"), ("trip", None, "frame", r"bed_|post|bearing|beam"), ("trip", None, "drum", None),
    ("rock", None, "drum", None), ("rock", None, "crown", None), ("rock", None, "crown_b", None), ("rock", None, "dog", r"sleeve"),
    ("rock", None, "frame", r"^(?!rock_bearing_)"), ("rock", None, "trip", r"tappet"), ("rock", None, "shaft", None),
    ("rock", None, "pinion_w", None), ("rock", None, "pinion_e", None), ("rock", None, "f1_carriage", None), ("rock", None, "f1_rope", None),
    ("dog", None, "frame", None), ("dog", None, "crown", r"_disc_"), ("dog", None, "crown_b", r"_b_"), ("crown_b", None, "frame", r"^(?!bearing_crown_n)"),
    ("pinion_w", None, "frame", None), ("pinion_e", None, "frame", None), ("crown", None, "frame", r"^(?!bearing_crown_s$)"),
    ("f{n}_slider", None, "frame", None), ("f{n}_saw", r"_tail$", "f{n}_slider", None), ("f{n}_saw", r"_tail$", "frame", None),
    ("f{n}_blade", None, "f{n}_slider", None), ("f{n}_slider", None, "f{n}_carriage", None),
    # the rotating parts clear the frame, except where a shaft runs in its bearings (and the drum
    # shaft through the north posts' caps, which hold its bearings)
    ("shaft", None, "frame", r"^(?!bearing|input_bearing)"), ("drum", None, "frame", r"^(?!bearing|f\d_post_n_)"),
    ("f{n}_rope", None, "frame", None), ("f{n}_spool", None, "frame", r"^(?!bearing)"),
]


def validate(els, parts, rig, shape, frame_shape):
    ok = True
    b = 1.0 / 16
    geo, lev = linkage(), lever_geometry()

    def fail(msg):
        nonlocal ok
        ok = False
        print("FAIL", msg)

    # Euler round trip
    worst = 0.0
    for el in els:
        back = from_euler(*euler_xyz(el.r))
        worst = max(worst, max(abs(back[i][j] - el.r[i][j]) for i in range(3) for j in range(3)))
    print(f"rotation round trip: worst matrix error {worst:.2e}")
    if worst > 1e-4:
        fail("euler decomposition")

    # parts
    counts = {}
    for el in els:
        got = part_of(parts, el.name)
        counts[got] = counts.get(got, 0) + 1
        if got != el.part:
            fail(f"{el.name}: matches part {got}, intended {el.part}")
    names = [el.name for el in els]
    dupes = {n for n in names if names.count(n) > 1}
    if dupes:
        fail(f"duplicate element names: {sorted(dupes)[:5]}")
    print("elements per part:", ", ".join(f"{p['id']} {counts.get(p['id'], 0)}" for p in parts), f"(total {len(els)})")
    for p in parts:
        if counts.get(p["id"], 0) == 0:
            fail(f"part {p['id']} matches nothing")

    by_part = {}
    for el in els:
        by_part.setdefault(el.part, []).append(el)
    pose_list = poses()
    posed_cache = {}

    def posed_part(pid, pose):
        key = (pid, pose)
        if key not in posed_cache:
            m = part_matrix(parts, pid, *pose)
            posed_cache[key] = [posed(el, m) for el in by_part[pid]]
        return posed_cache[key]

    def samples(pid):
        return [(0.0, 0.0, 0.0, 1.0)] if pid == "frame" else pose_list

    # inside the declared cells at rest, inside the machine box over the motion
    declared = {tuple(c["pos"]) for c in rig["cells"]}
    worst_out, worst_el = 0.0, None
    for pid in by_part:
        for pose in samples(pid):
            for el in posed_part(pid, pose):
                lo, hi = el.aabb()
                for k, n in enumerate((CELLS_X, CELLS_Y, CELLS_Z)):
                    over = max(-lo[k], hi[k] - n * 16)
                    if over > worst_out:
                        worst_out, worst_el = over, (el.name, pose)
                if pose == (0.0, 0.0, 0.0, 1.0):
                    lo = [v + 0.01 for v in lo]
                    hi = [v - 0.01 for v in hi]
                    for cx in range(int(lo[0] // 16), int(math.ceil(hi[0] / 16))):
                        for cy in range(int(lo[1] // 16), int(math.ceil(hi[1] / 16))):
                            for cz in range(int(lo[2] // 16), int(math.ceil(hi[2] / 16))):
                                if (cx, cy, cz) not in declared:
                                    fail(f"{el.name} reaches undeclared cell {(cx, cy, cz)}")
    print(f"machine box: worst overhang {max(worst_out, 0):.3f} voxels {worst_el or ''}")
    if worst_out > 0.01:
        fail("an element leaves the machine box")

    # trunk envelopes against everything but the blades, over the whole motion
    for size, (ln, w, h) in TRUNK_SIZES.items():
        lo = [CELLS_X * 8 - ln * 8, BED_TOP, TRUNK_Z - w * 8]
        hi = [CELLS_X * 8 + ln * 8, BED_TOP + h * 16, TRUNK_Z + w * 8]
        hits = set()
        for pid in by_part:
            if pid.endswith("_blade"):
                continue
            for pose in samples(pid):
                for el in posed_part(pid, pose):
                    if obb_overlap(el, lo, hi):
                        hits.add(el.name)
        print(f"trunk {size:>3} ({ln}x{w}x{h}): {'clear' if not hits else 'HITS ' + ', '.join(sorted(hits)[:6])}")
        if hits:
            fail(f"trunk {size} intersects the machine")

    # the infeed and the outfeed, with the saws at the top (depth 0, every shaft angle sampled):
    # an xxl trunk slides in lengthwise from beyond the west end, through the portal, to its place
    # on the bed; a 2x2 section slides from there out beyond the east end. Neither swept volume
    # touches anything, fixed or moving.
    def sweep(lo, hi):
        hits = set()
        for pid in by_part:
            for pose in ([(0.0, 0.0, 0.0, 1.0)] if pid == "frame" else [(i * math.pi / 8, 0.0, 0.0, i * 0.4) for i in range(16)]):
                for el in posed_part(pid, pose):
                    if obb_overlap(el, lo, hi):
                        hits.add(el.name)
        return hits
    ln, w, h = TRUNK_SIZES["xxl"]
    rest_x = (CELLS_X * 8 - ln * 8, CELLS_X * 8 + ln * 8)
    for what, (x0, x1), (w, h) in (("infeed: an xxl trunk sliding in from beyond the west end to the bed", (-ln * 16, rest_x[1]), (w, h)),
                                   ("outfeed: a 2x2 section sliding from the bed out beyond the east end", (rest_x[0], CELLS_X * 16 + ln * 16), (2, 2))):
        hits = sweep([x0, BED_TOP, TRUNK_Z - w * 8], [x1, BED_TOP + h * 16, TRUNK_Z + w * 8])
        print(f"{what} (x {x0:.0f}..{x1:.0f}) " + ("touches nothing with the saws at the top" if not hits else "HITS " + ", ".join(sorted(hits)[:6])))
        if hits:
            fail(what.split(":")[0] + " is blocked")
    # the rack stands on the ground against the west end (the infeed neighbours) and the axle comes
    # in at the power cell's west face: they never need the same cell
    pc = rig["powerCell"]
    if pc[1] == 0:
        fail("the power cell is at ground level, where the rack stands")
    print(f"west end: the rack stands at ground level (y 0) beyond x 0, the axle arrives at cell {pc} from the west")

    # blades: clear of the trunk at the top of the cycle, clear of the bed rails at the bottom
    for n in (1, 2):
        hits = set()
        for i in range(16):
            for el in posed_part(f"f{n}_blade", (i * math.pi / 8, 1.0, 0.0)):
                for rail in by_part["frame"]:
                    if rail.name.startswith("bed_") and obb_obb(el, rail):
                        hits.add(rail.name)
        lo = [8.0, BED_TOP, TRUNK_Z - 16]
        hi = [88.0, BED_TOP + 32, TRUNK_Z + 16]
        at_top = any(obb_overlap(el, lo, hi) for i in range(16) for el in posed_part(f"f{n}_blade", (i * math.pi / 8, 0.0, 0.0)))
        print(f"blades {n}: at depth 1 {'clear of the bed' if not hits else 'HIT ' + ', '.join(sorted(hits))};"
              f" at the top {'clear of a 2x2 trunk' if not at_top else 'TOUCH a 2x2 trunk'}")
        if hits or at_top:
            fail(f"blades {n} hit the bed, or a trunk with the saws at the top")

    # linkage: rod ends on their pins, head pin inside the yoke's slot, rope ends on drum and carriage
    for n, sx in enumerate(STATION_X, 1):
        worst_rod = worst_slot_z = 0.0
        slot_y_ok = True
        a0 = [(sx + ROD_DX) * b, ROD_PIN_Y * b, PIN_Z * b]
        psi = geo["psi0"]
        b0 = [a0[0], a0[1] - geo["length"] * b * math.sin(psi), a0[2] + geo["length"] * b * math.cos(psi)]
        sign = 1.0 if n == 1 else -1.0
        for i in range(72):
            theta = i * math.pi / 36
            for depth in (0.0, 0.5, 1.0):
                crank_pin = _apply(part_matrix(parts, "shaft", theta, depth), [a0[0], (SHAFT_Y + sign * CRANK_THROW) * b, SHAFT_Z * b])
                rod_top = _apply(part_matrix(parts, f"f{n}_rod", theta, depth), b0)
                rod_foot = _apply(part_matrix(parts, f"f{n}_rod", theta, depth), a0)
                yoke_pin = _apply(part_matrix(parts, f"f{n}_yoke", theta, depth), a0)
                worst_rod = max(worst_rod, 16 * math.dist(rod_top, crank_pin), 16 * math.dist(rod_foot, yoke_pin))
                pin = _apply(part_matrix(parts, f"f{n}_saw", theta, depth), [(sx + ROD_DX) * b, (SAW_TOP + PIN_Y) * b, PIN_Z * b])
                slot = _apply(part_matrix(parts, f"f{n}_yoke", theta, depth), [(sx + ROD_DX) * b, pin[1], PIN_Z * b])
                worst_slot_z = max(worst_slot_z, 16 * abs(pin[2] - slot[2]))
                y = pin[1] * 16
                if not (YOKE_Y[0] + YOKE_END + PIN_D / 2 <= y <= YOKE_Y[1] - YOKE_END - PIN_D / 2):
                    slot_y_ok = False
        print(f"station {n}: rod ends to their pins, worst gap {worst_rod:.2f} voxels; head pin off the slot's"
              f" centre line by {worst_slot_z:.3f}, {'inside' if slot_y_ok else 'OUTSIDE'} the slot's length at every depth")
        if worst_rod > 0.5:
            fail(f"station {n} rod drifts off its pins")
        if worst_slot_z > 0.05 or not slot_y_ok:
            fail(f"station {n} head pin leaves the yoke's slot")
        # the carriage's rope: its top end on the drum's rope wrap at the tangent point (DRUM_R from
        # the axis, level with it, on the north side), its foot on the carriage's eye, at every depth
        gaps = []
        for depth in [k / 20 for k in range(21)]:
            r = posed(by_part[f"f{n}_rope"][0], part_matrix(parts, f"f{n}_rope", 0.0, depth))
            lo, hi = r.aabb()
            cz = (lo[2] + hi[2]) / 2
            gaps += [abs(hi[1] - SHAFT_Y), abs(math.hypot(hi[1] - SHAFT_Y, cz - DRUM_Z) - DRUM_R)]
            eye = next(e for e in posed_part(f"f{n}_carriage", (0.0, depth, 0.0, 0.0)) if e.name.endswith("_eye"))
            e_lo, e_hi = eye.aabb()
            gaps.append(abs(lo[1] - e_hi[1]))
            if not (e_lo[0] <= lo[0] and hi[0] <= e_hi[0] and e_lo[2] <= lo[2] and hi[2] <= e_hi[2]):
                gaps.append(9.0)
        print(f"station {n}: carriage rope leaves the drum's wrap at the tangent point and ends on the carriage's eye, worst gap {max(gaps):.3f} voxels")
        if max(gaps) > 0.05:
            fail(f"station {n} rope does not meet its drum and carriage")

    # nothing fixed floats: every frame element shares a face with, or overlaps, the rest of the
    # frame, and the frame is one piece standing on the ground
    frame = by_part["frame"]
    boxes = [el.aabb() for el in frame]
    aligned = [all(el.local_axis_for(k) is not None for k in range(3)) for el in frame]

    def joined(i, j):
        (alo, ahi), (blo, bhi) = boxes[i], boxes[j]
        ov = [min(ahi[k], bhi[k]) - max(alo[k], blo[k]) for k in range(3)]
        if min(ov) < -0.02:
            return False
        if aligned[i] and aligned[j]:
            return sum(o > 0.05 for o in ov) >= 2          # a shared face, not just an edge or a corner
        return obb_obb(frame[i], frame[j], eps=-0.03)
    seen = {i for i, (lo, _) in enumerate(boxes) if lo[1] <= 0.01}
    todo = list(seen)
    while todo:
        i = todo.pop()
        for j in range(len(frame)):
            if j not in seen and joined(i, j):
                seen.add(j)
                todo.append(j)
    floating = sorted(frame[i].name for i in range(len(frame)) if i not in seen)
    print(f"frame: {len(seen)} of {len(frame)} elements joined to the ground through the frame"
          + ("" if not floating else f"; FLOATING: {', '.join(floating)}"))
    if floating:
        fail("frame elements float free of the structure")

    # each bearing encloses the shaft it carries, at rest and turned 45 degrees
    def shaft_in(bearing, pid, name_rx, axis):
        b_lo, b_hi = bearing.aabb()
        worst = 1e9
        for theta in (0.0, math.pi / 4):
            pose = (theta, 0.0, 0.0, 1.0) if pid != "rock" else (0.0, 0.0, 0.0, 1.0)
            for el in posed_part(pid, pose):
                if not re.search(name_rx, el.name):
                    continue
                lo, hi = el.aabb()
                if min(hi[axis], b_hi[axis]) - max(lo[axis], b_lo[axis]) <= 0.05:
                    continue
                for k in range(3):
                    if k != axis:
                        worst = min(worst, lo[k] - b_lo[k], b_hi[k] - hi[k])
        return worst
    carried = [(r"^bearing\d[we]$|^input_bearing$", "shaft", r"^(shaft_|f\d_crank_Rotor_default_4_00[15])", 0),
               (r"^bearing_drum", "drum", r"^drum_shaft_", 0),
               (r"^rock_bearing_", "rock", r"^lever_rock_shaft$", 0)]
    for brx, pid, srx, axis in carried:
        for bearing in [el for el in frame if re.search(brx, el.name)]:
            w = shaft_in(bearing, pid, srx, axis)
            print(f"bearing {bearing.name}: {'no shaft passes through it' if w > 1e8 else f'encloses its shaft with {w:.2f} to spare'}")
            if w > 1e8 or w < -0.01:
                fail(f"{bearing.name} does not carry its shaft")

    # every rotating shaft (and each half of the split crown axle) is carried by the frame: at least
    # two fixed bearings enclose its axis along the shaft's length, or one bearing plus a pilot
    # spigot into the other half; levers on a pivot pin need their bracket; loose wheels are
    # located on both sides by a collar or a bearing
    def supports(els_, axis, c, exclude=()):
        u, v = [k for k in range(3) if k != axis]
        on = [e for e in els_ if (lambda lo, hi: lo[u] <= c[0] <= hi[u] and lo[v] <= c[1] <= hi[v])(*e.aabb())]
        if not on:
            return [], None
        amin, amax = min(e.aabb()[0][axis] for e in on), max(e.aabb()[1][axis] for e in on)
        found = []
        for f in frame:
            if f.name in exclude:
                continue
            lo, hi = f.aabb()
            if lo[u] + 0.05 < c[0] < hi[u] - 0.05 and lo[v] + 0.05 < c[1] < hi[v] - 0.05 and min(hi[axis], amax) - max(lo[axis], amin) > 0.05:
                found.append(f.name)
        return found, (amin, amax)
    pilot = [e for e in els if e.name == "gear_crownb_pilot"]
    shafts = [("shaft", "main shaft and crankshaft", by_part["shaft"], 0, (SHAFT_Y, SHAFT_Z), 2, False),
              ("crown", "crown axle, the disc's half", by_part["crown"], 2, (GEAR_X, SHAFT_Y), 1, True),
              ("crown_b", "crown axle, the small crown gear's half", by_part["crown_b"], 2, (GEAR_X, SHAFT_Y), 1, True),
              ("drum", "drum shaft", by_part["drum"], 0, (SHAFT_Y, DRUM_Z), 2, False),
              ("rock", "rock shaft", by_part["rock"], 0, ROCK_PIVOT, 2, False)]
    tg = tail_rope_geometry()
    for n, sx in enumerate(STATION_X, 1):
        shafts.append((f"f{n}_sheave", f"station {n} sheave's pin", [e for e in frame if e.name == f"f{n}_tailpost_pin"], 0, (tg["cy"], tg["cz"]), 2, False))
    for pid, label, els_, axis, c, need, piloted in shafts:
        found, _ = supports(els_, axis, c, exclude=(f"f{pid[1]}_tailpost_pin",) if pid.endswith("_sheave") else ())
        has_pilot = False
        if piloted and pilot:
            # the pilot runs from the small crown gear's hub on into the disc's half's axle
            p_lo, p_hi = pilot[0].aabb()
            has_pilot = any(min(p_hi[2], e.aabb()[1][2]) - max(p_lo[2], e.aabb()[0][2]) > 0.05 for e in by_part["crown"] if e.name.startswith("gear_crown_axle"))
        ok_ = len(found) >= need and (not piloted or has_pilot) or len(found) >= 2
        print(f"support {pid} ({label}): {len(found)} bearing(s) {', '.join(sorted(found)) or 'NONE'}"
              + (" plus the pilot spigot" if has_pilot else "") + ("" if ok_ else "  <-- NOT CARRIED"))
        if not ok_:
            fail(f"{pid}: its shaft is not carried by the frame")
    for pid in ("pinion_w", "pinion_e"):
        lo, hi = aabb_of(by_part[pid])
        sides = []
        for face, sign in ((lo[0], -1), (hi[0], 1)):
            near = [e.name for e in by_part["shaft"] + frame
                    if (lambda a, b: a[1] <= SHAFT_Y <= b[1] and a[2] <= SHAFT_Z <= b[2] and
                        ((sign < 0 and face - 0.9 <= b[0] <= face + 0.01) or (sign > 0 and face - 0.01 <= a[0] <= face + 0.9))
                        and (b[0] - a[0]) < 3.5)(*e.aabb()) and not e.name.startswith("shaft_1")]
            sides.append(near)
        print(f"location {pid}: west {', '.join(sides[0]) or 'NOTHING'}; east {', '.join(sides[1]) or 'NOTHING'}")
        if not sides[0] or not sides[1]:
            fail(f"{pid} is free to slide along its shaft")

    # the guide block's rope: its ends meet the spool, the sheave and the guide block at every
    # depth, and the run, the drop and the sheave clear every other moving part over the whole
    # motion (both shaft directions) and the frame
    tg = tail_rope_geometry()
    for n, sx in enumerate(STATION_X, 1):
        rx = sx + TAIL_ROPE_DX
        run_lo, run_hi = by_part[f"f{n}_tailrun"][0].aabb()
        gaps = [abs(math.hypot((run_lo[1] + run_hi[1]) / 2 - SHAFT_Y, run_lo[2] - DRUM_Z) - DRUM_R),       # leaves the spool's underside
                abs(math.hypot((run_lo[1] + run_hi[1]) / 2 - tg["cy"], run_hi[2] - tg["cz"]) - tg["r"])]    # meets the sheave's top
        for depth in [k / 20 for k in range(21)]:
            for lifting in (0.0, 1.0):
                drop = posed_part(f"f{n}_taildrop", (0.0, depth, lifting, 1.0))[0]
                d_lo, d_hi = drop.aabb()
                sleeve = next(el for el in posed_part(f"f{n}_slider", (0.0, depth, lifting, 1.0)) if el.name.endswith("_eye"))
                s_lo, s_hi = sleeve.aabb()
                gaps.append(abs(d_lo[1] - s_hi[1]))                                                        # tied to the guide block's eye
                gaps.append(abs(math.hypot(d_hi[1] - tg["cy"], (d_lo[2] + d_hi[2]) / 2 - tg["cz"]) - tg["r"]))  # leaves the sheave's side
                if not (s_lo[0] <= d_lo[0] and d_hi[0] <= s_hi[0] and s_lo[2] <= d_lo[2] and d_hi[2] <= s_hi[2]):
                    gaps.append(9.0)
        hits = set()
        others = {pid: els_ for pid, els_ in by_part.items()
                  if pid not in (f"f{n}_tailrun", f"f{n}_taildrop", f"f{n}_sheave", f"f{n}_spool") and not pid.endswith("_blade") or pid == f"f{n}_blade"}
        for rope_pid in (f"f{n}_tailrun", f"f{n}_taildrop", f"f{n}_sheave"):
            for pose in pose_list:
                mine = posed_part(rope_pid, pose)
                for pid in others:
                    if rope_pid == f"f{n}_taildrop" and pid == f"f{n}_slider":
                        continue                                  # it is tied to the guide block
                    for el in posed_part(pid, pose if pid != "frame" else (0.0, 0.0, 0.0, 1.0)):
                        if rope_pid == f"f{n}_sheave" and el.name == f"f{n}_tailpost_pin":
                            continue                              # the sheave turns on its pin
                        elo, ehi = el.aabb()
                        for m_el in mine:
                            mlo, mhi = m_el.aabb()
                            if all(mlo[k] < ehi[k] - 0.02 and elo[k] < mhi[k] - 0.02 for k in range(3)) and obb_obb(m_el, el):
                                hits.add((m_el.name, el.name))
        print(f"station {n}: guide block's rope leaves the spool's wrap at the tangent point, lies in the sheave's groove both sides"
              f" and ends on the guide block's eye, worst gap {max(gaps):.3f} voxels;"
              f" run, drop and sheave {'clear of everything' if not hits else 'TOUCH ' + str(sorted(hits)[:4])}")
        if max(gaps) > 0.05:
            fail(f"station {n}: the guide block's rope does not meet its spool, sheave and guide block")
        if hits:
            fail(f"station {n}: the guide block's rope or sheave runs into something")

    # south-end support: over the whole stroke (72 shaft angles), every depth, cutting and
    # lifting, either direction, the tail stays in the guide block's slot and clear of a 2x2
    # trunk, the guide block stays on its post, and both stay inside declared cells
    trunk_s = TRUNK_Z + 16.0
    for n in (1, 2):
        tail = [el for el in by_part[f"f{n}_saw"] if el.name.endswith("_tail")]
        post = [el for el in by_part["frame"] if re.match(rf"^f{n}_tailpost_\d+$", el.name) or el.name == f"f{n}_tailpost"]
        post_lo, post_hi = aabb_of(post)
        z_lo = z_hi = None
        worst = {"slot_n": 1e9, "slot_s": 1e9, "trunk": 1e9, "slot_y": 1e9, "slot_x": 1e9, "post": 1e9}
        cells_out = set()
        for depth in [k / 20 for k in range(21)]:
            for lifting in (0.0, 1.0):
                for travel in (0.0, 2.0):
                    ms = part_matrix(parts, f"f{n}_slider", 0.0, depth, lifting, travel)
                    slider = [posed(el, ms) for el in by_part[f"f{n}_slider"]]
                    s_lo, s_hi = aabb_of(slider)
                    dy = _apply(ms, [0.0, 0.0, 0.0])[1] * 16
                    slot_y = (SAW_TOP + TAIL_Y[0] - 0.1 + dy, SAW_TOP + TAIL_Y[1] + 0.1 + dy)
                    worst["post"] = min(worst["post"], s_lo[1] - post_lo[1], post_hi[1] - s_hi[1])
                    for i in range(72):
                        theta = i * math.pi / 36
                        mt = part_matrix(parts, f"f{n}_saw", theta, depth, lifting, travel)
                        t_lo, t_hi = aabb_of([posed(el, mt) for el in tail])
                        z_lo = t_lo[2] if z_lo is None else min(z_lo, t_lo[2])
                        z_hi = t_hi[2] if z_hi is None else max(z_hi, t_hi[2])
                        worst["slot_n"] = min(worst["slot_n"], t_lo[2] - SLOT_Z[0])
                        worst["slot_s"] = min(worst["slot_s"], SLOT_Z[1] - t_hi[2])
                        worst["trunk"] = min(worst["trunk"], t_lo[2] - trunk_s)
                        worst["slot_y"] = min(worst["slot_y"], t_lo[1] - slot_y[0], slot_y[1] - t_hi[1])
                        worst["slot_x"] = min(worst["slot_x"], t_lo[0] - (STATION_X[n - 1] - SLOT_DX), STATION_X[n - 1] + SLOT_DX - t_hi[0])
                        for lo, hi in [(t_lo, t_hi)] + ([(s_lo, s_hi)] if i == 0 else []):
                            for cx in range(int((lo[0] + 0.01) // 16), int(math.ceil((hi[0] - 0.01) / 16))):
                                for cy in range(int((lo[1] + 0.01) // 16), int(math.ceil((hi[1] - 0.01) / 16))):
                                    for cz in range(int((lo[2] + 0.01) // 16), int(math.ceil((hi[2] - 0.01) / 16))):
                                        if (cx, cy, cz) not in declared:
                                            cells_out.add((cx, cy, cz))
        print(f"station {n}: tail strokes over z {z_lo:.2f}..{z_hi:.2f} in the guide block's slot {SLOT_Z[0]:.2f}..{SLOT_Z[1]:.2f}"
              f" (margins {worst['slot_n']:.2f} north, {worst['slot_s']:.2f} south, {worst['slot_y']:.2f} in height, {worst['slot_x']:.2f} across);"
              f" {worst['trunk']:.2f} clear of a 2x2 trunk's south face; guide block {worst['post']:.2f} inside its post's ends;"
              f" {'inside declared cells' if not cells_out else 'OUTSIDE cells ' + str(sorted(cells_out))}")
        if min(worst["slot_n"], worst["slot_s"], worst["slot_y"], worst["slot_x"]) < 0.05:
            fail(f"station {n}: the tail leaves the guide block's slot")
        if worst["trunk"] < 0.05:
            fail(f"station {n}: the tail reaches a 2x2 trunk")
        if worst["post"] < 0.5:
            fail(f"station {n}: the guide block runs off its post")
        if cells_out:
            fail(f"station {n}: the tail or guide block leaves the declared cells")

    # levers: the trip rod's top stays pinned to the tappet arm's tip, the fork stays in the dog
    # clutch's groove, and the clutch's throw puts its dogs' face on the dog hub's
    def m(pid, depth, lifting=0.0):
        return part_matrix(parts, pid, 0.0, depth, lifting, 0.0)

    ry, rz = ROCK_PIVOT
    window = [k / 80 for k in range(81)]          # the whole travel, both ways (lifting 0 and 1)
    q = [TRIP_X * b, ry * b, TRIP_ZC * b]
    worst = max(16 * math.dist(_apply(m("trip", d, lf), q), _apply(m("rock", d, lf), q)) for d in window for lf in (0.0, 1.0))
    print(f"lever joint trip rod / tappet arm: worst gap {worst:.3f} voxels")
    if worst > 0.3:
        fail("the trip rod comes off the tappet arm")
    fork_pt = [GEAR_X * b, FORK_Y * b, DOG_GZ * b]
    worst_fork = max(16 * abs(_apply(m("rock", d, lf), fork_pt)[2] - _apply(m("dog", d, lf), fork_pt)[2]) for d in window for lf in (0.0, 1.0))
    axis_pt = [GEAR_X * b, SHAFT_Y * b, DOG_Z0 * b]
    face = _apply(m("dog", 1.0), axis_pt)[2] * 16                                         # the clutch's north face, thrown
    print(f"dog clutch: fork off the groove by at most {worst_fork:.2f} voxels; thrown, its face reaches {face:.2f}, the dog hub's face is {DOG_HUB_Z1:.2f}")
    if worst_fork > 0.15 or abs(face - DOG_HUB_Z1) > 0.05:
        fail("the rock shaft does not throw the dog clutch onto the dog hub")
    # the counterweight sits over centre: thrown in (at the bottom) or out (at the top), the lever
    # stays where it was put until the carriage's lug throws it the other way. Its lean (z from the
    # rock shaft's axis) changes sign across the throw: south holds it out, north holds it in.
    wy, wz = weight_centre()
    wt = [TRIP_X * b, wy * b, wz * b]
    lean_out = _apply(m("rock", 0.0), wt)[2] * 16 - rz
    lean_in = _apply(m("rock", 1.0), wt)[2] * 16 - rz
    lean_mid = _apply(m("rock", (lev["trip_from"] + 1) / 2), wt)[2] * 16 - rz
    print(f"rock shaft: turns {math.degrees(lev['rock']):.2f} deg; tappet arm {lev['tappet_arm']:.2f}, fork arm {lev['fork_arm']:.2f} voxels;"
          f" the over-centre weight leans {lean_out:+.2f} voxels (south) out, {lean_mid:+.2f} half way, {lean_in:+.2f} in")
    if not (lean_out > 0.2 and lean_in < -0.2 and abs(lean_mid) < 0.05):
        fail("the counterweight does not pass over the rock shaft between out and in")
    # the rectifier: whichever way the shaft turns, the disc turns the same way and one pinion turns
    # with the shaft; during a raise the drum pinion (and so the drum, clutched) turns at the rate
    # the gameplay raises the saws (RAISE_TURNS shaft turns from the bed to the top)
    ratios = {p["id"]: p["drivers"][0]["ratio"] for p in parts if p["id"] in ("pinion_w", "pinion_e", "crown")}
    crown_b_per_rad = -next(p for p in parts if p["id"] == "crown_b")["drivers"][0]["amount"] / (2 * math.pi * RAISE_TURNS)
    print(f"rectifier: per radian of shaft travel the west pinion turns {ratios['pinion_w']:+.4f} (with the shaft forwards), the east"
          f" {ratios['pinion_e']:+.4f} (with it backwards), the disc {ratios['crown']:+.4f} either way; in a raise the small crown gear's"
          f" half turns {crown_b_per_rad:+.4f} per shaft radian, so the clutched halves turn together")
    if ratios["pinion_w"] != 1.0 or ratios["pinion_e"] != -1.0 or abs(crown_b_per_rad - ratios["crown"]) > 1e-4:
        fail("the rectified gear train does not match the shaft or the raise")
    if abs(ratios["crown"] * GEAR_RATIO + 1.0) > 1e-4:
        fail("the gears' ratios do not mesh")
    # the top trip: rising, the lug's top meets the collar exactly when the trip starts to move
    # back, and lifts it the rest of the way to the top
    worst_lift = 0.0
    for k in range(41):
        depth = k / 40
        lug_top = SAW_TOP + LUG_Y[1] - SINK * depth
        collar = COLLAR_Y[0] + _apply(m("trip", depth, 1.0), [0.0, 0.0, 0.0])[1] * 16
        worst_lift = max(worst_lift, lug_top - collar)
        if depth <= lev["trip_top"] and abs(collar - lug_top) > 0.05:
            fail(f"the carriage's lug is off the collar at depth {depth:.3f} on the way up")
    print(f"top trip: rising, the carriage's lug meets the collar from depth {lev['trip_top']:.4f} up; worst overlap {max(worst_lift, 0):.3f} voxels")
    if worst_lift > 0.05:
        fail("the lug runs into the collar before it moves")
    lug_y = SAW_TOP + LUG_Y[0]
    worst_push = 0.0
    for k in range(41):
        depth = k / 40
        lug = lug_y - SINK * depth
        tappet = TAPPET_Y[1] + _apply(m("trip", depth), [0.0, 0.0, 0.0])[1] * 16
        worst_push = max(worst_push, tappet - lug)
        if depth >= lev["trip_from"] and abs(tappet - lug) > 0.05:
            fail(f"the carriage's lug is off the tappet at depth {depth:.3f}")
    print(f"trip: the carriage's lug meets the tappet from depth {lev['trip_from']:.4f}; worst overlap {max(worst_push, 0):.3f} voxels")
    # at depth 1 the pushrod reaches from the lug, at the bottom of the carriage's travel, up to the
    # tappet arm's tip: its foot under the lug, its top on the arm's pin
    lug_bottom = lug_y - SINK
    tap = TAPPET_Y[1] + _apply(m("trip", 1.0), [0.0, 0.0, 0.0])[1] * 16
    rod_top = _apply(m("trip", 1.0), [TRIP_X * b, ROCK_PIVOT[0] * b, TRIP_ZC * b])
    arm_tip = _apply(m("rock", 1.0), [TRIP_X * b, ROCK_PIVOT[0] * b, TRIP_ZC * b])
    lug_x = (STATION_X[0] + LUG_X[0], STATION_X[0] + LUG_X[1])
    under = TRIP_X - 0.5 <= lug_x[0] and lug_x[1] <= STATION_X[0] + LUG_X[1] + 0.25
    print(f"trip at depth 1: the pushrod runs from its tappet (top {tap:.2f}, the lug's underside {lug_bottom:.2f}, lug {'over' if under else 'OFF'} the tappet)"
          f" up to y {rod_top[1] * 16:.2f}, {16 * math.dist(rod_top, arm_tip):.2f} voxels from the tappet arm's pin")
    if abs(tap - lug_bottom) > 0.05 or not under or 16 * math.dist(rod_top, arm_tip) > 0.3:
        fail("at depth 1 the pushrod does not reach from the lug to the tappet arm")
    if worst_push > 0.05:
        fail("the lug runs into the tappet before it moves")

    # pairs of parts that must not touch (pin joints excepted)
    reports = []
    for n_ in (1, 2):
        for pa, fa, pb, fb in CLEARANCE_PAIRS:
            pa_, pb_ = pa.format(n=n_), pb.format(n=n_)
            if n_ == 2 and "{n}" not in pa and "{n}" not in pb:
                continue
            hits = set()
            for pose in pose_list[::3]:
                ga = [e for e in posed_part(pa_, pose) if not fa or re.search(fa, e.name)]
                gb = [e for e in posed_part(pb_, pose if pb_ != "frame" else (0.0, 0.0, 0.0, 1.0)) if not fb or re.search(fb, e.name)]
                bb = [(e, e.aabb()) for e in gb]
                for e in ga:
                    elo, ehi = e.aabb()
                    for f, (flo, fhi) in bb:
                        if all(elo[k] < fhi[k] - 0.02 and flo[k] < ehi[k] - 0.02 for k in range(3)) and obb_obb(e, f):
                            hits.add((e.name, f.name))
            if hits:
                reports.append(f"{pa_} x {pb_}: {len(hits)} contacts, e.g. {sorted(hits)[:3]}")
    print("clearances:", "all clear" if not reports else f"{len(reports)} pairs touch")
    for r in reports:
        print("  TOUCH", r)
    if reports:
        fail("moving parts run into each other")

    # textures and JSON
    for sh, label in ((shape, "full"), (frame_shape, "frame")):
        used = {f["texture"].lstrip("#") for e in sh["elements"] for f in e["faces"].values()}
        missing = used - set(sh["textures"])
        if missing:
            fail(f"{label} shape uses undeclared textures {missing}")
        print(f"{label} shape: {len(sh['elements'])} elements, textures used {sorted(used)}")
    print(f"linkage: rod {geo['length']:.2f} voxels at {math.degrees(geo['psi0']):.1f} deg, stroke +-{geo['stroke']:.3f},"
          f" swing +-{math.degrees(geo['swing']):.2f} deg; levers: rock shaft {math.degrees(lev['rock']):.2f} deg,"
          f" dog clutch throw {lev['dog']:.3f}")
    return ok


def shipped(els, parts, rig):
    """The build-frame model, rig and parts moved so ORIGIN_CELL is the controller cell [0,0,0]."""
    d = [-ORIGIN_CELL[k] * 16.0 for k in range(3)]
    db = [v / 16 for v in d]
    ship_els = copy.deepcopy(els)
    translate(ship_els, d)
    ship_parts = copy.deepcopy(parts)
    for p in ship_parts:
        for drv in p.get("drivers", []):
            for key in ("pivot", "anchor"):
                if key in drv:
                    drv[key] = [round(drv[key][k] + db[k], 6) for k in range(3)]
    ship = copy.deepcopy(rig)
    ship["cells"] = [{**c, "pos": [c["pos"][k] - ORIGIN_CELL[k] for k in range(3)]} for c in rig["cells"]]
    ship["powerCell"] = [rig["powerCell"][k] - ORIGIN_CELL[k] for k in range(3)]
    ship["output"] = {**rig["output"], "pos": [round(rig["output"]["pos"][k] + db[k], 4) for k in range(3)]}
    ship["trunkBed"] = {**rig["trunkBed"], "origin": [round(rig["trunkBed"]["origin"][k] + db[k], 4) for k in range(3)]}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def check_shipped(els, parts, ship_els, ship_parts, ship):
    """The shipped files are the checked model moved, nothing else: every element posed by the
    shipped rig lands where the checked one does, shifted, and the controller is a cell."""
    d = [-ORIGIN_CELL[k] * 16.0 for k in range(3)]
    worst = 0.0
    for pose in ((0.0, 0.0, 0.0, 0.0), (1.3, 0.5, 0.0, 4.0), (4.0, 0.97, 1.0, 9.0), (2.2, 0.03, 1.0, 2.2)):
        for a, b in zip(els, ship_els):
            pa, pb = posed(a, part_matrix(parts, a.part, *pose)), posed(b, part_matrix(ship_parts, b.part, *pose))
            worst = max(worst, max(abs(pa.c[k] + d[k] - pb.c[k]) for k in range(3)))
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    span = [(min(c[k] for c in cells), max(c[k] for c in cells)) for k in range(3)]
    print(f"shipped: moved by {[v / 16 for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells x {span[0]}, y {span[1]}, z {span[2]}; "
          f"power cell {ship['powerCell']} face {ship['powerFace']}; output {ship['output']['pos']} ({ship['outputSide']}); infeed {ship['infeedSide']}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


def main():
    ap = argparse.ArgumentParser(description="Generate the Bucking Sawmill's shapes and rig from Immersive Woodworking's sawmill.")
    ap.add_argument("--out", type=Path, help="write the two shapes and rig.json into this directory instead of the mod's assets")
    args = ap.parse_args()
    source, iw = load_iw()
    els = build(iw)
    parts = rig_parts()
    for el in els:  # tidy: drop near-zero rotation noise from IW's 89.99999 degree angles
        el.r = [[0.0 if abs(v) < 1e-7 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-7 else v) for v in row] for row in el.r]
    rig = make_rig(els, parts)
    shape = shape_json(els, source)
    frame_shape = shape_json([el for el in els if el.part == "frame"], source)
    ok = validate(els, parts, rig, shape, frame_shape)
    if args.out:
        outs = (args.out / "buckingmill.json", args.out / "buckingmill_frame.json", args.out / "rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "buckingmill.json", SHAPE_DIR / "buckingmill_frame.json", RIG_DIR / "rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els, source), shape_json([el for el in ship_els if el.part == "frame"], source)
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship), reference_dumps(reference_json(ship_parts)))
    for path, text in zip(outs, texts):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        json.loads(path.read_text())
        print(f"wrote {path} ({path.stat().st_size // 1024} KiB)")
    if not (ok and check_shipped(els, parts, ship_els, ship_parts, ship)):
        sys.exit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
