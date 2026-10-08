"""The squaring shear generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import bearing_margin, box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
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


SHEET = ("lf", "lb", "cf", "cb")


def present(pid, k):
    """The lead sheet shows only with lead on the shear, the copper sheet only with copper."""
    if pid in ("lf", "lb"):
        return k == 1
    if pid in ("cf", "cb"):
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
    (r"^fr_(leg|rail|bed|post|cap|grail|stretcher)", {"oak"}),
    (r"^fr_(boss|pin)", {"iron"}),
    (r"^crosshead_beam", {"oak"}),
    (r"^crosshead_shoe", {"iron"}),
    (r"^treadle_(arm|foot)", {"oak"}),
    (r"^treadle_(knuckle|pin)", {"iron"}),
    (r"^link[we]_", {"iron"}),
    (r"^(upperblade|lowerblade)_", {"blade"}),
    (r"^(holddown|gauge)_", {"gauge"}),
    (r"^l[fb]_", {"lead"}),
    (r"^c[fb]_", {"copper"}),
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
    ("upperblade", None, "crosshead", None),
    ("link[we]", None, "crosshead", None), ("link[we]", None, "treadle", r"_pin"),
    ("treadle", r"_knuckle|_arm", "frame", r"fr_pin"),
    ("lowerblade", None, "frame", r"fr_bed"),
    ("gauge", r"_arm", "frame", r"fr_grail"),
    # the hold-down lying on the table and the cheeks' rails with no plate
    ("holddown", None, "frame", r"fr_bed|fr_rail"), ("holddown", None, "lowerblade", None),
    # the sheet: its halves at the cut, lying on the table, the blades, the gauge, under the hold-down
    ("[lc][fb]", None, "[lc][fb]", None),
    ("[lc][fb]", None, "lowerblade|upperblade|holddown|gauge", None),
    ("[lc][fb]", None, "frame", r"fr_bed"),
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


def blade_edge(v, pose):
    return min(e.aabb()[0][1] for e in v.posed("upperblade", pose))


def check_cut(v):
    """The upper blade stands clear of the sheet at rest and while the halves are drawn off; at the
    bottom of the stroke its edge is past the lower blade's, and it passes behind the lower blade's
    back face, never through it. The treadle's foot goes down as the blade does."""
    m = v.m
    gap = blade_edge(v, m.REST) - (m.YB + m.T)
    if gap < 0.5:
        v.fail(f"the upper blade's edge is only {gap:.2f} over the sheet at rest")
    worst_back = 1e9
    for k in (1, 2):
        bottom = blade_edge(v, m.pose_at(k, m.T_CUT[1]))
        if bottom > m.YB - 0.1:
            v.fail(f"at the bottom of the stroke the upper blade's edge is at {bottom:.3f}, not past the lower blade's {m.YB}")
        for w in (m.T_OFF[0], (m.T_OFF[0] + m.T_OFF[1]) / 2, m.T_OFF[1]):
            if blade_edge(v, m.pose_at(k, w)) - (m.YB + m.T) < 0.5:
                v.fail(f"the upper blade is not clear of the halves as they are drawn off at W {w}")
        for pose in cycle_poses(m, 0.01):
            if pose[2] != k:
                continue
            worst_back = min(worst_back, min(e.aabb()[0][2] for e in v.posed("upperblade", pose)) - m.ZB)
        foot_rest = aabb_of(v.named("treadle", r"_foot", m.pose_at(k, 0.0)))[0][1]
        foot_down = aabb_of(v.named("treadle", r"_foot", m.pose_at(k, m.T_CUT[1])))[0][1]
        if not foot_down < foot_rest - 2.0:
            v.fail(f"the treadle's foot does not go down with the blade ({foot_rest:.2f} to {foot_down:.2f})")
    print(f"cut: the upper blade {gap:.2f} over the sheet at rest, past the lower blade at the bottom of the stroke, "
          f"behind its back face by {worst_back:.3f} throughout")
    if worst_back < 0.02:
        v.fail("the upper blade runs into the lower blade's back face")


