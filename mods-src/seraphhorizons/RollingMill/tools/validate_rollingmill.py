"""The rolling mill generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import FACE_NORMAL, box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
from machinegen.checks import sight_glass
from machinegen.checks import supports as shaft_supports
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
            self.cache[key] = [self.m.posed_el(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def point(self, pid, pose, p):
        q = _apply(self.mat(pid, pose), [v / 16 for v in p])
        return [v * 16 for v in q]

    def named(self, pid, rx, pose=None):
        src = self.posed(pid, pose) if pose is not None else self.by_part.get(pid, [])
        r = re.compile(rx)
        return [e for e in src if r.search(e.name)]


def inside(el, p, eps=1e-6):
    """Whether point p (voxels) lies inside element `el` (an oriented box)."""
    d = [p[i] - el.c[i] for i in range(3)]
    local = [sum(el.r[j][k] * d[j] for j in range(3)) for k in range(3)]
    return all(abs(local[k]) <= abs(el.size[k]) / 2 + eps for k in range(3))


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
    idle = [p["id"] for p in v.parts if p["id"] != "frame" and not (p["requires"] or p.get("ride") or p["drivers"])]
    if idle:
        v.fail(f"parts the renderer would never draw (no requires, ride or driver) and not the frame: {idle}")


def job_poses(m, n=60):
    return [m.REST] + [m.pose_at(3.0 * i / n, k=1 + i % 2) for i in range(n + 1)]


def check_containment(v, poses):
    m = v.m
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where = -1e9, None
    for pose in poses:
        for pid in v.by_part:
            if not m.on_show(pid, pose[3]):
                continue
            for el in v.posed(pid, pose):
                if el.vanished:
                    continue
                lo, hi = el.aabb()
                o = box_overhang(lo, hi, box)
                if o > worst:
                    worst, where = o, (el.name, pose)
    print(f"containment over {len(poses)} poses: worst overhang {worst:.3f} voxels ({where[0]} at W {where[1][2]:.3f})")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the machine box at {where[1]}")


def check_floating(v):
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")


TEX_RULES = [
    (r"^(fr_sleeper|fr_pinsleeper|fr_entrypost|fr_tumblepost|fr_hingepost|fr_rodpost|fr_railpost|fr_traypost|entry_shaft[ab]|table_(side|beam)|tray_rail)", "oak"),
    (r"^(fr_hs\w+_(foot|post\d|cap|boss|chock[tb]_\w+)|fr_sole|fr_pin(foot|cheek|tie)|fr_west(post|foot)|crown_|tumble_drum|tray_weight)", "castiron"),
    (r"^(broll_(?!wob)|troll_(?!wob)|bpin_gear|tpin_gear)", "roll"),
    (r"^(fr_brass|fr_pinbrass|fr_cup\w+_(base|cap|knob|pipe))", "brass"),
    (r"^fr_cup\w+_glass", "glass"),
    (r"^oillevel_", "oil"),
    (r"^i(billet_|bar\d_)", "bar"),
    (r"^s(billet_|bar\d_)", "barsteel"),
    (r"^(entry_(rod|pinion|worm)|bpin_(shaft|wob)|tpin_(shaft|wob)|[bt]sp_|[bt]roll_wob|tumble_(shaft|wheel|cam|rib)|lrod|transfer_|table_(top|wear|pin)|"
     r"tray_(plate|stop|arm|cwarm|hub|cwhub|pin)|fr_(hs\w+_(bolt|screw|head|tommy|dist)|westbrg|entrybrg|tumblebrg|foreplate|foreguide|delivery|hinge|rodguide|rodarm|rail\d|traybrg))", "iron"),
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
        else:
            bad.append((el.name, "no rule", ""))
    print(f"textures: {len(TEX_RULES)} rules (oak timber and the axle's continuation; cast-iron housings, chocks, pinion housing, crown "
          f"wheel, drum, counterweights; the rolls and pinions turned bright; brass brasses and oilers; wrought iron for shafts, "
          f"plates and links; the hot bar, iron or steel): {len(bad)} elements break them")
    if bad:
        v.fail(f"textures by role: {bad[:4]}")


def check_anchors(v):
    m = v.m
    rig = v.rig
    pc = tuple(rig["powerCell"])
    entry = v.named("entry", r"_shaft[ab]")
    lo, hi = aabb_of(entry)
    want = ((pc[0] + 0.5) * 16, (pc[1] + 0.5) * 16)
    print(f"anchors: power cell {pc} face {rig['powerFace']}, the entry shaft reaches z {hi[2]:.2f} at (x {m.ENTRY_X}, y {m.BOT_Y}): the cell's centre")
    if abs(hi[2] - (pc[2] + 1) * 16) > 0.01 or pc != m.POWER_CELL or rig["powerFace"] != "south" or (m.ENTRY_X, m.BOT_Y) != want:
        v.fail("the entry shaft does not meet the power face at the cell's centre")
    for key in m.ANCHORS:
        p = rig[key]["pos"]
        if not all(0 <= p[i] <= (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)[i] for i in range(3)):
            v.fail(f"the {key} anchor is outside the machine")
    o = rig["output"]["pos"]
    if rig["outputSide"] != "east" or o[0] < m.CELLS_X - 0.01:
        v.fail("the output anchor is not on the east face")
    drip = [c * 16 for c in rig["drip"]["pos"]]
    pipe = v.named("frame", r"fr_cupw_pipe")[0].aabb()
    if not all(pipe[0][i] - 0.1 <= drip[i] <= pipe[1][i] + 0.1 for i in range(3)):
        v.fail(f"the drip anchor {drip} is not at the west cup's pipe")
    print(f"anchors: infeed {rig['infeedSide']}, output {rig['outputSide']} at {o}, bite {rig['bite']['pos']}, drip at the west cup's pipe")


# ---------------------------------------------------------------- mechanism
def ang_x(v, pid, pose):
    mm = v.mat(pid, pose)
    return math.atan2(mm[2][1], mm[1][1])


def ang_z(v, pid, pose):
    mm = v.mat(pid, pose)
    return math.atan2(mm[1][0], mm[0][0])


def no_bite(v, a_pid, a_rx, b_pid, b_rx, poses, eps, label):
    worst = 0
    for pose in poses:
        a = v.named(a_pid, a_rx, pose)
        b = v.named(b_pid, b_rx, pose)
        for x in a:
            for y in b:
                if obb_obb(x, y, eps=eps):
                    worst += 1
    print(f"gearing: {label}: no tooth runs more than {eps} into another over {len(poses)} angles: {'yes' if not worst else 'NO'}")
    if worst:
        v.fail(f"{label}: teeth run into each other")


def check_gearing(v):
    """The drive: the crown wheel turns a third of the entry shaft (their pitch arcs equal), the pinions equal and
    opposite on centres their pitch diameter apart, the rolls and spindles with their pinions, the bottom roll's top
    running south (the bar goes through) and the top roll's crown north (it carries the bar back); the tumbling shaft
    once a pass at the drawn pace, its worm wheel's teeth the worm's starts times the turns a pass."""
    m = v.m
    d = m.TOP_Y - m.BOT_Y
    if abs(d - 2 * m.R_PINION) > 1e-6:
        v.fail(f"the pinions' centres are {d} apart, their pitch diameter {2 * m.R_PINION}")
    for th in (0.4, -1.3, 2.7):
        s1, s2 = (th, abs(th), 0.0, 0, 0.0, 0.0), (th + 0.03, abs(th) + 0.03, 0.0, 0, 0.0, 0.0)
        de = math.remainder(ang_z(v, "entry", s2) - ang_z(v, "entry", s1), math.tau)
        dc = math.remainder(ang_x(v, "crown", s2) - ang_x(v, "crown", s1), math.tau)
        if abs(de * m.R_CPIN - dc * m.R_CROWN) > 1e-6:
            v.fail(f"the crown wheel slips on its pinion at theta {th}")
        db = math.remainder(ang_x(v, "pinionbottom", s2) - ang_x(v, "pinionbottom", s1), math.tau)
        dt = math.remainder(ang_x(v, "piniontop", s2) - ang_x(v, "piniontop", s1), math.tau)
        if abs(db + dt) > 1e-9 or abs(db - dc) > 1e-9:
            v.fail(f"the pinions do not turn equal and opposite with the crown wheel at theta {th}")
        for pid, ref in (("rollbottom", "pinionbottom"), ("spindlebottom", "pinionbottom"), ("rolltop", "piniontop"), ("spindletop", "piniontop")):
            if abs(math.remainder(ang_x(v, pid, s1) - ang_x(v, ref, s1), math.tau)) > 1e-9:
                v.fail(f"{pid} does not turn with {ref}")
    if not (m.ROLL_RATIO > 0):
        v.fail("the bottom roll does not run the bar south")
    print(f"gearing: crown wheel {m.CROWN_T} on the entry shaft's pinion {m.CROWN_PIN_T} (the rolls a third of the axle), pinions {m.PINION_T} "
          f"equal and opposite on centres {d} apart; the bottom roll's top runs south, the top roll's crown north")
    angles = [i * 0.07 for i in range(40)]
    no_bite(v, "entry", r"entry_pinion_tooth", "crown", r"crown_tooth", [(a, a, 0.0, 0, 0.0, 0.0) for a in angles], 0.12, "the crown wheel and its pinion")
    no_bite(v, "pinionbottom", r"_tooth", "piniontop", r"_tooth", [(a, a, 0.0, 0, 0.0, 0.0) for a in angles], 0.12, "the pinions")
    # the tumbling shaft at the drawn pace: once a pass, the worm's starts times its turns a pass its teeth
    worst = 0.0
    for W in (0.13, 0.5, 1.37, 2.9):
        pose = m.pose_at(W)
        want = math.remainder(pose[0] * m.WORM_STARTS / m.WORM_TEETH, math.tau)
        worst = max(worst, abs(math.remainder(ang_x(v, "tumble", pose) - want, math.tau)))
    print(f"gearing: the tumbling shaft turns once a pass, {m.TURNS_PER_PASS:g} axle turns (a {m.WORM_STARTS}-start worm, {m.WORM_TEETH} teeth), "
          f"with the worm at the drawn pace (worst {worst:.1e} rad)")
    if worst > 1e-6 or m.WORM_TEETH / m.WORM_STARTS != m.TURNS_PER_PASS:
        v.fail("the tumbling shaft is not geared to the worm at the drawn pace")


# ---------------------------------------------------------------- the passes and the work
def check_passes(v):
    """Each groove is its section's: the section lies in the pass with the rolls GROOVE_CLR off it (no closer,
    sampled across it) and no further than a turned step's depth anywhere; the last section is the press brake's
    angle; the bar drawn out of each pass is that section, in that lane, its lowest points on the pass line."""
    m = v.m
    top, bot = m.barrel_profile(True), m.barrel_profile(False)

    def r_at(prof, x):
        return next(r for x0, x1, r in prof if x0 - 1e-9 <= x <= x1 + 1e-9)
    for j in range(len(m.PASSES)):
        q = m.section(j)
        xc = m.LANES[j]
        gaps_t, gaps_b = [], []
        for i in range(1, 400):
            dx = -q["w"] + 2 * q["w"] * i / 400                      # inside the section: its outer tips are where the collars start
            gaps_t.append(m.TOP_Y - r_at(top, xc + dx) - m.bar_top(j, dx))
            if abs(dx) <= q["ix"]:                                   # the legs' tip faces are free: the bottom roll follows the inside
                gaps_b.append(m.bar_bottom(j, dx) - (m.BOT_Y + r_at(bot, xc + dx)))
        step = q["w"] / m.STEPS * max(q["c"] / q["s"], 1.0) + m.GROOVE_CLR + 0.05
        print(f"pass {j + 1}: a {m.PASSES[j]['angle']:g} degree vee, legs {q['a']} x {q['t']}, in lane x {xc}: the top roll {min(gaps_t):.3f}.."
              f"{max(gaps_t):.3f} off its outside, the bottom roll {min(gaps_b):.3f}..{max(gaps_b):.3f} off its inside")
        if min(gaps_t) < m.GROOVE_CLR - 1e-6 or min(gaps_b) < m.GROOVE_CLR - 1e-6:
            v.fail(f"pass {j + 1}'s rolls cut into its section")
        if max(gaps_t) > step or max(gaps_b) > step:
            v.fail(f"pass {j + 1}'s groove does not follow its section (gap up to {max(max(gaps_t), max(gaps_b)):.3f}, a step is {step:.3f})")
        # the bar drawn out of this pass is the section, in its lane (to the z-fighting fix's insets)
        pose = m.pose_at(m.roll_window(j)[1])
        legs = v.posed(f"bar{j + 1}iron", pose)
        lo, hi = aabb_of(legs)
        ok = (abs(lo[1] - m.PASS_LOW) < 0.05 and abs(hi[1] - q["y_apex"]) < 0.05 and abs(lo[0] - (xc - q["w"])) < 0.05
              and abs(hi[0] - (xc + q["w"])) < 0.05 and abs(hi[2] - lo[2] - m.BAR_LENGTHS[j]) < 1e-3 and abs(lo[2] - m.ROLL_Z) < 1e-3)
        if not ok:
            v.fail(f"the bar out of pass {j + 1} is not its section in its lane: {lo} .. {hi}")
    last = m.PASSES[-1]
    if (last["angle"], last["leg"], last["t"]) != (90.0, 4.0, 1.0):
        v.fail("the last pass is not the press brake's angle: 90 degrees, legs 4 outside, 1 thick")
    print("passes: the last is the press brake's angle (90 degrees, legs 4 outside, 1 thick); each bar out of a pass is its section")


def bottom_points(v, pid, pose):
    """The bar's lowest line's two ends at a pose: the inner tips' corners at its two ends (the billet: its bottom
    face's), as (y, z) in the plane of travel, the end that entered the rolls (authored far end) first."""
    els = v.posed(pid, pose)
    pts = [c for e in els for c in e.corners()]
    m = v.m
    if pid.startswith("billet"):                     # the authored ends, before the z-fighting fix's insets
        ends = (m.ROLL_Z, m.ROLL_Z - m.BILLET["len"])
    else:
        ends = (m.ROLL_Z + m.BAR_LENGTHS[int(pid[3]) - 1], m.ROLL_Z)
    mm = v.mat(pid, pose)
    out = []
    for z in ends:
        y = v.m.PASS_LOW
        x = v.m.LANES[0] if pid.startswith("billet") else v.m.LANES[int(pid[3]) - 1]
        p = _apply(mm, [x / 16, y / 16, z / 16])
        out.append((p[1] * 16, p[2] * 16))
    return out, pts


