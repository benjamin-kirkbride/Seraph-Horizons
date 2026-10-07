"""The draw bench generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved.
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


LEAD = re.compile(r"^l(slug|sect)\d")
COPPER = re.compile(r"^c(slug|sect)\d")


def present(pid, k):
    """The lead set shows only with lead on the bench, the copper set only with copper."""
    if LEAD.match(pid):
        return k == 1
    if COPPER.match(pid):
        return k == 2
    return True


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
    links = sum(n for k, n in counts.items() if re.match(r"^ch\d\d$", k))
    work = sum(n for k, n in counts.items() if LEAD.match(k) or COPPER.match(k))
    print(f"parts: {len(v.parts)}, elements {len(v.els)}: " + ", ".join(f"{k} {n}" for k, n in counts.items()
                                                                         if not (re.match(r"^ch\d\d$", k) or LEAD.match(k) or COPPER.match(k) or re.match(r"^(spring|rope)\d", k)))
          + f", chain links of their own {links}, slugs and sections {work}")


def check_containment(v, poses):
    m = v.m
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where = -1e9, None
    for pose in poses:
        k = pose[3]
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


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        k = pose[3]
        els = [e for pid in v.by_part if present(pid, k) for e in v.posed(pid, pose)]
        pairs = coplanar_faces([e for e in m.shown(els, pose) if e.c[1] > -500])
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses")
    if bad:
        v.fail("faces z-fight")


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels at every sampled pose.
ALLOWED = [
    # shafts in their bearings, studs and pins in their eyes
    ("entry", r"_shaft", "frame", r"fr_cheek_[we]"),
    ("rectshaft", None, "frame", r"fr_rect\d_bearing|fr_cheek_[we]"),
    ("driveshaft", r"_rod", "frame", r"fr_drive\d_bearing|fr_cheek_w"),
    ("returnshaft", r"_rod", "frame", r"fr_(beam_w|postn)"),
    ("barrel", r"_rod", "frame", r"fr_barrel_bearing"),
    ("idler", None, "frame", r"fr_idler_stud"),
    ("startlever", r"_shaft", "frame", r"fr_lever_bearing"),
    ("crank", r"_boss", "frame", r"fr_crank_post"),
    ("clutchrod", r"_bar|_stop", "frame", r"fr_rod_guide"),
    ("selector", r"_rod", "frame", r"fr_selector_guide"),
    # loose wheels, sleeves and keyed fittings on their shafts
    ("rect(b1|b2)", None, "rectshaft", None), ("cup", None, "rectshaft", None), ("sleeve", None, "rectshaft", None),
    ("cone", None, "sleeve|rectshaft", None), ("cluster", None, "sleeve|rectshaft", None),
    ("drivesprocket", None, "driveshaft", r"_rod"), ("returnsprocket", None, "returnshaft", r"_rod"),
    # meshing wheels (their pitch circles are checked by check_gearing)
    ("entry", r"entry_a1", "rectb1", None), ("entry", r"entry_a2", "idler", None), ("idler", None, "rectb2", None),
    ("cluster", r"cluster_a", "driveshaft", r"_ap"), ("cluster", r"cluster_b", "driveshaft", r"_bp"),
    ("returnshaft", r"_gear", "barrel", r"_gear"),
    # the cone in the cup, the forks in their grooves, the pins of the controls
    ("cone", None, "cup", None), ("crank", r"_fork", "cone", r"_groove|_flange"), ("selector", r"_fork", "cluster", r"_groove|_hub"),
    ("clutchrod", r"_pin|_bar", "startlever", r"_rodarm"), ("clutchrod", r"_bar", "crank", r"_rodarm"),
    ("startlever", r"_finger", "jaw", r"_knuckle"),
    # the chain on its sprockets, its links on each other, its ends on the dog's pins
    ("ch\\d\\d|chaintop|chainbottom", None, "ch\\d\\d|chaintop|chainbottom", None),
    ("ch\\d\\d|chaintop|chainbottom", None, "(drive|return)sprocket", None),
    ("ch\\d\\d|chaintop", None, "dog", r"dog_(shank|pin)"),
    # the dog on its ways, its lug round the clutch rod and on its collar, the jaw on its pin and spring
    ("dog", r"_sled", "frame", r"fr_(way_w|girder_e)"), ("dog", r"_lug", "clutchrod", None),
    ("jaw", None, "dog", r"_jawpin|_topplate|_spring"),
    # the rope on its barrel and in its weight; the weight on its pad
    ("rope\\d", None, "weight", None), ("rope\\d", None, "rope\\d", None), ("rope\\d", None, "ropetop", None),
    ("barrel", None, "ropetop", None), ("weight", None, "frame", r"fr_weight_pad"),
    # the mandrel in the tail stock and the die stock, the follower and spring on it
    ("mandrel", None, "frame", r"fr_(tailstock|diestock)"), ("mandrel", None, "die", None),
    ("follower", None, "mandrel", None), ("spring\\d", None, "mandrel", r"_nut"), ("spring\\d", None, "frame", r"fr_tailstock"),
    ("spring\\d", None, "follower", None),
    ("die", None, "frame", r"fr_diestock"),
    # the work: slugs on the mandrel, against the follower, into the die stock; sections through the die and
    # over the plug, in the jaws, on the skids and against each other on the rack
    ("[lc]slug\\d", None, "mandrel", None), ("[lc]slug\\d", None, "follower", None), ("[lc]slug\\d", None, "[lc]slug\\d", None),
    ("[lc]slug\\d", None, "frame", r"fr_(diestock|oiler)"), ("[lc]slug\\d", None, "[lc]sect\\d[ab]", None),
    ("[lc]sect\\d[ab]", None, "[lc]sect\\d[ab]", None), ("[lc]sect\\d[ab]", None, "mandrel", None),
    ("[lc]sect\\d[ab]", None, "die", None), ("[lc]sect\\d[ab]", None, "frame", r"fr_(diestock|skid|lip)"),
    ("[lc]sect\\d[ab]", None, "dog", r"_jawfixed"), ("[lc]sect\\d[ab]", None, "jaw", r"_face"),
    # the oil in its cup
    ("oillevel", None, "frame", r"fr_oiler_(base|glass)"),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose):
    k = pose[3]
    items = []
    for pid in v.by_part:
        if not present(pid, k):
            continue
        for el in v.posed(pid, pose):
            items.append((pid, el, el.aabb()))
    grid = {}
    for i, (pid, el, (lo, hi)) in enumerate(items):
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


def check_clearances(v, poses):
    allhits = {}
    for pose in poses:
        for key, hs in touching_pairs(v, pose).items():
            allhits.setdefault(key, {})
            for h in hs:
                allhits[key].setdefault(h, pose)
    print(f"clearances over {len(poses)} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at W {p[2]:.3f} k {p[3]}" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


def check_swept(v):
    """Swept paths: the dog through its whole stroke and back, every quarter voxel of its travel, and the
    section through its drop and roll, every 0.005 of a cycle, touch nothing but their intended contacts
    (both metals, the first and the last section)."""
    m = v.m
    span = m.T_DRAW[1] - m.T_DRAW[0]
    ts = [m.T_DRAW[0] + span * i / 64 for i in range(65)]
    ts += [m.T_RETURN[0] + (m.T_RETURN[1] - m.T_RETURN[0]) * i / 32 for i in range(33)]
    ts += [m.T_OPEN[0] + 0.005 * i for i in range(int((m.T_ROLL[1] - m.T_OPEN[0]) / 0.005) + 1)]
    n = 0
    hits = {}
    for k in (1, 2):
        for mm in (0, m.SLUGS - 1):
            for t in ts:
                pose = m.pose_at(k, mm + t)
                n += 1
                for key, hs in touching_pairs(v, pose).items():
                    for h in hs:
                        hits.setdefault(key, {}).setdefault(h, pose)
    print(f"swept paths: the dog and the section at {n} poses: {'all clear' if not hits else f'{len(hits)} pairs of parts touch'}")
    for key, hs in sorted(hits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at W {p[2]:.3f} k {p[3]}" for (a, b), p in ex))
    if hits:
        v.fail("something stands in the dog's or the section's path")


def cycle_ts():
    return (0.0, 0.02, 0.04, 0.1, 0.2, 0.3, 0.38, 0.39, 0.4, 0.41, 0.42, 0.43, 0.44, 0.45, 0.46, 0.48, 0.5, 0.52, 0.6, 0.72, 0.85, 0.92, 0.96)


def clearance_poses(m):
    out = [m.REST]
    for k in (1, 2):
        for mm in range(m.SLUGS):
            for t in cycle_ts():
                out.append(m.pose_at(k, mm + t))
        out.append(m.pose_at(k, float(m.SLUGS)))
    return out


TEX_RULES = [
    (r"^(fr_sill|fr_tie|fr_post[wen]|fr_beam|fr_weight_pad|entry_shaft[ab]|barrel_drum)", "oak"),
    (r"^(fr_way|fr_girder|fr_tailstock|fr_diestock|fr_head|fr_cheek|fr_\w+_(bearing|post)|fr_skid|fr_lip|weight_|dog_(sled|head|topplate)|crank_(boss|rodarm|forkarm)|ch\d\d_|chaintop_|chainbot_|(drive|return)sprocket_)", "iron"),
    (r"^(entry_a|rectb|idler_|rectshaft_|sleeve_|cluster_|driveshaft_|returnshaft_|barrel_(rod|gear)|mandrel_|spring\d|dog_(jawfixed|pin|jawpin|spring)|jaw_(face|boss)|clutchrod_|selector_(rod|fork))", "steel"),
    (r"^(cup_|cone_step)", "cupronickel"),
    (r"^die_", "die"),
    (r"^lslug", "lead"),
    (r"^cslug", "copper"),
    (r"^lsect", "leadsheet"),
    (r"^csect", "coppersheet"),
    (r"^(rope\d|ropetop|barrel_wraps)", "rope"),
    (r"^fr_oiler_glass", "glass"),
    (r"^fr_oiler_(base|cap|spout|tip|feed)|^(startlever|selector)_knob", "brass"),
    (r"^oillevel_", "oil"),
]


def check_textures(v):
    bad = []
    for el in v.els:
        for rx, tex in TEX_RULES:
            if re.match(rx, el.name):
                got = {f["texture"].lstrip("#") for f in el.faces.values()}
                if got != {tex}:
                    bad.append((el.name, sorted(got), tex))
                break
    print(f"textures: {len(TEX_RULES)} rules (oak timber and the axle's continuation, iron castings and chain, steel shafts, "
          f"gears and wearing parts, a cupronickel clutch, the die's own code, lead and copper work, rope, a glass and brass "
          f"oiler with oil in it): {len(bad)} elements break them")
    if bad:
        v.fail(f"textures by role: {bad[:4]}")


def check_anchors(v):
    m = v.m
    rig = v.rig
    pc = tuple(rig["powerCell"])
    entry = v.named("entry", r"_shaft")
    lo, _ = aabb_of(entry)
    print(f"anchors: power cell {pc} face {rig['powerFace']}, the entry shaft starts at x {lo[0]:.2f} on that face, axis at "
          f"(y {m.ENTRY[0]}, z {m.ENTRY[1]}): the cell's centre")
    if lo[0] > 0.01 or pc != m.POWER_CELL or (m.ENTRY[0] % 16, m.ENTRY[1] % 16) != (8.0, 8.0) or m.ENTRY[1] // 16 != m.POWER_CELL[2]:
        v.fail("the entry shaft does not meet the power face at the cell's centre")


# ---------------------------------------------------------------- mechanism
def disp(v, pid, p, ps1, ps2):
    """How far the material point of part pid at p (voxels, at pose ps1) moves going to ps2."""
    m1, m2 = v.mat(pid, ps1), v.mat(pid, ps2)
    r = [row[:3] for row in m1[:3]]
    t = [m1[i][3] * 16 for i in range(3)]
    q = [p[i] - t[i] for i in range(3)]
    x = [sum(r[j][i] * q[j] for j in range(3)) for i in range(3)]
    return [sum(m2[i][j] * x[j] for j in range(3)) + m2[i][3] * 16 - p[i] for i in range(3)]


def norm(a):
    return math.sqrt(sum(x * x for x in a))


def angle_x(v, pid, pose):
    mm = v.mat(pid, pose)
    return math.atan2(mm[2][1], mm[1][1])


def dog_s(v, pose):
    return v.mat("dog", pose)[2][3] * 16


def check_gearing(v):
    """Every meshing pair: the centre distance is the sum of the pitch radii, and the pitch point moves
    alike on both wheels (finite differences of the posed rig); the rectifier turns the rectified shaft
    forward either way; the cone and the cup turn together while the clutch is in, at each metal's pace;
    the chain's links on a sprocket turn with it."""
    m = v.m
    r = m.RECT_R
    pairs = [
        ("rectifier A1-B1", "entry", m.ENTRY_C, r["a1"], "rectb1", m.RECT_C, r["b1"], "theta"),
        ("rectifier A2-idler", "entry", m.ENTRY_C, r["a2"], "idler", m.IDLER_C, r["i"], "theta"),
        ("rectifier idler-B2", "idler", m.IDLER_C, r["i"], "rectb2", m.RECT_C, r["b2"], "theta"),
        ("change gear, lead 16:24", "cluster", m.RECT_C, m.CG_R["a"], "driveshaft", m.D_C, m.CG_R["ap"], 1),
        ("change gear, copper 10:30", "cluster", m.RECT_C, m.CG_R["b"], "driveshaft", m.D_C, m.CG_R["bp"], 2),
        ("return gears 1:1", "returnshaft", m.N_C, m.RET_R, "barrel", m.K_C, m.RET_R, 1),
    ]
    worst = 0.0
    for label, pa, ca, ra, pb, cb, rb, how in pairs:
        d = math.hypot(cb[1] - ca[1], cb[2] - ca[2])
        if abs(d - (ra + rb)) > 1e-6:
            v.fail(f"{label}: centres {d:.4f} apart, pitch radii add to {ra + rb:.4f}")
        if how == "theta":
            samples = [((0.3, 0.3, 0, 0, 0, 0), (0.35, 0.35, 0, 0, 0, 0)), ((-0.3, 0.3, 0, 0, 0, 0), (-0.35, 0.35, 0, 0, 0, 0))]
        else:
            samples = [(m.pose_at(how, w), m.pose_at(how, w + 0.004)) for w in (0.1, 0.3, 1.25, 0.6, 2.8)]
        for s1, s2 in samples:
            # an external mesh: the arcs rolled on the two pitch circles are equal and opposite
            arc_a = math.remainder(angle_x(v, pa, s2) - angle_x(v, pa, s1), 2 * math.pi) * ra
            arc_b = math.remainder(angle_x(v, pb, s2) - angle_x(v, pb, s1), 2 * math.pi) * rb
            size = max(abs(arc_a), abs(arc_b), 1e-9)
            worst = max(worst, abs(arc_a + arc_b) / size)
            if abs(arc_a + arc_b) > 1e-6 + 1e-5 * size:
                v.fail(f"{label}: {pa} rolls {arc_a:.6f} on its pitch circle, {pb} {arc_b:.6f}")
    print(f"gearing: {len(pairs)} meshes, centre distances the sum of the pitch radii, pitch points moving alike (worst {worst:.1e})")
    # the rectifier: whichever way the axle turns, one of B1 and B2 turns with the rectified shaft
    for th in (0.7, -0.7):
        a, b = (0.0, 0.0, 0, 0, 0, 0), (th, abs(th), 0, 0, 0, 0)
        rs = angle_x(v, "rectshaft", b) - angle_x(v, "rectshaft", a)
        b1 = angle_x(v, "rectb1", b) - angle_x(v, "rectb1", a)
        b2 = angle_x(v, "rectb2", b) - angle_x(v, "rectb2", a)
        n = sum(abs(q - rs) < 1e-9 for q in (b1, b2))
        if n != 1 or rs >= 0:
            v.fail(f"the rectifier: axle {th:+}, shaft {rs:.3f}, B1 {b1:.3f}, B2 {b2:.3f}")
    print("rectifier: for either sign of the axle exactly one loose wheel turns with the rectified shaft, always the draw's way")
    # the clutch: the cone (on the sleeve) and the cup (on the rectified shaft) together while in, each metal at its pace
    for k in (1, 2):
        for f in (0.1, 0.5, 0.9):
            w = m.T_DRAW[0] + (m.T_DRAW[1] - m.T_DRAW[0]) * f
            s1, s2 = m.pose_at(k, w), m.pose_at(k, w + 0.002)
            dc = angle_x(v, "cone", s2) - angle_x(v, "cone", s1)
            du = angle_x(v, "cup", s2) - angle_x(v, "cup", s1)
            if abs(dc - du) > 1e-6 * max(1.0, abs(du)) + 1e-9:
                v.fail(f"the clutch slips: metal {k} at W {w}: cone {dc:.6f}, cup {du:.6f}")
    print(f"clutch: the cone turns with the cup through the draw, at {m.turns_per_section('thin'):.3f} axle turns a section (lead) and "
          f"{m.turns_per_section('thick'):.3f} (copper)")


