"""The amalgamation plates generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its
rule. Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in
make_shape.py proves the shipped files are this model moved. A pose is (state,): one of make_shape.STATES, what
is fitted; nothing moves, so the checks look at each state's parts as they stand.
"""

from __future__ import annotations

import json
import math
import re

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
from machinegen.geometry import aabb_of
from machinegen.rigmath import part_of, posed


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

    def posed(self, pid):
        if pid not in self.cache:
            mm = self.m.pm(self.parts, pid, self.m.REST_POSE)
            self.cache[pid] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[pid]

    def named(self, pid, rx):
        r = re.compile(rx)
        return [e for e in self.posed(pid) if r.search(e.name)]

    def one(self, pid, name):
        (el,) = [e for e in self.posed(pid) if e.name == name]
        return el

    def shown(self, state):
        """The part ids that show in a state."""
        return [pid for pid in self.by_part if self.m.on_show(self.req[pid], state)]

    def shown_els(self, state):
        return [e for pid in self.shown(state) for e in self.posed(pid)]


def top_y(el, x, z):
    """The height of a (turned) box's top face over the point (x, z), or None if the point is not over it."""
    d = [x - el.c[0], 0.0, z - el.c[2]]
    up = [el.r[k][1] for k in range(3)]                      # the box's local y in world
    if abs(up[1]) < 1e-6:
        return None
    # the top face's plane: points p with dot(p - c, up) = size_y / 2
    h = abs(el.size[1]) / 2
    y = el.c[1] + (h - d[0] * up[0] - d[2] * up[2]) / up[1]
    q = [x - el.c[0], y - el.c[1], z - el.c[2]]
    local = [sum(el.r[i][k] * q[i] for i in range(3)) for k in range(3)]
    if abs(local[0]) > abs(el.size[0]) / 2 + 1e-9 or abs(local[2]) > abs(el.size[2]) / 2 + 1e-9:
        return None
    return y


def bottom_y(el, x, z):
    """The height of a (turned) box's bottom face under the point (x, z), or None."""
    flipped = el.clone()
    flipped.r = [[-el.r[i][0], -el.r[i][1], el.r[i][2]] for i in range(3)]
    return top_y(flipped, x, z)


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


def check_rig(v):
    """No part moves (no power, no driver, no ride); the requires vocabulary is the frame's (null), the chutes,
    the plates and their dressing, the three amalgam states, the trap and the pipe metals; the build stages
    add the plates, the trap and, last, services (the chutes and the water line), the dressing in none; the pipe metals are the same elements; the states only fit what exists,
    the amalgam only on dressed plates."""
    m = v.m
    rig = v.rig
    reqs = [p["requires"] for p in v.parts]
    if set(reqs) != set(m.REQUIRES) | {None} or len(reqs) != len(set(reqs)):
        v.fail(f"the parts' requires are {reqs}, want one part each for {m.REQUIRES} and the frame")
    moving = [p["id"] for p in v.parts if p["drivers"] or p.get("ride")]
    if moving:
        v.fail(f"parts that move: {moving} (the plates have no power: nothing moves)")
    for key in ("powerCell", "powerFace", "work", "trunkPath"):
        if key in rig:
            v.fail(f"the rig has {key}: the plates take no power and do no work a driver follows")
    stages = rig["buildStages"]
    if [(st["stage"], st["adds"], st["name"]) for st in stages] != [(n, list(req), name) for n, (req, name) in enumerate(m.BUILD_STAGES, 1)]:
        v.fail(f"the rig's build stages are {stages}")
    if [st["adds"] for st in stages] != [["plates"], ["trap"], ["chutes", *m.PIPES]]:
        v.fail("the build stages are not the plates, the trap and, last, services (the chutes and the water line)")
    staged = [r for st in stages for r in st["adds"]]
    if len(staged) != len(set(staged)) or set(staged) | set(m.RECURRING) | set(m.AMALGAM) != set(m.REQUIRES) or set(staged) & set(m.RECURRING):
        v.fail("every built part is in one build stage, and the dressing and the amalgam in none")
    if rig["amalgamStates"] != list(m.AMALGAM):
        v.fail(f"the rig's amalgam states are {rig['amalgamStates']}")
    for pid in m.PIPES[1:]:
        a = sorted((e.name[len(pid):], [round(c, 9) for c in e.c], e.size) for e in v.by_part[pid])
        b = sorted((e.name[len(m.PIPES[0]):], [round(c, 9) for c in e.c], e.size) for e in v.by_part[m.PIPES[0]])
        if a != b:
            v.fail(f"{pid} is not {m.PIPES[0]}'s elements in another metal")
    for st, fit in m.STATES.items():
        if set(fit) - set(m.REQUIRES):
            v.fail(f"{st} fits {set(fit) - set(m.REQUIRES)}, which no part needs")
        if "dressing" in fit and "plates" not in fit:
            v.fail(f"{st} dresses plates it has not fitted")
        if any(a in fit for a in m.AMALGAM) and "dressing" not in fit:
            v.fail(f"{st} has amalgam on undressed plates")
        if sum(a in fit for a in m.AMALGAM) > 1 or sum(p in fit for p in m.PIPES) > 1:
            v.fail(f"{st} draws two amalgam states or two pipe metals at once")
    counts = {st: len(v.shown_els(st)) for st in m.STATES}
    print("rig: no driver, no ride, no power; build stages " + ", ".join(f"{st['stage']} ({'+'.join(st['adds'])})" for st in stages)
          + "; elements shown: " + ", ".join(f"{k} {n}" for k, n in counts.items()))


