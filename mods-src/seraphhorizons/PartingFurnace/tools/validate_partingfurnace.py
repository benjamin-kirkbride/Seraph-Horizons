"""The parting furnace generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its
rule. Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in
make_shape.py proves the shipped files are this model moved.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import bearing_margin, box_overhang, cells_touched, coplanar_faces, euler_round_trip, frame_floating, obb_obb
from machinegen.checks import supports as shaft_supports
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import driver_input, part_of, posed


class V:
    def __init__(self, m, els, parts, rig):
        self.m, self.els, self.parts, self.rig = m, els, parts, rig
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        self.cache = {}
        self.ok = True
        # the water pipe is drawn in one metal at a time: the other metals' copies are checked as copies only
        self.copies = set(m.PIPES[1:])

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


def check_levels(v):
    """Every requires value belongs to one level, or is the water pipe's in a metal (fitted with the acid set); the
    parts are listed level by level (the viewer lists the requires values in the order parts first need them), the
    water pipe's after the acid set's, the frame last; every level has a set."""
    m = v.m
    seen = []
    for p in v.parts:
        r = p["requires"]
        if r is None:
            continue
        if sum(r in vs for vs in m.LEVELS.values()) + (r in m.PIPES) != 1:
            v.fail(f"part {p['id']} requires {r!r}, which is not one level's nor a water pipe's")
        if r not in seen:
            seen.append(r)
    if seen != m.REQUIRES:
        v.fail(f"requires in the parts' order {seen}, want the levels' order {m.REQUIRES}")
    if v.parts[-1]["id"] != "frame" or v.parts[-1]["requires"] is not None:
        v.fail("the frame must be the last part, with no requires")
    print(f"levels: {', '.join(f'level {k} ({m.LEVEL_TIERS[k]}): {len(vs)} sets' for k, vs in sorted(m.LEVELS.items()))}; "
          "the parts listed level by level")


def states(m):
    """The level states, cumulative: the frame, then each level with all before it (a later level adds parts and
    takes none away)."""
    out = [("frame", set())]
    have = set()
    for k in sorted(m.LEVELS):
        have |= set(m.LEVELS[k])
        if k == "1":
            have.add(m.PIPES[0])                 # the water pipe, in the first metal (the others are its copies)
        out.append((f"level {k}", set(have)))
    return out


def per_level_sets(m):
    return {k: set(vs) for k, vs in m.LEVELS.items()}


def check_floating(v):
    """Nothing floats at any level: the static elements of the frame and of each level's sets fitted so far are
    joined to the ground (an element of a later level may not hold up one of an earlier one)."""
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


def on_face(lo, hi, cell, face):
    """(the box's extent on the face's axis at the face's side, the face's coordinate) for an outer face."""
    k = {"east": 0, "west": 0, "north": 2, "south": 2}[face]
    far = face in ("east", "south")
    return (hi[k] if far else lo[k]), (cell[k] + (1 if far else 0)) * 16


def check_anchors(v):
    """The engine shaft's oak end meets the power face at the power cell's centre; the water pipe meets the water
    face at its cell's centre in ppex's 6 x 6 section; the input chute's mouth is on the input cell's face and the
    output spout's lip on the output cell's, each within its cell, the anchor points on those faces; the anchor
    cells and faces are on the footprint's outside and differ."""
    m = v.m
    rig = v.rig
    cells = set(m.footprint(v.els, v.parts))
    lo, hi = aabb_of(v.named("shaft", r"^sh_oak"))
    pc = m.POWER_CELL
    edge, face = on_face(lo, hi, pc, "north")
    centre = (pc[0] * 16 + 8, pc[1] * 16 + 8)
    print(f"anchors: power cell {pc} face {m.POWER_FACE}, the oak continuation ends at z {edge:.2f} (face z {face}), the shaft at "
          f"{m.SHAFT}, the cell's centre {centre}")
    if abs(edge - face) > 1e-6 or tuple(m.SHAFT) != centre:
        v.fail("the engine shaft does not meet the power face at the cell's centre")
    first = m.PIPES[0]
    lo, hi = aabb_of(v.named(first, rf"^{first}_inlet"))
    wc = m.WATER_CELL
    edge, face = on_face(lo, hi, wc, m.WATER_FACE)
    centre = (wc[0] * 16 + 8, wc[1] * 16 + 8)
    section = (hi[0] - lo[0], hi[1] - lo[1])
    mid = ((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2)
    print(f"anchors: water cell {wc} face {m.WATER_FACE}, the pipe ends at z {edge:.2f} (face z {face}), centred {mid}, "
          f"section {section}")
    if abs(edge - face) > 1e-6 or max(abs(mid[0] - centre[0]), abs(mid[1] - centre[1])) > 1e-6 or max(abs(s - 6) for s in section) > 1e-6:
        v.fail("the water pipe does not meet the water face at the cell's centre in ppex's section")
    for label, part, rx, cell, face, key in (("input", "chutes", r"^ch_in_", m.INPUT_CELL, m.INPUT_FACE, "input"),
                                             ("output", "chutes", r"^ch_spout_", m.OUTPUT_CELL, m.OUTPUT_FACE, "output")):
        lo, hi = aabb_of(v.named(part, rx))
        edge, want = on_face(lo, hi, cell, face)
        k = {"east": 0, "south": 2}[face]
        pos = [c * 16 for c in rig[key]["pos"]]
        inside = all(cell[q] * 16 - 1e-6 <= pos[q] <= (cell[q] + 1) * 16 + 1e-6 for q in range(3))
        across = [q for q in range(3) if q != k]
        span_ok = all(cell[q] * 16 - 1e-6 <= lo[q] and hi[q] <= (cell[q] + 1) * 16 + 1e-6 for q in across if q != 1)
        print(f"anchors: the {label} chute's end at {'xyz'[k]} {edge:.3f} on the {face} face of {cell} ({want}); its anchor at "
              f"{[round(c, 2) for c in pos]}")
        if abs(edge - want) > 0.02 or not inside or not span_ok or abs(pos[k] - want) > 1e-6:
            v.fail(f"the {label} chute does not meet its cell's {face} face, or its anchor is off it")
    normal = {"north": (0, 0, -1), "south": (0, 0, 1), "east": (1, 0, 0), "west": (-1, 0, 0)}
    seen = set()
    for cell, face in ((m.POWER_CELL, m.POWER_FACE), (m.WATER_CELL, m.WATER_FACE), (m.INPUT_CELL, m.INPUT_FACE),
                       (m.OUTPUT_CELL, m.OUTPUT_FACE)):
        beyond = tuple(cell[q] + normal[face][q] for q in range(3))
        if tuple(cell) not in cells or beyond in cells:
            v.fail(f"anchor cell {cell}'s {face} face is not on the footprint's outside")
        if (tuple(cell), face) in seen:
            v.fail(f"two anchors share {cell} {face}")
        seen.add((tuple(cell), face))


TEX_RULES = [
    (r"^sh_oak", "oak"),
    (r"^(sh_shaft|sh_pin|yk_rod|yk_slipper|st_shaft|st_wheel|bp_|en_gland)", "steel"),
    (r"^(sh_web|yk_(top|bottom|side)|en_(ped|cyl|cover|valvechest|guide|foot|elbow|main)"
     r"|cp_(grate|ring|channel|pot_|tuyere|firedoor|firehinge|ashdoor|buck|tie|lintel_iron)|lq_(plate|rim|spout|pot_|firedoor|buck|tie)"
     r"|ac_(firedoor|tray)|kt_(flange|skimmer|firedoor|ashdoor)|gl_|ch_|st_paddle)", "iron"),
    (r"^(cp_(hearth|wall|vault|haunch|gable)|lq_(base|wall|vault|haunch|gable)|ac_(pier|hood|firebox)|kt_setting"
     r"|fr_stack_(base|shaft))", "brick"),
    (r"^(cp_bridge|lq_step|fr_stack_cap)", "firebrick"),
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
    (r"^ac_(vessel|rim)", "stoneware"),
    (r"^ac_acid", "acid"),
    (r"^pipecopper_joint", "solder"),
    (r"^pipelead_joint", "solder"),
    (r"^pipecopper_", "pipecopper"),
    (r"^pipelead_", "pipelead"),
    (r"^ac_(cock|nozzle)", "brass"),
]


def check_textures(v):
    bad, unruled = [], []
    for el in v.els:
        for rx, tex in TEX_RULES:
            if re.match(rx, el.name):
                got = {f["texture"].lstrip("#") for f in el.faces.values()}
                if got and got != {tex}:
                    bad.append((el.name, sorted(got), tex))
                break
        else:
            unruled.append(el.name)
    print(f"textures: {len(TEX_RULES)} rules (iron castings, steel shafts, pins and gears, oak for the axle's continuation, brick and "
          f"fire brick, ppex's copper or lead pipe soldered and a brass cock for the water, the work's own materials): {len(bad)} elements break them, "
          f"{len(unruled)} have no rule")
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
    if abs(m.SHAFT[0] - ap[0]) > 1e-9 or abs(m.SHAFT[1] - ap[1]) > 1e-9 or abs(m.KET_C[0] - ap[0]) > 1e-9 or abs(m.KET_C[1] - ap[2]) > 1e-9:
        v.fail("the bevel pair's axes do not cross at its apex")
    if abs(m.R_P / m.R_W - m.PINION_TEETH / m.WHEEL_TEETH) > 1e-9 or abs(abs(m.STIR_RATIO) - m.PINION_TEETH / m.WHEEL_TEETH) > 1e-9:
        v.fail("the stirrer's ratio is not the bevel pair's teeth")
    p = [ap[0], ap[1] - m.R_P, ap[2] - m.R_W]
    worst = 0.0
    for th in (0.0, 0.4, 1.9, -2.7, 5.3, 11.0):
        a, b = (th, abs(th)), (th + 1e-4, abs(th + 1e-4))

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
          f"the pitch point moving alike on both (worst {worst:.1e}): the stirrer turns {m.STIR_RATIO} of the engine shaft")


