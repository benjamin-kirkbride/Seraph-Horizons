"""The rocker generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
from machinegen.rigmath import apply as _apply
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

    def mat(self, pid, theta):
        return self.m.pm(self.parts, pid, theta)

    def posed(self, pid, theta):
        key = (pid, theta)
        if key not in self.cache:
            if len(self.cache) > 40000:
                self.cache.clear()
            mm = self.mat(pid, theta)
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def point(self, pid, theta, p):
        q = _apply(self.mat(pid, theta), [v / 16 for v in p])
        return [v * 16 for v in q]

    def named(self, pid, rx, theta=None):
        src = self.posed(pid, theta) if theta is not None else self.by_part.get(pid, [])
        r = re.compile(rx)
        return [e for e in src if r.search(e.name)]


SPOUT_CLEAR = 0.3                            # voxels: the least gap between what rocks and the fixed spout


def check_basic(v):
    worst = euler_round_trip(v.els)
    print(f"euler round trip: worst {worst:.1e}")
    if worst > 1e-9:
        v.fail("an element's rotation does not survive being written as Euler angles")
    names = [el.name for el in v.els]
    seen, dup = set(), set()
    for n in names:
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
    rides = {p["id"]: p["ride"] for p in v.parts}
    for pid in v.m.MOVING:
        if pid != "cradle" and rides.get(pid) != "cradle":
            v.fail(f"{pid} does not ride the cradle")
    for pid in v.m.STATIC:
        p = next(q for q in v.parts if q["id"] == pid)
        if p["ride"] or p["drivers"]:
            v.fail(f"{pid} moves; it is fixed")


def check_containment(v, thetas):
    m = v.m
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where = -1e9, None
    for th in thetas:
        for pid in v.by_part:
            for el in v.posed(pid, th):
                lo, hi = el.aabb()
                o = box_overhang(lo, hi, box)
                if o > worst:
                    worst, where = o, (el.name, th)
    print(f"containment over {len(thetas)} poses of the rock: worst overhang {worst:.3f} voxels ({where[0]} at theta {where[1]:.3f})")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the cell at theta {where[1]:.3f}")


def check_floating(v):
    """The frame and the spout stand on the ground; everything that rocks hangs together on the rockers."""
    m = v.m
    fixed = v.by_part["frame"] + v.by_part["spout"]
    seen, loose = frame_floating(fixed)
    print(f"fixed: {len(seen)} elements of the sills and the spout joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"fixed elements float: {loose[:8]}")
    rocking = [e for pid in m.MOVING for e in v.posed(pid, m.REST)]
    seen, loose = frame_floating(rocking, ground=m.SILL_TOP + 1e-6)
    print(f"rocking: {len(seen)} elements joined to the rockers' running faces, {len(loose)} not")
    if loose:
        v.fail(f"rocking elements float: {loose[:8]}")


ROLES = [
    # (element regex, the textures it may wear)
    (r"^cradle_", {"oak"}),
    (r"^handle_stick", {"oak"}),
    (r"^handle_band", {"iron"}),
    (r"^riffle_", {"oak"}),
    (r"^apron_(rail|bar)", {"oak"}),
    (r"^apron_canvas", {"canvas"}),
    (r"^hopper_", {"oak"}),
    (r"^riddle_", {"riddle"}),
    (r"^(water|stream)_", {"water"}),
    (r"^charge_", {"charge"}),
    (r"^conc_", {"concentrate"}),
    (r"^spout_(union|inlet|run|riser|arm|nozzle)", {"pipe"}),
    (r"^spout_post", {"oak"}),
    (r"^spout_clip", {"iron"}),
    (r"^fr_sill", {"oak"}),
]


def check_textures(v):
    bad = []
    for el in v.els:
        tex = {f["texture"].lstrip("#") for f in el.faces.values()}
        rule = next((want for rx, want in ROLES if re.match(rx, el.name)), None)
        if rule is None or not tex <= rule:
            bad.append((el.name, sorted(tex), sorted(rule or [])))
    clear = [el.name for el in v.els if el.part in ("water", "stream") and el.render_pass != v.m.TRANSPARENT]
    print(f"textures by role: {len(v.els) - len(bad)} of {len(v.els)} elements as their role says; water in the Transparent "
          f"pass: {'all' if not clear else clear}")
    if bad:
        v.fail(f"textures off their role: {bad[:6]}")
    if clear:
        v.fail(f"water elements not in the Transparent pass: {clear[:6]}")


def angle_x(mat):
    """A part matrix's turn about x (degrees), from the turned y axis."""
    return math.degrees(math.atan2(mat[2][1], mat[1][1]))


