"""The arrastra generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its rule.
Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in make_shape.py
proves the shipped files are this model moved.

The model holds the whole machine built (the frame and every stage) and the pit's two contents,
which are never drawn together: every check that compares parts with each other does it with one content at a time
(`state`). A state with fewer stages draws a subset, so what holds for the whole holds for it.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, obb_obb, obb_overlap, supports
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply, part_of, posed

TOL = 0.02


class V:
    def __init__(self, m, els, parts, rig):
        self.m, self.els, self.parts, self.rig = m, els, parts, rig
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        self.spec = {p["id"]: p for p in parts}
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

    def named(self, pid, rx, pose=0.0):
        r = re.compile(rx)
        return [e for e in self.posed(pid, pose) if r.search(e.name)]

    def state(self, content):
        """The parts drawn with the machine built and `content` in the pit."""
        return [pid for pid in self.by_part if pid not in self.m.CONTENTS or pid == content]


def cycle(m, n):
    """n poses over the rig's whole cycle (CYCLE_TURNS turns of the axle), and two backwards."""
    span = m.CYCLE_TURNS * 2 * math.pi
    return [span * i / n for i in range(n)] + [-0.7, -2.9]


# ---------------------------------------------------------------- parts, sizes, containment
def check_basic(v):
    worst = euler_round_trip(v.els)
    print(f"euler round trip: worst {worst:.1e}")
    if worst > 1e-9:
        v.fail("an element's rotation does not survive being written as Euler angles")
    names = [el.name for el in v.els]
    if len(names) != len(set(names)):
        dup = sorted({n for n in names if names.count(n) > 1})
        v.fail(f"duplicate element names: {dup[:6]}")
    wrong = [(el.name, el.part, part_of(v.parts, el.name)) for el in v.els if part_of(v.parts, el.name) != el.part]
    if wrong:
        v.fail(f"{len(wrong)} elements land in the wrong part, e.g. {wrong[:4]}")
    empty = [p["id"] for p in v.parts if not v.by_part.get(p["id"])]
    if empty:
        v.fail(f"parts with no elements: {empty}")
    counts = {pid: len(els) for pid, els in v.by_part.items()}
    print(f"parts: {len(v.parts)}, elements {len(v.els)}: {counts}")


def check_chunky(v):
    """Readable at block scale: no element is a rod thinner than about a voxel (its two smaller sides both under
    0.9); a plate (one thin side) is a surface and may be thin."""
    thin = []
    for el in v.els:
        s = sorted(abs(x) for x in el.size)
        if s[1] < 0.9:
            thin.append((el.name, [round(x, 2) for x in s]))
    print(f"chunky: {len(v.els) - len(thin)} of {len(v.els)} elements at least 0.9 voxels across in two directions")
    if thin:
        v.fail(f"elements thinner than a voxel: {thin[:6]}")


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
        v.fail(f"{where[0]} leaves the machine box at theta {where[1]:.2f}")


def check_cells(v, poses):
    """The footprint is the box less EMPTY_CELLS: no element reaches into those, at any pose (exactly, each element's
    box against the cell's, not by bounding boxes)."""
    m = v.m
    found = None
    for cell in m.EMPTY_CELLS:
        lo = [cell[k] * m.B for k in range(3)]
        hi = [(cell[k] + 1) * m.B for k in range(3)]
        for pose in poses:
            for pid in v.by_part:
                for el in v.posed(pid, pose):
                    a, b = el.aabb()
                    if all(a[k] < hi[k] - 0.01 and lo[k] + 0.01 < b[k] for k in range(3)) and obb_overlap(el, lo, hi, eps=0.01):
                        found = found or (cell, el.name, round(pose, 3))
    print(f"footprint: {len(m.footprint())} cells; nothing reaches the box's other cells {list(m.EMPTY_CELLS)} over "
          f"{len(poses)} poses: {found is None}{'' if found is None else f' {found}'}")
    if found:
        v.fail(f"an element reaches a cell outside the footprint: {found}")


# ---------------------------------------------------------------- the build: stages, nothing floating
def joined(a, b, la, lb, aligned_a, aligned_b):
    """Whether two elements hold each other: machinegen's frame_floating rule (a shared face between two
    axis-aligned boxes, else an overlap)."""
    ov = [min(la[1][k], lb[1][k]) - max(la[0][k], lb[0][k]) for k in range(3)]
    if min(ov) < -0.02:
        return False
    if aligned_a and aligned_b:
        return sum(o > 0.05 for o in ov) >= 2
    return obb_obb(a, b, eps=-0.03)