def check_sheet(v):
    """The plate lies as laid until the halves are drawn off, and at W 1 the two half plates lie side by
    side on the table, 8 x 4 each."""
    m = v.m
    x0, x1 = m.SHEET_X
    for k, cls in ((1, "thin"), (2, "thick")):
        pre = "lc"[k - 1]
        for W in (0.0, m.T_CUT[1], m.T_OFF[0]):
            f = aabb_of(v.posed(f"{pre}f", m.pose_at(k, W)))
            b = aabb_of(v.posed(f"{pre}b", m.pose_at(k, W)))
            want_f = ([x0, m.YB, m.Z_PLATE[0]], [x1, m.YB + m.T, m.ZB])
            want_b = ([x0, m.YB, m.ZB], [x1, m.YB + m.T, m.Z_PLATE[1]])
            for got, want, name in ((f, want_f, "near"), (b, want_b, "far")):
                err = max(abs(got[i][q] - want[i][q]) for i in range(2) for q in range(3))
                if err > 1e-6:
                    v.fail(f"the {name} half ({cls}) is at {got} at W {W}, want {want}")
        f = aabb_of(v.posed(f"{pre}f", m.pose_at(k, 1.0)))
        b = aabb_of(v.posed(f"{pre}b", m.pose_at(k, 1.0)))
        for got, z0, name in ((f, m.Z_PLATE[0] - m.DRAW, "near"), (b, m.ZB - m.DRAW, "far")):
            want = ([x0, m.YB, z0], [x1, m.YB + m.T, z0 + m.HALF])
            err = max(abs(got[i][q] - want[i][q]) for i in range(2) for q in range(3))
            if err > 1e-6:
                v.fail(f"the delivered {name} half ({cls}) is at {got}, want {want}")
            size = [got[1][q] - got[0][q] for q in range(3)]
            if max(abs(size[q] - (8.0, m.T, m.HALF)[q]) for q in range(3)) > 1e-6:
                v.fail(f"a half plate is {size}, not 8 x 1 x 4")
        if f[0][2] < m.BED_Z[0]:
            v.fail(f"the delivered near half ({cls}) overhangs the table's front")
    print("sheet: the plate as laid until the halves are drawn off; at W 1 two 8 x 4 half plates side by side on the table")


def hold_bottom(v, pose):
    return min(e.aabb()[0][1] for e in v.posed("holddown", pose))


def check_clamp(v):
    """The hold-down lies on the table with no plate, on the sheet through the stroke, LIFT clear of it
    while the halves are drawn off."""
    m = v.m
    if abs(hold_bottom(v, m.REST) - m.YB) > 1e-6:
        v.fail("the hold-down does not lie on the table at rest")
    for k in (1, 2):
        for w in (m.T_CUT[0], (m.T_CUT[0] + m.T_CUT[1]) / 2, m.T_CUT[1], m.T_CUT[2]):
            gap = hold_bottom(v, m.pose_at(k, w)) - (m.YB + m.T)
            if abs(gap) > 1e-6:
                v.fail(f"the hold-down is not on the sheet at W {w} (gap {gap:.3f})")
        for w in (m.T_OFF[0], (m.T_OFF[0] + m.T_OFF[1]) / 2, m.T_OFF[1]):
            gap = hold_bottom(v, m.pose_at(k, w)) - (m.YB + m.T)
            if gap < m.LIFT - 1e-6:
                v.fail(f"the hold-down is not clear of the sheet at W {w} (gap {gap:.3f})")
    print("clamp: the hold-down on the table at rest, on the sheet through the stroke, clear while the halves come off")


