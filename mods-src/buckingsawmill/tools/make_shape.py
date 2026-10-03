#!/usr/bin/env python3
"""Generate the Bucking Sawmill's shapes and rig from Immersive Woodworking's sawmill.

The bucking mill is an edited copy of the sawmill model from Immersive Woodworking (IW) by
Bobrik00, shipped with permission (see ../CREDITS.md). This script derives our model from
IW's: it reads `build/mods/immersivewoodworking_*.zip` and writes, deterministically,

    assets/buckingsawmill/shapes/block/buckingmill.json        full machine, every moving part
    assets/buckingsawmill/shapes/block/buckingmill_frame.json  static frame only (block + item)
    assets/buckingsawmill/config/rig.json                      cells, anchors and part rig

Run it from anywhere with `python3 mods-src/buckingsawmill/tools/make_shape.py` after
`python3 tools/packtool.py fetch`. It validates its own output (see `validate`) and exits
non-zero if a check fails. Stdlib only.

How the model derives from IW's (all in voxels, 16 per block, native south-facing frame:
x = machine width, y up, z = depth, controller cell at the origin):

* IW's shape is flattened: every element (and child) is baked to a box with a world rotation
  matrix, and written back out as a top-level element with rotationOrigin at its centre.
* Saw frames: IW's two posts, their caps, feet and base sills (the "saw frame" proper), and
  its sash, are copied twice. The sash opening is widened across (`OPENING_WIDEN`) and the
  sash stretched in height (`SASH_HEIGHT_MAP`) so a 2x2 trunk fits with stroke clearance;
  then each copy is turned 90 degrees about y so its opening faces along x, and placed at
  `FRAME_X`.
* Blades: IW's 3-blade set keeps IW's orientation (that is: turned with the frame, then turned
  back, so the blade normal is x again and the blades cross-cut), stretched to the taller
  sash and packed into the sash's thickness. They feed across the trunk (+z) as the cut
  progresses.
* Drive: IW's crank (Rotor_default_4) keeps IW's orientation, so both cranks sit on one shared
  shaft along x at the top of the machine; its throw is shortened to `CRANK_THROW`. IW's
  crank rod (crankpit) is shortened to fit. IW's gear set (main rotor, both pinions and the
  crown disc) sits once at the west end, where the axle comes in.
* Dropped: IW's carriage, log, carriage springs and rope, the levers/ratchet train (it drove
  the carriage, hangs outboard of the post and hooks onto the IW-height sash), IW's bed table
  and the lever posts. The bed, top beams, bearings and supports are new elements built
  from IW's beam elements (their textures and UVs, cropped to length).
"""

from __future__ import annotations

import copy
import json
import math
import re
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
MOD = ROOT / "mods-src" / "buckingsawmill"
SHAPE_OUT = MOD / "assets" / "buckingsawmill" / "shapes" / "block" / "buckingmill.json"
FRAME_OUT = MOD / "assets" / "buckingsawmill" / "shapes" / "block" / "buckingmill_frame.json"
RIG_OUT = MOD / "assets" / "buckingsawmill" / "config" / "rig.json"
IW_SHAPE = "assets/immersivewoodworking/shapes/block/sawmill/sawmill.json"

# ---------------------------------------------------------------- placement (voxels)
CELLS_X, CELLS_Y, CELLS_Z = 6, 4, 3          # machine box: x 0..5, y 0..3, z 0..2 (blocks)
FRAME_X = (32.0, 64.0)                       # centre planes of the two saw frames
MID_Z = 24.0                                 # centre line of the bed, shaft and openings
BED_TOP = 8.0                                # trunk rests on the rails at this height
SHAFT_Y = 56.0                               # shared shaft axis (= centre of the west face of cell [0,3,1])
CRANK_THROW = 2.5                            # IW's is 3.5; shortened so the sash fits under the shaft
OPENING_WIDEN = 22.0                         # IW's sash opening is 16 wide; a 2x2 trunk is 32
BLADE_SPACING = 0.8                          # IW spaces its 3 blades 2.5 apart; ours fit the sash thickness
BLADE_PARK_Z = 5.2                           # north face of the blade bodies before the cut starts
BLADE_TRAVEL = 35.3                          # blades end just past the south face of a 2x2 trunk
ROD_PIVOT_Y = 47.0                           # crank rod's lower pin (centre of the crosshead) at mid-stroke
GEARBOX_DISC_X = 7.7                         # centre of IW's crown disc at the west end
RAIL_Z = ((17.0, 19.0), (29.0, 31.0))        # rails sit under both 1x1 and 2x2 trunks
RAIL_GAP = 6.0                               # rails stop this far either side of a frame plane
SLEEPER_X = (10.0, 22.0, 42.0, 54.0, 74.0, 86.0)
TOP_BEAM_Y = (60.0, 62.0)
TOP_BEAM_HALF_X = 6.5

