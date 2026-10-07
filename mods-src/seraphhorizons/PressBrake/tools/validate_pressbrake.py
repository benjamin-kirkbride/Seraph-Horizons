"""The press brake generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import bearing_margin, box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
from machinegen.geometry import aabb_of, mvec
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
        key = (pid, pose)
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


SHEET = re.compile(r"^[lc][amb]$")


def present(pid, k):
    """The lead sheet shows only with lead on the brake, the copper sheet only with copper."""
    if pid in ("la", "lm", "lb"):
        return k == 1
    if pid in ("ca", "cm", "cb"):
        return k == 2
    return True


def cycle_poses(m, step=0.005):
    n = int(round(1 / step))
    return [m.pose_at(k, i * step) for k in (1, 2) for i in range(n + 1)]


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
        k = pose[2]
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


ROLES = [
    # (element regex, the textures it may wear)
    (r"^fr_(leg|rail|bed|stretcher)", {"oak"}),
    (r"^fr_(boss|pin|upfront|upback|bridge|nut)", {"iron"}),
    (r"^leaf_(body|heel)", {"oak"}),
    (r"^leaf_(knuckle|strap)", {"iron"}),
    (r"^lever_(strap|arm)", {"iron"}),
    (r"^lever_handle", {"oak"}),
    (r"^bar_(sole|body)", {"oak"}),
    (r"^bar_cup", {"iron"}),
    (r"^(bededge|leafedge|baredge)_", {"edge"}),
    (r"^screw[we]_", {"screw"}),
    (r"^l[amb]_", {"lead"}),
    (r"^c[amb]_", {"copper"}),
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


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels at every sampled pose.
ALLOWED = [
    ("leaf", r"_knuckle", "frame", r"fr_pin"), ("leaf", r"_(knuckle|body)", "frame", r"fr_pin"),
    ("leafedge", None, "leaf", None), ("lever", None, "leaf", None),
    ("baredge", None, "bar", None),
    ("screw[we]", r"_rod", "frame", r"fr_nut"), ("screw[we]", r"_rod", "bar", r"_cup"),
    ("bededge", None, "frame", r"fr_bed"),
    # the sheet: its panels at their bends, lying on the bed, the edges and the leaf, under the bar
    ("[lc][amb]", None, "[lc][amb]", None),
    ("[lc][amb]", None, "leaf|leafedge|bededge|bar|baredge", None),
    ("[lc][amb]", None, "frame", r"fr_bed"),
    # the bar lying on the bed with no plate
    ("bar|baredge", None, "frame", r"fr_bed"), ("bar|baredge", None, "bededge", None),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose):
    k = pose[2]
    items = []
    for pid in v.by_part:
        if present(pid, k):
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
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at W {p[1]:.3f} k {p[2]}" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


def angle_x(mat):
    """A part matrix's turn about x (degrees), from the turned y axis."""
    return math.degrees(math.atan2(mat[2][1], mat[1][1]))


def face_gap(v, pid, pose, leaf_pose=None):
    """How far the panel's underside (its authored y = YB face) lies from the leaf's face plane: the worst
    of its corners, signed (positive: off the leaf's face, on the sheet's side)."""
    m = v.m
    L = v.mat("leaf", leaf_pose or pose)
    n = mvec([row[:3] for row in L[:3]], [0.0, 1.0, 0.0])
    o = [c * 16 for c in _apply(L, [0.0, m.YB / 16, m.EZ / 16])]
    worst = []
    for el in v.by_part[pid]:
        lo, hi = el.aabb()
        for x in (lo[0], hi[0]):
            for z in (lo[2], hi[2]):
                q = v.point(pid, pose, [x, m.YB, z])
                worst.append(sum(n[i] * (q[i] - o[i]) for i in range(3)))
    return min(worst), max(worst)


