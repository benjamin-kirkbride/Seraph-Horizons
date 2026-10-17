"""The liquation furnace generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its rule.
Most are the crucible furnace's (CrucibleFurnace/tools/validate_cruciblefurnace.py), its conventions being this
furnace's; the plate, the spout and the pot, the vault, the fire door and the sideways damper are this furnace's own.
Everything is in the build frame (voxels), which here is the shipped frame (the controller is the box's corner cell);
`check_shipped` in make_shape.py proves the shipped files are this model.
"""

from __future__ import annotations

import math
import os
import re
from pathlib import Path

from machinegen.checks import coplanar_faces, euler_round_trip, frame_floating, obb_obb
from machinegen.geometry import aabb_of
from machinegen.rigmath import part_of, posed


class V:
    def __init__(self, m, els, parts, rig):
        self.m, self.els, self.parts, self.rig = m, els, parts, rig
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        self.cache = {}
        self.ok = True

    def fail(self, msg):
        self.ok = False
        print("FAIL", msg)

    def posed(self, pid, depth, theta=0.0):
        key = (pid, round(depth, 9), theta)
        if key not in self.cache:
            if len(self.cache) > 20000:
                self.cache.clear()
            mm = self.m.pm(self.parts, pid, (theta, depth, None))
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def shown(self, scene, depth):
        """Every element drawn in `scene`, at `depth`."""
        return [e for pid in self.by_part if self.m.on_show(pid, scene) for e in self.posed(pid, depth)]


def inside(el, p, eps=0.0):
    """Whether point p lies inside element el (its box, turned), by more than eps."""
    d = [p[k] - el.c[k] for k in range(3)]
    for k in range(3):
        axis = [el.r[i][k] for i in range(3)]
        if abs(sum(d[i] * axis[i] for i in range(3))) > abs(el.size[k]) / 2 - eps:
            return False
    return True


def depths(step):
    n = int(round(1 / step))
    return [i * step for i in range(n + 1)]


def same_box(a, b, tol=1e-6):
    return max(abs(a[0][k] - b[0][k]) for k in range(3)) < tol and max(abs(a[1][k] - b[1][k]) for k in range(3)) < tol


def brick(v):
    """Every brick element, the brickwork's and the lining's (the vault's staves and backing with it)."""
    return v.by_part["brickwork"] + v.by_part["lining"]


def static(v):
    """Everything that stands still and is not the contents, the furnace built: the iron frame, the brickwork, the
    lining, the plate and the pot (the door moves)."""
    return v.by_part["frame"] + v.by_part["brickwork"] + v.by_part["lining"] + v.by_part["plate"] + v.by_part["pot"]


def box_corners(e):
    out = []
    for sx in (-0.5, 0.5):
        for sy in (-0.5, 0.5):
            for sz in (-0.5, 0.5):
                loc = (sx * e.size[0], sy * e.size[1], sz * e.size[2])
                out.append([e.c[k] + sum(e.r[k][q] * loc[q] for q in range(3)) for k in range(3)])
    return out


THIN_POST = 1.5                              # a post: a vertical element under this across both ways (an angle's leg is wider)


# ---------------------------------------------------------------- parts, frame, textures
def check_basic(v):
    worst = euler_round_trip(v.els)
    print(f"euler round trip: worst {worst:.1e}")
    if worst > 1e-9:
        v.fail("an element's rotation does not survive being written as Euler angles")
    names = [el.name for el in v.els]
    if len(names) != len(set(names)):
        v.fail("duplicate element names")
    wrong = [(el.name, el.part, part_of(v.parts, el.name)) for el in v.els if part_of(v.parts, el.name) != el.part]
    if wrong:
        v.fail(f"{len(wrong)} elements land in the wrong part, e.g. {wrong[:4]}")
    empty = [p["id"] for p in v.parts if not v.by_part.get(p["id"])]
    if empty:
        v.fail(f"parts with no elements: {empty}")
    counts = {p["id"]: len(v.by_part.get(p["id"], [])) for p in v.parts}
    print(f"parts: {len(v.parts)}, elements {len(v.els)}; the iron frame {counts['frame']}; "
          + ", ".join(f"{p} {n}" for p, n in counts.items() if p != "frame"))


def check_stages(v):
    """The build: at each stage (the iron frame alone, with the brickwork, with the lining, with the fittings) nothing
    floats: every element drawn is joined to the ground. The iron frame stands on its own: it is iron only, and no
    thin post stands in it taller than half the furnace. Every tie is hidden once the brickwork is in: points just off
    every face of it lie in brick or in other iron."""
    m = v.m
    out = []
    for sid, fitted in m.STAGE_STATES:
        drawn = [e for p in v.parts if p["requires"] is None or (p["requires"] in fitted and not p["drivers"])
                 for e in v.by_part[p["id"]]]
        if "fittings" in fitted:
            drawn += v.posed("door", m.SHUT)
        seen, loose = frame_floating(drawn)
        out.append(f"{sid} {len(seen)}")
        if loose:
            v.fail(f"at the {sid} stage, elements float: {loose[:8]}")
    frame = v.by_part["frame"]
    tex = {f["texture"].lstrip("#") for e in frame for f in e.faces.values()}
    if tex != {"iron"}:
        v.fail(f"the iron frame is not iron only: {sorted(tex)}")
    posts, tallest = [], 0.0
    for e in frame:
        lo, hi = e.aabb()
        if hi[0] - lo[0] < THIN_POST and hi[2] - lo[2] < THIN_POST:
            tallest = max(tallest, hi[1] - lo[1])
            if hi[1] - lo[1] > m.H / 2:
                posts.append(e.name)
    if posts:
        v.fail(f"thin posts stand in the iron frame: {posts}")
    ties = [e for e in frame if e.name.startswith("fr_tie")]
    others = [e for e in frame if not e.name.startswith("fr_tie")]
    bk = v.by_part["brickwork"]
    bare = 0
    for t in ties:
        rest = [e for e in frame if e is not t]
        lo, hi = t.aabb()
        for k in range(3):
            for side in (lo[k] - 0.02, hi[k] + 0.02):
                a, b = [q for q in range(3) if q != k]
                for fa in (0.1, 0.5, 0.9):
                    for fb in (0.1, 0.5, 0.9):
                        pnt = [0.0, 0.0, 0.0]
                        pnt[k] = side
                        pnt[a] = lo[a] + (hi[a] - lo[a]) * fa
                        pnt[b] = lo[b] + (hi[b] - lo[b]) * fb
                        if pnt[1] < 0.0 or any(inside(e, pnt) for e in rest):
                            continue
                        if not any(inside(e, pnt) for e in bk):
                            bare += 1
    print(f"stages: nothing floats at any stage (elements joined to the ground: {', '.join(out)}); the iron frame iron "
          f"only, with no thin post over {m.H / 2:.0f} (its tallest thin piece {tallest:.1f}); {len(ties)} ties hidden "
          f"in the brickwork ({bare} points off their faces bare); {len(others)} other pieces of iron")
    if bare:
        v.fail(f"{bare} points off the ties' faces are in no brickwork or iron: a tie shows")


ROLES = [
    (r"^bk_(brick_|ring\d|ringbacking)", {"brick"}),
    (r"^ln_(brick_|vault\d|backing)", {"lining"}),
    (r"^fr_(bearer|bar|platebearer|jamb|doorlintel|ashlintel|guide|rail|angle|band|tie)", {"iron"}),
    (r"^plate_", {"iron"}),
    (r"^pot_", {"iron"}),
    (r"^door_", {"iron"}),
    (r"^cakes_", {"cake"}),
    (r"^tinrun_", {"tin"}),
    (r"^tin_", {"tin"}),
    (r"^lead_", {"lead"}),
    (r"^fuel\d_", {"fuel"}),
    (r"^fire\d_", {"ember"}),
    (r"^damper(open|half|shut)_", {"iron"}),
    (r"^outletcap_", {"iron"}),
]


def check_textures(v):
    bad = []
    for el in v.els:
        tex = {f["texture"].lstrip("#") for f in el.faces.values()}
        rule = next((want for rx, want in ROLES if re.match(rx, el.name)), None)
        if rule is None or not tex <= rule:
            bad.append((el.name, sorted(tex), sorted(rule or [])))
    print(f"textures by role: {len(v.els) - len(bad)} of {len(v.els)} elements as their role says")
    if bad:
        v.fail(f"textures off their role: {bad[:6]}")


# ---------------------------------------------------------------- containment, the top, the sides, the walls
def check_containment(v):
    """Nothing leaves the furnace's eight cells over the fire door's whole travel; nothing stands over the top."""
    m = v.m
    worst, where = -1e9, None
    for dp in depths(0.05):
        for pid in v.by_part:
            for el in v.posed(pid, dp):
                lo, hi = el.aabb()
                o = max(-lo[0], hi[0] - m.W, -lo[1], hi[1] - m.H, -lo[2], hi[2] - m.D)
                if o > worst:
                    worst, where = o, (el.name, dp)
    print(f"containment over the door's travel (every 0.05): worst overhang {worst:.3f} voxels ({where[0]} at depth "
          f"{where[1]:.2f}); nothing over the top")
    if worst > 0.001:
        v.fail(f"{where[0]} leaves the furnace's cells at depth {where[1]}")


