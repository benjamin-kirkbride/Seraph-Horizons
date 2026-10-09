"""Pose maths for the eidolon's shape, as the game does it (Python 3.11 stdlib only).

The game poses a shape element by element (`ShapeElement.GetLocalTransformMatrix`, animation
version 0, which every shape here uses): an element's matrix is its parent's times

    T(rotationOrigin) · R(rotation + key rotation) · S(stretch) · T(from + key offset − rotationOrigin)

with lengths in blocks (voxels / 16) and R = Rx·Ry·Rz (`Mat4f.RotateByXYZ`), so a child's `from`
and `rotationOrigin` are measured from its parent's `from`. A key's offset is in voxels. Between
two keys the game interpolates each element's offset, rotation and stretch separately and
linearly (`Animation.GenerateFrameForElement`), each from the nearest keys that set it, wrapping
from the last key round to the first. An attachment point's frame is its element's matrix times
T(pos / 16) · R(rotation) (`EntityShapeRenderer.RenderItem`, `ClientAnimator`).

Everything here works in voxels at the model's own scale (1 voxel = 1/16 block at entity size 1).
"""

from __future__ import annotations

import math

GROUPS = {"off": ("offsetX", "offsetY", "offsetZ"),
          "rot": ("rotationX", "rotationY", "rotationZ"),
          "str": ("stretchX", "stretchY", "stretchZ")}
IDENTITY = ((1.0, 0.0, 0.0, 0.0), (0.0, 1.0, 0.0, 0.0), (0.0, 0.0, 1.0, 0.0), (0.0, 0.0, 0.0, 1.0))


def mul(a, b):
    return tuple(tuple(sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)) for i in range(4))


def translate(x, y, z):
    return ((1.0, 0.0, 0.0, x), (0.0, 1.0, 0.0, y), (0.0, 0.0, 1.0, z), (0.0, 0.0, 0.0, 1.0))


def scale(x, y, z):
    return ((x, 0.0, 0.0, 0.0), (0.0, y, 0.0, 0.0), (0.0, 0.0, z, 0.0), (0.0, 0.0, 0.0, 1.0))


def rot_xyz(dx, dy, dz):
    """R = Rx·Ry·Rz, degrees: what `Mat4f.RotateByXYZ` multiplies on."""
    x, y, z = (math.radians(v) for v in (dx, dy, dz))
    cx, sx, cy, sy, cz, sz = math.cos(x), math.sin(x), math.cos(y), math.sin(y), math.cos(z), math.sin(z)
    return ((cy * cz, -cy * sz, sy, 0.0),
            (sx * sy * cz + cx * sz, cx * cz - sx * sy * sz, -sx * cy, 0.0),
            (-cx * sy * cz + sx * sz, sx * cz + cx * sy * sz, cx * cy, 0.0),
            (0.0, 0.0, 0.0, 1.0))


def euler_xyz(m):
    """Degrees (x, y, z) with rot_xyz(x, y, z) == m's rotation (y kept within ±90)."""
    sy = max(-1.0, min(1.0, m[0][2]))
    y = math.asin(sy)
    if abs(sy) < 0.999999:
        x = math.atan2(-m[1][2], m[2][2])
        z = math.atan2(-m[0][1], m[0][0])
    else:  # gimbal lock: fold z into x
        x = math.atan2(m[2][1], m[1][1])
        z = 0.0
    return tuple(math.degrees(v) for v in (x, y, z))


def apply(m, p):
    """A point in voxels through a matrix whose lengths are blocks; the result in voxels."""
    q = (p[0] / 16, p[1] / 16, p[2] / 16, 1.0)
    return tuple(16 * sum(m[i][k] * q[k] for k in range(4)) for i in range(3))


def rotation_of(m):
    return tuple(tuple(m[i][j] for j in range(3)) for i in range(3))


def transpose3(r):
    return tuple(tuple(r[j][i] for j in range(3)) for i in range(3))


def mul3(a, b):
    return tuple(tuple(sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)) for i in range(3))


def as4(r, t=(0.0, 0.0, 0.0)):
    return tuple(tuple(r[i]) + (t[i],) for i in range(3)) + ((0.0, 0.0, 0.0, 1.0),)


def invert_rigid(m):
    """The inverse of a rotation-and-translation matrix (no stretch)."""
    rt = transpose3(rotation_of(m))
    t = tuple(m[i][3] for i in range(3))
    return as4(rt, tuple(-sum(rt[i][k] * t[k] for k in range(3)) for i in range(3)))


