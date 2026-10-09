"""The rosser generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model
breaks its rule. The numbers in the comments are the design's (build/rosser/design.md section 6.5):
each check says what it proves. Everything is in the build frame (voxels), on the model before the
origin shift; `check_shipped` in make_shape.py proves the shipped files are this model moved.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import (bearing_margin, box_overhang, cells_touched, coplanar_faces, euler_round_trip,
                               frame_floating, lid_gaps, obb_obb)
from machinegen.checks import supports as shaft_supports
from machinegen.geometry import aabb_of, mvec
from machinegen.rigmath import CLASSES, gauge_fraction, full_inputs, part_of, posed
from machinegen.rigmath import apply as _apply

CLASS_K = {"thin": 1, "thick": 2}


class V:
    """The running state of a validation: the model, the parts by id, a cache of posed parts."""

    def __init__(self, m, els, parts, rig, le):
        self.m, self.els, self.parts, self.rig, self.le = m, els, parts, rig, le
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        # The pipes in the other metals are the first metal's, copied (check_pipes): only one metal's
        # are ever drawn, so the checks of what meets what see the first metal's alone.
        self.copies = {f"pipe{x}" for x in m.PIPE_METALS[1:]}
        self.shown = [el for el in els if el.part not in self.copies]
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
            if len(self.cache) > 6000:
                self.cache.clear()
            mm = self.mat(pid, pose)
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def point(self, pid, pose, p):
        """A point of part pid (authored, voxels) where pose puts it (voxels)."""
        q = _apply(self.mat(pid, pose), [v / 16 for v in p])
        return [v * 16 for v in q]

    def els_named(self, pid, rx, pose=None):
        src = self.posed(pid, pose) if pose is not None else self.by_part.get(pid, [])
        r = re.compile(rx)
        return [e for e in src if r.search(e.name)]


def pose(th=0.0, ps=None, ph=0.0, T=0.0, k=0, p=0.0):
    return (th, abs(th) if ps is None else ps, ph, T, k, p)


# ---------------------------------------------------------------- the trunk
def trunk_els(v, cls, T):
    return v.m.trunk_placed(v.le[cls], cls, T)


def trunk_hull(v, cls):
    return v.m.hull_cache(v.m.SECTIONS[cls])


def hull_signed(hl, q):
    """Signed distance of a point (y, z offsets from the axis) to the convex hull: negative inside."""
    inside = True
    best_out = 1e9
    best_in = 1e9
    for i in range(len(hl)):
        a, b = hl[i - 1], hl[i]
        ex, ey = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(ex, ey)
        cross = (ex * (q[1] - a[1]) - ey * (q[0] - a[0])) / ln
        if cross < 0:
            inside = False
        t = max(0.0, min(1.0, ((q[0] - a[0]) * ex + (q[1] - a[1]) * ey) / (ln * ln)))
        d = math.hypot(q[0] - a[0] - t * ex, q[1] - a[1] - t * ey)
        best_out = min(best_out, d)
        best_in = min(best_in, cross)
    return -best_in if inside else best_out


def samples(el, n=3):
    """Points over an element's surface (corners, edges and faces on an n x n grid), voxels."""
    h = [abs(s) / 2 for s in el.size]
    pts = []
    grid = [-1 + 2 * i / (n - 1) for i in range(n)]
    for k in range(3):
        for s in (-1, 1):
            for a in grid:
                for b2 in grid:
                    loc = [0.0, 0.0, 0.0]
                    loc[k] = s * h[k]
                    u, w = [j for j in range(3) if j != k]
                    loc[u], loc[w] = a * h[u], b2 * h[w]
                    d = mvec(el.r, loc)
                    pts.append([el.c[i] + d[i] for i in range(3)])
    return pts


def depth_into_trunk(v, cls, T, els, n=3):
    """How deep any sample point of `els` goes into the trunk at travel T (voxels; negative: the
    clearance): make_shape.trunk_depth, the section's hull extruded from tail to nose."""
    return v.m.trunk_depth(cls, (v.m.NOSE0 / 16 + T) * 16, els, n), None


# ---------------------------------------------------------------- checks
def check_basic(v):
    m, els, parts, rig = v.m, v.els, v.parts, v.rig
    worst = euler_round_trip(els)
    print(f"rotation round trip: worst matrix error {worst:.2e}")
    if worst > 1e-4:
        v.fail("euler decomposition")
    counts = {}
    for el in els:
        got = part_of(parts, el.name)
        counts[got] = counts.get(got, 0) + 1
        if got != el.part:
            v.fail(f"{el.name}: matches part {got}, intended {el.part}")
    names = [el.name for el in els]
    dupes = sorted({n for n in names if names.count(n) > 1})
    if dupes:
        v.fail(f"duplicate element names: {dupes[:6]}")
    print("elements per part:", ", ".join(f"{p['id']} {counts.get(p['id'], 0)}" for p in parts), f"(total {len(els)})")
    for p in parts:
        if counts.get(p["id"], 0) == 0:
            v.fail(f"part {p['id']} matches nothing")
    # glob traps: a frame name that a moving part's glob would take
    for trap, other in (("ring_x", "ringtyre_x"), ("rock_x", "rocker_x"), ("toproll_in_x", "toparm_in_x")):
        if part_of(parts, trap) == part_of(parts, other):
            v.fail(f"globs do not tell {trap} from {other}")


def check_containment(v, poses):
    m = v.m
    declared = {tuple(c["pos"]) for c in v.rig["cells"]}
    bad = set()
    for el in v.els:
        lo, hi = posed(el, v.mat(el.part, m.REST)).aabb()
        for c in cells_touched(lo, hi):
            if c not in declared:
                bad.add((el.name, c))
    worst, who = 0.0, None
    for pid in v.by_part:
        for ps in ([m.REST] if pid == "frame" else poses):
            for el in v.posed(pid, ps):
                lo, hi = el.aabb()
                over = box_overhang(lo, hi, (m.CELLS_X, m.CELLS_Y, m.CELLS_Z))
                if over > worst:
                    worst, who = over, (el.name, ps)
    print(f"cells: {len(declared)} declared, {sum(1 for c in v.rig['cells'] if c.get('hollow'))} hollow; "
          f"{'every element at rest is inside them' if not bad else 'OUTSIDE: ' + str(sorted(bad)[:6])}; machine box: worst overhang over the motion {max(worst, 0):.3f} {who or ''}")
    if bad:
        v.fail("an element at rest reaches an undeclared cell")
    if worst > 0.01:
        v.fail("an element leaves the machine box")


def check_zfight(v):
    for ps in v.m.COPLANAR_POSES:
        pairs = coplanar_faces([posed(el, v.mat(el.part, ps)) for el in v.shown])
        print(f"coplanar faces at {ps}: {len(pairs)} pairs")
        for na, da, nb, db, area in pairs[:12]:
            v.fail(f"{na} {da} and {nb} {db} share a plane over {area} sq voxels (z-fighting)")


def check_floating(v):
    frame = v.by_part["frame"]
    seen, floating = frame_floating(frame)
    print(f"frame: {len(seen)} of {len(frame)} elements joined to the ground through the frame" + ("" if not floating else f"; FLOATING: {', '.join(floating[:12])}"))
    if floating:
        v.fail("frame elements float free of the structure")


