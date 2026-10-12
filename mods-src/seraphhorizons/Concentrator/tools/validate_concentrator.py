"""The concentrator generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its
rule. Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in
make_shape.py proves the shipped files are this model moved.

A pose is (theta,) or (theta, state): the state is one of make_shape.STATES (what is fitted), and the
clearance, flow, floating and z-fighting checks look only at the parts that show together in it.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import (box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb,
                               supports)
from machinegen.geometry import aabb_of, mvec
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import part_of, posed

TAU = 2 * math.pi
MOVING = ("lineshaft", "jigsheave", "tablesheave", "vannerpulley", "jigplunger", "tabledeck", "vannershaft", "vannerworm",
          "vannerframe", "vannerhead", "vannerfoot")


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
        return self.m.pm(self.parts, pid, pose[:1])        # turned from rest: the travel is |theta|

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


def turns(n=24, state="level4dressed"):
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


def check_levels(v):
    """Every part is the frame's, one level's set's or the plates'; a level keeps the last level's parts but
    for the sluice, which level 2 takes out; the plates only from level 2, dressed only when fitted; the
    rig's levels are the generator's states."""
    m = v.m
    reqs = {p["requires"] for p in v.parts}
    if reqs != set(m.REQUIRES) | set(m.PLATES) | set(m.PIPES) | {None}:
        v.fail(f"the parts' requires are {reqs}, want the frame's (null), {m.REQUIRES}, {m.PLATES} and {m.PIPES}")
    for pid in m.PIPES[1:]:
        a = sorted((e.name[len(pid):], e.c, e.size) for e in v.by_part[pid])
        b = sorted((e.name[len(m.PIPES[0]):], e.c, e.size) for e in v.by_part[m.PIPES[0]])
        if a != b:
            v.fail(f"{pid} is not {m.PIPES[0]}'s elements in another metal")
    pl = v.rig["plates"]
    if (pl["requires"], pl["dressing"], pl["fromLevel"]) != (*m.PLATES, m.PLATES_FROM):
        v.fail(f"the rig's plates are {pl}")
    levels = v.rig["levels"]
    prev = None
    for (n, tier, req, name), lv in zip(m.LEVELS, levels):
        if lv["level"] != n or lv["requires"] != list(req) or lv["tier"] != tier or m.LEVEL_STATES[f"level{n}"] != req:
            v.fail(f"level {n} ({name}) is {lv}, its state {m.LEVEL_STATES[f'level{n}']}")
        if prev is not None:
            lost = set(prev) - set(req)
            if lost - {"sluice"}:
                v.fail(f"level {n} takes out {lost - {'sluice'}}: a level only adds, but for the sluice")
        prev = req
    if [lv["tier"] for lv in levels] != ["water", "water", "mill", "mill"]:
        v.fail("levels 1 and 2 are the water tier's, 3 and 4 the mill tier's")
    for st, fit in m.STATES.items():
        if "plates" in fit and "jig" not in fit:
            v.fail(f"{st} fits the plates without the jig (they come from level {m.PLATES_FROM})")
        if "dressing" in fit and "plates" not in fit:
            v.fail(f"{st} dresses plates it has not fitted")
    for p in v.parts:
        if p.get("ride"):
            ride = next(q for q in v.parts if q["id"] == p["ride"])
            if ride["requires"] is not None and ride["requires"] not in m.STATES["level4"] and ride["requires"] != p["requires"]:
                v.fail(f"{p['id']} rides {ride['id']}, which is not always fitted with it")
            for st, fit in m.STATES.items():
                if p["requires"] in fit and ride["requires"] not in (None, *fit):
                    v.fail(f"in {st}, {p['id']} rides {ride['id']}, which is not fitted")
    counts = {}
    for st in ("frame", "level1", "level2", "level3", "level4", "level4dressed"):
        counts[st] = sum(len(v.by_part[pid]) for pid in v.shown(st))
    counts["pipes"] = len(v.by_part[m.PIPES[0]])
    print("levels: " + ", ".join(f"{lv['level']} {lv['tier']} ({'+'.join(lv['requires'])})" for lv in levels)
          + "; elements shown: " + ", ".join(f"{k} {n}" for k, n in counts.items()))


ROLES = [
    # the frame
    (r"^fr_(post|sill|plate|tie|brace|ln_block|cl_block|pt_block)", {"oak"}),
    (r"^fr_(ln|lsl|cl|pt|tb)_(floor|side|end|head|foot|spout$|[nsew]\d?$|sill|weir|lip)", {"planks"}),
    (r"^fr_feed_(floor|[nsew]$|spout)", {"planks"}),
    (r"^ch_wall", {"chute"}),
    (r"^fr_(feed_strap|pipe_(strap|hangrod|hanger)|tb_spigot)", {"iron"}),
    (r"^fr_pipe_post", {"oak"}),
    # the water line, in each metal: the pipe in its metal's texture, the joints and the cap in solder
    (r"^pipecopper_", {"pipecopper", "solder"}),
    (r"^pipelead_", {"pipelead", "solder"}),
    # level 1
    (r"^sl_(floor|side|head)", {"planks"}),
    (r"^sl_(riffle|trestle)", {"oak"}),
    # level 2: the line shaft continues the vanilla axle in oak, then steel
    (r"^lineshaft_entry[ab]$", {"oak"}),
    (r"^(lineshaft|jigsheave|tablesheave|vannerpulley|vannershaft|vannerworm)_", {"steel"}),
    (r"^ls_", {"iron"}),
    (r"^jigplunger_(yoke|boss)", {"iron"}),
    (r"^jigplunger_board", {"oak"}),
    (r"^jigplunger_rod", {"steel"}),
    (r"^jg_sieve", {"screen"}),
    (r"^jg_(floor|wall|weir|partition|spout)", {"planks"}),
    (r"^jg_(sill|ledge|guide_[ns]$)", {"oak"}),
    (r"^jg_(guide_eye|spigot)", {"iron"}),
    # level 3
    (r"^tabledeck_(board|lip|fb_|wl_)", {"planks"}),
    (r"^tabledeck_(joist|riffle)", {"oak"}),
    (r"^tabledeck_(yoke|drop|arm)", {"iron"}),
    (r"^tb_(leg|rail|tt_leg)", {"oak"}),
    (r"^tb_slide", {"iron"}),
    (r"^tb_tt_", {"planks"}),
    # level 4
    (r"^vannerframe_(rail|tie)", {"oak"}),
    (r"^vannerframe_(belt|edge)", {"leather"}),
    (r"^vannerframe_", {"iron"}),
    (r"^(vannerhead|vannerfoot)_drum", {"oak"}),
    (r"^(vannerhead|vannerfoot)_", {"steel"}),
    (r"^vn_(slidepost|pedestal|bearer)", {"oak"}),
    (r"^vn_(slide\d|bearing|fb_hanger)", {"iron"}),
    (r"^vn_(tank|fb_)", {"planks"}),
    (r"^vn_belt", {"leather"}),
    # the amalgamation plates and their dressing
    (r"^pl_plate\d", {"copper"}),
    (r"^pl_trap", {"iron"}),
    (r"^pd_", {"mercury"}),
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


def check_thickness(v):
    """No part thinner than about a voxel: every element at least 0.6 across in its two larger
    dimensions' smaller one, but for the listed thin-by-nature pieces (screens, belts, plates, coats, lips)."""
    thin_ok = re.compile(r"(sieve|belt|edge|plate\d|coat|well|pd_|_lip$|tooth|thread|_web|_rim|spout_floor|_floor|riffle|"
                         r"_wl_|_fb_|bolt|strap|sill\d|handle|stem|_arm\d?$|clip|bracket|tb_tt_side|plug|feed_spout|joint)")
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
    """At rest, the frame alone and the frame with each state's parts (moving ones included: a shaft is
    joined through its bearings) are one piece from the ground."""
    m = v.m
    for state in m.STATES:
        els = [e for pid in v.shown(state) for e in v.posed(pid, (0.0, state))]
        seen, loose = frame_floating(els)
        print(f"floating ({state}): {len(seen)} of {len(els)} elements joined to the ground")
        if loose:
            v.fail(f"in {state} these float: {loose[:8]}")


