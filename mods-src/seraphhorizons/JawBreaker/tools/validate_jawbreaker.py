"""The stamp mill generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its rule.
Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in make_shape.py
proves the shipped files are this model moved.

The levels add parts: level 1 fits the battery into the frame, level 2 adds the breaker and the grizzly to it. So
every check that sets parts against each other is run per build state (`make_shape.LEVELS`): level 1's against the
frame and each other, level 2's against all of it.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
from machinegen.checks import supports as shaft_supports
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import part_of, posed

STATES = ("level1", "level2")


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
    per_state = {s: sum(len(v.by_part[p]) for p in v.parts_in(s)) for s in ("frame",) + STATES}
    print(f"elements drawn per level: {per_state}")
    want = {None} | set(v.m.LEVELS["level2"]) | set(v.m.PIPE_REQUIRES)
    if {p["requires"] for p in v.parts} != want or not set(v.m.LEVELS["level1"]) <= set(v.m.LEVELS["level2"]):
        v.fail("the parts' requires are not the levels' sets, or level 2 does not keep level 1's")


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
    for state in ("frame",) + STATES:
        els = [e for pid in v.parts_in(state) for e in v.posed(pid, v.m.REST)]
        seen, loose = frame_floating(els)
        print(f"build state {state}: {len(els)} elements, {len(loose)} not joined to the ground")
        if loose:
            v.fail(f"in {state} these float: {loose[:8]}")


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way round.
# Everything else must clear by more than 0.02 voxels at every sampled pose of its build state.
ALLOWED = [
    # the power train: the entry shaft into the gear case, the line shaft in its boss and pedestals, its pulleys on it
    ("entry", None, "frame", r"fr_gearcase$"), ("line", None, "frame", r"fr_(gearcase|ped[wm]$)"),
    ("linepulley|jawpulley", None, "line", None),
    # level 1: the camshaft in its pillow blocks, the bull pulley's belt on it and on the line's pulley; the pillow
    # blocks on the cam rails; the stamps on their dies, their tappets on their cams, their stems in the guides
    ("camshaft", r"_shaft", "camframe", r"_pillow"), ("camframe", r"_pillow", "frame", r"fr_railcam_"),
    ("camframe", r"_belt", "camshaft", r"_bull_rim"), ("camframe", r"_belt", "linepulley", r"^linepulley_\d"),
    ("stamp\\d", r"_shoe", "mortar", r"_die"), ("stamp\\d", r"_tappet", "camshaft", r"_cam\d"),
    ("stamp\\d", r"_stem", "frame", r"fr_guide"),
    ("mortar", r"_block", "frame", r"fr_sill"), ("mortar", r"_apron", "mortar", None),
    # level 2: the breaker on its stand, the eccentric shaft in its bearings and the pitman's strap, the toggles in their
    # seats and on the pitman's foot, the swinging jaw on its hinge pin, the rods pinned to it, through the back wall to
    # their nuts, the buffers on the rods; its belt on its pulleys; the grizzly on the back tie and the cheeks
    ("jawframe", r"_base|_backwall|_frontwall|_post|_sill", "jawframe", r"_sill|_post"),
    ("eshaft", r"_shaft|_collar|_flyw_hub|_flye_hub", "jawframe", r"_ebearing"), ("eshaft", r"_eccentric|_shaft", "pitman", r"_strap"),
    ("pitman", r"_foot", "backtoggle|fronttoggle", None), ("backtoggle", None, "jawframe", r"_backseat|_backwall"),
    ("fronttoggle", None, "swingjaw", r"_seat|_slab"), ("swingjaw", r"_boss|_web", "jawframe", r"_hingepin|_hbearing"),
    ("rods", None, "swingjaw", r"_lug|_slab"), ("rods", r"rods_[we]_", "jawframe", r"_backwall"),
    ("rods", None, "buffer\\d", None),
    ("jawframe", r"_belt", "eshaft", r"_flye_rim"), ("jawframe", r"_belt", "jawpulley", r"^jawpulley_\d"),
    ("jawframe", r"_hingepin", "jawframe", r"_hbearing"),
    ("grizzly", r"_head", "frame", r"fr_tie_b"), ("grizzly", r"_bar", "grizzly", r"_head|_crossbar"),
    ("grizzly", r"_bracket", "jawframe", r"_side_|_hpost_|_hbearing"), ("grizzly", r"_throat|_lip", "jawframe", r"_frontwall|_fixedjaw|_side"),
    ("grizzly", r"_crossbar", "grizzly", r"_bracket"), ("grizzly", r"_lip", "grizzly", r"_throat"),
    ("grizzly", r"_chute_", "grizzly", r"_chute_"),
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
                if (pa == pb and pa not in ("mortar", "grizzly")) or (only is not None and pa not in only and pb not in only):
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
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at psi {p[0][1]:.3f} ({p[1]})" for (a, b), p in ex))
    return allhits


def clearance_poses(m):
    out = [(m.REST, s) for s in ("frame",) + STATES]
    for k in range(40):
        out.append((m.at_cam(math.pi * k / 40), "level1"))
    for k in range(24):
        out.append((m.at_ecc(math.tau * k / 24), "level2"))
    return out


def check_clearances(v):
    hits = report_touches(v, "clearances", clearance_poses(v.m))
    if hits:
        v.fail("parts run into each other")


def check_swept(v):
    """The battery through a whole camshaft turn every 0.5 degrees of it (each stamp lifted, thrown, falling and
    resting, its cams sweeping past and under its tappet), and the breaker through a whole turn of the eccentric
    every 2 degrees, everything fitted: nothing touches but the intended contacts."""
    m = v.m
    ps = [(m.at_cam(math.tau * k / 720), "level2") for k in range(720)]
    ps += [(m.at_ecc(math.tau * k / 180), "level2") for k in range(180)]
    only = {f"stamp{i + 1}" for i in range(m.STAMPS)} | {"camshaft", "line", "linepulley", "jawpulley", "eshaft", "pitman", "backtoggle",
                                                         "fronttoggle", "swingjaw", "rods"} | {f"buffer{c + 1}" for c in range(m.BUFFERS)}
    hits = report_touches(v, "swept paths", ps, only)
    if hits:
        v.fail("something stands in a moving part's path")


TEX_RULES = [
    (r"^grizzly_chute_", "chute"),
    (r"^(fr_(sill|cap|post|rail|tie|guide)|entry_shaft[ab]|mortar_block|camshaft_bull_(rim|arm)|jawframe_(sill|post))", "oak"),
    (r"^(mortar_(launder|spout|feed))", "planks"),
    (r"^(fr_(gearcase|ped|waterbracket)|mortar_(floor|front|back|end|apron)|camframe_pillow|camshaft_(hub|cam|bull_hub)|linepulley|jawpulley|"
     r"stamp\d_(head|tappet)|jawframe_(base|backpost|epost|ebearing|hpost|hbearing|side|backwall|frontwall)|eshaft_fly|pitman_|"
     r"swingjaw_(slab|boss|web|lug)|grizzly_)", "iron"),
    (r"^(line_|camshaft_shaft|stamp\d_(shoe|stem)|mortar_die|jawframe_(backseat|fixedjaw|hingepin)|eshaft_(shaft|eccentric|collar)|"
     r"backtoggle|fronttoggle|swingjaw_(face|seat)|rods_)", "steel"),
    (r"^pipecopper_", "pipecopper"),
    (r"^pipelead_", "pipelead"),
    (r"^mortar_screen", "mesh"),
    (r"^(camframe_belt|jawframe_belt)", "leather"),
    (r"^buffer\d", "rubber"),
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
    print(f"textures: {len(TEX_RULES)} rules (oak timber, planks for the launder and the feed box, iron castings, steel shafts and "
          f"wearing parts, the water line's copper or lead, the game's chute, a mesh screen, leather belts, rubber buffers): {len(bad)} break them, {len(unruled)} have no rule")
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
    anchor at the thing it names: the water flange, the feed box's mouth, the grizzly's head, the launder's spout."""
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
    spout = aabb_of(v.named("mortar", r"_spout_floor"))
    out = [c * 16 for c in rig["output"]["pos"]]
    feed = [c * 16 for c in rig["feed"]["pos"]]
    floor = aabb_of(v.named("mortar", r"_feed_floor"))
    infeed = [c * 16 for c in rig["infeed"]["pos"]]
    head = aabb_of(v.named("grizzly", r"_head"))
    chute = aabb_of(v.named("grizzly", r"_chute_"))
    checks = [("the water line's inlet reaches the water face, on the anchor's axis", abs(inlet[0][0] - water[0]) < 0.01
               and abs((inlet[0][1] + inlet[1][1]) / 2 - water[1]) < 0.01 and abs((inlet[0][2] + inlet[1][2]) / 2 - water[2]) < 0.01),
              ("the launder's spout runs out to the output face under the anchor", spout[0][2] <= 0.01 and spout[0][0] < out[0] < spout[1][0]
               and abs(spout[1][1] - out[1]) < 0.01),
              ("the feed anchor is over the feed box's floor", floor[0][0] < feed[0] < floor[1][0] and floor[0][2] < feed[2] < floor[1][2]),
              ("the infeed anchor is on the chute's axis, at its mouth, over the grizzly's head", abs((chute[0][0] + chute[1][0]) / 2 - infeed[0]) < 1e-6
               and abs((chute[0][2] + chute[1][2]) / 2 - infeed[2]) < 1e-6 and abs(chute[1][1] - infeed[1]) < 1e-6
               and head[0][0] < infeed[0] < head[1][0])]
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