def check_links(v):
    """Each link hangs from the crosshead and its slotted eye holds the treadle's pin at every pose: the
    treadle and the crosshead move together."""
    m = v.m
    py, pz = m.link_pin()
    worst = 0.0
    for pose in [m.REST] + cycle_poses(m, 0.01):
        for s in ("w", "e"):
            pin = v.point("treadle", pose, [8.0, py, pz])
            strap = aabb_of(v.posed(f"link{s}", pose))
            eye_y = strap[0][1] + m.PIN_R + 0.45
            dy = abs(pin[1] - eye_y)
            dz_lo, dz_hi = pin[2] - m.PIN_R - strap[0][2], strap[1][2] - (pin[2] + m.PIN_R)
            worst = max(worst, dy)
            if dz_lo < -1e-6 or dz_hi < -1e-6 or dy > 0.2:
                v.fail(f"the {s} link's eye lets go of the treadle's pin at {pose} (dy {dy:.3f}, z margins {dz_lo:.3f}, {dz_hi:.3f})")
                return
    print(f"links: the treadle's pins in the links' eyes over the cycle, the treadle and the crosshead out of step by at most {worst:.3f}")


def check_supports(v):
    m = v.m
    frame = v.by_part["frame"]
    for s in ("w", "e"):
        boss = next(f for f in frame if f.name == f"fr_boss_{s}")
        pins = [f for f in frame if f.name.startswith(f"fr_pin_{s}")]
        mg = bearing_margin(boss, pins, 0)
        print(f"support: the treadle's {s} pin in its boss, margin {mg:.2f}")
        if not (0 < mg < 1e8):
            v.fail(f"the treadle's {s} pin is not in its boss")
        # the knuckle round its pin, at rest and pressed
        for pose in (m.REST, m.pose_at(2, m.T_CUT[1])):
            kn = v.named("treadle", rf"_knuckle_{s}", pose)[0]
            lo, hi = kn.aabb()
            if not (lo[1] < m.PIVOT[0] < hi[1] and lo[2] < m.PIVOT[1] < hi[2]):
                v.fail(f"the treadle's {s} knuckle is off its pin")
        # the crosshead's and the hold-down's ends in their housing's slots, through the cycle
        posts = {n: next(f for f in frame if f.name == f"fr_post{n}_{s}").aabb() for n in ("front", "mid", "back")}
        for pose in (m.REST, m.pose_at(1, 0.0), m.pose_at(2, m.T_CUT[1]), m.pose_at(1, m.T_OFF[0])):
            shoe = aabb_of(v.named("crosshead", rf"_shoe_{s}", pose))
            if not (posts["mid"][1][2] < shoe[0][2] and shoe[1][2] < posts["back"][0][2] and shoe[0][1] > posts["mid"][0][1]
                    and shoe[1][1] < posts["mid"][1][1]):
                v.fail(f"the crosshead's {s} end leaves its slot at {pose}")
            hold = aabb_of(v.posed("holddown", pose))
            if not (posts["front"][1][2] < hold[0][2] and hold[1][2] < posts["mid"][0][2] and hold[1][1] < posts["front"][1][1]):
                v.fail(f"the hold-down's {s} end leaves its slot at {pose}")
    print("support: the treadle's pins in their bosses and knuckles; the crosshead's and hold-down's ends in their slots")


def check_lever(v):
    """theta moves nothing."""
    m = v.m
    for th in (0.0, 1.3, -2.9, 40.0):
        for pid in ("treadle", "crosshead"):
            a = v.mat(pid, (th, 0.3, 1, 1.0))
            b = v.mat(pid, (0.0, 0.3, 1, 1.0))
            if max(abs(a[i][j] - b[i][j]) for i in range(3) for j in range(4)) > 1e-12:
                v.fail(f"theta moves the {pid}")
    lo = min(e.aabb()[0][1] for e in v.posed("treadle", m.pose_at(2, m.T_CUT[1])))
    print(f"treadle: theta moves nothing (the treadle's clock); its lowest point pressed {lo:.2f} (throw {m.treadle_throw():.2f} degrees)")


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
    check_cut(v)
    check_clamp(v)
    check_links(v)
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
