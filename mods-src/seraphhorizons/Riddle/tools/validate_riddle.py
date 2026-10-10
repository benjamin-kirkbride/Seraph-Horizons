"""The riddle generator's validation: make_shape.py calls `validate` (once per model) and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when a model breaks its
rule. Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in
make_shape.py proves the shipped files are this model moved, and `check_same_riddle` that the stand's
riddle is the hand riddle's.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import bearing_margin, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import part_of, posed


class V:
    def __init__(self, m, md, els, parts, rig):
        self.m, self.md, self.els, self.parts, self.rig = m, md, els, parts, rig
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        self.moving = {p["id"] for p in parts if p["drivers"] or p.get("ride")}
        self.cache = {}
        self.ok = True

    def fail(self, msg):
        self.ok = False
        print(f"FAIL {self.md.key}:", msg)

    def mat(self, pid, pose):
        return self.m.pm(self.parts, pid, pose)

    def posed(self, pid, pose):
        key = (pid, pose)
        if key not in self.cache:
            if len(self.cache) > 60000:
                self.cache.clear()
            mm = self.mat(pid, pose)
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def point(self, pid, pose, p):
        q = _apply(self.mat(pid, pose), [v / 16 for v in p])
        return [v * 16 for v in q]

    def named(self, pid, rx, pose):
        r = re.compile(rx)
        return [e for e in self.posed(pid, pose) if r.search(e.name)]

    def relative(self, pid, frame_pid, pose):
        """A part's elements posed in another part's frame: as the rig poses them, the other part's motion undone."""
        f = self.mat(frame_pid, pose)
        rt = [[f[j][i] for j in range(3)] for i in range(3)]
        t = [-sum(rt[i][j] * f[j][3] for j in range(3)) for i in range(3)]
        inv = [rt[0] + [t[0]], rt[1] + [t[1]], rt[2] + [t[2]], [0.0, 0.0, 0.0, 1.0]]
        mm = self.mat(pid, pose)
        rel = [[sum(inv[i][k] * mm[k][j] for k in range(4)) for j in range(4)] for i in range(4)]
        return [posed(el, rel) for el in self.by_part.get(pid, [])]


def present(m, pid, k):
    return m.on_show(pid, k)


def cycle_poses(v, step=0.005):
    n = int(round(1 / step))
    return [v.m.pose_at(v.md, k, i * step) for k in (1, 2) for i in range(n + 1)]


def grid_poses(v):
    """W over the cycle against the shake's phase (psi a quarter turn apart and between), both classes."""
    ws = (0.0, 0.03, 0.06, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.64, 0.7, 0.8, 0.82, 0.9, 1.0)
    if v.md.key == "stand":
        ws += (0.69, 0.73, 0.77, 0.81, 0.83, 0.85, 0.87, 0.93, 0.97)
    psis = [i * math.pi / 4 for i in range(8)]
    return ([(psi, w, k, 1.0) for k in (1, 2) for w in sorted(set(ws)) for psi in psis]
            + [(psi, 0.5, k, 0.5) for k in (1, 2) for psi in psis[::2]])


def check_basic(v):
    worst = euler_round_trip(v.els)
    print(f"euler round trip: worst {worst:.1e}")
    if worst > 1e-9:
        v.fail("an element's rotation does not survive being written as Euler angles")
    names = [el.name for el in v.els]
    if len(names) != len(set(names)):
        v.fail("duplicate element names")
    wrong = [(el.name, el.part, part_of(v.parts, el.name)) for el in v.els if part_of(v.parts, el.name) != el.part]
    if wrong:
        v.fail(f"{len(wrong)} elements land in the wrong part, e.g. {wrong[:4]}")
    empty = [p["id"] for p in v.parts if not v.by_part.get(p["id"])]
    if empty:
        v.fail(f"parts with no elements: {empty}")
    counts = {p["id"]: len(v.by_part.get(p["id"], [])) for p in v.parts}
    print(f"parts: {len(v.parts)}, elements {len(v.els)}: " + ", ".join(f"{k} {n}" for k, n in counts.items()))


