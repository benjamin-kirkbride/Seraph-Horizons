"""The sluice generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift (which is no
move here); `check_shipped` in make_shape.py proves the shipped files are this model. A pose is
(concentrate shown 0 or 1, pipe part): nothing moves, so a pose only says what is shown.
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
    (r"^fr_hb_(floor_|wall_|sill)", {"planks"}),
    (r"^fr_sl_(floor|side_)", {"planks"}),
    (r"^fr_ld_(floor|side_|head)", {"planks"}),
    (r"^fr_cb_(floor|wall_)", {"planks"}),
    (r"^fr_hb_(leg_|bearer_)", {"oak"}),
    (r"^fr_sl_trestle\d_(legw|lege|beam)$", {"oak"}),
    (r"^fr_cb_sleeper\d$", {"oak"}),
    (r"^riffle_\d_bar$", {"oak"}),
    (r"^riffle_\d_strap$", {"iron"}),
    (r"^conc_(pocket\d[ab]|slot\d|launder\d|box)$", {"concentrate"}),
    (r"^(infeedchute|tailingschute|concentratechute)_", {"chute"}),
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
    """The frame is one piece from the ground; every fitted part and the concentrate is held by the frame
    (the concentrate by the frame or a riffle), through its own elements."""
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")
    frame = v.by_part["frame"]
    for pid, els in v.by_part.items():
        if pid == "frame":
            continue
        carriers = frame + (v.by_part["riffles"] if pid == "concentrate" else [])
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
    """The three chute sections are vanilla's straight chute exactly (its walls, wall thickness, place in the
    block and texture), centred in their faces and flush with them, so a chute placed beyond meets each with
    no gap or step: the infeed upright at the head cell's top face, the tailings chute north-south at the
    tail cell's south face, the concentrate chute east-west at the tail cell's west face. VANILLA_CHUTE is
    held to the game's own file wherever the game is installed. The four service faces all differ."""
    m = v.m
    rig = v.rig
    tex = {f["texture"] for pid in m.CHUTES for e in v.by_part[pid] for f in e.faces.values()}
    for pid, key, axis, cell, face, at_face in (("infeedchute", "infeed", "y", m.INFEED_CELL, "up", ("far", 16.0)),
                                                ("tailingschute", "tailings", "z", m.TAILINGS_CELL, "south", ("far", 16.0)),
                                                ("concentratechute", "concentrate", "x", m.CONC_CELL, "west", ("near", 0.0))):
        prof, near, far = m.chute_profile(v.els, pid, axis, cell)
        want = m.vanilla_profile(axis)
        ends = far if at_face[0] == "far" else near
        print(f"chutes: {pid} ({axis}): walls {prof}, the game's {want}; from {sorted(near)} to {sorted(far)} (cell-local; "
              f"the face at {at_face[1]}); {key}Cell {rig[key + 'Cell']} {rig[key + 'Face']}")
        if prof != want or ends != {at_face[1]} or len(near) != 1 or len(far) != 1 or rig[key + "Cell"] != list(cell) \
                or rig[key + "Face"] != face:
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
    faces = {tuple(rig[k + "Cell"]) + (rig[k + "Face"],) for k in m.FACE_ANCHORS}
    if len(faces) != len(m.FACE_ANCHORS):
        v.fail("the infeed, water, tailings and concentrate faces must all differ")


