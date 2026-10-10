"""The grinder generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its
rule. Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in
make_shape.py proves the shipped files are this model moved.

The model holds four states that are never drawn together: the frame alone, and the frame with tier 2, 3 or 4
fitted. Every check that compares parts with each other does it a state at a time (`state`).
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, obb_obb, supports
from machinegen.geometry import aabb_of
from machinegen.rigmath import part_of, posed

TOL = 0.02


class V:
    def __init__(self, m, els, parts, rig):
        self.m, self.els, self.parts, self.rig = m, els, parts, rig
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        self.spec = {p["id"]: p for p in parts}
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
            if len(self.cache) > 20000:
                self.cache.clear()
            mm = self.mat(pid, pose)
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def named(self, pid, rx, pose=0.0):
        r = re.compile(rx)
        return [e for e in self.posed(pid, pose) if r.search(e.name)]

    def state(self, k):
        """The parts drawn in state k (0: the frame alone; 2, 3, 4: the frame and that tier)."""
        return [pid for pid in self.by_part if self.m.tier_of(pid) in (0, k)]

    def moving(self, pid):
        p = self.spec[pid]
        return bool(p["drivers"]) or bool(p.get("ride"))


def cycle(m, n):
    """n poses over the rig's whole cycle (CYCLE_TURNS turns of the axle), and one turn backwards."""
    span = m.CYCLE_TURNS * 2 * math.pi
    return [span * i / n for i in range(n)] + [-0.7, -2.9]


# ---------------------------------------------------------------- parts, containment
def check_basic(v):
    worst = euler_round_trip(v.els)
    print(f"euler round trip: worst {worst:.1e}")
    if worst > 1e-9:
        v.fail("an element's rotation does not survive being written as Euler angles")
    names = [el.name for el in v.els]
    if len(names) != len(set(names)):
        dup = sorted({n for n in names if names.count(n) > 1})
        v.fail(f"duplicate element names: {dup[:6]}")
    wrong = [(el.name, el.part, part_of(v.parts, el.name)) for el in v.els if part_of(v.parts, el.name) != el.part]
    if wrong:
        v.fail(f"{len(wrong)} elements land in the wrong part, e.g. {wrong[:4]}")
    empty = [p["id"] for p in v.parts if not v.by_part.get(p["id"])]
    if empty:
        v.fail(f"parts with no elements: {empty}")
    counts = {k: sum(len(v.by_part.get(pid, [])) for pid in v.state(k) if v.m.tier_of(pid) == k) for k in (0, 2, 3, 4)}
    print(f"parts: {len(v.parts)}, elements {len(v.els)}: frame and shaft {counts[0]}, tier 2 {counts[2]}, tier 3 {counts[3]}, "
          f"tier 4 {counts[4]}")


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
        v.fail(f"{where[0]} leaves the machine box at theta {where[1]:.2f}")


# ---------------------------------------------------------------- the build: tiers, stages, nothing floating
def joined(a, b, la, lb, aligned_a, aligned_b):
    """Whether two elements hold each other: machinegen's frame_floating rule (a shared face between two
    axis-aligned boxes, else an overlap)."""
    ov = [min(la[1][k], lb[1][k]) - max(la[0][k], lb[0][k]) for k in range(3)]
    if min(ov) < -0.02:
        return False
    if aligned_a and aligned_b:
        return sum(o > 0.05 for o in ov) >= 2
    return obb_obb(a, b, eps=-0.03)


def stage_floating(groups, ground=0.01):
    """Each stage's elements (`groups`: [(stage, elements)], in build order) joined to the ground through what is
    already there and itself: {stage: the names of its elements that float}. (The eidolon gantry's rule.)"""
    placed, boxes, aligned, seen, out = [], [], [], [], {}
    for stage, els in groups:
        new = list(range(len(placed), len(placed) + len(els)))
        for el in els:
            placed.append(el)
            boxes.append(el.aabb())
            aligned.append(all(el.local_axis_for(k) is not None for k in range(3)))
            seen.append(False)
        todo = []
        for j in new:
            if boxes[j][0][1] <= ground or any(joined(placed[i], placed[j], boxes[i], boxes[j], aligned[i], aligned[j])
                                               for i in range(new[0]) if seen[i]):
                seen[j] = True
                todo.append(j)
        while todo:
            i = todo.pop()
            for j in new:
                if not seen[j] and joined(placed[i], placed[j], boxes[i], boxes[j], aligned[i], aligned[j]):
                    seen[j] = True
                    todo.append(j)
        out[stage] = sorted(placed[j].name for j in new if not seen[j])
    return out


