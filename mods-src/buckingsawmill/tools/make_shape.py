#!/usr/bin/env python3
"""Generate the Bucking Sawmill's shapes and rig from Immersive Woodworking's sawmill.

The bucking mill is an edited copy of the sawmill model from Immersive Woodworking (IW) by
Bobrik00, shipped with permission (see ../CREDITS.md). This script derives our model from
IW's: it reads `build/mods/immersivewoodworking_*.zip` and writes, deterministically,

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
  rail, turned on its side) hangs from the carriage's guide bars and strokes along z; IW's
  blade set, turned to cross-cut (normal x, long axis z, teeth down), is clamped in the head
  and cantilevered south across the trunk, as on a real drag saw.
* A slotted crosshead ("yoke"), a vertical bar sliding along z in fixed guides, carries the
  head's pin in its slot at every height. It is pushed by a rod from IW's crank on the
  shared shaft, so the stroke is driven at any depth.
* The windlass: IW's gear set at the west end. A sliding clutch collar beside the west one of
  IW's two loose pinions locks it to the shaft; both pinions mesh the peg ring of IW's crown
  disc (also the latch's ratchet wheel), whose axle runs north to IW's small crown gear, which
  drives a pinion on the drum shaft. The drum shaft runs along x above the carriages; one drum
  per station winds the rope that holds its carriage. No toothed wheel meshes nothing: the
  cranks are plain bars, and IW's toothed main rotor is left off.
* The levers: a trip rod down station 1's west post (the carriage knocks its tappet at the
  bottom of the cut), a bell crank, a link and a rock shaft that shift the clutch collar onto
  the winding pinion, and a pawl on the crown disc's pegs that latches the windlass at the top.
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
STATION_X = (32.0, 64.0)                     # blade planes of the two saws
SHAFT_Y, SHAFT_Z = 56.0, 24.0                # shared shaft (= centre of the west face of cell [0,3,1])
TRUNK_Z = 28.0                               # centre line of the bed (a 2-wide trunk spans z 12..44)
BED_TOP = 8.0                                # trunk rests on the rails at this height
SAW_TOP = 41.0                               # blades' cutting edge when latched (depth 0)
SAW_BOTTOM = 7.0                             # ... at the end of a cut (depth 1), below the bed top
SINK = SAW_TOP - SAW_BOTTOM                  # how far the carriage falls over a cut

# Stroke: crank -> rod -> yoke -> head pin. The rod pin sits on the yoke's top block.
CRANK_THROW = 3.0                            # IW's is 3.5
ROD_DX = 3.25                                # rod (and crank pin, yoke) plane east of the blade plane
ROD_PIN_Y = 48.25                            # rod's lower pin, on the yoke's top block
YOKE_DX = (2.5, 4.0)                         # yoke's x span east of the blade plane
YOKE_Y = (2.25, 49.0)                        # yoke's full height
YOKE_END = 1.75                              # height of its top and bottom blocks
YOKE_BAR = 0.75                              # thickness (z) of each side of the slot
PIN_D = 1.0                                  # slot width
PIN_FIT = 0.1                                # the pin is this much thinner than the slot (no coplanar faces)
GUIDE_GAP = 0.05                             # running clearance between the yoke and its guides

# Saw head (IW sash rail turned on its side) and blades. Head z at mid-stroke.
HEAD_Z = (3.5, 6.0)
HEAD_Y = (-0.5, 7.0)                         # relative to the blades' cutting edge
PIN_Z = (HEAD_Z[0] + HEAD_Z[1]) / 2
PIN_Y = 3.0                                  # head pin height above the cutting edge
BLADE_Z = (5.0, 44.0)                        # blade root (clamped in the head) to tip, mid-stroke
BLADE_SPACING = 0.8                          # IW spaces its 3 blades 2.5 apart; ours fit the head
GUIDE_DX = (1.25, 2.0)                       # carriage guide bars either side of the head
GUIDE_Y = (5.5, 7.0)                         # ... relative to the cutting edge

# Posts and carriage: IW's post pair and sash, unturned, squeezed to the north side.
POST_Z_MAP = [(-100.0, -100.0 * 0.5 + 10.5), (3.0, 9.0), (9.0, 12.0), (100.0, 57.5)]
POST_X_MAP = [(-100.0, -107.5), (-2.0, -9.5), (0.0, -7.5), (16.0, 7.5), (18.0, 9.5), (100.0, 91.5)]
POST_CAP_MAP = [(-100.0, -100.0), (53.0, 53.0), (59.0, 60.0), (100.0, 101.0)]
SOUTH_POST_PIVOT_Z = 28.5                    # the south posts are the north pair turned 180 about y
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
DRUM_Z = 12.25                               # drum shaft axis (y = SHAFT_Y, so it meshes like IW's pinions)
DRUM_SPIN = 2 * math.pi * RAISE_TURNS / GEAR_RATIO   # drum radians over a full sink or raise
DRUM_R = SINK / DRUM_SPIN                    # rope radius on the drum; the rope hangs at z = DRUM_Z - DRUM_R
DRUM_HALF = 1.5                              # drum half-length (x)
COLLAR_GROOVE = 0.6                          # half-width of the groove the shifter fork rides in
COLLAR_FLANGE = 0.6                          # thickness of each of the collar's flanges

# Levers (station 1's west side). Points are (x, y) at z LEVER_Z.
LEVER_Z = (7.5, 8.5)
TRIP_X = 21.0                                # trip rod centre line
TRIP_TRAVEL = 2.0                            # how far the carriage pushes it down
TAPPET_Y = (5.5, 6.5)
BELL_PIVOT = (15.0, 46.0)
BELL_ARM_B = 1.25                            # bell crank's down arm
ROCK_Y = 41.25                               # rock shaft along z (its x is the collar's, set below)
ROCK_Z = (7.5, 24.5)
FORK_CONTACT_Y = 54.0
LATCH_Y = SHAFT_Y + DISC_PEG_R               # the pawl drops between the disc's top two pegs
LATCH_PIVOT = (17.0, 22.25)                  # pawl pivot (x, z), axis y at LATCH_Y
LATCH_TIP = (GEAR_X, 21.0)                   # tip centre (x, z), among the pegs
LATCH_ANGLE = 0.3                            # pawl swings this far (rad) to clear the pegs
LATCH_WINDOW = 0.005                         # depth over which it swings out as the drop starts

# Bed
RAIL_Z = ((21.0, 23.0), (33.0, 35.0))        # rails sit under both 1x1 and 2x2 trunks
RAIL_GAP = 2.5                               # rails stop this far either side of a blade plane
SLEEPER_X = (10.0, 22.0, 42.0, 54.0, 74.0, 86.0)
TOP_BEAM_Y = (60.0, 62.0)

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
    """A bar from point a to point b in the plane normal to `axis` (a rod, an arm, a pawl): built
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
    """IW's post pair (posts, slats, caps, feet), unturned: the slots face each other across x
    and hold the carriage's stiles. North pair squeezed into z 9..12; the south pair is the
    same turned half round, without the sills, and only carries the top beams."""
    north = [el.clone() for el in iw if el.name in POST_KEEP]
    remap(north, 1, POST_CAP_MAP)
    remap(north, 0, POST_X_MAP)
    remap(north, 2, POST_Z_MAP)
    translate(north, (sx, 0.0, 0.0))
    south = [el.clone() for el in north if el.name not in POST_SILLS]
    rotate(south, "y", 180.0, (sx, 0.0, SOUTH_POST_PIVOT_Z))
    rename(north, f"f{n}_post_n_", "frame")
    rename(south, f"f{n}_post_s_", "frame")
    return north + south