def check_water(v):
    """The water inlet meets a ppex pipe end to end on the water face's centre in ppex's 6 x 6 tube (a 4 x 4
    bore), runs south under the head box between its bearers, and turns up through the box's floor (the
    floor's underside on the pipe); its reducer stands on the floor and its nozzle points up, its mouth inside
    the box, in the pool under the sill."""
    m = v.m
    rig = v.rig
    wc = m.WATER_CELL
    wx, wy = m.WATER_IN
    rx0, rx1, rz0, rz1 = m.riser_span()
    bearers = v.named("frame", r"^fr_hb_bearer_")
    for pid in m.PIPES:
        ilo, ihi = aabb_of(v.named(pid, r"_inlet_"))
        section = (round(ihi[0] - ilo[0], 6), round(ihi[1] - ilo[1], 6))
        centre = ((ilo[0] + ihi[0]) / 2, (ilo[1] + ihi[1]) / 2)
        bore = v.named(pid, r"_inlet_(east|west)$")
        bore_w = round(min(e.aabb()[0][0] for e in bore if e.c[0] > wx) - max(e.aabb()[1][0] for e in bore if e.c[0] < wx), 6)
        plo, phi = aabb_of(v.named(pid, r"_(inlet|bend)_"))
        rlo, rhi = aabb_of(v.named(pid, r"_riser_"))
        nlo, nhi = aabb_of(v.named(pid, r"_(reducer|nozzle\d)$"))
        print(f"water: {pid}: inlet from z {ilo[2]:.2f}, a {section[0]:.0f} x {section[1]:.0f} tube (bore {bore_w}) on (x {centre[0]}, "
              f"y {centre[1]}), under the head box to y {phi[1]:.2f} (its floor's underside {m.HB_Y[0]}); the riser x {rlo[0]}..{rhi[0]}, "
              f"z {rlo[2]}..{rhi[2]}, to y {rhi[1]}; reducer and nozzle y {nlo[1]:.2f}..{nhi[1]:.2f} (the sill {m.SL_HEAD_Y})")
        centred = (wx - wc[0] * 16, wy - wc[1] * 16) == (8.0, 8.0) and wc[2] == 0 and rig["waterFace"] == "north" \
            and rig["waterCell"] == list(wc)
        if not centred or ilo[2] > 1e-6 or section != (6.0, 6.0) or bore_w != 4.0 or centre != (wx, wy):
            v.fail(f"{pid}'s inlet does not meet a ppex pipe end to end on the water face's centre")
        if abs(phi[1] - m.HB_Y[0]) > 1e-6 or any(b.aabb()[0][0] < phi[0] < b.aabb()[1][0] or b.aabb()[0][0] < plo[0] < b.aabb()[1][0]
                                                 for b in bearers):
            v.fail(f"{pid} does not run under the head box, between its bearers")
        if (round(rlo[0], 6), round(rhi[0], 6), round(rlo[2], 6), round(rhi[2], 6)) != (rx0, rx1, rz0, rz1) \
                or abs(rhi[1] - m.HB_FLOOR) > 1e-6:
            v.fail(f"{pid}'s riser does not come up through the head box's floor")
        inner = (m.HB_X[0] + m.HB_T, m.HB_X[1] - m.HB_T, m.HB_Z[0] + m.HB_T, m.HB_Z[1] - m.HB_T)
        if not (inner[0] <= nlo[0] and nhi[0] <= inner[1] and inner[2] <= nlo[2] and nhi[2] <= inner[3]) \
                or abs(nlo[1] - m.HB_FLOOR) > 1e-6 or not (m.HB_FLOOR < nhi[1] < m.SL_HEAD_Y):
            v.fail(f"{pid}'s reducer and nozzle are not on the head box's floor, in the pool under the sill")
    sp = rig["waterSpout"]["pos"]
    if [round(c * 16, 4) for c in sp] != [wx, round(m.nozzle_mouth(), 4), m.RISER_Z]:
        v.fail(f"waterSpout {sp} is not the nozzle's mouth")


FLOOR_HITS = r"^(fr_sl_floor\d|riffle_\d_(bar|strap)|conc_(pocket\d[ab]|slot\d|launder\d)|fr_ld_floor\d)$"


