"""The crucible furnace generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import coplanar_faces, euler_round_trip, frame_floating, obb_obb
from machinegen.geometry import aabb_of, flatten
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

    def shown(self, state, depth):
        """Every element drawn with both holes in `state`, at `depth`."""
        return [e for pid in self.by_part if self.m.on_show(pid, state) for e in self.posed(pid, depth)]


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
    brick = sum(1 for e in v.by_part["frame"] if e.name.startswith("fr_brick"))
    print(f"parts: {len(v.parts)}, elements {len(v.els)}; frame {counts['frame']} ({brick} of brick); per hole: "
          + ", ".join(f"{p[2:]} {counts[p]}" for p in counts if p.startswith("h1")))


def check_floating(v):
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")


ROLES = [
    (r"^fr_brick_", {"brick"}),
    (r"^fr_(bearer|bar|lintel|plate|curb|flange|sleeve|corner|kerb)", {"iron"}),
    (r"^fr_stand\d", {"clay"}),
    (r"^h\dcover_(frame|hook)", {"iron"}),
    (r"^h\dcover_(fill|pocket)", {"brick"}),
    (r"^h\dpot_", {"clay"}),
    (r"^h\dcharge_", {"charge"}),
    (r"^h\dmelt_", {"melt"}),
    (r"^h\dcoke\d_", {"coke"}),
    (r"^h\dfire\d_", {"ember"}),
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


# ---------------------------------------------------------------- containment
def overhang(m, lo, hi):
    """How far (voxels) a box pokes out of where the model may be: the brickwork's box, and the column over each
    hole and the place behind it where its cover lies when off (the hollow cells); negative when inside."""
    W, H, D = m.CELLS_X * m.B, m.CELLS_Y * m.B, m.CELLS_Z * m.B
    worst = max(-lo[0], hi[0] - W, -lo[1], -lo[2], hi[2] - D)
    if hi[1] > H:
        cols = [r for r in m.regions()[1:]]
        best = min(max(r[0][0] - lo[0], hi[0] - r[1][0], hi[1] - r[1][1]) for r in cols)
        worst = max(worst, best)
    return worst


def check_containment(v):
    """Nothing leaves the brickwork and the hollow cells over the whole motion; with both covers seated nothing stands
    over the floor, and with one off only that cover does, lying on the floor (`coverOff`'s plate)."""
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
                if dp in (0.0, m.BOTH_ON, 1.0) and hi[1] > m.CELLS_Y * m.B + 1e-6:
                    over.setdefault(dp, set()).add(pid)
    off = {dp: {f"h{i}cover" for i in m.HOLES if m.cover_place(i, dp) == 0.0} for dp in (0.0, m.BOTH_ON, 1.0)}
    print(f"containment over the covers' motion (every 0.01): worst overhang {worst:.3f} voxels ({where[0]} at depth "
          f"{where[1]:.2f}); over the floor with both covers seated: {sorted(over.get(m.BOTH_ON, [])) or 'nothing'}, with "
          f"one off: {', '.join(sorted(p for s in (over.get(0.0, set()), over.get(1.0, set())) for p in s)) or 'nothing'}")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the furnace's cells at depth {where[1]}")
    for dp in (0.0, m.BOTH_ON, 1.0):
        if over.get(dp, set()) != off[dp]:
            v.fail(f"at depth {dp} over the floor: {sorted(over.get(dp, set()))}, not the off cover {sorted(off[dp])}")


# ---------------------------------------------------------------- the covers
def cover_box(m, i, seated):
    """Hole i's cover's box seated (flush in its hole's seat) or off (lying on the top behind its hole)."""
    s = m.SEAT_HALF
    z, y0 = (m.HOLE_Z, m.SEAT_Y) if seated else (m.OFF_Z, m.FLOOR_TOP)
    return ([m.HOLE_X[i] - s, y0, z - s], [m.HOLE_X[i] + s, y0 + m.COVER_T, z + s])


def same_box(a, b, tol=1e-6):
    return max(abs(a[0][k] - b[0][k]) for k in range(3)) < tol and max(abs(a[1][k] - b[1][k]) for k in range(3)) < tol


def check_covers(v):
    """Each cover lies flush in its seat or, off, on the top behind its hole at the ends of its window of depth, and
    stays put outside it; within it, it is lifted before it is carried, carried CARRY over the floor, and set down
    after. Only one cover is ever off its hole or on its way (gameplay's rule, which is what lets one depth pose
    both): at every depth at least one is seated, both at BOTH_ON, and the window's ends say so (`cover_place`,
    `depth_for`). Seated, a cover rests on the seat's brick ledge round its hole. Off, it rests flat on the top
    surface *(review)*: square to the axes, its underside on the floor, the floor under the whole of it there (brick
    or its iron floor plate, flush, sampled every half voxel: no hole, seat or outlet under it, no gap) and nothing of
    the frame above the floor into it. The covers are the only parts that move, and theta moves nothing."""
    m = v.m
    clear, off_both = 1e9, []
    frame = v.by_part["frame"]
    for i in m.HOLES:
        pid = f"h{i}cover"
        w0, w1, way = m.COVER_WINDOWS[i]
        carry = next((t0, t1) for t0, t1, axis, _ in m.cover_moves(i) if axis == "z")
        for dp in depths(0.01):
            box = aabb_of(v.posed(pid, dp))
            place = m.cover_place(i, dp)
            if place == 1.0 and not same_box(box, cover_box(m, i, True)):
                v.fail(f"{pid} is not seated flush in its hole at depth {dp:.2f}: {box}")
            if place == 0.0 and not same_box(box, cover_box(m, i, False)):
                v.fail(f"{pid} is not lying on the top behind its hole at depth {dp:.2f}: {box}")
            if carry[0] <= dp <= carry[1]:
                clear = min(clear, box[0][1] - m.FLOOR_TOP)
            if abs(m.depth_for(i, place) - min(w1, max(w0, dp))) > 1e-9:
                v.fail(f"{pid}: depth_for does not undo cover_place at {dp:.2f}")
        if m.cover_place(i, m.BOTH_ON) != 1.0:
            v.fail(f"{pid} is not seated at depth {m.BOTH_ON}, where both are on")
        # off: flat on the floor, the floor whole under it
        off_dp = m.depth_for(i, 0.0)
        els = v.posed(pid, off_dp)
        if any(abs(e.r[a][b] - (1.0 if a == b else 0.0)) > 1e-9 for e in els for a in range(3) for b in range(3)):
            v.fail(f"{pid} does not lie square when off")
        lo, hi = aabb_of(els)
        if abs(lo[1] - m.FLOOR_TOP) > 1e-6:
            v.fail(f"{pid}'s underside is {lo[1] - m.FLOOR_TOP:+.4f} off the floor when off")
        gaps, n = [], 0
        x = lo[0] + 0.25
        while x < hi[0]:
            z = lo[2] + 0.25
            while z < hi[2]:
                n += 1
                under = [e for e in frame if inside(e, (x, m.FLOOR_TOP - 0.05, z))]
                if not under or any(abs(e.aabb()[1][1] - m.FLOOR_TOP) > 1e-6 for e in under):
                    gaps.append((round(x, 2), round(z, 2)))
                z += 0.5
            x += 0.5
        if gaps:
            v.fail(f"{pid} is not borne by the floor when off at {len(gaps)} of {n} points, e.g. {gaps[:4]}")
        into = [e.name for e in frame if all(e.aabb()[0][q] < hi[q] and lo[q] < e.aabb()[1][q] for q in (0, 2))
                and e.aabb()[1][1] > m.FLOOR_TOP + 1e-6]
        if into:
            v.fail(f"frame elements rise above the floor under {pid} when off: {into[:4]}")
        # seated: on the seat's ledge round the hole
        cx, cz, h = m.HOLE_X[i], m.HOLE_Z, m.HOLE_HALF + 0.5
        for p in ((cx - h, m.SEAT_Y - 0.1, cz), (cx + h, m.SEAT_Y - 0.1, cz), (cx, m.SEAT_Y - 0.1, cz - h), (cx, m.SEAT_Y - 0.1, cz + h)):
            if not any(inside(e, p) for e in frame if e.name.startswith("fr_brick")):
                v.fail(f"no ledge under {pid}'s rim at {p}")
        plate = next((e for e in frame if e.name == f"fr_plate{i}"), None)
        if plate is None or abs(plate.aabb()[1][1] - m.FLOOR_TOP) > 1e-9:
            v.fail(f"hole {i}'s floor plate is missing or not flush with the top")
    for dp in depths(0.005):
        seated = [same_box(aabb_of(v.posed(f"h{i}cover", dp)), cover_box(m, i, True)) for i in m.HOLES]
        if not any(seated):
            off_both.append(dp)
    if off_both:
        v.fail(f"both covers are off their holes at depths {off_both[:5]}")
    if clear < m.CARRY - 1e-6:
        v.fail(f"a cover is carried {clear:+.3f} over the floor, not {m.CARRY}")
    for pid in (p["id"] for p in v.parts):
        for dp in (0.3, 0.8):
            a = v.m.pm(v.parts, pid, (0.0, dp, None))
            b = v.m.pm(v.parts, pid, (3.7, dp, None))
            if max(abs(a[r][c] - b[r][c]) for r in range(3) for c in range(4)) > 1e-12:
                v.fail(f"theta moves {pid}")
        if not pid.endswith("cover"):
            c = v.m.pm(v.parts, pid, (0.0, 1.0, None))
            if max(abs(c[r][q] - (1.0 if r == q else 0.0)) for r in range(3) for q in range(4)) > 1e-12:
                v.fail(f"depth moves {pid}")
    win = ", ".join(f"h{i}cover {'on' if way == 'on' else 'off'} over {w0}..{w1}" for i, (w0, w1, way) in m.COVER_WINDOWS.items())
    print(f"covers: one at a time ({win}; both seated at {m.BOTH_ON}, never both off, every 0.005); seated flush on the "
          f"seat's ledge, or off lying flat on the top behind the hole (square, underside on the floor, the floor whole "
          f"under it and nothing into it); carried {m.OFF_Z - m.HOLE_Z:.0f} at {clear:.3f} over the floor; the floor "
          f"plates flush; theta moves nothing, depth only the covers")


# ---------------------------------------------------------------- clearances
def allowed(pa, pb, ea, eb):
    """Intended overlaps: a lump lying on a coke layer's top is inside the layer over it (the same kind)."""
    ra, rb = re.fullmatch(r"h(\d)(coke|fire)(\d)", pa), re.fullmatch(r"h(\d)(coke|fire)(\d)", pb)
    if not (ra and rb and ra.group(1) == rb.group(1) and ra.group(2) == rb.group(2)):
        return False
    ja, jb = int(ra.group(3)), int(rb.group(3))
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


def check_clearances(v, step):
    """In every state of the holes, nothing of one part runs into another's (the brick, the iron and the stands are
    one part), at rest; and the covers, carried between the floor and the seat (every `step` of depth), touch nothing."""
    m = v.m
    allhits = {}
    for state in m.STATE_IDS:
        items = [(e.part, e, e.aabb()) for e in v.shown(state, 0.0)]
        for key, hs in touches(items).items():
            allhits.setdefault(key, {}).update({h: (state, 0.0) for h in hs})
        for dp in depths(step):
            items = [(e.part, e, e.aabb()) for e in v.shown(state, dp)]
            for key, hs in touches(items, movers={f"h{i}cover" for i in m.HOLES}).items():
                allhits.setdefault(key, {}).update({h: (state, dp) for h in hs})
    print(f"clearances in {len(m.STATE_IDS)} states at rest and with the covers carried every {step} of depth: "
          f"{'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} ({s}, depth {d:.2f})" for (a, b), (s, d) in ex))
    if allhits:
        v.fail("parts run into each other")


