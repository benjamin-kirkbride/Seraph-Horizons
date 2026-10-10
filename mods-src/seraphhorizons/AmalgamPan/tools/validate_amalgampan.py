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


def shaft_radius(m, p):
    """The distance across from the crankshaft's axis (along z, through (CX, SHAFT_Y))."""
    return math.hypot(p[0] - m.CX, p[1] - m.SHAFT_Y)


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
    (r"^fr_(leg|apron|bearer|rail|sill)_", {"oak"}),
    (r"^fr_(floor|wall|rim)_", {"pan"}),
    (r"^fr_(cone_base|cone_neck|plug_boss|stepbox|footstep|pedestal|bearing)", {"iron"}),
    (r"^fr_plug_bung", {"oak"}),
    (r"^muller_(spindle|crown|hub|arm|shoe)", {"iron"}),
    (r"^crank_(shaft|pinion|eye|arm)", {"iron"}),
    (r"^crank_handle_", {"oak"}),
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


def turn_z(mat):
    """A part matrix's turn about +z (radians), from the turned x axis."""
    return math.atan2(mat[1][0], mat[0][0])


def wrap(a):
    return (a + math.pi) % (2 * math.pi) - math.pi


def check_turning(v):
    """The crank turns about its shaft's axis by exactly theta; the muller about the pan's axis by theta
    times the gears' ratio (2/3); the contents and the frame stand still."""
    m = v.m
    worst_axis, worst_turn = 0.0, 0.0
    for th in [i * 0.37 - 7.0 for i in range(40)]:
        pose = (th, 0)
        mm, mc = v.mat("muller", pose), v.mat("crank", pose)
        for y in (0.0, 6.0, 14.0):
            q = v.point("muller", pose, [m.CX, y, m.CZ])
            worst_axis = max(worst_axis, abs(q[0] - m.CX), abs(q[1] - y), abs(q[2] - m.CZ))
        for z in (0.0, 4.0, 8.0):
            q = v.point("crank", pose, [m.CX, m.SHAFT_Y, z])
            worst_axis = max(worst_axis, abs(q[0] - m.CX), abs(q[1] - m.SHAFT_Y), abs(q[2] - z))
        worst_turn = max(worst_turn, abs(wrap(turn_y(mm) - m.GEAR_RATIO * th)), abs(wrap(turn_z(mc) - th)))
        for pid in ("mercury", "amalgam", "frame"):
            mp = v.mat(pid, pose)
            if max(abs(mp[i][j] - (1.0 if i == j else 0.0)) for i in range(3) for j in range(4)) > 1e-12:
                v.fail(f"theta moves the {pid}")
    print(f"turning: the axes stay put within {worst_axis:.1e}; the crank turns theta and the muller {m.GEAR_RATIO:g} theta "
          f"within {worst_turn:.1e}")
    if worst_axis > 1e-9 or worst_turn > 1e-9:
        v.fail("the crank or the muller does not turn about its own axis by its share of theta")