def check_chain(v):
    """Every link lies on the chain's line, along it, as far round it as the dog has gone; the sprockets
    turn as the links on them."""
    m = v.m
    worst = 0.0
    poses = [m.REST] + [m.pose_at(k, w) for k in (1, 2) for w in (0.1, 0.25, 0.44, 0.6, 0.8, 1.3, 2.44)]
    for pose in poses:
        s = dog_s(v, pose)
        for i in range(m.N_LINKS):
            kind = m.link_kind(i)
            pid = {"top": "chaintop", "bottom": "chainbottom"}.get(kind, f"ch{i + 1:02d}")
            el = [e for e in v.posed(pid, pose) if e.name.endswith(f"_link{i + 1:02d}")][0]
            y, z, a = m.loop_point(m.link_u(i) + s)
            err = math.hypot(el.c[1] - y, el.c[2] - z)
            dirn = (el.r[1][2], el.r[2][2])        # the link's local z axis in the y-z plane
            ang_err = abs(math.atan2(math.sin(math.atan2(-dirn[0], dirn[1]) - a), math.cos(math.atan2(-dirn[0], dirn[1]) - a)))
            worst = max(worst, err, ang_err)
            if err > 0.01 or ang_err > 1e-3:
                v.fail(f"chain link {i + 1} at W {pose[2]} is {err:.3f} off its line (dog at {s:.2f})")
    print(f"chain: {m.N_LINKS} links of pitch {m.PITCH:.4f}, ends on the dog's pins {m.PIN_SPAN:.3f} apart; every link on its line at "
          f"{len(poses)} poses (worst {worst:.1e})")
    # the sprockets turn s / R_C, as the links on them
    for pid, z in (("drivesprocket", m.D_Z), ("returnsprocket", m.N_Z)):
        for pose in poses[1:]:
            s = dog_s(v, pose)
            ang = angle_x(v, pid, pose)
            want = s / m.R_C
            if abs(math.remainder(ang - want, 2 * math.pi)) > 1e-6:
                v.fail(f"{pid} turns {ang:.4f}, the chain {want:.4f}")
    print("chain: both sprockets turn with the chain (the dog's travel over the chain's radius)")