# ---------------------------------------------------------------- the pot, the coke, the melt
def check_pot(v):
    """The stand on the grate, the pot on the stand, its mouth under the seated cover, coke room round it; the charge
    and the melt inside it, below its rim."""
    m = v.m
    for i in m.HOLES:
        stand = aabb_of([e for e in v.by_part["frame"] if e.name.startswith(f"fr_stand{i}_")])
        pot = aabb_of(v.by_part[f"h{i}pot"])
        if abs(stand[0][1] - m.GRATE_TOP) > 1e-9 or abs(pot[0][1] - stand[1][1]) > 1e-9:
            v.fail(f"hole {i}'s pot is not on its stand on the grate ({stand[0][1]}, {stand[1][1]}, {pot[0][1]})")
        if pot[1][1] > m.SEAT_Y - 2.0:
            v.fail(f"hole {i}'s pot reaches the cover")
        room = min(pot[0][0] - (m.HOLE_X[i] - m.HOLE_HALF), (m.HOLE_X[i] + m.HOLE_HALF) - pot[1][0],
                   pot[0][2] - (m.HOLE_Z - m.HOLE_HALF), (m.HOLE_Z + m.HOLE_HALF) - pot[1][2])
        if room < 1.5:
            v.fail(f"hole {i}'s pot leaves {room:.2f} of coke room")
        for what in ("charge", "melt"):
            lo, hi = aabb_of([e for e in v.by_part[f"h{i}{what}"] if "_piece" not in e.name])
            if lo[1] < m.POT_BASE[1] or hi[1] > m.POT_TOP - 1.0:
                v.fail(f"hole {i}'s {what} is not inside its pot")
        top = aabb_of(v.by_part[f"h{i}charge"])[1][1]
        if top > m.SEAT_Y - 1.0:
            v.fail(f"hole {i}'s charge reaches the cover ({top:.2f})")
        foot = aabb_of([e for e in v.by_part[f"h{i}pot"] if "_foot" in e.name])
        if foot[0][0] > stand[0][0] or foot[1][0] < stand[1][0] or foot[0][2] > stand[0][2] or foot[1][2] < stand[1][2]:
            v.fail(f"hole {i}'s pot's foot does not cover its stand")
        # the melt holds a full pot: the pot's inside, measured from its walls, from its floor to the melt's top
        floor = max(e.aabb()[1][1] for e in v.by_part[f"h{i}pot"] if "_foot" in e.name)
        level = max(e.aabb()[1][1] for e in v.by_part[f"h{i}melt"])
        held = inside_volume(v, i, floor, level) / m.VOXELS_PER_UNIT
        if abs(held - m.POT_UNITS) > 0.02 * m.POT_UNITS:
            v.fail(f"hole {i}'s melt holds {held:.0f} units, not a full pot's {m.POT_UNITS}")
        if m.POT_TOP - level < 1.0:
            v.fail(f"hole {i}'s melt stands {m.POT_TOP - level:.2f} under the rim")
    print(f"pot: on its stand on the grate, the mouth {m.SEAT_Y - m.POT_TOP:.1f} under the seated cover, "
          f"{m.HOLE_HALF - max(a for *_, a in m.POT_TUBES):.1f} of coke room round its belly; the melt at {level:.2f}, "
          f"{m.POT_TOP - level:.2f} under the rim, holding {held:.1f} units (a full pot: {m.POT_UNITS} at "
          f"{m.VOXELS_PER_UNIT} cubic voxels a unit, measured from the walls); the charge to {top:.2f}")