def check_top(v):
    """The top is flat: every half voxel of it, just under the top, is brick or flush iron whose top is the top, but
    the outlet's (the cap's seat and the stack's mouth)."""
    m = v.m
    solid = static(v)
    (sx0, sx1), _, (sz0, sz1) = m.CAP_SEAT
    bad, n = [], 0
    x = 0.25
    while x < m.W:
        z = 0.25
        while z < m.D:
            if not (sx0 <= x <= sx1 and sz0 <= z <= sz1):
                n += 1
                under = [e for e in solid if inside(e, (x, m.FLOOR_TOP - 0.05, z))]
                if not under or any(abs(e.aabb()[1][1] - m.FLOOR_TOP) > 1e-6 for e in under):
                    bad.append((x, z))
            z += 0.5
        x += 0.5
    print(f"top: flat at {n - len(bad)} of {n} points (every half voxel, the outlet aside)")
    if bad:
        v.fail(f"the top is not flat at {len(bad)} points, e.g. {bad[:4]}")


def side_leg(e):
    """Whether e is a corner angle's leg or a side band's leg down the side face (flush in it, so a row's walls still
    meet)."""
    return re.match(r"^fr_(angle_|band_[we]_face)", e.name) is not None


def check_sides(v):
    """Furnaces tile into a row: each side face is flush, plain brick over its whole area (sampled every half voxel)
    but for the corner angles' side legs and the side bands' legs, flush in it; nothing but that brick and those legs reaches either side plane,
    of any part at any depth; two furnaces side by side (one moved a furnace's width along x) touch nowhere but at their
    shared walls (and their corner angles' side legs, face to face), over the door's travel."""
    m = v.m
    flush = v.by_part["brickwork"] + [e for e in v.by_part["frame"] if side_leg(e)]
    bad, n, legs = [], 0, 0
    for x in (0.05, m.W - 0.05):
        y = 0.25
        while y < m.H:
            z = 0.25
            while z < m.D:
                n += 1
                hit = [e for e in flush if inside(e, (x, y, z))]
                if not hit:
                    bad.append((x, y, z))
                elif all(side_leg(e) for e in hit):
                    legs += 1
                z += 0.5
            y += 0.5
    if bad:
        v.fail(f"the side faces are not plain brick or the angles' legs at {len(bad)} of {n} points, e.g. {bad[:4]}")
    reach = set()
    for dp in depths(0.1):
        for pid in v.by_part:
            for e in v.posed(pid, dp):
                lo, hi = e.aabb()
                if (lo[0] < 1e-6 or hi[0] > m.W - 1e-6) and not (e.name.startswith("bk_brick") or side_leg(e)):
                    reach.add(e.name)
    if reach:
        v.fail(f"not brick or an angle's side leg at a side plane: {sorted(reach)[:4]}")
    hits = 0
    for dp in (0.0, 0.5, 1.0):
        a = [e for pid in v.by_part for e in v.posed(pid, dp)]
        b = []
        for e in a:
            c = e.clone()
            c.c = [c.c[0] + m.W, c.c[1], c.c[2]]
            b.append(c)
        near_a = [e for e in a if e.aabb()[1][0] > m.W - 2.0]
        near_b = [e for e in b if e.aabb()[0][0] < m.W + 2.0]
        for ea in near_a:
            for eb in near_b:
                alo, ahi = ea.aabb()
                blo, bhi = eb.aabb()
                if all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)) and obb_obb(ea, eb):
                    hits += 1
    if hits:
        v.fail(f"two furnaces side by side run into each other ({hits} pairs)")
    print(f"sides: plain brick at {n - len(bad) - legs} of {n} points on the two side faces, the corner angles' and the "
          f"side bands' legs flush at the other {legs}; nothing else reaches them; two furnaces side by side clear of each other but at "
          "the shared walls")


def check_binding(v):
    """The binding is angle iron (the owner's rule for every brick furnace): each vertical corner binder, and each band
    along a top edge, is an angle of two legs, each at most ANGLE_T + 0.01 thick, 2 to 3 wide outside, meeting along the
    angle's corner, its outside faces flush with the furnace's two faces at that corner; and each leg's inside face is
    bedded on brick over its whole length (sampled every half voxel just inside it, where no other binder is), so
    the angle wraps the brick's corner and holds it on both faces. The bands run from corner angle to corner angle,
    touching both, and no flat strap is left."""
    m = v.m
    frame = v.by_part["frame"]
    bk = brick(v)
    box_hi = (m.W, m.H, m.D)
    worst, n = [], 0
    binders = [e for e in frame if e.name.startswith(("fr_angle_", "fr_band_"))]
    for name, legs, corner in m.binding_angles():
        for (lo, hi), (axis, sign), other in zip(legs, corner, (legs[1], legs[0])):
            size = [hi[k] - lo[k] for k in range(3)]
            across = [k for k in range(3) if k != axis and size[k] < 4.0]
            if size[axis] > m.ANGLE_T + 0.01 or len(across) != 1:
                v.fail(f"{name}'s leg across axis {axis} is not a thin leg: {size}")
                continue
            face = 0.0 if sign < 0 else box_hi[axis]
            if abs((lo[axis] if sign < 0 else hi[axis]) - face) > 1e-9:
                v.fail(f"{name}'s leg on the {'+-'[sign < 0]}{'xyz'[axis]} face is not flush with it")
            k = across[0]
            width = max(hi[k], other[1][k]) - min(lo[k], other[0][k])
            if not (2.0 - 1e-9 <= width <= 3.0 + 1e-9):
                v.fail(f"{name}'s leg is {width:.2f} wide, not 2 to 3")
            if not all(lo[q] <= other[1][q] + 1e-9 and other[0][q] <= hi[q] + 1e-9 for q in range(3)):
                v.fail(f"{name}'s two legs do not meet")
            inner = lo[axis] - 0.05 if sign > 0 else hi[axis] + 0.05
            q = max(range(3), key=lambda r: size[r])
            bare = 0
            t = lo[q] + 0.25
            while t < hi[q]:
                u = lo[k] + 0.25
                while u < hi[k]:
                    p = [0.0, 0.0, 0.0]
                    p[axis], p[q], p[k] = inner, t, u
                    if not any(inside(e, p) for e in binders):
                        n += 1
                        if not any(inside(e, p) for e in bk):
                            bare += 1
                    u += 0.5
                t += 0.5
            if bare:
                worst.append((name, "xyz"[axis], bare))
    if worst:
        v.fail(f"angle legs not bedded on brick: {worst[:4]}")
    angles = {name: legs for name, legs, _ in m.binding_angles()}

    def meet(a, b):
        return any(all(p[0][q] <= c[1][q] + 1e-9 and c[0][q] <= p[1][q] + 1e-9 for q in range(3)) for p in a for c in b)
    for face in ("n", "s"):
        for side in ("w", "e"):
            corner = angles[f"fr_angle_{face}{side}"]
            for band in (f"fr_band_{face}", f"fr_band_{side}"):
                if not meet(angles[band], corner):
                    v.fail(f"{band} does not reach the {face}{side} corner angle")
            if not meet(angles[f"fr_band_{face}"], angles[f"fr_band_{side}"]):
                v.fail(f"the {face} and {side} bands do not meet at the top corner")
    # one frame: every binder's piece (as drawn, after the block split) joined to every other through pieces it
    # touches, no gap and no loose end
    pieces = [e.aabb() for e in binders]
    seen, todo = {0}, [0]
    while todo:
        i = todo.pop()
        for j, b in enumerate(pieces):
            if j not in seen and meet([pieces[i]], [b]):
                seen.add(j)
                todo.append(j)
    if len(seen) != len(pieces):
        loose = sorted({binders[j].name for j in range(len(pieces)) if j not in seen})
        v.fail(f"the binding is not one frame: {loose[:6]} not joined to the rest")
    straps = [e.name for e in frame if re.match(r"^fr_(strap|kerb)", e.name)]
    if straps:
        v.fail(f"flat straps in the binding: {straps}")
    print(f"binding: {len(angles)} angles (4 corner angles, 4 top bands), legs {m.ANGLE_T} thick and {m.ANGLE_LEG} wide, "
          f"each flush with its two faces and bedded on brick ({n} points); each band meeting both its corner angles and "
          f"the bands beside it, the {len(pieces)} pieces one frame")


