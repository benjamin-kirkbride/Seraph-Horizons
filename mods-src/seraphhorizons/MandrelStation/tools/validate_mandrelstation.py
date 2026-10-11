"""The mandrel station generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved.
"""

from __future__ import annotations

import math
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


def present(m, pid, pose):
    return m.on_show(pid, pose)


def cycle_poses(m, step=0.005):
    n = int(round(1 / step))
    return [m.pose_at(k, i * step) for k in (1, 2) for i in range(n + 1)]


def spring_poses(m, step=0.005):
    n = int(round(1 / step))
    return [m.spring_at(i * step) for i in range(n + 1)]


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
            if not present(m, pid, pose):
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
    (r"^sp", {"spring"}),
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
    overlap until the hollow is stretched; and the spring's pieces, one wire, whose joints and the pieces
    out of sight check_spring holds to account instead."""
    if m.is_spring(pa) and m.is_spring(pb):
        return True
    ra, rb = RING.fullmatch(pa), RING.fullmatch(pb)
    return bool(ra and rb and ra.group(1) == rb.group(1))


def touching_pairs(v, pose):
    items = []
    for pid in v.by_part:
        if v.m.seen(pid, pose):
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


# the rig's windows, pivots and angles are rounded to 6 places (blocks, radians): a wound side lies up to 1e-5 off where
# the coil puts it, and a joint between two pieces as far apart
AX_TOL = 1e-4
JOIN_TOL = 2e-3                              # a side's start on the end of the side before it
CORNER = 0.45                                # a side's corners: half a wire's diagonal, and a little
SKIN = 0.035                                 # an instant may change a surface by this much (pieces' sizes step 0.0075 a face)


def axis_of(el):
    """An element's long local axis (its local z) in world space."""
    return [el.r[i][2] for i in range(3)]


def dist(a, b):
    return max(abs(a[i] - b[i]) for i in range(3))


def inside(el, box, tol=1e-6):
    """Whether every corner of `el` lies inside the (rotated) box `box`."""
    for q in el.corners():
        rel = [q[i] - box.c[i] for i in range(3)]
        for k in range(3):
            if abs(sum(box.r[i][k] * rel[i] for i in range(3))) > abs(box.size[k]) / 2 + tol:
                return False
    return True


def point_in(p, box, tol=1e-6):
    rel = [p[i] - box.c[i] for i in range(3)]
    return all(abs(sum(box.r[i][k] * rel[i] for i in range(3))) <= abs(box.size[k]) / 2 + tol for k in range(3))


def seg_dist(p0, p1, q0, q1):
    """The least distance between the segments p0..p1 and q0..q1 (sampled finely: the pieces are short)."""
    best, n = 1e9, 60
    for i in range(n + 1):
        f = i / n
        p = [p0[k] + (p1[k] - p0[k]) * f for k in range(3)]
        d = [q1[k] - q0[k] for k in range(3)]
        dd = sum(x * x for x in d)
        t = max(0.0, min(1.0, sum((p[k] - q0[k]) * d[k] for k in range(3)) / dd)) if dd else 0.0
        best = min(best, math.sqrt(sum((p[k] - q0[k] - t * d[k]) ** 2 for k in range(3))))
    return best


