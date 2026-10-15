"""The stamp battery generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its rule.
Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in make_shape.py
proves the shipped files are this model moved.

The machine is a set of fitted parts: the battery and its feed box, fitted into the frame. So every check that sets
parts against each other is run per build state (`make_shape.STATES`): the frame alone, and the battery's parts with
it.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
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
        self.requires = {p["id"]: p["requires"] for p in parts}
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

    def parts_in(self, state):
        return [pid for pid in self.by_part if self.m.in_state(self.requires[pid], state)]


def check_basic(v):
    m = v.m
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
    per_state = {s: sum(len(v.by_part[p]) for p in v.parts_in(s)) for s in m.STATES}
    print(f"elements drawn per state: {per_state}")
    want = {None} | set(m.BATTERY) | set(m.PIPE_REQUIRES)
    if {p["requires"] for p in v.parts} != want or list(m.STATES) != ["frame", "battery"]:
        v.fail("the parts' requires are not the battery's set and the water line's metals")


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
    """The frame is one piece from the ground, and so is each build state at rest."""
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")
    for state in v.m.STATES:
        els = [e for pid in v.parts_in(state) for e in v.posed(pid, v.m.REST)]
        seen, loose = frame_floating(els)
        print(f"build state {state}: {len(els)} elements, {len(loose)} not joined to the ground")
        if loose:
            v.fail(f"in {state} these float: {loose[:8]}")


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way round.
# Everything else must clear by more than 0.02 voxels at every sampled pose of its build state. Elements of one
# static part are one casting or one joinery and are not set against each other.
ALLOWED = [
    # the power train: the entry shaft into the gear case, the line shaft in its boss and pedestals, its pulley on it
    ("entry", None, "frame", r"fr_gearcase$"), ("line", None, "frame", r"fr_(gearcase|ped[wm]$)"),
    ("linepulley", None, "line", None),
    # the battery: the camshaft in its pillow blocks, the bull pulley's belt on it and on the line's pulley; the pillow
    # blocks on the cam rails; the stamps on their dies, their tappets on their cams, their stems in the guides
    ("camshaft", r"_shaft", "camframe", r"_pillow"), ("camframe", r"_pillow", "frame", r"fr_railcam_"),
    ("camframe", r"_belt", "camshaft", r"_bull_rim"), ("camframe", r"_belt", "linepulley", r"^linepulley_\d"),
    ("stamp\\d", r"_shoe", "mortar", r"_die"), ("stamp\\d", r"_tappet", "camshaft", r"_cam\d"),
    ("stamp\\d", r"_stem", "frame", r"fr_guide"),
    ("mortar", r"_block", "frame", r"fr_sill"), ("mortar", r"_chutein_", "frame", r"fr_tie_[cb]$"),
    # the feed box on the shoot and hung on the back bearer
    ("feedbox", r"_floor|_[we]$", "mortar", r"_shoot"), ("feedbox", r"_back", "frame", r"fr_bearer_b"),
    # the water line in its hanger
    ("pipecopper", r"_north_", "frame", r"fr_hanger"),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose, state, only=None):
    items = []
    for pid in v.parts_in(state):
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
                if pa == pb or (only is not None and pa not in only and pb not in only):
                    continue
                if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                    continue
                if allowed(pa, ea.name, pb, eb.name):
                    continue
                if obb_obb(ea, eb):
                    hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def report_touches(v, label, poses_states, only=None):
    allhits = {}
    for pose, state in poses_states:
        for key, hs in touching_pairs(v, pose, state, only).items():
            allhits.setdefault(key, {})
            for h in hs:
                allhits[key].setdefault(h, (pose, state))
    print(f"{label} over {len(poses_states)} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at psi {p[0][1]:.3f} ({p[1]})" for (a, b), p in ex))
    return allhits


def clearance_poses(m):
    out = [(m.REST, s) for s in m.STATES]
    for k in range(40):
        out.append((m.at_cam(math.pi * k / 40), "battery"))
    return out


def check_clearances(v):
    hits = report_touches(v, "clearances", clearance_poses(v.m))
    if hits:
        v.fail("parts run into each other")


def check_swept(v):
    """The battery through a whole camshaft turn every 0.5 degrees of it (each stamp lifted, thrown, falling and
    resting, its cams sweeping past and under its tappet), everything fitted: nothing touches but the intended
    contacts."""
    m = v.m
    ps = [(m.at_cam(math.tau * k / 720), "battery") for k in range(720)]
    only = {f"stamp{i + 1}" for i in range(m.STAMPS)} | {"camshaft", "line", "linepulley"}
    hits = report_touches(v, "swept paths", ps, only)
    if hits:
        v.fail("something stands in a moving part's path")


TEX_RULES = [
    (r"^mortar_(chute(in|out)_|funnel_)", "chute"),
    (r"^(fr_(sill|cap|post|rail|tie|guide|bearer)|entry_shaft[ab]|mortar_(block|chuteleg)|camshaft_bull_(rim|arm))", "oak"),
    (r"^feedbox_", "planks"),
    (r"^(fr_(gearcase|ped|hanger)|mortar_(floor|front|back|end|shoot)|camframe_pillow|camshaft_(hub|cam|bull_hub)|linepulley|"
     r"stamp\d_(head|tappet))", "iron"),
    (r"^(line_|camshaft_shaft|stamp\d_(shoe|stem)|mortar_die)", "steel"),
    (r"^pipe(copper|lead)_reducer_", "solder"),
    (r"^pipe(copper|lead)_cock\d", "bronze"),
    (r"^pipecopper_", "pipecopper"),
    (r"^pipelead_", "pipelead"),
    (r"^mortar_screen", "mesh"),
    (r"^camframe_belt", "leather"),
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
    print(f"textures: {len(TEX_RULES)} rules (oak timber, planks for the feed box, iron castings, steel shafts and wearing parts, the "
          f"water line's copper or lead, its reducer's solder and its cocks' bronze, the game's chute for both chutes and the funnel, a mesh screen, leather belt): "
          f"{len(bad)} break them, {len(unruled)} have no rule")
    if bad:
        v.fail(f"textures by role: {bad[:4]}")
    if unruled:
        v.fail(f"elements with no texture rule: {unruled[:6]}")


def on_face(cell, face, pos):
    axis, side = {"up": (1, 1), "down": (1, 0), "north": (2, 0), "south": (2, 1), "west": (0, 0), "east": (0, 1)}[face]
    inside = all(cell[k] * 16 - 1e-6 <= pos[k] <= cell[k] * 16 + 16 + 1e-6 for k in range(3))
    return inside and abs(pos[axis] - (cell[axis] * 16 + 16 * side)) < 1e-6


def check_anchors(v):
    """The entry shaft meets the power face at the cell's centre; every anchor's point on its cell's face; and each
    anchor at the thing it names: the water line's inlet, the infeed chute's mouth over the feed box, the output chute's
    mouth at the front face."""
    m, rig = v.m, v.rig
    entry = v.named("entry", r"_shaft[ab]")
    lo, hi = aabb_of(entry)
    ok = hi[0] >= m.CELLS_X * 16 - 0.02 and (m.ENTRY[0] % 16, m.ENTRY[1] % 16) == (8.0, 8.0)
    ok = ok and (int(m.ENTRY[0] // 16), int(m.ENTRY[1] // 16)) == (m.POWER_CELL[1], m.POWER_CELL[2]) and m.POWER_CELL[0] == m.CELLS_X - 1
    print(f"anchors: power cell {m.POWER_CELL} face {m.POWER_FACE}, the entry shaft reaches x {hi[0]:.2f}, axis at (y {m.ENTRY[0]}, z {m.ENTRY[1]})")
    if not ok:
        v.fail("the entry shaft does not meet the power face at the cell's centre")
    for name, cell, face in m.CELL_ANCHORS[1:]:
        pos = [c * 16 for c in rig[name]["pos"]]
        good = on_face(cell, face, pos)
        print(f"anchors: {name} at {[round(c, 2) for c in pos]}, on cell {cell}'s {face} face: {good}")
        if not good:
            v.fail(f"the {name} anchor is not on its cell's {face} face")
    inlet = aabb_of(v.named("pipecopper", r"_inlet_"))
    water = [c * 16 for c in rig["water"]["pos"]]
    infeed = [c * 16 for c in rig["infeed"]["pos"]]
    output = [c * 16 for c in rig["output"]["pos"]]
    cin = aabb_of(v.named("mortar", r"_chutein_"))
    cout = aabb_of(v.named("mortar", r"_chuteout_"))
    box = aabb_of(v.by_part["feedbox"])
    floor = aabb_of(v.named("feedbox", r"_floor"))
    sides = aabb_of(v.named("feedbox", r"_[we]$"))
    o = m.chute_origin(m.INFEED_CELL)
    bore = ((o[0] + 5.0, o[0] + 11.0), (o[2] + 5.0, o[2] + 11.0))
    checks = [("the water line's inlet reaches the water face, on the anchor's axis", abs(inlet[0][0] - water[0]) < 0.01
               and abs((inlet[0][1] + inlet[1][1]) / 2 - water[1]) < 0.01 and abs((inlet[0][2] + inlet[1][2]) / 2 - water[2]) < 0.01),
              ("the infeed anchor is on the infeed chute's axis, at its mouth",
               abs((cin[0][0] + cin[1][0]) / 2 - infeed[0]) < 1e-6 and abs((cin[0][2] + cin[1][2]) / 2 - infeed[2]) < 1e-6
               and abs(cin[1][1] - infeed[1]) < 1e-6),
              ("the infeed chute's end is over the feed box, its bore inside the box's sides and over its floor",
               cin[0][1] >= box[1][1] and sides[0][0] + 1.0 <= bore[0][0] and bore[0][1] <= sides[1][0] - 1.0
               and floor[0][2] <= bore[1][0] and bore[1][1] <= floor[1][2]),
              ("the output anchor is on the output chute's axis, at its mouth, flush with the front face",
               abs((cout[0][0] + cout[1][0]) / 2 - output[0]) < 1e-6 and abs((cout[0][1] + cout[1][1]) / 2 - output[1]) < 1e-6
               and abs(cout[0][2] - output[2]) < 1e-6)]
    for label, good in checks:
        print(f"anchors: {label}: {good}")
        if not good:
            v.fail(label)


def game_chute():
    """The game's straight chute (shapes/block/metal/chute/straight.json) when the game is installed, else None."""
    import json
    import os
    from pathlib import Path
    for root in (os.environ.get("VINTAGE_STORY"), str(Path.home() / "Games" / "vintagestory")):
        if root:
            f = Path(root) / "assets" / "survival" / "shapes" / "block" / "metal" / "chute" / "straight.json"
            if f.exists():
                return json.loads(f.read_text())
    return None


