"""The slitting mill generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its rule.
Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in make_shape.py
proves the shipped files are this model moved.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import bearing_margin, box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
from machinegen.checks import supports as shaft_supports
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import part_of, posed

FRAME_W = 1.0 / 60                           # a frame at 60 frames a second with the axle at 1 turn a second, in W


class V:
    def __init__(self, m, els, parts, rig):
        self.m, self.els, self.parts, self.rig = m, els, parts, rig
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        self.cache = {}
        self.ok = True
        self.passed = {}                     # each check run: did it pass (the cause check leans on the work checks)

    def run(self, check, *args):
        ok, self.ok = self.ok, True
        check(self, *args)
        self.passed[check.__name__] = self.ok
        self.ok = ok and self.ok

    def fail(self, msg):
        self.ok = False
        print("FAIL", msg)

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

    def named(self, pid, rx, pose=None):
        src = self.posed(pid, pose) if pose is not None else self.by_part.get(pid, [])
        r = re.compile(rx)
        return [e for e in src if r.search(e.name)]


def present(m, pid, pose):
    """The work is drawn only with a plate on."""
    return not m.is_work(pid) or pose[3] != 0


def visible(m, el):
    return not el.vanished and min(abs(s) for s in el.size) >= 0.05


def cycle(m, step, back=False):
    n = int(round(m.END / step))
    return [m.pose_at(round(i * step, 6), back=back) for i in range(n + 1)]


def idle(m, n=8):
    """No plate: the axle at n angles round a turn, both ways (W stands at 0)."""
    return [(s * math.tau * i / n, math.tau * i / n, 0.0, 0, 0.0) for i in range(n) for s in (1.0, -1.0)]


def span(v, pid, pose):
    els = [e for e in v.posed(pid, pose) if not e.vanished]
    return aabb_of(els) if els else None


def angle_x(v, pid, pose):
    mm = v.mat(pid, pose)
    return math.atan2(mm[2][1], mm[1][1])


# ---------------------------------------------------------------- the basics
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
    idle_parts = [p["id"] for p in v.parts if p["id"] != "frame" and not (p["requires"] or p.get("ride") or p["drivers"])]
    if idle_parts:
        v.fail(f"parts the renderer would never draw (no requires, ride or driver) and not the frame: {idle_parts}")
    counts = {p["id"]: len(v.by_part.get(p["id"], [])) for p in v.parts}
    print(f"parts: {len(v.parts)}, elements {len(v.els)}: " + ", ".join(f"{k} {n}" for k, n in counts.items()))


def check_containment(v, poses):
    m = v.m
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where = -1e9, None
    for pose in poses:
        for pid in v.by_part:
            if not present(m, pid, pose):
                continue
            for el in v.posed(pid, pose):
                if el.vanished:
                    continue
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


TEX_RULES = [
    (r"^(fr_(sill|bearer|tpost|table|box)|entry_oak)", "oak"),
    (r"^(clutchgear_cup|cone_body)", "cupronickel"),
    (r"^fr_chock", "brass"),
    (r"^(fr_(screw_|stud_)|roll(top|bottom)_(cutter|spacer|neck)|knife_blade|bedknife_|crank_(pin|washer)|entrygears_|rectshaft_"
     r"|rectw[12]_catch|crankshaft_(rod|hub)|clutchgear_(body|tooth)|cone_(neck|flange)|pushshaft_(gear|pinion|rod))", "steel"),
    (r"^(fr_(hbase|hpost|hcheek|hcap|screwhead|capstan|ped|brg|std|guide|studboss|epost|forkguide|psh|barstop|rack)|entry_rod"
     r"|crank_disc|knife_(crosshead|ram|yoke)|wheel_|pinion(top|bottom)_|rectw[12]_(gear|hub)|idler_|roll(top|bottom)_collar"
     r"|crankshaft_(disc|tooth)|clutchfork_|pushsector_|pushshaft_(drum|flange)|pushweight_|pushchain_"
     r"|pusher_(head|finger|upright|rack))", "iron"),
    (r"^(plate|rods[lu]|nail[12][lu])_", "metal"),
]


def check_textures(v):
    bad = []
    for el in v.els:
        got = {f["texture"].lstrip("#") for f in el.faces.values()}
        rule = next((tex for rx, tex in TEX_RULES if re.match(rx, el.name)), None)
        if rule is None or not got <= {rule}:
            bad.append((el.name, sorted(got), rule))
    print(f"textures by role (oak timber, the box and the axle's continuation; iron castings, wheels, the knife's crosshead, ram "
          f"and yoke, the fork, the push bar, its sector, drum, weight and chain; steel collars, arbors, blades, pins, shafts, gears and "
          f"pinions; a cupronickel cone clutch; brass chocks; "
          f"the work 'metal'): {len(bad)} elements break them")
    if bad:
        v.fail(f"textures by role: {bad[:5]}")


def check_anchors(v):
    m, rig = v.m, v.rig
    pc = tuple(rig["powerCell"])
    _, hi = aabb_of(v.named("entry", r"_oak"))
    want_y, want_z = (pc[1] + 0.5) * 16, (pc[2] + 0.5) * 16
    print(f"anchors: power cell {pc} face {rig['powerFace']}; the entry reaches x {hi[0]:.2f} on that face, axis (y {m.E[0]}, z {m.E[1]})")
    if pc != m.POWER_CELL or rig["powerFace"] != "east" or abs(hi[0] - (pc[0] + 1) * 16) > 0.01 or (m.E[0], m.E[1]) != (want_y, want_z):
        v.fail("the entry shaft does not meet the power face at the cell's centre")
    for key in m.ANCHORS:
        p = rig[key]["pos"]
        if not all(0 <= p[i] <= (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)[i] for i in range(3)):
            v.fail(f"the {key} anchor is outside the machine")
    if rig["outputSide"] != "south" or rig["output"]["pos"][2] < m.CELLS_Z - 0.01 or rig["infeedSide"] != "north":
        v.fail("the output is not the nail box's south face, or the infeed not the table's north end")
    nip, knife = rig["nip"]["pos"], rig["knife"]["pos"]
    if abs(nip[2] * 16 - m.Z_N) > 1e-3 or abs(knife[2] * 16 - m.Z_K) > 1e-3:
        v.fail("the nip and knife anchors are not at the nip and the cut")


# ---------------------------------------------------------------- the drive
def theta_pairs(m):
    """(gear a's part, element regex, centre, pitch radius, teeth; the same for b): the pairs the axle turns."""
    rp = m.RECT_MOD / 2
    return [
        (("entrygears", r"_a1_tooth", m.E, m.A1_T * rp, m.A1_T), ("rectw1", r"_gear_tooth", m.R, m.W1_T * rp, m.W1_T)),
        (("entrygears", r"_a2_tooth", m.E, m.A2_T * rp, m.A2_T), ("idler", r"_gear_tooth", m.IDL, m.I_T * rp, m.I_T)),
        (("idler", r"_gear_tooth", m.IDL, m.I_T * rp, m.I_T), ("rectw2", r"_gear_tooth", m.R, m.W2_T * rp, m.W2_T)),
        (("rectshaft", r"_gear_tooth", m.R, m.SK_T * rp, m.SK_T), ("clutchgear", r"clutchgear_tooth", m.K, m.SK_T * rp, m.SK_T)),
    ]


def roll_mismatch(v, a, b, s1, s2):
    (pa, _ra, _ca, rpa, _na), (pb, _rb, _cb, rpb, _nb) = a, b
    arc_a = math.remainder(angle_x(v, pa, s2) - angle_x(v, pa, s1), math.tau) * rpa
    arc_b = math.remainder(angle_x(v, pb, s2) - angle_x(v, pb, s1), math.tau) * rpb
    return abs(arc_a + arc_b)


def teeth_hits(v, a, b, poses, eps=0.1):
    (pa, ra, *_), (pb, rb, *_) = a, b
    return sum(1 for pose in poses for x in v.named(pa, ra, pose) for y in v.named(pb, rb, pose) if obb_obb(x, y, eps=eps))


def check_gearing(v):
    """Every meshing pair: centres the sum of the pitch radii apart, the arcs rolled on the pitch circles equal and
    opposite (finite differences of the posed rig), no tooth running into the other's. The axle's pairs over the axle
    either way, the pinions over the job; the mutilated gear and the great wheel over each step. The rectifier:
    whichever way the axle turns, exactly one loose wheel turns with the rectified shaft, always the same way."""
    m = v.m
    worst = 0.0
    for a, b in theta_pairs(m):
        d = math.hypot(a[2][0] - b[2][0], a[2][1] - b[2][1])
        if abs(d - (a[3] + b[3])) > 1e-6:
            v.fail(f"{a[0]} and {b[0]}: centres {d:.4f} apart, pitch radii add to {a[3] + b[3]:.4f}")
        for th in (0.3, -0.3, 2.0, -5.1):
            s1, s2 = (th, abs(th), 0.0, 0, 0.0), (th + 0.05 * (1 if th > 0 else -1), abs(th) + 0.05, 0.0, 0, 0.0)
            mm = roll_mismatch(v, a, b, s1, s2)
            worst = max(worst, mm)
            if mm > 1e-5:
                v.fail(f"{a[0]} and {b[0]} slip at theta {th}")
        poses = [(sgn * (0.37 + i * math.tau / max(a[4], b[4]) / 6), 0.37 + i * math.tau / max(a[4], b[4]) / 6, 0.0, 0, 0.0)
                 for i in range(24) for sgn in (1.0, -1.0)]
        hits = teeth_hits(v, a, b, poses)
        if hits:
            v.fail(f"{a[0]} and {b[0]}: teeth run into each other ({hits} times over 48 angles)")
    # the pinions, over the job
    t, bt = (m.Y_T, m.Z_N), (m.Y_B, m.Z_N)
    pins = (("piniontop", r"_tooth", t, m.R_PIN, m.PIN_T), ("pinionbottom", r"_tooth", bt, m.R_PIN, m.PIN_T))
    if abs(m.ARB_D - 2 * m.R_PIN) > 1e-9:
        v.fail("the pinions' pitch circles do not meet")
    ws = [i * 0.01 for i in range(int(m.END * 100) + 1)]
    for W in ws[::7]:
        mm = roll_mismatch(v, *pins, m.pose_at(W), m.pose_at(W + 0.004))
        worst = max(worst, mm)
        if mm > 1e-5:
            v.fail(f"the pinions slip at W {W}")
    hits = teeth_hits(v, *pins, [m.pose_at(W) for W in ws[::3]])
    if hits:
        v.fail(f"the pinions' teeth run into each other ({hits} times)")
    print(f"gearing: the rectifier's three meshes, the clutch gears and the pinions at their pitch radii, worst roll mismatch "
          f"{worst:.1e}, no teeth running into each other")
    for sgn in (1.0, -1.0):
        s1, s2 = (sgn * 1.0, 1.0, 0.0, 0, 0.0), (sgn * 1.05, 1.05, 0.0, 0, 0.0)
        shaft = math.remainder(angle_x(v, "rectshaft", s2) - angle_x(v, "rectshaft", s1), math.tau)
        with_it = [w for w in ("rectw1", "rectw2")
                   if abs(math.remainder(angle_x(v, w, s2) - angle_x(v, w, s1), math.tau) - shaft) < 1e-9]
        if shaft >= 0 or len(with_it) != 1:
            v.fail(f"the rectifier: with the axle turning {'+' if sgn > 0 else '-'}, the shaft turns {shaft:.4f} and {with_it} with it")
    print("rectifier: whichever way the axle turns, one loose wheel carries the rectified shaft, always the same way")


def check_feed_train(v):
    """The mutilated gear and the great wheel: their centres at the pitch radii; the wheel stands while the sector is out
    of mesh and turns at the gear ratio while it is in; their teeth never run into each other over the job (every 0.0025
    of a stroke); a step is five of the wheel's teeth, and a job turns the arbors half a turn, which every pattern on
    them, the pinions and the wheel repeats in. The cone turns with the cup at the drawn pace, is in the cup while the
    plate is on and out with none, and its fork rides its neck; p easing in at the start and out at the end turns nothing."""
    m = v.m
    t = (m.Y_T, m.Z_N)
    rm, rg = m.pr(m.M_T, m.FEED_MOD), m.pr(m.G_T, m.FEED_MOD)
    d = math.hypot(m.K[0] - t[0], m.K[1] - t[1])
    if abs(d - (rm + rg)) > 1e-6:
        v.fail(f"the mutilated gear and the wheel: centres {d:.4f} apart, pitch radii add to {rm + rg}")
    worst_dwell, worst_rate = 0.0, 0.0
    for i in range(int(m.END / 0.0025)):
        W = i * 0.0025
        a, b = m.pose_at(W), m.pose_at(W + 0.0025)
        dg = math.remainder(angle_x(v, "wheel", b) - angle_x(v, "wheel", a), math.tau)
        dk = math.remainder(angle_x(v, "crankshaft", b) - angle_x(v, "crankshaft", a), math.tau)
        in_step = any(s - m.HALF_STEP + 1e-9 < W and W + 0.0025 < s + m.HALF_STEP - 1e-9 for s in m.STEP_AT)
        out_step = all(W + 0.0025 <= s - m.HALF_STEP + 1e-9 or W >= s + m.HALF_STEP - 1e-9 for s in m.STEP_AT)
        if out_step:
            worst_dwell = max(worst_dwell, abs(dg))
        if in_step:
            worst_rate = max(worst_rate, abs(dg + dk * rm / rg))
    hits = teeth_hits(v, ("crankshaft", r"_tooth|_disc"), ("wheel", r"_tooth|_rim"), [m.pose_at(i * 0.0025) for i in range(int(m.END / 0.0025) + 1)], eps=0.05)
    print(f"feed train: the wheel stands out of mesh (worst {worst_dwell:.1e} rad), turns at the gear ratio in mesh (worst {worst_rate:.1e}); "
          f"the sector's teeth run into the wheel's {hits} times over the job")
    if worst_dwell > 1e-5 or worst_rate > 1e-4 or hits:
        v.fail("the mutilated gear does not step the great wheel cleanly")
    if abs(m.STEP_ANGLE - math.tau * m.ADVANCE / m.G_T) > 1e-12 or abs(2 * m.HALF_STEP * m.M_T - m.ADVANCE) > 1e-12:
        v.fail("a step is not whole teeth of the wheel, or not as long as the sector takes to pass that many")
    for name, sym in (("cutters", math.tau / (2 * m.CUT_K)), ("spacers", math.tau / (2 * m.SPACER_K)), ("pinions", math.tau / m.PIN_T),
                      ("wheel", math.tau / m.G_T), ("necks", math.tau / 8), ("push sector's runs", math.pi)):
        r = (m.ARB_TURN / sym) % 1.0
        if min(r, 1 - r) > 1e-9:
            v.fail(f"a job's half turn of the arbors does not repeat the {name}' pattern: the next plate would make them jump")
    # p easing at the start and the end
    worst_p = 0.0
    for W in (0.0, m.END):
        base = {q["id"]: v.mat(q["id"], m.pose_at(W)) for q in v.parts if not m.is_work(q["id"]) and q["id"] not in ("cone", "clutchfork")}
        for p in (0.0, 0.3, 0.7):
            for pid, mm in base.items():
                o = v.mat(pid, m.pose_at(W, p=p))
                worst_p = max(worst_p, max(abs(o[i][j] - mm[i][j]) for i in range(3) for j in range(4)))
    k0 = v.mat("crankshaft", m.pose_at(m.END))
    if max(abs(k0[i][j] - (1.0 if i == j else 0.0)) for i in range(3) for j in range(4)) > 1e-4:
        v.fail("the crankshaft is not where it started at the job's end")
    print(f"feed train: a job turns the arbors {math.degrees(m.ARB_TURN):.0f} degrees, which every pattern on them repeats in; "
          f"p easing at the start and the end moves the drive by {worst_p:.1e}")
    if worst_p > 1e-4:
        v.fail("p easing in or out turns the drive")
    # the clutch
    worst = 0.0
    for back in (False, True):
        a0 = None
        for W in [i * 0.05 for i in range(int(m.END / 0.05) + 1)]:
            pose = m.pose_at(W, back=back)
            diff = math.remainder(angle_x(v, "cone", pose) - angle_x(v, "clutchgear", pose), math.tau)
            a0 = diff if a0 is None else a0
            worst = max(worst, abs(math.remainder(diff - a0, math.tau)))
    cone_in = aabb_of(v.named("cone", r"_body", m.pose_at(1.0)))
    cone_out = aabb_of(v.named("cone", r"_body", m.pose_at(1.0, p=0.0)))
    cup = aabb_of(v.named("clutchgear", r"_cup\d", m.pose_at(1.0)))
    engaged = cone_in[0][0] < cup[1][0] - 0.3 and cone_out[0][0] > cup[1][0] + 0.05
    fork = aabb_of(v.named("clutchfork", r"_prong", m.pose_at(1.0)))
    neck = aabb_of(v.named("cone", r"_neck", m.pose_at(1.0)))
    rides = neck[0][0] - 1e-6 <= fork[0][0] and fork[1][0] <= neck[1][0] + 1e-6
    print(f"clutch: the cone turns with the cup at the drawn pace (worst {worst:.1e}), {cone_in[0][0] - cup[1][0]:.2f} into it with the "
          f"plate on and {cone_out[0][0] - cup[1][0]:.2f} clear with none; the fork on the cone's neck: {'yes' if rides else 'NO'}")
    if worst > 1e-4 or not engaged or not rides:
        v.fail("the clutch does not engage as drawn")


def check_feed(v):
    """The rolls feed the plate: in each step the bottom arbor's top and the top arbor's bottom carry the work south a nail's
    length, as fast as the work goes, and stand between steps."""
    m = v.m
    worst = 0.0
    for W in [i * 0.01 for i in range(int(m.END * 100))]:
        a, b = m.pose_at(W), m.pose_at(W + 0.01)
        want = m.feed(W + 0.01) - m.feed(W)
        for pid, sgn in (("rollbottom", 1.0), ("rolltop", -1.0)):
            # the rim's travel at the nip, south: the bottom arbor's top and the top arbor's bottom
            arc = sgn * math.remainder(angle_x(v, pid, b) - angle_x(v, pid, a), math.tau) * m.R_CUT
            worst = max(worst, abs(arc - want))
    print(f"feed: the cutters' rims carry the work south {m.L_NAIL} a step at the work's pace, standing between (worst {worst:.1e}; "
          f"rim radius {m.R_CUT:.4f}, overlap {m.DELTA:.3f})")
    if worst > 1e-3:
        v.fail("the rolls and the work do not move together")


def check_slitters(v):
    m = v.m
    sp_vertex = m.R_SPACER / math.cos(math.pi / (2 * m.SPACER_K))
    low_clear = m.LOW_BOT - (m.Y_B + sp_vertex)
    up_clear = (m.Y_T - sp_vertex) - m.UP_TOP
    print(f"slitters: overlap {m.DELTA:.3f}; the lower layer's top on the top cutters' rims ({m.LOW_TOP:.4f}), {low_clear:.3f} over "
          f"the bottom spacers; the upper layer's bottom on the bottom cutters' ({m.UP_BOT:.4f}), {up_clear:.3f} under the top spacers")
    if m.DELTA < 0.3 or low_clear < 0.05 or up_clear < 0.05 or m.R_SPACER <= m.R_ARBOR:
        v.fail("the slitters do not cut, or a rod runs on a spacer")
    for i in range(m.RODS):
        if m.top_cutter(i + 1) != (i % 2 == 0):
            v.fail(f"rod {i} is not under the cutter of its layer")


# ---------------------------------------------------------------- the work and the cut
def check_work(v):
    """The plate goes into the nip a nail's length a step and is gone after the fourth; the rods grow out of the nip to the
    cut in two; each nail grows past the cut in its step; nothing of it shows before; the strips and nails go at the
    hand-out; no sliver shows at presence 0."""
    m = v.m
    for back in (False, True):
        for W in [round(0.025 * i, 4) for i in range(int(m.END / 0.025) + 1)]:
            pose = m.pose_at(W, back=back)
            pl = span(v, "plate", pose)
            want = m.PLATE_L - m.feed(W, m.PLATE_STEPS)
            got = 0.0 if pl is None else pl[1][2] - pl[0][2]
            if abs(got - want) > 1e-4 or (pl and abs(pl[1][2] - m.Z_N) > 1e-6):
                v.fail(f"the plate at W {W}: {got:.4f} long, want {want:.4f}, front at the nip")
            for lay in ("l", "u"):
                rods = span(v, f"rods{lay}", pose)
                want = m.feed(W, m.ROD_STEPS) + m.D_KNIFE * push_frac(m, W)
                got = 0.0 if rods is None else rods[1][2] - m.Z_N
                if W < m.END - m.TAKE_OFF and abs(max(got, m.EPS_ROD) - max(want, m.EPS_ROD)) > m.EPS_ROD + 1e-6:
                    v.fail(f"the rods ({lay}) at W {W}: front {got:.4f} past the nip, want {want:.4f}")
            for j in (1, 2):
                s = m.NAIL_STEP[j]
                for lay in ("l", "u"):
                    n = span(v, f"nail{j}{lay}", pose)
                    if n is None:
                        continue
                    length = n[1][2] - n[0][2]
                    if W <= m.STEP_AT[s] - m.HALF_STEP - 0.002 and length > 0.05:
                        v.fail(f"nail {j}{lay} shows at W {W} before the rods reach the cut")
                    if m.STEP_AT[s] - m.HALF_STEP <= W < m.END - m.TAKE_OFF:
                        want = m.L_NAIL * m.step_frac(W, s)
                        if abs(n[0][2] - m.Z_K) > 1e-4 or abs(length - want) > m.EPS_NAIL + 1e-6:
                            v.fail(f"nail {j}{lay} at W {W}: z {n[0][2]:.4f}..{n[1][2]:.4f}, want from the cut, {want:.3f} long")
        end = m.pose_at(m.END, back=back)
        if any(not e.vanished for pid in v.by_part if m.is_work(pid) and pid != "plate" for e in v.posed(pid, end)):
            v.fail("the strips or the nails are still there at the hand-out")
    for pid in v.by_part:
        if m.is_work(pid) and pid != "plate":
            for pose in (m.pose_at(0.0, p=0.0), m.pose_at(0.0, p=0.5), m.pose_at(m.END, p=0.0)):
                if any(visible(m, e) for e in v.posed(pid, pose)):
                    v.fail(f"{pid} shows at {pose}")
    print(f"work: the plate into the nip {m.L_NAIL} a step, gone after {m.PLATE_STEPS}; the rods out of it to the cut in {m.ROD_STEPS}; "
          f"a nail a rod in steps {[m.NAIL_STEP[j] for j in (1, 2)]}; strips and nails taken off at {m.END:g}; no sliver before; both ways round")


def blade_edge(v, pose):
    return aabb_of(v.named("knife", r"_blade", pose))[0][1]


def check_dwell(v):
    """No work moves while the knife's blade is in the rods' path: over the job (every 0.0025 of a stroke, both ways),
    between any two poses at which the blade's edge is below the upper layer's top, the plate, the rods and the nails
    that have not parted stand still along the feed; the feed steps happen with the edge clear above the rods."""
    m = v.m
    worst, inpath, clear_min = 0.0, 0, 1e9
    for back in (False, True):
        prev = None
        for i in range(int(m.END / 0.0025) + 1):
            W = i * 0.0025
            pose = m.pose_at(W, back=back)
            edge = blade_edge(v, pose)
            state = []
            for pid in ("plate", "rodsl", "rodsu", "nail1l", "nail1u", "nail2l", "nail2u"):
                s = span(v, pid, pose)
                state.append((s[0][2], s[1][2]) if s else (0.0, 0.0))
            if any(s - m.HALF_STEP <= W <= s + m.HALF_STEP for s in m.STEP_AT):
                clear_min = min(clear_min, edge - m.UP_TOP)
            if edge < m.UP_TOP + 0.05 and W < m.END - m.TAKE_OFF - 1e-9:
                inpath += 1
                if prev is not None:
                    # a nail's z doesn't change as it is pressed down or falls: everything here must stand
                    worst = max(worst, max(abs(a - b) for sa, sb in zip(state, prev) for a, b in zip(sa, sb)))
                prev = state
            else:
                prev = None
    print(f"dwell: with the blade in the rods' path ({inpath} poses) the work moves along the feed by at most {worst:.1e}; "
          f"in the feed steps the edge is at least {clear_min:.3f} over the rods")
    if worst > 1e-5 or clear_min < 0.3:
        v.fail("the work moves while the knife is down, or the feed runs with the blade in its path")


def check_parting(v):
    """Each nail parts as the blade goes through it: from the moment the edge comes down onto the nail's top it presses
    the nail down with it (the nail's top within 0.03 of the edge, both layers, both nails), and the nail falls free the
    instant the edge has gone through it, within a frame (at an axle turn a second) of the edge reaching the rod's bottom;
    before the edge comes down the nail lies in its layer, joined to its rod."""
    m = v.m
    worst_follow, worst_part = 0.0, 0.0
    for j, w_c in zip((1, 2), m.CUTS):
        for lay in ("l", "u"):
            pid = f"nail{j}{lay}"
            top0 = m.nail_top(lay)
            marks = m.cut_marks(lay, j)
            w0, w1 = marks[0], marks[-1]
            for i in range(41):
                W = w0 + (w1 - w0) * i / 40
                pose = m.pose_at(W)
                top = span(v, pid, pose)[1][1]
                worst_follow = max(worst_follow, abs(top - min(top0, blade_edge(v, pose))))
            before = span(v, pid, m.pose_at(w0 - 0.01))[1][1]
            if abs(before - top0) > 1e-6:
                v.fail(f"{pid} moves before the knife reaches it")
            # the edge reaches the rod's bottom (the nail's top, pressed down by the rod's thickness)
            w_through = w_c - m.before_bottom(top0 - 1.0)
            # the nail falls free: below the pressed depth, the first sample at which it is lower than the edge requires
            fall_start = None
            for i in range(400):
                W = w0 + i * 0.0005
                pose = m.pose_at(W)
                top = span(v, pid, pose)[1][1]
                if top < min(top0, blade_edge(v, pose)) - 0.02:
                    fall_start = W
                    break
            worst_part = max(worst_part, abs((fall_start or 1e9) - w_through))
            print(f"parting: {pid} pressed by the edge from W {w0:.4f}, through at {w_through:.4f}, free from {fall_start:.4f}")
    print(f"parting: the nails' tops follow the edge within {worst_follow:.3f}; each falls free within {worst_part:.4f} W "
          f"(a frame is {FRAME_W:.4f}) of the edge going through its rod")
    if worst_follow > 0.03 or worst_part > FRAME_W:
        v.fail("a nail does not part as the blade goes through it")


def check_knife(v):
    """The crank pin stays in the yoke's slot; the knife's edge comes down past the bed knife and under the lower layer at
    every whole stroke and stands clear over the upper layer at the half; its crosshead never comes down to the rods; its
    ends stay in the housings' slots; the blade passes behind the bed knife's back face."""
    m = v.m
    worst_y, worst_z = 0.0, 1e9
    zl, zh = m.K[1] - m.R_CRANK - m.PIN_R - 0.15, m.K[1] + m.R_CRANK + m.PIN_R + 0.15
    for i in range(73):
        pose = m.pose_at(i / 72)
        pin = aabb_of(v.named("crank", r"_pin", pose))
        pc = [(pin[0][k] + pin[1][k]) / 2 for k in range(3)]
        slot_c = v.point("knife", pose, [16.0, m.K[0], m.K[1]])
        worst_y = max(worst_y, abs(pc[1] - slot_c[1]))
        worst_z = min(worst_z, pin[0][2] - zl, zh - pin[1][2])
    print(f"knife: the crank pin's centre within {worst_y:.2e} of the yoke's slot's middle, its ends {worst_z:.3f} inside the slot")
    if worst_y > 1e-3 or worst_z < 0.02:
        v.fail("the crank pin leaves the yoke's slot")
    head = lambda pose: aabb_of(v.named("knife", r"_crosshead", pose))[0][1]    # noqa: E731
    down, up = blade_edge(v, m.pose_at(0.0)), blade_edge(v, m.pose_at(0.5))
    lowest_head = min(head(m.pose_at(i / 72)) for i in range(72))
    print(f"knife: edge {down:.3f} at the bottom ({m.LOW_BOT - down:.3f} under the lower layer), {up:.3f} at the top "
          f"({up - m.UP_TOP:.3f} over the upper layer); the crosshead no lower than {lowest_head - m.UP_TOP:.3f} over the upper layer")
    if down > m.LOW_BOT - 0.2 or up < m.UP_TOP + 0.5 or lowest_head < m.UP_TOP + 0.3:
        v.fail("the knife does not cut through both layers, or does not clear them, or its crosshead comes down to them")
    for W in m.CUTS:
        if abs(blade_edge(v, m.pose_at(W)) - down) > 1e-4:
            v.fail(f"the knife is not at the bottom at W {W}")
    blade = v.named("knife", r"_blade")[0].aabb()
    bed = v.named("bedknife", r"_bar")[0].aabb()
    if blade[0][2] - bed[1][2] < 0.02:
        v.fail("the blade rubs the bed knife's back face")
    frame = {e.name: e.aabb() for e in v.by_part["frame"]}
    for i in range(37):
        pose = m.pose_at(i / 36)
        xh = aabb_of(v.named("knife", r"_crosshead", pose))
        for s, x_in in (("w", xh[0][0]), ("e", xh[1][0])):
            ch_f, ch_b = frame[f"fr_hcheekf_{s}"], frame[f"fr_hcheekb_{s}"]
            in_slot = ch_f[0][0] - 1e-9 <= x_in <= ch_f[1][0] + 1e-9 and ch_f[1][2] < xh[0][2] and xh[1][2] < ch_b[0][2] \
                and ch_f[0][1] < xh[0][1] and xh[1][1] < ch_f[1][1]
            if not in_slot:
                v.fail(f"the knife's crosshead leaves its {s} slot at W {pose[2]:.3f}")
                return
    print(f"knife: the blade {blade[0][2] - bed[1][2]:.3f} behind the bed knife's back face; the crosshead's ends in the housings' "
          f"slots all round; at the bottom at both cuts")


def push_frac(m, W):
    return min(1.0, max(0.0, (W - m.PUSH[0]) / (m.PUSH[1] - m.PUSH[0])))


def output_box(m):
    """The output: the nail box's inside, where the player takes the nails and strips from."""
    t = m.BOX_WALL
    return ([m.BOX_X[0] + t, m.BOX_FLOOR, m.Z_K], [m.BOX_X[1] - t, m.BOX_TOP, m.BOX_Z[1] - t])


KEEP_OUT = ("rolltop", "rollbottom", "knife", "bedknife", "crank", "pusher")


def check_removal(v):
    """Nothing of the work shrinks or vanishes inside the machine: over the job (every 0.0025 of a stroke, both ways), any
    piece that gets smaller does so only (a) the plate, being turned into rods at the nip (its front stays there), or
    (b) at the hand-out, lying wholly inside the nail box, clear of the slitters, the knife, the bed knife, the push bar and
    every part of the frame but the box (and the nails and shelf it lies on)."""
    m = v.m
    lo_o, hi_o = output_box(m)
    keep = [e for pid in KEEP_OUT for e in v.by_part[pid]]
    frame = [e for e in v.by_part["frame"] if not e.name.startswith("fr_box")]
    work = [pid for pid in v.by_part if m.is_work(pid)]
    removed, worst = 0, 0.0
    for back in (False, True):
        prev = None
        for i in range(int(m.END / 0.0025) + 1):
            W = i * 0.0025
            pose = m.pose_at(W, back=back)
            cur = {pid: v.posed(pid, pose) for pid in work}
            if prev is not None:
                for pid in work:
                    for a, b in zip(prev[1][pid], cur[pid]):
                        va, vb = math.prod(abs(x) for x in a.size), math.prod(abs(x) for x in b.size)
                        if vb >= va - 1e-9:
                            continue
                        if pid == "plate":
                            if abs(b.aabb()[1][2] - m.Z_N) > 1e-6 and not b.vanished:
                                v.fail(f"the plate shrinks away from the nip at W {W}")
                            continue
                        removed += 1
                        lo, hi = a.aabb()
                        out = max(max(lo_o[k] - lo[k], hi[k] - hi_o[k]) for k in range(3))
                        worst = max(worst, out)
                        if out > 1e-6:
                            v.fail(f"{a.name} shrinks or vanishes at W {W} outside the nail box (by {out:.3f})")
                            return
                        others = [e for e in [posed(el, v.mat(el.part, prev[0])) for el in keep] + frame if obb_obb(a, e)]
                        if others:
                            v.fail(f"{a.name} is taken off at W {W} while touching {[e.name for e in others[:3]]}")
                            return
            prev = (pose, cur)
    print(f"removal: {removed} shrinkings of the work over the job both ways, every one in the nail box clear of the slitters, the "
          f"knife and the push bar (worst {worst:.1e} outside it); the plate shrinks only into the nip")


def check_pusher(v):
    """The push bar and the strips: the bar stands at rest, against the plate's tail as laid, while the plate is fed in
    the first steps; in the catch-up step it is driven up behind the plate's tail, never past it, and meets it at the
    step's end, the tail then at the nip; it stands there until the push, in which its fingers' front is the strips'
    tails all the way; it never reaches past the cut, so it is never in the knife's path; it is back at rest by the end.
    The strips are pushed wholly past the cut while the knife is clear above them, and fall only once their tails are
    past the bed knife."""
    m = v.m
    front = lambda pose: aabb_of(v.named("pusher", r"_finger", pose))[1][2]    # noqa: E731
    worst, reach, behind = 0.0, -1e9, 1e9
    for back in (False, True):
        for i in range(int(m.END / 0.005) + 1):
            W = i * 0.005
            pose = m.pose_at(W, back=back)
            f = front(pose)
            reach = max(reach, f)
            if W <= m.BAR_WIN[0]:
                worst = max(worst, abs(f - m.PUSH_REST))
            elif W <= m.BAR_WIN[1]:
                pl = span(v, "plate", pose)
                behind = min(behind, pl[0][2] - f)
            elif W <= m.PUSH[0]:
                worst = max(worst, abs(f - m.Z_N))
            elif W <= m.PUSH[1]:
                tails = [span(v, f"rods{lay}", pose)[0][2] for lay in ("l", "u")]
                worst = max(worst, max(abs(f - t) for t in tails))
            elif W <= m.PUSH_BACK[0]:
                worst = max(worst, abs(f - m.Z_K))
            elif W >= m.PUSH_BACK[1]:
                worst = max(worst, abs(f - m.PUSH_REST))
    met = front(m.pose_at(m.BAR_WIN[1])) - m.Z_N      # the plate's tail is at the nip as the step ends
    rest = (front(m.pose_at(0.0)), front(m.pose_at(m.END)), front(m.pose_at(m.END, p=0.0)))
    clear = min(blade_edge(v, m.pose_at(m.PUSH[0] + (m.PUSH[1] - m.PUSH[0]) * i / 20)) for i in range(21)) - m.UP_TOP
    tail_end = min(span(v, f"rods{lay}", m.pose_at(m.PUSH[1]))[0][2] for lay in ("l", "u"))
    print(f"push bar: at rest, at the nip and at the cut where it stands, and on the strips' tails as it pushes, within "
          f"{worst:.1e}; in the catch-up at least {behind:.3f} behind the plate's tail, meeting it at the step's end ({met:+.1e}); "
          f"reaches z {reach:.3f} at most (the cut {m.Z_K}, the blade {m.KNIFE_Z[0]}); at rest at {rest[0]:.2f} at the start and "
          f"{rest[1]:.2f} at the end; the strips' tails at {tail_end:.3f} when pushed, the edge at least {clear:.3f} over them "
          f"throughout; they fall from W {m.STRIP_FALL[0]:.3f}, the push done at {m.PUSH[1]:.3f}")
    if worst > 1e-4 or behind < -1e-6 or abs(met) > 1e-4 or reach > m.Z_K + 1e-6 or max(abs(r - m.PUSH_REST) for r in rest) > 1e-4 \
            or tail_end < m.Z_K - 1e-6 or clear < 0.05 or m.STRIP_FALL[0] < m.PUSH[1] - 1e-9:
        v.fail("the push bar does not push the strips out as drawn")


def gap(a, b, lo=-0.5, hi=0.5):
    """About how far apart two elements are (negative: how far into each other), by growing them until they meet."""
    if not obb_obb(a, b, eps=lo):
        return -2 * lo
    for _ in range(20):
        mid = (lo + hi) / 2
        if obb_obb(a, b, eps=mid):
            lo = mid
        else:
            hi = mid
    return -2 * lo


def nearest(xs, ys, centre, reach):
    """The closest gap between any of xs and any of ys, those of xs within `reach` of `centre` (y, z) only."""
    best = 9.0
    for a in xs:
        if math.hypot(a.c[1] - centre[0], a.c[2] - centre[1]) > reach:
            continue
        ra = math.hypot(*a.size) / 2
        for b in ys:
            if math.dist(a.c, b.c) - ra - math.hypot(*b.size) / 2 < min(best, 1.0):
                best = min(best, gap(a, b))
    return best


CONTACT = 0.05                               # a driving tooth is within this of the tooth it drives


def check_push_drive(v):
    """The push bar's drive. The push sector, keyed on the top arbor, and the push gear: centres the sum of their pitch
    radii apart; the push shaft turns only while a run of the sector is in mesh, at the gear ratio, with one of the run's
    teeth in contact with the gear's (within CONTACT), and back only once the run has let go (no tooth of the sector inside
    the gear's tip circle); their teeth never run into each other over the job. The pinion and the rack: the bar travels
    the pinion's pitch radius times its turn, wherever it moves a pinion tooth in contact with a rack tooth, their teeth
    never running into each other. The return weight rises and falls the drum's radius times its turn, its chain from the
    drum's side to the weight's top all the way. The catch-up is the plate's length and the push the strips'."""
    m = v.m
    t = (m.Y_T, m.Z_N)
    d = math.hypot(m.Q[0] - t[0], m.Q[1] - t[1])
    if abs(d - (m.R_PS + m.R_PG)) > 1e-6:
        v.fail(f"the push sector and gear: centres {d:.4f} apart, pitch radii add to {m.R_PS + m.R_PG}")
    if abs(m.PUSH_REST + m.BAR_V - m.Z_N) > 1e-9 or abs(m.PHI_PUSH * m.R_PP - m.D_KNIFE) > 1e-9:
        v.fail("the catch-up does not bring the bar to the nip, or the push is not the strips' length")
    ratio = m.R_PS / m.R_PG
    tip_g = m.R_PG + m.PUSH_MOD
    worst_rate, worst_contact, worst_rack, worst_rack_contact, worst_chain = 0.0, -9.0, 0.0, -9.0, 0.0
    grip_back, hits_s, hits_r = 9.0, 0, 0
    drive_w, back_w = [], []
    dw = 0.0025
    prev = None
    for i in range(int(m.END / dw) + 1):
        W = i * dw
        pose = m.pose_at(W)
        a_s, a_q = angle_x(v, "pushsector", pose), angle_x(v, "pushshaft", pose)
        z_bar = v.mat("pusher", pose)[2][3] * 16
        y_w = v.mat("pushweight", pose)[1][3] * 16
        st = v.named("pushsector", r"_tooth|_plate", pose)
        gt = v.named("pushshaft", r"_gear_", pose)
        hits_s += sum(1 for a in st if math.hypot(a.c[1] - m.Q[0], a.c[2] - m.Q[1]) < tip_g + 1.0 for b in gt if obb_obb(a, b, eps=0.05))
        pt_ = v.named("pushshaft", r"_pinion_tooth", pose)
        rk = v.named("pusher", r"_rack\d", pose)
        hits_r += sum(1 for a in pt_ if a.c[1] > m.Q[0] for b in rk if obb_obb(a, b, eps=0.05))
        # the chain: from the drum's side, at the shaft's height, to the weight's top
        ch = aabb_of(v.named("pushchain", r"_links", pose))
        wt = aabb_of(v.named("pushweight", r"_block", pose))
        worst_chain = max(worst_chain, abs(ch[1][1] - m.Q[0]), abs(ch[0][1] - wt[1][1]))
        if prev is not None:
            pa_s, pa_q, pz, py = prev
            ds = math.remainder(a_s - pa_s, math.tau)
            dq = math.remainder(a_q - pa_q, math.tau)
            worst_rack = max(worst_rack, abs((z_bar - pz) - m.R_PP * dq), abs((y_w - py) - m.R_DRUM * dq))
            if dq > 1e-9:
                drive_w.append(W)
                worst_rate = max(worst_rate, abs(dq + ratio * ds))
                worst_contact = max(worst_contact, nearest(st, gt, m.Q, tip_g + 1.0))
            elif dq < -1e-9:
                back_w.append(W)
                grip_back = min(grip_back, nearest(st, gt, m.Q, tip_g + 1.0))
            if abs(z_bar - pz) > 1e-9:
                worst_rack_contact = max(worst_rack_contact, nearest(pt_, rk, (m.Y_RACK, m.Q[1]), 2.5))
        prev = (a_s, a_q, z_bar, y_w)
    print(f"push drive: the push shaft turns over W {drive_w[0] - dw:.3f}..{drive_w[-1]:.3f} at the sector's ratio (worst "
          f"{worst_rate:.1e}), a sector tooth within {worst_contact:.3f} of a gear tooth all the while; back over "
          f"{back_w[0] - dw:.3f}..{back_w[-1]:.3f} with the sector's teeth at least {grip_back:.3f} clear of the gear's; "
          f"sector and gear teeth into each other {hits_s} times")
    print(f"push drive: the bar and the weight go with the pinion and the drum (worst {worst_rack:.1e}), a pinion tooth within "
          f"{worst_rack_contact:.3f} of a rack tooth wherever the bar moves, their teeth into each other {hits_r} times; the chain "
          f"from the drum to the weight within {worst_chain:.1e}")
    if (worst_rate > 1e-4 or worst_contact > CONTACT or grip_back < 0.1 or hits_s or worst_rack > 1e-4 or worst_rack_contact > CONTACT
            or hits_r or worst_chain > 1e-4):
        v.fail("the push drive does not drive the bar as drawn")


# ---------------------------------------------------------------- causes
ROOTS = ("axle", "player", "game")
STILL = 1e-7                                 # a part whose matrix changes by less than this stands (the rig's rounding)
EXCEPTIONS = {"clutchfork": "game"}          # thrown by the game while a plate is on: the documented exception


def same(a, b, tol=1e-9):
    return max(abs(a[i][j] - b[i][j]) for i in range(3) for j in range(4)) <= tol


def cause_pairs(m, parts):
    """Pose pairs a step apart: the axle turning either way with no plate; the job at the drawn pace forward, finely, and
    backward, every edge of every gauge's windows among the poses (so no pair straddles one: within a pair every gauge
    moves at one rate); p easing in at the start and out at the end."""
    out = []
    for i in range(48):
        for s in (1.0, -1.0):
            th = s * (0.11 + i * math.tau / 48)
            out.append(((th, abs(th), 0.0, 0, 0.0), (th + s * 0.03, abs(th) + 0.03, 0.0, 0, 0.0)))
    edges = set()
    for q in parts:
        for d in q["drivers"]:
            for w in d.get("windows", []):
                edges |= {w["from"], w["from"] + w["ease"], w["to"] - w["ease"], w["to"]}
    edges = {e for e in edges if 0.0 < e < m.END}
    for step, back in ((0.0025, False), (0.01, True)):
        ws = sorted({round(i * step, 9) for i in range(int(round(m.END / step)) + 1)} | edges)
        poses = [m.pose_at(W, back=back) for W in ws]
        out += list(zip(poses, poses[1:]))
    for W in (0.0, m.END):
        ps = [m.pose_at(W, p=i / 10) for i in range(11)]
        out += list(zip(ps, ps[1:]))
    return out


def check_causes(v):
    """Every moving part of the model has a mechanical cause traceable to the axle or to the work's own motion through
    drawn parts (make_shape.causes()). Over the axle turning either way, the job both ways and p easing at its ends,
    every change in a part's pose is accounted for by one of its causes, held link by link: keyed parts turn with their
    shafts, gears in mesh roll their pitch circles on each other, the catches and the clutch carry their shafts, a sector
    turns its gear only with a tooth in contact (within CONTACT), the rack goes with its pinion with a tooth in contact,
    the weight and its chain go with the drum, and the weight runs the push shaft back only once the sector's teeth are
    clear of its gear. The work's own links are held by the work checks, which must pass. Every part reaches the axle
    through its causes; the only exceptions are the clutch fork (the game's) and the plate's placing (the player's)."""
    m = v.m
    links = m.causes()
    by_part = {}
    for part, cause, how, fig in links:
        by_part.setdefault(part, []).append((cause, how, fig))
    pairs = cause_pairs(m, v.parts)
    rest = {pid: v.mat(pid, m.REST) for pid in v.by_part}
    moving = sorted(pid for pid in v.by_part if pid != "frame"
                    and any(not same(v.mat(pid, b), rest[pid], STILL) for _, b in pairs[::7] + pairs[-22:]))
    missing = [pid for pid in moving if pid not in by_part]
    stale = sorted({pid for pid in by_part if pid not in moving} | {c for _, c, _, _ in links if c not in ROOTS and c not in v.by_part})
    if missing or stale:
        v.fail(f"causes: moving parts with no cause {missing}; causes of parts that do not move or are not drawn {stale}")
    # reachability: every moving part's causes lead to the axle (the fork's to the game)
    def reaches(pid, root, seen=()):
        if pid == root:
            return True
        return any(c not in seen and reaches(c, root, seen + (pid,)) for c, _, _ in by_part.get(pid, []))
    unrooted = [pid for pid in moving if pid in by_part and not reaches(pid, EXCEPTIONS.get(pid, "axle"))]
    if unrooted:
        v.fail(f"causes: {unrooted} do not lead back to the axle")

    def ang(pid, pose):
        return angle_x(v, pid, pose)

    def d_ang(pid, a, b):
        return math.remainder(ang(pid, b) - ang(pid, a), math.tau)

    def only_p(a, b):
        return a[:4] == b[:4]

    def explains(part, cause, how, fig, a, b):
        if how in ("work", "fall"):
            return v.passed.get(fig, False)
        if how in ("placed", "fork"):
            return only_p(a, b)
        if how == "keyed":
            if cause == "axle":
                return all(abs(math.remainder(ang(part, p) - p[0], math.tau)) < 1e-6 for p in (a, b))
            return same(v.mat(part, a), v.mat(cause, a), 1e-6) and same(v.mat(part, b), v.mat(cause, b), 1e-6)
        if how == "feather":
            return all(max(abs(v.mat(part, p)[i][j] - v.mat(cause, p)[i][j]) for i in range(3) for j in range(3)) < 1e-6
                       and all(abs(v.mat(part, p)[i][3] - v.mat(cause, p)[i][3]) < 1e-6 for i in (1, 2)) for p in (a, b))
        if how == "forked":
            dx = lambda pid: v.mat(pid, b)[0][3] - v.mat(pid, a)[0][3]    # noqa: E731
            return abs(dx(cause)) > 1e-9 and abs(dx(part) - dx(cause)) < 1e-9
        dc = d_ang(cause, a, b) if cause in v.by_part else 0.0
        dp = d_ang(part, a, b)
        if how == "mesh":
            return abs(dc) > 1e-9 and abs(dp * fig[0] + dc * fig[1]) < 1e-6
        if how in ("catch", "clutch"):
            return abs(dc) > 1e-9 and abs(dp - dc) < (1e-9 if how == "catch" else 1e-5)
        if how == "sector":
            if abs(dc) < 1e-9 or abs(dp * fig[0] + dc * fig[1]) > 1e-4:
                return False
            centre = [e for e in v.by_part[part] if re.search(fig[2], e.name)]
            c = aabb_of([posed(e, v.mat(part, b)) for e in centre])
            mid = ((c[0][1] + c[1][1]) / 2, (c[0][2] + c[1][2]) / 2)
            return nearest(v.named(cause, fig[3], b), v.named(part, fig[2], b), mid, fig[0] + 2.0) <= CONTACT
        if how == "crankpin":
            pin = lambda p: aabb_of(v.named(cause, r"_pin", p))    # noqa: E731
            dy_pin = (pin(b)[0][1] + pin(b)[1][1] - pin(a)[0][1] - pin(a)[1][1]) / 2
            return abs((v.mat(part, b)[1][3] - v.mat(part, a)[1][3]) * 16 - dy_pin) < 1e-4
        if how == "rack":
            dz = (v.mat(part, b)[2][3] - v.mat(part, a)[2][3]) * 16
            if abs(dz - fig[0] * dc) > 1e-5:
                return False
            return nearest(v.named(cause, fig[2], b), v.named(part, fig[1], b), (m.Y_RACK, m.Q[1]), 2.5) <= CONTACT
        if how == "chain":
            low = lambda p: aabb_of(v.posed(part, p))[0][1]    # noqa: E731
            return abs(dc) > 1e-9 and abs((low(b) - low(a)) - fig * dc) < 1e-5
        if how == "weight":
            sec_cause, sec = next(((c, f) for c, h, f in by_part[part] if h == "sector"), (None, None))
            if sec is None:                  # a weight only runs back what a sector drove and let go
                return False
            dy = (v.mat(cause, b)[1][3] - v.mat(cause, a)[1][3]) * 16
            clear = nearest(v.named(sec_cause, sec[3], b), v.named(part, sec[2], b), m.Q, sec[0] + 2.0) > 0.1
            return dp < -1e-9 and dy < 0 and abs(dy - fig * dp) < 1e-5 and clear
        raise ValueError(f"unknown cause {how!r}")

    unexplained = {}
    for a, b in pairs:
        for pid in moving:
            if pid not in by_part or same(v.mat(pid, a), v.mat(pid, b), STILL):
                continue
            if not any(explains(pid, c, h, f, a, b) for c, h, f in by_part[pid]):
                unexplained.setdefault(pid, (a, b))
    held = sorted({h for _, _, h, _ in links})
    print(f"causes: {len(moving)} moving parts, each with a cause ({len(links)} links of {len(held)} kinds), each leading back to the "
          f"axle (the clutch fork to the game); every change of pose over {len(pairs)} pose pairs accounted for by a cause: "
          f"{'yes' if not unexplained else 'NO'}; the work's links held by {sorted({f for _, _, h, f in links if h in ('work', 'fall')})}")
    for pid, (a, b) in sorted(unexplained.items()):
        print(f"  UNCAUSED {pid}: from theta {a[0]:.3f} W {a[2]:.4f} p {a[4]} to theta {b[0]:.3f} W {b[2]:.4f} p {b[4]}")
    if unexplained:
        v.fail("a part moves with no mechanical cause")


# ---------------------------------------------------------------- supports
SHAFTS = [
    # (part, element regex of its shaft, centre key, the bearings it must run in)
    ("entry", r"_rod|_oak", "E", ("fr_brgentry", "fr_stdentry")),
    ("rectshaft", r"_rod", "R", ("fr_brgrect", "fr_stdrect")),
    ("crankshaft", r"_rod", "K", ("fr_brgcrank", "fr_stdcrank")),
    ("rolltop", r"_neck", "T", ("fr_chockt_w", "fr_chockt_e")),
    ("rollbottom", r"_neck", "Bt", ("fr_chockb_w", "fr_chockb_e")),
    ("pushshaft", r"_rod", "Q", ("fr_brgpsh", "fr_pshbrkt")),
]


def check_supports(v):
    m = v.m
    frame = v.by_part["frame"]
    centres = {"E": m.E, "R": m.R, "K": m.K, "T": (m.Y_T, m.Z_N), "Bt": (m.Y_B, m.Z_N), "Q": m.Q}
    for pid, rx, ck, want in SHAFTS:
        c = centres[ck]
        found, _ = shaft_supports(frame, v.named(pid, rx), 0, c)
        missing = [w for w in want if w not in found]
        margins = []
        for w in want:
            brg = next(f for f in frame if f.name == w)
            for W in (0.0, 0.125):
                margins.append(bearing_margin(brg, v.named(pid, rx, (math.tau * W, math.tau * W, W, 1, 1.0)), 0))
        print(f"support: {pid} in {sorted(found)}; inside its bearings by {min(margins):.2f}")
        if missing or not (0 < min(margins) < 1e8):
            v.fail(f"{pid} is not carried by {want} (found {found})")
    stud = [f for f in frame if f.name.startswith("fr_stud_")]
    lo, hi = aabb_of(v.named("idler", r"_body"))
    slo, shi = aabb_of(stud)
    if not (slo[0] < hi[0] and shi[0] >= hi[0] and lo[1] < m.IDL[0] < hi[1] and lo[2] < m.IDL[1] < hi[2]):
        v.fail("the idler is not on its stud")
    for s in ("w", "e"):
        scr = next(f for f in frame if f.name == f"fr_screw_{s}").aabb()
        ch = next(f for f in frame if f.name == f"fr_chockt_{s}").aabb()
        cap = next(f for f in frame if f.name == f"fr_hcap_{s}").aabb()
        if abs(scr[0][1] - ch[1][1]) > 1e-6 or not (scr[1][1] > cap[1][1]):
            v.fail(f"the {s} screw does not bear on its top chock through the cap")
    rod = aabb_of(v.named("clutchfork", r"_rod", m.pose_at(1.0)))
    guide = next(f for f in frame if f.name == "fr_forkguide").aabb()
    if not (rod[0][0] < guide[0][0] and rod[1][0] > guide[1][0] and abs(rod[0][1] - guide[1][1]) < 1e-6):
        v.fail("the fork's rod does not run through its guide on the standard")
    print("support: the idler on its stud; the loose wheels and the clutch gear on their shafts; each screw through its cap "
          "onto the top chock; the fork's rod through its guide on the standard")


# ---------------------------------------------------------------- clearances
ALLOWED = [
    # shafts in their bearings, parts keyed, feathered or loose on their shafts
    ("entry", None, "frame", r"fr_brgentry|fr_stdentry"), ("rectshaft", r"_rod", "frame", r"fr_brgrect|fr_stdrect"),
    ("crankshaft", r"_rod", "frame", r"fr_brgcrank|fr_stdcrank"),
    ("roll(top|bottom)", r"_neck", "frame", r"fr_chock"), ("idler", None, "frame", r"fr_stud"),
    ("entrygears", None, "entry", r"_rod"), ("rectw[12]", None, "rectshaft", r"_rod|_hub|_spacer"),
    ("clutchgear|cone|crank", None, "crankshaft", r"_rod|_hub"),
    ("pinion(top|bottom)", None, "roll(top|bottom)", r"_neck"), ("wheel", None, "rolltop", r"_neck"),
    # gears in mesh (check_gearing and check_feed_train hold their teeth)
    ("entrygears", r"_a1", "rectw1", r"_gear"), ("entrygears", r"_a2", "idler", None), ("idler", None, "rectw2", r"_gear"),
    ("rectshaft", r"_gear", "clutchgear", r"clutchgear_(body|tooth)"), ("crankshaft", r"_tooth|_disc", "wheel", r"_tooth|_rim"),
    ("piniontop", r"_gear", "pinionbottom", r"_gear"),
    # the cone in its cup, the fork's rod in its guide, the crank pin in the yoke's slot
    ("cone", r"_body", "clutchgear", r"_cup"), ("clutchfork", r"_rod", "frame", r"fr_forkguide"), ("crank", r"_pin", "knife", r"_yoke"),
    # the work: the plate on the table and in the nip, the rods in the slitters and on the bed knife, the nails against the
    # rods' ends and the bed knife, in the box and on each other (none of it touches the knife: check_parting)
    ("plate", None, "frame", r"fr_table"), ("plate|rods[lu]", None, "roll(top|bottom)", r"_cutter|_spacer"),
    ("plate", None, "rods[lu]|nail\\d[lu]", None), ("rods[lu]|nail\\d[lu]", None, "bedknife", None), ("rods[lu]", None, "nail\\d[lu]", None),
    ("rods[lu]", None, "frame", r"fr_box"),
    # the push bar: its fingers in the slitters' channels and the table's grooves, against the plate's and the rods' tails
    ("pusher", r"_finger", "roll(top|bottom)", r"_cutter|_spacer"), ("pusher", r"_finger", "plate|rods[lu]", None),
    ("pusher", None, "frame", r"fr_table|fr_guide"),
    # the push drive: the sector keyed on the top arbor's neck, the push shaft in its bearings, the sector's runs in mesh with the
    # push gear and the pinion with the rack (check_push_drive holds their teeth), the chain off the drum and on the weight
    ("pushsector", r"_hub|_plate", "rolltop", r"_neck"), ("pushshaft", r"_rod", "frame", r"fr_brgpsh|fr_pshbrkt"),
    ("pushsector", r"_tooth|_plate", "pushshaft", r"_gear_"), ("pushshaft", r"_pinion_", "pusher", r"_rack"),
    ("pushchain", None, "pushshaft", r"_drum"), ("pushchain", None, "pushweight", None),
    ("nail\\d[lu]", None, "frame", r"fr_box"), ("nail\\d[lu]", None, "nail\\d[lu]", None),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose):
    m = v.m
    items = []
    for pid in v.by_part:
        if not present(m, pid, pose):
            continue
        for el in v.posed(pid, pose):
            if m.is_work(pid) and not visible(m, el):
                continue
            items.append((pid, el, el.aabb()))
    grid = {}
    for i, (_pid, _el, (lo, hi)) in enumerate(items):
        for cx in range(int(lo[0] // 4), int(hi[0] // 4) + 1):
            for cy in range(int(lo[1] // 4), int(hi[1] // 4) + 1):
                for cz in range(int(lo[2] // 4), int(hi[2] // 4) + 1):
                    grid.setdefault((cx, cy, cz), []).append(i)
    hits = {}
    seen = set()
    for idx in grid.values():
        for a in range(len(idx)):
            for b in range(a + 1, len(idx)):
                i, j = idx[a], idx[b]
                if (i, j) in seen:
                    continue
                seen.add((i, j))
                pa, ea, (alo, ahi) = items[i]
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
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {p[0]:.2f} W {p[2]:.3f} p {p[4]}" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = [e for pid in v.by_part if present(m, pid, pose) for e in v.posed(pid, pose)]
        pairs = coplanar_faces([e for e in m.shown(els, pose) if e.c[1] > -500])
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses")
    if bad:
        v.fail("faces z-fight")


def cut_poses(m, n=12):
    """Poses through every nail's pressing and fall, finely."""
    out = []
    for j in (1, 2):
        for lay in ("l", "u"):
            marks = m.cut_marks(lay, j)
            w0, w1 = marks[0] - 0.01, marks[-1] + m.FALL
            out += [m.pose_at(round(w0 + (w1 - w0) * i / n, 6)) for i in range(n + 1)]
    return out


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    for check in (check_basic, check_floating, check_textures, check_anchors):
        v.run(check)
    v.run(check_containment, idle(m) + cycle(m, 0.1) + cycle(m, 0.1, back=True) + [m.pose_at(m.END, p=0.0)])
    for check in (check_gearing, check_feed_train, check_feed, check_slitters, check_work, check_dwell, check_parting,
                  check_pusher, check_push_drive, check_removal, check_knife, check_supports, check_causes):
        v.run(check)
    v.run(check_clearances, idle(m) + cycle(m, 0.05) + cycle(m, 0.1, back=True) + cut_poses(m) + [m.pose_at(1.0, p=0.0)], "clearances")
    if not quick:
        v.run(check_clearances, cycle(m, 0.01) + cycle(m, 0.02, back=True) + idle(m, 36) + cut_poses(m, 40),
              "swept paths (every 0.01 stroke, both ways, and the cuts finely)")
        v.run(check_zfight)
    return v.ok


def validate_files(m, shape, frame_shape, ship):
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL textures used but not declared: {missing}")
        ok = False
    gaps = lid_gaps(ship["cells"])
    hollow = [c["pos"] for c in ship["cells"] if c.get("hollow")]
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, hollow {hollow}; "
          f"lids over every column: {'yes' if not gaps else gaps}")
    if gaps or hollow:
        ok = False
    if ship.get("powerCell") != list(m.POWER_CELL) or ship.get("powerFace") != m.POWER_FACE:
        print("FAIL the power cell or face is not the entry shaft's")
        ok = False
    return ok
