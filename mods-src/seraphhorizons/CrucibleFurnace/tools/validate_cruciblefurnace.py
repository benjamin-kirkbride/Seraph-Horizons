"""The crucible furnace generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its rule.
Those marked (review) came from the owner's review. Everything is in the build frame (voxels), on the model before the
origin shift; `check_shipped` in make_shape.py proves the shipped files are this model moved.
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
    """Every brick box, the brickwork's and the lining's."""
    return [e for e in v.by_part["brickwork"] + v.by_part["lining"] if e.name.startswith(("bk_brick_", "ln_brick_"))]


def static(v):
    """Everything that stands still and is not the hole's contents, the furnace built: the iron frame, the brickwork
    with the hinge's iron, and the lining (with the stands)."""
    return v.by_part["frame"] + v.by_part["brickwork"] + v.by_part["hinge"] + v.by_part["lining"]


def iron(v):
    """The iron that stands still: the frame's and the hinge's."""
    return v.by_part["frame"] + v.by_part["hinge"]


THIN_POST = 1.5                              # a post: a vertical element under this across both ways (a strap is wider)


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
    """The build *(review)*: at each stage (the iron frame alone, with the brickwork and the hinge's iron, with the
    lining too) nothing floats: every element drawn is joined to the ground. The iron frame stands on its own: it is
    iron only, and no thin post stands in it taller than half the furnace (a vertical element under THIN_POST across
    both ways; the corner straps are bands, wider): the hinge's pins and stops, and the posts that hold them, come with
    the brickwork *(review)*. Every tie is hidden once the brick is in: points just off every face of it lie in brick
    or in other iron; the posts are in the brickwork, so only the bars by the pins and the stops show while the lining
    is not in."""
    m = v.m
    out = []
    for sid, fitted in m.STAGE_STATES:
        drawn = [e for p in v.parts if p["requires"] is None or (p["requires"] in fitted and not p["drivers"])
                 for e in v.by_part[p["id"]]]
        seen, loose = frame_floating(drawn)
        out.append(f"{sid} {len(seen)}")
        if loose:
            v.fail(f"at the {sid} stage, elements float: {loose[:8]}")
    frame = v.by_part["frame"]
    tex = {f["texture"].lstrip("#") for e in frame for f in e.faces.values()}
    if tex != {"iron"}:
        v.fail(f"the iron frame is not iron only: {sorted(tex)}")
    posts = []
    for e in frame:
        lo, hi = e.aabb()
        if hi[1] - lo[1] > m.H / 2 and hi[0] - lo[0] < THIN_POST and hi[2] - lo[2] < THIN_POST:
            posts.append(e.name)
    tallest = max(e.aabb()[1][1] - e.aabb()[0][1] for e in frame if e.aabb()[1][0] - e.aabb()[0][0] < THIN_POST
                  and e.aabb()[1][2] - e.aabb()[0][2] < THIN_POST)
    if posts:
        v.fail(f"thin posts stand in the iron frame: {posts}")
    if any(e.name.startswith(("fr_pin", "fr_stop")) for e in frame) or not v.by_part.get("hinge"):
        v.fail("the hinge's iron is not the brickwork stage's")
    allin = iron(v)
    ties = [e for e in allin if e.name.startswith(("fr_tie", "hg_tie"))]
    bk = v.by_part["brickwork"]
    bare, showing = 0, set()
    for t in ties:
        others = [e for e in allin if e is not t]
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
                        if pnt[1] < 0.0 or any(inside(e, pnt) for e in others):
                            continue                       # the ground, or other iron
                        if not any(inside(e, pnt) for e in brick(v)):
                            bare += 1
                        elif not any(inside(e, pnt) for e in bk):
                            showing.add(t.name)
    print(f"stages: nothing floats at any stage (elements joined to the ground: {', '.join(out)}); the iron frame iron "
          f"only, with no thin post over {m.H / 2:.0f} (its tallest thin piece {tallest:.1f}); {len(ties)} ties hidden "
          f"once the brick is in ({bare} points off their faces bare); with the brickwork alone these show in the "
          f"lining's place: {sorted(showing) or 'none'}")
    if bare:
        v.fail(f"{bare} points off the ties' faces are in no brick or iron: a tie shows")
    if any(not n.startswith(("hg_tie_pin_", "hg_tie_stop_")) for n in showing):
        v.fail(f"a post shows before the lining is in: {sorted(showing)}")