def check_bearings(v):
    """Each bearing block encloses the shaft it carries, at rest and turned 45 degrees; every shaft
    is carried by at least two fixed bearings (or one and a pilot); loose wheels are located."""
    m = v.m
    frame = v.by_part["frame"]
    t45 = pose(th=math.pi / 4, ps=math.pi / 4, ph=math.pi / 4, T=0.0)
    shafts = [
        ("main shaft", "main", r"^main_shaft_", 0, (m.MAIN_Y, m.MAIN_Z), r"^fr_bearing_main"),
        ("entry shaft", "entry", r"^entry_shaft_", 2, (m.ENTRY_X, m.MAIN_Y), r"^fr_entry_bearing"),
        ("rock shaft", "rock", r"^rock_shaft", 0, (m.ROCK_Y, m.SEL_Z), r"^fr_bearing_rock"),
        ("rocker shaft", "rocker", r"^rocker_shaft", 0, (m.ROCKER_Y, m.ROCKER_Z), r"^fr_rocker_bearing"),
        ("breaker pin", "breaker", r"^breaker_pin", 2, (m.BREAKER_X, m.BREAKER_Y), r"^fr_breaker_hanger"),
        ("treadle lever pin", "treadlever", r"^treadlever_axle", 0, (m.treadle_geometry()["y"], m.treadle_geometry()["pivot"]), r"^fr_treadle_bearing"),
    ]
    for st, s in m.STATIONS.items():
        shafts.append((f"{st} lay shaft", f"lay_{st}", r"_shaft_", 2, (s["X"], m.SHAFT_Y), rf"^fr_{st}_bearing_lay"))
        shafts.append((f"{st} cross shaft", f"cross_{st}", r"_shaft_", 2, (s["C"], m.SHAFT_Y), rf"^fr_{st}_bearing_cross"))
    for i, ang in enumerate(m.ROLLER_ANGLES, 1):
        p = m.ring_point(ang, 0.0, m.ROLLER_AXIS_R)
        shafts.append((f"roller {i}'s pin", f"roller{i}", r"_pin", 0, (p[1], p[2]), rf"^fr_roller{i}_bearing"))
    for label, pid, rx, axis, c, brx in shafts:
        els_ = v.els_named(pid, rx)
        found, _ = shaft_supports(frame, els_, axis, c)
        bearings = [f for f in frame if re.search(brx, f.name)]
        worst = 1e9
        for b in bearings:
            shaft_posed = v.els_named(pid, rx, m.REST) + v.els_named(pid, rx, t45)
            worst = min(worst, bearing_margin(b, shaft_posed, axis))
        named = sorted(set(found) & {b.name for b in bearings})
        print(f"support {label}: {len(named)} bearing(s) {', '.join(named) or 'NONE'}; each encloses it with {worst:.2f} to spare")
        if len(named) < 2:
            v.fail(f"{label} is not carried by two bearings")
        if worst < -0.01 or worst > 1e8:
            v.fail(f"a bearing of the {label} does not enclose it")
    # loose wheels located on both faces along their shaft
    loose = [("pinion_w", "main", 0, (m.MAIN_Y, m.MAIN_Z)), ("pinion_e", "main", 0, (m.MAIN_Y, m.MAIN_Z))]
    for st in m.STATIONS:
        loose += [(f"fast_{st}", f"cross_{st}", 2, (m.STATIONS[st]["C"], m.SHAFT_Y)), (f"slow_{st}", f"cross_{st}", 2, (m.STATIONS[st]["C"], m.SHAFT_Y))]
    for pid, holder, axis, c in loose:
        lo, hi = aabb_of(v.by_part[pid])
        u, w = [k for k in range(3) if k != axis]
        sides = []
        for face, sgn in ((lo[axis], -1), (hi[axis], 1)):
            near = []
            for e in v.by_part[holder] + v.by_part["frame"] + v.by_part.get(f"sel_{pid.split('_')[-1]}", []) + v.by_part.get(f"toparm_{pid.split('_')[-1]}", []):
                a, b2 = e.aabb()
                if not (a[u] <= c[0] <= b2[u] and a[w] <= c[1] <= b2[w]):
                    continue
                if (b2[axis] - a[axis]) > 6.0:
                    continue
                if (sgn < 0 and face - 1.0 <= b2[axis] <= face + 0.02) or (sgn > 0 and face - 0.02 <= a[axis] <= face + 1.0):
                    near.append(e.name)
            sides.append(near)
        print(f"location {pid}: {'; '.join((', '.join(sorted(s)[:2]) or 'NOTHING') for s in sides)}")
        if not sides[0] or not sides[1]:
            v.fail(f"{pid} is free to slide along its shaft")


def disp(v, pid, p, ps1, ps2):
    """How far the material point of part pid at p (voxels, at pose ps1) moves going to ps2."""
    m1, m2 = v.mat(pid, ps1), v.mat(pid, ps2)
    r = [row[:3] for row in m1[:3]]
    t = [m1[i][3] * 16 for i in range(3)]
    q = [p[i] - t[i] for i in range(3)]
    x = [sum(r[j][i] * q[j] for j in range(3)) for i in range(3)]       # m1 inverse (rigid)
    y = [sum(m2[i][j] * x[j] for j in range(3)) + m2[i][3] * 16 for i in range(3)]
    return [y[i] - p[i] for i in range(3)]