def check_sheet(v):
    """The sheet follows the leaf: the carried panel lies on the leaf's face through its rise and while the
    leaf falls to SET, then stays at SET; the flanges stand at 90 degrees after each fold; at W 1 the two
    sections are U's lying on the leaf."""
    m = v.m
    worst = 0.0
    for k, cls in ((1, "thin"), (2, "thick")):
        pre = "lc"[k - 1]
        throw = m.THROW[cls]
        for t, pid in ((m.T_FOLD1, f"{pre}a"), (m.T_FOLD2, f"{pre}m")):
            t_set = t[1] + (t[2] - t[1]) * (throw - m.SET) / throw
            for i in range(41):
                w = t[0] + (t_set - t[0]) * i / 40
                lo, hi = face_gap(v, pid, m.pose_at(k, w))
                worst = max(worst, abs(lo), abs(hi))
            # past SET the leaf falls away and the panel holds at SET
            for w in (t_set + 0.01, t[2], t[2] + 0.02):
                a = angle_x(v.mat(pid, m.pose_at(k, w)))
                lo, _ = face_gap(v, pid, m.pose_at(k, w))
                if abs(a - m.SET) > 1e-3 or lo < -1e-4:
                    v.fail(f"{pid} does not hold at {m.SET} degrees off the leaf at W {w:.3f} (angle {a:.3f}, gap {lo:.3f})")
            top = angle_x(v.mat(pid, m.pose_at(k, t[1])))
            if abs(top - throw) > 1e-3:
                v.fail(f"{pid} reaches {top:.3f} degrees, not the throw {throw}")
        # the delivered U: B on the leaf's face, M upright at the north, A over B at the U's height
        b = aabb_of(v.posed(f"{pre}b", m.pose_at(k, 1.0)))
        mm = aabb_of(v.posed(f"{pre}m", m.pose_at(k, 1.0)))
        a = aabb_of(v.posed(f"{pre}a", m.pose_at(k, 1.0)))
        want_b = ([m.HALVES[0][0], m.YB, m.Z_A0], [m.HALVES[1][1], m.YB + m.T, m.EZ])
        want_m = ([m.HALVES[0][0], m.YB, m.Z_A0], [m.HALVES[1][1], m.YB + m.S, m.Z_A0 + m.T])
        want_a = ([m.HALVES[0][0], m.YB + m.S - m.T, m.Z_A0], [m.HALVES[1][1], m.YB + m.S, m.EZ])
        for got, want, name in ((b, want_b, "B"), (mm, want_m, "M"), (a, want_a, "A")):
            err = max(abs(got[i][q] - want[i][q]) for i in range(2) for q in range(3))
            if err > 0.05:
                v.fail(f"the delivered U's {name} panel ({cls}) is at {got}, want {want}")
    print(f"sheet: the carried panel on the leaf's face through each fold to {m.SET:g} degrees (worst {worst:.2e}); "
          f"flanges hold at {m.SET:g}; the delivered U's lie on the leaf")
    if worst > 1e-4:
        v.fail("the carried panel leaves the leaf's face during a fold")


def bar_bottom(v, pose):
    return min(e.aabb()[0][1] for e in v.posed("bar", pose))


def sheet_top(v, pose, pid):
    return max(e.aabb()[1][1] for e in v.posed(pid, pose))


def check_clamp(v):
    """The bar lies on the bed with no plate, on the sheet whenever the leaf moves, LIFT clear of it while
    the sheet is pulled or slid off; each screw's tip stays in its cup and its turn is its thread's."""
    m = v.m
    if abs(bar_bottom(v, m.REST) - m.YB) > 1e-6:
        v.fail("the bar does not lie on the bed at rest")
    for k in (1, 2):
        pre = "lc"[k - 1]
        for t in (m.T_FOLD1, m.T_FOLD2):
            for w in (t[0], (t[0] + t[1]) / 2, t[1], t[2]):
                gap = bar_bottom(v, m.pose_at(k, w)) - (m.YB + m.T)
                if abs(gap) > 1e-6:
                    v.fail(f"the bar is not on the sheet at W {w} (gap {gap:.3f})")
        for t in (m.T_SHIFT, m.T_OFF):
            for w in (t[0], (t[0] + t[1]) / 2, t[1]):
                gap = bar_bottom(v, m.pose_at(k, w)) - max(sheet_top(v, m.pose_at(k, w), f"{pre}b"), m.YB + m.T)
                if gap < m.LIFT - 1e-6:
                    v.fail(f"the bar is not clear of the sheet at W {w} (gap {gap:.3f})")
    worst = {1: 0.0, 2: 0.0}
    thread = 0.0
    for pose in [m.REST] + cycle_poses(m, 0.01):
        k = pose[2]
        for s in ("w", "e"):
            tip = min(e.aabb()[0][1] for e in v.named(f"screw{s}", r"_rod", pose))
            cup = max(e.aabb()[1][1] for e in v.named("bar", rf"_cup_{s}", pose))
            worst[max(k, 1)] = max(worst[max(k, 1)], abs(tip - cup))
            mt = v.mat(f"screw{s}", pose)
            turn = math.atan2(mt[0][2], mt[0][0])        # about y
            rise = mt[1][3] * 16
            # the thread: the screw's rise is its turn times the pitch, give or take whole turns
            frac = (turn / (2 * math.pi) * m.PITCH - rise) / m.PITCH
            thread = max(thread, abs(frac - round(frac)) * m.PITCH)
    print(f"screws: tips in their cups (lead within {worst[1]:.3f}, copper within {worst[2]:.3f}: a quarter turn harder); "
          f"the turn is the thread's rise within {thread:.1e}")
    if worst[1] > 1e-6 or worst[2] > m.EXTRA_TURN["thick"] * m.PITCH + 1e-6 or thread > 1e-4:
        v.fail("a screw's tip leaves its cup, or turns off its thread")


