"""The riddle generator's validation: make_shape.py calls `validate` (once per model) and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when a model breaks its
rule. Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in
make_shape.py proves the shipped files are this model moved, and `check_same_riddle` that the stand's
riddle is the hand riddle's.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import bearing_margin, box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
from machinegen.geometry import aabb_of, mvec
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

    def in_riddle(self, pid, pose):
        """A part's elements posed in the riddle's own frame: as the rig poses them, then the riddle's motion undone."""
        f = self.mat("riddle", pose)
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
    m = v.m
    ws = (0.0, 0.03, 0.06, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.82, 0.9, 1.0)
    psis = [i * math.pi / 4 for i in range(8)]
    return [(psi, w, k, 1.0) for k in (1, 2) for w in ws for psi in psis] + [(psi, 0.5, k, 0.5) for k in (1, 2) for psi in psis[::2]]


def radius(p, m):
    return math.hypot(p[0] - m.CX, p[2] - m.CZ)


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
    m = v.m
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where = -1e9, None
    for pose in poses:
        for pid in v.by_part:
            if not present(m, pid, pose[2]):
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
    (r"^(fr_)?tub_(stave|bottom)", {"planks"}),
    (r"^(fr_)?tub_hoop", {"iron"}),
    (r"^fr_(bearer|leg|rail|endrail|stretcher|endstretcher)", {"oak"}),
    (r"^fr_(bracket|boss|pin|leverpin)", {"iron"}),
    (r"^riddle_rim", {"oak"}),
    (r"^riddle_(band|wire)", {"iron"}),
    (r"^(hanger|lug|link)_", {"iron"}),
    (r"^lever_bar", {"oak"}),
    (r"^lever_(knuckle|pin)", {"iron"}),
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


def y_range(els):
    """(the highest bottom, the lowest top) of a stack of strips: what is surely inside all of them."""
    return max(e.aabb()[0][1] for e in els), min(e.aabb()[1][1] for e in els)


def inside(v, inner, outer, r_outer, what):
    """Every element of `inner` lies inside the strips of `outer` (a disc of radius r_outer) by HIDE_MARGIN."""
    m = v.m
    lo, hi = y_range(outer)
    ilo = min(e.aabb()[0][1] for e in inner)
    ihi = max(e.aabb()[1][1] for e in inner)
    rmax = max(radius(c, m) for e in inner for c in e.corners())
    margin = min(ilo - lo, hi - ihi, r_outer - rmax)
    if margin < m.HIDE_MARGIN - 1e-9:
        v.fail(f"{what} is not hidden (margin {margin:.3f})")
    return margin


def check_charge(v):
    """At W 0 the heap lies as loaded and the fines are hidden in the tub's bottom; at W 1 every layer of fines
    has sunk inside the oversize bed, which has not moved in the riddle, and the fines lie in the tub, the
    first layer on the bottom and the second on it."""
    m, md = v.m, v.md
    worst = 1e9
    for k, pre in ((1, "c1"), (2, "c2")):
        start, done = m.pose_at(md, k, 0.0), m.pose_at(md, k, 1.0)
        for pid in v.by_part:
            if pid.startswith(pre):
                if any(abs(a - b) > 1e-9 for ea, eb in zip(v.posed(pid, start), v.by_part[pid]) for a, b in zip(ea.c, eb.c)):
                    v.fail(f"{pid} is not as loaded at W 0")
        for pose in (done, m.pose_at(md, k, 0.5)):
            base = v.in_riddle(f"{pre}base", pose)
            if any(abs(a - b) > 1e-9 for ea, eb in zip(base, v.by_part[f"{pre}base"]) for a, b in zip(ea.c, eb.c)):
                v.fail(f"the oversize bed ({pre}) moves in the riddle")
        base = v.in_riddle(f"{pre}base", done)
        sunk = ("c1top",) if k == 1 else ("c2mid", "c2top")
        for pid in sunk:
            worst = min(worst, inside(v, v.in_riddle(pid, done), base, m.BASE[0], f"{pid} at W 1"))
        if k == 2:
            # the top is first sunk into the lower layer, before that sinks (measured in the riddle: it is shaken then)
            t = m.pose_at(md, 2, (m.T_TOP[1] + m.T_MID[0]) / 2)
            worst = min(worst, inside(v, v.in_riddle("c2top", t), v.in_riddle("c2mid", t), m.MID[0], "c2top in c2mid"))
        bottom = v.named("frame" if md.key == "hand" else "tub", r"tub_bottom", start)
        fines = ("c1fines",) if k == 1 else ("c2fines1",)
        for pid in fines:
            worst = min(worst, inside(v, v.posed(pid, start), bottom, m.TUB_BOTTOM_R, f"{pid} at W 0"))
            low = min(e.aabb()[0][1] for e in v.posed(pid, done))
            if abs(low - m.TUB_BOTTOM[1]) > 1e-4:
                v.fail(f"{pid} does not lie on the tub's bottom at W 1 ({low:.4f})")
        if k == 2:
            worst = min(worst, inside(v, v.posed("c2fines2", start), v.posed("c2fines1", start), m.F1[0], "c2fines2 at W 0"))
            top1 = max(e.aabb()[1][1] for e in v.posed("c2fines1", done))
            low2 = min(e.aabb()[0][1] for e in v.posed("c2fines2", done))
            if abs(low2 - top1) > 1e-4:
                v.fail(f"the second layer of fines does not lie on the first at W 1 ({low2:.4f} on {top1:.4f})")
        for pid in fines + (("c2fines2",) if k == 2 else ()):
            rmax = max(radius(c, m) for e in v.posed(pid, done) for c in e.corners())
            if rmax > m.TUB_R - m.STAVE_T - 0.05:
                v.fail(f"{pid} reaches the staves ({rmax:.2f})")
    heap = max(e.aabb()[1][1] for e in v.posed("c2top", m.pose_at(md, 2, 0.0)))
    rim = md.y0 + m.RIM_H
    print(f"charge: as loaded at W 0 (a full charge heaped {heap - rim:.2f} over the rim); every layer of fines hidden "
          f"when sunk or before it rises, by at least {worst:.3f}; the fines on the tub's bottom at W 1, the bed left in the riddle")