ROLES = [
    (r"^bk_brick_", {"brick"}),
    (r"^ln_(brick_|stand\d)", {"lining"}),
    (r"^fr_(bearer|bar|lintel|guide|strap|kerb|tie)", {"iron"}),
    (r"^hg_(pin|stop|tie)", {"iron"}),
    (r"^lid_(frame|handle|knuckle)", {"iron"}),
    (r"^lid_(fill|pocket)", {"lining"}),
    (r"^pot\d_", {"clay"}),
    (r"^charge\d_", {"charge"}),
    (r"^melt\d_", {"melt"}),
    (r"^coke\d_", {"coke"}),
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


# ---------------------------------------------------------------- containment, the top, the sides
GROOVE_MAX = 0.1                             # the widest gap the shut lid may show round its hinge (review: no groove)


def overhang(m, lo, hi):
    """How far (voxels) a box pokes out of where the model may be: the brickwork's box, and the cells over the hole
    (where the lid stands open); negative when inside."""
    worst = max(-lo[0], hi[0] - m.W, -lo[1], -lo[2], hi[2] - m.D)
    if hi[1] > m.H:
        (rlo, rhi) = m.regions()[1]
        worst = max(worst, rlo[0] - lo[0], hi[0] - rhi[0], hi[1] - rhi[1], rlo[2] - lo[2], hi[2] - rhi[2])
    return worst


def check_containment(v):
    """Nothing leaves the brickwork and the six cells over its front two rows (the lid's swing: it stands open over
    the middle row and sweeps the front row's as it turns) over the lid's whole swing, so nothing reaches the
    chimney's cell or its neighbours; with the lid shut nothing stands over the floor but the hinge (the knuckles and
    the pins, their radius proud) and the two stops, and with it open only the lid besides."""
    m = v.m
    worst, where = -1e9, None
    over, high = {}, 0.0
    hinge = re.compile(r"^(lid_knuckle|hg_pin|hg_stop)")
    for dp in depths(0.01):
        for pid in v.by_part:
            for el in v.posed(pid, dp):
                lo, hi = el.aabb()
                o = overhang(m, lo, hi)
                if o > worst:
                    worst, where = o, (el.name, dp)
                if dp in (0.0, m.SHUT) and hi[1] > m.H + 1e-4:      # the lid's turn is written to 6 places
                    over.setdefault(dp, set()).add(pid if not hinge.match(el.name) else "hinge")
                    if dp == m.SHUT:
                        high = max(high, hi[1] - m.H)
    print(f"containment over the lid's swing (every 0.01): worst overhang {worst:.3f} voxels ({where[0]} at depth "
          f"{where[1]:.2f}); over the floor with the lid shut: {sorted(over.get(m.SHUT, [])) or 'nothing'} (to "
          f"{high:.2f}), open: {sorted(over.get(0.0, [])) or 'nothing'}")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the furnace's cells at depth {where[1]}")
    if over.get(m.SHUT, set()) - {"hinge"} or over.get(0.0, set()) - {"hinge", "lid"} or "lid" not in over.get(0.0, set()):
        v.fail("something but the hinge, its stops and the open lid stands over the floor")
    if high > m.STOP_H + 1e-6:
        v.fail(f"the hinge stands {high:.2f} over the floor with the lid shut")


def hinge_beds(m):
    """The hinge's places in the top, as (x0, x1, z0, z1): each knuckle's bed behind the seat, each pin's in its side
    wall, each stop's foot."""
    zb = m.PIVOT[1]
    out = [(x0, x1, zb, zb + m.BED) for x0, x1 in m.KNUCKLE_X]
    out += [(x0, x1, zb - m.PIN_BED, zb + m.PIN_BED) for x0, x1 in ((m.SEAT_X[0] - m.PIN_IN, m.SEAT_X[0]),
                                                                   (m.SEAT_X[1], m.SEAT_X[1] + m.PIN_IN))]
    out += [(x0, x1, m.STOP_Z, m.STOP_Z + m.STOP_D) for x0, x1 in m.STOP_X]
    return out


def check_top(v):
    """The top is flat *(review)*: no recess but the lid's seat and the outlet's mouth, and the hinge's beds, which
    its iron fills. Every half voxel of it, just under the floor, is brick or flush iron whose top is the floor; of the
    frame only the pins (their radius) and the two stops stand above it. And the shut lid shows no groove *(review)*:
    along lines across the hinge every hundredth of a voxel, just under the floor, the widest run in no element (the
    frame and the lid shut) is under GROOVE_MAX."""
    m = v.m
    frame = v.by_part["frame"]
    solid = static(v)
    (cx0, cx1), _, (cz0, cz1) = m.CURB
    beds = hinge_beds(m)

    def opening(x, z):
        if m.SEAT_X[0] <= x <= m.SEAT_X[1] and m.SEAT_Z[0] <= z <= m.SEAT_Z[1]:
            return True
        if any(x0 <= x <= x1 and z0 <= z <= z1 for x0, x1, z0, z1 in beds):
            return True
        return cx0 + m.CURB_RIM <= x <= cx1 - m.CURB_RIM and cz0 + m.CURB_RIM <= z <= cz1 - m.CURB_RIM
    bad, n = [], 0
    x = 0.25
    while x < m.W:
        z = 0.25
        while z < m.D:
            if not opening(x, z):
                n += 1
                under = [e for e in solid if inside(e, (x, m.FLOOR_TOP - 0.05, z))]
                if not under or any(abs(e.aabb()[1][1] - m.FLOOR_TOP) > 1e-6 for e in under):
                    bad.append((x, z))
            z += 0.5
        x += 0.5
    high = [e for e in iron(v) if e.aabb()[1][1] > m.FLOOR_TOP + 1e-6]
    odd = [e.name for e in high if not re.match(r"^hg_(pin|stop)_", e.name)]
    pins = max((e.aabb()[1][1] - m.FLOOR_TOP for e in high if e.name.startswith("hg_pin")), default=0.0)
    # the groove: lines across the hinge, along z through each knuckle, pin and stop and the lid's back edge between,
    # across its front edge (clear of the handle's pocket), and along x across the seat's sides
    near = [e for e in solid + v.posed("lid", m.SHUT) if e.aabb()[1][1] > m.FLOOR_TOP - 1.0]
    y = m.FLOOR_TOP - 0.02
    lines = [[(xa, y, m.PIVOT[1] - 1.0 + q * 0.01) for q in range(301)] for x0, x1, _, _ in beds for xa in (x0 + 0.3, (x0 + x1) / 2)]
    lines += [[(xa, y, m.PIVOT[1] - 1.0 + q * 0.01) for q in range(301)] for xa in (m.W / 2, m.SEAT_X[0] + 2.5, m.SEAT_X[1] - 2.5)]
    lines += [[(xa, y, m.SEAT_Z[0] - 1.5 + q * 0.01) for q in range(301)] for xa in (m.W / 2 - 6.0, m.SEAT_X[0] + 2.5, m.SEAT_X[1] - 2.5)]
    lines += [[(xa + q * 0.01, y, za) for q in range(301)] for xa in (m.SEAT_X[0] - 1.5, m.SEAT_X[1] - 1.5)
              for za in ((m.SEAT_Z[0] + m.SEAT_Z[1]) / 2, m.PIVOT[1] - 0.5)]
    widest, at = 0.0, None
    for line in lines:
        run = 0
        for p in line:
            if any(inside(e, p) for e in near):
                run = 0
            else:
                run += 1
                if run * 0.01 > widest:
                    widest, at = run * 0.01, p
    print(f"top: flat at the floor at {n - len(bad)} of {n} points (every half voxel, the seat, the outlet and the "
          f"hinge's beds aside); above it only the pins ({pins:.2f}) and the stops ({m.STOP_H}); with the lid shut the "
          f"widest groove across the hinge is {widest:.2f} (at {at and tuple(round(c, 2) for c in at)})")
    if bad:
        v.fail(f"the top is not flat at {len(bad)} points, e.g. {bad[:4]}")
    if odd:
        v.fail(f"frame elements stand above the top: {odd[:4]}")
    if widest > GROOVE_MAX:
        v.fail(f"the shut lid shows a groove {widest:.2f} wide at {at}")


def check_sides(v):
    """Furnaces tile into a row *(review)*: each side face is flush, plain brick over its whole area (sampled every
    half voxel), and nothing but brick reaches either side plane, of any part at any depth; two furnaces side by side
    (one moved a furnace's width along x) touch nowhere but at their shared walls, over the lid's whole swing."""
    m = v.m
    bricks = v.by_part["brickwork"]
    bad, n = [], 0
    for x in (0.05, m.W - 0.05):
        y = 0.25
        while y < m.H:
            z = 0.25
            while z < m.D:
                n += 1
                if not any(inside(e, (x, y, z)) for e in bricks):
                    bad.append((x, y, z))
                z += 0.5
            y += 0.5
    if bad:
        v.fail(f"the side faces are not plain brick at {len(bad)} of {n} points, e.g. {bad[:4]}")
    reach = set()
    for dp in depths(0.05):
        for pid in v.by_part:
            for e in v.posed(pid, dp):
                lo, hi = e.aabb()
                if (lo[0] < 1e-6 or hi[0] > m.W - 1e-6) and not e.name.startswith("bk_brick"):
                    reach.add(e.name)
    if reach:
        v.fail(f"not brick at a side plane: {sorted(reach)[:4]}")
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
    print(f"sides: plain brick at {n - len(bad)} of {n} points on the two side faces; nothing else reaches them; two "
          f"furnaces side by side clear of each other but at the shared walls")


def check_walls(v):
    """Thick walls, everything centred *(review)*: the hole, the ash mouth with its damper and the outlet are centred
    left to right; the brick round the hole and the lid's seat, sampled every half voxel from the grate to the floor
    along the west, east and front walls (and the back outside the flue's way), is solid (brick, or iron set into it)
    out to the furnace's face for at least MIN_WALL."""
    m = v.m
    frame = v.by_part["frame"]
    mid = m.W / 2
    for what, (x0, x1) in (("hole", m.HOLE_X), ("ash mouth", m.MOUTH[0]), ("damper's recess", m.RECESS[0]),
                           ("stack", m.STACK[0]), ("outlet's curb", m.CURB[0])):
        if abs((x0 + x1) / 2 - mid) > 1e-9:
            v.fail(f"the {what} is not centred left to right")
    near = brick(v) + [e for e in iron(v) if e.name.startswith(("fr_strap", "fr_kerb", "fr_bearer", "hg_pin", "fr_guide", "fr_tie", "hg_tie"))]

    def solid(p):
        return any(inside(e, p, eps=-1e-9) for e in near)

    def run(p, axis, sign, limit):
        """How far solid goes from p along axis (sign), in steps of a quarter voxel, up to limit."""
        t = 0.0
        while t < limit:
            q = list(p)
            q[axis] += sign * (t + 0.125)
            if not solid(q):
                return t
            t += 0.25
        return limit
    walls = {}
    ys = [m.GRATE_TOP + 0.25 + 0.5 * k for k in range(int((m.FLOOR_TOP - m.GRATE_TOP) * 2))]
    for y in ys:
        sx = m.SEAT_X if y > m.SEAT_Y else m.HOLE_X
        sz = m.SEAT_Z if y > m.SEAT_Y else m.HOLE_Z
        zs = [sz[0] + 0.25 + 0.5 * k for k in range(int((sz[1] - sz[0]) * 2))]
        xs = [sx[0] + 0.25 + 0.5 * k for k in range(int((sx[1] - sx[0]) * 2))]
        for z in zs:
            if abs(z - m.PIVOT[1]) < 0.75 and y > m.FLOOR_TOP - 1.0:
                continue                                  # the pin's bed, in the top corner behind the seat
            walls.setdefault("west", []).append((run((sx[0], y, z), 0, -1, sx[0]), (y, z)))
            walls.setdefault("east", []).append((run((sx[1], y, z), 0, 1, m.W - sx[1]), (y, z)))
        for x in xs:
            walls.setdefault("front", []).append((run((x, y, sz[0]), 2, -1, sz[0]), (x, y)))
            fx = m.CURB[0]                                  # the flue's way, to the outlet's curb
            if not (fx[0] - 0.5 <= x <= fx[1] + 0.5) and y <= m.FLOOR_TOP - 1.0:
                walls.setdefault("back", []).append((run((x, y, sz[1]), 2, 1, m.D - sz[1]), (x, y)))
    worst = {k: min(w) for k, w in walls.items()}
    print("walls: the hole, the ash mouth, the damper and the outlet centred; brick round the hole and the seat at least "
          + ", ".join(f"{k} {t:.2f}" for k, (t, _) in worst.items()) + f" (MIN_WALL {m.MIN_WALL})")
    for k, (t, at) in worst.items():
        if t < m.MIN_WALL - 1e-9:
            v.fail(f"the {k} wall is {t:.2f} thick at {at}")


def what_is(v, p):
    """What lies at p: "ln" (the lining's brick), "bk" (the brickwork's), "iron" (the frame), or None (open)."""
    for e in v.by_part["lining"]:
        if e.name.startswith("ln_brick_") and inside(e, p):
            return "ln"
    for e in v.by_part["brickwork"]:
        if inside(e, p):
            return "bk"
    if any(inside(e, p) for e in iron(v)):
        return "iron"
    return None


def face_samples(lo, hi, k, sign, step=1.0):
    """Points on the face of box (lo, hi) normal to axis k on the `sign` side, every `step`, a quarter in from its
    edges, with the direction out of the box."""
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
    """The lining *(review)*: on the hot faces (the hole's walls from the bearers up to the seat, and every face of the
    flue opening, the back flue and the stack that brick stands behind) the brick is the lining's for at least
    LINING_T from the face out, unless the brick ends sooner (the lining then reaches through), sampled every voxel and
    walked out every quarter; the ash pit's and the ash mouth's faces are the brickwork's."""
    m = v.m
    t = m.LINING_T
    hot = [([m.HOLE_X[0], m.BEARER_Y[1], m.HOLE_Z[0]], [m.HOLE_X[1], m.SEAT_Y, m.HOLE_Z[1]])]
    for (x0, x1), (y0, y1), (z0, z1) in (m.FLUE_OPENING, m.BACK_FLUE, m.STACK):
        hot.append(([x0, y0, z0], [x1, y1, z1]))
    thinnest, where, n = 1e9, None, 0
    for lo, hi in hot:
        for k in range(3):
            if k == 1 and lo is hot[0][0]:
                continue                                   # the hole's floor is the grate, its top the lid
            for sign in (-1, 1):
                for pnt in face_samples(lo, hi, k, sign):
                    first = list(pnt)
                    first[k] += sign * 0.125
                    if what_is(v, first) != "ln":
                        if what_is(v, first) == "bk":
                            v.fail(f"a hot face is the brickwork's at {first}")
                        continue
                    n += 1
                    d = 0.125
                    while d < t + 0.5:
                        q = list(pnt)
                        q[k] += sign * d
                        w = what_is(v, q) if 0.0 < q[k] < (m.W, m.H, m.D)[k] else None
                        if w != "ln":
                            if w == "bk" and d - 0.125 < t - 1e-6:
                                if d - 0.125 < thinnest:
                                    thinnest, where = d - 0.125, pnt
                            break
                        d += 0.25
    cold, wrong = 0, []
    (mx0, mx1), (my0, my1) = m.MOUTH
    for (lo, hi) in ((m.CAVE[0][0], m.CAVE[1][0], m.CAVE[2][0]), (m.CAVE[0][1], m.CAVE[1][1], m.CAVE[2][1])), \
                    ((mx0, my0, 1.0), (mx1, my1, m.CAVE[2][0])):
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
        v.fail(f"the ash pit or the ash mouth is lined at {len(wrong)} points, e.g. {wrong[:3]}")
    print(f"lining: at least {t} of the lining's brick behind the hot faces ({n} points, walked out every quarter) or "
          f"the lining reaching through; the ash pit and the ash mouth the brickwork's ({cold} points)")


def check_build(v):
    """The stages' counts *(review)*: the brickwork's and the lining's volumes, measured from their elements (each
    lattice box in exactly one brick box: their volumes add up to the lattice's), give the rig's quantities at the
    game's rates, rounded up; the lid stage and the frame's recipe are as settled; every stage's requires is a part's."""
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
    want = {"bricks": math.ceil(lat["bk"] / m.B ** 3 * m.BRICKS_PER_BLOCK - 1e-9)}
    stands = len(m.POTS) * m.oct_area(m.STAND[2]) * (m.STAND[1] - m.STAND[0])
    want["lining"] = math.ceil((lat["ln"] + stands) / m.B ** 3 * m.LINING_PER_BLOCK - 1e-9)
    want["fireclay"] = math.ceil(want["lining"] / m.LINING_PER_FIRECLAY - 1e-9)
    if got != want or any(got[k] != c[k] for k in got):
        v.fail(f"the stages' counts {got} are not the measured {want}")
    if [s["requires"] for s in build["stages"]] != list(m.STAGES):
        v.fail("the stages are not brickwork, lining, lid")
    if st["brickwork"]["materials"][0]["code"] != "game:burnedbrick-*" or not st["brickwork"].get("oneType"):
        v.fail("the brickwork does not take one type of any fired brick")
    if [(x["code"], x["quantity"]) for x in st["lid"]["materials"]] != list(m.LID_STAGE):
        v.fail("the lid stage's materials are not the settled ones")
    if [(x["code"], x["quantity"]) for x in build["frame"]["ingredients"]] != list(m.FRAME_RECIPE):
        v.fail("the frame's recipe is not the settled one")
    requires = {p["requires"] for p in v.parts}
    if not set(m.STAGES) <= requires or any(f"damper{d}" not in requires for d in m.DAMPERS):
        v.fail("a stage's requires is no part's")
    print(f"build: brickwork {lat['bk'] / m.B ** 3:.2f} blocks -> {got['bricks']} fired bricks; lining "
          f"{(lat['ln'] + stands) / m.B ** 3:.2f} blocks (the stands {stands / m.B ** 3:.3f}) -> {got['lining']} "
          f"refractory bricks and {got['fireclay']} fire clay; lid and damper {dict(m.LID_STAGE)}; the frame "
          f"{dict(m.FRAME_RECIPE)}")


# ---------------------------------------------------------------- the lid
def lid_body(v, depth):
    return v.posed("lid", depth)


def check_lid(v):
    """The lid *(review)*: shut, flush in its seat (its box, the knuckles aside, the seat's, its front face stepped
    back inside the arc its top front edge swings on, so it clears the seat's front wall), on the seat's ledge all
    round; hinged about its top back edge, so nothing of it passes behind that edge under the floor (no pit): its two
    knuckles turn about the axis in their beds, the pins run through them and PIN_IN into the side walls, bedded in
    brick. Open, turned OPEN_DEG about the axis, it rests just past upright with its weight behind the axis, its back
    face on the two stops' top front edges, standing in the cells over the hole (out of the chimney's). The lid is the
    only part that moves, and theta moves nothing."""
    m = v.m
    zb = m.PIVOT[1]
    seat = ([m.SEAT_X[0], m.SEAT_Y, m.SEAT_Z[0]], [m.SEAT_X[1], m.FLOOR_TOP, m.SEAT_Z[1]])
    shut = aabb_of([e for e in v.posed("lid", m.SHUT) if not e.name.startswith("lid_knuckle")])
    steps = m.front_steps()
    seat[0][2] += steps[0][2]
    if not same_box(shut, seat, tol=1e-4):
        v.fail(f"the lid is not flush in its seat when shut: {shut}")
    length = m.SEAT_Z[1] - m.SEAT_Z[0]                # the top front edge's distance from the axis
    reach = max(math.hypot(c[1] - m.PIVOT[0], c[2] - m.PIVOT[1]) for e in v.posed("lid", m.SHUT)
                if not e.name.startswith("lid_knuckle") for c in box_corners(e))
    if reach > length + 1e-6:
        v.fail(f"a corner of the lid is {reach - length:.3f} farther from the axis than its top front edge: it swings "
               "into the seat's front wall")
    if steps[0][2] > 0.01 or steps[0][1] != m.FLOOR_TOP:
        v.fail("the lid's front face is set back at the floor")
    bricks = brick(v)
    frame = v.by_part["frame"]
    y = m.SEAT_Y - 0.1
    xm, zm = (m.SEAT_X[0] + m.SEAT_X[1]) / 2, (m.HOLE_Z[0] + m.HOLE_Z[1]) / 2
    for p in ((m.SEAT_X[0] + 0.25, y, zm), (m.SEAT_X[1] - 0.25, y, zm), (xm - 6.0, y, m.SEAT_Z[0] + 0.25),
              (m.SEAT_X[0] + 3.0, y, m.SEAT_Z[1] - 0.25), (m.SEAT_X[1] - 3.0, y, m.SEAT_Z[1] - 0.25)):
        if not any(inside(e, p) for e in bricks):
            v.fail(f"no ledge under the shut lid at {p}")
    # the hinge: nothing of the lid behind its top back edge under the floor, at any depth, but the knuckles
    behind = 0.0
    for d in depths(0.02):
        for e in v.posed("lid", d):
            if e.name.startswith("lid_knuckle"):
                continue
            lo, hi = e.aabb()
            if lo[1] < m.FLOOR_TOP - 1e-6 and hi[2] > zb + 1e-6:
                corners = [c for c in box_corners(e) if c[1] < m.FLOOR_TOP - 1e-6]
                behind = max(behind, max(c[2] - zb for c in corners))
    if behind > 1e-4:
        v.fail(f"the lid passes {behind:.3f} behind its back edge under the floor: it would need a pit")
    pins = [e for e in v.by_part["hinge"] if e.name.startswith("hg_pin")]
    for d in (0.0, 0.5, m.SHUT):
        kn = [e for e in v.posed("lid", d) if e.name.startswith("lid_knuckle")]
        for x0, x1 in m.KNUCKLE_X:
            for x in (x0 + 0.2, (x0 + x1) / 2, x1 - 0.2):
                if not any(inside(k, (x, m.PIVOT[0], zb)) for k in kn) or not any(inside(q, (x, m.PIVOT[0], zb)) for q in pins):
                    v.fail(f"the pin is not in the knuckle at x {x}, depth {d}")
    bedded = 0
    for x0, x1 in ((m.SEAT_X[0] - m.PIN_IN, m.SEAT_X[0]), (m.SEAT_X[1], m.SEAT_X[1] + m.PIN_IN)):
        xc = (x0 + x1) / 2
        if not any(inside(q, (x0 + 0.05, m.PIVOT[0], zb)) for q in pins) or not any(inside(q, (x1 - 0.05, m.PIVOT[0], zb)) for q in pins):
            v.fail(f"a pin does not run {m.PIN_IN} into its side wall")
        out = x0 - 0.1 if x0 < m.SEAT_X[0] else x1 + 0.1                       # beyond the pin's end
        for p in ((xc, m.FLOOR_TOP - m.PIN_BED - 0.1, zb), (xc, m.FLOOR_TOP - 0.1, zb - m.PIN_BED - 0.1),
                  (xc, m.FLOOR_TOP - 0.1, zb + m.PIN_BED + 0.1), (out, m.FLOOR_TOP - 0.1, zb)):
            if any(inside(e, p) for e in bricks):
                bedded += 1
            else:
                v.fail(f"a pin's bed is not in brick at {p}")
    # open: its weight behind the axis, its back face on the stops' top front edges
    els = v.posed("lid", 0.0)
    vol = sum(abs(e.size[0] * e.size[1] * e.size[2]) for e in els)
    cz = sum(e.c[2] * abs(e.size[0] * e.size[1] * e.size[2]) for e in els) / vol
    if cz <= zb:
        v.fail(f"the open lid's weight is not behind its axis ({cz - zb:+.3f}): it would fall shut")
    a = math.radians(m.OPEN_DEG)
    n = (math.cos(a), math.sin(a))                    # the lid's top face's normal (y, z), turned open; the face holds the axis
    stops = [e.aabb() for e in v.by_part["hinge"] if e.name.startswith("hg_stop")]
    gaps = [n[0] * (s[1][1] - m.PIVOT[0]) + n[1] * (s[0][2] - zb) for s in stops]
    if len(stops) != 2 or any(abs(g) > 0.01 for g in gaps):
        v.fail(f"the open lid does not rest on the stops' edges: {gaps}")
    if any(abs(s[1][1] - (m.FLOOR_TOP + m.STOP_H)) > 1e-6 or abs(s[0][2] - m.STOP_Z) > 1e-6 for s in stops):
        v.fail("the stops' edges are not where the lid rests")
    if any(s[0][0] < m.SEAT_X[0] or s[1][0] > m.SEAT_X[1] for s in stops):
        v.fail("a stop is not behind the lid")
    lo, hi = aabb_of(els)
    for pid in (p["id"] for p in v.parts):
        for d in (0.3, 0.8):
            ma = m.pm(v.parts, pid, (0.0, d, None))
            mb = m.pm(v.parts, pid, (3.7, d, None))
            if max(abs(ma[r][c] - mb[r][c]) for r in range(3) for c in range(4)) > 1e-12:
                v.fail(f"theta moves {pid}")
        if pid != "lid":
            mc = m.pm(v.parts, pid, (0.0, 0.6, None))
            if max(abs(mc[r][q] - (1.0 if r == q else 0.0)) for r in range(3) for q in range(4)) > 1e-12:
                v.fail(f"depth moves {pid}")
    print(f"lid: shut flush in its seat on the ledge all round; nothing of it behind its back edge under the floor over "
          f"its swing (worst {behind:.1e}, no pit); the pins through both knuckles and {m.PIN_IN} into each side wall, "
          f"bedded ({bedded} points of brick round them); open {m.OPEN_DEG} degrees, its weight {cz - zb:.2f} behind the "
          f"axis, its back face {max(abs(g) for g in gaps):.1e} off the stops' edges, standing to y {hi[1]:.2f} and z "
          f"{hi[2]:.2f} (the cells over the hole end at {2 * m.B:.0f}); theta moves nothing, depth only the lid")


def box_corners(e):
    out = []
    for sx in (-0.5, 0.5):
        for sy in (-0.5, 0.5):
            for sz in (-0.5, 0.5):
                loc = (sx * e.size[0], sy * e.size[1], sz * e.size[2])
                out.append([e.c[k] + sum(e.r[k][q] * loc[q] for q in range(3)) for k in range(3)])
    return out


# ---------------------------------------------------------------- clearances
def allowed(pa, pb, ea, eb):
    """Intended overlaps: a lump lying on a coke layer's top is inside the layer over it (the same kind), and the
    hinge's pins run through the lid's knuckles, along its back bar's top back edge."""
    names = {ea.name, eb.name}
    if {pa, pb} == {"lid", "hinge"} and any(n.startswith("hg_pin") for n in names) and any(
            n.startswith(("lid_knuckle", "lid_frame_s")) for n in names):
        return True
    ra, rb = re.fullmatch(r"(coke|fire)(\d)", pa), re.fullmatch(r"(coke|fire)(\d)", pb)
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
    """The furnace built: every state of the pots with the damper open and the outlet open, and every place of the
    damper with the outlet capped; and each stage of the build before it is."""
    return ([(st, "open", False, "complete") for st in m.POT_STATE_IDS] + [("cold", d, True, "complete") for d in m.DAMPERS]
            + [("cold", "open", False, "frame"), ("cold", "open", False, "brickwork"), ("cold", "open", True, "lining")])


def check_clearances(v, step):
    """In every scene, nothing of one part runs into another's (the brick, the iron and the stands are one part) with
    the lid open; and the lid, swung every `step` of depth, touches nothing."""
    m = v.m
    allhits = {}
    for scene in scenes(m):
        items = [(e.part, e, e.aabb()) for e in v.shown(scene, 0.0)]
        for key, hs in touches(items).items():
            allhits.setdefault(key, {}).update({h: (scene, 0.0) for h in hs})
        for dp in depths(step):
            items = [(e.part, e, e.aabb()) for e in v.shown(scene, dp)]
            for key, hs in touches(items, movers={"lid"}).items():
                allhits.setdefault(key, {}).update({h: (scene, dp) for h in hs})
    print(f"clearances in {len(scenes(m))} scenes with the lid open and swung every {step} of depth: "
          f"{'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} ({s[0]}, depth {d:.2f})" for (a, b), (s, d) in ex))
    if allhits:
        v.fail("parts run into each other")