def check_walls(v):
    """The walls, everything centred but the outlet: the chamber, the firebox, the niche, the plate, the spout, the pot,
    the fire hole and the ash mouth are centred left to right (the stack is in the back row's east cell, where its
    chimney stands); the wall (brick, or iron set into it) round the firebox, from the grate to the plate, and round the
    chamber, from the plate to the springing, and from the stack to the east and back faces, from its foot to the cap's
    seat, sampled every half voxel, runs at least MIN_WALL out to the furnace's face (the front wall's to the niche),
    outside the fire hole, the ash mouth and their recesses."""
    m = v.m
    mid = m.W / 2
    for what, (x0, x1) in (("chamber and firebox", m.IN_X), ("niche", m.NICHE[0]), ("spout", m.SPOUT_GAP),
                           ("fire hole", m.FIRE_HOLE[0]), ("ash mouth", m.ASH_MOUTH[0]), ("door's recess", m.DOOR_RECESS[0])):
        if abs((x0 + x1) / 2 - mid) > 1e-9:
            v.fail(f"the {what} is not centred left to right")
    if abs(m.POT_C[0] - mid) > 1e-9:
        v.fail("the pot is not centred left to right")
    oc = m.outlet_cell()
    sx = (m.STACK[0][0] + m.STACK[0][1]) / 2
    if not (oc[0] * m.B <= m.STACK[0][0] and m.STACK[0][1] <= (oc[0] + 1) * m.B
            and oc[2] * m.B <= m.STACK[2][0] and m.STACK[2][1] <= (oc[2] + 1) * m.B):
        v.fail("the stack's mouth is not in one cell, the outlet's")
    near = brick(v) + v.by_part["frame"]

    def solid(p):
        return any(inside(e, p, eps=-1e-9) for e in near)

    def run(p, axis, sign, limit):
        t = 0.0
        while t < limit:
            q = list(p)
            q[axis] += sign * (t + 0.125)
            if not solid(q):
                return t
            t += 0.25
        return limit

    def open_back(x, y):
        """Whether (x, y) on the back wall is the fire hole's, the ash mouth's, or in a recess or the jambs' frame."""
        for (xr, yr) in (m.FIRE_HOLE, m.ASH_MOUTH, m.DOOR_RECESS[:2], m.DAMPER_RECESS[:2]):
            if xr[0] - 0.5 <= x <= xr[1] + 0.5 and yr[0] - 0.5 <= y <= yr[1] + 0.5:
                return True
        return False
    walls = {}
    x0, x1 = m.IN_X
    for y in [m.GRATE_TOP + 0.25 + 0.5 * k for k in range(int((m.SPRING - m.GRATE_TOP) * 2))]:
        zs = [0.25 + 0.5 * k for k in range(int(m.VAULT_Z[1] * 2))] if y > m.NICHE[1][1] else []
        zs += [m.FIRE_Z[0] + 0.25 + 0.5 * k for k in range(int((m.FIRE_Z[1] - m.FIRE_Z[0]) * 2))] if y < m.FIREBOX_TOP else []
        for z in sorted(set(zs)):
            walls.setdefault("west", []).append((run((x0, y, z), 0, -1, x0), (y, z)))
            walls.setdefault("east", []).append((run((x1, y, z), 0, 1, m.W - x1), (y, z)))
        if y < m.FRONT_WALL_TOP:
            for x in [x0 + 0.25 + 0.5 * k for k in range(int((x1 - x0) * 2))]:
                walls.setdefault("front", []).append((run((x, y, m.FIRE_Z[0]), 2, -1, m.FIRE_Z[0] - m.NICHE[2][1]), (x, y)))
        if y < m.FIREBOX_TOP:
            for x in [x0 + 0.25 + 0.5 * k for k in range(int((x1 - x0) * 2))]:
                if not open_back(x, y):
                    walls.setdefault("back", []).append((run((x, y, m.FIRE_Z[1]), 2, 1, m.D - m.FIRE_Z[1]), (x, y)))
    (sx0, sx1), (sy0, _), (sz0, sz1) = m.STACK
    for y in [sy0 + 0.25 + 0.5 * k for k in range(int((m.CAP_Y[0] - sy0) * 2))]:
        for z in [sz0 + 0.25 + 0.5 * k for k in range(int((sz1 - sz0) * 2))]:
            walls.setdefault("stack east", []).append((run((sx1, y, z), 0, 1, m.W - sx1), (y, z)))
        for x in [sx0 + 0.25 + 0.5 * k for k in range(int((sx1 - sx0) * 2))]:
            walls.setdefault("stack back", []).append((run((x, y, sz1), 2, 1, m.D - sz1), (x, y)))
    worst = {k: min(w) for k, w in walls.items()}
    print("walls: the chamber, the firebox, the niche, the spout, the pot, the fire hole and the ash mouth centred, the "
          f"stack in the outlet's cell (x {sx}); the wall round the fire, the plate and the stack at least "
          + ", ".join(f"{k} {t:.2f}" for k, (t, _) in worst.items()) + f" (MIN_WALL {m.MIN_WALL})")
    for k, (t, at) in worst.items():
        if t < m.MIN_WALL - 1e-9:
            v.fail(f"the {k} wall is {t:.2f} thick at {at}")


def what_is(v, p):
    """What lies at p: "ln" (the lining's brick), "bk" (the brickwork's), "iron" (the frame), or None (open)."""
    for e in v.by_part["lining"]:
        if inside(e, p):
            return "ln"
    for e in v.by_part["brickwork"]:
        if inside(e, p):
            return "bk"
    if any(inside(e, p) for e in v.by_part["frame"]):
        return "iron"
    return None


def face_samples(lo, hi, k, sign, step=1.0):
    """Points on the face of box (lo, hi) normal to axis k on the `sign` side, every `step`, a quarter in from its
    edges."""
    a, b = [q for q in range(3) if q != k]
    out = []
    ua = lo[a] + 0.25
    while ua < hi[a] - 0.2:
        ub = lo[b] + 0.25
        while ub < hi[b] - 0.2:
            pnt = [0.0, 0.0, 0.0]
            pnt[k] = hi[k] if sign > 0 else lo[k]
            pnt[a], pnt[b] = ua, ub
            out.append(pnt)
            ub += step
        ua += step
    return out


def check_lining(v):
    """The lining: on the hot faces (the firebox's walls from the grate's bearers up, the throat's and the stack's, the
    chamber's side walls over the plate, the hanging wall) the brick is the lining's for at least LINING_T from the
    face out, unless the brick ends sooner, sampled every voxel and walked out every quarter; the niche's, the ash
    pit's and the ash mouth's faces are the brickwork's. The vault is the lining's (its staves are LINING_T thick, by
    construction)."""
    m = v.m
    t = m.LINING_T
    x0, x1 = m.IN_X
    hot = [([x0, m.BEARER_Y[0], m.FIRE_Z[0]], [x1, m.FIREBOX_TOP, m.FIRE_Z[1]]),          # the firebox
           ([x0, m.BEARER_Y[0], m.HANG_Z[1]], [x1, m.THROAT_TOP, m.FIRE_Z[1]]),          # the throat
           (list(m.STACK[0][:1]) + [m.STACK[1][0]] + [m.STACK[2][0]], [m.STACK[0][1], m.STACK_LINING_TOP, m.STACK[2][1]]),
           ([x0, m.PLATE_LOW_TOP, m.FRONT_RING], [x1, m.SPRING, m.VAULT_Z[1]]),         # the chamber's side walls
           ([x0, m.HANG_FOOT, m.HANG_Z[0] - 0.5], [x1, m.SPRING, m.HANG_Z[0]])]          # the hanging wall's face
    thinnest, where, n = 1e9, None, 0
    for idx, (lo, hi) in enumerate(hot):
        for k in range(3):
            if idx == 3 and k != 0:
                continue                                   # the chamber: its side walls only
            if idx == 4 and k != 2:
                continue
            for sign in (-1, 1):
                if idx == 4 and sign < 0:
                    continue
                for pnt in face_samples(lo, hi, k, sign):
                    first = list(pnt)
                    first[k] += sign * 0.125
                    w0 = what_is(v, first)
                    if w0 != "ln":
                        if w0 == "bk":
                            v.fail(f"a hot face is the brickwork's at {first}")
                        continue
                    n += 1
                    d = 0.125
                    while d < t + 0.5:
                        q = list(pnt)
                        q[k] += sign * d
                        w = what_is(v, q) if 0.0 < q[k] < (m.W, m.H, m.D)[k] else None
                        if w != "ln":
                            if w == "bk" and d - 0.125 < t - 1e-6 and d - 0.125 < thinnest:
                                thinnest, where = d - 0.125, pnt
                            break
                        d += 0.25
    cold, wrong = 0, []
    for (xr, yr, zr) in (m.NICHE, (m.IN_X, m.ASH_PIT_Y, m.FIRE_Z), (m.ASH_MOUTH[0], m.ASH_MOUTH[1], (m.FIRE_Z[1], m.D))):
        lo, hi = [xr[0], yr[0], zr[0]], [xr[1], yr[1], zr[1]]
        for k in range(3):
            for sign in (-1, 1):
                for pnt in face_samples(lo, hi, k, sign, step=2.0):
                    q = list(pnt)
                    q[k] += sign * 0.125
                    w = what_is(v, q)
                    if w in ("ln", "bk"):
                        cold += 1
                        if w == "ln":
                            wrong.append(q)
    if thinnest < 1e9:
        v.fail(f"the lining is {thinnest:.2f} thick at {where}")
    if wrong:
        v.fail(f"the niche, the ash pit or the ash mouth is lined at {len(wrong)} points, e.g. {wrong[:3]}")
    print(f"lining: at least {t} of the lining's brick behind the hot faces ({n} points, walked out every quarter) or "
          f"the lining reaching through; the niche, the ash pit and the ash mouth the brickwork's ({cold} points)")


SKIN_OPEN = 1.0 + 1e-6                       # how deep a dent in an outer face may be and still be the outside (a recess, the
#                                              cap's seat): deeper is an opening into the furnace, its inside lined


