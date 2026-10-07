"""The mandrel station generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved.
"""

from __future__ import annotations

import re

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
from machinegen.geometry import aabb_of
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

    def group(self, rx, pose):
        r = re.compile(rx)
        return [e for pid in self.by_part if r.fullmatch(pid) for e in self.posed(pid, pose)]


def present(m, pid, k):
    return m.on_show(pid, k)


def cycle_poses(m, step=0.005):
    n = int(round(1 / step))
    return [m.pose_at(k, i * step) for k in (1, 2) for i in range(n + 1)]


def check_basic(v):
    worst = euler_round_trip(v.els)
    print(f"euler round trip: worst {worst:.1e}")
    if worst > 1e-9:
        v.fail("an element's rotation does not survive being written as Euler angles")
    names = [el.name for el in v.els]
    if len(names) != len(set(names)):
        v.fail("duplicate element names")
    wrong = [(el.name, el.part, part_of(v.parts, el.name)) for el in v.els if part_of(v.parts, el.name) != el.part]
    if wrong:
        v.fail(f"{len(wrong)} elements land in the wrong part, e.g. {wrong[:4]}")
    empty = [p["id"] for p in v.parts if not v.by_part.get(p["id"])]
    if empty:
        v.fail(f"parts with no elements: {empty}")
    frame = len(v.by_part.get("frame", []))
    print(f"parts: {len(v.parts)}, elements {len(v.els)}; frame {frame}, mandrel {len(v.by_part.get('mandrel', []))}")


def check_containment(v, poses):
    m = v.m
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where = -1e9, None
    for pose in poses:
        for pid in v.by_part:
            if not present(m, pid, pose[2]):
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