def check_rock(v):
    """The cradle is level at theta 0 and pi and leans ROCK_DEG south at pi/2 and north at 3 pi/2; every part
    that rocks moves as the cradle does, the handle among them; nothing fixed moves."""
    m = v.m
    want = {0.0: 0.0, math.pi / 2: m.ROCK_DEG, math.pi: 0.0, 3 * math.pi / 2: -m.ROCK_DEG}
    for th, deg in want.items():
        got = angle_x(v.mat("cradle", th))
        if abs(got - deg) > 1e-3:
            v.fail(f"the cradle is at {got:.4f} degrees at theta {th:.4f}, want {deg}")
    worst = 0.0
    for th in m.sample_thetas(15.0):
        c = v.mat("cradle", th)
        for pid in m.MOVING:
            a = v.mat(pid, th)
            worst = max(worst, max(abs(a[i][j] - c[i][j]) for i in range(3) for j in range(4)))
        for pid in m.STATIC:
            a = v.mat(pid, th)
            worst = max(worst, max(abs(a[i][j] - (1.0 if i == j else 0.0)) for i in range(3) for j in range(4)))
    print(f"rock: level at 0 and pi, {m.ROCK_DEG:g} degrees south at pi/2 and north at 3 pi/2; every rocking part with the "
          f"cradle and every fixed part still, within {worst:.1e}")
    if worst > 1e-9:
        v.fail("a part does not move with the cradle, or a fixed part moves")


def check_rolling(v):
    """The rockers roll on the sills without slipping: the running faces' corners lie on their circle about the
    rocking axis; as it rocks that axis stays at its height and moves south by the radius times the angle; each
    rocker's lowest point is on its sill (never below its top, never more than a facet's sagitta above it), over
    the sill's length."""
    m = v.m
    angles = m.facet_angles()
    radial = 0.0
    for name in ("foot", "head"):
        for f in v.named("cradle", rf"_rocker_{name}_facet"):
            lo_face = [c for c in f.corners()]
            # the two lowest corners of a facet are its chord's ends: on the circle
            lo_face.sort(key=lambda p: math.hypot(p[1] - m.AXIS_Y, p[2] - m.AXIS_Z), reverse=True)
            for p in lo_face[:4]:
                radial = max(radial, abs(math.hypot(p[1] - m.AXIS_Y, p[2] - m.AXIS_Z) - m.ROCKER_R))
    sag = m.facet_gap()
    worst_axis, worst_low, worst_high, off_sill = 0.0, 1e9, -1e9, []
    for th in m.sample_thetas(1.0):
        a = math.radians(angle_x(v.mat("cradle", th)))
        c = v.point("cradle", th, [8.0, m.AXIS_Y, m.AXIS_Z])
        worst_axis = max(worst_axis, abs(c[1] - m.AXIS_Y), abs(c[2] - (m.AXIS_Z + m.ROCKER_R * a)))
        for name, xr in (("foot", m.ROCKER_X[0]), ("head", m.ROCKER_X[1])):
            low = min((p for f in v.named("cradle", rf"_rocker_{name}_facet", th) for p in f.corners()), key=lambda p: p[1])
            gap = low[1] - m.SILL_TOP
            worst_low, worst_high = min(worst_low, gap), max(worst_high, gap)
            if not (m.SILL_Z[0] < low[2] < m.SILL_Z[1] and abs(low[0] - xr) < m.SILL_HALF_X):
                off_sill.append((name, round(th, 3)))
    print(f"rolling: running-face corners on their circle within {radial:.1e}; the rocking axis at its height and "
          f"{m.ROCKER_R:g} x the angle south within {worst_axis:.1e}; rockers {worst_low:.4f}..{worst_high:.4f} over their "
          f"sills (a facet's sagitta {sag:.4f}); the rock's travel {m.ROCKER_R * m.ROCK_DEG * m.DEG:.2f} each way, "
          f"{len(angles) - 1} facets")
    if radial > 1e-6 or worst_axis > 1e-4:
        v.fail("the rockers do not roll on their circle")
    if worst_low < -1e-6 or worst_high > sag + 1e-6:
        v.fail("a rocker sinks into its sill or lifts off it")
    if off_sill:
        v.fail(f"a rocker leaves its sill: {off_sill[:4]}")


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels at every sampled pose.
ALLOWED = [
    ("handle", None, "cradle|hopper", None),
    ("riffles|apron|hopper", None, "cradle", None),
    ("riddle", None, "hopper|cradle", None),
    # the contents: water against everything of the cradle it runs over or stands in, and the stream into it
    ("water", None, "cradle|riffles|apron|hopper|riddle|charge|concentrate", None),
    ("charge", None, "hopper|riddle", None),
    ("concentrate", None, "cradle|riffles", None),
    ("stream", None, "water|charge", None),
    ("stream", None, "spout", r"_nozzle"),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, theta, pairs_of):
    items = [(pid, el, el.aabb()) for pid in v.by_part for el in v.posed(pid, theta)]
    hits = {}
    for i in range(len(items)):
        pa, ea, (alo, ahi) = items[i]
        for j in range(i + 1, len(items)):
            pb, eb, (blo, bhi) = items[j]
            if pa == pb or not pairs_of(pa, pb):
                continue
            if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                continue
            if allowed(pa, ea.name, pb, eb.name):
                continue
            if obb_obb(ea, eb):
                hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def check_clearances(v, thetas, label, pairs_of):
    allhits = {}
    for th in thetas:
        for key, hs in touching_pairs(v, th, pairs_of).items():
            allhits.setdefault(key, {})
            for h in hs:
                allhits[key].setdefault(h, th)
    print(f"{label} over {len(thetas)} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {t:.3f}" for (a, b), t in ex))
    if allhits:
        v.fail("parts run into each other")