# ---------------------------------------------------------------- anchors and flows
def check_anchors(v):
    """The power face meets the line shaft's oak end at the power cell's centre in the axle's profile; the
    water line's inlet ends on the water face's centre in ppex's 6 x 6 tube; the feed box's chute side
    is on the feed cell's face; the outlets are on their faces; the cells differ."""
    m = v.m
    rig = v.rig
    lo, hi = aabb_of(v.named("lineshaft", r"_entry[ab]$"))
    ey, ez = m.LS
    pc = m.POWER_CELL
    centred = pc[0] == 0 and rig["powerFace"] == "west" and (ey - pc[1] * 16, ez - pc[2] * 16) == (8.0, 8.0)
    print(f"anchors: power cell {pc} {rig['powerFace']}: the axle's oak cross from x {lo[0]:.2f}, axis (y {ey}, z {ez}), "
          f"{hi[1] - lo[1]:.1f} x {hi[2] - lo[2]:.1f}")
    if not centred or lo[0] > 1e-6 or abs(hi[1] - lo[1] - 4.0) > 1e-6 or abs(hi[2] - lo[2] - 4.0) > 1e-6:
        v.fail("the line shaft does not meet the power face at the cell's centre in the axle's profile")
    wc = m.WATER_CELL
    wx, wy = m.WATER_IN
    centre_ok = (wx - wc[0] * 16, wy - wc[1] * 16) == (8.0, 8.0) and wc[2] == 0 and rig["waterFace"] == "north"
    for pid in m.PIPES:
        ilo, ihi = aabb_of(v.named(pid, r"_inlet_"))
        section = (ihi[0] - ilo[0], ihi[1] - ilo[1])
        centre = ((ilo[0] + ihi[0]) / 2, (ilo[1] + ihi[1]) / 2)
        print(f"anchors: water cell {wc} {rig['waterFace']}: {pid}'s inlet from z {ilo[2]:.2f}, ppex's {section[0]:.0f} x "
              f"{section[1]:.0f} tube on (x {centre[0]}, y {centre[1]})")
        if not centre_ok or ilo[2] > 1e-6 or section != (6.0, 6.0) or centre != (wx, wy):
            v.fail(f"{pid}'s inlet does not meet a ppex pipe end to end on the water face's centre")
    fc = m.FEED_CELL
    # the infeed: vanilla's chute at the top face, its profile exactly the game's, so a chute above meets it
    prof, tops = m.chute_profile(v.els)
    want = m.vanilla_profile()
    tex = {f["texture"] for e in v.by_part["infeed"] for f in e.faces.values()}
    print(f"anchors: feed cell {list(fc)} {rig['feedFace']}: the infeed's walls at the top face {prof}, the game's upright chute "
          f"{want}; their tops at {sorted(tops)}; texture {m.TEXTURES['chute']}")
    if prof != want or tops != {(fc[1] + 1) * 16.0} or rig["feedFace"] != "up" or tex != {"#chute"} \
            or m.TEXTURES["chute"] != m.VANILLA_CHUTE["texture"]:
        v.fail("the infeed is not vanilla's chute section at the feed cell's top face")
    game = m.read_vanilla_chute()
    if game is None:
        print("anchors: the game's chute shape is not here; VANILLA_CHUTE is held to it where it is")
    else:
        walls, runs, gtex = game
        ok = walls == sorted(m.VANILLA_CHUTE["walls"]) and runs == {(0.0, 16.0)} and gtex == m.VANILLA_CHUTE["texture"]
        print(f"anchors: the game's {m.VANILLA_CHUTE['shape']}: walls {walls} the length of the block, texture {gtex}")
        if not ok:
            v.fail("VANILLA_CHUTE is not the game's straight chute")
    hlo, hhi = aabb_of(v.named("frame", r"^fr_feed_[nsew]$"))
    if not (hlo[0] < fc[0] * 16 + 4 and hhi[0] > fc[0] * 16 + 12 and hlo[2] < fc[2] * 16 + 4 and hhi[2] > fc[2] * 16 + 12
            and hlo[1] < m.CHUTE_Y0 < hhi[1]):
        v.fail("the hopper does not take the infeed chute's foot in its mouth")
    if len({tuple(pc), tuple(wc), tuple(fc)}) != 3:
        v.fail("the power, water and feed cells must differ")
    c = rig["concentrate"]["pos"]
    t = rig["tailings"]["pos"]
    spout = v.named("frame", r"^fr_pt_spout$")[0].aabb()
    tspout = aabb_of(v.named("frame", r"^fr_tb_spigot"))
    print(f"anchors: concentrate out at {c} ({rig['concentrateSide']}), its spout to x {spout[0][0]:.2f}; tailings out at {t} "
          f"({rig['tailingsSide']}), the spigot to x {tspout[1][0]:.2f}")
    if abs(c[0]) > 1e-6 or rig["concentrateSide"] != "west" or spout[0][0] > 0.2:
        v.fail("the concentrate outlet is not on the west face")
    if abs(t[0] - m.CELLS_X) > 1e-6 or rig["tailingsSide"] != "east" or tspout[1][0] < m.X_LEN - 0.2:
        v.fail("the tailings outlet is not on the east face")
    # the return water: the tail box's overflow lip out to the return cell's face, a cell apart from the tailings'
    rc = m.RETURN_CELL
    lo, hi = v.named("frame", r"^fr_tb_lip$")[0].aabb()
    in_cell = all(rc[k] * 16 <= lo[k] and hi[k] <= rc[k] * 16 + 16 for k in (1, 2))
    tcell = (int(t[0]) - 1, int(t[1]), int(t[2]))
    print(f"anchors: return water out at cell {list(rc)} {rig['returnWaterFace']}, the overflow lip to x {hi[0]:.2f} "
          f"(z {lo[2]:.1f}..{hi[2]:.1f}, its sill {hi[1]:.1f}); the tailings' cell {list(tcell)}")
    if rig["returnWaterFace"] != "east" or rc[0] != m.CELLS_X - 1 or hi[0] < m.X_LEN - 0.2 or not in_cell or tuple(rc) == tcell:
        v.fail("the return water does not leave through its own cell's east face")


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