def check_skin(v):
    """The outside is fired brick or iron everywhere (owner's rule): the lining never shows from outside the
    footprint. Every half voxel of each of the six outer faces, a ray walks in square to the face (every eighth of a
    voxel) through the masonry and the iron frame alone (the movable fittings, the door, the damper and the cap, left
    out, so nothing covers for the lining when it is open or off); where it meets something within SKIN_OPEN of the face
    (the face itself, or a recess's or a seat's floor), that and the next OUTER_BRICK behind it hold no lining. Deeper
    hits are the furnace's openings (the working opening, the niche, the fire hole, the ash mouth, the flue), whose
    insides are lined by design."""
    m = v.m
    lining = v.by_part["lining"]
    masonry = v.by_part["brickwork"] + v.by_part["frame"] + lining
    size = (m.W, m.H, m.D)
    names = "xyz"
    reach = SKIN_OPEN + m.OUTER_BRICK
    bad, n, thinnest, openings = {}, 0, 1e9, 0
    for k in range(3):
        a, b = [q for q in range(3) if q != k]
        for sign in (-1, 1):
            face = f"{'+-'[sign < 0]}{names[k]}"
            base = 0.0 if sign < 0 else size[k]
            slab = []
            for e in masonry:
                lo, hi = e.aabb()
                if (lo[k] < reach if sign < 0 else hi[k] > size[k] - reach):
                    slab.append((e, lo, hi))
            ua = 0.25
            while ua < size[a]:
                ub = 0.25
                while ub < size[b]:
                    n += 1
                    hit, first_ln = None, None
                    line = [e for e, lo, hi in slab if lo[a] <= ua <= hi[a] and lo[b] <= ub <= hi[b]]
                    d = 0.0625
                    while d < reach:
                        p = [0.0, 0.0, 0.0]
                        p[a], p[b], p[k] = ua, ub, base - sign * d
                        near = [e for e in line if inside(e, p)]
                        if near:
                            if hit is None:
                                hit = d
                            if any(e in lining for e in near):
                                first_ln = d
                                break
                        elif hit is None and d > SKIN_OPEN:
                            break
                        d += 0.125
                    if hit is None:
                        openings += 1
                    elif first_ln is not None and first_ln - hit < m.OUTER_BRICK - 1e-9:
                        owner = next(e.name for e in lining if inside(e, [ua if q == a else ub if q == b else
                                                                          base - sign * first_ln for q in range(3)]))
                        bad.setdefault((face, owner), (round(first_ln - hit, 3), (ua, ub)))
                    elif first_ln is not None:
                        thinnest = min(thinnest, first_ln - hit)
                    ub += 0.5
                ua += 0.5
    print(f"skin: the outside fired brick or iron at {n - openings} of {n} points on the six faces (the other "
          f"{openings} the furnace's openings), at least {m.OUTER_BRICK} of it before any lining"
          + (f" (the thinnest {thinnest:.2f})" if thinnest < 1e9 else " (no lining within reach)"))
    for (face, owner), (t, at) in sorted(bad.items())[:12]:
        v.fail(f"the lining ({owner}) is {t} in from the {face} face at {at}: it shows, or the brick over it is too thin")


def check_vault(v):
    """The vault: its staves stay in the vault's zone (they bed into the side walls at the springing), and with their
    backing they are solid between the intrados and the zone's top: points just over the arc, every half voxel across
    and every two along (and at the front ring's middle), are the brickwork's fired brick within FRONT_RING of the
    front face (the arch ring) and the lining's behind it, and every point under it (the chamber) is open; at the
    front face the arch ring and its backing run flush."""
    m = v.m
    (zx0, zx1), (zy0, zy1), (zz0, zz1) = m.VAULT_ZONE
    out = []
    for e in v.by_part["lining"]:
        if not e.name.startswith(("ln_vault", "bk_ring")):
            continue
        lo, hi = e.aabb()
        if lo[0] < zx0 - 1e-6 or hi[0] > zx1 + 1e-6 or hi[1] > zy1 + 1e-6 or lo[1] < zy0 - 0.5 or lo[2] < zz0 - 1e-6 or hi[2] > zz1 + 1e-6:
            out.append(e.name)
    if out:
        v.fail(f"vault staves leave the vault's zone: {out}")
    lining, bk = v.by_part["lining"], v.by_part["brickwork"]
    holes, opens, n = [], [], 0
    x = m.IN_X[0] + 0.25
    while x < m.IN_X[1]:
        yi = m.intrados(x)
        for z in [zz0 + m.FRONT_RING / 2] + [zz0 + m.FRONT_RING + 0.5 + 2 * k for k in range(int((zz1 - zz0 - m.FRONT_RING) / 2))]:
            for y in (yi + 0.15, yi + 1.5, yi + 2.9, (yi + 3 + zy1) / 2):
                if y >= zy1:
                    continue
                n += 1
                if not any(inside(e, (x, y, z)) for e in (bk if z < m.FRONT_RING else lining)):
                    holes.append((x, y, z))
            under = (x, max(m.SPRING, yi - 0.15), z)
            if any(inside(e, under) for e in static(v)):
                opens.append(under)
        x += 0.5
    if holes:
        v.fail(f"the vault is not solid over its arc at {len(holes)} points, e.g. {holes[:3]}")
    if opens:
        v.fail(f"something stands under the vault's arc at {len(opens)} points, e.g. {opens[:3]}")
    print(f"vault: {m.STAVES} staves on an arc of radius {m.VAULT_R:.1f} (rise {m.RISE}, span {m.IN_X[1] - m.IN_X[0]:.0f}) "
          f"in their zone; solid over the arc to the brickwork at {n} points, open under it")


def check_build(v):
    """The stages' counts: the brickwork's and the lining's volumes, measured from their elements (each lattice box in
    exactly one brick box: their volumes add up to the lattice's; the vault's zone its box less the chamber's segment),
    give the rig's quantities at the game's rates, rounded up; the fittings and the frame's recipe are the ruled ones;
    every stage's requires is a part's."""
    m = v.m
    xs, ys, zs, mat = m.brick_lattice()
    lat = {"bk": 0.0, "ln": 0.0}
    for (i, j, k), w in mat.items():
        if w:
            lat[w] += (xs[i + 1] - xs[i]) * (ys[j + 1] - ys[j]) * (zs[k + 1] - zs[k])
    els = {w: sum(abs(e.size[0] * e.size[1] * e.size[2]) for e in brick(v) if e.name.startswith(f"{w}_brick_"))
           for w in ("bk", "ln")}
    for w in ("bk", "ln"):
        if abs(lat[w] - els[w]) > 0.05:             # the z-fighting fix steps a few faces in by 0.015
            v.fail(f"the {w} boxes hold {els[w]:.2f} cubic voxels, the lattice {lat[w]:.2f}")
    c = m.build_counts(m.build_frame())
    build = v.rig["build"]
    st = {s["requires"]: s for s in build["stages"]}
    got = {"bricks": st["brickwork"]["materials"][0]["quantity"], "lining": st["lining"]["materials"][0]["quantity"],
           "fireclay": st["lining"]["materials"][1]["quantity"]}
    ring, vault = m.vault_volume()
    want = {"bricks": math.ceil((lat["bk"] + ring) / m.B ** 3 * m.BRICKS_PER_BLOCK - 1e-9),
            "lining": math.ceil((lat["ln"] + vault) / m.B ** 3 * m.LINING_PER_BLOCK - 1e-9)}
    want["fireclay"] = math.ceil(want["lining"] / m.LINING_PER_FIRECLAY - 1e-9)
    if got != want or any(got[k] != c[k] for k in got):
        v.fail(f"the stages' counts {got} are not the measured {want}")
    if [s["requires"] for s in build["stages"]] != list(m.STAGES):
        v.fail("the stages are not brickwork, lining, fittings")
    if st["brickwork"]["materials"][0]["code"] != "game:burnedbrick-*" or not st["brickwork"].get("oneType"):
        v.fail("the brickwork does not take one type of any fired brick")
    if [(x["code"], x["quantity"]) for x in st["fittings"]["materials"]] != list(m.FITTINGS_STAGE):
        v.fail("the fittings stage's materials are not the ruled ones")
    if [(x["code"], x["quantity"]) for x in build["frame"]["ingredients"]] != list(m.FRAME_RECIPE):
        v.fail("the frame's recipe is not the ruled one")
    requires = {p["requires"] for p in v.parts}
    if not set(m.STAGES) <= requires or any(f"damper{d}" not in requires for d in m.DAMPERS):
        v.fail("a stage's requires is no part's")
    if sorted(st["fittings"]["parts"]) != sorted(p["id"] for p in v.parts if p["requires"] == "fittings"):
        v.fail("the fittings stage's parts are not the parts it fits")
    print(f"build: brickwork {(lat['bk'] + ring) / m.B ** 3:.2f} blocks (the arch ring {ring / m.B ** 3:.3f}) -> "
          f"{got['bricks']} fired bricks; lining "
          f"{(lat['ln'] + vault) / m.B ** 3:.2f} blocks (the vault {vault / m.B ** 3:.3f}) -> {got['lining']} "
          f"refractory bricks and {got['fireclay']} fire clay; fittings {dict(m.FITTINGS_STAGE)}; the frame "
          f"{dict(m.FRAME_RECIPE)}")


# ---------------------------------------------------------------- the plate, the spout, the pot, the charge
def plate_slab(v):
    return next(e for e in v.by_part["plate"] if e.name == "plate_slab")