def check_flow(v):
    """The pulp's way through: ore dropped down the infeed chute's bore lands on the head box's floor (or the
    nozzle in it); the box's floor is under its sill and the sill is the sluice's head floor; the channel is
    as wide as the chute's bore or a hair wider, over it; the floor falls at exactly 1 in 12 from the sill to
    the tail; a grain dropped anywhere on the channel's centre line lands on the floor, a riffle or the
    concentrate, or through a draw slot into the launder; the tail's lip stands in the tailings chute's
    mouth, over its bore floor and under its top, the last riffle too, and a grain off the lip lands in the
    tailings chute, not the concentrate box."""
    m = v.m
    pose = (0, m.PIPES[0])
    cz = (m.HB_Z[0] + m.HB_Z[1]) / 2
    landings = set()
    for dx in (-2.5, 0.0, 2.5):
        for dz in (-2.5, 0.0, 2.5):
            hit = first_below(v, [m.CX + dx, m.CHUTE_IN_Y0 - 0.01, cz + dz], pose)
            landings.add(hit[1] if hit else None)
    print(f"flow: ore down the infeed chute lands on {sorted(str(x) for x in landings)}")
    if not landings or not all(x and re.match(r"^(fr_hb_floor_[wes]|pipecopper_(reducer|nozzle\d))$", x) for x in landings):
        v.fail("ore down the infeed chute does not land on the head box's floor or the nozzle")
    sill = v.named("frame", r"^fr_hb_sill$")[0].aabb()
    if not (m.HB_FLOOR < sill[1][1] and abs(sill[1][1] - m.SL_HEAD_Y) < 1e-6 and m.SL_IN[0] - 0.1 < sill[0][0] <= m.SL_IN[0]
            and m.SL_IN[1] <= sill[1][0] < m.SL_IN[1] + 0.1):
        v.fail("the head box's sill is not the sluice's head floor across the channel")
    fl = v.named("frame", r"^fr_sl_floor\d$")
    grades = {round(-e.r[1][2] / e.r[1][1], 9) for e in fl}
    head = max((c for e in fl for c in e.corners()), key=lambda c: c[1])
    head_top = math.hypot(head[1] - m.SL_HEAD_Y, head[2] - m.SL_Z[0])
    width = m.SL_IN[1] - m.SL_IN[0]
    print(f"flow: the box's floor {m.HB_FLOOR} under the sill {sill[1][1]:.3f} (the sluice's head floor {m.SL_HEAD_Y:.3f}); "
          f"the channel {width:.2f} wide over the chute's bore {m.CHUTE_BORE}; the floor ({len(fl)} lengths between the slots) "
          f"falls {sorted(grades)} (1 in {1 / m.SL_SLOPE:.0f}), its head {head_top:.1e} off the sill's edge")
    if grades != {round(m.SL_SLOPE, 9)} or abs(head_top) > 1e-9 or not (m.SL_IN[0] <= m.CHUTE_BORE[0] and m.CHUTE_BORE[1] <= m.SL_IN[1]) \
            or width > m.CHUTE_BORE[1] - m.CHUTE_BORE[0] + 0.25:
        v.fail("the sluice's floor or channel is off")
    bad = []
    for shown_ in (0, 1):
        pose = (shown_, m.PIPES[0])
        z = m.SL_Z[0] + 0.25
        while z < m.SL_Z[1] - 0.25:
            hit = first_below(v, [m.CX, 15.95, z], pose)
            if not hit or not re.match(FLOOR_HITS, hit[1]):
                bad.append((shown_, z, hit))
            z += 0.25
    print(f"flow: grains down the channel's centre line, every quarter voxel, land on the floor, a riffle, the concentrate "
          f"or (through a slot) the launder: {len(bad)} do not")
    if bad:
        v.fail(f"the channel's floor has gaps or something over it: {bad[:4]}")
    # the tail: the lip in the tailings chute's mouth, the pulp off it falling into the chute's bore
    lip = m.sl_floor(m.SL_Z[1])
    bottom = [e for e in v.by_part["tailingschute"] if e.name.endswith("_bottom")][0].aabb()
    top = [e for e in v.by_part["tailingschute"] if e.name.endswith("_top")][0].aabb()
    riffle_top = max(c[1] for e in v.named("riffles", r"^riffle_\d+_strap$") if e.c[2] > m.SL_Z[1] - 4 for c in e.corners())
    side_end = max(c[2] for e in v.named("frame", r"^fr_sl_(floor|side_[we])\d$") for c in e.corners())
    tail_pieces = [e for e in v.named("frame", r"^fr_sl_(floor|side_[we])\d$") if max(c[2] for c in e.corners()) > m.SL_Z[1] - 0.5]
    side_bottom_end = min(max(c[2] for c in e.corners() if slope_h(m, c)[0] < -m.SL_FLOOR_T + 1e-6) for e in tail_pieces)
    off = first_below(v, [m.CX, lip + 0.2, m.SL_Z[1] + 0.3], (1, m.PIPES[0]))
    print(f"flow: the lip {lip:.2f} in the tailings chute's mouth (bore floor {bottom[1][1]:.2f}, top {top[0][1]:.2f}), the last "
          f"riffle's top {riffle_top:.2f}; the sluice's pieces reach z {side_bottom_end:.3f} at their bottoms, {side_end:.3f} at "
          f"most (the chute's mouth {m.CHUTE_OUT_Z0}); a grain off the lip lands on {off[1] if off else None}")
    if not (bottom[1][1] < lip < top[0][1] - 0.3) or riffle_top > top[1][1] + 1.5 or abs(side_bottom_end - m.CHUTE_OUT_Z0) > 1e-6 \
            or side_end > m.CHUTE_OUT_Z0 + 0.5 or abs(bottom[0][2] - m.CHUTE_OUT_Z0) > 1e-6 or not off \
            or off[1] != "tailingschute_bottom":
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
        hs = [slope_h(m, c) for c in bar.corners()]
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


