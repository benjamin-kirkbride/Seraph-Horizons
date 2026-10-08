"""Boxes and how to build with them: the machine-free half of a model generator.

Moved verbatim from the bucking sawmill's generator (BuckingSawmill/tools/make_shape.py), so its
output stays byte-identical; a value the mill read from a global (the texture size, IW's numeric
texture code) is now an argument whose default is the mill's.

Coordinates are voxels (16 per block). An `El` is a box of `size` along its local axes, turned by
the rotation matrix `r` about its centre `c`, in world voxels.
"""

from __future__ import annotations

import copy
import math
import re

TEX_SIZE = 64                                # the mill's textures are 64x64; scale_uv and metal default to it


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
TRANSPARENT = 3                              # EnumChunkRenderPass.Transparent: a glass element's pass in an opaque block


class El:
    """A box of `size` (local axes), rotated by `r` about its centre `c` (world voxels)."""

    render_pass = None                       # the element's own chunk render pass (TRANSPARENT for glass), else the block's

    def __init__(self, name, size, c, r, faces, part):
        self.name, self.size, self.c, self.r, self.faces, self.part = name, list(size), list(c), r, faces, part

    def clone(self, name=None, part=None):
        out = El(name or self.name, self.size, self.c, [row[:] for row in self.r],
                 copy.deepcopy(self.faces), part or self.part)
        out.render_pass = self.render_pass
        return out

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


IW_TEXTURES = {"#0": "#oak"}                 # IW's blocktype maps its texture "0" to debarked oak


def flatten(elements, parent_r=IDENT, parent_t=(0.0, 0.0, 0.0), textures=IW_TEXTURES):
    """Bake VS's hierarchy (child coordinates are relative to the parent's `from`). Face texture
    codes found in `textures` are renamed to its value."""
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
            if face.get("texture") in textures:
                face["texture"] = textures[face["texture"]]
        out.append(El(e["name"], [to[i] - f[i] for i in range(3)], c, r, faces, None))
        if e.get("children"):
            ct = [t[i] + mvec(r, f)[i] for i in range(3)]
            out += flatten(e["children"], r, ct, textures)
    return out


def pick(src, pattern):
    """Clones of the elements of `src` whose names match the regex `pattern` (from the start)."""
    rx = re.compile(pattern)
    return [el.clone() for el in src if rx.match(el.name)]


def tpl(src, name):
    """The element of `src` named `name` (a template; clone it before changing it)."""
    return next(el for el in src if el.name == name)


def aabb_of(els):
    boxes = [el.aabb() for el in els]
    return ([min(b[0][k] for b in boxes) for k in range(3)], [max(b[1][k] for b in boxes) for k in range(3)])


# ---------------------------------------------------------------- UVs
# Which local axis each face's u and v run along (before the face's own rotation).
FACE_UV_AXES = {"north": (0, 1), "south": (0, 1), "east": (2, 1), "west": (2, 1), "up": (0, 2), "down": (0, 2)}


def scale_uv(el: El, k: int, ratio: float, tex_size: float = TEX_SIZE):
    """Crop (ratio < 1) or extend (ratio > 1, clamped to the texture) the UVs along local axis k."""
    for d, face in el.faces.items():
        if "uv" not in face:
            continue
        u_axis, v_axis = FACE_UV_AXES[d]
        if face.get("rotation", 0) in (90, 270):
            u_axis, v_axis = v_axis, u_axis
        if k not in (u_axis, v_axis):
            continue
        uv = list(face["uv"])
        i0, i1 = (0, 2) if k == u_axis else (1, 3)
        span = uv[i1] - uv[i0]
        new = span * ratio
        if abs(new) > tex_size:
            new = math.copysign(tex_size, new)
        lo, hi = sorted((uv[i0], uv[i0] + new))
        shift = -lo if lo < 0 else (tex_size - hi if hi > tex_size else 0.0)
        uv[i0] += shift
        uv[i1] = uv[i0] + new
        face["uv"] = uv


def metal(el: El, tex_size: float = TEX_SIZE, texture: str = "#metal"):
    """Make an element iron: every face takes the `metal` texture, its UVs a region of the plate
    texture in proportion to the face's size (tex_size / 16 texels per voxel), unrotated."""
    for d, face in el.faces.items():
        u, v = FACE_UV_AXES[d]
        w = min(abs(el.size[u]) * tex_size / 16, tex_size)
        h = min(abs(el.size[v]) * tex_size / 16, tex_size)
        el.faces[d] = {"texture": texture, "uv": [0.0, 0.0, w, h]}
    return el


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
