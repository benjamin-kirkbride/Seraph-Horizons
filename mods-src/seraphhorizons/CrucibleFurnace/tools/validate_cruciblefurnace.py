"""The crucible furnace generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its rule.
Those marked (review) came from the owner's review. Everything is in the build frame (voxels), on the model before the
origin shift; `check_shipped` in make_shape.py proves the shipped files are this model moved.
"""

from __future__ import annotations

import math
import re

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
    return [e for e in v.by_part["frame"] if e.name.startswith("fr_brick")]


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
    print(f"parts: {len(v.parts)}, elements {len(v.els)}; frame {counts['frame']} ({len(brick(v))} of brick); "
          + ", ".join(f"{p} {n}" for p, n in counts.items() if p != "frame"))


def check_floating(v):
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")


ROLES = [
    (r"^fr_brick_", {"brick"}),
    (r"^fr_(bearer|bar|lintel|guide|pin|bracket|stop|curb|rim|strap|kerb)", {"iron"}),
    (r"^fr_stand\d", {"clay"}),
    (r"^lid_(frame|handle)", {"iron"}),
    (r"^lid_(fill|pocket)", {"brick"}),
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
def overhang(m, lo, hi):
    """How far (voxels) a box pokes out of where the model may be: the brickwork's box, and the cells over the hole
    (where the lid stands open); negative when inside."""
    worst = max(-lo[0], hi[0] - m.W, -lo[1], -lo[2], hi[2] - m.D)
    if hi[1] > m.H:
        (rlo, rhi) = m.regions()[1]
        worst = max(worst, rlo[0] - lo[0], hi[0] - rhi[0], hi[1] - rhi[1], rlo[2] - lo[2], hi[2] - rhi[2])
    return worst


def check_containment(v):
    """Nothing leaves the brickwork and the two cells over the hole over the lid's whole swing (so nothing reaches the
    chimney's cell or the one beside it); with the lid shut nothing stands over the floor, and with it open only the
    lid does."""
    m = v.m
    worst, where = -1e9, None
    over = {}
    for dp in depths(0.01):
        for pid in v.by_part:
            for el in v.posed(pid, dp):
                lo, hi = el.aabb()
                o = overhang(m, lo, hi)
                if o > worst:
                    worst, where = o, (el.name, dp)
                if dp in (0.0, m.SHUT) and hi[1] > m.H + 1e-6:
                    over.setdefault(dp, set()).add(pid)
    print(f"containment over the lid's swing (every 0.01): worst overhang {worst:.3f} voxels ({where[0]} at depth "
          f"{where[1]:.2f}); over the floor with the lid shut: {sorted(over.get(m.SHUT, [])) or 'nothing'}, open: "
          f"{sorted(over.get(0.0, [])) or 'nothing'}")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the furnace's cells at depth {where[1]}")
    if over.get(m.SHUT) or over.get(0.0, set()) != {"lid"}:
        v.fail("something but the open lid stands over the floor")


def check_top(v):
    """The top is flat *(review)*: no recess but the lid's seat, the hinge pit behind it and the outlet's mouth.
    Every half voxel of it, just under the floor, is brick or flush iron whose top is the floor; nothing of the frame
    stands above it."""
    m = v.m
    frame = v.by_part["frame"]
    (cx0, cx1), _, (cz0, cz1) = m.CURB

    def opening(x, z):
        if m.SEAT_X[0] <= x <= m.SEAT_X[1] and m.SEAT_Z[0] <= z <= m.PIT_BACK:
            return True
        return cx0 + m.CURB_RIM <= x <= cx1 - m.CURB_RIM and cz0 + m.CURB_RIM <= z <= cz1 - m.CURB_RIM
    bad, n = [], 0
    x = 0.25
    while x < m.W:
        z = 0.25
        while z < m.D:
            if not opening(x, z):
                n += 1
                under = [e for e in frame if inside(e, (x, m.FLOOR_TOP - 0.05, z))]
                if not under or any(abs(e.aabb()[1][1] - m.FLOOR_TOP) > 1e-6 for e in under):
                    bad.append((x, z))
            z += 0.5
        x += 0.5
    high = [e.name for e in frame if e.aabb()[1][1] > m.FLOOR_TOP + 1e-6]
    print(f"top: flat at the floor at {n - len(bad)} of {n} points (every half voxel, the seat, the hinge pit and the "
          f"outlet aside); {len(high)} frame elements above it")
    if bad:
        v.fail(f"the top is not flat at {len(bad)} points, e.g. {bad[:4]}")
    if high:
        v.fail(f"frame elements stand above the top: {high[:4]}")


def check_sides(v):
    """Furnaces tile into a row *(review)*: each side face is flush, plain brick over its whole area (sampled every
    half voxel), and nothing but brick reaches either side plane, of any part at any depth; two furnaces side by side
    (one moved a furnace's width along x) touch nowhere but at their shared walls, over the lid's whole swing."""
    m = v.m
    bricks = brick(v)
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
                if (lo[0] < 1e-6 or hi[0] > m.W - 1e-6) and not e.name.startswith("fr_brick"):
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


# ---------------------------------------------------------------- the lid
def lid_body(v, depth):
    return v.posed("lid", depth)


def check_lid(v):
    """The lid *(review)*: shut, flush in its seat (its box the seat's), on the seat's ledge at its front and sides and
    on the pin at its back; open, turned OPEN_DEG about the pin, resting just past upright with its weight behind the
    pin, its back face against the stop's edge, standing in the cells over the hole (out of the chimney's). The lid is
    the only part that moves, and theta moves nothing."""
    m = v.m
    seat = ([m.SEAT_X[0], m.SEAT_Y, m.SEAT_Z[0]], [m.SEAT_X[1], m.FLOOR_TOP, m.SEAT_Z[1]])
    shut = aabb_of(lid_body(v, m.SHUT))
    if not same_box(shut, seat, tol=1e-4):
        v.fail(f"the lid is not flush in its seat when shut: {shut}")
    bricks = brick(v)
    frame = v.by_part["frame"]
    y = m.SEAT_Y - 0.1
    for p in ((m.SEAT_X[0] + 0.25, y, (m.HOLE_Z[0] + m.HOLE_Z[1]) / 2), (m.SEAT_X[1] - 0.25, y, (m.HOLE_Z[0] + m.HOLE_Z[1]) / 2),
              ((m.SEAT_X[0] + m.SEAT_X[1]) / 2, y, m.SEAT_Z[0] + 0.25)):
        if not any(inside(e, p) for e in bricks):
            v.fail(f"no ledge under the shut lid at {p}")
    pin = [e for e in frame if e.name.startswith("fr_pin")]
    back = [e for e in v.by_part["lid"] if e.name.startswith("lid_frame_s")]
    for d in (0.0, 0.5, m.SHUT):
        bars = [e for e in v.posed("lid", d) if e.name.startswith("lid_frame_s")]
        if not all(any(inside(b, (x, m.PIVOT[0], m.PIVOT[1])) for b in bars) for x in (m.SEAT_X[0] + 1.0, m.SEAT_X[1] - 1.0)):
            v.fail(f"the pin is not in the lid's back bar at depth {d}")
    if not pin or not back:
        v.fail("no hinge pin or no back bar")
    for side, x in (("w", m.BRACKET[0]), ("e", m.BRACKET[1])):
        if not any(inside(e, ((x[0] + x[1]) / 2, m.PIVOT[0], m.PIVOT[1])) for e in pin):
            v.fail(f"the pin does not reach its {side} bracket")
    # open: its weight behind the pin, its back face on the stop's edge
    els = lid_body(v, 0.0)
    vol = sum(abs(e.size[0] * e.size[1] * e.size[2]) for e in els)
    cz = sum(e.c[2] * abs(e.size[0] * e.size[1] * e.size[2]) for e in els) / vol
    if cz <= m.PIVOT[1]:
        v.fail(f"the open lid's weight is not behind its pin ({cz - m.PIVOT[1]:+.3f}): it would fall shut")
    a = math.radians(m.OPEN_DEG)
    n = [0.0, math.cos(a), math.sin(a)]               # the lid's top face's normal, turned open
    top = (0.0, m.FLOOR_TOP, m.PIVOT[1])             # a point of the top face (shut), turned about the pin
    dy, dz = top[1] - m.PIVOT[0], top[2] - m.PIVOT[1]
    p0 = (0.0, m.PIVOT[0] + dy * math.cos(a) - dz * math.sin(a), m.PIVOT[1] + dy * math.sin(a) + dz * math.cos(a))
    edge = (0.0, m.FLOOR_TOP, m.STOP_Z)
    gap = sum(n[k] * (edge[k] - p0[k]) for k in range(3))
    if abs(gap) > 0.01:
        v.fail(f"the open lid does not rest on the stop's edge: {gap:+.4f}")
    stop = aabb_of([e for e in frame if e.name.startswith("fr_stop")])
    if abs(stop[0][2] - m.STOP_Z) > 1e-6 or abs(stop[1][1] - m.FLOOR_TOP) > 1e-6:
        v.fail("the stop's edge is not where the lid rests")
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
    print(f"lid: shut flush in its seat (on the ledge and the pin); open {m.OPEN_DEG} degrees about the pin, its weight "
          f"{cz - m.PIVOT[1]:.2f} behind it, its back face {gap:+.4f} off the stop's edge, standing to y {hi[1]:.2f} and "
          f"z {hi[2]:.2f} (the cells over the hole end at {m.B:.0f}); theta moves nothing, depth only the lid")


# ---------------------------------------------------------------- clearances
def allowed(pa, pb, ea, eb):
    """Intended overlaps: a lump lying on a coke layer's top is inside the layer over it (the same kind), and the
    hinge's pin runs through the lid's back bar."""
    names = {ea.name, eb.name}
    if {pa, pb} == {"lid", "frame"} and any(n.startswith("fr_pin") for n in names) and any(n.startswith("lid_frame_s") for n in names):
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
    """Every state of the pots with the damper open and the outlet open, and every place of the damper with the
    outlet capped."""
    return [(st, "open", False) for st in m.POT_STATE_IDS] + [("cold", d, True) for d in m.DAMPERS]


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
        stand = aabb_of([e for e in v.by_part["frame"] if e.name.startswith(f"fr_stand{i}_")])
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
    """The coke's layers stack from the grate to the pots' shoulders, two coke a layer; at the top of each the coke
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
    if 6 != len(layers) * m.COKE_PER_LAYER:
        v.fail("the layers do not hold six coke (CokeCapacity)")
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
    the floor in the curb's rim, resting on its ring, covering the stack's mouth."""
    m = v.m
    cap = aabb_of(v.by_part["outletcap"])
    (cx0, cx1), _, (cz0, cz1) = m.CURB
    r = m.CURB_RIM
    if not same_box(cap, ([cx0 + r, m.CAP_Y[0], cz0 + r], [cx1 - r, m.FLOOR_TOP, cz1 - r])):
        v.fail(f"the cap is not flush in the curb's rim: {cap}")
    ring = [e for e in v.by_part["frame"] if e.name.startswith("fr_curb")]
    if not ring or any(abs(e.aabb()[1][1] - m.CAP_Y[0]) > 1e-9 for e in ring):
        v.fail("the curb's ring is not under the cap")
    (sx0, sx1), _, (sz0, sz1) = m.STACK
    if not (cap[0][0] < sx0 and cap[1][0] > sx1 and cap[0][2] < sz0 and cap[1][2] > sz1):
        v.fail("the cap does not cover the stack's mouth")
    print(f"outlet: open to the sky through the stack; capped by a plate flush in the rim ({cap[1][0] - cap[0][0]:.0f} "
          f"square, {m.CAP_Y[1] - m.CAP_Y[0]} thick, on the ring), over the stack's mouth")


# ---------------------------------------------------------------- the draught: grate, cave, flue, stack
def empty(v, p):
    return not any(inside(e, p) for e in v.by_part["frame"])


def check_draught(v):
    """The way the air goes, natural draught only: in at the ash mouth into the cave, up between the firebars, through
    the fire, out at the flue opening near the top of the hole's back wall, along the back flue and up the stack to
    the outlet. Sampled points along it lie in no element of the frame, and the stack and the outlet's mouth in no
    element at all at any depth but the cap. The firebars lie on their bearers and the bearers' ends are built into
    the walls."""
    m = v.m
    (mx0, mx1), (my0, my1) = m.MOUTH
    xm = (mx0 + mx1) / 2
    path = [(xm, 4.0, -0.5), (xm, 4.0, 1.0), (xm, 4.0, 3.0), (xm, 5.0, m.POT_Z)]
    path += [(x, 5.0, m.POT_Z) for x in range(5, 28, 2)]
    bars = sorted((e.aabb() for e in v.by_part["frame"] if e.name.startswith("fr_bar") and e.name.endswith("w")), key=lambda b: b[0][2])
    for (alo, ahi), (blo, _) in zip(bars, bars[1:]):
        gz = (ahi[2] + blo[2]) / 2
        path += [(x, (m.BAR_Y[0] + m.BAR_Y[1]) / 2, gz) for x in (7.0, 13.0, 19.0, 25.0)]
    (fx0, fx1), (fy0, fy1), _ = m.FLUE_OPENING
    fx, fy = (fx0 + fx1) / 2, (fy0 + fy1) / 2
    path += [(fx, fy, m.POT_Z), (fx, fy, m.HOLE_Z[1] + 0.5), (fx, fy, 18.0), (fx, fy, 24.0)]
    sx, sz = (m.STACK[0][0] + m.STACK[0][1]) / 2, (m.STACK[2][0] + m.STACK[2][1]) / 2
    path += [(fx + (sx - fx) * t / 8, 25.5, sz) for t in range(9)]
    path += [(sx, y / 2, sz) for y in range(51, 65)]
    blocked = [p for p in path if not empty(v, p)]
    if blocked:
        v.fail(f"the draught's way is blocked at {blocked[:5]}")
    mouth_lo, mouth_hi = [m.STACK[0][0], m.STACK[1][0], m.STACK[2][0]], [m.STACK[0][1], m.FLOOR_TOP + 16.0, m.STACK[2][1]]
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
    check_floating(v)
    check_textures(v)
    check_containment(v)
    check_top(v)
    check_sides(v)
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


def validate_files(m, shape, frame_shape, ship):
    ok = True

    def fail(msg):
        nonlocal ok
        ok = False
        print("FAIL", msg)
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
    free = tuple(m.FREE_CELL[k] - m.ORIGIN_CELL[k] for k in range(3))
    if chim in cells or free in cells:
        fail("the chimney's cell or the one beside it is the furnace's")
    out = tuple(ship["outletCell"])
    if (out[0], out[1] + 1, out[2]) != chim or ship["outletFace"] != "up":
        fail("the outlet is not under the chimney's cell")
    line = ship["chimney"]
    if abs(line["origin"][1] - line["length"] / 2 - m.CELLS_Y) > 1e-9 or line["axis"] != "y" or line["length"] != m.MIN_CHIMNEY:
        fail("the chimney's line does not stand on the outlet")
    if abs(line["origin"][1] + line["length"] / 2 - (m.CELLS_Y + m.MIN_CHIMNEY)) > 1e-9:
        fail("the chimney's line is not the minimum counted from the outlet up")
    if not (out[0] <= line["origin"][0] <= out[0] + 1 and out[2] <= line["origin"][2] <= out[2] + 1):
        fail("the chimney's line is not over the outlet")
    for i in m.POTS:
        if ship[f"pot{i}Face"] != "up" or tuple(ship[f"pot{i}Cell"]) not in cells:
            fail(f"pot {i}'s cell")
    if ship["ashSide"] != "north" or abs(ship["ash"]["pos"][2]) > 1e-9:
        fail("the ash mouth is not on the front face")
    hinge = ship["hinge"]
    if hinge["axis"] != "x" or abs(hinge["origin"][1] * m.B - m.PIVOT[0]) > 1e-3 or abs(hinge["origin"][2] * m.B - m.PIVOT[1]) > 1e-3:
        fail("the hinge's line is not the pin's")
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, "
          f"{len(hollow)} hollow (the lid's), decks on {len(lids)}; glow on the burning coke and the melt only; no power "
          f"cell, no air port; the outlet under the chimney's cell {list(chim)}, the chimney six blocks from it up")
    return ok