def line_dist(p, a, d):
    """Signed distance of (y, z) p from the line through a along unit d, + on the side d turned +90 degrees."""
    return (p[0] - a[0]) * d[1] - (p[1] - a[1]) * d[0]


def check_work_path(v):
    """The bar's way through the cycle, posed by the rig: it grows out of the line of centres as it is rolled, lies on
    the table from its run-out until it is pulled, is moved one lane by the fingers (touching the bar as they push),
    rides the top roll's ride circle over the crown, lies on the fore tray to its stop, swings down with the tray and
    slides to the bite, where its entering end stays while the next pass takes it; the billet the same from the tray;
    the angle ends pushed off to the delivery place."""
    m = v.m
    g = m.geo()
    tc = (m.TOP_Y, m.ROLL_Z)
    worst = {}

    def note(key, val):
        worst[key] = max(worst.get(key, 0.0), abs(val))
    for k, metal in ((1, "iron"), (2, "steel")):
        work_path_of(v, m, g, tc, k, metal, note)
    tol = {"grow": 0.05, "table": 1e-4, "finger": 0.05, "shift": 1e-6, "deliver": 1e-6, "ride": 1e-4, "tray": 1e-4, "bite": 1e-4, "stop": 1e-4}
    print("work: " + ", ".join(f"{k} {val:.1e}" for k, val in sorted(worst.items())) + " (the worst miss of each rule, voxels, either metal; grow and finger measure faces, which the z-fighting fix may inset)")
    for k, val in worst.items():
        if val > tol[k]:
            v.fail(f"the bar's way: {k} is off by {val:.4f}")
    print(f"work: the bar rides the top roll at {g['R']:.3f} from its axis (the roll's most, under either returned bar), the table tilts "
          f"{math.degrees(g['alpha']):.2f} degrees, the bar goes {math.degrees(-g['dphi']):.1f} degrees over the crown, the tray swings "
          f"{math.degrees(g['delta']):.1f} degrees about ({g['C'][0]:.2f}, {g['C'][1]:.2f})")