def check_build(v):
    """The tiers: every part but the frame's (the frame, the power shaft) belongs to one tier's stage, each stage's
    parts are that tier's and need its requires; no requires value is in two tiers; and every step can be built,
    nothing floating: at rest the frame, then each of a tier's stages in turn, joined to the ground through what is
    already there (the tier's earlier stages and the frame, never another tier's)."""
    m = v.m
    staged = m.stage_of_part()
    reqs = [req for _, _, stages in m.TIERS for req, _ in stages]
    if len(reqs) != len(set(reqs)):
        v.fail(f"a requires value is in two stages: {reqs}")
    for p in v.parts:
        pid, want = p["id"], staged.get(p["id"])
        if m.tier_of(pid) == 0:
            if p["requires"] is not None:
                v.fail(f"the frame's {pid} needs {p['requires']}")
        elif want is None or p["requires"] != want or not want.startswith(f"t{m.tier_of(pid)}"):
            v.fail(f"{pid} needs {p['requires']}, its stage is {want}")
    base = [el for pid in v.state(0) for el in v.posed(pid, 0.0)]
    floating = stage_floating([("frame", base)])
    if floating["frame"]:
        v.fail(f"frame elements float: {floating['frame'][:8]}")
    for tier, name, stages in m.TIERS:
        groups = [("frame", base)] + [(req, [el for pid in pids for el in v.posed(pid, 0.0)]) for req, pids in stages]
        loose = {k: n for k, n in stage_floating(groups).items() if n}
        print(f"build, tier {tier} ({name}): frame ({len(base)}) > " + " > ".join(f"{req} ({len(els)})" for req, els in groups[1:])
              + f"; floating: {loose or 'nothing'}")
        if loose:
            v.fail(f"tier {tier}'s build floats: {loose}")


# ---------------------------------------------------------------- textures
ROLES = [
    (r"^fr_deck|^fr_hopper_(n|s|w|e\d|bottom|floor)$|^fr_box_(n|s|w|e\d|floor|floorpost)$", {"planks"}),
    (r"^fr_hopper_post", {"oak"}),
    (r"^fr_hopper_strap|^fr_bearing|^fr_box_lip", {"iron"}),
    (r"^sh_axle", {"axle"}),
    (r"^sh_(rod|collar)", {"steel"}),
    (r"^t2basin_(pier|curb)", {"drystone"}),
    (r"^t2basin_(plat|drain|spout)", {"planks"}),
    (r"^t2basin_pave", {"cobble"}),
    (r"^t2basin_centre", {"granite"}),
    (r"^t2post_post", {"oak"}),
    (r"^t2post_gudgeon|^t2pinion_|^t3pinion_|^t3shaft_(shaft|axle)|^t4mitre_|^t4counter_|^t4balls_|^t3pan_die|^t3scrapers_blade",
     {"steel"}),
    (r"^t2post_|^t2foot_|^t3foot_|^t3shaft_|^t3pan_|^t3scrapers_|^t4bed_|^t4cbear|^t4drum_(?!shell)|^t2arms_staple|^t2stones_eye"
     r"|^t3runner\d_(tyre|hub)|^t4feed_(spout|launder)", {"iron"}),
    (r"^t2arms_arm", {"oak"}),
    (r"^t2stones_stone", {"granite"}),
    (r"^t2stones_chain", {"chain"}),
    (r"^t3runner\d_stone", {"polished"}),
    (r"^t4drum_shell", {"riveted"}),
    (r"^t4feed_(box|funnel)(?!_leg)", {"planks"}),
    (r"^t4feed_(box|funnel)_leg", {"oak"}),
]
OVERRIDES = [(r"^t3pan_screen$", {"mesh"})]


