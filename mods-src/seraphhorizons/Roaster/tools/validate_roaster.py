"""The roaster generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved.

The roaster's levels share one place: the stalls of level 1 stand where the furnace of level 2 does. So
every check that looks at parts together looks at one state at a time (the frame alone, level 1, level 2:
make_shape.STATES), never at both levels at once.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, cells_touched, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb, supports
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import part_of, posed


class V:
    def __init__(self, m, els, parts, rig):
        self.m, self.els, self.parts, self.rig = m, els, parts, rig
        self.req = {p["id"]: p["requires"] for p in parts}
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        self.cache = {}
        self.ok = True

    def fail(self, msg):
        self.ok = False
        print("FAIL", msg)

    def mat(self, pid, theta):
        return self.m.pm(self.parts, pid, (theta, "all"))

    def posed(self, pid, theta):
        key = (pid, theta)
        if key not in self.cache:
            if len(self.cache) > 20000:
                self.cache.clear()
            mm = self.mat(pid, theta)
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def point(self, pid, theta, p):
        return [v * 16 for v in _apply(self.mat(pid, theta), [c / 16 for c in p])]

    def shows(self, pid, state):
        return self.m.state_shows(self.req[pid], state)

    def named(self, pid, rx, theta=None):
        src = self.posed(pid, theta) if theta is not None else self.by_part.get(pid, [])
        r = re.compile(rx)
        return [e for e in src if r.search(e.name)]


def thetas(n=48, turns=2):
    """Angles over the rig's whole cycle (the rabbles turn half as fast as the axle: two turns)."""
    return [turns * math.tau * i / n for i in range(n)]


def inside(p, el, eps=0.0):
    """Whether point p (voxels) is inside element el (a rotated box), by more than eps."""
    d = [p[k] - el.c[k] for k in range(3)]
    for k in range(3):
        axis = [el.r[i][k] for i in range(3)]
        if abs(sum(d[i] * axis[i] for i in range(3))) > abs(el.size[k]) / 2 - eps:
            return False
    return True


# ---------------------------------------------------------------- parts, textures, the box
def check_basic(v):
    worst = euler_round_trip(v.els)
    print(f"euler round trip: worst {worst:.1e}")
    if worst > 1e-9:
        v.fail("an element's rotation does not survive being written as Euler angles")
    seen, dup = set(), set()
    for el in v.els:
        (dup if el.name in seen else seen).add(el.name)
    if dup:
        v.fail(f"duplicate element names: {sorted(dup)[:5]}")
    wrong = [(el.name, el.part, part_of(v.parts, el.name)) for el in v.els if part_of(v.parts, el.name) != el.part]
    if wrong:
        v.fail(f"{len(wrong)} elements land in the wrong part, e.g. {wrong[:4]}")
    empty = [p["id"] for p in v.parts if not v.by_part.get(p["id"])]
    if empty:
        v.fail(f"parts with no elements: {empty}")
    counts = {p["id"]: len(v.by_part.get(p["id"], [])) for p in v.parts}
    print(f"parts: {len(v.parts)}, elements {len(v.els)}: " + ", ".join(f"{k} {n}" for k, n in counts.items()))
    for s in v.m.STATES:
        n = sum(len(v.by_part[p["id"]]) for p in v.parts if v.shows(p["id"], s))
        print(f"elements drawn in {s}: {n}")