def chute_profile(v, rx, axis, at):
    """The section's walls across the chute (the two other axes' spans, rounded), the walls' end along it nearest `at`,
    and the textures they wear."""
    walls = v.named("mortar", rx)
    u, w = [k for k in range(3) if k != axis]
    got, ends, tex = [], [], set()
    for e in walls:
        lo, hi = e.aabb()
        got.append((round(lo[u], 6), round(hi[u], 6), round(lo[w], 6), round(hi[w], 6)))
        ends.append(min(abs(lo[axis] - at), abs(hi[axis] - at)))
        tex |= {f["texture"] for f in e.faces.values()}
    return sorted(got), ends, tex


def check_chute(v):
    """Both chutes are the game's chute section: VANILLA_CHUTE is the game's straight chute (when the game is installed:
    every wall's from, to, UVs, rotations and drawn faces); at the infeed cell's top face the section's profile is a
    vanilla `chute-straight-ud`'s in that cell, and at the output cell's north face a `chute-straight-ns`'s (walls 1
    thick round a 6 x 6 bore, 4..12 in the block), each flush with its face, in the chute's texture, so a vanilla chute
    placed there meets it with no gap or step."""
    m = v.m
    data = game_chute()
    if data is None:
        print("chute: the game is not installed here; VANILLA_CHUTE not compared with its file")
    else:
        got = []
        for e in data["elements"]:
            faces = {d: (tuple(f["uv"]), f.get("rotation", 0)) for d, f in e["faces"].items() if f.get("enabled", True)}
            got.append((e["name"], tuple(e["from"]), tuple(e["to"]), faces))
        want = [(n, tuple(lo), tuple(hi), {d: (tuple(uv), r) for d, (uv, r) in fs.items()}) for n, lo, hi, fs in m.VANILLA_CHUTE]
        same = got == want and data.get("textures") == {"copper1": "block/metal/sheet/copper1"}
        print(f"chute: VANILLA_CHUTE is the game's straight chute (from, to, UVs, rotations, drawn faces, texture): {same}")
        if not same:
            v.fail("VANILLA_CHUTE is not the game's straight chute")
    # across the chute, (u, w) the two other axes in order: the game's North and South walls stay whole across the
    # bore's other side (z walls of the ud chute, x walls of the ns chute), Bottom and Top between them
    for label, rx, cell, axis, face_at, whole in (("infeed (ud) at the top face", r"_chutein_", m.INFEED_CELL, 1, (m.INFEED_CELL[1] + 1) * 16, 1),
                                                  ("output (ns) at the north face", r"_chuteout_", m.OUTPUT_CELL, 2, m.OUTPUT_CELL[2] * 16, 0)):
        o = m.chute_origin(cell)
        u, w = [k for k in range(3) if k != axis]
        if whole == 1:
            want = sorted([(o[u] + 4.0, o[u] + 12.0, o[w] + 4.0, o[w] + 5.0), (o[u] + 4.0, o[u] + 12.0, o[w] + 11.0, o[w] + 12.0),
                           (o[u] + 4.0, o[u] + 5.0, o[w] + 5.0, o[w] + 11.0), (o[u] + 11.0, o[u] + 12.0, o[w] + 5.0, o[w] + 11.0)])
        else:
            want = sorted([(o[u] + 4.0, o[u] + 5.0, o[w] + 4.0, o[w] + 12.0), (o[u] + 11.0, o[u] + 12.0, o[w] + 4.0, o[w] + 12.0),
                           (o[u] + 5.0, o[u] + 11.0, o[w] + 4.0, o[w] + 5.0), (o[u] + 5.0, o[u] + 11.0, o[w] + 11.0, o[w] + 12.0)])
        got, ends, tex = chute_profile(v, rx, axis, face_at)
        ok = got == want and all(e < 1e-6 for e in ends) and tex == {"#chute"} and m.TEXTURES["chute"] == "game:block/metal/sheet/copper1"
        print(f"chute: the {label}: its profile a vanilla chute's in its cell, flush, in the chute's texture: {ok}")
        if not ok:
            v.fail(f"the {label} does not meet a vanilla chute: walls {got}, want {want}, ends {ends}, textures {tex}")


