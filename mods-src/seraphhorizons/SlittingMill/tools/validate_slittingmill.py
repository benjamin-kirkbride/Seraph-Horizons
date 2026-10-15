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
    """The work is drawn only with a plate on; a sliver (a piece not yet fed) or a piece taken off counts as gone."""
    return not m.is_work(pid) or pose[3] != 0


def visible(m, el):
    return not el.vanished and min(abs(s) for s in el.size) >= 0.05


def cycle(m, step, back=False):
    n = int(round(m.HAND_OUT / step))
    return [m.pose_at(round(i * step, 6), back=back) for i in range(n + 1)]


def idle(m, n=8):
    """No plate: the axle at n angles round a turn, both ways."""
    return [(s * math.tau * i / n, math.tau * i / n, 0.0, 0, 0.0) for i in range(n) for s in (1.0, -1.0)]


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
    (r"^fr_studboss", "iron"),
    (r"^(fr_(sill|bearer|tpost|table|box)|entry_oak)", "oak"),
    (r"^(fr_(hbase|hpost|hcheek|hcap|screwhead|capstan|ped|brg|std|guide|studboss)|entry_rod|crank_disc|knife_(crosshead|ram|yoke)"
     r"|wheel_|pinion(top|bottom)_|rectw[12]_(gear|hub)|idler_|roll(top|bottom)_collar)", "iron"),
    (r"^(fr_(screw_|stud_)|roll(top|bottom)_(cutter|spacer|neck)|knife_blade|bedknife_|crank_(pin|washer)|entrygears_|rectshaft_"
     r"|rectw[12]_catch)", "steel"),
    (r"^fr_chock", "brass"),
    (r"^(plate|rods|nail[12][lu])_", "metal"),
]


def check_textures(v):
    bad = []
    for el in v.els:
        got = {f["texture"].lstrip("#") for f in el.faces.values()}
        rule = next((tex for rx, tex in TEX_RULES if re.match(rx, el.name)), None)
        if rule is None or not got <= {rule}:
            bad.append((el.name, sorted(got), rule))
    print(f"textures by role (oak timber, the box and the axle's continuation; iron castings, the knife's crosshead, ram and yoke, "
          f"the wheels; steel collars, arbors, blades, pins and pinions; brass chocks; the work 'metal'): {len(bad)} elements break them")
    if bad:
        v.fail(f"textures by role: {bad[:5]}")


# ---------------------------------------------------------------- anchors
def check_anchors(v):
    m, rig = v.m, v.rig
    pc = tuple(rig["powerCell"])
    entry = v.named("entry", r"_oak")
    _, hi = aabb_of(entry)
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
def angle_x(v, pid, pose):
    mm = v.mat(pid, pose)
    return math.atan2(mm[2][1], mm[1][1])


def mesh_pairs(m):
    """(gear a's part, element regex, centre, pitch radius, teeth; the same for b; what to sample: theta or psi)."""
    t, b = (m.Y_T, m.Z_N), (m.Y_B, m.Z_N)
    rp = m.RECT_MOD / 2
    return [
        (("entrygears", r"_a1_tooth", m.E, m.A1_T * rp, m.A1_T), ("rectw1", r"_gear_tooth", m.R, m.W1_T * rp, m.W1_T)),
        (("entrygears", r"_a2_tooth", m.E, m.A2_T * rp, m.A2_T), ("idler", r"_gear_tooth", m.IDL, m.I_T * rp, m.I_T)),
        (("idler", r"_gear_tooth", m.IDL, m.I_T * rp, m.I_T), ("rectw2", r"_gear_tooth", m.R, m.W2_T * rp, m.W2_T)),
        (("rectshaft", r"_p2_tooth", m.R, m.pr(m.P2_T), m.P2_T), ("wheel", r"_tooth", t, m.pr(m.G_T), m.G_T)),
        (("piniontop", r"_tooth", t, m.R_PIN, m.PIN_T), ("pinionbottom", r"_tooth", b, m.R_PIN, m.PIN_T)),
    ]