def build_carriage(iw, n: int, sx: float):
    """IW's sash, unturned and squeezed to a short frame between the posts: its stiles slide in
    the post slots, the blades pass through its opening. Two guide bars run north from it to
    hang the saw head, and a lug at its west end knocks the trip rod's tappet."""
    sash = [el for el in pick(iw, r"^sash") if not SASH_DROP.match(el.name)]
    remap(sash, 1, [(a, SAW_TOP + b) for a, b in SASH_Y_MAP])
    remap(sash, 0, POST_X_MAP)
    remap(sash, 2, POST_Z_MAP)
    translate(sash, (sx, 0.0, 0.0))
    rename(sash, f"f{n}_carriage_", f"f{n}_carriage")
    t_bar = tpl(iw, "sash_022")
    north_face = min(el.aabb()[0][2] for el in sash)
    out = sash
    for side, (x0, x1) in (("w", (sx - GUIDE_DX[1], sx - GUIDE_DX[0])), ("e", (sx + GUIDE_DX[0], sx + GUIDE_DX[1]))):
        out.append(from_template(t_bar, [x0, SAW_TOP + GUIDE_Y[0], 0.25], [x1, SAW_TOP + GUIDE_Y[1], north_face + 0.25],
                                 f"f{n}_carriage_guide_{side}", f"f{n}_carriage"))
    out.append(from_template(t_bar, [sx + LUG_X[0], SAW_TOP + LUG_Y[0], LEVER_Z[0]], [sx + LUG_X[1], SAW_TOP + LUG_Y[1], north_face + 0.25],
                             f"f{n}_carriage_lug", f"f{n}_carriage"))
    return out


def build_saw(iw, n: int, sx: float):
    """The saw head: IW's sash bottom rail with its clamp bars and rivets, turned on its side
    (rail length -> up, rail height -> z, rail depth -> x), so the clamp bars grip the blades'
    faces; plus the pin that rides in the yoke's slot. The blades: IW's set turned 90 degrees
    about x (normal x, long axis z, teeth down), stretched to reach across the trunk, packed
    into the head and cantilevered south from it."""
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

    blades = pick(iw, r"^saw")
    rotate(blades, "x", 90.0, (0.0, 0.0, 0.0))     # IW y (length) -> z, IW z (teeth +z) -> -y
    lo, hi = aabb_of(blades)
    remap(blades, 2, [(lo[2], BLADE_Z[0]), (hi[2], BLADE_Z[1])])
    spread(blades, 0, 8.0, BLADE_SPACING / 2.5)
    lo, _ = aabb_of(blades)
    translate(blades, (sx - 8.0, SAW_TOP - lo[1], 0.0))
    rename(blades, f"f{n}_blade_", f"f{n}_blade")
    return head + [pin] + blades


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
    rod = strut(t_rod, a, b, 1.0, 1.0, f"f{n}_rod_bar", f"f{n}_rod")
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
    out = octagon(t_rope, sx - DRUM_HALF, sx + DRUM_HALF, SHAFT_Y, DRUM_Z, DRUM_R, f"drum{n}_core", "drum")
    for side, x in (("w", sx - DRUM_HALF - 0.25), ("e", sx + DRUM_HALF + 0.25)):
        out += octagon(t_flange, x - 0.25, x + 0.25, SHAFT_Y, DRUM_Z, DRUM_R + 0.75, f"drum{n}_flange_{side}", "drum")
    top = SHAFT_Y - DRUM_R
    bottom = carriage_top()
    rope_z = DRUM_Z - DRUM_R
    out.append(from_template(t_rope, [sx - 0.2, bottom, rope_z - 0.2], [sx + 0.2, top, rope_z + 0.2], f"f{n}_rope", f"f{n}_rope"))
    return out