def sat_gap(a, b):
    """The separation of two boxes along their best separating axis (voxels): the distance between them when
    a face separates them, a lower bound otherwise; negative when they overlap."""
    ah = [abs(x) / 2 for x in a.size]
    bh = [abs(x) / 2 for x in b.size]
    aa = [[a.r[i][k] for i in range(3)] for k in range(3)]
    ba = [[b.r[i][k] for i in range(3)] for k in range(3)]
    d = [a.c[k] - b.c[k] for k in range(3)]
    axes = aa + ba
    for u in aa:
        for w in ba:
            x = [u[1] * w[2] - u[2] * w[1], u[2] * w[0] - u[0] * w[2], u[0] * w[1] - u[1] * w[0]]
            n = math.sqrt(sum(c * c for c in x))
            if n > 1e-6:
                axes.append([c / n for c in x])
    best = -1e9
    for ax in axes:
        dist = abs(sum(d[k] * ax[k] for k in range(3)))
        ra = sum(ah[k] * abs(sum(aa[k][i] * ax[i] for i in range(3))) for k in range(3))
        rb = sum(bh[k] * abs(sum(ba[k][i] * ax[i] for i in range(3))) for k in range(3))
        best = max(best, dist - ra - rb)
    return best


def least_gap(v, thetas, moving, fixed):
    """The least separation (voxels) between the posed `moving` parts' elements and the `fixed` parts' over
    `thetas`; for the report."""
    best, where = 1e9, None
    fx = [(e, e.aabb()) for pid in fixed for e in v.by_part[pid]]
    for th in thetas:
        for pid in moving:
            for e in v.posed(pid, th):
                lo, hi = e.aabb()
                for f, (flo, fhi) in fx:
                    if max(max(flo[k] - hi[k], lo[k] - fhi[k]) for k in range(3)) > best:
                        continue
                    g = sat_gap(e, f)
                    if g < best:
                        best, where = g, (e.name, f.name, th)
    return best, where


