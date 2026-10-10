"""The parting furnace generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its
rule. Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in
make_shape.py proves the shipped files are this model moved.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import (bearing_margin, box_overhang, cells_touched, coplanar_faces, euler_round_trip, frame_floating,
                               lid_gaps, obb_obb)
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
            if len(self.cache) > 20000:
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


def requires_of(v, pid):
    return next(p["requires"] for p in v.parts if p["id"] == pid)


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
    """Every requires value belongs to one tier and is named for it; the parts are listed tier by tier (the viewer
    lists the requires values in the order parts first need them), the frame last; every tier has a set."""
    m = v.m
    seen = []
    for p in v.parts:
        r = p["requires"]
        if r is None:
            continue
        tier = next((k for k, vs in m.TIERS.items() if r in vs), None)
        if tier is None or not r.startswith(f"t{tier}"):
            v.fail(f"part {p['id']} requires {r!r}, which is no tier's (or not named for its tier)")
        if r not in seen:
            seen.append(r)
    if seen != m.REQUIRES:
        v.fail(f"requires in the parts' order {seen}, want the tiers' order {m.REQUIRES}")
    if v.parts[-1]["id"] != "frame" or v.parts[-1]["requires"] is not None:
        v.fail("the frame must be the last part, with no requires")
    print(f"tiers: {', '.join(f'tier {k}: {len(vs)} sets' for k, vs in sorted(m.TIERS.items()))}; the parts listed tier by tier")


def states(m):
    """The tier states, cumulative: the frame, then each tier with all before it (the finished machine keeps every
    tier's sets: README, open questions)."""
    out = [("frame", set())]
    have = set()
    for k in sorted(m.TIERS):
        have |= set(m.TIERS[k])
        out.append((f"tier {k}", set(have)))
    return out


def check_floating(v):
    """Nothing floats at any tier: the static elements of the frame and of each tier's sets fitted so far are
    joined to the ground (an element of a later tier may not hold up one of an earlier one)."""
    m = v.m
    for label, fitted in states(m):
        els = [el for p in v.parts if not p["drivers"] and (p["requires"] is None or p["requires"] in fitted)
               for el in v.by_part[p["id"]]]
        seen, loose = frame_floating(els)
        print(f"floating, {label}: {len(seen)} static elements joined to the ground, {len(loose)} not")
        if loose:
            v.fail(f"elements float at {label}: {loose[:8]}")


def check_containment(v, poses):
    """Nothing leaves the machine box at any pose, and no moving part reaches into a cell the model does not hold
    at rest (the footprint)."""
    m = v.m
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    cells = set(m.footprint(v.els, v.parts))
    worst, where = -1e9, None
    stray = set()
    for pose in poses:
        for pid in v.by_part:
            for el in v.posed(pid, pose):
                lo, hi = el.aabb()
                o = box_overhang(lo, hi, box)
                if o > worst:
                    worst, where = o, (el.name, pose)
                if pid in m.MOVING:
                    stray |= {(el.name, c) for c in cells_touched(lo, hi) if c not in cells}
    print(f"containment over {len(poses)} poses: worst overhang {worst:.3f} voxels ({where[0]}); {len(cells)} cells at rest, "
          f"{len(stray)} moving elements reach outside them")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the machine box at {where[1]}")
    if stray:
        v.fail(f"moving parts reach outside the footprint: {sorted(stray)[:4]}")


def check_anchors(v):
    """The line shaft's oak end meets the power face at the power cell's centre; the input chute's mouth is on the
    input cell's face and the output chute's end on the output cell's; the anchor points are on those faces."""
    m = v.m
    rig = v.rig
    oak = v.named("lineshaft", r"^ls_oak")
    lo, hi = aabb_of(oak)
    pc = m.POWER_CELL
    centre = (pc[1] * 16 + 8, pc[2] * 16 + 8)
    print(f"anchors: power cell {pc} face {rig['powerFace']}, the oak continuation ends at x {hi[0]:.2f}, the shaft at (y {m.LS[0]}, "
          f"z {m.LS[1]}), the cell's centre {centre}")
    if abs(hi[0] - m.X_FACE) > 1e-6 or rig["powerFace"] != "east" or tuple(m.LS) != centre or int(hi[0] // 16) - 1 != pc[0]:
        v.fail("the line shaft does not meet the power face at the cell's centre")
    for label, rx, cell, face, key in (("input", r"^ch_in(mouth|_)", m.INPUT_CELL, "north", "input"),
                                       ("output", r"^ch_out_", m.OUTPUT_CELL, "south", "output")):
        lo, hi = aabb_of(v.named("chutes", rx))
        edge = lo[2] if face == "north" else hi[2]
        want = cell[2] * 16 if face == "north" else (cell[2] + 1) * 16
        pos = [c * 16 for c in rig[key]["pos"]]
        inside = all(cell[k] * 16 - 1e-6 <= pos[k] <= (cell[k] + 1) * 16 + 1e-6 for k in range(3))
        span_ok = cell[0] * 16 <= lo[0] and hi[0] <= (cell[0] + 1) * 16
        print(f"anchors: the {label} chute's end at z {edge:.2f} on the {face} face of {cell} (z {want}); its anchor at "
              f"{[round(c, 2) for c in pos]}")
        if abs(edge - want) > 0.02 or not inside or not span_ok or abs(pos[2] - want) > 1e-6:
            v.fail(f"the {label} chute does not meet its cell's {face} face, or its anchor is off it")


TEX_RULES = [
    (r"^ls_oak", "oak"),
    (r"^(ls_shaft|ls_collar|ls_pin|yk_rod|yk_slipper|st_(shaft|collar|hub|wheel)|bp_|tb_gland|tb_spindle|fr_s\d_bolt)", "steel"),
    (r"^(ls_flange|ls_crank|yk_(top|bottom|side|piston)|tb_(footflange|barrel|flange|cover|valve|guide|lip|foot|crossbar|main|elbow|wheel|stand|saddle)"
     r"|fr_s\d_|fr_band|cp_(grate|ring|channel|pot|tuyere|nozzle|firedoor|firehinge|ashdoor|buck|tie|nut|lintel_iron)|lq_(plate|rim|spout|pot_|firedoor|buck|tie|nut)"
     r"|ac_(apron|firedoor|tray)|pr_hoop|kt_(bowl|wall|flange|skim|firedoor|ashdoor)|gl_|ch_|st_paddle)", "iron"),
    (r"^(cp_(hearth|wall|lintel_|lintel$|vault|haunch|gable)|lq_(base|wall|vault|haunch|gable)|ac_(back|pier|hood|firebox)|kt_set|fr_stack_(base|low|high|cap[nswe]))", "brick"),
    (r"^(cp_bridge|lq_step|fr_stack_corbel)", "firebrick"),
    (r"^fr_floor", "paving"),
    (r"^fr_stack_soot", "soot"),
    (r"^cp_coke", "coke"),
    (r"^cp_fire\d", "ember"),
    (r"^cp_boneash", "boneash"),
    (r"^(cp_lead|kt_bath)", "lead"),
    (r"^cp_litharge", "litharge"),
    (r"^(lq_cake|lq_potmetal)", "tin"),
    (r"^lq_dross", "dross"),
    (r"^kt_crust", "zinc"),
    (r"^ac_sand", "sand"),
    (r"^ac_(vessel|lip)", "stoneware"),
    (r"^ac_acid", "acid"),
    (r"^pr_(stave|bottom)", "planks"),
    (r"^pr_bar", "oak"),
    (r"^pr_plate", "copper"),
    (r"^pr_solution", "solution"),
]


def check_textures(v):
    bad, unruled = [], []
    for el in v.els:
        for rx, tex in TEX_RULES:
            if re.match(rx, el.name):
                got = {f["texture"].lstrip("#") for f in el.faces.values()}
                if got != {tex}:
                    bad.append((el.name, sorted(got), tex))
                break
        else:
            unruled.append(el.name)
    print(f"textures: {len(TEX_RULES)} rules (iron castings, steel shafts, pins and gears, oak for the axle's continuation, brick and "
          f"fire brick, the work's own materials): {len(bad)} elements break them, {len(unruled)} have no rule")
    if bad:
        v.fail(f"textures by role: {bad[:4]}")
    if unruled:
        v.fail(f"elements no texture rule covers: {unruled[:6]}")


# ---------------------------------------------------------------- mechanism
def check_gearing(v):
    """The bevel pair: both axes pass through the cone apex, the pitch radii are in the teeth's ratio, and the
    pitch point (where the pitch cones touch at their outer ends) moves alike on both (finite differences of the
    posed rig, both ways round), so they roll without slipping."""
    m = v.m
    ap = m.APEX
    if abs(m.LS[0] - ap[1]) > 1e-9 or abs(m.LS[1] - ap[2]) > 1e-9 or abs(m.KET_C[0] - ap[0]) > 1e-9 or abs(m.KET_C[1] - ap[2]) > 1e-9:
        v.fail("the bevel pair's axes do not cross at its apex")
    if abs(m.R_P / m.R_W - m.PINION_TEETH / m.WHEEL_TEETH) > 1e-9 or abs(m.STIR_RATIO - m.PINION_TEETH / m.WHEEL_TEETH) > 1e-9:
        v.fail("the stirrer's ratio is not the bevel pair's teeth")
    p = [ap[0] + m.R_W, ap[1] - m.R_W / math.tan(m.DELTA_W), ap[2]]
    worst = 0.0
    for th in (0.0, 0.4, 1.9, -2.7, 5.3, 11.0):
        a, b = (th, abs(th)), (th + 1e-4, abs(th + 1e-4))
        # the material point of each gear that is at p at pose a, carried to pose b
        def moved(pid):
            ma, mb = v.mat(pid, a), v.mat(pid, b)
            r = [row[:3] for row in ma[:3]]
            t = [ma[i][3] * 16 for i in range(3)]
            loc = [sum(r[j][i] * (p[j] - t[j]) for j in range(3)) for i in range(3)]
            return [sum(mb[i][j] * loc[j] for j in range(3)) + mb[i][3] * 16 - p[i] for i in range(3)]
        da = moved("bevelpinion")
        db = moved("stirrer")
        size = max(math.sqrt(sum(x * x for x in da)), 1e-12)
        err = math.sqrt(sum((da[i] - db[i]) ** 2 for i in range(3))) / size
        worst = max(worst, err)
        if err > 1e-3 or size < 1e-6:
            v.fail(f"the bevel pair slips at theta {th}: pinion {da}, wheel {db}")
    print(f"gearing: a bevel pair, {m.PINION_TEETH} to {m.WHEEL_TEETH} (module {m.GEAR_MOD}), axes crossing at {[round(c, 2) for c in ap]}, "
          f"the pitch point moving alike on both (worst {worst:.1e}): the stirrer turns {m.STIR_RATIO} of the line shaft")


def check_yoke(v):
    """The Scotch yoke: the crank pin's centre stays on the slot's centre line (within the slot's clearance) and
    inside its length at every angle, and the yoke stays in its guides; it rises exactly with the pin."""
    m = v.m
    pin0 = [8.0, m.LS[0], m.LS[1] - m.CRANK_R]
    worst_y, worst_z = 0.0, 0.0
    for i in range(48):
        th = 2 * math.pi * i / 48
        pose = (th, th)
        pin = v.point("lineshaft", pose, pin0)
        slot = v.point("yoke", pose, [8.0, m.LS[0], m.LS[1]])
        dy, dz = pin[1] - slot[1], pin[2] - slot[2]
        worst_y, worst_z = max(worst_y, abs(dy)), max(worst_z, abs(dz))
    room = m.SLOT_L - 0.866
    print(f"yoke: the pin stays {worst_y:.1e} off the slot's centre line and {worst_z:.3f} along it (room {room:.3f}); the yoke's "
          f"stroke {2 * m.CRANK_R}")
    if worst_y > 1e-6 or worst_z > room + 1e-6:
        v.fail("the crank pin leaves the yoke's slot")


def check_supports(v):
    """The line shaft in its four plummer blocks (each round it at rest and turned 45 degrees), the stirrer's
    shaft in the gallows' two bosses, the yoke's slippers in their guides."""
    m = v.m
    frame = v.by_part["frame"]
    shaft = v.named("lineshaft", r"^ls_shaft")
    found, _ = shaft_supports(frame, shaft, 0, m.LS)
    named = sorted(n for n in found if re.match(r"^fr_s\d_bearing$", n))
    print(f"support line shaft: {len(named)} bearings {', '.join(named)}")
    if len(named) != 4:
        v.fail("the line shaft is not carried by its four plummer blocks")
    for pose in (m.REST, (math.pi / 4, math.pi / 4)):
        els = v.named("lineshaft", r"^ls_shaft", pose)
        for b in v.named("frame", r"^fr_s\d_bearing$"):
            mg = bearing_margin(b, els, 0)
            if not 0 < mg < 1e8:
                v.fail(f"{b.name} does not enclose the line shaft at {pose} (margin {mg:.3f})")
    gal = v.by_part["gallows"]
    st = v.named("stirrer", r"^st_shaft")
    found, _ = shaft_supports(gal, st, 1, (m.KET_C[0], m.KET_C[1]))
    bosses = sorted(n for n in found if "boss" in n)
    print(f"support stirrer: {len(bosses)} bosses {', '.join(bosses)}")
    if len(bosses) < 2:
        v.fail("the stirrer's shaft is not carried by two bearings")
    for pose in (m.REST, (math.pi / 4, math.pi / 4)):
        els = v.named("stirrer", r"^st_shaft", pose)
        for b in v.named("gallows", r"boss"):
            mg = bearing_margin(b, els, 1)
            if not 0 < mg < 1e8:
                v.fail(f"{b.name} does not enclose the stirrer's shaft at {pose} (margin {mg:.3f})")
    (g0, g1), (h0, h1) = m.guide_z()
    for i in range(12):
        th = 2 * math.pi * i / 12
        for s in v.named("yoke", r"slipper", (th, th)):
            lo, hi = s.aabb()
            z_ok = (abs(lo[2] - g1) < 1e-6) if s.name.endswith("n") else (abs(hi[2] - h0) < 1e-6)
            if not z_ok or lo[1] < m.TUB_Y[1] + 1.2 + 2.0 - 1e-6 or hi[1] > m.GUIDE_TOP - 1.0 + 1e-6:
                v.fail(f"{s.name} leaves its guide at theta {th:.2f}")
    print("support: the yoke's slippers run on their guides' faces between the lips over the whole stroke")


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way round.
ALLOWED = [
    ("lineshaft", r"^ls_(shaft|collar|flange)", "frame", r"^fr_s\d_(bearing|cap)"),
    ("lineshaft", r"^ls_pin", "yoke", r"^yk_(top|bottom|side)"),
    ("yoke", r"^yk_(rod|piston)", "tub", r"^tb_(barrel|flange|cover|gland)"),
    ("yoke", r"^yk_slipper", "tub", r"^tb_(guide|lip)"),
    ("bevelpinion", None, "lineshaft", r"^ls_shaft"),
    ("stirrer", r"^st_(shaft|hub|collar)", "gallows", r"^gl_(boss|upper|lower)"),
    ("stirrer", r"^st_shaft", "kettle", r"^kt_bath"),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose):
    """Pairs of elements of different parts, at least one moving, that overlap by more than 0.02 voxels."""
    m = v.m
    items = []
    for pid in v.by_part:
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
                if pa == pb or (pa not in m.MOVING and pb not in m.MOVING):
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
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {p[0]:.3f}" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = [e for pid in v.by_part for e in v.posed(pid, pose)]
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses")
    if bad:
        v.fail("faces z-fight")


def inside_disc(p, c, r):
    return math.hypot(p[0] - c[0], p[2] - c[1]) <= r


def check_flows(v):
    """Where the metal goes: the litharge channel ends over its pot, the liquation's spout over its receiving pot,
    the blast main reaches the tuyere, the input chute ends over the kettle's bath, the siphon's inner leg dips
    into the bath and its outer leg ends in the output chute's head box."""
    m = v.m
    out = []
    ch = aabb_of(v.named("cupel", r"^cp_channel"))
    end = ((ch[0][0] + ch[1][0]) / 2, 0.0, ch[1][2] - 0.3)
    out.append(("litharge channel over its pot", inside_disc(end, m.POT_L, 2.9) and ch[0][1] > 8.9))
    sp = aabb_of(v.named("liquation", r"^lq_spout"))
    pot = m.liq_world((m.POT_Q[0], 0.0, m.POT_Q[1]))
    end = ((sp[0][0] + sp[1][0]) / 2, 0.0, sp[1][2] - 0.3)
    out.append(("liquation spout over its pot", inside_disc(end, (pot[0], pot[2]), 2.8) and sp[0][1] > 8.5))
    main = aabb_of(v.named("tub", r"^tb_mainin"))
    tuy = aabb_of(v.named("cupel", r"^cp_tuyere"))
    out.append(("blast main into the tuyere", main[1][2] >= tuy[0][2] - 1e-6 and abs((main[0][0] + main[1][0]) / 2 - (tuy[0][0] + tuy[1][0]) / 2) < 1e-6))
    ci = aabb_of(v.named("chutes", r"^ch_in_bottom"))
    end = ((ci[0][0] + ci[1][0]) / 2, 0.0, ci[1][2])
    out.append(("input chute's end over the bath", inside_disc(end, m.KET_C, m.KET_R[0]) and ci[0][1] > m.SET_TOP + 0.8))
    si = aabb_of(v.named("chutes", r"^ch_siphonin"))
    out.append(("siphon's leg in the bath", si[0][1] < m.BATH_Y - 1.0 and inside_disc(((si[0][0] + si[1][0]) / 2, 0, (si[0][2] + si[1][2]) / 2), m.KET_C, m.KET_R[0] - 0.7)))
    so = aabb_of(v.named("chutes", r"^ch_siphonout"))
    hb = aabb_of(v.named("chutes", r"^ch_head"))
    out.append(("siphon's outer leg in the head box", hb[0][0] < so[0][0] and so[1][0] < hb[1][0] and hb[0][2] < so[0][2] and so[1][2] < hb[1][2]
                and so[0][1] < hb[1][1]))
    for label, good in out:
        if not good:
            v.fail(f"flow: {label}")
    print("flows: " + "; ".join(f"{label} {'yes' if good else 'NO'}" for label, good in out))


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_tiers(v)
    check_floating(v)
    check_containment(v, [m.REST] + m.cycle_poses(24))
    check_anchors(v)
    check_textures(v)
    check_gearing(v)
    check_yoke(v)
    check_supports(v)
    check_flows(v)
    check_clearances(v, [m.REST] + m.cycle_poses(96) + [(-1.3, 1.3), (-4.1, 4.1)])
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
    gaps = lid_gaps(ship["cells"], columns=m.no_lid)
    hollow = sum(1 for c in ship["cells"] if c.get("hollow"))
    lids = sum(1 for c in ship["cells"] if "lid" in c)
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, {hollow} hollow, "
          f"{lids} lids (none by design: README, open questions)")
    if gaps or hollow:
        ok = False
        print(f"FAIL lids or hollow cells: {gaps} {hollow}")
    return ok
