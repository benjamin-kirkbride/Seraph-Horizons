"""The concentrator generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its
rule. Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in
make_shape.py proves the shipped files are this model moved.

A pose is (theta, psi) or (theta, psi, state): the state is one of make_shape.STATES (what is fitted), and
the clearance, flow, floating and z-fighting checks look only at the parts that show together in it.
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
        return self.m.pm(self.parts, pid, pose[:2])

    def posed(self, pid, pose):
        key = (pid, tuple(pose[:2]))
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


def turns(m, n=24, state="tier4"):
    """A turn of the main shaft in n steps (the axle turning +), in a state."""
    return [(TAU * i / n, TAU * i / n, state) for i in range(n + 1)]


# ---------------------------------------------------------------- parts
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


def check_tiers(v):
    """Every part is the frame's or one tier's; each tier's set is its own (no element shared); the
    states are what the rig's tiers say: tier 4 is the table's set and the vanner's."""
    m = v.m
    reqs = {p["requires"] for p in v.parts}
    if reqs != set(m.REQUIRES) | set(m.PLATES) | {None}:
        v.fail(f"the parts' requires are {reqs}, want the frame's (null), {m.REQUIRES} and {m.PLATES}")
    pl = v.rig["plates"]
    if (pl["requires"], pl["dressing"], pl["fromTier"]) != (*m.PLATES, m.PLATES_FROM):
        v.fail(f"the rig's plates are {pl}")
    for st, fit in m.STATES.items():
        if "plates" in fit and not any(t in fit for t in ("jig", "table")):
            v.fail(f"{st} fits the plates without a tier from {m.PLATES_FROM}")
        if "dressing" in fit and "plates" not in fit:
            v.fail(f"{st} dresses plates it has not fitted")
    tiers = v.rig["tiers"]
    for (n, _, req, name), t in zip(m.TIERS, tiers):
        if t["tier"] != n or t["requires"] != list(req) or m.STATES[f"tier{n}"] != req:
            v.fail(f"tier {n} ({name}) is {t}, its state {m.STATES[f'tier{n}']}")
    for p in v.parts:
        if p["requires"] is None and p["id"] not in ("frame", "entry", "rectb1", "idler", "rectb2", "mainshaft"):
            v.fail(f"{p['id']} needs no tier, but is not the frame's")
        if p.get("ride"):
            ride = next(q for q in v.parts if q["id"] == p["ride"])
            if ride["requires"] not in (None, p["requires"]):
                v.fail(f"{p['id']} rides {ride['id']}, which another tier fits")
    counts = {r: sum(len(v.by_part[p["id"]]) for p in v.parts if p["requires"] == r) for r in m.REQUIRES + m.PLATES}
    print("tiers: " + ", ".join(f"{t['tier']} {t['name']} ({'+'.join(t['requires'])})" for t in tiers)
          + "; elements per set: " + ", ".join(f"{r} {n}" for r, n in counts.items()) + f", frame {sum(len(v.by_part[p['id']]) for p in v.parts if p['requires'] is None)}")