def check_gearing(v):
    """Each meshing pair: pitch circles tangent and the contact point moving the same way at the same
    speed on both wheels (so whole tooth counts at one circular pitch and the rig's ratios agree),
    for both shaft directions; the rectifier; the feed train per class; the rolls on the trunk."""
    m = v.m
    d = 1e-3
    worst_t, worst_v = 0.0, 0.0
    lines = []

    def mesh(label, pa, pb, ca, cb, ra, rb, axis, ps1, ps2, external=True):
        nonlocal worst_t, worst_v
        u, w = [k for k in range(3) if k != axis]
        dist = math.hypot(ca[u] - cb[u], ca[w] - cb[w])
        tang = abs(dist - (ra + rb if external else abs(ra - rb)))
        p = list(ca)
        p[u] = ca[u] + (cb[u] - ca[u]) * ra / dist
        p[w] = ca[w] + (cb[w] - ca[w]) * ra / dist
        da, db = disp(v, pa, p, ps1, ps2), disp(v, pb, p, ps1, ps2)
        mag = max(math.sqrt(sum(x * x for x in da)), 1e-12)
        err = math.sqrt(sum((da[i] - db[i]) ** 2 for i in range(3))) / mag
        worst_t, worst_v = max(worst_t, tang), max(worst_v, err)
        lines.append(f"{label}: centre distance off by {tang:.3f}, contact speeds differ by {100 * err:.2f} %")
        if tang > 0.05 or err > 0.01:
            v.fail(f"{label} does not mesh (distance off {tang:.3f}, speeds {100 * err:.2f} %)")

    G = m.G
    for th in (0.4, -0.4):
        p1, p2 = pose(th=th), pose(th=th + math.copysign(d, th))
        mesh(f"ring pinion / ring rim ({'+' if th > 0 else '-'})", "main", "ring", [0, m.MAIN_Y, m.MAIN_Z], [0, m.H, m.TZ], m.PINION_R, m.RING_PITCH_R, 0, p1, p2)
        for i, ang in enumerate(m.ROLLER_ANGLES, 1):
            rp = m.ring_point(ang, 122.0, m.ROLLER_AXIS_R)
            mesh(f"roller {i} / tyre", f"roller{i}", "ringtyre", rp, [122.0, m.H, m.TZ], m.ROLLER_RHO, m.TYRE_R, 0, p1, p2)
        for st, s in m.STATIONS.items():
            mesh(f"{st} lay fast / cross fast", f"lay_{st}", f"fast_{st}", [s["X"], m.SHAFT_Y, m.FAST_Z[0]], [s["C"], m.SHAFT_Y, m.FAST_Z[0]], m.FAST[0] * m.CHANGE_MODULE / 2, m.FAST[1] * m.CHANGE_MODULE / 2, 2, p1, p2)
            mesh(f"{st} lay slow / cross slow", f"lay_{st}", f"slow_{st}", [s["X"], m.SHAFT_Y, m.SLOW_Z[0]], [s["C"], m.SHAFT_Y, m.SLOW_Z[0]], m.SLOW[0] * m.CHANGE_MODULE / 2, m.SLOW[1] * m.CHANGE_MODULE / 2, 2, p1, p2)
            # the worm: its thread's crossing of the contact line advances by the lead per turn, as the wheel's pitch point
            ps1, ps2 = p1, p2
            main_turn = G * abs(d)
            wheel_turn = (m.mat_ratio(v.parts, f"lay_{st}")) * abs(d)
            adv_worm = m.WORM_LEAD * main_turn / (2 * math.pi)
            adv_wheel = m.WHEEL_PITCH_R * wheel_turn
            err = abs(adv_worm - adv_wheel) / adv_worm
            dist = abs((m.MAIN_Y - m.SHAFT_Y) - (m.WORM_PITCH_R + m.WHEEL_PITCH_R))
            lines.append(f"{st} worm / wheel: centre distance off by {dist:.3f}, lead {m.WORM_LEAD:.4f} = the wheel's circular pitch "
                         f"{2 * math.pi * m.WHEEL_PITCH_R / m.WHEEL_TEETH:.4f}; thread and wheel advance differ by {100 * err:.2f} %")
            dz = disp(v, f"lay_{st}", [s["X"], m.SHAFT_Y + m.WHEEL_PITCH_R, m.MAIN_Z], ps1, ps2)
            # the wheel's top moves the way the thread's crossing moves (right-handed thread: -x for the shaft's +turn)
            if err > 0.01 or dist > 0.05 or not dz[0] < 0:
                v.fail(f"{st} worm and wheel do not mesh")
    # the rectifier: the disc turns with theta; one pinion turns with the main shaft each way
    for th in (0.5, -0.5):
        p1, p2 = pose(th=th), pose(th=th + math.copysign(d, th))
        pinions = {pid: m.mat_ratio(v.parts, pid) * math.copysign(1, th) for pid in ("pinion_w", "pinion_e")}
        main = m.mat_ratio(v.parts, "main")
        bites = [pid for pid, r in pinions.items() if abs(r - main) < 1e-6]
        lines.append(f"rectifier, axle {'+' if th > 0 else '-'}: the main shaft turns {main:+.4f} per radian of travel, "
                     f"pinions {', '.join(f'{k} {r:+.4f}' for k, r in pinions.items())}: {bites[0] if bites else 'NONE'} carries it")
        if len(bites) != 1:
            v.fail("the rectifier does not turn the main shaft forward both ways")
        # the crown disc's peg ring against each pinion's: IW's ratio, and both pegs moving the same way where they meet
        for pid, sgn in (("pinion_w", -1), ("pinion_e", 1)):
            pp = [m.ENTRY_X + sgn * m.DISC_PEG_R, m.MAIN_Y, m.MAIN_Z - m.MESH_DZ]
            de, dp = disp(v, "entry", pp, p1, p2), disp(v, pid, pp, p1, p2)
            cos = sum(de[i] * dp[i] for i in range(3)) / max(1e-12, math.sqrt(sum(x * x for x in de)) * math.sqrt(sum(x * x for x in dp)))
            ratio = abs(m.mat_ratio(v.parts, pid) / m.mat_ratio(v.parts, "entry"))
            if cos < 0.95 or abs(ratio - m.DISC_PEG_R / 2.54) > 1e-4:
                v.fail(f"the crown disc and {pid} do not mesh (directions {cos:.3f}, ratio {ratio:.4f})")
    # the feed: with the selector on a class's gear, phi turns gear[k] per axle radian and T moves b per phi radian
    gear = m.feed_gear()
    b = m.blocks_per_radian()
    for cls in ("thin", "thick"):
        k = CLASS_K[cls]
        for st, s in m.STATIONS.items():
            T0 = 4.0 if st == "in" else 8.0
            T0 = min(T0, m.t_end(k) - 0.5)
            p1 = (0.3, 5.0, 20.0, T0, k, 1.0)
            p2 = (0.3 + d, 5.0 + d, 20.0 + gear[cls] * d, T0 + b * gear[cls] * d, k, 1.0)
            other = "fast" if cls == "thin" else "slow"
            r_sel = m.SEL_R[1]
            pt_ = [s["C"], m.SHAFT_Y + r_sel, m.selector_faces()[other]]
            ds, dg = disp(v, f"sel_{st}", pt_, p1, p2), disp(v, f"{other}_{st}", pt_, p1, p2)
            # the selector also slides at its gauge's rate (none at full occupancy)
            mag = max(math.sqrt(sum(x * x for x in dg)), 1e-12)
            err = math.sqrt(sum((ds[i] - dg[i]) ** 2 for i in range(3))) / mag
            lines.append(f"{st} {cls}: the selector on the {other} gear's dogs, faces move together to {100 * err:.2f} %")
            if err > 0.01:
                v.fail(f"{st} {cls}: the selector and the {other} gear turn at different rates (the drawn gear is not the feed's)")
            # the banjo
            xr, yr = m.top_roll_centre(st, m.roll_lift(cls))
            mesh(f"{st} {cls} banjo", f"cross_{st}", f"toproll_{st}", [s["C"], m.SHAFT_Y, m.BANJO_Z[0]], [xr, yr, m.BANJO_Z[0]],
                 m.BANJO[0] / 2, m.BANJO[1] / 2, 2, p1, p2)
            # the top roll's underside moves with the trunk: no slip
            under = [xr, yr - m.ROLL_RHO, m.TZ]
            du = disp(v, f"toproll_{st}", under, p1, p2)
            dt = b * gear[cls] * d * 16
            err = abs(du[0] - dt) / dt
            lines.append(f"{st} {cls}: the top roll's surface moves {du[0] / (gear[cls] * d):.4f} voxels per axle radian, the trunk "
                         f"{dt / (gear[cls] * d):.4f}: slip {100 * err:.2f} %")
            if err > 0.005 or abs(du[1]) > 0.01 * dt:
                v.fail(f"{st} {cls}: the top roll slips on the trunk")
            yb = m.SADDLE_UP - (m.cradle_drop() if cls == "thick" else 0.0)
            top = [m.BOTROLL_X[st], yb, m.TZ]
            Tb = (m.BOTROLL_X[st] - m.NOSE0) / 16 + 1.0
            q1 = (0.3, 5.0, 20.0, Tb, k, 1.0)
            q2 = (0.3, 5.0, 20.0 + d, Tb + b * d, k, 1.0)
            db_ = disp(v, f"botroll_{st}", top, q1, q2)
            err = abs(db_[0] - b * d * 16) / (b * d * 16)
            if err > 0.005:
                v.fail(f"{st} {cls}: the bottom roll's top does not move with the trunk ({100 * err:.2f} %)")
    # held to the gameplay's pace
    gp = m.gameplay_gear()
    for cls in ("thin", "thick"):
        dev = gear[cls] / gp[cls] - 1
        lines.append(f"feed.gear.{cls} {gear[cls]:.5f} feed radians per axle radian ({gear[cls] * b * 2 * math.pi:.4f} blocks per axle turn); "
                     f"the pace (RosserConfig's typical {cls} trunk) wants {gp[cls]:.5f}: {100 * dev:+.2f} %")
        if abs(dev) > 0.03:
            v.fail(f"the drawn {cls} gearing is more than 3 % off the pace")
    if not gear["thick"] < gear["thin"]:
        v.fail("thick trunks do not feed slower")
    for ln in lines:
        print(" ", ln)
    print(f"gearing: worst centre-distance error {worst_t:.3f}, worst contact speed mismatch {100 * worst_v:.2f} %")


def check_ring(v):
    """The ring is concentric with the trunk's path and clears the knots; it is carried by the
    rollers, tangent to its tyres all round, spanning more than half of it, flanges either side."""
    m = v.m
    ring = next(p for p in v.parts if p["id"] == "ring")
    piv = ring["drivers"][0]["pivot"]
    off = math.hypot(piv[1] * 16 - m.H, piv[2] * 16 - m.TZ)
    knots = max(m.support(m.SECTIONS["thick"], 2 * math.pi * j / 360) for j in range(360))
    print(f"ring: pivot {off:.2e} off the trunk's axis, about x; the bore ({m.BORE_R}) clears the thick knots ({knots:.2f}) by {m.BORE_R - knots:.2f}")
    if off > 1e-6 or ring["drivers"][0]["axis"] != "x" or m.BORE_R - knots < 1.0:
        v.fail("the ring is not concentric with the trunk or does not clear its knots")
    worst = 0.0
    for j in range(16):
        ps = pose(th=0.0, ps=2 * math.pi * j / 16 / abs(ring["drivers"][0]["ratio"]))
        c = v.point("ring", ps, [122.0, m.H, m.TZ])
        for i, ang in enumerate(m.ROLLER_ANGLES, 1):
            rp = m.ring_point(ang, 122.0, m.ROLLER_AXIS_R)
            worst = max(worst, abs(math.hypot(rp[1] - c[1], rp[2] - c[2]) - (m.TYRE_R + m.ROLLER_RHO)))
    angs = sorted(a % 360 for a in m.ROLLER_ANGLES)
    gaps = [(angs[(i + 1) % len(angs)] - angs[i]) % 360 for i in range(len(angs))]
    straddle = all(f0[1] <= m.TYRE_X[0] + 0.01 and m.TYRE_X[1] - 0.01 <= f1[0] for f0, f1 in [m.ROLLER_FLANGE_X])
    print(f"ring carried: rollers tangent to the tyres within {worst:.3f} at 16 ring angles; largest gap between rollers {max(gaps):.0f} degrees; "
          f"flanges {'either side of' if straddle else 'NOT round'} the tyre band")
    if worst > 0.05 or max(gaps) >= 180 or not straddle:
        v.fail("the ring is not carried by its rollers")


