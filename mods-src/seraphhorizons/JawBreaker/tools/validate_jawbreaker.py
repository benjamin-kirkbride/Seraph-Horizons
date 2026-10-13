"""The jaw breaker generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its rule.
Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in make_shape.py
proves the shipped files are this model moved.

The stages add parts to the frame (make_shape.STAGES: jaws, eccentric, toggles, grizzly), so every check that sets
parts against each other is run per build state where it matters: what is fitted at that stage against the frame and
each other. The breaker's checks are the stamp mill v2's (origin/729-stampmill-model-v2), in the breaker's own frame.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, cells_touched, coplanar_faces, euler_round_trip, frame_floating, obb_obb
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
        self.requires = {p["id"]: p["requires"] for p in parts}
        self.cache = {}
        self.ok = True

    def fail(self, msg):
        self.ok = False
        print("FAIL", msg)

    def mat(self, pid, pose):
        return self.m.pm(self.parts, pid, pose[:1])

    def posed(self, pid, pose):
        key = (pid, tuple(pose[:1]))
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
    per_state = {s: sum(len(v.by_part[p]) for p in v.parts_in(s)) for s in v.m.BUILD_STATES}
    print(f"elements drawn per build state: {per_state}")
    if {p["requires"] for p in v.parts} != {None} | set(v.m.STAGES):
        v.fail("the parts' requires are not the frame's none and the stages")
    reads = sorted({driver_input(d) for p in v.parts for d in p["drivers"]})
    print(f"inputs read: {reads} (a Blake works either way round: theta alone, no travel)")
    if reads != ["theta"]:
        v.fail("a driver reads something other than the axle's angle")


def check_containment(v, poses):
    """Nothing leaves the machine's cells (its box and the flywheel's cells behind it): no element reaches into a
    cell that is not the machine's, at any of the poses."""
    m = v.m
    cells = set(m.footprint())
    box = (m.CELLS_X, m.CELLS_Y, 3)
    worst, where, outside = -1e9, None, []
    for pose in poses:
        for pid in v.by_part:
            for el in v.posed(pid, pose):
                lo, hi = el.aabb()
                o = box_overhang(lo, hi, box)
                if o > worst:
                    worst, where = o, (el.name, pose)
                stray = [c for c in cells_touched(lo, hi) if c not in cells]
                if stray:
                    outside.append((el.name, stray[0], pose))
    print(f"containment over {len(poses)} poses, {len(cells)} cells: worst overhang of the cells' bounds {worst:.3f} voxels ({where[0]}); "
          f"{len(outside)} elements reach into a cell that is not the machine's")
    if worst > 0.01 or outside:
        v.fail(f"leaves the machine's cells: {(where if worst > 0.01 else outside[0])}")


