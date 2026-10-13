"""The sluice generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift (which is no
move here); `check_shipped` in make_shape.py proves the shipped files are this model. A pose is
(concentrate state 0..3, pipe part): nothing moves, so a pose only says what is shown.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, frame_floating, obb_obb
from machinegen.geometry import aabb_of
from machinegen.rigmath import part_of, posed, progress_of


class V:
    def __init__(self, m, els, parts, rig):
        self.m, self.els, self.parts, self.rig = m, els, parts, rig
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        self.ok = True

    def fail(self, msg):
        self.ok = False
        print("FAIL", msg)

    def mat(self, pid, pose):
        return self.m.pm(self.parts, pid, pose)

    def posed(self, pid, pose):
        mm = self.mat(pid, pose)
        return [posed(el, mm) for el in self.by_part.get(pid, [])]

    def named(self, pid, rx):
        r = re.compile(rx)
        return [e for e in self.by_part.get(pid, []) if r.search(e.name)]

    def shown(self, pose):
        return [pid for pid in self.by_part if self.m.on_show(pid, pose)]


def local(el, p):
    """A world point in an element's own frame, from its centre."""
    d = [p[k] - el.c[k] for k in range(3)]
    return [sum(el.r[k][i] * d[k] for k in range(3)) for i in range(3)]


def inside(el, p, eps=1e-6):
    q = local(el, p)
    return all(abs(q[i]) <= abs(el.size[i]) / 2 + eps for i in range(3))


def ray_down(el, p):
    """The distance down (along -y) from point p to where it enters element el, or None."""
    o = local(el, p)
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
    """What a grain dropped at point p lands on first in a pose: (distance, element name), or None."""
    best = None
    for pid in v.shown(pose):
        for el in v.posed(pid, pose):
            lo, hi = el.aabb()
            if not (lo[0] <= p[0] <= hi[0] and lo[2] <= p[2] <= hi[2] and lo[1] <= p[1] + 1e-9):
                continue
            t = ray_down(el, p)
            if t is not None and (best is None or t < best[0]):
                best = (t, el.name)
    return best


def slope_h(m, p):
    """A point's height over the sluice floor's top line, along the slope's normal, and its s along it."""
    dz, dy = p[2] - m.SL_Z[0], p[1] - m.SL_HEAD_Y
    c, s = math.cos(m.THETA), math.sin(m.THETA)
    return dz * s + dy * c, dz * c - dy * s


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


def check_static(v):
    """Nothing moves: no part has a driver or rides another, the rig has no work and no power cell, and every
    part's matrix is the identity at any input."""
    m = v.m
    moving = [p["id"] for p in v.parts if p["drivers"] or p.get("ride")]
    worst = 0.0
    for p in v.parts:
        for inputs in ({"theta": 0.0}, {"theta": 2.7, "depth": 0.4, "work": 3.0, "size": 1, "presence": 1.0}, {"theta": -9.1}):
            mm = m._part_matrix(v.parts, p["id"], inputs, None)
            worst = max(worst, max(abs(mm[i][j] - (1.0 if i == j else 0.0)) for i in range(3) for j in range(4)))
    print(f"static: {len(moving)} parts with a driver or a ride; every part's matrix the identity within {worst:.1e}")
    if moving or worst > 1e-12:
        v.fail(f"something moves: {moving}")
    if progress_of(v.rig) is not None or "powerCell" in v.rig or "powerFace" in v.rig:
        v.fail("an unpowered sluice has no power cell and no work")