def spans_union(spans):
    out = []
    for a, b in sorted(spans):
        if out and a <= out[-1][1] + 0.02:
            out[-1][1] = max(out[-1][1], b)
        else:
            out.append([a, b])
    return out


def check_work(v):
    """The section being drawn is whole from the die's mouth to its point in the jaws (the rest of it hidden in
    the die stock), the slugs lie end to end from the follower to the die stock, the spring's coils share
    the follower's travel; the sections on the rack lie flat on the skids, one beside the other."""
    m = v.m
    worst = 0.0
    for k, pre in ((1, "l"), (2, "c")):
        for mm in range(m.SLUGS):
            for t in [0.0] + [m.T_DRAW[0] + (m.T_TUBE - m.T_DRAW[0]) * f for f in (0.0, 0.2, 0.45, 0.7, 0.95, 1.0)]:
                pose = m.pose_at(k, mm + t)
                s = min(dog_s(v, pose), m.S_TUBE)
                nose = m.Z_MOUTH + m.POINT + s
                vis, hidden_ok = [], True
                for j in range(m.NSEG):
                    for e in v.posed(f"{pre}sect{mm + 1}{'ab'[j]}", pose):
                        lo, hi = e.aabb()
                        if hi[2] > m.Z_MOUTH + 1e-6:
                            vis.append((max(lo[2], m.Z_MOUTH), hi[2]))
                        if lo[2] < m.Z_MOUTH - 1e-6 and lo[2] < m.Z_DIE_BACK - 1e-6:
                            hidden_ok = False
                u = spans_union(vis)
                if len(u) != 1 or abs(u[0][0] - m.Z_MOUTH) > 0.03 or abs(u[0][1] - nose) > 0.03 or not hidden_ok:
                    v.fail(f"section {mm + 1} ({pre}) at W {mm + t}: shown {u}, want {m.Z_MOUTH}..{nose:.2f}")
                # the point sits in the jaws: the dog's back face is just ahead of it
                gap = (m.DOG_Z + dog_s(v, pose)) - nose
                worst = max(worst, abs(gap - (m.DOG_Z - (m.Z_MOUTH + m.POINT))))
            # the slugs: end to end from the follower to the die stock
            for t in (0.0, (m.T_DRAW[0] + m.T_TUBE) / 2, m.T_TUBE, m.T_RETURN[0]):
                pose = m.pose_at(k, mm + t)
                spans = [(e.aabb()[0][2], min(e.aabb()[1][2], m.Z_DIE_BACK)) for q in range(m.SLUGS) for e in v.posed(f"{pre}slug{q + 1}", pose)
                         if e.aabb()[0][2] < m.Z_DIE_BACK - 1e-6]
                fol = max(e.aabb()[1][2] for e in v.posed("follower", pose) if "plate" in e.name)
                u = spans_union(spans)
                if spans and (len(u) != 1 or abs(u[0][0] - fol) > 0.03 or abs(u[0][1] - m.Z_DIE_BACK) > 0.03):
                    v.fail(f"slugs ({pre}) at W {mm + t}: {u}, the follower's face at {fol:.2f}")
                if not spans and abs(fol - m.Z_DIE_BACK) > 0.03:
                    v.fail(f"no slugs left but the follower at {fol:.2f}")
    print(f"work: each section whole from the die's mouth to its point while drawn, its hidden part in the die stock; the slugs end to "
          f"end from the follower to the die stock (the point stays in the jaws to {worst:.1e})")
    # between strokes the die's bore is empty: what is seen in it is the frontmost hidden segment's face (its
    # four strips, 0.012 apart, cover the bore); the next thing behind it stands at least 0.01 further back
    for k, pre in ((1, "l"), (2, "c")):
        for w in (0.6, 1.6, 2.6, 0.0):
            pose = m.pose_at(k, w)
            fronts = {}
            for pid in [f"{pre}sect{q + 1}{c}" for q in range(m.SLUGS) for c in "ab"] + ["mandrel"]:
                for e in v.posed(pid, pose):
                    lo, hi = e.aabb()
                    if lo[2] < m.Z_MOUTH - 0.1 and m.Z_DIE_RING[0] - 1e-6 <= hi[2] <= m.Z_MOUTH + 1e-6:
                        fronts.setdefault(pid, []).append(hi[2])
            if not fronts:
                continue
            order = sorted(fronts, key=lambda q: -max(fronts[q]))
            first = order[0]
            if len(order) > 1 and min(fronts[first]) - max(fronts[order[1]]) < 0.01 - 1e-6:
                v.fail(f"in the die's bore at W {w} ({pre}): {order[1]} stands {min(fronts[first]) - max(fronts[order[1]]):.4f} behind {first}")
    print("bore: the face seen in the empty die's bore stands clear of whatever is behind it")
    # the spring's coils share the follower's travel
    for w in (0.0, 0.3, 1.39, 3.0):
        pose = m.pose_at(1, w)
        fol = min(e.aabb()[0][2] for e in v.posed("follower", pose) if "boss" in e.name)
        for i in range(m.COILS):
            z = sum(e.c[2] for e in v.posed(f"spring{i + 1}", pose)) / len(v.posed(f"spring{i + 1}", pose))
            want = m.Z_SPRING0 + (fol - m.Z_SPRING0) * (i + 0.5) / m.COILS
            if abs(z - want) > 0.01:
                v.fail(f"spring coil {i + 1} at {z:.3f}, want {want:.3f} (W {w})")
    print(f"spring: {m.COILS} coils evenly between the tail stock and the follower at every W")
    # the sections on the rack: each in its place, a face flat on the skids, the first against the lip, the
    # others beside it along the slope
    pose = m.pose_at(1, 3.0)
    for mm in range(m.SLUGS):
        els_ = [e for j in range(m.NSEG) for e in v.posed(f"lsect{mm + 1}{'ab'[j]}", pose)]
        cx = sum(e.c[0] for e in els_) / len(els_)
        cy = sum(e.c[1] for e in els_) / len(els_)
        if abs(cx - m.SLOT_X[mm]) > 0.01 or abs(cy - m.rest_y(m.SLOT_X[mm])) > 0.01:
            v.fail(f"section {mm + 1} lies at ({cx:.2f}, {cy:.2f}), not on its place on the rack")
        worst = max(abs(math.remainder(math.atan2(e.r[1][1], e.r[0][1]) - (math.pi / 2 - m.SLOPE_ANG), math.pi / 2)) for e in els_)
        if worst > 1e-3:
            v.fail(f"section {mm + 1} does not lie flat on the skids ({math.degrees(worst):.2f} degrees off)")
        low = min(min(p[1] - m.skid_top(p[0]) for p in e.corners()) for e in els_)
        if abs(low) > 0.02:
            v.fail(f"section {mm + 1} stands {low:.3f} off the skids")
    step = m.SLOT_DX * math.cos(m.SLOPE_ANG)
    gaps = [math.hypot(m.SLOT_X[mm] - m.SLOT_X[mm + 1], m.rest_y(m.SLOT_X[mm]) - m.rest_y(m.SLOT_X[mm + 1])) - 2 * m.PIPE_R for mm in range(m.SLUGS - 1)]
    lip = m.LIP_X[0] - (m.SLOT_X[0] + m.SPREAD)
    print(f"rack: three sections flat on the skids, {lip:.2f} from the lip and {min(gaps):.2f} apart along the slope (steps {step:.2f} in x)")