def riddle_centre(v, pose):
    m, md = v.m, v.md
    return v.point("riddle", pose, [m.CX, md.y0 + m.RIM_H / 2, m.CZ])


def check_shake(v):
    """The clock's travel moves nothing outside the shaking window; inside it the riddle goes to and fro (and,
    by hand, side to side) as far as designed."""
    m, md = v.m, v.md
    psis = [i * math.pi / 8 for i in range(16)]
    for k in (1, 2):
        for w in (0.0, 0.02, 0.86, 0.93, 1.0):
            ref = {p["id"]: v.mat(p["id"], (0.0, w, k, 1.0)) for p in v.parts}
            for psi in psis:
                for p in v.parts:
                    a = v.mat(p["id"], (psi, w, k, 1.0))
                    if max(abs(a[i][j] - ref[p["id"]][i][j]) for i in range(3) for j in range(4)) > 1e-12:
                        v.fail(f"the clock moves {p['id']} outside the shaking window (W {w})")
                        return
    rest = riddle_centre(v, m.REST)
    dz = [riddle_centre(v, (psi, 0.5, 2, 1.0))[2] - rest[2] for psi in psis]
    dx = [riddle_centre(v, (psi, 0.5, 2, 1.0))[0] - rest[0] for psi in psis]
    want_z = m.SHAKE_Z if md.key == "hand" else m.SWING
    want_x = m.SHAKE_X if md.key == "hand" else 0.0
    got_z, got_x = max(abs(d) for d in dz), max(abs(d) for d in dx)
    print(f"shake: the clock moves nothing outside the window; in it the riddle goes {got_z:.3f} to and fro (want {want_z}), "
          f"{got_x:.3f} side to side (want {want_x})")
    if abs(got_z - want_z) > 0.01 or abs(got_x - want_x) > 0.01:
        v.fail("the shake is not as designed")


def check_hand(v):
    """The riddle rests on the bearers, across both, and is lifted clear of them while it is shaken."""
    m = v.m
    low = min(e.aabb()[0][1] for e in v.posed("riddle", m.REST))
    if abs(low - m.HAND_Y0) > 1e-6 or abs(m.HAND_Y0 - (m.TUB_H + m.BEARER_H)) > 1e-9:
        v.fail(f"the riddle does not rest on the bearers ({low:.3f})")
    reach = math.sqrt(m.RIM_R ** 2 - (m.BEARER_Z[1] - m.CZ) ** 2)
    if not (m.BEARER_X[0] < m.CX - reach and m.CX + reach < m.BEARER_X[1]):
        v.fail("the riddle's rim does not cross both bearers")
    worst = 1e9
    bearers = [e.aabb() for e in v.named("frame", r"bearer", m.REST)]
    for pose in grid_poses(v):
        if not (m.T_SHAKE[1] <= pose[1] <= m.T_SHAKE[2]):
            continue
        for e in v.posed("riddle", pose):
            lo, hi = e.aabb()
            for blo, bhi in bearers:
                if lo[0] < bhi[0] and blo[0] < hi[0] and lo[2] < bhi[2] and blo[2] < hi[2]:
                    worst = min(worst, lo[1] - bhi[1])
    print(f"hand: the riddle rests on both bearers; shaken, it clears them by at least {worst:.3f}")
    if worst < 0.05:
        v.fail("the shaken riddle rubs on the bearers")


def rotation_error(mm):
    return max(abs(mm[i][j] - (1.0 if i == j else 0.0)) for i in range(3) for j in range(3))