ROLES = [
    # (element regex, the textures it may wear)
    (r"^fr_(sill|post|leg|bearer|pipe_rest|dist_cross|dist_cleat)", {"oak"}),
    (r"^fr_(bed|dist|tb)_", {"planks"}),
    (r"^ch_", {"chute"}),
    (r"^pl_plate\d$", {"copper"}),
    (r"^pd_coat\d$", {"mercury"}),
    (r"^am\d_", {"amalgam"}),
    (r"^tr_mercury$", {"mercury"}),
    (r"^tr_", {"iron"}),
    (r"^pipecopper_", {"pipecopper", "solder"}),
    (r"^pipelead_", {"pipelead", "solder"}),
]


def check_textures(v):
    """Every element wears its role's textures; the amalgam and the mercury are the amalgam pan's, so the two
    machines' amalgam reads the same; the chute's is vanilla's."""
    m = v.m
    bad = []
    for el in v.els:
        tex = {f["texture"].lstrip("#") for f in el.faces.values()}
        rule = next((want for rx, want in ROLES if re.match(rx, el.name)), None)
        if rule is None or not tex <= rule:
            bad.append((el.name, sorted(tex), sorted(rule or [])))
    print(f"textures by role: {len(v.els) - len(bad)} of {len(v.els)} elements as their role says")
    if bad:
        v.fail(f"textures off their role: {bad[:6]}")
    pan = json.loads(m.AMALGAM_PAN_SHAPE.read_text())["textures"]
    print(f"textures: the amalgam {m.TEXTURES['amalgam']} (the amalgam pan's {pan.get('amalgam')}), the mercury "
          f"{m.TEXTURES['mercury']} (the pan's {pan.get('mercury')}), the chute {m.TEXTURES['chute']}")
    if m.TEXTURES["amalgam"] != pan.get("amalgam") or m.TEXTURES["mercury"] != pan.get("mercury"):
        v.fail("the amalgam or the mercury is not the amalgam pan's")
    if m.TEXTURES["chute"] != m.VANILLA_CHUTE["texture"]:
        v.fail("the chute sections are not in vanilla's chute texture")


def check_thickness(v):
    """No part thinner than about a voxel across its two larger dimensions' smaller one, but for the pieces
    thin by nature (plates, coats, the amalgam, the mercury, the pipe's walls and cap)."""
    thin_ok = re.compile(r"^(pl_plate|pd_coat|am\d_|tr_mercury|pipe(copper|lead)_)")
    bad = []
    for el in v.els:
        s = sorted(abs(x) for x in el.size)
        if s[1] < 0.6 and not thin_ok.search(el.name):
            bad.append((el.name, [round(x, 2) for x in el.size]))
    print(f"thickness: {len(bad)} elements thinner than 0.6 across outside the thin-by-nature list")
    if bad:
        v.fail(f"too thin to read at block scale: {bad[:6]}")


def check_containment(v):
    m = v.m
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where = -1e9, None
    for el in (e for pid in v.by_part for e in v.posed(pid)):
        lo, hi = el.aabb()
        o = box_overhang(lo, hi, box)
        if o > worst:
            worst, where = o, el.name
    print(f"containment: worst overhang {worst:.3f} voxels ({where})")
    if worst > 0.01:
        v.fail(f"{where} leaves the machine box")