def check_supports(v):
    """Each shaft is carried by two bearings fixed to the frame, far apart: the spindle by the footstep
    under the pan and the cone's neck in it, the crankshaft by its outer and inner bearings. Each runs well
    inside both at rest and turned an eighth and a quarter; the hubs keyed on them stand just clear of the
    bearings beside them (the muller's over the cone, the crank's eye before the outer bearing)."""
    m = v.m
    frame = v.by_part["frame"]
    worst = {}
    for pose in ((0.0, 0), (math.pi / 8, 0), (math.pi / 4, 0)):
        for pid, rx, axis, c, want, sizes in (
                ("muller", r"_spindle_", 1, (m.CX, m.CZ), ["fr_cone_neck", "fr_footstep"],
                 {"fr_cone_neck": (m.CONE_NECK[0], m.CONE_NECK[1], m.CONE_NECK[2]),
                  "fr_footstep": (m.FOOTSTEP[0], m.LOW_Y[1] + m.STEPBOX[2], m.LOW_Y[1] + m.STEPBOX[2] + m.FOOTSTEP[1])}),
                ("crank", r"_shaft_", 2, (m.CX, m.SHAFT_Y), ["fr_bearing_i", "fr_bearing_o"],
                 {"fr_bearing_o": (m.BEARING_R, *m.BEARING_O), "fr_bearing_i": (m.BEARING_R, *m.BEARING_I)})):
            shaft = v.named(pid, rx, pose)
            names, span = supports(frame, shaft, axis, c)
            found = sorted({re.sub(r"_\d+$", "", n) for n in names if re.sub(r"_\d+$", "", n) in want})
            if found != want:
                v.fail(f"the {pid}'s shaft is not carried by {want} at theta {pose[0]:.3f}: {sorted(names)}")
            for name, (ap, a0, a1) in sizes.items():
                # a shaft is a prism: its corners say how far inside each bearing's section it runs
                if axis == 1:
                    depth = min(ap - math.hypot(q[0] - m.CX, q[2] - m.CZ) for e in shaft for q in e.corners())
                else:
                    depth = min(ap - shaft_radius(m, q) for e in shaft for q in e.corners())
                overlap = min(span[1], a1) - max(span[0], a0)
                worst[name] = min(worst.get(name, (1e9, 1e9))[0], depth), min(worst.get(name, (1e9, 1e9))[1], overlap)
                if depth < 0.3 or overlap < 0.5:
                    v.fail(f"the {pid}'s shaft is not well inside {name} (depth {depth:.2f}, along it {overlap:.2f})")
    apart_spindle = m.CONE_NECK[1] - (m.LOW_Y[1] + m.STEPBOX[2] + m.FOOTSTEP[1])
    apart_shaft = m.BEARING_I[0] - m.BEARING_O[1]
    gap_hub = min(e.aabb()[0][1] for e in v.named("muller", r"_hub_", (0.0, 0))) - m.CONE_NECK[2]
    gap_eye = m.BEARING_O[0] - max(e.aabb()[1][2] for e in v.named("crank", r"_eye_", (0.0, 0)))
    print("supports: " + ", ".join(f"{k} {d:.2f} inside along {o:.2f}" for k, (d, o) in sorted(worst.items()))
          + f"; the spindle's bearings {apart_spindle:.1f} apart, the crankshaft's {apart_shaft:.1f}; "
          f"the muller's hub {gap_hub:.2f} over the cone, the crank's eye {gap_eye:.2f} before the outer bearing")
    if not (0.1 <= gap_hub <= 0.5 and 0.1 <= gap_eye <= 0.5) or apart_spindle < 3.0 or apart_shaft < 1.5:
        v.fail("a hub is not just clear of its bearing, or a shaft's bearings are too close together")


