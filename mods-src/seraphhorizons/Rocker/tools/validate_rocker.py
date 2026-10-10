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
    drivers = {p["id"]: p["drivers"] for p in v.parts}
    for pid in v.m.MOVING:
        if pid != "cradle" and rides.get(pid) != "cradle":
            v.fail(f"{pid} does not ride the cradle")
    for pid in v.m.RIGID:
        if pid != "cradle" and drivers.get(pid):
            v.fail(f"{pid} has drivers of its own; it rocks as the cradle does")
    for pid in v.m.LEVEL:
        if [d["type"] for d in drivers.get(pid, [])] != ["swing"]:
            v.fail(f"{pid} does not swing back against the cradle")
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
    """The sills stand on the ground; everything that rocks hangs together on the rockers."""
    m = v.m
    fixed = v.by_part["frame"]
    seen, loose = frame_floating(fixed)
    print(f"fixed: {len(seen)} elements of the sills on the ground, {len(loose)} not")
    if loose:
        v.fail(f"fixed elements float: {loose[:8]}")
    rocking = [e for pid in m.MOVING for e in v.posed(pid, m.REST)]
    seen, loose = frame_floating(rocking, ground=m.SILL_TOP + 1e-6)
    print(f"rocking: {len(seen)} elements joined to the rockers' running faces, {len(loose)} not")
    if loose:
        v.fail(f"rocking elements float: {loose[:8]}")


ROLES = [
    # (element regex, the textures it may wear)
    (r"^cradle_apron_canvas$", {"canvas"}),
    (r"^cradle_riddle$", {"riddle"}),
    (r"^cradle_", {"oak"}),
    (r"^water_", {"water"}),
    (r"^charge_", {"charge"}),
    (r"^conc_", {"concentrate"}),
    (r"^fr_sill", {"oak"}),
]


def check_textures(v):
    bad = []
    for el in v.els:
        tex = {f["texture"].lstrip("#") for f in el.faces.values()}
        rule = next((want for rx, want in ROLES if re.match(rx, el.name)), None)
        if rule is None or not tex <= rule:
            bad.append((el.name, sorted(tex), sorted(rule or [])))
    clear = [el.name for el in v.els if el.part == "water" and el.render_pass != v.m.TRANSPARENT]
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
    """The cradle is level at theta 0 and pi and leans ROCK_DEG south at pi/2 and north at 3 pi/2; every rigid
    part that rocks moves as the cradle does; the water stays level, carried as its pivot on the cradle is;
    nothing fixed moves."""
    m = v.m
    want = {0.0: 0.0, math.pi / 2: m.ROCK_DEG, math.pi: 0.0, 3 * math.pi / 2: -m.ROCK_DEG}
    for th, deg in want.items():
        got = angle_x(v.mat("cradle", th))
        if abs(got - deg) > 1e-3:
            v.fail(f"the cradle is at {got:.4f} degrees at theta {th:.4f}, want {deg}")
    worst, level = 0.0, 0.0
    pivot = next(p for p in v.parts if p["id"] == m.LEVEL[0])["drivers"][0]["pivot"]
    if max(abs(pivot[k] - m.water_pivot()[k] / 16) for k in range(3)) > 1e-6:
        v.fail("the water's swing is not about its pivot")
    for th in m.sample_thetas(15.0):
        c = v.mat("cradle", th)
        for pid in m.RIGID:
            a = v.mat(pid, th)
            worst = max(worst, max(abs(a[i][j] - c[i][j]) for i in range(3) for j in range(4)))
        carried = _apply(c, pivot)
        for pid in m.LEVEL:
            a = v.mat(pid, th)
            level = max(level, max(abs(a[i][j] - (1.0 if i == j else 0.0)) for i in range(3) for j in range(3)))
            worst = max(worst, max(abs(a[i][3] - (carried[i] - pivot[i])) for i in range(3)))
        for pid in m.STATIC:
            a = v.mat(pid, th)
            worst = max(worst, max(abs(a[i][j] - (1.0 if i == j else 0.0)) for i in range(3) for j in range(4)))
    print(f"rock: level at 0 and pi, {m.ROCK_DEG:g} degrees south at pi/2 and north at 3 pi/2; every rigid rocking part with "
          f"the cradle, the water carried with its pivot, every fixed part still, within {worst:.1e}; the water level within {level:.1e}")
    if worst > 1e-9:
        v.fail("a part does not move with the cradle, or a fixed part moves")
    if level > 1e-9:
        v.fail("the water tips with the cradle")


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
    # the water runs over the floor and round the riffles, its edges in the sides and the head board, its
    # underside in the floor, the apron's low end in it; the concentrate lies under it
    ("water", None, "cradle", r"^cradle_(floor|side_[ns]|headboard|riffle\d|apron_(canvas|rail_[ns]))$"),
    ("water", None, "concentrate", None),
    ("charge", None, "cradle", r"^cradle_(riddle|hopper_wall_\w+)$"),
    ("concentrate", None, "cradle", r"^cradle_(floor|side_[ns]|riffle\d)$"),
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
    stick = v.named("cradle", r"^cradle_handle$")[0]
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
    for pid in ("cradle",):
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