def work_path_of(v, m, g, tc, k, metal, note):
    def pose_at(W):
        return m.pose_at(W, k=k)
    for j in range(len(m.PASSES)):
        pid = f"bar{j + 1}{metal}"
        cw = m.cycle_windows(j)
        # rolling: the far end on the line of centres... the near end at it, the length growing with W
        for f in (0.0, 0.5, 1.0):
            W = cw["roll"][0] + f * (cw["roll"][1] - cw["roll"][0])
            els = [e for e in v.posed(pid, pose_at(W)) if not e.vanished]
            if els:
                lo, hi = aabb_of(els)
                note("grow", lo[2] - m.ROLL_Z)
                note("grow", (hi[2] - lo[2]) - f * m.BAR_LENGTHS[j])
        # on the table: from the run-out's end until the pull starts, both ends on the table's top line
        last = j == len(m.PASSES) - 1
        for W in ((cw["runout"][1], m.HANDOUT - 0.01) if last else
                  (cw["runout"][1], (cw["runout"][1] + cw["pull"][0]) / 2, cw["pull"][0], (cw["pull"][0] + cw["pull"][1]) / 2)):
            pose = pose_at(W)
            (e1, e2), _ = bottom_points(v, pid, pose)
            tb = v.named("table", r"table_top1", pose)[0]
            a = (tb.c[1] + mvec(tb.r, [0, abs(tb.size[1]) / 2, 0])[1], tb.c[2] + mvec(tb.r, [0, abs(tb.size[1]) / 2, 0])[2])
            dirv = mvec(tb.r, [0, 0, 1])
            d = (dirv[1], dirv[2])
            if last or W < cw["pull"][0] + 1e-9:
                note("table", line_dist(e1, a, d))
                note("table", line_dist(e2, a, d))
            else:
                note("table", line_dist(e1, a, d))           # its far end still on the table while the near end goes up to the roll
        # the shift: the bar's x moves a lane; each finger meets its bar's west tip as it pushes
        mid = j + m.SHIFT[0] + m.SHIFT[2] * 0.6
        pose = pose_at(mid)
        bar = aabb_of([e for e in v.posed(pid, pose)])
        fing = aabb_of(v.named("transfer", rf"transfer_finger1{j + 1}$", pose))
        note("finger", bar[0][0] - fing[1][0])
        after = aabb_of(v.posed(pid, pose_at(j + 0.65)))
        note("shift", (after[0][0] + after[1][0]) / 2 - (m.LANES[j] + m.LANE_PITCH))
        if last:
            end = aabb_of(v.posed(pid, pose_at(m.HANDOUT)))
            note("deliver", (end[0][0] + end[1][0]) / 2 - m.DELIVER_X)
            gone = v.posed(pid, pose_at(m.HANDOUT + m.HANDOUT_EASE))
            if not all(e.vanished for e in gone):
                v.fail("the finished angle is still drawn after it is handed out")
            continue
        # over the top: the lowest line R from the top roll's axis, the roll under it
        for f in (0.0, 0.3, 0.7, 1.0):
            W = cw["over"][0] + f * (cw["over"][1] - cw["over"][0])
            (e1, e2), _ = bottom_points(v, pid, pose_at(W))
            d = (e2[0] - e1[0], e2[1] - e1[1])
            n = math.hypot(*d)
            d = (d[0] / n, d[1] / n)
            note("ride", abs(line_dist(tc, e1, d)) - g["R"])
        # on the fore tray, at its stop, and after the swing on the down line, then at the bite
        nw = m.cycle_windows(j + 1)
        for W in (cw["slide"][1], nw["down"][1], nw["feed"][1], nw["roll"][0] + 0.3 * (nw["roll"][1] - nw["roll"][0])):
            pose = pose_at(W)
            (e1, e2), _ = bottom_points(v, pid, pose)
            pl = v.named("tray", r"tray_plate1", pose)[0]
            top = [pl.c[i] + mvec(pl.r, [0, abs(pl.size[1]) / 2, 0])[i] for i in range(3)]
            dv = mvec(pl.r, [0, 0, 1])
            if W <= nw["down"][1] + 1e-9:
                note("tray", line_dist(e1, (top[1], top[2]), (dv[1], dv[2])))
                note("tray", line_dist(e2, (top[1], top[2]), (dv[1], dv[2])))
            if W >= nw["feed"][1] - 1e-9:
                note("bite", math.hypot(e1[0] - g["K"][0], e1[1] - g["K"][1]))
        stop = v.named("tray", r"tray_stop", pose_at(cw["slide"][1]))[0]
        (e1, e2), _ = bottom_points(v, pid, pose_at(cw["slide"][1]))
        sd = mvec(stop.r, [0, 0, 1])
        sface = (stop.c[1] + sd[1] * abs(stop.size[2]) / 2, stop.c[2] + sd[2] * abs(stop.size[2]) / 2)
        note("stop", (e2[0] - sface[0]) * sd[1] + (e2[1] - sface[1]) * sd[2])
    # the billet: on the tray up against its stop at W = 0, at the bite once fed
    (e1, e2), _ = bottom_points(v, f"billet{metal}", pose_at(0.0))
    pl = v.named("tray", r"tray_plate1", pose_at(0.0))[0]
    top = [pl.c[i] + mvec(pl.r, [0, abs(pl.size[1]) / 2, 0])[i] for i in range(3)]
    dv = mvec(pl.r, [0, 0, 1])
    note("tray", line_dist(e1, (top[1], top[2]), (dv[1], dv[2])))
    note("tray", line_dist(e2, (top[1], top[2]), (dv[1], dv[2])))
    w0 = m.cycle_windows(0)
    (e1, _), _ = bottom_points(v, f"billet{metal}", pose_at(w0["feed"][1]))
    note("bite", math.hypot(e1[0] - g["K"][0], e1[1] - g["K"][1]))