ROLES = [
    # (element regex, the textures it may wear)
    (r"^fr_(plinth|stackcap)", {"stone"}),
    (r"^fr_(chamber|stack_)", {"brick"}),
    (r"^fr_(spout|sulfurdoor|stackband)", {"iron"}),
    (r"^stalls_", {"rubble"}),
    (r"^stallflue_", {"brick"}),
    (r"^trestle_", {"oak"}),
    (r"^heaps_s\dlog", {"firewood"}),
    (r"^heaps_s\dcourse", {"ore"}),
    (r"^hopper_", {"iron"}),
    (r"^hearth_", {"firebrick"}),
    (r"^walls_(side|front)", {"brick"}),
    (r"^walls_skew", {"firebrick"}),
    (r"^arch_v", {"firebrick"}),
    (r"^arch_seat", {"iron"}),
    (r"^ironwork_", {"iron"}),
    (r"^firebox_", {"iron"}),
    (r"^charge_", {"ore"}),
    (r"^fire_bed", {"fire"}),
    (r"^fire_coal", {"coal"}),
    (r"^entry_", {"oak"}),
    (r"^gearcase_", {"iron"}),
    (r"^lineshaft_(rod|pinion)", {"iron"}),
    (r"^(pedestal|rabble|feedroll|feeder)", {"iron"}),
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


def check_containment(v):
    """Every element, at every sampled angle, inside the footprint's cells (not just the machine box: the
    footprint is the body, the drive's row over the roof and the stack)."""
    m = v.m
    cells = set(m.footprint())
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where, outside = -1e9, None, set()
    for th in thetas(24):
        for pid in v.by_part:
            for el in v.posed(pid, th):
                lo, hi = el.aabb()
                o = box_overhang(lo, hi, box)
                if o > worst:
                    worst, where = o, el.name
                for c in cells_touched(lo, hi):
                    if c not in cells:
                        outside.add((el.name, c))
    print(f"containment: {len(cells)} cells; worst overhang of the machine box {worst:.3f} voxels ({where}); "
          f"{len(outside)} elements outside the cells")
    if worst > 0.01:
        v.fail(f"{where} leaves the machine box")
    if outside:
        v.fail(f"elements outside the footprint's cells: {sorted(outside)[:6]}")


# ---------------------------------------------------------------- the levels
def check_levels(v):
    """The levels' sets: level 2 keeps level 1's hopper and pulls down only RETIRED (the stalls, their flue
    and the trestle); every requires value is in a level or in a level's contents, never both; every state's
    parts are the frame's and its own."""
    m = v.m
    l1, l2 = set(m.LEVELS["1"]), set(m.LEVELS["2"])
    contents = set(m.CONTENTS["1"]) | set(m.CONTENTS["2"])
    values = {p["requires"] for p in v.parts if p["requires"] is not None}
    if l1 & l2 != {"hopper"}:
        v.fail(f"level 2 should keep exactly the hopper of level 1's parts: it keeps {sorted(l1 & l2)}")
    if sorted(l1 - l2) != sorted(m.RETIRED):
        v.fail(f"level 2 retires {sorted(l1 - l2)}, not RETIRED {sorted(m.RETIRED)}")
    if contents & (l1 | l2):
        v.fail(f"contents that are also fitted parts: {sorted(contents & (l1 | l2))}")
    if values != l1 | l2 | contents:
        v.fail(f"requires values in no level, or levels naming no part: {sorted(values ^ (l1 | l2 | contents))}")
    if set(m.STATES) != {"frame", "level1", "level2"} or m.STATES["frame"]:
        v.fail("the states are the frame alone and levels 1 and 2")
    for s in m.STATES:
        shown = sorted(p["id"] for p in v.parts if v.shows(p["id"], s))
        print(f"state {s}: {len(shown)} parts ({', '.join(shown)})")
    print(f"levels: 1 {sorted(l1)}; 2 {sorted(l2)}; level 2 keeps {sorted(l1 & l2)} and retires {sorted(l1 - l2)}; contents {m.CONTENTS}")


def static_parts(v, state):
    return [p["id"] for p in v.parts if p["id"] not in v.m.MOVING and v.shows(p["id"], state)]


def check_floating(v):
    """Nothing floats, in each state: the frame and the state's static parts are one piece from the ground.
    (The moving parts are carried by their bearings: check_supports.)"""
    for s in v.m.STATES:
        els = [el for pid in static_parts(v, s) for el in v.by_part[pid]]
        seen, loose = frame_floating(els)
        print(f"nothing floats ({s}): {len(seen)} elements joined to the ground, {len(loose)} not")
        if loose:
            v.fail(f"elements float in {s}: {loose[:8]}")


# ---------------------------------------------------------------- the moving parts
# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else a moving part touches, in a state where both are drawn, is a failure.
ALLOWED = [
    ("entry", None, "gearcase", None),                              # the entry into the gear case
    ("lineshaft", r"_rod", "gearcase", None),                       # the line shaft out of it
    ("entry", None, "lineshaft", r"_rod"),                          # (the rectifier between them is in the case)
    ("lineshaft", r"_rod", "pedestals", r"_bearing"),               # the shaft in its bearings
    ("lineshaft", r"_rod", "feeder", r"_casing_[ns]"),              # and in the casing's end walls
    ("lineshaft", r"_rod", "feedroll", None),                       # the roll keyed on it
    ("lineshaft", r"_pinion\d_tooth", r"rabble\d", r"_wheel_tooth"),  # meshing teeth (their depth: check_gearing)
    ("rabble\\d", r"_spindle", "rabblemounts", r"_(gland|bearing|base)"),   # each spindle in its gland and its bearing
    ("rabble\\d", r"_spindle", "arch", r"arch_v4_"),                # (it passes the crown strip's cut ends within its hole)
    ("rabble\\d", r"_wheelhub", "rabblemounts", r"_bearing"),       # the wheel's hub on its bearing
    ("rabble\\d", r"_(arm\dplough|hub|spindle)", "charge", None),   # the ploughs, the hub and the spindle's foot in the charge
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def overlap(a, b, eps=0.02):
    return all(a[0][q] < b[1][q] - eps and b[0][q] < a[1][q] - eps for q in range(3))


def candidates(v, state, ths):
    """For each moving part drawn in `state`: the static elements drawn with it that its swept box (over
    `ths`) reaches, as (part, element, box); the statics stand still, so they are posed once."""
    statics = [(pid, el, el.aabb()) for pid in v.by_part if pid not in v.m.MOVING and v.shows(pid, state) for el in v.by_part[pid]]
    out = {}
    for pid in v.m.MOVING:
        if not v.shows(pid, state):
            continue
        boxes = [e.aabb() for th in ths for e in v.posed(pid, th)]
        swept = ([min(b[0][q] for b in boxes) for q in range(3)], [max(b[1][q] for b in boxes) for q in range(3)])
        out[pid] = [s for s in statics if overlap(s[2], swept)]
    return out


def touching_pairs(v, th, state, cands):
    moving = [(pid, el, el.aabb()) for pid in cands for el in v.posed(pid, th)]
    hits = {}
    for pa, ea, (alo, ahi) in moving:
        others = cands[pa] + [x for x in moving if x[0] != pa and v.m.MOVING.index(x[0]) > v.m.MOVING.index(pa)]
        for pb, eb, (blo, bhi) in others:
            if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                continue
            if allowed(pa, ea.name, pb, eb.name):
                continue
            if obb_obb(ea, eb):
                hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def check_clearances(v, n, label):
    """Every moving part against everything drawn with it (level 2; nothing moves at level 1), over the rig's
    cycle: the rabbles clear the hearth, the bridge, the walls and each other, the feed roll its casing, the
    gears everything but their mates."""
    allhits = {}
    ths = thetas(n)
    s = "level2"
    cands = candidates(v, s, ths)
    for th in ths:
        for key, hs in touching_pairs(v, th, s, cands).items():
            allhits.setdefault(key, {})
            for h in hs:
                allhits[key].setdefault(h, th)
    print(f"{label} over {len(ths)} angles at level 2: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {t:.3f}" for (a, b), t in ex))
    if allhits:
        v.fail("moving parts run into each other or the furnace")


def check_supports(v):
    """The line shaft in at least two bearings (the pedestals; the feeder casing's end walls besides), each
    spindle in its gland and its bearing, each wheel's hub resting on its bearing."""
    m = v.m
    statics = [el for pid in ("pedestals", "rabblemounts", "gearcase") for el in v.by_part[pid]]
    # supports() takes the other two coordinates in axis order: for a shaft along z, (x, y)
    found, span = supports(statics, v.named("lineshaft", r"_rod"), 2, (m.SHAFT[0], m.SHAFT[1]))
    bearings = sorted({re.sub(r"_[0-9]+$", "", n) for n in found if n.endswith("_bearing") or n.startswith("gearcase_boss")})
    print(f"support line shaft: {len(bearings)} bearings ({', '.join(bearings)}), shaft z {span}")
    if len(bearings) < 3 or "gearcase_bosss" not in bearings:
        v.fail("the line shaft is not carried by the gear case's boss and two bearings")
    found, _ = supports(statics, v.named("entry", r"."), 2, (m.SHAFT[0], m.SHAFT[1]))
    if not any(n.startswith("gearcase_bossn") for n in found):
        v.fail("the entry does not run into the gear case's boss")
    walls = sorted((e.aabb() for e in v.named("feeder", r"_casing_[ns]")), key=lambda b: b[0][2])
    if len(walls) != 2 or not all(b[0][2] < span[1] and span[0] < b[1][2] for b in walls) or span[1] > walls[1][1][2] + 1e-6:
        v.fail("the line shaft's end does not run in the feeder casing's end walls")
    for k, zs in enumerate(m.RABBLE_Z, 1):
        found, span = supports(statics + v.by_part["arch"], v.named(f"rabble{k}", r"_spindle"), 1, (m.SHAFT[0], zs))
        names = sorted(n for n in found if n.startswith("rabblemount"))
        print(f"support rabble {k}: spindle y {span}, carried by {', '.join(names)}")
        if not any(n.endswith("_gland") for n in names) or not any(n.endswith("_bearing") for n in names):
            v.fail(f"rabble {k}'s spindle is not in its gland and its bearing")
        hub = min(e.aabb()[0][1] for e in v.named(f"rabble{k}", r"_wheelhub"))
        seat = max(e.aabb()[1][1] for e in v.by_part["rabblemounts"] if e.name == f"rabblemount{k}_bearing")
        if abs(hub - seat) > 1e-6:
            v.fail(f"rabble {k}'s wheel hub does not rest on its bearing ({hub:.3f} against {seat:.3f})")


def overlap_depth(a, b):
    """How far two boxes run into each other: the shrink at which obb_obb stops finding an overlap, twice (as the
    gear cutter measures its meshing teeth)."""
    if not obb_obb(a, b, eps=0.0):
        return 0.0
    lo, hi = 0.0, 1.5
    for _ in range(18):
        mid = (lo + hi) / 2
        if obb_obb(a, b, eps=mid):
            lo = mid
        else:
            hi = mid
    return 2 * lo


MESH_LIMIT = 0.3                             # voxels: box teeth run into each other a little where a real tooth form would not


def check_gearing(v):
    """Each rabble's bevel pair: the pinion's pitch circle and the wheel's touch at one point, on the wheel's
    north rim, and their pitch cones share the apex where the axes meet; the rig turns the spindle at -NP/NW of
    the shaft, so the contact point moves alike on both (finite differences of the posed rig, either way round);
    the teeth stay in step (the pinion tooth nearest the contact and the wheel's gap there within a tenth of a
    tooth); and the box teeth run no more than MESH_LIMIT into each other over a tooth's cycle."""
    m = v.m
    want = -m.NP / m.NW
    worst_v, worst_g, worst_d, where = 0.0, 0.0, 0.0, None
    d = 1e-4
    for k, zs in enumerate(m.RABBLE_Z, 1):
        dr = next(p for p in v.parts if p["id"] == f"rabble{k}")["drivers"][0]
        if abs(dr["ratio"] - want) > 1e-6:
            v.fail(f"rabble {k} turns at {dr['ratio']}, not {want}")
        zp = m.pinion_z(zs)
        if abs(zp + m.RW - zs) > 1e-9 or abs(m.WHEEL_Y + m.RP - m.SHAFT[1]) > 1e-9:
            v.fail(f"bevel pair {k}: the pitch cones' apexes are not where the axes meet")
        contact = [m.SHAFT[0], m.WHEEL_Y, zp]
        # both turn about fixed axes, so a world point's velocity is the same at every angle: take it at rest
        # (where the material point at the contact is the contact), either way round
        for th in (0.0,):
            for sgn in (1.0, -1.0):
                t2 = th + sgn * d
                a0, a1 = v.point("lineshaft", th, contact), v.point("lineshaft", t2, contact)
                b0, b1 = v.point(f"rabble{k}", th, contact), v.point(f"rabble{k}", t2, contact)
                va = [(a1[i] - a0[i]) / d for i in range(3)]
                vb = [(b1[i] - b0[i]) / d for i in range(3)]
                mag = max(math.sqrt(sum(x * x for x in va)), 1e-12)
                worst_v = max(worst_v, math.sqrt(sum((va[i] - vb[i]) ** 2 for i in range(3))) / mag)
        for th in thetas(72):
            # the pinion's teeth: the one pointing most nearly down, its x at the pitch circle
            ang = [math.pi + math.tau * i / m.NP + th for i in range(m.NP)]
            a = min(ang, key=lambda t: abs(math.remainder(t - math.pi, math.tau)))
            xp = -m.RP * math.sin(a)
            # the wheel's gaps after it has turned RATIO * theta: the one nearest the north rim, its x
            gaps = [math.pi + math.tau * j / m.NW + m.RATIO * th for j in range(m.NW)]
            g = min(gaps, key=lambda t: abs(math.remainder(t - math.pi, math.tau)))
            xc = m.RW * math.sin(g)
            worst_g = max(worst_g, abs(xp - xc))
        for i in range(48):
            th = 0.31 + (math.tau / m.NP) * i / 48
            for a in v.named("lineshaft", rf"_pinion{k}_tooth", th):
                alo, ahi = a.aabb()
                for b in v.named(f"rabble{k}", r"_wheel_tooth", th):
                    blo, bhi = b.aabb()
                    if all(alo[q] < bhi[q] and blo[q] < ahi[q] for q in range(3)):
                        dd = overlap_depth(a, b)
                        if dd > worst_d:
                            worst_d, where = dd, (a.name, b.name, round(th, 3))
    pitch = math.pi * m.MODULE
    print(f"gearing: {m.NP}-tooth bevel pinions on the {m.NW}-tooth wheels' north rims, the spindles at {want:.4f} of the shaft; contact "
          f"speeds differ by {100 * worst_v:.3f} %; tooth and gap within {worst_g:.4f} voxels at the contact (a tooth's pitch is "
          f"{pitch:.3f}); teeth run at most {worst_d:.3f} into each other (limit {MESH_LIMIT}){'' if where is None else f' {where}'}")
    if worst_v > 0.01:
        v.fail("a bevel pair's contact point does not move alike on both gears")
    if worst_g > 0.1 * pitch:
        v.fail("a pinion's teeth drift out of the wheel's gaps")
    if worst_d > MESH_LIMIT:
        v.fail("a bevel pair's teeth run too far into each other")


def check_reverse(v):
    """Every machine tolerates the axle turning either way (the owner's rule): the entry shaft turns with the axle,
    and everything beyond the gear case's rectifier (the line shaft, the rabbles, the feed roll) is posed the same
    for the axle turned back as forward by as much, so the rabbles plough towards the discharge and the roll feeds
    either way; nothing at level 1 moves."""
    m = v.m
    worst, entry_moves = 0.0, True
    for th in (0.7, 2.9, 7.3):
        for pid in m.RECTIFIED:
            a, b = m.pm(v.parts, pid, (th, "all")), m.pm(v.parts, pid, (-th, "all"))
            worst = max(worst, max(abs(a[r][c] - b[r][c]) for r in range(3) for c in range(4)))
        a, b = m.pm(v.parts, "entry", (th, "all")), m.pm(v.parts, "entry", (-th, "all"))
        entry_moves = entry_moves and max(abs(a[r][c] - b[r][c]) for r in range(3) for c in range(4)) > 1e-3
    l1 = [pid for pid in m.MOVING if v.shows(pid, "level1")]
    print(f"reverse: beyond the rectifier {', '.join(m.RECTIFIED)} are posed alike for the axle turned either way (worst "
          f"difference {worst:.1e}); the entry turns back with the axle; moving at level 1: {l1 or 'nothing'}")
    if worst > 1e-12 or not entry_moves or set(m.MOVING) - set(m.RECTIFIED) != {"entry"} or l1:
        v.fail("a working part turns back when the axle does")


def check_feedroll(v):
    """The feed roll is keyed on the line shaft (it turns with it, about the same axis, at every angle), sits
    under the hopper's slot and over the feed chute, and its flutes stay inside the slot's width."""
    m = v.m
    worst = 0.0
    for th in thetas(12):
        a, b = v.mat("lineshaft", th), v.mat("feedroll", th)
        worst = max(worst, max(abs(a[r][c] - b[r][c]) for r in range(3) for c in range(4)))
    lo, hi = aabb_of(v.named("feedroll", r"."))
    (sx0, sx1), (sz0, sz1) = m.HOPPER_SLOT
    (cx0, cx1), (cz0, cz1) = m.CHUTE
    ok = sx0 < lo[0] and hi[0] < sx1 and sz0 <= lo[2] and hi[2] <= sz1 and cz0 >= lo[2] - 1e-6 - 0.5 and lo[1] > m.CHUTE_Y[1]
    print(f"feed roll: turns with the line shaft (worst matrix difference {worst:.1e}); x {lo[0]:.2f}..{hi[0]:.2f} inside the slot "
          f"{sx0:g}..{sx1:g}, z {lo[2]:.2f}..{hi[2]:.2f}; its foot at y {lo[1]:.2f} over the chute's top {m.CHUTE_Y[1]:g}")
    if worst > 1e-12 or not ok:
        v.fail("the feed roll is not keyed on the shaft, under the slot and over the chute")


# ---------------------------------------------------------------- the material's path, the firing, the doors
def sweep(v, k):
    """Rabble k's sweep: the largest distance (in plan) of its ploughs' corners from its spindle."""
    zs = v.m.RABBLE_Z[k - 1]
    return max(math.hypot(p[0] - v.m.SHAFT[0], p[2] - zs) for e in v.named(f"rabble{k}", r"plough") for p in e.corners())


def check_discharge(v):
    """The roasted ore's way out: the hearth's discharge hole is over the frame's pit, by the bridge, in the first
    rabble's sweep; the charge drops from the feed chute in the last rabble's sweep, and each rabble's sweep meets
    the next's, so the charge is passed from the feed to the discharge (review: the old charge fell out of reach);
    the sweeps clear the bridge and the flue chamber; the stalls' fronts open onto the forecourt where the pit is."""
    m = v.m
    (dx0, dx1), (dz0, dz1) = m.DISCHARGE
    (px0, px1), (pz0, pz1) = m.PIT
    if not (px0 <= dx0 and dx1 <= px1 and pz0 <= dz0 and dz1 <= pz1):
        v.fail("the discharge hole is not over the pit")
    r = [sweep(v, k) for k in (1, 2)]
    z1, z2 = m.RABBLE_Z
    charge = (m.CHUTE[1][0] + m.CHUTE[1][1]) / 2
    if not (m.BRIDGE_Z[1] < dz0 and z1 - r[0] < (dz0 + dz1) / 2):
        v.fail("the discharge hole is not in the first rabble's sweep, past the bridge")
    if not charge < z2 + r[1]:
        v.fail(f"the charge drops at z {charge:g}, beyond the last rabble's sweep (to {z2 + r[1]:.2f})")
    if not z1 + r[0] >= z2 - r[1] - 0.25:
        v.fail("the rabbles' sweeps do not meet")
    if not (z1 - r[0] > m.BRIDGE_Z[1] + 1.0 and z2 + r[1] < m.HEARTH_Z[1] - 1.0):
        v.fail("a rabble's sweep comes within a voxel of the bridge or the flue chamber")
    if not m.ST_FRONT <= px0:
        v.fail("the pit is under the stalls, not in front of them")
    print(f"discharge: the hole z {dz0:g}..{dz1:g} over the pit (z {pz0:g}..{pz1:g}); rabble sweeps {r[0]:.2f}, {r[1]:.2f}: z "
          f"{z1 - r[0]:.2f}..{z1 + r[0]:.2f} and {z2 - r[1]:.2f}..{z2 + r[1]:.2f} (bridge to {m.BRIDGE_Z[1]:g}, the chamber from "
          f"{m.HEARTH_Z[1]:g}); the charge drops at z {charge:g}")


def check_flue(v):
    """The fumes' way out, each level: level 1's collecting flue runs from over its stalls' back wall into the
    header, which closes the flue chamber's throat; level 2's hearth opens into the throat under the arch."""
    m = v.m
    (ix0, ix1), (iy0, iy1) = m.INLET
    z = m.FLUEBOX[2][0]
    hdr = aabb_of(v.named("stallflue", r"_header"))
    if not (hdr[0][0] <= ix0 and ix1 <= hdr[1][0] and hdr[0][1] <= iy0 and iy1 <= hdr[1][1] and abs(hdr[1][2] - z) < 1e-6):
        v.fail("the stalls' header does not close the chamber's throat")
    duct = aabb_of(v.named("stallflue", r"_duct"))
    if abs(duct[1][2] - hdr[0][2]) > 1e-6 or duct[0][2] > m.STALLS[0][0]:
        v.fail("the collecting flue does not run from the first stall to the header")
    for s0, s1 in m.STALLS:
        zc = (s0 + s1) / 2
        foot = [e for e in v.named("stalls", r"_backfoot\d") if e.aabb()[0][2] - 1e-6 <= zc <= e.aabb()[1][2] + 1e-6]
        if foot:
            v.fail(f"the stall at z {s0:g}..{s1:g} has no flue mouth at the foot of its back wall")
    hearth_top = max(e.aabb()[1][1] for e in v.by_part["hearth"] if e.name.startswith("hearth_bed"))
    if not (iy0 <= hearth_top + 1e-6 and iy1 <= m.CROWN_Y[0] and abs(m.HEARTH_Z[1] - z) < 1e-6):
        v.fail("the hearth does not open into the chamber's throat under the arch")
    print(f"flue: level 1's collecting flue z {duct[0][2]:g}..{duct[1][2]:g} into the header over the throat (x {ix0:g}..{ix1:g}, "
          f"y {iy0:g}..{iy1:g}); level 2's hearth (top {hearth_top:g}) opens into it under the arch's crown ({m.CROWN_Y[0]:g})")


def check_firing(v):
    """Level 2 fires itself (review: an unattended mill level must not look hand-stoked): the treads step down
    from the fire end towards the bridge, the coal bunker's throat ends over the top tread, its mouth is the
    fuel cell's top, and the fire end's only door is the ash pit's, below the lowest tread (no stoking door)."""
    m = v.m
    treads = sorted(v.named("firebox", r"_step\d"), key=lambda e: e.c[2])
    tops = [e.aabb()[1][1] for e in treads]
    if not all(a > b for a, b in zip(tops, tops[1:])) or treads[-1].aabb()[1][2] > m.BRIDGE_Z[0] + 1e-6:
        v.fail("the step grate does not step down to the bridge")
    throat = aabb_of(v.named("firebox", r"_throat"))
    top = treads[0].aabb()
    if not (throat[0][1] > top[1][1] and throat[0][2] < top[1][2] and throat[1][2] > top[0][2]):
        v.fail("the bunker's throat does not end over the top tread")
    bunker = aabb_of(v.named("firebox", r"_bunker"))
    fc = [m.FUEL_CELL[k] * 16 for k in range(3)]
    if abs(bunker[1][1] - (m.FUEL_CELL[1] + 1) * 16) > 1e-6 or not (bunker[0][0] < fc[0] + 16 and bunker[1][0] > fc[0]):
        v.fail("the bunker's mouth is not the fuel cell's top")
    front = [e for e in v.by_part["ironwork"] if e.aabb()[1][2] <= m.FRONT_Z[0] + 1e-6]
    doors = sorted({re.sub(r"\d+$", "", e.name) for e in front})
    grate = tops[0] - m.STEP_T                   # the top tread's underside: the grate's level at the fire end's wall
    high = [e.name for e in front if e.aabb()[1][1] > grate]
    if high:
        v.fail(f"a door on the fire end above the grate: {high}")
    print(f"firing: {len(treads)} treads at y {', '.join(f'{t:g}' for t in tops)} down to the bridge; the throat's foot at y "
          f"{throat[0][1]:g} over the top tread; the bunker's mouth at y {bunker[1][1]:g}; on the fire end only {doors}, all "
          f"below the grate's level at the fire end ({grate:g})")


def check_doors(v):
    """The furnace's inside can be seen (review: the old furnace was closed): beside each rabble a work door
    through each side wall stands open, its opening clear of every static element drawn at level 2 from outside
    the wall to the hearth, and a sight line from outside the door to the spindle, at the arms' height and at the
    hearth's, crosses nothing but the rabble and the charge."""
    m = v.m
    statics = [e for pid in static_parts(v, "level2") if pid != "charge" for e in v.by_part[pid]]
    hw, (y0, y1) = m.WORK_DOOR
    seen = 0
    for k, zc in enumerate(m.RABBLE_Z, 1):
        for x_out in (-1.0, 49.0):
            for y in ((m.ARM_Y[0] + m.ARM_Y[1]) / 2, m.HEARTH_Y + 1.5):
                for dz in (-hw + 1.0, 0.0, hw - 1.0):
                    a, b = [x_out, y, zc + dz], [m.SHAFT[0], y, zc]
                    hit = None
                    for i in range(1, 160):
                        t = i / 160
                        p = [a[q] + (b[q] - a[q]) * t for q in range(3)]
                        hit = next((e.name for e in statics if inside(p, e, 0.01)), None)
                        if hit:
                            break
                    if hit:
                        v.fail(f"the view through door {k} from x {x_out:g} at y {y:g}, z {zc + dz:g} is blocked by {hit}")
                    else:
                        seen += 1
    print(f"work doors: {seen} sight lines from outside the open doors to the spindles at the arms' and the hearth's height, "
          f"all clear (openings z ±{hw:g} about each rabble, y {y0:g}..{y1:g})")


# ---------------------------------------------------------------- the frame and the anchors
def check_anchors(v):
    """The anchors are the frame's (or the hopper's, which both levels keep) and never move: the power cell's
    face is the footprint's outside and the line shaft's entry reaches it at the cell's centre; the hopper's mouth
    is the infeed cell's top, inside the cell; the spout is in the output cell, its mouth on its east face; the
    stack, the flue chamber and the pit are the frame's."""
    m = v.m
    cells = set(m.footprint())
    normals = {"north": (0, 0, -1), "south": (0, 0, 1), "east": (1, 0, 0), "west": (-1, 0, 0), "up": (0, 1, 0), "down": (0, -1, 0)}
    for key, cell, face in (("power", m.POWER_CELL, m.POWER_FACE), ("infeed", m.INFEED_CELL, m.INFEED_FACE),
                            ("output", m.OUTPUT_CELL, m.OUTPUT_FACE), ("fuel", m.FUEL_CELL, m.FUEL_FACE)):
        n = normals[face]
        out = tuple(cell[k] + n[k] for k in range(3))
        if cell not in cells or out in cells:
            v.fail(f"the {key} cell {cell} is not in the footprint with its {face} face outside")
    entry = aabb_of(v.named("entry", r"."))
    c = [(m.POWER_CELL[k] + 0.5) * 16 for k in range(3)]
    if abs(entry[0][2] - m.POWER_CELL[2] * 16) > 1e-6 or abs((entry[0][0] + entry[1][0]) / 2 - c[0]) > 1e-6 or abs((entry[0][1] + entry[1][1]) / 2 - c[1]) > 1e-6:
        v.fail(f"the line shaft's entry does not meet the power face at the cell's centre: {entry}")
    top = (m.INFEED_CELL[1] + 1) * 16
    lo_c = [m.INFEED_CELL[k] * 16 for k in range(3)]
    lo, hi = aabb_of(v.named("hopper", r"_bin_"))
    inside_ = all(lo_c[k] - 1e-6 <= lo[k] and hi[k] <= lo_c[k] + 16 + 1e-6 for k in (0, 2))
    if abs(hi[1] - top) > 1e-6 or not inside_:
        v.fail(f"the hopper's mouth is not at the infeed cell's top ({hi[1]:.2f}, inside {inside_})")
    spout = aabb_of(v.named("frame", r"^fr_spout"))
    oc = [m.OUTPUT_CELL[k] * 16 for k in range(3)]
    if abs(spout[1][0] - (m.OUTPUT_CELL[0] + 1) * 16) > 1e-6 or not all(oc[k] <= spout[0][k] and spout[1][k] <= oc[k] + 16 for k in (1, 2)):
        v.fail(f"the spout is not in the output cell, on its east face: {spout}")
    stack = aabb_of(v.named("frame", r"^fr_stack"))
    if abs(stack[1][1] - m.STACK_TOP) > 1e-6:
        v.fail("the stack's top is not STACK_TOP")
    for rx in (r"^fr_stack_", r"^fr_chamber_", r"^fr_spout", r"^fr_plinth", r"^fr_sulfurdoor"):
        if not v.named("frame", rx):
            v.fail(f"the frame has no {rx}")
    print(f"anchors: power {m.POWER_CELL} {m.POWER_FACE} (the entry at its centre), infeed {m.INFEED_CELL} {m.INFEED_FACE} (the hopper's "
          f"mouth at y {top:g}, both levels), output {m.OUTPUT_CELL} {m.OUTPUT_FACE} (the spout's mouth at x {spout[1][0]:g}), fuel "
          f"{m.FUEL_CELL} {m.FUEL_FACE}; the stack to y {stack[1][1]:g}, the chamber, the pit and the spout in the frame")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        th, s = pose
        els = [e for pid in v.by_part if v.shows(pid, s) for e in v.posed(pid, th)]
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses (each state alone)")
    if bad:
        v.fail("faces z-fight")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_textures(v)
    check_levels(v)
    check_floating(v)
    check_containment(v)
    check_anchors(v)
    check_flue(v)
    check_discharge(v)
    check_firing(v)
    check_doors(v)
    check_supports(v)
    check_gearing(v)
    check_feedroll(v)
    check_reverse(v)
    check_clearances(v, 24, "clearances")
    if not quick:
        check_clearances(v, 144, "swept paths (every 5 degrees of the shaft over the rabbles' two turns)")
        check_zfight(v)
    return v.ok


# ---------------------------------------------------------------- the files
def collision(cell, fitted):
    """A cell's collision boxes (cell-local) with `fitted` the requires values fitted: the frame's and those."""
    out = list(cell.get("boxes", []))
    for r, bs in cell.get("fitted", {}).items():
        if r in fitted:
            out += bs
    return out


def check_collision(m, ship):
    """Collision follows what is fitted (review: level 1's stalls stood inside the furnace's walls): no cell's
    frame boxes come from a fitted part; at level 1 (its parts, no contents) each stall's middle and the forecourt
    in front of it are open, and at level 2 the furnace's walls are solid where the stalls were open."""
    ok = True
    values = set(m.LEVELS["1"]) | set(m.LEVELS["2"]) | set(m.CONTENTS["1"]) | set(m.CONTENTS["2"])
    bad = sorted({r for c in ship["cells"] for r in c.get("fitted", {}) if r not in values})
    if bad:
        print(f"FAIL fitted boxes keyed by values that are not the rig's: {bad}")
        ok = False
    cells = {tuple(c["pos"]): c for c in ship["cells"]}

    def solid(p, fitted):
        """Whether the build-frame point p (voxels) is inside a collision box with `fitted`."""
        cell = tuple(int(p[k] // 16) - m.ORIGIN_CELL[k] for k in range(3))
        local = [(p[k] / 16) % 1.0 for k in range(3)]
        c = cells.get(cell)
        if c is None:
            return False
        bs = collision(c, fitted)
        if not bs and "boxes" not in c and "fitted" not in c:
            return True
        return any(all(b[k] < local[k] < b[k + 3] for k in range(3)) for b in bs)
    l1, l2 = set(m.LEVELS["1"]), set(m.LEVELS["2"])
    probes = []
    for s0, s1 in m.STALLS:
        zc = (s0 + s1) / 2
        probes += [(f"stall z {zc:g}", [(m.ST_BACK[1] + m.ST_FRONT) / 2, m.PLINTH + 8.0, zc]),
                   (f"forecourt z {zc:g}", [(m.ST_FRONT + 46.0) / 2, m.PLINTH + 8.0, zc])]
    open1 = [name for name, p in probes if solid(p, l1)]
    if open1:
        print(f"FAIL at level 1 these are inside collision boxes: {open1}")
        ok = False
    wall2 = [[(m.WALL_X[0][0] + m.WALL_X[0][1]) / 2, m.PLINTH + 4.0, (s0 + s1) / 2] for s0, s1 in m.STALLS]
    if not all(solid(p, l2) for p in wall2):
        print("FAIL at level 2 the furnace's west wall is not solid beside the stalls' old places")
        ok = False
    frame_only = sum(1 for c in ship["cells"] if "boxes" in c and "fitted" not in c)
    fitted_only = sum(1 for c in ship["cells"] if "fitted" in c and "boxes" not in c)
    print(f"collision: {len(ship['cells'])} cells, {frame_only} with frame boxes only, {fitted_only} with fitted boxes only; at level 1 "
          f"the stalls and the forecourt ({len(probes)} probes) are open; at level 2 the furnace's wall is solid there")
    return ok


def validate_files(m, shape, frame_shape, ship):
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL textures used but not declared: {missing}")
        ok = False
    gaps = lid_gaps(ship["cells"])
    empty = [c["pos"] for c in ship["cells"] if not c.get("boxes") and not c.get("fitted")]
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, {len(empty)} "
          f"with no boxes at any level; lids over every column: {'yes' if not gaps else gaps}")
    if gaps or empty:
        ok = False
    for key in ("powerCell", "infeedCell", "outputCell", "fuelCell"):
        if ship[key] not in [c["pos"] for c in ship["cells"]]:
            print(f"FAIL the {key} is not one of the cells")
            ok = False
    return check_collision(m, ship) and ok