def check_gearing(v):
    """Every meshing pair: centres the sum of the pitch radii apart, the arcs rolled on the pitch circles equal and
    opposite (finite differences of the posed rig, the axle either way), and no tooth running into the other's over
    the angles of a pitch. The rectifier: whichever way the axle turns, exactly one loose wheel turns with the
    rectified shaft, and that the shaft's way."""
    m = v.m
    worst = 0.0
    for (pa, ra, ca, rpa, na), (pb, rb, cb, rpb, nb) in mesh_pairs(m):
        d = math.hypot(ca[0] - cb[0], ca[1] - cb[1])
        if abs(d - (rpa + rpb)) > 1e-6:
            v.fail(f"{pa} and {pb}: centres {d:.4f} apart, pitch radii add to {rpa + rpb:.4f}")
        for th in (0.3, -0.3, 2.0, -5.1):
            s1 = (th, abs(th), 0.0, 0, 0.0)
            s2 = (th + 0.05 * (1 if th > 0 else -1), abs(th) + 0.05, 0.0, 0, 0.0)
            arc_a = math.remainder(angle_x(v, pa, s2) - angle_x(v, pa, s1), math.tau) * rpa
            arc_b = math.remainder(angle_x(v, pb, s2) - angle_x(v, pb, s1), math.tau) * rpb
            worst = max(worst, abs(arc_a + arc_b))
            if abs(arc_a + arc_b) > 1e-6:
                v.fail(f"{pa} and {pb} slip at theta {th}: {arc_a:.6f} against {arc_b:.6f}")
        # the teeth: sampled over the pinion's pitch, either way
        hits = 0
        for i in range(24):
            for sgn in (1.0, -1.0):
                th = sgn * (0.37 + i * math.tau / max(na, nb) / 6)
                pose = (th, abs(th), 0.0, 0, 0.0)
                ta, tb = v.named(pa, ra, pose), v.named(pb, rb, pose)
                hits += sum(1 for x in ta for y in tb if obb_obb(x, y, eps=0.1))
        if hits:
            v.fail(f"{pa} and {pb}: teeth run into each other ({hits} times over 48 angles)")
    print(f"gearing: {len(mesh_pairs(m))} meshes (A1-W1, A2-idler-W2, P2-great wheel, the pinions), centres at the pitch radii, "
          f"worst roll mismatch {worst:.1e}, no teeth running into each other over 48 angles")
    for sgn in (1.0, -1.0):
        s1, s2 = (sgn * 1.0, 1.0, 0.0, 0, 0.0), (sgn * 1.05, 1.05, 0.0, 0, 0.0)
        shaft = math.remainder(angle_x(v, "rectshaft", s2) - angle_x(v, "rectshaft", s1), math.tau)
        with_it = [w for w in ("rectw1", "rectw2")
                   if abs(math.remainder(angle_x(v, w, s2) - angle_x(v, w, s1), math.tau) - shaft) < 1e-9]
        if shaft <= 0 or len(with_it) != 1:
            v.fail(f"the rectifier: with the axle turning {'+' if sgn > 0 else '-'}, the shaft turns {shaft:.4f} and {with_it} with it")
    print("rectifier: whichever way the axle turns, one loose wheel carries the rectified shaft forward")


def check_feed(v):
    """The rolls feed the plate: the cutters' rims move south at the nip at the plate's pace (a nail's length a turn
    of the axle), the bottom arbor's top and the top arbor's bottom both, whichever way the axle turns."""
    m = v.m
    for sgn in (1.0, -1.0):
        s1, s2 = (0.0, 0.0, 0.0, 0, 0.0), (sgn * 0.01, 0.01, 0.0, 0, 0.0)
        for pid, y, sign in (("rollbottom", m.Y_B + m.R_CUT, 1), ("rolltop", m.Y_T - m.R_CUT, -1)):
            a = v.point(pid, s1, [16.0, y, m.Z_N])
            b = v.point(pid, s2, [16.0, y, m.Z_N])
            per_turn = (b[2] - a[2]) / 0.01 * math.tau
            if abs(per_turn - m.L_NAIL) > 1e-3:
                v.fail(f"the {pid}'s rim moves {per_turn:.4f} a turn at the nip (axle {sgn:+}), the plate {m.L_NAIL}")
    print(f"feed: the cutters' rims carry the plate south {m.L_NAIL} a turn of the axle, either way (arbors {m.ROLL_RATIO:.5f} of the "
          f"axle, rim radius {m.R_CUT:.4f}, overlap {m.DELTA:.3f})")