# ---------------------------------------------------------------- the cams
def top_inside(els, x, z, y0, y1, step=0.005):
    """The highest y in y0..y1 on the vertical line (x, z) inside any of `els`, or None."""
    n = int((y1 - y0) / step)
    for i in range(n, -1, -1):
        y = y0 + i * step
        if any(inside(e, (x, y, z)) for e in els):
            return y
    return None


def bottom_inside(els, x, z, y0, y1, step=0.005):
    """The lowest y in y0..y1 on the vertical line (x, z) inside any of `els`, or None."""
    for i in range(int((y1 - y0) / step) + 1):
        y = y0 + i * step
        if any(inside(e, (x, y, z)) for e in els):
            return y
    return None


def x_span_inside(els, y, z, x0, x1, step=0.005):
    xs = [x0 + i * step for i in range(int((x1 - x0) / step) + 1)]
    hit = [x for x in xs if any(inside(e, (x, y, z)) for e in els)]
    return (min(hit), max(hit)) if hit else None


def check_cams(v, n=48):
    """The tumbling shaft's cams carry their followers: each lift rod's roller stands on its cam (within a little,
    the cam drawn in 5-degree spokes), each rod's pad under the table's wear plate, and the transfer's fork straddles
    the barrel cam's rib at every sampled W of a pass."""
    m = v.m
    roll_gap, pad_gap, fork = [], [], []
    for i in range(n):
        W = (i + 0.5) / n
        pose = m.pose_at(W)
        cams = [e for e in v.posed("tumble", pose) if "_cam" in e.name]
        for k, cx in enumerate(m.LIFT_CAM_X, 1):
            roller = v.named("liftrods", rf"lrod{k}_roller", pose)
            ry = min(c[1] for e in roller for c in e.corners())
            mine = [e for e in cams if f"_cam{k}_" in e.name]
            ct = top_inside(mine, cx, m.TUMBLE_Z, m.TUMBLE_Y, m.TUMBLE_Y + 6.0)
            roll_gap.append(ry - ct)
            pad = v.named("liftrods", rf"lrod{k}_pad", pose)[0]
            wear = v.named("table", rf"table_wear{k}", pose)
            under = bottom_inside(wear, cx, m.TUMBLE_Z, m.TABLE_UNDER - 1.0, m.TABLE_UNDER + 3.0)
            pad_gap.append(under - max(c[1] for c in pad.corners()))
        ribs = [e for e in v.posed("tumble", pose) if "_rib" in e.name]
        pw = aabb_of(v.named("transfer", r"transfer_forkw", pose))
        pe = aabb_of(v.named("transfer", r"transfer_forke", pose))
        for dz in (-m.FORK_HALF, 0.0, m.FORK_HALF):          # along the prongs' length: the rib leans on the ramps
            for ym in (m.TUMBLE_Y + m.DRUM_R + 0.1, m.TUMBLE_Y + m.DRUM_R + m.RIB["h"] - 0.05):
                span = x_span_inside(ribs, ym, m.TUMBLE_Z + dz, m.DRUM_X[0], m.DRUM_X[1])
                fork.append(min(span[0] - pw[1][0], pe[0][0] - span[1]) if span else -9.0)
    print(f"cams: the lift rods' rollers {min(roll_gap):.3f}..{max(roll_gap):.3f} over their cams, their pads {min(pad_gap):.3f}..{max(pad_gap):.3f} "
          f"under the table, the fork's prongs {min(fork):.3f} off the rib at the least, over {n} points of a pass")
    if min(roll_gap) < -0.05 or max(roll_gap) > 0.25:
        v.fail("a lift rod's roller leaves its cam or runs into it")
    if min(pad_gap) < -0.05 or max(pad_gap) > 0.15:
        v.fail("a lift rod's pad leaves the table or runs into it")
    if min(fork) < -0.02:
        v.fail("the transfer's fork does not straddle the barrel cam's rib")