def check_paths(v):
    """The ore's way through the machine, at rest: the infeed chute drops into the feed box, whose floor falls steeply
    enough onto the shoot; the shoot falls from under the feed box's floor's lower end to the feed lip; the output
    funnel's mouth spans the whole screen, its back against the mortar's front under it, and its floor is level with the
    output stub's and runs into it."""
    m = v.m
    screen = aabb_of(v.named("mortar", r"_screen"))
    stub = aabb_of(v.named("mortar", r"_chuteout_bottom"))
    floors = [aabb_of([e]) for e in v.named("mortar", r"_funnel_floor")]
    shoot = aabb_of(v.named("mortar", r"_shoot$"))
    back = aabb_of(v.named("mortar", r"_back_lo"))
    box_floor = aabb_of(v.named("feedbox", r"_floor"))
    (ya, za), (yb, zb) = m.SHOOT_LO, m.SHOOT_HI
    shoot_deg = math.degrees(math.atan2(yb - ya, zb - za))
    (wa, wb), (ea, eb) = m.funnel_lines()
    checks = [
        (f"the feed box's floor falls {math.degrees(m.FEEDBOX_SLOPE):.0f} degrees (at least 35) onto the shoot",
         m.FEEDBOX_SLOPE >= math.radians(35.0) and abs(m.feedbox_y(m.FEEDBOX_Z[0]) - (m.SHOOT_HI[0] + 0.1)) < 1e-9),
        (f"the shoot falls {shoot_deg:.0f} degrees (at least 45) from under the feed box floor's lower end to the feed lip",
         shoot_deg >= 45.0 and za <= box_floor[0][2] <= zb + 0.5 and shoot[1][1] < box_floor[1][1] and abs(ya - back[1][1]) < 0.01),
        (f"the funnel's mouth spans the whole screen (x {screen[0][0]:.1f}..{screen[1][0]:.1f}) at the mortar's front face, under it",
         wb[0] <= screen[0][0] + 1e-9 and eb[0] >= screen[1][0] - 1e-9 and abs(wb[1] - m.MORTAR_Z[0]) < 1e-9
         and abs(eb[1] - m.MORTAR_Z[0]) < 1e-9 and m.FUNNEL_Y[0] + m.FUNNEL_T < screen[0][1]),
        (f"the funnel narrows to the stub's bore where they meet (x {wa[0]:.1f}..{ea[0]:.1f} at z {wa[1]:.1f})",
         abs(wa[1] - stub[1][2]) < 1e-6 and abs(ea[1] - stub[1][2]) < 1e-6 and abs(wa[0] - stub[0][0]) < 1e-6
         and abs(ea[0] - stub[1][0]) < 1e-6),
        (f"the funnel's floor (top y {max(f[1][1] for f in floors):.2f}) is level with the stub's (y {stub[1][1]:.2f}) and reaches "
         f"the mortar's front", all(abs(f[1][1] - stub[1][1]) < 0.05 for f in floors)
         and max(f[1][2] for f in floors) > m.MORTAR_Z[0]),
    ]
    for label, good in checks:
        print(f"paths: {label}: {good}")
        if not good:
            v.fail(label)