def check_floating(v):
    """In every state, the frame and what is fitted are one piece from the ground: the chutes through the cross
    bars and the tail box's wall, the plates through the bed, the trap through the tail box, the pipe on its
    rest."""
    m = v.m
    for state in m.STATES:
        els = v.shown_els(state)
        seen, loose = frame_floating(els)
        if loose:
            v.fail(f"in {state} these float: {loose[:8]}")
    print(f"floating: in each of {len(m.STATES)} states every element is joined to the ground")


# ---------------------------------------------------------------- anchors
def check_anchors(v):
    """The spray pipe meets a ppex pipe end to end on the water face's centre, in ppex's 6 x 6 tube; the infeed
    is vanilla's upright chute section at the feed cell's top face, its foot inside the distributor over its
    floor; the outlet is vanilla's chute section along z at the output cell's end face, the tail box's sill
    inside its bore; VANILLA_CHUTE is the game's chute where the game is here; the cells are the footprint's."""
    m = v.m
    rig = v.rig
    cells = set(m.footprint())
    for key in ("feedCell", "waterCell", "outputCell"):
        if tuple(rig[key]) not in cells:
            v.fail(f"{key} {rig[key]} is not one of the machine's cells")
    # water: the inlet's end on the west face, its section ppex's, on the face's centre
    wc = m.WATER_CELL
    for pid in m.PIPES:
        tube = v.named(pid, r"_inlet_")
        lo, hi = aabb_of(tube)
        centre = ((lo[1] + hi[1]) / 2 - wc[1] * 16, (lo[2] + hi[2]) / 2 - wc[2] * 16)
        section = (round(hi[1] - lo[1], 6), round(hi[2] - lo[2], 6))
        print(f"anchors: water cell {list(wc)} {rig['waterFace']}: {pid}'s inlet from x {lo[0]:.2f}, ppex's {section[0]:.0f} x {section[1]:.0f} "
              f"tube on the face's (y {centre[0]}, z {centre[1]})")
        if rig["waterFace"] != "west" or wc[0] != 0 or abs(lo[0]) > 1e-9 or section != (6.0, 6.0) or centre != (8.0, 8.0):
            v.fail(f"{pid} does not meet a ppex pipe end to end on the water face's centre")
    # the infeed: upright at the feed cell's top face
    fc = m.FEED_CELL
    origin = [fc[k] * 16 for k in range(3)]
    prof, runs = m.chute_profile(v.posed("chutes"), "ch_in_", (0, 2), origin)
    want = sorted(m.upright_walls())
    print(f"anchors: feed cell {list(fc)} {rig['feedFace']}: the infeed's walls over (x, z) {prof}, the game's upright chute {want}; "
          f"they run y {sorted(runs)}")
    if prof != want or runs != {(m.INFEED_Y0, (fc[1] + 1) * 16.0)} or rig["feedFace"] != "up":
        v.fail("the infeed is not vanilla's chute section at the feed cell's top face")
    d = {e.name: e.aabb() for e in v.posed("frame") if e.name.startswith("fr_dist_")}
    inner = (d["fr_dist_w"][1][0], d["fr_dist_e"][0][0], d["fr_dist_n"][1][2], d["fr_dist_s"][0][2])
    floor_top = d["fr_dist_floor"][1][1]
    if not (inner[0] < origin[0] + 4 and origin[0] + 12 < inner[1] and inner[2] < origin[2] + 4 and origin[2] + 12 < inner[3]
            and floor_top < m.INFEED_Y0 < d["fr_dist_w"][1][1]):
        v.fail("the infeed chute's foot is not inside the distributor, over its floor")
    # the outlet: along z at the output cell's end face, its bore round the sill
    oc = m.OUTPUT_CELL
    origin = [oc[k] * 16 for k in range(3)]
    prof, runs = m.chute_profile(v.posed("chutes"), "ch_out_", (1, 0), origin)
    want = sorted(m.VANILLA_CHUTE["walls"])
    print(f"anchors: output cell {list(oc)} {rig['outputFace']}: the outlet's walls over (y, x) {prof}, the game's {want}; "
          f"they run z {sorted(runs)}")
    if prof != want or runs != {(m.OUTLET_Z0, (oc[2] + 1) * 16.0)} or rig["outputFace"] != "south":
        v.fail("the outlet is not vanilla's chute section at the output cell's end face")
    sill = v.one("frame", "fr_tb_sill").aabb()
    bore = (origin[0] + 5.0, origin[0] + 11.0, origin[1] + 5.0, origin[1] + 11.0)
    print(f"anchors: the tail box's sill at y {sill[1][1]:.2f} over x {sill[0][0]:.2f}..{sill[1][0]:.2f}, the outlet's bore x {bore[0]:.2f}..{bore[1]:.2f}, "
          f"y {bore[2]:.2f}..{bore[3]:.2f}; its wall's outside at z {sill[1][2]:.2f}, the outlet from z {m.OUTLET_Z0:.2f}")
    if not (bore[0] <= sill[0][0] and sill[1][0] <= bore[1] and bore[2] < sill[1][1] < bore[3] and abs(sill[1][2] - m.OUTLET_Z0) < 1e-9):
        v.fail("the tail box's sill does not pour into the outlet's bore")
    # the outlet's end butts against the box's south wall and its collar all round: every point of its walls'
    # section lies on a piece of the wall whose outside is the outlet's end
    wall = [e.aabb() for e in v.named("frame", r"^fr_tb_(s\d|sill|collar_)") if abs(e.aabb()[1][2] - m.OUTLET_Z0) < 1e-9]
    loose = 0
    for el in v.named("chutes", r"^ch_out_"):
        lo, hi = el.aabb()
        for i in range(9):
            for j in range(9):
                x = lo[0] + 0.02 + (hi[0] - lo[0] - 0.04) * i / 8
                y = lo[1] + 0.02 + (hi[1] - lo[1] - 0.04) * j / 8
                if not any(w[0][0] <= x <= w[1][0] and w[0][1] <= y <= w[1][1] for w in wall):
                    loose += 1
    print(f"anchors: the outlet's end against the tail box's south wall and collar: {loose} of {4 * 81} points off it")
    if loose:
        v.fail("the outlet chute's end does not butt against the tail box's wall all round")
    game = m.read_vanilla_chute()
    if game is None:
        print("anchors: the game's chute shape is not here; VANILLA_CHUTE is held to it where it is")
    else:
        walls, runs, gtex = game
        ok = walls == sorted(m.VANILLA_CHUTE["walls"]) and runs == {(0.0, 16.0)} and gtex == m.VANILLA_CHUTE["texture"]
        print(f"anchors: the game's {m.VANILLA_CHUTE['shape']}: walls {walls} the length of the block, texture {gtex}")
        if not ok:
            v.fail("VANILLA_CHUTE is not the game's straight chute")
    for key in m.POINTS:
        p = rig[key]["pos"]
        if not all(0 <= p[k] <= (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)[k] for k in range(3)):
            v.fail(f"the {key} point {p} is outside the machine")