class Rod:
    """The rod's pieces posed at W: each one's element, its centre line's start and end, its bar's axis from end to end
    (the centre line and the overhang past either end), and whether it can be seen."""

    def __init__(self, v, W):
        m = v.m
        self.W, self.pose = W, m.spring_at(W)
        self.pcs = m.plan()
        self.el, self.a, self.b, self.axis, self.seen = {}, {}, {}, {}, {}
        for pc in self.pcs:
            el = v.posed(pc.pid, self.pose)[0]
            ax = axis_of(el)
            mid = (pc.lo + pc.hi) / 2
            a = [el.c[i] - ax[i] * mid for i in range(3)]
            self.el[pc.pid], self.a[pc.pid], self.b[pc.pid] = el, a, [a[i] + ax[i] * pc.run for i in range(3)]
            self.axis[pc.pid] = ([a[i] + ax[i] * pc.lo for i in range(3)], [a[i] + ax[i] * pc.hi for i in range(3)])
            self.seen[pc.pid] = m.seen(pc.pid, self.pose)

    def shown(self):
        return [pc for pc in self.pcs if self.seen[pc.pid]]

    def joined(self, pa, pb):
        """Two pieces are one wire where they meet: a side starts at the end of the side before it (within JOIN_TOL); the
        straight rod's bar and a side's bar cross or touch end to side (their axes within half a wire of each other)."""
        if pa.kind == pb.kind == "side":
            if abs(pa.j - pb.j) != 1:
                return False
            first, then = (pa, pb) if pa.j < pb.j else (pb, pa)
            return dist(self.b[first.pid], self.a[then.pid]) <= JOIN_TOL
        return seg_dist(*self.axis[pa.pid], *self.axis[pb.pid]) <= self.m_wire / 2 + 1e-4

    def links(self, wire):
        self.m_wire = wire
        shown = self.shown()
        out = {pc.pid: set() for pc in shown}
        for x, pa in enumerate(shown):
            for pb in shown[x + 1:]:
                if self.joined(pa, pb):
                    out[pa.pid].add(pb.pid)
                    out[pb.pid].add(pa.pid)
        return out

    def skin(self, step=0.1):
        """Points on the faces of every seen piece (a grid `step` apart, and each face's corners)."""
        pts = []
        for pc in self.shown():
            el = self.el[pc.pid]
            h = [abs(s) / 2 for s in el.size]
            for k in range(3):
                for sign in (-1, 1):
                    u, w = [q for q in range(3) if q != k]
                    nu, nw = max(1, int(2 * h[u] / step)), max(1, int(2 * h[w] / step))
                    for i in range(nu + 1):
                        for j in range(nw + 1):
                            loc = [0.0, 0.0, 0.0]
                            loc[k], loc[u], loc[w] = sign * h[k], -h[u] + 2 * h[u] * i / nu, -h[w] + 2 * h[w] * j / nw
                            pts.append([el.c[q] + sum(el.r[q][x] * loc[x] for x in range(3)) for q in range(3)])
        return pts

    def holds(self, p, tol):
        return any(point_in(p, self.el[pc.pid], tol) for pc in self.shown())

    def side_hidden(self, pc):
        """How much of a side's run, away from its corners, lies inside a bigger piece that can be seen."""
        a, b = self.a[pc.pid], self.b[pc.pid]
        lo, hi = CORNER / pc.run, 1.0 - CORNER / pc.run
        n, hid = 40, 0
        for i in range(n + 1):
            f = lo + (hi - lo) * i / n
            p = [a[q] + (b[q] - a[q]) * f for q in range(3)]
            for o in self.shown():
                if o.pid != pc.pid and o.across > pc.across + 1e-6 and point_in(p, self.el[o.pid], 1e-4):
                    hid += 1
                    break
        return hid / (n + 1) * (hi - lo) * pc.run


def on_layer(m, p, tol=2e-3):
    """Whether a point lies on the coil's centre-line square (the layer of wire lying on the mandrel's faces): on one of
    its four sides, give or take tol."""
    x, y = p[0] - m.X0, p[1] - m.YM
    h = m.COIL.h
    return max(abs(x), abs(y)) <= h + tol and min(h - abs(x), h - abs(y)) <= tol


CUT = 0.02                                   # a piece may stand into another, away from their joint, by this much at most
JOINT_R = 1.35                               # the rod's end comes down on the side's end: their overlap, along the side, lies
                                             # within this of the side's far corner


def cut_into(side_el, rod_el, lo, hi, step=0.1):
    """Where the side's skin stands inside the rod: (how far along the side's bar from its start end, how deep) for each
    sample point inside it. `lo`..`hi` is the bar's local z about its centre line's start."""
    h = [abs(s) / 2 for s in side_el.size]
    out = []
    for k in range(3):
        for sign in (-1, 1):
            u, w = [q for q in range(3) if q != k]
            nu, nw = max(1, int(2 * h[u] / step)), max(1, int(2 * h[w] / step))
            for i in range(nu + 1):
                for j in range(nw + 1):
                    loc = [0.0, 0.0, 0.0]
                    loc[k], loc[u], loc[w] = sign * h[k], -h[u] + 2 * h[u] * i / nu, -h[w] + 2 * h[w] * j / nw
                    p = [side_el.c[q] + sum(side_el.r[q][x] * loc[x] for x in range(3)) for q in range(3)]
                    rel = [p[q] - rod_el.c[q] for q in range(3)]
                    d = min(abs(rod_el.size[x]) / 2 - abs(sum(rod_el.r[q][x] * rel[q] for q in range(3))) for x in range(3))
                    if d > 0:
                        out.append((loc[2] + (lo + hi) / 2 - lo, d))
    return out