def check_water(v):
    """The water line's branches, at rest with everything fitted: one to each stamp, over its stem's line; each open
    at its end (nothing of the pipe across its bore under the cock), and that end inside the mortar's feed opening:
    over the lip and under the rim, within the opening's width, at or behind the lip's inner edge and behind the stamp's
    shoe; and from it to the lip nothing of any part in the water's way. The line is ppex's full pipe at the water face,
    the reducer, then the small pipe."""
    m = v.m
    els = [e for pid in v.parts_in("battery") for e in v.posed(pid, m.REST)]
    pipe = v.by_part["pipecopper"]
    inner = m.MORTAR_Z[1] - m.MORTAR_WALL
    for k, x in enumerate(m.branch_xs(), 1):
        lo, hi = aabb_of(v.named("pipecopper", rf"_spout{k}_"))
        cx, cz = (lo[0] + hi[0]) / 2, (lo[2] + hi[2]) / 2
        r = m.SMALL_HALF - m.SMALL_WALL - 0.05
        capped = [e.name for e in pipe if e.aabb()[0][1] < lo[1] + 0.5 and inside(e, (cx, lo[1] + 0.1, cz), -0.01)]
        pts = [(cx + r * a, y, cz + r * b) for a in (-1, 0, 1) for b in (-1, 0, 1)
               for y in [m.FEED_LIP_Y + 0.1 + (lo[1] - m.FEED_LIP_Y - 0.2) * t / 4 for t in range(5)]]
        hits = sorted({e.name for q in pts for e in els if inside(e, q)})
        shoe = m.STEM_Z + m.SHOE_R
        checks = [
            (f"branch {k}: over stamp {k}'s stem (x {cx:.2f}), its end open", abs(cx - x) < 1e-6 and not capped),
            (f"branch {k}: its end (y {lo[1]:.2f}, x {lo[0]:.2f}..{hi[0]:.2f}, z {lo[2]:.2f}..{hi[2]:.2f}) inside the feed opening, "
             f"over the lip, at or behind its inner edge (z {inner:.2f}), behind the shoe (z {shoe:.2f})",
             m.FEED_LIP_Y < lo[1] < m.MORTAR_Y[1] and m.FEED_X[0] <= lo[0] and hi[0] <= m.FEED_X[1]
             and inner <= cz <= m.MORTAR_Z[1] and lo[2] > shoe),
            (f"branch {k}: nothing between its end and the lip", not hits),
        ]
        for label, good in checks:
            print(f"water: {label}: {good}")
            if not good:
                v.fail(f"{label} {capped or hits}")
    conn = aabb_of(v.named("pipecopper", r"_inlet_"))
    red = aabb_of(v.named("pipecopper", r"_reducer_"))
    run = aabb_of(v.named("pipecopper", r"_run$"))
    good = (abs(conn[1][1] - conn[0][1] - 2 * m.PIPE_HALF) < 1e-6 and abs(red[0][0] - conn[1][0]) < 1e-6
            and abs(run[0][0] - conn[1][0]) < 1e-6 and abs(run[1][1] - run[0][1] - 2 * m.SMALL_HALF) < 1e-6)
    print(f"water: ppex's full pipe ({2 * m.PIPE_HALF:.1f} across) at the water face to x {conn[1][0]:.1f}, the reducer, then the small "
          f"pipe ({2 * m.SMALL_HALF:.1f} across): {good}")
    if not good:
        v.fail("the water line is not ppex's pipe at the face, the reducer, then the small pipe")


