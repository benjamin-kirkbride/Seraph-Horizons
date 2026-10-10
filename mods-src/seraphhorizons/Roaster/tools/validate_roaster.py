"""The roaster generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved.

The roaster's tiers share one place: the stalls of tier 2 stand where the furnace of tiers 3 and 4 does.
So every check that looks at parts together looks at one state at a time (the frame alone, tier 2,
tier 3, tier 4: make_shape.STATES), never at every tier at once.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, cells_touched, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb, supports
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply as _apply
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

    def mat(self, pid, theta):
        return self.m.pm(self.parts, pid, (theta, "all"))

    def posed(self, pid, theta):
        key = (pid, theta)
        if key not in self.cache:
            if len(self.cache) > 20000:
                self.cache.clear()
            mm = self.mat(pid, theta)
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def point(self, pid, theta, p):
        return [v * 16 for v in _apply(self.mat(pid, theta), [c / 16 for c in p])]

    def shows(self, pid, state):
        return self.m.state_shows(self.req[pid], state)

    def named(self, pid, rx, theta=None):
        src = self.posed(pid, theta) if theta is not None else self.by_part.get(pid, [])
        r = re.compile(rx)
        return [e for e in src if r.search(e.name)]


def thetas(n=48, turns=3):
    """Angles over the rig's whole cycle (the rabbles turn a third of the axle: three turns)."""
    return [turns * math.tau * i / n for i in range(n)]


# ---------------------------------------------------------------- parts, textures, the box
def check_basic(v):
    worst = euler_round_trip(v.els)
    print(f"euler round trip: worst {worst:.1e}")
    if worst > 1e-9:
        v.fail("an element's rotation does not survive being written as Euler angles")
    seen, dup = set(), set()
    for el in v.els:
        (dup if el.name in seen else seen).add(el.name)
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


