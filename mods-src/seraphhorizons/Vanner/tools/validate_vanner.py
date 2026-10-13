"""Salvaged from origin/735-concentrator-model-v2 (Concentrator/tools/validate_concentrator.py), not yet
adapted: the check framework, textures by role, thickness, containment, floating, the drops, the
yokes, the drive (belt, worm, ratchet and pawl), supports, clearances and z-fighting (#924)."""

from __future__ import annotations

import math
import re

from machinegen.checks import (box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb,
                               supports)
from machinegen.geometry import aabb_of, mvec
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import part_of, posed

TAU = 2 * math.pi
MOVING = ("lineshaft", "jigsheave", "tablesheave", "vannerpulley", "jigplunger", "tabledeck", "vannershaft", "vannerworm",
          "vannerframe", "vannerhead", "vannerfoot")


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

    def mat(self, pid, pose):
        return self.m.pm(self.parts, pid, pose[:1])        # turned from rest: the travel is |theta|

    def posed(self, pid, pose):
        key = (pid, pose[0])
        if key not in self.cache:
            if len(self.cache) > 60000:
                self.cache.clear()
            mm = self.mat(pid, pose)
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def point(self, pid, pose, p):
        q = _apply(self.mat(pid, pose), [c / 16 for c in p])
        return [c * 16 for c in q]

    def named(self, pid, rx, pose=None):
        src = self.posed(pid, pose) if pose is not None else self.by_part.get(pid, [])
        r = re.compile(rx)
        return [e for e in src if r.search(e.name)]

    def shown(self, state):
        """The part ids that show in a state."""
        return [pid for pid in self.by_part if self.m.on_show(self.req[pid], state)]


def turns(n=24, state="level4dressed"):
    """A turn of the axle in n steps, in a state."""
    return [(TAU * i / n, state) for i in range(n + 1)]


# ---------------------------------------------------------------- parts
def check_basic(v):
    worst = euler_round_trip(v.els)
    print(f"euler round trip: worst {worst:.1e}")
    if worst > 1e-9:
        v.fail("an element's rotation does not survive being written as Euler angles")
    seen, dup = set(), set()
    for n in (el.name for el in v.els):
        (dup if n in seen else seen).add(n)
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


def check_thickness(v):
    """No part thinner than about a voxel: every element at least 0.6 across in its two larger
    dimensions' smaller one, but for the listed thin-by-nature pieces (screens, belts, plates, coats, lips)."""
    thin_ok = re.compile(r"(sieve|belt|edge|plate\d|coat|well|pd_|_lip$|tooth|thread|_web|_rim|spout_floor|_floor|riffle|"
                         r"_wl_|_fb_|bolt|strap|sill\d|handle|stem|_arm\d?$|clip|bracket|tb_tt_side|plug|feed_spout|joint)")
    bad = []
    for el in v.els:
        s = sorted(abs(x) for x in el.size)
        if s[1] < 0.6 and not thin_ok.search(el.name):
            bad.append((el.name, [round(x, 2) for x in el.size]))
    print(f"thickness: {len(bad)} elements thinner than 0.6 across outside the thin-by-nature list")
    if bad:
        v.fail(f"too thin to read at block scale: {bad[:6]}")


def check_containment(v, poses):
    m = v.m
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where = -1e9, None
    for pose in poses:
        for pid in v.by_part:
            for el in v.posed(pid, pose):
                lo, hi = el.aabb()
                o = box_overhang(lo, hi, box)
                if o > worst:
                    worst, where = o, (el.name, pose)
    print(f"containment over {len(poses)} poses: worst overhang {worst:.3f} voxels ({where[0]})")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the machine box at {where[1]}")


def check_floating(v):
    """At rest, the frame alone and the frame with each state's parts (moving ones included: a shaft is
    joined through its bearings) are one piece from the ground."""
    m = v.m
    for state in m.STATES:
        els = [e for pid in v.shown(state) for e in v.posed(pid, (0.0, state))]
        seen, loose = frame_floating(els)
        print(f"floating ({state}): {len(seen)} of {len(els)} elements joined to the ground")
        if loose:
            v.fail(f"in {state} these float: {loose[:8]}")