def check_textures(v):
    bad = []
    for el in v.els:
        tex = {f["texture"].lstrip("#") for f in el.faces.values()}
        rule = next((want for rx, want in OVERRIDES + ROLES if re.search(rx, el.name)), None)
        if rule is None or not tex <= rule:
            bad.append((el.name, sorted(tex), sorted(rule or [])))
    print(f"textures by role: {len(v.els) - len(bad)} of {len(v.els)} elements as their role says (wood for structure and the "
          f"axle's cross, iron castings and fittings, steel shafts, pinions, die, blades and balls, stone for the arrastra and "
          f"the runners, a riveted shell, a mesh screen, a chain)")
    if bad:
        v.fail(f"textures off their role: {bad[:6]}")


# ---------------------------------------------------------------- anchors, feed and discharge
def cell_face_box(m, cell, face):
    """The face's square (voxels) as (lo, hi) with zero thickness."""
    lo = [cell[k] * m.B for k in range(3)]
    hi = [(cell[k] + 1) * m.B for k in range(3)]
    k = {"west": 0, "east": 0, "down": 1, "up": 1, "north": 2, "south": 2}[face]
    if face in ("east", "up", "south"):
        lo[k] = hi[k]
    else:
        hi[k] = lo[k]
    return lo, hi, k


def check_anchors(v):
    """The anchor cells are on the footprint's outside, their faces looking out of it, and all different; the power
    face is on a side, not on the ore's ends (west, east) or its top; the axle's cross reaches the power face at its
    centre; the hopper's mouth is the infeed cell's top face, the return funnel's the return cell's, and the
    discharge box's lip reaches the output face in the output cell."""
    m, rig = v.m, v.rig
    size = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    cells = {k: tuple(rig[k]) for k in m.CELL_KEYS}
    if len(set(cells.values())) != len(cells):
        v.fail(f"two anchors share a cell: {cells}")
    out_of = {"west": (0, -1), "east": (0, 1), "down": (1, -1), "up": (1, 1), "north": (2, -1), "south": (2, 1)}
    for key, cell in cells.items():
        face = rig[key.replace("Cell", "Face")]
        k, sgn = out_of[face]
        edge = cell[k] == (0 if sgn < 0 else size[k] - 1)
        if not all(0 <= cell[q] < size[q] for q in range(3)) or not edge:
            v.fail(f"{key} {cell} {face}: not a face of the footprint's outside")
    if rig["powerFace"] in ("west", "east", "up", "down"):
        v.fail("the power face is on the ore's way in or out")
    # the axle's cross at the power face's centre
    lo, hi, k = cell_face_box(m, m.POWER_CELL, m.POWER_FACE)
    centre = [(lo[q] + hi[q]) / 2 for q in range(3)]
    axle = aabb_of(v.by_part["shaft"][:2])
    reach = axle[1][2]
    mid = [(axle[0][q] + axle[1][q]) / 2 for q in range(3)]
    off = max(abs(mid[0] - centre[0]), abs(mid[1] - centre[1]))
    print(f"anchors: the axle's cross reaches z {reach:.2f} (the power face at {lo[2]:.0f}), {off:.3f} off its centre; cells {cells}")
    if abs(reach - lo[2]) > 0.02 or off > 1e-6:
        v.fail("the axle's cross does not meet the power face at its centre")
    # the hopper's mouth over the infeed cell's top face, the funnel's over the return cell's
    for key, pid, rx in (("infeedCell", "frame", r"^fr_hopper_(n|s|w|e\d)$"), ("returnCell", "t4feed", r"^t4feed_funnel_(n|s|w|e)$")):
        walls = v.named(pid, rx)
        wlo, whi = aabb_of(walls)
        flo, fhi, _ = cell_face_box(m, cells[key], "up")
        inside = all(flo[q] - 1e-6 <= wlo[q] and whi[q] <= fhi[q] + 1e-6 for q in (0, 2))
        print(f"  {key}: its mouth x {wlo[0]:.1f}..{whi[0]:.1f}, z {wlo[2]:.1f}..{whi[2]:.1f}, top {whi[1]:.2f} (the cell's top {fhi[1]:.0f})")
        if not inside or abs(whi[1] - fhi[1]) > 1e-6:
            v.fail(f"{key}'s mouth is not the cell's top face")
    lip = v.named("frame", r"^fr_box_lip$")[0].aabb()
    flo, fhi, _ = cell_face_box(m, m.OUTPUT_CELL, m.OUTPUT_FACE)
    if abs(lip[1][0] - flo[0]) > 1e-6 or not (flo[1] <= lip[0][1] and lip[1][1] <= fhi[1] and flo[2] <= lip[0][2] and lip[1][2] <= fhi[2]):
        v.fail("the discharge box's lip does not reach the output face in the output cell")
    out = [p * m.B for p in rig["output"]["pos"]]
    if abs(out[0] - lip[1][0]) > 1e-6 or not (lip[0][2] <= out[2] <= lip[1][2]):
        v.fail("output.pos is not the lip's end")


