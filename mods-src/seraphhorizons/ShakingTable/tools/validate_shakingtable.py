"""The shaking table generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its
rule. Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in
make_shape.py proves the shipped files are this model moved.

A pose is (theta,) or (theta, state) or (theta, state, travel): the state is one of make_shape.STATES (what is
fitted), and the clearance, flow, floating and z-fighting checks look only at the parts that show together in
it. Without a travel the pose was turned from rest, so the travel is |theta|.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import (box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb,
                               supports)
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import part_of, posed

TAU = 2 * math.pi
MOVING = ("entry", "headshaft", "deck", "pullrod")
REST_POSE = (0.0,)


class V:
    def __init__(self, m, els, parts, rig):
        self.m, self.els, self.parts, self.rig = m, els, parts, rig
        self.req = {p["id"]: p["requires"] for p in parts}
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
        key = (pid, pose[0], pose[2] if len(pose) > 2 else None)
        if key not in self.cache:
            if len(self.cache) > 60000:
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

    def shown(self, state):
        """The part ids that show in a state."""
        return [pid for pid in self.by_part if self.m.on_show(self.req[pid], state)]


def stroke_poses(m, n=24, state="services", back=False):
    """One stroke (a turn of the head shaft) in n steps, in a state: the axle turned forward (or backwards)."""
    period = TAU / m.HEAD_RATIO
    return [((-1 if back else 1) * period * i / n, state) for i in range(n + 1)]


def deck_dz(v, pose):
    return v.point("deck", pose, [0.0, 0.0, 0.0])[2]


# ---------------------------------------------------------------- parts
def check_basic(v):
    worst = euler_round_trip(v.els)
    print(f"euler round trip: worst {worst:.1e}")
    if worst > 1e-9:
        v.fail("an element's rotation does not survive being written as Euler angles")
    seen, dup = set(), set()
    for n in (el.name for el in v.els):
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


def check_requires(v):
    """Every part is the frame's, one fitted set's or a water line's; the water line's metals are the same
    elements; a part rides only a part fitted whenever it is; the states fit only known sets."""
    m = v.m
    reqs = {p["requires"] for p in v.parts}
    if reqs != set(m.REQUIRES) | set(m.PIPES) | {None}:
        v.fail(f"the parts' requires are {reqs}, want the frame's (null), {m.REQUIRES} and {m.PIPES}")
    for pid in m.PIPES[1:]:
        a = sorted((e.name[len(pid):], e.c, e.size) for e in v.by_part[pid])
        b = sorted((e.name[len(m.PIPES[0]):], e.c, e.size) for e in v.by_part[m.PIPES[0]])
        if a != b:
            v.fail(f"{pid} is not {m.PIPES[0]}'s elements in another metal")
    for st, fit in m.STATES.items():
        if not set(fit) <= set(m.REQUIRES) | set(m.PIPES):
            v.fail(f"state {st} fits {fit}")
    for p in v.parts:
        if p.get("ride"):
            ride = next(q for q in v.parts if q["id"] == p["ride"])
            for st, fit in m.STATES.items():
                if p["requires"] in fit and ride["requires"] not in (None, *fit):
                    v.fail(f"in {st}, {p['id']} rides {ride['id']}, which is not fitted")
    counts = {st: sum(len(v.by_part[pid]) for pid in v.shown(st)) for st in m.STATES}
    print("states: " + ", ".join(f"{st} ({'+'.join(fit) or 'the frame'}) {counts[st]} elements" for st, fit in m.STATES.items()))


ROLES = [
    # the stand, the launders, the posts
    (r"^fr_(bed|sill|leg|rail|tie|post|tl_block|fl_block)", {"oak"}),
    (r"^fr_(tl|cl|ml)_(floor|side|end)|^fr_split$", {"planks"}),
    (r"^fr_(slide|strap)", {"iron"}),
    # the rectifier: the axle's cross continues the vanilla axle in oak; the case is cast iron
    (r"^entry_", {"oak"}),
    (r"^rc_", {"iron"}),
    # the head: cast iron, its screw steel; its shaft steel, its coupling and flywheel cast iron
    (r"^hd_screw_", {"steel"}),
    (r"^hd_", {"iron"}),
    (r"^headshaft_(rod|coupling_bolt|collar)", {"steel"}),
    (r"^headshaft_(coupling_[ab]|fly_)", {"iron"}),
    (r"^pullrod_rod", {"steel"}),
    (r"^pullrod_nut", {"iron"}),
    # the deck: boards, timber, and its cast bracket
    (r"^deck_(board|lip|fb_|wb_)", {"planks"}),
    (r"^deck_(joist|bearer|riffle)", {"oak"}),
    (r"^deck_bracket$", {"iron"}),
    # the chutes, and the water line in each metal: the pipe in its metal's texture, the cap in solder
    (r"^ch_", {"chute"}),
    (r"^pipecopper_", {"pipecopper", "solder"}),
    (r"^pipelead_", {"pipelead", "solder"}),
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
    if v.m.TEXTURES["chute"] != v.m.VANILLA_CHUTE["texture"]:
        v.fail("the chutes' texture is not vanilla's chute's")


def check_thickness(v):
    """No part thinner than about a voxel: every element at least 0.6 across in its two larger dimensions'
    smaller one, but for the listed thin-by-nature pieces (bolt heads, a handwheel's rim and spokes)."""
    thin_ok = re.compile(r"(bolt|_wheel_(rim|spoke))")
    bad = []
    for el in v.els:
        s = sorted(abs(x) for x in el.size)
        if s[1] < 0.6 - 1e-9 and not thin_ok.search(el.name):
            bad.append((el.name, [round(x, 2) for x in el.size]))
    print(f"thickness: {len(bad)} elements thinner than 0.6 across outside the thin-by-nature list")
    if bad:
        v.fail(f"too thin to read at block scale: {bad[:6]}")


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
    """At rest, the frame alone and the frame with each state's parts (moving ones included: a shaft is joined
    through its bearings) are one piece from the ground."""
    m = v.m
    for state in m.STATES:
        els = [e for pid in v.shown(state) for e in v.posed(pid, (0.0, state))]
        seen, loose = frame_floating(els)
        print(f"floating ({state}): {len(seen)} of {len(els)} elements joined to the ground")
        if loose:
            v.fail(f"in {state} these float: {loose[:8]}")


# ---------------------------------------------------------------- anchors
FACE_AXIS = {"west": (0, -1), "east": (0, 1), "down": (1, -1), "up": (1, 1), "north": (2, -1), "south": (2, 1)}


def check_anchors(v):
    """The power face meets the axle's oak cross at the power cell's centre in the game's axle profile; the water
    line's elbow is open on the water face's centre in ppex's 6 x 6 section; every chute is vanilla's section,
    flush with its face, centred in it; the game's chute is VANILLA_CHUTE; every anchor's face is on the
    footprint's outside and no two anchors want the same neighbouring block."""
    m = v.m
    rig = v.rig
    pc = m.POWER_CELL
    y, z = m.AXLE
    entry = v.by_part["entry"]
    lo, hi = aabb_of(entry)
    bars = sorted((round(e.size[1], 4), round(e.size[2], 4)) for e in entry)
    centred = rig["powerFace"] == "west" and pc[0] == 0 and (y - pc[1] * 16, z - pc[2] * 16) == (8.0, 8.0)
    print(f"anchors: power cell {list(pc)} {rig['powerFace']}: the axle's cross from x {lo[0]:.2f}, axis (y {y}, z {z}), bars "
          f"{bars} (the game's axle: two 4 x 2 bars)")
    if not centred or lo[0] > 1e-6 or bars != [(2.0, 4.0), (4.0, 2.0)]:
        v.fail("the axle's cross does not meet the power face at the cell's centre in the axle's profile")
    # the water line: the elbow's walls up to the water face, round the face's centre, ppex's 6 x 6
    wc = m.WATER_CELL
    for pid in m.PIPES:
        elbow = v.named(pid, r"_elbow_")
        elo, ehi = aabb_of(elbow)
        top = (wc[1] + 1) * 16
        section = (ehi[0] - elo[0], ehi[2] - elo[2])
        centre = ((elo[0] + ehi[0]) / 2 - wc[0] * 16, (elo[2] + ehi[2]) / 2 - wc[2] * 16)
        plugged = [e.name for e in elbow if e.name.endswith("_up")]
        print(f"anchors: water cell {list(wc)} {rig['waterFace']}: {pid}'s elbow to y {ehi[1]:.2f}, a {section[0]:.0f} x "
              f"{section[1]:.0f} section on the face's ({centre[0]}, {centre[1]}), its top {'plugged' if plugged else 'open'}")
        if rig["waterFace"] != "up" or abs(ehi[1] - top) > 1e-6 or section != (6.0, 6.0) or centre != (8.0, 8.0) or plugged:
            v.fail(f"{pid}'s elbow does not meet a ppex pipe end to end on the water face's centre")
    # the chutes
    tex = {f["texture"] for e in v.by_part["chutes"] for f in e.faces.values()}
    if tex != {"#chute"}:
        v.fail(f"the chutes' faces wear {tex}")
    chutes = [("infeed", "in", m.INFEED_CELL, m.INFEED_FACE, 1)] + [(n, n[:4], c, f, a) for n, c, f, a in m.OUTLETS]
    neighbours = {}
    cells = set(m.footprint())
    for name, short, cell, face, axis in chutes:
        k, sgn = FACE_AXIS[face]
        if rig[f"{name}Cell"] != list(cell) or rig[f"{name}Face"] != face or k != axis:
            v.fail(f"the rig's {name} anchor is {rig[f'{name}Cell']} {rig[f'{name}Face']}, the chute's {list(cell)} {face}")
        walls = v.named("chutes", rf"^ch_{short}_wall")
        across = [q for q in range(3) if q != axis]
        prof = sorted(tuple(round(v_, 4) for v_ in (w.aabb()[0][across[0]] - cell[across[0]] * 16, w.aabb()[1][across[0]] - cell[across[0]] * 16,
                                                     w.aabb()[0][across[1]] - cell[across[1]] * 16, w.aabb()[1][across[1]] - cell[across[1]] * 16))
                      for w in walls)
        plane = (cell[k] + (1 if sgn > 0 else 0)) * 16
        ends = {round(w.aabb()[1][k] if sgn > 0 else w.aabb()[0][k], 4) for w in walls}
        print(f"anchors: {name} chute in {list(cell)} {face}: walls {prof}, their ends at {sorted(ends)} (the face at {plane})")
        if prof != m.chute_profile(axis) or ends != {plane}:
            v.fail(f"the {name} chute is not vanilla's section flush with its face, centred in it")
        nb = tuple(cell[q] + (sgn if q == k else 0) for q in range(3))
        if nb in cells:
            v.fail(f"the {name} chute's face is inside the footprint")
        neighbours.setdefault(nb, []).append(name)
    for name, cell, face in (("power", m.POWER_CELL, m.POWER_FACE), ("water", m.WATER_CELL, m.WATER_FACE)):
        k, sgn = FACE_AXIS[face]
        nb = tuple(cell[q] + (sgn if q == k else 0) for q in range(3))
        if nb in cells:
            v.fail(f"the {name} face is inside the footprint")
        neighbours.setdefault(nb, []).append(name)
    clash = {k_: n for k_, n in neighbours.items() if len(n) > 1}
    print(f"anchors: {len(neighbours)} neighbouring blocks, one for each of power, water, the infeed and the three outputs")
    if clash:
        v.fail(f"anchors want the same neighbouring block: {clash}")
    game = m.read_vanilla_chute()
    if game is None:
        print("anchors: the game's chute shape is not here; VANILLA_CHUTE is held to it where it is")
    else:
        gwalls, runs, gtex = game
        good = gwalls == sorted(m.VANILLA_CHUTE["walls"]) and runs == {(0.0, 16.0)} and gtex == m.VANILLA_CHUTE["texture"]
        print(f"anchors: the game's {m.VANILLA_CHUTE['shape']}: walls {gwalls} the length of the block, texture {gtex}")
        if not good:
            v.fail("VANILLA_CHUTE is not the game's straight chute")


# ---------------------------------------------------------------- flows
def ray_down(el, p):
    """The distance down (along -y) from point p to where it enters element el, or None."""
    d = [p[k] - el.c[k] for k in range(3)]
    o = [sum(el.r[k][i] * d[k] for k in range(3)) for i in range(3)]
    dirn = [-el.r[1][i] for i in range(3)]
    t0, t1 = 0.0, 1e9
    for i in range(3):
        h = abs(el.size[i]) / 2
        if abs(dirn[i]) < 1e-12:
            if abs(o[i]) > h:
                return None
            continue
        a, b = (-h - o[i]) / dirn[i], (h - o[i]) / dirn[i]
        a, b = min(a, b), max(a, b)
        t0, t1 = max(t0, a), min(t1, b)
        if t0 > t1:
            return None
    return t0


def first_below(v, p, pose):
    state = pose[1]
    best = None
    for pid in v.shown(state):
        for el in v.posed(pid, pose):
            lo, hi = el.aabb()
            if not (lo[0] <= p[0] <= hi[0] and lo[2] <= p[2] <= hi[2] and lo[1] <= p[1] + 1e-9):
                continue
            t = ray_down(el, p)
            if t is not None and (best is None or t < best[0]):
                best = (t, el.name)
    return best


def inside_box(v, pose, walls_rx, p):
    """Whether point p (x, z) lies between a deck box's posed walls (their inner faces), at this pose."""
    walls = {e.name.rsplit("_", 1)[1]: e.aabb() for e in v.named("deck", walls_rx, pose)}
    return walls["w"][1][0] < p[0] < walls["weir"][0][0] and walls["n"][1][2] < p[1] < walls["s"][0][2]


def check_flows(v):
    """Ray-drops from every place material leaves: the infeed chute's bore onto the deck inside the feed box, the
    nozzles into the water box, the deck's low edge into the tailings launder, the lip into the concentrate
    launder west of the splitter and the middlings launder east of it, through a whole stroke; each launder's
    floor falls to its outlet and meets its chute's floor there, and the outlet's mouth drops into the chute."""
    m = v.m
    poses = stroke_poses(m, 12)
    n = 0
    (bx0, bx1), (bz0, bz1) = (m.INFEED_CELL[0] * 16 + 5.0, m.INFEED_CELL[0] * 16 + 11.0), (m.INFEED_CELL[2] * 16 + 5.0, m.INFEED_CELL[2] * 16 + 11.0)
    drops = []
    for x in (bx0 + 0.1, (bx0 + bx1) / 2, bx1 - 0.1):
        for z in (bz0 + 0.1, (bz0 + bz1) / 2, bz1 - 0.1):
            drops.append(("the infeed chute's bore", [x, m.CHUTE_IN_Y0 - 0.05, z], r"^deck_board", r"^deck_fb_"))
    for nz in m.NOZZLES_Z:
        drops.append((f"the nozzle at z {nz}", [sum(m.NOZZLE_X) / 2, m.NOZZLE_Y0 - 0.05, nz], r"^deck_board", r"^deck_wb_"))
    for label, p, rx, box in drops:
        for pose in poses:
            hit = first_below(v, p, pose)
            n += 1
            if hit is None or not re.search(rx, hit[1]) or not inside_box(v, pose, box, (p[0], p[2])):
                v.fail(f"flow: {label} from {[round(c, 2) for c in p]} at {pose} lands on {hit[1] if hit else 'nothing'}, "
                       f"not the deck inside its box")
                break
    for pose in poses:
        dz = deck_dz(v, pose)
        edge = max(e.aabb()[1][0] for e in v.named("deck", r"^deck_board", pose))
        for z in (16.0, 22.0, 28.0, 34.0, 41.0):
            p = [edge + 0.1, m.deck_y(m.DECK_X[1]) - 0.3, z + dz]
            hit = first_below(v, p, pose)
            n += 1
            if hit is None or not re.match(r"^fr_tl_floor", hit[1]):
                v.fail(f"flow: the deck's low edge at z {z} at {pose} lands on {hit[1] if hit else 'nothing'}")
        lip = max(e.aabb()[1][2] for e in v.named("deck", r"^deck_lip", pose))
        for x in (4.0, 8.0, 12.0, 16.0, 18.4, 19.6, 22.0, 25.0, 27.2):
            p = [x, m.deck_y(x) + m.LIP_Y[0] - 0.1, lip + 0.1]
            want = r"^fr_cl_floor" if x < m.CL_X[1] else r"^fr_ml_floor"
            hit = first_below(v, p, pose)
            n += 1
            if hit is None or not re.match(want, hit[1]):
                v.fail(f"flow: the lip at x {x} at {pose} lands on {hit[1] if hit else 'nothing'}, not {want}")
    # the launders fall to their outlets, whose floors meet the chutes' floors; a drop from each outlet's mouth
    # lands in its chute
    outs = (("tailings", "tail", m.tl_floor, (m.TL_Z[0] + m.LAUNDER_T, m.TL_Z[1] - m.LAUNDER_T), 2),
            ("concentrate", "conc", m.cl_floor, (m.CL_X[0] + m.LAUNDER_T, m.CL_X[1]), 0),
            ("middlings", "midd", m.ml_floor, (m.ML_X[0], m.ML_X[1] - m.LAUNDER_T), 0))
    for name, short, fl, span, along in outs:
        g0, g1 = m.outlet_span(name)
        lowest = min(fl(span[0]), fl(span[1]))
        at = (fl(g0), fl(g1))
        low_end = span[0] if fl(span[0]) < fl(span[1]) else span[1]
        mouth = [0.0, max(at) + 0.2, 0.0]
        _, cell, face, axis = next(o for o in m.OUTLETS if o[0] == name)
        mouth[axis] = m.outlet_from(name) + 0.3
        mouth[along] = (g0 + g1) / 2
        hit = first_below(v, mouth, (0.0, "services"))
        n += 1
        print(f"flows: the {name} launder falls to its outlet ({g0}..{g1}, its floor {min(at):.2f}..{max(at):.2f}, the chute's "
              f"{m.CHUTE_FLOOR}); its lowest end at {low_end:.2f}; its mouth drops onto {hit[1] if hit else 'nothing'}")
        if not (m.CHUTE_FLOOR < min(at) and max(at) < m.CHUTE_FLOOR + 0.6) or abs(min(at) - lowest) > 0.3 \
                or not (min(g0, g1) - 1.5 < low_end < max(g0, g1) + 1.5) or hit is None or not re.match(rf"^ch_{short}_wall", hit[1]):
            v.fail(f"the {name} launder does not fall to its outlet and into its chute")
    print(f"flows: {len(drops)} drops onto the deck in its boxes, the low edge and the lip into their launders, over a stroke "
          f"({n} drops): the feed and the water onto the deck, the tailings off the low side, the concentrate and the middlings "
          "off the far end, each launder to its chute")


# ---------------------------------------------------------------- the stroke and the drive
def angle_about_x(v, pid, pose):
    mm = v.mat(pid, pose)
    return math.atan2(mm[2][1], mm[1][1])


def check_stroke(v):
    """The deck's stroke: one a turn of the head shaft, as long as STROKE, its speed changing sign twice a stroke,
    the push forward (south) slow and long, the pull back quick; the rectifier: the head shaft and the deck the
    same whichever way the axle turns, the axle's cross following the axle."""
    m = v.m
    n = 720
    period = TAU / m.HEAD_RATIO
    zs = [deck_dz(v, (period * i / n,)) for i in range(n + 1)]
    sp = [(zs[i + 1] - zs[i]) for i in range(n)]
    fwd = sum(1 for s in sp if s > 0) / n
    changes = sum(1 for i in range(n) if (sp[i] > 0) != (sp[i - 1] > 0))
    stroke = max(zs) - min(zs)
    vf, vb = max(sp) * n / period, -min(sp) * n / period
    worst = max(abs(zs[i] - m.stroke_at(m.head_angle(period * i / n))) for i in range(n + 1))
    print(f"stroke: {stroke:.4f} voxels (want {m.STROKE}), one a turn of the head shaft ({m.HEAD_RATIO:g} a turn of the axle); "
          f"forward (south) {fwd:.0%} of the stroke at up to {vf:.3f} voxels a radian of the axle's travel, back {1 - fwd:.0%} at up to {vb:.3f} "
          f"({vb / vf:.2f} times as fast); its speed changes sign {changes} times a stroke; the rig against the harmonics: {worst:.1e}")
    if abs(stroke - m.STROKE) > 2e-3 or not (0.6 <= fwd <= 0.8) or changes != 2 or vb < 2.0 * vf or worst > 1e-4 \
            or abs(zs[0] - zs[-1]) > 1e-9:
        v.fail("the deck's stroke is not a slow push forward and a quick pull back, one a turn of the head shaft")
    for th in (0.4, 1.3, 2.9, 7.7):
        fwd_m, back_m = v.mat("headshaft", (th,)), v.mat("headshaft", (-th,))
        dk_f, dk_b = v.mat("deck", (th,)), v.mat("deck", (-th,))
        same = all(abs(fwd_m[i][j] - back_m[i][j]) < 1e-12 and abs(dk_f[i][j] - dk_b[i][j]) < 1e-12 for i in range(3) for j in range(4))
        a = angle_about_x(v, "headshaft", (th,))
        turned = abs(math.remainder(a - m.HEAD_RATIO * th, TAU)) < 1e-9
        e_f, e_b = angle_about_x(v, "entry", (th,)), angle_about_x(v, "entry", (-th,))
        follows = abs(math.remainder(e_f - th, TAU)) < 1e-9 and abs(math.remainder(e_b + th, TAU)) < 1e-9
        rod = v.mat("pullrod", (th,))
        rides = all(abs(rod[i][j] - dk_f[i][j]) < 1e-12 for i in range(3) for j in range(4))
        if not (same and turned and follows and rides):
            v.fail(f"the rectifier at axle {th:+}: head shaft and deck the same both ways {same}, the head shaft {m.HEAD_RATIO:g} times "
                   f"the travel {turned}, the cross with the axle {follows}, the pull rod with the deck {rides}")
    print(f"rectifier: the head shaft turns {m.HEAD_RATIO:g} times the axle's travel the same way, and the deck strokes the same, "
          "whichever way the axle turns; the axle's cross turns with the axle; the pull rod moves with the deck")


def check_supports(v):
    """The head shaft in its bearings (the case's east boss, the head box's bosses, the pedestal), the axle's
    cross in the case's west boss; the collars either side of the pedestal; the joists on their slides through a
    stroke; the pull rod through its guide and in the head box, its end through the deck's bracket."""
    m = v.m
    statics = v.by_part["rectifier"] + v.by_part["head"]
    for label, pid, rx, need, brx in (("head shaft", "headshaft", r"_rod", 4, r"^(rc_boss_e|hd_boss_[we]|hd_ped_bearing)"),
                                     ("axle's cross", "entry", r"^entry_", 1, r"^rc_boss_w")):
        found, span = supports(statics, v.named(pid, rx), 0, m.AXLE)
        named = sorted({re.sub(r"_\d+$", "", n_) for n_ in found if re.search(brx, n_)})
        print(f"support {label}: {len(named)} bearing(s) {', '.join(named) or 'NONE'}")
        if len(named) < need:
            v.fail(f"the {label} is not carried by {need} bearing(s)")
    plo, phi = aabb_of(v.named("head", r"^hd_ped_bearing"))
    gaps = (plo[0] - aabb_of(v.named("headshaft", r"_collar1_"))[1][0], aabb_of(v.named("headshaft", r"_collar2_"))[0][0] - phi[0])
    if not all(0.02 < g < 0.2 for g in gaps):
        v.fail(f"the collars do not sit either side of the pedestal (gaps {gaps})")
    for pose in stroke_poses(m, 12, "deck"):
        for j, (jx0, jx1) in enumerate(m.JOISTS_X, 1):
            joists = v.named("deck", rf"^deck_joist{j}_", pose)
            jlo, jhi = aabb_of(joists)
            for s in v.named("frame", rf"^fr_slide{j}\d$"):
                slo, shi = s.aabb()
                gap = min(e.aabb()[0][1] for e in joists if e.aabb()[0][2] < shi[2] and e.aabb()[1][2] > slo[2]) - shi[1]
                if not (jlo[2] < slo[2] and shi[2] < jhi[2] and -0.05 <= gap < 0.2):
                    v.fail(f"the deck's joist {j} leaves its slide {s.name} at {pose} (gap {gap:.3f})")
    body = v.named("head", r"^hd_body$")[0].aabb()
    glo, ghi = aabb_of(v.named("head", r"^hd_guide"))
    for pose in stroke_poses(m, 24, "services"):
        rlo, rhi = aabb_of(v.named("pullrod", r"_rod", pose))
        blo, bhi = v.named("deck", r"^deck_bracket$", pose)[0].aabb()
        if not (body[0][2] + 1.0 < rlo[2] < body[1][2] - 1.0 and rhi[2] > bhi[2] and rlo[2] < glo[2] and rhi[2] > ghi[2]
                and glo[0] < rlo[0] and rhi[0] < ghi[0] and glo[1] < rlo[1] and rhi[1] < ghi[1]):
            v.fail(f"the pull rod leaves its guide, the head box or the bracket at {pose}")
            break
    print(f"support: the collars {gaps[0]:.2f} and {gaps[1]:.2f} off the pedestal; the joists on their slides and the pull rod "
          "through its guide, into the head box and through the bracket over a stroke")


def meets(a, b, eps=0.02):
    """Whether two boxes (lo, hi) touch or overlap, to within eps."""
    return all(a[0][k] <= b[1][k] + eps and b[0][k] <= a[1][k] + eps for k in range(3))


def check_stages(v):
    """The build: the frame placed, then each stage one fitted set in order, services (the chutes and both water
    line metals) last, every requires in one stage, the rig's buildStages the same and each state the frame or a
    stage on the ones before it. Each stage's parts touch what came before them. The head comes after the
    rectifier and the deck: its shaft's first bearing is the rectifier's east boss (without it the head's own
    bosses and pedestal are three, not the four the shaft needs) and its pull rod pins into the deck's bracket."""
    m = v.m
    order = [sid for sid, _, _ in m.BUILD_STAGES]
    adds = [r for _, req, _ in m.BUILD_STAGES for r in req]
    if sorted(adds) != sorted(set(m.REQUIRES) | set(m.PIPES)) or len(adds) != len(set(adds)):
        v.fail(f"the stages fit {adds}, not each of {m.REQUIRES} and {m.PIPES} once")
    if m.BUILD_STAGES[-1][1] != ("chutes", *m.PIPES):
        v.fail("services (the chutes and the water line) are not the last stage")
    rig = [(s["stage"], s["id"], tuple(s["adds"])) for s in v.rig["buildStages"]]
    if rig != [(n, sid, req) for n, (sid, req, _) in enumerate(m.BUILD_STAGES, 1)]:
        v.fail(f"the rig's buildStages {rig} are not BUILD_STAGES")
    want, fit = {"frame": ()}, ()
    for sid, req, _ in m.BUILD_STAGES:
        fit += tuple(r for r in req if r not in m.PIPES[1:])
        want[sid] = fit
    if m.STATES != want:
        v.fail(f"the states {m.STATES} are not the frame and each stage on the ones before it ({want})")
    stage_of = {r: i for i, (_, req, _) in enumerate(m.BUILD_STAGES) for r in req}
    unborne = []
    for i, (sid, req, _) in enumerate(m.BUILD_STAGES):
        prior = [e for pid in v.by_part if v.req[pid] is None or stage_of[v.req[pid]] < i for e in v.posed(pid, REST_POSE)]
        own = [e for pid in v.by_part if v.req[pid] in req for e in v.posed(pid, REST_POSE)]
        if not any(meets(a.aabb(), b.aabb()) for a in own for b in prior):
            unborne.append(sid)
    if unborne:
        v.fail(f"stages whose parts touch nothing put in before them: {unborne}")
    if not stage_of["rectifier"] < stage_of["head"] or not stage_of["deck"] < stage_of["head"]:
        v.fail("the head is fitted before the rectifier (its shaft's first bearing) or the deck (its pull rod's pin)")
    shaft = v.named("headshaft", r"_rod")
    alone, _ = supports(v.by_part["head"], shaft, 0, m.AXLE)
    alone = {re.sub(r"_\d+$", "", n_) for n_ in alone if re.search(r"^(hd_boss_[we]|hd_ped_bearing)", n_)}
    withrc, _ = supports(v.by_part["head"] + v.by_part["rectifier"], shaft, 0, m.AXLE)
    withrc = {re.sub(r"_\d+$", "", n_) for n_ in withrc if re.search(r"^(rc_boss_e|hd_boss_[we]|hd_ped_bearing)", n_)}
    if "rc_boss_e" not in withrc or len(withrc) < 4 or len(alone) >= 4:
        v.fail(f"the head shaft's bearings: {sorted(alone)} from the head alone, {sorted(withrc)} with the rectifier")
    print(f"stages: the frame placed, then {'; '.join(f'{n} {sid} ({'+'.join(req)})' for n, (sid, req, _) in enumerate(m.BUILD_STAGES, 1))}; "
          f"each on what came before it; the head shaft on {len(alone)} bearing(s) of the head's own, {len(withrc)} with the "
          "rectifier's east boss, so the rectifier comes first")


# ---------------------------------------------------------------- clearances
# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels at every sampled pose.
ALLOWED = [
    ("entry", None, "rectifier", r"rc_boss_w"),
    ("headshaft", r"_rod", "rectifier", r"rc_boss_e"),
    ("headshaft", r"_rod", "head", r"hd_(boss|body|ped_bearing)"),
    ("pullrod", r"_rod", "head", r"hd_(guide|body)"),
    ("pullrod", None, "deck", r"deck_bracket"),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose, moving_only=False):
    state = pose[1]
    items = []
    for pid in v.shown(state):
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
                if pa == pb or (moving_only and pa not in MOVING and pb not in MOVING):
                    continue
                if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                    continue
                if allowed(pa, ea.name, pb, eb.name):
                    continue
                if obb_obb(ea, eb):
                    hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def check_clearances(v, poses, label, moving_only=False):
    allhits = {}
    for pose in poses:
        for key, hs in touching_pairs(v, pose, moving_only).items():
            allhits.setdefault(key, {})
            for h in hs:
                allhits[key].setdefault(h, pose)
    print(f"{label} over {len(poses)} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at {p}" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = [e for pid in v.shown(pose[1]) for e in v.posed(pid, pose)]
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
    check_requires(v)
    check_textures(v)
    check_thickness(v)
    check_floating(v)
    check_containment(v, [(t, "all") for t, _ in stroke_poses(m, 24)] + [(t, "all") for t, _ in stroke_poses(m, 6, back=True)])
    check_anchors(v)
    check_flows(v)
    check_stroke(v)
    check_supports(v)
    check_stages(v)
    poses = [p for st in m.STATES for p in stroke_poses(m, 8 if quick else 16, st)] + stroke_poses(m, 4, back=True)
    check_clearances(v, poses, "clearances")
    if not quick:
        check_clearances(v, stroke_poses(m, 96), "swept paths (every 1.25 degrees of the axle, a whole stroke)", moving_only=True)
        check_zfight(v)
    return v.ok


def validate_files(m, shape, frame_shape, ship):
    """The written files: every texture declared; lids over every column; no anchor's cell and not the
    controller's hollow."""
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL textures used but not declared: {missing}")
        ok = False
    gaps = lid_gaps(ship["cells"])
    hollow = [tuple(c["pos"]) for c in ship["cells"] if c.get("hollow")]
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, "
          f"{len(hollow)} hollow; lids over every column: {'yes' if not gaps else gaps}")
    if gaps:
        ok = False
    for key in [k for k in ship if k.endswith("Cell")]:
        if tuple(ship[key]) in hollow:
            print(f"FAIL the {key} is hollow")
            ok = False
    if (0, 0, 0) in hollow:
        print("FAIL the controller's cell is hollow")
        ok = False
    return ok