def check_arms(v):
    """The scraper tips ride the trunk's surface all round (penetration <= 0.3, gap <= 0.8 against
    the section's outline), only the tips touch it, and the nose arriving never presses into an arm
    by more than 0.3; with no trunk the arms are closed on their stops."""
    m = v.m
    ring = next(p for p in v.parts if p["id"] == "ring")
    ratio = abs(ring["drivers"][0]["ratio"])
    for cls in ("thin", "thick"):
        k = CLASS_K[cls]
        T = (m.ARM_PIN_X + 30 - m.NOSE0) / 16
        worst_pen, worst_gap, worst_other = -1e9, 0.0, -1e9
        for j in range(72):
            ps = (0.0, 2 * math.pi * j / 72 / ratio, 0.0, T, k, 1.0)
            for i in range(1, 5):
                tips = v.posed(f"tip{i}", ps)
                pen, _ = depth_into_trunk(v, cls, T, tips, 4)
                worst_pen = max(worst_pen, pen)
                worst_gap = max(worst_gap, -pen)
                oth, who = depth_into_trunk(v, cls, T, v.posed(f"arm{i}", ps), 3)
                worst_other = max(worst_other, oth)
        print(f"scraper tips, {cls}: over 72 ring angles the tips press in at most {max(worst_pen, 0):.2f} and stand off at most {worst_gap:.2f}; "
              f"the arms themselves stay {-worst_other:.2f} outside the trunk")
        if worst_pen > 0.3 or worst_gap > 0.8:
            v.fail(f"the scraper tips do not ride a {cls} trunk's surface")
        if worst_other > -0.5:
            v.fail(f"a scraper arm (not its tip) reaches a {cls} trunk")
        # the nose arriving: every T across the arms' window
        worst = -1e9
        ws = [w for w in m.arm_windows(-ratio) if w["gain"][cls] > 0][0]
        for n in range(41):
            nose = (ws["from"] - 0.25 + (ws["ease"] + 0.5) * n / 40) * 16
            T = (nose - m.NOSE0) / 16
            for jj in range(0, 72, 9):
                ps = (0.0, 2 * math.pi * jj / 72 / ratio, 0.0, T, k, 1.0)
                for i in range(1, 5):
                    pen, _ = depth_into_trunk(v, cls, T, v.posed(f"arm{i}", ps) + v.posed(f"tip{i}", ps), 3)
                    worst = max(worst, pen)
        print(f"scraper arms, {cls}: the nose riding under them presses in at most {max(worst, 0):.2f}")
        if worst > 0.3:
            v.fail(f"a {cls} trunk's nose runs into the scraper arms")


def check_rolls(v):
    """At full occupancy each top roll rests on the trunk's flat top and each bottom roll under its
    flat bottom (|gap| <= 0.05), the cradle up for thin and down for thick, the saddles under the
    flat bottom; a top roll at rest hangs REST_DROP below a thin trunk's top; the nose lifts a top roll
    without pressing into it by more than 0.3; the banjo stays in mesh over the arm's swing."""
    m = v.m
    for st in m.STATIONS:
        for cls in ("thin", "thick"):
            k = CLASS_K[cls]
            frm, full = m.roll_ride(st, cls)
            T = (full + 6 - m.NOSE0) / 16
            ps = (0.0, 0.0, 5.0, T, k, 1.0)
            xr, yr = m.top_roll_centre(st, 0.0)
            c = v.point(f"toproll_{st}", ps, [xr, yr, m.TZ])
            top = m.H + m.support(m.SECTIONS[cls], 0.0)
            gap_top = c[1] - m.ROLL_RHO - top
            xb = m.BOTROLL_X[st]
            cb = v.point(f"botroll_{st}", ps, [xb, m.SADDLE_UP - m.ROLL_RHO, m.TZ])
            bottom = m.H - m.support(m.SECTIONS[cls], math.pi)
            gap_bot = bottom - (cb[1] + m.ROLL_RHO)
            sk = v.point(f"cradle_{st}", ps, [0.0, m.SADDLE_UP, 0.0])[1]
            gap_sad = bottom - sk
            print(f"{st} rolls, {cls}: top roll {gap_top:+.3f} off the flat top, bottom roll {gap_bot:+.3f} under the flat bottom, saddles {gap_sad:+.3f}")
            for g, what in ((gap_top, "top roll"), (gap_bot, "bottom roll"), (gap_sad, "saddle")):
                if abs(g) > 0.05:
                    v.fail(f"{st} {what} does not bear on a {cls} trunk")
            # the nose lifting the top roll: the roll's circle against the trunk's side outline
            worst = 0.0
            for n in range(61):
                nose = frm - 1.0 + (full - frm + 2.0) * n / 60
                Tn = (nose - m.NOSE0) / 16
                c = v.point(f"toproll_{st}", (0.0, 0.0, 5.0, Tn, k, 1.0), [xr, yr, m.TZ])
                dx = max(0.0, c[0] - nose)
                dy = max(0.0, c[1] - top)
                dist = math.hypot(dx, dy) if (dx > 0 or dy > 0) else -min(nose - c[0], top - c[1])
                worst = max(worst, m.ROLL_RHO - dist)
            print(f"  the nose lifting it presses in at most {max(worst, 0):.2f}")
            if worst > 0.3:
                v.fail(f"{st}: a {cls} trunk's nose runs into the top roll")
        xr, yr = m.top_roll_centre(st, 0.0)
        rest_gap = (yr - m.ROLL_RHO) - (m.H + m.TRUNK_RADII["thin"][0])
        print(f"{st} top roll at rest: {rest_gap:+.2f} from a thin trunk's top (rule {-m.REST_DROP})")
        if abs(rest_gap + m.REST_DROP) > 0.01:
            v.fail(f"{st} top roll's rest height")


def check_cradle(v):
    """The rocker's arms bear under the cradles at both stops; the counterweights pull the cradles
    up (their centres on the far side of the rocker shaft) at both stops."""
    m = v.m
    for cls, k in (("thin", 1), ("thick", 2)):
        ps = (0.0, 0.0, 0.0, 0.0, k, 1.0)
        for st in m.STATIONS:
            ty, tz = m.rocker_tip(st)
            c = v.point("rocker", ps, [m.ROCKER_ARM_X[st], ty, tz])
            cheek = [e for e in v.posed(f"cradle_{st}", ps) if e.name == f"cradle_{st}_cheek_n"][0]
            gap = cheek.aabb()[0][1] - (c[1] + m.ROCKER_ROLLER)          # the tip roller's top (its axis turns with the arm)
            cw = [e for e in v.posed("rocker", ps) if e.name == f"rocker_cw_{st}"][0]
            side = m.ROCKER_Z - cw.c[2]                                     # (the arms reach south, the weights north)
            print(f"rocker, {cls}: arm {st} {gap:+.3f} under the cradle's cheek; counterweight {side:+.2f} north of the shaft")
            if abs(gap) > 0.05:
                v.fail(f"the rocker's {st} arm is not under its cradle ({cls})")
            if side <= 0.2:
                v.fail("the rocker's counterweight is not on the lifting side")