def inside_box_mouth(v, pt):
    """Whether a point is over the discharge box's inside (between its walls)."""
    m = v.m
    (x0, x1), (z0, z1), w = m.BOX_X, m.BOX_Z, m.BOX_WALL
    return x0 + w <= pt[0] <= x1 - w and z0 + w <= pt[2] <= z1 - w and pt[1] > m.BOX_TOP


def check_flow(v):
    """The ore's way at every tier, through the same cells: tiers 2 and 3's feed spouts start at the hopper's outlet
    (touching its east wall round it) and end over the floor inside the curb or the pan's wall; tier 4's feed box
    takes the hopper's outlet through its west wall, the return funnel's launder ends over it, and its spout ends
    inside the west trunnion's bore. Each tier's discharge falls into the discharge box: the arrastra's drain and
    the Chilean mill's launder lip end over its inside, and the ball mill's lip is over it."""
    m = v.m
    out_x = m.HOP_X[1]
    for pid in ("t2basin", "t3pan"):
        sp = aabb_of(v.named(pid, r"_spout_"))
        end_r = m.CX - m.SPOUT_END
        print(f"flow: {pid}'s spout from x {sp[0][0]:.2f} (the outlet's face {out_x}) to {sp[1][0]:.2f}, {end_r:.1f} from the centre "
              f"(inside the rim's {m.RIM_R[0] if pid == 't2basin' else m.RIM_R[1] - 1.0})")
        if abs(sp[0][0] - out_x) > 0.25 or end_r >= (m.RIM_R[0] if pid == "t2basin" else m.RIM_R[1] - 1.0):
            v.fail(f"{pid}'s spout does not run from the outlet to inside the rim")
    for pid, rx in (("t2basin", r"_drain_floor"), ("t3pan", r"_launder_lip")):
        e = v.named(pid, rx)[0]
        lo, hi = e.aabb()
        tip = [hi[0], lo[1], (lo[2] + hi[2]) / 2]
        print(f"  {pid}'s discharge ends at x {tip[0]:.2f}, y {tip[1]:.2f}: over the discharge box's inside {inside_box_mouth(v, tip)}")
        if not inside_box_mouth(v, tip):
            v.fail(f"{pid}'s discharge does not fall into the discharge box")
    lip = aabb_of(v.named("t4drum", r"_lip"))
    tip = [lip[1][0], lip[0][1], m.CZ]
    print(f"  t4drum's discharge lip ends at x {tip[0]:.2f}, its bottom y {tip[1]:.2f}: over the box's inside {inside_box_mouth(v, tip)}")
    if not inside_box_mouth(v, tip):
        v.fail("the drum's discharge does not fall into the discharge box")
    fb = aabb_of(v.named("t4feed", r"_box_"))
    if abs(fb[0][0] - out_x) > 1e-6 or not (fb[0][2] < m.OUTLET_Z[0] and m.OUTLET_Z[1] < fb[1][2]):
        v.fail("the feed box does not take the hopper's outlet")
    ld = v.named("t4feed", r"_launder_floor")[0]
    lend = max(ld.corners(), key=lambda p: -p[2])
    if not (fb[0][2] < lend[2] < fb[1][2] and lend[1] < fb[1][1] + 1.5):
        v.fail(f"the return launder does not end over the feed box ({lend})")
    sp = v.named("t4feed", r"_spout_")
    end = max(max(e.corners(), key=lambda p: p[0])[0] for e in sp)
    worst = max(math.hypot(p[1] - m.DRUM_Y, p[2] - m.CZ) for e in sp for p in e.corners() if p[0] > m.W_TRUNNION_X[0])
    print(f"  t4feed's spout ends at x {end:.2f} (the west trunnion {m.W_TRUNNION_X[0]}..{m.W_TRUNNION_X[1]}), at most "
          f"{worst:.2f} from the axis in it (the bore {m.TRUNNION_R[0]})")
    if not (m.W_TRUNNION_X[0] < end <= m.W_TRUNNION_X[1]) or worst > m.TRUNNION_R[0] - 0.15:
        v.fail("tier 4's feed spout is not in the west trunnion's bore")