def check_weight(v):
    """The weight rises as the barrel winds its rope, and the rope reaches from the barrel to the weight's
    cap at every pose, without a gap (the rest of it taken up inside the weight)."""
    m = v.m
    for w in (0.0, 0.1, 0.25, 0.44, 0.6, 0.8, 0.92):
        pose = m.pose_at(1, w)
        lift = v.mat("weight", pose)[1][3] * 16
        turned = -angle_x(v, "barrel", pose)
        if abs(math.remainder(turned * m.BARREL_R - lift, 2 * math.pi * m.BARREL_R)) > 1e-4:
            v.fail(f"the weight rises {lift:.3f} but the barrel winds {turned * m.BARREL_R:.3f}")
        cap = m.WEIGHT_Y0[1] + 0.25 + lift
        spans = []
        for pid in [f"rope{i + 1}" for i in range(m.ROPE_SEGS)] + ["ropetop"]:
            for e in v.posed(pid, pose):
                lo, hi = e.aabb()
                if hi[1] > cap + 1e-6:
                    spans.append((max(lo[1], cap), hi[1]))
                if lo[1] < cap - 1e-6 and lo[1] < m.WEIGHT_Y0[0] + lift - 1e-6:
                    v.fail(f"a piece of rope pokes out of the weight's bottom at W {w}")
        u = spans_union(spans)
        if len(u) != 1 or abs(u[0][0] - cap) > 0.03 or abs(u[0][1] - m.K_Y) > 0.01:
            v.fail(f"the rope at W {w}: {u}, want {cap:.2f}..{m.K_Y}")
    print(f"weight: lifted {m.LIFT:.2f} a stroke by the barrel ({m.BARREL_R} round), its rope whole from the barrel to its cap at every pose")