def check_supports(v):
    m = v.m
    frame = v.by_part["frame"]
    for s in ("w", "e"):
        boss = next(f for f in frame if f.name == f"fr_boss_{s}")
        pins = [f for f in frame if f.name.startswith(f"fr_pin_{s}")]
        mg = bearing_margin(boss, pins, 0)
        print(f"support: the leaf's {s} pin in its bearing, margin {mg:.2f}")
        if not (0 < mg < 1e8):
            v.fail(f"the leaf's {s} pin is not in its bearing")
        # the leaf's knuckle round its pin, at rest and at the throw
        for pose in (m.REST, m.pose_at(2, m.T_FOLD1[1])):
            kn = v.named("leaf", rf"_knuckle_{s}", pose)[0]
            lo, hi = kn.aabb()
            if not (lo[1] < m.YB < hi[1] + 0.31 and lo[2] < m.EZ + 0.31):
                v.fail(f"the leaf's {s} knuckle is off its pin")
        # the screw through its nut's four bars, at rest and raised
        bars = [f for f in frame if f.name.startswith(f"fr_nut_{s}_")]
        lo, hi = aabb_of(bars)
        x = m.screw_x(s)
        for pose in (m.REST, m.pose_at(2, 0.0), m.pose_at(2, m.T_FOLD1[1])):
            slo, shi = aabb_of(v.named(f"screw{s}", r"_rod", pose))
            if not (len(bars) == 4 and lo[0] < x < hi[0] and lo[2] < m.SCREW_Z < hi[2] and slo[1] < lo[1] and shi[1] > hi[1]):
                v.fail(f"the {s} screw is not through its nut")
        # the bar's end between the gallows' uprights
        up = [f for f in frame if re.match(rf"^fr_up(front|back)_{s}$", f.name)]
        blo, bhi = aabb_of(v.posed("bar", m.REST))
        ulo, uhi = aabb_of(up)
        if not (ulo[2] < blo[2] and bhi[2] < uhi[2]):
            v.fail(f"the bar's {s} end is not between its uprights")
    print("support: each screw through its nut, the bar's ends between the gallows' uprights, the knuckles on their pins")


def check_lever(v):
    """The lever's hand bar stays in the box over the copper throw, and theta moves nothing."""
    m = v.m
    for th in (0.0, 1.3, -2.9, 40.0):
        for pid in ("lever", "leaf"):
            a = v.mat(pid, (th, 0.3, 1, 1.0))
            b = v.mat(pid, (0.0, 0.3, 1, 1.0))
            if max(abs(a[i][j] - b[i][j]) for i in range(3) for j in range(4)) > 1e-12:
                v.fail(f"theta moves the {pid}")
    hi = max(e.aabb()[1][1] for e in v.posed("lever", v.m.pose_at(2, m.T_FOLD1[1])))
    print(f"lever: theta moves nothing (the lever's clock); the hand bar's top at copper's throw {hi:.2f}")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        k = pose[2]
        els = [e for pid in v.by_part if present(pid, k) for e in v.posed(pid, pose)]
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
    check_containment(v, [m.REST] + cycle_poses(m, 0.01))
    check_sheet(v)
    check_clamp(v)
    check_supports(v)
    check_lever(v)
    check_clearances(v, [m.REST] + cycle_poses(m, 0.02), "clearances")
    if not quick:
        check_clearances(v, cycle_poses(m, 0.0025), "swept paths (every 0.0025 of the cycle, both metals)")
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
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, {hollow} hollow; "
          f"lids over every column: {'yes' if not gaps else gaps}")
    if gaps or hollow:
        ok = False
    if "powerCell" in ship or "powerFace" in ship:
        print("FAIL a hand machine has no power cell")
        ok = False
    return ok
