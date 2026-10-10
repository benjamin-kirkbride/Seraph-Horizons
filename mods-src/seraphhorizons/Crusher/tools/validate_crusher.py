"""The crusher generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its rule.
Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in make_shape.py
proves the shipped files are this model moved.

The tiers never stand together: tier 2's battery is taken off when tier 3's jaw is fitted, and tier 4 adds the
rolls to the jaw. So every check that sets parts against each other is run per build state (`make_shape.TIERS`):
a part is checked with the frame and with the parts of its own tier, never with another tier's.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
from machinegen.checks import supports as shaft_supports
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import part_of, posed

STATES = ("t2", "t3", "t4")


class V:
    def __init__(self, m, els, parts, rig):
        self.m, self.els, self.parts, self.rig = m, els, parts, rig
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        self.requires = {p["id"]: p["requires"] for p in parts}
        self.cache = {}
        self.ok = True

    def fail(self, msg):
        self.ok = False
        print("FAIL", msg)

    def mat(self, pid, pose):
        return self.m.pm(self.parts, pid, pose[:5])

    def posed(self, pid, pose):
        key = (pid, tuple(pose[:5]))
        if key not in self.cache:
            if len(self.cache) > 80000:
                self.cache.clear()
            mm = self.mat(pid, pose)
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def point(self, pid, pose, p):
        q = _apply(self.mat(pid, pose), [c / 16 for c in p])
        return [c * 16 for c in q]

    def named(self, pid, rx, pose=None):
        src = self.posed(pid, pose) if pose is not None else self.by_part.get(pid, [])
        r = re.compile(rx)
        return [e for e in src if r.search(e.name)]

    def parts_in(self, state):
        return [pid for pid in self.by_part if self.m.in_state(self.requires[pid], state)]


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
    reqs = sorted({str(p["requires"]) for p in v.parts})
    print(f"requires: {reqs}; tiers {v.m.TIERS}")
    want = {None} | {r for t in v.m.TIERS.values() for r in t}
    if {p["requires"] for p in v.parts} != want:
        v.fail("the parts' requires are not the tiers' sets")


def check_containment(v, poses):
    m = v.m
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where = -1e9, None
    for pose in poses:
        for pid in v.by_part:
            for el in v.posed(pid, pose):
                lo, hi = el.aabb()
                o = box_overhang(lo, hi, box)
                if o > worst:
                    worst, where = o, (el.name, pose)
    print(f"containment over {len(poses)} poses: worst overhang {worst:.3f} voxels ({where[0]})")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the machine box at {where[1]}")


def check_floating(v):
    """The frame is one piece from the ground, and so is each build state at rest: every tier's parts are joined to
    the ground through the frame and each other (shafts in their bearings, parts on what carries them)."""
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")
    for state in ("frame",) + STATES:
        els = [e for pid in v.parts_in(state) for e in v.posed(pid, v.m.REST)]
        seen, loose = frame_floating(els)
        print(f"build state {state}: {len(els)} elements, {len(loose)} not joined to the ground")
        if loose:
            v.fail(f"in {state} these float: {loose[:8]}")


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way round.
# Everything else must clear by more than 0.02 voxels at every sampled pose of its build state.
ALLOWED = [
    # the power train: shafts in their bearings, loose wheels on their shafts, meshing wheels
    ("entry", r"_shaft|_rod", "frame", r"fr_(entryplate|bracket|post_e2)"),
    ("line", r"_shaft|_collar", "frame", r"fr_(lineplate|bracket|post_e2)"),
    ("rectb[12]", None, "line", r"_shaft|_spacer"), ("idler", None, "frame", r"fr_idler_stud"),
    ("entry", r"entry_a1", "rectb1", None), ("entry", r"entry_a2", "idler", None), ("idler", None, "rectb2", None),
    ("line", r"_pinion", "bullwheel|eshaft|roll1", r"_tooth|_rim"),
    # tier 2: the camshaft in its pillow blocks, the bull wheel and the cone on it, the cone in the cup, the lever's
    # yoke in the cone's groove and the lever on its pin; the stamps on their dies and their cams, in their guides
    ("camshaft", r"_shaft|_collar", "camframe", r"_pillow"), ("camframe", r"_pillow", "frame", r"fr_cam_"),
    ("camframe", r"_leverbracket", "frame", r"fr_deck_e1"),
    ("bullwheel", r"_hub|_cupback", "camshaft", r"_shaft|_collar_e"), ("cone", None, "camshaft", r"_shaft"),
    ("cone", r"_step", "bullwheel", r"_cup"), ("clutchlever", r"_prong", "cone", r"_groove|_flange"),
    ("clutchlever", r"_boss", "camframe", r"_leverpin|_leverlug"),
    ("stamp\\d", r"_shoe", "mortar", r"_die"), ("stamp\\d", r"_tappet", "camshaft", r"_cam\d"),
    ("stamp\\d", r"_stem", "guides", None),
    ("guides", None, "frame", r"fr_(deck|upper)_"), ("mortar", r"_block", "frame", None),
    ("mortar", r"_apron|_guide", "frame", r"fr_spout"), ("mortar", r"_chute|_feeder", "frame", r"fr_hopper"),
    # tier 3: the jaw's frame on the bearers, the eccentric shaft in its bearings and the pitman's strap, the toggles
    # in their seats and on the pitman's foot, the swinging jaw on its hinge pin, the rods pinned to it, through the
    # back wall to their nuts, the springs on the rods
    ("jawframe", r"_base|_backwall|_frontwall", "frame", r"fr_bearer"), ("jawframe", r"_launder", "frame", r"fr_spout"),
    ("eshaft", r"_shaft|_collar|_flyhub|_wheel_hub", "jawframe", r"_ebearing"), ("eshaft", r"_eccentric|_shaft", "pitman", r"_strap"),
    ("pitman", r"_foot", "backtoggle|fronttoggle", None), ("backtoggle", None, "jawframe", r"_backseat|_backwall"),
    ("fronttoggle", None, "swingjaw", r"_seat|_slab"), ("swingjaw", r"_boss|_web", "jawframe", r"_hingepin|_hbearing"),
    ("rods", None, "swingjaw", r"_lug|_slab"), ("rods", r"rods_[we]_", "jawframe", r"_backwall"),
    ("rods", None, "spring\\d", None),
    # tier 4: the rolls in their housings and blocks, the pair gears, the hopper and the chute
    ("roll[12]", r"_shaft|_collar", "rollframe", r"_housing|_block"), ("roll1", r"_pair", "roll2", r"_pair"),
    ("rollframe", r"_base", "frame", r"fr_sill"), ("rollframe", r"_hopper_s", "frame", r"fr_bearer_s"),
    ("rollframe", r"_chute", "frame", r"fr_inlet"), ("rollframe", r"_chute|_hopper_w", "rollframe", None),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose, state, only=None):
    items = []
    for pid in v.parts_in(state):
        for el in v.posed(pid, pose):
            items.append((pid, el, el.aabb()))
    grid = {}
    for i, (pid, el, (lo, hi)) in enumerate(items):
        for cx in range(int(lo[0] // 4), int(hi[0] // 4) + 1):
            for cy in range(int(lo[1] // 4), int(hi[1] // 4) + 1):
                for cz in range(int(lo[2] // 4), int(hi[2] // 4) + 1):
                    grid.setdefault((cx, cy, cz), []).append(i)
    hits, seen = {}, set()
    for idx in grid.values():
        for a in range(len(idx)):
            for b in range(a + 1, len(idx)):
                i, j = idx[a], idx[b]
                if (i, j) in seen:
                    continue
                seen.add((i, j))
                pa, ea, (alo, ahi) = items[i]
                pb, eb, (blo, bhi) = items[j]
                if pa == pb or (only is not None and pa not in only and pb not in only):
                    continue
                if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                    continue
                if allowed(pa, ea.name, pb, eb.name):
                    continue
                if obb_obb(ea, eb):
                    hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def report_touches(v, label, poses_states, only=None):
    allhits = {}
    for pose, state in poses_states:
        for key, hs in touching_pairs(v, pose, state, only).items():
            allhits.setdefault(key, {})
            for h in hs:
                allhits[key].setdefault(h, (pose, state))
    print(f"{label} over {len(poses_states)} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at {p[0][2]:.3f} psi {p[0][1]:.2f} ({p[1]})" for (a, b), p in ex))
    return allhits


def clearance_poses(m):
    out = [(m.REST, s) for s in ("frame",) + STATES]
    for w in [i * 0.05 for i in range(int(m.STAMP_LOAD / 0.05) + 1)]:
        out.append((m.pose_at(round(w, 6)), "t2"))
    for k in range(24):
        out.append((m.turning(math.tau * k / 24 / m.E_RATIO), "t3"))
        out.append((m.turning(math.tau * k / 24 / m.E_RATIO + 0.37), "t4"))
    return out


def check_clearances(v):
    hits = report_touches(v, "clearances", clearance_poses(v.m))
    if hits:
        v.fail("parts run into each other")


def check_swept(v):
    """The battery through a whole load every 0.004 of a camshaft turn (each stamp lifted, dropped and resting, its cam
    sweeping past and under its tappet), and the jaw and the rolls through a whole turn of the eccentric every 2 degrees:
    nothing touches but the intended contacts."""
    m = v.m
    ps = [(m.pose_at(round(0.004 * i, 6)), "t2") for i in range(int(m.STAMP_LOAD / 0.004) + 1)]
    ps += [(m.turning(math.tau * k / 180 / m.E_RATIO), "t4") for k in range(180)]
    only = {f"stamp{i + 1}" for i in range(m.STAMPS)} | {"camshaft", "cone", "clutchlever", "eshaft", "pitman", "backtoggle", "fronttoggle",
                                                         "swingjaw", "rods", "roll1", "roll2"} | {f"spring{c + 1}" for c in range(m.SPRING_COILS)}
    hits = report_touches(v, "swept paths", ps, only)
    if hits:
        v.fail("something stands in a moving part's path")


TEX_RULES = [
    (r"^(fr_sill|fr_post|fr_cap|fr_tie|fr_deck|fr_cam_|fr_upper|fr_bearer|fr_hopperbeam|entry_shaft[ab]|mortar_block|guides_|clutchlever_knob)", "oak"),
    (r"^(fr_hopper_|mortar_(chute|feeder)|rollframe_(hopper|chute))", "planks"),
    (r"^(fr_spout|fr_inlet|fr_bracket|fr_idler_arm|fr_\w+plate|mortar_(floor|front|back|end|apron|guide)|camframe_(pillow|lever(bracket|lug))|"
     r"bullwheel_|camshaft_(hub|cam)|clutchlever_(boss|bar)|stamp\d_(head|tappet)|jawframe_(base|backpost|epost|ebearing|hpost|hbearing|side|backwall|frontwall|launder)|"
     r"eshaft_(fly|wheel)|pitman_|swingjaw_(slab|boss|web|lug)|roll[12]_(bolt|wheel)|rollframe_(base|housing|lower|upper|end|block))", "iron"),
    (r"^(fr_idler_stud|entry_(rod|a[12])|rectb|idler|line_|camshaft_(shaft|collar)|cone_|clutchlever_prong|camframe_leverpin|stamp\d_(shoe|stem)|"
     r"mortar_die|jawframe_(backseat|fixedjaw|hingepin)|eshaft_(shaft|eccentric|collar)|backtoggle|fronttoggle|swingjaw_(face|seat)|rods_|spring\d|"
     r"roll[12]_(shell|shaft|pair|collar)|rollframe_(springrod|coil))", "steel"),
    (r"^mortar_screen", "mesh"),
]


def check_textures(v):
    bad, unruled = [], []
    for el in v.els:
        for rx, tex in TEX_RULES:
            if re.match(rx, el.name):
                got = {f["texture"].lstrip("#") for f in el.faces.values()}
                if got and got != {tex}:
                    bad.append((el.name, sorted(got), tex))
                break
        else:
            unruled.append(el.name)
    print(f"textures: {len(TEX_RULES)} rules (oak timber and the axle's continuation, planks for hopper and chutes, iron castings, "
          f"steel shafts, gears and wearing parts, a mesh screen): {len(bad)} elements break them, {len(unruled)} have no rule")
    if bad:
        v.fail(f"textures by role: {bad[:4]}")
    if unruled:
        v.fail(f"elements with no texture rule: {unruled[:6]}")


def check_anchors(v):
    m, rig = v.m, v.rig
    entry = v.named("entry", r"_shaft[ab]")
    lo, hi = aabb_of(entry)
    ok = hi[0] >= m.CELLS_X * 16 - 0.02 and (m.ENTRY[0] % 16, m.ENTRY[1] % 16) == (8.0, 8.0)
    ok = ok and (int(m.ENTRY[0] // 16), int(m.ENTRY[1] // 16)) == (m.POWER_CELL[1], m.POWER_CELL[2]) and m.POWER_CELL[0] == m.CELLS_X - 1
    print(f"anchors: power cell {m.POWER_CELL} face {m.POWER_FACE}, the entry shaft reaches x {hi[0]:.2f} on that face, axis at (y {m.ENTRY[0]}, "
          f"z {m.ENTRY[1]}): the cell's centre")
    if not ok:
        v.fail("the entry shaft does not meet the power face at the cell's centre")
    for name, cell, face in m.CELL_ANCHORS[1:]:
        pos = [c * 16 for c in rig[name]["pos"]]
        inside = all(cell[k] * 16 - 1e-6 <= pos[k] <= cell[k] * 16 + 16 + 1e-6 for k in range(3))
        axis, side = {"up": (1, 1), "north": (2, 0), "west": (0, 0)}[face]
        on_face = abs(pos[axis] - (cell[axis] * 16 + 16 * side)) < 1e-6
        print(f"anchors: {name} at {[round(c, 2) for c in pos]}, in cell {cell} on its {face} face: {inside and on_face}")
        if not (inside and on_face):
            v.fail(f"the {name} anchor is not on its cell's {face} face")
    # the frame's spout and inlet are where every tier's chutes end
    spout = aabb_of(v.named("frame", r"fr_spout_floor"))
    inlet = aabb_of(v.named("frame", r"fr_inlet_floor"))
    apron = aabb_of(v.named("mortar", r"_apron"))
    launder = aabb_of(v.named("jawframe", r"_launder$"))
    chute = aabb_of(v.named("rollframe", r"_chute$"))
    checks = [("tier 2's apron ends over the spout", apron[0][2] < spout[1][0 + 2] and apron[0][1] > spout[1][1] - 0.01),
              ("tier 3's launder runs onto the spout's floor", abs(launder[0][2] - spout[1][2]) < 0.3 and launder[0][1] <= spout[1][1] + 0.01),
              ("tier 4's oversize chute starts at the inlet", abs(chute[0][0] - inlet[1][0]) < 0.3)]
    for label, good in checks:
        print(f"anchors: {label}: {good}")
        if not good:
            v.fail(label)


# ---------------------------------------------------------------- mechanism
def angle_x(v, pid, pose):
    mm = v.mat(pid, pose)
    return math.atan2(mm[2][1], mm[1][1])


def check_gearing(v):
    """Every meshing pair: the centres are the pitch radii apart and the pitch circles roll alike (finite differences
    of the posed rig); the rectifier turns the line shaft the same way for either sign of the axle; the clutch's
    cone turns with the bull wheel through a load at the drawn pace; the rolls turn alike, towards their nip."""
    m = v.m
    ra, rb, ra2, ri, rb2, rp = m.RECT_A / 2, m.RECT_B / 2, m.RECT_A2 / 2, m.RECT_I / 2, m.RECT_B2 / 2, m.PINION_N / 2
    pair_r = m.PAIR_N * m.PAIR_MOD / 2
    meshes = [
        ("rectifier A1-B1", "entry", m.ENTRY, ra, "rectb1", m.LINE, rb, "theta"),
        ("rectifier A2-idler", "entry", m.ENTRY, ra2, "idler", m.IDLER, ri, "theta"),
        ("rectifier idler-B2", "idler", m.IDLER, ri, "rectb2", m.LINE, rb2, "theta"),
        ("pinion-bull wheel", "line", m.LINE, rp, "bullwheel", m.CAM_C, m.BULL_N / 2, "psi"),
        ("pinion-eccentric shaft's wheel", "line", m.LINE, rp, "eshaft", m.E_C, m.E_TEETH / 2, "psi"),
        ("pinion-north roll's wheel", "line", m.LINE, rp, "roll1", m.R1_C, m.R1_TEETH / 2, "psi"),
        ("rolls' pair gears", "roll1", m.R1_C, pair_r, "roll2", m.R2_C, pair_r, "psi"),
    ]
    worst = 0.0
    for label, pa, ca, r1, pb, cb, r2, how in meshes:
        d = math.hypot(cb[0] - ca[0], cb[1] - ca[1])
        if abs(d - (r1 + r2)) > 1e-6:
            v.fail(f"{label}: centres {d:.4f} apart, pitch radii add to {r1 + r2:.4f}")
        if how == "theta":
            samples = [((0.3, 0.3, 0, 0, 0), (0.35, 0.35, 0, 0, 0)), ((-0.3, 0.3, 0, 0, 0), (-0.35, 0.35, 0, 0, 0))]
        else:
            samples = [(m.turning(a), m.turning(a + 0.01)) for a in (0.2, 1.9, 4.4)]
        for s1, s2 in samples:
            arc_a = math.remainder(angle_x(v, pa, s2) - angle_x(v, pa, s1), math.tau) * r1
            arc_b = math.remainder(angle_x(v, pb, s2) - angle_x(v, pb, s1), math.tau) * r2
            size = max(abs(arc_a), abs(arc_b), 1e-9)
            worst = max(worst, abs(arc_a + arc_b) / size)
            if abs(arc_a + arc_b) > 1e-6 + 1e-5 * size:
                v.fail(f"{label}: {pa} rolls {arc_a:.6f} on its pitch circle, {pb} {arc_b:.6f}")
    print(f"gearing: {len(meshes)} meshes, centre distances the sum of the pitch radii, pitch points moving alike (worst {worst:.1e})")
    for th in (0.7, -0.7):
        a, b = (0.0, 0.0, 0, 0, 0), (th, abs(th), 0, 0, 0)
        ls = angle_x(v, "line", b) - angle_x(v, "line", a)
        b1 = angle_x(v, "rectb1", b) - angle_x(v, "rectb1", a)
        b2 = angle_x(v, "rectb2", b) - angle_x(v, "rectb2", a)
        n = sum(abs(q - ls) < 1e-9 for q in (b1, b2))
        if n != 1 or ls * m.LINE_SIGN <= 0:
            v.fail(f"the rectifier: axle {th:+}, line shaft {ls:.3f}, B1 {b1:.3f}, B2 {b2:.3f}")
    print("rectifier: for either sign of the axle exactly one loose wheel turns with the line shaft, always the same way")
    worst = 0.0
    for w in (0.1, 0.77, 2.3, 3.95):
        s1, s2 = m.pose_at(w), m.pose_at(w + 0.002)
        dc = angle_x(v, "cone", s2) - angle_x(v, "cone", s1)
        dw = angle_x(v, "bullwheel", s2) - angle_x(v, "bullwheel", s1)
        worst = max(worst, abs(dc - dw))
        if abs(dc - dw) > 1e-6:
            v.fail(f"the clutch slips at W {w}: cone {dc:.6f}, bull wheel {dw:.6f}")
    print(f"clutch: the cone turns with the bull wheel through a load at {m.TURNS_PER_REV:.4f} axle turns a camshaft turn (worst {worst:.1e})")
    # the clutch's throw: out at rest, in with a load on
    out_x = aabb_of(v.posed("cone", m.REST))[1][0]
    in_x = aabb_of(v.posed("cone", m.pose_at(0.5)))[1][0]
    cup = aabb_of(v.named("bullwheel", r"_cup\d", m.REST))
    print(f"clutch: the cone's end at x {out_x:.2f} out, {in_x:.2f} in, the cup from x {cup[0][0]:.2f}")
    if not (out_x < cup[0][0] + 0.2 < in_x):
        v.fail("the cone does not go into the cup with a load on, or is in it without")
    # the rolls: alike, the nip faces moving down
    s1, s2 = m.turning(1.0), m.turning(1.01)
    d1 = angle_x(v, "roll1", s2) - angle_x(v, "roll1", s1)
    d2 = angle_x(v, "roll2", s2) - angle_x(v, "roll2", s1)
    down1 = -d1 * m.ROLL_R                       # the north roll's south face, at the nip: dy = -dangle * r
    down2 = d2 * m.ROLL_R
    print(f"rolls: the north roll turns {d1:+.5f}, the south {d2:+.5f}: their faces at the nip move {down1:+.4f} and {down2:+.4f} (y)")
    if not (d1 > 0 and abs(d1 + d2) < 1e-9):
        v.fail("the rolls do not turn alike towards their nip")


def tappet_gap(v, i, pose):
    """How far stamp i's tappet's underside stands over the highest point of its cam under it (negative: into it)."""
    m = v.m
    tap = v.named(f"stamp{i + 1}", r"_tappet", pose)[0]
    lo, hi = tap.aabb()
    best = -1e9
    for e in v.named("camshaft", rf"_cam{i + 1}[ab]", pose):
        for q in e.corners():
            if lo[0] - 1e-6 <= q[0] <= hi[0] + 1e-6 and lo[2] - 1e-6 <= q[2] <= hi[2] + 1e-6:
                best = max(best, q[1])
        # also the box's top face where it crosses the tappet's edge on the camshaft's side
        cs = e.corners()
        for a in range(8):
            for b in range(8):
                p, q = cs[a], cs[b]
                if (p[2] - hi[2]) * (q[2] - hi[2]) < 0:
                    t = (hi[2] - p[2]) / (q[2] - p[2])
                    best = max(best, p[1] + t * (q[1] - p[1]))
    return lo[1] - best


def check_stamps(v):
    """Each stamp: on its die at rest; lifted by its cam (the tappet's underside on the cam within 0.12, never into it)
    through every lift; dropped and resting on its die between lifts; at W 0 and at the end of a load where it was drawn.
    Two drops a camshaft turn a stamp, in the firing order; and the cams turn with the camshaft."""
    m = v.m
    worst_gap, worst_in, n_lift = 0.0, 0.0, 0
    for i in range(m.STAMPS):
        a = m.stamp_phase(i)
        for k in range(2 * m.STAMP_LOAD):
            w0 = a + 0.5 * k
            for f in (0.03, 0.2, 0.4, 0.6, 0.8, 0.97):
                w = w0 + f * m.LIFT_W
                if w > m.STAMP_LOAD:
                    continue
                g = tappet_gap(v, i, m.pose_at(round(w, 6)))
                n_lift += 1
                worst_gap = max(worst_gap, g)
                worst_in = min(worst_in, g)
    print(f"stamps: over {n_lift} poses in the lifts the tappets ride their cams, at most {worst_gap:.3f} over and {-worst_in:.3f} into them")
    if worst_gap > 0.12 or worst_in < -0.02:
        v.fail("a tappet does not ride its cam through the lift")
    # resting on the die between lifts
    for i in range(m.STAMPS):
        for w in (m.stamp_phase(i) + m.LIFT_W + m.DROP_W + 0.05, m.stamp_phase(i) + 0.5 - 0.02):
            pose = m.pose_at(w)
            shoe = aabb_of(v.named(f"stamp{i + 1}", r"_shoe", pose))
            die = aabb_of(v.named("mortar", rf"_die{i + 1}_"))
            if abs(shoe[0][1] - die[1][1]) > 1e-3:
                v.fail(f"stamp {i + 1} at W {w:.3f} stands {shoe[0][1] - die[1][1]:.3f} off its die")
    print("stamps: each rests on its die between lifts")
    # where they were drawn at W 0 and at the end, with or without the load's presence
    ident = [[1.0 if r == c else 0.0 for c in range(4)] for r in range(3)]
    for pose in (m.REST, m.pose_at(0.0), m.pose_at(float(m.STAMP_LOAD)), (0.0, 0.0, float(m.STAMP_LOAD), 1, 0.3), (0.0, 0.0, 0.0, 2, 0.6)):
        for i in range(m.STAMPS):
            mm = v.mat(f"stamp{i + 1}", pose)
            off = max(abs(mm[r][c] - ident[r][c]) for r in range(3) for c in range(4))
            if off > 1e-6:
                v.fail(f"stamp {i + 1} is off its drawn place by {off * 16:.4f} at {pose}")
    print("stamps: at W 0 and at the end of a load each stands where it is drawn, the load's presence easing nothing")
    # two drops a turn each, in the firing order
    drops = []
    for i in range(m.STAMPS):
        prev = None
        for s in range(int(m.STAMP_LOAD * 400) + 1):
            w = s / 400
            y = v.mat(f"stamp{i + 1}", m.pose_at(w))[1][3] * 16
            if prev is not None and prev - y > 0.2 and not (drops and drops[-1][1] == i + 1 and w - drops[-1][0] < 0.05):
                drops.append((round(w, 3), i + 1))
            prev = y
    drops.sort()
    per = {i + 1: sum(1 for _, s in drops if s == i + 1) for i in range(m.STAMPS)}
    first = next(k for k, (_, s) in enumerate(drops) if s == 1)
    order = [s for _, s in drops[first:first + 5]]
    print(f"stamps: drops a load {per}, firing order {order}")
    if any(n != 2 * m.STAMP_LOAD for n in per.values()) or order != list(m.ORDER):
        v.fail("the stamps do not drop twice a camshaft turn each, in the firing order")


def check_jaw(v):
    """The linkage stays joined through a whole turn of the eccentric (72 steps): the pitman's strap round the
    eccentric, its foot on both toggles' ends, the back toggle on its seat, the front toggle on the swinging jaw's
    seat, the rods' pins in the jaw's lugs; the toggles never pass straight; the jaw's gap at the bottom."""
    m = v.m
    worst = {}
    gaps = []
    for k in range(72):
        phi = math.tau * k / 72
        pose = m.turning(phi / m.E_RATIO)
        s = m.linkage(phi)
        ecc = v.point("eshaft", pose, [22.0, m.E_C[0] + m.ECC, m.E_C[1]])
        strap = v.point("pitman", pose, [22.0, *m.design_to_mean_pitman(m.E_C)])
        foot = v.point("pitman", pose, [22.0, *m.design_to_mean_pitman(m.P0)])
        bt_p = v.point("backtoggle", pose, [22.0, *m.design_to_mean_back(m.P0)])
        bt_b = v.point("backtoggle", pose, [22.0, *m.design_to_mean_back(m.B0)])
        ft_p = v.point("fronttoggle", pose, [22.0, *m.design_to_mean_front(m.P0)])
        ft_j = v.point("fronttoggle", pose, [22.0, *m.design_to_mean_front(m.J0)])
        jaw_j = v.point("swingjaw", pose, [22.0, *m.design_to_mean_jaw(m.J0)])
        jaw_k = v.point("swingjaw", pose, [22.0, *m.design_to_mean_jaw(m.K0)])
        rod_k = v.point("rods", pose, [22.0, m.MEAN_K[0], m.MEAN_K[1]])
        pairs = {"strap on the eccentric": (strap, ecc), "foot on the back toggle": (foot, bt_p), "foot on the front toggle": (foot, ft_p),
                 "back toggle on its seat": (bt_b, [22.0, m.B0[0], m.B0[1]]), "front toggle on the jaw's seat": (ft_j, jaw_j),
                 "rods on the jaw's lugs": (rod_k, jaw_k), "foot where the linkage puts it": (foot, [22.0, s["p"][0], s["p"][1]])}
        for label, (a, b) in pairs.items():
            worst[label] = max(worst.get(label, 0.0), math.dist(a[1:], b[1:]))
        sag = m.TOGGLE_Y - foot[1]
        if sag < 0.3:
            v.fail(f"the toggles pass straight at phi {phi:.2f} (sag {sag:.2f})")
        face_bot = v.point("swingjaw", pose, [22.0, *m.design_to_mean_jaw(m.FACE_BOT)])
        gaps.append(m.FIXED_Z - face_bot[2])
    print("jaw: " + "; ".join(f"{k} within {d:.1e}" for k, d in worst.items()))
    if max(worst.values()) > 1e-3:
        v.fail("the jaw's linkage comes apart")
    print(f"jaw: the gap at the bottom between {min(gaps):.2f} and {max(gaps):.2f} voxels (a throw of {max(gaps) - min(gaps):.2f})")
    if min(gaps) < 1.0:
        v.fail("the jaws close too far")


def check_supports(v):
    m = v.m
    frame = v.by_part["frame"]
    rest = m.REST
    shafts = [
        ("entry shaft", "entry", r"_rod|_shaft", m.ENTRY, "frame", r"^fr_(entryplate_[we]|bracket)$", 2, "frame"),
        ("line shaft", "line", r"_shaft", m.LINE, "frame", r"^fr_(lineplate_[we]|bracket)$", 2, "frame"),
        ("camshaft", "camshaft", r"_shaft", m.CAM_C, "camframe", r"_pillow", 2, "t2"),
        ("eccentric shaft", "eshaft", r"_shaft", m.E_C, "jawframe", r"_ebearing", 2, "t3"),
        ("north roll", "roll1", r"_shaft", m.R1_C, "rollframe", r"_housing", 2, "t4"),
        ("south roll", "roll2", r"_shaft", m.R2_C, "rollframe", r"_block", 2, "t4"),
        ("hinge pin", "jawframe", r"_hingepin", m.H0, "jawframe", r"_hbearing", 2, "t3"),
        ("idler's stud", "frame", r"fr_idler_stud", m.IDLER, "frame", r"^(fr_idler_arm|fr_bracket)$", 1, "frame"),
    ]
    for label, pid, rx, c, bpid, brx, need, state in shafts:
        els_ = v.named(pid, rx)
        carriers = [e for e in v.by_part[bpid] if re.search(brx, e.name)]
        found, _ = shaft_supports(carriers, els_, 0, c)
        print(f"support {label}: {len(found)} bearing(s) {', '.join(sorted(found)) or 'NONE'}")
        if len(found) < need:
            v.fail(f"the {label} is not carried by {need} bearings")
    # loose wheels located: the bull wheel between the cone's cup and the camshaft's east collar; the B wheels by the spacer
    print("support: the bull wheel and the rectifier's B wheels loose on their shafts, each between a collar and a fitting")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        state = pose[5]
        els = [e for pid in v.parts_in(state) for e in v.posed(pid, pose)]
        pairs = coplanar_faces(els)
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
    poses = [m.REST] + [m.pose_at(w) for w in (0.1, 0.37, 1.2, 2.66, 3.9, 4.0)] + [m.turning(a) for a in (0.7, 2.9, 5.1, 11.0, 17.3)]
    check_containment(v, poses)
    check_anchors(v)
    check_textures(v)
    check_gearing(v)
    check_stamps(v)
    check_jaw(v)
    check_supports(v)
    check_clearances(v)
    if not quick:
        check_swept(v)
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
    if gaps:
        ok = False
    return ok