def check_yoke(v):
    """The Scotch yoke: the crank pin's centre stays on the slot's centre line (it rises exactly with the yoke)
    and inside the slot's length at every angle."""
    m = v.m
    x, y = m.SHAFT
    zc = (m.YOKE_Z[0] + m.YOKE_Z[1]) / 2
    pin0 = [x + m.CRANK_R, y, zc]
    worst_y, worst_x = 0.0, 0.0
    for i in range(48):
        th = 2 * math.pi * i / 48
        pose = (th, th)
        pin = v.point("shaft", pose, pin0)
        slot = v.point("yoke", pose, [x, y, zc])
        worst_y, worst_x = max(worst_y, abs(pin[1] - slot[1])), max(worst_x, abs(pin[0] - slot[0]))
    room = m.SLOT_L - 0.866
    print(f"yoke: the pin stays {worst_y:.1e} off the slot's centre line and {worst_x:.3f} along it (room {room:.3f}); the yoke's "
          f"stroke {2 * m.CRANK_R}")
    if worst_y > 1e-6 or worst_x > room + 1e-6:
        v.fail("the crank pin leaves the yoke's slot")
    # the piston rod stays in its gland over the whole stroke
    rod = v.by_part["yoke"]
    low = min(e.aabb()[0][1] for e in rod if e.name == "yk_rod")
    if not (m.GLAND_Y[0] < low + m.CRANK_R <= m.GLAND_Y[1] and low - m.CRANK_R > m.CYL_Y[0]):
        v.fail("the piston rod leaves its gland at the top of the stroke, or reaches the cylinder's foot at the bottom")
    print(f"yoke: the piston rod's foot runs {low - m.CRANK_R:.2f}..{low + m.CRANK_R:.2f}, inside the cylinder and the gland "
          f"(to {m.GLAND_Y[1]})")