def check_levers(v):
    """Treadle, pushrod, rock shaft, selector levers and holds."""
    m = v.m
    tg = m.treadle_geometry()
    lg = m.lever_geometry()
    rock_d = next(p for p in v.parts if p["id"] == "rock")["drivers"][0]
    # the plate: flush with the saddle under a trunk, proud with none
    for cls, k in (("none", 0), ("thin", 1), ("thick", 2)):
        ps = (0.0, 0.0, 0.0, 0.0, k, 1.0 if k else 0.0)
        plate = v.posed("treadle", ps)[0].aabb()[1][1]
        sad = v.point(f"cradle_in", ps, [0.0, m.SADDLE_UP, 0.0])[1]
        want = m.TREADLE_PROUD if k == 0 else 0.0
        print(f"treadle plate, {cls}: {plate - sad:+.3f} above the saddle (rule {want})")
        if abs(plate - sad - want) > 0.02:
            v.fail(f"the treadle plate is not where it should be ({cls})")
    # the lever under the plate, the pushrod on the lever's tail pin and under the tappet, through the throw
    worst = {"foot": 0.0, "slot": 0.0, "tappet": 0.0}
    for cls, k in (("thin", 1), ("thick", 2)):
        for n in range(41):
            T = (m.TREADLE_X[1] - (m.NOSE0 - m.LENGTHS[cls] * 16) + (n / 40 - 0.5) * 2.0) / 16
            T = max(T, 0.0)
            for p in (1.0, 0.5):
                ps = (0.0, 0.0, 0.0, T, k, p)
                foot = v.point("treadlever", ps, [m.PUSHROD_X, tg["y"] + 0.5, tg["foot"]])
                stem = v.point("treadle", ps, [m.PUSHROD_X, tg["y"] + 0.5, m.TZ])
                worst["foot"] = max(worst["foot"], abs(foot[1] - stem[1]))
                pin = v.point("treadlever", ps, [m.PUSHROD_X, tg["y"], tg["tail"]])
                rodbot = v.point("pushrod", ps, [m.PUSHROD_X, tg["y"], tg["tail"]])
                slot = tg["rise"]["thick"] - tg["rise"]["thin"] + 0.6
                inslot = pin[1] - rodbot[1]
                sw = m.stirrup(tg)
                worst["slot"] = max(worst["slot"], max(0.0, sw[0] + 0.3 - pin[2]), max(0.0, pin[2] - (sw[1] - 0.3)), max(0.0, -inslot), max(0.0, inslot - slot))
                rod_top = v.point("pushrod", ps, [m.PUSHROD_X, m.ROCK_Y - 0.5 - tg["rise"]["thin"], tg["tail"]])
                tip_in = [m.PUSHROD_X, m.ROCK_Y - 0.5, tg["tail"]]
                rot = -lg["rock"]
                tip_out = [tip_in[0], m.ROCK_Y + (tip_in[1] - m.ROCK_Y) * math.cos(rot) - (tip_in[2] - m.SEL_Z) * math.sin(rot),
                           m.SEL_Z + (tip_in[1] - m.ROCK_Y) * math.sin(rot) + (tip_in[2] - m.SEL_Z) * math.cos(rot)]
                tap = v.point("rock", ps, tip_out)
                # the pushrod is on the tappet while the treadle is what holds the rock in; when a roll's
                # hold has it, the pushrod may drop away (never press up into it)
                e_all = gauge_fraction(rock_d, full_inputs(m.inputs_of(ps)), m.PATH_BUILD)
                e_tr = gauge_fraction(dict(rock_d, windows=rock_d["windows"][:1]), full_inputs(m.inputs_of(ps)), m.PATH_BUILD)
                gap = tap[1] - rod_top[1]
                worst["tappet"] = max(worst["tappet"], abs(gap) if e_all - e_tr < 1e-6 else max(0.0, -gap))
    print(f"treadle: the lever's foot under the plate's stem within {worst['foot']:.3f}; the lever's tail pin in the pushrod's slot within "
          f"{worst['slot']:.3f}; the pushrod's top under the tappet within {worst['tappet']:.3f} (both classes, the release and the load)")
    if worst["foot"] > 0.3 or worst["slot"] > 0.05 or worst["tappet"] > 0.1:
        v.fail("the treadle's linkage comes apart")
    # the selector levers: the rock's pin and the cradle's fulcrum in the lever's slot, the fork in the groove,
    # the selector's dogs on the gear's hub face when in and clear in neutral
    for st, s in m.STATIONS.items():
        worst = {"pin": 0.0, "fulcrum": 0.0, "fork": 0.0}
        lx = s["C"] + m.LEVER_DX
        for cls, k in (("thin", 1), ("thick", 2)):
            for n in range(21):
                for T in (0.0, 3.0):
                    ps = (0.0, 0.0, 0.0, T, k, n / 20)
                    if n < 20 and cls == "thick":
                        continue                 # (the fulcrum moves with presence: checked at p = 1)
                    lever_line = lambda y: v.point(f"sellever_{st}", ps, [lx, y, m.SEL_Z])     # noqa: E731
                    pin = v.point("rock", ps, [lx - 0.3, m.LEVER_P, m.SEL_Z])
                    ful = v.point(f"cradle_{st}", ps, [lx, lg["f_up"], m.SEL_Z])
                    # offsets across the slot (z) at the pins' heights
                    def off(pt_):
                        a, b2 = lever_line(lg["f_down"] - 1.0), lever_line(m.LEVER_P + 1.0)
                        t = (pt_[1] - a[1]) / (b2[1] - a[1])
                        return abs(pt_[2] - (a[2] + (b2[2] - a[2]) * t))
                    worst["pin"] = max(worst["pin"], off(pin))
                    worst["fulcrum"] = max(worst["fulcrum"], off(ful))
                    fork = v.point(f"sellever_{st}", ps, [s["C"] + 1.6, m.LEVER_Q, m.SEL_Z])
                    groove = v.point(f"sel_{st}", ps, [s["C"], m.SHAFT_Y, m.SEL_Z])
                    worst["fork"] = max(worst["fork"], abs(fork[2] - groove[2]))
        print(f"{st} selector lever: the rock's pin within {worst['pin']:.3f} of the slot's line, the cradle's fulcrum within "
              f"{worst['fulcrum']:.3f}, the fork within {worst['fork']:.3f} of the groove's centre")
        if worst["pin"] > 0.2 or worst["fulcrum"] > 0.2 or worst["fork"] > 0.15:
            v.fail(f"the {st} selector lever comes off its pins or its fork leaves the groove")
        faces = m.selector_faces()
        for cls, k, gearname in (("thin", 1, "fast"), ("thick", 2, "slow")):
            ps = (0.0, 0.0, 0.0, 0.0, k, 1.0)
            a, b2 = aabb_of([e for e in v.posed(f"sel_{st}", ps) if "_dog" not in e.name])
            face = b2[2] if gearname == "fast" else a[2]
            print(f"  {cls}: the selector's dogs reach {face:.3f}, the {gearname} gear's hub face is {faces[gearname]:.3f}")
            if abs(face - faces[gearname]) > 0.05:
                v.fail(f"{st} {cls}: the selector does not reach its gear")
        a, b2 = aabb_of(v.posed(f"sel_{st}", m.REST))
        clear = min(faces["fast"] - b2[2], a[2] - faces["slow"])
        print(f"  neutral: the selector's dogs clear both hub faces by {clear:.3f}")
        if clear < 0.25:
            v.fail(f"{st}: the selector in neutral is not clear of both gears")
    # the throw-out weight: north of the rock shaft at every angle (so it always throws it out)
    for e in (0.0, 0.5, 1.0):
        ps = (0.0, 0.0, 0.0, 0.0, 1, e)
        w = [x for x in v.posed("rock", ps) if x.name == "rock_weight"][0]
        if not w.c[2] < m.SEL_Z - 0.2:
            v.fail("the rock shaft's weight does not always pull it out")
    print("rock shaft: its throw-out weight stays north of the shaft over the whole throw")
    # the holds: the finger rests on the arc whenever that roll alone holds the rock; the arc clears it at rest
    for st in m.STATIONS:
        hg = m.hold_geometry(st)
        worst_on, worst_rest = 0.0, 1e9
        for cls, k in (("thin", 1), ("thick", 2)):
            raised = (0.0, 0.0, 0.0, (m.roll_ride(st, cls)[1] + 10 - m.NOSE0) / 16, k, 1.0)
            fin = [x for x in v.posed("rock", raised) if x.name == f"rock_finger_{st}"][0]
            arcs = [x for x in v.posed(f"toparm_{st}", raised) if "_arc" in x.name]
            under = fin.aabb()[0][1]
            tops = [a.aabb()[1][1] for a in arcs if a.aabb()[0][0] - 0.01 <= fin.c[0] <= a.aabb()[1][0] + 0.01]
            worst_on = max(worst_on, abs(under - max(tops)) if tops else 9.0)
        fin = [x for x in v.posed("rock", m.REST) if x.name == f"rock_finger_{st}"][0]
        for a in [x for x in v.posed(f"toparm_{st}", m.REST) if "_arc" in x.name]:
            alo, ahi = a.aabb()
            flo, fhi = fin.aabb()
            gap = max(alo[0] - fhi[0], flo[0] - ahi[0], alo[1] - fhi[1], flo[1] - ahi[1])
            worst_rest = min(worst_rest, gap)
        print(f"{st} hold: raised, the finger rests on the arc within {worst_on:.3f}; at rest the arc clears the finger by {worst_rest:.2f}")
        if worst_on > 0.05 or worst_rest < 0.5:
            v.fail(f"{st} hold: the finger does not rest on the raised arc, or the arc at rest is in its way")
    # hold continuity: the rock is fully in from the start of the trip until the outfeed roll lets go,
    # and out exactly at T_end
    for cls, k in (("thin", 1), ("thick", 2)):
        te = m.t_end(k)
        worst_in = 1.0
        first_out = None
        n = int(te * 64)
        for i in range(n + 1):
            T = te * i / n
            e = gauge_fraction(rock_d, full_inputs(m.inputs_of((0.0, 0.0, 0.0, T, k, 1.0))), m.PATH_BUILD)
            if e < 0.999 and first_out is None:
                first_out = T
        e_end = gauge_fraction(rock_d, full_inputs(m.inputs_of((0.0, 0.0, 0.0, te, k, 1.0))), m.PATH_BUILD)
        print(f"hold continuity, {cls}: the rock is fully in from T 0 to {first_out:.3f} of {te:.3f}, and {e_end:.3f} in at T_end")
        if first_out is None or first_out < te - 1.0 or e_end > 1e-9:
            v.fail(f"the feed is not held through a {cls} trunk's trip, or does not stop at its end")