def ray_down(el, p):
    """The distance down (along -y) from point p to where it enters element el, or None."""
    d = [p[k] - el.c[k] for k in range(3)]
    o = [sum(el.r[k][i] * d[k] for k in range(3)) for i in range(3)]
    dirn = [-el.r[1][i] for i in range(3)]
    t0, t1 = 0.0, 1e9
    for i in range(3):
        h = abs(el.size[i]) / 2
        if abs(dirn[i]) < 1e-12:
            if abs(o[i]) > h:
                return None
            continue
        a, b = (-h - o[i]) / dirn[i], (h - o[i]) / dirn[i]
        a, b = min(a, b), max(a, b)
        t0, t1 = max(t0, a), min(t1, b)
        if t0 > t1:
            return None
    return t0


def first_below(v, p, pose):
    state = pose[1]
    best = None
    for pid in v.shown(state):
        for el in v.posed(pid, pose):
            lo, hi = el.aabb()
            if not (lo[0] <= p[0] <= hi[0] and lo[2] <= p[2] <= hi[2] and lo[1] <= p[1] + 1e-9):
                continue
            t = ray_down(el, p)
            if t is not None and (best is None or t < best[0]):
                best = (t, el.name)
    return best

# ---------------------------------------------------------------- mechanism
def angle_about(v, pid, pose, axis):
    mm = v.mat(pid, pose)
    if axis == "x":
        return math.atan2(mm[2][1], mm[1][1])
    return math.atan2(mm[1][0], mm[0][0])


def check_yokes(v):
    """Each sheave in its yoke: at every step of a turn the sheave's centre is on the yoke's slot line (the
    yoke follows it exactly: its bars stay tangent to the sheave's flats' circle) and the sheave stays
    inside the slot's length."""
    m = v.m
    rc = m.SHEAVE_RC
    cases = [("jig", "jigsheave", (m.JIG_X, m.LS[0], m.LS[1] - m.JIG_E), "jigplunger", "jigplunger_yoke", 1, "level2"),
             ("table", "tablesheave", (m.TABLE_X, m.LS[0] + m.TABLE_E, m.LS[1]), "tabledeck", "tabledeck_yoke", 2, "level3")]
    for i, xs in enumerate(m.CS_SHEAVES_X, 1):
        cases.append((f"vanner {i}", "vannershaft", (xs, m.CS[0] + m.VN_E, m.CS[1]), "vannerframe", f"vannerframe_yoke{i}", 2, "level4"))
    worst = 0.0
    for label, sp, centre, yp, yname, axis, state in cases:
        other = 2 if axis == 1 else 1
        for pose in turns(48, state):
            p = v.point(sp, pose, centre)
            bars = {e.name.rsplit("_", 1)[1]: e.aabb() for e in v.named(yp, rf"^{yname}_(top|bottom|n|s)$", pose)}
            lo_k, hi_k = ("bottom", "top") if axis == 1 else ("n", "s")
            lo_o, hi_o = ("n", "s") if axis == 1 else ("bottom", "top")
            inner_lo, inner_hi = bars[lo_k][1][axis], bars[hi_k][0][axis]
            mid = (inner_lo + inner_hi) / 2
            worst = max(worst, abs(p[axis] - mid))
            half = (inner_hi - inner_lo) / 2
            end_lo, end_hi = bars[lo_o][1][other], bars[hi_o][0][other]
            if abs(p[axis] - mid) > 1e-6 or abs(half - rc - 0.01) > 1e-6 or not (end_lo + rc < p[other] < end_hi - rc):
                v.fail(f"{label}: the sheave is off its yoke's slot at {pose} ({p[axis]:.4f} vs {mid:.4f}, across {p[other]:.3f} "
                       f"in {end_lo:.3f}..{end_hi:.3f})")
                break
    print(f"yokes: {len(cases)} eccentric sheaves, each on its yoke's slot line through a turn (worst {worst:.1e}), its bars "
          f"{rc + 0.01:.3f} off the sheave's centre (its corners {rc:.3f}), the sheave inside the slot's length")