def inside(el, q, margin=0.0):
    """Whether the point q is inside the element's box (grown by margin)."""
    rel = [q[i] - el.c[i] for i in range(3)]
    return all(abs(sum(rel[i] * el.r[i][k] for i in range(3))) <= el.size[k] / 2 + margin for k in range(3))


def check_outlet_clear(v):
    """The pulp's path out is open, at rest with everything fitted: the space in the funnel from its floor up to the
    screen's top, from the mortar's front to the stub, and the stub's bore out to the front face, hold no element of any
    part (the funnel's walls and floor bound it; nothing stands in it or over its open top in front of the screen)."""
    m = v.m
    els = [e for pid in v.parts_in("battery") for e in v.posed(pid, m.REST)]
    (wa, wb), (ea, eb) = m.funnel_lines()
    y_lo, y_hi = m.FUNNEL_Y[0] + m.FUNNEL_T + 0.2, m.SCREEN_Y[1] - 0.2
    pts = []
    za, zb = m.FUNNEL_Z
    for k in range(25):
        z = za + 0.2 + (zb - za - 0.4) * k / 24
        f = (z - wa[1]) / (wb[1] - wa[1])
        x0, x1 = wa[0] + (wb[0] - wa[0]) * f + 0.3, ea[0] + (eb[0] - ea[0]) * f - 0.3
        for i in range(13):
            for j in range(9):
                pts.append((x0 + (x1 - x0) * i / 12, y_lo + (y_hi - y_lo) * j / 8, z))
    cx0, cx1 = m.chute_bore_x()
    for k in range(6):
        for i in range(5):
            for j in range(5):
                pts.append((cx0 + 0.2 + (cx1 - cx0 - 0.4) * i / 4, 5.2 + 5.6 * j / 4, 0.2 + (za - 0.2) * k / 5))
    hits = sorted({e.name for q in pts for e in els if inside(e, q)})
    print(f"outlet: {len(pts)} points of the funnel's space (to the screen's top) and the stub's bore: {len(hits)} elements in it {hits[:6]}")
    if hits:
        v.fail(f"something stands in the pulp's path out: {hits[:6]}")