def stage_floating(groups, ground=0.05):
    """Each stage's elements (`groups`: [(stage, elements)], in build order) joined to the ground through what is
    already there and itself: {stage: the names of its elements that float}. (The eidolon gantry's rule.) On the ground
    is within `ground` of it: the z-fighting fix may have moved a bottom face up by an inset or two."""
    placed, boxes, aligned, seen, out = [], [], [], [], {}
    for stage, els in groups:
        new = list(range(len(placed), len(placed) + len(els)))
        for el in els:
            placed.append(el)
            boxes.append(el.aabb())
            aligned.append(all(el.local_axis_for(k) is not None for k in range(3)))
            seen.append(False)
        todo = []
        for j in new:
            if boxes[j][0][1] <= ground or any(joined(placed[i], placed[j], boxes[i], boxes[j], aligned[i], aligned[j])
                                               for i in range(new[0]) if seen[i]):
                seen[j] = True
                todo.append(j)
        while todo:
            i = todo.pop()
            for j in new:
                if not seen[j] and joined(placed[i], placed[j], boxes[i], boxes[j], aligned[i], aligned[j]):
                    seen[j] = True
                    todo.append(j)
        out[stage] = sorted(placed[j].name for j in new if not seen[j])
    return out


def check_build(v):
    """The stages: the frame's parts need nothing, every other part needs its stage's requires (the contents their
    own), no requires value is in two stages, and the rig's `build` lists the stages in order. The build can be done in
    that order, nothing floating: at rest the frame, then each stage in turn, joined to the ground through what is
    already there, and each content once the machine is built."""
    m = v.m
    reqs = [req for req, _ in m.STAGES]
    if len(reqs) != len(set(reqs)):
        v.fail(f"a requires value is in two stages: {reqs}")
    for p in v.parts:
        want = m.requires_of(p["id"])
        if p["requires"] != want:
            v.fail(f"{p['id']} needs {p['requires']}, not {want}")
    staged = {pid for _, pids in m.STAGES for pid in pids}
    loose = sorted(p["id"] for p in v.parts if p["id"] not in staged | set(m.FRAME_PARTS) | set(m.CONTENTS))
    if loose:
        v.fail(f"parts in no stage: {loose}")
    if [s["requires"] for s in v.rig["build"]["stages"]] != reqs:
        v.fail(f"the rig's build stages are not {reqs}")
    base = [el for pid in m.FRAME_PARTS for el in v.posed(pid, 0.0)]
    groups = [("frame", base)] + [(req, [el for pid in pids for el in v.posed(pid, 0.0)]) for req, pids in m.STAGES]
    floating = {}
    for k in m.CONTENTS:
        floating.update({key: n for key, n in stage_floating(groups + [(k, v.posed(k, 0.0))]).items() if n})
    print("build: " + " > ".join(f"{req} ({len(els)})" for req, els in groups) + "; each content after the build: "
          f" {', '.join(f'{k} ({len(v.posed(k, 0.0))})' for k in m.CONTENTS)}; "
          f"floating: {floating or 'nothing'}")
    if floating:
        v.fail(f"the build floats: {floating}")


# ---------------------------------------------------------------- textures
ROLES = [
    (r"^hopper_", {"planks"}),
    (r"^fr_(post|stringer|brace|transom)", {"oak"}),
    (r"^fr_hanger", {"iron"}),
    (r"^pit_gate_jamb", {"oak"}),
    (r"^pit_gate_board", {"planks"}),
    (r"^pit_gate_grip", {"iron"}),
    (r"^pit_curb", {"drystone"}),
    (r"^pit_(pave|edge)", {"cobble"}),
    (r"^pit_(centre|gate_sill)", {"granite"}),
    (r"^sh_axle", {"axle"}),
    (r"^sh_(rod|collar)", {"steel"}),
    (r"^post_post", {"oak"}),
    (r"^post_(foot|top)gudgeon|^pinion_", {"steel"}),
    (r"^post_|^step_|^arms_staple|^stones_eye", {"iron"}),
    (r"^arms_arm", {"oak"}),
    (r"^stones_stone", {"basalt"}),
    (r"^stones_chain", {"chain"}),
    (r"^charge_", {"charge"}),
    (r"^paste_", {"paste"}),
]