def check_plate(v):
    """The hearth plate: one slab whose top runs from PLATE_LOW_TOP at its low end to its high end, falling to the
    front; its edges against the side walls' lining; lying on its front bearer, the front wall's top and its back
    bearer, each touching its underside; the rims on it; the spout continuing its top at the low end, its lip over
    the pot's mouth; the cakes between the rims, on the plate, under the springing and the hanging wall's foot, over
    the firebox for the most part; the lead where the cakes lay; the tin's run from under the cakes to its stream,
    which falls into the pot to the tin's surface."""
    m = v.m
    slab = plate_slab(v)
    worst = 0.0
    for z in (m.PLATE_Z[0] + 0.5, (m.PLATE_Z[0] + m.PLATE_Z[1]) / 2, m.PLATE_Z[1] - 0.5):
        for dy, want in ((-0.03, True), (0.03, False)):
            if inside(slab, (m.XM, m.plate_top(z) + dy, z)) != want:
                v.fail(f"the plate's top is not at {m.plate_top(z):.2f} at z {z}")
        for dy, want in ((0.03, True), (-0.03, False)):
            if inside(slab, (m.XM, m.plate_under(z) + dy, z)) != want:
                v.fail(f"the plate's underside is not at {m.plate_under(z):.2f} at z {z}")
    if m.plate_top(m.PLATE_Z[1]) <= m.plate_top(m.PLATE_Z[0]):
        v.fail("the plate does not fall to the front")
    lo, hi = slab.aabb()
    if abs(lo[0] - m.IN_X[0]) > 1e-6 or abs(hi[0] - m.IN_X[1]) > 1e-6:
        v.fail("the plate does not run from side wall to side wall")
    for x in (m.IN_X[0] - 0.1, m.IN_X[1] + 0.1):
        for z in (m.PLATE_Z[0] + 2.0, m.PLATE_Z[1] - 2.0):
            if what_is(v, (x, m.plate_top(z) - 0.5, z)) != "ln":
                v.fail(f"the plate's edge is not against the lining at x {x}, z {z}")
    # its supports: each touches the underside along its front top edge
    frame = v.by_part["frame"]
    rests = []
    for name, (z0, z1) in m.PLATE_BEARERS:
        b = [e for e in frame if e.name.startswith(f"fr_platebearer_{name}")]
        top = max(e.aabb()[1][1] for e in b)
        gap = m.plate_under(z0) - top
        rests.append((name, gap))
        worst = max(worst, abs(gap))
    zw = (m.FRONT_WALL_Z[0] + m.FRONT_WALL_Z[1]) / 2
    wall_top = max(e.aabb()[1][1] for e in brick(v) if inside(e, (m.XM - 0.5, m.FRONT_WALL_TOP - 0.05, zw)))
    rests.append(("front wall", m.plate_under(m.FRONT_WALL_Z[0]) - wall_top))
    worst = max(worst, abs(rests[-1][1]))
    if worst > 1e-6:
        v.fail(f"the plate does not lie on its supports: {rests}")
    # the rims stand on its top, the spout continues it
    for e in v.by_part["plate"]:
        if e.name.startswith("plate_rim"):
            c = e.c
            z = c[2]
            if not inside(slab, (c[0], m.plate_top(z) - 0.05, z)):
                v.fail(f"{e.name} does not stand on the plate")
    lx, ly, lz = m.spout_lip()
    pot = aabb_of(v.by_part["pot"])
    inner = m.POT_A - m.POT_WALL
    if not (abs(lx - m.POT_C[0]) < inner and abs(lz - m.POT_C[1]) < inner and ly > pot[1][1]):
        v.fail("the spout's lip is not over the pot's mouth")
    # the charge
    rims = [e for e in v.by_part["plate"] if e.name.startswith("plate_rim_")]
    cakes = v.by_part["cakes"]
    clear_top = []
    for e in cakes:
        lo, hi = e.aabb()
        if lo[0] < m.IN_X[0] + m.RIM[0] - 1e-6 or hi[0] > m.IN_X[1] - m.RIM[0] + 1e-6:
            v.fail(f"{e.name} is not between the rims")
        if any(inside(r, e.c) for r in rims):
            v.fail(f"{e.name} lies on a rim")
        z = e.c[2]
        if not inside(slab, (e.c[0], m.plate_top(z) - 0.05, z)):
            v.fail(f"{e.name} does not lie on the plate")
        clear_top.append(min(m.SPRING, m.HANG_FOOT) - hi[1])
    over = sum(1 for e in cakes if e.c[2] > m.FIRE_Z[0])
    if min(clear_top) < 1.0:
        v.fail(f"the cakes stand within {min(clear_top):.2f} of the hanging wall's foot or the springing")
    if over < len(cakes):
        v.fail("a cake's middle is not over the firebox")
    lead = [e for e in v.by_part["lead"] if not e.name.startswith("lead_knob")]
    for e, c in zip(lead, cakes):
        if not inside(c, e.c, eps=-0.5) and abs(e.c[2] - c.c[2]) > 0.5:
            v.fail(f"{e.name} is not where a cake lay")
    run = next(e for e in v.by_part["tinrun"] if e.name == "tinrun_plate")
    stream = next(e for e in v.by_part["tinrun"] if e.name == "tinrun_stream")
    rlo, rhi = run.aabb()
    lowest = min(c.aabb()[0][2] for c in cakes)
    slo, shi = stream.aabb()
    if rlo[2] > m.PLATE_Z[0] + 0.3 or rhi[2] < lowest:
        v.fail("the tin's run does not reach from under the cakes to the plate's low end")
    if abs(slo[1] - m.TIN_LEVEL[1]) > 1e-6 or shi[1] < ly or not (m.POT_C[1] - inner < slo[2] and shi[2] < m.POT_C[1] + inner):
        v.fail("the tin's stream does not fall from the spout's lip to the tin in the pot")
    print(f"plate: falling to the front at {math.degrees(m.PLATE_ANGLE):.1f} degrees, its top {m.plate_top(m.PLATE_Z[0])} "
          f"to {m.plate_top(m.PLATE_Z[1]):.2f}, wall to wall, on its supports ({', '.join(f'{n} {g:+.1e}' for n, g in rests)}); "
          f"the spout's lip over the pot's mouth at y {ly:.2f} (the pot's rim {pot[1][1]}); {len(cakes)} cakes between "
          f"the rims, over the fire, {min(clear_top):.2f} under the hanging wall's foot or the springing; the lead "
          f"where they lay; the tin's run from z {rhi[2]:.2f} to the lip and its stream into the pot")


def check_pot(v):
    """The receiving pot: on the niche's floor, inside the niche, clear of the plate and its front bearer over it,
    under the spout's lip; the tin inside it, its surface 1.5 under the rim."""
    m = v.m
    lo, hi = aabb_of(v.by_part["pot"])
    (nx, ny, nz) = m.NICHE
    if abs(lo[1] - ny[0]) > 1e-9:
        v.fail("the pot does not stand on the niche's floor")
    if lo[0] < nx[0] or hi[0] > nx[1] or lo[2] < nz[0] or hi[2] > nz[1]:
        v.fail("the pot is not in the niche")
    over = min(e.aabb()[0][1] for e in v.by_part["frame"] if e.name.startswith("fr_platebearer_front"))
    if hi[1] > over - 1.0:
        v.fail(f"the pot's rim is within a voxel of the plate's front bearer ({hi[1]} under {over:.2f})")
    tlo, thi = aabb_of(v.by_part["tin"])
    if tlo[1] < m.POT_FOOT[1] - 1e-9 or hi[1] - thi[1] < 1.0:
        v.fail("the tin is not inside the pot, a voxel and more under its rim")
    print(f"pot: on the niche's floor, {hi[0] - lo[0]:.1f} across and {hi[1] - lo[1]:.1f} tall, its rim {over - hi[1]:.2f} "
          f"under the plate's front bearer; the tin at {thi[1]}, {hi[1] - thi[1]:.1f} under the rim")


def check_fuel(v):
    """The fuel's layers stack from the grate, under the plate (lumps and all), across the grate; the burning layers
    are the same boxes as the cold ones."""
    m = v.m
    layers = m.FUEL_LAYERS
    if abs(layers[0][0] - m.GRATE_TOP) > 1e-9 or any(abs(layers[k][1] - layers[k + 1][0]) > 1e-9 for k in range(len(layers) - 1)):
        v.fail("the fuel's layers do not stack from the grate")
    top = max(e.aabb()[1][1] for j in range(1, len(layers) + 1) for e in v.by_part[f"fuel{j}"])
    room = m.plate_under(m.FIRE_Z[0]) - top
    if room < 1.0:
        v.fail(f"the fuel comes within {room:.2f} of the plate")
    for j in range(1, len(layers) + 1):
        fire = {e.name.split("_", 1)[1]: e.aabb() for e in v.by_part[f"fire{j}"]}
        for e in v.by_part[f"fuel{j}"]:
            f = fire.get(e.name.split("_", 1)[1])
            if f is None or not same_box(e.aabb(), f):
                v.fail(f"{e.name} has no burning twin in fire{j}")
        beds = [e for e in v.by_part[f"fuel{j}"] if "_bed" in e.name]
        lo, hi = aabb_of(beds)
        if abs(lo[0] - m.IN_X[0]) > 1e-9 or abs(hi[0] - m.IN_X[1]) > 1e-9 or abs(lo[2] - m.FIRE_Z[0]) > 1e-9 or abs(hi[2] - m.FIRE_Z[1]) > 1e-9:
            v.fail(f"fuel layer {j} is not across the grate")
    print(f"fuel: {len(layers)} layers from the grate ({m.GRATE_TOP}) to {layers[-1][1]}, across it, its top {top:.2f}, "
          f"{room:.2f} under the plate; the burning layers the same boxes")