def check_controls(v):
    """The start lever's finger on the jaw's knuckle; the lug on the collar exactly when the rod starts out;
    the rod's stop on its guide at rest; the crank's fork moving the cone; the jaws closed on the point
    while drawing and open otherwise."""
    m = v.m
    rest = m.REST
    f = v.named("startlever", r"_finger", rest)[0]
    kn = v.named("jaw", r"_knuckle", rest)[0]
    gap0 = kn.aabb()[0][2] - f.aabb()[1][2]
    worst = 0.0
    for t in (0.0, 0.01, 0.02, 0.03, 0.04):
        pose = m.pose_at(1, t)
        fp = v.point("startlever", pose, [10.65, m.LEVER_Y + m.LEVER_FINGER_R, m.LEVER_Z + 0.2])
        kp = v.point("jaw", pose, [10.65, m.LEVER_Y + m.LEVER_FINGER_R, m.LEVER_Z + 0.2])
        worst = max(worst, abs(fp[2] - kp[2]))
    print(f"start lever: its finger meets the jaw's knuckle at rest ({gap0:.3f}) and pushes it shut (within {worst:.3f})")
    if abs(gap0) > 0.01 or worst > 0.06:
        v.fail("the start lever's finger does not drive the jaw's knuckle")
    # the knock-off
    lug = lambda pose: max(e.aabb()[1][2] for e in v.named("dog", r"_lugeye", pose))   # noqa: E731
    col = lambda pose: min(e.aabb()[0][2] for e in v.named("clutchrod", r"_collar", pose))   # noqa: E731
    t0 = m.pose_at(1, m.T_KNOCK[0])
    worst = abs(lug(t0) - col(t0))
    for t in (m.T_KNOCK[0] + 0.01, m.T_KNOCK[0] + 0.03, m.T_KNOCK[1]):
        pose = m.pose_at(1, t)
        worst = max(worst, abs(lug(pose) - col(pose)))
    early = col(m.pose_at(1, m.T_KNOCK[0] - 0.02)) - lug(m.pose_at(1, m.T_KNOCK[0] - 0.02))
    print(f"knock-off: the lug meets the collar as the rod starts out and carries it out (within {worst:.3f}); {early:.2f} short just before")
    if worst > 0.01 or early < 0.3:
        v.fail("the dog's lug does not knock the clutch out")
    stop = v.named("clutchrod", r"_stop", rest)[0].aabb()[1][2]
    guide = min(e.aabb()[0][2] for e in v.named("frame", r"fr_rod_guide\d"))
    if abs(stop - guide) > 0.01:
        v.fail(f"the rod's stop ({stop:.2f}) is not on its guide ({guide:.2f}) at rest")
    # the fork and the cone
    worst = 0.0
    for t in (0.0, 0.02, 0.04, 0.2, 0.41, 0.43, 0.44):
        pose = m.pose_at(1, t)
        fork = v.point("crank", pose, [m.CRANK[0], m.RECT[0] - m.GROOVE_R, m.RECT[1]])[0]
        cone = v.mat("cone", pose)[0][3] * 16 + m.CRANK[0]
        worst = max(worst, abs(fork - cone))
    print(f"clutch: the crank's fork carries the cone {m.CONE_THROW} in and out (within {worst:.3f})")
    if worst > 0.06:
        v.fail("the crank's fork does not follow the cone's groove")
    # the jaws: shut on the point while drawing, open before and after
    for t, want in ((0.0, "open"), (m.T_START[1], "shut"), ((m.T_START[1] + m.T_TUBE) / 2, "shut"), (m.T_TUBE - 0.005, "shut"),
                    (m.T_OPEN[1], "open"), (sum(m.T_RETURN) / 2, "open"), (0.95, "open")):
        pose = m.pose_at(1, t)
        face = min(e.aabb()[0][0] for e in v.named("jaw", r"_face", pose))
        gap = face - (m.DL[0] + m.PIPE_R)
        if (want == "shut" and abs(gap) > 0.02) or (want == "open" and gap < 0.05):
            v.fail(f"the jaws are not {want} at t {t} (gap {gap:.3f})")
    print("jaws: open at rest, shut on the point from the start to the section's release, open after")