def check_chute(v):
    """The chute-fed infeed is the game's chute section: VANILLA_CHUTE is the game's straight chute (when the game is
    installed: every wall's from, to, UVs, rotations and drawn faces), and at the infeed cell's top face the section's
    profile is a vanilla `chute-straight-ud`'s in that cell (walls 1 thick round a 6 x 6 bore, 4..12 in the block),
    flush with the face, in the chute's texture, so a vanilla chute placed on the face meets it with no gap or step."""
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
    # the profile at the face: the vanilla ud chute's walls in the infeed cell
    o = m.chute_origin()
    want = sorted([(o[0] + 4.0, o[0] + 12.0, o[2] + 4.0, o[2] + 5.0), (o[0] + 4.0, o[0] + 12.0, o[2] + 11.0, o[2] + 12.0),
                   (o[0] + 4.0, o[0] + 5.0, o[2] + 5.0, o[2] + 11.0), (o[0] + 11.0, o[0] + 12.0, o[2] + 5.0, o[2] + 11.0)])
    walls = v.named("grizzly", r"_chute_")
    got, top, tex = [], [], set()
    for e in walls:
        lo, hi = e.aabb()
        got.append((round(lo[0], 6), round(hi[0], 6), round(lo[2], 6), round(hi[2], 6)))
        top.append(hi[1])
        tex |= {f["texture"] for f in e.faces.values()}
    face_y = (m.INFEED_CELL[1] + 1) * 16
    ok = sorted(got) == want and all(abs(t - face_y) < 1e-6 for t in top) and tex == {"#chute"} and m.TEXTURES["chute"] == "game:block/metal/sheet/copper1"
    print(f"chute: the infeed section's profile at the top face is a vanilla ud chute's in its cell, flush, in the chute's texture: {ok}")
    if not ok:
        v.fail(f"the infeed chute does not meet a vanilla chute: walls {sorted(got)}, want {want}, tops {top}, textures {tex}")