def eye_supports(frame, shaft, axis, c, rx):
    """The bearings (frame groups named by `rx`, an eye of bars or a block, by their name less its last number)
    whose bounds hold the shaft's axis (along `axis`, through c, the other two coordinates in order) and overlap the
    shaft along it, the shaft's section inside their bounds there."""
    if not shaft:
        return []
    u, w = [k for k in range(3) if k != axis]
    slo, shi = aabb_of(shaft)
    groups = {}
    for f in frame:
        if re.search(rx, f.name):
            groups.setdefault(re.sub(r"_?\d+$", "", f.name), []).append(f)
    out = []
    for name, els in sorted(groups.items()):
        lo, hi = aabb_of(els)
        holds = lo[u] < c[0] < hi[u] and lo[w] < c[1] < hi[w]
        overlap = min(hi[axis], shi[axis]) - max(lo[axis], slo[axis]) > 0.3
        if holds and overlap:
            out.append(name)
    return out


def check_supports(v):
    m = v.m
    frame = v.by_part["frame"]
    shafts = [
        ("bottom pinion's shaft", "pinionbottom", r"_shaft", 0, (m.BOT_Y, m.ROLL_Z), r"^fr_(westbrg|pinbrass\d_8_)", 2),
        ("top pinion's shaft", "piniontop", r"_shaft", 0, (m.TOP_Y, m.ROLL_Z), r"^fr_pinbrass\d_19_", 2),
        ("bottom roll", "rollbottom", r"_neck", 0, (m.BOT_Y, m.ROLL_Z), r"^fr_brass[we]b", 2),
        ("top roll", "rolltop", r"_neck", 0, (m.TOP_Y, m.ROLL_Z), r"^fr_brass[we]t", 2),
        ("entry shaft", "entry", r"_shaft|_rod", 2, (m.ENTRY_X, m.BOT_Y), r"^fr_entrybrg", 2),
        ("tumbling shaft", "tumble", r"_shaft", 0, (m.TUMBLE_Y, m.TUMBLE_Z), r"^fr_tumblebrg", 3),
        ("table's hinge pin", "table", r"_pin", 0, m.TABLE_PIVOT, r"^fr_hinge", 2),
        ("tray's pivot pin", "tray", r"_pin", 0, m.geo()["C"], r"^fr_traybrg", 2),
    ]
    for label, pid, rx, axis, c, brx, need in shafts:
        named = eye_supports(frame, v.named(pid, rx), axis, c, brx)
        print(f"support {label}: {len(named)} bearing(s) {', '.join(named) or 'NONE'}")
        if len(named) < need:
            v.fail(f"the {label} is not carried by {need} bearings")
    for k, cx in enumerate(m.LIFT_CAM_X, 1):
        for pose in (m.REST, m.pose_at(0.7)):
            if not eye_supports(frame, v.named("liftrods", rf"lrod{k}_rod", pose), 1, (cx, m.TUMBLE_Z), rf"^fr_rodguide{k}_"):
                v.fail(f"lift rod {k} runs in no guide at W {pose[2]}")
    print("support: each lift rod in its guide")


