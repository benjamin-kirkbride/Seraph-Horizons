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
    staged = [p["id"] for p in v.parts if p["requires"] not in (None, "mercury", "amalgam")]
    if staged:
        v.fail(f"the station has no build stages: every part but the contents is always fitted ({staged})")


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
    (r"^fr_(leg|apron|bearer|rail|bridgetree)", {"oak"}),
    (r"^fr_(floor|wall|rim)_", {"pan"}),
    (r"^fr_(cone_base|cone_neck|plug_boss|footstep)", {"iron"}),
    (r"^fr_plug_bung", {"oak"}),
    (r"^muller_(spindle|hub|arm|shoe)", {"iron"}),
    (r"^muller_coupling", {"oak"}),                  # the vanilla axle's own wood, continued: a shaft that meets an axle stays oak
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


def wrap(a):
    return (a + math.pi) % (2 * math.pi) - math.pi


def check_turning(v):
    """The muller turns about the pan's axis by exactly theta (ratio 1: with the axle, as the quern's stone
    does); the contents and the frame stand still."""
    m = v.m
    worst_axis, worst_turn = 0.0, 0.0
    for th in [i * 0.37 - 7.0 for i in range(40)]:
        pose = (th, 0)
        mm = v.mat("muller", pose)
        for y in (0.0, 6.0, 14.0):
            q = v.point("muller", pose, [m.CX, y, m.CZ])
            worst_axis = max(worst_axis, abs(q[0] - m.CX), abs(q[1] - y), abs(q[2] - m.CZ))
        worst_turn = max(worst_turn, abs(wrap(turn_y(mm) - m.GEAR_RATIO * th)))
        for pid in ("mercury", "amalgam", "frame"):
            mp = v.mat(pid, pose)
            if max(abs(mp[i][j] - (1.0 if i == j else 0.0)) for i in range(3) for j in range(4)) > 1e-12:
                v.fail(f"theta moves the {pid}")
    print(f"turning: the pan's axis stays put within {worst_axis:.1e}; the muller turns {m.GEAR_RATIO:g} theta within {worst_turn:.1e}")
    if worst_axis > 1e-9 or worst_turn > 1e-9 or m.GEAR_RATIO != 1.0:
        v.fail("the muller does not turn about the pan's axis with the axle, once a turn")


def check_supports(v):
    """The spindle is carried by two bearings fixed to the frame, far apart: the footstep on the bridge-tree
    under the pan and the cone's neck in it. It runs well inside both at rest and turned an eighth and a
    quarter; the muller's hub stands just clear over the cone."""
    m = v.m
    frame = v.by_part["frame"]
    foot = (m.LOW_Y[1], m.LOW_Y[1] + m.FOOTSTEP[1])
    sizes = {"fr_cone_neck": (m.CONE_NECK[0], m.CONE_NECK[1], m.CONE_NECK[2]), "fr_footstep": (m.FOOTSTEP[0], *foot)}
    worst = {}
    for pose in ((0.0, 0), (math.pi / 8, 0), (math.pi / 4, 0)):
        shaft = v.named("muller", r"_spindle_", pose)
        names, span = supports(frame, shaft, 1, (m.CX, m.CZ))
        found = sorted({re.sub(r"_\d+$", "", n) for n in names} & set(sizes))
        if found != sorted(sizes):
            v.fail(f"the spindle is not carried by the footstep and the cone's neck at theta {pose[0]:.3f}: {sorted(names)}")
        for name, (ap, a0, a1) in sizes.items():
            # the spindle is a prism: its corners say how far inside each bearing's section it runs
            depth = min(ap - math.hypot(q[0] - m.CX, q[2] - m.CZ) for e in shaft for q in e.corners())
            overlap = min(span[1], a1) - max(span[0], a0)
            worst[name] = min(worst.get(name, (1e9, 1e9))[0], depth), min(worst.get(name, (1e9, 1e9))[1], overlap)
            if depth < 0.3 or overlap < 0.5:
                v.fail(f"the spindle is not well inside {name} (depth {depth:.2f}, along it {overlap:.2f})")
    apart = m.CONE_NECK[1] - foot[1]
    gap_hub = min(e.aabb()[0][1] for e in v.named("muller", r"_hub_", (0.0, 0))) - m.CONE_NECK[2]
    print("supports: " + ", ".join(f"{k} {d:.2f} inside along {o:.2f}" for k, (d, o) in sorted(worst.items()))
          + f"; the bearings {apart:.1f} apart; the muller's hub {gap_hub:.2f} over the cone")
    if not 0.1 <= gap_hub <= 0.5 or apart < 3.0:
        v.fail("the hub is not just clear of the cone, or the spindle's bearings are too close together")