def check_textures(v):
    bad = []
    for el in v.els:
        tex = {f["texture"].lstrip("#") for f in el.faces.values()}
        rule = next((want for rx, want in ROLES if re.search(rx, el.name)), None)
        if rule is None or not tex <= rule:
            bad.append((el.name, sorted(tex), sorted(rule or [])))
    print(f"textures by role: {len(v.els) - len(bad)} of {len(v.els)} elements as their role says (oak for the timber, "
          f"planks for the hopper and the gate, iron castings and fittings, steel shafts and pinion, granite, cobble and "
          f"drystone for the pit, basalt drag stones, chains, the contents' own)")
    if bad:
        v.fail(f"textures off their role: {bad[:6]}")


# ---------------------------------------------------------------- anchors
def cell_face_box(m, cell, face):
    """The face's square (voxels) as (lo, hi) with zero thickness, and its axis."""
    lo = [cell[k] * m.B for k in range(3)]
    hi = [(cell[k] + 1) * m.B for k in range(3)]
    k = {"west": 0, "east": 0, "down": 1, "up": 1, "north": 2, "south": 2}[face]
    if face in ("east", "up", "south"):
        lo[k] = hi[k]
    else:
        hi[k] = lo[k]
    return lo, hi, k


def check_anchors(v):
    """The anchor faces are on the footprint's outside, looking out of it, and all different; the power cell is no
    other anchor's; the power face is on a side; the axle's cross reaches the power face at its centre. Load: the
    hopper's mouth is the load cell's top face, and nothing else reaches the machine's top (the water goes in there
    too, by bucket: no pipe and no water anchor). Draw: the gate is in the draw cell, square to the draw
    face, its outer face the frontmost of the pit, between the front posts. The controller is the draw cell, at the
    front, where the player stands."""
    m, rig = v.m, v.rig
    size = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    anchors = {k: (tuple(rig[k]), rig[k.replace("Cell", "Face")]) for k in m.CELL_KEYS}
    if len(set(anchors.values())) != len(anchors):
        v.fail(f"two anchors share a face: {anchors}")
    if [k for k, (c, _) in anchors.items() if c == anchors["powerCell"][0] and k != "powerCell"]:
        v.fail("an anchor shares the power cell")
    out_of = {"west": (0, -1), "east": (0, 1), "down": (1, -1), "up": (1, 1), "north": (2, -1), "south": (2, 1)}
    for key, (cell, face) in anchors.items():
        k, sgn = out_of[face]
        edge = cell[k] == (0 if sgn < 0 else size[k] - 1)
        if not all(0 <= cell[q] < size[q] for q in range(3)) or not edge:
            v.fail(f"{key} {cell} {face}: not a face of the footprint's outside")
    if rig["powerFace"] in ("up", "down"):
        v.fail("the power face is not on a side")
    if tuple(m.DRAW_CELL) != tuple(m.ORIGIN_CELL) or m.DRAW_FACE != "north":
        v.fail("the draw gate is not at the controller, on the front")
    lo, hi, _ = cell_face_box(m, m.POWER_CELL, m.POWER_FACE)
    centre = [(lo[q] + hi[q]) / 2 for q in range(3)]
    axle = aabb_of(v.named("shaft", r"^sh_axle"))
    mid = [(axle[0][q] + axle[1][q]) / 2 for q in range(3)]
    off = max(abs(mid[0] - centre[0]), abs(mid[1] - centre[1]))
    print(f"anchors: the axle's cross reaches z {axle[1][2]:.2f} (the power face at {lo[2]:.0f}), {off:.3f} off its centre; {anchors}")
    if abs(axle[1][2] - lo[2]) > 0.02 or off > 1e-6:
        v.fail("the axle's cross does not meet the power face at its centre")
    # load: the hopper's mouth, and nothing else at the top
    flo, fhi, _ = cell_face_box(m, m.LOAD_CELL, m.LOAD_FACE)
    wlo, whi = aabb_of(v.posed("hopper", 0.0))
    inside = all(flo[q] - 1e-6 <= wlo[q] and whi[q] <= fhi[q] + 1e-6 for q in (0, 2))
    top = m.CELLS_Y * m.B
    high = sorted({e.name for pid in v.state(m.CONTENTS[0]) for e in v.posed(pid, 0.0) if e.aabb()[1][1] > top - 0.5})
    others = [n for n in high if not n.startswith("hopper_")]
    print(f"  load: the hopper's mouth x {wlo[0]:.1f}..{whi[0]:.1f}, z {wlo[2]:.1f}..{whi[2]:.1f}, top {whi[1]:.3f} (the load "
          f"cell's top {fhi[1]:.0f}); at the machine's top: {len(high)} of the hopper's walls, others {others or 'none'}")
    if not inside or abs(whi[1] - fhi[1]) > 0.02 or others or not high:
        v.fail("the hopper's mouth is not the load cell's top face, alone at the top")
    # draw: the gate in the draw cell, square to its face, the pit's frontmost, between the front posts
    gate = aabb_of(v.named("pit", r"_gate_"))
    clo = [m.DRAW_CELL[q] * m.B for q in range(3)]
    chi = [(m.DRAW_CELL[q] + 1) * m.B for q in range(3)]
    in_cell = all(clo[q] - 1e-6 <= gate[0][q] and gate[1][q] <= chi[q] + 1e-6 for q in range(3))
    front = min(e.aabb()[0][2] for e in v.posed("pit", 0.0))
    board = v.named("pit", r"_gate_board$")[0]
    square = board.local_axis_for(2) is not None and board.local_axis_for(0) is not None
    posts = (m.POST_X[0][1], m.POST_X[1][0])
    print(f"  draw: the gate x {gate[0][0]:.2f}..{gate[1][0]:.2f}, y 0..{gate[1][1]:.2f}, z {gate[0][2]:.2f}..{gate[1][2]:.2f}, in "
          f"the draw cell {in_cell}, square to its face {square}, its front at z {gate[0][2]:.2f} (the pit's front {front:.2f}), "
          f"between the front posts ({posts[0]}..{posts[1]})")
    if not (in_cell and square and abs(gate[0][2] - front) < 1e-4 and posts[0] < gate[0][0] and gate[1][0] < posts[1]):
        v.fail("the draw gate is not at the pit's front in the draw cell, between the front posts")


