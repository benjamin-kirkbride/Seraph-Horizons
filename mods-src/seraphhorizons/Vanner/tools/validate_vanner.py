"""The vanner generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its
rule. Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in
make_shape.py proves the shipped files are this model moved.

A pose is (theta,), (theta, travel) or (theta, travel, state): travel None or left out is |theta| (turned
from rest), the state one of make_shape.STATES (what is fitted; "all" when left out), and the clearance,
drop, floating and z-fighting checks look only at the parts that show in it.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb, supports
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import part_of, posed

TAU = 2 * math.pi
MOVING = ("countershaft", "shaker", "uprightw", "uprighte", "belt", "head", "foot", "worm")


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
        key = (pid, pose[0], pose[1] if len(pose) > 1 else None)
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


def turns(n=24, state="all", travel_extra=0.0):
    """A turn of the axle in n steps, in a state (turned from rest, plus `travel_extra` of travel)."""
    return [(TAU * i / n, TAU * i / n + travel_extra, state) for i in range(n + 1)]


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
    """Every part is the frame's, one of REQUIRES' or a water line's; the water line's metals are the same
    elements; a part riding another is fitted only with it in every state; the states build up."""
    m = v.m
    reqs = {p["requires"] for p in v.parts}
    if reqs != set(m.REQUIRES) | set(m.PIPES) | {None}:
        v.fail(f"the parts' requires are {reqs}, want the frame's (null), {m.REQUIRES} and {m.PIPES}")
    for pid in m.PIPES[1:]:
        a = sorted((e.name[len(pid):], e.c, e.size) for e in v.by_part[pid])
        b = sorted((e.name[len(m.PIPES[0]):], e.c, e.size) for e in v.by_part[m.PIPES[0]])
        if a != b:
            v.fail(f"{pid} is not {m.PIPES[0]}'s elements in another metal")
    for p in v.parts:
        if p.get("ride"):
            ride = next(q for q in v.parts if q["id"] == p["ride"])
            for st, fit in m.STATES.items():
                if p["requires"] in fit and ride["requires"] not in (None, *fit):
                    v.fail(f"in {st}, {p['id']} rides {ride['id']}, which is not fitted")
    counts = {st: sum(len(v.by_part[pid]) for pid in v.shown(st)) for st in m.STATES}
    print("states: " + ", ".join(f"{st} {n}" for st, n in counts.items()) + f"; the water line {len(v.by_part[m.PIPES[0]])} a metal")


ROLES = [
    # the frame: timber, castings and straps, boards, the chute sections
    (r"^fr_(sill|pedestal|post_|spraypost)", {"oak"}),
    (r"^fr_(cb|tb)_bearer", {"oak"}),
    (r"^(fr|sk)_up[we]\d_pin", {"steel"}),
    (r"^(fr|sk)_up[we]\d_cheek", {"iron"}),
    (r"^fr_(bearing|saddle|bracket|spraybracket)", {"iron"}),
    (r"^fr_(cb|tb)_", {"planks"}),
    (r"^fr_chute_", {"chute"}),
    # the countershaft continues the vanilla axle in oak, then steel
    (r"^cs_entry[ab]$", {"oak"}),
    (r"^cs_", {"steel"}),
    # the shaking frame
    (r"^sk_(rail|tie)", {"oak"}),
    (r"^sk_", {"iron"}),
    (r"^up[we]_", {"oak"}),
    (r"^belt_lace", {"lacing"}),
    (r"^belt_", {"leather"}),
    (r"^(head|foot)_drum", {"oak"}),
    (r"^(head|foot)_", {"steel"}),
    (r"^worm_", {"steel"}),
    (r"^pawl_post", {"oak"}),
    (r"^pawl_", {"iron"}),
    (r"^fb_chute_", {"chute"}),
    (r"^fb_", {"planks"}),
    # the water line, in each metal: the pipe in its metal's texture, the joints, the reducer and the cap in solder
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
    solder = {el.name for el in v.els if el.part in v.m.PIPES for f in el.faces.values() if f["texture"] == "#solder"}
    want = {n for n in solder if re.search(r"_(reducer|spray_cap|link|run|inlet|elbow|plug|spray|nozzle)", n)}
    print(f"textures by role: {len(v.els) - len(bad)} of {len(v.els)} elements as their role says; {len(solder)} in solder")
    if bad:
        v.fail(f"textures off their role: {bad[:6]}")
    if any(not re.search(r"_(reducer|spray_cap)", n) for n in want):
        v.fail(f"solder on pipe that is not a joint, the reducer or the cap: {sorted(want)[:4]}")


def check_thickness(v):
    """No part thinner than about a voxel: every element at least 0.6 across in its two larger dimensions'
    smaller one, but for the listed thin-by-nature pieces (the belt, gear teeth, the worm's thread, joints)."""
    thin_ok = re.compile(r"(belt_|tooth|thread|reducer|_cap|joint)")
    bad = []
    for el in v.els:
        s = sorted(abs(x) for x in el.size)
        if s[1] < 0.6 and not thin_ok.search(el.name):
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
    """At rest, in each state (the frame alone, then each part added), what shows is one piece from the
    ground (moving parts included: a shaft is joined through its bearings, the shaking frame through its
    slides)."""
    m = v.m
    for state in m.STATES:
        els = [e for pid in v.shown(state) for e in v.posed(pid, (0.0, 0.0, state))]
        seen, loose = frame_floating(els)
        print(f"floating ({state}): {len(seen)} of {len(els)} elements joined to the ground")
        if loose:
            v.fail(f"in {state} these float: {loose[:8]}")


# ---------------------------------------------------------------- anchors and drops
FACE_AXIS = {"north": 2, "south": 2, "east": 0, "west": 0, "up": 1, "down": 1}


def vanilla_profile(m):
    """VANILLA_CHUTE's walls as a section through a face has them over the two axes across it. The game's
    shape runs along x with its walls over (y, z); turned to run along z its walls lie over (x, y), and
    along y over (x, z): the same numbers, either way round (the section is square and centred, so
    mirroring and which axis comes first do not change it)."""
    walls = set()
    for a0, a1, b0, b1 in m.VANILLA_CHUTE["walls"]:
        walls.add((a0, a1, b0, b1))
        walls.add((b0, b1, a0, a1))
    return walls


def chute_ring(walls):
    """Whether walls (cell-local rectangles over a face, whole voxels) tile exactly an 8 x 8 tube 1 thick
    centred in the face: every voxel of the ring once, nothing else."""
    cover = {}
    for a0, a1, b0, b1 in walls:
        if any(v != int(v) for v in (a0, a1, b0, b1)):
            return False
        for a in range(int(a0), int(a1)):
            for b in range(int(b0), int(b1)):
                cover[(a, b)] = cover.get((a, b), 0) + 1
    ring = {(a, b) for a in range(4, 12) for b in range(4, 12) if not (5 <= a < 11 and 5 <= b < 11)}
    return set(cover) == ring and all(n == 1 for n in cover.values())


def check_anchors(v):
    """The power face meets the countershaft's oak cross at the power cell's centre in the vanilla axle's
    profile; the water line's inlet is open on the water face's centre in ppex's 6 x 6 tube; each chute
    section is vanilla's (its walls exactly the game's, at the face's centre, flush with the face, in the
    chute's texture) at its cell's face; the feed box takes the infeed's foot; the five cells differ."""
    m = v.m
    rig = v.rig
    lo, hi = aabb_of(v.named("countershaft", r"^cs_entry[ab]$"))
    pc = m.POWER_CELL
    x, y = m.CS
    centred = (x - pc[0] * 16, y - pc[1] * 16) == (8.0, 8.0) and rig["powerFace"] == "south" and pc[2] == m.CELLS_Z - 1
    print(f"anchors: power cell {pc} {rig['powerFace']}: the axle's oak cross to z {hi[2]:.2f}, axis (x {x}, y {y}), "
          f"{hi[0] - lo[0]:.1f} x {hi[1] - lo[1]:.1f}")
    if not centred or abs(hi[2] - m.Z_LEN) > 1e-6 or abs(hi[0] - lo[0] - 4.0) > 1e-6 or abs(hi[1] - lo[1] - 4.0) > 1e-6 \
            or abs((lo[0] + hi[0]) / 2 - x) > 1e-6 or abs((lo[1] + hi[1]) / 2 - y) > 1e-6:
        v.fail("the countershaft does not meet the power face at the cell's centre in the axle's profile")
    wc = m.WATER_CELL
    ix, iy, iz = m.INLET
    for pid in m.PIPES:
        walls = [e for e in v.named(pid, r"_inlet_") if e.aabb()[1][1] > m.Y_LEN - 1e-6]
        wlo, whi = aabb_of(walls)
        section = (whi[0] - wlo[0], whi[2] - wlo[2])
        centre = ((wlo[0] + whi[0]) / 2, (wlo[2] + whi[2]) / 2)
        face_c = (wc[0] * 16 + 8.0, wc[2] * 16 + 8.0)
        print(f"anchors: water cell {wc} {rig['waterFace']}: {pid}'s inlet open on y {whi[1]:.2f}, ppex's {section[0]:.0f} x "
              f"{section[1]:.0f} tube on (x {centre[0]}, z {centre[1]})")
        plugged = [e.name for e in v.named(pid, r"_inlet_") if e.aabb()[1][1] > m.Y_LEN - 1.5
                   and e.aabb()[0][0] < ix < e.aabb()[1][0] and e.aabb()[0][2] < iz < e.aabb()[1][2]]
        if rig["waterFace"] != "up" or wc[1] != m.CELLS_Y - 1 or section != (6.0, 6.0) or centre != face_c or plugged:
            v.fail(f"{pid}'s inlet is not open to a ppex pipe end to end on the water face's centre")
    cells = {"power": pc, "water": wc}
    for label, part, prefix, face, cell, key in (
            ("concentrate", "frame", r"^fr_chute_conc_", m.CONC_FACE, m.CONC_CELL, "concentrate"),
            ("tailings", "frame", r"^fr_chute_tail_", m.TAIL_FACE, m.TAIL_CELL, "tailings"),
            ("infeed", "feedbox", r"^fb_chute_", m.FEED_FACE, m.FEED_CELL, "feed")):
        els = v.named(part, prefix)
        prof, ends = m.chute_profile(els, face, cell)
        axis = FACE_AXIS[face]
        flush = (cell[axis] * 16.0) if face in ("north", "west", "down") else (cell[axis] + 1) * 16.0
        tex = {f["texture"] for e in els for f in e.faces.values()}
        want = vanilla_profile(m)
        print(f"anchors: {label} cell {list(cell)} {face}: the chute section's walls at the face {prof}, flush at {sorted(ends)}")
        if not set(prof) <= want or not chute_ring(prof) or ends != {flush} or tex != {"#chute"} or rig[f"{key}Face"] != face \
                or rig[f"{key}Cell"] != list(cell) or m.TEXTURES["chute"] != m.VANILLA_CHUTE["texture"]:
            v.fail(f"the {label} chute is not vanilla's chute section at its cell's {face} face")
        cells[label] = cell
    game = m.read_vanilla_chute()
    if game is None:
        print("anchors: the game's chute shape is not here; VANILLA_CHUTE is held to it where it is")
    else:
        walls, runs, gtex = game
        ok = walls == sorted(m.VANILLA_CHUTE["walls"]) and runs == {(0.0, 16)} and gtex == m.VANILLA_CHUTE["texture"]
        print(f"anchors: the game's {m.VANILLA_CHUTE['shape']}: walls {walls} the length of the block, texture {gtex}")
        if not ok:
            v.fail("VANILLA_CHUTE is not the game's straight chute")
    # the feed box holds the infeed's foot: its walls round the chute's, its floor under its foot
    clo, chi = aabb_of(v.named("feedbox", r"^fb_chute_"))
    blo, bhi = aabb_of(v.named("feedbox", r"^fb_(w|e|s|weir)$"))
    floor = v.named("feedbox", r"^fb_floor$")[0].aabb()
    if not (blo[0] <= clo[0] and chi[0] <= bhi[0] and blo[2] <= clo[2] and chi[2] <= bhi[2] and floor[1][1] < clo[1] < bhi[1]):
        v.fail("the feed box does not take the infeed chute's foot in its mouth")
    if len({tuple(c) for c in cells.values()}) != len(cells):
        v.fail(f"the anchor cells must differ: {cells}")


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
    state = pose[2] if len(pose) > 2 else "all"
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


def drops(v):
    """Each drop: (label, a point just past a lip, the element it must fall onto first (name regex), poses).
    The infeed into the feed box; the pulp off the apron and the wash water from each nozzle onto the belt,
    as the belt shakes; the concentrate off the head and the tailings off the foot into their boxes; each
    box's spout into its chute section's bore."""
    m = v.m
    out = []
    shake = [(a, None) for a in (0.0, 1.1, math.pi / 2, 2.6, 3.9, -math.pi / 2, 5.2)]
    rest = [(0.0, None)]
    fc = m.FEED_CELL
    out.append(("the infeed chute into the feed box", [fc[0] * 16 + 8.0, m.FEED_CHUTE_Y0 - 0.05, fc[2] * 16 + 8.0],
                r"^fb_floor$", rest))
    for x in (m.APRON_X[0] + 0.3, 10.0, m.APRON_X[1] - 0.3):
        out.append((f"the pulp off the apron at x {x}", [x, m.APRON_LIP_Y - m.APRON_T - 0.05, m.APRON_Z - 0.05],
                    r"^belt_run", shake))
    nb = m.SPRAY[0] - m.SPRAY_HALF - m.NOZZLE[1] - 0.05
    for k, nx in enumerate(m.NOZZLES_X, 1):
        out.append((f"the wash water from nozzle {k}", [nx, nb, m.SPRAY[1]], r"^belt_run", shake))
    hy, hz = m.HEAD
    fy, fz = m.FOOT
    r = m.ROLL_R + m.BELT_T + 0.05
    for x in (m.BELT_X[0] + 0.3, 10.0, m.BELT_X[1] - 0.3):
        out.append((f"the concentrate off the head at x {x}", [x, hy, hz - r], r"^fr_cb_floor$", shake))
        out.append((f"the tailings off the foot at x {x}", [x, fy, fz + r], r"^fr_tb_floor$", shake))
    sx = sum(m.SPOUT_X) / 2
    out.append(("the concentrate box's spout into its chute", [sx, m.SPOUT_Y[1] + 0.05, m.CONC_CHUTE_Z[1] - 1.45],
                r"^fr_chute_conc_", rest))
    out.append(("the tailings box's spout into its chute", [sx, m.SPOUT_Y[1] + 0.05, m.TAIL_CHUTE_Z[0] + 1.45],
                r"^fr_chute_tail_", rest))
    return out


def check_drops(v):
    n = 0
    fl = drops(v)
    for label, p, rx, poses in fl:
        for pose in poses:
            hit = first_below(v, p, pose)
            n += 1
            if hit is None or not re.search(rx, hit[1]):
                v.fail(f"drop: {label} from {[round(c, 2) for c in p]} at {pose} lands on {hit[1] if hit else 'nothing'}, not {rx}")
                break
    print(f"drops: {len(fl)} lips, {n} drops: the infeed into the feed box, the pulp and the wash water onto the belt as it "
          "shakes, the concentrate off the head and the tailings off the foot into their boxes, the spouts into their chutes")


# ---------------------------------------------------------------- mechanism
def angle_about(v, pid, pose, axis):
    mm = v.mat(pid, pose)
    if axis == "x":
        return math.atan2(mm[2][1], mm[1][1])
    return math.atan2(mm[1][0], mm[0][0])


def check_yokes(v):
    """Each sheave in its yoke: at every step of a turn the sheave's centre is on the yoke's slot line (the
    yoke follows it across exactly: its west and east bars stay a hundredth off its corners' circle) and
    the sheave stays inside the slot's length (clear of its top and bottom bars by YOKE_GAP or more)."""
    m = v.m
    rc = m.SHEAVE_RC
    sx, sy = m.sheave_centre()
    worst, tightest = 0.0, 1e9
    for i, z in enumerate(m.SHEAVES_Z, 1):
        for pose in turns(48):
            p = v.point("countershaft", pose, (sx, sy, z))
            bars = {e.name.rsplit("_", 1)[1]: e.aabb() for e in v.named("shaker", rf"^sk_yoke{i}_(w|e|top|bottom)$", pose)}
            inner_w, inner_e = bars["w"][1][0], bars["e"][0][0]
            mid = (inner_w + inner_e) / 2
            worst = max(worst, abs(p[0] - mid))
            half = (inner_e - inner_w) / 2
            room = min(bars["top"][0][1] - (p[1] + rc), (p[1] - rc) - bars["bottom"][1][1])
            tightest = min(tightest, room)
            if abs(p[0] - mid) > 1e-6 or abs(half - rc - 0.01) > 1e-6 or room < m.YOKE_GAP - 1e-6:
                v.fail(f"sheave {i} is off its yoke's slot at {pose} ({p[0]:.4f} vs {mid:.4f}; {room:.3f} to its top or bottom bar)")
                break
    print(f"yokes: {len(m.SHEAVES_Z)} eccentric sheaves, each on its yoke's slot line through a turn (worst {worst:.1e}), "
          f"its bars {rc + 0.01:.3f} off the sheave's centre (its corners {rc:.3f}), at least {tightest:.2f} from the top and bottom bars")


def check_drive(v):
    """The countershaft turns with the axle and the frame follows its eccentrics across (a stroke of 2 E a
    turn) whichever way the axle turns; the ratchet's tooth is the stroke, and the fixed pawl stays just
    under its bottom teeth through the shake; the worm on the wheel's pitch circle, in its plane, the wheel a
    tooth a worm turn; the ratchet, the worm and the rollers turn one way only (with the travel), the belt's
    top run creeping to the head whichever way the axle turns."""
    m = v.m
    a = (0.0, 0.0)
    for th in (0.4, -0.4, math.pi / 2, -math.pi / 2, 3.0):
        d = angle_about(v, "countershaft", (th, abs(th)), "z")
        if abs(math.remainder(d - th, TAU)) > 1e-9:
            v.fail(f"the countershaft turns {d:.4f} for the axle's {th}")
        dx = v.point("shaker", (th, abs(th)), (0.0, 0.0, 0.0))[0]
        if abs(dx - m.E * math.sin(th)) > 1e-9:
            v.fail(f"the shaking frame is at {dx:.4f} for the axle's {th}, not E sin theta")
    tooth = TAU * m.RATCHET_R / m.RATCHET_N
    if abs(tooth - 2 * m.E) > 1e-9:
        v.fail(f"the ratchet's tooth {tooth:.4f} is not the shake's stroke {2 * m.E}")
    wx, wy = m.WORM
    tips = wy - m.ratchet_reach()
    worst_gap, cover = 1e9, True
    for pose in turns(24):
        shift = v.point("shaker", pose, (0.0, 0.0, 0.0))[0]
        plo, phi = v.named("pawl", r"_arm$", pose)[0].aabb()
        rlo, rhi = aabb_of(v.named("worm", r"_ratchet_", pose))
        worst_gap = min(worst_gap, rlo[1] - phi[1], tips - phi[1])
        if not (plo[0] < wx + shift - 0.5 and phi[0] > wx + shift + 0.5 and plo[2] <= m.RATCHET_Z[0] + 0.15 and phi[2] >= m.RATCHET_Z[1] - 0.15):
            cover = False
    if not (0.0 < worst_gap < 0.2) or not cover or abs(tips - v.named("pawl", r"_arm$")[0].aabb()[1][1] - m.PAWL_GAP) > 1e-9:
        v.fail(f"the pawl is not just under the ratchet's bottom teeth through the shake (gap {worst_gap:.3f})")
    d = m.HEAD[0] - wy
    wlo, whi = aabb_of(v.named("head", r"_wheel_"))
    if abs(d - (m.WHEEL_R + m.WORM_R)) > 1e-9 or not (wlo[0] < wx < whi[0]):
        v.fail("the worm is not on its wheel's pitch circle, in its plane")
    b = (0.0, 0.4)
    dw = angle_about(v, "worm", b, "z") - angle_about(v, "worm", a, "z")
    dh = angle_about(v, "head", b, "x") - angle_about(v, "head", a, "x")
    df = angle_about(v, "foot", b, "x") - angle_about(v, "foot", a, "x")
    if abs(abs(dh) * m.WHEEL_N - abs(dw)) > 1e-5 or abs(dh - df) > 1e-12:
        v.fail(f"the worm wheel turns {dh:.6f} as the worm turns {dw:.6f}, not a tooth in {m.WHEEL_N} a turn, or the foot {df:.6f}")
    if abs(abs(dw) - 0.4 / m.RATCHET_N) > 1e-6:
        v.fail(f"the ratchet turns {dw:.6f} for 0.4 of travel, not a tooth a turn")
    top = [10.0, m.HEAD[0] + m.ROLL_R, m.HEAD[1]]
    for th in (0.3, -0.3, 2.0, -2.0):
        p0 = v.point("head", (0.0, 0.0), top)
        p1 = v.point("head", (th, abs(th)), top)
        f1 = v.point("shaker", (th, abs(th)), [0.0, 0.0, 0.0])
        if p1[2] - p0[2] >= 0 or abs((p1[0] - f1[0]) - p0[0]) > 1e-9:
            v.fail(f"the belt does not creep to the head with the axle turned {th:+}: the ratchet must turn the worm one way either way")
    per_turn = abs(m.HEAD_RATIO) * (m.ROLL_R + m.BELT_T) * TAU
    print(f"drive: the countershaft with the axle, the frame E sin theta across ({2 * m.E} a stroke, a stroke a turn); the ratchet: "
          f"{m.RATCHET_N} teeth {tooth:.3f} apart (the stroke), the fixed pawl {worst_gap:.2f} under their tips through the shake: a "
          f"tooth a turn, one way; the worm on the wheel's pitch circle ({m.WHEEL_R:.2f} + {m.WORM_R:.2f}), the wheel a tooth a worm "
          f"turn (1/{m.WHEEL_N}): the belt creeps {per_turn:.3f} voxels a turn to the head, whichever way the axle turns")


def thread_crossings(v, pose):
    """Where the worm's thread crosses the worm's top (the line under the wheel's axis), as z along the
    worm: between each two neighbouring bars of the helix whose angles about the worm straddle the top,
    interpolated (the helix is linear in its angle, so this is exact for its middle line). Also the bar's
    lean there: the angle its long axis makes with the plane square to the worm (signed: + rising along +z
    as it runs along +x)."""
    m = v.m
    ax, ay, _ = v.point("worm", pose, (*m.WORM, 0.0))
    bars = sorted(v.named("worm", r"_thread\d+$", pose), key=lambda e: int(re.search(r"(\d+)$", e.name).group(1)))
    angs, prev = [], None
    for e in bars:
        a = math.atan2(-(e.c[0] - ax), e.c[1] - ay)        # from the top, right-handed about +z
        if prev is not None:
            a = prev + math.remainder(a - prev, TAU)
        angs.append(a)
        prev = a
    out = []
    for i in range(len(bars) - 1):
        a0, a1 = angs[i], angs[i + 1]
        k = math.ceil(a0 / TAU)
        if a0 <= k * TAU < a1:
            f = (k * TAU - a0) / (a1 - a0)
            lean = math.copysign(math.asin(abs(bars[i].r[2][0])), bars[i].r[2][0] * bars[i].r[0][0])
            out.append((bars[i].c[2] + f * (bars[i + 1].c[2] - bars[i].c[2]), lean))
    return out


def check_mesh(v):
    """The worm's thread in the wheel's tooth spaces: at every sampled pose (a worm turn in 32 steps, and the
    frame across its stroke), the thread's crossing under the wheel (the one nearest the wheel's lowest
    point, within half a pitch of it) lies in the middle of the space between the two lowest teeth, measured round the
    pitch circle, within a twentieth of the pitch; the thread leans as the teeth there do (both the lead
    angle); its crest reaches past the teeth's tips into the spaces and the teeth's tips reach past its crest
    into the thread's (the depth they share). The clearance checks then prove the thread never runs into a
    tooth (the pair is not in ALLOWED)."""
    m = v.m
    pitch = TAU * m.WHEEL_R / m.WHEEL_N
    worst, worst_lean, n = 0.0, 0.0, 0
    worm_turn = TAU / abs(m.WORM_RATIO)
    poses = [(th, worm_turn * i / 32 + abs(th)) for i in range(33) for th in (0.0, math.pi / 2, -math.pi / 2)]
    for pose in poses:
        hy, hz = v.point("head", pose, (0.0, *m.HEAD))[1:]
        cross = thread_crossings(v, pose)
        z, lean = min(cross, key=lambda c: abs(c[0] - hz)) if cross else (math.inf, 0.0)
        if abs(z - hz) > pitch / 2 + 1e-9:
            v.fail(f"the worm's thread does not cross under the wheel at {pose}")
            continue
        s = z - hz                                         # along the pitch line, from under the wheel's axis
        teeth = []
        for e in v.named("head", r"_wheel_tooth\d+_c$", pose):
            b = math.atan2(e.c[2] - hz, -(e.c[1] - hy))     # its angle from the wheel's lowest point, towards +z
            teeth.append((m.WHEEL_R * b, e))
        below = max((t for t in teeth if t[0] <= s), key=lambda t: t[0], default=None)
        above = min((t for t in teeth if t[0] > s), key=lambda t: t[0], default=None)
        if below is None or above is None or abs(above[0] - below[0] - pitch) > 1e-6:
            v.fail(f"the worm's thread is not between two of the wheel's teeth at {pose}")
            continue
        off = s - (below[0] + above[0]) / 2
        worst = max(worst, abs(off))
        for t in (below, above):
            r = t[1].r                                      # a tooth's lean off the wheel's axis, whatever its angle round it
            tl = math.copysign(math.atan2(math.hypot(r[1][0], r[2][0]), abs(r[0][0])), r[2][0] * r[0][0])
            worst_lean = max(worst_lean, abs(tl - lean), abs(abs(lean) - m.WORM_LAMBDA))
        n += 1
        if abs(off) > pitch / 20:
            v.fail(f"the worm's thread is {off:+.3f} off the middle of the wheel's tooth space at {pose}")
    if worst_lean > 1e-6:
        v.fail(f"the worm's thread and the wheel's teeth do not lean alike at the mesh ({math.degrees(worst_lean):.2f} degrees)")
    tips = m.WHEEL_R + m.WORM_R - m.wheel_tip(0.0)          # the teeth's tips at the throat, from the worm's axis
    roots = m.WHEEL_R + m.WORM_R - (m.WHEEL_R - 1.25 * m.WHEEL_MOD)   # the wheel's root, from the worm's axis
    depth = min(m.WORM_OUT, roots) - tips                   # the depth across which thread and teeth overlap
    if depth < 0.8 or tips > m.WORM_R - 0.3 or tips < m.WORM_CORE / math.cos(math.pi / 8):
        v.fail(f"the worm's thread and the wheel's teeth do not engage (shared depth {depth:.2f}) or the tips reach its core")
    print(f"mesh: over {n} poses (a worm turn in 32 steps, the frame at rest and at each end of its stroke) the worm's thread "
          f"crosses under the wheel in the middle of a tooth space (worst {worst:.4f} off it, the pitch {pitch:.3f}), leaning the lead "
          f"angle as the teeth either side do ({math.degrees(m.WORM_LAMBDA):.1f} degrees, worst {math.degrees(worst_lean):.1e} off); thread and teeth share "
          f"{depth:.2f} of depth, the tips {tips - m.WORM_CORE / math.cos(math.pi / 8):.2f} off the worm's core at the throat")


def check_supports(v):
    """Every shaft in two bearings or more: the countershaft in the frame's three, the worm shaft in its two
    on the shaking frame, the head and foot rollers' journals in theirs."""
    m = v.m
    fr = v.by_part["frame"]
    sk = v.by_part["shaker"]
    shafts = [("countershaft", "countershaft", r"_rod", 2, m.CS, fr, r"^fr_bearing\d$", 3),
              ("worm shaft", "worm", r"_rod", 2, m.WORM, sk, r"^sk_wbearing\d$", 2),
              ("head roller", "head", r"_journal", 0, m.HEAD, sk, r"^sk_headbearing_", 2),
              ("foot roller", "foot", r"_journal", 0, m.FOOT, sk, r"^sk_footbearing_", 2)]
    for label, pid, rx, axis, c, where, brx, need in shafts:
        found, _ = supports(where, v.named(pid, rx), axis, c)
        named = sorted(n for n in found if re.search(brx, n))
        print(f"support {label}: {len(named)} bearing(s) {', '.join(named) or 'NONE'}")
        if len(named) < need:
            v.fail(f"the {label} is not carried by {need} bearing(s)")
    print("support: every shaft in its bearings")


def check_uprights(v):
    """The uprights carry the shaking frame: each leans about its foot pin, fixed in the frame's cheeks,
    and its head stays on the rail's clevis pin through a turn, within the arc's drop (UP_LEN (1 - cos
    UP_SWING), a few hundredths); the leaning post stays between its cheeks at both ends and clear of the
    rail above it."""
    m = v.m
    worst, worst_top = 0.0, 1e9
    for pose in turns(48):
        for k, side in enumerate("we"):
            cx = sum(m.RAILS_X[k]) / 2
            for i, z in enumerate(m.UPRIGHTS_Z, 1):
                head = v.point(f"upright{side}", pose, (cx, m.UP_PIVOTS_Y[1], z))
                pin = v.point("shaker", pose, (cx, m.UP_PIVOTS_Y[1], z))
                foot = v.point(f"upright{side}", pose, (cx, m.UP_PIVOTS_Y[0], z))
                d = math.dist(head, pin)
                worst = max(worst, d)
                post = v.named(f"upright{side}", rf"^up{side}_{i}$", pose)[0]
                lo, hi = post.aabb()
                rail_lo = aabb_of(v.named("shaker", rf"^sk_rail_{side}", pose))[0]
                worst_top = min(worst_top, rail_lo[1] - hi[1])
                if d > m.UP_LEN * (1 - math.cos(m.UP_SWING)) + 1e-6 or math.dist(foot, (cx, m.UP_PIVOTS_Y[0], z)) > 1e-9 \
                        or hi[1] > rail_lo[1] - 0.02 or lo[2] < z - m.UP_D / 2 - 1e-6 or hi[2] > z + m.UP_D / 2 + 1e-6:
                    v.fail(f"upright {side}{i} leaves its pins at {pose} (head {d:.4f} off the rail's pin)")
                    return
    print(f"uprights: {2 * len(m.UPRIGHTS_Z)} leaning {math.degrees(m.UP_SWING):.2f} degrees each way, each head within {worst:.4f} of "
          f"its rail's pin through a turn (the arc's drop {m.UP_LEN * (1 - math.cos(m.UP_SWING)):.4f}), at least {worst_top:.2f} under the rail")


# ---------------------------------------------------------------- clearances
# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels at every sampled pose.
ALLOWED = [
    # shafts in their bearings and collars against them
    ("countershaft", r"_rod|_collar", "frame", r"^fr_bearing"),
    ("head|foot", r"_journal|_collar", "shaker", r"bearing_"),
    ("foot", r"_journal", "shaker", r"^sk_rail_"),          # through the rails where its bearings are set into them
    ("worm", r"_rod|_collar", "shaker", r"_wbearing"),
    # the sheaves in their yokes; the belt on its rollers (the worm and its wheel are not here: the thread runs
    # in the tooth spaces clear of the teeth, check_mesh)
    ("countershaft", r"_sheave", "shaker", r"_yoke"),
    ("belt", None, "head|foot", r"_drum"),
    # the uprights on their pins
    ("uprightw|uprighte", None, "frame", r"^fr_up[we]\d_pin"),
    ("uprightw|uprighte", None, "shaker", r"^sk_up[we]\d_pin"),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose, moving_only=False):
    state = pose[2] if len(pose) > 2 else "all"
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
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {p[0]:.3f}" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = [e for pid in v.shown(m.state_of(pose)) for e in v.posed(pid, pose)]
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
    check_containment(v, [(a, None) for a in (0.0, 0.7, math.pi / 2, 2.5, 3.3, 4.1, -math.pi / 2, 5.8, -1.2)])
    check_anchors(v)
    check_drops(v)
    check_yokes(v)
    check_drive(v)
    check_mesh(v)
    check_supports(v)
    check_uprights(v)
    check_clearances(v, turns(12 if quick else 24) + turns(8, travel_extra=37.0) + [(-0.9, None), (-2.4, None)], "clearances")
    if not quick:
        check_clearances(v, turns(96) + turns(48, travel_extra=TAU * 6.5), "swept paths (every 3.75 degrees of the axle, the creep "
                         "run on)", moving_only=True)
        check_zfight(v)
    return v.ok


def validate_files(m, shape, frame_shape, ship):
    """The written files: every texture declared; lids over every column; the anchor cells and the
    controller's not hollow."""
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
    for key in ("powerCell", "waterCell", "feedCell", "concentrateCell", "tailingsCell"):
        if tuple(ship[key]) in hollow:
            print(f"FAIL the {key} is hollow")
            ok = False
    if (0, 0, 0) in hollow:
        print("FAIL the controller's cell is hollow")
        ok = False
    return ok
