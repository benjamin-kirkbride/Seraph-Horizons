"""The gear cutter generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, coplanar_faces, drawn_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
from machinegen.checks import sight_glass
from machinegen.checks import supports as shaft_supports
from machinegen.checks import bearing_margin
from machinegen.geometry import aabb_of
from machinegen.rigmath import part_of, posed
from machinegen.rigmath import apply as _apply


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

    def mat(self, pid, pose):
        return self.m.pm(self.parts, pid, pose)

    def posed(self, pid, pose):
        key = (pid, pose)
        if key not in self.cache:
            if len(self.cache) > 20000:
                self.cache.clear()
            mm = self.mat(pid, pose)
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def point(self, pid, pose, p):
        q = _apply(self.mat(pid, pose), [v / 16 for v in p])
        return [v * 16 for v in q]

    def named(self, pid, rx, pose=None):
        src = self.posed(pid, pose) if pose is not None else self.by_part.get(pid, [])
        r = re.compile(rx)
        return [e for e in src if r.search(e.name)]


# Parts that belong to one master's set-up only; the other's are never fitted at the same time.
SMALL = re.compile(r"^(master|blanksmall|gs\d\d)$")
LARGE = re.compile(r"^(masterlarge|blanklarge|gl\d\d)$")


def present(pid, k):
    if SMALL.match(pid):
        return k == 1
    if LARGE.match(pid):
        return k == 2
    return True


def check_basic(v):
    m = v.m
    worst = euler_round_trip(v.els)
    print(f"euler round trip: worst {worst:.1e}")
    if worst > 1e-9:
        v.fail("an element's rotation does not survive being written as Euler angles")
    names = [el.name for el in v.els]
    dup = sorted({n for n in names if names.count(n) > 1})
    if dup:
        v.fail(f"duplicate element names: {dup[:5]}")
    wrong = [(el.name, el.part, part_of(v.parts, el.name)) for el in v.els if part_of(v.parts, el.name) != el.part]
    if wrong:
        v.fail(f"{len(wrong)} elements land in the wrong part, e.g. {wrong[:4]}")
    empty = [p["id"] for p in v.parts if not v.by_part.get(p["id"])]
    if empty:
        v.fail(f"parts with no elements: {empty}")
    counts = {p["id"]: len(v.by_part.get(p["id"], [])) for p in v.parts}
    print(f"parts: {len(v.parts)}, elements {len(v.els)}: " + ", ".join(f"{k} {n}" for k, n in counts.items() if not re.match(r"^g[sl]\d\d$", k))
          + f", gap fills {sum(n for k, n in counts.items() if re.match(r'^g[sl]', k))}")


def check_containment(v, poses):
    """Every element at rest in the machine box; nothing leaves it over the poses."""
    m = v.m
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where = -1e9, None
    for pose in poses:
        k = pose[3]
        for pid in v.by_part:
            if not present(pid, k):
                continue
            for el in v.posed(pid, pose):
                lo, hi = el.aabb()
                o = box_overhang(lo, hi, box)
                if o > worst:
                    worst, where = o, (el.name, pose)
    print(f"containment over {len(poses)} poses: worst overhang {worst:.3f} voxels ({where[0]})")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the machine box at {where[1]}")


def check_floating(v):
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        k = pose[3]
        els = [e for pid in v.by_part if present(pid, k) for e in v.posed(pid, pose)]
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses")
    if bad:
        v.fail("faces z-fight")


# The work and the cutter: the parts whose faces are stacked close (a blank's body, teeth and fills, the
# masters drawn the same way, the cutter's body and bands).
WORK = re.compile(r"^(blanksmall|blanklarge|g[sl]\d\d|master|masterlarge|cutter)$")


def _inside(el, p):
    d = [p[i] - el.c[i] for i in range(3)]
    return all(abs(sum(el.r[i][a] * d[i] for i in range(3))) < abs(el.size[a]) / 2 - 1e-6 for a in range(3))


def close_faces(v, pose, gap):
    """Every pair of drawn faces, at least one of them the work's or the cutter's, facing the same way, less
    than `gap` apart and overlapping where both can be seen: z-fighting the exact-plane check misses (the depth
    buffer cannot part faces a hundredth of a voxel apart at a few blocks). A point of a face is out of sight
    when another element holds the point `gap` in front of it: whatever is drawn there is that far in front."""
    k = pose[3]
    els = [e for pid in v.by_part if present(pid, k) for e in v.posed(pid, pose)]
    by_name = {e.name: e for e in els}
    grid = {}
    for e in els:
        lo, hi = e.aabb()
        for cx in range(int(lo[0] // 2), int(hi[0] // 2) + 1):
            for cy in range(int(lo[1] // 2), int(hi[1] // 2) + 1):
                for cz in range(int(lo[2] // 2), int(hi[2] // 2) + 1):
                    grid.setdefault((cx, cy, cz), []).append(e)

    def hidden(p, n):
        q = [p[i] + (gap - 0.001) * n[i] for i in range(3)]    # a face exactly a gap behind another is hidden
        return any(_inside(e, q) for e in grid.get(tuple(int(q[i] // 2) for i in range(3)), ()))

    def quad(el, d):
        return next((nn, qq) for dd, nn, qq in drawn_faces(el) if dd == d)

    out = []
    for na, da, nb, db, _area in coplanar_faces(els, eps=gap - 1e-6):
        a, b = by_name[na], by_name[nb]
        if not (WORK.match(a.part) or WORK.match(b.part)):
            continue
        n, qa = quad(a, da)
        _, qb = quad(b, db)
        e1 = [qb[1][i] - qb[0][i] for i in range(3)]
        e2 = [qb[3][i] - qb[0][i] for i in range(3)]
        l1, l2 = sum(x * x for x in e1), sum(x * x for x in e2)
        seen = False
        for si in range(10):
            for ti in range(10):
                s_, t_ = (si + 0.5) / 10, (ti + 0.5) / 10
                p = [qa[0][i] + s_ * (qa[1][i] - qa[0][i]) + t_ * (qa[3][i] - qa[0][i]) for i in range(3)]
                d0 = [p[i] - qb[0][i] for i in range(3)]
                u_ = sum(d0[i] * e1[i] for i in range(3)) / l1
                w_ = sum(d0[i] * e2[i] for i in range(3)) / l2
                if not (0.0 < u_ < 1.0 and 0.0 < w_ < 1.0):
                    continue
                off = sum(d0[i] * n[i] for i in range(3))
                pb = [p[i] - off * n[i] for i in range(3)]
                if not hidden(p, n) and not hidden(pb, n):
                    seen = True
                    break
            if seen:
                break
        if seen:
            sep = abs(sum((qa[0][i] - qb[0][i]) * n[i] for i in range(3)))
            out.append((na, da, nb, db, round(sep, 4)))
    return out


def check_close_faces(v):
    """No two faces of the work or the cutter z-fight: at rest (every fill up), mid-gear (some sunk, one
    sinking) and at the end of a gear (all sunk), for both masters, every pair of same-facing faces that
    overlap where both can be seen is at least the generator's ZF_GAP apart."""
    m = v.m
    poses = [m.pose_at(1, T) for T in (0.0, 4.25, 11.99)] + [m.pose_at(2, T) for T in (0.0, 7.25, 19.99)]
    found = {}
    for pose in poses:
        for f in close_faces(v, pose, m.ZF_GAP):
            found.setdefault(f[:4], (f[4], pose))
    print(f"close faces: {len(found)} visible pairs of the work's and the cutter's faces closer than {m.ZF_GAP} over "
          f"{len(poses)} poses")
    for (na, da, nb, db), (sep, pose) in sorted(found.items())[:12]:
        print(f"  CLOSE {na}.{da} / {nb}.{db}: {sep} apart at T {pose[2]} k {pose[3]}")
    if found:
        v.fail("faces of the work or the cutter z-fight")
    return found


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels (machinegen's eps) at every sampled pose.
ALLOWED = [
    # shafts in their bearings and bosses, studs and pins in their eyes
    ("entry", r"_shaft", "frame", r"fr_(entry_bearing|col_back|col_front)"),
    ("feedshaft", None, "frame", r"fr_(feed_bearing|col_front|case_e)"),
    ("camshaft", r"cam_shaft", "frame", r"fr_(cam_bearing|case_s)"),
    ("headshaft", r"_rod", "frame", r"fr_col_(back|front|boss)"),
    ("spindle", r"_rod", "frame", r"fr_spindle_bearing"),
    ("idler", None, "frame", r"fr_idler_stud"),
    ("clutchlever", None, "frame", r"fr_clutch_pin"),
    ("arbor", r"_rod", "table", r"table_(bearing|upright|boss)"),
    ("jring", r"jring_back", "table", r"table_boss"),
    ("checkpawl", None, "table", r"table_check(post|arm)"),
    ("detent", None, "table", r"table_detentpost"),
    ("screw", r"_rod", "knee", r"knee_web_m"),
    # loose wheels and fittings on their shafts
    ("rectb1", None, "feedshaft", None), ("rectb2", None, "feedshaft", None), ("worm", None, "feedshaft", None),
    ("clutch", None, "feedshaft", None), ("sun", r"sun_shaft", "arbor", None), ("jcarrier", None, "arbor", None),
    ("jplanet\\d", None, "jcarrier", r"_pin"), ("sun", r"sun_shaft", "jring", r"jring_eye"), ("sun", None, "jcarrier", r"_hub"),
    ("lever", r"_hub", "sun", r"sun_shaft"), ("shield", r"_hub", "sun", r"sun_shaft"),
    ("pawl", r"_pin", "lever", None), ("cutter", None, "spindle", None),
    ("master", None, "arbor", None), ("masterlarge", None, "arbor", None), ("blanksmall", None, "arbor", None), ("blanklarge", None, "arbor", None),
    ("g[sl]\\d\\d", None, "blank(small|large)", None),
    # meshing wheels (their pitch circles are checked by check_gearing)
    ("entry", r"entry_a1", "rectb1", None), ("entry", r"entry_a2", "idler", None), ("idler", None, "rectb2", None),
    ("worm", None, "camshaft", r"cam_wheel"), ("headshaft", r"_bevel", "spindle", r"_bevel"),
    ("jplanet\\d", None, "sun", r"sun_gear"), ("jplanet\\d", None, "jring", r"jring_int"),
    ("master", r"_tooth", "frame", r"fr_rack_tooth"), ("masterlarge", r"_tooth", "frame", r"fr_rack_tooth"),
    # cams and their followers, slides in their guides
    ("camfeed", r"cam_fgroove|cam_fdrum", "feedslider", r"_pin"), ("camindex", r"cam_igroove|cam_idrum", "indexslider", r"_pin"),
    ("liftcam", r"cam_lift", "screw", r"screw_foot"),
    # the drums and the lift cam are keyed on the camshaft
    ("cam(feed|index)|liftcam", None, "camshaft", r"cam_shaft"),
    ("feedslider", None, "frame", r"fr_guide"), ("indexslider", None, "frame", r"fr_guide"),
    ("feedslider", r"_post", "table", r"table_guide|table_ear"),
    ("pusher", r"_pad", "lever", r"_roller"), ("pusher", None, "indexslider", r"_post"),
    ("knee", r"knee_back", "frame", r"fr_gib"), ("knee", r"knee_way", "table", r"table_plate"),
    ("clutch", r"_dog", "worm", r"_dog"), ("clutchlever", r"_fork", "clutch", r"_groove|_sleeve"),
    # the index: pawls on the housing's rim teeth, the pawl on the shield, the lever on its stop, the detent in the sun's notch
    ("pawl", None, "jring", r"jring_rim\d"), ("checkpawl", None, "jring", r"jring_rim\d"), ("pawl", None, "shield", None),
    ("lever", r"_tail", "table", r"table_stop"), ("detent", None, "sun", r"sun_detentnotch"),
    # the oil in its cup
    ("oillevel", None, "frame", r"fr_oiler_(base|glass)"),
    # the injection valve on the oiler's bracket (on the cross-head), fed from the reservoir's base
    ("valve", r"valve_(body|boss)", "frame", r"fr_oiler_bracket"), ("valve", r"valve_nozzle$", "frame", r"fr_oiler_bracket"),
    ("valve", r"valve_feed", "frame", r"fr_oiler_base"), ("valveplunger", r"_stem", "valve", r"valve_body"),
    # the belt on its pulleys
    ("belt", None, "entry", r"_cone1"), ("belt", None, "headshaft", r"_cone1"),
    # the cutter takes the gap it is cutting; sunk fills lie in the blank's body, next to each other
    ("cutter", None, "g[sl]\\d\\d", None), ("g[sl]\\d\\d", None, "g[sl]\\d\\d", None),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose):
    """Every pair of elements of different parts that overlap at `pose`, apart from the intended contacts."""
    k = pose[3]
    items = []
    for pid in v.by_part:
        if not present(pid, k):
            continue
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
                if pa == pb:
                    continue
                if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                    continue
                if allowed(pa, ea.name, pb, eb.name):
                    continue
                if obb_obb(ea, eb):
                    hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def check_clearances(v, poses):
    allhits = {}
    for pose in poses:
        for key, hs in touching_pairs(v, pose).items():
            allhits.setdefault(key, {})
            for h in hs:
                allhits[key].setdefault(h, pose)
    print(f"clearances over {len(poses)} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at T {p[2]} k {p[3]}" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


def clearance_poses(m):
    out = [m.REST]
    for k in (1, 2):
        # the small master's cut ends at 12 teeth (the viewer and the game stop T there)
        for T in (0.0, 0.03, 0.06, 0.12, 0.2, 0.3, 0.37, 0.39, 0.5, 0.6, 0.72, 0.75, 0.78, 0.8, 0.83, 0.86, 0.89, 0.92, 0.95, 0.98, 1.0,
                  5.21, 9.47, 11.5) + ((14.33, 17.5, 19.6) if k == 2 else ()):
            out.append(m.pose_at(k, T))
    return out


# ---------------------------------------------------------------- mechanism
def disp(v, pid, p, ps1, ps2):
    """How far the material point of part pid at p (voxels, at pose ps1) moves going to ps2."""
    m1, m2 = v.mat(pid, ps1), v.mat(pid, ps2)
    r = [row[:3] for row in m1[:3]]
    t = [m1[i][3] * 16 for i in range(3)]
    q = [p[i] - t[i] for i in range(3)]
    x = [sum(r[j][i] * q[j] for j in range(3)) for i in range(3)]
    return [sum(m2[i][j] * x[j] for j in range(3)) + m2[i][3] * 16 - p[i] for i in range(3)]


def norm(a):
    return math.sqrt(sum(x * x for x in a))


def angle_of(v, pid, pose, axis="x"):
    """A part's turn about +axis at a pose (radians, from its matrix)."""
    mm = v.mat(pid, pose)
    if axis == "x":
        return math.atan2(mm[2][1], mm[1][1])
    if axis == "z":
        return math.atan2(mm[1][0], mm[0][0])
    return math.atan2(mm[0][2], mm[0][0])


def check_gearing(v):
    """Every meshing pair: centre distance equals the sum of the pitch radii (internal: the difference),
    and the contact point moves the same way at the same speed on both wheels (finite differences of
    the posed rig), for the axle turning either way; the rectifier; the worm; the planetary at the
    index and in the roll; the master rolling on the rack without slip, and the blank under the cutter
    likewise (the generating condition)."""
    m = v.m
    d = 1e-3
    worst_t, worst_v = 0.0, 0.0
    lines = []

    def mesh(label, pa, pb, ca, cb, ra, rb, axis, ps1, ps2, external=True, ref=0.0):
        nonlocal worst_t, worst_v
        u, w = [k for k in range(3) if k != axis]
        dist = math.hypot(ca[u] - cb[u], ca[w] - cb[w])
        tang = abs(dist - (ra + rb if external else abs(ra - rb)))
        p = list(ca)
        sgn = 1 if external else (1 if rb < ra else -1)
        f = ra / dist if external else ra / dist
        p[u] = ca[u] + (cb[u] - ca[u]) * f * (1 if external or rb > ra else -1)
        p[w] = ca[w] + (cb[w] - ca[w]) * f * (1 if external or rb > ra else -1)
        da, db = disp(v, pa, p, ps1, ps2), disp(v, pb, p, ps1, ps2)
        mag = max(norm(da), norm(db), ref, 1e-12)
        err = norm([da[i] - db[i] for i in range(3)]) / mag
        worst_t, worst_v = max(worst_t, tang), max(worst_v, err)
        lines.append(f"{label}: centre distance off by {tang:.4f}, contact speeds differ by {100 * err:.2f} %")
        if tang > 0.01 or err > 0.01:
            v.fail(f"{label} does not mesh (distance off {tang:.4f}, speeds {100 * err:.2f} %)")

    for th in (0.4, -0.4):
        s = "+" if th > 0 else "-"
        p1, p2 = (th, abs(th), 0.0, 0, 0.0), (th + math.copysign(d, th), abs(th) + d, 0.0, 0, 0.0)
        xa, xb = sum(m.RECT_X[0]) / 2, sum(m.RECT_X[1]) / 2
        mesh(f"A1 / B1 ({s})", "entry", "rectb1", [xa, *m.ENTRY], [xa, *m.FEED], m.RECT_R[0], m.RECT_R[0], 0, p1, p2)
        mesh(f"A2 / idler ({s})", "entry", "idler", [xb, *m.ENTRY], [xb, *m.IDLER], m.RECT_R[1], m.RECT_R[1], 0, p1, p2)
        mesh(f"idler / B2 ({s})", "idler", "rectb2", [xb, *m.IDLER], [xb, *m.FEED], m.RECT_R[1], m.RECT_R[1], 0, p1, p2)
        # the rectifier: exactly one loose gear turns with the feed shaft, forward
        fs = m.TAU * 0 + 1.0
        rates = {}
        for pid in ("rectb1", "rectb2", "feedshaft"):
            rates[pid] = (angle_of(v, pid, p2) - angle_of(v, pid, p1)) / d
        bites = [pid for pid in ("rectb1", "rectb2") if abs(rates[pid] - rates["feedshaft"]) < 1e-3]
        lines.append(f"rectifier, axle {s}: feed shaft {rates['feedshaft']:+.4f} per radian of travel, B1 {rates['rectb1']:+.4f}, "
                     f"B2 {rates['rectb2']:+.4f}: {bites[0] if bites else 'NONE'} drives it")
        if len(bites) != 1 or rates["feedshaft"] <= 0:
            v.fail("the rectifier does not turn the feed shaft forward both ways")
        # the mitre bevels: their pitch circles touch at one point, which moves alike on both
        pmeet = [m.X_BLANK - m.MITRE_R, m.SPINDLE_Y, m.HEAD_Z - m.MITRE_R]
        dh, ds = disp(v, "headshaft", pmeet, p1, p2), disp(v, "spindle", pmeet, p1, p2)
        err = norm([dh[i] - ds[i] for i in range(3)]) / max(norm(dh), 1e-12)
        lines.append(f"head shaft / spindle mitre ({s}): contact speeds differ by {100 * err:.2f} %")
        if err > 0.01:
            v.fail("the mitre bevels do not mesh")
        # the belt: both pulleys' rims move at the belt's speed
        ve = abs(rates.get("entry", 0.0))
        re_ = (angle_of(v, "entry", p2) - angle_of(v, "entry", p1)) / d * m.CONE_LOW[0]
        rh = (angle_of(v, "headshaft", p2) - angle_of(v, "headshaft", p1)) / d * m.CONE_HIGH[0]
        if abs(re_ - rh) > 1e-3:
            v.fail(f"the belt slips: rim speeds {re_:.4f} and {rh:.4f}")
    lines.append(f"cone and belt: step 1, {m.CONE_LOW[0]} to {m.CONE_HIGH[0]}: the head shaft and cutter turn {m.CONE_LOW[0] / m.CONE_HIGH[0]:.4f} per axle turn")
    # the worm and wheel, through the clutch: the wheel's top moves with the thread, the feed's pace
    T0 = 0.3
    p1, p2 = m.pose_at(1, T0), m.pose_at(1, T0 + d)
    top = [m.CAM_X, m.CAM_Y + m.WHEEL_R, m.FEED[1]]
    dw = disp(v, "camshaft", top, p1, p2)
    worm_turn = angle_of(v, "worm", p2) - angle_of(v, "worm", p1)
    adv = m.WORM_LEAD * worm_turn / m.TAU
    err = abs(-dw[0] - adv) / abs(adv)
    dist = abs((m.FEED[0] - m.CAM_Y) - (m.WHEEL_R + m.WORM_R))
    cam_per_axle = (angle_of(v, "camshaft", p2, "z") - angle_of(v, "camshaft", p1, "z")) / (p2[0] - p1[0])
    lines.append(f"worm / wheel: centre distance off by {dist:.4f}; lead {m.WORM_LEAD:.4f} = the wheel's circular pitch "
                 f"{m.TAU * m.WHEEL_R / m.WORM_TEETH:.4f}; thread and wheel advance differ by {100 * err:.2f} %; the camshaft turns "
                 f"{cam_per_axle * m.TAU / m.TAU:.5f} per axle radian: {1 / cam_per_axle:.2f} axle turns a tooth")
    if err > 0.01 or dist > 0.01 or abs(1 / cam_per_axle - m.TURNS_PER_TOOTH) > 1e-3:
        v.fail("the worm and wheel do not mesh, or the pace is not turnsPerTooth")
    clutch_w = (angle_of(v, "clutch", p2) - angle_of(v, "clutch", p1)) / (p2[1] - p1[1])
    worm_w = worm_turn / (p2[1] - p1[1])
    lines.append(f"clutch: the sleeve turns {clutch_w:.4f} and the worm {worm_w:.4f} per radian of the axle's travel while cutting")
    if abs(clutch_w - worm_w) > 1e-3:
        v.fail("the clutch's two halves turn at different rates while engaged")
    # the planetary, in the index and in the roll, both masters
    for k, cls in ((1, "thin"), (2, "thick")):
        for T0, what in ((0.86 if k == 1 else 0.88, "index"), (0.25, "roll"), (0.55, "roll back")):
            q1, q2 = m.pose_at(k, T0), m.pose_at(k, T0 + 1e-5)   # small: the planetary turns fast per tooth, and a chord is not an arc
            for i, ang in enumerate(m.PLANET_ANG, 1):
                pc0 = m.ring_point(m.ARBOR, m.J_CARRIER, ang)
                ax = v.point("sun", q1, [26.4, m.ARBOR_Y, m.Z_REST])
                pc = v.point(f"jplanet{i}", q1, [26.4, pc0[1], pc0[2]])
                # at the index the sun is held, so both contact speeds are ~0: compare to the planet's pin's speed
                pin_speed = norm(disp(v, "jcarrier", pc, q1, q2))
                mesh(f"{cls} {what}: sun / planet {i}", "sun", f"jplanet{i}", ax, pc, m.J_R["sun"], m.J_R["planet"], 0, q1, q2, ref=pin_speed)
                # the ring's contact point: on the far side of the planet from the axis
                pr = [26.4, ax[1] + (pc[1] - ax[1]) * m.J_R["ring"] / m.J_CARRIER, ax[2] + (pc[2] - ax[2]) * m.J_R["ring"] / m.J_CARRIER]
                dp_, dr = disp(v, f"jplanet{i}", pr, q1, q2), disp(v, "jring", pr, q1, q2)
                # at the index the ring is held, so both speeds are ~0: compare to the sun's contact speed
                ref = norm(disp(v, "sun", [ax[0], ax[1] + (pc[1] - ax[1]) * m.J_R["sun"] / m.J_CARRIER, ax[2] + (pc[2] - ax[2]) * m.J_R["sun"] / m.J_CARRIER], q1, q2))
                err = norm([dp_[j] - dr[j] for j in range(3)]) / max(norm(dp_), norm(dr), ref, 1e-12)
                lines.append(f"{cls} {what}: planet {i} / ring: contact speeds differ by {100 * err:.2f} %")
                if err > 0.01:
                    v.fail(f"planet {i} and the ring do not mesh ({cls} {what})")
        # the master rolls on the rack and the blank under the cutter: their top pitch points stand still
        for T0 in (0.2, 0.3, 0.5, 0.6):
            q1, q2 = m.pose_at(k, T0), m.pose_at(k, T0 + d)
            for pid, x in (("arbor", m.X_MASTER), ("arbor", m.X_BLANK)):
                arbor_z = m.Z_REST + v.mat("table", q1)[2][3] * 16
                arbor_y = m.ARBOR_Y + v.mat("table", q1)[1][3] * 16
                ptop = [x, arbor_y + m.PITCH_R[cls], arbor_z]
                dd = disp(v, pid, ptop, q1, q2)
                dz = v.mat("table", q2)[2][3] * 16 - v.mat("table", q1)[2][3] * 16
                slip = abs(dd[2]) / max(abs(dz), 1e-12)
                worst_v = max(worst_v, slip)
                if slip > 0.01:
                    v.fail(f"{cls}: the pitch point at x {x} slips {100 * slip:.2f} % at T {T0}")
        lines.append(f"{cls}: the master's and the blank's top pitch points stand still as the table moves (no slip on the rack, "
                     f"the blank generated under the cutter); a pass rolls {math.degrees(m.STROKE / m.PITCH_R[cls]):.1f} degrees")
    for ln in lines:
        print(" ", ln)
    print(f"gearing: worst centre-distance error {worst_t:.4f}, worst contact speed mismatch {100 * worst_v:.2f} %")


def overlap_depth(a, b):
    """How far two boxes run into each other: the shrink at which obb_obb stops finding an overlap, twice."""
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


# Meshing teeth are boxes, so they run into each other a little where a real tooth form would not;
# each pair's worst over a tooth's cycle is held to its tolerance (voxels).
MESH_LIMITS = [
    ("master", r"_tooth", "frame", r"fr_rack_tooth", 1, 0.1),
    ("masterlarge", r"_tooth", "frame", r"fr_rack_tooth", 2, 0.1),
    ("cutter", None, "blanksmall", r"_tooth", 1, 0.0),
    ("cutter", None, "blanklarge", r"_tooth", 2, 0.0),
    ("entry", r"entry_a1_tooth", "rectb1", r"_tooth", 1, 0.2),
    ("entry", r"entry_a2_tooth", "idler", r"_tooth", 1, 0.2),
    ("idler", r"_tooth", "rectb2", r"_tooth", 1, 0.2),
    ("worm", r"_thread", "camshaft", r"cam_wheel_tooth", 1, 0.3),
    ("headshaft", r"_bevel_tooth", "spindle", r"_bevel_tooth", 1, 0.3),
    ("jplanet1", r"_tooth", "sun", r"sun_gear_tooth", 1, 0.3),
    ("jplanet1", r"_tooth", "jring", r"jring_int", 1, 0.3),
]


def check_mesh_depths(v, steps=50):
    m = v.m
    for pa, ra, pb, rb, k, limit in MESH_LIMITS:
        worst, where = 0.0, None
        for i in range(steps):
            pose = m.pose_at(k, 3 + i / steps, theta=0.37 + 2.1 * i / steps)
            A = v.named(pa, ra or ".", pose)
            Bs = v.named(pb, rb or ".", pose)
            for a in A:
                alo, ahi = a.aabb()
                for b in Bs:
                    blo, bhi = b.aabb()
                    if all(alo[q] < bhi[q] and blo[q] < ahi[q] for q in range(3)):
                        dd = overlap_depth(a, b)
                        if dd > worst:
                            worst, where = dd, (a.name, b.name, round(i / steps, 3))
        print(f"teeth: {pa} into {pb}, worst {worst:.3f} (limit {limit}){'' if where is None else f' {where}'}")
        if worst > limit + 1e-9:
            v.fail(f"{pa}'s teeth run {worst:.3f} into {pb}'s")


def check_index(v):
    """The count: after every whole tooth the arbor is exactly n steps on, the housing (the ring) 1.5n
    steps, the sun back where it started (it turns only in the roll, and rolls back) and the planets'
    own turn 2n steps; so at the end of a gear (T = N) the arbor and planets stand where they started
    and the housing is half a turn on, which it looks the same at (every one of its patterns repeats
    in 180 degrees), and a new blank at T = 0 starts without a visible jump."""
    m = v.m
    worst = 0.0
    sym = math.pi                                       # the housing's patterns: 6 spokes, 8 rivets, 16-gons, 20 and 40 teeth
    for n_ in (6, 8, 16, m.J_TEETH["ring"], m.RIM_TEETH):
        assert abs(math.remainder(sym, m.TAU / n_)) < 1e-12
    for k, cls in ((1, "thin"), (2, "thick")):
        n = m.TEETH[cls]
        for j in (0, 1, 5, n // 2, n - 1, n):
            pose = m.pose_at(k, float(j))
            want = -j * m.STEP[cls]
            got = angle_of(v, "arbor", pose)
            e = abs(math.remainder(got - want, m.TAU))
            e2 = abs(math.remainder(angle_of(v, "sun", pose), m.TAU))
            e3 = abs(math.remainder(angle_of(v, "jring", pose) - m.J_RING_PER_ARBOR * want, m.TAU))
            worst = max(worst, e, e2, e3)
            if max(e, e2, e3) > 1e-6:
                print(f"  index {cls} after {j} teeth: arbor off {math.degrees(e):.4f}, sun {math.degrees(e2):.4f}, ring {math.degrees(e3):.4f} degrees")
        end = m.pose_at(k, float(n))
        e = abs(math.remainder(angle_of(v, "arbor", end) - angle_of(v, "arbor", m.REST), m.TAU))
        e4 = abs(math.remainder(angle_of(v, "jring", end), sym))
        worst = max(worst, e, e4)
    print(f"index: the arbor, the housing and the sun land on their steps after every tooth, worst {math.degrees(worst):.2e} degrees; "
          f"at the end of a gear the arbor stands as at the start and the housing half a turn on, which looks the same")
    if worst > 1e-4:
        v.fail("the index does not land on whole teeth")


def obb_point_dist(el, p):
    """The distance from point p to box el (0 inside)."""
    q = [p[i] - el.c[i] for i in range(3)]
    loc = [sum(el.r[i][k] * q[i] for i in range(3)) for k in range(3)]
    out = [max(0.0, abs(loc[k]) - abs(el.size[k]) / 2) for k in range(3)]
    return norm(out)


def check_cams(v):
    """The followers: each slider's pin in its drum's groove at every 1/100 of a tooth; the screw's
    roller on the lift cam; the pusher on the lever's roller through the push and clear of it
    otherwise; the pawl at a tooth's face through the push; the detent in its notch at the index and
    out of the ring's way while the ring turns."""
    m = v.m
    worst_g, worst_l, worst_p, worst_slack = 0.0, 0.0, 0.0, 1e9
    for k in (1, 2):
        for i in range(100):
            t = i / 100
            pose = m.pose_at(k, 3 + t)
            for tag, grv in (("feedslider", "cam_fgroove"), ("indexslider", "cam_igroove")):
                pin = v.named(tag, r"_pin", pose)[0]
                zs = {}
                for e in v.named("camindex" if grv == "cam_igroove" else "camfeed", grv, pose):
                    if e.c[1] < m.CAM_Y + m.DRUM_R:
                        continue
                    e1 = [e.r[i][0] for i in range(3)]
                    if abs(e1[0]) < 1e-6:
                        continue
                    s = (m.CAM_X - e.c[0]) / e1[0]          # along the rail's axis to the drum's top (x = the camshaft's)
                    if abs(s) <= abs(e.size[0]) / 2 + 1e-6:
                        zs.setdefault(e.name[len(grv)], []).append(e.c[2] + s * e1[2])
                if "a" not in zs or "b" not in zs:
                    v.fail(f"no rails over the {tag}'s drum at t {t}")
                    continue
                za = min(zs["a"], key=lambda z: abs(z - pin.c[2]))
                zb = min(zs["b"], key=lambda z: abs(z - pin.c[2]))
                mid = (za + zb) / 2
                worst_g = max(worst_g, abs(mid - pin.c[2]))
                # the rails' inner faces against the pin's sides (rails 0.4 thick)
                slack = min(pin.c[2] - pin.size[2] / 2 - (za + 0.2), (zb - 0.2) - (pin.c[2] + pin.size[2] / 2))
                worst_slack = min(worst_slack, slack)
                if slack < -0.02:
                    v.fail(f"{tag}'s pin runs into its groove's rail at t {t} (by {-slack:.2f})")
            # the screw's roller on the lift cam
            roller = v.named("screw", r"screw_foot", pose)
            rc = [m.CAM_X, sum(e.c[1] for e in roller) / len(roller), sum(e.c[2] for e in roller) / len(roller)]
            gap = min(obb_point_dist(e, rc) for e in v.named("liftcam", r"cam_lift\d", pose)) - m.FOOT_R


            worst_l = max(worst_l, abs(gap))
            if gap < -0.2 or gap > 0.2:
                v.fail(f"the screw's roller is {gap:+.2f} off the lift cam at t {t}")
            # the pusher and the lever's roller
            pad = v.named("pusher", r"_pad", pose)[0]
            rl = v.named("lever", r"_roller", pose)
            rcz = sum(e.c[2] for e in rl) / len(rl)
            face = pad.c[2] + pad.size[2] / 2
            g = (rcz - m.ROLLER_R) - face
            if m.T_LEVER[0] + 0.002 <= t <= m.T_LEVER[2] - 0.002:
                # pushed, and on the way back held to the pusher by the lever's spring
                worst_p = max(worst_p, abs(g))
                if abs(g) > 0.15:
                    v.fail(f"the pusher is {g:+.2f} off the lever's roller in the push at t {t}")

            elif g < -0.02 or (g < 0.15 and not (m.T_GAP[0] < t < m.T_GAP[3])):
                v.fail(f"the pusher touches the lever's roller outside the push at t {t} (gap {g:.2f})")
    print(f"cams: pins within {worst_g:.3f} of their grooves' centres, never closer than {worst_slack:.3f} to a rail; the screw's roller on the lift cam within {worst_l:.3f}; "

          f"the pusher on the lever's roller within {worst_p:.3f} through the push")
    # the pawl: at rest LOST short of a rim face; through the push at the face; the housing's step per index
    for k, cls in ((1, "thin"), (2, "thick")):
        rest = m.pose_at(k, 3.0)
        push = m.pose_at(k, 3.0 + (m.T_STEP[cls][0] + m.T_STEP[cls][1]) / 2)
        for label, pose in (("rest", rest), ("mid-push", push)):
            tip = m.ring_point(m.ARBOR, m.PAWL_TIP_R, m.PAWL_TIP_ANG)
            tip = v.point("pawl", pose, [27.75, tip[1], tip[2]])
            ring_ang = angle_of(v, "jring", pose)
            yc, zc = m.ARBOR_Y + v.mat("table", pose)[1][3] * 16, m.Z_REST + v.mat("table", pose)[2][3] * 16
            tip_ang = math.atan2(tip[2] - zc, tip[1] - yc)
            rel = math.degrees(math.remainder(tip_ang - ring_ang - m.RIM_FACE0, m.TAU / m.RIM_TEETH))
            r = math.hypot(tip[2] - zc, tip[1] - yc)
            print(f"  pawl, {cls} {label}: tip {rel:+.2f} degrees from a rim face, at radius {r:.2f} (root {m.RIM_R[0]}, tip {m.RIM_R[1]})")
            want = math.degrees(m.LOST) if label == "rest" else 0.0
            if abs(math.remainder(rel - want, 360 / m.RIM_TEETH)) > 1.0 or not (m.RIM_R[0] - 0.05 <= r <= m.RIM_R[1]):
                v.fail(f"the pawl is not where it should be at {cls} {label}")
        step = angle_of(v, "jring", m.pose_at(k, 4.0)) - angle_of(v, "jring", m.pose_at(k, 3.0))
        teeth_ = abs(math.degrees(math.remainder(step, m.TAU))) / (360 / m.RIM_TEETH)
        print(f"  rim ratchet, {cls}: {teeth_:.3f} teeth per index (the shield {'stowed' if k == 1 else 'in'}); "
              f"the arbor {math.degrees(m.STEP[cls]):.0f} degrees")
        if abs(teeth_ - m.PUSH[cls] / (m.TAU / m.RIM_TEETH)) > 1e-3 or abs(teeth_ - round(teeth_)) > 1e-3:
            v.fail(f"the housing does not step whole rim teeth for the {cls} master")
        # the shield: swung in for the large master, the pawl rides it until it drops onto a face
        if k == 2:
            early = m.pose_at(k, 3.0 + m.t_at_lever(m.LEVER_SWING - m.PUSH[cls] - 6.0 * m.DEG))
            tip = v.point("pawl", early, [28.55, *m.ring_point(m.ARBOR, m.PAWL_TIP_R, m.PAWL_TIP_ANG)[1:]])
            yc, zc = m.ARBOR_Y + v.mat("table", early)[1][3] * 16, m.Z_REST + v.mat("table", early)[2][3] * 16
            r = math.hypot(tip[2] - zc, tip[1] - yc)
            print(f"  pawl, thick, on the shield before it drops: tip at radius {r:.2f} (the rim's tips {m.RIM_R[1]})")
            if r < m.RIM_R[1] - 0.05:
                v.fail("with the shield in, the pawl is down among the rim's teeth before its push")
    # the detent: plunger tip in the sun's notch while the sun is held, out past the disc while it turns
    worst_d = 1e9
    for k in (1, 2):
        for t in (0.0, 0.05, 0.095, 0.1, 0.105, 0.11, 0.2, 0.39, 0.6, 0.67, 0.675, 0.68, 0.7, 0.82, 0.86, 0.9):
            pose = m.pose_at(k, 7 + t)
            plunger = v.named("detent", r"_plunger", pose)[0]
            disc = v.named("sun", r"sun_detent", pose)
            for e in disc:
                if obb_obb(plunger, e):
                    v.fail(f"the detent's plunger runs into the sun's disc ({e.name}) at k {k} t {t}")
            if m.T_DETENT[0] < t < m.T_DETENT[3]:
                continue
            tip_z = plunger.c[2] - plunger.size[2] / 2 - (m.Z_REST + v.mat("table", pose)[2][3] * 16)
            worst_d = min(worst_d, m.DETENT_R - tip_z)
            if tip_z > m.DETENT_R - 0.15:
                v.fail(f"the detent is not in the sun's notch at k {k} t {t} (tip {tip_z:.2f} from the axis)")
    print(f"detent: the plunger never runs into the sun's disc; it sits {worst_d:.2f} into the notch while the housing turns")


def check_supports(v):
    """Every shaft in at least two bearings (or one and a pilot), each bearing enclosing it at rest
    and turned 45 degrees."""
    m = v.m
    frame = v.by_part["frame"]
    table = v.by_part["table"]
    t45 = (math.pi / 4, math.pi / 4, 3.0, 1, 1.0)      # whole teeth: the table at rest, the arbor turned three steps
    shafts = [
        ("entry shaft", "entry", r"_shaft", 0, m.ENTRY, frame, r"^fr_entry_bearing", 2),
        ("feed shaft", "feedshaft", r"_rod", 0, m.FEED, frame, r"^fr_(feed_bearing|case_e)", 2),

        ("head shaft", "headshaft", r"_rod", 0, (m.SPINDLE_Y, m.HEAD_Z), frame, r"^fr_col_(back|front|boss)", 2),
        ("camshaft", "camshaft", r"cam_shaft", 2, (m.CAM_X, m.CAM_Y), frame, r"^fr_cam_bearing", 2),
        ("spindle", "spindle", r"_rod", 2, (m.X_BLANK, m.SPINDLE_Y), frame, r"^fr_spindle_bearing", 2),
        ("arbor", "arbor", r"_rod", 0, (m.ARBOR_Y, m.Z_REST), table, r"^table_bearing", 2),
        ("idler's stud", "frame", r"fr_idler_stud", 0, m.IDLER, frame, r"^fr_idler_(arm|post)", 1),
    ]
    for label, pid, rx, axis, c, carrier, brx, need in shafts:
        els_ = v.named(pid, rx)
        found, _ = shaft_supports(carrier, els_, axis, c)
        bearings = [f for f in carrier if re.search(brx, f.name)]
        named = sorted(set(found) & {b.name for b in bearings})
        worst = 1e9
        for b in bearings:
            if b.name not in named:
                continue
            worst = min(worst, bearing_margin(b, v.named(pid, rx, m.REST) + v.named(pid, rx, t45), axis))
        print(f"support {label}: {len(named)} bearing(s) {', '.join(named) or 'NONE'}; enclosed with {worst:.2f} to spare")
        if len(named) < need:
            v.fail(f"the {label} is not carried by {need} bearing(s)")
        if named and worst < -0.01:
            v.fail(f"a bearing of the {label} does not enclose it")
    # the sun shaft: in the ring's eye (an annulus round it) and piloted in the arbor's end
    sun = v.named("sun", r"sun_shaft")
    lo, hi = aabb_of(sun)
    eye = v.named("jring", r"jring_eye")
    elo, ehi = aabb_of(eye)
    inner = min(math.hypot(*(lambda q: (q[1] - m.ARBOR_Y, q[2] - m.Z_REST))(e.c)) - abs(e.size[1]) / 2 for e in eye)
    arbor = v.named("arbor", r"_rod")
    alo, ahi = aabb_of(arbor)
    pilot = min(hi[0], ahi[0]) - max(lo[0], alo[0])
    print(f"support sun shaft: through the ring's eye (x {elo[0]:.2f}..{ehi[0]:.2f}, bore {inner:.2f} round a shaft of 0.4) and "
          f"piloted {pilot:.2f} into the arbor's end")
    if not (lo[0] < elo[0] and hi[0] > ehi[0]) or inner < 0.4 or pilot < 0.3:
        v.fail("the sun shaft is not carried")



TEX_RULES = [
    (r"^(fr_sill|entry_shaft[ab])", "oak"),
    (r"^(fr_col|fr_overarm|fr_crosshead|fr_bed|knee_(back|floor|girder|web)|table_(plate|upright|ear)|fr_cam_bearing|fr_entry_bearing)", "iron"),
    (r"^(master_|mastl_)(tooth|body|bar|rim|web)", "temporal"),
    (r"^(master_boss|mastl_boss|jring_band|jring_rivet|jcarrier_pin)", "gold"),
    (r"^(jring_(back|drum)|jcarrier_(arm|hub))", "cupronickel"),
    (r"^(blanks_|blankl_|g[sl]\d\d_|cutter_|spindle_rod|arbor_rod|sun_gear|jplanet|jring_rim|pawl_|checkpawl_|worm_thread|cam_wheel|fr_rack_tooth)", "steel"),
    (r"^belt_", "leather"),
    (r"^(valve_|valveplunger_stem)", "cupronickel"),
    (r"^valveplunger_handle", "marble"),

    (r"^fr_oiler_glass", "glass"),
    (r"^fr_oiler_(base|cap|knob|bracket|tube)", "brass"),
    (r"^oillevel_", "oil"),
]


def check_textures(v):
    bad = []
    for el in v.els:
        for rx, tex in TEX_RULES:
            if re.match(rx, el.name):
                got = {f["texture"].lstrip("#") for f in el.faces.values()}
                if got != {tex}:
                    bad.append((el.name, sorted(got), tex))
                break
    print(f"textures: {len(TEX_RULES)} rules (oak for the sills and the axle's continuation, iron castings, steel wearing parts and "
          f"gears, temporal masters, cupronickel and gold Jonas head, leather belt, a glass and brass oiler with oil in it): "
          f"{len(bad)} elements break them")
    if bad:
        v.fail(f"textures by role: {bad[:4]}")


def check_fills(v):
    """Each gap's fill is under the cutter at the middle of its own pass, sinks as the cutter rolls
    through it, and is wholly sunk once the pass is over, for both blanks."""
    m = v.m
    for k, cls, pre in ((1, "thin", "gs"), (2, "thick", "gl")):
        n = m.TEETH[cls]
        worst = 0.0
        for j in (0, 1, n // 3, n - 1):
            mid = m.pose_at(k, j + (m.T_FWD[0] + m.T_FWD[1]) / 2)
            fill = v.named(f"{pre}{j + 1:02d}", r"_hi", mid)[0]
            c = [fill.c[q] for q in range(3)]
            yc = m.ARBOR_Y + v.mat("table", mid)[1][3] * 16
            zc = m.Z_REST + v.mat("table", mid)[2][3] * 16
            ang = math.degrees(math.atan2(c[2] - zc, c[1] - yc))
            worst = max(worst, abs(ang))
            after = m.pose_at(k, j + m.T_FWD[1] + 0.01)
            lo = v.named(f"{pre}{j + 1:02d}", r"_hi", after)[0]
            r_after = math.hypot(lo.c[1] - (m.ARBOR_Y + v.mat("table", after)[1][3] * 16), lo.c[2] - (m.Z_REST + v.mat("table", after)[2][3] * 16))
            if r_after > m.PITCH_R[cls] - m.DED - 0.1:
                v.fail(f"{cls} gap {j + 1}'s fill is not sunk after its pass (radius {r_after:.2f})")
        print(f"fills, {cls}: each tested gap is under the cutter at mid-pass (within {worst:.2f} degrees of the top) and sunk below the root after it")
        if worst > 1.0:
            v.fail(f"{cls}: a gap is not under the cutter at the middle of its pass")


def _arbor_centre(v, pose):
    """The arbor's axis at a pose, (y, z): it moves with the table (which rides the knee) and turns about itself."""
    t = v.mat("table", pose)
    return v.m.ARBOR_Y + t[1][3] * 16, v.m.Z_REST + t[2][3] * 16


def _covers(el, x, y, z):
    """Whether the point lies in the element's box, seen along x (the blank's elements turn about x only)."""
    d = [x - el.c[0], y - el.c[1], z - el.c[2]]
    q = [sum(el.r[i][a] * d[i] for i in range(3)) for a in range(3)]
    return abs(q[1]) <= el.size[1] / 2 and abs(q[2]) <= el.size[2] / 2


def check_blank_faces(v):
    """The blank reads as plain disc until it is cut, and each gap opens cleanly as it is. Mid-gear, with a
    third of the gaps cut, seen along the arbor: every point of the face inside the root, and of an uncut gap's
    sector out to the tip, is covered (no crack to see through), with a fill's face, standing just proud of the
    teeth, in front of every tooth's flank edge; the middle of every cut gap, root to tip, is open (nothing of
    the blank or the fills in it). A sunk fill is wholly inside the body: within its inradius and behind its
    faces, so it neither shows on a face nor pokes out of the rim."""
    m = v.m
    for k, cls, part, pre in ((1, "thin", "blanksmall", "gs"), (2, "thick", "blanklarge", "gl")):
        n, step = m.TEETH[cls], m.STEP[cls]
        root, tip = m.PITCH_R[cls] - m.DED, m.PITCH_R[cls] + m.ADD
        j = n // 3
        pose = m.pose_at(k, j)
        yc, zc = _arbor_centre(v, pose)
        turn = angle_of(v, "arbor", pose)
        mid = (root + tip) / 2 + 0.1                       # where a box tooth steps from its wide part to its narrow one
        half_a = (math.pi * m.MODULE / 2 - (m.UNDERCUT if n < 17 else 0.0)) / 2
        blank = v.posed(part, pose)
        fills = [e for g in range(n) for e in v.posed(f"{pre}{g + 1:02d}", pose)]
        els = blank + fills
        teeth_x0 = min(e.aabb()[0][0] for e in blank if "_tooth" in e.name)
        x = m.X_BLANK
        holes, blocked, front = [], [], []
        for ir in range(1, 160):
            r = tip * ir / 160 - 0.01
            if r < 0.7:
                continue
            for ia in range(720):
                a = math.tau * ia / 720
                y, z = yc + r * math.cos(a), zc + r * math.sin(a)
                rel = (a - turn - m.gap_angle(cls, 0)) / step
                g = round(rel) % n
                off = (rel - round(rel)) * step               # from the gap's centre line, radians
                cover = [e for e in els if _covers(e, x, y, z)]
                if g < j and r > root + 0.05 and abs(r * math.sin(off)) < 0.15:
                    if cover:
                        blocked.append((round(r, 2), g + 1, cover[0].name))
                elif r <= root - 0.1 or g >= j:
                    if not cover:
                        holes.append((round(r, 2), round(math.degrees(a), 1)))
                    elif g >= j and r > root and min(e.aabb()[0][0] for e in cover) > teeth_x0 - 0.005 \
                            and abs(r * math.sin(off)) < (r * math.sin(step / 2) - (half_a if r < mid else 0.225) * math.cos(step / 2)) + 0.03:
                        front.append((round(r, 2), g + 1))        # a tooth's face, not a fill's, in front at the gap or a flank's edge
        sunk = []
        bodies = v.named(part, r"_body", m.REST)
        inr = min(e.size[1] for e in bodies) / 2
        bx0, bx1 = max(e.aabb()[0][0] for e in bodies), min(e.aabb()[1][0] for e in bodies)
        worst_r = 0.0
        for g in (0, j - 1):
            after = m.pose_at(k, g + m.T_FWD[1] + 0.01)
            ay, az = _arbor_centre(v, after)
            for e in v.posed(f"{pre}{g + 1:02d}", after):
                lo, hi = e.aabb()
                rr = max(math.hypot(q[1] - ay, q[2] - az) for q in e.corners())
                worst_r = max(worst_r, rr)
                if rr > inr - 0.005 or lo[0] < bx0 + 0.005 or hi[0] > bx1 - 0.005:
                    sunk.append(e.name)
        print(f"blank faces, {cls}, {j} of {n} gaps cut: {len(holes)} uncovered points of the uncut face, {len(front)} where a tooth "
              f"shows in front of an uncut gap's fill, {len(blocked)} points blocked in the cut gaps; sunk fills reach radius "
              f"{worst_r:.3f} of the body's {inr:.3f}, behind its faces: {'yes' if not sunk else sunk}")
        if holes:
            v.fail(f"{cls}: the uncut blank's face has holes, e.g. {holes[:4]}")
        if front:
            v.fail(f"{cls}: a tooth's outline shows at an uncut gap, e.g. {front[:4]}")
        if blocked:
            v.fail(f"{cls}: a cut gap is not open, e.g. {blocked[:4]}")
        if sunk:
            v.fail(f"{cls}: sunk fills show: {sunk[:4]}")


def check_cutter_fills(v, steps=40):
    """The cutter takes only the fill of the gap it is cutting: through a whole tooth's cycle it never runs
    into another gap's fill, cut or uncut (the fills reach past the tip circle and over the teeth's flanks)."""
    m = v.m
    for k, cls, pre in ((1, "thin", "gs"), (2, "thick", "gl")):
        n = m.TEETH[cls]
        worst, where = 0.0, None
        for j in (0, n // 3, n - 1):
            for i in range(steps + 1):
                pose = m.pose_at(k, j + i / steps * 0.999)
                cut = v.posed("cutter", pose)
                for g in range(n):
                    if g == j:
                        continue
                    for f in v.posed(f"{pre}{g + 1:02d}", pose):
                        flo, fhi = f.aabb()
                        for c in cut:
                            clo, chi = c.aabb()
                            if all(clo[q] < fhi[q] and flo[q] < chi[q] for q in range(3)):
                                d = overlap_depth(c, f)
                                if d > worst:
                                    worst, where = d, (c.name, f.name, round(j + i / steps, 3))
        print(f"cutter: into the fills of the gaps it is not cutting, {cls}, worst {worst:.3f}{'' if where is None else f' {where}'}")
        if worst > 0.0:
            v.fail(f"{cls}: the cutter runs into a fill it is not cutting")


def check_oiler(v):
    """The sight-feed oiler: the cup over the cutter (its z span holds the cutter's plane), the oil
    inside the glass from empty (oil 0) to full (oil 1) and under the cap; the drip tube's end just
    off the cutter's teeth, where the drip anchor is; the tray on the knee under the blank all through
    the pass, either master."""
    m = v.m
    (x0, x1), (y0, y1), (z0, z1) = m.OILER["x"], m.OILER["y"], m.OILER["z"]
    for oil in (0.0, 0.5, 1.0):
        lvl = v.named("oillevel", r".", (0.0, 0.0, 0.0, 0, 0.0, oil))[0]
        lo, hi = lvl.aabb()
        want = m.OIL_EMPTY + (m.OIL_FULL - m.OIL_EMPTY) * oil
        print(f"oiler: oil {oil}: the level is {hi[1] - lo[1]:.3f} tall (want {want:.3f}), from y {lo[1]:.2f} to {hi[1]:.2f} in a cup "
              f"{y0}..{y1}")
        if abs((hi[1] - lo[1]) - want) > 1e-6 or abs(lo[1] - y0) > 1e-6 or hi[1] > y1 - 0.1:
            v.fail(f"the oil level is wrong at oil {oil}")
        if lo[0] < x0 or hi[0] > x1 or lo[2] < z0 or hi[2] > z1:
            v.fail("the oil is outside its glass")
    glass = v.named("frame", r"^fr_oiler_glass")
    for problem in sight_glass(glass, v.named("oillevel", r".", (0.0, 0.0, 0.0, 0, 0.0, 1.0))[0]):
        v.fail(f"the oiler's level is hidden: {problem}")
    print(f"oiler: {len(glass)} panes of glass in the Transparent pass, the level seen through one from every side")
    if not (z0 < m.Z_CUT < z1):
        v.fail("the oiler is not over the cutter")
    tip = v.named("valve", r"valve_nozzle_tip")[0]
    pipe = v.named("valve", r"valve_nozzle$")[0]
    tlo, thi = tip.aabb()
    plo, phi = pipe.aabb()
    tip_y = lambda x: m.SPINDLE_Y + math.sqrt(max(0.0, m.CUTTER_R ** 2 - (x - m.X_BLANK) ** 2))   # noqa: E731
    end_x = (tlo[0] + thi[0]) / 2
    over = tlo[1] - tip_y(end_x)
    least = min(min(tlo[1] - tip_y(tlo[0] + (thi[0] - tlo[0]) * i / 10) for i in range(11)),
                min(plo[1] - tip_y(plo[0] + (phi[0] - plo[0]) * i / 20) for i in range(21)))
    drip = [c * 16 for c in v.rig["drip"]["pos"]]
    print(f"oiler: the injection valve's nozzle mouth is {over:.2f} over the cutter's teeth (the nozzle never closer than "
          f"{least:.2f}) at z {m.Z_CUT}; the drip anchor at {[round(c, 2) for c in drip]}")
    if not (0.2 < over < 0.8) or least < 0.2 or abs(drip[0] - end_x) > 0.01 or abs(drip[1] - tlo[1]) > 0.01 or abs(drip[2] - m.Z_CUT) > 0.01:
        v.fail("the valve's nozzle does not end over the cutter's teeth, or the drip anchor is not at its mouth")
    feed = v.named("valve", r"valve_feed")[0]
    base = v.named("frame", r"fr_oiler_base")[0]
    flo, fhi = feed.aabb()
    if abs(fhi[1] - base.aabb()[0][1]) > 1e-6:
        v.fail("the valve's feed does not reach the reservoir's base")
    # the plunger pumps: out and back once a tooth
    xs = [v.mat("valveplunger", m.pose_at(1, 3 + i / 8))[0][3] * 16 for i in range(8)]
    print(f"oiler: the plunger moves {max(xs) - min(xs):.2f} once a tooth")
    if abs((max(xs) - min(xs)) - 2 * m.PLUNGER_STROKE) > 1e-3:
        v.fail("the valve's plunger does not pump")

    tray = v.named("knee", r"knee_tray$")[0]
    for k, cls in ((1, "thin"), (2, "thick")):
        for T in (0.0, 0.1, 0.24, 0.38):
            pose = m.pose_at(k, 3 + T)
            tr = v.named("knee", r"knee_tray$", pose)[0]
            rlo, rhi = tr.aabb()
            bl = v.named("blanksmall" if k == 1 else "blanklarge", r"_body", pose)
            blo, bhi = aabb_of(bl)
            under = rlo[0] <= blo[0] and rhi[0] >= bhi[0] and rlo[2] <= blo[2] and rhi[2] >= bhi[2] and rhi[1] < blo[1]
            if not under:
                v.fail(f"the drip tray is not under the {cls} blank at T {3 + T}")
    print(f"oiler: the drip tray ({tray.size[0]:.1f} x {tray.size[2]:.1f}) is under either blank all through the pass")


def check_cover_seams(v):
    """Every edge of every cover panel meets the frame (or another cover panel): points along each edge,
    at the panel's mid-thickness and on both its faces, must lie on or within 0.06 of a frame or other
    cover element, except where the gearbox top is slotted for the clutch lever's handle."""
    m = v.m
    eps, n = 0.06, 9
    others = [(e.name, e.aabb()) for pid in ("frame", "cover") for e in v.by_part.get(pid, [])]
    px, _, pz = m.CLUTCH_PIVOT
    slot = ((px - 1.4, px + 1.4), (pz - 0.6, pz + 0.6))
    bad = []
    edges = 0
    for el in v.by_part.get("cover", []):
        lo, hi = el.aabb()
        size = [hi[k] - lo[k] for k in range(3)]
        thin = min(range(3), key=lambda k: size[k])
        a, b = [k for k in range(3) if k != thin]
        for axis, other in ((a, b), (b, a)):
            for at in (lo[axis], hi[axis]):
                edges += 1
                for i in range(n):
                    q = [0.0] * 3
                    q[axis] = at
                    q[other] = lo[other] + (hi[other] - lo[other]) * (i + 0.5) / n
                    if (slot[0][0] - 1e-6 <= q[0] <= slot[0][1] + 1e-6) and (slot[1][0] - 1e-6 <= q[2] <= slot[1][1] + 1e-6) and thin == 1:
                        continue
                    ok = False
                    for t in (lo[thin], (lo[thin] + hi[thin]) / 2, hi[thin]):
                        q[thin] = t
                        for name, (olo, ohi) in others:
                            if name == el.name:
                                continue
                            if all(olo[k] - eps <= q[k] <= ohi[k] + eps for k in range(3)):
                                ok = True
                                break
                        if ok:
                            break
                    if not ok:
                        bad.append((el.name, "xyz"[axis], round(at, 3), [round(c, 2) for c in q]))
                        break
    print(f"cover seams: {len(v.by_part.get('cover', []))} panels, {edges} edges; {len(bad)} edges with a gap to the frame")
    if bad:
        v.fail(f"cover panels do not meet the frame: {bad[:6]}")


def check_outline(v):
    """The sills are the base's outline in plan: no frame or cover element whose bottom is below the knee
    projects beyond their outer faces, each sill's outer face is the body's extreme face on its side, and
    the east and west sills run the full length, meeting the north and south sills' outer faces."""
    m = v.m
    eps = 1e-6
    sills = {e.name: e.aabb() for e in v.by_part["frame"] if e.name.startswith("fr_sill_")}
    x0 = min(lo[0] for lo, _ in sills.values())
    x1 = max(hi[0] for _, hi in sills.values())
    z0 = min(lo[2] for lo, _ in sills.values())
    z1 = max(hi[2] for _, hi in sills.values())
    low = [e for pid in ("frame", "cover") for e in v.by_part.get(pid, [])
           if not e.name.startswith("fr_sill_") and e.aabb()[0][1] < m.KNEE_BOTTOM]
    out = []
    for e in low:
        lo, hi = e.aabb()
        if lo[0] < x0 - eps or hi[0] > x1 + eps or lo[2] < z0 - eps or hi[2] > z1 + eps:
            out.append((e.name, [round(lo[0], 2), round(hi[0], 2), round(lo[2], 2), round(hi[2], 2)]))
    bx0 = min(e.aabb()[0][0] for e in low)
    bx1 = max(e.aabb()[1][0] for e in low)
    bz0 = min(e.aabb()[0][2] for e in low)
    bz1 = max(e.aabb()[1][2] for e in low)
    w, e_, n, s = sills["fr_sill_w"], sills["fr_sill_e"], sills["fr_sill_n"], sills["fr_sill_s"]
    faces = {"west": (w[0][0], bx0), "east": (e_[1][0], bx1), "north": (n[0][2], bz0), "south": (s[1][2], bz1)}
    print(f"outline: sills x {x0:.2f}..{x1:.2f}, z {z0:.2f}..{z1:.2f}; the body below the knee x {bx0:.2f}..{bx1:.2f}, "
          f"z {bz0:.2f}..{bz1:.2f}; {len(out)} elements beyond the sills")
    if out:
        v.fail(f"elements below the knee project beyond the sills: {out[:6]}")
    for side, (sill, body) in faces.items():
        if abs(sill - body) > eps:
            v.fail(f"the {side} sill's outer face ({sill:.2f}) is not the body's {side} face ({body:.2f})")
    for name, (lo, hi) in (("fr_sill_w", w), ("fr_sill_e", e_)):
        if abs(lo[2] - z0) > eps or abs(hi[2] - z1) > eps:
            v.fail(f"{name} does not run the full length (z {lo[2]:.2f}..{hi[2]:.2f})")
    if not (x0 >= -eps and x1 <= m.CELLS_X * 16 + eps and z0 >= -eps and z1 <= m.CELLS_Z * 16 + eps):
        v.fail("the sills leave the footprint")


def check_anchors(v):
    m = v.m
    rig = v.rig
    pc = tuple(rig["powerCell"])
    entry = v.named("entry", r"_shaft")
    lo, _ = aabb_of(entry)
    print(f"anchors: power cell {pc} face {rig['powerFace']}, the entry shaft starts at x {lo[0]:.2f} on that face, axis at "
          f"(y {m.ENTRY[0]}, z {m.ENTRY[1]}): the cell's centre")
    if lo[0] > 0.01 or pc != m.POWER_CELL or (m.ENTRY[0] % 16, m.ENTRY[1] % 16) != (8.0, 8.0):
        v.fail("the entry shaft does not meet the power face at the cell's centre")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_floating(v)
    poses = [m.REST] + [m.pose_at(k, T) for k in (1, 2) for T in (0.0, 0.03, 0.06, 0.2, 0.37, 0.5, 0.72, 0.78, 0.84, 0.89, 0.93, 0.98, 3.5, 11.21)]
    check_containment(v, poses)
    check_anchors(v)
    check_textures(v)
    check_gearing(v)
    check_index(v)
    if not quick:
        check_mesh_depths(v)
    check_fills(v)
    check_blank_faces(v)
    if not quick:
        check_cutter_fills(v)
    check_cams(v)
    check_supports(v)
    check_oiler(v)
    check_cover_seams(v)
    check_outline(v)


    check_clearances(v, clearance_poses(m))

    if not quick:
        check_zfight(v)
        check_close_faces(v)
    return v.ok


def check_items(m, items):
    """Each forged part's item shape: inside the 16-voxel item box at its scale, centred on its floor,
    every element with a textured face, and its scale no smaller than it needs (1 when it fits as it is)."""
    ok = True
    for item, els in items.items():
        cs = [q for el in els for q in el.corners()]
        lo = [min(q[i] for q in cs) for i in range(3)]
        hi = [max(q[i] for q in cs) for i in range(3)]
        need = min(1.0, 16.0 / (max(hi[i] - lo[i] for i in range(3)) / m.ITEM_SCALE[item]))
        bare = [el.name for el in els if not any(f.get("texture") for f in el.faces.values())]
        print(f"item {item}: {len(els)} elements, {' x '.join(f'{hi[i] - lo[i]:.2f}' for i in range(3))} at scale "
              f"{m.ITEM_SCALE[item]} (fits from {need:.3f})")
        if min(lo) < -1e-6 or max(hi) > 16 + 1e-6 or abs(lo[1]) > 1e-6 or abs(lo[0] + hi[0] - 16) > 1e-6 or abs(lo[2] + hi[2] - 16) > 1e-6:
            print(f"FAIL item {item} is not centred on the floor of the item box: {lo} .. {hi}")
            ok = False
        if bare or not els:
            print(f"FAIL item {item} has elements with no textured face: {bare[:4]}")
            ok = False
        if m.ITEM_SCALE[item] > need + 1e-9 or m.ITEM_SCALE[item] < need - 0.02:
            print(f"FAIL item {item}'s scale {m.ITEM_SCALE[item]} is not the scale it needs, {need:.3f}")
            ok = False
    return ok


def validate_files(m, shape, frame_shape, ship):
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL textures used but not declared: {missing}")
        ok = False
    gaps = lid_gaps(ship["cells"])
    hollow = sum(1 for c in ship["cells"] if c.get("hollow"))
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, {hollow} hollow; "
          f"lids over every column: {'yes' if not gaps else gaps}")
    if gaps:
        ok = False
    return ok