def carriage_top():
    """Top of the carriage's top rail at depth 0 (where the rope is tied)."""
    return SAW_TOP + piecewise(SASH_Y_MAP)(30.0)


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
    # the disc's axle (IW's, two crossed bars) from the small crown gear to the disc
    axle = []
    _, c2_hi = aabb_of(crown2)
    d_lo, _ = aabb_of(disc)
    for name in ("Rotor_default_3_015", "Rotor_default_3_016"):
        t = translate([tpl(iw, name).clone()], (GEAR_X - IW_DISC_X, 0.0, 0.0))[0]
        lo, hi = t.aabb()
        axle += beam(t, [lo[0], lo[1], c2_hi[2] - 0.6], [hi[0], hi[1], d_lo[2] + 0.5], f"axle_{name[-3:]}", "", seg=10.0)
    rename(pin_w, "gear_pinion_w_", "pinion_w")
    rename(pin_e, "gear_pinion_e_", "pinion_e")
    rename(crown2, "gear_crown_b_", "crown")
    rename(disc, "gear_crown_disc_", "crown")
    rename(axle, "gear_crown_", "crown")
    rename(pin_d, "drum_pinion_", "drum")
    return pin_w + pin_e + build_collar(iw) + disc + axle + crown2 + pin_d


def build_collar(iw):
    """The sliding clutch sleeve keyed to the shaft between the two pinions: two sleeve halves
    and a narrower hub between them, the groove the shifter fork rides in. At full throw of the
    shifter one end of it meets a pinion's face: the west one for a shaft turning forwards, the
    east one for a shaft turning backwards."""
    t = tpl(iw, "MainRotor_twoway_021")
    cx = COLLAR_X
    out = [from_template(t, [cx - COLLAR_GROOVE, SHAFT_Y - 1.75, SHAFT_Z - 1.75], [cx + COLLAR_GROOVE, SHAFT_Y + 1.75, SHAFT_Z + 1.75],
                         "gear_clutch_hub", "clutch")]
    for side, (x0, x1) in (("w", (cx - COLLAR_HALF, cx - COLLAR_GROOVE)), ("e", (cx + COLLAR_GROOVE, cx + COLLAR_HALF))):
        out.append(from_template(t, [x0, SHAFT_Y - 2.25, SHAFT_Z - 2.25], [x1, SHAFT_Y + 2.25, SHAFT_Z + 2.25], f"gear_clutch_sleeve_{side}", "clutch"))
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

    fill(occupied_main, 0.0, max(b for _, b in occupied_main), SHAFT_Y, SHAFT_Z, "shaft", "shaft")
    fill(occupied_drum, min(a for a, _ in occupied_drum), end_x, SHAFT_Y, DRUM_Z, "drum_shaft", "drum")
    return out


def build_levers(iw):
    """The trip, the clutch shifter and the latch (station 1's west side and the gear set)."""
    t_oak, t_metal = tpl(iw, "sash_022"), tpl(iw, "leveler_metal_static_003")
    z0, z1 = LEVER_Z
    out = []
    # trip rod with its tappet, down the west post of station 1
    out.append(from_template(t_metal, [TRIP_X - 0.5, TAPPET_Y[1], z0], [TRIP_X + 0.5, BELL_PIVOT[1], z1], "lever_trip_rod", "trip"))
    out.append(from_template(t_oak, [TRIP_X - 0.5, TAPPET_Y[0], z0], [STATION_X[0] + LUG_X[1] + 0.25, TAPPET_Y[1], z1], "lever_trip_tappet", "trip"))
    # bell crank: arm A east to the trip rod's top, arm B down to the link
    bx, by = BELL_PIVOT
    out.append(from_template(t_oak, [bx - 0.5, by - 0.5, z0], [TRIP_X + 0.5, by + 0.5, z1], "lever_bell_arm_a", "bell"))
    out.append(from_template(t_oak, [bx - 0.5, by - BELL_ARM_B - 0.5, z0], [bx + 0.5, by, z1], "lever_bell_arm_b", "bell"))
    # link from the bell crank to the rock shaft's north lever (beside them, one step south)
    ly = by - BELL_ARM_B
    rx, ry = ROCK_PIVOT
    # the bell crank's pin rides in a slot in the link's east end, running west of the pin by
    # twice the pin's throw (lost motion: the bell crank only frees the shifter; which way the
    # shifter throws follows the shaft's direction)
    out.append(from_template(t_metal, [rx - 0.25, ly - 0.35, z1], [bx + 0.6, ly + 0.35, z1 + 0.75], "lever_link", "link"))
    # rock shaft along z with its north lever and the fork under the clutch collar
    out.append(from_template(t_oak, [rx - 0.5, ry - 0.5, ROCK_Z[0]], [rx + 0.5, ry + 0.5, ROCK_Z[1]], "lever_rock_shaft", "rock"))
    out.append(from_template(t_oak, [rx - 0.5, ry, z0], [rx + 0.5, ly + 0.5, z1], "lever_rock_lever", "rock"))
    fz0, fz1 = SHAFT_Z - 0.5, SHAFT_Z + 0.5
    out.append(from_template(t_oak, [rx - 0.5, ry, fz0], [rx + 0.5, FORK_CONTACT_Y - 1.5, fz1], "lever_rock_fork", "rock"))
    out.append(from_template(t_metal, [rx - 0.35, FORK_CONTACT_Y - 1.5, fz0 - 0.75], [rx + 0.35, FORK_CONTACT_Y + 0.4, fz1 + 0.75],
                             "lever_rock_fork_blade", "rock"))
    # latch pawl over the crown disc: an arm in front of the peg tips, its tip dipping between the top two pegs
    px, pz = LATCH_PIVOT
    tx, tz = LATCH_TIP
    out.append(from_template(t_oak, [tx - 0.5, LATCH_Y - 0.5, pz - 0.5], [px + 0.5, LATCH_Y + 0.5, pz + 0.5], "lever_latch_pawl", "latch"))
    out.append(from_template(t_metal, [tx - 0.5, LATCH_Y - 0.5, tz - 0.5], [tx + 0.5, LATCH_Y + 0.5, pz - 0.5], "lever_latch_tip", "latch"))
    return out