# ---------------------------------------------------------------- the bed and the plates
def check_bed(v):
    """The bed falls 1 in 10 (its floor's turn); the plates lie flat on its floor between the side boards, in
    order down it, from past the head board to short of the foot's lip; the side boards stand over the plates
    and the head board over the floor; the trestles' bearers are under the bed's underside."""
    m = v.m
    floor = v.named("frame", r"^fr_bed_floor\d$")
    worst = 0.0
    for el in floor:
        fall = -el.r[1][2] / el.r[2][2]                     # dy/dz along the box's own z axis
        worst = max(worst, abs(fall - m.SLOPE))
    print(f"bed: falls {m.SLOPE} (1 in {1 / m.SLOPE:.0f}), {math.degrees(math.atan(m.SLOPE)):.2f} degrees, its floors within {worst:.1e}; "
          f"from y {m.BED_HEAD_Y} at z {m.BED_Z[0]} to {m.BED_FOOT_Y:.2f} at z {m.BED_Z[1]}")
    if worst > 1e-9:
        v.fail("the bed does not fall 1 in 10")
    x = (m.PLATE_X[0] + m.PLATE_X[1]) / 2
    last = m.BED_Z[0]
    for i, el in enumerate(v.named("plates", r"^pl_plate\d$"), 1):
        lo, hi = el.aabb()
        gaps = []
        for z in (lo[2] + 0.3, (lo[2] + hi[2]) / 2, hi[2] - 0.3):
            under = [top_y(f, x, z) for f in floor]
            under = [u for u in under if u is not None]
            gaps.append(abs(bottom_y(el, x, z) - under[0]) if under else 1e9)
        width = hi[0] - lo[0]
        if max(gaps) > 1e-6 or abs(width - (m.PLATE_X[1] - m.PLATE_X[0])) > 1e-6 or lo[2] < last or not (lo[0] >= 1.2 and hi[0] <= 14.8):
            v.fail(f"plate {i} is not flat on the floor between the side boards, below the last (gap {max(gaps):.2e}, width {width:.2f})")
        last = hi[2]
    head = v.one("frame", "fr_bed_head").aabb()
    print(f"bed: three plates {m.PLATE_X[1] - m.PLATE_X[0]:.1f} wide flat on the floor from z {m.PLATE_Z[0][0]} to {last:.2f}; "
          f"the head board to z {head[1][2]:.2f}, the lip at z {m.BED_Z[1]}")
    if m.PLATE_Z[0][0] < head[1][2] or last > m.BED_Z[1] - 0.1:
        v.fail("the plates do not lie between the head board and the foot's lip")
    side_top = min(top_y(e, 0.8, z) for e in v.named("frame", r"^fr_bed_side_w") for z in (m.BED_Z[1] - 0.5,)
                   if top_y(e, 0.8, z) is not None)
    plate_top = m.bed_y(m.BED_Z[1] - 0.5) + m.PLATE_T / m.COS
    if side_top - plate_top < 1.0:
        v.fail(f"the side boards stand only {side_top - plate_top:.2f} over the plates")
    for b in v.named("frame", r"^fr_bearer\d$"):
        lo, hi = b.aabb()
        gap = min(m.under_bed(lo[2], hi[2]) + 0.01 - hi[1], 1e9)
        if not (0.0 < gap < 0.03):
            v.fail(f"{b.name} is not just under the bed ({gap:.3f})")


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