def inside_volume(v, i, y0, y1, dy=0.01):
    """The volume inside hole i's pot from y0 to y1, from its walls' elements: at each height, the octagon whose
    apothem is the nearest of the walls' inner faces to the pot's axis."""
    m = v.m
    cx, cz = m.HOLE_X[i], m.HOLE_Z
    walls = []
    for e in v.by_part[f"h{i}pot"]:
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


def octagon_has(cx, cz, a, x, z):
    dx, dz = abs(x - cx), abs(z - cz)
    return dx <= a and dz <= a and (dx + dz) <= a * math.sqrt(2.0)


def check_coke(v):
    """The coke's layers stack from the grate to the pot's shoulders, two coke a layer; at the top of each the coke
    meets the pot with no gap (its opening the pot's widest section in the layer); each fills the hole across from
    the walls to the pot (sampled); its top, lumps and all, stays under the flue openings; the burning layers are
    the same boxes as the cold ones."""
    m = v.m
    layers = m.COKE_LAYERS
    if abs(layers[0][0] - m.GRATE_TOP) > 1e-9 or any(abs(layers[k][1] - layers[k + 1][0]) > 1e-9 for k in range(len(layers) - 1)):
        v.fail("the coke's layers do not stack from the grate")
    belly = next(t for t in m.POT_TUBES if t[0] == "belly")
    if abs(layers[-1][1] - belly[2]) > 1e-9:
        v.fail("the coke does not come up to the pot's shoulders (the belly's top)")
    if 6 != len(layers) * m.COKE_PER_LAYER:
        v.fail("the layers do not hold the hole's six coke (CokeCapacity)")
    worst_gap, holes_in = 0.0, 0
    for i in m.HOLES:
        cx, cz, h = m.HOLE_X[i], m.HOLE_Z, m.HOLE_HALF
        for j, (y0, y1) in enumerate(layers, 1):
            a = m.coke_opening(j)
            gap = a - m.pot_apothem(y1 - 1e-6)
            worst_gap = max(worst_gap, abs(gap))
            if abs(gap) > 1e-9:
                v.fail(f"hole {i}'s coke layer {j} leaves a gap of {gap:.3f} round the pot at its top")
            els = [e for e in v.by_part[f"h{i}coke{j}"] if "_lump" not in e.name]
            y = (y0 + y1) / 2
            n = 24
            for p in range(n + 1):
                for q in range(n + 1):
                    x, z = cx - h + 0.05 + (2 * h - 0.1) * p / n, cz - h + 0.05 + (2 * h - 0.1) * q / n
                    if octagon_has(cx, cz, a + 0.01, x, z):
                        continue
                    if not any(inside(e, (x, y, z), eps=-0.001) for e in els):
                        holes_in += 1
            fire = {e.name.split("_", 1)[1]: e.aabb() for e in v.by_part[f"h{i}fire{j}"]}
            for e in v.by_part[f"h{i}coke{j}"]:
                f = fire.get(e.name.split("_", 1)[1])
                if f is None or not same_box(e.aabb(), f):
                    v.fail(f"{e.name} has no burning twin in h{i}fire{j}")
        top = max(e.aabb()[1][1] for j in (1, 2, 3) for e in v.by_part[f"h{i}coke{j}"])
        if top >= m.FLUE_OPENING[1][0]:
            v.fail(f"hole {i}'s coke reaches its flue opening ({top:.2f})")
    if holes_in:
        v.fail(f"{holes_in} sampled points of the coke's layers are neither coke nor the pot's place")
    print(f"coke: {len(layers)} layers of {m.COKE_PER_LAYER} from the grate ({m.GRATE_TOP}) to the shoulders "
          f"({layers[-1][1]}), meeting the pot at each top (worst {worst_gap:.1e}); every layer whole across; its top "
          f"{top:.2f} under the flue openings ({m.FLUE_OPENING[1][0]}); the burning layers the same boxes")