def build_frame(iw):
    """Top beams, shaft bearings, the input end, the west posts, the yoke guides, trip-rod straps,
    the pawl bracket and the bed."""
    t_beam, t_post, t_block = tpl(iw, "Frame.119"), tpl(iw, "Frame.011"), tpl(iw, "Frame.088")
    t_bar = tpl(iw, "sash_022")
    out = []
    for n, sx in enumerate(STATION_X, 1):
        for side, (x0, x1) in (("w", (sx - 9.5, sx - 7.5)), ("e", (sx + 7.5, sx + 9.5))):
            out += beam(t_beam, [x0, TOP_BEAM_Y[0], 9.0], [x1, TOP_BEAM_Y[1], 48.0], f"top_beam{n}{side}", "frame")
            out.append(from_template(t_block, [x0, SHAFT_Y - 2.0, SHAFT_Z - 2.0], [x1, TOP_BEAM_Y[0], SHAFT_Z + 2.0], f"bearing{n}{side}", "frame"))
        # yoke guides on the east post: top and bottom, along z, with a bracket to the post
        gx0, gx1 = sx + YOKE_DX[1] + GUIDE_GAP, sx + YOKE_DX[1] + 1.0
        for tag, (y0, y1) in (("top", (YOKE_Y[1] - YOKE_END, YOKE_Y[1])), ("foot", (YOKE_Y[0], YOKE_Y[0] + YOKE_END))):
            out.append(from_template(t_bar, [gx0, y0, 0.0], [gx1, y1, 9.0], f"f{n}_guide_{tag}", "frame"))
            out.append(from_template(t_bar, [gx1, y0, 8.0], [sx + 7.5, y1, 9.0], f"f{n}_guide_{tag}_bracket", "frame"))
    # trip-rod straps on station 1's west post
    for i, y in enumerate((18.0, 32.0), 1):
        out.append(from_template(tpl(iw, "leveler_metal_static_003"), [TRIP_X - 0.75, y, LEVER_Z[1]], [STATION_X[0] - 9.5, y + 1.0, 9.0],
                                 f"trip_strap{i}", "frame"))
    # input end: a post under the shaft and a bearing block around it
    out += beam(t_post, [0.5, 0.0, SHAFT_Z - 1.5], [3.5, SHAFT_Y - 2.0, SHAFT_Z + 1.5], "input_post", "frame")
    out.append(from_template(t_block, [0.5, SHAFT_Y - 2.0, SHAFT_Z - 2.0], [3.5, SHAFT_Y + 2.5, SHAFT_Z + 2.0], "input_bearing", "frame"))
    # west north post, head beam and drum-shaft bearing
    out += beam(t_post, [0.5, 0.0, 9.0], [3.5, TOP_BEAM_Y[0], 12.0], "west_post", "frame")
    out += beam(t_beam, [0.5, TOP_BEAM_Y[0], DRUM_Z - 1.5], [STATION_X[0] - 9.5, TOP_BEAM_Y[1], DRUM_Z + 1.5], "west_head_beam", "frame")
    out.append(from_template(t_block, [18.5, SHAFT_Y - 2.0, DRUM_Z - 1.5], [20.5, TOP_BEAM_Y[0], DRUM_Z + 1.5], "bearing_drum", "frame"))
    # rock-shaft bearings: from the west post and from the input post
    rx, ry = ROCK_PIVOT
    out.append(from_template(t_bar, [3.5, ry - 0.5, LEVER_Z[1]], [rx - 0.5, ry + 0.5, 9.5], "rock_bearing_n", "frame"))
    out.append(from_template(t_bar, [3.5, ry - 0.5, SHAFT_Z - 1.5], [rx - 0.5, ry + 0.5, SHAFT_Z - 0.5], "rock_bearing_s", "frame"))
    # pawl bracket hanging from station 1's west top beam
    px, pz = LATCH_PIVOT
    out.append(from_template(t_block, [px - 0.5, LATCH_Y + 0.5, pz - 0.5], [STATION_X[0] - 7.5, LATCH_Y + 1.5, pz + 0.5], "latch_bracket", "frame"))
    out += build_bed(iw)
    return out