def check_concentrate(v):
    """The concentrate leaves by chute, continuously. Behind each of riffles 1..6 a draw slot runs through
    the floor, from the riffle's downstream face, across the channel, under its strap's lip; a grain dropped
    through each slot lands on the launder's floor. The launder, closed at its head and slung under the floor
    at its grade, runs from upstream of the first slot to its open end over the concentrate box; a grain off
    its lip lands on the box's floor. The box's floor is the concentrate chute's bore floor, its inside the
    bore's width, and it lies open to the chute. Drawn while it runs, the concentrate is a bed behind each
    riffle over its slot (on the floor and just into the sides, under the straps), a sheet through each slot,
    a layer down the launder and in the box."""
    m = v.m
    bad = []
    pose = (0, m.PIPES[0])
    for i, ((a, b), (zu, zd)) in enumerate(zip(m.slot_spans(), m.riffle_zs()), 1):
        if abs(a - m.s_at(zd)) > 1e-6 or b - a < 0.5 or b - a > m.RIFFLE_PITCH - m.RIFFLE_T - 1.0:
            bad.append(f"slot {i} is not right behind riffle {i}")
        hit = first_below(v, m.slope_point((a + b) / 2, 0.5), pose)
        if not hit or not re.match(r"^fr_ld_floor\d$", hit[1]):
            bad.append(f"a grain through slot {i} lands on {hit}")
    ld = v.named("frame", r"^fr_ld_floor\d$")
    grades = {round(-e.r[1][2] / e.r[1][1], 9) for e in ld}
    lip = m.launder_lip()
    off = first_below(v, [lip[0], lip[1] + 0.1, lip[2] + 0.3], pose)
    first_slot = m.slope_point(m.slot_spans()[0][0], 0.0)[2]
    head = v.named("frame", r"^fr_ld_head$")[0].aabb()
    floor = v.named("frame", r"^fr_cb_floor$")[0].aabb()
    chute = {e.name.rsplit("_", 1)[1]: e.aabb() for e in v.by_part["concentratechute"]}
    print(f"concentrate: {len(m.slot_spans())} draw slots, each into the launder; the launder ({len(ld)} lengths) falls "
          f"{sorted(grades)}, closed at z {head[0][2]:.2f} (the first slot from {first_slot:.2f}), its lip {lip[1]:.2f} at z "
          f"{lip[2]:.2f}, a grain off it landing on {off[1] if off else None}; the box's floor at {floor[1][1]:.2f}, z "
          f"{floor[0][2]:.2f}..{floor[1][2]:.2f}, the concentrate chute's bore floor {chute['bottom'][1][1]:.2f}, z "
          f"{chute['n'][1][2]:.2f}..{chute['s'][0][2]:.2f}")
    if grades != {round(m.SL_SLOPE, 9)} or not head[1][2] <= first_slot or not off or off[1] != "fr_cb_floor":
        bad.append("the launder does not carry the slots' catch to the concentrate box")
    if abs(floor[1][1] - chute["bottom"][1][1]) > 1e-6 or abs(floor[0][2] - chute["n"][1][2]) > 1e-6 \
            or abs(floor[1][2] - chute["s"][0][2]) > 1e-6 or abs(floor[0][0] - chute["bottom"][1][0]) > 1e-6:
        bad.append("the concentrate box's floor is not the concentrate chute's bore floor, its width")
    # the box lies open to the chute: nothing of the frame across the chute's mouth in its bore under the launder
    mouth = [m.CHUTE_CONC_X1 + 0.5, (m.CB_Y0 + m.CB_T + m.CB_TOP) / 2, (m.CB_Z[0] + m.CB_Z[1]) / 2]
    blocked = [e.name for e in v.by_part["frame"] if inside(e, mouth, eps=0.0)]
    if blocked:
        bad.append(f"the concentrate box is closed to its chute by {blocked}")
    # the concentrate, while it runs
    pk = m.pockets()
    for e in v.named("concentrate", r"^conc_pocket"):
        i = int(re.match(r"conc_pocket(\d)", e.name).group(1)) - 1
        a, b = pk[i]
        hs = [slope_h(m, c) for c in e.corners()]
        lo_h, hi_h = min(h for h, _ in hs), max(h for h, _ in hs)
        s0, s1 = min(s for _, s in hs), max(s for _, s in hs)
        lo, hi = e.aabb()
        if not (-0.1 < lo_h < 0.0 and hi_h < m.RIFFLE_H - 0.05):
            bad.append(f"{e.name} is not on the floor under the straps (h {lo_h:.3f}..{hi_h:.3f})")
        if not (a - 0.1 < s0 < a and s1 <= b):
            bad.append(f"{e.name} is not behind riffle {i + 1} in its pocket")
        if not (m.SL_IN[0] - 0.1 < lo[0] < m.SL_IN[0] and m.SL_IN[1] < hi[0] < m.SL_IN[1] + 0.1):
            bad.append(f"{e.name} is not just into the sides")
        sa, sb = m.slot_spans()[i]
        if not (s0 < sa and sb < s1):
            bad.append(f"{e.name} does not lie over slot {i + 1}")
    sheets = v.named("concentrate", r"^conc_slot\d$")
    layers = v.named("concentrate", r"^conc_(launder\d|box)$")
    if len(v.named("concentrate", r"^conc_pocket")) != 2 * len(pk) or len(sheets) != len(pk) or len(layers) != 3:
        bad.append("the concentrate is not a bed in each pocket, a sheet through each slot and the layers in the launder and box")
    print(f"concentrate: beds in {len(pk)} pockets, {len(sheets)} sheets through the slots, {len(layers)} layers in the launder and box")
    if bad:
        v.fail(f"the concentrate's way out is off: {bad[:4]}")