# IW's sash, in IW's own coordinates (opening across x 0..16, y 7.5..27.5), maps to ours at
# mid-stroke: bottom bar top 7.5 -> 5.25 (BED_TOP - 0.25 - throw), top bar bottom 27.5 -> 43
# (a 32-tall trunk + 0.5 + throw), the yoke squeezed down onto the top bar.
SASH_HEIGHT_MAP = [(-100.0, -102.25), (8.0, 5.75), (27.0, 42.5), (30.0, 45.5), (34.0, 46.0), (100.0, 112.0)]
OPENING_WIDTH_MAP = [(-100.0, -100.0), (2.0, 2.0), (14.0, 14.0 + OPENING_WIDEN), (100.0, 100.0 + OPENING_WIDEN)]
IW_OPENING_MID_X = 8.0 + OPENING_WIDEN / 2   # centre of the widened opening, IW x
IW_FRAME_MID_Z = 6.0                         # IW's frame plane (posts z 3..9)
POST_CAP_MAP = [(-100.0, -100.0), (53.0, 53.0), (59.0, TOP_BEAM_Y[0]), (100.0, 101.0)]
ROD_HEIGHT_MAP = [(36.0, 48.0), (37.5, 49.5), (53.5, SHAFT_Y + 0.5)]
CRANK_THROW_MAP = [(52.0, 56.0 - CRANK_THROW - 1.0 + 0.0), (53.01, 56.0 - CRANK_THROW), (56.5, 56.5)]

TEXTURES = {"oak": "game:block/wood/debarked/oak", "metal": "game:block/metal/plate/iron"}
TEX_SIZE = 64

# IW elements kept as the saw frame proper: posts with their slats (011-018, 022-049), post tops and
# caps (084-089), small post brackets (092-094, 097, 259), feet (090, 091) and base sills.
FRAME_KEEP = {f"Frame.{n:03d}" for n in [*range(11, 19), *range(22, 50), *range(84, 95), 97, 259, 119, 120, 121, 251]}
# Lever bracket on the sash (the levers are dropped).
SASH_DROP = re.compile(r"^sash_081\.\d+$")

# Shapes the rig expects (also used in the trunk check), blocks: (length along x, width, height)
TRUNK_SIZES = {"xs": (1, 1, 1), "sm": (2, 1, 1), "md": (3, 1, 1), "lg": (4, 1, 1), "xl": (4, 2, 2), "xxl": (5, 2, 2)}


# ---------------------------------------------------------------- small 3x3 linear algebra
def mmul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def mvec(m, v):
    return [m[i][0] * v[0] + m[i][1] * v[1] + m[i][2] * v[2] for i in range(3)]


def transpose(m):
    return [list(r) for r in zip(*m)]


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


def turn_frame(els, frame_x: float):
    """Turn an IW-oriented frame 90 degrees about y (opening now faces along x) and place it:
    IW x (across the opening) -> our -z centred on MID_Z, IW z (feed) -> our x at frame_x."""
    rotate(els, "y", 90.0, (0.0, 0.0, 0.0))  # (x, y, z) -> (z, y, -x)
    return translate(els, (frame_x - IW_FRAME_MID_Z, 0.0, MID_Z + IW_OPENING_MID_X))


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


