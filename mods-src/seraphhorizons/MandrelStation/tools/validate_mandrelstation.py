"""The mandrel station generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved.
"""

from __future__ import annotations

import re

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, frame_floating, obb_obb
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


def band_offsets(v, k, pre, W):
    """For each ring of metal `pre`'s work at W, for each of its faces running along the axis: where on the
    work's sheet the face starts, less where its near edge stands from the work's near end (texture units,
    4 a voxel). Equal on every face of a ring (it is rigid); equal between two rings, the texture runs on
    from one to the next with no seam."""
    m, out = v.m, {}
    unit = m.TEX / 16
    far = unit * (m.band_z(m.N_RINGS - 1) + m.RING_L)
    for i in range(m.N_RINGS):
        for el in v.group(rf"{pre}{i + 1}[udew]+", m.pose_at(k, W)):
            lo = el.aabb()[0][2] - m.Z0
            for d, f in el.faces.items():
                uv = f["uv"]
                start = {"up": uv[1], "down": uv[1], "west": uv[0], "east": far - uv[2]}.get(d)
                if start is not None:
                    out.setdefault(i, []).append(start - unit * lo)
    return out


def check_banding(v):
    """The work's faces along its length are one sheet (`band` in make_shape.py): at W_BAND every ring's
    texture continues the one before it exactly. A ring slides rigidly, so elsewhere two rings' textures are
    off by how far their spacing has moved; at rest and at W 1 the largest offset between neighbouring rings
    is reported, and must stay under a voxel's worth (4 texture units)."""
    m, worst, own = v.m, {}, 0.0
    for k, pre in ((1, "l"), (2, "c")):
        for W in (0.0, m.W_BAND, 1.0):
            offs = band_offsets(v, k, pre, W)
            own = max([own] + [max(o) - min(o) for o in offs.values()])
            mean = [sum(offs[i]) / len(offs[i]) for i in range(m.N_RINGS)]
            worst[W] = max([worst.get(W, 0.0)] + [abs(mean[i + 1] - mean[i]) for i in range(m.N_RINGS - 1)])
    print(f"banding: one sheet along the work, offset between neighbouring rings {worst[m.W_BAND]:.3f} texture units "
          f"at W {m.W_BAND}, {worst[0.0]:.2f} at rest, {worst[1.0]:.2f} at W 1 (4 a voxel); within a ring {own:.3f}")
    if worst[m.W_BAND] > 0.01 or own > 0.01:
        v.fail(f"the work's rings are not one sheet at W {m.W_BAND}: offset {worst[m.W_BAND]:.3f}, within a ring {own:.3f}")
    if max(worst[0.0], worst[1.0]) >= m.TEX / 16:
        v.fail(f"the work's texture jumps by a voxel or more between rings at rest or at W 1: {worst}")


RING = re.compile(r"([lc])(\d+)[udew]+")


def allowed(m, pa, pb, pose):
    """Intended overlaps, all inside the work, whose union is the tube (the volumes inside each other are
    never seen): a ring's own walls and corner bars, which overlap as they close, and the rings, which
    overlap until the hollow is stretched."""
    ra, rb = RING.fullmatch(pa), RING.fullmatch(pb)
    return bool(ra and rb and ra.group(1) == rb.group(1))


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