# ---------------------------------------------------------------- gearing and rolling
def velocity(v, pid, p, pose=0.0, h=1e-4):
    """The velocity (voxels per radian of theta) of the point of part pid that is at p (at pose)."""
    from machinegen.rigmath import apply
    a = v.mat(pid, pose + h)
    b = v.mat(pid, pose - h)
    m0 = v.mat(pid, pose)
    # the part's material point now at p: p moved back by the pose's own matrix (rigid: the inverse is the transpose)
    r = [row[:3] for row in m0[:3]]
    t = [m0[i][3] * 16 for i in range(3)]
    q = [sum(r[j][i] * (p[j] - t[j]) for j in range(3)) for i in range(3)]
    pa = apply(a, [x / 16 for x in q])
    pb = apply(b, [x / 16 for x in q])
    return [(pa[i] - pb[i]) * 16 / (2 * h) for i in range(3)]


def teeth(v, pid, rx, pose):
    return v.named(pid, rx, pose)


def mesh_contact(v, a, b, poses, eps_in=0.12, eps_near=-0.3):
    """Over `poses`: the worst pair of a's and b's elements that run into each other by more than about 2 eps_in
    (none, or a name pair), and whether at every pose some pair is within about 2 |eps_near| (the mesh engaged)."""
    (pa, ra), (pb, rb) = a, b
    clash, engaged = None, True
    for pose in poses:
        ea, eb = teeth(v, pa, ra, pose), teeth(v, pb, rb, pose)
        near = False
        for x in ea:
            xl, xh = x.aabb()
            for y in eb:
                yl, yh = y.aabb()
                if all(xl[k] < yh[k] + 0.7 and yl[k] < xh[k] + 0.7 for k in range(3)):
                    if obb_obb(x, y, eps=eps_in) and clash is None:
                        clash = (x.name, y.name, round(pose, 3))
                    if not near and obb_obb(x, y, eps=eps_near):
                        near = True
        engaged = engaged and near
    return clash, engaged


def omega(v, pid, h=0.01):
    """How fast part pid turns, radians per radian of the axle."""
    r = v.mat(pid, h)
    c = (r[0][0] + r[1][1] + r[2][2] - 1) / 2
    return math.acos(max(-1.0, min(1.0, c))) / h