ROLES = [
    # (element regex, the textures it may wear)
    (r"^fr_hb_(floor|wall_|sill)", {"planks"}),
    (r"^fr_sl_(floor|side_)", {"planks"}),
    (r"^fr_(hb|sl)_trestle\d_(legw|lege|beam)$", {"oak"}),
    (r"^riffle_\d_bar$", {"oak"}),
    (r"^riffle_\d_strap$", {"iron"}),
    (r"^conc\d_pocket\d[ab]?$", {"concentrate"}),
    (r"^infeedchute_", {"chute"}),
    (r"^tailingschute_", {"chute"}),
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
    # a face's UVs cover at most one texture (16 voxels): a longer element would stretch it
    long_ = [(el.name, round(max(abs(x) for x in el.size), 3)) for el in v.els if max(abs(x) for x in el.size) > 16.0 + 1e-6]
    print(f"textures: the longest element {max(max(abs(x) for x in el.size) for el in v.els):.2f} voxels (no texture stretched past 16)")
    if long_:
        v.fail(f"elements longer than a texture: {long_[:4]}")


def check_containment(v):
    m = v.m
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where = -1e9, None
    for el in v.els:
        lo, hi = el.aabb()
        o = box_overhang(lo, hi, box)
        if o > worst:
            worst, where = o, el.name
    print(f"containment: worst overhang {worst:.3f} voxels ({where}) out of the {box} block box")
    if worst > 0.001:
        v.fail(f"{where} leaves the machine's cells")


def check_floating(v):
    """The frame is one piece from the ground; every fitted part and every concentrate state touches the
    frame (or, the concentrate, the riffles)."""
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")
    frame = v.by_part["frame"]
    for pid, els in v.by_part.items():
        if pid == "frame":
            continue
        carriers = frame + (v.by_part["riffles"] if pid.startswith("concentrate") else [])
        held = {i for i, e in enumerate(els) if any(obb_obb(e, f, eps=-0.03) for f in carriers)}
        todo = list(held)
        while todo:                              # and the part's elements held through each other
            i = todo.pop()
            for j, e in enumerate(els):
                if j not in held and obb_obb(els[i], e, eps=-0.03):
                    held.add(j)
                    todo.append(j)
        loose = [e.name for i, e in enumerate(els) if i not in held]
        if loose:
            v.fail(f"{pid}: elements held by nothing that carries them: {loose[:6]}")
    print("fitted parts: each held by the frame (the concentrate by the frame or a riffle), through its own elements")


def check_chutes(v):
    """Both chute sections are vanilla's straight chute exactly (its walls, wall thickness, place in the
    block and texture), centred in their faces and flush with them, so a chute placed beyond meets each with
    no gap or step: the infeed upright at the head cell's top face, the tailings chute north-south at the
    tail cell's south face. VANILLA_CHUTE is held to the game's own file wherever the game is installed."""
    m = v.m
    rig = v.rig
    tex = {f["texture"] for pid in ("infeedchute", "tailingschute") for e in v.by_part[pid] for f in e.faces.values()}
    for pid, axis, cell, face, want_far in (("infeedchute", "y", m.INFEED_CELL, "up", (m.INFEED_CELL[1] + 1) * 16.0),
                                            ("tailingschute", "z", m.TAILINGS_CELL, "south", (m.TAILINGS_CELL[2] + 1) * 16.0)):
        prof, near, far = m.chute_profile(v.els, pid, axis)
        want = m.vanilla_profile(axis)
        key = "infeed" if pid == "infeedchute" else "tailings"
        print(f"chutes: {pid} ({axis}): walls {prof}, the game's {want}; from {sorted(near)} to {sorted(far)} "
              f"(the face at {want_far}); {key}Cell {rig[key + 'Cell']} {rig[key + 'Face']}")
        if prof != want or far != {want_far} or len(near) != 1 or rig[key + "Cell"] != list(cell) or rig[key + "Face"] != face:
            v.fail(f"the {pid} is not vanilla's chute section flush with its face")
    if tex != {"#chute"} or m.TEXTURES["chute"] != m.VANILLA_CHUTE["texture"]:
        v.fail(f"the chute sections do not wear vanilla's chute texture: {tex}")
    game = m.read_vanilla_chute()
    if game is None:
        print("chutes: the game's chute shape is not here; VANILLA_CHUTE is held to it where it is")
    else:
        walls, runs, gtex = game
        ok = walls == sorted(m.VANILLA_CHUTE["walls"]) and runs == {(0.0, 16.0)} and gtex == m.VANILLA_CHUTE["texture"]
        print(f"chutes: the game's {m.VANILLA_CHUTE['shape']}: walls {walls} the length of the block, texture {gtex}")
        if not ok:
            v.fail("VANILLA_CHUTE is not the game's straight chute")
    if len({tuple(m.INFEED_CELL) + (m.INFEED_FACE,), tuple(m.WATER_CELL) + (m.WATER_FACE,),
            tuple(m.TAILINGS_CELL) + (m.TAILINGS_FACE,)}) != 3:
        v.fail("the infeed, water and tailings faces must differ")


def check_water(v):
    """The water inlet meets a ppex pipe end to end on the water face's centre in ppex's 6 x 6 tube (a 4 x 4
    bore), runs straight through the head box's north wall, and ends inside the box in its reducer and
    nozzle, standing on the box's floor, pointing down the sluice (south), its mouth inside the box above
    its floor, short of the falling ore's path (the infeed chute's bore)."""
    m = v.m
    rig = v.rig
    wc = m.WATER_CELL
    wx, wy = m.WATER_IN
    hb_in = (m.HB_X[0] + m.HB_T, m.HB_X[1] - m.HB_T, m.HB_Z[0] + m.HB_T, m.HB_Z[1] - m.HB_T)
    for pid in m.PIPES:
        ilo, ihi = aabb_of(v.named(pid, r"_inlet_"))
        section = (round(ihi[0] - ilo[0], 6), round(ihi[1] - ilo[1], 6))
        centre = ((ilo[0] + ihi[0]) / 2, (ilo[1] + ihi[1]) / 2)
        bore = [e for e in v.named(pid, r"_inlet_(east|west)$")]
        bore_w = round(min(e.aabb()[0][0] for e in bore if e.c[0] > wx) - max(e.aabb()[1][0] for e in bore if e.c[0] < wx), 6)
        nlo, nhi = aabb_of(v.named(pid, r"_(reducer|nozzle\d)$"))
        mouth = max(e.aabb()[1][2] for e in v.named(pid, r"_nozzle\d$"))
        print(f"water: {pid}: inlet from z {ilo[2]:.2f} to {ihi[2]:.2f}, a {section[0]:.0f} x {section[1]:.0f} tube (bore {bore_w}) on "
              f"(x {centre[0]}, y {centre[1]}); reducer and nozzle z {nlo[2]:.2f}..{mouth:.2f}, y {nlo[1]:.2f}..{nhi[1]:.2f}")
        centred = (wx - wc[0] * 16, wy - wc[1] * 16) == (8.0, 8.0) and wc[2] == 0 and rig["waterFace"] == "north" \
            and rig["waterCell"] == list(wc)
        if not centred or ilo[2] > 1e-6 or section != (6.0, 6.0) or bore_w != 4.0 or centre != (wx, wy):
            v.fail(f"{pid}'s inlet does not meet a ppex pipe end to end on the water face's centre")
        if abs(ihi[2] - hb_in[2]) > 1e-6:
            v.fail(f"{pid}'s inlet does not run through the head box's north wall to its inside face")
        if not (hb_in[0] < nlo[0] and nhi[0] < hb_in[1] and hb_in[2] <= nlo[2] and mouth < hb_in[3]) or abs(nlo[1] - m.HB_FLOOR) > 1e-6:
            v.fail(f"{pid}'s reducer and nozzle are not inside the head box, on its floor")
        if mouth > m.CHUTE_BORE[0] + 1e-6:
            v.fail(f"{pid}'s nozzle reaches into the falling ore's path (the chute's bore from z {m.CHUTE_BORE[0]})")
    sp = rig["waterSpout"]["pos"]
    if [round(c * 16, 4) for c in sp] != [wx, wy, m.NOZZLE[-1][3]]:
        v.fail(f"waterSpout {sp} is not the nozzle's mouth")


def check_flow(v):
    """The pulp's way through: ore dropped down the infeed chute's bore lands on the head box's floor; the
    box's floor is under its sill and the sill is the sluice's head floor; the channel is as wide as the
    chute's bore or a hair wider, over it; the floor falls at exactly 1 in 12 from the sill to the tail;
    a grain dropped anywhere on the channel's centre line lands on the floor, a riffle or the concentrate;
    the tail's lip stands over the tailings chute's bore floor, its pulp falling into the bore, under the
    bore's top, the last riffle too."""
    m = v.m
    pose = (0, m.PIPES[0])
    cz = (m.HB_Z[0] + m.HB_Z[1]) / 2
    landings = set()
    for dx in (-2.5, 0.0, 2.5):
        for dz in (-2.5, 0.0, 2.5):
            hit = first_below(v, [m.CX + dx, m.CHUTE_IN_Y0 - 0.01, cz + dz], pose)
            landings.add(hit[1] if hit else None)
    print(f"flow: ore down the infeed chute lands on {sorted(str(x) for x in landings)}")
    if landings != {"fr_hb_floor"}:
        v.fail("ore down the infeed chute does not land on the head box's floor")
    sill = v.named("frame", r"^fr_hb_sill$")[0].aabb()
    if not (m.HB_FLOOR < sill[1][1] and abs(sill[1][1] - m.SL_HEAD_Y) < 1e-6 and m.SL_IN[0] - 0.1 < sill[0][0] <= m.SL_IN[0]
            and m.SL_IN[1] <= sill[1][0] < m.SL_IN[1] + 0.1):
        v.fail("the head box's sill is not the sluice's head floor across the channel")
    fl = v.named("frame", r"^fr_sl_floor\d$")
    tops = [(e, [e.c[k] + e.r[k][1] * e.size[1] / 2 for k in range(3)]) for e in fl]
    grades = {round(-e.r[1][2] / e.r[1][1], 9) for e in fl}
    head = max((c for e in fl for c in e.corners()), key=lambda c: c[1])
    head_top = math.hypot(head[1] - m.SL_HEAD_Y, head[2] - m.SL_Z[0])
    width = m.SL_IN[1] - m.SL_IN[0]
    print(f"flow: the box's floor {m.HB_FLOOR} under the sill {sill[1][1]:.3f} (the sluice's head floor {m.SL_HEAD_Y:.3f}); "
          f"the channel {width:.2f} wide over the chute's bore {m.CHUTE_BORE}; the floor's grade {sorted(grades)} (1 in "
          f"{1 / m.SL_SLOPE:.0f}), its head {head_top:.1e} off the sill's edge")
    if grades != {round(m.SL_SLOPE, 9)} or abs(head_top) > 1e-9 or not (m.SL_IN[0] <= m.CHUTE_BORE[0] and m.CHUTE_BORE[1] <= m.SL_IN[1]) \
            or width > m.CHUTE_BORE[1] - m.CHUTE_BORE[0] + 0.25:
        v.fail("the sluice's floor or channel is off")
    # a grain dropped down the channel's centre line, every half voxel, lands on the floor, a riffle or the concentrate
    bad = []
    for state in (0, *m.STATES):
        pose = (state, m.PIPES[0])
        z = m.SL_Z[0] + 0.25
        while z < m.SL_Z[1] - 0.25:
            hit = first_below(v, [m.CX, 15.9, z], pose)
            if not hit or not re.match(r"^(fr_sl_floor\d|riffle_\d_(bar|strap)|conc\d_pocket\d[ab]?)$", hit[1]):
                bad.append((state, z, hit))
            z += 0.5
    print(f"flow: grains down the channel's centre line land on the floor, a riffle or the concentrate: {len(bad)} do not")
    if bad:
        v.fail(f"the channel's floor has gaps or something over it: {bad[:4]}")
    # the tail: the lip over the chute's bore floor and under its top, the pulp falling into the bore
    lip = m.sl_floor(m.SL_Z[1])
    bottom = [e for e in v.by_part["tailingschute"] if e.name.endswith("_bottom")][0].aabb()
    top = [e for e in v.by_part["tailingschute"] if e.name.endswith("_top")][0].aabb()
    riffle_top = max(c[1] for e in v.named("riffles", r"^riffle_\d+_strap$") if e.c[2] > m.SL_Z[1] - 4 for c in e.corners())
    side_end = max(c[2] for e in v.named("frame", r"^fr_sl_(floor|side_[we])2$") for c in e.corners())
    side_bottom_end = min(max(c[2] for c in e.corners() if slope_h(m, c)[0] < -m.SL_FLOOR_T + 1e-6)
                          for e in v.named("frame", r"^fr_sl_(floor|side_[we])2$"))
    print(f"flow: the lip {lip:.2f} over the tailings chute's bore floor {bottom[1][1]:.2f}, the last riffle's top {riffle_top:.2f} "
          f"under its bore's top {top[0][1]:.2f}; the sluice's pieces reach z {side_bottom_end:.3f} at their bottoms, "
          f"{side_end:.3f} at most (the chute's mouth {m.CHUTE_OUT_Z0})")
    if not (bottom[1][1] < lip < bottom[1][1] + 1.0 and riffle_top < top[0][1] - 2.0) or abs(side_bottom_end - m.CHUTE_OUT_Z0) > 1e-6 \
            or side_end > m.CHUTE_OUT_Z0 + 0.5 or abs(bottom[0][2] - m.CHUTE_OUT_Z0) > 1e-6:
        v.fail("the sluice's tail does not pour into the tailings chute's bore")


def check_riffles(v):
    """Seven riffles across the channel, evenly spaced, each an oak bar standing on the floor square to
    the slope, from side to side, with an iron strap on its top overhanging its downstream face; plain floor
    at the head for the pulp to spread on, and the last riffle short of the lip."""
    m = v.m
    rz = m.riffle_zs()
    bars = sorted(v.named("riffles", r"_bar$"), key=lambda e: e.c[2])
    straps = sorted(v.named("riffles", r"_strap$"), key=lambda e: e.c[2])
    bad = []
    for i, (bar, strap, (zu, zd)) in enumerate(zip(bars, straps, rz), 1):
        cs = bar.corners()
        hs = [slope_h(m, c) for c in cs]
        lo_h, hi_h = min(h for h, _ in hs), max(h for h, _ in hs)
        s0, s1 = min(s for _, s in hs), max(s for _, s in hs)
        blo, bhi = bar.aabb()
        shs = [slope_h(m, c) for c in strap.corners()]
        if abs(lo_h) > 1e-6 or abs(hi_h - m.RIFFLE_H) > 1e-6 or abs(s0 - m.s_at(zu)) > 1e-6 or abs(s1 - m.s_at(zd)) > 1e-6:
            bad.append(f"riffle {i}'s bar is not on the floor at z {zu}..{zd}")
        if abs(blo[0] - m.SL_IN[0]) > 1e-6 or abs(bhi[0] - m.SL_IN[1]) > 1e-6:
            bad.append(f"riffle {i} does not run side to side")
        if abs(min(h for h, _ in shs) - m.RIFFLE_H) > 1e-6 or abs(max(s for _, s in shs) - m.s_at(zd) - m.STRAP_LIP) > 1e-6 \
                or abs(min(s for _, s in shs) - m.s_at(zu)) > 1e-6:
            bad.append(f"riffle {i}'s strap is not on its top, over its downstream face")
    pitches = {round(b - a, 9) for (a, _), (b, _) in zip(rz, rz[1:])}
    head, lip = rz[0][0] - m.SL_Z[0], m.SL_Z[1] - rz[-1][1]
    print(f"riffles: {len(bars)} bars and {len(straps)} straps, every {sorted(pitches)} voxels; {head} of plain floor at the head, "
          f"{lip} from the last to the lip")
    if len(bars) != m.RIFFLE_N or len(straps) != m.RIFFLE_N or len(pitches) != 1 or head < 3.0 or lip < 0.9 or bad:
        v.fail(f"the riffles are off: {bad[:4]}")


def pocket_volumes(v, m, state):
    """Each pocket's concentrate in a state: the volume of its deposit above the floor (an estimate on a
    grid across the pocket, voxels^3)."""
    els = v.by_part[f"concentrate{state}"]
    out = []
    for a, b in m.pockets():
        vol = 0.0
        n = 24
        for i in range(n):
            s = a + (b - a) * (i + 0.5) / n
            top = 0.0
            for e in els:
                for h in [k * 0.02 for k in range(60, -1, -1)]:
                    p = [m.CX, m.SL_HEAD_Y + h * math.cos(m.THETA) - s * math.sin(m.THETA), m.SL_Z[0] + s * math.cos(m.THETA) + h * math.sin(m.THETA)]
                    if inside(e, p):
                        top = max(top, h)
                        break
            vol += top * (b - a) / n
        out.append(vol * (m.SL_IN[1] - m.SL_IN[0]))
    return out


def check_concentrate(v):
    """The concentrate lies behind the riffles: each deposit in its pocket, against the riffle upstream of
    it (from its downstream face), on the floor and into the sides by a hair, under the riffles' straps;
    each state holds at least as much as the last in every pocket, the pockets fill from the head down,
    and full is every pocket brimming (to CONC_FULL from riffle to riffle)."""
    m = v.m
    pk = m.pockets()
    bad = []
    for k in m.STATES:
        for e in v.by_part[f"concentrate{k}"]:
            i = int(re.match(r"conc\d_pocket(\d)", e.name).group(1)) - 1
            a, b = pk[i]
            hs = [slope_h(m, c) for c in e.corners()]
            lo_h, hi_h = min(h for h, _ in hs), max(h for h, _ in hs)
            s0, s1 = min(s for _, s in hs), max(s for _, s in hs)
            lo, hi = e.aabb()
            if not (-0.1 < lo_h < 0.0 and hi_h <= m.CONC_FULL + 1e-6 and hi_h < m.RIFFLE_H - 0.05):
                bad.append(f"{e.name} is not on the floor under the straps (h {lo_h:.3f}..{hi_h:.3f})")
            if not (a - 0.1 < s0 < a and s1 <= b + m.CONC_IN + 1e-6):
                bad.append(f"{e.name} is not behind riffle {i + 1} in its pocket (s {s0:.2f}..{s1:.2f}, the pocket {a:.2f}..{b:.2f})")
            if not (m.SL_IN[0] - 0.1 < lo[0] < m.SL_IN[0] and m.SL_IN[1] < hi[0] < m.SL_IN[1] + 0.1):
                bad.append(f"{e.name} is not just into the sides")
    vols = {k: pocket_volumes(v, m, k) for k in m.STATES}
    full = [(b - a) * m.CONC_FULL * (m.SL_IN[1] - m.SL_IN[0]) for a, b in pk]
    for k in m.STATES:
        print(f"concentrate {k}: pockets " + ", ".join(f"{x / f:.0%}" for x, f in zip(vols[k], full))
              + f"; {sum(vols[k]) / sum(full):.0%} of full")
    for k0, k1 in zip(m.STATES, m.STATES[1:]):
        if any(x1 + 1e-6 < x0 for x0, x1 in zip(vols[k0], vols[k1])):
            bad.append(f"state {k1} holds less than state {k0} in a pocket")
    for k in m.STATES:
        if any(x1 > x0 + 1e-6 for x0, x1 in zip(vols[k], vols[k][1:])):
            bad.append(f"state {k}'s pockets do not fill from the head down")
    if any(abs(x - f) > 0.03 * f for x, f in zip(vols[m.STATES[-1]], full)):
        bad.append("the full state is not every pocket brimming")
    if bad:
        v.fail(f"the concentrate is off: {bad[:4]}")


def check_supports(v):
    """Each trestle's beam meets what it carries (the head box's underside, the sluice's) and its legs
    stand on the ground beside it, touching its sides; the sluice's head meets the head box's south wall."""
    m = v.m
    frame = {e.name: e for e in v.by_part["frame"]}
    bad = []
    for name in sorted(n for n in frame if n.endswith("_beam")):
        beam = frame[name]
        top = beam.aabb()[1][1]
        zc = beam.c[2]
        if name.startswith("fr_hb_"):
            gap = m.HB_Y[0] - top
        else:
            gap = (min(m.sl_floor(zc - m.BEAM_D / 2), m.sl_floor(zc + m.BEAM_D / 2)) - m.SL_FLOOR_T / math.cos(m.THETA)) - top
        if not (0.0 <= gap < 0.02):
            bad.append(f"{name} is {gap:.3f} under what it carries")
    for name in sorted(n for n in frame if n.endswith(("_legw", "_lege"))):
        leg = frame[name]
        lo, hi = leg.aabb()
        carried = m.HB_X if name.startswith("fr_hb_") else (m.SL_IN[0] - m.SL_SIDE_T, m.SL_IN[1] + m.SL_SIDE_T)
        side = hi[0] if name.endswith("legw") else lo[0]
        want = carried[0] if name.endswith("legw") else carried[1]
        if lo[1] != 0.0 or abs(side - want) > 1e-6:
            bad.append(f"{name} is not on the ground beside the box it carries")
    joint = m.SL_SPLIT_Z
    under = [n for n in frame if n.startswith("fr_sl_trestle") and n.endswith("_beam")
             and abs(frame[n].c[2] - joint) < m.BEAM_D / 2 - 0.1]
    if not under:
        bad.append(f"no trestle's beam under the sluice boards' joint at z {joint}")
    print(f"supports: {sum(1 for n in frame if n.endswith('_beam'))} trestles, beams under what they carry, legs on the ground beside it; "
          f"the boards' joint (z {joint}) over {under}")
    if bad:
        v.fail(f"supports are off: {bad[:4]}")


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels in every state.
ALLOWED = [
    ("riffles", None, "frame", r"^fr_sl_(floor|side_)"),                          # on the floor, from side to side
    (r"concentrate\d", None, "frame", r"^fr_sl_(floor|side_)"),                    # on the floor, into the sides
    (r"concentrate\d", None, "riffles", r"_bar$"),                                 # packed against the riffles
    ("infeedchute", None, "frame", r"^fr_hb_wall_"),                              # held in the head box's mouth
    ("tailingschute", None, "frame", r"^fr_sl_(floor|side_[we])2$"),              # the sluice's tail into its mouth
    (r"pipe(copper|lead)", r"_inlet_", "frame", r"^fr_hb_(wall_n|trestle1_beam)"),  # through the north wall, over the beam
    (r"pipe(copper|lead)", r"_(reducer|inlet_)", "frame", r"^fr_hb_floor$"),       # the reducer on the box's floor
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def check_clearances(v):
    allhits = {}
    for pose in v.m.POSES:
        items = [(pid, el, el.aabb()) for pid in v.shown(pose) for el in v.posed(pid, pose)]
        for i in range(len(items)):
            pa, ea, (alo, ahi) = items[i]
            for j in range(i + 1, len(items)):
                pb, eb, (blo, bhi) = items[j]
                if pa == pb:
                    continue
                if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                    continue
                if allowed(pa, ea.name, pb, eb.name) or not obb_obb(ea, eb):
                    continue
                allhits.setdefault(tuple(sorted((pa, pb))), {}).setdefault(tuple(sorted((ea.name, eb.name))), pose)
    print(f"clearances over {len(v.m.POSES)} states: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} in {p}" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = [e for pid in v.shown(pose) for e in v.posed(pid, pose)]
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar in {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} states")
    if bad:
        v.fail("faces z-fight")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_static(v)
    check_floating(v)
    check_textures(v)
    check_containment(v)
    check_chutes(v)
    check_water(v)
    check_flow(v)
    check_riffles(v)
    check_concentrate(v)
    check_supports(v)
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
    cells = ship["cells"]
    hollow = [c["pos"] for c in cells if c.get("hollow")]
    lids = [c["pos"] for c in cells if "lid" in c]
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(cells)} cells, hollow {hollow}, "
          f"lids {lids}")
    for c in cells:
        print(f"  cell {c['pos']}: boxes {c.get('boxes')}")
    if hollow or lids or [c["pos"] for c in cells] != [list(p) for p in m.footprint()]:
        print("FAIL the cells are not the three of the footprint, each with boxes and no lid")
        ok = False
    for key in ("powerCell", "powerFace", "work", "trunkPath", "infeedSide", "outputSide"):
        if key in ship:
            print(f"FAIL the sluice's rig has a {key}")
            ok = False
    for key in m.POINTS:
        p = ship[key]["pos"]
        if not all(0 <= p[k] <= (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)[k] for k in range(3)):
            print(f"FAIL {key} {p} is outside the footprint")
            ok = False
    if ship["fill"]["requires"] != [f"concentrate{k}" for k in m.STATES] or \
            set(ship["fill"]["requires"]) - {p["requires"] for p in ship["parts"]}:
        print("FAIL fill does not list the concentrate's states in order")
        ok = False
    return ok