def flows(v):
    """Each level's flows: (label, a point just past a lip, the receiver it must fall into first (element
    name regex), poses). The feed and the water land on the head of the line; each machine's tailings reach
    the next machine or a launder to the tail box, and its concentrate the concentrate launder; the
    launders carry them out of their faces, the concentrate over the plates when they are fitted."""
    m = v.m
    out = []
    fx, fy, fz = m.FEED_LIP
    feed = [fx, fy - 0.05, fz]
    wx, wy, wz = m.WATER_LIP
    water = [wx, wy - 0.05, wz]
    rest = lambda st: [(0.0, st)]                                   # noqa: E731
    shake = lambda st: [(a, st) for a in (0.0, 1.1, 2.6, 3.9, 5.2)]  # noqa: E731
    out.append(("the infeed chute into the hopper", [5.5, m.CHUTE_Y0 - 0.05, 5.5], r"^fr_feed_floor", rest("level3")))
    out += [("feed onto the sluice's head", feed, r"^sl_floor$", rest("level1")),
            ("water onto the sluice's head", water, r"^sl_floor$", rest("level1")),
            ("the sluice's tail", [m.SL_X[1] + 0.1, m.sl_floor(m.SL_X[1]) - 0.2, sum(m.SL_Z) / 2], r"^fr_ln_floor$", rest("level1"))]
    weir = [m.SPOUT_X1 + 0.1, m.WEIR_Y - 0.7, sum(m.WEIR_Z) / 2]
    spigot = [sum(m.SPIGOT_X) / 2, m.SPIGOT_Y[0] - 0.05, m.CL_Z[0] + 1.4]
    for st in ("level2", "level3", "level4"):
        out += [(f"feed onto the jig's sieve ({st})", feed, r"^jg_sieve$", rest(st)),
                (f"water into the jig ({st})", water, r"^jg_sieve$", rest(st)),
                (f"the jig's spigot ({st})", spigot, r"^fr_cl_floor$", rest(st))]
    out.append(("the jig's weir (level 2)", weir, r"^fr_ln_floor$", rest("level2")))
    for st in ("level3", "level4"):
        out.append((f"the jig's weir into the table's feed box ({st})", weir, r"^tabledeck_board\d$", shake(st)))
        out.append((f"the table's water into its launder ({st})", [m.WATER_IN[0], m.WATER_LIP[1] - 0.05, m.TABLE_OUT_Z],
                    r"^tabledeck_wl_floor$", shake(st)))
        for z in (8.0, 16.0, 24.0):
            out.append((f"the deck's low edge at z {z} ({st})", lambda vv, pose, z=z: edge_point(vv, pose, z), r"^tb_tt_floor$", shake(st)))
        for x in (24.0, 30.0, 35.0):
            out.append((f"the deck's lip at x {x} ({st})", lambda vv, pose, x=x: lip_point(vv, pose, x), r"^fr_cl_floor$", shake(st)))
    p3 = [sum(m.TT_X) / 2, m.tt_floor(m.TT_Z[1]) - 0.7, m.TT_Z[1] + 0.1]
    out.append(("the table's tails (level 3)", p3, r"^fr_lsl_floor$", rest("level3")))
    out.append(("the table's tails onto the vanner's feed box (level 4)", p3, r"^vn_fb_floor$", rest("level4")))
    out.append(("the vanner's feed box onto the belt", [m.VFEED_X[0] - 0.1, m.VFEED_Y[0] + 0.3, 37.5], r"^vannerframe_(belt_run|edge)",
                shake("level4")))
    for zn in m.VAN_NOZZLES_Z:
        out.append((f"the vanner's wash water onto the belt at z {zn}", [m.VAN_X, m.VAN_Y - m.PIPE_HALF - m.NOZZLE_Y - 0.05, zn],
                    r"^vannerframe_belt", shake("level4")))
    out.append(("the belt's foot (its tails)", [m.FOOT[0] - m.ROLL_R - m.BELT_T - 0.05, m.FOOT[1], 37.5], r"^fr_lsl_floor$", shake("level4")))
    out.append(("the concentrate washed off under the head", [m.HEAD[0], m.HEAD[1] - m.ROLL_R - m.BELT_T - 0.1, 37.5],
                r"^vn_tank_floor$", shake("level4")))
    out.append(("the concentrate box's spout", [sum(m.TANK_SPOUT_X) / 2, m.TANK_Y[0] + 0.4, m.CL_Z[0] + 1.5], r"^fr_cl_floor$", rest("level4")))
    for st in m.STATES:
        out.append((f"launder N into the tail box ({st})", [m.LN_X[1] + 0.1, m.ln_floor(m.LN_X[1]) + 0.2, sum(m.LN_Z) / 2],
                    r"^fr_tb_floor$", rest(st)))
        out.append((f"launder S into the tail box ({st})", [m.LS_LAUNDER_X[1] + 0.1, m.lsl_floor(m.LS_LAUNDER_X[1]) + 0.2,
                                                          sum(m.LS_LAUNDER_Z) / 2], r"^fr_tb_floor$", rest(st)))
        nz = sum(m.PT_NOTCH_Z) / 2
        out.append((f"the concentrate outlet ({st})", [1.0, m.pt_floor(nz) + 0.02, nz], r"^fr_pt_spout$", rest(st)))
    cl_end = [m.CL_X[0] - 0.1, m.cl_floor(m.CL_X[0]) - 0.2, 30.0]
    for st in ("level2", "level3", "level4"):
        out.append((f"the concentrate onto the plate table ({st})", cl_end, r"^fr_pt_floor$", rest(st)))
    out.append(("the concentrate onto the bare plates", cl_end, r"^pl_plate1$", rest("level2plates")))
    for st in ("level3dressed", "level4dressed"):
        out.append((f"the concentrate onto the dressed plates ({st})", cl_end, r"^pd_coat1$", rest(st)))
    z = m.TRAP_Z[1] + 0.4
    for st in ("level2plates", "level4dressed"):
        out.append((f"past the trap ({st})", [8.0, m.pt_floor(z) + 0.3, z], r"^fr_pt_floor$", rest(st)))
    return out