def check_supports(v):
    m = v.m
    frame = v.by_part["frame"]
    t45 = (math.pi / 4, math.pi / 4, 0.0, 0, 0.0)
    shafts = [
        ("entry shaft", "entry", r"_shaft", 0, m.ENTRY, r"^fr_cheek_[we]$", 2),
        ("rectified shaft", "rectshaft", r"_rod", 0, m.RECT, r"^(fr_rect\d_bearing|fr_cheek_[we])$", 2),
        ("drive shaft", "driveshaft", r"_rod", 0, (m.SPR_Y, m.D_Z), r"^(fr_drive\d_bearing|fr_cheek_w)$", 2),
        ("return shaft", "returnshaft", r"_rod", 0, (m.SPR_Y, m.N_Z), r"^fr_(beam_w|postn)$", 2),
        ("barrel shaft", "barrel", r"_rod", 0, (m.K_Y, m.N_Z), r"^fr_barrel_bearing", 2),
        ("start lever", "startlever", r"_shaft", 0, (m.LEVER_Y, m.LEVER_Z), r"^fr_lever_bearing", 2),
        ("idler's stud", "frame", r"fr_idler_stud", 0, m.IDLER, r"^(fr_idler_arm|fr_cheek_e)$", 1),
    ]
    for label, pid, rx, axis, c, brx, need in shafts:
        els_ = v.named(pid, rx)
        found, _ = shaft_supports(frame, els_, axis, c)
        names = {f.name for f in frame if re.search(brx, f.name)}
        named = sorted(set(found) & names)
        print(f"support {label}: {len(named)} bearing(s) {', '.join(named) or 'NONE'}")
        if len(named) < need:
            v.fail(f"the {label} is not carried by {need} bearing(s)")
    # rods in eyes: each eye's four bars round the rod's axis, along its length, at rest and moved
    for label, pid, rx, axis, c, eyes, poses in (
            ("clutch rod", "clutchrod", r"_bar", 2, (m.ROD_X, m.ROD_Y), (r"^fr_rod_guide\d$",), (m.REST, m.pose_at(1, 0.2))),
            ("selector rod (lead)", "selector", r"_rod", 0, (m.SEL_Y, m.RECT[1]), (r"^fr_selector_guide1_\d$",), (m.REST,)),
            ("selector rod (copper)", "selector", r"_rod", 0, (m.SEL_Y, m.RECT[1]), (r"^fr_selector_guide1_\d$", r"^fr_selector_guide2_\d$"),
             (m.pose_at(2, 0.0),))):
        n = 0
        for erx in eyes:
            bars = [f for f in frame if re.search(erx, f.name)]
            lo, hi = aabb_of(bars)
            u, w = [q for q in range(3) if q != axis]
            ok = len(bars) == 4 and lo[u] < c[0] < hi[u] and lo[w] < c[1] < hi[w]
            for pose in poses:
                slo, shi = aabb_of(v.named(pid, rx, pose))
                ok = ok and shi[axis] > hi[axis] and slo[axis] < lo[axis]
            n += ok
        print(f"support {label}: through {n} eye(s) at rest and moved")
        if n < len(eyes):
            v.fail(f"the {label} is not carried by its eyes")
    # the selector's fork is its pilot: in the cluster's groove, set for either metal
    for k in (1, 2):
        pose = m.pose_at(k, 0.0)
        fk = v.named("selector", r"_fork", pose)[0].aabb()
        gr = aabb_of(v.named("cluster", r"_groove", pose))
        if not (gr[0][0] - 1e-6 <= fk[0][0] and fk[1][0] <= gr[1][0] + 1e-6):
            v.fail(f"the selector's fork is not in the cluster's groove for metal {k}")
    print("support: the selector's fork rides in the cluster's groove (its pilot) for either metal")
    # the crank turns on a pin in its post, the rod's north end on the start lever's arm
    print("support: the crank on its post's pin; the clutch rod's north end pinned to the start lever's arm, its south end in its guide")