def check_paths(v):
    """The ore's way through the machine, at rest (everything fitted): the grizzly's bars fall steeply enough to shed
    rock, their gaps let the fines through, their foot lies over the breaker's mouth and their head under the infeed;
    the throat plate sheds what falls on it past the front wall's face; the breaker's discharge falls on the feed
    box's floor, which falls steeply enough to the feed lip; the screen is over the apron, which ends over the
    launder."""
    m = v.m
    (hz, hy), (fz, fy) = m.GRIZ_HEAD, m.GRIZ_FOOT
    griz = math.degrees(math.atan2(hy - fy, fz - hz))
    gap = (m.GRIZ_X[1] - m.GRIZ_X[0] - m.GRIZ_BARS * m.GRIZ_BAR_W) / (m.GRIZ_BARS - 1)
    mouth = (m.JAW_C - m.FIXED_U, m.JAW_C - m.FACE_TOP[1])
    bars = aabb_of(v.named("grizzly", r"_bar"))
    throat = aabb_of(v.named("grizzly", r"_throat"))
    floor = aabb_of(v.named("mortar", r"_feed_floor"))
    front = m.JAW_C - m.FRONT_U
    disc = (m.JAW_C - m.FIXED_U, m.JAW_C - m.FACE_BOT[1])
    screen = aabb_of(v.named("mortar", r"_screen"))
    apron = aabb_of(v.named("mortar", r"_apron"))
    launder = aabb_of(v.named("mortar", r"_launder_floor"))
    back = aabb_of(v.named("mortar", r"_back_lo"))
    checks = [
        (f"the grizzly's bars fall {griz:.1f} degrees (at least 40)", griz >= 40.0),
        (f"the bars stand {gap:.2f} apart (at least 1)", gap >= 1.0),
        (f"the bars' foot (z {bars[1][2]:.2f}) lies over the mouth (z {mouth[0]:.2f}..{mouth[1]:.2f})", mouth[0] < bars[1][2] < mouth[1]),
        (f"the throat plate falls {math.degrees(m.THROAT_SLOPE):.0f} degrees and ends at z {throat[0][2]:.2f}, past the front wall's face (z {front:.2f})",
         m.THROAT_SLOPE >= math.radians(38.0) and throat[0][2] < front - 0.3),
        (f"the discharge (z {disc[0]:.2f}..{disc[1]:.2f}) falls on the feed box's floor (z {floor[0][2]:.2f}..{floor[1][2]:.2f})",
         floor[0][2] < disc[0] and disc[1] < floor[1][2]),
        (f"the feed box's floor falls {math.degrees(m.FEED_SLOPE):.0f} degrees (at least 35) to the lip (y {m.FEED_LIP_Y})",
         m.FEED_SLOPE >= math.radians(35.0) and abs(m.feed_floor_y(m.MORTAR_Z[1]) - back[1][1]) < 0.01),
        ("the apron starts under the screen and ends over the launder", apron[1][1] <= screen[0][1] and apron[0][2] > launder[0][2]
         and apron[0][2] < launder[1][2]),
    ]
    for label, good in checks:
        print(f"paths: {label}: {good}")
        if not good:
            v.fail(label)