def edge_point(v, pose, z):
    """Just past the deck's low (east) edge, at z on the deck as it is posed."""
    m = v.m
    dz = v.point("tabledeck", pose, [0.0, 0.0, 0.0])[2]
    return [m.TT_X[0] + 1.1, m.deck_y(m.DECK_X[1]) - 0.3, z + dz]


def lip_point(v, pose, x):
    """Just past the deck's lip at its south end, at x."""
    m = v.m
    lip = v.named("tabledeck", r"_lip$", pose)[0]
    return [x, m.deck_y(x) - 0.6, lip.aabb()[1][2] + 0.05]


def check_flows(v):
    n = 0
    fl = flows(v)
    for label, p, rx, poses in fl:
        for pose in poses:
            q = p(v, pose) if callable(p) else p
            hit = first_below(v, q, pose)
            n += 1
            if hit is None or not re.search(rx, hit[1]):
                v.fail(f"flow: {label} from {[round(c, 2) for c in q]} at {pose} lands on {hit[1] if hit else 'nothing'}, not {rx}")
                break
    print(f"flows: {len(fl)} lips, {n} drops: the feed and the water onto the head of the line, each machine's tailings on to "
          "the next or a launder to the tail box, every concentrate into the concentrate launder and over the plates, out of the faces")