# ---------------------------------------------------------------- the pit
def check_pit(v):
    """The pit is at ground level, as period pits were (no basin on piers): the paving lies on the ground with its top
    at PAVE_TOP, nothing else under it inside the curb; the curb stands on the ground round it, every stone to the
    curb's top but the one at the front, left out for the draw gate (none is cut down: there is no pipe); at rest every
    drag stone lies on the paving, and over the cycle no stone leaves the paving or reaches the curb."""
    m = v.m
    pave = v.named("pit", r"_pave\d")
    lo = min(e.aabb()[0][1] for e in pave)
    hi = max(e.aabb()[1][1] for e in pave)
    curb = v.named("pit", r"_curb\d+$")
    every = [e for pid in v.state(None) for e in v.posed(pid, 0.0)]
    under = [e.name for e in every
             if not re.match(r"pit_(pave|centre|curb|edge|gate)|fr_post", e.name) and e.aabb()[0][1] < hi - 0.01
             and math.hypot((e.aabb()[0][0] + e.aabb()[1][0]) / 2 - m.CX, (e.aabb()[0][2] + e.aabb()[1][2]) / 2 - m.CZ) < m.CURB_R[0]]
    tops = sorted({round(e.aabb()[1][1], 2) for e in curb})
    stones = v.named("stones", r"_stone\d")
    rest = sorted({round(e.aabb()[0][1], 4) for e in stones})
    reach = 0.0
    for pose in cycle(m, 48):
        for e in v.named("stones", r"_stone\d", pose):
            for p in e.corners():
                reach = max(reach, math.hypot(p[0] - m.CX, p[2] - m.CZ))
    print(f"pit: paving y {lo:.2f}..{hi:.2f} on the ground, {len(curb)} curb stones from the ground to {tops} (the gate in the "
          f"front stone's place), nothing else "
          f"under the paving inside the curb: {not under}; the drag stones' undersides at rest {rest} (the paving "
          f"{m.PAVE_TOP}), reaching {reach:.2f} from the centre over the cycle (the curb at {m.CURB_R[0]})")
    if lo > 0.05 or abs(hi - m.PAVE_TOP) > 0.05 or under:
        v.fail(f"the pit is not paved at ground level: {under[:4]}")
    if min(e.aabb()[0][1] for e in curb) > 0.05 or tops[0] < m.CURB_TOP - 0.05 or len(curb) != m.CURB_N - 1:
        v.fail("the curb does not stand on the ground to its top, every stone but the gate's")
    if any(abs(r - m.PAVE_TOP) > 1e-3 for r in rest) or reach > m.CURB_R[0] - 0.3:
        v.fail("the drag stones do not run on the paving inside the curb")