def check_stream(v):
    """The stream falls into the hopper at every point of the rock: each point down its middle lies inside the
    riddle plate's square, clear of the walls by the stream's half width and REACH more, and its foot stands in
    the water on the plate, above the plate."""
    m = v.m
    (x0, x1), (z0, z1), ry = m.riddle_box()
    reach = 0.3
    mx, my, mz = m.nozzle_mouth()
    worst_wall, foot_lo, foot_hi = 1e9, 1e9, -1e9
    for th in m.sample_thetas(2.0):
        cm = v.mat("cradle", th)
        r = [row[:3] for row in cm[:3]]
        t = [cm[i][3] * 16 for i in range(3)]
        for k in range(9):
            y = m.STREAM_FOOT + (my - m.STREAM_FOOT) * k / 8
            w = [mx - t[0], y - t[1], mz - t[2]]
            local = [sum(r[j][i] * w[j] for j in range(3)) for i in range(3)]      # the transpose: the inverse turn
            p = m.untilt_point(local)
            if p[1] < m.SIDE_TOP + m.HOP_H:
                worst_wall = min(worst_wall, p[0] - x0, x1 - p[0], p[2] - z0, z1 - p[2])
            if k == 0:
                foot_lo, foot_hi = min(foot_lo, p[1] - ry), max(foot_hi, p[1] - ry)
    print(f"stream: inside the hopper's walls by {worst_wall:.2f} at worst over the rock; its foot {foot_lo:.2f}..{foot_hi:.2f} "
          f"over the plate (the water stands {m.HOP_WATER:g})")
    if worst_wall < m.STREAM_HALF + reach or foot_lo < 0.1 or foot_hi > m.HOP_WATER:
        v.fail("the stream misses the hopper, or does not reach its water")


def check_path(v):
    """The material's path, in the level frame: the apron's high end reaches under the hopper's foot wall, so
    all that falls through the riddle plate lands on it, clear of the plate; it falls towards the head and
    ends short of the head board, so what it carries drops onto the floor's head; the riffles stand on the
    floor below the apron's high end and the open foot, in the water's way down the floor."""
    m = v.m
    (rx0, rx1), _, ry = m.riddle_box()
    hi, lo = m.APRON_HI, m.APRON_LO
    head_in = m.BOX_X[1] - m.HEAD_T
    under = (ry - m.RIDDLE_T) - hi[1]
    drop = head_in - lo[0]
    print(f"path: the apron from x {hi[0]:g} (the riddle plate from {rx0:g}) {under:.2f} under the plate, falling "
          f"{math.degrees(math.atan2(hi[1] - lo[1], lo[0] - hi[0])):.1f} degrees to x {lo[0]:g}, {drop:.2f} short of the head "
          f"board and {lo[1] - m.FLOOR_TOP:.2f} over the floor; riffles at {list(m.RIFFLE_X)}")
    if not (hi[0] < rx0 and under >= 0.2 and lo[0] > hi[0] and lo[1] < hi[1] and drop >= 0.5 and lo[1] - m.FLOOR_TOP >= 1.0):
        v.fail("the apron does not catch what falls through the riddle and carry it to the head of the box")
    if not all(m.BOX_X[0] + 1.0 < x and x + m.RIFFLE_W < lo[0] for x in m.RIFFLE_X):
        v.fail("a riffle is off the floor's run")
    if any(x < hi[0] and not (rx0 - x > 1.0) for x in m.RIFFLE_X):
        v.fail("a riffle crowds the apron's high end")


def check_handle(v):
    m = v.m
    stick = v.named("handle", r"_stick")[0]
    lo, hi = stick.aabb()
    side = next(e for e in v.by_part["cradle"] if e.name == "cradle_side_n")
    slo, shi = side.aabb()
    print(f"handle: its top at {hi[1]:.2f} (a hand's height {m.HANDLE_TOP:g}), against the north side at z {hi[2]:.2f}, "
          f"{hi[1] - shi[1]:.2f} over the side's top")
    if abs(hi[1] - m.HANDLE_TOP) > 1e-6 or abs(hi[2] - slo[2]) > 1e-6 or not obb_obb(stick, side, eps=-0.03):
        v.fail("the handle is not on the north side, at a hand's height")


def check_foot(v):
    """The foot is open: nothing of the cradle west of the floor's lip over the floor, so the tailings and the water
    leave over it; the tailings land beyond it, in the cell west of the rocker, on the ground."""
    m = v.m
    lip = m.tilt_point([m.BOX_X[0], m.FLOOR_TOP, m.AXIS_Z])
    blocking = []
    for pid in ("cradle", "riffles", "apron", "hopper", "riddle", "handle"):
        for e in v.by_part[pid]:
            lo, hi = e.aabb()
            if lo[0] < lip[0] - 1e-6 and hi[1] > lip[1] + 0.05 and lo[2] < m.IN_Z[1] - 1e-6 and hi[2] > m.IN_Z[0] + 1e-6:
                blocking.append(e.name)
    t = v.rig["tailings"]["pos"]
    print(f"foot: open over the lip at x {lip[0]:.2f}, y {lip[1]:.2f}; the tailings land at {t} ({v.rig['tailingsSide']})")
    if blocking:
        v.fail(f"the foot is not open: {blocking[:4]}")
    if not (t[0] < 0 and t[1] == 0.0 and 0 < t[2] < 1 and v.rig["tailingsSide"] == "west"):
        v.fail("the tailings do not land west of the foot, on the ground")