def meshes(m):
    t2, t3 = m.T2_BEVEL, m.T3_BEVEL
    return [
        ("tier 2's bevels", ("t2pinion", r"_bevel_"), ("t2post", r"_bevel_"), m.bevel_pitch_point(t2), t2["pinion"], t2["wheel"]),
        ("tier 3's bevels", ("t3pinion", r"_bevel_"), ("t3shaft", r"_bevel_"), m.bevel_pitch_point(t3), t3["pinion"], t3["wheel"]),
        ("tier 4's mitres", ("t4mitre", r"_bevel_"), ("t4counter", r"_bevel_"), [m.CX + m.MITRE_R, m.SHAFT_Y, m.CZ + m.MITRE_R],
         m.MITRE["teeth"], m.MITRE["teeth"]),
        ("tier 4's girth gear", ("t4counter", r"_pinion_"), ("t4drum", r"_girth_tooth|_girthrim"),
         [(m.GIRTH_X[0] + m.GIRTH_X[1]) / 2, m.SHAFT_Y + m.PINION_R, m.CZ], m.PINION["teeth"], m.GIRTH["teeth"]),
    ]


def check_gearing(v):
    """Every mesh: the two pitch points move together (the rig's ratios are the teeth's, with the right signs), the
    ratio is the tooth counts', and over a whole turn of the faster wheel, every 2.5 degrees, no tooth runs into the
    other wheel by more than about 0.25 voxels while some tooth is always within 0.6 of it (engaged). Each pinion
    or wheel is keyed: its part turns with the shaft it is on."""
    m = v.m
    for label, (pa, ra), (pb, rb), pp, na, nb in meshes(m):
        va, vb = velocity(v, pa, pp), velocity(v, pb, pp)
        diff = max(abs(va[i] - vb[i]) for i in range(3))
        speed = math.sqrt(sum(x * x for x in va))
        wa, wb = omega(v, pa), omega(v, pb)
        span = 2 * math.pi / wa                              # a whole turn of a, the faster wheel
        poses = [span * i / 144 for i in range(144)]
        clash, engaged = mesh_contact(v, (pa, ra), (pb, rb), poses)
        print(f"gearing, {label}: pitch points move together within {diff:.2e} ({speed:.3f} voxels a radian of the axle), "
              f"{na}:{nb} teeth, turning {wa:.4f} and {wb:.4f} a radian of the axle (ratio {wb / wa:.4f}, teeth {na / nb:.4f}); "
              f"a turn of the first every 2.5 degrees: no tooth into the other wheel {clash is None}"
              f"{'' if clash is None else f' {clash}'}, always engaged {engaged}")
        if diff > 1e-3 or speed < 1e-3:
            v.fail(f"{label}: the pitch points do not move together")
        if abs(wb / wa - na / nb) > 1e-3:
            v.fail(f"{label}: the ratio is not the tooth counts'")
        if clash is not None or not engaged:
            v.fail(f"{label}: the teeth do not run together")


def check_rolling(v):
    """The edge runners roll on the die without slipping at their track (mid-face): the point of each tyre at the
    bottom stands still, and the tyre's lowest flat is on the die's top at rest."""
    m = v.m
    worst = 0.0
    for i, sgn in ((1, 1.0), (2, -1.0)):
        p = [m.CX + sgn * m.RUNNER_TRACK, m.FLOOR_Y, m.CZ]
        vel = velocity(v, f"t3runner{i}", p)
        worst = max(worst, max(abs(x) for x in vel))
        lo = min(e.aabb()[0][1] for e in v.named(f"t3runner{i}", r"_tyre"))
        if abs(lo - m.FLOOR_Y) > 1e-6:
            v.fail(f"runner {i}'s tyre is not on the die at rest ({lo:.3f})")
    carry = velocity(v, "t3shaft", [m.CX + m.RUNNER_TRACK, m.FLOOR_Y, m.CZ])
    print(f"rolling: the runners' contact points move {worst:.2e} voxels a radian (carried round at "
          f"{math.sqrt(sum(x * x for x in carry)):.3f}); each tyre on the die at rest")
    if worst > 1e-3:
        v.fail("the edge runners slip on the die")