def check_floating(v):
    """The frame is one piece from the ground, and so is each build state at rest."""
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")
    for state in v.m.BUILD_STATES[1:]:
        els = [e for pid in v.parts_in(state) for e in v.posed(pid, v.m.REST)]
        seen, loose = frame_floating(els)
        print(f"build state {state}: {len(els)} elements, {len(loose)} not joined to the ground")
        if loose:
            v.fail(f"in {state} these float: {loose[:8]}")


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way round.
# Everything else must clear by more than 0.02 voxels at every sampled pose of its build state.
ALLOWED = [
    # the eccentric shaft in its bearings (the cheeks' posts and bosses, the pillow block), the flywheel's hub and the
    # collar against the bosses,
    # the bosses, the pitman's strap round the eccentric
    ("eshaft", r"_shaft|_fly_hub|_collar", "frame", r"fr_cheek_[we]_(post|ebear)|fr_ped_block"),
    ("eshaft", r"_eccentric|_shaft", "pitman", r"_strap"),
    # the linkage: the toggles in their seats and on the pitman's foot, the swinging jaw on its hinge pin, the rods
    # pinned to it and through the abutments to their nuts, the springs between the nuts and the abutments, each turn
    # meeting the next at a corner
    ("pitman", r"_foot", "backtoggle|fronttoggle", None), ("backtoggle", None, "frame", r"fr_backseat|fr_backwall"),
    ("fronttoggle", None, "swingjaw", r"_seat|_slab"), ("swingjaw", r"_boss|_web", "fixedjaw", r"_hingepin"),
    ("fixedjaw", r"_hingepin", "frame", r"fr_cheek_[we]_hbear"), ("fixedjaw", r"_plate", "frame", r"fr_frontwall"),
    ("rods", None, "swingjaw", r"_lug|_slab"), ("rods", r"rods_[we]_", "frame", r"fr_cheek_[we]_lug"),
    ("spring\\d", None, "rods", r"rods_nut"), ("spring\\d", None, "frame", r"fr_cheek_[we]_lug"), ("spring\\d", None, "spring\\d", None),
    # the grizzly on the head block, the cross bar and its brackets on the cheeks; the throat plate and its lip on the
    # front wall
    ("grizzly", r"_bar|_curb", "grizzly", r"_head|_crossbar"), ("grizzly", r"_curb", "grizzly", r"_bar"),
    ("grizzly", r"_head", "frame", r"fr_beam_head"), ("grizzly", r"_bracket", "frame", r"fr_cheek_[we]_front"),
    ("grizzly", r"_crossbar", "grizzly", r"_bracket"), ("grizzly", r"_lip", "grizzly", r"_throat"),
    ("grizzly", r"_throat|_lip", "frame", r"fr_frontwall|fr_cheek_[we]_front"), ("grizzly", r"_lip", "fixedjaw", r"_plate"),
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
                if (pa == pb and pa != "grizzly") or (only is not None and pa not in only and pb not in only):
                    continue
                if pa == pb and ea.name.split("_")[1][:4] == eb.name.split("_")[1][:4]:
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
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {p[0][0]:.3f} ({p[1]})" for (a, b), p in ex))
    return allhits


def clearance_poses(m):
    out = [(m.REST, s) for s in m.BUILD_STATES]
    for k in range(24):
        out.append((m.at_ecc(math.tau * k / 24), m.STAGES[-1]))
    return out


def check_clearances(v):
    hits = report_touches(v, "clearances", clearance_poses(v.m))
    if hits:
        v.fail("parts run into each other")


MOVING = ("eshaft", "pitman", "backtoggle", "fronttoggle", "swingjaw", "rods", "spring1", "spring2", "spring3", "spring4")


def check_swept(v):
    """The breaker through a whole turn of the eccentric every 2 degrees, everything fitted: nothing touches but the
    intended contacts."""
    m = v.m
    ps = [(m.at_ecc(math.tau * k / 180), m.STAGES[-1]) for k in range(180)]
    hits = report_touches(v, "swept paths", ps, set(MOVING))
    if hits:
        v.fail("something stands in a moving part's path")


TEX_RULES = [
    (r"^fr_chute(in|out)_", "chute"),
    (r"^(fr_(post|sill|cross|beam|tie|ped_post)|eshaft_stub)", "oak"),
    (r"^fr_hopper_", "sheet"),
    (r"^(fr_(cheek|backwall|frontwall|ped_block)|eshaft_(fly_(rim|arm|hub)|coupling)|pitman_|swingjaw_(slab|boss|web|lug)|grizzly_)", "iron"),
    (r"^(fr_backseat|fixedjaw_|eshaft_(shaft|eccentric|collar|fly_key)|backtoggle|fronttoggle|swingjaw_(face|seat)|rods_|spring\d)", "steel"),
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
    print(f"textures: {len(TEX_RULES)} rules (oak timber and the axle's stub, sheet iron for the hopper, iron castings and bars, "
          f"steel shafts, pins, springs and wearing parts, the game's chute): {len(bad)} break them, {len(unruled)} have no rule")
    if bad:
        v.fail(f"textures by role: {bad[:4]}")
    if unruled:
        v.fail(f"elements with no texture rule: {unruled[:6]}")


def on_face(cell, face, pos):
    axis, side = {"up": (1, 1), "down": (1, 0), "north": (2, 0), "south": (2, 1), "west": (0, 0), "east": (0, 1)}[face]
    inside = all(cell[k] * 16 - 1e-6 <= pos[k] <= cell[k] * 16 + 16 + 1e-6 for k in range(3))
    return inside and abs(pos[axis] - (cell[axis] * 16 + 16 * side)) < 1e-6


def check_anchors(v):
    """The eccentric shaft's stub meets the power face at the cell's centre; the infeed and output anchors on their
    cells' faces, at their sections' mouths; and the infeed and output in one column (no middle column)."""
    m, rig = v.m, v.rig
    stub = v.named("eshaft", r"_stub[ab]")
    lo, hi = aabb_of(stub)
    y, z = m.AXLE
    ok = hi[0] >= m.CELLS_X * 16 - 0.02 and (y % 16, z % 16) == (8.0, 8.0)
    ok = ok and (int(y // 16), int(z // 16)) == (m.POWER_CELL[1], m.POWER_CELL[2]) and m.POWER_CELL[0] == m.CELLS_X - 1
    ok = ok and abs((lo[1] + hi[1]) / 2 - y) < 1e-6 and abs((lo[2] + hi[2]) / 2 - z) < 1e-6
    print(f"anchors: power cell {m.POWER_CELL} face {m.POWER_FACE}, the axle's stub reaches x {hi[0]:.2f}, axis at (y {y}, z {z})")
    if not ok:
        v.fail("the eccentric shaft's stub does not meet the power face at the cell's centre")
    for name, cell, face in m.CELL_ANCHORS[1:]:
        pos = [c * 16 for c in rig[name]["pos"]]
        good = on_face(cell, face, pos)
        print(f"anchors: {name} at {[round(c, 2) for c in pos]}, on cell {cell}'s {face} face: {good}")
        if not good:
            v.fail(f"the {name} anchor is not on its cell's {face} face")
    for name, face_axis in (("chutein", 1), ("chuteout", 2)):
        walls = aabb_of(v.named("frame", rf"fr_{name}_"))
        key = "infeed" if name == "chutein" else "output"
        p = [c * 16 for c in rig[key]["pos"]]
        centre = [(walls[0][k] + walls[1][k]) / 2 for k in range(3)]
        at_face = walls[1][face_axis] if face_axis == 1 else walls[0][face_axis]
        good = all(abs(centre[k] - p[k]) < 1e-6 for k in range(3) if k != face_axis) and abs(at_face - p[face_axis]) < 1e-6
        print(f"anchors: the {key} anchor is on its section's axis, at its mouth: {good}")
        if not good:
            v.fail(f"the {key} anchor is not at its chute section's mouth")
    same = m.INFEED_CELL[0] == m.OUTPUT_CELL[0] and m.INFEED_CELL[2] == m.OUTPUT_CELL[2]
    print(f"anchors: both chutes in column x {m.INFEED_CELL[0]} (front row {m.INFEED_CELL[2]}): {same}")
    if not same:
        v.fail("the infeed and the output are not in one column")


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


def check_chute(v):
    """The infeed and the output are the game's chute section: VANILLA_CHUTE is the game's straight chute (when the game
    is installed: every wall's from, to, UVs, rotations and drawn faces), and at each anchor's face the section's
    profile is a vanilla chute's in that cell (walls 1 thick round a 6 x 6 bore, 4..12 in the block), flush with the
    face, in the chute's texture, so a vanilla chute placed against the face meets it with no gap or step."""
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
    # the profile at each face: a vanilla chute's walls in its cell, across the two axes other than the chute's
    # (the section, its cell, the axis it runs along, which of the game's (y, z) cross-section axes lands on each of
    # the other two: `ud` turns the game's y onto x; `ns` turns the game's z onto x)
    for name, cell, along, game in (("chutein", m.INFEED_CELL, 1, (1, 2)), ("chuteout", m.OUTPUT_CELL, 2, (2, 1))):
        o = m.chute_origin(cell)
        a, b = [k for k in range(3) if k != along]
        want = sorted((o[a] + lo[game[0]], o[a] + hi[game[0]], o[b] + lo[game[1]], o[b] + hi[game[1]]) for _, lo, hi, _ in m.VANILLA_CHUTE)
        walls = v.named("frame", rf"fr_{name}_")
        got, ends, tex = [], [], set()
        for e in walls:
            lo, hi = e.aabb()
            got.append((round(lo[a], 6), round(hi[a], 6), round(lo[b], 6), round(hi[b], 6)))
            ends.append((lo[along], hi[along]))
            tex |= {f["texture"] for f in e.faces.values()}
        face = o[along] + 16.0 if along == 1 else o[along]
        flush = all(abs((hi if along == 1 else lo) - face) < 1e-6 and abs(hi - lo - m.CHUTE_LEN) < 1e-6 for lo, hi in ends)
        ok = sorted(got) == want and flush and tex == {"#chute"} and m.TEXTURES["chute"] == "game:block/metal/sheet/copper1"
        print(f"chute: the {name} section's profile at its face is a vanilla chute's in its cell, flush, {m.CHUTE_LEN} long, in the chute's "
              f"texture: {ok}")
        if not ok:
            v.fail(f"the {name} section does not meet a vanilla chute: walls {sorted(got)}, want {want}, ends {ends}, textures {tex}")


def check_paths(v):
    """The ore's way through the machine, at rest (everything fitted): the infeed's stream lands on the grizzly
    between its curbs; the bars fall steeply enough to shed rock, their gaps let the fines through, their foot lies
    over the breaker's mouth; the throat plate sheds what falls on it past the front wall's face; the fines, the
    throat plate's and the breaker's discharge all fall into the hopper, whose floor falls steeply to the trough,
    which runs into the output section's bore."""
    m = v.m
    gap = (m.GRIZ_X[1] - m.GRIZ_X[0] - m.GRIZ_BARS * m.GRIZ_BAR_W) / (m.GRIZ_BARS - 1)
    bars = aabb_of(v.named("grizzly", r"_bar"))
    throat = aabb_of(v.named("grizzly", r"_throat"))
    curbs = [aabb_of(v.named("grizzly", rf"_curb{t}")) for t in "we"]
    bore = (m.INFEED_CELL[0] * 16 + 5.0, m.INFEED_CELL[0] * 16 + 11.0, m.INFEED_CELL[2] * 16 + 5.0, m.INFEED_CELL[2] * 16 + 11.0)
    mouth = (m.FIXED_Z, m.u_to_z(m.FACE_TOP[1]))
    disc = (m.FIXED_Z, m.u_to_z(m.FACE_BOT[1]) + 0.6)
    hopper = aabb_of(v.named("frame", r"fr_hopper_"))
    trough = aabb_of(v.named("frame", r"fr_hopper_trough"))
    out_bore = aabb_of(v.named("frame", r"fr_chuteout_bottom"))
    slope = math.degrees(math.atan2(m.HOPPER_TOP - m.LIP_Y, m.HOPPER_E - 12.0))
    checks = [
        (f"the infeed's stream (x {bore[0]:.1f}..{bore[1]:.1f}, z {bore[2]:.1f}..{bore[3]:.1f}) lands between the curbs (x {curbs[0][1][0]:.2f}.."
         f"{curbs[1][0][0]:.2f}) over the bars (z {bars[0][2]:.2f}..{bars[1][2]:.2f})",
         curbs[0][1][0] <= bore[0] + 1e-6 and bore[1] <= curbs[1][0][0] and bars[0][2] < bore[2] and bore[3] < bars[1][2]),
        (f"the grizzly's bars fall {math.degrees(m.GRIZ_SLOPE):.1f} degrees (at least 40)", math.degrees(m.GRIZ_SLOPE) >= 40.0 - 1e-9),
        (f"the bars stand {gap:.2f} apart (at least 1)", gap >= 1.0),
        (f"the bars' foot (z {bars[1][2]:.2f}) lies over the mouth (z {mouth[0]:.2f}..{mouth[1]:.2f}), their span x {m.GRIZ_X} within the "
         f"chamber {m.JAW_X}", mouth[0] < bars[1][2] < mouth[1] and m.JAW_X[0] <= m.GRIZ_X[0] and m.GRIZ_X[1] <= m.JAW_X[1]),
        (f"the throat plate falls {math.degrees(m.THROAT_SLOPE):.0f} degrees and ends at z {throat[0][2]:.2f}, past the front wall's face "
         f"(z {m.JAW_FRONT_Z:.2f})", m.THROAT_SLOPE >= math.radians(38.0) and throat[0][2] < m.JAW_FRONT_Z - 0.3),
        (f"the fines (z {bars[0][2]:.2f}..{m.JAW_FRONT_Z:.2f}, off the throat plate's edge at {throat[0][2]:.2f}) and the discharge "
         f"(z {disc[0]:.2f}..{disc[1]:.2f}) fall into the hopper (z {hopper[0][2]:.2f}..{hopper[1][2]:.2f}, x {hopper[0][0]:.2f}..{hopper[1][0]:.2f})",
         hopper[0][2] <= max(bars[0][2], bore[2]) and disc[1] < hopper[1][2] - 0.5 and hopper[0][0] <= m.JAW_X[0] - 0.5
         and m.JAW_X[1] < hopper[1][0]),
        (f"the hopper's floor falls {slope:.1f} degrees (at least 40) to the trough", slope >= 40.0),
        ("the trough's floor carries on the output section's", abs(trough[1][1] - out_bore[1][1]) < 1e-6 and abs(trough[0][2] - out_bore[1][2]) < 1e-6
         and abs(trough[0][0] - out_bore[0][0]) < 1e-6 and abs(trough[1][0] - out_bore[1][0]) < 1e-6),
        (f"the front cross sill (z {m.JAW_FRONT_Z:.2f}..{m.FIXED_Z:.2f}) lets the fines fall in front of it and the discharge behind it",
         throat[0][2] < m.JAW_FRONT_Z and disc[0] >= m.FIXED_Z - 1e-9),
    ]
    for label, good in checks:
        print(f"paths: {label}: {good}")
        if not good:
            v.fail(label)


def check_jaw(v):
    """The breaker's linkage stays joined through a whole turn of the eccentric (72 steps), in the build frame: the
    pitman's strap round the eccentric, its foot on both toggles' ends and where the solved linkage puts it, the back
    toggle on its seat, the front toggle on the swinging jaw's seat, the rods' pins in the jaw's lugs; the toggles
    never pass straight; the jaws never close to under 1; and the eccentric shaft turns with the axle, once a turn."""
    m = v.m
    x = m.JAW_XC

    def b(p):
        q = m.jaw_to_build(p)
        return [x, q[0], q[1]]
    worst, gaps = {}, []
    for k in range(72):
        phi = math.tau * k / 72
        pose = m.at_ecc(phi)
        s = m.linkage(phi)
        ecc = v.point("eshaft", pose, b((m.E_C[0] + m.ECC, m.E_C[1])))
        strap = v.point("pitman", pose, b(m.design_to_mean_pitman(m.E_C)))
        foot = v.point("pitman", pose, b(m.design_to_mean_pitman(m.P0)))
        bt_p = v.point("backtoggle", pose, b(m.design_to_mean_back(m.P0)))
        bt_b = v.point("backtoggle", pose, b(m.design_to_mean_back(m.B0)))
        ft_p = v.point("fronttoggle", pose, b(m.design_to_mean_front(m.P0)))
        ft_j = v.point("fronttoggle", pose, b(m.design_to_mean_front(m.J0)))
        jaw_j = v.point("swingjaw", pose, b(m.design_to_mean_jaw(m.J0)))
        jaw_k = v.point("swingjaw", pose, b(m.design_to_mean_jaw(m.K0)))
        rod_k = v.point("rods", pose, b(m.MEAN_K))
        pairs = {"strap on the eccentric": (strap, ecc), "foot on the back toggle": (foot, bt_p), "foot on the front toggle": (foot, ft_p),
                 "back toggle on its seat": (bt_b, b(m.B0)), "front toggle on the jaw's seat": (ft_j, jaw_j),
                 "rods on the jaw's lugs": (rod_k, jaw_k), "foot where the linkage puts it": (foot, b(s["p"]))}
        for label, (p, q) in pairs.items():
            worst[label] = max(worst.get(label, 0.0), math.dist(p[1:], q[1:]))
        sag = m.TOGGLE_Y - foot[1]
        if sag < 0.3:
            v.fail(f"the toggles pass straight at phi {phi:.2f} (sag {sag:.2f})")
        face_bot = v.point("swingjaw", pose, b(m.design_to_mean_jaw(m.FACE_BOT)))
        gaps.append(face_bot[2] - m.FIXED_Z)
    print("jaw: " + "; ".join(f"{k} within {d:.1e}" for k, d in worst.items()))
    if max(worst.values()) > 1e-3:
        v.fail("the jaw's linkage comes apart")
    print(f"jaw: the gap at the bottom between {min(gaps):.2f} and {max(gaps):.2f} voxels (a throw of {max(gaps) - min(gaps):.2f})")
    if min(gaps) < 1.0:
        v.fail("the jaws close too far")
    worst_turn = 0.0
    for th in (0.4, 1.9, -2.7, 6.1):
        mm = v.mat("eshaft", (th,))
        worst_turn = max(worst_turn, abs(math.remainder(math.atan2(mm[2][1], mm[1][1]) - th, math.tau)))
    print(f"drive: the eccentric shaft turns with the axle, once a turn either way (worst {worst_turn:.1e} rad)")
    if worst_turn > 1e-9:
        v.fail("the eccentric shaft does not turn with the axle")


def check_supports(v):
    m = v.m
    shafts = [
        ("eccentric shaft", "eshaft", r"_shaft", m.AXLE, "frame", r"^fr_(cheek_[we]_(post|ebear)|ped_block)$", 3),
        ("hinge pin", "fixedjaw", r"_hingepin", m.jaw_to_build(m.H0), "frame", r"^fr_cheek_[we]_hbear$", 2),
    ]
    for label, pid, rx, c, bpid, brx, need in shafts:
        els_ = v.named(pid, rx)
        carriers = [e for e in v.by_part[bpid] if re.search(brx, e.name)]
        found, _ = shaft_supports(carriers, els_, 0, c)
        print(f"support {label}: {len(found)} bearing(s) {', '.join(sorted(found)) or 'NONE'}")
        if len(found) < need:
            v.fail(f"the {label} is not carried by {need} bearings")
    hub = aabb_of(v.named("eshaft", r"_fly_hub"))
    collar = aabb_of(v.named("eshaft", r"_collar"))
    bosses = [aabb_of(v.named("frame", rf"fr_cheek_{t}_ebear")) for t in "we"]
    located = abs(hub[1][0] - bosses[0][0][0]) < 0.05 and abs(collar[0][0] - bosses[1][1][0]) < 0.05
    print(f"support: the flywheel's hub and the collar bear on the bearing bosses' outer faces, locating the shaft endwise: {located}")
    if not located:
        v.fail("the eccentric shaft is not located endwise by the flywheel's hub and its collar")


def check_flywheel(v):
    """The flywheel: one, about 2 blocks across, on the eccentric shaft on the side away from the axle, its rim's
    sweep (the whole circle, at the rim's faces) inside the machine's cells and above the ground, clear of the frame
    by the clearance and swept-path checks; its rim heavy (its depth and width) and its arms, hub and key there."""
    m = v.m
    y, z = m.AXLE
    cells = set(m.footprint())
    stray, low = [], 1e9
    for k in range(360):
        a = math.tau * k / 360
        for r in (m.FLY_R, m.FLY_R - m.FLY_RIM):
            py, pz = y + r * math.cos(a), z + r * math.sin(a)
            low = min(low, py)
            for px in m.FLY_X:
                c = (int(px // 16), int(py // 16), int(pz // 16))
                if c not in cells:
                    stray.append((round(px, 2), round(py, 2), round(pz, 2)))
    away = (m.FLY_X[1] < m.CHEEK_W[0]) == (m.POWER_FACE == "east")
    arms = len(v.named("eshaft", r"_fly_arm"))
    ok = not stray and low > 1.0 and away and 30.0 <= 2 * m.FLY_R <= 34.0 and arms == m.FLY_ARMS and v.named("eshaft", r"_fly_key")
    print(f"flywheel: {2 * m.FLY_R:.0f} across ({2 * m.FLY_R / 16:.2f} blocks), rim {m.FLY_RIM} deep and {m.FLY_X[1] - m.FLY_X[0]:.1f} wide, "
          f"{arms} arms, keyed; on the side away from the axle: {away}; its sweep {low:.1f} over the ground, "
          f"{len(stray)} points outside the machine's cells")
    if not ok:
        v.fail(f"the flywheel: outside the cells at {stray[:3]}, {low:.2f} over the ground, away from the axle {away}")


def check_springs(v):
    """The coil springs on the tension rods, through a turn (72 steps): each spring's end turns bear on its nut and
    its abutment lug (within 0.15 of their faces, along the rod), and its turns never close up on one another (the
    gap between one turn's bars and the next's, along the rod, stays over 0.1)."""
    m = v.m
    worst_end, worst_gap = 0.0, 1e9
    for x in m.ROD_X:
        coil = m.spring_coil(x)

        def drawn(c, p):
            """A turn's authored point (breaker frame (x, y, u)) in the build frame, as drawn (its share of the mean)."""
            sh = m.spring_share(c)
            return [p[0], p[1] + sh * (m.MEAN_K[0] - m.K0[0]), m.JAW_C - (p[2] + sh * (m.MEAN_K[1] - m.K0[1]))]
        nut = [x, m.MEAN_K[0], m.JAW_C - (m.ROD_NUT_U[1] + m.MEAN_K[1] - m.K0[1])]
        lug_z = m.JAW_C - m.ABUT_U
        for k in range(72):
            pose = m.at_ecc(math.tau * k / 72)
            starts = [v.point(f"spring{c + 1}", pose, drawn(c, coil.corner(4 * c))) for c in range(m.SPRING_TURNS)]
            end = v.point(f"spring{m.SPRING_TURNS}", pose, drawn(m.SPRING_TURNS - 1, coil.end(4 * m.SPRING_TURNS - 1)))
            nut_now = v.point("rods", pose, nut)
            worst_end = max(worst_end, abs(starts[0][2] - nut_now[2]) - coil.span(), abs(end[2] - lug_z) - coil.span())
            for a, b in zip(starts, starts[1:]):
                worst_gap = min(worst_gap, abs(a[2] - b[2]) - m.SPRING_WIRE / math.cos(coil.tilt))
    print(f"springs: {m.SPRING_TURNS} turns of {m.SPRING_WIRE} wire on each rod; their ends within {worst_end:.3f} of the nut and the "
          f"lug through a turn; the turns at least {worst_gap:.3f} apart")
    if worst_end > 0.15 or worst_gap < 0.1:
        v.fail("a spring leaves its nut or lug, or closes up solid")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        state = pose[1]
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
    poses = [m.REST] + [m.at_ecc(a) for a in (0.7, 1.6, 2.9, 4.0, 5.1)] + [(31.0,), (-13.0,)]
    check_containment(v, poses)
    check_anchors(v)
    check_paths(v)
    check_chute(v)
    check_textures(v)
    check_jaw(v)
    check_supports(v)
    check_flywheel(v)
    check_springs(v)
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
    hollow = [c["pos"] for c in ship["cells"] if c.get("hollow")]
    lids = [c["pos"] for c in ship["cells"] if "lid" in c]
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, hollow {hollow}; "
          f"lids {lids or 'none'}")
    if lids:
        print("FAIL a lid over the grizzly and the infeed would be an invisible floor")
        ok = False
    for c in ship["cells"]:
        for b in c.get("boxes", []):
            if not (len(b) == 6 and all(0 <= x <= 1 for x in b) and all(b[k] < b[k + 3] for k in range(3))):
                print(f"FAIL a box of cell {c['pos']} is not inside it: {b}")
                ok = False
    return ok
