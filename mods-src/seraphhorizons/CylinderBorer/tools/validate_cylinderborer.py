"""The cylinder borer generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
from machinegen.checks import supports as shaft_supports
from machinegen.checks import sight_glass
from machinegen.geometry import aabb_of, mvec
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
            if len(self.cache) > 40000:
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


def present(m, pid, k):
    return m.on_show(pid, k)


def inside(el, p, eps=1e-6):
    """Whether point p (voxels) lies inside element `el` (an oriented box)."""
    d = [p[i] - el.c[i] for i in range(3)]
    local = [sum(el.r[j][k] * d[j] for j in range(3)) for k in range(3)]
    return all(abs(local[k]) <= abs(el.size[k]) / 2 + eps for k in range(3))


def corners_and_faces(el):
    """The element's corners, its face centres and its edge midpoints: the points a hidden element must keep
    inside whatever hides it."""
    h = [abs(s) / 2 for s in el.size]
    pts = []
    for sx in (-1, 0, 1):
        for sy in (-1, 0, 1):
            for sz in (-1, 0, 1):
                if (sx, sy, sz) == (0, 0, 0):
                    continue
                local = [sx * h[0], sy * h[1], sz * h[2]]
                w = mvec(el.r, local)
                pts.append([el.c[i] + w[i] for i in range(3)])
    return pts


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
    skin = sum(n for k, n in counts.items() if v.m.is_skin(k))
    ropes = sum(n for k, n in counts.items() if v.m.is_rope_piece(k))
    print(f"parts: {len(v.parts)}, elements {len(v.els)}: " + ", ".join(f"{k} {n}" for k, n in counts.items()
                                                                         if not (v.m.is_skin(k) or v.m.is_rope_piece(k)))
          + f", skin quarters {sum(1 for k in counts if v.m.is_skin(k))} ({skin} staves), rope pieces {ropes}")
    # nothing static without a reason to be drawn: the renderer draws a part with a requires, a ride or a driver
    idle = [p["id"] for p in v.parts if p["id"] != "frame" and not (p["requires"] or p.get("ride") or p["drivers"])]
    if idle:
        v.fail(f"parts the renderer would never draw (no requires, ride or driver) and not the frame: {idle}")


def check_containment(v, poses):
    m = v.m
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where = -1e9, None
    for pose in poses:
        k = pose[3]
        for pid in v.by_part:
            if not present(m, pid, k):
                continue
            for el in v.posed(pid, pose):
                lo, hi = el.aabb()
                o = box_overhang(lo, hi, box)
                if o > worst:
                    worst, where = o, (el.name, pose)
    print(f"containment over {len(poses)} poses: worst overhang {worst:.3f} voxels ({where[0]})")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the machine box at {where[1]}")


def check_floating(v):
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        k = pose[3]
        els = [e for pid in v.by_part for e in v.posed(pid, pose)]
        pairs = coplanar_faces([e for e in m.shown(els, pose) if e.c[1] > -500])
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses")
    if bad:
        v.fail("faces z-fight")


# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels at every sampled pose.
SKIN = r"s[sl]\d[tlbr]"
ALLOWED = [
    # shafts in their bearings, collars against them
    ("entry", r"_shaft|_rod", "frame", r"fr_entry_bearing"),
    ("bar", r"bar_seg", "bearings", None), ("bar", r"bar_collar", "bearings", r"brg_far"),
    ("drum", r"drum_shaft", "frame", r"fr_drum_bearing|fr_post"),
    # the gears in mesh (their pitch circles are checked by check_gearing)
    ("entry", r"entry_pinion", "bar", r"wheel_tooth|wheel_rim"),
    # the head on the bar (keyed, it slides along it), the yoke in its groove, the rod in the yoke
    ("head", None, "bar", r"bar_(seg|key)"), ("cutter\\d", r"_holder", "head", r"head_body|head_wedge"),
    ("cutter\\d", r"_holder", "bar", r"bar_seg"),
    ("feed", r"feed_yoke", "head", r"head_groove|head_lip"),
    # the crosshead on its ways
    ("feed", r"feed_shoe", "frame", r"fr_way"),
    # the ropes: on the drums, through the crosshead's eye and in its top rail, through the weight's eye and in the weight
    ("xrope\\d|feedropes", None, "feed", r"feed_toprail|feed_ropeeye"), ("xrope\\d|feedropes", None, "drum", None),
    ("xrope\\d|feedropes", None, "xrope\\d|feedropes", None),
    ("wrope\\d|feedropes", None, "weight", None), ("wrope\\d|feedropes", None, "wrope\\d|feedropes", None),
    # the cylinders in their V-blocks and chains, the chains on their hook bars
    ("blank(small|large)", None, "saddles", r"_cheek"), ("chain(small|large)", None, "blank(small|large)", None),
    ("chain(small|large)", None, "frame", r"fr_hookbar\d$"),
    # the skin is the casting's: it lies on and sinks into its wall; the cutters cut it (check_bore holds the rest)
    (SKIN, None, "blank(small|large)", None), (SKIN, None, SKIN, None),
    # the V-blocks on their jacks' nuts, round the screws; the nuts on the screws and on the beams
    ("saddles", r"_base", "jack\\d", r"_nut"), ("saddles", None, "frame", r"fr_jack\d_screw"),
    ("jack\\d", None, "frame", r"fr_jack\d_screw|fr_cross_saddle"),
    # the oil in its cups, the cups' pipes into the bearings
    ("oillevel", None, "frame", r"fr_cup\w+_(base|glass)"), ("bearings", None, "frame", r"fr_cup\w+_pipe|fr_\w+brg_post"),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose, skip=lambda pa, ea, pb, eb: False):
    m = v.m
    k = pose[3]
    items = []
    for pid in v.by_part:
        if not present(m, pid, k):
            continue
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
                if pa == pb:
                    continue
                if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                    continue
                if allowed(pa, ea.name, pb, eb.name) or skip(pa, ea, pb, eb):
                    continue
                if obb_obb(ea, eb):
                    hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def cutting(m, pose):
    """The skin slices the cutters may touch at this pose: those whose window W is in."""
    k, W = pose[3], pose[2]
    if not k:
        return set()
    cls = m.CLASSES[k - 1]
    t, _ = m.blank_tag(cls)
    out = set()
    for j in range(1, m.SKIN_SLICES + 1):
        w0, w1 = m.skin_window(cls, j)
        if w0 - 1e-9 <= W <= w1 + 1e-9:
            out |= {f"s{t}{j}{q}" for q in "tlbr"}
    return out


def cut_skip(m, pose):
    cut = cutting(m, pose)

    def skip(pa, ea, pb, eb):
        for x, y in ((pa, pb), (pb, pa)):
            if re.fullmatch(r"cutter\d", x) and y in cut:
                return True
        return False
    return skip


def report_hits(label, n, allhits):
    print(f"{label} over {n} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at W {p[2]:.4f} k {p[3]}" for (a, b), p in ex))


def check_clearances(v, poses):
    allhits = {}
    for pose in poses:
        for key, hs in touching_pairs(v, pose, cut_skip(v.m, pose)).items():
            allhits.setdefault(key, {})
            for h in hs:
                allhits[key].setdefault(h, pose)
    report_hits("clearances", len(poses), allhits)
    if allhits:
        v.fail("parts run into each other")


def check_clearing(v):
    """The bored cylinder taken away: at its end, with its parts no longer drawn (gameplay hides the work when it
    delivers it), the head, the crosshead, the weight and the V-blocks and cutters ease back with p, touching nothing
    but their intended contacts."""
    m = v.m
    hits = {}
    n = 0
    work = ("blank", "chain")

    def skip(pa, ea, pb, eb):
        return any(x.startswith(work) or m.is_skin(x) for x in (pa, pb))
    for k in (1, 2):
        for p in (0.85, 0.6, 0.35, 0.1):
            pose = m.pose_at(k, m.end_of(k), p=p)
            n += 1
            for key, hs in touching_pairs(v, pose, skip).items():
                for h in hs:
                    hits.setdefault(key, {}).setdefault(h, pose)
    report_hits("clearing: the head and the feed easing back with the cylinder gone", n, hits)
    if hits:
        v.fail("something runs into something as the machine is cleared")


def check_swept(v):
    """Swept paths: the head, its cutters, the rod and the crosshead through the whole travel, every 1/64 block,
    for both cylinders and five shaft angles (the bar a quarter turn apart, so its feather comes all round), touch
    nothing but their intended contacts (the cutters only the skin slices they are cutting)."""
    m = v.m
    n = 0
    hits = {}
    for k in (1, 2):
        end = m.end_of(k)
        steps = int(round(end * 64))
        for i in range(steps + 1):
            for th in (0.0, 0.7, 2 * math.pi, 4 * math.pi, 6 * math.pi):    # the bar a quarter turn apart: its feather all round
                pose = m.pose_at(k, end * i / steps, theta=th)
                n += 1
                for key, hs in touching_pairs(v, pose, cut_skip(m, pose)).items():
                    for h in hs:
                        hits.setdefault(key, {}).setdefault(h, pose)
    report_hits("swept paths: the head and the feed", n, hits)
    if hits:
        v.fail("something stands in the head's or the feed's path")


TEX_RULES = [
    (r"^(fr_sill|fr_cross|fr_post|fr_topbeam|fr_farbeam|fr_rail|fr_leg|fr_\w+brg_post|fr_entry_post|entry_shaft[ab]|saddle\d_|drum_big|drum_small|feed_(grip|base|upright|toprail))", "oak"),
    (r"^(fr_way|fr_drum_bearing|fr_entry_bearing|fr_jack\d_screw|fr_hookbar|brg_|bar_(seg|collar)|wheel_|entry_(rod|pinion|hub)|head_(body|lip|backhub)|cut\d_holder|feed_(shoe|boss|ropeeye|handlearm|rod)|drum_(shaft|flange|sflange)|weight_|jack\d_|ch[sl]\d_link)", "iron"),
    (r"^(head_(groove|wedge)|cut\d_bit|feed_yoke|bar_key)", "steel"),
    (r"^bl[sl]_", "cast"),
    (r"^s[sl]\d[tlbr]_", "rough"),
    (r"^(ropes_|xr\d|wr\d|drum_(wraps|swraps))", "rope"),
    (r"^fr_cup\w+_glass", "glass"),
    (r"^fr_cup\w+_(base|cap|knob|bracket|arm|pipe)", "brass"),
    (r"^oillevel_", "oil"),
]


def check_textures(v):
    bad = []
    for el in v.els:
        for rx, tex in TEX_RULES:
            if re.match(rx, el.name):
                got = {f["texture"].lstrip("#") for f in el.faces.values()}
                want = {tex}
                if re.match(r"^bl[sl]_wall", el.name):
                    want = {"cast", "bored"}            # the barrel's bore face is bright, the rest is the casting
                if got != want:
                    bad.append((el.name, sorted(got), sorted(want)))
                break
        else:
            bad.append((el.name, "no rule", ""))
    print(f"textures: {len(TEX_RULES)} rules (oak timber, the axle's continuation, the V-blocks, the drums and the crosshead; iron castings, "
          f"bearings, gears, the bar, the head, the crosshead's shoes, the weight and the chains; steel bits, wedges, the feather and the yoke; the "
          f"casting, its bright bore and its rough skin; rope; a glass and brass oiler with oil in it): {len(bad)} elements break them")
    if bad:
        v.fail(f"textures by role: {bad[:4]}")
    # the bore face of each barrel stave is the one towards the bar
    for el in v.els:
        if re.match(r"^bl[sl]_wall\d", el.name):
            down = mvec(el.r, [0.0, -1.0, 0.0])
            to_axis = [v.m.BAR[0] - el.c[0], v.m.BAR[1] - el.c[1], 0.0]
            if sum(down[i] * to_axis[i] for i in range(3)) <= 0 or el.faces["down"]["texture"] != "#bored":
                v.fail(f"{el.name}'s bored face does not face the bar")


def check_anchors(v):
    m = v.m
    rig = v.rig
    pc = tuple(rig["powerCell"])
    entry = v.named("entry", r"_shaft")
    _, hi = aabb_of(entry)
    print(f"anchors: power cell {pc} face {rig['powerFace']}, the entry shaft reaches z {hi[2]:.2f} on that face, axis at "
          f"(x {m.ENTRY[0]}, y {m.ENTRY[1]}): the cell's centre")
    want = ((pc[0] + 0.5) * 16, (pc[1] + 0.5) * 16)
    if abs(hi[2] - (pc[2] + 1) * 16) > 0.01 or pc != m.POWER_CELL or rig["powerFace"] != "south" or (m.ENTRY[0], m.ENTRY[1]) != want:
        v.fail("the entry shaft does not meet the power face at the cell's centre")
    for key in ("output", "chips", "drip"):
        p = rig[key]["pos"]
        if not all(0 <= p[i] <= (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)[i] for i in range(3)):
            v.fail(f"the {key} anchor is outside the machine")
    ox = rig["output"]["pos"]
    if rig["outputSide"] != "west" or ox[0] > 0.01 or not (m.SADDLE_Z[0][1] / 16 < ox[2] < m.SADDLE_Z[1][0] / 16):
        v.fail("the output anchor is not on the west face beside the cradle")
    if rig["infeedSide"] != "east":
        v.fail("the infeed is not the cradle's east side")
    print(f"anchors: infeed {rig['infeedSide']}, output {rig['outputSide']} at {ox}, chips at the bore's drive end, drip at the far cup's pipe")


# ---------------------------------------------------------------- mechanism
def angle_z(v, pid, pose):
    mm = v.mat(pid, pose)
    return math.atan2(mm[1][0], mm[0][0])


def angle_x(v, pid, pose):
    mm = v.mat(pid, pose)
    return math.atan2(mm[2][1], mm[1][1])


def check_gearing(v):
    """The pinion and the great wheel: the centre distance is the sum of the pitch radii, and the arcs they roll on
    their pitch circles are equal and opposite (finite differences of the posed rig), either way round; the wheel's
    tooth gaps meet the pinion's teeth on the line of centres."""
    m = v.m
    d = math.hypot(m.BAR[0] - m.ENTRY[0], m.BAR[1] - m.ENTRY[1])
    if abs(d - (m.R_PINION + m.R_WHEEL)) > 1e-6:
        v.fail(f"pinion and wheel: centres {d:.4f} apart, pitch radii add to {m.R_PINION + m.R_WHEEL}")
    worst = 0.0
    for th in (0.3, -0.3, 2.0, -5.1):
        s1, s2 = (th, abs(th), 0.0, 0, 0.0, 0.0), (th + 0.05, abs(th) + 0.05, 0.0, 0, 0.0, 0.0)
        arc_a = math.remainder(angle_z(v, "entry", s2) - angle_z(v, "entry", s1), 2 * math.pi) * m.R_PINION
        arc_b = math.remainder(angle_z(v, "bar", s2) - angle_z(v, "bar", s1), 2 * math.pi) * m.R_WHEEL
        worst = max(worst, abs(arc_a + arc_b))
        if abs(arc_a + arc_b) > 1e-6:
            v.fail(f"pinion and wheel slip at theta {th}: {arc_a:.6f} against {arc_b:.6f}")
        # the head turns with the bar
        if abs(math.remainder(angle_z(v, "head", s1) - angle_z(v, "bar", s1), 2 * math.pi)) > 1e-9:
            v.fail(f"the head does not turn with the bar at theta {th}")
    print(f"gearing: pinion {m.PINION_T} on the entry shaft, wheel {m.WHEEL_T} on the bar, centres {d:.3f} apart (pitch radii "
          f"{m.R_PINION} + {m.R_WHEEL}), the bar a quarter of the axle the other way (worst roll mismatch {worst:.1e}); the head keyed to it")
    # interleave: at a few angles, the pinion's teeth and the wheel's teeth stay out of each other but for a nibble
    worst = 0.0
    for th in [i * 0.1 for i in range(40)]:
        pose = (th, th, 0.0, 0, 0.0, 0.0)
        pin = v.named("entry", r"entry_pinion_tooth", pose)
        wheel = v.named("bar", r"wheel_tooth", pose)
        for a in pin:
            for b in wheel:
                if obb_obb(a, b, eps=0.12):
                    worst = max(worst, 1.0)
    print(f"gearing: no pinion tooth runs more than 0.12 into a wheel tooth over 40 angles: {'yes' if worst == 0 else 'NO'}")
    if worst:
        v.fail("the gears' teeth run into each other")