def check_gears(v):
    """The crown wheel and pinion: their tooth counts give the rig's ratio, and their circular pitches
    agree, so the teeth roll on one another at the rig's speeds; the pinion's pitch circle touches the
    crown's pitch plane, and its face lies inside the crown's teeth's radial band, over the crown's pitch
    circle; each mate's tips clear the other's roots; at rest the pinion's top tooth sits in the middle
    of a gap of the crown. (That the teeth pass each other is the swept-path check's: every degree of a
    turn.)"""
    m = v.m
    ratio = m.PINION_TEETH / m.WHEEL_TEETH
    pitch_p = 2 * math.pi * m.R_PINION / m.PINION_TEETH
    pitch_w = 2 * math.pi * m.R_WHEEL / m.WHEEL_TEETH
    if abs(ratio - m.GEAR_RATIO) > 1e-12 or abs(pitch_p - pitch_w) > 1e-9:
        v.fail(f"the gears' ratio ({ratio}) is not the rig's ({m.GEAR_RATIO}), or their pitches differ ({pitch_p}, {pitch_w})")
    if abs(m.SHAFT_Y + m.R_PINION - m.PITCH_Y) > 1e-9 or not (m.CROWN_TEETH_R[0] <= m.CZ - m.PINION_Z[1] and m.CZ - m.PINION_Z[0] <= m.CROWN_TEETH_R[1]):
        v.fail("the pinion's pitch circle is off the crown's pitch plane, or its face is off the crown's teeth")
    teeth_p = v.named("crank", r"_piniontooth\d+$")
    teeth_w = v.named("muller", r"_crowntooth\d+$")
    if (len(teeth_p), len(teeth_w)) != (m.PINION_TEETH, m.WHEEL_TEETH):
        v.fail(f"the gears have {len(teeth_p)} and {len(teeth_w)} teeth, not {m.PINION_TEETH} and {m.WHEEL_TEETH}")
    tip_p = max(math.hypot(q[0] - m.CX, q[1] - m.SHAFT_Y) for t in teeth_p for q in t.corners())
    tip_w = min(q[1] for t in teeth_w for q in t.corners())
    clear_w = m.CROWN_Y[0] - (m.SHAFT_Y + tip_p)                  # the pinion's tips under the crown's web
    clear_p = (tip_w - m.SHAFT_Y) - m.PINION_BODY / math.cos(math.pi / 8)   # the crown's tips over the pinion's hub
    top = next(t for t in teeth_p if t.name.endswith("tooth1"))
    near = sorted(abs(t.c[0] - top.c[0]) for t in teeth_w if t.c[2] < m.CZ - 1.0)[:2]
    print(f"gears: pinion {m.PINION_TEETH} and crown {m.WHEEL_TEETH} teeth (ratio {ratio:g}, the rig's {m.GEAR_RATIO:g}), pitch "
          f"{pitch_p:.3f}, radii {m.R_PINION:.2f} and {m.R_WHEEL:.2f}; tips clear the roots by {clear_w:.2f} and {clear_p:.2f}; "
          f"at rest the pinion's top tooth between the crown's ({near[0]:.2f} and {near[1]:.2f} either side across)")
    if min(clear_w, clear_p) < 0.1:
        v.fail("a gear's tips run near its mate's roots")
    if len(near) < 2 or abs(near[0] - near[1]) > 1e-6 or near[0] < (m.PINION_W + m.CROWN_W) / 2:
        v.fail("the pinion's tooth does not sit in the middle of a gap of the crown at rest")


def check_crank(v):
    """The crank is on the operator's side (north): the handle stands out to the block's north face, the
    whole crank turns north of every other moving part and clear of the ground and of the pan's
    underside over a turn."""
    m = v.m
    lo_y, hi_y, max_z = 1e9, -1e9, -1e9
    handle_min_z = 1e9
    for i in range(72):
        pose = (i * 5 * math.pi / 180, 0)
        for e in v.named("crank", r"_(arm|handle_|eye_)", pose):
            for q in e.corners():
                lo_y, hi_y, max_z = min(lo_y, q[1]), max(hi_y, q[1]), max(max_z, q[2])
        for e in v.named("crank", r"_handle_", pose):
            handle_min_z = min(handle_min_z, min(q[2] for q in e.corners()))
    print(f"crank: turns from {lo_y:.2f} to {hi_y:.2f} high (the pan's underside {m.PAN_Y0}), the handle out to "
          f"{handle_min_z:.2f} from the north face, the crank's back at z {max_z:.2f}")
    if lo_y < 0.3 or hi_y > m.PAN_Y0 - 0.2 or handle_min_z > 0.5 or handle_min_z < 0.1 or max_z > m.BEARING_O[0]:
        v.fail("the crank runs near the ground or the pan, or its handle is not out at the operator's side")


def check_shoes(v):
    """Every shoe bears flat on the floor and sits under its arm's end at every degree of the muller's
    turn; it stays clear of the wall and of the cone by at least 0.3."""
    m = v.m
    worst_floor, worst_wall, worst_step, worst_arm = 0.0, 1e9, 1e9, 0.0
    for i in range(360):
        pose = (i * math.pi / 180 / m.GEAR_RATIO, 0)          # the muller's degree i: theta is the crank's
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
    ("muller", r"_spindle_", "frame", r"fr_(footstep|floor|cone_)"),        # the spindle in its footstep, up through the floor and the cone
    ("crank", r"_shaft_", "frame", r"fr_bearing_"),                         # the crankshaft in its bearings
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
    check_gears(v)
    check_crank(v)
    check_shoes(v)
    check_contents(v)
    check_clearances(v, [m.REST] + m.turn_poses(10.0), "clearances")
    if not quick:
        check_clearances(v, m.turn_poses(1.0), "swept paths (every degree of the crank's cycle, each contents)")
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