# ---------------------------------------------------------------- clearances
ROLL_PARTS = "rollbottom|rolltop"
BARS = r"(billet|bar\d)(iron|steel)"
ALLOWED = [
    ("entry", r"_shaft|_rod", "frame", r"fr_entrybrg"),
    ("pinionbottom", r"_shaft", "frame", r"fr_(westbrg|pinbrass)"), ("piniontop", r"_shaft", "frame", r"fr_pinbrass"),
    ("rollbottom|rolltop", r"_neck", "frame", r"fr_brass"), ("tumble", r"_shaft", "frame", r"fr_tumblebrg"),
    ("table", r"_pin", "frame", r"fr_hinge"), ("tray", r"_pin", "frame", r"fr_traybrg"),
    ("liftrods", r"_rod", "frame", r"fr_rodguide"),
    ("entry", r"entry_pinion", "crown", None), ("pinionbottom", r"_gear", "piniontop", r"_gear"), ("entry", r"entry_worm", "tumble", r"tumble_wheel"),
    ("pinionbottom", None, "crown", None),
    ("spindlebottom", None, "pinionbottom|rollbottom", r"_wob|_shaft|_neck"), ("spindletop", None, "piniontop|rolltop", r"_wob|_shaft|_neck"),
    ("liftrods", r"_roller", "tumble", r"_cam"), ("liftrods", r"_pad", "table", r"table_wear"),
    ("transfer", r"_fork", "tumble", r"_rib"), ("transfer", r"transfer_bar", "frame", r"fr_rail"),
    ("oillevel", None, "frame", r"fr_cup\w+_(base|glass)"),
    (BARS, None, "table", r"table_top"), (BARS, None, "tray", r"tray_(plate|stop)"),
    (BARS, None, "frame", r"fr_(foreplate|foreguide|delivery)"), (BARS, None, "transfer", r"transfer_finger"),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def rolling_now(m, pid, W):
    """Whether bar `pid` is in the rolls at W: while it is rolled out of its pass, or fed into the next and rolled."""
    bite = (m.FEED[0] + m.FEED[1]) / 2               # from half way through its feed the bar's end meets the rolls: the bite, ahead of the line of centres
    if pid.startswith("billet"):
        return bite <= W <= m.roll_window(0)[1] + 1e-6
    j = int(pid[3]) - 1
    wins = [m.roll_window(j)] + ([(j + 1 + bite, m.roll_window(j + 1)[1])] if j + 1 < len(m.PASSES) else [])
    return any(a - 1e-6 <= W <= b + 1e-6 for a, b in wins)


def riding_now(m, pid, W):
    if pid.startswith("billet") or pid.startswith(f"bar{len(m.PASSES)}"):
        return False
    cw = m.cycle_windows(int(pid[3]) - 1)
    return cw["pull"][0] - 1e-6 <= W <= cw["slide"][1] + 1e-6


def touching_pairs(v, pose):
    m = v.m
    W = pose[2]
    items = []
    for pid in v.by_part:
        for el in v.posed(pid, pose):
            if el.vanished or not m.on_show(pid, pose[3]):
                continue
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
                if pa == pb or not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                    continue
                if allowed(pa, ea.name, pb, eb.name):
                    continue
                bar, other = (pa, pb) if re.fullmatch(BARS, pa) else (pb, pa)
                if re.fullmatch(BARS, bar) and re.fullmatch(ROLL_PARTS, other) and (rolling_now(m, bar, W) or (other == "rolltop" and riding_now(m, bar, W))):
                    continue
                if re.fullmatch(BARS, pa) and re.fullmatch(BARS, pb) and rolling_now(m, pa, W) and rolling_now(m, pb, W):
                    continue                                 # the bar going in and the bar coming out: one bar, in the pass
                if obb_obb(ea, eb):
                    hits.setdefault(tuple(sorted((pa, pb))), {}).setdefault(tuple(sorted((ea.name, eb.name))), W)
    return hits


def check_clearances(v, poses, label):
    allhits = {}
    for pose in poses:
        for key, hs in touching_pairs(v, pose).items():
            for h, W in hs.items():
                allhits.setdefault(key, {}).setdefault(h, W)
    print(f"{label} over {len(poses)} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items(), key=lambda t: t[1])[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at W {w:.4f}" for (a, b), w in ex))
    if allhits:
        v.fail("parts run into each other")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = [e for pid in v.by_part if m.on_show(pid, pose[3]) for e in v.posed(pid, pose) if not e.vanished]
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at W {pose[2]}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses")
    if bad:
        v.fail("faces z-fight")