def check_slitters(v):
    """The collars: cutters of one arbor between the other's, the even rods in the lower layer under the top cutters,
    the odd in the upper over the bottom ones, each rod between its cutter's rim and the other arbor's spacer."""
    m = v.m
    sp_vertex = m.R_SPACER / math.cos(math.pi / (2 * m.SPACER_K))
    low_clear = (m.LOW_TOP - 1.0) - (m.Y_B + sp_vertex)
    up_clear = (m.Y_T - sp_vertex) - (m.UP_BOT + 1.0)
    print(f"slitters: overlap {m.DELTA:.3f}; the lower layer's top on the top cutters' rims ({m.LOW_TOP:.4f}), {low_clear:.3f} over "
          f"the bottom spacers; the upper layer's bottom on the bottom cutters' ({m.UP_BOT:.4f}), {up_clear:.3f} under the top spacers")
    if m.DELTA < 0.3 or low_clear < 0.05 or up_clear < 0.05:
        v.fail("the slitters do not cut, or a rod runs on a spacer")
    for i in range(m.RODS):
        p = i + 1
        if m.top_cutter(p) != (i % 2 == 0):
            v.fail(f"rod {i} is not under the cutter of its layer")


# ---------------------------------------------------------------- the work
def span(v, pid, pose):
    els = [e for e in v.posed(pid, pose) if not e.vanished]
    return aabb_of(els) if els else None


def check_work(v):
    """The plate goes into the nip a nail's length a stroke and is gone at 4; the rods grow out of the nip to the
    knife's face by 2 and stay; each nail grows past the knife's face from a stroke before its cut, is a nail long at
    the cut (the knife at the bottom), falls into the box and lies there; the strips (nip to knife) and the nails go at
    the hand-out. Nothing of the work shows before the plate is on (p 0)."""
    m = v.m
    tol = 1e-6
    for back in (False, True):
        for W in [round(0.05 * i, 4) for i in range(int(m.HAND_OUT / 0.05) + 1)]:
            pose = m.pose_at(W, back=back)
            pl = span(v, "plate", pose)
            want = m.PLATE_L * max(0.0, 1 - W / m.STROKES_FEED)
            got = 0.0 if pl is None else pl[1][2] - pl[0][2]
            if abs(got - want) > tol or (pl and abs(pl[1][2] - m.Z_N) > tol):
                v.fail(f"the plate at W {W}: {got:.4f} long, want {want:.4f}, front at the nip")
            rods = span(v, "rods", pose)
            want = min(m.D_KNIFE, m.L_NAIL * W) if W < m.HAND_OUT else 0.0
            got = 0.0 if rods is None else rods[1][2] - m.Z_N
            if W < m.HAND_OUT - m.END_EASE and abs(max(got, m.EPS_ROD) - max(want, m.EPS_ROD)) > m.EPS_ROD + 1e-6:
                v.fail(f"the rods at W {W}: front {got:.4f} past the nip, want {want:.4f}")
            for j, w_cut in enumerate(m.CUTS, 1):
                for lay in ("l", "u"):
                    n = span(v, f"nail{j}{lay}", pose)
                    if n is None:
                        continue
                    length = n[1][2] - n[0][2]
                    if w_cut - 1 < W <= w_cut and W < m.HAND_OUT - m.END_EASE:
                        if abs(n[0][2] - m.Z_K) > 1e-4 or abs(length - m.L_NAIL * (W - (w_cut - 1))) > m.EPS_NAIL + 1e-6:
                            v.fail(f"nail {j}{lay} at W {W}: z {n[0][2]:.4f}..{n[1][2]:.4f}, want from the knife's face, "
                                   f"{m.L_NAIL * (W - (w_cut - 1)):.3f} long")
                    if W >= w_cut + m.FALL and W < m.HAND_OUT - m.END_EASE:
                        floor = m.BOX_FLOOR + (j - 1) * 1.0 + m.NAIL_IN
                        if abs(n[0][1] - floor) > 1e-4 or abs(length - m.L_NAIL) > 1e-4:
                            v.fail(f"nail {j}{lay} at W {W} does not lie in the box (bottom {n[0][1]:.4f}, want {floor:.4f})")
                    if W <= w_cut - 1 - 0.002 and length > 0.05:
                        v.fail(f"nail {j}{lay} shows at W {W} before the rods reach the knife")
        end = m.pose_at(m.HAND_OUT, back=back)
        if any(not e.vanished for pid in v.by_part if m.is_work(pid) and pid != "plate" for e in v.posed(pid, end)):
            v.fail("the strips or the nails are still there at the hand-out")
    # nothing of the work at p 0, a plate on at W 0
    for pid in v.by_part:
        if m.is_work(pid) and pid != "plate":
            for pose in (m.pose_at(0.0, p=0.0), m.pose_at(0.0, p=0.5), m.pose_at(m.HAND_OUT, p=0.0)):
                if any(visible(m, e) for e in v.posed(pid, pose)):
                    v.fail(f"{pid} shows at {pose}")
    print(f"work: the plate into the nip {m.L_NAIL} a stroke, gone at {m.STROKES_FEED:g}; the rods out of it to the knife's face by "
          f"{m.D_KNIFE / m.L_NAIL:g}; two nails a rod, parted at W {m.CUTS} and lying in the box; strips and nails taken off at "
          f"{m.HAND_OUT}; no sliver shows before the plate goes on; both ways round")