# ---------------------------------------------------------------- mechanism
def angle_x(v, pid, pose):
    mm = v.mat(pid, pose)
    return math.atan2(mm[2][1], mm[1][1])


def check_drive(v):
    """The rectifier's gear case turns the line shaft one way whichever way the axle turns; every belt's two pulleys
    move their rims alike (an open belt: the same sense) and its runs are tangent to both rims."""
    m = v.m
    for th in (0.7, -0.7):
        a, b = (0.0, 0.0), (th, abs(th))
        ls = angle_x(v, "line", b) - angle_x(v, "line", a)
        if ls * m.LINE_RATIO <= 0:
            v.fail(f"the line shaft turns {ls:+.3f} for the axle's {th:+}")
    print("rectifier: the line shaft turns the same way for either sign of the axle")
    # the axle either way: with the same travel, every part but the entry shaft stands the same whichever sign the
    # axle's angle has (they are all behind the rectifier, on psi)
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
    belts = [("bull belt", "linepulley", m.LINE, m.LINE_PULLEY_R, "camshaft", m.CAM_C, m.BULL_R, "camframe", r"camframe_belt"),
             ("breaker's belt", "jawpulley", m.LINE, m.JAWDRIVE_R, "eshaft", m.jaw_to_build(m.E_C), m.FLY_R, "jawframe", r"jawframe_belt")]
    for label, pa, ca, ra, pb, cb, rb, bpart, brx in belts:
        worst = 0.0
        for s in (0.2, 3.1, 7.7):
            s1, s2 = m.turning(s), m.turning(s + 0.01)
            da = math.remainder(angle_x(v, pa, s2) - angle_x(v, pa, s1), math.tau) * ra
            db = math.remainder(angle_x(v, pb, s2) - angle_x(v, pb, s1), math.tau) * rb
            worst = max(worst, abs(da - db))
        runs = v.named(bpart, brx + r"_run")
        tang = 0.0
        for run in runs:
            ends = []
            k = max(range(3), key=lambda i: run.size[i])        # the run's centre line: its long axis
            ax = [run.r[i][k] for i in range(3)]
            half = run.size[k] / 2
            for sgn in (1, -1):
                ends.append([run.c[i] + sgn * half * ax[i] for i in range(3)])
            for e in ends:
                for cc, rr in ((ca, ra), (cb, rb)):
                    d = math.hypot(e[1] - cc[0], e[2] - cc[1])
                    if abs(d - (rr + m.BELT_T / 2)) < 0.5:
                        radial = ((e[1] - cc[0]) / d, (e[2] - cc[1]) / d)
                        tang = max(tang, abs(d - (rr + m.BELT_T / 2)), abs(radial[0] * ax[1] + radial[1] * ax[2]))
        ends_ok = len(runs) == 2
        print(f"belts: the {label}'s rims move alike (worst {worst:.1e}); its runs tangent to both rims (worst {tang:.1e})")
        if worst > 1e-6 or tang > 1e-6 or not ends_ok:
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
    between drops (at most 0.12 over it, never into it); thrown above its cam's tip and falling clear of it; two drops
    a camshaft turn, in the firing order."""
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


def check_jaw(v):
    """The breaker's linkage stays joined through a whole turn of the eccentric (72 steps), in the build frame: the
    pitman's strap round the eccentric, its foot on both toggles' ends and where the solved linkage puts it, the back
    toggle on its seat, the front toggle on the swinging jaw's seat, the rods' pins in the jaw's lugs; the toggles
    never pass straight; the jaws never close to under 1."""
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
        sag = m.TOGGLE_Y + m.JAW_DY - foot[1]
        if sag < 0.3:
            v.fail(f"the toggles pass straight at phi {phi:.2f} (sag {sag:.2f})")
        face_bot = v.point("swingjaw", pose, b(m.design_to_mean_jaw(m.FACE_BOT)))
        gaps.append(face_bot[2] - (m.JAW_C - m.FIXED_U))
    print("jaw: " + "; ".join(f"{k} within {d:.1e}" for k, d in worst.items()))
    if max(worst.values()) > 1e-3:
        v.fail("the jaw's linkage comes apart")
    print(f"jaw: the gap at the bottom between {min(gaps):.2f} and {max(gaps):.2f} voxels (a throw of {max(gaps) - min(gaps):.2f})")
    if min(gaps) < 1.0:
        v.fail("the jaws close too far")


def check_supports(v):
    m = v.m
    shafts = [
        ("entry shaft", "entry", r"_shaft", m.ENTRY, "frame", r"^fr_gearcase$", 1),
        ("line shaft", "line", r"_shaft", m.LINE, "frame", r"^fr_(gearcase_boss|pedw|pedm)$", 3),
        ("camshaft", "camshaft", r"_shaft", m.CAM_C, "camframe", r"_pillow", 2),
        ("eccentric shaft", "eshaft", r"_shaft", m.jaw_to_build(m.E_C), "jawframe", r"_ebearing", 2),
        ("hinge pin", "jawframe", r"_hingepin", m.jaw_to_build(m.H0), "jawframe", r"_hbearing", 2),
    ]
    for label, pid, rx, c, bpid, brx, need in shafts:
        els_ = v.named(pid, rx)
        carriers = [e for e in v.by_part[bpid] if re.search(brx, e.name)]
        found, _ = shaft_supports(carriers, els_, 0, c)
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
    poses = [m.REST] + [m.at_cam(a) for a in (0.3, 1.2, 2.2, 3.0)] + [m.at_ecc(a) for a in (0.7, 2.9, 5.1)] + [m.turning(31.0, -1.0)]
    check_containment(v, poses)
    check_anchors(v)
    check_paths(v)
    check_chute(v)
    check_textures(v)
    check_drive(v)
    check_stamps(v)
    check_jaw(v)
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
    lidded = lambda x, z: m.lidded(x + m.ORIGIN_CELL[0], z + m.ORIGIN_CELL[2])  # noqa: E731
    gaps = lid_gaps(ship["cells"], lidded)
    hollow = sum(1 for c in ship["cells"] if c.get("hollow"))
    keys = {k for c in ship["cells"] for k in c.get("fitted", {})}
    bad_keys = keys - set(m.LEVELS["level2"]) - set(m.PIPE_REQUIRES)
    by = {k: sum(1 for c in ship["cells"] if k in c.get("fitted", {})) for k in sorted(keys)}
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, {hollow} hollow; "
          f"fitted boxes in {by}; lids over the battery's columns: {'yes' if not gaps else gaps}")
    if gaps or bad_keys:
        print(f"FAIL lids {gaps}, fitted keys that are not requires values {bad_keys}")
        ok = False
    for c in ship["cells"]:
        for b in c.get("boxes", []) + [b for bs in c.get("fitted", {}).values() for b in bs]:
            if not (len(b) == 6 and all(0 <= x <= 1 for x in b) and all(b[k] < b[k + 3] for k in range(3))):
                print(f"FAIL a box of cell {c['pos']} is not inside it: {b}")
                ok = False
    return ok