# ---------------------------------------------------------------- the door, the damper, the outlet
def check_door(v):
    """The fire door: in its recess between its guides at every depth; shut it covers the fire hole, open (the rest
    pose) it clears it; it is the only part that moves, along y by DOOR_TRAVEL, and theta moves nothing."""
    m = v.m
    (rx, ry, rz) = m.DOOR_RECESS
    (hx, hy) = m.FIRE_HOLE
    gx = (m.JAMBS[0][1], m.JAMBS[1][0])
    for dp in depths(0.05):
        lo, hi = aabb_of(v.posed("door", dp))
        if lo[0] < gx[0] - 1e-9 or hi[0] > gx[1] + 1e-9 or lo[1] < ry[0] - 1e-9 or hi[1] > ry[1] + 1e-9 or lo[2] < rz[0] - 1e-9 or hi[2] > rz[1] + 1e-9:
            v.fail(f"the door leaves its recess at depth {dp}")
    plate = lambda dp: next(e for e in v.posed("door", dp) if e.name == "door_plate").aabb()  # noqa: E731
    shut, opened = plate(m.SHUT), plate(0.0)
    if not (shut[0][0] <= hx[0] and shut[1][0] >= hx[1] and shut[0][1] <= hy[0] and shut[1][1] >= hy[1]):
        v.fail("the shut door does not cover the fire hole")
    if opened[0][1] < hy[1] - 1e-9:
        v.fail("the open door does not clear the fire hole")
    for pid in (p["id"] for p in v.parts):
        for d in (0.3, 0.8):
            ma = m.pm(v.parts, pid, (0.0, d, None))
            mb = m.pm(v.parts, pid, (3.7, d, None))
            if max(abs(ma[r][c] - mb[r][c]) for r in range(3) for c in range(4)) > 1e-12:
                v.fail(f"theta moves {pid}")
        if pid != "door":
            mc = m.pm(v.parts, pid, (0.0, 0.6, None))
            if max(abs(mc[r][q] - (1.0 if r == q else 0.0)) for r in range(3) for q in range(4)) > 1e-12:
                v.fail(f"depth moves {pid}")
    print(f"door: in its recess between its guides over its travel ({m.DOOR_TRAVEL}); shut over the fire hole "
          f"({shut[0][1]}..{shut[1][1]} over {hy[0]}..{hy[1]}), open clear of it ({opened[0][1]}); theta moves nothing, "
          "depth only the door")


def check_damper(v):
    """The ash mouth's damper: in each of its three places a plate in its recess behind the back face, between its
    rails; shut it covers the whole ash mouth, half its east half, open none of it."""
    m = v.m
    (mx0, mx1), (my0, my1) = m.ASH_MOUTH
    (rx, ry, rz) = m.DAMPER_RECESS
    for place in m.DAMPERS:
        els = v.by_part[f"damper{place}"]
        plate = next(e for e in els if e.name.endswith("_plate"))
        lo, hi = plate.aabb()
        allo, alhi = aabb_of(els)
        if allo[0] < rx[0] - 1e-9 or alhi[0] > rx[1] + 1e-9 or allo[2] < rz[0] - 1e-9 or alhi[2] > rz[1] + 1e-9:
            v.fail(f"the damper leaves its recess when {place}")
        rails = [e.aabb() for e in v.by_part["frame"] if e.name.startswith("fr_rail")]
        if not (abs(lo[1] - max(r[1][1] for r in rails if r[1][1] < 3)) < 1e-9 and abs(hi[1] - min(r[0][1] for r in rails if r[0][1] > 3)) < 1e-9):
            v.fail(f"the damper's plate is not between its rails when {place}")
        if lo[1] > my0 or hi[1] < my1:
            v.fail(f"the damper's plate does not span the ash mouth's height when {place}")
        covers = (max(lo[0], mx0), min(hi[0], mx1))
        if place == "shut" and not (lo[0] <= mx0 and hi[0] >= mx1):
            v.fail("the damper does not cover the ash mouth when shut")
        if place == "half" and abs(lo[0] - (mx0 + mx1) / 2) > 1e-9:
            v.fail("the half damper does not cover the mouth's east half")
        if place == "open" and lo[0] < mx1 - 1e-9:
            v.fail("the open damper does not clear the ash mouth")
        print(f"damper {place}: covers the mouth from x {covers[0]:.1f} to {max(covers[0], covers[1]):.1f} (the mouth "
              f"{mx0}..{mx1}); in its recess between its rails")


def check_outlet(v):
    """The outlet: open, its mouth clear to the sky through the stack; capped, a flat iron plate flush with the top in
    the curb's seat, covering the stack's mouth; the curb, the top course's fired brick, a rim round the cap (sampled
    every half voxel) and a ledge under its edge all round, both the brickwork's fired brick (the outside's)."""
    m = v.m
    cap = aabb_of(v.by_part["outletcap"])
    (cx0, cx1), _, (cz0, cz1) = m.CURB
    r = m.CURB_RIM
    if not same_box(cap, ([cx0 + r, m.CAP_Y[0], cz0 + r], [cx1 - r, m.FLOOR_TOP, cz1 - r])):
        v.fail(f"the cap is not flush in the curb's seat: {cap}")
    curb_brick = v.by_part["brickwork"]
    (sx0, sx1), _, (sz0, sz1) = m.STACK
    def halves(a0, a1):
        return [a0 + 0.25 + 0.5 * k for k in range(int(round((a1 - a0) * 2)))]
    rim = [(x, m.FLOOR_TOP - 0.25, z) for x in halves(cx0, cx1) for z in halves(cz0, cz1)
           if not (cx0 + r <= x <= cx1 - r and cz0 + r <= z <= cz1 - r)]
    ledge = [(x, m.CAP_Y[0] - 0.1, z) for x in halves(cx0 + r, cx1 - r) for z in halves(cz0 + r, cz1 - r)
             if not (sx0 <= x <= sx1 and sz0 <= z <= sz1)]
    missing = [p for p in rim + ledge if not any(inside(e, p) for e in curb_brick)]
    if missing:
        v.fail(f"the curb is not the brickwork's fired brick at {len(missing)} points, e.g. {missing[:3]}")
    if not (cap[0][0] < sx0 and cap[1][0] > sx1 and cap[0][2] < sz0 and cap[1][2] > sz1):
        v.fail("the cap does not cover the stack's mouth")
    # the opening, measured from the elements (not the constants): just under the cap's seat and at the bottom of the
    # stack, the clear span through its middle along x and along z. It must be square, STACK_SIDE across, and centred
    # under the chimney's line, the outlet cell's middle, which the chimney's shaft stands on; the curb square and
    # centred with it.
    solid = static(v)
    line = v.rig["chimney"]["origin"]
    cx, cz = line[0] * m.B, line[2] * m.B
    oc = m.outlet_cell()
    if abs(cx - (oc[0] + 0.5) * m.B) > 1e-6 or abs(cz - (oc[2] + 0.5) * m.B) > 1e-6:
        v.fail(f"the chimney's line ({cx}, {cz}) is not over the outlet cell's middle")

    def span(y, axis):
        ends = []
        for sign in (-1, 1):
            t = 0.0
            while t < m.B:
                p = [cx, y, cz]
                p[axis] += sign * (t + 0.0625)
                if any(inside(e, p) for e in solid):
                    break
                t += 0.125
            ends.append(p[axis] - sign * 0.0625)
        return ends
    measured = []
    for y in (m.CAP_Y[0] - 0.25, m.STACK[1][0] + 3.5, (m.STACK[1][0] + m.FLOOR_TOP) / 2):
        (ax0, ax1), (az0, az1) = span(y, 0), span(y, 2)
        measured.append((y, ax1 - ax0, az1 - az0))
        if abs((ax1 - ax0) - m.STACK_SIDE) > 0.13 or abs((az1 - az0) - m.STACK_SIDE) > 0.13:
            v.fail(f"the flue at y {y} is {ax1 - ax0:.2f} by {az1 - az0:.2f}, not {m.STACK_SIDE} square")
        if abs((ax0 + ax1) / 2 - cx) > 0.07 or abs((az0 + az1) / 2 - cz) > 0.07:
            v.fail(f"the flue at y {y} is not centred under the chimney's line: x {ax0}..{ax1}, z {az0}..{az1}")
    if abs((cx0 + cx1) / 2 - cx) > 1e-6 or abs((cz0 + cz1) / 2 - cz) > 1e-6 or abs((cx1 - cx0) - (cz1 - cz0)) > 1e-6:
        v.fail("the curb is not square and centred under the chimney's line")
    if abs(cap[1][0] - cap[0][0] - (cap[1][2] - cap[0][2])) > 1e-6:
        v.fail("the cap is not square")
    print(f"outlet: the flue square and centred under the chimney's line (x {cx}, z {cz}): "
          + ", ".join(f"{a:.2f} by {b:.2f} at y {y}" for y, a, b in measured)
          + f" ({m.STACK_SIDE} square); the curb {cx1 - cx0} square round it")
    print(f"outlet: open to the sky through the stack; capped by a plate flush in the curb's seat "
          f"({cap[1][0] - cap[0][0]:.0f} by {cap[1][2] - cap[0][2]:.0f}, {m.CAP_Y[1] - m.CAP_Y[0]} thick), over the "
          f"stack's mouth; the curb the brickwork's fired brick, rim and ledge ({len(rim) + len(ledge)} points)")