def check_drive(v):
    """The vanner's drive belt: its pulleys' rims at one speed and its runs on their tangents; the worm on
    its wheel's pitch circle, the wheel a tooth a worm turn; the belt's upper run moving to the head as the
    axle turns forward (and backwards with it: there is no rectifier)."""
    m = v.m
    a, b = (0.0,), (0.4,)
    for pa, ra, pb, rb in (("vannerpulley", m.VPULLEY_R, "vannershaft", m.VPULLEY_R),):
        da = angle_about(v, pa, b, "x") - angle_about(v, pa, a, "x")
        db = angle_about(v, pb, b, "x") - angle_about(v, pb, a, "x")
        if abs(da * ra - db * rb) > 1e-6:
            v.fail(f"the vanner's drive belt slips: {pa} {da * ra:.5f}, {pb} {db * rb:.5f}")
    for run in v.named("vanner", r"^vn_belt_run[ab]$"):
        k = min((q for q in range(3) if abs(run.r[0][q]) < 0.5), key=lambda q: abs(run.size[q]))
        n = mvec(run.r, [1.0 if q == k else 0.0 for q in range(3)])
        t = abs(run.size[k])
        for c, r_ in (((0.0, *m.LS), m.VPULLEY_R), ((0.0, *m.CS), m.VPULLEY_R)):
            dist = abs(sum((c[q] - run.c[q]) * n[q] for q in (1, 2)))
            if abs(dist - (r_ + t / 2)) > 1e-6:
                v.fail(f"{run.name} is {dist:.4f} from a pulley's centre, not its radius {r_} plus half its thickness")
    d = m.CS[0] - m.HEAD[1]
    if abs(d - (m.WHEEL_R + m.WORM_R)) > 1e-9 or not (m.WHEEL_Z[0] < m.CS[1] < m.WHEEL_Z[1]):
        v.fail("the worm is not on its wheel's pitch circle, in its plane")
    dw = angle_about(v, "vannerworm", b, "x") - angle_about(v, "vannerworm", a, "x")
    dh = angle_about(v, "vannerhead", b, "z") - angle_about(v, "vannerhead", a, "z") \
        - (angle_about(v, "vannerframe", b, "z") - angle_about(v, "vannerframe", a, "z"))
    if abs(abs(dh) * m.WORM_N - abs(dw)) > 1e-5:
        v.fail(f"the worm wheel turns {dh:.5f} as the worm turns {dw:.4f}, not a tooth in {m.WORM_N} a turn")
    top = [m.HEAD[0], m.HEAD[1] + m.ROLL_R, 37.0]
    p0 = v.point("vannerhead", a, top)
    for th in (0.3, -0.3, 2.0, -2.0):
        p1 = v.point("vannerhead", (th,), top)
        f1 = v.point("vannerframe", (th,), top)
        w0, w1 = angle_about(v, "vannerworm", a, "x"), angle_about(v, "vannerworm", (th,), "x")
        if (p1[0] - (f1[0] - top[0])) - p0[0] <= 0 or w1 - w0 <= 0:
            v.fail(f"the belt does not creep to the head with the axle turned {th:+}: the ratchet must turn the worm forward either way")
    # the ratchet: the pawl under its lowest teeth, a tooth's pitch pushed each turn
    tooth = TAU * m.RATCHET_R / m.RATCHET_N
    plo, phi = v.named("vannerframe", r"_pawl$")[0].aabb()
    gap = (m.CS[0] - m.RATCHET_R - m.RATCHET_MOD) - phi[1]
    if not (0.0 < gap < 0.2) or abs(m.PAWL_TRAVEL - tooth) > 0.1 * tooth or not (plo[2] < m.CS[1] - m.VN_E and phi[2] > m.CS[1] + m.VN_E):
        v.fail(f"the pawl is not under the ratchet's teeth through the shake (gap {gap:.3f}, stroke {m.PAWL_TRAVEL} for a tooth of {tooth:.2f})")
    per_turn = m.ROLL_R * TAU * m.WORM_RATIO / m.WORM_N
    print(f"drive: the vanner's belt 1:1, its runs on their tangents; the ratchet: 12 teeth {tooth:.2f} apart, the pawl {gap:.2f} under "
          f"them, pushed {m.PAWL_TRAVEL} a stroke: the worm shaft turns {m.WORM_RATIO:.4f} a turn of the axle, either way; worm: centre "
          f"distance {d:.2f} (pitch radii {m.WHEEL_R:.2f} + {m.WORM_R:.2f}), the wheel a tooth a worm turn (1/{m.WORM_N}): the belt "
          f"creeps {per_turn:.3f} voxels a turn to the head, whichever way the axle turns")