def build_saw_frame(iw, n: int, frame_x: float, crank_phase: float):
    """One saw frame: static frame, sash (with crosshead), crank rod, crank, blade set."""
    p = f"f{n}_"
    frame = [el.clone() for el in iw if el.name in FRAME_KEEP]
    remap(frame, 1, POST_CAP_MAP)
    remap(frame, 0, OPENING_WIDTH_MAP)
    turn_frame(frame, frame_x)
    rename(frame, p + "frame_", "frame")

    sash = [el for el in pick(iw, r"^(sash|crankpit$|crankpit_00[2-7]$)") if not SASH_DROP.match(el.name)]
    remap(sash, 1, SASH_HEIGHT_MAP)
    remap(sash, 0, OPENING_WIDTH_MAP)
    turn_frame(sash, frame_x)
    rename(sash, p + "sash_", f"f{n}_sash")

    rod = pick(iw, r"^crankpit_00[18]$")
    remap(rod, 1, ROD_HEIGHT_MAP)
    remap(rod, 0, OPENING_WIDTH_MAP)
    turn_frame(rod, frame_x)
    rename(rod, p + "rod_", f"f{n}_rod")

    crank = pick(iw, r"^Rotor_default_4")
    remap([el for el in crank if el.name in ("Rotor_default_4_002", "Rotor_default_4_003", "Rotor_default_4_004")],
          1, CRANK_THROW_MAP)
    translate(crank, (frame_x - 8.0, SHAFT_Y - 56.0, MID_Z - 6.0))
    rotate(crank, "x", crank_phase, (frame_x, SHAFT_Y, MID_Z))
    rename(crank, p + "crank_", "shaft")

    blades = pick(iw, r"^saw")
    remap(blades, 1, SASH_HEIGHT_MAP)
    spread(blades, 0, 8.0, BLADE_SPACING / 2.5)
    lo, _ = aabb_of(blades)
    translate(blades, (frame_x - 8.0, 0.0, BLADE_PARK_Z - lo[2]))
    rename(blades, p + "blade_", f"f{n}_blade")
    return frame + sash + rod + crank + blades


def build_gearbox(iw):
    """IW's gear set at the west end: main rotor and pinions on the shaft, crown disc beside it."""
    dx = GEARBOX_DISC_X - (-10.7)
    move = (dx, SHAFT_Y - 56.0, MID_Z - 8.0)
    main = translate(pick(iw, r"^(MainRotor_twoway|Rotor_default_2)"), move)
    idler = translate(pick(iw, r"^Rotor_default_1"), move)
    disc = translate(pick(iw, r"^Rotor_default_3"), move)
    rename(main, "gear_main_", "shaft")
    rename(idler, "gear_idler_", "gear_idler")
    rename(disc, "gear_disc_", "gear_disc")
    return main + idler + disc


def disc_centre(els):
    lo, hi = aabb_of([el for el in els if el.part == "gear_disc"])
    return [(lo[i] + hi[i]) / 2 for i in range(3)]


def aabb_of(els):
    boxes = [el.aabb() for el in els]
    return ([min(b[0][k] for b in boxes) for k in range(3)], [max(b[1][k] for b in boxes) for k in range(3)])