# ---------------------------------------------------------------- mechanism
def angle_about(v, pid, pose, axis):
    mm = v.mat(pid, pose)
    if axis == "x":
        return math.atan2(mm[2][1], mm[1][1])
    return math.atan2(mm[1][0], mm[0][0])


def check_yokes(v):
    """Each sheave in its yoke: at every step of a turn the sheave's centre is on the yoke's slot line (the
    yoke follows it exactly: its bars stay tangent to the sheave's flats' circle) and the sheave stays
    inside the slot's length."""
    m = v.m
    rc = m.SHEAVE_RC
    cases = [("jig", "jigsheave", (m.JIG_X, m.LS[0], m.LS[1] - m.JIG_E), "jigplunger", "jigplunger_yoke", 1, "level2"),
             ("table", "tablesheave", (m.TABLE_X, m.LS[0] + m.TABLE_E, m.LS[1]), "tabledeck", "tabledeck_yoke", 2, "level3")]
    for i, xs in enumerate(m.CS_SHEAVES_X, 1):
        cases.append((f"vanner {i}", "vannershaft", (xs, m.CS[0] + m.VN_E, m.CS[1]), "vannerframe", f"vannerframe_yoke{i}", 2, "level4"))
    worst = 0.0
    for label, sp, centre, yp, yname, axis, state in cases:
        other = 2 if axis == 1 else 1
        for pose in turns(48, state):
            p = v.point(sp, pose, centre)
            bars = {e.name.rsplit("_", 1)[1]: e.aabb() for e in v.named(yp, rf"^{yname}_(top|bottom|n|s)$", pose)}
            lo_k, hi_k = ("bottom", "top") if axis == 1 else ("n", "s")
            lo_o, hi_o = ("n", "s") if axis == 1 else ("bottom", "top")
            inner_lo, inner_hi = bars[lo_k][1][axis], bars[hi_k][0][axis]
            mid = (inner_lo + inner_hi) / 2
            worst = max(worst, abs(p[axis] - mid))
            half = (inner_hi - inner_lo) / 2
            end_lo, end_hi = bars[lo_o][1][other], bars[hi_o][0][other]
            if abs(p[axis] - mid) > 1e-6 or abs(half - rc - 0.01) > 1e-6 or not (end_lo + rc < p[other] < end_hi - rc):
                v.fail(f"{label}: the sheave is off its yoke's slot at {pose} ({p[axis]:.4f} vs {mid:.4f}, across {p[other]:.3f} "
                       f"in {end_lo:.3f}..{end_hi:.3f})")
                break
    print(f"yokes: {len(cases)} eccentric sheaves, each on its yoke's slot line through a turn (worst {worst:.1e}), its bars "
          f"{rc + 0.01:.3f} off the sheave's centre (its corners {rc:.3f}), the sheave inside the slot's length")