# ---------------------------------------------------------------- the knife
def check_knife(v):
    """The crank pin stays in the yoke's slot; the knife's edge comes down past the bed knife and under the lower layer
    at every whole turn (so at every cut) and stands clear over the upper layer at the half turn; its crosshead never
    comes down to the rods; its ends stay in the housings' slots; the blade passes behind the bed knife's back face."""
    m = v.m
    worst_y, worst_z = 0.0, 1e9
    for i in range(72):
        for sgn in (1.0, -1.0):
            th = sgn * math.tau * i / 72
            pose = (th, abs(th), 0.0, 0, 0.0)
            pin = aabb_of(v.named("crank", r"_pin", pose))
            pc = [(pin[0][k] + pin[1][k]) / 2 for k in range(3)]
            slot_c = v.point("knife", pose, [16.0, m.E[0], m.E[1]])
            worst_y = max(worst_y, abs(pc[1] - slot_c[1]))
            zl, zh = m.E[1] - m.R_CRANK - m.PIN_R - 0.15, m.E[1] + m.R_CRANK + m.PIN_R + 0.15
            worst_z = min(worst_z, pc[2] - (pin[1][2] - pin[0][2]) / 2 - zl, zh - pc[2] - (pin[1][2] - pin[0][2]) / 2)
    print(f"knife: the crank pin's centre within {worst_y:.2e} of the yoke's slot's middle, its ends {worst_z:.3f} inside the slot")
    if worst_y > 1e-6 or worst_z < 0.02:
        v.fail("the crank pin leaves the yoke's slot")
    edge = lambda pose: aabb_of(v.named("knife", r"_blade", pose))[0][1]    # noqa: E731
    head = lambda pose: aabb_of(v.named("knife", r"_crosshead", pose))[0][1]    # noqa: E731
    down, up = edge((0.0, 0.0, 0.0, 0, 0.0)), edge((math.pi, math.pi, 0.0, 0, 0.0))
    low_bot, up_top = m.LOW_TOP - 1.0, m.UP_BOT + 1.0
    lowest_head = min(head((math.tau * i / 72, 0.0, 0.0, 0, 0.0)) for i in range(72))
    print(f"knife: edge {down:.3f} at the bottom ({low_bot - down:.3f} under the lower layer, the bed knife's top {m.BED_Y[1]:.3f}), "
          f"{up:.3f} at the top ({up - up_top:.3f} over the upper layer); the crosshead no lower than {lowest_head:.3f} "
          f"({lowest_head - up_top:.3f} over the upper layer)")
    if down > low_bot - 0.2 or up < up_top + 0.5 or lowest_head < up_top + 0.3:
        v.fail("the knife does not cut through both layers, or does not clear them, or its crosshead comes down to them")
    for W in m.CUTS:
        for back in (False, True):
            if abs(edge(m.pose_at(W, back=back)) - down) > 1e-9:
                v.fail(f"the knife is not at the bottom at the cut, W {W}")
    blade = v.named("knife", r"_blade")[0].aabb()
    bed = v.named("bedknife", r"_bar")[0].aabb()
    if blade[0][2] - bed[1][2] < 0.02:
        v.fail("the blade rubs the bed knife's back face")
    frame = {e.name: e.aabb() for e in v.by_part["frame"]}
    for i in range(36):
        pose = (math.tau * i / 36, 0.0, 0.0, 0, 0.0)
        xh = aabb_of(v.named("knife", r"_crosshead", pose))
        for s, x_in in (("w", xh[0][0]), ("e", xh[1][0])):
            ch_f, ch_b = frame[f"fr_hcheekf_{s}"], frame[f"fr_hcheekb_{s}"]
            in_slot = ch_f[0][0] - 1e-9 <= x_in <= ch_f[1][0] + 1e-9 and ch_f[1][2] < xh[0][2] and xh[1][2] < ch_b[0][2] \
                and ch_f[0][1] < xh[0][1] and xh[1][1] < ch_f[1][1]
            if not in_slot:
                v.fail(f"the knife's crosshead leaves its {s} slot at theta {pose[0]:.2f}")
                return
    print(f"knife: the blade {blade[0][2] - bed[1][2]:.3f} behind the bed knife's back face; the crosshead's ends in the housings' "
          f"slots all round; at the bottom at both cuts, either way round")