def check_breaker(v):
    """At full occupancy the bars rest on the trunk; at rest they hang below a thin trunk's top; the
    nose arriving lifts them without pressing into it by more than 0.3."""
    m = v.m
    for cls, k in (("thin", 1), ("thick", 2)):
        T = (m.BREAKER_X + 30 - m.NOSE0) / 16
        pen, _ = depth_into_trunk(v, cls, T, v.posed("breaker", (0.0, 0.0, 0.0, T, k, 1.0)), 5)
        worst = -1e9
        w = [x for x in m.breaker_windows() if x["gain"][cls] > 0][0]
        for n in range(41):
            nose = (w["from"] - 0.3 + (w["ease"] + 0.6) * n / 40) * 16
            Tn = (nose - m.NOSE0) / 16
            p2, _ = depth_into_trunk(v, cls, Tn, v.posed("breaker", (0.0, 0.0, 0.0, Tn, k, 1.0)), 4)
            worst = max(worst, p2)
        print(f"breaker, {cls}: the bars rest {-pen:+.2f} off the trunk; the nose lifting them presses in at most {max(worst, 0):.2f}")
        if abs(pen) > 0.15 or worst > 0.3:
            v.fail(f"the breaker bars do not ride a {cls} trunk")
    low = min(e.aabb()[0][1] for e in v.posed("breaker", m.REST) if "_bar_" in e.name)
    print(f"breaker at rest: the bars hang to {low:.2f}, a thin trunk's top is {m.H + m.TRUNK_RADII['thin'][0]:.2f}")
    if low >= m.H + m.TRUNK_RADII["thin"][0]:
        v.fail("the breaker bars do not hang below a thin trunk's top at rest")


# Intended contacts: (part a, element rx a, part b, element rx b); anything else touching is a fault.
def allowed(m):
    out = [
        ("entry", "", "pinion_[we]", ""), ("entry", "", "frame", r"^fr_entry_bearing"), ("entry", "", "main", ""),
        ("pinion_[we]", "", "main", ""), ("main", "", "frame", r"^fr_bearing_main"), ("main", "", "ring", r"iwtooth|_rim"),
        ("main", r"worm_\w+_iwthread", "lay_(in|out)", r"_wheel_iwtooth"), ("ring", "", "ringtyre", ""), ("ring", "", "arm[1-4]", ""), ("ringtyre", "", "roller[1-4]", ""),
        ("ring", "", "roller[1-4]", "flange"), ("roller[1-4]", "", "frame", r"^fr_roller\d_bearing"),
        ("arm([1-4])", "", "tip\\1", ""), ("breaker", "", "frame", r"^fr_breaker_hanger"),
        ("lay_(in|out)", "", "frame", r"^fr_\w+_bearing_lay"), ("cross_(in|out)", "", "frame", r"^fr_\w+_bearing_cross"),
        ("lay_(in|out)", "", "(fast|slow)_\\1", ""), ("(fast|slow)_(in|out)", "", "cross_\\2", ""), ("sel_(in|out)", "", "cross_\\1", ""),
        ("sel_(in|out)", "", "(fast|slow)_\\1", r"_hub"), ("sel_(in|out)", "", "sellever_\\1", r"prong"),
        ("cross_(in|out)", "", "toparm_\\1", r"_boss"), ("cross_(in|out)", "", "toproll_\\1", r"_gear"),
        ("toparm_(in|out)", "", "toproll_\\1", r"_journal|_body"), ("cradle_(in|out)", "", "botroll_\\1", ""),
        ("cradle_(in|out)", r"fulcrum", "sellever_\\1", ""), ("rock", r"_pin_", "sellever_(in|out)", ""),
        ("rock", "", "frame", r"^fr_bearing_rock|^fr_rock_stop"), ("toparm_(in|out)", r"_n_bar", "frame", r"^fr_(in|out)_armstop$"), ("rocker", "", "frame", r"^fr_rocker_bearing"),
        ("rocker", r"_pad_", "cradle_(in|out)", r"cheek_n"), ("cradle_(in|out)", r"_tongue", "frame", r"guide"),
        ("treadle", "", "cradle_in", r"_skid"), ("treadle", "", "treadlever", ""), ("treadlever", "", "frame", r"^fr_treadle_bearing"),
        ("treadlever", r"_pin", "pushrod", ""), ("pushrod", "", "rock", r"_tappet"), ("rock", r"_finger_(in|out)", "toparm_\\1", r"_arc"),
    ]
    return out