def check_anchors(v):
    """The water face is the cell's south face and the inlet ends on it, on the ppex pipe's axis, inside its section; the
    spout point is the nozzle's mouth; the points that ride the cradle name it."""
    m = v.m
    rig = v.rig
    p0, p1 = m.PPEX_PIPE
    ends = [e for e in v.by_part["spout"] if abs(e.aabb()[1][2] - m.FACE) < 1e-9]
    for e in ends:
        lo, hi = e.aabb()
        mid = ((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2)
        off = max(abs(mid[0] - m.PIPE_AXIS[0]), abs(mid[1] - m.PIPE_AXIS[1]))
        if not (p0 <= lo[0] and hi[0] <= p1 and p0 <= lo[1] and hi[1] <= p1) or off > 1e-9:
            v.fail(f"{e.name} meets the south face off the ppex pipe's axis or outside its section")
    if rig["waterCell"] != [0, 0, 0] or rig["waterFace"] != "south" or [e.name for e in ends] != ["spout_union"]:
        v.fail("the inlet does not end on the south face")
    mouth = [c / 16 for c in m.nozzle_mouth()]
    if max(abs(a - b) for a, b in zip(mouth, rig["spout"]["pos"])) > 1e-6:
        v.fail("the spout point is not the nozzle's mouth")
    nozzle = next(e for e in v.by_part["spout"] if e.name == "spout_nozzle")
    if abs(nozzle.aabb()[0][1] - m.nozzle_mouth()[1]) > 1e-6:
        v.fail("the nozzle's mouth is not where the spout point says")
    ids = {p["id"] for p in v.parts}
    for key in m.POINT_ANCHORS:
        part = rig[key].get("part")
        if part is not None and part not in ids:
            v.fail(f"the {key} point rides {part}, which is no part")
    print(f"anchors: water {rig['waterCell']} {rig['waterFace']} (the inlet's end on ppex's {p1 - p0:g}-square pipe's axis), "
          f"spout {rig['spout']['pos']}, hopper {rig['hopper']['pos']}, outflow {rig['outflow']['pos']}, "
          f"concentrate {rig['concentrate']['pos']}")


def check_zfight(v):
    m = v.m
    bad = 0
    for th in m.coplanar_poses():
        els = [e for pid in v.by_part for e in v.posed(pid, th)]
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at theta {th:.3f}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses")
    if bad:
        v.fail("faces z-fight")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_floating(v)
    check_textures(v)
    check_containment(v, m.sample_thetas(5.0))
    check_rock(v)
    check_rolling(v)
    check_stream(v)
    check_path(v)
    check_handle(v)
    check_foot(v)
    check_anchors(v)
    check_clearances(v, [m.REST, math.pi / 2, 3 * math.pi / 2], "clearances, every part", lambda a, b: True)
    moving, fixed = set(m.MOVING), set(m.STATIC)
    sweep = (lambda a, b: (a in moving) != (b in moving))
    if not quick:
        check_clearances(v, m.sample_thetas(2.0), "swept rock (every 2 degrees of theta), rocking against fixed", sweep)
        check_zfight(v)
    gap, where = least_gap(v, m.sample_thetas(5.0), [p for p in m.MOVING if p != "water"], ["spout"])
    print(f"least gap between what rocks and the spout: {gap:.2f} voxels ({where[0]} to {where[1]} at theta {where[2]:.3f})")
    if gap < SPOUT_CLEAR:
        v.fail(f"what rocks passes within {gap:.2f} of the spout; keep {SPOUT_CLEAR} clear")
    return v.ok


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
    if gaps or hollow:
        ok = False
    if "powerCell" in ship or "powerFace" in ship:
        print("FAIL a hand station has no power cell")
        ok = False
    passes = {e["name"]: e.get("renderPass") for e in shape["elements"]}
    if any(n.startswith(("water_", "stream_")) and p != m.TRANSPARENT for n, p in passes.items()):
        print("FAIL the water lost its Transparent pass in the file")
        ok = False
    return ok