# ---------------------------------------------------------------- the charge's way
def check_flow(v):
    """The charge's way, by hand. In: the player tips it into the hopper's mouth, and pours the water in after it by
    bucket; the bin's floor runs down to the outlet, and the spout runs from under the outlet down to over the stones'
    track, clear above the arms. The contents lie
    on the paving inside the curb, clear of the footstep, the drag stones standing out of them. Out: the gate's
    threshold is below the contents' tops (they are drawn out over it with the gate lifted), the gate stands above
    them, and its front is the draw face."""
    m = v.m
    floor = v.named("hopper", r"_floor$")[0]
    low = min(p[1] for p in floor.corners())
    spout = v.named("hopper", r"_spout_floor$")[0]
    cz = sorted(p[2] for p in spout.corners())
    (_, _), (ez, ey) = m.SPOUT
    r_end = abs(ez - m.CZ)
    under = min(p[1] for e in v.named("hopper", r"_spout_") for p in e.corners() if p[2] > ez - 1.0)
    print(f"flow: the bin's floor runs down to y {low:.2f} at the outlet (z {m.OUTLET_Z[0]}..{m.OUTLET_Z[1]}); the spout from "
          f"z {cz[0]:.2f} to its end at z {ez}, {r_end:.2f} from the centre (the stones' track {m.STONE_R[0]}..{m.STONE_R[1]}), "
          f"its underside there at y {under:.2f} (the arms' top {m.ARM_Y[1]})")
    if not (cz[0] <= m.OUTLET_Z[0] + 0.2 and cz[-1] > m.OUTLET_Z[1]):
        v.fail("the spout does not run from under the hopper's outlet")
    if not (m.STONE_R[0] <= r_end <= m.STONE_R[1]) or under < m.ARM_Y[1] + 0.3:
        v.fail("the spout's end is not over the stones' track, clear above the arms")
    tops = {k: max(e.aabb()[1][1] for e in v.posed(k, 0.0)) for k in m.CONTENTS}
    stone_top = min(e.aabb()[1][1] for e in v.named("stones", r"_stone\d"))
    sole = v.named("step", r"_sole$")[0].aabb()
    sole_r = math.hypot(sole[1][0] - m.CX, sole[1][2] - m.CZ)
    curb_corner = m.CURB_R[0] / math.cos(math.pi / m.CURB_N)

    def along(e, p):
        """How far corner p of a radial box e lies from the pit's axis along the box's own direction."""
        a = math.atan2(e.c[0] - m.CX, e.c[2] - m.CZ)
        return (p[0] - m.CX) * math.sin(a) + (p[2] - m.CZ) * math.cos(a)

    for k in m.CONTENTS:
        els = v.posed(k, 0.0)
        bottom = min(e.aabb()[0][1] for e in els)
        reach_out = max(math.hypot(p[0] - m.CX, p[2] - m.CZ) for e in els for p in e.corners())
        inner = min(along(e, p) for e in els for p in e.corners())
        print(f"  {k}: y {bottom:.2f}..{tops[k]:.2f}, bedded on the paving ({m.PAVE_TOP}), from {inner:.2f} (the footstep's sole's "
              f"corners at {sole_r:.2f}) out to {reach_out:.2f} (the curb's inner corners at {curb_corner:.2f}), under the "
              f"stones' tops ({stone_top:.2f})")
        if not m.PAVE_TOP - 0.5 <= bottom < m.PAVE_TOP or tops[k] >= stone_top - 0.5 or inner < sole_r + 0.2 or reach_out > curb_corner:
            v.fail(f"the {k} does not lie on the paving inside the curb, clear of the footstep, under the stones' tops")
    sill = v.named("pit", r"_gate_sill$")[0].aabb()
    board = v.named("pit", r"_gate_board$")[0].aabb()
    gate = aabb_of(v.named("pit", r"_gate_"))
    print(f"  draw: the gate's threshold at y {sill[1][1]:.2f} under the contents' tops {sorted(tops.values())}, the gate to "
          f"y {board[1][1]:.2f}, its front at z {gate[0][2]:.3f} (the draw face at 0)")
    if sill[1][1] >= min(tops.values()) or board[1][1] <= max(tops.values()) or abs(gate[0][2]) > 1e-6:
        v.fail("the charge cannot be drawn out over the gate's threshold at the draw face")