def check_drive(v):
    """The vanner's drive belt: its pulleys' rims at one speed and its runs on their tangents; the worm on
    its wheel's pitch circle, the wheel a tooth a worm turn; the belt's upper run moving to the head as the
    axle turns forward (and backwards with it: there is no rectifier)."""
    m = v.m
    a, b = (0.0,), (0.4,)
    for pa, ra, pb, rb in (("vannerpulley", m.VPULLEY_R, "vannershaft", m.VPULLEY_R),):
        da = angle_about(v, pa, b, "x") - angle_about(v, pa, a, "x")
        db = angle_about(v, pb, b, "x") - angle_about(v, pb, a, "x")
        if abs(da * ra - db * rb) > 1e-6:
            v.fail(f"the vanner's drive belt slips: {pa} {da * ra:.5f}, {pb} {db * rb:.5f}")
    for run in v.named("vanner", r"^vn_belt_run[ab]$"):
        k = min((q for q in range(3) if abs(run.r[0][q]) < 0.5), key=lambda q: abs(run.size[q]))
        n = mvec(run.r, [1.0 if q == k else 0.0 for q in range(3)])
        t = abs(run.size[k])
        for c, r_ in (((0.0, *m.LS), m.VPULLEY_R), ((0.0, *m.CS), m.VPULLEY_R)):
            dist = abs(sum((c[q] - run.c[q]) * n[q] for q in (1, 2)))
            if abs(dist - (r_ + t / 2)) > 1e-6:
                v.fail(f"{run.name} is {dist:.4f} from a pulley's centre, not its radius {r_} plus half its thickness")
    d = m.CS[0] - m.HEAD[1]
    if abs(d - (m.WHEEL_R + m.WORM_R)) > 1e-9 or not (m.WHEEL_Z[0] < m.CS[1] < m.WHEEL_Z[1]):
        v.fail("the worm is not on its wheel's pitch circle, in its plane")
    dw = angle_about(v, "vannerworm", b, "x") - angle_about(v, "vannerworm", a, "x")
    dh = angle_about(v, "vannerhead", b, "z") - angle_about(v, "vannerhead", a, "z") \
        - (angle_about(v, "vannerframe", b, "z") - angle_about(v, "vannerframe", a, "z"))
    if abs(abs(dh) * m.WORM_N - abs(dw)) > 1e-5:
        v.fail(f"the worm wheel turns {dh:.5f} as the worm turns {dw:.4f}, not a tooth in {m.WORM_N} a turn")
    top = [m.HEAD[0], m.HEAD[1] + m.ROLL_R, 37.0]
    p0 = v.point("vannerhead", a, top)
    for th in (0.3, -0.3, 2.0, -2.0):
        p1 = v.point("vannerhead", (th,), top)
        f1 = v.point("vannerframe", (th,), top)
        w0, w1 = angle_about(v, "vannerworm", a, "x"), angle_about(v, "vannerworm", (th,), "x")
        if (p1[0] - (f1[0] - top[0])) - p0[0] <= 0 or w1 - w0 <= 0:
            v.fail(f"the belt does not creep to the head with the axle turned {th:+}: the ratchet must turn the worm forward either way")
    # the ratchet: the pawl under its lowest teeth, a tooth's pitch pushed each turn
    tooth = TAU * m.RATCHET_R / m.RATCHET_N
    plo, phi = v.named("vannerframe", r"_pawl$")[0].aabb()
    gap = (m.CS[0] - m.RATCHET_R - m.RATCHET_MOD) - phi[1]
    if not (0.0 < gap < 0.2) or abs(m.PAWL_TRAVEL - tooth) > 0.1 * tooth or not (plo[2] < m.CS[1] - m.VN_E and phi[2] > m.CS[1] + m.VN_E):
        v.fail(f"the pawl is not under the ratchet's teeth through the shake (gap {gap:.3f}, stroke {m.PAWL_TRAVEL} for a tooth of {tooth:.2f})")
    per_turn = m.ROLL_R * TAU * m.WORM_RATIO / m.WORM_N
    print(f"drive: the vanner's belt 1:1, its runs on their tangents; the ratchet: 12 teeth {tooth:.2f} apart, the pawl {gap:.2f} under "
          f"them, pushed {m.PAWL_TRAVEL} a stroke: the worm shaft turns {m.WORM_RATIO:.4f} a turn of the axle, either way; worm: centre "
          f"distance {d:.2f} (pitch radii {m.WHEEL_R:.2f} + {m.WORM_R:.2f}), the wheel a tooth a worm turn (1/{m.WORM_N}): the belt "
          f"creeps {per_turn:.3f} voxels a turn to the head, whichever way the axle turns")