ROLES = [
    # the frame
    (r"^fr_(post|sill|plate|tie|brace|tl_block|cl_block|pt_block|feed_post)", {"oak"}),
    (r"^fr_(tl|cl|pt|feed)_(floor|side|end|lip|curb|spout)", {"planks"}),
    (r"^fr_(cheek|boss|hanger|feed_knee|feed_pad|pipe_flange|pipe_bracket|pipe_arm|pipe_hanger|pipe_clip)", {"iron"}),
    (r"^fr_idler_stud", {"steel"}),
    (r"^fr_pipe_(inlet|elbow|header|cap|drop|run)", {"pipe"}),
    (r"^fr_cock", {"brass"}),
    # the drive: the entry continues the vanilla axle in oak, then steel
    (r"^entry_shaft[ab]$", {"oak"}),
    (r"^(entry|rectb1|rectb2|idler|mainshaft|ltpulley|jgpulley|tbpulley|vnpulley)_", {"steel"}),
    # tier 1
    (r"^lt_riddle", {"screen"}),
    (r"^lt_(tom_|rf_|tc_)", {"planks"}),
    (r"^lt_(cleat|rest|trestle|tcblock|gallows\d_(post|beam))", {"oak"}),
    (r"^lt_gallows\d_(bearing|guide)", {"iron"}),
    (r"^lt_belt", {"leather"}),
    (r"^ltcrank_", {"steel"}),
    (r"^ltrake_crosshead", {"oak"}),
    (r"^ltrake_", {"iron"}),
    # tier 2
    (r"^jg_sieve", {"screen"}),
    (r"^jg_(floor|wall|lip|partition|cross|cl_|tc_)", {"planks"}),
    (r"^jg_guide\d_eye", {"iron"}),
    (r"^jg_(sill|ledge|guide\d[ns]$|clblock|tcblock|pedestal)", {"oak"}),
    (r"^jg_(spigot|bearing)", {"iron"}),
    (r"^jg_belt", {"leather"}),
    (r"^jgshaft_", {"steel"}),
    (r"^jgplunger\d_board", {"oak"}),
    (r"^jgplunger\d_rod", {"steel"}),
    (r"^jgplunger\d_", {"iron"}),
    # tier 3
    (r"^tbdeck_(board|lip|wl_|fb_)", {"planks"}),
    (r"^tbdeck_(joist|riffle)", {"oak"}),
    (r"^tbdeck_bracket", {"iron"}),
    (r"^tbdeck_pullrod", {"steel"}),
    (r"^tbinput_", {"steel"}),
    (r"^tb_(hm_base|leg|rail|apron_post|mb_leg)", {"oak"}),
    (r"^tb_(hm_|slide)", {"iron"}),
    (r"^tb_(apron$|mb_)", {"planks"}),
    (r"^tb_belt", {"leather"}),
    # tier 4
    (r"^vnframe_(rail|tie)", {"oak"}),
    (r"^vnframe_(belt|edge)", {"leather"}),
    (r"^vnframe_fb_(floor|side|end)", {"planks"}),
    (r"^vnframe_dist_(pipe|box)", {"pipe"}),
    (r"^vnframe_", {"iron"}),
    (r"^(vnhead|vnfoot)_drum", {"oak"}),
    (r"^(vncrank|vnworm|vnhead|vnfoot|vncarry\d|vndeflect)_", {"steel"}),
    (r"^vn_(slidepost|pedestal|tails_leg|chute_leg|pipe_post)", {"oak"}),
    (r"^vn_(slide\d|bearing)", {"iron"}),
    (r"^vn_(tank|tankspout|tails_|chute_)", {"planks"}),
    (r"^vn_pipe_", {"pipe"}),
    (r"^vn_(belt|smallbelt)", {"leather"}),
    # the amalgamation plates and their dressing
    (r"^pl_plate\d", {"copper"}),
    (r"^pl_trap", {"iron"}),
    (r"^pd_", {"amalgam"}),
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
        els = [e for pid in v.shown(state) for e in v.posed(pid, m.REST)]
        seen, loose = frame_floating(els)
        print(f"floating ({state}): {len(seen)} of {len(els)} elements joined to the ground")
        if loose:
            v.fail(f"in {state} these float: {loose[:8]}")


# ---------------------------------------------------------------- anchors and flows
def check_anchors(v):
    """The power cell's face meets the entry shaft's oak end at the cell's centre; the water inlet ends on
    the water cell's face, on its centre, in ppex's 6 x 6 section, flanged; the feed chute's open end is
    on the feed cell's face; the outlets are on the end faces; the cells differ and look out of the box."""
    m = v.m
    rig = v.rig
    lo, hi = aabb_of(v.named("entry", r"_shaft[ab]$"))
    ey, ez = m.ENTRY
    pc = m.POWER_CELL
    ok = lo[0] <= 1e-6 and pc[0] == 0 and rig["powerFace"] == "west" and (ey - pc[1] * 16, ez - pc[2] * 16) == (8.0, 8.0)
    print(f"anchors: power cell {pc} {rig['powerFace']}: the entry's oak cross from x {lo[0]:.2f}, axis (y {ey}, z {ez}), "
          f"{hi[1] - lo[1]:.1f} x {hi[2] - lo[2]:.1f} (the vanilla axle's 4 x 2 boards)")
    if not ok or abs(hi[1] - lo[1] - 4.0) > 1e-6 or abs(hi[2] - lo[2] - 4.0) > 1e-6:
        v.fail("the entry shaft does not meet the power face at the cell's centre in the axle's profile")
    inlet = next(e for e in v.by_part["frame"] if e.name == "fr_pipe_inlet")
    flange = next(e for e in v.by_part["frame"] if e.name == "fr_pipe_flange")
    ilo, ihi = inlet.aabb()
    flo, fhi = flange.aabb()
    wc = m.WATER_CELL
    wx, wy = m.WATER_IN
    centre_ok = (wx - wc[0] * 16, wy - wc[1] * 16) == (8.0, 8.0) and wc[2] == 0 and rig["waterFace"] == "north"
    section = (ihi[0] - ilo[0], ihi[1] - ilo[1])
    print(f"anchors: water cell {wc} {rig['waterFace']}: the inlet's flange on the face (z {flo[2]:.2f}), the pipe {section[0]:.0f} x "
          f"{section[1]:.0f} at (x {wx}, y {wy}), its flange {fhi[0] - flo[0]:.0f} across (ppex's pipe ends in an 8 x 8 flange)")
    if not centre_ok or flo[2] > 1e-6 or section != (6.0, 6.0) or abs(fhi[0] - flo[0] - 8.0) > 1e-6 or ilo[2] > flo[2] + 1.0 + 1e-6:
        v.fail("the water inlet does not meet a ppex pipe on the water face's centre")
    feed = v.named("frame", r"^fr_feed_(floor|side)")
    flo, fhi = aabb_of(feed)
    fc = m.FEED_CELL
    print(f"anchors: feed cell {fc} {rig['feedFace']}: the feed chute's open end at z {flo[2]:.2f}, over x {flo[0]:.1f}..{fhi[0]:.1f}, "
          f"y {flo[1]:.1f}..{fhi[1]:.1f}")
    if flo[2] > 1.0 or not (fc[0] * 16 <= flo[0] and fhi[0] <= fc[0] * 16 + 16) or rig["feedFace"] != "north":
        v.fail("the feed chute does not come in through the feed cell's face")
    cells = {tuple(pc), tuple(wc), tuple(fc)}
    if len(cells) != 3:
        v.fail("the power, water and feed cells must differ")
    c = rig["concentrate"]["pos"]
    t = rig["tailings"]["pos"]
    print(f"anchors: concentrate out at {c} ({rig['concentrateSide']}), tailings out at {t} ({rig['tailingsSide']}), "
          f"feed lands at {rig['feedSpout']['pos']}, water at {rig['waterSpout']['pos']}")
    if abs(c[0] - m.CELLS_X) > 1e-6 or rig["concentrateSide"] != "east" or abs(t[0]) > 1e-6 or rig["tailingsSide"] != "west":
        v.fail("the outlets are not on the end faces")


def ray_down(el, p):
    """The distance down (along -y) from point p to where it enters element el, or None."""
    d = [p[k] - el.c[k] for k in range(3)]
    o = [sum(el.r[k][i] * d[k] for k in range(3)) for i in range(3)]       # into the element's frame
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
    state = pose[2]
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


def deck_end(z):
    """Just past the deck's end lip, at z, where it is posed."""
    def at(v, pose):
        lip = v.named("tbdeck", r"_lip$", pose)[0]
        return [lip.aabb()[1][0] + 0.05, v.m.deck_y(z) - 0.4, z]
    return at


def deck_edge(x):
    """Just past the deck's low edge, at x (on the deck as it is posed), under the board's level."""
    def at(v, pose):
        board = v.named("tbdeck", r"_board$", pose)[0]
        dx = v.point("tbdeck", pose, [0.0, 0.0, 0.0])[0]
        return [x + dx, v.m.deck_y(v.m.DECK_Z[1]) - v.m.DECK_T - 0.05, board.aabb()[1][2] + 0.05]
    return at


def flows(v):
    """Each tier's flows: (label, the states it holds in, a point just past a lip or under a screen, the
    receiver it must fall into first (element name regex), poses). The feed and the water land on every
    tier's head; every tier's tailings reach the tailings launder and its concentrate (tiers 2-4) the
    concentrate launder; the frame's launders carry them out of the end faces."""
    m = v.m
    out = []
    fx, fy, fz = m.FEED_LIP
    feed = [fx, fy - 0.3, fz + 0.3]
    water = [m.COCK1_X, m.SPOUT_Y - 0.05, m.SPOUT_Z]
    rest = lambda st: [(0.0, 0.0, st)]                                   # noqa: E731
    shake = lambda st: [(a, a, st) for a in (0.0, 1.1, 2.6, 3.9, 5.2)]   # noqa: E731
    out += [("feed onto the tom", "tier1", feed, r"^lt_tom_floor$", rest("tier1")),
            ("water onto the tom", "tier1", water, r"^lt_tom_floor$", rest("tier1"))]
    for x in (45.0, 46.5, 48.0, 51.5):
        for z in (12.0, 16.0, 20.0):
            out.append((f"the riddle's fines at x {x} z {z}", "tier1", [x, m.lt_floor(x) - 0.55, z], r"^lt_(rf_floor|cleat\d)$", rest("tier1")))
    out += [("the riffle box's tail", "tier1", [m.RF_X[1] + 0.1, m.rf_floor(m.RF_X[1]) + 0.2, 16.0], r"^lt_tc_floor$", rest("tier1")),
            ("the tom's tail chute", "tier1", [70.5, m.tc_floor(m.TC_Z[1]) + 0.3, m.TC_Z[1] + 0.1], r"^fr_tl_floor$", rest("tier1"))]
    out += [("feed onto the jig's sieve", "tier2", feed, r"^jg_sieve1$", rest("tier2")),
            ("water into the jig", "tier2", water, r"^jg_sieve1$", rest("tier2")),
            ("the jig's overflow", "tier2", [m.JT_X[0] + 1.7, m.LIP2_Y + 0.7, 15.0], r"^jg_tc_floor$", rest("tier2")),
            ("the jig's tail chute", "tier2", [63.3, m.jt_floor(m.JT_Z[1]) + 0.3, m.JT_Z[1] + 0.1], r"^fr_tl_floor$", rest("tier2"))]
    for i, (sx0, sx1) in enumerate(m.SPIGOT_X, 1):
        out.append((f"spigot {i}", "tier2", [(sx0 + sx1) / 2, m.SPIGOT_Y[0] - 0.05, m.JL_Z[1] - 1.6], r"^jg_cl_floor$", rest("tier2")))
    out.append(("the jig's concentrate launder", "tier2", [m.JL_X[1] + 0.1, m.jl_floor(m.JL_X[1]) + 0.2, sum(m.JL_Z) / 2], r"^fr_cl_floor$",
                rest("tier2")))
    for st in ("tier3", "tier4"):
        out += [(f"feed into the table's feed box ({st})", st, feed, r"^tbdeck_board$", shake(st)),
                (f"water into the table's launder ({st})", st, water, r"^tbdeck_wl_floor$", shake(st))]
        for z in (12.0, 21.0, 30.0):
            out.append((f"the deck's end at z {z} ({st})", st, deck_end(z), r"^fr_cl_floor$", shake(st)))
        for x in (26.0, 40.0, 55.0, 63.0):
            out.append((f"the deck's low edge at x {x} ({st})", st, deck_edge(x), r"^tb_apron$", shake(st)))
        for x in (66.5, 68.5):
            out.append((f"the deck's low edge at x {x}, middlings ({st})", st, deck_edge(x), r"^tb_mb_floor$", shake(st)))
        ap = next(e for e in v.by_part["table"] if e.name == "tb_apron")
        alo, ahi = ap.aabb()
        out.append((f"the apron's foot ({st})", st, [40.0, alo[1] - 0.05, ahi[2] - 0.05], r"^fr_tl_floor$", rest(st)))
    mb_lip = [67.5, m.MB_TOP - 3.4 - 0.05, m.MB_Z[1] + 0.1]
    out.append(("the middlings spout (tier 3)", "tier3", mb_lip, r"^fr_tl_floor$", rest("tier3")))
    out.append(("the middlings spout (tier 4)", "tier4", mb_lip, r"^vn_chute_floor$", rest("tier4")))
    out.append(("the middlings chute's lip", "tier4", [67.5, m.feedbox_y() + 2.9 - 0.3, m.VC_Z[1] + 0.1], r"^vnframe_fb_floor$", shake("tier4")))
    out.append(("the vanner's feed box's slot", "tier4", [m.FEEDBOX_X[0] + 0.9, m.feedbox_y() - 0.05, 64.5], r"^vnframe_belt_run3$", shake("tier4")))
    out.append(("the vanner's water onto the distributor", "tier4", [m.COCK2_X, m.dist_y() + 2.5 + 1.2 - 0.05, m.VN_RAIL_Z[0][0] + 1.3],
                r"^vnframe_dist_box$", shake("tier4")))
    out.append(("the belt's foot (tailings)", "tier4", [m.TAILS_X[1] - 0.7, m.FOOT[1], 64.5], r"^vn_tails_floor$", shake("tier4")))
    out.append(("the vanner's tails spout", "tier4", [21.5, m.TAILS_Y[0] + 0.2, m.TAILS_Z[0] - 0.1], r"^fr_tl_floor$", rest("tier4")))
    out.append(("the wash tank's spout", "tier4", [m.CL_X[0] + 3.1, m.TANK_Y - 1.8 + 0.2, 64.5], r"^fr_cl_floor$", rest("tier4")))
    for st in m.STATES:
        out.append((f"the tailings launder's west end ({st})", st, [0.4, m.TL_Y[0] + 0.3, sum(m.TL_Z) / 2], r"^fr_tl_floor$", rest(st)))
        nz = sum(m.PT_NOTCH_Z) / 2
        out.append((f"the concentrate outlet ({st})", st, [m.X_LEN - 0.3, m.pt_floor(nz) + 0.3, nz], r"^fr_pt_spout$", rest(st)))
    # the concentrate launder spills over its lip onto the plate table: the bare bed, or the head plate
    (lx0, lx1), (lz0, lz1) = m.CL_LIP
    lip = [lx1 + 0.05, m.floor_y(m.CL_Y[0], m.CL_Y[1], m.CL_Z[0], m.CL_Z[1], lz0) - 0.2, (lz0 + lz1) / 2]
    for st in ("tier2", "tier3", "tier4"):
        out.append((f"the concentrate onto the plate table ({st})", st, lip, r"^fr_pt_floor$", rest(st)))
    out.append(("the concentrate onto the bare plates", "tier2plates", lip, r"^pl_plate1$", rest("tier2plates")))
    for st in ("tier3plates", "tier4plates"):
        out.append((f"the concentrate onto the dressed plates ({st})", st, lip, r"^pd_coat1$", rest(st)))
    # past the trap, the pulp's foot on the bed, before the notch
    z = m.PT_NOTCH_Z[0] + 1.0
    for st in ("tier2plates", "tier4plates"):
        out.append((f"past the trap ({st})", st, [92.0, m.pt_floor(z) + 0.4, z], r"^fr_pt_floor$", rest(st)))
    return out


def check_flows(v):
    """Material dropped at each lip lands first in its receiver, at every pose given (the deck's and the
    vanner's at points through their shake)."""
    n = 0
    for f in flows(v):
        label, state, p, rx, poses = f
        for pose in poses:
            q = p(v, pose) if callable(p) else p
            hit = first_below(v, q, pose)
            n += 1
            if hit is None or not re.search(rx, hit[1]):
                v.fail(f"flow: {label} from {[round(c, 2) for c in q]} at {pose[:2]} lands on {hit[1] if hit else 'nothing'}, not {rx}")
                break
    print(f"flows: {len(flows(v))} lips, {n} drops: the feed and the water onto every tier's head, every tier's tailings into the "
          "tailings launder and its concentrate into the concentrate launder, both out of the end faces")


# ---------------------------------------------------------------- mechanism
def angle_about(v, pid, pose, axis):
    mm = v.mat(pid, pose)
    if axis == "x":
        return math.atan2(mm[2][1], mm[1][1])
    return math.atan2(mm[1][0], mm[0][0])


def check_gearing(v):
    """The rectifier's meshes (centre distances the sums of the pitch radii, the pitch points moving
    alike); for either sign of the axle exactly one loose wheel turns with the main shaft, always +; the
    worm and its wheel (the centre distance, the wheel a tooth a worm turn)."""
    m = v.m
    r = m.RECT_R
    pairs = [("A1-B1", "entry", m.ENTRY_C, r["a1"], "rectb1", m.MAIN_C, r["b1"]),
             ("A2-idler", "entry", m.ENTRY_C, r["a2"], "idler", m.IDLER_C, r["i"]),
             ("idler-B2", "idler", m.IDLER_C, r["i"], "rectb2", m.MAIN_C, r["b2"])]
    worst = 0.0
    for label, pa, ca, ra, pb, cb, rb in pairs:
        d = math.hypot(cb[1] - ca[1], cb[2] - ca[2])
        if abs(d - (ra + rb)) > 1e-6:
            v.fail(f"rectifier {label}: centres {d:.4f} apart, pitch radii add to {ra + rb:.4f}")
        for th in (0.3, -0.3):
            s1, s2 = (th, abs(th)), (th * 1.2, abs(th) * 1.2)
            arc_a = math.remainder(angle_about(v, pa, s2, "x") - angle_about(v, pa, s1, "x"), TAU) * ra
            arc_b = math.remainder(angle_about(v, pb, s2, "x") - angle_about(v, pb, s1, "x"), TAU) * rb
            worst = max(worst, abs(arc_a + arc_b))
            if abs(arc_a + arc_b) > 1e-6:
                v.fail(f"rectifier {label}: {pa} rolls {arc_a:.6f}, {pb} {arc_b:.6f}")
    for th in (0.7, -0.7):
        a, b = (0.0, 0.0), (th, abs(th))
        ms = angle_about(v, "mainshaft", b, "x") - angle_about(v, "mainshaft", a, "x")
        b1 = angle_about(v, "rectb1", b, "x") - angle_about(v, "rectb1", a, "x")
        b2 = angle_about(v, "rectb2", b, "x") - angle_about(v, "rectb2", a, "x")
        if sum(abs(q - ms) < 1e-9 for q in (b1, b2)) != 1 or ms <= 0:
            v.fail(f"the rectifier: axle {th:+}, main shaft {ms:.3f}, B1 {b1:.3f}, B2 {b2:.3f}")
    print(f"gearing: the rectifier's 3 meshes tangent and rolling alike (worst {worst:.1e}); either way the axle turns, one loose "
          "wheel carries the main shaft, always +")
    d = m.WORM_C[0] - m.HEAD[1]
    if abs(d - (m.WHEEL_R + m.WORM_R)) > 1e-9 or abs(m.WORM_C[1] - sum(m.WHEEL_Z) / 2) > 1e-9:
        v.fail("the worm is not on its wheel's pitch circle")
    a, b = (0.0, 0.0), (1.0, 1.0)
    dw = angle_about(v, "vnworm", b, "x") - angle_about(v, "vnworm", a, "x")
    dh = angle_about(v, "vnhead", b, "z") - angle_about(v, "vnhead", a, "z")
    if abs(abs(dh) * m.WORM_N - abs(dw)) > 1e-6:
        v.fail(f"the worm wheel turns {dh:.5f} as the worm turns {dw:.5f}, not a tooth in {m.WORM_N} a turn")
    print(f"worm: centre distance {d:.2f} (pitch radii {m.WHEEL_R} + {m.WORM_R}); the wheel turns {abs(dh):.5f} rad as the worm turns "
          f"{dw:.4f} (1/{m.WORM_N}); the belt creeps {abs(dh) * m.ROLL_R:.4f} voxels per radian of the main shaft, up-slope")
    # the belt runs up-slope (east, to the head) on top whichever way the axle turns
    for th in (0.6, -0.6):
        top = [m.HEAD[0], m.HEAD[1] + m.ROLL_R, 64.0]
        p0 = v.point("vnhead", (0.0, 0.0), top)
        p1 = v.point("vnhead", (th, abs(th)), top)
        f0 = v.point("vnframe", (0.0, 0.0), top)
        f1 = v.point("vnframe", (th, abs(th)), top)
        if (p1[0] - f1[0]) - (p0[0] - f0[0]) <= 0:
            v.fail(f"the head roller's top runs west with the axle at {th:+}")


def check_belts(v):
    """Each belt's pulleys: the pitch speeds equal (r times the turn), the runs on their tangents."""
    m = v.m
    belts = [("tom", "ltpulley", m.MAIN_C, m.PULLEY_R, "ltcrank", (0.0, *m.RAKE_C), m.PULLEY_R),
             ("jig", "jgpulley", m.MAIN_C, m.PULLEY_R, "jgshaft", (0.0, *m.JG_C), m.JG_PULLEY_R),
             ("table", "tbpulley", m.MAIN_C, m.PULLEY_R, "tbinput", (0.0, *m.HM_IN), m.HM_PULLEY_R),
             ("vanner", "vnpulley", m.MAIN_C, m.PULLEY_R, "vncrank", (0.0, *m.VN_C), m.PULLEY_R),
             ("vanner's worm", "vncrank", (0.0, *m.VN_C), m.SMALL_R, "vnworm", (0.0, *m.WORM_C), m.SMALL_R)]
    for label, pa, ca, ra, pb, cb, rb in belts:
        a, b = (0.0, 0.0), (0.4, 0.4)
        da = angle_about(v, pa, b, "x") - angle_about(v, pa, a, "x")
        db = angle_about(v, pb, b, "x") - angle_about(v, pb, a, "x")
        if abs(da * ra - db * rb) > 1e-6:
            v.fail(f"the {label} belt slips: {pa} {da * ra:.5f}, {pb} {db * rb:.5f}")
    for name, part, ca, ra, cb, rb in (("lt_belt", "longtom", m.MAIN_C, m.PULLEY_R, (0.0, *m.RAKE_C), m.PULLEY_R),
                                       ("jg_belt", "jig", m.MAIN_C, m.PULLEY_R, (0.0, *m.JG_C), m.JG_PULLEY_R),
                                       ("tb_belt", "table", m.MAIN_C, m.PULLEY_R, (0.0, *m.HM_IN), m.HM_PULLEY_R),
                                       ("vn_belt", "vanner", m.MAIN_C, m.PULLEY_R, (0.0, *m.VN_C), m.PULLEY_R),
                                       ("vn_smallbelt", "vanner", (0.0, *m.VN_C), m.SMALL_R, (0.0, *m.WORM_C), m.SMALL_R)):
        for run in v.named(part, rf"^{name}_run[ab]$"):
            # the run's thickness axis: its thinnest local axis not along the shafts (x)
            k = min((q for q in range(3) if abs(run.r[0][q]) < 0.5), key=lambda q: abs(run.size[q]))
            n = mvec(run.r, [1.0 if q == k else 0.0 for q in range(3)])
            t = abs(run.size[k])
            for c, r_ in ((ca, ra), (cb, rb)):
                dist = abs(sum((c[q] - run.c[q]) * n[q] for q in (1, 2)))
                if abs(dist - (r_ + t / 2)) > 1e-6:
                    v.fail(f"{run.name} is {dist:.4f} from a pulley's centre, not its radius {r_} plus half its thickness")
    print(f"belts: {len(belts)} belts, each pulley pair's rims at one speed, the runs on their tangents")


def check_yokes(v):
    """Each slotted crank: at every step of a turn the pin's centre is on the yoke's slot line (the yoke
    follows exactly), and inside the slot's length; the rake's tines stay over the riddle between the
    tom's sides, the plungers in their compartment, the vanner's frame in its throw."""
    m = v.m
    cases = [("rake", "ltcrank", (sum(m.RAKE_CRANK_X) / 2, m.RAKE_C[0] + m.RAKE_E, m.RAKE_C[1]), "ltrake", r"_yoke[12]$", 2, "tier1"),
             ("plunger 1", "jgshaft", (sum(m.PLUNGERS[0]) / 2, m.JG_C[0], m.JG_C[1] + m.JG_E), "jgplunger1", r"_yoke(low|high)$", 1, "tier2"),
             ("plunger 2", "jgshaft", (sum(m.PLUNGERS[1]) / 2, m.JG_C[0], m.JG_C[1] - m.JG_E), "jgplunger2", r"_yoke(low|high)$", 1, "tier2")]
    for i, xc in enumerate(m.VN_CRANKS, 1):
        cases.append((f"vanner crank {i}", "vncrank", (xc, m.VN_C[0] + m.VN_E, m.VN_C[1]), "vnframe", rf"_yoke{i}[ab]$", 2, "tier4"))
    worst = 0.0
    for label, pin_part, pin, yoke_part, rx, axis, state in cases:
        for pose in turns(m, 48, state):
            p = v.point(pin_part, pose, pin)
            bars = v.named(yoke_part, rx, pose)
            lo0, hi0 = bars[0].aabb()
            lo1, hi1 = bars[1].aabb()
            mid = (min(hi0[axis], hi1[axis]) + max(lo0[axis], lo1[axis])) / 2
            worst = max(worst, abs(p[axis] - mid))
            span_axis = 1 if axis == 2 else 2
            slo, shi = max(lo0[span_axis], lo1[span_axis]), min(hi0[span_axis], hi1[span_axis])
            if abs(p[axis] - mid) > 1e-6 or not (slo < p[span_axis] < shi):
                v.fail(f"{label}: the pin is off its yoke's slot at {pose[:2]} ({p[axis]:.4f} vs {mid:.4f})")
                break
    print(f"yokes: {len(cases)} slotted cranks, the pin on its slot's line through a turn (worst {worst:.1e}) and inside its length")
    # the rake's tines between the tom's sides, over the riddle
    lo_side = 11.0
    hi_side = 21.0
    for pose in turns(m, 24, "tier1"):
        tl, th = aabb_of(v.named("ltrake", r"_tine\d", pose))
        if tl[2] < lo_side + 0.2 or th[2] > hi_side - 0.2 or tl[0] < m.RIDDLE_X[0] or th[0] > m.RIDDLE_X[1]:
            v.fail(f"the rake's tines leave the riddle at {pose[:2]}")
            break
    sweep = [aabb_of(v.named("ltrake", r"_tine\d", pose)) for pose in turns(m, 24, "tier1")]
    print(f"rake: the tines comb z {min(s[0][2] for s in sweep):.2f}..{max(s[1][2] for s in sweep):.2f} over the riddle "
          f"(its channel z {lo_side}..{hi_side}), {min(s[0][1] for s in sweep) - m.lt_floor(m.RIDDLE_X[1]):.2f} over it at the tail")


def check_supports(v):
    """Every shaft in two bearings (or more); the rollers in the frame's bearings at both ends."""
    m = v.m
    statics = {"frame": v.by_part["frame"], "longtom": v.by_part["longtom"], "jig": v.by_part["jig"], "table": v.by_part["table"],
               "vanner": v.by_part["vanner"], "vnframe": v.by_part["vnframe"]}
    shafts = [
        ("entry shaft", "entry", r"_shaft_rod", 0, m.ENTRY, "frame", r"^fr_(cheek[12]|boss[12]entry)", 2),
        ("main shaft", "mainshaft", r"_rod", 0, m.MAIN, "frame", r"^fr_(cheek[12]|boss[12]main|hanger_bearing)", 3),
        ("rake's crank shaft", "ltcrank", r"_rod", 0, m.RAKE_C, "longtom", r"^lt_gallows\d_bearing", 2),
        ("jig's crank shaft", "jgshaft", r"_rod", 0, m.JG_C, "jig", r"^jg_bearing\d", 2),
        ("head motion's input", "tbinput", r"_rod", 0, m.HM_IN, "table", r"^tb_hm_(box|boss)", 2),
        ("vanner's crank shaft", "vncrank", r"_rod", 0, m.VN_C, "vanner", r"^vn_bearing\d", 3),
        ("worm shaft", "vnworm", r"_rod", 0, m.WORM_C, "vnframe", r"_wormbearing\d", 2),
        ("head roller", "vnhead", r"_journal", 2, (m.HEAD[0], m.HEAD[1]), "vnframe", r"_arm[12]", 2),
        ("foot roller", "vnfoot", r"_journal", 2, (m.FOOT[0], m.FOOT[1]), "vnframe", r"_footbearing[12]", 2),
        ("deflector roller", "vndeflect", r"_journal", 2, m.DEFLECT, "vnframe", r"_arm[12]", 2),
        ("idler", "frame", r"fr_idler_stud", 0, m.IDLER, "frame", r"^fr_cheek2_arm$", 1),
    ]
    for i, x in enumerate(m.CARRY, 1):
        shafts.append((f"carrying roller {i}", f"vncarry{i}", r"_journal", 2, (x, m.run_y(x) - m.CARRY_R), "vnframe", r"_rail[12]$", 2))
    for label, pid, rx, axis, c, where, brx, need in shafts:
        els_ = v.named(pid, rx)
        found, _ = supports(statics[where], els_, axis, c)
        named = sorted(n for n in found if re.search(brx, n))
        print(f"support {label}: {len(named)} bearing(s) {', '.join(named) or 'NONE'}")
        if len(named) < need:
            v.fail(f"the {label} is not carried by {need} bearing(s)")
    # the plunger rods through their guides' eyes; the deck's joists on their slides; the frame on its slides
    for i in (1, 2):
        rod = v.named(f"jgplunger{i}", r"_rod$")[0]
        eyes = v.named("jig", rf"^jg_guide{i}_eye")
        lo, hi = aabb_of(eyes)
        rlo, rhi = rod.aabb()
        if not (lo[0] < rlo[0] and rhi[0] < hi[0] and lo[2] < rlo[2] and rhi[2] < hi[2] and rlo[1] < lo[1] and rhi[1] > hi[1]):
            v.fail(f"plunger {i}'s rod is not through its guide's eye")
    for pose in turns(m, 12, "tier3"):
        dk = v.named("tbdeck", r"_joist\d", pose)
        for j, (jz0, jz1) in enumerate(m.JOISTS_Z, 1):
            slides = v.named("table", rf"^tb_slide\d{j}$")
            joist = dk[j - 1]
            jlo, jhi = joist.aabb()
            for s in slides:
                slo, shi = s.aabb()
                if not (jlo[0] < slo[0] and shi[0] < jhi[0] and 0.0 <= jlo[1] - shi[1] < 0.15):
                    v.fail(f"the deck's joist {j} leaves its slide {s.name} at {pose[:2]}")
    for pose in turns(m, 12, "tier4"):
        for j in (1, 2):
            rail = v.named("vnframe", rf"_rail{j}$", pose)[0]
            rlo, rhi = rail.aabb()
            for s in v.named("vanner", rf"^vn_slide\d{j}$"):
                slo, shi = s.aabb()
                if not (slo[2] < rlo[2] and rhi[2] < shi[2] and 0.0 <= rlo[1] - shi[1] < 0.05):
                    v.fail(f"the vanner's rail {j} leaves its slide {s.name} at {pose[:2]}")
    print("support: the plunger rods through their eyes; the deck's joists on their slides and the vanner's rails on theirs through a turn")


# ---------------------------------------------------------------- clearances
# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels at every sampled pose.
ALLOWED = [
    # shafts in their bearings, keyed and loose wheels on their shafts, the idler on its stud
    ("entry", r"_shaft_rod|_coupling", "frame", r"fr_(cheek[12]|boss)"),
    ("mainshaft", None, "frame", r"fr_(cheek[12]|boss|hanger_bearing)"),
    ("rectb1|rectb2", None, "mainshaft", None), ("idler", None, "frame", r"fr_idler_stud"),
    ("ltpulley|jgpulley|tbpulley|vnpulley", None, "mainshaft", None),
    ("ltcrank", r"_rod|_collar", "longtom", r"lt_gallows\d_bearing"),
    ("jgshaft", r"_rod|_collar", "jig", r"jg_bearing\d"),
    ("tbinput", r"_rod", "table", r"tb_hm_(box|boss)"),
    ("vncrank", r"_rod|_collar", "vanner", r"vn_bearing\d"),
    ("vnworm", r"_rod|_collar", "vnframe", r"_wormbearing"),
    ("vnhead", r"_journal", "vnframe", r"_arm[12]"),
    ("vnfoot", r"_journal", "vnframe", r"_footbearing|_rail"),
    ("vncarry\\d", r"_journal", "vnframe", r"_rail"),
    # meshing wheels (checked by check_gearing)
    ("entry", r"entry_a1", "rectb1", None), ("entry", r"entry_a2", "idler", None), ("idler", None, "rectb2", None),
    ("vnworm", r"_thread|_core", "vnhead", r"_wheel"),
    # the belts on their pulleys
    ("longtom", r"lt_belt", "ltpulley|ltcrank", r"pulley"), ("jig", r"jg_belt", "jgpulley|jgshaft", r"pulley"),
    ("table", r"tb_belt", "tbpulley|tbinput", r"pulley"), ("vanner", r"vn_belt", "vnpulley|vncrank", r"pulley"),
    ("vanner", r"vn_smallbelt", "vncrank|vnworm", r"small|pulley"),
    ("vnframe", r"_belt|_edge", "vnhead|vnfoot|vncarry\\d|vndeflect", None),
    ("vndeflect", r"_journal", "vnframe", r"_arm[12]"),
    # the crank pins in their yokes; the rods and slides
    ("ltcrank", r"_pin", "ltrake", r"_yoke"), ("jgshaft", r"_pin", "jgplunger\\d", r"_yoke"), ("vncrank", r"_pin", "vnframe", r"_yoke"),
    ("ltrake", r"_shoe", "longtom", r"lt_gallows\d_guide"),
    ("jgplunger\\d", r"_rod", "jig", r"jg_guide\d_eye"),
    ("tbdeck", r"_joist", "table", r"tb_slide"), ("tbdeck", r"_pullrod", "table", r"tb_hm_(box|gland)"),
    ("vnframe", r"_rail", "vanner", r"vn_slide\d"),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose, moving_only=False):
    state = pose[2]
    items = []
    for pid in v.shown(state):
        for el in v.posed(pid, pose):
            items.append((pid, el, el.aabb()))
    still = {"frame", "longtom", "jig", "table", "vanner"}
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
                if pa == pb or (moving_only and pa in still and pb in still):
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
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at psi {p[1]:.3f} ({p[2]})" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


def clearance_poses(m, n=16):
    out = []
    for state in ("tier1", "tier2", "tier3", "tier4", "frame"):
        out += turns(m, n, state)
    for state in ("tier2plates", "tier3plates", "tier4plates"):
        out += turns(m, 4, state)
    out += [(-0.9, 0.9, "tier4"), (-2.4, 2.4, "tier4")]
    return out


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        state = pose[2]
        els = [e for pid in v.shown(state) for e in v.posed(pid, pose)]
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
    check_tiers(v)
    check_textures(v)
    check_floating(v)
    check_containment(v, [(a, a, "all") for a in (0.0, 0.7, 1.6, 2.5, 3.3, 4.1, 5.0, 5.8)] + [(-1.2, 1.2, "all")])
    check_anchors(v)
    check_flows(v)
    check_gearing(v)
    check_belts(v)
    check_yokes(v)
    check_supports(v)
    check_clearances(v, clearance_poses(m, 12 if quick else 24), "clearances")
    if not quick:
        check_clearances(v, [p for st in ("tier1", "tier2", "tier3", "tier4") for p in turns(m, 96, st)],
                         "swept paths (every 3.75 degrees of the main shaft, every tier)", moving_only=True)
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
    hollow = [tuple(c["pos"]) for c in ship["cells"] if c.get("hollow")]
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, {len(hollow)} hollow; "
          f"lids over every column: {'yes' if not gaps else gaps}")
    if gaps:
        ok = False
    for key in ("powerCell", "waterCell", "feedCell"):
        if tuple(ship[key]) in hollow:
            print(f"FAIL the {key} is hollow")
            ok = False
    if (0, 0, 0) in hollow:
        print("FAIL the controller's cell is hollow")
        ok = False
    return ok