# ---------------------------------------------------------------- supports
def check_supports(v):
    """Every shaft carried: the power shaft in the frame's two pillow blocks; the arrastra's post on its footstep and
    in the centre stone; the Chilean mill's shaft on its footstep and in the pan's boss; the countershaft in its two
    pillow blocks; the drum's trunnions each in a bearing that closes round it on four sides; each runner between a
    collar and a nut on its axle; each tier's pinion on the power shaft's end."""
    m = v.m

    def held(label, shaft_els, holders, axis, c, need=2):
        found, span = supports(holders, shaft_els, axis, c)
        print(f"supports: {label} in {sorted(found)}")
        if len(found) < need:
            v.fail(f"{label} is carried by {found}")

    held("the power shaft", v.named("shaft", r"^sh_rod"), v.posed("frame", 0.0), 2, (m.CX, m.SHAFT_Y))
    held("the arrastra's post", v.named("t2post", r"_gudgeon|_post\d"), v.posed("t2foot", 0.0) + v.named("t2basin", r"_centre"),
         1, (m.CX, m.CZ))
    held("the Chilean mill's shaft", v.named("t3shaft", r"_shaft\d"), v.posed("t3foot", 0.0) + v.named("t3pan", r"_boss"), 1, (m.CX, m.CZ))
    held("the countershaft", v.named("t4counter", r"_shaft\d"), v.posed("t4cbear", 0.0), 0, (m.SHAFT_Y, m.CZ))
    th = m.TRUNNION_R[1]
    for i, (x0, x1) in enumerate(m.PEDESTALS_X, 1):
        tr = v.named("t4drum", r"_wtrunnion" if i == 1 else r"_etrunnion")
        tlo, thi = aabb_of(tr)
        walls = {k: v.named("t4bed", rf"_bearing{i}_{k}$")[0].aabb() for k in ("low", "cap", "n", "s")}
        closes = (abs(walls["low"][1][1] - (m.DRUM_Y - th)) < 1e-6 and abs(walls["cap"][0][1] - (m.DRUM_Y + th)) < 1e-6
                  and abs(walls["n"][1][2] - (m.CZ - th)) < 1e-6 and abs(walls["s"][0][2] - (m.CZ + th)) < 1e-6)
        through = tlo[0] < x0 and thi[0] > x1
        print(f"supports: the drum's {'west' if i == 1 else 'east'} trunnion through bearing {i} (x {x0}..{x1}): {through}, "
              f"closed round it at {th}: {closes}")
        if not (closes and through):
            v.fail(f"the drum's trunnion is not carried in bearing {i}")
    for i, sgn in ((1, 1.0), (2, -1.0)):
        r = aabb_of(v.posed(f"t3runner{i}", 0.0))
        tag = "e" if sgn > 0 else "w"
        col = aabb_of(v.named("t3shaft", rf"_collar{tag}"))
        nut = aabb_of(v.named("t3shaft", rf"_nut{tag}"))
        inner, outer = (col[1][0], nut[0][0]) if sgn > 0 else (nut[1][0], col[0][0])
        gaps = (r[0][0] - inner, outer - r[1][0])
        print(f"supports: runner {i} between its collar and nut, gaps {gaps[0]:+.3f} and {gaps[1]:+.3f}")
        if max(abs(g) for g in gaps) > 0.05:
            v.fail(f"runner {i} is not located between its collar and nut")
    for pid in ("t2pinion", "t3pinion", "t4mitre"):
        hub = aabb_of(v.named(pid, r"_hub"))
        if abs(hub[0][2] - m.SHAFT_END) > 1e-6:
            v.fail(f"{pid}'s hub does not start at the power shaft's end")