def check_supports(v):
    """Every shaft in two bearings or more; the rollers in the frame's bearings at both ends; the rods
    through their guides; the deck's joists on their slides and the vanner's rails on theirs through a turn."""
    m = v.m
    st = {p: v.by_part[p] for p in ("frame", "linehangers", "jig", "table", "vanner", "vannerframe")}
    shafts = [("line shaft", "lineshaft", r"_rod", 0, m.LS, "linehangers", r"^ls_bearing\d", 3),
              ("countershaft", "vannershaft", r"_rod", 0, m.CS, "vanner", r"^vn_bearing[123]$", 3),
              ("worm shaft", "vannerworm", r"_rod", 0, m.CS, "vanner", r"^vn_bearing[45]$", 2),
              ("head roller", "vannerhead", r"_journal", 2, m.HEAD, "vannerframe", r"_headbearing[12]", 2),
              ("foot roller", "vannerfoot", r"_journal", 2, m.FOOT, "vannerframe", r"_footbearing[12]", 2)]
    for label, pid, rx, axis, c, where, brx, need in shafts:
        found, _ = supports(st[where], v.named(pid, rx), axis, c)
        named = sorted(n for n in found if re.search(brx, n))
        print(f"support {label}: {len(named)} bearing(s) {', '.join(named) or 'NONE'}")
        if len(named) < need:
            v.fail(f"the {label} is not carried by {need} bearing(s)")
    for label, rp, rrx, gp, grx in (("plunger rod", "jigplunger", r"_rod$", "jig", r"^jg_guide_eye"),):
        for pose in turns(12, "level4"):
            rlo, rhi = v.named(rp, rrx, pose)[0].aabb()
            glo, ghi = aabb_of(v.named(gp, grx))
            if not (rlo[1] < glo[1] and rhi[1] > ghi[1] and glo[0] <= rlo[0] + 0.62 and rhi[0] - 0.62 <= ghi[0]):
                v.fail(f"the {label} leaves its guide at {pose}")
                break
    for pose in turns(12, "level3"):
        for j, (jx0, jx1) in enumerate(m.JOISTS_X, 1):
            joists = v.named("tabledeck", rf"_joist{j}", pose)
            jlo, jhi = aabb_of(joists)
            for s in v.named("table", rf"^tb_slide{j}\d$"):
                slo, shi = s.aabb()
                gap = min(e.aabb()[0][1] for e in joists if e.aabb()[0][2] < shi[2] and e.aabb()[1][2] > slo[2]) - shi[1]
                if not (jlo[2] < slo[2] and shi[2] < jhi[2] and -0.05 <= gap < 0.2):
                    v.fail(f"the deck's joist {j} leaves its slide {s.name} at {pose} (gap {gap:.3f})")
    for pose in turns(12, "level4"):
        for j in (1, 2):
            rails = v.named("vannerframe", rf"_rail{j}_", pose)
            rlo, rhi = aabb_of(rails)
            for s in v.named("vanner", rf"^vn_slide\d{j}$"):
                slo, shi = s.aabb()
                if not (slo[2] < rlo[2] and rhi[2] < shi[2] and 0.0 <= rlo[1] - shi[1] < 0.05):
                    v.fail(f"the vanner's rail {j} leaves its slide {s.name} at {pose}")
    print("support: the rods through their guides; the deck's joists on their slides and the vanner's rails on theirs through a turn")