def axis_of(els):
    lo, hi = aabb_of(els)
    return ((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2)


def check_concentric(v):
    """Each cylinder lies concentric with the bar, its two lower flats either side of the bottom bedded on the V-blocks
    as set for it; the chains lie on its upper flats and come down to their hook bars; the cutters reach just inside its
    bore and its skin starts at its rough bore."""
    m = v.m
    for k, cls in ((1, "thin"), (2, "thick")):
        size = "small" if k == 1 else "large"
        pose = m.pose_at(k, 0.0)
        g = m.CYL[cls]
        walls = v.named(f"blank{size}", r"_wall", pose)
        cx, cy = axis_of(walls)
        if math.hypot(cx - m.BAR[0], cy - m.BAR[1]) > 1e-6:
            v.fail(f"the {size} cylinder's axis is at ({cx:.3f}, {cy:.3f}), not the bar's")
        # the V's faces: each cheek's top face plane, against the cylinder's flats at 30 degrees either side of the bottom
        worst = 0.0
        for cheek in v.named("saddles", r"_cheek", pose):
            n = mvec(cheek.r, [0.0, 1.0, 0.0])
            if n[1] < 0:
                n = [-x for x in n]
            # the face towards the V (up and in) is the cheek's +-y face whose normal points up
            face_c = [cheek.c[i] + n[i] * abs(cheek.size[1]) / 2 for i in range(3)]
            dist = sum((m.BAR[i] - face_c[i]) * n[i] for i in range(2))
            worst = max(worst, abs(dist - g["out"]))
        print(f"cradle: the {size} cylinder concentric with the bar; its bedding flats lie on the V-blocks' faces (within {worst:.1e})")
        if worst > 1e-3:
            v.fail(f"the {size} cylinder does not bed on its V-blocks")
        # the cutters' bits just inside its bore
        tips = []
        for q in m.CUTTER_ANGLES:
            bit = v.named(f"cutter{q + 1}", r"_bit", pose)[0]
            tips.append(max(math.hypot(p[0] - m.BAR[0], p[1] - m.BAR[1]) for p in bit.corners()))
        gap = g["bore"] - max(tips)
        print(f"cutters: set for the {size} bore, their bits {min(tips):.3f}..{max(tips):.3f} from the axis, {gap:.3f} inside it")
        if not (0.0 < gap <= 0.05):
            v.fail(f"the cutters are not set to the {size} bore")
        # the chains: every link's centre CHAIN_OFF off the casting's surface or on the vertical runs, ends on the hook bars
        links = v.named(f"chain{size}", r"_link", pose)
        rc = g["out"] + m.CHAIN_OFF
        worst = 0.0
        for ln in links:
            x, y = ln.c[0] - m.BAR[0], ln.c[1] - m.BAR[1]
            if y > rc * math.tan(math.pi / m.NGON) - 1e-6:
                # on the upper flats: its distance along the nearest flat's normal
                a = round(math.degrees(math.atan2(-x, y)) / 30) * 30
                dx, dy = m.ang_dir(a * m.DEG)
                worst = max(worst, abs(x * dx + y * dy - rc))
            else:
                worst = max(worst, abs(abs(x) - rc))
        ends = sorted(links, key=lambda e: e.c[1])[:4]
        low = max(e.aabb()[0][1] for e in ends)
        print(f"chains: {len(links)} links over the {size} cylinder, on its flats (within {worst:.3f}), down to their hook bars at y {m.HOOK_Y}")
        if worst > 0.05 or low > m.HOOK_Y + 0.2:
            v.fail(f"the {size} cylinder's chains are not on it and its hook bars")


def check_cradle(v):
    """The jacks: each nut turns V_DROP / lead turns as it runs down V_DROP for the large cylinder; the V-blocks rest on
    the nuts for both; the nuts stand on the beams for the large one."""
    m = v.m
    for k in (1, 2):
        pose = m.pose_at(k, 0.0)
        base = min(e.aabb()[0][1] for e in v.named("saddles", r"_base", pose))
        for i in range(1, len(m.jack_spots()) + 1):
            nut = v.named(f"jack{i}", r"_nut", pose)
            top = max(e.aabb()[1][1] for e in nut)
            bot = min(e.aabb()[0][1] for e in nut)
            if abs(top - base) > 0.03:
                v.fail(f"jack {i}'s nut is not under the V-block (cylinder {k}): {top:.3f} against {base:.3f}")
            if k == 2 and abs(bot - m.CRADLE_TOP) > 0.01:
                v.fail(f"jack {i}'s nut does not stand on its beam for the large cylinder ({bot:.3f})")
            turned = -angle_y(v, f"jack{i}", pose)
            drop = -v.mat(f"jack{i}", pose)[1][3] * 16
            want_turn = drop / m.JACK_LEAD * 2 * math.pi
            if abs(math.remainder(turned - want_turn, 2 * math.pi)) > 1e-4:
                v.fail(f"jack {i} turns {turned:.4f} for a drop of {drop:.3f}")
    print(f"cradle: the V-blocks wound down {m.V_DROP:.3f} for the large cylinder on four jacks, each nut turning "
          f"{m.V_DROP / m.JACK_LEAD:.3f} turns on its screw (lead {m.JACK_LEAD}), the blocks resting on the nuts")


def angle_y(v, pid, pose):
    mm = v.mat(pid, pose)
    return math.atan2(mm[0][2], mm[0][0])


def spans_union(spans):
    out = []
    for a, b in sorted(spans):
        if out and a <= out[-1][1] + 0.02:
            out[-1][1] = max(out[-1][1], b)
        else:
            out.append([a, b])
    return out


def check_feed(v):
    """The weighted feed: the head, the yoke, the rod and the crosshead travel together, W blocks; the yoke stays in
    the head's groove; the crosshead's rope winds onto the big drum as it comes (the drum turns the travel over its
    radius) and stays whole from the drum's top to the crosshead's eye, level; the weight falls the small drum's share
    of it and its rope stays whole from the small drum to the weight's cap; every rope piece taken up is inside the
    crosshead's top rail or the weight."""
    m = v.m
    worst = 0.0
    for k in (1, 2):
        end = m.end_of(k)
        for f in (0.0, 0.13, 0.5, 0.77, 1.0):
            W = end * f
            pose = m.pose_at(k, W)
            for pid in ("head", "feed"):
                dz = v.mat(pid, pose)[2][3] * 16
                worst = max(worst, abs(dz + W * 16))
            # the yoke in the groove
            yoke = aabb_of(v.named("feed", r"feed_yoke", pose))
            groove = aabb_of(v.named("head", r"head_groove", pose))
            if not (groove[0][2] - 1e-6 <= yoke[0][2] and yoke[1][2] <= groove[1][2] + 1e-6):
                v.fail(f"the yoke leaves the head's groove at W {W:.3f}")
            # the drum: its turn times its radius is the travel
            turned = -angle_x(v, "drum", pose)
            if abs(math.remainder(turned * m.R_DRUM - W * 16, 2 * math.pi * m.R_DRUM)) > 1e-4:
                v.fail(f"the drum turns {turned:.4f} for a travel of {W * 16:.3f}")
            fall = -v.mat("weight", pose)[1][3] * 16
            if abs(math.remainder(fall - turned * m.R_WDRUM, 2 * math.pi * m.R_WDRUM)) > 1e-4:
                v.fail(f"the weight falls {fall:.3f}, the small drum pays out {turned * m.R_WDRUM:.3f}")
            # the crosshead's rope: whole from the drum's top tangent to the crosshead's north face
            xh = min(e.aabb()[0][2] for e in v.named("feed", r"feed_toprail", pose))
            rail = aabb_of(v.named("feed", r"feed_toprail", pose))
            spans = []
            for pid in [f"xrope{i}" for i in range(1, m.XROPE_SEGS + 1)] + ["feedropes"]:
                for e in v.posed(pid, pose):
                    if not e.name.startswith(("xr", "ropes_xtop")):
                        continue
                    lo, hi = e.aabb()
                    if abs(e.c[1] - m.XROPE_Y) > 1e-6 or abs(e.c[0] - m.BAR[0]) > 1e-6:
                        v.fail(f"{e.name} is off the rope's line at W {W:.3f}")
                    if lo[2] < xh - 1e-6:
                        spans.append((lo[2], min(hi[2], xh)))
                    if hi[2] > xh + 1e-6 and not (rail[0][2] - 1e-6 <= lo[2] and hi[2] <= rail[1][2] + 1e-6) and lo[2] > xh - 1e-6:
                        v.fail(f"{e.name} pokes out of the crosshead at W {W:.3f}")
                    if hi[2] > rail[1][2] + 1e-6:
                        v.fail(f"{e.name} reaches past the crosshead's back at W {W:.3f}")
            u = spans_union(spans)
            if len(u) != 1 or abs(u[0][0] - m.DRUM_C[1]) > 0.01 or abs(u[0][1] - xh) > 0.03:
                v.fail(f"the crosshead's rope at W {W:.3f}: {u}, want {m.DRUM_C[1]}..{xh:.2f}")
            # the weight's rope: whole from the small drum to the weight's cap, the rest inside the weight
            cap = max(e.aabb()[1][1] for e in v.named("weight", r"weight_cap", pose))
            body = aabb_of(v.named("weight", r"weight_body|weight_cap", pose))
            spans = []
            for pid in [f"wrope{i}" for i in range(2, m.WROPE_SEGS + 1)] + ["feedropes"]:
                for e in v.posed(pid, pose):
                    if not e.name.startswith(("wr", "ropes_w")):
                        continue
                    lo, hi = e.aabb()
                    if hi[1] > cap + 1e-6:
                        spans.append((max(lo[1], cap), hi[1]))
                    if lo[1] < cap - 1e-6 and lo[1] < body[0][1] - 1e-6:
                        v.fail(f"{e.name} pokes out of the weight's bottom at W {W:.3f}")
            u = spans_union(spans)
            if len(u) != 1 or abs(u[0][0] - cap) > 0.03 or abs(u[0][1] - m.DRUM_C[0]) > 0.01:
                v.fail(f"the weight's rope at W {W:.3f}: {u}, want {cap:.2f}..{m.DRUM_C[0]}")
    print(f"feed: the head, the yoke, the rod and the crosshead travel together (within {worst:.1e}), the yoke in the head's groove; "
          f"the big drum ({m.R_DRUM}) winds the crosshead's rope on, level off its top, whole to the crosshead; the weight falls "
          f"{m.R_WDRUM / m.R_DRUM:g} of the travel ({m.DROP:.1f} in a large bore), its rope whole to its cap")
    # the head stays on the bar between its bearings, and is clear of the cylinder at W = 0
    for k in (1, 2):
        for W in (0.0, m.end_of(k)):
            head = aabb_of([e for e in v.posed("head", m.pose_at(k, W))])
            if head[0][2] < m.BRG_FAR_Z[1] or head[1][2] > m.BRG_DRIVE_Z[0]:
                v.fail(f"the head leaves the bar between its bearings at W {W}")
    print("feed: the head stays between the bar's bearings over its whole travel")


def wall_hides(walls, el):
    return all(any(inside(w, p, eps=1e-4) for w in walls) for p in corners_and_faces(el))


def check_bore(v):
    """The bore: each skin slice stands where it was cast (at the rough bore) until the cutters reach it, and from when
    they have passed it lies wholly inside the wall (every corner, edge and face centre of it inside a wall stave or a
    flange), so the bored face shows; at the end of the job every slice is sunk; the cutters' leading face reaches the
    first slice only after W = 0 and their trailing face leaves the last before the end."""
    m = v.m
    for k, cls in ((1, "thin"), (2, "thick")):
        size = "small" if k == 1 else "large"
        t, _ = m.blank_tag(cls)
        end = m.end_of(k)
        walls_rest = v.named(f"blank{size}", r".", m.pose_at(k, 0.0))
        first, last = m.skin_window(cls, 1), m.skin_window(cls, m.SKIN_SLICES)
        if not (first[0] > 0 and last[1] < end):
            v.fail(f"the {size} bore's cut does not lie within the head's travel: {first} .. {last}, end {end}")
        n_hidden = 0
        for j in range(1, m.SKIN_SLICES + 1):
            w0, w1 = m.skin_window(cls, j)
            for W, want in ((max(0.0, w0 - 0.01), "rest"), (w1 + 1e-6, "sunk"), (end, "sunk")):
                pose = m.pose_at(k, W)
                for q in "tlbr":
                    pid = f"s{t}{j}{q}"
                    mm = v.mat(pid, pose)
                    moved = max(abs(mm[r][c] - (1.0 if r == c else 0.0)) for r in range(3) for c in range(4))
                    if want == "rest" and moved > 1e-9:
                        v.fail(f"{pid} has moved before the cutters reach it (W {W:.4f})")
                    if want == "sunk":
                        for e in v.posed(pid, pose):
                            if not wall_hides(walls_rest, e):
                                v.fail(f"{e.name} is not hidden in the {size} wall at W {W:.4f}")
                            n_hidden += 1
        print(f"bore: the {size} cylinder's skin, {m.SKIN_SLICES} slices of four quarters, in place until the cutters reach "
              f"each slice and wholly inside the wall once they have passed it ({n_hidden} staves checked); cut over W "
              f"{first[0]:.3f}..{last[1]:.3f} of {end:.4f}")


def check_supports(v):
    m = v.m
    frame = v.by_part["frame"]
    bearings = v.by_part["bearings"]
    shafts = [
        ("boring bar", "bar", r"bar_seg", 2, m.BAR, bearings, r"^brg_(far|drive)$", 2),
        ("entry shaft", "entry", r"_shaft|_rod", 2, m.ENTRY, frame, r"^fr_entry_bearing\d$", 2),
        ("drum shaft", "drum", r"drum_shaft", 0, m.DRUM_C, frame, r"^fr_drum_bearing_[we]$", 2),
    ]
    for label, pid, rx, axis, c, carriers, brx, need in shafts:
        els_ = v.named(pid, rx)
        found, _ = shaft_supports(carriers, els_, axis, c)
        named = sorted(f for f in set(found) if re.search(brx, f))
        print(f"support {label}: {len(named)} bearing(s) {', '.join(named) or 'NONE'}")
        if len(named) < need:
            v.fail(f"the {label} is not carried by {need} bearing(s)")
    # the bearings sit on their pedestals
    for b in bearings:
        lo, _ = b.aabb()
        posts = [f for f in frame if f.name.startswith("fr_") and f.name.endswith("brg_post")]
        if not any(abs(p.aabb()[1][1] - lo[1]) < 1e-6 and p.aabb()[0][2] >= lo[2] - 1e-6 and p.aabb()[1][2] <= b.aabb()[1][2] + 1e-6 for p in posts):
            v.fail(f"{b.name} does not sit on a pedestal")
    print("support: the bar's bearings on their pedestals")
    # the control rod is held at both ends: in the crosshead's boss and in the yoke, at every W
    for k in (1, 2):
        for W in (0.0, m.end_of(k)):
            pose = m.pose_at(k, W)
            rod = aabb_of(v.named("feed", r"feed_rod", pose))
            boss = aabb_of(v.named("feed", r"feed_boss", pose))
            yoke = aabb_of(v.named("feed", r"feed_yoke", pose))
            if not (rod[0][2] < boss[1][2] - 0.5 and rod[1][2] > yoke[0][2] + 0.1 and rod[1][1] > yoke[0][1]):
                v.fail("the control rod is not held in the crosshead's boss and the yoke")
    print("support: the control rod held in the crosshead's boss and in the yoke at both ends of the travel")
    # the bench's rails rest on the far beam and on legs at their middle and near end
    for rx in (r"fr_rail_w", r"fr_rail_e"):
        rail = aabb_of(v.named("frame", rx))
        legs = [f for f in frame if re.match(r"fr_leg_", f.name) and f.aabb()[1][1] == rail[0][1]
                and rail[0][0] <= f.c[0] <= rail[1][0]]
        far = [f for f in frame if f.name == "fr_farbeam" and f.aabb()[1][1] == rail[0][1] and f.aabb()[0][2] <= rail[0][2] + 1e-6]
        if len(legs) < 2 or not far:
            v.fail(f"{rx} is not carried at both ends")
    print("support: the bench's rails on the far beam and on two legs each")


def check_oiler(v):
    m = v.m
    (x0, x1), (y0, y1) = m.CUP["x"], m.CUP["y"]
    for oil in (0.0, 0.5, 1.0):
        for lvl in v.named("oillevel", r".", (0.0, 0.0, 0.0, 0, 0.0, oil)):
            lo, hi = lvl.aabb()
            want = m.OIL_EMPTY + (m.OIL_FULL - m.OIL_EMPTY) * oil
            if abs((hi[1] - lo[1]) - want) > 1e-6 or abs(lo[1] - m.OIL_Y) > 1e-6 or hi[1] > y1 - 0.1:
                v.fail(f"{lvl.name}: the oil level is wrong at oil {oil}")
            if lo[0] < x0 or hi[0] > x1:
                v.fail("the oil is outside its glass")
    print(f"oiler: two cups, the level {m.OIL_EMPTY} tall empty and {m.OIL_FULL:.2f} full, linear between, inside the glass")
    for tag in ("far", "drive"):
        glass = v.named("frame", rf"^fr_cup{tag}_glass")
        lvl = v.named("oillevel", rf"oillevel_{tag}", (0.0, 0.0, 0.0, 0, 0.0, 1.0))[0]
        for problem in sight_glass(glass, lvl):
            v.fail(f"the {tag} cup's level is hidden: {problem}")
    print("oiler: each cup's four panes in the Transparent pass, the level seen through one from every side")
    drip = [c * 16 for c in v.rig["drip"]["pos"]]
    pipe = v.named("frame", r"fr_cupfar_pipe")[0].aabb()
    if not all(pipe[0][i] - 1e-3 <= drip[i] <= pipe[1][i] + 1e-3 for i in range(3)):
        v.fail(f"the drip anchor {drip} is not at the far cup's pipe")


def clearance_poses(m):
    out = [m.REST, (0.9, 0.9, 0.0, 0, 0.0, 0.5)]
    for k in (1, 2):
        cls = m.CLASSES[k - 1]
        ws = {0.0, m.end_of(k)}
        for j in range(1, m.SKIN_SLICES + 1):
            w0, w1 = m.skin_window(cls, j)
            ws |= {w0, (w0 + w1) / 2, w1}
        for i, W in enumerate(sorted(ws)):
            out.append(m.pose_at(k, W, theta=(0.0, 0.4, 1.1, 2.3)[i % 4]))
        out.append(m.pose_at(k, 0.0, p=0.5))
    return out


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_floating(v)
    poses = [m.REST] + [m.pose_at(k, m.end_of(k) * f, theta=th) for k in (1, 2) for f in (0.0, 0.25, 0.5, 0.75, 1.0) for th in (0.0, 0.5)]
    check_containment(v, poses)
    check_anchors(v)
    check_textures(v)
    check_gearing(v)
    check_concentric(v)
    check_cradle(v)
    check_feed(v)
    check_bore(v)
    check_supports(v)
    check_oiler(v)
    check_clearances(v, clearance_poses(m))
    check_clearing(v)
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
    if gaps or hollow:
        ok = False
    return ok
