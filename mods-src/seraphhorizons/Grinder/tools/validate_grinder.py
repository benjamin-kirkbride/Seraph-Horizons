"""The grinder generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its
rule. Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in
make_shape.py proves the shipped files are this model moved.

The model holds three states that are never drawn together: the frame alone, and the frame with level 1 (the
arrastra) or level 2 (the ball mill, which retires the arrastra's parts) fitted. Every check that compares
parts with each other does it a state at a time (`state`).
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
        # the pipe's other metals are the first's elements copied after the checks; only one is ever drawn
        self.copies = [pid for pid in m.PIPE_PARTS[1:]]
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

    def state(self, k):
        """The parts drawn in state k (0: the frame alone; 1, 2: the frame and that level)."""
        return [pid for pid in self.by_part if self.m.level_of(pid) in (0, k)]


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
    empty = [p["id"] for p in v.parts if not v.by_part.get(p["id"]) and p["id"] not in v.copies]
    if empty:
        v.fail(f"parts with no elements: {empty}")
    counts = {k: sum(len(v.by_part[pid]) for pid in v.by_part if v.m.level_of(pid) == k) for k in (0, 1, 2)}
    print(f"parts: {len(v.parts)}, elements {len(v.els)}: frame and shaft {counts[0]}, level 1 (arrastra) {counts[1]}, "
          f"level 2 (ball mill) {counts[2]}; drawn: frame alone {counts[0]}, level 1 {counts[0] + counts[1]}, "
          f"level 2 {counts[0] + counts[2]}")


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
    """The footprint is the box less EMPTY_CELLS: no element of any level reaches into those, at any pose
    (exactly, each element's box against the cell's, not by bounding boxes)."""
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


# ---------------------------------------------------------------- the build: levels, stages, nothing floating
def joined(a, b, la, lb, aligned_a, aligned_b):
    """Whether two elements hold each other: machinegen's frame_floating rule (a shared face between two
    axis-aligned boxes, else an overlap)."""
    ov = [min(la[1][k], lb[1][k]) - max(la[0][k], lb[0][k]) for k in range(3)]
    if min(ov) < -0.02:
        return False
    if aligned_a and aligned_b:
        return sum(o > 0.05 for o in ov) >= 2
    return obb_obb(a, b, eps=-0.03)


def stage_floating(groups, ground=0.01):
    """Each stage's elements (`groups`: [(stage, elements)], in build order) joined to the ground through what is
    already there and itself: {stage: the names of its elements that float}. (The eidolon gantry's rule.)"""
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
    """The levels: every part but the frame's (the frame, the power shaft) belongs to one level's stage and needs
    its requires; no requires value is in two stages; level 2 retires exactly level 1's parts (the ball mill goes
    into the arrastra's pit) and keeps the frame; every step can be built, nothing floating: at rest the frame,
    then each of a level's stages in turn, joined to the ground through what is already there."""
    m = v.m
    staged = m.stage_of_part()
    reqs = [req for _, _, _, stages, _ in m.LEVELS for req, _ in stages]
    if len(reqs) != len(set(reqs)):
        v.fail(f"a requires value is in two stages: {reqs}")
    for p in v.parts:
        pid, want = p["id"], staged.get(p["id"])
        if pid in m.PIPE_PARTS:
            if p["requires"] != pid:
                v.fail(f"the pipe's {pid} needs {p['requires']}, not its own metal")
        elif m.level_of(pid) == 0:
            if p["requires"] is not None:
                v.fail(f"the frame's {pid} needs {p['requires']}")
        elif want is None or p["requires"] != want or not want.startswith(f"l{m.level_of(pid)}"):
            v.fail(f"{pid} needs {p['requires']}, its stage is {want}")
    (l1, _, _, s1, r1), (l2, _, _, s2, r2) = m.LEVELS
    if r1 or sorted(r2) != sorted(req for req, _ in s1):
        v.fail(f"level 2 should retire level 1's parts and nothing else: {r2}")
    base = [el for pid in v.state(0) for el in v.posed(pid, 0.0)]
    floating = stage_floating([("frame", base)])
    if floating["frame"]:
        v.fail(f"frame elements float: {floating['frame'][:8]}")
    for level, tier, name, stages, _ in m.LEVELS:
        groups = [("frame", base)] + [(req, [el for pid in pids for el in v.posed(pid, 0.0)]) for req, pids in stages]
        loose = {k: n for k, n in stage_floating(groups).items() if n}
        print(f"build, level {level} ({tier}: {name}): frame ({len(base)}) > " + " > ".join(f"{req} ({len(els)})" for req, els in groups[1:])
              + f"; floating: {loose or 'nothing'}")
        if loose:
            v.fail(f"level {level}'s build floats: {loose}")


# ---------------------------------------------------------------- textures
ROLES = [
    (r"^fr_(hopper_|chute_|launder_(floor|n|s)$)", {"planks"}),
    (r"^fr_(post|stringer|brace|transom|launder_post)", {"oak"}),
    (r"^fr_hanger", {"iron"}),
    (r"^pipecopper_", {"pipecopper", "solder"}),
    (r"^fr_(curb)", {"drystone"}),
    (r"^fr_pave", {"cobble"}),
    (r"^fr_centre", {"granite"}),
    (r"^sh_axle", {"axle"}),
    (r"^sh_(rod|collar)", {"steel"}),
    (r"^l1post_post", {"oak"}),
    (r"^l1post_(foot|top)gudgeon|^l1pinion_|^l2pinion_|^l2balls_", {"steel"}),
    (r"^l1post_|^l1step_|^l1arms_staple|^l1stones_eye|^l2bed_|^l2drum_(?!shell)|^l2feed_", {"iron"}),
    (r"^l1arms_arm", {"oak"}),
    (r"^l1stones_stone", {"basalt"}),
    (r"^l1stones_chain", {"chain"}),
    (r"^l2drum_shell", {"riveted"}),
]


def check_textures(v):
    bad = []
    for el in v.els:
        tex = {f["texture"].lstrip("#") for f in el.faces.values()}
        rule = next((want for rx, want in ROLES if re.search(rx, el.name)), None)
        if rule is None or not tex <= rule:
            bad.append((el.name, sorted(tex), sorted(rule or [])))
    print(f"textures by role: {len(v.els) - len(bad)} of {len(v.els)} elements as their role says (oak for the timber, plank "
          f"boxes, iron castings and fittings, steel shafts, pinions and balls, granite for the pit, basalt drag stones, a "
          f"riveted shell, chains, a lead pipe)")
    if bad:
        v.fail(f"textures off their role: {bad[:6]}")


# ---------------------------------------------------------------- anchors, the pit, the ore's way
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
    """The anchor faces are on the footprint's outside, looking out of it, and all different; the power cell is
    no other anchor's; the power face is on a side; the axle's cross reaches the power face at its centre; the
    hopper's mouth is the infeed cell's top face; the inlet pipe's end is the water face's centre, on its axis;
    the launder's lip reaches the output face in the output cell and output.pos is its end."""
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
    lo, hi, _ = cell_face_box(m, m.POWER_CELL, m.POWER_FACE)
    centre = [(lo[q] + hi[q]) / 2 for q in range(3)]
    axle = aabb_of(v.named("shaft", r"^sh_axle"))
    mid = [(axle[0][q] + axle[1][q]) / 2 for q in range(3)]
    off = max(abs(mid[0] - centre[0]), abs(mid[1] - centre[1]))
    print(f"anchors: the axle's cross reaches z {axle[1][2]:.2f} (the power face at {lo[2]:.0f}), {off:.3f} off its centre; {anchors}")
    if abs(axle[1][2] - lo[2]) > 0.02 or off > 1e-6:
        v.fail("the axle's cross does not meet the power face at its centre")
    walls = v.named("frame", r"^fr_hopper_(n|s|w\d|e\d)$")
    wlo, whi = aabb_of(walls)
    flo, fhi, _ = cell_face_box(m, m.INFEED_CELL, "up")
    inside = all(flo[q] - 1e-6 <= wlo[q] and whi[q] <= fhi[q] + 1e-6 for q in (0, 2))
    print(f"  infeed: the hopper's mouth x {wlo[0]:.1f}..{whi[0]:.1f}, z {wlo[2]:.1f}..{whi[2]:.1f}, top {whi[1]:.2f} (the cell's top {fhi[1]:.0f})")
    if not inside or abs(whi[1] - fhi[1]) > 1e-6:
        v.fail("the hopper's mouth is not the infeed cell's top face")
    pipe = aabb_of(v.named("pipecopper", r"_inlet_"))
    flo, fhi, k = cell_face_box(m, m.WATER_CELL, m.WATER_FACE)
    centre = [(flo[q] + fhi[q]) / 2 for q in range(3)]
    pc = [(pipe[0][q] + pipe[1][q]) / 2 for q in range(3)]
    end = pipe[0][k] if m.WATER_FACE in ("north", "west", "down") else pipe[1][k]
    across = max(abs(pc[q] - centre[q]) for q in range(3) if q != k)
    width = [pipe[1][q] - pipe[0][q] for q in range(3) if q != k]
    floor_at = max(floor_height(v, m.HOP_Z[0] + m.HOP_WALL), m.HOP_FLOOR[1][1])
    print(f"  water: the inlet pipe ends at {end:.2f} on the face's axis ({across:.3f} off its centre), {width} across "
          f"(ppex's 6), through the hopper's wall to z {pipe[1][2]:.2f}; its bore from y {m.PIPE_Y - 2:.1f}, the hopper's floor "
          f"or outlet under it at {floor_at:.2f}")
    if abs(end - flo[k]) > 1e-6 or across > 1e-6 or any(abs(w - 2 * 3.0) > 1e-6 for w in width):
        v.fail("the inlet pipe does not meet the water face on the ppex pipe's axis")
    if abs(pipe[1][2] - (m.HOP_Z[0] + m.HOP_WALL)) > 0.05 or m.PIPE_Y - 2 < floor_at:
        v.fail("the inlet pipe does not open into the hopper above its floor")
    lip = aabb_of(v.named("frame", r"^fr_launder_floor$"))
    flo, fhi, _ = cell_face_box(m, m.OUTPUT_CELL, m.OUTPUT_FACE)
    if abs(lip[1][0] - flo[0]) > 1e-3 or not (flo[1] <= lip[0][1] and lip[1][1] <= fhi[1] and flo[2] <= lip[0][2] and lip[1][2] <= fhi[2]):
        v.fail("the launder does not reach the output face in the output cell")
    out = [p * m.B for p in rig["output"]["pos"]]
    if abs(out[0] - flo[0]) > 1e-6 or not (lip[0][2] <= out[2] <= lip[1][2]) or abs(out[1] - m.LAUNDER_LIP) > 1e-6:
        v.fail("output.pos is not the launder floor's end")


def floor_height(v, z):
    """The hopper floor's top at z (its sloping board)."""
    (z0, y0), (z1, y1) = v.m.HOP_FLOOR
    return y1 + (y0 - y1) * (z - z1) / (z0 - z1)


def check_pit(v):
    """The arrastra's pit is at ground level, as period pits were (no basin on piers): the paving lies on the
    ground with its top at PAVE_TOP, nothing of the frame is under it, the curb stands on the ground round it and
    its lowest top is the notch's sill; at rest every drag stone lies on the paving, and over the cycle no stone
    leaves the paving or reaches the curb."""
    m = v.m
    pave = v.named("frame", r"^fr_pave\d")
    lo = min(e.aabb()[0][1] for e in pave)
    hi = max(e.aabb()[1][1] for e in pave)
    curb = v.named("frame", r"^fr_curb")
    under = [e.name for e in v.posed("frame", 0.0)
             if not re.match(r"fr_(pave|centre|curb)", e.name) and e.aabb()[0][1] < hi - 0.01
             and math.hypot((e.aabb()[0][0] + e.aabb()[1][0]) / 2 - m.CX, (e.aabb()[0][2] + e.aabb()[1][2]) / 2 - m.CZ) < m.CURB_R[0]]
    tops = sorted({round(e.aabb()[1][1], 3) for e in curb})
    stones = v.named("l1stones", r"_stone\d")
    rest = sorted({round(e.aabb()[0][1], 4) for e in stones})
    reach = 0.0
    for pose in cycle(m, 48):
        for e in v.named("l1stones", r"_stone\d", pose):
            for p in e.corners():
                reach = max(reach, math.hypot(p[0] - m.CX, p[2] - m.CZ))
    print(f"pit: paving y {lo:.2f}..{hi:.2f} on the ground, the curb from the ground to {tops[-1]:.2f} (its notch's sill "
          f"{tops[0]:.2f}), nothing else under the paving inside the curb: {not under}; the drag stones' undersides at rest "
          f"{rest} (the paving {m.PAVE_TOP}), reaching {reach:.2f} from the centre over the cycle (the curb at {m.CURB_R[0]})")
    if lo > 0.01 or abs(hi - m.PAVE_TOP) > 0.01 or under:
        v.fail(f"the pit is not paved at ground level: {under[:4]}")
    if min(e.aabb()[0][1] for e in curb) > 0.01 or abs(tops[0] - m.NOTCH_SILL) > 0.01:
        v.fail("the curb does not stand on the ground with the notch's sill its lowest top")
    if any(abs(r - m.PAVE_TOP) > 1e-3 for r in rest) or reach > m.CURB_R[0] - 0.3:
        v.fail("the drag stones do not run on the paving inside the curb")


def check_flow(v):
    """The ore's way, through the same cells at both levels. Everything comes in by the hopper's mouth (fresh ore,
    and at level 2 the concentrator's middlings: the owner's ruling) and the water by the inlet: of all the parts only
    the hopper reaches the machine's top face, and its floor runs down to the outlet. The chute runs from the
    outlet down south: at level 1 its end is over the drag stones' track, above the sweep arms' path; at level 2
    the feed lip continues it into the north trunnion's bore. The ball mill's lip discharges over the paving inside
    the curb, and the pulp of both levels leaves by the notch: the launder's floor runs down from the sill to the
    output face."""
    m = v.m
    top = m.CELLS_Y * m.B
    high = sorted({e.name for pid in v.by_part for e in v.posed(pid, 0.0) if e.aabb()[1][1] > top - 0.5})
    others = [n for n in high if not n.startswith("fr_hopper")]
    floor = v.named("frame", r"^fr_hopper_floor$")[0]
    low = min(p[1] for p in floor.corners())
    print(f"flow: the only way in at the top is the hopper's mouth: parts at the top face {len(high)}, all the hopper's "
          f"{not others}; its floor runs down to y {low:.2f} at the outlet (z {m.OUTLET_Z[0]}..{m.OUTLET_Z[1]})")
    if others:
        v.fail(f"parts other than the hopper reach the top face, a second way in: {others[:6]}")
    chute = v.named("frame", r"^fr_chute_floor$")[0]
    cz = sorted(p[2] for p in chute.corners())
    if not (cz[0] <= m.OUTLET_Z[0] + 0.2 and cz[-1] > m.OUTLET_Z[1]):
        v.fail("the chute does not run from under the hopper's outlet")
    (_, _), (ez, ey) = m.CHUTE
    r_end = abs(ez - m.CZ)
    under = min(p[1] for e in v.named("frame", r"^fr_chute_") for p in e.corners() if p[2] > ez - 1.0)
    print(f"  level 1: the chute's end at z {ez}, {r_end:.2f} from the centre (the stones' track {m.STONE_R[0]}..{m.STONE_R[1]}), "
          f"its underside there at y {under:.2f} (the arms' top {m.ARM_Y[1]})")
    if not (m.STONE_R[0] <= r_end <= m.STONE_R[1]) or under < m.ARM_Y[1] + 0.3:
        v.fail("level 1: the chute's end is not over the stones' track, clear above the arms")
    lip = v.named("l2feed", r"_floor$")[0]
    start = min(lip.corners(), key=lambda p: p[2])
    end = max(lip.corners(), key=lambda p: p[2])
    worst = max(math.hypot(p[0] - m.CX, p[1] - m.DRUM_Y) for e in v.named("l2feed", r".") for p in e.corners() if p[2] > m.N_TRUNNION_Z[0])
    print(f"  level 2: the feed lip from z {start[2]:.2f} (the chute's end {ez}) to {end[2]:.2f} in the north trunnion "
          f"({m.N_TRUNNION_Z[0]}..{m.N_TRUNNION_Z[1]}), at most {worst:.2f} from the axis in it (the bore {m.TRUNNION_R[0]})")
    if start[2] > ez + 0.3 or not (m.N_TRUNNION_Z[0] < end[2] <= m.N_TRUNNION_Z[1]) or worst > m.TRUNNION_R[0] - 0.15:
        v.fail("level 2's feed lip does not run from the chute into the north trunnion's bore")
    dl = aabb_of(v.named("l2drum", r"_lip"))
    tip = [m.CX, dl[0][1], dl[1][2]]
    r_tip = math.hypot(tip[0] - m.CX, tip[2] - m.CZ)
    print(f"  level 2: the drum's discharge lip ends at z {tip[2]:.2f}, its bottom y {tip[1]:.2f}: {r_tip:.2f} from the "
          f"centre, inside the curb ({m.CURB_R[0]})")
    if r_tip >= m.CURB_R[0] - 0.3 or tip[1] < m.CURB_TOP:
        v.fail("the drum does not discharge into the pit")
    fl = v.named("frame", r"^fr_launder_floor$")[0]
    west = max(p[1] for p in fl.corners() if p[0] < m.LAUNDER_X[0] + 0.5)
    east = max(p[1] for p in fl.corners() if p[0] > m.LAUNDER_X[1] - 0.5)
    print(f"  the overflow: the launder's floor from y {west:.2f} at the notch (its sill {m.NOTCH_SILL}) down to {east:.2f} at the face")
    if abs(west - m.NOTCH_SILL) > 0.3 or east >= west:
        v.fail("the launder does not run down from the notch's sill to the output face")


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
    return [
        ("level 1's bevels", ("l1pinion", r"_bevel_"), ("l1post", r"_wheel_"), m.bevel_pitch_point(), m.BEVEL["pinion"], m.BEVEL["wheel"]),
        ("level 2's girth gear", ("l2pinion", r"_gear_"), ("l2drum", r"_girth"), m.girth_pitch_point(), m.PINION["teeth"], m.GIRTH["teeth"]),
    ]


def check_gearing(v):
    """Every mesh: the two pitch points move together (the rig's ratios are the teeth's, with the right signs), the
    ratio is the tooth counts', and over a whole turn of the faster wheel, every 2.5 degrees, no tooth runs into the
    other wheel by more than about 0.25 voxels while some tooth is always within 0.6 of it (engaged)."""
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
              f"a turn of the first every 2.5 degrees: no tooth into the other wheel {clash is None}"
              f"{'' if clash is None else f' {clash}'}, always engaged {engaged}")
        if diff > 1e-3 or speed < 1e-3:
            v.fail(f"{label}: the pitch points do not move together")
        if abs(wb / wa - na / nb) > 1e-3:
            v.fail(f"{label}: the ratio is not the tooth counts'")
        if clash is not None or not engaged:
            v.fail(f"{label}: the teeth do not run together")