# ---------------------------------------------------------------- mechanism
def angle_x(v, pid, pose):
    mm = v.mat(pid, pose)
    return math.atan2(mm[2][1], mm[1][1])


def check_drive(v):
    """The rectifier's gear case turns the line shaft one way whichever way the axle turns; the bull belt's two pulleys
    move their rims alike (an open belt: the same sense) and its runs are tangent to both rims."""
    m = v.m
    for th in (0.7, -0.7):
        a, b = (0.0, 0.0), (th, abs(th))
        ls = angle_x(v, "line", b) - angle_x(v, "line", a)
        if ls * m.LINE_RATIO <= 0:
            v.fail(f"the line shaft turns {ls:+.3f} for the axle's {th:+}")
    print("rectifier: the line shaft turns the same way for either sign of the axle")
    worst = 0.0
    for psi in (0.9, 3.7, 12.2, 40.1):
        for pid in v.by_part:
            a, b = v.mat(pid, (psi, psi)), v.mat(pid, (-psi, psi))
            d = max(abs(a[r][c] - b[r][c]) for r in range(3) for c in range(4))
            if pid != "entry":
                worst = max(worst, d)
    print(f"reverse: with the axle turning backwards every part but the entry shaft moves as forwards (worst {worst:.1e})")
    if worst > 1e-12:
        v.fail("a part behind the rectifier depends on the axle's direction")
    label, pa, ca, ra, pb, cb, rb, bpart, brx = ("bull belt", "linepulley", m.LINE, m.LINE_PULLEY_R, "camshaft", m.CAM_C, m.BULL_R,
                                                 "camframe", r"camframe_belt")
    worst = 0.0
    for s in (0.2, 3.1, 7.7):
        s1, s2 = m.turning(s), m.turning(s + 0.01)
        da = math.remainder(angle_x(v, pa, s2) - angle_x(v, pa, s1), math.tau) * ra
        db = math.remainder(angle_x(v, pb, s2) - angle_x(v, pb, s1), math.tau) * rb
        worst = max(worst, abs(da - db))
    runs = v.named(bpart, brx + r"_run")
    tang = 0.0
    for run in runs:
        k = max(range(3), key=lambda i: run.size[i])        # the run's centre line: its long axis
        ax = [run.r[i][k] for i in range(3)]
        half = run.size[k] / 2
        for sgn in (1, -1):
            e = [run.c[i] + sgn * half * ax[i] for i in range(3)]
            for cc, rr in ((ca, ra), (cb, rb)):
                d = math.hypot(e[1] - cc[0], e[2] - cc[1])
                if abs(d - (rr + m.BELT_T / 2)) < 0.5:
                    radial = ((e[1] - cc[0]) / d, (e[2] - cc[1]) / d)
                    tang = max(tang, abs(d - (rr + m.BELT_T / 2)), abs(radial[0] * ax[1] + radial[1] * ax[2]))
    print(f"belts: the {label}'s rims move alike (worst {worst:.1e}); its runs tangent to both rims (worst {tang:.1e})")
    if worst > 1e-6 or tang > 1e-6 or len(runs) != 2:
        v.fail(f"the {label} slips or leaves its pulleys")