def tube_whole(els, half, length, wall, joints=()):
    """Whether the elements make one continuous square tube, `half` from its axis to its outside, `wall`
    thick and `length` long: every sample point of the walls covered, none of the bore or the outside,
    all along it (but within 0.1 of a `joints` z, a part line allowed to show); and its outside and length
    within TOL. Returns a reason it is not, or None."""
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
        if any(abs(z - j) < 0.1 for j in joints):
            continue
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
    between rings larger than TOL); at W 1 it is closed onto the mandrel, still on it (its far end not past
    the tip), and its two sections (rings 1..4 and 5..8) are each 6 x 6 x 8, meeting end to end; output.pos
    is beyond the tip, where gameplay drops them."""
    m = v.m
    near, far = list(m.NEAR), [i for i in range(m.N_RINGS) if i not in m.NEAR]
    worst_gap = -1e9
    for k, pre in ((1, "l"), (2, "c")):
        for t in range(11):
            W = t / 10
            pose = m.pose_at(k, W)
            e = forged(m, W)
            els = v.group(rf"{pre}\d+[udew]+", pose)
            # the part line between the sections (rings 4 and 5) may show as the piece finishes
            joint = min(x.aabb()[0][2] for x in v.group(rf"{pre}{m.SECTION_RINGS + 1}[udew]+", pose))
            why = tube_whole(els, m.OUT - m.CLOSE * e, m.L + m.L * e, m.WALL, joints=(joint,))
            if why:
                v.fail(f"{pre} at W {W:.1f} is not one even tube: {why}")
            gap = ring_gaps(v, pre, range(m.N_RINGS), pose)
            worst_gap = max(worst_gap, gap)
            if gap > TOL:
                v.fail(f"{pre} at W {W:.1f}: a gap of {gap:.3f} between rings")
        pose = m.pose_at(k, 1.0)
        lo, hi = aabb_of(v.group(rf"{pre}\d+[udew]+", pose))
        want = (m.X0 - m.SEC, m.YM - m.SEC, m.X0 + m.SEC, m.YM + m.SEC)
        if max(abs(a - b) for a, b in zip((lo[0], lo[1], hi[0], hi[1]), want)) > TOL:
            v.fail(f"{pre} does not close onto the mandrel: {lo}, {hi}")
        if hi[2] > m.TIP + 1e-6:
            v.fail(f"{pre}'s tube runs off the tip at W 1 ({hi[2]:.3f} past {m.TIP})")
        for name, g in (("near", near), ("far", far)):
            els = [x for i in g for x in v.group(rf"{pre}{i + 1}[udew]+", pose)]
            why = tube_whole(els, m.SEC, m.L, m.WALL)
            if why:
                v.fail(f"{pre}'s {name} section at W 1 is not 6 x 6 x 8: {why}")
        n_hi = aabb_of([x for i in near for x in v.group(rf"{pre}{i + 1}[udew]+", pose)])[1][2]
        f_lo = aabb_of([x for i in far for x in v.group(rf"{pre}{i + 1}[udew]+", pose)])[0][2]
        if abs(f_lo - n_hi) > TOL:
            v.fail(f"{pre}'s sections do not meet end to end at W 1 ({n_hi:.3f}, {f_lo:.3f})")
    if not m.output_point()[2] > m.TIP:
        v.fail("output.pos is not beyond the tip")
    print(f"forging: at every tenth of W one even square tube (8 -> 6 across, 8 -> 16 long together, over the whole "
          f"work; the largest gap between rings {worst_gap:.3f}); at W 1 closed onto the mandrel and still on it, two "
          f"6 x 6 x 8 sections end to end; output.pos beyond the tip")


def check_hang(v):
    """The hollow hangs on the mandrel, never centred round it: at every tenth of W the bore's ceiling (the
    top walls' undersides) bears on the bar's top face (the lowest within 0.01, every ring's within the
    z-fighting fix's steps), and no element of the work passes into the bar by more than 0.01."""
    m = v.m
    bar = next(e for e in v.by_part["mandrel"] if e.name == "mandrel_body").aabb()
    top = m.YM + m.MH
    worst_low, worst_ring, worst_in = 0.0, 0.0, 0.0
    for k, pre in ((1, "l"), (2, "c")):
        for t in range(11):
            pose = m.pose_at(k, t / 10)
            ceil = [aabb_of(v.group(rf"{pre}{i + 1}u", pose))[0][1] for i in range(m.N_RINGS)]
            low = min(ceil) - top
            worst_low = max(worst_low, abs(low))
            worst_ring = max(worst_ring, max(c - top for c in ceil))
            if abs(low) > 0.01:
                v.fail(f"{pre} at W {t / 10:.1f}: the bore's ceiling is {low:+.3f} off the bar's top")
            if max(c - top for c in ceil) > TOL or min(ceil) < top - 0.01:
                v.fail(f"{pre} at W {t / 10:.1f}: a ring's ceiling is off the bar ({[round(c - top, 3) for c in ceil]})")
            for e in v.group(rf"{pre}\d+[udew]+", pose):
                lo, hi = e.aabb()
                depth = min(min(hi[q], bar[1][q]) - max(lo[q], bar[0][q]) for q in range(3))
                worst_in = max(worst_in, depth)
                if depth > 0.01:
                    v.fail(f"{e.name} passes {depth:.3f} into the bar at W {t / 10:.1f}")
    print(f"hang: the bore's ceiling on the bar's top at every tenth of W (lowest within {worst_low:.4f}, every ring within "
          f"{worst_ring:.3f}); nothing passes into the bar (worst {worst_in:+.3f})")


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
    check_banding(v)
    check_containment(v, [m.REST] + cycle_poses(m, 0.01))
    check_forging(v)
    check_mandrel(v)
    check_hang(v)
    check_clearances(v, [m.REST] + cycle_poses(m, 0.02), "clearances")
    if not quick:
        check_clearances(v, cycle_poses(m, 0.0025), "swept paths (every 0.0025 of the cycle, both metals: the hollow stretching along the mandrel)")
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
    hollow = sum(1 for c in ship["cells"] if c.get("hollow"))
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, {hollow} hollow; "
          f"lids: {lids or 'none (a hand station is not walked on)'}")
    if lids or hollow:
        ok = False
    if "powerCell" in ship or "powerFace" in ship:
        print("FAIL a hand station has no power cell")
        ok = False
    return ok