# ---------------------------------------------------------------- supports
SHAFTS = [
    # (part, element regex of its shaft, centre (y, z), the bearings it must run in)
    ("entry", r"_rod|_oak", "E", ("fr_brgentry", "fr_stdentry")),
    ("rectshaft", r"_rod", "R", ("fr_brgrect", "fr_stdrect")),
    ("rolltop", r"_neck", "T", ("fr_chockt_w", "fr_chockt_e")),
    ("rollbottom", r"_neck", "Bt", ("fr_chockb_w", "fr_chockb_e")),
]


def check_supports(v):
    m = v.m
    frame = v.by_part["frame"]
    centres = {"E": m.E, "R": m.R, "T": (m.Y_T, m.Z_N), "Bt": (m.Y_B, m.Z_N)}
    for pid, rx, ck, want in SHAFTS:
        c = centres[ck]
        found, _ = shaft_supports(frame, v.named(pid, rx), 0, c)
        missing = [w for w in want if w not in found]
        margins = []
        for w in want:
            brg = next(f for f in frame if f.name == w)
            for th in (0.0, math.pi / 4):
                margins.append(bearing_margin(brg, v.named(pid, rx, (th, th, 0.0, 0, 0.0)), 0))
        print(f"support: {pid} in {sorted(found)}; inside its bearings by {min(margins):.2f}")
        if missing or not (0 < min(margins) < 1e8):
            v.fail(f"{pid} is not carried by {want} (found {found})")
    stud = [f for f in frame if f.name.startswith("fr_stud_")]
    idl = v.named("idler", r"_body")
    lo, hi = aabb_of(idl)
    slo, shi = aabb_of(stud)
    if not (slo[0] < hi[0] and shi[0] >= hi[0] and lo[1] < m.IDL[0] < hi[1] and lo[2] < m.IDL[1] < hi[2] and slo[0] < lo[0] + 0.5):
        v.fail("the idler is not on its stud")
    # the loose wheels located: a hub or the spacer on each face
    print("support: the idler on its stud from the standard; the loose wheels between the pinion's hub, the spacer and the standard")
    for s in ("w", "e"):
        scr = next(f for f in frame if f.name == f"fr_screw_{s}").aabb()
        ch = next(f for f in frame if f.name == f"fr_chockt_{s}").aabb()
        cap = next(f for f in frame if f.name == f"fr_hcap_{s}").aabb()
        if abs(scr[0][1] - ch[1][1]) > 1e-6 or not (scr[1][1] > cap[1][1]):
            v.fail(f"the {s} screw does not bear on its top chock through the cap")
    print("support: each screw through its cap onto the top chock")