# ---------------------------------------------------------------- supports
def check_supports(v):
    """Every shaft carried: the power shaft in its two drop hangers; the arrastra's post on its footstep and in its
    top bearing; the drum's trunnions each through a bearing closed round it on four sides; each pinion's hub on the
    power shaft's end."""
    m = v.m

    def held(label, shaft_els, holders, axis, c, need=2):
        found, _ = supports(holders, shaft_els, axis, c)
        print(f"supports: {label} in {sorted(found)}")
        if len(found) < need:
            v.fail(f"{label} is carried by {found}")

    held("the power shaft", v.named("shaft", r"^sh_rod"), v.named("frame", r"^fr_hanger\d_box"), 2, (m.CX, m.SHAFT_Y))
    held("the arrastra's post", v.named("l1post", r"gudgeon|_post\d"), v.named("l1step", r"_(block|bearing)$"), 1, (m.CX, m.CZ))
    th = m.TRUNNION_R[1]
    for i, ((z0, z1), tz) in enumerate(zip(m.BEARINGS_Z, (m.N_TRUNNION_Z, m.S_TRUNNION_Z)), 1):
        walls = {k: v.named("l2bed", rf"_bearing{i}_{k}$")[0].aabb() for k in ("low", "cap", "w", "e")}
        closes = (abs(walls["low"][1][1] - (m.DRUM_Y - th)) < 1e-6 and abs(walls["cap"][0][1] - (m.DRUM_Y + th)) < 1e-6
                  and abs(walls["w"][1][0] - (m.CX - th)) < 1e-6 and abs(walls["e"][0][0] - (m.CX + th)) < 1e-6)
        through = tz[0] < z0 and tz[1] > z1
        print(f"supports: the drum's {'north' if i == 1 else 'south'} trunnion (z {tz[0]}..{tz[1]}) through bearing {i} "
              f"(z {z0}..{z1}): {through}, closed round it at {th}: {closes}")
        if not (closes and through):
            v.fail(f"the drum's trunnion is not carried in bearing {i}")
    for pid in ("l1pinion", "l2pinion"):
        hub = aabb_of(v.named(pid, r"_hub"))
        if abs(hub[0][2] - m.SHAFT_END) > 1e-6:
            v.fail(f"{pid}'s hub does not start at the power shaft's end")