# ---------------------------------------------------------------- gearing
def velocity(v, pid, p, pose=0.0, h=1e-4):
    """The velocity (voxels per radian of theta) of the point of part pid that is at p (at pose)."""
    a = v.mat(pid, pose + h)
    b = v.mat(pid, pose - h)
    m0 = v.mat(pid, pose)
    r = [row[:3] for row in m0[:3]]
    t = [m0[i][3] * 16 for i in range(3)]
    q = [sum(r[j][i] * (p[j] - t[j]) for j in range(3)) for i in range(3)]
    pa = apply(a, [x / 16 for x in q])
    pb = apply(b, [x / 16 for x in q])
    return [(pa[i] - pb[i]) * 16 / (2 * h) for i in range(3)]


def mesh_contact(v, a, b, poses, eps_in=0.12, eps_near=-0.3):
    """Over `poses`: the worst pair of a's and b's elements that run into each other by more than about 2 eps_in
    (none, or a name pair), and whether at every pose some pair is within about 2 |eps_near| (the mesh engaged)."""
    (pa, ra), (pb, rb) = a, b
    clash, engaged = None, True
    for pose in poses:
        ea, eb = v.named(pa, ra, pose), v.named(pb, rb, pose)
        near = False
        for x in ea:
            xl, xh = x.aabb()
            for y in eb:
                yl, yh = y.aabb()
                if all(xl[k] < yh[k] + 0.7 and yl[k] < xh[k] + 0.7 for k in range(3)):
                    if obb_obb(x, y, eps=eps_in) and clash is None:
                        clash = (x.name, y.name, round(pose, 3))
                    if not near and obb_obb(x, y, eps=eps_near):
                        near = True
        engaged = engaged and near
    return clash, engaged


def omega(v, pid, h=0.01):
    """How fast part pid turns, radians per radian of the axle."""
    r = v.mat(pid, h)
    c = (r[0][0] + r[1][1] + r[2][2] - 1) / 2
    return math.acos(max(-1.0, min(1.0, c))) / h


def meshes(m):
    return [("the bevels", ("pinion", r"_bevel_"), ("post", r"_wheel_"), m.bevel_pitch_point(), m.BEVEL["pinion"], m.BEVEL["wheel"])]


def check_gearing(v):
    """The mesh: the two pitch points move together (the rig's ratio is the teeth's, with the right sign), the ratio is
    the tooth counts', and over a whole turn of the pinion, every 2.5 degrees, no tooth runs into the other wheel by
    more than about 0.25 voxels while some tooth is always within 0.6 of it (engaged)."""
    for label, (pa, ra), (pb, rb), pp, na, nb in meshes(v.m):
        va, vb = velocity(v, pa, pp), velocity(v, pb, pp)
        diff = max(abs(va[i] - vb[i]) for i in range(3))
        speed = math.sqrt(sum(x * x for x in va))
        wa, wb = omega(v, pa), omega(v, pb)
        span = 2 * math.pi / wa
        poses = [span * i / 144 for i in range(144)]
        clash, engaged = mesh_contact(v, (pa, ra), (pb, rb), poses)
        print(f"gearing, {label}: pitch points move together within {diff:.2e} ({speed:.3f} voxels a radian of the axle), "
              f"{na}:{nb} teeth, turning {wa:.4f} and {wb:.4f} a radian of the axle (ratio {wb / wa:.4f}, teeth {na / nb:.4f}); "
              f"a turn of the pinion every 2.5 degrees: no tooth into the other wheel {clash is None}"
              f"{'' if clash is None else f' {clash}'}, always engaged {engaged}")
        if diff > 1e-3 or speed < 1e-3:
            v.fail(f"{label}: the pitch points do not move together")
        if abs(wb / wa - na / nb) > 1e-3:
            v.fail(f"{label}: the ratio is not the tooth counts'")
        if clash is not None or not engaged:
            v.fail(f"{label}: the teeth do not run together")