def tappet_gap(v, i, pose):
    """How far stamp i's tappet's underside stands over the highest point of its cam under it (negative: into it)."""
    tap = v.named(f"stamp{i + 1}", r"_tappet", pose)[0]
    lo, hi = tap.aabb()
    best = -1e9
    for e in v.named("camshaft", rf"_cam{i + 1}[ab]", pose):
        cs = e.corners()
        for q in cs:
            if lo[0] - 1e-6 <= q[0] <= hi[0] + 1e-6 and lo[2] - 1e-6 <= q[2] <= hi[2] + 1e-6:
                best = max(best, q[1])
        for a in range(8):
            for b in range(8):
                p, q = cs[a], cs[b]
                if (p[2] - lo[2]) * (q[2] - lo[2]) < 0:
                    t = (lo[2] - p[2]) / (q[2] - p[2])
                    best = max(best, p[1] + t * (q[1] - p[1]))
    return lo[1] - best


def stamp_lift_drawn(v, i, pose):
    """Stamp i's shoe over its die (voxels) at the pose."""
    return v.mat(f"stamp{i + 1}", pose)[1][3] * 16 + v.m.DRAWN_LIFT


def check_stamps(v):
    """Each stamp: its tappet on its cam through every lift (never into it, at most 0.15 over it); resting on its die
    between drops (at most 0.12 over it, never into it); two drops a camshaft turn, in the firing order."""
    m = v.m
    worst_over, worst_in, n = 0.0, 0.0, 0
    tr = m.LIFT / m.CAM_RB
    for i in range(m.STAMPS):
        for lobe in (0, 1):
            for f in (0.06, 0.2, 0.4, 0.6, 0.8, 0.95):
                pose = m.at_cam(m.stamp_phase(i) + math.pi * lobe + f * tr)
                g = tappet_gap(v, i, pose)
                n += 1
                worst_over, worst_in = max(worst_over, g), min(worst_in, g)
    print(f"stamps: over {n} poses in the lifts the tappets ride their cams, at most {worst_over:.3f} over and {-worst_in:.3f} into them")
    if worst_over > 0.15 or worst_in < -0.01:
        v.fail("a tappet does not ride its cam through the lift")
    worst_hi, worst_lo = 0.0, 0.0
    for i in range(m.STAMPS):
        die = aabb_of(v.named("mortar", rf"_die{i + 1}_"))
        for k in range(24):
            x = tr + m.FALL + 0.12 + (math.pi - tr - m.FALL - 0.12) * k / 23
            pose = m.at_cam(m.stamp_phase(i) + x)
            shoe = aabb_of(v.named(f"stamp{i + 1}", r"_shoe", pose))
            d = shoe[0][1] - die[1][1]
            worst_hi, worst_lo = max(worst_hi, d), min(worst_lo, d)
    print(f"stamps: resting, each shoe stands {worst_lo:.3f}..{worst_hi:.3f} over its die")
    if worst_hi > 0.12 or worst_lo < -0.01:
        v.fail("a stamp does not rest on its die between drops")
    drops = []
    mid = m.LIFT / 2
    for i in range(m.STAMPS):
        prev = None
        for s in range(721):
            phi = math.tau * s / 720
            y = stamp_lift_drawn(v, i, m.at_cam(phi))
            if prev is not None and prev >= mid > y:
                drops.append((phi, i + 1))
            prev = y
    drops.sort()
    per = {i + 1: sum(1 for _, s in drops if s == i + 1) for i in range(m.STAMPS)}
    first = next(k for k, (_, s) in enumerate(drops) if s == 1)
    order = [s for _, s in (drops[first:] + drops[:first])[:5]]
    print(f"stamps: drops a camshaft turn {per}, firing order {order}")
    if any(c != 2 for c in per.values()) or order != list(m.ORDER):
        v.fail("the stamps do not drop twice a camshaft turn each, in the firing order")