def check_oiler(v):
    m = v.m
    for oil in (0.0, 0.5, 1.0):
        for lvl in v.named("oillevel", r".", (0.0, 0.0, 0.0, 0, 0.0, oil)):
            lo, hi = lvl.aabb()
            want = m.OIL_EMPTY + (m.OIL_FULL - m.OIL_EMPTY) * oil
            if abs((hi[1] - lo[1]) - want) > 1e-6 or abs(lo[1] - m.OIL_Y) > 1e-6:
                v.fail(f"{lvl.name}: the oil level is wrong at oil {oil}")
    for tag in ("w", "e"):
        glass = v.named("frame", rf"^fr_cup{tag}_glass")
        lvl = v.named("oillevel", rf"oillevel_{tag}", (0.0, 0.0, 0.0, 0, 0.0, 1.0))[0]
        for problem in sight_glass(glass, lvl):
            v.fail(f"the {tag} cup's level is hidden: {problem}")
    print(f"oiler: two cups, the level {m.OIL_EMPTY} tall empty and {m.OIL_FULL:.2f} full, linear between, seen through glass from every side")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_floating(v)
    check_containment(v, job_poses(m) + [(0.7, 0.7, 0.4, 0, 0.0, 0.0)])
    check_anchors(v)
    check_textures(v)
    check_gearing(v)
    check_passes(v)
    check_work_path(v)
    check_cams(v, 24 if quick else 96)
    check_supports(v)
    step = 0.05 if quick else 0.01
    check_clearances(v, [m.REST, (1.1, 1.1, 0.0, 0, 0.0, 0.5)] + [m.pose_at(step * i) for i in range(int(3 / step) + 1)], "clearances")
    check_oiler(v)
    if not quick:
        check_zfight(v)
    return v.ok


def validate_files(m, shape, frame_shape, ship):
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL textures used but not declared: {missing}")
        ok = False
    tops = {}
    for c in ship["cells"]:
        x, y, z = c["pos"]
        if (x, z) not in tops or y > tops[(x, z)]["pos"][1]:
            tops[(x, z)] = c
    solid = lambda x, z: not tops[(x, z)].get("hollow")  # noqa: E731
    gaps = lid_gaps(ship["cells"], columns=solid)
    hollow = sorted(tuple(c["pos"]) for c in ship["cells"] if c.get("hollow"))
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, hollow {hollow}; "
          f"lids over every column with a solid top: {'yes' if not gaps else gaps}")
    if gaps or ship.get("powerCell") is None:
        ok = False
    return ok
