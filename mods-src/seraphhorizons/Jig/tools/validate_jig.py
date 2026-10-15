"""The jig generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its
rule. Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in
make_shape.py proves the shipped files are this model moved.

A pose is (theta,) or (theta, state): the state is one of make_shape.STATES (what is fitted), and the
clearance, flow, floating and z-fighting checks look only at the parts that show together in it.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb, supports
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import part_of, posed

TAU = 2 * math.pi


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
        return self.m.pm(self.parts, pid, pose[:1])

    def posed(self, pid, pose):
        key = (pid, pose[0])
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


def turns(n=24, state="working"):
    """A turn of the axle in n steps, in a state."""
    return [(TAU * i / n, state) for i in range(n + 1)]


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


def check_build_stages(v):
    """The build stages as settled (2026-10-15): the hutch is the frame, placed; then the sieve, the drive and
    plunger, and last services (the chutes and the water inlet, one metal). Every fitted part is in one
    stage, the frame and the contents in none; the rig's buildStages are the generator's; each stage's state
    fits the stages up to it, so the floating check sees every stage stand on the ones before."""
    m = v.m
    stages = v.rig["buildStages"]
    if [(st["stage"], st["adds"], st["name"]) for st in stages] != [(n, list(req), name) for n, (req, name) in enumerate(m.BUILD_STAGES, 1)]:
        v.fail(f"the rig's build stages are {stages}")
    if [st["adds"] for st in stages] != [["sieve"], ["drive", "plunger"], ["infeed", "outlets", *m.PIPES]]:
        v.fail("the build stages are not the sieve, the drive and plunger and, last, services")
    staged = [r for st in stages for r in st["adds"]]
    if len(staged) != len(set(staged)) or set(staged) != set(m.REQUIRES) | set(m.PIPES):
        v.fail("every fitted part is in one build stage, the frame and the contents in none")
    for n, name in enumerate(("sieve", "drive", "complete"), 1):
        want = {r for st in stages[:n] for r in st["adds"]} - set(m.PIPES[1:])
        if set(m.STATES[name]) != want:
            v.fail(f"state {name} fits {set(m.STATES[name])}, not build stages 1..{n}")
    print("build stages: " + ", ".join(f"{st['stage']} {st['name']} ({'+'.join(st['adds'])})" for st in stages))


def check_requires(v):
    """The parts' requires are the hutch's (null), the fitted parts', the inlet's metals and the contents'; a
    part riding another is fitted with it in every state; the inlet's metals are the same elements; every
    state names only known values."""
    m = v.m
    reqs = {p["requires"] for p in v.parts}
    want = set(m.REQUIRES) | set(m.PIPES) | set(m.CONTENTS) | {None}
    if reqs != want:
        v.fail(f"the parts' requires are {reqs}, want {want}")
    for pid in m.PIPES[1:]:
        a = sorted((e.name[len(pid):], e.c, e.size) for e in v.by_part[pid])
        b = sorted((e.name[len(m.PIPES[0]):], e.c, e.size) for e in v.by_part[m.PIPES[0]])
        if a != b:
            v.fail(f"{pid} is not {m.PIPES[0]}'s elements in another metal")
    for st, fit in m.STATES.items():
        if not set(fit) <= want:
            v.fail(f"state {st} fits unknown values {set(fit) - want}")
    for p in v.parts:
        if p.get("ride"):
            ride = next(q for q in v.parts if q["id"] == p["ride"])
            for st, fit in m.STATES.items():
                if p["requires"] in fit and ride["requires"] not in (None, *fit):
                    v.fail(f"in {st}, {p['id']} rides {ride['id']}, which is not fitted")
    if not set(m.STATES["complete"]) == set(m.REQUIRES) | {m.PIPES[0]}:
        v.fail("the complete state is not every fitted part with the inlet")
    counts = {st: sum(len(v.by_part[pid]) for pid in v.shown(st)) for st in ("frame", "sieve", "drive", "complete", "working")}
    print("requires: " + ", ".join(sorted(str(r) for r in reqs)) + "; elements shown: "
          + ", ".join(f"{k} {n}" for k, n in counts.items()))


ROLES = [
    # the hutch
    (r"^hutch_(sill|ledge|hopper_cleat)", {"oak"}),
    (r"^hutch_(floor|wall|partition|head_|hopper$)", {"planks"}),
    (r"^hutch_(weir_lip|spigot_(bottom|top|w|e))$", {"iron"}),
    (r"^hutch_strap_", {"iron"}),
    (r"^sieve_mesh$", {"screen"}),
    # the drive: the vanilla axle's oak end, then iron and steel
    (r"^shaft_entry[ab]$", {"oak"}),
    (r"^shaft_flange", {"iron"}),
    (r"^shaft_(rod|collar)", {"steel"}),
    (r"^stand_(post_[we]|beam)$", {"oak"}),
    (r"^stand_(bush_[we]|strap_[we]\d|guide)$", {"iron"}),
    (r"^sheave_", {"steel"}),
    (r"^plunger_yoke_", {"iron"}),
    (r"^plunger_(tail|rod)$", {"steel"}),
    (r"^plunger_board$", {"oak"}),
    (r"^plunger_boss$", {"iron"}),
    # the feed and the outlets: vanilla's chute sections in its copper, the trough's boards, the timber and iron
    (r"^(infeed_chute|outlet_tailchute|outlet_concchute)_([nsew]|bottom|top)$", {"chute"}),
    (r"^infeed_trough_", {"planks"}),
    (r"^infeed_bearer\d$", {"oak"}),
    (r"^infeed_strap\d[ns]$", {"iron"}),
    (r"^outlet_tail_(arm|leg)$", {"iron"}),
    (r"^outlet_conc_block$", {"oak"}),
    # the water inlet, in each metal: the pipe in its metal's texture (and solder, should a joint be drawn)
    (r"^pipecopper_", {"pipecopper", "solder"}),
    (r"^pipelead_", {"pipelead", "solder"}),
    # the contents
    (r"^water(sieve|plunger)_surface$", {"water"}),
    (r"^bed_ore$", {"bed"}),
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
    transparent = sorted(el.name for el in v.els if el.render_pass is not None)
    want = sorted(el.name for el in v.els if el.name.startswith("water"))
    if transparent != want:
        v.fail(f"the transparent pass is for the water's surface alone, not {transparent}")


def check_thickness(v):
    """No part thinner than about half a voxel across its two larger dimensions' smaller one, but for the
    thin-by-nature pieces (the sieve, the weir's lip, the water's surface, straps)."""
    thin_ok = re.compile(r"(sieve_mesh|weir_lip|_surface$|strap)")
    bad = []
    for el in v.els:
        s = sorted(abs(x) for x in el.size)
        if s[1] < 0.5 and not thin_ok.search(el.name):
            bad.append((el.name, [round(x, 2) for x in el.size]))
    print(f"thickness: {len(bad)} elements thinner than 0.5 across outside the thin-by-nature list")
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
    """At rest, the hutch alone, after each build stage, the finished machine, and each service alone on the
    machine before it, are each one piece from the ground (the shaft is joined through its bushes, the plunger through its
    yoke on the sheave)."""
    m = v.m
    for state in m.STATES:
        els = [e for pid in v.shown(state) for e in v.posed(pid, (0.0, state))]
        seen, loose = frame_floating(els)
        print(f"floating ({state}): {len(seen)} of {len(els)} elements joined to the ground")
        if loose:
            v.fail(f"in {state} these float: {loose[:8]}")


# ---------------------------------------------------------------- anchors
def check_chute(v, label, prefix, axis, cell, face, run):
    """A chute section is vanilla's: its walls' section the game's (VANILLA_CHUTE as that run has it), centred
    in its cell's face and flush with it, in the chute's texture, the length `run` along its axis."""
    m = v.m
    prof, runs = m.chute_profile(v.els, prefix, axis, cell)
    want = sorted(m.chute_walls(axis))
    tex = {f["texture"] for e in v.els if e.name.startswith(prefix) for f in e.faces.values()}
    a = m.AX[axis]
    side = {"west": (0, cell[0] * 16.0), "east": (0, cell[0] * 16.0 + 16), "north": (2, cell[2] * 16.0),
            "south": (2, cell[2] * 16.0 + 16), "down": (1, cell[1] * 16.0), "up": (1, cell[1] * 16.0 + 16)}[face]
    flush = side[0] == a and all(side[1] in r for r in runs)
    print(f"anchors: {label} at {list(cell)} {face}: walls {prof}, the game's {want}; run {sorted(runs)}; texture "
          f"{sorted(tex)}")
    if prof != want or not flush or runs != {run} or tex != {"#chute"} or m.TEXTURES["chute"] != m.VANILLA_CHUTE["texture"]:
        v.fail(f"the {label} is not vanilla's chute section centred in and flush with its face")


def check_anchors(v):
    """The power face meets the shaft's oak end at the power cell's face's centre in the axle's profile; each
    chute is vanilla's section on its face; the water inlet meets a ppex pipe end to end on the water face's
    centre; every service is on a face of its own, and the blocks beyond them differ."""
    m = v.m
    rig = v.rig
    lo, hi = aabb_of(v.named("shaft", r"_entry[ab]$"))
    ey, ez = m.SHAFT
    pc = m.POWER_CELL
    centred = pc[0] == 0 and rig["powerFace"] == "west" and (ey - pc[1] * 16, ez - pc[2] * 16) == (8.0, 8.0)
    print(f"anchors: power cell {pc} {rig['powerFace']}: the axle's oak cross from x {lo[0]:.2f}, axis (y {ey}, z {ez}), "
          f"{hi[1] - lo[1]:.1f} x {hi[2] - lo[2]:.1f}")
    if not centred or lo[0] > 1e-6 or abs(hi[1] - lo[1] - 4.0) > 1e-6 or abs(hi[2] - lo[2] - 4.0) > 1e-6:
        v.fail("the shaft does not meet the power face at the cell's centre in the axle's profile")
    top = (m.INFEED_CELL[1] + 1) * 16.0
    check_chute(v, "infeed", "infeed_chute_", "y", m.INFEED_CELL, m.INFEED_FACE, (m.INFEED_Y0, top))
    check_chute(v, "tailings chute", "outlet_tailchute_", "x", m.TAILINGS_CELL, m.TAILINGS_FACE, (0.0, m.TAIL_X1))
    check_chute(v, "concentrate chute", "outlet_concchute_", "z", m.CONCENTRATE_CELL, m.CONCENTRATE_FACE, (0.0, m.CONC_Z1))
    for key, cell, face in (("infeed", m.INFEED_CELL, m.INFEED_FACE), ("tailings", m.TAILINGS_CELL, m.TAILINGS_FACE),
                            ("concentrate", m.CONCENTRATE_CELL, m.CONCENTRATE_FACE), ("water", m.WATER_CELL, m.WATER_FACE),
                            ("power", m.POWER_CELL, m.POWER_FACE)):
        if rig[f"{key}Cell"] != list(cell) or rig[f"{key}Face"] != face:
            v.fail(f"the rig's {key} anchor is not {cell} {face}")
    game = m.read_vanilla_chute()
    if game is None:
        print("anchors: the game's chute shape is not here; VANILLA_CHUTE is held to it where it is")
    else:
        walls, runs, gtex = game
        ok = walls == sorted(m.VANILLA_CHUTE["walls"]) and runs == {(0.0, 16.0)} and gtex == m.VANILLA_CHUTE["texture"]
        print(f"anchors: the game's {m.VANILLA_CHUTE['shape']}: walls {walls} the length of the block, texture {gtex}")
        if not ok:
            v.fail("VANILLA_CHUTE is not the game's straight chute")
    wc = m.WATER_CELL
    wx, wy = m.WATER_IN
    centre_ok = (wx - wc[0] * 16, wy - wc[1] * 16) == (8.0, 8.0) and wc[2] == m.CELLS_Z - 1 and rig["waterFace"] == "south"
    for pid in m.PIPES:
        ilo, ihi = aabb_of(v.named(pid, r"_inlet_"))
        section = (ihi[0] - ilo[0], ihi[1] - ilo[1])
        centre = ((ilo[0] + ihi[0]) / 2, (ilo[1] + ihi[1]) / 2)
        print(f"anchors: water cell {list(wc)} {rig['waterFace']}: {pid}'s inlet to z {ihi[2]:.2f}, ppex's {section[0]:.0f} x "
              f"{section[1]:.0f} tube on (x {centre[0]}, y {centre[1]}), its open end at z {ilo[2]:.2f}")
        if not centre_ok or abs(ihi[2] - m.Z_LEN) > 1e-6 or section != (6.0, 6.0) or centre != (wx, wy):
            v.fail(f"{pid}'s inlet does not meet a ppex pipe end to end on the water face's centre")
        if not (m.PLUNGE_Z[0] < ilo[2] < m.PLUNGE_Z[1] and ihi[1] < m.BOARD_Y[0] - m.E):
            v.fail(f"{pid}'s inlet does not open into the plunger compartment under the plunger")
    faces = [(tuple(rig[f"{k}Cell"]), rig[f"{k}Face"]) for k in ("power", "infeed", "tailings", "concentrate", "water")]
    step = {"west": (-1, 0, 0), "east": (1, 0, 0), "down": (0, -1, 0), "up": (0, 1, 0), "north": (0, 0, -1), "south": (0, 0, 1)}
    beyond = [tuple(c[k] + step[f][k] for k in range(3)) for c, f in faces]
    inside = {tuple(c) for c in m.footprint()}
    print(f"anchors: the services' blocks beyond their faces {beyond}")
    if len(set(faces)) != len(faces) or len(set(beyond)) != len(beyond) or set(beyond) & inside:
        v.fail("every service needs a face of its own, the blocks beyond them outside the machine and apart")


# ---------------------------------------------------------------- water and flows
def check_levels(v):
    """The water stands where a jig's does: the weir's sill over the tailings chute's bore floor (so it
    overflows into it) and under every wall's rim; the sieve and the plunger board under the water through the
    stroke; the yoke over it; the partition open below the plunger board's lowest and above the floor; the
    spigot draining the floor into the concentrate chute's bore. The contents' surfaces sit at the weir and
    follow the plunger the way the water would."""
    m = v.m
    bore_floor = m.TAILINGS_CELL[1] * 16 + m.BORE[0]
    rim = m.RIM
    low_board_top = m.BOARD_Y[1] + m.BOSS[1] + m.E
    yoke_low = m.yoke_bottom() - m.E
    # the spout: its floor flush with the slope's foot and over the concentrate chute's bore floor, its sides
    # and head inside the chute's bore, its lip in the chute's mouth
    cb = (m.CONCENTRATE_CELL[1] * 16 + m.BORE[0], m.CONCENTRATE_CELL[1] * 16 + m.BORE[1])
    cx = (m.CONCENTRATE_CELL[0] * 16 + m.BORE[0], m.CONCENTRATE_CELL[0] * 16 + m.BORE[1])
    (hop,) = v.named("frame", r"^hutch_hopper$")
    hop_foot = max(c[1] for c in corners(hop) if c[2] < m.HOPPER_Z[0] + 0.01)
    spigot_ok = (abs(hop_foot - m.SPIGOT_FLOOR) < 1e-6 and cb[0] < m.SPIGOT_FLOOR and m.SPIGOT_Y[1] < cb[1]
                 and cx[0] <= m.SPIGOT_X[0] and m.SPIGOT_X[1] <= cx[1] and m.SPIGOT_Z[0] < m.CONC_Z1 <= m.SPIGOT_Z[1])
    print(f"levels: weir {m.WEIR_Y} over the tailings chute's bore floor {bore_floor}, under the rim {rim}; the sieve's top "
          f"{m.SIEVE_Y[1]}, the plunger board's boss up to {low_board_top:.2f}, the yoke down to {yoke_low:.2f} over the "
          f"water's highest {m.WEIR_Y + abs(m.WATER_PLUNGE_E):.2f}; the partition open {m.FLOOR[1]}..{m.PART_Y0} under the "
          f"board's lowest {m.BOARD_Y[0] - m.E}; the hutch's slope down to {hop_foot:.2f} at the spout, its floor "
          f"{m.SPIGOT_FLOOR} over the concentrate chute's bore floor {cb[0]}")
    if not (bore_floor < m.WEIR_Y < rim - 1.0):
        v.fail("the weir does not overflow into the tailings chute under the rim")
    if not (m.SIEVE_Y[1] + (m.BED_Y[1] - m.BED_Y[0]) + abs(m.BED_E) < m.WEIR_Y - abs(m.WATER_SIEVE_E) - 1.0):
        v.fail("the bed on the sieve is not under the water through the stroke")
    if not low_board_top < m.WEIR_Y - abs(m.WATER_PLUNGE_E) - 1.0:
        v.fail("the plunger board leaves the water")
    # the yoke and the plunger compartment's surface move together, so the gap between them is the least
    gap = min((m.yoke_bottom() + m.E * math.sin(a)) - (m.WEIR_Y + m.WATER_PLUNGE_E * math.sin(a)) for a in
              (TAU * i / 48 for i in range(48)))
    if gap < 0.3:
        v.fail(f"the yoke comes within {gap:.2f} of the water")
    if not (max(m.HOPPER_Y) + 2.0 < m.PART_Y0 < m.BOARD_Y[0] - m.E - 2.0):
        v.fail("the partition does not leave the water a way under it, below the plunger's stroke")
    if not spigot_ok:
        v.fail("the spout does not drain the hutch's slope into the concentrate chute's bore")
    for pid, amp in (("watersieve", m.WATER_SIEVE_E), ("waterplunger", m.WATER_PLUNGE_E), ("bed", m.BED_E)):
        drv = next(p for p in v.parts if p["id"] == pid)["drivers"]
        if len(drv) != 1 or drv[0]["type"] != "slide" or abs(drv[0]["amplitude"] * 16 - amp) > 1e-4:
            v.fail(f"{pid} does not answer the stroke by {amp:.3f}")
    if not (m.WATER_SIEVE_E < 0 < m.WATER_PLUNGE_E and m.BED_E < 0):
        v.fail("the sieve's water and the bed must rise as the plunger falls, the plunger's water fall with it")
    for pid in ("watersieve", "waterplunger"):
        (el,) = v.by_part[pid]
        lo, hi = el.aabb()
        if abs(hi[1] - m.WEIR_Y) > 1e-6:
            v.fail(f"{pid}'s surface is not at the weir's level")


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


def first_below(v, p, pose, skip=("watersieve", "waterplunger")):
    """The first element a grain dropped from p falls on (the water's surface is not one)."""
    state = pose[1]
    best = None
    for pid in v.shown(state):
        if pid in skip:
            continue
        for el in v.posed(pid, pose):
            lo, hi = el.aabb()
            if not (lo[0] <= p[0] <= hi[0] and lo[2] <= p[2] <= hi[2] and lo[1] <= p[1] + 1e-9):
                continue
            t = ray_down(el, p)
            if t is not None and (best is None or t < best[0]):
                best = (t, el.name)
    return best


def flows(v):
    """(label, a point just past a lip, the receiver it must fall onto first (element name regex), poses): the
    infeed onto the feed trough, the trough's lip onto the sieve (or the bed on it) at its east end, the
    weir's lip into the tailings chute's mouth, the spigot into the concentrate chute's."""
    m = v.m
    out = []
    cx = m.INFEED_CELL[0] * 16 + 8.0
    cz = m.INFEED_CELL[2] * 16 + 8.0
    rest = lambda st: [(0.0, st)]                                   # noqa: E731
    turn = lambda st: [(a, st) for a in (0.0, 1.1, 2.6, 3.9, 5.2)]   # noqa: E731
    for dx, dz in ((-2.9, -2.9), (0.0, 0.0), (2.9, 2.9), (-2.9, 2.9)):
        out.append((f"the infeed chute onto the feed trough ({dx:+}, {dz:+})", [cx + dx, m.INFEED_Y0 - 0.05, cz + dz],
                    r"^infeed_trough_(floor|end)$", rest("complete")))
    lip = [m.TROUGH_X[1] + 0.1, m.trough_floor(m.TROUGH_X[1]) - 0.2, cz]
    out.append(("the feed trough's lip onto the sieve", lip, r"^sieve_mesh$", rest("complete")))
    out.append(("the feed trough's lip onto the bed", lip, r"^bed_ore$", turn("working")))
    for z in (m.WEIR_Z[0] + 0.3, sum(m.WEIR_Z) / 2, m.WEIR_Z[1] - 0.3):
        out.append((f"the weir into the tailings chute at z {z:.1f}", [m.LIP_X0 - 0.05, m.WEIR_Y, z], r"^outlet_tailchute_bottom$",
                    rest("complete")))
    for x in (m.SPIGOT_X[0] + m.SPIGOT_T + 0.2, sum(m.SPIGOT_X) / 2, m.SPIGOT_X[1] - m.SPIGOT_T - 0.2):
        out.append((f"the spout's lip into the concentrate chute at x {x:.1f}", [x, m.SPIGOT_FLOOR - 0.01, m.SPIGOT_Z[0] - 0.05],
                    r"^outlet_concchute_bottom$", rest("complete")))
    for x, z in ((m.IN_X[0] + 1.0, m.SIEVE_Z[0] + 1.0), (9.0, 10.0), (m.IN_X[1] - 1.0, m.SIEVE_Z[1] - 1.0)):
        out.append((f"through the sieve onto the hutch's slope at (x {x}, z {z})", [x, m.SIEVE_Y[0] - 0.05, z],
                    r"^hutch_hopper$", rest("complete")))
    out.append(("the trough's lip clears the bearers", [m.TROUGH_X[1] + 0.1, m.trough_floor(m.TROUGH_X[1]) - 0.2, cz - 2.5],
                r"^sieve_mesh$", rest("complete")))
    return out


def check_infeed_drop(v):
    """The infeed chute's mouth stands clear over the feed trough: from its walls' foot down to the highest
    point of the trough's boards under the chute's section, at least INFEED_DROP of open fall, and nothing
    else of the machine in that space."""
    m = v.m
    chute = v.named("infeed", r"^infeed_chute_")
    mouth = min(e.aabb()[0][1] for e in chute)
    clo = [min(e.aabb()[0][k] for e in chute) for k in range(3)]
    chi = [max(e.aabb()[1][k] for e in chute) for k in range(3)]
    under = []
    for pid in v.shown("complete"):
        for e in v.posed(pid, (0.0, "complete")):
            lo, hi = e.aabb()
            if e.name.startswith("infeed_chute_") or hi[1] > mouth + 1e-6:
                continue
            if lo[0] < chi[0] and hi[0] > clo[0] and lo[2] < chi[2] and hi[2] > clo[2]:
                under.append((hi[1], e.name))
    top, name = max(under)
    gap = mouth - top
    print(f"infeed drop: the chute's mouth at y {mouth:.2f}, the highest thing under it {name} at {top:.2f}: "
          f"{gap:.2f} of open fall (at least {m.INFEED_DROP})")
    if not name.startswith("infeed_trough_"):
        v.fail(f"the first thing under the infeed chute is {name}, not the feed trough")
    if gap < m.INFEED_DROP - 1e-6:
        v.fail(f"the infeed chute's mouth is {gap:.2f} over the feed trough, under {m.INFEED_DROP}")


def check_flows(v):
    n = 0
    fl = flows(v)
    for label, p, rx, poses in fl:
        for pose in poses:
            hit = first_below(v, p, pose)
            n += 1
            if hit is None or not re.search(rx, hit[1]):
                v.fail(f"flow: {label} from {[round(c, 2) for c in p]} at {pose} lands on {hit[1] if hit else 'nothing'}, not {rx}")
                break
    print(f"flows: {len(fl)} lips, {n} drops: the feed onto the trough and off its lip onto the sieve's east end, over the "
          "weir into the tailings chute, through the sieve onto the hutch's slope, off the spout's lip into the concentrate chute")
    # the weir is the only way out over the walls: the feed lands the sieve's length from it
    run = v.m.TROUGH_X[1] - (v.m.HUTCH_X[0] + v.m.WALL_T)
    if run < 8.0:
        v.fail(f"the feed lands only {run:.1f} from the weir")


def corners(el):
    """An element's eight corners, posed (build frame)."""
    out = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                loc = [sx * abs(el.size[0]) / 2, sy * abs(el.size[1]) / 2, sz * abs(el.size[2]) / 2]
                out.append([el.c[k] + sum(el.r[k][i] * loc[i] for i in range(3)) for k in range(3)])
    return out


GRAIN = 0.3                                    # the half-size of the probe walked along an outlet's path


def outlet_paths(v):
    """Each product's way out, from where it collects to its chute's face: (label, anchor key, points). The
    tailings collect on the bed at the tail end, rise through the water over the weir's lip, pass the port in
    the head and run down the tailings chute's bore to the west face; the concentrate falls through the sieve
    onto the hutch's slope, runs down it into the spout, off its lip into the concentrate chute and along its
    bore to the north face."""
    m = v.m
    zc = sum(m.WEIR_Z) / 2
    bed_top = m.BED_Y[1] + abs(m.BED_E)
    tb = m.TAILINGS_CELL[1] * 16 + m.BORE[0]
    tail = [[m.IN_X[0] + 1.5, bed_top + 0.4, zc],          # on the bed at the tail end
            [m.IN_X[0] + 0.8, m.WEIR_Y + GRAIN + 0.05, zc],  # up to the lip's height, inside the wall
            [m.LIP_X0 - GRAIN - 0.05, m.WEIR_Y + GRAIN + 0.05, zc],   # over the lip, through the port, past it
            [m.LIP_X0 - GRAIN - 0.05, tb + GRAIN + 0.05, zc],         # off the lip onto the chute's floor
            [0.0, tb + GRAIN + 0.05, zc]]                   # along the bore to the face
    xc = sum(m.SPIGOT_X) / 2
    cb = m.CONCENTRATE_CELL[1] * 16 + m.BORE[0]
    zm = 10.0
    slope_at = lambda z: m.HOPPER_Y[0] + (m.HOPPER_Y[1] - m.HOPPER_Y[0]) * (z - m.HOPPER_Z[0]) / (m.HOPPER_Z[1] - m.HOPPER_Z[0])  # noqa: E731
    conc = [[xc, m.SIEVE_Y[0] - GRAIN - 0.05, zm],              # under the sieve
            [xc, slope_at(zm) + GRAIN + 0.1, zm],               # down onto the slope
            [xc, m.SPIGOT_FLOOR + GRAIN + 0.1, m.SPIGOT_Z[1]],    # down it to the spout
            [xc, m.SPIGOT_FLOOR + GRAIN + 0.1, m.SPIGOT_Z[0] - GRAIN - 0.05],   # through the spout, past its lip
            [xc, cb + GRAIN + 0.05, m.SPIGOT_Z[0] - GRAIN - 0.05],   # off it onto the chute's floor
            [xc, cb + GRAIN + 0.05, 0.0]]                       # along the bore to the face
    return [("tailings", "tailings", tail), ("concentrate", "concentrate", conc)]


def probe_hits(v, p, pose, skip):
    g = GRAIN
    probe = v.m.box([p[k] - g for k in range(3)], [p[k] + g for k in range(3)], "probe", "probe", "iron")
    plo, phi = probe.aabb()
    for pid in v.shown(pose[1]):
        if pid in skip:
            continue
        for el in v.posed(pid, pose):
            lo, hi = el.aabb()
            if all(lo[k] < phi[k] and plo[k] < hi[k] for k in range(3)) and obb_obb(probe, el, eps=0.0):
                return el.name
    return None


def check_paths(v):
    """Each outlet has an open way from where its product collects to its chute's face: a grain (a probe
    GRAIN each way) walked along the path in steps of a tenth of a voxel touches nothing but the water and the
    bed, through a turn; the path ends inside its chute's bore on its anchor's face; the tailings' path stays
    over the sieve and the concentrate's under it until it leaves the hutch, so they leave by separate ways."""
    m = v.m
    skip = ("watersieve", "waterplunger", "bed")
    poses = [(a, "working") for a in (0.0, 1.6, 3.1, 4.7)]
    n = 0
    for label, key, pts in outlet_paths(v):
        cell, face = v.rig[f"{key}Cell"], v.rig[f"{key}Face"]
        blocked = None
        for a, b in zip(pts, pts[1:]):
            steps = max(1, int(math.ceil(math.dist(a, b) / 0.1)))
            for i in range(steps + 1):
                p = [a[k] + (b[k] - a[k]) * i / steps for k in range(3)]
                for pose in poses:
                    n += 1
                    hit = probe_hits(v, p, pose, skip)
                    if hit:
                        blocked = (p, hit, pose)
                        break
                if blocked:
                    break
            if blocked:
                break
        if blocked:
            p, hit, pose = blocked
            v.fail(f"path: the {label} way out is blocked at {[round(c, 2) for c in p]} by {hit} at {pose}")
        end = pts[-1]
        ax, side = {"west": (0, cell[0] * 16.0), "north": (2, cell[2] * 16.0), "east": (0, cell[0] * 16.0 + 16),
                    "south": (2, cell[2] * 16.0 + 16)}[face]
        across = [k for k in range(3) if k != ax]
        in_bore = all(cell[k] * 16 + m.BORE[0] + GRAIN <= end[k] <= cell[k] * 16 + m.BORE[1] - GRAIN for k in across)
        if abs(end[ax] - side) > 1e-6 or not in_bore:
            v.fail(f"path: the {label} way out does not end in its chute's bore on the {face} face of {cell}")
        inside = [p for p in pts if m.HUTCH_X[0] + m.WALL_T <= p[0] and m.HUTCH_Z[0] + m.WALL_T <= p[2]]
        if label == "tailings" and min(p[1] for p in inside) - GRAIN < m.SIEVE_Y[1]:
            v.fail("path: the tailings' way out goes under the sieve")
        if label == "concentrate" and max(p[1] for p in inside) + GRAIN > m.SIEVE_Y[0]:
            v.fail("path: the concentrate's way out goes over the sieve")
        print(f"path: {label} from {[round(c, 2) for c in pts[0]]} to {[round(c, 2) for c in end]} on {cell} {face}, "
              f"{len(pts) - 1} legs: {'open' if not blocked else 'BLOCKED'}")
    print(f"paths: {n} probes of a grain {2 * GRAIN} across, the water and the bed not counted")


# ---------------------------------------------------------------- mechanism
def check_yoke(v):
    """The sheave in its yoke: at every step of a turn the sheave's centre is on the yoke's slot line (the
    yoke follows it exactly: its bars stay tangent to the sheave's flats' circle) and inside the slot's
    length; the plunger moves E sin theta, the same stroke either way the axle turns."""
    m = v.m
    rc = m.SHEAVE_RC
    centre = (m.SHEAVE_X, m.SHAFT[0], m.SHAFT[1] - m.E)
    worst = 0.0
    for pose in turns(48, "complete"):
        p = v.point("sheave", pose, centre)
        bars = {e.name.rsplit("_", 1)[1]: e.aabb() for e in v.named("plunger", r"^plunger_yoke_(top|bottom|n|s)$", pose)}
        inner_lo, inner_hi = bars["bottom"][1][1], bars["top"][0][1]
        mid = (inner_lo + inner_hi) / 2
        worst = max(worst, abs(p[1] - mid))
        half = (inner_hi - inner_lo) / 2
        end_lo, end_hi = bars["n"][1][2], bars["s"][0][2]
        if abs(p[1] - mid) > 1e-6 or abs(half - rc - 0.01) > 1e-6 or not (end_lo + rc < p[2] < end_hi - rc):
            v.fail(f"the sheave is off its yoke's slot at {pose} ({p[1]:.4f} vs {mid:.4f}, across {p[2]:.3f} in "
                   f"{end_lo:.3f}..{end_hi:.3f})")
            break
    for th in (0.4, 1.3, 2.9):
        a = v.point("plunger", (th,), [0.0, 0.0, 0.0])[1]
        b = v.point("plunger", (-th,), [0.0, 0.0, 0.0])[1]
        if abs(a - m.E * math.sin(th)) > 1e-6 or abs(a + b) > 1e-9:
            v.fail(f"the plunger is not at E sin theta at {th}")
    lo, hi = aabb_of(v.by_part["sheave"])
    shaft = (m.SHAFT_R / math.cos(math.pi / 8))
    if m.SHEAVE_R - m.E < shaft + 0.1:
        v.fail("the sheave does not enclose the shaft off its centre")
    print(f"yoke: the sheave on its slot line through a turn (worst {worst:.1e}), the bars {rc + 0.01:.3f} off its centre "
          f"(its corners {rc:.3f}), inside the slot's length; the plunger at {m.E} sin theta either way; the sheave "
          f"{m.SHEAVE_R} across its flats round a shaft {shaft:.2f} out to its corners, {m.E} off centre")


def check_supports(v):
    """The shaft in its two bushes, located by a collar or the flange on both faces of each; the tail rod in
    its guide and through the beam, and the plunger board inside its compartment, over a turn; the standards
    on the side walls' rims and the beam between their heads."""
    m = v.m
    frame = v.by_part["stand"]
    found, _ = supports(frame, v.named("shaft", r"_rod"), 0, m.SHAFT)
    bushes = sorted(n for n in found if re.match(r"^stand_bush_[we]$", n))
    print(f"support shaft: {len(bushes)} bush(es) {', '.join(bushes) or 'NONE'}")
    if len(bushes) < 2:
        v.fail("the shaft is not carried by two bushes")
    for tag, (b0, b1) in zip("we", m.bushes()):
        near = []
        for el in v.by_part["shaft"]:
            if not re.search(r"_(collar\d|flange)_", el.name):
                continue
            lo, hi = el.aabb()
            if abs(hi[0] - b0) < 0.05 or abs(lo[0] - b1) < 0.05:
                near.append("west" if abs(hi[0] - b0) < 0.05 else "east")
        if set(near) != {"west", "east"}:
            v.fail(f"the shaft is not located on both faces of the {tag} bush (found {sorted(set(near))})")
    for pose in turns(24, "complete"):
        rlo, rhi = v.named("plunger", r"_tail$", pose)[0].aabb()
        glo, ghi = v.named("stand", r"_guide$")[0].aabb()
        blo, bhi = v.named("stand", r"_beam$")[0].aabb()
        if not (rlo[1] < blo[1] and rhi[1] > ghi[1] and glo[0] < rlo[0] and rhi[0] < ghi[0] and glo[2] < rlo[2] and rhi[2] < ghi[2]):
            v.fail(f"the tail rod leaves its guide at {pose}")
            break
        plo, phi = v.named("plunger", r"_board$", pose)[0].aabb()
        if not (m.IN_X[0] < plo[0] and phi[0] < m.IN_X[1] and m.PLUNGE_Z[0] < plo[2] and phi[2] < m.PLUNGE_Z[1]
                and plo[1] > m.PART_Y0):
            v.fail(f"the plunger board leaves its compartment at {pose}")
            break
    for tag, (x0, x1) in zip("we", m.POSTS_X):
        (post,) = v.named("stand", rf"^stand_post_{tag}$")
        lo, hi = post.aabb()
        if abs(lo[1] - m.RIM) > 1e-6 or abs(lo[0] - x0) > 1e-6 or abs(hi[0] - x1) > 1e-6:
            v.fail(f"the {tag} standard is not on its wall's rim")
    print("support: the shaft located at both bushes; the tail rod in its guide and the plunger board in its compartment "
          "through a turn; the standards on the side walls' rims")


# ---------------------------------------------------------------- the hutch's corner irons
def gap(a, b):
    """How far apart two boxes (lo, hi) are: the largest separation along an axis, 0 or less if they meet."""
    (alo, ahi), (blo, bhi) = a, b
    return max(max(blo[k] - ahi[k], alo[k] - bhi[k]) for k in range(3))


def check_corner_straps(v):
    """Every vertical corner of the hutch (found from its walls) is bound by an angle-iron strap: a leg flat on
    each of its two outer faces, the legs meeting at the corner, from the sills' top to the rim (up the head to
    its top where the head stands over the corner), each leg nailed; on each corner at the rim (not the head's), a
    corner iron on the rim lapping the strap's head, nailed too. The irons stand clear (0.1 or more) of the outlets,
    the infeed, the water inlet, and the shaft and plunger through a turn."""
    m = v.m
    frame = v.by_part["frame"]
    walls = [e.aabb() for e in frame if re.match(r"^hutch_(wall|head)_", e.name)]
    x0, x1 = min(lo[0] for lo, _ in walls), max(hi[0] for _, hi in walls)
    z0, z1 = min(lo[2] for lo, _ in walls), max(hi[2] for _, hi in walls)
    sill_top = max(e.aabb()[1][1] for e in frame if e.name.startswith("hutch_sill"))
    straps = {e.name: e.aabb() for e in frame if e.name.startswith("hutch_strap_")}
    eps = 1e-6
    found = []

    def corner_top(xc, zc):
        return max(hi[1] for lo, hi in walls if lo[0] - eps <= xc <= hi[0] + eps and lo[2] - eps <= zc <= hi[2] + eps)

    rim = min(corner_top(xc, zc) for xc in (x0, x1) for zc in (z0, z1))   # the hutch's rim: a corner over it is the head's
    for xc, sx in ((x0, -1), (x1, 1)):
        for zc, sz in ((z0, -1), (z1, 1)):
            # the corner's height: the walls' top at it
            top = corner_top(xc, zc)
            where = f"({xc:g}, {zc:g})"
            # the leg on the face looking along z, flat on it, lapping the corner; the leg on the face looking along x
            lz = [(n, b) for n, b in straps.items() if "nail" not in n and "_rim" not in n
                  and abs((b[0][2] if sz > 0 else b[1][2]) - zc) < eps and b[0][0] - eps <= xc <= b[1][0] + eps]
            lx = [(n, b) for n, b in straps.items() if "nail" not in n and "_rim" not in n
                  and abs((b[0][0] if sx > 0 else b[1][0]) - xc) < eps and b[0][2] - eps <= zc <= b[1][2] + eps]
            if len(lz) != 1 or len(lx) != 1:
                v.fail(f"the hutch's corner {where} has no angle-iron strap (legs {[n for n, _ in lz + lx]})")
                continue
            (nz, bz), (nx, bx) = lz[0], lx[0]
            # the legs meet: they touch along the corner, overlapping over their height
            if abs(gap(bz, bx)) > eps or not (min(bz[1][1], bx[1][1]) - max(bz[0][1], bx[0][1]) > top - sill_top - eps):
                v.fail(f"the strap's legs at {where} do not meet along the corner ({nz}, {nx})")
            # outside the hutch, wrapping it: the z-face leg reaches past the corner by the other's thickness
            if not ((bz[1][0] >= bx[1][0] - eps) if sx > 0 else (bz[0][0] <= bx[0][0] + eps)):
                v.fail(f"the strap at {where} does not wrap the corner")
            for n, b in ((nz, bz), (nx, bx)):
                if b[0][1] > sill_top + eps or b[1][1] < top - eps:
                    v.fail(f"{n} does not run from the sills' top {sill_top} to the corner's top {top}: y {b[0][1]}..{b[1][1]}")
                nails = [k for k, nb in straps.items() if "nail" in k and "_rim" not in k and abs(gap(nb, b)) < eps]
                if len(nails) < 2:
                    v.fail(f"{n} is not nailed (nails on it: {nails})")
            # the rim's corner: covered by the head, or an iron on it lapping the strap's head
            covered = top > rim + eps
            if not covered:
                arms = [n for n, b in straps.items() if "_rim" in n and "nail" not in n and abs(b[0][1] - top) < eps
                        and b[0][0] - eps <= xc <= b[1][0] + eps and b[0][2] - eps <= zc <= b[1][2] + eps]
                if not arms or not all(abs(gap(straps[a], leg)) < eps for a in arms for leg in (bz, bx)):
                    v.fail(f"the rim's corner at {where} has no corner iron over it lapping the strap's head ({arms})")
                arms += [n for n, b in straps.items() if "_rim" in n and "nail" not in n and n not in arms
                         and any(abs(gap(b, straps[a])) < eps for a in arms)]
                rnails = [k for k, nb in straps.items() if "nail" in k and any(
                    abs(gap(nb, straps[a])) < eps and nb[0][1] >= straps[a][1][1] - eps for a in arms)]
                if len(rnails) < 2:
                    v.fail(f"the rim's corner iron at {where} is not nailed ({rnails})")
            found.append(f"{where} {nz}/{nx} to {top:g}" + (" (head over it)" if covered else " + rim iron"))
    print("corner straps: " + "; ".join(found))
    if len(found) != 4:
        v.fail("not every corner of the hutch is strapped")
    # clear of the services and the moving parts
    near = []
    others = [(e.name, e.aabb()) for pid in ("outlets", "infeed", *m.PIPES) for e in v.by_part[pid]]
    for pose in turns(24, "complete"):
        others += [(e.name, e.aabb()) for pid in ("shaft", "sheave", "plunger") for e in v.posed(pid, pose)]
    worst = (1e9, None)
    for n, b in straps.items():
        for on, ob in others:
            g = gap(b, ob)
            if g < worst[0]:
                worst = (g, (n, on))
            if g < 0.1 - 1e-6:
                near.append((n, on, round(g, 3)))
    print(f"corner straps clear of the outlets, infeed, inlet, shaft and plunger by {worst[0]:.2f} at least ({worst[1]})")
    if near:
        v.fail(f"corner irons too near a service or moving part: {sorted(set(near))[:6]}")


# ---------------------------------------------------------------- clearances
# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels at every sampled pose.
ALLOWED = [
    # the shaft in its bushes and through its standards, the collars and the flange against the bushes
    ("shaft", r"_rod|_collar|_flange", "stand", r"_bush_|_post_"),
    ("sheave", None, "shaft", r"_rod"),
    # the sheave in its yoke; the tail rod in its guide and through the beam
    ("sheave", None, "plunger", r"_yoke"),
    ("plunger", r"_tail$", "stand", r"_guide$|_beam$"),
    # the water inlet through the back wall; the water's surface against the walls, the partition, the
    # plunger's rod, and the weir's lip and the hutch under the notch (it stands at the sill)
    (r"pipecopper|pipelead", None, "frame", r"_wall_s$|_floor\d$"),
    (r"watersieve|waterplunger", None, "frame", r"_wall|_partition$|_weir_lip$"),
    ("waterplunger", None, "stand", r"_strap_"),
    ("waterplunger", None, "plunger", r"_rod$"),
    # the bed on the sieve, against the walls and the partition
    ("bed", None, "sieve|frame", None),
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
                if pa == pb or (moving_only and pa not in v.m.MOVING and pb not in v.m.MOVING):
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
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {p[0]:.3f} ({p[1]})" for (a, b), p in ex))
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
    check_build_stages(v)
    check_textures(v)
    check_thickness(v)
    check_floating(v)
    check_containment(v, [(a, "all") for a in (0.0, 0.7, 1.6, 2.5, 3.3, 4.1, 4.71, 5.0, 5.8, -1.2)])
    check_anchors(v)
    check_levels(v)
    check_infeed_drop(v)
    check_flows(v)
    check_paths(v)
    check_yoke(v)
    check_supports(v)
    check_corner_straps(v)
    check_clearances(v, [p for st in ("working", "complete") for p in turns(12 if quick else 24, st)]
                     + [(-0.9, "working"), (-2.4, "working")], "clearances")
    if not quick:
        check_clearances(v, turns(96, "working"), "swept paths (every 3.75 degrees of the axle)", moving_only=True)
        check_zfight(v)
    return v.ok


def validate_files(m, shape, frame_shape, ship):
    """The written files: every texture declared; lids over every column; four cells, none hollow, each with
    boxes."""
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL textures used but not declared: {missing}")
        ok = False
    gaps = lid_gaps(ship["cells"])
    bad = [c["pos"] for c in ship["cells"] if c.get("hollow") or not c.get("boxes")]
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, "
          f"boxes per cell {[len(c['boxes']) for c in ship['cells']]}; lids over every column: {'yes' if not gaps else gaps}")
    if gaps or bad or len(ship["cells"]) != m.CELLS_X * m.CELLS_Y * m.CELLS_Z:
        print(f"FAIL the cells: lid gaps {gaps}, without boxes {bad}")
        ok = False
    return ok