def check_supports(v):
    m = v.m
    shafts = [
        ("entry shaft", "entry", r"_shaft", 0, m.ENTRY, "frame", r"^fr_gearcase$", 1),
        ("line shaft", "line", r"_shaft", 0, m.LINE, "frame", r"^fr_(gearcase_boss|pedw|pedm)$", 3),
        ("camshaft", "camshaft", r"_shaft", 0, m.CAM_C, "camframe", r"_pillow", 2),
    ]
    for label, pid, rx, axis, c, bpid, brx, need in shafts:
        els_ = v.named(pid, rx)
        carriers = [e for e in v.by_part[bpid] if re.search(brx, e.name)]
        found, _ = shaft_supports(carriers, els_, axis, c)
        print(f"support {label}: {len(found)} bearing(s) {', '.join(sorted(found)) or 'NONE'}")
        if len(found) < need:
            v.fail(f"the {label} is not carried by {need} bearings")
    print("support: the entry shaft is the vanilla axle's end, carried by the axle beyond the face and the gear case")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        state = pose[2]
        els = [e for pid in v.parts_in(state) for e in v.posed(pid, pose)]
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
    check_floating(v)
    poses = [m.REST] + [m.at_cam(a) for a in (0.3, 1.2, 2.2, 3.0, 4.1, 5.0, 5.9)] + [m.turning(31.0, -1.0)]
    check_containment(v, poses)
    check_anchors(v)
    check_paths(v)
    check_outlet_clear(v)
    check_water(v)
    check_chute(v)
    check_textures(v)
    check_drive(v)
    check_stamps(v)
    check_supports(v)
    check_clearances(v)
    if not quick:
        check_swept(v)
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
    if gaps or len(ship["cells"]) != m.CELLS_X * m.CELLS_Y * m.CELLS_Z:
        print(f"FAIL lids {gaps}, or not every cell of the box")
        ok = False
    for c in ship["cells"]:
        for b in c.get("boxes", []):
            if not (len(b) == 6 and all(0 <= x <= 1 for x in b) and all(b[k] < b[k + 3] for k in range(3))):
                print(f"FAIL a box of cell {c['pos']} is not inside it: {b}")
                ok = False
    return ok