def check_clearances(v, poses):
    """Moving parts against each other and the frame over sampled poses: nothing touches except the
    intended contacts (meshing wheels, shafts in their bearings, pins in their slots and lugs)."""
    m = v.m
    rules = allowed(m)

    def ok_pair(pa, na, pb, nb):
        for a, ra, b, rb in rules:
            for (x, nx, y, ny) in ((pa, na, pb, nb), (pb, nb, pa, na)):
                ma = re.fullmatch(a, x)
                mr = re.search(ra, nx) if ra else None
                if not ma or (ra and not mr):
                    continue
                bb = b
                for gi, g in enumerate(ma.groups() + (mr.groups() if mr else ()), 1):
                    bb = bb.replace(f"\\{gi}", g or "")
                if re.fullmatch(bb, y) and (not rb or re.search(rb, ny)):
                    return True
        return False
    hits = {}
    frame = v.by_part["frame"]
    for ps in poses:
        items = []
        for pid in v.by_part:
            if pid in v.copies:
                continue
            src = frame if pid == "frame" else v.posed(pid, ps)
            for el in src:
                items.append((pid, el, el.aabb()))
        grid = {}
        for i, (pid, el, (lo, hi)) in enumerate(items):
            for cx in range(int(lo[0] // 8), int(hi[0] // 8) + 1):
                for cy in range(int(lo[1] // 8), int(hi[1] // 8) + 1):
                    for cz in range(int(lo[2] // 8), int(hi[2] // 8) + 1):
                        grid.setdefault((cx, cy, cz), []).append(i)
        seen = set()
        for cell, idx in grid.items():
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
                    if not all(alo[k] < bhi[k] - 0.02 and blo[k] < ahi[k] - 0.02 for k in range(3)):
                        continue
                    if ok_pair(pa, ea.name, pb, eb.name):
                        continue
                    if obb_obb(ea, eb):
                        key = tuple(sorted((pa, pb)))
                        hits.setdefault(key, set()).add((ea.name, eb.name))
    print(f"clearances over {len(poses)} poses: {'all clear' if not hits else f'{len(hits)} pairs of parts touch'}")
    for key, hs in sorted(hits.items()):
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. {sorted(hs)[:3]}")
    if hits:
        v.fail("parts run into each other")


# arms ride the trunk within check_arms' tolerances; everything else must clear it
INTENDED_TRUNK = re.compile(r"^(toproll_(in|out)_body|botroll_(in|out)_body|cradle_\w+_skid\d|treadle_plate|tip\d_|arm\d_|breaker_bar_)")


def check_swept(v, step=1 / 16, thetas=(0.0,)):
    """Each class's trunk (LE's model, placed as the renderer places it), every 1/16 block from
    waiting to delivered: it touches nothing but its intended contacts (rolls, saddles, treadle,
    tips, breaker bars), which other checks hold to their tolerances; everything else clears it
    by 0.1. Also: the trunk's box stays inside declared cells."""
    m = v.m
    declared = {tuple(c["pos"]) for c in v.rig["cells"]}
    for cls, k in (("thin", 1), ("thick", 2)):
        te = m.t_end(k)
        n = int(round(te / step))
        hits = set()
        cells_out = set()
        frame = v.by_part["frame"]
        for i in range(n + 1):
            T = min(te, i * step)
            tr = trunk_els(v, cls, T)
            tlo, thi = aabb_of(tr)
            for c in cells_touched(tlo, thi, 0.05):
                if c not in declared:
                    cells_out.add(c)
            for th in thetas:
                ps = (th, abs(th) + 2.0, 10.0, T, k, 1.0)
                for pid in v.by_part:
                    if pid in v.copies:
                        continue
                    src = frame if pid == "frame" else v.posed(pid, ps)
                    for el in src:
                        if INTENDED_TRUNK.match(el.name):
                            continue
                        lo, hi = el.aabb()
                        if not all(lo[q] < thi[q] + 0.1 and tlo[q] < hi[q] + 0.1 for q in range(3)):
                            continue
                        for t in tr:
                            a, b2 = t.aabb()
                            if all(lo[q] < b2[q] + 0.1 and a[q] < hi[q] + 0.1 for q in range(3)) and obb_obb(el, t, eps=-0.05):
                                hits.add(el.name)
                                break
        print(f"swept path, {cls}: T 0..{te:.3f} in {n + 1} steps: "
              + ("touches nothing but its contacts" if not hits else f"HITS {len(hits)}: {', '.join(sorted(hits)[:10])}")
              + ("" if not cells_out else f"; trunk OUTSIDE cells {sorted(cells_out)[:6]}"))
        if hits:
            v.fail(f"a {cls} trunk's path is blocked")
        if cells_out:
            v.fail(f"a {cls} trunk leaves the declared cells")


def check_anchors(v):
    """The power and water cells are cells, the cells beyond their faces are outside the footprint,
    differ from each other, from the chute's cell and from the racks' cells at both ends; the power
    cell is not at ground level; a mill in line keeps its power feed cell free."""
    m = v.m
    rig = v.rig
    cells = {tuple(c["pos"]) for c in rig["cells"]}
    face = {"north": (0, 0, -1), "south": (0, 0, 1)}
    pc, wc = tuple(rig["powerCell"]), tuple(rig["waterCell"])
    beyond_p = tuple(pc[i] + face[rig["powerFace"]][i] for i in range(3))
    beyond_w = tuple(wc[i] + face[rig["waterFace"]][i] for i in range(3))
    chute = tuple(int(math.floor(rig["chute"]["pos"][i])) for i in range(3))
    racks = {(-1, 0, z) for z in range(m.BED_CELLS_Z[0], m.BED_CELLS_Z[1] + 1)} | {(m.CELLS_X, 0, z) for z in range(m.BED_CELLS_Z[0], m.BED_CELLS_Z[1] + 1)}
    print(f"anchors: power {pc} ({rig['powerFace']}), beyond {beyond_p}; water {wc} ({rig['waterFace']}), beyond {beyond_w}; chute cell {chute}")
    if pc not in cells or wc not in cells or pc == wc:
        v.fail("the power or water cell is not a cell of its own")
    if pc[1] == 0:
        v.fail("the power cell is at ground level")
    if beyond_p in cells or beyond_w in cells or beyond_p == beyond_w or {beyond_p, beyond_w} & racks or chute in (beyond_p, beyond_w):
        v.fail("the cells beyond the power and water faces clash")
    # the axle reaches the entry shaft through the power face; the drip's inlet meets the pipe beyond the
    # water face end to end: it reaches the face, on the middle of the cell's face, in ppex's section
    entry_end = min(e.aabb()[0][2] for e in v.by_part["entry"])
    inlet = [e for e in v.by_part[f"pipe{m.PIPE_METALS[0]}"] if e.name.startswith(f"pipe{m.PIPE_METALS[0]}_inlet")]
    ilo, ihi = aabb_of(inlet)
    middle = [(wc[k] + 0.5) * 16 for k in (0, 1)]
    off = max(abs((ilo[k] + ihi[k]) / 2 - middle[k]) for k in (0, 1))
    across = [ihi[k] - ilo[k] for k in (0, 1)]
    print(f"  the entry shaft starts {entry_end:.2f} from the north face; the drip's inlet ends {m.CELLS_Z * 16 - ihi[2]:.2f} from the south face, "
          f"{off:.2f} off the middle of the water cell's face, {across[0]:g} x {across[1]:g} across")
    if entry_end > 0.05 or m.CELLS_Z * 16 - ihi[2] > 0.05:
        v.fail("the entry shaft or the drip's inlet does not reach its face")
    if off > 0.01 or any(abs(a - 2 * m.PIPE_HALF) > 0.01 for a in across):
        v.fail("the drip's inlet does not meet a pipe on the water face end to end")
    # the mill in line: its controller 6 blocks east of ours on the same line, its power feed cell [-6,3,0] = our east end's top
    mill_feed = (m.ORIGIN_CELL[0], 3, m.ORIGIN_CELL[2])
    print(f"  a mill in line: its bed centreline z {16 * 0.6875:.2f} (ours {m.TZ:.2f} in the controller's column); "
          f"its power feed cell is our {mill_feed}: {'free' if mill_feed not in cells else 'TAKEN'}; "
          f"trunk axis heights: ours {m.H / 16:.4f} blocks, the mill's thick {0.5 + m.TRUNK_RADII['thick'][0] / 16:.4f}, thin {0.5 + m.TRUNK_RADII['thin'][0] / 16:.4f}")
    if mill_feed in cells or abs(m.TZ - (m.ORIGIN_CELL[2] * 16 + 11.0)) > 1e-9:
        v.fail("a mill in line is not on our trunk line, or its power feed cell is ours")


IRON = re.compile(r"^(tip\d|arm\d|roller\d|ringtyre|toproll_(in|out)_(body|journal)|botroll_(in|out)_(body|journal)|worm_|sel_|sellever_|rock_|rocker_|"
                  r"treadlever|pushrod|treadle_stem|breaker_|ring_pin|ring_spring|fr_drip|cradle_\w+_(upright|fulcrum))")
OAK = re.compile(r"^(fr_post|fr_sill|fr_toprail|fr_cross|fr_topbeam|fr_\w+_shaftbeam|fr_chute|cradle_\w+_(skid|bearer|cheek|tongue|crossbar)|toparm_\w+_(n|s)_bar|treadle_plate)")


def check_textures(v):
    """(review) Iron for pins, wearing surfaces and linkage (the feed rolls and the scraper tips the
    trunk runs on, the worms, the selector dogs, every lever), oak for structure."""
    bad = []
    for el in v.els:
        tex = {f["texture"] for f in el.faces.values()}
        if IRON.match(el.name) and tex - {"#metal"}:
            bad.append(f"{el.name} (should be iron)")
        if OAK.match(el.name) and tex - {"#oak"}:
            bad.append(f"{el.name} (should be oak)")
        if el.part.startswith("pipe") and tex != {"#" + el.part}:
            bad.append(f"{el.name} (should be {el.part[4:]} pipe)")
        elif not el.part.startswith("pipe") and any(t.startswith("#pipe") for t in tex):
            bad.append(f"{el.name} (wears a pipe's metal)")
    print(f"textures: {'iron, oak and the pipes in their metals where they belong' if not bad else 'WRONG: ' + ', '.join(bad[:8])}")
    if bad:
        v.fail("an element has the wrong material")


def check_pipes(v):
    """The drip's pipes: one part per metal the rosser takes, each the first metal's elements
    exactly (the same boxes and faces, renamed, in its own texture), so whichever is fitted draws
    the same pipes; the header lies across the whole width a thin trunk's top takes."""
    m = v.m
    first = sorted(v.by_part[f"pipe{m.PIPE_METALS[0]}"], key=lambda e: e.name)
    bad = []
    for metal in m.PIPE_METALS[1:]:
        pid = f"pipe{metal}"
        copies = sorted(v.by_part.get(pid, []), key=lambda e: e.name)
        if len(copies) != len(first):
            bad.append(f"{pid} has {len(copies)} elements, {len(first)} wanted")
            continue
        for a, b in zip(first, copies):
            faces = {d: {**f, "texture": "#" + pid} for d, f in a.faces.items()}
            if (b.name != pid + a.name[len(first[0].part):] or b.size != a.size or b.c != a.c or b.r != a.r
                    or b.faces != faces):
                bad.append(f"{b.name} is not {a.name}")
    head = [e for e in first if "_header" in e.name]
    hlo, hhi = aabb_of(head)
    thin = m.TRUNK_RADII["thin"][1]
    print(f"pipes: {len(first)} elements a metal in {', '.join(m.PIPE_METALS)}; the header z {hlo[2]:.1f}..{hhi[2]:.1f} "
          f"(a thin trunk's corners {m.TZ - thin:.1f}..{m.TZ + thin:.1f}), its underside {hlo[1]:.2f}"
          + ("" if not bad else "; WRONG: " + ", ".join(bad[:6])))
    if bad:
        v.fail("the pipes are not the same in every metal")
    if hlo[2] > m.TZ - thin or hhi[2] < m.TZ + thin:
        v.fail("the drip's header does not span a thin trunk")


def check_chute(v):
    """(review) The chute is built: its boards fall to the south face at the mouth, under the
    anchor, and nothing else of the frame blocks the mouth."""
    m = v.m
    boards = [e for e in v.by_part["frame"] if re.match(r"^fr_chute\d_board", e.name)]
    anchor = [c * 16 for c in v.rig["chute"]["pos"]]
    reach = [max(e.aabb()[1][2] for e in boards)] if boards else [0.0]
    ends_low = all(e.aabb()[0][1] < m.CHUTE_MOUTH_Y for e in boards)
    under = any(e.aabb()[0][0] <= anchor[0] <= e.aabb()[1][0] for e in boards)
    lo, hi = [m.CHUTE_X[0], 0.0, m.CELLS_Z * 16 - 4.0], [m.CHUTE_X[1], anchor[1], m.CELLS_Z * 16]
    from machinegen.checks import obb_overlap
    blocking = [e.name for e in v.by_part["frame"] if not e.name.startswith("fr_chute") and obb_overlap(e, lo, hi)]
    print(f"chute: {len(boards)} boards reaching z {reach[0]:.2f}, {'low at the mouth' if ends_low else 'NOT low'}, "
          f"{'under' if under else 'NOT under'} the anchor {[round(a, 2) for a in anchor]}; the mouth is {'clear' if not blocking else 'BLOCKED by ' + ', '.join(blocking)}")
    if len(boards) < 2 or reach[0] < m.CELLS_Z * 16 - 0.5 or not ends_low or not under or blocking:
        v.fail("the chute does not carry bark and sticks out of its mouth")


def check_stops(v):
    """(review) Every rest has a visible stop: the top-roll arms hang on their brackets with no trunk,
    and the rock shaft's tappet arm meets its stop at the end of the throw (out of it while out)."""
    m = v.m
    for st in m.STATIONS:
        bar = [e for e in v.posed(f"toparm_{st}", m.REST) if e.name == f"toparm_{st}_n_bar"][0]
        stop = [e for e in v.by_part["frame"] if e.name == f"fr_{st}_armstop"][0]
        (x0, x1), top, _ = m.arm_stop(st)
        # the bar's outline seen along z (a rectangle in x-y): its lowest point over the stop's x span
        cs = [(p[0], p[1]) for p in bar.corners()]
        edges = [(a, b2) for a in cs for b2 in cs if a != b2]
        lows = [y for x, y in cs if x0 <= x <= x1]
        for xs in (x0, x1):
            for a, b2 in edges:
                if min(a[0], b2[0]) <= xs <= max(a[0], b2[0]) and abs(b2[0] - a[0]) > 1e-9:
                    lows.append(a[1] + (b2[1] - a[1]) * (xs - a[0]) / (b2[0] - a[0]))
        gap = min(lows) - stop.aabb()[1][1] if lows else 9.0
        print(f"{st} top-roll arms at rest: {gap:+.3f} above their stop")
        if abs(gap) > 0.08:
            v.fail(f"the {st} top-roll arms do not rest on their stop")
    tap_in = [e for e in v.posed("rock", (0.0, 0.0, 0.0, 0.0, 1, 1.0)) if e.name == "rock_tappet"][0]
    tap_out = [e for e in v.posed("rock", m.REST) if e.name == "rock_tappet"][0]
    stop = [e for e in v.by_part["frame"] if e.name == "fr_rock_stop"][0]
    s_lo, s_hi = stop.aabb()

    def top_under(el):
        pts = [p for p in m.samples(el, 21) if s_lo[2] - 0.01 <= p[2] <= s_hi[2] + 0.01]
        return max(p[1] for p in pts)
    gap_in = s_lo[1] - top_under(tap_in)
    gap_out = s_lo[1] - top_under(tap_out)
    print(f"rock shaft: in, its tappet arm is {gap_in:+.3f} under its stop; out, {gap_out:+.2f}")
    if abs(gap_in) > 0.08 or gap_out < 0.5:
        v.fail("the rock shaft's throw has no stop")


def check_le(v):
    """LE's models are what the rig's radii and the arms' fit were made from."""
    m = v.m
    for cls in ("thin", "thick"):
        pts = m.SECTIONS[cls]
        flats = min(m.support(pts, a) for a in (0.0, math.pi / 2, math.pi, 3 * math.pi / 2))
        flats_v = min(m.support(pts, a) for a in (0.0, math.pi))
        corners = max(m.support(pts, math.pi / 4 + j * math.pi / 2) for j in range(4))
        print(f"LE {m.SHOWN[cls]}: flats {flats_v:.2f} (top and bottom), corners {corners:.2f}; the rig says {m.TRUNK_RADII[cls]}")
        if abs(flats_v - m.TRUNK_RADII[cls][0]) > 0.05:
            v.fail(f"LE's {m.SHOWN[cls]} model changed: the trunk radii and the beds no longer fit")


def validate(m, els, parts, rig, le, quick=False):
    v = V(m, els, parts, rig, le)
    check_basic(v)
    check_le(v)
    te = {k: m.t_end(k) for k in (1, 2)}
    motion = [m.REST, pose(th=1.3), pose(th=-2.2, ps=9.0)]
    for k in (1, 2):
        for f in (0.0, 0.1, 0.25, 0.4, 0.55, 0.7, 0.85, 1.0):
            for th in (0.0, 0.9):
                motion.append((th, 3.0 + th, 7.0 * f, te[k] * f, k, 1.0))
        motion.append((0.2, 0.2, 0.0, 0.0, k, 0.5))
    check_containment(v, motion)
    check_anchors(v)
    check_gearing(v)
    check_ring(v)
    check_rolls(v)
    check_cradle(v)
    check_levers(v)
    check_breaker(v)
    check_floating(v)
    check_bearings(v)
    check_textures(v)
    check_pipes(v)
    check_chute(v)
    check_stops(v)
    if not quick:
        check_zfight(v)
        check_arms(v)
        check_clearances(v, clearance_poses(m))
        check_swept(v)
    return v.ok


def clearance_poses(m):
    out = [m.REST, pose(th=0.7, ps=0.7), pose(th=-1.9, ps=5.0), pose(th=2.6, ps=11.1)]
    for k in (1, 2):
        te = m.t_end(k)
        wins = set()
        for p in m.rig_parts():
            for d in p["drivers"]:
                for w in d.get("windows", []):
                    if w["gain"][CLASSES[k]] == 0:
                        continue
                    for edge in (w["from"] - m.NOSE0 / 16, w["to"] - m.NOSE0 / 16 + m.LENGTHS[CLASSES[k]]):
                        for off in (-w["ease"] / 2, w["ease"] / 2):
                            t = edge + off
                            if 0 <= t <= te:
                                wins.add(round(t, 4))
        ts = sorted(wins | {round(te * i / 8, 4) for i in range(9)})
        for i, T in enumerate(ts):
            out.append((0.3 * i, 1.0 + 0.7 * i, 2.0 + 1.3 * i, T, k, 1.0))
        out.append((0.5, 0.5, 0.0, 0.0, k, 0.5))
    return out


def validate_files(m, shape, frame_shape, ship):
    ok = True
    for sh, label in ((shape, "full"), (frame_shape, "frame")):
        used = {f["texture"].lstrip("#") for e in sh["elements"] for f in e["faces"].values()}
        missing = used - set(sh["textures"])
        print(f"{label} shape: {len(sh['elements'])} elements, textures used {sorted(used)}")
        if missing:
            print(f"FAIL {label} shape uses undeclared textures {missing}")
            ok = False
    # a lid on every station column's top cell, so nothing falls into the rosser from above, and none over the beds
    gaps = lid_gaps(ship["cells"], m.shipped_station_column)
    decks = sorted({c["lid"] + c["pos"][1] for c in ship["cells"] if "lid" in c})
    print(f"lids: {sum(1 for c in ship['cells'] if 'lid' in c)}, decks at y {decks}")
    if gaps:
        print(f"FAIL station columns with no lid on their top cell, or bed columns with one: {gaps}")
        ok = False
    return ok