def first_below(v, p, state):
    best = None
    for el in v.shown_els(state):
        lo, hi = el.aabb()
        if not (lo[0] <= p[0] <= hi[0] and lo[2] <= p[2] <= hi[2] and lo[1] <= p[1] + 1e-9):
            continue
        t = ray_down(el, p)
        if t is not None and (best is None or t < best[0]):
            best = (t, el.name)
    return best


def surface(state):
    """What the pulp and the spray land on, on the plates, in a state: the amalgam, the dressing, the bare
    plates, or the bed's floor."""
    alts = [r"^fr_bed_floor\d$"]
    if "plates" in state:
        alts.append(r"^pl_plate1$")
    if "dressing" in state:
        alts.append(r"^pd_coat1$")
    alts += [rf"^am{n}_" for n in range(1, 4) if f"amalgam{n}" in state]
    return "|".join(alts)


def flows(v):
    """(label, a point just past a lip, the receiver it must fall into first (element name regex), states). The
    infeed chute's foot onto the distributor's floor; each of the distributor's holes onto the head of the plates;
    each nozzle's jet onto the plates past the head board; the foot's lip into the tail box (or the trap's well);
    the sill into the outlet chute."""
    m = v.m
    out = []
    for st, fit in m.STATES.items():
        if "chutes" in fit:
            out.append((f"the infeed onto the distributor's floor ({st})", [8.0, m.INFEED_Y0 - 0.05, 8.0], r"^fr_dist_floor$", st))
            out.append((f"the sill into the outlet ({st})", [8.0, m.SILL_TOP - 0.05, m.OUTLET_Z0 + 0.1], r"^ch_out_bottom$", st))
        z = sum(m.HOLES_Z) / 2
        for i, x in enumerate(m.hole_centres(), 1):
            out.append((f"hole {i} onto the plates ({st})", [x, m.DIST_Y[0] - 0.05, z], surface(fit), st))
        if any(p in fit for p in m.PIPES):
            for i, x in enumerate(m.NOZZLE_XS, 1):
                out.append((f"nozzle {i}'s jet onto the plates ({st})", [x, m.NOZZLE_Y[0] - 0.05, m.NOZZLE_Z1 - 0.05], surface(fit), st))
        well = r"^tr_mercury$" if "trap" in fit else r"^fr_tb_floor$"
        for x in (m.PLATE_X[0] + 0.1, 8.0, m.PLATE_X[1] - 0.1):
            out.append((f"the foot's lip at x {x:.1f} into the {'trap' if 'trap' in fit else 'tail box'} ({st})",
                        [x, m.BED_FOOT_Y - 0.7, m.BED_Z[1] + 0.1], well, st))
    return out