def check_supports(v):
    """Each beam, bearer and sleeper meets what it carries (the head box's underside, the launder's, the
    concentrate box's) and each leg stands on the ground beside the box it carries, touching it; the sluice
    sides' joints lie over the trestles."""
    m = v.m
    frame = {e.name: e for e in v.by_part["frame"]}
    bad = []
    carried = 0
    for name in sorted(n for n in frame if re.search(r"_(beam|bearer_[we]|sleeper\d)$", n)):
        top = frame[name].aabb()[1][1]
        zc = frame[name].c[2]
        if name.startswith("fr_hb_"):
            want = m.HB_Y[0]
        elif name.startswith("fr_cb_"):
            want = m.CB_Y0
        else:
            want = min(m.sl_floor(zc - m.BEAM_D / 2), m.sl_floor(zc + m.BEAM_D / 2)) + m.LD_H[0] / math.cos(m.THETA)
        gap = want - top
        carried += 1
        if not (0.0 <= gap < 0.02):
            bad.append(f"{name} is {gap:.3f} under what it carries")
    for name in sorted(n for n in frame if re.search(r"_leg(w|e|_[ns][we])$", n)):
        lo, hi = frame[name].aabb()
        box_ = m.HB_X if name.startswith("fr_hb_") else (m.SL_IN[0] - m.SL_SIDE_T, m.SL_IN[1] + m.SL_SIDE_T)
        west = name.endswith(("legw", "w"))
        if lo[1] != 0.0 or abs((hi[0] if west else lo[0]) - (box_[0] if west else box_[1])) > 1e-6:
            bad.append(f"{name} is not on the ground beside the box it carries")
    joints = []
    for z in m.SL_SPLITS:
        under = [n for n in frame if n.startswith("fr_sl_trestle") and n.endswith("_beam") and abs(frame[n].c[2] - z) < m.BEAM_D / 2 - 0.1]
        joints.append((z, under))
        if not under:
            bad.append(f"no trestle's beam under the sluice sides' joint at z {z}")
    print(f"supports: {carried} beams, bearers and sleepers under what they carry, legs on the ground beside it; the sides' joints "
          f"{joints}")
    if bad:
        v.fail(f"supports are off: {bad[:4]}")


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels with the concentrate shown or not, either metal.
ALLOWED = [
    ("riffles", None, "frame", r"^fr_sl_(floor|side_)"),                          # on the floor, from side to side
    ("concentrate", None, "frame", r"^fr_(sl_(floor|side_)|ld_|cb_)"),           # on the floor, in the launder and the box
    ("concentrate", None, "riffles", r"_bar$"),                                   # packed against the riffles
    ("infeedchute", None, "frame", r"^fr_hb_wall_"),                              # held in the head box's mouth
    ("tailingschute", None, "frame", r"^fr_sl_(floor|side_[we])\d$"),             # the sluice's tail into its mouth
    (r"pipe(copper|lead)", r"_(reducer|riser_\w+)$", "frame", r"^fr_hb_floor_"),  # up through the head box's floor, onto it
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
    print(f"clearances over {len(v.m.POSES)} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
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
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses")
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
    for key in ("powerCell", "powerFace", "work", "trunkPath", "infeedSide", "outputSide", "fill"):
        if key in ship:
            print(f"FAIL the sluice's rig has a {key}")
            ok = False
    for key in m.POINTS:
        p = ship[key]["pos"]
        if not all(0 <= p[k] <= (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)[k] for k in range(3)):
            print(f"FAIL {key} {p} is outside the footprint")
            ok = False
    if any(re.search(r"clean", k, re.I) for k in ship):
        print("FAIL the rig names a hand clean-up")
        ok = False
    return ok