# ---------------------------------------------------------------- the pots, the coke, the melt
def inside_volume(v, i, y0, y1, dy=0.01):
    """The volume inside pot i from y0 to y1, from its walls' elements: at each height, the octagon whose apothem is
    the nearest of the walls' inner faces to the pot's axis."""
    m = v.m
    cx, cz = m.POT_X[i], m.POT_Z
    walls = []
    for e in v.by_part[f"pot{i}"]:
        if "_foot" in e.name:
            continue
        k = min((0, 2), key=lambda q: abs(e.size[q]))           # the thin local axis: the wall's normal
        n = [e.r[q][k] for q in range(3)]
        d = abs((e.c[0] - cx) * n[0] + (e.c[2] - cz) * n[2]) - abs(e.size[k]) / 2
        lo, hi = e.aabb()
        walls.append((lo[1], hi[1], d))
    vol, y = 0.0, y0 + dy / 2
    while y < y1:
        a = min(d for lo, hi, d in walls if lo <= y <= hi)
        vol += m.oct_area(a) * min(dy, y1 - (y - dy / 2))
        y += dy
    return vol


def check_pots(v):
    """Each pot on its stand on the grate (its foot covering the stand), its mouth under the shut lid, coke room round
    it (to the walls and between the two); the charge and the melt inside it; and its melt holds a full pot *(review)*:
    the pot's inside from its floor to the melt's top, measured from its walls' elements, holds POT_UNITS at
    VOXELS_PER_UNIT within 2 %, the melt at least a voxel under the rim."""
    m = v.m
    rooms, helds = [], []
    for i in m.POTS:
        stand = aabb_of([e for e in v.by_part["lining"] if e.name.startswith(f"ln_stand{i}_")])
        pot = aabb_of(v.by_part[f"pot{i}"])
        foot = aabb_of([e for e in v.by_part[f"pot{i}"] if "_foot" in e.name])
        if abs(stand[0][1] - m.GRATE_TOP) > 1e-9 or abs(pot[0][1] - stand[1][1]) > 1e-9:
            v.fail(f"pot {i} is not on its stand on the grate")
        if foot[0][0] > stand[0][0] or foot[1][0] < stand[1][0] or foot[0][2] > stand[0][2] or foot[1][2] < stand[1][2]:
            v.fail(f"pot {i}'s foot does not cover its stand")
        if pot[1][1] > m.SEAT_Y - 2.0:
            v.fail(f"pot {i} reaches the lid")
        rooms += [pot[0][0] - m.HOLE_X[0] if i == 1 else m.HOLE_X[1] - pot[1][0], pot[0][2] - m.HOLE_Z[0], m.HOLE_Z[1] - pot[1][2]]
        for what in ("charge", "melt"):
            lo, hi = aabb_of([e for e in v.by_part[f"{what}{i}"] if "_piece" not in e.name])
            if lo[1] < m.POT_BASE[1] or hi[1] > m.POT_TOP - 1.0:
                v.fail(f"pot {i}'s {what} is not inside it")
        top = aabb_of(v.by_part[f"charge{i}"])[1][1]
        if top > m.SEAT_Y - 1.0:
            v.fail(f"pot {i}'s charge reaches the lid ({top:.2f})")
        floor = max(e.aabb()[1][1] for e in v.by_part[f"pot{i}"] if "_foot" in e.name)
        level = max(e.aabb()[1][1] for e in v.by_part[f"melt{i}"])
        held = inside_volume(v, i, floor, level) / m.VOXELS_PER_UNIT
        helds.append(held)
        if abs(held - m.POT_UNITS) > 0.02 * m.POT_UNITS:
            v.fail(f"pot {i}'s melt holds {held:.0f} units, not a full pot's {m.POT_UNITS}")
        if m.POT_TOP - level < 1.0:
            v.fail(f"pot {i}'s melt stands {m.POT_TOP - level:.2f} under the rim")
    between = aabb_of(v.by_part["pot2"])[0][0] - aabb_of(v.by_part["pot1"])[1][0]
    rooms.append(between)
    if min(rooms) < 1.5 - 1e-6:
        v.fail(f"the pots leave {min(rooms):.2f} of coke room")
    print(f"pots: two on their stands on the grate, their mouths {m.SEAT_Y - m.POT_TOP:.1f} under the shut lid, coke "
          f"room {min(rooms[:-1]):.1f} to the walls and {between:.1f} between them; each melt at {level:.2f}, "
          f"{m.POT_TOP - level:.2f} under the rim, holding {', '.join(f'{h:.1f}' for h in helds)} units (a full pot: "
          f"{m.POT_UNITS} at {m.VOXELS_PER_UNIT} cubic voxels a unit, measured from the walls); the charges to {top:.2f}")