def check_supports(v):
    """The engine shaft in its two pedestal bearings (each round it at rest and turned 45 degrees), the stirrer's
    shaft in the gallows' two bosses, the yoke's slippers on their guides over the whole stroke."""
    m = v.m
    engine = v.by_part["engine"]
    shaft = v.named("shaft", r"^sh_shaft")
    found, _ = shaft_supports(engine, shaft, 2, m.SHAFT)
    named = sorted(n for n in found if re.match(r"^en_ped[ns]_bearing$", n))
    print(f"support engine shaft: {len(named)} bearings {', '.join(named)}")
    if len(named) != 2:
        v.fail("the engine shaft is not carried by its two pedestal bearings")
    for pose in (m.REST, (math.pi / 4, math.pi / 4)):
        els = v.named("shaft", r"^sh_shaft", pose)
        for b in v.named("engine", r"^en_ped[ns]_bearing$"):
            mg = bearing_margin(b, els, 2)
            if not 0 < mg < 1e8:
                v.fail(f"{b.name} does not enclose the engine shaft at {pose} (margin {mg:.3f})")
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
    (g0, g1), (h0, h1) = m.guide_x()
    for i in range(12):
        th = 2 * math.pi * i / 12
        for s in v.named("yoke", r"slipper", (th, th)):
            lo, hi = s.aabb()
            x_ok = (abs(lo[0] - g1) < 1e-6) if s.name.endswith("w") else (abs(hi[0] - h0) < 1e-6)
            if not x_ok or lo[1] < m.CYL_TOP + 0.8 - 1e-6 or hi[1] > m.GUIDE_TOP + 1e-6:
                v.fail(f"{s.name} leaves its guide at theta {th:.2f}")
    print("support: the yoke's slippers run on their guides' faces, between the feet and the cap, over the whole stroke")


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way round.
ALLOWED = [
    ("shaft", r"^sh_shaft", "engine", r"^en_ped[ns]_bearing"),
    ("shaft", r"^sh_pin", "yoke", r"^yk_(top|bottom|side)"),
    ("yoke", r"^yk_rod", "engine", r"^en_(cyl|cover|gland)"),
    ("yoke", r"^yk_slipper", "engine", r"^en_guide"),
    ("bevelpinion", None, "shaft", r"^sh_shafts"),
    ("stirrer", r"^st_shaft", "gallows", r"^gl_(boss|upper|lower)"),
    ("stirrer", r"^st_(shaft|paddle)", "kettle", r"^kt_(bath|setting)"),
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
        els = [e for pid in v.by_part if pid not in v.copies for e in v.posed(pid, pose)]
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses")
    if bad:
        v.fail("faces z-fight")


def inside_disc(p, c, r):
    return math.hypot(p[0] - c[0], p[2] - c[1]) <= r


def mid(lo, hi, k):
    return (lo[k] + hi[k]) / 2


def check_flows(v):
    """Where the metal, the blast and the water go: the litharge channel ends over its pot, the liquation's spout
    over its receiving pot, the blast pipe runs from the valve chest to the tuyere, the input chute ends over the
    kettle's bath, the siphon's inner leg dips into the bath and its outer leg ends in the output spout, and the
    water cock's nozzle is over the parting vessel's mouth."""
    m = v.m
    out = []
    ch = aabb_of(v.named("cupel", r"^cp_channel"))
    end = (mid(*ch, 0), 0.0, ch[1][2] - 0.3)
    out.append(("litharge channel over its pot", inside_disc(end, m.POT_L, 2.4) and ch[0][1] > 7.3))
    sp = aabb_of(v.named("liquation", r"^lq_spout"))
    pot = m.liq_world((m.POT_Q[0], 0.0, m.POT_Q[1]))
    end = (mid(*sp, 0), 0.0, sp[1][2] - 0.3)
    out.append(("liquation spout over its pot", inside_disc(end, (pot[0], pot[2]), 2.0) and sp[0][1] > 7.1))
    chest = aabb_of(v.named("engine", r"^en_valvechest"))
    e1 = aabb_of(v.named("engine", r"^en_elbow1"))
    e3 = aabb_of(v.named("engine", r"^en_elbow3"))
    tuy = aabb_of(v.named("cupel", r"^cp_tuyere"))
    out.append(("blast pipe from the valve chest", abs(e1[1][0] - chest[0][0]) < 1e-6 and chest[0][1] <= e1[0][1] and e1[1][1] <= chest[1][1] + 1e-6))
    out.append(("blast pipe into the tuyere", abs(tuy[0][2] - e3[1][2]) < 1e-6 and abs(mid(*tuy, 0) - mid(*e3, 0)) < 1e-6
                and abs(mid(*tuy, 1) - mid(*e3, 1)) < 1e-6 and tuy[1][2] >= m.CUP_Z[0] + m.CUP_WALL))
    ci = aabb_of(v.named("chutes", r"^ch_in_bottom"))
    end = (ci[0][0] + 0.3, 0.0, mid(*ci, 2))
    out.append(("input chute's end over the bath", inside_disc(end, m.KET_C, m.KET_R) and ci[0][1] > m.FLANGE_Y[1]))
    si = aabb_of(v.named("chutes", r"^ch_siphonin"))
    out.append(("siphon's leg in the bath", si[0][1] < m.BATH_Y[0] and inside_disc((mid(*si, 0), 0, mid(*si, 2)), m.KET_C, m.KET_R - 0.5)))
    so = aabb_of(v.named("chutes", r"^ch_siphonout"))
    sb = aabb_of(v.named("chutes", r"^ch_spout_bottom"))
    out.append(("siphon's outer leg in the spout", sb[0][0] < so[0][0] and so[1][0] < sb[1][0] and sb[0][2] < so[0][2] and so[1][2] < sb[1][2]
                and so[0][1] > sb[1][1] - 0.6 and so[0][1] < sb[1][1] + m.CHUTE_SIDE))
    nz = aabb_of(v.named("acid", r"^ac_nozzle"))
    tip = (mid(*nz, 0), 0.0, mid(*nz, 2))
    out.append(("water cock's nozzle over the vessel's mouth", inside_disc(tip, m.VESSEL_C, m.RIM_R[0] - 0.6) and nz[0][1] > m.RIM_Y[1]))
    for label, good in out:
        if not good:
            v.fail(f"flow: {label}")
    print("flows: " + "; ".join(f"{label} {'yes' if good else 'NO'}" for label, good in out))


def check_pipes(v):
    """The water pipe as UnifiedPipes draws soldered ppex pipe (the rosser's drip pipes' way): a straight length of
    ppex's section, four walls round the bore, from the water face to the brass cock, a wiped joint of solder where it
    meets the cock; each other metal's part the same elements in its own pipe texture, the joint in solder."""
    m = v.m
    first = m.PIPES[0]
    els = sorted(v.by_part[first], key=lambda e: e.name)
    walls = [e for e in els if e.name.startswith(f"{first}_inlet_")]
    joint = [e for e in els if e.name.startswith(f"{first}_joint")]
    lo, hi = aabb_of(walls)
    clo, chi = aabb_of(v.named("acid", r"^ac_cock"))
    bad = []
    if len(walls) != 4:
        bad.append(f"{len(walls)} walls, 4 wanted")
    if abs(lo[2] - m.COCK_Z[1]) > 0.05 or abs(chi[2] - m.COCK_Z[1]) > 1e-6 or not (clo[0] <= lo[0] + 0.2 and hi[1] <= chi[1]):
        bad.append("the pipe does not end in the cock")
    if len(joint) != 4 or abs(aabb_of(joint)[0][2] - m.COCK_Z[1]) > 0.05:
        bad.append("no wiped joint where the pipe meets the cock")
    for metal in m.PIPES[1:]:
        copies = sorted(v.by_part.get(metal, []), key=lambda e: e.name)
        if len(copies) != len(els):
            bad.append(f"{metal} has {len(copies)} elements, {len(els)} wanted")
            continue
        for a, b in zip(els, copies):
            if b.name != metal + a.name[len(first):] or max(abs(a.c[k] - b.c[k]) + abs(a.size[k] - b.size[k]) for k in range(3)) > 1e-9:
                bad.append(f"{b.name} is not {a.name} in {metal}")
    for msg in bad:
        v.fail(f"pipes: {msg}")
    print(f"pipes: {len(els)} elements a metal in {', '.join(m.PIPES)}: ppex's section from the water face (z {hi[2]:.1f}) to the "
          f"cock (z {lo[2]:.1f}), a wiped joint there")


def check_reverse(v):
    """Every motion works either way round, since the axle may turn either way: no driver reads the shaft's travel or
    anything else that knows the direction; over a turn backwards the yoke covers the same stroke as forwards (the
    blowing cylinder's valves are self-acting flaps, opened and shut by the air, so it blows either way); the paddles
    lie symmetric about the stirrer's axis (they stir alike either way); and the clearances hold over a turn
    backwards."""
    m = v.m
    for p in v.parts:
        for d in p["drivers"]:
            if driver_input(d) != "theta" or d.get("rectified"):
                v.fail(f"reverse: {p['id']} reads {driver_input(d)}, which knows the direction")
    ys = {sign: [v.mat("yoke", (sign * TAU * i / 48, TAU * i / 48))[1][3] * 16 for i in range(49)] for sign in (1, -1)}
    fw, bw = (min(ys[1]), max(ys[1])), (min(ys[-1]), max(ys[-1]))
    if max(abs(fw[0] - bw[0]), abs(fw[1] - bw[1])) > 1e-9 or abs(fw[1] - fw[0] - 2 * m.CRANK_R) > 1e-6:
        v.fail(f"reverse: the yoke's stroke is {fw} forwards and {bw} backwards")
    for e in v.named("stirrer", r"^st_paddle"):
        lo, hi = e.aabb()
        if max(abs((lo[0] + hi[0]) / 2 - m.KET_C[0]), abs((lo[2] + hi[2]) / 2 - m.KET_C[1])) > 1e-6:
            v.fail(f"reverse: {e.name} is not symmetric about the stirrer's axis")
    print(f"reverse: every driver reads theta alone; the yoke's stroke {fw[0]:.2f}..{fw[1]:.2f} voxels either way round; the "
          f"paddles symmetric about the stirrer's axis")
    check_clearances(v, [(-TAU * i / 48, TAU * i / 48) for i in range(1, 49)])


TAU = 2 * math.pi


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_levels(v)
    check_floating(v)
    check_containment(v, [m.REST] + m.cycle_poses(24))
    check_anchors(v)
    check_textures(v)
    check_gearing(v)
    check_yoke(v)
    check_supports(v)
    check_flows(v)
    check_pipes(v)
    check_reverse(v)
    check_clearances(v, [m.REST] + m.cycle_poses(96) + [(-1.3, 1.3), (-4.1, 4.1)])
    if not quick:
        check_zfight(v)
    return v.ok


def validate_files(m, shape, frame_shape, ship):
    """The written files: every texture declared; no hollow cells and no lids; every cell's fitted boxes keyed by a
    requires value; and what collides at each level (the frame's boxes plus the fitted sets' boxes)."""
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL textures used but not declared: {missing}")
        ok = False
    cells = ship["cells"]
    hollow = sum(1 for c in cells if c.get("hollow"))
    lids = sum(1 for c in cells if "lid" in c)
    bad_keys = sorted({k for c in cells for k in c.get("fitted", {}) if k not in m.REQUIRES})
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(cells)} cells, {hollow} hollow, "
          f"{lids} lids (none by design: README, open questions)")
    if hollow or lids or bad_keys:
        ok = False
        print(f"FAIL hollow cells, lids or fitted keys that are no requires value: {hollow} {lids} {bad_keys}")
    for label, fitted in states(m):
        solid = [c["pos"] for c in cells if c.get("boxes") or any(k in fitted for k in c.get("fitted", {}))]
        print(f"collision, {label}: {len(solid)} of {len(cells)} cells have boxes")
    # no sliver boxes: a part reaching a hair over a cell's edge gives that cell a box too thin to stand for anything
    thin = [(c["pos"], key) for c in cells for key, bs in [("frame", c.get("boxes", []))] + list(c.get("fitted", {}).items())
            for b in bs if min(b[k + 3] - b[k] for k in range(3)) < 0.04]
    print(f"collision: {len(thin)} boxes thinner than 0.04 of a block")
    if thin:
        ok = False
        print(f"FAIL sliver collision boxes (a part just over a cell's edge): {thin[:4]}")
    return ok