# ---------------------------------------------------------------- supports
def check_supports(v):
    """Every shaft carried: the power shaft in its two drop hangers; the post on its footstep and in its top bearing;
    the pinion's hub on the power shaft's end."""
    m = v.m

    def held(label, shaft_els, holders, axis, c, need=2):
        found, _ = supports(holders, shaft_els, axis, c)
        print(f"supports: {label} in {sorted(found)}")
        if len(found) < need:
            v.fail(f"{label} is carried by {found}")

    held("the power shaft", v.named("shaft", r"^sh_rod"), v.named("frame", r"^fr_hanger\d_box"), 2, (m.CX, m.SHAFT_Y))
    held("the post", v.named("post", r"gudgeon|_post\d"), v.named("step", r"_(block|bearing)$"), 1, (m.CX, m.CZ))
    hub = aabb_of(v.named("pinion", r"_hub"))
    if abs(hub[0][2] - m.SHAFT_END) > 1e-6:
        v.fail("the pinion's hub does not start at the power shaft's end")


# ---------------------------------------------------------------- clearances
ALLOWED = [
    # shafts in their bearings and footstep
    ("shaft", r"^sh_rod", "frame", r"^fr_hanger"),
    ("post", r"gudgeon", "step", None),
    # the drag stones drag on the paving and through the charge
    ("stones", r"_stone\d", "pit", r"_pave\d"),
    ("stones", r"_stone\d", "charge|paste", None),
    # the pinion keyed on the power shaft's end
    ("pinion", r"_hub", "shaft", r"^sh_rod"),
    # meshing teeth (check_gearing holds them)
    ("pinion", r"_bevel_", "post", r"_wheel_"),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose, pids):
    """Every pair of elements of two parts that move against each other (their matrices differ) that overlap, but
    for the intended contacts."""
    mats = {pid: v.mat(pid, pose) for pid in pids}

    def together(a, b):
        return max(abs(mats[a][i][j] - mats[b][i][j]) for i in range(3) for j in range(4)) < 1e-12

    items = []
    for pid in pids:
        for el in v.posed(pid, pose):
            items.append((pid, el, el.aabb()))
    grid = {}
    for i, (pid, el, (lo, hi)) in enumerate(items):
        for cx in range(int(lo[0] // 4), int(hi[0] // 4) + 1):
            for cy in range(int(lo[1] // 4), int(hi[1] // 4) + 1):
                for cz in range(int(lo[2] // 4), int(hi[2] // 4) + 1):
                    grid.setdefault((cx, cy, cz), []).append(i)
    hits, seen = {}, set()
    for idx in grid.values():
        for a in range(len(idx)):
            for b in range(a + 1, len(idx)):
                i, j = idx[a], idx[b]
                if (i, j) in seen:
                    continue
                seen.add((i, j))
                pa, ea, (alo, ahi) = items[i]
                pb, eb, (blo, bhi) = items[j]
                if pa == pb or together(pa, pb):
                    continue
                if not all(alo[q] < bhi[q] - TOL and blo[q] < ahi[q] - TOL for q in range(3)):
                    continue
                if allowed(pa, ea.name, pb, eb.name):
                    continue
                if obb_obb(ea, eb):
                    hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def check_clearances(v, poses, label):
    """Every pair of parts that move against each other, over the poses, touches only at the intended contacts
    (`ALLOWED`). Both contents are in the one state: they stand still with the frame, so they are only checked
    against what turns."""
    pids = list(v.by_part)
    allhits = {}
    for pose in poses:
        for key, hs in touching_pairs(v, pose, pids).items():
            allhits.setdefault(key, {})
            for h in hs:
                allhits[key].setdefault(h, pose)
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {p:.3f}" for (a, b), p in ex))
    print(f"{label} over {len(poses)} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    if allhits:
        v.fail("parts run into each other")


def check_gaps(v):
    """The parts that pass close to each other keep a visible gap over the cycle: the sweep arms under the spout, the
    bevel wheel under the hopper, and the sweep (the arms and the stones) clear of the curb and the gate."""
    m = v.m
    rows = []
    for label, (pa, ra), (pb, rb) in (("arms under the spout", ("arms", r"_arm"), ("hopper", r"_spout")),
                                      ("wheel under the hopper", ("post", r"_wheel"), ("hopper", r".")),
                                      ("arms clear of the curb and the gate", ("arms", r"."), ("pit", r"_(curb|gate)")),
                                      ("stones clear of the curb and the gate", ("stones", r"."), ("pit", r"_(curb|gate)"))):
        near = None
        for pose in cycle(m, 24):
            for a in v.named(pa, ra, pose):
                for b in v.named(pb, rb, pose):
                    if near is None and obb_obb(a, b, eps=-0.15):   # each grown by 0.15: 0.3 between faces
                        near = (a.name, b.name, round(pose, 3))
        rows.append((label, near))
    print("gaps: " + "; ".join(f"{label} at least 0.3 apart: {near is None}" for label, near in rows))
    for label, near in rows:
        if near:
            v.fail(f"{label}: closer than 0.3 voxels, {near}")


# ---------------------------------------------------------------- z-fighting
def check_zfight(v):
    m = v.m
    bad = 0
    for k in m.CONTENTS:
        for pose in m.coplanar_poses():
            els = [e for pid in v.state(k) for e in v.posed(pid, pose)]
            pairs = coplanar_faces(els)
            if pairs:
                bad += len(pairs)
                print(f"  coplanar (with the {k}) at theta {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses with each content")
    if bad:
        v.fail("faces z-fight")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_chunky(v)
    check_build(v)
    check_textures(v)
    check_anchors(v)
    check_pit(v)
    check_flow(v)
    check_supports(v)
    check_containment(v, cycle(m, 48))
    check_cells(v, cycle(m, 48))
    check_gearing(v)
    check_gaps(v)
    check_clearances(v, cycle(m, 72), "clearances")
    if not quick:
        check_clearances(v, cycle(m, 216), "swept paths (every 5 degrees of the axle over its three-turn cycle)")
        check_zfight(v)
    return v.ok


def validate_files(m, shape, frame_shape, ship):
    """The written files: every texture declared; the frame shape the frame part alone; the footprint's cells, every
    one holding part of the machine (none hollow), with boxes inside them, no lids; batch and watered by hand: no chute
    and no pipe (no chute, pipe or solder texture), and of the anchors only the power and the two hand actions (no
    infeed, output, chute or water); the rig's build lists the stages in order."""
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL textures used but not declared: {missing}")
        ok = False
    lids = [c["pos"] for c in ship["cells"] if "lid" in c]
    hollow = [c["pos"] for c in ship["cells"] if c.get("hollow")]
    framed = [c["pos"] for c in ship["cells"] if c.get("boxes")]
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, "
          f"{len(framed)} with boxes, hollow {hollow or 'none'}, lids {lids or 'none'}")
    if len(ship["cells"]) != len(m.footprint()) or lids or hollow:
        print("FAIL the cells are not the footprint's, each holding part of the machine, without lids")
        ok = False
    for c in ship["cells"]:
        for b in c.get("boxes") or []:
            if not (all(0 <= x <= 1 for x in b) and all(b[k] < b[k + 3] for k in range(3))):
                print(f"FAIL a box outside its cell: {c['pos']} {b}")
                ok = False
    frame_names = {e["name"] for e in frame_shape["elements"]}
    if not frame_names or any(not n.startswith("fr_") for n in frame_names):
        print("FAIL the frame shape is not the frame part")
        ok = False
    keys = [k for k in ship if k not in ("_comment", "cells", "parts", "build")]
    want = [k for key in m.CELL_KEYS for k in (key, key.replace("Cell", "Face"))]
    chutes = [k for k in shape["textures"] if any(w in k or w in shape["textures"][k] for w in ("chute", "pipe", "solder"))]
    print(f"  anchors {keys}; chute or pipe textures {chutes or 'none'}; build stages {[s['requires'] for s in ship['build']['stages']]}")
    if keys != want or chutes:
        print("FAIL batch and watered by hand: the anchors are the power and the two hand actions, and nothing is a chute or pipe")
        ok = False
    if [s["requires"] for s in ship["build"]["stages"]] != [req for req, _ in m.STAGES]:
        print("FAIL the rig's build stages are not the generator's")
        ok = False
    return ok