def octagon_has(cx, cz, a, x, z):
    dx, dz = abs(x - cx), abs(z - cz)
    return dx <= a and dz <= a and (dx + dz) <= a * math.sqrt(2.0)


def check_coke(v):
    """The coke's layers stack from the grate to the pots' shoulders, four coke a layer; at the top of each the coke
    meets both pots with no gap (its openings the pots' widest section in the layer); each fills the hole across from
    the walls to the pots (sampled); its top, lumps and all, stays under the flue opening; the burning layers are the
    same boxes as the cold ones."""
    m = v.m
    layers = m.COKE_LAYERS
    if abs(layers[0][0] - m.GRATE_TOP) > 1e-9 or any(abs(layers[k][1] - layers[k + 1][0]) > 1e-9 for k in range(len(layers) - 1)):
        v.fail("the coke's layers do not stack from the grate")
    belly = next(t for t in m.POT_TUBES if t[0] == "belly")
    if abs(layers[-1][1] - belly[2]) > 1e-9:
        v.fail("the coke does not come up to the pots' shoulders (the belly's top)")
    if m.COKE_CAPACITY != len(layers) * m.COKE_PER_LAYER:
        v.fail(f"the layers do not hold {m.COKE_CAPACITY} coke (CokeCapacity)")
    worst_gap, holes_in = 0.0, 0
    (hx0, hx1), (hz0, hz1) = m.HOLE_X, m.HOLE_Z
    for j, (y0, y1) in enumerate(layers, 1):
        a = m.coke_opening(j)
        gap = a - m.pot_apothem(y1 - 1e-6)
        worst_gap = max(worst_gap, abs(gap))
        if abs(gap) > 1e-9:
            v.fail(f"coke layer {j} leaves a gap of {gap:.3f} round the pots at its top")
        els = [e for e in v.by_part[f"coke{j}"] if "_lump" not in e.name]
        y = (y0 + y1) / 2
        nx, nz = 48, 24
        for p in range(nx + 1):
            for q in range(nz + 1):
                x, z = hx0 + 0.05 + (hx1 - hx0 - 0.1) * p / nx, hz0 + 0.05 + (hz1 - hz0 - 0.1) * q / nz
                if any(octagon_has(m.POT_X[i], m.POT_Z, a + 0.01, x, z) for i in m.POTS):
                    continue
                if not any(inside(e, (x, y, z), eps=-0.001) for e in els):
                    holes_in += 1
        fire = {e.name.split("_", 1)[1]: e.aabb() for e in v.by_part[f"fire{j}"]}
        for e in v.by_part[f"coke{j}"]:
            f = fire.get(e.name.split("_", 1)[1])
            if f is None or not same_box(e.aabb(), f):
                v.fail(f"{e.name} has no burning twin in fire{j}")
    top = max(e.aabb()[1][1] for j in (1, 2, 3) for e in v.by_part[f"coke{j}"])
    if top >= m.FLUE_OPENING[1][0]:
        v.fail(f"the coke reaches the flue opening ({top:.2f})")
    if holes_in:
        v.fail(f"{holes_in} sampled points of the coke's layers are neither coke nor a pot's place")
    print(f"coke: {len(layers)} layers of {m.COKE_PER_LAYER} from the grate ({m.GRATE_TOP}) to the shoulders "
          f"({layers[-1][1]}), meeting both pots at each top (worst {worst_gap:.1e}); every layer whole across; its top "
          f"{top:.2f} under the flue opening ({m.FLUE_OPENING[1][0]}); the burning layers the same boxes")