def check_stand(v):
    """The riddle hangs level; its trunnions stay in the hangers' eyes and the link's eye on the hanger's pin;
    the lever's pin stays in the link's slot; every pin in its bearing."""
    m = v.m
    poses = [m.REST] + grid_poses(v) + cycle_poses(v, 0.01)
    level = max(rotation_error(v.mat("riddle", pose)) for pose in poses)
    trun = link_h = 0.0
    slot_y = slot_z = 0.0
    lz = m.LEVER_PIVOT[1]
    for pose in poses:
        for x in (m.CX - m.RIM_R, m.CX + m.RIM_R):
            p = [x, m.TRUNNION_Y, m.CZ]
            a, b = v.point("lugs", pose, p), v.point("hangers", pose, p)
            trun = max(trun, max(abs(a[i] - b[i]) for i in range(3)))
        p = [m.LINK_X[0], m.LINK_PIN_Y, m.CZ]
        a, b = v.point("link", pose, p), v.point("hangers", pose, p)
        link_h = max(link_h, max(abs(a[i] - b[i]) for i in range(3)))
        q = [m.LINK_X[0], m.LINK_PIN_Y, lz]
        a, b = v.point("lever", pose, q), v.point("link", pose, q)
        slot_y, slot_z = max(slot_y, abs(a[1] - b[1])), max(slot_z, abs(a[2] - b[2]))
    room = (m.SLOT_Y[1] - m.SLOT_Y[0]) / 2 - m.LINK_PIN_R - 0.05
    print(f"stand: the riddle level within {level:.1e}; trunnions in the hangers' eyes within {trun:.1e}; the link on the "
          f"hanger's pin within {link_h:.1e}; the lever's pin in the link's slot, {slot_y:.3f} up or down (room {room:.2f}), "
          f"{slot_z:.4f} along it")
    if level > 1e-9:
        v.fail("the riddle tilts as it swings")
    if trun > 1e-6 or link_h > 1e-6:
        v.fail("a pin leaves its eye")
    if slot_y > room or slot_z > 0.03:
        v.fail("the lever's pin leaves the link's slot")
    # every pin in its bearing; the hangers' and the lever's eyes round their pivots through the swing
    frame = v.by_part["frame"]
    for s in ("w", "e"):
        bracket = next(f for f in frame if f.name == f"fr_bracket_{s}")
        rail = next(f for f in frame if f.name == f"fr_rail_{s}")
        pins = [f for f in frame if f.name.startswith(f"fr_pin_{s}")]
        for bearing in (bracket, rail):
            mg = bearing_margin(bearing, pins, 0)
            if not (0 < mg < 1e8):
                v.fail(f"the {s} hanger's pivot pin is not carried by {bearing.name}")
    mg = bearing_margin(next(f for f in frame if f.name == "fr_boss"), [f for f in frame if f.name.startswith("fr_leverpin")], 0)
    if not (0 < mg < 1e8):
        v.fail("the lever's pivot pin is not in its boss")
    worst = 1e9
    for pose in poses:
        for s, x in (("w", m.CX - m.RIM_R), ("e", m.CX + m.RIM_R)):
            strap = v.named("hangers", rf"strap_{s}", pose)[0]
            for p in ([x, m.PIVOT_Y, m.CZ], v.point("lugs", pose, [x, m.TRUNNION_Y, m.CZ])):
                worst = min(worst, eye_margin(strap, p))
        knuckle = v.named("lever", r"knuckle", pose)[0]
        worst = min(worst, eye_margin(knuckle, [m.CX, m.LEVER_PIVOT[0], lz]))
    print(f"stand: pivot pins carried by the rails, the brackets and the boss; every eye round its pin by at least {worst:.3f}")
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
    ],
    "stand": [
        ("lugs", None, "riddle", r"_rim"), ("lugs", None, "hangers", None),
        ("hangers", None, "frame", r"fr_pin|fr_bracket"),
        ("link", None, "hangers", None), ("link", None, "lever", None),
        ("lever", None, "frame", r"fr_leverpin|fr_boss"),
    ],
}
ALLOWED_BOTH = [
    # the charge: the bed on the mesh, each layer sinking into the one under it
    ("c[12]base", None, "riddle", r"_wire|_band"),
    ("c[12](top|mid)", None, "c[12](base|mid)", None),
    # the fines: hidden in the tub's bottom, the second in the first
    ("c[12]fines\\d?", None, "frame|tub", r"tub_bottom"),
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
    check_containment(v, [m.REST] + grid_poses(v) + cycle_poses(v, 0.01))
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
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL {md.key}: textures used but not declared: {missing}")
        ok = False
    gaps = lid_gaps(ship["cells"])
    hollow = sum(1 for c in ship["cells"] if c.get("hollow"))
    print(f"{md.key} files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, "
          f"{hollow} hollow; lids over every column: {'yes' if not gaps else gaps}")
    if gaps or hollow or len(ship["cells"]) != 1:
        print(f"FAIL {md.key}: the cell is not one lidded block")
        ok = False
    if "powerCell" in ship or "powerFace" in ship:
        print(f"FAIL {md.key}: a hand station has no power cell")
        ok = False
    return ok