# ---------------------------------------------------------------- the draught
def empty(v, p, door_depth=None):
    els = static(v) + (v.posed("door", door_depth) if door_depth is not None else [])
    return not any(inside(e, p) for e in els)


def check_draught(v):
    """The two ways the air goes, natural draught only. The fire's: in at the ash mouth, through the ash pit, up
    between the firebars, through the fire under the plate and up its underside to its high end, up behind it into
    the throat and up the stack to the outlet. The chamber's: in at the working opening over the plate's low end,
    over the plate up to its high end, under the hanging wall's foot into the throat and up the stack. Sampled
    points along both lie in no element of the brick, the frame, the plate or the pot, with the door shut; the
    stack and the outlet's mouth are clear at every depth but for the cap. The firebars lie on their bearers and the
    bearers' ends are built into the walls."""
    m = v.m
    (ax0, ax1), (ay0, ay1) = m.ASH_MOUTH
    xm = m.XM
    fire = [(xm, (ay0 + ay1) / 2, z) for z in (m.D - 0.25, m.D - 1.5, m.D - 3.0, m.FIRE_Z[1] - 0.5)]
    fire += [(x, (m.ASH_PIT_Y[0] + m.ASH_PIT_Y[1]) / 2, z) for x in (10.5, 13.0, 19.0, 21.5) for z in (16.0, 20.0, 24.0)]
    bars = sorted((e.aabb() for e in v.by_part["frame"] if e.name.startswith("fr_bar") and e.name.endswith("w")), key=lambda b: b[0][2])
    for (alo, ahi), (blo, _) in zip(bars, bars[1:]):
        gz = (ahi[2] + blo[2]) / 2
        fire += [(x, (m.BAR_Y[0] + m.BAR_Y[1]) / 2, gz) for x in (10.5, 13.0, 19.0, 21.5)]
    under = [(z0, z1, m.plate_bearer(z0, z1)[0][1]) for _, (z0, z1) in m.PLATE_BEARERS]
    for z in [m.FIRE_Z[0] + 0.5 + q for q in range(int(m.PLATE_Z[1] - m.FIRE_Z[0]))]:
        y = m.plate_under(z) - 0.6
        for z0, z1, bottom in under:
            if z0 - 0.5 <= z <= z1 + 0.5:
                y = min(y, bottom - 0.5)                   # under the plate's back bearer, which crosses the firebox
        fire.append((xm, y, z))
    behind = (m.PLATE_Z[1] + 0.4 + m.FIRE_Z[1]) / 2
    fire += [(xm + dx, y, behind) for dx in (-6.0, 0.0, 6.0) for y in (12.0, 15.0, 18.0, 21.0)]
    (sx0, sx1), (sy0, _), (sz0, sz1) = m.STACK
    sx, sz = (sx0 + sx1) / 2, (sz0 + sz1) / 2
    fire += [(sx, y, sz) for y in (21.0, 22.0) + tuple(sy0 + 0.5 + q * 0.5 for q in range(int((m.FLOOR_TOP + m.B - sy0) * 2) - 1))]
    chamber = [(xm, 16.0, z) for z in (-0.5, 0.5, 2.0, 4.0)]
    for z in [m.PLATE_Z[0] + 0.5 + q for q in range(int(m.PLATE_Z[1] - m.PLATE_Z[0]))]:
        chamber.append((xm, min(m.plate_top(z) + 2.5, m.SPRING - 0.5) if z < m.HANG_Z[0] else (m.plate_top(z) + m.HANG_FOOT) / 2 + 1.0, z))
        chamber.append((10.5, m.plate_top(z) + 2.2, z))
    chamber += [(xm, (m.plate_top(m.HANG_Z[0]) + m.HANG_FOOT) / 2 + 1.0, z) for z in (m.HANG_Z[0] + 0.5, m.HANG_Z[1] - 0.5)]
    chamber += [((sx0 + m.IN_X[1]) / 2, 21.0, z) for z in (m.HANG_Z[1] + 0.5, sz)]
    blocked = [p for p in fire + chamber if not empty(v, p, m.SHUT)]
    if blocked:
        v.fail(f"the draught's way is blocked at {blocked[:5]}")
    mouth_lo, mouth_hi = [sx0, sy0, sz0], [sx1, m.FLOOR_TOP + m.B, sz1]
    for dp in depths(0.1):
        for pid in v.by_part:
            if pid == "outletcap":
                continue
            for e in v.posed(pid, dp):
                lo, hi = e.aabb()
                if all(lo[q] < mouth_hi[q] - 0.02 and mouth_lo[q] < hi[q] - 0.02 for q in range(3)):
                    v.fail(f"{e.name} is in the stack or over the outlet at depth {dp}")
    frame = v.by_part["frame"]
    bearers = [e.aabb() for e in frame if re.match(r"fr_bearer\d", e.name)]
    for e in frame:
        if not e.name.startswith("fr_bar"):
            continue
        lo, hi = e.aabb()
        under = [b for b in bearers if b[0][0] < hi[0] and b[1][0] > lo[0]]
        if len(under) != 2 or any(abs(lo[1] - b[1][1]) > 1e-9 or not (b[0][2] <= lo[2] and b[1][2] >= hi[2]) for b in under):
            v.fail(f"{e.name} does not lie on a bearer at each end")
    for blo, bhi in bearers:
        xc = (blo[0] + bhi[0]) / 2
        for z in (m.FIRE_Z[0] - 0.5, m.FIRE_Z[1] + 0.5):
            for y in (bhi[1] + 0.2, blo[1] - 0.2):
                if not any(inside(e, (xc, y, z)) for e in brick(v)):
                    v.fail(f"a bearer is not built into the wall at x {xc}, z {z}")
    gap = (m.FIRE_Z[1] - m.FIRE_Z[0] - m.BARS * m.BAR_W) / (m.BARS - 1)
    if gap < 0.8:
        v.fail(f"the firebars' gaps ({gap:.2f}) are too narrow for the draught")
    print(f"draught: natural; the fire's in at the ash mouth, up through {m.BARS - 1} gaps of {gap:.2f} between the "
          f"firebars, under the plate to its high end, up behind it into the throat and the stack ({len(fire)} points); "
          f"the chamber's in at the working opening, over the plate, under the hanging wall's foot ({m.HANG_FOOT}) into "
          f"the throat ({len(chamber)} points): all clear, the door shut; the stack's mouth clear at every depth but for "
          "the cap; every firebar on a bearer at each end, the bearers built into the walls")


# ---------------------------------------------------------------- clearances
def allowed(pa, pb, ea, eb):
    """Intended overlaps: a lump lying on a fuel layer's top is inside the layer over it (the same kind). Nothing else:
    the brick's voids hold the iron, the plate lies on its supports, the contents lie on the plate, in the pot and on
    the grate."""
    ra, rb = re.fullmatch(r"(fuel|fire)(\d)", pa), re.fullmatch(r"(fuel|fire)(\d)", pb)
    if not (ra and rb and ra.group(1) == rb.group(1)):
        return False
    ja, jb = int(ra.group(2)), int(rb.group(2))
    lower = ea if ja < jb else eb
    return abs(ja - jb) == 1 and "_lump" in lower.name


def touches(items, movers=None):
    """Pairs of elements of different parts that overlap; with `movers` (part ids), only pairs with one of them."""
    hits = {}
    for x in range(len(items)):
        pa, ea, (alo, ahi) = items[x]
        for y in range(x + 1, len(items)):
            pb, eb, (blo, bhi) = items[y]
            if pa == pb or (movers is not None and pa not in movers and pb not in movers):
                continue
            if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                continue
            if allowed(pa, pb, ea, eb):
                continue
            if obb_obb(ea, eb):
                hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def scenes(m):
    """The furnace built: every state of the charge (the fire lit, the damper open, the outlet open), every state of
    the fire, every place of the damper with the outlet capped; and each stage of the build before it is."""
    return ([(ch, "lit", "open", False, "fittings") for ch in m.CHARGE_IDS]
            + [("charged", fi, "open", False, "fittings") for fi in m.FIRE_IDS if fi != "lit"]
            + [("empty", "cold", d, True, "fittings") for d in m.DAMPERS]
            + [("empty", "cold", "open", False, "frame"), ("empty", "cold", "open", False, "brickwork"),
               ("empty", "cold", "open", True, "lining")])


def check_clearances(v, step):
    """In every scene, nothing of one part runs into another's with the door open; and the door, slid every `step` of
    depth, touches nothing."""
    m = v.m
    allhits = {}
    for scene in scenes(m):
        items = [(e.part, e, e.aabb()) for e in v.shown(scene, 0.0)]
        for key, hs in touches(items).items():
            allhits.setdefault(key, {}).update({h: (scene, 0.0) for h in hs})
        for dp in depths(step):
            items = [(e.part, e, e.aabb()) for e in v.shown(scene, dp)]
            for key, hs in touches(items, movers={"door"}).items():
                allhits.setdefault(key, {}).update({h: (scene, dp) for h in hs})
    print(f"clearances in {len(scenes(m))} scenes with the door open and slid every {step} of depth: "
          f"{'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} ({s[:2]}, depth {d:.2f})" for (a, b), (s, d) in ex))
    if allhits:
        v.fail("parts run into each other")