def check_water(v):
    """Its only water is poured into the hopper from the bucket the player holds while rocking it: the rig has no
    water cell or face, no spout and no part for a pipe or a stream; nothing fixed stands over the sills (the
    sills are the only fixed part); the hopper is open to the sky, nothing of the rocker over its riddle plate
    at any point of the rock; and the points that ride name a part the rig has."""
    m = v.m
    rig = v.rig
    ids = {p["id"] for p in v.parts}
    plumbing = [k for k in rig if re.search(r"(Cell|Face)$", k) or k in ("spout", "stream", "pipe", "inlet")]
    plumbing += [p["id"] for p in v.parts if p["id"] in ("spout", "stream") or p["requires"] in ("spout", "stream")]
    if plumbing:
        v.fail(f"the rocker takes water only by bucket, but the rig declares {plumbing}")
    if set(m.STATIC) != {"frame"} or max(e.aabb()[1][1] for e in v.by_part["frame"]) > m.SILL_TOP + 1e-9:
        v.fail("something fixed stands beside the rocker besides its sills")
    (x0, x1), (z0, z1), ry = m.riddle_box()
    over = set()
    for th in m.sample_thetas(10.0):
        cm = v.mat("cradle", th)
        r = [row[:3] for row in cm[:3]]
        t = [cm[i][3] * 16 for i in range(3)]
        for pid in v.by_part:
            if pid in ("water", "charge"):
                continue
            for e in v.posed(pid, th):
                for p in e.corners():
                    w = [p[k] - t[k] for k in range(3)]
                    q = m.untilt_point([sum(r[j][i] * w[j] for j in range(3)) for i in range(3)])
                    if x0 + 1e-6 < q[0] < x1 - 1e-6 and z0 + 1e-6 < q[2] < z1 - 1e-6 and q[1] > ry + 1e-6:
                        over.add(e.name)
    for key in m.POINT_ANCHORS:
        part = rig[key].get("part")
        if part is not None and part not in ids:
            v.fail(f"the {key} point rides {part}, which is no part")
    print(f"water: by bucket only (no water cell, face, spout or stream in the rig); the sills the only fixed part; the "
          f"hopper open to the sky over the rock: {'yes' if not over else sorted(over)}; hopper {rig['hopper']['pos']}, "
          f"outflow {rig['outflow']['pos']}, concentrate {rig['concentrate']['pos']}")
    if over:
        v.fail(f"something stands over the hopper, in the bucket's way: {sorted(over)[:4]}")


def in_cradle(v, th, p):
    """A posed point (voxels) in the cradle's own level frame: the box as built, before its slope."""
    m = v.m
    cm = v.mat("cradle", th)
    r = [row[:3] for row in cm[:3]]
    w = [p[k] - cm[k][3] * 16 for k in range(3)]
    return m.untilt_point([sum(r[j][i] * w[j] for j in range(3)) for i in range(3)])


MARGIN = 0.05                                # voxels: how far inside the wood the water's hidden faces must stay