# ---------------------------------------------------------------- the draught: grate, cave, flue, stack, air
def empty(v, p, frame_only=True, depth=None):
    pool = v.by_part["frame"] if frame_only else [e for pid in v.by_part for e in v.posed(pid, depth)]
    return not any(inside(e, p) for e in pool)


def check_draught(v):
    """The way the air goes: in at each ash mouth (or the air port at the back, through the duct and the sleeve) into
    the cave, up between the firebars, through the fire, out at each hole's flue opening near the top, along the back
    flue to the stack and up through the outlet, open to the sky but for the chimney. Sampled points along it lie in
    no element of the frame (and the outlet's mouth in no element at all, at any depth). The firebars lie on both
    bearers and the bearers' ends are built into the walls."""
    m = v.m
    path = []
    for i in m.HOLES:
        cx, cz = m.HOLE_X[i], m.HOLE_Z
        path += [(cx, 4.0, -0.5), (cx, 4.0, 1.0), (cx, 4.0, 3.0), (cx, 5.0, cz)]
        bars = sorted((e.aabb() for e in v.by_part["frame"] if e.name.startswith(f"fr_bar{i}")), key=lambda b: b[0][2])
        for (alo, ahi), (blo, _) in zip(bars, bars[1:]):
            gz = (ahi[2] + blo[2]) / 2
            path += [(cx - 3.0, (m.BAR_Y[0] + m.BAR_Y[1]) / 2, gz), (cx + 3.0, (m.BAR_Y[0] + m.BAR_Y[1]) / 2, gz)]
        fx = cx
        fy = (m.FLUE_OPENING[1][0] + m.FLUE_OPENING[1][1]) / 2
        path += [(fx, fy, cz + 3.0), (fx, fy, 15.0), (fx, fy, 18.0), (fx, fy, 24.0)]
        sx = (m.STACK[0][0] + m.STACK[0][1]) / 2
        path += [(fx + (sx - fx) * t / 10, 25.5, 24.0) for t in range(11)]
    sx, sz = (m.STACK[0][0] + m.STACK[0][1]) / 2, (m.STACK[2][0] + m.STACK[2][1]) / 2
    path += [(sx, y / 2, sz) for y in range(51, 64)]
    dx, dy = (m.DUCT[0][0] + m.DUCT[0][1]) / 2, (m.DUCT[1][0] + m.DUCT[1][1]) / 2
    path += [(dx, dy, 2 * m.B + 0.5), (dx, dy, 2 * m.B - 0.5), (dx, dy, 24.0), (dx, dy, 15.0), (dx, dy, 8.0)]
    path += [(x, dy, 8.0) for x in range(4, 45, 2)]
    blocked = [p for p in path if not empty(v, p)]
    if blocked:
        v.fail(f"the draught's way is blocked at {blocked[:5]}")
    mouth_lo, mouth_hi = [m.STACK[0][0], m.STACK[1][0], m.STACK[2][0]], [m.STACK[0][1], m.FLOOR_TOP + 16.0, m.STACK[2][1]]
    for dp in depths(0.05):
        for pid in v.by_part:
            for e in v.posed(pid, dp):
                lo, hi = e.aabb()
                if all(lo[q] < mouth_hi[q] - 0.02 and mouth_lo[q] < hi[q] - 0.02 for q in range(3)):
                    v.fail(f"{e.name} is in the stack or over the outlet at depth {dp}")
    for i in m.HOLES:
        bars = [e.aabb() for e in v.by_part["frame"] if e.name.startswith(f"fr_bar{i}")]
        bearers = [e.aabb() for e in v.by_part["frame"] if e.name.startswith(f"fr_bearer{i}")]
        for lo, hi in bars:
            for blo, bhi in bearers:
                if abs(lo[1] - bhi[1]) > 1e-9 or not (lo[0] <= blo[0] + 1e-9 and hi[0] >= bhi[0] - 1e-9):
                    v.fail(f"a firebar of hole {i} does not lie across both bearers")
        cz = m.HOLE_Z
        for blo, bhi in bearers:
            xc = (blo[0] + bhi[0]) / 2
            for z in (cz - m.HOLE_HALF - 0.5, cz + m.HOLE_HALF + 0.5):
                for y in (bhi[1] + 0.2, blo[1] - 0.2):
                    if not any(inside(e, (xc, y, z)) for e in v.by_part["frame"] if e.name.startswith("fr_brick")):
                        v.fail(f"a bearer of hole {i} is not built into the wall at z {z}")
        gap = (2 * m.HOLE_HALF - m.BARS * m.BAR_W) / (m.BARS - 1)
    if gap < 0.8:
        v.fail(f"the firebars' gaps ({gap:.2f}) are too narrow for the draught")
    print(f"draught: in at the ash mouths and the air port, up through {m.BARS - 1} gaps of {gap:.2f} between the "
          f"firebars, out at each hole's flue opening ({m.FLUE_OPENING[1][0]}..{m.FLUE_OPENING[1][1]}, under the seat at "
          f"{m.SEAT_Y}), along the back flue to the stack and the outlet: {len(path)} points clear; the outlet's mouth "
          f"clear at every depth; the firebars on both bearers, the bearers built into the walls")