def build_bed(iw):
    """Rails along x on sleepers, with gaps where the blades pass below the trunk."""
    tpl_rail, tpl_leg, tpl_cross = tpl(iw, "Frame.119"), tpl(iw, "Frame.102"), tpl(iw, "Frame.110")
    out = []
    edges = [4.0]
    for sx in STATION_X:
        edges += [sx - RAIL_GAP, sx + RAIL_GAP]
    edges.append(CELLS_X * 16 - 4.0)
    runs = list(zip(edges[0::2], edges[1::2]))
    for r, (z0, z1) in enumerate(RAIL_Z, 1):
        for s, (x0, x1) in enumerate(runs, 1):
            out += beam(tpl_rail, [x0, BED_TOP - 2.0, z0], [x1, BED_TOP, z1], f"bed_rail{r}_{s}", "frame")
    for i, sx in enumerate(SLEEPER_X, 1):
        out += beam(tpl_cross, [sx - 1.5, BED_TOP - 4.0, RAIL_Z[0][0] - 3.0], [sx + 1.5, BED_TOP - 2.0, RAIL_Z[1][1] + 3.0],
                    f"bed_sleeper{i}", "frame")
        for j, (z0, z1) in enumerate(((RAIL_Z[0][0] - 3.0, RAIL_Z[0][0]), (RAIL_Z[1][1], RAIL_Z[1][1] + 3.0)), 1):
            out.append(from_template(tpl_leg, [sx - 1.5, 0.0, z0], [sx + 1.5, BED_TOP - 4.0, z1], f"bed_leg{i}{j}", "frame"))
    return out


def build(iw):
    geo = linkage()
    els, crank_spans = [], []
    for n, sx in enumerate(STATION_X, 1):
        crank = build_crank(iw, n, sx, 180.0 if n == 1 else 0.0)
        lo, hi = aabb_of(crank)
        crank_spans.append((lo[0], hi[0]))
        els += build_posts(iw, n, sx) + build_carriage(iw, n, sx) + build_saw(iw, n, sx)
        els += build_yoke(iw, n, sx) + crank + build_rod(iw, n, sx, geo) + build_rope_and_drum(iw, n, sx)
    gears = build_gearbox(iw)
    els += gears
    main_occ = crank_spans
    pin_d = [e for e in gears if e.name.startswith("drum_pinion_")]
    drum_occ = [(aabb_of(pin_d)[0][0], aabb_of(pin_d)[1][0])]
    drum_occ += [(sx - DRUM_HALF - 0.5, sx + DRUM_HALF + 0.5) for sx in STATION_X]
    els += build_shafts(iw, main_occ, drum_occ, STATION_X[1] + 9.5)
    els += build_levers(iw)
    els += build_frame(iw)
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
    """The lever train's angles and travels, each from the one before, so the joints stay made."""
    arm_a = TRIP_X - BELL_PIVOT[0]
    bell = math.asin(TRIP_TRAVEL / arm_a)                     # bell crank turns clockwise (-z)
    link = BELL_ARM_B * math.sin(bell)                        # link moves west
    north = BELL_PIVOT[1] - BELL_ARM_B - ROCK_Y                # rock shaft's north lever
    rock = math.asin(link / north)                            # rock shaft turns anticlockwise (+z)
    fork = FORK_CONTACT_Y - ROCK_Y
    collar = fork * math.sin(rock)                            # collar moves west, onto the west pinion
    return {"bell": bell, "link": link, "rock": rock, "collar": collar, "trip_from": 1.0 - TRIP_TRAVEL / SINK,
            }


# The collar sits beside the west pinion so a full throw of the shifter puts it on the pinion's
# face; the rock shaft and its fork stand under the collar's groove.
PINION_W_FACE = GEAR_X - DISC_PEG_R - MESH_DX + PINION_FACE   # the pinions' inner faces (x)
PINION_E_FACE = GEAR_X + DISC_PEG_R + MESH_DX - PINION_FACE
COLLAR_X = (PINION_W_FACE + PINION_E_FACE) / 2
COLLAR_HALF = (PINION_E_FACE - PINION_W_FACE) / 2 - lever_geometry()["collar"]
ROCK_PIVOT = (COLLAR_X, ROCK_Y)


