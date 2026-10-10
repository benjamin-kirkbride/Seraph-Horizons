"""The amalgam pan generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved. A pose is
(theta, contents): contents 0 empty, 1 mercury, 2 amalgam.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb, supports
from machinegen.geometry import aabb_of
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

    def mat(self, pid, pose):
        return self.m.pm(self.parts, pid, pose)

    def posed(self, pid, pose):
        key = (pid, pose[0])
        if key not in self.cache:
            if len(self.cache) > 40000:
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


TAU_16 = 2 * math.pi / 16


def radius(m, p):
    return math.hypot(p[0] - m.CX, p[2] - m.CZ)


def polygon_depth(m, p, apothem):
    """How far inside a 16-gon of `apothem` about the axis (its faces on the pan's angles) the point
    lies, across: positive inside."""
    dx, dz = p[0] - m.CX, p[2] - m.CZ
    return min(apothem - (dx * math.sin(a) + dz * math.cos(a)) for a in (TAU_16 * i for i in range(16)))


def axis_distance(m, el):
    """The least distance across from the pan's axis to a box standing upright (turned about y only)."""
    d = [m.CX - el.c[0], 0.0, m.CZ - el.c[2]]
    local = [sum(el.r[i][k] * d[i] for i in range(3)) for k in range(3)]
    out = [max(0.0, abs(local[k]) - abs(el.size[k]) / 2) for k in (0, 2)]
    return math.hypot(*out)


def point_distance(el, p):
    """The distance from a point to a (turned) box."""
    d = [p[i] - el.c[i] for i in range(3)]
    local = [sum(el.r[i][k] * d[i] for i in range(3)) for k in range(3)]
    return math.sqrt(sum(max(0.0, abs(local[k]) - abs(el.size[k]) / 2) ** 2 for k in range(3)))


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


def check_containment(v, poses):
    m = v.m
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where = -1e9, None
    for pose in poses:
        for pid in v.by_part:
            if not m.on_show(pid, pose[1]):
                continue
            for el in v.posed(pid, pose):
                lo, hi = el.aabb()
                o = box_overhang(lo, hi, box)
                if o > worst:
                    worst, where = o, (el.name, pose)
    print(f"containment over {len(poses)} poses: worst overhang {worst:.3f} voxels ({where[0]})")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the block at {where[1]}")


def check_floating(v):
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")


ROLES = [
    # (element regex, the textures it may wear)
    (r"^fr_(leg|apron|bearer|rail)_", {"oak"}),
    (r"^fr_(floor|wall|rim)_", {"pan"}),
    (r"^fr_(cone_base|cone_neck|foot|arch|boss|plug_boss)", {"iron"}),
    (r"^fr_plug_bung", {"oak"}),
    (r"^muller_(spindle|hub|arm|crankhub|crank)", {"iron"}),
    (r"^muller_handle_", {"oak"}),
    (r"^shoe_", {"shoe"}),
    (r"^mercury_", {"mercury"}),
    (r"^amalgam_", {"amalgam"}),
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


def turn_y(mat):
    """A part matrix's turn about +y (radians), from the turned x axis."""
    return math.atan2(-mat[2][0], mat[0][0])


def check_turning(v):
    """The muller turns about the pan's axis by exactly theta (ratio 1), the shoes with it; the
    contents and the frame stand still."""
    m = v.m
    worst_axis, worst_turn, worst_ride = 0.0, 0.0, 0.0
    for th in [i * 0.37 - 7.0 for i in range(40)]:
        pose = (th, 0)
        mm = v.mat("muller", pose)
        for y in (0.0, 6.0, 14.0):
            q = v.point("muller", pose, [m.CX, y, m.CZ])
            worst_axis = max(worst_axis, abs(q[0] - m.CX), abs(q[1] - y), abs(q[2] - m.CZ))
        d = (turn_y(mm) - th + math.pi) % (2 * math.pi) - math.pi
        worst_turn = max(worst_turn, abs(d))
        ms = v.mat("shoes", pose)
        worst_ride = max(worst_ride, max(abs(mm[i][j] - ms[i][j]) for i in range(3) for j in range(4)))
        for pid in ("mercury", "amalgam", "frame"):
            mp = v.mat(pid, pose)
            if max(abs(mp[i][j] - (1.0 if i == j else 0.0)) for i in range(3) for j in range(4)) > 1e-12:
                v.fail(f"theta moves the {pid}")
    print(f"turning: the pan's axis stays put within {worst_axis:.1e}, the muller turns theta within {worst_turn:.1e}, "
          f"the shoes ride it within {worst_ride:.1e}")
    if worst_axis > 1e-9 or worst_turn > 1e-9 or worst_ride > 1e-12:
        v.fail("the muller does not turn with the crank about the pan's axis, or the shoes do not ride it")


def check_supports(v):
    """The spindle is carried by two bearings fixed to the frame, far apart: the step (the cone's neck)
    and the bridge's boss; it lies inside each at rest and turned an eighth; each hub keyed on it stands
    clear of the bearing beside it."""
    m = v.m
    frame = v.by_part["frame"]
    inside = 1e9
    for pose in ((0.0, 0), (math.pi / 8, 0), (math.pi / 4, 0)):
        spindle = v.named("muller", r"_spindle_", pose)
        names, span = supports(frame, spindle, 1, (m.CX, m.CZ))
        bearings = sorted({re.sub(r"_\d+$", "", n) for n in names if re.match(r"fr_(cone_neck|boss)_", n)})
        if bearings != ["fr_boss", "fr_cone_neck"]:
            v.fail(f"the spindle is not carried by the step and the boss at theta {pose[0]:.3f}: {sorted(names)}")
        reach = max(radius(m, c) for e in spindle for c in e.corners())
        # the spindle is a prism: its section is the same all along, so its corners say how far inside each
        # bearing's section it runs (a 16-gon of the bearing's apothem lies inside the bearing's octagon)
        for name, (ap, y0, y1) in (("step", m.CONE_NECK), ("boss", m.BOSS)):
            depth = min(polygon_depth(m, c, ap) for e in spindle for c in e.corners())
            inside = min(inside, depth)
            if depth < 0.3 or min(span[1], y1) - max(span[0], y0) < 0.5:
                v.fail(f"the spindle is not well inside the {name} (depth {depth:.2f}, span {span})")
    gap_step = min(e.aabb()[0][1] for e in v.named("muller", r"_hub_", (0.0, 0))) - m.CONE_NECK[2]
    gap_boss = min(e.aabb()[0][1] for e in v.named("muller", r"_crankhub_", (0.0, 0))) - m.BOSS[2]
    apart = m.BOSS[1] - m.CONE_NECK[2]
    print(f"support: the spindle (reach {reach:.2f} from its axis) at least {inside:.2f} inside the step and the boss, {apart:.1f} apart; "
          f"the muller's hub {gap_step:.2f} over the step, the crank's hub {gap_boss:.2f} over the boss")
    if not (0.1 <= gap_step <= 0.5 and 0.1 <= gap_boss <= 1.0) or apart < 3.0:
        v.fail("a hub is not just clear of its bearing, or the bearings are too close together")


def check_shoes(v):
    """Every shoe bears flat on the floor and sits under its arm's end at every angle of a turn; it stays
    clear of the wall and of the step by at least 0.3."""
    m = v.m
    worst_floor, worst_wall, worst_step, worst_arm = 0.0, 1e9, 1e9, 0.0
    for i in range(360):
        pose = (i * math.pi / 180, 0)
        shoes = v.posed("shoes", pose)
        arms = {e.name[-1]: e for e in v.named("muller", r"_arm\d$", pose)}
        for s in shoes:
            cs = s.corners()
            worst_floor = max(worst_floor, abs(min(c[1] for c in cs) - m.FLOOR_TOP))
            worst_wall = min(worst_wall, min(polygon_depth(m, c, m.R_IN) for c in cs))
            worst_step = min(worst_step, axis_distance(m, s) - m.CONE_BASE[0] / math.cos(math.pi / 8))
            arm = arms[s.name[-1]]
            alo, ahi = arm.aabb()
            worst_arm = max(worst_arm, abs(max(c[1] for c in cs) - alo[1]))
            # the arm's end over the shoe: its outer end's middle inside the shoe across
            tip = [arm.c[k] + arm.r[k][2] * arm.size[2] / 2 for k in range(3)]
            if not (m.SHOE_R[0] < radius(m, tip) < m.SHOE_R[1]):
                v.fail(f"{arm.name}'s end is not over {s.name}")
    print(f"shoes: on the floor within {worst_floor:.1e}, up against their arms within {worst_arm:.1e}; "
          f"clear of the wall by {worst_wall:.2f} and of the step by {worst_step:.2f} over a turn")
    if worst_floor > 1e-6 or worst_arm > 1e-6 or worst_wall < 0.3 or worst_step < 0.3:
        v.fail("a shoe is off the floor or its arm, or runs close to the wall or the step")


def check_contents(v):
    """Each layer lies on the floor inside the wall, its edge in the wall (no gap) and its underside in the
    floor; the mercury is a pool under the paste's level, and the paste under the step's top and the
    shoes', so the shoes stand out of it and the spindle's foot stays dry."""
    m = v.m
    tops = {}
    for _, pid in m.CONTENTS:
        els = v.by_part[pid]
        lo, hi = aabb_of(els)
        tops[pid] = hi[1]
        reach = max(radius(m, c) for e in els for c in e.corners())
        edge = min(polygon_depth(m, c, m.R_IN) for e in els for c in e.corners())
        if not (m.PAN_Y0 < lo[1] < m.FLOOR_TOP and hi[1] > m.FLOOR_TOP):
            v.fail(f"the {pid} does not lie on the floor ({lo[1]:.2f}..{hi[1]:.2f})")
        if not (-0.1 <= edge < 0.0) or reach > m.R_OUT:
            v.fail(f"the {pid}'s edge is not just into the wall (depth {edge:.3f}, reach {reach:.2f})")
    shoe_top = m.SHOE_Y[1]
    order = [m.FLOOR_TOP, tops["mercury"], tops["amalgam"], m.SPINDLE_Y[0], m.CONE_NECK[2], shoe_top]
    print("contents: the mercury " + f"{tops['mercury'] - m.FLOOR_TOP:.2f} deep, the amalgam {tops['amalgam'] - m.FLOOR_TOP:.2f}; "
          f"under the spindle's foot ({m.SPINDLE_Y[0]}), the step's top ({m.CONE_NECK[2]}) and the shoes' ({shoe_top})")
    if order != sorted(order) or len(set(order)) != len(order):
        v.fail("the layers are not in order under the spindle's foot, the step and the shoes")


def check_bridge(v):
    """The bridge's feet stand on the rim (over the wall's top and the bead); each half of the arch runs
    from inside its foot into the boss at the crown, clear of the spindle."""
    m = v.m
    frame = {e.name: e for e in v.by_part["frame"]}
    boss = [e for n, e in frame.items() if n.startswith("fr_boss_")]
    for s in ("w", "e"):
        foot = frame[f"fr_foot_{s}"]
        lo, hi = foot.aabb()
        under = [e for n, e in frame.items() if re.match(r"fr_(wall|rim)_", n) and abs(e.aabb()[1][1] - lo[1]) < 1e-6
                 and obb_obb(e, foot, eps=-0.03)]
        kinds = {re.match(r"fr_(wall|rim)_", e.name).group(1) for e in under}
        if abs(lo[1] - m.RIM_TOP) > 1e-6 or kinds != {"wall", "rim"}:
            v.fail(f"the {s} foot does not stand on the wall and the bead ({sorted(e.name for e in under)})")
        def ends(name):
            el = frame[name]
            pts = [[el.c[k] + sg * el.r[k][0] * el.size[0] / 2 for k in range(3)] for sg in (-1, 1)]
            return sorted(pts, key=lambda p: radius(m, p), reverse=True)       # outer end first
        (outer, knee1), (knee2, inner) = ends(f"fr_arch_{s}1"), ends(f"fr_arch_{s}2")
        if not all(lo[k] < outer[k] < hi[k] for k in range(3)):
            v.fail(f"the {s} half of the arch does not start inside its foot ({outer})")
        if not (polygon_depth(m, inner, m.BOSS[0]) > 0.1 and m.BOSS[1] < inner[1] < m.BOSS[2]):
            v.fail(f"the {s} half of the arch does not end in the boss ({inner})")
        if math.dist(knee1, knee2) > 0.25 or radius(m, knee2) < radius(m, knee1):          # they overlap at the bend
            v.fail(f"the {s} half of the arch's two bars do not meet at the bend ({knee1}, {knee2})")
    spindle = v.named("muller", r"_spindle_", (0.0, 0))
    reach = max(radius(m, c) for e in spindle for c in e.corners())
    gap = min(point_distance(frame[f"fr_arch_{s}2"], [m.CX, m.BOSS[1] + (m.BOSS[2] - m.BOSS[1]) * i / 40, m.CZ])
              for s in ("w", "e") for i in range(41)) - reach
    print(f"bridge: both feet on the rim (the wall's top and the bead), each half arch from its foot, bent once, into the boss "
          f"({len(boss)} strips), {gap:.2f} clear of the spindle")
    if gap < 0.05:
        v.fail("the arch runs into the spindle")


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels at every sampled pose.
ALLOWED = [
    ("muller", r"_spindle_", "frame", r"fr_(cone_|boss)"),                 # the spindle in its bearings
    ("shoes", None, "muller", r"_arm\d"),                                   # each shoe bolted under its arm
    ("mercury|amalgam", None, "frame", r"fr_(floor|wall|cone)"),            # the contents in the pan
    ("mercury|amalgam", None, "shoes", None),                               # the shoes stand in them
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose):
    items = []
    for pid in v.by_part:
        if v.m.on_show(pid, pose[1]):
            for el in v.posed(pid, pose):
                items.append((pid, el, el.aabb()))
    hits = {}
    for i in range(len(items)):
        pa, ea, (alo, ahi) = items[i]
        for j in range(i + 1, len(items)):
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


def check_clearances(v, poses, label):
    allhits = {}
    for pose in poses:
        for key, hs in touching_pairs(v, pose).items():
            allhits.setdefault(key, {})
            for h in hs:
                allhits[key].setdefault(h, pose)
    print(f"{label} over {len(poses)} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {p[0]:.3f}" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = [e for pid in v.by_part if m.on_show(pid, pose[1]) for e in v.posed(pid, pose)]
        pairs = coplanar_faces([e for e in m.shown(els, pose) if e.c[1] > -500])
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
    check_containment(v, [m.REST] + m.turn_poses(5.0))
    check_turning(v)
    check_supports(v)
    check_shoes(v)
    check_contents(v)
    check_bridge(v)
    check_clearances(v, [m.REST] + m.turn_poses(10.0), "clearances")
    if not quick:
        check_clearances(v, m.turn_poses(1.0), "swept paths (every degree of a turn, each contents)")
        check_zfight(v)
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
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cell, {hollow} hollow; "
          f"lid over it: {'yes' if not gaps else gaps}; boxes {ship['cells'][0].get('boxes')}")
    if gaps or hollow or len(ship["cells"]) != 1:
        ok = False
    if "powerCell" in ship or "powerFace" in ship or "work" in ship or "trunkPath" in ship:
        print("FAIL a hand station has no power cell and nothing posed by a work")
        ok = False
    return ok