def check_flows(v):
    fl = flows(v)
    for label, p, rx, state in fl:
        hit = first_below(v, p, state)
        if hit is None or not re.search(rx, hit[1]):
            v.fail(f"flow: {label} from {[round(c, 2) for c in p]} lands on {hit[1] if hit else 'nothing'}, not {rx}")
    print(f"flows: {len(fl)} drops over {len(v.m.STATES)} states: the infeed onto the distributor, its holes and the spray onto the "
          "plates, the lip into the tail box or the trap, the sill into the outlet")


# ---------------------------------------------------------------- the trap and the amalgam
def check_trap(v):
    """The trap's well stands on the tail box's floor inside its walls, under the lip; its baffle spans the well
    wall to wall, hangs from lugs on the box's walls' tops, its foot over the mercury and under the weir's top
    and its top over the box's water level (the sill), so the pulp must pass under it and over the weir; the
    mercury lies in the well under the baffle's foot; the plug runs out through the box's east wall."""
    m = v.m
    t = {e.name: e.aabb() for e in v.posed("trap")}
    f = {e.name: e.aabb() for e in v.posed("frame")}
    floor, baffle, weir, merc, plug = t["tr_floor"], t["tr_baffle"], t["tr_weir"], t["tr_mercury"], t["tr_plug"]
    box_in = (f["fr_tb_w"][1][0], f["fr_tb_e"][0][0], f["fr_tb_n"][1][2], f["fr_tb_s1"][0][2])
    walls = (t["tr_wall_w"][1][0], t["tr_wall_e"][0][0])
    print(f"trap: the well x {floor[0][0]:.2f}..{floor[1][0]:.2f}, z {floor[0][2]:.2f}..{floor[1][2]:.2f} on the box's floor (y {floor[0][1]:.2f}); "
          f"the mercury to y {merc[1][1]:.2f}, the baffle's foot at {baffle[0][1]:.2f}, the weir's top {weir[1][1]:.2f}, the sill {m.SILL_TOP:.2f}, "
          f"the baffle's top {baffle[1][1]:.2f}")
    if not (abs(floor[0][1] - m.TB_FLOOR) < 1e-9 and box_in[0] < floor[0][0] and floor[1][0] < box_in[1]
            and box_in[2] < floor[0][2] and floor[1][2] < box_in[3]):
        v.fail("the trap's well does not stand on the tail box's floor inside its walls")
    if not (floor[0][2] < m.BED_Z[1] < baffle[0][2]):
        v.fail("the lip does not pour into the well upstream of the baffle")
    if not (merc[1][1] < baffle[0][1] < weir[1][1] < m.SILL_TOP < baffle[1][1]):
        v.fail("the pulp does not pass under the baffle (over the mercury) and over the weir")
    if abs(baffle[0][0] - walls[0]) > 1e-9 or abs(baffle[1][0] - walls[1]) > 1e-9:
        v.fail("the baffle does not span the well wall to wall")
    for s, wall in (("w", "fr_tb_w"), ("e", "fr_tb_e")):
        lug, w = t[f"tr_lug_{s}"], f[wall]
        if not (abs(lug[0][1] - w[1][1]) < 1e-9 and lug[0][0] < w[1][0] and w[0][0] < lug[1][0]
                and lug[0][0] < baffle[1][0] and baffle[0][0] < lug[1][0] and abs(lug[0][1] - baffle[1][1]) < 1e-9):
            v.fail(f"the baffle's {s} lug does not rest on the box's wall and carry the baffle")
    if not (merc[0][2] < baffle[0][2] and baffle[1][2] < merc[1][2]):
        v.fail("the mercury does not lie under the baffle's foot")
    east = f["fr_tb_e"]
    if not (plug[0][0] <= walls[1] + 0.4 + 1e-9 and plug[1][0] > east[1][0] and east[0][1] < plug[0][1] and plug[1][1] < east[1][1]):
        v.fail("the drain plug does not run out through the box's east wall")