# ---------------------------------------------------------------- the damper, the outlet
def check_damper(v):
    """The ash-pit damper *(review)*: in each of its three places a plate in its recess behind the front face, between
    its guides; shut it covers the whole ash mouth, half its upper half (the mouth open below its middle), open none
    of it; it never leaves the recess."""
    m = v.m
    (mx0, mx1), (my0, my1) = m.MOUTH
    (rx, ry, rz) = m.RECESS
    gx = (m.GUIDES[0][1], m.GUIDES[1][0])
    want = {"shut": my0, "half": (my0 + my1) / 2, "open": my1}
    for place in m.DAMPERS:
        els = v.by_part[f"damper{place}"]
        plate = next(e for e in els if e.name.endswith("_plate"))
        lo, hi = plate.aabb()
        allo, alhi = aabb_of(els)
        if allo[2] < rz[0] - 1e-9 or alhi[2] > rz[1] + 1e-9 or allo[1] < ry[0] - 1e-9 or alhi[1] > ry[1] + 1e-9:
            v.fail(f"the damper leaves its recess when {place}")
        if abs(lo[0] - gx[0]) > 1e-9 or abs(hi[0] - gx[1]) > 1e-9 or lo[0] > mx0 or hi[0] < mx1:
            v.fail(f"the damper's plate is not between its guides across the mouth when {place}")
        covers = (max(lo[1], my0), min(hi[1], my1))
        if place == "shut" and not (lo[1] <= my0 + 1e-9 and hi[1] >= my1):
            v.fail("the damper does not cover the ash mouth when shut")
        if place == "half" and abs(lo[1] - want["half"]) > 1e-9:
            v.fail(f"the half damper's underside is at {lo[1]}, not the mouth's middle {want['half']}")
        if place == "half" and hi[1] < my1:
            v.fail("the half damper does not cover the mouth's upper half")
        if place == "open" and lo[1] < my1 - 1e-9:
            v.fail("the open damper does not clear the ash mouth")
        print(f"damper {place}: covers the mouth from {covers[0]:.1f} to {max(covers[0], covers[1]):.1f} "
              f"(the mouth {my0}..{my1}); in its recess between its guides")