def check_containment(v, poses):
    """Nothing leaves the footprint's box, but for the parts the model lets rise above it (the stand's lever),
    and those only through its top: never out of its sides or under it."""
    m, md = v.m, v.md
    size = [n * 16.0 for n in md.cells]
    worst, where = -1e9, None
    above = 0.0
    for pose in poses:
        for pid in v.by_part:
            if not present(m, pid, pose[2]):
                continue
            for el in v.posed(pid, pose):
                lo, hi = el.aabb()
                sides = [-lo[0], -lo[1], -lo[2], hi[0] - size[0], hi[2] - size[2]]
                top = hi[1] - size[1]
                if pid in md.above:
                    above = max(above, top)
                else:
                    sides.append(top)
                o = max(sides)
                if o > worst:
                    worst, where = o, (el.name, pose)
    allowed = f"; {', '.join(md.above)} up to {above:.2f} over the top" if md.above else ""
    print(f"containment over {len(poses)} poses: worst overhang {worst:.3f} voxels ({where[0]}){allowed}")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the footprint at {where[1]}")
    if md.above and above < 1.0:
        v.fail("the lever does not rise above the block")


def check_floating(v):
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")


ROLES = [
    # (element regex, the textures it may wear)
    (r"^(fr_)?o?box_(wall|bottom)", {"planks"}),
    (r"^(fr_)?o?box_corner", {"iron"}),
    (r"^fr_(bearer|leg|rail|endrail|stretcher|endstretcher|roller)", {"oak"}),
    (r"^fr_(bracket|bearing|pin)", {"iron"}),
    (r"^riddle_(lower|upper)", {"oak"}),
    (r"^riddle_wire", {"iron"}),
    (r"^(hanger|lug|link)_", {"iron"}),
    (r"^lever_bar", {"oak"}),
    (r"^lever_(strap|pin)", {"iron"}),
    (r"^c[12](base|top|mid|fines)", {"ore"}),
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


def inside(v, inner, outer, what):
    """Every element of `inner` lies inside the box `outer` (one element, axis-aligned in this frame) by HIDE_MARGIN."""
    lo, hi = aabb_of(outer)
    ilo, ihi = aabb_of(inner)
    margin = min(min(ilo[k] - lo[k], hi[k] - ihi[k]) for k in range(3))
    if margin < v.m.HIDE_MARGIN - 1e-6:
        v.fail(f"{what} is not hidden (margin {margin:.3f})")
    return margin


def bottom_of(v, pose):
    box = "frame" if v.md.key == "hand" else "boxes"
    return [e for e in v.posed(box, pose) if re.search(r"^(fr_)?box_bottom", e.name)]


def check_charge(v):
    """At W 0 the heap lies as loaded and the fines are hidden in the box's bottom; the bed moves with the riddle
    while it is riddled; every layer of fines sinks inside the bed (a full charge's top first inside its lower
    layer) and the fines lie in the box at W 1, the first layer on the bottom and the second on it."""
    m, md = v.m, v.md
    worst = 1e9
    for k, pre in ((1, "c1"), (2, "c2")):
        start, done = m.pose_at(md, k, 0.0), m.pose_at(md, k, 1.0)
        for pid in v.by_part:
            if pid.startswith(pre):
                if any(abs(a - b) > 1e-9 for ea, eb in zip(v.posed(pid, start), v.by_part[pid]) for a, b in zip(ea.c, eb.c)):
                    v.fail(f"{pid} is not as loaded at W 0")
        # the bed is part of the riddle while it is riddled (on the stand until it is lifted out)
        last = md.t["lift"][0] if "lift" in md.t else 1.0
        for w in [i * 0.01 for i in range(int(round(last * 100)) + 1)]:
            for psi in (0.0, 1.3, 2.9):
                a, b = v.mat(f"{pre}base", (psi, w, k, 1.0)), v.mat("riddle", (psi, w, k, 1.0))
                if max(abs(a[i][j] - b[i][j]) for i in range(3) for j in range(4)) > 1e-9:
                    v.fail(f"the oversize bed ({pre}) leaves the riddle at W {w:.2f}")
                    break
        sunk = ("c1top",) if k == 1 else ("c2mid", "c2top")
        bed = v.relative(f"{pre}base", f"{pre}base", done)
        for pid in sunk:
            worst = min(worst, inside(v, v.relative(pid, f"{pre}base", done), bed, f"{pid} at W 1"))
        if k == 2:
            t = m.pose_at(md, 2, (md.t["top"][1] + md.t["mid"][0]) / 2)
            worst = min(worst, inside(v, v.relative("c2top", "c2mid", t), v.relative("c2mid", "c2mid", t), "c2top in c2mid"))
        fines = ("c1fines",) if k == 1 else ("c2fines1",)
        for pid in fines:
            worst = min(worst, inside(v, v.posed(pid, start), bottom_of(v, start), f"{pid} at W 0"))
            low = aabb_of(v.posed(pid, done))[0][1]
            if abs(low - m.BOX_BOTTOM) > 1e-4:
                v.fail(f"{pid} does not lie on the box's bottom at W 1 ({low:.4f})")
        if k == 2:
            worst = min(worst, inside(v, v.posed("c2fines2", start), v.posed("c2fines1", start), "c2fines2 at W 0"))
            top1 = aabb_of(v.posed("c2fines1", done))[1][1]
            low2 = aabb_of(v.posed("c2fines2", done))[0][1]
            if abs(low2 - top1) > 1e-4:
                v.fail(f"the second layer of fines does not lie on the first at W 1 ({low2:.4f} on {top1:.4f})")
    heap = aabb_of(v.posed("c2top", m.pose_at(md, 2, 0.0)))[1][1]
    print(f"charge: as loaded at W 0 (a full charge heaped {heap - md.y0 - m.RIM_H:.2f} over the rim); the bed moves with the "
          f"riddle while it is riddled; every layer of fines hidden when sunk or before it rises, by at least {worst:.3f}; "
          "the fines in the box at W 1")


def riddle_centre(v, pose):
    m, md = v.m, v.md
    return v.point("riddle", pose, [md.centre[0], md.y0 + m.RIM_H / 2, md.centre[1]])


def check_shake(v):
    """The clock's travel moves nothing outside the shaking window; inside it the riddle goes to and fro (and,
    by hand, side to side) as far as designed."""
    m, md = v.m, v.md
    psis = [i * math.pi / 8 for i in range(16)]
    outside = (0.0, 0.02, md.t["shake"][3] + 0.01, 0.9, 1.0)
    for k in (1, 2):
        for w in outside:
            ref = {p["id"]: v.mat(p["id"], (0.0, w, k, 1.0)) for p in v.parts}
            for psi in psis:
                for p in v.parts:
                    a = v.mat(p["id"], (psi, w, k, 1.0))
                    if max(abs(a[i][j] - ref[p["id"]][i][j]) for i in range(3) for j in range(4)) > 1e-12:
                        v.fail(f"the clock moves {p['id']} outside the shaking window (W {w})")
                        return
    rest = riddle_centre(v, m.REST)
    w = (md.t["shake"][1] + md.t["shake"][2]) / 2
    dz = [riddle_centre(v, (psi, w, 2, 1.0))[2] - rest[2] for psi in psis]
    dx = [riddle_centre(v, (psi, w, 2, 1.0))[0] - rest[0] for psi in psis]
    want_z = m.SHAKE_Z if md.key == "hand" else m.RIDDLE_SWING
    want_x = m.SHAKE_X if md.key == "hand" else 0.0
    got_z, got_x = max(abs(d) for d in dz), max(abs(d) for d in dx)
    print(f"shake: the clock moves nothing outside the window; in it the riddle goes {got_z:.3f} to and fro (want {want_z}), "
          f"{got_x:.3f} side to side (want {want_x})")
    if abs(got_z - want_z) > 0.05 or abs(got_x - want_x) > 0.01:
        v.fail("the shake is not as designed")


def check_hand(v):
    """The riddle rests on the bearers, across both, and is lifted clear of them while it is shaken."""
    m = v.m
    low = aabb_of(v.posed("riddle", m.REST))[0][1]
    if abs(low - m.HAND_Y0) > 1e-6:
        v.fail(f"the riddle does not rest on the bearers ({low:.3f})")
    worst = 1e9
    bearers = [e.aabb() for e in v.named("frame", r"bearer", m.REST)]
    for pose in grid_poses(v):
        if not (m.HAND_T["shake"][1] <= pose[1] <= m.HAND_T["shake"][2]):
            continue
        for e in v.posed("riddle", pose):
            lo, hi = e.aabb()
            for blo, bhi in bearers:
                if lo[0] < bhi[0] and blo[0] < hi[0] and lo[2] < bhi[2] and blo[2] < hi[2]:
                    worst = min(worst, lo[1] - bhi[1])
    print(f"hand: the riddle rests on both bearers; shaken, it clears them by at least {worst:.3f}")
    if worst < 0.05:
        v.fail("the shaken riddle rubs on the bearers")


def roller_gap(v, pose):
    """How far the riddle's underside stands off the roller (negative: into it), in the plane x = const."""
    m, md = v.m, v.md
    ry, rz, rr = m.ROLLER
    mm = v.mat("riddle", pose)
    # the underside: the plane y = y0 in the riddle's frame; its normal and a point on it, posed
    n = [mm[0][1], mm[1][1], mm[2][1]]
    p = v.point("riddle", pose, [md.centre[0], md.y0, md.centre[1]])
    return -((ry - p[1]) * n[1] + (rz - p[2]) * n[2]) - rr


def check_stand(v):
    """The riddle's north end on the hangers' pins, the link exactly on its two pins (a parallelogram), the
    riddle's underside on the roller (riddled, swung out and back); tipped into the second cell at full swing,
    back at rest by W 1; the oversize laid flat on the oversize box's floor; every pin carried."""
    m, md = v.m, v.md
    poses = [m.REST] + grid_poses(v) + cycle_poses(v, 0.005)
    pin = link = 0.0
    lo_gap, hi_gap = 1e9, -1e9
    for pose in poses:
        for x in (md.centre[0] - m.UPPER_HALF, md.centre[0] + m.UPPER_HALF):
            p = [x, m.HANG_Y, m.HANG_Z]
            a, b = v.point("lugs", pose, p), v.point("hangers", pose, p)
            pin = max(pin, max(abs(a[i] - b[i]) for i in range(3)))
        for z, other in ((m.HANG_Z, "hangers"), (m.LEVER_Z, "lever")):
            p = [m.LINK_X[0], m.LINK_PIN_Y, z]
            a, b = v.point("link", pose, p), v.point(other, pose, p)
            link = max(link, max(abs(a[i] - b[i]) for i in range(3)))
        if pose[2]:
            g = roller_gap(v, pose)
            lo_gap, hi_gap = min(lo_gap, g), max(hi_gap, g)
    print(f"stand: the riddle's north end on the hangers' pins within {pin:.1e}; the link on both its pins within {link:.1e}; "
          f"the riddle's underside off the roller by {lo_gap:.3f} .. {hi_gap:.3f}")
    if pin > 1e-6 or link > 1e-6:
        v.fail("a pin leaves its eye")
    if lo_gap < -0.06 or hi_gap > 0.12:
        v.fail("the riddle does not ride on the roller")
    # tipped into the second cell, and back
    full = m.pose_at(md, 2, md.t["out"] + m.KNOTS * md.t["step"])
    tip = math.degrees(math.atan2(v.mat("riddle", full)[2][1], v.mat("riddle", full)[1][1]))
    lip = max(c[2] for e in v.posed("riddle", full) for c in e.corners())
    back = max(max(abs(v.mat(p["id"], m.pose_at(md, k, 1.0))[i][j] - (1.0 if i == j else 0.0)) for i in range(3) for j in range(4))
               for p in v.parts if p["id"] in ("hangers", "riddle", "lever", "link") for k in (1, 2))
    print(f"stand: swung out, the riddle tips {tip:.1f} degrees, its far end over the second cell to z {lip:.2f}; back at rest at "
          f"W 1 within {back:.1e}")
    if tip < 35.0 or lip < 17.0:
        v.fail("the riddle does not tip over the oversize box")
    if back > 1e-9:
        v.fail("the riddle is not back at rest when the charge is delivered")
    # the oversize laid flat on the oversize box's floor, inside its walls
    obox = [e.aabb() for e in v.posed("boxes", m.REST) if e.name.startswith("obox_")]
    inner = [(e.aabb()) for e in v.posed("boxes", m.REST) if e.name == "obox_bottom"][0]
    for k, pre in ((1, "c1"), (2, "c2")):
        lo, hi = aabb_of(v.posed(f"{pre}base", m.pose_at(md, k, 1.0)))
        flat = max(abs(v.mat(f"{pre}base", m.pose_at(md, k, 1.0))[i][j] - (1.0 if i == j else 0.0)) for i in range(3) for j in range(3))
        if abs(lo[1] - inner[1][1]) > 1e-3 or flat > 1e-5 or not (inner[0][0] < lo[0] and hi[0] < inner[1][0]
                                                                   and inner[0][2] < lo[2] and hi[2] < inner[1][2]):
            v.fail(f"the oversize ({pre}) is not laid flat in the oversize box at W 1 ({lo}, {hi}, turn {flat:.1e})")
    del obox
    print("stand: the oversize tipped off and laid flat on the oversize box's floor, inside its walls")
    # every pin carried; every eye round its pin through the swing
    frame = v.by_part["frame"]
    for s in ("w", "e"):
        pins = [f for f in frame if f.name.startswith(f"fr_pin_{s}")]
        for bearing in (next(f for f in frame if f.name == f"fr_bracket_{s}"), next(f for f in frame if f.name == f"fr_rail_{s}")):
            mg = bearing_margin(bearing, pins, 0)
            if not (0 < mg < 1e8):
                v.fail(f"the {s} hanger's pivot pin is not carried by {bearing.name}")
        roll = [f for f in frame if f.name.startswith("fr_roller")]
        for bearing in (next(f for f in frame if f.name == f"fr_bearing_{s}"), next(f for f in frame if f.name == f"fr_legm_{s}")):
            mg = bearing_margin(bearing, roll, 0)
            if not (0 < mg < 1e8):
                v.fail(f"the roller is not carried by {bearing.name}")
    for bearing in (next(f for f in frame if f.name == "fr_bracket_lever"), next(f for f in frame if f.name == "fr_rail_w")):
        mg = bearing_margin(bearing, [f for f in frame if f.name.startswith("fr_pin_lever")], 0)
        if not (0 < mg < 1e8):
            v.fail(f"the lever's pivot pin is not carried by {bearing.name}")
    worst = 1e9
    for pose in poses[::3]:
        for s, x in (("w", md.centre[0] - m.UPPER_HALF), ("e", md.centre[0] + m.UPPER_HALF)):
            strap = v.named("hangers", rf"strap_{s}", pose)[0]
            for p in ([x, m.PIVOT_Y, m.HANG_Z], v.point("lugs", pose, [x, m.HANG_Y, m.HANG_Z])):
                worst = min(worst, eye_margin(strap, p))
        bar = v.named("lever", r"_bar", pose)[0]
        worst = min(worst, eye_margin(bar, [md.centre[0], m.PIVOT_Y, m.LEVER_Z]))
    print(f"stand: pivot pins carried by the rails and their plates, the roller by the middle legs and their bearings; every "
          f"eye round its pin by at least {worst:.3f}")
    if worst < m.PIN_R:
        v.fail("an eye lets go of its pin")


def eye_margin(el, p):
    """How far inside a posed element's y-z section a pin's axis (along x, through p) stays."""
    rel = [p[i] - el.c[i] for i in range(3)]
    local = [sum(el.r[j][k] * rel[j] for j in range(3)) for k in range(3)]
    return min(abs(el.size[k]) / 2 - abs(local[k]) for k in (1, 2))


ALLOWED = {
    "hand": [
        ("riddle", None, "frame", r"fr_bearer"),
        ("c[12]fines\\d?", None, "frame", r"box_bottom"),
    ],
    "stand": [
        ("lugs", None, "riddle", r"_upper"), ("lugs", None, "hangers", None),
        ("hangers", None, "frame", r"fr_pin_|fr_bracket_"),
        ("link", None, "hangers", None), ("link", None, "lever", None),
        ("lever", None, "frame", r"fr_pin_lever|fr_bracket_lever"),
        # the riddle rides on the roller
        ("riddle", r"_lower", "frame", r"fr_roller"),
        ("c[12]fines\\d?", None, "boxes", r"^box_bottom"),
        # the oversize laid on the oversize box's floor
        ("c[12]base", None, "boxes", r"obox_bottom"),
    ],
}
ALLOWED_BOTH = [
    # the charge: the bed on the mesh, each layer sinking into the one under it
    ("c[12]base", None, "riddle", r"_wire"),
    ("c[12](top|mid)", None, "c[12](base|mid)", None),
    ("c2fines2", None, "c2fines1", None),
]


def allowed(md, pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED[md.key] + ALLOWED_BOTH:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose, statics):
    k = pose[2]
    items = []
    for pid in v.by_part:
        if present(v.m, pid, k):
            for el in v.posed(pid, pose):
                items.append((pid, el, el.aabb()))
    hits = {}
    for i in range(len(items)):
        pa, ea, (alo, ahi) = items[i]
        for j in range(i + 1, len(items)):
            pb, eb, (blo, bhi) = items[j]
            if pa == pb or (not statics and pa not in v.moving and pb not in v.moving):
                continue
            if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                continue
            if allowed(v.md, pa, ea.name, pb, eb.name):
                continue
            if obb_obb(ea, eb):
                hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def check_clearances(v, poses, label):
    allhits = {}
    for n, pose in enumerate(poses):
        for key, hs in touching_pairs(v, pose, statics=(n == 0)).items():
            allhits.setdefault(key, {})
            for h in hs:
                allhits[key].setdefault(h, pose)
    print(f"{label} over {len(poses)} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at psi {p[0]:.2f} W {p[1]:.3f} k {p[2]}" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


def check_zfight(v):
    m = v.m
    bad = 0
    poses = m.coplanar_poses(v.md)
    for pose in poses:
        k = pose[2]
        els = [e for pid in v.by_part for e in v.posed(pid, pose)]
        pairs = coplanar_faces([e for e in m.shown(els, pose) if e.c[1] > -500 and present(m, e.part, k)])
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(poses)} poses")
    if bad:
        v.fail("faces z-fight")


def validate(m, md, els, parts, rig, quick=False):
    v = V(m, md, els, parts, rig)
    check_basic(v)
    check_floating(v)
    check_textures(v)
    check_containment(v, [m.REST] + grid_poses(v) + cycle_poses(v, 0.005))
    check_charge(v)
    check_shake(v)
    if md.key == "hand":
        check_hand(v)
    else:
        check_stand(v)
    check_clearances(v, [m.REST] + grid_poses(v), "clearances")
    if not quick:
        check_clearances(v, [m.REST] + cycle_poses(v, 0.0025), "swept paths (every 0.0025 of the cycle at the pace, both charges)")
        check_zfight(v)
    return v.ok


def validate_files(m, md, shape, frame_shape, ship):
    """Every texture declared; the cells exactly the footprint, every box inside its cell (so nothing drawn
    outside the footprint, the stand's lever above the block, is selectable or solid), lids on every column;
    no power cell."""
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL {md.key}: textures used but not declared: {missing}")
        ok = False
    gaps = lid_gaps(ship["cells"])
    hollow = sum(1 for c in ship["cells"] if c.get("hollow"))
    cells = sorted(tuple(c["pos"]) for c in ship["cells"])
    want = sorted(tuple(c[k] - m.ORIGIN_CELL[k] for k in range(3)) for c in m.footprint(md))
    boxes = [b for c in ship["cells"] for b in c.get("boxes", [])]
    inside_cell = all(0.0 <= b[i] <= 1.0 and b[i] < b[i + 3] <= 1.0 for b in boxes for i in range(3))
    lids = all(0.0 < c.get("lid", 0.0) <= 1.0 for c in ship["cells"])
    print(f"{md.key} files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; cells {cells}, {hollow} hollow; "
          f"{len(boxes)} boxes, all inside their cells: {'yes' if inside_cell else 'NO'}; lids over every column: "
          f"{'yes' if not gaps and lids else gaps}")
    if gaps or hollow or cells != want or not inside_cell or not lids:
        print(f"FAIL {md.key}: the cells are not the footprint's, boxed inside it and lidded")
        ok = False
    if "powerCell" in ship or "powerFace" in ship:
        print(f"FAIL {md.key}: a hand station has no power cell")
        ok = False
    return ok