# ---------------------------------------------------------------- rig
def rig_parts():
    b = 1.0 / 16
    geo, lev = linkage(), lever_geometry()
    shaft_pivot = [0.0, SHAFT_Y * b, SHAFT_Z * b]
    spin = DRUM_SPIN                                           # drum radians over a full cut
    trip = {"from": round(lev["trip_from"], 6), "to": 1.0, "lifting": "hold"}

    def r6(v):
        return round(v, 6)

    parts = [
        {"id": "shaft", "match": ["shaft_*", "f1_crank_*", "f2_crank_*"], "requires": "crankshaft",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": shaft_pivot, "ratio": 1.0}]},
        {"id": "clutch", "match": ["gear_clutch*"], "requires": "crankshaft",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": shaft_pivot, "ratio": 1.0},
                     {"type": "step", "motion": "slide", "axis": "x", "amount": r6(-lev["collar"] * b), **trip, "reversible": True}]},
        {"id": "pinion_w", "match": ["gear_pinion_w_*"], "requires": "crankshaft",
         "drivers": [{"type": "step", "motion": "rotate", "axis": "x", "pivot": shaft_pivot, "amount": r6(-spin * GEAR_RATIO)}]},
        {"id": "pinion_e", "match": ["gear_pinion_e_*"], "requires": "crankshaft",
         "drivers": [{"type": "step", "motion": "rotate", "axis": "x", "pivot": shaft_pivot, "amount": r6(spin * GEAR_RATIO)}]},
        {"id": "crown", "match": ["gear_crown_*"], "requires": "crankshaft",
         "drivers": [{"type": "step", "motion": "rotate", "axis": "z", "pivot": [GEAR_X * b, SHAFT_Y * b, 0.0], "amount": r6(spin)}]},
        {"id": "drum", "match": ["drum*"], "requires": "crankshaft",
         "drivers": [{"type": "step", "motion": "rotate", "axis": "x", "pivot": [0.0, SHAFT_Y * b, DRUM_Z * b], "amount": r6(-spin)}]},
    ]
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
            {"id": f"f{n}_rope", "match": [f"f{n}_rope*"], "requires": f"sash{n}",
             "drivers": [{"type": "stretch", "axis": "y", "anchor": [r6(sx * b), r6((SHAFT_Y - DRUM_R) * b), r6((DRUM_Z - DRUM_R) * b)],
                          "length": r6(-(SHAFT_Y - DRUM_R - carriage_top()) * b), "travel": r6(-SINK * b)}]},
        ]
    bx, by = BELL_PIVOT
    rx, ry = ROCK_PIVOT
    zl = (LEVER_Z[0] + LEVER_Z[1]) / 2
    parts += [
        {"id": "trip", "match": ["lever_trip_*"], "requires": "levers",
         "drivers": [{"type": "step", "motion": "slide", "axis": "y", "amount": r6(-TRIP_TRAVEL * b), **trip}]},
        {"id": "bell", "match": ["lever_bell_*"], "requires": "levers",
         "drivers": [{"type": "step", "motion": "rotate", "axis": "z", "pivot": [bx * b, by * b, zl * b], "amount": r6(-lev["bell"]), **trip}]},
        {"id": "link", "match": ["lever_link*"], "requires": "levers",
         "drivers": [{"type": "step", "motion": "slide", "axis": "x", "amount": r6(-lev["link"] * b), **trip, "reversible": True}]},
        {"id": "rock", "match": ["lever_rock_*"], "requires": "levers",
         "drivers": [{"type": "step", "motion": "rotate", "axis": "z", "pivot": [rx * b, ry * b, zl * b], "amount": r6(lev["rock"]), **trip, "reversible": True}]},
        {"id": "latch", "match": ["lever_latch_*"], "requires": "levers",
         "drivers": [{"type": "step", "motion": "rotate", "axis": "y", "pivot": [LATCH_PIVOT[0] * b, LATCH_Y * b, LATCH_PIVOT[1] * b],
                      "amount": LATCH_ANGLE, "from": 0.0, "to": LATCH_WINDOW, "lifting": "block"}]},
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
#   depth    the saw's depth, 0 (latched at the top) .. 1 (at the bed, through the trunk)
#   lifting  1 while the saw is being wound back up, else 0 (a renderer may ease it)
#   direction  +1 while the shaft turns forwards (theta increasing), -1 backwards (may be eased);
#            a step driver with "reversible": true scales its throw by it
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
    return e


def driver_matrix(d, theta, depth, lifting=0.0, direction=1.0):
    axis = d["axis"]
    unit = [0.0, 0.0, 0.0]
    unit[AXES[axis]] = 1.0
    kind = d["type"]
    if kind == "rotate":
        return _about(rot(axis, math.degrees(d.get("ratio", 1.0) * theta)), d["pivot"])
    if kind == "swing":
        ang = d["amplitude"] * math.sin(d.get("ratio", 1.0) * theta + d.get("phase", 0.0))
        return _about(rot(axis, math.degrees(ang)), d["pivot"])
    if kind == "slide":
        return _m4(IDENT, [u * d["amplitude"] * math.sin(d.get("ratio", 1.0) * theta + d.get("phase", 0.0)) for u in unit])
    if kind == "feed":
        return _m4(IDENT, [u * d["travel"] * depth for u in unit])
    if kind == "step":
        e = step_amount(d, depth, lifting)
        if d.get("reversible"):
            e *= direction
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


def part_matrix(parts, pid, theta, depth, lifting=0.0, direction=1.0):
    """Drivers apply in list order to the authored geometry (pivots in the authored frame);
    then the `ride` part's whole transform is applied on top."""
    p = next(q for q in parts if q["id"] == pid)
    m = _m4()
    for d in p["drivers"]:
        m = _m4mul(driver_matrix(d, theta, depth, lifting, direction), m)
    if p.get("ride"):
        m = _m4mul(part_matrix(parts, p["ride"], theta, depth, lifting, direction), m)
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


REFERENCE_POSES = [(theta, depth, lifting, direction)
                   for theta in (0.0, 1.1, 2.9, 4.6)
                   for depth in (0.0, 0.002, 0.4, 0.96, 1.0)
                   for lifting in (0.0, 1.0)
                   for direction in (1.0, -1.0)] + [(0.7, 0.5, 0.5, 1.0), (5.9, 0.97, 0.25, -1.0), (-2.3, 0.003, 0.6, 0.3), (13.0, 0.75, 0.0, -0.6)]


def reference_json(parts):
    """Every part's matrix (3 rows of 4, block units) at a grid of poses, from this file's
    reference maths, for the C# tests to compare against so the two cannot drift."""
    poses = []
    for theta, depth, lifting, direction in REFERENCE_POSES:
        mats = {p["id"]: [[round(v, 6) for v in row] for row in part_matrix(parts, p["id"], theta, depth, lifting, direction)[:3]] for p in parts}
        poses.append({"theta": theta, "depth": depth, "lifting": lifting, "direction": direction, "matrices": mats})
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
        "_comment": f"Generated by mods-src/buckingsawmill/tools/make_shape.py from Immersive Woodworking's "
                    f"sawmill ({source}) by Bobrik00, used with permission. Keep element names when editing.",
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
                if boxes is not None or (x, y, z) == (0, 0, 0):
                    cells.append({"pos": [x, y, z], "boxes": boxes or []})
    return {
        "_comment": "Generated by mods-src/buckingsawmill/tools/make_shape.py. Native frame (south-facing), "
                    "block units, controller cell at [0,0,0]; see the mod's README for the schema.",
        "cells": cells,
        "powerCell": [0, int(SHAFT_Y // 16), int(SHAFT_Z // 16)],
        "powerFace": "west",
        "infeedSide": "north",
        "outputSide": "south",
        "output": {"pos": [CELLS_X / 2, round((BED_TOP + 2) * b, 4), CELLS_Z + 0.25]},
        "trunkBed": {"origin": [CELLS_X / 2, BED_TOP * b, TRUNK_Z * b], "axis": "x", "length": 5.0,
                     "_origin": "centre of the bed's top surface; trunks are drawn centred on it, lying along +x"},
        "saw": {"topY": SAW_TOP * b, "bottomY": SAW_BOTTOM * b,
                "_comment": "height of the blades' cutting edge at depth 0 (latched) and depth 1 (end of the cut)"},
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
    """(theta, depth, lifting, direction) samples: 8 shaft angles, five depths, cutting and
    lifting, the shaft turning either way."""
    out = []
    for i in range(8):
        for depth in (0.0, 0.25, 0.5, 0.75, 1.0):
            for lifting in (0.0, 1.0):
                out.append((i * math.pi / 4, depth, lifting, 1.0 if (i + int(depth * 4)) % 2 == 0 else -1.0))
    out += [(0.0, depth, 0.0, direction) for depth in (0.02, 0.9, 0.95, 0.98, 1.0) for direction in (1.0, -1.0)]
    return out


# Moving-part pairs that must not touch, besides the trunk and the frame (pin joints are
# allowed: a pair is checked only between the element-name patterns given).
CLEARANCE_PAIRS = [
    ("f{n}_rod", None, "f{n}_carriage", None), ("f{n}_rod", None, "drum", None), ("f{n}_rod", None, "frame", None),
    ("f{n}_yoke", None, "f{n}_carriage", None), ("f{n}_yoke", None, "frame", r"_guide_"),
    ("f{n}_saw", None, "f{n}_yoke", r"_(bar|head|foot)"), ("f{n}_saw", r"_pin$", "f{n}_carriage", None),
    ("f{n}_saw", None, "frame", None), ("f{n}_carriage", None, "frame", r"^(?!f\d_post_n_)"),
    ("f{n}_blade", None, "frame", r"bed_"), ("f{n}_blade", None, "f{n}_carriage", None),
    ("f{n}_rope", None, "f{n}_saw", None), ("f{n}_rope", None, "f{n}_yoke", None),
    ("trip", None, "f1_carriage", r"(?<!lug)$"), ("trip", None, "frame", r"bed_|post"), ("bell", None, "drum", None),
    ("link", None, "drum", None), ("rock", None, "crown", None), ("rock", None, "pinion_w", None), ("rock", None, "pinion_e", None), ("rock", None, "clutch", r"sleeve"),
    ("rock", None, "shaft", None), ("latch", None, "crown", r"_disc_"), ("latch", None, "shaft", None), ("latch", None, "pinion_e", None),
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

    # blades: clear of the trunk while latched, clear of the bed rails at the bottom
    for n in (1, 2):
        hits = set()
        for i in range(16):
            for el in posed_part(f"f{n}_blade", (i * math.pi / 8, 1.0, 0.0)):
                for rail in by_part["frame"]:
                    if rail.name.startswith("bed_") and obb_obb(el, rail):
                        hits.add(rail.name)
        lo = [8.0, BED_TOP, TRUNK_Z - 16]
        hi = [88.0, BED_TOP + 32, TRUNK_Z + 16]
        latched = any(obb_overlap(el, lo, hi) for i in range(16) for el in posed_part(f"f{n}_blade", (i * math.pi / 8, 0.0, 0.0)))
        print(f"blades {n}: at depth 1 {'clear of the bed' if not hits else 'HIT ' + ', '.join(sorted(hits))};"
              f" latched {'clear of a 2x2 trunk' if not latched else 'TOUCH a 2x2 trunk'}")
        if hits or latched:
            fail(f"blades {n} hit the bed or a latched trunk")

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
        worst_rope = 0.0
        rope = by_part[f"f{n}_rope"][0]
        for depth in (0.0, 0.25, 0.5, 0.75, 1.0):
            r = posed(rope, part_matrix(parts, f"f{n}_rope", 0.0, depth))
            lo, hi = r.aabb()
            tie = _apply(part_matrix(parts, f"f{n}_carriage", 0.0, depth), [sx * b, carriage_top() * b, 0.0])[1] * 16
            worst_rope = max(worst_rope, abs(hi[1] - (SHAFT_Y - DRUM_R)), abs(lo[1] - tie))
        print(f"station {n}: rope ends on the drum and the carriage's top rail, worst gap {worst_rope:.3f} voxels")
        if worst_rope > 0.05:
            fail(f"station {n} rope does not meet its drum and carriage")

    # levers: each joint stays made over the whole throw, the lug meets the tappet, the collar
    # reaches the west pinion
    def m(pid, depth, lifting=0.0, direction=1.0):
        return part_matrix(parts, pid, 0.0, depth, lifting, direction)

    zl = (LEVER_Z[0] + LEVER_Z[1]) / 2
    bx, by = BELL_PIVOT
    rx = ROCK_PIVOT[0]
    ly = by - BELL_ARM_B
    slot = 2 * BELL_ARM_B * math.sin(lev["bell"])
    window = [lev["trip_from"] + (1 - lev["trip_from"]) * k / 20 for k in range(21)]
    fork_pt = [COLLAR_X * b, FORK_CONTACT_Y * b, SHAFT_Z * b]
    for direction in (1.0, -1.0):
        tag = "forwards" if direction > 0 else "backwards"
        joints = [("trip rod / bell crank", "trip", "bell", [TRIP_X, by, zl]), ("link / rock lever", "link", "rock", [rx, ly, zl])]
        for label, p1, p2, pt in joints:
            q = [v * b for v in pt]
            worst = max(16 * math.dist(_apply(m(p1, d, 0, direction), q), _apply(m(p2, d, 0, direction), q)) for d in window)
            print(f"lever joint {label} ({tag}): worst gap {worst:.2f} voxels")
            if worst > 0.4:
                fail(f"lever joint {label} comes apart ({tag})")
        # the bell crank's pin rides in the link's slot, which runs west of its rest position by `slot`
        q = [bx * b, ly * b, zl * b]
        offs = [16 * (_apply(m("bell", d, 0, direction), q)[0] - _apply(m("link", d, 0, direction), q)[0]) for d in window]
        inside = all(-slot - 0.05 <= o <= 0.05 for o in offs)
        print(f"lever joint bell crank pin in the link's slot ({tag}): offset {min(offs):.2f}..{max(offs):.2f} of {-slot:.2f}..0")
        if not inside:
            fail(f"the bell crank's pin leaves the link's slot ({tag})")
        worst_fork = max(16 * abs(_apply(m("rock", d, 0, direction), fork_pt)[0] - _apply(m("clutch", d, 0, direction), fork_pt)[0]) for d in window)
        shift = (_apply(m("clutch", 1.0, 0, direction), fork_pt)[0] - COLLAR_X * b) * 16
        if direction > 0:
            face, target, name = COLLAR_X + shift - COLLAR_HALF, aabb_of(by_part["pinion_w"])[1][0], "west"
        else:
            face, target, name = COLLAR_X + shift + COLLAR_HALF, aabb_of(by_part["pinion_e"])[0][0], "east"
        print(f"clutch ({tag}): fork blade off the groove by at most {worst_fork:.2f} voxels; engaged sleeve end {face:.2f},"
              f" {name} pinion's face {target:.2f}")
        if worst_fork > 0.3 or abs(face - target) > 0.3:
            fail(f"the shifter does not put the sleeve on the {name} pinion ({tag})")
    # during a raise the depth falls 1/(RAISE_TURNS turns) per shaft radian, either way round; the
    # engaged pinion (west forwards, east backwards) must turn with the shaft, and the drum wind up
    per_rad = 1.0 / (2 * math.pi * RAISE_TURNS)
    amounts = {p["id"]: p["drivers"][0]["amount"] for p in parts if p["id"] in ("pinion_w", "pinion_e", "drum")}
    w_rate = -amounts["pinion_w"] * per_rad          # dθ > 0, depth falls
    e_rate = amounts["pinion_e"] * per_rad           # dθ < 0, depth falls: pinion turns by -amount·per_rad per -1 rad
    drum_up = amounts["drum"] < 0                    # depth falling turns the drum back the way that winds the rope in
    print(f"raise: engaged pinion turns {w_rate:.4f}x the shaft forwards (west) and {e_rate:.4f}x backwards (east);"
          f" the drum winds in as the depth falls: {drum_up}")
    if abs(w_rate - 1) > 1e-3 or abs(e_rate - 1) > 1e-3 or not drum_up:
        fail("the engaged pinion slips against the shaft, or the drum does not wind up")
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
                if (pa_, pb_) == ("latch", "crown") and pose[2] > 0:
                    continue          # while winding, the pawl clicks over the pegs
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
          f" swing +-{math.degrees(geo['swing']):.2f} deg; levers: bell {math.degrees(lev['bell']):.1f} deg, link {lev['link']:.3f},"
          f" rock {math.degrees(lev['rock']):.2f} deg, collar {lev['collar']:.3f}")
    return ok


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
    if args.out:
        outs = (args.out / "buckingmill.json", args.out / "buckingmill_frame.json", args.out / "rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "buckingmill.json", SHAPE_DIR / "buckingmill_frame.json", RIG_DIR / "rig.json", REFERENCE_OUT)
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(rig), reference_dumps(reference_json(parts)))
    for path, text in zip(outs, texts):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        json.loads(path.read_text())
        print(f"wrote {path} ({path.stat().st_size // 1024} KiB)")
    if not validate(els, parts, rig, shape, frame_shape):
        sys.exit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