def check_outlet(v):
    """The outlet *(review)*: open, its mouth clear to the sky through the stack; capped, a flat iron plate flush with
    the floor in the curb's seat, covering the stack's mouth; the curb, the lining's refractory brick, a rim round the
    cap (sampled every half voxel) and a ledge under its edge all round."""
    m = v.m
    cap = aabb_of(v.by_part["outletcap"])
    (cx0, cx1), _, (cz0, cz1) = m.CURB
    r = m.CURB_RIM
    if not same_box(cap, ([cx0 + r, m.CAP_Y[0], cz0 + r], [cx1 - r, m.FLOOR_TOP, cz1 - r])):
        v.fail(f"the cap is not flush in the curb's seat: {cap}")
    lining = [e for e in v.by_part["lining"] if e.name.startswith("ln_brick_")]
    (sx0, sx1), _, (sz0, sz1) = m.STACK
    rim = [(x + 0.25, m.FLOOR_TOP - 0.25, z + 0.25) for x in range(int(cx0), int(cx1)) for z in range(int(cz0), int(cz1))
           if not (cx0 + r <= x + 0.25 <= cx1 - r and cz0 + r <= z + 0.25 <= cz1 - r)]
    ledge = [(x + 0.25, m.CAP_Y[0] - 0.1, z + 0.25) for x in range(int(cx0 + r), int(cx1 - r)) for z in range(int(cz0 + r), int(cz1 - r))
             if not (sx0 <= x + 0.25 <= sx1 and sz0 <= z + 0.25 <= sz1)]
    missing = [p for p in rim + ledge if not any(inside(e, p) for e in lining)]
    if missing:
        v.fail(f"the curb is not the lining's brick at {len(missing)} points, e.g. {missing[:3]}")
    (sx0, sx1), _, (sz0, sz1) = m.STACK
    if not (cap[0][0] < sx0 and cap[1][0] > sx1 and cap[0][2] < sz0 and cap[1][2] > sz1):
        v.fail("the cap does not cover the stack's mouth")
    print(f"outlet: open to the sky through the stack; capped by a plate flush in the curb's seat ({cap[1][0] - cap[0][0]:.0f} "
          f"square, {m.CAP_Y[1] - m.CAP_Y[0]} thick), over the stack's mouth; the curb the lining's brick, rim and ledge "
          f"({len(rim) + len(ledge)} points)")