def check_spring(v):
    """The spring job, held to the plan's promises. Wound (W 1): every side where machinegen.coil puts it, on the
    mandrel's faces; every stretch of the straight rod back in the mandrel. At W 0: the straight rod alone, along the
    mandrel's top east edge from the shoulder to past the nose. Every 1/1000 of W: what cannot be seen is inside the
    mandrel's body or inside a piece that can; one stretch of the straight rod, along the mandrel, its far end where it
    was at W 0, on its slot's hop (the arc over the face from corner to corner); the coil one wire (each side on the end
    of the one before), the rod on the newest side's corners at its slot's ends; the rod and the newest side cut into
    each other nowhere but where they are one (the side inside the rod as it lifts off, in the slot's first BEND stage,
    and the rod's end on the side's end as it lands, within JOINT_R of the side's far corner); no other side covered
    away from its corners; no two pieces overlapping that are not neighbours on the wire. Over each step of the plan: an
    instant changes nothing that can be seen; a slot turns one side in its face's layer and carries the straight rod
    along its arc, and moves nothing else."""
    m = v.m
    C, n = m.COIL, m.COIL.sides
    pcs = m.plan()
    sides = [pc for pc in pcs if pc.kind == "side"]
    rods = [pc for pc in pcs if pc.kind == "rod"]
    body = next(el for el in v.by_part["mandrel"] if el.name == "mandrel_body")

    # wound
    end = Rod(v, 1.0)
    worst = 0.0
    for pc in sides:
        want = C.side(pc.j, "x", "x", "spring", across=m.spring_across(pc.j))
        got = end.el[pc.pid]
        worst = max(worst, max(min(dist(p, q) for q in got.corners()) for p in want.corners()))
        k, sign = (1, 1) if C.face(pc.j) == "u" else (1, -1) if C.face(pc.j) == "d" else (0, -1) if C.face(pc.j) == "w" else (0, 1)
        inner = sign * (got.c[k] - (m.X0, m.YM)[k]) - pc.across / 2
        if abs(inner - m.MH) > m.STEP + AX_TOL:
            v.fail(f"{pc.pid} is not on the mandrel's face when wound ({inner:.4f} from the axis)")
        lo, hi = got.aabb()
        if lo[2] < m.SHOULDER_Z[1] - 1e-6 or hi[2] > m.NOSE_Z[0] + 1e-6:
            v.fail(f"{pc.pid} is not between the shoulder and the nose when wound")
    if worst > AX_TOL:
        v.fail(f"a wound side is not where the coil puts it ({worst:.1e})")
    left = [pc.pid for pc in rods if end.seen[pc.pid] or not inside(end.el[pc.pid], body)]
    if left:
        v.fail(f"stretches of the rod left out when the spring is wound: {left[:5]}")
    print(f"spring wound: {n} sides, {C.turns} turns of {m.WIRE} wire at pitch {C.pitch}, each where the coil puts it (within "
          f"{worst:.1e}); the {len(rods)} stretches of the straight rod back in the mandrel")

    # W 0
    start = Rod(v, 0.0)
    first = [pc.pid for pc in start.shown()]
    r0 = start.el["spr0"]
    lo, hi = r0.aabb()
    along = abs(axis_of(r0)[2] - 1.0) < 1e-9 and dist(start.a["spr0"], C.corner(0)) < AX_TOL
    print(f"spring at W 0: {', '.join(first)}, along the mandrel's top east edge from z {lo[2]:.2f} (the shoulder at "
          f"{m.SHOULDER_Z[1]}) to {hi[2]:.2f} (the tip at {m.TIP}), {hi[2] - lo[2]:.2f} long: {along}")
    if first != ["spr0"] or not along or lo[2] < m.SHOULDER_Z[1] or lo[2] > m.SHOULDER_Z[1] + 0.2:
        v.fail("at W 0 the rod is not the straight rod alone, along the mandrel's top east edge from the shoulder")

    # every 1/1000 of W
    samples = 1000
    hidden_bad, broken, covered, clash, off, moved, cuts = [], [], [], set(), [], [], []
    worst_cover, worst_far, worst_arc, worst_cut, worst_joint = 0.0, 0.0, 0.0, 0.0, 0.0
    slots = [(a, b) for kind, a, b in m.WINDOWS if kind == "slot"]
    reveal = m.BEND[0][0]
    for i in range(samples + 1):
        W = i / samples
        rod = Rod(v, W)
        shown = rod.shown()
        u = W * m.US
        for pc in pcs:
            if rod.seen[pc.pid]:
                continue
            el = rod.el[pc.pid]
            if not (inside(el, body, 1e-4) or any(inside(el, rod.el[o.pid], 1e-4) for o in shown)):
                hidden_bad.append((pc.pid, round(W, 4)))
        straight = [pc for pc in shown if pc.kind == "rod"]
        seen_sides = [pc for pc in shown if pc.kind == "side"]
        newest = max(seen_sides, key=lambda pc: pc.j) if seen_sides else None
        # one stretch at a time, until the last side, the rod's last length, is bent off it
        if len(straight) != (0 if rod.seen[sides[-1].pid] else 1):
            off.append((round(W, 4), "stretches seen", [pc.pid for pc in straight]))
        # the slot this W is in (its instants count with it), and how far through its hop
        j = min(int(W * n), n - 1)
        a, b = slots[j]
        f = min(1.0, max(0.0, (u - a) / (b - a)))
        for pc in straight:
            el = rod.el[pc.pid]
            if abs(axis_of(el)[2] - 1.0) > 1e-9:
                off.append((round(W, 4), pc.pid, "not along the mandrel"))
            far = abs(rod.b[pc.pid][2] - m.ROD_END)
            worst_far = max(worst_far, far)
            if far > AX_TOL:
                moved.append((pc.pid, round(W, 4), round(far, 4)))
            # on its slot's hop over face j, from corner j to corner j + 1, as far along it as the slot has got
            p = rod.a[pc.pid]
            if j + 1 >= n:
                off.append((round(W, 4), pc.pid, "a stretch left after the last side"))
                continue
            d, along = m.hop_off(j, p)
            worst_arc = max(worst_arc, d)
            if d > 2e-3 or abs(along - f) > 0.02:
                off.append((round(W, 4), pc.pid, f"off its hop by {d:.3f}, {along:.3f} along it at {f:.3f} of the slot"))
            # on the newest side's corners at the slot's ends: its start as it lifts off, its end as it lands
            if newest is not None and (u <= a + 1 or u >= b - 1):
                at = m.corner(j) if u <= a + 1 else m.corner(j + 1)
                joint = max(abs(p[k] - at[k]) for k in (0, 1))
                worst_joint = max(worst_joint, joint)
                if joint > AX_TOL:
                    broken.append((round(W, 4), f"{pc.pid} not on {newest.pid}'s corner ({joint:.4f})"))
        links = rod.links(m.WIRE)
        reached = {"sp1u"} if seen_sides else set()
        todo = list(reached)
        while todo:
            for q in links[todo.pop()]:
                if q not in reached:
                    reached.add(q)
                    todo.append(q)
        lost = [pc.pid for pc in seen_sides if pc.pid not in reached]
        if lost:
            broken.append((round(W, 4), lost[:3]))
        # the rod and the side being laid cut into each other only where they are one: the side inside the rod as the rod
        # lifts off it (the first BEND stage), and the rod's end coming down on the side's end; and the rod lifting off
        # the side before, whose end it came down on, only at that end
        joined_to = []
        if straight and newest is not None:
            for pc in sides:
                if pc.j in (j - 1, j) and rod.seen[pc.pid]:
                    joined_to.append(pc)
                    if pc.j == j and f <= reveal + 1e-9:
                        continue
                    away = [(along, d) for along, d in cut_into(rod.el[pc.pid], rod.el[straight[0].pid], pc.lo, pc.hi)
                            if along < pc.run + m.WIRE / 2 - JOINT_R]
                    if away:
                        deep = max(d for _, d in away)
                        worst_cut = max(worst_cut, deep)
                        if deep > CUT:
                            near, far_ = min(a for a, _ in away), max(a for a, _ in away)
                            cuts.append((pc.pid, round(W, 4), round(f, 3), round(near, 2), round(far_, 2), round(deep, 3)))
        for pc in seen_sides:
            if pc is newest and pc.j == j:
                continue
            h = rod.side_hidden(pc)
            worst_cover = max(worst_cover, h)
            if h > 0.05:
                covered.append((pc.pid, round(W, 4), round(h, 3)))
        near_ = {}
        for pid in links:
            two = set(links[pid])
            for q in links[pid]:
                two |= links[q]
            near_[pid] = two
        for x, pa in enumerate(shown):
            for pb in shown[x + 1:]:
                if (pa.kind == "rod" and pb in joined_to) or (pb.kind == "rod" and pa in joined_to):
                    continue                 # the two sides the rod is one with at its slot's ends: held to the cut rule
                if pb.pid not in near_[pa.pid] and obb_obb(rod.el[pa.pid], rod.el[pb.pid]):
                    clash.add((pa.pid, pb.pid, round(W, 4)))
    print(f"spring winding: every 1/{samples} of W one stretch of the straight rod, along the mandrel, its far end fixed (within "
          f"{worst_far:.1e}), on its slot's hop (within {worst_arc:.1e} of its path) and on the newest side's corners at the "
          f"slot's ends (within {worst_joint:.1e}); the coil one wire; every piece out of sight inside the mandrel or a piece "
          f"that can be seen; the rod and the side being laid stand into each other away from their joint by {worst_cut:.3f} at most, past the side's "
          f"first stage (their joint: the side's last {JOINT_R}); other sides covered away from their corners at most "
          f"{worst_cover:.3f}; no two pieces overlap but neighbours")
    if hidden_bad:
        v.fail(f"pieces neither seen nor hidden: {hidden_bad[:5]}")
    if off:
        v.fail(f"the straight rod is not one stretch along the mandrel on its hop: {off[:5]}")
    if moved:
        v.fail(f"the straight rod's far end moves: {moved[:5]}")
    if broken:
        v.fail(f"the rod comes apart: {broken[:5]}")
    if cuts:
        v.fail(f"the rod cuts into the side being laid away from their joint (side, W, f, from, to, depth): {cuts[:5]}")
    if covered:
        v.fail(f"a side is covered: {covered[:5]}")
    if clash:
        v.fail(f"pieces of the rod not joined overlap: {sorted(clash)[:5]}")

    # over each step: an instant changes nothing seen; a slot turns its side in its layer, by BEND's profile, and carries
    # the straight rod along its arc, lifted off the layer between the corners, and moves nothing else
    pops, wrong = [], []
    lengths, lifts = [], []
    j = -1
    for kind, a, b in m.WINDOWS:
        ra, rb = Rod(v, a / m.US), Rod(v, b / m.US)
        if kind == "instant":
            out = [p for p in ra.skin() if not rb.holds(p, SKIN)] + [p for p in rb.skin() if not ra.holds(p, SKIN)]
            if out:
                pops.append((round(a / m.US, 6), len(out), [round(x, 2) for x in out[0]]))
            continue
        j += 1
        side, nxt = sides[j], rods[j + 1] if j + 1 < n else None
        stages = [Rod(v, (a + f * (b - a)) / m.US) for f, _g in m.BEND]
        mid = Rod(v, (a + b) / 2 / m.US)
        movers = [pc.pid for pc in pcs
                  if max(dist(r.el[pc.pid].c, ra.el[pc.pid].c) for r in stages + [mid, rb]) > 1e-6
                  or max(abs(ra.el[pc.pid].r[p][q] - rb.el[pc.pid].r[p][q]) for p in range(3) for q in range(3)) > 1e-9]
        want = sorted([side.pid] + ([nxt.pid] if nxt else []))
        if sorted(movers) != want:
            wrong.append((kind, round(a / m.US, 4), f"moves {movers}, not {want}"))
            continue
        axis0 = axis_of(ra.el[side.pid])
        for r, (_f, g) in zip([ra] + stages, ((0.0, 0.0),) + m.BEND):
            p = side.pid
            if not (on_layer(m, r.a[p]) and on_layer(m, r.b[p]) and on_layer(m, [(r.a[p][q] + r.b[p][q]) / 2 for q in range(3)])):
                wrong.append((kind, round(a / m.US, 4), f"{p} leaves its face's layer"))
                break
            turned = math.acos(max(-1.0, min(1.0, sum(axis0[q] * axis_of(r.el[p])[q] for q in range(3)))))
            if abs(turned - g * abs(C.swing(j)[1])) > 1e-3:
                wrong.append((kind, round(a / m.US, 4), f"{p} has turned {turned:.4f}, not {g * abs(C.swing(j)[1]):.4f}"))
                break
        if nxt:
            p = nxt.pid
            if dist(ra.a[p][:2] + [0.0], m.corner(j)[:2] + [0.0]) > AX_TOL or dist(rb.a[p][:2] + [0.0], m.corner(j + 1)[:2] + [0.0]) > AX_TOL:
                wrong.append((kind, round(a / m.US, 4), f"{p} does not hop from corner {j} to corner {j + 1}"))
            lift = math.hypot(mid.a[p][0] - m.X0, mid.a[p][1] - m.YM) - C.h
            lifts.append(lift)
            if lift < m.WIRE / 2 + m.ROD / 2 + 0.05:
                wrong.append((kind, round(a / m.US, 4), f"{p} is not up over the face mid-hop (lifted {lift:.3f})"))
            if max(abs(ra.el[p].r[x][y] - r2.el[p].r[x][y]) for r2 in stages + [mid] for x in range(3) for y in range(3)) > 1e-9:
                wrong.append((kind, round(a / m.US, 4), f"{p} turns as it hops"))
            lengths.append(ra.el[p].size[2])
    print(f"spring steps: {len(m.WINDOWS)} ({sum(1 for w in m.WINDOWS if w[0] == 'instant')} instants, "
          f"{sum(1 for w in m.WINDOWS if w[0] == 'slot')} slots: a side turned by BEND's profile, in its layer, and the rod hopped "
          f"corner to corner, lifted {min(lifts):.2f} .. {max(lifts):.2f} over the layer mid-hop, never turning); the straight rod "
          f"{lengths[0]:.2f} long at the first hop, {lengths[-1]:.2f} at the last; instants change nothing seen")
    if pops:
        v.fail(f"an instant changes the rod that can be seen: {pops[:4]}")
    if wrong:
        v.fail(f"a step moves more or other than it should: {wrong[:4]}")
    if any(b > a + 1e-9 for a, b in zip(lengths, lengths[1:])):
        v.fail("the straight rod gets longer")

    # the class is the job: the spring moves for its class alone, the hollows never for it
    hollows = [p["id"] for p in v.parts if p["requires"] in ("hollowlead", "hollowcopper")]
    spring = [pc.pid for pc in pcs]
    worst = 0.0
    for i in range(0, 41, 3):
        W = i / 40
        for k, pids in ((0, spring), (1, spring), (2, spring), (m.SPRING_CLASS, hollows)):
            for pid in pids:
                a = v.mat(pid, m.spring_at(W, k=k))
                worst = max(worst, max(abs(a[r][c] - (1.0 if r == c else 0.0)) for r in range(3) for c in range(4)))
    if worst > 1e-12:
        v.fail(f"a job's parts move with another job's class ({worst:.1e})")