# ---------------------------------------------------------------- clearances
ALLOWED = [
    # shafts in their bearings and footsteps
    ("shaft", r"^sh_rod", "frame", r"^fr_hanger"),
    ("l1post", r"gudgeon", "l1step", None),
    ("l2drum", r"_[ns]trunnion", "l2bed", r"_bearing"),
    # the drag stones drag on the paving
    ("l1stones", r"_stone\d", "frame", r"^fr_pave\d"),
    # the charge lies on the shell as it turns under it
    ("l2balls", None, "l2drum", r"_shell"),
    # pinions keyed on the power shaft's end
    ("l1pinion|l2pinion", r"_hub", "shaft", r"^sh_rod"),
    # meshing teeth (check_gearing holds them)
    ("l1pinion", r"_bevel_", "l1post", r"_wheel_"), ("l2pinion", r"_gear_", "l2drum", r"_girth"),
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
    total = 0
    for k in (1, 2):
        pids = v.state(k)
        allhits = {}
        for pose in poses:
            for key, hs in touching_pairs(v, pose, pids).items():
                allhits.setdefault(key, {})
                for h in hs:
                    allhits[key].setdefault(h, pose)
        total += len(allhits)
        for key, hs in sorted(allhits.items()):
            ex = sorted(hs.items())[:3]
            print(f"  TOUCH (level {k}) {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {p:.3f}" for (a, b), p in ex))
    print(f"{label} over {len(poses)} poses a level: {'all clear' if not total else f'{total} pairs of parts touch'}")
    if total:
        v.fail("parts run into each other")