# ---------------------------------------------------------------- the draught: grate, cave, flue, stack
def empty(v, p):
    return not any(inside(e, p) for e in static(v))


def check_draught(v):
    """The way the air goes, natural draught only: in at the ash mouth into the cave, up between the firebars, through
    the fire, out at the flue opening near the top of the hole's back wall, along the back flue and up the stack to
    the outlet. Sampled points along it lie in no element of the brick or the frame, and the stack and the outlet's mouth in no
    element at all at any depth but the cap. The firebars lie on their bearers and the bearers' ends are built into
    the walls."""
    m = v.m
    (mx0, mx1), (my0, my1) = m.MOUTH
    xm, ym = (mx0 + mx1) / 2, (my0 + my1) / 2
    (cx0, cx1), (cy0, cy1), _ = m.CAVE
    path = [(xm, ym, z) for z in (-0.5, 0.5, 1.5) + tuple(range(3, int(m.HOLE_Z[0]) + 1, 2))]
    path += [(x + 0.5, (cy0 + cy1) / 2, m.POT_Z) for x in range(int(cx0), int(cx1), 2)]
    bars = sorted((e.aabb() for e in v.by_part["frame"] if e.name.startswith("fr_bar") and e.name.endswith("w")), key=lambda b: b[0][2])
    hx0, hx1 = m.HOLE_X
    for (alo, ahi), (blo, _) in zip(bars, bars[1:]):
        gz = (ahi[2] + blo[2]) / 2
        path += [(hx0 + (hx1 - hx0) * f, (m.BAR_Y[0] + m.BAR_Y[1]) / 2, gz) for f in (0.15, 0.3, 0.4, 0.6, 0.7, 0.85)]
    (fx0, fx1), (fy0, fy1), (fz0, fz1) = m.FLUE_OPENING
    fx, fy = (fx0 + fx1) / 2, (fy0 + fy1) / 2
    (bx0, bx1), (by0, by1), (bz0, bz1) = m.BACK_FLUE
    (sx0, sx1), (sy0, _), (sz0, sz1) = m.STACK
    sx, sz = (sx0 + sx1) / 2, (sz0 + sz1) / 2
    path += [(fx, fy, m.POT_Z), (fx, fy, m.HOLE_Z[1] - 0.5)] + [(fx, fy, fz0 + 0.5 + q) for q in range(int(fz1 - fz0))]
    path += [((bx0 + bx1) / 2, (by0 + by1) / 2, bz0 + 0.5 + q) for q in range(int(bz1 - bz0))]
    path += [(sx, sy0 + 0.5 + q * 0.5, sz) for q in range(int((m.FLOOR_TOP + m.B - sy0) * 2) - 1)]
    if not (fx0 >= bx0 and fx1 <= bx1 and fy0 >= by0 and fy1 <= by1 and abs(fz1 - bz0) < 1e-9 and sx0 >= bx0 and sx1 <= bx1
            and sz0 >= bz0 and sz1 <= bz1 and sy0 <= by0):
        v.fail("the flue opening, the back flue and the stack do not run into each other")
    blocked = [p for p in path if not empty(v, p)]
    if blocked:
        v.fail(f"the draught's way is blocked at {blocked[:5]}")
    mouth_lo, mouth_hi = [sx0, sy0, sz0], [sx1, m.FLOOR_TOP + m.B, sz1]
    for dp in depths(0.05):
        for pid in v.by_part:
            if pid == "outletcap":
                continue
            for e in v.posed(pid, dp):
                lo, hi = e.aabb()
                if all(lo[q] < mouth_hi[q] - 0.02 and mouth_lo[q] < hi[q] - 0.02 for q in range(3)):
                    v.fail(f"{e.name} is in the stack or over the outlet at depth {dp}")
    frame = v.by_part["frame"]
    bearers = [e.aabb() for e in frame if e.name.startswith("fr_bearer")]
    for e in frame:
        if not e.name.startswith("fr_bar"):
            continue
        lo, hi = e.aabb()
        under = [b for b in bearers if b[0][0] < hi[0] and b[1][0] > lo[0]]
        if len(under) != 2 or any(abs(lo[1] - b[1][1]) > 1e-9 or not (b[0][2] <= lo[2] and b[1][2] >= hi[2]) for b in under):
            v.fail(f"{e.name} does not lie on a bearer at each end")
    for blo, bhi in bearers:
        xc = (blo[0] + bhi[0]) / 2
        for z in (m.HOLE_Z[0] - 0.5, m.HOLE_Z[1] + 0.5):
            for y in (bhi[1] + 0.2, blo[1] - 0.2):
                if not any(inside(e, (xc, y, z)) for e in brick(v)):
                    v.fail(f"a bearer is not built into the wall at x {xc}, z {z}")
    gap = (m.HOLE_Z[1] - m.HOLE_Z[0] - m.BARS * m.BAR_W) / (m.BARS - 1)
    if gap < 0.8:
        v.fail(f"the firebars' gaps ({gap:.2f}) are too narrow for the draught")
    print(f"draught: natural, in at the ash mouth, up through {m.BARS - 1} gaps of {gap:.2f} between the firebars, out "
          f"at the flue opening ({fy0}..{fy1}, under the lid at {m.SEAT_Y}), along the back flue to the stack and the "
          f"outlet: {len(path)} points clear; the stack's mouth clear at every depth but for the cap; every firebar on a "
          f"bearer at each end, the bearers built into the walls")


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
    check_walls(v)
    check_lining(v)
    check_build(v)
    check_lid(v)
    check_pots(v)
    check_coke(v)
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
    """Any fired brick *(review)*: the rig's bricks name every type of the game's burnedbrick and nothing else, each
    with the block its bricks make and that block's textures; the shape's own `brick` is the default type's colour
    layer. With the game's assets at hand, every texture of the mapping and of the shape is a file there, every block
    one of the game's, and the types the game's."""
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
        want = m.FIRE_GLOW if re.fullmatch(r"fire\d", stem) else m.MELT_GLOW if re.fullmatch(r"melt\d", stem) else None
        got = {f.get("glow") for f in e["faces"].values()}
        if got != {want}:
            fail(f"{e['name']}'s faces glow {got}, not {want}")
    if "powerCell" in ship or "powerFace" in ship:
        fail("a coke-fired furnace has no power cell")
    if any(k.startswith("air") for k in ship):
        fail("a natural-draught furnace has no air port")
    cells = {tuple(c["pos"]): c for c in ship["cells"]}
    lidcells = {tuple(c[k] - m.ORIGIN_CELL[k] for k in range(3)) for c in m.LID_CELLS}
    hollow = {p for p, c in cells.items() if c.get("hollow")}
    lids = {p for p, c in cells.items() if "lid" in c}
    top = {p for p in cells if p not in lidcells and p[1] == m.CELLS_Y - 1}
    if hollow != lidcells or any(cells[p].get("boxes") for p in hollow):
        fail(f"the hollow cells are {sorted(hollow)}, not the lid's {sorted(lidcells)}")
    if lids != top or any(cells[p]["lid"] != 1.0 for p in lids):
        fail(f"decks on {sorted(lids)}, not at the floor on the brickwork's top cells {sorted(top)}")
    if any(not cells[p].get("boxes") for p in cells if p not in lidcells):
        fail("a cell of the brickwork has no boxes")
    chim = tuple(m.CHIMNEY_CELL[k] - m.ORIGIN_CELL[k] for k in range(3))
    free = [tuple(c[k] - m.ORIGIN_CELL[k] for k in range(3)) for c in m.FREE_CELLS]
    if chim in cells or any(f in cells for f in free):
        fail("the chimney's cell or a cell beside or before it is the furnace's")
    out = tuple(ship["outletCell"])
    if (out[0], out[1] + 1, out[2]) != chim or ship["outletFace"] != "up":
        fail("the outlet is not under the chimney's cell")
    if out[0] != 0 or out[2] != m.CELLS_Z - 1 - m.ORIGIN_CELL[2]:
        fail("the outlet is not in the back row's middle")
    line = ship["chimney"]
    if abs(line["origin"][1] - line["length"] / 2 - m.CELLS_Y) > 1e-9 or line["axis"] != "y" or line["length"] != m.MIN_CHIMNEY:
        fail("the chimney's line does not stand on the outlet")
    if abs(line["origin"][1] + line["length"] / 2 - (m.CELLS_Y + m.MIN_CHIMNEY)) > 1e-9:
        fail("the chimney's line is not the minimum counted from the outlet up")
    if not (out[0] <= line["origin"][0] <= out[0] + 1 and out[2] <= line["origin"][2] <= out[2] + 1):
        fail("the chimney's line is not over the outlet")
    hole = tuple(ship["holeCell"])
    if ship["holeFace"] != "up" or hole not in cells or hole[1] != m.CELLS_Y - 1:
        fail("the hole's cell is not a top cell worked from above")
    for i in m.POTS:
        p = ship[f"pot{i}"]["pos"]
        if any(not (hole[k] <= p[k] <= hole[k] + 1) for k in range(3)):
            fail(f"pot {i}'s mouth is not in the hole's cell")
    if ship["pot1"]["pos"][0] >= ship["pot2"]["pos"][0]:
        fail("pot 1 is not west of pot 2")
    ash = ship["ash"]["pos"]
    if ship["ashSide"] != "north" or abs(ash[2]) > 1e-9 or not (0.0 <= ash[0] <= 1.0 and 0.0 <= ash[1] <= 1.0):
        fail("the ash mouth is not on the controller cell's front face")
    hinge = ship["hinge"]
    if hinge["axis"] != "x" or abs(hinge["origin"][1] * m.B - m.PIVOT[0]) > 1e-3 or abs(hinge["origin"][2] * m.B - m.PIVOT[1]) > 1e-3:
        fail("the hinge's line is not the lid's axis")
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, "
          f"{len(hollow)} hollow (the lid's), decks on {len(lids)}; glow on the burning coke and the melt only; no power "
          f"cell, no air port; the hole's cell {list(hole)} holding both pots' mouths; the ash mouth on the controller's "
          f"front face; the outlet under the chimney's cell {list(chim)}, the chimney {m.MIN_CHIMNEY} blocks from it up; "
          f"the cells round the chimney ({len(free)}) left free")
    return ok