# ---------------------------------------------------------------- clearances
# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels at every sampled pose.
ALLOWED = [
    # shafts in their bearings, sheaves and pulleys on their shafts
    ("lineshaft", r"_rod|_coupling|_collar", "linehangers", r"ls_bearing"),
    ("jigsheave|tablesheave|vannerpulley", None, "lineshaft", r"_rod"),
    ("vannershaft|vannerworm", r"_rod|_collar", "vanner", r"vn_bearing"),
    ("vannerhead|vannerfoot", r"_journal", "vannerframe", r"bearing|_rail"),
    # the sheaves in their yokes
    ("jigsheave", None, "jigplunger", r"_yoke"), ("tablesheave", None, "tabledeck", r"_yoke"),
    ("vannershaft", r"_sheave", "vannerframe", r"_yoke"),
    # the worm and its wheel; the belts on their pulleys and rollers
    ("vannerworm", r"_worm", "vannerhead", r"_wheel"),
    ("vanner", r"vn_belt", "vannerpulley|vannershaft", r"pulley"),
    ("vannerframe", r"_belt|_edge", "vannerhead|vannerfoot", r"_drum"),
    # the rods in their guides, the slides
    ("jigplunger", r"_rod", "jig", r"jg_guide_eye"),
    ("tabledeck", r"_joist", "table", r"tb_slide"), ("vannerframe", r"_rail", "vanner", r"vn_slide\d"),
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
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {p[0]:.3f} ({p[1]})" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


def clearance_poses(n=16):
    out = []
    for state in ("frame", "level1", "level2plates", "level3dressed", "level4dressed"):
        out += turns(n, state)
    out += [(-0.9, "level4dressed"), (-2.4, "level4dressed")]
    return out


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
    check_levels(v)
    check_textures(v)
    check_thickness(v)
    check_floating(v)
    check_containment(v, [(a, "all") for a in (0.0, 0.7, 1.6, 2.5, 3.3, 4.1, 5.0, 5.8, -1.2)])
    check_anchors(v)
    check_flows(v)
    check_yokes(v)
    check_drive(v)
    check_supports(v)
    check_clearances(v, clearance_poses(12 if quick else 24), "clearances")
    if not quick:
        check_clearances(v, [p for st in ("level1", "level2", "level3", "level4") for p in turns(96, st)],
                         "swept paths (every 3.75 degrees of the axle, every level)", moving_only=True)
        check_zfight(v)
    return v.ok


def validate_files(m, shape, frame_shape, ship):
    """The written files: every texture declared; lids over every column; the power, water and feed
    cells and the controller's not hollow; collision per level: the frame's boxes where the frame is, and
    each cell's fitted boxes keyed by a part's requires value, so the sluice's are gone at level 2 and the
    vanner's absent before level 4."""
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL textures used but not declared: {missing}")
        ok = False
    gaps = lid_gaps(ship["cells"])
    hollow = [tuple(c["pos"]) for c in ship["cells"] if c.get("hollow")]
    reqs = {r for c in ship["cells"] for r in c.get("fitted", {})}
    per = {r: sum(1 for c in ship["cells"] if r in c.get("fitted", {})) for r in (*m.REQUIRES, *m.PLATES, *m.PIPES)}
    framed = sum(1 for c in ship["cells"] if c.get("boxes"))
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, {framed} with "
          f"frame boxes, {len(hollow)} hollow; fitted boxes per part: " + ", ".join(f"{r} {n}" for r, n in per.items())
          + f"; lids over every column: {'yes' if not gaps else gaps}")
    if gaps:
        ok = False
    if not reqs <= set(m.REQUIRES) | set(m.PLATES) | set(m.PIPES):
        print(f"FAIL fitted boxes for {reqs - set(m.REQUIRES) - set(m.PLATES) - set(m.PIPES)}")
        ok = False
    a, b = ({tuple(c["pos"]): c.get("fitted", {}).get(pid) for c in ship["cells"]} for pid in m.PIPES)
    if a != b:
        print("FAIL the water line's metals collide differently")
        ok = False
    for key in ("powerCell", "waterCell", "feedCell"):
        if tuple(ship[key]) in hollow:
            print(f"FAIL the {key} is hollow")
            ok = False
    if (0, 0, 0) in hollow:
        print("FAIL the controller's cell is hollow")
        ok = False
    return ok