ROLES = [
    (r"^fr_stump", {"oak"}),
    (r"^fr_(hoop|base|band|swage)", {"iron"}),
    (r"^mandrel_", {"mandrel"}),
    (r"^l\d", {"lead"}),
    (r"^c\d", {"copper"}),
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


RING = re.compile(r"([lc])(\d+)[udew]+")


def allowed(m, pa, pb, pose):
    """Intended overlaps, all inside the work, whose union is the tube (the volumes inside each other are
    never seen): a ring's own walls and corner bars, which overlap as they close; the rings of one
    section, which overlap until the hollow is stretched; and the two sections' rings until they are
    parted."""
    ra, rb = RING.fullmatch(pa), RING.fullmatch(pb)
    if not (ra and rb and ra.group(1) == rb.group(1)):
        return False
    near_a, near_b = int(ra.group(2)) - 1 in m.NEAR, int(rb.group(2)) - 1 in m.NEAR
    return near_a == near_b or pose[1] <= m.T_PART[0]


def touching_pairs(v, pose):
    items = []
    for pid in v.by_part:
        if present(v.m, pid, pose[2]):
            for el in v.posed(pid, pose):
                items.append((pid, el, el.aabb()))
    hits = {}
    for i in range(len(items)):
        pa, ea, (alo, ahi) = items[i]
        for j in range(i + 1, len(items)):
            pb, eb, (blo, bhi) = items[j]
            if pa == pb or (pa == "frame" and pb == "frame"):
                continue
            if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                continue
            if allowed(v.m, pa, pb, pose):
                continue
            if obb_obb(ea, eb):
                hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def check_clearances(v, poses, label):
    allhits = {}
    for pose in poses:
        for key, hs in touching_pairs(v, pose).items():
            allhits.setdefault(key, {})
            for h in hs:
                allhits[key].setdefault(h, pose)
    print(f"{label} over {len(poses)} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at W {p[1]:.3f} k {p[2]}" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


TOL = 0.15                                   # voxels: the z-fighting fix's stepped insets (0.015 each) on overlapping rings


def covered(boxes, p, seam=TOL):
    """Whether a point lies in one of the boxes (axis-aligned (lo, hi)), give or take `seam` (the
    z-fighting fix's insets leave hairline seams between a wall and its corner bar)."""
    for lo, hi in boxes:
        if all(lo[q] - seam <= p[q] <= hi[q] + seam for q in range(3)):
            return True
    return False


def tube_whole(els, half, length, wall):
    """Whether the elements make one continuous square tube, `half` from its axis to its outside, `wall`
    thick and `length` long: every sample point of the walls covered, none of the bore or the outside,
    all along it; and its outside and length within TOL. Returns a reason it is not, or None."""
    lo, hi = aabb_of(els)
    size = [hi[q] - lo[q] for q in range(3)]
    if max(abs(size[0] - 2 * half), abs(size[1] - 2 * half), abs(size[2] - length)) > TOL:
        return f"its box is {[round(x, 3) for x in size]}, not {2 * half:.2f} across and {length:.2f} long"
    cx, cy = (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2
    nz = max(2, int(length / 0.25))
    n = 16
    boxes = [e.aabb() for e in els]
    for kz in range(nz + 1):
        z = lo[2] + 0.05 + (length - 0.1) * kz / nz
        for i in range(n + 1):
            for j in range(n + 1):
                x = -half - 0.5 + (2 * half + 1) * i / n
                y = -half - 0.5 + (2 * half + 1) * j / n
                d = max(abs(x), abs(y))
                inside = covered(boxes, (cx + x, cy + y, z), seam=0.03)
                if half - wall + TOL < d < half - TOL and not inside:
                    return f"a gap in its wall at {x:.2f}, {y:.2f}, z {z:.2f}"
                if (d < half - wall - TOL or d > half + TOL) and inside:
                    return f"metal outside its wall at {x:.2f}, {y:.2f}, z {z:.2f}"
    return None


def ring_gaps(v, pre, rings, pose):
    """The largest gap along the axis between one ring and the next of `rings` (negative: overlapping)."""
    spans = sorted(aabb_of(v.group(rf"{pre}{i + 1}[udew]+", pose)) for i in rings)
    spans = sorted(((lo[2], hi[2]) for lo, hi in spans))
    reach, worst = spans[0][1], -1e9
    for z0, z1 in spans[1:]:
        worst = max(worst, z0 - reach)
        reach = max(reach, z1)
    return worst


def forged(m, W):
    """How far the forging has got at W, 0..1."""
    t0, t1 = m.T_FORGE
    return min(1.0, max(0.0, (W - t0) / (t1 - t0)))


def check_forging(v):
    """At every tenth of W the work is one continuous square tube of even cross-section along its whole
    length (the walls closing from 8 to 6 across and the length stretching from 8 to 16 together, no gap
    between rings larger than TOL), and, once parted, two such sections of 6 x 6 x 8; the closed tube's
    bore is the mandrel's; the parted sections lie a gap apart; at W 1 both are off the tip, the far one
    on the ground and the near one on it, about output.pos."""
    m = v.m
    near, far = list(m.NEAR), [i for i in range(m.N_RINGS) if i not in m.NEAR]
    worst_gap = -1e9
    for k, pre in ((1, "l"), (2, "c")):
        for t in range(11):
            W = t / 10
            pose = m.pose_at(k, W)
            e = forged(m, W)
            half = m.OUT - m.CLOSE * e
            groups = [range(m.N_RINGS)] if W <= m.T_PART[0] else [near, far]
            for g in groups:
                els = [x for i in g for x in v.group(rf"{pre}{i + 1}[udew]+", pose)]
                length = (m.L + m.L * e) * len(g) / m.N_RINGS
                why = tube_whole(els, half, length, m.WALL)
                if why:
                    v.fail(f"{pre} at W {W:.1f} is not one even tube ({len(g)} rings): {why}")
                gap = ring_gaps(v, pre, g, pose)
                worst_gap = max(worst_gap, gap)
                if gap > TOL:
                    v.fail(f"{pre} at W {W:.1f}: a gap of {gap:.3f} between rings")
        # closed: its bore is the mandrel's
        lo, hi = aabb_of(v.group(rf"{pre}\d+[udew]+", m.pose_at(k, m.T_FORGE[1])))
        want = (m.X0 - m.SEC, m.YM - m.SEC, m.X0 + m.SEC, m.YM + m.SEC)
        if max(abs(a - b) for a, b in zip((lo[0], lo[1], hi[0], hi[1]), want)) > TOL:
            v.fail(f"{pre} does not close onto the mandrel: {lo}, {hi}")
        # parted, and at W 1 off the tip in a pile about output.pos
        pose = m.pose_at(k, m.T_PART[1])
        n_box = aabb_of([x for i in near for x in v.group(rf"{pre}{i + 1}[udew]+", pose)])
        f_box = aabb_of([x for i in far for x in v.group(rf"{pre}{i + 1}[udew]+", pose)])
        if abs(f_box[0][2] - n_box[1][2] - m.PART_GAP) > TOL:
            v.fail("the sections are not parted")
        pose = m.pose_at(k, 1.0)
        n_box = aabb_of([x for i in near for x in v.group(rf"{pre}{i + 1}[udew]+", pose)])
        f_box = aabb_of([x for i in far for x in v.group(rf"{pre}{i + 1}[udew]+", pose)])
        if not (n_box[0][2] > m.TIP and f_box[0][2] > m.TIP):
            v.fail("a section is still on the mandrel at W 1")
        if abs(f_box[0][1]) > TOL or abs(n_box[0][1] - f_box[1][1]) > TOL:
            v.fail(f"the sections do not lie on the ground, one on the other: {n_box}, {f_box}")
        o = m.output_point()
        mid = [(n_box[0][q] + f_box[1][q]) / 2 if q == 1 else (n_box[0][q] + n_box[1][q]) / 2 for q in range(3)]
        if max(abs(mid[q] - o[q]) for q in range(3)) > TOL:
            v.fail(f"output.pos {o} is not the pile's middle {mid}")
    print(f"forging: at every tenth of W one even square tube (8 -> 6 across, 8 -> 16 long together; the largest gap "
          f"between rings {worst_gap:.3f}), then two 6 x 6 x 8 sections; closed onto the mandrel; parted; off the tip at "
          f"W 1 at output.pos")


def check_mandrel(v):
    """The mandrel's root lies in both bands of the bracket (held at two places against the cantilever), its
    collar clear of them, the hollow against the collar; theta moves nothing."""
    m = v.m
    frame = {e.name: e for e in v.by_part["frame"]}
    body = next(e for e in v.by_part["mandrel"] if e.name == "mandrel_body")
    blo, bhi = body.aabb()
    for i, (z0, z1) in enumerate(m.BANDS_Z, 1):
        parts = [frame[f"fr_band{i}_{s}"] for s in ("saddle", "w", "e", "cap")]
        lo, hi = aabb_of(parts)
        if not (lo[0] < blo[0] and hi[0] > bhi[0] and lo[1] < blo[1] and hi[1] > bhi[1] and blo[2] < z0 and bhi[2] > z1):
            v.fail(f"the mandrel is not through band {i}")
        for s, q, side in (("saddle", 1, 0), ("cap", 1, 1), ("w", 0, 0), ("e", 0, 1)):
            f = frame[f"fr_band{i}_{s}"].aabb()
            face = f[1 - side][q]
            if abs(face - (blo[q] if side == 0 else bhi[q])) > 1e-6:
                v.fail(f"band {i}'s {s} does not bear on the mandrel")
    for k in (1, 2):
        pre = "lc"[k - 1]
        lo, _ = aabb_of(v.group(rf"{pre}\d+[udew]+", m.pose_at(k, 0.0)))
        if abs(lo[2] - m.SHOULDER_Z[1]) > TOL:
            v.fail("the hollow is not against the shoulder")
    for th in (0.0, 1.3, -2.9, 40.0):
        for pid in (p["id"] for p in v.parts):
            a = v.mat(pid, (th, 0.3, 1, 1.0))
            b = v.mat(pid, (0.0, 0.3, 1, 1.0))
            if max(abs(a[i][j] - b[i][j]) for i in range(3) for j in range(4)) > 1e-12:
                v.fail(f"theta moves the {pid}")
    over = m.TIP - m.STUMP_Z[1]
    print(f"mandrel: through both bands, each bearing on it; the hollow against the shoulder; theta moves nothing; "
          f"{over:.1f} voxels past the stump")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = [e for pid in v.by_part if present(m, pid, pose[2]) for e in v.posed(pid, pose)]
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
    check_textures(v)
    check_containment(v, [m.REST] + cycle_poses(m, 0.01))
    check_forging(v)
    check_mandrel(v)
    check_clearances(v, [m.REST] + cycle_poses(m, 0.02), "clearances")
    if not quick:
        check_clearances(v, cycle_poses(m, 0.0025), "swept paths (every 0.0025 of the cycle, both metals: the hollow stretching, the sections off the tip and dropping)")
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
    if "powerCell" in ship or "powerFace" in ship:
        print("FAIL a hand station has no power cell")
        ok = False
    return ok