ROLES = [
    # (element regex, the textures it may wear)
    (r"^fr_(plinth|stackcap)", {"stone"}),
    (r"^fr_(chamber|stack_)", {"brick"}),
    (r"^fr_(spout|sulfurdoor|stackband)", {"iron"}),
    (r"^stalls_", {"rubble"}),
    (r"^stallflue_", {"brick"}),
    (r"^stallbin_", {"oak"}),
    (r"^heaps_s\dlog", {"firewood"}),
    (r"^heaps_s\dcourse", {"ore"}),
    (r"^hearth_(bed|bridge)", {"firebrick"}),
    (r"^hearth_(bearer|grate)", {"iron"}),
    (r"^walls_(side|end)", {"brick"}),
    (r"^walls_(gable|skew)", {"firebrick"}),
    (r"^arch_v", {"firebrick"}),
    (r"^arch_seat", {"iron"}),
    (r"^ironwork_", {"iron"}),
    (r"^fire_", {"fire"}),
    (r"^lineshaft_entry", {"oak"}),
    (r"^lineshaft_(rod|pinion)", {"iron"}),
    (r"^(pedestal|rabble|crank|yoke|charger|hopper)", {"iron"}),
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


def check_containment(v):
    """Every element, at every sampled angle, inside the footprint's cells (not just the machine box: the
    footprint is the body, the drive's row over the roof and the stack)."""
    m = v.m
    cells = set(m.footprint())
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where, outside = -1e9, None, set()
    for th in thetas(24):
        for pid in v.by_part:
            for el in v.posed(pid, th):
                lo, hi = el.aabb()
                o = box_overhang(lo, hi, box)
                if o > worst:
                    worst, where = o, el.name
                for c in cells_touched(lo, hi):
                    if c not in cells:
                        outside.add((el.name, c))
    print(f"containment: {len(cells)} cells; worst overhang of the machine box {worst:.3f} voxels ({where}); "
          f"{len(outside)} elements outside the cells")
    if worst > 0.01:
        v.fail(f"{where} leaves the machine box")
    if outside:
        v.fail(f"elements outside the footprint's cells: {sorted(outside)[:6]}")


# ---------------------------------------------------------------- the tiers
def check_tiers(v):
    """The tiers' sets: tier 2 shares nothing with 3 or 4 (its parts drop back when the furnace is fitted);
    tier 4 is tier 3 less its charging box, plus the hopper and the feeder; every requires value is in a
    tier; every state's parts are the frame's and its own."""
    m = v.m
    t2, t3, t4 = (set(m.TIERS[k]) for k in ("2", "3", "4"))
    values = {p["requires"] for p in v.parts if p["requires"] is not None}
    if t2 & (t3 | t4):
        v.fail(f"tier 2 shares parts with the furnace: {sorted(t2 & (t3 | t4))}")
    if t4 != (t3 - {"charger"}) | {"hopper", "feeder"}:
        v.fail(f"tier 4 is not tier 3 less the charging box plus the hopper and the feeder: {sorted(t4)}")
    if values != t2 | t3 | t4:
        v.fail(f"requires values in no tier, or tiers naming no part: {sorted(values ^ (t2 | t3 | t4))}")
    if set(m.STATES) != {"frame", "tier2", "tier3", "tier4"} or m.STATES["frame"]:
        v.fail("the states are the frame alone and tiers 2, 3 and 4")
    for s, fitted in m.STATES.items():
        shown = sorted(p["id"] for p in v.parts if v.shows(p["id"], s))
        print(f"state {s}: {len(shown)} parts ({', '.join(shown)})")
    print(f"tiers: 2 {sorted(t2)}; 3 {sorted(t3)}; 4 {sorted(t4)}")


def static_parts(v, state):
    return [p["id"] for p in v.parts if p["id"] not in v.m.MOVING and v.shows(p["id"], state)]


def check_floating(v):
    """Nothing floats, in each state: the frame and the state's static parts are one piece from the ground.
    (The moving parts are carried by their bearings: check_supports.)"""
    for s in v.m.STATES:
        els = [el for pid in static_parts(v, s) for el in v.by_part[pid]]
        seen, loose = frame_floating(els)
        print(f"nothing floats ({s}): {len(seen)} elements joined to the ground, {len(loose)} not")
        if loose:
            v.fail(f"elements float in {s}: {loose[:8]}")


# ---------------------------------------------------------------- the moving parts
# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else a moving part touches, in a state where both are drawn, is a failure.
ALLOWED = [
    ("lineshaft", r"_rod", "pedestals", r"_bearing"),               # the shaft in its bearings
    ("lineshaft", r"_rod", "crank", r"_disc"),                      # the crank keyed on its end
    ("crank", None, "lineshaft", r"_rod"),
    ("rabble\\d", r"_spindle", "rabblemounts", r"_(gland|bearing|base)"),   # each spindle in its gland and its bearing
    ("rabble\\d", r"_spindle", "arch", r"arch_v4_"),                # (it passes the crown strip's cut ends within its hole)
    ("yoke", r"_rod", "hopper", r"_feedbox_n"),                     # the push rod in its slot
    ("yoke", r"_plate", "hopper", r"_runner"),                      # the plate on its runners
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def overlap(a, b, eps=0.02):
    return all(a[0][q] < b[1][q] - eps and b[0][q] < a[1][q] - eps for q in range(3))


def candidates(v, state, ths):
    """For each moving part drawn in `state`: the static elements drawn with it that its swept box (over
    `ths`) reaches, as (part, element, box); the statics stand still, so they are posed once."""
    statics = [(pid, el, el.aabb()) for pid in v.by_part if pid not in v.m.MOVING and v.shows(pid, state) for el in v.by_part[pid]]
    out = {}
    for pid in v.m.MOVING:
        if not v.shows(pid, state):
            continue
        boxes = [e.aabb() for th in ths for e in v.posed(pid, th)]
        swept = ([min(b[0][q] for b in boxes) for q in range(3)], [max(b[1][q] for b in boxes) for q in range(3)])
        out[pid] = [s for s in statics if overlap(s[2], swept)]
    return out


def touching_pairs(v, th, state, cands):
    moving = [(pid, el, el.aabb()) for pid in cands for el in v.posed(pid, th)]
    hits = {}
    for i, (pa, ea, (alo, ahi)) in enumerate(moving):
        others = cands[pa] + [x for x in moving if x[0] != pa and v.m.MOVING.index(x[0]) > v.m.MOVING.index(pa)]
        for pb, eb, (blo, bhi) in others:
            if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                continue
            if allowed(pa, ea.name, pb, eb.name):
                continue
            if obb_obb(ea, eb):
                hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def check_clearances(v, n, label):
    """Every moving part against everything drawn with it (tiers 3 and 4), over the rig's cycle: the gear
    teeth clear each other, the rabbles clear the hearth, the bridge and the walls, the yoke and the plate
    clear the feed box."""
    allhits = {}
    ths = thetas(n)
    for s in ("tier3", "tier4"):
        cands = candidates(v, s, ths)
        for th in ths:
            for key, hs in touching_pairs(v, th, s, cands).items():
                allhits.setdefault(key, {})
                for h in hs:
                    allhits[key].setdefault(h, (th, s))
    print(f"{label} over {len(ths)} angles in tiers 3 and 4: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {p[0]:.3f} ({p[1]})" for (a, b), p in ex))
    if allhits:
        v.fail("moving parts run into each other or the furnace")


def check_supports(v):
    """The line shaft in at least two bearings, each spindle in its gland and its bearing; the pinions and
    crown wheels on their shafts' axes."""
    m = v.m
    statics = [el for pid in ("pedestals", "rabblemounts") for el in v.by_part[pid]]
    # supports() takes the other two coordinates in axis order: for a shaft along z, (x, y)
    found, span = supports(statics, v.named("lineshaft", r"_rod"), 2, (m.SHAFT[0], m.SHAFT[1]))
    bearings = [n for n in found if n.endswith("_bearing")]
    print(f"support line shaft: {len(bearings)} bearings ({', '.join(bearings)}), shaft z {span}")
    if len(bearings) < 2:
        v.fail("the line shaft is not carried by two bearings")
    for k, zs in enumerate(m.RABBLE_Z, 1):
        found, span = supports(statics + v.by_part["arch"], v.named(f"rabble{k}", r"_spindle"), 1, (m.SHAFT[0], zs))
        names = sorted(n for n in found if n.startswith("rabblemount"))
        print(f"support rabble {k}: spindle y {span}, carried by {', '.join(names)}")
        if not any(n.endswith("_gland") for n in names) or not any(n.endswith("_bearing") for n in names):
            v.fail(f"rabble {k}'s spindle is not in its gland and its bearing")
        hub = min(e.aabb()[0][1] for e in v.named(f"rabble{k}", r"_crownhub"))
        seat = max(e.aabb()[1][1] for e in v.by_part["rabblemounts"] if e.name == f"rabblemount{k}_bearing")
        if abs(hub - seat) > 1e-6:
            v.fail(f"rabble {k}'s crown hub does not rest on its bearing ({hub:.3f} against {seat:.3f})")


def check_gearing(v):
    """Each crown wheel is meshed by its pinion: the pinion's pitch circle meets the crown's pitch radius on
    its north rim, at its pitch plane; the rig turns the spindle at -NP/NC of the shaft (a crown driven from its
    north rim turns back), and the teeth stay in step: at every sampled angle the pinion tooth nearest the
    contact and the crown gap there are within a tenth of a tooth of each other."""
    m = v.m
    want = -m.NP / m.NC
    worst = 0.0
    for k, zs in enumerate(m.RABBLE_Z, 1):
        d = next(p for p in v.parts if p["id"] == f"rabble{k}")["drivers"][0]
        if abs(d["ratio"] - want) > 1e-6:
            v.fail(f"rabble {k} turns at {d['ratio']}, not {want}")
        zp = zs - m.RC
        teeth = v.named("lineshaft", rf"_pinion{k}_tooth")
        cz = sum(e.c[2] for e in teeth) / len(teeth)
        if abs(cz - zp) > 1e-6 or abs(m.SHAFT[1] - m.RP - m.CROWN_PITCH_Y) > 1e-9:
            v.fail(f"pinion {k} is not on crown {k}'s north rim at its pitch plane")
        # the angular pitch along the line of contact: the contact moves across x at RP per radian of the shaft
        for th in thetas(72):
            # the pinion's teeth: the one pointing most nearly down, its x at the pitch circle
            ang = [math.pi + math.tau * i / m.NP + th for i in range(m.NP)]
            a = min(ang, key=lambda t: abs(math.remainder(t - math.pi, math.tau)))
            xp = -m.RP * math.sin(a)
            # the crown's gaps after it has turned RATIO * theta: the one nearest the north rim, its x
            gaps = [math.pi + math.tau * j / m.NC + m.RATIO * th for j in range(m.NC)]
            g = min(gaps, key=lambda t: abs(math.remainder(t - math.pi, math.tau)))
            xc = m.RC * math.sin(g)
            worst = max(worst, abs(xp - xc))
    pitch = math.pi * m.MODULE
    print(f"gearing: {m.NP}-tooth pinions on the crown wheels' north rims, {m.NC} teeth, the spindles at {want:.4f} of the shaft; "
          f"tooth and gap within {worst:.4f} voxels at the contact (a tooth's pitch is {pitch:.3f})")
    if worst > 0.1 * pitch:
        v.fail("a pinion's teeth drift out of the crown's gaps")


def check_yoke(v):
    """The Scotch yoke: at every sampled angle the crank pin is between the yoke's cheeks, clear of both
    (the slot carries it exactly: the yoke's slide is the pin's x), and within their height."""
    m = v.m
    worst_off, worst_y = 0.0, 1e9
    for th in thetas(72, 1):
        pin = aabb_of(v.named("crank", r"_pin", th))
        cw = aabb_of(v.named("yoke", r"_cheekw", th))
        ce = aabb_of(v.named("yoke", r"_cheeke", th))
        mid = ((cw[1][0] + ce[0][0]) / 2, (pin[0][0] + pin[1][0]) / 2)
        worst_off = max(worst_off, abs(mid[0] - mid[1]))
        worst_y = min(worst_y, pin[0][1] - cw[0][1], cw[1][1] - pin[1][1])
    print(f"yoke: the pin in the slot within {worst_off:.2e} voxels of its middle; at least {worst_y:.2f} inside the cheeks' height")
    if worst_off > 1e-6 or worst_y < 0.05:
        v.fail("the crank pin leaves the yoke's slot")


# ---------------------------------------------------------------- the frame and the anchors
def check_anchors(v):
    """The anchors are the frame's and never move: the power cell's face is the footprint's outside and the
    line shaft's entry reaches it at the cell's centre; every tier's hopper has its mouth at the infeed cell's
    top, inside the cell; the spout is in the output cell, its mouth on its east face; the stack, the flue
    chamber and the pit are the frame's."""
    m = v.m
    cells = set(m.footprint())
    normals = {"north": (0, 0, -1), "south": (0, 0, 1), "east": (1, 0, 0), "west": (-1, 0, 0), "up": (0, 1, 0), "down": (0, -1, 0)}
    for key, cell, face in (("power", m.POWER_CELL, m.POWER_FACE), ("infeed", m.INFEED_CELL, m.INFEED_FACE), ("output", m.OUTPUT_CELL, m.OUTPUT_FACE)):
        n = normals[face]
        out = tuple(cell[k] + n[k] for k in range(3))
        if cell not in cells or out in cells:
            v.fail(f"the {key} cell {cell} is not in the footprint with its {face} face outside")
    entry = aabb_of(v.named("lineshaft", r"_entry"))
    c = [(m.POWER_CELL[k] + 0.5) * 16 for k in range(3)]
    if abs(entry[0][2] - m.POWER_CELL[2] * 16) > 1e-6 or abs((entry[0][0] + entry[1][0]) / 2 - c[0]) > 1e-6 or abs((entry[0][1] + entry[1][1]) / 2 - c[1]) > 1e-6:
        v.fail(f"the line shaft's entry does not meet the power face at the cell's centre: {entry}")
    top = (m.INFEED_CELL[1] + 1) * 16
    lo_c = [m.INFEED_CELL[k] * 16 for k in range(3)]
    for pid, rx in (("stallbin", r"_box_"), ("charger", r"_box_"), ("hopper", r"_bin_")):
        lo, hi = aabb_of(v.named(pid, rx))
        inside = all(lo_c[k] - 1e-6 <= lo[k] and hi[k] <= lo_c[k] + 16 + 1e-6 for k in (0, 2))
        if abs(hi[1] - top) > 1e-6 or not inside:
            v.fail(f"the {pid}'s mouth is not at the infeed cell's top ({hi[1]:.2f}, inside {inside})")
    spout = aabb_of(v.named("frame", r"^fr_spout"))
    oc = [m.OUTPUT_CELL[k] * 16 for k in range(3)]
    if abs(spout[1][0] - (m.OUTPUT_CELL[0] + 1) * 16) > 1e-6 or not all(oc[k] <= spout[0][k] and spout[1][k] <= oc[k] + 16 for k in (1, 2)):
        v.fail(f"the spout is not in the output cell, on its east face: {spout}")
    stack = aabb_of(v.named("frame", r"^fr_stack"))
    if abs(stack[1][1] - m.STACK_TOP) > 1e-6:
        v.fail("the stack's top is not STACK_TOP")
    for rx in (r"^fr_stack_", r"^fr_chamber_", r"^fr_spout", r"^fr_plinth", r"^fr_sulfurdoor"):
        if not v.named("frame", rx):
            v.fail(f"the frame has no {rx}")
    print(f"anchors: power {m.POWER_CELL} {m.POWER_FACE} (the entry at its centre), infeed {m.INFEED_CELL} {m.INFEED_FACE} (every tier's "
          f"mouth at y {top:g}), output {m.OUTPUT_CELL} {m.OUTPUT_FACE} (the spout's mouth at x {spout[1][0]:g}); the stack to y "
          f"{stack[1][1]:g}, the chamber, the pit and the spout in the frame")


def check_flue(v):
    """The fumes' way out, each tier: tier 2's collecting flue runs from over its stalls' back wall into the
    header, which closes the flue chamber's throat; tiers 3 and 4's hearth opens into the throat under the
    arch."""
    m = v.m
    (ix0, ix1), (iy0, iy1) = m.INLET
    z = m.FLUEBOX[2][0]
    hdr = aabb_of(v.named("stallflue", r"_header"))
    if not (hdr[0][0] <= ix0 and ix1 <= hdr[1][0] and hdr[0][1] <= iy0 and iy1 <= hdr[1][1] and abs(hdr[1][2] - z) < 1e-6):
        v.fail("the stalls' header does not close the chamber's throat")
    duct = aabb_of(v.named("stallflue", r"_duct"))
    if abs(duct[1][2] - hdr[0][2]) > 1e-6 or duct[0][2] > m.STALLS[0][0]:
        v.fail("the collecting flue does not run from the first stall to the header")
    for s0, s1 in m.STALLS:
        zc = (s0 + s1) / 2
        foot = [e for e in v.named("stalls", r"_backfoot\d") if e.aabb()[0][2] - 1e-6 <= zc <= e.aabb()[1][2] + 1e-6]
        if foot:
            v.fail(f"the stall at z {s0:g}..{s1:g} has no flue mouth at the foot of its back wall")
    hearth_top = max(e.aabb()[1][1] for e in v.by_part["hearth"] if e.name.startswith("hearth_bed"))
    if not (iy0 <= hearth_top + 1e-6 and iy1 <= m.CROWN_Y[0] and abs(m.HEARTH_Z[1] - z) < 1e-6):
        v.fail("the hearth does not open into the chamber's throat under the arch")
    print(f"flue: tier 2's collecting flue z {duct[0][2]:g}..{duct[1][2]:g} into the header over the throat (x {ix0:g}..{ix1:g}, "
          f"y {iy0:g}..{iy1:g}); the hearth (top {hearth_top:g}) opens into it under the arch's crown ({m.CROWN_Y[0]:g})")


def check_discharge(v):
    """The roasted ore's way out: the hearth's discharge hole is over the frame's pit, by the bridge; the
    pit's tunnel runs to the spout; the stalls' fronts open onto the forecourt where the pit is."""
    m = v.m
    (dx0, dx1), (dz0, dz1) = m.DISCHARGE
    (px0, px1), (pz0, pz1) = m.PIT
    if not (px0 <= dx0 and dx1 <= px1 and pz0 <= dz0 and dz1 <= pz1):
        v.fail("the discharge hole is not over the pit")
    if not (m.BRIDGE_Z[1] < dz0 < m.RABBLE_Z[0]):
        v.fail("the discharge hole is not between the bridge and the first rabble")
    reach = m.RABBLE_Z[0] - m.RABBLE_R
    if not reach < dz1:
        v.fail("the first rabble does not reach the discharge hole")
    if not m.ST_FRONT <= px0:
        v.fail("the pit is under the stalls, not in front of them")
    print(f"discharge: the hole x {dx0:g}..{dx1:g}, z {dz0:g}..{dz1:g} over the pit (x {px0:g}..{px1:g}, z {pz0:g}..{pz1:g}), in reach of "
          f"the first rabble (from z {reach:g}); the pit in the stalls' forecourt (fronts at x {m.ST_FRONT:g})")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        th, s = pose
        els = [e for pid in v.by_part if v.shows(pid, s) for e in v.posed(pid, th)]
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses (each state alone)")
    if bad:
        v.fail("faces z-fight")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_textures(v)
    check_tiers(v)
    check_floating(v)
    check_containment(v)
    check_anchors(v)
    check_flue(v)
    check_discharge(v)
    check_supports(v)
    check_gearing(v)
    check_yoke(v)
    check_clearances(v, 24, "clearances")
    if not quick:
        check_clearances(v, 216, "swept paths (every 5 degrees of the shaft over the rabbles' three turns)")
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
    for key in ("powerCell", "infeedCell", "outputCell"):
        if ship[key] not in [c["pos"] for c in ship["cells"]]:
            print(f"FAIL the {key} is not one of the cells")
            ok = False
    return ok
