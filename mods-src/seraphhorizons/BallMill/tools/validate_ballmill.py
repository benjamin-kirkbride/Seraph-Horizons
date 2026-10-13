"""The ball mill generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its
rule. Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in
make_shape.py proves the shipped files are this model moved. The model is the finished machine, every part
fitted (the water pipe in its first metal; the other is the same boxes, copied after these checks).
"""

from __future__ import annotations

import json
import math
import os
import re
from pathlib import Path

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, obb_obb, supports
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
        self.copies = list(m.PIPE_PARTS[1:])
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

    def drawn(self):
        """The parts drawn together: all of them, the pipe in its first metal."""
        return [pid for pid in self.by_part if pid not in self.copies]


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
    counts = {pid: len(v.by_part[pid]) for pid in v.by_part}
    print(f"parts: {len(v.parts)}, elements {len(v.els)} (the lead pipe's copies come after): {counts}")


def check_chunky(v):
    """Readable at block scale: no element is a rod thinner than about a voxel (its two smaller sides both under
    0.9); a plate (one thin side: a sheet, a screen, a collar) is a surface and may be thin."""
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


def check_cells(v):
    """Every cell of the footprint holds part of the machine (none is a ghost with nothing in it)."""
    m = v.m
    used = set()
    for pid in v.drawn():
        for el in v.posed(pid, 0.0):
            lo, hi = el.aabb()
            for cx in range(int(lo[0] // m.B), int(math.ceil(hi[0] / m.B))):
                for cy in range(int(lo[1] // m.B), int(math.ceil(hi[1] / m.B))):
                    for cz in range(int(lo[2] // m.B), int(math.ceil(hi[2] / m.B))):
                        used.add((cx, cy, cz))
    empty = [c for c in m.footprint() if c not in used]
    print(f"footprint: {len(m.footprint())} cells ({m.CELLS_X} x {m.CELLS_Y} x {m.CELLS_Z}); empty: {empty or 'none'}")
    if empty:
        v.fail(f"cells of the footprint hold nothing: {empty}")


def check_size(v):
    """The drum against the issue's figures (about 1.5 blocks across and two long): measured from the turning
    parts at rest, printed; it fails only if the drum is under a block and a quarter across or a block and a half
    long."""
    m = v.m
    drum = [e for pid in ("drum", "screens") for e in v.posed(pid, 0.0) if not re.search(r"trunnion|journal", e.name)]
    lo, hi = aabb_of(drum)
    across = (hi[2] - lo[2]) / m.B
    heads = (m.E_HEAD_X[1] - m.W_HEAD_X[0]) / m.B
    with_gear = (m.GIRTH_X[1] - m.W_HEAD_X[0]) / m.B
    print(f"size: the drum {across:.2f} blocks across over its heads, {heads:.2f} long between the heads' outer faces "
          f"({with_gear:.2f} with the girth wheel), the screens {(m.SCREEN_X[1] - m.SCREEN_X[0]) / m.B:.2f} long; the issue's "
          f"about 1.5 across and 2 long")
    if across < 1.25 or heads < 1.5:
        v.fail("the drum is far smaller than the issue's")


# ---------------------------------------------------------------- the build: nothing floating
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
    """Each group's elements (`groups`: [(name, elements)], in fitting order) joined to the ground through what is
    already there and itself: {name: the names of its elements that float}. (The eidolon gantry's rule.)"""
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
    """Every part but the frame is a fitted part needing its own id (the pipes their metal); the frame needs
    nothing. Nothing floats: at rest the frame, then each fitted part in FIT_ORDER, joined to the ground
    through what is already there. (The build stages themselves are to be settled with the owner.)"""
    m = v.m
    for p in v.parts:
        want = None if p["id"] == "frame" else p["id"]
        if p["requires"] != want:
            v.fail(f"{p['id']} needs {p['requires']}, not {want}")
    order = [pid for pid in m.FIT_ORDER]
    missing = [pid for pid in v.drawn() if pid != "frame" and pid not in order]
    if missing:
        v.fail(f"parts not in FIT_ORDER: {missing}")
    groups = [("frame", v.posed("frame", 0.0))] + [(pid, v.posed(pid, 0.0)) for pid in order]
    loose = {k: n for k, n in stage_floating(groups).items() if n}
    print("build: frame (" + str(len(groups[0][1])) + ") > " + " > ".join(f"{k} ({len(e)})" for k, e in groups[1:])
          + f"; floating: {loose or 'nothing'}")
    if loose:
        v.fail(f"elements float: {loose}")


# ---------------------------------------------------------------- textures
ROLES = [
    (r"^fr_bed", {"stonebrick"}),
    (r"^fr_", {"iron"}),
    (r"^pinion_axle", {"axle"}),
    (r"^pinion_", {"steel"}),
    (r"^drum_plate", {"riveted"}),
    (r"^drum_", {"iron"}),
    (r"^girth_", {"iron"}),
    (r"^screens_coarse", {"perforated"}),
    (r"^screens_fine", {"wirecloth"}),
    (r"^screens_(bar|hoop)", {"iron"}),
    (r"^balls_", {"steel"}),
    (r"^(feedbox|hopper)_chute_", {"chute"}),
    (r"^feedbox_(spout|leg|rail)", {"iron"}),
    (r"^feedbox_", {"sheet"}),
    (r"^hopper_(foot|bracket)", {"iron"}),
    (r"^hopper_", {"sheet"}),
    (r"^pipecopper_", {"pipecopper", "solder"}),
]


def check_textures(v):
    bad = []
    for el in v.els:
        tex = {f["texture"].lstrip("#") for f in el.faces.values()}
        rule = next((want for rx, want in ROLES if re.search(rx, el.name)), None)
        if rule is None or not tex <= rule:
            bad.append((el.name, sorted(tex), sorted(rule or [])))
    print(f"textures by role: {len(v.els) - len(bad)} of {len(v.els)} elements as their role says (a granite bed, cast "
          f"iron for the castings, sheet iron for the feed box and hopper, steel for the pinion, its shaft and the balls, "
          f"riveted grinding plates, perforated plate and wire cloth for the screens, the game's chute, the axle's wood, "
          f"the pipe's metal and solder)")
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
    other anchor's; the power face is on a side; the axle's cross reaches the power face at its centre, on the
    pinion shaft's axis; the infeed and output chute sections reach their faces (check_chute_profile holds their
    walls); the water pipe's end is the water face's centre, on its axis; output.pos is the output chute's floor at
    the face."""
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
    lo, hi, k = cell_face_box(m, m.POWER_CELL, m.POWER_FACE)
    centre = [(lo[q] + hi[q]) / 2 for q in range(3)]
    axle = aabb_of(v.named("pinion", r"_axle"))
    mid = [(axle[0][q] + axle[1][q]) / 2 for q in range(3)]
    off = max(abs(mid[q] - centre[q]) for q in range(3) if q != k)
    on_axis = max(abs(mid[1] - m.PINION_C[0]), abs(mid[2] - m.PINION_C[1]))
    print(f"anchors: the axle's cross reaches x {axle[1][0]:.2f} (the power face at {lo[0]:.0f}), {off:.3f} off its centre, "
          f"{on_axis:.3f} off the pinion shaft's axis; {anchors}")
    if abs(axle[1][0] - lo[0]) > 0.02 or off > 1e-6 or on_axis > 1e-6:
        v.fail("the axle's cross does not meet the power face at its centre, on the pinion's shaft")
    for label, pid, rx, cell, face in (("infeed", "feedbox", r"_chute_", m.INFEED_CELL, m.INFEED_FACE),
                                       ("output", "hopper", r"_chute_", m.OUTPUT_CELL, m.OUTPUT_FACE)):
        flo, fhi, k = cell_face_box(m, cell, face)
        ch = aabb_of(v.named(pid, rx))
        end = ch[0][k] if face in ("north", "west", "down") else ch[1][k]
        inside = all(flo[q] - 1e-6 <= ch[0][q] and ch[1][q] <= fhi[q] + 1e-6 for q in range(3) if q != k)
        print(f"  {label}: the chute section ends at {end:.2f} (the face at {flo[k]:.0f}), inside the face's square: {inside}")
        if abs(end - flo[k]) > 1e-6 or not inside:
            v.fail(f"the {label} chute does not reach its face in its cell")
    pipe = aabb_of(v.named("pipecopper", r"_inlet_"))
    flo, fhi, k = cell_face_box(m, m.WATER_CELL, m.WATER_FACE)
    centre = [(flo[q] + fhi[q]) / 2 for q in range(3)]
    pc = [(pipe[0][q] + pipe[1][q]) / 2 for q in range(3)]
    end = pipe[0][k] if m.WATER_FACE in ("north", "west", "down") else pipe[1][k]
    across = max(abs(pc[q] - centre[q]) for q in range(3) if q != k)
    width = [pipe[1][q] - pipe[0][q] for q in range(3) if q != k]
    print(f"  water: the inlet pipe ends at {end:.2f} on the face's axis ({across:.3f} off its centre), {width} across (ppex's 6)")
    if abs(end - flo[k]) > 1e-6 or across > 1e-6 or any(abs(w - 2 * 3.0) > 1e-6 for w in width):
        v.fail("the inlet pipe does not meet the water face on the ppex pipe's axis")
    out = [p * m.B for p in rig["output"]["pos"]]
    flo, fhi, k = cell_face_box(m, m.OUTPUT_CELL, m.OUTPUT_FACE)
    floor = max(e.aabb()[1][1] for e in v.named("hopper", r"_chute_bottom$"))
    if abs(out[k] - flo[k]) > 1e-6 or abs(out[1] - floor) > 1e-6 or abs(out[0] - (flo[0] + fhi[0]) / 2) > 1e-6:
        v.fail(f"output.pos {out} is not the output chute's floor at the face")


def check_chute_profile(v):
    """The infeed and the output are drawn as the game's chute (the owner's rule for every chute a line meets): at
    each face the section is a placed chute's (chute-straight-we at the west face, chute-straight-ns at the south),
    wall for wall, in the cell's place, in the game's chute texture. The sections are made from VANILLA_CHUTE; when
    the game is installed (VINTAGE_STORY, else ~/Games/vintagestory) those numbers are held to the game's own shape
    file."""
    m = v.m
    for label, pid, side, cell in (("infeed", "feedbox", "we", m.INFEED_CELL), ("output", "hopper", "ns", m.OUTPUT_CELL)):
        a = 2 if side == "we" else 0
        o = [cell[k] * m.B for k in range(3)]
        want = m.chute_profile(side)
        got = []
        for e in v.named(pid, r"_chute_(north|south|bottom|top)$"):
            lo, hi = e.aabb()
            got.append((round(lo[a] - o[a], 4), round(lo[1] - o[1], 4), round(hi[a] - o[a], 4), round(hi[1] - o[1], 4)))
        got.sort()
        worst = max(abs(x - y) for g, w in zip(got, want) for x, y in zip(g, w)) if len(got) == len(want) else 1e9
        tex = {f["texture"] for e in v.named(pid, r"_chute_") for f in e.faces.values()}
        print(f"chute profile, {label}: its section {got}, the game's chute-straight-{side} {want}: worst {worst:.3f} voxels; "
              f"textures {sorted(tex)} ({m.TEXTURES['chute']})")
        if worst > 0.02 or tex != {"#chute"} or m.TEXTURES["chute"] != "game:block/metal/sheet/copper1":
            v.fail(f"the {label} is not the game's chute section at its face")
    roots = [os.environ.get("VINTAGE_STORY"), str(Path.home() / "Games" / "vintagestory")]
    found = next((Path(r) / m.VANILLA_CHUTE_SHAPE for r in roots if r and (Path(r) / m.VANILLA_CHUTE_SHAPE).is_file()), None)
    if found is None:
        print("  the game's chute shape not found: the constants are not compared with it")
        return

    def norm(els):
        return [(e["name"], e["from"], e["to"], {d: (f.get("uv"), f.get("rotation", 0), f.get("enabled", True)) for d, f in e["faces"].items()})
                for e in els]

    same = norm(json.loads(found.read_text())["elements"]) == norm(json.loads(m.VANILLA_CHUTE))
    print(f"  the game's chute shape ({found}): VANILLA_CHUTE is it: {same}")
    if not same:
        v.fail("VANILLA_CHUTE is not the game's chute shape")


# ---------------------------------------------------------------- the ore's way
def top_at(el, x=None, z=None):
    """The height of a board's top face over (x, z): its highest corner's plane, solved from its corners (the
    board is a box; its top is the face whose normal is most upward)."""
    corners = el.corners()
    up = max(range(3), key=lambda k: abs(el.r[1][k]))
    sgn = 1 if el.r[1][up] > 0 else -1
    face = [p for i, p in enumerate(corners) if ((i >> up) & 1) == (1 if sgn > 0 else 0)]
    # the plane through three of its corners
    p0, p1, p2 = face[0], face[1], face[2]
    n = [(p1[1] - p0[1]) * (p2[2] - p0[2]) - (p1[2] - p0[2]) * (p2[1] - p0[1]),
         (p1[2] - p0[2]) * (p2[0] - p0[0]) - (p1[0] - p0[0]) * (p2[2] - p0[2]),
         (p1[0] - p0[0]) * (p2[1] - p0[1]) - (p1[1] - p0[1]) * (p2[0] - p0[0])]
    x = p0[0] if x is None else x
    z = p0[2] if z is None else z
    return p0[1] - (n[0] * (x - p0[0]) + n[2] * (z - p0[2])) / n[1]


def check_flow(v):
    """The ore's way. In: the infeed chute's floor stands over the feed box's V floor where it ends, the V floor
    falls from the walls to the strip on the axis, the strip runs on as the spout through the east wall, falling
    into the trunnion's bore, every corner of it inside the bore with room to turn; the water pipe's bore is clear
    above the floor where it opens. Out: the drum's lowest point stands over the hopper's floor, the V sides stand
    outside the screens and the heads (under the axis), the wings fall from the end plates to the channel, the
    channel falls to the output chute's floor at its mouth, and the chute's floor runs level to the face."""
    m = v.m
    fb = {e.name: e for e in v.posed("feedbox", 0.0)}
    chute_floor = max(e.aabb()[1][1] for e in v.named("feedbox", r"_chute_bottom$"))
    chute_end = max(e.aabb()[1][0] for e in v.named("feedbox", r"_chute_bottom$"))
    zc = (m.INFEED_CELL[2] * m.B + 8.0)
    under = top_at(fb["feedbox_floor_n"], x=chute_end + 0.5, z=zc)
    hi_n, lo_n = top_at(fb["feedbox_floor_n"], z=m.FEED_Z[0] + m.FEED_WALL), top_at(fb["feedbox_floor_n"], z=m.STRIP_Z[0])
    hi_s, lo_s = top_at(fb["feedbox_floor_s"], z=m.FEED_Z[1] - m.FEED_WALL), top_at(fb["feedbox_floor_s"], z=m.STRIP_Z[1])
    strip = fb["feedbox_strip"].aabb()[1][1]
    s0 = top_at(fb["feedbox_spout_floor"], x=m.SPOUT_X[0])
    s1 = top_at(fb["feedbox_spout_floor"], x=m.SPOUT_X[1])
    print(f"flow, in: the chute's floor {chute_floor:.2f} over the V floor's {under:.2f} where it ends; the V floor "
          f"{hi_n:.2f} > {lo_n:.2f} (north), {hi_s:.2f} > {lo_s:.2f} (south) down to the strip at {strip:.2f}; the spout "
          f"from {s0:.2f} to {s1:.2f}")
    if not (chute_floor > under + 0.1 and hi_n > lo_n and hi_s > lo_s and abs(lo_n - strip) < 0.05 and abs(lo_s - strip) < 0.05
            and abs(s0 - strip) < 0.05 and s1 < s0 - 0.5):
        v.fail("the feed does not run down from the chute by the V floor and the spout")
    # the spout in the bore: every corner of its elements past the trunnion's mouth within the bore, with room
    r_bore = m.TRUNNION_R[0]
    worst = 0.0
    for e in v.named("feedbox", r"_spout_"):
        for p in e.corners():
            if p[0] > m.W_TRUNNION_X[0]:
                worst = max(worst, math.hypot(p[1] - m.DRUM_Y, p[2] - m.DRUM_Z))
    tip = max(p[0] for e in v.named("feedbox", r"_spout_floor") for p in e.corners())
    print(f"  the spout reaches x {tip:.2f} in the trunnion (its mouth at {m.W_TRUNNION_X[0]}, the head at {m.W_HEAD_X[0]}), at most "
          f"{worst:.2f} from the axis in it (the bore {r_bore})")
    if not (m.W_TRUNNION_X[0] + 1.0 < tip < m.W_HEAD_X[0]) or worst > r_bore - 0.3:
        v.fail("the spout does not run into the trunnion's bore clear of it")
    pipe_end = [e for e in v.named("pipecopper", r"_run_")]
    lo, hi = aabb_of(pipe_end)
    bore_bottom = lo[1] + 1.0
    floor_there = top_at(fb["feedbox_floor_s"], z=lo[2])
    print(f"  water: the pipe's open end at z {lo[2]:.2f} inside the south wall ({m.FEED_Z[1] - m.FEED_WALL}), its bore from y "
          f"{bore_bottom:.2f} over the floor's {floor_there:.2f} there")
    if lo[2] >= m.FEED_Z[1] - m.FEED_WALL or bore_bottom < floor_there + 0.3:
        v.fail("the water pipe does not open into the feed box above its floor")
    # out
    hp = {e.name: e for e in v.posed("hopper", 0.0)}
    low = 1e9
    for pose in cycle(m, 24):
        for pid in ("drum", "screens", "girth"):
            for e in v.posed(pid, pose):
                if m.HOP_END_X[0][1] < (e.aabb()[0][0] + e.aabb()[1][0]) / 2 < m.HOP_END_X[1][0]:
                    low = min(low, e.aabb()[0][1])
    wing = max(top_at(hp["hopper_floor_w"], x=m.HOP_END_X[0][1]), top_at(hp["hopper_floor_e"], x=m.HOP_END_X[1][0]))
    w_in, e_in = top_at(hp["hopper_floor_w"], x=m.CHANNEL_X[0]), top_at(hp["hopper_floor_e"], x=m.CHANNEL_X[1])
    c0 = top_at(hp["hopper_channel"], z=m.HOP_FOOT[0][1])
    c1 = top_at(hp["hopper_channel"], z=m.HOP_FOOT[1][0])
    floor_out = max(e.aabb()[1][1] for e in v.named("hopper", r"_chute_bottom$"))
    print(f"flow, out: the drum's lowest point over the hopper {low:.2f} (the floor's highest {wing:.2f}); the wings fall from "
          f"{wing:.2f} to {w_in:.2f} and {e_in:.2f} at the channel, the channel from {c0:.2f} to {c1:.2f} at the chute's mouth, its "
          f"floor {floor_out:.2f} to the face")
    if low < wing + 0.6 or not (wing > w_in and wing > e_in and w_in >= c0 - 0.05 and e_in >= c0 - 0.05 and c0 > c1
                                and abs(c1 - floor_out) < 0.05):
        v.fail("the pulp does not run down the hopper's floor to the output chute")
    # the V sides catch what the lower screens let through
    sides = v.named("hopper", r"_side_")
    near = 1e9
    for e in sides:
        for p in e.corners():
            pass
    for pose in cycle(m, 12):
        for pid in ("drum", "screens"):
            for e in v.posed(pid, pose):
                for s in sides:
                    if obb_obb(e, s, eps=-0.15):
                        near = min(near, 0.0)
    top_y = max(p[1] for e in sides for p in e.corners())
    print(f"  the V sides up to y {top_y:.2f} (the axis at {m.DRUM_Y}), at least 0.3 clear of the turning drum: {near > 0}")
    if near <= 0 or top_y > m.DRUM_Y:
        v.fail("the hopper's V sides do not stand clear under the screens")


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


def check_gearing(v):
    """The girth wheel and the pinion: the two pitch points move together (the rig's ratio is the teeth's, with the
    right sign), the ratio is the tooth counts', and over a whole turn of the pinion, every 2.5 degrees, no tooth
    runs into the other wheel by more than about 0.25 voxels while some tooth is always within 0.6 of it."""
    m = v.m
    label, (pa, ra), (pb, rb), pp, na, nb = ("the girth wheel", ("pinion", r"_gear_"), ("girth", r"_(tooth|rim)"),
                                             m.pitch_point(), m.PINION_T, m.GIRTH_T)
    va, vb = velocity(v, pa, pp), velocity(v, pb, pp)
    diff = max(abs(va[i] - vb[i]) for i in range(3))
    speed = math.sqrt(sum(x * x for x in va))
    wa, wb = omega(v, pa), omega(v, pb)
    span = 2 * math.pi / wa
    poses = [span * i / 144 for i in range(144)]
    clash, engaged = mesh_contact(v, (pa, ra), (pb, rb), poses)
    print(f"gearing, {label}: module {m.MODULE:.4f} on {m.CENTRES:.3f} centres; pitch points move together within {diff:.2e} "
          f"({speed:.3f} voxels a radian of the axle), {na}:{nb} teeth, turning {wa:.4f} and {wb:.4f} a radian of the axle "
          f"(ratio {wb / wa:.4f}, teeth {na / nb:.4f}); a turn of the pinion every 2.5 degrees: no tooth into the other wheel "
          f"{clash is None}{'' if clash is None else f' {clash}'}, always engaged {engaged}")
    if diff > 1e-3 or speed < 1e-3:
        v.fail(f"{label}: the pitch points do not move together")
    if abs(wb / wa - na / nb) > 1e-3:
        v.fail(f"{label}: the ratio is not the tooth counts'")
    if clash is not None or not engaged:
        v.fail(f"{label}: the teeth do not run together")


# ---------------------------------------------------------------- supports
def check_supports(v):
    """Every shaft carried: the drum's west trunnion and east journal each through a bearing closed round it on
    four sides; the pinion's shaft in its two pillow blocks, located by a collar outside each; the pinion and the
    girth wheel keyed on their shafts."""
    m = v.m
    th = m.TRUNNION_R[1]
    for i, ((x0, x1), (a, b), what) in enumerate(zip(m.BEARINGS_X, (m.W_TRUNNION_X, m.E_JOURNAL_X), ("west trunnion", "east journal")), 1):
        walls = {k: v.named("frame", rf"^fr_bearing{i}_{k}$")[0].aabb() for k in ("low", "cap", "n", "s")}
        closes = (abs(walls["low"][1][1] - (m.DRUM_Y - th)) < 1e-6 and abs(walls["cap"][0][1] - (m.DRUM_Y + th)) < 1e-6
                  and abs(walls["n"][1][2] - (m.DRUM_Z - th)) < 1e-6 and abs(walls["s"][0][2] - (m.DRUM_Z + th)) < 1e-6)
        through = a < x0 and b > x1
        print(f"supports: the drum's {what} (x {a}..{b}) through bearing {i} (x {x0}..{x1}): {through}, closed round it at {th}: {closes}")
        if not (closes and through):
            v.fail(f"the drum's {what} is not carried in bearing {i}")
    found, span = supports(v.named("frame", r"^fr_pillow\d_bearing$"), v.named("pinion", r"_rod"), 0, m.PINION_C)
    print(f"supports: the pinion's shaft (x {span}) in {sorted(found)}")
    if len(found) < 2:
        v.fail(f"the pinion's shaft is carried by {found}")
    collars = sorted(aabb_of(v.named("pinion", rf"_collar{i}"))[0][0] for i in (1, 2))
    pil = [aabb_of(v.named("frame", rf"^fr_pillow{i}_bearing$")) for i in (1, 2)]
    located = (abs(aabb_of(v.named("pinion", r"_collar1"))[1][0] - pil[0][0][0]) < 1e-6
               and abs(aabb_of(v.named("pinion", r"_collar2"))[0][0] - pil[1][1][0]) < 1e-6)
    gear = aabb_of(v.named("pinion", r"_gear_body"))
    rod = aabb_of(v.named("pinion", r"_rod"))
    hub = aabb_of(v.named("girth", r"_hub"))
    journal = aabb_of(v.named("drum", r"_ejournal"))
    keyed = rod[0][0] <= gear[0][0] and hub[0][0] >= journal[0][0] and hub[1][0] <= journal[1][0]
    print(f"supports: collars against the pillow blocks' outer faces {located} (at {collars}); the pinion on its shaft's end "
          f"and the girth wheel's hub on the journal {keyed}")
    if not (located and keyed):
        v.fail("the pinion's shaft is not located, or a wheel is not on its shaft")


# ---------------------------------------------------------------- clearances
ALLOWED = [
    # the drum's trunnion and journal in their bearings
    ("drum", r"_(wtrunnion|ejournal)", "frame", r"^fr_bearing"),
    # the charge lies on the grinding plates as they turn under it
    ("balls", None, "drum", r"_plate"),
    # the pinion's shaft in its pillow blocks
    ("pinion", r"_rod", "frame", r"^fr_pillow\d_bearing"),
    # meshing teeth (check_gearing holds them)
    ("pinion", r"_gear_", "girth", r"_(tooth|rim)"),
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
    pids = v.drawn()
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
    """The parts that pass close to each other keep a visible gap over the cycle: the screens and heads over the
    hopper, the heads by its end plates, the girth wheel and the pinion by its east end plate, the trunnion's mouth
    by the feed box, the spout in the bore, the girth wheel's hub by the east bearing."""
    m = v.m
    rows = []
    for label, (pa, ra), (pb, rb) in (("screens over the hopper", ("screens", r"."), ("hopper", r".")),
                                      ("heads by the hopper", ("drum", r"_[we]head"), ("hopper", r".")),
                                      ("girth wheel by the hopper", ("girth", r"."), ("hopper", r".")),
                                      ("pinion by the hopper", ("pinion", r"."), ("hopper", r".")),
                                      ("trunnion by the feed box", ("drum", r"_wtrunnion"), ("feedbox", r"_(e\d|n|s\d|lid|leg)")),
                                      ("spout in the bore", ("drum", r"_wtrunnion|_whead"), ("feedbox", r"_spout_")),
                                      ("girth wheel by the east bearing", ("girth", r"."), ("frame", r"^fr_bearing2")),
                                      ("pinion by the east pedestal", ("pinion", r"."), ("frame", r"^fr_(bearing2|pedestal2|sole2)"))):
        near = None
        for pose in cycle(m, 24):
            for a in v.named(pa, ra, pose):
                for b in v.named(pb, rb, pose):
                    if near is None and obb_obb(a, b, eps=-0.1):   # each grown by 0.1: 0.2 between faces
                        near = (a.name, b.name, round(pose, 3))
        rows.append((label, near))
    print("gaps: " + "; ".join(f"{label} at least 0.2 apart: {near is None}" for label, near in rows))
    for label, near in rows:
        if near:
            v.fail(f"{label}: closer than 0.2 voxels, {near}")


# ---------------------------------------------------------------- z-fighting
def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = [e for pid in v.drawn() for e in v.posed(pid, pose)]
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at theta {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses")
    if bad:
        v.fail("faces z-fight")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_chunky(v)
    check_size(v)
    check_build(v)
    check_textures(v)
    check_anchors(v)
    check_chute_profile(v)
    check_flow(v)
    check_supports(v)
    check_containment(v, cycle(m, 48))
    check_cells(v)
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
    lids = [c["pos"] for c in ship["cells"] if "lid" in c]
    hollow = [c["pos"] for c in ship["cells"] if c.get("hollow")]
    tops = sorted({tuple(c["pos"]) for c in ship["cells"] if c["pos"][1] == max(d["pos"][1] for d in ship["cells"])})
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, "
          f"hollow {hollow or 'none'}, lids on {len(lids)} (the top layer's {len(tops)})")
    if len(ship["cells"]) != len(m.footprint()) or hollow or sorted(tuple(p) for p in lids) != tops:
        print("FAIL the cells are not the footprint's, every one with boxes and the top layer lidded")
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
    return ok