# ---------------------------------------------------------------- clearances
# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels at every sampled pose.
ALLOWED = [
    # shafts in their bearings, sheaves and pulleys on their shafts
    ("lineshaft", r"_rod|_coupling|_collar", "linehangers", r"ls_bearing"),
    ("jigsheave|tablesheave|vannerpulley", None, "lineshaft", r"_rod"),
    ("vannershaft|vannerworm", r"_rod|_collar", "vanner", r"vn_bearing"),
    ("vannerhead|vannerfoot", r"_journal", "vannerframe", r"bearing|_rail"),
    # the sheaves in their yokes
    ("jigsheave", None, "jigplunger", r"_yoke"), ("tablesheave", None, "tabledeck", r"_yoke"),
    ("vannershaft", r"_sheave", "vannerframe", r"_yoke"),
    # the worm and its wheel; the belts on their pulleys and rollers
    ("vannerworm", r"_worm", "vannerhead", r"_wheel"),
    ("vanner", r"vn_belt", "vannerpulley|vannershaft", r"pulley"),
    ("vannerframe", r"_belt|_edge", "vannerhead|vannerfoot", r"_drum"),
    # the rods in their guides, the slides
    ("jigplunger", r"_rod", "jig", r"jg_guide_eye"),
    ("tabledeck", r"_joist", "table", r"tb_slide"), ("vannerframe", r"_rail", "vanner", r"vn_slide\d"),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose, moving_only=False):
    state = pose[1]
    items = []
    for pid in v.shown(state):
        for el in v.posed(pid, pose):
            items.append((pid, el, el.aabb()))
    grid = {}
    for i, (pid, el, (lo, hi)) in enumerate(items):
        for cx in range(int(lo[0] // 4), int(hi[0] // 4) + 1):
            for cy in range(int(lo[1] // 4), int(hi[1] // 4) + 1):
                for cz in range(int(lo[2] // 4), int(hi[2] // 4) + 1):
                    grid.setdefault((cx, cy, cz), []).append(i)
    hits = {}
    seen = set()
    for idx in grid.values():
        for a in range(len(idx)):
            for b in range(a + 1, len(idx)):
                i, j = idx[a], idx[b]
                if (i, j) in seen:
                    continue
                seen.add((i, j))
                pa, ea, (alo, ahi) = items[i]
                pb, eb, (blo, bhi) = items[j]
                if pa == pb or (moving_only and pa not in MOVING and pb not in MOVING):
                    continue
                if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                    continue
                if allowed(pa, ea.name, pb, eb.name):
                    continue
                if obb_obb(ea, eb):
                    hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def check_clearances(v, poses, label, moving_only=False):
    allhits = {}
    for pose in poses:
        for key, hs in touching_pairs(v, pose, moving_only).items():
            allhits.setdefault(key, {})
            for h in hs:
                allhits[key].setdefault(h, pose)
    print(f"{label} over {len(poses)} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {p[0]:.3f} ({p[1]})" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


def clearance_poses(n=16):
    out = []
    for state in ("frame", "level1", "level2plates", "level3dressed", "level4dressed"):
        out += turns(n, state)
    out += [(-0.9, "level4dressed"), (-2.4, "level4dressed")]
    return out


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = [e for pid in v.shown(pose[1]) for e in v.posed(pid, pose)]
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses")
    if bad:
        v.fail("faces z-fight")