# ---------------------------------------------------------------- clearances
ALLOWED = [
    # shafts in their bearings, keyed parts on their shafts
    ("entry", None, "frame", r"fr_brgentry|fr_stdentry"), ("rectshaft", r"_rod", "frame", r"fr_brgrect|fr_stdrect"),
    ("roll(top|bottom)", r"_neck", "frame", r"fr_chock"), ("idler", None, "frame", r"fr_stud"),
    ("entrygears|crank", None, "entry", r"_rod"), ("rectw[12]", None, "rectshaft", r"_rod|_hubp|_spacer"),
    ("pinion(top|bottom)", None, "roll(top|bottom)", r"_neck"), ("wheel", None, "rolltop", r"_neck"),
    # gears in mesh (check_gearing holds their teeth)
    ("entrygears", r"_a1", "rectw1", r"_gear"), ("entrygears", r"_a2", "idler", None), ("idler", None, "rectw2", r"_gear"),
    ("rectshaft", r"_p2", "wheel", r"_tooth|_rim"), ("piniontop", r"_gear", "pinionbottom", r"_gear"),
    # the crank pin in the yoke's slot
    ("crank", r"_pin", "knife", r"_yoke"),
    # the work: the plate on the table and in the nip, the rods in the slitters and on the bed knife, the nails parted by the
    # knife, against the bed knife's back, lying in the box and on each other
    ("plate", None, "frame", r"fr_table"), ("plate|rods", None, "roll(top|bottom)", r"_cutter|_spacer"),
    ("plate", None, "rods|nail\\d[lu]", None), ("rods|nail\\d[lu]", None, "bedknife", None), ("rods", None, "nail\\d[lu]", None),
    ("nail\\d[lu]", None, "knife", r"_blade"), ("nail\\d[lu]", None, "frame", r"fr_box"), ("nail\\d[lu]", None, "nail\\d[lu]", None),
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
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {p[0]:.2f} W {p[2]:.3f}" for (a, b), p in ex))
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


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_floating(v)
    check_textures(v)
    check_anchors(v)
    check_containment(v, idle(m) + cycle(m, 0.1) + cycle(m, 0.1, back=True))
    check_gearing(v)
    check_feed(v)
    check_slitters(v)
    check_work(v)
    check_knife(v)
    check_supports(v)
    check_clearances(v, idle(m) + cycle(m, 0.05) + cycle(m, 0.1, back=True), "clearances")
    if not quick:
        check_clearances(v, cycle(m, 0.01) + cycle(m, 0.02, back=True) + idle(m, 36), "swept paths (every 0.01 stroke, both ways)")
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
    hollow = [c["pos"] for c in ship["cells"] if c.get("hollow")]
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, hollow {hollow}; "
          f"lids over every column: {'yes' if not gaps else gaps}")
    if gaps or hollow:
        ok = False
    if ship.get("powerCell") != list(m.POWER_CELL) or ship.get("powerFace") != m.POWER_FACE:
        print("FAIL the power cell or face is not the entry shaft's")
        ok = False
    return ok