# ---------------------------------------------------------------- clearances
ALLOWED = [
    # shafts in their bearings and footsteps
    ("shaft", r"^sh_rod", "frame", r"^fr_bearing"),
    ("t2post", r"_gudgeon", "t2foot", None), ("t2post", r"_post\d", "t2basin", r"_centre"),
    ("t3shaft", r"_shaft\d", "t3foot", None), ("t3shaft", r"_shaft\d", "t3pan", r"_boss"),
    ("t4counter", r"_shaft\d|_hub", "t4cbear", None),
    ("t4drum", r"_[we]trunnion", "t4bed", r"_bearing"),
    # the charge lies on the shell as it turns under it
    ("t4balls", None, "t4drum", r"_shell"),
    # wheels and pinions keyed on their shafts' ends
    ("t2pinion|t3pinion|t4mitre", r"_hub", "shaft", r"^sh_rod"),
    # meshing teeth (check_gearing holds them)
    ("t2pinion", None, "t2post", r"_bevel_"), ("t3pinion", None, "t3shaft", r"_bevel_"),
    ("t4mitre", r"_bevel_", "t4counter", r"_bevel_"), ("t4counter", r"_pinion_", "t4drum", r"_girth"),
    # the runners on their axle, between collar and nut, and on the die
    ("t3runner\\d", None, "t3shaft", r"_axle|_collar|_nut"), ("t3runner\\d", r"_tyre", "t3pan", r"_die"),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose, pids):
    """Every pair of elements of two parts that move against each other (one moves and their matrices differ) that
    overlap, but for the intended contacts."""
    mats = {pid: v.mat(pid, pose) for pid in pids}

    def together(a, b):
        return max(abs(mats[a][i][j] - mats[b][i][j]) for i in range(3) for j in range(4)) < 1e-12

    items = []
    for pid in pids:
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
                if pa == pb or together(pa, pb):
                    continue
                if not all(alo[q] < bhi[q] - TOL and blo[q] < ahi[q] - TOL for q in range(3)):
                    continue
                if allowed(pa, ea.name, pb, eb.name):
                    continue
                if obb_obb(ea, eb):
                    hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def check_clearances(v, poses, label):
    total = 0
    for k in (2, 3, 4):
        pids = v.state(k)
        allhits = {}
        for pose in poses:
            for key, hs in touching_pairs(v, pose, pids).items():
                allhits.setdefault(key, {})
                for h in hs:
                    allhits[key].setdefault(h, pose)
        total += len(allhits)
        for key, hs in sorted(allhits.items()):
            ex = sorted(hs.items())[:3]
            print(f"  TOUCH (tier {k}) {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {p:.3f}" for (a, b), p in ex))
    print(f"{label} over {len(poses)} poses a tier: {'all clear' if not total else f'{total} pairs of parts touch'}")
    if total:
        v.fail("parts run into each other")


# ---------------------------------------------------------------- z-fighting
def check_zfight(v):
    m = v.m
    bad = 0
    for k in (0, 2, 3, 4):
        for pose in m.coplanar_poses():
            els = [e for pid in v.state(k) for e in v.posed(pid, pose)]
            pairs = coplanar_faces(els)
            if pairs:
                bad += len(pairs)
                print(f"  coplanar (state {k}) at theta {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses of each state")
    if bad:
        v.fail("faces z-fight")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_build(v)
    check_textures(v)
    check_anchors(v)
    check_flow(v)
    check_supports(v)
    check_rolling(v)
    check_containment(v, cycle(m, 48))
    check_gearing(v)
    check_clearances(v, cycle(m, 72), "clearances")
    if not quick:
        check_clearances(v, cycle(m, 432), "swept paths (every 5 degrees of the axle over its six-turn cycle)")
        check_zfight(v)
    return v.ok


def validate_files(m, shape, frame_shape, ship):
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL textures used but not declared: {missing}")
        ok = False
    lids = [c["pos"] for c in ship["cells"] if "lid" in c]
    hollow = [c["pos"] for c in ship["cells"] if c.get("hollow")]
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, hollow {hollow or 'none'}, "
          f"lids {lids or 'none'}")
    if len(ship["cells"]) != m.CELLS_X * m.CELLS_Y * m.CELLS_Z or lids:
        ok = False
    frame_names = {e["name"] for e in frame_shape["elements"]}
    if not frame_names or any(not n.startswith("fr_") for n in frame_names):
        print("FAIL the frame shape is not the frame part")
        ok = False
    tiers = ship["tiers"]
    reqs = {p["requires"] for p in ship["parts"]} - {None}
    if {r for t in tiers for r in t["fitted"]} != reqs or [t["tier"] for t in tiers] != [2, 3, 4]:
        print(f"FAIL the rig's tiers do not list every requires value once: {tiers}")
        ok = False
    return ok