def check_coupling(v):
    """The axle comes in from below, as into the quern: the rig's power cell is the pan's own cell and its
    face the down face; the spindle's foot is the vanilla axle's oak cross profile on the pan's axis, from
    the block's bottom face (where the axle below meets it) up into the iron spindle; it turns under the
    bridge-tree, clear of it and of every leg and rail (the clearance checks say the same for the rest)."""
    m = v.m
    rig = v.rig
    if rig.get("powerCell") != [0, 0, 0] or rig.get("powerFace") != "down":
        v.fail(f"the power cell is not the pan's own, entered from below ({rig.get('powerCell')}, {rig.get('powerFace')})")
    bars = v.named("muller", r"_coupling[ab]$")
    lo, hi = aabb_of(bars)
    spindle = v.named("muller", r"_spindle_")
    s_lo = min(e.aabb()[0][1] for e in spindle)
    half, thick, high = m.COUPLING
    cross = sorted(sorted((round(abs(e.size[0]) / 2, 6), round(abs(e.size[2]) / 2, 6))) for e in bars)
    centred = all(abs(e.c[0] - m.CX) < 1e-9 and abs(e.c[2] - m.CZ) < 1e-9 for e in bars)
    print(f"coupling: power cell {rig.get('powerCell')} face {rig.get('powerFace')}; the axle's cross profile "
          f"({2 * half:g} by {2 * thick:g}) from y {lo[1]:.2f} to {hi[1]:.2f}, the spindle's foot at {s_lo:.2f} inside it, "
          f"{m.LOW_Y[0] - hi[1]:.2f} under the bridge-tree")
    if (len(bars) != 2 or not centred or cross != [[thick, half], [thick, half]] or abs(lo[1] - m.AXLE_MEET) > 1e-9
            or not lo[1] < s_lo < hi[1] or m.LOW_Y[0] - hi[1] < 0.1):
        v.fail("the spindle's foot does not meet the axle below in the axle's own profile, or it runs into the bridge-tree")

def check_shoes(v):
    """Every shoe bears flat on the floor and sits under its arm's end at every degree of the muller's
    turn; it stays clear of the wall and of the cone by at least 0.3."""
    m = v.m
    worst_floor, worst_wall, worst_step, worst_arm = 0.0, 1e9, 1e9, 0.0
    for i in range(360):
        pose = (i * math.pi / 180 / m.GEAR_RATIO, 0)          # the muller's degree i
        shoes = v.named("muller", r"_shoe\d$", pose)
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
          f"clear of the wall by {worst_wall:.2f} and of the cone by {worst_step:.2f} over a turn")
    if worst_floor > 1e-6 or worst_arm > 1e-6 or worst_wall < 0.3 or worst_step < 0.3:
        v.fail("a shoe is off the floor or its arm, or runs close to the wall or the cone")


def check_contents(v):
    """Each layer lies on the floor inside the wall, its edge in the wall (no gap) and its underside in the
    floor; the mercury is a pool under the paste's level, and the paste under the cone's top and the
    shoes', so the shoes stand out of it."""
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
    order = [m.FLOOR_TOP, tops["mercury"], tops["amalgam"], m.CONE_NECK[2], shoe_top]
    print("contents: the mercury " + f"{tops['mercury'] - m.FLOOR_TOP:.2f} deep, the amalgam {tops['amalgam'] - m.FLOOR_TOP:.2f}; "
          f"under the cone's top ({m.CONE_NECK[2]}) and the shoes' ({shoe_top})")
    if order != sorted(order) or len(set(order)) != len(order):
        v.fail("the layers are not in order under the cone and the shoes")


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels at every sampled pose.
ALLOWED = [
    ("muller", r"_spindle_", "frame", r"fr_(footstep|bridgetree|floor|cone_)"),   # the spindle down through the footstep and the
                                                                                   # bridge-tree, up through the floor and the cone
    ("mercury|amalgam", None, "frame", r"fr_(floor|wall|cone)"),            # the contents in the pan
    ("mercury|amalgam", None, "muller", r"_(shoe\d|spindle_)"),             # the shoes stand in them; the spindle, inside the cone
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
    check_coupling(v)
    check_shoes(v)
    check_contents(v)
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
    if ship.get("powerCell") != [0, 0, 0] or ship.get("powerFace") != "down" or "work" in ship or "trunkPath" in ship:
        print("FAIL the pan takes its axle from below at its own cell, and nothing is posed by a work")
        ok = False
    return ok