def check_amalgam(v):
    """Each amalgam state lies on the dressing, within its coats' outlines (its ridges on its sheets); the
    states grow, each covering more of the plates, reaching further down them and standing thicker than the
    last; the thickest stays under the side boards' tops, the nozzles and the distributor."""
    m = v.m
    coats = {e.name: e for e in v.posed("dressing")}
    stats = []
    for n, pid in enumerate(m.AMALGAM, 1):
        area, reach, thick = 0.0, 0.0, 0.0
        for plate, f0, f1, x0, x1, h0, h1, name in m.AMALGAM_PIECES[pid]:
            el = v.one(pid, f"am{n}_{name}")
            coat = coats[f"pd_coat{plate}"]
            clo, chi = coat.aabb()
            lo, hi = el.aabb()
            if not (clo[0] - 1e-9 <= lo[0] and hi[0] <= chi[0] + 1e-9 and clo[2] - 0.05 <= lo[2] and hi[2] <= chi[2] + 0.05):
                v.fail(f"{el.name} runs off its plate's dressing")
            xm, zm = (lo[0] + hi[0]) / 2, (lo[2] + hi[2]) / 2
            base = top_y(coat, xm, zm)
            if h0 == 0.0:
                if base is None or abs(bottom_y(el, xm, zm) - base) > 1e-6:
                    v.fail(f"{el.name} does not lie on the dressing")
                area += (hi[0] - lo[0]) * (hi[2] - lo[2])
            else:
                under = [e for e in v.posed(pid) if e is not el and abs((top_y(e, xm, zm) or -9) - bottom_y(el, xm, zm)) < 1e-6]
                if not under:
                    v.fail(f"{el.name} does not lie on its state's sheet")
            reach = max(reach, hi[2])
            thick = max(thick, h1)
        stats.append((area, reach, thick))
    print("amalgam: " + "; ".join(f"{pid} covers {a:.0f} square voxels to z {r:.1f}, {t:.2f} thick" for pid, (a, r, t) in zip(m.AMALGAM, stats)))
    for (a0, r0, t0), (a1, r1, t1), pid in zip(stats, stats[1:], m.AMALGAM[1:]):
        if not (a1 > a0 and r1 > r0 and t1 > t0):
            v.fail(f"{pid} does not cover more, reach further and stand thicker than the state before")
    # the side boards stand over the thickest amalgam wherever it lies, so it reads as on the plates, in the bed
    sides = v.named("frame", r"^fr_bed_side_w")
    least = 1e9
    for pid in m.AMALGAM:
        for el in v.posed(pid):
            lo, hi = el.aabb()
            for z in (lo[2] + 0.05, hi[2] - 0.05):
                side = next(t for t in (top_y(s, 0.8, z) for s in sides) if t is not None)
                amal = max(top_y(el, x, z) or -9.0 for x in (lo[0] + 0.05, hi[0] - 0.05))
                least = min(least, side - amal)
    first = min(e.aabb()[0][2] for pid in m.AMALGAM for e in v.posed(pid))
    print(f"amalgam: the side boards stand at least {least:.2f} over it; it starts at z {first:.2f}, past the nozzles' tips "
          f"(z {m.NOZZLE_Z1})")
    if least < 0.5 or first <= m.NOZZLE_Z1:
        v.fail("the amalgam stands up to the side boards' tops, or runs under the spray's nozzles")


# ---------------------------------------------------------------- clearances and z-fighting
# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels in every state.
ALLOWED = [
    ("trap", r"^tr_plug$", "frame", r"^fr_tb_e$"),                       # the drain plug through the box's east wall
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, state):
    items = [(pid, el, el.aabb()) for pid in v.shown(state) for el in v.posed(pid)]
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


def check_clearances(v):
    m = v.m
    allhits = {}
    for state in m.STATES:
        for key, hs in touching_pairs(v, state).items():
            for h in hs:
                allhits.setdefault(key, {}).setdefault(h, state)
    print(f"clearances over {len(m.STATES)} states: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} in {s}" for (a, b), s in ex))
    if allhits:
        v.fail("parts run into each other")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = v.shown_els(pose[0])
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar in {pose[0]}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} states")
    if bad:
        v.fail("faces z-fight")


VANNER_SPRAY = (1.2, 0.4)                    # the vanner's spray pipe (Vanner/tools/make_shape.py): half across, wall