# ---------------------------------------------------------------- z-fighting
def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = m.shown(v.parts, [e for pid in v.by_part for e in v.posed(pid, pose[1])], pose)
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses")
    if bad:
        v.fail("faces z-fight")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_stages(v)
    check_textures(v)
    check_containment(v)
    check_top(v)
    check_sides(v)
    check_binding(v)
    check_walls(v)
    check_lining(v)
    check_skin(v)
    check_vault(v)
    check_build(v)
    check_plate(v)
    check_pot(v)
    check_fuel(v)
    check_door(v)
    check_damper(v)
    check_outlet(v)
    check_draught(v)
    check_clearances(v, 0.1 if quick else 0.02)
    if not quick:
        check_zfight(v)
    return v.ok


def game_assets():
    """The game's assets folder: $VINTAGE_STORY/assets, else ~/Games/vintagestory/assets; None if neither is there."""
    for root in (os.environ.get("VINTAGE_STORY"), str(Path.home() / "Games" / "vintagestory")):
        if root and (Path(root) / "assets" / "survival").is_dir():
            return Path(root) / "assets"
    return None


def game_texture(assets, code):
    """The file of a game texture code (game:block/...), in the survival or the game domain, or None."""
    rel = code.split(":", 1)[1] + ".png"
    for domain in ("survival", "game"):
        f = assets / domain / "textures" / rel
        if f.is_file():
            return f
    return None


def game_brick_types(assets):
    """The type states of the game's burnedbrick item (its itemtype file is JSON5, so read by pattern)."""
    text = (assets / "survival" / "itemtypes" / "resource" / "burnedbrick.json").read_text()
    found = re.search(r'code:\s*"type",\s*states:\s*\[([^\]]*)\]', text)
    return re.findall(r'"([^"]+)"', found.group(1)) if found else []


def game_block_exists(assets, code):
    """Whether a brick block code is one of the game's: claybricks-good-fire, or a four-high running brick course of a
    colour its brickcourse block type has."""
    clay = assets / "survival" / "blocktypes" / "clay"
    if code == "game:claybricks-good-fire":
        text = (clay / "brick.json").read_text()
        return '"good"' in text and '"fire"' in text
    hit = re.fullmatch(r"game:brickcourse-four-running-(\w+)", code)
    if not hit:
        return False
    text = (clay / "brickcourse.json").read_text()
    colours = re.search(r'"code":\s*"color",\s*"states":\s*\[([^\]]*)\]', text)
    return bool(colours) and f'"{hit.group(1)}"' in colours.group(1) and '"four"' in text and '"running"' in text


def check_bricks(m, shape, ship, fail):
    """Any fired brick: the rig's bricks name every type of the game's burnedbrick and nothing else, each with the
    block its bricks make and that block's textures; the shape's own `brick` is the default type's colour layer. With
    the game's assets at hand, every texture of the mapping and of the shape is a file there, every block one of the
    game's, and the types the game's."""
    bricks = ship["bricks"]
    types = [t for t, *_ in m.BRICKS]
    if list(bricks["types"]) != types or bricks["default"] not in types or bricks["code"] != "brick":
        fail("the rig's bricks are not the furnace's types")
    d = bricks["types"][bricks["default"]]
    if shape["textures"]["brick"] != (d.get("overlays") or [d["base"]])[-1]:
        fail("the shape's brick is not the default type's colour")
    assets = game_assets()
    if assets is None:
        print("bricks: SKIPPED the game's assets (no $VINTAGE_STORY or ~/Games/vintagestory): the textures, blocks and "
              "types are not checked against the game")
        return
    paths = {t: [d["base"]] + d.get("overlays", []) for t, d in bricks["types"].items()}
    missing = [(t, c) for t, cs in paths.items() for c in cs if game_texture(assets, c) is None]
    missing += [("shape", c) for c in shape["textures"].values() if game_texture(assets, c) is None]
    if missing:
        fail(f"textures that are not the game's: {missing}")
    blocks = [d["block"] for d in bricks["types"].values() if not game_block_exists(assets, d["block"])]
    if blocks:
        fail(f"blocks that are not the game's: {blocks}")
    game = game_brick_types(assets)
    if sorted(game) != sorted(types):
        fail(f"the game's fired brick types are {game}, the rig's {types}")
    print(f"bricks: {len(types)} types ({', '.join(types)}), the game's burnedbrick types; each type's block one of the "
          f"game's and its {sum(len(c) for c in paths.values())} textures, and the shape's {len(shape['textures'])}, "
          f"files in {assets}")


def validate_files(m, shape, frame_shape, ship):
    ok = True

    def fail(msg):
        nonlocal ok
        ok = False
        print("FAIL", msg)
    check_bricks(m, shape, ship, fail)
    if {f["texture"].lstrip("#") for e in frame_shape["elements"] for f in e["faces"].values()} != {"iron"}:
        fail("the frame's shape is not the iron frame alone")
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        fail(f"textures used but not declared: {missing}")
    for e in shape["elements"]:
        stem = e["name"].split("_")[0]
        want = m.FIRE_GLOW if re.fullmatch(r"fire\d", stem) else None
        got = {f.get("glow") for f in e["faces"].values()}
        if got and got != {want}:                        # (a brick box inside the wall draws no face at all)
            fail(f"{e['name']}'s faces glow {got}, not {want}")
    if "powerCell" in ship or "powerFace" in ship:
        fail("a fire-heated furnace has no power cell")
    if any(k.startswith("air") for k in ship):
        fail("a natural-draught furnace has no air port")
    cells = {tuple(c["pos"]): c for c in ship["cells"]}
    want_cells = {tuple(c[k] - m.ORIGIN_CELL[k] for k in range(3)) for c in m.footprint()}
    if set(cells) != want_cells or any(c.get("hollow") for c in cells.values()):
        fail(f"the cells are {sorted(cells)}, not the furnace's {sorted(want_cells)}")
    lids = {p for p, c in cells.items() if "lid" in c}
    top = {p for p in cells if p[1] == m.CELLS_Y - 1 - m.ORIGIN_CELL[1]}
    if lids != top or any(cells[p]["lid"] != 1.0 for p in lids):
        fail(f"decks on {sorted(lids)}, not on the top cells {sorted(top)}")
    if any(not c.get("boxes") for c in cells.values()):
        fail("a cell has no boxes")
    chim = tuple(m.CHIMNEY_CELL[k] - m.ORIGIN_CELL[k] for k in range(3))
    free = [tuple(c[k] - m.ORIGIN_CELL[k] for k in range(3)) for c in m.FREE_CELLS]
    if chim in cells or any(f in cells for f in free):
        fail("the chimney's cell or the cell beside it is the furnace's")
    out = tuple(ship["outletCell"])
    if (out[0], out[1] + 1, out[2]) != chim or ship["outletFace"] != "up":
        fail("the outlet is not under the chimney's cell")
    if out != tuple(m.OUTLET_CELL[k] - m.ORIGIN_CELL[k] for k in range(3)):
        fail("the outlet is not in the back row's east cell")
    line = ship["chimney"]
    if abs(line["origin"][1] - line["length"] / 2 - m.CELLS_Y) > 1e-9 or line["axis"] != "y" or line["length"] != m.MIN_CHIMNEY:
        fail("the chimney's line does not stand on the outlet")
    if not (out[0] <= line["origin"][0] <= out[0] + 1 and out[2] <= line["origin"][2] <= out[2] + 1):
        fail("the chimney's line is not over the outlet")
    sx, sz = (m.STACK[0][0] + m.STACK[0][1]) / 2 / m.B, (m.STACK[2][0] + m.STACK[2][1]) / 2 / m.B
    if any(abs(a - b) > 1e-9 for a, b in ((line["origin"][0], out[0] + 0.5), (line["origin"][2], out[2] + 0.5), (sx, out[0] + 0.5), (sz, out[2] + 0.5))):
        fail("the chimney's line and the stack's square mouth are not both centred on the outlet's cell")
    front = [ship[k]["pos"] for k in ("pot", "spout", "opening")]
    if any(not (0.0 <= p[2] <= 1.0) for p in front):
        fail("the pot, the spout and the opening are not in the front row")
    plate = ship["plate"]["pos"]
    if ship["plateSide"] != "north" or not (m.VAULT_Z[0] <= plate[2] * m.B <= m.VAULT_Z[1]) or not (
            m.IN_X[0] <= plate[0] * m.B <= m.IN_X[1]):
        fail("the plate's anchor is not in the chamber, worked from the front")
    for key in ("fireDoor", "ash"):
        p = ship[key]["pos"]
        if ship[f"{key}Side"] != "south" or abs(p[2] - m.CELLS_Z) > 1e-9 or abs(p[0] - m.CELLS_X / 2) > 1e-9:
            fail(f"the {key} is not in the middle of the back face")
    pot = ship["pot"]["pos"]
    if abs(pot[0] * m.B - m.POT_C[0]) > 1e-3 or abs(pot[2] * m.B - m.POT_C[1]) > 1e-3:
        fail("the pot's anchor is not its mouth")
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, "
          f"decks on {len(lids)}; glow on the burning fuel only; no power cell, no air port; the plate, the pot, the "
          f"spout and the opening worked from the front, the fire door and the ash mouth from the back; the outlet "
          f"{list(out)} under the chimney's cell {list(chim)}, the chimney {m.MIN_CHIMNEY} blocks from it up")
    return ok