# ---------------------------------------------------------------- the top
def check_top(v):
    """The top is flat *(review)*: no recess but the holes' seats and the outlet's mouth. Every half voxel of it, just
    under the floor, is brick or flush iron whose top is the floor; nothing of the frame stands above it."""
    m = v.m
    frame = v.by_part["frame"]
    s = m.SEAT_HALF
    (sx0, sx1), _, (sz0, sz1) = m.STACK

    def opening(x, z):
        if sx0 <= x <= sx1 and sz0 <= z <= sz1:
            return True
        return any(abs(x - m.HOLE_X[i]) <= s and abs(z - m.HOLE_Z) <= s for i in m.HOLES)
    bad, n = [], 0
    x = 0.25
    while x < m.CELLS_X * m.B:
        z = 0.25
        while z < m.CELLS_Z * m.B:
            if not opening(x, z):
                n += 1
                under = [e for e in frame if inside(e, (x, m.FLOOR_TOP - 0.05, z))]
                if not under or any(abs(e.aabb()[1][1] - m.FLOOR_TOP) > 1e-6 for e in under):
                    bad.append((x, z))
            z += 0.5
        x += 0.5
    high = [e.name for e in frame if e.aabb()[1][1] > m.FLOOR_TOP + 1e-6]
    print(f"top: flat at the floor at {n - len(bad)} of {n} points (every half voxel, the holes' seats and the outlet "
          f"aside); {len(high)} frame elements above it")
    if bad:
        v.fail(f"the top is not flat at {len(bad)} points, e.g. {bad[:4]}")
    if high:
        v.fail(f"frame elements stand above the top: {high[:4]}")


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
    check_covers(v)
    check_pot(v)
    check_coke(v)
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
        want = m.FIRE_GLOW if re.fullmatch(r"h\dfire\d", stem) else m.MELT_GLOW if re.fullmatch(r"h\dmelt", stem) else None
        got = {f.get("glow") for f in e["faces"].values()}
        if got != {want}:
            fail(f"{e['name']}'s faces glow {got}, not {want}")
    if "powerCell" in ship or "powerFace" in ship:
        fail("a coke-fired furnace has no power cell")
    cells = {tuple(c["pos"]): c for c in ship["cells"]}
    lift = {tuple(c[k] - m.ORIGIN_CELL[k] for k in range(3)) for c in m.LIFT_CELLS}
    hollow = {p for p, c in cells.items() if c.get("hollow")}
    lids = {p for p, c in cells.items() if "lid" in c}
    top = {p for p in cells if p not in lift and p[1] == m.CELLS_Y - 1}
    if hollow != lift or any(cells[p].get("boxes") for p in hollow):
        fail(f"the hollow cells are {sorted(hollow)}, not the covers' lift {sorted(lift)}")
    if lids != top or any(cells[p]["lid"] != 1.0 for p in lids):
        fail(f"lids on {sorted(lids)}, not at the floor on the brickwork's top cells {sorted(top)}")
    if any(not cells[p].get("boxes") for p in cells if p not in lift):
        fail("a cell of the brickwork has no boxes")
    chim = tuple(m.CHIMNEY_CELL[k] - m.ORIGIN_CELL[k] for k in range(3))
    if chim in cells:
        fail("the chimney's cell is the furnace's")
    out = tuple(ship["outletCell"])
    if (out[0], out[1] + 1, out[2]) != chim or ship["outletFace"] != "up":
        fail("the outlet is not under the chimney's cell")
    line = ship["chimney"]
    if abs(line["origin"][1] - line["length"] / 2 - m.CELLS_Y) > 1e-9 or line["axis"] != "y" or line["length"] < m.MIN_CHIMNEY:
        fail("the chimney's line does not stand on the outlet")
    if not (out[0] <= line["origin"][0] <= out[0] + 1 and out[2] <= line["origin"][2] <= out[2] + 1):
        fail("the chimney's line is not over the outlet")
    # counted from the outlet up: MIN_CHIMNEY blocks on it, so its top is the furnace's height and that over the ground
    if abs(line["origin"][1] + line["length"] / 2 - (m.CELLS_Y + m.MIN_CHIMNEY)) > 1e-9:
        fail("the chimney's line is not the minimum counted from the outlet up")
    for i in m.HOLES:
        if ship[f"hole{i}Face"] != "up" or tuple(ship[f"hole{i}Cell"]) not in cells:
            fail(f"hole {i}'s cell")
        # where an off cover lies: a hollow cell, its box the cover lying on the floor (gameplay's collision)
        off = ship["furnace"]["coverOff"][f"h{i}cover"]
        if tuple(off["cell"]) not in hollow:
            fail(f"h{i}cover's place when off is not in a hollow cell")
        posed_off = [posed(w, m.pm(ship["parts"], f"h{i}cover", (0.0, m.depth_for(i, 0.0), None)))
                     for w in flatten(shape["elements"], textures={}) if w.name.startswith(f"h{i}cover_")]
        lo, hi = aabb_of(posed_off)
        want = [off["cell"][k % 3] * m.B + off["box"][k] * m.B for k in range(6)]
        if max(abs(a - b) for a, b in zip(lo + hi, want)) > 1e-3:
            fail(f"furnace.coverOff for h{i}cover is not the off cover's box: {lo + hi} against {want}")
        if abs(off["box"][1]) > 1e-9 or abs(off["box"][4] - m.COVER_T / m.B) > 1e-6:
            fail(f"h{i}cover's off box is not a plate {m.COVER_T} high on the floor")
    if ship["airFace"] != "south" or tuple(ship["airCell"]) not in cells:
        fail("the air port's cell")
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, "
          f"{len(hollow)} hollow (where the covers are carried and lie off), lids on {len(lids)}; glow on the burning coke and the melt only; "
          f"no power cell; the outlet under the chimney's cell {list(chim)}")
    return ok