def check_gaps(v):
    """The parts that pass close to each other keep a visible gap over the cycle: the sweep arms under the chute's
    end, the bevel wheel under the hopper's floor, the drum under the power shaft and clear of the chute."""
    m = v.m
    rows = []
    for label, (pa, ra), (pb, rb), k in (("arms under the chute", ("l1arms", r"_arm"), ("frame", r"^fr_chute"), 1),
                                         ("wheel under the hopper", ("l1post", r"_wheel"), ("frame", r"^fr_hopper"), 1),
                                         ("drum under the power shaft", ("l2drum", r"."), ("shaft", r"."), 2),
                                         ("drum clear of the chute", ("l2drum", r"."), ("frame", r"^fr_chute"), 2)):
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
    for k in (0, 1, 2):
        for pose in m.coplanar_poses():
            els = [e for pid in v.state(k) for e in v.posed(pid, pose)]
            pairs = coplanar_faces(els)
            if pairs:
                bad += len(pairs)
                print(f"  coplanar (state {k}) at theta {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses of each state")
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
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL textures used but not declared: {missing}")
        ok = False
    reqs = {p["requires"] for p in ship["parts"]} - {None}
    pipes = {f"pipe{x}" for x in m.PIPE_METALS}
    lids = [c["pos"] for c in ship["cells"] if "lid" in c]
    hollow = [c["pos"] for c in ship["cells"] if c.get("hollow")]
    keys = {k for c in ship["cells"] for k in c.get("fitted", {})}
    framed = [c["pos"] for c in ship["cells"] if c.get("boxes")]
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, "
          f"{len(framed)} with frame boxes, fitted boxes for {sorted(keys)}, hollow {hollow or 'none'}, lids {lids or 'none'}")
    if len(ship["cells"]) != len(m.footprint()) or lids or not keys <= reqs:
        print("FAIL the cells are not the footprint's, with fitted boxes keyed by requires values")
        ok = False
    for c in ship["cells"]:
        for bs in [c.get("boxes") or []] + list(c.get("fitted", {}).values()):
            for b in bs:
                if not (all(0 <= x <= 1 for x in b) and all(b[k] < b[k + 3] for k in range(3))):
                    print(f"FAIL a box outside its cell: {c['pos']} {b}")
                    ok = False
    frame_names = {e["name"] for e in frame_shape["elements"]}
    if not frame_names or any(not n.startswith("fr_") for n in frame_names):
        print("FAIL the frame shape is not the frame part")
        ok = False
    levels = ship["levels"]
    if {r for lv in levels for r in lv["fitted"]} != reqs - pipes or [lv["level"] for lv in levels] != [1, 2] \
            or [lv["tier"] for lv in levels] != ["water", "mill"]:
        print(f"FAIL the rig's levels do not list every requires value once: {levels}")
        ok = False
    return ok