def build_shaft_and_supports(iw, gear_els, crank_els):
    """The shared shaft (IW's cross-section main-rotor shaft profile) between the gear set and
    the cranks, top beams and bearings over each frame, the disc's axle and post, and the
    bearing post at the input end."""
    out = []
    tpl_shaft_a = next(el for el in iw if el.name == "MainRotor_twoway_001")
    tpl_shaft_b = next(el for el in iw if el.name == "MainRotor_twoway_002")
    tpl_post = next(el for el in iw if el.name == "Frame.011")
    tpl_block = next(el for el in iw if el.name == "Frame.088")
    tpl_beam = next(el for el in iw if el.name == "Frame.119")

    # Gaps along the shaft line: from the input face to the gear set, gear set to crank 1, and so on.
    spans = []
    occupied = []
    for group in (gear_els, *crank_els):
        on_axis = [el for el in group if el.part == "shaft"]
        lo, hi = aabb_of(on_axis)
        occupied.append((lo[0], hi[0]))
    occupied.sort()
    x = 0.0
    for a, b in occupied:
        if a - x > 0.05:
            spans.append((x, a + 0.25))
        x = max(x, b - 0.25)
    for i, (a, b) in enumerate(spans):
        # profile: the main rotor's two crossed bars (1x3 and 3x1)
        for tpl, tag in ((tpl_shaft_a, "a"), (tpl_shaft_b, "b")):
            ylo, yhi = tpl.aabb()[0][1], tpl.aabb()[1][1]
            zlo, zhi = tpl.aabb()[0][2], tpl.aabb()[1][2]
            hy, hz = (yhi - ylo) / 2, (zhi - zlo) / 2
            out += beam(tpl, [a, SHAFT_Y - hy, MID_Z - hz], [b, SHAFT_Y + hy, MID_Z + hz],
                        f"shaft_{i + 1}{tag}", "shaft", seg=10.0)

    # Over each frame: two top beams across the post caps, each carrying a bearing on the shaft.
    for n, fx in enumerate(FRAME_X, 1):
        for side, (x0, x1) in (("w", (fx - TOP_BEAM_HALF_X, fx - 2.0)), ("e", (fx + 2.0, fx + TOP_BEAM_HALF_X))):
            out += beam(tpl_beam, [x0, TOP_BEAM_Y[0], 3.0], [x1, TOP_BEAM_Y[1], 45.0], f"top_beam{n}{side}", "frame")
        for side, (x0, x1) in (("w", (fx - 6.0, fx - 4.5)), ("e", (fx + 4.5, fx + 6.0))):
            out.append(from_template(tpl_block, [x0, SHAFT_Y - 2.0, MID_Z - 2.0], [x1, TOP_BEAM_Y[0], MID_Z + 2.0],
                                     f"bearing{n}{side}", "frame"))

    # Input end: a post under the shaft and a bearing block around it.
    out += beam(tpl_post, [0.5, 0.0, MID_Z - 1.5], [3.5, SHAFT_Y - 2.0, MID_Z + 1.5], "input_post", "frame")
    out.append(from_template(tpl_block, [0.5, SHAFT_Y - 2.0, MID_Z - 2.0], [3.5, SHAFT_Y + 2.5, MID_Z + 2.0],
                             "input_bearing", "frame"))

    # The crown disc's axle runs north to a post standing at the north edge.
    c = disc_centre(gear_els)
    disc_lo, _ = aabb_of([el for el in gear_els if el.part == "gear_disc"])
    out += beam(tpl_shaft_b, [c[0] - 0.5, c[1] - 0.5, 6.0], [c[0] + 0.5, c[1] + 0.5, disc_lo[2] + 0.5],
                "gear_disc_axle", "gear_disc", seg=10.0)
    out += beam(tpl_post, [c[0] - 1.5, 0.0, 3.0], [c[0] + 1.5, c[1] + 2.0, 6.0], "disc_post", "frame")
    return out


def build_bed(iw):
    """Rails along x on sleepers, with gaps where the sashes stroke through."""
    tpl_rail = next(el for el in iw if el.name == "Frame.119")
    tpl_leg = next(el for el in iw if el.name == "Frame.102")
    tpl_cross = next(el for el in iw if el.name == "Frame.110")
    out = []
    edges = [4.0]
    for fx in FRAME_X:
        edges += [fx - RAIL_GAP, fx + RAIL_GAP]
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
    els = []
    crank_groups = []
    for n, fx in enumerate(FRAME_X, 1):
        # The second crank runs half a turn behind the first, so the sashes balance.
        f = build_saw_frame(iw, n, fx, 0.0 if n == 1 else 180.0)
        crank_groups.append([el for el in f if el.name.startswith(f"f{n}_crank_")])
        els += f
    gears = build_gearbox(iw)
    els += gears
    els += build_shaft_and_supports(iw, gears, crank_groups)
    els += build_bed(iw)
    return els