def check_reducer(v):
    """The water line narrows as the vanner's does: the inlet's bore is closed at its east end by the plug and
    the spray pipe's bore together, with no gap and no overlap; the spray pipe is the vanner's size, its bore
    inside the inlet's, starting at the plug's face, a ring of solder round it there and a solder cap closing
    its east end; every nozzle stands on its south face past the reducer, inside its length, and clears the
    head board's top."""
    m = v.m
    sp = m.solderedpipe
    y, z = m.PIPE_C
    i = m.PIPE_HALF - sp.WALL
    sy, sz = m.SPRAY
    si = m.SPRAY_HALF - m.SPRAY_WALL
    for pid in m.PIPES:
        plug = [e.aabb() for e in v.named(pid, r"_plug_")]
        area = sum((hi[1] - lo[1]) * (hi[2] - lo[2]) for lo, hi in plug) + (2 * si) ** 2
        xs = {(round(lo[0], 6), round(hi[0], 6)) for lo, hi in plug}
        inside = all(y - i - 1e-9 <= lo[1] and hi[1] <= y + i + 1e-9 and z - i - 1e-9 <= lo[2] and hi[2] <= z + i + 1e-9 for lo, hi in plug)
        bore_in = y - i <= sy - si and sy + si <= y + i and z - i <= sz - si and sz + si <= z + i
        clash = [lo for lo, hi in plug if lo[1] < sy + si - 1e-9 and sy - si + 1e-9 < hi[1] and lo[2] < sz + si - 1e-9 and sz - si + 1e-9 < hi[2]]
        slo, shi = aabb_of(v.named(pid, r"_spray_(down|up|north|south)$"))
        rlo, rhi = aabb_of(v.named(pid, r"_reducer_"))
        cap = v.named(pid, r"_spray_cap$")
        nozzles = [e.aabb() for e in v.named(pid, r"_nozzle\d$")]
        print(f"reducer: {pid}: the plug and the spray pipe's bore close {area:.2f} of the inlet's {(2 * i) ** 2:.2f} square voxels at x "
              f"{sorted(xs)}; the spray pipe {shi[1] - slo[1]:.2f} across (the vanner's {2 * VANNER_SPRAY[0]}), x {slo[0]:.2f}..{shi[0]:.2f}; "
              f"{len(nozzles)} nozzles")
        if (abs(area - (2 * i) ** 2) > 1e-6 or xs != {(m.RUN_X1 - sp.WALL, m.RUN_X1)} or not inside or not bore_in or clash):
            v.fail(f"{pid}: the plug and the spray pipe's bore do not close the inlet's end")
        if (m.SPRAY_HALF, m.SPRAY_WALL) != VANNER_SPRAY or abs(slo[0] - m.RUN_X1) > 1e-9:
            v.fail(f"{pid}: the spray pipe is not the vanner's, or does not start at the plug")
        if abs(rlo[0] - m.RUN_X1) > 1e-9 or rhi[1] - rlo[1] <= shi[1] - slo[1] or len(cap) != 1 or abs(cap[0].aabb()[1][0] - shi[0]) > 1e-9:
            v.fail(f"{pid}: no solder ring round the spray pipe at the plug, or no cap at its end")
        head = m.BED_HEAD_Y + m.HEAD_H
        for lo, hi in nozzles:
            if not (rhi[0] < lo[0] and hi[0] < shi[0] - m.SPRAY_WALL and abs(lo[2] - shi[2]) < 1e-9
                    and slo[1] <= lo[1] and hi[1] <= shi[1] and lo[1] > head):
                v.fail(f"{pid}: a nozzle at x {lo[0]:.2f} is not on the spray pipe's south face past the reducer, over the head board")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_rig(v)
    check_textures(v)
    check_thickness(v)
    check_containment(v)
    check_floating(v)
    check_anchors(v)
    check_reducer(v)
    check_bed(v)
    check_flows(v)
    check_trap(v)
    check_amalgam(v)
    check_clearances(v)
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
    gaps = lid_gaps(ship["cells"])
    hollow = [c["pos"] for c in ship["cells"] if c.get("hollow")]
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, "
          f"{len(hollow)} hollow; lids: {'every column' if not gaps else gaps}")
    for c in ship["cells"]:
        print(f"  cell {c['pos']}: boxes {c.get('boxes')}, lid {c.get('lid')}")
    if gaps or hollow or len(ship["cells"]) != m.CELLS_X * m.CELLS_Y * m.CELLS_Z:
        ok = False
    if any(p["drivers"] for p in ship["parts"]) or "powerCell" in ship or "work" in ship:
        print("FAIL nothing in the plates moves or takes power")
        ok = False
    return ok