def check_oiler(v):
    m = v.m
    (x0, x1), (y0, y1), (z0, z1) = m.OILER["x"], m.OILER["y"], m.OILER["z"]
    for oil in (0.0, 0.5, 1.0):
        lvl = v.named("oillevel", r".", (0.0, 0.0, 0.0, 0, 0.0, oil))[0]
        lo, hi = lvl.aabb()
        want = m.OIL_EMPTY + (m.OIL_FULL - m.OIL_EMPTY) * oil
        print(f"oiler: oil {oil}: the level is {hi[1] - lo[1]:.3f} tall (want {want:.3f}), y {lo[1]:.2f}..{hi[1]:.2f} in a cup {y0:.2f}..{y1:.2f}")
        if abs((hi[1] - lo[1]) - want) > 1e-6 or abs(lo[1] - y0) > 1e-6 or hi[1] > y1 - 0.1:
            v.fail(f"the oil level is wrong at oil {oil}")
        if lo[0] < x0 or hi[0] > x1 or lo[2] < z0 or hi[2] > z1:
            v.fail("the oil is outside its glass")
    tip = v.named("frame", r"fr_oiler_tip")[0].aabb()
    drip = [c * 16 for c in v.rig["drip"]["pos"]]
    slug_top = m.DL[1] + m.SLUG_R
    print(f"oiler: its spout drips {tip[0][1] - slug_top:.2f} over the slug, {m.Z_DIE_BACK - drip[2]:.2f} behind the die stock; the drip anchor at "
          f"{[round(c, 2) for c in drip]}")
    if abs(drip[1] - tip[0][1]) > 0.01 or not (tip[0][2] <= drip[2] <= tip[1][2]) or not (0.1 < tip[0][1] - slug_top < 1.0):
        v.fail("the drip anchor is not at the spout's mouth over the slug")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_floating(v)
    poses = [m.REST] + [m.pose_at(k, mm + t) for k in (1, 2) for mm in range(m.SLUGS) for t in (0.0, 0.2, 0.39, 0.44, 0.47, 0.52, 0.7)]
    check_containment(v, poses)
    check_anchors(v)
    check_textures(v)
    check_gearing(v)
    check_chain(v)
    check_work(v)
    check_weight(v)
    check_controls(v)
    check_supports(v)
    check_oiler(v)
    check_clearances(v, clearance_poses(m))
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
    if gaps or hollow:
        ok = False
    return ok