# ---------------------------------------------------------------- rig
def rig_parts(els):
    b = 1.0 / 16
    c = disc_centre(els)
    r = CRANK_THROW
    lv = SHAFT_Y - ROD_PIVOT_Y
    amp = math.asin(r / lv)
    parts = [
        {"id": "shaft", "match": ["shaft_*", "f1_crank_*", "f2_crank_*", "gear_main_*"], "requires": "crankshaft",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": [0.0, SHAFT_Y * b, MID_Z * b], "ratio": 1.0}]},
        {"id": "gear_idler", "match": ["gear_idler_*"], "requires": "crankshaft",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": [0.0, SHAFT_Y * b, MID_Z * b], "ratio": -1.0}]},
        {"id": "gear_disc", "match": ["gear_disc_*"], "requires": "crankshaft",
         "drivers": [{"type": "rotate", "axis": "z", "pivot": [round(c[0] * b, 5), SHAFT_Y * b, round(c[2] * b, 5)], "ratio": -1.0}]},
    ]
    for n, fx in enumerate(FRAME_X, 1):
        # Crank 1's pin starts at the bottom (sash at its lowest), crank 2's at the top.
        sash_phase = -math.pi / 2 if n == 1 else math.pi / 2
        parts.append({"id": f"f{n}_sash", "match": [f"f{n}_sash_*"], "requires": f"sash{n}",
                      "drivers": [{"type": "slide", "axis": "y", "amplitude": r * b, "ratio": 1.0, "phase": round(sash_phase, 6)}]})
    for n, fx in enumerate(FRAME_X, 1):
        rod_phase = math.pi if n == 1 else 0.0
        parts.append({"id": f"f{n}_rod", "match": [f"f{n}_rod_*"], "requires": "crankshaft", "ride": f"f{n}_sash",
                      "drivers": [{"type": "swing", "axis": "x", "pivot": [fx * b, ROD_PIVOT_Y * b, MID_Z * b],
                                   "amplitude": round(amp, 6), "ratio": 1.0, "phase": round(rod_phase, 6)}]})
    for n, fx in enumerate(FRAME_X, 1):
        parts.append({"id": f"f{n}_blade", "match": [f"f{n}_blade_*"], "requires": f"blade{n}", "ride": f"f{n}_sash",
                      "drivers": [{"type": "feed", "axis": "z", "travel": round(BLADE_TRAVEL * b, 6)}]})
    parts.append({"id": "frame", "match": ["*"], "requires": None, "drivers": []})
    return parts


def glob_rx(pattern):
    return re.compile("^" + re.escape(pattern).replace(r"\*", ".*") + "$")


def part_of(parts, name):
    for p in parts:
        if any(glob_rx(g).match(name) for g in p["match"]):
            return p["id"]
    return None


# Rig evaluation (the semantics the renderer implements). Matrices are 4x4, block units.
def _m4(r=IDENT, t=(0.0, 0.0, 0.0)):
    return [[r[0][0], r[0][1], r[0][2], t[0]], [r[1][0], r[1][1], r[1][2], t[1]], [r[2][0], r[2][1], r[2][2], t[2]], [0, 0, 0, 1]]


def _m4mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def _about(r, pivot):
    t = [pivot[i] - mvec(r, pivot)[i] for i in range(3)]
    return _m4(r, t)


def driver_matrix(d, theta, progress):
    axis = d["axis"]
    unit = {"x": (1, 0, 0), "y": (0, 1, 0), "z": (0, 0, 1)}[axis]
    if d["type"] == "rotate":
        return _about(rot(axis, math.degrees(d["ratio"] * theta)), d["pivot"])
    if d["type"] == "swing":
        ang = d["amplitude"] * math.sin(d["ratio"] * theta + d["phase"])
        return _about(rot(axis, math.degrees(ang)), d["pivot"])
    if d["type"] == "slide":
        off = d["amplitude"] * math.sin(d["ratio"] * theta + d["phase"])
    elif d["type"] == "feed":
        off = d["travel"] * progress
    else:
        raise ValueError(d["type"])
    return _m4(IDENT, [u * off for u in unit])


def part_matrix(parts, pid, theta, progress):
    """Drivers apply in list order to the authored geometry (pivots in the authored frame);
    then the `ride` part's whole transform is applied on top."""
    p = next(q for q in parts if q["id"] == pid)
    m = _m4()
    for d in p["drivers"]:
        m = _m4mul(driver_matrix(d, theta, progress), m)
    if p.get("ride"):
        m = _m4mul(part_matrix(parts, p["ride"], theta, progress), m)
    return m


def _apply(m, p):
    return [m[i][0] * p[0] + m[i][1] * p[1] + m[i][2] * p[2] + m[i][3] for i in range(3)]


def posed(el: El, m) -> El:
    """`el` (voxels) moved by a part matrix (blocks)."""
    out = el.clone()
    r = [row[:3] for row in m[:3]]
    t = [m[i][3] * 16 for i in range(3)]
    out.c = [mvec(r, el.c)[i] + t[i] for i in range(3)]
    out.r = mmul(r, el.r)
    return out


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


def compact_dumps(obj, indent=0):
    """JSON with one element per line and short arrays inline."""
    pad = "\t" * indent
    if isinstance(obj, dict):
        if all(not isinstance(v, (dict, list)) or (isinstance(v, list) and all(not isinstance(x, (dict, list)) for x in v))
               for v in obj.values()) and len(json.dumps(obj)) < 140:
            return json.dumps(obj, separators=(", ", ": "))
        items = [f'{pad}\t{json.dumps(k)}: {compact_dumps(v, indent + 1)}' for k, v in obj.items()]
        return "{\n" + ",\n".join(items) + f"\n{pad}}}"
    if isinstance(obj, list):
        if all(not isinstance(x, (dict, list)) for x in obj):
            return json.dumps(obj, separators=(", ", ": "))
        items = [f"{pad}\t{compact_dumps(x, indent + 1)}" for x in obj]
        return "[\n" + ",\n".join(items) + f"\n{pad}]"
    return json.dumps(obj)


def shape_json(els, source):
    return {
        "_comment": f"Generated by mods-src/buckingsawmill/tools/make_shape.py from Immersive Woodworking's "
                    f"sawmill ({source}) by Bobrik00, used with permission. Keep element names when editing.",
        "textureWidth": TEX_SIZE, "textureHeight": TEX_SIZE,
        "textureSizes": {k: [TEX_SIZE, TEX_SIZE] for k in TEXTURES},
        "textures": dict(TEXTURES),
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
    cells = []
    for x in range(CELLS_X):
        for y in range(CELLS_Y):
            for z in range(CELLS_Z):
                boxes = cell_boxes(els, (x, y, z))
                if boxes is not None or (x, y, z) == (0, 0, 0):
                    cells.append({"pos": [x, y, z], "boxes": boxes or []})
    return {
        "_comment": "Generated by mods-src/buckingsawmill/tools/make_shape.py. Native frame (south-facing), "
                    "block units, controller cell at [0,0,0]; see the mod's README for the schema.",
        "cells": cells,
        "powerCell": [0, int(SHAFT_Y // 16), int(MID_Z // 16)],
        "powerFace": "west",
        "infeedSide": "north",
        "outputSide": "south",
        "output": {"pos": [CELLS_X / 2, round((BED_TOP + 2) * b, 4), CELLS_Z + 0.25]},
        "trunkBed": {"origin": [CELLS_X / 2, BED_TOP * b, MID_Z * b], "axis": "x", "length": 5.0,
                     "_origin": "centre of the bed's top surface; trunks are drawn centred on it, lying along +x"},
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
    eh = [abs(s) / 2 - eps for s in el.size]
    if min(eh) < 0:
        eh = [max(h, 0.0) for h in eh]
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


def poses():
    """(theta, progress) samples covering the stroke extremes, the rod swing and the feed."""
    out = []
    for i in range(8):
        for prog in (0.0, 0.5, 1.0):
            out.append((i * math.pi / 4, prog))
    return out


def validate(els, parts, rig, shape, frame_shape):
    ok = True

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
    if worst > 1e-4:  # outputs are rounded to 1e-4 degrees anyway
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

    # inside the declared cells, at rest and over the motion
    declared = {tuple(c["pos"]) for c in rig["cells"]}
    by_part = {}
    for el in els:
        by_part.setdefault(el.part, []).append(el)
    worst_out, worst_el = 0.0, None
    for pid, group in by_part.items():
        samples = poses() if pid != "frame" else [(0.0, 0.0)]
        for theta, prog in samples:
            m = part_matrix(parts, pid, theta, prog)
            for el in group:
                lo, hi = posed(el, m).aabb()
                for k, n in enumerate((CELLS_X, CELLS_Y, CELLS_Z)):
                    over = max(-lo[k], hi[k] - n * 16)
                    if over > worst_out:
                        worst_out, worst_el = over, (el.name, theta, prog)
                if (theta, prog) == (0.0, 0.0):
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

    # trunk envelopes against static and sash elements, over the whole stroke
    for size, (ln, w, h) in TRUNK_SIZES.items():
        lo = [CELLS_X * 8 - ln * 8, BED_TOP, MID_Z - w * 8]
        hi = [CELLS_X * 8 + ln * 8, BED_TOP + h * 16, MID_Z + w * 8]
        hits = set()
        for pid, group in by_part.items():
            if pid == "frame":
                samples = [(0.0, 0.0)]
            elif pid.endswith("_sash") or pid.endswith("_rod"):
                samples = poses()
            elif pid.endswith("_blade"):
                samples = [(t, 0.0) for t, _ in poses()]  # parked blades stay clear
            else:
                samples = poses()
            for theta, prog in samples:
                m = part_matrix(parts, pid, theta, prog)
                for el in group:
                    if obb_overlap(posed(el, m), lo, hi):
                        hits.add(el.name)
        print(f"trunk {size:>3} ({ln}x{w}x{h}): {'clear' if not hits else 'HITS ' + ', '.join(sorted(hits)[:6])}")
        if hits:
            fail(f"trunk {size} intersects the machine")

    # linkage: the rod's upper end follows its crank pin (the rig swings the rod rather than solving it)
    for n, fx in enumerate(FRAME_X, 1):
        pin_y = SHAFT_Y + (-CRANK_THROW if n == 1 else CRANK_THROW)
        worst = 0.0
        for i in range(64):
            theta = i * math.pi / 32
            top = _apply(part_matrix(parts, f"f{n}_rod", theta, 0.0), [fx / 16, SHAFT_Y / 16, MID_Z / 16])
            pin = _apply(part_matrix(parts, "shaft", theta, 0.0), [fx / 16, pin_y / 16, MID_Z / 16])
            worst = max(worst, 16 * math.dist(top, pin))
        print(f"frame {n} rod end to crank pin: worst gap {worst:.2f} voxels")
        if worst > 0.75:
            fail(f"frame {n} rod drifts off its crank pin")

    # textures and JSON
    for sh, label in ((shape, "full"), (frame_shape, "frame")):
        used = {f["texture"].lstrip("#") for e in sh["elements"] for f in e["faces"].values()}
        missing = used - set(sh["textures"])
        if missing:
            fail(f"{label} shape uses undeclared textures {missing}")
        print(f"{label} shape: {len(sh['elements'])} elements, textures used {sorted(used)}")
    return ok


def main():
    source, iw = load_iw()
    els = build(iw)
    parts = rig_parts(els)
    for el in els:  # tidy: drop near-zero rotation noise from IW's 89.99999 degree angles
        el.r = [[0.0 if abs(v) < 1e-7 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-7 else v) for v in row] for row in el.r]
    rig = make_rig(els, parts)
    shape = shape_json(els, source)
    frame_shape = shape_json([el for el in els if el.part == "frame"], source)
    for path, text in ((SHAPE_OUT, shape_dumps(shape)), (FRAME_OUT, shape_dumps(frame_shape)), (RIG_OUT, rig_dumps(rig))):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        json.loads(path.read_text())
        print(f"wrote {path.relative_to(ROOT)} ({path.stat().st_size // 1024} KiB)")
    if not validate(els, parts, rig, shape, frame_shape):
        sys.exit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
