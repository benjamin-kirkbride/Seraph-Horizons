"""The amalgam pan generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved. A pose is
(theta, contents): contents 0 empty, 1 mercury, 2 slurry (the charge), 3 amalgam, 4 the charge under its
water film.
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
    vocab = [p["id"] for p in v.parts if p["requires"] not in (None,) + STAGES + CONTENTS_REQUIRES + ("water",)]
    if vocab:
        v.fail(f"parts with a requires outside the pan's vocabulary: {vocab}")


STAGES = ("muller", "shoes")                     # #718's fitted parts, in the order they are fitted
CONTENTS_REQUIRES = ("mercury", "slurry", "amalgam")


def check_stages(v):
    """#718's fitted stages, in order: the muller (a rod: spindle, coupling, hub, arms), which carries the
    rotate on theta; then the shoes (a plate), riding the muller with no driver of their own, so they turn
    with it bolted under its arms. Every other part is the frame (requires null) or a content. The `shoe`
    texture code is on the shoes alone, and the shoes wear nothing else, so a renderer setting it to the
    fitted plate's metal changes the shoes and nothing more."""
    by_id = {p["id"]: p for p in v.parts}
    order = [p["requires"] for p in v.parts if p["requires"] in STAGES]
    muller, shoes = by_id.get("muller", {}), by_id.get("shoes", {})
    print(f"stages: {order}; the muller's drivers {[d['type'] for d in muller.get('drivers', [])]} riding "
          f"{muller.get('ride')}, the shoes' {shoes.get('drivers')} riding {shoes.get('ride')}")
    if order != list(STAGES) or muller.get("requires") != "muller" or shoes.get("requires") != "shoes":
        v.fail(f"the fitted stages are not the muller then the shoes: {order}")
    if muller.get("ride") is not None or [d["type"] for d in muller.get("drivers", [])] != ["rotate"]:
        v.fail("the muller is not the part theta turns, riding nothing")
    if shoes.get("ride") != "muller" or shoes.get("drivers") != []:
        v.fail("the shoes do not ride the muller with no driver of their own")
    for p in v.parts:
        if p["requires"] in CONTENTS_REQUIRES and (p.get("ride") is not None or p["drivers"] or p["id"] != p["requires"]):
            v.fail(f"the {p['id']} content is not a still layer of its own requires")
    water = by_id.get("water", {})
    if water.get("requires") != "water" or water.get("ride") != "muller" or water.get("drivers") != []:
        v.fail("the water film is not its own requires riding the muller with no driver of its own")
    with_shoe = sorted({el.part for el in v.els for f in el.faces.values() if f["texture"] == "#shoe"})
    shoe_wears = sorted({f["texture"] for el in v.by_part.get("shoes", []) for f in el.faces.values()})
    if with_shoe != ["shoes"] or shoe_wears != ["#shoe"]:
        v.fail(f"the shoe texture code is not the shoes' alone (on {with_shoe}; the shoes wear {shoe_wears})")


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
    (r"^muller_(spindle|hub|arm)", {"iron"}),
    (r"^shoe_\d$", {"shoe"}),                       # the fitted plate's metal (the renderer sets the code)
    (r"^muller_coupling", {"oak"}),                  # the vanilla axle's own wood, continued: a shaft that meets an axle stays oak
    (r"^mercury_", {"mercury"}),
    (r"^slurry_", {"slurry"}),
    (r"^amalgam_", {"amalgam"}),
    (r"^water_", {"water"}),
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
        ms = v.mat("shoes", pose)
        if max(abs(ms[i][j] - mm[i][j]) for i in range(3) for j in range(4)) > 1e-12:
            v.fail("the shoes do not turn with the muller")
        mw = v.mat("water", pose)
        if max(abs(mw[i][j] - mm[i][j]) for i in range(3) for j in range(4)) > 1e-12:
            v.fail("the water film does not turn with the muller")
        for pid in ("mercury", "slurry", "amalgam", "frame"):
            mp = v.mat(pid, pose)
            if max(abs(mp[i][j] - (1.0 if i == j else 0.0)) for i in range(3) for j in range(4)) > 1e-12:
                v.fail(f"theta moves the {pid}")
    print(f"turning: the pan's axis stays put within {worst_axis:.1e}; the muller turns {m.GEAR_RATIO:g} theta within {worst_turn:.1e}, "
          "the shoes and the water film with it")
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
        shoes = v.named("shoes", r"^shoe_\d$", pose)
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
    floor; the mercury is a pool under the paste's level, the paste under the charge's (the charge is the
    deepest: it reads as a loaded pan), and the charge under the cone's top and the shoes', so the shoes
    stand out of all three."""
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
    order = [m.FLOOR_TOP, tops["mercury"], tops["amalgam"], tops["slurry"], m.CONE_NECK[2], shoe_top]
    print("contents: the mercury " + f"{tops['mercury'] - m.FLOOR_TOP:.2f} deep, the amalgam {tops['amalgam'] - m.FLOOR_TOP:.2f}, "
          f"the charge {tops['slurry'] - m.FLOOR_TOP:.2f}; under the cone's top ({m.CONE_NECK[2]}) and the shoes' ({shoe_top})")
    if order != sorted(order) or len(set(order)) != len(order):
        v.fail("the layers are not in order under the cone and the shoes")


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels at every sampled pose.
ALLOWED = [
    ("muller", r"_spindle_", "frame", r"fr_(footstep|bridgetree|floor|cone_)"),   # the spindle down through the footstep and the
                                                                                   # bridge-tree, up through the floor and the cone
    ("mercury|slurry|amalgam", None, "frame", r"fr_(floor|wall|cone)"),            # the contents in the pan
    ("mercury|slurry|amalgam", None, "muller", r"_spindle_"),                       # the spindle, inside the cone
    ("mercury|slurry|amalgam", None, "shoes", None),                                 # the shoes stand in them
    ("water", None, "frame", r"fr_(wall|cone_neck)_"),                               # the film's edge set into the wall, its middle
                                                                                   # into the cone's neck (the shoes: never)
    ("water", None, "slurry", None),                                                 # the film's underside in the charge
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


def inside(el, p, grow=0.0):
    """Whether point p is inside the box el (grown by `grow` across x and z)."""
    d = [p[k] - el.c[k] for k in range(3)]
    local = [sum(el.r[i][k] * d[i] for i in range(3)) for k in range(3)]
    half = [abs(el.size[k]) / 2 + (grow if k != 1 else 0.0) for k in range(3)]
    return all(abs(local[k]) <= half[k] for k in range(3))


def check_water(v):
    """The water film over the charge: just over the charge's top, under the cone's neck and every arm; clear
    of every shoe by its slot's margin, its boxes lapping a little, never at one height (they turn together, so at every angle); its edge between the wall's
    inside and outside at every angle; and, at a spread of angles, every point of the open pan at the film's
    height (inside the wall, outside the cone's neck) is under the film or in a shoe's slot, so no charge
    shows through but round the shoes."""
    m = v.m
    els = v.by_part["water"]
    lo, hi = aabb_of(els)
    arms = min(e.aabb()[0][1] for e in v.named("muller", r"_(arm\d|hub_\d)$"))
    reach = max(radius(m, c) for e in els for c in e.corners())
    print(f"water: {len(els)} boxes, y {lo[1]:.2f} to {hi[1]:.2f} (the charge's top {m.SLURRY_TOP:.2f}, the neck's {m.CONE_NECK[2]:.2f}, "
          f"the arms' foot {arms:.2f}); out to {reach:.2f} (the wall {m.R_IN / math.cos(math.pi / 16):.2f} to {m.R_OUT:.2f})")
    if not (lo[1] < m.SLURRY_TOP - 0.1 and m.SLURRY_TOP + 0.05 <= hi[1] < min(m.CONE_NECK[2], arms) - 0.1) or reach > m.R_OUT - 0.05:
        v.fail("the water film is not a thin sheet just over the charge, under the neck and the arms, inside the wall's outside")
    # neighbours lap (no crack between them, whatever the shape file's rounding), by little, at different heights
    laps = [(obb_gap(a, b), a, b) for i, a in enumerate(els) for b in els[i + 1:]]
    near = [(g, a, b) for g, a, b in laps if g < 0.02]
    bad = [(a.name, b.name, round(g, 4)) for g, a, b in near
           if not -2 * m.WATER_LAP - 1e-6 <= g <= -m.WATER_LAP or abs(a.aabb()[1][1] - b.aabb()[1][1]) < m.WATER_STEP - 1e-9]
    print(f"water: {len(near)} pairs of its boxes lap, each by {-max(g for g, _, _ in near):.3f} to {-min(g for g, _, _ in near):.3f}, "
          "their tops apart")
    if bad:
        v.fail(f"the water film's boxes meet with a crack, lap too far or share a top: {bad[:4]}")
    # its outer edge: wherever a ray from the axis leaves the film, it is in the wall at every angle
    worst_in, worst_gap, uncovered, tested = 1e9, 1e9, [], 0
    for k in range(240):
        a = k * math.pi / 120
        rs = [i * 0.02 for i in range(275, 350)]
        on = [any(inside(e, [m.CX + r * math.sin(a), (lo[1] + hi[1]) / 2, m.CZ + r * math.cos(a)]) for e in els) for r in rs]
        last = max(i for i, x in enumerate(on) if x) if any(on) else None
        if last is not None:
            worst_in = min(worst_in, rs[last])
    wall_in = m.R_IN / math.cos(math.pi / 16)
    for th in (0.0, 0.2, 11.25 * math.pi / 180, 0.9, 2.0):
        pose = (th, 4)
        water = v.posed("water", pose)
        shoes = v.posed("shoes", pose)
        for s in shoes:
            for w in water:
                if obb_gap(w, s) < m.SLOT_MARGIN - 1e-6:
                    v.fail(f"{w.name} comes within {obb_gap(w, s):.3f} of {s.name} at theta {th:.3f}")
                worst_gap = min(worst_gap, obb_gap(w, s))
        y = (lo[1] + hi[1]) / 2
        n = 70
        for i in range(n + 1):
            for j in range(n + 1):
                p = [m.CX - 7 + 14 * i / n, y, m.CZ - 7 + 14 * j / n]
                if polygon_depth(m, p, m.R_IN) <= 0.0 or polygon_depth(m, p, m.CONE_NECK[0]) >= 0.0:
                    continue
                tested += 1
                if any(inside(w, p) for w in water) or any(inside(s, p, m.SLOT_MARGIN + 1e-6) for s in shoes):
                    continue
                uncovered.append((round(p[0], 2), round(p[2], 2), round(th, 3)))
    print(f"water: its edge at least {worst_in:.2f} out all round (into the wall past {wall_in:.2f}); {worst_gap:.2f} clear of the shoes; "
          f"{tested} points of the open pan over 5 angles, {len(uncovered)} uncovered outside the shoes' slots")
    if worst_in < wall_in + 0.02:
        v.fail("the water film's edge stops short of the wall somewhere as it turns")
    if uncovered:
        v.fail(f"the charge shows through the water film away from the shoes, e.g. {uncovered[:4]}")


def obb_gap(a, b):
    """The gap across (x, z) between two boxes standing upright and turned alike about y (the film's and the
    shoes', which turn together): measured in a's frame, where both are square."""
    def span(el, frame):
        cs = [[sum(frame.r[i][k] * (c[i] - frame.c[i]) for i in range(3)) for k in range(3)] for c in el.corners()]
        return [(min(c[k] for c in cs), max(c[k] for c in cs)) for k in (0, 2)]
    sa, sb = span(a, a), span(b, a)
    return max(max(sb[k][0] - sa[k][1], sa[k][0] - sb[k][1]) for k in range(2))


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
    check_stages(v)
    check_floating(v)
    check_textures(v)
    check_containment(v, [m.REST] + m.turn_poses(5.0))
    check_turning(v)
    check_supports(v)
    check_coupling(v)
    check_shoes(v)
    check_contents(v)
    check_water(v)
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
    # nothing goes in or out but by the player's hand: no anchor for a chute, hopper or chest
    transfer = sorted(k for k in ship if (k.endswith("Side") and k != "operatorSide") or k.startswith(("infeed", "output", "outfeed")))
    print(f"anchors: {sorted(k for k in ship if not k.startswith('_') and k not in ('cells', 'parts'))}; for item transfer: {transfer or 'none'}")
    if transfer:
        print("FAIL the pan takes no automated input or output")
        ok = False
    return ok