def check_item(m, item):
    """The spring item: the wound coil inside the 16-voxel item box at scale 1, centred on its floor, every element with
    a textured face."""
    ok = True
    lo, hi = aabb_of(item)
    print(f"item spring: {len(item)} elements, {' x '.join(f'{hi[i] - lo[i]:.2f}' for i in range(3))}, from {[round(x, 3) for x in lo]}")
    if min(lo) < -1e-6 or max(hi) > 16 + 1e-6 or abs(lo[1]) > 1e-6 or abs(lo[0] + hi[0] - 16) > 1e-6 or abs(lo[2] + hi[2] - 16) > 1e-6:
        print(f"FAIL the spring item is not centred on the floor of the item box: {lo} .. {hi}")
        ok = False
    if len(item) != m.COIL.sides or any(not any(f.get("texture") for f in el.faces.values()) for el in item):
        print("FAIL the spring item is not the coil's sides, each textured")
        ok = False
    return ok


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        # what can be seen: the spring's pieces out of sight are not
        els = [e for pid in v.by_part if m.seen(pid, pose) for e in v.posed(pid, pose)]
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
    check_containment(v, [m.REST] + cycle_poses(m, 0.01) + spring_poses(m, 0.0025))
    check_forging(v)
    check_mandrel(v)
    check_hang(v)
    check_spring(v)
    check_clearances(v, [m.REST] + cycle_poses(m, 0.02) + spring_poses(m, 0.01), "clearances")
    if not quick:
        check_clearances(v, cycle_poses(m, 0.0025), "swept paths (every 0.0025 of the cycle, both metals: the hollow stretching along the mandrel)")
        check_clearances(v, spring_poses(m, 0.001), "swept paths (every 0.001 of the winding: the rod round the mandrel, the coil growing)")
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