def check_level_water(v):
    """The water stays level while the box tips under it, and its sheet stays in the box over the whole rock
    (every 5 degrees of theta), read in the box's own level frame: its underside inside the floor (never
    showing above the floor or below it), its edges inside the sides and the head board, its foot end at the
    lip, and water over the floor right across the box."""
    m = v.m
    sheet = next(e for e in v.by_part["water"] if e.name == "water_sheet")
    bad = []
    lo_bottom, hi_bottom, shallow, deep = 1e9, -1e9, 1e9, -1e9
    side_in, side_out = 1e9, -1e9
    for th in m.sample_thetas(5.0):
        mat = v.mat("water", th)
        posed_sheet = posed(sheet, mat)
        for i, p in enumerate(posed_sheet.corners()):
            q = in_cradle(v, th, p)
            east, top, south = i & 1, (i >> 1) & 1, (i >> 2) & 1
            if top:
                shallow, deep = min(shallow, q[1] - m.FLOOR_TOP), max(deep, q[1] - m.FLOOR_TOP)
            else:
                lo_bottom, hi_bottom = min(lo_bottom, q[1] - m.FLOOR_Y0), max(hi_bottom, q[1] - m.FLOOR_TOP)
            into = (m.IN_Z[0] - q[2]) if not south else (q[2] - m.IN_Z[1])
            side_in, side_out = min(side_in, into), max(side_out, into)
            if east and not (m.HEAD_IN + MARGIN < q[0] < m.BOX_X[1] - MARGIN):
                bad.append(("east end out of the head board", round(th, 3)))
            if not east and not (m.BOX_X[0] + 0.02 < q[0] < m.BOX_X[0] + 0.25):
                bad.append(("foot end off the lip", round(th, 3)))
    print(f"level water over the rock: underside {lo_bottom:.2f} over the floor's underside and {-hi_bottom:.2f} under its "
          f"top; edges {side_in:.2f}..{side_out:.2f} into the sides ({m.BOARD:g} thick); depth at the sides "
          f"{shallow:.2f}..{deep:.2f} over the floor")
    if lo_bottom < MARGIN or hi_bottom > -MARGIN:
        v.fail("the water's underside shows through or above the floor")
    if side_in < MARGIN or side_out > m.BOARD - MARGIN:
        v.fail("the water's edges leave the sides")
    if shallow < 0.3:
        v.fail("the floor shows through the water at the high side")
    if bad:
        v.fail(f"the water leaves the box: {bad[:4]}")


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
    check_path(v)
    check_handle(v)
    check_foot(v)
    check_water(v)
    check_level_water(v)
    check_clearances(v, [m.REST, math.pi / 2, 3 * math.pi / 2], "clearances, every part", lambda a, b: True)
    moving, fixed = set(m.MOVING), set(m.STATIC)
    sweep = (lambda a, b: (a in moving) != (b in moving))
    if not quick:
        check_clearances(v, m.sample_thetas(2.0), "swept rock (every 2 degrees of theta), rocking against fixed", sweep)
        check_zfight(v)
    return v.ok


def validate_files(m, shape, frame_shape, item_shape, ship):
    ok = True
    for s in (shape, frame_shape, item_shape):
        used = {f["texture"].lstrip("#") for e in s["elements"] for f in e["faces"].values()}
        missing = used - set(s["textures"])
        if missing:
            print(f"FAIL textures used but not declared: {missing}")
            ok = False
    parts = ship["parts"]
    of = {e["name"]: part_of(parts, e["name"]) for e in shape["elements"]}
    frame = {e["name"] for e in frame_shape["elements"]}
    item = {e["name"] for e in item_shape["elements"]}
    if frame != {n for n, p in of.items() if p in m.STATIC}:
        print("FAIL the frame shape is not the fixed parts")
        ok = False
    if item != {n for n, p in of.items() if p not in m.STATES}:
        print("FAIL the item shape is not the rocker dry and empty")
        ok = False
    gaps = lid_gaps(ship["cells"])
    hollow = sum(1 for c in ship["cells"] if c.get("hollow"))
    counts = {}
    for p in of.values():
        counts[p] = counts.get(p, 0) + 1
    print(f"files: {len(shape['elements'])} elements ({', '.join(f'{k} {n}' for k, n in counts.items())}), frame "
          f"{len(frame_shape['elements'])}, item {len(item_shape['elements'])}; {len(ship['cells'])} cells, {hollow} hollow; "
          f"lids over every column: {'yes' if not gaps else gaps}; operator {ship.get('operatorSide')}")
    if ship.get("operatorSide") != "north":
        print("FAIL the operator stands on the north side, at the handle")
        ok = False
    if gaps or hollow:
        ok = False
    if "powerCell" in ship or "powerFace" in ship:
        print("FAIL a hand station has no power cell")
        ok = False
    if any(k.endswith(("Cell", "Face")) for k in ship):
        print("FAIL the rocker takes no water by pipe: no water cell or face")
        ok = False
    passes = {e["name"]: e.get("renderPass") for e in shape["elements"]}
    if any(n.startswith("water_") and p != m.TRANSPARENT for n, p in passes.items()):
        print("FAIL the water lost its Transparent pass in the file")
        ok = False
    return ok