class Rig:
    """A shape's element tree, for posing."""

    def __init__(self, shape):
        self.elements = {}
        self.parent = {}
        self.order = []

        def walk(es, parent):
            for e in es:
                self.elements[e["name"]] = e
                self.parent[e["name"]] = parent
                self.order.append(e["name"])
                walk(e.get("children", []), e["name"])

        walk(shape["elements"], None)

    def chain(self, name):
        out = []
        while name is not None:
            out.append(name)
            name = self.parent[name]
        return out[::-1]

    def local(self, name, key=None):
        e = self.elements[name]
        key = key or {}
        ro = [v / 16 for v in e.get("rotationOrigin", (0, 0, 0))]
        r = key.get("rot", (0.0, 0.0, 0.0))
        o = key.get("off", (0.0, 0.0, 0.0))
        s = key.get("str", (1.0, 1.0, 1.0))
        m = translate(*ro)
        m = mul(m, rot_xyz(e.get("rotationX", 0) + r[0], e.get("rotationY", 0) + r[1], e.get("rotationZ", 0) + r[2]))
        m = mul(m, scale(s[0], s[1], s[2]))
        return mul(m, translate(*(e["from"][i] / 16 + o[i] / 16 - ro[i] for i in range(3))))

    def matrix(self, name, pose):
        """The element's model matrix in a pose ({element: {"rot"|"off"|"str": (x, y, z)}})."""
        m = IDENTITY
        for n in self.chain(name):
            m = mul(m, self.local(n, pose.get(n)))
        return m

    def all_matrices(self, pose):
        out = {}

        def walk(names, pm):
            for n in names:
                m = mul(pm, self.local(n, pose.get(n)))
                out[n] = m
                walk([c["name"] for c in self.elements[n].get("children", [])], m)

        walk([n for n in self.order if self.parent[n] is None], IDENTITY)
        return out

    def corners(self, name, m):
        e = self.elements[name]
        size = [e["to"][i] - e["from"][i] for i in range(3)]
        return [apply(m, (a * size[0], b * size[1], c * size[2])) for a in (0, 1) for b in (0, 1) for c in (0, 1)]

    def centre(self, name, m):
        e = self.elements[name]
        return apply(m, tuple((e["to"][i] - e["from"][i]) / 2 for i in range(3)))

    def point(self, name, local_voxels, pose):
        """A point given in the element's own voxels (from its `from`), in model voxels."""
        return apply(self.matrix(name, pose), local_voxels)

    def attachment(self, name, ap, pose):
        """An attachment point's frame (4×4, blocks) in a pose."""
        m = self.matrix(name, pose)
        m = mul(m, translate(float(ap["posX"]) / 16, float(ap["posY"]) / 16, float(ap["posZ"]) / 16))
        return mul(m, rot_xyz(float(ap["rotationX"]), float(ap["rotationY"]), float(ap["rotationZ"])))


def key_of(elem_json):
    """An animation key's element entry as {"rot"|"off"|"str": (x, y, z)} (groups it sets fully)."""
    out = {}
    for g, names in GROUPS.items():
        if any(elem_json.get(n) is not None for n in names):
            out[g] = tuple(float(elem_json.get(n) or 0.0) for n in names)
    return out


def sample(anim, frame):
    """The pose the game computes at an integer frame of an animation (the shape's JSON form)."""
    keys = anim["keyframes"]
    q = anim["quantityframes"]
    names = {n for k in keys for n in k["elements"]}
    pose = {}
    for n in names:
        entry = {}
        for g in GROUPS:
            setters = [i for i, k in enumerate(keys) if g in key_of(k["elements"].get(n, {}))]
            if not setters:
                continue
            right = next((i for i in setters if keys[i]["frame"] > frame), setters[0])
            pos = setters.index(right)
            left = setters[pos - 1] if len(setters) > 1 else right
            lf, rf = keys[left]["frame"], keys[right]["frame"]
            lv, rv = key_of(keys[left]["elements"][n])[g], key_of(keys[right]["elements"][n])[g]
            if left == right:
                t = 0.0
            elif rf < lf:
                t = ((frame - lf) % q) / (rf + (q - lf))
            else:
                t = (frame - lf) / (rf - lf)
            entry[g] = tuple(lv[i] + (rv[i] - lv[i]) * t for i in range(3))
        pose[n] = entry
    return pose


def solve(cost, start, bounds, steps=(16.0, 8.0, 4.0, 2.0, 1.0, 0.5, 0.25, 0.1, 0.05, 0.02)):
    """Deterministic coordinate descent: the vector within bounds that (locally) minimises cost."""
    x = list(start)
    best = cost(x)
    for step in steps:
        improved = True
        rounds = 0
        while improved and rounds < 200:
            improved = False
            rounds += 1
            for i in range(len(x)):
                for d in (step, -step):
                    v = min(bounds[i][1], max(bounds[i][0], x[i] + d))
                    if v == x[i]:
                        continue
                    old = x[i]
                    x[i] = v
                    c = cost(x)
                    if c < best - 1e-12:
                        best = c
                        improved = True
                        break
                    x[i] = old
    return x, best


def dist(a, b):
    return math.sqrt(sum((a[i] - b[i]) ** 2 for i in range(3)))
